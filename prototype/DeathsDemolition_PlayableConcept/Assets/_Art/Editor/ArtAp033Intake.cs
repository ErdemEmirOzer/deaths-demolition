using System;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-096 (S-03) · AP-033 zombie locomotion intake, one batch step for every integrated part (<see cref="Deliveries"/>: p1 4 + p2 8 + p3 10):
    /// <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtAp033Intake.RunAll</c> (<see cref="RunP1"/> = the p1 window's entry).
    ///   1. force-imports the delivered FBX (Characters/Animations/AP033_*_v001.fbx) — ArtAssetPostprocessor applies the v1.1
    ///      character contract (Humanoid, avatar from the model, the 52-bone template mapping) and the manifest row's take
    ///      (name, loop, root locked, the anim-events.json events at frame / cycle normalised time, value = side);
    ///   2. gates every file on ArtCharacterContract.Validate (importer + humanoid avatar + clip contract) and the clip itself
    ///      (looping, frames, event count); a take the pre-written .meta could not bind is rebuilt once from the FBX's default take;
    ///   3. gates the manifest on the v3 event rules (AP022_ / AP033_);
    ///   4. rebuilds DD_Zombie.controller + CharacterAnimationSet_Zombie.asset in place (ArtAnimatorBuilder.BuildZombieOnly —
    ///      the player controller / sets are not touched) — the build ends with the Unity-rig stride calibration
    ///      (ArtZombieStrideCalibration, one "[DD-ART] stride &lt;clip&gt; spd → unity" line per gait) — then checks the gait tree (one child per
    ///      ZombieGaitClips index, the limp R child mirrored) and every intake clip's set speed (measured, or the spd fallback);
    ///   5. logs the verifiable line <c>[DD-ART] ap033.p1+p2+p3 imported=22 gaitChildren=22 mirror=1 rebound=0 calibrated=… …</c>. Any failure throws.
    /// </summary>
    public static class ArtAp033Intake
    {
        public const string LogPrefix = "[DD-ART] ";
        public static readonly string[] P1Clips = { C.ZombieWalkLurchAV4, C.ZombieWalkLimpAV4, C.ZombieRunFeralAV4, C.ZombieWalkStompAV4 };
        public static readonly string[] P2Clips =
        {
            C.ZombieWalkLurchBV4, C.ZombieWalkDragAV4, C.ZombieWalkDragBV4, C.ZombieRunFeralBV4, C.ZombieRunStumbleAV4, C.ZombieWalkHeavyAV4, C.ZombieWalkHeavyBV4, C.ZombieWalkStompBV4
        };

        /// <summary>Integrated AP-033 gaits and p3 one-shot transitions; only gait clips enter stride calibration.</summary>
        public static readonly string[] P3Clips = ZombieTransitionData.Clips;
        public static readonly (string part, string[] clips)[] Deliveries = { ("p1", P1Clips), ("p2", P2Clips), ("p3", P3Clips) };

        [MenuItem("DD/Art/Run AP-033 Zombie Locomotion p1+p2+p3 (WP-096)", priority = 334)]
        public static void RunAll()
        {
            var clips = new List<string>();
            var parts = new List<string>();
            foreach ((string part, string[] list) d in Deliveries) { parts.Add(d.part); clips.AddRange(d.list); }
            Run("ap033." + string.Join("+", parts), clips.ToArray());
        }

        /// <summary>The p1 window's entry (same steps; checks the p1 files only, the build covers the whole manifest).</summary>
        public static void RunP1() => Run("ap033.p1", P1Clips);

        private static void Run(string tag, string[] intake)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException(LogPrefix + tag + " cannot run in Play mode.");
            AssetDatabase.Refresh();
            Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest = ArtAnimationClipContract.Manifest();
            if (manifest == null) throw new InvalidOperationException(LogPrefix + tag + " manifest missing: " + ArtAnimationClipContract.ManifestPath);

            var errors = new List<string>();
            int imported = 0, rebound = 0;
            foreach (string name in intake)
            {
                if (!manifest.TryGetValue(name, out ArtAnimationClipContract.ClipSpec spec)) { errors.Add(name + ": no manifest row"); continue; }
                string path = ArtAnimationClipContract.FbxPath(spec);
                if (AssetImporter.GetAtPath(path) == null) { errors.Add(name + ": FBX not in the project " + path); continue; }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                List<string> v = Check(importer, spec);
                if (v.Count > 0)
                {
                    // the pre-written take (takeName / frame range) did not bind: rebuild it from the FBX's own default take, once
                    Debug.LogWarning(LogPrefix + tag + " " + name + " re-binding the take from the FBX default (" + string.Join("; ", v) + ")");
                    importer.clipAnimations = new ModelImporterClipAnimation[0];
                    ArtCharacterContract.Apply(importer);
                    importer.SaveAndReimport();
                    importer = (ModelImporter)AssetImporter.GetAtPath(path);
                    v = Check(importer, spec);
                    rebound++;
                }

                if (v.Count == 0) imported++;
                foreach (string line in v) errors.Add(name + ": " + line);
            }

            foreach (string line in ArtAnimationEventContract.ValidateManifest(manifest))
            {
                if (line.StartsWith(ArtAnimationEventContract.Ap033Prefix, StringComparison.Ordinal)) errors.Add("event contract: " + line);
            }

            if (errors.Count > 0) throw new InvalidOperationException(LogPrefix + tag + " FAILED imported=" + imported + "/" + intake.Length + "\n" + string.Join("\n", errors));

            ArtAnimatorBuilder.BuildZombieOnly();

            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet);
            var controller = set != null ? set.Controller as AnimatorController : null;
            AnimatorState loco = controller != null ? ArtAnimatorBuilder.FindState(controller, 0, C.LocomotionState) : null;
            var tree = loco != null ? loco.motion as BlendTree : null;
            BlendTree gait = null;
            if (tree != null) foreach (ChildMotion cm in tree.children) if (cm.motion is BlendTree bt && bt.name == C.GaitState) gait = bt;
            if (gait == null) throw new InvalidOperationException(LogPrefix + tag + " FAILED: no " + C.GaitState + " tree in " + ArtAnimatorBuilder.ZombieController);

            int mirror = 0;
            var problems = new List<string>();
            ChildMotion[] children = gait.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].mirror) mirror++;
                if (Mathf.Abs(children[i].threshold - i) > 1e-4f) problems.Add("child " + i + " threshold " + children[i].threshold);
                if (i < C.ZombieGaitClips.Length && children[i].mirror != C.IsGaitMirrored(i)) problems.Add("child " + i + " mirror " + children[i].mirror);
                if (i < C.ZombieGaitClips.Length && children[i].motion != set.Clip(C.ZombieGaitClips[i])) problems.Add("child " + i + " motion " + (children[i].motion != null ? children[i].motion.name : "null"));
            }

            var speeds = new List<string>();
            foreach (string name in intake)
            {
                CharacterAnimationSet.Entry e = set.Find(name);
                if (e == null) { problems.Add(name + " not in the set"); continue; }
                if (manifest[name].transition != null)
                {
                    if (e.transition == null || !e.transition.Valid(e.frames)) problems.Add(name + " missing transition data");
                    if (e.unitySpeedMps > 0f) problems.Add(name + " transition calibrated as gait");
                    if (ArtAnimatorBuilder.FindState(controller, 0, name) == null) problems.Add(name + " missing transition state");
                    continue;
                }
                float s = set.SpeedOf(name);
                float spd = manifest[name].speedMps;
                speeds.Add(name.Substring(ArtAnimationEventContract.Ap033Prefix.Length + "zombie_".Length) + "=" + s.ToString("F2", CultureInfo.InvariantCulture) + "/" + spd.ToString("F2", CultureInfo.InvariantCulture));
                // WP-096: the runtime speed is the Unity-rig measurement (unitySpeedMps) inside the calibration's trust band of the Blender spd
                float ratio = spd > 0.01f ? s / spd : 0f;
                if (e.unitySpeedMps <= 0.01f || ratio < ArtZombieStrideCalibration.TrustMin - 1e-3f || ratio > ArtZombieStrideCalibration.TrustMax + 1e-3f)
                    problems.Add(name + " set speed " + s.ToString("F3", CultureInfo.InvariantCulture) + " (unity " + e.unitySpeedMps.ToString("F3", CultureInfo.InvariantCulture) + ") vs spd " + spd.ToString("F3", CultureInfo.InvariantCulture));
            }

            Debug.Log(LogPrefix + tag + " imported=" + imported + " gaitChildren=" + children.Length + " mirror=" + mirror + " rebound=" + rebound + " " + ArtAnimatorBuilder.ZombieCalibrationSummary
                      + " speeds(unity/spd) " + string.Join(" ", speeds) + " controller=" + ArtAnimatorBuilder.ZombieController + " set=" + ArtCharacterPrefabBuilder.ZombieAnimationSet);
            if (imported != intake.Length || children.Length != C.ZombieGaitClips.Length || mirror != 1 || problems.Count > 0)
            {
                throw new InvalidOperationException(LogPrefix + tag + " FAILED gaitChildren=" + children.Length + "/" + C.ZombieGaitClips.Length + " mirror=" + mirror + "\n" + string.Join("\n", problems));
            }
        }

        /// <summary>Contract violations of one imported p1 file (importer, avatar, take) plus the imported clip's own reading.</summary>
        private static List<string> Check(ModelImporter importer, ArtAnimationClipContract.ClipSpec spec)
        {
            var v = new List<string>();
            if (importer == null) { v.Add("no ModelImporter"); return v; }
            v.AddRange(ArtCharacterContract.Validate(importer));
            AnimationClip clip = ArtAnimationClipContract.ClipOf(spec);
            if (clip == null) { v.Add("clip " + spec.name + " not imported"); return v; }
            if (clip.isLooping != spec.loop) v.Add("clip loop != manifest");
            if (spec.transition != null && !spec.transition.Valid(spec.frames)) v.Add("invalid p3 motor contract");
            if (!clip.isHumanMotion) v.Add("clip not humanoid");
            float seconds = (spec.frames - 1) / (float)ArtAnimationClipContract.Fps;
            if (Mathf.Abs(clip.length - seconds) > 0.5f / ArtAnimationClipContract.Fps) v.Add("clip length " + clip.length.ToString("F3", CultureInfo.InvariantCulture) + " s != " + seconds.ToString("F3", CultureInfo.InvariantCulture));
            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
            if (events.Length != spec.events.Count) v.Add("clip events " + events.Length + " != " + spec.events.Count);
            return v;
        }

        // BEGIN WP096 RESTDUMP READONLY — isolated opt-in; never calls the production intake path.
        [Serializable] private sealed class RestDumpPacket
        {
            public string schema = "wp096-avatar-rest-readonly-v1", sourceCommit, unityVersion;
            public string basis = "Unity HumanDescription T-pose local metres; quaternion xyzw; parent availability explicit";
            public string scope = "Existing importer and loaded Avatar records only; no evaluated pose or bind/frame0 parity claim";
            public bool complete;
            public string[] reasons;
            public int omittedReasons, requestedStreams = 4;
            public RestDumpTarget[] targets;
        }
        [Serializable] private sealed class RestDumpTarget
        {
            public string role, assetPath, expectedGuid, actualAssetGuid, fbxSha256, metaSha256;
            public string avatarName, avatarGuid, avatarLocalFileIdR;
            public bool avatarIdentityAvailable, avatarHuman, avatarValid;
            public int loadedSubAssetCount, avatarCount, avatarInstanceId;
            public RestDumpDescription importer, loadedAvatar;
            public RestDumpDependency[] dependencies;
        }
        [Serializable] private sealed class RestDumpDescription
        {
            public string source, parentProvenance;
            public bool available, truncated, humanMappingCountExpected52, parentFieldAvailable;
            public int skeletonCount, humanCount, duplicateNames, duplicateHumanNames, unavailableParents, nonFiniteRows;
            public string upperArmTwistR, lowerArmTwistR, upperLegTwistR, lowerLegTwistR, armStretchR, legStretchR, feetSpacingR;
            public bool hasTranslationDoF;
            public RestDumpBone[] skeleton;
            public RestDumpHuman[] human;
        }
        [Serializable] private sealed class RestDumpBone
        {
            public int order;
            public string name, parentName, parentProvenance;
            public bool parentAvailable, finite, nameTruncated;
            public string[] localPositionR, localRotationR, localScaleR;
        }
        [Serializable] private sealed class RestDumpHuman
        {
            public int order;
            public string humanName, boneName;
            public bool useDefaultValues;
            public string[] limitMinR, limitMaxR, limitCenterR;
            public string axisLengthR;
            public bool finite;
        }
        [Serializable] private sealed class RestDumpDependency
        {
            public string path, guid, sha256, metaSha256, fileStatus;
        }
        private sealed class RestDumpContext
        {
            public readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
            public readonly List<string> Reasons = new List<string>(32);
            public readonly HashSet<string> DependencyPaths = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> FileHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly string ProjectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            public long BytesRead;
            public int OmittedReasons;
            public bool Failed;
            public void Fail(string reason)
            {
                Failed = true;
                if (Reasons.Count < 32) Reasons.Add(reason.Length <= 512 ? reason : reason.Substring(0, 512));
                else OmittedReasons++;
            }
            public void Check()
            {
                if (Watch.Elapsed.TotalSeconds >= 10) throw new TimeoutException("rest-dump-helper-budget-10s");
            }
            public string Hash(string assetPath)
            {
                Check();
                string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectRoot, assetPath));
                if (!full.StartsWith(ProjectRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidOperationException("rest-dump-file-outside-project");
                if (FileHashes.TryGetValue(full, out string cached)) return cached;
                var info = new System.IO.FileInfo(full);
                if (!info.Exists) return null;
                if (info.Length > 16L * 1024 * 1024 || BytesRead + info.Length > 64L * 1024 * 1024)
                    throw new InvalidOperationException("rest-dump-source-byte-cap");
                BytesRead += info.Length;
                using (var stream = System.IO.File.OpenRead(full))
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    cached = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                Check(); FileHashes.Add(full, cached); return cached;
            }
        }

        /// <summary>Separate readonly dump. SOURCE candidate only until ROOT intake and engine grant.</summary>
        public static void DumpRunnerBodyAvatarRestReadonly()
        {
            const string output = "/Users/Erdem/Dev/KADRO/context/projects/deaths-demolition/wp-096/evidence/runner-avatar-rest-readonly-0810-v001-raw";
            string directory = Environment.GetEnvironmentVariable("DD_WP096_AVATAR_REST_DUMP_DIR");
            string source = Environment.GetEnvironmentVariable("DD_R2_COMMIT");
            if (Environment.GetEnvironmentVariable("DD_WP096_AVATAR_REST_DUMP") != "1"
                || string.IsNullOrEmpty(directory) || !System.IO.Path.IsPathRooted(directory)
                || System.IO.Path.GetFullPath(directory) != output || System.IO.Directory.Exists(output)
                || string.IsNullOrEmpty(source) || !System.Text.RegularExpressions.Regex.IsMatch(source, "^[0-9a-f]{40}$"))
                throw new InvalidOperationException("rest-dump-opt-in/source/fresh-output-required");
            var ctx = new RestDumpContext();
            var packet = new RestDumpPacket { sourceCommit = source, unityVersion = Application.unityVersion,
                targets = new[] {
                    new RestDumpTarget { role = "Runner", assetPath = "Assets/_Art/Characters/Animations/AP033_zombie_run_feral_A_v001.fbx", expectedGuid = "467291184d3644baae885070eebc43cc" },
                    new RestDumpTarget { role = "Body", assetPath = "Assets/_Art/Characters/Zombie/zombie_body_A.fbx", expectedGuid = "5d86ff0c59ec8411d8c8aab7aa0422bb" }
                } };
            foreach (RestDumpTarget target in packet.targets)
            {
                try { RestDumpReadTarget(target, ctx); }
                catch (Exception error) { ctx.Fail(target.role + (error is TimeoutException ? ":helper-budget10s" : ":reader-exception:" + error.GetType().Name)); }
            }
            packet.complete = !ctx.Failed; packet.reasons = ctx.Reasons.ToArray(); packet.omittedReasons = ctx.OmittedReasons;
            string json = JsonUtility.ToJson(packet, true);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 1024 * 1024)
            {
                packet.complete = false; packet.targets = new RestDumpTarget[0];
                packet.reasons = new[] { "rest-dump-json-byte-cap-1MiB; payload omitted" };
                json = JsonUtility.ToJson(packet, true);
            }
            System.IO.Directory.CreateDirectory(output);
            using (var file = new System.IO.StreamWriter(new System.IO.FileStream(System.IO.Path.Combine(output, "avatar-rest.json"), System.IO.FileMode.CreateNew), new System.Text.UTF8Encoding(false)))
                file.Write(json);
            Debug.Log("[DD-ART] readonly-avatar-rest complete=" + packet.complete + " targets=2 requestedStreams=4 source=" + source);
            if (!packet.complete) throw new InvalidOperationException("rest-dump-unavailable; inspect bounded diagnostic artifact");
        }
        private static void RestDumpReadTarget(RestDumpTarget target, RestDumpContext ctx)
        {
            ctx.Check();
            target.actualAssetGuid = AssetDatabase.AssetPathToGUID(target.assetPath);
            if (target.actualAssetGuid != target.expectedGuid) { ctx.Fail(target.role + ":asset-guid-mismatch"); return; }
            target.fbxSha256 = ctx.Hash(target.assetPath); target.metaSha256 = ctx.Hash(target.assetPath + ".meta");
            if (target.fbxSha256 == null || target.metaSha256 == null) ctx.Fail(target.role + ":source-file-unavailable");
            var importer = AssetImporter.GetAtPath(target.assetPath) as ModelImporter;
            if (importer == null) { ctx.Fail(target.role + ":importer-unavailable"); return; }
            target.importer = RestDumpReadDescription(importer.humanDescription, target.role + "Importer", ctx);
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(target.assetPath);
            target.loadedSubAssetCount = assets == null ? 0 : assets.Length;
            if (assets == null || assets.Length > 32) { ctx.Fail(target.role + ":loaded-subasset-unavailable-or-cap32"); return; }
            Avatar avatar = null;
            foreach (UnityEngine.Object asset in assets)
                if (asset is Avatar found) { target.avatarCount++; avatar = found; }
            if (target.avatarCount != 1 || avatar == null) { ctx.Fail(target.role + ":expected-exactly-one-loaded-avatar"); return; }
            ctx.Check();
            target.avatarName = RestDumpText(avatar.name, ctx);
            target.avatarHuman = avatar.isHuman; target.avatarValid = avatar.isValid; target.avatarInstanceId = avatar.GetInstanceID();
            target.avatarIdentityAvailable = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(avatar, out string guid, out long localId);
            target.avatarGuid = guid; target.avatarLocalFileIdR = localId.ToString(CultureInfo.InvariantCulture);
            if (!target.avatarIdentityAvailable || guid != target.expectedGuid) ctx.Fail(target.role + ":loaded-avatar-identity-unavailable");
            if (target.role == "Body" && localId != 9000000) ctx.Fail("Body:avatar-reference-localId-mismatch");
            if (!target.avatarHuman || !target.avatarValid) { ctx.Fail(target.role + ":invalid-or-nonhuman-avatar"); return; }
            target.loadedAvatar = RestDumpReadDescription(avatar.humanDescription, target.role + "LoadedAvatar", ctx);
            string[] paths = AssetDatabase.GetDependencies(target.assetPath, false);
            if (paths == null) { ctx.Fail(target.role + ":direct-dependencies-unavailable"); return; }
            Array.Sort(paths, StringComparer.Ordinal);
            if (paths.Length > 64) ctx.Fail(target.role + ":direct-dependency-cap64");
            var dependencies = new List<RestDumpDependency>(Math.Min(paths.Length, 64));
            for (int i = 0; i < paths.Length && i < 64; i++)
            {
                ctx.Check(); string path = paths[i];
                if (path.Length > 512) { ctx.Fail(target.role + ":dependency-path-length-cap"); continue; }
                ctx.DependencyPaths.Add(path);
                if (ctx.DependencyPaths.Count > 64) { ctx.Fail("total-direct-dependency-cap64"); break; }
                var dep = new RestDumpDependency { path = path, guid = AssetDatabase.AssetPathToGUID(path) };
                if (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    dep.sha256 = ctx.Hash(path); dep.metaSha256 = ctx.Hash(path + ".meta");
                    dep.fileStatus = dep.sha256 != null ? "project-source" : "source-file-unavailable";
                    if (dep.sha256 == null) ctx.Fail(target.role + ":dependency-file-unavailable");
                    if (path.StartsWith("Assets/", StringComparison.Ordinal) && (dep.metaSha256 == null || string.IsNullOrEmpty(dep.guid)))
                        ctx.Fail(target.role + ":dependency-meta-or-guid-unavailable");
                }
                else dep.fileStatus = "builtin-or-nonproject; no source-file hash";
                dependencies.Add(dep);
            }
            target.dependencies = dependencies.ToArray();
        }
        private static RestDumpDescription RestDumpReadDescription(HumanDescription hd, string source, RestDumpContext ctx)
        {
            ctx.Check();
            var result = new RestDumpDescription { source = source, available = hd.skeleton != null && hd.human != null,
                skeletonCount = hd.skeleton == null ? 0 : hd.skeleton.Length, humanCount = hd.human == null ? 0 : hd.human.Length,
                upperArmTwistR = RestDumpR(hd.upperArmTwist), lowerArmTwistR = RestDumpR(hd.lowerArmTwist), upperLegTwistR = RestDumpR(hd.upperLegTwist), lowerLegTwistR = RestDumpR(hd.lowerLegTwist),
                armStretchR = RestDumpR(hd.armStretch), legStretchR = RestDumpR(hd.legStretch), feetSpacingR = RestDumpR(hd.feetSpacing), hasTranslationDoF = hd.hasTranslationDoF };
            result.humanMappingCountExpected52 = result.humanCount == 52;
            if (!result.available || result.skeletonCount == 0) ctx.Fail(source + ":description-unavailable");
            if (!result.humanMappingCountExpected52) ctx.Fail(source + ":human-mapping-count-not52");
            result.truncated = result.skeletonCount > 160 || result.humanCount > 64;
            if (result.truncated) ctx.Fail(source + ":description-row-cap");
            var parent = typeof(SkeletonBone).GetField("parentName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            result.parentFieldAvailable = parent != null && parent.FieldType == typeof(string);
            result.parentProvenance = result.parentFieldAvailable ? "SkeletonBone.parentName; fixed-field readonly reflection" : "not-exposed; no inferred parent";
            result.skeleton = new RestDumpBone[Math.Min(result.skeletonCount, 160)];
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < result.skeleton.Length; i++)
            {
                ctx.Check(); SkeletonBone bone = hd.skeleton[i];
                string parentName = result.parentFieldAvailable ? parent.GetValue(bone) as string : null;
                var row = new RestDumpBone { order = i, name = RestDumpText(bone.name, ctx), parentName = RestDumpText(parentName, ctx),
                    parentAvailable = result.parentFieldAvailable && parentName != null, parentProvenance = result.parentProvenance,
                    localPositionR = RestDumpR(bone.position), localRotationR = new[] { RestDumpR(bone.rotation.x), RestDumpR(bone.rotation.y), RestDumpR(bone.rotation.z), RestDumpR(bone.rotation.w) }, localScaleR = RestDumpR(bone.scale),
                    nameTruncated = (bone.name != null && bone.name.Length > 256) || (parentName != null && parentName.Length > 256) };
                row.finite = RestDumpFinite(bone.position.x) && RestDumpFinite(bone.position.y) && RestDumpFinite(bone.position.z)
                    && RestDumpFinite(bone.rotation.x) && RestDumpFinite(bone.rotation.y) && RestDumpFinite(bone.rotation.z) && RestDumpFinite(bone.rotation.w)
                    && RestDumpFinite(bone.scale.x) && RestDumpFinite(bone.scale.y) && RestDumpFinite(bone.scale.z);
                if (!row.finite) { result.nonFiniteRows++; ctx.Fail(source + ":nonfinite-skeleton-row"); }
                if (!row.parentAvailable) { result.unavailableParents++; ctx.Fail(source + ":parent-unavailable"); }
                if (string.IsNullOrEmpty(row.name) || !names.Add(row.name)) { result.duplicateNames++; ctx.Fail(source + ":empty-or-duplicate-bone-name"); }
                result.skeleton[i] = row;
            }
            result.human = new RestDumpHuman[Math.Min(result.humanCount, 64)];
            var humanNames = new HashSet<string>(StringComparer.Ordinal);
            var humanBones = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < result.human.Length; i++)
            {
                ctx.Check(); HumanBone human = hd.human[i]; HumanLimit limit = human.limit;
                result.human[i] = new RestDumpHuman { order = i, humanName = RestDumpText(human.humanName, ctx), boneName = RestDumpText(human.boneName, ctx),
                    useDefaultValues = limit.useDefaultValues, limitMinR = RestDumpR(limit.min), limitMaxR = RestDumpR(limit.max), limitCenterR = RestDumpR(limit.center), axisLengthR = RestDumpR(limit.axisLength),
                    finite = RestDumpFinite(limit.min) && RestDumpFinite(limit.max) && RestDumpFinite(limit.center) && RestDumpFinite(limit.axisLength) };
                if (!result.human[i].finite) ctx.Fail(source + ":nonfinite-human-limit");
                if (string.IsNullOrEmpty(human.humanName) || string.IsNullOrEmpty(human.boneName)
                    || !humanNames.Add(human.humanName) || !humanBones.Add(human.boneName))
                { result.duplicateHumanNames++; ctx.Fail(source + ":empty-or-duplicate-human-mapping"); }
            }
            return result;
        }
        private static string RestDumpText(string value, RestDumpContext ctx)
        {
            if (value == null || value.Length <= 256) return value;
            ctx.Fail("record-text-cap256"); return value.Substring(0, 256);
        }
        private static bool RestDumpFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool RestDumpFinite(Vector3 value) => RestDumpFinite(value.x) && RestDumpFinite(value.y) && RestDumpFinite(value.z);
        private static string RestDumpR(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string[] RestDumpR(Vector3 value) => new[] { RestDumpR(value.x), RestDumpR(value.y), RestDumpR(value.z) };
        // END WP096 RESTDUMP READONLY
    }
}
