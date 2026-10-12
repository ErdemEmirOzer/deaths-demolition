using System;
using DD.Prototype.Core;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>WP-104 B4 (S-07) · the VFX side's own markers (B0's TreeMarkers are frozen; R4 rule: a marker from the first commit).</summary>
    public static class TreeVfxMarkers
    {
        public static readonly ProfilerMarker Binder = new ProfilerMarker("WP104.Vfx.Binder");
        public static readonly ProfilerMarker Aura = new ProfilerMarker("WP104.Vfx.Aura");
        public static readonly ProfilerMarker Glow = new ProfilerMarker("WP104.Vfx.Glow");
        /// <summary>The K3 marked rounds' sumi cores (TracerRibbonRenderer.DrawTdInk).</summary>
        public static readonly ProfilerMarker Ink = new ProfilerMarker("WP104.Vfx.Ink");
    }

    /// <summary>
    /// WP-104 B4 (S-07) → S-09 · the round-end tree's live diorama (AD §5.4). <see cref="Begin"/> switches the presentation to the
    /// unscaled clock (<see cref="VfxClock"/>), makes rounds born from then on take <paramref name="stats"/>'s tracer look
    /// (<see cref="TracerStyle"/>), and anchors the player-side layers (aura, ensō, dodge, leech) on the diorama's mannequin.
    /// <see cref="SetStats"/> switches the "before" / "after" halves; proc visuals are played with <see cref="TreeVfxBinder.Play"/>
    /// (not TreeEvents.RaiseProc: no audio, telemetry or gameplay listener sees a preview). <see cref="End"/> clears the tree effects
    /// and gives the clock back.
    /// </summary>
    public static class TreeVfxPreview
    {
        private static TreeStats s_stats;

        public static bool Active { get; private set; }
        public static Transform Anchor { get; private set; }
        public static ref readonly TreeStats Stats => ref s_stats;

        public static void Begin(in TreeStats stats, Transform anchor)
        {
            s_stats = stats;
            Anchor = anchor;
            Active = true;
            VfxClock.BeginPreview();
            TracerStyle.SetPreview(in stats);
        }

        public static void SetStats(in TreeStats stats)
        {
            s_stats = stats;
            if (Active) TracerStyle.SetPreview(in stats);
        }

        public static void End()
        {
            if (!Active) return;
            Active = false;
            Anchor = null;
            TracerStyle.ClearPreview();
            TreeVfxBinder binder = TreeVfxBinder.Instance;
            if (binder != null) binder.Clear();
            VfxClock.EndPreview();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = false;
            Anchor = null;
            s_stats = default;
        }
    }

    /// <summary>
    /// WP-104 B4 (S-07) · <see cref="TreeEvents"/> → AP-041 B1–B18 (AD-VISUAL §3, B4-TASARIM.md table), read with S-04's field
    /// meanings (12.10 §4): Point = the moment's world position (the player's for shield / dodge / undying / leech / Endless), Radius
    /// the gameplay radius (0 = a point), Seconds the moment's length (fire ground, venom cloud, black hole, freeze, Endless; 0 = an
    /// instant), Element the element (a fused blast ORs them, Nova carries every owned one), Tier the raising branch's tier,
    /// Synergy filled on a synergy's own moment (Thunder Cascade bolt / chain blast, Frozen Gravity collapse + ice blast, Barrage
    /// part, Wildfire Rounds, Critical Meltdown), Target the body (callback only), Depth the chain generation (0 = direct, so a
    /// chain blast is ≥ 1). Every tree blast is ONE proc after S-04's queue fused it (its damage goes through the receivers, never
    /// ReportExplosion: this binder is the blast's only visual — card, shock ring, sumi, scorch; an S-02 Explode with
    /// ExplosionSource.Tree draws nothing in ExplosionDirector); a death's chain blast lands a frame or more after the hit that
    /// killed. S-04 B5b: Target is the zombie (ITreeStatusReceiver.Unit), Leech's Point is the victim, OverclockPeak's the muzzle
    /// when S-02 passes it. One DDOL owner made by <see cref="VfxDressing.Configure"/> (as ExplosionDirector), next to
    /// <see cref="TreeAuraVfx"/> (B11 aura, B14 dome) and <see cref="TreeAttachGlow"/> (S-06 add-on emissive ladder); the K3 marked
    /// round and the tracer ladder are TracerRibbonRenderer's (TracerStyle). Every proc picks its sheets, sizes a hit by the tier ladder (AD §2 "İsabet olayı
    /// ölçeği", <see cref="TreeVfxLadder"/>) and an area event by its gameplay radius, adds the tier's single sumi layer (III ink shock, IV ensō on
    /// gravity, V spatter) and keeps the AD caps:
    /// <list type="bullet">
    /// <item>A9 — B5 + B8 ≤ 8, B6 ≤ 1, B7 ≤ 1, tree scorches ≤ 12 (the oldest fades), sumi ≤ 3; over a cap the visual MERGES (a live blast
    /// re-pops × 1.1) — power is the gameplay's, never dropped there; a visual that has nowhere to merge is skipped (K9 order).</item>
    /// <item>K6 — explosive round + chain blast on one hit (≤ <see cref="SameHitRadius"/> m and ≤ <see cref="SameHitSeconds"/> s apart —
    /// S-04's queue presents the death's chain blast at least a frame later): B8 only. Barrage (AD §4.2) — the third explosive or
    /// cluster-part blast within 3 m / 0.25 s folds into one × 1.1.</item>
    /// <item>K1 — a secondary element shows only on its hit at × 0.6, at most two element visuals per body per frame.</item>
    /// <item>One ceremony on screen (WP-068 §1): synergy ensō &gt; undying oath &gt; level-up &gt; heal; a synergy's ensō once per run.</item>
    /// <item>Reduce Flashes (AD §2): no B7 heat glint and shock α 0.35, crit / level-up starburst without its flash (dimmed shape),
    /// B3 flicker off and ≤ 3 hits / s, aura sparks × 0.5, no ring pulse. Large onsets (≥ 43 000 px²) go through the shared FlashGate
    /// (≤ 3 / s, WCAG 2.3.1) in every mode. Reduced Motion: a static ensō for 0.6 s, one static dodge silhouette.</item>
    /// </list>
    /// Hot path 0 B: rows resolved once per configure, own pool (<see cref="PoolCapacity"/>, prewarmed, RecycleOldest), fixed slot
    /// tables, a 32-entry delay queue (dodge ghosts, the per-frame spawn budget <see cref="MaxSpawnsPerFrame"/>). Marker
    /// <c>WP104.Vfx.Binder</c>. <c>-ddTreeVfxOff</c> (development / Editor) keeps it disarmed for the ABBA perf run.
    /// </summary>
    [DefaultExecutionOrder(960)]
    public sealed class TreeVfxBinder : MonoBehaviour
    {
        public static int PoolCapacity => VfxTier.Pick(32, 48, 64);
        /// <summary>Spawns per frame before a proc waits for the next frame (TreeBlastsPerFrame 4 × blast + sumi + scorch).</summary>
        public const int MaxSpawnsPerFrame = 12;
        public const int PendingCapacity = 32;
        public const int RecentCapacity = 8;
        public const int ScorchCapacity = 12;
        public const float SameHitRadius = .75f;
        /// <summary>K6 window: S-04's blast queue presents a death's chain blast ≥ 1 frame after the killing hit and lands a blast that
        /// waited 0.25 s quietly (TreeSystemsTuning.BlastMaxWaitSeconds), so "the same hit" is this long.</summary>
        public const float SameHitSeconds = .25f;
        /// <summary>A body-status moment (ignite / freeze / shock) whose Point is this close above the ground is the body's root:
        /// its hit sheet goes to <see cref="ChestHeight"/>.</summary>
        public const float BodyRootBand = .3f;
        /// <summary>Leech (S-04 B5b): Point is the victim; a thread shorter than this (a body at the hero's feet) is not drawn.</summary>
        public const float LeechMinLength = .3f;
        public const float BarrageMergeRadius = 3f, BarrageMergeSeconds = .25f, BarrageMergeScale = 1.1f;
        public const int BarrageMergeCount = 3;
        public const float ChestHeight = 1.2f, GhostHeight = .9f, FeetLift = .04f, DomeLift = 1f;
        public const float LeechSeconds = .45f, ThreadThickness = .6f;
        public const float GhostSeconds = .5f, GhostAlpha = .55f;
        public static readonly float[] GhostDelays = { 0f, .08f, .16f };
        public const float EnsoSeconds = .8f, EnsoFade = .25f, EnsoStaticSeconds = .6f, EnsoAlpha = .7f;
        public const float NukeShockAlphaReduced = .35f, DimFlashGain = .35f;
        public const int ShockHitsPerSecondReduced = 3;
        public const float GroundProbe = 3f;
        public const float WhiteHotSeconds = .05f;
        /// <summary>AD-B4 M7: a camera-facing underdraw sits this much farther from the camera than its layer, so the back-to-front
        /// transparent queue draws it first (the two cards share a centre otherwise: an undefined order).</summary>
        public const float UnderBehindMetres = .05f;

        private enum Fam
        {
            Blast = 0, Nuke, NukeShock, Glint, Hole, HoleRing, HoleEnso, Endless, Sumi, Crit, ElementHit, Coating, Cloud, Thread, Ghost,
            Ceremony, Misc, ShieldHit, Under, Count
        }

        // A9 / K9 caps per family (sum stays under the High pool of 48 in any realistic frame); Under: the threads' jade underdraws
        private static readonly int[] s_caps = { 8, 1, 1, 1, 1, 1, 1, 1, 3, 4, 6, 6, TreeGuardrails.VenomCloudsActive, 4, 4, 2, 6, 1, 4 };

        private const int RecentAny = 0, RecentExplosive = 1, RecentChain = 2;
        private const int CeremonyHeal = 0, CeremonyLevelUp = 1, CeremonyUndying = 2, CeremonySynergy = 3;

        private struct Recent
        {
            public bool Used;
            public int Kind, Count;
            public float At, Size;
            public Vector3 Point;
            public FlipbookEffect Fx;
        }

        private struct Pending
        {
            public bool Active;
            public TreeProcKind Kind;
            public Vector3 Point;
            public float Radius, At;
            public TreeElement Element;
            public TreeSynergy Synergy;
            public int Tier, Depth, Frame;
            public float Seconds;
        }

        private static TreeVfxBinder s_instance;
        /// <summary>A ground card lying face up, its +U along world +X and +V along +Z: the TD camera reads it unmirrored (the X8
        /// ensō's left / right tints and every brush stroke keep their side; TreeAuraVfx's Euler(90, 0, 0) is the same frame).</summary>
        private static readonly Quaternion Flat = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        private static int s_groundMask;

        private VfxDressing _owner;
        private TreeAuraVfx _aura;
        private TreeAttachGlow _glow;
        private PrototypePool<FlipbookEffect> _pool;
        private readonly VfxCatalogEntry[] _rows = new VfxCatalogEntry[(int)TreeVfxRow.Count];
        private readonly FlipbookEffect[][] _slots = new FlipbookEffect[(int)Fam.Count][];
        private readonly VfxCatalogEntry[][] _slotRows = new VfxCatalogEntry[(int)Fam.Count][];
        private readonly Recent[] _recent = new Recent[RecentCapacity];
        private readonly Pending[] _pending = new Pending[PendingCapacity];
        private readonly DecalEffect[] _scorches = new DecalEffect[ScorchCapacity];
        private readonly VfxCatalogEntry[] _scorchRows = new VfxCatalogEntry[ScorchCapacity];   // a recycled decal is another system's
        private readonly int[] _hitIds = new int[8];
        private readonly int[] _hitFrames = new int[8];
        private readonly int[] _hitCounts = new int[8];
        private readonly float[] _shockHits = new float[ShockHitsPerSecondReduced];
        private int _fireId;
        private PlayerRig _weaponsRig;
        private DD.Prototype.Weapons.WeaponController _weapons;
        private readonly int[] _byKind = new int[32];
        private int _recentNext, _scorchNext, _scorchTurn, _hitNext, _shockNext, _frame = -1, _spawnsThisFrame;
        private bool _configured, _armed, _subscribedScene;
        private float _ceremonyUntil = -1f;
        private int _ceremonyPriority = -1;
        private TreeSynergy _synergyShown;
        private int _lastTiers;
        private bool _pendingHeal;
        private FlipbookEffect _endlessFx;

        public static TreeVfxBinder Instance => s_instance;

        /// <summary>All <see cref="TreeVfxRow"/> rows are in the catalog with a material (ArtAp041TreeIntake ran).</summary>
        public bool RowsReady { get; private set; }
        public int RowsResolved { get; private set; }
        public TreeAuraVfx Aura => _aura;
        public TreeAttachGlow AttachGlow => _glow;

        // readouts (tests / F1)
        public int ProcsSeen { get; private set; }
        public int Played { get; private set; }
        public int Merged { get; private set; }
        public int Replaced { get; private set; }
        public int Dropped { get; private set; }
        public int Deferred { get; private set; }
        public int Missing { get; private set; }
        public int CeremoniesPlayed { get; private set; }
        public int CeremoniesDropped { get; private set; }
        /// <summary>FireGround procs handed to WP-073's GroundFireSystem (the flames; B4 lays the B1 scorch).</summary>
        public int FireZonesFed { get; private set; }
        /// <summary>Leech procs without a victim point (default) or with the victim at the hero's feet: no thread.</summary>
        public int LeechUnresolved { get; private set; }
        public FlipbookEffect LastSpawned { get; private set; }
        public TreeVfxRow LastSpawnedRow { get; private set; } = TreeVfxRow.Count;
        /// <summary>The last leech thread's jade underdraw (AD-B4 M7; LastSpawned stays the thread itself).</summary>
        public FlipbookEffect LastUnderlay { get; private set; }
        /// <summary>Endless Window is open (S-06 reads it for the E2 mask's ≤ 1 Hz indigo pulse).</summary>
        public static bool EndlessWindowActive { get; private set; }

        public int PlayedByKind(TreeProcKind kind) => (int)kind >= 0 && (int)kind < _byKind.Length ? _byKind[(int)kind] : 0;
        public int LiveBlasts => Live(Fam.Blast);
        public int LiveNukes => Live(Fam.Nuke);
        public int LiveHoles => Live(Fam.Hole);
        public int LiveCrits => Live(Fam.Crit);
        public int LiveSumi => Live(Fam.Sumi);
        public int LiveClouds => Live(Fam.Cloud);
        public int LiveGlints => Live(Fam.Glint);
        public int LiveCeremonies => Live(Fam.Ceremony);
        public int PendingCount { get { int n = 0; for (int i = 0; i < _pending.Length; i++) if (_pending[i].Active) n++; return n; } }
        public VfxCatalogEntry Row(TreeVfxRow row) => row >= 0 && row < TreeVfxRow.Count ? _rows[(int)row] : null;

        /// <summary>Made once by <see cref="VfxDressing.Configure"/> (idempotent): rows, pool, aura, add-on glow, subscriptions.</summary>
        public static TreeVfxBinder Ensure(VfxDressing owner)
        {
            if (s_instance == null) s_instance = FindObjectOfType<TreeVfxBinder>();
            if (s_instance == null)
            {
                var go = new GameObject("[TreeVfx]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<TreeVfxBinder>();
            }

            s_instance._owner = owner;
            s_instance.Configure();
            return s_instance;
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            for (int f = 0; f < (int)Fam.Count; f++)
            {
                _slots[f] = new FlipbookEffect[s_caps[f]];
                _slotRows[f] = new VfxCatalogEntry[s_caps[f]];
            }

            _aura = GetComponent<TreeAuraVfx>();
            if (_aura == null) _aura = gameObject.AddComponent<TreeAuraVfx>();
            _glow = GetComponent<TreeAttachGlow>();
            if (_glow == null) _glow = gameObject.AddComponent<TreeAttachGlow>();
            s_groundMask = (1 << 0) | (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
        }

        private void Configure()
        {
            int resolved = 0;
            for (int i = 0; i < (int)TreeVfxRow.Count; i++)
            {
                _rows[i] = VfxLibrary.TryGetPlayable(TreeVfxIds.Id((TreeVfxRow)i), out VfxCatalogEntry e) ? e : null;
                if (_rows[i] != null) resolved++;
            }

            RowsResolved = resolved;
            RowsReady = resolved == (int)TreeVfxRow.Count;
            if (_pool == null)
            {
                var template = new GameObject("FX_WP104_Tree");
                template.transform.SetParent(transform, false);
                template.SetActive(false);
                _pool = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(template.AddComponent<FlipbookEffect>(), PoolCapacity, PoolCapacity,
                    PoolExhaustedPolicy.RecycleOldest));
            }

            if (_aura != null)
            {
                _aura.Configure(this);
                _aura.enabled = !DevOff;   // -ddTreeVfxOff: the ABBA perf run's B side draws no tree layer at all
            }

            if (_glow != null) _glow.enabled = !DevOff;
            _lastTiers = TreeRuntime.Current.BranchTiers;
            _configured = true;
            if (!DevOff) Arm();
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static readonly bool DevOff = Array.IndexOf(Environment.GetCommandLineArgs(), "-ddTreeVfxOff") >= 0;
#else
        private const bool DevOff = false;
#endif

        // ------------------------------------------------------------------ subscriptions (WP-062J re-arm: TreeEvents.ResetForTests clears them)

        private void Arm()
        {
            Disarm();
            TreeEvents.Proc += OnProc;
            TreeEvents.AuraChanged += OnAuraChanged;
            TreeEvents.NodePurchased += OnNodePurchased;
            RunEvents.RunStarted += OnRunStarted;
            RunEvents.RoundStarted += OnRoundStarted;
            if (_glow != null) _glow.Subscribe();   // S-06 WeaponAttachEvents.Changed
            _armed = true;
            if (!_subscribedScene)
            {
                _subscribedScene = true;
                SceneManager.activeSceneChanged += OnSceneChanged;
            }
        }

        private void Disarm()
        {
            TreeEvents.Proc -= OnProc;
            TreeEvents.AuraChanged -= OnAuraChanged;
            TreeEvents.NodePurchased -= OnNodePurchased;
            RunEvents.RunStarted -= OnRunStarted;
            RunEvents.RoundStarted -= OnRoundStarted;
            _armed = false;
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            Clear();
            if (_configured && !DevOff) Arm();
        }

        private void OnDestroy()
        {
            Disarm();
            if (_subscribedScene) SceneManager.activeSceneChanged -= OnSceneChanged;
            _subscribedScene = false;
            if (s_instance == this) s_instance = null;
        }

        /// <summary>Tests: re-subscribe after a fixture cleared the static buses.</summary>
        public void Rearm()
        {
            if (_configured) Arm();
        }

        public bool Armed => _armed;

        // ------------------------------------------------------------------ events

        private void OnProc(TreeProcInfo info)
        {
            using (TreeVfxMarkers.Binder.Auto())
            {
                Play(in info);
            }
        }

        private void OnAuraChanged(int level)
        {
            if (_aura != null) _aura.SetLevel(level, true);
        }

        private void OnNodePurchased(string nodeId, TreeBranch branch, int tier)
        {
            // AD §3: Iron Body I–III → the healing ground pulse (B17) at the next round's start
            if (branch == TreeBranch.Survival && nodeId != null && nodeId.StartsWith("sv_hp", StringComparison.Ordinal)) _pendingHeal = true;
        }

        private void OnRunStarted(int round)
        {
            _synergyShown = TreeSynergy.None;
            _lastTiers = TreeRuntime.Current.BranchTiers;
            _pendingHeal = false;
            EndlessWindowActive = false;
            if (_aura != null) _aura.ResetShield();
        }

        /// <summary>The round-end tree closed: a branch that climbed a tier gets the global level-up (B13), else Iron Body's pulse (B17).</summary>
        private void OnRoundStarted(int round)
        {
            int tiers = TreeRuntime.Current.BranchTiers;
            if (TierIncreased(_lastTiers, tiers)) LevelUp();
            else if (_pendingHeal) Heal();
            _lastTiers = tiers;
            _pendingHeal = false;
            if (_aura != null) _aura.ResetShield();   // S-04 fills the shield at the round start without a proc
        }

        public static bool TierIncreased(int before, int after)
        {
            for (int b = 0; b < 7; b++)
            {
                if (((after >> (b * 3)) & 7) > ((before >> (b * 3)) & 7)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ dispatch

        /// <summary>One tree moment → its visuals (TreeEvents.Proc, or the S-09 diorama directly). False = nothing drawn.</summary>
        public bool Play(in TreeProcInfo info)
        {
            ProcsSeen++;
            int k = (int)info.Kind;
            if (k >= 0 && k < _byKind.Length) _byKind[k]++;
            if (!_configured) return false;
            BeginFrame();
            int frame = Time.frameCount;
            if (_spawnsThisFrame >= MaxSpawnsPerFrame && Deferrable(info.Kind))
            {
                return Defer(info.Kind, info.Point, info.Radius, info.Element, info.Tier, info.Depth, info.Synergy, info.Seconds, 0f, frame);
            }

            return Dispatch(info.Kind, info.Point, info.Radius, info.Element, info.Tier, info.Depth, info.Synergy, info.Seconds, info.Target, frame);
        }

        private static bool Deferrable(TreeProcKind kind)
        {
            switch (kind)
            {
                case TreeProcKind.ShieldBreak:
                case TreeProcKind.ShieldUp:
                case TreeProcKind.Dodge:
                case TreeProcKind.UndyingOath:
                case TreeProcKind.EndlessWindowOpen:
                case TreeProcKind.EndlessWindowClose:
                case TreeProcKind.SynergyFirstTrigger:
                case TreeProcKind.OverclockPeak:
                    return false;
                default:
                    return true;
            }
        }

        private bool Dispatch(TreeProcKind kind, Vector3 p, float radius, TreeElement element, int tier, int depth, TreeSynergy synergy, float seconds,
            Component target, int frame)
        {
            switch (kind)
            {
                case TreeProcKind.Crit: return Crit(p, tier, 0f);
                case TreeProcKind.CritRing: return Crit(p, tier, radius);
                case TreeProcKind.ExplosiveRound: return Explosive(p, radius, tier, element);
                case TreeProcKind.ChainBlast: return Chain(p, radius, tier, depth, synergy);
                case TreeProcKind.MiniNuke: return Nuke(p, radius, tier, element);
                case TreeProcKind.BlackHoleOpen: return HoleOpen(p, radius, tier, element, seconds);
                case TreeProcKind.BlackHoleCollapse: return HoleCollapse(p, radius, tier, element, synergy);
                case TreeProcKind.Ignite: return ElementHit(TreeElement.Fire, BodyPoint(p, target), tier, target);
                case TreeProcKind.FireGround: return FireGround(p, radius, seconds);
                case TreeProcKind.Freeze: return Freeze(BodyPoint(p, target), tier, target, seconds);
                case TreeProcKind.Shatter: return Shatter(p, radius, tier);
                case TreeProcKind.ShockChain: return Shock(BodyPoint(p, target), tier, depth, target);
                case TreeProcKind.VenomCloud: return Cloud(p, radius, tier, seconds);
                case TreeProcKind.ElementNova: return Nova(p, radius, element);
                case TreeProcKind.DoomsdayBell: return Bell(p, radius);
                case TreeProcKind.ShieldBreak: return ShieldBreak();
                case TreeProcKind.ShieldUp: return ShieldUp();
                case TreeProcKind.ShieldHit: return ShieldHit();
                case TreeProcKind.Dodge: return Dodge(depth);
                case TreeProcKind.Leech: return Leech(p);
                case TreeProcKind.UndyingOath: return Undying();
                case TreeProcKind.EndlessWindowOpen: return Endless(true, seconds);
                case TreeProcKind.EndlessWindowClose: return Endless(false, 0f);
                case TreeProcKind.OverclockPeak: return Overclock(p, tier);
                case TreeProcKind.SynergyFirstTrigger: return Synergy(synergy);
                case TreeProcKind.ClusterSplit: return Cluster(p, tier);
                // Ricochet (27) and BarrageRocket (28): the round's own tracer + impact and RocketVisualSystem carry them (B10 launch
                // flame: B4-2) - no B4 world visual of their own
                default: return false;
            }
        }

        // ------------------------------------------------------------------ Kaos (B5 B6 B7 B8 B13)

        private bool Crit(Vector3 p, int tier, float ringRadius)
        {
            if (Live(Fam.Crit) >= s_caps[(int)Fam.Crit])
            {
                Dropped++;   // the damage number carries the crit (K9: the event scale goes last, a fourth starburst first)
                return true;
            }

            float size = WorldSize(TreeVfxRow.B13Crit) * TreeVfxLadder.ImpactScale(tier);
            bool flash = FlashAllowed(size);
            Spawn(TreeVfxRow.B13Crit, Fam.Crit, p, Quaternion.identity, FlipbookEffect.Facing.Camera, size, flash ? Color.white : Dim(), flash ? 1f : .6f, .3f, -1f);
            if (ringRadius > 0f)
            {
                Spawn(TreeVfxRow.T3InkShock, Fam.Sumi, Ground(p) + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                    RadiusSize(TreeVfxRow.T3InkShock, ringRadius, tier, 1f), Color.white, .8f, .4f, -1f);
            }

            return true;
        }

        private bool Explosive(Vector3 p, float radius, int tier, TreeElement element)
        {
            // K6: a chain blast on this hit already owns the moment
            if (FindRecent(p, SameHitRadius, SameHitSeconds, RecentChain) >= 0)
            {
                Merged++;
                return true;
            }

            float size = RadiusSize(TreeVfxRow.B5Explosive, radius, tier, 1f);
            if (FoldBarrage(p, size, out int r)) return true;

            FlipbookEffect fx = Spawn(TreeVfxRow.B5Explosive, Fam.Blast, p, Quaternion.identity, FlipbookEffect.Facing.Camera, size, Color.white, 1f, .3f, -1f);
            if (fx == null)
            {
                Merged++;   // A9 B5 + B8 ≤ 8: the live blasts carry it
                return true;
            }

            Vector3 g = Ground(p);
            // the blast's ground shock ring: a tree blast never reaches ReportExplosion, so FeelBrush draws no ring for it (TD included)
            Spawn(TreeVfxRow.B5Shock, Fam.Misc, g + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                RadiusSize(TreeVfxRow.B5Shock, radius, tier, 1.3f), Color.white, .7f, .4f, -1f);

            SumiLayer(g, tier, false, size);
            Scorch(p, radius);
            // a blast S-04's queue fused with an element blast carries its element (K2 rim stand-in; the ground fire of Wildfire
            // Rounds is its own FireGround proc)
            if (element != TreeElement.None) SecondaryHit(TreeVfxLadder.PickElement(element, TracerStyle.DominantElement), p, tier);
            if (r < 0) Remember(p, fx, RecentExplosive, size);
            return true;
        }

        /// <summary>
        /// Barrage (AD §4.2): the third B5 (explosive round or Barrage cluster part) within 3 m / 0.25 s and every later one fold
        /// into the live blast × 1.1 (true = folded, nothing to spawn). <paramref name="recent"/> is the nearby entry, −1 when none.
        /// </summary>
        private bool FoldBarrage(Vector3 p, float size, out int recent)
        {
            recent = FindRecent(p, BarrageMergeRadius, BarrageMergeSeconds, RecentAny);
            if (recent < 0) return false;
            ref Recent rec = ref _recent[recent];
            rec.Count++;
            if (rec.Count < BarrageMergeCount) return false;
            if (Alive(rec.Fx, TreeVfxRow.B5Explosive))
            {
                rec.Size = Mathf.Max(rec.Size, size) * BarrageMergeScale;
                rec.Fx.Play(rec.Fx.Row, FlipbookEffect.Facing.Camera, rec.Size, Color.white, 1f, .3f, -1f);
            }

            Merged++;
            return true;
        }

        /// <summary>B8: <paramref name="depth"/> is S-04's chain generation — the first chain blast of a weapon kill is 1 (0 = the direct
        /// hit), so the visual generation is depth − 1: full size and the sumi layer on the first, × 0.85 per generation after. AD-B4
        /// M10: the size is the chain's damage area × the tier's event scale (<see cref="TreeVfxLadder.ChainCardSize"/>: R 3 m at III
        /// → a 5.4 m card), and its sumi ring follows the card as B5's does.</summary>
        private bool Chain(Vector3 p, float radius, int tier, int depth, TreeSynergy synergy)
        {
            int gen = Mathf.Max(0, depth - 1);
            // K6: an explosive round on the same hit yields to the chain
            int r = FindRecent(p, SameHitRadius, SameHitSeconds, RecentExplosive);
            if (r >= 0)
            {
                if (Alive(_recent[r].Fx, TreeVfxRow.B5Explosive)) _recent[r].Fx.Despawn();
                _recent[r].Kind = RecentChain;
                Replaced++;
            }

            float size = TreeVfxLadder.ChainCardSize(radius, tier, gen, WorldSize(TreeVfxRow.B8Chain1));
            FlipbookEffect fx = Spawn(TreeVfxIds.ChainRow(gen), Fam.Blast, p, Quaternion.identity, FlipbookEffect.Facing.Camera, size, Color.white, 1f, .3f, -1f);
            if (fx == null)
            {
                Merged++;
                return true;
            }

            if (gen == 0) SumiLayer(Ground(p), tier, false, size);   // B5's rule: the ink ring × 1.2 of the card (T3 clamp)
            // Thunder Cascade (S-04 marks the chain blast with the synergy; its bolts are their own ShockChain procs)
            if ((synergy & TreeSynergy.ThunderCascade) != 0)
            {
                Spawn(TreeVfxRow.B3SparkRing, Fam.Misc, Ground(p) + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                    WorldSize(TreeVfxRow.B3SparkRing) * TreeVfxLadder.ImpactScale(tier), Color.white, 1f, .3f, -1f);
            }

            if (r >= 0)
            {
                _recent[r].Fx = fx;
                _recent[r].Size = size;
            }
            else
            {
                Remember(p, fx, RecentChain, size);
            }

            return true;
        }

        private bool Nuke(Vector3 p, float radius, int tier, TreeElement element)
        {
            if (Live(Fam.Nuke) > 0)
            {
                Merged++;   // A9: one nuke on screen (its 3 s cooldown keeps it so anyway)
                return true;
            }

            bool reduced = ReducedFlashes;
            Vector3 g = Ground(p);
            float plume = RadiusSize(TreeVfxRow.B7Nuclear, radius, tier, 1f);
            Spawn(TreeVfxRow.B7Nuclear, Fam.Nuke, g + Vector3.up * .1f, Quaternion.identity, FlipbookEffect.Facing.Camera, plume, Color.white, .9f, .35f, -1f);
            Spawn(TreeVfxRow.B7Shock, Fam.NukeShock, g + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                RadiusSize(TreeVfxRow.B7Shock, radius, tier, 1.6f), Color.white, reduced ? NukeShockAlphaReduced : 1f, .3f, -1f);
            // AD §2 RF: the heat glint goes; with flashes on it still passes the FlashGate (large onset)
            float glint = WorldSize(TreeVfxRow.B7HeatGlint) * TreeVfxLadder.ImpactScale(tier);
            if (!reduced && UiMotion.FlashOnset(TreeVfxLadder.TdAreaPixels(glint) >= TreeVfxLadder.LargeFlashPixels))
            {
                Spawn(TreeVfxRow.B7HeatGlint, Fam.Glint, g + Vector3.up * .2f, Quaternion.identity, FlipbookEffect.Facing.Camera, glint, Color.white, 1f, .2f, -1f);
            }

            SumiLayer(g, tier, false, plume);
            Scorch(p, radius);
            if (element != TreeElement.None) SecondaryHit(TreeVfxLadder.FirstElement(element), p, tier);   // K2 stand-in (rim mask B4-2)
            return true;
        }

        private bool HoleOpen(Vector3 p, float radius, int tier, TreeElement element, float seconds)
        {
            if (Live(Fam.Hole) > 0)
            {
                Merged++;   // TreeGuardrails.BlackHolesActive = 1
                return true;
            }

            float hold = seconds > 0f ? seconds : TreeRuntime.Current.HoleSeconds > 0f ? TreeRuntime.Current.HoleSeconds : 2.5f;
            Vector3 g = Ground(p);
            Spawn(TreeVfxRow.B6Singularity, Fam.Hole, p, Quaternion.identity, FlipbookEffect.Facing.Camera,
                RadiusSize(TreeVfxRow.B6Singularity, radius, tier, .67f), Color.white, 1f, .2f, hold);
            Spawn(TreeVfxRow.B6Rings, Fam.HoleRing, g + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                RadiusSize(TreeVfxRow.B6Rings, radius, tier, 1f), Color.white, .8f, .2f, hold);
            // IV: the ensō attraction ring over the physical singularity (centre kept clear by the sheet)
            if (TreeVfxLadder.SumiFor(tier, true) == TreeVfxLadder.Sumi.Enso)
            {
                Spawn(TreeVfxRow.T4Enso, Fam.HoleEnso, g + Vector3.up * (FeetLift + .01f), Flat, FlipbookEffect.Facing.Fixed,
                    RadiusSize(TreeVfxRow.T4Enso, radius, tier, 1f), Color.white, .7f, .2f, hold);
            }

            if (element != TreeElement.None) SecondaryHit(TreeVfxLadder.PickElement(element, TracerStyle.DominantElement), p, tier);   // K2 stand-in
            return true;
        }

        private bool HoleCollapse(Vector3 p, float radius, int tier, TreeElement element, TreeSynergy synergy)
        {
            DespawnFamily(Fam.Hole);
            DespawnFamily(Fam.HoleRing);
            DespawnFamily(Fam.HoleEnso);
            Vector3 g = Ground(p);
            Spawn(TreeVfxRow.T3InkShock, Fam.Sumi, g + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                RadiusSize(TreeVfxRow.T3InkShock, radius, Mathf.Max(3, tier), 1f), Color.white, .8f, .4f, -1f);
            // Frozen Gravity: S-04 raises its ice blast as its own Shatter proc (B2 shards there); only when the queue fused that ice
            // blast into this collapse (Element carries ice) does the collapse draw the breaking glass itself
            if ((synergy & TreeSynergy.FrozenGravity) != 0 && (element & TreeElement.Ice) != 0)
            {
                Spawn(TreeVfxRow.B2Shards, Fam.ElementHit, p, Quaternion.identity, FlipbookEffect.Facing.Camera,
                    RadiusSize(TreeVfxRow.B2Shards, radius, tier, 1f), Color.white, 1f, .3f, -1f);
            }

            return true;
        }

        // ------------------------------------------------------------------ Element (B1 B2 B3 B4)

        private bool ElementHit(TreeElement element, Vector3 p, int tier, Component target)
        {
            if (!TakeHitVisual(target, p))
            {
                Dropped++;   // K1: two element visuals per hit
                return true;
            }

            TreeVfxRow row = TreeVfxIds.ElementImpact(element);
            float k = TreeVfxLadder.ImpactScale(tier) * (IsSecondary(element) ? TreeVfxLadder.SecondaryElementScale : 1f);
            return Spawn(row, Fam.ElementHit, p, Quaternion.identity, FlipbookEffect.Facing.Camera, WorldSize(row) * k, Color.white, 1f, .3f, -1f) != null;
        }

        private void SecondaryHit(TreeElement element, Vector3 p, int tier)
        {
            TreeVfxRow row = TreeVfxIds.ElementImpact(element);
            if (row == TreeVfxRow.Count) return;
            Spawn(row, Fam.ElementHit, p, Quaternion.identity, FlipbookEffect.Facing.Camera,
                WorldSize(row) * TreeVfxLadder.ImpactScale(tier) * TreeVfxLadder.SecondaryElementScale, Color.white, .8f, .3f, -1f);
        }

        /// <summary>
        /// A fire zone opened (S-04 12.10 §5: one proc per zone, Seconds = its life, Damage = dps; ticks raise nothing, a merged zone
        /// raises nothing). The flames are WP-073's GroundFireSystem, fed from here (it listens to the old tree's RunTreeGroundFire
        /// only; its zone goes out on its own clock) as a tree zone: AD-B4 M8's #E0702A ember underdraw under its glow; B4 lays the
        /// B1 scorch, α 0.35, outliving the fire by 2 s.
        /// </summary>
        private bool FireGround(Vector3 p, float radius, float seconds)
        {
            float life = seconds > 0f ? seconds : TreeRuntime.Current.FireGroundSeconds > 0f ? TreeRuntime.Current.FireGroundSeconds : 2f;
            GroundFireSystem flames = GroundFireSystem.Instance;
            if (flames != null && !TreeVfxPreview.Active)
            {
                _fireId = _fireId <= int.MinValue + 1 ? -1 : _fireId - 1;   // negative ids: never one of RunTreeGroundFire's
                flames.OnTreeIgnited(new GroundFireInfo(_fireId, p, radius, 0f, life, life));
                FireZonesFed++;
            }

            return LayScorch(TreeVfxRow.B1GroundScorch, p, Mathf.Max(WorldSize(TreeVfxRow.B1GroundScorch), radius * 2f), life + 2f, .35f);
        }

        private bool Freeze(Vector3 p, int tier, Component target, float seconds)
        {
            if (!TakeHitVisual(target, p))
            {
                Dropped++;
                return true;
            }

            bool secondary = IsSecondary(TreeElement.Ice);
            float k = TreeVfxLadder.ImpactScale(tier) * (secondary ? TreeVfxLadder.SecondaryElementScale : 1f);
            Spawn(TreeVfxRow.B2IceImpact, Fam.ElementHit, p, Quaternion.identity, FlipbookEffect.Facing.Camera, WorldSize(TreeVfxRow.B2IceImpact) * k, Color.white, 1f, .3f, -1f);
            // the crystal coat grows over the freeze for its Seconds (Elite half, Ultra slowed only: S-03 sends no Freeze for an Ultra)
            float hold = seconds > 0f ? seconds : TreeRuntime.Current.FreezeSeconds > 0f ? TreeRuntime.Current.FreezeSeconds : 1.5f;
            Spawn(TreeVfxRow.B2Coating, Fam.Coating, p, Quaternion.identity, FlipbookEffect.Facing.Camera,
                WorldSize(TreeVfxRow.B2Coating) * .5f, Color.white, .8f, .25f, hold);
            return true;
        }

        private bool Shatter(Vector3 p, float radius, int tier)
        {
            float size = RadiusSize(TreeVfxRow.B2Shards, radius, tier, 1f);
            Spawn(TreeVfxRow.B2Shards, Fam.ElementHit, p, Quaternion.identity, FlipbookEffect.Facing.Camera, size, Color.white, 1f, .3f, -1f);
            Spawn(TreeVfxRow.B2IceImpact, Fam.ElementHit, p, Quaternion.identity, FlipbookEffect.Facing.Camera,
                WorldSize(TreeVfxRow.B2IceImpact) * TreeVfxLadder.ImpactScale(tier), Color.white, 1f, .3f, -1f);
            SumiLayer(Ground(p), tier, false, size);
            return true;
        }

        private bool Shock(Vector3 p, int tier, int depth, Component target)
        {
            bool reduced = ReducedFlashes;
            if (reduced && !ShockGate())
            {
                Dropped++;   // AD §2 RF: the electric flicker ≤ 3 Hz (the chain line itself is ReportLink's)
                return true;
            }

            if (!TakeHitVisual(target, p))
            {
                Dropped++;
                return true;
            }

            float k = TreeVfxLadder.ImpactScale(tier) * Mathf.Pow(.75f, Mathf.Max(0, depth)) * (IsSecondary(TreeElement.Shock) ? TreeVfxLadder.SecondaryElementScale : 1f);
            Spawn(TreeVfxRow.B3Impact, Fam.ElementHit, p, Quaternion.identity, FlipbookEffect.Facing.Camera, WorldSize(TreeVfxRow.B3Impact) * k, Color.white, 1f, .3f, -1f);
            if (!reduced)
            {
                Spawn(TreeVfxRow.B3Flash, Fam.Misc, p, Quaternion.identity, FlipbookEffect.Facing.Camera, WorldSize(TreeVfxRow.B3Flash) * k * .8f, Color.white, 1f, 0f, -1f);
            }

            if (tier >= 2 && depth == 0)
            {
                Spawn(TreeVfxRow.B3SparkRing, Fam.Misc, Ground(p) + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                    WorldSize(TreeVfxRow.B3SparkRing) * k, Color.white, 1f, .3f, -1f);
            }

            // the struck body's electrocution rim (the Raiju path's look; a new hop extends, never stacks)
            var body = target as ZombieEnemy;
            if (body == null && target != null) target.TryGetComponent(out body);
            if (body != null && body.Skinned != null)
            {
                float stagger = TreeRuntime.Current.ShockStaggerSeconds;
                ZombieSkinFx.Shock(body.Skinned, stagger > 0f ? stagger : .3f);
            }

            return true;
        }

        /// <summary>A venom zone opened (S-04 §5: one proc, Seconds = its life): the cloud loops for that long on the ground.</summary>
        private bool Cloud(Vector3 p, float radius, int tier, float seconds)
        {
            float life = seconds > 0f ? seconds : TreeRuntime.Current.VenomCloudSeconds > 0f ? TreeRuntime.Current.VenomCloudSeconds : -1f;
            FlipbookEffect fx = Spawn(TreeVfxRow.B4Cloud, Fam.Cloud, Ground(p) + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                RadiusSize(TreeVfxRow.B4Cloud, radius, tier, 1f), Color.white, .6f, .35f, life);
            if (fx == null) Merged++;   // TreeGuardrails.VenomCloudsActive: the zones merge, so do the clouds
            return true;
        }

        private bool Nova(Vector3 p, float radius, TreeElement element)
        {
            // S-04's nova carries every owned element: it shows the dominant one (K1)
            TreeElement e = TreeVfxLadder.PickElement(element, TracerStyle.DominantElement);
            if (e == TreeElement.None) e = TracerStyle.DominantElement;
            TreeVfxRow row = TreeVfxIds.ElementImpact(e);
            Vector3 g = Ground(p);
            float size = RadiusSize(row == TreeVfxRow.Count ? TreeVfxRow.T5Spatter : row, radius, 5, 1f);
            if (row != TreeVfxRow.Count)
            {
                bool flash = FlashAllowed(size);
                Spawn(row, Fam.Sumi, g + Vector3.up * .1f, Quaternion.identity, FlipbookEffect.Facing.Camera, size, flash ? Color.white : Dim(), 1f, .3f, -1f);
            }

            SumiLayer(g, 5, false, size);
            return true;
        }

        private bool Bell(Vector3 p, float radius)
        {
            Vector3 g = Ground(p);
            float size = RadiusSize(TreeVfxRow.B7Shock, radius, 5, 1f);
            bool flash = FlashAllowed(size);
            Spawn(TreeVfxRow.B7Shock, Fam.NukeShock, g + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed, size,
                flash ? Color.white : Dim(), ReducedFlashes ? NukeShockAlphaReduced : 1f, .3f, -1f);
            SumiLayer(g, 5, false, size * .5f);
            return true;
        }

        // ------------------------------------------------------------------ Hayatta kalma (B14 B15 B16 B17)

        private bool ShieldBreak()
        {
            Transform a = PlayerAnchor();
            if (_aura != null) _aura.OnShieldBreak();   // the dome waits for ShieldUp (25) or the next round
            if (a == null) return false;
            return Spawn(TreeVfxRow.B14Break, Fam.Misc, a.position + Vector3.up * DomeLift, Flat, FlipbookEffect.Facing.Fixed,
                WorldSize(TreeVfxRow.B14Break), Color.white, 1f, .3f, -1f) != null;
        }

        /// <summary>S-04 ShieldUp (25, the old "ShieldRestored"): refilled to full or up again — the B14 dome stands again.</summary>
        private bool ShieldUp()
        {
            if (_aura == null) return false;
            _aura.OnShieldUp();
            return true;
        }

        /// <summary>S-04 ShieldHit (26): a hit the shield absorbed without breaking — the B14 hit wave on the dome (≤ 1 live).</summary>
        private bool ShieldHit()
        {
            Transform a = PlayerAnchor();
            if (a == null) return false;
            return Spawn(TreeVfxRow.B14Hit, Fam.ShieldHit, a.position + Vector3.up * DomeLift, Flat, FlipbookEffect.Facing.Fixed,
                WorldSize(TreeVfxRow.B14Hit), Color.white, 1f, .3f, -1f) != null;
        }

        /// <summary>B16: three ink ghosts 0 / 0.08 / 0.16 s behind (Reduced Motion: one still silhouette) and the ink shadow.</summary>
        private bool Dodge(int ghost)
        {
            Transform a = PlayerAnchor();
            if (a == null) return false;
            bool still = UiMotion.ReducedMotion;
            Vector3 forward = Vector3.ProjectOnPlane(a.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            Quaternion lying = Quaternion.LookRotation(Vector3.down, forward.normalized);
            Spawn(TreeVfxRow.B16Ghost, Fam.Ghost, a.position + Vector3.up * GhostHeight, lying, FlipbookEffect.Facing.Fixed,
                WorldSize(TreeVfxRow.B16Ghost), Color.white, GhostAlpha, still ? 0f : 1f, GhostSeconds);
            if (ghost > 0 || still) return true;
            Spawn(TreeVfxRow.B16Shadow, Fam.Ghost, Ground(a.position) + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                WorldSize(TreeVfxRow.B16Shadow), Color.white, 1f, .3f, -1f);
            for (int i = 1; i < GhostDelays.Length; i++) Defer(TreeProcKind.Dodge, a.position, 0f, TreeElement.None, 1, i, TreeSynergy.None, 0f, GhostDelays[i], Time.frameCount);
            return true;
        }

        /// <summary>
        /// B15: the thread victim → hero, 0.45 s. S-04 B5b (12.10): Point is the victim (PlayerHealth passes the kill point to
        /// OnKillHeal); the hero end is PlayerRegistry.Primary's chest (the diorama mannequin in the preview). A root point is lifted
        /// to the victim's chest. No point (default) or a victim at the hero's feet: no thread. AD-B4 M7: under it the same ribbon
        /// in jade (<see cref="ThreadUnder"/>) — the pale fibre alone did not read on light stone.
        /// </summary>
        private bool Leech(Vector3 point)
        {
            Transform a = PlayerAnchor();
            if (a == null) return false;
            if (point.sqrMagnitude < 1e-6f)
            {
                LeechUnresolved++;
                return false;
            }

            Vector3 from = BodyPoint(point, null);
            Vector3 to = a.position + Vector3.up * ChestHeight;
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < LeechMinLength)
            {
                LeechUnresolved++;
                return false;
            }
            FlipbookEffect fx = Spawn(TreeVfxRow.B15Thread, Fam.Thread, from + d * .5f, Quaternion.identity, FlipbookEffect.Facing.Camera, length, Color.white, 1f, .3f, LeechSeconds);
            if (fx == null) return true;
            VfxCatalogEntry row = _rows[(int)TreeVfxRow.B15Thread];
            float aspect = row.WorldSizeMetres > 0f && row.WorldHeightMetres > 0f ? row.WorldHeightMetres / row.WorldSizeMetres : .2f;
            fx.SetAlign(d, true);   // the fibres flow +U: victim → hero
            fx.SetStretch(1f, ThreadThickness / Mathf.Max(.01f, length * aspect));
            ThreadUnder(from + d * .5f, d, length, aspect);   // after the thread: never an underdraw without its thread
            LastSpawned = fx;   // the proc's readout stays the thread
            LastSpawnedRow = TreeVfxRow.B15Thread;
            return true;
        }

        /// <summary>
        /// AD-B4 M7: the thread's jade underdraw — the same ribbon (row, length, flow) once more, #4F8A63 at α 0.5 with its coverage
        /// lifted (<see cref="TreeVfxLadder.LeechUnderCoverage"/>: the tint's α is the shader's fade), ×
        /// <see cref="TreeVfxLadder.LeechUnderThickness"/> the thread's band, <see cref="UnderBehindMetres"/> farther from the camera so
        /// it is drawn first. A missing slot or pool instance leaves the thread alone (K9: the underdraw goes before its layer).
        /// </summary>
        private void ThreadUnder(Vector3 mid, Vector3 along, float length, float aspect)
        {
            Vector3 at = mid;
            if (FeedbackCameraRef.TryGetPosition(out Vector3 eye))
            {
                Vector3 away = mid - eye;
                if (away.sqrMagnitude > 1e-4f) at += away.normalized * UnderBehindMetres;
            }

            Color tint = TreeVfxLadder.LeechUnderTint;
            tint.a = TreeVfxLadder.LeechUnderCoverage;
            FlipbookEffect under = Spawn(TreeVfxRow.B15Thread, Fam.Under, at, Quaternion.identity, FlipbookEffect.Facing.Camera, length, tint,
                TreeVfxLadder.LeechUnderAlpha, .3f, LeechSeconds);
            if (under == null) return;
            under.SetAlign(along, true);
            under.SetStretch(1f, ThreadThickness * TreeVfxLadder.LeechUnderThickness / Mathf.Max(.01f, length * aspect));
            LastUnderlay = under;
        }

        private bool Undying()
        {
            Transform a = PlayerAnchor();
            if (a == null || !TryCeremony(CeremonyUndying, EnsoSeconds)) return false;
            Vector3 feet = Ground(a.position) + Vector3.up * FeetLift;
            Spawn(TreeVfxRow.B17Pulse, Fam.Ceremony, feet, Flat, FlipbookEffect.Facing.Fixed, WorldSize(TreeVfxRow.B17Pulse), Color.white, 1f, .3f, -1f);
            float burst = WorldSize(TreeVfxRow.B13LevelUp);
            bool flash = FlashAllowed(burst);
            Spawn(TreeVfxRow.B13LevelUp, Fam.Ceremony, feet + Vector3.up * .01f, Flat, FlipbookEffect.Facing.Fixed, burst, flash ? Color.white : Dim(), 1f, .3f, -1f);
            return true;
        }

        private void LevelUp()
        {
            Transform a = PlayerAnchor();
            if (a == null || !TryCeremony(CeremonyLevelUp, .6f)) return;
            float burst = WorldSize(TreeVfxRow.B13LevelUp);
            bool flash = FlashAllowed(burst);
            Spawn(TreeVfxRow.B13LevelUp, Fam.Ceremony, Ground(a.position) + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed, burst,
                flash ? Color.white : Dim(), 1f, .3f, -1f);
        }

        private void Heal()
        {
            Transform a = PlayerAnchor();
            if (a == null || !TryCeremony(CeremonyHeal, .8f)) return;
            Spawn(TreeVfxRow.B17Pulse, Fam.Ceremony, Ground(a.position) + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                WorldSize(TreeVfxRow.B17Pulse), Color.white, 1f, .3f, -1f);
        }

        // ------------------------------------------------------------------ Şarjör / Hız (T4, B12) · sinerji (X8) · B10

        private bool Endless(bool open, float seconds)
        {
            EndlessWindowActive = open;
            if (!open)
            {
                if (_endlessFx != null && _endlessFx.IsSpawned && _endlessFx.Row == _rows[(int)TreeVfxRow.T4Enso]) _endlessFx.Despawn();
                _endlessFx = null;
                return true;
            }

            Transform a = PlayerAnchor();
            if (a == null) return false;
            float hold = seconds > 0f ? seconds : TreeRuntime.Current.EndlessSeconds > 0f ? TreeRuntime.Current.EndlessSeconds : 4f;
            _endlessFx = Spawn(TreeVfxRow.T4Enso, Fam.Endless, Ground(a.position) + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                WorldSize(TreeVfxRow.T4Enso) * .8f, Color.white, .6f, .2f, hold);
            return _endlessFx != null;
        }

        /// <summary>
        /// B12 white-hot at the muzzle: HDR by the Speed tier ladder (AP-041 HDR_multiplier_by_tier), × FlashScale under Reduce
        /// Flashes; 50 ms. S-04 B5b: Point = the last shot's muzzle when S-02 passes it to TreeHooks.OnShotFired; without it (default)
        /// the hero's weapon muzzle as the flash uses it (S-06's WeaponAttachments.FlashOrigin: the E5 power ring's muzzle while one
        /// is mounted).
        /// </summary>
        private bool Overclock(Vector3 point, int tier)
        {
            Vector3 muzzle = point;
            if (point.sqrMagnitude < 1e-6f && !TryPlayerMuzzle(out muzzle)) return false;
            float gain = 1f + TreeVfxLadder.EmissiveFor(tier, GameFeelSettings.FlashScale);
            return Spawn(TreeVfxRow.B12WhiteHot, Fam.Misc, muzzle, Quaternion.identity, FlipbookEffect.Facing.Camera,
                WorldSize(TreeVfxRow.B12WhiteHot), Gain(gain), 1f, .5f, WhiteHotSeconds) != null;
        }

        /// <summary>AD §4.1 "çift mühür": the two-colour ensō at the player's feet, once per synergy per run, in the ceremony slot.</summary>
        private bool Synergy(TreeSynergy mask)
        {
            TreeSynergy s = TreeVfxLadder.FirstSynergy(mask);
            if (s == TreeSynergy.None) return false;
            if ((_synergyShown & s) != 0) return true;
            Transform a = PlayerAnchor();
            if (a == null) return false;
            bool still = UiMotion.ReducedMotion;
            if (!TryCeremony(CeremonySynergy, still ? EnsoStaticSeconds : EnsoSeconds))
            {
                // another synergy holds the slot: this one waits for it (each still shows once per run)
                return Defer(TreeProcKind.SynergyFirstTrigger, a.position, 0f, TreeElement.None, 1, 0, s, 0f, Mathf.Max(0f, _ceremonyUntil - VfxClock.Now) + .05f, Time.frameCount);
            }

            _synergyShown |= s;
            TreeVfxLadder.SynergyTints(s, out Color left, out Color right);
            FlipbookEffect fx = Spawn(TreeVfxRow.X8Enso, Fam.Ceremony, Ground(a.position) + Vector3.up * (FeetLift + .02f), Flat, FlipbookEffect.Facing.Fixed,
                WorldSize(TreeVfxRow.X8Enso), left, EnsoAlpha, EnsoFade, still ? EnsoStaticSeconds : EnsoSeconds);
            if (fx == null) return false;
            fx.SetTintRight(right);
            if (still) fx.HoldFrame(int.MaxValue);   // Reduced Motion: the drawn ensō, still, 0.6 s
            return true;
        }

        /// <summary>A Barrage cluster part (S-04: ClusterSplit, Synergy Barrage, three around the rocket's impact): B10 flame + B5 × 0.6,
        /// the third part within 3 m / 0.25 s folding like any Barrage blast (AD §4.2).</summary>
        private bool Cluster(Vector3 p, int tier)
        {
            float size = WorldSize(TreeVfxRow.B5Explosive) * TreeVfxLadder.SecondaryElementScale * TreeVfxLadder.ImpactScale(tier);
            if (FoldBarrage(p, size, out int r)) return true;
            Spawn(TreeVfxRow.B10MiniFlame, Fam.Misc, p, Quaternion.identity, FlipbookEffect.Facing.Camera, WorldSize(TreeVfxRow.B10MiniFlame), Color.white, 1f, .3f, -1f);
            FlipbookEffect fx = Spawn(TreeVfxRow.B5Explosive, Fam.Blast, p, Quaternion.identity, FlipbookEffect.Facing.Camera, size, Color.white, 1f, .3f, -1f);
            if (fx == null) Merged++;   // A9 B5 + B8 ≤ 8
            else if (r < 0) Remember(p, fx, RecentExplosive, size);
            return true;
        }

        /// <summary>B11 "kademe giriş halkası": the new aura ring grows once from 35 % (not under Reduce Flashes: no ring pulse).</summary>
        internal void AuraEntry(int level, Vector3 feet)
        {
            if (ReducedFlashes || level < 1) return;
            TreeVfxRow row = TreeVfxIds.AuraRing(level);
            float size = WorldSize(row);
            FlipbookEffect fx = Spawn(row, Fam.Misc, feet + Vector3.up * (FeetLift + .01f), Flat, FlipbookEffect.Facing.Fixed, size,
                TreeVfxLadder.AuraTint(level), TreeVfxLadder.AuraAlpha(level), .5f, TreeAuraVfx.EntryPulseSeconds);
            if (fx != null) fx.SetGrow(size * TreeAuraVfx.EntryGrowFrom, TreeAuraVfx.EntryPulseSeconds * .7f);
        }

        // ------------------------------------------------------------------ shared pieces

        private void SumiLayer(Vector3 ground, int tier, bool gravity, float baseSize)
        {
            TreeVfxRow row;
            switch (TreeVfxLadder.SumiFor(tier, gravity))
            {
                case TreeVfxLadder.Sumi.InkShock: row = TreeVfxRow.T3InkShock; break;
                case TreeVfxLadder.Sumi.Enso: row = TreeVfxRow.T4Enso; break;
                case TreeVfxLadder.Sumi.Spatter: row = TreeVfxRow.T5Spatter; break;
                default: return;
            }

            float ws = WorldSize(row);
            Spawn(row, Fam.Sumi, ground + Vector3.up * FeetLift, Flat, FlipbookEffect.Facing.Fixed,
                Mathf.Clamp(baseSize * 1.2f, ws * .5f, ws * 1.5f), Color.white, .8f, .4f, -1f);
        }

        private void Scorch(Vector3 p, float radius)
        {
            LayScorch(TreeVfxIds.ScorchRow(_scorchTurn++), p, Mathf.Max(.6f, radius) * 1.2f, ExplosionDirector.ScorchSeconds, ExplosionDirector.ScorchAlpha);
        }

        private bool LayScorch(TreeVfxRow row, Vector3 p, float size, float seconds, float alpha)
        {
            VfxCatalogEntry entry = _rows[(int)row];
            PrototypePool<DecalEffect> pool = _owner != null ? _owner.DecalPool : null;
            if (entry == null || pool == null)
            {
                Missing++;
                return false;
            }

            if (!Physics.Raycast(p + Vector3.up * .15f, Vector3.down, out RaycastHit hit, GroundProbe, s_groundMask, QueryTriggerInteraction.Ignore)) return false;
            // A9: tree scorches ≤ 12 — the oldest fades out first (only while it is still ours: the decal pool recycles the oldest
            // instance for any system, and a bullet hole on a recycled instance must not fade with it)
            DecalEffect old = _scorches[_scorchNext];
            if (old != null && old.IsSpawned && old.Row == _scorchRows[_scorchNext]) old.BeginFade(.3f);
            Vector3 n = hit.normal.sqrMagnitude > 1e-4f ? hit.normal.normalized : Vector3.up;
            Vector3 up = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > .99f ? Vector3.forward : Vector3.up;
            DecalEffect decal = pool.Get(hit.point + n * .02f, Quaternion.LookRotation(n, up));
            if (decal == null) return false;
            decal.Play(entry, size, seconds, alpha, Color.white, (_scorchTurn * 137.5f) % 360f);
            _scorches[_scorchNext] = decal;
            _scorchRows[_scorchNext] = entry;
            _scorchNext = (_scorchNext + 1) % ScorchCapacity;
            Played++;
            return true;
        }

        private FlipbookEffect Spawn(TreeVfxRow row, Fam fam, Vector3 at, Quaternion rotation, FlipbookEffect.Facing facing, float size, Color tint,
            float alphaCap, float fadeOut, float seconds)
        {
            VfxCatalogEntry entry = row < TreeVfxRow.Count ? _rows[(int)row] : null;
            if (entry == null || _pool == null)
            {
                Missing++;
                return null;
            }

            int slot = FreeSlot(fam);
            if (slot < 0)
            {
                Dropped++;
                return null;
            }

            FlipbookEffect fx = _pool.Get(at, rotation);
            if (fx == null)
            {
                Dropped++;
                return null;
            }

            fx.Play(entry, facing, size, tint, alphaCap, fadeOut, seconds);
            _slots[(int)fam][slot] = fx;
            _slotRows[(int)fam][slot] = entry;
            _spawnsThisFrame++;
            Played++;
            LastSpawned = fx;
            LastSpawnedRow = row;
            return fx;
        }

        private int FreeSlot(Fam fam)
        {
            FlipbookEffect[] fx = _slots[(int)fam];
            VfxCatalogEntry[] rows = _slotRows[(int)fam];
            for (int i = 0; i < fx.Length; i++)
            {
                if (fx[i] == null || !fx[i].IsSpawned || fx[i].Row != rows[i]) return i;
            }

            return -1;
        }

        private int Live(Fam fam)
        {
            FlipbookEffect[] fx = _slots[(int)fam];
            VfxCatalogEntry[] rows = _slotRows[(int)fam];
            int n = 0;
            for (int i = 0; i < fx.Length; i++)
            {
                if (fx[i] != null && fx[i].IsSpawned && fx[i].Row == rows[i]) n++;
            }

            return n;
        }

        private void DespawnFamily(Fam fam)
        {
            FlipbookEffect[] fx = _slots[(int)fam];
            VfxCatalogEntry[] rows = _slotRows[(int)fam];
            for (int i = 0; i < fx.Length; i++)
            {
                if (fx[i] != null && fx[i].IsSpawned && fx[i].Row == rows[i]) fx[i].Despawn();
                fx[i] = null;
            }
        }

        private bool Alive(FlipbookEffect fx, TreeVfxRow row) => fx != null && fx.IsSpawned && fx.Row == _rows[(int)row];

        private int FindRecent(Vector3 p, float radius, float seconds, int kind)
        {
            float now = VfxClock.Now;
            float r2 = radius * radius;
            for (int i = 0; i < _recent.Length; i++)
            {
                ref Recent rec = ref _recent[i];
                if (!rec.Used || (kind != RecentAny && rec.Kind != kind)) continue;
                if (now - rec.At > seconds) continue;
                if ((rec.Point - p).sqrMagnitude <= r2) return i;
            }

            return -1;
        }

        private void Remember(Vector3 p, FlipbookEffect fx, int kind, float size)
        {
            _recent[_recentNext] = new Recent { Used = true, Kind = kind, Count = 1, At = VfxClock.Now, Size = size, Point = p, Fx = fx };
            _recentNext = (_recentNext + 1) % _recent.Length;
        }

        /// <summary>K1: at most two element visuals per body per frame — the body by its Target, else by its point (S-04's status
        /// procs put a body's moments at the body's position, so one body = one point within a frame).</summary>
        private bool TakeHitVisual(Component target, Vector3 p)
        {
            int id = target != null ? target.GetInstanceID() : PointKey(p);
            int frame = Time.frameCount;
            for (int i = 0; i < _hitIds.Length; i++)
            {
                if (_hitIds[i] != id || _hitFrames[i] != frame) continue;
                if (_hitCounts[i] >= TreeVfxLadder.ElementVisualsPerHit) return false;
                _hitCounts[i]++;
                return true;
            }

            _hitIds[_hitNext] = id;
            _hitFrames[_hitNext] = frame;
            _hitCounts[_hitNext] = 1;
            _hitNext = (_hitNext + 1) % _hitIds.Length;
            return true;
        }

        private static int PointKey(Vector3 p) =>
            unchecked(Mathf.FloorToInt(p.x * 4f) * 73856093 ^ Mathf.FloorToInt(p.z * 4f) * 19349663 ^ Mathf.FloorToInt(p.y * 4f) * 83492791);

        /// <summary>
        /// Where a body-status moment's hit sheet goes: the Target's chest when the body is known; else Point, lifted to the chest when
        /// it is the body's root on the ground (S-04's ignite / freeze points are the body's position).
        /// </summary>
        private static Vector3 BodyPoint(Vector3 p, Component target)
        {
            if (target != null) return target.transform.position + Vector3.up * ChestHeight;
            Vector3 g = Ground(p);
            return p.y - g.y <= BodyRootBand ? g + Vector3.up * ChestHeight : p;
        }

        /// <summary>The hero's weapon muzzle as the flash uses it (S-02's WeaponAttachments.FlashOrigin); false in the diorama.</summary>
        private bool TryPlayerMuzzle(out Vector3 at)
        {
            at = default;
            if (TreeVfxPreview.Active) return false;
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null) return false;
            if (_weaponsRig != rig || _weapons == null)
            {
                _weaponsRig = rig;
                _weapons = rig.GetComponentInChildren<DD.Prototype.Weapons.WeaponController>(true);
            }

            Transform muzzle = _weapons != null ? DD.Prototype.Weapons.Attachments.WeaponAttachments.FlashOrigin(_weapons.Muzzle) : null;
            if (muzzle == null) return false;
            at = muzzle.position;
            return true;
        }

        private static bool IsSecondary(TreeElement element)
        {
            TreeElement dominant = TracerStyle.DominantElement;
            return dominant != TreeElement.None && dominant != element;
        }

        /// <summary>Reduce Flashes: ≤ 3 electric hits per second (unscaled; FlashGate's window).</summary>
        private bool ShockGate()
        {
            float now = Time.unscaledTime;
            float oldest = _shockHits[_shockNext];
            if (oldest > 0f && now - oldest < 1f) return false;
            _shockHits[_shockNext] = Mathf.Max(now, 1e-4f);
            _shockNext = (_shockNext + 1) % _shockHits.Length;
            return true;
        }

        private bool TryCeremony(int priority, float seconds)
        {
            float now = VfxClock.Now;
            if (now < _ceremonyUntil && priority <= _ceremonyPriority)
            {
                CeremoniesDropped++;   // one ceremony per screen: a lower or equal one goes to the HUD stamp only
                return false;
            }

            _ceremonyUntil = now + seconds;
            _ceremonyPriority = priority;
            CeremoniesPlayed++;
            return true;
        }

        /// <summary>A flash may start: not under Reduce Flashes, and a large one (≥ 43 000 px²) only through the shared FlashGate.</summary>
        private static bool FlashAllowed(float worldSize)
        {
            if (ReducedFlashes) return false;
            return UiMotion.FlashOnset(TreeVfxLadder.TdAreaPixels(worldSize) >= TreeVfxLadder.LargeFlashPixels);
        }

        private static bool ReducedFlashes => GameFeelSettings.FlashScale < .99f;

        /// <summary>A white tint whose LINEAR value is <paramref name="linearGain"/> after MaterialPropertyBlock.SetColor's sRGB decode.</summary>
        private static Color Gain(float linearGain)
        {
            float g = Mathf.LinearToGammaSpace(Mathf.Max(0f, linearGain));
            return new Color(g, g, g, 1f);
        }

        /// <summary>RF / gated flash: the sheet's shape without its flash (dimmed fill — a contour variant is an AP-041 request).</summary>
        private static Color Dim() => Gain(DimFlashGain);

        private float WorldSize(TreeVfxRow row)
        {
            VfxCatalogEntry e = row < TreeVfxRow.Count ? _rows[(int)row] : null;
            return e != null ? Mathf.Max(.05f, e.WorldSizeMetres) : 1f;
        }

        /// <summary>
        /// An area event shows its gameplay area: diameter 2 r × share (kept within ½–3 × the sheet's authored size) — the AD §2 event
        /// scale would draw a tier-I blast smaller and a tier-V one larger than the damage it does (open AD item). Without a radius the
        /// sheet's authored size × the tier ladder (×0.6 … ×1.25).
        /// </summary>
        private float RadiusSize(TreeVfxRow row, float radius, int tier, float share)
        {
            float ws = WorldSize(row);
            if (radius <= 0f) return ws * TreeVfxLadder.ImpactScale(tier);
            return Mathf.Clamp(2f * radius * share, ws * .5f, ws * 3f);
        }

        private static Vector3 Ground(Vector3 p)
        {
            return Physics.Raycast(p + Vector3.up * .15f, Vector3.down, out RaycastHit hit, GroundProbe, s_groundMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : p;
        }

        /// <summary>The player (or the diorama mannequin while the preview runs).</summary>
        internal static Transform PlayerAnchor()
        {
            if (TreeVfxPreview.Active) return TreeVfxPreview.Anchor;
            PlayerRig rig = PlayerRegistry.Primary;
            return rig != null ? rig.transform : null;
        }

        // ------------------------------------------------------------------ frame

        private void BeginFrame()
        {
            int f = Time.frameCount;
            if (f == _frame) return;
            _frame = f;
            _spawnsThisFrame = 0;
        }

        private bool Defer(TreeProcKind kind, Vector3 p, float radius, TreeElement element, int tier, int depth, TreeSynergy synergy, float seconds,
            float delay, int frame)
        {
            for (int i = 0; i < _pending.Length; i++)
            {
                if (_pending[i].Active) continue;
                _pending[i] = new Pending
                {
                    Active = true, Kind = kind, Point = p, Radius = radius, Element = element, Tier = tier, Depth = depth, Synergy = synergy,
                    Seconds = seconds, Frame = frame, At = VfxClock.Now + delay
                };
                Deferred++;
                return true;
            }

            Dropped++;
            return false;
        }

        private void Update()
        {
            using (TreeVfxMarkers.Binder.Auto())
            {
                BeginFrame();
                float now = VfxClock.Now;
                for (int i = 0; i < _pending.Length && _spawnsThisFrame < MaxSpawnsPerFrame; i++)
                {
                    ref Pending p = ref _pending[i];
                    if (!p.Active || p.At > now) continue;
                    p.Active = false;
                    Dispatch(p.Kind, p.Point, p.Radius, p.Element, p.Tier, p.Depth, p.Synergy, p.Seconds, null, p.Frame);
                }

                // Endless Window's ensō rides the player's feet
                if (_endlessFx != null)
                {
                    Transform a = PlayerAnchor();
                    if (!_endlessFx.IsSpawned || _endlessFx.Row != _rows[(int)TreeVfxRow.T4Enso]) _endlessFx = null;
                    else if (a != null) _endlessFx.transform.position = a.position + Vector3.up * FeetLift;
                }
            }
        }

        /// <summary>Scene change / preview end / tests: every tree effect back to the pool, queues emptied.</summary>
        public void Clear()
        {
            for (int f = 0; f < (int)Fam.Count; f++) DespawnFamily((Fam)f);
            for (int i = 0; i < _pending.Length; i++) _pending[i].Active = false;
            for (int i = 0; i < _recent.Length; i++) _recent[i] = default;
            for (int i = 0; i < _scorches.Length; i++)
            {
                _scorches[i] = null;
                _scorchRows[i] = null;
            }
            _endlessFx = null;
            _ceremonyUntil = -1f;
            _ceremonyPriority = -1;
            EndlessWindowActive = false;
        }

        /// <summary>Tests: counters back to zero and the run's once-per-synergy memory forgotten.</summary>
        public void ResetCountersForTests()
        {
            ProcsSeen = Played = Merged = Replaced = Dropped = Deferred = Missing = CeremoniesPlayed = CeremoniesDropped = 0;
            FireZonesFed = LeechUnresolved = 0;
            Array.Clear(_byKind, 0, _byKind.Length);
            _synergyShown = TreeSynergy.None;
            LastSpawned = null;
            LastSpawnedRow = TreeVfxRow.Count;
            LastUnderlay = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            EndlessWindowActive = false;
        }
    }
}
