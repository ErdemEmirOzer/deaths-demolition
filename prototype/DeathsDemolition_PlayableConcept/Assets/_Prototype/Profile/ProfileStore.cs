using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DD.Prototype.Profile
{
    /// <summary>The steps of a save, in order. A fault hook (tests) can throw at any of them.</summary>
    public enum ProfileSaveStage
    {
        None = 0,

        /// <summary>The save was refused before touching the disk (locked, invalid input).</summary>
        Refused = 1,

        Serialize = 2,
        BeforeCandidateWrite = 3,

        /// <summary>Half of the candidate bytes are on disk. A fault here leaves a truncated candidate.</summary>
        CandidateWriteMid = 4,

        Flush = 5,
        CandidateReadBack = 6,
        BackupCopy = 7,
        BeforeReplace = 8,
        Replace = 9,
        PostReplaceVerify = 10
    }

    /// <summary>How the last load ended up with the profile it returned.</summary>
    public enum ProfileLoadRecovery
    {
        /// <summary>The primary file was read as-is (or there was none and a fresh profile started).</summary>
        None = 0,

        /// <summary>The primary was missing and a validated candidate from an interrupted replace was adopted.</summary>
        RolledForwardCandidate = 1,

        /// <summary>The primary was missing or unreadable and the validated last-known-good backup was adopted.</summary>
        RestoredBackup = 2,

        /// <summary>The primary was unreadable, no backup validated: a fresh profile; the bytes were kept aside.</summary>
        FreshAfterCorruption = 3,

        /// <summary>The primary is from a newer build: left untouched, profile is read-only for this session.</summary>
        ReadOnlyFutureVersion = 4
    }

    /// <summary>What a save did. Success means the primary file now holds exactly what was asked.</summary>
    public readonly struct ProfileSaveResult
    {
        public readonly bool Success;
        public readonly ProfileSaveStage FailedStage;
        public readonly string Message;

        public ProfileSaveResult(bool success, ProfileSaveStage failedStage, string message)
        {
            Success = success;
            FailedStage = failedStage;
            Message = message ?? string.Empty;
        }

        public static ProfileSaveResult Ok => new ProfileSaveResult(true, ProfileSaveStage.None, string.Empty);
    }

    /// <summary>
    /// Reads and writes the local profile file. Offline, no service, no cloud.
    ///
    /// FILES (all in the same directory as the primary)
    ///   prototype-profile.json                the primary; the only file anything reads in play
    ///   prototype-profile.json.writing        the candidate of a save in progress
    ///   prototype-profile.json.bak            verified copy of the primary taken before each replace
    ///   prototype-profile.json.bak.writing    transient candidate used to refresh the backup atomically
    ///   prototype-profile.json.corrupt.N.json unreadable primaries, kept for inspection, never deleted
    ///   prototype-profile.json.lock           write ownership of this profile (pid + process name)
    ///
    /// SAVE CHAIN (WP-009) - each numbered step is a <see cref="ProfileSaveStage"/> the
    /// fault-injection tests can fail:
    ///   1 serialize, re-parse and validate the text in memory;
    ///   2 write the candidate to .writing (a FileStream, flushed with fsync);
    ///   3 read .writing back and require it to equal the text written;
    ///   4 copy the current primary to .bak (only if the primary itself validates);
    ///   5 rename .writing over the primary - one rename(2) via File.Replace, so the
    ///     primary is never absent and never half-written;
    ///   6 read the primary back and require it to equal the text; on a mismatch the
    ///     backup is restored and the save reports failure.
    ///
    /// The claim this buys, and the one the tests prove: after a process death at any
    /// point, the next load finds EITHER the complete old profile OR the complete new
    /// one - never a truncated, empty or zeroed file taken as truth.
    ///
    /// LOAD / RECOVERY (deterministic)
    ///   primary valid              → use it; delete an orphan .writing (never authoritative)
    ///   primary missing            → adopt a validated .writing (interrupted step 5, roll
    ///                                forward) else a validated .bak (roll back) else fresh
    ///   primary unreadable         → keep its bytes as .corrupt.N.json; delete .writing
    ///                                (unverified); adopt a validated .bak else fresh
    ///   primary from a newer build → leave it exactly as it is; fresh in memory; saves locked
    ///
    /// FAILURE POLICY (acceptance criterion 27): every failure logs at WARNING level,
    /// never throws out of here, and returns a usable profile / an honest result.
    /// </summary>
    public static class ProfileStore
    {
        /// <summary>PROTOTYPE-ONLY file name. One local profile, no slots.</summary>
        public const string FileName = "prototype-profile.json";

        public const string CandidateSuffix = ".writing";
        public const string BackupSuffix = ".bak";
        public const string CorruptSuffix = ".corrupt";
        public const string LockSuffix = ".lock";

        private static string s_pathOverride;
        private static FileStream s_lease;
        private static string s_leasePath;

        /// <summary>Absolute path of the save file.</summary>
        public static string Path
        {
            get
            {
                if (!string.IsNullOrEmpty(s_pathOverride))
                {
                    return s_pathOverride;
                }

                return System.IO.Path.Combine(Application.persistentDataPath, FileName);
            }
        }

        public static string CandidatePath => Path + CandidateSuffix;
        public static string BackupPath => Path + BackupSuffix;
        public static string BackupCandidatePath => BackupPath + CandidateSuffix;
        public static string LockPath => Path + LockSuffix;

        // ------------------------------------------------------------ diagnostics

        /// <summary>True when the last <see cref="Load"/> could not use the primary as written.</summary>
        public static bool LastLoadFellBack { get; private set; }

        /// <summary>Why, in English. Shown on the profile pane.</summary>
        public static string LastLoadMessage { get; private set; } = string.Empty;

        public static ProfileLoadStatus LastLoadStatus { get; private set; } = ProfileLoadStatus.Missing;
        public static ProfileLoadRecovery LastLoadRecovery { get; private set; } = ProfileLoadRecovery.None;

        /// <summary>
        /// True while this process must not write the profile: the file is from a newer
        /// build, or another live instance of the game owns it. Every save is refused
        /// with <see cref="ProfileSaveStage.Refused"/> and <see cref="SaveLockReason"/>.
        /// </summary>
        public static bool SaveLocked { get; private set; }

        public static string SaveLockReason { get; private set; } = string.Empty;

        /// <summary>The last save's result, for the F1 panel and tests.</summary>
        public static ProfileSaveResult LastSaveResult { get; private set; } = ProfileSaveResult.Ok;

        // -------------------------------------------------------------- test seams
        // Editor and development builds only: a release Player carries neither the
        // delegates nor the calls (the release dll is scanned for the names).

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>
        /// Fault injection (tests and probes): invoked at every <see cref="ProfileSaveStage"/>;
        /// throw to simulate a failure there. Null in play. Reset on domain reload.
        /// </summary>
        public static Action<ProfileSaveStage> SaveStageHook;

        /// <summary>
        /// Fault injection for a real partial backup write. Invoked only after half of the
        /// backup candidate has reached the OS; throwing simulates ENOSPC/device failure.
        /// </summary>
        public static Action BackupWriteMidFault;

        /// <summary>
        /// Lease fault injection (tests): invoked while acquiring the write lease; throw to
        /// simulate a lock that cannot be created/opened - the session must go read-only.
        /// </summary>
        public static Action LockAcquisitionFault;
#endif

        /// <summary>Redirects the file, for tests and probes. Null restores the default. Releases the current lock.</summary>
        public static void SetPathOverride(string path)
        {
            ReleaseOwnership();
            s_pathOverride = path;
            SaveLocked = false;
            SaveLockReason = string.Empty;
        }

        // ====================================================================== load

        public static ProfileData Load()
        {
            LastLoadFellBack = false;
            LastLoadMessage = string.Empty;
            LastLoadRecovery = ProfileLoadRecovery.None;

            string path = Path;
            AcquireOwnership(path);

            ProfileData data;
            ProfileLoadStatus status = ReadFile(path, out data);
            LastLoadStatus = status;

            if (SaveLocked)
            {
                // Another live instance owns the directory: this process reads and never
                // touches a file (no orphan clean-up, no roll-forward, no move-aside).
                return LoadReadOnly(path, status, data);
            }

            // A future-version primary or side file belongs to a newer build. Detect it
            // before any orphan cleanup so every byte remains untouched, fail-closed.
            if (status == ProfileLoadStatus.FutureVersion || HasFutureVersionSideFile())
            {
                return RefuseFutureVersion(path);
            }

            // A backup refresh always happens before the primary replace. Therefore an
            // orphan current-version staging file can never be newer authority than the
            // still-complete primary / published backup and is safe to discard now.
            DiscardOrphanBackupCandidate();

            switch (status)
            {
                case ProfileLoadStatus.Ok:
                    DiscardOrphanCandidate("the primary profile is valid");
                    return data;

                case ProfileLoadStatus.Missing:
                    return RecoverMissingPrimary(path);

                default:
                    return RecoverUnreadablePrimary(path, status);
            }
        }

        /// <summary>Strictly read-only load for a session that must not write (another instance owns the profile).</summary>
        private static ProfileData LoadReadOnly(string path, ProfileLoadStatus status, ProfileData data)
        {
            if (status == ProfileLoadStatus.Ok)
            {
                return data;
            }

            if (status == ProfileLoadStatus.FutureVersion)
            {
                LastLoadFellBack = true;
                LastLoadRecovery = ProfileLoadRecovery.ReadOnlyFutureVersion;
                return Fresh();
            }

            ProfileData side;
            if (status == ProfileLoadStatus.Missing && File.Exists(CandidatePath) && ReadFile(CandidatePath, out side) == ProfileLoadStatus.Ok)
            {
                return side;
            }

            if (File.Exists(BackupPath) && ReadFile(BackupPath, out side) == ProfileLoadStatus.Ok)
            {
                return side;
            }

            LastLoadFellBack = status != ProfileLoadStatus.Missing;
            return Fresh();
        }

        private static ProfileData RecoverMissingPrimary(string path)
        {
            // A side file from a NEWER build locks this session exactly like a newer primary
            // would: nothing of the newer build's data may be overwritten.
            if (IsFutureVersionFile(CandidatePath) || IsFutureVersionFile(BackupPath))
            {
                return RefuseFutureVersion(path);
            }

            // An interrupted replace: the candidate had been validated before the rename
            // began, and it is validated again here. Adopting it finishes the save.
            ProfileData candidate;
            if (File.Exists(CandidatePath) && ReadFile(CandidatePath, out candidate) == ProfileLoadStatus.Ok)
            {
                LastLoadRecovery = ProfileLoadRecovery.RolledForwardCandidate;
                LastLoadMessage = "The profile file was missing; a complete save that had been interrupted was recovered.";
                Debug.LogWarning("[Profile] " + LastLoadMessage);
                if (!TryMove(CandidatePath, path))
                {
                    // The rename failed but the candidate is a complete, validated profile:
                    // it stays the truth in memory and is written through the normal chain.
                    Materialize(candidate, path);
                }

                return candidate;
            }

            DiscardOrphanCandidate("the primary profile is missing and the candidate did not validate");

            ProfileData backup;
            if (File.Exists(BackupPath) && ReadFile(BackupPath, out backup) == ProfileLoadStatus.Ok)
            {
                LastLoadRecovery = ProfileLoadRecovery.RestoredBackup;
                LastLoadMessage = "The profile file was missing; the last verified backup was restored. The most recent run may not be in it.";
                Debug.LogWarning("[Profile] " + LastLoadMessage);
                Materialize(backup, path);
                return backup;
            }

            // First launch. Not a failure, and not worth a console line.
            return Fresh();
        }

        private static ProfileData RecoverUnreadablePrimary(string path, ProfileLoadStatus status)
        {
            LastLoadFellBack = true;
            string kept = PreserveCorruptFile(path);
            DiscardOrphanCandidate("the primary profile is unreadable; an unverified candidate is never used to replace it");

            string reason = Describe(status);

            ProfileData backup;
            if (File.Exists(BackupPath) && ReadFile(BackupPath, out backup) == ProfileLoadStatus.Ok)
            {
                LastLoadRecovery = ProfileLoadRecovery.RestoredBackup;
                LastLoadMessage = reason + " The last verified backup was restored instead; the most recent run may not be in it." +
                                  (string.IsNullOrEmpty(kept) ? string.Empty : " The unreadable file was kept as '" + kept + "'.");
                Debug.LogWarning("[Profile] " + LastLoadMessage);
                Materialize(backup, path);
                return backup;
            }

            LastLoadRecovery = ProfileLoadRecovery.FreshAfterCorruption;
            LastLoadMessage = reason + " Starting from a new profile instead." +
                              (string.IsNullOrEmpty(kept) ? string.Empty : " The unreadable file was kept as '" + kept + "'.");

            // WARNING, not Error. The prototype is fully playable from here; only the
            // previous profile is gone, and it has been kept on disk if it existed.
            Debug.LogWarning("[Profile] " + LastLoadMessage);
            return Fresh();
        }

        private static ProfileData RefuseFutureVersion(string path)
        {
            // Never downgrade, never overwrite, never tidy: every file in the directory
            // belongs to a newer build. This session plays on a fresh in-memory profile
            // and writes nothing (ResetProfile is refused too).
            LastLoadFellBack = true;
            LastLoadRecovery = ProfileLoadRecovery.ReadOnlyFutureVersion;
            SaveLocked = true;
            SaveLockReason = "The profile file was written by a newer version of the game, so this version leaves it untouched. Progress made now will not be saved.";
            LastLoadMessage = SaveLockReason;
            Debug.LogWarning("[Profile] " + LastLoadMessage + " (" + path + ")");
            return Fresh();
        }

        private static bool IsFutureVersionFile(string path)
        {
            ProfileData ignored;
            return File.Exists(path) && ReadFile(path, out ignored) == ProfileLoadStatus.FutureVersion;
        }

        private static bool HasFutureVersionSideFile()
        {
            return IsFutureVersionFile(CandidatePath) ||
                   IsFutureVersionFile(BackupPath) ||
                   IsFutureVersionFile(BackupCandidatePath);
        }

        private static string Describe(ProfileLoadStatus status)
        {
            switch (status)
            {
                case ProfileLoadStatus.Empty: return "The profile file was empty.";
                case ProfileLoadStatus.Truncated: return "The profile file was cut off before its end.";
                case ProfileLoadStatus.Malformed: return "The profile file is not valid JSON.";
                case ProfileLoadStatus.NoVersion: return "The profile file does not carry a save version.";
                case ProfileLoadStatus.Unreadable: return "The profile file could not be read.";
                case ProfileLoadStatus.InvalidLineage: return "The profile file has no valid profile lineage.";
                default: return "The profile file could not be used.";
            }
        }

        /// <summary>Reads and diagnoses one file. Never throws.</summary>
        public static ProfileLoadStatus ReadFile(string path, out ProfileData data)
        {
            data = null;
            string json;
            try
            {
                if (!File.Exists(path))
                {
                    return ProfileLoadStatus.Missing;
                }

                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Profile] Could not read '" + path + "' (" + e.GetType().Name + ").");
                return ProfileLoadStatus.Unreadable;
            }

            ProfileLoadStatus status = ProfileMigration.Parse(json, out data);
            if (status == ProfileLoadStatus.Ok && ProfileMigration.CarriesLegacyRing(json))
            {
                // v2 → v3 quarantine policy: the retired ring is not carried into the new
                // document; the raw-GUID namespace is refused fail-closed by the parser.
                Debug.Log("[Profile] A version-2 completion-id ring was found in '" + path + "' and dropped (WP-009H: lineage + ordinal replace it).");
            }

            if (status == ProfileLoadStatus.Ok && !data.IsValid())
            {
                // Parse + Migrate sanitises, so this cannot happen for real data; a
                // failure here would be a bug in Sanitize, and it is reported, not trusted.
                data = null;
                return ProfileLoadStatus.Malformed;
            }

            return status;
        }

        /// <summary>Writes a recovered profile as the primary through the normal chain (no-op while locked).</summary>
        private static void Materialize(ProfileData data, string path)
        {
            if (SaveLocked)
            {
                return;
            }

            ProfileSaveResult result = Save(data);
            if (!result.Success)
            {
                Debug.LogWarning("[Profile] The recovered profile could not be written back to '" + path + "' (" +
                                 result.FailedStage + "). It stays in memory for this session.");
            }
        }

        private static void DiscardOrphanCandidate(string why)
        {
            string candidate = CandidatePath;
            if (!File.Exists(candidate))
            {
                return;
            }

            Debug.Log("[Profile] Discarding leftover save candidate '" + System.IO.Path.GetFileName(candidate) + "': " + why + ".");
            TryDelete(candidate);
        }

        private static void DiscardOrphanBackupCandidate()
        {
            string candidate = BackupCandidatePath;
            if (!File.Exists(candidate))
            {
                return;
            }

            Debug.Log("[Profile] Discarding leftover backup candidate '" + System.IO.Path.GetFileName(candidate) + "'.");
            TryDelete(candidate);
        }

        // ====================================================================== save

        public static ProfileSaveResult Save(ProfileData data)
        {
            ProfileSaveResult result = SaveCore(data);
            LastSaveResult = result;
            if (!result.Success)
            {
                // A save that cannot be written must not interrupt play either.
                Debug.LogWarning("[Profile] Profile not saved (" + result.FailedStage + "): " + result.Message);
            }

            return result;
        }

        private static ProfileSaveResult SaveCore(ProfileData data)
        {
            if (data == null)
            {
                return new ProfileSaveResult(false, ProfileSaveStage.Refused, "No profile to save.");
            }

            if (SaveLocked)
            {
                return new ProfileSaveResult(false, ProfileSaveStage.Refused, SaveLockReason);
            }

            string path = Path;
            string candidatePath = CandidatePath;
            string backupPath = BackupPath;
            ProfileSaveStage stage = ProfileSaveStage.Serialize;
            string json = null;

            try
            {
                data.Sanitize();
                if (!data.IsValid())
                {
                    return new ProfileSaveResult(false, ProfileSaveStage.Refused, "The profile failed validation and was not written.");
                }

                data.LastSavedUtc = DateTime.UtcNow.ToString("o");

                // 1 ---- serialize and prove the text round-trips before it touches the disk
                Hook(stage);
                // The profile is machine-owned. Compact JSON keeps the fixed-size
                // lineage+ordinal replay state O(1) without pretty-print whitespace
                // amplifying finite entitlement-list growth.
                json = JsonUtility.ToJson(data, false);
                ProfileData echo;
                if (ProfileMigration.Parse(json, out echo) != ProfileLoadStatus.Ok || !echo.IsValid())
                {
                    return new ProfileSaveResult(false, stage, "The serialised profile did not parse back.");
                }

                byte[] bytes = Encoding.UTF8.GetBytes(json);

                string directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 2 ---- candidate: written in two halves so a mid-write fault leaves a real
                //        truncated file, then fsync'd so a crash after this step keeps it
                stage = ProfileSaveStage.BeforeCandidateWrite;
                Hook(stage);
                using (var stream = new FileStream(candidatePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    int half = bytes.Length / 2;
                    stream.Write(bytes, 0, half);
                    stream.Flush(false); // buffer → OS: a crash here really does leave half a file
                    stage = ProfileSaveStage.CandidateWriteMid;
                    Hook(stage);
                    stream.Write(bytes, half, bytes.Length - half);
                    stage = ProfileSaveStage.Flush;
                    Hook(stage);
                    stream.Flush(true);
                }

                // 3 ---- read-back
                stage = ProfileSaveStage.CandidateReadBack;
                Hook(stage);
                if (!FileEquals(candidatePath, json))
                {
                    TryDelete(candidatePath);
                    return new ProfileSaveResult(false, stage, "The save candidate did not read back as written.");
                }

                // 4 ---- last-known-good backup of the current primary (only if it validates:
                //        a bad primary must not overwrite a good backup)
                stage = ProfileSaveStage.BackupCopy;
                Hook(stage);
                bool primaryExists = File.Exists(path);
                if (primaryExists)
                {
                    ProfileData current;
                    if (ReadFile(path, out current) == ProfileLoadStatus.Ok)
                    {
                        RefreshBackupAtomically(path, backupPath);
                    }
                }

                // 5 ---- replace: one rename over the primary
                stage = ProfileSaveStage.BeforeReplace;
                Hook(stage);
                stage = ProfileSaveStage.Replace;
                Hook(stage);
                ReplacePrimary(candidatePath, path, primaryExists);

                // 6 ---- verify the primary; on a mismatch put the backup back
                stage = ProfileSaveStage.PostReplaceVerify;
                Hook(stage);
                if (!FileEquals(path, json))
                {
                    if (RecoverAfterFailedReplace(path, candidatePath, backupPath, json))
                    {
                        return ProfileSaveResult.Ok;
                    }

                    return new ProfileSaveResult(false, stage, "The profile file did not verify after the replace; the previous profile was restored.");
                }

                return ProfileSaveResult.Ok;
            }
            catch (Exception e)
            {
                // The exception class goes to the log; the receipt carries a fixed sentence.
                // Exception messages can contain absolute paths or OS details. The stage
                // and exception type are sufficient for diagnostics without leaking them.
                Debug.LogWarning("[Profile] Save failed at " + stage + " (" + e.GetType().Name + ").");
                if (stage >= ProfileSaveStage.Replace && RecoverAfterFailedReplace(path, candidatePath, backupPath, json))
                {
                    // Nothing older was left to restore and the validated candidate is now
                    // the primary, verified byte for byte: the save did complete.
                    return ProfileSaveResult.Ok;
                }

                TryDelete(candidatePath);
                TryDelete(BackupCandidatePath);
                return new ProfileSaveResult(false, stage, "The profile file could not be written.");
            }
        }

        private static void Hook(ProfileSaveStage stage)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Action<ProfileSaveStage> hook = SaveStageHook;
            if (hook != null)
            {
                hook(stage);
            }
#endif
        }

        private static void ReplacePrimary(string candidatePath, string path, bool primaryExists)
        {
            if (!primaryExists)
            {
                File.Move(candidatePath, path);
                return;
            }

            try
            {
                // No backup argument: on this runtime that is a single rename(2) over the
                // destination, atomic on the tested file systems. The .bak was copied in
                // step 4, so nothing depends on File.Replace's own backup semantics.
                File.Replace(candidatePath, path, null);
            }
            catch (PlatformNotSupportedException)
            {
                // Non-atomic fallback. The window between the two calls is covered by
                // the recovery chain: .bak (old) and .writing (new) both exist during it.
                File.Delete(path);
                File.Move(candidatePath, path);
            }
        }

        /// <summary>
        /// After a failure at or past the replace step the disk must end up equal to what
        /// the caller will keep in memory. With a validated backup that is the OLD profile:
        /// it is put back through a temp file and a rename (the save reports failure, live
        /// stays old, disk is old). Without a backup the only complete profile left is
        /// the NEW one - already renamed into place, or still the validated candidate - and
        /// deleting it would lose the whole profile; it is kept / rolled forward and, when
        /// the primary then holds exactly this save's text, the save counts as completed
        /// (returns true, so the caller publishes and live equals disk again).
        /// </summary>
        private static bool RecoverAfterFailedReplace(string path, string candidatePath, string backupPath, string json)
        {
            try
            {
                // The replace may have completed before a verification/read-back fault.
                // If the primary is byte-for-byte the already validated candidate, the
                // transaction committed. Publishing success keeps RAM and disk aligned;
                // attempting a rollback first could itself fail and falsely report that
                // the old profile is still authoritative while the new one is on disk.
                if (!string.IsNullOrEmpty(json) && FileEquals(path, json))
                {
                    TryDelete(candidatePath);
                    return true;
                }

                ProfileData backup;
                if (File.Exists(backupPath) && ReadFile(backupPath, out backup) == ProfileLoadStatus.Ok)
                {
                    if (!FilesEqual(path, backupPath))
                    {
                        string restoring = path + ".restoring";
                        CopyDurable(backupPath, restoring);
                        ReplacePrimary(restoring, path, File.Exists(path));
                    }

                    TryDelete(candidatePath);
                    return false;
                }

                ProfileData primary;
                if (File.Exists(path) && ReadFile(path, out primary) == ProfileLoadStatus.Ok)
                {
                    TryDelete(candidatePath);
                    return FileEquals(path, json);
                }

                ProfileData candidate;
                if (File.Exists(candidatePath) && ReadFile(candidatePath, out candidate) == ProfileLoadStatus.Ok &&
                    FileEquals(candidatePath, json))
                {
                    ReplacePrimary(candidatePath, path, File.Exists(path));
                    return FileEquals(path, json);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Profile] Recovery after a failed replace did not complete (" + e.GetType().Name + ").");
            }

            return false;
        }

        private static bool FileEquals(string path, string expected)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string actual = File.ReadAllText(path);
            return string.Equals(actual, expected, StringComparison.Ordinal);
        }

        private static bool FilesEqual(string a, string b)
        {
            return File.Exists(a) && File.Exists(b) && string.Equals(File.ReadAllText(a), File.ReadAllText(b), StringComparison.Ordinal);
        }

        /// <summary>Copies a file and fsyncs the copy, so the backup survives a crash right after.</summary>
        private static void CopyDurable(string source, string destination)
        {
            byte[] bytes = File.ReadAllBytes(source);
            using (var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        /// <summary>
        /// Refreshes the last-known-good backup without ever truncating the published
        /// backup in place. A failed/partial write only damages .bak.writing; the prior
        /// .bak and the primary remain complete. Publication is one atomic replace/move.
        /// </summary>
        private static void RefreshBackupAtomically(string source, string backupPath)
        {
            string staging = backupPath + CandidateSuffix;
            TryDelete(staging);

            byte[] bytes = File.ReadAllBytes(source);
            using (var stream = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                int half = bytes.Length / 2;
                stream.Write(bytes, 0, half);
                stream.Flush(false);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Action fault = BackupWriteMidFault;
                if (fault != null)
                {
                    fault();
                }
#endif
                stream.Write(bytes, half, bytes.Length - half);
                stream.Flush(true);
            }

            ProfileData candidate;
            if (!FilesEqual(source, staging) || ReadFile(staging, out candidate) != ProfileLoadStatus.Ok)
            {
                throw new IOException("The backup candidate did not verify.");
            }

            if (File.Exists(backupPath))
            {
                try
                {
                    File.Replace(staging, backupPath, null);
                }
                catch (PlatformNotSupportedException e)
                {
                    // Do not fall back to delete+move: preserving the older verified
                    // backup and failing before the primary replace is the safe outcome.
                    throw new IOException("Atomic backup replace is unavailable.", e);
                }
            }
            else
            {
                File.Move(staging, backupPath);
            }

            ProfileData published;
            if (!FilesEqual(source, backupPath) || ReadFile(backupPath, out published) != ProfileLoadStatus.Ok)
            {
                throw new IOException("The published backup did not verify.");
            }
        }

        private static bool TryMove(string from, string to)
        {
            try
            {
                File.Move(from, to);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Profile] Could not adopt '" + System.IO.Path.GetFileName(from) + "' (" + e.GetType().Name + ").");
                return false;
            }
        }

        // ================================================================= ownership

        /// <summary>
        /// Write ownership of the profile file (WP-009H): an OS lease, not a pid file.
        ///
        /// The lock file next to the profile is opened once and a byte-range lock is
        /// taken on it with <see cref="FileStream.Lock"/> - on this runtime (Mono, Unix) an
        /// <c>fcntl</c> POSIX record lock: owned by the process, refused to every other
        /// process while held, released by the kernel when the process exits or crashes,
        /// unaffected by pid reuse. The stream stays open for as long as this process
        /// owns the profile; that open, locked stream IS the lease. The file's text
        /// (pid, name) is diagnostics only and carries no authority.
        ///
        /// FAIL-CLOSED: if the lock cannot be taken - another live instance holds it, or
        /// opening / locking / writing the file throws for any reason - this session is
        /// read-only (<see cref="SaveLocked"/>): it plays, it never saves, resets or commits
        /// a completion, and the profile pane says so. There is no "continue without the
        /// lock" path.
        ///
        /// RULES that keep the lease honest: the lock file is opened exactly once per
        /// process for a given path (a POSIX lock is dropped when ANY descriptor of the
        /// file in the process is closed, so nothing else in the process may open it);
        /// it is never deleted (unlinking would let a newcomer lock a different inode
        /// than a third process still has open); release closes only this process's own
        /// stream. Same process, same path, second load → still the owner. The Editor
        /// releases the lease before a domain reload and re-acquires it afterwards (the
        /// old stream would otherwise be closed by the finalizer at an arbitrary moment).
        /// </summary>
        private static void AcquireOwnership(string path)
        {
            string lockPath = path + LockSuffix;
            if (s_lease != null && string.Equals(s_leasePath, lockPath, StringComparison.Ordinal))
            {
                return;
            }

            ReleaseOwnership();

            FileStream stream = null;
            try
            {
                string directory = System.IO.Path.GetDirectoryName(lockPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Action fault = LockAcquisitionFault;
                if (fault != null)
                {
                    fault();
                }
#endif

                stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                try
                {
                    stream.Lock(0, 1);
                }
                catch (IOException)
                {
                    // Held by another live process (an fcntl conflict). Read-only for this session.
                    stream.Dispose();
                    SaveLocked = true;
                    SaveLockReason = OtherInstanceReason;
                    Debug.LogWarning("[Profile] " + SaveLockReason + " (" + lockPath + ")");
                    return;
                }

                // Diagnostics through the lease stream itself (never a second open).
                byte[] text = Encoding.UTF8.GetBytes("pid=" + CurrentPid() + "\nname=" + CurrentProcessName() + "\n");
                stream.SetLength(0);
                stream.Write(text, 0, text.Length);
                stream.Flush(true);

                s_lease = stream;
                s_leasePath = lockPath;
            }
            catch (Exception e)
            {
                if (stream != null)
                {
                    try { stream.Dispose(); } catch (Exception) { }
                }

                s_lease = null;
                s_leasePath = null;
                SaveLocked = true;
                SaveLockReason = LockUnavailableReason;
                Debug.LogWarning("[Profile] " + SaveLockReason + " (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        private const string OtherInstanceReason =
            "Another copy of the game is already using this profile. Progress made in this window will not be saved.";

        private const string LockUnavailableReason =
            "The profile could not be locked for writing, so this session is read-only. Progress made now will not be saved.";

        /// <summary>Releases the lease this process holds, if any. The lock file is never deleted.</summary>
        public static void ReleaseOwnership()
        {
            FileStream stream = s_lease;
            s_lease = null;
            s_leasePath = null;
            if (stream == null)
            {
                return;
            }

            try
            {
                stream.Unlock(0, 1);
            }
            catch (Exception)
            {
                // Closing the stream releases the lock regardless.
            }

            try
            {
                stream.Dispose();
            }
            catch (Exception)
            {
                // Nothing depends on it.
            }
        }

        /// <summary>True when this process holds the write lease for the current path.</summary>
        public static bool OwnsProfile => s_lease != null && string.Equals(s_leasePath, LockPath, StringComparison.Ordinal);

        private static int CurrentPid()
        {
            try
            {
                using (Process me = Process.GetCurrentProcess())
                {
                    return me.Id;
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string CurrentProcessName()
        {
            try
            {
                using (Process me = Process.GetCurrentProcess())
                {
                    return me.ProcessName ?? string.Empty;
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

#if UNITY_EDITOR
        // K-2: a domain reload would close the old lease stream in a finalizer at an
        // arbitrary time; release deliberately before the reload and let the next load
        // re-acquire (same process → succeeds).
        [UnityEditor.InitializeOnLoadMethod]
        private static void InstallEditorLeaseHooks()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseOwnership;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseOwnership;
            UnityEditor.EditorApplication.quitting -= ReleaseOwnership;
            UnityEditor.EditorApplication.quitting += ReleaseOwnership;
        }
#endif

        // ================================================================== plumbing

        /// <summary>
        /// Deletes the save and its side files. Debug affordance only; nothing in normal
        /// play calls it. Refused while the profile is read-only: a newer build's file or
        /// another instance's file is never deleted from here.
        /// </summary>
        public static bool Delete()
        {
            if (SaveLocked)
            {
                Debug.LogWarning("[Profile] Reset refused: " + SaveLockReason);
                return false;
            }

            TryDelete(Path);
            TryDelete(CandidatePath);
            TryDelete(BackupPath);
            TryDelete(BackupCandidatePath);
            return true;
        }

        private static ProfileData Fresh()
        {
            var data = new ProfileData { ProfileLineageId = ProfileData.NewLineageId(), LastAppliedCompletionOrdinal = 0 };
            data.Sanitize();
            return data;
        }

        /// <summary>
        /// Debug reset (WP-009H): writes a FRESH profile (new lineage, ordinal 0) over the
        /// primary through the normal chain and aligns the backup with it, so there is no
        /// window without a complete profile and recovery cannot resurrect the old
        /// lineage. Returns the fresh profile, or null when refused (read-only) or the
        /// write failed (old profile untouched on disk).
        /// </summary>
        public static ProfileData Reset()
        {
            if (SaveLocked)
            {
                Debug.LogWarning("[Profile] Reset refused: " + SaveLockReason);
                return null;
            }

            ProfileData fresh = Fresh();
            ProfileSaveResult written = Save(fresh);
            if (!written.Success)
            {
                Debug.LogWarning("[Profile] Reset did not happen: the fresh profile could not be written (" + written.FailedStage + ").");
                return null;
            }

            try
            {
                RefreshBackupAtomically(Path, BackupPath);
            }
            catch (Exception e)
            {
                TryDelete(BackupCandidatePath);
                if (TryRollbackResetFromBackup())
                {
                    Debug.LogWarning("[Profile] Reset did not happen: the new profile backup could not be aligned (" +
                                     e.GetType().Name + "), so the previous profile was restored.");
                    return null;
                }

                // No restorable old profile remains (for example, resetting before the
                // first save). Keep the already committed fresh primary. Quarantine any
                // stale backup so later corruption cannot resurrect an older lineage.
                PreserveCorruptFile(BackupPath);
                TryDelete(BackupPath);
                Debug.LogWarning("[Profile] Reset completed, but its backup could not be aligned (" + e.GetType().Name + ").");
            }

            return fresh;
        }

        private static bool TryRollbackResetFromBackup()
        {
            ProfileData previous;
            if (ReadFile(BackupPath, out previous) != ProfileLoadStatus.Ok)
            {
                return false;
            }

            try
            {
                ReplacePrimary(BackupPath, Path, File.Exists(Path));
                ProfileData restored;
                return ReadFile(Path, out restored) == ProfileLoadStatus.Ok &&
                       string.Equals(restored.ProfileLineageId, previous.ProfileLineageId, StringComparison.Ordinal) &&
                       restored.LastAppliedCompletionOrdinal == previous.LastAppliedCompletionOrdinal;
            }
            catch (Exception rollbackError)
            {
                Debug.LogWarning("[Profile] Reset rollback could not restore the previous profile (" +
                                 rollbackError.GetType().Name + ").");
                return false;
            }
        }

        /// <summary>
        /// Renames the unreadable file aside instead of overwriting it. Best effort: if
        /// even this fails, it is swallowed, because the caller is already on a recovery
        /// path and a second warning would say nothing new.
        /// </summary>
        private static string PreserveCorruptFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string target = path + CorruptSuffix + "." + DateTime.UtcNow.Ticks + ".json";
                File.Move(path, target);
                return System.IO.Path.GetFileName(target);
            }
            catch
            {
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Nothing useful to do, and nothing depends on it.
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_pathOverride = null;
            ReleaseOwnership();
            LastLoadFellBack = false;
            LastLoadMessage = string.Empty;
            LastLoadStatus = ProfileLoadStatus.Missing;
            LastLoadRecovery = ProfileLoadRecovery.None;
            SaveLocked = false;
            SaveLockReason = string.Empty;
            LastSaveResult = ProfileSaveResult.Ok;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            SaveStageHook = null;
            BackupWriteMidFault = null;
            LockAcquisitionFault = null;
#endif
        }
    }
}
