using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using DamageTarget = DD.Prototype.Weapons.DamageTarget;

namespace DD.Prototype.Rounds
{
    /// <summary>WP-077 K10b · one burning-ground zone as the pool publishes it (VFX / audio consumers). Readonly, no allocation.</summary>
    public readonly struct GroundFireInfo
    {
        public readonly int Id;
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly float DamagePerSecond;
        public readonly float Seconds;
        public readonly float SecondsLeft;

        public GroundFireInfo(int id, Vector3 center, float radius, float damagePerSecond, float seconds, float secondsLeft)
        {
            Id = id; Center = center; Radius = radius; DamagePerSecond = damagePerSecond; Seconds = seconds; SecondsLeft = secondsLeft;
        }
    }

    /// <summary>
    /// WP-077 K10b · burning ground (ks.explosion.ashcloud "Ash Cloud", seal.rl_1 "Hibana"). A fixed pool of
    /// <see cref="Capacity"/> zones (centre, radius, damage per second, lifetime), advanced once a frame by
    /// <see cref="ProjectileSystem.Tick"/> (the same scaled clock as the projectiles and the Specials). Every
    /// <see cref="TickSeconds"/> a zone burns each enemy inside its sphere once for dps × <see cref="TickSeconds"/>, in one
    /// damage transaction per zone tick, through the explosion receiver (the region split: a plate absorbs its share).
    /// A tick carries <see cref="DamageHitContext.GroundFire"/>, so the body takes it with a chain's weak force (no
    /// stagger lock from standing in fire). The first tick lands <see cref="TickSeconds"/> after ignition (the blast
    /// itself already hit), so a 2 s zone burns 4 times and a 3 s zone 6 times.
    ///
    /// Full pool: the zone with the least time left is replaced (counted, <see cref="Replaced"/>); an ignition during a
    /// tick into a full pool is dropped (<see cref="Dropped"/>). Cleared (with <see cref="Extinguished"/> per zone) on
    /// run start / end and on every scene change. No allocation per ignition or tick.
    /// </summary>
    public static class RunTreeGroundFire
    {
        [PrototypeOnly(PendingDecisions.None, "WP-077 K10b burning ground: pool size (a rocket volley with Ash Cloud + Hibana is 2 zones per rocket) and the damage tick (s).", true)]
        public const int Capacity = 16;
        public const float TickSeconds = 0.5f;

        private const float Epsilon = 1e-4f;

        private struct Zone
        {
            public int Id;
            public Vector3 Center;
            public float Radius, DamagePerSecond, Seconds, Elapsed, NextTick;
            public bool FromWeapon;
            public ClassicWeaponId Weapon;
        }

        private static readonly Zone[] s_zones = new Zone[Capacity];
        private static readonly List<DamageTarget> s_targets = new List<DamageTarget>(64);
        private static int s_count;
        private static int s_nextId;
        private static bool s_ticking;

        /// <summary>A zone started burning (S-07 VFX / S-08 audio: ground fire at Center, Radius, for Seconds).</summary>
        public static event Action<GroundFireInfo> Ignited;

        /// <summary>A zone went out (expired, replaced or cleared); the id of <see cref="Ignited"/>.</summary>
        public static event Action<int> Extinguished;

        /// <summary>Live zones.</summary>
        public static int Count => s_count;

        /// <summary>Zone damage ticks run since the domain reset (tests / F1).</summary>
        public static int BurnTicks { get; private set; }

        /// <summary>Bodies a tick damaged (tests / F1).</summary>
        public static int BodiesBurned { get; private set; }

        /// <summary>Zones pushed out by an ignition into a full pool.</summary>
        public static int Replaced { get; private set; }

        /// <summary>Ignitions lost because the pool was full during a tick.</summary>
        public static int Dropped { get; private set; }

        /// <summary>Zone <paramref name="i"/> (0 ≤ i &lt; <see cref="Count"/>).</summary>
        public static GroundFireInfo Get(int i)
        {
            if (i < 0 || i >= s_count) return default;
            Zone z = s_zones[i];
            return new GroundFireInfo(z.Id, z.Center, z.Radius, z.DamagePerSecond, z.Seconds, Mathf.Max(0f, z.Seconds - z.Elapsed));
        }

        /// <summary>Starts a zone; returns its id (0 = nothing burns: a non-positive radius, dps or time, or a full pool mid-tick).</summary>
        public static int Ignite(Vector3 center, float radius, float damagePerSecond, float seconds, bool fromWeapon = false, ClassicWeaponId weapon = default)
        {
            if (radius <= 0f || damagePerSecond <= 0f || seconds <= 0f) return 0;

            int index;
            if (s_count < Capacity)
            {
                index = s_count++;
            }
            else if (s_ticking)
            {
                Dropped++;
                return 0;
            }
            else
            {
                index = IndexOfSoonest();
                int old = s_zones[index].Id;
                Replaced++;
                Raise(Extinguished, old);
            }

            s_zones[index] = new Zone
            {
                Id = ++s_nextId, Center = center, Radius = radius, DamagePerSecond = damagePerSecond, Seconds = seconds,
                Elapsed = 0f, NextTick = TickSeconds, FromWeapon = fromWeapon, Weapon = weapon
            };

            if (Ignited != null)
            {
                try { Ignited(new GroundFireInfo(s_nextId, center, radius, damagePerSecond, seconds, seconds)); }
                catch (Exception e) { Debug.LogException(e); }
            }

            return s_nextId;
        }

        /// <summary>Advances every zone by <paramref name="deltaTime"/>: due ticks burn, spent zones go out.</summary>
        public static void Tick(float deltaTime)
        {
            if (s_count == 0 || deltaTime <= 0f) return;

            s_ticking = true;
            try
            {
                for (int i = s_count - 1; i >= 0; i--)
                {
                    if (i >= s_count) continue;   // a burn's listener cleared the pool
                    s_zones[i].Elapsed += deltaTime;
                    while (i < s_count && s_zones[i].NextTick <= s_zones[i].Elapsed + Epsilon && s_zones[i].NextTick <= s_zones[i].Seconds + Epsilon)
                    {
                        Zone z = s_zones[i];
                        s_zones[i].NextTick += TickSeconds;
                        Burn(in z);
                    }

                    if (i < s_count && s_zones[i].Elapsed >= s_zones[i].Seconds - Epsilon) RemoveAt(i);
                }
            }
            finally
            {
                s_ticking = false;
            }
        }

        /// <summary>Puts every zone out (run start / end, scene change, tests).</summary>
        public static void Clear()
        {
            int n = s_count;
            s_count = 0;
            for (int i = 0; i < n; i++)
            {
                int id = s_zones[i].Id;
                s_zones[i] = default;
                Raise(Extinguished, id);
            }
        }

        // ------------------------------------------------------------------ burn

        private static void Burn(in Zone z)
        {
            BurnTicks++;
            float amount = z.DamagePerSecond * TickSeconds;
            int group = 0;

            int transaction = DamageTransaction.NextId();
            DamageTransaction.Resume(transaction);
            try
            {
                HitscanEngine.OverlapEnemies(z.Center, z.Radius, s_targets);
                for (int i = 0; i < s_targets.Count; i++)
                {
                    DamageTarget target = s_targets[i];
                    if (target.Explosive == null || target.Root == null) continue;

                    DamageHitContext.Set(1f, 1f, z.FromWeapon, z.Weapon, null);
                    DamageHitContext.MarkGroundFire();
                    DamageResult r;
                    try
                    {
                        r = target.Explosive.ApplyExplosion(amount, z.Center);
                    }
                    finally
                    {
                        DamageHitContext.Clear();
                    }

                    if (DamageResults.IsEmpty(in r)) continue;
                    BodiesBurned++;

                    // A tick is a damage-over-time: it is dressed only when it kills or breaks a plate (the death / break
                    // dressing lives in the feedback director), never per tick per body (REQ-128 readability budget).
                    if (!r.Killed && !r.ArmorBroke) continue;
                    if (group == 0) group = FeelEventRouter.NextGroupId();
                    Vector3 point = HitscanEngine.CentreOf(target);
                    Vector3 away = point - z.Center;
                    away.y = 0f;
                    Vector3 dir = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.up;
                    HitFeedbackDirector.Instance.ReportEnemyHit(new EnemyHitFeedbackInfo
                    {
                        Point = point, Direction = dir, Normal = dir, Result = r, Power = WeaponPower.Light, TargetId = target.Id,
                        TargetRoot = target.Root, Weight = target.Weight, ImpactForce = 0.35f, FromExplosion = true,
                        PresentationGroupId = group, SnapshotRevision = FeelRuntime.Current.Revision
                    });
                }
            }
            finally
            {
                s_targets.Clear();
                DamageTransaction.End();
            }
        }

        // ------------------------------------------------------------------ pool bookkeeping

        private static int IndexOfSoonest()
        {
            int best = 0;
            for (int i = 1; i < s_count; i++)
            {
                if (s_zones[i].Seconds - s_zones[i].Elapsed < s_zones[best].Seconds - s_zones[best].Elapsed) best = i;
            }

            return best;
        }

        private static void RemoveAt(int index)
        {
            if (index < 0 || index >= s_count) return;
            int id = s_zones[index].Id;
            s_zones[index] = s_zones[--s_count];
            s_zones[s_count] = default;
            Raise(Extinguished, id);
        }

        private static void Raise(Action<int> handler, int id)
        {
            if (handler == null) return;
            try { handler(id); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ install

        private static void OnRunStarted(int run) => Clear();

        private static void OnSceneChanged(Scene from, Scene to)
        {
            Clear();
            EnsureHooked();
        }

        /// <summary>Idempotent (remove-then-add): a bus a test reset stays wired, never two copies.</summary>
        public static void EnsureHooked()
        {
            SpecialWeaponRoundBridge.RunStarted -= OnRunStarted;
            SpecialWeaponRoundBridge.RunStarted += OnRunStarted;
            SpecialWeaponRoundBridge.RunEnded -= Clear;
            SpecialWeaponRoundBridge.RunEnded += Clear;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            EnsureHooked();
            // Re-arm on every active-scene change (static bus rearm lesson) and put the fire out with the old scene.
            SceneManager.activeSceneChanged -= OnSceneChanged;
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < Capacity; i++) s_zones[i] = default;
            s_count = 0;
            s_nextId = 0;
            s_ticking = false;
            s_targets.Clear();
            BurnTicks = 0;
            BodiesBurned = 0;
            Replaced = 0;
            Dropped = 0;
            Ignited = null;
            Extinguished = null;
        }
    }
}
