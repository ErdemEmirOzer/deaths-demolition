using System;
using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-016 · Import Contract v1.1 (ADR-016-1) and the zombie look contract (ADR-016-2):
    /// every character FBX on the skinned contract with a valid humanoid avatar whose
    /// human-bone mapping equals its source's; sockets and anchors present; the generated
    /// player / zombie prefabs, animation sets and controllers wired; the module library
    /// complete (3 bodies × 4 LOD, 19 modules × cw × fit × 4 LOD, bone orders, budgets);
    /// the look selector deterministic and inside the AP-007 crowd ratio rule over 1000
    /// seeds; the Resources asset set pointing at all of it; builders idempotent.
    /// </summary>
    [TestFixture]
    [Category("WP016")]
    public sealed class ArtCharacterTests
    {
        private static readonly string[] Fits = { "A", "B", "C" };

        private static List<string> CharacterFbx()
        {
            var list = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtImportContract.CharactersRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (ArtCharacterContract.IsCharacterModel(path)) list.Add(path);
            }

            list.Sort(string.CompareOrdinal);
            return list;
        }

        // ================================================================ sözleşme v1.1

        [Test]
        public void HerKarakterFbx_V11Sozlesmesi_Avatar_HumanEslemesiKaynaklaAyni()
        {
            List<string> files = CharacterFbx();
            Assert.AreEqual(45, files.Count, "45 karakter FBX (oyuncu 1 + 2 klip; zombi 3 gövde + 19 modül + 2 klip; WP-017 zırh 18):\n" + string.Join("\n", files));

            List<string> playerMap = ArtCharacterContract.HumanMapping(ArtCharacterContract.AvatarOf(ArtCharacterContract.PlayerAvatarSource));
            List<string> zombieMap = ArtCharacterContract.HumanMapping(ArtCharacterContract.AvatarOf(ArtCharacterContract.ZombieAvatarSource));
            // The contract writes the explicit 52-bone template mapping into every importer (the
            // auto-mapper found 35 of 52 on the player, 52 on the zombie — ADR-016-1).
            foreach (string required in ArtCharacterContract.RequiredHumanBones)
            {
                Assert.IsTrue(playerMap.Exists(l => l.StartsWith(required + "=", StringComparison.Ordinal)), "oyuncu avatarında eksik insan kemiği: " + required);
            }

            Assert.AreEqual(ArtCharacterContract.HumanoidBoneCount, playerMap.Count, "oyuncu avatarı 52 insan kemiği eşlemeli (şablon)");
            Assert.IsTrue(playerMap.Contains("Left Little Distal=LeftPinky3"), "parmak eşlemesi (Pinky → Little)");
            Assert.AreEqual(playerMap, zombieMap, "zombi avatarı oyuncu şablonuyla aynı insan kemiği eşlemesini vermeli (AP-007 iskelet paritesi)");

            var ihlaller = new List<string>();
            foreach (string path in files)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { ihlaller.Add(path + ": ModelImporter yok"); continue; }
                List<string> v = ArtCharacterContract.Validate(importer);
                if (v.Count > 0) ihlaller.Add(path + ": " + string.Join("; ", v));
                List<string> map = ArtCharacterContract.HumanMapping(ArtCharacterContract.AvatarOf(path));
                if (!SameList(map, playerMap)) ihlaller.Add(path + ": insan kemiği eşlemesi kaynaktan farklı (" + map.Count + " vs " + playerMap.Count + ")");
            }

            Assert.IsEmpty(ihlaller, "v1.1 ihlali (" + ihlaller.Count + "):\n" + string.Join("\n", ihlaller));
            Debug.Log("[DD-TEST] wp016.contract files=" + files.Count + " humanBones=" + playerMap.Count);
        }

        [Test]
        public void OyuncuModeli_56Kemik_3Soket_3Lod()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterContract.PlayerAvatarSource);
            Assert.IsNotNull(model);
            Dictionary<string, Transform> bones = ArtCharacterContract.Bones(model, ArtCharacterPrefabBuilder.PlayerArmature);
            Assert.AreEqual(ArtCharacterContract.BoneCount, bones.Count, "PlayerRig altında 56 transform (Root + 52 + 3 soket)");
            foreach (string s in ArtCharacterContract.Sockets) Assert.IsTrue(bones.ContainsKey(s), "soket yok: " + s);
            for (int lod = 0; lod < 3; lod++) Assert.IsNotNull(model.transform.Find("Player_LOD" + lod), "Player_LOD" + lod);
            int tris0 = (int)(model.transform.Find("Player_LOD0").GetComponent<SkinnedMeshRenderer>().sharedMesh.GetIndexCount(0) / 3);
            Assert.LessOrEqual(tris0, 25000, "AP-006 LOD0 tavanı 25 000");
        }

        [Test]
        public void ZombiGovdeleri_56Kemik_3Ankraj_4Lod_AnkrajKonumlari()
        {
            foreach (string fit in Fits)
            {
                string path = ArtCharacterContract.ZombieFolder + "/zombie_body_" + fit + ".fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path);
                Dictionary<string, Transform> bones = ArtCharacterContract.Bones(model, ArtCharacterPrefabBuilder.ZombieArmature);
                Assert.AreEqual(ArtCharacterContract.BoneCount + 3, bones.Count, fit + ": ZombieRig altında 56 + 3 ankraj");
                for (int lod = 0; lod < 4; lod++) Assert.IsNotNull(model.transform.Find("zombie_body_" + fit + "_LOD" + lod), fit + " LOD" + lod);
                // Anchor origins (AP007-anchors.json, Blender z → Unity y): helmet 1.60, torso 1.17, legs 0.93; parents Head / Chest / Hips.
                AssertAnchor(bones, ZombieSkinnedVisual.AnchorHelmet, "Head", 1.60f);
                AssertAnchor(bones, ZombieSkinnedVisual.AnchorTorso, "Chest", 1.17f);
                AssertAnchor(bones, ZombieSkinnedVisual.AnchorLegs, "Hips", 0.93f);
            }
        }

        private static void AssertAnchor(Dictionary<string, Transform> bones, string anchor, string parent, float y)
        {
            Assert.IsTrue(bones.TryGetValue(anchor, out Transform t), "ankraj yok: " + anchor);
            Assert.AreEqual(parent, t.parent.name, anchor + " ebeveyni");
            Assert.AreEqual(y, t.position.y, 0.02f, anchor + " yüksekliği");
            Assert.Less(Mathf.Abs(t.position.x) + Mathf.Abs(t.position.z), 0.02f, anchor + " eksende");
        }

        // ================================================================ prefablar

        [Test]
        public void OyuncuPrefabi_SkinnedVisual_Animator_Lod3_Soket_Materyal()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.PlayerPrefab);
            Assert.IsNotNull(prefab, "Player_AP006 prefabı yok (Run Characters koşmalı)");
            var visual = prefab.GetComponent<PlayerSkinnedVisual>();
            Assert.IsNotNull(visual);
            Assert.IsNotNull(visual.Bridge);
            Assert.IsNotNull(visual.Animator);
            Assert.IsTrue(visual.Animator.avatar != null && visual.Animator.avatar.isHuman, "Humanoid avatar");
            Assert.IsNotNull(visual.Animator.runtimeAnimatorController, "controller");
            Assert.AreEqual(3, visual.Renderers.Length);
            var material = AssetDatabase.LoadAssetAtPath<Material>(ArtCharacterPrefabBuilder.PlayerMaterial);
            foreach (SkinnedMeshRenderer r in visual.Renderers)
            {
                Assert.IsNotNull(r.sharedMesh, "LOD mesh");
                Assert.AreEqual(material, r.sharedMaterial, "tek paylaşılan M_player");
            }

            Assert.IsNotNull(visual.WeaponSocket);
            Assert.AreEqual(PlayerSkinnedVisual.WeaponSocketName, visual.WeaponSocket.name);
            Assert.AreEqual("RightHand", visual.WeaponSocket.parent.name, "silah soketi sağ el kemiğinin çocuğu");
            Assert.IsNotNull(visual.AnimationSet);
            LOD[] lods = visual.LodGroup.GetLODs();
            Assert.AreEqual(3, lods.Length);
            for (int i = 1; i < lods.Length; i++) Assert.Less(lods[i].screenRelativeTransitionHeight, lods[i - 1].screenRelativeTransitionHeight, "LOD eşikleri azalan");
            Assert.AreEqual(LODFadeMode.CrossFade, visual.LodGroup.fadeMode);
            Assert.IsTrue(material.IsKeywordEnabled("_NORMALMAP") && material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") && material.IsKeywordEnabled("_OCCLUSIONMAP"));
            Assert.IsNotNull(material.GetTexture("_OcclusionMap"), "AO maskenin G kanalından");
            Debug.Log("[DD-TEST] wp016.player.prefab lods=" + lods.Length + " t=" + lods[0].screenRelativeTransitionHeight.ToString("F3") + "/" + lods[1].screenRelativeTransitionHeight.ToString("F3") + "/" + lods[2].screenRelativeTransitionHeight.ToString("F3"));
        }

        [Test]
        public void ZombiPrefabi_16Slot_Lod4_Ankrajlar_Animator()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.ZombiePrefab);
            Assert.IsNotNull(prefab, "Zombie_AP007 prefabı yok");
            var visual = prefab.GetComponent<ZombieSkinnedVisual>();
            Assert.IsNotNull(visual);
            Assert.IsNotNull(visual.Bridge);
            Assert.IsTrue(visual.Animator != null && visual.Animator.avatar != null && visual.Animator.avatar.isHuman);
            Assert.IsNotNull(visual.Animator.runtimeAnimatorController);
            for (int lod = 0; lod < 4; lod++)
            for (int slot = 0; slot < ZombieSkinnedVisual.SlotCount; slot++)
                Assert.IsNotNull(visual.Slot(lod, slot), "slot " + lod + "/" + slot);
            Assert.IsNotNull(visual.Slot(0, ZombieSkinnedVisual.SlotBody).sharedMesh, "gövde A LOD0 mesh");
            Assert.IsNotNull(visual.HelmetAnchor); Assert.IsNotNull(visual.TorsoAnchor); Assert.IsNotNull(visual.LegsAnchor);
            Assert.AreEqual("Head", visual.HelmetAnchor.parent.name);
            Assert.AreEqual("Chest", visual.TorsoAnchor.parent.name);
            Assert.AreEqual("Hips", visual.LegsAnchor.parent.name);
            Assert.AreEqual(4, visual.LodGroup.GetLODs().Length);
            Assert.AreEqual(ZombieSkinnedVisual.SlotCount + ZombieSkinnedVisual.ArmorSlotCount * ZombieSkinnedVisual.ArmorPartCount, visual.LodGroup.GetLODs()[0].renderers.Length, "LOD0 dört giysi + altı zırh renderer'ı (WP-017)");
            Assert.IsTrue(visual.HasArmorSlots, "WP-017 zırh yuvaları");
        }

        // ================================================================ kütüphane

        [Test]
        public void ZombiKutuphanesi_3Govde_19Modul_360Mesh_KemikAdlari_Butceler()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ArtCharacterPrefabBuilder.ZombieLibrary);
            Assert.IsNotNull(lib);
            Assert.AreEqual(ZombieModuleLibrary.Schema, lib.schema);
            Assert.AreEqual(3, lib.bodies.Length);
            Assert.AreEqual(19, lib.modules.Length);
            var hatalar = new List<string>();
            int[] bodyCeil = { 6000, 2500, 900, 300 };
            var moduleCeil = new Dictionary<string, int[]> { { "top", new[] { 2800, 1200, 430, 140 } }, { "bottom", new[] { 2400, 1000, 350, 115 } }, { "head", new[] { 900, 380, 150, 55 } } };
            foreach (ZombieModuleLibrary.Body b in lib.bodies)
            {
                for (int lod = 0; lod < 4; lod++) CheckMesh(hatalar, "body " + b.fit + " LOD" + lod, b.lods[lod], bodyCeil[lod]);
            }

            int meshes = 0, tops = 0, bottoms = 0, heads = 0;
            foreach (ZombieModuleLibrary.Module m in lib.modules)
            {
                int expectedVariants = (m.HasColourways ? m.colourways.Length : 1) * 3;
                if (m.variants.Length != expectedVariants) hatalar.Add(m.id + ": " + m.variants.Length + " varyant, beklenen " + expectedVariants);
                if (m.kind == "top") tops++; else if (m.kind == "bottom") bottoms++; else if (m.kind == "head") heads++; else hatalar.Add(m.id + ": tür " + m.kind);
                foreach (ZombieModuleLibrary.Variant v in m.variants)
                {
                    for (int lod = 0; lod < 4; lod++)
                    {
                        // T7 mino cape LOD0 is a Tripo mesh (2796 tris, README) — the top ceiling 2800 holds.
                        CheckMesh(hatalar, m.id + " " + v.colourway + "/" + v.fit + " LOD" + lod, v.lods[lod], moduleCeil[m.kind][lod]);
                        meshes++;
                    }
                }
            }

            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            Assert.AreEqual(7, tops); Assert.AreEqual(7, bottoms); Assert.AreEqual(5, heads);
            Assert.AreEqual(360, meshes, "19 modül × cw × fit × 4 LOD = 360");
            Assert.AreEqual(4, lib.tierMaterials.Length);
            for (int t = 0; t < 4; t++) Assert.IsNotNull(lib.tierMaterials[t], "tier materyali " + t);
            Assert.AreEqual(lib.tierMaterials[0].shader, lib.tierMaterials[1].shader);
            Assert.IsTrue(lib.tierMaterials[1].IsKeywordEnabled("_DETAIL_MULX2") && lib.tierMaterials[1].GetTexture("_DetailMask") != null, "versiyon tintleri DetailMask ile kumaşa sınırlı");
            Assert.IsFalse(lib.tierMaterials[0].IsKeywordEnabled("_DETAIL_MULX2"), "Standard tintsiz");
            Assert.AreEqual(Color.black, lib.tierMaterials[1].GetColor("_EmissionColor"), "emissive yok (DEC-067)");
            Debug.Log("[DD-TEST] wp016.library bodies=3 modules=19 meshes=" + meshes);
        }

        private static void CheckMesh(List<string> hatalar, string what, ZombieModuleLibrary.MeshRef r, int ceiling)
        {
            if (r == null || r.mesh == null) { hatalar.Add(what + ": mesh yok"); return; }
            if (r.boneNames == null || r.boneNames.Length == 0) hatalar.Add(what + ": kemik adı yok");
            else foreach (string n in r.boneNames) if (string.IsNullOrEmpty(n)) { hatalar.Add(what + ": boş kemik adı"); break; }
            if (r.triangles > ceiling) hatalar.Add(what + ": " + r.triangles + " üçgen > tavan " + ceiling);
        }

        // ================================================================ look seçici

        [Test]
        public void LookSecici_Deterministik_OranKurali_1000Seed()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ArtCharacterPrefabBuilder.ZombieLibrary);
            Assert.IsNotNull(lib);
            ZombieLook a = ZombieLookSelector.Select(lib, 12345u);
            ZombieLook b = ZombieLookSelector.Select(lib, 12345u);
            Assert.AreEqual(a.Key, b.Key, "aynı seed → aynı look");

            int n = 1000, cwPicks = 0, cw2 = 0, mino = 0, bald = 0, strapsFundoshi = 0, invalid = 0;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var fits = new Dictionary<string, int>();
            uint seed = 1u;
            for (int i = 0; i < n; i++)
            {
                unchecked { seed = seed * 1664525u + 1013904223u; }   // the EnemyFactory seed stream
                ZombieLook look = ZombieLookSelector.Select(lib, seed);
                if (!look.IsValid) { invalid++; continue; }
                keys.Add(look.Key);
                fits[look.fit] = (fits.TryGetValue(look.fit, out int f) ? f : 0) + 1;
                if (look.topColourway != ZombieModuleLibrary.NoColourway) { cwPicks++; if (look.topColourway == ZombieModuleLibrary.ColourwayFaded) cw2++; }
                if (look.bottomColourway != ZombieModuleLibrary.NoColourway) { cwPicks++; if (look.bottomColourway == ZombieModuleLibrary.ColourwayFaded) cw2++; }
                if (look.top == ZombieModuleLibrary.TopMinoCape) mino++;
                if (look.head == ZombieModuleLibrary.HeadNone) bald++;
                if (look.top == ZombieModuleLibrary.TopBareStraps && look.bottom == ZombieModuleLibrary.BottomFundoshi) strapsFundoshi++;
                Assert.IsNotNull(lib.FindModule(look.top).Find(look.topColourway, look.fit), look.Key + " top varyantı kütüphanede yok");
                Assert.IsNotNull(lib.FindModule(look.bottom).Find(look.bottomColourway, look.fit), look.Key + " bottom varyantı yok");
                if (look.head != ZombieModuleLibrary.HeadNone) Assert.IsNotNull(lib.FindModule(look.head).Find(ZombieModuleLibrary.NoColourway, look.fit), look.Key + " head varyantı yok");
            }

            float fadedShare = cwPicks > 0 ? (float)cw2 / cwPicks : 0f;
            Debug.Log("[DD-TEST] wp016.looks n=" + n + " distinct=" + keys.Count + " faded=" + fadedShare.ToString("F3") + " mino=" + ((float)mino / n).ToString("F3") + " bald=" + ((float)bald / n).ToString("F3") + " fits=" + fits["A"] + "/" + fits["B"] + "/" + fits["C"] + " strapsFundoshi=" + strapsFundoshi);
            Assert.AreEqual(0, invalid);
            Assert.LessOrEqual(fadedShare, ZombieLookSelector.MaxFadedShare, "cw2 payı ≤ %30");
            Assert.LessOrEqual((float)mino / n, ZombieLookSelector.MaxMinoShare, "T7 ≤ %10");
            Assert.LessOrEqual((float)bald / n, ZombieLookSelector.MaxBaldShare, "H0 ≤ %25");
            Assert.AreEqual(0, strapsFundoshi, "T6 + B5 asla birlikte");
            Assert.Greater(keys.Count, 700, "1000 spawn'da ≥ 700 farklı look (tekrar oranı düşük)");
            foreach (string fit in Fits) Assert.Greater(fits[fit], 250, "fit " + fit + " dağılımı");

            // Mino cape under body armour → another top, same everything else.
            uint minoSeed = 0;
            for (uint s = 1; s < 50000 && minoSeed == 0; s++) if (ZombieLookSelector.Select(lib, s).top == ZombieModuleLibrary.TopMinoCape) minoSeed = s;
            Assert.AreNotEqual(0u, minoSeed, "50 000 seed içinde mino yok?");
            ZombieLook m = ZombieLookSelector.Select(lib, minoSeed);
            ZombieLook swapped = ZombieLookSelector.WithoutMinoCape(lib, m, minoSeed);
            Assert.AreNotEqual(ZombieModuleLibrary.TopMinoCape, swapped.top);
            Assert.AreEqual(m.fit, swapped.fit); Assert.AreEqual(m.bottom, swapped.bottom); Assert.AreEqual(m.head, swapped.head);
        }

        // ================================================================ animasyon + asset seti

        [Test]
        public void AnimasyonSetleri_IdleWalk_Dongu_Controller_BlendTree()
        {
            foreach (string path in new[] { ArtCharacterPrefabBuilder.PlayerAnimationSet, ArtCharacterPrefabBuilder.ZombieAnimationSet })
            {
                var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(path);
                Assert.IsNotNull(set, path);
                Assert.IsNotNull(set.Idle); Assert.IsNotNull(set.Walk);
                Assert.IsTrue(set.Idle.isLooping && set.Walk.isLooping, "klipler döngülü");
                Assert.IsTrue(set.Idle.humanMotion && set.Walk.humanMotion, "klipler Humanoid");
                var controller = set.Controller as AnimatorController;
                Assert.IsNotNull(controller);
                bool speed = false;
                foreach (AnimatorControllerParameter p in controller.parameters) if (p.name == CharacterAnimationSet.SpeedParameter && p.type == AnimatorControllerParameterType.Float) speed = true;
                Assert.IsTrue(speed, "Speed parametresi");
                AnimatorState state = controller.layers[0].stateMachine.defaultState;
                Assert.AreEqual(CharacterAnimationSet.LocomotionState, state.name);
                var tree = state.motion as BlendTree;
                Assert.IsNotNull(tree);
                Assert.AreEqual(2, tree.children.Length);
                Assert.AreEqual(set.Idle, tree.children[0].motion); Assert.AreEqual(set.Walk, tree.children[1].motion);
                Assert.AreEqual(0.5f, set.SpeedParameterFor(set.WalkSpeedMetersPerSecond * 0.5f), 1e-4f);
            }
        }

        [Test]
        public void AssetSeti_Resources_Skinned_TumReferanslar_VeYuklenebilir()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAssetSet>(ArtCharacterPrefabBuilder.CharacterAssets);
            Assert.IsNotNull(set);
            Assert.AreEqual(CharacterVisualSource.Skinned, set.Source);
            Assert.IsTrue(set.PlayerReady && set.ZombieReady, "referanslar eksik");
            Assert.AreEqual(3, set.PlayerSkinAlbedos.Length);
            Assert.AreEqual("T_player_skin1_BaseColor", set.PlayerSkinAlbedos[0].name);
            Assert.AreEqual("T_player_skin2a_BaseColor", set.PlayerSkinAlbedos[1].name, "skin 2 = 2a (kullanıcı kararı; 2b gelirse tek doku takası)");
            Assert.AreEqual("T_player_skin3_BaseColor", set.PlayerSkinAlbedos[2].name);
            CharacterAssetSet.ResetCache();
            Assert.AreSame(set, CharacterAssetSet.Load(), "Resources.Load aynı varlığı bulmalı");
            CharacterAssetSet.ResetCache();
        }

        // ================================================================ idempotans

        [Test]
        public void Builder_Idempotan_KutuphaneVeAssetSeti()
        {
            var lib = ArtCharacterPrefabBuilder.BuildZombieLibrary();
            string once = EditorJsonUtility.ToJson(lib);
            string guid = AssetDatabase.AssetPathToGUID(ArtCharacterPrefabBuilder.ZombieLibrary);
            lib = ArtCharacterPrefabBuilder.BuildZombieLibrary();
            Assert.AreEqual(once, EditorJsonUtility.ToJson(lib), "ikinci koşu kütüphaneyi değiştirmemeli");
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(ArtCharacterPrefabBuilder.ZombieLibrary));
            var set = ArtCharacterPrefabBuilder.BuildAssetSet();
            string setOnce = EditorJsonUtility.ToJson(set);
            Assert.AreEqual(setOnce, EditorJsonUtility.ToJson(ArtCharacterPrefabBuilder.BuildAssetSet()));
        }

        // ================================================================ dokular

        [Test]
        public void KarakterDokulari_CharacterSinifi_2048_Streaming_TuretilmisMaskeler()
        {
            foreach (string p in new[]
            {
                ArtCharacterPrefabBuilder.PlayerTextures + "/T_player_skin1_BaseColor.png",
                ArtCharacterPrefabBuilder.PlayerTextures + "/T_player_Normal.png",
                ArtCharacterPrefabBuilder.ZombieTextures + "/T_zombie_atlas_BaseColor.png",
                ArtCharacterPrefabBuilder.ZombieTextures + "/T_zombie_atlas_Mask.png",
                ArtCharacterPrefabBuilder.ZombieDerived + "/T_zombie_atlas_DetailMask.png",
                ArtCharacterPrefabBuilder.PlayerDerived + "/T_player_MetallicSmoothness.png"
            })
            {
                Assert.AreEqual(ArtImportContract.TextureClass.Character, ArtImportContract.ClassOf(p), p);
                var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                Assert.IsNotNull(importer, p);
                Assert.AreEqual(2048, importer.maxTextureSize, p);
                Assert.IsTrue(importer.streamingMipmaps, p);
                Assert.IsEmpty(ArtImportContract.ValidateTexture(importer, ArtImportContract.RoleOf(p)), p);
            }

            var mask = AssetImporter.GetAtPath(ArtCharacterPrefabBuilder.ZombieTextures + "/T_zombie_atlas_Mask.png") as TextureImporter;
            Assert.IsFalse(mask.sRGBTexture); Assert.AreEqual(TextureImporterAlphaSource.FromInput, mask.alphaSource);
            Assert.IsTrue(File.Exists(ArtCharacterPrefabBuilder.PlayerDerived + "/T_player_MetallicSmoothness.provenance.json"));
            Assert.IsTrue(File.Exists(ArtCharacterPrefabBuilder.ZombieDerived + "/T_zombie_atlas_DetailMask.provenance.json"));
        }

        private static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
