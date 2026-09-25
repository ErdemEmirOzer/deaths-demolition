using System;
using System.Collections;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B5 · AD evidence for the atmosphere: <c>-ddVfxAtmoCapture &lt;folder&gt;</c> rides along a run (QA bot or direct)
    /// and, once the world's <see cref="AtmosphereSystem"/> is live, grabs six frames 1.5 s apart named
    /// <c>&lt;mode&gt;-&lt;tier&gt;-atmo-&lt;n&gt;.png</c>, logging the live grains per layer with each, then quits. Opt-in only.
    /// </summary>
    public sealed class VfxAtmoCaptureAgent : MonoBehaviour
    {
        private const string Flag = "-ddVfxAtmoCapture";
        private string _folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != Flag) continue;
                var go = new GameObject("VfxAtmoCaptureAgent");
                DontDestroyOnLoad(go);
                go.AddComponent<VfxAtmoCaptureAgent>()._folder = args[i + 1];
                return;
            }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            float deadline = Time.realtimeSinceStartup + 240f;
            bool without = Array.IndexOf(Environment.GetCommandLineArgs(), "-ddNoAtmoV3") >= 0;   // A/B: the world without B5
            while ((WeaponRegistry.ActiveController == null || Camera.main == null || (!without && (AtmosphereSystem.Active == null || !AtmosphereSystem.Active.Ready)))
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return new WaitForSeconds(4f);
            string mode = CameraModeService.Current == CameraMode.FirstPerson ? "fp" : CameraModeService.Current == CameraMode.ThirdPerson ? "tp" : "topdown";
            string tier = VfxTier.Current.ToString().ToLowerInvariant();
            for (int n = 0; n < 6; n++)
            {
                yield return new WaitForEndOfFrame();
                string file = Path.Combine(_folder, mode + "-" + tier + "-atmo-" + n + ".png");
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(file, frame.EncodeToPNG());
                Destroy(frame);
                AtmosphereSystem a = AtmosphereSystem.Active;
                string counts = "";
                if (a != null)
                {
                    foreach (AtmosphereSystem.Layer layer in Enum.GetValues(typeof(AtmosphereSystem.Layer))) counts += " " + layer + "=" + a.LiveCount(layer);
                }

                Vector4 vl = Shader.GetGlobalVector("_DD_VL_Density");
                string field = " fieldLive=" + AtmosphereSystem.FieldLive + " vl=" + vl.ToString("F4") + " vlMask=" + Shader.GetGlobalVector("_DD_VL_Mask").x.ToString("F1")
                    + (a != null ? " volumeRegions=" + a.VolumeRegions + " volumeInRange=" + a.InRangeCount(AtmosphereSystem.Source.Volume) + " shaftCards=" + a.InRangeCount(AtmosphereSystem.Source.Shaft) : "");
                Debug.Log("[VfxAtmoCapture] " + file + " total=" + (a != null ? a.TotalLive : -1) + counts + " shaftW=" + Shader.GetGlobalVector("_DD_ShaftDir").w.ToString("F2") + field);
                yield return new WaitForSeconds(1.5f);
            }

            Debug.Log("[VfxAtmoCapture] done");
            Application.Quit(0);
        }
    }
}
