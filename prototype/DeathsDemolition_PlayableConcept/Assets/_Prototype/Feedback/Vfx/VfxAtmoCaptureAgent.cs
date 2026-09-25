using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.PlayerSystem;
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

        private static bool PlaceInDensestRegion()
        {
            int n = (int)Shader.GetGlobalFloat("_DD_VL_RegionCount");
            PlayerRig rig = PlayerRegistry.Primary;
            if (n <= 0 || rig == null) return false;
            var regions = new List<Vector4>(8);
            var shapes = new List<Vector4>(8);
            Shader.GetGlobalVectorArray("_DD_VL_Regions", regions);
            Shader.GetGlobalVectorArray("_DD_VL_RegionShape", shapes);
            int best = -1;
            for (int i = 0; i < Mathf.Min(n, Mathf.Min(regions.Count, shapes.Count)); i++) if (best < 0 || shapes[i].y > shapes[best].y) best = i;
            if (best < 0) return false;
            Vector4 r = regions[best];
            Vector3 centre = new Vector3(r.x, r.y, r.z);
            Vector3 at = centre - Vector3.forward * (r.w * 0.6f);
            int mask = (1 << 0) | (1 << DD.Prototype.Core.PrototypeLayers.Environment);
            if (Physics.Raycast(new Vector3(at.x, centre.y + 30f, at.z), Vector3.down, out RaycastHit hit, 60f, mask, QueryTriggerInteraction.Ignore)) at = hit.point;
            rig.Health.DiagnosisInvulnerable = true;
            rig.Teleport(at, Quaternion.LookRotation(Vector3.forward));
            Debug.Log("[VfxAtmoCapture] placed in region " + best + " centre " + centre.ToString("F1") + " radius " + r.w.ToString("F1") + " at " + at.ToString("F1"));
            return true;
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

            yield return new WaitForSeconds(8f);   // past the round-intro plate (AD B5: the FP frame at 4 s was the plate)
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ddVfxAtmoInRegion") >= 0)
            {
                // AD B5 MUST 4: a frame through a shaft — the player (invulnerable, evidence only) is moved to the rim of the
                // densest region of the DEC-102 field, facing its centre, and the grains get 4 s to fill round the new focus.
                if (PlaceInDensestRegion()) yield return new WaitForSeconds(4f);
            }

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
