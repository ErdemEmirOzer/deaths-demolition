using System;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-074 B5b-1 · World v5 architecture shell: the AP-023 kit (manifest v004, B5a library) dresses the v6 greybox where the kit
    /// matches the layout. That covers the palace hall on its plinth (R1), built from the kit's own 36 × 13 recipe (AP023-build-
    /// architecture-b-v001.py hall_layout(), the AD-passed preview), with the R1 doors as open bays and no rear stair. It also covers
    /// the ring tsuiji, the forecourt walls, the karamon, the axis lanterns and the centre planters. B5b-2 (manifest v005): the slate
    /// plaza (<see cref="WorldV5Ground"/>), the kit planters with pine islands, the corner yagura. The kit's colliders replace the
    /// greybox boxes they dress; GreyboxV6Builder bakes NavMesh v5 after this pass. Roofs keep the greybox roof contract (CameraOccluder
    /// layer, CameraOccluderClass Roof, out of the bake, collider for the top-down fade). The kairō (open bays carry koran rails), R2–R6,
    /// the pond and the planting stay greybox until their slices.
    /// Coordinates: the kit is authored in Blender (x, y, z-up). A placement matrix M in that space maps to Unity through
    /// <see cref="WorldPlacementAsset.ToUnity"/> / <see cref="WorldPlacementAsset.ToUnityYaw"/> (prefab root space = Blender (x, z, y)).
    /// </summary>
    public static class WorldV5Dress
    {
        public const string RootName = "Dress v5 (AP-023)";
        public const string Ap = "AP-023";
        public const string HallRoofName = "Hall_Roof";
        public const string RoofOccluderName = "RoofOccluder";

        /// <summary>
        /// The hall frame's front plinth edge (kit y = 0) sits at z 37.5. At that line the kit's E/W stairs (y 6.5) meet the R1 E/W
        /// doors (z 44), and the wall lines (y 1.5 / 11.5) land on R1's z 39 / 49.
        /// </summary>
        public const float HallFrontZ = 37.5f;

        /// <summary>Ring tsuiji centreline: body ±0.30 → inner face 55.0 = the ring (the greybox RingWall's inner face).</summary>
        public const float RingCentreline = 55.3f;

        // ---------------------------------------------------------------- AP-023 palace kit constants (hall frame, metres)

        private const float ZPl = 1.20f, ZFl = 1.28f, YWl = 1.50f, Post = 0.38f, RailY = 0.10f, BayClear = 2.62f, EaveOut = 0.90f;
        private static float PUp(float d) => 6.80f + 0.404f * d + 0.0218f * d * d;
        private static float PMk(float d) => 4.456f + 0.36f * d + 0.02f * d * d;
        /// <summary>Wall-plate top (rafter bottom at the wall line) ≈ 7.765.</summary>
        public static readonly float ZWp = PUp(YWl + EaveOut) - 0.03f - 0.10f;
        /// <summary>Mokoshi ledger top ≈ 5.039.</summary>
        public static readonly float ZLedgerTop = PMk(1.13f + 0.07f + 0.60f) - 0.03f - 0.10f;
        private const float ZKokabeLo0 = ZFl + 2.55f, ZKokabeLo1 = 5.25f, ZKoshiUp0 = 5.45f, ZKokabeUp0 = 6.65f, RidgeTop = 11.50f;

        /// <summary>The kit's hall_anchor per palace_kit variation: the prefab pivot in the hall frame (V01–V09 at the origin).</summary>
        public static Vector3 Anchor(int v)
        {
            switch (v)
            {
                case 10: return new Vector3(0f, YWl, ZFl);
                case 11: return new Vector3(-YWl, YWl, ZFl);
                case 12: case 13: case 14: return new Vector3(0f, 0f, ZPl);
                case 15: case 17: case 18: case 19: return new Vector3(0f, RailY, ZFl);
                case 16: return new Vector3(0f, 0f, ZFl);
                case 20: case 21: case 22: return new Vector3(-0.75f, YWl, ZFl);
                case 23: return new Vector3(0f, YWl, ZKoshiUp0);
                case 24: return new Vector3(0f, YWl, ZKokabeLo0);
                case 25: return new Vector3(0f, YWl, ZKokabeUp0);
                case 26: case 27: case 29: case 30: case 31: return new Vector3(0f, YWl, ZWp);
                case 28: return new Vector3(-YWl, YWl, ZWp);
                case 32: case 34: return new Vector3(0f, YWl, ZLedgerTop);
                case 33: return new Vector3(-YWl, YWl, ZLedgerTop);
                default: return Vector3.zero;
            }
        }

        public struct KitPlacement
        {
            public int V;
            /// <summary>Blender-space matrix in the hall frame (x −18…18 along the front, y 0 front edge … 13 back edge, z up).</summary>
            public Matrix4x4 M;
            /// <summary>Upper roof and mokoshi (V26–V34): parented under the hall roof occluder.</summary>
            public bool Roof;
        }

        /// <summary>Walls of the hall frame: F front (−y, south), E east, B back (north), W west.</summary>
        public static Matrix4x4 SideM(char side, float s)
        {
            switch (side)
            {
                case 'F': return Mz(-18f + s, 0f, 0f, 0f);
                case 'E': return Mz(18f, s, 0f, 90f);
                case 'B': return Mz(18f - s, 13f, 0f, 180f);
                default: return Mz(-18f, 13f - s, 0f, 270f);
            }
        }

        private static readonly (float x, float y, float deg)[] Corners = { (18f, 0f, 0f), (18f, 13f, 90f), (-18f, 13f, 180f), (-18f, 0f, 270f) };

        private static Matrix4x4 Mz(float x, float y, float z, float degrees)
        {
            return Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.AngleAxis(degrees, Vector3.forward), Vector3.one);
        }

        /// <summary>
        /// The kit recipe (hall_layout) with two layout rules. First, a shoji slot stays open (no panels; posts, bands and plaster
        /// above the 2.55 m head stay) where an R1 door of the v6 layout overlaps its clear width by ≥ min(1 m, door/2). Second, the
        /// back has no stair: the v6 hall has no rear exit, and K6 runs behind it. <paramref name="openBay"/>(side, s, clear half-width).
        /// </summary>
        public static List<KitPlacement> HallLayout(Func<char, float, float, bool> openBay)
        {
            var list = new List<KitPlacement>();
            void Add(int v, Matrix4x4 frame, Matrix4x4? extra, float sx)
            {
                Matrix4x4 m = frame * (extra ?? Matrix4x4.Translate(Anchor(v)));
                if (sx != 1f) m *= Matrix4x4.Scale(new Vector3(sx, 1f, 1f));
                list.Add(new KitPlacement { V = v, M = m, Roof = v >= 26 });
            }

            void S(int v, char side, float s, Matrix4x4? extra = null, float sx = 1f) => Add(v, SideM(side, s), extra, sx);
            Matrix4x4 slotL = Matrix4x4.Translate(new Vector3(-0.75f, YWl, ZFl));
            Matrix4x4 slotR = Matrix4x4.Translate(new Vector3(0.75f, YWl, ZFl)) * Matrix4x4.Rotate(Quaternion.AngleAxis(180f, Vector3.forward));

            foreach ((float x, float y, float deg) in Corners)
            foreach (int v in new[] { 2, 11, 13, 16, 28, 33 })
                Add(v, Mz(x, y, 0f, deg), null, 1f);

            foreach (char side in new[] { 'F', 'B' })
            {
                bool front = side == 'F';
                for (int k = 0; k < 10; k++)
                {
                    float s = 4.5f + 3f * k;
                    S(front && (s == 7.5f || s == 16.5f || s == 19.5f || s == 28.5f) ? 3 : 1, side, s);
                }

                for (int k = 0; k < 11; k++)
                {
                    float sb = 3f + 3f * k;
                    S(12, side, sb);
                    S(32, side, sb);
                    if (!openBay(side, sb, BayClear * 0.5f)) { S(20, side, sb, slotL); S(20, side, sb, slotR); }
                    S(24, side, sb);
                    S(23, side, sb);
                    S(25, side, sb);
                }

                for (int k = 1; k < 11; k++) S(10, side, 1.5f + 3f * k);
                for (int k = 0; k < 10; k++) S(26, side, 4.5f + 3f * k);
                if (front)
                {
                    foreach ((int v, float s) in new[] { (15, 3f), (17, 5.25f), (18, 9.75f), (15, 12f), (17, 14.25f), (18, 21.75f), (15, 24f), (17, 26.25f), (18, 30.75f), (15, 33f) })
                        S(v, side, s);
                    S(6, side, 7.5f);
                    S(8, side, 18f);
                    S(6, side, 28.5f);
                    for (int k = 0; k < 8; k++) S(29, side, 7.5f + 3f * k);
                }
                else
                {
                    for (int k = 0; k < 11; k++) S(15, side, 3f + 3f * k);
                }

                S(30, side, 31.5f);
                S(31, side, 31.5f);
            }

            foreach (char side in new[] { 'E', 'W' })
            {
                S(5, side, 4f);
                S(4, side, 6.5f);
                S(5, side, 9f);
                S(7, side, 6.5f);
                foreach (float s in new[] { 3f, 6f, 9f }) { S(12, side, s); S(32, side, s); }
                S(14, side, 11f);
                S(34, side, 11f);
                foreach ((int v, float s) in new[] { (19, 2f), (19, 3f), (17, 4.25f), (18, 8.75f), (19, 10f), (19, 11f) }) S(v, side, s);
                foreach (float s in new[] { 4.5f, 8.5f }) S(10, side, s);
                S(26, side, 4.5f);
                S(26, side, 7.5f);
                S(27, side, 9.5f);
                foreach ((float sb, float w) in new[] { (3f, 3f), (6.5f, 4f), (10f, 3f) })
                {
                    if (!openBay(side, sb, (w - Post) * 0.5f)) { S(20, side, sb, slotL); S(20, side, sb, slotR); }
                    float k = (w - Post) / BayClear;
                    S(24, side, sb, null, k);
                    S(23, side, sb, null, k);
                    S(25, side, sb, null, k);
                    if (w > 3f)
                        foreach (float dx in new[] { -1.405f, 1.405f })
                            S(24, side, sb, Matrix4x4.Translate(new Vector3(dx, YWl, ZFl)) * Matrix4x4.Scale(new Vector3(0.5f / BayClear, 1f, (ZKokabeLo0 - ZFl) / (ZKokabeLo1 - ZKokabeLo0))));
                }
            }

            return list;
        }

        /// <summary>R1's doors (GreyboxV6Builder.Rooms[0]) against a hall slot: the slot centre on its wall line, measured along that wall.</summary>
        public static bool R1DoorOpens(char side, float s, float clearHalf)
        {
            GreyboxV6Builder.Room r1 = GreyboxV6Builder.Rooms[0];
            Vector3 c = SideM(side, s).MultiplyPoint3x4(new Vector3(0f, YWl, 0f));
            char wall = side == 'F' ? 'S' : side == 'B' ? 'N' : side;
            float along = wall == 'S' || wall == 'N' ? c.x : c.y + HallFrontZ;
            if (r1.Doors == null) return false;
            foreach (GreyboxV6Builder.Door d in r1.Doors)
            {
                if (d.Side != wall) continue;
                float overlap = Mathf.Min(along + clearHalf, d.At + d.Width * 0.5f) - Mathf.Max(along - clearHalf, d.At - d.Width * 0.5f);
                if (overlap >= Mathf.Min(1f, d.Width * 0.5f)) return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- the pass

        /// <summary>What the pass placed and removed (for the builder's report and the tests).</summary>
        public sealed class Report
        {
            public int Instances, RemovedBoxes, HallPieces, WallPieces, OpenBays, PlazaCells;
            public override string ToString() => "dress v5: " + Instances + " AP-023 instances (hall " + HallPieces + ", walls " + WallPieces + ", " + OpenBays + " open bays), " + RemovedBoxes + " greybox boxes replaced, plaza " + PlazaCells + " cells";
        }

        private static readonly Dictionary<string, GameObject> s_prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        /// <summary>Greybox pieces the kit replaces (name prefixes under the greybox root).</summary>
        public static readonly string[] ReplacedPrefixes = { "Hall_Plinth", "Hall_Stair", "R1_MainHall_", "RingWall_", "Court_", "Karamon_", "Lantern_", "Planter_", "Vista", "K3_", "K6_", "R6_Chumon_",
                                                           "K1_", "K2_", "K4_", "K5_", "R2_", "R3_", "R4_", "R5_", "Tree_", "R7_Sakura_",
                                                           "Bridge_", "Causeway", "Yard_", "R7_W_", "R7_E_", "R7_N_", "R7_S_", "Karesansui_" };

        /// <summary>
        /// B5b-4 · The straight open kairō dressed with AP-023 bays (the greybox corridors are entered from the side — C-00 decision (b):
        /// v007's rail-less kairo_open; the kit's own rail part was switched off per instance before it). The kit
        /// floor is walkable at 0.25 (the greybox deck), 4.74 m wide, posts every 2.4 m at ±2.1–2.37 (clear 4.2 ≥ the greybox 4.0).
        /// K2 / K4 (angled joints) and K5 / K1 stay greybox.
        /// </summary>
        public static readonly (string id, Vector2[] path, bool closed)[] KitKairo =
        {
            ("K3_CloisterWest", new[] { new Vector2(-22f, 22f), new Vector2(-22f, 36f) }, false),
            ("K3_CloisterEast", new[] { new Vector2(22f, 22f), new Vector2(22f, 36f) }, false),
            ("K6_RearKairo", new[] { new Vector2(-22f, 52.5f), new Vector2(22f, 52.5f) }, false),
            // B5b-5: K1 closed (V02; its doors as half-open V03 bays), the angled links and the shore walk open (V01, rails off).
            ("K1_WestKairo", new[] { new Vector2(-33f, -16f), new Vector2(-33f, 20f) }, true),
            ("K2_NorthWestLink", GreyboxV6Builder.CorridorPath("K2_NorthWestLink"), false),   // v007-B: kairo_angle corners (Kairo)
            ("K4_EastKairo", GreyboxV6Builder.CorridorPath("K4_EastKairo"), false),
            ("K5_SouthShore", new[] { new Vector2(-22f, -46f), new Vector2(0f, -46f) }, false),
        };

        /// <summary>Kit pieces under the kairō group: every interior vertex a kairo_angle corner, every <see cref="KairoDoorPieces"/> door, and
        /// each run of a segment — less the corners' arms and the doors — its length over 2.4 m in bays, rounded (≥ 1; none where arms meet).</summary>
        public static int BayCount()
        {
            int n = 0;
            foreach ((string id, Vector2[] path, bool closed) in KitKairo)
            {
                n += Mathf.Max(0, path.Length - 2);
                for (int i = 0; i + 1 < path.Length; i++)
                {
                    Vector2 d = (path[i + 1] - path[i]).normalized;
                    Vector2 a = path[i] + d * (i > 0 ? KairoAngleArm : 0f), b = path[i + 1] - d * (i + 2 < path.Length ? KairoAngleArm : 0f);
                    foreach ((float from, float to, bool door) in KairoRuns(id, a, b))
                        n += door ? 1 : to - from >= MinBayLength ? Mathf.Max(1, Mathf.RoundToInt((to - from) / KairoBay)) : 0;
                }
            }

            return n;
        }

        /// <summary>
        /// WP-092 (S-03 08.10 (c)) · Kit doors on a kairō side: K1's west door (z 0) is AP-023 kairo_door V01 (3.6 m, 2.0 m clear on its −Y
        /// side). The half-open V03 bay it replaces left two 1.06 m slots, which the NavMesh closed although the player could walk through them.
        /// </summary>
        public static readonly (string corridor, Vector2 at, char side)[] KairoDoorPieces = { ("K1_WestKairo", new Vector2(-33f, 0f), 'W') };

        public const float KairoDoorLength = 3.6f;
        public const string KairoDoorFamily = "kairo_door";

        /// <summary>A segment a → b of corridor <paramref name="id"/> as runs along it (distances from a): bay runs, and the door pieces cut into them.</summary>
        public static List<(float from, float to, bool door)> KairoRuns(string id, Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            Vector2 d = (b - a) / Mathf.Max(len, 1e-6f);
            var doors = new List<float>();
            foreach ((string corridor, Vector2 at, char side) in KairoDoorPieces)
            {
                if (corridor != id) continue;
                float u = Vector2.Dot(at - a, d);
                if (u > KairoDoorLength * 0.5f && u < len - KairoDoorLength * 0.5f && Vector2.Distance(a + d * u, at) < 0.5f) doors.Add(u);
            }

            doors.Sort();
            var runs = new List<(float, float, bool)>();
            float cursor = 0f;
            foreach (float u in doors)
            {
                runs.Add((cursor, u - KairoDoorLength * 0.5f, false));
                runs.Add((u - KairoDoorLength * 0.5f, u + KairoDoorLength * 0.5f, true));
                cursor = u + KairoDoorLength * 0.5f;
            }

            runs.Add((cursor, len, false));
            return runs;
        }

        /// <summary>
        /// WP-092 (S-03 08.10 (b)4) · The kit door's kamoi (lintel) block starts 2.45 m over its 0.25 floor, under the Executioner's 2.66 m body:
        /// its collider goes (the beam stays in sight), so the door's physical head is the eave beam's. Returns the colliders switched off.
        /// </summary>
        internal static int LintelPass(GameObject door)
        {
            Physics.SyncTransforms();
            float y = door.transform.position.y;
            int n = 0;
            foreach (Collider c in door.GetComponentsInChildren<Collider>(true))
            {
                Bounds b = c.bounds;
                if (b.min.y - y > LintelBottomMin && b.min.y - y < LintelBottomMax && b.max.y - y < 4.2f) { c.enabled = false; n++; }
            }

            return n;
        }

        /// <summary>The kamoi collider's bottom over the piece's base (0.25 floor + 2.45 clear = 2.70) lies in this band.</summary>
        public const float LintelBottomMin = 2.55f, LintelBottomMax = 2.85f;

        public const float KairoBay = 2.4f;
        public const string OpenBayFamily = "kairo_open";
        public const string KairoGroupName = "Kairo";

        public const string VistaGroupName = "VistaForest";
        /// <summary>The vista band (B5b-3): tree bases between these square radii — outside the tsuiji's outer face (55.6) to the vista edge.</summary>
        public const float VistaInner = 58f, VistaOuter = 84f, VistaSpacing = 3.5f;
        /// <summary>The distant keep (HF-center's top-left tower): a yagura at 1.8× north-west beyond the ring.</summary>
        public static readonly Vector2 VistaKeep = new Vector2(-70f, 74f);

        /// <summary>The four centre planters (greybox BuildArena corners), 3 × 3 m.</summary>
        public static readonly Vector2[] PlanterCentres = { new Vector2(-9f, -9f), new Vector2(-9f, 9f), new Vector2(9f, -9f), new Vector2(9f, 9f) };

        public static Report Apply(Transform greybox, Transform worldRoot)
        {
            s_prefabs.Clear();
            var report = new Report();
            var dress = new GameObject(RootName).transform;
            dress.SetParent(worldRoot, false);

            List<(Vector3 position, float radius, bool greatSakura)> garden = GardenSpots(greybox);
            foreach (Transform t in new List<Transform>(Children(greybox)))
                foreach (string prefix in ReplacedPrefixes)
                    if (t.name.StartsWith(prefix, StringComparison.Ordinal)) { UnityEngine.Object.DestroyImmediate(t.gameObject); report.RemovedBoxes++; break; }

            report.HallPieces = Hall(dress, greybox, report);
            report.WallPieces = RingWall(dress) + Forecourt(dress);
            SideDoors(dress);
            Karamon(dress);
            Lanterns(dress);
            Planters(dress);
            Yagura(dress);
            Kairo(dress);
            Chumon(dress);
            Buildings(dress);
            GardenTrees(dress, garden);
            Ground(greybox);
            Ponds(dress, greybox);
            Bridges(dress);
            Grove(dress, greybox);
            Fences(dress);
            Karesansui(dress);
            Vista(dress);
            ArtSurfaceImporter.BuildHorizonPlates(dress.gameObject);   // the AP-019 horizon rings (contract radii) …
            WorldV5Sky.Build(dress);                                    // … in sky v2 (WP-075 C3): v2 sheets, cloud planes, landmarks
            WorldV5Atmosphere.Build(dress, greybox);                    // WP-075 C4 shaft cards + C5 ground fog sheets (tiered)
            dress.gameObject.AddComponent<LookV3Post>();                // WP-075 C7 post by tier / camera mode (grain, vignette, DoF, blur)
            report.PlazaCells = WorldV5Ground.Build(dress, (parent, family, v, m, role) => Place(parent, family, v, m, Vector3.zero, role));
            report.Instances = CountInstances(dress);
            return report;
        }

        private static IEnumerable<Transform> Children(Transform t)
        {
            for (int i = 0; i < t.childCount; i++) yield return t.GetChild(i);
        }

        private static int CountInstances(Transform dress)
        {
            int n = 0;
            foreach (Transform t in dress.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) n++;
            return n;
        }

        private static int Hall(Transform dress, Transform greybox, Report report)
        {
            var hall = new GameObject("Hall_R1").transform;
            hall.SetParent(dress, false);
            // The roof occluder: the upper roof's box (eaves 0.9 m past the plinth, wall plate − 0.3 … ridge) on CameraOccluder, out of
            // the bake — the top-down fade finds it and fades every roof piece under it (OccluderFadeState on the holder).
            var roof = new GameObject(HallRoofName) { layer = PrototypeLayers.CameraOccluder };
            roof.transform.SetParent(hall, false);
            var col = roof.AddComponent<BoxCollider>();
            float z0 = ZWp - 0.3f;
            col.center = new Vector3(0f, (z0 + RidgeTop + 0.1f) * 0.5f, HallFrontZ + 6.5f);
            col.size = new Vector3(36f + 2f * EaveOut, RidgeTop + 0.1f - z0, 13f + 2f * EaveOut);
            roof.AddComponent<CameraOccluderClass>().Kind = CameraOccluderKind.Roof;
            roof.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            int n = 0;
            foreach (KitPlacement p in HallLayout((side, s, half) =>
                     {
                         bool open = R1DoorOpens(side, s, half);
                         if (open) report.OpenBays++;
                         return open;
                     }))
            {
                Place(p.Roof ? roof.transform : hall, "palace_kit", p.V, p.M, new Vector3(0f, HallFrontZ, 0f), p.Roof ? "roof" : "structure");
                n++;
            }

            // Inside the wall line the kit has no floor: a slab x ±16.5, z 39…49 at the engawa height (1.28), planks tiled 1.08 m.
            Slab(hall, "Hall_Floor", new Vector3(0f, ZFl * 0.5f, HallFrontZ + 6.5f), new Vector3(36f - 2f * YWl, ZFl, 13f - 2f * YWl), "wood_planks", 1.08f);
            // The greybox dais and shoji screens stay (interior), in kit materials.
            Restyle(greybox, "Hall_Dais", "wood_planks", 1.08f);
            Restyle(greybox, "Hall_Screen_W", "plaster_interior", 3f);
            Restyle(greybox, "Hall_Screen_E", "plaster_interior", 3f);
            return n;
        }

        private static int RingWall(Transform dress)
        {
            var ring = new GameObject("RingWall").transform;
            ring.SetParent(dress, false);
            float c = RingCentreline;
            int n = 0;
            // Corners: arms 1.5 along +x / +y from the centreline intersection → SW 0°, SE 90°, NE 180°, NW 270°.
            foreach ((float x, float y, float deg) in new[] { (-c, -c, 0f), (c, -c, 90f), (c, c, 180f), (-c, c, 270f) })
            {
                Place(ring, "wall_kit", 3, Mz(x, y, 0f, deg), Vector3.zero, "structure");
                n++;
            }

            n += Run(ring, new Vector2(-c + 1.5f, -c), new Vector2(c - 1.5f, -c));
            n += Run(ring, new Vector2(-c + 1.5f, c), new Vector2(c - 1.5f, c));
            n += Run(ring, new Vector2(-c, -c + 1.5f), new Vector2(-c, c - 1.5f));
            n += Run(ring, new Vector2(c, -c + 1.5f), new Vector2(c, c - 1.5f));
            return n;
        }

        /// <summary>DEC-102 AD E (C-00 25.09): single-leaf side doors in the ring tsuiji by the NE and SE corners, where the corner spawn
        /// gates (NE_Corner, SE_Shore) sit — the spawns read as coming through the wall, not a gate motif: on both runs of each corner,
        /// <see cref="SideDoorOffset"/> m from the corner, <see cref="SideDoorWidth"/> × <see cref="SideDoorHeight"/> m under the continuous
        /// cap, a dark void (the flat cloth, L ≤ 25) in a timber frame on the inner face. Visual only: no collider, out of the NavMesh bake.</summary>
        public const float SideDoorOffset = 6f, SideDoorWidth = 1.4f, SideDoorHeight = 2.0f, SideDoorFrame = 0.14f;
        public const string SideDoorGroupName = "SideDoors";

        /// <summary>The door centres on the tsuiji's inner face (x, z) and their inward normals: NE north run, NE east run, SE south run, SE east run.</summary>
        public static (Vector2 at, Vector2 inward)[] SideDoorSpots()
        {
            float face = RingCentreline - 0.30f, along = RingCentreline - SideDoorOffset;
            return new[]
            {
                (new Vector2(along, face), new Vector2(0f, -1f)), (new Vector2(face, along), new Vector2(-1f, 0f)),
                (new Vector2(along, -face), new Vector2(0f, 1f)), (new Vector2(face, -along), new Vector2(-1f, 0f)),
            };
        }

        private static void SideDoors(Transform dress)
        {
            var group = new GameObject(SideDoorGroupName) { layer = PrototypeLayers.Environment }.transform;
            group.SetParent(dress, false);
            group.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            foreach ((Vector2 at, Vector2 inward) in SideDoorSpots())
            {
                Quaternion q = Quaternion.LookRotation(new Vector3(inward.x, 0f, inward.y), Vector3.up);   // local +z = into the court
                Vector3 c = new Vector3(at.x, 0f, at.y), t = q * Vector3.right, n = q * Vector3.forward;
                float w = SideDoorWidth, h = SideDoorHeight, f = SideDoorFrame;
                DoorPiece(group, "SideDoor_Void", "cloth", c + n * 0.02f + Vector3.up * (h * 0.5f), q, new Vector3(w, h, 0.04f));
                foreach (float side in new[] { -1f, 1f })
                    DoorPiece(group, "SideDoor_Frame", "wood_timber", c + n * 0.05f + t * (side * (w + f) * 0.5f) + Vector3.up * ((h + f) * 0.5f), q, new Vector3(f, h + f, 0.10f));
                DoorPiece(group, "SideDoor_Frame", "wood_timber", c + n * 0.05f + Vector3.up * (h + f * 0.5f), q, new Vector3(w + 2f * f, f, 0.10f));
            }
        }

        private static void DoorPiece(Transform parent, string name, string role, Vector3 centre, Quaternion rotation, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.layer = PrototypeLayers.Environment;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, rotation);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = BoxMaterial(name, role, Vector2.one);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static int Forecourt(Transform dress)
        {
            // Court_W / Court_E (x ±26, z 20…36, 4 m gap at z 28…32) and the south walls z 20 up to the chūmon (x ±4); corners at (±26, 20).
            var court = new GameObject("Forecourt").transform;
            court.SetParent(dress, false);
            int n = 0;
            Place(court, "wall_kit", 3, Mz(-26f, 20f, 0f, 0f), Vector3.zero, "structure");
            Place(court, "wall_kit", 3, Mz(26f, 20f, 0f, 90f), Vector3.zero, "structure");
            n += 2;
            foreach (float x in new[] { -26f, 26f })
            {
                // End caps put the body's end on the gap edge / the open end (cap pivot = joint, body 0.30 past it).
                n += Run(court, new Vector2(x, 21.5f), new Vector2(x, 27.7f));
                n += Cap(court, new Vector2(x, 27.7f), new Vector2(0f, 1f));
                n += Cap(court, new Vector2(x, 32.3f), new Vector2(0f, -1f));
                n += Run(court, new Vector2(x, 32.3f), new Vector2(x, 35.7f));
                n += Cap(court, new Vector2(x, 35.7f), new Vector2(0f, 1f));
            }

            // B5b-4 / v007-B: the walls stay on their line; they end ChumonSidePassage short of the chūmon's side walls (x ±ChumonHalfWidth)
            // — its side passages, the greybox R6's 2 m E / W doors.
            float end = ChumonHalfWidth + ChumonSidePassage;
            n += Run(court, new Vector2(-24.5f, 20f), new Vector2(-end, 20f));
            n += Cap(court, new Vector2(-end, 20f), new Vector2(1f, 0f));
            n += Run(court, new Vector2(24.5f, 20f), new Vector2(end, 20f));
            n += Cap(court, new Vector2(end, 20f), new Vector2(-1f, 0f));
            return n;
        }

        /// <summary>A straight tsuiji run a→b from 4 m / 3 m modules: the mix whose length is nearest (log scale), each stretched to fit exactly.</summary>
        private static int Run(Transform parent, Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            int best4 = 0, best3 = 0;
            float bestErr = float.MaxValue;
            for (int n4 = 0; n4 <= Mathf.CeilToInt(len / 4f) + 1; n4++)
            for (int n3 = 0; n3 <= 2; n3++)
            {
                float sum = 4f * n4 + 3f * n3;
                if (sum <= 0f) continue;
                float err = Mathf.Abs(Mathf.Log(len / sum));
                if (err < bestErr - 1e-5f) { bestErr = err; best4 = n4; best3 = n3; }
            }

            float scale = len / (4f * best4 + 3f * best3);
            Vector2 d = (b - a) / len;
            float deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, at = 0f;
            int count = 0;
            // 3 m modules at the run's ends (symmetric), 4 m in the middle.
            var modules = new List<float>();
            for (int i = 0; i < best3; i++) modules.Add(3f);
            for (int i = 0; i < best4; i++) modules.Insert(modules.Count / 2, 4f);
            foreach (float m in modules)
            {
                float w = m * scale;
                Vector2 p = a + d * (at + w * 0.5f);
                Matrix4x4 mat = Mz(p.x, p.y, 0f, deg) * Matrix4x4.Scale(new Vector3(scale, 1f, 1f));
                Place(parent, "wall_kit", m == 4f ? 2 : 1, mat, Vector3.zero, "structure");
                at += w;
                count++;
            }

            return count;
        }

        /// <summary>End cap at a joint; <paramref name="outward"/> points from the run to where the wall stops (the cap's +x).</summary>
        private static int Cap(Transform parent, Vector2 joint, Vector2 outward)
        {
            Place(parent, "wall_kit", 6, Mz(joint.x, joint.y, 0f, Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg), Vector3.zero, "structure");
            return 1;
        }

        private static void Karamon(Transform dress)
        {
            // Posts x ±3.225 (6.0 m clear) on the south axis at z −16 — the greybox karamon's own footprint.
            GameObject gate = Place(dress, "gates", 1, Mz(0f, -16f, 0f, 0f), Vector3.zero, "structure");
            Transform roof = gate.transform.Find(ArtLibraryPrefabBuilder.RoofName);
            if (roof != null) RoofOccluder(gate.transform, roof);
        }

        private static void Lanterns(Transform dress)
        {
            // The axis lanterns (greybox 0.6 × 2.1 boxes at x ±3, z 14 / 8 / −14.5) as kasuga tōrō, the two variations alternating.
            var group = new GameObject("AxisLanterns").transform;
            group.SetParent(dress, false);
            int i = 0;
            foreach (float z in new[] { 14f, 8f, -14.8f })   // B5b-2: the south pair 0.3 m further out, clear of the plaza kerb
            foreach (float x in new[] { -3f, 3f })
                Place(group, "lantern_kasuga", 1 + (i++ % 2), Mz(x, z, 0f, x < 0f ? -90f : 90f), Vector3.zero, "structure");
        }

        private static void Planters(Transform dress)
        {
            // v005 raised planters (AP023_planter V07, 3.06 m square, 0.6 m collider box — the greybox planter's obstacle) with a pine
            // island on the moss fill: a black pine, satsuki and groundcover (the HF-center court's planted beds).
            var group = new GameObject("CentrePlanting").transform;
            group.SetParent(dress, false);
            var rng = new System.Random(742);
            const float top = 0.6f;
            for (int i = 0; i < PlanterCentres.Length; i++)
            {
                Vector2 c = PlanterCentres[i];
                Place(group, "planter", 7, Mz(c.x, c.y, 0f, 90f * i), Vector3.zero, "structure");
                Place(group, "black_pine", i % 2 == 0 ? 2 : 3, Mz(c.x + 0.2f, c.y - 0.1f, top, (float)(rng.NextDouble() * 360.0)), Vector3.zero, "vegetation");
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI * 0.5f + (float)rng.NextDouble() * 0.6f, r = 0.8f + (float)rng.NextDouble() * 0.3f;
                    Place(group, k % 2 == 0 ? "satsuki" : "groundcover", k % 2 == 0 ? 2 : 1 + k / 2, Mz(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, top, (float)(rng.NextDouble() * 360.0)),
                        Vector3.zero, "dressing");
                }
            }
        }

        /// <summary>v005 corner yagura just outside the ring corners, at 1.25× (AD B5b SHOULD-3: they hold the corners): ±60.2 puts the 9.5 m tower's inner face behind the tsuiji, facing in.</summary>
        private static void Yagura(Transform dress)
        {
            // The modifier's own layer must be in the bake mask (NavMeshSurface.CollectSources skips it otherwise).
            var group = new GameObject("CornerYagura") { layer = PrototypeLayers.Environment }.transform;
            group.SetParent(dress, false);
            const float c = 60.2f, scale = 1.25f;
            // Vista only: out of the bake (their floors / roofs sit inside the NavMesh volume's ±58 m and would grow islands).
            group.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            foreach ((float x, float y, float deg) in new[] { (-c, -c, 0f), (c, -c, 90f), (c, c, 180f), (-c, c, 270f) })
                Place(group, "yagura", 1, Mz(x, y, 0f, deg) * Matrix4x4.Scale(Vector3.one * scale), Vector3.zero, "structure");
        }

        /// <summary>
        /// B5b-3 · The vista (55–85 m, walk-free) in place of the greybox boxes: a jittered 3.5 m grid of trees (a closed canopy, HF-center) in the square band
        /// <see cref="VistaInner"/>…<see cref="VistaOuter"/> — sugi (cheap LODs, 12–16 m: the treeline over the tsuiji) with black
        /// pine accents west / south and sakura / momiji east (the garden side) — and one distant keep. Out of the bake (layer
        /// Environment so the modifier counts); clear of the corner yagura and the keep.
        /// </summary>
        private static void Vista(Transform dress)
        {
            var group = new GameObject(VistaGroupName) { layer = PrototypeLayers.Environment }.transform;
            group.SetParent(dress, false);
            group.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var rng = new System.Random(744);
            for (float x = -VistaOuter; x <= VistaOuter; x += VistaSpacing)
            for (float z = -VistaOuter; z <= VistaOuter; z += VistaSpacing)
            {
                var p = new Vector2(x + (float)(rng.NextDouble() - 0.5) * VistaSpacing * 0.8f, z + (float)(rng.NextDouble() - 0.5) * VistaSpacing * 0.8f);
                double pick = rng.NextDouble(), yaw = rng.NextDouble() * 360.0, s = 0.85 + rng.NextDouble() * 0.35;
                float band = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                if (band < VistaInner || band > VistaOuter) continue;
                if (Vector2.Distance(new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)), new Vector2(60.2f, 60.2f)) < 8f) continue;   // corner yagura
                if (Vector2.Distance(p, VistaKeep) < 11f) continue;
                (string family, int v) = VistaSpecies(p, pick);
                Place(group, family, v, Mz(p.x, p.y, 0f, (float)yaw) * Matrix4x4.Scale(Vector3.one * (float)s), Vector3.zero, "vegetation");
            }

            Place(group, "yagura", 1, Mz(VistaKeep.x, VistaKeep.y, 0f, 30f) * Matrix4x4.Scale(Vector3.one * 1.8f), Vector3.zero, "structure");
        }

        /// <summary>Species by side: east (x > 40) 12 % sakura / momiji, west and south 10 % black pine, the rest sugi (V01 / V02 / the
        /// young V03 — v007: 50 / 32 / 18 %, a second canopy height). v007 adds sakura V04, momiji V02 / V03 and black pine V05.</summary>
        public static (string family, int variation) VistaSpecies(Vector2 p, double pick)
        {
            if (p.x > 40f && pick < 0.08) return ("sakura_v2", pick < 0.04 ? 2 : 4);
            if (p.x > 40f && pick < 0.12) return ("momiji", pick < 0.10 ? 2 : 3);
            if ((p.x < -40f || p.y < -40f) && pick < 0.10) return ("black_pine", pick < 0.05 ? 4 : 5);
            return ("sugi", pick < 0.60 ? 1 : pick < 0.82 ? 2 : 3);
        }

        private static void Kairo(Transform dress)
        {
            var group = new GameObject(KairoGroupName).transform;
            group.SetParent(dress, false);
            foreach ((string id, Vector2[] path, bool closed) in KitKairo)
            {
                int index = 0;
                for (int v = 1; v + 1 < path.Length; v++) AngleCorner(group, id, v, path[v - 1], path[v], path[v + 1]);
                for (int seg = 0; seg + 1 < path.Length; seg++)
                {
                    // v007-B: an interior vertex is a kairo_angle corner — its arms take KairoAngleArm of each segment it joins.
                    Vector2 d = (path[seg + 1] - path[seg]).normalized;
                    Vector2 segA = path[seg] + d * (seg > 0 ? KairoAngleArm : 0f), segB = path[seg + 1] - d * (seg + 2 < path.Length ? KairoAngleArm : 0f);
                    float deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    foreach ((float from, float to, bool isDoor) in KairoRuns(id, segA, segB))
                    {
                    if (isDoor)
                    {
                        // WP-092: the kit door, its −Y side (the opening) toward the door's side of the corridor.
                        Vector2 at = segA + d * ((from + to) * 0.5f);
                        char doorSide = 'W';
                        foreach ((string corridor, Vector2 p, char side) in KairoDoorPieces) if (corridor == id && Vector2.Distance(p, at) < 0.5f) doorSide = side;
                        Vector2 want = doorSide == 'W' ? Vector2.left : doorSide == 'E' ? Vector2.right : doorSide == 'N' ? Vector2.up : Vector2.down;
                        float a0 = deg * Mathf.Deg2Rad;
                        float doorYaw = Vector2.Dot(new Vector2(Mathf.Sin(a0), -Mathf.Cos(a0)), want) > 0f ? deg : deg + 180f;   // kit −Y = (sin, −cos)
                        GameObject door = Place(group, KairoDoorFamily, 1, Mz(at.x, at.y, 0f, doorYaw), Vector3.zero, "structure");
                        door.name = id + "_Door_" + (index++).ToString("00");
                        LintelPass(door);
                        Transform doorRoof = door.transform.Find(ArtLibraryPrefabBuilder.RoofName);
                        if (doorRoof != null) RoofOccluder(door.transform, doorRoof);
                        continue;
                    }

                    Vector2 a = segA + d * from, b = segA + d * to;
                    float len = to - from;
                    if (len < MinBayLength) continue;   // the corners' arms meet: no straight bay between them
                    int n = Mathf.Max(1, Mathf.RoundToInt(len / KairoBay));
                    float s = len / (n * KairoBay);
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 c = a + d * ((i + 0.5f) * len / n);
                        int variation = 1;
                        float yaw = deg;
                        if (closed)
                        {
                            // Closed bays (V02) except where a K1 door opens a side: V03 (wall −Y, open +Y) faces its open side at the door.
                            variation = 2;
                            float half = len / n * 0.5f;
                            foreach ((char side, float z, float width) in GreyboxV6Builder.K1Doors)
                            {
                                float overlap = Mathf.Min(c.y + half, z + width * 0.5f) - Mathf.Max(c.y - half, z - width * 0.5f);
                                if (overlap < 1f) continue;
                                variation = 3;
                                yaw = side == 'W' ? deg : deg + 180f;   // +Y is west at 90°, east at 270°
                            }
                        }

                        // v007: the open bays are P-05's rail-less kairo_open (C-00 decision (b)); the closed / door bays stay kairo_bay V02 / V03.
                        bool open = variation == 1;
                        GameObject bay = Place(group, open ? OpenBayFamily : "kairo_bay", variation, Mz(c.x, c.y, 0f, yaw) * Matrix4x4.Scale(new Vector3(s, 1f, 1f)), Vector3.zero, "structure");
                        bay.name = id + "_Bay_" + (index++).ToString("00");
                        if (!open) RailsOff(bay);
                        Transform roof = bay.transform.Find(ArtLibraryPrefabBuilder.RoofName);
                        if (roof != null) RoofOccluder(bay.transform, roof);
                    }
                    }
                }
            }
        }

        /// <summary>v007-B · AP-023 kairo_angle's arm: the joint plane sits this far from the corner vertex along each corridor.</summary>
        public const float KairoAngleArm = 3.6f, MinBayLength = 0.3f;

        /// <summary>The kit corner piece for the turn in → at → out: V01 / V02 / V03 = 30 / 45 / 60°. Its arm 1 runs along local −X, arm 2
        /// along (cos t, sin t) — a left (counter-clockwise, x → z) turn; a right turn is the same piece walked backwards (its +X along
        /// the reversed outgoing leg).</summary>
        private static void AngleCorner(Transform group, string id, int vertex, Vector2 from, Vector2 at, Vector2 to)
        {
            Vector2 a = (at - from).normalized, b = (to - at).normalized;
            float turn = Vector2.SignedAngle(a, b);
            int variation = Mathf.Abs(Mathf.Abs(turn) - 30f) < 0.5f ? 1 : Mathf.Abs(Mathf.Abs(turn) - 45f) < 0.5f ? 2 : Mathf.Abs(Mathf.Abs(turn) - 60f) < 0.5f ? 3 : 0;
            if (variation == 0) throw new InvalidOperationException(id + " corner " + vertex + ": " + turn.ToString("0.0") + "° — the kit turns 30 / 45 / 60° only");
            Vector2 forward = turn > 0f ? a : -b;
            GameObject corner = Place(group, "kairo_angle", variation, Mz(at.x, at.y, 0f, Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg), Vector3.zero, "structure");
            corner.name = id + "_Corner_" + vertex.ToString("00");
            RailsOff(corner);
            Transform roof = corner.transform.Find(ArtLibraryPrefabBuilder.RoofName);
            if (roof != null) RoofOccluder(corner.transform, roof);
        }

        /// <summary>The bay's koran off: the rail part's renderers and its colliders (shells 0.25 → 1.1–1.5 m; posts reach 3.65, sills 0.1).</summary>
        public static void RailsOff(GameObject bay)
        {
            Physics.SyncTransforms();
            float y = bay.transform.position.y;
            foreach (Transform t in bay.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("rail_LOD", StringComparison.Ordinal)) t.gameObject.SetActive(false);
            foreach (Collider c in bay.GetComponentsInChildren<Collider>(true))
            {
                Bounds b = c.bounds;
                if (b.min.y - y > 0.2f && b.max.y - y > 0.9f && b.max.y - y < 1.5f) c.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// B5b-5 · R2–R5 as the kit's buildings, which P-05 made to the greybox rooms: the pavilion (openings W / S / E on R2's doors), the
        /// kura (10 × 8, doors E / W), the tea house (raised 0.45, entries N / E) and the azumaya (open). Their roofs fade top-down.
        /// </summary>
        public static readonly (string id, string family, Vector2 at, float yaw)[] KitBuildings =
        {
            ("R2_EastPavilion", "pavilion", new Vector2(35f, 28f), 0f),
            ("R3_Kura", "kura", new Vector2(-39f, 40f), 0f),
            ("R4_TeaHouse", "teahouse", new Vector2(-32f, -36f), 0f),
            ("R5_Azumaya", "azumaya", new Vector2(33f, -33f), 0f),
        };

        public const string BuildingGroupName = "Buildings", GardenGroupName = "GardenTrees";

        private static void Buildings(Transform dress)
        {
            var group = new GameObject(BuildingGroupName).transform;
            group.SetParent(dress, false);
            foreach ((string id, string family, Vector2 at, float yaw) in KitBuildings)
            {
                GameObject b = Place(group, family, 1, Mz(at.x, at.y, 0f, yaw), Vector3.zero, "structure");
                b.name = id;
                Transform roof = b.transform.Find(ArtLibraryPrefabBuilder.RoofName);
                if (roof != null) RoofOccluder(b.transform, roof);
            }
        }

        /// <summary>The greybox garden's trunks (Tree_*: sakura / maple garden, the NE pines) and the R7 court's six-trunk island.</summary>
        private static List<(Vector3 position, float radius, bool greatSakura)> GardenSpots(Transform greybox)
        {
            var spots = new List<(Vector3, float, bool)>();
            bool sakura = false;
            foreach (Transform t in Children(greybox))
            {
                if (t.name.StartsWith("Tree_", StringComparison.Ordinal)) spots.Add((t.position, t.localScale.x * 0.5f, false));
                else if (t.name.StartsWith("R7_Sakura_", StringComparison.Ordinal) && !sakura) { spots.Add((new Vector3(36f, 0f, 1f), 2.6f, true)); sakura = true; }
            }

            return spots;
        }

        /// <summary>WP-074 B9: offsets that give the garden trees' yaw and scale their own hash channels (see <see cref="GroveHash01"/>).</summary>
        private static readonly Vector2 GardenYawSalt = new Vector2(17.31f, 5.13f), GardenScaleSalt = new Vector2(-8.71f, 31.97f);

        /// <summary>WP-074 B9 (Y7): one garden tree as the AD-accepted world shipped it — kit variation and Unity transform.</summary>
        public readonly struct GardenPin
        {
            public readonly string Family;
            public readonly int Variation;
            public readonly Vector3 Position, Scale;
            public readonly Quaternion Rotation;

            public GardenPin(string family, int variation, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Family = family;
                Variation = variation;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }

        /// <summary>
        /// WP-074 B9 (Y7, C-00 30.09 decision B): the garden as the user played it and the AD accepted it (B2 / B5; the 35 trees of
        /// main 68317433) — the one source. A spot within 1 cm of a pin keeps that tree exactly; only a spot with no pin (a moved spawn
        /// gate opened it) draws its tree from the position hash, so a later gate move changes only the spots it opens or closes.
        /// </summary>
        public static readonly GardenPin[] GardenPins =
        {
            new GardenPin("sakura_v2", 4, new Vector3(27.27307f, 0.0f, -13.808204f), new Quaternion(-0.0f, 0.98053366f, -0.0f, 0.19635093f), new Vector3(1.0531878f, 1.0531877f, 1.0531878f)),
            new GardenPin("black_pine", 1, new Vector3(27.427578f, 0.0f, 40.16469f), new Quaternion(-0.0f, -0.0937249f, -0.0f, 0.99559814f), new Vector3(1.1237115f, 1.1237115f, 1.1237115f)),
            new GardenPin("momiji", 3, new Vector3(27.555439f, 0.0f, 19.062237f), new Quaternion(-0.0f, -0.4711185f, -0.0f, 0.88206995f), new Vector3(0.9248941f, 0.9248941f, 0.9248941f)),
            new GardenPin("black_pine", 5, new Vector3(28.619577f, 0.0f, 35.344456f), new Quaternion(-0.0f, -0.18641677f, -0.0f, 0.98247075f), new Vector3(1.0152978f, 1.0152978f, 1.0152978f)),
            new GardenPin("momiji", 1, new Vector3(31.789766f, 0.0f, 15.800505f), new Quaternion(-0.0f, 0.3071197f, -0.0f, 0.9516709f), new Vector3(0.97382635f, 0.97382635f, 0.97382635f)),
            new GardenPin("momiji", 2, new Vector3(32.669563f, 0.0f, -24.79141f), new Quaternion(-0.0f, 0.63201225f, -0.0f, 0.7749584f), new Vector3(1.1013647f, 1.1013647f, 1.1013647f)),
            new GardenPin("black_pine", 1, new Vector3(33.514217f, 0.0f, 39.920044f), new Quaternion(-0.0f, -0.85345566f, -0.0f, 0.52116543f), new Vector3(0.9185012f, 0.91850126f, 0.9185012f)),
            new GardenPin("black_pine", 5, new Vector3(33.67609f, 0.0f, 35.17861f), new Quaternion(-0.0f, 0.8133765f, -0.0f, 0.58173764f), new Vector3(1.0147291f, 1.0147291f, 1.0147291f)),
            new GardenPin("sakura_v2", 1, new Vector3(36.0f, 0.0f, 1.0f), new Quaternion(-0.0f, -0.9638874f, -0.0f, 0.26631004f), new Vector3(0.99999994f, 1.0f, 0.99999994f)),
            new GardenPin("momiji", 1, new Vector3(38.157207f, 0.0f, -21.008404f), new Quaternion(-0.0f, 0.6355531f, -0.0f, 0.77205724f), new Vector3(0.9124465f, 0.9124465f, 0.9124465f)),
            new GardenPin("sakura_v2", 2, new Vector3(38.901318f, 0.0f, 13.792571f), new Quaternion(-0.0f, -0.30190903f, -0.0f, 0.9533367f), new Vector3(1.1314535f, 1.1314535f, 1.1314535f)),
            new GardenPin("sakura_v2", 3, new Vector3(38.913963f, 0.0f, -24.52365f), new Quaternion(-0.0f, 0.82458323f, -0.0f, 0.56574064f), new Vector3(1.1198817f, 1.1198817f, 1.1198817f)),
            new GardenPin("momiji", 3, new Vector3(41.941948f, 0.0f, 29.4557f), new Quaternion(-0.0f, 0.8314763f, -0.0f, 0.5555603f), new Vector3(1.1409254f, 1.1409254f, 1.1409254f)),
            new GardenPin("sakura_v2", 3, new Vector3(43.028095f, 0.0f, -15.19005f), new Quaternion(-0.0f, 0.9680875f, -0.0f, 0.25061265f), new Vector3(1.0744752f, 1.0744753f, 1.0744752f)),
            new GardenPin("momiji", 3, new Vector3(43.344933f, 0.0f, -18.951832f), new Quaternion(-0.0f, -0.98145884f, -0.0f, 0.19167306f), new Vector3(1.073683f, 1.0736829f, 1.073683f)),
            new GardenPin("black_pine", 4, new Vector3(43.597748f, 0.0f, 34.47863f), new Quaternion(-0.0f, -0.16941893f, -0.0f, 0.98554415f), new Vector3(0.96612936f, 0.96612936f, 0.96612936f)),
            new GardenPin("momiji", 3, new Vector3(44.060856f, 0.0f, 16.115232f), new Quaternion(-0.0f, 0.58255124f, -0.0f, 0.81279397f), new Vector3(0.9320751f, 0.9320751f, 0.9320751f)),
            new GardenPin("black_pine", 1, new Vector3(46.985085f, 0.0f, 39.34443f), new Quaternion(-0.0f, -0.91385853f, -0.0f, 0.40603277f), new Vector3(1.0595003f, 1.0595003f, 1.0595003f)),
            new GardenPin("black_pine", 4, new Vector3(47.20042f, 0.0f, 34.29946f), new Quaternion(-0.0f, 0.47435725f, -0.0f, 0.88033247f), new Vector3(1.1238931f, 1.1238931f, 1.1238931f)),
            new GardenPin("sakura_v2", 2, new Vector3(47.873093f, 0.0f, -4.485217f), new Quaternion(-0.0f, 0.66429913f, -0.0f, 0.74746686f), new Vector3(1.0161849f, 1.0161849f, 1.0161849f)),
            new GardenPin("sakura_v2", 3, new Vector3(47.95798f, 0.0f, -39.93435f), new Quaternion(-0.0f, -0.8256846f, -0.0f, 0.56413203f), new Vector3(1.0283902f, 1.0283902f, 1.0283902f)),
            new GardenPin("sakura_v2", 4, new Vector3(48.01479f, 0.0f, 8.899051f), new Quaternion(-0.0f, -0.44784215f, -0.0f, 0.89411265f), new Vector3(1.1330634f, 1.1330634f, 1.1330634f)),
            new GardenPin("sakura_v2", 2, new Vector3(48.115868f, 0.0f, -30.299335f), new Quaternion(-0.0f, -0.9626397f, -0.0f, 0.27078566f), new Vector3(1.1178902f, 1.1178901f, 1.1178902f)),
            new GardenPin("momiji", 3, new Vector3(48.439476f, 0.0f, 26.218359f), new Quaternion(-0.0f, 0.38596f, -0.0f, 0.9225155f), new Vector3(0.9859051f, 0.9859051f, 0.9859051f)),
            new GardenPin("sakura_v2", 2, new Vector3(48.744434f, 0.0f, 29.639233f), new Quaternion(-0.0f, -0.7195312f, -0.0f, 0.69446015f), new Vector3(0.9800008f, 0.98000073f, 0.9800008f)),
            new GardenPin("sakura_v2", 3, new Vector3(48.91532f, 0.0f, -34.17147f), new Quaternion(-0.0f, 0.42877305f, -0.0f, 0.9034122f), new Vector3(1.1163206f, 1.1163206f, 1.1163206f)),
            new GardenPin("sakura_v2", 3, new Vector3(49.091724f, 0.0f, 5.647511f), new Quaternion(-0.0f, -0.360927f, -0.0f, 0.9325941f), new Vector3(0.9697288f, 0.9697289f, 0.9697288f)),
            new GardenPin("sakura_v2", 2, new Vector3(51.854298f, 0.0f, -34.71484f), new Quaternion(-0.0f, 0.7417904f, -0.0f, 0.67063177f), new Vector3(1.0593051f, 1.0593052f, 1.0593051f)),
            new GardenPin("sakura_v2", 4, new Vector3(52.11861f, 0.0f, -5.322835f), new Quaternion(-0.0f, 0.21244577f, -0.0f, 0.9771729f), new Vector3(0.9596694f, 0.9596695f, 0.9596694f)),
            new GardenPin("momiji", 1, new Vector3(52.160057f, 0.0f, -29.49861f), new Quaternion(-0.0f, -0.80928636f, -0.0f, 0.5874143f), new Vector3(1.0178903f, 1.0178903f, 1.0178903f)),
            new GardenPin("momiji", 2, new Vector3(52.261646f, 0.0f, 9.808929f), new Quaternion(-0.0f, -0.8011191f, -0.0f, 0.59850496f), new Vector3(1.1029754f, 1.1029755f, 1.1029754f)),
            new GardenPin("sakura_v2", 3, new Vector3(52.529053f, 0.0f, 20.244831f), new Quaternion(-0.0f, 0.88890237f, -0.0f, 0.4580968f), new Vector3(1.0711288f, 1.0711287f, 1.0711288f)),
            new GardenPin("momiji", 2, new Vector3(52.571136f, 0.0f, 29.223837f), new Quaternion(-0.0f, 0.9985348f, -0.0f, 0.054113366f), new Vector3(1.0349222f, 1.0349222f, 1.0349222f)),
            new GardenPin("sakura_v2", 2, new Vector3(52.636906f, 0.0f, 5.1773553f), new Quaternion(-0.0f, 0.5439856f, -0.0f, 0.8390945f), new Vector3(0.90647304f, 0.90647304f, 0.90647304f)),
            new GardenPin("black_pine", 1, new Vector3(52.891045f, 0.0f, 39.806545f), new Quaternion(-0.0f, 0.9977127f, -0.0f, 0.06759773f), new Vector3(0.9450798f, 0.9450797f, 0.9450798f)),
        };

        /// <summary>
        /// What the builder puts on a garden spot (<see cref="GardenTrees"/> and the parity test read this one rule): the pinned tree, or
        /// for an unpinned spot the position hash — the NE corner (z &gt; 34) black pine, the rest sakura (60 %) and momiji; the R7
        /// island's great sakura V01 at scale 1.
        /// </summary>
        public static (string family, int variation, Vector3 position, Quaternion rotation, Vector3 scale) GardenTreeAt(Vector3 spot, bool greatSakura)
        {
            var c = new Vector2(spot.x, spot.z);
            int pin = Array.FindIndex(GardenPins, g => Vector2.Distance(new Vector2(g.Position.x, g.Position.z), c) < 0.01f);
            if (pin >= 0)
            {
                GardenPin g = GardenPins[pin];
                return (g.Family, g.Variation, g.Position, g.Rotation, g.Scale);
            }

            double pick = GroveHash01(c), yaw = GroveHash01(c + GardenYawSalt) * 360.0, s = 0.9 + GroveHash01(c + GardenScaleSalt) * 0.25;
            // v007: + black pine V05, sakura V04, momiji V02 (7 m) / V03 (3 m)
            (string family, int v) = greatSakura ? ("sakura_v2", 1) : spot.z > 34f ? ("black_pine", pick < 0.34 ? 1 : pick < 0.67 ? 4 : 5)
                : pick < 0.6 ? ("sakura_v2", pick < 0.2 ? 2 : pick < 0.4 ? 3 : 4) : ("momiji", pick < 0.73 ? 1 : pick < 0.87 ? 2 : 3);
            return (family, v, WorldPlacementAsset.ToUnity(spot.x, spot.z, 0f), WorldPlacementAsset.ToUnityYaw((float)yaw), Vector3.one * (greatSakura ? 1f : (float)s));
        }

        /// <summary>
        /// B5b-5 · The garden trunks as v005 trees: the NE corner (z &gt; 34) black pine, the rest sakura (60 %) and momiji; one great
        /// sakura (V01, 8.9 m crown) on the R7 island. Trunk colliders replace the greybox cylinders.
        /// </summary>
        private static void GardenTrees(Transform dress, List<(Vector3 position, float radius, bool greatSakura)> spots)
        {
            var group = new GameObject(GardenGroupName).transform;
            group.SetParent(dress, false);
            foreach ((Vector3 p, float radius, bool great) in spots)
            {
                // WP-074 B9 (Y7): the pinned tree or the position hash (GardenTreeAt) — never a sequential rng, so adding or clearing
                // one spot (a moved spawn gate) leaves every other tree as it was and the scene stays the builder's output.
                (string family, int variation, Vector3 position, Quaternion rotation, Vector3 scale) t = GardenTreeAt(p, great);
                GameObject tree = Place(group, t.family, t.variation, Mz(p.x, p.z, 0f, 0f), Vector3.zero, "vegetation");
                tree.transform.SetPositionAndRotation(t.position, t.rotation);
                tree.transform.localScale = t.scale;
            }
        }

        // ---------------------------------------------------------------- B5b-5 (2): ground, water, bridges, grove, fences, rock garden

        public const string WaterMaterialPath = "Assets/_Art/Materials/M_water_v2.mat";
        public const string ShoreGroupName = "PondShores", BridgeGroupName = "Bridges", GroveGroupName = "BambooGrove", FenceGroupName = "Fences", KaresansuiGroupName = "Karesansui";
        /// <summary>The grove's culm clumps: V01–V04 and v007's V06 (V05 is a 0.6 m shoot, not a clump).</summary>
        public static readonly int[] GroveVariations = { 1, 2, 3, 4, 6 };
        public const float GroveEdgeBand = 5f, GroveEdgeGap = 0.55f, GroveInnerGap = 0.25f;

        /// <summary>0..1 from a grove cell position (deterministic, independent of the dress rng).</summary>
        public static float GroveHash01(Vector2 c)
        {
            float h = Mathf.Sin(c.x * 12.9898f + c.y * 78.233f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        private static void Ground(Transform greybox)
        {
            // The greybox ground (170 m box) on DD/GroundMoss (WP-075 C7b: graded kit moss, tone field, soil / gravel / leaf patches, path
            // bands; WorldV5Moss); the ponds on the AP-015 water v2 (world-space: fits the discs).
            WorldV5Moss.Apply(greybox);
            var water = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (water == null) throw new InvalidOperationException("water v2 material missing: " + WaterMaterialPath);
            foreach (Transform t in Children(greybox))
                if (t.name.StartsWith("Pond", StringComparison.Ordinal) && t.name.EndsWith("_Water", StringComparison.Ordinal)) t.GetComponent<MeshRenderer>().sharedMaterial = water;
        }

        /// <summary>Moss / pebble shore pieces round each pond ellipse every ~3 m (tangent-aligned), boulders and reed clumps between — visual, no colliders (the rims are the NavMesh boundary).</summary>
        private static void Ponds(Transform dress, Transform greybox)
        {
            var group = new GameObject(ShoreGroupName) { layer = PrototypeLayers.Environment }.transform;
            group.SetParent(dress, false);
            group.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var rng = new System.Random(746);
            Transform falls = null;   // v007-B: the waterfall goes on the largest pond's north rim
            foreach (Transform w in Children(greybox))
                if (w.name.StartsWith("Pond", StringComparison.Ordinal) && w.name.EndsWith("_Water", StringComparison.Ordinal)
                    && (falls == null || w.localScale.x * w.localScale.z > falls.localScale.x * falls.localScale.z)) falls = w;
            Vector3 fallsAt = falls != null ? RimPoint(falls, NorthRimAngle(falls)) : Vector3.positiveInfinity;
            foreach (Transform w in Children(greybox))
            {
                if (!w.name.StartsWith("Pond", StringComparison.Ordinal) || !w.name.EndsWith("_Water", StringComparison.Ordinal)) continue;
                float perimeter = Mathf.PI * (w.localScale.x + w.localScale.z) * 0.5f;
                int n = Mathf.Max(8, Mathf.RoundToInt(perimeter / 3f));
                for (int i = 0; i < n; i++)
                {
                    float t = i * Mathf.PI * 2f / n;
                    Vector3 p = w.TransformPoint(new Vector3(Mathf.Cos(t) * 0.5f, 0f, Mathf.Sin(t) * 0.5f));
                    Vector3 q = w.TransformPoint(new Vector3(Mathf.Cos(t + 0.01f) * 0.5f, 0f, Mathf.Sin(t + 0.01f) * 0.5f));
                    float deg = Mathf.Atan2(q.z - p.z, q.x - p.x) * Mathf.Rad2Deg;
                    double pick = rng.NextDouble();
                    int shoreVariation = 1 + rng.Next(3);
                    // v007-B: the falls' own curved shore replaces the strips near it (Waterfall); the rng draws stay, so the rest is as before.
                    // N3 (menu-n2-n4 RAPOR §N3 a): the clearing is the curved pieces' own reach (WaterfallShoreOffset) — at 9 m it took
                    // every strip of Pond0 (its rim is ≤ 7.8 m from the north point); a piece whose point falls on a crossing deck stays out.
                    bool keep = (w != falls || Vector3.Distance(p, fallsAt) >= WaterfallClearing)
                                && !GreyboxV6Builder.InPondStripExclusion(new Vector2(p.x, p.z));
                    if (keep) Visual(Place(group, pick < 0.6 ? "pond_shore_moss" : "pond_shore_pebble", shoreVariation, Mz(p.x, p.z, 0f, deg), Vector3.zero, "dressing"));
                    if (pick < 0.25)
                    {
                        int rockVariation = 3 + rng.Next(3);
                        float rockYaw = (float)(rng.NextDouble() * 360.0);
                        if (keep) Visual(Place(group, "pond_rock", rockVariation, Mz(p.x, p.z, 0f, rockYaw), Vector3.zero, "dressing"));
                    }

                    if (pick > 0.8)
                    {
                        Vector3 r = w.TransformPoint(new Vector3(Mathf.Cos(t) * 0.42f, 0f, Mathf.Sin(t) * 0.42f));
                        int reedVariation = 1 + rng.Next(4);
                        float reedYaw = (float)(rng.NextDouble() * 360.0);
                        if (keep) Visual(Place(group, "pond_reeds", reedVariation, Mz(r.x, r.z, 0f, reedYaw), Vector3.zero, "dressing"));
                    }
                }

                Lotus(group, w);
            }

            if (falls != null) Waterfall(group, falls);
        }

        public const float WaterfallShoreOffset = 5.5f, WaterfallClearing = WaterfallShoreOffset, WaterfallInset = 1.2f;
        public const string WaterfallName = "Waterfall";

        private static Vector3 RimPoint(Transform water, float t) => water.TransformPoint(new Vector3(Mathf.Cos(t) * 0.5f, 0f, Mathf.Sin(t) * 0.5f));

        /// <summary>N3 · the ellipse parameter whose rim point lies <paramref name="metres"/> (straight line) from the rim point at
        /// <paramref name="t"/>, walking the rim in the direction of <paramref name="side"/>.</summary>
        public static float RimParameterAt(Transform water, float t, float side, float metres)
        {
            Vector3 from = RimPoint(water, t);
            float tp = t;
            for (int i = 0; i < 3600 && Vector3.Distance(RimPoint(water, tp), from) < metres; i++) tp += side * 0.001f;
            return tp;
        }

        /// <summary>The ellipse parameter of the pond's northernmost rim point (sampled; the disc may be rotated).</summary>
        private static float NorthRimAngle(Transform water)
        {
            float best = 0f, bestZ = float.NegativeInfinity;
            for (int i = 0; i < 360; i++)
            {
                float t = i * Mathf.Deg2Rad, z = RimPoint(water, t).z;
                if (z > bestZ) { bestZ = z; best = t; }
            }

            return best;
        }

        /// <summary>v007-B (C-00 25.09: curved shore only at the waterfall pond) · a taki-ishi group on the largest pond's north rim, set
        /// <see cref="WaterfallInset"/> into the water and pouring toward the pond's centre (the kit's water leaves along local −Y), with
        /// the concave curved shore modules (the land pushing into the water: V03 pebble, V06 moss) either side in place of the straight
        /// strips. Visual: it stands inside the pond's NotWalkable rim, which stays the NavMesh boundary.</summary>
        private static void Waterfall(Transform group, Transform water)
        {
            float t = NorthRimAngle(water);
            Vector3 rim = RimPoint(water, t);
            Vector3 inward = water.position - rim; inward.y = 0f; inward.Normalize();
            Vector3 at = rim + inward * WaterfallInset;
            GameObject rock = Place(group, "waterfall_rock", 2, Mz(at.x, at.z, 0f, Mathf.Atan2(inward.x, -inward.z) * Mathf.Rad2Deg), Vector3.zero, "dressing");
            rock.name = WaterfallName;
            Visual(rock);
            foreach ((float side, int variation) in new[] { (-1f, 3), (1f, 6) })
            {
                // N3: on the rim itself (the ellipse point WaterfallShoreOffset away along the shore), tangent-aligned there — the
                // straight tangent put both pieces ≈ 2.5 m out on the grass
                float tp = RimParameterAt(water, t, side, WaterfallShoreOffset);
                Vector3 p = RimPoint(water, tp), q = RimPoint(water, tp + 0.01f);
                float deg = Mathf.Atan2(q.z - p.z, q.x - p.x) * Mathf.Rad2Deg;
                Visual(Place(group, "pond_shore_curve", variation, Mz(p.x, p.z, 0f, deg), Vector3.zero, "dressing"));
            }
        }

        /// <summary>v007 · Lotus (hasu) clusters per pond: one per <see cref="LotusAreaPerCluster"/> m² of water, in drifts between 45 and 85 %
        /// of the ellipse (clear of the shore strips and the open middle the planar reflection shows), on the water surface (the kit's
        /// pivot is the water datum). Visual; their own rng so the shore sequence stays as it was.</summary>
        private static void Lotus(Transform group, Transform water)
        {
            var rng = new System.Random(748 + water.GetSiblingIndex());
            float area = Mathf.PI * water.localScale.x * water.localScale.z * 0.25f;
            int drifts = Mathf.Clamp(Mathf.RoundToInt(area / (LotusAreaPerCluster * 3f)), 1, 6);
            for (int d = 0; d < drifts; d++)
            {
                float t = (float)(rng.NextDouble() * Mathf.PI * 2.0), rad = 0.45f + (float)rng.NextDouble() * 0.30f;
                for (int k = 0; k < 3; k++)
                {
                    float tk = t + (float)(rng.NextDouble() - 0.5) * 0.35f, rk = Mathf.Min(0.85f, rad + (float)(rng.NextDouble() - 0.5) * 0.15f);
                    Vector3 p = water.TransformPoint(new Vector3(Mathf.Cos(tk) * 0.5f * rk, 0f, Mathf.Sin(tk) * 0.5f * rk));
                    Visual(Place(group, "lotus", 1 + rng.Next(3), Mz(p.x, p.z, water.position.y, (float)(rng.NextDouble() * 360.0)), Vector3.zero, "dressing"));
                }
            }
        }

        public const float LotusAreaPerCluster = 12f;

        /// <summary>The kit crossings' own deck width / length (AP-023 plank_bridge V01, slab_bridge V01), which the dress scales to the greybox decks.</summary>
        public const float KitPlankWidth = 1.6f, KitPlankLength = 10.8f, KitSlabWidth = 2f / 1.72f, KitSlabLength = 3.56f, KitSlabStep = 3.25f;   // KitSlabStep: the C1 slab as placed (13 m / 4)

        /// <summary>WP-088: the C1 granite slabs — the slab length holds as the causeway grows (13 m → 4, 17 m → 5).</summary>
        public static int C1SlabCount => Mathf.Max(1, Mathf.RoundToInt(GreyboxV6Builder.Crossings[GreyboxV6Builder.C1Causeway].Length / KitSlabStep));

        /// <summary>The pieces under <see cref="BridgeGroupName"/>: the two C8 plank spans and the C1 slabs.</summary>
        public static int BridgePieceCount => 2 + C1SlabCount;

        /// <summary>
        /// The C8 bridge as two plank spans and the C1 causeway as <see cref="C1SlabCount"/> granite slabs, scaled to the greybox decks
        /// (<see cref="GreyboxV6Builder.Crossings"/> — WP-074 B9 M-39: one source with the pond-strip exclusions): C8 3 m wide (× 1.875),
        /// C1 2 m (× 1.72). Kit colliders (arched deck ≤ 0.6, landings 0.15).
        /// </summary>
        private static void Bridges(Transform dress)
        {
            var group = new GameObject(BridgeGroupName).transform;
            group.SetParent(dress, false);
            GreyboxV6Builder.Crossing c8 = GreyboxV6Builder.Crossings[GreyboxV6Builder.C8Bridge], c1 = GreyboxV6Builder.Crossings[GreyboxV6Builder.C1Causeway];
            float span = c8.Length * 0.5f;
            foreach (float z in new[] { c8.Centre.y + span * 0.5f, c8.Centre.y - span * 0.5f })
                Place(group, "plank_bridge", 1, Mz(c8.Centre.x, z, 0f, 0f) * Matrix4x4.Scale(new Vector3(c8.Width / KitPlankWidth, span / KitPlankLength, 1f)), Vector3.zero, "structure");
            int slabs = C1SlabCount;
            float slab = c1.Length / slabs;
            for (int i = 0; i < slabs; i++)
                Place(group, "slab_bridge", 1, Mz(c1.Centre.x, c1.Min.y + slab * 0.5f + slab * i, 0f, 0f) * Matrix4x4.Scale(new Vector3(c1.Width / KitSlabWidth, slab / KitSlabLength, 1f)), Vector3.zero, "structure");
        }

        /// <summary>The west grove: its greybox blocks stay as the (invisible) collision, bamboo v2 clumps dress the solid cells, yotsume fences line the main lane — visual, no colliders.</summary>
        private static void Grove(Transform dress, Transform greybox)
        {
            foreach (Transform t in Children(greybox))
                if (t.name.StartsWith("Bamboo_", StringComparison.Ordinal)) t.GetComponent<MeshRenderer>().enabled = false;
            var group = new GameObject(GroveGroupName) { layer = PrototypeLayers.Environment }.transform;
            group.SetParent(dress, false);
            group.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var rng = new System.Random(747);
            for (float x = -53.5f; x <= -38f; x += 3.2f)
            for (float z = -29.5f; z <= 29.5f; z += 3.2f)
            {
                var c = new Vector2(x + (float)(rng.NextDouble() - 0.5) * 1.6f, z + (float)(rng.NextDouble() - 0.5) * 1.6f);
                double yaw = rng.NextDouble() * 360.0, s = 0.9 + rng.NextDouble() * 0.3;
                int v = GroveVariations[rng.Next(GroveVariations.Length)];
                if (!GreyboxV6Builder.GroveCellSolid(c)) continue;
                float laneDistance = float.MaxValue;
                foreach (Vector2[] lane in GreyboxV6Builder.BambooLanes) laneDistance = Mathf.Min(laneDistance, GreyboxV6Builder.DistanceToPolyline(c, lane));
                if (laneDistance < 3f) continue;
                // DEC-102 AD B2 (25.09): canopy gaps so the sun reaches the lanes (HF west: clumped bamboo, big gaps) — a position hash (the
                // rng sequence and every other clump stay put) leaves out GroveEdgeGap of the clumps within GroveEdgeBand m of a lane
                // and GroveInnerGap deeper in.
                if (GroveHash01(c) < (laneDistance < 3f + GroveEdgeBand ? GroveEdgeGap : GroveInnerGap)) continue;
                Visual(Place(group, "bamboo_v2", v, Mz(c.x, c.y, 0f, (float)yaw) * Matrix4x4.Scale(Vector3.one * (float)s), Vector3.zero, "vegetation"));
            }

            // The main lane (x −45) between its cross lanes: yotsume fence both sides, 1.8 m modules.
            foreach (float side in new[] { -1.9f, 1.9f })
            foreach ((float z0, float z1) in new[] { (-25f, -16.5f), (-11.5f, -2.5f), (2.5f, 11.5f), (16.5f, 25f) })
                FenceRun(group, "fence_yotsume", new Vector2(-45f + side, z0), new Vector2(-45f + side, z1), true);
        }

        /// <summary>The service yard (1.8 m) and the R7 sakura court walls as kenninji fences on the same lines and gaps (kit colliders).</summary>
        public static readonly (Vector2 a, Vector2 b, (float at, float width)[] gaps)[] FenceLines =
        {
            (new Vector2(-48f, 30f), new Vector2(-28f, 30f), new[] { (12f, 4f) }),
            (new Vector2(-28f, 30f), new Vector2(-28f, 48f), new[] { (2f, 4f) }),
            (new Vector2(-48f, 48f), new Vector2(-28f, 48f), new (float, float)[0]),
            (new Vector2(-48f, 30f), new Vector2(-48f, 48f), new[] { (16f, 3f) }),
            (new Vector2(27f, -8f), new Vector2(27f, 10f), new[] { (9f, 4f) }),
            (new Vector2(45f, -8f), new Vector2(45f, 10f), new[] { (9f, 3.5f) }),
            (new Vector2(27f, 10f), new Vector2(45f, 10f), new[] { (9f, 4f) }),
            (new Vector2(27f, -8f), new Vector2(45f, -8f), new[] { (9f, 4f) }),
        };

        private static void Fences(Transform dress)
        {
            var group = new GameObject(FenceGroupName).transform;
            group.SetParent(dress, false);
            foreach ((Vector2 a, Vector2 b, (float at, float width)[] gaps) in FenceLines)
            {
                float len = Vector2.Distance(a, b);
                Vector2 d = (b - a) / len;
                float cursor = 0f;
                foreach ((float at, float width) in gaps)
                {
                    if (at - width * 0.5f > cursor + 0.1f) FenceRun(group, "fence_kenninji", a + d * cursor, a + d * (at - width * 0.5f), false);
                    cursor = at + width * 0.5f;
                }

                if (len > cursor + 0.1f) FenceRun(group, "fence_kenninji", a + d * cursor, b, false);
            }
        }

        /// <summary>1.8 m fence modules (pivot at their left end, running +x) from a to b, stretched to fit.</summary>
        private static void FenceRun(Transform parent, string family, Vector2 a, Vector2 b, bool visual)
        {
            float len = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.8f));
            float w = len / n;
            Vector2 d = (b - a) / len;
            float deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = a + d * (i * w);
                GameObject f = Place(parent, family, 1, Mz(p.x, p.y, 0f, deg) * Matrix4x4.Scale(new Vector3(w / 1.8f, 1f, 1f)), Vector3.zero, "structure");
                if (visual) Visual(f);
            }
        }

        /// <summary>The rock garden: raked gravel bed (x 12…28, z −30…−16), low stone walls on the greybox lines and gaps, five kit boulders.</summary>
        private static void Karesansui(Transform dress)
        {
            var group = new GameObject(KaresansuiGroupName).transform;
            group.SetParent(dress, false);
            Slab(group, "Karesansui_Gravel", new Vector3(20f, 0.011f, -23f), new Vector3(16f, 0.02f, 14f), "gravel", 3f);
            UnityEngine.Object.DestroyImmediate(group.Find("Karesansui_Gravel").GetComponent<Collider>());
            foreach ((Vector2 a, Vector2 b) in new[] { (new Vector2(12f, -16f), new Vector2(16.5f, -16f)), (new Vector2(19.5f, -16f), new Vector2(28f, -16f)),
                                                       (new Vector2(28f, -30f), new Vector2(28f, -23.5f)), (new Vector2(28f, -20.5f), new Vector2(28f, -16f)) })
                LowWall(group, a, b);
            var rng = new System.Random(748);
            foreach (Vector2 rock in new[] { new Vector2(16f, -20f), new Vector2(22f, -19f), new Vector2(25f, -25f), new Vector2(15f, -27f), new Vector2(20f, -25.5f) })
                Place(group, "rock_large", 3 + rng.Next(3), Mz(rock.x, rock.y, 0f, (float)(rng.NextDouble() * 360.0)), Vector3.zero, "structure");
        }

        /// <summary>Low stone wall (wall_kit V07, 3 m module, 0.9 m) from a to b, stretched to fit.</summary>
        private static void LowWall(Transform parent, Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 3f));
            float w = len / n;
            Vector2 d = (b - a) / len;
            float deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = a + d * ((i + 0.5f) * w);
                Place(parent, "wall_kit", 7, Mz(p.x, p.y, 0f, deg) * Matrix4x4.Scale(new Vector3(w / 3f, 1f, 1f)), Vector3.zero, "structure");
            }
        }

        /// <summary>An instance made visual only: its colliders off (the greybox collision or the NavMesh boundary rules there).</summary>
        internal static void Visual(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) c.gameObject.SetActive(false);
        }

        /// <summary>v007-B · R6's chūmon: AP-023 chumon V01 (posts 8 × 5 m, 5.0 m bays) widened × <see cref="ChumonWidthScale"/> along x so the
        /// axis opening stays ≥ 6 m (C-00 25.09: the karamon's 6.0 m clear), the court walls ending <see cref="ChumonSidePassage"/> short.</summary>
        public const float ChumonWidthScale = 1.2f, ChumonHalfWidth = 4.0f * ChumonWidthScale, ChumonSidePassage = 1.5f;
        public const string ChumonName = "Chumon";

        private static void Chumon(Transform dress)
        {
            // R6 (x ±4.8, z 18…23): the two-storey gate house on the north axis, its long sides open S / N (6.0 m clear), its short sides'
            // 2 m doors toward the side passages (the greybox E / W doors). Upper storey not enterable (the kit's own colliders).
            GameObject gate = Place(dress, "chumon", 1, Mz(0f, 20.5f, 0f, 0f) * Matrix4x4.Scale(new Vector3(ChumonWidthScale, 1f, 1f)), Vector3.zero, "structure");
            gate.name = ChumonName;
            Transform roof = gate.transform.Find(ArtLibraryPrefabBuilder.RoofName);
            if (roof != null) RoofOccluder(gate.transform, roof);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>One library prefab at a Blender-space matrix (+ a Blender-space offset); static and lightmap policy as PlacementImporter.</summary>
        private static GameObject Place(Transform parent, string family, int variation, Matrix4x4 blender, Vector3 blenderOffset, string role)
        {
            return Place(parent, Ap, family, variation, blender, blenderOffset, role);
        }

        /// <summary>WP-092 · <see cref="Place(Transform, string, int, Matrix4x4, Vector3, string)"/> from any library package (AP-023, AP-030).</summary>
        internal static GameObject Place(Transform parent, string ap, string family, int variation, Matrix4x4 blender, Vector3 blenderOffset, string role)
        {
            string name = "SM_" + family + "_V" + variation.ToString("00");
            if (!s_prefabs.TryGetValue(ap + "/" + name, out GameObject prefab))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtLibraryPaths.Prefab(ap, family, name));
                if (prefab == null) throw new InvalidOperationException(ap + " prefab missing: " + name);
                s_prefabs[ap + "/" + name] = prefab;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            Vector3 t = (Vector3)blender.GetColumn(3) + blenderOffset;
            Vector3 cx = blender.GetColumn(0), cy = blender.GetColumn(1), cz = blender.GetColumn(2);
            float yaw = Mathf.Atan2(cx.y, cx.x) * Mathf.Rad2Deg;
            go.transform.SetPositionAndRotation(WorldPlacementAsset.ToUnity(t.x, t.y, t.z), WorldPlacementAsset.ToUnityYaw(yaw));
            go.transform.localScale = new Vector3(cx.magnitude, cz.magnitude, cy.magnitude);

            // PlacementImporter's policy: structures lightmapped (ContributeGI), dressing static but probe-lit, vegetation not static
            // (it keeps its LOD cross-fade and GPU instancing; B8: 1 100 vista trees must not take lightmap charts).
            StaticEditorFlags flags = StaticEditorFlags.OccludeeStatic;
            if (role != "vegetation")
            {
                flags |= StaticEditorFlags.ReflectionProbeStatic;
                if (role != "dressing") flags |= StaticEditorFlags.ContributeGI;
                if (!PlacementImporter.IsHeavyForStaticBatching(prefab)) flags |= StaticEditorFlags.BatchingStatic;
            }
            foreach (Transform c in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(c.gameObject, flags);
            ArtLightingBake.ApplyLightmapLodPolicy(go, role != "dressing" && role != "vegetation");
            if (PlacementImporter.LowCasterRole(role) && PlacementImporter.ShadowCasterHeight(go) < PlacementImporter.LowCasterMaxHeight)
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A box collider on the prefab's Roof child (layer CameraOccluder, Roof class, out of the bake) so the top-down fade finds it.</summary>
        internal static void RoofOccluder(Transform instance, Transform roof)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (Renderer r in roof.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = instance.InverseTransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (!any) { bounds = new Bounds(corner, Vector3.zero); any = true; }
                    else bounds.Encapsulate(corner);
                }
            }

            if (!any) return;
            var go = new GameObject(RoofOccluderName) { layer = PrototypeLayers.CameraOccluder };
            go.transform.SetParent(roof, false);
            go.transform.SetPositionAndRotation(instance.position, instance.rotation);
            var col = go.AddComponent<BoxCollider>();
            col.center = bounds.center;
            col.size = bounds.size;
            go.AddComponent<CameraOccluderClass>().Kind = CameraOccluderKind.Roof;
            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        }

        private static void Slab(Transform parent, string name, Vector3 centre, Vector3 size, string role, float tile)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = PrototypeLayers.Environment;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = BoxMaterial(name, role, new Vector2(size.x / tile, size.z / tile));
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static void Restyle(Transform greybox, string name, string role, float tile)
        {
            Transform t = greybox.Find(name);
            if (t == null) return;
            // The largest face decides the tiling (a cube face maps 0…1): its two largest dimensions.
            var dims = new List<float> { t.localScale.x, t.localScale.y, t.localScale.z };
            dims.Sort();
            var face = new Vector2(dims[2], dims[1]);
            t.GetComponent<MeshRenderer>().sharedMaterial = BoxMaterial(name.StartsWith("Planter_", StringComparison.Ordinal) ? "Planter" : name, role, face / tile);
        }

        /// <summary>A tiled copy of M_ap023_&lt;role&gt; for a primitive box (V5_&lt;name&gt;.mat in the greybox material folder; rebuilt in place).</summary>
        private static Material BoxMaterial(string name, string role, Vector2 tiling)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.ArtRoot + "/Materials/M_ap023_" + role + ".mat");
            if (source == null) throw new InvalidOperationException("M_ap023_" + role + " missing");
            string path = GreyboxV6Builder.DataFolder + "/Materials/V5_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(source);
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.CopyPropertiesFromMaterial(source);
                m.shader = source.shader;
            }

            m.SetTextureScale("_BaseMap", tiling);
            if (m.HasProperty("_MainTex")) m.SetTextureScale("_MainTex", tiling);   // the legacy slot kept in step, so a rebuild is byte-stable
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
