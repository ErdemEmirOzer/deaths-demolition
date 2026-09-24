using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>WP-073 · the six weapon classes the fire VFX are authored for (WP-069 arsenal v3).</summary>
    public enum VfxWeaponClass
    {
        AssaultRifle = 0,
        Smg = 1,
        Shotgun = 2,
        Lmg = 3,
        Sniper = 4,
        Launcher = 5
    }

    /// <summary>
    /// WP-073 · one weapon slot's fire visuals (direction §1.1–1.3; palette WP-069 §3.1): tracer ribbon, pellet glint
    /// bars, muzzle flash / light / smoke, casing. Colours are sRGB with the HDR multiplier applied (linear × intensity).
    /// </summary>
    public sealed class VfxWeaponProfile
    {
        public string Slot;
        public VfxWeaponClass Class;

        // ---- tracer (§1.2): L = clamp(v × LengthSeconds, 1.2, MaxLength); the tracer IS the bullet (no mesh).
        public float Speed;                 // m/s (the visual projectile speed; S-02's projectile data replaces it)
        public float LengthSeconds = 0.020f;
        public float MaxLength = 8f;
        public float CoreWidth;             // m
        public float GlowWidth;             // m (0 = none)
        public float HeadBead = 0.20f;      // m at full intensity
        public Color HeadColor;             // HDR
        public Color TailColor;             // HDR
        public int TracerEveryN = 1;        // full tracer on every Nth round; the glint rides every round
        public bool PelletBars;             // shotgun: 0.5 m glint bars, no tracer
        public bool CentreTracer;           // sg_2: the centre pellet carries a Kor-medium tracer

        // ---- muzzle (§1.1)
        public float FlashScale = 1f;
        public Vector2 FlashStretch = Vector2.one;
        public float FlashDurationScale = 1f;
        public float FlashIntensity = 1f;   // sheet emission multiplier (sg: the bloom stays within ~1.5× the quad, AD B1 MUST 4)
        public Color LightColor;
        public float LightIntensity;
        public float LightRange;
        public float LightHoldSeconds;      // full intensity (frames / 60)
        public float LightFadeSeconds;
        public bool Sustained;              // auto ≥ 600 RPM: the light stays lit and flickers 10 Hz while firing
        public float LightTopDownScale = 0.8f;      // AD B1 SHOULD 11: top-down pools under the silhouette
        public float LightFirstPersonScale = 1f;    // WP-073 B2 (AD B1 SHOULD): the sniper's pool at the eye
        public float SmokeStart, SmokeEnd, SmokeSeconds, SmokeAlpha;

        // ---- casing (§1.3)
        public MuzzleVariant CasingMesh = MuzzleVariant.A;
        public float CasingSpeed = 3f;
        public float CasingSpinHz = 12f;
        public Vector3 RestSeconds = new Vector3(3f, 5f, 8f);   // Performance / High / Cinematic

        public float TracerLength => Mathf.Clamp(Speed * LengthSeconds, 1.2f, MaxLength);
    }

    /// <summary>
    /// WP-073 · The fire-VFX table per WP-069 slot (ar_1 … rl_1). Today's three classics map onto ar_1 / smg_1 / sg_1
    /// until S-02's WeaponDefinition v3 carries the slot id.
    /// </summary>
    public static class VfxWeaponProfiles
    {
        private static readonly Color Glint = Hdr("#E8EEF2", 1f);

        public static readonly VfxWeaponProfile Ar1 = new VfxWeaponProfile
        {
            Slot = "ar_1", Class = VfxWeaponClass.AssaultRifle, Speed = 260f, CoreWidth = 0.035f, GlowWidth = 0.10f,
            HeadColor = Hdr("#FFF1DC", 5f), TailColor = Hdr("#FF9A4A", 2f), TracerEveryN = 3,
            FlashScale = 1f, FlashStretch = new Vector2(1.35f, 1f),
            LightColor = Hex("#FFC28A"), LightIntensity = 6f, LightRange = 4f, LightHoldSeconds = 2f / 60f, LightFadeSeconds = 0.08f,
            SmokeStart = 0.30f, SmokeEnd = 0.60f, SmokeSeconds = 0.8f, SmokeAlpha = 0.35f,
            CasingMesh = MuzzleVariant.A, CasingSpeed = 3.0f, CasingSpinHz = 12f, RestSeconds = new Vector3(3f, 5f, 8f)
        };

        public static readonly VfxWeaponProfile Ar2 = new VfxWeaponProfile
        {
            Slot = "ar_2", Class = VfxWeaponClass.AssaultRifle, Speed = 320f, CoreWidth = 0.045f, GlowWidth = 0.12f,
            HeadColor = Hdr("#FFF4E6", 6f), TailColor = Hdr("#FF8A3A", 2.5f), TracerEveryN = 2,
            FlashScale = 0.9f, FlashStretch = new Vector2(1.35f, 1f),
            LightColor = Hex("#FFC28A"), LightIntensity = 6f, LightRange = 4f, LightHoldSeconds = 2f / 60f, LightFadeSeconds = 0.08f,
            SmokeStart = 0.30f, SmokeEnd = 0.60f, SmokeSeconds = 0.8f, SmokeAlpha = 0.35f,
            CasingMesh = MuzzleVariant.A, CasingSpeed = 3.0f, CasingSpinHz = 12f, RestSeconds = new Vector3(3f, 5f, 8f)
        };

        public static readonly VfxWeaponProfile Smg1 = new VfxWeaponProfile
        {
            Slot = "smg_1", Class = VfxWeaponClass.Smg, Speed = 170f, CoreWidth = 0.030f, GlowWidth = 0.08f, HeadBead = 0.15f,
            HeadColor = Hdr("#FFF1DC", 5f), TailColor = Hdr("#FF9A4A", 2f), TracerEveryN = 3,
            FlashScale = 0.75f, FlashDurationScale = 0.6f, Sustained = true,
            LightColor = Hex("#FFC28A"), LightIntensity = 5f, LightRange = 4f, LightHoldSeconds = 2f / 60f, LightFadeSeconds = 0.08f,
            SmokeStart = 0.25f, SmokeEnd = 0.45f, SmokeSeconds = 0.6f, SmokeAlpha = 0.30f,
            CasingMesh = MuzzleVariant.B, CasingSpeed = 2.5f, CasingSpinHz = 15f, RestSeconds = new Vector3(3f, 5f, 8f)
        };

        public static readonly VfxWeaponProfile Smg2 = new VfxWeaponProfile
        {
            Slot = "smg_2", Class = VfxWeaponClass.Smg, Speed = 200f, CoreWidth = 0.030f, GlowWidth = 0.08f, HeadBead = 0.15f,
            HeadColor = Hdr("#FFF1DC", 4f), TailColor = Hdr("#FF9A4A", 2f), TracerEveryN = 4,
            FlashScale = 0.86f, FlashDurationScale = 0.5f, Sustained = true,
            LightColor = Hex("#FFC28A"), LightIntensity = 5f, LightRange = 4f, LightHoldSeconds = 2f / 60f, LightFadeSeconds = 0.08f,
            SmokeStart = 0.25f, SmokeEnd = 0.45f, SmokeSeconds = 0.6f, SmokeAlpha = 0.30f,
            CasingMesh = MuzzleVariant.B, CasingSpeed = 2.5f, CasingSpinHz = 15f, RestSeconds = new Vector3(3f, 5f, 8f)
        };

        public static readonly VfxWeaponProfile Sg1 = new VfxWeaponProfile
        {
            Slot = "sg_1", Class = VfxWeaponClass.Shotgun, Speed = 120f, CoreWidth = 0.025f, GlowWidth = 0f,
            HeadColor = Hdr("#FFD9B0", 2f), TailColor = Hdr("#FFD9B0", 2f), TracerEveryN = 0, PelletBars = true,
            FlashScale = 1.0f, FlashIntensity = 0.5f, FlashStretch = new Vector2(1f, 1.3f),
            LightColor = Hex("#FFB878"), LightIntensity = 7f, LightRange = 5.5f, LightHoldSeconds = 3f / 60f, LightFadeSeconds = 0.12f,
            SmokeStart = 0.40f, SmokeEnd = 0.90f, SmokeSeconds = 1.0f, SmokeAlpha = 0.40f,
            CasingMesh = MuzzleVariant.C, CasingSpeed = 2.0f, CasingSpinHz = 6f, RestSeconds = new Vector3(3f, 6f, 10f)
        };

        public static readonly VfxWeaponProfile Sg2 = new VfxWeaponProfile
        {
            Slot = "sg_2", Class = VfxWeaponClass.Shotgun, Speed = 130f, CoreWidth = 0.025f, GlowWidth = 0.12f,
            HeadColor = Hdr("#FFF4E6", 6f), TailColor = Hdr("#FF8A3A", 2.5f), TracerEveryN = 0, PelletBars = true, CentreTracer = true,
            FlashScale = 1.0f, FlashIntensity = 0.5f, FlashStretch = new Vector2(1f, 1.3f),
            LightColor = Hex("#FFB878"), LightIntensity = 7f, LightRange = 5.5f, LightHoldSeconds = 3f / 60f, LightFadeSeconds = 0.12f,
            SmokeStart = 0.40f, SmokeEnd = 0.90f, SmokeSeconds = 1.0f, SmokeAlpha = 0.40f,
            CasingMesh = MuzzleVariant.C, CasingSpeed = 1.5f, CasingSpinHz = 6f, RestSeconds = new Vector3(3f, 6f, 10f)
        };

        public static readonly VfxWeaponProfile Lmg1 = new VfxWeaponProfile
        {
            Slot = "lmg_1", Class = VfxWeaponClass.Lmg, Speed = 250f, CoreWidth = 0.045f, GlowWidth = 0.12f,
            HeadColor = Hdr("#FFF4E6", 6f), TailColor = Hdr("#FF8A3A", 2.5f), TracerEveryN = 4,
            FlashScale = 1.1f, FlashStretch = new Vector2(1.3f, 1f), Sustained = true,
            LightColor = Hex("#FFC28A"), LightIntensity = 9f, LightRange = 6f, LightHoldSeconds = 2f / 60f, LightFadeSeconds = 0.08f,
            SmokeStart = 0.30f, SmokeEnd = 0.70f, SmokeSeconds = 1.0f, SmokeAlpha = 0.25f,
            CasingMesh = MuzzleVariant.A, CasingSpeed = 3.5f, CasingSpinHz = 12f, RestSeconds = new Vector3(3f, 5f, 8f)
        };

        public static readonly VfxWeaponProfile Lmg2 = new VfxWeaponProfile
        {
            Slot = "lmg_2", Class = VfxWeaponClass.Lmg, Speed = 300f, CoreWidth = 0.060f, GlowWidth = 0.16f, HeadBead = 0.30f,
            HeadColor = Hdr("#FFF8F0", 7f), TailColor = Hdr("#FF6E2E", 3f), TracerEveryN = 5,
            FlashScale = 1.0f, FlashStretch = new Vector2(1.2f, 1f), Sustained = true,
            LightColor = Hex("#FFC28A"), LightIntensity = 9f, LightRange = 6f, LightHoldSeconds = 2f / 60f, LightFadeSeconds = 0.08f,
            SmokeStart = 0.30f, SmokeEnd = 0.70f, SmokeSeconds = 1.0f, SmokeAlpha = 0.25f,
            CasingMesh = MuzzleVariant.A, CasingSpeed = 3.5f, CasingSpinHz = 12f, RestSeconds = new Vector3(3f, 5f, 8f)
        };

        public static readonly VfxWeaponProfile Sr1 = new VfxWeaponProfile
        {
            Slot = "sr_1", Class = VfxWeaponClass.Sniper, Speed = 420f, LengthSeconds = 0.025f, MaxLength = 10f,
            CoreWidth = 0.070f, GlowWidth = 0.20f, HeadBead = 0.30f,
            HeadColor = Hdr("#F4FAFF", 8f), TailColor = Hdr("#9FC8E8", 2f), TracerEveryN = 1,
            FlashScale = 1.6f, FlashStretch = new Vector2(2.2f, 1f), FlashDurationScale = 1.4f,
            LightColor = Hex("#FFD0A0"), LightIntensity = 18f, LightRange = 10f, LightHoldSeconds = 4f / 60f, LightFadeSeconds = 0.20f,
            LightTopDownScale = 0.65f, LightFirstPersonScale = 0.7f,   // WP-073 B2 (AD B1 SHOULD): the 18 / 10 m pool washed the frame
            SmokeStart = 0.45f, SmokeEnd = 1.00f, SmokeSeconds = 1.4f, SmokeAlpha = 0.40f,
            CasingMesh = MuzzleVariant.A, CasingSpeed = 3.0f, CasingSpinHz = 10f, RestSeconds = new Vector3(3f, 5f, 8f)
        };

        public static readonly VfxWeaponProfile Rl1 = new VfxWeaponProfile
        {
            Slot = "rl_1", Class = VfxWeaponClass.Launcher, Speed = 46f, CoreWidth = 0f, GlowWidth = 0f, TracerEveryN = 0,
            FlashScale = 1.8f, FlashDurationScale = 1.5f,
            LightColor = Hex("#FFB070"), LightIntensity = 24f, LightRange = 12f, LightHoldSeconds = 4f / 60f, LightFadeSeconds = 0.25f,
            SmokeStart = 0.8f, SmokeEnd = 2.0f, SmokeSeconds = 2.0f, SmokeAlpha = 0.45f
        };

        private static readonly VfxWeaponProfile[] s_all = { Ar1, Ar2, Smg1, Smg2, Sg1, Sg2, Lmg1, Lmg2, Sr1, Rl1 };

        public static VfxWeaponProfile[] All => s_all;

        /// <summary>The glint every round carries (0.02 m cold white α 0.35).</summary>
        public static Color GlintColor => Glint;
        public const float GlintWidth = 0.02f;
        public const float GlintAlpha = 0.35f;

        /// <summary>Profile by WP-069 slot id (ar_1 …); null when unknown.</summary>
        public static VfxWeaponProfile BySlot(string slot)
        {
            for (int i = 0; i < s_all.Length; i++)
            {
                if (s_all[i].Slot == slot) return s_all[i];
            }

            return null;
        }

        private static VfxWeaponProfile[] s_byRuntimeId = new VfxWeaponProfile[16];

        /// <summary>
        /// The profile of a runtime weapon id: its WeaponDefinition v3 (WP-069) `presentation.tracerVfx` slot, else its
        /// `weaponId` (ar_1 …), else the M3 classic mapping. Cached per id (no lookup per round after the first).
        /// </summary>
        public static VfxWeaponProfile For(ClassicWeaponId id)
        {
            int i = (int)id;
            if (i < 0) return Ar1;
            if (i >= s_byRuntimeId.Length) System.Array.Resize(ref s_byRuntimeId, i + 8);
            VfxWeaponProfile p = s_byRuntimeId[i];
            if (p != null) return p;

            DD.Prototype.Core.Weapons.WeaponDefinition d = DD.Prototype.Core.Weapons.Arsenal.Get(id);
            if (d != null)
            {
                p = BySlot(d.presentation != null ? d.presentation.tracerVfx : null) ?? BySlot(d.weaponId);
            }

            p = p ?? ForClassic(id);
            s_byRuntimeId[i] = p;
            return p;
        }

        /// <summary>The M3 classics (until every definition carries its slot).</summary>
        public static VfxWeaponProfile ForClassic(ClassicWeaponId id)
        {
            switch (id)
            {
                case ClassicWeaponId.AutoSmg: return Smg1;
                case ClassicWeaponId.CombatShotgun: return Sg1;
                default: return Ar1;
            }
        }

        /// <summary>The profile of the weapon in the player's hands, or null (a Special, or no weapon).</summary>
        public static VfxWeaponProfile Active()
        {
            DD.Prototype.Weapons.WeaponController active = DD.Prototype.Weapons.WeaponRegistry.ActiveController;
            if (active == null || active.Classic == null || active.SpecialWeaponActive)
            {
                return null;
            }

            return For(active.Classic.Id);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            System.Array.Clear(s_byRuntimeId, 0, s_byRuntimeId.Length);
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }

        private static Color Hdr(string hex, float intensity)
        {
            Color c = Hex(hex).linear * intensity;
            c.a = 1f;
            return c;
        }
    }
}
