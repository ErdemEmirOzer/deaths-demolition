using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Art;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Audio.EditorTools
{
    /// <summary>
    /// WP-021 · Manifest → catalog. Reads <c>Assets/_Art/Audio/AP011-audio-manifest.json</c>
    /// (the AP-011 delivery: event, files, loudness, loop, bus), pins the importer settings
    /// of every referenced clip and writes <c>Resources/DD_AudioCatalog.asset</c>.
    ///
    /// Idempotent: the catalog stores the manifest's SHA-256; an unchanged manifest is a
    /// no-op (same bytes on disk, no import churn). Also builds the region map from the
    /// AP-005 placement asset (<see cref="BuildRegionMap"/>) - same rule.
    ///
    /// Import settings (sound-engineer standard, §2): Vorbis · SFX quality 0.6 (≈ q6) ·
    /// short one-shots decompressed on load · loops ≥ 8 s streamed · force mono · 48 kHz
    /// preserved (no resampling; the files already are).
    /// </summary>
    public static class AudioCatalogBuilder
    {
        public const string ManifestPath = "Assets/_Art/Audio/AP011-audio-manifest.json";
        public const string AudioRoot = "Assets/_Art/Audio/AP011/";
        public const string CatalogPath = "Assets/_Prototype/Audio/Resources/DD_AudioCatalog.asset";
        public const string RegionMapPath = "Assets/_Prototype/Audio/Resources/DD_AudioRegionMap.asset";
        public const string PlacementPath = "Assets/_Art/Environment/World/AP005_Placement.asset";

        /// <summary>Vorbis quality (Unity 0–1 ≈ ogg q0–q10 / 10): v003 standard q8 SFX, q7 loops.</summary>
        public const float SfxQuality = 0.8f;
        public const float MusicQuality = 0.7f;

        [Serializable]
        public sealed class ManifestEntry
        {
            public string sound;
            public string @event;
            public string[] files;
            public bool loop;
            public float lufs;
            public float truePeakDb;

            /// <summary>v003: quietest 100 ms window of file 1 (dBFS), DC offset (dBFS), clipped samples over all files, source bit depth.</summary>
            public float noiseFloorDb;
            public float dcDb;
            public int clipped;
            public int bitDepth;
            public bool continuous;
            public float endSilenceDb;

            /// <summary>v003: the entry's own loudness target and true-peak ceiling (design level / codec headroom).</summary>
            public float targetLufs;
            public float ceilingDbtp;
            public string layers;
            public string bus;
            public bool spatial;
            public string provenance;
        }

        [Serializable]
        public sealed class ManifestStandard
        {
            public float sfxLufs = -18f;
            public float musicLufs = -16f;
            public float ambienceLufs = -20f;
            public float truePeakMax = -1f;
            public float toleranceLu = 1.5f;
            public float noiseFloorMaxDb = -60f;
            public int bitDepth = 24;
        }

        [Serializable]
        public sealed class Manifest
        {
            public string version;
            public string package;
            public ManifestStandard standard = new ManifestStandard();
            public ManifestEntry[] entries = Array.Empty<ManifestEntry>();
        }

        [MenuItem("DD/Audio/Build Catalog + Region Map")]
        public static void BuildMenu()
        {
            Debug.Log("[AudioCatalogBuilder] " + BuildCatalog());
            Debug.Log("[AudioCatalogBuilder] " + BuildRegionMap());
        }

        /// <summary>Batchmode entry: mixer + catalog + region map, throws on a missing piece.</summary>
        public static void BuildAllBatch()
        {
            Debug.Log("[AudioMixerBuilder] " + AudioMixerBuilder.Build());
            if (!AudioMixerBuilder.Verify(out string why))
            {
                throw new InvalidOperationException("Mixer: " + why);
            }

            Debug.Log("[AudioCatalogBuilder] " + BuildCatalog());
            Debug.Log("[AudioCatalogBuilder] " + BuildRegionMap());
            AssetDatabase.SaveAssets();
            Debug.Log("[AudioCatalogBuilder] BUILD-ALL OK");
        }

        public static Manifest LoadManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                return null;
            }

            return JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        }

        public static string HashOf(string path)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(File.ReadAllBytes(path));
                var sb = new StringBuilder(64);
                for (int i = 0; i < bytes.Length; i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }

        public static string BuildCatalog()
        {
            Manifest manifest = LoadManifest();
            if (manifest == null)
            {
                return "manifest missing: " + ManifestPath + " (catalog untouched)";
            }

            string hash = HashOf(ManifestPath);
            var existing = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (existing != null && existing.SourceHash == hash && existing.Entries.Count == manifest.entries.Length)
            {
                return "catalog up to date (" + manifest.entries.Length + " entries, " + hash.Substring(0, 8) + ")";
            }

            var entries = new List<AudioCatalog.Entry>(manifest.entries.Length);
            int missing = 0;
            for (int i = 0; i < manifest.entries.Length; i++)
            {
                ManifestEntry m = manifest.entries[i];
                if (!Enum.TryParse(m.sound, out AudioSoundId id))
                {
                    Debug.LogWarning("[AudioCatalogBuilder] unknown sound id in manifest: " + m.sound);
                    continue;
                }

                var clips = new List<AudioClip>(m.files.Length);
                for (int f = 0; f < m.files.Length; f++)
                {
                    string path = AudioRoot + m.files[f];
                    PinImporter(path, m.loop);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip == null)
                    {
                        missing++;
                        Debug.LogWarning("[AudioCatalogBuilder] clip missing: " + path);
                        continue;
                    }

                    clips.Add(clip);
                }

                entries.Add(new AudioCatalog.Entry
                {
                    Sound = id,
                    Clips = clips.ToArray(),
                    Loop = m.loop,
                    Lufs = m.lufs,
                    TruePeakDb = m.truePeakDb,
                    NoiseFloorDb = m.noiseFloorDb,
                    DcDb = m.dcDb,
                    Clipped = m.clipped,
                    BitDepth = m.bitDepth,
                    Layers = m.layers ?? string.Empty,
                    Provenance = m.provenance ?? string.Empty
                });
            }

            AudioCatalog catalog = existing;
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.Set(manifest.version, hash, entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            return "catalog built: " + entries.Count + " entries, " + missing + " missing clips, manifest " + hash.Substring(0, 8);
        }

        /// <summary>Importer pin. Only writes when something differs, so a no-op build reimports nothing.</summary>
        private static void PinImporter(string path, bool loop)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                return;
            }

            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            AudioClip probe = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            bool longClip = probe != null && probe.length >= 8f;

            // v003 quality standard (user directive 2026-09-16): OGG q8 for SFX, q7 for music/ambience loops.
            var wanted = new AudioImporterSampleSettings
            {
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = loop || longClip ? MusicQuality : SfxQuality,
                loadType = longClip ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = !longClip
            };

            bool same = s.compressionFormat == wanted.compressionFormat
                        && Mathf.Approximately(s.quality, wanted.quality)
                        && s.loadType == wanted.loadType
                        && s.sampleRateSetting == wanted.sampleRateSetting
                        && importer.forceToMono
                        && importer.loadInBackground == longClip;

            if (same)
            {
                return;
            }

            importer.defaultSampleSettings = wanted;
            importer.forceToMono = true;
            importer.loadInBackground = longClip;
            importer.ambisonic = false;
            importer.SaveAndReimport();
        }

        public static string BuildRegionMap()
        {
            var placement = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(PlacementPath);
            if (placement == null)
            {
                return "placement missing: " + PlacementPath + " (region map untouched)";
            }

            string version = placement.package + "/" + placement.version;
            var existing = AssetDatabase.LoadAssetAtPath<AudioRegionMap>(RegionMapPath);
            if (existing != null && existing.SourceVersion == version && existing.Polygons.Count == placement.regions.Count)
            {
                return "region map up to date (" + version + ", " + existing.Polygons.Count + " polygons)";
            }

            var polygons = new List<AudioRegionMap.Polygon>(placement.regions.Count);
            for (int i = 0; i < placement.regions.Count; i++)
            {
                WorldPlacementAsset.Region r = placement.regions[i];
                AudioRegion region = AudioRegionMap.Parse(r.id);
                if (region == AudioRegion.None || r.polygonXZ == null || r.polygonXZ.Length < 6)
                {
                    Debug.LogWarning("[AudioCatalogBuilder] region skipped: " + r.id);
                    continue;
                }

                polygons.Add(new AudioRegionMap.Polygon { Region = region, SourceId = r.id, XZ = (float[])r.polygonXZ.Clone() });
            }

            AudioRegionMap map = existing;
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<AudioRegionMap>();
                Directory.CreateDirectory(Path.GetDirectoryName(RegionMapPath));
                AssetDatabase.CreateAsset(map, RegionMapPath);
            }

            map.Set(version, polygons);
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
            return "region map built: " + polygons.Count + " polygons from " + version;
        }
    }
}
