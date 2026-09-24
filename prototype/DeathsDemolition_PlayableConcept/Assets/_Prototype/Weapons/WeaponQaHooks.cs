using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-069 · WP-078 (S-10 acceptance): the one safe way for QA tooling (QaRunBot's weapon cycle) to put a
    /// classic weapon in the player's hands. Accepts only an enabled Arsenal classic weapon; writes the
    /// selection (the next run starts with it) and, when a controller is live and no Special Weapon is in
    /// hand, equips it now with a full magazine and the current round's reserve allowance. No profile gate
    /// (acceptance runs every weapon), no gameplay path calls this.
    /// </summary>
    public static class WeaponQaHooks
    {
        /// <summary>True when <paramref name="id"/> is an enabled classic weapon and was selected (and equipped when possible).</summary>
        public static bool TrySetClassicWeapon(ClassicWeaponId id)
        {
            WeaponDefinition d = Arsenal.Get(id);
            if (d == null || !d.enabled || d.slot != WeaponSlotKind.Classic)
            {
                return false;
            }

            WeaponRegistry.SelectedWeapon = id;
            WeaponController controller = WeaponRegistry.ActiveController;
            if (controller != null && !controller.SpecialWeaponActive)
            {
                controller.EquipClassic(id);
                int round = SpecialWeaponRoundBridge.CurrentRound;
                if (round > 1)
                {
                    controller.Classic.RefillReserveForRound(round);
                }
            }

            return true;
        }
    }
}
