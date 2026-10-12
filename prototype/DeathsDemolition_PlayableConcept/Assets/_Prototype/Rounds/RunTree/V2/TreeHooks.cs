// WP-104 B2 · the tree hook BODIES (S-04, 12.10). This file replaces the B1b stub in place (same path, same GUID):
// Assets/_Prototype/Rounds/RunTree/V2/TreeHooks.cs. Public signatures are the stub's, plus OverclockResetSeconds / SustainedFireSeconds.
using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// What the tree changes about how a shot FEELS (WP-104 B2, GDEV (a) risk 3). The weapon reads it next to its own
    /// <see cref="WeaponPower"/>: a converted weapon (Full-Auto Conversion on a single / burst gun) fires up to 20 times a second
    /// and must not punch the camera, freeze the world or throw the heavy blast shake on every round.
    /// </summary>
    [Flags]
    public enum TreeFeelOverride : byte
    {
        None = 0,
        /// <summary>No ShotgunFire / HeavyFire directional kick per round (REQ-126 camera drift).</summary>
        NoHeavyFireShake = 1 << 0,
        /// <summary>No top-down global hit-stop per round (REQ-127: automatic fire stays out of hit-stop).</summary>
        NoHitStop = 1 << 1,
        /// <summary>Rockets: the blast's own shake / Heavy presentation is reduced (S-07 / S-08 limiter decides how).</summary>
        LightBlastShake = 1 << 2
    }

    /// <summary>What the tree did to one hit on the player (<see cref="TreeHooks.IncomingDamage"/>).</summary>
    public enum TreeHitOutcome : byte
    {
        /// <summary>No tree effect: the damage passes unchanged.</summary>
        None = 0,
        /// <summary>Undying Oath's grace window: the hit is ignored.</summary>
        Invulnerable = 1,
        /// <summary>Phantom Step's ghost window after a dodge: the hit is ignored.</summary>
        Ghost = 2,
        /// <summary>Phantom Step rolled a dodge: the hit is ignored and a ghost window opens.</summary>
        Dodged = 3,
        /// <summary>Spirit Ward took all or part of the hit and still holds.</summary>
        ShieldAbsorbed = 4,
        /// <summary>Spirit Ward took what it had left and broke; the rest passes.</summary>
        ShieldBroken = 5,
        /// <summary>The hit was fatal and Undying Oath caught it: health is set to <see cref="TreeHitResult.HealthFloor"/>.</summary>
        Undying = 6
    }

    /// <summary>
    /// One hit after the Survival branch. The caller applies it in one line:
    /// <c>_current = Mathf.Max(hit.HealthFloor, before - hit.Damage);</c> (HealthFloor is 0 unless Undying Oath fired).
    /// </summary>
    public struct TreeHitResult
    {
        /// <summary>Damage left for health (≥ 0).</summary>
        public float Damage;
        /// <summary>Damage the shield took.</summary>
        public float Absorbed;
        /// <summary>Undying Oath: the health the player keeps (0 otherwise).</summary>
        public float HealthFloor;
        public TreeHitOutcome Outcome;
    }

    /// <summary>
    /// The player's Survival state the health component owns (one field on PlayerHealth): the shield pool, its regen delay, the
    /// dodge ghost and Undying windows and the dodge roll's deterministic random state. A plain struct, no allocation.
    /// </summary>
    public struct TreeShieldState
    {
        public float Shield;
        /// <summary>Time of the last hit that landed (the shield's regen delay counts from it).</summary>
        public float LastHitTime;
        public float GhostUntil;
        public float InvulnerableUntil;
        /// <summary>Stone Guardian: the break wave is ready again at this time.</summary>
        public float BulwarkReadyAt;
        /// <summary>xorshift32 state of the dodge roll (0 = seeded on first use).</summary>
        public uint Rng;

        public static TreeShieldState Create(uint seed) => new TreeShieldState { Rng = seed, LastHitTime = float.NegativeInfinity };
    }

    /// <summary>
    /// The plan of one trigger pull (design K4 + §2.9, K6), built once per pull by <c>TreeHooks.PlanVolley</c>:
    /// <see cref="Count"/> volleys (directions), each firing <see cref="PelletsPerVolley"/> pellets. Over a cap (pellets × volleys ≤
    /// <see cref="TreeGuardrails.ShotsPerPull"/>, a pellet weapon's 5-way fan ≤ pellets × 1.6, spawns ≤
    /// <see cref="TreeGuardrails.ProjectileSpawnsPerSecond"/>) shots MERGE: fewer, thicker, harder projectiles carrying the same total
    /// damage (<see cref="MergedDamageScale"/>, <see cref="MergedSizeScale"/>). A plain value, no allocation.
    /// </summary>
    public struct TreeVolleyPlan
    {
        /// <summary>Volleys (directions) this pull, 1 … 1 + <see cref="TreeGuardrails.ExtraShotsMax"/>.</summary>
        public int Count;
        /// <summary>Pellets each volley fires (the weapon's pellets unless merged).</summary>
        public int PelletsPerVolley;
        /// <summary>Damage share of an extra volley (the main volley is 1).</summary>
        public float ExtraDamage;
        /// <summary>Total fan arc in degrees.</summary>
        public float ArcDegrees;
        /// <summary>K6: factor on every projectile of the pull so the merged pull deals the unmerged pull's damage (1 = no merge).</summary>
        public float MergedDamageScale;
        /// <summary>K6: a merged projectile is thicker (sweep radius / tracer; ≤ <see cref="TreeHooks.MergedSizeMax"/>; 1 = no merge).</summary>
        public float MergedSizeScale;
        /// <summary>WP-104 (S-07 K3): this pull carries the chaos core's next hit (Mini Nuke armed, or the Black Hole off cooldown):
        /// S-02 sets <c>RunTreeHooks.TreeRoundMarkedChaos</c> on its main projectile so S-07 draws the marked round.</summary>
        public bool Marked;

        /// <summary>Projectiles this pull.</summary>
        public int TotalShots => Count * PelletsPerVolley;

        public bool Merged => MergedDamageScale > 1f + 1e-4f;

        /// <summary>Damage factor of every pellet of volley <paramref name="volley"/>: its share × the merge factor.</summary>
        public float DamageScale(int volley) => (volley <= 0 ? 1f : ExtraDamage) * MergedDamageScale;

        public float YawDegrees(int volley) => TreeHooks.YawOf(volley, Count, ArcDegrees);

        /// <summary>One volley of the weapon's own pellets at ×1 (no tree).</summary>
        public static TreeVolleyPlan Single(int pellets) => new TreeVolleyPlan
        {
            Count = 1, PelletsPerVolley = Math.Max(1, pellets), ExtraDamage = 0f, ArcDegrees = 0f, MergedDamageScale = 1f, MergedSizeScale = 1f
        };
    }

    /// <summary>
    /// WP-104 B2 · the feature-based run tree's gameplay hooks (S-04 owns the bodies; S-02 / PlayerHealth / WP-107 call them in one
    /// line from their existing paths, K/wp-104/b2/S02-CALLSITES.md). Every hook reads <see cref="TreeRuntime.Current"/> by
    /// <c>ref readonly</c> (no copy, no allocation); with <see cref="TreeStats.Neutral"/> every hook returns the shipped value and
    /// consumes no random number, so wiring a call changes nothing until a node is owned. Hooks with logic run inside
    /// <see cref="TreeMarkers.ShotHooks"/> / <see cref="TreeMarkers.HitHooks"/> / <see cref="MagazineMarker"/>; single-field reads are
    /// not wrapped (the marker would cost more than the read). Small run state lives in static struct fields, cleared by
    /// <see cref="ResetRunState"/> at every run boundary.
    /// <para>
    /// <b>B2 integration checklist — node → hook.</b> A node goes into <see cref="TreeLiveV2"/> in the commit that wires the LAST
    /// hook it needs (the WP-077 rule: never sell an effect nobody reads). "+" marks a part owned by another slice.
    /// </para>
    /// <list type="table">
    /// <listheader><term>Node</term><description>Hook(s) · call site owner</description></listheader>
    /// <item><term>sh_heavy</term><description><see cref="ShotDamageScale"/> · S-02 CWR.BuildShot</description></item>
    /// <item><term>sh_bore</term><description><see cref="ProjectileSizeScale"/> (sweep radius + tracer) + <see cref="ShotDamageScale"/> · S-02 CWR.Launch/BuildShot (+ S-07 trace width)</description></item>
    /// <item><term>sh_double, sh_triple, sh_fan</term><description><c>PlanVolley</c> (or <see cref="VolleyCount"/> / <see cref="VolleyYawDegrees"/> / <see cref="VolleyDamageScale"/>) · S-02 WC.FireClassicOnce</description></item>
    /// <item><term>sh_pierce</term><description><see cref="PierceCount"/> + <see cref="PierceKeep"/> · S-02 CWR.BuildShot / HSE</description></item>
    /// <item><term>sh_rail</term><description><see cref="PierceCount"/> + <see cref="PierceKeep"/> + PierceStaggerSeconds (S-03 stagger receiver, B3)</description></item>
    /// <item><term>sh_ricochet, sh_storm</term><description><see cref="RicochetBounces"/> + <see cref="RicochetChance"/> + <see cref="RicochetRadius"/> + <see cref="RicochetKeep"/> + <see cref="VolleyCarriesRicochet"/> (+ <see cref="SeekDegrees"/> for storm) · S-02 CWR.BuildShot / HSE</description></item>
    /// <item><term>sh_explosive</term><description><see cref="ExplosiveRound"/> / <see cref="OwnBlastRadiusScale"/> · S-02 HSE + TreeBlastQueue (B3)</description></item>
    /// <item><term>sh_cap</term><description><see cref="SeekDegrees"/> (homing projectile, S-02 PS) + <c>PlanVolley</c> extra damage</description></item>
    /// <item><term>mg_ext, mg_double</term><description><see cref="MagazineSize"/> + <see cref="ReserveAllowance"/> · S-02 CWR.MagazineSize / ReserveAllowance (+ S-09 HUD ladder)</description></item>
    /// <item><term>mg_drum</term><description><see cref="MagazineSize"/> + tail damage in <see cref="ShotDamageScale"/> · S-02</description></item>
    /// <item><term>mg_reload1, mg_reload2</term><description><see cref="ReloadTimeScale"/> · S-02 CWR.ReloadMultiplier</description></item>
    /// <item><term>mg_reload3</term><description><see cref="ReloadTimeScale"/> + <see cref="ReloadMoveFree"/> · S-02 CWR.MoveSpeedMultiplier</description></item>
    /// <item><term>mg_autoload</term><description><see cref="AutoReloadTick"/> · S-02 CWR.Tick</description></item>
    /// <item><term>mg_endless</term><description><see cref="OnKill"/> (installed here) + <see cref="ConsumesAmmo"/> / <see cref="NoReloadWindow"/> + <see cref="Tick"/> · S-02 CWR.ConsumeShot, WC.Update</description></item>
    /// <item><term>mg_cap</term><description>as mg_endless + window rpm in <see cref="FireRateScale"/> · S-02 CWR.EffectiveFireRate / ConsumeShot</description></item>
    /// <item><term>sp_rate1, sp_rate2, sp_rate3</term><description><see cref="FireRateScale"/> · S-02 CWR.EffectiveFireRate + ConsumeShot</description></item>
    /// <item><term>sp_move</term><description><see cref="MoveScale"/> + <see cref="FiringMoveFactor"/> · PlayerMotor velocity line + S-02 CWR.MoveSpeedMultiplier</description></item>
    /// <item><term>sp_dash</term><description><see cref="MoveScale"/> (kill boost reads <see cref="OnKill"/>) · PlayerMotor</description></item>
    /// <item><term>sp_fullauto</term><description><see cref="FireMode"/> + <see cref="PowerOverride"/> / <see cref="ShotPower"/> + <see cref="FireRateScale"/> + <see cref="ShotDamageScale"/> + <see cref="MagazineSize"/> + <see cref="ReserveAllowance"/> + <see cref="ReloadTimeScale"/> (rl_1 box 3.0 s) + <see cref="RocketSelfImmune"/> + <see cref="OnClassicEquipping"/> · S-02 (+ S-06 mechanism anim, S-08 limiter)</description></item>
    /// <item><term>sp_twin</term><description><see cref="FireRateScale"/> + <see cref="ConsumesAmmo"/> / <see cref="TwinFreeRound"/> · S-02 (+ S-06 E3 second barrel)</description></item>
    /// <item><term>sp_cap</term><description><see cref="FireRateScale"/> + <see cref="OnShotFired"/> (Overclock clock, OverclockPeak proc) · S-02</description></item>
    /// <item><term>kx_crit</term><description><see cref="RollCrit"/> once per pull · S-02 WC.FireClassicOnce (+ ShotRequest crit field, DamageHitContext mark)</description></item>
    /// <item><term>kx_crit2</term><description><see cref="RollCrit"/> + crit-kill ring (B3 TreeBlastQueue)</description></item>
    /// <item><term>sv_hp1, sv_hp2, sv_hp3</term><description><see cref="MaxHealthScale"/> + <see cref="RegenDelayScale"/> · PlayerHealth.ApplyConfig (+ StatsChanged re-apply)</description></item>
    /// <item><term>sv_shield</term><description><see cref="IncomingDamage"/> + <see cref="ShieldTick"/> · PlayerHealth (+ S-09 shield bar, VFX gap AP-041)</description></item>
    /// <item><term>sv_bulwark</term><description>as sv_shield + ShieldBreak proc radius (B3 push / stun listener)</description></item>
    /// <item><term>sv_leech</term><description><see cref="OnKillHeal"/> · PlayerHealth EnemyDied listener</description></item>
    /// <item><term>sv_dodge</term><description><see cref="IncomingDamage"/> (dodge + ghost; collision-less ghost = PlayerMotor, open)</description></item>
    /// <item><term>sv_cap</term><description><see cref="IncomingDamage"/> (Undying) + <see cref="OnRoundStarted"/> (installed here) (+ 6 m push, B3)</description></item>
    /// <item><term>ec_dealer</term><description><see cref="WallBuyPrice"/> + <see cref="AmmoPrice"/> · WP-107 WallBuyRules.Quote</description></item>
    /// <item><term>ec_midas</term><description><see cref="ShotDamageScale"/> (carried gold) · S-02 CWR.BuildShot</description></item>
    /// <item><term>ec_gold3 (magnet)</term><description><see cref="MagnetRadiusScale"/> · S-04 GoldService.MagnetRadius (gold × is live since B1)</description></item>
    /// <item><term>el_*, kx_chain, kx_nuke, kx_hole, kx_mass, kx_cap, aw_alt_*</term><description>B3 (TreeStatusSystem / TreeBlastQueue / TreeGravityWells) and B4 — not here</description></item>
    /// </list>
    /// </summary>
    public static partial class TreeHooks
    {
        /// <summary>Magazine-side hooks (auto-reload, windows, reserve). Move to <see cref="TreeMarkers"/> when the contract takes it.</summary>
        public static readonly ProfilerMarker MagazineMarker = new ProfilerMarker("WP104.Tree.Magazine");

        /// <summary>GD-TREE §2.9: a pellet weapon's 5-way fan fires at most this many times its pellets per pull (8 → 13); the rest becomes damage.</summary>
        public const float PelletFanShare = 1.6f;

        /// <summary>GD-TREE sp_cap Overclock: one rpm step per this much sustained fire.</summary>
        public const float OverclockPeriodSeconds = 0.5f;

        /// <summary>GD-TREE sp_cap Overclock: a pause in fire longer than this resets the build-up (TreeHooks' own clock, <see cref="OnShotFired"/>).</summary>
        public const float OverclockResetSeconds = 1f;

        /// <summary>The window in which <see cref="TreeStats.LeechCapPerSecond"/> is measured.</summary>
        public const float LeechWindowSeconds = 1f;

        private const float Epsilon = 1e-4f;

        // ------------------------------------------------------------------ run state (static structs, cleared per run)

        private struct ShotState
        {
            public int PullsSinceCrit;
            public int KillsTowardWindow;
            public float WindowUntil;
            public bool WindowOpen;
            public float LastKillTime;
            public bool OverclockPeaked;
            public float LastShotTime;
            public float SustainStart;
            public Vector3 LastMuzzle;
            // B6 awakening alternatives
            public int PullCount;
            public bool FountainRefill;
            public float FountainUntil, BlinkUntil, RainUntil;
            public int RainSpentMark;
            public uint FountainRng;
        }

        private struct BodyState
        {
            public int UndyingUsedThisRound;
            public bool PhoenixUsed;   // B6 Phoenix: once per RUN
            public float LeechWindowStart;
            public float LeechInWindow;
        }

        private struct ReloadCache
        {
            public bool Valid;
            public int Version;
            public ClassicWeaponId Weapon;
            public float Seconds;
        }

        private static ShotState s_shot = NewShotState();
        private static BodyState s_body = NewBodyState();
        private static ReloadCache s_reload;
        private static TreeWeaponProfile s_inHand;

        /// <summary>Raised by <see cref="OnClassicEquipping"/> after the run tree re-resolved for the new weapon (HUD, tests).</summary>
        public static event Action<TreeWeaponProfile> Equipping;

        /// <summary>The weapon in hand as the tree last heard of it (<see cref="OnClassicEquipping"/>).</summary>
        public static ref readonly TreeWeaponProfile InHand => ref s_inHand;

        /// <summary>Endless Window: kills counted towards the next window (tests / F1).</summary>
        public static int EndlessKillCount => s_shot.KillsTowardWindow;

        /// <summary>Bad-luck guard: pulls since the last crit (tests / F1).</summary>
        public static int PullsSinceCrit => s_shot.PullsSinceCrit;

        /// <summary>Undying Oath: uses this round (tests / F1).</summary>
        public static int UndyingUsedThisRound => s_body.UndyingUsedThisRound;

        private static ShotState NewShotState() => new ShotState
        {
            LastKillTime = float.NegativeInfinity, WindowUntil = float.NegativeInfinity, LastShotTime = float.NegativeInfinity, SustainStart = float.NegativeInfinity,
            FountainUntil = float.NegativeInfinity, BlinkUntil = float.NegativeInfinity, RainUntil = float.NegativeInfinity, RainSpentMark = -1,
            FountainRng = 0x9E3779B9u
        };

        private static BodyState NewBodyState() => new BodyState { LeechWindowStart = float.NegativeInfinity };

        // ------------------------------------------------------------------ equip

        /// <summary>
        /// The classic weapon is about to be equipped (WC.EquipClassicCore BEFORE <c>_classic.Equip</c>, wall buy included): the run
        /// tree re-resolves its stats for it synchronously (RunTreeV2.OnEquip, while the V2 tree is built), so the magazine and the
        /// reserve the weapon fills with are the NEW weapon's. The weapon-bound hook state is cleared: the Endless window
        /// (the kill count towards the next one is kept — kills are earned), the Overclock peak edge, the reload cache.
        /// </summary>
        public static void OnClassicEquipping(in TreeWeaponProfile weapon)
        {
            s_inHand = weapon;
            s_reload.Valid = false;
            s_shot.OverclockPeaked = false;
            s_shot.LastShotTime = float.NegativeInfinity;
            s_shot.SustainStart = float.NegativeInfinity;
            if (s_shot.WindowOpen)
            {
                s_shot.WindowOpen = false;
                s_shot.WindowUntil = float.NegativeInfinity;
                Raise(TreeProcKind.EndlessWindowClose, default, 0f, TreeBranch.Magazine, weapon.Id);
            }

            RunDirector run = RunDirector.Instance;
            RunTreeV2 tree = run != null && run.Shop != null ? run.Shop.TreeV2 : null;
            if (tree != null && tree.IsBuilt) tree.OnEquip(weapon);
            Equipping?.Invoke(weapon);
        }

        // ------------------------------------------------------------------ shot: volley

        /// <summary>K6: the largest thickening a merged projectile gets (GD §2.9 merged rockets: radius ×1.25).</summary>
        public const float MergedSizeMax = 1.25f;
        /// <summary>A magazine this small takes the magazine nodes as reload speed (rl_1 1, sg_2 2).</summary>
        public const int TinyMagazine = 2;

        /// <summary>The pull plan without a spawn-rate cap (tests, a single pull). Call ONCE per trigger pull: it also counts <see cref="TreeEvents.ShapedShots"/>.</summary>
        public static void PlanVolley(in TreeWeaponProfile weapon, int pellets, out TreeVolleyPlan plan) => PlanVolley(weapon, pellets, 0f, out plan);

        /// <summary>
        /// The whole pull in one call (K4: the whole shot incl. pellets repeats per extra shot). <paramref name="pellets"/> is the
        /// weapon's pellets this pull (CWR.PelletCount); <paramref name="pullsPerSecond"/> (CWR.EffectiveFireRate, 0 = unknown)
        /// feeds the spawn cap. Caps, in order: extra shots ≤ <see cref="TreeGuardrails.ExtraShotsMax"/>; a pellet weapon's 5-way
        /// fan ≤ pellets × <see cref="PelletFanShare"/>; pellets × volleys ≤ <see cref="TreeGuardrails.ShotsPerPull"/>; spawns ≤
        /// <see cref="TreeGuardrails.ProjectileSpawnsPerSecond"/>. Pellets merge first, then volleys; the total damage never drops.
        /// Neutral: <see cref="TreeVolleyPlan.Single"/>.
        /// </summary>
        public static void PlanVolley(in TreeWeaponProfile weapon, int pellets, float pullsPerSecond, out TreeVolleyPlan plan)
        {
            pellets = Math.Max(1, pellets);
            ref readonly TreeStats s = ref TreeRuntime.Current;
            int n = VolleyCountOf(s);
            // B6 Ring of Ruin (awakening, Shot V): every Nth pull is a 360° ring of RingShots at RingShare (the budget caps still hold).
            s_shot.PullCount++;
            bool ring = s.RingEvery > 0 && s.RingShots > 1 && s_shot.PullCount % s.RingEvery == 0;
            if (ring) n = Math.Max(n, s.RingShots);
            if (Shapes(s, weapon, n)) TreeEvents.CountShapedShot();   // S-10 FINAL perf cell: proof the measured pulls ran the tree
            if (n <= 1)
            {
                plan = TreeVolleyPlan.Single(pellets);
                plan.Marked = ChaosShotArmed(in s);
                return;
            }

            using (TreeMarkers.ShotHooks.Auto())
            {
                int budget = TreeGuardrails.ShotsPerPull;
                if (pellets > 1 && s.ExtraShots >= TreeGuardrails.ExtraShotsMax) budget = Math.Min(budget, Math.Max(pellets, Mathf.RoundToInt(pellets * PelletFanShare)));
                if (pullsPerSecond > 0f) budget = Math.Min(budget, Math.Max(1, (int)(TreeGuardrails.ProjectileSpawnsPerSecond / pullsPerSecond)));

                int count = Math.Min(n, budget);
                int perVolley = Math.Max(1, Math.Min(pellets, budget / count));
                float extra = ring ? Mathf.Max(0f, s.RingShare) : Mathf.Max(0f, s.ExtraShotDamage);
                float wanted = pellets * (1f + (n - 1) * extra);
                float fired = perVolley * (1f + (count - 1) * extra);
                float merge = fired > 0f ? Mathf.Max(1f, wanted / fired) : 1f;
                plan = new TreeVolleyPlan
                {
                    Count = count,
                    PelletsPerVolley = perVolley,
                    ExtraDamage = extra,
                    ArcDegrees = ring ? 360f * (count - 1) / count : Mathf.Max(0f, s.FanArcDegrees),   // B6 ring: evenly round the player
                    MergedDamageScale = merge,
                    MergedSizeScale = merge > 1f + Epsilon ? Mathf.Min(MergedSizeMax, Mathf.Sqrt(merge)) : 1f,
                    Marked = ChaosShotArmed(in s)
                };
            }
        }

        /// <summary>S-07 K3: the chaos core's next hit is loaded — Mini Nuke armed, or the Black Hole off cooldown.</summary>
        private static bool ChaosShotArmed(in TreeStats s)
        {
            if ((s.Core & TreeChaosCore.Nuke) != 0 && TreeSystemsRunner.NukeArmed) return true;
            return (s.Core & TreeChaosCore.Hole) != 0 && Time.time >= TreeGravityWells.ReadyAt;
        }

        /// <summary>The tree changes this pull: extra volleys, Full-Auto (conversion or ×1.3 rpm), projectile size or fire rate.</summary>
        private static bool Shapes(in TreeStats s, in TreeWeaponProfile weapon, int volleys) =>
            volleys > 1 || IsConverted(s, weapon.FireMode) || Mathf.Abs(s.FullAutoRpmScale - 1f) > Epsilon
            || Mathf.Abs(s.ProjectileScale - 1f) > Epsilon || Mathf.Abs(s.FireRateScale - 1f) > Epsilon;

        /// <summary>Volleys (directions) per pull before any merge: 1 + ExtraShots, capped. Neutral 1.</summary>
        public static int VolleyCount(in TreeWeaponProfile weapon) => VolleyCountOf(TreeRuntime.Current);

        /// <summary>Volley <paramref name="i"/>'s yaw inside <see cref="TreeStats.FanArcDegrees"/>: main shot nearest the centre, the rest symmetric (n = 2 → ±3° for a 6° arc).</summary>
        public static float VolleyYawDegrees(int i, int n) => YawOf(i, n, Mathf.Max(0f, TreeRuntime.Current.FanArcDegrees));

        /// <summary>Damage share of volley <paramref name="i"/>: the main shot 1, every extra shot <see cref="TreeStats.ExtraShotDamage"/> (merge factor not included: see the plan).</summary>
        public static float VolleyDamageScale(int i) => i <= 0 ? 1f : TreeRuntime.Current.ExtraShotDamage;

        private static int VolleyCountOf(in TreeStats s) => 1 + Mathf.Clamp(s.ExtraShots, 0, TreeGuardrails.ExtraShotsMax);

        internal static float YawOf(int i, int n, float arc)
        {
            if (n <= 1 || i < 0 || arc <= 0f) return 0f;
            float step = arc / (n - 1);
            if ((n & 1) == 1)
            {
                int k = (i + 1) / 2;
                return (i & 1) == 1 ? -k * step : k * step;
            }

            int pair = i / 2;
            float offset = (pair + 0.5f) * step;
            return (i & 1) == 0 ? -offset : offset;
        }

        // ------------------------------------------------------------------ shot: projectile

        /// <summary>Big Bore: sweep radius and tracer width scale (a bullet becomes a SphereCast — HSE cost, WP104.Proj.Queries).</summary>
        public static float ProjectileSizeScale => Mathf.Max(0.1f, TreeRuntime.Current.ProjectileScale);

        /// <summary>Bodies a projectile passes through: the larger of the weapon's own and the tree's (≤ <see cref="TreeGuardrails.PierceMax"/> unless the weapon has more).</summary>
        public static int PierceCount(int baseCount)
        {
            int tree = TreeRuntime.Current.PierceCount;
            if (tree <= baseCount) return baseCount;
            return Math.Max(baseCount, Math.Min(tree, TreeGuardrails.PierceMax));
        }

        /// <summary>Damage kept per pierced body: the tree's keep while it owns a pierce node, never worse than the weapon's own.</summary>
        public static float PierceKeep(float baseKeep)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            return s.PierceCount > 0 ? Mathf.Max(baseKeep, s.PierceKeep) : baseKeep;
        }

        public static int RicochetBounces(int baseBounces)
        {
            int tree = TreeRuntime.Current.RicochetBounces;
            if (tree <= baseBounces) return baseBounces;
            return Math.Max(baseBounces, Math.Min(tree, TreeGuardrails.RicochetMax));
        }

        /// <summary>A tree ricochet always bounces (the WP-077 tier rolled a chance): 1 while the tree owns a ricochet node, else the weapon's chance.</summary>
        public static float RicochetChance(float baseChance) => TreeRuntime.Current.RicochetBounces > 0 ? 1f : baseChance;

        /// <summary>Which volleys carry the per-shot branches (ricochet): the main one; every volley with Pinball Storm (sh_double + sh_ricochet).</summary>
        public static bool VolleyCarriesRicochet(int volley) => volley <= 0 || (TreeRuntime.Current.Synergies & TreeSynergy.PinballStorm) != 0;

        public static float RicochetRadius(float baseRadius)
        {
            float tree = TreeRuntime.Current.RicochetRadius;
            return tree > baseRadius ? tree : baseRadius;
        }

        public static float RicochetKeep(float baseKeep)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            return s.RicochetBounces > 0 ? Mathf.Max(baseKeep, s.RicochetKeep) : baseKeep;
        }

        /// <summary>Homing cone of extra shots and bounces in degrees (0 = off).</summary>
        public static float SeekDegrees => Mathf.Max(0f, TreeRuntime.Current.SeekDegrees);

        /// <summary>Explosive Rounds on a bullet weapon: blast radius and the share of the shot dealt as area damage. False when off.</summary>
        public static bool ExplosiveRound(out float radius, out float areaShare)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            radius = s.ExplosiveRadius;
            areaShare = s.ExplosiveAreaShare;
            if (radius > 0f && areaShare > 0f) return true;
            radius = 0f;
            areaShare = 0f;
            return false;
        }

        /// <summary>A weapon that already explodes (rl_1): its own blast radius scale (Explosive Rounds ×1.3, Full-Auto ×0.85, Barrage lifts the penalty).</summary>
        public static float OwnBlastRadiusScale => Mathf.Max(0.1f, TreeRuntime.Current.OwnBlastRadiusScale);

        // ------------------------------------------------------------------ shot: damage / rate / mode

        /// <summary>
        /// The tree's factor on a shot's damage: DamageScale × Hearth × Midas (carried gold) × Bottomless Drum tail × Full-Auto damage ×
        /// the fire rate above the caps (K6: tree rpm over ×3, the trigger over 1 200 rpm or a launcher over 200 rpm becomes damage).
        /// <paramref name="ammoLeft"/> is the magazine BEFORE this round is spent (BuildShot runs before ConsumeShot).
        /// <paramref name="heldSeconds"/> is not read (signature of the B1b stub on main; Overclock runs on <see cref="OnShotFired"/>'s clock, C-00 12.10).
        /// </summary>
        public static float ShotDamageScale(in TreeWeaponProfile weapon, float heldSeconds, float now, int carriedGold, int ammoLeft, int magazineSize)
        {
            using (TreeMarkers.ShotHooks.Auto())
            {
                ref readonly TreeStats s = ref TreeRuntime.Current;
                float scale = s.DamageScale * s.HearthDamageScale * s.FullAutoDamageScale;
                scale *= MidasScale(s, carriedGold);
                scale *= TailScale(s, ammoLeft, magazineSize);
                if (now < s_shot.FountainUntil) scale *= Mathf.Max(1f, s.FountainDamage);   // B6 Ammo Fountain
                if (now < s_shot.RainUntil) scale *= Mathf.Max(1f, s.RainDamage);           // B6 Golden Rain
                RateScale(s, weapon, now, out float overflow);
                return scale * overflow;
            }
        }

        private static float MidasScale(in TreeStats s, int carriedGold)
        {
            if (s.MidasGoldStep <= 0 || s.MidasDamageStep <= 0f || carriedGold < s.MidasGoldStep) return 1f;
            float bonus = (carriedGold / s.MidasGoldStep) * s.MidasDamageStep;
            return 1f + Mathf.Min(bonus, Mathf.Max(0f, s.MidasDamageMax));
        }

        private static float TailScale(in TreeStats s, int ammoLeft, int magazineSize)
        {
            if (s.MagazineTailShare <= 0f || magazineSize <= 0 || ammoLeft <= 0) return 1f;
            int tail = Math.Max(1, CeilInt(magazineSize * s.MagazineTailShare));
            return ammoLeft <= tail ? s.MagazineTailDamage : 1f;
        }

        /// <summary>Full-Auto Conversion: every fire mode becomes Auto (Burst / Single included).</summary>
        public static WeaponFireMode FireMode(WeaponFireMode definition) => TreeRuntime.Current.FullAuto ? WeaponFireMode.Auto : definition;

        /// <summary>True when the weapon in hand fires automatically only because of Full-Auto Conversion.</summary>
        public static bool IsConverted(in TreeWeaponProfile weapon) => IsConverted(TreeRuntime.Current, weapon.FireMode);

        private static bool IsConverted(in TreeStats s, WeaponFireMode definition) => s.FullAuto && definition != WeaponFireMode.Auto;

        /// <summary>
        /// The presentation a converted SINGLE-projectile weapon drops (sr_1, rl_1 under Full-Auto: GDEV (a) risk 3). A pellet weapon
        /// keeps its Heavy shotgun feel (C-00 / S-04 12.10), an already automatic weapon is untouched. None when neutral.
        /// </summary>
        public static TreeFeelOverride PowerOverride(in TreeWeaponProfile weapon)
        {
            if (weapon.Pellets > 1 || !IsConverted(TreeRuntime.Current, weapon.FireMode)) return TreeFeelOverride.None;
            TreeFeelOverride o = TreeFeelOverride.NoHeavyFireShake | TreeFeelOverride.NoHitStop;
            return weapon.IsRocket ? o | TreeFeelOverride.LightBlastShake : o;
        }

        /// <summary>CWR.Power in one line: a converted single-projectile Heavy weapon (sr_1, rl_1) reads Medium; shotguns and everything else unchanged.</summary>
        public static WeaponPower ShotPower(WeaponPower power, in TreeWeaponProfile weapon) =>
            power == WeaponPower.Heavy && PowerOverride(weapon) != TreeFeelOverride.None ? WeaponPower.Medium : power;

        /// <summary>
        /// RocketImpacts.HurtPlayer early-out: a launcher under Full-Auto Conversion (Barrage included — Barrage IS rl_1 + Full-Auto)
        /// never hurts its owner (GDEV (a) 3: 4 × 72 damage a second otherwise). False when neutral.
        /// </summary>
        public static bool RocketSelfImmune(in TreeWeaponProfile weapon) =>
            weapon.IsRocket && (IsConverted(TreeRuntime.Current, weapon.FireMode) || (TreeRuntime.Current.Synergies & TreeSynergy.Barrage) != 0);

        /// <summary>
        /// The tree's factor on the weapon's rate (multiply into EffectiveFireRate and the ConsumeShot clock): Trigger Discipline ×
        /// Twin Barrel × Overclock (sustained fire on <see cref="OnShotFired"/>'s clock) × Endless window rpm, capped at ×3; × Full-Auto
        /// rpm; the trigger capped at <see cref="TreeGuardrails.TriggerRpmMax"/>, a launcher at <see cref="TreeGuardrails.RocketAutoRpm"/>
        /// (a weapon born faster keeps its rpm). The excess goes to <see cref="ShotDamageScale"/> (K6). Raises
        /// <see cref="TreeProcKind.OverclockPeak"/> once when Overclock reaches its max. <paramref name="heldSeconds"/> is not read
        /// (signature of the B1b stub on main; Overclock runs on <see cref="OnShotFired"/>'s clock, not CWR._stringSeconds — C-00 12.10).
        /// </summary>
        public static float FireRateScale(in TreeWeaponProfile weapon, float heldSeconds, float now)
        {
            using (TreeMarkers.ShotHooks.Auto())
            {
                ref readonly TreeStats s = ref TreeRuntime.Current;
                float rate = RateScale(s, weapon, now, out _);
                TrackOverclockPeak(s, weapon, now);
                return rate;
            }
        }

        /// <summary>
        /// A round left the weapon (S-02: once per FireClassicOnce). Overclock's own clock: sustained fire starts at the first shot
        /// after a pause longer than <see cref="OverclockResetSeconds"/>. <paramref name="muzzle"/> (optional) is where the
        /// OverclockPeak moment is drawn (S-07 12.10).
        /// </summary>
        public static void OnShotFired(float now, Vector3 muzzle = default)
        {
            s_shot.LastMuzzle = muzzle;
            if (now - s_shot.LastShotTime > OverclockResetSeconds || now < s_shot.LastShotTime) s_shot.SustainStart = now;
            s_shot.LastShotTime = now;
        }

        /// <summary>Seconds of sustained fire at <paramref name="now"/> (0 after a pause longer than <see cref="OverclockResetSeconds"/>).</summary>
        public static float SustainedFireSeconds(float now)
        {
            if (now - s_shot.LastShotTime > OverclockResetSeconds || now < s_shot.SustainStart) return 0f;
            return now - s_shot.SustainStart;
        }

        private static float RateScale(in TreeStats s, in TreeWeaponProfile weapon, float now, out float overflow)
        {
            overflow = 1f;
            float tree = s.FireRateScale * OverclockScale(s, now) * (InWindow(now) ? Mathf.Max(1f, s.EndlessFireRateScale) : 1f)
                         * (now < s_shot.BlinkUntil ? Mathf.Max(1f, s.BlinkRpm) : 1f);   // B6 Blink Fire
            if (tree > TreeGuardrails.TreeFireRateScaleMax)
            {
                overflow *= tree / TreeGuardrails.TreeFireRateScaleMax;
                tree = TreeGuardrails.TreeFireRateScaleMax;
            }

            float rate = tree * s.FullAutoRpmScale;
            float baseRpm = Mathf.Max(1f, weapon.BaseRpm);
            float ceiling = weapon.IsRocket ? Mathf.Min(TreeGuardrails.TriggerRpmMax, TreeGuardrails.RocketAutoRpm) : TreeGuardrails.TriggerRpmMax;
            float cap = Mathf.Max(ceiling, baseRpm) / baseRpm;
            if (rate > cap)
            {
                overflow *= rate / cap;
                rate = cap;
            }

            return rate;
        }

        private static float OverclockScale(in TreeStats s, float now)
        {
            if (s.OverclockStep <= 0f || s.OverclockMax <= 0f) return 1f;
            float held = SustainedFireSeconds(now);
            if (held < OverclockPeriodSeconds) return 1f;
            int steps = (int)(held / OverclockPeriodSeconds);
            return 1f + Mathf.Min(steps * s.OverclockStep, s.OverclockMax);
        }

        private static void TrackOverclockPeak(in TreeStats s, in TreeWeaponProfile weapon, float now)
        {
            if (s.OverclockMax <= 0f) return;
            bool peak = OverclockScale(s, now) >= 1f + s.OverclockMax - Epsilon;
            if (peak == s_shot.OverclockPeaked) return;
            s_shot.OverclockPeaked = peak;
            if (peak) Raise(TreeProcKind.OverclockPeak, s_shot.LastMuzzle, 0f, TreeBranch.Speed, weapon.Id);
        }

        // ------------------------------------------------------------------ shot: crit

        /// <summary>A non-zero xorshift seed for a run (S-02 seeds its pull RNG once per run; same run id → same crits).</summary>
        public static uint SeedFor(int runId, uint salt)
        {
            uint x = unchecked((uint)runId * 2654435761u ^ salt * 40503u);
            return x != 0u ? x : 0x9E3779B9u;
        }

        /// <summary>
        /// Lucky Strike: ONE roll per trigger pull (the whole pull crits; GDEV risk 6 — not per pellet). Deterministic xorshift on the
        /// caller's state; after <see cref="TreeStats.CritPity"/> crit-less pulls the next pull is a crit. Neutral: false, ×1, the
        /// random state untouched. The presentation (B13) is raised at the hit, not here.
        /// </summary>
        public static bool RollCrit(ref uint rngState, out float multiplier)
        {
            using (TreeMarkers.ShotHooks.Auto())
            {
                ref readonly TreeStats s = ref TreeRuntime.Current;
                multiplier = 1f;
                if (s.CritChance <= 0f || s.CritMultiplier <= 1f) return false;
                bool pity = s.CritPity > 0 && s_shot.PullsSinceCrit >= s.CritPity;
                bool roll = Next01(ref rngState) < s.CritChance;   // always advance: the stream stays aligned with the pulls
                if (!(pity || roll))
                {
                    s_shot.PullsSinceCrit++;
                    return false;
                }

                s_shot.PullsSinceCrit = 0;
                multiplier = s.CritMultiplier;
                return true;
            }
        }

        // ------------------------------------------------------------------ magazine

        /// <summary>
        /// Magazine size: a converted weapon starts from its Full-Auto magazine (rl_1 12-rocket box), then × MagazineScale rounded up,
        /// at least +1 when scaled up (sr 5→8). A TINY magazine (≤ <see cref="TinyMagazine"/>: rl_1 1, sg_2 2) keeps its size and takes the
        /// scale as reload speed instead (<see cref="ReloadTimeScale"/>) — C-00 12.10 B5 (b): +1 round there doubled the sustained DPS.
        /// Neutral: <paramref name="baseSize"/>.
        /// </summary>
        public static int MagazineSize(int baseSize, in TreeWeaponProfile weapon)
        {
            using (MagazineMarker.Auto())
            {
                ref readonly TreeStats s = ref TreeRuntime.Current;
                int size = s.FullAutoMagazine > 0 && IsConverted(s, weapon.FireMode) ? s.FullAutoMagazine : baseSize;
                float scale = s.MagazineScale;
                if (size <= 0 || Mathf.Abs(scale - 1f) <= Epsilon || size <= TinyMagazine) return size;
                int scaled = Math.Max(1, CeilInt(size * scale));
                return scale > 1f ? Math.Max(size + 1, scaled) : scaled;
            }
        }

        /// <summary>
        /// Reserve factor: a converted weapon or a launcher keeps the same number of MAGAZINES in reserve (rl_1 8 → 96 with the
        /// 12-box, so the box can always be filled); every other weapon 1. Neutral 1.
        /// </summary>
        public static float ReserveScale(int baseMagazine, in TreeWeaponProfile weapon)
        {
            if (baseMagazine <= 0) return 1f;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (!weapon.IsRocket && !IsConverted(s, weapon.FireMode)) return 1f;
            return MagazineSize(baseMagazine, weapon) / (float)baseMagazine;
        }

        /// <summary>
        /// CWR.ReserveAllowance in one line: <paramref name="baseReserve"/> (the definition's reserve, round coefficient included) ×
        /// <see cref="ReserveScale"/> over the weapon's own magazine (<see cref="TreeWeaponProfile.Magazine"/>), and never below one
        /// full tree magazine. An infinite reserve (&lt; 0) unchanged. Neutral: <paramref name="baseReserve"/>.
        /// </summary>
        public static int ReserveAllowance(int baseReserve, in TreeWeaponProfile weapon)
        {
            if (baseReserve <= 0) return baseReserve;
            float scale = ReserveScale(weapon.Magazine, weapon);
            if (Mathf.Abs(scale - 1f) <= Epsilon) return baseReserve;
            int scaled = Mathf.RoundToInt(baseReserve * scale);
            return Math.Max(scaled, MagazineSize(weapon.Magazine, weapon));
        }

        /// <summary>
        /// Reload duration factor for a reload of <paramref name="baseSeconds"/> (CWR passes the definition's tac / empty seconds):
        /// Quick Hands × the converted launcher's box reload brought to its target (run-tree-v2 fullAuto "reload": rl_1 3.0 s; Barrage's
        /// "reload" 2.4 s), i.e. target / base. Catalog read on a stats / weapon change only (cached by
        /// <see cref="TreeRuntime.Version"/>). Neutral 1.
        /// </summary>
        public static float ReloadTimeScale(in TreeWeaponProfile weapon, float baseSeconds)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            float scale = Mathf.Max(0.05f, s.ReloadScale);
            // C-00 12.10 B5 (b): a tiny magazine takes the magazine nodes as reload speed. With fire time ≈ reload a ×m magazine gives a
            // sustained 2m/(m+1); a reload ×1/m gives the same, so every weapon class gains the same ratio.
            int effectiveMagazine = s.FullAutoMagazine > 0 && IsConverted(s, weapon.FireMode) ? s.FullAutoMagazine : weapon.Magazine;
            if (effectiveMagazine > 0 && effectiveMagazine <= TinyMagazine && s.MagazineScale > 1f + Epsilon) scale /= s.MagazineScale;
            if (baseSeconds <= 0f || !IsConverted(s, weapon.FireMode)) return scale;
            if (!s_reload.Valid || s_reload.Version != TreeRuntime.Version || s_reload.Weapon != weapon.Id)
            {
                s_reload.Seconds = ConvertedReloadSeconds(s, weapon.Key);
                s_reload.Version = TreeRuntime.Version;
                s_reload.Weapon = weapon.Id;
                s_reload.Valid = true;
            }

            return s_reload.Seconds > 0f ? scale * s_reload.Seconds / baseSeconds : scale;
        }

        /// <summary>Quick Hands III: no movement penalty while reloading.</summary>
        public static bool ReloadMoveFree => TreeRuntime.Current.ReloadMoveFree;

        private static float ConvertedReloadSeconds(in TreeStats s, string key)
        {
            TreeFullAutoDefV2 row = TreeCatalogV2.FullAutoFor(key);
            float seconds = row != null ? row.reload : 0f;
            if (seconds <= 0f || (s.Synergies & TreeSynergy.Barrage) == 0) return seconds;
            TreeSynergyDefV2[] synergies = TreeCatalogV2.Document.synergies;
            for (int i = 0; synergies != null && i < synergies.Length; i++)
            {
                if (synergies[i] != null && synergies[i].Synergy == TreeSynergy.Barrage) return synergies[i].Arg("reload", seconds);
            }

            return seconds;
        }

        /// <summary>
        /// Auto-Loader: after <see cref="TreeStats.AutoReloadIdleSeconds"/> without a shot the magazine refills
        /// <see cref="TreeStats.AutoReloadPerSecond"/> of itself per second. Returns whole rounds to load now (≤ <paramref name="missing"/>;
        /// the caller takes them from the reserve like a reload); <paramref name="carry"/> keeps the fraction between frames and is
        /// cleared while firing. Neutral: 0.
        /// </summary>
        public static int AutoReloadTick(float dt, float sinceShot, int missing, int magazineSize, ref float carry)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s_shot.FountainRefill && missing > 0)
            {
                s_shot.FountainRefill = false;   // B6 Ammo Fountain: the whole magazine, once
                carry = 0f;
                return missing;
            }

            if (s.AutoReloadIdleSeconds <= 0f || s.AutoReloadPerSecond <= 0f || missing <= 0 || sinceShot < s.AutoReloadIdleSeconds)
            {
                carry = 0f;
                return 0;
            }

            using (MagazineMarker.Auto())
            {
                carry += s.AutoReloadPerSecond * Math.Max(1, magazineSize) * Mathf.Max(0f, dt);
                int rounds = (int)carry;
                if (rounds <= 0) return 0;
                carry -= rounds;
                return Math.Min(rounds, missing);
            }
        }

        /// <summary>Endless Window: the magazine is not spent until this closes.</summary>
        public static bool NoReloadWindow(float now)
        {
            RefreshWindow(now);
            return s_shot.WindowOpen;
        }

        /// <summary>Twin Barrel: every Nth round (shot serial, 1-based) does not use ammo.</summary>
        public static bool TwinFreeRound(int shotSerial)
        {
            int every = TreeRuntime.Current.TwinFreeEvery;
            return every > 0 && shotSerial > 0 && shotSerial % every == 0;
        }

        /// <summary>CWR.ConsumeShot in one line: false inside the Endless window or on a Twin Barrel free round.</summary>
        public static bool ConsumesAmmo(int shotSerial, float now) => !NoReloadWindow(now) && !TwinFreeRound(shotSerial);

        /// <summary>
        /// A kill (installed on <see cref="DamageEvents.EnemyDied"/>; public for tests). Endless Window counts KILLS, never hits
        /// (GDEV risk 6); kills inside an open window do not count towards the next one. Also the kill clock of Wind Step.
        /// </summary>
        public static void OnKill(float now, Vector3 point = default)
        {
            s_shot.LastKillTime = now;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s.BlinkSeconds > 0f) s_shot.BlinkUntil = now + s.BlinkSeconds;   // B6 Blink Fire
            if (s.FountainChance > 0f && Next01(ref s_shot.FountainRng) < s.FountainChance)
            {
                s_shot.FountainRefill = true;   // B6 Ammo Fountain
                s_shot.FountainUntil = now + Mathf.Max(0f, s.FountainSeconds);
            }

            if (s.EndlessKills <= 0 || s.EndlessSeconds <= 0f) return;
            using (TreeMarkers.HitHooks.Auto())
            {
                RefreshWindow(now);
                if (s_shot.WindowOpen) return;
                s_shot.KillsTowardWindow++;
                if (s_shot.KillsTowardWindow < s.EndlessKills) return;
                s_shot.KillsTowardWindow = 0;
                s_shot.WindowUntil = now + s.EndlessSeconds;
                s_shot.WindowOpen = true;
                Raise(TreeProcKind.EndlessWindowOpen, point, 0f, TreeBranch.Magazine, s_inHand.Id, s.EndlessSeconds);
            }
        }

        /// <summary>Per frame (WC.Update): closes the Endless window on time (raises EndlessWindowClose even when nobody fires).</summary>
        public static void Tick(float now)
        {
            RefreshWindow(now);
            if (TreeRuntime.Current.RainGoldStep <= 0) return;
            DD.Prototype.Economy.GoldService gold = DD.Prototype.Economy.GoldService.Instance;
            if (gold != null && gold.Wallet != null) OnGoldSpent(gold.Wallet.TotalSpent, now);
        }

        /// <summary>
        /// Golden Rain (awakening, Economy IV): the run wallet's total spend (tree, rerolls, wall buys); every
        /// <see cref="TreeStats.RainGoldStep"/> spent opens <see cref="TreeStats.RainSeconds"/> of ×<see cref="TreeStats.RainDamage"/>.
        /// The first call only marks (gold spent before the node does not rain). Public for tests.
        /// </summary>
        public static void OnGoldSpent(int totalSpent, float now)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s.RainGoldStep <= 0) return;
            if (s_shot.RainSpentMark < 0 || totalSpent < s_shot.RainSpentMark) s_shot.RainSpentMark = totalSpent;   // first look / a new run's wallet
            while (totalSpent - s_shot.RainSpentMark >= s.RainGoldStep)
            {
                s_shot.RainSpentMark += s.RainGoldStep;   // B6 Golden Rain: every step of gold spent
                s_shot.RainUntil = now + Mathf.Max(0f, s.RainSeconds);
            }
        }

        private static bool InWindow(float now) => s_shot.WindowOpen && now < s_shot.WindowUntil;

        private static void RefreshWindow(float now)
        {
            if (!s_shot.WindowOpen || now < s_shot.WindowUntil) return;
            s_shot.WindowOpen = false;
            Raise(TreeProcKind.EndlessWindowClose, default, 0f, TreeBranch.Magazine, s_inHand.Id);
        }

        // ------------------------------------------------------------------ survival

        public static float MaxHealthScale => Mathf.Max(0.1f, TreeRuntime.Current.MaxHealthScale);

        public static float RegenDelayScale => Mathf.Max(0f, TreeRuntime.Current.RegenDelayScale);

        /// <summary>Seconds since the last kill (∞ before the first).</summary>
        public static float SinceKill(float now) => now - s_shot.LastKillTime;

        /// <summary>The tree's move-speed factor (above the weapon clamp): Light Feet / Wind Step × Wind Step's kill boost window.</summary>
        public static float MoveScale(float now)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            float boost = s.KillSpeedBoostSeconds > 0f && SinceKill(now) < s.KillSpeedBoostSeconds ? Mathf.Max(1f, s.KillSpeedBoost) : 1f;
            return Mathf.Max(0.1f, s.MoveScale) * boost;
        }

        /// <summary>Light Feet: the weapon's firing slowdown <paramref name="weaponFactor"/> (≤ 1) softened (0.5 = half the penalty).</summary>
        public static float FiringMoveFactor(float weaponFactor)
        {
            if (weaponFactor >= 1f) return weaponFactor;
            return 1f - (1f - weaponFactor) * Mathf.Clamp01(TreeRuntime.Current.FiringMovePenaltyScale);
        }

        /// <summary>Spirit Ward / Stone Guardian pool for a max health.</summary>
        public static float ShieldMax(float maxHealth) => Mathf.Max(0f, TreeRuntime.Current.ShieldShare) * Mathf.Max(0f, maxHealth);

        /// <summary>Run / round start: the shield starts full.</summary>
        public static void ShieldFill(float maxHealth, ref TreeShieldState state) => state.Shield = ShieldMax(maxHealth);

        /// <summary>Per frame (PlayerHealth.Update): after <see cref="TreeStats.ShieldDelaySeconds"/> without a hit the shield refills ShieldRegenPerSecond of its max per second.</summary>
        public static void ShieldTick(float dt, float now, float maxHealth, ref TreeShieldState state, Vector3 point = default)
        {
            float max = ShieldMax(maxHealth);
            if (max <= 0f)
            {
                state.Shield = 0f;
                return;
            }

            if (state.Shield > max) state.Shield = max;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (state.Shield >= max || s.ShieldRegenPerSecond <= 0f || now - state.LastHitTime < s.ShieldDelaySeconds) return;
            state.Shield = Mathf.Min(max, state.Shield + s.ShieldRegenPerSecond * max * Mathf.Max(0f, dt));
            if (state.Shield >= max) Raise(TreeProcKind.ShieldUp, point, 0f, TreeBranch.Survival, s_inHand.Id);   // S-08 / S-07: refilled to full
        }

        /// <summary>
        /// One hit on the player after the Survival branch, in order: Undying grace → dodge ghost → dodge roll (≤ 30 %) → shield →
        /// Undying Oath on a fatal remainder (once per round). Raises Dodge / ShieldBreak / UndyingOath. Neutral: the damage
        /// unchanged, <see cref="TreeHitOutcome.None"/>, no random number used.
        /// </summary>
        public static TreeHitResult IncomingDamage(float damage, float now, float health, float maxHealth, ref TreeShieldState state, Vector3 point = default)
        {
            var hit = new TreeHitResult { Damage = Mathf.Max(0f, damage) };
            if (hit.Damage <= 0f) return hit;
            using (TreeMarkers.HitHooks.Auto())
            {
                ref readonly TreeStats s = ref TreeRuntime.Current;
                if (now < state.InvulnerableUntil)
                {
                    hit.Damage = 0f;
                    hit.Outcome = TreeHitOutcome.Invulnerable;
                    return hit;
                }

                if (now < state.GhostUntil)
                {
                    hit.Damage = 0f;
                    hit.Outcome = TreeHitOutcome.Ghost;
                    return hit;
                }

                float dodge = Mathf.Clamp(s.DodgeChance, 0f, TreeGuardrails.DodgeChanceMax);
                if (dodge > 0f && Next01(ref state.Rng) < dodge)
                {
                    state.GhostUntil = now + Mathf.Max(0f, s.DodgeGhostSeconds);
                    hit.Damage = 0f;
                    hit.Outcome = TreeHitOutcome.Dodged;
                    if (s.BlinkSeconds > 0f) s_shot.BlinkUntil = now + s.BlinkSeconds;   // B6 Blink Fire
                    Raise(TreeProcKind.Dodge, point, 0f, TreeBranch.Survival, s_inHand.Id);
                    return hit;
                }

                if (s.ShieldShare > 0f) state.LastHitTime = now;
                if (state.Shield > 0f)
                {
                    float absorbed = Mathf.Min(state.Shield, hit.Damage);
                    state.Shield -= absorbed;
                    hit.Damage -= absorbed;
                    hit.Absorbed = absorbed;
                    hit.Outcome = TreeHitOutcome.ShieldAbsorbed;
                    if (state.Shield > Epsilon) Raise(TreeProcKind.ShieldHit, point, 0f, TreeBranch.Survival, s_inHand.Id);
                    if (state.Shield <= Epsilon)
                    {
                        state.Shield = 0f;
                        hit.Outcome = TreeHitOutcome.ShieldBroken;
                        float radius = 0f;
                        if (s.BulwarkRadius > 0f && now >= state.BulwarkReadyAt)
                        {
                            radius = s.BulwarkRadius;
                            state.BulwarkReadyAt = now + Mathf.Max(0f, s.BulwarkCooldown);
                        }

                        Raise(TreeProcKind.ShieldBreak, point, radius, TreeBranch.Survival, s_inHand.Id);
                        if (radius > 0f) TreeSystemsRunner.GuardWave(point, radius, s.BulwarkStunSeconds);   // Stone Guardian: stun + push wave
                    }
                }

                if (hit.Damage > 0f && health - hit.Damage <= 0f && s.UndyingPerRound > 0 && s_body.UndyingUsedThisRound < s.UndyingPerRound)
                {
                    s_body.UndyingUsedThisRound++;
                    float floor = Mathf.Max(1f, Mathf.Clamp01(s.UndyingHealthShare) * maxHealth);
                    state.InvulnerableUntil = now + Mathf.Max(0f, s.UndyingSeconds);
                    hit.HealthFloor = floor;
                    hit.Damage = Mathf.Max(0f, health - floor);
                    hit.Outcome = TreeHitOutcome.Undying;
                    Raise(TreeProcKind.UndyingOath, point, s.UndyingPushRadius, TreeBranch.Survival, s_inHand.Id);
                    if (s.UndyingPushRadius > 0f) TreeSystemsRunner.GuardWave(point, s.UndyingPushRadius, 0f);   // Undying Oath: push wave
                }
                else if (hit.Damage > 0f && health - hit.Damage <= 0f && s.PhoenixRadius > 0f && !s_body.PhoenixUsed)
                {
                    // B6 Phoenix (awakening, Survival V): once per RUN the killing blow becomes a fire blast and a rise.
                    s_body.PhoenixUsed = true;
                    float floor = Mathf.Max(1f, Mathf.Clamp01(s.PhoenixHealthShare) * maxHealth);
                    state.InvulnerableUntil = now + Mathf.Max(0f, s.PhoenixSeconds);
                    hit.HealthFloor = floor;
                    hit.Damage = Mathf.Max(0f, health - floor);
                    hit.Outcome = TreeHitOutcome.Undying;
                    Raise(TreeProcKind.UndyingOath, point, s.PhoenixRadius, TreeBranch.Survival, s_inHand.Id, s.PhoenixSeconds);
                    TreeSystemsRunner.PhoenixBlast(point, s.PhoenixRadius, s.PhoenixShare);
                }

                return hit;
            }
        }

        /// <summary>
        /// Life Drain: the share of MAX health a kill heals (Standard / Special 1 %, Elite 5 %, Ultra 15 %), at most
        /// <see cref="TreeStats.LeechCapPerSecond"/> per second (multi-shot builds kill fast). Raises Leech. Neutral: 0.
        /// </summary>
        public static float OnKillHeal(EnemyTier tier, float now, Vector3 point = default)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            float want = tier == EnemyTier.Ultra ? s.LeechUltra : tier == EnemyTier.Elite ? s.LeechElite : s.LeechStandard;
            if (want <= 0f || s.LeechCapPerSecond <= 0f) return 0f;
            using (TreeMarkers.HitHooks.Auto())
            {
                if (now - s_body.LeechWindowStart >= LeechWindowSeconds || now < s_body.LeechWindowStart)
                {
                    s_body.LeechWindowStart = now;
                    s_body.LeechInWindow = 0f;
                }

                float heal = Mathf.Min(want, s.LeechCapPerSecond - s_body.LeechInWindow);
                if (heal <= Epsilon) return 0f;
                s_body.LeechInWindow += heal;
                Raise(TreeProcKind.Leech, point, 0f, TreeBranch.Survival, s_inHand.Id);
                return heal;
            }
        }

        // ------------------------------------------------------------------ economy (gold pickup)

        /// <summary>Coin Sense III: the gold orb magnet radius factor (GoldService.MagnetRadius). Neutral 1.</summary>
        public static float MagnetRadiusScale => Mathf.Max(0.1f, TreeRuntime.Current.MagnetScale);

        /// <summary>A round began (installed on the round bridge; public for tests): Undying Oath is ready again.</summary>
        public static void OnRoundStarted(int round = 0) => s_body.UndyingUsedThisRound = 0;

        // ------------------------------------------------------------------ economy (WP-107)

        /// <summary>Arms Dealer: a wall buy price (rounded up, at least 1). 0 / negative (not sold) unchanged. Neutral: the price.</summary>
        public static int WallBuyPrice(int basePrice)
        {
            if (basePrice <= 0) return basePrice;
            return Math.Max(1, CeilInt(basePrice * (double)Mathf.Max(0f, TreeRuntime.Current.WallBuyScale)));
        }

        /// <summary>
        /// Arms Dealer: an ammo refill — <paramref name="refillShare"/> (WP-107 catalog, half) of the RAW weapon price × AmmoBuyScale,
        /// rounded up, at least 1 (0 when the weapon is not sold). Neutral: exactly WallBuyRules.RefillCost.
        /// </summary>
        public static int AmmoPrice(int basePrice, float refillShare)
        {
            if (basePrice <= 0) return 0;
            double cost = basePrice * (double)Mathf.Clamp01(refillShare) * Mathf.Max(0f, TreeRuntime.Current.AmmoBuyScale);
            return Math.Max(1, CeilInt(cost));
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Ceiling that ignores float noise (2 500 × 0.8f must be 2 000, not 2 001).</summary>
        private static int CeilInt(double value) => (int)Math.Ceiling(value - 1e-3);

        /// <summary>xorshift32 → [0, 1). A zero state is seeded (xorshift's fixed point).</summary>
        private static float Next01(ref uint state)
        {
            uint x = state != 0u ? state : 0x9E3779B9u;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return (x >> 8) * (1f / 16777216f);
        }

        private static void Raise(TreeProcKind kind, Vector3 point, float radius, TreeBranch branch, ClassicWeaponId weapon, float seconds = 0f)
        {
            var info = new TreeProcInfo
            {
                Kind = kind,
                Point = point,
                Radius = radius,
                Seconds = seconds,
                Tier = (TreeRuntime.Current.BranchTiers >> ((int)branch * 3)) & 7,
                Weapon = weapon
            };
            TreeEvents.RaiseProc(in info);
        }

        /// <summary>Seeker Swarm: extra shots (VolleyIndex &gt; 0) lock the nearest body in this cone at spawn and steer to it. Neutral 0.
        /// The ricochet search stays <see cref="SeekDegrees"/>.</summary>
        public static float ExtraShotSeekDegrees => Mathf.Max(0f, TreeRuntime.Current.ExtraShotSeekDegrees);

        /// <summary>
        /// Rail Line: seconds every pierced body is stunned after the hit (S-02 HitscanEngine, one call per pierced body; S-03's
        /// rail stun kind applies the Elite / Ultra share). Neutral 0.
        /// </summary>
        public static float PierceStaggerSeconds => Mathf.Max(0f, TreeRuntime.Current.PierceStaggerSeconds);

        // ------------------------------------------------------------------ Special resonance (KARAR-WP104 6)

        /// <summary>Raiju (sw_arc) lifetime scale: Storm Chain owned → ×1,3 (design §3). Neutral 1. S-02's Special path reads it once per pickup.</summary>
        public static float RaijuDurationScale => Mathf.Max(1f, TreeRuntime.Current.RaijuDurationScale);

        /// <summary>Extra chain jumps of Raiju's lightning: Storm Chain owned → +2. Neutral 0.</summary>
        public static int RaijuExtraJumps => Math.Max(0, TreeRuntime.Current.RaijuExtraJumps);

        /// <summary>Namekuji's (sw_gel) slick floor freezes the bodies in it: Deep Freeze owned. Neutral false.</summary>
        public static bool NamekujiFreezes => TreeRuntime.Current.NamekujiFreezes;

        // ------------------------------------------------------------------ run boundary / install

        /// <summary>A new run (or its end): crit pity, Endless counter and window, Overclock edge, kill clock, Undying, leech window.</summary>
        public static void ResetRunState()
        {
            s_shot = NewShotState();
            s_body = NewBodyState();
            s_reload.Valid = false;
        }

        private static void OnRunStarted(int run) => ResetRunState();

        private static void OnEnemyDied(EnemyDeathInfo info) => OnKill(Time.time, info.Position);

        /// <summary>Idempotent (remove-then-add): a bus a test reset is re-armed, never two copies.</summary>
        public static void EnsureHooked()
        {
            SpecialWeaponRoundBridge.RunStarted -= OnRunStarted;
            SpecialWeaponRoundBridge.RunStarted += OnRunStarted;
            SpecialWeaponRoundBridge.RunEnded -= ResetRunState;
            SpecialWeaponRoundBridge.RunEnded += ResetRunState;
            SpecialWeaponRoundBridge.RoundStarted -= OnRoundStarted;
            SpecialWeaponRoundBridge.RoundStarted += OnRoundStarted;
            DamageEvents.EnemyDied -= OnEnemyDied;
            DamageEvents.EnemyDied += OnEnemyDied;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetRunState();
            s_inHand = default;
            Equipping = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            EnsureHooked();
            // Static bus rearm lesson: re-arm on every active-scene change.
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private static void OnActiveSceneChanged(Scene from, Scene to) => EnsureHooked();
    }
}
