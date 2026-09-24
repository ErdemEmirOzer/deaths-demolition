using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// WP-076 B6 · §5.1 the camera-mode rows of the spawn rules (TopDown, ThirdPerson, FirstPerson) and the tier path gate.
    /// TopDown keeps the WP-030/054 values untouched (config MinSpawnDistance, tuning BehindCameraRejectDistance /
    /// OnScreenFactor, viewport "on screen"); in TP / FP the stricter row applies (max of the two distances, min of the
    /// two factors) and "on screen" means in the view frustum AND not behind a wall (Linecast camera → chest, Environment).
    /// Tier path gate: an Elite / Ultra comes only from a point ≤ 55 m of path from the player, a Special ≤ 75 m
    /// (S-05 §3.4; the Executioner's first contact ≤ 35 s); Standard unlimited.
    /// </summary>
    public static class SpawnModeTable
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076 §5.1 by camera mode (TopDown, TP, FP): behind-camera reject distance (m), minimum spawn distance (m), on-screen weight factor.", true)]
        public static readonly float[] BehindCameraReject = { 15f, 16f, 18f };
        public static readonly float[] MinSpawnDistance = { 12f, 14f, 16f };
        public static readonly float[] OnScreenFactor = { 0.35f, 0.30f, 0.25f };

        [PrototypeOnly(PendingDecisions.None, "WP-076 §5.1 tier path gate (m of walkable path to the player): Elite / Ultra, Special; chest height (m) of the TP/FP line of sight.", true)]
        public const float HeavyTierMaxPath = 55f, SpecialMaxPath = 75f, ChestHeight = 1.2f;

        public static int Mode => (int)CameraModeService.Current;

        public static int Clamp(int mode) => mode < 0 ? 0 : mode > 2 ? 2 : mode;

        /// <summary>0 = no gate.</summary>
        public static float TierPathMax(EnemyTier tier) =>
            tier == EnemyTier.Elite || tier == EnemyTier.Ultra ? HeavyTierMaxPath : tier == EnemyTier.Special ? SpecialMaxPath : 0f;
    }
}
