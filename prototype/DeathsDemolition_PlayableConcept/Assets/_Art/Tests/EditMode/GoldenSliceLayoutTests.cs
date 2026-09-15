using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DD.Art.Pipeline.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-011 · Golden slice layout asset'i (AP001_GoldenSliceLayout.asset) ↔ kaynak JSON ↔ aile
    /// prefabları paritesi. İki bağımsız kanıt zinciri:
    ///
    ///   1. JSON ↔ .asset: her giriş (ad, aile, TRS, AABB, malzeme, bayraklar) JSON'dan TEST
    ///      TARAFININ kendi eksen eşlemesiyle türetilir ve asset'tekiyle karşılaştırılır. Üretim
    ///      tarafındaki ArtAxis burada kullanılmaz; ayrı bir testte doğrudan sınanır.
    ///   2. AABB PARİTESİ (eksen sözleşmesinin uçtan uca kanıtı): her giriş için aile prefabının
    ///      LOD&lt;lod_in_source&gt; mesh sınırı, prefab hiyerarşisi matrisleri × giriş TRS'siyle
    ///      dünyaya taşınır ve girişin AABB'siyle 2 cm içinde aynı olmalıdır. Sahneye hiçbir şey
    ///      instantiate edilmez. 819/823 tam parite; 4 kahraman ağaç girişi B-4'te beyan edilen
    ///      sapmadır (aşağıda adlarıyla ve tazelik nöbetçisiyle).
    ///
    /// NEDEN lod_in_source: 431 creeping_groundcover örneği kaynak sahnede LOD1 mesh'iyle
    /// yerleşiktir ve JSON'daki world AABB o mesh'ten hesaplanmıştır (LOD0 sınırı 8.6 cm daha
    /// geniştir). LOD0 ile karşılaştırmak yanlış kırmızı verir; giriş bu yüzden hangi LOD'dan
    /// geldiğini taşır.
    /// </summary>
    [TestFixture]
    [Category("WP011")]
    public sealed class GoldenSliceLayoutTests
    {
        private const int BeklenenAileOrnegi = 823;
        private const int BeklenenToplamOrnek = 875;
        private const int BeklenenAileDisiTekil = 52;
        private const int BeklenenTekilMesh = 28;
        private const float PariteToleransMetre = 0.02f;
        private const float JsonAssetToleransMetre = 0.0005f;
        private const float AciToleransDerece = 0.01f;

        /// <summary>AP-001 dilimindeki dağılım (manifest 04, B-3). Test bunu JSON'la da çapraz doğrular.</summary>
        private static readonly KeyValuePair<string, int>[] BeklenenDagilim =
        {
            new KeyValuePair<string, int>("creeping_groundcover", 431),
            new KeyValuePair<string, int>("greenery_cluster", 184),
            new KeyValuePair<string, int>("understory_shrub", 132),
            new KeyValuePair<string, int>("timber_beam", 11),
            new KeyValuePair<string, int>("timber_post", 11),
            new KeyValuePair<string, int>("palace_roof_bay", 11),
            new KeyValuePair<string, int>("plaster_wall", 11),
            new KeyValuePair<string, int>("sakura_foliage", 11),
            new KeyValuePair<string, int>("sakura_trunk", 11),
            new KeyValuePair<string, int>("rock_prop", 3),
            new KeyValuePair<string, int>("curb", 2),
            new KeyValuePair<string, int>("roof_segment", 1),
            new KeyValuePair<string, int>("gate_transition", 1),
            new KeyValuePair<string, int>("open_corridor", 1),
            new KeyValuePair<string, int>("low_rise", 1),
            new KeyValuePair<string, int>("planter_boundary", 1),
            new KeyValuePair<string, int>("courtyard_floor", 0),
            new KeyValuePair<string, int>("ring_transition", 0)
        };

        /// <summary>
        /// B-4 beyanı: iki kahraman ağacın gövde+yaprak mesh'leri aile mesh'inden farklıdır
        /// (payanda ayrıntısı; Tree_9.007 ise döndürme+ölçeği mesh'e pişirilmiş '.001' kopyası —
        /// çıkarım betiği bunu da unique_mesh sayar ve AP-001 config ölçeğini yazar; pişirilmiş yaw
        /// GLB'den geri alınamaz). Aile prefabı + config/derived scale bu dördü yaklaşık
        /// yerleştirir; sapma 1–2.2 m ölçülmüştür. Beyan dışı kalan 819 giriş 2 cm'de.
        /// </summary>
        private static readonly string[] BeyanEdilenKahramanAgacGirisleri =
        {
            "AP001_TreeFoliage_1.008_Export",
            "AP001_TreeTrunk_1.008_Export",
            "AP001_TreeFoliage_9.007_Export",
            "AP001_TreeTrunk_9.007_Export"
        };

        private const float KahramanAgacUstSinirMetre = 3f;

        private static readonly Regex HexSha256 = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled);

        // ------------------------------------------------------------------ yardımcılar

        private static GoldenSliceLayoutAsset Layout()
        {
            var layout = AssetDatabase.LoadAssetAtPath<GoldenSliceLayoutAsset>(ArtPipelinePaths.LayoutAsset);
            Assert.IsNotNull(layout, "Layout asset'i yok: " + ArtPipelinePaths.LayoutAsset + " (GoldenSliceLayoutBuilder koşmalı).");
            Assert.IsNotNull(layout.entries, "Layout asset'inin entries listesi null.");
            return layout;
        }

        private static bool BeyanliMi(string ad)
        {
            return Array.IndexOf(BeyanEdilenKahramanAgacGirisleri, ad) >= 0;
        }

        private static Dictionary<string, int> AileSayimi(GoldenSliceLayoutAsset layout)
        {
            var sayim = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (GoldenSliceLayoutAsset.Entry e in layout.entries)
            {
                int n;
                sayim.TryGetValue(e.family ?? "", out n);
                sayim[e.family ?? ""] = n + 1;
            }

            return sayim;
        }

        // ============================================================== asset varlığı ve sayımlar

        [Test]
        public void LayoutAsset_VarVeSayimlarManifestleAyni()
        {
            GoldenSliceLayoutAsset layout = Layout();

            Assert.AreEqual(BeklenenAileOrnegi, layout.familyInstanceCount, "familyInstanceCount");
            Assert.AreEqual(layout.familyInstanceCount, layout.entries.Count, "entries.Count ≠ familyInstanceCount");
            Assert.AreEqual(BeklenenToplamOrnek, layout.instanceCountTotal, "instanceCountTotal");
            Assert.AreEqual(BeklenenAileDisiTekil, layout.nonFamilyUniqueCount, "nonFamilyUniqueCount");

            // JSON ile çapraz: sayılar asset'e kopyalanmış olmalı, uydurulmuş değil.
            Assert.AreEqual(ArtLayoutJson.IntField("family_instance_count"), layout.familyInstanceCount);
            Assert.AreEqual(ArtLayoutJson.IntField("instance_count_total"), layout.instanceCountTotal);
            Assert.AreEqual(ArtLayoutJson.IntField("non_family_unique_count"), layout.nonFamilyUniqueCount);
            Assert.AreEqual(ArtLayoutJson.FamilyInstances().Count, layout.entries.Count, "JSON family_instances girişi ≠ asset entries");

            Assert.IsTrue(layout.sourceGlbSha256 != null && HexSha256.IsMatch(layout.sourceGlbSha256), "sourceGlbSha256 64-hex değil: '" + layout.sourceGlbSha256 + "'");
            Assert.AreEqual(ArtLayoutJson.SourceGlbSha256(), layout.sourceGlbSha256, "sourceGlbSha256 JSON'daki source_glb.sha256 ile aynı olmalı.");
            Assert.IsTrue(layout.sourceJsonSha256 != null && HexSha256.IsMatch(layout.sourceJsonSha256), "sourceJsonSha256 64-hex değil: '" + layout.sourceJsonSha256 + "'");
        }

        [Test]
        public void AileAdlari_ArtFamiliesIcindeVeJsonListesiyleAyniSirada()
        {
            List<string> jsonAileler = ArtLayoutJson.Families();
            Assert.AreEqual(18, jsonAileler.Count);
            CollectionAssert.AreEqual(jsonAileler, ArtFamilies.All, "ArtFamilies.All JSON 'families' listesiyle (alfabetik) birebir aynı olmalı.");

            GoldenSliceLayoutAsset layout = Layout();
            var bilinmeyen = new List<string>();
            foreach (GoldenSliceLayoutAsset.Entry e in layout.entries)
            {
                if (Array.IndexOf(ArtFamilies.All, e.family) < 0)
                {
                    bilinmeyen.Add(e.name + " → '" + e.family + "'");
                }
            }

            Assert.IsEmpty(bilinmeyen, "ArtFamilies.All dışında aile (" + bilinmeyen.Count + "):\n" + string.Join("\n", bilinmeyen));
        }

        [Test]
        public void AileDagilimi_AP001IleVeJsonIleAyni()
        {
            GoldenSliceLayoutAsset layout = Layout();
            Dictionary<string, int> asset = AileSayimi(layout);

            var json = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ArtLayoutJson.Instance i in ArtLayoutJson.FamilyInstances())
            {
                int n;
                json.TryGetValue(i.Family, out n);
                json[i.Family] = n + 1;
            }

            var hatalar = new List<string>();
            int toplam = 0;
            foreach (KeyValuePair<string, int> b in BeklenenDagilim)
            {
                int a, j;
                asset.TryGetValue(b.Key, out a);
                json.TryGetValue(b.Key, out j);
                toplam += b.Value;
                if (a != b.Value || j != b.Value)
                {
                    hatalar.Add(b.Key + ": beklenen " + b.Value + ", asset " + a + ", json " + j);
                }
            }

            Assert.AreEqual(BeklenenAileOrnegi, toplam, "Beklenen dağılım tablosu 823'e toplanmıyor; tablo bozuk.");
            Assert.IsEmpty(hatalar, "Aile dağılımı (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }

        [Test]
        public void UniqueMesh_28Giris_VeJsonIleAyniAdlar()
        {
            GoldenSliceLayoutAsset layout = Layout();
            var assetTekil = new HashSet<string>(StringComparer.Ordinal);
            foreach (GoldenSliceLayoutAsset.Entry e in layout.entries)
            {
                if (e.uniqueMesh) assetTekil.Add(e.name);
            }

            var jsonTekil = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArtLayoutJson.Instance i in ArtLayoutJson.FamilyInstances())
            {
                if (i.UniqueMesh) jsonTekil.Add(i.Name);
            }

            Assert.AreEqual(BeklenenTekilMesh, assetTekil.Count, "uniqueMesh giriş sayısı");
            Assert.IsTrue(assetTekil.SetEquals(jsonTekil), "uniqueMesh adları JSON ile aynı değil.");
        }

        [Test]
        public void Rotasyonlar_NormalizeVeSayilarSonlu_OlcekPozitif()
        {
            GoldenSliceLayoutAsset layout = Layout();
            var hatalar = new List<string>();
            foreach (GoldenSliceLayoutAsset.Entry e in layout.entries)
            {
                Quaternion q = e.rotation;
                float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                if (Mathf.Abs(n - 1f) > 1e-4f)
                {
                    hatalar.Add(e.name + ": |q| = " + n.ToString("0.000000", CultureInfo.InvariantCulture));
                }

                if (!Sonlu(e.position) || !Sonlu(e.scale) || !Sonlu(e.aabbMin) || !Sonlu(e.aabbMax))
                {
                    hatalar.Add(e.name + ": NaN/sonsuz bileşen.");
                }

                if (e.scale.x <= 0f || e.scale.y <= 0f || e.scale.z <= 0f)
                {
                    hatalar.Add(e.name + ": ölçek pozitif değil " + ArtGateGeometry.Yaz(e.scale) + " (ayna yasak).");
                }

                if (e.aabbMin.x > e.aabbMax.x || e.aabbMin.y > e.aabbMax.y || e.aabbMin.z > e.aabbMax.z)
                {
                    hatalar.Add(e.name + ": aabbMin > aabbMax " + ArtGateGeometry.Yaz(e.aabbMin, e.aabbMax));
                }
            }

            Assert.IsEmpty(hatalar, "Giriş sağlığı (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }

        private static bool Sonlu(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        // ============================================================== JSON ↔ asset (bağımsız eşleme)

        [Test]
        public void JsonAssetParitesi_TrsAabbMalzemeBayraklar_823()
        {
            GoldenSliceLayoutAsset layout = Layout();
            List<ArtLayoutJson.Instance> json = ArtLayoutJson.FamilyInstances();

            var jsonAdla = new Dictionary<string, ArtLayoutJson.Instance>(StringComparer.Ordinal);
            foreach (ArtLayoutJson.Instance i in json)
            {
                Assert.IsFalse(jsonAdla.ContainsKey(i.Name), "JSON'da yinelenen giriş adı: " + i.Name);
                jsonAdla[i.Name] = i;
            }

            var hatalar = new List<string>();
            int sakuraPurple = 0;
            var gorulen = new HashSet<string>(StringComparer.Ordinal);

            foreach (GoldenSliceLayoutAsset.Entry e in layout.entries)
            {
                ArtLayoutJson.Instance j;
                if (!jsonAdla.TryGetValue(e.name, out j))
                {
                    hatalar.Add(e.name + ": JSON'da yok.");
                    continue;
                }

                if (!gorulen.Add(e.name))
                {
                    hatalar.Add(e.name + ": asset'te yineleniyor.");
                    continue;
                }

                if (e.family != j.Family) hatalar.Add(e.name + ": aile '" + e.family + "' ≠ json '" + j.Family + "'");
                if (e.lodInSource != j.LodInSource) hatalar.Add(e.name + ": lodInSource " + e.lodInSource + " ≠ " + j.LodInSource);
                if (e.uniqueMesh != j.UniqueMesh) hatalar.Add(e.name + ": uniqueMesh " + e.uniqueMesh + " ≠ " + j.UniqueMesh);
                if (e.triangles != j.Triangles) hatalar.Add(e.name + ": triangles " + e.triangles + " ≠ " + j.Triangles);
                if (e.occluderCandidate != j.OccluderCandidate) hatalar.Add(e.name + ": occluderCandidate " + e.occluderCandidate + " ≠ " + j.OccluderCandidate);
                if (e.material != j.Material) hatalar.Add(e.name + ": material '" + e.material + "' ≠ '" + j.Material + "'");
                if (e.material == "sakura_purple") sakuraPurple++;

                Vector3 pos = ArtLayoutJson.ToContractPosition(j.Translation);
                if ((pos - e.position).magnitude > JsonAssetToleransMetre)
                {
                    hatalar.Add(e.name + ": position " + ArtGateGeometry.Yaz(e.position) + " ≠ beklenen " + ArtGateGeometry.Yaz(pos));
                }

                Quaternion rot = ArtLayoutJson.ToContractRotation(j.Rotation);
                float aci = Quaternion.Angle(rot, e.rotation);
                if (aci > AciToleransDerece)
                {
                    hatalar.Add(e.name + ": rotation açı farkı " + aci.ToString("0.000", CultureInfo.InvariantCulture) + "° (asset " + e.rotation.eulerAngles + ", beklenen " + rot.eulerAngles + ")");
                }

                Vector3 olcek = ArtLayoutJson.ToContractScale(j.UniqueMesh ? j.DerivedScale : j.Scale);
                if ((olcek - e.scale).magnitude > 1e-5f)
                {
                    hatalar.Add(e.name + ": scale " + ArtGateGeometry.Yaz(e.scale) + " ≠ beklenen " + ArtGateGeometry.Yaz(olcek) + (j.UniqueMesh ? " (derived_scale_from_bounds)" : " (gltf_scale)"));
                }

                Vector3 amin, amax;
                ArtLayoutJson.ToContractAabb(j.AabbMin, j.AabbMax, out amin, out amax);
                float sapma = ArtGateGeometry.MaxSapma(e.aabbMin, e.aabbMax, amin, amax);
                if (sapma > JsonAssetToleransMetre)
                {
                    hatalar.Add(e.name + ": aabb " + ArtGateGeometry.Yaz(e.aabbMin, e.aabbMax) + " ≠ beklenen " + ArtGateGeometry.Yaz(amin, amax));
                }
            }

            Assert.AreEqual(2, sakuraPurple, "sakura_purple malzemeli giriş sayısı (AP-001: 2 ağaç).");
            Assert.IsEmpty(hatalar, "JSON ↔ asset paritesi (" + hatalar.Count + "/" + layout.entries.Count + "):\n" + string.Join("\n", Ilk(hatalar, 40)));
        }

        [Test]
        public void GirisBayraklari_ArtFamiliesTablosuylaTutarli()
        {
            GoldenSliceLayoutAsset layout = Layout();
            var hatalar = new List<string>();
            foreach (GoldenSliceLayoutAsset.Entry e in layout.entries)
            {
                bool occ = ArtFamilies.IsOccluderCandidate(e.family);
                if (e.occluderCandidate != occ)
                {
                    hatalar.Add(e.name + " (" + e.family + "): occluderCandidate " + e.occluderCandidate + ", ArtFamilies " + occ);
                }

                string rol = ArtFamilies.MaterialRole(e.family);
                bool uyumlu = e.material == rol || (e.material == "sakura_purple" && rol == "sakura_pink");
                if (!uyumlu)
                {
                    hatalar.Add(e.name + " (" + e.family + "): material '" + e.material + "', ArtFamilies.MaterialRole '" + rol + "'");
                }
            }

            Assert.IsEmpty(hatalar, "Bayrak/rol tutarlılığı (" + hatalar.Count + "):\n" + string.Join("\n", Ilk(hatalar, 40)));

            // OccluderCandidates listesi ↔ IsOccluderCandidate tek kaynak.
            foreach (string aile in ArtFamilies.All)
            {
                bool listede = Array.IndexOf(ArtFamilies.OccluderCandidates, aile) >= 0;
                Assert.AreEqual(listede, ArtFamilies.IsOccluderCandidate(aile), aile + ": OccluderCandidates listesi ile IsOccluderCandidate çelişiyor.");
            }

            Assert.AreEqual(6, ArtFamilies.OccluderCandidates.Length, "AP-001 occluder adayı 6 aile.");
        }

        // ============================================================== ArtAxis (üretim eşlemesi) doğrudan

        [Test]
        public void ArtAxis_BagimsizEslemeyleAyni()
        {
            // Test tarafı eşleme: (x, y, z) → (x, y, −z); q → (−qx, −qy, qz, qw); ölçek aynı.
            var noktalar = new[] { new Vector3(35f, 4.1f, -25f), new Vector3(-14f, -0.06f, -42f), new Vector3(1.5f, 2f, 3f) };
            foreach (Vector3 g in noktalar)
            {
                Vector3 beklenen = ArtLayoutJson.ToContractPosition(g);
                Assert.Less((ArtAxis.GltfToContractPosition(g) - beklenen).magnitude, 1e-6f, "GltfToContractPosition(" + g + ")");
                Assert.Less((ArtAxis.GltfToContractScale(g) - ArtLayoutJson.ToContractScale(g)).magnitude, 1e-6f, "GltfToContractScale(" + g + ")");
            }

            // Kahraman ağacın gerçek yaw'ı ve saf +90° yaw: yön işareti burada görünür.
            var donusler = new[] { new Quaternion(0f, 0.875898f, 0f, 0.482497f), Quaternion.AngleAxis(90f, Vector3.up), Quaternion.identity };
            foreach (Quaternion q in donusler)
            {
                Quaternion beklenen = ArtLayoutJson.ToContractRotation(q);
                float aci = Quaternion.Angle(ArtAxis.GltfToContractRotation(q), beklenen);
                Assert.Less(aci, AciToleransDerece, "GltfToContractRotation(" + q + ") açı farkı " + aci + "°");
            }

            // +90° glTF yaw'ı +X'i −Z_gltf = +Z_unity'ye taşır: sözleşme uzayında X → +Z.
            Vector3 x = ArtAxis.GltfToContractRotation(Quaternion.AngleAxis(90f, Vector3.up)) * Vector3.right;
            Assert.AreEqual(0f, x.x, 1e-4f);
            Assert.AreEqual(1f, x.z, 1e-4f, "Yaw yönü ters: Z aynası altında açı işareti değişmeli.");

            Bounds b = ArtAxis.GltfAabbToContract(new Vector3(-1.5f, -0.06f, -4f), new Vector3(1.5f, 3.873f, 6.5f));
            Vector3 bmin, bmax;
            ArtLayoutJson.ToContractAabb(new Vector3(-1.5f, -0.06f, -4f), new Vector3(1.5f, 3.873f, 6.5f), out bmin, out bmax);
            Assert.Less(ArtGateGeometry.MaxSapma(b.min, b.max, bmin, bmax), 1e-5f, "GltfAabbToContract: " + ArtGateGeometry.Yaz(b.min, b.max) + " ≠ " + ArtGateGeometry.Yaz(bmin, bmax));
        }

        // ============================================================== AABB PARİTESİ (eksen sözleşmesi kanıtı)

        private sealed class AileLodBilgisi
        {
            public Mesh Mesh;
            public Matrix4x4 PrefabUzayi;
        }

        /// <summary>Aile prefabının LOD&lt;lod&gt; mesh'i ve prefab köküne göre matrisi; instantiate yok.</summary>
        private static AileLodBilgisi AileLod(string aile, int lod, Dictionary<string, AileLodBilgisi> onbellek, List<string> hatalar)
        {
            string anahtar = aile + "#" + lod;
            AileLodBilgisi bilgi;
            if (onbellek.TryGetValue(anahtar, out bilgi))
            {
                return bilgi;
            }

            bilgi = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPipelinePaths.FamilyPrefab(aile));
            if (prefab == null)
            {
                hatalar.Add(aile + ": prefab yok (" + ArtPipelinePaths.FamilyPrefab(aile) + ").");
            }
            else
            {
                string beklenenYol = ArtPipelinePaths.FamilyLod(aile, lod);
                foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || AssetDatabase.GetAssetPath(mf.sharedMesh) != beklenenYol)
                    {
                        continue;
                    }

                    bilgi = new AileLodBilgisi
                    {
                        Mesh = mf.sharedMesh,
                        PrefabUzayi = ArtGateGeometry.LocalToRoot(mf.transform, prefab.transform)
                    };
                    break;
                }

                if (bilgi == null)
                {
                    hatalar.Add(aile + ": prefabda " + beklenenYol + " mesh'ini taşıyan MeshFilter yok.");
                }
            }

            onbellek[anahtar] = bilgi;
            return bilgi;
        }

        [Test]
        public void AabbParitesi_PrefabCarpiTrs_GirisAabbIle2cm_823()
        {
            GoldenSliceLayoutAsset layout = Layout();
            Assert.AreEqual(BeklenenAileOrnegi, layout.entries.Count);

            var onbellek = new Dictionary<string, AileLodBilgisi>(StringComparer.Ordinal);
            var kurulumHatalari = new List<string>();
            var ihlaller = new List<string>();
            var beyanli = new List<string>();
            int olculen = 0;
            float enKotu = -1f;
            string enKotuYazi = "";
            float beyanliEnKotu = 0f;

            foreach (GoldenSliceLayoutAsset.Entry e in layout.entries)
            {
                AileLodBilgisi bilgi = AileLod(e.family, e.lodInSource, onbellek, kurulumHatalari);
                if (bilgi == null)
                {
                    continue;
                }

                Matrix4x4 dunya = Matrix4x4.TRS(e.position, e.rotation, e.scale) * bilgi.PrefabUzayi;
                Vector3 min, max;
                ArtGateGeometry.TransformedAabb(bilgi.Mesh.bounds, dunya, out min, out max);
                float sapma = ArtGateGeometry.MaxSapma(min, max, e.aabbMin, e.aabbMax);
                olculen++;

                string yazi = e.name + " (" + e.family + " LOD" + e.lodInSource + (e.uniqueMesh ? ", unique" : "") + "): prefab×TRS " +
                              ArtGateGeometry.Yaz(min, max) + " vs giriş " + ArtGateGeometry.Yaz(e.aabbMin, e.aabbMax) +
                              " sapma " + (sapma * 100f).ToString("0.00", CultureInfo.InvariantCulture) + " cm";

                if (BeyanliMi(e.name))
                {
                    beyanli.Add(yazi);
                    beyanliEnKotu = Mathf.Max(beyanliEnKotu, sapma);
                    if (sapma > KahramanAgacUstSinirMetre)
                    {
                        ihlaller.Add("[beyanlı üst sınır " + KahramanAgacUstSinirMetre + " m aşıldı] " + yazi);
                    }

                    continue;
                }

                if (sapma > enKotu)
                {
                    enKotu = sapma;
                    enKotuYazi = yazi;
                }

                if (sapma > PariteToleransMetre)
                {
                    ihlaller.Add(yazi);
                }
            }

            // Kurulum hataları (prefab/mesh yok) sayım kanıtını da düşürür; ayrı raporlanır.
            Assert.IsEmpty(kurulumHatalari, "Parite ölçülemedi (" + kurulumHatalari.Count + "):\n" + string.Join("\n", kurulumHatalari));
            Assert.AreEqual(BeklenenAileOrnegi, olculen, "823 girişin tamamı ölçülmeliydi.");
            Assert.AreEqual(BeyanEdilenKahramanAgacGirisleri.Length, beyanli.Count, "Beyanlı kahraman ağaç girişleri asset'te bulunamadı:\n" + string.Join("\n", beyanli));

            Debug.Log("[DD-TEST] wp011.layout.parity measured=" + olculen + " strict=" + (olculen - beyanli.Count) +
                      " worst=" + (enKotu * 100f).ToString("0.000", CultureInfo.InvariantCulture) + "cm at " + enKotuYazi +
                      "\n  declared hero trees (B-4):\n  " + string.Join("\n  ", beyanli));

            Assert.IsEmpty(ihlaller,
                "AABB paritesi 2 cm ihlali (" + ihlaller.Count + "/" + (olculen - beyanli.Count) + "), en kötü: " + enKotuYazi +
                "\n(eksen sözleşmesi: glTF (x, y, z) → Unity (x, y, −z); prefab geometrisi +90° X; yaw işareti ters ise yüzlerce giriş sapar)\n" +
                string.Join("\n", Ilk(ihlaller, 30)));

            // Tazelik nöbetçisi: beyanlı liste bayatlarsa (ağaçlar gerçek mesh'le gelirse) burası
            // kırmızıya döner ve liste kaldırılır — beyan sessizce sürmez.
            Assert.Greater(beyanliEnKotu, PariteToleransMetre,
                "Beyanlı kahraman ağaç girişleri artık 2 cm içinde: B-4 beyanı kapanmış, BeyanEdilenKahramanAgacGirisleri listesini kaldır.");
        }

        private static List<string> Ilk(List<string> liste, int n)
        {
            if (liste.Count <= n)
            {
                return liste;
            }

            List<string> kisa = liste.GetRange(0, n);
            kisa.Add("… (+" + (liste.Count - n) + " daha)");
            return kisa;
        }
    }
}
