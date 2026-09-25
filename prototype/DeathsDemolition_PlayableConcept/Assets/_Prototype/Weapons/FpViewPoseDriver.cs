using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-069 · runs <see cref="WeaponController"/>'s first-person view pose after the weapon mount alignment (900), the grip
    /// IK (910) and the twist corrector (920), so the view model is placed on the frame's final camera and mount. Added by
    /// the controller itself; holds no state.
    /// </summary>
    [DefaultExecutionOrder(950)]
    [DisallowMultipleComponent]
    public sealed class FpViewPoseDriver : MonoBehaviour
    {
        private WeaponController _controller;

        public void Bind(WeaponController controller) => _controller = controller;

        private void LateUpdate()
        {
            if (_controller != null) _controller.ApplyFpViewPose();
        }
    }
}
