using System;
using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-012 · Asset gate for the imported libraries (AP-002 / AP-003 / AP-003B): manifest ↔
    /// FBX ↔ prefab ↔ index agreement, Import Contract on every library FBX, vertex colours
    /// on the wind carriers, the ADR-012-2 prefab contract (LODGroup, materials by slot,
    /// primitive colliders, layers, roof/broken children), the shared lighting base and the
    /// showcase scene. Numbers come from the manifests, not from this file.
    /// </summary>
    [TestFixture]
    [Category("WP012")]
    public sealed class ArtLibraryGateTests
    {
        private const float SinirToleransMetre = 0.01f;

        private static readonly Dictionary<string, ArtLibraryManifest> Manifestler = new Dictionary<string, ArtLibraryManifest>(StringComparer.Ordinal);

        private static ArtLibraryManifest Manifest(string ap)
        {
            if (!Manifestler.TryGetValue(ap, out ArtLibraryManifest m))
            {
                string yol = ArtLibraryPaths.ManifestJson(ap);
                Assert.IsTrue(File.Exists(yol), "Manifest yok: " + yol);
                m = ArtLibraryManifest.FromJson(File.ReadAllText(yol));
                Manifestler[ap] = m;
            }

            return m;
        }

        private static IEnumerable<(string ap, ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v)> Varyasyonlar()
        {
            foreach (string ap in ArtLibraryPaths.Packages)
            foreach (ArtLibraryManifest.Family f in Manifest(ap).families)
            foreach (ArtLibraryManifest.Variation v in f.variations)
            {
                yield return (ap, f, v);
            }
        }

        private static List<Mesh> Meshler(string yol)
        {
            var list = new List<Mesh>();
            foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(yol))
            {
                if (sub is Mesh m) list.Add(m);
            }

            return list;
        }

        private static Mesh MeshBul(List<Mesh> meshler, string ad)
        {
            Mesh onek = null;
            foreach (Mesh m in meshler)
            {
                if (m.name == ad) return m;
                if (onek == null && m.name.StartsWith(ad, StringComparison.Ordinal)) onek = m;
            }

            return onek;
        }

        // ================================================================== manifestler

        [Test]
        public void Manifestler_UcKutuphane_AileVeVaryasyonSayilari()
        {
            Assert.AreEqual(12, Manifest("AP-002").families.Count, "AP-002 12 aile");
            Assert.AreEqual(12, Manifest("AP-002").VariationCount, "AP-002 12 varyasyon");
            Assert.AreEqual(6, Manifest("AP-003").families.Count, "AP-003 6 aile");
            Assert.AreEqual(42, Manifest("AP-003").VariationCount, "AP-003 42 varyasyon");
            Assert.AreEqual(11, Manifest("AP-003B").families.Count, "AP-003B 11 aile");
            Assert.AreEqual(12, Manifest("AP-005").families.Count, "AP-005 12 aile (WP-014 modüller)");
            Assert.AreEqual(12, Manifest("AP-005").VariationCount, "AP-005 12 varyasyon");
            Assert.AreEqual(53, Manifest("AP-003B").VariationCount, "AP-003B 53 varyasyon");
            Assert.AreEqual(27, Manifest("AP-017").families.Count, "AP-017 27 aile (9 türev + 8 kırılabilir, WP-038B; + 3 prop + 1 kırılabilir, Faz 2 WP-038I; + 5 prop + 1 kırılabilir, Faz 2b WP-038L)");
            Assert.AreEqual(50, Manifest("AP-017").VariationCount, "AP-017 50 varyasyon (38 + 6 Faz 2 + 6 Faz 2b)");
            Assert.AreEqual(59, Manifest("AP-023").families.Count, "AP-023 59 aile (WP-074 B5, manifest v004 44 + B5b-2 v005 7 + v007 8)");
            Assert.AreEqual(192, Manifest("AP-023").VariationCount, "AP-023 192 varyasyon (v004 148 + v005 18 + v007 26)");
            Assert.AreEqual(24, Manifest("AP-030").families.Count, "AP-030 24 aile (WP-092 C, v001 P1: lambalar + hibachi + iç mekân 11; v002: yol 5 + küme 7 + clutter_item 1)");
            Assert.AreEqual(62, Manifest("AP-030").VariationCount, "AP-030 62 varyasyon (v001 22 + v002 40; 6 v001 deltası yerinde)");
            Assert.AreEqual(47, Manifest("AP-040").families.Count, "AP-040 47 aile (WP-108, v001 dünya kiti: plaza 18 + garden 25 + hf 4)");
            Assert.AreEqual(110, Manifest("AP-040").VariationCount, "AP-040 110 varyasyon (plaza 42 + garden 64 + hf 4)");

            var hatalar = new List<string>();
            foreach ((string ap, ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v) in Varyasyonlar())
            {
                if (v.parts.Count == 0) hatalar.Add(ap + " " + v.prefab + ": parça yok");
                foreach (ArtLibraryManifest.Part p in v.parts)
                {
                    // WP-074 B5: a detail part the production LOD rule drops at distance (AP-023 ropes, a fitting) runs LOD0…LODn, n < 2.
                    if (p.lods.Count < 1 || p.lods.Count > 3 || (p.lods.Count < 3 && ap != "AP-023" && ap != "AP-030")) hatalar.Add(ap + " " + v.prefab + " '" + p.name + "': " + p.lods.Count + " LOD");
                    foreach (ArtLibraryManifest.Lod l in p.lods)
                    foreach (string rol in l.materials)
                    {
                        if (!ArtLibraryMaterialRoles.IsKnown(rol) && Array.IndexOf(ArtFamilies.MaterialRoles, rol) < 0)
                            hatalar.Add(ap + " " + v.prefab + ": bilinmeyen malzeme rolü '" + rol + "'");
                    }
                }

                if (v.boundsMin == null || v.boundsMin.Length != 3 || v.boundsMax == null || v.boundsMax.Length != 3)
                    hatalar.Add(ap + " " + v.prefab + ": sınırlar eksik");
            }

            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp012.gate.manifest families=12+6+11+12 variations=12+42+53+12");
        }

        // ================================================================== FBX

        [Test]
        public void HerKutuphaneFbx_SozlesmeyeUyar_MeshAdlariManifestle()
        {
            var hatalar = new List<string>();
            int fbx = 0;
            int mesh = 0;
            var gorulen = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string ap, ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v) in Varyasyonlar())
            {
                var adlar = new List<(string fbx, string obj)>();
                foreach (ArtLibraryManifest.Part p in v.parts)
                foreach (ArtLibraryManifest.Lod l in p.lods) adlar.Add((l.fbx, l.@object));
                foreach (ArtLibraryManifest.Collider c in v.colliders) adlar.Add((c.fbx, c.@object));

                foreach ((string dosya, string obj) in adlar)
                {
                    string yol = ArtLibraryPaths.Fbx(ap, f.family, dosya);
                    if (gorulen.Add(yol))
                    {
                        fbx++;
                        var importer = AssetImporter.GetAtPath(yol) as ModelImporter;
                        if (importer == null) { hatalar.Add(yol + ": ModelImporter yok"); continue; }
                        List<string> ihlal = ArtImportContract.ValidateModel(importer);
                        if (ihlal.Count > 0) hatalar.Add(yol + ": " + string.Join("; ", ihlal));
                    }

                    if (MeshBul(Meshler(yol), obj) == null) hatalar.Add(yol + ": mesh '" + obj + "' yok (" + string.Join(", ", Meshler(yol).ConvertAll(m => m.name)) + ")");
                    else mesh++;
                }
            }

            Assert.AreEqual(575, fbx, "143 + 12 (AP-005) + 6 (AP-005B, WP-017) + 38 (AP-017, WP-038B) + 6 (AP-017 Faz 2, WP-038I) + 6 (Faz 2b, WP-038L) + 192 (AP-023 v007) + 22 (AP-030 v001, WP-092 C) + 40 (AP-030 v002) + 110 (AP-040 v001, WP-108) = 575 kütüphane FBX");
            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp012.gate.fbx contract=ok fbx=" + fbx + " meshRefs=" + mesh);
        }

        [Test]
        public void RuzgarTasiyicilar_VertexRenkTasir_DigerleriTasimaz()
        {
            var hatalar = new List<string>();
            int tasiyan = 0;
            int tasimayan = 0;
            foreach ((string ap, ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v) in Varyasyonlar())
            foreach (ArtLibraryManifest.Part p in v.parts)
            foreach (ArtLibraryManifest.Lod l in p.lods)
            {
                Mesh m = MeshBul(Meshler(ArtLibraryPaths.Fbx(ap, f.family, l.fbx)), l.@object);
                if (m == null) continue;
                bool beklenen = l.windExpected || (l.colourLayers != null && l.colourLayers.Length > 0);
                int renk = m.colors.Length;
                if (beklenen)
                {
                    tasiyan++;
                    if (renk != m.vertexCount) hatalar.Add(ap + " " + l.@object + ": rüzgâr rengi " + renk + "/" + m.vertexCount + " vertex");
                }
                else
                {
                    tasimayan++;
                    if (renk != 0) hatalar.Add(ap + " " + l.@object + ": rüzgâr taşımamalı ama " + renk + " renk var");
                }
            }

            Assert.Greater(tasiyan, 0, "Hiç rüzgâr taşıyıcı yok; tarama yalanlanmıştır.");
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp012.gate.wind carriers=" + tasiyan + " nonCarriers=" + tasimayan);
        }

        /// <summary>
        /// AP-023 M-41 (P-05 30.09, v016): a still part (windExpected false) carries no colour layer or exactly <c>AP023_Mask</c>
        /// (R streak, G moss), and a mask whose G is 1 on every vertex is the old <c>AP023_Streak</c> export slip read as full moss
        /// (kairo_end_chumon V01 up to v015). Same rule as P-05's export round-trip check.
        /// </summary>
        [Test]
        public void Ap023_DuraganParca_RenkKatmaniYalnizMaske_GHerYerdeBirDegil()
        {
            var hatalar = new List<string>();
            int maske = 0;
            foreach (ArtLibraryManifest.Family f in Manifest("AP-023").families)
            foreach (ArtLibraryManifest.Variation v in f.variations)
            foreach (ArtLibraryManifest.Part p in v.parts)
            foreach (ArtLibraryManifest.Lod l in p.lods)
            {
                if (l.windExpected || l.colourLayers == null || l.colourLayers.Length == 0) continue;
                if (l.colourLayers.Length != 1 || l.colourLayers[0] != "AP023_Mask")
                {
                    hatalar.Add(l.@object + ": renk katmanları [" + string.Join(", ", l.colourLayers) + "] — yalnız [] ya da [AP023_Mask]");
                    continue;
                }

                Mesh m = MeshBul(Meshler(ArtLibraryPaths.Fbx("AP-023", f.family, l.fbx)), l.@object);
                if (m == null) continue;   // HerKutuphaneFbx_… reports the missing mesh
                maske++;
                bool hepBir = true;
                foreach (Color c in m.colors)
                {
                    if (c.g < 0.999f) { hepBir = false; break; }
                }

                if (hepBir) hatalar.Add(l.@object + ": AP023_Mask G her vertexte 1 (tam yosun) — Streak dışa aktarım kayması (M-41)");
            }

            Assert.Greater(maske, 0, "Hiç AP023_Mask taşıyan durağan parça yok; tarama yalanlanmıştır.");
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] ap023.gate.m41 maskParts=" + maske);
        }

        // ================================================================== prefab sözleşmesi

        [Test]
        public void HerVaryasyon_Prefab_LodGroup_MalzemeSlotlari_Katman()
        {
            var hatalar = new List<string>();
            int prefabSayisi = 0;
            int renderer = 0;
            foreach ((string ap, ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v) in Varyasyonlar())
            {
                string yol = ArtLibraryPaths.Prefab(ap, f.family, v.prefab);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(yol);
                if (prefab == null) { hatalar.Add(yol + ": yok"); continue; }
                prefabSayisi++;
                string on = ap + " " + v.prefab;

                if (prefab.name != v.prefab) hatalar.Add(on + ": kök adı " + prefab.name);
                int beklenenKatman = f.occluder ? PrototypeLayers.CameraOccluder : PrototypeLayers.Environment;
                if (prefab.layer != beklenenKatman) hatalar.Add(on + ": kök katmanı " + LayerMask.LayerToName(prefab.layer));
                var fade = prefab.GetComponent<OccluderFadeState>();
                if (f.occluder && (fade == null || fade.enabled)) hatalar.Add(on + ": occluder kökünde kapalı OccluderFadeState olmalı");
                if (!f.occluder && fade != null) hatalar.Add(on + ": occluder değil ama OccluderFadeState var");

                Transform geometry = prefab.transform.Find(ArtLibraryPrefabBuilder.GeometryName);
                if (geometry == null) { hatalar.Add(on + ": Geometry yok"); continue; }
                if (Quaternion.Angle(geometry.localRotation, ArtImportContract.GeometryRotation) > 0.001f) hatalar.Add(on + ": Geometry dönüşü " + geometry.localRotation.eulerAngles);

                var lodGroup = prefab.GetComponent<LODGroup>();
                if (lodGroup == null) { hatalar.Add(on + ": LODGroup yok"); continue; }
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length != 3) hatalar.Add(on + ": " + lods.Length + " LOD");
                for (int i = 1; i < lods.Length; i++)
                    if (lods[i].screenRelativeTransitionHeight >= lods[i - 1].screenRelativeTransitionHeight) hatalar.Add(on + ": LOD eşikleri azalan değil");
                if (lodGroup.fadeMode != LODFadeMode.CrossFade) hatalar.Add(on + ": fadeMode " + lodGroup.fadeMode);

                Transform roof = prefab.transform.Find(ArtLibraryPrefabBuilder.RoofName);
                Transform broken = prefab.transform.Find(ArtLibraryPrefabBuilder.BrokenName);
                bool roofBekle = false, brokenBekle = false;
                foreach (ArtLibraryManifest.Part p in v.parts) { roofBekle |= p.IsRoof; brokenBekle |= p.IsBroken; }
                if (roofBekle != (roof != null)) hatalar.Add(on + ": Roof çocuğu " + (roof != null) + " ≠ manifest " + roofBekle);
                if (brokenBekle != (broken != null)) hatalar.Add(on + ": Broken çocuğu " + (broken != null) + " ≠ manifest " + brokenBekle);
                if (roof != null)
                {
                    if (roof.gameObject.layer != PrototypeLayers.CameraOccluder) hatalar.Add(on + ": Roof katmanı " + LayerMask.LayerToName(roof.gameObject.layer));
                    var rf = roof.GetComponent<OccluderFadeState>();
                    if (rf == null || rf.enabled) hatalar.Add(on + ": Roof'ta kapalı OccluderFadeState olmalı (REQ-111)");
                    if (roof.GetComponent<LODGroup>() == null) hatalar.Add(on + ": Roof'un kendi LODGroup'u yok");
                }

                if (broken != null && broken.gameObject.activeSelf) hatalar.Add(on + ": Broken etkin olmamalı (REQ-113)");

                // Malzemeler: her LOD parçası için sub-mesh sırası = manifest slot sırası (yüzü olan slotlar).
                foreach (ArtLibraryManifest.Part p in v.parts)
                {
                    Transform parent = p.IsRoof ? roof : p.IsBroken ? broken : geometry;
                    if (parent == null) continue;
                    for (int lod = 0; lod < p.lods.Count; lod++)   // WP-074 B5: AP-023 detail parts (ropes, a fitting) end before LOD2
                    {
                        string ad = (string.IsNullOrEmpty(p.name) ? string.Empty : p.name + "_") + "LOD" + lod;
                        Transform t = parent.Find(ad);
                        if (t == null) { hatalar.Add(on + ": " + ad + " yok"); continue; }
                        var mr = t.GetComponent<MeshRenderer>();
                        var mf = t.GetComponent<MeshFilter>();
                        if (mr == null || mf == null || mf.sharedMesh == null) { hatalar.Add(on + ": " + ad + " renderer/mesh eksik"); continue; }
                        renderer++;
                        ArtLibraryManifest.Lod l = p.lods[lod];
                        var beklenen = new List<string>();
                        for (int i = 0; i < l.materials.Length; i++)
                            if (l.facesPerSlot == null || i >= l.facesPerSlot.Length || l.facesPerSlot[i] > 0 || l.materials.Length == mf.sharedMesh.subMeshCount) beklenen.Add(l.materials[i]);
                        // DEC-102 AD MUST-C1: the sub-mesh order is the importer's, not always the manifest's (AP-023 roofs came tiles-first);
                        // where the face counts place every sub-mesh, that placement is the expectation — and each sub-mesh's triangles must
                        // equal its slot's face count (the geometry check the old order-only rule missed: the kawara drew wood_planks).
                        string[] yuzle = ArtLibraryPrefabBuilder.MatchSlotsByFaces(mf.sharedMesh, l.materials, l.facesPerSlot);
                        if (yuzle != null) beklenen = new List<string>(yuzle);
                        Material[] mats = mr.sharedMaterials;
                        if (mats.Length != mf.sharedMesh.subMeshCount) hatalar.Add(on + ": " + ad + " " + mats.Length + " malzeme / " + mf.sharedMesh.subMeshCount + " sub-mesh");
                        if (mats.Length != beklenen.Count) hatalar.Add(on + ": " + ad + " " + mats.Length + " malzeme ≠ manifest " + beklenen.Count + " (" + string.Join(",", l.materials) + ")");
                        for (int i = 0; i < mats.Length && i < beklenen.Count; i++)
                        {
                            if (mats[i] == null) { hatalar.Add(on + ": " + ad + " slot " + i + " null"); continue; }
                            if (mats[i].name != "M_" + beklenen[i]) hatalar.Add(on + ": " + ad + " slot " + i + " " + mats[i].name + " ≠ M_" + beklenen[i]);
                            if (mats[i].shader == null || mats[i].shader.name != BeklenenShaderAdi(beklenen[i])) hatalar.Add(on + ": " + ad + " shader " + (mats[i].shader != null ? mats[i].shader.name : "null") + " ≠ " + BeklenenShaderAdi(beklenen[i]));
                        }

                        // WP-038G: AP-017 litter never casts; WP-038L: the underwater koi neither; WP-108: AP-040 obeys AP040_shadow_cast.
                        bool ap040GolgeYok = ap == ArtAp040WorldKit.Package && !ArtAp040WorldKit.CastsShadow(f.family, v.id);
                        ShadowCastingMode golge = lod == 2 || f.IsDerivative || v.HasSwim || ap040GolgeYok ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        if (mr.shadowCastingMode != golge) hatalar.Add(on + ": " + ad + " gölge " + mr.shadowCastingMode + " (ADR-012-1: " + golge + ")");
                    }
                }
            }

            Assert.AreEqual(539, prefabSayisi, "12 + 42 + 53 + 12 (AP-005) + 6 (AP-005B) + 50 (AP-017, Faz 1+2+2b) + 192 (AP-023 v007) + 62 (AP-030 v001 + v002, WP-092 C) + 110 (AP-040 v001, WP-108) = 539 varyasyon prefabı");
            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp012.gate.prefab count=" + prefabSayisi + " renderers=" + renderer);
        }

        [Test]
        public void Colliderlar_YalnizPrimitif_ShellSayisiManifestle_TriggerSinifi()
        {
            var hatalar = new List<string>();
            int box = 0, capsule = 0, trigger = 0;
            foreach ((string ap, ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v) in Varyasyonlar())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtLibraryPaths.Prefab(ap, f.family, v.prefab));
                if (prefab == null) continue;
                string on = ap + " " + v.prefab;
                if (prefab.GetComponentsInChildren<MeshCollider>(true).Length > 0) hatalar.Add(on + ": MeshCollider yasak");
                Collider[] hepsi = prefab.GetComponentsInChildren<Collider>(true);
                int beklenenShell = 0;
                int beklenenTrigger = 0;
                int beklenenProxy = 0;   // WP-038B: AP-017 `none` footprints build no collider
                foreach (ArtLibraryManifest.Collider c in v.colliders)
                {
                    if (c.IsNone) continue;
                    beklenenProxy++;
                    beklenenShell += Math.Max(1, c.shells);
                    if (c.IsTrigger) beklenenTrigger += Math.Max(1, c.shells);
                }

                if (beklenenProxy > 0 && hepsi.Length == 0) hatalar.Add(on + ": collider bekleniyordu, yok");
                if (beklenenProxy == 0 && hepsi.Length > 0) hatalar.Add(on + ": collider beklenmiyordu, " + hepsi.Length + " var");
                // AP-003B beyan ettiği shell sayısıyla birebir; AP-002/AP-003 tek shell beyan eder (roof envelope birden fazla olabilir).
                if (ap == "AP-003B" && hepsi.Length != beklenenShell) hatalar.Add(on + ": " + hepsi.Length + " collider ≠ " + beklenenShell + " shell");
                if (ap != "AP-003B" && hepsi.Length < beklenenProxy) hatalar.Add(on + ": " + hepsi.Length + " collider < " + beklenenProxy + " proxy");
                int t = 0;
                foreach (Collider c in hepsi)
                {
                    if (c is BoxCollider) box++;
                    else if (c is CapsuleCollider) capsule++;
                    else hatalar.Add(on + ": " + c.GetType().Name + " primitif değil");
                    if (c.isTrigger) { t++; trigger++; }
                    // Collider.bounds dünya uzayıdır ve sahnede olmayan prefab varlığında sıfırdır; boyut alanları okunur.
                    if (c is BoxCollider bc && bc.size.magnitude <= 0f) hatalar.Add(on + ": sıfır boyutlu kutu " + c.name);
                    if (c is CapsuleCollider cc && (cc.radius <= 0f || cc.height <= 0f)) hatalar.Add(on + ": sıfır boyutlu kapsül " + c.name);
                }

                if (ap == "AP-003B" && t != beklenenTrigger) hatalar.Add(on + ": trigger " + t + " ≠ " + beklenenTrigger);
            }

            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Assert.Greater(box, 0);
            Debug.Log("[DD-TEST] wp012.gate.colliders box=" + box + " capsule=" + capsule + " trigger=" + trigger);
        }

        [Test]
        public void PrefabUzayiAabb_ManifestSinirlariyla_1cm()
        {
            var hatalar = new List<string>();
            int sayi = 0;
            float enBuyuk = 0f;
            foreach ((string ap, ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v) in Varyasyonlar())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtLibraryPaths.Prefab(ap, f.family, v.prefab));
                if (prefab == null) continue;
                var renderers = new List<Renderer>();
                foreach (ArtLibraryManifest.Part p in v.parts)
                {
                    if (p.IsBroken) continue; // manifest sınırları bozuk durumu kapsamaz
                    Transform parent = p.IsRoof ? prefab.transform.Find(ArtLibraryPrefabBuilder.RoofName) : prefab.transform.Find(ArtLibraryPrefabBuilder.GeometryName);
                    Transform t = parent != null ? parent.Find((string.IsNullOrEmpty(p.name) ? string.Empty : p.name + "_") + "LOD0") : null;
                    if (t != null) renderers.Add(t.GetComponent<Renderer>());
                }

                if (renderers.Count == 0) { hatalar.Add(ap + " " + v.prefab + ": LOD0 renderer yok"); continue; }
                Bounds olcum = ArtLibraryPrefabBuilder.PrefabSpaceBounds(renderers, prefab.transform);
                Bounds beklenen = ArtLibraryManifest.PrefabBounds(v);
                float sapma = ArtGateGeometry.MaxSapma(olcum.min, olcum.max, beklenen.min, beklenen.max);
                enBuyuk = Mathf.Max(enBuyuk, sapma);
                sayi++;
                if (sapma > SinirToleransMetre) hatalar.Add(ap + " " + v.prefab + ": sapma " + (sapma * 1000f).ToString("0.0") + " mm (ölçüm " + olcum.min + ".." + olcum.max + ", manifest " + beklenen.min + ".." + beklenen.max + ")");
            }

            Assert.AreEqual(539, sayi);   // WP-038I: + 6 AP-017 Phase 2 variations; WP-038L: + 6 Phase 2b; WP-074 B5: + 148 AP-023; B5b-2: + 18 v005; v007: + 26; WP-092 C: + 22 AP-030 v001, + 40 v002; WP-108: + 110 AP-040
            Assert.IsEmpty(hatalar, "Eksen sözleşmesi ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp012.gate.aabb variations=" + sayi + " maxDeviationMm=" + (enBuyuk * 1000f).ToString("0.00"));
        }

        [Test]
        public void Indeks_HerKutuphane_TumVaryasyonlarPrefabaCozulur()
        {
            foreach (string ap in ArtLibraryPaths.Packages)
            {
                var index = AssetDatabase.LoadAssetAtPath<ArtLibraryAsset>(ArtLibraryPaths.IndexAsset(ap));
                Assert.IsNotNull(index, ap + " indeksi yok");
                Assert.AreEqual(ap, index.ap);
                ArtLibraryManifest m = Manifest(ap);
                Assert.AreEqual(m.VariationCount, index.items.Count, ap + " indeks girişleri");
                foreach (ArtLibraryManifest.Family f in m.families)
                foreach (ArtLibraryManifest.Variation v in f.variations)
                {
                    GameObject p = index.Find(f.family, v.id);
                    Assert.IsNotNull(p, ap + " " + f.family + " " + v.id + " indekste çözülmüyor");
                    Assert.AreEqual(v.prefab, p.name);
                }
            }
        }

        [Test]
        public void PrefabUretici_Idempotent_IkinciKosuAyniDosya()
        {
            ArtLibraryManifest m = Manifest("AP-003B");
            ArtLibraryManifest.Family f = m.families.Find(x => x.family == "bridge_segment");
            Assert.IsNotNull(f);
            ArtLibraryManifest.Variation v = f.variations[0];
            string yol = ArtLibraryPaths.Prefab("AP-003B", f.family, v.prefab);
            Assume.That(File.Exists(yol), "Ön koşul: prefab üretilmiş olmalı.");
            string once = File.ReadAllText(yol);
            ArtLibraryPrefabBuilder.Build("AP-003B", f, v);
            AssetDatabase.SaveAssets();
            string sonra = File.ReadAllText(yol);
            Assert.AreEqual(once, sonra, "İkinci üretim prefab dosyasını değiştirdi (idempotency kırık).");
        }

        /// <summary>WP-013: a role's shader is the row's ShaderKind (Lit, or the wind/water shaders); AP-001 roles are Lit.</summary>
        private static string BeklenenShaderAdi(string rol)
        {
            ArtLibraryMaterialRoles.Spec spec = ArtLibraryMaterialRoles.Get(rol);
            if (spec == null) return ArtMaterialBuilder.LitShaderName;
            switch (spec.Shader)
            {
                case ArtLibraryMaterialRoles.ShaderKind.FoliageWind: return ArtMaterialBuilder.FoliageWindShaderName;
                case ArtLibraryMaterialRoles.ShaderKind.Water: return ArtMaterialBuilder.WaterShaderName;
                default: return ArtMaterialBuilder.LitShaderName;
            }
        }

        // ================================================================== malzeme ve doku

        [Test]
        public void KutuphaneMalzemeleri_HerRolVar_UrpLit_CutoutVeDokular()
        {
            var hatalar = new List<string>();
            foreach (ArtLibraryMaterialRoles.Spec spec in ArtLibraryMaterialRoles.All)
            {
                string yol = ArtPipelinePaths.Material(spec.Role);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(yol);
                if (mat == null) { hatalar.Add(yol + ": yok"); continue; }
                if (mat.shader == null || mat.shader.name != BeklenenShaderAdi(spec.Role)) hatalar.Add(yol + ": shader " + (mat.shader != null ? mat.shader.name : "null") + " ≠ " + BeklenenShaderAdi(spec.Role));
                if (spec.Shader == ArtLibraryMaterialRoles.ShaderKind.Water) continue;   // WP-013: su v1 kapısı ArtVfxGateTests'te (saydam, ayrı sözleşme)
                bool cutout = spec.Kind == ArtLibraryMaterialRoles.Kind.Cutout || spec.AlphaCutout;   // WP-038L: the koi card sheet (BaseColor-only cutout)
                if (mat.IsKeywordEnabled("_ALPHATEST_ON") != cutout) hatalar.Add(yol + ": _ALPHATEST_ON " + !cutout);
                if (cutout && !spec.SingleSided && mat.GetFloat("_Cull") != 0f) hatalar.Add(yol + ": cutout çift taraflı olmalı (_Cull 0)");
                if (cutout && spec.SingleSided && mat.GetFloat("_Cull") != 2f) hatalar.Add(yol + ": tek taraflı cutout (v008 sugi taç pedi) _Cull 2 olmalı");
                if (!cutout && !spec.DoubleSided && mat.GetFloat("_Cull") != 2f) hatalar.Add(yol + ": _Cull " + mat.GetFloat("_Cull"));
                if (!cutout && spec.DoubleSided && mat.GetFloat("_Cull") != 0f) hatalar.Add(yol + ": çift yüzlü opak rol (WP-108 AP-040 flora) _Cull 0 olmalı, " + mat.GetFloat("_Cull"));
                if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")) hatalar.Add(yol + ": saydam olmamalı");
                if (spec.HasBaseColorMap && mat.GetTexture("_BaseMap") == null) hatalar.Add(yol + ": _BaseMap yok");
                if (!spec.HasBaseColorMap && mat.GetTexture("_BaseMap") != null) hatalar.Add(yol + ": düz renk rolünde doku var");
                if (spec.HasNormalMap && (mat.GetTexture("_BumpMap") == null || !mat.IsKeywordEnabled("_NORMALMAP"))) hatalar.Add(yol + ": normal map/keyword yok");
                if (spec.HasPackedMap && (mat.GetTexture("_MetallicGlossMap") == null || !mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))) hatalar.Add(yol + ": maske/keyword yok");
                // WP-108: a ready URP mask (AP-040 MaskMap) binds as delivered — the row's own map, not a derived one.
                if (spec.MaskMapReady)
                {
                    Texture maske = mat.GetTexture("_MetallicGlossMap");
                    if (maske == null || !mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP")) hatalar.Add(yol + ": hazır maske/keyword yok");
                    else if (AssetDatabase.GetAssetPath(maske) != spec.TexturePath("MaskMap")) hatalar.Add(yol + ": _MetallicGlossMap " + AssetDatabase.GetAssetPath(maske) + " ≠ " + spec.TexturePath("MaskMap"));
                }

                if (spec.HasBaseColorMap)
                {
                    Texture tex = mat.GetTexture("_BaseMap");
                    string beklenen = spec.TexturePath("BaseColor");   // WP-108: T_<TextureRole>_BaseColor, or the row's external set (the AP-040 shared atlas)
                    if (tex != null && AssetDatabase.GetAssetPath(tex) != beklenen) hatalar.Add(yol + ": _BaseMap " + AssetDatabase.GetAssetPath(tex) + " ≠ " + beklenen);
                }

                Color tint = mat.GetColor("_BaseColor").linear;
                Color beklenenTint = spec.Kind == ArtLibraryMaterialRoles.Kind.Flat ? spec.FlatLinearAlbedo : spec.Tint;
                if (Mathf.Abs(tint.r - beklenenTint.r) > 0.01f || Mathf.Abs(tint.g - beklenenTint.g) > 0.01f || Mathf.Abs(tint.b - beklenenTint.b) > 0.01f)
                    hatalar.Add(yol + ": _BaseColor lineer " + tint + " ≠ " + beklenenTint);
            }

            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp012.gate.materials roles=" + ArtLibraryMaterialRoles.All.Count);
        }

        [Test]
        public void CutoutBaseColor_AlfaTutar_DigerBaseColorTutmaz()
        {
            var cut = AssetImporter.GetAtPath(ArtPipelinePaths.SourceTexture("green_foliage_01", "BaseColor")) as TextureImporter;
            var opak = AssetImporter.GetAtPath(ArtPipelinePaths.SourceTexture("bark_01", "BaseColor")) as TextureImporter;
            Assert.IsNotNull(cut); Assert.IsNotNull(opak);
            Assert.AreEqual(TextureImporterAlphaSource.FromInput, cut.alphaSource, "cutout BaseColor alfa kaynağı");
            Assert.IsTrue(cut.alphaIsTransparency);
            Assert.AreEqual(TextureImporterAlphaSource.None, opak.alphaSource, "opak BaseColor alfa taşımaz");
            Assert.IsTrue(ArtImportContract.KeepsCutoutAlpha(cut.assetPath));
            Assert.IsFalse(ArtImportContract.KeepsCutoutAlpha(opak.assetPath));
            Assert.AreEqual("green_foliage_01", ArtImportContract.RoleSlugOf(cut.assetPath));
            Assert.IsEmpty(ArtImportContract.ValidateTexture(cut, ArtImportContract.TextureRole.BaseColor));
            Assert.IsEmpty(ArtImportContract.ValidateTexture(opak, ArtImportContract.TextureRole.BaseColor));
        }

        // ================================================================== ışık tabanı ve gök (ADR-011-4 kapanışı)

        [Test]
        public void IsikTabani_PaylasilanProfilVeGok_RunVePilotAyniVarligi()
        {
            var profil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtLibraryPaths.OvercastVolumeProfile);
            Assert.IsNotNull(profil, "Profil yok: " + ArtLibraryPaths.OvercastVolumeProfile);
            Assert.AreEqual(6, profil.components.Count, "Tonemapping/ColorAdjustments/WhiteBalance/Vignette/Bloom + ColorLookup (WP-029 look LUT)");
            foreach (VolumeComponent c in profil.components) Assert.AreEqual(ArtLibraryPaths.OvercastVolumeProfile, AssetDatabase.GetAssetPath(c), c.GetType().Name + " alt-varlık değil");

            var gok = AssetDatabase.LoadAssetAtPath<Material>(ArtLibraryPaths.SkyMaterial);
            Assert.IsNotNull(gok, "Gök yok: " + ArtLibraryPaths.SkyMaterial);
            Assert.AreEqual(ArtSkyBuilder.SkyShaderName, gok.shader.name);
            Assert.AreEqual(ArtLibraryPaths.SkyResourceName, WorldAtmosphere.SharedSkyResourceName, "Prototype ve Art aynı Resources adını kullanmalı");
            Assert.AreEqual(gok, Resources.Load<Material>(WorldAtmosphere.SharedSkyResourceName), "Resources.Load aynı varlığı vermeli");

            string profilGuid = AssetDatabase.AssetPathToGUID(ArtLibraryPaths.OvercastVolumeProfile);
            string gokGuid = AssetDatabase.AssetPathToGUID(ArtLibraryPaths.SkyMaterial);
            foreach (string sahne in new[] { ArtLightingBuilder.RunScenePath, ArtPipelinePaths.PilotScene })
            {
                string metin = File.ReadAllText(sahne);
                Assert.IsTrue(metin.Contains(profilGuid), sahne + " paylaşılan profili referanslamıyor");
                Assert.IsTrue(metin.Contains("m_SkyboxMaterial: {fileID: 2100000, guid: " + gokGuid), sahne + " paylaşılan göğü referanslamıyor");
                Assert.IsFalse(metin.Contains("World/OvercastSky"), sahne + " hâlâ gömülü prosedürel gök taşıyor");
            }
        }

        // ================================================================== vitrin sahnesi

        [Test]
        public void VitrinSahnesi_DiskteVar_BuildDisi_TumVaryasyonlarOrnek()
        {
            Assert.IsTrue(File.Exists(ArtLibraryPaths.ShowcaseScene), "Vitrin sahnesi yok: " + ArtLibraryPaths.ShowcaseScene);
            // WP-015 (ADR-015-2): listed but DISABLED = dev-only; never an enabled (release) entry.
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.path == ArtLibraryPaths.ShowcaseScene) Assert.IsFalse(s.enabled, "Vitrin release listesine (etkin) girmez.");

            string metin = File.ReadAllText(ArtLibraryPaths.ShowcaseScene);
            int toplam = 0;
            foreach ((string ap, ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v) in Varyasyonlar())
            {
                string guid = AssetDatabase.AssetPathToGUID(ArtLibraryPaths.Prefab(ap, f.family, v.prefab));
                Assert.IsTrue(metin.Contains(guid), ap + " " + v.prefab + " vitrinde yok");
                toplam++;
            }

            Assert.AreEqual(539, toplam);   // WP-038I: + 6 AP-017 Phase 2 variations; WP-038L: + 6 Phase 2b; WP-074 B5: + 148 AP-023; B5b-2: + 18 v005; v007: + 26; WP-092 C: + 22 AP-030 v001, + 40 v002; WP-108: + 110 AP-040
        }

        [Test]
        public void Faz2b_Koi_Furin_BalikTuzagi_CaySeti_OzelKurallar()
        {
            // WP-038L · AP-017 Phase 2b: the koi shoal swims (ArtSwimLoop from the FBX custom props), is a cutout card sheet with no
            // shadow and no collider; the furin hangs on the wind shader with no collider; the fish trap LOD1/2 bind their own albedo;
            // the tea set is a breakable on the existing BreakCeramic binder.
            string Prefab(string family) => ArtLibraryPaths.Prefab("AP-017", family, "SM_" + family + "_V01");
            var koi = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab("koi_shoal"));
            Assert.IsNotNull(koi, "koi prefab");
            ArtSwimLoop swim = koi.GetComponent<ArtSwimLoop>();
            Assert.IsNotNull(swim, "koi ArtSwimLoop");
            Assert.AreEqual(0.8f, swim.RadiusMin, 1e-4f); Assert.AreEqual(1.6f, swim.RadiusMax, 1e-4f); Assert.AreEqual(0.15f, swim.Speed, 1e-4f);
            foreach (Renderer r in koi.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, r.name + ": koi gölge düşürmez");
                Assert.IsTrue(r.sharedMaterial != null && r.sharedMaterial.IsKeywordEnabled("_ALPHATEST_ON"), r.name + ": koi cutout");
            }
            Assert.AreEqual(0, koi.GetComponentsInChildren<Collider>(true).Length, "koi collider yok");
            var koiAlbedo = AssetImporter.GetAtPath(ArtPipelinePaths.SourceTexture("prop_koi_shoal", "BaseColor")) as TextureImporter;
            Assert.IsNotNull(koiAlbedo);
            Assert.AreEqual(TextureImporterAlphaSource.FromInput, koiAlbedo.alphaSource, "koi BaseColor alfası korunur (cutout)");

            var furin = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab("furin_wind_chime"));
            Assert.IsNotNull(furin, "furin prefab");
            Assert.AreEqual(0, furin.GetComponentsInChildren<Collider>(true).Length, "furin collider yok");
            foreach (Renderer r in furin.GetComponentsInChildren<Renderer>(true))
                Assert.AreEqual("DD/Foliage Wind", r.sharedMaterial.shader.name, r.name + ": furin rüzgâr yolu");

            var trap = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab("fish_trap"));
            Assert.IsNotNull(trap, "fish trap prefab");
            LOD[] lods = trap.GetComponentInChildren<LODGroup>(true).GetLODs();
            Assert.AreEqual("M_prop_fish_trap", lods[0].renderers[0].sharedMaterial.name);
            Assert.AreEqual("M_prop_fish_trap_lod", lods[1].renderers[0].sharedMaterial.name);
            Assert.AreEqual("M_prop_fish_trap_lod", lods[2].renderers[0].sharedMaterial.name);

            var tea = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab("breakable_tea_set"));
            Assert.IsNotNull(tea, "tea set prefab");
            var breakable = tea.GetComponent<DD.Prototype.Props.BreakableProp>();
            Assert.IsNotNull(breakable, "çay seti kırılabilir binder");
            Assert.AreEqual("BreakCeramic", breakable.EventId);
        }
    }
}
