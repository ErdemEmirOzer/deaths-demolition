using System;

namespace DD.Prototype.UI
{
    /// <summary>Tip classes (UXW §1): FACT = a rule, every run; HINT = onboarding, only while hints are on.</summary>
    public enum UiTipClass
    {
        Fact = 0,
        Hint = 1
    }

    /// <summary>The five tip groups; the selector round-robins across them.</summary>
    public enum UiTipGroup
    {
        Controls = 0,
        Economy = 1,
        Special = 2,
        Tiers = 3,
        Survival = 4
    }

    /// <summary>One loading tip: key, group, class, the string (≤ 90 characters, verified against code - council/loading-ux-writer.md §1).</summary>
    public readonly struct UiTip
    {
        public readonly string Key;
        public readonly UiTipGroup Group;
        public readonly UiTipClass Class;
        public readonly string Text;

        /// <summary>Bit in the once-per-machine HINT mask (−1 for FACT).</summary>
        public readonly int HintBit;

        public UiTip(string key, UiTipGroup group, UiTipClass cls, string text, int hintBit = -1)
        {
            Key = key;
            Group = group;
            Class = cls;
            Text = text;
            HintBit = hintBit;
        }
    }

    /// <summary>
    /// WP-068 slice 10b · the U9 loading tip set and its selection rules (direction §15.4,
    /// UXW §1.1). While hints are on (fewer than three runs played and Settings HINTS = ON)
    /// an unseen HINT comes first, each HINT once per machine (a bit mask in ui-hints.json);
    /// then FACT. Hints off: FACT only. Round-robin across the five groups, no repeat within
    /// a session, the first loading screen of a session never shows a survival tip. A tip is
    /// picked once per loading screen and never swapped mid-load. Boot, Results and the
    /// Pause abort show no tip (the caller asks only for the other transitions).
    /// R4 WP-105 (KOD MUST-4, D5): every tip checked against this build again. Retired - never shown again, their keys
    /// stay in the pack, their HINT bits are never reused (<see cref="RetiredHintBits"/>: a player who saw the old tip
    /// would otherwise miss its replacement):
    ///   ctrl.01  "every classic weapon is automatic": Single and Burst weapons exist (WP-069 B2); holding fire still
    ///            refires them at their own cycle (WeaponController) - ctrl.05;
    ///   econ.01  "up to two of them": rounds 10-13 allow three (DEC-107, EconomyConfigSection.LatePurchasesPerRound) - econ.06;
    ///   sw.01    "no ammo and no reload": both Specials are magazine weapons that reload (DEC-097, SpecialWeaponRuntime);
    ///   sw.03, sw.04, sw.05, tier.04: the Kagura Bell, the Ofuda seals and the Tide Jewel are not in this build (DEC-094:
    ///            exactly two Specials, Raiju and Namekuji).
    /// The Special group's new rows are the R1 content pack's own loading tips (tips.011/012/015/017: already in both pack
    /// copies, their EN column is the text here) and one new HINT, sw.07 (Raiju: WD_sw_arc chargeSeconds 0.25,
    /// arcChain.maxTargets 8).
    /// </summary>
    public static class UiLoadingTips
    {
        public const int MaxLength = 90;
        public const int HintRuns = 3;

        /// <summary>R4 WP-105: the HINT bits of the retired tips (sw.03, sw.04, sw.05, tier.04). Never given to a new tip.</summary>
        public static readonly int[] RetiredHintBits = { 1, 2, 3, 6 };

        public static readonly UiTip[] All =
        {
            new UiTip("loading.tip.ctrl.05", UiTipGroup.Controls, UiTipClass.Fact, "Holding fire keeps firing. Single-shot and burst weapons refire at their own pace."),
            new UiTip("loading.tip.ctrl.02", UiTipGroup.Controls, UiTipClass.Hint, "Holding fire on an empty magazine starts the reload.", 0),
            new UiTip("loading.tip.ctrl.03", UiTipGroup.Controls, UiTipClass.Fact, "Keys and buttons can be remapped under CONTROLS. Overrides stay on this machine."),
            new UiTip("loading.tip.ctrl.04", UiTipGroup.Controls, UiTipClass.Fact, "There is no dash and no dodge. Footwork is the only evasion."),
            new UiTip("loading.tip.econ.06", UiTipGroup.Economy, UiTipClass.Fact, "Each round ends with three upgrade cards. Buy up to two, or three in rounds 10 to 13."),
            new UiTip("loading.tip.econ.02", UiTipGroup.Economy, UiTipClass.Fact, "One paid reroll per round, only before a purchase. It swaps one or two cards."),
            new UiTip("loading.tip.econ.03", UiTipGroup.Economy, UiTipClass.Fact, "Unspent gold carries into the next round."),
            new UiTip("loading.tip.econ.04", UiTipGroup.Economy, UiTipClass.Fact, "Gold still on the ground when a round is cleared is collected on its own."),
            new UiTip("loading.tip.econ.05", UiTipGroup.Economy, UiTipClass.Fact, "Armour an enemy spawned with adds gold. Breaking the plate never lowers the reward."),
            new UiTip("tips.011", UiTipGroup.Special, UiTipClass.Fact, "A Special carries its own ammunition. When it runs dry, your classic weapon returns."),
            new UiTip("tips.012", UiTipGroup.Special, UiTipClass.Fact, "A Special also has a time limit. Spend its ammunition before the limit does."),
            new UiTip("loading.tip.sw.02", UiTipGroup.Special, UiTipClass.Fact, "Round-end upgrades change the classic weapon only. A Special Weapon is untouched."),
            new UiTip("tips.017", UiTipGroup.Special, UiTipClass.Fact, "You slide fast on Namekuji gel and steer poorly. Use it to escape, not to aim."),
            new UiTip("loading.tip.sw.07", UiTipGroup.Special, UiTipClass.Hint, "Hold fire to charge Raiju. The orb's hit starts a chain of up to eight enemies.", 9),
            new UiTip("tips.015", UiTipGroup.Special, UiTipClass.Hint, "Release fire while Raiju is charging and the shot is cancelled. No ammo is spent.", 10),
            new UiTip("loading.tip.sw.06", UiTipGroup.Special, UiTipClass.Hint, "A drop's beacon blinks faster as its ground timer runs out.", 4),
            new UiTip("loading.tip.tier.01", UiTipGroup.Tiers, UiTipClass.Fact, "A zombie deals damage only when its swing lands. Contact alone does nothing."),
            new UiTip("loading.tip.tier.02", UiTipGroup.Tiers, UiTipClass.Fact, "Special, Elite and Ultra versions each arrive on a set round, then join the pool."),
            new UiTip("loading.tip.tier.03", UiTipGroup.Tiers, UiTipClass.Hint, "An exposed head takes more damage than the body. A helmet covers it until it breaks.", 5),
            new UiTip("loading.tip.tier.05", UiTipGroup.Tiers, UiTipClass.Hint, "BEHIND YOU means an attack is winding up outside your view.", 7),
            new UiTip("loading.tip.surv.01", UiTipGroup.Survival, UiTipClass.Fact, "Health regenerates after a few seconds without damage. A new hit resets the wait."),
            new UiTip("loading.tip.surv.02", UiTipGroup.Survival, UiTipClass.Fact, "There is no invulnerability window. Two hits in one moment both count in full."),
            new UiTip("loading.tip.surv.03", UiTipGroup.Survival, UiTipClass.Fact, "One life. When health reaches zero, the run ends and the results screen follows."),
            new UiTip("loading.tip.surv.04", UiTipGroup.Survival, UiTipClass.Hint, "During NEXT ROUND IN the world keeps moving. The gap is for repositioning.", 8),
        };

        /// <summary>
        /// The stall notice (10 s without progress; direction §15.2). R4 WP-105 (V-49): it says what comes next - the load
        /// goes on by itself, and only if it stays stuck does the error with its exits appear. One line at TextScale 1.25
        /// (it hangs under the tip strip). Pack key loading.stalled.wait (the old loading.stalled row said nothing about
        /// what happens next).
        /// </summary>
        public const string StalledLine = "Still loading. If it stays stuck, a way out appears.";

        // ------------------------------------------------------------------ session state

        private static readonly bool[] s_shownThisSession = new bool[All.Length];
        private static int s_nextGroup;
        private static bool s_firstScreenDone;

        /// <summary>Forget the session (tests; SubsystemRegistration in a Player).</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetSession()
        {
            Array.Clear(s_shownThisSession, 0, s_shownThisSession.Length);
            s_nextGroup = 0;
            s_firstScreenDone = false;
        }

        /// <summary>
        /// Pick the tip for one loading screen. <paramref name="hintsOn"/> = HINTS setting and
        /// fewer than <see cref="HintRuns"/> runs played; <paramref name="hintMask"/> = HINTs
        /// already seen on this profile (updated when a HINT is chosen). Returns -1 when there
        /// is nothing left to show this session.
        /// </summary>
        public static int Pick(bool hintsOn, ref int hintMask)
        {
            bool first = !s_firstScreenDone;
            s_firstScreenDone = true;

            if (hintsOn)
            {
                int seen = hintMask; // a ref parameter cannot be captured by the lambda
                int hint = NextFromGroups(first, i => All[i].Class == UiTipClass.Hint && (seen & (1 << All[i].HintBit)) == 0);
                if (hint >= 0)
                {
                    hintMask |= 1 << All[hint].HintBit;
                    return hint;
                }
            }

            return NextFromGroups(first, i => All[i].Class == UiTipClass.Fact);
        }

        /// <summary>Round-robin: the next group (from the rotating start) that still has an eligible, unshown tip.</summary>
        private static int NextFromGroups(bool first, Func<int, bool> eligible)
        {
            const int groups = 5;
            for (int g = 0; g < groups; g++)
            {
                var group = (UiTipGroup)((s_nextGroup + g) % groups);
                if (first && group == UiTipGroup.Survival)
                {
                    continue; // the first screen of a session: the player has not fought yet
                }

                for (int i = 0; i < All.Length; i++)
                {
                    if (All[i].Group != group || s_shownThisSession[i] || !eligible(i))
                    {
                        continue;
                    }

                    s_shownThisSession[i] = true;
                    s_nextGroup = ((int)group + 1) % groups;
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Pick with the live settings / profile, persisting the HINT mask when it changes. Returns the text, or null.</summary>
        public static string PickForLoadingScreen()
        {
            int runs = DD.Prototype.Profile.ProfileService.Current != null ? DD.Prototype.Profile.ProfileService.Current.RunsPlayed : 0;
            bool hintsOn = UiSettings.Hints && runs < HintRuns;
            int mask = UiHintState.Seen;
            int index = Pick(hintsOn, ref mask);
            if (mask != UiHintState.Seen)
            {
                UiHintState.Seen = mask;
            }

            return index >= 0 ? UiStrings.Get(All[index].Key, All[index].Text) : null;   // WP-068 TR: the pack row under the tip's key
        }
    }
}
