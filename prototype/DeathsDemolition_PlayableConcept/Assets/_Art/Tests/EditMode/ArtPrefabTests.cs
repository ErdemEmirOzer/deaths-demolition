using System;
using System.Collections.Generic;
using System.Globalization;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-011 · 18 aile prefabı (SM_&lt;family&gt;.prefab) ve ArtFamilyLibrary nöbetçisi.
    /// Prefab spec (01-ORTAK-SOZLESME): kök LODGroup + katman; Geometry çocuğu
    /// <see cref="ArtImportContract.GeometryRotation"/> ile döndürülmüş; LOD0/1/2 ilgili FBX'in
    /// Mesh alt varlığı + M_&lt;role&gt; (URP/Lit); collider'lar parça sınırından primitif
    /// (28 Box + 1 Capsule, MeshCollider yok).
    ///
    /// YÜK TAŞIYAN KONTROLLER (neden burada):
    ///  - Katman yalnız kökte değil, her çocukta: NavMesh (WorldNavigation) yalnız
    ///    Environment|CameraOccluder katmanındaki COLLIDER'ları toplar; mermi ve kamera
    ///    saydamlaşması da collider katmanını okur. Varsayılan katmanda kalan bir COL_ çocuğu
    ///    duvardan geçen düşman demektir ve hiçbir yerde hata loglamaz.
    ///  - LOD mesh'i doğru FBX'ten: yanlış LOD'a bağlanmış bir mesh görsel olarak fark edilmez,
    ///    ölçüm (üçgen/LOD sayacı) sessizce yalan söyler.
    ///  - Prefab uzayı AABB'si = sözleşme uzayı kanonik AABB (1 mm): geometrinin prefab
    ///    içinde dik ve doğru yönde durduğunun doğrudan kanıtı (layout paritesinden bağımsız).
    /// </summary>
    [TestFixture]
    [Category("WP011")]
    public sealed class ArtPrefabTests
    {
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        private const int BeklenenToplamCollider = 29;
        private const int BeklenenBoxCollider = 28;
        private const int BeklenenCapsuleCollider = 1;
        private const string CapsuleAilesi = "sakura_trunk";
        private const float SinirToleransMetre = 0.001f;

        private static GameObject Prefab(string aile)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPipelinePaths.FamilyPrefab(aile));
            Assert.IsNotNull(prefab, "Prefab yok: " + ArtPipelinePaths.FamilyPrefab(aile) + " (ArtFamilyPrefabBuilder koşmalı).");
            return prefab;
        }

        private static Transform Cocuk(Transform ebeveyn, string ad)
        {
            Transform t = ebeveyn.Find(ad);
            return t;
        }

        // ============================================================== varlık ve hiyerarşi

        [Test]
        public void OnSekizPrefab_VarVeKokAdiSMAile()
        {
            Assert.AreEqual(18, ArtFamilies.All.Length);
            var hatalar = new List<string>();
            foreach (string aile in ArtFamilies.All)
            {
                string yol = ArtPipelinePaths.FamilyPrefab(aile);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(yol);
                if (prefab == null)
                {
                    hatalar.Add(yol + ": yok.");
                    continue;
                }

                if (prefab.name != "SM_" + aile)
                {
                    hatalar.Add(yol + ": kök adı '" + prefab.name + "' ≠ 'SM_" + aile + "'.");
                }

                if (PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Regular)
                {
                    hatalar.Add(yol + ": prefab türü " + PrefabUtility.GetPrefabAssetType(prefab) + " (Regular beklenir; model prefabı değil).");
                }
            }

            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
        }

        [Test]
        public void GeometryCocugu_SozlesmeDonusuyle_LodCocuklariAltinda()
        {
            var hatalar = new List<string>();
            foreach (string aile in ArtFamilies.All)
            {
                GameObject prefab = Prefab(aile);
                Transform geometry = Cocuk(prefab.transform, "Geometry");
                if (geometry == null)
                {
                    hatalar.Add(aile + ": 'Geometry' çocuğu yok.");
                    continue;
                }

                float aci = Quaternion.Angle(geometry.localRotation, ArtImportContract.GeometryRotation);
                if (aci > 0.001f)
                {
                    hatalar.Add(aile + ": Geometry.localRotation " + geometry.localRotation.eulerAngles + " ≠ sözleşme " + ArtImportContract.GeometryRotation.eulerAngles + " (fark " + aci.ToString("0.0000", CultureInfo.InvariantCulture) + "°).");
                }

                if (geometry.localPosition.sqrMagnitude > 1e-10f || (geometry.localScale - Vector3.one).sqrMagnitude > 1e-10f)
                {
                    hatalar.Add(aile + ": Geometry yerel konum/ölçek birim değil " + geometry.localPosition + " / " + geometry.localScale + ".");
                }

                for (int lod = 0; lod < 3; lod++)
                {
                    Transform l = Cocuk(geometry, "LOD" + lod);
                    if (l == null)
                    {
                        hatalar.Add(aile + ": Geometry/LOD" + lod + " yok.");
                        continue;
                    }

                    if (l.localPosition.sqrMagnitude > 1e-10f || Quaternion.Angle(l.localRotation, Quaternion.identity) > 0.001f || (l.localScale - Vector3.one).sqrMagnitude > 1e-10f)
                    {
                        hatalar.Add(aile + ": LOD" + lod + " yerel TRS birim değil.");
                    }
                }
            }

            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
        }

        // ============================================================== LODGroup

        [Test]
        public void LodGroup_UcSeviye_KesinAzalanEsik_CrossFade()
        {
            var hatalar = new List<string>();
            var ozet = new System.Text.StringBuilder();
            foreach (string aile in ArtFamilies.All)
            {
                GameObject prefab = Prefab(aile);
                var group = prefab.GetComponent<LODGroup>();
                if (group == null)
                {
                    hatalar.Add(aile + ": kökte LODGroup yok.");
                    continue;
                }

                LOD[] lods = group.GetLODs();
                if (lods.Length != 3)
                {
                    hatalar.Add(aile + ": " + lods.Length + " LOD (3 beklenir).");
                    continue;
                }

                if (group.fadeMode != LODFadeMode.CrossFade || !group.animateCrossFading)
                {
                    hatalar.Add(aile + ": fadeMode " + group.fadeMode + " / animateCrossFading " + group.animateCrossFading + " (CrossFade + true beklenir; hat cross-fade açık).");
                }

                float onceki = float.PositiveInfinity;
                for (int i = 0; i < lods.Length; i++)
                {
                    float h = lods[i].screenRelativeTransitionHeight;
                    if (!(h < onceki) || h <= 0f || h > 1f)
                    {
                        hatalar.Add(aile + ": LOD" + i + " eşiği " + h.ToString("0.0000", CultureInfo.InvariantCulture) + " (önceki " + onceki + "); (0, 1] içinde ve kesin azalan olmalı.");
                    }

                    onceki = h;

                    if (lods[i].renderers == null || lods[i].renderers.Length == 0)
                    {
                        hatalar.Add(aile + ": LOD" + i + " renderer listesi boş.");
                        continue;
                    }

                    foreach (Renderer r in lods[i].renderers)
                    {
                        if (r == null)
                        {
                            hatalar.Add(aile + ": LOD" + i + " renderer listesinde null.");
                            continue;
                        }

                        if (r.transform.parent == null || r.transform.parent.name != "Geometry" || r.name != "LOD" + i)
                        {
                            hatalar.Add(aile + ": LOD" + i + " renderer'ı Geometry/LOD" + i + " düğümünde değil (" + r.name + ").");
                        }
                    }
                }

                ozet.Append(aile).Append('=').Append(lods[0].screenRelativeTransitionHeight.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append('/').Append(lods[1].screenRelativeTransitionHeight.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append('/').Append(lods[2].screenRelativeTransitionHeight.ToString("0.000", CultureInfo.InvariantCulture)).Append(' ');
            }

            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp011.prefab.lod " + ozet);
        }

        /// <summary>
        /// WP-074 B7 · TransitionHeights göz hizası sözleşmesi v2: s/(1,1547·d), d = 15 / 30 m yazılı (Yüksek lodBias 2'de 30 / 60 m;
        /// 0,85 yerleşim ölçeğinde bile E-8'in 25 / 50 m'si). Cull sınıfa göre: yapı 300 m (Performans'ta bile sis sonu), bitki 150 m
        /// (Yüksek'te 300), prop 22,5 m (Yüksek'te 45; LOD1→2 cull'ün ¾'ü). Ölçü en büyük kenar (Unity LODGroup.size, PRA K-10).
        /// [0.001, 0.95] kırpma, kesin azalan, her bant en az onda bir.
        /// </summary>
        [Test]
        public void TransitionHeights_GozHizasiSozlesmesi()
        {
            const float tol = 5e-4f, k = 1.1547f;
            var duvar = new Bounds(new Vector3(0f, 1.2f, 0f), new Vector3(4f, 2.4f, 0.6f));
            float[] yapi = ArtFamilyPrefabBuilder.TransitionHeights(duvar, ArtFamilyPrefabBuilder.LodClass.Structure);
            Assert.AreEqual(3, yapi.Length);
            Assert.AreEqual(4f / (k * 15f), yapi[0], tol, "LOD0→1: Yüksek'te 30 m (E-8 25 m / 0,85 ölçek)");
            Assert.AreEqual(4f / (k * 30f), yapi[1], tol, "LOD1→2: Yüksek'te 60 m (E-8 50 m / 0,85 ölçek)");
            Assert.AreEqual(4f / (k * 300f), yapi[2], tol, "Yapı: lodBias 1'de bile 300 m (dünya kenarı açılmaz)");

            float[] sugi = ArtFamilyPrefabBuilder.TransitionHeights(new Bounds(Vector3.zero, new Vector3(5f, 14f, 5f)), ArtFamilyPrefabBuilder.LodClass.Vegetation);
            Assert.AreEqual(14f / (k * 15f), sugi[0], tol, "Sugi LOD0→1 Yüksek'te 30 m.");
            Assert.AreEqual(14f / (k * 30f), sugi[1], tol, "Sugi LOD1→2 Yüksek'te 60 m.");
            Assert.AreEqual(14f / (k * 150f), sugi[2], tol, "Bitki: Yüksek'te 300 m, Performans'ta 150 m.");

            float[] bordur = ArtFamilyPrefabBuilder.TransitionHeights(new Bounds(Vector3.zero, new Vector3(3f, 0.16f, 0.2f)), ArtFamilyPrefabBuilder.LodClass.Prop);
            Assert.AreEqual(3f / (k * 15f), bordur[0], tol, "En büyük kenar (3 m) ölçü olmalı.");
            Assert.AreEqual(3f / (k * 16.875f), bordur[1], tol, "Prop LOD1→2 cull'ün ¾'ü.");
            Assert.AreEqual(3f / (k * 22.5f), bordur[2], tol, "Prop: Yüksek'te 45 m.");

            // 40 m bir yapı: LOD0 0,95'e kırpılır, LOD1 bandı onda birden inceyse yarıya iner (iki geçiş aynı anda olmaz).
            float[] dev = ArtFamilyPrefabBuilder.TransitionHeights(new Bounds(Vector3.zero, new Vector3(40f, 8f, 12f)), ArtFamilyPrefabBuilder.LodClass.Structure);
            Assert.AreEqual(0.95f, dev[0], 1e-6f, "Üst kırpma 0.95.");
            Assert.LessOrEqual(dev[1], dev[0] * 0.9f + 1e-6f, "LOD1 bandı en az onda bir.");
            Assert.LessOrEqual(dev[2], dev[1] * 0.9f + 1e-6f, "LOD2 bandı en az onda bir.");

            // Tek aileli prefab (AP-001 … AP-003): sınıf sınırlarından — alçak ve küçük prop; ≥ 2 m, ≥ 3 m uzun ya da occluder yapı.
            float[] minik = ArtFamilyPrefabBuilder.TransitionHeights(new Bounds(Vector3.zero, new Vector3(0.3f, 0.1f, 0.2f)));
            Assert.AreEqual(0.5f / (k * 15f), minik[0], tol, "0,5 m tabanı uygulanmalı.");
            Assert.AreEqual(0.5f / (k * 22.5f), minik[2], tol, "Alçak küçük prefab prop.");
            Assert.AreEqual(6f / (k * 300f), ArtFamilyPrefabBuilder.TransitionHeights(new Bounds(Vector3.zero, new Vector3(3f, 6f, 3f)))[2], tol, "≥ 2 m prefab yapı.");
            Assert.AreEqual(3.2f / (k * 300f), ArtFamilyPrefabBuilder.TransitionHeights(new Bounds(Vector3.zero, new Vector3(3.2f, 0.5f, 0.4f)))[2], tol, "≥ 3 m kiriş yapı.");
            Assert.AreEqual(1.2f / (k * 300f), ArtFamilyPrefabBuilder.TransitionHeights(new Bounds(Vector3.zero, new Vector3(1.2f, 1.2f, 0.3f)), true)[2], tol, "Occluder yapı.");
        }

        /// <summary>WP-074 B7 · Aile sınıfı: rüzgârlı ≥ 2 m → bitki; occluder, yürünür çarpıştırıcı, ≥ 2 m ya da ≥ 3 m kenar → yapı; kalan ve AP-017 süsü → prop.</summary>
        [Test]
        public void LodSinifi_AileVerisinden()
        {
            ArtLibraryManifest.Family Aile(string kind, Vector3 boyut, bool ruzgar, string carpistirici = null, bool occluder = false)
            {
                var v = new ArtLibraryManifest.Variation { boundsMin = new[] { -boyut.x / 2f, -boyut.y / 2f, 0f }, boundsMax = new[] { boyut.x / 2f, boyut.y / 2f, boyut.z } };
                v.parts.Add(new ArtLibraryManifest.Part { wind = ruzgar });
                if (carpistirici != null) v.colliders.Add(new ArtLibraryManifest.Collider { @class = carpistirici });
                var f = new ArtLibraryManifest.Family { kind = kind, occluder = occluder };
                f.variations.Add(v);
                return f;
            }

            ArtFamilyPrefabBuilder.LodClass yapi = ArtFamilyPrefabBuilder.LodClass.Structure, bitki = ArtFamilyPrefabBuilder.LodClass.Vegetation, prop = ArtFamilyPrefabBuilder.LodClass.Prop;
            Assert.AreEqual(yapi, ArtFamilyPrefabBuilder.ClassOf(Aile("", new Vector3(2f, 0.6f, 2.6f), false)), "halka duvarı kiti");
            Assert.AreEqual(bitki, ArtFamilyPrefabBuilder.ClassOf(Aile("", new Vector3(5f, 5f, 16f), true)), "sugi");
            Assert.AreEqual(prop, ArtFamilyPrefabBuilder.ClassOf(Aile("", new Vector3(2f, 0.2f, 1.4f), false, "blocking")), "alçak çit");
            Assert.AreEqual(prop, ArtFamilyPrefabBuilder.ClassOf(Aile("", new Vector3(1f, 1f, 1.2f), true)), "sazlık");
            Assert.AreEqual(yapi, ArtFamilyPrefabBuilder.ClassOf(Aile("", new Vector3(1.5f, 1f, 0.2f), false, "walkable")), "basamak taşı: yürünür, cull edilmez");
            Assert.AreEqual(yapi, ArtFamilyPrefabBuilder.ClassOf(Aile("", new Vector3(3.2f, 0.4f, 0.5f), false)), "kiriş ≥ 3 m");
            Assert.AreEqual(yapi, ArtFamilyPrefabBuilder.ClassOf(Aile("", new Vector3(1.2f, 0.3f, 1.2f), false, null, true)), "occluder");
            Assert.AreEqual(prop, ArtFamilyPrefabBuilder.ClassOf(Aile("breakable", new Vector3(1f, 1f, 2.1f), false, "walkable")), "AP-017 kırılabilir");
            Assert.AreEqual(prop, ArtFamilyPrefabBuilder.ClassOf(Aile("prop", new Vector3(1f, 1f, 2.0f), false)), "AP-017 prop");
        }

        // ============================================================== mesh ve malzeme

        [Test]
        public void LodMeshleri_DogruFbxten_MalzemeMRol_UrpLit()
        {
            var hatalar = new List<string>();
            foreach (string aile in ArtFamilies.All)
            {
                GameObject prefab = Prefab(aile);
                string rol = ArtFamilies.MaterialRole(aile);
                string malzemeYolu = ArtPipelinePaths.Material(rol);

                for (int lod = 0; lod < 3; lod++)
                {
                    Transform l = prefab.transform.Find("Geometry/LOD" + lod);
                    if (l == null)
                    {
                        hatalar.Add(aile + ": Geometry/LOD" + lod + " yok.");
                        continue;
                    }

                    var mf = l.GetComponent<MeshFilter>();
                    var mr = l.GetComponent<MeshRenderer>();
                    if (mf == null || mr == null)
                    {
                        hatalar.Add(aile + ": LOD" + lod + " MeshFilter/MeshRenderer eksik.");
                        continue;
                    }

                    string meshYolu = mf.sharedMesh != null ? AssetDatabase.GetAssetPath(mf.sharedMesh) : "(null)";
                    if (meshYolu != ArtPipelinePaths.FamilyLod(aile, lod))
                    {
                        hatalar.Add(aile + ": LOD" + lod + " mesh'i " + meshYolu + " (beklenen " + ArtPipelinePaths.FamilyLod(aile, lod) + ").");
                    }

                    Material m = mr.sharedMaterial;
                    if (m == null)
                    {
                        hatalar.Add(aile + ": LOD" + lod + " sharedMaterial null.");
                        continue;
                    }

                    if (mr.sharedMaterials.Length != 1)
                    {
                        hatalar.Add(aile + ": LOD" + lod + " " + mr.sharedMaterials.Length + " malzeme yuvası (1 beklenir; alt mesh tek).");
                    }

                    if (m.shader == null || m.shader.name != UrpLitShaderName)
                    {
                        hatalar.Add(aile + ": LOD" + lod + " shader '" + (m.shader != null ? m.shader.name : "null") + "' ≠ '" + UrpLitShaderName + "'.");
                    }

                    string mYolu = AssetDatabase.GetAssetPath(m);
                    if (mYolu != malzemeYolu)
                    {
                        hatalar.Add(aile + ": LOD" + lod + " malzemesi " + mYolu + " (beklenen " + malzemeYolu + ", rol '" + rol + "').");
                    }

                    if (!m.enableInstancing)
                    {
                        hatalar.Add(aile + ": " + mYolu + " enableInstancing kapalı.");
                    }

                    ShadowCastingMode golge = lod == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    if (mr.shadowCastingMode != golge) hatalar.Add(aile + ": LOD" + lod + " shadowCastingMode " + mr.shadowCastingMode + " (" + golge + " beklenir; ADR-012-1 LOD2 gölge kapalı).");
                    if (!mr.receiveShadows) hatalar.Add(aile + ": LOD" + lod + " receiveShadows kapalı.");
                    if (mr.lightProbeUsage != LightProbeUsage.Off) hatalar.Add(aile + ": LOD" + lod + " lightProbeUsage " + mr.lightProbeUsage + " (Off beklenir).");
                    if (mr.reflectionProbeUsage != ReflectionProbeUsage.Off) hatalar.Add(aile + ": LOD" + lod + " reflectionProbeUsage " + mr.reflectionProbeUsage + " (Off beklenir).");
                    if (mr.motionVectorGenerationMode != MotionVectorGenerationMode.ForceNoMotion) hatalar.Add(aile + ": LOD" + lod + " motionVectors " + mr.motionVectorGenerationMode + " (ForceNoMotion beklenir).");
                }
            }

            Assert.IsEmpty(hatalar, "LOD mesh/malzeme (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }

        /// <summary>
        /// Prefab uzayı AABB'si (mesh sınırı × Geometry/LOD matrisleri) = sözleşme uzayına taşınmış
        /// kanonik kaynak AABB (1 mm). Geometri yatık (Z-up) ya da 180° dönük olsaydı asimetrik
        /// aileler (palace_roof_bay z −6.5..4) burada kırmızıya dönerdi.
        /// </summary>
        [Test]
        public void PrefabUzayiAabb_KanonikKaynakAabbIle1mm_54Mesh()
        {
            var hatalar = new List<string>();
            int olculen = 0;
            float enKotu = 0f;
            foreach (string aile in ArtFamilies.All)
            {
                GameObject prefab = Prefab(aile);
                for (int lod = 0; lod < 3; lod++)
                {
                    Transform l = prefab.transform.Find("Geometry/LOD" + lod);
                    var mf = l != null ? l.GetComponent<MeshFilter>() : null;
                    ArtLayoutJson.LodBounds kaynak = ArtLayoutJson.CanonicalLod(aile, lod);
                    if (mf == null || mf.sharedMesh == null || kaynak == null)
                    {
                        hatalar.Add(aile + " LOD" + lod + ": mesh ya da manifest girişi yok.");
                        continue;
                    }

                    Vector3 bmin, bmax;
                    ArtLayoutJson.ToContractAabb(kaynak.Min, kaynak.Max, out bmin, out bmax);
                    Vector3 min, max;
                    ArtGateGeometry.TransformedAabb(mf.sharedMesh.bounds, ArtGateGeometry.LocalToRoot(l, prefab.transform), out min, out max);
                    float sapma = ArtGateGeometry.MaxSapma(min, max, bmin, bmax);
                    enKotu = Mathf.Max(enKotu, sapma);
                    olculen++;
                    if (sapma > SinirToleransMetre)
                    {
                        hatalar.Add(aile + " LOD" + lod + ": prefab " + ArtGateGeometry.Yaz(min, max) + " ≠ kaynak " + ArtGateGeometry.Yaz(bmin, bmax) + " (" + (sapma * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm)");
                    }
                }
            }

            Assert.AreEqual(54, olculen, "54 LOD mesh'i ölçülmeliydi.");
            Assert.IsEmpty(hatalar, "Prefab uzayı ↔ kaynak AABB (" + hatalar.Count + "/54):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp011.prefab.bounds measured=" + olculen + " worst=" + (enKotu * 1000f).ToString("0.000", CultureInfo.InvariantCulture) + "mm");
        }

        // ============================================================== katman

        [Test]
        public void Katman_KokVeTumCocuklar_AileTablosunaGore()
        {
            int env = PrototypeLayers.Environment;
            int occ = PrototypeLayers.CameraOccluder;
            Assert.IsTrue(env >= 0 && occ >= 0, "Environment/CameraOccluder katmanları projede tanımlı değil.");

            var hatalar = new List<string>();
            foreach (string aile in ArtFamilies.All)
            {
                GameObject prefab = Prefab(aile);
                int beklenen = ArtFamilies.IsOccluderCandidate(aile) ? occ : env;
                foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (t.gameObject.layer != beklenen)
                    {
                        hatalar.Add(aile + "/" + t.name + ": katman " + LayerMask.LayerToName(t.gameObject.layer) + " (beklenen " + LayerMask.LayerToName(beklenen) + ").");
                    }
                }
            }

            Assert.IsEmpty(hatalar, "Katman (" + hatalar.Count + "; NavMesh/mermi/saydamlaşma collider katmanını okur):\n" + string.Join("\n", hatalar));
        }

        // ============================================================== collider

        [Test]
        public void Colliderlar_28BoxArti1Capsule_MeshColliderYok_AileTablosuylaAyni()
        {
            var hatalar = new List<string>();
            int box = 0, capsule = 0, mesh = 0, diger = 0;
            var ozet = new System.Text.StringBuilder();

            foreach (string aile in ArtFamilies.All)
            {
                GameObject prefab = Prefab(aile);
                Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
                int aileBox = 0, aileCapsule = 0;
                foreach (Collider c in colliders)
                {
                    if (c is BoxCollider) { box++; aileBox++; }
                    else if (c is CapsuleCollider) { capsule++; aileCapsule++; }
                    else if (c is MeshCollider) { mesh++; hatalar.Add(aile + "/" + c.name + ": MeshCollider (yasak; primitif beklenir)."); }
                    else { diger++; hatalar.Add(aile + "/" + c.name + ": beklenmeyen collider türü " + c.GetType().Name + "."); }

                    if (c.isTrigger)
                    {
                        hatalar.Add(aile + "/" + c.name + ": isTrigger açık (NavMesh ve sphere cast trigger'ı görmez).");
                    }

                    if (!c.name.StartsWith("COL_", StringComparison.Ordinal))
                    {
                        hatalar.Add(aile + "/" + c.name + ": collider düğümü 'COL_<i>' adında değil.");
                    }
                }

                bool hasCollider = ArtFamilies.HasCollider(aile);
                if (hasCollider && colliders.Length == 0)
                {
                    hatalar.Add(aile + ": HasCollider true ama prefabda collider yok.");
                }

                if (!hasCollider && colliders.Length > 0)
                {
                    hatalar.Add(aile + ": HasCollider false ama prefabda " + colliders.Length + " collider var.");
                }

                if (aile == CapsuleAilesi ? aileCapsule != 1 : aileCapsule != 0)
                {
                    hatalar.Add(aile + ": CapsuleCollider " + aileCapsule + " (yalnız " + CapsuleAilesi + " için tam 1).");
                }

                // Collider sayısı = COLLIDERS FBX'indeki parça mesh sayısı (her parça bir primitif).
                if (hasCollider)
                {
                    int parca = 0;
                    foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(ArtPipelinePaths.FamilyColliders(aile)))
                    {
                        if (sub is Mesh) parca++;
                    }

                    if (parca != colliders.Length)
                    {
                        hatalar.Add(aile + ": prefabda " + colliders.Length + " collider, COLLIDERS FBX'inde " + parca + " parça.");
                    }
                }

                ozet.Append(aile).Append('=').Append(aileBox).Append('b').Append(aileCapsule).Append("c ");
            }

            Assert.IsEmpty(hatalar, "Collider (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Assert.AreEqual(BeklenenBoxCollider, box, "BoxCollider toplamı");
            Assert.AreEqual(BeklenenCapsuleCollider, capsule, "CapsuleCollider toplamı");
            Assert.AreEqual(0, mesh + diger, "MeshCollider/diğer toplamı");
            Assert.AreEqual(BeklenenToplamCollider, box + capsule, "Toplam collider");
            Debug.Log("[DD-TEST] wp011.prefab.colliders total=" + (box + capsule) + " " + ozet);
        }

        [Test]
        public void SakuraTrunkKapsulu_MeshZEkseni_DunyadaDikey_PozitifBoyut()
        {
            GameObject prefab = Prefab(CapsuleAilesi);
            var kapsul = prefab.GetComponentInChildren<CapsuleCollider>(true);
            Assert.IsNotNull(kapsul, CapsuleAilesi + " prefabında CapsuleCollider yok.");
            // Mesh alt varlığı (bakeAxisConversion) (bx, by, −bz) uzayındadır: gövde ekseni mesh Z'dir
            // ve Geometry çocuğunun +90° X döndürmesi bunu dünya Y'sine (dikey) taşır. Her iki
            // olgu birlikte sınanır: yerel eksen Z (2) VE dünya yönü yukarı.
            Assert.AreEqual(2, kapsul.direction, "Kapsül ekseni mesh uzayında Z (2) olmalı (bakeAxisConversion: (bx, by, −bz)).");
            // Eksen işaretsizdir: mesh z −3.4..0 (bz negatif) Rx(+90°) ile dünya +Y'ye gider; |dot| ölçülür.
            Vector3 dunyaEksen = kapsul.transform.TransformDirection(Vector3.forward);
            Assert.Greater(Mathf.Abs(Vector3.Dot(dunyaEksen, Vector3.up)), 0.999f, "Kapsül ekseni dünyada dikey olmalı (Geometry +90° X): " + dunyaEksen);
            Assert.Greater(kapsul.radius, 0.05f, "Kapsül yarıçapı sıfıra yakın.");
            Assert.Greater(kapsul.height, 2f * kapsul.radius, "Kapsül yüksekliği çapından büyük olmalı (bir gövde, bir küre değil).");
            Debug.Log("[DD-TEST] wp011.prefab.capsule r=" + kapsul.radius.ToString("0.000", CultureInfo.InvariantCulture) +
                      " h=" + kapsul.height.ToString("0.000", CultureInfo.InvariantCulture) + " c=" + ArtGateGeometry.Yaz(kapsul.center));
        }

        // ============================================================== kütüphane

        [Test]
        public void Kutuphane_18Giris_PrefablaraCozulur_FindCalisir()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ArtFamilyLibrary>(ArtPipelinePaths.FamilyLibraryAsset);
            Assert.IsNotNull(lib, "Kütüphane asset'i yok: " + ArtPipelinePaths.FamilyLibraryAsset);
            Assert.IsNotNull(lib.items, "items null.");
            Assert.AreEqual(18, lib.items.Count, "Kütüphane 18 giriş taşımalı.");

            var hatalar = new List<string>();
            var gorulen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArtFamilyLibrary.Item item in lib.items)
            {
                if (item == null || string.IsNullOrEmpty(item.family))
                {
                    hatalar.Add("boş giriş.");
                    continue;
                }

                if (!gorulen.Add(item.family))
                {
                    hatalar.Add(item.family + ": yineleniyor.");
                }

                if (Array.IndexOf(ArtFamilies.All, item.family) < 0)
                {
                    hatalar.Add(item.family + ": ArtFamilies.All dışında.");
                }

                if (item.prefab == null)
                {
                    hatalar.Add(item.family + ": prefab null.");
                    continue;
                }

                string yol = AssetDatabase.GetAssetPath(item.prefab);
                if (yol != ArtPipelinePaths.FamilyPrefab(item.family))
                {
                    hatalar.Add(item.family + ": prefab " + yol + " (beklenen " + ArtPipelinePaths.FamilyPrefab(item.family) + ").");
                }
            }

            foreach (string aile in ArtFamilies.All)
            {
                GameObject bulunan = lib.Find(aile);
                if (bulunan == null)
                {
                    hatalar.Add(aile + ": Find null döndü.");
                }
                else if (bulunan.name != "SM_" + aile)
                {
                    hatalar.Add(aile + ": Find '" + bulunan.name + "' döndü.");
                }
            }

            Assert.IsNull(lib.Find("yok_boyle_aile"), "Bilinmeyen aile için Find null dönmeli.");
            Assert.IsEmpty(hatalar, "Kütüphane (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }
    }
}
