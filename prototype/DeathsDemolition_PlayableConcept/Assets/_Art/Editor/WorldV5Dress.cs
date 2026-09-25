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
        public static readonly string[] ReplacedPrefixes = { "Hall_Plinth", "Hall_Stair", "R1_MainHall_", "RingWall_", "Court_", "Karamon_", "Lantern_", "Planter_" };

        /// <summary>The four centre planters (greybox BuildArena corners), 3 × 3 m.</summary>
        public static readonly Vector2[] PlanterCentres = { new Vector2(-9f, -9f), new Vector2(-9f, 9f), new Vector2(9f, -9f), new Vector2(9f, 9f) };

        public static Report Apply(Transform greybox, Transform worldRoot)
        {
            s_prefabs.Clear();
            var report = new Report();
            var dress = new GameObject(RootName).transform;
            dress.SetParent(worldRoot, false);

            foreach (Transform t in new List<Transform>(Children(greybox)))
                foreach (string prefix in ReplacedPrefixes)
                    if (t.name.StartsWith(prefix, StringComparison.Ordinal)) { UnityEngine.Object.DestroyImmediate(t.gameObject); report.RemovedBoxes++; break; }

            report.HallPieces = Hall(dress, greybox, report);
            report.WallPieces = RingWall(dress) + Forecourt(dress);
            Karamon(dress);
            Lanterns(dress);
            Planters(dress);
            Yagura(dress);
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

            // Up to the chūmon's side walls (greybox R6, x ±4, 0.3 thick): the run ends in its centreline.
            n += Run(court, new Vector2(-24.5f, 20f), new Vector2(-4.15f, 20f));
            n += Run(court, new Vector2(24.5f, 20f), new Vector2(4.15f, 20f));
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

        // ---------------------------------------------------------------- helpers

        /// <summary>One library prefab at a Blender-space matrix (+ a Blender-space offset); static and lightmap policy as PlacementImporter.</summary>
        private static GameObject Place(Transform parent, string family, int variation, Matrix4x4 blender, Vector3 blenderOffset, string role)
        {
            string name = "SM_" + family + "_V" + variation.ToString("00");
            if (!s_prefabs.TryGetValue(name, out GameObject prefab))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtLibraryPaths.Prefab(Ap, family, name));
                if (prefab == null) throw new InvalidOperationException("AP-023 prefab missing: " + name);
                s_prefabs[name] = prefab;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            Vector3 t = (Vector3)blender.GetColumn(3) + blenderOffset;
            Vector3 cx = blender.GetColumn(0), cy = blender.GetColumn(1), cz = blender.GetColumn(2);
            float yaw = Mathf.Atan2(cx.y, cx.x) * Mathf.Rad2Deg;
            go.transform.SetPositionAndRotation(WorldPlacementAsset.ToUnity(t.x, t.y, t.z), WorldPlacementAsset.ToUnityYaw(yaw));
            go.transform.localScale = new Vector3(cx.magnitude, cz.magnitude, cy.magnitude);

            StaticEditorFlags flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            if (role != "dressing") flags |= StaticEditorFlags.ContributeGI;
            if (!PlacementImporter.IsHeavyForStaticBatching(prefab)) flags |= StaticEditorFlags.BatchingStatic;
            foreach (Transform c in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(c.gameObject, flags);
            ArtLightingBake.ApplyLightmapLodPolicy(go, role != "dressing");
            if (PlacementImporter.LowCasterRole(role) && PlacementImporter.ShadowCasterHeight(go) < PlacementImporter.LowCasterMaxHeight)
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A box collider on the prefab's Roof child (layer CameraOccluder, Roof class, out of the bake) so the top-down fade finds it.</summary>
        private static void RoofOccluder(Transform instance, Transform roof)
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
