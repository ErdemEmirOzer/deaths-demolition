using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    // AP038-specific explicit entry point. No BuildAll, material builder, scene load, bake or PlayMode.
    public static class Ap038BoundedImporter
    {
        const string Ap = "AP-038", Family = "kairo_door", Fbx = "AP023_kairo_door_V02.fbx";
        const string FbxSha = "3f31270ea59a8f2c990a7dae9ea0256e87bfc066bfd7def23f24eb74fb9a16d4";
        [Serializable] public sealed class Receipt
        {
            public string status, fbxSha256, manifestSha256, prefab, error;
            public int importedMeshes, visualMeshes, colliderMeshes, physicalColliders, enabledHeaders;
            public bool importContract, materialFaces, allLods, UnityImportOnly = true;
            public string worldBake = "NOT_RUN", P4Runtime = "NOT_RUN";
        }
        static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException("AP038: " + why); }
        static string Sha(string file)
        {
            using (var h = SHA256.Create()) using (var s = File.OpenRead(file))
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }
        public static void Run()
        {
            string outDir = Environment.GetEnvironmentVariable("DD_AP038_RECEIPT_DIR");
            Require(!string.IsNullOrEmpty(outDir), "receipt directory missing");
            Require(Directory.Exists(outDir), "receipt directory must already exist");
            string receiptPath = Path.Combine(outDir, "unity-import-receipt.json");
            Require(!File.Exists(receiptPath), "receipt already exists");
            var receipt = new Receipt { status = "FAILED_IMPORT_ONLY" };
            int code = 1;
            try
            {
                string manifestPath = ArtLibraryPaths.ManifestJson(Ap);
                string fbxPath = ArtLibraryPaths.Fbx(Ap, Family, Fbx);
                receipt.fbxSha256 = Sha(fbxPath);
                receipt.manifestSha256 = Sha(manifestPath);
                Require(receipt.fbxSha256 == FbxSha, "FBX hash differs");
                Require(receipt.manifestSha256 == Environment.GetEnvironmentVariable("DD_AP038_MANIFEST_SHA"), "manifest hash differs");
                ArtLibraryManifest m = ArtLibraryPrefabBuilder.LoadManifest(Ap);
                Require(m.ap == Ap && m.families.Count == 1, "only AP038 one family allowed");
                var f = m.families[0];
                Require(f.family == Family && f.variations.Count == 1, "one kairo_door only");
                var v = f.variations[0];
                Require(v.id == "V02" && v.prefab == "SM_kairo_door_V02", "only V02 allowed");
                Require(v.parts.Count == 5 && v.colliders.Count == 3, "expected five parts and three collider meshes");
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
                var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                Require(imp != null, "missing ModelImporter");
                var issues = ArtImportContract.ValidateModel(imp);
                Require(issues.Count == 0, "import contract: " + string.Join("; ", issues));
                receipt.importContract = true;
                var meshes = AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath).OfType<Mesh>().ToDictionary(x => x.name);
                receipt.importedMeshes = meshes.Count;
                Require(meshes.Count == 18, "expected18 literal meshes");
                foreach (var part in v.parts)
                {
                    Require(part.lods.Count == 3, "actual five parts each require3LODs");
                    for (int lod = 0; lod < 3; lod++)
                    {
                        var l = part.lods[lod];
                        Require(l.fbx == Fbx && l.@object == "AP023_kairo_door_V02_" + part.name + "_LOD" + lod, "literal mesh name");
                        Require(meshes.TryGetValue(l.@object, out Mesh mesh), "mesh missing " + l.@object);
                        long tris = 0;
                        for (int sub = 0; sub < mesh.subMeshCount; sub++) tris += mesh.GetIndexCount(sub) / 3;
                        Require(tris == l.triangles, "triangle count " + l.@object);
                        Require(l.materials != null && l.facesPerSlot != null && l.materials.Length == l.facesPerSlot.Length, "slot metadata");
                        Require(ArtLibraryPrefabBuilder.MatchSlotsByFaces(mesh, l.materials, l.facesPerSlot) != null, "material face count mapping " + l.@object);
                        foreach (string role in l.materials)
                            Require(role.StartsWith("ap023_", StringComparison.Ordinal) && AssetDatabase.LoadAssetAtPath<Material>("Assets/_Art/Materials/M_" + role + ".mat") != null, "existing material missing " + role);
                        receipt.visualMeshes++;
                    }
                }
                int shellCount = 0;
                foreach (var c in v.colliders)
                {
                    Require(c.fbx == Fbx && meshes.ContainsKey(c.@object) && c.@object.StartsWith("AP023_kairo_door_V02_", StringComparison.Ordinal) && c.@object.EndsWith("_COL", StringComparison.Ordinal), "collider literal mesh");
                    Require(c.shells > 0 && c.shellBoxes != null && c.shellBoxes.Count == c.shells, "actual collider shell data required");
                    shellCount += c.shells;
                    receipt.colliderMeshes++;
                }
                receipt.materialFaces = receipt.allLods = true;
                var index = ArtLibraryPrefabBuilder.BuildLibrary("AP-038");
                Require(index != null && index.ap == Ap && index.items.Count == 1, "index exact single item");
                string path = ArtLibraryPaths.Prefab(Ap, Family, v.prefab);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(prefab != null, "prefab missing");
                Require(prefab.GetComponentsInChildren<MeshRenderer>(true).Length == 15, "15 prefab renderers");
                var physical = prefab.GetComponentsInChildren<Collider>(true);
                receipt.physicalColliders = physical.Length;
                Require(physical.Length == shellCount, "primitive count differs from actual shells");
                foreach (var c in physical)
                {
                    Require(c.enabled && c.gameObject.activeSelf && !c.isTrigger && c is BoxCollider, "enabled box collider required");
                    var box = (BoxCollider)c;
                    Vector3 lo = Vector3.one * float.PositiveInfinity, hi = Vector3.one * float.NegativeInfinity;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var s = new Vector3((corner & 1) == 0 ? -.5f : .5f, (corner & 2) == 0 ? -.5f : .5f, (corner & 4) == 0 ? -.5f : .5f);
                        var q = prefab.transform.InverseTransformPoint(box.transform.TransformPoint(box.center + Vector3.Scale(box.size, s)));
                        lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
                    }
                    if (Mathf.Abs(lo.y - 3.25f) < .002f && Mathf.Abs(hi.y - 3.65f) < .002f && Mathf.Abs(lo.x + 1.065f) < .002f && Mathf.Abs(hi.x - 1.065f) < .002f && Mathf.Abs(lo.z + 2.305f) < .002f && Mathf.Abs(hi.z + 2.165f) < .002f) receipt.enabledHeaders++;
                    Require(!(lo.x < .998f && hi.x > -.998f && lo.z < -1.9f && hi.z > -2.9f && lo.y < 3.248f && hi.y > .252f), "collider intrudes into3m opening");
                }
                Require(receipt.enabledHeaders == 1, "exactly one raised physical header required");
                receipt.prefab = path; receipt.status = "PASS_EXPLICIT_AP038_IMPORT_ONLY"; code = 0;
            }
            catch (Exception e) { receipt.error = e.ToString(); Debug.LogException(e); }
            finally { File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt, true) + "\n"); }
            EditorApplication.Exit(code);
        }
    }
}
