using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    // Runtime values are deliberately separate from the mutable serialization DTOs in
    // FeelConfig.cs. Every section is copied once at commit and is then safe to retain on
    // hot paths without cloning or exposing an authoring object.
    public readonly struct AimAssistFeel
    {
        public readonly int defaultLevel; public readonly float maxAngleDegrees, maxDistanceMeters, lightAngleShare;
        internal AimAssistFeel(AimAssistFeelData d) { defaultLevel = d.defaultLevel; maxAngleDegrees = d.maxAngleDegrees; maxDistanceMeters = d.maxDistanceMeters; lightAngleShare = d.lightAngleShare; }
    }

    public readonly struct AimFeel
    {
        public readonly float stickPointDistanceMeters, pointerMinDistanceMeters;
        internal AimFeel(AimFeelData d) { stickPointDistanceMeters = d.stickPointDistanceMeters; pointerMinDistanceMeters = d.pointerMinDistanceMeters; }
    }

    public readonly struct CameraFeel
    {
        public readonly float heightMeters, backDistanceMeters, fieldOfViewDegrees, yawDegrees, focusHeightOffsetMeters;
        public readonly float followSmoothSeconds, maxLookAheadMeters, lookAheadSmoothSeconds, pointerLookAheadFactor;
        public readonly float occlusionFadeSeconds, occlusionTargetAlpha;
        internal CameraFeel(CameraFeelData d)
        {
            heightMeters = d.heightMeters; backDistanceMeters = d.backDistanceMeters; fieldOfViewDegrees = d.fieldOfViewDegrees;
            yawDegrees = d.yawDegrees; focusHeightOffsetMeters = d.focusHeightOffsetMeters; followSmoothSeconds = d.followSmoothSeconds;
            maxLookAheadMeters = d.maxLookAheadMeters; lookAheadSmoothSeconds = d.lookAheadSmoothSeconds;
            pointerLookAheadFactor = d.pointerLookAheadFactor; occlusionFadeSeconds = d.occlusionFadeSeconds;
            occlusionTargetAlpha = d.occlusionTargetAlpha;
        }
    }

    public readonly struct ShakeKernelFeel
    {
        public readonly float rattleFrequency, rumbleFrequency, rumbleZShare, rumbleDirectionShare, rumbleRollDegreesPerMeter;
        public readonly float punchDecayPower, punchCosinePiFactor, punchRollDegreesPerMeter;
        public readonly float bumpDecayPower, bumpCosinePiFactor, bumpZShare;
        internal ShakeKernelFeel(ShakeKernelFeelData d)
        {
            rattleFrequency = d.rattleFrequency; rumbleFrequency = d.rumbleFrequency; rumbleZShare = d.rumbleZShare;
            rumbleDirectionShare = d.rumbleDirectionShare; rumbleRollDegreesPerMeter = d.rumbleRollDegreesPerMeter;
            punchDecayPower = d.punchDecayPower; punchCosinePiFactor = d.punchCosinePiFactor;
            punchRollDegreesPerMeter = d.punchRollDegreesPerMeter; bumpDecayPower = d.bumpDecayPower;
            bumpCosinePiFactor = d.bumpCosinePiFactor; bumpZShare = d.bumpZShare;
        }
    }

    public readonly struct ShakeFeel
    {
        public readonly float designGain, nominalAggregateCapMeters, rollDegreesPerMeter;
        public readonly int maxConcurrent; public readonly ShakeKernelFeel kernels;
        internal ShakeFeel(ShakeFeelData d) { designGain = d.designGain; nominalAggregateCapMeters = d.nominalAggregateCapMeters; maxConcurrent = d.maxConcurrent; rollDegreesPerMeter = d.rollDegreesPerMeter; kernels = new ShakeKernelFeel(d.kernels); }
    }

    public readonly struct FireShakeFeel
    {
        public readonly float metersAtWeightOne, baseSeconds, secondsPerWeight;
        internal FireShakeFeel(FireShakeFeelData d) { metersAtWeightOne = d.metersAtWeightOne; baseSeconds = d.baseSeconds; secondsPerWeight = d.secondsPerWeight; }
    }

    public readonly struct RecoilFeel
    {
        public readonly float damping, pitchDegreesPerMeter, springCoefficient, animatorFullStrengthKickMeters;
        public readonly float heldKickAccumulation, heldKickMaxMeters, heldKickMaxDegrees, heldKickSettleRecoveries;
        internal RecoilFeel(RecoilFeelData d)
        {
            damping = d.damping; pitchDegreesPerMeter = d.pitchDegreesPerMeter; springCoefficient = d.springCoefficient; animatorFullStrengthKickMeters = d.animatorFullStrengthKickMeters;
            heldKickAccumulation = d.heldKickAccumulation; heldKickMaxMeters = d.heldKickMaxMeters; heldKickMaxDegrees = d.heldKickMaxDegrees; heldKickSettleRecoveries = d.heldKickSettleRecoveries;
        }
    }

    public readonly struct PlayerDamageFeel
    {
        public readonly float fullReactionDamageFraction, minimumStrength, flashDecaySeconds;
        internal PlayerDamageFeel(PlayerDamageFeelData d) { fullReactionDamageFraction = d.fullReactionDamageFraction; minimumStrength = d.minimumStrength; flashDecaySeconds = d.flashDecaySeconds; }
    }

    public readonly struct WeaponFeel
    {
        public readonly WeaponFeelId id; public readonly float recoilKickMeters, recoilRecoverySeconds, recoilUpShare, shakeWeight;
        public readonly FeelShakeKind shakeKind; public readonly float rumbleFraction; public readonly int rumbleDurationMs;
        /// <summary>WP-069 M3: the view-model rig's kick (the row's rig column, else its recoil column).</summary>
        public readonly float rigKickMeters, rigRecoverySeconds, rigUpShare;
        internal WeaponFeel(WeaponFeelRow d)
        {
            id = d.id; recoilKickMeters = d.recoilKickMeters; recoilRecoverySeconds = d.recoilRecoverySeconds; recoilUpShare = d.recoilUpShare; shakeWeight = d.shakeWeight; shakeKind = d.shakeKind; rumbleFraction = d.rumbleFraction; rumbleDurationMs = d.rumbleDurationMs;
            rigKickMeters = d.rigKickMeters > 0f ? d.rigKickMeters : d.recoilKickMeters;
            rigRecoverySeconds = d.rigRecoverySeconds > 0f ? d.rigRecoverySeconds : d.recoilRecoverySeconds;
            rigUpShare = d.rigUpShare > 0f ? d.rigUpShare : d.recoilUpShare;
        }
    }

    public readonly struct ImpactFeel
    {
        public readonly ImpactFeelId id; public readonly int cameraPriority; public readonly float shakeAmplitudeMeters, shakeSeconds;
        public readonly FeelShakeKind shakeKind; public readonly float hitStopSeconds, rumbleFraction; public readonly int rumbleDurationMs;
        internal ImpactFeel(ImpactFeelRow d) { id = d.id; cameraPriority = d.cameraPriority; shakeAmplitudeMeters = d.shakeAmplitudeMeters; shakeSeconds = d.shakeSeconds; shakeKind = d.shakeKind; hitStopSeconds = d.hitStopSeconds; rumbleFraction = d.rumbleFraction; rumbleDurationMs = d.rumbleDurationMs; }
    }

    public readonly struct HitStopFeel
    {
        public readonly bool enabled; public readonly float minIntervalSeconds, presentationScale, maxContinuousSeconds;
        internal HitStopFeel(HitStopFeelData d) { enabled = d.enabled; minIntervalSeconds = d.minIntervalSeconds; presentationScale = d.presentationScale; maxContinuousSeconds = d.maxContinuousSeconds; }
    }

    public readonly struct VignetteFeel
    {
        public readonly bool enabled; public readonly float flashAlpha, lowHealthAlpha, lowHealthFraction; public readonly int flashInFrames, flashOutFrames;
        internal VignetteFeel(VignetteFeelData d) { enabled = d.enabled; flashAlpha = d.flashAlpha; lowHealthAlpha = d.lowHealthAlpha; lowHealthFraction = d.lowHealthFraction; flashInFrames = d.flashInFrames; flashOutFrames = d.flashOutFrames; }
    }

    public readonly struct DeathFeel
    {
        public readonly bool enabled; public readonly float slowScale, slowInSeconds, holdSeconds, outSeconds, cameraPushInFraction, resultsDelaySeconds;
        internal DeathFeel(DeathFeelData d) { enabled = d.enabled; slowScale = d.slowScale; slowInSeconds = d.slowInSeconds; holdSeconds = d.holdSeconds; outSeconds = d.outSeconds; cameraPushInFraction = d.cameraPushInFraction; resultsDelaySeconds = d.resultsDelaySeconds; }
    }

    public readonly struct FeelColorValue
    {
        public readonly float r, g, b, a;
        internal FeelColorValue(FeelColor d) { r = d.r; g = d.g; b = d.b; a = d.a; }
        public Color UnityColor => new Color(r, g, b, a);
    }

    public readonly struct DamageNumberFeel
    {
        public readonly bool enabled; public readonly int maxConcurrent; public readonly float mergeWindowSeconds, lifetimeSeconds;
        public readonly float anchorOffsetYMeters, mergeAnchorBlend, driftExtentXMeters, driftRiseMeters, driftExtentZMeters;
        public readonly float mergePopScale, mergePopSeconds, endScale, fadeStartNormalized; public readonly int fontSize;
        public readonly float characterSize; public readonly FeelColorValue normalColor, armorDefaultColor;
        public readonly string criticalToken, armorAccessibleToken;
        internal DamageNumberFeel(DamageNumberFeelData d)
        {
            enabled = d.enabled; maxConcurrent = d.maxConcurrent; mergeWindowSeconds = d.mergeWindowSeconds; lifetimeSeconds = d.lifetimeSeconds;
            anchorOffsetYMeters = d.anchorOffsetYMeters; mergeAnchorBlend = d.mergeAnchorBlend; driftExtentXMeters = d.driftExtentXMeters;
            driftRiseMeters = d.driftRiseMeters; driftExtentZMeters = d.driftExtentZMeters; mergePopScale = d.mergePopScale;
            mergePopSeconds = d.mergePopSeconds; endScale = d.endScale; fadeStartNormalized = d.fadeStartNormalized;
            fontSize = d.fontSize; characterSize = d.characterSize; normalColor = new FeelColorValue(d.normalColor);
            armorDefaultColor = new FeelColorValue(d.armorDefaultColor); criticalToken = d.criticalToken; armorAccessibleToken = d.armorAccessibleToken;
        }
    }

    public readonly struct RumbleFeel
    {
        public readonly float highMotorRatio, classicFallbackFraction, specialFallbackFraction;
        public readonly int classicFallbackDurationMs, specialFallbackDurationMs;
        internal RumbleFeel(RumbleFeelData d) { highMotorRatio = d.highMotorRatio; classicFallbackFraction = d.classicFallbackFraction; classicFallbackDurationMs = d.classicFallbackDurationMs; specialFallbackFraction = d.specialFallbackFraction; specialFallbackDurationMs = d.specialFallbackDurationMs; }
    }

    public readonly struct BudgetCategoryFeel
    {
        public readonly string id; public readonly int capacity;
        internal BudgetCategoryFeel(BudgetCategoryFeelRow d) { id = d.id; capacity = d.capacity; }
    }

    public readonly struct BudgetResourceFeel
    {
        public readonly int flipbook, decal, fragment, smoke, casing, straw, spark, leaf, paper, bloodDecal, bloodDroplet;
        internal BudgetResourceFeel(BudgetResourceFeelData d) { flipbook = d.flipbook; decal = d.decal; fragment = d.fragment; smoke = d.smoke; casing = d.casing; straw = d.straw; spark = d.spark; leaf = d.leaf; paper = d.paper; bloodDecal = d.bloodDecal; bloodDroplet = d.bloodDroplet; }
    }

    public readonly struct BudgetFeel
    {
        private readonly BudgetCategoryFeel[] _categories;
        public readonly float perFrameFraction, mergeDistanceMeters, mergeWindowSeconds;
        public readonly int minStartsPerFrame, importantStartMultiplier; public readonly BudgetResourceFeel resources;
        internal BudgetFeel(BudgetFeelData d)
        {
            _categories = new BudgetCategoryFeel[d.categories.Length];
            for (int i = 0; i < d.categories.Length; i++) _categories[i] = new BudgetCategoryFeel(d.categories[i]);
            perFrameFraction = d.perFrameFraction; minStartsPerFrame = d.minStartsPerFrame;
            importantStartMultiplier = d.importantStartMultiplier; mergeDistanceMeters = d.mergeDistanceMeters;
            mergeWindowSeconds = d.mergeWindowSeconds; resources = new BudgetResourceFeel(d.resources);
        }
        public int CategoryCount => _categories.Length;
        public BudgetCategoryFeel CategoryAt(int index) => _categories[index];
        public int CapacityFor(string id, int fallback)
        {
            for (int i = 0; i < _categories.Length; i++)
                if (string.Equals(_categories[i].id, id, StringComparison.Ordinal)) return _categories[i].capacity;
            return fallback;
        }
    }

    public readonly struct TelemetryFeel
    {
        public readonly bool enabledByDefault, captureEventTrace; public readonly float uiRefreshHz;
        public readonly int roundCapacity, ttkCapacity, lifeCapacity, eventTraceCapacity;
        internal TelemetryFeel(TelemetryFeelData d) { enabledByDefault = d.enabledByDefault; uiRefreshHz = d.uiRefreshHz; roundCapacity = d.roundCapacity; ttkCapacity = d.ttkCapacity; lifeCapacity = d.lifeCapacity; eventTraceCapacity = d.eventTraceCapacity; captureEventTrace = d.captureEventTrace; }
    }

    /// <summary>A detached, validated runtime view. Consumers never read the authoring asset.</summary>
    public sealed class FeelSnapshot
    {
        private readonly FeelConfigData _data;
        private readonly WeaponFeel[] _weapons = new WeaponFeel[6];
        private readonly ImpactFeel[] _impacts = new ImpactFeel[11];

        internal FeelSnapshot(FeelConfigData data, ulong revision, string contentHash, bool pendingNextRun)
        {
            _data = FeelProfileCodec.Clone(data);
            Revision = revision;
            ContentHash = contentHash;
            PendingNextRun = pendingNextRun;
            Enabled = _data.enabled;
            AimAssist = new AimAssistFeel(_data.aimAssist); Aim = new AimFeel(_data.aim); Camera = new CameraFeel(_data.camera);
            Shake = new ShakeFeel(_data.shake); FireShake = new FireShakeFeel(_data.fireShake); Recoil = new RecoilFeel(_data.recoil);
            PlayerDamage = new PlayerDamageFeel(_data.playerDamage); HitStop = new HitStopFeel(_data.hitStop);
            Vignette = new VignetteFeel(_data.vignette); Death = new DeathFeel(_data.death);
            DamageNumbers = new DamageNumberFeel(_data.damageNumbers); Rumble = new RumbleFeel(_data.rumble);
            Budgets = new BudgetFeel(_data.budgets); Telemetry = new TelemetryFeel(_data.telemetry);
            for (int i = 0; i < _data.weapons.Length; i++) _weapons[(int)_data.weapons[i].id] = new WeaponFeel(_data.weapons[i]);
            for (int i = 0; i < _data.impacts.Length; i++) _impacts[(int)_data.impacts[i].id] = new ImpactFeel(_data.impacts[i]);
        }

        public bool Enabled { get; }
        public ulong Revision { get; }
        public string ContentHash { get; }
        public bool PendingNextRun { get; }
        public AimAssistFeel AimAssist { get; }
        public AimFeel Aim { get; }
        public CameraFeel Camera { get; }
        public ShakeFeel Shake { get; }
        public FireShakeFeel FireShake { get; }
        public RecoilFeel Recoil { get; }
        public PlayerDamageFeel PlayerDamage { get; }
        public HitStopFeel HitStop { get; }
        public VignetteFeel Vignette { get; }
        public DeathFeel Death { get; }
        public DamageNumberFeel DamageNumbers { get; }
        public RumbleFeel Rumble { get; }
        public BudgetFeel Budgets { get; }
        public TelemetryFeel Telemetry { get; }

        public WeaponFeel Weapon(WeaponFeelId id) => _weapons[(int)id];
        public ImpactFeel Impact(ImpactFeelId id) => _impacts[(int)id];
        internal FeelConfigData CloneData() => FeelProfileCodec.Clone(_data);
    }

    /// <summary>
    /// Owns the one active feel snapshot. JSON/hash/reflection work is confined to staging;
    /// combat reads a stable reference and fixed enum-indexed arrays.
    /// </summary>
    public static class FeelRuntime
    {
        private static FeelSnapshot s_current;
        private static FeelConfigData s_pendingLive;
        private static FeelConfigData s_pendingNextRun;
        private static string s_pendingLiveHash;
        private static string s_pendingNextRunHash;
        private static int s_lastCommitFrame = int.MinValue;
        private static ulong s_revision;

        public static event Action<FeelSnapshot> Changed;

        public static FeelSnapshot Current
        {
            get
            {
                if (s_current == null) Initialize(null);
                return s_current;
            }
        }

        public static bool HasPendingApply => s_pendingLive != null;
        public static bool HasPendingNextRun => s_pendingNextRun != null;

        public static void Initialize(FeelConfig config)
        {
            if (s_current != null) return;
            if (config == null)
            {
                PrototypeConfig prototype = PrototypeConfig.Active;
                config = prototype != null ? prototype.Feel : null;
            }
            if (config == null) config = Resources.Load<FeelConfig>(FeelConfig.ResourcesPath);
            FeelConfigData data = config != null && config.AuthoredData != null
                ? config.AuthoredData : FeelConfig.CreateDefaults();
            if (!FeelConfigValidator.Validate(data, out string error))
            {
                Debug.LogError("[Feel] Packaged profile invalid: " + error);
                data = FeelConfig.CreateDefaults();
            }

            s_revision = 1;
            s_current = new FeelSnapshot(data, s_revision, FeelProfileCodec.Hash(data), false);
        }

        public static bool QueueAuthored(FeelConfigData data, string profileId, string baseCommit, out string error)
        {
            if (!FeelConfigValidator.Validate(data, out error)) return false;
            FeelConfigData detached = FeelProfileCodec.Clone(data);
            Stage(detached);
            return true;
        }

        public static bool QueueJson(string json, out string error)
        {
            if (!FeelProfileCodec.TryImport(json, out FeelProfileDocument document, out error)) return false;
            Stage(document.data);
            return true;
        }

        private static void Stage(FeelConfigData incoming)
        {
            FeelConfigData active = Current.CloneData();
            bool nChanged = NFieldsChanged(active, incoming);
            s_pendingLive = MergeLive(active, incoming);
            s_pendingLiveHash = FeelProfileCodec.Hash(s_pendingLive);
            s_pendingNextRun = nChanged ? FeelProfileCodec.Clone(incoming) : null;
            s_pendingNextRunHash = s_pendingNextRun != null ? FeelProfileCodec.Hash(s_pendingNextRun) : null;
        }

        /// <summary>Call at the start of Update, before input and weapons. At most one swap per frame.</summary>
        public static bool BeginFrame(int frameId)
        {
            if (s_pendingLive == null || s_lastCommitFrame == frameId) return false;
            s_lastCommitFrame = frameId;
            FeelConfigData committed = s_pendingLive;
            s_pendingLive = null;
            unchecked { s_revision++; }
            s_current = new FeelSnapshot(committed, s_revision, s_pendingLiveHash, s_pendingNextRun != null);
            s_pendingLiveHash = null;
            Changed?.Invoke(s_current);
            return true;
        }

        /// <summary>Run boundary transaction for pool/setup fields.</summary>
        public static bool ApplyPendingNextRun()
        {
            if (s_pendingNextRun == null) return false;
            FeelConfigData committed = s_pendingNextRun;
            s_pendingNextRun = null;
            // A run boundary may arrive before the next Update (for example a scene load
            // requested in the same frame as an import). The full N snapshot supersedes
            // its live-only merge; leaving that merge queued would revert N next frame.
            s_pendingLive = null;
            s_pendingLiveHash = null;
            unchecked { s_revision++; }
            s_current = new FeelSnapshot(committed, s_revision, s_pendingNextRunHash, false);
            s_pendingNextRunHash = null;
            Changed?.Invoke(s_current);
            return true;
        }

        /// <summary>
        /// WP-062H · the runtime master switch (A/B): stages the active profile with <c>enabled</c> flipped.
        /// A live field, so it commits on the next <see cref="BeginFrame"/>; the authored asset is never written.
        /// </summary>
        public static void SetEnabled(bool on)
        {
            // Base = what will be current after the pending commits (a queued next-run profile keeps its N fields).
            FeelConfigData data = s_pendingNextRun ?? s_pendingLive;
            if (data == null && Current.Enabled == on) return;
            data = data != null ? FeelProfileCodec.Clone(data) : Current.CloneData();
            data.enabled = on;
            Stage(data);
        }

        /// <summary>The master switch as it will read after the next commit (a pending A/B flip included).</summary>
        public static bool EnabledAfterCommit => s_pendingLive != null ? s_pendingLive.enabled : Current.Enabled;

        public static string ExportJson(string profileId, string baseCommit)
        {
            return FeelProfileCodec.Export(Current.CloneData(), profileId, baseCommit);
        }

        private static FeelConfigData MergeLive(FeelConfigData active, FeelConfigData incoming)
        {
            FeelConfigData live = FeelProfileCodec.Clone(incoming);
            live.camera.heightMeters = active.camera.heightMeters;
            live.camera.backDistanceMeters = active.camera.backDistanceMeters;
            live.camera.fieldOfViewDegrees = active.camera.fieldOfViewDegrees;
            live.camera.yawDegrees = active.camera.yawDegrees;
            live.camera.focusHeightOffsetMeters = active.camera.focusHeightOffsetMeters;
            live.shake.maxConcurrent = active.shake.maxConcurrent;
            live.damageNumbers.maxConcurrent = active.damageNumbers.maxConcurrent;
            live.damageNumbers.fontSize = active.damageNumbers.fontSize;
            live.damageNumbers.characterSize = active.damageNumbers.characterSize;
            for (int i = 0; i < live.budgets.categories.Length; i++)
                live.budgets.categories[i].capacity = CapacityFor(active.budgets.categories,
                    live.budgets.categories[i].id, live.budgets.categories[i].capacity);
            live.budgets.resources = FeelProfileCodec.Clone(active.budgets.resources);
            live.telemetry.enabledByDefault = active.telemetry.enabledByDefault;
            live.telemetry.roundCapacity = active.telemetry.roundCapacity;
            live.telemetry.ttkCapacity = active.telemetry.ttkCapacity;
            live.telemetry.lifeCapacity = active.telemetry.lifeCapacity;
            live.telemetry.eventTraceCapacity = active.telemetry.eventTraceCapacity;
            return live;
        }

        private static bool NFieldsChanged(FeelConfigData a, FeelConfigData b)
        {
            if (a.camera.heightMeters != b.camera.heightMeters || a.camera.backDistanceMeters != b.camera.backDistanceMeters ||
                a.camera.fieldOfViewDegrees != b.camera.fieldOfViewDegrees || a.camera.yawDegrees != b.camera.yawDegrees ||
                a.camera.focusHeightOffsetMeters != b.camera.focusHeightOffsetMeters ||
                a.shake.maxConcurrent != b.shake.maxConcurrent || a.damageNumbers.maxConcurrent != b.damageNumbers.maxConcurrent ||
                a.damageNumbers.fontSize != b.damageNumbers.fontSize || a.damageNumbers.characterSize != b.damageNumbers.characterSize ||
                a.telemetry.enabledByDefault != b.telemetry.enabledByDefault || a.telemetry.roundCapacity != b.telemetry.roundCapacity ||
                a.telemetry.ttkCapacity != b.telemetry.ttkCapacity || a.telemetry.lifeCapacity != b.telemetry.lifeCapacity ||
                a.telemetry.eventTraceCapacity != b.telemetry.eventTraceCapacity) return true;
            if (a.budgets.categories.Length != b.budgets.categories.Length) return true;
            for (int i = 0; i < a.budgets.categories.Length; i++)
                if (a.budgets.categories[i].capacity != CapacityFor(b.budgets.categories,
                    a.budgets.categories[i].id, int.MinValue)) return true;
            return FeelProfileCodec.Canonical(a.budgets.resources) != FeelProfileCodec.Canonical(b.budgets.resources);
        }

        private static int CapacityFor(BudgetCategoryFeelRow[] rows, string id, int fallback)
        {
            for (int i = 0; i < rows.Length; i++)
                if (string.Equals(rows[i].id, id, StringComparison.Ordinal)) return rows[i].capacity;
            return fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null; s_pendingLive = null; s_pendingNextRun = null;
            s_pendingLiveHash = null; s_pendingNextRunHash = null;
            s_lastCommitFrame = int.MinValue; s_revision = 0; Changed = null;
        }

#if UNITY_EDITOR
        public static void ResetForTests() => ResetStatics();
#endif
    }

    [DefaultExecutionOrder(-32000)]
    internal sealed class FeelRuntimeDriver : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("Feel Runtime");
            DontDestroyOnLoad(go);
            go.AddComponent<FeelRuntimeDriver>();
        }

        private void Awake() => FeelRuntime.Initialize(null);
        private void Update() => FeelRuntime.BeginFrame(Time.frameCount);
    }

    public sealed class FeelProfileDocument
    {
        public int schemaVersion;
        public string profileId;
        public string baseCommit;
        public string contentHash;
        public FeelConfigData data;
    }

    public static class FeelConfigValidator
    {
        public static bool Validate(FeelConfigData d, out string error)
        {
            error = null;
            if (d == null) return Fail("data is required", out error);
            if (!FiniteTree(d)) return Fail("all numbers must be finite", out error);
            if (d.aimAssist == null || d.aim == null || d.camera == null || d.shake == null || d.shake.kernels == null ||
                d.fireShake == null || d.recoil == null || d.playerDamage == null || d.hitStop == null || d.vignette == null ||
                d.death == null || d.damageNumbers == null || d.rumble == null || d.budgets == null ||
                d.budgets.resources == null || d.telemetry == null) return Fail("all sections are required", out error);
            if (!Range(d.aimAssist.maxAngleDegrees, 0f, 6f) || !Range(d.aimAssist.maxDistanceMeters, 0f, 15f) ||
                !Range(d.aimAssist.lightAngleShare, 0f, 1f) || d.aimAssist.defaultLevel < 0 || d.aimAssist.defaultLevel > 2)
                return Fail("aimAssist out of range", out error);
            if (!Range(d.aim.stickPointDistanceMeters, 1f, 15f) || !Range(d.aim.pointerMinDistanceMeters, 0f, 1f))
                return Fail("aim out of range", out error);
            if (!Range(d.camera.heightMeters, 10f, 20f) || !Range(d.camera.backDistanceMeters, 8f, 20f) ||
                !Range(d.camera.fieldOfViewDegrees, 35f, 55f) || !Range(d.camera.followSmoothSeconds, .01f, .5f) ||
                !Range(d.camera.yawDegrees, -180f, 180f) || !Range(d.camera.focusHeightOffsetMeters, 0f, 2f) ||
                !Range(d.camera.maxLookAheadMeters, 0f, 2.5f) || !Range(d.camera.lookAheadSmoothSeconds, .01f, .5f) ||
                !Range(d.camera.pointerLookAheadFactor, 0f, .5f) || !Range(d.camera.occlusionFadeSeconds, .05f, .5f) ||
                !Range(d.camera.occlusionTargetAlpha, 0f, .5f))
                return Fail("camera out of range", out error);
            if (!Range(d.shake.designGain, 0f, 1f) || !Range(d.shake.nominalAggregateCapMeters, 0f, .22f) ||
                d.shake.maxConcurrent < 1 || d.shake.maxConcurrent > 12 || !Range(d.shake.rollDegreesPerMeter, 0f, 6f) ||
                !Range(d.shake.kernels.rattleFrequency, 1f, 34f) || !Range(d.shake.kernels.rumbleFrequency, 1f, 14f) ||
                !Range(d.shake.kernels.rumbleZShare, 0f, 1f) || !Range(d.shake.kernels.rumbleDirectionShare, 0f, 1f) ||
                !Range(d.shake.kernels.rumbleRollDegreesPerMeter, 0f, 6f) || !Range(d.shake.kernels.punchDecayPower, 1f, 4f) ||
                !Range(d.shake.kernels.punchCosinePiFactor, 0f, 3f) || !Range(d.shake.kernels.punchRollDegreesPerMeter, 0f, 6f) ||
                !Range(d.shake.kernels.bumpDecayPower, 1f, 4f) || !Range(d.shake.kernels.bumpCosinePiFactor, 0f, 1.5f) ||
                !Range(d.shake.kernels.bumpZShare, 0f, 1f)) return Fail("shake out of range", out error);
            if (!Range(d.fireShake.metersAtWeightOne, 0f, .15f) || !Range(d.fireShake.baseSeconds, 0f, .2f) ||
                !Range(d.fireShake.secondsPerWeight, 0f, .2f) || d.fireShake.baseSeconds + d.fireShake.secondsPerWeight > .2f)
                return Fail("fireShake out of range", out error);
            if (!Range(d.recoil.damping, 0f, 1f) || !Range(d.recoil.pitchDegreesPerMeter, 0f, 90f) ||
                Mathf.Abs(d.recoil.springCoefficient - .25f) > 1e-6f ||
                !Range(d.recoil.animatorFullStrengthKickMeters, .05f, .5f) || !Range(d.recoil.heldKickAccumulation, 1f, 3f) ||
                !Range(d.recoil.heldKickMaxMeters, 0f, .1f) || !Range(d.recoil.heldKickMaxDegrees, 0f, 15f) ||
                !Range(d.recoil.heldKickSettleRecoveries, .5f, 6f)) return Fail("recoil out of range", out error);
            if (!Range(d.playerDamage.fullReactionDamageFraction, .01f, 1f) || !Range(d.playerDamage.minimumStrength, 0f, 1f) ||
                !Range(d.playerDamage.flashDecaySeconds, .01f, 1f)) return Fail("playerDamage out of range", out error);
            if (d.weapons == null || d.weapons.Length != 6) return Fail("exactly six weapon rows required", out error);
            var seenWeapons = new bool[6];
            for (int i = 0; i < d.weapons.Length; i++)
            {
                WeaponFeelRow w = d.weapons[i];
                if (w == null) return Fail("weapon row is required", out error);
                int id = (int)w.id;
                if (id < 0 || id >= seenWeapons.Length || seenWeapons[id]) return Fail("weapon id duplicate/unknown", out error);
                seenWeapons[id] = true;
                if (!Range(w.recoilKickMeters, 0f, .5f) || !Range(w.recoilRecoverySeconds, .02f, .6f) ||
                    !Range(w.recoilUpShare, 0f, 1f) || !Range(w.shakeWeight, 0f, 1f) ||
                    !Range(w.rumbleFraction, 0f, 1f) || w.rumbleDurationMs < 0 || w.rumbleDurationMs > 120 ||
                    !Range(w.rigKickMeters, 0f, .5f) || !(w.rigRecoverySeconds == 0f || Range(w.rigRecoverySeconds, .02f, .6f)) || !Range(w.rigUpShare, 0f, 1f))
                    return Fail("weapon row out of range: " + w.id, out error);
            }
            if (d.impacts == null || d.impacts.Length != 11) return Fail("all fixed impact rows required", out error);
            var seenImpacts = new bool[11];
            for (int i = 0; i < d.impacts.Length; i++)
            {
                ImpactFeelRow row = d.impacts[i];
                if (row == null) return Fail("impact row is required", out error);
                int id = (int)row.id;
                if (id < 0 || id >= seenImpacts.Length || seenImpacts[id]) return Fail("impact id duplicate/unknown", out error);
                seenImpacts[id] = true;
                if (!Range(row.shakeAmplitudeMeters, 0f, .15f) || !Range(row.shakeSeconds, 0f, .2f) ||
                    !Range(row.hitStopSeconds, 0f, .04f) || !Range(row.rumbleFraction, 0f, 1f) ||
                    row.rumbleDurationMs < 0 || row.rumbleDurationMs > 120) return Fail("impact row out of range: " + row.id, out error);
                if (row.id != ImpactFeelId.ArmorBreak && row.id != ImpactFeelId.HeavyDeath && row.hitStopSeconds != 0f)
                    return Fail("hit stop is fixed to zero for " + row.id, out error);
            }
            if (Mathf.Abs(d.hitStop.maxContinuousSeconds - .04f) > 1e-6f || !Range(d.hitStop.minIntervalSeconds, .12f, 1f) ||
                !Range(d.hitStop.presentationScale, .06f, 1f)) return Fail("hitStop out of range", out error);
            if (!Range(d.vignette.flashAlpha, 0f, .5f) || !Range(d.vignette.lowHealthAlpha, 0f, .5f) ||
                !Range(d.vignette.lowHealthFraction, 0f, 1f) || d.vignette.flashInFrames < 1 || d.vignette.flashInFrames > 12 ||
                d.vignette.flashOutFrames < 1 || d.vignette.flashOutFrames > 60) return Fail("vignette out of range", out error);
            float deathTotal = d.death.slowInSeconds + d.death.holdSeconds + d.death.outSeconds;
            if (!Range(d.death.slowScale, .2f, 1f) || !Range(d.death.slowInSeconds, 0f, .5f) ||
                !Range(d.death.holdSeconds, 0f, 2.5f) || !Range(d.death.outSeconds, 0f, 1f) ||
                !Range(d.death.cameraPushInFraction, 0f, .25f) ||
                d.death.resultsDelaySeconds < deathTotal + .1f || d.death.resultsDelaySeconds > 4f)
                return Fail("death results delay must follow the slow beat", out error);
            if (d.damageNumbers.maxConcurrent < 2 || d.damageNumbers.maxConcurrent > 30 ||
                !Range(d.damageNumbers.mergeWindowSeconds, 0f, 1f) || !Range(d.damageNumbers.lifetimeSeconds, .2f, 2f) ||
                !Range(d.damageNumbers.anchorOffsetYMeters, 0f, 1f) || !Range(d.damageNumbers.mergeAnchorBlend, 0f, 1f) ||
                !Range(d.damageNumbers.mergePopScale, 1f, 2f) || !Range(d.damageNumbers.mergePopSeconds, 0f, .5f) ||
                !Range(d.damageNumbers.endScale, 0f, 1f) || !Range(d.damageNumbers.fadeStartNormalized, 0f, 1f) ||
                d.damageNumbers.fontSize < 8 || d.damageNumbers.fontSize > 128 ||
                !Range(d.damageNumbers.characterSize, .005f, .2f))
                return Fail("damageNumbers out of range", out error);
            if (!Range(d.rumble.highMotorRatio, 0f, 1f) || !Range(d.rumble.classicFallbackFraction, 0f, 1f) ||
                d.rumble.classicFallbackDurationMs < 0 || d.rumble.classicFallbackDurationMs > 120 ||
                !Range(d.rumble.specialFallbackFraction, 0f, 1f) || d.rumble.specialFallbackDurationMs < 0 ||
                d.rumble.specialFallbackDurationMs > 120) return Fail("rumble out of range", out error);
            if (d.budgets.categories == null || d.budgets.categories.Length != 11) return Fail("eleven budget categories required", out error);
            var categoryIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < d.budgets.categories.Length; i++)
                if (d.budgets.categories[i] == null || string.IsNullOrEmpty(d.budgets.categories[i].id) ||
                    !categoryIds.Add(d.budgets.categories[i].id) || d.budgets.categories[i].capacity < 1 ||
                    d.budgets.categories[i].capacity > 128) return Fail("budget category duplicate/invalid", out error);
            if (!Range(d.budgets.perFrameFraction, .05f, .25f) || d.budgets.minStartsPerFrame < 1 ||
                d.budgets.minStartsPerFrame > 2 || d.budgets.importantStartMultiplier < 1 ||
                d.budgets.importantStartMultiplier > 2 || !Range(d.budgets.mergeDistanceMeters, 0f, 2f) ||
                !Range(d.budgets.mergeWindowSeconds, 0f, .5f)) return Fail("budget policy out of range", out error);
            if (!ResourceRange(d.budgets.resources) || !Range(d.telemetry.uiRefreshHz, 1f, 10f) ||
                d.telemetry.roundCapacity < 16 || d.telemetry.roundCapacity > 64 ||
                d.telemetry.ttkCapacity < 64 || d.telemetry.ttkCapacity > 512 ||
                d.telemetry.lifeCapacity < 32 || d.telemetry.lifeCapacity > 256 ||
                d.telemetry.eventTraceCapacity < 128 || d.telemetry.eventTraceCapacity > 2048)
                return Fail("resources/telemetry out of range", out error);
            return true;
        }

        private static bool ResourceRange(BudgetResourceFeelData r)
        {
            FieldInfo[] fields = typeof(BudgetResourceFeelData).GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                int value = (int)fields[i].GetValue(r);
                if (value < 1 || value > 256) return false;
            }
            return true;
        }

        private static bool FiniteTree(object value)
        {
            if (value == null) return true;
            Type t = value.GetType();
            if (t == typeof(float)) return !float.IsNaN((float)value) && !float.IsInfinity((float)value);
            if (t == typeof(double)) return !double.IsNaN((double)value) && !double.IsInfinity((double)value);
            if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return true;
            if (value is IEnumerable enumerable) { foreach (object item in enumerable) if (!FiniteTree(item)) return false; return true; }
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++) if (!FiniteTree(fields[i].GetValue(value))) return false;
            return true;
        }

        private static bool Range(float value, float min, float max) => value >= min && value <= max;
        private static bool Fail(string message, out string error) { error = message; return false; }
    }

    public static class FeelProfileCodec
    {
        public const int MaxDocumentBytes = 256 * 1024;

        public static T Clone<T>(T value) where T : class
        {
            return value == null ? null : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        }

        public static string Hash(FeelConfigData data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical(data)));
                var b = new StringBuilder(64);
                for (int i = 0; i < digest.Length; i++) b.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return b.ToString();
            }
        }

        public static string Export(FeelConfigData data, string profileId, string baseCommit)
        {
            string canonical = Canonical(data);
            string hash = Hash(data);
            return "{\"schemaVersion\":1,\"profileId\":" + Quote(profileId ?? string.Empty) +
                   ",\"baseCommit\":" + Quote(baseCommit ?? string.Empty) + ",\"contentHash\":" + Quote(hash) +
                   ",\"data\":" + canonical + "}";
        }

        public static bool TryImport(string json, out FeelProfileDocument document, out string error)
        {
            document = null; error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "empty document"; return false; }
            if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes) { error = "document exceeds 256 KiB"; return false; }
            try
            {
                object parsed = StrictJson.Parse(json);
                var root = parsed as Dictionary<string, object>;
                if (root == null) throw new FormatException("root must be an object");
                RequireKeys(root, "schemaVersion", "profileId", "baseCommit", "contentHash", "data");
                int schema = ConvertInt(root["schemaVersion"], "schemaVersion");
                if (schema != FeelConfig.CurrentSchemaVersion) throw new FormatException("unknown schemaVersion");
                string profile = ConvertString(root["profileId"], "profileId");
                string baseCommit = ConvertString(root["baseCommit"], "baseCommit");
                string suppliedHash = ConvertString(root["contentHash"], "contentHash");
                var data = (FeelConfigData)Bind(typeof(FeelConfigData), root["data"], "data");
                if (!FeelConfigValidator.Validate(data, out error)) return false;
                string calculated = Hash(data);
                if (!string.IsNullOrEmpty(suppliedHash) && !string.Equals(suppliedHash, calculated, StringComparison.OrdinalIgnoreCase))
                { error = "contentHash mismatch"; return false; }
                document = new FeelProfileDocument { schemaVersion = schema, profileId = profile, baseCommit = baseCommit,
                    contentHash = calculated, data = data };
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        public static string Canonical(object value)
        {
            if (value == null) return "null";
            Type t = value.GetType();
            if (t == typeof(string)) return Quote((string)value);
            if (t == typeof(bool)) return (bool)value ? "true" : "false";
            if (t.IsEnum) return Quote(Enum.GetName(t, value));
            if (t == typeof(float)) return ((float)value).ToString("R", CultureInfo.InvariantCulture);
            if (t == typeof(double)) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            if (t == typeof(int) || t == typeof(long) || t == typeof(uint) || t == typeof(ulong)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is IList list)
            {
                var items = new List<object>(list.Count); for (int i = 0; i < list.Count; i++) items.Add(list[i]);
                if (items.Count > 0 && items[0] != null && items[0].GetType().GetField("id") != null)
                    items.Sort(CompareById);
                var array = new StringBuilder("[");
                for (int i = 0; i < items.Count; i++) { if (i > 0) array.Append(','); array.Append(Canonical(items[i])); }
                return array.Append(']').ToString();
            }
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            var obj = new StringBuilder("{");
            for (int i = 0; i < fields.Length; i++)
            { if (i > 0) obj.Append(','); obj.Append(Quote(fields[i].Name)).Append(':').Append(Canonical(fields[i].GetValue(value))); }
            return obj.Append('}').ToString();
        }

        private static int CompareById(object a, object b)
        {
            object av = a.GetType().GetField("id").GetValue(a);
            object bv = b.GetType().GetField("id").GetValue(b);
            if (av != null && av.GetType().IsEnum)
                return Convert.ToInt32(av, CultureInfo.InvariantCulture).CompareTo(Convert.ToInt32(bv, CultureInfo.InvariantCulture));
            return string.CompareOrdinal(Convert.ToString(av, CultureInfo.InvariantCulture), Convert.ToString(bv, CultureInfo.InvariantCulture));
        }

        private static object Bind(Type target, object value, string path)
        {
            if (target == typeof(string)) return ConvertString(value, path);
            if (target == typeof(bool)) { if (!(value is bool)) throw new FormatException(path + " must be boolean"); return value; }
            if (target == typeof(int)) return ConvertInt(value, path);
            if (target == typeof(float)) return (float)ConvertNumber(value, path);
            if (target == typeof(double)) return ConvertNumber(value, path);
            if (target.IsEnum)
            {
                string name = ConvertString(value, path);
                if (!Enum.IsDefined(target, name)) throw new FormatException(path + " has unknown enum value");
                return Enum.Parse(target, name, false);
            }
            if (target.IsArray)
            {
                var values = value as List<object>; if (values == null) throw new FormatException(path + " must be an array");
                Type itemType = target.GetElementType(); Array result = Array.CreateInstance(itemType, values.Count);
                for (int i = 0; i < values.Count; i++) result.SetValue(Bind(itemType, values[i], path + "[" + i + "]"), i);
                return result;
            }
            var dict = value as Dictionary<string, object>; if (dict == null) throw new FormatException(path + " must be an object");
            object instance = Activator.CreateInstance(target);
            FieldInfo[] fields = target.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var known = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i]; known.Add(f.Name);
                if (!dict.TryGetValue(f.Name, out object raw)) throw new FormatException(path + "." + f.Name + " is required");
                f.SetValue(instance, Bind(f.FieldType, raw, path + "." + f.Name));
            }
            foreach (string key in dict.Keys) if (!known.Contains(key)) throw new FormatException(path + "." + key + " is unknown");
            return instance;
        }

        private static void RequireKeys(Dictionary<string, object> dict, params string[] keys)
        {
            var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
            for (int i = 0; i < keys.Length; i++) if (!dict.ContainsKey(keys[i])) throw new FormatException(keys[i] + " is required");
            foreach (string key in dict.Keys) if (!allowed.Contains(key)) throw new FormatException(key + " is unknown");
        }

        private static string ConvertString(object value, string path)
        { if (value is string s) return s; throw new FormatException(path + " must be a string"); }
        private static int ConvertInt(object value, string path)
        { double n = ConvertNumber(value, path); if (n != Math.Truncate(n) || n < int.MinValue || n > int.MaxValue) throw new FormatException(path + " must be an integer"); return (int)n; }
        private static double ConvertNumber(object value, string path)
        { if (value is double n && !double.IsNaN(n) && !double.IsInfinity(n)) return n; throw new FormatException(path + " must be finite number"); }

        private static string Quote(string value)
        {
            var b = new StringBuilder("\"");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': b.Append("\\\""); break; case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break; case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break;
                    default: if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); break;
                }
            }
            return b.Append('"').ToString();
        }
    }

    internal sealed class StrictJson
    {
        private readonly string _text; private int _at;
        private StrictJson(string text) { _text = text; }
        public static object Parse(string text) { var p = new StrictJson(text); object v = p.Value(); p.Space(); if (p._at != text.Length) throw new FormatException("trailing JSON"); return v; }
        private object Value()
        {
            Space(); if (_at >= _text.Length) throw new FormatException("unexpected end of JSON"); char c = _text[_at];
            if (c == '{') return Object(); if (c == '[') return Array(); if (c == '"') return String();
            if (c == 't') { Word("true"); return true; } if (c == 'f') { Word("false"); return false; }
            if (c == 'n') { Word("null"); return null; } return Number();
        }
        private Dictionary<string, object> Object()
        {
            _at++; var d = new Dictionary<string, object>(StringComparer.Ordinal); Space(); if (Take('}')) return d;
            while (true)
            {
                Space(); if (_at >= _text.Length || _text[_at] != '"') throw new FormatException("object key must be string");
                string key = String(); Space(); Need(':'); object value = Value(); if (d.ContainsKey(key)) throw new FormatException("duplicate key: " + key); d.Add(key, value);
                Space(); if (Take('}')) return d; Need(',');
            }
        }
        private List<object> Array()
        {
            _at++; var a = new List<object>(); Space(); if (Take(']')) return a;
            while (true) { a.Add(Value()); Space(); if (Take(']')) return a; Need(','); }
        }
        private string String()
        {
            Need('"'); var b = new StringBuilder();
            while (_at < _text.Length)
            {
                char c = _text[_at++]; if (c == '"') return b.ToString(); if (c < 32) throw new FormatException("control character in string");
                if (c != '\\') { b.Append(c); continue; }
                if (_at >= _text.Length) throw new FormatException("bad escape"); c = _text[_at++];
                switch (c)
                {
                    case '"': b.Append('"'); break; case '\\': b.Append('\\'); break; case '/': b.Append('/'); break;
                    case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break; case 'n': b.Append('\n'); break; case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                    case 'u': if (_at + 4 > _text.Length) throw new FormatException("bad unicode escape"); b.Append((char)int.Parse(_text.Substring(_at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); _at += 4; break;
                    default: throw new FormatException("bad escape");
                }
            }
            throw new FormatException("unterminated string");
        }
        private double Number()
        {
            int start = _at; if (Take('-')) { } if (_at >= _text.Length) throw new FormatException("bad number");
            if (_text[_at] == '0') _at++; else { if (!Digit(_text[_at])) throw new FormatException("bad number"); while (_at < _text.Length && Digit(_text[_at])) _at++; }
            if (Take('.')) { if (_at >= _text.Length || !Digit(_text[_at])) throw new FormatException("bad fraction"); while (_at < _text.Length && Digit(_text[_at])) _at++; }
            if (_at < _text.Length && (_text[_at] == 'e' || _text[_at] == 'E')) { _at++; if (_at < _text.Length && (_text[_at] == '+' || _text[_at] == '-')) _at++; if (_at >= _text.Length || !Digit(_text[_at])) throw new FormatException("bad exponent"); while (_at < _text.Length && Digit(_text[_at])) _at++; }
            if (!double.TryParse(_text.Substring(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || double.IsInfinity(n) || double.IsNaN(n)) throw new FormatException("bad number");
            return n;
        }
        private void Word(string word) { if (_at + word.Length > _text.Length || string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0) throw new FormatException("bad token"); _at += word.Length; }
        private void Space() { while (_at < _text.Length && char.IsWhiteSpace(_text[_at])) _at++; }
        private bool Take(char c) { if (_at < _text.Length && _text[_at] == c) { _at++; return true; } return false; }
        private void Need(char c) { if (!Take(c)) throw new FormatException("expected " + c); }
        private static bool Digit(char c) => c >= '0' && c <= '9';
    }
}
