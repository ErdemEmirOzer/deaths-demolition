using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art
{
    /// <summary>
    /// WP-075 · DEC-102 · Keeps the sun's disc-and-halo quad (DD/SunDisc, WorldV5Sky) on the sun direction for every camera: placed
    /// <see cref="Distance"/> m from the camera just before it renders (inside the gameplay rig's 260 m far clip, so it is never
    /// frustum-culled; the planar camera gets its own placement, so the puddles mirror the sun), facing back along the direction.
    /// </summary>
    public sealed class SunDisc : MonoBehaviour
    {
        public const float Distance = 200f;

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += Place;

        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= Place;

        private void Place(ScriptableRenderContext context, Camera camera)
        {
            Vector3 toSun = VolumetricLight.SunDirection;
            if (toSun.y <= 0f) return;
            transform.SetPositionAndRotation(camera.transform.position + toSun * Distance, Quaternion.LookRotation(toSun));
        }
    }
}
