using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-055 · Special Weapon ↔ enemy reactions v1 (PROTOTYPE-ONLY numbers; the Special Weapon design is
    /// revised together at the end of the project). The weapon keeps its WP-007 damage / armour path; what
    /// it adds here is the BODY reaction, resolved per tier:
    ///
    ///   Kagura Bell  push away from the origin + a short daze (attack cancelled, REQ-124 stagger gate; the WP-032E
    ///                Dazed visual = stumble + sway); resistance Elite ×0.5 / Ultra ×0.2 on both the shove and the daze
    ///   Ofuda Loom   netted for the seal's life: Standard rooted (WP-032E Netted visual = full stop, no swing), Special slowed
    ///                hard (a Runner must keep reading fast), Elite / Ultra slowed less; a slowed body still swings in range
    ///   Tide Jewel   one pull tick toward the core (velocity impulse); the Ultra is not moved but slowed
    ///
    /// Every reaction is one ADR-040-1 beat (sw_push / sw_web / sw_pull) with the weapon id, so S-07's
    /// sw_* VFX slots and S-08's cues bind by the same surface as every other enemy moment.
    ///
    /// Who asks: nothing in the S-02 weapon files (C-00 decision 2026-09-17) — `SpecialReactionBinder`
    /// consumes S-02's ADR-056-1 `SpecialWeaponUsageEvents` (`Hit` per target → ApplySpecialReaction;
    /// `FieldEnded` / `Deactivated` → <see cref="ZombieEnemy.EndSpecialField"/>).
    /// </summary>
    public static class SpecialReactionProfile
    {
        [PrototypeOnly(PendingDecisions.None, "Kagura push: knockback velocity per unit of the weapon's ImpactForce (5 → 4.5 m/s cap), daze seconds; tier resistance Elite / Ultra.", true)]
        public const float PushSpeedPerForce = 0.9f, PushSpeedCap = 4.5f, PushDazeSeconds = 0.45f;
        /// <summary>Below this the daze is a gameplay blip only (REQ-124 stagger gate) — no Dazed visual (stumble + sway): the Ultra's 0.09 s never reads as a stumble.</summary>
        public const float DazeVisualMinSeconds = 0.2f;
        public const float PushResistElite = 0.5f, PushResistUltra = 0.2f;

        [PrototypeOnly(PendingDecisions.None, "Ofuda net: speed factor while sealed per tier (0 = rooted) and the share of the seal's life the net holds.", true)]
        public static readonly float[] WebSpeedFactor = { 0f, 0.35f, 0.55f, 0.75f };   // Standard rooted, Runner 35 %, Guard 55 %, Executioner 75 %
        public static readonly float[] WebSecondsShare = { 0.35f, 0.25f, 0.20f, 0.15f };   // of SealSeconds (6 s → 2.1 / 1.5 / 1.2 / 0.9 s)

        [PrototypeOnly(PendingDecisions.None, "Tide pull: velocity impulse per unit force toward the core per tick; the Ultra takes a slow instead.", true)]
        public const float PullSpeedPerForce = 0.55f, PullSpeedCap = 3.2f, UltraPullSlowFactor = 0.6f;

        public static float PushResistance(EnemyTier tier) => tier == EnemyTier.Elite ? PushResistElite : tier == EnemyTier.Ultra ? PushResistUltra : 1f;
        public static float WebFactor(EnemyTier tier) => WebSpeedFactor[Mathf.Clamp((int)tier, 0, 3)];
        public static float WebSeconds(EnemyTier tier, float sealSeconds) => Mathf.Max(0.2f, sealSeconds) * WebSecondsShare[Mathf.Clamp((int)tier, 0, 3)];
        public static bool PullMoves(EnemyTier tier) => tier != EnemyTier.Ultra;

        /// <summary>The efficacy the SpawnMixSimulator reports per tier (push resistance × web factor complement), 0..1.</summary>
        public static float Efficacy(EnemyTier tier) => 0.5f * PushResistance(tier) + 0.5f * (1f - WebFactor(tier));
    }

    public sealed partial class ZombieEnemy : ISpecialReactionReceiver
    {
        private float _specialSlowFactor = 1f, _specialSlowUntil = -1f;
        private int _lastSpecialTransaction;
        private SpecialWeaponId _lastSpecialSource;
        private float _specialDazeUntil = -1f;
        private ZombieSpecialVisual _specialVisual = ZombieSpecialVisual.None;

        /// <summary>WP-032E visual the body currently shows for its Special reaction (readout).</summary>
        public ZombieSpecialVisual SpecialVisual => _specialVisual;
        public bool IsRooted => IsNetted && _specialSlowFactor <= 0f;
        public bool IsDazed => _specialDazeUntil > 0f && Time.time < _specialDazeUntil;

        private void SetSpecialVisual(ZombieSpecialVisual state)
        {
            if (_specialVisual == state) return;
            _specialVisual = state;
            _animator.SetSpecialVisual(state);
        }

        /// <summary>Netted (a rooted body) wins over Dazed; both end on their own timers, a field end or death.</summary>
        private void TickSpecialVisual()
        {
            ZombieSpecialVisual want = IsRooted ? ZombieSpecialVisual.Netted : IsDazed ? ZombieSpecialVisual.Dazed : ZombieSpecialVisual.None;
            if (want != _specialVisual) SetSpecialVisual(want);
        }
        private static readonly System.Collections.Generic.List<ZombieEnemy> s_specialAffected = new System.Collections.Generic.List<ZombieEnemy>(64);

        /// <summary>
        /// WP-055: the weapon's field is gone (ADR-056-1 FieldEnded / Deactivated) — every body still netted or
        /// dragged by THAT weapon walks free at once; a push's daze runs out on its own (stagger timer).
        /// Returns the bodies released.
        /// </summary>
        public static int EndSpecialField(SpecialWeaponId source)
        {
            int released = 0;
            for (int i = s_specialAffected.Count - 1; i >= 0; i--)
            {
                ZombieEnemy z = s_specialAffected[i];
                if (z == null) { s_specialAffected.RemoveAt(i); continue; }
                if (z._lastSpecialSource != source) continue;
                if (z._specialSlowUntil > 0f && Time.time < z._specialSlowUntil) released++;
                z._specialSlowFactor = 1f; z._specialSlowUntil = -1f;
                if (z._alive && !z._dying) z.TickSpecialVisual();
                s_specialAffected.RemoveAt(i);
            }
            return released;
        }

        /// <summary>Bodies currently holding a Special effect (tests).</summary>
        public static int SpecialAffectedCount => s_specialAffected.Count;

        /// <summary>WP-055 readouts.</summary>
        public float SpecialSlowFactor => _specialSlowUntil > 0f && Time.time < _specialSlowUntil ? _specialSlowFactor : 1f;
        public bool IsNetted => SpecialSlowFactor < 1f;
        public int SpecialReactionCount { get; private set; }
        public SpecialReactionKind LastSpecialReaction { get; private set; }
        public Vector3 LastSpecialImpulse { get; private set; }

        private void ResetSpecialReactions()
        {
            _specialSlowFactor = 1f; _specialSlowUntil = -1f; _lastSpecialTransaction = 0; SpecialReactionCount = 0; LastSpecialImpulse = Vector3.zero;
            _specialDazeUntil = -1f; _specialVisual = ZombieSpecialVisual.None;
            // a pooled body keeps its driver: tell it explicitly (found by the WP-007 parity suite — a Dazed sway / Netted
            // freeze leaking into the next life put the head in the torso ray of the first dart)
            if (_animator != null) _animator.SetSpecialVisual(ZombieSpecialVisual.None);
            s_specialAffected.Remove(this);
        }

        public bool ApplySpecialReaction(in SpecialReaction r)
        {
            if (!_alive || _dying || IsRising) return false;
            if (r.TransactionId != 0 && r.TransactionId == _lastSpecialTransaction) return false;   // one reaction per attack transaction
            _lastSpecialTransaction = r.TransactionId;
            _lastSpecialSource = r.Source;

            Vector3 dir = r.Direction; dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
            float resist = SpecialReactionProfile.PushResistance(_tier);
            EnemyCombatEventKind beat;
            float seconds = 0f;

            switch (r.Kind)
            {
                case SpecialReactionKind.Push:
                {
                    // the weapon's damage call already passed this force through the REQ-124 governor (HitscanEngine.ApplyHit,
                    // same transaction) — governing it again here would double-count the window; the daze is a stagger without damage
                    float speed = Mathf.Min(SpecialReactionProfile.PushSpeedCap, r.Force * resist * SpecialReactionProfile.PushSpeedPerForce);
                    LastSpecialImpulse = dir * speed;
                    _knockbackVelocity += LastSpecialImpulse;
                    seconds = SpecialReactionProfile.PushDazeSeconds * resist;
                    if (seconds > 0.05f)
                    {
                        // gameplay gate = the REQ-124 stagger timer (no walk / no swing) — the wave's own WP-007 damage may
                        // already have staggered this body in the same frame (impact accumulator), then only the daze timer
                        // and the WP-032E Dazed visual (stumble + sway) are added on top
                        if (!_animator.IsStaggering) _animator.BeginStagger(-dir, 0.8f * resist + 0.3f, seconds, transform);
                        _attack.Cancel();
                        if (seconds >= SpecialReactionProfile.DazeVisualMinSeconds) { _specialDazeUntil = Mathf.Max(_specialDazeUntil, Time.time + seconds); TickSpecialVisual(); }
                    }
                    beat = EnemyCombatEventKind.SpecialPush;
                    break;
                }
                case SpecialReactionKind.Web:
                {
                    seconds = SpecialReactionProfile.WebSeconds(_tier, r.Seconds);
                    float factor = SpecialReactionProfile.WebFactor(_tier);
                    // a fresh seal never shortens a net already holding longer
                    float until = Time.time + seconds;
                    if (until > _specialSlowUntil || _specialSlowUntil <= 0f) { _specialSlowUntil = until; _specialSlowFactor = factor; }
                    else _specialSlowFactor = Mathf.Min(_specialSlowFactor, factor);
                    TickSpecialVisual();   // a rooted body holds its pose (WP-032E Netted); a slowed one keeps walking
                    beat = EnemyCombatEventKind.SpecialWeb;
                    break;
                }
                default:
                {
                    if (SpecialReactionProfile.PullMoves(_tier))
                    {
                        float speed = Mathf.Min(SpecialReactionProfile.PullSpeedCap, r.Force * SpecialReactionProfile.PullSpeedPerForce);
                        LastSpecialImpulse = dir * speed;
                        _knockbackVelocity += LastSpecialImpulse;
                    }
                    else
                    {
                        // the Executioner holds its ground; the well only drags its feet while it lasts (Seconds = time
                        // left to the implosion; FieldEnded releases it earlier through EndSpecialField)
                        seconds = Mathf.Max(0.2f, r.Seconds);
                        _specialSlowFactor = SpecialReactionProfile.UltraPullSlowFactor;
                        _specialSlowUntil = Mathf.Max(_specialSlowUntil, Time.time + seconds);
                        LastSpecialImpulse = Vector3.zero;
                    }
                    seconds = seconds > 0f ? seconds : r.Seconds;
                    beat = EnemyCombatEventKind.SpecialPull;
                    break;
                }
            }

            SpecialReactionCount++;
            LastSpecialReaction = r.Kind;
            if (_specialSlowUntil > 0f && !s_specialAffected.Contains(this)) s_specialAffected.Add(this);
            EnemyCombatEvents.Raise(new EnemyCombatEventInfo(beat, transform, _tier, _lifeId, transform.position + Vector3.up * 1.1f, dir, ArmorSlot.None,
                _animator.Driven, seconds, _attack != null ? _attack.Style : ZombieAttackStyle.Swipe, r.Source));
            return true;
        }
    }
}
