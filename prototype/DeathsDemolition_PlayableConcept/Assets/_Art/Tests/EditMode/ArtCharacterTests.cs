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
            Assert.AreEqual(498, files.Count, "498 = 476 + 22 AP-033 v001 zombie gait / transition clips (WP-096 S-03 p1–p3: lurch, limp, feral, stomp, drag, heavy, stumble, starts, stops, turns); 476 = 355 + WP-082 R3 AP-026 locomotion 121 (idle_03, 24 loops, 48 starts, 48 stops; AP026-loco-manifest.json); 355 = 351 + WP-082 R3 hero v2 (HeroV2/Hero_v2_TD_LOD0..3.fbx, AP-024D D6: LOD0 avatar source, LOD1–3 rig fragments); 351 = 327 + 24 AP-025D v003 fit A/B garments (WP-084: Modules/AB); 327 = 287 + 40 AP-025 v002 (WP-084: 12 faces Faces/, 24 H2 fit-C garments Modules/C_H2, 4 H2 fit-C tier parts Tier/C_H2); 287 = 272 + 15 AP-022 v008 locomotion (WP-071 R1-C: player 10 TP loops + zombie 5 tier gaits); 272 = 260 + 12 FP Special; 260 = 258 + 2 lmg_2 reloads; 258 = 248 + AP-022 FP arms v2 model + 9 smg_2 FP (WP-071 r2); 248 = 236 + TP Special family 12; 236 = 203 + 33 WP-071 B4c TP (AP-022 v008); 203 = 197 + 6 AP-022 slice 5b (ar_2 FP set); 197 = 174 + 23 AP-022 slice 5 (FP arms); 174 = 153 + 21 AP-022 slices 4a/4b (AR + SMG TP); 153 = 150 + 3 AP-022 slice 2a (electro loop, arc_electro, gel_slip); 150 = 140 + 10 AP-022 slice 1 (WP-071); 140 = 135 (WP-032G + sw_hold_jewel) + 5 AP-007C v003 modülü (H6/H7/A1/A2/T8, WP-038C) — 134 karakter FBX (oyuncu 1 + 2 klip; zombi 3 gövde + 24 modül + 2 klip; WP-017 zırh 18; WP-032C AP-009D v004 85 klip; WP-036 AP-014 tier parçaları 4):\n" + string.Join("\n", files));

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
                if (ArtCharacterContract.IsRigFragment(path)) continue;   // WP-084: the AP-025D v003 faces — Generic, no avatar, bound by bone name
                List<string> map = ArtCharacterContract.HumanMapping(ArtCharacterContract.AvatarOf(path));
                if (!SameList(map, playerMap)) ihlaller.Add(path + ": insan kemiği eşlemesi kaynaktan farklı (" + map.Count + " vs " + playerMap.Count + ")");
            }

            Assert.IsEmpty(ihlaller, "v1.1 ihlali (" + ihlaller.Count + "):\n" + string.Join("\n", ihlaller));
            Debug.Log("[DD-TEST] wp016.contract files=" + files.Count + " humanBones=" + playerMap.Count);
        }

        [Test]
        public void OyuncuModeli_HeroV2_77Kemik_Soketler_4Lod_FpModeliEski()
        {
            // WP-082 R3 · hero v2 (AP-024 v003 via the D6 package): 77 PlayerRig bones = 56 (Root + 52 + 3 sockets) + 7 sec_* + 14 (twist ×8,
            // jaw, fp_camera, socket_mag_l, mag_socket, prop_r, prop_l) — rig v002's ik_hand/ik_foot ×4 are gone, four sockets are new.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterContract.PlayerAvatarSource);
            Assert.IsNotNull(model, ArtCharacterContract.PlayerAvatarSource);
            Dictionary<string, Transform> bones = ArtCharacterContract.Bones(model, ArtCharacterPrefabBuilder.PlayerArmature);
            int secondary = 0; foreach (string b in bones.Keys) if (b.StartsWith(ArtCharacterContract.SecondaryBonePrefix)) secondary++;
            Assert.AreEqual(ArtCharacterContract.PlayerSecondaryBones, secondary, "sec_* kemikleri (sash ×3, band ×2, pouch, gourd)");
            Assert.AreEqual(ArtCharacterContract.HeroV2BoneCount, bones.Count, "PlayerRig altında 77 kemik (AP-024 README)");
            Assert.AreEqual(ArtCharacterContract.BoneCount + ArtCharacterContract.PlayerSecondaryBones + ArtCharacterContract.PlayerRigV002Bones, ArtCharacterContract.HeroV2BoneCount, "56 + 7 + 14 = 77");
            foreach (string s in ArtCharacterContract.Sockets) Assert.IsTrue(bones.ContainsKey(s), "soket yok: " + s);
            foreach (string t in ArtCharacterContract.PlayerTwistBones) Assert.IsTrue(bones.ContainsKey(t), "twist kemiği yok: " + t);
            Assert.AreEqual("Head", bones["fp_camera"].parent.name, "fp_camera Head çocuğu (S-01)");
            // D6 = four files: LOD0 (avatar source) + LOD1–3 rig fragments, each PlayerRig + its own Player_Body_LOD<n>
            for (int lod = 0; lod < ArtCharacterContract.HeroV2LodCount; lod++)
            {
                var file = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterContract.HeroV2LodFile(lod));
                Assert.IsNotNull(file, ArtCharacterContract.HeroV2LodFile(lod));
                Assert.IsNotNull(file.transform.Find("Player_Body_LOD" + lod), "Player_Body_LOD" + lod);
                Assert.IsNotNull(file.transform.Find(ArtCharacterPrefabBuilder.PlayerArmature), "PlayerRig LOD" + lod);
                Assert.IsNull(file.transform.Find("Player_Head_LOD" + lod), "kahramanda baş ayrı değil");
                Assert.AreEqual(lod > 0, ArtCharacterContract.IsRigFragment(ArtCharacterContract.HeroV2LodFile(lod)), "LOD1–3 parça (Generic)");
            }
            int tris0 = (int)(model.transform.Find("Player_Body_LOD0").GetComponent<SkinnedMeshRenderer>().sharedMesh.GetIndexCount(0) / 3);
            Assert.LessOrEqual(tris0, 35000, "AP-024 LOD0 bütçesi 35 000");

            // FP (out of the TD scope) keeps the pre-hero model: rig v002, body + head per LOD.
            var legacy = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterContract.LegacyPlayerModel);
            Assert.IsNotNull(legacy, ArtCharacterContract.LegacyPlayerModel);
            for (int lod = 0; lod < 3; lod++) Assert.IsNotNull(legacy.transform.Find("Player_Body_LOD" + lod), "eski model Player_Body_LOD" + lod);
            Debug.Log("[DD-TEST] wp082.hero.model bones=" + bones.Count + " lod0Tris=" + tris0);
        }

        [Test]
        public void ZombiGovdeleri_56Kemik_17Ikincil_3Ankraj_4Lod_AnkrajKonumlari()
        {
            foreach (string fit in Fits)
            {
                string path = ArtCharacterContract.ZombieFolder + "/zombie_body_" + fit + ".fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path);
                Dictionary<string, Transform> bones = ArtCharacterContract.Bones(model, ArtCharacterPrefabBuilder.ZombieArmature);
                int secondary = 0; foreach (string b in bones.Keys) if (b.StartsWith(ArtCharacterContract.SecondaryBonePrefix)) secondary++;
                Assert.AreEqual(ArtCharacterContract.ZombieSecondaryBones, secondary, fit + ": WP-032F AP-007 v003 sec_* zincir kemikleri (7 zincir / 17)");
                // WP-084 · AP-025 v002 bodies (73-bone ZombieRig, faces bound rigidly to Head): no jaw in the FBX — C-00 05.10 decision (a)
                // puts an empty jaw transform on the prefab (ZombiPrefabi_…), AP-025D brings the weights.
                int jaw = ArtAp025Intake.Active ? (bones.ContainsKey("jaw") ? 1 : 0) : ArtCharacterContract.ZombieJawBones;   // AP-025D may bring it
                Assert.AreEqual(ArtCharacterContract.BoneCount + 3 + ArtCharacterContract.ZombieSecondaryBones + jaw, bones.Count, fit + ": ZombieRig altında 56 + 3 ankraj + 17 sec_* (+ jaw, rig v002 / AP-025D)");
                if (jaw > 0) Assert.AreEqual("Head", bones["jaw"].parent.name, fit + ": jaw Head çocuğu");
                for (int lod = 0; lod < 4; lod++) Assert.IsNotNull(model.transform.Find(ArtAp025Intake.BodyObject(fit, lod)), fit + " LOD" + lod);
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
            Assert.IsNotNull(visual.Events, "WP-018: CharacterAnimationEvents");
            Assert.IsNotNull(visual.AimLayer, "WP-018: PlayerAimLayer");
            Assert.IsNotNull(visual.Animator);
            Assert.IsTrue(visual.Animator.avatar != null && visual.Animator.avatar.isHuman, "Humanoid avatar");
            Assert.IsNotNull(visual.Animator.runtimeAnimatorController, "controller");
            Assert.AreEqual(4, visual.Renderers.Length, "WP-082 R3: hero v2 4 LOD gövde (baş ayrı değil)");
            Assert.AreEqual(0, visual.HeadRenderers.Length, "WP-082 R3: kahramanda ayrı baş renderer'ı yok");
            foreach (LOD l in visual.GetComponentInChildren<LODGroup>().GetLODs()) Assert.AreEqual(1, l.renderers.Length, "LOD = tek gövde");
            var twist = visual.GetComponentInChildren<TwistCorrector>();
            Assert.IsNotNull(twist, "WP-071: TwistCorrector");
            Assert.AreEqual(8, twist.LinkCount, "8 twist kemiği bağlı");
            var material = AssetDatabase.LoadAssetAtPath<Material>(ArtCharacterPrefabBuilder.PlayerMaterial);
            foreach (SkinnedMeshRenderer r in visual.Renderers)
            {
                Assert.IsNotNull(r.sharedMesh, "LOD mesh");
                Assert.AreEqual(material, r.sharedMaterial, "tek paylaşılan M_player");
                Assert.AreEqual(ArtCharacterPrefabBuilder.CharacterRenderingLayers, r.renderingLayerMask, "WP-038G karakter rendering layer");
            }

            Assert.IsNotNull(visual.WeaponSocket);
            Assert.AreEqual(PlayerSkinnedVisual.WeaponSocketName, visual.WeaponSocket.name);
            Assert.AreEqual("RightHand", visual.WeaponSocket.parent.name, "silah soketi sağ el kemiğinin çocuğu");
            Assert.IsNotNull(visual.AnimationSet);
            LOD[] lods = visual.LodGroup.GetLODs();
            Assert.AreEqual(4, lods.Length, "WP-082 R3: 4 LOD (32/52/90/138 m)");
            for (int i = 1; i < lods.Length; i++) Assert.Less(lods[i].screenRelativeTransitionHeight, lods[i - 1].screenRelativeTransitionHeight, "LOD eşikleri azalan");
            Assert.AreEqual(LODFadeMode.CrossFade, visual.LodGroup.fadeMode);
            Assert.IsTrue(material.IsKeywordEnabled("_NORMALMAP") && material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") && material.IsKeywordEnabled("_OCCLUSIONMAP"));
            Assert.IsNotNull(material.GetTexture("_OcclusionMap"), "AO maskenin G kanalından");
            Debug.Log("[DD-TEST] wp016.player.prefab lods=" + lods.Length + " t=" + lods[0].screenRelativeTransitionHeight.ToString("F3") + "/" + lods[1].screenRelativeTransitionHeight.ToString("F3") + "/" + lods[2].screenRelativeTransitionHeight.ToString("F3") + "/" + lods[3].screenRelativeTransitionHeight.ToString("F3"));
        }

        [Test]
        public void ZombiPrefabi_16Slot_Lod4_Ankrajlar_Animator()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.ZombiePrefab);
            Assert.IsNotNull(prefab, "Zombie_AP007 prefabı yok");
            var visual = prefab.GetComponent<ZombieSkinnedVisual>();
            Assert.IsNotNull(visual);
            Assert.IsNotNull(visual.Events, "WP-018: CharacterAnimationEvents");
            Assert.IsNotNull(prefab.GetComponent<CharacterAnimatorLod>(), "WP-018: CharacterAnimatorLod");
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
            Assert.AreEqual(ZombieSkinnedVisual.SlotCount + ZombieSkinnedVisual.ArmorSlotCount * ZombieSkinnedVisual.ArmorPartCount, visual.LodGroup.GetLODs()[0].renderers.Length, "LOD0 beş yuva (WP-084 yüz dâhil) + altı zırh renderer'ı (WP-017)");
            Assert.IsTrue(visual.HasArmorSlots, "WP-017 zırh yuvaları");
            Assert.AreEqual(5, ZombieSkinnedVisual.SlotCount, "WP-084: gövde, üst, alt, baş, yüz (SlotFace)");

            // WP-084 (C-00 05.10 decision (a)): the empty jaw transform under Head at the rig v002 rest, whatever the body FBX carries.
            Transform jaw = null;
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true)) if (t.name == "jaw") { Assert.IsNull(jaw, "tek jaw"); jaw = t; }
            Assert.IsNotNull(jaw, "jaw dönüşümü");
            Assert.AreEqual("Head", jaw.parent.name, "jaw Head çocuğu");
            Assert.Less(Vector3.Distance(ArtCharacterPrefabBuilder.JawRestPosition, jaw.localPosition), 1e-4f, "jaw rest konumu");
            Assert.Less(Quaternion.Angle(ArtCharacterPrefabBuilder.JawRestRotation, jaw.localRotation), 0.01f, "jaw rest dönüşü");

            // WP-084 (unity-specialist + game-developer 06.10): the O1 body is headless — every slot and armour renderer shares one
            // envelope that reaches the face / hair top, and the LODGroup keeps the R1 head-height size (1.857, AP-007 body with head).
            Bounds envelope = visual.Slot(0, ZombieSkinnedVisual.SlotBody).localBounds;
            Transform space = visual.Slot(0, ZombieSkinnedVisual.SlotBody).rootBone;
            float top = float.MinValue;
            for (int c = 0; c < 8; c++)
                top = Mathf.Max(top, space.TransformPoint(new Vector3((c & 1) == 0 ? envelope.min.x : envelope.max.x, (c & 2) == 0 ? envelope.min.y : envelope.max.y, (c & 4) == 0 ? envelope.min.z : envelope.max.z)).y);
            foreach (SkinnedMeshRenderer r in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Assert.AreEqual(envelope, r.localBounds, r.name + ": ortak zarf");
            Debug.Log("[DD-TEST] wp084.prefab.envelope top=" + top.ToString("F3") + " lodGroupSize=" + visual.LodGroup.size.ToString("F3"));
            Assert.GreaterOrEqual(top, 1.75f, "zarf baş tepesine ulaşmalı (yüz / saç)");
            Assert.AreEqual(1.857f, visual.LodGroup.size, 1.857f * 0.05f, "LODGroup boyutu R1 baş yüksekliğinde (±%5)");

            // WP-038G: every character renderer carries the character rendering layer (fill light); armour plates never cast.
            foreach (SkinnedMeshRenderer r in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Assert.AreEqual(ArtCharacterPrefabBuilder.CharacterRenderingLayers, r.renderingLayerMask, r.name + " rendering layer");
                bool armour = r.name.Contains("Helmet") || r.name.Contains("Armor");
                bool far = r.name.StartsWith("LOD2") || r.name.StartsWith("LOD3");
                Assert.AreEqual(armour || far ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode, r.name + " gölge");
            }
        }

        // ================================================================ kütüphane

        [Test]
        public void ZombiKutuphanesi_3Govde_24Modul_444Mesh_KemikAdlari_Butceler()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ArtCharacterPrefabBuilder.ZombieLibrary);
            Assert.IsNotNull(lib);
            Assert.AreEqual(ZombieModuleLibrary.Schema, lib.schema);
            Assert.AreEqual(3, lib.bodies.Length);
            Assert.AreEqual(27, lib.modules.Length);   // WP-038C: AP-007C v003 (+H6/H7, A1/A2 accessory, T8); WP-084: + 3 AP-025 faces
            var hatalar = new List<string>();
            // WP-084 · AP-025 O1 bodies are headless (the face is its own slot) with the 32-vertex neck ring: LOD3 328 / 328 / 336 as
            // delivered (validation/body-fbx-reimport.json). AP-025 budgets the whole actor, not the body — that check is below.
            int[] bodyCeil = ArtAp025Intake.Active ? new[] { 6000, 2500, 900, 340 } : new[] { 6000, 2500, 900, 300 };
            var moduleCeil = new Dictionary<string, int[]> { { "top", new[] { 2800, 1200, 430, 140 } }, { "bottom", new[] { 2400, 1000, 350, 115 } }, { "head", new[] { 900, 380, 150, 55 } }, { "accessory", new[] { 300, 120, 50, 32 } }, { "face", new[] { 12600, 5700, 2400, 820 } } };   // face: AP-025 v002 README budget (11888/5360/2216/752)   // accessory: AP-007C budget 300/120/50/20; A1 LOD3 32 (rim collapse stall, never on screen — README)
            foreach (ZombieModuleLibrary.Body b in lib.bodies)
            {
                for (int lod = 0; lod < 4; lod++) CheckMesh(hatalar, "body " + b.fit + " LOD" + lod, b.lods[lod], bodyCeil[lod]);
            }

            int meshes = 0, tops = 0, bottoms = 0, heads = 0, faces = 0;
            foreach (ZombieModuleLibrary.Module m in lib.modules)
            {
                int expectedVariants = (m.HasColourways ? m.colourways.Length : 1) * 3;
                if (m.variants.Length != expectedVariants) hatalar.Add(m.id + ": " + m.variants.Length + " varyant, beklenen " + expectedVariants);
                if (m.kind == "top") tops++; else if (m.kind == "bottom") bottoms++; else if (m.kind == "head") heads++; else if (m.kind == ZombieModuleLibrary.KindFace) faces++; else if (m.kind != "accessory") hatalar.Add(m.id + ": tür " + m.kind);
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

            // WP-084 · AP-025 actor budget (validation/production-budget-v001.json, Standard [24000, 12000, 5000, 1500] per LOD): the
            // heaviest look a fit can draw — body + heaviest face + top + bottom + head + accessory of that fit — stays inside it.
            if (ArtAp025Intake.Active)
            {
                int[] actorCeil = { 24000, 12000, 5000, 1500 };
                var actor = new List<string>();
                foreach (ZombieModuleLibrary.Body b in lib.bodies)
                {
                    for (int lod = 0; lod < 4; lod++)
                    {
                        int total = b.lods[lod].triangles;
                        foreach (string kind in new[] { ZombieModuleLibrary.KindFace, "top", "bottom", "head", "accessory" })
                        {
                            int worst = 0;
                            foreach (ZombieModuleLibrary.Module m in lib.modules)
                            {
                                if (m.kind != kind) continue;
                                foreach (ZombieModuleLibrary.Variant v in m.variants)
                                    if (v.fit == b.fit && v.lods[lod] != null) worst = Math.Max(worst, v.lods[lod].triangles);
                            }

                            total += worst;
                        }

                        actor.Add(b.fit + " LOD" + lod + " " + total + "/" + actorCeil[lod]);
                        if (total > actorCeil[lod]) hatalar.Add("aktör " + b.fit + " LOD" + lod + ": " + total + " üçgen > AP-025 Standard bütçesi " + actorCeil[lod]);
                    }
                }

                Debug.Log("[DD-TEST] wp084.actor.worst " + string.Join(" | ", actor));
                Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            }

            int accessories = lib.Ids("accessory").Count;
            Assert.AreEqual(8, tops); Assert.AreEqual(7, bottoms); Assert.AreEqual(7, heads); Assert.AreEqual(2, accessories, "A1_straw_hat + A2_face_wrap (accessory slot: library only — the renderer slot / selection rule is the S-03 binder follow-up)");
            Assert.AreEqual(3, faces, "WP-084: AP-025 yüzleri Gaunt / Swollen / Riven");
            Assert.AreEqual(480, meshes, "24 modül × cw × fit × 4 LOD = 444 + 3 yüz × 3 fit × 4 LOD (WP-084) = 480");
            Assert.AreEqual(4, lib.tierMaterials.Length);
            for (int t = 0; t < 4; t++) Assert.IsNotNull(lib.tierMaterials[t], "tier materyali " + t);
            Assert.AreEqual(lib.tierMaterials[0].shader, lib.tierMaterials[1].shader);
            bool o1 = ArtAp025Intake.HasO1Atlas;
            if (o1)
            {
                // WP-084 · AP-025 O1 skin atlas: Tier Lit _ZOMBIE_MASKS on every version, the mask array + one ORM bound, the retired
                // body maps unbound; the version tint rides the tier library (M_zombie_atlas_tint*), not a detail mask.
                for (int t = 0; t < 4; t++)
                {
                    Material m = lib.tierMaterials[t];
                    Assert.AreEqual(ArtTierBuilder.ShaderName, m.shader.name, m.name);
                    Assert.IsTrue(m.IsKeywordEnabled("_ZOMBIE_MASKS"), m.name + " _ZOMBIE_MASKS");
                    Assert.IsNotNull(m.GetTexture("_ZombieMaskArray"), m.name + " maske dizisi");
                    Assert.IsNotNull(m.GetTexture("_ZombieORM"), m.name + " ORM");
                    Assert.IsNull(m.GetTexture("_MetallicGlossMap"), m.name + " eski MS haritası bağlı değil");
                    Assert.IsNull(m.GetTexture("_OcclusionMap"), m.name + " eski AO haritası bağlı değil");
                    Assert.IsFalse(m.IsKeywordEnabled("_DETAIL_MULX2"), m.name + " detay tint yok (tint tier kütüphanesinde)");
                    // S-05 §17 S2: the single ORM read replaces the URP metallic / occlusion maps — their keywords off (no dead samples)
                    Assert.AreEqual(1f, m.GetFloat("_ZombieUseOrm"), m.name + " _ZombieUseOrm");
                    Assert.IsFalse(m.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), m.name + " _METALLICSPECGLOSSMAP kapalı (ORM)");
                    Assert.IsFalse(m.IsKeywordEnabled("_OCCLUSIONMAP"), m.name + " _OCCLUSIONMAP kapalı (ORM)");
                }
            }
            else
            {
                Assert.IsTrue(lib.tierMaterials[1].IsKeywordEnabled("_DETAIL_MULX2") && lib.tierMaterials[1].GetTexture("_DetailMask") != null, "versiyon tintleri DetailMask ile kumaşa sınırlı");
                Assert.IsFalse(lib.tierMaterials[0].IsKeywordEnabled("_DETAIL_MULX2"), "Standard tintsiz");
            }

            Assert.AreEqual(Color.black, lib.tierMaterials[1].GetColor("_EmissionColor"), "emissive yok (DEC-067)");

            // WP-020 · AP-007 v002 second cloth atlas: material order per module (7 atlas+cloth, 7 cloth only, 5 atlas only), M_zombie_cloth ×4,
            // sub-mesh count of every variant mesh equals the module's material count, tier arrays cached (no allocation after warm-up).
            int both = 0, clothOnly = 0, atlasOnly = 0;
            foreach (ZombieModuleLibrary.Module m in lib.modules)
            {
                Assert.IsNotNull(m.materials, m.id + ": materyal sırası");
                string key = string.Join("+", m.materials);
                // WP-084 (AD delta MD3): the order is the FBX sub-mesh order the builder measures from the UVs — [atlas, cloth] (H5–H7, T6)
                // or [cloth, atlas] (B1–B4, B6, B7, A1, T8); ZombiModulleri_AltMeshMalzemesi_UvBolgesiyleUyumlu checks every sub-mesh.
                if (key == ZombieModuleLibrary.MaterialAtlas + "+" + ZombieModuleLibrary.MaterialCloth || key == ZombieModuleLibrary.MaterialCloth + "+" + ZombieModuleLibrary.MaterialAtlas) both++;
                else if (key == ZombieModuleLibrary.MaterialCloth) clothOnly++;
                else if (key == ZombieModuleLibrary.MaterialAtlas) atlasOnly++;
                else hatalar.Add(m.id + ": materyal sırası " + key);
                foreach (ZombieModuleLibrary.Variant v in m.variants)
                    for (int lod = 0; lod < 4; lod++)
                    {
                        if (v.lods[lod] == null || v.lods[lod].mesh == null) continue;
                        // WP-038C: a per-LOD override (fewer sub-meshes: T8 cw1 cloth-only, A1 LOD3 without the cord) must match its mesh; WP-084 MD3:
                        // shorter than the module list and made of its materials (its order is the mesh's own, no longer the list's tail).
                        string[] eff = v.lods[lod].materials ?? m.materials;
                        if (v.lods[lod].mesh.subMeshCount != eff.Length)
                            hatalar.Add(m.id + " " + v.colourway + "/" + v.fit + " LOD" + lod + ": " + v.lods[lod].mesh.subMeshCount + " alt-mesh ≠ " + eff.Length + " materyal");
                        if (v.lods[lod].materials != null && (v.lods[lod].materials.Length >= m.materials.Length || Array.Exists(v.lods[lod].materials, x => Array.IndexOf(m.materials, x) < 0)))
                            hatalar.Add(m.id + " " + v.colourway + "/" + v.fit + " LOD" + lod + ": LOD materyal listesi modül listesinden kısa ve onun malzemelerinden olmalı");
                    }
            }

            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            Assert.AreEqual(11, both, "atlas + kumaş modülleri (v003: +H6, H7, A1, T8)"); Assert.AreEqual(8, clothOnly, "yalnız kumaş (v003: +A2)"); Assert.AreEqual(8, atlasOnly, "yalnız atlas (baş/saç/straw + WP-084 3 yüz)");
            Assert.AreEqual(4, lib.tierClothMaterials.Length);
            for (int t = 0; t < 4; t++) Assert.IsNotNull(lib.tierClothMaterials[t], "kumaş tier materyali " + t);
            Assert.AreNotEqual(lib.tierMaterials[0], lib.tierClothMaterials[0]);
            Assert.IsTrue(lib.tierClothMaterials[1].IsKeywordEnabled("_DETAIL_MULX2") && lib.tierClothMaterials[1].GetTexture("_DetailMask") != null, "kumaş atlası versiyon tint'li (DetailMask 4×4 sabit)");
            Assert.AreEqual(4, lib.tierClothMaterials[1].GetTexture("_DetailMask").width, "kumaş DetailMask 4×4 alt doku (2K Mask sevk edilmez)");
            Assert.IsFalse(lib.tierClothMaterials[0].IsKeywordEnabled("_DETAIL_MULX2"));
            if (o1) for (int t = 0; t < 4; t++) Assert.IsTrue(lib.tierClothMaterials[t].IsKeywordEnabled("_ZOMBIE_MASKS"), "WP-084: kumaşta kir / kan maskeleri " + t);
            Material[] pair = lib.MaterialsFor(new[] { ZombieModuleLibrary.MaterialAtlas, ZombieModuleLibrary.MaterialCloth }, 2);
            Assert.AreSame(pair, lib.MaterialsFor(new[] { ZombieModuleLibrary.MaterialAtlas, ZombieModuleLibrary.MaterialCloth }, 2), "önbellek: aynı dizi");
            Assert.AreEqual(lib.tierMaterials[2], pair[0]); Assert.AreEqual(lib.tierClothMaterials[2], pair[1]);
            Debug.Log("[DD-TEST] wp016.library bodies=3 modules=" + lib.modules.Length + " faces=" + faces + " meshes=" + meshes + " wp020 both=" + both + " cloth=" + clothOnly + " atlas=" + atlasOnly);
        }

        private static void CheckMesh(List<string> hatalar, string what, ZombieModuleLibrary.MeshRef r, int ceiling)
        {
            if (r == null || r.mesh == null) { hatalar.Add(what + ": mesh yok"); return; }
            if (r.boneNames == null || r.boneNames.Length == 0) hatalar.Add(what + ": kemik adı yok");
            else foreach (string n in r.boneNames) if (string.IsNullOrEmpty(n)) { hatalar.Add(what + ": boş kemik adı"); break; }
            if (r.triangles > ceiling) hatalar.Add(what + ": " + r.triangles + " üçgen > tavan " + ceiling);
        }

        /// <summary>
        /// WP-084 · Z4's eyes / teeth, measured on the library's face meshes in the Editor (the character meshes are not readable while
        /// playing): the AP-025 contract marks every non-skin vertex (eyes, teeth, mouth, hair, blood) with vertex colour R = 0 and gives
        /// each face its own atlas rectangle (AP025-material-contract.json atlas_uv_layout; v002 dropped v001's shared "extras" island —
        /// eyes / teeth / gums sit on the face's own island) plus the shared neck strip. Every face, LOD0 and LOD1: colours imported,
        /// ≥ 30 non-skin vertices, and every UV inside those regions, edges included (the lat-long grid's pole row lies ON the rectangle's
        /// max edge — 06.10: 161 / 109 UVs at v = .995 / .495, which Rect.Contains' open max edge had counted as outside; the FBX
        /// UV arrays read directly: 0 outside with the edges closed and a 0.002 margin — the one vertex past an edge is Swollen LOD0's
        /// neck vertex at u = .7455 against the strip's .745).
        /// </summary>
        [Test]
        public void ZombiYuzleri_GozDis_TenDisiKoseler_AtlasBolgesi()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ArtCharacterPrefabBuilder.ZombieLibrary);
            Assert.IsNotNull(lib);
            string contract = File.ReadAllText(ArtAp025Intake.ContractPath);
            Rect neck = ContractRect(contract, "neck_strip_shared_body_and_head_collar");
            var hatalar = new List<string>();
            int faces = 0;
            foreach (ZombieModuleLibrary.Module face in lib.modules)
            {
                if (face.kind != ZombieModuleLibrary.KindFace) continue;
                faces++;
                string name = face.id.Substring(face.id.IndexOf('_') + 1);
                Rect rect = ContractRect(contract, "face_" + char.ToUpperInvariant(name[0]) + name.Substring(1));
                for (int lod = 0; lod < 2; lod++)
                {
                    Mesh m = face.variants[0].lods[lod].mesh;
                    var colors = new List<Color32>();
                    var uv = new List<Vector2>();
                    m.GetColors(colors);
                    m.GetUVs(0, uv);
                    int nonSkin = 0, inRect = 0, inNeck = 0, outside = 0;
                    var outsideBounds = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
                    foreach (Color32 col in colors) if (col.r < 128) nonSkin++;
                    foreach (Vector2 p in uv)
                    {
                        if (InsideClosed(rect, p)) inRect++;
                        else if (InsideClosed(neck, p)) inNeck++;
                        else
                        {
                            outside++;
                            outsideBounds.xMin = Mathf.Min(outsideBounds.xMin, p.x); outsideBounds.yMin = Mathf.Min(outsideBounds.yMin, p.y);
                            outsideBounds.xMax = Mathf.Max(outsideBounds.xMax, p.x); outsideBounds.yMax = Mathf.Max(outsideBounds.yMax, p.y);
                        }
                    }

                    Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[DD-TEST] wp084.face {0} lod{1} vertices={2} colours={3} nonSkin={4} uv={5} inRect={6} inNeck={7} outside={8}{9}",
                        face.id, lod, m.vertexCount, colors.Count, nonSkin, uv.Count, inRect, inNeck, outside,
                        outside > 0 ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " outsideU=[{0:0.0000},{1:0.0000}] outsideV=[{2:0.0000},{3:0.0000}]", outsideBounds.xMin, outsideBounds.xMax, outsideBounds.yMin, outsideBounds.yMax) : ""));
                    if (colors.Count != m.vertexCount) hatalar.Add(face.id + " lod" + lod + ": vertex rengi yok (" + colors.Count + "/" + m.vertexCount + ")");
                    else if (nonSkin < 30) hatalar.Add(face.id + " lod" + lod + ": ten dışı (göz / diş / ağız) köşe " + nonSkin);
                    if (uv.Count != m.vertexCount) hatalar.Add(face.id + " lod" + lod + ": UV yok (" + uv.Count + "/" + m.vertexCount + ")");
                    else if (outside > 0) hatalar.Add(face.id + " lod" + lod + ": " + outside + " UV belgelenmiş bölgelerin dışında");
                }
            }

            Assert.AreEqual(3, faces, "kaynak yok: üç yüz");
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
        }

        /// <summary>Rect.Contains with the max edges closed and an import-precision margin (the contract rectangles are inclusive).</summary>
        private static bool InsideClosed(Rect r, Vector2 p)
        {
            const float eps = 0.002f;
            return p.x >= r.xMin - eps && p.x <= r.xMax + eps && p.y >= r.yMin - eps && p.y <= r.yMax + eps;
        }

        private static Rect ContractRect(string json, string key)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(json,
                "\"" + key + "\"\\s*:\\s*\\[\\s*([-0-9.eE]+)\\s*,\\s*([-0-9.eE]+)\\s*,\\s*([-0-9.eE]+)\\s*,\\s*([-0-9.eE]+)\\s*\\]");
            Assert.IsTrue(m.Success, "sözleşmede " + key + " yok");
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            return Rect.MinMaxRect(float.Parse(m.Groups[1].Value, inv), float.Parse(m.Groups[2].Value, inv), float.Parse(m.Groups[3].Value, inv), float.Parse(m.Groups[4].Value, inv));
        }

        /// <summary>WP-084 (C-00 06.10, Z3b texture budget ≤ 96 MB): the zombie cloth's derived MetallicSmoothness ships at 512² — the
        /// rule, the importer and the loaded texture (the texture-policy tests compare with MaxTextureSizeOf and would follow a removed rule).</summary>
        [Test]
        public void ZombiKumas_MetallicSmoothness_512()
        {
            string path = ArtCharacterPrefabBuilder.ZombieDerived + "/T_zombie_cloth_MetallicSmoothness.png";
            Assert.AreEqual(ArtImportContract.ZombieClothMetallicSmoothnessMaxSize, ArtImportContract.MaxTextureSizeOf(path), "sözleşme");
            Assert.AreEqual(512, ArtImportContract.ZombieClothMetallicSmoothnessMaxSize);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer, "kaynak yok: " + path);
            Assert.AreEqual(512, importer.maxTextureSize, "import");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(tex, path);
            Assert.LessOrEqual(tex.width, 512, "yüklenen doku");
        }

        /// <summary>
        /// WP-084 (AD delta MD3, 06.10) · every garment sub-mesh of the library, every variant and LOD: the material the library binds
        /// (the LOD's list, else the module's) is the atlas exactly when ≥ 95 % of the sub-mesh's UV corners lie in the AP-025
        /// contract's preserved AP-007 regions, cloth when ≤ 85 % (FBX 06.10: atlas ≥ 99.9 %, cloth ≤ 69.4 %). Measured from the meshes, not from the manifest (the AP-007 list
        /// order was wrong for eight garments; ZombieMaterialAtlasTests compares against the same list and could not see it).
        /// </summary>
        [Test]
        public void ZombiModulleri_AltMeshMalzemesi_UvBolgesiyleUyumlu()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ArtCharacterPrefabBuilder.ZombieLibrary);
            Assert.IsNotNull(lib);
            List<Rect> preserved = ArtAp025Intake.PreservedAtlasRects();
            Assert.IsNotEmpty(preserved, "kaynak yok: sözleşmede preserved_AP007");
            var uv = new List<Vector2>();
            var hatalar = new List<string>();
            int subMeshes = 0, twoMaterial = 0;
            var dump = new System.Text.StringBuilder();
            foreach (ZombieModuleLibrary.Module m in lib.modules)
            {
                if (m.kind == ZombieModuleLibrary.KindFace) continue;
                foreach (ZombieModuleLibrary.Variant v in m.variants)
                {
                    for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                    {
                        ZombieModuleLibrary.MeshRef r = v.lods[lod];
                        string[] bound = r.materials != null && r.materials.Length > 0 ? r.materials : m.materials;
                        if (r.mesh.subMeshCount > 1) twoMaterial++;
                        for (int sm = 0; sm < r.mesh.subMeshCount; sm++)
                        {
                            subMeshes++;
                            float share = ArtAp025Intake.ShareInside(r.mesh, sm, preserved, uv);
                            string want = share >= 0.95f ? ZombieModuleLibrary.MaterialAtlas : share <= 0.85f ? ZombieModuleLibrary.MaterialCloth : "?";
                            string got = bound != null && sm < bound.Length ? bound[sm] : "none";
                            if (lod == 0 && r.mesh.subMeshCount > 1) dump.Append(' ').Append(r.mesh.name).Append('[').Append(sm).Append("]=").Append(got).Append('(').Append(share.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(')');
                            if (want != got) hatalar.Add(r.mesh.name + " alt-mesh " + sm + ": bağlı " + got + ", UV'lerin %" + (share * 100f).ToString("F1") + "'i korunan atlas bölgesinde → " + want);
                        }
                    }
                }
            }

            Debug.Log("[DD-TEST] wp084.md3 subMeshes=" + subMeshes + " multiMaterialMeshes=" + twoMaterial + " mismatches=" + hatalar.Count + " |" + dump);
            Assert.Greater(twoMaterial, 0, "kaynak yok: iki malzemeli modül");
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar.GetRange(0, Math.Min(20, hatalar.Count))));
        }

        /// <summary>WP-084 (C-00 06.10, AP-025D v003): the interim top × bottom pairs come from the intake data into the library, and the
        /// look selector never draws one — 3000 seeds × no armour / body / leg / body + leg constraints; the armour constraints still hold.</summary>
        [Test]
        public void LookSecici_AraUstAltCiftleri_HicCizilmez_ZirhKisitiKorunur()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ArtCharacterPrefabBuilder.ZombieLibrary);
            Assert.IsNotNull(lib);
            List<(string top, string bottom)> pairs = ArtAp025Intake.GarmentPairExcludes();
            Assert.AreEqual(pairs.Count, lib.excludedPairTops.Length, "kütüphane çiftleri = alım verisi");
            if (pairs.Count == 0) return;
            var armor = AssetDatabase.LoadAssetAtPath<ZombieArmorLibrary>(ArtArmorBuilder.LibraryPath);
            Assert.IsNotNull(armor);
            var sets = new List<ZombieLookConstraints> { ZombieLookConstraints.None };
            foreach ((bool h, bool b, bool l) in new[] { (false, true, false), (false, false, true), (true, true, true) })
                sets.Add(ZombieLookConstraints.Build(armor, new ArmorLook { helmet = "K1_jingasa", body = "D1_lamellar", leg = "L1_haidate_heavy" }, h, b, l));
            int draws = 0;
            var hatalar = new List<string>();
            foreach (ZombieLookConstraints c in sets)
                for (uint seed = 1; seed <= 3000; seed++)
                {
                    ZombieLook look = ZombieLookSelector.Select(lib, seed, c);
                    draws++;
                    if (lib.ExcludesPair(look.top, look.bottom)) hatalar.Add(seed + ": " + look.top + " × " + look.bottom);
                    if (c.ExcludesBottom(look.bottom) || c.ExcludesTop(look.top)) hatalar.Add(seed + ": zırh kısıtı bozuldu " + look.Key);
                    if (hatalar.Count > 20) break;
                }

            Debug.Log("[DD-TEST] wp084.pairs draws=" + draws + " pairs=" + pairs.Count + " violations=" + hatalar.Count);
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
        }

        /// <summary>
        /// Z4b — the lower face rides the jaw: every face LOD lists the "jaw" bone (teeth / lower lip weighted to it). C-00 05.10
        /// decision (a): v002 faces are bound to Head and the prefab carries an empty jaw transform; AP-025D delivers the weights.
        /// WP-084 (C-00 06.10, AP-025D v003 MUST-5): live — the v003 faces carry the jaw weights; the mouth sac was checked back-face
        /// culled (AP-025D jaw-check2), so every zombie skin / cloth material must draw Cull Back. EditMode (r10c 06.10): the face meshes
        /// are not readable, which only the editor outside play mode may read.
        /// </summary>
        [Test]
        public void Z4b_YuzCenesi_JawAgirligi_CullBack()
        {
            var library = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ArtCharacterPrefabBuilder.ZombieLibrary);
            Assert.IsNotNull(library, "zombie module library");
            // 3d-modeller 06.10: Blender lists every deform bone, so "jaw in the bone list" proves nothing — the lower face must be
            // WEIGHTED to it at every LOD, the shared collar ring (y 1.50) must carry no jaw weight, and the jaw's bind pose must be
            // the rig v002 rest under Head (0, 0.045, −0.012), the empty jaw the prefab carries today.
            var rest = new Vector3(0f, 0.045f, -0.012f);
            var restRotation = new Quaternion(-0.8468003f, 0f, 0f, 0.5319109f);
            int faces = 0;
            var bad = new List<string>();
            if (library.modules != null)
            {
                foreach (ZombieModuleLibrary.Module m in library.modules)
                {
                    if (m == null || m.kind != ZombieModuleLibrary.KindFace) continue;
                    faces++;
                    ZombieModuleLibrary.Variant v = m.variants[0];
                    for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                    {
                        ZombieModuleLibrary.MeshRef r = v.lods[lod];
                        int jaw = r != null && r.boneNames != null ? Array.IndexOf(r.boneNames, "jaw") : -1, head = r != null && r.boneNames != null ? Array.IndexOf(r.boneNames, "Head") : -1;
                        if (jaw < 0 || head < 0 || r.mesh == null) { bad.Add(m.id + " lod" + lod + ": jaw/Head listede yok"); continue; }
                        var perVertex = r.mesh.GetBonesPerVertex();
                        var weights = r.mesh.GetAllBoneWeights();
                        Vector3[] vertices = r.mesh.vertices;
                        int weighted = 0, ringJaw = 0, w = 0;
                        for (int i = 0; i < perVertex.Length; i++)
                        {
                            for (int k = 0; k < perVertex[i]; k++, w++)
                            {
                                if (weights[w].boneIndex != jaw || weights[w].weight <= 0f) continue;
                                weighted++;
                                if (Mathf.Abs(vertices[i].y - 1.5f) < 0.005f) ringJaw++;
                            }
                        }

                        Matrix4x4 local = r.mesh.bindposes[head] * r.mesh.bindposes[jaw].inverse;
                        float posErr = Vector3.Distance(local.GetColumn(3), rest), rotErr = Quaternion.Angle(local.rotation, restRotation);
                        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[DD-OBS] r2.z4b {0} lod{1} jawWeighted={2} ringJaw={3} bindPosErrMm={4:0.000} bindRotErrDeg={5:0.000}", m.id, lod, weighted, ringJaw, posErr * 1000f, rotErr));
                        if (weighted == 0) bad.Add(m.id + " lod" + lod + ": jaw ağırlıklı köşe yok");
                        if (ringJaw > 0) bad.Add(m.id + " lod" + lod + ": boyun halkasında jaw ağırlığı " + ringJaw);
                        if (posErr > 0.0001f || rotErr > 0.05f) bad.Add(m.id + " lod" + lod + ": jaw bind pozu rig v002 rest'inden farklı");
                    }
                }
            }

            // the mouth sac's inner wall is hidden by back-face culling (AP-025D jaw-check2): skin atlas and cloth, every version
            for (int tier = 0; tier < 4; tier++)
            {
                foreach (Material mat in new[] { library.MaterialFor(tier), library.tierClothMaterials != null && tier < library.tierClothMaterials.Length ? library.tierClothMaterials[tier] : null })
                {
                    if (mat == null) continue;
                    float cull = mat.HasProperty("_Cull") ? mat.GetFloat("_Cull") : -1f;
                    Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[DD-OBS] r2.z4b cull {0} tier{1} _Cull={2}", mat.name, tier, cull));
                    if (cull != (float)UnityEngine.Rendering.CullMode.Back) bad.Add(mat.name + ": _Cull " + cull + " (Back = 2 bekleniyor)");
                }
            }

            Assert.Greater(faces, 0, "kaynak yok: yüz modülü yok");
            Assert.IsEmpty(bad, string.Join("\n", bad));
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

            // WP-084 · AP-025 faces: every look draws one, the three shares stay near a third, and the face is the LAST draw — the same
            // library without faces gives every look bit-for-bit (earlier keys, pinned seeds and tests unchanged).
            List<string> faceIds = lib.Ids(ZombieModuleLibrary.KindFace);
            Assert.AreEqual(3, faceIds.Count, "3 yüz");
            var noFaces = UnityEngine.Object.Instantiate(lib);
            try
            {
                noFaces.modules = Array.FindAll(lib.modules, x => x.kind != ZombieModuleLibrary.KindFace);
                var faceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                seed = 1u;
                for (int i = 0; i < n; i++)
                {
                    unchecked { seed = seed * 1664525u + 1013904223u; }
                    ZombieLook withFace = ZombieLookSelector.Select(lib, seed), without = ZombieLookSelector.Select(noFaces, seed);
                    Assert.IsTrue(withFace.HasFace, withFace.Key + ": yüz yok");
                    Assert.IsFalse(without.HasFace);
                    Assert.AreEqual(without.Key + "/" + withFace.face, withFace.Key, "yüz son çekiliş: önceki çekilişler aynı");
                    faceCounts[withFace.face] = (faceCounts.TryGetValue(withFace.face, out int c) ? c : 0) + 1;
                }

                foreach (string id in faceIds) Assert.That((float)(faceCounts.TryGetValue(id, out int c) ? c : 0) / n, Is.InRange(0.25f, 0.42f), "yüz payı " + id);
                Debug.Log("[DD-TEST] wp084.faces n=" + n + " " + string.Join(" ", faceIds.ConvertAll(id => id + "=" + (faceCounts.TryGetValue(id, out int c) ? c : 0))));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(noFaces);
            }
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
                AnimatorState state = controller.layers[0].stateMachine.defaultState;
                Assert.AreEqual(CharacterAnimationSet.LocomotionState, state.name);
                var tree = state.motion as BlendTree;
                Assert.IsNotNull(tree);
                if (set.IsAuthored)
                {
                    // WP-018: AP-009 set — the locomotion tree carries the idle and the walk (player 2-D corners; zombie nested idle/gait trees).
                    // WP-071 MUST-3 (C-00 01.10, P-06 AD refuter B): the player tree's centre is the static stand (DD_Player_StandStill) and the
                    // AP-022 idle breathes at rate 1 in its own base state Idle_Breath — the idle is the motion of that state instead (H5: rule change).
                    AnimatorState breath = null;
                    foreach (ChildAnimatorState st in controller.layers[0].stateMachine.states) if (st.state.name == CharacterAnimatorContract.StillState) breath = st.state;
                    bool idleOk = ContainsMotion(tree, set.Idle) || (breath != null && breath.motion == set.Idle && tree.children.Length > 0 && tree.children[0].motion != null
                                                                     && tree.children[0].motion.name == CharacterAnimatorContract.PlayerStandStill);
                    Assert.IsTrue(idleOk && ContainsMotion(tree, set.Walk), "locomotion ağacı walk (+ idle ya da statik duruş ve Idle_Breath) içermeli");
                    Assert.AreEqual(0.5f, set.RateFor(set.Walk.name, set.SpeedOf(set.Walk.name) * 0.5f), 1e-4f);
                }
                else
                {
                    bool speed = false;
                    foreach (AnimatorControllerParameter p in controller.parameters) if (p.name == CharacterAnimationSet.SpeedParameter && p.type == AnimatorControllerParameterType.Float) speed = true;
                    Assert.IsTrue(speed, "Speed parametresi");
                    Assert.AreEqual(2, tree.children.Length);
                    Assert.AreEqual(set.Idle, tree.children[0].motion); Assert.AreEqual(set.Walk, tree.children[1].motion);
                    Assert.AreEqual(0.5f, set.SpeedParameterFor(set.WalkSpeedMetersPerSecond * 0.5f), 1e-4f);
                }
            }
        }

        private static bool ContainsMotion(BlendTree tree, Motion motion)
        {
            foreach (ChildMotion c in tree.children)
            {
                if (c.motion == motion) return true;
                if (c.motion is BlendTree sub && ContainsMotion(sub, motion)) return true;
            }

            return false;
        }

        [Test]
        public void AssetSeti_Resources_Skinned_TumReferanslar_VeYuklenebilir()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAssetSet>(ArtCharacterPrefabBuilder.CharacterAssets);
            Assert.IsNotNull(set);
            Assert.AreEqual(CharacterVisualSource.Skinned, set.Source);
            Assert.IsTrue(set.PlayerReady && set.ZombieReady, "referanslar eksik");
            Assert.AreEqual(3, set.PlayerSkinAlbedos.Length);
            // WP-082 R3 (plan A5, AP-024D D5): the hero atlas's skins — 1 the v003 BaseColor (D4 kept), 2 Steel/Indigo, 3 olive-moss/cream.
            Assert.AreEqual("T_hero_v2_BaseColor", set.PlayerSkinAlbedos[0].name);
            Assert.AreEqual("T_hero_v2_skin2_BaseColor", set.PlayerSkinAlbedos[1].name);
            Assert.AreEqual("T_hero_v2_skin3_BaseColor", set.PlayerSkinAlbedos[2].name);
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
            bool o1 = ArtAp025Intake.HasO1Atlas;   // WP-084: the AP-025 O1 atlas retires the body Mask / DetailMask for one ORM + the mask array
            var paths = new List<string>
            {
                ArtCharacterPrefabBuilder.PlayerTextures + "/T_player_skin1_BaseColor.png",
                ArtCharacterPrefabBuilder.PlayerTextures + "/T_player_Normal.png",
                ArtCharacterPrefabBuilder.ZombieTextures + "/T_zombie_atlas_BaseColor.png",
                ArtCharacterPrefabBuilder.PlayerDerived + "/T_player_MetallicSmoothness.png"
            };
            paths.AddRange(ArtCharacterPrefabBuilder.HeroV2TextureFiles());   // WP-082 R3 · hero v2 (BaseColor / Normal 2048, ORM / derived MS ≤ 1024)
            if (o1) paths.Add(ArtCharacterPrefabBuilder.ZombieTextures + "/T_zombie_atlas_ORM.png");
            else paths.AddRange(new[] { ArtCharacterPrefabBuilder.ZombieTextures + "/T_zombie_atlas_Mask.png", ArtCharacterPrefabBuilder.ZombieDerived + "/T_zombie_atlas_DetailMask.png" });
            foreach (string p in paths)
            {
                Assert.AreEqual(ArtImportContract.TextureClass.Character, ArtImportContract.ClassOf(p), p);
                var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                Assert.IsNotNull(importer, p);
                // Contract v1.3 (WP-038 C): sources 2048, derived masks (MetallicSmoothness / DetailMask) ≤ 1024.
                Assert.AreEqual(ArtImportContract.IsCappedMaskRole(ArtImportContract.RoleOf(p)) ? ArtImportContract.DerivedMaskMaxSize : 2048, importer.maxTextureSize, p);   // WP-038H: the packed _Mask too
                Assert.IsTrue(importer.streamingMipmaps, p);
                Assert.IsEmpty(ArtImportContract.ValidateTexture(importer, ArtImportContract.RoleOf(p)), p);
            }

            Assert.IsTrue(File.Exists(ArtCharacterPrefabBuilder.PlayerDerived + "/T_player_MetallicSmoothness.provenance.json"));
            Assert.IsTrue(File.Exists(ArtCharacterPrefabBuilder.HeroV2Derived + "/T_hero_v2_MetallicSmoothness.provenance.json"));
            if (!o1)
            {
                var mask = AssetImporter.GetAtPath(ArtCharacterPrefabBuilder.ZombieTextures + "/T_zombie_atlas_Mask.png") as TextureImporter;
                Assert.IsFalse(mask.sRGBTexture); Assert.AreEqual(TextureImporterAlphaSource.FromInput, mask.alphaSource);
                Assert.IsTrue(File.Exists(ArtCharacterPrefabBuilder.ZombieDerived + "/T_zombie_atlas_DetailMask.provenance.json"));
                return;
            }

            // WP-084 · the AP-025 mask array: a 3×3 sheet of 1024² single-channel slices → one Texture2DArray of 9 slices (skin / dirt /
            // blood × 3), BC4, linear, mipmapped; the ORM linear, ≤ 1024; the retired body maps gone from disk.
            string arrayPath = ArtCharacterPrefabBuilder.ZombieTextures + "/T_zombie_masks_Array.png";
            var arrayImporter = AssetImporter.GetAtPath(arrayPath) as TextureImporter;
            Assert.IsNotNull(arrayImporter, arrayPath);
            Assert.AreEqual(ArtImportContract.TextureRole.Array, ArtImportContract.RoleOf(arrayPath));
            Assert.IsEmpty(ArtImportContract.ValidateTexture(arrayImporter, ArtImportContract.TextureRole.Array), arrayPath);
            Assert.AreEqual(TextureImporterShape.Texture2DArray, arrayImporter.textureShape);
            Assert.IsFalse(arrayImporter.sRGBTexture, "maske dizisi doğrusal");
            var array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(arrayPath);
            Assert.IsNotNull(array, "Texture2DArray");
            Assert.AreEqual(9, array.depth, "9 dilim");
            Assert.AreEqual(1024, array.width); Assert.AreEqual(1024, array.height);
            Assert.AreEqual(TextureFormat.BC4, array.format, "BC4 tek kanal");
            Assert.Greater(array.mipmapCount, 1, "mip");
            var orm = AssetImporter.GetAtPath(ArtCharacterPrefabBuilder.ZombieTextures + "/T_zombie_atlas_ORM.png") as TextureImporter;
            Assert.IsFalse(orm.sRGBTexture, "ORM doğrusal");
            foreach (string retired in new[] { "/T_zombie_atlas_Packed.png", "/T_zombie_atlas_Mask.png" })
                Assert.IsFalse(File.Exists(ArtCharacterPrefabBuilder.ZombieTextures + retired), "emekli: " + retired);
            foreach (string retired in new[] { "/T_zombie_atlas_MetallicSmoothness.png", "/T_zombie_atlas_DetailMask.png" })
                Assert.IsFalse(File.Exists(ArtCharacterPrefabBuilder.ZombieDerived + retired), "emekli: " + retired);
            Debug.Log("[DD-TEST] wp084.masks array " + array.width + "x" + array.height + "x" + array.depth + " " + array.format + " mips=" + array.mipmapCount);
        }

        private static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
