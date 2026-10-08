// STAGING DRAFT: not compiled/imported. No gameplay acceptance thresholds change.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DD.Prototype.Characters;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    internal static class ArtNatural24Calibration
    {
        internal const string DataPath = "Assets/_Art/Characters/Player/Calibration/R3Natural24Calibration-v001.json";
        private const string DataSha256 = "f0dcd888db483aa7ea1d17f0cd73fd09aca816d8ea26edb8e1080d8fe1e10b7c";
        private const string SourceSha256 = "9fdcc5e1e1f9f9580ad858923d91532f87736e10e38ef27fbc6b0e94eda73974";
        private const string Prefix = "[Natural24] ";
        [Serializable] private sealed class Data
        {
            public string schema, sourceCommit, evidenceSha256, measurement, assetPath, maskedAssetSha256;
            public Row[] rows;
            public Dependency[] dependencies;
        }
        [Serializable] private sealed class Row
        {
            public string name, guid, fileId, path, events;
            public int slot, directionIndex, frames, stanceFrames;
            public float oldValue, rawMps, naturalMps, childScale, seconds, templateStrideM, cycleSeconds, globalK;
        }
        [Serializable] private sealed class Dependency
        {
            public string path, fileSha256, metaSha256, guid, fileId;
        }
        private static void Need(bool valid, string reason)
        {
            if (!valid) throw new InvalidDataException(Prefix + "STALE/FAIL: " + reason + "; remeasure or explicitly review/re-pin the source. No fallback.");
        }
        private static string FullPath(string relative)
        {
            string project = Path.GetFullPath(Directory.GetCurrentDirectory()) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(project, relative));
            Need(relative.StartsWith("Assets/", StringComparison.Ordinal) && full.StartsWith(project, StringComparison.Ordinal), "path outside Assets: " + relative);
            return full;
        }
        private static string Digest(byte[] data)
        {
            using (SHA256 algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }
        private static string FileDigest(string path)
        {
            // Only a future authorized builder execution reads binary FBX bytes here.
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static bool Close(float a, float b) => !float.IsNaN(a) && !float.IsInfinity(a) && Mathf.Abs(a - b) <= 0.000002f;
        private static string EntryPattern(string name) => @"(^  - name: " + Regex.Escape(name) + @"\n(?:(?!^  - name: |^  _)[\s\S])*?^    unitySpeedMps: )([-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)$";
        private static void ValidateAsset(Data data)
        {
            string yaml = File.ReadAllText(FullPath(data.assetPath), Encoding.UTF8);
            foreach (Row row in data.rows)
            {
                var expression = new Regex(EntryPattern(row.name), RegexOptions.Multiline);
                MatchCollection matches = expression.Matches(yaml);
                Need(matches.Count == 1, row.name + " asset entry count");
                float stored = float.Parse(matches[0].Groups[2].Value, CultureInfo.InvariantCulture);
                // Initial intake and idempotent rebuild are both legal. Other speeds fail.
                Need(stored == row.oldValue || stored == row.rawMps, row.name + " stored raw speed drift");
                yaml = expression.Replace(yaml, m => m.Groups[1].Value + "<AUTHORIZED_RAW>");
            }
            Need(Digest(Encoding.UTF8.GetBytes(yaml)) == data.maskedAssetSha256, "asset contract changed outside the 24 speed fields");
        }
        private static Data LoadAndValidate()
        {
            byte[] bytes = File.ReadAllBytes(FullPath(DataPath));
            Need(Digest(bytes) == DataSha256, "calibration source SHA");
            Data data = JsonUtility.FromJson<Data>(Encoding.UTF8.GetString(bytes));
            Need(data != null && data.schema == "natural24-builder-override-v001" && data.evidenceSha256 == SourceSha256
                && data.sourceCommit == "d05b1145cca04eaef641080d90ec3f9c3e4b64f6"
                && data.measurement == "ACTUAL_ANIMATOR_FIXED_ROOT_RATE1_PHASE_ALL_NO_FOOT_IK", "source semantics/pin");
            Need(data.rows != null && data.rows.Length == 24 && data.dependencies != null, "source coverage");
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (string gait in new[] { "walk", "run", "sprint" })
                foreach (string direction in new[] { "fwd", "fwd_r", "right", "back_r", "back", "back_l", "left", "fwd_l" }) expected.Add("AP026_tp_" + gait + "_" + direction);
            var cells = new HashSet<int>();
            foreach (Row row in data.rows)
            {
                Need(expected.Remove(row.name) && row.slot >= 1 && row.slot <= 3 && row.directionIndex >= 0 && row.directionIndex < 8
                    && cells.Add(row.slot * 8 + row.directionIndex), "duplicate/missing canonical row");
                Need(row.rawMps > 0f && row.childScale > 0f && Close(row.rawMps * row.childScale, row.naturalMps), row.name + " raw/child scale relation");
            }
            Need(expected.Count == 0, "missing canonical loops");
            foreach (Dependency pin in data.dependencies)
            {
                string full = FullPath(pin.path);
                Need(FileDigest(full + ".meta") == pin.metaSha256, pin.path + " meta SHA");
                if (!string.IsNullOrEmpty(pin.fileSha256)) Need(FileDigest(full) == pin.fileSha256, pin.path + " bytes SHA");
                if (!string.IsNullOrEmpty(pin.guid)) Need(AssetDatabase.AssetPathToGUID(pin.path) == pin.guid, pin.path + " GUID");
                if (!string.IsNullOrEmpty(pin.fileId))
                {
                    bool found = false;
                    foreach (UnityEngine.Object item in AssetDatabase.LoadAllAssetsAtPath(pin.path))
                        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item, out string guid, out long id)
                            && guid == pin.guid && id.ToString(CultureInfo.InvariantCulture) == pin.fileId) found = true;
                    Need(found, pin.path + " imported fileID");
                }
            }
            ValidateAsset(data);
            return data;
        }
        internal static void ValidateInputs() => LoadAndValidate();

        internal static void Apply(Dictionary<string, float> speeds, Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest,
            Dictionary<string, AnimationClip> clips, ArtAnimatorBuilder.LocoData loco)
        {
            Data data = LoadAndValidate();
            Need(loco != null && loco.tables != null && loco.tables.loops.Count == 24, "AP026 ring data missing");
            // Validate all 24 rows first. No partial dictionary override on any validation failure.
            foreach (Row row in data.rows)
            {
                Need(manifest.TryGetValue(row.name, out ArtAnimationClipContract.ClipSpec spec)
                    && spec.target == "player" && spec.loop && spec.frames == row.frames && spec.stanceFrames == row.stanceFrames
                    && spec.EventsText() == row.events && Close(spec.seconds, row.seconds), row.name + " clip contract");
                Need(clips.TryGetValue(row.name, out AnimationClip clip) && clip != null && clip.name == row.name
                    && AssetDatabase.GetAssetPath(clip) == row.path && Close(clip.length, row.seconds), row.name + " clip identity/length");
                Need(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long fileId)
                    && guid == row.guid && fileId.ToString(CultureInfo.InvariantCulture) == row.fileId, row.name + " GUID/fileID");
                Need(Close(loco.timeScale[row.slot - 1][row.directionIndex], row.childScale)
                    && Close(loco.tables.heroStrideScale, row.globalK), row.name + " child scale/k drift");
                int found = 0;
                foreach (CharacterAnimationSet.LocoLoop loop in loco.tables.loops)
                {
                    if (loop.clip != row.name) continue;
                    found++;
                    Need(loop.ringSlot == row.slot && loop.dirIndex == row.directionIndex
                        && Close(loop.strideCycleMTemplate, row.templateStrideM) && Close(loop.cycleSecondsNatural, row.cycleSeconds), row.name + " loop template/cycle drift");
                }
                Need(found == 1, row.name + " loop table count");
            }
            foreach (Row row in data.rows) speeds[row.name] = row.rawMps;
            // rawMps is already naturalMps / measured childScale. Do not divide again.
            // PSV consumes rawMps * imported clip.length for stride; cadence remains in loco timeScale.
            Debug.Log(Prefix + "applied 24 PHASE_ALL rate-1 raw speeds; source " + data.evidenceSha256 + "; no gameplay acceptance claim");
        }
        internal static void VerifyGenerated()
        {
            // No input file validation after mutation: inspect the generated in-memory set before SaveAssets.
            Data data = JsonUtility.FromJson<Data>(File.ReadAllText(FullPath(DataPath), Encoding.UTF8));
            Need(Digest(File.ReadAllBytes(FullPath(DataPath))) == DataSha256, "calibration source changed during build");
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(data.assetPath);
            Need(set != null, "generated animation set missing");
            foreach (Row row in data.rows)
            {
                CharacterAnimationSet.Entry entry = set.Find(row.name);
                Need(entry != null && entry.unitySpeedMps == row.rawMps, row.name + " generated raw speed mismatch");
            }
        }
    }
}
