#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
#endif
using DD.Prototype.Core.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>User-selected O2. Other looks exist only in development/editor comparison builds.</summary>
    public enum Ap043BulletLook
    {
        Baseline = 0,
        O2 = 2,
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        O1 = 1,
        O3 = 3,
#endif
    }

    public static class Ap043BulletLooks
    {
        public const Ap043BulletLook Selected = Ap043BulletLook.O2;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public static Ap043BulletLook Current { get; private set; } = Selected;
        public static bool Active => Current != Ap043BulletLook.Baseline;

        // Startup only: never enumerate command-line arguments from an emission or frame callback.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize() => Current = Parse(Environment.GetCommandLineArgs());

        public static Ap043BulletLook Parse(string[] arguments)
        {
            if (arguments == null) return Selected;
            for (int i = 0; i + 1 < arguments.Length; i++)
            {
                if (!string.Equals(arguments[i], "-ddBulletLook", StringComparison.Ordinal)) continue;
                switch (arguments[i + 1])
                {
                    case "R4":
                    case "Baseline": return Ap043BulletLook.Baseline;
                    case "O1": return Ap043BulletLook.O1;
                    case "O2": return Ap043BulletLook.O2;
                    case "O3": return Ap043BulletLook.O3;
                    default: return Selected;
                }
            }

            return Selected;
        }

        /// <summary>Test fixture only; restore the captured value in TearDown. Production selects once at startup.</summary>
        public static void SetForTests(Ap043BulletLook look) => Current = look;
#else
        public static Ap043BulletLook Current => Selected;
        public static bool Active => true;
#endif
    }

    /// <summary>
    /// Unmeasured proposal envelopes at 1080p. Metre widths use a 70 px/m reference; the shader applies an absolute, resolution-
    /// proportional pixel ceiling at the actual camera depth. The ceiling has no world-width exemption. These artistic shutter
    /// windows describe presentation only: projectile positions, speed, collision and damage are never changed.
    /// </summary>
    public readonly struct Ap043TracerEnvelope
    {
        public readonly float CoreMetres, CoreFloorPx, CoreCapPx, HaloRatio;
        public readonly float ExposureSeconds, MinLength, MaxLength, TailOutSeconds;

        public Ap043TracerEnvelope(float corePx, float floorPx, float capPx, float haloRatio,
            float exposureSeconds, float minLength, float maxLength, float tailOutSeconds)
        {
            CoreMetres = corePx / 70f;
            CoreFloorPx = floorPx;
            CoreCapPx = capPx;
            HaloRatio = haloRatio;
            ExposureSeconds = exposureSeconds;
            MinLength = minLength;
            MaxLength = maxLength;
            TailOutSeconds = tailOutSeconds;
        }

        public float Trail(float speed) => Mathf.Clamp(Mathf.Max(0f, speed) * ExposureSeconds, MinLength, MaxLength);
    }

    public static class Ap043TracerDesign
    {
        public const string Keyword = "DD_AP043";
        /// <summary>Only the nine explicitly owned normal weapons use the selected flight. Future/special ids keep legacy visuals.</summary>
        public static bool IsNormalWeapon(DD.Prototype.Core.Config.ClassicWeaponId weaponId)
        {
            switch (weaponId)
            {
                case DD.Prototype.Core.Config.ClassicWeaponId.AssaultRifle:
                case DD.Prototype.Core.Config.ClassicWeaponId.AutoSmg:
                case DD.Prototype.Core.Config.ClassicWeaponId.CombatShotgun:
                case DD.Prototype.Core.Config.ClassicWeaponId.Ar2:
                case DD.Prototype.Core.Config.ClassicWeaponId.Smg2:
                case DD.Prototype.Core.Config.ClassicWeaponId.Sg2:
                case DD.Prototype.Core.Config.ClassicWeaponId.Lmg1:
                case DD.Prototype.Core.Config.ClassicWeaponId.Lmg2:
                case DD.Prototype.Core.Config.ClassicWeaponId.Sr1: return true;
                default: return false;
            }
        }

        public static bool IsNormalFlight(ProjectileKind kind, int projectileId, DD.Prototype.Core.Config.ClassicWeaponId weaponId,
            VfxWeaponClass weaponClass = VfxWeaponClass.AssaultRifle) => projectileId >= 0 && IsNormalWeapon(weaponId)
            && weaponClass != VfxWeaponClass.Launcher && (kind == ProjectileKind.Bullet || kind == ProjectileKind.Pellet);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>Proposed physical projectile length, independent of exposure/velocity/tier. Pellets are small round grains.</summary>
        public static float MetalBodyLengthMetres(VfxWeaponClass weaponClass, bool pellet)
        {
            if (pellet) return .005f;
            switch (weaponClass)
            {
                case VfxWeaponClass.Smg: return .023f;
                case VfxWeaponClass.Lmg: return .032f;
                case VfxWeaponClass.Sniper: return .034f;
                default: return .029f;
            }
        }

#endif

        /// <summary>Existing _Extra.w world descriptor: O1 +body length; O2 0; O3 tracer +burn offset, ordinary -body length.</summary>
        public static float WorldDetail(Ap043BulletLook option, VfxWeaponClass weaponClass, bool pellet, bool tracer)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (option == Ap043BulletLook.O1) return MetalBodyLengthMetres(weaponClass, pellet);
            if (option == Ap043BulletLook.O3) return tracer ? .024f : -MetalBodyLengthMetres(weaponClass, pellet);
#endif
            return 0f;
        }

        public static Ap043TracerEnvelope Envelope(Ap043BulletLook look, VfxWeaponClass weaponClass, bool pellet, bool tracer)
        {
            if (pellet)
            {
                if (look == Ap043BulletLook.O2) return new Ap043TracerEnvelope(1.4f, 1.2f, 1.75f, 1.5f, 1f / 700f, .12f, .22f, .035f);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (look == Ap043BulletLook.O3) return new Ap043TracerEnvelope(1.1f, .9f, 1.4f, 1.4f, 1f / 1200f, .08f, .14f, .025f);
                return new Ap043TracerEnvelope(1.25f, 1f, 1.6f, 1.4f, 1f / 900f, .1f, .18f, .03f);
#else
                return new Ap043TracerEnvelope(1.4f, 1.2f, 1.75f, 1.5f, 1f / 700f, .12f, .22f, .035f);
#endif
            }

            if (look == Ap043BulletLook.O2)
            {
                switch (weaponClass)
                {
                    case VfxWeaponClass.Smg: return new Ap043TracerEnvelope(2.1f, 1.7f, 2.5f, 1.6f, 1f / 600f, .22f, .62f, .04f);
                    case VfxWeaponClass.Lmg: return new Ap043TracerEnvelope(2.5f, 2f, 3f, 1.6f, 1f / 430f, .3f, 1.15f, .045f);
                    case VfxWeaponClass.Sniper: return new Ap043TracerEnvelope(2.8f, 2.2f, 3f, 1.6f, 1f / 360f, .48f, 1.5f, .05f);
                    default: return new Ap043TracerEnvelope(2.3f, 1.8f, 2.8f, 1.6f, 1f / 480f, .28f, .95f, .04f);
                }
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (look == Ap043BulletLook.O3)
            {
                if (!tracer)
                {
                    switch (weaponClass)
                    {
                        case VfxWeaponClass.Smg: return new Ap043TracerEnvelope(1.5f, 1.1f, 1.8f, 1.4f, 1f / 1600f, .08f, .24f, .025f);
                        case VfxWeaponClass.Sniper: return new Ap043TracerEnvelope(2f, 1.5f, 2.5f, 1.4f, 1f / 850f, .28f, .8f, .035f);
                        default: return new Ap043TracerEnvelope(1.7f, 1.3f, 2f, 1.4f, 1f / 1500f, .1f, .32f, .025f);
                    }
                }

                switch (weaponClass)
                {
                    case VfxWeaponClass.Smg: return new Ap043TracerEnvelope(1.8f, 1.4f, 2.2f, 1.5f, 1f / 450f, .25f, .75f, .04f);
                    case VfxWeaponClass.Lmg: return new Ap043TracerEnvelope(2.3f, 1.8f, 2.8f, 1.5f, 1f / 320f, .35f, 1.35f, .045f);
                    default: return new Ap043TracerEnvelope(2.1f, 1.6f, 2.5f, 1.5f, 1f / 360f, .3f, 1.2f, .045f);
                }
            }

            switch (weaponClass)
            {
                case VfxWeaponClass.Smg: return new Ap043TracerEnvelope(1.6f, 1.2f, 1.9f, 1.4f, 1f / 1000f, .16f, .36f, .03f);
                case VfxWeaponClass.Lmg: return new Ap043TracerEnvelope(2.1f, 1.6f, 2.4f, 1.4f, 1f / 800f, .2f, .8f, .035f);
                case VfxWeaponClass.Sniper: return new Ap043TracerEnvelope(2.5f, 1.8f, 2.8f, 1.4f, 1f / 650f, .32f, 1f, .04f);
                default: return new Ap043TracerEnvelope(1.9f, 1.5f, 2.2f, 1.4f, 1f / 900f, .18f, .65f, .03f);
            }
#else
            return Envelope(Ap043BulletLook.O2, weaponClass, false, tracer);
#endif
        }

        /// <summary>O1 is a nonemissive sliver, O2 a continuous compact streak, O3 separates metal and sparse burning bases.</summary>
        public static bool IsTracer(Ap043BulletLook look, VfxWeaponClass weaponClass, int rhythmId, bool pellet)
        {
            if (pellet || look == Ap043BulletLook.Baseline) return false;
            if (look == Ap043BulletLook.O2) return true;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (look == Ap043BulletLook.O1) return false;
            int n;
            switch (weaponClass)
            {
                case VfxWeaponClass.Lmg: n = 5; break;
                case VfxWeaponClass.AssaultRifle: n = 4; break;
                case VfxWeaponClass.Smg: n = 6; break;
                default: return false; // the sniper's ordinary round is a reflection, never a coloured burning base
            }

            return rhythmId % n == 0;
#else
            return true;
#endif
        }

        /// <summary>Exactly the experimental vertex ceiling, including orthographic capture targets and flipped RT matrices.</summary>
        public static float PixelWorld(float viewDepth, float projectionM11, float screenHeight, bool orthographic = false, float orthoHalfHeight = 1f)
        {
            float height = Mathf.Max(screenHeight, 1f);
            if (orthographic) return 2f * Mathf.Max(orthoHalfHeight, .001f) / height;
            return 2f * Mathf.Max(Mathf.Abs(viewDepth), .001f) / (Mathf.Max(Mathf.Abs(projectionM11), .0001f) * height);
        }

        /// <summary>No SizeScale, merge or tier argument: those gameplay values cannot enlarge the physical core.</summary>
        public static float WidthPixels(in Ap043TracerEnvelope envelope, float pixelsPerMetre, float screenHeight, bool halo)
        {
            float ratio = halo ? envelope.HaloRatio : 1f;
            float scale = Mathf.Max(screenHeight, 1f) / 1080f;
            return Mathf.Clamp(envelope.CoreMetres * pixelsPerMetre, envelope.CoreFloorPx * scale, envelope.CoreCapPx * scale) * ratio;
        }

        public static float PackDetail(int look, int tier, int secondaryLook, int projectileId, bool pellet = false)
        {
            uint h = unchecked((uint)projectileId * 747796405u + 2891336453u);
            h = ((h >> (int)((h >> 28) + 4)) ^ h) * 277803737u;
            h = (h >> 22) ^ h;
            // This single existing float channel replaces the old halo-step channel in the opt-in shader variant.
            return Mathf.Clamp(look, 0, 5) + 8f * Mathf.Clamp(tier, 1, 5) + 64f * Mathf.Clamp(secondaryLook, 0, 4)
                + (pellet ? 512f : 0f) + (h & 1023u) / 1024f;
        }
    }

    public sealed partial class TracerRibbonRenderer
    {
        private static readonly int Ap043ModeId = Shader.PropertyToID("_Ap043Mode");
        private static readonly int Ap043LayerId = Shader.PropertyToID("_Ap043Layer");
        private static readonly int Ap043FrameId = Shader.PropertyToID("_Ap043Frame");
        private static readonly Unity.Profiling.ProfilerMarker Ap043Marker = new Unity.Profiling.ProfilerMarker("AP043.TracerOptions");
        private static readonly Color Ap043Steel = new Color(.22f, .23f, .24f).linear;
        private static readonly Color Ap043WarmWhite = new Color(1f, .98f, .94f).linear;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static readonly Color Ap043BurningBase = new Color(1f, .96f, .86f).linear;
#endif
        private Material _ap043CoreMaterial, _ap043GlowMaterial, _ap043InkMaterial;
        private bool _ap043HasLegacyTdRounds;

        private void ConfigureAp043Td()
        {
            if (!Ap043BulletLooks.Active) return;
            _ap043CoreMaterial = CloneAp043Material(_tdCoreMaterial, 0f);
            _ap043GlowMaterial = CloneAp043Material(_tdGlowMaterial, 1f);
            _ap043InkMaterial = CloneAp043Material(_tdInkMaterial, 2f);
            // Baseline TD clones, shared _material, ArcSystem and GelSystem never receive this local keyword or uniforms.
        }

        private static Material CloneAp043Material(Material baseline, float layer)
        {
            if (baseline == null) return null;
            var material = new Material(baseline) { name = "AP043 TD selected flight (runtime)", hideFlags = HideFlags.DontSave, enableInstancing = true };
            material.EnableKeyword(Ap043TracerDesign.Keyword);
            material.SetFloat(Ap043ModeId, (float)Ap043BulletLooks.Current);
            material.SetFloat(Ap043LayerId, layer);
            return material;
        }

        private void DestroyAp043Td()
        {
            if (_ap043CoreMaterial != null) Destroy(_ap043CoreMaterial);
            if (_ap043GlowMaterial != null) Destroy(_ap043GlowMaterial);
            if (_ap043InkMaterial != null) Destroy(_ap043InkMaterial);
        }

        private static void ApplyAp043Td(ref TdRound r, VfxWeaponProfile profile, bool tracer)
        {
            Ap043BulletLook look = Ap043BulletLooks.Current;
            r.Ap043Option = look;
            r.Ap043Class = profile.Class;
            r.Ap043Tier = Mathf.Clamp(Mathf.Max(TracerStyle.ShotTier, TracerStyle.HaloTier), 1, 5);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            r.Ap043SecondaryLook = TracerStyle.Ap043SecondaryLook;
#else
            // Only this admitted normal round reads the tree. Legacy/special/UI tree consumers remain unchanged.
            ref readonly DD.Prototype.Rounds.TreeStats stats = ref DD.Prototype.Rounds.TreeRuntime.Current;
            r.Ap043SecondaryLook = stats.Fusion
                ? TracerStyle.LookOf(TreeVfxLadder.FirstElement(stats.Elements & ~TreeVfxLadder.DominantElement(in stats)))
                : TracerStyle.NeutralLook;
#endif
            r.Tracer = tracer;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (look == Ap043BulletLook.O1) r.Tracer = r.Look == TracerStyle.MarkedLook;
#endif
            r.Ap043Envelope = Ap043TracerDesign.Envelope(look, profile.Class, r.Pellet, r.Tracer);
            // Preserve tree hue and K3, but discard the old world-width exemption, tier halo step, trail growth and size/merge.
            r.Core = r.Base = r.Ap043Envelope.CoreMetres;
            r.Halo = r.Core * r.Ap043Envelope.HaloRatio;
            r.CoreMinPx = r.Ap043Envelope.CoreFloorPx;
            r.CapPx = r.Ap043Envelope.CoreCapPx;
            r.HaloPx = 0f;
            r.TrailScale = r.HaloHdr = 1f;
        }

        private static void MarkAp043Td(ref TdRound r, VfxWeaponProfile profile)
        {
            if (!Ap043BulletLooks.Active || !r.Ap043Eligible) return;
            ApplyAp043Td(ref r, profile, true);
            r.Tracer = true; // marked and Third Eye rounds remain explicit information cues even on O1/O3
        }

        private static float Ap043TailOut(in TdRound r) => r.Ap043Option == Ap043BulletLook.Baseline ? TdTailOutSeconds : r.Ap043Envelope.TailOutSeconds;

        /// <summary>Draw through the existing fixed arrays and TD materials. No emitter, light, texture, object or new layer.</summary>
        private int DrawAp043Td(int layer)
        {
            using var marker = Ap043Marker.Auto();
            if (layer == 0) _ap043HasLegacyTdRounds = false;
            if (layer == 1) return 0; // full-flight sniper vapour is replaced by local detail inside the short, bounded ribbon
            int n = 0;
            float flash = Mathf.Clamp01(GameFeelSettings.FlashScale);
            for (int slot = 0; slot < TdCapacity; slot++)
            {
                ref TdRound r = ref _tdRounds[slot];
                if (!r.Alive) continue;
                if (r.Ap043Option == Ap043BulletLook.Baseline) { _ap043HasLegacyTdRounds = true; continue; }
                if (!TdCoreLive(r)) continue;
                bool body = !r.Tracer;
                bool opticalTracerBody = false;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                opticalTracerBody = r.Tracer && r.Ap043Option == Ap043BulletLook.O3 && layer == 2;
#endif
                bool detail = r.Look != TracerStyle.NeutralLook;
                if (layer == 0 && body) continue;
                if (layer == 2 && r.Tracer && _performance) continue;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (layer == 2 && r.Tracer && !detail && r.Ap043Option == Ap043BulletLook.O1) continue;
#endif
                float trail = Mathf.Min((r.Head - r.Origin).magnitude, TdTrail(r));
                if (r.Stopped) trail *= 1f - Mathf.Clamp01((r.Age - r.StopAge) / Ap043TailOut(in r));
                if (trail < .01f) continue;
                Vector3 tail = r.Head - r.Direction * trail;
                Ap043TracerEnvelope envelope = r.Ap043Envelope;
                bool wide = layer == 2 && (!body || detail);
                float ratio = wide ? envelope.HaloRatio : 1f;
                float width = r.Core * ratio;
                Color head, back;
                float alpha;
                if (body || opticalTracerBody)
                {
                    // Nonemissive metal: the material reads ordinary rounds as reflection, not heated projectile bodies.
                    head = back = Ap043Steel;
                    if (detail) head = back = Color.Lerp(Ap043Steel, TracerStyle.Body(r.Look, Ap043Steel), .25f);
                    if (detail) back = TracerStyle.Halo(r.Look, Ap043Steel) * .45f; // separate element-tail colour; never refill metal outside its body mask
                    alpha = .75f;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    if (r.Ap043Option == Ap043BulletLook.O3) alpha = .5f;
#endif
                    if (r.Pellet) alpha *= .85f;
                }
                else if (layer == 2)
                {
                    head = back = TracerStyle.Halo(r.Look, Ap043WarmWhite) * .8f;
                    alpha = detail ? .18f : .08f;
                }
                else
                {
                    float hdr = 1.55f;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    if (r.Ap043Option != Ap043BulletLook.O2) hdr = 1.4f;
#endif
                    hdr *= Mathf.Lerp(.55f, 1f, flash);
                    head = TracerStyle.CoreHead(r.Look, Ap043WarmWhite * TracerStyle.CoreHdr) * (hdr / TracerStyle.CoreHdr);
                    back = TracerStyle.CoreTail(r.Look, Ap043WarmWhite * TracerStyle.TailHdr) * (.4f / TracerStyle.TailHdr);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    if (r.Ap043Option == Ap043BulletLook.O3 && r.Look == TracerStyle.NeutralLook)
                    {
                        // Burning focus lies behind the metal nose in the shader, with a much fainter exposure tail.
                        // This is a white-neutral readability proposal; ammo chemistry and Player luminance are unverified.
                        head = Ap043BurningBase * hdr; back = Ap043BurningBase * .18f;
                    }
#endif
                    alpha = 1f;
                }

                Write(n, 0, r.Head, tail, width, 0f, 1f + trail, 1f, 0f, 0f, head, back, false, false);
                _extra[n] = new Vector4(envelope.CoreFloorPx * ratio,
                    Ap043TracerDesign.PackDetail(r.Look, r.Ap043Tier, r.Ap043SecondaryLook, r.Id, r.Pellet), -envelope.CoreCapPx * ratio,
                    Ap043TracerDesign.WorldDetail(r.Ap043Option, r.Ap043Class, r.Pellet, r.Tracer));
                _headColor[n].w = alpha;
                _tailColor[n].w = body || opticalTracerBody ? 1f / ratio : 0f; // opt-in lane: metal fraction; 0 = a separate halo
                n++;
                n = BendTd(n, slot, false, 0f); // B9 remains curved, with the same short envelope and pixel cap
            }

            if (n == 0) return 0;
            BindAp043Td(flash);
            Material material = layer == 0 ? _ap043CoreMaterial : _ap043GlowMaterial;
            Graphics.DrawMeshInstanced(_quad, 0, material, _matrices, n, _block, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
#if DEVELOPMENT_BUILD || UNITY_EDITOR || DD_PERF_PROBE
            DD.Prototype.Diagnostics.Ap043FlightMetrics.RecordDraw(n);
#endif
            return n;
        }

        private int DrawAp043Ink()
        {
            DrawnInk = 0;
            if (_ap043InkMaterial == null) return 0;
            int n = 0;
            for (int slot = 0; slot < TdCapacity; slot++)
            {
                ref TdRound r = ref _tdRounds[slot];
                if (!r.Alive || r.Ap043Option == Ap043BulletLook.Baseline || r.Look != TracerStyle.MarkedLook || !r.Tracer || !TdCoreLive(r)) continue;
                float trail = Mathf.Min((r.Head - r.Origin).magnitude, TdTrail(r));
                if (r.Stopped) trail *= 1f - Mathf.Clamp01((r.Age - r.StopAge) / Ap043TailOut(in r));
                if (trail < .01f) continue;
                Vector3 tail = r.Head - r.Direction * trail;
                Color ink = TracerStyle.Ink;
                const float share = .68f;
                Write(n, 0, r.Head, tail, r.Core * share, 0f, 1f + trail, 1f, 0f, 0f, ink, ink, false, false);
                _extra[n] = new Vector4(r.CoreMinPx * share,
                    Ap043TracerDesign.PackDetail(r.Look, r.Ap043Tier, 0, r.Id, r.Pellet), -r.CapPx * share,
                    Ap043TracerDesign.WorldDetail(r.Ap043Option, r.Ap043Class, r.Pellet, false)); // physical ink insert, not a second burning focus
                _headColor[n].w = TracerStyle.InkAlpha;
                _tailColor[n].w = 1f;
                n++;
                n = BendTd(n, slot, false, 0f);
            }

            if (n == 0) return 0;
            BindAp043Td(Mathf.Clamp01(GameFeelSettings.FlashScale));
            Graphics.DrawMeshInstanced(_quad, 0, _ap043InkMaterial, _matrices, n, _block, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
#if DEVELOPMENT_BUILD || UNITY_EDITOR || DD_PERF_PROBE
            DD.Prototype.Diagnostics.Ap043FlightMetrics.RecordDraw(n);
#endif
            DrawnInk = n;
            return n;
        }

        private void BindAp043Td(float flash)
        {
            _block.SetVectorArray(HeadId, _head); _block.SetVectorArray(TailId, _tail);
            _block.SetVectorArray(HeadColorId, _headColor); _block.SetVectorArray(TailColorId, _tailColor);
            _block.SetVectorArray(SpanId, _span); _block.SetVectorArray(ExtraId, _extra);
            _block.SetVector(Ap043FrameId, new Vector4(VfxClock.Now, flash, flash < .99f ? 1f : 0f, _performance ? .5f : 1f));
        }
    }
}
