using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>WP-069 ADR-069-1: a projectile came into being (S-07 muzzle / trail start, S-08 flight loop).</summary>
    public readonly struct ProjectileSpawnInfo
    {
        public readonly int Index;
        public readonly ClassicWeaponId WeaponId;
        public readonly ProjectileKind Kind;
        public readonly WeaponCaliber Caliber;
        public readonly Vector3 Position;
        public readonly Vector3 Direction;
        public readonly float Speed;
        public readonly int PelletIndex;

        public ProjectileSpawnInfo(int index, ClassicWeaponId weaponId, ProjectileKind kind, WeaponCaliber caliber,
            Vector3 position, Vector3 direction, float speed, int pelletIndex)
        {
            Index = index;
            WeaponId = weaponId;
            Kind = kind;
            Caliber = caliber;
            Position = position;
            Direction = direction;
            Speed = speed;
            PelletIndex = pelletIndex;
        }
    }

    /// <summary>WP-069 ADR-069-1: a projectile touched something (world surface or a body).</summary>
    public readonly struct ProjectileImpactInfo
    {
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

        public ProjectileImpactInfo(ClassicWeaponId weaponId, ProjectileKind kind, WeaponCaliber caliber, Vector3 position,
            Vector3 normal, Vector3 direction, SurfaceKind surface, float speed, bool flesh, bool armor, bool killed,
            bool stopped, int transactionId)
        {
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
    /// </summary>
    public static class ProjectileEvents
    {
        public static event Action<ProjectileSpawnInfo> Spawned;
        public static event Action<ProjectileImpactInfo> Impact;

        /// <summary>A projectile ended without an impact (range / lifetime / capacity eviction). Index, last position.</summary>
        public static event Action<int, Vector3> Expired;

        internal static void RaiseSpawned(in ProjectileSpawnInfo info) => Spawned?.Invoke(info);
        internal static void RaiseImpact(in ProjectileImpactInfo info) => Impact?.Invoke(info);
        internal static void RaiseExpired(int index, Vector3 position) => Expired?.Invoke(index, position);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Spawned = null;
            Impact = null;
            Expired = null;
        }
    }
}
