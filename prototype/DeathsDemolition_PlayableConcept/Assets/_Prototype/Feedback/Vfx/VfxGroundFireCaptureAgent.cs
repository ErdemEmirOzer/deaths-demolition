#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 · AD evidence for the burning ground: <c>-ddVfxGroundFireCapture &lt;folder&gt;</c> rides along a run and ignites
    /// one K10b zone (radius 3 m, 3 s, a token 0.001 dps) at a readable ground point (FP 4 m, TP 6 m, top-down 3 m ahead of the
    /// player), framing it at ignition (0.3 s), full (1.2 s), dying (2.6 s), out (3.3 s) and the char 2 s later (5.3 s) as
    /// <c>&lt;mode&gt;-groundfire-&lt;name&gt;.png</c> with the system's counters logged, then quits. The player is held invulnerable
    /// (evidence only, as UiCaptureAgent). Opt-in only.
    /// </summary>
    public sealed class VfxGroundFireCaptureAgent : MonoBehaviour
    {
        private const string Flag = "-ddVfxGroundFireCapture";
        private string _folder;
        private string _mode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != Flag) continue;
                // M-55 (C-00 30.09): a capture run writes frames and plays a run, so it only starts inside a sandbox
                // (-ddProfilePath → ProfileStore, or -ddUiDataRoot / QA bot → UiDataPaths; both set BeforeSceneLoad).
                if (!DD.Prototype.Profile.ProfileStore.HasPathOverride && !DD.Prototype.UI.UiDataPaths.IsProcessLocal)
                {
                    Debug.LogError("[Capture] refused: no sandbox (" + Flag + ")");
                    Application.Quit(2);
                    return;
                }

                var go = new GameObject("VfxGroundFireCaptureAgent");
                DontDestroyOnLoad(go);
                go.AddComponent<VfxGroundFireCaptureAgent>()._folder = args[i + 1];
                return;
            }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            float deadline = Time.realtimeSinceStartup + 240f;
            while ((WeaponRegistry.ActiveController == null || Camera.main == null || GroundFireSystem.Instance == null) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return new WaitForSeconds(4f);
            if (PlayerRegistry.Primary != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;   // evidence only
            _mode = CameraModeService.Current == CameraMode.FirstPerson ? "fp" : CameraModeService.Current == CameraMode.ThirdPerson ? "tp" : "topdown";

            if (Aim(out Vector3 p))
            {
                RunTreeGroundFire.Ignite(p, 3f, 0.001f, 3f);
                yield return new WaitForSeconds(0.3f);
                yield return Frame("ignition");
                yield return new WaitForSeconds(0.9f);
                yield return Frame("full");
                yield return new WaitForSeconds(1.4f);
                yield return Frame("dying");
                yield return new WaitForSeconds(0.7f);
                yield return Frame("out");
                yield return new WaitForSeconds(2.0f);
                yield return Frame("char-out+2s");   // the char holds 3 s past the fire (AD MUST 4)
            }

            Debug.Log("[VfxGroundFireCapture] done");
            Application.Quit(0);
        }

        private static bool Aim(out Vector3 point)
        {
            Camera cam = Camera.main;
            point = Vector3.zero;
            if (cam == null) return false;
            Transform c = cam.transform;
            Vector3 flat = Vector3.ProjectOnPlane(c.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(c.up, Vector3.up);
            flat.Normalize();
            Vector3 origin;
            switch (CameraModeService.Current)
            {
                case CameraMode.FirstPerson: origin = c.position + flat * 4f; break;
                case CameraMode.ThirdPerson: origin = c.position + flat * 6f; break;
                default:
                    Transform player = WeaponRegistry.ActiveController != null ? WeaponRegistry.ActiveController.transform : c;
                    origin = player.position + flat * 3f;
                    break;
            }

            int mask = (1 << 0) | (1 << DD.Prototype.Core.PrototypeLayers.Environment);
            if (!Physics.Raycast(origin + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, mask, QueryTriggerInteraction.Ignore)) return false;
            point = hit.point;
            return true;
        }

        private IEnumerator Frame(string name)
        {
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(_folder, _mode + "-groundfire-" + name + ".png");
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(file, frame.EncodeToPNG());
            Destroy(frame);
            GroundFireSystem g = GroundFireSystem.Instance;
            Debug.Log("[VfxGroundFireCapture] " + file + " zones=" + g.LiveZones + " flames=" + g.FlamesLive + " embers=" + g.EmbersLive + " glow=" + g.GlowLive + " char=" + g.CharLive + " shimmer=" + g.ShimmerLive
                + " lit=" + g.LitZones + " haze=" + g.HazeShown + " tier=" + VfxTier.Current);
        }
    }
}
#endif
