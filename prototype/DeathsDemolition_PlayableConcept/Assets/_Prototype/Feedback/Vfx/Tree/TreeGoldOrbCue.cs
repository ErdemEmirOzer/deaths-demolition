using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-104 B4-3 (S-07) · AD-VISUAL K8 / §1.1 (f) / X10 B18, the "koban orb": the run tree's gold multiplier shown in the world. Coin
    /// Sense I–III (<see cref="TreeStats.GoldScale"/> &gt; 1; the catalog's 1.10 / 1.22 / 1.40) could be bought and paid more gold, but
    /// only the HUD number showed it. The orb's colour never changes (the world's gold reserve, K8): the multiplier is carried by FORM —
    /// the orb at ×<see cref="KobanScale"/> and a small added glint. AP-041 has no koban silhouette sheet yet (later); until it does, the
    /// form is the existing orb (the AP-010 pouch, or the WP-004 sphere without the catalog) at ×1.25 with the glint.
    /// <list type="bullet">
    /// <item>When: read once per orb life in GoldOrb.Begin (<see cref="Arm"/>), the frame the kill's gold took TreeEconomyV2's scale — so
    /// a koban orb is exactly an orb whose gold the tree multiplied. GoldScale 1 → ×1, no glint, and nothing added to a body that never
    /// had a koban life (this component is added at a body's first koban life only).</item>
    /// <item>Size: GoldOrb multiplies ×1.25 into its <c>_baseScale</c>, so the hop, the collect flash (FlashScale) and the HUD flight
    /// (its end scale) all scale from it as before.</item>
    /// <item>Glint: B4-2's socket glint sheet and cell (<see cref="Row"/>, frame <see cref="Frame"/>: the four-point star with its hot
    /// core), ADDED (one shared copy of the row's material on Blend One One), one quad per orb body built at its first koban life — 0 B per
    /// spawn after that. Screen-aligned to the gameplay camera (<see cref="FeedbackCameraRef"/>) on the orb's upper-right rim
    /// (<see cref="RimOffset"/>), lifted towards the camera (<see cref="LiftShare"/>) so the orb never hides it. Tint: the orb's own
    /// colour (GoldOrbTuning.Color, linear, brightest channel 1), so its hue is the orb's. It holds <see cref="RestLevel"/> of its peak
    /// (a still frame always shows it) and twinkles once a period to <see cref="PeakValue"/> — the AD's lowest bloom-level value (E4 III):
    /// only the twinkle's peak passes the bloom threshold; × FlashScale (Reduce Flashes: no bloom, the glint stays).</item>
    /// <item>Tiers, abartısız: I / II / III differ only in the glint's size (<see cref="GlintSize"/>) and twinkle rate
    /// (<see cref="GlintPeriod"/>); the ×1.25 and the colour are the same for all three.</item>
    /// <item>Life: the glint shows while the packet lies in the world (Spawned / Waiting / Magnetizing); a taken packet (the flash, the
    /// HUD flight) shows the coin alone; the pool return (GoldOrb.OnDespawned) calls <see cref="Off"/>.</item>
    /// </list>
    /// Scaled time (a paused game holds it, as it holds the orb); marker <c>WP104.Vfx.Koban</c>; 0 B per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TreeGoldOrbCue : MonoBehaviour
    {
        public static readonly ProfilerMarker Marker = new ProfilerMarker("WP104.Vfx.Koban");

        /// <summary>AD K8: the koban orb's size over the orb's (one value for every tier).</summary>
        public const float KobanScale = 1.25f;
        /// <summary>Tier thresholds: midway between the catalog's Coin Sense values (I 1.10 · II 1.22 · III 1.40; cap 1.60 → III).</summary>
        public const float TierIIFrom = 1.16f, TierIIIFrom = 1.31f;
        /// <summary>AP-041 <c>G_B7_heat_glint</c> (catalog <c>fb_ap041t_b7_heat_glint</c>) — the sheet B4-2's socket glint card draws.</summary>
        public const TreeVfxRow Row = TreeVfxRow.B7HeatGlint;
        /// <summary>The sheet's first cell (the four-point star at full strength), held.</summary>
        public const int Frame = 0;
        /// <summary>The glint between twinkles, as a share of its peak.</summary>
        public const float RestLevel = .45f;
        /// <summary>The twinkle's linear HDR peak (the orb colour × this): the AD's E4 III value, just over the bloom threshold 1.25.</summary>
        public const float PeakValue = 1.4f;
        /// <summary>The twinkle takes this share of a period; its rise this share of the twinkle (a quick rise, a slower fall).</summary>
        public const float TwinkleShare = .3f, AttackShare = .25f;
        /// <summary>The phase of the twinkle's peak.</summary>
        public const float PeakPhase = TwinkleShare * AttackShare;
        /// <summary>A phase between twinkles (the rest level).</summary>
        public const float RestPhase = .65f;
        /// <summary>The card grows by this share at the twinkle's peak.</summary>
        public const float SizePulseShare = .15f;
        /// <summary>The card's centre sits this share of the orb's diameter right and up on the screen (≈ 0.76 of its radius: the rim).</summary>
        public const float RimOffset = .27f;
        /// <summary>…and this share of the diameter towards the camera, in front of the orb's own surface.</summary>
        public const float LiftShare = .6f;
        public const float SizeMinMetres = .25f, SizeMaxMetres = 1.4f;
        /// <summary>The card's GameObject under the orb (a sibling of the orb's "Visual", like the AP-010 sparkle card).</summary>
        public const string CardName = "Koban glint";
        /// <summary>GoldOrb's visual child (the AP-010 pouch or the WP-004 sphere; both are named so).</summary>
        public const string VisualName = "Visual";

        /// <summary>The card's edge over the koban orb's diameter, tiers I–III.</summary>
        private static readonly float[] s_sizeFactor = { 1.5f, 1.75f, 2f };
        /// <summary>Seconds between twinkles, tiers I–III.</summary>
        private static readonly float[] s_period = { 1.6f, 1.25f, .9f };

        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int AlphaCapId = Shader.PropertyToID("_AlphaCap");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");

        private static Material s_material;

        private GoldOrb _orb;
        private MeshRenderer _card;
        private Transform _cardT;
        private MaterialPropertyBlock _block;
        private Renderer _visual;
        private int _visualTries;
        private bool _built, _cardEnabled = true;
        private float _size, _phase0, _clock;
        private Color _colour;

        /// <summary>This life is a koban life (GoldScale &gt; 1 at its Begin).</summary>
        public bool Armed { get; private set; }
        /// <summary>Coin Sense tier of this life (1–3), 0 when not armed.</summary>
        public int Tier { get; private set; }
        /// <summary>The tree's GoldScale this life was armed with (1 when not armed).</summary>
        public float GoldScale { get; private set; } = 1f;
        /// <summary>The koban orb's nominal diameter (m): the orb's own × <see cref="KobanScale"/>.</summary>
        public float Diameter { get; private set; }
        /// <summary>Seconds between twinkles this life (<see cref="GlintPeriod"/>).</summary>
        public float Period { get; private set; }
        /// <summary>The card's edge (m) at the last pose.</summary>
        public float CardEdge { get; private set; }
        /// <summary>The card's linear <c>_Tint</c> at the last pose (the orb's hue × the level × <see cref="PeakValue"/>).</summary>
        public Vector4 CardTint { get; private set; }
        /// <summary>The glint quad (null until the body's first koban life, or without the AP-041 row).</summary>
        public Renderer CardRenderer => _card;
        /// <summary>The glint is drawn now.</summary>
        public bool CardShown => _card != null && _card.enabled;
        /// <summary>The twinkle's phase now (whole numbers = period starts).</summary>
        public float Phase => _phase0 + (Period > 0f ? _clock / Period : 0f);

        /// <summary>The AP-041 glint row is in the catalog with a playable material (tests: the glint needs it, the ×1.25 does not).</summary>
        public static bool RowReady => VfxLibrary.TryGetPlayable(TreeVfxIds.Id(Row), out _);

        /// <summary>
        /// Tests / captures: false takes the glint off at once for this life (the orb's own pixels); true poses it again. Every new life
        /// starts with it on.
        /// </summary>
        public bool CardEnabled
        {
            get => _cardEnabled;
            set
            {
                _cardEnabled = value;
                Pose();
            }
        }

        // ------------------------------------------------------------------ pure rules (Wp104B43KobanRulesTests)

        /// <summary>The tree multiplies this orb's gold: GoldScale above 1 (TreeEconomyV2 pays the base for a scale ≈ 1).</summary>
        public static bool IsKoban(float goldScale) => goldScale > 1f && !Mathf.Approximately(goldScale, 1f);

        /// <summary>Coin Sense tier 1–3 of a GoldScale; 0 when it is not a koban orb.</summary>
        public static int TierOf(float goldScale) => !IsKoban(goldScale) ? 0 : goldScale < TierIIFrom ? 1 : goldScale < TierIIIFrom ? 2 : 3;

        /// <summary>The orb's size factor: ×<see cref="KobanScale"/> for a koban orb, exactly 1 otherwise.</summary>
        public static float ScaleFor(float goldScale) => IsKoban(goldScale) ? KobanScale : 1f;

        /// <summary>The glint card's edge (m) for a tier over a koban orb of this diameter; 0 without a tier.</summary>
        public static float GlintSize(int tier, float kobanDiameter) =>
            tier < 1 ? 0f : Mathf.Clamp(s_sizeFactor[Mathf.Min(tier, 3) - 1] * Mathf.Max(0f, kobanDiameter), SizeMinMetres, SizeMaxMetres);

        /// <summary>Seconds between twinkles for a tier; 0 without a tier.</summary>
        public static float GlintPeriod(int tier) => tier < 1 ? 0f : s_period[Mathf.Min(tier, 3) - 1];

        /// <summary>The twinkle (0–1) at a phase: 0 outside its <see cref="TwinkleShare"/> of the period, 1 at <see cref="PeakPhase"/>.</summary>
        public static float Twinkle(float phase)
        {
            float p = phase - Mathf.Floor(phase);
            if (p >= TwinkleShare) return 0f;
            float x = p / TwinkleShare;
            float k = x < AttackShare ? x / AttackShare : 1f - (x - AttackShare) / (1f - AttackShare);
            return k * k;
        }

        /// <summary>The glint's level (share of <see cref="PeakValue"/>): <see cref="RestLevel"/> → 1 with the twinkle, × FlashScale.</summary>
        public static float Level(float twinkle, float flashScale) =>
            (RestLevel + (1f - RestLevel) * Mathf.Clamp01(twinkle)) * Mathf.Clamp01(flashScale);

        /// <summary>The card's size factor with the twinkle.</summary>
        public static float SizePulse(float twinkle) => 1f + SizePulseShare * Mathf.Clamp01(twinkle);

        /// <summary>The orb colour's hue in linear, its brightest channel 1 (white for black).</summary>
        public static Color Hue(Color orbColour)
        {
            Color c = orbColour.linear;
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return max > 0f ? new Color(c.r / max, c.g / max, c.b / max, 1f) : Color.white;
        }

        /// <summary>The card's <c>_Tint</c> (linear, set with SetVector — no gamma step on an HDR value): the orb's hue × level × peak.</summary>
        public static Vector4 Tint(Color orbColour, float level)
        {
            Color h = Hue(orbColour);
            float v = PeakValue * Mathf.Max(0f, level);
            return new Vector4(h.r * v, h.g * v, h.b * v, 1f);
        }

        /// <summary>A packet's own twinkle phase (deterministic from its transaction id; no global Random is drawn).</summary>
        public static float SeedPhase(int seed)
        {
            uint h = unchecked((uint)seed * 2654435761u);
            return (h >> 8) * (1f / 16777216f);
        }

        // ------------------------------------------------------------------ the GoldOrb hook

        /// <summary>
        /// GoldOrb.Begin: the orb's size factor for this life (×<see cref="KobanScale"/> while the tree's GoldScale is above 1, else 1)
        /// and the glint armed or off. <paramref name="cue"/> is the orb's cached component (added at the body's first koban life, then
        /// kept); <paramref name="orbDiameter"/> the orb's own scale before the factor; <paramref name="seed"/> the packet's transaction id.
        /// </summary>
        public static float Arm(GoldOrb orb, ref TreeGoldOrbCue cue, float orbDiameter, Color orbColour, int seed)
        {
            float goldScale = TreeRuntime.Current.GoldScale;
            if (orb == null || !IsKoban(goldScale))
            {
                if (cue != null) cue.Off();
                return 1f;
            }

            using (Marker.Auto())
            {
                if (cue == null && !orb.TryGetComponent(out cue)) cue = orb.gameObject.AddComponent<TreeGoldOrbCue>();

                cue.On(orb, goldScale, orbDiameter * KobanScale, orbColour, seed);
            }

            return KobanScale;
        }

        /// <summary>The life ended (GoldOrb.OnDespawned) or is not a koban life: the glint off, no per-frame work.</summary>
        public void Off()
        {
            Armed = false;
            Tier = 0;
            GoldScale = 1f;
            Period = 0f;
            _orb = null;
            HideCard();
            enabled = false;
        }

        /// <summary>Tests / captures: hold the twinkle at <paramref name="phase"/> (<see cref="PeakPhase"/>, <see cref="RestPhase"/>) and
        /// pose the glint now.</summary>
        public void SetPhase(float phase)
        {
            _phase0 = phase;
            _clock = 0f;
            Pose();
        }

        private void On(GoldOrb orb, float goldScale, float diameter, Color colour, int seed)
        {
            if (!_built) Build();
            _orb = orb;
            Armed = true;
            GoldScale = goldScale;
            Tier = TierOf(goldScale);
            Diameter = diameter;
            Period = GlintPeriod(Tier);
            _size = GlintSize(Tier, diameter);
            _colour = colour;
            _phase0 = SeedPhase(seed);
            _clock = 0f;
            _cardEnabled = true;
            _visualTries = 0;
            enabled = true;
            Pose();
        }

        private void OnDisable() => HideCard();

        private void LateUpdate()
        {
            using (Marker.Auto())
            {
                _clock += Time.deltaTime;
                if (Period > 0f && _clock >= Period) _clock -= Period * Mathf.Floor(_clock / Period);
                Pose();
            }
        }

        /// <summary>The glint onto the orb's rim, facing the gameplay camera, at this moment's twinkle (0 B).</summary>
        private void Pose()
        {
            if (!Armed)
            {
                HideCard();
                return;
            }

            GoldPacketState state = _orb != null ? _orb.Packet.State : GoldPacketState.Recycled;
            if (state != GoldPacketState.Spawned && state != GoldPacketState.Waiting && state != GoldPacketState.Magnetizing)
            {
                HideCard();   // taken: the flash / HUD flight shows the coin alone
                enabled = false;
                return;
            }

            float flash = Mathf.Clamp01(GameFeelSettings.FlashScale);
            Camera cam = FeedbackCameraRef.Current;
            if (_card == null || !_cardEnabled || flash <= 0f || cam == null)
            {
                HideCard();
                return;
            }

            if (_visual == null && _visualTries < 2)
            {
                // GoldOrb builds its visual in OnSpawned (a pooled body has it at Begin); a bare body builds it after Begin's hook
                _visualTries++;
                Transform v = transform.Find(VisualName);
                if (v != null && v.TryGetComponent(out Renderer visual)) _visual = visual;
            }

            Transform ct = cam.transform;
            Vector3 centre = _visual != null ? _visual.bounds.center : transform.position;   // the bob moves the visual, not the root
            float twinkle = Twinkle(Phase);
            Vector3 toCam = ct.position - centre;
            float dist = toCam.magnitude;
            Vector3 lift = dist > 1e-4f ? toCam * (LiftShare * Diameter / dist) : Vector3.zero;
            _cardT.SetPositionAndRotation(centre + (ct.right + ct.up) * (RimOffset * Diameter) + lift, ct.rotation);
            float edge = _size * SizePulse(twinkle);
            _cardT.localScale = new Vector3(edge, edge, 1f);
            CardEdge = edge;
            CardTint = Tint(_colour, Level(twinkle, flash));
            _block.SetVector(TintId, CardTint);
            _card.SetPropertyBlock(_block);
            if (_card.gameObject.layer != gameObject.layer) _card.gameObject.layer = gameObject.layer;
            if (!_card.enabled) _card.enabled = true;
        }

        private void HideCard()
        {
            if (_card != null && _card.enabled) _card.enabled = false;
        }

        /// <summary>Once per orb body (its first koban life): the property block and, with the AP-041 row, the glint quad.</summary>
        private void Build()
        {
            _built = true;
            _block = new MaterialPropertyBlock();
            Material material = SharedMaterial();
            if (material == null) return;   // no AP-041 row (the art pipeline has not run): the ×1.25 alone
            var go = new GameObject(CardName) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = VfxLibrary.Quad;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.enabled = false;
            _card = r;
            _cardT = go.transform;
            _block.SetFloat(FrameId, Frame);
            _block.SetFloat(AlphaId, 1f);
            _block.SetFloat(AlphaCapId, 1f);
        }

        /// <summary>The glint row's material once for every orb (VfxLibrary), as a copy drawn ADDED: the premultiplied sheet on Blend One
        /// One (TreeAttachGlow's socket card does the same). Null without the catalog row.</summary>
        private static Material SharedMaterial()
        {
            if (s_material != null) return s_material;
            if (!VfxLibrary.TryGetPlayable(TreeVfxIds.Id(Row), out VfxCatalogEntry row)) return null;
            s_material = new Material(row.Material) { name = row.Material.name + " (WP-104 koban glint, added)", hideFlags = HideFlags.DontSave };
            s_material.SetFloat(DstBlendId, (float)BlendMode.One);
            return s_material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_material = null;
        }
    }
}
