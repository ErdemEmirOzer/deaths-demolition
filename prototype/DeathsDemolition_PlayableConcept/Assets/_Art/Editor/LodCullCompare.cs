using System;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-074 B7 · What the eye-level lodBias culls (edit mode; no PlayMode, no Player; the scene is never saved): from fixed eye
    /// points, one render per tier lodBias (the Quality level's base — the runtime value) and mode FOV and one reference with every LOD
    /// forced far (lodBias 100), in the runtime look. Writes, per view, a strip [reference | tier/mode renders] and the share of
    /// pixels that differ by more than <see cref="DiffLevel"/> from the reference. Output: env DD_B7_OUT (a folder).
    /// </summary>
    public static class LodCullCompare
    {
        public const int Width = 480, Height = 270, DiffLevel = 24;
        public const float ReferenceBias = 100f;

        private static readonly (string name, Vector3 eye, float yaw)[] Views =
        {
            ("plaza-N", new Vector3(0f, 1.6f, -6f), 0f), ("plaza-S", new Vector3(0f, 1.6f, 6f), 180f),
            ("plaza-E", new Vector3(-6f, 1.6f, 0f), 90f), ("plaza-W", new Vector3(6f, 1.6f, 0f), 270f),
            ("corner-SW-to-NE", new Vector3(-48f, 1.6f, -48f), 45f), ("corner-NE-to-SW", new Vector3(48f, 1.6f, 48f), 225f),
        };

        private static readonly (string name, float bias, float vfov)[] Cameras =
        {
            // The runtime lodBias is the tier's base (QualityTierSetup); CameraModeProfile.LodBiasFactor is not applied anywhere.
            ("Cinematic 3 TP", 3f, 60f), ("High 2 TP", 2f, 60f), ("Performance 1 TP", 1f, 60f), ("High 2 FP", 2f, 58.7f),
        };

        public static void Run()
        {
            string outDir = Environment.GetEnvironmentVariable("DD_B7_OUT");
            if (string.IsNullOrEmpty(outDir)) throw new InvalidOperationException("[B7] set DD_B7_OUT");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);

            WorldLayoutKind previousLayout = WorldLayout.Current;
            float previousBias = QualitySettings.lodBias;
            var lookRoot = new GameObject("B7 Look (not saved)");
            WorldLayout.SetCurrent(WorldLayoutKind.V6);
            WorldAtmosphere.ApplyEnvironment();
            Light sun = WorldAtmosphere.FindBakedSun();
            if (sun != null) { sun.enabled = true; RenderSettings.sun = sun; }
            var bootstrap = UnityEngine.Object.FindObjectOfType<Ap005WorldBootstrap>();
            var profile = bootstrap != null ? new SerializedObject(bootstrap).FindProperty("_volumeProfile").objectReferenceValue as VolumeProfile : null;
            WorldAtmosphere.EnsureVolume(lookRoot.transform, profile);

            var go = new GameObject("B7 Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 450f;
            cam.enabled = false;
            WorldAtmosphere.ConfigureCamera(cam);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            cam.targetTexture = rt;

            var sb = new StringBuilder("view\tcamera\tdiffShare\n");
            foreach ((string name, Vector3 eye, float yaw) in Views)
            {
                Vector3 p = eye;
                if (UnityEngine.AI.NavMesh.SamplePosition(new Vector3(eye.x, 1f, eye.z), out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) p = hit.position + Vector3.up * eye.y;
                cam.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                var strip = new Texture2D(Width * (Cameras.Length + 1), Height, TextureFormat.RGBA32, false);
                foreach ((string cname, float bias, float vfov) in Cameras)
                {
                    cam.fieldOfView = vfov;
                    QualitySettings.lodBias = ReferenceBias;
                    Color32[] reference = Grab(cam, rt, read);
                    QualitySettings.lodBias = bias;
                    Color32[] tier = Grab(cam, rt, read);
                    int diff = 0;
                    for (int i = 0; i < tier.Length; i++)
                        if (Math.Abs(tier[i].r - reference[i].r) + Math.Abs(tier[i].g - reference[i].g) + Math.Abs(tier[i].b - reference[i].b) > DiffLevel) diff++;
                    int column = Array.FindIndex(Cameras, c => c.name == cname) + 1;
                    if (column == 1) strip.SetPixels32(0, 0, Width, Height, reference);
                    strip.SetPixels32(column * Width, 0, Width, Height, tier);
                    sb.Append(name).Append('\t').Append(cname).Append('\t').Append((diff / (float)tier.Length).ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
                }

                strip.Apply(false);
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), strip.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(strip);
            }

            QualitySettings.lodBias = previousBias;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(lookRoot);
            rt.Release();
            WorldLayout.SetCurrent(previousLayout);
            File.WriteAllText(Path.Combine(outDir, "diff.tsv"), sb.ToString());
            Debug.Log("[B7] cull compare: strips [reference lodBias " + ReferenceBias + " | " + string.Join(" | ", Array.ConvertAll(Cameras, c => c.name)) + "]\n" + sb);
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
    }
}
