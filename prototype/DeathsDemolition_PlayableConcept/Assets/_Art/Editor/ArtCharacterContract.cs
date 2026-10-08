using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-016 · Import Contract v1.1 — the skinned-character extension of v1 (ADR-016-1).
    /// Applies to every FBX under <c>Assets/_Art/Characters</c>: the v1 model rules stay
    /// (metres, bakeAxisConversion, normals imported, MikkTSpace tangents, no material or
    /// texture sub-assets, Read/Write off) and on top of them:
    ///
    ///   - <c>animationType Humanoid</c>, avatar <c>CreateFromThisModel</c> on EVERY file. The
    ///     AP-006/007 rig template (56 bones, identical names / hierarchy / rest pose) is the
    ///     single skeleton source; Unity's <c>CopyFromOther</c> cannot express it because the
    ///     copied avatar also lists the source file's mesh transforms (measured: "invalid copied
    ///     Avatar Rig Configuration" on every file whose mesh objects differ), so each file
    ///     builds its avatar from the same skeleton and the gate proves the equivalence: the
    ///     52 human-bone mappings of every character avatar equal the source's
    ///     (<c>player_base.fbx</c> for the player, <c>zombie_body_A.fbx</c> for the zombie).
    ///     Runtime uses one avatar per character (the source's, on the prefab Animator); clips
    ///     retarget through the Humanoid muscle space.
    ///   - <c>optimizeGameObjects</c> off: the bone Transforms are what the module renderers
    ///     re-point to by name and where the sockets / armour anchors hang.
    ///   - Animation is imported only from the <c>Clips/</c> files (rig tests until AP-009):
    ///     every clip loops, root rotation / height / position locked to the pose (no root
    ///     motion; the motors move the characters).
    ///   - No blend shapes (none authored), no cameras / lights, no mesh compression.
    ///
    /// Textures follow v1 with the WP-016 additions in <see cref="ArtImportContract"/>:
    /// class <c>Character</c> (2048, streamed), roles <c>Mask</c> / <c>DetailMask</c>.
    /// </summary>
    public static class ArtCharacterContract
    {
        public const string Version = "ADR-016-1 Import Contract v1.1 skinned (2026-09-16)";
        public const string PlayerFolder = ArtImportContract.CharactersRoot + "/Player";
        public const string ZombieFolder = ArtImportContract.CharactersRoot + "/Zombie";
        public const string ClipsFolderName = "Clips";
        /// <summary>
        /// WP-082 R3 (C-00 07.10) · hero v2 "Kırık Mühür" (AP-024 v003 data, P-06 AP-024D D6 = exports/v004: four FBX
        /// <c>Hero_v2_TD_LOD0..3</c>, each the armature <c>PlayerRig</c> + its own <c>Player_Body_LOD&lt;n&gt;</c>, 77 bones, no
        /// separate head) is the player body. LOD0 is the avatar source; LOD1–3 are rig fragments (Generic, no avatar) whose
        /// renderers the prefab builder re-binds by bone name onto the LOD0 skeleton.
        /// </summary>
        public const string HeroV2Folder = PlayerFolder + "/HeroV2";
        public const int HeroV2LodCount = 4;
        public static string HeroV2LodFile(int lod) => HeroV2Folder + "/Hero_v2_TD_LOD" + lod + ".fbx";
        public const string PlayerAvatarSource = HeroV2Folder + "/Hero_v2_TD_LOD0.fbx";
        /// <summary>
        /// WP-082 R3 · the AP-022 rig v002 body (pre-hero). Kept ONLY for the first-person viewmodel: FP copies this model and draws
        /// the AP-022 FP arms on its skeleton, bit-identical to before (FP is out of the R3 TD scope; not re-targeted to the hero).
        /// Still a character model on the v1.1 contract with its own avatar (the FP Animator needs it).
        /// </summary>
        public const string LegacyPlayerModel = PlayerFolder + "/player_base.fbx";
        public const string ZombieAvatarSource = ZombieFolder + "/zombie_body_A.fbx";
        public const int BoneCount = 56;
        /// <summary>WP-082 R3 · hero v2: the 77 PlayerRig bones under the armature (Root + 52 human + jaw + twist ×8 + fp_camera + 7 sockets + 7 sec_*).</summary>
        public const int HeroV2BoneCount = 77;
        /// <summary>WP-082 R3 · hero v2 sockets (AP-024 README): name → parent bone.</summary>
        public static readonly string[,] HeroV2SocketParents =
        {
            { "socket_weapon_r", "RightHand" }, { "socket_weapon_l", "LeftHand" }, { "socket_mag_l", "LeftHand" }, { "mag_socket", "socket_mag_l" },
            { "prop_r", "RightHand" }, { "prop_l", "LeftHand" }, { "socket_back", "UpperChest" }, { "fp_camera", "Head" }
        };
        /// <summary>WP-032F (AP-006C): secondary-motion chain bones `sec_&lt;chain&gt;_&lt;i&gt;` — extra transforms outside the human map, never keyed by the clips. Player 7 (sash ×3, pouch, gourd, band ×2), zombie 17 (mino ×9, hair ×2, queue ×2, hachimaki ×2, bandage ×2).</summary>
        public const int PlayerSecondaryBones = 7;
        public const int ZombieSecondaryBones = 17;
        /// <summary>WP-071 · AP-022 zombie rig v002 (K10: jaw only): `jaw` Head child, lower-face weights on bodies A/B/C + A2/H2/H4/H6/H7.</summary>
        public const int ZombieJawBones = 1;
        /// <summary>WP-071 · AP-022 rig v002 (player): extra transforms outside the human map — twist ×8 (upperarm, lowerarm_01/02, thigh ×2 sides; TwistCorrector), jaw, fp_camera (Head child, Y = look, Z = up), ik_hand/ik_foot ×2 (Root children).</summary>
        public const int PlayerRigV002Bones = 14;
        public static readonly string[] PlayerTwistBones = { "upperarm_twist_l", "upperarm_twist_r", "lowerarm_twist_01_l", "lowerarm_twist_01_r", "lowerarm_twist_02_l", "lowerarm_twist_02_r", "thigh_twist_l", "thigh_twist_r" };
        public const string SecondaryBonePrefix = "sec_";
        public const int HumanoidBoneCount = 52;
        public static readonly string[] Sockets = { "socket_weapon_r", "socket_weapon_l", "socket_back" };

        /// <summary>The 22 body bones every character avatar must map (Unity human names); fingers are optional until AP-009.</summary>
        public static readonly string[] RequiredHumanBones =
        {
            "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
            "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes"
        };
        public static readonly string[] ZombieAnchors = { "anchor_helmet", "anchor_torso", "anchor_legs" };

        public static bool IsCharacterModel(string assetPath)
        {
            return ArtImportContract.IsContractModel(assetPath) && assetPath.StartsWith(ArtImportContract.CharactersRoot + "/", StringComparison.Ordinal);
        }

        /// <summary>
        /// WP-084 (C-00 06.10, AP-025D v003) · the AP-025 face FBXs carry a rig fragment (Root … Neck / Head / jaw), not the 52-bone
        /// body, so no humanoid avatar can be built from them. They never animate on their own — ZombieSkinnedVisual binds their bone
        /// list by name to the actor skeleton — so they import Generic with no avatar; every other v1.1 rule holds.
        /// </summary>
        public static bool IsRigFragment(string assetPath)
        {
            if (assetPath == null) return false;
            string p = assetPath.Replace('\\', '/');
            return p.StartsWith(ZombieFolder + "/Faces/", StringComparison.Ordinal) || IsHeroV2LodFragment(p);
        }

        /// <summary>WP-082 R3 · the hero's LOD1–3 files: Generic / no avatar (their meshes are re-bound to the LOD0 skeleton by bone name).</summary>
        public static bool IsHeroV2LodFragment(string assetPath)
        {
            for (int lod = 1; lod < HeroV2LodCount; lod++) if (assetPath == HeroV2LodFile(lod)) return true;
            return false;
        }

        public static bool IsClip(string assetPath)
        {
            string p = assetPath.Replace('\\', '/');
            return IsCharacterModel(assetPath) && (p.Contains("/" + ClipsFolderName + "/") || ArtAnimationClipContract.IsAnimationClip(assetPath));
        }

        public static bool IsAvatarSource(string assetPath)
        {
            return assetPath == PlayerAvatarSource || assetPath == ZombieAvatarSource || assetPath == LegacyPlayerModel;   // WP-082 R3: the FP model keeps its own avatar
        }

        /// <summary>The avatar source file a character FBX copies from (itself for the two sources).</summary>
        public static string AvatarSourceFor(string assetPath)
        {
            if (IsAvatarSource(assetPath)) return assetPath;
            return assetPath.StartsWith(PlayerFolder + "/", StringComparison.Ordinal) ? PlayerAvatarSource : ZombieAvatarSource;
        }

        private static readonly string[] Sides = { "Left", "Right" };
        private static readonly string[] FingerHuman = { "Thumb", "Index", "Middle", "Ring", "Little" };
        private static readonly string[] FingerBone = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        private static readonly string[] Phalanx = { "Proximal", "Intermediate", "Distal" };

        /// <summary>
        /// The explicit 52-bone human mapping (Unity human name → AP-006/007 bone name): the 22
        /// body bones share Unity's names; the 30 finger bones are <c>&lt;Side&gt;&lt;Finger&gt;1..3</c>
        /// in the export ("Pinky" for Unity's "Little"), which the auto-mapper only partly
        /// recognises (measured: 35 of 52 on the player, 52 on the zombie). Written into every
        /// character importer so all avatars carry the same complete mapping.
        /// </summary>
        public static HumanBone[] TemplateHumanBones()
        {
            var list = new List<HumanBone>(HumanoidBoneCount);
            foreach (string body in RequiredHumanBones)
            {
                list.Add(new HumanBone { humanName = body, boneName = body, limit = new HumanLimit { useDefaultValues = true } });
            }

            foreach (string side in Sides)
            for (int f = 0; f < FingerHuman.Length; f++)
            for (int p = 0; p < Phalanx.Length; p++)
            {
                list.Add(new HumanBone
                {
                    humanName = side + " " + FingerHuman[f] + " " + Phalanx[p],
                    boneName = side + FingerBone[f] + (p + 1),
                    limit = new HumanLimit { useDefaultValues = true }
                });
            }

            return list.ToArray();
        }

        /// <summary>
        /// Applies the template mapping when the importer's human description differs. Returns true if written.
        /// WP-017: the comparison is order-insensitive — Unity re-serialises the human list in its own
        /// order on import, and an ordered compare rewrote every character .meta on each pipeline run
        /// (measured: 27 WP-016 meta files flipping between the template and the canonical order).
        /// </summary>
        public static bool ApplyHumanDescription(ModelImporter importer)
        {
            HumanBone[] template = TemplateHumanBones();
            HumanDescription hd = importer.humanDescription;
            bool same = hd.human != null && hd.human.Length == template.Length;
            if (same)
            {
                var current = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < hd.human.Length; i++) current.Add(hd.human[i].humanName + "=" + hd.human[i].boneName);
                for (int i = 0; i < template.Length; i++)
                {
                    if (!current.Contains(template[i].humanName + "=" + template[i].boneName)) { same = false; break; }
                }
            }

            if (same) return false;
            hd.human = template;
            importer.humanDescription = hd;
            return true;
        }

        // WP-082 R3: reviewed source-only one-clip exception; actual import requires a separate grant.
        private const string PinnedHeroCopyClipPath = "Assets/_Art/Characters/Animations/AP022_tp_lmg_2_reload_tac_v008.fbx";
        private const string PinnedHeroCopyClipGuid = "91d21fc08d3234866bef95abc4b6c473";
        private const string PinnedHeroReferencePath = "Assets/_Art/Characters/Player/HeroV2/Hero_v2_TD_LOD0.fbx";
        private const string PinnedHeroReferenceGuid = "96b52215e71f840c4a81e3b4bffddc89";
        private const long PinnedHeroReferenceLocalId = 9000000L;
        private const string PinnedHeroReferenceDependency = "bc7caa10d859f4e5ca5ef00f81dc6da4";

        private static bool UsesPinnedHeroCopyAvatar(string path)
        {
            return string.Equals(path, PinnedHeroCopyClipPath, StringComparison.Ordinal)
                && string.Equals(AssetDatabase.AssetPathToGUID(path), PinnedHeroCopyClipGuid, StringComparison.Ordinal);
        }
        private static bool HasExactTemplateHumanMapping(Avatar avatar)
        {
            HumanBone[] template = TemplateHumanBones();
            if (HumanoidBoneCount != 52 || template.Length != 52)
                throw new InvalidOperationException("Pinned exception requires existing 52-bone template");
            var expected = new List<string>(template.Length);
            foreach (HumanBone bone in template) expected.Add(bone.humanName + "=" + bone.boneName);
            expected.Sort(StringComparer.Ordinal);
            List<string> actual = HumanMapping(avatar);
            return actual.Count == 52 && string.Equals(string.Join("\n", actual), string.Join("\n", expected), StringComparison.Ordinal);
        }
        private static Avatar ExplicitPinnedHeroAvatar()
        {
            if (!string.Equals(AssetDatabase.AssetPathToGUID(PinnedHeroReferencePath), PinnedHeroReferenceGuid, StringComparison.Ordinal))
                throw new InvalidOperationException("Pinned Hero reference GUID mismatch");
            if (!string.Equals(AssetDatabase.GetAssetDependencyHash(PinnedHeroReferencePath).ToString(), PinnedHeroReferenceDependency, StringComparison.Ordinal))
                throw new InvalidOperationException("Pinned Hero reference dependency mismatch");
            Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(PinnedHeroReferencePath);
            string guid; long localId;
            if (avatar == null || !string.Equals(AssetDatabase.GetAssetPath(avatar), PinnedHeroReferencePath, StringComparison.Ordinal)
                || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(avatar, out guid, out localId)
                || !string.Equals(guid, PinnedHeroReferenceGuid, StringComparison.Ordinal) || localId != PinnedHeroReferenceLocalId
                || !string.Equals(avatar.name, "Hero_v2_TD_LOD0Avatar", StringComparison.Ordinal)
                || !avatar.isValid || !avatar.isHuman || !HasExactTemplateHumanMapping(avatar))
                throw new InvalidOperationException("Pinned Hero identity/human mapping invalid");
            return avatar;
        }
        private static void ValidatePinnedHeroCopyReference(ModelImporter importer, List<string> violations)
        {
            Avatar expected;
            try { expected = ExplicitPinnedHeroAvatar(); }
            catch (InvalidOperationException exception) { violations.Add("sourceAvatar: " + exception.Message); return; }
            Avatar actual = importer.sourceAvatar; string guid; long localId;
            if (actual == null || actual != expected
                || !string.Equals(AssetDatabase.GetAssetPath(actual), PinnedHeroReferencePath, StringComparison.Ordinal)
                || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(actual, out guid, out localId)
                || !string.Equals(guid, PinnedHeroReferenceGuid, StringComparison.Ordinal) || localId != PinnedHeroReferenceLocalId
                || !actual.isValid || !actual.isHuman || !HasExactTemplateHumanMapping(actual))
                violations.Add("sourceAvatar: explicit pinned Hero Avatar required");
        }

        /// <summary>Writes the v1.1 rules into a character importer. Returns true if anything changed.</summary>
        public static bool Apply(ModelImporter importer)
        {
            string path = importer.assetPath;
            bool copyHero = UsesPinnedHeroCopyAvatar(path);
            Avatar pinnedHero = copyHero ? ExplicitPinnedHeroAvatar() : null;
            bool changed = copyHero
                ? ArtImportContract.ApplyModelPreservingAnimationState(importer)
                : ArtImportContract.Apply(importer);   // Default assets retain v1 animation defaults.
            bool clip = IsClip(path);
            bool fragment = IsRigFragment(path);   // WP-084: Generic, no avatar

            changed |= Set(importer.animationType, fragment ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human, v => importer.animationType = v);
            changed |= Set(importer.generateSecondaryUV, ArtImportContract.CharacterGenerateSecondaryUV, v => importer.generateSecondaryUV = v);   // WP-029: skinned = probe-lit, no lightmap UVs
            changed |= Set(importer.optimizeGameObjects, false, v => importer.optimizeGameObjects = v);
            changed |= Set(importer.importAnimation, clip, v => importer.importAnimation = v);
            changed |= Set(importer.importBlendShapes, false, v => importer.importBlendShapes = v);
            changed |= Set(importer.importVisibility, false, v => importer.importVisibility = v);

            if (copyHero)
            {
                changed |= Set(importer.avatarSetup, ModelImporterAvatarSetup.CopyFromOther, v => importer.avatarSetup = v);
                if (importer.sourceAvatar != pinnedHero)
                {
                    importer.sourceAvatar = pinnedHero;
                    changed = true;
                }
                // Hero source HumanDescription is authoritative; no local rewrite here.
            }
            else
            {
                changed |= Set(importer.avatarSetup, fragment ? ModelImporterAvatarSetup.NoAvatar : ModelImporterAvatarSetup.CreateFromThisModel, v => importer.avatarSetup = v);
                if (importer.sourceAvatar != null)
                {
                    importer.sourceAvatar = null;
                    changed = true;
                }
    
                if (!fragment) changed |= ApplyHumanDescription(importer);
            }

            if (clip)
            {
                changed |= ApplyClipsFor(importer);
            }

            return changed;
        }

        /// <summary>WP-018: the AP-009 Animations/ files take their loop / events / additive flags from the manifest.</summary>
        public static bool ApplyClipsFor(ModelImporter importer)
        {
            return ArtAnimationClipContract.IsAnimationClip(importer.assetPath)
                ? ArtAnimationClipContract.ApplyClips(importer)
                : ApplyClips(importer);
        }

        /// <summary>
        /// Every take of a Clips/ file as an explicit clip: looping, root rotation / height /
        /// XZ locked to the pose (no root motion). Returns true if anything changed; a no-op
        /// while the file's takes are not parsed yet (first OnPreprocessModel).
        /// </summary>
        public static bool ApplyClips(ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            if (clips == null || clips.Length == 0)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < clips.Length; i++)
            {
                ModelImporterClipAnimation c = clips[i];
                if (!c.loopTime || !c.lockRootRotation || !c.lockRootHeightY || !c.lockRootPositionXZ || !c.keepOriginalOrientation || !c.keepOriginalPositionY || !c.keepOriginalPositionXZ)
                {
                    c.loopTime = true;
                    c.loopPose = false;
                    c.lockRootRotation = true;
                    c.lockRootHeightY = true;
                    c.lockRootPositionXZ = true;
                    c.keepOriginalOrientation = true;
                    c.keepOriginalPositionY = true;
                    c.keepOriginalPositionXZ = true;
                    clips[i] = c;
                    changed = true;
                }
            }

            if (changed || importer.clipAnimations == null || importer.clipAnimations.Length != clips.Length)
            {
                importer.clipAnimations = clips;
                changed = true;
            }

            return changed;
        }

        /// <summary>Every field that differs from v1.1, as "field: actual != expected". Empty = compliant.</summary>
        public static List<string> Validate(ModelImporter importer)
        {
            List<string> v = ArtImportContract.ValidateModel(importer);
            // v1 expects animationType None / importAnimation false; v1.1 overrides those two.
            v.RemoveAll(line => line.StartsWith("animationType:", StringComparison.Ordinal) || line.StartsWith("importAnimation:", StringComparison.Ordinal) || line.StartsWith("generateSecondaryUV:", StringComparison.Ordinal));
            Check(v, "generateSecondaryUV", importer.generateSecondaryUV, ArtImportContract.CharacterGenerateSecondaryUV);

            string path = importer.assetPath;
            bool clip = IsClip(path);
            bool fragment = IsRigFragment(path);
            bool copyHero = UsesPinnedHeroCopyAvatar(path);
            Check(v, "animationType", importer.animationType, fragment ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human);
            Check(v, "optimizeGameObjects", importer.optimizeGameObjects, false);
            Check(v, "importAnimation", importer.importAnimation, clip);
            Check(v, "importBlendShapes", importer.importBlendShapes, false);
            Check(v, "avatarSetup", importer.avatarSetup, copyHero ? ModelImporterAvatarSetup.CopyFromOther : fragment ? ModelImporterAvatarSetup.NoAvatar : ModelImporterAvatarSetup.CreateFromThisModel);
            Avatar avatar = AvatarOf(path);
            if (copyHero)
            {
                ValidatePinnedHeroCopyReference(importer, v);
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                    v.Add("copiedAvatar: RUNTIME_UNKNOWN; local AvatarOf is " + (avatar == null ? "missing" : !avatar.isValid ? "invalid" : "not humanoid"));
                else if (!HasExactTemplateHumanMapping(avatar))
                    v.Add("copiedAvatar.humanBones: exact existing 52-bone mapping required");
                GameObject importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Animator[] importedAnimators = importedModel == null ? new Animator[0] : importedModel.GetComponentsInChildren<Animator>(true);
                if (importedAnimators.Length != 1)
                    v.Add("copiedAvatar.effective: UNKNOWN; expected one imported Animator");
                else
                {
                    Avatar effective = importedAnimators[0].avatar;
                    Avatar expected = null;
                    try { expected = ExplicitPinnedHeroAvatar(); }
                    catch (InvalidOperationException exception) { v.Add("copiedAvatar.effective: " + exception.Message); }
                    if (effective == null || !effective.isValid || !effective.isHuman)
                        v.Add("copiedAvatar.effective: UNKNOWN; missing/invalid/nonhuman Avatar");
                    else if (expected != null && (effective != expected || effective != importer.sourceAvatar || !HasExactTemplateHumanMapping(effective)))
                        v.Add("copiedAvatar.effective: pinned Hero/sourceAvatar identity mismatch");
                }
            }
            else if (fragment)
            {
                if (avatar != null) v.Add("avatar: present on a rig fragment (WP-084: Generic, no avatar)");
            }
            else if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                v.Add("avatar: " + (avatar == null ? "missing" : !avatar.isValid ? "invalid" : "not humanoid"));
            }
            else
            {
                int mapped = HumanMapping(avatar).Count;
                if (mapped != HumanoidBoneCount) v.Add("avatar.humanBones: " + mapped + " != " + HumanoidBoneCount);
            }

            if (clip && ArtAnimationClipContract.IsAnimationClip(path))
            {
                v.AddRange(ArtAnimationClipContract.Validate(importer));
            }
            else if (clip)
            {
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0)
                {
                    v.Add("clipAnimations: none (a Clips/ file must carry its take)");
                }
                else
                {
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (!clips[i].loopTime) v.Add("clip[" + clips[i].name + "].loopTime: false != true");
                        if (!clips[i].lockRootRotation || !clips[i].lockRootHeightY || !clips[i].lockRootPositionXZ) v.Add("clip[" + clips[i].name + "]: root not locked (no root motion)");
                    }
                }
            }

            return v;
        }

        /// <summary>
        /// "HumanBone=boneName" lines of an avatar's human description, sorted — the skeleton
        /// signature the gate compares between every character file and its source.
        /// </summary>
        public static List<string> HumanMapping(Avatar avatar)
        {
            var lines = new List<string>();
            if (avatar == null) return lines;
            HumanBone[] human = avatar.humanDescription.human;
            for (int i = 0; i < human.Length; i++) lines.Add(human[i].humanName + "=" + human[i].boneName);
            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        /// <summary>The avatar of an imported character FBX, or null.</summary>
        public static Avatar AvatarOf(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<Avatar>(assetPath);
        }

        /// <summary>Bone transforms under the imported model prefab (every Transform that is a descendant of the armature Null), by name.</summary>
        public static Dictionary<string, Transform> Bones(GameObject modelRoot, string armatureName)
        {
            var map = new Dictionary<string, Transform>(64, StringComparer.Ordinal);
            if (modelRoot == null) return map;
            Transform armature = modelRoot.transform.Find(armatureName);
            Transform root = armature != null ? armature : modelRoot.transform;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (!map.ContainsKey(t.name)) map.Add(t.name, t);
            }

            return map;
        }

        private static bool Set<T>(T current, T expected, Action<T> write)
        {
            if (EqualityComparer<T>.Default.Equals(current, expected)) return false;
            write(expected);
            return true;
        }

        private static void Check<T>(List<string> v, string field, T actual, T expected)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected)) v.Add(field + ": " + actual + " != " + expected);
        }
    }
}
