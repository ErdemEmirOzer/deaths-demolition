using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-076 B3 · the body's side of §3.1/§3.4: S-03 owns the displacement (one writer to the transform, through the
    /// CharacterController with the crowd / NavMesh / out-of-world rules), S-06 reads <see cref="Knockback"/> for the pose.
    /// `-ddAiV3 0` keeps the WP-002 speed-per-excess knockback and no suppression (A/B).
    /// </summary>
    public sealed partial class ZombieEnemy
    {
        private KnockbackProfile _kbProfile;
        private Vector3 _kbDirection;
        private float _kbTime = -1f;

        private float _suppressCounter, _suppressUntil;
        private Vector3 _suppressPush;

        /// <summary>The shove in progress (S-06 reads it every frame for z_hit_knockback / knockback_end).</summary>
        public KnockbackState Knockback
        {
            get
            {
                bool active = _kbTime >= 0f && _kbTime < _kbProfile.Seconds;
                return new KnockbackState
                {
                    Active = active,
                    Direction = _kbDirection,
                    Speed = active ? _kbProfile.SpeedAt(_kbTime) : 0f,
                    Seconds = _kbProfile.Seconds,
                    Progress01 = active && _kbProfile.Seconds > 0f ? _kbTime / _kbProfile.Seconds : (_kbTime >= 0f ? 1f : 0f)
                };
            }
        }

        /// <summary>LMG suppression: the walking-speed factor (0.70 while suppressed, else 1).</summary>
        public float SuppressionFactor => Time.time < _suppressUntil ? HitReactionProfiles.SuppressionSpeedFactor : 1f;
        public bool IsSuppressed => Time.time < _suppressUntil;
        /// <summary>Tests / readout: the current sustained suppression push (m/s).</summary>
        public float SuppressionPushSpeed => _suppressPush.magnitude;

        private void ResetHitResponse()
        {
            _kbTime = -1f; _kbProfile = default; _kbDirection = Vector3.zero;
            _suppressCounter = 0f; _suppressUntil = 0f; _suppressPush = Vector3.zero;
            _treeStaggerAllowedAt = 0f;   // DEC-112: the lock is per life
        }

        /// <summary>Per-class response for one landed, non-lethal hit. Returns the (possibly raised) flinch.</summary>
        private float ApplyWeaponResponse(in DamageHit hit, in HitReaction reaction, Vector3 direction, bool known, WeaponClass weaponClass)
        {
            Vector3 flat = direction; flat.y = 0f;
            if (flat.sqrMagnitude > 1e-4f) flat.Normalize(); else flat = -transform.forward;

            if (!AttackSelector.Enabled)
            {
                if (reaction.KnockbackVelocity != Vector3.zero) _knockbackVelocity += reaction.KnockbackVelocity;   // WP-002 formula
                return reaction.FlinchIntensity;
            }

            if (reaction.KnockbackEarned)
            {
                float scale = HitReactionProfiles.KnockbackTierScale(_tier);
                if (scale > 0f)
                {
                    KnockbackProfile p = HitReactionProfiles.Knockback(weaponClass, known);
                    _kbProfile = new KnockbackProfile(p.Distance * scale, p.Seconds);
                    _kbDirection = flat;
                    _kbTime = 0f;
                }
            }

            if (known && weaponClass == WeaponClass.Lmg)
            {
                float tierScale = HitReactionProfiles.SuppressionTierScale(_tier);
                _suppressCounter += 1f;
                if (_suppressCounter >= HitReactionProfiles.SuppressionThreshold && tierScale > 0f)
                    _suppressUntil = Time.time + HitReactionProfiles.SuppressionHoldSeconds;
                if (tierScale > 0f)
                {
                    _suppressPush += flat * (HitReactionProfiles.SuppressionPushPerHit * tierScale);
                    float cap = HitReactionProfiles.SuppressionPushCap * tierScale;
                    if (_suppressPush.sqrMagnitude > cap * cap) _suppressPush = _suppressPush.normalized * cap;
                }
            }

            if (known && weaponClass == WeaponClass.Sniper && !reaction.Stagger)
                return Mathf.Max(reaction.FlinchIntensity, HitReactionProfiles.SniperHeavyFlinch);
            return reaction.FlinchIntensity;
        }

        /// <summary>
        /// WP-077 cap.sg_1 (Ash Wall): a point-blank shove on this knockback path — <paramref name="metres"/> over
        /// <paramref name="seconds"/> along the shot, the tier scale kept (Elite ×0.5, Ultra never). Replaces a shove in progress.
        /// </summary>
        public void ApplyTreeShove(Vector3 direction, float metres, float seconds)
        {
            if (!_alive || _dying || metres <= 0f || seconds <= 0f) return;
            float scale = HitReactionProfiles.KnockbackTierScale(_tier);
            Vector3 flat = direction; flat.y = 0f;
            if (scale <= 0f || flat.sqrMagnitude < 1e-4f) return;
            flat.Normalize();

            if (!AttackSelector.Enabled)
            {
                _knockbackVelocity += flat * (2f * metres * scale / seconds);   // WP-002 path: the same distance as an initial speed
                return;
            }

            _kbProfile = new KnockbackProfile(metres * scale, seconds);
            _kbDirection = flat;
            _kbTime = 0f;
        }

        [PrototypeOnly(PendingDecisions.None, "DEC-112 (user 30.09): the tree stagger's per-body lock (s) and the Ultra duration factor.", true)]
        public const float TreeStaggerLockSeconds = 1.5f, TreeStaggerUltraFactor = 0.5f;

        /// <summary>Tree staggers started this session (tests / telemetry).</summary>
        public static int TreeStaggers { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTreeStaggerStatics() => TreeStaggers = 0;
        private float _treeStaggerAllowedAt;

        /// <summary>
        /// DEC-112 (user 30.09, S-04 WP-077 Kurogane capstone "a zombie whose armour breaks staggers 0.35 s"): the tree's stagger on
        /// this body through the REQ-124 path — refused while the body is dying, already staggering, inside the REQ-124 stagger lock
        /// or inside this body's <see cref="TreeStaggerLockSeconds"/> lock; the Ultra staggers for <see cref="TreeStaggerUltraFactor"/>
        /// of the seconds. Adds no damage; marked as a tree stagger so stagger-keyed damage bonuses (IsHitStaggering) do not see it.
        /// Starts the REQ-124 lock. Returns true when the stagger started.
        /// </summary>
        public bool ApplyTreeStagger(float seconds, Vector3 direction)
        {
            if (!_alive || _dying || seconds <= 0f || _animator == null) return false;
            if (Time.time < _treeStaggerAllowedAt || _animator.IsStaggering || _reaction.StaggerOnCooldown) return false;
            float duration = _tier == EnemyTier.Ultra ? seconds * TreeStaggerUltraFactor : seconds;
            Vector3 flat = direction; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = -transform.forward;
            _treeStaggerAllowedAt = Time.time + TreeStaggerLockSeconds;
            _reaction.NoteExternalStagger();
            _animator.BeginTreeStagger(flat.normalized, 1f, duration, transform);
            if (_tier == EnemyTier.Special) _animator.PlayStumble();
            _attack.Cancel();
            RaiseBeat(EnemyCombatEventKind.Stagger, transform.position + Vector3.up, flat.normalized, ArmorSlot.None, _animator.Driven, duration);
            TreeStaggers++;
            return true;
        }

        /// <summary>The displacement this frame adds on top of locomotion (knockback ease-out + suppression push); decays the state.</summary>
        private Vector3 TickHitResponse(float dt)
        {
            Vector3 v = Vector3.zero;
            if (_kbTime >= 0f && _kbTime < _kbProfile.Seconds)
            {
                // WP-076 R1-B AI4: move by the eased delta D·(e(t+dt) − e(t)), not v(t)·dt (explicit Euler overshot the 0.6 m shove
                // to 0.72 m at 20 fps and 0.62 m at 144 fps) — the total is exactly Distance at any frame rate.
                float moved = _kbProfile.DistanceAt(_kbTime + dt) - _kbProfile.DistanceAt(_kbTime);
                if (dt > 0f) v += _kbDirection * (moved / dt);
                _kbTime += dt;
            }

            if (_suppressCounter > 0f)
            {
                _suppressCounter *= Mathf.Pow(0.5f, dt / HitReactionProfiles.SuppressionHalfLife);
                if (_suppressCounter < 0.01f) _suppressCounter = 0f;
            }
            if (_suppressPush.sqrMagnitude > 1e-6f)
            {
                v += _suppressPush;
                _suppressPush *= Mathf.Pow(0.5f, dt / HitReactionProfiles.SuppressionPushHalfLife);
            }
            else _suppressPush = Vector3.zero;
            return v;
        }
    }
}
