using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-092 C · TD check frames for an imported library (AP-030 by default): the variations of a set of families on a grid in an
    /// unsaved scene with the showcase lighting (Run environment, sun, overcast volume), framed by the gameplay rig (focus = ground +
    /// <see cref="WorldTdCapture.FocusHeight"/>, camera at focus + <see cref="WorldTdCapture.RigOffset"/> — 49° down, ≈ 20 m — vertical FOV
    /// <see cref="WorldTdCapture.RigFov"/>), 1920×1080, post-processing on (bloom reads the lamp glow). Three frames per set: <c>day</c>,
    /// <c>dusk</c> (sun and ambient at a few per cent: the glow slots carry the frame) and <c>dusk-off</c> (the glow renderers' emission
    /// black through a property block — the materials are never touched). Hanging pieces (pivot at the ceiling: bounds top at 0) hang
    /// from 2.4 m, wall pieces (<c>wall_*</c>) sit 1.6 m up on a stand-in wall panel, turned to face the camera.
    /// Sets: <c>-ddSet &lt;tag&gt;:&lt;family+family…|*&gt;:&lt;columns&gt;:&lt;spacing m&gt;</c>, repeatable (v002: roads, clusters, lamps,
    /// interior); none = every variation, 6 columns at 2.4 m, tag <c>ap030</c>. Files <c>&lt;tag&gt;-td-{day,dusk,dusk-off}.png</c>.
    /// Batch: <c>-executeMethod …ArtAp030Preview.CaptureTd -ddOut &lt;folder&gt; [-ddAp AP-030] [-ddSet …]…</c>. Nothing is saved.
    /// </summary>
    public static class ArtAp030Preview
    {
        public const int Width = 1920, Height = 1080, DefaultColumns = 6;
        public const float DefaultSpacing = 2.4f, HangHeight = 2.4f, WallMountHeight = 1.6f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private sealed class Set
        {
            public string Tag;
            public HashSet<string> Families;   // null = every family
            public int Columns = DefaultColumns;
            public float Spacing = DefaultSpacing;
        }

        public static void CaptureTd()
        {
            string dir = Arg("-ddOut") ?? Environment.GetEnvironmentVariable("DD_AP030_OUT");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("[AP030PREVIEW] pass -ddOut <folder> (or DD_AP030_OUT)");
            string ap = Arg("-ddAp") ?? "AP-030";
            Directory.CreateDirectory(dir);

            var index = AssetDatabase.LoadAssetAtPath<ArtLibraryAsset>(ArtLibraryPaths.IndexAsset(ap));
            if (index == null) throw new InvalidOperationException("[AP030PREVIEW] library index missing: " + ArtLibraryPaths.IndexAsset(ap));
            ArtLibraryManifest manifest = ArtLibraryPrefabBuilder.LoadManifest(ap);

            var sets = new List<Set>();
            foreach (string spec in Args("-ddSet"))
            {
                string[] p = spec.Split(':');
                if (p.Length != 4) throw new ArgumentException("[AP030PREVIEW] -ddSet <tag>:<family+family…|*>:<columns>:<spacing> — got " + spec);
                sets.Add(new Set
                {
                    Tag = p[0],
                    Families = p[1] == "*" ? null : new HashSet<string>(p[1].Split('+'), StringComparer.Ordinal),
                    Columns = int.Parse(p[2], CultureInfo.InvariantCulture),
                    Spacing = float.Parse(p[3], CultureInfo.InvariantCulture)
                });
            }

            if (sets.Count == 0) sets.Add(new Set { Tag = "ap030" });
            foreach (Set set in sets) CaptureSet(ap, index, manifest, dir, set);
        }

        private static void CaptureSet(string ap, ArtLibraryAsset index, ArtLibraryManifest manifest, string dir, Set set)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PilotSceneBuilder.ApplyEnvironment();
            PilotSceneBuilder.CreateSun();
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtLibraryPaths.OvercastVolumeProfile);
            if (profile != null) PilotSceneBuilder.CreateVolume(profile);
            Light sun = RenderSettings.sun;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground (stand-in)";
            ground.transform.localScale = new Vector3(8f, 1f, 8f);
            var earth = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("earth"));
            if (earth != null) ground.GetComponent<MeshRenderer>().sharedMaterial = earth;

            // Lamps (variations with a flame socket) first, nearest the camera; then the rest, both in index order.
            var items = new List<(ArtLibraryAsset.Item item, ArtLibraryManifest.Variation v)>();
            var rest = new List<(ArtLibraryAsset.Item item, ArtLibraryManifest.Variation v)>();
            foreach (ArtLibraryAsset.Item item in index.items)
            {
                if (set.Families != null && !set.Families.Contains(item.family)) continue;
                ArtLibraryManifest.Variation v = Find(manifest, item.family, item.variation);
                if (item.prefab != null && v != null) (v.sockets.Exists(s => s.name == "SOCKET_Flame") ? items : rest).Add((item, v));
            }

            items.AddRange(rest);
            var glow = new List<Renderer>();
            int rows = (items.Count + set.Columns - 1) / set.Columns;
            for (int i = 0; i < items.Count; i++)
            {
                int col = i % set.Columns, row = i / set.Columns;
                var at = new Vector3((col - (set.Columns - 1) * 0.5f) * set.Spacing, 0f, (row - (rows - 1) * 0.5f) * set.Spacing);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(items[i].item.prefab);
                bool hanging = items[i].v.boundsMax != null && items[i].v.boundsMax[2] <= 0.01f;
                bool wall = items[i].item.family.StartsWith("wall_", StringComparison.Ordinal);
                if (hanging) at.y = HangHeight;
                if (wall)
                {
                    at.y = WallMountHeight;
                    go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // its front (+Z) to the camera, the wall behind it
                    var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    panel.name = "Wall (stand-in) " + items[i].v.prefab;
                    panel.transform.position = new Vector3(at.x, 1.2f, at.z + 0.04f);
                    panel.transform.localScale = new Vector3(1.2f, 2.4f, 0.08f);
                }

                go.transform.position = at;
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterial != null && r.sharedMaterial.IsKeywordEnabled("_EMISSION")) glow.Add(r);
            }

            var camGo = new GameObject("AP030PREVIEW Camera") { hideFlags = HideFlags.HideAndDontSave };
            bool forceAll = Texture.streamingTextureForceLoadAll;
            try
            {
                Texture.streamingTextureForceLoadAll = true;
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 600f;
                cam.fieldOfView = WorldTdCapture.RigFov;
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;
                Vector3 focus = Vector3.up * WorldTdCapture.FocusHeight;
                cam.transform.SetPositionAndRotation(focus + WorldTdCapture.RigOffset, Quaternion.LookRotation(-WorldTdCapture.RigOffset.normalized, Vector3.up));

                Render(cam, Path.Combine(dir, set.Tag + "-td-day.png"));

                if (sun != null) sun.intensity *= 0.06f;
                RenderSettings.ambientSkyColor *= 0.08f;
                RenderSettings.ambientEquatorColor *= 0.08f;
                RenderSettings.ambientGroundColor *= 0.08f;
                RenderSettings.fogColor *= 0.15f;
                RenderSettings.reflectionIntensity *= 0.1f;
                Render(cam, Path.Combine(dir, set.Tag + "-td-dusk.png"));

                var block = new MaterialPropertyBlock();
                block.SetColor(EmissionColorId, Color.black);
                foreach (Renderer r in glow) r.SetPropertyBlock(block);
                Render(cam, Path.Combine(dir, set.Tag + "-td-dusk-off.png"));
                foreach (Renderer r in glow) r.SetPropertyBlock(null);
                Debug.Log("[AP030PREVIEW] " + ap + " " + set.Tag + ": " + items.Count + " variations, " + glow.Count + " emissive renderers, frames in " + dir);
            }
            finally
            {
                Texture.streamingTextureForceLoadAll = forceAll;
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        private static ArtLibraryManifest.Variation Find(ArtLibraryManifest m, string family, string id)
        {
            foreach (ArtLibraryManifest.Family f in m.families)
                if (f.family == family)
                    foreach (ArtLibraryManifest.Variation v in f.variations)
                        if (v.id == id) return v;
            return null;
        }

        private static void Render(Camera cam, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            try
            {
                cam.targetTexture = rt;
                cam.aspect = (float)Width / Height;
                cam.Render();
                cam.Render();   // the second frame: post-processing history and streamed mips settled
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                read.Apply(false);
                RenderTexture.active = prev;
                File.WriteAllBytes(path, read.EncodeToPNG());
                Debug.Log("[AP030PREVIEW] " + path);
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        private static string Arg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.Ordinal)) return args[i + 1];
            return null;
        }

        private static List<string> Args(string flag)
        {
            var list = new List<string>();
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.Ordinal)) list.Add(args[i + 1]);
            return list;
        }
    }
}
