using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-092 C · AP-030 kit gate (P-05 v001 P1, staged by wp-092/tools/stage-ap030.py, built by ArtPipelineMenu.RunLibraryAp030).
    ///
    /// Lambalar (andon, kerosene_lamp, wall_sconce; hibachi de soket taşır): prefab kökünde SOCKET_Flame + SOCKET_Light, konumları
    /// manifestin Blender-uzayı noktasının Unity karşılığı (WorldPlacementAsset.ToUnity, 1 mm) ve FBX importer'ının kendi Null düğümleriyle
    /// aynı yer (importer boşları düşürmüyor kanıtı); ışıyan alan ayrı glow malzemesinde: _EMISSION açık, _EmissionMap = lamba atlası
    /// Emission (sRGB), _EmissionColor lineer = rol yoğunluğu, GI'ye pişer (BakedEmissive). Hiçbir prefabda Light bileşeni yok (ışıkları
    /// WP-092 lideri lamba başına pişmiş nokta ışığı olarak yerleştirir).
    /// Her AP-030 prefabında kök LODGroup (3 LOD) ve S-05 §6 kuralı: hiçbir collider üstü (0,30; 0,50) m bandında değil — ≤ 0,30
    /// (üstünden adım atılır) ya da ≥ 0,50 (engel), 5 mm tolerans; v002: barikat ve yatai collider üstü ≤ 1,20 m (Y5: mermi ayak
    /// + 1,39 m'den geçer). v002 soketleri (yol SOCKET_In / SOCKET_Out — köşe / kıvrım çıkışları dönük —, yatai SOCKET_Lamp, barikat
    /// SOCKET_End_L/R, yakuimon SOCKET_Wall_L/R) da kökte, manifestin noktası ve yaw'ı ile (1 mm, 0,1°). Sayılar manifestten türetilir.
    /// </summary>
    [TestFixture]
    [Category("WP092")]
    public sealed class Ap030KitTests
    {
        private const string Ap = "AP-030";
        private static readonly string[] Lambalar = { "andon", "kerosene_lamp", "wall_sconce" };
        private static readonly string[] Soketler = { "SOCKET_Flame", "SOCKET_Light" };
        private const float BantAlt = 0.30f, BantUst = 0.50f, BantTolerans = 0.005f, SoketTolerans = 0.001f, SoketAciTolerans = 0.1f;
        /// <summary>v002 · "üstünden ateş edilen" engeller (S-05 Y5): collider üstü en çok 1,20 m.</summary>
        private static readonly string[] AlcakEngeller = { "barricade", "yatai" };
        private const float AlcakEngelUst = 1.20f;
        private static readonly string[] YolAileleri = { "ishidatami", "gravel_path", "dirt_path", "boardwalk", "drain_channel" };

        private static ArtLibraryManifest Manifest()
        {
            string yol = ArtLibraryPaths.ManifestJson(Ap);
            Assert.IsTrue(File.Exists(yol), "Manifest yok: " + yol);
            return ArtLibraryManifest.FromJson(File.ReadAllText(yol));
        }

        private static IEnumerable<(ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab)> Prefablar()
        {
            foreach (ArtLibraryManifest.Family f in Manifest().families)
            foreach (ArtLibraryManifest.Variation v in f.variations)
            {
                string yol = ArtLibraryPaths.Prefab(Ap, f.family, v.prefab);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(yol);
                Assert.IsNotNull(prefab, "prefab yok: " + yol);
                yield return (f, v, prefab);
            }
        }

        private static string F(float x) => x.ToString("0.000", CultureInfo.InvariantCulture);

        [Test]
        public void Ap030_Lambalar_SoketlerKokte_ManifestVeFbxIleAyni_GlowMalzemesiIsiyor()
        {
            var hatalar = new List<string>();
            int lamba = 0, soket = 0, glow = 0;
            var lambaAileleri = new HashSet<string>();
            foreach ((ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab) in Prefablar())
            {
                if (Array.IndexOf(Lambalar, f.family) < 0) continue;
                lamba++;
                lambaAileleri.Add(f.family);
                string on = v.prefab;

                // 1. Sockets: root children, at the manifest point (Blender → Unity), and where the FBX importer put its own Null node.
                string fbxYolu = null;
                foreach (ArtLibraryManifest.Part p in v.parts) { fbxYolu = ArtLibraryPaths.Fbx(Ap, f.family, p.lods[0].fbx); break; }
                var model = fbxYolu != null ? AssetDatabase.LoadAssetAtPath<GameObject>(fbxYolu) : null;
                if (model == null) hatalar.Add(on + ": FBX model varlığı yüklenmedi (" + fbxYolu + ")");
                foreach (string ad in Soketler)
                {
                    Transform t = prefab.transform.Find(ad);
                    ArtLibraryManifest.Socket m = v.sockets.Find(s => s.name == ad);
                    if (m == null) { hatalar.Add(on + ": manifestte " + ad + " yok"); continue; }
                    if (t == null) { hatalar.Add(on + ": prefab kökünde " + ad + " yok"); continue; }
                    soket++;
                    if (Vector3.Distance(t.localPosition, m.PrefabPosition) > SoketTolerans)
                        hatalar.Add(on + " " + ad + ": prefab " + t.localPosition.ToString("F4") + " ≠ manifest " + m.PrefabPosition.ToString("F4"));
                    if (model != null)
                    {
                        Transform fbxSoket = null;
                        foreach (Transform c in model.GetComponentsInChildren<Transform>(true)) if (c.name == ad) { fbxSoket = c; break; }
                        if (fbxSoket == null) hatalar.Add(on + ": FBX importer " + ad + " düğümünü düşürdü");
                        else
                        {
                            Vector3 fbxNokta = model.transform.InverseTransformPoint(fbxSoket.position);
                            if (Vector3.Distance(fbxNokta, m.PrefabPosition) > SoketTolerans)
                                hatalar.Add(on + " " + ad + ": FBX düğümü model uzayında " + fbxNokta.ToString("F4") + " ≠ manifest " + m.PrefabPosition.ToString("F4"));
                        }
                    }
                }

                // 2. The glow slot: one emissive material, the lamp atlas Emission map, linear intensity, baked GI.
                ArtLibraryMaterialRoles.Spec spec = ArtLibraryMaterialRoles.Get(ArtLibraryMaterialRoles.Ap030RolePrefix + f.family + "_glow");
                if (spec == null || !spec.HasEmission) { hatalar.Add(on + ": " + f.family + "_glow rolü emisyonsuz"); continue; }
                Material mat = null;
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                        if (m != null && m.name == "M_" + spec.Role) mat = m;
                if (mat == null) { hatalar.Add(on + ": M_" + spec.Role + " hiçbir renderer'da yok"); continue; }
                glow++;
                if (!mat.IsKeywordEnabled("_EMISSION")) hatalar.Add(on + ": " + mat.name + " _EMISSION kapalı");
                Texture em = mat.GetTexture("_EmissionMap");
                string beklenenHarita = ArtPipelinePaths.SourceTexture(spec.EmissionTextureRole, "Emission");
                if (em == null || AssetDatabase.GetAssetPath(em) != beklenenHarita) hatalar.Add(on + ": _EmissionMap " + (em != null ? AssetDatabase.GetAssetPath(em) : "null") + " ≠ " + beklenenHarita);
                var imp = AssetImporter.GetAtPath(beklenenHarita) as TextureImporter;
                if (imp == null || !imp.sRGBTexture) hatalar.Add(beklenenHarita + ": sRGB değil / importer yok");
                Color c0 = mat.GetColor("_EmissionColor").linear;
                if (Mathf.Abs(c0.r - spec.EmissionIntensity) > 0.02f || Mathf.Abs(c0.g - spec.EmissionIntensity) > 0.02f || Mathf.Abs(c0.b - spec.EmissionIntensity) > 0.02f)
                    hatalar.Add(on + ": _EmissionColor lineer " + c0 + " ≠ beyaz × " + spec.EmissionIntensity);
                if ((mat.globalIlluminationFlags & MaterialGlobalIlluminationFlags.BakedEmissive) == 0) hatalar.Add(on + ": " + mat.name + " GI bayrağı " + mat.globalIlluminationFlags + " (BakedEmissive değil)");
            }

            Assert.AreEqual(Lambalar.Length, lambaAileleri.Count, "lamba aileleri manifestte: " + string.Join(", ", lambaAileleri));
            Assert.AreEqual(7, lamba, "v001: andon ×2, kerosene_lamp ×2, wall_sconce ×2; v002: + kerosene_lamp V03 (1,15×)");
            Assert.IsEmpty(hatalar, "AP-030 lamba ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp092.ap030.lamps prefabs=" + lamba + " sockets=" + soket + " glowMaterials=" + glow);
        }

        [Test]
        public void Ap030_HerPrefab_LodGroup_IsikYok_ColliderUstuYasakBandinDisinda()
        {
            var hatalar = new List<string>();
            var ustler = new List<string>();
            int prefabSayisi = 0, collider = 0;
            foreach ((ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab) in Prefablar())
            {
                prefabSayisi++;
                string on = v.prefab;
                var lodGroup = prefab.GetComponent<LODGroup>();
                if (lodGroup == null) hatalar.Add(on + ": kökte LODGroup yok");
                else if (lodGroup.GetLODs().Length != 3) hatalar.Add(on + ": " + lodGroup.GetLODs().Length + " LOD");
                if (prefab.GetComponentsInChildren<Light>(true).Length > 0) hatalar.Add(on + ": prefabda Light bileşeni var (ışıklar yerleşimde pişirilir)");

                // Collider tops in prefab-root space (Collider.bounds is empty on an asset that is not in a scene: the corners are mapped).
                Matrix4x4 kok = prefab.transform.worldToLocalMatrix;
                foreach (Collider c in prefab.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger) continue;
                    collider++;
                    Matrix4x4 m = kok * c.transform.localToWorldMatrix;
                    float ust = float.MinValue;
                    if (c is BoxCollider b)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            var kose = b.center + Vector3.Scale(b.size * 0.5f, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
                            ust = Mathf.Max(ust, m.MultiplyPoint3x4(kose).y);
                        }
                    }
                    else if (c is CapsuleCollider k)
                    {
                        Vector3 eksen = k.direction == 0 ? Vector3.right : k.direction == 1 ? Vector3.up : Vector3.forward;
                        float yari = Mathf.Max(0f, k.height * 0.5f - k.radius);
                        foreach (float s in new[] { -1f, 1f })
                        {
                            Vector3 uc = m.MultiplyPoint3x4(k.center + eksen * (s * yari));
                            ust = Mathf.Max(ust, uc.y + k.radius * m.lossyScale.y);
                        }
                    }
                    else
                    {
                        hatalar.Add(on + ": " + c.GetType().Name + " primitif değil");
                        continue;
                    }

                    ustler.Add(on + "/" + c.name + " " + F(ust));
                    if (ust > BantAlt + BantTolerans && ust < BantUst - BantTolerans)
                        hatalar.Add(on + "/" + c.name + ": collider üstü " + F(ust) + " m yasak bantta (S-05 §6: ≤ 0,30 ya da ≥ 0,50)");
                    if (Array.IndexOf(AlcakEngeller, f.family) >= 0 && ust > AlcakEngelUst + BantTolerans)
                        hatalar.Add(on + "/" + c.name + ": collider üstü " + F(ust) + " m > " + F(AlcakEngelUst) + " (S-05 Y5: üstünden ateş edilen engel)");
                }
            }

            Assert.AreEqual(Manifest().VariationCount, prefabSayisi);
            Assert.AreEqual(62, prefabSayisi, "AP-030 v001 22 + v002 40 varyasyon");
            Assert.Greater(collider, 0, "Hiç collider yok; tarama yalanlanmıştır.");
            Assert.IsEmpty(hatalar, "AP-030 prefab ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp092.ap030.prefabs count=" + prefabSayisi + " colliders=" + collider + " tops: " + string.Join(", ", ustler));
        }

        [Test]
        public void Ap030_HerSoket_KoktenManifestNoktasiVeYawIle_FbxDugumuIleAyni_YolModulleriSoketli()
        {
            var hatalar = new List<string>();
            int soket = 0, donuk = 0, yol = 0;
            foreach ((ArtLibraryManifest.Family f, ArtLibraryManifest.Variation v, GameObject prefab) in Prefablar())
            {
                string on = v.prefab;
                if (Array.IndexOf(YolAileleri, f.family) >= 0 && v.sockets.Exists(s => s.name == "SOCKET_In")) yol++;
                string fbxYolu = ArtLibraryPaths.Fbx(Ap, f.family, v.parts[0].lods[0].fbx);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxYolu);
                foreach (ArtLibraryManifest.Socket m in v.sockets)
                {
                    Transform t = prefab.transform.Find(m.name);
                    if (t == null) { hatalar.Add(on + ": prefab kökünde " + m.name + " yok"); continue; }
                    soket++;
                    if (Mathf.Abs(m.yawDeg) > 0.01f) donuk++;
                    if (Vector3.Distance(t.localPosition, m.PrefabPosition) > SoketTolerans)
                        hatalar.Add(on + " " + m.name + ": prefab " + t.localPosition.ToString("F4") + " ≠ manifest " + m.PrefabPosition.ToString("F4"));
                    if (Quaternion.Angle(t.localRotation, m.PrefabRotation) > SoketAciTolerans)
                        hatalar.Add(on + " " + m.name + ": prefab dönüşü " + t.localRotation.eulerAngles.ToString("F2") + " ≠ manifest yaw " + F(m.yawDeg) + "°");
                    // The flow direction: Blender (−sin yaw, cos yaw, 0) → Unity (x, z, y) is the socket's +Z.
                    float r = m.yawDeg * Mathf.Deg2Rad;
                    var akis = new Vector3(-Mathf.Sin(r), 0f, Mathf.Cos(r));
                    if (Vector3.Angle(t.localRotation * Vector3.forward, akis) > SoketAciTolerans) hatalar.Add(on + " " + m.name + ": +Z akış yönü değil");
                    Transform fbxSoket = null;
                    if (model != null) foreach (Transform c in model.GetComponentsInChildren<Transform>(true)) if (c.name == m.name) { fbxSoket = c; break; }
                    if (fbxSoket == null) { hatalar.Add(on + ": FBX importer " + m.name + " düğümünü düşürdü (" + fbxYolu + ")"); continue; }
                    Vector3 fbxNokta = model.transform.InverseTransformPoint(fbxSoket.position);
                    if (Vector3.Distance(fbxNokta, m.PrefabPosition) > SoketTolerans)
                        hatalar.Add(on + " " + m.name + ": FBX düğümü model uzayında " + fbxNokta.ToString("F4") + " ≠ manifest " + m.PrefabPosition.ToString("F4"));
                }
            }

            Assert.Greater(donuk, 0, "v002 köşe / kıvrım çıkışları dönük soket taşır; hiç yok: yaw okunmamış.");
            Assert.GreaterOrEqual(yol, 18, "v002 yol modülleri SOCKET_In taşır (18)");
            Assert.IsEmpty(hatalar, "AP-030 soket ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp092.ap030.sockets count=" + soket + " yawed=" + donuk + " roadModules=" + yol);
        }
    }
}
