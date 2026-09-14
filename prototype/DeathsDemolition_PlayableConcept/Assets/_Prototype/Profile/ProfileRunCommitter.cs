using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Profile
{
    /// <summary>
    /// The one listener that turns a finished run into profile progress (WP-009).
    ///
    /// Before this class the call sat in <c>PrototypeUiFlow.OnRunEnded</c> behind a
    /// RAM-only navigation flag. That flag still exists - it stops the flow from
    /// scheduling two trips to Results - but it is not, and never was, what makes the
    /// profile commit exactly-once. That guarantee lives in
    /// <see cref="ProfileService.ApplyRun"/> (the durable completion identity recorded in
    /// the saved profile); this class only makes sure the transaction is attempted for
    /// every <see cref="RunEvents.RunEnded"/>, independent of any screen, scene or
    /// coroutine. A duplicate event, a re-opened Results screen, a scene rebind or a
    /// retry all reach the same idempotent transaction.
    ///
    /// The commit is SYNCHRONOUS inside the event: if the player alt-F4s during the
    /// death beat, the run is already on disk. Only the navigation waits.
    ///
    /// Abort (pause → main menu) never publishes RunEnded, so nothing here runs for it -
    /// the B5 characterisation this package preserves.
    /// </summary>
    public static class ProfileRunCommitter
    {
        private static bool s_installed;
        private static string s_lastCommittedId;

        /// <summary>How many RunEnded events reached the committer this session (tests / F1).</summary>
        public static int EventsSeen { get; private set; }

        /// <summary>How many of those became a committed transaction (Applied).</summary>
        public static int Commits { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;
            RunEvents.RunEnded += OnRunEnded;
        }

        private static void OnRunEnded(RunSummary summary)
        {
            Commit(summary);
        }

        /// <summary>The event adapter. Public so a test can drive it without the director.</summary>
        public static RunRewardReport Commit(in RunSummary summary)
        {
            EventsSeen++;
            RunRewardReport report = ProfileService.ApplyRun(summary);

            // A duplicate hands back the ORIGINAL receipt (Applied == true); only a new
            // identity counts as a commit here.
            if (report != null && report.Applied &&
                !string.Equals(report.CompletionId, s_lastCommittedId, System.StringComparison.Ordinal))
            {
                s_lastCommittedId = report.CompletionId;
                Commits++;
            }

            return report;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_installed = false;
            s_lastCommittedId = null;
            EventsSeen = 0;
            Commits = 0;
        }
    }
}
