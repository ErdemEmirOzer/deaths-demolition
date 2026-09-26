using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 TR · the Turkish language pack in the UI: the pack is whole in both columns, every literal key the UI asks for
    /// exists, Settings › LANGUAGE persists and sets <see cref="UiStrings.Language"/>, and upper-casing follows the language.
    /// </summary>
    public sealed class UiLanguageTests
    {
        private static readonly Regex LiteralKey = new Regex("UiStrings\\.(?:Get|Format)\\(\\s*\"([^\"]+)\"\\s*[,)]", RegexOptions.Compiled);

        private string _dir;

        private static string RepoRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")); }
        }

        [SetUp]
        public void SetUp()
        {
            UiStrings.ResetForTests();
            UiStrings.Language = UiStrings.English;
            _dir = Path.Combine(Path.GetTempPath(), "dd-ui-language-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            UiSettings.SetPathOverride(null);
            UiStrings.Language = UiStrings.English;
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void Paket_IkiKopyaBaytBayt_HerAnahtarEnVeTrDolu()
        {
            string planning = Path.Combine(RepoRoot, "planning", "content", "r1-strings.json");
            string shipped = Path.Combine(Application.dataPath, "_Prototype", "UI", "Resources", "Strings", "r1-strings.json");
            CollectionAssert.AreEqual(File.ReadAllBytes(planning), File.ReadAllBytes(shipped), "the two pack files are byte-equal");

            Dictionary<string, Dictionary<string, string>> table = UiStrings.Parse(File.ReadAllText(planning));
            Assert.Greater(table.Count, 800);
            var eksik = new List<string>();
            foreach (KeyValuePair<string, Dictionary<string, string>> row in table)
            {
                string en, tr;
                if (!row.Value.TryGetValue("en", out en) || string.IsNullOrEmpty(en)) eksik.Add(row.Key + " (en)");
                if (!row.Value.TryGetValue("tr", out tr) || string.IsNullOrEmpty(tr)) eksik.Add(row.Key + " (tr)");
            }

            Assert.IsEmpty(eksik, "every key has en and tr:\n" + string.Join("\n", eksik));
        }

        [Test]
        public void KaynakTaramasi_HerSabitAnahtarPakette()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime");
            var eksik = new List<string>();
            int seen = 0;
            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match m in LiteralKey.Matches(File.ReadAllText(path)))
                {
                    seen++;
                    if (!UiStrings.Has(m.Groups[1].Value))
                    {
                        eksik.Add(Path.GetFileName(path) + ": " + m.Groups[1].Value);
                    }
                }
            }

            Assert.Greater(seen, 300, "the scan found the UI's pack calls");
            Assert.IsEmpty(eksik, "UiStrings keys missing from the pack:\n" + string.Join("\n", eksik));
        }

        [Test]
        public void DilAyari_EskiDosyaIngilizce_TrKalici_UiStringsDiliniKurar()
        {
            string file = Path.Combine(_dir, UiSettings.FileName);
            File.WriteAllText(file, "{\"Version\":2,\"UiScale\":1.0,\"MasterVolume\":0.5}");
            UiSettings.SetPathOverride(file);
            Assert.AreEqual(UiStrings.English, UiSettings.Language, "an older file without the key reads English");
            Assert.AreEqual(UiStrings.English, UiStrings.Language);
            Assert.AreEqual("LANGUAGE", UiStrings.Get("settings.row.language", "LANGUAGE"));

            UiSettings.Language = UiStrings.Turkish;
            Assert.AreEqual(UiStrings.Turkish, UiStrings.Language, "the setting sets the column the UI reads");
            Assert.AreEqual("DİL", UiStrings.Get("settings.row.language", "LANGUAGE"));
            StringAssert.Contains("\"Language\": \"tr\"", File.ReadAllText(file).Replace("\":\"", "\": \""));

            UiStrings.Language = UiStrings.English;   // what a fresh session starts with
            UiSettings.SetPathOverride(file);          // reload from disk
            Assert.AreEqual(UiStrings.Turkish, UiSettings.Language, "tr survives a reload");
            Assert.AreEqual(UiStrings.Turkish, UiStrings.Language, "loading the settings applies the saved language");

            UiSettings.Language = "de";
            Assert.AreEqual(UiStrings.English, UiSettings.Language, "only en / tr");
            Assert.AreEqual(UiStrings.English, UiStrings.Language);

            File.WriteAllText(file, "{\"Version\":2,\"Language\":\"xx\"}");
            UiSettings.SetPathOverride(file);
            Assert.AreEqual(UiStrings.English, UiSettings.Language, "an unknown code on disk reads English");
        }

        [Test]
        public void Upper_TurkceIleNoktaliI_IngilizceIleDuz()
        {
            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual("İSTATİSTİK", UiStrings.Upper("istatistik"));
            Assert.AreEqual("KILIÇ", UiStrings.Upper("kılıç"));
            UiStrings.Language = UiStrings.English;
            Assert.AreEqual("ISTATISTIK", UiStrings.Upper("istatistik"));
            Assert.AreEqual(string.Empty, UiStrings.Upper(null));
        }
    }
}
