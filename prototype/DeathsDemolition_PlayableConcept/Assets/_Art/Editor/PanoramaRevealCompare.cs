using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 · Before / after of dd4e5bff (the near horizon plate's bottom edge AA + panorama reveal) from eye-level poses (edit mode;
    /// no PlayMode, no Player; the scene and its materials are never saved): the runtime look, one render with the plate's
    /// <c>_BottomEdgeAA</c> / <c>_BottomPanoramaReveal</c> at 0 (before) and one as shipped (after), side by side, plus the share of
    /// pixels that differ. Output: env DD_PANO_OUT (a folder). Poses: env DD_PANO_POSES, a capture-pose CSV (the DD_ART_CAPTURE_POSES
    /// format: name,px,py,pz,tx,ty,tz,vfov[,…] — absolute eye and look-at points; the elevated HF / sugi poses show the footer); without
    /// it, the built-in eye-level poses on the NavMesh.
    /// </summary>
    public static class PanoramaRevealCompare
    {
        public const int Width = 960, Height = 540, DiffLevel = 12;
        private static readonly string[] Properties = { "_BottomEdgeAA", "_BottomPanoramaReveal" };

        private static readonly (string name, Vector3 eye, float yaw, float pitch, float vfov)[] Views =
        {
            ("plaza-N-TP", new Vector3(0f, 2.2f, -6f), 0f, -2f, 60f), ("plaza-E-TP", new Vector3(-6f, 2.2f, 0f), 90f, -2f, 60f),
            ("plaza-S-TP", new Vector3(0f, 2.2f, 6f), 180f, -2f, 60f), ("plaza-W-TP", new Vector3(6f, 2.2f, 0f), 270f, -2f, 60f),
            ("gallery-N-FP", new Vector3(12f, 1.6f, 54f), 0f, 0f, 58.7f), ("corner-SW-NE-FP", new Vector3(-48f, 1.6f, -48f), 45f, 0f, 58.7f),
            ("corner-NE-SW-FP", new Vector3(48f, 1.6f, 48f), 225f, 0f, 58.7f), ("south-edge-S-FP", new Vector3(-24f, 1.6f, -54f), 180f, 0f, 58.7f),
        };

        public static void Run()
        {
            string outDir = Environment.GetEnvironmentVariable("DD_PANO_OUT");
            if (string.IsNullOrEmpty(outDir)) throw new InvalidOperationException("[PANO] set DD_PANO_OUT");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);

            WorldLayoutKind previousLayout = WorldLayout.Current;
            var lookRoot = new GameObject("PANO Look (not saved)");
            WorldLayout.SetCurrent(WorldLayoutKind.V6);
            WorldAtmosphere.ApplyEnvironment();
            Light sun = WorldAtmosphere.FindBakedSun();
            if (sun != null) { sun.enabled = true; RenderSettings.sun = sun; }
            var bootstrap = UnityEngine.Object.FindObjectOfType<Ap005WorldBootstrap>();
            var profile = bootstrap != null ? new SerializedObject(bootstrap).FindProperty("_volumeProfile").objectReferenceValue as VolumeProfile : null;
            WorldAtmosphere.EnsureVolume(lookRoot.transform, profile);

            // The plate materials that carry the reveal, and their shipped values (restored before the tool returns).
            var shipped = new List<(Material m, string p, float v)>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null)
                        foreach (string p in Properties)
                            if (m.HasProperty(p) && m.GetFloat(p) > 0f && !shipped.Exists(e => e.m == m && e.p == p)) shipped.Add((m, p, m.GetFloat(p)));

            var go = new GameObject("PANO Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1200f;
            cam.enabled = false;
            WorldAtmosphere.ConfigureCamera(cam);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            cam.targetTexture = rt;

            var sb = new StringBuilder("view\tdiffShare\n");
            try
            {
                foreach ((string name, Vector3 position, Quaternion rotation, float vfov) in Poses())
                {
                    cam.transform.SetPositionAndRotation(position, rotation);
                    cam.fieldOfView = vfov;

                    foreach ((Material m, string prop, float _) in shipped) m.SetFloat(prop, 0f);
                    Color32[] before = Grab(cam, rt, read);
                    foreach ((Material m, string prop, float v) in shipped) m.SetFloat(prop, v);
                    Color32[] after = Grab(cam, rt, read);

                    int diff = 0;
                    for (int i = 0; i < after.Length; i++)
                        if (Math.Abs(after[i].r - before[i].r) + Math.Abs(after[i].g - before[i].g) + Math.Abs(after[i].b - before[i].b) > DiffLevel) diff++;
                    var strip = new Texture2D(Width * 2, Height, TextureFormat.RGBA32, false);
                    strip.SetPixels32(0, 0, Width, Height, before);
                    strip.SetPixels32(Width, 0, Width, Height, after);
                    strip.Apply(false);
                    File.WriteAllBytes(Path.Combine(outDir, name + ".png"), strip.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(strip);
                    sb.Append(name).Append('\t').Append((diff / (float)after.Length).ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
                }
            }
            finally
            {
                foreach ((Material m, string prop, float v) in shipped) m.SetFloat(prop, v);
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(lookRoot);
                rt.Release();
                WorldLayout.SetCurrent(previousLayout);
            }

            string summary = "[PANO] " + shipped.Count + " plate properties toggled; strips [before (reveal off) | after (dd4e5bff)]\n" + sb;
            File.WriteAllText(Path.Combine(outDir, "diff.tsv"), sb.ToString());
            File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary);
            Debug.Log(summary);
        }

        private static List<(string name, Vector3 position, Quaternion rotation, float vfov)> Poses()
        {
            var poses = new List<(string, Vector3, Quaternion, float)>();
            string csv = Environment.GetEnvironmentVariable("DD_PANO_POSES");
            if (!string.IsNullOrEmpty(csv))
            {
                foreach (string line in File.ReadAllLines(csv))
                {
                    string[] c = line.Split(',');
                    if (c.Length < 8 || c[0] == "name" || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    var eye = new Vector3(F(c[1]), F(c[2]), F(c[3]));
                    var at = new Vector3(F(c[4]), F(c[5]), F(c[6]));
                    poses.Add((c[0], eye, Quaternion.LookRotation(at - eye, Vector3.up), F(c[7])));
                }

                return poses;
            }

            foreach ((string name, Vector3 eye, float yaw, float pitch, float vfov) in Views)
            {
                Vector3 p = eye;
                if (NavMesh.SamplePosition(new Vector3(eye.x, 1f, eye.z), out NavMeshHit hit, 3f, NavMesh.AllAreas)) p = hit.position + Vector3.up * eye.y;
                poses.Add((name, p, Quaternion.Euler(-pitch, yaw, 0f), vfov));
            }

            return poses;
        }

        private static float F(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);

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
    }
}
