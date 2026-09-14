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

            data.SaveVersion = ProfileData.CurrentVersion;

            // Sanitize is part of every step: it is what turns "TotalKills > tier sum"
            // into an explicit UnattributedKills value instead of a silent discrepancy.
            data.Sanitize();
        }
    }
}
