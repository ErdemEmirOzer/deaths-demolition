using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-055 · The one consumer that turns S-02's Special Weapon usage events (WP-056, ADR-056-1) into
    /// body reactions. The weapon files never learn the enemy; this binder never learns the weapon
    /// internals:
    ///   Hit          → <see cref="ISpecialReactionReceiver.ApplySpecialReaction"/> on the event's target
    ///                  (tier resistance resolved by the body; force × falloff at that distance)
    ///   FieldEnded   → <see cref="ZombieEnemy.EndSpecialField"/>: the web collapsed / expired or the well
    ///                  imploded — every body that weapon still nets or drags walks free
    ///   Deactivated  → the same, for the weapon that just left the hand
    /// A wave's FieldEnded clears nothing (a push has no held state); the daze runs out on its own.
    /// </summary>
    public static class SpecialReactionBinder
    {
        /// <summary>Readouts (tests / probes): targeted hits that produced a body reaction, and field ends routed.</summary>
        public static int HitsRouted { get; private set; }
        public static int FieldsEnded { get; private set; }
        public static int BodiesReleased { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bind() => Rebind();

        /// <summary>Subscribe (again). Tests call this after <c>SpecialWeaponUsageEvents.ResetForTests</c>, which drops every subscriber.</summary>
        public static void Rebind()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            SpecialWeaponUsageEvents.Usage += OnUsage;
            HitsRouted = 0; FieldsEnded = 0; BodiesReleased = 0;
        }

        public static SpecialReactionKind KindFor(SpecialWeaponId id)
        {
            switch (id)
            {
                case SpecialWeaponId.OfudaLoom: return SpecialReactionKind.Web;
                case SpecialWeaponId.TideJewel: return SpecialReactionKind.Pull;
                default: return SpecialReactionKind.Push;
            }
        }

        /// <summary>The reaction request a usage event maps to (pure; the receiver resolves it by tier).</summary>
        public static SpecialReaction ReactionFor(in SpecialUsageEvent e)
        {
            // Push: Direction = away from the origin, force on the DamageForce scale × the rim falloff at this body;
            // Web: Seconds = the seal's life (Force 0); Pull: Direction = toward the core, Seconds = time left to the implosion
            return new SpecialReaction(KindFor(e.Id), e.Id, e.Direction, e.Force * Mathf.Clamp01(e.Falloff01), e.Seconds, e.TransactionId);
        }

        private static void OnUsage(SpecialUsageEvent e)
        {
            switch (e.Kind)
            {
                case SpecialUsageKind.Hit:
                    if (e.Target.Receiver is ISpecialReactionReceiver body && body.ApplySpecialReaction(ReactionFor(in e))) HitsRouted++;
                    break;
                case SpecialUsageKind.FieldEnded:
                case SpecialUsageKind.Deactivated:
                    FieldsEnded++;
                    BodiesReleased += ZombieEnemy.EndSpecialField(e.Id);
                    break;
            }
        }
    }
}
