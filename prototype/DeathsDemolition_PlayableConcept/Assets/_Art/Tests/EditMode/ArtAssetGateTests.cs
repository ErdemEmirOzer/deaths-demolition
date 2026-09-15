using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DD.Art.Pipeline.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-011 · M3 CONTENT PIPELINE PILOT — asset gate (ADR-011-1 Import Contract v1, ADR-011-2 layout).
    ///
    /// Her FBX/PNG'nin importer'ı sözleşmeyle birebir mi, alt varlık listesinde gömülü doku/materyal
    /// sızmış mı, LOD üçgen sayıları AP-001 manifestiyle (drift 0) aynı mı, mesh model uzayına
    /// KAYNAK SINIRLARIYLA (1 mm) iniyor mu — eksen sözleşmesinin FBX düzeyindeki kanıtı — ve
    /// GUID/meta hijyeni bozulmuş mu.
    ///
    /// KAYNAK: AP-001 manifest sayıları doğrudan
    /// <c>Assets/_Art/Environment/GoldenSlice/AP001_GoldenSliceLayout.json</c>'dan okunur
    /// (<see cref="ArtLayoutJson"/>): test hiçbir üçgen sayısını kilitlemez, manifestten TÜRETİR.
    /// Eksen eşlemesi test tarafında BAĞIMSIZ yazılmıştır (glTF (x, y, z) → Unity (x, y, −z));
    /// üretim tarafındaki <c>ArtAxis</c> kullanılmaz ki iki taraf aynı hatayı paylaşamasın.
    ///
    /// Hiçbir test asset'e yazmaz: importer'lar salt okunur okunur, SaveAndReimport çağrılmaz.
    /// </summary>
    [TestFixture]
    [Category("WP011")]
    public sealed class ArtAssetGateTests
    {
        private const string ArtRoot = ArtImportContract.ArtRoot;

        /// <summary>Kaynak sınır paritesi: JSON 6 ondalık, float32 mesh sınırı; 1 mm sözleşme değeri.</summary>
        private const float SinirToleransMetre = 0.001f;

        /// <summary>AP-001'in 14 tekil dokusu: PBR seti (BaseColor+Normal+Packed) ve yalnız BaseColor taşıyan iki rol.</summary>
        private static readonly string[] PbrRoller = { "stone", "wood", "bark", "earth" };
        private static readonly string[] YalnizBaseColorRoller = { "plaster", "roof" };

        // ------------------------------------------------------------------ yardımcılar

        private static List<string> DiskteUzanti(string uzanti)
        {
            var sonuc = new List<string>();
            foreach (string yol in Directory.GetFiles(ArtRoot, "*" + uzanti, SearchOption.AllDirectories))
            {
                sonuc.Add(yol.Replace('\\', '/'));
            }

            sonuc.Sort(string.CompareOrdinal);
            return sonuc;
        }

        private static int UcgenSayisi(Mesh mesh)
        {
            // GetIndexCount alt-mesh meta verisidir; Read/Write kapalı mesh'te de okunur
            // (ArtImportStudy aynı yolu kullandı, evidence/study2-contract).
            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                tris += (int)(mesh.GetIndexCount(i) / 3);
            }

            return tris;
        }

        private static List<Mesh> MeshAltVarliklari(string yol)
        {
            var meshler = new List<Mesh>();
            foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(yol))
            {
                if (sub is Mesh m)
                {
                    meshler.Add(m);
                }
            }

            return meshler;
        }

        /// <summary>
        /// FBX'in model uzayı: mesh sınır kutusunun 8 köşesi, mesh'i taşıyan düğümün köke kadar
        /// (kök dahil) biriken TRS'siyle taşınır. Bu, modeli sahneye sürüklediğinde Unity'nin
        /// uyguladığı dönüşümün ta kendisidir (importer'ın +90° X düğümü dahil).
        /// </summary>
        private static bool ModelUzayiSinirlari(string fbxYolu, Mesh mesh, out Vector3 min, out Vector3 max)
        {
            var kok = AssetDatabase.LoadAssetAtPath<GameObject>(fbxYolu);
            min = max = Vector3.zero;
            if (kok == null)
            {
                return false;
            }

            foreach (MeshFilter mf in kok.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != mesh)
                {
                    continue;
                }

                Matrix4x4 m = ArtGateGeometry.LocalToRoot(mf.transform, null);
                ArtGateGeometry.TransformedAabb(mesh.bounds, m, out min, out max);
                return true;
            }

            return false;
        }

        // ============================================================== FBX importer sözleşmesi

        [Test]
        public void HerFbx_ImportSozlesmesineUyar()
        {
            List<string> fbxler = DiskteUzanti(".fbx");
            Assert.Greater(fbxler.Count, 0, "Assets/_Art altında hiç FBX yok; tarama yalanlanmıştır.");

            var ihlaller = new List<string>();
            foreach (string yol in fbxler)
            {
                var importer = AssetImporter.GetAtPath(yol) as ModelImporter;
                if (importer == null)
                {
                    ihlaller.Add(yol + ": ModelImporter yok (import edilmemiş?)");
                    continue;
                }

                List<string> v = ArtImportContract.ValidateModel(importer);
                if (v.Count > 0)
                {
                    ihlaller.Add(yol + ": " + string.Join("; ", v));
                }
            }

            Assert.IsEmpty(ihlaller, "Import Contract v1 ihlali (" + ihlaller.Count + "/" + fbxler.Count + "):\n" + string.Join("\n", ihlaller));
            Debug.Log("[DD-TEST] wp011.gate.fbx contract=ok count=" + fbxler.Count);
        }

        [Test]
        public void HerFbx_AltVarliktaMaterialVeTexture2DYok_EnAzBirMesh()
        {
            List<string> fbxler = DiskteUzanti(".fbx");
            Assert.Greater(fbxler.Count, 0, "Assets/_Art altında hiç FBX yok; tarama yalanlanmıştır.");

            var bulgular = new List<string>();
            foreach (string yol in fbxler)
            {
                int mesh = 0;
                foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(yol))
                {
                    if (sub == null)
                    {
                        continue;
                    }

                    if (sub is Material)
                    {
                        bulgular.Add(yol + ": Material alt varlığı sızmış (" + sub.name + ") — materialImportMode None olmalı.");
                    }
                    else if (sub is Texture2D)
                    {
                        bulgular.Add(yol + ": gömülü Texture2D çıkarılmış (" + sub.name + ") — dokular yalnız Textures/Source'tan gelir.");
                    }
                    else if (sub is Mesh)
                    {
                        mesh++;
                    }
                }

                if (mesh == 0)
                {
                    bulgular.Add(yol + ": hiç Mesh alt varlığı yok.");
                }
            }

            Assert.IsEmpty(bulgular, string.Join("\n", bulgular));
        }

        /// <summary>
        /// Diskteki FBX kümesi = ArtFamilies/ArtPipelinePaths'ten TÜRETİLEN küme (18×3 LOD +
        /// collider taşıyan aileler + zemin collider). Fazla ya da eksik dosya iki yönde de yakalanır.
        /// </summary>
        [Test]
        public void FbxKumesi_AileTablosundanTuretilenleAyni()
        {
            var beklenen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string aile in ArtFamilies.All)
            {
                for (int lod = 0; lod < 3; lod++)
                {
                    beklenen.Add(ArtPipelinePaths.FamilyLod(aile, lod));
                }

                if (ArtFamilies.HasCollider(aile))
                {
                    beklenen.Add(ArtPipelinePaths.FamilyColliders(aile));
                }
            }

            beklenen.Add(ArtPipelinePaths.GroundColliderFbx);

            var diskte = new HashSet<string>(DiskteUzanti(".fbx"), StringComparer.Ordinal);
            var eksik = new List<string>();
            var fazla = new List<string>();
            foreach (string b in beklenen) if (!diskte.Contains(b)) eksik.Add(b);
            foreach (string d in diskte) if (!beklenen.Contains(d)) fazla.Add(d);
            eksik.Sort(string.CompareOrdinal);
            fazla.Sort(string.CompareOrdinal);

            Assert.AreEqual(18, ArtFamilies.All.Length, "ArtFamilies.All 18 aile olmalı.");
            Assert.IsEmpty(eksik, "Beklenen FBX diskte yok (" + eksik.Count + "):\n" + string.Join("\n", eksik));
            Assert.IsEmpty(fazla, "Aile tablosunda karşılığı olmayan FBX (" + fazla.Count + "):\n" + string.Join("\n", fazla));
            Debug.Log("[DD-TEST] wp011.gate.fbxset count=" + diskte.Count + " (18x3 LOD + " + (beklenen.Count - 55) + " collider + zemin)");
        }

        // ============================================================== PNG importer sözleşmesi

        [Test]
        public void HerPng_RolBilinirVeImportSozlesmesineUyar()
        {
            List<string> pngler = DiskteUzanti(".png");
            Assert.Greater(pngler.Count, 0, "Assets/_Art altında hiç PNG yok; tarama yalanlanmıştır.");

            var ihlaller = new List<string>();
            foreach (string yol in pngler)
            {
                ArtImportContract.TextureRole rol = ArtImportContract.RoleOf(yol);
                if (rol == ArtImportContract.TextureRole.Unknown)
                {
                    ihlaller.Add(yol + ": rol Unknown — T_<role>_<BaseColor|Normal|Packed|MetallicSmoothness>.png adlandırması dışında.");
                }

                var importer = AssetImporter.GetAtPath(yol) as TextureImporter;
                if (importer == null)
                {
                    ihlaller.Add(yol + ": TextureImporter yok (import edilmemiş?)");
                    continue;
                }

                List<string> v = ArtImportContract.ValidateTexture(importer, rol);
                if (v.Count > 0)
                {
                    ihlaller.Add(yol + " (" + rol + "): " + string.Join("; ", v));
                }
            }

            Assert.IsEmpty(ihlaller, "Doku sözleşme ihlali (" + ihlaller.Count + "/" + pngler.Count + "):\n" + string.Join("\n", ihlaller));
            Debug.Log("[DD-TEST] wp011.gate.png contract=ok count=" + pngler.Count);
        }

        /// <summary>
        /// Diskteki PNG kümesi = 14 AP-001 kaynak dokusu + 4 türetilmiş MetallicSmoothness maskesi
        /// (repack aracı; stone/wood/bark/earth) + 1 pilot gökyüzü gradyanı (ArtSkyBuilder).
        /// Maske eksikse hat koşmamıştır — kapı kırmızı.
        /// </summary>
        [Test]
        public void PngKumesi_14KaynakArti4TuretilmisMaskeArti1Gokyuzu()
        {
            var beklenen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rol in PbrRoller)
            {
                beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "BaseColor"));
                beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Normal"));
                beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Packed"));
                beklenen.Add(ArtPipelinePaths.DerivedMask(rol));
            }

            foreach (string rol in YalnizBaseColorRoller)
            {
                beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "BaseColor"));
            }

            // WP-011 pilot gökyüzü (ADR-011-4 adayı): ArtSkyBuilder'ın ürettiği 4×256 kapalı-hava gradyanı,
            // BaseColor rolünde (sRGB). Sözleşme dışı bir PNG değildir; küme 14 + 4 + 1 = 19.
            beklenen.Add(ArtPipelinePaths.TexturesDerived + "/T_sky_BaseColor.png");

            Assert.AreEqual(19, beklenen.Count, "Beklenen doku kümesi 14 + 4 + 1 = 19 olmalı; yol üreticisi çakışan ad üretti.");

            var diskte = new HashSet<string>(DiskteUzanti(".png"), StringComparer.Ordinal);
            var eksik = new List<string>();
            var fazla = new List<string>();
            foreach (string b in beklenen) if (!diskte.Contains(b)) eksik.Add(b);
            foreach (string d in diskte) if (!beklenen.Contains(d)) fazla.Add(d);
            eksik.Sort(string.CompareOrdinal);
            fazla.Sort(string.CompareOrdinal);

            Assert.IsEmpty(eksik, "Beklenen doku diskte yok (" + eksik.Count + "; türetilmiş maske için repack aracı koşmalı):\n" + string.Join("\n", eksik));
            Assert.IsEmpty(fazla, "Sözleşme dışı PNG (" + fazla.Count + "):\n" + string.Join("\n", fazla));
        }

        /// <summary>
        /// Türetilmiş maske gerçekten alfa taşımalı (A = 1 − roughness): alfasız bir PNG'de
        /// URP/Lit smoothness her yerde 1 okur ve sahne cam gibi parlar — görsel olarak
        /// fark edilir ama sayısal olarak burada yakalanır.
        /// </summary>
        [Test]
        public void TuretilmisMaske_AlfaKanaliTasir()
        {
            var bulgular = new List<string>();
            foreach (string rol in PbrRoller)
            {
                string yol = ArtPipelinePaths.DerivedMask(rol);
                var importer = AssetImporter.GetAtPath(yol) as TextureImporter;
                if (importer == null)
                {
                    bulgular.Add(yol + ": yok.");
                    continue;
                }

                if (!importer.DoesSourceTextureHaveAlpha())
                {
                    bulgular.Add(yol + ": kaynak PNG alfa taşımıyor (smoothness kanalı kayıp).");
                }
            }

            Assert.IsEmpty(bulgular, string.Join("\n", bulgular));
        }

        // ============================================================== LOD üçgen ve sınır paritesi

        [Test]
        public void HerAile_LodUcgenSayilari_ManifestleAyniVeKesinAzalan()
        {
            var bulgular = new List<string>();
            var ozet = new StringBuilder();

            foreach (string aile in ArtFamilies.All)
            {
                int onceki = int.MaxValue;
                for (int lod = 0; lod < 3; lod++)
                {
                    string yol = ArtPipelinePaths.FamilyLod(aile, lod);
                    List<Mesh> meshler = MeshAltVarliklari(yol);
                    if (meshler.Count != 1)
                    {
                        bulgular.Add(yol + ": tam 1 Mesh alt varlığı beklenir, " + meshler.Count + " var.");
                        continue;
                    }

                    ArtLayoutJson.LodBounds kaynak = ArtLayoutJson.CanonicalLod(aile, lod);
                    if (kaynak == null)
                    {
                        bulgular.Add(yol + ": manifestte canonical_lod_bounds_gltf." + aile + "_LOD" + lod + " yok.");
                        continue;
                    }

                    int tris = UcgenSayisi(meshler[0]);
                    if (tris != kaynak.Triangles)
                    {
                        bulgular.Add(yol + ": üçgen " + tris + " != manifest " + kaynak.Triangles + " (drift).");
                    }

                    if (tris >= onceki)
                    {
                        bulgular.Add(yol + ": LOD" + lod + " üçgeni (" + tris + ") LOD" + (lod - 1) + "'den (" + onceki + ") küçük değil.");
                    }

                    onceki = tris;
                    ozet.Append(aile).Append(" L").Append(lod).Append('=').Append(tris).Append(' ');
                }
            }

            Assert.IsEmpty(bulgular, "LOD üçgen paritesi (" + bulgular.Count + "):\n" + string.Join("\n", bulgular));
            Debug.Log("[DD-TEST] wp011.gate.lodtris " + ozet);
        }

        /// <summary>
        /// EKSEN SÖZLEŞMESİNİN FBX DÜZEYİNDEKİ KANITI. Kaynak AABB (glTF Y-up) test tarafında
        /// bağımsız eşlemeyle sözleşme uzayına taşınır: (x, y, z)_gltf → (x, y, −z). Model uzayı
        /// AABB'si (mesh sınırı × importer düğüm TRS'si) bununla 1 mm içinde min VE max olarak
        /// aynı olmalı — yalnız boyut değil, konum da: 180° yaw sapması (−4..6.5 ↔ −6.5..4) ya da
        /// yatık geometri (Z-up) burada kırmızıya döner. Simetrik aileler bunu göremez, o yüzden
        /// palace_roof_bay gibi asimetrik aileler kanıt taşır.
        /// </summary>
        [Test]
        public void HerAile_LodSinirlari_KaynakSinirlarlaAyni_1mm()
        {
            var bulgular = new List<string>();
            float enKotu = 0f;
            string enKotuAd = "";
            int olculen = 0;

            foreach (string aile in ArtFamilies.All)
            {
                for (int lod = 0; lod < 3; lod++)
                {
                    string yol = ArtPipelinePaths.FamilyLod(aile, lod);
                    List<Mesh> meshler = MeshAltVarliklari(yol);
                    ArtLayoutJson.LodBounds kaynak = ArtLayoutJson.CanonicalLod(aile, lod);
                    if (meshler.Count != 1 || kaynak == null)
                    {
                        bulgular.Add(yol + ": mesh (" + meshler.Count + ") ya da manifest girişi yok.");
                        continue;
                    }

                    Vector3 beklenenMin, beklenenMax;
                    ArtLayoutJson.ToContractAabb(kaynak.Min, kaynak.Max, out beklenenMin, out beklenenMax);

                    Vector3 min, max;
                    if (!ModelUzayiSinirlari(yol, meshler[0], out min, out max))
                    {
                        bulgular.Add(yol + ": mesh'i taşıyan MeshFilter düğümü model prefabında bulunamadı.");
                        continue;
                    }

                    olculen++;
                    float sapma = ArtGateGeometry.MaxSapma(min, max, beklenenMin, beklenenMax);
                    if (sapma > enKotu)
                    {
                        enKotu = sapma;
                        enKotuAd = yol;
                    }

                    if (sapma > SinirToleransMetre)
                    {
                        bulgular.Add(yol + ": model AABB " + ArtGateGeometry.Yaz(min, max) + " != kaynak " +
                                     ArtGateGeometry.Yaz(beklenenMin, beklenenMax) + " (sapma " + (sapma * 1000f).ToString("0.0", CultureInfo.InvariantCulture) +
                                     " mm; ham mesh sınırı " + ArtGateGeometry.Yaz(meshler[0].bounds.min, meshler[0].bounds.max) + ")");
                    }
                }
            }

            Assert.AreEqual(54, olculen, "54 LOD mesh'i ölçülmeliydi; " + olculen + " ölçüldü.");
            Assert.IsEmpty(bulgular, "Kaynak sınır paritesi 1 mm (" + bulgular.Count + "/54):\n" + string.Join("\n", bulgular));
            Debug.Log("[DD-TEST] wp011.gate.bounds measured=" + olculen + " worst=" + (enKotu * 1000f).ToString("0.000", CultureInfo.InvariantCulture) + "mm at " + enKotuAd);
        }

        // ============================================================== GUID ve meta hijyeni

        private static readonly Regex GuidDeseni = new Regex(@"^guid: ([0-9a-f]{32})\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static string MetaGuid(string metaYolu)
        {
            // .meta küçük; ilk birkaç satırda guid vardır. Tümünü okumak da ucuz.
            Match m = GuidDeseni.Match(File.ReadAllText(metaYolu));
            return m.Success ? m.Groups[1].Value : null;
        }

        [Test]
        public void GuidTekligi_ArtIcindeVeAssetsGeneliCakismaYok()
        {
            var guidSahibi = new Dictionary<string, string>(StringComparer.Ordinal);
            var cakisma = new List<string>();
            var guidsiz = new List<string>();
            int artMeta = 0;

            foreach (string metaHam in Directory.GetFiles("Assets", "*.meta", SearchOption.AllDirectories))
            {
                string meta = metaHam.Replace('\\', '/');
                string guid = MetaGuid(meta);
                if (guid == null)
                {
                    guidsiz.Add(meta);
                    continue;
                }

                if (meta.StartsWith(ArtRoot + "/", StringComparison.Ordinal) || meta == ArtRoot + ".meta")
                {
                    artMeta++;
                }

                string sahip;
                if (guidSahibi.TryGetValue(guid, out sahip))
                {
                    cakisma.Add(guid + ": " + sahip + " <-> " + meta);
                }
                else
                {
                    guidSahibi[guid] = meta;
                }
            }

            Assert.Greater(artMeta, 0, "Assets/_Art altında hiç .meta görülmedi; tarama yalanlanmıştır.");
            Assert.IsEmpty(guidsiz, "guid satırı olmayan .meta (" + guidsiz.Count + "):\n" + string.Join("\n", guidsiz));
            Assert.IsEmpty(cakisma, "GUID çakışması (" + cakisma.Count + "):\n" + string.Join("\n", cakisma));
            Debug.Log("[DD-TEST] wp011.gate.guid unique=" + guidSahibi.Count + " artMeta=" + artMeta);
        }

        [Test]
        public void ArtAltindaHerAssetinMetasiVar_VeYetimMetaYok()
        {
            var eksik = new List<string>();
            var yetim = new List<string>();

            // Kök klasörün kendi metası da sayılır: Assets/_Art.meta yoksa git'te klasör GUID'i kaybolur.
            if (!File.Exists(ArtRoot + ".meta"))
            {
                eksik.Add(ArtRoot + " (klasör)");
            }

            foreach (string dizinHam in Directory.GetDirectories(ArtRoot, "*", SearchOption.AllDirectories))
            {
                string dizin = dizinHam.Replace('\\', '/');
                if (Path.GetFileName(dizin).StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!File.Exists(dizin + ".meta"))
                {
                    eksik.Add(dizin + " (klasör)");
                }
            }

            foreach (string yolHam in Directory.GetFiles(ArtRoot, "*", SearchOption.AllDirectories))
            {
                string yol = yolHam.Replace('\\', '/');
                string ad = Path.GetFileName(yol);
                if (ad.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (yol.EndsWith(".meta", StringComparison.Ordinal))
                {
                    string sahip = yol.Substring(0, yol.Length - ".meta".Length);
                    if (!File.Exists(sahip) && !Directory.Exists(sahip))
                    {
                        yetim.Add(yol);
                    }

                    continue;
                }

                if (!File.Exists(yol + ".meta"))
                {
                    eksik.Add(yol);
                }
            }

            Assert.IsEmpty(eksik, "Meta dosyası eksik (" + eksik.Count + "):\n" + string.Join("\n", eksik));
            Assert.IsEmpty(yetim, "Yetim .meta (" + yetim.Count + "):\n" + string.Join("\n", yetim));
        }
    }

    // ==================================================================== ortak yardımcılar
    // (DD.Art.Tests.EditMode assembly'sine özel; test dosyaları dışında bir dosya sahipliği
    // olmadığı için burada yaşarlar. GoldenSliceLayoutTests, ArtPrefabTests ve
    // ArtRepoPolicyTests aynı okuyucuyu kullanır.)

    /// <summary>
    /// AABB dönüşümü ve prefab/model hiyerarşisi matrisleri. Sahneye instantiate etmez: prefab
    /// asset'inin Transform zinciri EditMode'da okunabilir ve deterministiktir (Renderer.bounds
    /// ise bir kare beklemeden güncellenmez).
    /// </summary>
    internal static class ArtGateGeometry
    {
        /// <summary>t'nin köke kadar biriken yerel TRS'si; root null ise kök dahil tüm zincir.</summary>
        public static Matrix4x4 LocalToRoot(Transform t, Transform root)
        {
            Matrix4x4 m = Matrix4x4.identity;
            for (Transform c = t; c != null && c != root; c = c.parent)
            {
                m = Matrix4x4.TRS(c.localPosition, c.localRotation, c.localScale) * m;
            }

            return m;
        }

        /// <summary>Sınır kutusunun 8 köşesini taşıyıp yeni eksen hizalı kutuyu verir.</summary>
        public static void TransformedAabb(Bounds b, Matrix4x4 m, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            Vector3 bmin = b.min;
            Vector3 bmax = b.max;
            for (int i = 0; i < 8; i++)
            {
                var kose = new Vector3((i & 1) == 0 ? bmin.x : bmax.x, (i & 2) == 0 ? bmin.y : bmax.y, (i & 4) == 0 ? bmin.z : bmax.z);
                Vector3 p = m.MultiplyPoint3x4(kose);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        public static float MaxSapma(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        {
            float s = 0f;
            for (int i = 0; i < 3; i++)
            {
                s = Mathf.Max(s, Mathf.Abs(aMin[i] - bMin[i]));
                s = Mathf.Max(s, Mathf.Abs(aMax[i] - bMax[i]));
            }

            return s;
        }

        public static string Yaz(Vector3 min, Vector3 max)
        {
            return "[" + Yaz(min) + " .. " + Yaz(max) + "]";
        }

        public static string Yaz(Vector3 v)
        {
            return "(" + v.x.ToString("0.000", CultureInfo.InvariantCulture) + ", " +
                   v.y.ToString("0.000", CultureInfo.InvariantCulture) + ", " +
                   v.z.ToString("0.000", CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>
    /// AP001_GoldenSliceLayout.json'un test tarafı okuyucusu. Küçük, hedefli regex'ler; genel bir
    /// JSON parser değil (üretim tarafındaki ArtJson bilerek kullanılmaz: kanıt üretici koddan
    /// bağımsız olmalı). Giriş nesneleri iç içe nesne içermez, bu yüzden <c>{ … }</c> blokları
    /// düz eşleşir.
    ///
    /// Eksen eşlemesi (coordinate_note): glTF Y-up sağ el; sözleşme Unity (x, y, −z).
    /// Kuaterniyon: Z aynası altında R' = M·R·M → (−qx, −qy, qz, qw). Ölçek değişmez.
    /// </summary>
    internal static class ArtLayoutJson
    {
        private static string s_metin;

        public static string Metin
        {
            get
            {
                if (s_metin == null)
                {
                    Assert.IsTrue(File.Exists(ArtPipelinePaths.LayoutJson), "Layout JSON yok: " + ArtPipelinePaths.LayoutJson);
                    s_metin = File.ReadAllText(ArtPipelinePaths.LayoutJson);
                }

                return s_metin;
            }
        }

        public sealed class LodBounds
        {
            public Vector3 Min;
            public Vector3 Max;
            public int Triangles;
        }

        public sealed class Instance
        {
            public string Name;
            public string Family;
            public string Material;
            public int LodInSource;
            public int Triangles;
            public bool UniqueMesh;
            public bool OccluderCandidate;
            public Vector3 Translation;
            public Quaternion Rotation;
            public Vector3 Scale;
            public Vector3 DerivedScale;
            public Vector3 AabbMin;
            public Vector3 AabbMax;
        }

        // ------------------------------------------------------------------ eşleme (bağımsız)

        public static Vector3 ToContractPosition(Vector3 g)
        {
            return new Vector3(g.x, g.y, -g.z);
        }

        public static Quaternion ToContractRotation(Quaternion q)
        {
            return new Quaternion(-q.x, -q.y, q.z, q.w);
        }

        public static Vector3 ToContractScale(Vector3 s)
        {
            return s;
        }

        public static void ToContractAabb(Vector3 gMin, Vector3 gMax, out Vector3 cMin, out Vector3 cMax)
        {
            cMin = new Vector3(gMin.x, gMin.y, -gMax.z);
            cMax = new Vector3(gMax.x, gMax.y, -gMin.z);
        }

        /// <summary>"AP001_M_sakura_purple_v009" → "sakura_purple" (ön ek AP001_M_, son ek _vNNN).</summary>
        public static string MaterialRole(string materialName)
        {
            const string onEk = "AP001_M_";
            string s = materialName;
            if (s.StartsWith(onEk, StringComparison.Ordinal))
            {
                s = s.Substring(onEk.Length);
            }

            int v = s.LastIndexOf("_v", StringComparison.Ordinal);
            if (v > 0)
            {
                s = s.Substring(0, v);
            }

            return s;
        }

        // ------------------------------------------------------------------ alanlar

        public static LodBounds CanonicalLod(string family, int lod)
        {
            var desen = new Regex("\"" + Regex.Escape(family + "_LOD" + lod) + "\":\\s*\\{\\s*\"min\":\\s*\\[([^\\]]*)\\],\\s*\"max\":\\s*\\[([^\\]]*)\\],\\s*\"tri\":\\s*(\\d+)");
            Match m = desen.Match(Metin);
            if (!m.Success)
            {
                return null;
            }

            return new LodBounds
            {
                Min = Vec3(m.Groups[1].Value),
                Max = Vec3(m.Groups[2].Value),
                Triangles = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
            };
        }

        public static string SourceGlbSha256()
        {
            Match m = Regex.Match(Metin, "\"source_glb\":\\s*\\{[^}]*\"sha256\":\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static int IntField(string name)
        {
            Match m = Regex.Match(Metin, "\"" + Regex.Escape(name) + "\":\\s*(-?\\d+)");
            Assert.IsTrue(m.Success, "Layout JSON'da '" + name + "' alanı yok.");
            return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        public static List<string> Families()
        {
            Match m = Regex.Match(Metin, "\"families\":\\s*\\[([^\\]]*)\\]");
            Assert.IsTrue(m.Success, "Layout JSON'da 'families' listesi yok.");
            var sonuc = new List<string>();
            foreach (Match s in Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\""))
            {
                sonuc.Add(s.Groups[1].Value);
            }

            return sonuc;
        }

        private static List<Instance> s_instances;

        public static List<Instance> FamilyInstances()
        {
            if (s_instances != null)
            {
                return s_instances;
            }

            int bas = Metin.IndexOf("\"family_instances\"", StringComparison.Ordinal);
            int son = Metin.IndexOf("\"non_family_unique\"", StringComparison.Ordinal);
            Assert.IsTrue(bas >= 0 && son > bas, "Layout JSON'da family_instances / non_family_unique bölümleri bulunamadı.");
            string bolum = Metin.Substring(bas, son - bas);

            var liste = new List<Instance>(900);
            foreach (Match blok in Regex.Matches(bolum, @"\{[^{}]*\}"))
            {
                string b = blok.Value;
                liste.Add(new Instance
                {
                    Name = Str(b, "name"),
                    Family = Str(b, "family"),
                    Material = MaterialRole(IlkMaterial(b)),
                    LodInSource = Int(b, "lod_in_source"),
                    Triangles = Int(b, "triangles"),
                    UniqueMesh = Bool(b, "unique_mesh"),
                    OccluderCandidate = Bool(b, "occluder_candidate"),
                    Translation = Vec3(Arr(b, "gltf_translation")),
                    Rotation = Quat(Arr(b, "gltf_rotation")),
                    Scale = Vec3(Arr(b, "gltf_scale")),
                    DerivedScale = Vec3(Arr(b, "derived_scale_from_bounds")),
                    AabbMin = Vec3(Arr(b, "world_aabb_gltf_min")),
                    AabbMax = Vec3(Arr(b, "world_aabb_gltf_max"))
                });
            }

            s_instances = liste;
            return liste;
        }

        // ------------------------------------------------------------------ küçük parçalayıcılar

        private static string Str(string blok, string alan)
        {
            Match m = Regex.Match(blok, "\"" + alan + "\":\\s*\"([^\"]*)\"");
            Assert.IsTrue(m.Success, "Girişte '" + alan + "' yok: " + Kisalt(blok));
            return m.Groups[1].Value;
        }

        private static int Int(string blok, string alan)
        {
            Match m = Regex.Match(blok, "\"" + alan + "\":\\s*(-?\\d+)");
            Assert.IsTrue(m.Success, "Girişte '" + alan + "' yok: " + Kisalt(blok));
            return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>true → true; false/null/eksik → false (occluder_candidate null olan aileler için).</summary>
        private static bool Bool(string blok, string alan)
        {
            Match m = Regex.Match(blok, "\"" + alan + "\":\\s*(true|false|null)");
            return m.Success && m.Groups[1].Value == "true";
        }

        private static string Arr(string blok, string alan)
        {
            Match m = Regex.Match(blok, "\"" + alan + "\":\\s*\\[([^\\]]*)\\]");
            Assert.IsTrue(m.Success, "Girişte '" + alan + "' yok: " + Kisalt(blok));
            return m.Groups[1].Value;
        }

        private static string IlkMaterial(string blok)
        {
            Match m = Regex.Match(blok, "\"materials\":\\s*\\[\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static float[] Sayilar(string icerik)
        {
            string[] parcalar = icerik.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var s = new float[parcalar.Length];
            for (int i = 0; i < parcalar.Length; i++)
            {
                s[i] = float.Parse(parcalar[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            return s;
        }

        public static Vector3 Vec3(string icerik)
        {
            float[] s = Sayilar(icerik);
            Assert.AreEqual(3, s.Length, "3 bileşen beklenir: [" + icerik.Trim() + "]");
            return new Vector3(s[0], s[1], s[2]);
        }

        private static Quaternion Quat(string icerik)
        {
            float[] s = Sayilar(icerik);
            Assert.AreEqual(4, s.Length, "4 bileşen (x, y, z, w) beklenir: [" + icerik.Trim() + "]");
            return new Quaternion(s[0], s[1], s[2], s[3]);
        }

        private static string Kisalt(string s)
        {
            s = s.Replace('\n', ' ');
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }
    }
}
