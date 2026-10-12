using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-108 · AP-040 v001 world kit gate (P-05 plaza / garden / hf: 47 families, 110 FBX; staged by wp-108/ap040-intake/stage-ap040.py,
    /// built by ArtPipelineMenu.RunLibraryAp040). Assumes the staging and the run happened: the prefabs exist.
    ///
    /// One prefab per manifest variation (110), each with a root LODGroup of three LODs whose meshes are SM_&lt;family&gt;_VNN_LOD0|1|2;
    /// collider proxies never render and never sit in the LODGroup, flora has none; plaza collider tops stay out of the S-05 §6 band
    /// (0.30, 0.50) m and, Y5, at most 1.20 m when they would fall in (1.20, 1.39] m (barricade_cart 1.225 and hokora 1.220 in the
    /// delivery, lowered by the builder); 18 sockets at the root at the manifest point, the Blender ".001" duplicates of toro_post V02 read
    /// by their SOCKET_ prefix; the materials bind the WP-107B shared atlas in place (MaskMap as delivered, glow × 2.5, flora on DD/Foliage
    /// Wind without wind, both faces); AP040_shadow_cast is obeyed; the flora FBX carry no lightmap UVs. Counts come from the manifest
    /// and the kit tables, cross-checked here.
    /// </summary>
    [TestFixture]
    [Category("WP108")]
    public sealed class Ap040KitTests
    {
        private const string Ap = ArtAp040WorldKit.Package;
        private const float BantAlt = 0.30f, BantUst = 0.50f, Tolerans = 0.005f, SoketTolerans = 0.001f, SoketAciTolerans = 0.1f;
        private const float AtesUstuSinir = 1.20f, Y5BantUst = 1.39f;
        /// <summary>The two plaza families whose delivered proxy tops sit in the Y5 band (1.225 / 1.220 m).</summary>
        private static readonly string[] Y5Aileleri = { "barricade_cart", "hokora" };

        private static ArtLibraryManifest Manifest()
        {
            string yol = ArtLibraryPaths.ManifestJson(Ap);
            Assert.IsTrue(File.Exists(yol), "Manifest yok: " + yol + " (stage-ap040.py --wt koşmadı mı?)");
            return ArtLibraryManifest.FromJson(File.ReadAllText(yol));
        }

        private static IEnumerable<(ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab)> Prefablar()
        {
            foreach (ArtLibraryManifest.Family f in Manifest().families)
            foreach (ArtLibraryManifest.Variation v in f.variations)
            {
                string yol = ArtLibraryPaths.Prefab(Ap, f.family, v.prefab);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(yol);
                Assert.IsNotNull(prefab, "prefab yok: " + yol + " (RunLibraryAp040 koşmadı mı?)");
                yield return (f, v, prefab);
            }
        }

        private static string F(float x) => x.ToString("0.000", CultureInfo.InvariantCulture);

        /// <summary>Collider top in prefab-root space, written here independently of the builder (asset prefabs are not in a scene: corners are mapped).</summary>
        private static float Ust(Collider c, Transform kok)
        {
            Matrix4x4 m = kok.worldToLocalMatrix * c.transform.localToWorldMatrix;
            float ust = float.MinValue;
            Vector3 merkez, yari;
            if (c is BoxCollider b)
            {
                merkez = b.center;
                yari = b.size * 0.5f;
            }
            else if (c is MeshCollider mc && mc.sharedMesh != null)
            {
                merkez = mc.sharedMesh.bounds.center;
                yari = mc.sharedMesh.bounds.extents;
            }
            else if (c is CapsuleCollider k)
            {
                Vector3 eksen = k.direction == 0 ? Vector3.right : k.direction == 1 ? Vector3.up : Vector3.forward;
                float boy = Mathf.Max(0f, k.height * 0.5f - k.radius);
                foreach (float s in new[] { -1f, 1f }) ust = Mathf.Max(ust, m.MultiplyPoint3x4(k.center + eksen * (s * boy)).y + k.radius * m.lossyScale.y);
                return ust;
            }
            else
            {
                return float.NaN;
            }

            for (int i = 0; i < 8; i++)
            {
                var kose = merkez + Vector3.Scale(yari, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
                ust = Mathf.Max(ust, m.MultiplyPoint3x4(kose).y);
            }

            return ust;
        }

        // ================================================================== kit tables ↔ manifest

        [Test]
        public void Ap040_KitTablolari_ManifestleTutarli_AdlandirmaSaglam()
        {
            ArtLibraryManifest m = Manifest();
            var hatalar = new List<string>();
            int plaza = 0, bahce = 0, hf = 0, flora = 0;
            foreach (ArtLibraryManifest.Family f in m.families)
            {
                int grup = (ArtAp040WorldKit.IsPlaza(f.family) ? 1 : 0) + (ArtAp040WorldKit.IsGarden(f.family) ? 1 : 0) + (ArtAp040WorldKit.IsHf(f.family) ? 1 : 0);
                if (grup != 1) hatalar.Add(f.family + ": " + grup + " grupta (tam bir: plaza / garden / hf)");
                if (ArtAp040WorldKit.IsPlaza(f.family)) plaza++;
                if (ArtAp040WorldKit.IsGarden(f.family)) bahce++;
                if (ArtAp040WorldKit.IsHf(f.family)) hf++;
                bool ruzgar = false;
                foreach (ArtLibraryManifest.Variation v in f.variations) foreach (ArtLibraryManifest.Part p in v.parts) ruzgar |= p.wind;
                if (ruzgar != ArtAp040WorldKit.IsFlora(f.family)) hatalar.Add(f.family + ": manifest rüzgârı " + ruzgar + " ≠ flora tablosu " + ArtAp040WorldKit.IsFlora(f.family));
                if (ruzgar) flora++;
                if (ArtAp040WorldKit.ShadowCastVariationCount(f.family) != f.variations.Count)
                    hatalar.Add(f.family + ": AP040_shadow_cast satırı " + ArtAp040WorldKit.ShadowCastVariationCount(f.family) + " varyasyon, manifest " + f.variations.Count);
                foreach (ArtLibraryManifest.Variation v in f.variations)
                foreach (string sorun in ArtAp040WorldKit.NameProblems(f, v)) hatalar.Add(v.prefab + ": " + sorun);
            }

            Assert.AreEqual(ArtAp040WorldKit.ExpectedFamilies, m.families.Count, "AP-040 v001 dünya kiti 47 aile");
            Assert.AreEqual(ArtAp040WorldKit.ExpectedVariations, m.VariationCount, "AP-040 v001 dünya kiti 110 varyasyon (FBX)");
            Assert.AreEqual(18, plaza, "plaza 18 aile");
            Assert.AreEqual(25, bahce, "garden 25 aile");
            Assert.AreEqual(4, hf, "hf 4 aile");
            Assert.AreEqual(ArtAp040WorldKit.FloraFamilies.Length, flora, "vc1 taşıyan aileler = flora tablosu (13 + momiji_hf)");
            Assert.IsEmpty(hatalar, "AP-040 tablo / adlandırma ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }

        // ================================================================== prefabs

        [Test]
        public void Ap040_PrefabSayisi_ManifestVaryasyonlari_HerbirindeUcLodluLodGroup()
        {
            var hatalar = new List<string>();
            int prefabSayisi = 0;
            foreach ((ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab) in Prefablar())
            {
                prefabSayisi++;
                string on = v.prefab;
                if (prefab.name != "SM_" + f.family + "_" + v.id) hatalar.Add(on + ": kök adı " + prefab.name);
                var lodGroup = prefab.GetComponent<LODGroup>();
                if (lodGroup == null) { hatalar.Add(on + ": kökte LODGroup yok"); continue; }
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length != 3) { hatalar.Add(on + ": " + lods.Length + " LOD"); continue; }
                for (int i = 0; i < 3; i++)
                {
                    if (lods[i].renderers == null || lods[i].renderers.Length == 0) { hatalar.Add(on + ": LOD" + i + " boş"); continue; }
                    foreach (Renderer r in lods[i].renderers)
                    {
                        var mf = r != null ? r.GetComponent<MeshFilter>() : null;
                        string beklenen = "SM_" + f.family + "_" + v.id + "_LOD" + i;
                        if (mf == null || mf.sharedMesh == null) hatalar.Add(on + ": LOD" + i + " mesh yok");
                        else if (!mf.sharedMesh.name.StartsWith(beklenen, StringComparison.Ordinal)) hatalar.Add(on + ": LOD" + i + " mesh '" + mf.sharedMesh.name + "' ≠ " + beklenen);
                        if (r != null && r.name != "LOD" + i) hatalar.Add(on + ": LOD" + i + " renderer adı " + r.name);
                    }
                }
            }

            Assert.AreEqual(Manifest().VariationCount, prefabSayisi, "prefab sayısı = AP040_Library varyasyon sayısı");
            Assert.AreEqual(ArtAp040WorldKit.ExpectedVariations, prefabSayisi, "AP-040 v001: 110 prefab");
            Assert.IsEmpty(hatalar, "AP-040 prefab / LOD ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp108.ap040.prefabs count=" + prefabSayisi);
        }

        [Test]
        public void Ap040_ColliderProxyleri_RenderEdilmez_LodGroupDisinda_BitkilerdeColliderYok()
        {
            var hatalar = new List<string>();
            int proxy = 0, kutu = 0, mesh = 0, beklenenToplam = 0;
            foreach ((ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab) in Prefablar())
            {
                string on = v.prefab;
                proxy += v.colliders.Count;
                var lodRendererlari = new HashSet<Renderer>();
                var lodGroup = prefab.GetComponent<LODGroup>();
                if (lodGroup != null) foreach (LOD l in lodGroup.GetLODs()) foreach (Renderer r in l.renderers) if (r != null) lodRendererlari.Add(r);

                // No proxy mesh is drawn anywhere in the prefab, and no collider object carries a renderer.
                foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && mf.sharedMesh.name.EndsWith("_COL", StringComparison.Ordinal)) hatalar.Add(on + ": proxy mesh render ediliyor (" + mf.name + " → " + mf.sharedMesh.name + ")");
                Collider[] hepsi = prefab.GetComponentsInChildren<Collider>(true);
                foreach (Collider c in hepsi)
                {
                    Renderer r = c.GetComponent<Renderer>();
                    if (r != null) hatalar.Add(on + "/" + c.name + ": collider nesnesinde renderer var");
                    if (r != null && lodRendererlari.Contains(r)) hatalar.Add(on + "/" + c.name + ": collider LODGroup'ta");
                    if (c is BoxCollider) kutu++;
                    else if (c is MeshCollider mc)
                    {
                        mesh++;
                        if (!mc.convex) hatalar.Add(on + "/" + c.name + ": MeshCollider convex değil");
                    }
                    else hatalar.Add(on + "/" + c.name + ": " + c.GetType().Name + " (AP-040: kutu ya da convex MeshCollider)");
                }

                if (ArtAp040WorldKit.IsFlora(f.family))
                {
                    if (v.colliders.Count > 0) hatalar.Add(on + ": manifestte bitki proxy'si var (" + v.colliders.Count + ")");
                    if (hepsi.Length > 0) hatalar.Add(on + ": bitkide " + hepsi.Length + " collider");
                    continue;
                }

                // Expected: one box per shell when every shell is a box, else one convex mesh collider per proxy.
                int beklenen = 0;
                foreach (ArtLibraryManifest.Collider c in v.colliders)
                {
                    bool kutular = c.shellBoxes != null && c.shellBoxes.Count > 0;
                    if (c.shellBoxes != null) foreach (ArtLibraryManifest.ShellBox s in c.shellBoxes) kutular &= s.IsBox;
                    beklenen += kutular ? c.shellBoxes.Count : 1;
                }

                beklenenToplam += beklenen;
                if (hepsi.Length != beklenen) hatalar.Add(on + ": " + hepsi.Length + " collider ≠ beklenen " + beklenen + " (" + v.colliders.Count + " proxy)");
            }

            Assert.AreEqual(ArtAp040WorldKit.ExpectedColliderProxies, proxy, "AP-040 v001: 34 collider proxy nesnesi");
            Assert.AreEqual(beklenenToplam, kutu + mesh, "collider toplamı");
            Assert.Greater(kutu, 0, "Hiç kutu collider yok; tarama yalanlanmıştır.");
            Assert.IsEmpty(hatalar, "AP-040 collider ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp108.ap040.colliders proxies=" + proxy + " box=" + kutu + " convexMesh=" + mesh);
        }

        [Test]
        public void Ap040_MeydanColliderUstleri_YasakBantBos_Y5Bandinda120Ustu_Yok()
        {
            var hatalar = new List<string>();
            var ustler = new List<string>();
            int collider = 0, y5 = 0;
            foreach ((ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab) in Prefablar())
            {
                if (!ArtAp040WorldKit.IsPlaza(f.family)) continue;
                foreach (Collider c in prefab.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger) continue;
                    float ust = Ust(c, prefab.transform);
                    string ad = v.prefab + "/" + c.name;
                    if (float.IsNaN(ust)) { hatalar.Add(ad + ": " + c.GetType().Name + " ölçülemedi"); continue; }
                    collider++;
                    ustler.Add(ad + " " + F(ust));
                    if (ust > BantAlt + Tolerans && ust < BantUst - Tolerans)
                        hatalar.Add(ad + ": collider üstü " + F(ust) + " m yasak bantta (S-05 §6: ≤ 0,30 ya da ≥ 0,50)");
                    if (ust > AtesUstuSinir + Tolerans && ust <= Y5BantUst + Tolerans)
                        hatalar.Add(ad + ": collider üstü " + F(ust) + " m Y5 bandında (S-05 Y5: üstünden ateş edilen engel ≤ 1,20)");
                    if (Array.IndexOf(Y5Aileleri, f.family) >= 0)
                    {
                        y5++;
                        if (ust > AtesUstuSinir + Tolerans) hatalar.Add(ad + ": collider üstü " + F(ust) + " m > 1,20 (teslimde 1,22; kurucu indirmeliydi)");
                        if (ust < AtesUstuSinir - Tolerans) hatalar.Add(ad + ": collider üstü " + F(ust) + " m — 1,20'ye indirilmeliydi, daha aşağı değil");
                    }
                }
            }

            Assert.Greater(collider, 0, "Hiç meydan collider'ı yok; tarama yalanlanmıştır.");
            Assert.AreEqual(4, y5, "barricade_cart V01/V02 + hokora V01/V02: birer kutu");
            Assert.IsEmpty(hatalar, "AP-040 meydan collider ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp108.ap040.plazaTops colliders=" + collider + " tops: " + string.Join(", ", ustler));
        }

        // ================================================================== sockets

        [Test]
        public void Ap040_Soketler_18_KokteManifestNoktasinda_BlenderEkiOnekle()
        {
            var hatalar = new List<string>();
            int soket = 0;
            var turler = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach ((ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab) in Prefablar())
            {
                if (v.sockets == null || v.sockets.Count == 0) continue;
                string on = v.prefab;
                string fbxYolu = ArtLibraryPaths.Fbx(Ap, f.family, v.parts[0].lods[0].fbx);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxYolu);
                if (model == null) hatalar.Add(on + ": FBX model varlığı yüklenmedi (" + fbxYolu + ")");
                foreach (ArtLibraryManifest.Socket m in v.sockets)
                {
                    if (!m.name.StartsWith("SOCKET_", StringComparison.Ordinal)) hatalar.Add(on + ": soket adı " + m.name);
                    string tur = ArtLibraryManifest.Socket.KindOf(m.name);
                    turler[tur] = (turler.TryGetValue(tur, out int n) ? n : 0) + 1;
                    Transform t = prefab.transform.Find(m.name);
                    if (t == null) { hatalar.Add(on + ": prefab kökünde " + m.name + " yok"); continue; }
                    soket++;
                    if (Vector3.Distance(t.localPosition, m.PrefabPosition) > SoketTolerans)
                        hatalar.Add(on + " " + m.name + ": prefab " + t.localPosition.ToString("F4") + " ≠ manifest " + m.PrefabPosition.ToString("F4"));
                    if (Quaternion.Angle(t.localRotation, m.PrefabRotation) > SoketAciTolerans)
                        hatalar.Add(on + " " + m.name + ": prefab dönüşü " + t.localRotation.eulerAngles.ToString("F2") + " ≠ manifest yaw " + F(m.yawDeg) + "°");
                    if (model == null) continue;
                    Transform fbxSoket = null;
                    foreach (Transform c in model.GetComponentsInChildren<Transform>(true)) if (c.name == m.name) { fbxSoket = c; break; }
                    if (fbxSoket == null) { hatalar.Add(on + ": FBX importer " + m.name + " düğümünü düşürdü ya da yeniden adlandırdı (" + fbxYolu + ")"); continue; }
                    Vector3 fbxNokta = model.transform.InverseTransformPoint(fbxSoket.position);
                    if (Vector3.Distance(fbxNokta, m.PrefabPosition) > SoketTolerans)
                        hatalar.Add(on + " " + m.name + ": FBX düğümü model uzayında " + fbxNokta.ToString("F4") + " ≠ manifest " + m.PrefabPosition.ToString("F4"));
                }

                // Prefix lookup: every root child whose kind is SOCKET_Flame / SOCKET_Light, the Blender ".001" copies included.
                foreach (string tur in new[] { "SOCKET_Flame", "SOCKET_Light" })
                {
                    int manifestte = v.sockets.FindAll(s => ArtLibraryManifest.Socket.KindOf(s.name) == tur).Count, kokte = 0;
                    foreach (Transform c in prefab.transform) if (ArtLibraryManifest.Socket.KindOf(c.name) == tur) kokte++;
                    if (manifestte != kokte) hatalar.Add(on + ": " + tur + " önekli kök çocuğu " + kokte + " ≠ manifest " + manifestte);
                }
            }

            ArtLibraryManifest.Family toro = Manifest().families.Find(x => x.family == "toro_post");
            Assert.IsNotNull(toro, "toro_post manifestte yok");
            ArtLibraryManifest.Variation toroV02 = toro.variations.Find(x => x.id == "V02");
            Assert.IsNotNull(toroV02, "toro_post V02 manifestte yok");
            Assert.AreEqual(2, toroV02.sockets.FindAll(s => ArtLibraryManifest.Socket.KindOf(s.name) == "SOCKET_Flame").Count, "toro_post V02: SOCKET_Flame + SOCKET_Flame.001 önekle iki alev");
            Assert.AreEqual(2, toroV02.sockets.FindAll(s => ArtLibraryManifest.Socket.KindOf(s.name) == "SOCKET_Light").Count, "toro_post V02: SOCKET_Light + SOCKET_Light.001 önekle iki ışık");
            Assert.AreEqual(ArtAp040WorldKit.ExpectedSockets, soket, "AP-040 v001: 18 soket (stone_lantern, toro_post, yatai, ishidoro_small)");
            Assert.IsEmpty(hatalar, "AP-040 soket ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            var ozet = new List<string>();
            foreach (KeyValuePair<string, int> kv in turler) ozet.Add(kv.Key + "×" + kv.Value);
            Debug.Log("[DD-TEST] wp108.ap040.sockets count=" + soket + " kinds: " + string.Join(", ", ozet));
        }

        // ================================================================== materials and shadows

        [Test]
        public void Ap040_Malzemeler_PaylasilanAtlasYerinde_MaskMapDogrudan_Glow25_FloraRuzgarsizCiftYuz()
        {
            var hatalar = new List<string>();
            string[] paylasilan = ArtAp040WorldKit.SharedTextureFiles();   // BaseColor, Normal, MaskMap, Emission
            string Yol(Texture t) => t != null ? AssetDatabase.GetAssetPath(t) : "null";
            Material Yukle(string rol)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material(rol));
                if (mat == null) hatalar.Add(ArtPipelinePaths.Material(rol) + ": yok");
                return mat;
            }

            foreach (string rol in new[] { "ap040_props", "ap040_glow" })
            {
                Material m = Yukle(rol);
                if (m == null) continue;
                if (m.shader == null || m.shader.name != ArtMaterialBuilder.LitShaderName) hatalar.Add(rol + ": shader URP/Lit değil");
                if (Yol(m.GetTexture("_BaseMap")) != paylasilan[0]) hatalar.Add(rol + ": _BaseMap " + Yol(m.GetTexture("_BaseMap")) + " ≠ " + paylasilan[0]);
                if (Yol(m.GetTexture("_BumpMap")) != paylasilan[1]) hatalar.Add(rol + ": _BumpMap " + Yol(m.GetTexture("_BumpMap")) + " ≠ " + paylasilan[1]);
                if (Yol(m.GetTexture("_MetallicGlossMap")) != paylasilan[2]) hatalar.Add(rol + ": _MetallicGlossMap " + Yol(m.GetTexture("_MetallicGlossMap")) + " ≠ " + paylasilan[2] + " (MaskMap doğrudan)");
                if (Yol(m.GetTexture("_OcclusionMap")) != paylasilan[2]) hatalar.Add(rol + ": _OcclusionMap " + Yol(m.GetTexture("_OcclusionMap")) + " ≠ MaskMap (AO G kanalında)");
                foreach (string k in new[] { "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP" })
                    if (!m.IsKeywordEnabled(k)) hatalar.Add(rol + ": " + k + " kapalı");
                if (Mathf.Abs(m.GetFloat("_Metallic") - 1f) > 1e-4f || Mathf.Abs(m.GetFloat("_Smoothness") - 1f) > 1e-4f) hatalar.Add(rol + ": skalerler 1 değil (harita otorite olmalı)");
                if (m.GetFloat("_SmoothnessTextureChannel") != 0f) hatalar.Add(rol + ": smoothness Metallic alfasından okunmalı");
                if (File.Exists(ArtPipelinePaths.DerivedMask(ArtLibraryMaterialRoles.Ap040SharedTextureRole))) hatalar.Add(rol + ": paylaşılan atlas için türetilmiş maske üretilmiş (repack yok olmalı)");
            }

            Material glow = Yukle("ap040_glow");
            if (glow != null)
            {
                if (!glow.IsKeywordEnabled("_EMISSION")) hatalar.Add("ap040_glow: _EMISSION kapalı");
                if (Yol(glow.GetTexture("_EmissionMap")) != paylasilan[3]) hatalar.Add("ap040_glow: _EmissionMap " + Yol(glow.GetTexture("_EmissionMap")) + " ≠ " + paylasilan[3]);
                Color e = glow.GetColor("_EmissionColor").linear;
                float g = ArtLibraryMaterialRoles.Ap040GlowIntensity;
                if (Mathf.Abs(e.r - g) > 0.02f || Mathf.Abs(e.g - g) > 0.02f || Mathf.Abs(e.b - g) > 0.02f) hatalar.Add("ap040_glow: _EmissionColor lineer " + e + " ≠ beyaz × " + g);
                if ((glow.globalIlluminationFlags & MaterialGlobalIlluminationFlags.BakedEmissive) == 0) hatalar.Add("ap040_glow: GI bayrağı " + glow.globalIlluminationFlags);
            }

            foreach (string rol in new[] { "ap040_flora", "ap040_momiji_hf" })
            {
                Material m = Yukle(rol);
                if (m == null) continue;
                if (m.shader == null || m.shader.name != ArtMaterialBuilder.FoliageWindShaderName) hatalar.Add(rol + ": shader " + (m.shader != null ? m.shader.name : "null") + " ≠ DD/Foliage Wind");
                if (Mathf.Abs(m.GetFloat("_WindScale")) > 1e-4f) hatalar.Add(rol + ": _WindScale " + m.GetFloat("_WindScale") + " (vc1 AP003_Wind değil: 0)");
                if (Mathf.Abs(m.GetFloat("_BackfaceLighting")) > 1e-4f) hatalar.Add(rol + ": _BackfaceLighting " + m.GetFloat("_BackfaceLighting") + " (üst yarıküre normalleri çevrilmez: 0)");
                if (m.GetFloat("_Cull") != 0f) hatalar.Add(rol + ": _Cull " + m.GetFloat("_Cull") + " (iki yüz: 0)");
                if (m.IsKeywordEnabled("_ALPHATEST_ON")) hatalar.Add(rol + ": _ALPHATEST_ON açık (atlas alfası her yerde 255; kesim yok)");
                if (Mathf.Abs(m.GetFloat("_Smoothness") - ArtLibraryMaterialRoles.Ap040FloraSmoothness) > 1e-4f) hatalar.Add(rol + ": _Smoothness " + m.GetFloat("_Smoothness"));
            }

            Material floraMat = Yukle("ap040_flora");
            if (floraMat != null && Yol(floraMat.GetTexture("_BaseMap")) != paylasilan[0]) hatalar.Add("ap040_flora: _BaseMap " + Yol(floraMat.GetTexture("_BaseMap")) + " ≠ " + paylasilan[0]);
            Material momiji = Yukle("ap040_momiji_hf");
            if (momiji != null && Yol(momiji.GetTexture("_BaseMap")) != ArtPipelinePaths.SourceTexture("ap040_momiji_hf", "BaseColor")) hatalar.Add("ap040_momiji_hf: _BaseMap " + Yol(momiji.GetTexture("_BaseMap")));

            foreach (string aile in ArtLibraryMaterialRoles.Ap040HfLitFamilies)
            {
                string rol = ArtLibraryMaterialRoles.Ap040RolePrefix + aile;
                Material m = Yukle(rol);
                if (m == null) continue;
                if (m.shader == null || m.shader.name != ArtMaterialBuilder.LitShaderName) hatalar.Add(rol + ": shader URP/Lit değil");
                if (Yol(m.GetTexture("_BaseMap")) != ArtPipelinePaths.SourceTexture(rol, "BaseColor")) hatalar.Add(rol + ": _BaseMap " + Yol(m.GetTexture("_BaseMap")));
                if (Yol(m.GetTexture("_MetallicGlossMap")) != ArtPipelinePaths.SourceTexture(rol, "MaskMap")) hatalar.Add(rol + ": _MetallicGlossMap " + Yol(m.GetTexture("_MetallicGlossMap")) + " (MaskMap doğrudan)");
                if (Yol(m.GetTexture("_OcclusionMap")) != ArtPipelinePaths.SourceTexture(rol, "MaskMap")) hatalar.Add(rol + ": _OcclusionMap " + Yol(m.GetTexture("_OcclusionMap")));
            }

            // Every renderer of the kit draws an ap040_* material.
            foreach ((ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab) in Prefablar())
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (Material mat in r.sharedMaterials)
                        if (mat == null || !mat.name.StartsWith("M_" + ArtLibraryMaterialRoles.Ap040RolePrefix, StringComparison.Ordinal))
                            hatalar.Add(v.prefab + "/" + r.name + ": malzeme " + (mat != null ? mat.name : "null"));

            Assert.IsEmpty(hatalar, "AP-040 malzeme ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }

        [Test]
        public void Ap040_Golge_AP040ShadowCastUyulur_Lod2Kapali()
        {
            var hatalar = new List<string>();
            int dusuren = 0, dusurmeyen = 0;
            foreach ((ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab) in Prefablar())
            {
                bool golge = ArtAp040WorldKit.CastsShadow(f.family, v.id);
                if (golge) dusuren++; else dusurmeyen++;
                Transform geometry = prefab.transform.Find(ArtLibraryPrefabBuilder.GeometryName);
                if (geometry == null) { hatalar.Add(v.prefab + ": Geometry yok"); continue; }
                for (int lod = 0; lod < 3; lod++)
                {
                    Transform t = geometry.Find("LOD" + lod);
                    var r = t != null ? t.GetComponent<Renderer>() : null;
                    if (r == null) { hatalar.Add(v.prefab + ": LOD" + lod + " renderer yok"); continue; }
                    ShadowCastingMode beklenen = golge && lod < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    if (r.shadowCastingMode != beklenen) hatalar.Add(v.prefab + " LOD" + lod + ": gölge " + r.shadowCastingMode + " ≠ " + beklenen + " (AP040_shadow_cast " + (golge ? 1 : 0) + ")");
                }
            }

            Assert.AreEqual(45, dusuren, "AP040_shadow_cast 1: 45 varyasyon");
            Assert.AreEqual(65, dusurmeyen, "AP040_shadow_cast 0: 65 varyasyon (tutamlar, döküntü, alçak dekor)");
            Assert.IsEmpty(hatalar, "AP-040 gölge ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }

        // ================================================================== import

        [Test]
        public void Ap040_Import_BitkiFbxUv2siz_DigerleriUv2li_HaritalarDogrusal()
        {
            var hatalar = new List<string>();
            int flora = 0, diger = 0;
            var gorulen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArtLibraryManifest.Family f in Manifest().families)
            foreach (ArtLibraryManifest.Variation v in f.variations)
            {
                string yol = ArtLibraryPaths.Fbx(Ap, f.family, v.parts[0].lods[0].fbx);
                if (!gorulen.Add(yol)) continue;
                var importer = AssetImporter.GetAtPath(yol) as ModelImporter;
                if (importer == null) { hatalar.Add(yol + ": ModelImporter yok"); continue; }
                bool bitki = ArtAp040WorldKit.IsFlora(f.family);
                if (bitki) flora++; else diger++;
                if (ArtAp040WorldKit.IsFloraModel(yol) != bitki) hatalar.Add(yol + ": IsFloraModel " + !bitki);
                if (importer.generateSecondaryUV == bitki) hatalar.Add(yol + ": generateSecondaryUV " + importer.generateSecondaryUV + " (bitki UV2'siz, diğerleri UV2'li)");
                if (importer.isReadable) hatalar.Add(yol + ": Read/Write açık");
                List<string> sozlesme = ArtImportContract.ValidateModel(importer);
                if (sozlesme.Count > 0) hatalar.Add(yol + ": " + string.Join("; ", sozlesme));
            }

            List<string> haritalar = ArtAp040WorldKit.StagedTextureFiles();
            Assert.AreEqual(ArtAp040WorldKit.ExpectedStagedTextures, haritalar.Count, "14 T_ap040_* haritası");
            foreach (string yol in haritalar)
            {
                var importer = AssetImporter.GetAtPath(yol) as TextureImporter;
                if (importer == null) { hatalar.Add(yol + ": yok / TextureImporter yok"); continue; }
                ArtImportContract.TextureRole rol = ArtImportContract.RoleOf(yol);
                if (rol == ArtImportContract.TextureRole.Unknown) hatalar.Add(yol + ": rol Unknown");
                bool dogrusal = rol == ArtImportContract.TextureRole.Normal || rol == ArtImportContract.TextureRole.MaskMap ||
                                rol == ArtImportContract.TextureRole.VegetationOrm || rol == ArtImportContract.TextureRole.PetalMask;
                if (importer.sRGBTexture == dogrusal) hatalar.Add(yol + ": sRGB " + importer.sRGBTexture + " (" + rol + ")");
                if (rol == ArtImportContract.TextureRole.MaskMap && importer.alphaSource != TextureImporterAlphaSource.FromInput) hatalar.Add(yol + ": MaskMap alfası (smoothness) korunmalı");
                List<string> sozlesme = ArtImportContract.ValidateTexture(importer, rol);
                if (sozlesme.Count > 0) hatalar.Add(yol + ": " + string.Join("; ", sozlesme));
            }

            foreach (string yol in ArtAp040WorldKit.SharedTextureFiles())
                if (!File.Exists(yol)) hatalar.Add(yol + ": WP-107B paylaşılan atlas haritası yok");

            Assert.AreEqual(40, flora, "bitki FBX'i: 13 bahçe florası × 3 varyasyon + momiji_hf = 40");
            Assert.AreEqual(70, diger, "bitki dışı FBX: 110 − 40 = 70");
            Assert.IsEmpty(hatalar, "AP-040 içe aktarma ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp108.ap040.import floraFbx=" + flora + " otherFbx=" + diger + " stagedMaps=" + haritalar.Count);
        }
    }
}
