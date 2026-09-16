using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-017 · AP-005B delta import (ADR-017-2): the delta file (dd-placement-delta-v1) applied on
    /// the v001 source gives exactly the v002 world — removed ids absent, added ids present with
    /// the delta's transform (full XYZ euler: the eight tilted gable panels), changed ids carrying
    /// the new transform, the asset recording the delta's sha256 and counts; every
    /// <c>material_override</c> mapped (plaster copies ×0.82, roof copy with the tile-course
    /// normal, stone kerbs) and applied on every renderer slot of the instance in the scene; the
    /// AP-005B prop prefabs resolvable; the WP-015 overrides still on their instances.
    /// </summary>
    [TestFixture]
    [Category("WP017")]
    public sealed class WorldDeltaTests
    {
        private static WorldPlacementAsset Placement()
        {
            var p = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            Assert.IsNotNull(p, "placement asset yok");
            return p;
        }

        private static Dictionary<string, object> Delta(out byte[] bytes)
        {
            string path = ArtLibraryPaths.Ap005PlacementDeltaJson;
            Assert.IsTrue(File.Exists(path), "delta dosyası yok: " + path);
            bytes = File.ReadAllBytes(path);
            return ArtJson.AsObject(ArtJson.Parse(Encoding.UTF8.GetString(bytes)), "delta");
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

        private static Transform Instances()
        {
            Scene scene = EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != Ap005WorldBootstrap.RootName) continue;
                Transform t = root.transform.Find(Ap005WorldBootstrap.InstancesName);
                Assert.IsNotNull(t);
                return t;
            }

            Assert.Fail("dünya kökü yok");
            return null;
        }

        [Test]
        public void Delta_Sema_Sayilar_AssetSha_KaldirilanYok_EklenenVar()
        {
            Dictionary<string, object> d = Delta(out byte[] bytes);
            Assert.AreEqual(WorldPlacementAsset.DeltaSchema, ArtJson.AsString(d["schema"]));
            Assert.AreEqual("AP-005B", ArtJson.AsString(d["package"]));
            Dictionary<string, object> counts = ArtJson.AsObject(d["counts"], "counts");
            Assert.AreEqual(202, ArtJson.AsInt(counts["added"])); Assert.AreEqual(9, ArtJson.AsInt(counts["changed"])); Assert.AreEqual(169, ArtJson.AsInt(counts["removed"]));
            Assert.AreEqual(2387, ArtJson.AsInt(counts["instances_v002"]));

            WorldPlacementAsset p = Placement();
            Assert.AreEqual(Sha256(bytes), p.deltaSha256, "asset delta sha256");
            Assert.AreEqual(202, p.deltaAdded); Assert.AreEqual(9, p.deltaChanged); Assert.AreEqual(169, p.deltaRemoved);
            Assert.AreEqual(2387, p.instances.Count);

            var byId = new Dictionary<string, WorldPlacementAsset.Instance>(StringComparer.Ordinal);
            foreach (WorldPlacementAsset.Instance i in p.instances) byId[i.id] = i;
            var removed = new List<string>();
            foreach (object o in ArtJson.AsList(d["removed_ids"], "removed_ids")) removed.Add(ArtJson.AsString(o));
            Assert.AreEqual(169, removed.Count);
            CollectionAssert.AreEquivalent(removed, p.deltaRemovedIds);
            foreach (string id in removed) Assert.IsFalse(byId.ContainsKey(id), id + " kaldırılmış olmalı");

            // Order: the v001 survivors keep their order, the added records follow (deterministic digest).
            int firstAdded = -1;
            for (int i = 0; i < p.instances.Count; i++)
            {
                if (p.instances[i].deltaVersion == WorldPlacementAsset.DeltaVersionTag && p.instances[i].id.CompareTo("AP005_i2354") > 0) { firstAdded = i; break; }
            }

            Assert.AreEqual(2354 - 169, firstAdded, "eklenenler v001 kalanlarının ardında");
            Debug.Log("[DD-TEST] wp017.delta added=" + p.deltaAdded + " changed=" + p.deltaChanged + " removed=" + p.deltaRemoved + " instances=" + p.instances.Count + " target=" + p.deltaTarget);
        }

        [Test]
        public void Delta_EklenenVeDegisen_Transform_Paritesi_TamEuler_8EgikPanel()
        {
            Dictionary<string, object> d = Delta(out _);
            WorldPlacementAsset p = Placement();
            var byId = new Dictionary<string, WorldPlacementAsset.Instance>(StringComparer.Ordinal);
            foreach (WorldPlacementAsset.Instance i in p.instances) byId[i.id] = i;
            var overrides = new HashSet<string>(p.overriddenIds, StringComparer.Ordinal);

            var hatalar = new List<string>();
            int tilted = 0, checkedCount = 0, overrideCount = 0;
            foreach (string key in new[] { "added", "changed" })
            foreach (object o in ArtJson.AsList(d[key], key))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, key);
                string id = ArtJson.AsString(r["id"]);
                if (!byId.TryGetValue(id, out WorldPlacementAsset.Instance inst)) { hatalar.Add(id + " (" + key + ") asset'te yok"); continue; }
                Assert.IsFalse(overrides.Contains(id), id + ": delta kaydı override ile çakışmamalı");
                List<object> pos = ArtJson.AsList(r["position"], "position");
                List<object> rot = ArtJson.AsList(r["rotation_euler_deg"], "rotation");
                Vector3 expected = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(pos[0]), ArtJson.AsFloat(pos[1]), ArtJson.AsFloat(pos[2]));
                if (Vector3.Distance(expected, inst.position) > 1e-4f) hatalar.Add(id + ": konum " + inst.position + " ≠ " + expected);
                var euler = new Vector3(ArtJson.AsFloat(rot[0]), ArtJson.AsFloat(rot[1]), ArtJson.AsFloat(rot[2]));
                if (Vector3.Distance(euler, inst.rotationEuler) > 1e-4f) hatalar.Add(id + ": euler " + inst.rotationEuler + " ≠ " + euler);
                if (Mathf.Abs(inst.yawDegrees - euler.z) > 1e-4f) hatalar.Add(id + ": yaw");
                if (inst.deltaVersion != WorldPlacementAsset.DeltaVersionTag) hatalar.Add(id + ": deltaVersion '" + inst.deltaVersion + "'");
                if (inst.IsTilted) tilted++;
                string mo = r.ContainsKey("material_override") ? ArtJson.AsString(r["material_override"]) : string.Empty;
                if (mo != (inst.materialOverride ?? string.Empty)) hatalar.Add(id + ": material_override '" + inst.materialOverride + "' ≠ '" + mo + "'");
                if (!string.IsNullOrEmpty(mo)) { overrideCount++; if (!PlacementImporter.MaterialOverrides.ContainsKey(mo)) hatalar.Add(id + ": eşlenmemiş override " + mo); }
                Assert.AreEqual(ArtJson.AsString(r["lod_group"]), inst.lodGroup, id);
                checkedCount++;
            }

            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Assert.AreEqual(211, checkedCount);
            Assert.AreEqual(8, tilted, "AP-005B: sekiz eğik alınlık paneli (Y ekseni)");
            Assert.AreEqual(100, overrideCount, "material_override taşıyan delta kaydı");

            // The rotation map: (0, 0, yaw) is the WP-014 yaw rule; a Y tilt lands on Unity's Z with the sign flipped.
            Assert.AreEqual(WorldPlacementAsset.ToUnityYaw(37f).eulerAngles.ToString("F3"), WorldPlacementAsset.ToUnityRotation(new Vector3(0f, 0f, 37f)).eulerAngles.ToString("F3"));
            Quaternion tilt = WorldPlacementAsset.ToUnityRotation(new Vector3(0f, -36f, 90f));
            Vector3 blenderUp = WorldPlacementAsset.RotateBlender(new Vector3(0f, -36f, 90f), Vector3.forward);   // Blender +Z after the tilt
            Vector3 unityUp = tilt * Vector3.up;
            Assert.Less(Vector3.Distance(unityUp, WorldPlacementAsset.ToUnity(blenderUp.x, blenderUp.y, blenderUp.z)), 1e-4f, "Blender ↔ Unity dönüş paritesi (eğik panel)");
            Debug.Log("[DD-TEST] wp017.delta.transforms checked=" + checkedCount + " tilted=" + tilted + " overrides=" + overrideCount);
        }

        [Test]
        public void Delta_MateryalOverride_SahnedeTumSlotlarda_DunyaMateryalleri()
        {
            WorldPlacementAsset p = Placement();
            Transform instances = Instances();
            var hatalar = new List<string>();
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < p.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = p.instances[i];
                Transform t = instances.GetChild(i);
                Assert.AreEqual(inst.id, t.name);
                if (string.IsNullOrEmpty(inst.materialOverride)) continue;
                PlacementImporter.MaterialOverride over = PlacementImporter.MaterialOverrides[inst.materialOverride];
                counts[inst.materialOverride] = (counts.TryGetValue(inst.materialOverride, out int n) ? n : 0) + 1;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        string role = m.name.Substring(2);
                        bool isTarget = over.Map.ContainsValue(role);
                        bool wasSource = over.Map.ContainsKey(role) || over.Map.ContainsKey("*");
                        if (wasSource && !isTarget) hatalar.Add(inst.id + " " + r.name + ": slot hâlâ " + m.name + " (override " + inst.materialOverride + ")");
                    }
                }

                if (!inst.IsTilted) continue;
                float dev = Quaternion.Angle(t.rotation, inst.Rotation);
                if (dev > 0.01f) hatalar.Add(inst.id + ": sahne dönüşü " + dev.ToString("F3") + "° sapma");
            }

            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Assert.AreEqual(74, counts["AP005_M_plaster_world"]); Assert.AreEqual(21, counts["AP005_M_roof_world"]);
            Assert.AreEqual(4, counts["AP002_M_stone_v009_build_v003"]); Assert.AreEqual(1, counts["AP005_M_plaster_world_ap001"]);

            // The world materials: plaster copies carry the ×0.82 linear tint on the same maps; the roof copy the tile-course detail normal.
            var plasterWorld = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("plaster_hf003_world"));
            var plaster = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("plaster_hf003"));
            Assert.IsNotNull(plasterWorld); Assert.IsNotNull(plaster);
            Assert.AreEqual(plaster.GetTexture("_BaseMap"), plasterWorld.GetTexture("_BaseMap"), "aynı albedo");
            Color tint = plasterWorld.GetColor("_BaseColor");
            Color expectedTint = ArtMaterialBuilder.EncodeForColorProperty(new Color(ArtLibraryMaterialRoles.WorldPlasterTint, ArtLibraryMaterialRoles.WorldPlasterTint, ArtLibraryMaterialRoles.WorldPlasterTint, 1f));
            Assert.Less(Mathf.Abs(tint.r - expectedTint.r) + Mathf.Abs(tint.g - expectedTint.g) + Mathf.Abs(tint.b - expectedTint.b), 0.01f, "plaster_world tint ×0.82 (lineer)");
            var roofWorld = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("roof_world"));
            Assert.IsNotNull(roofWorld);
            Assert.IsTrue(roofWorld.IsKeywordEnabled("_DETAIL_MULX2"), "roof_world detay katmanı");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPipelinePaths.DerivedTexture(ArtLibraryMaterialRoles.RoofCoursesTextureRole, "Normal")), roofWorld.GetTexture("_DetailNormalMap"), "kiremit sırası normal'i");
            Assert.IsNull(roofWorld.GetTexture("_DetailAlbedoMap"), "detay albedo yok (nötr gri)");
            Debug.Log("[DD-TEST] wp017.delta.materials " + string.Join(" ", counts.Keys) + " plasterTint=" + tint.r.ToString("F3"));
        }

        [Test]
        public void Ap005B_PropAileleri_6Prefab_Kutuphanede_YerlesimdeCozulur()
        {
            var index = AssetDatabase.LoadAssetAtPath<ArtLibraryAsset>(ArtLibraryPaths.IndexAsset("AP-005B"));
            Assert.IsNotNull(index, "AP-005B kütüphane indeksi yok");
            Assert.AreEqual(6, index.items.Count);
            WorldPlacementAsset p = Placement();
            var byFamily = new Dictionary<string, int>();
            foreach (WorldPlacementAsset.Instance i in p.instances)
            {
                if (i.ap != "AP-005B") continue;
                Assert.IsNotNull(index.Find(i.family, i.variation), i.id + " " + i.lodGroup + " prefab yok");
                byFamily[i.family] = (byFamily.TryGetValue(i.family, out int n) ? n : 0) + 1;
            }

            Assert.AreEqual(2, byFamily["tsukubai"]); Assert.AreEqual(2, byFamily["handcart"]); Assert.AreEqual(4, byFamily["strawbales"]);
            Assert.AreEqual(2, byFamily["barrels"]); Assert.AreEqual(5, byFamily["mossrock"]); Assert.AreEqual(1, byFamily["shed"]);
            foreach (string prop in new[] { "tsukubai", "handcart", "strawbales", "barrels", "mossrock", "shed" })
            {
                string role = "prop_" + prop;
                Assert.AreEqual(ArtImportContract.TextureClass.Prop, ArtImportContract.ClassOfRole(role), role + " Prop sınıfı");
                var m = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material(role));
                Assert.IsNotNull(m, "M_" + role);
                Assert.IsNotNull(m.GetTexture("_BaseMap"), role + " albedo");
                Assert.IsNotNull(m.GetTexture("_BumpMap"), role + " normal");
                Assert.IsNotNull(m.GetTexture("_MetallicGlossMap"), role + " türetilmiş maske");
                var importer = AssetImporter.GetAtPath(ArtPipelinePaths.SourceTexture(role, "BaseColor")) as TextureImporter;
                Assert.IsNotNull(importer);
                Assert.AreEqual(512, importer.maxTextureSize, role + " 512 (ADR-015-1 Prop)");
            }

            Debug.Log("[DD-TEST] wp017.props families=6 instances=" + string.Join(",", byFamily));
        }
    }
}
