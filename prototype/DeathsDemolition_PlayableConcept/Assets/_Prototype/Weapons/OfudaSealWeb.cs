using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The Ofuda Loom's web, live.
    ///
    /// THE VERB IS BIND
    /// Every other weapon here spends its ammunition on the enemy in front of the cursor.
    /// This one spends it on a POSITION IN THE FUTURE: a talisman does almost nothing on
    /// its own (9 damage against 34 base health), and the payoff only arrives when the
    /// player decides the net is big enough and fires into it. That makes it the only
    /// weapon in the prototype whose skill is target SELECTION rather than target
    /// tracking - which is exactly the kind of verb an eleven-second power fantasy should
    /// have, because it asks the player to plan under pressure instead of holding a
    /// button harder.
    ///
    /// WHY A COLLAPSE NEEDS A MINIMUM
    /// The loom fires five times a second into a crowd of forty. Without a floor, the
    /// first accidental second hit on an already-sealed body would spend a one-seal web
    /// and the mechanic would read as broken rather than as demanding. So a hit on a
    /// sealed enemy below <see cref="OfudaLoomSettings.MinSealsToCollapse"/> refreshes
    /// that seal instead, and a seal cannot take part in a collapse for
    /// <see cref="OfudaLoomSettings.SealArmSeconds"/> after it lands. The mechanic is
    /// then only ever spent on purpose.
    ///
    /// WHY POOLED ENEMIES CANNOT CORRUPT IT
    /// A sealed enemy can die and go back to its pool while the seal is still live. The
    /// entry is validated on every use - transform alive AND active in the hierarchy -
    /// and a failed validation drops the entry silently. There is no path where a
    /// recycled body inherits somebody else's talisman.
    /// </summary>
    public sealed class OfudaSealWeb
    {
        private struct Seal
        {
            public int TargetId;
            public DamageTarget Target;
            public SealGlyphEffect Glyph;
            public float Remaining;
            public float ArmingRemaining;

            public bool Armed => ArmingRemaining <= 0f;
        }

        private readonly List<Seal> _seals = new List<Seal>(16);
        private readonly List<Vector3> _points = new List<Vector3>(16);

        /// <summary>Seals currently on bodies. F1 readout and HUD hook.</summary>
        public int SealCount => _seals.Count;

        /// <summary>Seals old enough to take part in a collapse.</summary>
        public int ArmedSealCount
        {
            get
            {
                int armed = 0;
                for (int i = 0; i < _seals.Count; i++)
                {
                    if (_seals[i].Armed)
                    {
                        armed++;
                    }
                }

                return armed;
            }
        }

        /// <summary>Collapses since the last <see cref="Clear"/>. Test hook.</summary>
        public int CollapseCount { get; private set; }

        /// <summary>True when this enemy already carries a talisman.</summary>
        public bool IsSealed(int targetId)
        {
            return IndexOf(targetId) >= 0;
        }

        /// <summary>
        /// Put a talisman on a body, or refresh the one already there.
        /// Returns false when the web is full and this body is not already in it.
        /// </summary>
        public bool ApplySeal(in DamageTarget target, OfudaLoomSettings settings)
        {
            if (settings == null || !target.IsValid)
            {
                return false;
            }

            float seconds = Mathf.Max(0.2f, settings.SealSeconds);
            float arming = Mathf.Max(0f, settings.SealArmSeconds);

            int index = IndexOf(target.Id);
            if (index >= 0)
            {
                Seal existing = _seals[index];
                existing.Remaining = seconds;
                existing.Target = target;
                _seals[index] = existing;
                return true;
            }

            if (_seals.Count >= Mathf.Max(1, settings.MaxSeals))
            {
                return false;
            }

            var seal = new Seal
            {
                TargetId = target.Id,
                Target = target,
                Remaining = seconds,
                ArmingRemaining = arming,
                Glyph = HitFeedbackDirector.Instance.ReportSeal(
                    target.Root, target.Height, target.Id, seconds)
            };

            _seals.Add(seal);
            return true;
        }

        /// <summary>
        /// Spend the whole web. Every sealed body takes <paramref name="settings"/>'s
        /// collapse damage at once and every talisman is consumed.
        /// Returns how many bodies were actually hit.
        /// </summary>
        public int Collapse(in OfudaLoomSettings settings)
        {
            if (settings == null || _seals.Count == 0)
            {
                return 0;
            }

            _points.Clear();

            float damage = Mathf.Max(0f, settings.CollapseDamage);
            float impulse = Mathf.Max(0f, settings.CollapseImpactForce);

            for (int i = 0; i < _seals.Count; i++)
            {
                Seal seal = _seals[i];

                if (!IsAlive(seal))
                {
                    ReleaseGlyph(seal);
                    continue;
                }

                _points.Add(HitscanEngine.CentreOf(seal.Target));
            }

            // Draw the net BEFORE the damage. The links have to be on screen on the same
            // frame the bodies react, and a hit that kills can retire a transform the
            // link would otherwise have been drawn from.
            HitFeedbackDirector director = HitFeedbackDirector.Instance;
            for (int i = 1; i < _points.Count; i++)
            {
                director.ReportSealLink(_points[i - 1], _points[i]);
            }

            if (_points.Count > 2)
            {
                // Close the loop, so a web reads as a NET rather than as a chain. This is
                // the one visual difference between this weapon and the Chain upgrade
                // branch, and it is worth one extra link.
                director.ReportSealLink(_points[_points.Count - 1], _points[0]);
            }

            int hit = 0;

            // WP-007 (DT-1/3): the collapse is ONE attack - every sealed body shares one
            // transaction id. Seals are unique per target, so no body is applied twice.
            using (HitscanEngine.BeginExternalAttack())
            {
                for (int i = 0; i < _seals.Count; i++)
                {
                    Seal seal = _seals[i];
                    ReleaseGlyph(seal);

                    if (!IsAlive(seal))
                    {
                        continue;
                    }

                    Vector3 centre = HitscanEngine.CentreOf(seal.Target);

                    // The reaction direction is away from the player-facing side of the body,
                    // approximated as away from the web's own centre of mass. A collapse that
                    // shoved everything the same way would read as a single explosion instead
                    // of as many simultaneous ones.
                    Vector3 direction = centre - WebCentre();
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 0.0001f)
                    {
                        direction = Vector3.forward;
                    }

                    HitscanEngine.ApplyExternalHit(
                        seal.Target, damage, centre, direction, impulse,
                        DamageSourceKind.Chain, WeaponPower.Heavy);

                    hit++;
                }
            }

            _seals.Clear();
            CollapseCount++;

            if (hit > 0)
            {
                AudioEvents.RaiseCritical(CombatAudioEvent.Explosion, _points.Count > 0 ? _points[0] : Vector3.zero, 1f, 75);
                CameraShakeBridge.Shake(0.085f, 0.2f, ShakeProfile.ArmorBreak);
                HitStopService.Request(0.05f, 0.25f);
            }

            return hit;
        }

        /// <summary>Age the web. Call once per frame while the loom is in hand.</summary>
        public void Tick(float deltaTime)
        {
            for (int i = _seals.Count - 1; i >= 0; i--)
            {
                Seal seal = _seals[i];

                if (seal.ArmingRemaining > 0f)
                {
                    seal.ArmingRemaining -= deltaTime;
                }

                seal.Remaining -= deltaTime;

                if (seal.Remaining <= 0f || !IsAlive(seal))
                {
                    ReleaseGlyph(seal);
                    _seals.RemoveAt(i);
                    continue;
                }

                _seals[i] = seal;
            }
        }

        /// <summary>Burn every talisman without spending it. Called when the activation ends.</summary>
        public void Clear()
        {
            for (int i = 0; i < _seals.Count; i++)
            {
                ReleaseGlyph(_seals[i]);
            }

            _seals.Clear();
            CollapseCount = 0;
        }

        private Vector3 WebCentre()
        {
            if (_points.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _points.Count; i++)
            {
                sum += _points[i];
            }

            return sum / _points.Count;
        }

        private int IndexOf(int targetId)
        {
            for (int i = 0; i < _seals.Count; i++)
            {
                if (_seals[i].TargetId == targetId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsAlive(in Seal seal)
        {
            return seal.Target.IsValid
                   && seal.Target.Root != null
                   && seal.Target.Root.gameObject.activeInHierarchy;
        }

        private static void ReleaseGlyph(in Seal seal)
        {
            if (seal.Glyph != null)
            {
                seal.Glyph.Consume();
            }
        }
    }
}
