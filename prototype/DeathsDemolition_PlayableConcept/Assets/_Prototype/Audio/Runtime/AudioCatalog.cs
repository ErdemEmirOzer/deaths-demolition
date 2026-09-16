using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-021 · The produced sound set (AP-011), one entry per <see cref="AudioSoundId"/>
    /// that has real clips on disk. Built by the Editor tool from
    /// <c>Assets/_Art/Audio/AP011-audio-manifest.json</c> (idempotent: same manifest, same
    /// asset bytes) and loaded once by the director from <c>Resources/DD_AudioCatalog</c>.
    ///
    /// The catalog is an OVERLAY on the procedural library, not a replacement: a sound id
    /// with an entry here plays the produced clips, everything else keeps its recipe. That
    /// is what lets the set grow one family at a time and what keeps a checkout without
    /// LFS content (or a test project) audible.
    /// </summary>
    [CreateAssetMenu(fileName = "DD_AudioCatalog", menuName = "DD/Audio/Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        public const string ResourcePath = "DD_AudioCatalog";

        [Serializable]
        public sealed class Entry
        {
            public AudioSoundId Sound;
            public AudioClip[] Clips = Array.Empty<AudioClip>();
            public bool Loop;

            /// <summary>Integrated loudness of the delivered file(s), BS.1770, from the manifest.</summary>
            public float Lufs;

            /// <summary>True peak of the delivered file(s), dBTP, from the manifest (worst of the variations).</summary>
            public float TruePeakDb;

            /// <summary>Origin note: model + job, or "procedural" for a recipe bake shipped as a file.</summary>
            public string Provenance = string.Empty;

            // v003 quality standard stamps (from the manifest; the EditMode gate re-measures).
            public float NoiseFloorDb;
            public float DcDb;
            public int Clipped;
            public int BitDepth;
            public string Layers = string.Empty;
        }

        [SerializeField] private string _manifestVersion = string.Empty;
        [SerializeField] private string _sourceHash = string.Empty;
        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        private Dictionary<AudioSoundId, Entry> _index;

        public string ManifestVersion => _manifestVersion;
        public string SourceHash => _sourceHash;
        public IReadOnlyList<Entry> Entries => _entries;

        private static AudioCatalog s_loaded;
        private static bool s_loadAttempted;

        /// <summary>The shipped catalog, or null when none is in Resources. Cached; never throws.</summary>
        public static AudioCatalog Load()
        {
            if (!s_loadAttempted)
            {
                s_loadAttempted = true;
                s_loaded = Resources.Load<AudioCatalog>(ResourcePath);
            }

            return s_loaded;
        }

        /// <summary>Test seam: forget the cached instance (or force one).</summary>
        public static void OverrideForTests(AudioCatalog catalog, bool clearOnly = false)
        {
            s_loadAttempted = !clearOnly;
            s_loaded = clearOnly ? null : catalog;
        }

        public bool TryGet(AudioSoundId sound, out Entry entry)
        {
            if (_index == null || _index.Count != _entries.Length)
            {
                _index = new Dictionary<AudioSoundId, Entry>(_entries.Length);
                for (int i = 0; i < _entries.Length; i++)
                {
                    if (_entries[i] != null && !_index.ContainsKey(_entries[i].Sound))
                    {
                        _index.Add(_entries[i].Sound, _entries[i]);
                    }
                }
            }

            return _index.TryGetValue(sound, out entry) && entry.Clips != null && entry.Clips.Length > 0;
        }

        /// <summary>Editor/build-time setter. Keeps entries sorted by id so the asset is deterministic.</summary>
        public void Set(string manifestVersion, string sourceHash, List<Entry> entries)
        {
            entries.Sort((a, b) => ((int)a.Sound).CompareTo((int)b.Sound));
            _manifestVersion = manifestVersion ?? string.Empty;
            _sourceHash = sourceHash ?? string.Empty;
            _entries = entries.ToArray();
            _index = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_loaded = null;
            s_loadAttempted = false;
        }
    }
}
