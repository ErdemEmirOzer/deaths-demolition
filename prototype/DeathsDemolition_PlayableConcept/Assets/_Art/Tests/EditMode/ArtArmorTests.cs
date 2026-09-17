using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-017 · AP-008 armour on the skinned contract (ADR-017-1): the 18 armour FBX files on
    /// Import Contract v1.1 with the zombie avatar mapping and the three anchors at the AP-007
    /// heights; every (variant, state, fit, LOD) resolvable by the manifest naming (the Broken
    /// files drop the manifest's "_any" token — recorded); the library complete with bone
    /// names inside the ZombieRig and LOD0 triangles under the AP-008 budgets; the atlas
    /// materials (lacquer-only version tint); the armour look and the garment constraints
    /// deterministic and free of every forbidden combination over 1000 seeds × 8 compositions;
    /// the Resources asset set carrying the library; the textures in the Character class.
    /// </summary>
    [TestFixture]
    [Category("WP017")]
    public sealed class ArtArmorTests
    {
        private static readonly string[] Fits = { "A", "B", "C" };
        private static readonly ArmorVisualState[] States = { ArmorVisualState.Present, ArmorVisualState.Damaged, ArmorVisualState.Broken };

        private static List<string> ArmorFbx()
        {
            var list = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtArmorBuilder.ArmorFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (ArtArmorBuilder.IsArmorModel(path)) list.Add(path);
            }

            list.Sort(string.CompareOrdinal);
            return list;
        }

        private static ZombieArmorLibrary Library()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ZombieArmorLibrary>(ArtArmorBuilder.LibraryPath);
            Assert.IsNotNull(lib, "zırh kütüphanesi yok: " + ArtArmorBuilder.LibraryPath + " (Run Characters koşmalı)");
            return lib;
        }

        private static ZombieModuleLibrary Modules()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ArtCharacterPrefabBuilder.ZombieLibrary);
            Assert.IsNotNull(lib, "modül kütüphanesi yok");
            return lib;
        }

        // ================================================================ sözleşme

        [Test]
        public void ZirhFbx_18Dosya_V11Sozlesmesi_ZombiAvatarEslemesi_Ankrajlar()
        {
            List<string> files = ArmorFbx();
            Assert.AreEqual(ArtArmorBuilder.ExpectedFbxCount, files.Count, "18 zırh FBX:\n" + string.Join("\n", files));
            List<string> zombieMap = ArtCharacterContract.HumanMapping(ArtCharacterContract.AvatarOf(ArtCharacterContract.ZombieAvatarSource));
            Assert.AreEqual(ArtCharacterContract.HumanoidBoneCount, zombieMap.Count);

            var hatalar = new List<string>();
            foreach (string path in files)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { hatalar.Add(path + ": ModelImporter yok"); continue; }
                List<string> v = ArtCharacterContract.Validate(importer);
                if (v.Count > 0) hatalar.Add(path + ": " + string.Join("; ", v));
                List<string> map = ArtCharacterContract.HumanMapping(ArtCharacterContract.AvatarOf(path));
                if (string.Join("|", map) != string.Join("|", zombieMap)) hatalar.Add(path + ": avatar insan eşlemesi zombie_body_A'dan farklı (" + map.Count + " kemik)");

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Dictionary<string, Transform> bones = ArtCharacterContract.Bones(model, ArtCharacterPrefabBuilder.ZombieArmature);
                foreach ((string anchor, string parent, float height) in new[] { ("anchor_helmet", "Head", 1.60f), ("anchor_torso", "Chest", 1.17f), ("anchor_legs", "Hips", 0.93f) })
                {
                    if (!bones.TryGetValue(anchor, out Transform a)) { hatalar.Add(path + ": " + anchor + " yok"); continue; }
                    if (a.parent == null || a.parent.name != parent) hatalar.Add(path + ": " + anchor + " ebeveyni " + (a.parent != null ? a.parent.name : "-") + " ≠ " + parent);
                    if (Mathf.Abs(a.position.y - height) > 0.01f) hatalar.Add(path + ": " + anchor + " yüksekliği " + a.position.y.ToString("F3") + " ≠ " + height);
                }
            }

            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp017.armor.contract files=" + files.Count + " humanBones=" + zombieMap.Count);
        }

        [Test]
        public void ZirhManifesti_HerParcaFitLod_Cozulur_ButcelerIcinde()
        {
            List<ArtArmorBuilder.ManifestPiece> pieces = ArtArmorBuilder.ReadManifest(out Dictionary<string, object> doc, out _);
            Assert.AreEqual(18, pieces.Count, "manifest 18 parça (6 varyant × 3 durum)");
            Dictionary<string, object> budgets = ArtJson.AsObject(ArtJson.AsObject(doc["budgets"], "budgets")["per_family_LOD0_3"], "per_family_LOD0_3");

            var hatalar = new List<string>();
            int deviations = 0, meshes = 0;
            foreach (ArtArmorBuilder.ManifestPiece p in pieces)
            {
                string fbx = ArtArmorBuilder.ArmorFolder + "/" + Path.GetFileName(p.fbx);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (model == null) { hatalar.Add(p.variant + " " + p.state + ": FBX yok " + fbx); continue; }
                List<object> budget = ArtJson.AsList(budgets[p.state == ZombieArmorLibrary.StateBroken ? "Broken" : p.family], p.family);
                foreach (string fit in Fits)
                for (int lod = 0; lod < ZombieArmorLibrary.LodCount; lod++)
                {
                    int tris = 0;
                    foreach (string name in ArtArmorBuilder.ObjectNames(p, fit, lod))
                    {
                        Transform t = ArtArmorBuilder.FindObject(model, name, out string resolved);
                        if (t == null) { hatalar.Add(p.variant + " " + p.state + " " + fit + " LOD" + lod + ": '" + name + "' yok"); continue; }
                        if (resolved != name) deviations++;
                        var smr = t.GetComponent<SkinnedMeshRenderer>();
                        if (smr == null || smr.sharedMesh == null) { hatalar.Add(resolved + ": skinned mesh değil"); continue; }
                        tris += (int)(smr.sharedMesh.GetIndexCount(0) / 3);
                        meshes++;
                    }

                    int cap = ArtJson.AsInt(budget[lod]);
                    if (tris > cap) hatalar.Add(p.variant + " " + p.state + " " + fit + " LOD" + lod + ": " + tris + " üçgen > bütçe " + cap);
                }
            }

            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Debug.Log("[DD-TEST] wp017.armor.manifest pieces=" + pieces.Count + " meshes=" + meshes + " nameDeviations=" + deviations + " (Broken '_any' token absent in the export)");
        }

        // ================================================================ kütüphane

        [Test]
        public void ZirhKutuphanesi_6Varyant_3Fit_3Durum_4Lod_KemikAdlari_Materyaller()
        {
            ZombieArmorLibrary lib = Library();
            Assert.AreEqual(ZombieArmorLibrary.Schema, lib.schema);
            Assert.IsTrue(lib.IsReady);
            Assert.AreEqual(6, lib.variants.Length);
            Assert.AreEqual(2, lib.Ids(ArmorSlot.Helmet).Count); Assert.AreEqual(2, lib.Ids(ArmorSlot.Body).Count); Assert.AreEqual(2, lib.Ids(ArmorSlot.Leg).Count);

            var rig = new HashSet<string>(ArtCharacterContract.Bones(AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterContract.ZombieAvatarSource), ArtCharacterPrefabBuilder.ZombieArmature).Keys, StringComparer.Ordinal);
            var hatalar = new List<string>();
            int refs = 0, subs = 0;
            foreach (ZombieArmorLibrary.Variant v in lib.variants)
            {
                if (v.slot == ArmorSlot.None) hatalar.Add(v.id + ": slot None");
                foreach (string fit in Fits)
                {
                    ZombieArmorLibrary.Fit f = v.FindFit(fit);
                    if (f == null) { hatalar.Add(v.id + ": fit " + fit + " yok"); continue; }
                    foreach (ArmorVisualState state in States)
                    for (int lod = 0; lod < ZombieArmorLibrary.LodCount; lod++)
                    {
                        ZombieModuleLibrary.MeshRef main = f.Of(state).Part(ZombieArmorLibrary.PartMain, lod);
                        ZombieModuleLibrary.MeshRef sub = f.Of(state).Part(ZombieArmorLibrary.PartSub, lod);
                        if (main == null || main.mesh == null) { hatalar.Add(v.id + " " + state + " " + fit + " LOD" + lod + ": ana mesh yok"); continue; }
                        refs++;
                        bool expectSub = state != ArmorVisualState.Broken && v.slot != ArmorSlot.Helmet;
                        if (expectSub != (sub != null && sub.mesh != null)) hatalar.Add(v.id + " " + state + " " + fit + " LOD" + lod + ": alt mesh " + (sub != null ? "var" : "yok") + " (beklenen " + expectSub + ")");
                        if (sub != null && sub.mesh != null) subs++;
                        foreach (ZombieModuleLibrary.MeshRef m in new[] { main, sub })
                        {
                            if (m == null || m.mesh == null) continue;
                            if (m.boneNames == null || m.boneNames.Length == 0) { hatalar.Add(m.mesh.name + ": kemik adı yok"); continue; }
                            foreach (string b in m.boneNames) if (!rig.Contains(b)) { hatalar.Add(m.mesh.name + ": kemik '" + b + "' ZombieRig'de yok"); break; }
                        }
                    }
                }
            }

            Assert.IsEmpty(hatalar, "İhlal (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Assert.AreEqual(6 * 3 * 3 * 4, refs, "ana mesh referansı");
            Assert.AreEqual(4 * 2 * 3 * 4, subs, "alt mesh referansı (Body + Leg, Present + Damaged)");
            Assert.AreEqual(4, lib.tierMaterials.Length);
            for (int t = 0; t < 4; t++)
            {
                Material m = lib.tierMaterials[t];
                Assert.IsNotNull(m, "tier materyali " + t);
                Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(ArtArmorBuilder.BaseColorMap), m.GetTexture("_BaseMap"), "atlas");
                Assert.IsFalse(m.enableInstancing, "skinned: instancing kapalı");
                if (t > 0)
                {
                    Assert.IsTrue(m.IsKeywordEnabled("_DETAIL_MULX2"), "tier " + t + " detay tint");
                    Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(ArtArmorBuilder.DetailMaskMap), m.GetTexture("_DetailMask"), "tier " + t + " lake maskesi");
                    Assert.IsNotNull(m.GetTexture("_DetailAlbedoMap"), "tier " + t + " tint dokusu");
                }
                else
                {
                    Assert.IsFalse(m.IsKeywordEnabled("_DETAIL_MULX2"), "Standard = doğal siyah lake");
                }
            }

            Debug.Log("[DD-TEST] wp017.armor.library variants=" + lib.variants.Length + " mainRefs=" + refs + " subRefs=" + subs + " lod0 " + string.Join(",", Array.ConvertAll(lib.variants, x => x.id + "=" + x.trianglesLod0)));
        }

        [Test]
        public void ZirhDislama_Manifestten_Kutuphaneye()
        {
            ZombieArmorLibrary lib = Library();
            CollectionAssert.AreEquivalent(new[] { "H2_hair_matted", "H3_topknot_broken", "H4_tenugui_wrap", "H5_hachimaki_band" }, lib.Find("K2_kabuto").excludedModules);
            CollectionAssert.IsEmpty(lib.Find("K1_jingasa").excludedModules, "jingasa her başla");
            CollectionAssert.AreEquivalent(new[] { "T2_kosode_long", "T6_bare_straps", "T7_mino_cape" }, lib.Find("D1_lamellar").excludedModules);
            CollectionAssert.AreEquivalent(new[] { "T6_bare_straps", "T7_mino_cape" }, lib.Find("D2_plate").excludedModules);
            CollectionAssert.AreEquivalent(new[] { "B1_hakama_wide", "B3_koshimaki_wrap", "B4_tattsuke_hakama", "B6_hakama_torn" }, lib.Find("L1_haidate_heavy").excludedModules);
            CollectionAssert.AreEquivalent(new[] { "B1_hakama_wide", "B3_koshimaki_wrap", "B4_tattsuke_hakama", "B6_hakama_torn" }, lib.Find("L2_suneate_heavy").excludedModules);
        }

        // ================================================================ seçici ve kurallar

        [Test]
        public void ZirhSecici_Deterministik_VaryantDagilimi_1000Seed()
        {
            ZombieArmorLibrary lib = Library();
            Assert.AreEqual(ArmorLookSelector.Select(lib, 777u).Key, ArmorLookSelector.Select(lib, 777u).Key, "aynı seed aynı zırh");
            var counts = new Dictionary<string, int>();
            for (uint seed = 1; seed <= 1000; seed++)
            {
                ArmorLook a = ArmorLookSelector.Select(lib, seed);
                Assert.IsNotNull(lib.Find(a.helmet), "helmet " + a.helmet); Assert.IsNotNull(lib.Find(a.body), "body " + a.body); Assert.IsNotNull(lib.Find(a.leg), "leg " + a.leg);
                foreach (string id in new[] { a.helmet, a.body, a.leg }) counts[id] = (counts.TryGetValue(id, out int n) ? n : 0) + 1;
            }

            var lines = new List<string>();
            foreach (KeyValuePair<string, int> kv in counts)
            {
                lines.Add(kv.Key + "=" + kv.Value);
                Assert.That(kv.Value, Is.InRange(400, 600), kv.Key + " payı %40–60 (eşit ağırlık)");
            }

            Debug.Log("[DD-TEST] wp017.armor.looks n=1000 " + string.Join(" ", lines));
        }

        /// <summary>
        /// Every armour composition (8) × 1000 seeds: the garment look under the constraints
        /// never carries a forbidden module (AP-008 exclusions), faded cloth sits under every
        /// Body / Leg piece, the unconstrained look is byte-for-byte the WP-016 one, and the
        /// crowd ratio rule still holds on the unarmoured draw.
        /// </summary>
        [Test]
        public void GiysiKisitlari_8Bilesim_1000Seed_YasakKombinasyon0_cw2ZirhAltinda()
        {
            ZombieArmorLibrary armor = Library();
            ZombieModuleLibrary modules = Modules();
            int forbidden = 0, changed = 0, total = 0, fadedUnderArmor = 0, armoredCloth = 0;
            for (uint seed = 1; seed <= 1000; seed++)
            {
                ArmorLook a = ArmorLookSelector.Select(armor, seed);
                ZombieLook plain = ZombieLookSelector.Select(modules, seed);
                Assert.AreEqual(plain.Key, ZombieLookSelector.Select(modules, seed, ZombieLookConstraints.None).Key, "boş kısıt = WP-016 görünümü");
                for (int mask = 0; mask < 8; mask++)
                {
                    bool helmet = (mask & 1) != 0, body = (mask & 2) != 0, leg = (mask & 4) != 0;
                    ZombieLookConstraints c = ZombieLookConstraints.Build(armor, a, helmet, body, leg);
                    ZombieLook look = ZombieLookSelector.Select(modules, seed, c);
                    total++;
                    Assert.IsTrue(look.IsValid, "seed " + seed + " mask " + mask);
                    Assert.AreEqual(plain.fit, look.fit, "fit kısıtla değişmez");
                    if (look.Key != plain.Key) changed++;
                    if (helmet && armor.Find(a.helmet).Excludes(look.head)) forbidden++;
                    if (body && armor.Find(a.body).Excludes(look.top)) forbidden++;
                    if (leg && armor.Find(a.leg).Excludes(look.bottom)) forbidden++;
                    if (body && look.top == ZombieModuleLibrary.TopMinoCape) forbidden++;
                    if (body && modules.FindModule(look.top).HasColourways) { armoredCloth++; if (look.topColourway == ZombieModuleLibrary.ColourwayFaded) fadedUnderArmor++; }
                    if (leg && modules.FindModule(look.bottom).HasColourways) { armoredCloth++; if (look.bottomColourway == ZombieModuleLibrary.ColourwayFaded) fadedUnderArmor++; }
                    if (look.top == ZombieModuleLibrary.TopBareStraps && look.bottom == ZombieModuleLibrary.BottomFundoshi) forbidden++;
                }
            }

            Debug.Log("[DD-TEST] wp017.armor.constraints draws=" + total + " changedByRules=" + changed + " forbidden=" + forbidden + " fadedUnderArmor=" + fadedUnderArmor + "/" + armoredCloth);
            Assert.AreEqual(0, forbidden, "yasak kombinasyon");
            Assert.AreEqual(armoredCloth, fadedUnderArmor, "zırh altındaki kumaş cw2");
            Assert.Greater(changed, 0, "kurallar en az bir görünümü değiştirmeli");
        }

        // ================================================================ asset seti + dokular

        [Test]
        public void AssetSeti_ZirhKutuphanesi_Yuklenebilir_PrefabZirhYuvalari()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAssetSet>(ArtCharacterPrefabBuilder.CharacterAssets);
            Assert.IsNotNull(set);
            Assert.IsTrue(set.ArmorReady, "asset seti zırh kütüphanesini taşımalı");
            Assert.AreEqual(Library(), set.ZombieArmor);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.ZombiePrefab);
            var visual = prefab.GetComponent<ZombieSkinnedVisual>();
            Assert.IsTrue(visual.HasArmorSlots);
            for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
            for (int slot = 0; slot < ZombieSkinnedVisual.ArmorSlotCount; slot++)
            for (int part = 0; part < ZombieSkinnedVisual.ArmorPartCount; part++)
            {
                SkinnedMeshRenderer r = visual.ArmorRenderer(lod, slot, part);
                Assert.IsNotNull(r, "zırh renderer " + lod + "/" + slot + "/" + part);
                Assert.IsFalse(r.enabled, "prefab'ta zırh kapalı");
                Assert.AreEqual(Library().MaterialFor(0), r.sharedMaterial, "zırh atlası (Standard)");
            }
        }

        [Test]
        public void ZirhDokulari_CharacterSinifi_2048_TuretilmisMaskeler_LakeAlfasi()
        {
            foreach (string path in new[] { ArtArmorBuilder.BaseColorMap, ArtArmorBuilder.NormalMap, ArtArmorBuilder.PackedMap, ArtArmorBuilder.MaskMap, ArtArmorBuilder.MetallicSmoothnessMap, ArtArmorBuilder.DetailMaskMap })
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, path);
                Assert.AreEqual(ArtImportContract.TextureClass.Character, ArtImportContract.ClassOf(path), path);
                // Contract v1.3 (WP-038 C): sources 2048, derived masks ≤ 1024.
                Assert.AreEqual(ArtImportContract.IsDerivedMaskRole(ArtImportContract.RoleOf(path)) ? ArtImportContract.DerivedMaskMaxSize : 2048, importer.maxTextureSize, path);
                Assert.IsTrue(importer.streamingMipmaps, path + " streaming");
                Assert.IsEmpty(ArtImportContract.ValidateTexture(importer, ArtImportContract.RoleOf(path)), path);
            }

            // Provenance sidecars name the algorithms; the detail mask's alpha is the lacquer channel (not cloth as on the AP-007 atlas).
            string prov = File.ReadAllText(ArtArmorBuilder.Derived + "/T_zombie_armor_DetailMask.provenance.json");
            StringAssert.Contains("lacquer", prov);
            Assert.AreEqual(ArtArmorBuilder.LacquerDetailMaskAlgorithm, ArtArmorBuilder.LacquerMaskToDetailMask(new Color32(200, 10, 20, 255)).a == 200 ? ArtArmorBuilder.LacquerDetailMaskAlgorithm : "R→A değil", "DetailMask.A = Mask.R");
        }
    }
}
