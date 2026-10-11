// WP-104 B1b · NEUTRAL STUB of the B2 tree hooks (S-04, 12.10; S-02's call sites build on it on main). Same public types and
// signatures as the B2 bodies, every hook returns the shipped value (×1, +0, off, the input unchanged) and touches no random state.
// The S-04 B2 commit replaces this file's contents in place (same path, same GUID).
using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using Unity.Profiling;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    [Flags]
    public enum TreeFeelOverride : byte
    {
        None = 0,
        NoHeavyFireShake = 1 << 0,
        NoHitStop = 1 << 1,
        LightBlastShake = 1 << 2
    }

    public enum TreeHitOutcome : byte
    {
        None = 0,
        Invulnerable = 1,
        Ghost = 2,
        Dodged = 3,
        ShieldAbsorbed = 4,
        ShieldBroken = 5,
        Undying = 6
    }

    public struct TreeHitResult
    {
        public float Damage;
        public float Absorbed;
        public float HealthFloor;
        public TreeHitOutcome Outcome;
    }

    public struct TreeShieldState
    {
        public float Shield;
        public float LastHitTime;
        public float GhostUntil;
        public float InvulnerableUntil;
        public float BulwarkReadyAt;
        public uint Rng;

        public static TreeShieldState Create(uint seed) => new TreeShieldState { Rng = seed, LastHitTime = float.NegativeInfinity };
    }

    public struct TreeVolleyPlan
    {
        public int Count;
        public int PelletsPerVolley;
        public float ExtraDamage;
        public float ArcDegrees;
        public float MergedDamageScale;
        public float MergedSizeScale;

        public int TotalShots => Count * PelletsPerVolley;
        public bool Merged => MergedDamageScale > 1f + 1e-4f;
        public float DamageScale(int volley) => (volley <= 0 ? 1f : ExtraDamage) * MergedDamageScale;
        public float YawDegrees(int volley) => 0f;

        public static TreeVolleyPlan Single(int pellets) => new TreeVolleyPlan
        {
            Count = 1, PelletsPerVolley = Math.Max(1, pellets), ExtraDamage = 0f, ArcDegrees = 0f, MergedDamageScale = 1f, MergedSizeScale = 1f
        };
    }

    public static partial class TreeHooks
    {
        public static readonly ProfilerMarker MagazineMarker = new ProfilerMarker("WP104.Tree.Magazine");
        public const float PelletFanShare = 1.6f;
        public const float OverclockPeriodSeconds = 0.5f;
        public const float LeechWindowSeconds = 1f;
        public const float MergedSizeMax = 1.25f;

        private static TreeWeaponProfile s_inHand;

        public static event Action<TreeWeaponProfile> Equipping;
        public static ref readonly TreeWeaponProfile InHand => ref s_inHand;
        public static int EndlessKillCount => 0;
        public static int PullsSinceCrit => 0;
        public static int UndyingUsedThisRound => 0;

        public static void OnClassicEquipping(in TreeWeaponProfile weapon)
        {
            s_inHand = weapon;
            Equipping?.Invoke(weapon);
        }

        // shot
        public static void PlanVolley(in TreeWeaponProfile weapon, int pellets, out TreeVolleyPlan plan) => plan = TreeVolleyPlan.Single(pellets);
        public static void PlanVolley(in TreeWeaponProfile weapon, int pellets, float pullsPerSecond, out TreeVolleyPlan plan) => plan = TreeVolleyPlan.Single(pellets);
        public static int VolleyCount(in TreeWeaponProfile weapon) => 1;
        public static float VolleyYawDegrees(int i, int n) => 0f;
        public static float VolleyDamageScale(int i) => 1f;
        public static float ProjectileSizeScale => 1f;
        public static int PierceCount(int baseCount) => baseCount;
        public static float PierceKeep(float baseKeep) => baseKeep;
        public static int RicochetBounces(int baseBounces) => baseBounces;
        public static float RicochetChance(float baseChance) => baseChance;
        public static bool VolleyCarriesRicochet(int volley) => volley <= 0;
        public static float RicochetRadius(float baseRadius) => baseRadius;
        public static float RicochetKeep(float baseKeep) => baseKeep;
        public static float SeekDegrees => 0f;

        public static bool ExplosiveRound(out float radius, out float areaShare)
        {
            radius = 0f;
            areaShare = 0f;
            return false;
        }

        public static float OwnBlastRadiusScale => 1f;
        public static float ShotDamageScale(in TreeWeaponProfile weapon, float heldSeconds, float now, int carriedGold, int ammoLeft, int magazineSize) => 1f;
        public static WeaponFireMode FireMode(WeaponFireMode definition) => definition;
        public static bool IsConverted(in TreeWeaponProfile weapon) => false;
        public static TreeFeelOverride PowerOverride(in TreeWeaponProfile weapon) => TreeFeelOverride.None;
        public static WeaponPower ShotPower(WeaponPower power, in TreeWeaponProfile weapon) => power;
        public static bool RocketSelfImmune(in TreeWeaponProfile weapon) => false;
        public static float FireRateScale(in TreeWeaponProfile weapon, float heldSeconds, float now) => 1f;
        public static uint SeedFor(int runId, uint salt) => 0x9E3779B9u;

        public static bool RollCrit(ref uint rngState, out float multiplier)
        {
            multiplier = 1f;
            return false;
        }

        // magazine
        public static int MagazineSize(int baseSize, in TreeWeaponProfile weapon) => baseSize;
        public static float ReserveScale(int baseMagazine, in TreeWeaponProfile weapon) => 1f;
        public static int ReserveAllowance(int baseReserve, in TreeWeaponProfile weapon) => baseReserve;
        public static float ReloadTimeScale(in TreeWeaponProfile weapon, float baseSeconds) => 1f;
        public static bool ReloadMoveFree => false;

        public static int AutoReloadTick(float dt, float sinceShot, int missing, int magazineSize, ref float carry)
        {
            carry = 0f;
            return 0;
        }

        public static bool NoReloadWindow(float now) => false;
        public static bool TwinFreeRound(int shotSerial) => false;
        public static bool ConsumesAmmo(int shotSerial, float now) => true;
        public static void OnKill(float now, Vector3 point = default) { }
        public static void Tick(float now) { }

        // survival
        public static float MaxHealthScale => 1f;
        public static float RegenDelayScale => 1f;
        public static float SinceKill(float now) => float.PositiveInfinity;
        public static float MoveScale(float now) => 1f;
        public static float FiringMoveFactor(float weaponFactor) => weaponFactor;
        public static float ShieldMax(float maxHealth) => 0f;
        public static void ShieldFill(float maxHealth, ref TreeShieldState state) => state.Shield = 0f;
        public static void ShieldTick(float dt, float now, float maxHealth, ref TreeShieldState state) => state.Shield = 0f;

        public static TreeHitResult IncomingDamage(float damage, float now, float health, float maxHealth, ref TreeShieldState state, Vector3 point = default) =>
            new TreeHitResult { Damage = Mathf.Max(0f, damage) };

        public static float OnKillHeal(EnemyTier tier, float now, Vector3 point = default) => 0f;
        public static void OnRoundStarted(int round = 0) { }

        // economy (WP-107)
        public static int WallBuyPrice(int basePrice) => basePrice;

        public static int AmmoPrice(int basePrice, float refillShare) =>
            basePrice <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(basePrice * (double)Mathf.Clamp01(refillShare) - 1e-3));

        /// <summary>B2: S-02 calls it on every fired shot (Overclock's own 1 s release clock).</summary>
        public static void OnShotFired(float now) { }

        /// <summary>B2: Coin Sense III magnet radius scale (gold orb pull).</summary>
        public static float MagnetRadiusScale => 1f;

        // run boundary
        public static void ResetRunState() { }
        public static void EnsureHooked() { }
    }
}
