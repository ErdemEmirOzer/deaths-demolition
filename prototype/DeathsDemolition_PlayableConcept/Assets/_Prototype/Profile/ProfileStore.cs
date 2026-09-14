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
        private static string s_ownedLockPath;

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
        /// Liveness probe for the ownership lock (tests): (pid, processName) → alive.
        /// Null uses <see cref="System.Diagnostics.Process"/>. Reset on domain reload.
        /// </summary>
        public static Func<int, string, bool> ProcessLivenessProbe;
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

            switch (status)
            {
                case ProfileLoadStatus.Ok:
                    DiscardOrphanCandidate("the primary profile is valid");
                    return data;

                case ProfileLoadStatus.Missing:
                    return RecoverMissingPrimary(path);

                case ProfileLoadStatus.FutureVersion:
                    return RefuseFutureVersion(path);

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

        private static string Describe(ProfileLoadStatus status)
        {
            switch (status)
            {
                case ProfileLoadStatus.Empty: return "The profile file was empty.";
                case ProfileLoadStatus.Truncated: return "The profile file was cut off before its end.";
                case ProfileLoadStatus.Malformed: return "The profile file is not valid JSON.";
                case ProfileLoadStatus.NoVersion: return "The profile file does not carry a save version.";
                case ProfileLoadStatus.Unreadable: return "The profile file could not be read.";
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
                json = JsonUtility.ToJson(data, true);
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
                        CopyDurable(path, backupPath);
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
                    RecoverAfterFailedReplace(path, candidatePath, backupPath, json);
                    return new ProfileSaveResult(false, stage, "The profile file did not verify after the replace; the previous profile was restored.");
                }

                return ProfileSaveResult.Ok;
            }
            catch (Exception e)
            {
                // The detail goes to the log; the receipt carries a fixed sentence.
                Debug.LogWarning("[Profile] Save failed at " + stage + ": " + e.GetType().Name + ": " + e.Message);
                if (stage >= ProfileSaveStage.Replace && RecoverAfterFailedReplace(path, candidatePath, backupPath, json))
                {
                    // Nothing older was left to restore and the validated candidate is now
                    // the primary, verified byte for byte: the save did complete.
                    return ProfileSaveResult.Ok;
                }

                TryDelete(candidatePath);
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
        /// Write ownership of the profile file (WP-009 §13: several Player instances).
        /// A lock file next to the profile names the owning process. Another LIVE process
        /// of the same name holding it makes this one read-only for the session; a lock
        /// left by a dead process is taken over. The same pid re-acquiring (domain
        /// reload in the Editor) is always the owner.
        /// </summary>
        private static void AcquireOwnership(string path)
        {
            string lockPath = path + LockSuffix;
            if (string.Equals(s_ownedLockPath, lockPath, StringComparison.Ordinal))
            {
                return;
            }

            int ourPid;
            string ourName;
            try
            {
                using (Process me = Process.GetCurrentProcess())
                {
                    ourPid = me.Id;
                    ourName = me.ProcessName ?? string.Empty;
                }
            }
            catch (Exception)
            {
                ourPid = 0;
                ourName = string.Empty;
            }

            try
            {
                if (File.Exists(lockPath))
                {
                    int pid;
                    string name;
                    if (ParseLock(File.ReadAllText(lockPath), out pid, out name) && pid != ourPid && IsAlive(pid, name))
                    {
                        SaveLocked = true;
                        SaveLockReason = "Another copy of the game is already using this profile. Progress made in this window will not be saved.";
                        Debug.LogWarning("[Profile] " + SaveLockReason + " (lock held by pid " + pid + ")");
                        return;
                    }
                }

                string directory = System.IO.Path.GetDirectoryName(lockPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Written through an exclusive create so two instances starting at the same
                // instant cannot both believe they own it; a stale file is removed first.
                TryDelete(lockPath);
                byte[] lockBytes = Encoding.UTF8.GetBytes("pid=" + ourPid + "\nname=" + ourName + "\n");
                using (var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(lockBytes, 0, lockBytes.Length);
                }

                s_ownedLockPath = lockPath;
            }
            catch (Exception e)
            {
                // A lock that cannot be written is not worth refusing saves over; the
                // profile itself is still replaced atomically.
                Debug.Log("[Profile] Ownership lock unavailable (" + e.GetType().Name + "); continuing without it.");
            }
        }

        /// <summary>Releases the lock this process holds, if any. Called on quit and on a path change.</summary>
        public static void ReleaseOwnership()
        {
            string lockPath = s_ownedLockPath;
            s_ownedLockPath = null;
            if (string.IsNullOrEmpty(lockPath))
            {
                return;
            }

            TryDelete(lockPath);
        }

        /// <summary>True when this process holds the write lock for the current path.</summary>
        public static bool OwnsProfile => string.Equals(s_ownedLockPath, LockPath, StringComparison.Ordinal);

        /// <summary>Case-insensitive match on the first 15 characters (the macOS p_comm width).</summary>
        public static bool SameProcessName(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            const int width = 15;
            string x = a.Length > width ? a.Substring(0, width) : a;
            string y = b.Length > width ? b.Substring(0, width) : b;
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ParseLock(string text, out int pid, out string name)
        {
            pid = 0;
            name = string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("pid=", StringComparison.Ordinal))
                {
                    int.TryParse(line.Substring(4), out pid);
                }
                else if (line.StartsWith("name=", StringComparison.Ordinal))
                {
                    name = line.Substring(5);
                }
            }

            return pid > 0;
        }

        private static bool IsAlive(int pid, string name)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Func<int, string, bool> probe = ProcessLivenessProbe;
            if (probe != null)
            {
                return probe(pid, name);
            }
#endif

            try
            {
                using (Process other = Process.GetProcessById(pid))
                {
                    if (other == null || other.HasExited)
                    {
                        return false;
                    }

                    // The pid may have been reused by an unrelated process since the crash
                    // that left the lock: only a process of the same name counts. macOS
                    // reports other processes' names truncated (p_comm, 15 chars), so the
                    // comparison uses that prefix.
                    return SameProcessName(other.ProcessName, name);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

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
            return true;
        }

        private static ProfileData Fresh()
        {
            var data = new ProfileData();
            data.Sanitize();
            return data;
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
            s_ownedLockPath = null;
            LastLoadFellBack = false;
            LastLoadMessage = string.Empty;
            LastLoadStatus = ProfileLoadStatus.Missing;
            LastLoadRecovery = ProfileLoadRecovery.None;
            SaveLocked = false;
            SaveLockReason = string.Empty;
            LastSaveResult = ProfileSaveResult.Ok;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            SaveStageHook = null;
            ProcessLivenessProbe = null;
#endif
        }
    }
}
