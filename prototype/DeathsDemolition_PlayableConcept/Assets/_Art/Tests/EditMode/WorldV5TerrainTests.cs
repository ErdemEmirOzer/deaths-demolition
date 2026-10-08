using System;
using System.Collections.Generic;
using DD.Art.Pipeline.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-092 B5 · The V6 ground (WorldV5Terrain, DD/GroundTerrain): deterministic, flat wherever the game needs it flat (plaza,
    /// rooms, crossings, gates, weapon points, openings, ponds), only hollows where bodies walk (the runtime's flat effects at
    /// +0.01–0.03 m never sink), relief inside ±0.15 m, seamless chunks, the masks (paths, paving, ruts, moss gardens), roads.json,
    /// the S-07 grass fields and the built scene's contract (box collider kept, renderer gone, chunks on the terrain shader).
    /// The layout tests need no scene; <see cref="Terrain_Sahne_ParcalarVeKutu"/> reads the rebuilt World_V6_Greybox.
    /// </summary>
    [TestFixture]
    [Category("WP092")]
    public sealed class WorldV5TerrainTests
    {
        // Two test ponds shaped like the V6 basins (one turned): the layout tests stay scene-free.
        private static readonly WorldV5Terrain.Pond[] Ponds =
        {
            new WorldV5Terrain.Pond { Centre = new Vector2(-2f, -28f), Semi = new Vector2(7.5f, 5f), YawDeg = 0f },
            new WorldV5Terrain.Pond { Centre = new Vector2(10f, -32f), Semi = new Vector2(4f, 3f), YawDeg = 20f },
        };

        private static WorldV5Terrain.Inputs s_inputs;
        private static WorldV5Terrain.Masks s_masks;

        private static WorldV5Terrain.Inputs Inputs => s_inputs ??= WorldV5Terrain.LayoutInputs(Ponds, null);
        private static WorldV5Terrain.Masks Masks => s_masks ??= WorldV5Terrain.BuildMasks(Inputs);

        private static int IndexA(Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x + WorldV5Terrain.Half) / WorldV5Terrain.TexelA), 0, WorldV5Terrain.MaskASize - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((p.y + WorldV5Terrain.Half) / WorldV5Terrain.TexelA), 0, WorldV5Terrain.MaskASize - 1);
            return y * WorldV5Terrain.MaskASize + x;
        }

        private static int IndexB(Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x + WorldV5Terrain.Half) / WorldV5Terrain.TexelB), 0, WorldV5Terrain.MaskBSize - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((p.y + WorldV5Terrain.Half) / WorldV5Terrain.TexelB), 0, WorldV5Terrain.MaskBSize - 1);
            return y * WorldV5Terrain.MaskBSize + x;
        }

        [Test]
        public void Terrain_Belirlenimci_AyniGirdiAyniZemin()
        {
            WorldV5Terrain.Inputs a = WorldV5Terrain.LayoutInputs(Ponds, null), b = WorldV5Terrain.LayoutInputs(Ponds, null);
            for (float x = -60f; x <= 60f; x += 3.7f)
            for (float z = -60f; z <= 60f; z += 4.1f)
            {
                var p = new Vector2(x, z);
                Assert.AreEqual(WorldV5Terrain.Height(a, p), WorldV5Terrain.Height(b, p), 0f, "yükseklik " + p);
                Assert.AreEqual(WorldV5Terrain.ContactOcclusion(a, p), WorldV5Terrain.ContactOcclusion(b, p), 0f, "temas AO " + p);
            }

            var mesh0 = new Mesh();
            var mesh1 = new Mesh();
            WorldV5Terrain.ChunkMesh(a, 4, 3, mesh0);
            WorldV5Terrain.ChunkMesh(b, 4, 3, mesh1);
            CollectionAssert.AreEqual(mesh0.vertices, mesh1.vertices, "parça köşeleri");
            CollectionAssert.AreEqual(mesh0.triangles, mesh1.triangles, "parça üçgenleri");
            UnityEngine.Object.DestroyImmediate(mesh0);
            UnityEngine.Object.DestroyImmediate(mesh1);

            WorldV5Terrain.Masks m2 = WorldV5Terrain.BuildMasks(b);
            CollectionAssert.AreEqual(Masks.Gravel, m2.Gravel, "maske A.R");
            CollectionAssert.AreEqual(Masks.Paved, m2.Paved, "maske A.A");
            CollectionAssert.AreEqual(Masks.Moss, m2.Moss, "maske B.R");
        }

        [Test]
        public void Terrain_GerekenYerdeDuz()
        {
            var points = new List<(Vector2 p, string what)>();
            foreach (GreyboxV6Builder.Gate g in GreyboxV6Builder.Gates)
                for (int k = 0; k < 8; k++) points.Add((g.Position + new Vector2(Mathf.Cos(k * 0.785f), Mathf.Sin(k * 0.785f)) * 2.4f, "kapı " + g.Id));
            foreach (Vector2 w in GreyboxV6Builder.WeaponPositions) points.Add((w + new Vector2(1.4f, 0f), "silah " + w));
            foreach (Vector2 w in GreyboxV6Builder.ExtraWeaponPositions) points.Add((w, "ek silah " + w));
            foreach (Vector4 r in WorldV5Ground.PlazaRects)
            {
                points.Add((new Vector2((r.x + r.z) * 0.5f, (r.y + r.w) * 0.5f), "plaza"));
                points.Add((new Vector2(r.x - 0.55f, r.y - 0.55f), "plaza köşesi + bordür"));
                points.Add((new Vector2(r.z + 0.55f, r.w + 0.55f), "plaza köşesi + bordür"));
            }

            foreach (GreyboxV6Builder.Room room in GreyboxV6Builder.Rooms)
            {
                points.Add((new Vector2((room.X0 + room.X1) * 0.5f, (room.Z0 + room.Z1) * 0.5f), room.Id));
                if (room.Doors == null) continue;
                foreach (GreyboxV6Builder.Door d in room.Doors)
                {
                    Vector2 at = d.Side == 'S' ? new Vector2(d.At, room.Z0 - 1.4f) : d.Side == 'N' ? new Vector2(d.At, room.Z1 + 1.4f)
                        : d.Side == 'W' ? new Vector2(room.X0 - 1.4f, d.At) : new Vector2(room.X1 + 1.4f, d.At);
                    if (room.Id != "R1_MainHall") points.Add((at, room.Id + " kapı eşiği"));
                }
            }

            foreach (GreyboxV6Builder.Crossing c in GreyboxV6Builder.Crossings)
            {
                points.Add((c.Centre, c.Id));
                points.Add((new Vector2(c.Centre.x, c.Min.y - 1.4f), c.Id + " güney ucu"));
                points.Add((new Vector2(c.Centre.x, c.Max.y + 1.4f), c.Id + " kuzey ucu"));
            }

            foreach (GreyboxV6Builder.Corridor c in GreyboxV6Builder.Corridors)
                points.Add(((c.Path[0] + c.Path[1]) * 0.5f, c.Id));
            points.Add((new Vector2(20f, -23f), "karesansui"));
            foreach (WorldV5Terrain.Pond pond in Ponds) points.Add((pond.Centre + new Vector2(pond.Semi.x + 0.7f, 0f), "göl kıyısı"));   // ≤ 1 m off the rim (the turned pond's rim lies 0.17 m inside Semi.x on its axis)
            foreach ((Vector2 p, string what) in points)
                Assert.AreEqual(0f, WorldV5Terrain.Height(Inputs, p), 0f, what + " düz değil @" + p);
        }

        [Test]
        public void Terrain_Kabartma_SinirIcinde_YurunenYerdeYalnizCukur()
        {
            float min = float.MaxValue, max = float.MinValue;
            int hollows = 0, mounds = 0, walked = 0;
            for (float x = -63f; x <= 63f; x += 0.5f)
            for (float z = -63f; z <= 63f; z += 0.5f)
            {
                var p = new Vector2(x, z);
                float h = WorldV5Terrain.Height(Inputs, p);
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
                if (h > 1e-4f) mounds++;
                if (h < -0.02f) hollows++;
                if (WorldV5Terrain.Unwalked(p) > 0f || Mathf.Abs(x) > 54f || Mathf.Abs(z) > 54f) continue;
                bool nearTrunk = false;
                foreach (WorldV5Terrain.Tree t in Inputs.Trees) if (Vector2.Distance(p, t.Position) < t.Trunk + 1.45f) { nearTrunk = true; break; }
                bool nearWall = false;
                foreach (WorldV5Terrain.Segment w in Inputs.Walls)
                    if (WorldV5Terrain.SegmentDistance(p, w.A, w.B, out _, out _) - w.HalfThickness < 0.4f) { nearWall = true; break; }
                if (nearTrunk || nearWall) continue;
                walked++;
                Assert.LessOrEqual(h, 0f, "yürünen yerde tümsek (yer efektleri gömülür) @" + p);
            }

            Assert.GreaterOrEqual(min, WorldV5Terrain.MinRelief - 1e-5f, "en derin");
            Assert.LessOrEqual(max, WorldV5Terrain.MaxRelief + 1e-5f, "en yüksek");
            Assert.LessOrEqual(max - min, 0.25f, "toplam kabartma");
            Assert.Greater(walked, 20000, "yürünen örnek");
            Assert.Greater(hollows, 2000, "yürünen yerde çukurlar var (düz değil)");
            Assert.Greater(mounds, 2000, "yürünmeyen yerde tümsek var (bambu, kök, duvar dibi)");
            for (float r = WorldV5Terrain.ReliefFadeEnd; r <= WorldV5Terrain.Half; r += 2f)
                Assert.AreEqual(0f, WorldV5Terrain.Height(Inputs, new Vector2(r, r * 0.3f)), 0f, "kaba halka düz (T-ek çatlağı yok)");
        }

        [Test]
        public void Terrain_Parcalar_DikisYok()
        {
            // Fine | fine (x = 0) and fine | coarse (x = 68): the shared edge's heights and normals agree.
            foreach ((int ax, int bx, int cz) in new[] { (4, 5, 4), (8, 9, 4), (4, 5, 2) })
            {
                var a = new Mesh();
                var b = new Mesh();
                WorldV5Terrain.ChunkMesh(Inputs, ax, cz, a);
                WorldV5Terrain.ChunkMesh(Inputs, bx, cz, b);
                float edge = -WorldV5Terrain.Half + bx * WorldV5Terrain.ChunkSize;
                var right = new Dictionary<float, (float h, Vector3 n)>();
                Vector3[] bv = b.vertices, bn = b.normals;
                for (int i = 0; i < bv.Length; i++) if (Mathf.Abs(bv[i].x - edge) < 1e-3f) right[Mathf.Round(bv[i].z * 100f) / 100f] = (bv[i].y, bn[i]);
                Vector3[] av = a.vertices, an = a.normals;
                int shared = 0;
                for (int i = 0; i < av.Length; i++)
                {
                    if (Mathf.Abs(av[i].x - edge) > 1e-3f) continue;
                    if (!right.TryGetValue(Mathf.Round(av[i].z * 100f) / 100f, out (float h, Vector3 n) other))
                    {
                        Assert.AreEqual(0f, av[i].y, 1e-6f, "ince/kaba sınırında ara köşe düz değil");
                        continue;
                    }

                    shared++;
                    Assert.AreEqual(other.h, av[i].y, 1e-6f, "dikişte yükseklik farkı z " + av[i].z);
                    Assert.Less(Vector3.Angle(other.n, an[i]), 0.01f, "dikişte normal farkı z " + av[i].z);
                }

                Assert.Greater(shared, 10, "ortak kenar köşesi");
                Assert.AreEqual(WorldV5Terrain.FineStep, WorldV5Terrain.StepOf(ax, cz), "oyun alanı ince ızgara");
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void Terrain_Maskeler_YolDosemeTekerIziYosun()
        {
            WorldV5Terrain.Masks m = Masks;
            Assert.Greater(m.Gravel[IndexA(new Vector2(20f, 1f))], 0.9f, "bahçe yolu (13,1)→(27,1) çakıl");
            Assert.Greater(m.Dirt[IndexA(new Vector2(-45f, 5f))], 0.9f, "bambu ana yolu toprak");
            Assert.GreaterOrEqual(m.Paved[IndexA(new Vector2(0f, 0f))], WorldV5Terrain.PavedCore, "plaza döşeli");
            float halo = m.Paved[IndexA(new Vector2(-6.88f, -25f))];   // 0.12 m east of the C1 causeway deck (no kerb there)
            Assert.Greater(halo, 0.05f, "döşeme dışı temas halesi");
            Assert.Less(halo, WorldV5Terrain.PavedCore, "hale döşeme değil");
            Assert.Greater(m.Moss[IndexB(new Vector2(36f, -4f))], 0.9f, "R7 yosun bahçesi");
            Assert.Greater(m.Pebble[IndexB(new Vector2(35f, 31.5f))], 0.5f, "R2 damla şeridi çakıl");

            // A rut: the service-yard approach (dirt, ruts) carries mud on a track ±0.55 m off its centre somewhere along it.
            WorldV5Terrain.Road yard = Inputs.Roads.Find(r => r.Ruts);
            Assert.IsNotNull(yard, "tekerlek izli toprak yol");
            float best = 0f;
            for (int k = 0; k + 1 < yard.Points.Length; k++)   // the ruts run in stretches (runs), not on every segment
            {
                Vector2 a = yard.Points[k], b = yard.Points[k + 1], dir = (b - a).normalized, side = new Vector2(-dir.y, dir.x);
                for (float t = 0.05f; t < 0.95f; t += 0.01f)
                foreach (float sign in new[] { 1f, -1f })
                    best = Mathf.Max(best, m.Mud[IndexA(Vector2.Lerp(a, b, t) + side * (sign * WorldV5Terrain.RutOffset))]);
            }

            Assert.Greater(best, 0.8f, "tekerlek izi çamur");

            // Far from every path, paving, yard and pond: open ground (no path material).
            var lawn = new Vector2(42f, -26.5f);
            Assert.Less(m.Gravel[IndexA(lawn)] + m.Paved[IndexA(lawn)], 0.05f, "açık zemin @" + lawn);
            Assert.Less(m.Gravel[IndexA(new Vector2(-75f, -75f))] + m.Dirt[IndexA(new Vector2(-75f, -75f))], 0.02f, "vista: yol yok");

            // Path edges are irregular: the gravel boundary across a straight path moves along it (no ruled edge).
            var edges = new List<float>();
            for (float x = 14f; x < 26f; x += 0.5f)
            {
                float zEdge = 1f;
                for (float z = 1f; z < 3f; z += WorldV5Terrain.TexelA) if (m.Gravel[IndexA(new Vector2(x, z))] > 0.5f) zEdge = z;
                edges.Add(zEdge);
            }

            edges.Sort();
            Assert.Greater(edges[edges.Count - 1] - edges[0], 0.2f, "yol kenarı cetvel düz");
        }

        [Test]
        public void Terrain_RoadsJson_OkunurVeKullanilir()
        {
            const string json = "{\"schema\":\"wp092.roads.v1\",\"roads\":[" +
                                "{\"id\":\"main_n\",\"type\":\"ishidatami\",\"width\":3.5,\"points\":[[0,-30],[0,-45]]}," +
                                "{\"id\":\"cart\",\"type\":\"dirt\",\"width\":2.6,\"points\":[[-40,20],[-30,20],[-30,10]]}," +
                                "{\"id\":\"g1\",\"type\":\"gravel\",\"width\":1.8,\"points\":[[30,-20],[40,-20]]}]," +
                                "\"zones\":[{\"id\":\"market\",\"kind\":\"mud\",\"polygon\":[[40,40],[46,40],[46,46],[40,46]]}]}";
            var roads = new List<WorldV5Terrain.Road>();
            var zones = new List<WorldV5Terrain.Zone>();
            WorldV5Terrain.ParseRoads(json, roads, zones);
            Assert.AreEqual(3, roads.Count);
            Assert.AreEqual(WorldV5Terrain.RoadType.Ishidatami, roads[0].Type);
            Assert.IsTrue(roads[0].Paved);
            Assert.IsTrue(roads[1].Ruts, "≥ 2,2 m toprak yol tekerlek izli");
            Assert.IsFalse(roads[2].Ruts);
            Assert.AreEqual(1, zones.Count);
            Assert.AreEqual(WorldV5Terrain.ZoneKind.Mud, zones[0].Kind);
            Assert.Throws<FormatException>(() => WorldV5Terrain.ParseRoads("{\"schema\":\"x\",\"roads\":[]}", roads, zones), "şema denetimi");
            Assert.Throws<FormatException>(() => WorldV5Terrain.ParseRoads("{\"schema\":\"wp092.roads.v1\",\"roads\":[{\"id\":\"a\",\"type\":\"lava\",\"width\":1,\"points\":[[0,0],[1,1]]}]}", roads, zones), "tür denetimi");

            WorldV5Terrain.Inputs inputs = WorldV5Terrain.LayoutInputs(Ponds, json);
            Assert.AreEqual("roads.json", inputs.RoadSource);
            Assert.AreEqual(3, inputs.Roads.Count, "roads.json varken geçici yollar kullanılmaz");
            WorldV5Terrain.Masks m = WorldV5Terrain.BuildMasks(inputs);
            Assert.GreaterOrEqual(m.Paved[IndexA(new Vector2(0f, -37f))], WorldV5Terrain.PavedCore, "ishidatami örtülü (kit döşeme)");
            Assert.Greater(m.Gravel[IndexA(new Vector2(35f, -20f))], 0.9f, "çakıl yol");
            Assert.Greater(m.Dirt[IndexA(new Vector2(-38.5f, 20f))], 0.9f, "toprak yol (K1 kairō zemininin dışında)");
            Assert.Greater(m.Mud[IndexA(new Vector2(43f, 43f))], 0.6f, "çamur bölgesi");
            Assert.Less(m.Gravel[IndexA(new Vector2(20f, 1f))], 0.1f, "geçici bahçe yolu yok");
        }

        [Test]
        public void Terrain_BitkiMaskeleri_Wp093Sozlesmesi()
        {
            // K/wp-093/MASK-CONTRACT.md (wp093.vegmask.v1): ±55 m, 4 px/m = 440², binary channels, j from the bottom (Unity rows).
            Assert.AreEqual(440, WorldV5Terrain.VegSize);
            WorldV5Terrain.VegMasks v = WorldV5Terrain.BuildVegetationMasks(Inputs);
            int s = WorldV5Terrain.VegSize;
            Color32 A(Vector2 p) => v.A[Mathf.FloorToInt((p.y + 55f) * 4f) * s + Mathf.FloorToInt((p.x + 55f) * 4f)];
            Color32 B(Vector2 p) => v.B[Mathf.FloorToInt((p.y + 55f) * 4f) * s + Mathf.FloorToInt((p.x + 55f) * 4f)];
            foreach (Color32 c in v.A)
            {
                Assert.IsTrue(c.r == 0 || c.r == 255, "A.R ikili (yumuşak kenar yok)");
                Assert.IsTrue(c.g == 0 || c.g == 255, "A.G ikili");
                Assert.IsTrue(c.b == 0 || c.b == 255, "A.B ikili");
                Assert.IsTrue(c.a == 0 || c.a == WorldV5Terrain.VegNotCorridor || c.a == WorldV5Terrain.VegCorridor, "A.A kodu");
            }

            foreach (Color32 c in v.B) Assert.LessOrEqual(c.g, 14, "bölge kimliği 0–14");
            Assert.AreEqual(255, A(new Vector2(20f, 1f)).r, "bahçe yolu yol yüzeyi");
            Assert.AreEqual(255, A(new Vector2(0f, 0f)).r, "plaza yol/kaplama");
            Assert.AreEqual(255, A(new Vector2(-45f, 5f)).r, "bambu yolu");
            Assert.AreEqual(0, A(new Vector2(20f, 4f)).r, "yoldan 3 m: yol değil");
            Assert.AreEqual(255, A(new Vector2(35f, 28f)).g, "R2 iç mekân dışlama");
            Assert.AreEqual(255, A(new Vector2(35f, 24.5f)).g, "R2 damla şeridi (0,5 m) dışlama");
            Assert.AreEqual(255, A(new Vector2(-33f, 2f)).g, "K1 kairō zemini dışlama");
            Assert.AreEqual(255, A(new Vector2(-31f, 34f)).g, "ambar avlusu dövülmüş toprak");
            Assert.AreEqual(0, A(new Vector2(20f, -23f)).g, "karesansui dışlanmaz (Z12)");
            Assert.AreEqual(255, A(Ponds[0].Centre).b, "göl su");
            Assert.AreEqual(0, A(Ponds[0].Centre + new Vector2(Ponds[0].Semi.x + 0.6f, 0f)).b, "kıyı su değil");
            Assert.AreEqual(WorldV5Terrain.VegCorridor, A(new Vector2(42f, -26.5f)).a, "açık bahçe koridor");
            Assert.AreEqual(WorldV5Terrain.VegNotCorridor, A(new Vector2(30f, 54.2f)).a, "halka duvarına 0,8 m: koridor değil");
            Assert.AreEqual(WorldV5Terrain.VegNotCorridor, A(new Vector2(-50f, 10f)).a, "bambu bandı yürünmez");
            Assert.AreEqual(24, B(new Vector2(30f, 54.9f)).r, "halka tsuiji taban izi 24 dm (son sıra)");
            Assert.AreEqual(18, B(new Vector2(-42f, 30f)).r, "ambar çiti 18 dm");
            Assert.AreEqual(0, B(new Vector2(-36f, 30f)).r, "çit kapısı açık");
            Assert.AreEqual(0, B(new Vector2(35f, 24.8f)).r, "yapı duvarı Z6 tohumu değil (Z7)");
            Assert.AreEqual((byte)WorldV5Terrain.VegZone.MossCourt, B(new Vector2(31f, -5f)).g, "R7 Z11");
            Assert.AreEqual((byte)WorldV5Terrain.VegZone.Karesansui, B(new Vector2(20f, -23f)).g, "Z12");
            Assert.AreEqual((byte)WorldV5Terrain.VegZone.Bamboo, B(new Vector2(-50f, 10f)).g, "Z3");
            Assert.AreEqual((byte)WorldV5Terrain.VegZone.PlazaEdge, B(new Vector2(-17.5f, 0f)).g, "Z9");
            Assert.AreEqual((byte)WorldV5Terrain.VegZone.Auto, B(new Vector2(-31f, 34f)).g, "avlunun dövülmüş toprağı dışlama (bölge 0)");
            Assert.AreEqual((byte)WorldV5Terrain.VegZone.Wear, B(new Vector2(24.5f, -7.5f)).g, "Z14 doğma kapısı I_R7SouthWest");
            Assert.AreEqual((byte)WorldV5Terrain.VegZone.Auto, B(new Vector2(20f, 1f)).g, "yolda bölge 0");
            // S-07 (07.10, S07-NOTLAR (4)b): the plaza is zone 13 "Z01 derz", mask_b.B the slate joint lines (255) / faces (1).
            Assert.AreEqual((byte)WorldV5Terrain.VegZone.PlazaJoints, B(new Vector2(0f, 0f)).g, "plaza Z01 derz");
            Assert.AreEqual(255, A(new Vector2(0f, 0f)).r, "plaza döşeme yol yüzeyi kalır");
            int joint = 0, face = 0;
            for (float x = -15.9f; x < 15.9f; x += 0.25f)
            for (float z = -13.9f; z < 19.6f; z += 0.25f)
            {
                Color32 b = B(new Vector2(x, z));
                Assert.AreEqual((byte)WorldV5Terrain.VegZone.PlazaJoints, b.g, "plaza bölgesi @" + x + "," + z);
                if (b.b == WorldV5Terrain.JointDensity) joint++;
                else if (b.b == WorldV5Terrain.FaceDensity) face++;
                else Assert.Fail("plaza yoğunluk kodu " + b.b);
            }

            float share = (float)joint / (joint + face);
            Assert.That(share, Is.InRange(0.12f, 0.6f), "derz hücre payı " + share);
            Assert.AreEqual(v.JointCells, ((Func<int>)(() => { int n = 0; foreach (Color32 c in v.B) if (c.g == 13 && c.b == 255) n++; return n; }))(), "derz sayacı");
            Debug.Log("[DD-TEST] wp092.plaza.joints " + v.JointCells + "/" + v.PlazaCells + " share " + share.ToString("F3"));
            CollectionAssert.AreEqual(v.A, WorldV5Terrain.BuildVegetationMasks(WorldV5Terrain.LayoutInputs(Ponds, null)).A, "belirlenimci");

            Dictionary<string, object> json = ArtJson.AsObject(ArtJson.Parse(WorldV5Terrain.VegJson(Inputs)));
            Assert.AreEqual("wp093.vegmask.v1", ArtJson.AsString(json["schema"]));
            Assert.AreEqual("World_V6_Greybox", ArtJson.AsString(json["scene"]));
            Assert.AreEqual(440, ArtJson.AsInt(json["width"]));
            Assert.AreEqual(-55f, ArtJson.AsFloat(json["minX"]));
            Assert.AreEqual(55f, ArtJson.AsFloat(json["maxZ"]));
            Assert.AreEqual(WorldV5Terrain.VegZoneTable.Length, 12, "tek tablo: 12 boyanan bölge (Z5/Z6 çalışma zamanında)");
            Assert.IsTrue(v.Clear.Exists(c => c.kind == "spawn" && c.r == 3f) && v.Clear.Exists(c => c.kind == "weapon") && v.Clear.Exists(c => c.kind == "door"), "veg_clear noktaları");
            StringAssert.Contains("\"zoneNotes\"", WorldV5Terrain.VegJson(Inputs), "masks.json zoneNotes (13 = Z01 derz)");
        }

        [Test]
        public void Terrain_YerlesimPlani_BolgeTemizlikYeniYapi()
        {
            // wp092.layout.v1 (layout-data.json): zones[].vegZone, vegZoneOverrides[] (polygon or the ring rule) first, clear[] as
            // veg_clear, new buildings excluded (mask_a.G) and flat on the ground; a zone 13 off the plaza is bare (exclusion, zone 0).
            const string layout = "{\"schema\":\"wp092.layout.v1\",\"version\":\"vT\"," +
                "\"zones\":[{\"id\":\"Z14\",\"vegZone\":1,\"polygon\":[[28,-45],[55,-45],[55,20],[28,20]]}," +
                "{\"id\":\"Z21\",\"vegZone\":7,\"polygon\":[[-55,-55],[-26,-55],[-26,-41],[-55,-41]]}]," +
                "\"vegZoneOverrides\":[{\"id\":\"ring_base\",\"vegZone\":6,\"polygon\":null,\"rule\":\"|x| veya |z| ≥ 53,5\"}," +
                "{\"id\":\"monk_yard\",\"vegZone\":13,\"polygon\":[[-54,-46],[-49,-46],[-49,-42],[-54,-42]]}]," +
                "\"clear\":[{\"x\":44,\"z\":44,\"r\":4.0,\"kind\":\"spawn\",\"id\":\"NE_Corner\"},{\"x\":30,\"z\":20,\"r\":1.5,\"kind\":\"weapon\"}]," +
                "\"buildings\":[{\"id\":\"B1\",\"rect\":[40.0,-30.0,44.0,-24.0],\"new\":true,\"doors\":[{\"x\":44.0,\"z\":-27.0}]}," +
                "{\"id\":\"R2\",\"rect\":[30.8,24.8,39.2,31.2],\"new\":false}]}";
            WorldV5Terrain.VegLayout plan = WorldV5Terrain.ParseLayout(layout);
            Assert.AreEqual(2, plan.Zones.Count);
            Assert.AreEqual(53.5f, plan.Overrides[0].ringMin, 1e-4f, "halka kuralı");
            Assert.AreEqual(2, plan.Buildings.Count);
            WorldV5Terrain.Inputs inputs = WorldV5Terrain.LayoutInputs(Ponds, null, layout);
            Assert.IsNotNull(inputs.Layout, "plan girdisi");
            WorldV5Terrain.VegMasks v = WorldV5Terrain.BuildVegetationMasks(inputs);
            int s = WorldV5Terrain.VegSize;
            Color32 A(Vector2 p) => v.A[Mathf.FloorToInt((p.y + 55f) * 4f) * s + Mathf.FloorToInt((p.x + 55f) * 4f)];
            Color32 B(Vector2 p) => v.B[Mathf.FloorToInt((p.y + 55f) * 4f) * s + Mathf.FloorToInt((p.x + 55f) * 4f)];
            Assert.AreEqual(1, B(new Vector2(50f, -36f)).g, "Z14 → 1");
            Assert.AreEqual(6, B(new Vector2(50f, -54f)).g, "halka geçersiz kılma önce (|z| ≥ 53,5)");
            Assert.AreEqual(7, B(new Vector2(-40f, -50f)).g, "Z21 → 7");
            Assert.AreEqual(255, A(new Vector2(-51f, -44f)).g, "plaza dışı bölge 13: çıplak, dışlama");
            Assert.AreEqual(0, B(new Vector2(-51f, -44f)).g, "dışlamada bölge 0");
            Assert.AreEqual(255, A(new Vector2(42f, -27f)).g, "yeni yapı tabanı dışlama");
            Assert.AreEqual(0f, WorldV5Terrain.Height(inputs, new Vector2(42f, -27f)), 0f, "yeni yapı tabanı düz");
            Assert.AreEqual(0f, WorldV5Terrain.Height(inputs, new Vector2(45f, -27f)), 0f, "yeni yapı kapısı düz");
            Assert.AreEqual(2, v.Clear.Count, "veg_clear = plan clear[]");
            Assert.IsTrue(v.Clear.Exists(c => c.id == "NE_Corner" && c.r == 4f && c.kind == "spawn"));
            Assert.AreEqual((byte)WorldV5Terrain.VegZone.PlazaJoints, B(new Vector2(0f, 0f)).g, "plaza yine Z01 derz");
            StringAssert.Contains("layout-data.json vT", WorldV5Terrain.VegJson(inputs));
            WorldV5Terrain.VegLayout copy = WorldV5Terrain.ParseLayout(WorldV5Terrain.LayoutCopyJson(plan, "test"));
            Assert.AreEqual(2, copy.Clear.Count, "kopya yeniden okunur");
            Assert.AreEqual(53.5f, copy.Overrides[0].ringMin, 1e-4f, "kopyada halka kuralı");
            Assert.Throws<FormatException>(() => WorldV5Terrain.ParseLayout("{\"schema\":\"x\"}"), "şema denetimi");
        }

        [Test]
        public void Terrain_UzaklikAlani_Kesin()
        {
            const int size = 23;
            var set = new bool[size * size];
            set[5 * size + 7] = true;
            set[17 * size + 15] = true;
            float[] d = WorldV5Terrain.DistanceField(set, size, 0.5f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float want = Mathf.Min(Mathf.Sqrt((x - 7) * (x - 7) + (y - 5) * (y - 5)), Mathf.Sqrt((x - 15) * (x - 15) + (y - 17) * (y - 17))) * 0.5f;
                Assert.AreEqual(want, d[y * size + x], 1e-4f, "uzaklık (" + x + ", " + y + ")");
            }
        }

        [Test]
        public void Terrain_Golgelendirici_DerlenirVeKurallar()
        {
            // The plaza blot (#4): the sun's mirror point lies in front of the TD camera. Direct and probe specular both stay under a
            // cap (DD_GroundLighting.hlsl DDKnee), the sun-disc roughness floor stays below a mirror, the planar sample is clamped.
            foreach (string name in new[] { WorldV5Terrain.ShaderName, "DD/GroundTiles" })
            {
                Shader shader = Shader.Find(name);
                Assert.IsNotNull(shader, name);
                Assert.IsFalse(ShaderUtil.ShaderHasError(shader), name + " derleme hatası");
                float Default(string property)
                {
                    int index = shader.FindPropertyIndex(property);
                    Assert.GreaterOrEqual(index, 0, name + ": " + property);
                    return shader.GetPropertyDefaultFloatValue(index);
                }

                Assert.LessOrEqual(Default("_DirectSpecMax"), 0.5f, name + ": doğrudan parlama tavanı (plaza beyaz lekesi)");
                Assert.LessOrEqual(Default("_EnvSpecMax"), 2f, name + ": prob gök parlaması tavanı (neon tekerlek izi)");
                Assert.LessOrEqual(Default("_DirectSmoothnessCap"), 0.9f, name + ": güneş diski pürüzlülük tabanı");
            }

            Shader tiles = Shader.Find("DD/GroundTiles");
            Assert.LessOrEqual(tiles.GetPropertyDefaultFloatValue(tiles.FindPropertyIndex("_PlanarMax")), 2f, "düzlemsel yansıma parlaklık tavanı");
            Shader terrain = Shader.Find(WorldV5Terrain.ShaderName);
            Assert.AreEqual(0f, terrain.GetPropertyDefaultFloatValue(terrain.FindPropertyIndex("_DebugView")), "hata ayıklama görünümü kapalı");
            Assert.AreEqual(ShaderPropertyType.Vector, terrain.GetPropertyType(terrain.FindPropertyIndex("_MossTintSun")), "doğrusal ton çarpanı Vector (Color sRGB çözülür)");
        }

        [Test]
        public void Terrain_Taslar_YerlesimKurallari()
        {
            List<WorldV5GroundStones.Stone> stones = WorldV5GroundStones.Place(Inputs);
            Assert.Greater(stones.Count, 800, "taş sayısı");
            Assert.Less(stones.Count, 8000, "taş bütçesi");
            var kinds = new HashSet<WorldV5GroundStones.Kind>();
            foreach (WorldV5GroundStones.Stone st in stones)
            {
                kinds.Add(st.Kind);
                float top = st.Base - 0.2f * st.Height + 1.25f * st.Height;
                Assert.LessOrEqual(top, WorldV5GroundStones.MaxTop + 1e-4f, "taş tepesi " + st.At + " (WorldTdCapture 6 m içerik eşiği 0,15 altında)");
                Assert.GreaterOrEqual(st.Height, WorldV5GroundStones.MinHeight, "taş boyu");
                foreach (Vector4 r in WorldV5Ground.PlazaRects) Assert.Greater(WorldV5Terrain.RectDistance(r, st.At), 0f, "plazada taş " + st.At);
                foreach (GreyboxV6Builder.Gate g in GreyboxV6Builder.Gates) Assert.Greater(Vector2.Distance(g.Position, st.At), 2.5f, "doğma kapısında taş " + st.At);
                foreach (Vector2 w in GreyboxV6Builder.WeaponPositions) Assert.Greater(Vector2.Distance(w, st.At), 1.5f, "silah noktasında taş " + st.At);
                foreach (WorldV5Terrain.Pond pond in Ponds) Assert.Greater(pond.Distance(st.At), 0f, "suda taş " + st.At);
                foreach (GreyboxV6Builder.Crossing c in GreyboxV6Builder.Crossings)
                    Assert.Greater(WorldV5Terrain.RectDistance(new Vector4(c.Min.x, c.Min.y, c.Max.x, c.Max.y), st.At), 0.5f, "köprüde taş " + st.At);
                foreach (WorldV5Terrain.Road road in Inputs.Roads)
                    if (!road.Paved) Assert.Greater(WorldV5Terrain.PolylineDistance(st.At, road.Points, out _) + WorldV5Terrain.EdgeNoise(st.At), road.Width * 0.5f, "yol yüzeyinde taş " + st.At);
                if (st.Kind != WorldV5GroundStones.Kind.DripKerb && st.Kind != WorldV5GroundStones.Kind.Bamboo)
                    Assert.GreaterOrEqual(WorldV5Terrain.FlatWeight(Inputs, st.At), 0.999f, "düz bölgede taş " + st.Kind + " " + st.At);
            }

            foreach (WorldV5GroundStones.Kind k in Enum.GetValues(typeof(WorldV5GroundStones.Kind))) Assert.IsTrue(kinds.Contains(k), "taş türü " + k);
            List<WorldV5GroundStones.Stone> again = WorldV5GroundStones.Place(WorldV5Terrain.LayoutInputs(Ponds, null));
            Assert.AreEqual(stones.Count, again.Count, "belirlenimci");
            for (int i = 0; i < stones.Count; i += 37) Assert.AreEqual(stones[i].At, again[i].At, "belirlenimci " + i);

            for (int v = 0; v < WorldV5GroundStones.Variants; v++)
            {
                (Vector3[] verts, int[] tris) = WorldV5GroundStones.UnitStone(v);
                Assert.LessOrEqual(tris.Length / 3, 80, "taş üçgeni");
                foreach (Vector3 q in verts)
                {
                    Assert.That(q.y, Is.InRange(-1e-4f, 1.0001f));
                    Assert.That(Mathf.Abs(q.x), Is.LessThanOrEqualTo(0.5001f));
                    Assert.That(Mathf.Abs(q.z), Is.LessThanOrEqualTo(0.5001f));
                }
            }
        }

        [Test]
        public void Terrain_Sahne_ParcalarVeKutu()
        {
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);
            Transform box = GameObject.Find(GreyboxV6Builder.GreyboxRootName).transform.Find("Ground");
            Assert.IsNotNull(box, "greybox Ground");
            Assert.IsNotNull(box.GetComponent<BoxCollider>(), "yürünen yüzey ve NavMesh kaynağı kutunun çarpıştırıcısı kalır");
            Assert.IsNull(box.GetComponent<MeshRenderer>(), "kutu çizilmez");

            Transform root = GameObject.Find(WorldV5Dress.RootName).transform.Find(WorldV5Terrain.RootName);
            Assert.IsNotNull(root, WorldV5Terrain.RootName);
            Assert.AreEqual(WorldV5Terrain.ChunksPerSide * WorldV5Terrain.ChunksPerSide, root.childCount, "parça sayısı");
            Material material = null;
            int tris = 0;
            foreach (Transform chunk in root)
            {
                Assert.IsTrue(chunk.name.StartsWith(WorldV5Terrain.ChunkPrefix, StringComparison.Ordinal), chunk.name);
                Assert.AreEqual(0, chunk.GetComponentsInChildren<Collider>(true).Length, chunk.name + ": çarpıştırıcı yok");
                var r = chunk.GetComponent<MeshRenderer>();
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, chunk.name + " gölge düşürmez");
                Assert.AreEqual(LightProbeUsage.Off, r.lightProbeUsage, chunk.name + " pişmiş probları kullanmaz (AD 07.10 M1)");
                Assert.AreEqual(WorldV5Terrain.ShaderName, r.sharedMaterial.shader.name);
                Assert.AreEqual(1, r.sharedMaterials.Length);
                material ??= r.sharedMaterial;
                Assert.AreSame(material, r.sharedMaterial, "tek malzeme (SRP Batcher)");
                tris += chunk.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
            }

            Assert.AreEqual(WorldV5Terrain.MaterialPath, AssetDatabase.GetAssetPath(material));
            foreach (string slot in new[] { "_MaskA", "_MaskB", "_MossMap", "_MossNormal", "_Moss2Map", "_SoilMap", "_SoilNormal", "_DirtMap", "_GravelMap", "_GravelNormal", "_PebbleMap", "_PebbleNormal" })
                Assert.IsNotNull(material.GetTexture(slot), slot);
            Assert.IsFalse(material.enableInstancing);
            Assert.That(tris, Is.InRange(100000, 260000), "üçgen bütçesi");

            var maskA = AssetDatabase.LoadAssetAtPath<Texture2D>(WorldV5Terrain.MaskAPath);
            Assert.AreEqual(WorldV5Terrain.MaskASize, maskA.width);
            var importer = (TextureImporter)AssetImporter.GetAtPath(WorldV5Terrain.MaskAPath);
            Assert.IsFalse(importer.sRGBTexture, "maske doğrusal");
            Assert.IsFalse(importer.GetPlatformTextureSettings("Standalone").overridden, "zemin maskesinde platform geçersiz kılması yok");
            Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode);
            foreach (string path in new[] { WorldV5Terrain.VegMaskAPath, WorldV5Terrain.VegMaskBPath })
            {
                var veg = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(veg, path);
                Assert.IsTrue(veg.isReadable, path + " Read/Write");
                Assert.IsFalse(veg.sRGBTexture, path + " sRGB kapalı");
                Assert.AreEqual(TextureImporterCompression.Uncompressed, veg.textureCompression, path);
                Assert.IsFalse(veg.mipmapEnabled, path + " mip yok");
                Assert.AreEqual(FilterMode.Point, veg.filterMode, path);
                Assert.AreEqual(TextureWrapMode.Clamp, veg.wrapMode, path);
                Assert.IsFalse(veg.GetPlatformTextureSettings("Standalone").overridden, path + ": Standalone geçersiz kılması yok (oyunda da sıkıştırmasız, tam boy)");
                Assert.AreEqual(WorldV5Terrain.VegSize, AssetDatabase.LoadAssetAtPath<Texture2D>(path).width, path);
            }

            Assert.IsNotNull(Resources.Load<TextAsset>("VegetationMasks/" + GreyboxV6Builder.SceneName + "/masks"), "masks.json Resources'ta");
            Assert.IsNotNull(Resources.Load<TextAsset>("WorldRoads/" + GreyboxV6Builder.SceneName), "yol ağı çalışma zamanı kopyası Resources'ta (MASK-CONTRACT §4.3)");

            // The stone layer: one renderer per chunk that holds stones, the kit boulder material, no collider, nothing over the 6 m content line.
            Transform stones = GameObject.Find(WorldV5Dress.RootName).transform.Find(WorldV5GroundStones.RootName);
            Assert.IsNotNull(stones, WorldV5GroundStones.RootName);
            Assert.That(stones.childCount, Is.InRange(10, WorldV5Terrain.ChunksPerSide * WorldV5Terrain.ChunksPerSide), "taş parçaları");
            var stoneMaterial = AssetDatabase.LoadAssetAtPath<Material>(WorldV5GroundStones.MaterialPath);
            foreach (Transform chunk in stones)
            {
                var r = chunk.GetComponent<MeshRenderer>();
                Assert.AreSame(stoneMaterial, r.sharedMaterial, chunk.name);
                Assert.AreEqual(0, chunk.GetComponentsInChildren<Collider>(true).Length, chunk.name + ": çarpıştırıcı yok");
                Assert.Less(r.bounds.max.y, 0.15f, chunk.name + ": 6 m içerik eşiğinin altında");
            }
            Assert.IsNotNull(Resources.Load<Texture2D>("VegetationMasks/" + GreyboxV6Builder.SceneName + "/veg_mask_a"), "veg_mask_a Resources'ta");
        }
    }
}
