using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-069 (review R1-A #5): why a projectile ended without a stopping impact. Every projectile ends exactly
    /// once: either an <see cref="ProjectileImpactInfo"/> with <c>Stopped</c> or one <see cref="ProjectileEvents.Expired"/>.
    /// </summary>
    public enum ProjectileEndReason
    {
        /// <summary>Flew its full range.</summary>
        Range = 0,
        /// <summary>Its lifetime ran out.</summary>
        Lifetime = 1,
        /// <summary>Retired early because the pool was full (counted in OverflowCount).</summary>
        Evicted = 2,
        /// <summary>Removed with the rest by ProjectileSystem.Clear (scene unload, run end, tests).</summary>
        Cleared = 3
    }

    /// <summary>WP-069 (review R1-A #5): a projectile ended without a stopping impact.</summary>
    public readonly struct ProjectileExpireInfo
    {
        /// <summary>The projectile's key — the same value its Spawned / Impact events carry. Never reused in a session.</summary>
        public readonly int ProjectileId;
        /// <summary>Pool slot (reused after the projectile ends; key by <see cref="ProjectileId"/>).</summary>
        public readonly int Index;
        public readonly ClassicWeaponId WeaponId;
        public readonly Vector3 Position;
        public readonly ProjectileEndReason Reason;

        public ProjectileExpireInfo(int projectileId, int index, ClassicWeaponId weaponId, Vector3 position, ProjectileEndReason reason)
        {
            ProjectileId = projectileId;
            Index = index;
            WeaponId = weaponId;
            Position = position;
            Reason = reason;
        }
    }

    /// <summary>WP-069 ADR-069-1: a projectile came into being (S-07 muzzle / trail start, S-08 flight loop).</summary>
    public readonly struct ProjectileSpawnInfo
    {
        /// <summary>WP-069 (review R1-A #5): the key every event of this projectile carries. Never reused in a session.</summary>
        public readonly int ProjectileId;
        /// <summary>Pool slot (reused after the projectile ends; key by <see cref="ProjectileId"/>).</summary>
        public readonly int Index;
        public readonly ClassicWeaponId WeaponId;
        public readonly ProjectileKind Kind;
        public readonly WeaponCaliber Caliber;
        public readonly Vector3 Position;
        public readonly Vector3 Direction;
        public readonly float Speed;
        public readonly int PelletIndex;
        /// <summary>WP-104 B2: the run tree's volley of the pull (0 = the main shot). Once-per-pull listeners skip volleys above 0.</summary>
        public readonly int VolleyIndex;
        /// <summary>WP-104 B2: the round's size (sh_bore × a merged shot; 1 = shipped) for the trail (S-07).</summary>
        public readonly float SizeScale;

        public ProjectileSpawnInfo(int projectileId, int index, ClassicWeaponId weaponId, ProjectileKind kind, WeaponCaliber caliber,
            Vector3 position, Vector3 direction, float speed, int pelletIndex, int volleyIndex = 0, float sizeScale = 1f)
        {
            ProjectileId = projectileId;
            Index = index;
            WeaponId = weaponId;
            Kind = kind;
            Caliber = caliber;
            Position = position;
            Direction = direction;
            Speed = speed;
            PelletIndex = pelletIndex;
            VolleyIndex = volleyIndex;
            SizeScale = sizeScale;
        }
    }

    /// <summary>WP-069 ADR-069-1: a projectile touched something (world surface or a body).</summary>
    public readonly struct ProjectileImpactInfo
    {
        /// <summary>WP-069 (review R1-A #5): the projectile's key (see <see cref="ProjectileSpawnInfo.ProjectileId"/>).</summary>
        public readonly int ProjectileId;
        /// <summary>Pool slot (reused after the projectile ends).</summary>
        public readonly int Index;
        public readonly ClassicWeaponId WeaponId;
        public readonly ProjectileKind Kind;
        public readonly WeaponCaliber Caliber;
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector3 Direction;
        public readonly SurfaceKind Surface;
        public readonly float Speed;
        public readonly bool Flesh;
        public readonly bool Armor;
        public readonly bool Killed;
        /// <summary>True when the projectile stops here (world, last body it could pass).</summary>
        public readonly bool Stopped;
        public readonly int TransactionId;

        public ProjectileImpactInfo(int projectileId, int index, ClassicWeaponId weaponId, ProjectileKind kind, WeaponCaliber caliber, Vector3 position,
            Vector3 normal, Vector3 direction, SurfaceKind surface, float speed, bool flesh, bool armor, bool killed,
            bool stopped, int transactionId)
        {
            ProjectileId = projectileId;
            Index = index;
            WeaponId = weaponId;
            Kind = kind;
            Caliber = caliber;
            Position = position;
            Normal = normal;
            Direction = direction;
            Surface = surface;
            Speed = speed;
            Flesh = flesh;
            Armor = armor;
            Killed = killed;
            Stopped = stopped;
            TransactionId = transactionId;
        }
    }

    /// <summary>
    /// WP-069 ADR-069-1 · the projectile event surface (S-07 WP-073 visuals, S-08 WP-072 sound,
    /// telemetry). Struct payloads, no allocation. Trails are drawn by READING the live state
    /// (<see cref="ProjectileSystem.LiveCount"/> / <see cref="ProjectileSystem.Get"/>), not from
    /// these events — the events mark births, impacts and deaths.
    ///
    /// Keying (review R1-A #5): every event carries <c>ProjectileId</c>. A projectile ends exactly once —
    /// an Impact with <c>Stopped</c>, or one Expired (range, lifetime, eviction, or <c>Cleared</c> when the
    /// system is emptied on a scene change) — so per-projectile state (a trail, a flight loop) always closes.
    /// </summary>
    public static class ProjectileEvents
    {
        public static event Action<ProjectileSpawnInfo> Spawned;
        public static event Action<ProjectileImpactInfo> Impact;

        /// <summary>A projectile ended without a stopping impact (range, lifetime, eviction, clear).</summary>
        public static event Action<ProjectileExpireInfo> Expired;

        internal static void RaiseSpawned(in ProjectileSpawnInfo info) => Spawned?.Invoke(info);
        internal static void RaiseImpact(in ProjectileImpactInfo info) => Impact?.Invoke(info);
        internal static void RaiseExpired(in ProjectileExpireInfo info) => Expired?.Invoke(info);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Spawned = null;
            Impact = null;
            Expired = null;
        }
    }
}
