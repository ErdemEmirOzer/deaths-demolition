using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// One Tide Jewel well, live on the map.
    ///
    /// THE VERB IS GATHER
    /// This weapon does not shoot the crowd, it MOVES the crowd. A well drags everything
    /// inside it toward one point for a couple of seconds and then implodes on the pile.
    /// That makes it the only Special Weapon whose value depends on where the player
    /// stands afterwards rather than on what it kills directly, and it is deliberately
    /// the one that reads as the most "impossible" of the three - which is what the brief
    /// asked a Special Weapon to be.
    ///
    /// HOW THE PULL WORKS WITHOUT ONE LINE OF ENEMY CODE
    /// The enemy's hit reaction already turns a heavy impulse into a shove along
    /// <c>DamageHit.Direction</c>. A shotgun points that vector away from the muzzle; this
    /// points it at the well. Same contract, opposite sign, zero new coupling - and it
    /// means the pull automatically obeys every rule the shove already obeys: tier
    /// susceptibility, the REQ-124 stagger cooldown, the knockback speed ceiling. A
    /// bespoke "drag the NavMeshAgent" implementation would have had to re-earn all four
    /// and would have owned a stun-lock bug.
    ///
    /// WHY THE PULL INTERVAL IS 0.6 s AND NOT 0.1 s
    /// The enemy refuses a second stagger inside its 1.1 s cooldown, so a faster pulse
    /// would not pull harder - it would only spend feedback budget and damage. 0.6 s
    /// means roughly every second tick actually moves a given body, and the ticks that do
    /// not still land their damage. The well therefore costs the crowd budget about a
    /// third of what the old rotary cannon did per second.
    /// </summary>
    public sealed class TideWell : PooledBehaviour
    {
        private static readonly List<TideWell> s_live = new List<TideWell>(4);
        private static readonly List<DamageTarget> s_scratch = new List<DamageTarget>(32);
        private static PrototypePool<VortexEffect> s_vortexPool;

        private VortexEffect _vortex;

        private float _radius = 7f;
        private float _duration = 2.2f;
        private float _remaining;
        private float _pullInterval = 0.6f;
        private float _pullCooldown;
        private float _pullDamage = 6f;
        private float _pullForce = 5.2f;
        private float _implosionDamage = 90f;
        private bool _running;

        /// <summary>Wells open right now. The weapon checks this before opening another.</summary>
        public static int LiveCount => s_live.Count;

        /// <summary>Pull ticks applied since the last <see cref="CloseAll"/>. Test hook.</summary>
        public static int TotalPullTicks { get; private set; }

        /// <summary>WP-056: pull ticks of this well (the FieldEnded count).</summary>
        private int _pullsThisWell;

        /// <summary>The pooled template, built from primitives like every other visual here.</summary>
        public static TideWell CreateTemplate()
        {
            var go = new GameObject("TideWell");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.NotEditable;
            return go.AddComponent<TideWell>();
        }

        /// <summary>Open a well here.</summary>
        public void Begin(TideJewelSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _radius = Mathf.Max(0.5f, settings.WellRadius);
            _duration = Mathf.Max(0.2f, settings.WellSeconds);
            _remaining = _duration;
            _pullInterval = Mathf.Max(0.05f, settings.PullInterval);

            // First pull lands immediately, so the well is never a second of nothing.
            _pullCooldown = 0f;

            _pullDamage = Mathf.Max(0f, settings.PullDamage);
            _pullForce = Mathf.Max(0f, settings.Common.ImpactForce);
            _implosionDamage = Mathf.Max(0f, settings.ImplosionDamage);
            _running = true;

            AttachVortex();

            AudioEvents.RaiseLoop(CombatAudioEvent.SpecialWeaponBeaconLoop, transform.position, 0.5f, 55);
            _pullsThisWell = 0;
            // WP-056: the well is the jewel's field (S-03 pull lean, S-07 sw_tide_well); Seconds = time to the implosion.
            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.TideJewel, SpecialUsageKind.FieldSpawned,
                SpecialWeaponUsage.For(SpecialWeaponId.TideJewel).FieldVfxFamily, transform.position, Vector3.up, _radius, _duration, 0);
        }

        /// <summary>Close every open well without imploding it. Run teardown and activation end.</summary>
        public static void CloseAll()
        {
            for (int i = s_live.Count - 1; i >= 0; i--)
            {
                TideWell well = s_live[i];
                if (well == null)
                {
                    continue;
                }

                well._running = false;
                well.TearDown();
                well.Despawn();
            }

            s_live.Clear();
            TotalPullTicks = 0;
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            if (!s_live.Contains(this))
            {
                s_live.Add(this);
            }
        }

        protected override void OnDespawned()
        {
            s_live.Remove(this);

            // The vortex is released before Despawn is called, not here: the pool contract
            // forbids releasing another pooled object inside OnDespawned. This is only a
            // safety net for a teardown that skipped the normal path.
            _vortex = null;
            _running = false;
        }

        private void Update()
        {
            if (!IsSpawned || !_running)
            {
                return;
            }

            // Scaled time: a paused game must not drain a well.
            float dt = Time.deltaTime;

            _remaining -= dt;
            _pullCooldown -= dt;

            if (_vortex != null)
            {
                _vortex.SetCharge(_duration > 0f ? 1f - Mathf.Clamp01(_remaining / _duration) : 1f);
            }

            if (_pullCooldown <= 0f)
            {
                _pullCooldown += _pullInterval;
                Pull();
            }

            if (_remaining <= 0f)
            {
                Implode();
            }
        }

        private void Pull()
        {
            Vector3 centre = transform.position;

            int count = HitscanEngine.OverlapEnemies(centre, _radius, s_scratch);
            if (count == 0)
            {
                return;
            }

            // WP-007 (DT-1/3): one pull tick is ONE attack - every body in the well shares
            // one transaction id this tick; the next tick is a new attack, so a body pulled
            // every tick is never refused by the receiver's same-transaction guard.
            using (HitscanEngine.BeginExternalAttack())
            {
                for (int i = 0; i < count; i++)
                {
                    DamageTarget target = s_scratch[i];
                    if (!target.IsValid)
                    {
                        continue;
                    }

                    Vector3 point = HitscanEngine.CentreOf(target);

                    Vector3 inward = centre - point;
                    inward.y = 0f;

                    if (inward.sqrMagnitude < 0.0001f)
                    {
                        // Already standing on the core. Nothing to pull; the implosion will
                        // deal with it.
                        continue;
                    }

                    TotalPullTicks++;
                    _pullsThisWell++;

                    HitscanEngine.ApplyExternalHit(
                        target,
                        _pullDamage,
                        point,
                        inward.normalized,
                        _pullForce,
                        DamageSourceKind.Explosion,
                        WeaponPower.Heavy);
                    // WP-056 · targeted Hit for S-03 (pull lean toward the well): direction = inward, seconds = time left to the implosion.
                    SpecialWeaponUsageEvents.PublishHit(SpecialWeaponId.TideJewel, SpecialWeaponUsage.For(SpecialWeaponId.TideJewel).FieldVfxFamily,
                        target, point, inward.normalized, _pullForce, 1f, Mathf.Max(0f, _remaining));
                }
            }
        }

        private void Implode()
        {
            _running = false;

            Vector3 centre = transform.position;

            TearDown();

            // Through the shared blast path on purpose: it owns the three-region armour
            // split, the explosion feedback, the camera rumble and the hit stop, and none
            // of that should be re-implemented by a weapon.
            HitscanEngine.Explode(centre, _implosionDamage, _radius);
            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.TideJewel, SpecialUsageKind.FieldEnded,
                SpecialWeaponUsage.For(SpecialWeaponId.TideJewel).FieldVfxFamily, centre, Vector3.up, _radius, 0f, _pullsThisWell);

            Despawn();
        }

        private void TearDown()
        {
            AudioEvents.StopLoop(CombatAudioEvent.SpecialWeaponBeaconLoop, transform.position);
            ReleaseVortex();
        }

        private void AttachVortex()
        {
            if (s_vortexPool == null)
            {
                VortexEffect template = FeedbackEffectFactory.VortexTemplate;
                if (template == null)
                {
                    return;
                }

                // Capacity 4 against a shipped ceiling of 2 live wells: generous enough
                // that an F1 edit to MaxWells cannot silently produce an invisible well.
                s_vortexPool = PoolService.GetOrCreate(template, 2, 4, PoolExhaustedPolicy.RecycleOldest);
            }

            if (s_vortexPool == null)
            {
                return;
            }

            _vortex = s_vortexPool.Get(transform.position, Quaternion.identity, transform);
            if (_vortex != null)
            {
                _vortex.Begin(_radius, HitFeedbackDirector.Instance.TideColor);
            }
        }

        private void ReleaseVortex()
        {
            if (_vortex == null)
            {
                return;
            }

            _vortex.Despawn();
            _vortex = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_live.Clear();
            s_scratch.Clear();
            s_vortexPool = null;
            TotalPullTicks = 0;
        }
    }
}
