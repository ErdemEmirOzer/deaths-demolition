using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-081 (C-00 05.10, decision ii) · every wallpaper in Resources/UI/Wallpapers came in through the intake
    /// (tools/wallpaper/wallpaper_intake.py) or was adopted as it was, and is the very file its record names: the bytes and
    /// the sha256 in UI/Art/wallpaper-intake.json match, no file is unrecorded and no record is orphaned. An encoded file
    /// met the intake's bar (PSNR ≥ 40 dB, the luminance ceiling where one was asked). Masters stay outside the repo
    /// (AP-028's lossless PNGs in K/asset-production/AP-028); the repo carries JPEG q90, which Unity compresses to BC7.
    /// </summary>
    [TestFixture]
    [Category("WP081")]
    public sealed class WallpaperIntakeTests
    {
        private const string Folder = "Assets/_Prototype/UI/Art/Resources/UI/Wallpapers";
        private const string RecordPath = "Assets/_Prototype/UI/Art/wallpaper-intake.json";
        private const string Schema = "dd-wallpaper-intake-v1";
        private const float MinPsnr = 40f;
        private const float UpgradeCeiling = 0.0245f;

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable]
        private sealed class Master
        {
            public string path;
            public string sha256;
            public long bytes;
        }

        [Serializable]
        private sealed class Luminance
        {
            public float master;
            public float jpeg;
        }

        [Serializable]
        private sealed class Row
        {
            public string file;
            public long bytes;
            public string sha256;
            public Master master;
            public string encoder;
            public float psnr;
            public float ssim;
            public Luminance maxLuminance;
            public string note;
        }

        [Serializable]
        private sealed class Record
        {
            public string schema;
            public Row[] files;
        }
#pragma warning restore 0649

        private static Record Load()
        {
            Assert.IsTrue(File.Exists(RecordPath), RecordPath);
            Record record = JsonUtility.FromJson<Record>(File.ReadAllText(RecordPath));
            Assert.IsNotNull(record);
            Assert.AreEqual(Schema, record.schema);
            Assert.IsNotNull(record.files);
            return record;
        }

        private static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        [Test]
        public void HerDuvarKagidi_KaydindakiDosya_BaytVeSha256()
        {
            Record record = Load();
            var rows = new Dictionary<string, Row>();
            foreach (Row row in record.files)
            {
                Assert.IsFalse(rows.ContainsKey(row.file), row.file + ": one record per file");
                rows[row.file] = row;
            }

            var seen = new HashSet<string>();
            foreach (string path in Directory.GetFiles(Folder))
            {
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = Path.GetFileName(path);
                Assert.IsTrue(name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase), name + ": a wallpaper is a JPEG (masters stay outside the repo)");
                Assert.IsTrue(rows.ContainsKey(name), name + ": unrecorded - bring it in with tools/wallpaper/wallpaper_intake.py");
                byte[] data = File.ReadAllBytes(path);
                Assert.AreEqual(rows[name].bytes, data.LongLength, name + ": bytes");
                Assert.AreEqual(rows[name].sha256, Sha256(data), name + ": sha256");
                seen.Add(name);
            }

            foreach (string file in rows.Keys)
            {
                Assert.IsTrue(seen.Contains(file), file + ": recorded but not in " + Folder);
            }

            foreach (string art in UiCurtain.LoadingKeyArts)
            {
                Assert.IsTrue(rows.ContainsKey(Path.GetFileName(art) + ".jpg"), art + ": every rotation art is recorded");
            }
        }

        [Test]
        public void KodlananDosya_Psnr40_TavanKorunur()
        {
            foreach (Row row in Load().files)
            {
                bool adopted = row.encoder.StartsWith("adopted", StringComparison.Ordinal);
                Assert.IsTrue(adopted || row.encoder.Contains("JPEG q90"), row.file + ": JPEG q90 (C-00 05.10) - " + row.encoder);
                Assert.IsFalse(string.IsNullOrEmpty(row.master.path), row.file + ": where its master lives");
                if (adopted)
                {
                    continue;
                }

                Debug.Log("[DD-TEST] wp081.intake " + row.file + " psnr=" + row.psnr.ToString("F2") + " ssim=" + row.ssim.ToString("F4") + " bytes=" + row.bytes);
                Assert.GreaterOrEqual(row.psnr, MinPsnr, row.file + ": PSNR against its master");
                Assert.AreEqual(64, row.master.sha256.Length, row.file + ": the master's sha256");
            }

            Row upgrade = Array.Find(Load().files, r => r.file == "upgrade_wallpaper.jpg");
            Assert.IsNotNull(upgrade, "the round-end wallpaper's record");
            Assert.LessOrEqual(upgrade.maxLuminance.jpeg, UpgradeCeiling, "the intake kept the R1-C ceiling");
        }
    }
}
