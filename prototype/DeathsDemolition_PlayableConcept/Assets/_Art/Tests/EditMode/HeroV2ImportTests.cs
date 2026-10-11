using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-082 R3 (S-06, plan §2 A1–A5 / §3) · hero v2 "Kırık Mühür" as the player model: the AP-024 v003 data in P-06's D6
    /// package (exports/v004 Hero_v2_TD_LOD0..3: PlayerRig + Player_Body_LOD&lt;n&gt; each) imports on Contract v1.1 — LOD0 with a
    /// valid humanoid avatar, LOD1–3 Generic and re-bound by bone name onto the LOD0 skeleton (bind poses equal); the 77
    /// bones and their parents are the AP-024 rig's (table in HeroV2-provenance.json, from validation/hero_rig_final_clean_bones.json);
    /// the bind pose is oriented like the pre-hero player body at scale 1 and stands 1.82 m; four LODs at the AP-024 triangle counts; M_player binds the hero maps;
    /// the sockets hang where the README puts them; the RightShoulder Down-Up muscle at rest is read into the log (MUST-4 (iii),
    /// TP scope: value only); the FP viewmodel stays on the pre-hero model and its arms mesh asset is byte-identical to before.
    /// </summary>
    [TestFixture]
    [Category("WP082")]
    public sealed class HeroV2ImportTests
    {
        /// <summary>AP-024 v002/v003 README, measured in Blender: LOD0..3 triangles.</summary>
        private static readonly int[] HeroLodTriangles = { 34144, 17802, 7860, 2958 };
        /// <summary>SHA-256 of Generated/Player_FPArms.asset on the branch base (d0d6c6c5, before the hero intake).</summary>
        private const string FpArmsAssetSha256 = "d9e3f99a6cd520a77027d6aa234c646fc8b641c3f7017c802495e176d22acd73";
        /// <summary>The TD rig (DD_FeelConfig: height 15 m, back 13 m, FOV 42°) — distance to the figure ≈ 19.1 m (plan §1 [hesap]).</summary>
        private const float TdDistance = 19.1f, TdFov = 42f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static GameObject Model()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterContract.PlayerAvatarSource);
            Assert.IsNotNull(model, ArtCharacterContract.PlayerAvatarSource + " yok (AP-024D D6 alınmadı)");
            return model;
        }

        private static PlayerSkinnedVisual Visual()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.PlayerPrefab);
            Assert.IsNotNull(prefab, ArtCharacterPrefabBuilder.PlayerPrefab);
            var visual = prefab.GetComponent<PlayerSkinnedVisual>();
            Assert.IsNotNull(visual);
            return visual;
        }

        [Test]
        public void Avatar_GecerliHumanoid_SablonEslemesi_PrefabKahramanAvatari()
        {
            var importer = AssetImporter.GetAtPath(ArtCharacterContract.PlayerAvatarSource) as ModelImporter;
            Assert.IsNotNull(importer);
            Assert.IsEmpty(ArtCharacterContract.Validate(importer), "Contract v1.1");
            Assert.AreEqual(ModelImporterAnimationType.Human, importer.animationType);
            Assert.AreEqual(ModelImporterAvatarSetup.CreateFromThisModel, importer.avatarSetup);
            Assert.IsFalse(importer.optimizeGameObjects);
            Assert.IsTrue(importer.bakeAxisConversion, "bakeAxisConversion (kök X −90° ağaca işlenir)");
            Assert.IsFalse(importer.importBlendShapes);

            Avatar avatar = ArtCharacterContract.AvatarOf(ArtCharacterContract.PlayerAvatarSource);
            Assert.IsNotNull(avatar, "avatar");
            Assert.IsTrue(avatar.isValid, "avatar.isValid");
            Assert.IsTrue(avatar.isHuman, "avatar.isHuman");
            List<string> map = ArtCharacterContract.HumanMapping(avatar);
            Assert.AreEqual(ArtCharacterContract.HumanoidBoneCount, map.Count, "52 insan kemiği (şablon: 22 gövde + 30 parmak)");
            foreach (string required in new[] { "UpperChest=UpperChest", "LeftShoulder=LeftShoulder", "RightShoulder=RightShoulder", "Left Little Distal=LeftPinky3", "Right Thumb Proximal=RightThumb1" })
                Assert.IsTrue(map.Contains(required), "eşleme: " + required);

            PlayerSkinnedVisual visual = Visual();
            Assert.AreSame(avatar, visual.Animator.avatar, "prefab Animator'ı kahraman avatarını kullanmalı");
            Assert.IsNotNull(visual.Animator.runtimeAnimatorController, "controller (mevcut DD_Player) bağlı");
            Debug.Log("[DD-TEST] wp082.hero.avatar isValid=" + avatar.isValid + " isHuman=" + avatar.isHuman + " humanBones=" + map.Count
                      + " controller=" + visual.Animator.runtimeAnimatorController.name);
        }

        [Test]
        public void Iskelet_77Ad_Ebeveynler_KahramanTablosuyla_Ayni()
        {
            string json = File.ReadAllText(ArtCharacterPrefabBuilder.HeroV2Provenance);
            var doc = ArtJson.AsObject(ArtJson.Parse(json), "provenance");
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (object o in ArtJson.AsList(ArtJson.Require(doc, "bones"), "bones"))
            {
                List<object> pair = ArtJson.AsList(o, "bone");
                expected[ArtJson.AsString(pair[0], "name")] = pair[1] == null ? null : ArtJson.AsString(pair[1], "parent");
            }

            Assert.AreEqual(ArtCharacterContract.HeroV2BoneCount, expected.Count, "provenance kemik tablosu 77");
            Dictionary<string, Transform> bones = ArtCharacterContract.Bones(Model(), ArtCharacterPrefabBuilder.PlayerArmature);
            var errors = new List<string>();
            foreach (KeyValuePair<string, string> kv in expected)
            {
                if (!bones.TryGetValue(kv.Key, out Transform t)) { errors.Add(kv.Key + ": yok"); continue; }
                string parent = t.parent != null ? t.parent.name : null;
                string want = kv.Value ?? ArtCharacterPrefabBuilder.PlayerArmature;   // Root hangs under the armature object
                if (parent != want) errors.Add(kv.Key + ": ebeveyn " + parent + " != " + want);
            }

            foreach (string name in bones.Keys) if (!expected.ContainsKey(name)) errors.Add(name + ": tabloda yok (fazla)");
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(ArtCharacterContract.HeroV2BoneCount, bones.Count);
            Debug.Log("[DD-TEST] wp082.hero.bones count=" + bones.Count + " nameParentMismatch=" + errors.Count);
        }

        [Test]
        public void Eksen_EskiOyuncuIleAyni_Olcek1_Boy182()
        {
            GameObject instance = UnityEngine.Object.Instantiate(Model());
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Dictionary<string, Transform> b = ArtCharacterContract.Bones(instance, ArtCharacterPrefabBuilder.PlayerArmature);
                Transform armature = instance.transform.Find(ArtCharacterPrefabBuilder.PlayerArmature);
                Assert.IsNotNull(armature, "PlayerRig");
                float rootAngle = Quaternion.Angle(Quaternion.identity, armature.localRotation);
                float maxScaleErr = 0f;
                foreach (Transform t in b.Values) maxScaleErr = Mathf.Max(maxScaleErr, (t.lossyScale - Vector3.one).magnitude);
                Assert.Less(maxScaleErr, 1e-3f, "her kemik ölçek 1");
                // hero-t1 / hero-t3 (07.10): the import leaves the Blender root's X rotation on the PlayerRig node (90°) and the bind pose
                // faces −Z with the anatomical left at +X — EXACTLY like the pre-hero AP-022 body (measured on both prefabs: toes −Z, left
                // arm +X, PlayerRig 90° X). The Humanoid avatar normalises the body orientation, so the played character faces the
                // Animator's +Z. The contract is therefore parity with the shipped player model, not the README's raw-FBX "+Z".
                var legacyModel = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterContract.LegacyPlayerModel);
                Assert.IsNotNull(legacyModel);
                Dictionary<string, Transform> lb = ArtCharacterContract.Bones(legacyModel, ArtCharacterPrefabBuilder.PlayerArmature);
                Transform legacyRig = legacyModel.transform.Find(ArtCharacterPrefabBuilder.PlayerArmature);
                float legacyRootAngle = Quaternion.Angle(Quaternion.identity, legacyRig.localRotation);
                Assert.Less(Quaternion.Angle(legacyRig.localRotation, armature.localRotation), 0.5f, "PlayerRig düğüm dönüşü eski modelle aynı");
                float legacyToe = lb["LeftToes"].position.z - lb["LeftFoot"].position.z, legacyArm = lb["LeftUpperArm"].position.x;

                float hips = b["Hips"].position.y;
                Vector3 lFoot = b["LeftFoot"].position, lToe = b["LeftToes"].position, rFoot = b["RightFoot"].position, rToe = b["RightToes"].position;
                Assert.Greater(Mathf.Abs(lToe.z - lFoot.z), 0.05f, "ayak ucu Z ekseninde");
                Assert.AreEqual(Mathf.Sign(legacyToe), Mathf.Sign(lToe.z - lFoot.z), "sol ayak ucu eski modelle aynı yöne bakar");
                Assert.AreEqual(Mathf.Sign(legacyToe), Mathf.Sign(rToe.z - rFoot.z), "sağ ayak ucu eski modelle aynı yöne bakar");
                Assert.AreEqual(Mathf.Sign(legacyArm), Mathf.Sign(b["LeftUpperArm"].position.x), "anatomik sol eski modelle aynı tarafta");
                Assert.Greater(Mathf.Abs(b["LeftUpperArm"].position.x), 0.1f);
                Assert.AreEqual(-Mathf.Sign(b["LeftUpperArm"].position.x), Mathf.Sign(b["RightUpperArm"].position.x), "sağ kol karşı tarafta");
                Assert.AreEqual(0.99f, hips, 0.01f, "Hips 0,99 m (AP-024 README)");

                var smr = instance.transform.Find("Player_Body_LOD0").GetComponent<SkinnedMeshRenderer>();
                var baked = new Mesh();
                try
                {
                    smr.BakeMesh(baked, true);
                    Vector3[] v = baked.vertices;
                    float minY = float.MaxValue, maxY = float.MinValue;
                    Matrix4x4 m = smr.transform.localToWorldMatrix;
                    // BakeMesh(useScale) bakes the renderer's scale; position + rotation only
                    Matrix4x4 tr = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for (int i = 0; i < v.Length; i++) { float y = tr.MultiplyPoint3x4(v[i]).y; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
                    float height = maxY - minY;
                    Debug.Log(string.Format(Inv, "[DD-TEST] wp082.hero.axis height={0:F4} minY={1:F4} maxY={2:F4} hips={3:F4} toeFwdL={4:F3} toeFwdR={5:F3} leftArmX={6:F3} rootAngle={7:F3} legacyRootAngle={10:F3} legacyToeFwdL={11:F3} legacyLeftArmX={12:F3} maxScaleErr={8:E2} rendererScale={9}",
                        height, minY, maxY, hips, lToe.z - lFoot.z, rToe.z - rFoot.z, b["LeftUpperArm"].position.x, rootAngle, maxScaleErr, m.lossyScale.ToString("F4"), legacyRootAngle, legacyToe, legacyArm));
                    Assert.AreEqual(1.82f, height, 0.01f, "boy 1,82 ± 0,01 m");
                    Assert.AreEqual(0f, minY, 0.02f, "ayak tabanı zeminde");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Lod4_UcgenSayilari_Mesafeler_TdMesafesindeLod0()
        {
            PlayerSkinnedVisual visual = Visual();
            Assert.AreEqual(4, visual.Renderers.Length, "4 LOD gövde");
            var tris = new int[4];
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual("Player_Body_LOD" + i, visual.Renderers[i].name);
                tris[i] = (int)(visual.Renderers[i].sharedMesh.GetIndexCount(0) / 3);
                Assert.AreEqual(1, visual.Renderers[i].sharedMesh.subMeshCount, "tek materyal");
            }

            CollectionAssert.AreEqual(HeroLodTriangles, tris, "AP-024 LOD üçgenleri (Blender ölçümü)");
            LOD[] lods = visual.LodGroup.GetLODs();
            Assert.AreEqual(4, lods.Length);
            CollectionAssert.AreEqual(new[] { 32f, 52f, 90f, 138f }, ArtCharacterPrefabBuilder.PlayerIntendedDistances);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(1, lods[i].renderers.Length);
                Assert.AreSame(visual.Renderers[i], lods[i].renderers[0], "LOD" + i + " = Player_Body_LOD" + i);
            }

            // [hesap] Unity's screen-relative height at the TD distance: size / (2 · d · tan(fov / 2)) × lodBias (shipped 2).
            float size = visual.LodGroup.size;
            float rel = size / (2f * TdDistance * Mathf.Tan(TdFov * 0.5f * Mathf.Deg2Rad)) * ArtFamilyPrefabBuilder.ShippedLodBias;
            int active = lods.Length;
            for (int i = 0; i < lods.Length; i++) if (rel >= lods[i].screenRelativeTransitionHeight) { active = i; break; }
            Debug.Log(string.Format(Inv, "[DD-TEST] wp082.hero.lod tris={0}/{1}/{2}/{3} t={4:F4}/{5:F4}/{6:F4}/{7:F4} size={8:F3} tdRel={9:F4} tdActiveLod={10} (hesap: d={11} fov={12} lodBias={13})",
                tris[0], tris[1], tris[2], tris[3], lods[0].screenRelativeTransitionHeight, lods[1].screenRelativeTransitionHeight, lods[2].screenRelativeTransitionHeight,
                lods[3].screenRelativeTransitionHeight, size, rel, active, TdDistance, TdFov, ArtFamilyPrefabBuilder.ShippedLodBias));
            Assert.AreEqual(0, active, "TD varsayılan mesafesinde LOD0 beklenir (plan A3, hesap)");
        }

        [Test]
        public void Lod1_3_Parcalar_Generic_KemikleriLod0Iskeletinde_BindposeFarkiSifir()
        {
            PlayerSkinnedVisual visual = Visual();
            SkinnedMeshRenderer lod0 = visual.Renderers[0];
            Transform armature = visual.Animator.transform.Find(ArtCharacterPrefabBuilder.PlayerArmature);
            Assert.IsNotNull(armature);
            var bind0 = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            var bone0 = new Dictionary<string, Transform>(StringComparer.Ordinal);
            Matrix4x4[] bp0 = lod0.sharedMesh.bindposes;
            for (int j = 0; j < lod0.bones.Length; j++) { bind0[lod0.bones[j].name] = bp0[j]; bone0[lod0.bones[j].name] = lod0.bones[j]; }
            var rows = new List<string>();
            for (int lod = 1; lod < ArtCharacterContract.HeroV2LodCount; lod++)
            {
                string path = ArtCharacterContract.HeroV2LodFile(lod);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                Assert.AreEqual(ModelImporterAnimationType.Generic, importer.animationType, path);
                Assert.AreEqual(ModelImporterAvatarSetup.NoAvatar, importer.avatarSetup, path);
                Assert.IsNull(ArtCharacterContract.AvatarOf(path), path + ": avatar yok");
                Assert.IsEmpty(ArtCharacterContract.Validate(importer), path);
                SkinnedMeshRenderer src = null;
                foreach (SkinnedMeshRenderer r in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == "Player_Body_LOD" + lod) src = r;
                Assert.IsNotNull(src, path + " Player_Body_LOD" + lod);

                SkinnedMeshRenderer dst = visual.Renderers[lod];
                Assert.AreSame(src.sharedMesh, dst.sharedMesh, "LOD" + lod + " mesh parçadan");
                Assert.AreEqual(lod0.transform.localPosition, dst.transform.localPosition, "LOD" + lod + " renderer yeri LOD0 ile aynı");
                Assert.Less(Quaternion.Angle(lod0.transform.localRotation, dst.transform.localRotation), 0.01f);
                Assert.AreEqual(src.bones.Length, dst.bones.Length);
                Matrix4x4[] bp = dst.sharedMesh.bindposes;
                float maxDiff = 0f;
                int shared = 0;
                for (int j = 0; j < dst.bones.Length; j++)
                {
                    Assert.IsNotNull(dst.bones[j], "LOD" + lod + " kemik #" + j);
                    Assert.AreEqual(src.bones[j].name, dst.bones[j].name, "LOD" + lod + " kemik sırası ad ad");
                    Assert.IsTrue(dst.bones[j].IsChildOf(armature), dst.bones[j].name + " LOD0 iskeletinde");
                    if (!bind0.TryGetValue(dst.bones[j].name, out Matrix4x4 b0)) continue;
                    Assert.AreSame(bone0[dst.bones[j].name], dst.bones[j], dst.bones[j].name + ": LOD0 ile aynı kemik");
                    shared++;
                    for (int e = 0; e < 16; e++) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(bp[j][e] - b0[e]));
                }

                Assert.AreEqual(dst.rootBone != null ? dst.rootBone.name : null, src.rootBone != null ? src.rootBone.name : null, "rootBone");
                Assert.Greater(shared, 50, "LOD" + lod + ": LOD0 ile ortak kemik");
                Assert.Less(maxDiff, 1e-4f, "LOD" + lod + " bindpose farkı ≈ 0");
                rows.Add(string.Format(Inv, "LOD{0} bones={1} shared={2} maxBindDiff={3:E2}", lod, dst.bones.Length, shared, maxDiff));
            }

            Debug.Log("[DD-TEST] wp082.hero.rebind " + string.Join(" | ", rows));
        }

        [Test]
        public void Materyal_KahramanHaritalari_AyniLitShader_DokuIceAlimi()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(ArtCharacterPrefabBuilder.PlayerMaterial);
            Assert.IsNotNull(m);
            // WP-111 R4: the hero rides Tier Lit (a URP 14 Lit copy, DEC-077's lighting) for its _HERO_READ variant
            Assert.AreEqual(ArtTierBuilder.ShaderName, m.shader.name, "proje karakter Tier Lit shader'ı (DEC-077 ışığı, _HERO_READ)");
            Assert.AreEqual(ArtCharacterPrefabBuilder.HeroV2BaseColor, AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")));
            Assert.AreEqual(ArtCharacterPrefabBuilder.HeroV2Normal, AssetDatabase.GetAssetPath(m.GetTexture("_BumpMap")));
            Assert.AreEqual(ArtCharacterPrefabBuilder.HeroV2MetallicSmoothness, AssetDatabase.GetAssetPath(m.GetTexture("_MetallicGlossMap")));
            Assert.AreEqual(ArtCharacterPrefabBuilder.HeroV2MetallicSmoothness, AssetDatabase.GetAssetPath(m.GetTexture("_OcclusionMap")), "AO = türetilmiş haritanın G'si (ORM R)");
            Assert.IsTrue(m.IsKeywordEnabled("_NORMALMAP") && m.IsKeywordEnabled("_METALLICSPECGLOSSMAP") && m.IsKeywordEnabled("_OCCLUSIONMAP"));
            // WP-111 R4 (AD-R3READ SHOULD-1 / N1): the darks-only lift + rim replace R3's flat 1.3 gain — _BaseColor back to white
            Assert.AreEqual(Color.white, m.GetColor("_BaseColor"), "_BaseColor beyaz (düz kazanç kalktı)");
            Assert.IsTrue(m.IsKeywordEnabled("_HERO_READ"), "_HERO_READ açık");
            Assert.IsFalse(m.IsKeywordEnabled("_TIER_TINT") || m.IsKeywordEnabled("_ZOMBIE_MASKS"), "zombi katmanları kapalı");
            Vector4 lift = m.GetVector("_HeroLift"), rim = m.GetVector("_HeroRim");
            Assert.AreEqual(new Vector4(ArtCharacterPrefabBuilder.HeroDarkGain, ArtCharacterPrefabBuilder.HeroDarkFadeStart, ArtCharacterPrefabBuilder.HeroDarkFadeEnd, 0f), lift, "koyu kaldırma");
            Assert.AreEqual(ArtCharacterPrefabBuilder.HeroRim, rim, "kenar ışığı");
            Assert.Less(lift.y, lift.z, "solma aralığı");
            foreach (SkinnedMeshRenderer r in Visual().Renderers) Assert.AreSame(m, r.sharedMaterial, r.name);

            var baseColor = (TextureImporter)AssetImporter.GetAtPath(ArtCharacterPrefabBuilder.HeroV2BaseColor);
            var normal = (TextureImporter)AssetImporter.GetAtPath(ArtCharacterPrefabBuilder.HeroV2Normal);
            var orm = (TextureImporter)AssetImporter.GetAtPath(ArtCharacterPrefabBuilder.HeroV2Orm);
            var ms = (TextureImporter)AssetImporter.GetAtPath(ArtCharacterPrefabBuilder.HeroV2MetallicSmoothness);
            Assert.IsTrue(baseColor.sRGBTexture, "BaseColor sRGB");
            foreach (string skin in new[] { ArtCharacterPrefabBuilder.HeroV2Skin2BaseColor, ArtCharacterPrefabBuilder.HeroV2Skin3BaseColor })
            {
                var si = (TextureImporter)AssetImporter.GetAtPath(skin);
                Assert.IsNotNull(si, skin);
                Assert.IsTrue(si.sRGBTexture, skin + " sRGB");
                Assert.AreEqual(baseColor.maxTextureSize, si.maxTextureSize, skin + " deri 1 ile aynı boyut");
            }
            Assert.AreEqual(TextureImporterType.NormalMap, normal.textureType, "Normal Map");
            Assert.IsFalse(orm.sRGBTexture, "ORM doğrusal"); Assert.IsFalse(ms.sRGBTexture, "MS doğrusal");
            Assert.AreEqual(TextureImporterAlphaSource.FromInput, ms.alphaSource, "smoothness alfada");

            // the repack: out.R = ORM.B (metallic), out.G = ORM.R (AO), out.A = 255 − ORM.G (smoothness) — probe pixels of the two PNGs
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var dst = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(src, File.ReadAllBytes(ArtCharacterPrefabBuilder.HeroV2Orm), false));
                Assert.IsTrue(ImageConversion.LoadImage(dst, File.ReadAllBytes(ArtCharacterPrefabBuilder.HeroV2MetallicSmoothness), false));
                Assert.AreEqual(src.width, dst.width); Assert.AreEqual(src.height, dst.height);
                Color32[] a = src.GetPixels32(), c = dst.GetPixels32();
                int bad = 0;
                for (int i = 0; i < a.Length; i += 997)
                    if (c[i].r != a[i].b || c[i].g != a[i].r || c[i].a != (byte)(255 - a[i].g)) bad++;
                Assert.AreEqual(0, bad, "ORM → MetallicSmoothness kanal eşlemesi");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(src);
                UnityEngine.Object.DestroyImmediate(dst);
            }
        }

        [Test]
        public void Soketler_EbeveynKemikleri_PrefabSilahSoketi()
        {
            Dictionary<string, Transform> bones = ArtCharacterContract.Bones(Model(), ArtCharacterPrefabBuilder.PlayerArmature);
            var rows = new List<string>();
            for (int i = 0; i < ArtCharacterContract.HeroV2SocketParents.GetLength(0); i++)
            {
                string socket = ArtCharacterContract.HeroV2SocketParents[i, 0], parent = ArtCharacterContract.HeroV2SocketParents[i, 1];
                Assert.IsTrue(bones.TryGetValue(socket, out Transform t), "soket yok: " + socket);
                Assert.AreEqual(parent, t.parent.name, socket + " ebeveyni");
                rows.Add(socket + "→" + parent);
            }

            PlayerSkinnedVisual visual = Visual();
            Assert.AreEqual(PlayerSkinnedVisual.WeaponSocketName, visual.WeaponSocket.name);
            Assert.AreEqual("RightHand", visual.WeaponSocket.parent.name);
            Assert.AreEqual("UpperChest", visual.BackSocket.parent.name, "socket_back → UpperChest");
            Assert.AreSame(visual.Animator.GetBoneTransform(HumanBodyBones.RightHand), visual.WeaponSocket.parent, "silah soketi Humanoid sağ elde");
            var chains = visual.Animator.GetComponent<SecondaryChainDriver>();
            Assert.IsNotNull(chains, "ikincil zincir sürücüsü");
            Assert.AreEqual(1, chains.Chains.Length, "yalnız ağırlıklı sash zinciri");
            Assert.AreEqual("sash", chains.Chains[0].name);
            Assert.AreEqual(3, chains.Chains[0].bones.Length);
            var twist = visual.Animator.GetComponent<TwistCorrector>();
            Assert.IsNotNull(twist); Assert.AreEqual(8, twist.LinkCount, "8 twist kemiği bağlı");
            Debug.Log("[DD-TEST] wp082.hero.sockets " + string.Join(" ", rows) + " chains=" + chains.Chains.Length + " twist=" + twist.LinkCount);
        }

        [Test]
        public void SagOmuz_KasDegeri_Kayda()
        {
            int muscle = Array.IndexOf(HumanTrait.MuscleName, "Right Shoulder Down-Up");
            Assert.GreaterOrEqual(muscle, 0, "Right Shoulder Down-Up kası");
            float hero = RestMuscle(ArtCharacterContract.PlayerAvatarSource, muscle, out float heroL);
            float legacy = RestMuscle(ArtCharacterContract.LegacyPlayerModel, muscle, out float legacyL);
            int leftMuscle = Array.IndexOf(HumanTrait.MuscleName, "Left Shoulder Down-Up");
            Debug.Log(string.Format(Inv, "[DD-TEST] wp082.hero.rightShoulderDownUp rest hero={0:F4} legacy={1:F4} (left: hero={2:F4} legacy={3:F4}) defaultRange=[{4:F1},{5:F1}] deg useDefaultValues={6}",
                hero, legacy, heroL, legacyL, HumanTrait.GetMuscleDefaultMin(muscle), HumanTrait.GetMuscleDefaultMax(muscle), RightShoulderUsesDefaults()));
            Assert.IsFalse(float.IsNaN(hero) || float.IsInfinity(hero), "kas değeri okunmalı");
            Assert.IsFalse(float.IsNaN(legacy) || float.IsInfinity(legacy));
            Assert.GreaterOrEqual(leftMuscle, 0);
        }

        private static bool RightShoulderUsesDefaults()
        {
            foreach (HumanBone hb in ArtCharacterContract.AvatarOf(ArtCharacterContract.PlayerAvatarSource).humanDescription.human)
                if (hb.humanName == "RightShoulder") return hb.limit.useDefaultValues;
            return false;
        }

        /// <summary>The muscle value of the model's bind / rest pose through its own avatar (HumanPoseHandler on an instance).</summary>
        private static float RestMuscle(string modelPath, int muscle, out float leftShoulder)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Assert.IsNotNull(model, modelPath);
            GameObject instance = UnityEngine.Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Avatar avatar = ArtCharacterContract.AvatarOf(modelPath);
                Assert.IsTrue(avatar != null && avatar.isValid && avatar.isHuman, modelPath + " avatar");
                using (var handler = new HumanPoseHandler(avatar, instance.transform))
                {
                    var pose = new HumanPose();
                    handler.GetHumanPose(ref pose);
                    leftShoulder = pose.muscles[Array.IndexOf(HumanTrait.MuscleName, "Left Shoulder Down-Up")];
                    return pose.muscles[muscle];
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FpModeli_EskiKaynak_FpKolMeshi_BitEs()
        {
            PlayerSkinnedVisual visual = Visual();
            var so = new SerializedObject(visual);
            UnityEngine.Object fpModel = so.FindProperty("_fpModel").objectReferenceValue;
            UnityEngine.Object fpMesh = so.FindProperty("_fpArmsMesh").objectReferenceValue;
            Assert.AreEqual(ArtCharacterContract.LegacyPlayerModel, AssetDatabase.GetAssetPath(fpModel), "FP viewmodel eski modeli kopyalar (FP kapsam dışı, değişmez)");
            Assert.AreEqual(ArtCharacterPrefabBuilder.PlayerFpArmsMesh, AssetDatabase.GetAssetPath(fpMesh));
            Assert.AreEqual(ArtCharacterPrefabBuilder.PlayerFpArmsMaterial, AssetDatabase.GetAssetPath(so.FindProperty("_fpArmsMaterial").objectReferenceValue));
            string sha;
            using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(ArtCharacterPrefabBuilder.PlayerFpArmsMesh))).Replace("-", "").ToLowerInvariant();
            Debug.Log("[DD-TEST] wp082.hero.fp model=" + AssetDatabase.GetAssetPath(fpModel) + " armsSha=" + sha);
            Assert.AreEqual(FpArmsAssetSha256, sha, "Player_FPArms.asset kahraman alımından önceki hâliyle bit-eş");
            Avatar legacy = ArtCharacterContract.AvatarOf(ArtCharacterContract.LegacyPlayerModel);
            Assert.IsTrue(legacy != null && legacy.isValid && legacy.isHuman, "eski modelin avatarı (FP Animator) geçerli");
        }
    }
}
