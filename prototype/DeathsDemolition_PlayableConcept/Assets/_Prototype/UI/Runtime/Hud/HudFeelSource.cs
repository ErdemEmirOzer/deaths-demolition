using System;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>What kind of blow hit the player (FeelEvents.PlayerDamaged does not carry it yet - Unknown; S-02 adds it on request).</summary>
    public enum HudDamageKind
    {
        Unknown = 0,
        Melee = 1,
        Projectile = 2,
        Explosion = 3,
        Hazard = 4
    }

    /// <summary>
    /// WP-094 HUD v4 · the player-damage payload the HUD's feel reads (HAREKET-HUD §5 "D1, D2, H1–H3"): the direction the blow
    /// travelled (FROM the attacker TOWARDS the player, horizontal, zero when the attacker gave no position), what it took and
    /// out of what, whether it was heavy (≥ 25 % of max), and - when FeelEvents carries it - its kind. The HUD never reads the
    /// source's payload itself: the one adapter (<see cref="HudFeelSource.FromFeel"/>) does.
    /// </summary>
    public readonly struct HudDamage
    {
        public readonly Vector3 HitDirection;
        public readonly float AppliedAmount;
        public readonly float HealthBefore;
        public readonly float HealthAfter;
        public readonly float MaxHealth;
        public readonly bool WasFatal;
        public readonly bool Heavy;
        public readonly HudDamageKind Kind;

        public HudDamage(Vector3 hitDirection, float appliedAmount, float healthBefore, float healthAfter, float maxHealth, bool wasFatal,
            HudDamageKind kind = HudDamageKind.Unknown, bool heavy = false)
        {
            HitDirection = hitDirection;
            AppliedAmount = appliedAmount;
            HealthBefore = healthBefore;
            HealthAfter = healthAfter;
            MaxHealth = maxHealth;
            WasFatal = wasFatal;
            Kind = kind;
            Heavy = heavy;
        }

        /// <summary>Health before the hit, 0..1 of max.</summary>
        public float NormalizedBefore
        {
            get { return MaxHealth > 0f ? Mathf.Clamp01(HealthBefore / MaxHealth) : 0f; }
        }

        /// <summary>Health after the hit, 0..1 of max.</summary>
        public float NormalizedAfter
        {
            get { return MaxHealth > 0f ? Mathf.Clamp01(HealthAfter / MaxHealth) : 0f; }
        }

        /// <summary>What the hit took, as a share of max health (0 without a max).</summary>
        public float Fraction
        {
            get { return MaxHealth > 0f ? Mathf.Max(0f, AppliedAmount) / MaxHealth : 0f; }
        }
    }

    /// <summary>
    /// WP-094 HUD v4 (R3, C-00 07.10 / 08.10) · THE SEAM between the game's feel events and the HUD's damage feel. Every HUD
    /// consumer of a player hit - the screen-edge direction (UiHitDirectionLayer, D1), the top-down wedges (HudPlayerArcs,
    /// D2), the health chunk / knock (H1, H3) - subscribes to <see cref="PlayerDamaged"/>.
    ///
    /// SOURCE (08.10, S-02's FeelEvents in main 7151f821; K/wp-095/FEELEVENTS-LISTE.md "R3 his işinde bu anlara yalnız
    /// FeelEvents'ten abone olun"): one bridge on <see cref="FeelEvents.PlayerDamaged"/> - raised once from PlayerHealth's
    /// authoritative place, after health is written; invulnerable / second-wind refusals raise nothing - made with the first
    /// HUD listener and dropped with the last, so the menu carries no subscriber. The 07.10 bridge on PlayerHealth.AnyDamaged
    /// is gone in the same change (no double trigger). The direction is the event's Direction (attacker → player), never
    /// WorldPoint − player (always zero - the known CameraFeelDriver bug). <see cref="FromFeel"/> is the one adapter.
    /// No allocation per raise: the payload is a readonly struct, the bridge delegate is cached.
    /// </summary>
    public static class HudFeelSource
    {
        private static Action<HudDamage> s_damaged;
        private static readonly Action<FeelPlayerDamage> s_bridge = OnFeelPlayerDamaged;

        /// <summary>A player hit, as the HUD reads it (D1, D2, H1–H3).</summary>
        public static event Action<HudDamage> PlayerDamaged
        {
            add
            {
                s_damaged += value;
                Bind();
            }

            remove
            {
                s_damaged -= value;
                if (s_damaged == null)
                {
                    Unbind();
                }
            }
        }

        /// <summary>True while a HUD listens (tests).</summary>
        public static bool HasListeners
        {
            get { return s_damaged != null; }
        }

        /// <summary>THE adapter: FeelEvents' player-damage record as the HUD's payload (Kind is not carried yet - Unknown).</summary>
        public static HudDamage FromFeel(in FeelPlayerDamage feel)
        {
            return new HudDamage(feel.Direction, feel.Amount, feel.HealthBefore, feel.HealthAfter, feel.MaxHealth, feel.Fatal,
                HudDamageKind.Unknown, feel.Heavy);
        }

        /// <summary>The one subscription on the source. Remove-then-add: never twice, and a statics reset under a live HUD re-binds on the next add.</summary>
        private static void Bind()
        {
            FeelEvents.PlayerDamaged -= s_bridge;
            FeelEvents.PlayerDamaged += s_bridge;
        }

        private static void Unbind()
        {
            FeelEvents.PlayerDamaged -= s_bridge;
        }

        private static void OnFeelPlayerDamaged(FeelPlayerDamage feel)
        {
            Action<HudDamage> handlers = s_damaged;
            if (handlers != null)
            {
                handlers(FromFeel(feel));
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_damaged = null;   // FeelEvents drops its own listeners (the bridge with them) in the same phase
        }
    }
}
