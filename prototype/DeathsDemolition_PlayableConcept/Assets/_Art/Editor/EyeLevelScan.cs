using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Art;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-074 B6 · Eye-level cleanliness scan of World_V6_Greybox (edit mode, no PlayMode, no Player): eye points on the walkable
    /// NavMesh every <see cref="GridStep"/> m at the FP (1.6 m) and TP (2.2 m) heights, eight yaw directions each, two low-resolution
    /// renders per view. The look pass carries the runtime look the bootstrap adds (v6 layout environment, the baked sun on, the
    /// global volume, post-processing on the camera; the cloud cookie and drift stay runtime-only). The mask pass clears the sky to
    /// pure magenta with post-processing off. Reported per view:
    /// HOLE = mask magenta under the horizon whose ray meets the ground plane inside the dressed world (±<see cref="GreyboxV6Builder.VistaOuter"/>):
    /// the ground, a deck or a wall is missing there;
    /// EDGE = mask magenta under the horizon whose ray leaves the dressed world before it meets the ground: the far backdrop (horizon
    /// plates, the panorama reveal of dd4e5bff) is what covers it at runtime — measured, a backdrop matter, not a world hole;
    /// ERROR = look-pass pixels that are the error shader's pure magenta (a missing or broken material);
    /// BLACK = look-pass pixels at or under sRGB <see cref="BlackLevel"/> (as graded — what the player sees).
    /// Back faces are not detected (URP has no replacement shaders; E-1 stays a manual/AD check). The scene is never saved.
    /// Output (env DD_B6_OUT, a folder): scan.json (every view), summary.txt, PNGs (look | mask) of the worst views per metric.
    /// Batch: -executeMethod DD.Art.Pipeline.EditorTools.EyeLevelScan.Run.
    /// </summary>
    public static class EyeLevelScan
    {
        public const float GridStep = 6f, VerticalFov = 60f, BlackShareFlag = 0.02f;
        public const byte BlackLevel = 4;
        public static readonly float[] EyeHeights = { 1.6f, 2.2f };
        public const int Width = 256, Height = 144, HorizonMargin = 2, WorstToSave = 8;
        private static readonly Color Magenta = new Color(1f, 0f, 1f, 1f);

        [Serializable] private sealed class View { public float x, y, z, yaw; public int holePx, edgePx, errorPx, blackPx; public float holeShare, edgeShare, errorShare, blackShare; }
        [Serializable] private sealed class Report
        {
            public string scene, unity, look;
            public int points, views, viewsWithHole, viewsWithEdge, viewsWithError, viewsWithBlack;
            public float gridStep;
            public List<View> views_ = new List<View>();
        }

        private sealed class Worst
        {
            private readonly List<(float score, View view, byte[] png)> _items = new List<(float, View, byte[])>();
            public IReadOnlyList<(float score, View view, byte[] png)> Items => _items;
            public bool Wants(float score) => score > 0f && (_items.Count < WorstToSave || score > _items[_items.Count - 1].score);

            public void Add(float score, View view, byte[] png)
            {
                _items.Add((score, view, png));
                _items.Sort((a, b) => b.score.CompareTo(a.score));
                if (_items.Count > WorstToSave) _items.RemoveAt(_items.Count - 1);
            }
        }

        public static void Run()
        {
            string outDir = Environment.GetEnvironmentVariable("DD_B6_OUT");
            if (string.IsNullOrEmpty(outDir)) throw new InvalidOperationException("[B6] set DD_B6_OUT");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);

            WorldLayoutKind previousLayout = WorldLayout.Current;
            var lookRoot = new GameObject("B6 Look (not saved)");
            string look = ApplyRuntimeLook(lookRoot.transform);

            var go = new GameObject("B6 Scan Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = VerticalFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1200f;
            cam.enabled = false;
            WorldAtmosphere.ConfigureCamera(cam);
            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            bool postFx = camData.renderPostProcessing;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "B6 Scan" };
            var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            cam.targetTexture = rt;
            Material sky = RenderSettings.skybox;

            var report = new Report { scene = GreyboxV6Builder.ScenePath, unity = Application.unityVersion, gridStep = GridStep, look = look };
            Worst worstHole = new Worst(), worstEdge = new Worst(), worstError = new Worst(), worstBlack = new Worst();
            int horizon = Height / 2, below = (horizon - HorizonMargin) * Width;
            float half = GreyboxV6Builder.HalfExtent - 1f, dressed = GreyboxV6Builder.VistaOuter;
            for (float x = -half; x <= half; x += GridStep)
            for (float z = -half; z <= half; z += GridStep)
            {
                if (!NavMesh.SamplePosition(new Vector3(x, 1f, z), out NavMeshHit hit, 1.2f, NavMesh.AllAreas)) continue;
                report.points++;
                foreach (float h in EyeHeights)
                for (int d = 0; d < 8; d++)
                {
                    float yaw = d * 45f;
                    cam.transform.SetPositionAndRotation(hit.position + Vector3.up * h, Quaternion.Euler(0f, yaw, 0f));

                    // Look pass: the scene as the runtime shows it.
                    RenderSettings.skybox = sky;
                    cam.clearFlags = CameraClearFlags.Skybox;
                    camData.renderPostProcessing = postFx;
                    Color32[] lookPx = Grab(cam, rt, read);
                    // Mask pass: sky cleared to magenta, no grade — what stays magenta has no geometry behind it.
                    RenderSettings.skybox = null;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Magenta;
                    camData.renderPostProcessing = false;
                    Color32[] maskPx = Grab(cam, rt, read);
                    RenderSettings.skybox = sky;

                    // Texture rows run bottom → top; rows under the horizon, a margin short of it. An empty ray is a hole when it
                    // would meet the ground (y 0) inside the dressed world, an edge when it leaves the world first.
                    int holePx = 0, edgePx = 0, errorPx = 0, blackPx = 0;
                    for (int i = 0; i < below; i++)
                    {
                        if (!IsMagenta(maskPx[i])) continue;
                        Ray ray = cam.ViewportPointToRay(new Vector3((i % Width + 0.5f) / Width, (i / Width + 0.5f) / Height, 0f));
                        float t = ray.direction.y < -1e-4f ? -ray.origin.y / ray.direction.y : float.PositiveInfinity;
                        Vector3 ground = ray.origin + ray.direction * Mathf.Min(t, 1e5f);
                        if (Mathf.Abs(ground.x) <= dressed && Mathf.Abs(ground.z) <= dressed) holePx++;
                        else edgePx++;
                    }

                    foreach (Color32 c in lookPx)
                    {
                        if (IsMagenta(c)) errorPx++;
                        else if (c.r <= BlackLevel && c.g <= BlackLevel && c.b <= BlackLevel) blackPx++;
                    }

                    var view = new View
                    {
                        x = hit.position.x, y = hit.position.y + h, z = hit.position.z, yaw = yaw,
                        holePx = holePx, edgePx = edgePx, errorPx = errorPx, blackPx = blackPx,
                        holeShare = holePx / (float)below, edgeShare = edgePx / (float)below,
                        errorShare = errorPx / (float)lookPx.Length, blackShare = blackPx / (float)lookPx.Length,
                    };
                    report.views_.Add(view);
                    report.views++;
                    if (holePx > 0) report.viewsWithHole++;
                    if (edgePx > 0) report.viewsWithEdge++;
                    if (errorPx > 0) report.viewsWithError++;
                    if (view.blackShare > BlackShareFlag) report.viewsWithBlack++;

                    byte[] png = null;
                    Consider(worstHole, view.holeShare, view, lookPx, maskPx, ref png);
                    Consider(worstEdge, view.edgeShare, view, lookPx, maskPx, ref png);
                    Consider(worstError, view.errorShare, view, lookPx, maskPx, ref png);
                    Consider(worstBlack, view.blackShare, view, lookPx, maskPx, ref png);
                }
            }

            RenderSettings.skybox = sky;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(lookRoot);
            rt.Release();
            WorldLayout.SetCurrent(previousLayout);

            var sb = new StringBuilder();
            sb.AppendLine("[B6] points " + report.points + " views " + report.views + " (grid " + GridStep + " m × heights 1.6/2.2 × 8 yaw); look: " + look);
            sb.AppendLine("[B6] views with HOLE: " + report.viewsWithHole + " · EDGE: " + report.viewsWithEdge + " · ERROR: " + report.viewsWithError +
                          " · BLACK > " + BlackShareFlag.ToString("P0", CultureInfo.InvariantCulture) + ": " + report.viewsWithBlack);
            Save(outDir, "hole", worstHole, sb);
            Save(outDir, "edge", worstEdge, sb);
            Save(outDir, "error", worstError, sb);
            Save(outDir, "black", worstBlack, sb);
            File.WriteAllText(Path.Combine(outDir, "scan.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(outDir, "summary.txt"), sb.ToString());
            Debug.Log(sb.ToString());
        }

        /// <summary>What <c>Ap005WorldBootstrap</c> adds at runtime that the saved scene does not carry: the v6 layout's environment
        /// (sky v3, fog 90–300 m, cold ambient), the baked sun enabled, the global volume from the bootstrap's own profile.</summary>
        private static string ApplyRuntimeLook(Transform root)
        {
            WorldLayout.SetCurrent(WorldLayoutKind.V6);
            WorldAtmosphere.ApplyEnvironment();
            Light sun = WorldAtmosphere.FindBakedSun();
            if (sun != null)
            {
                sun.enabled = true;
                RenderSettings.sun = sun;
            }

            var bootstrap = UnityEngine.Object.FindObjectOfType<Ap005WorldBootstrap>();
            var profile = bootstrap != null ? new SerializedObject(bootstrap).FindProperty("_volumeProfile").objectReferenceValue as VolumeProfile : null;
            WorldAtmosphere.EnsureVolume(root, profile);
            return "v6 environment, sun " + (sun != null ? "baked on" : "none") + ", volume " + (profile != null ? profile.name : "in-memory default") +
                   ", post " + (WorldAtmosphere.PostFx.Enabled ? "on" : "off");
        }

        private static bool IsMagenta(Color32 c) => c.r > 250 && c.g < 5 && c.b > 250;

        private static void Consider(Worst worst, float score, View view, Color32[] look, Color32[] mask, ref byte[] png)
        {
            if (!worst.Wants(score)) return;
            png ??= EncodeSideBySide(look, mask);
            worst.Add(score, view, png);
        }

        private static void Save(string outDir, string metric, Worst worst, StringBuilder sb)
        {
            sb.AppendLine("  worst " + metric + ":");
            for (int i = 0; i < worst.Items.Count; i++)
            {
                View v = worst.Items[i].view;
                string name = string.Format(CultureInfo.InvariantCulture, "{0}-{1:00}_x{2:F0}_z{3:F0}_h{4:F1}_yaw{5:F0}.png", metric, i, v.x, v.z, v.y, v.yaw);
                File.WriteAllBytes(Path.Combine(outDir, name), worst.Items[i].png);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    x {0:F1} z {1:F1} eye {2:F1} yaw {3:F0}: hole {4:P2} edge {5:P2} error {6:P2} black {7:P2}",
                    v.x, v.z, v.y, v.yaw, v.holeShare, v.edgeShare, v.errorShare, v.blackShare));
            }
        }

        private static Color32[] Grab(Camera cam, RenderTexture rt, Texture2D read)
        {
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            read.Apply(false);
            RenderTexture.active = prev;
            return read.GetPixels32();
        }

        private static byte[] EncodeSideBySide(Color32[] look, Color32[] mask)
        {
            var tex = new Texture2D(Width * 2, Height, TextureFormat.RGBA32, false);
            var both = new Color32[Width * 2 * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                both[y * Width * 2 + x] = look[y * Width + x];
                both[y * Width * 2 + Width + x] = mask[y * Width + x];
            }

            tex.SetPixels32(both);
            tex.Apply(false);
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            return png;
        }
    }
}
