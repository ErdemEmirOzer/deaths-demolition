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
            // WP-057 mix pass v2 (sound-designer proposal, calibrated by the PlayMode phase meter):
            // the score sits under the fight, comes forward in the break and the menu, UI trims 3-4 dB.
            // WP-060 v2.2 (Fable listening round on the Player output capture): the fight read
            // −17.9 LUFS-I / −0.8 dBTP at the speakers, ~3 dB above the PlayMode meter, so the Run
            // snapshot comes down 1-2 dB per group (target ≈ −19.5 LUFS-I, TP ≈ −2.5) and the menu
            // ambience comes up 3 dB so results/menu are not a 10 dB hole under the fight.
            //           Master  Music  Ambience  SFX-World  SFX-Player  UI
            // WP-072 B7 (user 01.10, A/B kit option B): the player's weapon forward of the horde — SFX-Player +3, SFX-World −1 in Run and
            // RoundBreak (weapon margin in a 6-zombie fight TopDown −2.4 → +1.6 LU, FP −0.8 → +3.2; the master compressor is not engaged).
            // Run: WP-060 −5/−4/0/−1/−3 → −6/−5/−2/−2/−4. WP-072 B7 (user 01.10, option B: World −1 / Player +3 against the rest) then
            // −1 dB on every content group for A4 (dense-40 TP/FP read −17.7/−17.6 LUFS-I; Master is pinned by the user's volume
            // setting at runtime, so a snapshot Master value never applies): Music −7, Ambience −6, World −4, Player 0, UI −4.
            new[] { 0f,   -7f,   -6f,      -4f,        0f,      -4f },
            new[] { 0f,   -3f,   -3f,      -7f,        0f,       3f },   // RoundBreak: stinger + shop UI forward, world back (WP-058B: UI −2 → 0; WP-068 s11: UI +3, the v3 shop cards / stamps ≥ 6 LU over the break bed; WP-072 B7: World −6 → −7, Player −3 → 0)
            new[] { 0f,  -14f,  -12f,     -18f,      -18f,      -2f },   // Pause: world far back, UI audible (listener is paused anyway)
            new[] { 0f,   -4f,   -7f,     -80f,      -80f,       4f }    // Menu: menu loop + UI, no world (WP-058B: UI −3 → −1; WP-060: Amb −10 → −7; WP-068 s11: UI −1 → +4 - the menu bed reads −27.8 LUFS-I, v3 cues ≥ 6 LU over it)
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

            // WP-021C: an asset whose tree is right but which predates the master limiter is
            // upgraded in place (same GUID), not rebuilt. WP-057: same for the snapshot mix table.
            if (VerifyTree(out string _))
            {
                string limiter = HasMasterLimiter(out string _) ? "limiter: present" : EnsureMasterLimiter();
                string mix = EnsureSnapshotMix();
                return Verify(out string why0) ? "upgraded in place: " + AssetPath + " · " + limiter + " · " + mix : "upgrade with gaps: " + why0 + " · " + limiter + " · " + mix;
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

            string limiterReport = EnsureMasterLimiter();
            return Verify(out string why) ? "built: " + AssetPath + " · " + limiterReport : "built with gaps: " + why + " · " + limiterReport;
        }

        // ---- WP-021C · master limiter ---------------------------------------------------

        /// <summary>Unity's built-in "Compressor" effect (the closest thing to a limiter the mixer ships: fixed hard knee, ~10 ms attack floor).</summary>
        public const string LimiterEffectName = "Compressor";

        /// <summary>Exposed parameter names for the master limiter; Verify requires them.</summary>
        public const string LimiterThresholdParam = "MasterLimiterThreshold";
        public const string LimiterAttackParam = "MasterLimiterAttack";
        public const string LimiterReleaseParam = "MasterLimiterRelease";

        /// <summary>
        /// Limiter settings (PROTOTYPE-ONLY / INVENTED; sound-engineer, WP-021C): threshold −8 dB
        /// so a shotgun + explosion + gong sum is caught, fastest attack the effect offers,
        /// 90 ms release (short enough that a taiko hit does not pump the bed), no make-up gain -
        /// the clips already sit at their LUFS targets and a limiter that adds gain is a compressor.
        /// </summary>
        public const float LimiterThresholdDb = -8f;
        public const float LimiterAttackMs = 10f;
        public const float LimiterReleaseMs = 90f;

        /// <summary>Inserts the Compressor on Master (idempotent), sets its parameters in every snapshot and exposes them.</summary>
        public static string EnsureMasterLimiter()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath);
            if (mixer == null)
            {
                return "limiter: asset missing";
            }

            if (HasMasterLimiter(out string _))
            {
                return "limiter: present";
            }

            Type controllerType = FindType("UnityEditor.Audio.AudioMixerController");
            Type groupType = FindType("UnityEditor.Audio.AudioMixerGroupController");
            Type effectType = FindType("UnityEditor.Audio.AudioMixerEffectController");
            Type snapshotType = FindType("UnityEditor.Audio.AudioMixerSnapshotController");
            Type exposedType = FindType("UnityEditor.Audio.ExposedAudioParameter");
            if (controllerType == null || groupType == null || effectType == null || snapshotType == null || exposedType == null)
            {
                throw new InvalidOperationException("Internal AudioMixer effect API not found in this Unity version.");
            }

            object controller = mixer;
            object master = Get(controllerType, controller, "masterGroup");

            object effect = Activator.CreateInstance(effectType, new object[] { LimiterEffectName });
            MethodInfo prealloc = effectType.GetMethod("PreallocateGUIDs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            prealloc?.Invoke(effect, null);

            // Insert after Attenuation (index = current count) so the fader stays pre-limiter.
            Array effects = (Array)Get(groupType, master, "effects");
            MethodInfo insert = groupType.GetMethod("InsertEffect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (insert == null)
            {
                throw new InvalidOperationException("AudioMixerGroupController.InsertEffect missing.");
            }

            insert.Invoke(master, new[] { effect, (object)effects.Length });
            AssetDatabase.AddObjectToAsset((UnityEngine.Object)effect, (UnityEngine.Object)controller);

            // Parameter GUIDs by the effect's own parameter names (Threshold / Attack / Release / Make up gain).
            MethodInfo guidFor = effectType.GetMethod("GetGUIDForParameter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setValue = snapshotType.GetMethod("SetValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array snapshots = (Array)Get(controllerType, controller, "snapshots");
            string[] names = LimiterParameterNames(effect, effectType);
            var values = new Dictionary<string, float>
            {
                { "Threshold", LimiterThresholdDb }, { "Attack", LimiterAttackMs }, { "Release", LimiterReleaseMs }, { "Make up gain", 0f }
            };
            var exposedNames = new Dictionary<string, string>
            {
                { "Threshold", LimiterThresholdParam }, { "Attack", LimiterAttackParam }, { "Release", LimiterReleaseParam }
            };

            var report = new System.Text.StringBuilder("limiter: " + LimiterEffectName + " on Master · params [" + string.Join(", ", names) + "]");
            Array exposed = (Array)Get(controllerType, controller, "exposedParameters");
            var exposedList = new List<object>();
            foreach (object e in exposed) exposedList.Add(e);
            FieldInfo exposedGuid = exposedType.GetField("guid");
            FieldInfo exposedName = exposedType.GetField("name");

            foreach (KeyValuePair<string, float> kv in values)
            {
                object guid = guidFor.Invoke(effect, new object[] { kv.Key });
                if (guid == null)
                {
                    report.Append(" · no guid for '" + kv.Key + "'");
                    continue;
                }

                foreach (object snap in snapshots)
                {
                    setValue.Invoke(snap, new[] { guid, (object)kv.Value });
                }

                if (exposedNames.TryGetValue(kv.Key, out string exposeAs))
                {
                    object param = Activator.CreateInstance(exposedType);
                    exposedGuid.SetValue(param, guid);
                    exposedName.SetValue(param, exposeAs);
                    exposedList.Add(param);
                }

                report.Append(" · " + kv.Key + " = " + kv.Value);
            }

            Array exposedNow = Array.CreateInstance(exposedType, exposedList.Count);
            for (int i = 0; i < exposedList.Count; i++) exposedNow.SetValue(exposedList[i], i);
            SetProp(controllerType, controller, "exposedParameters", exposedNow);

            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            return report.ToString();
        }

        private static string[] LimiterParameterNames(object effect, Type effectType)
        {
            // MixerEffectDefinitions.GetEffectParameters(effectName) → MixerParameterDefinition[] { name, ... }
            Type defs = FindType("UnityEditor.Audio.MixerEffectDefinitions");
            MethodInfo get = defs?.GetMethod("GetEffectParameters", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (get == null)
            {
                return new[] { "?" };
            }

            Array parameters = get.Invoke(null, new object[] { LimiterEffectName }) as Array;
            if (parameters == null)
            {
                return new[] { "?" };
            }

            var names = new List<string>();
            foreach (object p in parameters)
            {
                FieldInfo f = p.GetType().GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                names.Add(f != null ? (string)f.GetValue(p) : p.ToString());
            }

            return names.ToArray();
        }

        /// <summary>True when Master carries the Compressor and its threshold/attack/release are exposed.</summary>
        public static bool HasMasterLimiter(out string why)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath);
            if (mixer == null)
            {
                why = "asset missing";
                return false;
            }

            Type controllerType = FindType("UnityEditor.Audio.AudioMixerController");
            Type groupType = FindType("UnityEditor.Audio.AudioMixerGroupController");
            object master = Get(controllerType, mixer, "masterGroup");
            Array effects = (Array)Get(groupType, master, "effects");
            bool found = false;
            foreach (object e in effects)
            {
                object name = Get(e.GetType(), e, "effectName");
                if ((string)name == LimiterEffectName) { found = true; break; }
            }

            if (!found)
            {
                why = "Master has no " + LimiterEffectName;
                return false;
            }

            foreach (string p in new[] { LimiterThresholdParam, LimiterAttackParam, LimiterReleaseParam })
            {
                if (!mixer.GetFloat(p, out float _))
                {
                    why = "exposed parameter missing: " + p;
                    return false;
                }
            }

            why = string.Empty;
            return true;
        }

        /// <summary>True when the asset exists with every expected group, snapshot, exposed volume, the master limiter (WP-021C) AND the mix table (WP-057).</summary>
        public static bool Verify(out string why)
        {
            return VerifyTree(out why) && HasMasterLimiter(out why) && HasSnapshotMix(out why);
        }

        // ---- WP-057 · snapshot mix table ------------------------------------------------

        /// <summary>Writes <see cref="SnapshotVolumesDb"/> into the existing snapshots (in place, same GUID). Idempotent.</summary>
        public static string EnsureSnapshotMix()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath);
            if (mixer == null) return "mix: asset missing";
            if (HasSnapshotMix(out string _)) return "mix: up to date";
            Type controllerType = FindType("UnityEditor.Audio.AudioMixerController");
            Type groupType = FindType("UnityEditor.Audio.AudioMixerGroupController");
            Type snapshotType = FindType("UnityEditor.Audio.AudioMixerSnapshotController");
            MethodInfo guidForVolume = groupType.GetMethod("GetGUIDForVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setValue = snapshotType.GetMethod("SetValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array snapshots = (Array)Get(controllerType, mixer, "snapshots");
            int written = 0;
            foreach (object snap in snapshots)
            {
                int s = Array.IndexOf(AudioMixerBridge.SnapshotNames, ((UnityEngine.Object)snap).name);
                if (s < 0) continue;
                for (int g = 0; g < AudioMixerBridge.GroupNames.Length; g++)
                {
                    object group = FindGroup(mixer, controllerType, groupType, AudioMixerBridge.GroupNames[g]);
                    if (group == null) continue;
                    setValue.Invoke(snap, new[] { guidForVolume.Invoke(group, null), (object)SnapshotVolumesDb[s][g] });
                    written++;
                }
            }

            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            return "mix: " + written + " snapshot volumes written (v2)";
        }

        /// <summary>True when every snapshot carries the <see cref="SnapshotVolumesDb"/> values (±0.01 dB).</summary>
        public static bool HasSnapshotMix(out string why)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath);
            if (mixer == null) { why = "asset missing"; return false; }
            Type controllerType = FindType("UnityEditor.Audio.AudioMixerController");
            Type groupType = FindType("UnityEditor.Audio.AudioMixerGroupController");
            Type snapshotType = FindType("UnityEditor.Audio.AudioMixerSnapshotController");
            MethodInfo guidForVolume = groupType.GetMethod("GetGUIDForVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getValue = snapshotType.GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getValue == null) { why = "AudioMixerSnapshotController.GetValue missing"; return false; }
            Array snapshots = (Array)Get(controllerType, mixer, "snapshots");
            foreach (object snap in snapshots)
            {
                int s = Array.IndexOf(AudioMixerBridge.SnapshotNames, ((UnityEngine.Object)snap).name);
                if (s < 0) continue;
                for (int g = 0; g < AudioMixerBridge.GroupNames.Length; g++)
                {
                    object group = FindGroup(mixer, controllerType, groupType, AudioMixerBridge.GroupNames[g]);
                    if (group == null) continue;
                    object[] args = { guidForVolume.Invoke(group, null), 0f };
                    bool has = (bool)getValue.Invoke(snap, args);
                    float value = has ? (float)args[1] : float.NaN;
                    if (!has || Mathf.Abs(value - SnapshotVolumesDb[s][g]) > 0.01f)
                    {
                        why = "snapshot " + AudioMixerBridge.SnapshotNames[s] + " / " + AudioMixerBridge.GroupNames[g] + " = " + value + " dB, expected " + SnapshotVolumesDb[s][g];
                        return false;
                    }
                }
            }

            why = string.Empty;
            return true;
        }

        private static object FindGroup(AudioMixer mixer, Type controllerType, Type groupType, string name)
        {
            object master = Get(controllerType, mixer, "masterGroup");
            if (((UnityEngine.Object)master).name == name) return master;
            MethodInfo getAll = controllerType.GetMethod("GetAllAudioGroupsSlow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getAll != null)
            {
                var all = getAll.Invoke(mixer, null) as System.Collections.IEnumerable;
                if (all != null) foreach (object g in all) if (((UnityEngine.Object)g).name == name) return g;
            }

            Array children = (Array)Get(groupType, master, "children");
            foreach (object c in children) if (((UnityEngine.Object)c).name == name) return c;
            return null;
        }

        /// <summary>The WP-021 tree check: groups, snapshots, exposed volumes.</summary>
        public static bool VerifyTree(out string why)
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
