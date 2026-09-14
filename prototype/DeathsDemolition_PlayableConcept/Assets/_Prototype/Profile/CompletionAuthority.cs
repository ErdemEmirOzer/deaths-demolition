using System;
using System.Globalization;
using UnityEngine;

namespace DD.Prototype.Profile
{
    /// <summary>Why a completion token was accepted or refused. Order follows the validation sequence.</summary>
    public enum CompletionTokenVerdict
    {
        Valid = 0,

        /// <summary>Null or empty: the run carried no identity.</summary>
        Missing = 1,

        /// <summary>Not a token this authority ever produced (includes every legacy raw GUID).</summary>
        Malformed = 2,

        /// <summary>A token schema this build does not understand.</summary>
        UnsupportedVersion = 3,

        /// <summary>Issued for another profile lineage (older profile, reset profile, foreign file).</summary>
        ForeignLineage = 4,

        /// <summary>Ordinal outside the valid range (0, negative, unparsable).</summary>
        InvalidOrdinal = 5,

        /// <summary>Ordinal at or below the profile's high-water mark: this completion is already in the profile.</summary>
        AlreadyApplied = 6,

        /// <summary>Ordinal beyond the next expected one: not the single live run.</summary>
        OrdinalGap = 7,

        /// <summary>Right ordinal, but not the capability this process issued for it (stale nonce, abort, relaunch, forged).</summary>
        NotIssued = 8
    }

    /// <summary>The parsed parts of a completion token. Opaque outside this file.</summary>
    public readonly struct CompletionToken
    {
        public readonly string Lineage;
        public readonly long Ordinal;
        public readonly string Nonce;

        public CompletionToken(string lineage, long ordinal, string nonce)
        {
            Lineage = lineage;
            Ordinal = ordinal;
            Nonce = nonce;
        }
    }

    /// <summary>
    /// WP-009H · The completion identity authority: the one place that issues and
    /// validates the identity a finished run presents to the profile.
    ///
    /// WHY (the WP-009 defect): the profile used to remember the last eight completion
    /// GUIDs. A ninth run evicted the first, and the first run's summary could then be
    /// applied a second time - "exactly-once" held for eight runs only. Bounded
    /// metadata cannot make arbitrary GUIDs exactly-once; the identity itself has to be
    /// ordered.
    ///
    /// MODEL (O(1), single active run): the profile carries a fixed lineage id and a
    /// monotonic high-water mark <see cref="ProfileData.LastAppliedCompletionOrdinal"/>.
    /// A run is issued ONE live capability for ordinal <c>mark + 1</c>; the token is
    /// <c>ddc1:&lt;lineage&gt;:&lt;ordinal&gt;:&lt;nonce&gt;</c>. A token is valid only when it parses,
    /// names this lineage, carries exactly the next ordinal, and is the capability this
    /// process currently holds. Once the transaction that carries it is on disk the mark
    /// advances and the capability is consumed, so every earlier ordinal - no matter how
    /// many runs later - is refused as already applied. No list, no ring, no history.
    ///
    /// LIFECYCLE: StartRun issues (a new issue invalidates the previous capability);
    /// abort and profile reset invalidate; a save failure leaves the capability live so
    /// the same token can be retried; a successful save consumes it. After a relaunch
    /// nothing is live, so an unapplied token of the previous process is refused
    /// fail-closed (writing an active run at crash time is out of scope), and an applied
    /// one is refused by the mark. Overflow: at <c>long.MaxValue</c> nothing more is issued;
    /// nothing wraps, nothing old becomes valid again.
    ///
    /// Only the profile transaction reads the parts; every other system treats the
    /// string as opaque. The test seam below exists in the Editor and development
    /// builds only; a release Player has no way to mint or forge a token.
    /// </summary>
    public static class CompletionAuthority
    {
        /// <summary>Token schema version tag. Anything else is refused.</summary>
        public const string SchemaTag = "ddc1";

        private const int NonceLength = 32;
        private const char Separator = ':';

        private static string s_liveToken;
        private static long s_liveOrdinal;

        /// <summary>True while a run's capability is live in this process.</summary>
        public static bool HasLiveCapability => s_liveToken != null;

        /// <summary>The live token, for diagnostics; null when none.</summary>
        public static string LiveToken => s_liveToken;

        /// <summary>The live ordinal, for diagnostics; 0 when none.</summary>
        public static long LiveOrdinal => s_liveToken != null ? s_liveOrdinal : 0;

        // ================================================================== issuing

        /// <summary>
        /// Issues the single live capability for the next run of the current profile
        /// and returns its token, or null when no valid identity can exist (the high-water
        /// mark is at <c>long.MaxValue</c> or the profile has no lineage). The previous
        /// capability, whatever its state, is invalidated. Loads the profile first, so
        /// the lineage and mark are the authoritative ones; a read-only session (newer
        /// file, other instance) still gets a token - the run is playable, and its
        /// transaction is refused by the store, not by the identity.
        /// </summary>
        public static string IssueForNewRun()
        {
            ProfileService.EnsureLoaded();
            ProfileData profile = ProfileService.Current;
            InvalidateLive();

            if (profile == null || !ProfileData.IsLineageId(profile.ProfileLineageId))
            {
                Debug.LogWarning("[Profile] No completion identity issued: the profile has no lineage.");
                return null;
            }

            if (profile.LastAppliedCompletionOrdinal >= long.MaxValue)
            {
                Debug.LogWarning("[Profile] No completion identity issued: the profile's completion ordinal is exhausted.");
                return null;
            }

            long ordinal = profile.LastAppliedCompletionOrdinal + 1;
            string nonce = Guid.NewGuid().ToString("N");
            string token = Compose(profile.ProfileLineageId, ordinal, nonce);
            s_liveToken = token;
            s_liveOrdinal = ordinal;
            return token;
        }

        /// <summary>Drops the live capability (abort, profile reset, a new issue). The profile is untouched.</summary>
        public static void InvalidateLive()
        {
            s_liveToken = null;
            s_liveOrdinal = 0;
        }

        /// <summary>Consumes the live capability once its transaction is on disk. No-op for any other token.</summary>
        public static void Consume(string token)
        {
            if (s_liveToken != null && string.Equals(s_liveToken, token, StringComparison.Ordinal))
            {
                InvalidateLive();
            }
        }

        // =============================================================== validation

        /// <summary>
        /// The full check a transaction runs BEFORE touching the profile, in the order of
        /// the package contract: format → schema → lineage → ordinal range → high-water
        /// mark → next-in-sequence → live capability.
        /// </summary>
        public static CompletionTokenVerdict Validate(string token, ProfileData profile, out CompletionToken parsed)
        {
            parsed = default;
            if (string.IsNullOrEmpty(token))
            {
                return CompletionTokenVerdict.Missing;
            }

            CompletionTokenVerdict format = TryParse(token, out parsed);
            if (format != CompletionTokenVerdict.Valid)
            {
                return format;
            }

            if (profile == null || !string.Equals(parsed.Lineage, profile.ProfileLineageId, StringComparison.Ordinal))
            {
                return CompletionTokenVerdict.ForeignLineage;
            }

            if (parsed.Ordinal <= profile.LastAppliedCompletionOrdinal)
            {
                return CompletionTokenVerdict.AlreadyApplied;
            }

            if (profile.LastAppliedCompletionOrdinal >= long.MaxValue || parsed.Ordinal != profile.LastAppliedCompletionOrdinal + 1)
            {
                return CompletionTokenVerdict.OrdinalGap;
            }

            if (s_liveToken == null || !string.Equals(s_liveToken, token, StringComparison.Ordinal))
            {
                return CompletionTokenVerdict.NotIssued;
            }

            return CompletionTokenVerdict.Valid;
        }

        /// <summary>
        /// Strict parse: exactly four colon-separated parts, the schema tag, a 32-hex
        /// lineage, a positive decimal ordinal with no sign/whitespace/leading zeros, a
        /// 32-hex nonce. A legacy raw GUID has one part and is <see cref="CompletionTokenVerdict.Malformed"/>.
        /// </summary>
        public static CompletionTokenVerdict TryParse(string token, out CompletionToken parsed)
        {
            parsed = default;
            if (string.IsNullOrEmpty(token))
            {
                return CompletionTokenVerdict.Missing;
            }

            string[] parts = token.Split(Separator);
            if (parts.Length != 4)
            {
                return CompletionTokenVerdict.Malformed;
            }

            if (!string.Equals(parts[0], SchemaTag, StringComparison.Ordinal))
            {
                // "ddc<n>" with another number is a schema this build does not know;
                // anything else is not a token at all.
                return parts[0].Length == 4 && parts[0].StartsWith("ddc", StringComparison.Ordinal) && char.IsDigit(parts[0][3])
                    ? CompletionTokenVerdict.UnsupportedVersion
                    : CompletionTokenVerdict.Malformed;
            }

            if (!ProfileData.IsLineageId(parts[1]) || !IsHex(parts[3], NonceLength))
            {
                return CompletionTokenVerdict.Malformed;
            }

            string ordinalText = parts[2];
            if (ordinalText.Length == 0 || ordinalText.Length > 19 || ordinalText[0] == '0' || !AllDigits(ordinalText))
            {
                return CompletionTokenVerdict.InvalidOrdinal;
            }

            long ordinal;
            if (!long.TryParse(ordinalText, NumberStyles.None, CultureInfo.InvariantCulture, out ordinal) || ordinal <= 0)
            {
                return CompletionTokenVerdict.InvalidOrdinal;
            }

            parsed = new CompletionToken(parts[1], ordinal, parts[3]);
            return CompletionTokenVerdict.Valid;
        }

        private static string Compose(string lineage, long ordinal, string nonce)
        {
            return SchemaTag + Separator + lineage + Separator + ordinal.ToString(CultureInfo.InvariantCulture) + Separator + nonce;
        }

        private static bool AllDigits(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            InvalidateLive();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Editor / development-build seam for tests and probes. Absent from a release
        /// Player: nothing there can mint, forge or re-arm a token.
        /// </summary>
        public static class TestSeam
        {
            /// <summary>The production issue path, for tests that build summaries by hand.</summary>
            public static string IssueForNextRun()
            {
                return IssueForNewRun();
            }

            /// <summary>Composes a token from raw parts WITHOUT registering it as live (forgery for negative tests).</summary>
            public static string Forge(string lineage, long ordinal, string nonce)
            {
                return Compose(lineage, ordinal, nonce);
            }

            /// <summary>Composes a token with a different schema tag (future / unknown schema tests).</summary>
            public static string ForgeWithTag(string tag, string lineage, long ordinal, string nonce)
            {
                return tag + Separator + lineage + Separator + ordinal.ToString(CultureInfo.InvariantCulture) + Separator + nonce;
            }

            /// <summary>Makes an arbitrary token the live capability (simulates a relaunch that re-arms nothing, or a forged issue).</summary>
            public static void ArmLive(string token)
            {
                CompletionToken parsed;
                if (TryParse(token, out parsed) == CompletionTokenVerdict.Valid)
                {
                    s_liveToken = token;
                    s_liveOrdinal = parsed.Ordinal;
                }
            }

            /// <summary>A fresh 32-hex nonce, for forged tokens.</summary>
            public static string NewNonce()
            {
                return Guid.NewGuid().ToString("N");
            }
        }
#endif
    }
}
