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
        private const string Ap023RolOnEki = "ap023_";   // WP-074 B5 · AP-023 dünya v5 kiti rolleri
        private const string Ap030RolOnEki = ArtLibraryMaterialRoles.Ap030RolePrefix;   // WP-092 C · AP-030 lamba + iç mekân kiti rolleri

        // ------------------------------------------------------------------ yardımcılar

        private static List<string> DiskteUzanti(string uzanti)
        {
            var sonuc = new List<string>();
            foreach (string yol in Directory.GetFiles(ArtRoot, "*" + uzanti, SearchOption.AllDirectories))
            {
                string p = yol.Replace('\\', '/');
                if (ArtImportContract.IsLutTexture(p)) continue;   // WP-029: LUT strips have their own contract (LutTests)
                if (ArtImportContract.IsMenuPlateTexture(p)) continue;   // WP-068: menu plates have their own contract (MenuPlateTests)
                if (ArtImportContract.IsLightCookieTexture(p)) continue;   // WP-075 C2: the cloud cookie has its own contract (ApplyLightCookie, CloudShadowTests)
                if (ArtImportContract.IsDataMaskTexture(p)) continue;   // WP-092 B5: ground / vegetation data masks have their own contract (WorldV5TerrainTests)
                sonuc.Add(p);
            }

            sonuc.Sort(string.CompareOrdinal);
            return sonuc;
        }

        /// <summary>WP-019: PNG paths the AP-010 manifest ships under the VFX root (row files + emissive layers; the GLB is not imported).</summary>
        public static List<string> ArtVfxManifestPngs()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var paths = new List<string> { ArtVfxContract.ManifestPath };
            paths.AddRange(ArtVfxImporter.SupplementManifests());   // WP-047E: AP010C (AP-014C) supplement
            foreach (string path in paths)
            foreach (object o in ArtJson.AsList(ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(path)), "manifest")["assets"], "assets"))
            {
                var a = ArtJson.AsObject(o, "asset");
                string file = ArtJson.AsString(a["file"], "file");
                // v003 paths are relative to Assets/_Art/VFX/ (AP010/…, AP010B/…); v002 paths to the AP010 root.
                string parent = ArtVfxContract.VfxRoot.Substring(0, ArtVfxContract.VfxRoot.LastIndexOf('/'));
                if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) set.Add((file.StartsWith("AP010") ? parent : ArtVfxContract.VfxRoot) + "/" + file);
                if (a.TryGetValue("emissive_file", out object em) && em != null)
                {
                    string ef = ArtJson.AsString(em, "emissive_file");
                    set.Add((ef.StartsWith("AP010") ? parent : ArtVfxContract.VfxRoot) + "/" + ef);
                }
            }

            var list = new List<string>(set);
            list.Sort(string.CompareOrdinal);
            return list;
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

                // WP-016: skinned characters validate against the v1.1 extension (ADR-016-1).
                List<string> v = ArtCharacterContract.IsCharacterModel(yol) ? ArtCharacterContract.Validate(importer) : ArtImportContract.ValidateModel(importer);
                if (v.Count > 0)
                {
                    ihlaller.Add(yol + ": " + string.Join("; ", v));
                }
            }

            Assert.IsEmpty(ihlaller, "Import Contract v1/v1.1 ihlali (" + ihlaller.Count + "/" + fbxler.Count + "):\n" + string.Join("\n", ihlaller));
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

                // WP-018: the AP-009 Animations/ files are armature + animation only (no mesh by contract).
                bool animationOnly = ArtAnimationClipContract.IsAnimationClip(yol);
                if (mesh == 0 && !animationOnly)
                {
                    bulgular.Add(yol + ": hiç Mesh alt varlığı yok.");
                }
                else if (mesh > 0 && animationOnly)
                {
                    bulgular.Add(yol + ": animasyon dosyasına Mesh sızmış (" + mesh + ").");
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

            // WP-012: every FBX the three library manifests reference (LOD chains + proxies).
            int kutuphane = 0;
            foreach (string ap in ArtLibraryPaths.Packages)
            {
                ArtLibraryManifest m = ArtLibraryManifest.FromJson(File.ReadAllText(ArtLibraryPaths.ManifestJson(ap)));
                foreach (ArtLibraryManifest.Family f in m.families)
                foreach (ArtLibraryManifest.Variation v in f.variations)
                {
                    foreach (ArtLibraryManifest.Part part in v.parts)
                    foreach (ArtLibraryManifest.Lod lod in part.lods)
                    {
                        if (beklenen.Add(ArtLibraryPaths.Fbx(ap, f.family, lod.fbx))) kutuphane++;
                    }

                    foreach (ArtLibraryManifest.Collider c in v.colliders)
                    {
                        if (beklenen.Add(ArtLibraryPaths.Fbx(ap, f.family, c.fbx))) kutuphane++;
                    }
                }
            }

            // WP-092 AP-038 (9ab684e2, the V02 kairo door): a bounded library (Ap038BoundedImporter), not one of ArtLibraryPaths.Packages —
            // its manifest's FBX join the family table here (WP-096 R3, CI e8fc3f70: hidden behind the clip-count assert until 254 held)
            int ap038 = 0;
            string ap038Json = ArtLibraryPaths.ManifestJson("AP-038");
            if (File.Exists(ap038Json))
            {
                ArtLibraryManifest m38 = ArtLibraryManifest.FromJson(File.ReadAllText(ap038Json));
                foreach (ArtLibraryManifest.Family f in m38.families)
                foreach (ArtLibraryManifest.Variation v in f.variations)
                {
                    foreach (ArtLibraryManifest.Part part in v.parts)
                    foreach (ArtLibraryManifest.Lod lod in part.lods)
                        if (beklenen.Add(ArtLibraryPaths.Fbx("AP-038", f.family, lod.fbx))) ap038++;
                    foreach (ArtLibraryManifest.Collider c in v.colliders)
                        if (beklenen.Add(ArtLibraryPaths.Fbx("AP-038", f.family, c.fbx))) ap038++;
                }

                Assert.AreEqual(1, ap038, "AP-038: tek kapı FBX'i (AP023_kairo_door_V02; LOD'lar ve çarpıştırıcılar aynı dosyada)");
            }

            // WP-038 (ADR-038-1): the AP-015P surface plates and water meshes listed by the surfaces manifest.
            int yuzey = 0;
            foreach (string f in ArtSurfaceImporter.ManifestFbxPaths()) if (beklenen.Add(f)) yuzey++;
            Assert.AreEqual(74, yuzey, "AP-015P 74 yüzey FBX (9 zemin + 49 rota + 13 kaldırım + 3 su)");

            // WP-016 (ADR-016-1): the character files — player base + 2 clips, zombie 3 bodies + 19 modules
            // (AP007-modules.json) + 2 clips; the library test checks their contents.
            for (int lod = 0; lod < ArtCharacterContract.HeroV2LodCount; lod++) beklenen.Add(ArtCharacterContract.HeroV2LodFile(lod));   // WP-082 R3: hero v2 (AP-024D D6, exports/v004 Hero_v2_TD_LOD0..3)
            beklenen.Add(ArtCharacterContract.LegacyPlayerModel);        // WP-082 R3: the pre-hero body, kept for the FP viewmodel
            beklenen.Add(ArtCharacterPrefabBuilder.PlayerFpArmsModel);   // WP-071 · AP-022 FP arms v2 (P-06)
            // WP-018: the 30 AP-009 clip files listed by the manifest.
            int klip = 0;
            var manifest = ArtAnimationClipContract.Manifest();
            if (manifest != null) foreach (var spec in manifest.Values) if (beklenen.Add(ArtAnimationClipContract.FbxPath(spec))) klip++;
            Assert.AreEqual(254, klip, "254 = 244 + AP-033 p3 zombie transitions 10 (WP-096 185bbe6b: start_walk, start_run, stop_walk_l/r, stop_run_l/r, turn45_run_L, turn90_L, turn180_L, stumble_recover); 244 = 236 + AP-033 p2 zombie locomotion 8 (WP-096: lurch_B, drag_A/B, run_feral_B, run_stumble_A, walk_heavy_A/B, walk_stomp_B); 236 = 232 + AP-033 p1 zombie locomotion 4 (WP-096: lurch_A, limp_A, run_feral_A, walk_stomp_A); 232 = 222 + AP-022 player locomotion 10 (MUST-3/4); 222 = 217 + AP-022 zombie locomotion 5 (MUST-I1); 217 = 205 + FP Special 12; 205 = 203 + lmg_2 tray reloads 2; 203 = 194 + smg_2 FP 9 (WP-071 r2); 194 = 182 + TP Special family 12; 182 = 149 + WP-071 B4c TP 33 (sniper 11, launcher 8, LMG 11, smg_2 3); AP-009D v004 85 klip FBX (WP-032C) + player_sw_hold_jewel (AP-009G v007, WP-032G) + AP-022 v008 zombie deaths ×4 / attacks ×6 (WP-071 d9de74ba) + slice 2a electro loop / arc_electro / gel_slip + slices 4a/4b AR + SMG TP 21 + slice 5/5b FP arms 29 (WP-071) = 149");
            // WP-082 R3 · AP-026 v002 locomotion (AP026-loco-manifest.json): idle_03 + 24 loops + 48 starts + 48 stops = 121.
            int loco = 0;
            var locoManifest = ArtAnimationClipContract.LocoManifest();
            if (locoManifest != null) foreach (var spec in locoManifest.Values) if (beklenen.Add(ArtAnimationClipContract.FbxPath(spec))) loco++;
            Assert.AreEqual(121, loco, "AP-026 R3 yürüyüş: idle_03 + 24 döngü + 48 start + 48 stop");
            beklenen.Add(ArtCharacterPrefabBuilder.PlayerIdleClipFile);
            beklenen.Add(ArtCharacterPrefabBuilder.PlayerWalkClipFile);
            foreach (string b in ArtCharacterPrefabBuilder.ZombieBodyModels) beklenen.Add(b);
            beklenen.Add(ArtCharacterPrefabBuilder.ZombieIdleClipFile);
            beklenen.Add(ArtCharacterPrefabBuilder.ZombieWalkClipFile);
            int moduller = 0;
            foreach (object o in ArtJson.AsList(ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ArtCharacterPrefabBuilder.ModulesJson)), "modules")["modules"], "modules"))
            {
                string fbx = ArtJson.AsString(ArtJson.AsObject(o, "module")["fbx"]);
                if (beklenen.Add(ArtCharacterContract.ZombieFolder + "/Modules/" + Path.GetFileName(fbx))) moduller++;
            }

            Assert.AreEqual(24, moduller, "AP-007 v003 24 modül FBX (19 + H6/H7 baş, A1/A2 aksesuar, T8 üst — WP-038C)");

            // WP-017 (ADR-017-1): the 18 AP-008 armour files listed by the manifest (6 variants × 3 states).
            int zirh = 0;
            foreach (object o in ArtJson.AsList(ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ArtArmorBuilder.ManifestJson)), "manifest")["families"], "families"))
            {
                string fbx = ArtJson.AsString(ArtJson.AsObject(o, "piece")["fbx"]);
                if (beklenen.Add(ArtArmorBuilder.ArmorFolder + "/" + Path.GetFileName(fbx))) zirh++;
            }

            Assert.AreEqual(ArtArmorBuilder.ExpectedFbxCount, zirh, "AP-008 18 zırh FBX");

            // WP-036 (ADR-036-1): the AP-014 tier silhouette parts listed by the tier manifest (inventory; S-03 builder).
            int tierParca = 0;
            foreach (ArtTierBuilder.ManifestPart part in ArtTierBuilder.ReadParts(out _, out _))
            {
                if (beklenen.Add(ArtTierBuilder.TierFolder + "/" + Path.GetFileName(part.fbx))) tierParca++;
            }

            Assert.AreEqual(4, tierParca, "AP-014 4 tier parça FBX");

            // WP-084 · AP-025 intake: every FBX the intake manifest copied besides the three bodies (12 faces, 24 H2 fit-C garments,
            // 4 H2 fit-C tier parts; v003 adds the 24 fit A/B garments, Modules/AB) — the manifest is the table (tools/art/ap025_intake.py
            // writes it with each file's SHA-256).
            int ap025 = 0;
            if (ArtAp025Intake.Active)
            {
                foreach (object o in ArtJson.AsList(ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ArtAp025Intake.ManifestPath)), "AP025 intake manifest")["copied"], "copied"))
                {
                    string to = ArtJson.AsString(ArtJson.AsObject(o, "copied")["to"]);
                    if (to.EndsWith(".fbx", StringComparison.Ordinal) && beklenen.Add(ArtCharacterContract.ZombieFolder + "/" + to)) ap025++;
                }

                Assert.AreEqual(64, ap025, "AP-025D v003: 12 yüz + 24 H2 giysi (fit C) + 24 A/B giysi + 4 H2 tier parçası");
            }

            // WP-037B: the AP-014 Special Weapon pickup + hand models (S-02 builder).
            int sw = 0;
            foreach (string f in ArtSpecialWeaponBuilder.ModelFiles()) if (beklenen.Add(f)) sw++;
            Assert.AreEqual(6, sw, "AP-014 3 Special Weapon × pickup/hand FBX (AP-014D Tide Jewel, WP-059C)");

            // WP-019 (ADR-019-1): the AP-010 gold pack mesh (the only FBX the effect package ships).
            beklenen.Add(ArtVfxContract.VfxRoot + "/gold/AP010_goldpack_V01.fbx");

            // WP-042B: the AP-013 classic weapon base models A/B/C (S-02 builder).
            int classic = 0;
            foreach (string f in ArtWeaponBuilder.ModelFiles()) if (beklenen.Add(f)) classic++;
            Assert.AreEqual(3, classic, "AP-013 3 klasik silah FBX");
            // WP-047E: the AP-013C casings (S-07 builder, under VFX).
            beklenen.Add(ArtCasingBuilder.ModelPath);
            // WP-093: the AP-031 v001 vegetation kit (P-05) — every FBX its manifest lists (ArtVegetationIntake).
            int bitki = 0;
            foreach (string f in ArtVegetationIntake.ModelFiles()) if (beklenen.Add(f)) bitki++;
            if (ArtVegetationIntake.Active) Assert.AreEqual(ArtVegetationIntake.ExpectedFbx, bitki, "AP-031 v001: 19 bitki FBX (çim 7 + flora 12)");

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
            Assert.AreEqual(465, kutuphane, "Kütüphane FBX sayısı 48 (AP-002) + 42 (AP-003) + 53 (AP-003B) + 12 (AP-005, WP-014) + 6 (AP-005B, WP-017) + 38 (AP-017, WP-038B) + 6 (AP-017 Faz 2, WP-038I) + 6 (Faz 2b, WP-038L) = 211 olmalı. + 192 (AP-023 dünya v5 kiti v007: v005 166 + 26) = 403; + 22 (AP-030 v001 P1 lamba + iç mekân, WP-092 C) = 425; + 40 (AP-030 v002 yol + küme + clutter_item + kerosene V03; 6 v001 deltası aynı dosya) = 465");
            Debug.Log("[DD-TEST] wp011.gate.fbxset count=" + diskte.Count + " (18x3 LOD + " + (beklenen.Count - 55 - kutuphane) + " collider + zemin + " + kutuphane + " kütüphane)");
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
                if (ArtVfxContract.IsVfxTexture(yol)) continue;   // WP-019: AP-010 sheets follow ArtVfxContract (ArtVfxCatalogTests)
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
        public void PngKumesi_14KaynakArti4TuretilmisMaskeArti1GokyuzuArti63Kutuphane()
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

            // WP-012 kütüphane rolleri (ADR-012-2): her doku rolü BaseColor; Packed taşıyanlar Normal + Packed +
            // türetilmiş maske. 18 doku rolü → 18 + 15×3 = 63 kaynak/türetilmiş PNG; küme 19 + 63 = 82.
            foreach (string rol in ArtLibraryMaterialRoles.TextureRoles())
            {
                if (Array.IndexOf(ArtFamilies.MaterialRoles, rol) >= 0) continue; // stone (weathered) WP-011 dokularını paylaşır
                if (rol.StartsWith(Ap023RolOnEki, StringComparison.Ordinal)) continue;   // WP-074 B5: aşağıda kendi bloğu
                if (rol.StartsWith(Ap030RolOnEki, StringComparison.Ordinal)) continue;   // WP-092 C: aşağıda kendi bloğu
                beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "BaseColor"));
            }

            foreach (string rol in ArtLibraryMaterialRoles.PackedTextureRoles())
            {
                if (Array.IndexOf(ArtFamilies.MaterialRoles, rol) >= 0) continue;
                if (rol.StartsWith(Ap023RolOnEki, StringComparison.Ordinal)) continue;
                if (rol.StartsWith(Ap030RolOnEki, StringComparison.Ordinal)) continue;
                beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Normal"));
                beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Packed"));
                beklenen.Add(ArtPipelinePaths.DerivedMask(rol));
            }

            // WP-017: + 6 AP-005B prop roles (BaseColor + Normal + Packed + derived mask = 24) + the generated roof-course normal → 82 + 25 = 107.
            beklenen.Add(ArtPipelinePaths.DerivedTexture(ArtLibraryMaterialRoles.RoofCoursesTextureRole, "Normal"));
            // WP-038I: + 4 AP-017 Phase 2 prop roles (same four files each) → 107 + 16 = 123.
            // WP-038L: + 5 AP-017 Phase 2b prop roles (four files each) + 2 BaseColor-only roles (fish trap LOD albedo, koi cards) → 123 + 22 = 145.
            Assert.AreEqual(145, beklenen.Count, "Beklenen doku kümesi 19 + 63 (WP-012) + 24 (WP-017 prop) + 1 (roof_courses) + 16 (WP-038I AP-017 Faz 2 prop) + 22 (WP-038L Faz 2b) = 145 olmalı.");

            // WP-016 karakter setleri (ADR-016-1): oyuncu 3 skin albedo + Normal + Packed(ORM) + türetilmiş
            // MetallicSmoothness(+AO); zombi atlas BaseColor + Normal + Packed + Mask + türetilmiş MS + DetailMask.
            string pt = ArtCharacterPrefabBuilder.PlayerTextures, pd = ArtCharacterPrefabBuilder.PlayerDerived;
            string zt = ArtCharacterPrefabBuilder.ZombieTextures, zd = ArtCharacterPrefabBuilder.ZombieDerived;
            foreach (string f in new[]
            {
                pt + "/T_player_skin1_BaseColor.png", pt + "/T_player_skin2b_BaseColor.png", pt + "/T_player_skin3_BaseColor.png",
                pt + "/T_player_Normal.png", pt + "/T_player_Packed.png", pd + "/T_player_MetallicSmoothness.png",
                zt + "/T_zombie_atlas_BaseColor.png", zt + "/T_zombie_atlas_Normal.png", zt + "/T_zombie_atlas_Packed.png", zt + "/T_zombie_atlas_Mask.png",
                zd + "/T_zombie_atlas_MetallicSmoothness.png", zd + "/T_zombie_atlas_DetailMask.png",
                // WP-017 AP-008 armour atlas: BaseColor + Normal + Packed + Mask + derived MS + lacquer DetailMask.
                ArtArmorBuilder.BaseColorMap, ArtArmorBuilder.NormalMap, ArtArmorBuilder.PackedMap, ArtArmorBuilder.MaskMap,
                ArtArmorBuilder.MetallicSmoothnessMap, ArtArmorBuilder.DetailMaskMap,
                // WP-020 AP-007 v002 cloth atlas: BaseColor + Normal + Packed + derived MS (the constant Mask is a 4×4 sub-texture, not a map).
                zt + "/T_zombie_cloth_BaseColor.png", zt + "/T_zombie_cloth_Normal.png", zt + "/T_zombie_cloth_Packed.png", zd + "/T_zombie_cloth_MetallicSmoothness.png"
            }) beklenen.Add(f);

            Assert.AreEqual(167, beklenen.Count, "Beklenen doku kümesi 145 + 12 (WP-016 karakterler) + 6 (WP-017 zırh) + 4 (WP-020 kumaş atlası) = 129 olmalı.");

            // WP-036 tier parts atlas (BaseColor + Normal + Packed + Mask + derived MS = 5) and WP-037B Special Weapon
            // atlas (4 + derived MS) + 4 VFX use sheets (Sheet role) = 9 → 125 + 5 + 9 = 139.
            foreach (string f in new[] { ArtTierBuilder.BaseColorMap, ArtTierBuilder.NormalMap, ArtTierBuilder.PackedMap, ArtTierBuilder.MaskMap, ArtTierBuilder.MetallicSmoothnessMap }) beklenen.Add(f);
            foreach (string f in ArtSpecialWeaponBuilder.TextureFiles()) beklenen.Add(f);
            Assert.AreEqual(177, beklenen.Count, "Beklenen doku kümesi 167 (WP-020 kumaş atlası dâhil) + 5 (WP-036 tier) + 5 (WP-037B SW atlas; AP-014 sheet'leri WP-073 B6 ile çıktı).");
            // WP-042B AP-013 weapons atlas (BaseColor + Normal + Packed(MRA) + derived MS = 4) → 147.
            foreach (string f in ArtWeaponBuilder.TextureFiles()) beklenen.Add(f);
            Assert.AreEqual(181, beklenen.Count, "Beklenen doku kümesi 177 + 4 (WP-042B silah atlası).");

            // WP-038: the seven AP-015P water v2 inputs (2 normals, 4 data masks, 1 ring sheet) → 143 + 7 = 150.
            foreach (string f in ArtSurfaceImporter.WaterTextures) beklenen.Add(f);
            // (WP-047E: the WP-038 line said 150 but the set already held the 4 weapon maps — 147 + 7 = 154.)
            Assert.AreEqual(188, beklenen.Count, "Beklenen doku kümesi 181 + 7 (WP-038 su v2).");
            // WP-038E: the AP-018 wet inputs — wet stone albedo, three wet packed maps + their derived MetallicSmoothness, damp / puddle /
            // shore masks, the puddle decal sheet + normal → 154 + 12 = 166.
            beklenen.Add(ArtPipelinePaths.SourceTexture("wet_stone", "BaseColor"));
            foreach (string role in ArtSurfaceImporter.WetPackedRoles) { beklenen.Add(ArtPipelinePaths.SourceTexture(role, "Packed")); beklenen.Add(ArtPipelinePaths.DerivedMask(role)); }
            foreach (string role in new[] { "wet_damp", "wet_puddle", "wet_shore" }) beklenen.Add(ArtPipelinePaths.SourceTexture(role, "Mask"));
            beklenen.Add(ArtPipelinePaths.SourceTexture("decal_puddle", "Sheet"));
            beklenen.Add(ArtPipelinePaths.SourceTexture("decal_puddle", "Normal"));
            Assert.AreEqual(200, beklenen.Count, "Beklenen doku kümesi 188 + 12 (WP-038E AP-018 ıslak yüzeyler).");
            // WP-038E (2): the AP-019 sky / horizon plates — three horizon sheets + the sky band source → 166 + 4 = 170.
            foreach (string layer in new[] { "near", "mid", "far" }) beklenen.Add(ArtPipelinePaths.SourceTexture("horizon_" + layer, "Sheet"));
            beklenen.Add(ArtSkyBuilder.SkyBandSourcePath);
            Assert.AreEqual(204, beklenen.Count, "Beklenen doku kümesi 200 + 4 (WP-038E AP-019 gök/ufuk).");

            // WP-047E · AP-013C casings: BaseColor + Normal + MRA source + derived MetallicSmoothness = 4 → 170.
            foreach (string f in ArtCasingBuilder.TextureFiles()) beklenen.Add(f);
            Assert.AreEqual(208, beklenen.Count, "Beklenen doku kümesi 204 + 4 (WP-047E kovan seti).");

            // WP-074 B5 · AP-023 dünya v5 kiti: 30 doku rolü BaseColor; 21 karo rolü Normal + Packed + türetilmiş maske; 9 kart atlası
            // yalnız Normal (ORM yok, NoPackedMap) → 30 + 63 + 9 = 102 (B5b-2 v005: + gravel karo, + hishigaki kartı).
            // v007 (WP-074): + trim karo (4K atlas: BaseColor + Normal + Packed + maske) + lotus kartı (BaseColor + Normal) → 32 + 66 + 10 = 108.
            int ap023 = 0;
            foreach (string rol in ArtLibraryMaterialRoles.TextureRoles())
                if (rol.StartsWith(Ap023RolOnEki, StringComparison.Ordinal) && beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "BaseColor"))) ap023++;
            foreach (string rol in ArtLibraryMaterialRoles.PackedTextureRoles())
            {
                if (!rol.StartsWith(Ap023RolOnEki, StringComparison.Ordinal)) continue;
                if (beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Normal"))) ap023++;
                if (beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Packed"))) ap023++;
                if (beklenen.Add(ArtPipelinePaths.DerivedMask(rol))) ap023++;
            }

            foreach (string rol in ArtLibraryMaterialRoles.NormalOnlyTextureRoles())
                if (beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Normal"))) ap023++;
            // v016 (WP-074 B9, P-05 30.09): + bark_maple karo (momiji gövdesi: BaseColor + Normal + Packed + maske) → 33 + 69 + 10 = 112.
            Assert.AreEqual(112, ap023, "AP-023 v016: 33 BaseColor + 23 × (Normal + Packed + türetilmiş maske) + 10 atlas Normal = 112");

            // WP-092 C · AP-030 (P-05 v001 P1): iç mekân + lamba atlası BaseColor + Normal + Packed (ORM) + türetilmiş maske; lamba Emission.
            int ap030 = 0;
            foreach (string rol in ArtLibraryMaterialRoles.TextureRoles())
                if (rol.StartsWith(Ap030RolOnEki, StringComparison.Ordinal) && beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "BaseColor"))) ap030++;
            foreach (string rol in ArtLibraryMaterialRoles.PackedTextureRoles())
            {
                if (!rol.StartsWith(Ap030RolOnEki, StringComparison.Ordinal)) continue;
                if (beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Normal"))) ap030++;
                if (beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Packed"))) ap030++;
                if (beklenen.Add(ArtPipelinePaths.DerivedMask(rol))) ap030++;
            }

            foreach (string rol in ArtLibraryMaterialRoles.EmissionTextureRoles())
                if (beklenen.Add(ArtPipelinePaths.SourceTexture(rol, "Emission"))) ap030++;
            Assert.AreEqual(37, ap030, "AP-030 v001: 2 atlas × (BaseColor + Normal + Packed + türetilmiş maske) + lamba Emission = 9; v002: + 7 atlas (ishidatami, gravel, dirt, boardwalk, drain, puddle, props) × 4 = 37");

            // WP-074 B5b-2 · the AP-023 4K ground sets (plaza slate ×3, path granite ×2): BaseColor + Normal + packed Mask = 15.
            int zemin = 0;
            foreach (string f in WorldV5Ground.TextureFiles()) if (beklenen.Add(f)) zemin++;
            Assert.AreEqual(15, zemin, "AP-023 zemin setleri: 5 × (BaseColor + Normal + Mask)");

            // WP-075 C3 · AP-023 sky v2 (v6 layout): band v2 + its derived lat-long, 3 horizon v2 sheets, 2 cloud sheets, 3 landmark + 1 NW cluster card = 11.
            int gok = 0;
            foreach (string f in WorldV5Sky.TextureFiles()) if (beklenen.Add(f)) gok++;
            Assert.AreEqual(12, gok, "AP-023 gök v2 dokuları + DEC-102 r2 gök v3 panoraması (sky_dec102)");

            // WP-071 · AP-022 FP arms v2 (P-06, C-00 25.09): 3 skin albedos + Normal + Packed (ORM) + derived MetallicSmoothness = 6.
            int fpArms = 0;
            var fpArmsFiles = new List<string>(ArtCharacterPrefabBuilder.PlayerFpArmsAlbedos)
            {
                ArtCharacterPrefabBuilder.PlayerFpArmsNormal, ArtCharacterPrefabBuilder.PlayerFpArmsPacked, ArtCharacterPrefabBuilder.PlayerFpArmsMetallicSmoothness
            };
            foreach (string f in fpArmsFiles) if (beklenen.Add(f)) fpArms++;
            Assert.AreEqual(6, fpArms, "AP-022 FP kolları v2: 3 skin albedo + Normal + Packed + türetilmiş MS");

            // WP-082 R3 · AP-024 hero v2 (v003 Textures + D5 skins, SHA in HeroV2-provenance.json): BaseColor + Normal + ORM + derived MetallicSmoothness + skin 2 / 3 BaseColor = 6.
            int hero = 0;
            foreach (string f in ArtCharacterPrefabBuilder.HeroV2TextureFiles()) if (beklenen.Add(f)) hero++;
            Assert.AreEqual(6, hero, "kahraman v2: BaseColor + Normal + ORM + türetilmiş MS + D5 deri 2/3");

            // WP-019 (ADR-019-1): every PNG the AP-010 manifest ships (rows + their emissive layers).
            int vfx = 0;
            foreach (string f in ArtVfxManifestPngs()) if (beklenen.Add(f)) vfx++;
            Assert.AreEqual(110, vfx, "AP-010 v004 manifest 109 PNG (v003 109 + fb_armor_break sheet + emissive, WP-047H − ofuda_seal / _burn, WP-073 B6) + AP010C 1 (barrel_heat_distort_tile) = 110.");

            // WP-101 (R4, AP-041): the premultiplied sheets of the AP-041 v001 intake (15 TD muzzle + smoke + 5 impacts + 12 holes).
            int ap041 = 0;
            foreach (string f in ArtAp041VfxIntake.TextureFiles()) if (beklenen.Add(f)) ap041++;
            if (ArtAp041VfxIntake.Active) Assert.AreEqual(ArtAp041VfxIntake.ExpectedTextures, ap041, "AP-041 v001: 15 TD namlu + duman + 5 isabet + 12 delik = 33");

            // WP-104 B4 (S-07): the run tree's AP-041 v001 + v002 sheets (B1–B17, T3–T5, X8 ensō mask).
            int tree = 0;
            foreach (string f in ArtAp041TreeIntake.TextureFiles()) if (beklenen.Add(f)) tree++;
            if (ArtAp041TreeIntake.Active) Assert.AreEqual(ArtAp041TreeIntake.Table.Length, tree, "AP-041 ağaç: 45 sayfa");

            // WP-084 · AP-025 O1 atlas (intake manifest present, ORM staged): the body Packed / Mask and their derived MetallicSmoothness /
            // DetailMask are retired (tools/art/ap025-intake.json "retire"); one ORM + the 9-slice mask array sheet take their place.
            if (ArtAp025Intake.HasO1Atlas)
            {
                foreach (string f in new[] { zt + "/T_zombie_atlas_Packed.png", zt + "/T_zombie_atlas_Mask.png", zd + "/T_zombie_atlas_MetallicSmoothness.png", zd + "/T_zombie_atlas_DetailMask.png" })
                    Assert.IsTrue(beklenen.Remove(f), "emekli doku tabloda yoktu: " + f);
                beklenen.Add(zt + "/T_zombie_atlas_ORM.png");
                beklenen.Add(zt + "/T_zombie_masks_Array.png");
            }

            // WP-093: the AP-031 v001 grass / flora atlases (3 × 2) and ground-detail sets (4 × 3) from its manifest.
            int bitkiDoku = 0;
            foreach (string f in ArtVegetationIntake.TextureFiles()) if (beklenen.Add(f)) bitkiDoku++;
            if (ArtVegetationIntake.Active) Assert.AreEqual(ArtVegetationIntake.ExpectedTextures, bitkiDoku, "AP-031 v001: 6 atlas + 12 zemin ayrıntı dokusu");

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

        // ============================================================== WP-015 · doku politikası (ADR-015-1)

        /// <summary>
        /// Every contract PNG has a class in the ADR-015-1 table, imports at its class size with
        /// streaming per class, and the whole SHIPPED set (every map a material binds: BaseColor,
        /// Normal, MetallicSmoothness, sky — the AP Packed maps are provenance and are never
        /// referenced) estimates to at most the resident budget even fully resident, i.e. before
        /// streaming reduces it. Sizes come from the PNG header, not from the importer, so a
        /// wrong importer cannot make the sum look right.
        /// </summary>
        [Test]
        public void DokuPolitikasi_HerDokuSinifli_BoyutVeStreamingSinifa_SevkToplamiButceAltinda()
        {
            List<string> pngler = DiskteUzanti(".png");
            Assert.Greater(pngler.Count, 0);
            var ihlaller = new List<string>();
            var sinifToplam = new SortedDictionary<string, long>();
            var sinifSayi = new SortedDictionary<string, int>();
            long sevk = 0, tam2048 = 0, sevkDunya = 0, sevkKarakter = 0, sevkSilah = 0;
            int sevkSayi = 0;
            foreach (string yol in pngler)
            {
                if (ArtVfxContract.IsVfxTexture(yol)) continue;   // WP-019: VFX class has its own line (ArtVfxCatalogTests)
                ArtImportContract.TextureRole rol = ArtImportContract.RoleOf(yol);
                string slug = ArtImportContract.RoleSlugOf(yol);
                ArtImportContract.TextureClass? sinif = ArtImportContract.ClassifiedClassOf(yol);   // WP-037: a Sheet is the Vfx class without a table row
                if (sinif == null)
                {
                    ihlaller.Add(yol + ": rol '" + slug + "' ADR-015-1 sınıf tablosunda yok.");
                    continue;
                }

                var importer = AssetImporter.GetAtPath(yol) as TextureImporter;
                if (importer == null)
                {
                    ihlaller.Add(yol + ": TextureImporter yok.");
                    continue;
                }

                int beklenenBoyut = ArtImportContract.MaxTextureSizeOf(yol);   // WP-038: derived masks capped at 1024; WP-038E: wet packed / derived 512
                if (importer.maxTextureSize != beklenenBoyut) ihlaller.Add(yol + ": maxTextureSize " + importer.maxTextureSize + " != " + beklenenBoyut + " (" + sinif + ")");
                TextureImporterPlatformSettings st = importer.GetPlatformTextureSettings(ArtImportContract.StandalonePlatform);
                if (!st.overridden || st.maxTextureSize != beklenenBoyut) ihlaller.Add(yol + ": Standalone maxTextureSize " + st.maxTextureSize + " != " + beklenenBoyut);
                if (importer.streamingMipmaps != ArtImportContract.StreamsMipmaps(sinif.Value)) ihlaller.Add(yol + ": streamingMipmaps " + importer.streamingMipmaps + " != " + ArtImportContract.StreamsMipmaps(sinif.Value) + " (" + sinif + ")");

                PngBoyut(yol, out int w, out int h);
                if (!ArtImportContract.IsShippedMap(yol)) continue;   // provenance only: the AP Packed maps and the masks no material binds (WP-016: the AP-007 ID mask feeds the derived DetailMask); WP-091: one rule with Z3, the _TierMask masks ship
                long bytes = ArtImportContract.EstimatedShippedBytes(yol, w, h, importer.mipmapEnabled);   // class / role cap + the WP-038E wet 512 cap
                sevk += bytes;
                if (ArtImportContract.IsCharacterClass(sinif.Value)) sevkKarakter += bytes;
                else if (ArtImportContract.IsWeaponClass(sinif.Value)) sevkSilah += bytes;
                else sevkDunya += bytes;
                sevkSayi++;
                tam2048 += ArtImportContract.EstimatedShippedBytes(w, h, ArtImportContract.TextureClass.Ground, importer.mipmapEnabled);
                string k = sinif.Value.ToString();
                sinifToplam[k] = (sinifToplam.TryGetValue(k, out long t) ? t : 0) + bytes;
                sinifSayi[k] = (sinifSayi.TryGetValue(k, out int n) ? n : 0) + 1;
            }

            Assert.IsEmpty(ihlaller, "Doku politikası ihlali (" + ihlaller.Count + "):\n" + string.Join("\n", ihlaller));
            var satirlar = new List<string>();
            foreach (KeyValuePair<string, long> kv in sinifToplam) satirlar.Add(kv.Key + " " + sinifSayi[kv.Key] + " doku " + (kv.Value / 1048576.0).ToString("F1") + " MB");
            double sevkMb = sevk / 1048576.0, dunyaMb = sevkDunya / 1048576.0, karakterMb = sevkKarakter / 1048576.0, silahMb = sevkSilah / 1048576.0;
            Debug.Log("[DD-TEST] wp015.texturePolicy shipped=" + sevkSayi + " estMB=" + sevkMb.ToString("F1") + " (all-2048 would be " + (tam2048 / 1048576.0).ToString("F1") + " MB) world=" + dunyaMb.ToString("F1") + "/" + ArtImportContract.ShippedWorldBudgetMb + " character=" + karakterMb.ToString("F1") + "/" + ArtImportContract.ShippedCharacterBudgetMb + " weapon=" + silahMb.ToString("F1") + "/" + ArtImportContract.ShippedWeaponBudgetMb + " total/" + ArtImportContract.ShippedTotalBudgetMb + " | " + string.Join(" | ", satirlar));
            // WP-017 · ADR-015-1 v2: world ≤ 120, character ≤ 80, total ≤ 200 MB (fully resident, before streaming).
            Assert.LessOrEqual(dunyaMb, ArtImportContract.ShippedWorldBudgetMb, "Dünya doku kümesi (Ground/Structure/Foliage/Prop/Sky) sevk bütçesini aşıyor (ADR-015-1 v2).");
            Assert.LessOrEqual(karakterMb, ArtImportContract.ShippedCharacterBudgetMb, "Karakter doku kümesi (oyuncu + zombi + zırh atlası) sevk bütçesini aşıyor (ADR-015-1 v2).");
            Assert.LessOrEqual(silahMb, ArtImportContract.ShippedWeaponBudgetMb, "Silah doku kümesi (AP-014 SW + AP-013 klasik atlas) sevk bütçesini aşıyor (ADR-015-1 v3, WP-042B).");
            Assert.LessOrEqual(sevkMb, ArtImportContract.ShippedTotalBudgetMb, "Sevk edilen doku kümesi toplam bütçeyi aşıyor (ADR-015-1 v2).");
        }

        /// <summary>
        /// WP-091 Z3 (C-00 06.10) · The shipped lines count what the build carries: every contract map a material under
        /// Assets/_Art binds, through a property its shader declares, is a shipped map (<see cref="ArtImportContract.IsShippedMap"/>).
        /// The Tier Lit _TierMask masks (WP-036 / WP-038H) were bound yet counted as provenance until this check.
        /// </summary>
        [Test]
        public void SevkSatiri_MateryalinBagladigiHerSozlesmeHaritasi_Sayilir()
        {
            var sayilmayan = new List<string>();
            var adlar = new List<string>();
            int materyal = 0, harita = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { ArtRoot }))
            {
                string materyalYolu = AssetDatabase.GUIDToAssetPath(guid);
                // the shader variant pins are not shipped look materials; loading the transparent pin lets URP re-validate it and the
                // editor rewrites it on exit (gt 06.10: a dirty tree stops the R2 chain's verify_tree) — skipped before loading
                if (materyalYolu.Contains("/ArtVariantPins/")) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(materyalYolu);
                if (m == null || m.shader == null) continue;
                materyal++;
                adlar.Clear();
                m.GetTexturePropertyNames(adlar);
                foreach (string ad in adlar)
                {
                    if (m.shader.FindPropertyIndex(ad) < 0) continue;   // a saved property of an earlier shader is never sampled
                    Texture tex = m.GetTexture(ad);
                    string yol = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
                    if (string.IsNullOrEmpty(yol) || !AssetDatabase.IsMainAsset(tex) || !yol.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    if (ArtImportContract.RoleOf(yol) == ArtImportContract.TextureRole.Unknown) continue;   // not a T_<role>_<Map> contract map
                    harita++;
                    if (!ArtImportContract.IsShippedMap(yol)) sayilmayan.Add(AssetDatabase.GetAssetPath(m) + " ." + ad + " → " + yol + " (" + ArtImportContract.RoleOf(yol) + ")");
                }
            }

            Debug.Log("[DD-TEST] wp091.shippedBound materials=" + materyal + " boundContractMaps=" + harita + " uncounted=" + sayilmayan.Count);
            Assert.Greater(materyal, 0, ArtRoot + " altında materyal yok; tarama yalanlanmıştır.");
            Assert.Greater(harita, 0, "materyallerde sözleşme haritası yok; tarama yalanlanmıştır.");
            Assert.IsEmpty(sayilmayan, "Materyalin bağladığı harita sevk satırında sayılmıyor (ArtImportContract.IsShippedMap):\n" + string.Join("\n", sayilmayan));
        }

        private static void PngBoyut(string yol, out int w, out int h)
        {
            using (FileStream f = File.OpenRead(yol))
            {
                var head = new byte[24];
                int read = f.Read(head, 0, 24);
                Assert.AreEqual(24, read, yol + ": PNG başlığı okunamadı");
                Assert.IsTrue(head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G', yol + ": PNG değil (LFS pointer?)");
                w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            }
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
