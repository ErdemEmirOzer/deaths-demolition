using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Core;
using DD.Prototype.World;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-014 · dd-placement-v1 ↔ Unity parity (ADR-014-1): schema and counts, the axis
    /// contract on real instances (veranda y=33 → z=33; AABB of three instances against the
    /// Blender AABB), assemblies, the per-instance region/navmesh/layer/static assignments in
    /// World_AP005.unity, importer idempotence (instance digest), spawn/weapon parity with the
    /// prototype tables, and the world-source asset. Numbers come from the placement, not from
    /// this file, except the production totals it was delivered with (2354/49/24/12/12/5).
    /// </summary>
    [TestFixture]
    [Category("WP014")]
    public sealed class WorldPlacementTests
    {
        private const float ToleransMetre = 0.01f;

        private static WorldPlacementAsset Placement()
        {
            var asset = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            Assert.IsNotNull(asset, "Placement asset yok: " + ArtLibraryPaths.Ap005PlacementAsset + " (Run World Assembly koşmalı).");
            return asset;
        }

        private static Dictionary<string, object> SourceJson()
        {
            Assert.IsTrue(File.Exists(ArtLibraryPaths.Ap005PlacementJson), "placement.json kopyası yok: " + ArtLibraryPaths.Ap005PlacementJson);
            return ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ArtLibraryPaths.Ap005PlacementJson)), "placement");
        }

        private static Scene OpenWorld()
        {
            Assert.IsTrue(File.Exists(ArtLibraryPaths.Ap005WorldScene), "Dünya sahnesi yok: " + ArtLibraryPaths.Ap005WorldScene);
            return EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
        }

        private static Transform Instances(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == Ap005WorldBootstrap.RootName)
                {
                    Transform t = root.transform.Find(Ap005WorldBootstrap.InstancesName);
                    Assert.IsNotNull(t, "Instances kökü yok.");
                    return t;
                }
            }

            Assert.Fail("'" + Ap005WorldBootstrap.RootName + "' kökü sahnede yok.");
            return null;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(bytes)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ================================================================== şema ve sayılar

        [Test]
        public void Placement_SemaGecerli_Sayilar_3143_49_24_12_12_5_KaynakShaAyni()
        {
            Dictionary<string, object> doc = SourceJson();
            Assert.DoesNotThrow(() => PlacementImporter.Validate(doc), "dd-placement-v1 şema doğrulaması");

            WorldPlacementAsset p = Placement();
            Assert.AreEqual(WorldPlacementAsset.Schema, p.schema);
            Assert.AreEqual("AP-005", p.package);
            // WP-017: v001 source (2354) + AP-005B delta (+202 / ~9 / −169) = 2387; WP-020: + AP-005C delta (+312 / ~23 / −35) = 2664.
            Assert.AreEqual(3143, p.instances.Count, "instances");   // WP-038B: + AP-017 delta v004 (+456); WP-038I: + v005 (+23)
            Assert.AreEqual(49, p.routes.Count, "routes");
            Assert.AreEqual(24, p.markers.Count, "markers");
            Assert.AreEqual(12, p.spawnPoints.Count, "spawn_points");
            Assert.AreEqual(12, p.weaponPoints.Count, "weapon_points");
            Assert.AreEqual(5, p.regions.Count, "regions");
            Assert.AreEqual(ArtJson.AsList(doc["instances"]).Count + p.deltaAdded - p.deltaRemoved, p.instances.Count, "asset ↔ json + delta instance sayısı");
            Assert.AreEqual(Sha256(File.ReadAllBytes(ArtLibraryPaths.Ap005PlacementJson)), p.sourceSha256, "asset kaynağın sha256'sını taşımalı");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorldPlacementAsset.Instance i in p.instances) Assert.IsTrue(ids.Add(i.id), "çift id " + i.id);
            var byRegion = new Dictionary<string, int>();
            foreach (WorldPlacementAsset.Instance i in p.instances) byRegion[i.region] = (byRegion.TryGetValue(i.region, out int n) ? n : 0) + 1;
            Debug.Log("[DD-TEST] wp014.placement instances=" + p.instances.Count + " regions=" + string.Join(",", byRegion));
        }

        // ================================================================== WP-015 · override'lar

        /// <summary>
        /// WP-015 · The overrides file moves exactly the listed instances by exactly its deltas
        /// (Blender → Unity), every other instance is byte-for-byte the source, the asset carries
        /// the overrides' sha256 and ids, no delta exceeds the nudge limit, and the three WP-014
        /// camera-plane culms are the ones listed.
        /// </summary>
        [Test]
        public void Overridelar_WP020_DosyaBos_KatlananDortBambuKaynaktaV003_HicbiriSapmaz()
        {
            string path = ArtLibraryPaths.Ap005PlacementOverridesJson;
            Assert.IsTrue(File.Exists(path), "override dosyası yok: " + path);
            byte[] bytes = File.ReadAllBytes(path);
            Dictionary<string, object> doc = ArtJson.AsObject(ArtJson.Parse(Encoding.UTF8.GetString(bytes)), "overrides");
            Assert.AreEqual(WorldPlacementAsset.OverridesSchema, ArtJson.AsString(doc["schema"]));
            Assert.AreEqual(Path.GetFileName(ArtLibraryPaths.Ap005PlacementJson), ArtJson.AsString(doc["source"]));

            var delta = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (object o in ArtJson.AsList(doc["overrides"], "overrides"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "override");
                List<object> d = ArtJson.AsList(r["position_delta"], "position_delta");
                string id = ArtJson.AsString(r["id"]);
                Assert.IsFalse(delta.ContainsKey(id), "çift override " + id);
                delta[id] = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(d[0]), ArtJson.AsFloat(d[1]), ArtJson.AsFloat(d[2]));
                Assert.LessOrEqual(delta[id].magnitude, PlacementImporter.MaxOverrideDeltaMetres, id + " override sınırı");
                Assert.IsFalse(string.IsNullOrWhiteSpace(ArtJson.AsString(r["reason"])), id + " gerekçesiz");
            }

            // WP-020: AP-005C folded the four WP-014/WP-015 bamboo nudges into the source (delta v003 unity_overrides_folded) — the file stays, empty.
            Assert.AreEqual(0, delta.Count, "override dosyası boş (AP-005C katladı)");
            Dictionary<string, object> v3 = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ArtLibraryPaths.Ap005PlacementDeltaV3Json)), "delta");
            var folded = new List<string>();
            foreach (object o in ArtJson.AsList(v3["unity_overrides_folded"], "unity_overrides_folded")) folded.Add(ArtJson.AsString(ArtJson.AsObject(o, "folded")["id"]));
            CollectionAssert.AreEquivalent(new[] { "AP005_i1532", "AP005_i1535", "AP005_i1605", "AP005_i1549", "AP005_i0048" }, folded, "katlanan kayıtlar: 4 bambu + i0048 materyal override'ı");

            WorldPlacementAsset p = Placement();
            Assert.AreEqual(Sha256(bytes), p.overridesSha256, "asset override dosyasının sha256'sını taşımalı");
            CollectionAssert.AreEqual(new List<string>(delta.Keys), p.overriddenIds, "asset override id listesi dosya sırasında");

            var source = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (object o in ArtJson.AsList(SourceJson()["instances"], "instances"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "instance");
                List<object> pos = ArtJson.AsList(r["position"], "position");
                source[ArtJson.AsString(r["id"])] = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(pos[0]), ArtJson.AsFloat(pos[1]), ArtJson.AsFloat(pos[2]));
            }

            // WP-017/WP-020: the delta chain (added + changed records, later files win) is part of the "source" the overrides sit on.
            foreach (string deltaPath in ArtLibraryPaths.Ap005PlacementDeltaJsons)
            {
                Dictionary<string, object> deltaDoc = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(deltaPath)), "delta");
                foreach (string key in new[] { "added", "changed" })
                foreach (object o in ArtJson.AsList(deltaDoc[key], key))
                {
                    Dictionary<string, object> r = ArtJson.AsObject(o, key);
                    List<object> pos = ArtJson.AsList(r["position"], "position");
                    source[ArtJson.AsString(r["id"])] = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(pos[0]), ArtJson.AsFloat(pos[1]), ArtJson.AsFloat(pos[2]));
                }
            }

            int moved = 0, same = 0;
            foreach (WorldPlacementAsset.Instance i in p.instances)
            {
                Vector3 expected = source[i.id] + (delta.TryGetValue(i.id, out Vector3 d) ? d : Vector3.zero);
                Assert.AreEqual(expected.ToString("F4"), i.position.ToString("F4"), i.id + (delta.ContainsKey(i.id) ? " (override)" : " (kaynak)"));
                if (delta.ContainsKey(i.id)) moved++; else same++;
            }

            Assert.AreEqual(0, moved);
            Assert.AreEqual(p.instances.Count, same);

            // The scene carries the folded (v003) transforms of the four culms: source + the old nudge, now from the delta.
            Transform instances = Instances(OpenWorld());
            foreach (string id in new[] { "AP005_i1532", "AP005_i1535", "AP005_i1605", "AP005_i1549" })
            {
                Transform t = instances.Find(id);
                Assert.IsNotNull(t, id + " sahnede yok");
                Assert.AreEqual(source[id].ToString("F3"), t.position.ToString("F3"), id + " sahne konumu = v003 kaynak (override yok)");
            }

            Debug.Log("[DD-TEST] wp020.overrides applied=" + moved + " folded=" + folded.Count + " sha=" + p.overridesSha256.Substring(0, 12));
        }

        // ================================================================== koordinat sözleşmesi

        [Test]
        public void KoordinatDonusumu_Veranda_Y33_UnityZ33_IlkKayit()
        {
            WorldPlacementAsset p = Placement();
            int veranda = 0;
            foreach (WorldPlacementAsset.Instance i in p.instances)
            {
                if (i.family != "palace_veranda") continue;
                veranda++;
                Assert.AreEqual(33f, i.position.z, 1e-4f, i.id + ": Blender y=33 → Unity z=33");
                Assert.AreEqual(0f, i.position.y, 1e-4f, i.id + ": Blender z=0 → Unity y=0");
            }

            Assert.AreEqual(11, veranda, "11 veranda modülü");

            WorldPlacementAsset.Instance first = p.instances[0];
            Assert.AreEqual("AP005_i0001", first.id);
            Assert.AreEqual(new Vector3(-12f, 0.075f, 33.9062f).ToString("F4"), first.position.ToString("F4"), "(x, y, z)_B → (x, z, y)_U");
            Assert.AreEqual("AP-005", first.ap);
            Assert.AreEqual("palace_screen", first.family);
            Assert.AreEqual("V01", first.variation);

            // Yaw: Blender +Z CCW → Unity −yaw about +Y; +X stays +X and Blender +Y is Unity +Z.
            Quaternion q = WorldPlacementAsset.ToUnityYaw(90f);
            Vector3 x = q * Vector3.right;
            Assert.AreEqual(Vector3.forward.ToString("F3"), x.ToString("F3"), "90° CCW about Blender Z takes +X to Blender +Y = Unity +Z");
        }

        [Test]
        public void UcInstance_PrefabAabb_BlenderAabbIle_1cm()
        {
            WorldPlacementAsset p = Placement();
            Scene scene = OpenWorld();
            Transform instances = Instances(scene);
            var manifests = new Dictionary<string, ArtLibraryManifest>(StringComparer.Ordinal);
            foreach (string ap in ArtLibraryPaths.Packages)
            {
                manifests[ap] = ArtLibraryManifest.FromJson(File.ReadAllText(ArtLibraryPaths.ManifestJson(ap)));
            }

            // Three deterministic picks: a yawed structure, a vegetation instance, a module.
            int[] picks = PickIndices(p);
            float worst = 0f;
            foreach (int index in picks)
            {
                WorldPlacementAsset.Instance inst = p.instances[index];
                Transform t = instances.GetChild(index);
                Assert.AreEqual(inst.id, t.name);

                ArtLibraryManifest.Variation v = FindVariation(manifests[inst.ap], inst.family, inst.variation);
                Assert.IsNotNull(v, inst.lodGroup + " manifestte yok");

                // Expected: Blender local AABB corners → yaw about +Z → +position → AABB → Unity (x, z, y).
                Vector3 bmin = new Vector3(v.boundsMin[0], v.boundsMin[1], v.boundsMin[2]);
                Vector3 bmax = new Vector3(v.boundsMax[0], v.boundsMax[1], v.boundsMax[2]);
                Vector3 bpos = new Vector3(inst.position.x, inst.position.z, inst.position.y);
                var emin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var emax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int k = 0; k < 8; k++)
                {
                    var corner = new Vector3((k & 1) != 0 ? bmax.x : bmin.x, (k & 2) != 0 ? bmax.y : bmin.y, (k & 4) != 0 ? bmax.z : bmin.z);
                    Vector3 r = WorldPlacementAsset.RotateBlender(inst.rotationEuler, corner) + bpos;   // WP-017: full XYZ euler (tilted gable panels)
                    emin = Vector3.Min(emin, r);
                    emax = Vector3.Max(emax, r);
                }

                Vector3 expectedMin = new Vector3(emin.x, emin.z, emin.y);
                Vector3 expectedMax = new Vector3(emax.x, emax.z, emax.y);

                // Measured: LOD0 renderer mesh bounds of every part (Geometry/Roof, not Broken) in world space.
                var renderers = new List<Renderer>();
                foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.EndsWith("LOD0", StringComparison.Ordinal) && child.parent != null && child.parent.name != ArtLibraryPrefabBuilder.BrokenName)
                    {
                        renderers.Add(child.GetComponent<Renderer>());
                    }
                }

                Assert.Greater(renderers.Count, 0, inst.id + ": LOD0 renderer yok");
                Bounds measured = ArtLibraryPrefabBuilder.PrefabSpaceBounds(renderers, t);
                Matrix4x4 m = t.localToWorldMatrix;
                var wmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var wmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int k = 0; k < 8; k++)
                {
                    var corner = new Vector3((k & 1) != 0 ? measured.max.x : measured.min.x, (k & 2) != 0 ? measured.max.y : measured.min.y, (k & 4) != 0 ? measured.max.z : measured.min.z);
                    Vector3 w = m.MultiplyPoint3x4(corner);
                    wmin = Vector3.Min(wmin, w);
                    wmax = Vector3.Max(wmax, w);
                }

                float dev = ArtGateGeometry.MaxSapma(wmin, wmax, expectedMin, expectedMax);
                worst = Mathf.Max(worst, dev);
                Assert.LessOrEqual(dev, ToleransMetre, inst.id + " (" + inst.lodGroup + ", yaw " + inst.yawDegrees + "): sapma " + (dev * 1000f).ToString("F1") + " mm; ölçüm " + wmin + ".." + wmax + " beklenen " + expectedMin + ".." + expectedMax);
            }

            Debug.Log("[DD-TEST] wp014.placement.aabb picks=" + string.Join(",", picks) + " maxDeviationMm=" + (worst * 1000f).ToString("F2"));
        }

        private static int[] PickIndices(WorldPlacementAsset p)
        {
            int yawed = -1, vegetation = -1, module = -1;
            for (int i = 0; i < p.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = p.instances[i];
                if (yawed < 0 && Mathf.Abs(inst.yawDegrees) > 5f && inst.role == "structure") yawed = i;
                if (vegetation < 0 && inst.role == "vegetation" && Mathf.Abs(inst.yawDegrees) > 5f) vegetation = i;
                if (module < 0 && inst.ap == "AP-005" && inst.family == "palace_veranda") module = i;
            }

            Assert.IsTrue(yawed >= 0 && vegetation >= 0 && module >= 0, "seçim: yaw'lı yapı " + yawed + ", bitki " + vegetation + ", modül " + module);
            return new[] { yawed, vegetation, module };
        }

        private static ArtLibraryManifest.Variation FindVariation(ArtLibraryManifest m, string family, string variation)
        {
            foreach (ArtLibraryManifest.Family f in m.families)
            {
                if (f.family != family) continue;
                foreach (ArtLibraryManifest.Variation v in f.variations) if (v.id == variation) return v;
            }

            return null;
        }

        // ================================================================== assembly

        [Test]
        public void Assembly_ParcalarPrefabAltObjeleri()
        {
            WorldPlacementAsset p = Placement();
            Scene scene = OpenWorld();
            Transform instances = Instances(scene);
            int assemblies = 0;
            var hatalar = new List<string>();
            for (int i = 0; i < p.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = p.instances[i];
                if (inst.assembly == null || inst.assembly.Length == 0) continue;
                assemblies++;
                Transform t = instances.GetChild(i);
                foreach (string part in inst.assembly)
                {
                    bool found = false;
                    foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == part + "_LOD0") { found = true; break; }
                    }

                    if (!found) hatalar.Add(inst.id + " " + inst.lodGroup + ": parça '" + part + "' prefab'ta yok");
                }
            }

            Assert.AreEqual(247, assemblies, "assembly[] taşıyan kayıt (v001 242 − v002 kaldırılan 29 + v003 outer-band bambu kümeleri/props 34)");
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
        }

        // ================================================================== sahne atamaları

        [Test]
        public void Sahne_3143Ornek_RegionNavmeshKatmanStatik_NavMeshVerisi()
        {
            WorldPlacementAsset p = Placement();
            Scene scene = OpenWorld();
            Transform instances = Instances(scene);
            Assert.AreEqual(p.instances.Count, instances.childCount, "sahnedeki örnek sayısı");

            int env = PrototypeLayers.Environment, occ = PrototypeLayers.CameraOccluder;
            var hatalar = new List<string>();
            var regionCounts = new Dictionary<string, int>();
            int occluders = 0, statics = 0, ignoreModifiers = 0;
            for (int i = 0; i < p.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = p.instances[i];
                Transform t = instances.GetChild(i);
                if (t.name != inst.id) { hatalar.Add(i + ": ad " + t.name + " ≠ " + inst.id); continue; }
                if (Vector3.Distance(t.position, inst.position) > 1e-3f) hatalar.Add(inst.id + ": konum " + t.position + " ≠ " + inst.position);
                if (Vector3.Distance(t.localScale, inst.scale) > 1e-3f) hatalar.Add(inst.id + ": ölçek");

                var tag = t.GetComponent<RegionTag>();
                if (tag == null || tag.Region != inst.region || tag.NavMesh != inst.navMesh || tag.Role != inst.role) hatalar.Add(inst.id + ": RegionTag eksik/yanlış");
                regionCounts[inst.region] = (regionCounts.TryGetValue(inst.region, out int n) ? n : 0) + 1;

                bool occluder = inst.occluder || inst.fade;
                int expectedLayer = occluder ? occ : env;
                foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
                {
                    if (child.gameObject.layer != expectedLayer) { hatalar.Add(inst.id + ": katman " + LayerMask.LayerToName(child.gameObject.layer) + " (" + child.name + ")"); break; }
                }

                if (occluder) occluders++;

                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                bool isStatic = (flags & StaticEditorFlags.OccludeeStatic) != 0;
                bool isBatched = (flags & StaticEditorFlags.BatchingStatic) != 0;
                bool expectStatic = inst.role == "structure" || inst.role == "ground" || inst.role == "dressing";
                // WP-017: heavy meshes (roof bays / slabs) are static but not batched (scene-file size).
                bool expectBatched = expectStatic && !PlacementImporter.IsHeavyForStaticBatching(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject));
                if (isStatic != expectStatic) hatalar.Add(inst.id + ": static " + isStatic + " (rol " + inst.role + ")");
                if (isBatched != expectBatched) hatalar.Add(inst.id + ": batching " + isBatched + " (beklenen " + expectBatched + ")");
                if (isStatic) statics++;

                var modifier = t.GetComponent<NavMeshModifier>();
                bool hasCollider = t.GetComponentInChildren<Collider>(true) != null;
                bool expectModifier = inst.navMesh == "ignore" && hasCollider;
                if ((modifier != null) != expectModifier) hatalar.Add(inst.id + ": NavMeshModifier " + (modifier != null) + " (navmesh " + inst.navMesh + ", collider " + hasCollider + ")");
                if (modifier != null && !modifier.ignoreFromBuild) hatalar.Add(inst.id + ": modifier ignoreFromBuild kapalı");
                if (modifier != null) ignoreModifiers++;
            }

            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar.GetRange(0, Math.Min(40, hatalar.Count))));
            // WP-017 (AP-005B v002): center 20, north 397, east 630, south 527, west 813; occluder 146 ∪ fade 27 = 150.
            // WP-020 (AP-005C v003): center 20, north 473, east 696, south 589, west 886; occluder 186 ∪ fade 29 = 190.
            // WP-038B (AP-017 v004, +456): center +4, north +75, east +126, south +67, west +120, outer +64; no new occluders.
            // WP-038I (AP-017 Faz 2 v005, +23): north +6, east +5, south +7, west +5; no new occluders.
            Assert.AreEqual(24, regionCounts["center"]); Assert.AreEqual(554, regionCounts["north"]); Assert.AreEqual(827, regionCounts["east"]);
            Assert.AreEqual(663, regionCounts["south"]); Assert.AreEqual(1011, regionCounts["west"]);
            Assert.AreEqual(190, occluders, "occluder_candidate 186 ∪ fade_mesh 29 = 190 (AP-005C v003)");

            NavMeshSurface surface = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                surface = root.GetComponentInChildren<NavMeshSurface>(true);
                if (surface != null) break;
            }

            Assert.IsNotNull(surface, "NavMeshSurface yok");
            Assert.IsNotNull(surface.navMeshData, "NavMesh verisi pişmemiş");
            Assert.AreEqual(ArtLibraryPaths.Ap005NavMeshData, AssetDatabase.GetAssetPath(surface.navMeshData), "NavMesh verisi varlık olarak kaydedilmeli");
            Debug.Log("[DD-TEST] wp014.scene instances=" + instances.childCount + " occluders=" + occluders + " static=" + statics + " ignoreModifiers=" + ignoreModifiers);
        }

        [Test]
        public void Importer_Idempotan_IkinciKosuAyniDigest()
        {
            WorldPlacementAsset p = Placement();
            Scene scene = OpenWorld();
            string once = PlacementImporter.Digest(Instances(scene));
            // WP-020 (WP-039 sanity N1): the re-import rewrites the scene, the NavMesh data and the generated meshes
            // in place (content-equal, but the serialisation churns 200 k lines) — restore the committed bytes after the check
            // so a test run leaves the working tree clean.
            string[] rewritten =
            {
                ArtLibraryPaths.Ap005WorldScene, ArtLibraryPaths.Ap005NavMeshData, ArtLibraryPaths.Ap005GroundMesh,
                Path.GetDirectoryName(ArtLibraryPaths.Ap005GroundMesh).Replace('\\', '/') + "/AP005_PavingGrout_AP005_i0051.asset"
            };
            var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string f in rewritten) if (File.Exists(f)) snapshot[f] = File.ReadAllBytes(f);
            try
            {
                PlacementImporter.ImportReport report = PlacementImporter.BuildScene(p);
                Assert.AreEqual(3143, report.Instances);
                Assert.IsEmpty(report.Unmatched, "eşleşmeyen kayıt");
                Assert.AreEqual(once, report.Digest, "İkinci koşu farklı bir örnek kümesi üretti (idempotency kırık).");
                Scene again = OpenWorld();
                Assert.AreEqual(once, PlacementImporter.Digest(Instances(again)), "Kaydedilen sahnenin digest'i");
                Debug.Log("[DD-TEST] wp014.importer.idempotent digest=" + once.Substring(0, 12) + " navTris=" + report.NavMeshTriangles);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (KeyValuePair<string, byte[]> kv in snapshot)
                {
                    byte[] now = File.ReadAllBytes(kv.Key);
                    if (now.Length == kv.Value.Length && StructuralComparisons.StructuralEqualityComparer.Equals(now, kv.Value)) continue;
                    File.WriteAllBytes(kv.Key, kv.Value);
                    AssetDatabase.ImportAsset(kv.Key, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        // ================================================================== noktalar ve kaynak

        [Test]
        public void SpawnVeSilahNoktalari_PrototipTablosuylaBirebir()
        {
            WorldPlacementAsset p = Placement();
            Assert.AreEqual(WorldMarkers.Spawns.Length, p.spawnPoints.Count);
            for (int i = 0; i < WorldMarkers.Spawns.Length; i++)
            {
                WorldMarkers.SpawnPlacement s = WorldMarkers.Spawns[i];
                WorldPlacementAsset.Point q = p.spawnPoints[i];
                Assert.AreEqual(s.Id, q.id, "spawn " + i);
                Assert.AreEqual(s.X, q.position.x, 1e-3f, s.Id + " x");
                Assert.AreEqual(s.Z, q.position.z, 1e-3f, s.Id + " z");
            }

            Assert.AreEqual(WorldMarkers.Weapons.Length, p.weaponPoints.Count);
            for (int i = 0; i < WorldMarkers.Weapons.Length; i++)
            {
                WorldMarkers.WeaponPlacement w = WorldMarkers.Weapons[i];
                WorldPlacementAsset.Point q = p.weaponPoints[i];
                Assert.AreEqual(w.Name, q.id, "weapon " + i);
                Assert.AreEqual(w.X, q.position.x, 1e-3f, w.Name + " x");
                Assert.AreEqual(w.Z, q.position.z, 1e-3f, w.Name + " z");
                Assert.AreEqual(w.Weight, q.weight, 1e-3f, w.Name + " weight");
            }

            Assert.AreEqual(WorldLayout.PlayerStart.ToString("F3"), p.playerStart.ToString("F3"), "player_start");
            WorldMarkers.MarkerSet set = Ap005WorldBootstrap.MarkerSetFrom(p);
            Assert.AreEqual(12, set.Spawns.Length); Assert.AreEqual(12, set.Weapons.Length);
            Assert.AreEqual(WorldMarkers.Weapons[4].OffRoute, set.Weapons[4].OffRoute, "off-route bayrağı tablodan");
        }

        [Test]
        public void WorldSource_ResourcesVarligi_SahneAdiVeCozumleme()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldSourceSettings>(ArtLibraryPaths.WorldSourceAsset);
            Assert.IsNotNull(settings, "WorldSource varlığı yok: " + ArtLibraryPaths.WorldSourceAsset);
            Assert.AreEqual(ArtLibraryPaths.Ap005WorldSceneName, settings.Ap005SceneName);
            Assert.AreEqual(settings, Resources.Load<WorldSourceSettings>(WorldSourceSettings.ResourceName), "Resources adı");
            string before = Environment.GetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
                Assert.AreEqual(WorldSource.AP005, WorldSourceSettings.Resolve(out string scene));
                Assert.AreEqual(ArtLibraryPaths.Ap005WorldSceneName, scene);
                Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "Procedural");
                Assert.AreEqual(WorldSource.Procedural, WorldSourceSettings.Resolve());
            }
            finally
            {
                Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, before);
            }

            Debug.Log("[DD-TEST] wp014.worldsource asset=" + settings.Source);
        }
    }
}
