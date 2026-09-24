using UnityEngine;

namespace DD.Prototype.Core
{
    /// <summary>
    /// Layer names and cached masks. Resolve once, never call LayerMask.NameToLayer in
    /// a per-frame path.
    /// </summary>
    public static class PrototypeLayers
    {
        public const string PlayerName = "Player";
        public const string EnemyName = "Enemy";
        public const string EnemyHitboxName = "EnemyHitbox";
        public const string AttackHitboxName = "AttackHitbox";
        public const string ProjectileName = "Projectile";
        public const string PickupName = "Pickup";
        public const string EnvironmentName = "Environment";
        public const string CameraOccluderName = "CameraOccluder";
        /// <summary>WP-071: ragdoll proxy bodies (collide with the world and each other; never with actors, hitboxes, projectiles).</summary>
        public const string RagdollName = "Ragdoll";

        private static bool s_resolved;

        private static int s_player;
        private static int s_enemy;
        private static int s_enemyHitbox;
        private static int s_attackHitbox;
        private static int s_projectile;
        private static int s_pickup;
        private static int s_environment;
        private static int s_cameraOccluder;
        private static int s_ragdoll;

        public static int Player { get { Resolve(); return s_player; } }
        public static int Enemy { get { Resolve(); return s_enemy; } }
        public static int EnemyHitbox { get { Resolve(); return s_enemyHitbox; } }
        public static int AttackHitbox { get { Resolve(); return s_attackHitbox; } }
        public static int Projectile { get { Resolve(); return s_projectile; } }
        public static int Pickup { get { Resolve(); return s_pickup; } }
        public static int Environment { get { Resolve(); return s_environment; } }
        public static int CameraOccluder { get { Resolve(); return s_cameraOccluder; } }
        public static int Ragdoll { get { Resolve(); return s_ragdoll; } }

        /// <summary>What a player bullet is allowed to hit.</summary>
        public static int BulletHitMask => (1 << EnemyHitbox) | (1 << Environment) | (1 << CameraOccluder);

        /// <summary>What an enemy attack hitbox is allowed to hit.</summary>
        public static int EnemyAttackMask => 1 << Player;

        /// <summary>Enemies only, for explosion and chain queries.</summary>
        public static int EnemyMask => 1 << Enemy;

        /// <summary>Blockers for the aim raycast onto the ground plane.</summary>
        public static int AimBlockMask => (1 << Environment) | (1 << CameraOccluder);

        private static void Resolve()
        {
            if (s_resolved)
            {
                return;
            }

            s_resolved = true;
            s_player = LayerMask.NameToLayer(PlayerName);
            s_enemy = LayerMask.NameToLayer(EnemyName);
            s_enemyHitbox = LayerMask.NameToLayer(EnemyHitboxName);
            s_attackHitbox = LayerMask.NameToLayer(AttackHitboxName);
            s_projectile = LayerMask.NameToLayer(ProjectileName);
            s_pickup = LayerMask.NameToLayer(PickupName);
            s_environment = LayerMask.NameToLayer(EnvironmentName);
            s_cameraOccluder = LayerMask.NameToLayer(CameraOccluderName);
            s_ragdoll = LayerMask.NameToLayer(RagdollName);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_resolved = false;
        }
    }
}
