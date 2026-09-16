#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-025 · ADR-025-2 measurement: the render-texture footprint of the Run scene under
    /// four in-memory variants of the shipped URP asset (no ProjectSettings/asset change):
    ///   A  as shipped (MSAA 4×, HDR on)   B  MSAA 2×   C  MSAA off   D  MSAA off + HDR off
    /// For each: every live RenderTexture (size, format, samples, depth bits, computed bytes)
    /// and the graphics-driver allocation, written to &lt;out&gt;/rt-&lt;variant&gt;.txt plus a summary.
    /// Development builds only:  -ddRtProbe &lt;outDir&gt; [-ddProfilePath &lt;file&gt;]
    /// The asset is restored to its shipped values before quitting.
    /// </summary>
    public static class RenderTextureBudgetProbe
    {
        private const string OutFlag = "-ddRtProbe";
        private const string ProfileFlag = "-ddProfilePath";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            string path = Arg(ProfileFlag);
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(Arg(OutFlag)))
            {
                ProfileStore.SetPathOverride(path);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string outDir = Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir))
            {
                return;
            }

            var carrier = new GameObject("RenderTextureBudgetProbe");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().OutDir = outDir;
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private sealed class Driver : MonoBehaviour
        {
            public string OutDir;

            private IEnumerator Start()
            {
                Directory.CreateDirectory(OutDir);
                var summary = new StringBuilder();
                try
                {
                    yield return WaitForScene(PrototypeScenes.MainMenu, 60f);
                    PrototypeUiFlow.GoToSelection();
                    yield return WaitForScene(PrototypeScenes.Selection, 60f);
                    PrototypeUiFlow.SelectedSkin = PlayerSkin.IndigoBlack;
                    PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                    PrototypeUiFlow.StartRun();
                    yield return WaitForScene(PrototypeScenes.Run, 90f);
                    for (int i = 0; i < 90; i++)
                    {
                        yield return null;
                    }

                    var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    if (asset == null)
                    {
                        summary.Append("FAIL no URP asset\n");
                        yield break;
                    }

                    int msaa0 = asset.msaaSampleCount;
                    bool hdr0 = asset.supportsHDR;
                    float scale0 = asset.renderScale;
                    summary.Append("RenderTextureBudgetProbe screen=" + Screen.width + "x" + Screen.height + " shipped msaa=" + msaa0 +
                                   " hdr=" + hdr0 + " precision=" + asset.hdrColorBufferPrecision + " renderScale=" + scale0.ToString("0.00", CultureInfo.InvariantCulture) + "\n");
                    var variants = new[]
                    {
                        new Variant("A-shipped-msaa" + msaa0 + "-hdr" + (hdr0 ? "on" : "off"), msaa0, hdr0),
                        new Variant("B-msaa2-hdr" + (hdr0 ? "on" : "off"), 2, hdr0),
                        new Variant("C-msaa1-hdr" + (hdr0 ? "on" : "off"), 1, hdr0),
                        new Variant("D-msaa1-hdroff", 1, false),
                    };
                    foreach (Variant v in variants)
                    {
                        asset.msaaSampleCount = v.Msaa;
                        asset.supportsHDR = v.Hdr;
                        for (int i = 0; i < 45; i++)
                        {
                            yield return null;
                        }

                        yield return new WaitForEndOfFrame();
                        long bytes = Dump(v.Name);
                        long driver = Profiler.GetAllocatedMemoryForGraphicsDriver();
                        summary.Append(v.Name + " rtBytes=" + bytes + " (" + (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) +
                                       " MB) driverBytes=" + driver + " (" + (driver / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB)\n");
                    }

                    asset.msaaSampleCount = msaa0;
                    asset.supportsHDR = hdr0;
                    asset.renderScale = scale0;
                    summary.Append("restored msaa=" + asset.msaaSampleCount + " hdr=" + asset.supportsHDR + "\n");
                }
                finally
                {
                    File.WriteAllText(Path.Combine(OutDir, "summary.txt"), summary.ToString());
                    Debug.Log("[RtProbe] " + summary.ToString().Replace("\n", " | "));
                    Application.Quit(0);
                }
            }

            private long Dump(string name)
            {
                RenderTexture[] all = Resources.FindObjectsOfTypeAll<RenderTexture>();
                var sb = new StringBuilder();
                long total = 0;
                sb.Append("name\twidth\theight\tformat\tsamples\tdepthBits\tvolume\tbytes\n");
                foreach (RenderTexture rt in all)
                {
                    if (rt == null || rt.width <= 0 || rt.height <= 0)
                    {
                        continue;
                    }

                    long bytes = Estimate(rt);
                    total += bytes;
                    sb.Append(rt.name).Append('\t').Append(rt.width).Append('\t').Append(rt.height).Append('\t').Append(rt.graphicsFormat)
                      .Append('\t').Append(rt.antiAliasing).Append('\t').Append(rt.depth).Append('\t').Append(rt.volumeDepth).Append('\t').Append(bytes).Append('\n');
                }

                sb.Append("TOTAL\t\t\t\t\t\t\t").Append(total).Append('\n');
                File.WriteAllText(Path.Combine(OutDir, "rt-" + name + ".txt"), sb.ToString());
                return total;
            }

            private static long Estimate(RenderTexture rt)
            {
                long pixels = (long)rt.width * rt.height * Math.Max(1, rt.volumeDepth);
                int samples = Math.Max(1, rt.antiAliasing);
                long color = pixels * BytesPerPixel(rt.graphicsFormat) * samples;
                long depth = rt.depth > 0 ? pixels * (rt.depth >= 32 ? 5 : rt.depth >= 24 ? 4 : 2) * samples : 0;
                return color + depth;
            }

            private static int BytesPerPixel(UnityEngine.Experimental.Rendering.GraphicsFormat f)
            {
                try
                {
                    return (int)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(f);
                }
                catch (Exception)
                {
                    return 4;
                }
            }

            private static IEnumerator WaitForScene(string name, float timeoutSeconds)
            {
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != name)
                {
                    if (Time.realtimeSinceStartup - t0 > timeoutSeconds)
                    {
                        throw new TimeoutException("[RtProbe] scene " + name + " not reached");
                    }

                    yield return null;
                }

                yield return null;
            }

            private readonly struct Variant
            {
                public readonly string Name;
                public readonly int Msaa;
                public readonly bool Hdr;

                public Variant(string name, int msaa, bool hdr)
                {
                    Name = name;
                    Msaa = msaa;
                    Hdr = hdr;
                }
            }
        }
    }
}
#endif
