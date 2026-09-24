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

        /// <summary>The displacement this frame adds on top of locomotion (knockback ease-out + suppression push); decays the state.</summary>
        private Vector3 TickHitResponse(float dt)
        {
            Vector3 v = Vector3.zero;
            if (_kbTime >= 0f && _kbTime < _kbProfile.Seconds)
            {
                v += _kbDirection * _kbProfile.SpeedAt(_kbTime);
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
