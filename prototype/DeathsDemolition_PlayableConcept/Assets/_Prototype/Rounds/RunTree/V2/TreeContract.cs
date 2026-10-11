using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using Unity.Profiling;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    // WP-104 B0 (S-04, C-00 11.10): the frozen contract of the feature-based run tree (design K/c-00/R4/teslim/S-04-11.10-1.md,
    // decisions K/c-00/R4/KARAR-WP104-11.10.md). S-02 (fire path), S-03 (zombie status receivers), S-07 (VFX), S-08 (audio) and
    // S-09 (tree screen) build on these types in parallel; B1 fills them. Values append only: enums are serialised as ints.

    /// <summary>The seven branches. Values are identities, not the screen order (S-09 lays the rows out in the AD order).</summary>
    public enum TreeBranch
    {
        Shot = 0,
        Magazine = 1,
        Speed = 2,
        Element = 3,
        Chaos = 4,
        Survival = 5,
        Economy = 6
    }

    /// <summary>The four elements (F1: at most two per run, three with the Third Affinity awakening).</summary>
    [Flags]
    public enum TreeElement : byte
    {
        None = 0,
        Fire = 1 << 0,
        Ice = 1 << 1,
        Shock = 1 << 2,
        Venom = 1 << 3
    }

    /// <summary>The chaos core (F2: Mini Nuke or Black Hole; both only with the Twin Cores awakening).</summary>
    [Flags]
    public enum TreeChaosCore : byte
    {
        None = 0,
        Nuke = 1 << 0,
        Hole = 1 << 1
    }

    /// <summary>The ten synergies (free, unlock themselves, "double seal" badge). Bit per synergy.</summary>
    [Flags]
    public enum TreeSynergy : ushort
    {
        None = 0,
        /// <summary>rl_1 in hand + Full-Auto Conversion.</summary>
        Barrage = 1 << 0,
        /// <summary>Deep Freeze + Black Hole Rounds.</summary>
        FrozenGravity = 1 << 1,
        /// <summary>Arc Rounds + Chain Reaction.</summary>
        ThunderCascade = 1 << 2,
        /// <summary>Double Shot + Ricochet.</summary>
        PinballStorm = 1 << 3,
        /// <summary>Endless Window + Full-Auto Conversion.</summary>
        UnbrokenLine = 1 << 4,
        /// <summary>Wildfire + Explosive Rounds.</summary>
        WildfireRounds = 1 << 5,
        /// <summary>Venom Rounds + Piercing.</summary>
        ToxicRail = 1 << 6,
        /// <summary>Fortune's Edge + Mini Nuke.</summary>
        CriticalMeltdown = 1 << 7,
        /// <summary>Rail Line + Explosive Rounds.</summary>
        JudgmentLine = 1 << 8,
        /// <summary>Midas Hoard + Lucky Strike.</summary>
        GoldenGun = 1 << 9
    }

    /// <summary>Node states on the tree screen (word + glyph, never colour alone).</summary>
    public enum TreeNodeState
    {
        /// <summary>Its tier's round gate is not reached yet: under the fog.</summary>
        Fogged = 0,
        PrereqMissing = 1,
        Available = 2,
        Unaffordable = 3,
        Purchased = 4,
        /// <summary>Closed by a fork (F1 third element, F2 other core).</summary>
        Excluded = 5,
        /// <summary>An awakening variant the profile has not unlocked with Ash.</summary>
        AwakeningLocked = 6,
        /// <summary>WP-104 B1: no system reads this node's effect yet (TreeLiveV2), so it is never sold (the WP-077 rule).</summary>
        EffectPending = 7
    }

    /// <summary>Gameplay moments the tree raises for VFX (S-07), audio (S-08), HUD (S-09) and telemetry. Values append only.</summary>
    public enum TreeProcKind
    {
        None = 0,
        Crit = 1,
        CritRing = 2,
        ExplosiveRound = 3,
        ChainBlast = 4,
        MiniNuke = 5,
        BlackHoleOpen = 6,
        BlackHoleCollapse = 7,
        Ignite = 8,
        FireGround = 9,
        Freeze = 10,
        Shatter = 11,
        ShockChain = 12,
        VenomCloud = 13,
        ElementNova = 14,
        DoomsdayBell = 15,
        ShieldBreak = 16,
        Dodge = 17,
        Leech = 18,
        UndyingOath = 19,
        EndlessWindowOpen = 20,
        EndlessWindowClose = 21,
        OverclockPeak = 22,
        SynergyFirstTrigger = 23,
        ClusterSplit = 24,
        /// <summary>WP-104 B1 (S-08 / S-07 request): the shield came up or refilled to full (Point = player).</summary>
        ShieldUp = 25,
        /// <summary>Damage absorbed by the shield without breaking it (Point = player).</summary>
        ShieldHit = 26,
        /// <summary>One bounce (Point = bounce point, Depth = bounce index; S-02's fire path raises it).</summary>
        Ricochet = 27,
        /// <summary>A Barrage (rl_1 + Full-Auto) rocket launched (S-02 fire path).</summary>
        BarrageRocket = 28
    }

    /// <summary>Difficulty modes as the tree reads them (index into per-mode tables). Same order as S-03's WP-102 DifficultyMode.</summary>
    public static class TreeMode
    {
        public const int Normal = 0;
        public const int Hard = 1;
        public const int Demolition = 2;
        public const int Count = 3;
    }

    /// <summary>
    /// What the tree needs to know about the weapon in hand to resolve <see cref="TreeStats"/> (S-02 fills it on equip from the
    /// Arsenal <see cref="WeaponDefinition"/>). Weapon power SG = rpm / 60 × damage × pellets (design K2).
    /// </summary>
    public struct TreeWeaponProfile
    {
        public ClassicWeaponId Id;
        /// <summary>WP-069 key (ar_1 … rl_1).</summary>
        public string Key;
        public WeaponFireMode FireMode;
        public float BaseRpm;
        public float BaseDamage;
        public int Pellets;
        public int Magazine;
        public ProjectileKind Projectile;

        public bool IsRocket => Projectile == ProjectileKind.Rocket;
        public bool IsAutomatic => FireMode == WeaponFireMode.Auto;
        /// <summary>Design K3: ≤ 90 rpm weapons (sg_1, sr_1, rl_1) take more stacks per hit.</summary>
        public bool IsSlow => BaseRpm <= TreeGuardrails.SlowWeaponRpm;
        public float BaseDps => BaseRpm / 60f * BaseDamage * Mathf.Max(1, Pellets);
    }

    /// <summary>One tree moment. A plain struct raised through <see cref="TreeEvents.Proc"/>; listeners must not keep <see cref="Target"/>.</summary>
    public struct TreeProcInfo
    {
        public TreeProcKind Kind;
        public Vector3 Point;
        public float Radius;
        /// <summary>Damage dealt (after scaling); 0 for pure presentation moments.</summary>
        public float Damage;
        public TreeElement Element;
        /// <summary>Highest owned tier in the branch that raised it (1–5): drives the visual tier ladder (AD §2).</summary>
        public int Tier;
        public TreeSynergy Synergy;
        public ClassicWeaponId Weapon;
        /// <summary>The body hit or killed, if any. Valid only during the callback.</summary>
        public Component Target;
        /// <summary>Chain generation (0 = direct), see <see cref="TreeGuardrails.ChainDepth"/>; for Ricochet the bounce index.</summary>
        public int Depth;
        /// <summary>WP-104 B1 (S-07 / S-08): how long the moment lasts in seconds — fire ground, venom cloud, black hole, freeze,
        /// Endless window; 0 for an instant (blast, crit, chain).</summary>
        public float Seconds;
    }

    /// <summary>
    /// The tree's event surface. Raised on the main thread, never per frame per body: VFX and audio subscribe once.
    /// Purchase / synergy events fire from the round-end screen only.
    /// </summary>
    public static class TreeEvents
    {
        public static event Action<TreeProcInfo> Proc;
        /// <summary>(node id, branch, tier) after a purchase.</summary>
        public static event Action<string, TreeBranch, int> NodePurchased;
        public static event Action<TreeSynergy> SynergyUnlocked;
        /// <summary>The aura level changed (0–3, AD B11).</summary>
        public static event Action<int> AuraChanged;
        /// <summary>The resolved stats changed (purchase, equip, run start/end).</summary>
        public static event Action StatsChanged;

        /// <summary>WP-104 B1 (S-10 FINAL tree perf cell): tree moments raised since the last <see cref="ResetCounters"/> — proof that a
        /// measured window really ran tree effects (the probe writes it as treeEvents; a cell with 0 is not a tree measurement).</summary>
        public static int ProcCount { get; private set; }

        /// <summary>Trigger pulls the tree changed (extra shots, Full-Auto, size…): the B2 fire-path hooks call <see cref="CountShapedShot"/>.</summary>
        public static int ShapedShots { get; private set; }

        public static void CountShapedShot() => ShapedShots++;

        public static void ResetCounters()
        {
            ProcCount = 0;
            ShapedShots = 0;
        }

        public static void RaiseProc(in TreeProcInfo info)
        {
            ProcCount++;
            Proc?.Invoke(info);
        }
        public static void RaiseNodePurchased(string nodeId, TreeBranch branch, int tier) => NodePurchased?.Invoke(nodeId, branch, tier);
        public static void RaiseSynergyUnlocked(TreeSynergy synergy) => SynergyUnlocked?.Invoke(synergy);
        public static void RaiseAuraChanged(int level) => AuraChanged?.Invoke(level);
        public static void RaiseStatsChanged() => StatsChanged?.Invoke();

        /// <summary>Tests / domain reload: drop every listener.</summary>
        public static void ResetForTests()
        {
            Proc = null;
            NodePurchased = null;
            SynergyUnlocked = null;
            AuraChanged = null;
            StatsChanged = null;
            ResetCounters();
        }
    }

    /// <summary>
    /// Design §8 perf guardrails (binding; the stricter of GD and GDEV). Over a cap the power is merged, never dropped:
    /// shots fuse into one thicker shot, blasts into the nearest queued blast (damage summed, radius max).
    /// </summary>
    public static class TreeGuardrails
    {
        /// <summary>Shots per trigger pull (pellets × volley × fan).</summary>
        public const int ShotsPerPull = 24;
        /// <summary>Extra shots per pull from the Shot branch (5 total).</summary>
        public const int ExtraShotsMax = 4;
        public const int LiveProjectilesSoft = 160;
        public const int LiveProjectilesHard = 256;
        public const int ProjectileSpawnsPerSecond = 60;
        public const int ProjectileQueriesPerFrame = 120;
        public const int LiveRockets = 12;
        /// <summary>Full-Auto rl_1 (design §2.3).</summary>
        public const float RocketAutoRpm = 200f;
        /// <summary>Trigger rate cap; the excess goes to damage (design K6).</summary>
        public const float TriggerRpmMax = 1200f;
        public const float TreeFireRateScaleMax = 3f;
        public const int TreeBlastsPerSecond = 12;
        public const int TreeBlastsPerFrame = 4;
        public const int TreeBlastQueue = 32;
        public const int ChainDepth = 3;
        public const float NukeCooldownSeconds = 3f;
        public const int BlackHolesActive = 1;
        public const int StatusSlots = 64;
        public const float StatusTickHz = 4f;
        public const int StatusPhases = 4;
        public const int AreaZones = 16;
        public const int FireGroundsActive = 8;
        public const int VenomCloudsActive = 6;
        public const int OverlapQueriesPerFrame = 8;
        public const int ShockEventsPerSecond = 6;
        public const int ShockJumpsMax = 6;
        public const int RicochetMax = 3;
        public const int PierceMax = 8;
        public const float DodgeChanceMax = 0.30f;
        /// <summary>Tree × meta gold multiplier ceiling.</summary>
        public const float GoldScaleMax = 1.60f;
        /// <summary>Mass deaths (nuke, chain) are presented over this many frames.</summary>
        public const int MassDeathSpreadFrames = 4;
        /// <summary>Design K3.</summary>
        public const float SlowWeaponRpm = 90f;
        /// <summary>Tree systems' CPU budget (TD High, 40 zombies, worst build): mean / p99 in ms.</summary>
        public const float BudgetMeanMs = 0.35f;
        public const float BudgetP99Ms = 1.0f;
    }

    /// <summary>R4 rule: every new tree system carries its marker from its first commit (WP104.Tree.*).</summary>
    public static class TreeMarkers
    {
        public static readonly ProfilerMarker Resolve = new ProfilerMarker("WP104.Tree.Resolve");
        public static readonly ProfilerMarker ShotHooks = new ProfilerMarker("WP104.Tree.ShotHooks");
        public static readonly ProfilerMarker HitHooks = new ProfilerMarker("WP104.Tree.HitHooks");
        public static readonly ProfilerMarker BlastQueue = new ProfilerMarker("WP104.Tree.BlastQueue");
        public static readonly ProfilerMarker Status = new ProfilerMarker("WP104.Tree.Status");
        public static readonly ProfilerMarker AreaZones = new ProfilerMarker("WP104.Tree.AreaZones");
        public static readonly ProfilerMarker Gravity = new ProfilerMarker("WP104.Tree.Gravity");
        public static readonly ProfilerMarker Nuke = new ProfilerMarker("WP104.Tree.Nuke");
        public static readonly ProfilerMarker ProjectileQueries = new ProfilerMarker("WP104.Proj.Queries");
        /// <summary>WP-104 B1 (S-03 request): the zombie-side receivers of tree statuses (ApplyIce / ApplyStun / ApplyPull / ApplyStatusDamage).</summary>
        public static readonly ProfilerMarker ZombieStatus = new ProfilerMarker("WP104.Tree.ZombieStatus");
    }

    /// <summary>
    /// The live resolved stats every hook reads (one static struct; field reads, no copy, no allocation).
    /// Neutral outside a run. B1 writes it on purchase / equip / run start; <see cref="TestStats"/> lets a test drive hooks.
    /// </summary>
    public static class TreeRuntime
    {
        private static TreeStats s_current = TreeStats.Neutral;
        private static TreeStats s_test;
        private static bool s_hasTest;

        /// <summary>The resolved stats for the weapon in hand (test override first).</summary>
        public static ref readonly TreeStats Current
        {
            get
            {
                if (s_hasTest) return ref s_test;
                return ref s_current;
            }
        }

        /// <summary>Bumped on every write; caches compare it.</summary>
        public static int Version { get; private set; }

        /// <summary>The run's difficulty mode (<see cref="TreeMode"/>); Normal outside a run.</summary>
        public static int Mode { get; private set; }

        public static void Publish(in TreeStats stats)
        {
            s_current = stats;
            Version++;
            TreeEvents.RaiseStatsChanged();
        }

        public static void SetMode(int mode)
        {
            Mode = mode < 0 || mode >= TreeMode.Count ? TreeMode.Normal : mode;
        }

        public static void Reset()
        {
            s_current = TreeStats.Neutral;
            Mode = TreeMode.Normal;
            Version++;
        }

        public static void SetTestStats(in TreeStats stats)
        {
            s_test = stats;
            s_hasTest = true;
            Version++;
        }

        public static void ClearTestStats()
        {
            s_hasTest = false;
            Version++;
        }
    }
}
