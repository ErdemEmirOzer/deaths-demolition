using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Enemies;
using DD.Prototype.Rendering;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>What a lamp is, from its scene name (S-05 LAMP-CONTRACT: <c>Lamp_&lt;id&gt;_&lt;baked|pool|emissive|unlit&gt;</c>).</summary>
    public enum LampKind : byte
    {
        Legacy = 0,     // a lantern found by family / name (no Lamp_ prefix: the AP-023 stone lanterns, the axis kasuga)
        Baked = 1,      // flame + halo + sparks; S-05 bakes its light → the pool light reaches characters only
        Pool = 2,       // flame + halo + sparks; first claim on the ≤ 6 pool lights
        Emissive = 3,   // flame + glow flicker, never a light
        Unlit = 4,      // nothing (a dead lamp)
        Toro = 5        // M8 floating tōrō (placement group M8_toro, not a lamp): flame + glow + bob / drift + rings + streak
    }

    /// <summary>
    /// WP-047 D · The AP-010B `lantern_flame` loop (4×4, 24 fps, 0.12 × 0.18 m) on the lanterns of the loaded world. One scan per world
    /// load (allocating, off the hot path), at most <see cref="MaxFlames"/> flames, each a pooled looping card. The scan re-arms when the
    /// scene changes.
    ///
    /// WP-093 1b (LOOK-BIBLE §5) · ONE FLAME, ONE SIGNAL. Each lamp has one flicker sample s ∈ [−1, 1] (<see cref="FlickerSignal"/>:
    /// 1.3 Hz × 0.7 + 6.5 Hz × 0.3 smooth value noise, nothing above 8 Hz). Light intensity (±12 %), card α (±10 %), card scale (±8 %),
    /// halo α, the glow slot's emission and the reflection streak all read the SAME sample in the same 20 Hz pass; reduce-flashes
    /// scales every swing. Halo Ø 0.7 m α 0.12 #E8A870; sparks (High / Cinematic, ≤ 16 / 30 live, ≤ 3 per lamp) 1.0–1.5 cm #E8B48C.
    ///
    /// WP-093 · AP-030 lamp contract (S-05): SOCKET_Flame / SOCKET_Light, the AP030_M_&lt;family&gt;_glow_v001 slot (and the AP-023
    /// chōchin / tsuri paper) flickering on _EmissionColor through a per-slot MaterialPropertyBlock, rendering layers per kind.
    ///
    /// WP-092 layout (LAMP-CONTRACT.md, layout-data.json v002): lamps are found by NAME (<see cref="ParseLampName"/>) under the builder
    /// root, each kind behaving as <see cref="LampKind"/> says; the 5 pool lamps (M1_L01, M5_L01, L-K1-7, L-K1-8, L-C01-1) are lit first,
    /// the remaining slots go to the baked / legacy lamps nearest the player. The M8_toro group's 7 tōrō bob (±1 cm, 0.3 Hz), yaw (±5°)
    /// and drift (r ≤ 0.4 m, 2–4 cm/s), ring the water every 3–5 s (Ø 0.3–0.8 m) and carry a fake reflection streak 0.15 × 0.6 m (URP 14:
    /// no SSR). The axis lanterns on the M4 rain mirror get a −z streak 2.0 × 0.12 m α 0.4. Hanging chōchin / tsuri swing ±3–5° (per
    /// moment) at 0.37 Hz, phase = position along the wind, amplitude with the gust. Shoji (M_ap023_washi / lantern_paper) in the
    /// shoji moments (R1 south, R4) take a warm transmitted emission #E8C89A (v ≤ 0.78) driven by the nearest lamp's sample.
    /// A mover that Unity static-batched cannot move: it is left still and counted (<see cref="MotionBlockedByStaticBatch"/>).
    /// </summary>
    public sealed class LanternFlames
    {
        /// <summary>WP-092: 130 layout lamps + 7 tōrō + the axis / garden stones (was 40).</summary>
        public const int MaxFlames = 160;

        /// <summary>
        /// FINAL c4093280 perf (C-00 10.10): flame cards farther than this from the camera are hidden (0 = none). Set by
        /// QualityModeApplier: 40 m in High top-down — the TD frame ends ~35 m out, so the cap only takes cards no one sees;
        /// every other tier / mode 0. Applied on the flicker pass.
        /// </summary>
        public static float CardCullMetres;
        public const float RescanInterval = 5f;
        private const float LoopSeconds = 3600f;
        /// <summary>The firebox window of the AP-003B stone lantern sits just above the bounds centre (0.6 put the flame inside the roof mass).</summary>
        public const float FireboxHeightFraction = 0.08f;
        /// <summary>×1.6 on the manifest 0.12 × 0.18 m so the flame still reads (~10 px) at the 20 m gameplay camera.</summary>
        public const float FlameScale = 1.6f;
        /// <summary>The greyscale flame sheet carries no tint in the manifest. AD M5 (WP-047): (1.0, 0.60, 0.28) × 0.9.</summary>
        public static readonly Color FlameTint = new Color(1.0f * 0.9f, 0.60f * 0.9f, 0.28f * 0.9f, 1f);
        /// <summary>Flames / glow slots further than this from the camera keep their last state (the flicker pass skips them).</summary>
        public const float FlickerCullMetres = 45f;

        /// <summary>The lamps the WP-093 systems read (the feedback director's instance).</summary>
        public static LanternFlames Current { get; private set; }

        private readonly List<FlipbookEffect> _flames = new List<FlipbookEffect>(MaxFlames);
        private readonly List<Vector3> _anchors = new List<Vector3>(MaxFlames);       // flame point at rest (SOCKET_Flame or the heuristic)
        private readonly List<Vector3> _lightPoints = new List<Vector3>(MaxFlames);   // light centre at rest
        private readonly bool[] _baked = new bool[MaxFlames];                          // light reaches characters only
        private readonly LampKind[] _kind = new LampKind[MaxFlames];
        private readonly string[] _lampId = new string[MaxFlames];
        private readonly sbyte[] _moment = new sbyte[MaxFlames];
        private readonly byte[] _lightMode = new byte[MaxFlames];                      // 0 none, 1 characters only, 2 full
        private readonly bool[] _priority = new bool[MaxFlames];
        private readonly bool[] _haloOn = new bool[MaxFlames];
        private readonly float[] _haloAlpha = new float[MaxFlames];
        private readonly byte[] _maxSparks = new byte[MaxFlames];
        private readonly float[] _scale = new float[MaxFlames];
        private readonly float[] _groundY = new float[MaxFlames];

        /// <summary>Test isolation: when set, the scan only counts lamps under this root.</summary>
        public Transform ScanRoot;

        // ------------------------------------------------------------------ WP-093 · AP-030 lamp contract (S-05)

        public const string FlameSocketName = "SOCKET_Flame";
        public const string LightSocketName = "SOCKET_Light";
        public const string LampPrefix = "Lamp_";
        /// <summary>AP030_M_&lt;family&gt;_glow_v001: the slot whose emission flickers with the flame.</summary>
        public const string GlowSlotPrefix = "AP030_M_", GlowSlotMarker = "_glow_v";
        public const string GlowEmissionProperty = "_EmissionColor";
        public const string PaperSlotMarker = "lantern_paper", WashiSlotMarker = "washi";
        /// <summary>Every family the pool lights (SurfaceTag.Family or the object name). "lantern" covers lantern_kasuga / oribe / yukimi / chochin / tsuri.</summary>
        public static readonly string[] LanternFamilies = { "lantern", "andon", "kerosene_lamp", "wall_sconce" };
        /// <summary>Families S-05 bakes an interior point light for (2200–2700 K): their realtime light reaches characters only.</summary>
        public static readonly string[] BakedFamilies = { "andon", "kerosene_lamp", "wall_sconce" };
        /// <summary>The Characters rendering layer alone (UniversalRenderPipelineGlobalSettings: bit 0 Default, bit 1 Characters).</summary>
        public const uint CharactersOnlyRenderingLayers = ZombieSkinnedVisual.CharacterRenderingLayers & ~1u;
        public const uint FullRenderingLayers = uint.MaxValue;
        /// <summary>
        /// S-05 LAMP-CONTRACT (08.10, S-07 decision): "pool" lamps are baked too (LampLight Baked in the bake), so their realtime pool
        /// light reaches Characters only, like "baked" — no double lighting on the static world. Legacy unbaked outdoor lanterns
        /// (no Lamp_ name) keep the full mask. One switch, tests read it.
        /// </summary>
        public static bool PoolLampsFullMask = false;

        /// <summary>The URP rendering layers a pooled light writes (UniversalAdditionalLightData.renderingLayers — the field URP 14
        /// reads); null when the light has no URP data. Tests read it here: the test assembly does not reference URP.</summary>
        public static uint? RenderingLayersOf(Light light)
        {
            var data = light != null ? light.GetComponent<UniversalAdditionalLightData>() : null;
            return data != null ? data.renderingLayers : (uint?)null;
        }

        /// <summary>
        /// "Lamp_M1_L01_pool" → id "M1_L01", kind Pool. The kind is the last "_" token (baked / pool / emissive / unlit); anything else
        /// is not a contract lamp (false).
        /// </summary>
        public static bool ParseLampName(string name, out string id, out LampKind kind)
        {
            id = null;
            kind = LampKind.Legacy;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(LampPrefix, System.StringComparison.Ordinal)) return false;
            int last = name.LastIndexOf('_');
            if (last <= LampPrefix.Length) return false;
            string suffix = name.Substring(last + 1);
            switch (suffix)
            {
                case "baked": kind = LampKind.Baked; break;
                case "pool": kind = LampKind.Pool; break;
                case "emissive": kind = LampKind.Emissive; break;
                case "unlit": kind = LampKind.Unlit; break;
                default: return false;
            }

            id = name.Substring(LampPrefix.Length, last - LampPrefix.Length);
            return id.Length > 0;
        }

        public const int MaxGlowSlots = MaxFlames * 4 + MaxShojiSlots;   // LOD0..2 + a spare per lamp, + the shoji
        private readonly Renderer[] _glowRenderer = new Renderer[MaxGlowSlots];
        private readonly int[] _glowSlot = new int[MaxGlowSlots];
        private readonly int[] _glowAnchor = new int[MaxGlowSlots];
        private readonly Color[] _glowBase = new Color[MaxGlowSlots];
        private readonly Vector3[] _glowPos = new Vector3[MaxGlowSlots];
        private readonly bool[] _glowShoji = new bool[MaxGlowSlots];
        private int _glowCount;
        private MaterialPropertyBlock _glowBlock;
        private static readonly int GlowEmissionId = Shader.PropertyToID(GlowEmissionProperty);
        private const string EmissionKeyword = "_EMISSION";
        // Scan only: a material whose _EMISSION keyword is off gets one runtime copy with it on (the emission stays black until the block).
        private readonly Dictionary<Material, Material> _emissiveCopies = new Dictionary<Material, Material>();
        private readonly List<Renderer> _swappedRenderers = new List<Renderer>();
        private readonly List<Material[]> _swappedOriginals = new List<Material[]>();

        /// <summary>AP-023 chōchin / tsuri paper glow when its material carries no emission of its own (#E8A870 × 0.55, v 0.50).</summary>
        public static readonly Color PaperGlow = new Color(232f / 255f * 0.55f, 168f / 255f * 0.55f, 112f / 255f * 0.55f, 1f);
        /// <summary>
        /// C · shoji transmission: #E8C89A at v 0.78 / 1.12 — the lamp's +12 % peak lands exactly on S-05's v ≤ 0.78 (#E8C89A is v 0.91).
        /// </summary>
        public static readonly Color ShojiEmission = new Color(0.9098f, 0.7843f, 0.6039f, 1f) * (0.78f / 1.12f / 0.9098f);
        public const float ShojiLampReach = 5f;
        public const int MaxShojiSlots = 96;
        private int _shojiCount;
        public int ShojiSlots => _shojiCount;

        /// <summary>Token match (a family inside a longer name: "SM_andon_V01", "kerosene_lamp_table"); "lantern" keeps the legacy substring rule.</summary>
        private static bool HasFamilyToken(string name, string family)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int at = name.IndexOf(family, System.StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;
            if (family == "lantern") return true;
            while (at >= 0)
            {
                bool startOk = at == 0 || !char.IsLetter(name[at - 1]);
                int end = at + family.Length;
                bool endOk = end >= name.Length || !char.IsLetter(name[end]);
                if (startOk && endOk) return true;
                at = name.IndexOf(family, at + 1, System.StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>True when <paramref name="familyOrName"/> is one of <see cref="LanternFamilies"/> (and not a flame card).</summary>
        public static bool IsLanternFamily(string familyOrName)
        {
            if (string.IsNullOrEmpty(familyOrName) || familyOrName.IndexOf("flame", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            for (int i = 0; i < LanternFamilies.Length; i++) if (HasFamilyToken(familyOrName, LanternFamilies[i])) return true;
            return false;
        }

        public static bool IsBakedFamily(string familyOrName)
        {
            if (string.IsNullOrEmpty(familyOrName)) return false;
            for (int i = 0; i < BakedFamilies.Length; i++) if (HasFamilyToken(familyOrName, BakedFamilies[i])) return true;
            return false;
        }

        /// <summary>Hanging paper lanterns (pendulum sway, paper glow).</summary>
        public static bool IsPaperLantern(string familyOrName) =>
            !string.IsNullOrEmpty(familyOrName) && (familyOrName.IndexOf("chochin", System.StringComparison.OrdinalIgnoreCase) >= 0 || familyOrName.IndexOf("tsuri", System.StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>The glow slot's emission for flicker sample <paramref name="s"/>: the base × (1 ± 0.12 × scale) — the light's swing.</summary>
        public static Color GlowEmissionFor(Color baseEmission, float s, float flashScale)
        {
            float k = 1f + LightFlickerAmplitude * Mathf.Clamp01(flashScale) * s;
            return new Color(baseEmission.r * k, baseEmission.g * k, baseEmission.b * k, baseEmission.a);
        }

        public int AnchorCount => _anchors.Count;
        public Vector3 FlamePointOf(int i) => _anchors[i];
        public Vector3 LightPointOf(int i) => _lightPoints[i];
        /// <summary>The flame point where it is now (rest + pendulum / tōrō motion; the slow wind sway not included).</summary>
        public Vector3 MovedFlamePointOf(int i) => _anchors[i] + _motion[i];
        public bool IsBaked(int i) => i >= 0 && i < MaxFlames && _baked[i];
        public LampKind KindOf(int i) => _kind[i];
        public string LampIdOf(int i) => _lampId[i];
        /// <summary>The moment index (<see cref="WorldMoments.Current"/>) the lamp stands in, −1 for none.</summary>
        public int MomentOf(int i) => _moment[i];
        /// <summary>0 no light, 1 characters only, 2 full mask.</summary>
        public int LightModeOf(int i) => _lightMode[i];
        public bool IsPriority(int i) => _priority[i];
        public bool HasHalo(int i) => _haloOn[i];
        public float HaloAlphaOf(int i) => _haloAlpha[i];
        public int MaxSparksOf(int i) => _maxSparks[i];
        public int GlowSlots => _glowCount;
        public Renderer GlowRendererOf(int k) => _glowRenderer[k];
        public int GlowMaterialIndexOf(int k) => _glowSlot[k];
        public int GlowAnchorOf(int k) => _glowAnchor[k];
        public Color GlowBaseOf(int k) => _glowBase[k];
        public bool GlowIsShoji(int k) => _glowShoji[k];

        private static Transform FindChildDeep(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c.name == name) return c;
                Transform d = FindChildDeep(c, name);
                if (d != null) return d;
            }

            return null;
        }

        /// <summary>A layout lamp's family: its SurfaceTag, else the first lantern-family name under it (renderer / mesh names).</summary>
        private static string FamilyOf(Transform root)
        {
            var tag = root.GetComponent<SurfaceTag>();
            if (tag != null && IsLanternFamily(tag.Family)) return tag.Family;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);   // scan only
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsLanternFamily(renderers[i].name)) return renderers[i].name;
                var mf = renderers[i].GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && IsLanternFamily(mf.sharedMesh.name)) return mf.sharedMesh.name;
            }

            return root.name;
        }

        private static MeshRenderer BodyOf(Transform root)
        {
            MeshRenderer[] all = root.GetComponentsInChildren<MeshRenderer>(false);   // scan only
            for (int i = 0; i < all.Length; i++) if (all[i].name.IndexOf("LOD0", System.StringComparison.OrdinalIgnoreCase) >= 0) return all[i];
            return all.Length > 0 ? all[0] : null;
        }

        /// <summary>Scan only: one lamp → flame / light points (sockets when present), its kind's rules, its glow slots, its motion.</summary>
        private bool AddLamp(Transform root, string family, Renderer body, LampKind kind, string lampId)
        {
            if (_anchors.Count >= MaxFlames || kind == LampKind.Unlit) return false;
            Transform flame = FindChildDeep(root, FlameSocketName);
            Vector3 flamePoint, lightPoint;
            if (flame != null)
            {
                Transform light = FindChildDeep(root, LightSocketName);
                flamePoint = flame.position;
                lightPoint = light != null ? light.position : flamePoint;
            }
            else
            {
                if (body == null) return false;
                Bounds b = body.bounds;
                // A lantern fragment / decal is not a body; the small AP-030 lamps (andon, sconce, a 0.6 tōrō) are allowed down to 15 cm.
                bool small = IsBakedFamily(family) || kind == LampKind.Toro;
                if (b.size.y < (small ? 0.15f : 0.4f)) return false;
                flamePoint = new Vector3(b.center.x, b.center.y + b.extents.y * FireboxHeightFraction, b.center.z);
                lightPoint = flamePoint + Vector3.up * 0.08f;
            }

            int index = _anchors.Count;
            _anchors.Add(flamePoint);
            _lightPoints.Add(lightPoint);
            _kind[index] = kind;
            _lampId[index] = lampId;
            int moment = WorldMoments.Find(root.position);
            _moment[index] = (sbyte)moment;
            MomentSpec spec = moment >= 0 ? WorldMoments.Current[moment] : default;
            _scale[index] = kind == LampKind.Toro ? Mathf.Clamp(root.lossyScale.y, 0.3f, 1f) : 1f;
            _groundY[index] = root.position.y;

            switch (kind)
            {
                case LampKind.Baked:
                    _lightMode[index] = 1;
                    break;
                case LampKind.Pool:
                    _lightMode[index] = (byte)(PoolLampsFullMask ? 2 : 1);
                    break;
                case LampKind.Emissive:
                case LampKind.Toro:
                    _lightMode[index] = 0;
                    break;
                default:   // legacy: the AP-030 families by tag are baked (WP-093 AP-030 contract), the stones are outdoor
                    _lightMode[index] = (byte)(IsBakedFamily(family) ? 1 : 2);
                    break;
            }

            _baked[index] = _lightMode[index] == 1;
            _priority[index] = kind == LampKind.Pool;
            _haloOn[index] = kind == LampKind.Baked || kind == LampKind.Pool || kind == LampKind.Legacy || (kind == LampKind.Emissive && spec.HaloEmissive);
            _haloAlpha[index] = WorldMoments.HaloAlphaFor(lampId, HaloAlpha);
            _maxSparks[index] = (byte)(kind == LampKind.Emissive ? Mathf.Clamp(spec.EmissiveSparks, 0, MaxSparksPerLantern) : kind == LampKind.Toro ? 0 : MaxSparksPerLantern);

            // Glow slots: the AP-030 glow material; the AP-023 paper of a chōchin / tsuri.
            bool paper = IsPaperLantern(family);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);   // scan only
            for (int r = 0; r < renderers.Length && _glowCount < MaxGlowSlots - MaxShojiSlots; r++)
            {
                Renderer rd = renderers[r];
                if (rd is ParticleSystemRenderer) continue;
                Material[] mats = rd.sharedMaterials;
                for (int m = 0; m < mats.Length && _glowCount < MaxGlowSlots - MaxShojiSlots; m++)
                {
                    Material mat = mats[m];
                    if (mat == null) continue;
                    bool glow = mat.name.StartsWith(GlowSlotPrefix, System.StringComparison.Ordinal) && mat.name.IndexOf(GlowSlotMarker, System.StringComparison.Ordinal) >= 0;
                    bool paperSlot = !glow && paper && mat.name.IndexOf(PaperSlotMarker, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    if ((!glow && !paperSlot) || !mat.HasProperty(GlowEmissionId)) continue;
                    Color baseEmission = mat.GetColor(GlowEmissionId);
                    if (paperSlot && baseEmission.maxColorComponent < 0.01f) baseEmission = PaperGlow;
                    AddGlow(rd, m, index, baseEmission, false);
                }
            }

            SetupMotion(index, root, family, kind, spec);
            return true;
        }

        private void AddGlow(Renderer rd, int slot, int anchor, Color baseEmission, bool shoji)
        {
            EnsureEmissionKeyword(rd, slot);
            _glowRenderer[_glowCount] = rd;
            _glowSlot[_glowCount] = slot;
            _glowAnchor[_glowCount] = anchor;
            _glowBase[_glowCount] = baseEmission;
            _glowPos[_glowCount] = rd.bounds.center;
            _glowShoji[_glowCount] = shoji;
            _glowCount++;
            if (shoji) _shojiCount++;
        }

        /// <summary>URP Lit only emits with _EMISSION on: a slot whose material has it off gets a cached runtime copy with it on (scan only).</summary>
        private void EnsureEmissionKeyword(Renderer rd, int slot)
        {
            Material[] mats = rd.sharedMaterials;
            Material m = slot < mats.Length ? mats[slot] : null;
            if (m == null || m.IsKeywordEnabled(EmissionKeyword)) return;
            if (!_emissiveCopies.TryGetValue(m, out Material copy) || copy == null)
            {
                copy = new Material(m) { name = m.name + " (WP-093 emissive)", hideFlags = HideFlags.DontSave };
                copy.EnableKeyword(EmissionKeyword);
                copy.SetColor(GlowEmissionId, Color.black);
                copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                _emissiveCopies[m] = copy;
            }

            _swappedRenderers.Add(rd);
            _swappedOriginals.Add((Material[])mats.Clone());
            mats[slot] = copy;
            rd.sharedMaterials = mats;
        }

        private void TickGlow(float scale, bool haveCam, Vector3 camera)
        {
            if (_glowCount == 0) return;
            if (_glowBlock == null) _glowBlock = new MaterialPropertyBlock();   // first pass only
            float cull2 = FlickerCullMetres * FlickerCullMetres;
            for (int k = 0; k < _glowCount; k++)
            {
                Renderer r = _glowRenderer[k];
                if (r == null) continue;
                if (haveCam && (_glowPos[k] - camera).sqrMagnitude > cull2) continue;
                // Read-modify-write on that slot only: the other slots (stone, paper, metal) keep their own blocks.
                r.GetPropertyBlock(_glowBlock, _glowSlot[k]);
                _glowBlock.SetColor(GlowEmissionId, GlowEmissionFor(_glowBase[k], _signal[_glowAnchor[k]], scale));
                r.SetPropertyBlock(_glowBlock, _glowSlot[k]);
            }
        }

        private void ClearGlow()
        {
            if (_glowCount > 0 && _glowBlock == null) _glowBlock = new MaterialPropertyBlock();
            for (int k = 0; k < _glowCount; k++)
            {
                Renderer r = _glowRenderer[k];
                if (r == null) continue;
                _glowBlock.Clear();
                r.SetPropertyBlock(_glowBlock, _glowSlot[k]);   // an empty block: the material's own emission again
                _glowRenderer[k] = null;
            }

            // Newest first: a renderer swapped twice (two slots) gets its untouched array back last.
            for (int i = _swappedRenderers.Count - 1; i >= 0; i--) if (_swappedRenderers[i] != null) _swappedRenderers[i].sharedMaterials = _swappedOriginals[i];
            _swappedRenderers.Clear();
            _swappedOriginals.Clear();
            _glowCount = 0;
            _shojiCount = 0;
        }

        // ------------------------------------------------------------------ WP-092 · motion: pendulum (chōchin / tsuri), tōrō (M8)

        public const float PendulumHz = 0.37f;
        public const float PendulumWaveMetres = 6f;
        public const float ToroBobMetres = 0.01f, ToroBobHz = 0.3f, ToroYawDegrees = 5f, ToroDriftRadius = 0.3f, ToroDriftSpeed = 0.03f;
        public const float ToroRingMinSeconds = 3f, ToroRingMaxSeconds = 5f, ToroRingMinDiameter = 0.3f, ToroRingMaxDiameter = 0.8f;

        private readonly Transform[] _mover = new Transform[MaxFlames];
        private readonly byte[] _moveKind = new byte[MaxFlames];   // 0 none, 1 pendulum, 2 tōrō
        private readonly Vector3[] _basePos = new Vector3[MaxFlames];
        private readonly Quaternion[] _baseRot = new Quaternion[MaxFlames];
        private readonly Vector3[] _pivot = new Vector3[MaxFlames];
        private readonly float[] _swingDeg = new float[MaxFlames];
        private readonly float[] _phase = new float[MaxFlames];
        private readonly Vector3[] _motion = new Vector3[MaxFlames];
        private readonly Vector3[] _lightMotion = new Vector3[MaxFlames];
        private readonly float[] _ringNext = new float[MaxFlames];

        public int MotionBlockedByStaticBatch { get; private set; }
        public int Movers { get; private set; }
        public int ToroCount { get; private set; }
        public int ToroRings { get; private set; }
        public Transform MoverOf(int i) => _mover[i];
        public float SwingDegreesOf(int i) => _swingDeg[i];
        public Vector3 BasePositionOf(int i) => _basePos[i];

        private void SetupMotion(int index, Transform root, string family, LampKind kind, MomentSpec spec)
        {
            _mover[index] = null;
            _moveKind[index] = 0;
            _motion[index] = Vector3.zero;
            _lightMotion[index] = Vector3.zero;
            _phase[index] = (index * 0.618034f) % 1f * 6.2831853f;
            _basePos[index] = root.position;
            _baseRot[index] = root.rotation;
            _pivot[index] = root.position;
            _ringNext[index] = 0f;
            bool hanging = kind != LampKind.Toro && IsPaperLantern(family) && root.position.y > 1f;
            if (kind != LampKind.Toro && !hanging) return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);   // scan only
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r].isPartOfStaticBatch)
                {
                    MotionBlockedByStaticBatch++;   // Unity merged its mesh: moving the transform would move only the flame
                    return;
                }
            }

            _mover[index] = root;
            if (kind == LampKind.Toro)
            {
                _moveKind[index] = 2;
            }
            else
            {
                _moveKind[index] = 1;
                Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.position, Vector3.zero);
                for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);
                _pivot[index] = new Vector3(b.center.x, b.max.y, b.center.z);   // the hang point: the top of the lantern
                _swingDeg[index] = spec.SwingDegrees > 0f ? spec.SwingDegrees : WorldMoments.DefaultSwingDegrees;
            }

            Movers++;
        }

        private void TickMotion(float now)
        {
            Vector3 wind = WorldWind.Direction;
            float gust = 0.35f + 0.65f * WorldWind.Strength;
            Vector3 swingAxis = Vector3.Cross(Vector3.up, wind);
            if (swingAxis.sqrMagnitude < 1e-6f) swingAxis = Vector3.forward;
            swingAxis.Normalize();
            for (int i = 0; i < _anchors.Count; i++)
            {
                Transform t = _mover[i];
                if (t == null) continue;
                Vector3 p = _pivot[i], translate;
                Quaternion q;
                if (_moveKind[i] == 1)
                {
                    // Pendulum: ±amp × gust at 0.37 Hz, phase = position along the wind (the gust wave walks downwind).
                    float along = _basePos[i].x * wind.x + _basePos[i].z * wind.z;
                    float deg = _swingDeg[i] * gust * Mathf.Sin(6.2831853f * (PendulumHz * now - along / PendulumWaveMetres));
                    q = Quaternion.AngleAxis(deg, swingAxis);
                    translate = Vector3.zero;
                }
                else
                {
                    // Tōrō: a slow circle round its placement (r 0.3 m ± 25 % ≤ 0.375 < 0.4, at 3 cm/s ≈ 0.1 rad/s), a 1 cm bob at 0.3 Hz,
                    // a ±5° yaw. Circling the placement itself keeps every offset inside S-05's r ≤ 0.4 m.
                    float ph = _phase[i];
                    float w = ToroDriftSpeed / ToroDriftRadius;
                    float r = ToroDriftRadius * (1f + 0.25f * Mathf.Sin(0.17f * now + ph));
                    translate = new Vector3(Mathf.Cos(w * now + ph) * r, ToroBobMetres * Mathf.Sin(6.2831853f * ToroBobHz * now + ph), Mathf.Sin(w * now + ph) * r);
                    q = Quaternion.AngleAxis(ToroYawDegrees * Mathf.Sin(0.45f * now + ph * 1.7f), Vector3.up);
                }

                t.SetPositionAndRotation(p + q * (_basePos[i] - p) + translate, q * _baseRot[i]);
                _motion[i] = p + q * (_anchors[i] - p) + translate - _anchors[i];
                _lightMotion[i] = p + q * (_lightPoints[i] - p) + translate - _lightPoints[i];
            }
        }

        private void TickToroRings(float now)
        {
            AmbientVfxSystem ambient = AmbientVfxSystem.Active;
            if (ambient == null || !ambient.Ready) return;
            for (int i = 0; i < _anchors.Count; i++)
            {
                if (_moveKind[i] != 2 && _kind[i] != LampKind.Toro) continue;
                if (now < _ringNext[i]) continue;
                bool first = _ringNext[i] <= 0f;
                _ringNext[i] = now + Mathf.Lerp(ToroRingMinSeconds, ToroRingMaxSeconds, Next01());
                if (first) continue;   // the first interval starts the clock
                Vector3 at = (_mover[i] != null ? _mover[i].position : _basePos[i]);
                at.y = _groundY[i];
                if (ambient.EmitCausedRing(at, AmbientVfxSystem.RingCause.Toro, Mathf.Lerp(ToroRingMinDiameter, ToroRingMaxDiameter, Next01()), true)) ToroRings++;
            }
        }

        private PrototypePool<FlipbookEffect> _cards;
        private Transform _root;
        // C-00 01.10 GC finding: the scene stamp is read every frame (0 B) and a changed scene is scanned at once, then once more
        // SettleFrames later — both inside a scene's first half second, never on a steady frame. Scan has its own ProfilerMarker.
        public const int SettleFrames = 30;
        private static readonly Unity.Profiling.ProfilerMarker s_scanMarker = new Unity.Profiling.ProfilerMarker("LanternFlames.Scan");
        private int _settleFrame = -1;
        private int _sceneStamp = -1;

        public int Placed => _flames.Count;
        public int Scans { get; private set; }

        /// <summary>Own pool (the shared flipbook pool must not be starved by the looping flames).</summary>
        public void Configure(Transform templateRoot, PrototypePool<FlipbookEffect> unused)
        {
            _root = templateRoot;
            Current = this;
            if (_cards == null)
            {
                var go = new GameObject("FX_AP010B_LanternFlame");
                go.transform.SetParent(templateRoot, false);
                go.SetActive(false);
                _cards = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(go.AddComponent<FlipbookEffect>(), MaxFlames, MaxFlames, PoolExhaustedPolicy.RecycleOldest));
            }

            EnsureHolder();
        }

        // WP-093 1b: the lights and the sparks need an active parent (the template root is inactive by design: pool templates).
        private Transform _holder;

        private void EnsureHolder()
        {
            if (_holder != null) return;
            var go = new GameObject("[Lantern FX (WP-047E lights, WP-093 sparks)]");
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            _holder = go.transform;
            for (int i = 0; i < _lights.Count; i++) if (_lights[i] != null) _lights[i].transform.SetParent(_holder, true);
            CreateSparks();
            CreateHalos();
            CreateStreaks();
        }

        /// <summary>Tests: the holder of the lights and sparks (active in the hierarchy).</summary>
        public Transform Holder => _holder;

        // ------------------------------------------------------------------ WP-047E · point-light budget (AD SHOULD 2, S-05 lighting budget)

        /// <summary>Lights follow the lamps that matter: at most this many at once (URP per-object additional-light limit is 4 — WP-029 asset).</summary>
        public const int MaxLanternLights = 6;
        public const float LanternLightRange = 1.6f;
        public const float LanternLightIntensity = 0.35f;
        public const float LanternLightMaxCameraMetres = 26f;
        public const float LanternLightReassignSeconds = 0.5f;
        /// <summary>Off switch for the budget review (S-05); default on.</summary>
        public static bool PointLightsEnabled = true;

        private readonly List<Light> _lights = new List<Light>(MaxLanternLights);
        private readonly int[] _lightAnchor = new int[MaxLanternLights];
        private readonly UniversalAdditionalLightData[] _lightData = new UniversalAdditionalLightData[MaxLanternLights];
        private readonly uint[] _lightLayers = new uint[MaxLanternLights];
        private readonly float[] _lightKey = new float[MaxLanternLights];
        private float _nextLightPass;

        public int LightsLit
        {
            get { int n = 0; for (int i = 0; i < _lights.Count; i++) if (_lights[i] != null && _lights[i].enabled) n++; return n; }
        }

        private void TickLights(float now)
        {
            if (!PointLightsEnabled || _anchors.Count == 0 || now < _nextLightPass) return;
            _nextLightPass = now + LanternLightReassignSeconds;
            if (!FeedbackCameraRef.TryGetPosition(out Vector3 camera)) return;

            if (_lights.Count == 0)
            {
                for (int i = 0; i < MaxLanternLights; i++)
                {
                    var go = new GameObject("LanternLight");
                    go.transform.SetParent(_holder != null ? _holder : _root, false);
                    Light l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.range = LanternLightRange;
                    l.intensity = LanternLightIntensity;
                    l.color = new Color(FlameTint.r, FlameTint.g, FlameTint.b, 1f);
                    l.shadows = LightShadows.None;
                    l.enabled = false;
                    _lightData[i] = go.AddComponent<UniversalAdditionalLightData>();
                    _lightLayers[i] = 0u;   // forces the first ApplyLight to write the mask
                    _lights.Add(l);
                    _lightAnchor[i] = -1;
                }
            }

            // WP-092: the pool lamps first (key − 1e6), then the lamps with a light nearest the PLAYER (S-05: AdditionalLightsPerObjectLimit
            // 4), all within reach of the camera; emissive / unlit / tōrō never. Insertion sort into the slots (no allocation).
            Transform player = EnemyTargeting.Player;
            Vector3 focus = player != null ? player.position : camera;
            float max2 = LanternLightMaxCameraMetres * LanternLightMaxCameraMetres;
            for (int i = 0; i < MaxLanternLights; i++) { _lightAnchor[i] = -1; _lightKey[i] = float.MaxValue; }
            for (int a = 0; a < _anchors.Count; a++)
            {
                if (_lightMode[a] == 0) continue;
                if ((_lightPoints[a] - camera).sqrMagnitude > max2) continue;
                float d = (_lightPoints[a] - focus).sqrMagnitude + (_priority[a] ? -1e6f : 0f);
                for (int slot = 0; slot < MaxLanternLights; slot++)
                {
                    if (_lightAnchor[slot] >= 0 && _lightKey[slot] <= d) continue;
                    for (int k = MaxLanternLights - 1; k > slot; k--) { _lightAnchor[k] = _lightAnchor[k - 1]; _lightKey[k] = _lightKey[k - 1]; }
                    _lightAnchor[slot] = a;
                    _lightKey[slot] = d;
                    break;
                }
            }

            for (int i = 0; i < MaxLanternLights; i++)
            {
                Light l = _lights[i];
                if (l == null) continue;
                int a = _lightAnchor[i];
                if (a < 0) { l.enabled = false; continue; }
                ApplyLight(i, l, a);
                l.enabled = true;
            }
        }

        /// <summary>Light slot <paramref name="slot"/>'s lantern (anchor = flame index), −1 when the slot is dark.</summary>
        public int LightAnchorOf(int slot) => slot >= 0 && slot < MaxLanternLights && slot < _lights.Count && _lights[slot] != null && _lights[slot].enabled ? _lightAnchor[slot] : -1;

        /// <summary>Light slot <paramref name="slot"/> (null before the first light pass).</summary>
        public Light LightOf(int slot) => slot >= 0 && slot < _lights.Count ? _lights[slot] : null;

        /// <summary>Flame <paramref name="i"/>'s card (null when the pool ran out).</summary>
        public FlipbookEffect FlameOf(int i) => i >= 0 && i < _flames.Count ? _flames[i] : null;

        /// <summary>The flicker signal flame <paramref name="i"/> was last driven with (card and light alike).</summary>
        public float SignalOf(int i) => i >= 0 && i < MaxFlames ? _signal[i] : 0f;

        // The light follows its flame: the same signal, the same sway (a little less: the light sits in the firebox), the same motion.
        private void ApplyLight(int slot, Light l, int a)
        {
            l.transform.position = _lightPoints[a] + _lightMotion[a] + _sway[a] * 0.6f;
            l.intensity = LightIntensityFor(_signal[a], FlashScaleNow);
            // Baked lamps: S-05's baked point light already lights the room — the realtime one reaches characters only.
            uint layers = _lightMode[a] == 1 ? CharactersOnlyRenderingLayers : FullRenderingLayers;
            if (_lightLayers[slot] != layers)
            {
                _lightLayers[slot] = layers;
                l.renderingLayerMask = unchecked((int)layers);
                if (_lightData[slot] != null) _lightData[slot].renderingLayers = layers;
            }
        }

        public const float FlickerHz = 20f;
        /// <summary>Card alpha swing (WP-047E AD SHOULD): the cap moves between 1 − this and 1.</summary>
        public const float FlickerAmplitude = 0.10f;
        /// <summary>LOOK-BIBLE §5: light intensity 1.0 ± this (× <see cref="LanternLightIntensity"/>).</summary>
        public const float LightFlickerAmplitude = 0.12f;
        /// <summary>LOOK-BIBLE §5: the flame card's scale ± this, from the same sample.</summary>
        public const float FlameScaleAmplitude = 0.08f;
        /// <summary>The two noise octaves (§5: 1.3 Hz × 0.7 + 6.5 Hz × 0.3) and the ceiling nothing may cross (> 8 Hz: none).</summary>
        public const float FlickerSlowHz = 1.3f, FlickerFastHz = 6.5f, FlickerMaxHz = 8f;
        public const float FlickerSlowWeight = 0.7f, FlickerFastWeight = 0.3f;
        public const float SwayMetres = 0.025f;
        public const float SwayHz = 0.35f;
        private float _nextFlicker;
        private readonly float[] _signal = new float[MaxFlames];
        private readonly Vector3[] _sway = new Vector3[MaxFlames];

        /// <summary>
        /// The one flicker sample of flame <paramref name="index"/> at <paramref name="time"/>: 0.7 · n(1.3 Hz) + 0.3 · n(6.5 Hz), n a
        /// smooth (cubic-eased) 1-D value noise in [−1, 1] with its own lattice per flame and octave.
        /// </summary>
        public static float FlickerSignal(int index, float time)
        {
            return FlickerSlowWeight * ValueNoise(time * FlickerSlowHz, index * 2)
                 + FlickerFastWeight * ValueNoise(time * FlickerFastHz, index * 2 + 1);
        }

        private static float ValueNoise(float x, int lattice)
        {
            float i = Mathf.Floor(x);
            float f = x - i;
            float u = f * f * (3f - 2f * f);
            int k = (int)i;
            return Mathf.Lerp(Hash01(k, lattice), Hash01(k + 1, lattice), u) * 2f - 1f;
        }

        private static float Hash01(int k, int lattice)
        {
            unchecked
            {
                uint h = (uint)k * 0x9E3779B1u ^ (uint)(lattice + 1) * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Card alpha cap for signal <paramref name="s"/>: 1 at s = 1, 1 − <see cref="FlickerAmplitude"/> × scale at s = −1.</summary>
        public static float CardAlphaFor(float s, float flashScale) => 1f - FlickerAmplitude * Mathf.Clamp01(flashScale) * (0.5f - 0.5f * s);

        /// <summary>Light intensity for signal <paramref name="s"/>: the base × (1 ± <see cref="LightFlickerAmplitude"/> × scale), rising with the card.</summary>
        public static float LightIntensityFor(float s, float flashScale) => LanternLightIntensity * (1f + LightFlickerAmplitude * Mathf.Clamp01(flashScale) * s);

        /// <summary>Flame card scale for signal <paramref name="s"/>: 1 ± <see cref="FlameScaleAmplitude"/> × scale (§5: the same sample as the light).</summary>
        public static float FlameScaleFor(float s, float flashScale) => 1f + FlameScaleAmplitude * Mathf.Clamp01(flashScale) * s;

        private static float FlashScaleNow => Mathf.Clamp01(GameFeelSettings.FlashScale);

        /// <summary>One pass drives motion, every flame card AND its light, halo, glow and streak from the same signal.</summary>
        private void TickFlicker(float now)
        {
            if (_flames.Count == 0 || now < _nextFlicker) return;
            _nextFlicker = now + 1f / FlickerHz;
            float scale = FlashScaleNow;
            bool haveCam = FeedbackCameraRef.TryGetPosition(out Vector3 camera);
            float cull2 = FlickerCullMetres * FlickerCullMetres;
            TickMotion(now);
            Vector3 wind = WorldWind.Direction;
            var cross = new Vector3(-wind.z, 0f, wind.x);
            float gust = 0.35f + 0.65f * WorldWind.Strength;
            for (int i = 0; i < _flames.Count && i < MaxFlames; i++)
            {
                float s = FlickerSignal(i, now);
                _signal[i] = s;
                float along = SwayMetres * gust * (0.5f + 0.5f * Mathf.Sin(6.2831853f * SwayHz * now + i * 1.3f));
                float side = SwayMetres * 0.4f * Mathf.Sin(6.2831853f * SwayHz * 1.5f * now + i * 2.9f);
                _sway[i] = wind * along + cross * side;
                FlipbookEffect f = _flames[i];
                if (f == null || !f.IsSpawned) continue;
                float d2 = haveCam ? (_anchors[i] - camera).sqrMagnitude : 0f;
                bool hidden = haveCam && CardCullMetres > 0f && d2 > CardCullMetres * CardCullMetres;
                f.SetHidden(hidden);
                if (hidden) continue;
                if (haveCam && _mover[i] == null && d2 > cull2) continue;   // far and still: last state holds
                Transform ft = f.transform;
                ft.position = _anchors[i] + _motion[i] + _sway[i];
                ft.localScale = Vector3.one * (FlameScaleFor(s, scale) * _scale[i]);
                f.SetAlphaCap(CardAlphaFor(s, scale));
            }

            for (int k = 0; k < _lights.Count; k++)
            {
                Light l = _lights[k];
                int a = _lightAnchor[k];
                if (l != null && l.enabled && a >= 0 && a < _anchors.Count) ApplyLight(k, l, a);
            }

            TickHalos(scale);
            TickStreaks(scale);
            TickGlow(scale, haveCam, camera);
            TickToroRings(now);
        }

        // ------------------------------------------------------------------ WP-093 1b · halo (LOOK-BIBLE §5 "1 alev + 1 hale")

        public const float HaloDiameter = 0.7f;
        public const float HaloAlpha = 0.12f;
        /// <summary>§5 halo #E8A870 (alpha-blended at 0.12: it warms the stone, it cannot reach the beacon reserve).</summary>
        public static readonly Color HaloTint = new Color(232f / 255f, 168f / 255f, 112f / 255f, 1f);
        private ParticleSystem _halos;
        private Material _haloMaterial;
        private readonly ParticleSystem.Particle[] _haloBuffer = new ParticleSystem.Particle[MaxFlames];
        private ParticleSystem.EmitParams _haloEp;

        public int HalosLive => _halos != null ? _halos.particleCount : 0;
        public ParticleSystem HaloSystem => _halos;

        private void TickHalos(float scale)
        {
            if (_halos == null || _halos.particleCount == 0) return;
            bool haveCam = FeedbackCameraRef.TryGetPosition(out Vector3 camera);
            Transform player = EnemyTargeting.Player;
            bool guard = haveCam && player != null && DD.Prototype.CameraSystem.CameraModeService.Current != DD.Prototype.CameraSystem.CameraMode.FirstPerson;
            Vector3 chest = player != null ? player.position + Vector3.up * 1.1f : Vector3.zero;
            int n = _halos.GetParticles(_haloBuffer);
            for (int k = 0; k < n; k++)
            {
                int i = (int)_haloBuffer[k].randomSeed - 1;
                if (i < 0 || i >= _anchors.Count) continue;
                float a = _haloAlpha[i] * (1f + LightFlickerAmplitude * scale * _signal[i]);
                Vector3 at = _anchors[i] + _motion[i] + _sway[i] * 0.6f;
                // R9: the halo adds ≤ 0.03 luma over a character — × 0.2 when it sits on the camera → player line.
                if (guard && GelSystem.GuardFactor(camera, chest, at, HaloDiameter * 0.5f) < 1f) a *= 0.2f;
                Color32 c = HaloTint;
                c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                _haloBuffer[k].startColor = c;
                _haloBuffer[k].position = at;
                _haloBuffer[k].remainingLifetime = LoopSeconds;   // a session longer than the loop keeps its halos
            }

            _halos.SetParticles(_haloBuffer, n);
        }

        private ParticleSystem NewHolderSystem(string name, int maxParticles, bool size3D, ParticleSystemRenderMode mode, float fudge)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(_holder, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = LoopSeconds;
            main.startSize3D = size3D;
            main.maxParticles = maxParticles;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = mode;
            psr.sortMode = ParticleSystemSortMode.None;
            psr.sortingFudge = fudge;
            psr.maxParticleSize = 2f;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
            psr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            psr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            psr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return ps;
        }

        private void CreateHalos()
        {
            if (_halos != null || !PrototypeShaders.IsReady) return;
            Shader shader = PrototypeShaders.VfxFlipbook;
            if (shader == null || !shader.isSupported) return;
            ParticleSystem ps = NewHolderSystem("LanternHalos", MaxFlames, false, ParticleSystemRenderMode.Billboard, 5f);   // behind the flame card
            _haloMaterial = new Material(shader) { name = "DD_VfxFlipbook_LanternHalo (WP-093, runtime)", hideFlags = HideFlags.DontSave };
            AmbientVfxTextures.SetupFlipbook(_haloMaterial, AmbientVfxTextures.Glow, false, HaloAlpha * (1f + LightFlickerAmplitude));
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = _haloMaterial;
            ps.gameObject.SetActive(true);
            _halos = ps;
        }

        private void PlaceHalos()
        {
            if (_halos == null) return;
            _halos.Clear();
            for (int i = 0; i < _flames.Count && i < MaxFlames; i++)
            {
                if (!_haloOn[i]) continue;
                Color c = HaloTint;
                c.a = _haloAlpha[i];
                _haloEp.position = _anchors[i];
                _haloEp.velocity = Vector3.zero;
                _haloEp.startLifetime = LoopSeconds;
                _haloEp.startSize = HaloDiameter * _scale[i];
                _haloEp.startColor = c;
                _haloEp.randomSeed = (uint)(i + 1);   // the flame index rides in the seed (TickHalos maps it back)
                _halos.Emit(_haloEp, 1);
            }
        }

        // ------------------------------------------------------------------ WP-092 · reflection streaks (M4 axis lanterns, M8 tōrō)

        /// <summary>M4: "per axis flame a −z streak 2.0 × 0.12 m α 0.4 synced to LanternFlames".</summary>
        public const float AxisStreakLength = 2.0f, AxisStreakWidth = 0.12f, AxisStreakAlpha = 0.4f;
        /// <summary>M8: "0.15 × 0.6 m fake streak per tōrō" (URP 14 has no SSR; the pond has no planar reflection).</summary>
        public const float ToroStreakLength = 0.6f, ToroStreakWidth = 0.15f, ToroStreakAlpha = 0.3f;
        private ParticleSystem _streaks;
        private Material _streakMaterial;
        private readonly ParticleSystem.Particle[] _streakBuffer = new ParticleSystem.Particle[MaxFlames];
        private readonly byte[] _streakKind = new byte[MaxFlames];   // 0 none, 1 axis, 2 tōrō
        private ParticleSystem.EmitParams _streakEp;

        public int StreaksLive => _streaks != null ? _streaks.particleCount : 0;
        public ParticleSystem StreakSystem => _streaks;
        public int StreakKindOf(int i) => _streakKind[i];

        private void CreateStreaks()
        {
            if (_streaks != null || !PrototypeShaders.IsReady) return;
            Shader shader = PrototypeShaders.VfxFlipbook;
            if (shader == null || !shader.isSupported) return;
            ParticleSystem ps = NewHolderSystem("LanternStreaks", MaxFlames, true, ParticleSystemRenderMode.HorizontalBillboard, 0f);
            _streakMaterial = new Material(shader) { name = "DD_VfxFlipbook_LanternStreak (WP-093, runtime)", hideFlags = HideFlags.DontSave };
            AmbientVfxTextures.SetupFlipbook(_streakMaterial, AmbientVfxTextures.Glow, true, 1f);   // additive: a warm sheen on the wet stone / water
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = _streakMaterial;
            ps.gameObject.SetActive(true);
            _streaks = ps;
        }

        /// <summary>The streak's centre: on the surface under the flame, toward the camera (−z: the TD camera looks north).</summary>
        private Vector3 StreakCentre(int i)
        {
            float len = _streakKind[i] == 1 ? AxisStreakLength : ToroStreakLength * _scale[i] / 0.6f;
            Vector3 f = _anchors[i] + _motion[i];
            return new Vector3(f.x, _groundY[i] + _motion[i].y + 0.012f, f.z - len * 0.5f - 0.05f);
        }

        private void PlaceStreaks()
        {
            if (_streaks == null) return;
            _streaks.Clear();
            for (int i = 0; i < _flames.Count && i < MaxFlames; i++)
            {
                _streakKind[i] = 0;
                if (_kind[i] == LampKind.Toro) _streakKind[i] = 2;
                else if (_kind[i] == LampKind.Legacy && _moment[i] >= 0 && WorldMoments.Current[_moment[i]].AxisStreaks) _streakKind[i] = 1;
                if (_streakKind[i] == 0) continue;
                bool axis = _streakKind[i] == 1;
                Color c = FlameTint;
                c.a = axis ? AxisStreakAlpha : ToroStreakAlpha;
                _streakEp.position = StreakCentre(i);
                _streakEp.velocity = Vector3.zero;
                _streakEp.startLifetime = LoopSeconds;
                _streakEp.startSize3D = axis ? new Vector3(AxisStreakWidth, AxisStreakLength, 1f) : new Vector3(ToroStreakWidth, ToroStreakLength, 1f) * (_scale[i] / 0.6f);
                _streakEp.rotation = 0f;
                _streakEp.startColor = c;
                _streakEp.randomSeed = (uint)(i + 1);
                _streaks.Emit(_streakEp, 1);
            }
        }

        private void TickStreaks(float scale)
        {
            if (_streaks == null || _streaks.particleCount == 0) return;
            int n = _streaks.GetParticles(_streakBuffer);
            for (int k = 0; k < n; k++)
            {
                int i = (int)_streakBuffer[k].randomSeed - 1;
                if (i < 0 || i >= _anchors.Count) continue;
                float a = (_streakKind[i] == 1 ? AxisStreakAlpha : ToroStreakAlpha) * (1f + LightFlickerAmplitude * scale * _signal[i]);
                Color32 c = FlameTint;
                c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                _streakBuffer[k].startColor = c;
                _streakBuffer[k].position = StreakCentre(i);
                _streakBuffer[k].remainingLifetime = LoopSeconds;
            }

            _streaks.SetParticles(_streakBuffer, n);
        }

        // ------------------------------------------------------------------ WP-093 1b · warm sparks

        public const int MaxSparksPerLantern = 3;
        public const float SparkMaxCameraMetres = 24f;
        /// <summary>Sparks per second per lantern at a full flare (s = 1); a third of it at s ≤ 0 (with a 2.5–4 s life: ≈ 0–2 live each).</summary>
        public const float SparkRatePerLantern = 0.45f;
        /// <summary>LOOK-BIBLE §5: 1.0–1.5 cm (the brief's 2–4 cm superseded); the renderer keeps them ≥ ~1.6 px.</summary>
        public const float SparkSizeMin = 0.010f, SparkSizeMax = 0.015f;
        public const float SparkLifeMin = 2.5f, SparkLifeMax = 4f;
        public const float SparkRiseMin = 0.08f, SparkRiseMax = 0.15f;
        /// <summary>Additive (rgb × α) and dim: α 0.25 is the §5 cap for anything in the 0.3–2.0 m corridor band (no bloom: threshold 1.25).</summary>
        public const float SparkAlpha = 0.25f;
        /// <summary>§5 #E8B48C (h 26°: off the gold band and the beacon reserve).</summary>
        public static readonly Color SparkTint = new Color(232f / 255f, 180f / 255f, 140f / 255f, 1f);

        /// <summary>Live spark ceiling per tier: none on Performance, 16 on High, 30 on Cinematic.</summary>
        public static int SparkCap(VfxTierLevel tier) => tier == VfxTierLevel.Cinematic ? 30 : tier == VfxTierLevel.High ? 16 : 0;

        private ParticleSystem _sparks;
        private Material _sparkMaterial;
        private readonly float[] _sparkDeath = new float[MaxFlames * MaxSparksPerLantern];
        private ParticleSystem.EmitParams _sparkEp;
        private int _sparkCapApplied = -1;
        private float _lastTick = -1f;
        private uint _rng = 0x6C8E9CF5u;

        public int SparksLive => _sparks != null ? _sparks.particleCount : 0;
        public int SparksEmitted { get; private set; }
        public ParticleSystem SparkSystem => _sparks;

        /// <summary>Sparks of lantern <paramref name="i"/> still alive at <paramref name="now"/> (≤ <see cref="MaxSparksPerLantern"/>).</summary>
        public int SparksOf(int i, float now)
        {
            int n = 0;
            for (int k = 0; k < MaxSparksPerLantern; k++) if (_sparkDeath[i * MaxSparksPerLantern + k] > now) n++;
            return n;
        }

        private void CreateSparks()
        {
            if (_sparks != null || !PrototypeShaders.IsReady) return;
            Shader shader = PrototypeShaders.VfxFlipbook;
            if (shader == null || !shader.isSupported) return;

            ParticleSystem ps = NewHolderSystem("LanternSparks", 30, false, ParticleSystemRenderMode.Billboard, 0f);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = SparkLifeMax;

            ParticleSystem.NoiseModule noise = ps.noise;   // §5 "lazy curl" as it rises
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.06f);
            noise.frequency = 0.8f;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
            ParticleSystem.ExternalForcesModule forces = ps.externalForces;
            forces.enabled = true;
            forces.multiplier = 0.3f;

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.95f, 0.78f, 0.68f), 1f) },   // cools as it dies
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.5f));

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.minParticleSize = 0.0015f;   // ≥ ~1.6 px at 1080p: a 1–1.5 cm spark at the 20 m camera (≈ 0.8 px) would otherwise shimmer sub-pixel
            psr.maxParticleSize = 0.02f;
            _sparkMaterial = new Material(shader) { name = "DD_VfxFlipbook_LanternSparks (WP-093, runtime)", hideFlags = HideFlags.DontSave };
            AmbientVfxTextures.SetupFlipbook(_sparkMaterial, AmbientVfxTextures.Dot, true, 1f);
            psr.sharedMaterial = _sparkMaterial;
            ps.gameObject.SetActive(true);
            _sparks = ps;
        }

        private void TickSparks(float now)
        {
            float dt = _lastTick < 0f ? 0f : Mathf.Clamp(now - _lastTick, 0f, 0.1f);
            _lastTick = now;
            if (_sparks == null) return;
            int cap = SparkCap(VfxTier.Current);
            if (cap != _sparkCapApplied)
            {
                _sparkCapApplied = cap;
                ParticleSystem.MainModule main = _sparks.main;
                main.maxParticles = Mathf.Max(1, cap);
                if (_sparks.particleCount > cap) _sparks.Clear();
            }

            if (cap == 0 || dt <= 0f || _flames.Count == 0 || !FeedbackCameraRef.TryGetPosition(out Vector3 camera)) return;

            Transform player = EnemyTargeting.Player;
            bool guard = player != null && DD.Prototype.CameraSystem.CameraModeService.Current != DD.Prototype.CameraSystem.CameraMode.FirstPerson;
            Vector3 chest = player != null ? player.position + Vector3.up * 1.1f : Vector3.zero;
            Vector3 wind = WorldWind.Direction * WorldWind.Strength;
            float max2 = SparkMaxCameraMetres * SparkMaxCameraMetres;
            float alpha = SparkAlpha * Mathf.Lerp(0.6f, 1f, FlashScaleNow);   // reduce flashes: dimmer still
            for (int i = 0; i < _flames.Count && i < MaxFlames; i++)
            {
                if (_sparks.particleCount >= cap) return;
                int allowed = _maxSparks[i];
                if (allowed == 0) continue;
                FlipbookEffect f = _flames[i];
                if (f == null || !f.IsSpawned || (_anchors[i] - camera).sqrMagnitude > max2) continue;
                float flare = Mathf.Max(0f, _signal[i]);
                if (Next01() >= SparkRatePerLantern * dt * (0.35f + 0.65f * flare)) continue;
                int slot = -1;
                for (int k = 0; k < allowed && slot < 0; k++) if (_sparkDeath[i * MaxSparksPerLantern + k] <= now) slot = i * MaxSparksPerLantern + k;
                if (slot < 0) continue;   // this lamp's sparks are all rising

                Vector3 pos = _anchors[i] + _motion[i] + _sway[i] + new Vector3(Signed() * 0.03f, 0.12f, Signed() * 0.03f);
                // Never on the camera → player line, never at the body (the GelSystem guard, a spark's reach ≈ 0.5 m of rise).
                if (guard && (GelSystem.GuardFactor(camera, chest, pos, 0.5f) < 1f || (pos - chest).sqrMagnitude < 1f)) continue;
                float life = Mathf.Lerp(SparkLifeMin, SparkLifeMax, Next01());
                Color c = SparkTint;
                c.a = alpha;
                _sparkEp.position = pos;
                _sparkEp.velocity = new Vector3(Signed() * 0.02f, Mathf.Lerp(SparkRiseMin, SparkRiseMax, Next01()), Signed() * 0.02f) + wind * 0.06f;
                _sparkEp.startLifetime = life;
                _sparkEp.startSize = Mathf.Lerp(SparkSizeMin, SparkSizeMax, Next01());
                _sparkEp.startColor = c;
                _sparkEp.randomSeed = NextUInt();
                _sparks.Emit(_sparkEp, 1);
                _sparkDeath[slot] = now + life;
                SparksEmitted++;
            }
        }

        private uint NextUInt()
        {
            uint x = _rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rng = x;
            return x;
        }

        private float Next01() => (NextUInt() & 0xFFFFFF) / 16777216f;
        private float Signed() => Next01() * 2f - 1f;

        public void Tick(float now)
        {
            TickFlicker(now);
            TickLights(now);
            TickSparks(now);
            if (_cards == null)
            {
                return;
            }

            // One scan per scene + one settle scan (the FindObjectsOfType arrays allocate — never on a steady frame, ADR-037-1);
            // a scene with no lanterns is remembered as such.
            int stamp = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle + UnityEngine.SceneManagement.SceneManager.sceneCount * 7919;
            if (stamp != _sceneStamp)
            {
                _sceneStamp = stamp;
                _settleFrame = Time.frameCount + SettleFrames;
                using (s_scanMarker.Auto()) Scan();
                return;
            }

            if (_settleFrame >= 0 && Time.frameCount >= _settleFrame)
            {
                _settleFrame = -1;
                using (s_scanMarker.Auto()) Scan();
            }
        }

        private bool InScope(Transform t) => ScanRoot == null || (t != null && t.IsChildOf(ScanRoot));

        private Transform FindLayoutRoot()
        {
            if (ScanRoot != null) return ScanRoot.name == WorldMoments.LayoutRootName ? ScanRoot : FindChildDeep(ScanRoot, WorldMoments.LayoutRootName);
            GameObject go = GameObject.Find(WorldMoments.LayoutRootName);
            return go != null ? go.transform : null;
        }

        // Contract lamps (Lamp_*) anywhere under the layout root; the M8_toro group's children are the tōrō. Scan only.
        private void ScanLayout(Transform t, HashSet<int> roots)
        {
            for (int i = 0; i < t.childCount && _anchors.Count < MaxFlames; i++)
            {
                Transform c = t.GetChild(i);
                if (!c.gameObject.activeInHierarchy) continue;
                if (ParseLampName(c.name, out string id, out LampKind kind))
                {
                    roots.Add(c.GetInstanceID());
                    if (kind != LampKind.Unlit) AddLamp(c, FamilyOf(c), BodyOf(c), kind, id);
                    continue;   // a lamp's children are its own parts
                }

                if (c.name == WorldMoments.ToroGroupName)
                {
                    for (int k = 0; k < c.childCount && _anchors.Count < MaxFlames; k++)
                    {
                        Transform toro = c.GetChild(k);
                        if (!toro.gameObject.activeInHierarchy) continue;
                        roots.Add(toro.GetInstanceID());
                        if (AddLamp(toro, FamilyOf(toro), BodyOf(toro), LampKind.Toro, toro.name)) ToroCount++;
                    }

                    continue;
                }

                ScanLayout(c, roots);
            }
        }

        /// <summary>Find the lamps and light them. Allocates (FindObjectsOfType) — once per world load.</summary>
        public int Scan()
        {
            Scans++;
            Clear();
            if (!VfxLibrary.TryGetPlayable(VfxIds.LanternFlame, out VfxCatalogEntry row))
            {
                return 0;
            }

            // WP-092: the layout lamps by name under the S-05 builder root (its presence makes the moments live).
            var roots = new HashSet<int>();
            Transform layout = FindLayoutRoot();
            if (ScanRoot == null) WorldMoments.LayoutPresent = layout != null;
            if (layout != null) ScanLayout(layout, roots);

            // AP005 world instances are renamed AP005_iNNNN (WP-014); the family survives on the SurfaceTag that Ap005SurfaceTags writes
            // on every instance root (Family, WP-047). Library / pilot scenes keep prefab names. Layout lamps were taken above.
            SurfaceTag[] tags = Object.FindObjectsOfType<SurfaceTag>();
            for (int i = 0; i < tags.Length && _anchors.Count < MaxFlames; i++)
            {
                SurfaceTag tag = tags[i];
                if (tag == null || !tag.gameObject.activeInHierarchy || !InScope(tag.transform)) continue;
                if (layout != null && tag.transform.IsChildOf(layout)) continue;
                string family = IsLanternFamily(tag.Family) ? tag.Family : NamedLantern(tag.transform) ? tag.transform.name : null;
                if (family == null || !roots.Add(tag.transform.GetInstanceID())) continue;
                AddLamp(tag.transform, family, tag.GetComponentInChildren<MeshRenderer>(), LampKind.Legacy, tag.transform.name);
            }

            if (_anchors.Count == 0)
            {
                // No tags (pilot / library scene): fall back to prefab names on the renderer hierarchy. A root with sockets is one lamp
                // (its LOD renderers would otherwise each add one); a socketless one keeps the per-LOD0-renderer rule.
                MeshRenderer[] renderers = Object.FindObjectsOfType<MeshRenderer>();
                for (int i = 0; i < renderers.Length && _anchors.Count < MaxFlames; i++)
                {
                    MeshRenderer r = renderers[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || !InScope(r.transform)) continue;
                    Transform t = r.transform;
                    Transform owner = NamedLantern(t) ? t : NamedLantern(t.parent) ? t.parent : t.parent != null && NamedLantern(t.parent.parent) ? t.parent.parent : null;
                    if (owner == null) continue;
                    if (FindChildDeep(owner, FlameSocketName) != null)
                    {
                        if (roots.Add(owner.GetInstanceID())) AddLamp(owner, owner.name, r, LampKind.Legacy, owner.name);
                        continue;
                    }

                    if (r.name.IndexOf("LOD", System.StringComparison.OrdinalIgnoreCase) >= 0 && r.name.IndexOf("LOD0", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    AddLamp(r.transform, owner.name, r, LampKind.Legacy, owner.name);
                }
            }

            for (int i = 0; i < _anchors.Count; i++)
            {
                FlipbookEffect fx = _cards.Get(_anchors[i], Quaternion.identity);
                if (fx == null) break;
                fx.Play(row, FlipbookEffect.Facing.CameraYaw, row.WorldSizeMetres * FlameScale, FlameTint, 1f, 0f, LoopSeconds);
                fx.SkipWhenInvisible = true;   // FINAL 10.10 perf: an off-screen flame skips its frame / facing update
                _flames.Add(fx);
            }

            ScanShoji(roots);
            PlaceHalos();
            PlaceStreaks();
            return _flames.Count;
        }

        /// <summary>C · the shoji of the lit interiors (moments with Shoji: R1 south, R4): washi / lantern paper not on a lamp, ≤ 5 m from one.</summary>
        private void ScanShoji(HashSet<int> lampRoots)
        {
            if (!WorldMoments.Live || _anchors.Count == 0) return;
            Renderer[] all = Object.FindObjectsOfType<Renderer>();
            float reach2 = ShojiLampReach * ShojiLampReach;
            for (int i = 0; i < all.Length && _shojiCount < MaxShojiSlots; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || r is ParticleSystemRenderer || !InScope(r.transform)) continue;
                Vector3 c = r.bounds.center;
                int moment = WorldMoments.Find(c);
                if (moment < 0 || !WorldMoments.Current[moment].Shoji || UnderLamp(r.transform, lampRoots)) continue;
                Material[] mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length && _shojiCount < MaxShojiSlots; m++)
                {
                    Material mat = mats[m];
                    if (mat == null || !mat.HasProperty(GlowEmissionId)) continue;
                    if (mat.name.IndexOf(WashiSlotMarker, System.StringComparison.OrdinalIgnoreCase) < 0 && mat.name.IndexOf(PaperSlotMarker, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int best = -1;
                    float bestD = reach2;
                    for (int a = 0; a < _anchors.Count; a++)
                    {
                        if (_kind[a] == LampKind.Toro) continue;
                        float d = (_anchors[a] - c).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = a; }
                    }

                    if (best >= 0) AddGlow(r, m, best, ShojiEmission, true);
                }
            }
        }

        private static bool UnderLamp(Transform t, HashSet<int> lampRoots)
        {
            for (int depth = 0; depth < 8 && t != null; depth++, t = t.parent) if (lampRoots.Contains(t.GetInstanceID())) return true;
            return false;
        }

        private static bool NamedLantern(Transform t) => t != null && IsLanternFamily(t.name);

        public void Clear()
        {
            for (int i = 0; i < _flames.Count; i++)
            {
                if (_flames[i] == null) continue;
                _flames[i].transform.localScale = Vector3.one;   // the ±8 % flicker scale does not travel with the pooled card
                if (_flames[i].IsSpawned) _flames[i].Despawn();
            }

            // Movers back to rest (a rescan of the same world must not inherit a swing).
            for (int i = 0; i < _anchors.Count && i < MaxFlames; i++)
            {
                if (_mover[i] != null) _mover[i].SetPositionAndRotation(_basePos[i], _baseRot[i]);
                _mover[i] = null;
                _moveKind[i] = 0;
                _motion[i] = Vector3.zero;
                _lightMotion[i] = Vector3.zero;
                _streakKind[i] = 0;
            }

            _flames.Clear();
            _anchors.Clear();
            _lightPoints.Clear();
            System.Array.Clear(_baked, 0, _baked.Length);
            System.Array.Clear(_lightMode, 0, _lightMode.Length);
            System.Array.Clear(_priority, 0, _priority.Length);
            System.Array.Clear(_haloOn, 0, _haloOn.Length);
            ClearGlow();
            Movers = 0;
            MotionBlockedByStaticBatch = 0;
            ToroCount = 0;
            for (int i = 0; i < _lights.Count; i++) if (_lights[i] != null) _lights[i].enabled = false;
            _nextLightPass = float.NegativeInfinity;   // a rescan reassigns the lights on its first tick (not after the old world's timer)
            if (_sparks != null) _sparks.Clear();
            if (_halos != null) _halos.Clear();
            if (_streaks != null) _streaks.Clear();
            System.Array.Clear(_sparkDeath, 0, _sparkDeath.Length);
            _nextFlicker = 0f;   // the next tick drives the new flames at once
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }
    }
}
