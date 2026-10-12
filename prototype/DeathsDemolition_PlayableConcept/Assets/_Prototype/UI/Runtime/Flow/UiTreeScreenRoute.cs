using System;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-104 B4 (S-09) · THE one place the round-end screen picks its tree. <see cref="UpgradeScreen"/> asks
    /// <see cref="Resolve"/> once per round-end open:
    ///   * a provider → the feature-based tree screen (<see cref="UiTreeScreenV2"/>, KARAR-WP104-11.10 decision 1);
    ///   * none → the shipped WP-077 tree / card row (<see cref="UiRunTreePanel"/>), unchanged.
    /// Order: a capture / test <see cref="Override"/> (marked fake data, never in the player's path) → a test's
    /// <see cref="Provider"/> → the run's own tree, S-04 B1's <c>RoundUpgradeShop.TreeV2</c> (<see cref="RunTreeV2"/>) while it
    /// is built for this run and open for this round end → none.
    /// B1 (main 17f29c5d) builds RunTreeV2 only while <c>RoundUpgradeShop.TreeV2Active</c> (config <c>RunTreeV2</c>, off by
    /// default; dev <c>-ddTreeV2</c> / <c>-ddTreePreset</c>): with it off the round end is the shipped page / WP-077 panel, the
    /// player's path unchanged. The switch is S-04's config flag; the UI adds none.
    /// </summary>
    public static class UiTreeScreenRoute
    {
        private static ITreeShopCommands s_override;

        /// <summary>A test's stand-in for the run's shop (null = the run's own tree). Read once per open.</summary>
        public static Func<ITreeShopCommands> Provider;

        /// <summary>
        /// Capture / tests only: a provider that wins over the run's (the S-10 <c>-ddUiCapture tree</c> frames, PlayMode tests).
        /// Null clears it. Never set in the player's path.
        /// </summary>
        public static void Override(ITreeShopCommands shop)
        {
            s_override = shop;
        }

        public static bool Overridden => s_override != null;

        /// <summary>The run's own provider, without the override: the test seam, else B1's RunTreeV2 when built and open.</summary>
        public static ITreeShopCommands RunProvider()
        {
            Func<ITreeShopCommands> provider = Provider;
            if (provider != null)
            {
                return provider();
            }

            RunDirector director = RunDirector.Instance;
            RoundUpgradeShop shop = director != null ? director.Shop : null;
            return shop != null && shop.TreeV2.IsBuilt && shop.TreeV2.IsOpen ? shop.TreeV2 : null;
        }

        /// <summary>The tree the round-end screen shows: the override, else the run's provider, else null (the shipped tree).</summary>
        public static ITreeShopCommands Resolve()
        {
            return s_override ?? RunProvider();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_override = null;
            Provider = null;
        }
    }

    /// <summary>
    /// WP-104 (S-09) · the Awakening page's door (KARAR-WP104-11.10 decision 2; C-00: the link stays dead until S-04 B1).
    /// <see cref="LinkEnabled"/> - the Results "AWAKENING" control is built only while it is on: on by default since S-09 12.10
    /// (<see cref="LinkDefault"/>; B1 is in main since 17f29c5d), and a test can still set <see cref="LinkOverride"/>.
    /// <see cref="PurchaseEnabled"/> - the page's UNLOCK (a 500 ms hold) goes to S-04's meta command
    /// (<c>ProfileService.TryBuyMetaNode</c>, confirmed by S-04 12.10): on by default; off = the read-only page with the
    /// "not in this build yet" line. One page at a time; the host screen forwards its keys (<see cref="HandleKeys"/>).
    /// </summary>
    public static class UiAwakeningRoute
    {
        public const string DevFlag = "-ddAwakeningPage";

        /// <summary>Tests / integration: force the link on or off (null = the dev flag decides).</summary>
        public static bool? LinkOverride { get; set; }

        /// <summary>Tests / integration: force purchases on or off (null = <see cref="PurchaseDefault"/>).</summary>
        public static bool? PurchaseOverride { get; set; }

        /// <summary>The door's default: on (S-09 12.10 - B1a/B1b in main, the page buys through TryBuyMetaNode, and the Ash
        /// refund notice sends the player here). The main menu's rail gets no door (R2 U1 letters; a layout decision).</summary>
        public const bool LinkDefault = true;

        /// <summary>UNLOCK's default: S-04 confirmed TryBuyMetaNode as the Awakening command (12.10).</summary>
        public const bool PurchaseDefault = true;

        private static UiAwakeningPage s_open;

        public static bool LinkEnabled
        {
            get
            {
                if (LinkOverride.HasValue)
                {
                    return LinkOverride.Value;
                }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                return LinkDefault || Array.IndexOf(Environment.GetCommandLineArgs(), DevFlag) >= 0;
#else
                return LinkDefault;
#endif
            }
        }

        public static bool PurchaseEnabled => PurchaseOverride ?? PurchaseDefault;

        public static bool IsOpen => s_open != null && s_open.IsOpen;

        public static UiAwakeningPage Current => IsOpen ? s_open : null;

        /// <summary>Opens the page over <paramref name="screenRoot"/> (its canvas root); <paramref name="onClosed"/> runs when it closes.</summary>
        public static UiAwakeningPage Open(RectTransform screenRoot, Action onClosed = null, IAwakeningSource source = null)
        {
            if (screenRoot == null)
            {
                return null;
            }

            if (s_open == null || s_open.transform.parent != screenRoot)
            {
                s_open = UiAwakeningPage.Create(screenRoot);
            }

            s_open.Open(source ?? new ProfileAwakeningSource(PurchaseEnabled), onClosed);
            return s_open;
        }

        /// <summary>The host screen's Update forwards here while the page is open (T6: the page has no per-frame loop of its own).</summary>
        public static void HandleKeys()
        {
            if (IsOpen)
            {
                s_open.HandleKeys();
            }
        }

        public static void Close()
        {
            if (IsOpen)
            {
                s_open.Close();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_open = null;
            LinkOverride = null;
            PurchaseOverride = null;
        }
    }
}
