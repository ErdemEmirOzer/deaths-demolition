using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Prototype.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-016 · AP-006 player + AP-007 zombie → Unity (ADR-016-1 skinned contract,
    /// ADR-016-2 zombie look contract). Every step is idempotent (assets found by path,
    /// GUIDs stable) and the whole pass runs from the batch menu
    /// (<c>ArtPipelineMenu.RunCharacters</c>):
    ///
    ///   1. avatars      re-import any character FBX that is off the v1.1 contract or lacks a
    ///                   valid humanoid avatar (each file builds its own from the shared template)
    ///   2. textures     derived maps: player / zombie ORM → MetallicSmoothness(+AO in G),
    ///                   AP-007 ID mask → DetailMask (A = cloth)
    ///   3. materials    M_player (Lit, skin-1 albedo; skins swap the albedo at runtime),
    ///                   M_zombie_atlas + three version variants (cloth-masked detail tint)
    ///   4. animation    CharacterAnimationSet_Player / _Zombie (rig-test idle + walk) and the
    ///                   generated 1-D locomotion controllers
    ///   5. prefabs      Player_AP006 (LODGroup 3, sockets, skinned visual + bridge),
    ///                   Zombie_AP007 (LODGroup 4 × 4 slots, anchors, skinned visual + bridge)
    ///   6. library      ZombieModuleLibrary (3 bodies × 4 LOD, 19 modules × cw × fit × 4 LOD,
    ///                   bone-name orders, tier materials, tint sub-textures)
    ///   7. asset set    Resources/DD_Characters (source Skinned) — what the prototype reads
    /// </summary>
    public static class ArtCharacterPrefabBuilder
    {
        private const string LogPrefix = "[ArtCharacters] ";

        public const string PlayerFolder = ArtCharacterContract.PlayerFolder;
        public const string ZombieFolder = ArtCharacterContract.ZombieFolder;
        public const string PlayerTextures = PlayerFolder + "/Textures";
        public const string ZombieTextures = ZombieFolder + "/Textures";
        public const string PlayerDerived = PlayerTextures + "/Derived";
        public const string ZombieDerived = ZombieTextures + "/Derived";
        public const string PlayerMaterial = PlayerFolder + "/M_player.mat";
        public const string ZombieMaterial = ZombieFolder + "/M_zombie_atlas.mat";
        /// <summary>WP-020 · AP-007 v002 second (cloth) atlas material; the cloth Mask is a constant (G = 1) so its DetailMask is a 4×4 sub-texture, not a 2K map.</summary>
        public const string ZombieClothMaterial = ZombieFolder + "/M_zombie_cloth.mat";
        public const string ZombieClothBaseColor = ZombieTextures + "/T_zombie_cloth_BaseColor.png";
        public static readonly string[] TierSuffix = { "", "_Special", "_Elite", "_Ultra" };
        public const string PlayerAnimationSet = PlayerFolder + "/CharacterAnimationSet_Player.asset";
        public const string ZombieAnimationSet = ZombieFolder + "/CharacterAnimationSet_Zombie.asset";
        public const string PlayerController = PlayerFolder + "/DD_Locomotion_Player.controller";
        public const string ZombieController = ZombieFolder + "/DD_Locomotion_Zombie.controller";
        public const string PlayerPrefab = PlayerFolder + "/Player_AP006.prefab";
        public const string ZombiePrefab = ZombieFolder + "/Zombie_AP007.prefab";
        public const string ZombieLibrary = ZombieFolder + "/ZombieModuleLibrary.asset";
        public const string ModulesJson = ZombieFolder + "/AP007-modules.json";
        public const string CharacterAssets = ArtPipelinePaths.ArtRoot + "/Resources/" + CharacterAssetSet.ResourceName + ".asset";
        public const string PlayerModel = ArtCharacterContract.PlayerAvatarSource;
        public static readonly string[] ZombieBodyModels = { ZombieFolder + "/zombie_body_A.fbx", ZombieFolder + "/zombie_body_B.fbx", ZombieFolder + "/zombie_body_C.fbx" };
        public const string PlayerIdleClipFile = PlayerFolder + "/Clips/player_rigtest_idle.fbx";
        public const string PlayerWalkClipFile = PlayerFolder + "/Clips/player_rigtest_walk.fbx";
        public const string ZombieIdleClipFile = ZombieFolder + "/Clips/zombie_rigtest_idle.fbx";
        public const string ZombieWalkClipFile = ZombieFolder + "/Clips/zombie_rigtest_walk.fbx";
        public const string PlayerArmature = "PlayerRig";
        public const string ZombieArmature = "ZombieRig";

        /// <summary>Intended LOD distances (metres, lodBias 2 → authored = intended / 2; ADR-012-1 logic): player LOD0→1→2→cull, zombie LOD0→1→2→3→cull.</summary>
        public static readonly float[] PlayerIntendedDistances = { 32f, 52f, 138f };
        public static readonly float[] ZombieIntendedDistances = { 32f, 52f, 90f, 138f };

        /// <summary>
        /// PROTOTYPE-ONLY version tints (URP detail albedo, ×2 convention: 0.5 = neutral), reaching
        /// cloth only through the AP-007 mask. Parity with the M1 placeholder cues (Special = red
        /// sash family, Elite = cooler/darker, Ultra = darker, desaturated); AP-014 re-authors.
        /// </summary>
        public static readonly Color[] TierDetailTint =
        {
            new Color(0.5f, 0.5f, 0.5f, 1f),
            new Color(0.60f, 0.42f, 0.42f, 1f),
            new Color(0.42f, 0.45f, 0.55f, 1f),
            new Color(0.36f, 0.36f, 0.36f, 1f)
        };

        // =================================================================== pipeline

        public static void BuildAll()
        {
            EnsureAvatars();
            BuildTextures();
            BuildMaterials();
            BuildAnimation();
            BuildPlayerPrefab();
            ZombieModuleLibrary library = BuildZombieLibrary();
            BuildZombiePrefab(library);
            BuildAssetSet();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ==================================================================== 1 avatars

        /// <summary>Re-imports every character FBX whose importer is not yet on the v1.1 contract or has no valid humanoid avatar.</summary>
        public static int EnsureAvatars()
        {
            int reimported = 0;
            // Sources first, then dependents.
            foreach (string source in new[] { ArtCharacterContract.PlayerAvatarSource, ArtCharacterContract.ZombieAvatarSource })
            {
                var importer = AssetImporter.GetAtPath(source) as ModelImporter;
                if (importer == null) throw new FileNotFoundException(LogPrefix + "avatar source missing", source);
                if (ArtCharacterContract.Validate(importer).Count > 0 || ArtCharacterContract.AvatarOf(source) == null)
                {
                    AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceUpdate);
                    reimported++;
                }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtImportContract.CharactersRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ArtCharacterContract.IsCharacterModel(path) || ArtCharacterContract.IsAvatarSource(path)) continue;
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                if (ArtCharacterContract.Validate(importer).Count > 0)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    reimported++;
                }
            }

            Debug.Log(LogPrefix + "avatars: " + reimported + " character FBX re-imported onto v1.1");
            return reimported;
        }

        // =================================================================== 2 textures

        public static void BuildTextures()
        {
            ArtTextureRepack.BuildDerived(PlayerTextures + "/T_player_Packed.png", PlayerDerived + "/T_player_MetallicSmoothness.png",
                PlayerDerived + "/T_player_MetallicSmoothness.provenance.json", ArtTextureRepack.OrmToMetallicSmoothnessAo, ArtTextureRepack.OrmAlgorithm);
            ArtTextureRepack.BuildDerived(ZombieTextures + "/T_zombie_atlas_Packed.png", ZombieDerived + "/T_zombie_atlas_MetallicSmoothness.png",
                ZombieDerived + "/T_zombie_atlas_MetallicSmoothness.provenance.json", ArtTextureRepack.OrmToMetallicSmoothnessAo, ArtTextureRepack.OrmAlgorithm);
            ArtTextureRepack.BuildDerived(ZombieTextures + "/T_zombie_atlas_Mask.png", ZombieDerived + "/T_zombie_atlas_DetailMask.png",
                ZombieDerived + "/T_zombie_atlas_DetailMask.provenance.json", ArtTextureRepack.IdMaskToDetailMask, ArtTextureRepack.DetailMaskAlgorithm);
            if (HasClothAtlas)
            {
                ArtTextureRepack.BuildDerived(ZombieTextures + "/T_zombie_cloth_Packed.png", ZombieDerived + "/T_zombie_cloth_MetallicSmoothness.png",
                    ZombieDerived + "/T_zombie_cloth_MetallicSmoothness.provenance.json", ArtTextureRepack.OrmToMetallicSmoothnessAo, ArtTextureRepack.OrmAlgorithm);
            }
        }

        /// <summary>WP-020 · True once the AP-007 v002 cloth atlas is staged (the v001 world keeps one atlas).</summary>
        public static bool HasClothAtlas => File.Exists(ZombieClothBaseColor);

        // ================================================================== 3 materials

        public static void BuildMaterials()
        {
            Shader lit = ArtMaterialBuilder.ResolveLitShader();
            Material player = LoadOrCreateMaterial(PlayerMaterial, lit, "M_player");
            ArtMaterialBuilder.ApplyCommonPublic(player);
            ApplyCharacterMaps(player,
                Load<Texture2D>(PlayerTextures + "/T_player_skin1_BaseColor.png"),
                Load<Texture2D>(PlayerTextures + "/T_player_Normal.png"),
                Load<Texture2D>(PlayerDerived + "/T_player_MetallicSmoothness.png"));
            EditorUtility.SetDirty(player);

            Texture2D atlas = Load<Texture2D>(ZombieTextures + "/T_zombie_atlas_BaseColor.png");
            Texture2D atlasNormal = Load<Texture2D>(ZombieTextures + "/T_zombie_atlas_Normal.png");
            Texture2D atlasMask = Load<Texture2D>(ZombieDerived + "/T_zombie_atlas_MetallicSmoothness.png");
            Texture2D detailMask = Load<Texture2D>(ZombieDerived + "/T_zombie_atlas_DetailMask.png");
            for (int tier = 0; tier < TierSuffix.Length; tier++)
            {
                string path = TierMaterialPath(tier);
                Material m = LoadOrCreateMaterial(path, lit, Path.GetFileNameWithoutExtension(path));
                ArtMaterialBuilder.ApplyCommonPublic(m);
                ApplyCharacterMaps(m, atlas, atlasNormal, atlasMask);
                if (tier > 0)
                {
                    m.SetTexture("_DetailMask", detailMask);
                    m.SetFloat("_DetailAlbedoMapScale", 1f);
                    m.SetFloat("_DetailNormalMapScale", 0f);
                    m.EnableKeyword("_DETAIL_MULX2");
                }
                else
                {
                    m.SetTexture("_DetailMask", null);
                    m.SetTexture("_DetailAlbedoMap", null);
                    m.DisableKeyword("_DETAIL_MULX2");
                }

                EditorUtility.SetDirty(m);
            }

            // WP-020 · AP-007 v002 cloth atlas: same Lit block; every texel is version-tintable (mask constant G = 1 → DetailMask A = 1, a 4×4 sub-texture).
            if (HasClothAtlas)
            {
                Texture2D cloth = Load<Texture2D>(ZombieClothBaseColor);
                Texture2D clothNormal = Load<Texture2D>(ZombieTextures + "/T_zombie_cloth_Normal.png");
                Texture2D clothMask = Load<Texture2D>(ZombieDerived + "/T_zombie_cloth_MetallicSmoothness.png");
                for (int tier = 0; tier < TierSuffix.Length; tier++)
                {
                    string path = ClothMaterialPath(tier);
                    Material m = LoadOrCreateMaterial(path, lit, Path.GetFileNameWithoutExtension(path));
                    ArtMaterialBuilder.ApplyCommonPublic(m);
                    ApplyCharacterMaps(m, cloth, clothNormal, clothMask);
                    if (tier > 0)
                    {
                        m.SetFloat("_DetailAlbedoMapScale", 1f);
                        m.SetFloat("_DetailNormalMapScale", 0f);
                        m.EnableKeyword("_DETAIL_MULX2");   // _DetailMask / _DetailAlbedoMap bound by BuildZombieLibrary (sub-textures inside the library asset)
                    }
                    else
                    {
                        m.SetTexture("_DetailMask", null);
                        m.SetTexture("_DetailAlbedoMap", null);
                        m.DisableKeyword("_DETAIL_MULX2");
                    }

                    EditorUtility.SetDirty(m);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "materials: M_player + M_zombie_atlas ×4 (version detail tints through Mask.G)" + (HasClothAtlas ? " + M_zombie_cloth ×4 (whole cloth atlas tintable)" : ""));
        }

        public static string TierMaterialPath(int tier)
        {
            return ZombieFolder + "/M_zombie_atlas" + TierSuffix[Mathf.Clamp(tier, 0, TierSuffix.Length - 1)] + ".mat";
        }

        public static string ClothMaterialPath(int tier)
        {
            return ZombieFolder + "/M_zombie_cloth" + TierSuffix[Mathf.Clamp(tier, 0, TierSuffix.Length - 1)] + ".mat";
        }

        public static void ApplyCharacterMapsPublic(Material m, Texture2D baseMap, Texture2D normal, Texture2D mask) => ApplyCharacterMaps(m, baseMap, normal, mask);
        public static Material LoadOrCreateMaterialPublic(string path, Shader shader, string name) => LoadOrCreateMaterial(path, shader, name);

        private static void ApplyCharacterMaps(Material m, Texture2D baseMap, Texture2D normal, Texture2D mask)
        {
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            // Occlusion rides the same mask (G) — URP reads _OcclusionMap.g.
            m.SetTexture("_OcclusionMap", mask);
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_OCCLUSIONMAP");
            // Characters are skinned: GPU instancing is meaningless for them and SRP batching wants it off.
            m.enableInstancing = false;
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader, string name)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            return m;
        }

        // ================================================================== 4 animation

        public static void BuildAnimation()
        {
            // WP-018: with the AP-009 set staged the generated controllers replace the rig-test blend
            // (ArtAnimatorBuilder); the rig-test path stays for a project without the clips.
            if (ArtAnimatorBuilder.HasAp009())
            {
                ArtAnimatorBuilder.BuildAll(PlayerLocomotionReferenceSpeed());
                return;
            }

            BuildAnimationSet(PlayerAnimationSet, PlayerController, PlayerIdleClipFile, PlayerWalkClipFile, "AP-006 rig tests (RigTest_Idle / RigTest_Walk); AP-009 replaces");
            BuildAnimationSet(ZombieAnimationSet, ZombieController, ZombieIdleClipFile, ZombieWalkClipFile, "AP-007 rig tests (AP-006 RigTest_Idle / RigTest_Walk on ZombieRig); AP-009 replaces");
        }

        /// <summary>The config player MoveSpeed the 2-D locomotion blend is normalised to (the shipped default when no config asset is found).</summary>
        public static float PlayerLocomotionReferenceSpeed()
        {
            var config = DD.Prototype.Core.Config.PrototypeConfig.Active;
            return config != null && config.Player != null && config.Player.MoveSpeed > 0.5f ? config.Player.MoveSpeed : new DD.Prototype.Core.Config.PlayerConfigSection().MoveSpeed;
        }

        private static CharacterAnimationSet BuildAnimationSet(string setPath, string controllerPath, string idleFile, string walkFile, string source)
        {
            AnimationClip idle = ClipOf(idleFile);
            AnimationClip walk = ClipOf(walkFile);
            AnimatorController controller = BuildController(controllerPath, idle, walk);

            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(setPath);
            bool created = set == null;
            if (created)
            {
                set = ScriptableObject.CreateInstance<CharacterAnimationSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }

            var so = new SerializedObject(set);
            so.FindProperty("_idle").objectReferenceValue = idle;
            so.FindProperty("_walk").objectReferenceValue = walk;
            so.FindProperty("_walkSpeedMetersPerSecond").floatValue = 1.4f;
            so.FindProperty("_controller").objectReferenceValue = controller;
            so.FindProperty("_source").stringValue = source;
            so.FindProperty("_clips").ClearArray();
            so.FindProperty("_manifestSchema").stringValue = "";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            Debug.Log(LogPrefix + "animation set " + (created ? "created" : "updated") + ": " + setPath + " (idle " + idle.name + " " + idle.length.ToString("F2") + " s, walk " + walk.name + " " + walk.length.ToString("F2") + " s)");
            return set;
        }

        /// <summary>The first AnimationClip sub-asset of a Clips/ FBX (the take), excluding preview clips.</summary>
        public static AnimationClip ClipOf(string fbxPath)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
            {
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return clip;
            }

            throw new InvalidOperationException(LogPrefix + "no AnimationClip in " + fbxPath + " (is it under Clips/ and imported on v1.1?)");
        }

        /// <summary>One Locomotion state = 1-D blend tree on Speed (idle at 0, walk at 1). Rebuilt in place, GUID stable.</summary>
        public static AnimatorController BuildController(string path, AnimationClip idle, AnimationClip walk)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            }

            bool hasParam = false;
            foreach (AnimatorControllerParameter p in controller.parameters)
            {
                if (p.name == CharacterAnimationSet.SpeedParameter) hasParam = true;
            }

            if (!hasParam)
            {
                controller.AddParameter(CharacterAnimationSet.SpeedParameter, AnimatorControllerParameterType.Float);
            }

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState locomotion = null;
            foreach (ChildAnimatorState child in sm.states)
            {
                if (child.state.name == CharacterAnimationSet.LocomotionState) locomotion = child.state;
            }

            if (locomotion == null)
            {
                locomotion = sm.AddState(CharacterAnimationSet.LocomotionState);
            }

            sm.defaultState = locomotion;
            var tree = locomotion.motion as BlendTree;
            if (tree == null)
            {
                tree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.Simple1D, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, controller);
                locomotion.motion = tree;
            }

            tree.blendParameter = CharacterAnimationSet.SpeedParameter;
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                new ChildMotion { motion = idle, threshold = 0f, timeScale = 1f },
                new ChildMotion { motion = walk, threshold = 1f, timeScale = 1f }
            };
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ============================================================== 5 player prefab

        public static GameObject BuildPlayerPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModel);
            if (model == null) throw new FileNotFoundException(LogPrefix + "player model missing", PlayerModel);
            var animationSet = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(PlayerAnimationSet);
            var material = AssetDatabase.LoadAssetAtPath<Material>(PlayerMaterial);
            if (animationSet == null || material == null) throw new InvalidOperationException(LogPrefix + "build animation + materials first");

            var root = new GameObject(PlayerSkinnedVisual.PrefabName);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                Animator animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = ArtCharacterContract.AvatarOf(PlayerModel);
                animator.runtimeAnimatorController = animationSet.Controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                // WP-018 (ADR-018-1/2): the event receiver and the aim layer live with the Animator.
                CharacterAnimationEvents events = instance.GetComponent<CharacterAnimationEvents>() ?? instance.AddComponent<CharacterAnimationEvents>();
                PlayerAimLayer aimLayer = instance.GetComponent<PlayerAimLayer>() ?? instance.AddComponent<PlayerAimLayer>();
                // WP-032: head look-at along the aim + foot IK (ADR-032-1).
                CharacterLookAt playerLook = instance.GetComponent<CharacterLookAt>() ?? instance.AddComponent<CharacterLookAt>();
                playerLook.Configure(animator, 0.6f, 0.05f);
                FootIkAdapter playerFeet = instance.GetComponent<FootIkAdapter>() ?? instance.AddComponent<FootIkAdapter>();
                playerFeet.Configure(animator);

                var renderers = new SkinnedMeshRenderer[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    Transform t = instance.transform.Find("Player_LOD" + lod);
                    if (t == null) throw new InvalidOperationException(LogPrefix + "player model has no Player_LOD" + lod);
                    var smr = t.GetComponent<SkinnedMeshRenderer>();
                    smr.sharedMaterial = material;
                    ConfigureSkinnedRenderer(smr, lod >= 2);
                    renderers[lod] = smr;
                }

                LODGroup group = instance.GetComponent<LODGroup>() ?? instance.AddComponent<LODGroup>();
                ApplyLods(group, renderers, PlayerIntendedDistances, r => new[] { r });

                Dictionary<string, Transform> bones = ArtCharacterContract.Bones(instance, PlayerArmature);
                bones.TryGetValue(PlayerSkinnedVisual.WeaponSocketName, out Transform weaponSocket);
                bones.TryGetValue(PlayerSkinnedVisual.BackSocketName, out Transform backSocket);
                if (weaponSocket == null) throw new InvalidOperationException(LogPrefix + "player model has no " + PlayerSkinnedVisual.WeaponSocketName);

                var visual = root.AddComponent<PlayerSkinnedVisual>();
                visual.Configure(animator, group, renderers, weaponSocket, backSocket, animationSet, events, aimLayer, playerLook, playerFeet);

                EnsureFolder(Path.GetDirectoryName(PlayerPrefab).Replace('\\', '/'));
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
                Debug.Log(LogPrefix + "player prefab written: " + PlayerPrefab + " (bones " + bones.Count + ", LOD tris " + Tris(renderers) + ")");
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ============================================================== 6 zombie library

        [Serializable]
        private class ModulesDoc
        {
            public string version;
            public ModuleDoc[] modules;
        }

        [Serializable]
        private class ModuleDoc
        {
            public string module;
            public string kind;
            public string fbx;
            public string[] colourways;
            public string[] fits;
            public string object_name_pattern;
        }

        public static ZombieModuleLibrary BuildZombieLibrary()
        {
            if (!File.Exists(ModulesJson)) throw new FileNotFoundException(LogPrefix + "AP007-modules.json missing", ModulesJson);
            byte[] bytes = File.ReadAllBytes(ModulesJson);
            Dictionary<string, object> doc = ArtJson.AsObject(ArtJson.Parse(Encoding.UTF8.GetString(bytes)), "modules");

            var library = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ZombieLibrary);
            bool created = library == null;
            if (created)
            {
                library = ScriptableObject.CreateInstance<ZombieModuleLibrary>();
                AssetDatabase.CreateAsset(library, ZombieLibrary);
            }

            library.schema = ZombieModuleLibrary.Schema;
            library.package = "AP-007";
            library.version = doc.ContainsKey("version") ? ArtJson.AsString(doc["version"]) : ArtJson.AsString(doc["schema"]);   // v002 manifest: schema only
            library.sourceSha256 = ArtTextureRepack.Sha256Hex(bytes);

            // Bodies A/B/C: zombie_body_<fit>_LOD<n>.
            var bodies = new List<ZombieModuleLibrary.Body>();
            foreach (string fit in new[] { "A", "B", "C" })
            {
                string path = ZombieFolder + "/zombie_body_" + fit + ".fbx";
                var body = new ZombieModuleLibrary.Body { fit = fit };
                for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                {
                    body.lods[lod] = MeshRefOf(path, "zombie_body_" + fit + "_LOD" + lod);
                }

                bodies.Add(body);
            }

            library.bodies = bodies.ToArray();

            var modules = new List<ZombieModuleLibrary.Module>();
            foreach (object o in ArtJson.AsList(doc["modules"], "modules"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "module");
                string id = ArtJson.AsString(r["module"]);
                string kind = ArtJson.AsString(r["kind"]);
                var cws = new List<string>();
                foreach (object c in ArtJson.AsList(r["colourways"], "colourways")) cws.Add(ArtJson.AsString(c));
                var fits = new List<string>();
                foreach (object f in ArtJson.AsList(r["fits"], "fits")) fits.Add(ArtJson.AsString(f));
                string fbx = ZombieFolder + "/Modules/" + Path.GetFileName(ArtJson.AsString(r["fbx"]));
                var module = new ZombieModuleLibrary.Module { id = id, kind = kind, colourways = cws.ToArray() };
                // WP-020 · AP-007 v002: materials in FBX sub-mesh order (atlas / cloth); v001 records carry none → atlas only.
                if (r.ContainsKey("materials"))
                {
                    var mats = new List<string>();
                    foreach (object mo in ArtJson.AsList(r["materials"], "materials"))
                    {
                        string name = ArtJson.AsString(mo);
                        if (name != ZombieModuleLibrary.MaterialAtlas && name != ZombieModuleLibrary.MaterialCloth) throw new InvalidOperationException(LogPrefix + id + ": unknown module material '" + name + "'");
                        mats.Add(name);
                    }

                    module.materials = mats.ToArray();
                }
                var variants = new List<ZombieModuleLibrary.Variant>();
                foreach (string cw in cws)
                {
                    foreach (string fit in fits)
                    {
                        var v = new ZombieModuleLibrary.Variant { colourway = cw, fit = fit };
                        for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                        {
                            string meshName = cw == ZombieModuleLibrary.NoColourway
                                ? "zombie_" + id + "_" + fit + "_LOD" + lod
                                : "zombie_" + id + "_" + cw + "_" + fit + "_LOD" + lod;
                            v.lods[lod] = MeshRefOf(fbx, meshName);
                        }

                        variants.Add(v);
                    }
                }

                module.variants = variants.ToArray();
                modules.Add(module);
            }

            library.modules = modules.ToArray();

            // Tier materials + their tint sub-textures (4×4 flat colour, stored inside the library asset).
            library.tierMaterials = new Material[TierSuffix.Length];
            for (int tier = 0; tier < TierSuffix.Length; tier++)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(TierMaterialPath(tier));
                if (m == null) throw new InvalidOperationException(LogPrefix + "build materials first: " + TierMaterialPath(tier));
                library.tierMaterials[tier] = m;
                if (tier > 0)
                {
                    Texture2D tint = TintTexture(library, "T_zombie_tint" + TierSuffix[tier], TierDetailTint[tier]);
                    m.SetTexture("_DetailAlbedoMap", tint);
                    EditorUtility.SetDirty(m);
                }
            }

            // WP-020 · cloth atlas tier materials: the same tint sub-textures, DetailMask = a 4×4 opaque-alpha sub-texture (mask constant G = 1).
            library.tierClothMaterials = new Material[TierSuffix.Length];
            if (HasClothAtlas)
            {
                Texture2D clothDetailMask = TintTexture(library, "T_zombie_cloth_DetailMask", new Color(0f, 1f, 0f, 1f));
                for (int tier = 0; tier < TierSuffix.Length; tier++)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(ClothMaterialPath(tier));
                    if (m == null) throw new InvalidOperationException(LogPrefix + "build materials first: " + ClothMaterialPath(tier));
                    library.tierClothMaterials[tier] = m;
                    if (tier > 0)
                    {
                        m.SetTexture("_DetailMask", clothDetailMask);
                        m.SetTexture("_DetailAlbedoMap", TintTexture(library, "T_zombie_tint" + TierSuffix[tier], TierDetailTint[tier]));
                        EditorUtility.SetDirty(m);
                    }
                }
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            int meshes = 0, twoMaterial = 0;
            foreach (ZombieModuleLibrary.Module m in library.modules) { meshes += m.variants.Length * ZombieModuleLibrary.LodCount; if (m.materials != null && m.materials.Length > 1) twoMaterial++; }
            Debug.Log(LogPrefix + "zombie library " + (created ? "created" : "updated") + ": " + library.bodies.Length + " bodies, " + library.modules.Length + " modules (" + twoMaterial + " two-material), " + meshes + " module meshes (+12 body), sha " + library.sourceSha256.Substring(0, 12) + "…");
            return library;
        }

        private static Texture2D TintTexture(ZombieModuleLibrary library, string name, Color colour)
        {
            return TintTexturePublic(library, ZombieLibrary, name, colour);
        }

        /// <summary>A 4×4 flat-colour sub-texture stored inside <paramref name="owner"/> (the library asset), reused by name across runs.</summary>
        public static Texture2D TintTexturePublic(UnityEngine.Object owner, string ownerPath, string name, Color colour)
        {
            Texture2D existing = null;
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(ownerPath))
            {
                if (o is Texture2D t && t.name == name) existing = t;
            }

            Texture2D tex = existing ?? new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = name, hideFlags = HideFlags.None };
            var px = new Color32[16];
            Color32 c = colour;
            for (int i = 0; i < px.Length; i++) px[i] = c;
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            tex.Apply(false, false);
            if (existing == null) AssetDatabase.AddObjectToAsset(tex, owner);
            EditorUtility.SetDirty(tex);
            return tex;
        }

        /// <summary>Mesh + bone-name order of one skinned object inside an imported character FBX.</summary>
        private static ZombieModuleLibrary.MeshRef MeshRefOf(string fbxPath, string objectName)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new FileNotFoundException(LogPrefix + "character FBX missing", fbxPath);
            Transform t = FindDeep(model.transform, objectName);
            if (t == null) throw new InvalidOperationException(LogPrefix + fbxPath + " has no object '" + objectName + "'");
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null) throw new InvalidOperationException(LogPrefix + objectName + " in " + fbxPath + " is not a skinned mesh");
            var names = new string[smr.bones.Length];
            for (int i = 0; i < names.Length; i++) names[i] = smr.bones[i] != null ? smr.bones[i].name : string.Empty;
            return new ZombieModuleLibrary.MeshRef
            {
                mesh = smr.sharedMesh,
                boneNames = names,
                rootBone = smr.rootBone != null ? smr.rootBone.name : "Hips",
                triangles = (int)(smr.sharedMesh.GetIndexCount(0) / 3)
            };
        }

        // ============================================================== 5b zombie prefab

        public static GameObject BuildZombiePrefab(ZombieModuleLibrary library)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterContract.ZombieAvatarSource);
            if (model == null) throw new FileNotFoundException(LogPrefix + "zombie body A missing", ArtCharacterContract.ZombieAvatarSource);
            var animationSet = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ZombieAnimationSet);
            if (animationSet == null || library == null) throw new InvalidOperationException(LogPrefix + "build animation + library first");

            var root = new GameObject(ZombieSkinnedVisual.PrefabName);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);

                Animator animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = ArtCharacterContract.AvatarOf(ArtCharacterContract.ZombieAvatarSource);
                animator.runtimeAnimatorController = animationSet.Controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // WP-032: bone-mounted hitboxes / attack socket must follow the pose even off screen
                // WP-018 (ADR-018-1/2): the event receiver with the Animator; the far-body step throttle on the root.
                CharacterAnimationEvents events = instance.GetComponent<CharacterAnimationEvents>() ?? instance.AddComponent<CharacterAnimationEvents>();
                // WP-032: threat look-at + foot IK (ADR-032-1), gated by the LOD step component on the root.
                CharacterLookAt zombieLook = instance.GetComponent<CharacterLookAt>() ?? instance.AddComponent<CharacterLookAt>();
                zombieLook.Configure(animator, 0.8f, 0.15f);
                FootIkAdapter zombieFeet = instance.GetComponent<FootIkAdapter>() ?? instance.AddComponent<FootIkAdapter>();
                zombieFeet.Configure(animator);

                // Slots: the imported body renderers become the body slots; top/bottom/head slots are new.
                var slots = new SkinnedMeshRenderer[ZombieModuleLibrary.LodCount * ZombieSkinnedVisual.SlotCount];
                string[] slotNames = { "Body", "Top", "Bottom", "Head" };
                Material standard = library.MaterialFor(0);
                for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                {
                    Transform bodyT = instance.transform.Find("zombie_body_A_LOD" + lod);
                    if (bodyT == null) throw new InvalidOperationException(LogPrefix + "zombie body A has no LOD" + lod);
                    bodyT.name = "LOD" + lod + "_Body";
                    var body = bodyT.GetComponent<SkinnedMeshRenderer>();
                    body.sharedMaterial = standard;
                    ConfigureSkinnedRenderer(body, lod >= 2);
                    slots[lod * ZombieSkinnedVisual.SlotCount] = body;
                    for (int slot = 1; slot < ZombieSkinnedVisual.SlotCount; slot++)
                    {
                        var go = new GameObject("LOD" + lod + "_" + slotNames[slot]);
                        go.transform.SetParent(instance.transform, false);
                        var smr = go.AddComponent<SkinnedMeshRenderer>();
                        smr.sharedMaterial = standard;
                        smr.rootBone = body.rootBone;
                        smr.localBounds = body.localBounds;
                        smr.enabled = false;
                        ConfigureSkinnedRenderer(smr, lod >= 2);
                        slots[lod * ZombieSkinnedVisual.SlotCount + slot] = smr;
                    }
                }

                // WP-017: three AP-008 armour slots per LOD (helmet / body / leg), two renderers each
                // (main plate + kote / suneate sub-mesh), driven by ArmorVisualBinder (mesh swap per
                // state). Material = the armour atlas (Standard) when the armour library exists;
                // the binder sets the version variant.
                var armorLibrary = AssetDatabase.LoadAssetAtPath<ZombieArmorLibrary>(ArtArmorBuilder.LibraryPath);
                Material armorStandard = armorLibrary != null ? armorLibrary.MaterialFor(0) : standard;
                string[] armorNames = { "Helmet", "BodyArmor", "LegArmor" };
                string[] partNames = { "", "_Sub" };
                var armorSlots = new SkinnedMeshRenderer[ZombieSkinnedVisual.ArmorRendererCount];
                for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                {
                    SkinnedMeshRenderer body = slots[lod * ZombieSkinnedVisual.SlotCount];
                    for (int a = 0; a < ZombieSkinnedVisual.ArmorSlotCount; a++)
                    for (int part = 0; part < ZombieSkinnedVisual.ArmorPartCount; part++)
                    {
                        var go = new GameObject("LOD" + lod + "_" + armorNames[a] + partNames[part]);
                        go.transform.SetParent(instance.transform, false);
                        var smr = go.AddComponent<SkinnedMeshRenderer>();
                        smr.sharedMaterial = armorStandard;
                        smr.rootBone = body.rootBone;
                        smr.localBounds = body.localBounds;
                        smr.enabled = false;
                        ConfigureSkinnedRenderer(smr, lod >= 2);
                        armorSlots[(lod * ZombieSkinnedVisual.ArmorSlotCount + a) * ZombieSkinnedVisual.ArmorPartCount + part] = smr;
                    }
                }

                LODGroup group = instance.GetComponent<LODGroup>() ?? instance.AddComponent<LODGroup>();
                var perLod = new SkinnedMeshRenderer[ZombieModuleLibrary.LodCount];
                for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++) perLod[lod] = slots[lod * ZombieSkinnedVisual.SlotCount];
                ApplyLods(group, perLod, ZombieIntendedDistances, r =>
                {
                    int lod = Array.IndexOf(perLod, r);
                    int perLodArmor = ZombieSkinnedVisual.ArmorSlotCount * ZombieSkinnedVisual.ArmorPartCount;
                    var list = new Renderer[ZombieSkinnedVisual.SlotCount + perLodArmor];
                    for (int slot = 0; slot < ZombieSkinnedVisual.SlotCount; slot++) list[slot] = slots[lod * ZombieSkinnedVisual.SlotCount + slot];
                    for (int a = 0; a < perLodArmor; a++) list[ZombieSkinnedVisual.SlotCount + a] = armorSlots[lod * perLodArmor + a];
                    return list;
                });

                Dictionary<string, Transform> bones = ArtCharacterContract.Bones(instance, ZombieArmature);
                Transform armature = instance.transform.Find(ZombieArmature);
                bones.TryGetValue(ZombieSkinnedVisual.AnchorHelmet, out Transform helmet);
                bones.TryGetValue(ZombieSkinnedVisual.AnchorTorso, out Transform torso);
                bones.TryGetValue(ZombieSkinnedVisual.AnchorLegs, out Transform legs);
                if (helmet == null || torso == null || legs == null) throw new InvalidOperationException(LogPrefix + "zombie body A is missing an armour anchor");

                var visual = root.AddComponent<ZombieSkinnedVisual>();
                CharacterAnimatorLod lodStep = root.GetComponent<CharacterAnimatorLod>() ?? root.AddComponent<CharacterAnimatorLod>();
                lodStep.Configure(animator, group);
                visual.Configure(animator, group, slots, armature != null ? armature : instance.transform, helmet, torso, legs, animationSet, events, zombieLook, zombieFeet, lodStep);
                visual.ConfigureArmor(armorSlots);

                EnsureFolder(Path.GetDirectoryName(ZombiePrefab).Replace('\\', '/'));
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ZombiePrefab);
                Debug.Log(LogPrefix + "zombie prefab written: " + ZombiePrefab + " (bones " + bones.Count + ", slots " + slots.Length + ", armour slots " + armorSlots.Length + (armorLibrary != null ? ", armour atlas" : ", no armour library") + ")");
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ================================================================ 7 asset set

        public static CharacterAssetSet BuildAssetSet()
        {
            EnsureFolder(Path.GetDirectoryName(CharacterAssets).Replace('\\', '/'));
            var set = AssetDatabase.LoadAssetAtPath<CharacterAssetSet>(CharacterAssets);
            bool created = set == null;
            if (created)
            {
                set = ScriptableObject.CreateInstance<CharacterAssetSet>();
                AssetDatabase.CreateAsset(set, CharacterAssets);
            }

            var so = new SerializedObject(set);
            so.FindProperty("_source").enumValueIndex = (int)CharacterVisualSource.Skinned;
            so.FindProperty("_playerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            SerializedProperty skins = so.FindProperty("_playerSkinAlbedos");
            skins.arraySize = 3;
            skins.GetArrayElementAtIndex(0).objectReferenceValue = Load<Texture2D>(PlayerTextures + "/T_player_skin1_BaseColor.png");
            skins.GetArrayElementAtIndex(1).objectReferenceValue = Load<Texture2D>(PlayerTextures + "/T_player_skin2b_BaseColor.png");   // WP-017: skin 2 = 2b (user decision 2026-09-16 07:50)
            skins.GetArrayElementAtIndex(2).objectReferenceValue = Load<Texture2D>(PlayerTextures + "/T_player_skin3_BaseColor.png");
            so.FindProperty("_playerAnimation").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(PlayerAnimationSet);
            so.FindProperty("_zombiePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ZombiePrefab);
            so.FindProperty("_zombieLibrary").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ZombieModuleLibrary>(ZombieLibrary);
            so.FindProperty("_zombieAnimation").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ZombieAnimationSet);
            so.FindProperty("_zombieArmor").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ZombieArmorLibrary>(ArtArmorBuilder.LibraryPath);   // WP-017
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "asset set " + (created ? "created" : "updated") + ": " + CharacterAssets + " (Skinned)");
            return set;
        }

        // ================================================================== helpers

        private static void ConfigureSkinnedRenderer(SkinnedMeshRenderer smr, bool lodFar)
        {
            smr.shadowCastingMode = lodFar ? ShadowCastingMode.Off : ShadowCastingMode.On;   // ADR-012-1: far LODs cast none
            smr.receiveShadows = true;
            smr.lightProbeUsage = LightProbeUsage.Off;
            smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            smr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            smr.updateWhenOffscreen = false;
            smr.skinnedMotionVectors = false;
            smr.quality = SkinQuality.Bone4;
        }

        /// <summary>LODGroup levels from intended distances (÷ shipped lodBias), cross-fade; the last distance is the cull.</summary>
        public static void ApplyLods(LODGroup group, SkinnedMeshRenderer[] perLod, float[] intendedDistances, Func<SkinnedMeshRenderer, Renderer[]> renderersOf)
        {
            Bounds b = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 1.8f, 0.9f));   // 1.8 m capsule envelope (AP-006/007)
            float h = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            var lods = new LOD[perLod.Length];
            float previous = float.MaxValue;
            for (int i = 0; i < perLod.Length; i++)
            {
                float authored = intendedDistances[i] / ArtFamilyPrefabBuilder.ShippedLodBias;
                float t = Mathf.Clamp(h / (ArtFamilyPrefabBuilder.VisibleHeightPerMetre * authored), ArtFamilyPrefabBuilder.MinTransition, ArtFamilyPrefabBuilder.MaxTransition);
                if (t >= previous) t = previous * 0.5f;
                previous = t;
                lods[i] = new LOD(t, renderersOf(perLod[i])) { fadeTransitionWidth = 0.15f };
            }

            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        private static string Tris(SkinnedMeshRenderer[] renderers)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(renderers[i] != null && renderers[i].sharedMesh != null ? (renderers[i].sharedMesh.GetIndexCount(0) / 3).ToString() : "-");
            }

            return sb.ToString();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }

            return null;
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException(LogPrefix + typeof(T).Name + " missing", path);
            return asset;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
