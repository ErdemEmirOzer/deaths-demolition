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

        /// <summary>WP-085 B5 (G4 = AP-027, C-00 05.10): the v4 weapon set — its entries replace the AP-011 rows of the same sound.</summary>
        public const string Ap027ManifestPath = "Assets/_Art/Audio/AP027-audio-manifest.json";
        public const string Ap027AudioRoot = "Assets/_Art/Audio/AP027/";

        /// <summary>
        /// WP-097 K2 (S-08 07.10): the AP-029 v001 set (tools/audio/ap029_import.py) — Fire v5 under the AP-027 Fire names, the 900-block
        /// class tails (Echo / Release / Room), the special-weapon rows under their AP-011 names, the AP-029 foley (Metal / Mat / Grass
        /// casings and links, MagDrop). Priority per sound: AP-029 &gt; AP-027 &gt; AP-011.
        /// </summary>
        public const string Ap029ManifestPath = "Assets/_Art/Audio/AP029-audio-manifest.json";
        public const string Ap029AudioRoot = "Assets/_Art/Audio/AP029/";
        public const string CatalogPath = "Assets/_Prototype/Audio/Resources/DD_AudioCatalog.asset";
        public const string RegionMapPath = "Assets/_Prototype/Audio/Resources/DD_AudioRegionMap.asset";
        public const string PlacementPath = "Assets/_Art/Environment/World/AP005_Placement.asset";

        /// <summary>WP-072 · WP-074: S-05's acoustic spaces (source) and the baked runtime map.</summary>
        public const string AcousticSourcePath = "Assets/_Art/Environment/World/V6/acoustic-spaces-v6.json";
        public const string AcousticMapPath = "Assets/_Prototype/Audio/Resources/DD_AcousticSpaces.asset";

        /// <summary>Vorbis quality (Unity 0–1 ≈ ogg q0–q10 / 10): v003 standard q8 SFX, q7 loops.</summary>
        public const float SfxQuality = 0.8f;
        public const float MusicQuality = 0.7f;

        /// <summary>WP-072 (DEC-093, 5 GB cap): player-weapon fire, body loops and space tails at q9.</summary>
        public const float WeaponQuality = 0.9f;

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

            /// <summary>WP-085 B5: a named reason this entry's noise-floor stamp may exceed the standard (empty = none).</summary>
            public string noiseFloorException;
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
            Debug.Log("[AudioCatalogBuilder] " + BuildAcousticSpaces());
            AssetDatabase.SaveAssets();
            Debug.Log("[AudioCatalogBuilder] BUILD-ALL OK");
        }

        /// <summary>
        /// Minimal JSON reader for the acoustic source (JsonUtility cannot read S-05's nested <c>segmentsXZ</c> arrays):
        /// objects → Dictionary, arrays → List, numbers → double, strings, bools, null. Editor-only.
        /// </summary>
        private sealed class MiniJson
        {
            private readonly string _s;
            private int _i;

            private MiniJson(string s) { _s = s; }

            public static object Parse(string s) => new MiniJson(s).Value();

            private void Ws() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

            private object Value()
            {
                Ws();
                char c = _s[_i];
                if (c == '{')
                {
                    var o = new Dictionary<string, object>(); _i++; Ws();
                    if (_s[_i] == '}') { _i++; return o; }
                    while (true)
                    {
                        Ws(); string k = (string)Value(); Ws(); _i++;   // ':'
                        o[k] = Value(); Ws();
                        if (_s[_i++] == '}') return o;
                    }
                }

                if (c == '[')
                {
                    var a = new List<object>(); _i++; Ws();
                    if (_s[_i] == ']') { _i++; return a; }
                    while (true)
                    {
                        a.Add(Value()); Ws();
                        if (_s[_i++] == ']') return a;
                    }
                }

                if (c == '"')
                {
                    var sb = new StringBuilder(); _i++;
                    while (_s[_i] != '"')
                    {
                        if (_s[_i] == '\\') { _i++; char e = _s[_i]; sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'u' ? (char)Convert.ToInt32(_s.Substring(_i + 1, 4), 16) : e); if (e == 'u') _i += 4; }
                        else sb.Append(_s[_i]);
                        _i++;
                    }

                    _i++;
                    return sb.ToString();
                }

                if (_s.Substring(_i).StartsWith("true", StringComparison.Ordinal)) { _i += 4; return true; }
                if (_s.Substring(_i).StartsWith("false", StringComparison.Ordinal)) { _i += 5; return false; }
                if (_s.Substring(_i).StartsWith("null", StringComparison.Ordinal)) { _i += 4; return null; }
                int st = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                return double.Parse(_s.Substring(st, _i - st), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static float[] Floats(object list)
        {
            var l = (List<object>)list; var f = new float[l.Count];
            for (int i = 0; i < l.Count; i++) f[i] = (float)(double)l[i];
            return f;
        }

        /// <summary>WP-072: bake acoustic-spaces-v6.json into Resources/DD_AcousticSpaces.asset (no-op when the source hash matches).</summary>
        public static string BuildAcousticSpaces()
        {
            if (!File.Exists(AcousticSourcePath))
            {
                return "acoustic spaces: no source (" + AcousticSourcePath + ") - every point stays Open";
            }

            string text = File.ReadAllText(AcousticSourcePath);
            string hash;
            using (var sha = SHA256.Create())
            {
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }

            var existing = AssetDatabase.LoadAssetAtPath<AcousticSpaceMap>(AcousticMapPath);
            if (existing != null && existing.SourceHash == hash)
            {
                return "acoustic spaces up to date (" + existing.Spaces.Count + " spaces, " + hash + ")";
            }

            var root = (Dictionary<string, object>)MiniJson.Parse(text);
            string schema = root.TryGetValue("schema", out object sc) ? sc as string : "?";
            var spaces = new List<AcousticSpaceMap.Space>();
            foreach (object o in (List<object>)root["spaces"])
            {
                var s = (Dictionary<string, object>)o;
                string id = (string)s["id"];
                // WP-097 K1 (C-00 K3): "Open" / "Corridor" / "Hall" / "Room" — the enum's names, case-sensitive; a number is refused.
                string enclosureText = s["enclosure"] as string;
                if (string.IsNullOrEmpty(enclosureText) || char.IsDigit(enclosureText[0]) || enclosureText[0] == '-'
                    || !Enum.TryParse(enclosureText, false, out AcousticSpace enclosure) || !Enum.IsDefined(typeof(AcousticSpace), enclosure))
                {
                    throw new InvalidOperationException("acoustic space '" + id + "': enclosure '" + s["enclosure"] + "' invalid");
                }

                float floor = s.TryGetValue("floor", out object fl) ? (float)(double)fl : 0f;
                float ceiling = s.TryGetValue("ceiling", out object ce) ? (float)(double)ce : 4f;
                // Rooms carry one polygonXZ; corridors a segmentsXZ list of quads (one space per segment, same id + #n).
                var polygons = new List<float[]>();
                if (s.TryGetValue("polygonXZ", out object poly)) polygons.Add(Floats(poly));
                if (s.TryGetValue("segmentsXZ", out object segs)) foreach (object seg in (List<object>)segs) polygons.Add(Floats(seg));
                if (polygons.Count == 0) throw new InvalidOperationException("acoustic space '" + id + "': no polygonXZ / segmentsXZ");
                for (int k = 0; k < polygons.Count; k++)
                {
                    if (polygons[k].Length < 6) throw new InvalidOperationException("acoustic space '" + id + "': polygon " + k + " has < 3 corners");
                    spaces.Add(new AcousticSpaceMap.Space { id = polygons.Count > 1 ? id + "#" + k : id, enclosure = enclosure, polygonXZ = polygons[k], floor = floor, ceiling = ceiling });
                }
            }

            AcousticSpaceMap map = existing;
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<AcousticSpaceMap>();
                Directory.CreateDirectory(Path.GetDirectoryName(AcousticMapPath));
                AssetDatabase.CreateAsset(map, AcousticMapPath);
            }

            map.Set(hash, "World_V6_Greybox", spaces);
            EditorUtility.SetDirty(map);
            return "acoustic spaces baked: " + spaces.Count + " polygons (" + schema + ", " + hash + ")";
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

            // WP-085 B5: the AP-027 manifest rides on top — its rows win for the same sound, the rest of AP-011 stays.
            Manifest v4 = File.Exists(Ap027ManifestPath) ? JsonUtility.FromJson<Manifest>(File.ReadAllText(Ap027ManifestPath)) : null;
            // WP-097 K2: the AP-029 manifest rides on top of both — its rows win for the same sound.
            Manifest v5 = File.Exists(Ap029ManifestPath) ? JsonUtility.FromJson<Manifest>(File.ReadAllText(Ap029ManifestPath)) : null;
            List<(ManifestEntry entry, string root)> rows = MergeRows(manifest, v4, v5);

            // The source hash names every manifest that fed the catalog (a changed AP-029 import rebuilds it); without AP-029 it is
            // the value it was before K2, so an unchanged tree stays a no-op.
            string hash = v4 != null ? HashOf(ManifestPath).Substring(0, 32) + HashOf(Ap027ManifestPath).Substring(0, 32) : HashOf(ManifestPath);
            if (v5 != null) hash = hash.Substring(0, 32) + (v4 != null ? HashOf(Ap027ManifestPath).Substring(0, 32) : string.Empty) + HashOf(Ap029ManifestPath).Substring(0, 32);
            string version = v4 != null ? manifest.version + "+" + v4.version : manifest.version;
            if (v5 != null) version += "+" + v5.version;
            var existing = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (existing != null && existing.SourceHash == hash && existing.Entries.Count == rows.Count)
            {
                return "catalog up to date (" + rows.Count + " entries, " + hash.Substring(0, 8) + ")";
            }

            var entries = new List<AudioCatalog.Entry>(rows.Count);
            var ids = new HashSet<AudioSoundId>();
            int missing = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                ManifestEntry m = rows[i].entry;
                string root = rows[i].root;
                if (!Enum.TryParse(m.sound, out AudioSoundId id))
                {
                    Debug.LogWarning("[AudioCatalogBuilder] unknown sound id in manifest: " + m.sound);
                    continue;
                }

                // WP-085 G2: two spellings that parse to one id (a number, a flags list) are still one sound twice.
                if (!ids.Add(id))
                {
                    throw new InvalidOperationException("audio manifest row '" + m.sound + "' resolves to " + id + ", already in the catalog (catalog untouched)");
                }

                var clips = new List<AudioClip>(m.files.Length);
                for (int f = 0; f < m.files.Length; f++)
                {
                    string path = root + m.files[f];
                    PinImporter(path, m.loop, m.sound);
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

            catalog.Set(version, hash, entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            return "catalog built: " + entries.Count + " entries, " + missing + " missing clips, manifest " + hash.Substring(0, 8);
        }

        /// <summary>
        /// WP-085 B5 overlay as a pure step: AP-027 rows win for the same sound, AP-011 keeps the rest (<paramref name="ap027"/>
        /// may be null). WP-085 G2: a sound listed twice inside ONE manifest is a delivery error — which row is meant cannot be
        /// decided here, and a duplicate Sound in the catalog used to make AudioCatalog.TryGet rebuild its index on every call —
        /// so it throws before the catalog is touched.
        /// </summary>
        public static List<(ManifestEntry entry, string root)> MergeRows(Manifest ap011, Manifest ap027)
        {
            return MergeRows(ap011, ap027, null);
        }

        /// <summary>
        /// WP-097 K2: the three-layer overlay — AP-029 rows win over AP-027 and AP-011 for the same sound, AP-027 over AP-011 (either
        /// newer manifest may be null). A sound listed twice inside one manifest still throws (WP-085 G2).
        /// </summary>
        public static List<(ManifestEntry entry, string root)> MergeRows(Manifest ap011, Manifest ap027, Manifest ap029)
        {
            var rows = new List<(ManifestEntry entry, string root)>();
            var duplicates = new List<string>();
            var v5Sounds = new HashSet<string>();
            if (ap029 != null && ap029.entries != null)
            {
                foreach (ManifestEntry e in ap029.entries)
                {
                    if (v5Sounds.Add(e.sound)) rows.Add((e, Ap029AudioRoot));
                    else duplicates.Add("AP-029 " + e.sound);
                }
            }

            var v4Sounds = new HashSet<string>();
            if (ap027 != null && ap027.entries != null)
            {
                foreach (ManifestEntry e in ap027.entries)
                {
                    if (!v4Sounds.Add(e.sound)) duplicates.Add("AP-027 " + e.sound);
                    else if (!v5Sounds.Contains(e.sound)) rows.Add((e, Ap027AudioRoot));
                }
            }

            var v1Sounds = new HashSet<string>();
            if (ap011 != null && ap011.entries != null)
            {
                foreach (ManifestEntry e in ap011.entries)
                {
                    if (!v1Sounds.Add(e.sound)) duplicates.Add("AP-011 " + e.sound);
                    else if (!v4Sounds.Contains(e.sound) && !v5Sounds.Contains(e.sound)) rows.Add((e, AudioRoot));
                }
            }

            if (duplicates.Count > 0)
            {
                throw new InvalidOperationException("audio manifest lists a sound more than once (catalog untouched): " + string.Join(", ", duplicates));
            }

            return rows;
        }

        /// <summary>WP-072: the v016 player-weapon set (fire / transient / body loop / space tail); WP-085 B5: the AP-027 layers
        /// (mechanical, per-weapon tails, distance voices) ride the same q9 rule.</summary>
        public static bool IsWeaponV3(string sound)
        {
            return sound != null && sound.StartsWith("Weapon", StringComparison.Ordinal)
                   && (sound.EndsWith("Fire", StringComparison.Ordinal) || sound.EndsWith("Transient", StringComparison.Ordinal)
                       || sound.EndsWith("Loop", StringComparison.Ordinal) || sound.StartsWith("WeaponTail", StringComparison.Ordinal)
                       || sound.EndsWith("Mechanical", StringComparison.Ordinal) || sound.Contains("Tail") || sound.Contains("Distance"));
        }

        /// <summary>Stereo files: every space tail (v016 class tails, AP-027 per-weapon tails; WP-097 K2: the AP-029 900-block Echo /
        /// Release / Room class tails — their "WeaponTail" prefix makes them weapon v3 (q9) and stereo).</summary>
        public static bool IsStereoTail(string sound) => IsWeaponV3(sound) && sound.Contains("Tail");

        /// <summary>
        /// WP-097 K1 (03 §2): the open-air echo / release class tails (WeaponTail*Echo / WeaponTail*Release, 900 block) stay Vorbis in
        /// memory (CompressedInMemory): 6 classes × 4 variations × ~2.5 s stereo would be ~11.5 MB as PCM, ~1.5 MB as q9 Vorbis, and at
        /// most two of them sound at once, so the decode cost is negligible. Every other sound keeps its load type.
        /// </summary>
        public static bool IsEchoOrRelease(string sound)
        {
            return sound != null && sound.StartsWith("WeaponTail", StringComparison.Ordinal)
                   && (sound.EndsWith("Echo", StringComparison.Ordinal) || sound.EndsWith("Release", StringComparison.Ordinal));
        }

        /// <summary>The load type an entry's files are pinned to: streaming for ≥ 8 s, WP-097 echo / release in memory compressed, else decompressed.</summary>
        public static AudioClipLoadType WantedLoadType(string sound, bool longClip)
        {
            if (longClip) return AudioClipLoadType.Streaming;
            return IsEchoOrRelease(sound) ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
        }

        /// <summary>The codec and Vorbis quality an entry's files are pinned to (the quality test reads the same rule).</summary>
        public static AudioCompressionFormat WantedFormat(string sound, bool loop, bool longClip, out float quality)
        {
            if (IsWeaponV3(sound))
            {
                quality = WeaponQuality;
                return sound.EndsWith("Transient", StringComparison.Ordinal) ? AudioCompressionFormat.PCM : AudioCompressionFormat.Vorbis;
            }

            quality = loop || longClip ? MusicQuality : SfxQuality;
            return AudioCompressionFormat.Vorbis;
        }

        /// <summary>Importer pin. Only writes when something differs, so a no-op build reimports nothing.</summary>
        private static void PinImporter(string path, bool loop, string sound = "")
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
            // WP-072 (DEC-093): the weapon v3 set is chosen for quality - q9 Vorbis, the ≤ 70 ms transients
            // as PCM (no codec pre-echo on the click that carries the weapon), space tails keep their stereo.
            bool stereo = IsStereoTail(sound);
            var wanted = new AudioImporterSampleSettings
            {
                compressionFormat = WantedFormat(sound, loop, longClip, out float quality),
                quality = quality,
                loadType = WantedLoadType(sound, longClip),   // WP-097 K1: echo / release CompressedInMemory
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = !longClip
            };

            bool same = s.compressionFormat == wanted.compressionFormat
                        && Mathf.Approximately(s.quality, wanted.quality)
                        && s.loadType == wanted.loadType
                        && s.sampleRateSetting == wanted.sampleRateSetting
                        && importer.forceToMono == !stereo
                        && importer.loadInBackground == longClip;

            if (same)
            {
                return;
            }

            importer.defaultSampleSettings = wanted;
            importer.forceToMono = !stereo;
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
