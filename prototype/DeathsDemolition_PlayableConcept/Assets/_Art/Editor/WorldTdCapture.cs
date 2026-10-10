using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DD.Prototype.World;
using DD.Prototype.World.Vegetation;
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

        public static void RenderPoses() => RenderPoses(false);

        /// <summary>WP-092 + WP-093 evidence: requires a populated Cinematic-tier runtime vegetation preview.</summary>
        public static void RenderCombinedPoses() => RenderPoses(true);

        /// <summary>Opt-in companion evidence. First engine window is palace + SW only; no aggregate gate is changed.</summary>
        public static void RenderCombinedDiagnostics()
        {
            string dir = OutDir();
            using (var diagnostics = new TdCoverDiagnostics(dir))
                WithLook(cam =>
                {
                    diagnostics.Observe(cam);
                    foreach ((string name, Vector2 feet) in Poses)
                    {
                        if (name != "td2-palace" && name != "td7-corner-sw") continue;
                        Vector3 ground = Ground(feet);
                        Vector3 focus = ground + Vector3.up * FocusHeight;
                        cam.orthographic = false;
                        cam.fieldOfView = RigFov;
                        cam.transform.SetPositionAndRotation(focus + RigOffset, Quaternion.LookRotation(-RigOffset.normalized, Vector3.up));
                        diagnostics.Label(name + "-beauty-original");
                        Save(Grab(cam, Width, Height), Path.Combine(dir, name + ".png"));
                        diagnostics.MarkBareEntropy();
                        diagnostics.Label(name + "-bare-original");
                        BareShare bare = BareGroundShare(cam, Path.Combine(dir, name + "-bare"));
                        File.WriteAllText(Path.Combine(dir, name + "-original-metric.json"), "{\"bare\": " + F(bare.Ground) + ", \"legacyBlack\": " + F(bare.LegacyBlack) + "}");
                        diagnostics.Capture(cam, name, feet, ground);
                    }
                }, true, false);
        }

        private static void RenderPoses(bool requireVegetation) => WithLook(cam =>
        {
            string dir = OutDir();
            var bareRows = new StringBuilder();
            var legacyRows = new StringBuilder();
            var splitRows = new StringBuilder();
            var gateRows = new StringBuilder();
            float bareSum = 0f, legacySum = 0f, naturalSum = 0f, pavedSum = 0f, gateSum = 0f;
            long gateBare = 0, gateSeen = 0;
            int gatePoses = 0;
            foreach ((string name, Vector2 feet) in Poses)
            {
                Vector3 ground = Ground(feet);
                Vector3 focus = ground + Vector3.up * FocusHeight;
                cam.orthographic = false;
                cam.fieldOfView = RigFov;
                cam.transform.SetPositionAndRotation(focus + RigOffset, Quaternion.LookRotation(-RigOffset.normalized, Vector3.up));
                Save(Grab(cam, Width, Height), Path.Combine(dir, name + ".png"));
                BareShare bare = BareGroundShare(cam, requireVegetation ? Path.Combine(dir, name + "-bare") : null);
                bareRows.Append(bareRows.Length > 0 ? ", " : "").Append("\"" + name + "\": " + F(bare.Ground));
                legacyRows.Append(legacyRows.Length > 0 ? ", " : "").Append("\"" + name + "\": " + F(bare.LegacyBlack));
                splitRows.Append(splitRows.Length > 0 ? ", " : "").Append("\"" + name + "\": {\"natural\": " + F(bare.Natural) + ", \"paved\": " + F(bare.Paved) + "}");
                bareSum += bare.Ground;
                legacySum += bare.LegacyBlack;
                naturalSum += bare.Natural;
                pavedSum += bare.Paved;
                if (bare.VegSeen > 0)
                {
                    float share = (float)bare.VegBare / bare.VegSeen;
                    gateRows.Append(gateRows.Length > 0 ? ", " : "").Append("\"" + name + "\": {\"bare\": " + F(share) + ", \"bareCells\": " + bare.VegBare + ", \"seen\": " + bare.VegSeen + "}");
                    gateSum += share;
                    gateBare += bare.VegBare;
                    gateSeen += bare.VegSeen;
                    gatePoses++;
                }
            }

            int n = Poses.Length;
            string gate = gatePoses == 0 ? "null"
                : "{\"metric\": \"" + VegGateMetric + "\", " + gateRows + ", \"mean\": " + F(gateSum / gatePoses) + ", \"pooled\": " + F(gateSeen > 0 ? (float)gateBare / gateSeen : 0f) +
                  ", \"poses\": " + gatePoses + ", \"limit\": " + F(VegGateLimit) + ", \"pass\": " + (gatePoses == n && gateSum / gatePoses <= VegGateLimit ? "true" : "false") + "}";
            File.WriteAllText(Path.Combine(dir, "bare-ground.json"),
                "{" + bareRows + ", \"mean\": " + F(bareSum / n) + ", \"metric\": \"" + BareMetric + "\", \"split\": {" + splitRows + ", \"mean\": {\"natural\": " + F(naturalSum / n) +
                ", \"paved\": " + F(pavedSum / n) + "}}, \"legacyBlack\": {" + legacyRows + ", \"mean\": " + F(legacySum / n) + "}, \"gate\": " + gate + "}");
            if (gatePoses > 0)
                Debug.Log("[TDCAP] vegetation-ground gate (" + VegGateMetric + "): mean " + F(gateSum / gatePoses) + " over " + gatePoses + " poses (limit " + F(VegGateLimit) + "), pooled " +
                          F(gateSeen > 0 ? (float)gateBare / gateSeen : 0f));
            Debug.Log("[TDCAP] " + n + " TD poses -> " + dir + " bare-ground mean " + F(bareSum / n) + " (" + BareMetric + "; natural " + F(naturalSum / n) +
                      ", paved " + F(pavedSum / n) + "; legacy black-threshold " + F(legacySum / n) + ")");
        }, requireVegetation);

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

        /// <summary>The bare-ground metric's name in bare-ground.json (R3 10.10: what ground shows, by id, not by colour).</summary>
        public const string BareMetric = "ground-id-v2";

        /// <summary>Ground shaders: natural (moss, terrain: the vegetation's ground) and paved (plaza slate, path granite, route plates).</summary>
        public static readonly string[] NaturalGroundShaders = { "DD/GroundMoss", "DD/GroundTerrain" };
        public static readonly string[] PavedGroundShaders = { "DD/GroundTiles", "DD/GroundBlend" };

        /// <summary>The id colours of the ground render: natural ground, paved ground (anything else is cover).</summary>
        public static readonly Color32 NaturalId = new Color32(255, 0, 255, 255), PavedId = new Color32(0, 255, 255, 255);

        /// <summary>One pose's bare ground: <see cref="Ground"/> = natural + paved (the gate value), and the legacy black-threshold share.</summary>
        public struct BareShare
        {
            public float Ground, Natural, Paved, LegacyBlack;
            /// <summary>The gate (<see cref="VegGateMetric"/>): pixels whose line of sight ends on vegetation ground, and those of them that show it bare.</summary>
            public int VegSeen, VegBare;
        }

        /// <summary>
        /// C-00 10.10 decision (B) · the gate's scope: vegetation ground. Paths, paving, desire paths, the karesansui gravel, the amaochi
        /// drip strip, water and the ground under roofs are designed bare surfaces (the user asked for more roads) and are not counted.
        /// A cell is vegetation ground when the grid marks it Ground and none of Path / Plaza / Water / Excluded / Roofed, and its
        /// final zone is one of <see cref="VegetationZones"/>. Gate: the mean of the poses' shares ≤ <see cref="VegGateLimit"/>.
        /// </summary>
        public const string VegGateMetric = "veg-ground-v1";
        public const float VegGateLimit = 0.25f;

        public static readonly VegetationZone[] VegetationZones =
        {
            VegetationZone.Lawn, VegetationZone.Grove, VegetationZone.GroveSkirt, VegetationZone.Bamboo, VegetationZone.BambooEdge,
            VegetationZone.BambooPathSide, VegetationZone.PondBank, VegetationZone.PondShore, VegetationZone.PathEdge, VegetationZone.WallBase,
            VegetationZone.WallBaseShadow, VegetationZone.WallOuter, VegetationZone.BuildingSurround, VegetationZone.BuildingShadow,
            VegetationZone.Courtyard, VegetationZone.PlazaEdge, VegetationZone.BridgeApproach, VegetationZone.MossCourt,
            VegetationZone.KaresansuiCollar,
        };

        private const VegetationGrid.Flag NotVegetationGround = VegetationGrid.Flag.Path | VegetationGrid.Flag.Plaza | VegetationGrid.Flag.Water |
                                                                 VegetationGrid.Flag.Excluded | VegetationGrid.Flag.Roofed;

        /// <summary>True when grid cell <paramref name="i"/> is vegetation ground (see <see cref="VegGateMetric"/>).</summary>
        public static bool IsVegetationGround(ushort flags, byte finalZone)
        {
            if ((flags & (ushort)VegetationGrid.Flag.Ground) == 0 || (flags & (ushort)NotVegetationGround) != 0) return false;
            return Array.IndexOf(VegetationZones, (VegetationZone)finalZone) >= 0;
        }

        /// <summary>
        /// WP-092 · The share of the TD frame that shows bare ground — the gate (C-00 08.10: mean ≤ 25 %, plaza ≤ 80 %).
        /// R3 10.10 (<see cref="BareMetric"/>): an id render decides it. The ground renderers draw in flat unlit id colours (natural /
        /// paved), the water flat white (cover, as before), fog / shaft cards hidden, black clear, no post, no fog, no MSAA, the
        /// vegetation preview and every other renderer as they are: a pixel is bare when it shows a ground id. The legacy measure
        /// (ground hidden, a pixel bare when its colour sums ≤ 6) is kept beside it: it also counted dark covered pixels — roof tile
        /// channels, eaves and shaded interiors that the no-post render takes to 0 — as bare (Codex 08.10: 450 822 of 1 036 800
        /// diagnostic pixels at exactly 0, cause unproven).
        /// </summary>
        private static BareShare BareGroundShare(Camera cam, string diagnosticPrefix)
        {
            var result = new BareShare { LegacyBlack = LegacyBlackShare(cam, diagnosticPrefix) };
            Shader unlit = DD.Prototype.Rendering.PrototypeShaders.Unlit;
            if (unlit == null) throw new InvalidOperationException("[TDCAP] the URP Unlit shader is missing from the shader library: no ground-id render");
            var natural = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
            var paved = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
            var water = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
            SetUnlitColour(natural, NaturalId);
            SetUnlitColour(paved, PavedId);
            SetUnlitColour(water, new Color32(255, 255, 255, 255));
            var swapped = new List<(Renderer renderer, Material[] materials)>();
            var hidden = new List<Renderer>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r.sharedMaterial == null) continue;
                string shader = r.sharedMaterial.shader.name;
                Material id = Array.IndexOf(NaturalGroundShaders, shader) >= 0 ? natural
                    : Array.IndexOf(PavedGroundShaders, shader) >= 0 ? paved
                    : shader.StartsWith("DD/Water", StringComparison.Ordinal) ? water : null;
                if (id != null)
                {
                    Material[] was = r.sharedMaterials;
                    var ids = new Material[was.Length];
                    for (int i = 0; i < ids.Length; i++) ids[i] = id;
                    r.sharedMaterials = ids;
                    swapped.Add((r, was));
                }
                else if (shader == "DD/FogSheet" || shader == "DD/ShaftCard")
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
            }

            CameraClearFlags flags = cam.clearFlags;
            Color background = cam.backgroundColor;
            bool fog = RenderSettings.fog;
            var data = cam.GetUniversalAdditionalCameraData();
            bool post = data != null && data.renderPostProcessing;
            try
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                RenderSettings.fog = false;
                if (data != null) data.renderPostProcessing = false;
                int w = Width / 4, h = Height / 4;
                Color32[] px = Grab(cam, w, h, 1);
                int nat = 0, pav = 0;
                Color32[] mask = diagnosticPrefix != null ? new Color32[px.Length] : null;
                for (int i = 0; i < px.Length; i++)
                {
                    GroundClass c = Classify(px[i]);
                    if (c == GroundClass.Natural) nat++;
                    else if (c == GroundClass.Paved) pav++;
                    if (mask != null) mask[i] = c == GroundClass.Natural ? new Color32(255, 255, 255, 255) : c == GroundClass.Paved ? new Color32(128, 128, 128, 255) : new Color32(0, 0, 0, 255);
                }

                if (diagnosticPrefix != null)
                {
                    Save(px, diagnosticPrefix + "-id.png", w, h);
                    Save(mask, diagnosticPrefix + "-id-mask.png", w, h);
                }

                result.Natural = (float)nat / px.Length;
                result.Paved = (float)pav / px.Length;
                result.Ground = result.Natural + result.Paved;
                VegetationGround(cam, px, w, h, ref result, diagnosticPrefix);
                return result;
            }
            finally
            {
                cam.clearFlags = flags;
                cam.backgroundColor = background;
                RenderSettings.fog = fog;
                if (data != null) data.renderPostProcessing = post;
                foreach ((Renderer r, Material[] was) in swapped) r.sharedMaterials = was;
                foreach (Renderer r in hidden) r.enabled = true;
                UnityEngine.Object.DestroyImmediate(natural);
                UnityEngine.Object.DestroyImmediate(paved);
                UnityEngine.Object.DestroyImmediate(water);
            }
        }

        /// <summary>
        /// The gate pass over the id render's pixels (bottom-left origin, as GetPixels32): a physics ray per pixel centre (vegetation has
        /// no colliders, so a ray through grass reaches the ground behind it). A hit within 0.3 m of the grid's ground height is ground;
        /// a pixel that shows a ground id but whose ray meets an invisible collider first is placed on the ground by marching the grid's
        /// height field. Seen = the ground point is vegetation ground; bare = it also shows a ground id. Needs the zoner's snapshot
        /// (DD.Prototype.World.Vegetation.VegetationPreview.LastDiagnostics, recorded inside <see cref="WithLook"/>); none → the gate is not measured.
        /// </summary>
        private static void VegetationGround(Camera cam, Color32[] px, int w, int h, ref BareShare result, string diagnosticPrefix)
        {
            DD.Prototype.World.Vegetation.VegetationPreview.DiagnosticSnapshot snap = DD.Prototype.World.Vegetation.VegetationPreview.LastDiagnostics;   // qualified: this class has a VegetationPreview method
            if (snap == null || snap.Grid == null || snap.FinalZone == null) return;
            VegetationGrid grid = snap.Grid;
            Physics.SyncTransforms();
            Color32[] mask = diagnosticPrefix != null ? new Color32[px.Length] : null;
            int seen = 0, bare = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                GroundClass c = Classify(px[i]);
                Ray ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / w, (y + 0.5f) / h, 0f));
                int cell = -1;
                if (Physics.Raycast(ray, out RaycastHit hit, 600f, ~0, QueryTriggerInteraction.Ignore))
                {
                    int k = CellOf(grid, hit.point);
                    if (k >= 0 && Mathf.Abs(hit.point.y - grid.GroundY[k]) <= 0.3f) cell = k;
                }

                if (cell < 0 && c != GroundClass.Cover) cell = MarchGround(grid, ray);
                bool veg = cell >= 0 && IsVegetationGround(grid.Flags[cell], snap.FinalZone[cell]);
                if (veg)
                {
                    seen++;
                    if (c == GroundClass.Natural) bare++;
                }

                if (mask != null) mask[i] = !veg ? new Color32(0, 0, 0, 255) : c == GroundClass.Natural ? new Color32(255, 255, 255, 255) : new Color32(60, 160, 60, 255);
            }

            if (mask != null) Save(mask, diagnosticPrefix + "-veg-mask.png", w, h);
            result.VegSeen = seen;
            result.VegBare = bare;
        }

        private static int CellOf(VegetationGrid grid, Vector3 p)
        {
            int cx = Mathf.FloorToInt((p.x - grid.Origin.x) / grid.Cell), cz = Mathf.FloorToInt((p.z - grid.Origin.y) / grid.Cell);
            return cx < 0 || cz < 0 || cx >= grid.Width || cz >= grid.Height ? -1 : grid.Index(cx, cz);
        }

        /// <summary>The first grid cell whose ground the ray reaches (0.1 m steps over 120 m), or -1.</summary>
        private static int MarchGround(VegetationGrid grid, Ray ray)
        {
            for (float t = 0f; t < 120f; t += 0.1f)
            {
                Vector3 p = ray.GetPoint(t);
                int k = CellOf(grid, p);
                if (k >= 0 && p.y <= grid.GroundY[k] + 0.02f) return k;
            }

            return -1;
        }

        public enum GroundClass { Cover, Natural, Paved }

        /// <summary>A pixel of the id render: an id colour within 24 per channel, anything else cover (lit, shaded, black or blended).</summary>
        public static GroundClass Classify(Color32 c)
        {
            if (Near(c, NaturalId)) return GroundClass.Natural;
            if (Near(c, PavedId)) return GroundClass.Paved;
            return GroundClass.Cover;
        }

        private static bool Near(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) <= 24 && Mathf.Abs(a.g - b.g) <= 24 && Mathf.Abs(a.b - b.b) <= 24;

        private static void SetUnlitColour(Material m, Color32 c)
        {
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Surface", 0f);   // opaque
        }

        /// <summary>The legacy measure (WP-092 08.10), kept for comparison: every ground-shader renderer (and the fog / shaft cards)
        /// hidden against a black clear, a pixel bare when its colour sums ≤ 6 — dark covered pixels included.</summary>
        private static float LegacyBlackShare(Camera cam, string diagnosticPrefix)
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
                Color32[] mask = diagnosticPrefix != null ? new Color32[px.Length] : null;
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 c = px[i];
                    bool isBare = c.r + c.g + c.b <= 6;
                    if (isBare) bare++;
                    if (mask != null) mask[i] = isBare ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                }

                if (diagnosticPrefix != null)
                {
                    Save(px, diagnosticPrefix + "-source.png", Width / 4, Height / 4);
                    Save(mask, diagnosticPrefix + "-mask.png", Width / 4, Height / 4);
                }

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

        [Serializable]
        private sealed class CombinedCaptureContext
        {
            public string scenePath, vegetationType, completedUtc;
            public string[] poseNames;
            public Vector2[] poseFeet;
            public Vector3 rigOffset;
            public int vegetationTier, vegetationInstances, poseCount, width, height;
            public float verticalFov, focusHeight;
        }

        private static void WithLook(Action<Camera> body, bool requireVegetation = false, bool writeCombinedContext = true)
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
            IDisposable vegetation = null, zoneSnapshot = null;
            try
            {
                Scene world = SceneManager.GetActiveScene();
                // C-00 10.10 (B): the gate reads the zoner's own grid and final zones — recorded while the preview populates
                if (requireVegetation) zoneSnapshot = DD.Prototype.World.Vegetation.VegetationPreview.BeginDiagnostics();
                vegetation = VegetationPreview(world, requireVegetation, out int tier, out int instances);
                body(cam);
                if (requireVegetation && writeCombinedContext)
                {
                    var context = new CombinedCaptureContext
                    {
                        scenePath = world.path, vegetationType = VegetationPreviewType,
                        completedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                        vegetationTier = tier, vegetationInstances = instances,
                        poseCount = Poses.Length, width = Width, height = Height, verticalFov = RigFov,
                        focusHeight = FocusHeight, rigOffset = RigOffset,
                        poseNames = new string[Poses.Length], poseFeet = new Vector2[Poses.Length],
                    };
                    for (int i = 0; i < Poses.Length; i++)
                    {
                        context.poseNames[i] = Poses[i].name;
                        context.poseFeet[i] = Poses[i].feet;
                    }
                    File.WriteAllText(Path.Combine(OutDir(), "combined-capture-context.json"), JsonUtility.ToJson(context, true));
                }
            }
            finally
            {
                vegetation?.Dispose();
                zoneSnapshot?.Dispose();
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

        private static IDisposable VegetationPreview(Scene world, bool required, out int selectedTier, out int instances)
        {
            selectedTier = -1;
            instances = 0;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(VegetationPreviewType, false);
                if (type == null) continue;
                FieldInfo tier = type.GetField(required ? "TierCinematic" : "TierHigh", BindingFlags.Public | BindingFlags.Static);
                MethodInfo populate = type.GetMethod("Populate", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Scene), typeof(int) }, null);
                if (populate == null)
                {
                    if (required) throw new InvalidOperationException("[TDCAP] combined capture requires Populate(Scene, int)");
                    Debug.LogWarning("[TDCAP] vegetation preview: " + VegetationPreviewType + " has no Populate(Scene, int)");
                    return null;
                }
                selectedTier = tier != null ? Convert.ToInt32(tier.GetValue(null)) : (required ? 2 : 1);
                IDisposable handle = populate.Invoke(null, new object[] { world, selectedTier }) as IDisposable;
                try
                {
                    PropertyInfo total = handle?.GetType().GetProperty("TotalInstances", BindingFlags.Public | BindingFlags.Instance);
                    instances = total != null ? Convert.ToInt32(total.GetValue(handle)) : 0;
                    if (required && (handle == null || instances <= 0))
                        throw new InvalidOperationException("[TDCAP] combined capture requires a populated vegetation field; TotalInstances=" + instances);
                    Debug.Log("[TDCAP] vegetation preview: tier=" + selectedTier + " instances=" + instances);
                    return handle;
                }
                catch
                {
                    handle?.Dispose();
                    throw;
                }
            }

            if (required) throw new InvalidOperationException("[TDCAP] combined capture requires WP-093 VegetationPreview");
            Debug.Log("[TDCAP] vegetation preview: absent (WP-093 not in this project) — ground cover without the grass");
            return null;
        }

        private static Vector3 Ground(Vector2 xz)
        {
            var top = new Vector3(xz.x, 30f, xz.y);
            if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
            return NavMesh.SamplePosition(new Vector3(xz.x, 0f, xz.y), out NavMeshHit h, 3f, NavMesh.AllAreas) ? h.position : new Vector3(xz.x, 0f, xz.y);
        }

        private static Color32[] Grab(Camera cam, int w, int h) => Grab(cam, w, h, 4);

        /// <summary>One render into a <paramref name="w"/>×<paramref name="h"/> ARGB32 target; <paramref name="msaa"/> 1 for the id render (no blended edges).</summary>
        private static Color32[] Grab(Camera cam, int w, int h, int msaa)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa };
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
            Save(px, path, w, h);
        }

        private static void Save(Color32[] px, string path, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
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
