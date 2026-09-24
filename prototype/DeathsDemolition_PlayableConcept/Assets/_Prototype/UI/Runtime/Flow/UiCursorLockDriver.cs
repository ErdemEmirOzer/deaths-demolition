using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// M4-R1 N2 · the one per-frame hook of <see cref="UiCursor"/>: the camera's cursor intent changes without an event
    /// (a look block, the pause freeze, the mode), so the lock is synced after every frame's UI and camera work.
    /// Created by UiCursor at install (hidden, survives scene loads).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiCursorLockDriver : MonoBehaviour
    {
        private void LateUpdate()
        {
            UiCursor.SyncLock();
        }

        private void OnApplicationFocus(bool focused)
        {
            UiCursor.SyncLock();
        }
    }
}
