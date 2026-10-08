using System;
using System.Collections.Generic;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.World.Vegetation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-093 · AP-031 v001 intake (P-05 08.10-1): every kit mesh, read the way the runtime reads it (VegetationKit.ExtractLod —
    /// the LOD node's transform applied, Y-up), passes the vc1 acceptance (root vertices y ≤ 1 cm → R ≤ 0.05; per-object max R
    /// ≥ 0.95, moss R = 0; nothing below the pivot; height = the manifest's) and the AP-031-SPEC triangle ceilings; the texture
    /// and model importers carry the vegetation rules (Alpha Is Transparency off, coverage mips at 0.45, aniso 4, normal maps,
    /// linear ORM, ground detail 1024, readable models without UV2); the kit binds all 10 species and the draw set uses it; and
    /// the edit-mode preview of the V6 world is the runtime placement (same field function, same instances).
    /// </summary>
    [TestFixture]
    [Category("WP093")]
    public sealed class Wp093VegetationKitTests
    {
        // AP-031-SPEC §2 (LOD0, LOD1, LOD2) by VegetationType.
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

        private static VegetationKit Kit()
        {
            var kit = AssetDatabase.LoadAssetAtPath<VegetationKit>(ArtVegetationIntake.KitAsset);
            Assert.IsNotNull(kit, ArtVegetationIntake.KitAsset + " yok");
            Assert.AreSame(kit, VegetationKit.Load(), "Resources.Load(DD_VegetationKit) başka bir varlık döndürdü");
            return kit;
        }

        [Test]
        public void Kit_On10TuruBaglar_VaryantSayilariTarifle()
        {
            VegetationKit kit = Kit();
            VegetationRecipe recipe = VegetationRecipe.Load();
            for (int t = 0; t < VegetationTypes.Count; t++)
            {
                var type = (VegetationType)t;
                VegetationKit.Entry e = kit.Find(type);
                Assert.IsNotNull(e, VegetationTypes.Codes[t] + ": kit girdisi yok");
                Assert.IsNotNull(e.material, VegetationTypes.Codes[t] + ": malzeme yok");
                Assert.AreEqual("DD/World Vegetation", e.material.shader.name, VegetationTypes.Codes[t] + ": malzeme gölgelendiricisi");
                Assert.IsNotNull(e.material.GetTexture("_BaseMap"), VegetationTypes.Codes[t] + ": atlas bağlı değil");
                if (type == VegetationType.LeafLitter)
                {
                    Assert.IsFalse(e.HasModels);
                    Assert.AreEqual(12, e.cards.Length, "momiji manifesti 8 tek + 4 küme kart");
                    int clusters = 0;
                    foreach (VegetationKit.Card c in e.cards)
                    {
                        if (c.cluster) clusters++;
                        Assert.That(c.uv.x, Is.LessThan(c.uv.z));
                        Assert.That(c.uv.y, Is.LessThan(c.uv.w));
                        Assert.That(c.size.x, Is.InRange(0.04f, 0.3f), "kart gerçek boyu (m)");
                    }

                    Assert.AreEqual(4, clusters);
                    continue;
                }

                Assert.AreEqual(recipe.Get(type).variants, e.models.Length, VegetationTypes.Codes[t] + ": varyant sayısı tarifle aynı değil");
                foreach (GameObject m in e.models) Assert.IsNotNull(m, VegetationTypes.Codes[t] + ": model referansı boş");
            }

            var draw = new VegetationDrawSet(recipe, kit);
            try
            {
                for (int t = 0; t < VegetationTypes.Count; t++)
                {
                    Assert.IsTrue(draw.UsesKit((VegetationType)t), VegetationTypes.Codes[t] + ": çizim seti yer tutucuya düştü");
                    Assert.Greater(draw.NominalHeights[t][0], 0.002f);
                }
            }
            finally
            {
                draw.Dispose();
            }
        }

        [Test]
        public void KitMeshleri_Vc1Kabulu_PivotYukseklik_UcgenTavanlari()
        {
            VegetationKit kit = Kit();
            var bad = new List<string>();
            int checkedMeshes = 0;
            for (int t = 0; t < VegetationTypes.Count; t++)
            {
                var type = (VegetationType)t;
                VegetationKit.Entry e = kit.Find(type);
                if (e == null || !e.HasModels) continue;
                bool moss = type == VegetationType.Moss;
                foreach (GameObject model in e.models)
                {
                    int lods = VegetationKit.LodCount(model);
                    Assert.GreaterOrEqual(lods, 2, model.name + ": LOD0 + LOD1 yok");
                    for (int l = 0; l < lods; l++)
                    {
                        Mesh mesh = VegetationKit.ExtractLod(model, l);
                        _made.Add(mesh);
                        checkedMeshes++;
                        string id = model.name + "_LOD" + l;
                        Vector3[] v = mesh.vertices;
                        Color[] c = mesh.colors;
                        if (c.Length != v.Length) { bad.Add(id + ": vc1 renk katmanı yok"); continue; }
                        float minY = float.MaxValue, maxY = float.MinValue, maxR = 0f, rootR = 0f;
                        for (int i = 0; i < v.Length; i++)
                        {
                            minY = Mathf.Min(minY, v[i].y);
                            maxY = Mathf.Max(maxY, v[i].y);
                            maxR = Mathf.Max(maxR, c[i].r);
                            if (v[i].y <= 0.01f) rootR = Mathf.Max(rootR, c[i].r);
                        }

                        if (minY < -0.005f) bad.Add(id + ": pivotun altında geometri (min y " + minY.ToString("F3") + ") — eksen dönüşümü yanlış?");
                        if (rootR > 0.05f) bad.Add(id + ": kök köşelerinde R " + rootR.ToString("F3") + " > 0,05");
                        if (moss ? maxR > 0.05f : maxR < 0.95f) bad.Add(id + ": nesne başına max R " + maxR.ToString("F3") + (moss ? " (yosun R = 0)" : " < 0,95"));
                        Dictionary<string, object> rec = ArtVegetationIntake.ObjectRecord(model.name, l);
                        if (rec != null)
                        {
                            float h = ArtJson.AsFloat(ArtJson.AsObject(rec["vc1"], "vc1")["height_m"]);
                            if (Mathf.Abs(maxY - h) > Mathf.Max(0.005f, h * 0.03f)) bad.Add(id + ": boy " + maxY.ToString("F3") + " ≠ manifest " + h.ToString("F3") + " (Y yukarı değil?)");
                            int mt = ArtJson.AsInt(rec["tris"]);
                            if (mesh.triangles.Length / 3 != mt) bad.Add(id + ": üçgen " + mesh.triangles.Length / 3 + " ≠ manifest " + mt);
                        }

                        int tris = mesh.triangles.Length / 3;
                        int cap = Ceilings[t, Mathf.Min(l, 2)];
                        if (cap > 0 && tris > cap) bad.Add(id + ": " + tris + " üçgen > AP-031 tavanı " + cap);
                        if (l == 2 && cap == 0) bad.Add(id + ": bu türün LOD2'si sözleşmede yok");
                    }
                }
            }

            Debug.Log("[DD-TEST] wp093.kit meshes=" + checkedMeshes);
            Assert.GreaterOrEqual(checkedMeshes, 19 * 2);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void IceAktarma_DokularVeModeller_BitkiKurallari()
        {
            var bad = new List<string>();
            List<string> textures = ArtVegetationIntake.TextureFiles();
            Assert.AreEqual(ArtVegetationIntake.ExpectedTextures, textures.Count);
            foreach (string path in textures)
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) { bad.Add(path + ": içe aktarılmamış"); continue; }
                ArtImportContract.TextureRole role = ArtImportContract.RoleOf(path);
                string slug = ArtImportContract.RoleSlugOf(path);
                if (ti.alphaIsTransparency) bad.Add(path + ": Alpha Is Transparency açık");
                if (ti.anisoLevel != 4) bad.Add(path + ": aniso " + ti.anisoLevel);
                if (role == ArtImportContract.TextureRole.Normal && ti.textureType != TextureImporterType.NormalMap) bad.Add(path + ": normal haritası değil");
                if (role == ArtImportContract.TextureRole.Orm && ti.sRGBTexture) bad.Add(path + ": ORM sRGB");
                if (role == ArtImportContract.TextureRole.BaseColor && !ti.sRGBTexture) bad.Add(path + ": BaseColor lineer");
                if (role == ArtImportContract.TextureRole.BaseColor && slug.StartsWith("VEG_", StringComparison.Ordinal))
                {
                    if (!ti.mipMapsPreserveCoverage) bad.Add(path + ": Mip Maps Preserve Coverage kapalı");
                    if (Mathf.Abs(ti.alphaTestReferenceValue - 0.45f) > 1e-4f) bad.Add(path + ": kapsama kesimi " + ti.alphaTestReferenceValue);
                    if (ti.alphaSource != TextureImporterAlphaSource.FromInput) bad.Add(path + ": alfa kaynağı " + ti.alphaSource);
                }

                if (slug.StartsWith("GroundDetail_", StringComparison.Ordinal) && ti.maxTextureSize != 1024) bad.Add(path + ": zemin ayrıntısı Max Size " + ti.maxTextureSize);
                List<string> v = ArtImportContract.ValidateTexture(ti, role);
                if (v.Count > 0) bad.Add(path + ": " + string.Join("; ", v));
            }

            List<string> models = ArtVegetationIntake.ModelFiles();
            Assert.AreEqual(ArtVegetationIntake.ExpectedFbx, models.Count);
            foreach (string path in models)
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { bad.Add(path + ": içe aktarılmamış"); continue; }
                if (!mi.isReadable) bad.Add(path + ": Read/Write kapalı (çalışma zamanı LOD kopyası okuyamaz)");
                if (mi.generateSecondaryUV) bad.Add(path + ": lightmap UV üretiyor");
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) bad.Add(path + ": malzeme içe aktarıyor");
                if (mi.addCollider) bad.Add(path + ": collider ekliyor");
                if (Math.Abs(mi.globalScale - 1f) > 1e-6f) bad.Add(path + ": ölçek " + mi.globalScale);
                List<string> v = ArtImportContract.ValidateModel(mi);
                if (v.Count > 0) bad.Add(path + ": " + string.Join("; ", v));
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root != null && root.GetComponentInChildren<Collider>(true) != null) bad.Add(path + ": collider var");
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        // ------------------------------------------------------------------ preview == runtime (V6 world, edit mode)

        [Test]
        public void V6Onizleme_CalismaZamaniYerlesimiyleAyni_YaprakVeDerzYolu()
        {
            EditorSceneManager.OpenScene(ArtLibraryPaths.GreyboxV6Scene, OpenSceneMode.Single);
            Scene scene = SceneManager.GetActiveScene();
            VegetationWorldHookup.Register();
            VegetationRecipe recipe = VegetationRecipe.Load();
            VegetationDensityField a = VegetationWorldFields.Build(scene, recipe);
            VegetationDensityField b = VegetationWorldFields.Build(scene, recipe);
            Assert.IsNotNull(a, "V6 sahnesi alan üretmedi");
            for (int t = 0; t < VegetationTypes.Count; t++) Assert.AreEqual(a.Layers[t], b.Layers[t], "aynı sahne iki kez farklı alan: " + VegetationTypes.Codes[t]);

            using (var preview = (VegetationPreview.Handle)VegetationPreview.Populate(scene, VegetationPreview.TierHigh))
            {
                var draw = new VegetationDrawSet(recipe, VegetationKit.Load());
                try
                {
                    VegetationLayout runtime = VegetationPlacer.Place(a, recipe, recipe.TierDensity(VegetationPreview.TierHigh), DD.Prototype.World.Vegetation.VegetationSystem.Seed, draw.NominalHeights);
                    VegetationLayout shown = preview.Layout;
                    Assert.IsNotNull(shown);
                    Assert.AreEqual(runtime.TotalInstances, shown.TotalInstances, "önizleme çalışma zamanından farklı sayıda örnek yerleştirdi");
                    Assert.AreEqual(runtime.Groups.Length, shown.Groups.Length);
                    for (int g = 0; g < runtime.Groups.Length; g++)
                    {
                        Matrix4x4[] x = runtime.Groups[g].Matrices, y = shown.Groups[g].Matrices;
                        Assert.AreEqual(x.Length, y.Length, "grup " + g);
                        for (int k = 0; k < x.Length; k++)
                        {
                            Vector4 px = x[k].GetColumn(3), py = y[k].GetColumn(3);
                            Assert.AreEqual(VegetationHash.Instance(px.x, px.z), VegetationHash.Instance(py.x, py.z), "grup " + g + " örnek " + k);
                        }
                    }

                    int ml = runtime.InstancesPerType[(int)VegetationType.LeafLitter];
                    Debug.Log("[DD-TEST] wp093.v6preview instances=" + shown.TotalInstances + " litter=" + ml + " moss=" + runtime.InstancesPerType[(int)VegetationType.Moss]
                              + " cover(0,-30)=" + preview.DrawSet.CoverAt(0f, -30f).ToString("F2"));
                    Assert.Greater(shown.TotalInstances, 10000, "V6 bahçesi neredeyse boş");
                    Assert.Greater(ml, 0, "momiji yaprak yolu boş (akçaağaç gövdeleri bulunamadı?)");
                }
                finally
                {
                    draw.Dispose();
                }
            }
        }
    }
}
