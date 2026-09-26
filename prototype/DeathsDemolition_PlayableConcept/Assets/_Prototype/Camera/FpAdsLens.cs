using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// WP-069 FP weapon feel (C-00 26.09) · what the first-person weapon publishes for the camera each frame: the eased ADS
    /// weight and the world-FOV factor (WP-073 A4.1: ×0.85 AR / SMG / LMG, ×0.90 shotgun, ×0.45 sr_1, ×0.80 launcher at full
    /// ADS). The rig multiplies the FP horizontal FOV by <see cref="WorldFovScale"/>; S-07's sink can scale bob / roll / FOV
    /// punch by <see cref="Ads01"/> (CameraModesFeel.BobAdsScale, AdsRollScale, AdsFovPunchScale). Written by
    /// S-02's WeaponController (1 and 0 outside first person).
    /// </summary>
    public static class FpAdsLens
    {
        /// <summary>The eased ADS weight, 0 hip … 1 on the sights.</summary>
        public static float Ads01 { get; set; }

        /// <summary>The factor on the FP world FOV (1 at hip).</summary>
        public static float WorldFovScale { get; set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Ads01 = 0f;
            WorldFovScale = 1f;
        }
    }
}
