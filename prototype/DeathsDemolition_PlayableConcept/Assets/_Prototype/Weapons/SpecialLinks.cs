using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-069 × WP-077 §2.4 · the run tree's Special links (S-04 catalog, DEC-098; Q4 answered 25.09): a link node
    /// works FROM THE PICKUP of its Special TO THE END OF THAT ROUND (<see cref="IsOpen"/>; DEC-099 ends a Special in
    /// hand at the round end, so the window never outlives the round).
    ///
    ///   link.arc.conductor  (Raiju I)    the classic weapon's Chain +1 jump (once the Chain branch is owned).
    ///   link.arc.storm      (Raiju II)   each Raiju kill refills 10 % of the parked classic magazine
    ///                                    (down, ≥ 1, never above the magazine; the reserve untouched).
    ///   link.gel.slickfloor (Namekuji I) a body in gel (sliding / falling / downed / getting up) takes +15 % from
    ///                                    every source (ZombieEnemy.IncomingDamageScale).
    ///   link.gel.freeze     (Namekuji II, "Gel Harvest") a body that dies in gel drops +1 gold, added to the normal
    ///                                    kill reward (GoldService.KillBonus) — income, not a separate credit.
    ///
    /// The window also names the tree's link slots: the first Special picked up in a run reaches
    /// RunTreeState.OnSpecialPickedUp (nothing else calls it at run time). Values are the catalog's effectValue.
    /// Hooks install at load; no allocation per shot / kill.
    /// </summary>
    public static class SpecialLinks
    {
        private static readonly bool[] s_open = new bool[2];

        /// <summary>Test seam: answers "does the run own this effect?" instead of RunTreeEffects.Owned (null = the live run).</summary>
        public static Func<string, bool> OwnedOverride { get; set; }

        public static int StormRefills { get; private set; }
        public static int StormRounds { get; private set; }
        public static int SlickHits { get; private set; }
        public static int HarvestGold { get; private set; }

        /// <summary>The link window of <paramref name="id"/>: its pickup happened this round.</summary>
        public static bool IsOpen(SpecialWeaponId id)
        {
            int i = (int)id;
            return i >= 0 && i < s_open.Length && s_open[i];
        }

        private static bool Owned(string effectId)
        {
            Func<string, bool> o = OwnedOverride;
            return o != null ? o(effectId) : RunTreeEffects.Owned(effectId);
        }

        private static float ValueOf(string special, int level, float fallback)
        {
            RunTreeCatalogNode def = RunTreeCatalog.FindLink(special, level);
            return def != null ? def.effectValue : fallback;
        }

        // ------------------------------------------------------------------ effects

        /// <summary>Conductor: extra Chain jumps for the classic weapon right now (0 or 1).</summary>
        public static int ConductorBonusJumps()
        {
            return IsOpen(SpecialWeaponId.Raiju) && Owned(RunTreeEffects.LinkArcConductor) ? 1 : 0;
        }

        /// <summary>Storm: a Raiju kill landed (SpecialImpacts). Refills the parked classic magazine.</summary>
        public static int OnRaijuKill()
        {
            if (!IsOpen(SpecialWeaponId.Raiju) || !Owned(RunTreeEffects.LinkArcStorm)) return 0;
            WeaponController controller = WeaponRegistry.ActiveController;
            ClassicWeaponRuntime classic = controller != null ? controller.Classic : null;
            if (classic == null) return 0;
            int rounds = Mathf.Max(1, Mathf.FloorToInt(ValueOf(SpecialWeaponKeys.Raiju, 2, 0.10f) * classic.MagazineSize));
            int added = classic.AddRounds(rounds);
            if (added > 0)
            {
                StormRefills++;
                StormRounds += added;
            }

            return added;
        }

        /// <summary>Slick floor: the damage scale of a body right now (1 unless it lies / slides in gel with the link owned).</summary>
        public static float SlickScale(ZombieEnemy body)
        {
            if (body == null || body.Gel == GelState.None || !IsOpen(SpecialWeaponId.Namekuji) || !Owned(RunTreeEffects.LinkGelSlickFloor)) return 1f;
            SlickHits++;
            return 1f + Mathf.Max(0f, ValueOf(SpecialWeaponKeys.Namekuji, 1, 0.15f));
        }

        /// <summary>Gel harvest: the extra gold of a death (the body died in gel with the link owned).</summary>
        public static int HarvestBonus(EnemyDeathInfo info)
        {
            if (!IsOpen(SpecialWeaponId.Namekuji) || !Owned(RunTreeEffects.LinkGelFreeze) || info.Owner == null) return 0;
            var body = info.Owner.GetComponent<ZombieEnemy>();
            if (body == null || body.Gel == GelState.None) return 0;
            int bonus = Mathf.Max(0, Mathf.RoundToInt(ValueOf(SpecialWeaponKeys.Namekuji, 2, 1f)));
            HarvestGold += bonus;
            return bonus;
        }

        // ------------------------------------------------------------------ window

        /// <summary>Idempotent (remove-then-add on every bus): safe after a test cleared a bus, never two copies.</summary>
        public static void EnsureHooked()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            SpecialWeaponUsageEvents.Usage += OnUsage;
            SpecialWeaponRoundBridge.RoundEnded -= OnRoundEnded;
            SpecialWeaponRoundBridge.RoundEnded += OnRoundEnded;
            SpecialWeaponRoundBridge.RunStarted -= OnRunBoundary;
            SpecialWeaponRoundBridge.RunStarted += OnRunBoundary;
            SpecialWeaponRoundBridge.RunEnded -= CloseAll;
            SpecialWeaponRoundBridge.RunEnded += CloseAll;
            ZombieEnemy.IncomingDamageScale = SlickScale;
            GoldService.KillBonus = HarvestBonus;
        }

        private static void OnUsage(SpecialUsageEvent e)
        {
            if (e.Kind != SpecialUsageKind.Activated) return;
            int i = (int)e.Id;
            if (i < 0 || i >= s_open.Length) return;
            s_open[i] = true;
            RunDirector director = RunDirector.Instance;
            if (director != null && director.Shop != null) director.Shop.Tree.OnSpecialPickedUp(SpecialWeaponKeys.Of(e.Id));
        }

        private static void OnRoundEnded(int round) => CloseAll();

        private static void OnRunBoundary(int run) => CloseAll();

        /// <summary>Close every window (round end, run start / end, tests).</summary>
        public static void CloseAll()
        {
            for (int i = 0; i < s_open.Length; i++) s_open[i] = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CloseAll();
            OwnedOverride = null;
            StormRefills = 0;
            StormRounds = 0;
            SlickHits = 0;
            HarvestGold = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            EnsureHooked();
            // Re-arm on every active-scene change (the WP-072 AudioEventRouter.Rearm lesson): a bus a test reset stays wired.
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private static void OnActiveSceneChanged(Scene from, Scene to) => EnsureHooked();
    }
}
