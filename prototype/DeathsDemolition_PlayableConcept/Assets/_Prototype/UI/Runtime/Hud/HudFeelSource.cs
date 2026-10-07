using System;
using DD.Prototype.PlayerSystem;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>What kind of blow hit the player (S-02's FeelEvents.PlayerDamaged will carry it; PlayerHealth does not - Unknown).</summary>
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
    /// out of what, and - once S-02's FeelEvents carries them - its kind. The HUD never reads PlayerDamageInfo itself: the one
    /// adapter (<see cref="HudFeelSource.FromPlayerHealth"/>) does, so the source can change under it in one place.
    /// </summary>
    public readonly struct HudDamage
    {
        public readonly Vector3 HitDirection;
        public readonly float AppliedAmount;
        public readonly float HealthBefore;
        public readonly float HealthAfter;
        public readonly float MaxHealth;
        public readonly bool WasFatal;
        public readonly HudDamageKind Kind;

        public HudDamage(Vector3 hitDirection, float appliedAmount, float healthBefore, float healthAfter, float maxHealth, bool wasFatal,
            HudDamageKind kind = HudDamageKind.Unknown)
        {
            HitDirection = hitDirection;
            AppliedAmount = appliedAmount;
            HealthBefore = healthBefore;
            HealthAfter = healthAfter;
            MaxHealth = maxHealth;
            WasFatal = wasFatal;
            Kind = kind;
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
    /// WP-094 HUD v4 (R3, C-00 07.10; S-09 decision: "bind to PlayerHealth.AnyDamaged today through ONE adapter method so
    /// S-02's upcoming FeelEvents can replace the source in one place") · THE SEAM between the game's feel events and the
    /// HUD's feel. Every HUD consumer of a player hit - the screen-edge direction (UiHitDirectionLayer, D1), the top-down
    /// wedges (HudPlayerArcs, D2), the health chunk / knock (H1, H3) - subscribes to <see cref="PlayerDamaged"/>, never to
    /// PlayerHealth.
    ///
    /// TODAY: one bridge subscription on PlayerHealth.AnyDamaged (raised after health is written, for every source - the
    /// reasons UiHitDirectionLayer chose it in REQ-045 still hold), made when the first HUD listener arrives and dropped when
    /// the last one leaves, so the menu carries no subscriber (LifecycleHardeningTests: the run adds 1 here, not 2).
    /// WHEN FeelEvents LANDS (S-02, HAREKET-HUD §5 "tek kaynak"): <see cref="Bind"/> / <see cref="Unbind"/> subscribe to
    /// FeelEvents.PlayerDamaged instead, and its adapter replaces <see cref="FromPlayerHealth"/> (SourcePosition, Kind,
    /// AttackerTier fill the payload) - in the same commit, so there is never a double trigger. Nothing else changes.
    /// No allocation per raise: the payload is a readonly struct, the bridge delegate is cached.
    /// </summary>
    public static class HudFeelSource
    {
        private static Action<HudDamage> s_damaged;
        private static readonly Action<PlayerDamageInfo> s_bridge = OnPlayerHealthDamaged;

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

        /// <summary>THE adapter: today's source (PlayerHealth's damage record) as the HUD's payload. Kind is not known there.</summary>
        public static HudDamage FromPlayerHealth(in PlayerDamageInfo info)
        {
            return new HudDamage(info.HitDirection, info.AppliedAmount, info.HealthBefore, info.HealthAfter, info.MaxHealth, info.WasFatal);
        }

        /// <summary>The one subscription on the source. Remove-then-add: never twice, and a statics reset under a live HUD re-binds on the next add.</summary>
        private static void Bind()
        {
            PlayerHealth.AnyDamaged -= s_bridge;
            PlayerHealth.AnyDamaged += s_bridge;
        }

        private static void Unbind()
        {
            PlayerHealth.AnyDamaged -= s_bridge;
        }

        private static void OnPlayerHealthDamaged(PlayerDamageInfo info)
        {
            Action<HudDamage> handlers = s_damaged;
            if (handlers != null)
            {
                handlers(FromPlayerHealth(info));
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_damaged = null;   // PlayerHealth drops its own listeners (the bridge with them) in the same phase
        }
    }
}
