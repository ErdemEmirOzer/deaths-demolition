using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Characters;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-018 · ADR-018-1 · The AP-009 clip import (AnimationClipEventImporter): every FBX under
    /// <c>Assets/_Art/Characters/Animations/</c> is a Humanoid animation-only file (v1.1 rig
    /// rules) whose take is configured FROM THE MANIFEST next to it
    /// (<c>AP009-clips-manifest.json</c>, a copy of the AP-009 export):
    ///   * loop time = <c>loop</c> (11 loops, seam 0.0°; loop pose off), root locked (no root motion);
    ///   * AnimationEvents = <c>events</c> (frame / 30 s, function = the manifest name, which
    ///     <see cref="CharacterAnimationEvents"/> implements);
    ///   * additive clips get the additive reference pose = frame 0 (deltas from rest);
    ///   * the take is renamed to the manifest clip name so the controller binds by name.
    /// Idempotent: nothing is written when the importer already matches (a compliant asset
    /// never dirties its .meta); <see cref="Validate"/> is the gate.
    /// </summary>
    public static class ArtAnimationClipContract
    {
        public const string AnimationsFolderName = "Animations";
        public const string AnimationsFolder = ArtImportContract.CharactersRoot + "/" + AnimationsFolderName;
        public const string ManifestPath = AnimationsFolder + "/AP009-clips-manifest.json";
        public const string ManifestSchema = "AP009.clips-manifest.v006";   // WP-032E: AP-009F v006 (85 clips; 23 pose re-keys: aim A shoulder / B compact / C hip class poses, stumble, death_ultra — frames/events as v005b)
        public const int Fps = CharacterAnimatorContract.Fps;

        /// <summary>
        /// WP-032: the AP-009 armature-only export faces the opposite way from the AP-006/007 body
        /// files once both are on the v1 axis conversion — measured: the retargeted body's forward
        /// · the model's forward = −0.96 (the crowd walked and struck facing away from its own
        /// transform through WP-018). The clip's root rotation is offset by 180° at import so the
        /// animated body faces the model's +Z like the rig-test clips did.
        /// </summary>
        public const float RootRotationOffsetDegrees = 180f;

        public sealed class ClipSpec
        {
            public string name;
            public string fbx;
            public string target;
            public int frames;
            public float seconds;
            public bool loop;
            public bool additive;
            public string layer;
            public string mask;
            public float speedMps;
            public string fit;
            public string tier;
            public bool optional;
            /// <summary>WP-032B: manifest validation.foot_contact_A stance frames (min over both feet); 0 = not given (whole-window calibration).</summary>
            public int stanceFrames;
            public readonly List<KeyValuePair<string, int>> events = new List<KeyValuePair<string, int>>();

            /// <summary>"name=frame;…" in manifest order (CharacterAnimationSet.Entry.events).</summary>
            public string EventsText()
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < events.Count; i++)
                {
                    if (i > 0) sb.Append(';');
                    sb.Append(events[i].Key).Append('=').Append(events[i].Value);
                }

                return sb.ToString();
            }
        }

        private static Dictionary<string, ClipSpec> s_cache;
        private static string s_cacheText;

        public static bool IsAnimationClip(string assetPath)
        {
            return ArtCharacterContract.IsCharacterModel(assetPath)
                   && assetPath.Replace('\\', '/').StartsWith(AnimationsFolder + "/", StringComparison.Ordinal);
        }

        /// <summary>The manifest, parsed (cached by text). Null when the folder has no manifest.</summary>
        public static Dictionary<string, ClipSpec> Manifest()
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), ManifestPath);
            if (!File.Exists(full)) return null;
            string text = File.ReadAllText(full);
            if (s_cache != null && s_cacheText == text) return s_cache;

            var root = ArtJson.AsObject(ArtJson.Parse(text), "manifest");
            string schema = ArtJson.AsString(ArtJson.Require(root, "schema"), "schema");
            if (schema != ManifestSchema) throw new InvalidDataException("[ArtAnimation] manifest schema " + schema + " != " + ManifestSchema);
            int fps = ArtJson.AsInt(ArtJson.Require(root, "fps"), "fps");
            if (fps != Fps) throw new InvalidDataException("[ArtAnimation] manifest fps " + fps + " != " + Fps);

            var map = new Dictionary<string, ClipSpec>(StringComparer.Ordinal);
            foreach (object o in ArtJson.AsList(ArtJson.Require(root, "clips"), "clips"))
            {
                var c = ArtJson.AsObject(o, "clip");
                var spec = new ClipSpec
                {
                    name = ArtJson.AsString(ArtJson.Require(c, "clip"), "clip"),
                    fbx = ArtJson.AsString(ArtJson.Require(c, "fbx"), "fbx"),
                    target = ArtJson.AsString(ArtJson.Require(c, "target"), "target"),
                    frames = ArtJson.AsInt(ArtJson.Require(c, "frames"), "frames"),
                    seconds = ArtJson.AsFloat(ArtJson.Require(c, "seconds"), "seconds"),
                    loop = ArtJson.AsBool(ArtJson.Require(c, "loop"), "loop"),
                    additive = ArtJson.AsBool(ArtJson.Require(c, "additive"), "additive"),
                    layer = ArtJson.AsString(ArtJson.Require(c, "layer"), "layer"),
                    mask = ArtJson.AsString(ArtJson.Require(c, "mask"), "mask"),
                    optional = ArtJson.Get(c, "optional") is bool b && b
                };
                object speed = ArtJson.Get(c, "speed_mps");
                spec.speedMps = speed == null ? 0f : ArtJson.AsFloat(speed, "speed_mps");
                object fit = ArtJson.Get(c, "fit");
                spec.fit = fit == null ? null : ArtJson.AsString(fit, "fit");
                object tier = ArtJson.Get(c, "tier");
                spec.tier = tier == null ? null : ArtJson.AsString(tier, "tier");
                object validation = ArtJson.Get(c, "validation");
                object contact = validation is Dictionary<string, object> vd ? ArtJson.Get(vd, "foot_contact_A") : null;
                if (contact is Dictionary<string, object> cd)
                {
                    foreach (KeyValuePair<string, object> side in cd)
                    {
                        if (!(side.Value is Dictionary<string, object> sd)) continue;
                        object sf = ArtJson.Get(sd, "stance_frames");
                        if (sf == null) continue;
                        int n = ArtJson.AsInt(sf, "stance_frames");
                        spec.stanceFrames = spec.stanceFrames == 0 ? n : Math.Min(spec.stanceFrames, n);
                    }
                }

                object ev = ArtJson.Get(c, "events");
                if (ev != null)
                {
                    foreach (KeyValuePair<string, object> kv in ArtJson.AsObject(ev, "events"))
                    {
                        spec.events.Add(new KeyValuePair<string, int>(kv.Key, ArtJson.AsInt(kv.Value, kv.Key)));
                    }
                }

                map[spec.name] = spec;
            }

            s_cache = map;
            s_cacheText = text;
            return map;
        }

        /// <summary>The manifest entry for an Animations/ FBX (by file name), or null.</summary>
        public static ClipSpec SpecFor(string assetPath)
        {
            Dictionary<string, ClipSpec> map = Manifest();
            if (map == null) return null;
            string file = Path.GetFileName(assetPath);
            foreach (ClipSpec s in map.Values)
            {
                if (s.fbx == file) return s;
            }

            return null;
        }

        /// <summary>Asset path of a clip's FBX.</summary>
        public static string FbxPath(ClipSpec spec)
        {
            return AnimationsFolder + "/" + spec.fbx;
        }

        /// <summary>
        /// Configure the take from the manifest. Returns true if anything changed; a no-op while
        /// the file's takes are not parsed yet (first OnPreprocessModel) or without a manifest.
        /// </summary>
        public static bool ApplyClips(ModelImporter importer)
        {
            ClipSpec spec = SpecFor(importer.assetPath);
            if (spec == null) return false;

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return false;

            // One take per file (AP-009 export); the first take is the clip.
            var c = clips[0];
            bool changed = false;
            changed |= Set(ref c, spec);
            AnimationEvent[] wanted = EventsOf(spec, c.firstFrame, c.lastFrame);
            if (!SameEvents(c.events, wanted))
            {
                c.events = wanted;
                changed = true;
            }

            if (changed || clips.Length != 1 || importer.clipAnimations == null || importer.clipAnimations.Length != 1)
            {
                importer.clipAnimations = new[] { c };
                return true;
            }

            return false;
        }

        /// <summary>Diagnostics: the first field ApplyClips would rewrite ("" when compliant).</summary>
        public static string WhyChanged(ModelImporter importer)
        {
            ClipSpec spec = SpecFor(importer.assetPath);
            if (spec == null) return "no spec";
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length != 1) return "clipAnimations " + (clips == null ? "null" : clips.Length.ToString());
            var c = clips[0];
            if (c.name != spec.name) return "name " + c.name;
            if (c.loopTime != spec.loop) return "loopTime";
            if (c.loopPose) return "loopPose";
            if (!c.lockRootRotation || !c.lockRootHeightY || !c.lockRootPositionXZ) return "lock";
            if (!c.keepOriginalOrientation || !c.keepOriginalPositionY || !c.keepOriginalPositionXZ) return "keepOriginal";
            if (Math.Abs(c.rotationOffset - RootRotationOffsetDegrees) > 0.01f) return "rotationOffset " + c.rotationOffset;
            if (c.hasAdditiveReferencePose != spec.additive) return "additive";
            if (spec.additive && Math.Abs(c.additiveReferencePoseFrame) > 0.001f) return "additiveFrame " + c.additiveReferencePoseFrame;
            if (!SameEvents(c.events, EventsOf(spec, c.firstFrame, c.lastFrame))) return "events " + Describe(c.events);
            return "";
        }

        private static bool Set(ref ModelImporterClipAnimation c, ClipSpec spec)
        {
            bool changed = false;
            if (c.name != spec.name) { c.name = spec.name; changed = true; }
            if (c.loopTime != spec.loop) { c.loopTime = spec.loop; changed = true; }
            if (c.loopPose) { c.loopPose = false; changed = true; }
            if (!c.lockRootRotation) { c.lockRootRotation = true; changed = true; }
            if (!c.lockRootHeightY) { c.lockRootHeightY = true; changed = true; }
            if (!c.lockRootPositionXZ) { c.lockRootPositionXZ = true; changed = true; }
            if (!c.keepOriginalOrientation) { c.keepOriginalOrientation = true; changed = true; }
            if (Math.Abs(c.rotationOffset - RootRotationOffsetDegrees) > 0.01f) { c.rotationOffset = RootRotationOffsetDegrees; changed = true; }
            if (!c.keepOriginalPositionY) { c.keepOriginalPositionY = true; changed = true; }
            if (!c.keepOriginalPositionXZ) { c.keepOriginalPositionXZ = true; changed = true; }
            if (c.hasAdditiveReferencePose != spec.additive) { c.hasAdditiveReferencePose = spec.additive; changed = true; }
            if (spec.additive && Math.Abs(c.additiveReferencePoseFrame) > 0.001f) { c.additiveReferencePoseFrame = 0f; changed = true; }
            return changed;
        }

        /// <summary>
        /// The manifest events as importer AnimationEvents, in frame order. IMPORTER TIME IS
        /// NORMALISED (0..1 over the take's frame range) — measured: a time written in seconds
        /// landed at seconds² and stretched every clip's length to it (WP-018 iteration 1).
        /// The imported AnimationClip then carries the event at frame / fps seconds.
        /// </summary>
        public static AnimationEvent[] EventsOf(ClipSpec spec, float firstFrame, float lastFrame)
        {
            float span = Mathf.Max(1f, lastFrame - firstFrame);
            var list = new List<AnimationEvent>(spec.events.Count);
            foreach (KeyValuePair<string, int> e in spec.events)
            {
                list.Add(new AnimationEvent { functionName = e.Key, time = Mathf.Clamp01((e.Value - firstFrame) / span), messageOptions = SendMessageOptions.DontRequireReceiver });
            }

            list.Sort((a, b) => a.time != b.time ? a.time.CompareTo(b.time) : string.CompareOrdinal(a.functionName, b.functionName));
            return list.ToArray();
        }

        private static bool SameEvents(AnimationEvent[] a, AnimationEvent[] b)
        {
            if (a == null) a = new AnimationEvent[0];
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].functionName != b[i].functionName || Math.Abs(a[i].time - b[i].time) > 1e-3f) return false;
            }

            return true;
        }

        /// <summary>Every field that differs from the manifest, as "field: actual != expected". Empty = compliant.</summary>
        public static List<string> Validate(ModelImporter importer)
        {
            var v = new List<string>();
            ClipSpec spec = SpecFor(importer.assetPath);
            if (spec == null)
            {
                v.Add("manifest: no entry for " + Path.GetFileName(importer.assetPath));
                return v;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length != 1)
            {
                v.Add("clipAnimations: " + (clips == null ? 0 : clips.Length) + " != 1");
                return v;
            }

            var c = clips[0];
            if (c.name != spec.name) v.Add("clip.name: " + c.name + " != " + spec.name);
            if (c.loopTime != spec.loop) v.Add("clip.loopTime: " + c.loopTime + " != " + spec.loop);
            if (c.loopPose) v.Add("clip.loopPose: true != false");
            if (!c.lockRootRotation || !c.lockRootHeightY || !c.lockRootPositionXZ) v.Add("clip: root not locked (no root motion)");
            if (Math.Abs(c.rotationOffset - RootRotationOffsetDegrees) > 0.01f) v.Add("clip.rotationOffset: " + c.rotationOffset + " != " + RootRotationOffsetDegrees);
            if (c.hasAdditiveReferencePose != spec.additive) v.Add("clip.hasAdditiveReferencePose: " + c.hasAdditiveReferencePose + " != " + spec.additive);
            if (!SameEvents(c.events, EventsOf(spec, c.firstFrame, c.lastFrame))) v.Add("clip.events: " + Describe(c.events) + " != " + spec.EventsText());
            if (Mathf.Abs(c.lastFrame - c.firstFrame - (spec.frames - 1)) > 0.5f) v.Add("clip.frames: " + c.firstFrame + ".." + c.lastFrame + " != " + (spec.frames - 1));
            return v;
        }

        private static string Describe(AnimationEvent[] events)
        {
            if (events == null || events.Length == 0) return "(none)";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < events.Length; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(events[i].functionName).Append('@').Append(events[i].time.ToString("F3"));
            }

            return sb.ToString();
        }

        /// <summary>The imported AnimationClip of a manifest clip (by take name).</summary>
        public static AnimationClip ClipOf(ClipSpec spec)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetRepresentationsAtPath(FbxPath(spec)))
            {
                if (o is AnimationClip clip && clip.name == spec.name) return clip;
            }

            return null;
        }
    }
}
