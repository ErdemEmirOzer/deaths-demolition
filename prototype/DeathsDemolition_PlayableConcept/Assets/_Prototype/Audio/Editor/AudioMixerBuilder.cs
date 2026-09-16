using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace DD.Prototype.Audio.EditorTools
{
    /// <summary>
    /// WP-021 · Builds <c>Assets/_Prototype/Audio/Resources/DD_AudioMixer.mixer</c>.
    ///
    /// Unity has no public API for authoring an AudioMixer asset, so this drives the
    /// internal <c>UnityEditor.Audio.AudioMixerController</c> through reflection: create the
    /// controller asset, add the five child groups under Master, expose each group's
    /// volume under a stable name, clone the four snapshots and write their group volumes.
    /// Idempotent: an existing asset with the expected tree is left untouched (its GUID,
    /// and every scene/prefab reference to it, survives); anything else is rebuilt in place.
    ///
    /// The runtime never depends on this having run - <see cref="AudioMixerBridge"/> is a
    /// no-op without the asset - but the EditMode test does, so the mixer is a checked-in
    /// build product, not something regenerated on import.
    /// </summary>
    public static class AudioMixerBuilder
    {
        public const string AssetPath = "Assets/_Prototype/Audio/Resources/DD_AudioMixer.mixer";

        /// <summary>Per-snapshot group volumes in dB, groups in <see cref="AudioMixerBridge.GroupNames"/> order (Master first).</summary>
        private static readonly float[][] SnapshotVolumesDb =
        {
            //           Master  Music  Ambience  SFX-World  SFX-Player  UI
            new[] { 0f,    0f,    0f,       0f,        0f,       0f },   // Run
            new[] { 0f,   -6f,    2f,      -4f,       -4f,       0f },   // RoundBreak: music pulls back, ambience forward
            new[] { 0f,  -14f,   -10f,     -80f,      -80f,      0f },   // Pause: world silent, UI audible (listener is paused anyway)
            new[] { 0f,   -8f,   -3f,      -12f,      -12f,      0f }    // Menu
        };

        [MenuItem("DD/Audio/Build Mixer")]
        public static void BuildMenu()
        {
            string report = Build();
            Debug.Log("[AudioMixerBuilder] " + report);
        }

        /// <summary>Batchmode entry (-executeMethod). Throws on failure so the process exits non-zero.</summary>
        public static void BuildBatch()
        {
            string report = Build();
            Debug.Log("[AudioMixerBuilder] " + report);
            if (!Verify(out string why))
            {
                throw new InvalidOperationException("Mixer verification failed: " + why);
            }

            Debug.Log("[AudioMixerBuilder] VERIFY OK");
        }

        public static string Build()
        {
            if (Verify(out string _))
            {
                return "up to date: " + AssetPath;
            }

            Type controllerType = FindType("UnityEditor.Audio.AudioMixerController");
            Type groupType = FindType("UnityEditor.Audio.AudioMixerGroupController");
            Type snapshotType = FindType("UnityEditor.Audio.AudioMixerSnapshotController");
            Type exposedType = FindType("UnityEditor.Audio.ExposedAudioParameter");
            if (controllerType == null || groupType == null || snapshotType == null || exposedType == null)
            {
                throw new InvalidOperationException("Internal AudioMixerController API not found in this Unity version.");
            }

            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath) != null)
            {
                AssetDatabase.DeleteAsset(AssetPath);
            }

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AssetPath));

            // --- controller ---------------------------------------------------------
            MethodInfo create = controllerType.GetMethod("CreateMixerControllerAtPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (create == null)
            {
                throw new InvalidOperationException("AudioMixerController.CreateMixerControllerAtPath missing.");
            }

            object controller = create.Invoke(null, new object[] { AssetPath });
            if (controller == null)
            {
                throw new InvalidOperationException("CreateMixerControllerAtPath returned null.");
            }

            object master = Get(controllerType, controller, "masterGroup");
            Set(master, "name", AudioMixerBridge.GroupMaster);

            MethodInfo createGroup = controllerType.GetMethod("CreateNewGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo addChild = controllerType.GetMethod("AddChildToParent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo guidForVolume = groupType.GetMethod("GetGUIDForVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (createGroup == null || addChild == null || guidForVolume == null)
            {
                throw new InvalidOperationException("AudioMixerController group API missing (CreateNewGroup/AddChildToParent/GetGUIDForVolume).");
            }

            var groups = new List<object> { master };
            for (int i = 1; i < AudioMixerBridge.GroupNames.Length; i++)
            {
                object group = createGroup.Invoke(controller, new object[] { AudioMixerBridge.GroupNames[i], false });
                addChild.Invoke(controller, new[] { group, master });
                groups.Add(group);
            }

            // --- exposed volumes ------------------------------------------------------
            Array exposed = Array.CreateInstance(exposedType, groups.Count);
            FieldInfo exposedGuid = exposedType.GetField("guid");
            FieldInfo exposedName = exposedType.GetField("name");
            for (int i = 0; i < groups.Count; i++)
            {
                object param = Activator.CreateInstance(exposedType);
                exposedGuid.SetValue(param, guidForVolume.Invoke(groups[i], null));
                exposedName.SetValue(param, AudioMixerBridge.VolumeParams[i]);
                exposed.SetValue(param, i);
            }

            SetProp(controllerType, controller, "exposedParameters", exposed);

            // --- snapshots ------------------------------------------------------------
            // The controller is created with one snapshot; clone it for the rest and name all four.
            Array existing = (Array)Get(controllerType, controller, "snapshots");
            object first = existing.GetValue(0);
            Set(first, "name", AudioMixerBridge.SnapshotNames[0]);

            MethodInfo clone = controllerType.GetMethod("CloneNewSnapshotFromTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (clone == null)
            {
                throw new InvalidOperationException("AudioMixerController.CloneNewSnapshotFromTarget missing.");
            }

            var snapshots = new List<object> { first };
            for (int i = 1; i < AudioMixerBridge.SnapshotNames.Length; i++)
            {
                object snap = clone.Invoke(controller, new object[] { false });
                if (snap == null)
                {
                    // Some versions return void and append to the array instead.
                    Array now = (Array)Get(controllerType, controller, "snapshots");
                    snap = now.GetValue(now.Length - 1);
                }

                Set(snap, "name", AudioMixerBridge.SnapshotNames[i]);
                snapshots.Add(snap);
            }

            MethodInfo setValue = snapshotType.GetMethod("SetValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int s = 0; s < snapshots.Count; s++)
            {
                for (int g = 0; g < groups.Count; g++)
                {
                    object guid = guidForVolume.Invoke(groups[g], null);
                    setValue.Invoke(snapshots[s], new[] { guid, (object)SnapshotVolumesDb[s][g] });
                }
            }

            SetProp(controllerType, controller, "startSnapshot", snapshots[3]);
            TrySetProp(controllerType, controller, "TargetSnapshot", snapshots[0]);

            EditorUtility.SetDirty((UnityEngine.Object)controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

            return Verify(out string why) ? "built: " + AssetPath : "built with gaps: " + why;
        }

        /// <summary>True when the asset exists with every expected group and snapshot.</summary>
        public static bool Verify(out string why)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath);
            if (mixer == null)
            {
                why = "asset missing";
                return false;
            }

            for (int i = 0; i < AudioMixerBridge.GroupNames.Length; i++)
            {
                bool found = false;
                AudioMixerGroup[] matches = mixer.FindMatchingGroups(AudioMixerBridge.GroupNames[i]);
                for (int j = 0; j < matches.Length; j++)
                {
                    if (matches[j].name == AudioMixerBridge.GroupNames[i]) { found = true; break; }
                }

                if (!found)
                {
                    why = "group missing: " + AudioMixerBridge.GroupNames[i];
                    return false;
                }
            }

            for (int i = 0; i < AudioMixerBridge.SnapshotNames.Length; i++)
            {
                if (mixer.FindSnapshot(AudioMixerBridge.SnapshotNames[i]) == null)
                {
                    why = "snapshot missing: " + AudioMixerBridge.SnapshotNames[i];
                    return false;
                }
            }

            for (int i = 0; i < AudioMixerBridge.VolumeParams.Length; i++)
            {
                if (!mixer.GetFloat(AudioMixerBridge.VolumeParams[i], out float _))
                {
                    why = "exposed parameter missing: " + AudioMixerBridge.VolumeParams[i];
                    return false;
                }
            }

            why = string.Empty;
            return true;
        }

        // ---- reflection helpers ------------------------------------------------------

        private static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static object Get(Type type, object target, string member)
        {
            PropertyInfo p = type.GetProperty(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                return p.GetValue(target);
            }

            FieldInfo f = type.GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                return f.GetValue(target);
            }

            throw new InvalidOperationException(type.Name + "." + member + " not found.");
        }

        private static void SetProp(Type type, object target, string member, object value)
        {
            if (!TrySetProp(type, target, member, value))
            {
                throw new InvalidOperationException(type.Name + "." + member + " not settable.");
            }
        }

        private static bool TrySetProp(Type type, object target, string member, object value)
        {
            PropertyInfo p = type.GetProperty(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite)
            {
                p.SetValue(target, value);
                return true;
            }

            FieldInfo f = type.GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                f.SetValue(target, value);
                return true;
            }

            return false;
        }

        private static void Set(object unityObject, string member, object value)
        {
            if (member == "name" && unityObject is UnityEngine.Object o)
            {
                o.name = (string)value;
                return;
            }

            SetProp(unityObject.GetType(), unityObject, member, value);
        }
    }
}
