using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-092 (R3) · Before / after evidence for the V6 world in the top-down camera only:
    /// <list type="bullet">
    /// <item><see cref="RenderPoses"/>: the eight fixed TD frames at the gameplay rig's own framing (focus = feet + 1 m, camera at
    /// focus + (0, 15, −13), look at the focus, vertical FOV 42°: FeelConfig CameraFeelData defaults), 1920×1080, runtime look.</item>
    /// <item><see cref="RenderTopMap"/>: an orthographic plan of ±<see cref="MapHalf"/> m (the layout plan's base).</item>
    /// <item><see cref="MeasureEmptyArea"/>: the share of the walkable play area (NavMesh, ±55 m) whose 6 m radius holds no
    /// object or plant — WP-092's "boş alan oranı" (target ≤ 15 %) — with a mask PNG for the layout team.</item>
    /// </list>
    /// Env: DD_TD_OUT (folder). Nothing is saved to the scene. Runs in batch (method-gfx).
    /// </summary>
    public static class WorldTdCapture
    {
        public const int Width = 1920, Height = 1080, MapPixels = 2048;
        public const float MapHalf = 90f, PlayHalf = 55f, EmptyRadius = 6f, MinContentHeight = 0.12f;
        public static readonly Vector3 RigOffset = new Vector3(0f, 15f, -13f);
        public const float RigFov = 42f, FocusHeight = 1f;

        /// <summary>The eight fixed TD poses (feet XZ): plaza, palace front, west grove, sakura court, south pond, NE garden, SW corner, NW kairō.</summary>
        public static readonly (string name, Vector2 feet)[] Poses =
        {
            ("td1-plaza", new Vector2(0f, 0f)), ("td2-palace", new Vector2(0f, 30f)), ("td3-grove-w", new Vector2(-45f, 0f)),
            ("td4-sakura", new Vector2(36f, 1f)), ("td5-pond-s", new Vector2(0f, -30f)), ("td6-garden-ne", new Vector2(40f, 38f)),
            ("td7-corner-sw", new Vector2(-40f, -40f)), ("td8-kairo-nw", new Vector2(-25f, 28f)),
        };

        /// <summary>Shaders that are ground, water or air, never "content" for the empty-area measure.</summary>
        private static readonly string[] NonContentShaders =
        {
            "DD/GroundTiles", "DD/GroundMoss", "DD/GroundBlend", "DD/GroundTerrain", "DD/Water", "DD/FogSheet", "DD/ShaftCard", "DD/SkyPlate",
            "DD/SkyPanorama", "DD/UnderstorySkirt", "DD/SunDisc",
        };

        public static void RenderPoses() => WithLook(cam =>
        {
            string dir = OutDir();
            var bareRows = new StringBuilder();
            float bareSum = 0f;
            foreach ((string name, Vector2 feet) in Poses)
            {
                Vector3 ground = Ground(feet);
                Vector3 focus = ground + Vector3.up * FocusHeight;
                cam.orthographic = false;
                cam.fieldOfView = RigFov;
                cam.transform.SetPositionAndRotation(focus + RigOffset, Quaternion.LookRotation(-RigOffset.normalized, Vector3.up));
                Save(Grab(cam, Width, Height), Path.Combine(dir, name + ".png"));
                float bare = BareGroundShare(cam);
                bareRows.Append(bareRows.Length > 0 ? ", " : "").Append("\"" + name + "\": " + F(bare));
                bareSum += bare;
            }

            File.WriteAllText(Path.Combine(dir, "bare-ground.json"), "{" + bareRows + ", \"mean\": " + F(bareSum / Poses.Length) + "}");
            Debug.Log("[TDCAP] " + Poses.Length + " TD poses -> " + dir + " bare-ground mean " + F(bareSum / Poses.Length));
        });

        /// <summary>WP-092 B5 · One frame at the TD rig's angle from another offset (the ground study's close looks) → DD_TD_OUT/&lt;name&gt;.png.</summary>
        public static void RenderAt(string name, Vector2 feet, Vector3 offset) => WithLook(cam =>
        {
            Vector3 focus = Ground(feet) + Vector3.up * FocusHeight;
            cam.orthographic = false;
            cam.fieldOfView = RigFov;
            cam.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset.normalized, Vector3.up));
            Save(Grab(cam, Width, Height), Path.Combine(OutDir(), name + ".png"));
        });

        public static void RenderTopMap() => WithLook(cam =>
        {
            cam.orthographic = true;
            cam.orthographicSize = MapHalf;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 150f, 0f), Quaternion.Euler(90f, 0f, 0f));
            string path = Path.Combine(OutDir(), "topmap.png");
            Save(Grab(cam, MapPixels, MapPixels), path);
            Debug.Log("[TDCAP] top map ±" + MapHalf + " m -> " + path);
        });

        public static void MeasureEmptyArea()
        {
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);
            var content = new List<Bounds>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r.sharedMaterial == null) continue;
                Bounds b = r.bounds;
                if (b.size.y < MinContentHeight || b.max.y < 0.15f) continue;
                if (Math.Abs(b.center.x) > PlayHalf + EmptyRadius || Math.Abs(b.center.z) > PlayHalf + EmptyRadius) continue;
                if (Array.IndexOf(NonContentShaders, r.sharedMaterial.shader.name) >= 0) continue;
                content.Add(b);
            }

            const float cell = 6f;
            var buckets = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < content.Count; i++)
            {
                Bounds b = content[i];
                for (int x = Mathf.FloorToInt((b.min.x - EmptyRadius) / cell); x <= Mathf.FloorToInt((b.max.x + EmptyRadius) / cell); x++)
                for (int z = Mathf.FloorToInt((b.min.z - EmptyRadius) / cell); z <= Mathf.FloorToInt((b.max.z + EmptyRadius) / cell); z++)
                {
                    if (!buckets.TryGetValue((x, z), out List<int> list)) buckets[(x, z)] = list = new List<int>();
                    list.Add(i);
                }
            }

            int side = Mathf.RoundToInt(PlayHalf * 2f);
            var mask = new Texture2D(side, side, TextureFormat.RGBA32, false);
            int walk = 0, empty = 0;
            for (int gx = 0; gx < side; gx++)
            for (int gz = 0; gz < side; gz++)
            {
                var p = new Vector3(-PlayHalf + gx + 0.5f, 0.3f, -PlayHalf + gz + 0.5f);
                if (!NavMesh.SamplePosition(p, out NavMeshHit h, 0.5f, NavMesh.AllAreas)) { mask.SetPixel(gx, gz, Color.black); continue; }
                walk++;
                bool near = false;
                if (buckets.TryGetValue((Mathf.FloorToInt(h.position.x / cell), Mathf.FloorToInt(h.position.z / cell)), out List<int> ids))
                    foreach (int i in ids)
                    {
                        Bounds b = content[i];
                        float dx = Mathf.Max(0f, Mathf.Max(b.min.x - h.position.x, h.position.x - b.max.x));
                        float dz = Mathf.Max(0f, Mathf.Max(b.min.z - h.position.z, h.position.z - b.max.z));
                        if (dx * dx + dz * dz <= EmptyRadius * EmptyRadius) { near = true; break; }
                    }

                if (!near) empty++;
                mask.SetPixel(gx, gz, near ? new Color(0.45f, 0.45f, 0.45f) : Color.white);
            }

            mask.Apply(false);
            string dir = OutDir();
            File.WriteAllBytes(Path.Combine(dir, "empty-mask.png"), mask.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(mask);
            float ratio = walk > 0 ? (float)empty / walk : 0f;
            string json = "{\"radiusMeters\": " + F(EmptyRadius) + ", \"playHalfMeters\": " + F(PlayHalf) + ", \"contentRenderers\": " + content.Count +
                          ", \"walkableCells\": " + walk + ", \"emptyCells\": " + empty + ", \"emptyRatio\": " + F(ratio) + "}";
            File.WriteAllText(Path.Combine(dir, "empty-area.json"), json);
            Debug.Log("[TDCAP] empty area " + json);
        }

        /// <summary>
        /// WP-092 · The share of the TD frame that shows bare ground: the same pose rendered with every ground-shader renderer
        /// (and the fog / shaft cards over it) hidden against a black clear; a pixel nothing else covers is bare ground.
        /// Water, plants, props and buildings all count as covered.
        /// </summary>
        private static float BareGroundShare(Camera cam)
        {
            var hidden = new List<Renderer>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r.sharedMaterial == null) continue;
                string shader = r.sharedMaterial.shader.name;
                if (shader == "DD/GroundTiles" || shader == "DD/GroundMoss" || shader == "DD/GroundBlend" || shader == "DD/GroundTerrain" || shader == "DD/FogSheet" || shader == "DD/ShaftCard")
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
            }

            CameraClearFlags flags = cam.clearFlags;
            Color background = cam.backgroundColor;
            var data = cam.GetUniversalAdditionalCameraData();
            bool post = data != null && data.renderPostProcessing;
            try
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                if (data != null) data.renderPostProcessing = false;
                Color32[] px = Grab(cam, Width / 4, Height / 4);
                int bare = 0;
                foreach (Color32 c in px) if (c.r + c.g + c.b <= 6) bare++;
                return (float)bare / px.Length;
            }
            finally
            {
                cam.clearFlags = flags;
                cam.backgroundColor = background;
                if (data != null) data.renderPostProcessing = post;
                foreach (Renderer r in hidden) r.enabled = true;
            }
        }

        /// <summary>Kit families that are architecture, walls, bridges or kerbs: not "content" for the object-empty measure.</summary>
        private static readonly string[] StructureFamilies =
        {
            "wall_kit", "wall_pier", "kairo_", "palace_kit", "gates", "chumon", "yagura", "kura", "teahouse", "pavilion", "azumaya",
            "tsuridono", "engawa", "plank_bridge", "taiko_bridge", "slab_bridge", "granite_kerb", "fence_", "interior_kit", "groundcover",
        };

        /// <summary>
        /// WP-092 · Walkable 1 m cells (±55 m) with no prop or plant within <paramref name="radius"/> m. Props and plants are the
        /// dress renderers whose prefab family is not architecture (<see cref="StructureFamilies"/>), not ground / water / air and
        /// at least <see cref="MinContentHeight"/> tall; one entry per placed object (LOD siblings share it). Writes
        /// object-empty-&lt;r&gt;m.json + mask.
        /// </summary>
        public static void MeasureObjectEmpty()
        {
            float radius = float.TryParse(Environment.GetEnvironmentVariable("DD_TD_OBJECT_RADIUS"), NumberStyles.Float, CultureInfo.InvariantCulture, out float rr) ? rr : 4f;
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);
            var objects = new List<Bounds>();
            var seen = new HashSet<GameObject>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r.sharedMaterial == null) continue;
                if (Array.IndexOf(NonContentShaders, r.sharedMaterial.shader.name) >= 0) continue;
                GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
                if (root == null) continue;   // greybox boxes and builder meshes are not dress content
                string family = PrefabUtility.GetCorrespondingObjectFromSource(root) is GameObject src ? src.name : root.name;
                bool structure = false;
                foreach (string f in StructureFamilies) if (family.IndexOf(f, StringComparison.Ordinal) >= 0) { structure = true; break; }
                if (structure || !seen.Add(root)) continue;
                Bounds b = r.bounds;
                foreach (Renderer other in root.GetComponentsInChildren<Renderer>()) b.Encapsulate(other.bounds);
                if (b.size.y < MinContentHeight) continue;
                objects.Add(b);
            }

            int side = Mathf.RoundToInt(PlayHalf * 2f), walk = 0, empty = 0;
            var mask = new Texture2D(side, side, TextureFormat.RGBA32, false);
            for (int gx = 0; gx < side; gx++)
            for (int gz = 0; gz < side; gz++)
            {
                var p = new Vector3(-PlayHalf + gx + 0.5f, 0.3f, -PlayHalf + gz + 0.5f);
                if (!NavMesh.SamplePosition(p, out NavMeshHit h, 0.5f, NavMesh.AllAreas)) { mask.SetPixel(gx, gz, Color.black); continue; }
                walk++;
                bool near = false;
                foreach (Bounds b in objects)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(b.min.x - h.position.x, h.position.x - b.max.x));
                    float dz = Mathf.Max(0f, Mathf.Max(b.min.z - h.position.z, h.position.z - b.max.z));
                    if (dx * dx + dz * dz <= radius * radius) { near = true; break; }
                }

                if (!near) empty++;
                mask.SetPixel(gx, gz, near ? new Color(0.45f, 0.45f, 0.45f) : Color.white);
            }

            mask.Apply(false);
            string dir = OutDir(), tag = F(radius) + "m";
            File.WriteAllBytes(Path.Combine(dir, "object-empty-" + tag + "-mask.png"), mask.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(mask);
            string json = "{\"radiusMeters\": " + F(radius) + ", \"objects\": " + objects.Count + ", \"walkableCells\": " + walk + ", \"emptyCells\": " + empty +
                          ", \"emptyRatio\": " + F(walk > 0 ? (float)empty / walk : 0f) + "}";
            File.WriteAllText(Path.Combine(dir, "object-empty-" + tag + ".json"), json);
            Debug.Log("[TDCAP] object-empty " + json);
        }

        private static void WithLook(Action<Camera> body)
        {
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);
            WorldLayoutKind previous = WorldLayout.Current;
            var root = new GameObject("TDCAP (not saved)");
            WorldLayout.SetCurrent(WorldLayoutKind.V6);
            WorldAtmosphere.ApplyEnvironment();
            Light sun = WorldAtmosphere.FindBakedSun();
            if (sun != null) { sun.enabled = true; RenderSettings.sun = sun; }
            var bootstrap = UnityEngine.Object.FindObjectOfType<Ap005WorldBootstrap>();
            var profile = bootstrap != null ? new SerializedObject(bootstrap).FindProperty("_volumeProfile").objectReferenceValue as VolumeProfile : null;
            Volume volume = WorldAtmosphere.EnsureVolume(root.transform, profile);
            FilmGrain grain = null;
            bool grainWas = false;
            if (volume != null && volume.sharedProfile != null && volume.sharedProfile.TryGet(out grain)) { grainWas = grain.active; grain.active = false; }

            var go = new GameObject("TDCAP Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.enabled = false;
            WorldAtmosphere.ConfigureCamera(cam);
            IDisposable vegetation = VegetationPreview(SceneManager.GetActiveScene());
            try { body(cam); }
            finally
            {
                vegetation?.Dispose();
                if (grain != null) grain.active = grainWas;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(root);
                WorldLayout.SetCurrent(previous);
            }
        }

        /// <summary>
        /// WP-092 / WP-093 (C-00 08.10 (b)) · S-07's editor-only vegetation preview, when the project has it: the runtime's deterministic
        /// population (same seed, same masks) drawn for the capture camera, so the frames and the bare-ground share see the grass. Found by
        /// reflection (this branch builds without WP-093); absent → the frames are as before and the log says so.
        /// </summary>
        public const string VegetationPreviewType = "DD.Prototype.World.Vegetation.VegetationPreview";

        private static IDisposable VegetationPreview(Scene world)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(VegetationPreviewType, false);
                if (type == null) continue;
                FieldInfo tier = type.GetField("TierHigh", BindingFlags.Public | BindingFlags.Static);
                MethodInfo populate = type.GetMethod("Populate", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Scene), typeof(int) }, null);
                if (populate == null) { Debug.LogWarning("[TDCAP] vegetation preview: " + VegetationPreviewType + " has no Populate(Scene, int)"); return null; }
                int high = tier != null ? Convert.ToInt32(tier.GetValue(null)) : 1;
                Debug.Log("[TDCAP] vegetation preview: on (tier " + high + ")");
                return populate.Invoke(null, new object[] { world, high }) as IDisposable;
            }

            Debug.Log("[TDCAP] vegetation preview: absent (WP-093 not in this project) — ground cover without the grass");
            return null;
        }

        private static Vector3 Ground(Vector2 xz)
        {
            var top = new Vector3(xz.x, 30f, xz.y);
            if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
            return NavMesh.SamplePosition(new Vector3(xz.x, 0f, xz.y), out NavMeshHit h, 3f, NavMesh.AllAreas) ? h.position : new Vector3(xz.x, 0f, xz.y);
        }

        private static Color32[] Grab(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                cam.targetTexture = rt;
                cam.aspect = (float)w / h;
                cam.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                read.Apply(false);
                RenderTexture.active = prev;
                return read.GetPixels32();
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        private static void Save(Color32[] px, string path)
        {
            int w = px.Length == MapPixels * MapPixels ? MapPixels : Width, h = px.Length == MapPixels * MapPixels ? MapPixels : Height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static string OutDir()
        {
            string dir = Environment.GetEnvironmentVariable("DD_TD_OUT");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("[TDCAP] set DD_TD_OUT");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>All of them in one batch run (one scene open each; the look is applied per render).</summary>
        public static void RunAll()
        {
            RenderTopMap();
            RenderPoses();
            MeasureEmptyArea();
            MeasureObjectEmpty();
            if (File.Exists(WorldV5Layout.LayoutJson)) RenderLayoutPoses();
        }

        /// <summary>
        /// WP-092 · The layout plan's own frames, at the gameplay rig's framing (<c>layout/</c>): every plan building at its centre with the roofs
        /// over it hidden — the top-down fade's end point (the game keeps them at 22 %: the room reads, the roof's ghost stays) — and every
        /// moment (M1–M8) at its centre. The eight fixed poses stay the before / after pair; these show what the plan added.
        /// </summary>
        public static void RenderLayoutPoses() => WithLook(cam =>
        {
            string dir = Path.Combine(OutDir(), "layout");
            Directory.CreateDirectory(dir);
            Dictionary<string, object> json = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(WorldV5Layout.LayoutJson)), "layout");
            var poses = new List<(string name, Vector2 feet, Rect? roofs)>();
            foreach (object o in ArtJson.AsList(ArtJson.Require(json, "buildings"), "buildings"))
            {
                Dictionary<string, object> b = ArtJson.AsObject(o, "building");
                List<object> r = ArtJson.AsList(ArtJson.Require(b, "rect"), "rect");
                Rect rect = Rect.MinMaxRect(Mathf.Min(ArtJson.AsFloat(r[0]), ArtJson.AsFloat(r[2])), Mathf.Min(ArtJson.AsFloat(r[1]), ArtJson.AsFloat(r[3])),
                                            Mathf.Max(ArtJson.AsFloat(r[0]), ArtJson.AsFloat(r[2])), Mathf.Max(ArtJson.AsFloat(r[1]), ArtJson.AsFloat(r[3])));
                // R1 (33 × 10 m) and K1 (36 m long) are larger than the view: their frame sits on the plan's lamps there (the hall's west altar
                // side, K1's middle); the others at their centre.
                string id = ArtJson.AsString(ArtJson.Require(b, "id"));
                Vector2 feet = id == "R1" ? new Vector2(-6f, 43.5f) : id == "K1" ? new Vector2(-33f, 4f) : rect.center;
                poses.Add(("bld-" + id, feet, rect));
            }

            object moments = ArtJson.Get(json, "moments");
            if (moments != null)
                foreach (object o in ArtJson.AsList(moments, "moments"))
                {
                    Dictionary<string, object> m = ArtJson.AsObject(o, "moment");
                    List<object> c = ArtJson.AsList(ArtJson.Require(m, "center"), "center");
                    poses.Add(("mom-" + ArtJson.AsString(ArtJson.Require(m, "id")), new Vector2(ArtJson.AsFloat(c[0]), ArtJson.AsFloat(c[1])), null));
                }

            Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
            foreach ((string name, Vector2 feet, Rect? roofs) in poses)
            {
                var hidden = new List<Renderer>();
                if (roofs.HasValue)
                {
                    Rect area = Rect.MinMaxRect(roofs.Value.xMin - 1.5f, roofs.Value.yMin - 1.5f, roofs.Value.xMax + 1.5f, roofs.Value.yMax + 1.5f);
                    foreach (Renderer r in all)
                    {
                        if (!r.enabled || !area.Contains(new Vector2(r.bounds.center.x, r.bounds.center.z)) || !UnderRoof(r.transform)) continue;
                        r.enabled = false;
                        hidden.Add(r);
                    }
                }

                Vector3 focus = Ground(feet) + Vector3.up * FocusHeight;
                cam.orthographic = false;
                cam.fieldOfView = RigFov;
                cam.transform.SetPositionAndRotation(focus + RigOffset, Quaternion.LookRotation(-RigOffset.normalized, Vector3.up));
                Save(Grab(cam, Width, Height), Path.Combine(dir, name + ".png"));
                foreach (Renderer r in hidden) r.enabled = true;
            }

            Debug.Log("[TDCAP] " + poses.Count + " layout poses -> " + dir);
        });

        /// <summary>A roof piece: under a kit Roof child, the hall's roof holder, or a roof / rafters mesh.</summary>
        private static bool UnderRoof(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.name == ArtLibraryPrefabBuilder.RoofName || p.name == WorldV5Dress.HallRoofName) return true;
                if (p.name.StartsWith("roof_LOD", StringComparison.Ordinal) || p.name.StartsWith("rafters_LOD", StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
