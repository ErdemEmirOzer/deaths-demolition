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
    /// WP-096 (S-03) · AP-033 zombie locomotion intake, one batch step for every integrated part (<see cref="Deliveries"/>: p1 4 + p2 8):
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
    ///   5. logs the verifiable line <c>[DD-ART] ap033.p1+p2 imported=12 gaitChildren=22 mirror=1 rebound=0 calibrated=… …</c>. Any failure throws.
    /// </summary>
    public static class ArtAp033Intake
    {
        public const string LogPrefix = "[DD-ART] ";
        public static readonly string[] P1Clips = { C.ZombieWalkLurchAV4, C.ZombieWalkLimpAV4, C.ZombieRunFeralAV4, C.ZombieWalkStompAV4 };
        public static readonly string[] P2Clips =
        {
            C.ZombieWalkLurchBV4, C.ZombieWalkDragAV4, C.ZombieWalkDragBV4, C.ZombieRunFeralBV4, C.ZombieRunStumbleAV4, C.ZombieWalkHeavyAV4, C.ZombieWalkHeavyBV4, C.ZombieWalkStompBV4
        };

        /// <summary>The integrated AP-033 parts (p3 = the transitions: a later round, with the start / stop / turn states).</summary>
        public static readonly (string part, string[] clips)[] Deliveries = { ("p1", P1Clips), ("p2", P2Clips) };

        [MenuItem("DD/Art/Run AP-033 Zombie Locomotion p1+p2 (WP-096)", priority = 334)]
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
            if (!clip.isLooping) v.Add("clip not looping");
            if (!clip.isHumanMotion) v.Add("clip not humanoid");
            float seconds = (spec.frames - 1) / (float)ArtAnimationClipContract.Fps;
            if (Mathf.Abs(clip.length - seconds) > 0.5f / ArtAnimationClipContract.Fps) v.Add("clip length " + clip.length.ToString("F3", CultureInfo.InvariantCulture) + " s != " + seconds.ToString("F3", CultureInfo.InvariantCulture));
            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
            if (events.Length != spec.events.Count) v.Add("clip events " + events.Length + " != " + spec.events.Count);
            return v;
        }
    }
}
