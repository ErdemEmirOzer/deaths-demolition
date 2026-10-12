using System;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rendering;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Weapons.Attachments
{
    /// <summary>
    /// WP-104 B4-2 · B12 on the weapon attachments (S-06, C-00 12.10 (a); S-07 keeps the E1–E5 glow ladder in its B4 kit) ·
    /// AD-VISUAL §1.2 (b) / §2 / §3:
    /// - element motes at E4's SOCKET_ElementVFX from Element tier III (embers rise, frost drifts, sparks snap, venom drips);
    /// - the twin barrel's own muzzle flash (E3 SecondaryMuzzle) with every classic shot of this weapon — "two flames";
    /// - the element edge on the muzzle flash: a thin ring of the dominant element's glow around the flash origin (E5's PowerMuzzle
    ///   when mounted, else the rig muzzle), never more than a few percent of the flash (AD: the flame itself is never tinted, the
    ///   element adds an outer edge ≤ 30 % of its area).
    /// Brightness follows S-07's TreeVfxLadder.AddOnEmissive (AD M9: element III 1.4 · IV 1.6 · V 2.0; below III the add-on ladder
    /// .3 / .6) × GameFeelSettings.FlashScale, held under the 1.25 bloom threshold under Reduce Flashes, which also halves the motes.
    /// Never writes M_AP040_Glow or any add-on renderer (S-07's slot-1 property blocks). The add-ons show in TopDown only; hidden,
    /// the motes are inactive with them and no flash or edge is reported. Nothing runs per frame but a flash-setting comparison.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponAttachB12 : MonoBehaviour
    {
        /// <summary>The motes start where the capsule starts to glow (AD-VISUAL §2: "III — E4 ışır").</summary>
        public const int MotesFromTier = 3;
        /// <summary>The twin barrel's flash against the main one (WeaponController's 0.28 m card × the shot's power scale).</summary>
        public const float TwinFlashShare = 0.75f;
        /// <summary>URP's bloom threshold is 1.25: under Reduce Flashes nothing here glows past this.</summary>
        public const float ReducedFlashCap = 1.2f;
        /// <summary>The edge ring: dots on a circle of half the flash card, a few percent of its area.</summary>
        public const int EdgeDots = 8;
        private const float MuzzleFlashSize = 0.28f;

        /// <summary>S-07 TreeVfxLadder.AddOnEmissive for the element (AD M9: III 1.4 · IV 1.6 · V 2.0; the add-on ladder below III).</summary>
        private static readonly float[] s_elementIntensity = { 0f, 0.3f, 0.6f, 1.4f, 1.6f, 2.0f };
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>AD-VISUAL §1.5 in-game element glow (sRGB): fire, ice, electric, venom.</summary>
        public static readonly Color FireGlow = Hex(0xFF5A1F), IceGlow = Hex(0x8CE6FF), ShockGlow = Hex(0xADC6FF), VenomGlow = Hex(0xA8E04A);

        private static Material s_additive;

        private WeaponAttachMount _mount;
        private MaterialPropertyBlock _block;
        private Action<WeaponAttachMount> _onChanged;
        private Action<FeelShot> _onShot;
        private float _appliedFlash = -1f;
        private ParticleSystem _motes, _edge;
        private Transform _twinAnchor, _edgeAnchor;

        /// <summary>Element motes per second at E4 (0 = none).</summary>
        public float MoteRate { get; private set; }
        /// <summary>The motes' and the edge's HDR brightness (the particle material's base colour through a property block).</summary>
        public float Intensity { get; private set; }
        /// <summary>Twin-barrel flashes and element edges reported since the mount came up (tests and probes).</summary>
        public int SecondaryFlashes { get; private set; }
        public int EdgeBursts { get; private set; }
        /// <summary>The flash origin the last edge ring was drawn at (E5's PowerMuzzle, else the rig muzzle).</summary>
        public Transform LastEdgeOrigin { get; private set; }

        public static Color GlowFor(TreeElement e) =>
            e == TreeElement.Fire ? FireGlow : e == TreeElement.Ice ? IceGlow : e == TreeElement.Shock ? ShockGlow : e == TreeElement.Venom ? VenomGlow : Color.black;

        /// <summary>The element's B12 brightness at a tier × the flash scale; ≤ <see cref="ReducedFlashCap"/> under Reduce Flashes.</summary>
        public static float IntensityFor(int tier, float flashScale)
        {
            float k = s_elementIntensity[Mathf.Clamp(tier, 0, s_elementIntensity.Length - 1)] * Mathf.Clamp01(flashScale);
            return flashScale < 0.999f ? Mathf.Min(k, ReducedFlashCap) : k;
        }

        /// <summary>Motes per second for an element tier and flash scale: III 4 · IV 7 · V 12, halved under Reduce Flashes.</summary>
        public static float MoteRateFor(int tier, float flashScale)
        {
            float rate = tier >= 5 ? 12f : tier == 4 ? 7f : tier == MotesFromTier ? 4f : 0f;
            return flashScale < 0.999f ? rate * 0.5f : rate;
        }

        /// <summary>Dots in one edge ring: <see cref="EdgeDots"/>, halved under Reduce Flashes.</summary>
        public static int EdgeDotsFor(float flashScale) => flashScale < 0.999f ? EdgeDots / 2 : EdgeDots;

        private void Awake()
        {
            _mount = GetComponent<WeaponAttachMount>();
            _block = new MaterialPropertyBlock();
            _onChanged = OnChanged;
            _onShot = OnShot;
        }

        private void OnEnable()
        {
            WeaponAttachEvents.Changed += _onChanged;
            FeelEvents.ShotFired += _onShot;
            Apply();
        }

        private void OnDisable()
        {
            WeaponAttachEvents.Changed -= _onChanged;
            FeelEvents.ShotFired -= _onShot;
        }

        private void Update()
        {
            if (!Mathf.Approximately(_appliedFlash, GameFeelSettings.FlashScale)) Apply();   // the setting has no event
        }

        private void OnChanged(WeaponAttachMount m)
        {
            if (m == _mount) Apply();
        }

        /// <summary>Re-shape the motes for the mounted set (event time; the systems are reused).</summary>
        public void Apply()
        {
            if (_mount == null) return;
            float flash = GameFeelSettings.FlashScale;
            _appliedFlash = flash;
            WeaponAttachSet set = _mount.Set;
            Intensity = set.Side != TreeElement.None ? IntensityFor(set.SideTier, flash) : 0f;
            Transform socket = _mount.ElementVfx;
            MoteRate = socket != null && set.Side != TreeElement.None ? MoteRateFor(set.SideTier, flash) : 0f;
            if (MoteRate <= 0f)
            {
                if (_motes != null) _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }

            if (_motes == null || _motes.transform.parent != socket) _motes = BuildSystem("ElementMotes", socket, gameObject.layer, 16);
            ShapeMotes(_motes, set.Side, MoteRate);
            Bright(_motes, Intensity);
            if (!_motes.isPlaying) _motes.Play(true);
        }

        private void OnShot(FeelShot shot)
        {
            if (shot.Special || _mount == null || !_mount.Shown || _mount.Row == null || shot.Classic != _mount.Row.id) return;
            if (WeaponAttachments.Active != _mount || _mount.MuzzleSocket == null) return;
            TwinFlash(shot);
            ElementEdge();
        }

        /// <summary>E3's own flash: an anchor on the persistent SOCKET_Muzzle moved to E3's muzzle — never parented to the add-on itself,
        /// which a set change destroys while the pooled flash may still ride it. No second muzzle light: one per shot.</summary>
        private void TwinFlash(in FeelShot shot)
        {
            Transform twin = _mount.SecondaryMuzzle;
            if (twin == null) return;
            if (_twinAnchor == null) _twinAnchor = Anchor("TwinFlashAnchor");
            _twinAnchor.SetPositionAndRotation(twin.position, twin.rotation);
            HitFeedbackDirector director = HitFeedbackDirector.Instance;
            if (director == null) return;
            director.ReportMuzzleFlash(_twinAnchor, MuzzleFlashSize * TwinFlashShare * Mathf.Max(shot.PowerScale, 0f), 0f);
            SecondaryFlashes++;
        }

        /// <summary>The dominant element's thin ring around the flash origin (E5's PowerMuzzle when mounted, else the rig muzzle).</summary>
        private void ElementEdge()
        {
            WeaponAttachSet set = _mount.Set;
            if (set.Side == TreeElement.None || Intensity <= 0f) return;
            Transform origin = _mount.PowerMuzzle != null ? _mount.PowerMuzzle : RigMuzzle();
            if (origin == null) return;
            if (_edgeAnchor == null) _edgeAnchor = Anchor("ElementEdgeAnchor");
            _edgeAnchor.SetPositionAndRotation(origin.position, origin.rotation);
            if (_edge == null) _edge = BuildEdge(_edgeAnchor, gameObject.layer);
            ParticleSystem.MainModule main = _edge.main;
            main.startColor = GlowFor(set.Side);
            Bright(_edge, Intensity);
            _edge.Emit(EdgeDotsFor(_appliedFlash));
            LastEdgeOrigin = origin;
            EdgeBursts++;
        }

        private Transform RigMuzzle()
        {
            WeaponController c = WeaponRegistry.ActiveController;
            return c != null ? c.Muzzle : null;
        }

        private Transform Anchor(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_mount.MuzzleSocket, false);
            t.gameObject.layer = gameObject.layer;
            return t;
        }

        private void Bright(ParticleSystem ps, float intensity)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, new Color(intensity, intensity, intensity, 1f));
            r.SetPropertyBlock(_block);
        }

        private static ParticleSystem BuildSystem(string name, Transform parent, int layer, int max)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // they trail the weapon when the hero moves
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Additive();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>The element's mote: embers rise, frost drifts, sparks snap short and fast, venom drips.</summary>
        private static void ShapeMotes(ParticleSystem ps, TreeElement e, float rate)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startColor = GlowFor(e);
            bool shock = e == TreeElement.Shock;
            main.startLifetime = shock ? 0.12f : e == TreeElement.Ice ? 0.9f : 0.6f;
            main.startSpeed = shock ? 0.4f : e == TreeElement.Fire ? 0.15f : 0.04f;
            main.startSize = shock ? 0.018f : 0.024f;
            main.gravityModifier = e == TreeElement.Fire ? -0.25f : e == TreeElement.Venom ? 0.35f : 0.02f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.02f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;
        }

        /// <summary>The edge ring: emitted on the rim of a circle across the muzzle axis, flying outward for one flash frame or two.</summary>
        private static ParticleSystem BuildEdge(Transform anchor, int layer)
        {
            ParticleSystem ps = BuildSystem("ElementEdge", anchor, layer, EdgeDots * 4);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;   // kept playing with emission off, so a shot's Emit always simulates
            main.startLifetime = 0.07f;
            main.startSpeed = 0.9f;
            main.startSize = 0.03f;
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;   // Emit() per shot
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;   // the circle lies in the anchor's XY, across the muzzle's +Z
            shape.radius = MuzzleFlashSize * 0.5f;
            shape.radiusThickness = 0f;                          // the rim only
            shape.arcMode = ParticleSystemShapeMultiModeValue.Loop;
            ps.Play();
            return ps;
        }

        /// <summary>One additive particle material (white; each system is tinted by startColor and brightened through its property block).</summary>
        private static Material Additive()
        {
            if (s_additive != null) return s_additive;
            s_additive = new Material(PrototypeShaders.ParticlesUnlit) { name = "WeaponAttach_B12", hideFlags = HideFlags.HideAndDontSave };
            // FeedbackMaterials' additive recipe, set by property presence (URP Particles/Unlit)
            Set(s_additive, "_Surface", 1f);
            Set(s_additive, "_Blend", 1f);
            Set(s_additive, "_ZWrite", 0f);
            Set(s_additive, "_AlphaClip", 0f);
            Set(s_additive, "_SrcBlend", (float)BlendMode.SrcAlpha);
            Set(s_additive, "_DstBlend", (float)BlendMode.One);
            s_additive.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_additive.DisableKeyword("_ALPHATEST_ON");
            s_additive.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            s_additive.renderQueue = (int)RenderQueue.Transparent;
            return s_additive;
        }

        private static void Set(Material m, string property, float value)
        {
            if (m.HasProperty(property)) m.SetFloat(property, value);
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_additive = null;
    }
}
