using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>WP-073 A4.6: a damped spring (frequency, damping ratio).</summary>
    public readonly struct FeelSpring
    {
        public readonly float Hz;
        public readonly float Zeta;
        public FeelSpring(float hz, float zeta) { Hz = hz; Zeta = zeta; }
    }

    /// <summary>WP-069 §3 weight class (W1 light … W4 launcher / sniper).</summary>
    public enum FeelWeight { W1 = 0, W2 = 1, W3 = 2, W4 = 3 }

    /// <summary>WP-073 A4.5 hit-stop events.</summary>
    public enum HitStopEventId { Kill = 0, HeadshotKill = 1, ArmorBreak = 2, HeavyDeath = 3, UltraKill = 4, SpecialChainKill = 5, PlayerHeavyHit = 6 }

    /// <summary>WP-073 A4.2 / A4.3 · one weapon's cosmetic camera row (recoil pitch / yaw stay S-02's game data).</summary>
    public sealed class CameraWeaponFeelRow
    {
        public string WeaponId;
        public FeelWeight Weight;
        public float RecoverySeconds;                 // recoil return (%95 settle); ω = 4.6 / s
        public float FpRollDeg, FpFovPunchDeg, FpFovPunchAdsDeg;
        public float FpMicroHz, FpMicroDeg, FpMicroSeconds;   // 0 s = sustained while firing
        public float VmBackCm;                        // TP kick: back / down m = VmBackCm × 0.005 (A4.3)
        public float TpFovPunchDeg => FpFovPunchDeg * 0.65f;
        public float TpRollDeg => Mathf.Min(FpRollDeg * 0.6f, 1f);
        public float TpBackMeters => Mathf.Min(VmBackCm * 0.005f, 0.12f);
        public bool Sustained => FpMicroSeconds <= 0f;
    }

    /// <summary>WP-073 A4.1 · FP bob band.</summary>
    public readonly struct BobFeel
    {
        public readonly float VerticalCm, LateralCm, RollDeg, MinHz, MaxHz;
        public BobFeel(float v, float l, float roll, float minHz, float maxHz) { VerticalCm = v; LateralCm = l; RollDeg = roll; MinHz = minHz; MaxHz = maxHz; }
    }

    /// <summary>
    /// WP-073 A4 (direction A4.0–A4.8) · the camera-mode feel values, owned by S-07, read by the rig (S-01 sink) and the
    /// S-07 feel driver. Kept as a code table like <c>VfxWeaponProfiles</c> (a new weapon = a new row, J7); the FeelConfig v2
    /// section of A4.8 is the later home when the live-edit panel needs it.
    /// </summary>
    public static class CameraModesFeel
    {
        // ---- recoil (A4.2 rule 1, A4.6 RecoilReturn)
        public const float RecoilCapFpDeg = 8f, RecoilCapTpDeg = 6f;
        public const float DefaultRecoverySeconds = 0.35f;

        // ---- kick springs by weight (A4.6 KickSpring)
        private static readonly FeelSpring[] s_kickSprings = { new FeelSpring(14f, 0.70f), new FeelSpring(11f, 0.65f), new FeelSpring(8f, 0.55f), new FeelSpring(6f, 0.50f) };
        public static FeelSpring KickSpring(FeelWeight w) => s_kickSprings[(int)w];

        // ---- FOV punch (A4.6): peak ≤ 0.08 s ease-out, return spring 6 Hz ζ 0.8; sustained auto +0.4° ramp 0.25 s
        public const float FovPunchPeakSeconds = 0.08f;
        public static readonly FeelSpring FovReturn = new FeelSpring(6f, 0.8f);
        public const float FovSustainAutoDeg = 0.4f, FovSustainRampSeconds = 0.25f;
        public const float FovPunchCapDeg = 6f;
        public const float TpFovScale = 0.65f, AdsFovPunchScale = 0.6f, AdsRollScale = 0.5f;
        public const float SprintFovFpDeg = 4f, SprintFovTpDeg = 2f, SprintFovRampSeconds = 0.25f;

        // ---- roll / shake caps (A4.6)
        public const float RollCapFpDeg = 1.5f, RollCapTpDeg = 1.0f;
        public const float FpShakeDegreesPerMeter = 12f, FpShakeAggregateCapDeg = 1.8f, FpShakeSingleCapDeg = 0.5f;
        public const float TpShakeTranslationScale = 0.55f;
        public const float MicroContinuousMaxHz = 9f, MicroRampIn = 0.25f, MicroRampOut = 0.20f;
        public const float CosmeticAngularVelocityCapDegPerSec = 60f;

        // ---- bob (A4.1): walk ≈ 3 m/s, run 6.2 m/s; TP × 0.25; ADS × 0.3; reload × 0.8; weight W1 0.85 … W4 1.4
        public static readonly BobFeel BobWalk = new BobFeel(1.4f, 0.9f, 0.35f, 1.8f, 2.0f);
        public static readonly BobFeel BobRun = new BobFeel(3.0f, 1.7f, 0.70f, 2.7f, 3.4f);
        public const float BobWalkSpeed = 3.0f, BobRunSpeed = 6.2f, BobMinSpeed = 0.35f, BobFadeSeconds = 0.12f;
        public const float BobTpScale = 0.25f, BobAdsScale = 0.30f, BobReloadScale = 0.8f, BobHighCadenceHz = 3f, BobHighCadenceScale = 0.8f;
        public const int BobContactFrames = 3;
        private static readonly float[] s_bobWeight = { 0.85f, 1.0f, 1.25f, 1.4f };
        public static float BobWeightScale(FeelWeight w) => s_bobWeight[(int)w];
        public const float BreathPitchDeg = 0.12f, BreathHz = 0.3f;

        // ---- landing (A4.1 LandDip, A4.6 8 Hz ζ 0.6)
        public const float LandRunDipCm = 1.5f, LandRunSeconds = 0.12f, LandFallDipCm = 5f, LandFallPitchDeg = 0.6f, LandFallSeconds = 0.18f, LandFallMinMeters = 0.6f;
        public static readonly FeelSpring LandSpring = new FeelSpring(8f, 0.6f);

        // ---- directional damage (A4.5 §5.3)
        public const float DamageRollFpDeg = 1.2f, DamageRollTpDeg = 0.8f, DamagePitchFpDeg = 0.6f, DamageKickTpMeters = 0.03f;
        public const float DamageSeconds = 0.20f, HeavyDamageScale = 1.6f, DamageStackWindow = 0.25f, HeavyDamageFraction = 0.25f;
        public static readonly FeelSpring DamageSpring = new FeelSpring(8f, 0.6f);

        // ---- kill confirm (A4.5 §5.2)
        public const float KillBumpMeters = 0.008f, KillBumpBigMeters = 0.012f, KillBumpRollFpDeg = 0.10f, KillBumpSeconds = 0.06f;
        public const float HeadshotFovDeg = 0.5f, KillMergeWindow = 0.08f, KillMinInterval = 0.12f;
        public static readonly FeelSpring KillSpring = new FeelSpring(12f, 0.7f);

        // ---- explosion falloff (A4.5 §5.4)
        public const float ExplosionFullRange = 3.2f, ExplosionZeroRange = 12f, ExplosionFalloffPower = 1.5f;
        public const float ExplosionFpDeg = 1.8f, ExplosionFpFovDeg = 3f, ExplosionFovRange = 5f, ExplosionTpMeters = 0.10f, ExplosionTpRollDeg = 2f;

        /// <summary>A(d) = (1 − sat((d − 3.2) / (12 − 3.2)))^1.5.</summary>
        public static float ExplosionFalloff(float distance)
        {
            float t = Mathf.Clamp01((distance - ExplosionFullRange) / (ExplosionZeroRange - ExplosionFullRange));
            return Mathf.Pow(1f - t, ExplosionFalloffPower);
        }

        // ---- hit-stop (A4.5 §5.1): top-down / TP / FP seconds; FP cap 35 ms; min interval kill 0.20 s, others 0.25 s
        public const float HitStopFpCapSeconds = 0.035f, HitStopKillInterval = 0.20f, HitStopOtherInterval = 0.25f;
        private static readonly Vector3[] s_hitStop =
        {
            new Vector3(0.018f, 0.018f, 0.015f),   // Kill
            new Vector3(0.028f, 0.028f, 0.025f),   // HeadshotKill
            new Vector3(0.035f, 0.035f, 0.030f),   // ArmorBreak
            new Vector3(0.040f, 0.040f, 0.035f),   // HeavyDeath
            new Vector3(0.040f, 0.040f, 0.035f),   // UltraKill
            new Vector3(0.022f, 0.022f, 0.020f),   // SpecialChainKill
            new Vector3(0.020f, 0.020f, 0.015f),   // PlayerHeavyHit
        };

        public static float HitStopSeconds(HitStopEventId id, CameraMode mode)
        {
            Vector3 row = s_hitStop[(int)id];
            float s = mode == CameraMode.FirstPerson ? row.z : mode == CameraMode.ThirdPerson ? row.y : row.x;
            return mode == CameraMode.FirstPerson ? Mathf.Min(s, HitStopFpCapSeconds) : s;
        }

        // ---- A4.2 weapon rows (12). Recoil pitch / yaw: S-02 WeaponDefinition v3 (not here).
        private static CameraWeaponFeelRow Row(string id, FeelWeight w, float rec, float roll, float fov, float fovAds, float microHz, float microDeg, float microS, float vmBack)
        {
            return new CameraWeaponFeelRow { WeaponId = id, Weight = w, RecoverySeconds = rec, FpRollDeg = roll, FpFovPunchDeg = fov, FpFovPunchAdsDeg = fovAds,
                FpMicroHz = microHz, FpMicroDeg = microDeg, FpMicroSeconds = microS, VmBackCm = vmBack };
        }

        public static readonly CameraWeaponFeelRow[] Weapons =
        {
            Row("ar_1", FeelWeight.W2, 0.35f, 0.15f, 1.2f, 0.72f, 20f, 0.06f, 0.08f, 3.0f),
            Row("ar_2", FeelWeight.W2, 0.30f, 0.20f, 1.4f, 0.84f, 20f, 0.07f, 0.10f, 3.5f),
            Row("smg_1", FeelWeight.W1, 0.25f, 0.12f, 0.8f, 0.48f, 24f, 0.05f, 0.06f, 2.0f),
            Row("smg_2", FeelWeight.W1, 0.30f, 0.15f, 0.9f, 0.54f, 24f, 0.05f, 0.06f, 2.2f),
            Row("sg_1", FeelWeight.W3, 0.55f, 0.60f, 3.5f, 2.10f, 14f, 0.25f, 0.14f, 8.0f),
            Row("sg_2", FeelWeight.W3, 0.60f, 0.70f, 4.0f, 2.40f, 14f, 0.28f, 0.16f, 9.0f),
            Row("lmg_1", FeelWeight.W3, 0.50f, 0.25f, 1.2f, 0.72f, 8f, 0.12f, 0f, 3.5f),
            Row("lmg_2", FeelWeight.W3, 0.60f, 0.22f, 1.1f, 0.66f, 8f, 0.11f, 0f, 3.2f),
            Row("sr_1", FeelWeight.W4, 0.90f, 0.80f, 4.5f, 2.50f, 12f, 0.20f, 0.12f, 9.0f),
            Row("rl_1", FeelWeight.W4, 1.00f, 1.20f, 6.0f, 3.60f, 9f, 0.40f, 0.30f, 12.0f),
            Row("sw_arc", FeelWeight.W2, 0.40f, 0.40f, 2.5f, 1.50f, 28f, 0.15f, 0.35f, 5.0f),
            Row("sw_gel", FeelWeight.W1, 0.20f, 0.20f, 0.8f, 0.48f, 6f, 0.10f, 0.20f, 3.0f),
        };

        /// <summary>
        /// WP-073 B4 J5: the player's last footfall (animation events foot_l / foot_r, reported by the S-07 driver) — the rig
        /// locks the bob phase to it so the camera trough lands on the contact even when the feet slide.
        /// </summary>
        public static int FootContactFrame = -1;
        public static bool FootContactLeft;

        public static void ReportFootContact(bool left)
        {
            FootContactFrame = Time.frameCount;
            FootContactLeft = left;
        }

        /// <summary>The row of the weapon that fired last (set by the S-07 feel driver per shot; the rig's recoil return reads it).</summary>
        public static CameraWeaponFeelRow ActiveRow = null;

        private static readonly Dictionary<string, CameraWeaponFeelRow> s_byId = BuildIndex();

        private static Dictionary<string, CameraWeaponFeelRow> BuildIndex()
        {
            var d = new Dictionary<string, CameraWeaponFeelRow>(Weapons.Length);
            foreach (CameraWeaponFeelRow r in Weapons) d[r.WeaponId] = r;
            return d;
        }

        /// <summary>The row for a WeaponDefinition v3 id (ar_1 …), else ar_1 (a weapon without a row still feels like a rifle).</summary>
        public static CameraWeaponFeelRow For(string weaponId)
        {
            return weaponId != null && s_byId.TryGetValue(weaponId, out CameraWeaponFeelRow r) ? r : Weapons[0];
        }

        private static readonly CameraWeaponFeelRow[] s_byClassic = new CameraWeaponFeelRow[32];

        /// <summary>The row for a classic weapon (WP-069 Arsenal id), cached per id.</summary>
        public static CameraWeaponFeelRow For(ClassicWeaponId id)
        {
            int i = (int)id;
            if (i < 0 || i >= s_byClassic.Length) return Weapons[0];
            CameraWeaponFeelRow row = s_byClassic[i];
            if (row != null) return row;
            DD.Prototype.Core.Weapons.WeaponDefinition d = DD.Prototype.Core.Weapons.Arsenal.Get(id);
            row = For(d != null ? d.weaponId : null);
            s_byClassic[i] = row;
            return row;
        }

        // ---- A4.7 accessibility
        /// <summary>Comfort preset (one click): shake 0.6, head bob 0.4, roll 0, FOV punch ×0.5 (cap +3°), no micro / sustained shake, sprint FOV +2°.</summary>
        public const float ComfortShakeScale = 0.6f, ComfortHeadBobScale = 0.4f, ComfortFovPunchScale = 0.5f, ComfortFovPunchCapDeg = 3f, ComfortSprintFovDeg = 2f;
        /// <summary>Reduced motion: bob / FOV punch / roll / kick translation / micro / land dip 0, sway ×0.35, shake ×0.3; recoil unchanged (game).</summary>
        public const float ReducedShakeScale = 0.3f, ReducedSwayScale = 0.35f;

        /// <summary>Angular cosmetic channels scale with the FP FOV slider: × clamp(fov / 85, 0.85, 1.15).</summary>
        public static float FovAngularScale(float horizontalFovDeg) => Mathf.Clamp(horizontalFovDeg / 85f, 0.85f, 1.15f);
    }
}
