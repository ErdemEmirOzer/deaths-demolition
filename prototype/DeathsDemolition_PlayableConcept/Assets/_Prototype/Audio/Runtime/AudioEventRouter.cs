using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>The director's single funnel, as a delegate (the <c>in</c> parameter rules out Func).</summary>
    public delegate bool AudioPlayFunc(in AudioPlacement placement, Vector3 position, float gainScale, int cuePriority);

    /// <summary>
    /// WP-021 · The listeners the WP-002 director did not have: the zombie VOCAL set
    /// (DEC-067) from the damage contract and from the attack state machine.
    ///
    ///   EnemyDamaged  → pain grunt, throttled per enemy life (a shotgun's nine pellets
    ///                   are one grunt, not nine)
    ///   EnemyDied     → the death vocal, once per life, over the body cue
    ///   attack WINDUP → the telegraph growl. No event exists for it, so this polls
    ///                   <see cref="ZombieEnemy.AttackPhase"/> on the live list and fires
    ///                   on the None/Recovery → Windup edge. The telegraph is 0.55 s
    ///                   (EnemyTierSettings), so the growl lands ≥ 0.3 s before the swing
    ///                   by construction; it is positional (REQ-104 "yön + tehdit").
    ///   attack ACTIVE → the swing, on the Windup → Active edge.
    ///
    /// Polling forty enemies' enum once a frame is a few hundred instructions and no
    /// allocation; it keeps this package out of <c>Enemies/</c> entirely and it goes away
    /// the day WP-018's animation events (ADR-018-1 hit_window) reach main - see the
    /// reserved rows in <see cref="AudioEventMap"/>.
    ///
    /// Everything is routed through the director's single funnel so the budget, the
    /// bus, the duck and the mixer group are the same as for every other sound.
    /// </summary>
    public sealed class AudioEventRouter
    {
        /// <summary>Seconds between pain grunts from the same enemy. PROTOTYPE-ONLY / INVENTED.</summary>
        public const float HitThrottleSeconds = 0.35f;

        private struct Tracked
        {
            public ZombieAttackPhase Phase;
            public float LastHit;
        }

        private readonly AudioPlayFunc _play;
        private readonly Dictionary<ZombieEnemy, Tracked> _tracked = new Dictionary<ZombieEnemy, Tracked>(48);
        private readonly List<ZombieEnemy> _stale = new List<ZombieEnemy>(16);
        private int _sweep;

        /// <summary>Windup cues raised. Test/F1 readout.</summary>
        public int WindupCount { get; private set; }
        public int AttackCount { get; private set; }
        public int HitCount { get; private set; }
        public int DeathCount { get; private set; }

        /// <summary>Hits swallowed by the per-enemy throttle. Test readout.</summary>
        public int HitThrottled { get; private set; }

        public bool Subscribed { get; private set; }

        public AudioEventRouter(AudioPlayFunc play)
        {
            _play = play;
        }

        public void Subscribe()
        {
            if (Subscribed)
            {
                return;
            }

            DamageEvents.EnemyDamaged += OnEnemyDamaged;
            DamageEvents.EnemyDied += OnEnemyDied;
            Subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!Subscribed)
            {
                return;
            }

            DamageEvents.EnemyDamaged -= OnEnemyDamaged;
            DamageEvents.EnemyDied -= OnEnemyDied;
            Subscribed = false;
        }

        public void Clear()
        {
            _tracked.Clear();
        }

        // ------------------------------------------------------------------ events

        private void OnEnemyDamaged(EnemyDamageInfo info)
        {
            if (info.Result.Killed || info.Result.HealthDamage <= 0f && info.Result.ArmorDamage <= 0f)
            {
                return; // the death vocal owns a killing blow; an empty result is nothing
            }

            ZombieEnemy z = info.Owner != null ? info.Owner.GetComponent<ZombieEnemy>() : null;
            float now = Time.unscaledTime;
            if (z != null)
            {
                if (_tracked.TryGetValue(z, out Tracked t) && now - t.LastHit < HitThrottleSeconds)
                {
                    HitThrottled++;
                    return;
                }

                t.LastHit = now;
                _tracked[z] = t;
            }

            EnemyTierWeight weight = WeightOf(info.Tier);
            if (_play(AudioMixPlan.ZombieHit(AudioEventMap.ZombieHit(info.Tier), weight), info.Point, 1f, 30))
            {
                HitCount++;
            }
        }

        private void OnEnemyDied(EnemyDeathInfo info)
        {
            EnemyTierWeight weight = WeightOf(info.Tier);
            if (_play(AudioMixPlan.ZombieDeath(AudioEventMap.ZombieDeath(info.Tier), weight), info.Position, 1f, 70))
            {
                DeathCount++;
            }

            ZombieEnemy z = info.Owner != null ? info.Owner.GetComponent<ZombieEnemy>() : null;
            if (z != null)
            {
                _tracked.Remove(z);
            }
        }

        // ------------------------------------------------------------------ polling

        /// <summary>Once per frame from the director. Detects attack-phase edges on the live list.</summary>
        public void Tick()
        {
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                ZombieEnemy z = live[i];
                if (z == null || !z.IsAlive)
                {
                    continue;
                }

                ZombieAttackPhase phase = z.AttackPhase;
                _tracked.TryGetValue(z, out Tracked t);
                if (phase == t.Phase)
                {
                    continue;
                }

                if (phase == ZombieAttackPhase.Windup && t.Phase != ZombieAttackPhase.Windup)
                {
                    Vector3 at = z.transform.position + Vector3.up * 1.4f;
                    if (_play(AudioMixPlan.ZombieWindup(AudioEventMap.ZombieWindup(z.Tier), WeightOf(z.Tier)), at, 1f, 60))
                    {
                        WindupCount++;
                    }
                }
                else if (phase == ZombieAttackPhase.Active && t.Phase == ZombieAttackPhase.Windup)
                {
                    Vector3 at = z.transform.position + Vector3.up * 1.4f;
                    if (_play(AudioMixPlan.ZombieAttack(AudioEventMap.ZombieAttack(z.Tier), WeightOf(z.Tier)), at, 1f, 50))
                    {
                        AttackCount++;
                    }
                }

                t.Phase = phase;
                _tracked[z] = t;
            }

            // Forget pooled-out bodies a few at a time so the dictionary never grows past
            // the pool and the sweep never costs a frame.
            if (_tracked.Count > live.Count && (++_sweep & 63) == 0)
            {
                _stale.Clear();
                foreach (KeyValuePair<ZombieEnemy, Tracked> pair in _tracked)
                {
                    if (pair.Key == null || !pair.Key.IsAlive)
                    {
                        _stale.Add(pair.Key);
                    }
                }

                for (int i = 0; i < _stale.Count; i++)
                {
                    _tracked.Remove(_stale[i]);
                }
            }
        }

        public static EnemyTierWeight WeightOf(EnemyTier tier)
        {
            return tier == EnemyTier.Elite || tier == EnemyTier.Ultra ? EnemyTierWeight.Heavy : EnemyTierWeight.Light;
        }

        public string Describe()
        {
            return string.Format("windup {0} · attack {1} · hit {2} (throttled {3}) · death {4} · tracked {5}",
                WindupCount, AttackCount, HitCount, HitThrottled, DeathCount, _tracked.Count);
        }
    }
}
