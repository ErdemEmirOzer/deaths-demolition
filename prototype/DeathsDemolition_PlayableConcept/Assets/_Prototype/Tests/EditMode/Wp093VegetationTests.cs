using System.Collections.Generic;
using DD.Prototype.World.Vegetation;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-093 Faz 1 · living ground, EditMode (no Unity scene, no physics): the vc1 placeholder contract (AP-031 §1: root
    /// R ≈ 0, tip R ≈ 1, pivot at the base, AO 0.35..1, triangle ceilings §2), the C# / HLSL hash contract, chunk
    /// distance / density / culling math, deterministic placement (same seed → identical matrices), the LOOK-BIBLE
    /// exclusions and bands (path, plaza, water, corridor caps, wall-base GT band), S-05 mask merge and roads.json parse,
    /// and the push map's stamp / spring / flatten decay (WIND-SPEC §3). TD only.
    /// </summary>
    [Category("WP093")]
    public sealed class Wp093VegetationTests
    {
        // AP-031 §2 triangle ceilings (LOD0, LOD1, LOD2); ML is a card set (own ceiling).
        private static readonly int[,] Ceilings =
        {
            { 96, 32, 0 }, { 128, 40, 4 }, { 192, 56, 4 }, { 160, 48, 4 }, { 120, 40, 4 },
            { 48, 16, 0 }, { 320, 96, 4 }, { 128, 40, 0 }, { 240, 72, 4 }, { 48, 16, 0 },
        };

        private readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _made.Count; i++)
            {
                if (_made[i] != null) Object.DestroyImmediate(_made[i]);
            }

            _made.Clear();
        }

        private static VegetationGrid FlatGrid(float half, VegetationRecipe recipe, List<VegetationRoad> roads = null)
        {
            var o = new VegetationProbeOptions
            {
                Min = new Vector2(-half, -half), Max = new Vector2(half, half),
                FlatGround = true, UseNavRegions = false, UseMarkers = false, Roads = roads
            };
            return VegetationWorldProbe.Probe(o, recipe);
        }

        // ------------------------------------------------------------------ vc1 contract

        [Test]
        public void Vc1_YerTutucuMeshler_KokSifir_UcBir_PivotTabanda_UcgenTavani()
        {
            VegetationRecipe recipe = VegetationRecipe.Load();
            for (int t = 0; t < VegetationTypes.Count; t++)
            {
                VegetationRecipe.Species sp = recipe.Get((VegetationType)t);
                bool still = sp.type == VegetationType.Moss || sp.type == VegetationType.LeafLitter;   // R = 0: does not move
                for (int v = 0; v < sp.variants; v++)
                {
                    for (int l = 0; l < VegetationMeshFactory.LodCount(sp); l++)
                    {
                        Mesh mesh = VegetationMeshFactory.Build(sp, v, l);
                        _made.Add(mesh);
                        string id = mesh.name;
                        Vector3[] pos = mesh.vertices;
                        Color[] col = mesh.colors;
                        Assert.AreEqual(pos.Length, col.Length, id + ": vertex colour stream missing");
                        Assert.Greater(pos.Length, 0, id + ": empty");

                        float minY = float.MaxValue, maxY = float.MinValue, maxR = 0f;
                        int top = 0;
                        for (int i = 0; i < pos.Length; i++)
                        {
                            if (pos[i].y < minY) minY = pos[i].y;
                            if (pos[i].y > maxY) { maxY = pos[i].y; top = i; }
                            maxR = Mathf.Max(maxR, col[i].r);
                            Assert.That(col[i].a, Is.InRange(0.35f - 1e-3f, 1f + 1e-3f), id + ": AO outside 0.35..1");
                            Assert.That(col[i].g, Is.InRange(0f, 1f), id + ": phase outside 0..1");
                            Assert.That(col[i].b, Is.InRange(0f, 1f), id + ": stiffness outside 0..1");
                            if (pos[i].y < 0.002f) Assert.LessOrEqual(col[i].r, 0.02f, id + ": root vertex bends (R must be ≈ 0 at y = 0)");
                        }

                        Assert.GreaterOrEqual(minY, -1e-4f, id + ": geometry below the pivot");
                        Assert.LessOrEqual(minY, 0.01f, id + ": root not at y = 0");
                        Vector3 b = mesh.bounds.center;
                        Assert.Less(new Vector2(b.x, b.z).magnitude, sp.footprint, id + ": pivot not at the base centre");
                        if (still) Assert.LessOrEqual(maxR, 1e-4f, id + ": moss / litter must not bend");
                        else
                        {
                            Assert.GreaterOrEqual(maxR, 0.95f, id + ": tip R must reach ≈ 1");
                            Assert.GreaterOrEqual(col[top].r, 0.95f, id + ": the highest vertex must carry R ≈ 1");
                        }

                        int tris = mesh.triangles.Length / 3;
                        int cap = Ceilings[t, Mathf.Min(l, 2)];
                        Assert.LessOrEqual(tris, cap, id + ": " + tris + " triangles over the AP-031 ceiling " + cap);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ hash + math

        [Test]
        public void Hash_Deterministik_KuantumIcindeSabit_Aralikta()
        {
            Assert.AreEqual(VegetationHash.Instance(3.25f, -7.5f), VegetationHash.Instance(3.25f, -7.5f));
            // Same 1/64 m cell → same hash (the shader quantises identically).
            Assert.AreEqual(VegetationHash.Instance(10.001f, 4.001f), VegetationHash.Instance(10.0011f, 4.0011f));
            int differ = 0;
            for (int i = 0; i < 64; i++)
            {
                float h = VegetationHash.Instance01(i * 0.37f, -i * 0.11f);
                Assert.That(h, Is.InRange(0f, 1f - 1e-7f));
                if (Mathf.Abs(h - VegetationHash.Instance01(i * 0.37f + 0.5f, -i * 0.11f)) > 1e-6f) differ++;
            }

            Assert.Greater(differ, 56, "hash barely varies with position");
        }

        [Test]
        public void ParcaMesafeYogunlukLod_Matematigi()
        {
            var b = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
            VegetationLayout.DistanceRange(b, new Vector3(3f, 0f, 0f), out float near, out float far);
            Assert.AreEqual(2f, near, 1e-4f);
            Assert.AreEqual(Mathf.Sqrt(18f), far, 1e-4f);
            VegetationLayout.DistanceRange(b, Vector3.zero, out near, out _);
            Assert.AreEqual(0f, near, 1e-6f, "inside the box = 0");

            Assert.AreEqual(1f, VegetationLayout.DensityAt(10f, 30f, 42f, 0.35f), 1e-6f);
            Assert.AreEqual(0.35f, VegetationLayout.DensityAt(60f, 30f, 42f, 0.35f), 1e-6f);
            float mid = VegetationLayout.DensityAt(36f, 30f, 42f, 0.35f);
            Assert.That(mid, Is.InRange(0.35f, 1f));

            Assert.AreEqual(100, VegetationLayout.DrawCount(100, 1f, 0.06f));
            Assert.AreEqual(Mathf.CeilToInt(100 * 0.56f) + 1, VegetationLayout.DrawCount(100, 0.5f, 0.06f));
            Assert.AreEqual(0, VegetationLayout.DrawCount(0, 1f, 0.06f));

            Assert.AreEqual(1f, VegetationLayout.HeightFactor(15f, 15f, 0.5f), 1e-6f, "TD camera height = authored density");
            Assert.AreEqual(0.5f, VegetationLayout.HeightFactor(30f, 15f, 0.5f), 1e-6f);
            Assert.AreEqual(0.5f, VegetationLayout.HeightFactor(90f, 15f, 0.5f), 1e-6f, "never below the floor");
        }

        [Test]
        public void ParcaKirpma_TdKamerasi_OndekiGorunur_ArkadakiDegil()
        {
            var go = new GameObject("WP093 TD camera");
            _made.Add(go);
            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = 42f;
            cam.aspect = 16f / 9f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            go.transform.position = new Vector3(0f, 15f, -13f);
            go.transform.LookAt(Vector3.zero);
            var planes = new Plane[6];
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Assert.IsTrue(GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(4f, 0.5f, 4f), new Vector3(8f, 1f, 8f))), "chunk under the focus culled");
            Assert.IsFalse(GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(0f, 0.5f, -60f), new Vector3(8f, 1f, 8f))), "chunk behind the camera drawn");
            Assert.IsFalse(GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(120f, 0.5f, 0f), new Vector3(8f, 1f, 8f))), "chunk far to the side drawn");

            var layout = new VegetationLayout { Origin = new Vector2(-16f, -16f), ChunkSize = 8f, ChunksX = 4, ChunksZ = 4 };
            Assert.AreEqual(0, layout.ChunkIndex(-15.9f, -15.9f));
            Assert.AreEqual(15, layout.ChunkIndex(15.9f, 15.9f));
            Assert.AreEqual(1 * 4 + 2, layout.ChunkIndex(0.5f, -7.5f));
            Assert.AreEqual(0, layout.ChunkIndex(-500f, -500f), "outside clamps to the edge chunk");
        }

        // ------------------------------------------------------------------ placement

        [Test]
        public void Yerlesim_AyniTohum_AyniOrnekler_FarkliTohum_Farkli()
        {
            VegetationRecipe recipe = VegetationRecipe.Load();
            VegetationDensityField field = VegetationZoner.Evaluate(FlatGrid(12f, recipe), recipe);
            VegetationLayout a = VegetationPlacer.Place(field, recipe, 1f, 9301, null);
            VegetationLayout b = VegetationPlacer.Place(field, recipe, 1f, 9301, null);
            VegetationLayout c = VegetationPlacer.Place(field, recipe, 1f, 4242, null);
            Assert.Greater(a.TotalInstances, 100, "flat lawn placed almost nothing");
            Assert.AreEqual(a.TotalInstances, b.TotalInstances);
            Assert.AreEqual(a.Groups.Length, b.Groups.Length);
            bool anyDiff = a.TotalInstances != c.TotalInstances;
            for (int g = 0; g < a.Groups.Length; g++)
            {
                Assert.AreEqual(a.Groups[g].Count, b.Groups[g].Count, "group " + g);
                for (int k = 0; k < a.Groups[g].Count; k++)
                {
                    Assert.AreEqual(a.Groups[g].Matrices[k], b.Groups[g].Matrices[k], "group " + g + " instance " + k);
                }

                if (!anyDiff && g < c.Groups.Length && c.Groups[g].Count > 0 && a.Groups[g].Count > 0 && a.Groups[g].Matrices[0] != c.Groups[g].Matrices[0]) anyDiff = true;
            }

            Assert.IsTrue(anyDiff, "the seed changes nothing");

            // Chunk ranges are hash-sorted (the CPU half of the distance density fade).
            for (int g = 0; g < a.Groups.Length; g++)
            {
                VegetationLayout.Group grp = a.Groups[g];
                for (int ch = 0; ch < a.ChunkCount; ch++)
                {
                    float last = -1f;
                    for (int k = grp.ChunkStart[ch]; k < grp.ChunkStart[ch] + grp.ChunkCount[ch]; k++)
                    {
                        Vector4 p = grp.Matrices[k].GetColumn(3);
                        float h = VegetationHash.Instance01(p.x, p.z);
                        Assert.GreaterOrEqual(h, last, "chunk range not sorted by hash");
                        last = h;
                    }
                }
            }

            // Tier scale: Performance places fewer.
            VegetationLayout perf = VegetationPlacer.Place(field, recipe, recipe.densityPerformance, 9301, null);
            Assert.Less(perf.TotalInstances, a.TotalInstances);
        }

        // ------------------------------------------------------------------ masks and bible bands

        [Test]
        public void Maske_YolPlazaSu_Dislanir_KenarSolmasi_KoridorTavani()
        {
            VegetationRecipe recipe = VegetationRecipe.Load();
            var roads = new List<VegetationRoad> { new VegetationRoad { Id = "t", Type = "gravel", Width = 2f, Points = new[] { new Vector2(-12f, 0f), new Vector2(12f, 0f) } } };
            VegetationGrid grid = FlatGrid(12f, recipe, roads);
            VegetationWorldProbe.StampPlaza(grid, new[] { new Vector4(4f, 4f, 8f, 8f) });
            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 c = grid.CellCenter(i);
                if ((c - new Vector2(-6f, 6f)).magnitude < 1.5f) grid.Set(i, VegetationGrid.Flag.Water);
            }

            VegetationDensityField f = VegetationZoner.Evaluate(grid, recipe);
            int ss = (int)VegetationType.GrassShort, gt = (int)VegetationType.GrassTall;
            float pathEdgeSs = 0f, lawnSs = 0f;
            int pathEdgeN = 0, lawnN = 0;
            for (int i = 0; i < f.Count; i++)
            {
                Vector2 c = f.CellCenter(i);
                bool excluded = grid.Has(i, VegetationGrid.Flag.Path) || grid.Has(i, VegetationGrid.Flag.Plaza) || grid.Has(i, VegetationGrid.Flag.Water);
                if (excluded)
                {
                    for (int t = 0; t < VegetationTypes.Count; t++) Assert.AreEqual(0, f.Layers[t][i], "vegetation on an excluded cell " + c + " species " + VegetationTypes.Codes[t]);
                    continue;
                }

                // Open flat lawn far from obstacles = corridor: no GT / FE / KZ / HY, height cap ≤ 0.45 m (R1, principle 2).
                Assert.AreEqual(0, f.Layers[gt][i], "GT in the corridor at " + c);
                Assert.AreEqual(0, f.Layers[(int)VegetationType.Kumazasa][i], "KZ in the corridor at " + c);
                if ((c - new Vector2(-6f, 6f)).magnitude > 4f)   // the pond's shore / bank are not corridor
                {
                    float cap = f.HeightCapAt(i);
                    Assert.That(cap, Is.GreaterThan(0f).And.LessThanOrEqualTo(recipe.transitions.corridorHeightCap + 1e-3f), "corridor cap missing at " + c);
                }

                float dz = Mathf.Abs(c.y) - 1f;   // distance to the road edge
                if (dz > 0.05f && dz < 0.35f && Mathf.Abs(c.x) < 3f) { pathEdgeSs += f.Density(ss, i); pathEdgeN++; }
                if (dz > 3f && Mathf.Abs(c.x - 6f) > 4f && Mathf.Abs(c.x + 6f) > 4f) { lawnSs += f.Density(ss, i); lawnN++; }
            }

            Assert.Greater(pathEdgeN, 0);
            Assert.Greater(lawnN, 0);
            Assert.Less(pathEdgeSs / pathEdgeN, 0.6f * (lawnSs / lawnN), "no path-edge fade (bible Z5: SS 30 % in the inner 0.4 m)");
        }

        [Test]
        public void DuvarDibi_UzunOtBandi_DuvaraYakin_UzaktaYok()
        {
            VegetationRecipe recipe = VegetationRecipe.Load();
            VegetationGrid grid = FlatGrid(16f, recipe);
            // A 2.4 m wall along x at z = 4 .. 4.5 (0.5 m thick), 30 m long.
            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 c = grid.CellCenter(i);
                if (c.y >= 4f && c.y <= 4.5f && Mathf.Abs(c.x) <= 15f)
                {
                    grid.Obstacle[i] = (byte)VegetationObstacle.Wall;
                    grid.ObstacleHeight[i] = 2.4f;
                }
            }

            VegetationDensityField f = VegetationZoner.Evaluate(grid, recipe);
            int gt = (int)VegetationType.GrassTall;
            float band = 0f;
            for (int i = 0; i < f.Count; i++)
            {
                Vector2 c = f.CellCenter(i);
                float d = c.y > 4.5f ? c.y - 4.5f : c.y < 4f ? 4f - c.y : 0f;
                if (f.Layers[gt][i] == 0) continue;
                Assert.Less(d, recipe.transitions.wallBand + 0.3f, "GT outside the wall-base band at " + c);
                band += f.Density(gt, i);
            }

            Assert.Greater(band, 0f, "no tall grass at the wall base (bible Z6)");
        }

        [Test]
        public void S05Maskesi_KanallarIzgaraya_Isler()
        {
            VegetationRecipe recipe = VegetationRecipe.Load();
            VegetationGrid grid = FlatGrid(4f, recipe);
            var a = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            var b = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            _made.Add(a);
            _made.Add(b);
            var pa = new Color32[64];
            var pb = new Color32[64];
            for (int i = 0; i < 64; i++)
            {
                int x = i % 8;
                pa[i] = new Color32(x == 0 ? (byte)255 : (byte)0, x == 1 ? (byte)255 : (byte)0, x == 2 ? (byte)255 : (byte)0, x == 3 ? (byte)255 : x == 4 ? (byte)64 : (byte)0);
                pb[i] = new Color32(x == 5 ? (byte)24 : (byte)0, x == 6 ? (byte)11 : (byte)0, x == 7 ? (byte)64 : (byte)0, 0);
            }

            a.SetPixels32(pa);
            a.Apply();
            b.SetPixels32(pb);
            b.Apply();
            var masks = new VegetationMaskSet { MaskA = a, MaskB = b, MinX = -4f, MinZ = -4f, MaxX = 4f, MaxZ = 4f };
            masks.ApplyTo(grid);
            int Col(float wx) { grid.TryCell(wx, 0.1f, out int cx, out int cz); return grid.Index(cx, cz); }
            Assert.IsTrue(grid.Has(Col(-3.5f), VegetationGrid.Flag.Path), "R = path");
            Assert.IsTrue(grid.Has(Col(-2.5f), VegetationGrid.Flag.Excluded), "G = exclusion");
            Assert.IsTrue(grid.Has(Col(-1.5f), VegetationGrid.Flag.Water), "B = water");
            Assert.AreEqual(2, grid.Corridor[Col(-0.5f)], "A ≥ 128 = corridor");
            Assert.AreEqual(1, grid.Corridor[Col(0.5f)], "A 1..127 = not corridor");
            Assert.AreEqual((byte)VegetationObstacle.Wall, grid.Obstacle[Col(1.5f)], "mask_b.R = wall footprint");
            Assert.AreEqual(2.4f, grid.ObstacleHeight[Col(1.5f)], 1e-4f, "wall height in decimetres");
            Assert.AreEqual((byte)VegetationZone.MossCourt, grid.Zone[Col(2.5f)], "mask_b.G = zone id");
            Assert.AreEqual(0.5f, VegetationGrid.Multiplier(grid.DensityMul[Col(3.5f)]), 1e-4f, "mask_b.B = density × v/128");
            Assert.AreEqual(1f, VegetationGrid.Multiplier(grid.DensityMul[Col(-3.5f)]), 1e-6f, "0 = ×1");
        }

        [Test]
        public void Roads_Json_wp092_Okunur_YanlisSemaReddedilir()
        {
            const string json = "{\"schema\":\"wp092.roads.v1\",\"roads\":[{\"id\":\"main_n\",\"type\":\"gravel\",\"width\":3.5,\"points\":[[0,-10],[0.5,4.25],[-2,20]]},"
                                + "{\"id\":\"steps\",\"type\":\"stepstone\",\"width\":1.2,\"points\":[[5,5],[6,6]]}],\"zones\":[{\"id\":\"m\",\"kind\":\"moss\",\"polygon\":[[0,0],[1,0],[1,1]]}]}";
            List<VegetationRoad> roads = VegetationMaskSet.ParseRoads(json, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(2, roads.Count);
            Assert.AreEqual("main_n", roads[0].Id);
            Assert.AreEqual(3, roads[0].Points.Length);
            Assert.AreEqual(new Vector2(0.5f, 4.25f), roads[0].Points[1]);
            Assert.AreEqual(1.75f, roads[0].HalfWidth, 1e-5f);
            Assert.AreEqual(0.36f, roads[1].HalfWidth, 1e-5f, "stepstone half width × 0.6");

            Assert.IsNull(VegetationMaskSet.ParseRoads("{\"schema\":\"other\",\"roads\":[]}", out error));
            Assert.IsNotNull(error);
            Assert.IsNull(VegetationMaskSet.ParseRoads("{\"schema\":", out error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void PlazaDerzi_Bolge13_TaslarArasindaTutam_DigerPlazaBos()
        {
            VegetationRecipe recipe = VegetationRecipe.Load();
            VegetationGrid grid = FlatGrid(8f, recipe);
            VegetationWorldProbe.StampPlaza(grid, new[] { new Vector4(-6f, -6f, 6f, 6f) });
            int joints = 0;
            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 c = grid.CellCenter(i);
                if (c.x < -5f || c.x > 5f || c.y < -5f || c.y > 5f) continue;
                if (c.y > 0f) continue;                                   // north half: plaza without the joint zone
                grid.Zone[i] = (byte)VegetationZone.PlazaJoint;
                bool line = Mathf.Abs(Mathf.Repeat(c.x, 1f) - 0.125f) < 0.01f;   // one joint column per metre
                grid.DensityMul[i] = line ? (byte)255 : (byte)1;
                if (line) joints++;
            }

            Assert.Greater(joints, 0);
            VegetationDensityField f = VegetationZoner.Evaluate(grid, recipe);
            int ss = (int)VegetationType.GrassShort, mo = (int)VegetationType.Moss;
            float onJoint = 0f, onStone = 0f;
            for (int i = 0; i < f.Count; i++)
            {
                Vector2 c = f.CellCenter(i);
                if (c.x < -5f || c.x > 5f || c.y < -5f || c.y > 5f) continue;
                if (c.y > 0f)
                {
                    for (int t = 0; t < VegetationTypes.Count; t++) Assert.AreEqual(0, f.Layers[t][i], "plaza without zone 13 grows " + VegetationTypes.Codes[t] + " at " + c);
                    continue;
                }

                if (grid.DensityMul[i] == 255)
                {
                    onJoint += f.Density(ss, i) + f.Density(mo, i);
                    Assert.That(f.HeightCapAt(i), Is.GreaterThan(0f).And.LessThanOrEqualTo(0.12f + 1e-3f), "joint tufts ≤ 12 cm");
                }
                else onStone += f.Density(ss, i) + f.Density(mo, i);
            }

            Assert.Greater(onJoint, 0f, "no tufts on the joint lines");
            Assert.Less(onStone, onJoint * 0.05f, "stone faces (B = 1) carry tufts");
        }

#if UNITY_EDITOR
        [Test]
        public void Onizleme_SentetikDunya_CalismaZamaniYerlesimiyleAyni_DisposeTemizler()
        {
            VegetationRecipe recipe = VegetationRecipe.Load();
            VegetationDensityField field = VegetationZoner.Evaluate(FlatGrid(10f, recipe), recipe);
            VegetationPreview.Handle h = VegetationPreview.Populate(field, VegetationPreview.TierHigh, recipe);
            var draw = new VegetationDrawSet(recipe, VegetationKit.Load());
            try
            {
                VegetationLayout runtime = VegetationPlacer.Place(field, recipe, recipe.TierDensity(VegetationPreview.TierHigh), VegetationSystem.Seed, draw.NominalHeights);
                Assert.IsNotNull(h.Layout);
                Assert.Greater(runtime.TotalInstances, 100);
                Assert.AreEqual(runtime.TotalInstances, h.Layout.TotalInstances, "preview count != runtime count");
                for (int g = 0; g < runtime.Groups.Length; g++)
                {
                    Assert.AreEqual(runtime.Groups[g].Count, h.Layout.Groups[g].Count, "group " + g);
                    for (int k = 0; k < runtime.Groups[g].Count; k++)
                    {
                        Vector4 a = runtime.Groups[g].Matrices[k].GetColumn(3), b = h.Layout.Groups[g].Matrices[k].GetColumn(3);
                        Assert.AreEqual(VegetationHash.Instance(a.x, a.z), VegetationHash.Instance(b.x, b.z), "group " + g + " instance " + k);
                        Assert.AreEqual(runtime.Groups[g].Matrices[k], h.Layout.Groups[g].Matrices[k]);
                    }
                }

                Assert.Greater(h.DrawSet.CoverAt(0f, 0f), 0.3f, "lawn cover missing from the ground-cover map");
            }
            finally
            {
                draw.Dispose();
                h.Dispose();
            }

            Assert.IsNull(h.Layout, "Dispose left the preview layout");
            Assert.AreEqual(0f, Shader.GetGlobalVector(VegetationDrawSet.GroundId).w, "Dispose left the ground-cover globals on");
        }
#endif

        // ------------------------------------------------------------------ interaction map (CPU)

        [Test]
        public void ItmeHaritasi_Damga_Yay_Yatma_Sonumlenir()
        {
            var map = new VegetationPushMap(256, 40f, false);
            map.Recenter(Vector2.zero);
            map.Advance(0.016f);
            var p = new Vector2(1f, 1f);
            map.Stamp(p, new Vector2(3f, 0f), 0.55f, 1f, 1.5f, 0.6f, 0.6f, 0.016f);
            float d0 = map.DisplacementAt(p);
            float side = map.DisplacementAt(p + new Vector2(0.3f, 0f));
            Assert.Greater(d0, 0.05f, "no displacement at the pusher");
            Assert.Greater(side, 0f, "no displacement beside the pusher");
            Assert.AreEqual(0f, map.DisplacementAt(p + new Vector2(2f, 0f)), 1e-6f, "displacement outside the radius");
            Assert.Greater(map.FlattenAt(p), 0f, "no flatten from dwell");

            // Dwell builds flatten up to its cap.
            for (int i = 0; i < 120; i++) map.Stamp(p, Vector2.zero, 0.55f, 1f, 1.5f, 0.6f, 0f, 0.016f);
            float flat = map.FlattenAt(p);
            Assert.That(flat, Is.InRange(0.3f, 0.6f + 1e-4f), "flatten does not build to the 0.6 cap");
            float disp = map.DisplacementAt(p);

            // Release: ≈ 0.6 s spring-back (ω 8, ζ 0.7), flatten holds 0.4 s then halves per second.
            for (int i = 0; i < 40; i++) map.Advance(0.016f);   // 0.64 s, no stamps
            Assert.Less(map.DisplacementAt(p), 0.12f * disp, "displacement did not spring back in ≈ 0.6 s");
            Assert.Greater(map.FlattenAt(p), 0.5f * flat, "flatten released too early (hold 0.4 s, half-life 1 s)");
            for (int i = 0; i < 200; i++) map.Advance(0.016f);  // +3.2 s
            Assert.Less(map.FlattenAt(p), 0.25f * flat, "flatten never decays");
            for (int i = 0; i < 400; i++) map.Advance(0.016f);  // past 8 s
            Assert.AreEqual(0f, map.FlattenAt(p), "texel not empty after 8 s");
            Assert.AreEqual(1f, VegetationPushMap.Release(0f, 8f, 0.7f), 1e-6f);
            Assert.Less(Mathf.Abs(VegetationPushMap.Release(0.6f, 8f, 0.7f)), 0.1f);
        }

        [Test]
        public void ItmeHaritasi_Patlama_CekirdekAnlik_HalkaYayilir_PencereKaymasiTemizler()
        {
            var map = new VegetationPushMap(256, 40f, false);
            map.Recenter(Vector2.zero);
            map.AddBurst(new Vector2(-5f, -5f), 3f, 1f);
            Assert.Greater(map.DisplacementAt(new Vector2(-4f, -5f)), 0.5f, "blast core (r < 0.6 R) not instant");
            Assert.AreEqual(0f, map.DisplacementAt(new Vector2(-2.5f, -5f)), 1e-6f, "ring ahead of its front");
            for (int i = 0; i < 12; i++) { map.Advance(0.016f); map.StepBursts(0.016f); }
            Assert.Greater(map.DisplacementAt(new Vector2(-2.5f, -5f)), 0f, "ring front never arrived at 0.83 R");
            Assert.Greater(map.FlattenAt(new Vector2(-4.5f, -5f)), 1f, "core flatten 1.8 missing");

            // Re-centre far away: the window moves wholesale, old content must not survive.
            map.Recenter(new Vector2(500f, 500f));
            map.Recenter(Vector2.zero);
            Assert.AreEqual(0f, map.DisplacementAt(new Vector2(-4f, -5f)), 1e-6f, "stale texels survived a full window move");
        }
    }
}
