using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · WIND-SPEC v1 §4 parameters (animation-specialist) as data. The one wind stays <see cref="WorldWind"/>
    /// (ADR-013-1, LOOK-BIBLE principle 3): the bend direction, the clock and the on/off come from _DD_WindVector
    /// (dir.xy, strength, time); the travelling gust waves are fixed at the AUTHORED base direction (WindDriver
    /// DirectionDegrees, +24°) plus each wave's offset. These are only the vegetation response on top.
    /// Override asset: Resources/<see cref="ResourcesPath"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Deaths Demolition/Vegetation Wind Settings", fileName = "DD_VegetationWind")]
    public sealed class VegetationWindSettings : ScriptableObject
    {
        public const string ResourcesPath = "DD_VegetationWind";

        [System.Serializable]
        public struct Wave
        {
            public float wavelength;
            public float speed;
            public float amplitude;
            public float offsetDegrees;
            public float phase;
        }

        [Header("Global (§1)")]
        [Tooltip("Used only when no WindDriver exists (tests); otherwise the driver's authored direction.")]
        public float windDirDegrees = 24f;
        [Range(0f, 1.6f)] public float windGlobalStrength = 1f;
        [Tooltip("WorldWind.Strength the global strength is normalised against (base 0.35 + half the 0.4 gust).")]
        public float worldWindReference = 0.55f;
        [Range(0.15f, 0.45f)] public float windBase = 0.30f;
        public Wave wave1 = new Wave { wavelength = 14f, speed = 3.0f, amplitude = 0.40f, offsetDegrees = 0f, phase = 0f };
        public Wave wave2 = new Wave { wavelength = 6.5f, speed = 2.2f, amplitude = 0.22f, offsetDegrees = 17f, phase = 2.1f };
        public Wave wave3 = new Wave { wavelength = 3.1f, speed = 1.6f, amplitude = 0.10f, offsetDegrees = -23f, phase = 4.4f };
        public float noiseTile = 24f, noiseSpeed = 2f;

        [Header("Response (§1 / §4)")]
        public float swayK = 0.47f, swayFMin = 0.40f, swayFMax = 1.30f, swayLambda = 4f, swayCross = 0.35f;
        public float leanBase = 0.07f, leanGust = 0.24f, swayBase = 0.03f, swayGust = 0.065f;
        public float stiffScale = 0.8f, normalTilt = 1.2f, arcK = 0.8f, flatSquash = 0.35f;
        public float phaseSpreadShort = 0.35f, phaseSpreadTall = 0.6f, phaseSpreadFern = 0.8f, phaseSpreadWoody = 0.3f;
        public float gustTintLo = -0.03f, gustTintHi = 0.07f;
        public float capGrass = 0.85f, capWoody = 0.18f, capTall = 0.75f, tallHeight = 0.6f;

        [Header("Interaction (§3)")]
        public int pushMapSize = 256;
        public float pushMapWorld = 40f;
        public int pushMapSizePerformance = 128;
        public float pushMapWorldPerformance = 24f;
        public int maxPushers = 64;
        public float pushOmegaGrass = 8f, pushOmegaWoody = 10f, pushZetaGrass = 0.70f, pushZetaWoody = 0.50f;
        public float flatHoldGrass = 0.4f, flatHalfLifeGrass = 1.0f, flatHoldFern = 0.3f, flatHalfLifeFern = 0.7f, flatHoldWoody = 0f, flatHalfLifeWoody = 0.4f;
        [Tooltip("Performance: interaction only within this distance of the player and only on LOD0.")]
        public float performanceInteractionRadius = 12f;
        [Header("Pushers (§3 table)")]
        public float playerRadius = 0.55f, playerStrength = 1f, playerFlattenRate = 1.5f, playerFlattenCap = 0.6f, playerVelocityBias = 0.6f;
        public float zombieRadius = 0.45f, zombieStrength = 0.75f, zombieFlattenRate = 1.0f, zombieFlattenCap = 0.5f;
        public float zombieRunRadius = 0.5f, zombieRunStrength = 0.9f, zombieRunFlattenRate = 1.6f, zombieRunFlattenCap = 0.7f, zombieRunVelocityBias = 0.8f, zombieRunSpeed = 2.5f;

        [Header("Reduced motion (§4)")]
        public float reducedGlobalStrength = 0.35f;

        private static VegetationWindSettings s_default;

        public static VegetationWindSettings Load()
        {
            var asset = Resources.Load<VegetationWindSettings>(ResourcesPath);
            if (asset != null) return asset;
            if (s_default == null)
            {
                s_default = CreateInstance<VegetationWindSettings>();
                s_default.name = "DD_VegetationWind (code defaults)";
                s_default.hideFlags = HideFlags.HideAndDontSave;
            }

            return s_default;
        }

        // ------------------------------------------------------------------ globals

        public static readonly int WaveDir01Id = Shader.PropertyToID("_DD_VegWaveDir01");
        public static readonly int WaveDir2Id = Shader.PropertyToID("_DD_VegWaveDir2");
        public static readonly int WaveKId = Shader.PropertyToID("_DD_VegWaveK");
        public static readonly int WaveWId = Shader.PropertyToID("_DD_VegWaveW");
        public static readonly int WaveAmpId = Shader.PropertyToID("_DD_VegWaveAmp");
        public static readonly int WavePhaseId = Shader.PropertyToID("_DD_VegWavePhase");
        public static readonly int SwayId = Shader.PropertyToID("_DD_VegSway");
        public static readonly int LeanId = Shader.PropertyToID("_DD_VegLean");
        public static readonly int ShapeId = Shader.PropertyToID("_DD_VegShape");
        public static readonly int TintId = Shader.PropertyToID("_DD_VegTint");
        public static readonly int CapsId = Shader.PropertyToID("_DD_VegCaps");
        public static readonly int SpreadId = Shader.PropertyToID("_DD_VegPhaseSpread");
        public static readonly int SpringId = Shader.PropertyToID("_DD_VegPushSpring");
        public static readonly int FlatId = Shader.PropertyToID("_DD_VegFlat");
        public static readonly int FlatFernId = Shader.PropertyToID("_DD_VegFlatFern");

        /// <summary>
        /// Writes the vegetation wind globals (no allocation). <paramref name="tier"/> 0 Performance (W1 only, no noise,
        /// no short-grass sway), 1 High / 2 Cinematic (3 waves + noise). <paramref name="baseDegrees"/> = authored wind
        /// direction; <paramref name="worldStrength"/> = WorldWind.Strength (0 = calm → no motion at all).
        /// </summary>
        public void ApplyGlobals(int tier, bool reducedMotion, float baseDegrees, float worldStrength)
        {
            bool perf = tier <= 0;
            float global = windGlobalStrength * Mathf.Clamp(worldStrength / Mathf.Max(0.05f, worldWindReference), 0f, 1.6f);
            if (reducedMotion) global = Mathf.Min(global, reducedGlobalStrength);

            Vector2 d1 = Dir(baseDegrees + wave1.offsetDegrees), d2 = Dir(baseDegrees + wave2.offsetDegrees), d3 = Dir(baseDegrees + wave3.offsetDegrees);
            float k1 = K(wave1), k2 = K(wave2), k3 = K(wave3);
            Shader.SetGlobalVector(WaveDir01Id, new Vector4(d1.x, d1.y, d2.x, d2.y));
            Shader.SetGlobalVector(WaveDir2Id, new Vector4(d3.x, d3.y, 1f / Mathf.Max(1f, noiseTile), noiseSpeed));
            Shader.SetGlobalVector(WaveKId, new Vector4(k1, k2, k3, windBase));
            Shader.SetGlobalVector(WaveWId, new Vector4(k1 * wave1.speed, k2 * wave2.speed, k3 * wave3.speed, global));
            Shader.SetGlobalVector(WaveAmpId, new Vector4(wave1.amplitude, perf ? 0f : wave2.amplitude, perf ? 0f : wave3.amplitude, perf ? 0f : 1f));
            Shader.SetGlobalVector(WavePhaseId, new Vector4(wave1.phase, wave2.phase, wave3.phase, 0f));
            Shader.SetGlobalVector(SwayId, new Vector4(swayK, swayFMin, Mathf.Min(1.5f, swayFMax), Mathf.Max(0.5f, swayLambda)));
            Shader.SetGlobalVector(LeanId, new Vector4(leanBase, leanGust, swayBase, swayGust));
            Shader.SetGlobalVector(ShapeId, new Vector4(swayCross, normalTilt, arcK, flatSquash));
            // w: sway enable (reduced motion: off) — x of _DD_VegCaps.w carries "Performance: no sway on short grass".
            Shader.SetGlobalVector(TintId, new Vector4(gustTintLo * (reducedMotion ? 0.5f : 1f), gustTintHi * (reducedMotion ? 0.5f : 1f), stiffScale, reducedMotion ? 0f : 1f));
            Shader.SetGlobalVector(CapsId, new Vector4(capGrass, capWoody, capTall, perf ? tallHeight * -1f : tallHeight));
            Shader.SetGlobalVector(SpreadId, new Vector4(phaseSpreadShort, phaseSpreadTall, phaseSpreadFern, phaseSpreadWoody));
            float zg = reducedMotion ? 1f : pushZetaGrass, zw = reducedMotion ? 1f : pushZetaWoody;
            Shader.SetGlobalVector(SpringId, new Vector4(pushOmegaGrass, pushOmegaWoody, zg, zw));
            Shader.SetGlobalVector(FlatId, new Vector4(flatHoldGrass, flatHalfLifeGrass, flatHoldWoody, flatHalfLifeWoody));
            Shader.SetGlobalVector(FlatFernId, new Vector4(flatHoldFern, flatHalfLifeFern, 0f, 0f));
        }

        private static Vector2 Dir(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private static float K(Wave w) => 2f * Mathf.PI / Mathf.Max(0.5f, w.wavelength);
    }
}
