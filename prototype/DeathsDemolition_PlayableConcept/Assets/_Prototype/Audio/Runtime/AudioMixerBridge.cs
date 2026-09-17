using UnityEngine;
using UnityEngine.Audio;

namespace DD.Prototype.Audio
{
    /// <summary>The four mixer snapshots (WP-021 §2).</summary>
    public enum AudioSnapshot
    {
        Run = 0,
        RoundBreak = 1,
        Pause = 2,
        Menu = 3
    }

    /// <summary>
    /// WP-021 · The Unity <see cref="AudioMixer"/> seam.
    ///
    /// Asset: <c>Resources/DD_AudioMixer.mixer</c>, built by the Editor tool with the group
    /// tree Master → { Music, Ambience, SFX-World, SFX-Player, UI } and the snapshots Run /
    /// RoundBreak / Pause / Menu. Every AudioSource the audio system creates is routed
    /// through <see cref="GroupFor"/>; the software <see cref="AudioBusMixer"/> keeps doing
    /// the per-cue ducking (fast, sample-exact, no snapshot churn) while the mixer owns the
    /// coarse state (snapshots) and the user-facing volumes (exposed parameters).
    ///
    /// Missing asset = every call is a no-op and the game plays exactly as before WP-021.
    /// </summary>
    public sealed class AudioMixerBridge
    {
        public const string ResourcePath = "DD_AudioMixer";

        public const string GroupMaster = "Master";
        public const string GroupMusic = "Music";
        public const string GroupAmbience = "Ambience";
        public const string GroupSfxWorld = "SFX-World";
        public const string GroupSfxPlayer = "SFX-Player";
        public const string GroupUi = "UI";

        public static readonly string[] GroupNames = { GroupMaster, GroupMusic, GroupAmbience, GroupSfxWorld, GroupSfxPlayer, GroupUi };
        public static readonly string[] SnapshotNames = { "Run", "RoundBreak", "Pause", "Menu" };

        /// <summary>Exposed volume parameters, one per group, in <see cref="GroupNames"/> order.</summary>
        public static readonly string[] VolumeParams = { "MasterVolume", "MusicVolume", "AmbienceVolume", "SfxWorldVolume", "SfxPlayerVolume", "UiVolume" };

        private readonly AudioMixer _mixer;
        private readonly AudioMixerGroup[] _groups = new AudioMixerGroup[GroupNames.Length];
        private readonly AudioMixerSnapshot[] _snapshots = new AudioMixerSnapshot[SnapshotNames.Length];

        private AudioSnapshot _current = AudioSnapshot.Menu;
        private bool _currentKnown;

        public bool Available => _mixer != null;
        public AudioMixer Mixer => _mixer;
        public AudioSnapshot Current => _current;

        /// <summary>Snapshot transitions requested. Test/F1 readout.</summary>
        public int TransitionCount { get; private set; }

        public AudioMixerBridge() : this(Resources.Load<AudioMixer>(ResourcePath))
        {
        }

        public AudioMixerBridge(AudioMixer mixer)
        {
            _mixer = mixer;
            if (_mixer == null)
            {
                return;
            }

            for (int i = 0; i < GroupNames.Length; i++)
            {
                AudioMixerGroup[] found = _mixer.FindMatchingGroups(GroupNames[i]);
                for (int j = 0; j < found.Length; j++)
                {
                    if (found[j].name == GroupNames[i])
                    {
                        _groups[i] = found[j];
                        break;
                    }
                }
            }

            for (int i = 0; i < SnapshotNames.Length; i++)
            {
                _snapshots[i] = _mixer.FindSnapshot(SnapshotNames[i]);
            }
        }

        public AudioMixerGroup Group(string name)
        {
            for (int i = 0; i < GroupNames.Length; i++)
            {
                if (GroupNames[i] == name)
                {
                    return _groups[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Software bus → mixer group. Weapon = the player's own hands (SFX-Player);
        /// Impact and Target = the world (SFX-World); Interface = UI; Ambience = Ambience.
        /// Music has no software bus and is routed by <see cref="MusicGroup"/>.
        /// </summary>
        public AudioMixerGroup GroupFor(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Weapon: return _groups[4];
                case AudioBus.Impact:
                case AudioBus.Target: return _groups[3];
                case AudioBus.Interface: return _groups[5];
                case AudioBus.Ambience: return _groups[2];
                case AudioBus.Music: return _groups[1];
                default: return _groups[0];
            }
        }

        public AudioMixerGroup MusicGroup => _groups[1];

        public bool HasSnapshot(AudioSnapshot snapshot)
        {
            return _snapshots[(int)snapshot] != null;
        }

        /// <summary>Route a source. Null-safe: without a mixer the source keeps the default output.</summary>
        public void Route(AudioSource source, AudioBus bus)
        {
            if (source == null || _mixer == null)
            {
                return;
            }

            AudioMixerGroup group = GroupFor(bus);
            if (group != null)
            {
                source.outputAudioMixerGroup = group;
            }
        }

        public void RouteMusic(AudioSource source)
        {
            if (source == null || _mixer == null || _groups[1] == null)
            {
                return;
            }

            source.outputAudioMixerGroup = _groups[1];
        }

        /// <summary>Cross-fade into a snapshot. Same snapshot twice is a no-op.</summary>
        public void Transition(AudioSnapshot snapshot, float seconds)
        {
            if (_currentKnown && _current == snapshot)
            {
                return;
            }

            _current = snapshot;
            _currentKnown = true;
            TransitionCount++;

            AudioMixerSnapshot target = _mixer != null ? _snapshots[(int)snapshot] : null;
            if (target != null)
            {
                target.TransitionTo(Mathf.Max(0f, seconds));
            }
        }

        /// <summary>User volume 0-1 on one group, through the exposed parameter (dB). Persisted by the settings owner, not here.</summary>
        public bool SetVolume(string groupName, float linear01)
        {
            if (_mixer == null)
            {
                return false;
            }

            for (int i = 0; i < GroupNames.Length; i++)
            {
                if (GroupNames[i] == groupName)
                {
                    return _mixer.SetFloat(VolumeParams[i], LinearToDb(linear01));
                }
            }

            return false;
        }

        public static float LinearToDb(float linear01)
        {
            return linear01 <= 0.0001f ? -80f : Mathf.Clamp(20f * Mathf.Log10(linear01), -80f, 0f);
        }

        public string Describe()
        {
            if (_mixer == null)
            {
                return "mixer: none (software buses only)";
            }

            int groups = 0;
            for (int i = 0; i < _groups.Length; i++) { if (_groups[i] != null) groups++; }
            int snaps = 0;
            for (int i = 0; i < _snapshots.Length; i++) { if (_snapshots[i] != null) snaps++; }
            return string.Format("mixer: {0} · groups {1}/{2} · snapshots {3}/{4} · current {5} · transitions {6}",
                _mixer.name, groups, _groups.Length, snaps, _snapshots.Length, _currentKnown ? _current.ToString() : "-", TransitionCount);
        }
    }
}
