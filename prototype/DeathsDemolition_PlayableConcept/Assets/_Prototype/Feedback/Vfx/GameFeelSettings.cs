using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019 · ADR-019-2 · The game-feel layer in one asset (Resources/DD_GameFeel):
    /// hit-stop, screen shake, damage vignette. Every value is clamped to the WP-019
    /// ceilings so a tuning pass cannot push the layer past "measured":
    ///
    ///   hit-stop        ≤ 40 ms, only armour break and death (a heavy impact no longer stops time)
    ///   screen shake    ≤ 0.15 m amplitude, ≤ 0.2 s (explosion, player damage)
    ///   damage vignette ≤ 0.5 alpha; flash in 4 f / out 12 f; low-health hold 0.35
    ///
    /// Numbers are PROTOTYPE-ONLY (technical defaults, not a product decision); the asset
    /// is the tuning surface, <see cref="Enabled"/> switches the whole layer off for A/B.
    /// </summary>
    [CreateAssetMenu(fileName = "DD_GameFeel", menuName = "Deaths Demolition/Game Feel Settings")]
    public sealed class GameFeelSettings : ScriptableObject
    {
        public const string ResourceName = "DD_GameFeel";

        public const float MaxHitStopSeconds = 0.040f;
        public const float MaxShakeAmplitude = 0.15f;
        public const float MaxShakeSeconds = 0.20f;
        public const float MaxVignetteAlpha = 0.5f;

        [Header("Layer")]
        [SerializeField] private bool _enabled = true;

        [Header("Hit-stop (s) — armour break and death only")]
        [SerializeField, Range(0f, MaxHitStopSeconds)] private float _hitStopArmorBreak = 0.035f;
        [SerializeField, Range(0f, MaxHitStopSeconds)] private float _hitStopDeath = 0.040f;
        [SerializeField, Range(0f, 0.5f)] private float _hitStopMinInterval = 0.12f;

        [Header("Screen shake (m, s)")]
        [SerializeField, Range(0f, MaxShakeAmplitude)] private float _shakeExplosionAmplitude = 0.12f;
        [SerializeField, Range(0f, MaxShakeSeconds)] private float _shakeExplosionSeconds = 0.20f;
        [SerializeField, Range(0f, MaxShakeAmplitude)] private float _shakePlayerDamageAmplitude = 0f;   // REQ-045 bump lives in PlayerSystem.PlayerDamageFeedback; 0 = no double bump
        [SerializeField, Range(0f, MaxShakeSeconds)] private float _shakePlayerDamageSeconds = 0.15f;
        [SerializeField, Range(0f, MaxShakeAmplitude)] private float _shakeArmorBreakAmplitude = 0.075f;
        [SerializeField, Range(0f, MaxShakeAmplitude)] private float _shakeHeavyDeathAmplitude = 0.095f;

        [Header("Damage vignette")]
        [SerializeField, Range(0f, MaxVignetteAlpha)] private float _vignetteFlashAlpha = 0.35f;   // the HUD edge layer carries the direction; this is the ring
        [SerializeField, Range(0f, MaxVignetteAlpha)] private float _vignetteLowHealthAlpha = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _lowHealthFraction = 0.30f;
        [SerializeField, Range(1, 12)] private int _vignetteFlashInFrames = 4;
        [SerializeField, Range(1, 60)] private int _vignetteFlashOutFrames = 12;

        private static GameFeelSettings s_active;
        private static bool s_tried;

        /// <summary>The Resources asset, or a default instance when the asset is absent (a fresh checkout).</summary>
        public static GameFeelSettings Active
        {
            get
            {
                if (s_active == null && !s_tried)
                {
                    s_tried = true;
                    s_active = Resources.Load<GameFeelSettings>(ResourceName);
                }

                if (s_active == null)
                {
                    s_active = CreateInstance<GameFeelSettings>();
                    s_active.name = ResourceName + " (runtime default)";
                    s_active.hideFlags = HideFlags.HideAndDontSave;
                }

                return s_active;
            }
        }

        public bool Enabled => _enabled;
        public float HitStopArmorBreak => _enabled ? Mathf.Clamp(_hitStopArmorBreak, 0f, MaxHitStopSeconds) : 0f;
        public float HitStopDeath => _enabled ? Mathf.Clamp(_hitStopDeath, 0f, MaxHitStopSeconds) : 0f;
        public float HitStopMinInterval => Mathf.Max(0f, _hitStopMinInterval);
        public float ShakeExplosionAmplitude => _enabled ? Mathf.Clamp(_shakeExplosionAmplitude, 0f, MaxShakeAmplitude) : 0f;
        public float ShakeExplosionSeconds => Mathf.Clamp(_shakeExplosionSeconds, 0f, MaxShakeSeconds);
        public float ShakePlayerDamageAmplitude => _enabled ? Mathf.Clamp(_shakePlayerDamageAmplitude, 0f, MaxShakeAmplitude) : 0f;
        public float ShakePlayerDamageSeconds => Mathf.Clamp(_shakePlayerDamageSeconds, 0f, MaxShakeSeconds);
        public float ShakeArmorBreakAmplitude => _enabled ? Mathf.Clamp(_shakeArmorBreakAmplitude, 0f, MaxShakeAmplitude) : 0f;
        public float ShakeHeavyDeathAmplitude => _enabled ? Mathf.Clamp(_shakeHeavyDeathAmplitude, 0f, MaxShakeAmplitude) : 0f;
        public float VignetteFlashAlpha => _enabled ? Mathf.Clamp(_vignetteFlashAlpha, 0f, MaxVignetteAlpha) : 0f;
        public float VignetteLowHealthAlpha => _enabled ? Mathf.Clamp(_vignetteLowHealthAlpha, 0f, MaxVignetteAlpha) : 0f;
        public float LowHealthFraction => Mathf.Clamp01(_lowHealthFraction);
        public int VignetteFlashInFrames => Mathf.Max(1, _vignetteFlashInFrames);
        public int VignetteFlashOutFrames => Mathf.Max(1, _vignetteFlashOutFrames);

        /// <summary>Tests / A/B: swap the active asset (null restores the Resources asset).</summary>
        public static void Override(GameFeelSettings settings)
        {
            s_active = settings;
            s_tried = settings != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active = null;
            s_tried = false;
        }
    }
}
