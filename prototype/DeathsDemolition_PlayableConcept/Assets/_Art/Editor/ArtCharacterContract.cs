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
        public const string PlayerAvatarSource = PlayerFolder + "/player_base.fbx";
        public const string ZombieAvatarSource = ZombieFolder + "/zombie_body_A.fbx";
        public const int BoneCount = 56;
        /// <summary>WP-032F (AP-006C): secondary-motion chain bones `sec_&lt;chain&gt;_&lt;i&gt;` — extra transforms outside the human map, never keyed by the clips. Player 7 (sash ×3, pouch, gourd, band ×2), zombie 17 (mino ×9, hair ×2, queue ×2, hachimaki ×2, bandage ×2).</summary>
        public const int PlayerSecondaryBones = 7;
        public const int ZombieSecondaryBones = 17;
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

        public static bool IsClip(string assetPath)
        {
            string p = assetPath.Replace('\\', '/');
            return IsCharacterModel(assetPath) && (p.Contains("/" + ClipsFolderName + "/") || ArtAnimationClipContract.IsAnimationClip(assetPath));
        }

        public static bool IsAvatarSource(string assetPath)
        {
            return assetPath == PlayerAvatarSource || assetPath == ZombieAvatarSource;
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

        /// <summary>Writes the v1.1 rules into a character importer. Returns true if anything changed.</summary>
        public static bool Apply(ModelImporter importer)
        {
            bool changed = ArtImportContract.Apply(importer);   // v1 first; the lines below override its animation/blend-shape defaults
            string path = importer.assetPath;
            bool clip = IsClip(path);

            changed |= Set(importer.animationType, ModelImporterAnimationType.Human, v => importer.animationType = v);
            changed |= Set(importer.generateSecondaryUV, ArtImportContract.CharacterGenerateSecondaryUV, v => importer.generateSecondaryUV = v);   // WP-029: skinned = probe-lit, no lightmap UVs
            changed |= Set(importer.optimizeGameObjects, false, v => importer.optimizeGameObjects = v);
            changed |= Set(importer.importAnimation, clip, v => importer.importAnimation = v);
            changed |= Set(importer.importBlendShapes, false, v => importer.importBlendShapes = v);
            changed |= Set(importer.importVisibility, false, v => importer.importVisibility = v);

            changed |= Set(importer.avatarSetup, ModelImporterAvatarSetup.CreateFromThisModel, v => importer.avatarSetup = v);
            if (importer.sourceAvatar != null)
            {
                importer.sourceAvatar = null;
                changed = true;
            }

            changed |= ApplyHumanDescription(importer);

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
            Check(v, "animationType", importer.animationType, ModelImporterAnimationType.Human);
            Check(v, "optimizeGameObjects", importer.optimizeGameObjects, false);
            Check(v, "importAnimation", importer.importAnimation, clip);
            Check(v, "importBlendShapes", importer.importBlendShapes, false);
            Check(v, "avatarSetup", importer.avatarSetup, ModelImporterAvatarSetup.CreateFromThisModel);
            Avatar avatar = AvatarOf(path);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
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
