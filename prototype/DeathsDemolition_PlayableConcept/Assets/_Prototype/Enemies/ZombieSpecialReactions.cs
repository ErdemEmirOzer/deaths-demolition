using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-076 (M4-R1 N1, DEC-094): the Kagura / Ofuda / Tide reactions below are RETIRED — the two new Specials (chain
    /// lightning, gel) live in ZombieShock / ZombieGel. This table stays only because S-10's SpawnMixSimulator still
    /// reads it; its owner drops it with S-02's WP-069 B4a. Original WP-055 text:
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

    /// <summary>
    /// WP-076 · the body's Special visual (S-06 WP-032E / WP-071 B3). The WP-055 reaction behaviour (push / net / pull /
    /// daze, the <c>ISpecialReactionReceiver</c> routing) is removed with the old Specials; the readouts and the two old
    /// entry points below stay inert (false / 1 / no-op) so S-09's status markers and capture agent compile until their
    /// owners drop them.
    /// </summary>
    public sealed partial class ZombieEnemy
    {
        private ZombieSpecialVisual _specialVisual = ZombieSpecialVisual.None;

        /// <summary>WP-032E visual the body currently shows for its Special state (readout).</summary>
        public ZombieSpecialVisual SpecialVisual => _specialVisual;

        /// <summary>Retired WP-055 readouts — always false / 1 now (kept for S-09 UiEnemyStatusMarkers / UiCaptureAgent).</summary>
        public bool IsRooted => false;
        public bool IsDazed => false;
        public bool IsNetted => false;
        public float SpecialSlowFactor => 1f;

        /// <summary>Retired WP-055 entry points — inert (no reaction, nothing released); kept only for S-09's UiCaptureAgent and an S-09 test until their owners drop them.</summary>
        public bool ApplySpecialReaction(in SpecialReaction r) => false;
        public static int EndSpecialField(SpecialWeaponId source) => 0;

        private void SetSpecialVisual(ZombieSpecialVisual state)
        {
            if (_specialVisual == state) return;
            _specialVisual = state;
            _animator.SetSpecialVisual(state);
        }

        /// <summary>
        /// WP-076 · the shock shows S-06's Electrocuted visual (hit_electro_loop: window/telegraph closed, animator ×0.15 +
        /// spasm); every gel state shows Slipping (stumble → sway) until S-06 adds Downed / GettingUp for the fall and the
        /// get-up (AP-022). The shock wins over gel (a shocked slide stands still).
        /// </summary>
        private void TickSpecialVisual()
        {
            ZombieSpecialVisual want = IsElectrocuted ? ZombieSpecialVisual.Electrocuted
                : IsFrozen ? ZombieSpecialVisual.Netted    // WP-104 B3: frozen — S-06 holds the Animator mid-pose (speed 0)
                : IsStunned ? ZombieSpecialVisual.Dazed    // WP-104 B3: the nuke / bell / bulwark stun reads as the daze
                : Gel != GelState.None ? ZombieSpecialVisual.Slipping
                : ZombieSpecialVisual.None;
            if (want != _specialVisual) SetSpecialVisual(want);
        }

        private void ResetSpecialReactions()
        {
            _specialVisual = ZombieSpecialVisual.None;
            // a pooled body keeps its driver: tell it explicitly (found by the WP-007 parity suite — a Dazed sway / Netted
            // freeze leaking into the next life put the head in the torso ray of the first dart)
            if (_animator != null) _animator.SetSpecialVisual(ZombieSpecialVisual.None);
        }
    }
}
