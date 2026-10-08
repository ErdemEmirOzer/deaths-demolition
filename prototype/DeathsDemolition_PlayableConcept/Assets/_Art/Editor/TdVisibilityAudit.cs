using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.CameraSystem;
using DD.Prototype.World;
using Unity.AI.Navigation;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-092 A (R3 #8, S-05) · Which World_V6_Greybox renderers the top-down camera can ever show.
    /// <para><b>Poses.</b> The gameplay rig (FeelConfig camera: focus = feet + 1 m, camera at focus + (0, 15, −13), looking at the
    /// focus, vertical FOV 42°) at every walkable place: the NavMesh on a ≤ 4 m grid (one pose per NavMesh layer under a grid node),
    /// the centroid of every NavMesh triangle no grid pose covers, and the ring edge (NavMesh within 1.5 m of ±55) every 4 m — each
    /// with no look-ahead and 2.5 m toward ±x / ±z. The vertical FOV gets <see cref="FovMarginDegrees"/> (shake, grid gaps).</para>
    /// <para><b>Aspect.</b> One 2.4:1 render per pass (21:9 monitors are 2.37–2.39:1); 16:9 is its centre crop — same vertical FOV
    /// (Hor+), same rays. A unit kept only by the crop's margin is reported as ultra-wide-only.</para>
    /// <para><b>Passes.</b> An object-id render (Hidden/DD/TdVisibilityId: 5- or 6-bit-per-channel ids with tolerance, MSAA / HDR /
    /// post / features off, the planar renderer) with LOD 0 forced and the original alpha tests and cull modes; (A) opaque, (B) the
    /// transparent queue against the opaque depth; and where the frame shows the plaza's planar-reflection receiver (DD/GroundTiles,
    /// dilated 4 px), the same two passes from the mirrored camera (PlanarReflection's matrices, culling flipped). The top-down fader is
    /// emulated per pose: CameraOccluder colliders on the camera → feet + 1.1 m sphere cast (r 0.65) are hidden, so what a faded roof
    /// shows counts.</para>
    /// <para><b>Shadows.</b> A shadow caster no pose sees still stays when its bounds, swept along the sun to 2 m below the ground,
    /// touch any pose frustum cut at the 60 m shadow distance (or a shadow-casting point / spot light's range that touches one).</para>
    /// <para><b>Output.</b> The committed list (<see cref="WorldFptpSplit.ListPath"/>: every movable unit no pose shows, no shadow
    /// reaches and no reflection carries), the per-renderer JSON and the summary with the expected top-down gain (unique mesh / texture
    /// MB, triangles, frustum draw and shadow-caster estimates) in DD_VIS_OUT. Opens the world merged (<see cref="WorldFptpSplit.OpenMerged"/>),
    /// so it reads a split world like a whole one; saves no scene. Batch: <c>-executeMethod …TdVisibilityAudit.RunCli</c> (graphics device).
    /// Flags: <c>-ddVisMaxFeet N</c> (smoke), <c>-ddVisHeight H</c>, <c>-ddVisNoList</c>, <c>-ddVisPrepare</c> (WorldFptpSplit.PrepareCli after).</para>
    /// </summary>
    public static class TdVisibilityAudit
    {
        public const int DefaultHeight = 1080;
        public const float WideAspect = 2.4f, StandardAspect = 16f / 9f;
        public const float FovMarginDegrees = 1.5f;
        public const float GridStep = 4f, PlayHalf = 55f, EdgeBand = 1.5f;
        public const float ShadowDistance = 60f, DirectFar = 1000f, MirrorFar = 6000f, NearClip = 0.3f, GameplayFar = 260f;
        public const float FaderRadius = 0.65f, FaderHeight = 1.1f, ShadowGroundY = -2f;
        public const int ReceiverDilatePixels = 4;
        public const string ShaderPath = "Assets/_Art/Editor/Shaders/DD_TdVisibilityId.shader";
        public const string ReceiverShader = "DD/GroundTiles";
        public const string OutEnv = "DD_VIS_OUT";

        public struct Rig
        {
            public Vector3 Offset;
            public float Fov, FocusHeight, LookAhead;
        }

        /// <summary>The top-down rig as the game frames it (PrototypeCameraRig.ApplyFeel over the packaged FeelConfig).</summary>
        public static Rig GameplayRig()
        {
            var config = Resources.Load<FeelConfig>(FeelConfig.ResourcesPath);
            CameraFeelData d = config != null && config.AuthoredData != null && config.AuthoredData.camera != null ? config.AuthoredData.camera : new CameraFeelData();
            return new Rig
            {
                Offset = Quaternion.Euler(0f, d.yawDegrees, 0f) * new Vector3(0f, d.heightMeters, -d.backDistanceMeters),
                Fov = d.fieldOfViewDegrees,
                FocusHeight = d.focusHeightOffsetMeters,
                LookAhead = d.maxLookAheadMeters
            };
        }

        public struct Pose
        {
            public Vector3 Feet, Camera;
            public Quaternion Rotation;
        }

        // ------------------------------------------------------------------------------------------------ JSON records

        [Serializable]
        public sealed class RendererRecord
        {
            public string path, unit, family, decision, reason;
            public int triangles, max16, max21, reflMax, poses, transparent;
            public long sum21;
            public bool shadowCaster, collider, navRelevant;
        }

        [Serializable]
        public sealed class UnitRecord
        {
            public string key, family, mode, decision, reason;
            public int renderers, triangles, max16, max21, reflMax, poses;
            public bool shadowCaster, collider, navRelevant, staticBatched;
            public float x, y, z;
        }

        [Serializable]
        public sealed class Gain
        {
            public int movedUnits, movedRenderers, proxies, twins, movedTrianglesAllLods, movedTrianglesLod0;
            public int uniqueMeshes, uniqueTextures;
            public float uniqueMeshMb, uniqueTextureMb, staticBatchMb;
            public float drawsMean16Before, drawsMean16After, drawsP95Before16, drawsP95After16;
            public float drawsMean21Before, drawsMean21After;
            public float shadowCastersMeanBefore, shadowCastersMeanAfter;
            public string method;
        }

        [Serializable]
        public sealed class Summary
        {
            public string schema = "dd.td-visibility-audit/v1", utc, scene, contentHash, rule, listPath;
            public int width, height, feet, poses, renders, mirrorPoses, renderers, units, ids, idBits;
            public float fov, fovMargin, aspectWide, seconds;
            public long invalidIdPixels, offCentrePixels, sampledPixels;
            public int pinned, visible, visible16, ultraWideOnly, reflectionOnly, transparentOnly, shadowKept, lightKept, moved;
            public List<string> pinReasons = new List<string>();
            public Gain gain = new Gain();
        }

        [Serializable]
        private sealed class Report
        {
            public Summary summary;
            public List<UnitRecord> units = new List<UnitRecord>();
            public List<RendererRecord> renderers = new List<RendererRecord>();
        }

        // ------------------------------------------------------------------------------------------------ entry

        public static void RunCli()
        {
            string[] args = Environment.GetCommandLineArgs();
            int maxFeet = IntArg(args, "-ddVisMaxFeet", 0);
            int height = IntArg(args, "-ddVisHeight", DefaultHeight);
            bool writeList = Array.IndexOf(args, "-ddVisNoList") < 0;
            string outDir = Environment.GetEnvironmentVariable(OutEnv);
            if (string.IsNullOrEmpty(outDir)) outDir = System.IO.Path.GetFullPath("Library/TdVisibility");
            Directory.CreateDirectory(outDir);
            Summary s = Run(outDir, height, maxFeet, writeList && maxFeet == 0);
            Debug.Log("[TDVIS] done: " + s.moved + " units to " + WorldFptpSplit.SceneName + " of " + s.units + " (" + s.poses + " poses, " + s.renders + " renders, " + s.seconds.ToString("F0", CultureInfo.InvariantCulture) + " s) -> " + outDir);
            if (Array.IndexOf(args, "-ddVisPrepare") >= 0) WorldFptpSplit.PrepareCli();
        }

        private static int IntArg(string[] args, string name, int fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }

        public static Summary Run(string outDir, int height, int maxFeet, bool writeList)
        {
            var clock = Stopwatch.StartNew();
            Scene scene = WorldFptpSplit.OpenMerged();
            Transform worldRoot = WorldFptpSplit.FindRoot(scene, Ap005WorldBootstrap.RootName);
            if (worldRoot == null) throw new InvalidOperationException("[TDVIS] no " + Ap005WorldBootstrap.RootName + " in " + GreyboxV6Builder.ScenePath);
            EnsureNavMesh(worldRoot);
            Physics.SyncTransforms();

            Rig rig = GameplayRig();
            Vector3 sunEuler = WorldFptpSplit.EffectiveSunEuler();
            List<WorldFptpSplit.Unit> units = WorldFptpSplit.Units(worldRoot);
            string hash = WorldFptpSplit.ContentHash(worldRoot, units, sunEuler, WorldFptpSplit.RigSignature(), out string hashInput);
            File.WriteAllText(System.IO.Path.Combine(outDir, "td-visibility-hash-input.txt"), hashInput, new UTF8Encoding(false));

            // ids: every renderer in the scene (units and the rest), 1-based
            var all = new List<Renderer>();
            foreach (GameObject go in scene.GetRootGameObjects()) all.AddRange(go.GetComponentsInChildren<Renderer>(true));
            var idOf = new Dictionary<Renderer, int>(all.Count);
            for (int i = 0; i < all.Count; i++) idOf[all[i]] = i + 1;
            int bits = all.Count < (1 << 15) ? 5 : 6;
            if (all.Count >= (1 << 18)) throw new InvalidOperationException("[TDVIS] " + all.Count + " renderers exceed the 18-bit id space");

            var unitOf = new Dictionary<Renderer, WorldFptpSplit.Unit>();
            foreach (WorldFptpSplit.Unit u in units) foreach (Renderer r in u.Renderers) unitOf[r] = u;

            List<Vector3> feet = FeetPositions(maxFeet, out int allFeet);
            List<Pose> poses = Poses(feet, rig);
            Debug.Log("[TDVIS] " + units.Count + " units, " + all.Count + " renderers (" + bits + " bits/channel), " + feet.Count + " feet (" + allFeet + " unthinned, " + allFeet * 5 + " full poses), " + poses.Count + " poses, hash " + hash);

            // gains need the original materials: measure the scene before the id materials go on
            var meshBytes = new Dictionary<Mesh, long>();
            var textureBytes = new Dictionary<Texture, long>();

            var state = new IdState(all, idOf, bits);
            var stats = new Stats(all.Count + 1);
            int renders = 0, mirrorPoses = 0;
            var sampled = new long[3];
            int width = Mathf.RoundToInt(height * WideAspect);
            int crop0 = (width - Mathf.RoundToInt(height * StandardAspect)) / 2, crop1 = width - crop0;

            PlanarReflection planar = UnityEngine.Object.FindObjectOfType<PlanarReflection>(true);
            float planeHeight = planar != null ? planar.PlaneHeight : float.NaN;
            var receiver = new bool[all.Count + 1];
            for (int i = 0; i < all.Count; i++)
                foreach (Material m in all[i].sharedMaterials)
                    if (m != null && m.shader != null && m.shader.name == ReceiverShader) receiver[i + 1] = true;

            var faderCache = new Dictionary<Collider, Renderer[]>();
            HashSet<Transform> fadeOwners = FadeOwners(scene);
            UniversalRenderPipelineAsset urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float renderScale = urp != null ? urp.renderScale : 1f;
            bool asyncCompile = ShaderUtil.allowAsyncCompilation;
            var camGo = new GameObject("TDVIS Camera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1, filterMode = FilterMode.Point, useMipMap = false };
            var read = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            var pixels = new uint[width * height];
            var mask = new bool[width * height];
            var scratch = new int[width * height];
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                if (urp != null) urp.renderScale = 1f;
                state.Apply();
                Camera cam = MakeCamera(camGo, rt, rig);
                int opaqueMask = 0;
                for (int p = 0; p < poses.Count; p++)
                {
                    Pose pose = poses[p];
                    List<Renderer> faded = Faded(pose, faderCache, fadeOwners);
                    foreach (Renderer r in faded) r.forceRenderingOff = true;
                    try
                    {
                        cam.ResetWorldToCameraMatrix();
                        cam.ResetProjectionMatrix();
                        cam.farClipPlane = DirectFar;
                        cam.transform.SetPositionAndRotation(pose.Camera, pose.Rotation);

                        state.Pass(transparent: false);
                        Grab(cam, rt, read, pixels);
                        renders++;
                        stats.Count(pixels, width, height, bits, crop0, crop1, null, Stats.Channel.Direct, all.Count, p < 24 ? sampled : null);
                        opaqueMask = ReceiverMask(pixels, width, height, bits, receiver, mask, scratch);

                        state.Pass(transparent: true);
                        Grab(cam, rt, read, pixels);
                        renders++;
                        stats.Count(pixels, width, height, bits, crop0, crop1, null, Stats.Channel.Direct, all.Count, null);

                        if (opaqueMask > 0 && !float.IsNaN(planeHeight))
                        {
                            mirrorPoses++;
                            Mirror(cam, planeHeight);
                            state.Mirror(true);
                            try
                            {
                                state.Pass(transparent: false);
                                Grab(cam, rt, read, pixels);
                                renders++;
                                stats.Count(pixels, width, height, bits, crop0, crop1, mask, Stats.Channel.Reflection, all.Count, null);
                                state.Pass(transparent: true);
                                Grab(cam, rt, read, pixels);
                                renders++;
                                stats.Count(pixels, width, height, bits, crop0, crop1, mask, Stats.Channel.Reflection, all.Count, null);
                            }
                            finally
                            {
                                state.Mirror(false);
                            }
                        }

                        stats.EndPose();
                    }
                    finally
                    {
                        foreach (Renderer r in faded) r.forceRenderingOff = false;
                    }

                    if (p % 200 == 0) Debug.Log("[TDVIS] pose " + p + "/" + poses.Count + " " + clock.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s");
                }
            }
            finally
            {
                state.Restore();
                ShaderUtil.allowAsyncCompilation = asyncCompile;
                if (urp != null) urp.renderScale = renderScale;
                UnityEngine.Object.DestroyImmediate(camGo);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(read);
            }

            Debug.Log("[TDVIS] renders done: " + renders + " (" + mirrorPoses + " mirror poses) in " + clock.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s");
            if (stats.InvalidIds > 0)
                throw new InvalidOperationException("[TDVIS] " + stats.InvalidIds + " pixels decoded to no renderer: the id pass is not exact (render scale, MSAA, post or a feature in the way)");
            if (stats.TotalIdPixels == 0) throw new InvalidOperationException("[TDVIS] the id passes drew nothing");
            if (sampled[0] == 0 || sampled[1] * 1000L > sampled[0])
                throw new InvalidOperationException("[TDVIS] id colours drift: " + sampled[1] + " of " + sampled[0] + " sampled pixels off their code centre by > 1 (the id pass is not exact; first RGB 0x" + sampled[2].ToString("X6", CultureInfo.InvariantCulture) + ")");

            // ---- verdicts
            List<Plane[]> shadowFrusta = Frusta(poses, rig, ShadowDistance, WideAspect);
            Vector3 sunDir = Quaternion.Euler(sunEuler) * Vector3.forward;
            List<(Vector3 centre, float range)> lights = ShadowLights(shadowFrusta);
            var summary = new Summary
            {
                utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                scene = GreyboxV6Builder.ScenePath,
                contentHash = hash,
                listPath = WorldFptpSplit.ListPath,
                width = width,
                height = height,
                feet = feet.Count,
                poses = poses.Count,
                renders = renders,
                mirrorPoses = mirrorPoses,
                renderers = all.Count,
                units = units.Count,
                ids = all.Count,
                idBits = bits,
                fov = rig.Fov,
                fovMargin = FovMarginDegrees,
                aspectWide = WideAspect,
                invalidIdPixels = stats.InvalidIds,
                offCentrePixels = sampled[1],
                sampledPixels = sampled[0],
                rule = Rule()
            };

            var report = new Report { summary = summary };
            var list = new WorldFptpSplit.VisibilityList { contentHash = hash, rule = summary.rule, units = units.Count };
            var pinCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var moved = new HashSet<WorldFptpSplit.Unit>();
            var reasonOf = new Dictionary<Renderer, string>(all.Count);
            foreach (WorldFptpSplit.Unit u in units)
            {
                int max16 = 0, max21 = 0, refl = 0, posesSeen = 0, transparentPixels = 0;
                bool caster = false, staticBatched = false;
                var b = new Bounds();
                bool any = false;
                foreach (Renderer r in u.Renderers)
                {
                    int id = idOf[r];
                    max16 = Math.Max(max16, stats.Max16[id]);
                    max21 = Math.Max(max21, stats.Max21[id]);
                    refl = Math.Max(refl, stats.MaxReflection[id]);
                    posesSeen = Math.Max(posesSeen, stats.Poses[id]);
                    transparentPixels += state.IsTransparentOnly(id) ? stats.Max21[id] : 0;
                    caster |= r.shadowCastingMode != ShadowCastingMode.Off;
                    staticBatched |= (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.BatchingStatic) != 0;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }

                string decision, reason;
                if (u.Pin != null) { decision = "keep"; reason = "pin:" + u.Pin; summary.pinned++; }
                else if (max21 > 0) { decision = "keep"; reason = max16 > 0 ? "visible" : "visible-21:9-only"; summary.visible++; if (max16 > 0) summary.visible16++; else summary.ultraWideOnly++; if (transparentPixels == max21 && max21 > 0) summary.transparentOnly++; }
                else if (refl > 0) { decision = "keep"; reason = "reflection"; summary.reflectionOnly++; }
                else if (caster && ShadowReaches(b, sunDir, shadowFrusta)) { decision = "keep"; reason = "shadow"; summary.shadowKept++; }
                else if (caster && InLight(b, lights)) { decision = "keep"; reason = "light-shadow"; summary.lightKept++; }
                else { decision = "move"; reason = "never-visible"; }
                if (u.Pin != null)
                {
                    string head = u.Pin.Split(':')[0];
                    pinCounts[head] = pinCounts.TryGetValue(head, out int n) ? n + 1 : 1;
                }

                bool nav = NavRelevant(u, worldRoot);
                report.units.Add(new UnitRecord
                {
                    key = u.Key, family = u.Family, mode = u.Mode.ToString(), decision = decision, reason = reason, renderers = u.Renderers.Count, triangles = u.Triangles,
                    max16 = max16, max21 = max21, reflMax = refl, poses = posesSeen, shadowCaster = caster, collider = u.HasColliders, navRelevant = nav, staticBatched = staticBatched,
                    x = b.center.x, y = b.center.y, z = b.center.z
                });
                foreach (Renderer r in u.Renderers)
                {
                    int id = idOf[r];
                    reasonOf[r] = decision == "move" ? "move" : reason;
                    report.renderers.Add(new RendererRecord
                    {
                        path = FullPath(r.transform), unit = u.Key, family = u.Family, decision = decision, reason = reason, triangles = WorldFptpSplit.Triangles(r),
                        max16 = stats.Max16[id], max21 = stats.Max21[id], reflMax = stats.MaxReflection[id], poses = stats.Poses[id], sum21 = stats.Sum21[id],
                        transparent = state.IsTransparentOnly(id) ? 1 : 0, shadowCaster = r.shadowCastingMode != ShadowCastingMode.Off, collider = u.HasColliders, navRelevant = nav
                    });
                }

                if (decision != "move") continue;
                moved.Add(u);
                list.entries.Add(new WorldFptpSplit.Entry { key = u.Key, family = u.Family, fingerprint = u.Fingerprint, mode = u.Mode.ToString(), renderers = u.Renderers.Count, triangles = u.Triangles });
            }

            foreach (KeyValuePair<string, int> kv in pinCounts) summary.pinReasons.Add(kv.Key + " " + kv.Value);
            summary.moved = moved.Count;
            list.moved = moved.Count;
            summary.gain = Gains(all, units, moved, poses, rig, sunDir, meshBytes, textureBytes);
            summary.seconds = (float)clock.Elapsed.TotalSeconds;

            OverheadMaps(outDir, all, idOf, bits, reasonOf);
            File.WriteAllText(System.IO.Path.Combine(outDir, "td-visibility-audit.json"), JsonUtility.ToJson(report) + "\n", new UTF8Encoding(false));
            File.WriteAllText(System.IO.Path.Combine(outDir, "td-visibility-summary.json"), JsonUtility.ToJson(summary, true) + "\n", new UTF8Encoding(false));
            File.WriteAllText(System.IO.Path.Combine(outDir, "td-visibility-list.json"), JsonUtility.ToJson(list, true) + "\n", new UTF8Encoding(false));
            if (writeList) WorldFptpSplit.WriteList(list);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // the merged world is discarded, never saved
            return summary;
        }

        public static string Rule()
        {
            return "move = movable unit (WorldFptpSplit.StaticPin null) with 0 px in every TD pose (2.4:1 + 16:9 crop, FOV +" + FovMarginDegrees.ToString("0.0", CultureInfo.InvariantCulture) +
                   "deg, LOD0, fader emulated, opaque + transparent passes), 0 px in the plaza planar reflection, no shadow into a pose frustum (sun sweep to y " +
                   ShadowGroundY.ToString("0", CultureInfo.InvariantCulture) + ", " + ShadowDistance.ToString("0", CultureInfo.InvariantCulture) + " m) and no shadow-casting point/spot light range on a pose";
        }

        // ------------------------------------------------------------------------------------------------ poses

        private static void EnsureNavMesh(Transform worldRoot)
        {
            if (NavMesh.CalculateTriangulation().indices.Length > 0) return;
            foreach (NavMeshSurface s in worldRoot.GetComponentsInChildren<NavMeshSurface>(true))
                if (s.navMeshData != null) { s.RemoveData(); s.AddData(); }
            if (NavMesh.CalculateTriangulation().indices.Length == 0) throw new InvalidOperationException("[TDVIS] no NavMesh in the world");
        }

        /// <summary>Walkable feet positions: the ≤ 4 m grid on every NavMesh layer, uncovered triangles' centroids, the ring edge every 4 m.</summary>
        public static List<Vector3> FeetPositions(int max) => FeetPositions(max, out _);

        public static List<Vector3> FeetPositions(int max, out int unthinned)
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            Vector3[] v = tri.vertices;
            int[] idx = tri.indices;
            var feet = new List<Vector3>();
            var seen = new HashSet<(int, int, int)>();
            float limit = PlayHalf + 0.5f;
            void Add(Vector3 p, float cell, int space)
            {
                if (Mathf.Abs(p.x) > limit || Mathf.Abs(p.z) > limit) return;
                var k = (Mathf.RoundToInt(p.x / cell) + space * 100000, Mathf.RoundToInt(p.z / cell), Mathf.RoundToInt(p.y / 1.5f));   // a key space per source
                if (seen.Add(k)) feet.Add(p);
            }

            var covered = new bool[idx.Length / 3];
            for (int t = 0; t < idx.Length / 3; t++)
            {
                Vector3 a = v[idx[3 * t]], b = v[idx[3 * t + 1]], c = v[idx[3 * t + 2]];
                float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                for (float gx = Mathf.Ceil(x0 / GridStep) * GridStep; gx <= x1; gx += GridStep)
                for (float gz = Mathf.Ceil(z0 / GridStep) * GridStep; gz <= z1; gz += GridStep)
                {
                    if (!Barycentric(new Vector2(gx, gz), a, b, c, out float y)) continue;
                    covered[t] = true;
                    Add(new Vector3(gx, y, gz), GridStep, 0);
                }
            }

            for (int t = 0; t < covered.Length; t++)
            {
                if (covered[t]) continue;
                Vector3 a = v[idx[3 * t]], b = v[idx[3 * t + 1]], c = v[idx[3 * t + 2]];
                Add((a + b + c) / 3f, GridStep * 0.5f, 1);
            }

            for (int i = 0; i < v.Length; i++)
                if (Mathf.Max(Mathf.Abs(v[i].x), Mathf.Abs(v[i].z)) >= PlayHalf - EdgeBand) Add(v[i], GridStep, 2);

            feet.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.z != q.z ? p.z.CompareTo(q.z) : p.y.CompareTo(q.y));
            unthinned = feet.Count;
            if (max > 0 && feet.Count > max)
            {
                var thin = new List<Vector3>(max);
                for (int i = 0; i < max; i++) thin.Add(feet[i * feet.Count / max]);
                return thin;
            }

            return feet;
        }

        private static bool Barycentric(Vector2 p, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0f;
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-6f) return false;
            float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.y - c.z)) / d;
            float l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.y - c.z)) / d;
            float l3 = 1f - l1 - l2;
            const float e = -1e-4f;
            if (l1 < e || l2 < e || l3 < e) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y;
            return true;
        }

        public static List<Pose> Poses(List<Vector3> feet, Rig rig)
        {
            var offsets = new[] { Vector3.zero, new Vector3(rig.LookAhead, 0f, 0f), new Vector3(-rig.LookAhead, 0f, 0f), new Vector3(0f, 0f, rig.LookAhead), new Vector3(0f, 0f, -rig.LookAhead) };
            Quaternion rotation = Quaternion.LookRotation(-rig.Offset.normalized, Vector3.up);
            var poses = new List<Pose>(feet.Count * offsets.Length);
            foreach (Vector3 f in feet)
                foreach (Vector3 o in offsets)
                    poses.Add(new Pose { Feet = f, Camera = f + Vector3.up * rig.FocusHeight + o + rig.Offset, Rotation = rotation });
            return poses;
        }

        /// <summary>
        /// The top-down fader at this pose: the renderers of every CameraOccluder the camera → feet + 1.1 m sphere cast hits. The game's
        /// fader (CameraOcclusionFader.Resolve) fades the nearest OccluderFadeState above the hit collider, and those are created on
        /// demand on each collider's owner — so which subtree fades depends on what was hit before. The audit takes the widest: the
        /// outermost fade owner on the collider's ancestor chain.
        /// </summary>
        private static List<Renderer> Faded(Pose pose, Dictionary<Collider, Renderer[]> cache, HashSet<Transform> owners)
        {
            var list = new List<Renderer>();
            Vector3 to = pose.Feet + Vector3.up * FaderHeight, delta = to - pose.Camera;
            float distance = delta.magnitude;
            if (distance < 0.01f) return list;
            RaycastHit[] hits = Physics.SphereCastAll(pose.Camera, FaderRadius, delta / distance, distance, 1 << PrototypeLayers.CameraOccluder, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (!cache.TryGetValue(hit.collider, out Renderer[] renderers))
                {
                    Transform owner = FadeOwner(hit.collider);
                    for (Transform p = hit.collider.transform; p != null; p = p.parent)
                        if (owners.Contains(p) && (owner == null || owner.IsChildOf(p))) owner = p;
                    renderers = owner != null ? Array.FindAll(owner.GetComponentsInChildren<Renderer>(true), x => x is MeshRenderer) : Array.Empty<Renderer>();
                    cache[hit.collider] = renderers;
                }

                list.AddRange(renderers);
            }

            return list;
        }

        /// <summary>Where CameraOcclusionFader.Resolve puts the fade for a collider: itself when renderers sit under it, else its nearest renderer parent.</summary>
        private static Transform FadeOwner(Collider c)
        {
            if (c.transform.GetComponentInChildren<Renderer>(true) != null) return c.transform;
            Renderer parent = c.GetComponentInParent<Renderer>();
            return parent != null ? parent.transform : null;
        }

        /// <summary>Every transform a fade can sit on: the scene's OccluderFadeStates and each live CameraOccluder collider's owner.</summary>
        private static HashSet<Transform> FadeOwners(Scene scene)
        {
            var owners = new HashSet<Transform>();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                foreach (OccluderFadeState state in go.GetComponentsInChildren<OccluderFadeState>(true)) owners.Add(state.transform);
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (c.gameObject.layer != PrototypeLayers.CameraOccluder || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger) continue;
                    Transform owner = FadeOwner(c);
                    if (owner != null) owners.Add(owner);
                }
            }

            return owners;
        }

        // ------------------------------------------------------------------------------------------------ rendering

        private static Camera MakeCamera(GameObject go, RenderTexture rt, Rig rig)
        {
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = rt;
            cam.aspect = WideAspect;
            cam.fieldOfView = rig.Fov + FovMarginDegrees;
            cam.nearClipPlane = NearClip;
            cam.farClipPlane = DirectFar;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.cullingMask = ~((1 << 5) | (1 << LayerMask.NameToLayer("FirstPersonView")));
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.SetRenderer(PlanarReflection.PlanarRendererIndex);
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.dithering = false;
            data.stopNaN = false;
            return cam;
        }

        private static void Mirror(Camera cam, float planeHeight)
        {
            cam.farClipPlane = MirrorFar;
            cam.ResetProjectionMatrix();
            Matrix4x4 mirror = PlanarReflection.Reflection(new Vector4(0f, 1f, 0f, -planeHeight));
            cam.worldToCameraMatrix = cam.worldToCameraMatrix * mirror;
            Vector4 clip = PlanarReflection.CameraSpacePlane(cam.worldToCameraMatrix, new Vector3(0f, planeHeight, 0f), Vector3.up, 0.03f);
            cam.projectionMatrix = cam.CalculateObliqueMatrix(clip);
        }

        private static void Grab(Camera cam, RenderTexture rt, Texture2D read, uint[] pixels)
        {
            cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);   // the CPU copy is what is read: no Apply (GPU upload)
            RenderTexture.active = previous;
            NativeArray<uint> raw = read.GetRawTextureData<uint>();
            raw.CopyTo(pixels);
        }

        /// <summary>Marks (dilated) the pixels whose opaque id is a planar-reflection receiver; returns their count before dilation.</summary>
        private static int ReceiverMask(uint[] pixels, int w, int h, int bits, bool[] receiver, bool[] mask, int[] scratch)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                int id = Decode(pixels[i], bits);
                bool on = id > 0 && id < receiver.Length && receiver[id];
                mask[i] = on;
                if (on) count++;
            }

            if (count == 0) return 0;
            int r = ReceiverDilatePixels;
            for (int y = 0; y < h; y++)
            {
                int row = y * w, run = 0;
                for (int x = 0; x < w + r; x++)
                {
                    if (x < w && mask[row + x]) run = 2 * r + 1;
                    if (x - r >= 0) scratch[row + x - r] = run > 0 ? 1 : 0;
                    if (run > 0) run--;
                }
            }

            for (int x = 0; x < w; x++)
            {
                int run = 0;
                for (int y = 0; y < h + r; y++)
                {
                    if (y < h && scratch[y * w + x] != 0) run = 2 * r + 1;
                    if (y - r >= 0) mask[(y - r) * w + x] = run > 0;
                    if (run > 0) run--;
                }
            }

            return count;
        }

        public static int Decode(uint v, int bits)
        {
            int shift = 8 - bits, m = (1 << bits) - 1;
            int r = (int)(v & 0xFF) >> shift, g = (int)((v >> 8) & 0xFF) >> shift, b = (int)((v >> 16) & 0xFF) >> shift;
            return (r & m) | ((g & m) << bits) | ((b & m) << (2 * bits));
        }

        /// <summary>The id's raw UNorm8 code centres as a shader vector (set with SetVector: no colour-space conversion; the target is linear).</summary>
        public static Vector4 Encode(int id, int bits)
        {
            int shift = 8 - bits, m = (1 << bits) - 1, half = 1 << (shift - 1);
            int r = ((id & m) << shift) + half, g = (((id >> bits) & m) << shift) + half, b = (((id >> (2 * bits)) & m) << shift) + half;
            return new Vector4(r / 255f, g / 255f, b / 255f, 1f);
        }

        /// <summary>Per-renderer pixel statistics over the poses.</summary>
        private sealed class Stats
        {
            public enum Channel { Direct, Reflection }

            public readonly int[] Max16, Max21, MaxReflection, Poses;
            public readonly long[] Sum21;
            private readonly int[] _c16, _c21, _cr;
            private readonly List<int> _touched = new List<int>(4096);
            private readonly bool[] _isTouched;
            public long InvalidIds, TotalIdPixels;

            public Stats(int n)
            {
                Max16 = new int[n]; Max21 = new int[n]; MaxReflection = new int[n]; Poses = new int[n]; Sum21 = new long[n];
                _c16 = new int[n]; _c21 = new int[n]; _cr = new int[n]; _isTouched = new bool[n];
            }

            public void Count(uint[] px, int w, int h, int bits, int crop0, int crop1, bool[] mask, Channel channel, int maxId, long[] sample)
            {
                int shift = 8 - bits, centre = 1 << (shift - 1);
                if (mask == null)
                {
                    CountRuns(px, w, h, bits, crop0, crop1, channel, maxId, sample, shift, centre);
                    return;
                }

                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        uint v = px[row + x];
                        if ((v & 0xFFFFFF) == 0) continue;
                        if (mask != null && !mask[row + x]) continue;
                        int id = Decode(v, bits);
                        if (id == 0) continue;
                        if (id > maxId) { InvalidIds++; continue; }
                        TotalIdPixels++;
                        if (sample != null)
                        {
                            sample[0]++;
                            int dr = Math.Abs((int)(v & 0xFF) - (((int)(v & 0xFF) >> shift << shift) + centre));
                            int dg = Math.Abs((int)((v >> 8) & 0xFF) - (((int)((v >> 8) & 0xFF) >> shift << shift) + centre));
                            int db = Math.Abs((int)((v >> 16) & 0xFF) - (((int)((v >> 16) & 0xFF) >> shift << shift) + centre));
                            if (dr > 1 || dg > 1 || db > 1)
                            {
                                if (sample[1] == 0) sample[2] = v & 0xFFFFFF;   // the first drifting pixel (RGB) for the message
                                sample[1]++;
                            }
                        }

                        if (!_isTouched[id]) { _isTouched[id] = true; _touched.Add(id); }
                        if (channel == Channel.Reflection) { _cr[id]++; continue; }
                        _c21[id]++;
                        if (x >= crop0 && x < crop1) _c16[id]++;
                    }
                }
            }

            /// <summary>The unmasked count by runs of one value along a row (one decode per run; the same totals as per pixel).</summary>
            private void CountRuns(uint[] px, int w, int h, int bits, int crop0, int crop1, Channel channel, int maxId, long[] sample, int shift, int centre)
            {
                for (int y = 0; y < h; y++)
                {
                    int row = y * w, x = 0;
                    while (x < w)
                    {
                        uint v = px[row + x];
                        int start = x++;
                        while (x < w && px[row + x] == v) x++;
                        if ((v & 0xFFFFFF) == 0) continue;
                        int id = Decode(v, bits), run = x - start;
                        if (id == 0) continue;
                        if (id > maxId) { InvalidIds += run; continue; }
                        TotalIdPixels += run;
                        if (sample != null)
                        {
                            sample[0] += run;
                            int dr = Math.Abs((int)(v & 0xFF) - (((int)(v & 0xFF) >> shift << shift) + centre));
                            int dg = Math.Abs((int)((v >> 8) & 0xFF) - (((int)((v >> 8) & 0xFF) >> shift << shift) + centre));
                            int db = Math.Abs((int)((v >> 16) & 0xFF) - (((int)((v >> 16) & 0xFF) >> shift << shift) + centre));
                            if (dr > 1 || dg > 1 || db > 1)
                            {
                                if (sample[1] == 0) sample[2] = v & 0xFFFFFF;
                                sample[1] += run;
                            }
                        }

                        if (!_isTouched[id]) { _isTouched[id] = true; _touched.Add(id); }
                        if (channel == Channel.Reflection) { _cr[id] += run; continue; }
                        _c21[id] += run;
                        int inside = Math.Min(x, crop1) - Math.Max(start, crop0);
                        if (inside > 0) _c16[id] += inside;
                    }
                }
            }

            public void EndPose()
            {
                foreach (int id in _touched)
                {
                    if (_c21[id] > 0) { Poses[id]++; Sum21[id] += _c21[id]; }
                    if (_c21[id] > Max21[id]) Max21[id] = _c21[id];
                    if (_c16[id] > Max16[id]) Max16[id] = _c16[id];
                    if (_cr[id] > MaxReflection[id]) MaxReflection[id] = _cr[id];
                    _c21[id] = 0; _c16[id] = 0; _cr[id] = 0; _isTouched[id] = false;
                }

                _touched.Clear();
            }
        }

        /// <summary>The id materials (one per original material), the per-renderer id blocks, LOD 0, and the pass / mirror switches.</summary>
        private sealed class IdState
        {
            private readonly List<Renderer> _all;
            private readonly Dictionary<Renderer, int> _ids;
            private readonly int _bits;
            private readonly Dictionary<Material, Material> _idMaterial = new Dictionary<Material, Material>();
            private readonly List<Material> _opaque = new List<Material>(), _transparent = new List<Material>();
            private readonly Dictionary<Renderer, Material[]> _original = new Dictionary<Renderer, Material[]>();
            private readonly List<LODGroup> _lods = new List<LODGroup>();
            private readonly HashSet<int> _transparentOnly = new HashSet<int>();
            private readonly List<Renderer> _off = new List<Renderer>();
            private Material _fallback;
            private Shader _shader;

            /// <summary>Overrides the id code per renderer (the overhead maps' flat colours); null = the id.</summary>
            public Func<Renderer, Vector4> Colour;

            public IdState(List<Renderer> all, Dictionary<Renderer, int> ids, int bits)
            {
                _all = all;
                _ids = ids;
                _bits = bits;
            }

            public bool IsTransparentOnly(int id) => _transparentOnly.Contains(id);

            public void Apply()
            {
                _shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
                if (_shader == null || !_shader.isSupported) throw new InvalidOperationException("[TDVIS] id shader missing or unsupported: " + ShaderPath);
                _fallback = Make(null);
                var block = new MaterialPropertyBlock();
                foreach (Renderer r in _all)
                {
                    if (!(r is MeshRenderer))
                    {
                        if (!r.forceRenderingOff) { r.forceRenderingOff = true; _off.Add(r); }
                        continue;
                    }

                    Material[] original = r.sharedMaterials;
                    _original[r] = original;
                    var swapped = new Material[original.Length];
                    bool opaque = false;
                    for (int i = 0; i < original.Length; i++)
                    {
                        Material m = original[i];
                        if (m == null) { swapped[i] = _fallback; opaque = true; continue; }
                        if (!_idMaterial.TryGetValue(m, out Material id)) _idMaterial[m] = id = Make(m);
                        swapped[i] = id;
                        opaque |= id.renderQueue < 2500;
                    }

                    if (!opaque && original.Length > 0) _transparentOnly.Add(_ids[r]);
                    r.sharedMaterials = swapped;
                    block.Clear();
                    // SetVector, not SetColor: in a Linear project SetColor converts gamma → linear and every code would drift
                    block.SetVector("_IdColor", Colour != null ? Colour(r) : Encode(_ids[r], _bits));
                    r.SetPropertyBlock(block);
                }

                foreach (LODGroup g in UnityEngine.Object.FindObjectsOfType<LODGroup>(true))
                {
                    g.ForceLOD(0);
                    _lods.Add(g);
                }
            }

            private Material Make(Material original)
            {
                var m = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };
                bool transparent = original != null && original.renderQueue >= 2500;
                m.renderQueue = transparent ? 3000 : 2000;
                float cutoff = -1f;
                Texture clip = null;
                if (original != null)
                {
                    bool alphaTest = original.renderQueue >= 2450 && original.renderQueue < 2500
                                     || original.IsKeywordEnabled("_ALPHATEST_ON")
                                     || original.HasProperty("_AlphaClip") && original.GetFloat("_AlphaClip") > 0.5f;
                    if (alphaTest && original.HasProperty("_Cutoff")) cutoff = original.GetFloat("_Cutoff");
                    else if (transparent) cutoff = 0.02f;
                    string main = original.HasProperty("_BaseMap") ? "_BaseMap" : original.HasProperty("_MainTex") ? "_MainTex" : null;
                    clip = main != null ? original.GetTexture(main) : null;
                    if (clip != null)
                    {
                        m.SetTexture("_ClipTex", clip);
                        m.SetTextureScale("_ClipTex", original.GetTextureScale(main));
                        m.SetTextureOffset("_ClipTex", original.GetTextureOffset(main));
                    }
                    else if (!alphaTest)
                    {
                        cutoff = -1f;
                    }

                    m.SetFloat("_ClipAlphaScale", original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor").a : original.HasProperty("_Color") ? original.GetColor("_Color").a : 1f);
                    m.SetFloat("_Cull", original.HasProperty("_Cull") ? original.GetFloat("_Cull") : DefaultCull(original.shader));
                }

                m.SetFloat("_Cutoff", cutoff);
                (transparent ? _transparent : _opaque).Add(m);
                return m;
            }

            /// <summary>Shaders without a _Cull property: the ones that hard-code Cull Off draw both faces.</summary>
            private static float DefaultCull(Shader shader)
            {
                if (shader == null) return 2f;
                string path = AssetDatabase.GetAssetPath(shader);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".shader", StringComparison.Ordinal) && File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    if (text.IndexOf("Cull Off", StringComparison.OrdinalIgnoreCase) >= 0) return 0f;
                    if (text.IndexOf("Cull Front", StringComparison.OrdinalIgnoreCase) >= 0) return 1f;
                }

                return 2f;
            }

            /// <summary>(A) opaque ids with depth; (B) opaque depth only, then the transparent queue's ids with depth among themselves.</summary>
            public void Pass(bool transparent)
            {
                foreach (Material m in _opaque)
                {
                    m.SetFloat("_ColorMask", transparent ? 0f : 15f);
                    m.SetFloat("_ZWrite", 1f);
                }

                foreach (Material m in _transparent)
                {
                    m.SetFloat("_ColorMask", transparent ? 15f : 0f);
                    m.SetFloat("_ZWrite", transparent ? 1f : 0f);
                }
            }

            /// <summary>Every queue draws its colour with depth (the overhead maps).</summary>
            public void PassAll()
            {
                foreach (Material m in _opaque) { m.SetFloat("_ColorMask", 15f); m.SetFloat("_ZWrite", 1f); }
                foreach (Material m in _transparent) { m.SetFloat("_ColorMask", 15f); m.SetFloat("_ZWrite", 1f); }
            }

            /// <summary>The mirrored camera flips the winding: back ↔ front culling (double-sided stays).</summary>
            public void Mirror(bool on)
            {
                foreach (Material m in _idMaterial.Values) Flip(m);
                Flip(_fallback);
            }

            private static void Flip(Material m)
            {
                float c = m.GetFloat("_Cull");
                if (c == 1f) m.SetFloat("_Cull", 2f);
                else if (c == 2f) m.SetFloat("_Cull", 1f);
            }

            public void Restore()
            {
                foreach (KeyValuePair<Renderer, Material[]> kv in _original)
                {
                    if (kv.Key == null) continue;
                    kv.Key.sharedMaterials = kv.Value;
                    kv.Key.SetPropertyBlock(null);
                }

                foreach (Renderer r in _off) if (r != null) r.forceRenderingOff = false;
                foreach (LODGroup g in _lods) if (g != null) g.ForceLOD(-1);
                foreach (Material m in _idMaterial.Values) UnityEngine.Object.DestroyImmediate(m);
                if (_fallback != null) UnityEngine.Object.DestroyImmediate(_fallback);
            }
        }

        // ------------------------------------------------------------------------------------------------ overhead maps

        public const int MapSize = 2048;
        public const float PlayMapMargin = 30f, WorldMapLimit = 1500f;

        /// <summary>
        /// The verdict seen from straight above (orthographic, LOD 0, flat colours, no fade): kept renderers in greys (shadow /
        /// reflection / light keeps in blue), the moved ones red drawn over everything (a second render of the moved layer alone, so a
        /// moved unit under a kept roof or canopy still shows), the play square in yellow. Two PNGs: the whole world (≤ ±1500 m) and
        /// the play area ± 30 m. +z is up, +x right.
        /// </summary>
        private static void OverheadMaps(string outDir, List<Renderer> all, Dictionary<Renderer, int> idOf, int bits, Dictionary<Renderer, string> reasonOf)
        {
            var extent = new Bounds();
            bool any = false;
            foreach (Renderer r in all)
            {
                if (!(r is MeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { extent = r.bounds; any = true; } else extent.Encapsulate(r.bounds);
            }

            if (!any) return;
            float worldHalf = Mathf.Min(WorldMapLimit, Mathf.Max(Mathf.Max(Mathf.Abs(extent.min.x), Mathf.Abs(extent.max.x)), Mathf.Max(Mathf.Abs(extent.min.z), Mathf.Abs(extent.max.z))));
            float top = extent.max.y + 20f, depth = extent.size.y + 60f;

            var red = new Vector4(1f, 0.08f, 0.08f, 1f);
            var state = new IdState(all, idOf, bits)
            {
                Colour = r =>
                {
                    reasonOf.TryGetValue(r, out string reason);
                    if (reason == "move") return red;
                    float g = 0.3f + 0.45f * ((idOf[r] * 2654435761u >> 16) & 0xFF) / 255f;
                    return reason == "shadow" || reason == "light-shadow" || reason == "reflection" ? new Vector4(0.25f * g, 0.45f * g + 0.1f, 0.9f * g + 0.1f, 1f) : new Vector4(g, g, g, 1f);
                }
            };

            var changed = new HashSet<Renderer>();
            var rt = new RenderTexture(MapSize, MapSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1, filterMode = FilterMode.Point, useMipMap = false };
            var read = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false, true);
            var keptPx = new uint[MapSize * MapSize];
            var movedPx = new uint[MapSize * MapSize];
            var camGo = new GameObject("TDVIS Map Camera") { hideFlags = HideFlags.HideAndDontSave };
            UniversalRenderPipelineAsset urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float renderScale = urp != null ? urp.renderScale : 1f;
            bool asyncCompile = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                if (urp != null) urp.renderScale = 1f;
                state.Apply();
                state.PassAll();
                Camera cam = MakeCamera(camGo, rt, new Rig { Fov = 40f });
                cam.orthographic = true;
                cam.aspect = 1f;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = depth + 20f;
                foreach ((string file, float half) in new[] { ("td-visibility-map-world.png", worldHalf), ("td-visibility-map-play.png", PlayHalf + PlayMapMargin) })
                {
                    cam.orthographicSize = half;
                    cam.transform.SetPositionAndRotation(new Vector3(0f, top, 0f), Quaternion.Euler(90f, 0f, 0f));
                    for (int layer = 0; layer < 2; layer++)
                    {
                        // layer 0: what stays; layer 1: what moves (alone)
                        foreach (Renderer r in all)
                        {
                            if (!(r is MeshRenderer) || r.forceRenderingOff && !changed.Contains(r)) continue;
                            reasonOf.TryGetValue(r, out string reason);
                            bool off = (reason == "move") == (layer == 0);
                            if (off == r.forceRenderingOff) continue;
                            r.forceRenderingOff = off;
                            if (off) changed.Add(r); else changed.Remove(r);
                        }

                        Grab(cam, rt, read, layer == 0 ? keptPx : movedPx);
                    }

                    var image = new Color32[MapSize * MapSize];
                    for (int i = 0; i < image.Length; i++)
                    {
                        uint v = (movedPx[i] & 0xFFFFFF) != 0 ? movedPx[i] : keptPx[i];
                        image[i] = (v & 0xFFFFFF) == 0 ? new Color32(16, 16, 20, 255) : new Color32((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF), 255);
                    }

                    // the play square (±PlayHalf) in yellow, 2 px
                    float scale = MapSize / (2f * half);
                    int a = Mathf.RoundToInt((half - PlayHalf) * scale), b = Mathf.RoundToInt((half + PlayHalf) * scale);
                    for (int t = a; t <= b; t++)
                    for (int w = 0; w < 2; w++)
                    {
                        Plot(image, t, a + w); Plot(image, t, b - w); Plot(image, a + w, t); Plot(image, b - w, t);
                    }

                    var png = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false, true);
                    png.SetPixels32(image);
                    png.Apply(false);
                    File.WriteAllBytes(System.IO.Path.Combine(outDir, file), png.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(png);
                }
            }
            finally
            {
                foreach (Renderer r in changed) if (r != null) r.forceRenderingOff = false;
                state.Restore();
                ShaderUtil.allowAsyncCompilation = asyncCompile;
                if (urp != null) urp.renderScale = renderScale;
                UnityEngine.Object.DestroyImmediate(camGo);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        private static void Plot(Color32[] image, int x, int y)
        {
            if (x < 0 || y < 0 || x >= MapSize || y >= MapSize) return;
            image[y * MapSize + x] = new Color32(255, 214, 0, 255);
        }

        // ------------------------------------------------------------------------------------------------ shadows

        private static List<Plane[]> Frusta(List<Pose> poses, Rig rig, float far, float aspect)
        {
            var list = new List<Plane[]>(poses.Count);
            foreach (Pose p in poses)
            {
                Matrix4x4 proj = Matrix4x4.Perspective(rig.Fov + FovMarginDegrees, aspect, NearClip, far);
                Matrix4x4 view = Matrix4x4.TRS(p.Camera, p.Rotation, Vector3.one).inverse;
                view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * view;
                list.Add(GeometryUtility.CalculateFrustumPlanes(proj * view));
            }

            return list;
        }

        /// <summary>
        /// The caster's bounds swept along the sun down to <see cref="ShadowGroundY"/>: does the sweep touch a pose frustum? The sweep's
        /// AABB picks the candidate frusta; the sweep itself (the box stepped along the sun, each step grown by the step, so the union
        /// covers the continuous prism) decides — a slanted shadow is not kept for a frustum only its AABB's corner reaches.
        /// </summary>
        public static bool ShadowReaches(Bounds b, Vector3 sunDir, List<Plane[]> frusta)
        {
            Bounds swept = SweptBounds(b, sunDir);
            var candidates = new List<Plane[]>();
            foreach (Plane[] planes in frusta)
                if (GeometryUtility.TestPlanesAABB(planes, swept)) candidates.Add(planes);
            if (candidates.Count == 0) return false;
            float down = Mathf.Max(0.05f, -sunDir.y);
            float t = Mathf.Max(0f, (b.max.y - ShadowGroundY) / down);
            float step = Mathf.Max(0.5f, Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z)));
            int n = Mathf.Max(1, Mathf.CeilToInt(t / step));
            for (int i = 0; i <= n; i++)
            {
                var box = new Bounds(b.center + sunDir * (t * i / n), b.size + Vector3.one * step);
                foreach (Plane[] planes in candidates)
                    if (GeometryUtility.TestPlanesAABB(planes, box)) return true;
            }

            return false;
        }

        public static Bounds SweptBounds(Bounds b, Vector3 sunDir)
        {
            float down = Mathf.Max(0.05f, -sunDir.y);
            float t = Mathf.Max(0f, (b.max.y - ShadowGroundY) / down);
            var swept = b;
            swept.Encapsulate(new Bounds(b.center + sunDir * t, b.size));
            return swept;
        }

        private static List<(Vector3, float)> ShadowLights(List<Plane[]> frusta)
        {
            var list = new List<(Vector3, float)>();
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>(true))
            {
                if (!l.isActiveAndEnabled || l.shadows == LightShadows.None || l.lightmapBakeType == LightmapBakeType.Baked) continue;
                if (l.type != LightType.Point && l.type != LightType.Spot) continue;
                var b = new Bounds(l.transform.position, Vector3.one * (2f * l.range));
                foreach (Plane[] planes in frusta)
                    if (GeometryUtility.TestPlanesAABB(planes, b)) { list.Add((l.transform.position, l.range)); break; }
            }

            return list;
        }

        private static bool InLight(Bounds b, List<(Vector3 centre, float range)> lights)
        {
            foreach ((Vector3 c, float range) in lights)
                if (b.SqrDistance(c) <= range * range) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------------------ nav relevance

        private static bool NavRelevant(WorldFptpSplit.Unit u, Transform worldRoot)
        {
            foreach (Component c in u.Root.GetComponentsInChildren<Component>(true))
                if (c is NavMeshModifier || c is NavMeshModifierVolume || c is NavMeshObstacle || c is NavMeshLink) return true;
            for (Transform p = u.Root; p != null && p != worldRoot; p = p.parent)
            {
                NavMeshModifier m = p.GetComponent<NavMeshModifier>();
                if (m != null && m.ignoreFromBuild) return false;
            }

            NavMeshSurface surface = worldRoot.GetComponentInChildren<NavMeshSurface>(true);
            if (surface == null) return false;
            var volume = new Bounds(surface.transform.TransformPoint(surface.center), surface.size);
            foreach (Collider c in u.Root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || !c.enabled || (surface.layerMask & (1 << c.gameObject.layer)) == 0) continue;
                if (volume.Intersects(c.bounds)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------------------------------------ gains

        /// <summary>
        /// The expected top-down gain of the split, measured on the scene (one measurement): what only the moved units reference (meshes
        /// by vertex / index layout, textures by imported format × mips — WorldV5Lighting.ImportedBytes), the static-batch copies of
        /// moved static renderers, their triangles, and per pose the renderers' submesh draws whose bounds the gameplay frustum (42°,
        /// far 260) touches — LOD 0 / unLODed only — before and after, plus the shadow casters URP would cull in (a cylinder along the
        /// sun around the 60 m view slice). Twins keep their (disabled) renderer in the world: their assets count as kept.
        /// </summary>
        private static Gain Gains(List<Renderer> all, List<WorldFptpSplit.Unit> units, HashSet<WorldFptpSplit.Unit> moved, List<Pose> poses, Rig rig, Vector3 sunDir,
            Dictionary<Mesh, long> meshBytes, Dictionary<Texture, long> textureBytes)
        {
            var gain = new Gain
            {
                method = "assets referenced only by moved Move/Proxy units (twins keep theirs); draws = LOD0/unLODed renderer submeshes whose bounds touch the 42deg frustum (far 260), mean/p95 over all poses; shadow casters = within a sun-aligned cylinder around the 60 m view slice"
            };
            var movedRenderers = new HashSet<Renderer>();
            var freed = new HashSet<Renderer>();
            foreach (WorldFptpSplit.Unit u in moved)
            {
                gain.movedUnits++;
                if (u.Mode == WorldFptpSplit.Mode.Proxy) gain.proxies++;
                if (u.Mode == WorldFptpSplit.Mode.Twin) gain.twins++;
                foreach (Renderer r in u.Renderers)
                {
                    movedRenderers.Add(r);
                    if (u.Mode != WorldFptpSplit.Mode.Twin) freed.Add(r);
                    int tris = WorldFptpSplit.Triangles(r);
                    gain.movedTrianglesAllLods += tris;
                    if (IsLod0OrPlain(r)) gain.movedTrianglesLod0 += tris;
                }
            }

            gain.movedRenderers = movedRenderers.Count;
            var keptMeshes = new HashSet<Mesh>();
            var keptTextures = new HashSet<Texture>();
            var freedMeshes = new HashSet<Mesh>();
            var freedTextures = new HashSet<Texture>();
            long staticBytes = 0;
            foreach (Renderer r in all)
            {
                bool isFreed = freed.Contains(r);
                Mesh mesh = r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
                if (mesh != null) (isFreed ? freedMeshes : keptMeshes).Add(mesh);
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    foreach (int pid in m.GetTexturePropertyNameIDs())
                    {
                        Texture t = m.GetTexture(pid);
                        if (t != null) (isFreed ? freedTextures : keptTextures).Add(t);
                    }
                }

                if (isFreed && mesh != null && (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.BatchingStatic) != 0) staticBytes += MeshBytes(mesh, meshBytes);
            }

            long meshTotal = 0, textureTotal = 0;
            foreach (Mesh m in freedMeshes)
                if (!keptMeshes.Contains(m)) { gain.uniqueMeshes++; meshTotal += MeshBytes(m, meshBytes); }
            foreach (Texture t in freedTextures)
                if (!keptTextures.Contains(t)) { gain.uniqueTextures++; textureTotal += TextureBytes(t, textureBytes); }
            gain.uniqueMeshMb = meshTotal / 1048576f;
            gain.uniqueTextureMb = textureTotal / 1048576f;
            gain.staticBatchMb = staticBytes / 1048576f;

            // per pose frustum draws and shadow casters
            var draws = new List<(Bounds bounds, int submeshes, bool moved, bool caster)>();
            foreach (Renderer r in all)
            {
                if (!(r is MeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy || !IsLod0OrPlain(r)) continue;
                MeshFilter f = r.GetComponent<MeshFilter>();
                int sub = f != null && f.sharedMesh != null ? f.sharedMesh.subMeshCount : 1;
                draws.Add((r.bounds, sub, movedRenderers.Contains(r), r.shadowCastingMode != ShadowCastingMode.Off));
            }

            List<Plane[]> f16 = Frusta(poses, rig, GameplayFar, StandardAspect), f21 = Frusta(poses, rig, GameplayFar, WideAspect);
            var before16 = new List<float>(poses.Count);
            var after16 = new List<float>(poses.Count);
            double b21 = 0, a21 = 0, sb = 0, sa = 0;
            for (int p = 0; p < poses.Count; p++)
            {
                int db16 = 0, da16 = 0, db21 = 0, da21 = 0, cb = 0, ca = 0;
                (Vector3 centre, float radius) slice = ShadowSlice(poses[p], rig);
                foreach ((Bounds bounds, int submeshes, bool isMoved, bool caster) in draws)
                {
                    if (GeometryUtility.TestPlanesAABB(f21[p], bounds))
                    {
                        db21 += submeshes;
                        if (!isMoved) da21 += submeshes;
                        if (GeometryUtility.TestPlanesAABB(f16[p], bounds))
                        {
                            db16 += submeshes;
                            if (!isMoved) da16 += submeshes;
                        }
                    }

                    if (caster && InShadowCylinder(bounds, slice.centre, slice.radius, sunDir))
                    {
                        cb += submeshes;
                        if (!isMoved) ca += submeshes;
                    }
                }

                before16.Add(db16);
                after16.Add(da16);
                b21 += db21; a21 += da21; sb += cb; sa += ca;
            }

            int n = Math.Max(1, poses.Count);
            gain.drawsMean16Before = Mean(before16);
            gain.drawsMean16After = Mean(after16);
            gain.drawsP95Before16 = P95(before16);
            gain.drawsP95After16 = P95(after16);
            gain.drawsMean21Before = (float)(b21 / n);
            gain.drawsMean21After = (float)(a21 / n);
            gain.shadowCastersMeanBefore = (float)(sb / n);
            gain.shadowCastersMeanAfter = (float)(sa / n);
            return gain;
        }

        private static (Vector3, float) ShadowSlice(Pose pose, Rig rig)
        {
            Vector3 forward = pose.Rotation * Vector3.forward;
            float half = Mathf.Tan((rig.Fov + FovMarginDegrees) * 0.5f * Mathf.Deg2Rad);
            float farHalfH = ShadowDistance * half, farHalfW = farHalfH * WideAspect;
            Vector3 centre = pose.Camera + forward * (ShadowDistance * 0.5f);
            float radius = Mathf.Sqrt(ShadowDistance * 0.5f * ShadowDistance * 0.5f + farHalfH * farHalfH + farHalfW * farHalfW);
            return (centre, radius);
        }

        private static bool InShadowCylinder(Bounds b, Vector3 centre, float radius, Vector3 sunDir)
        {
            Vector3 toLight = -sunDir;
            Vector3 d = b.center - centre;
            float along = Vector3.Dot(d, toLight);
            float extent = b.extents.magnitude;
            if (along < -(radius + extent)) return false;
            Vector3 perp = d - toLight * along;
            return perp.magnitude <= radius + extent;
        }

        private static bool IsLod0OrPlain(Renderer r)
        {
            LODGroup g = r.GetComponentInParent<LODGroup>();
            if (g == null) return true;
            LOD[] lods = g.GetLODs();
            if (lods.Length == 0) return true;
            bool inAny = false;
            for (int i = 0; i < lods.Length; i++)
                foreach (Renderer x in lods[i].renderers)
                    if (x == r)
                    {
                        if (i == 0) return true;
                        inAny = true;
                    }

            return !inAny;
        }

        private static long MeshBytes(Mesh m, Dictionary<Mesh, long> cache)
        {
            if (cache.TryGetValue(m, out long v)) return v;
            long bytes = 0;
            for (int s = 0; s < m.vertexBufferCount; s++) bytes += (long)m.vertexCount * m.GetVertexBufferStride(s);
            int indexSize = m.indexFormat == IndexFormat.UInt16 ? 2 : 4;
            for (int s = 0; s < m.subMeshCount; s++) bytes += (long)m.GetIndexCount(s) * indexSize;
            cache[m] = bytes;
            return bytes;
        }

        private static long TextureBytes(Texture t, Dictionary<Texture, long> cache)
        {
            if (cache.TryGetValue(t, out long v)) return v;
            long bytes = t is Texture2D || t is Cubemap ? WorldV5Lighting.ImportedBytes(t) : 0;
            cache[t] = bytes;
            return bytes;
        }

        private static float Mean(List<float> values)
        {
            if (values.Count == 0) return 0f;
            double s = 0;
            foreach (float v in values) s += v;
            return (float)(s / values.Count);
        }

        private static float P95(List<float> values)
        {
            if (values.Count == 0) return 0f;
            var copy = new List<float>(values);
            copy.Sort();
            return copy[Mathf.Clamp(Mathf.CeilToInt(copy.Count * 0.95f) - 1, 0, copy.Count - 1)];
        }

        private static string FullPath(Transform t)
        {
            var parts = new List<string>();
            for (Transform p = t; p != null; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
