namespace DD.Prototype.Profile
{
    /// <summary>What a load attempt found. One value per distinguishable situation.</summary>
    public enum ProfileLoadStatus
    {
        /// <summary>No file. First launch; not a failure.</summary>
        Missing = 0,

        /// <summary>Parsed, migrated, validated.</summary>
        Ok = 1,

        /// <summary>Zero bytes or whitespace only.</summary>
        Empty = 2,

        /// <summary>Not parseable and does not end in a closing brace: cut off mid-write.</summary>
        Truncated = 3,

        /// <summary>Not parseable although it looks complete; or parsed to nothing.</summary>
        Malformed = 4,

        /// <summary>SaveVersion above what this build understands. Kept untouched.</summary>
        FutureVersion = 5,

        /// <summary>SaveVersion 0 or negative: not a real save.</summary>
        NoVersion = 6,

        /// <summary>The file could not be read at all (permissions, IO error).</summary>
        Unreadable = 7,

        /// <summary>
        /// WP-009H: a version-3 document whose replay lineage is missing or malformed. It
        /// is NOT repaired with a fresh lineage (that would silently re-open replay); it
        /// takes the corrupt-file procedure like any other unusable primary.
        /// </summary>
        InvalidLineage = 8
    }

    /// <summary>
    /// The schema chain. Every version this build has ever written is readable, and
    /// each step is explicit, idempotent and provable in EditMode with a hand-written
    /// document of that version.
    ///
    ///   1 → 2 (WP-009): UnattributedKills is derived by <see cref="ProfileData.Sanitize"/>
    ///   from a legacy total that exceeded its tier sum (lossless; nothing is spread
    ///   over tiers).
    ///   2 → 3 (WP-009H): a fresh ProfileLineageId is minted once and
    ///   LastAppliedCompletionOrdinal starts at 0 - the new lineage has applied no
    ///   completion yet; every run the player plays from here is ordinal 1, 2, ... The
    ///   v2 ring (AppliedCompletionIds, last 8 GUIDs) is DROPPED: evicted ids cannot be
    ///   recovered, the ids are not converted to ordinals, and the raw-GUID namespace is
    ///   retired - the token parser refuses every legacy id fail-closed, so nothing that
    ///   was in (or fell out of) the ring can ever be applied again. Every progression
    ///   field is carried unchanged. Idempotent: a v3 document is left as it is.
    ///   5 → 6 (WP-104, C-00 11.10 decision 2): the full free respec into the Awakening page — AshBalance =
    ///   AshEarnedTotal, AshSpentTotal = 0, MetaNodes / pin / seen emptied, MetaRespecCount unchanged, and the refund
    ///   recorded once in PendingAshRefundNotice. WeaponMastery, AshMilestones, the lineage and the ordinal are carried.
    ///   ModeStats is seeded: Normal = the pre-v6 HighestRound / RunsPlayed / TotalKills (no mode existed before v6).
    ///   A "v5" document that carries v6 fields is forged or corrupt: they are rebuilt, never trusted. The byte-for-byte
    ///   <c>*.v5.bak</c> is taken by <see cref="ProfileStore"/> before any write that replaces a pre-v6 file (§4.14).
    ///   Exactly once: the step runs only while the document says &lt; 6, and the save that publishes it stamps 6.
    ///
    /// A FUTURE version is refused, not downgraded: the caller keeps the file exactly
    /// as it is and runs read-only (<see cref="ProfileStore.SaveLocked"/>).
    /// </summary>
    public static class ProfileMigration
    {
        /// <summary>Parses a document into a diagnosis plus, when readable, a sanitised profile.</summary>
        public static ProfileLoadStatus Parse(string json, out ProfileData data)
        {
            data = null;

            if (json == null || json.Trim().Length == 0)
            {
                return ProfileLoadStatus.Empty;
            }

            ProfileData raw;
            try
            {
                raw = UnityEngine.JsonUtility.FromJson<ProfileData>(json);
            }
            catch (System.Exception)
            {
                // Truncated: a prefix of one of our own documents (opens with the version
                // key) that never reached its closing brace. Anything else is malformed.
                string trimmed = json.Trim();
                bool looksLikeOurs = trimmed.StartsWith("{") && trimmed.Contains("\"SaveVersion\"");
                bool unterminated = !trimmed.EndsWith("}");
                return looksLikeOurs && unterminated ? ProfileLoadStatus.Truncated : ProfileLoadStatus.Malformed;
            }

            if (raw == null)
            {
                return ProfileLoadStatus.Malformed;
            }

            // JsonUtility leaves an absent key at the field's default (the current version),
            // so the key itself must be present in the text for the document to count as a save.
            if (raw.SaveVersion <= 0 || json.IndexOf("\"SaveVersion\"", System.StringComparison.Ordinal) < 0)
            {
                return ProfileLoadStatus.NoVersion;
            }

            if (raw.SaveVersion > ProfileData.CurrentVersion)
            {
                return ProfileLoadStatus.FutureVersion;
            }

            if (raw.SaveVersion >= 3 && !ProfileData.IsLineageId(raw.ProfileLineageId))
            {
                return ProfileLoadStatus.InvalidLineage;
            }

            Migrate(raw);
            data = raw;
            return ProfileLoadStatus.Ok;
        }

        /// <summary>DEC-098: Kül = XP / 30 (the fallback when no config is readable).</summary>
        public const int FallbackXpPerAsh = 30;

        /// <summary>WP-077 v5 back-grant: Ash for the XP and the achievements a v4 profile already earned.</summary>
        public static int BackGrantAsh(ProfileData data)
        {
            if (data == null) return 0;
            // R1-B P4 (C-00 30.09): Parse/Migrate must not throw (ProfileStore.ReadFile never throws). A config or catalog
            // failure falls back to the XP-only grant at the DEC-098 rate (Kül = XP / 30), never to an exception.
            long xpOnly = System.Math.Max(0, data.TotalXpEarned) / FallbackXpPerAsh;
            try
            {
                DD.Prototype.Core.Config.PrototypeConfig config = DD.Prototype.Core.Config.PrototypeConfig.Active;
                DD.Prototype.Core.Config.ProgressionConfigSection prog = config != null ? config.Progression : null;
                int perAsh = prog != null ? System.Math.Max(1, prog.XpPerAsh) : FallbackXpPerAsh;
                long ash = System.Math.Max(0, data.TotalXpEarned) / perAsh;
                if (data.Achievements != null)
                {
                    for (int i = 0; i < data.Achievements.Count; i++)
                    {
                        DD.Prototype.Achievements.AchievementDefinition d = DD.Prototype.Achievements.AchievementCatalog.Find(data.Achievements[i]);
                        if (d != null && prog != null) ash += prog.AshForRarity((int)d.Rarity);
                    }
                }

                return (int)System.Math.Min(ash, int.MaxValue);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[ProfileMigration] Ash back-grant fell back to XP only: " + e.Message);
                return (int)System.Math.Min(xpOnly, int.MaxValue);
            }
        }

        [System.Serializable]
        private sealed class VersionProbe
        {
            public int SaveVersion;
        }

        /// <summary>
        /// WP-104: the SaveVersion a document declares on disk, before any migration (0 when it declares none or does not
        /// parse). ProfileStore reads it to keep a pre-v6 file byte for byte before replacing it. Never throws.
        /// </summary>
        public static int ReadDocumentVersion(string json)
        {
            if (string.IsNullOrEmpty(json) || json.IndexOf("\"SaveVersion\"", System.StringComparison.Ordinal) < 0) return 0;
            try
            {
                VersionProbe probe = UnityEngine.JsonUtility.FromJson<VersionProbe>(json);
                return probe != null ? System.Math.Max(0, probe.SaveVersion) : 0;
            }
            catch (System.Exception)
            {
                return 0;
            }
        }

        /// <summary>True when the text still carries the retired v2 ring (reported at migration, never used).</summary>
        public static bool CarriesLegacyRing(string json)
        {
            return json != null && json.IndexOf("\"AppliedCompletionIds\"", System.StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Brings a readable document up to <see cref="ProfileData.CurrentVersion"/> in
        /// place. Running it on a current document changes nothing (idempotent).
        /// </summary>
        public static void Migrate(ProfileData data)
        {
            if (data == null)
            {
                return;
            }

            int from = data.SaveVersion;

            if (from < 2)
            {
                // v1 → v2: fields absent from the file already hold their defaults after
                // JsonUtility (UnattributedKills is derived by Sanitize below).
                data.UnattributedKills = 0;
            }

            if (from < 3)
            {
                // v2 → v3: one lineage for the rest of this profile's life; no completion
                // applied under it yet. The legacy ring is not carried (see the class note).
                data.ProfileLineageId = ProfileData.NewLineageId();
                data.LastAppliedCompletionOrdinal = 0;
            }

            if (from < 4)
            {
                // v3 → v4 (WP-023): the two lifetime feat statistics and the content list are
                // absent from the file and already at their zero / empty defaults after
                // JsonUtility. Lineage and ordinal are carried as they are: a migrated
                // profile keeps refusing every completion it has already applied.
                data.SpecialWeaponPickups = 0;
                data.FlawlessRounds = 0;
                if (data.UnlockedContent == null)
                {
                    data.UnlockedContent = new System.Collections.Generic.List<string>();
                }
            }

            if (from < 5)
            {
                // v4 → v5 (WP-077): the meta PATH. K11 (C-00, 2026-09-24): the level is NOT recomputed on
                // the new curve — the player keeps the level earned; 100·L + 10·L² applies from here on.
                // Weapons stay granted (Sanitize maps the M3 names to WP-069 keys). Ash is granted back for
                // the life so far: floor(TotalXpEarned / XpPerAsh) + each earned achievement's rarity Ash.
                int ash = BackGrantAsh(data);
                data.AshEarnedTotal = ash;
                data.AshSpentTotal = 0;
                data.AshBalance = ash;
                // R1-B P6 (C-00 30.09): a real v4 build cannot write the v5 meta fields — a "v4" document that carries them is
                // forged or corrupt, and keeping them would mean free nodes (AshSpentTotal 0). Start them empty.
                data.MetaNodes = new System.Collections.Generic.List<string>();
                data.MetaRespecCount = 0;
                data.MetaSeen = 0;
                data.AshMilestones = new System.Collections.Generic.List<string>();
                data.WeaponMastery = new System.Collections.Generic.List<WeaponMasteryEntry>();
                data.MetaPinnedNode = string.Empty;
            }

            if (from < 6)
            {
                MigrateToAwakening(data);
            }

            data.SaveVersion = ProfileData.CurrentVersion;

            // Sanitize is part of every step: it is what turns "TotalKills > tier sum"
            // into an explicit UnattributedKills value instead of a silent discrepancy.
            data.Sanitize();
        }

        /// <summary>
        /// v5 → v6 (WP-104, C-00 11.10 decision 2): the full free respec. The weapon page is retired and the Ash meaning of
        /// the PATH changes (Awakening), so every Ash spent comes back instead of a page-by-page refund: the simplest rule that
        /// cannot lose a point. Runs on the in-memory document only; ProfileStore keeps the v5 bytes before the first write.
        /// </summary>
        private static void MigrateToAwakening(ProfileData data)
        {
            int earned = System.Math.Max(0, data.AshEarnedTotal);
            int refund = System.Math.Min(System.Math.Max(0, data.AshSpentTotal), earned);
            data.AshEarnedTotal = earned;
            data.AshSpentTotal = 0;
            data.AshBalance = earned;
            data.MetaNodes = new System.Collections.Generic.List<string>();
            data.MetaPinnedNode = string.Empty;
            data.MetaSeen = 0;
            // MetaRespecCount is NOT bumped: this respec is the build's, not the player's (the next one stays as it was).
            // A v5 build cannot write the v6 fields: whatever a "v5" document carries in them is rebuilt here.
            data.PendingAshRefundNotice = refund;
            data.ModeStats = ProfileData.NewModeStats();
            ModeStatsEntry normal = data.ModeStats[DD.Prototype.Rounds.TreeMode.Normal];
            normal.HighestRound = System.Math.Max(0, data.HighestRound);
            normal.Runs = System.Math.Max(0, data.RunsPlayed);
            // The kill invariant is enforced by Sanitize after this step (a tier count is never un-counted): seed with its result.
            int witnessed = ProfileProgression.SaturatingAdd(
                ProfileProgression.SaturatingSum(System.Math.Max(0, data.StandardKills), System.Math.Max(0, data.SpecialKills),
                    System.Math.Max(0, data.EliteKills), System.Math.Max(0, data.UltraKills)), System.Math.Max(0, data.UnattributedKills));
            normal.TotalKills = System.Math.Max(System.Math.Max(0, data.TotalKills), witnessed);
        }
    }
}
