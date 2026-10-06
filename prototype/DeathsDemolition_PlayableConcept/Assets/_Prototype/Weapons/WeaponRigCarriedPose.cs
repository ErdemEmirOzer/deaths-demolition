using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-087 (C-00 06.10, K/wp-083/KARAR-HELDKICK-API-06.10.KONSOLIDE.md §1): runs after WeaponHoldIk (890) and before
    /// WeaponMountAlignment (900) / WeaponGripIk (910), so the rig's body takes 1 − the hold's weight of the same frame the hold
    /// used for the arms (<see cref="WeaponVisualRig.ApplyCarriedShare"/>). WeaponVisualRig adds it to itself; nothing to set up.
    /// </summary>
    [DefaultExecutionOrder(895)]
    [DisallowMultipleComponent]
    public sealed class WeaponRigCarriedPose : MonoBehaviour
    {
        private WeaponVisualRig _rig;

        private void Awake() => _rig = GetComponent<WeaponVisualRig>();

        private void LateUpdate()
        {
            if (_rig != null) _rig.ApplyCarriedShare();
        }
    }
}
