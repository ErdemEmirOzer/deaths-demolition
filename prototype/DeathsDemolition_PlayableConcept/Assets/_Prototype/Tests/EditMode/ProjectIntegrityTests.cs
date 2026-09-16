using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// GT · bağımsız doğrulama · proje bütünlüğü.
    ///
    /// İki iş yapar:
    ///   1. SEVKİYAT ASSET'İ denetimi — testler varsayılan alan değerlerini doğruluyor,
    ///      ama oyun Resources/PrototypeConfig.asset ile koşuyor. Asset kaymışsa
    ///      formül testleri yeşil kalır ve hiçbir şey kanıtlamaz. Bu boşluğu kapatır.
    ///   2. KOPUK REFERANS TARAMASI (TS-105) — kapıya gömülü, elle bash'a bağlı değil.
    ///      Yöntem: metin taraması + AssetDatabase ile GUID çözümü. Bash'taki
    ///      comm-tabanlı taramadan güçlüdür çünkü paket GUID'lerini de çözer.
    /// </summary>
    [TestFixture]
    public sealed class ProjectIntegrityTests
    {
        private const string AssetsRoot = "Assets";

        // ------------------------------------------------------- sevkiyat asset denetimi

        private static PrototypeConfig SevkiyatConfig()
        {
            var config = Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);
            Assert.IsNotNull(config,
                "Resources/PrototypeConfig bulunamadı. Prototip bu asset olmadan koşamaz.");
            return config;
        }

        [Test]
        public void SevkiyatAsseti_HavuzFormuluVarsayilanlaAyni()
        {
            PrototypeConfig config = SevkiyatConfig();

            int[] beklenen = { 12, 20, 29, 38, 48, 57, 67, 77, 87, 97, 107, 117, 127, 137, 147 };
            for (int i = 0; i < beklenen.Length; i++)
            {
                int r = i + 1;
                Assert.AreEqual(beklenen[i], config.TotalEnemyPool(r),
                    "SEVKİYAT ASSET'İ R" + r + " havuzunda kaymış.");
            }
        }

        [Test]
        public void SevkiyatAsseti_HedefAktifVarsayilanlaAyni()
        {
            PrototypeConfig config = SevkiyatConfig();

            int[] beklenen = { 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, 29, 31, 32, 32, 32 };
            for (int i = 0; i < beklenen.Length; i++)
            {
                int r = i + 1;
                Assert.AreEqual(beklenen[i], config.TargetActiveEnemies(r),
                    "SEVKİYAT ASSET'İ R" + r + " hedef aktif sayısında kaymış.");
            }

            Assert.AreEqual(40, config.AbsoluteSafetyCeiling, "Mutlak güvenlik tavanı 40 olmalı.");
        }

        /// <summary>
        /// Sevkiyat asset'i ↔ kod varsayılanı PARİTESİ (K5 istisnası): buradaki sayılar ürün kararı
        /// değil, asset'in koddan sapmadığının nöbetçisidir (WP-007: Special eğrisi değerleri
        /// PrototypeOnly; sözleşme testleri config-relative fixture'dadır).
        /// </summary>
        [Test]
        [Category("PrototypeOnly")]
        public void SevkiyatAsseti_IlerlemeVeSpecialEgrisiVarsayilanlaAyni()
        {
            PrototypeConfig config = SevkiyatConfig();

            Assert.AreEqual(100, config.LevelRequirement(1));
            Assert.AreEqual(175, config.LevelRequirement(2));
            Assert.AreEqual(250, config.LevelRequirement(3));
            Assert.AreEqual(325, config.LevelRequirement(4));

            Assert.AreEqual(20, config.RoundCompletionXp(1));
            Assert.AreEqual(55, config.RoundCompletionXp(8));

            Assert.AreEqual(1, config.KillXp(EnemyTier.Standard));
            Assert.AreEqual(9, config.KillXp(EnemyTier.Ultra));

            Assert.AreEqual(0f, config.SpecialWeaponSpawnChance(3), 0.0005f);
            Assert.AreEqual(0.35f, config.SpecialWeaponSpawnChance(4), 0.0005f);
            Assert.AreEqual(0.65f, config.SpecialWeaponSpawnChance(14), 0.0005f);
            Assert.AreEqual(0.65f, config.SpecialWeaponSpawnChance(99), 0.0005f);
        }

        [Test]
        public void SevkiyatAsseti_HasarModeliSayilariVarsayilanlaAyni()
        {
            PrototypeConfig config = SevkiyatConfig();

            Assert.AreEqual(34f, config.Enemies.StandardBaseHealth, 0.0001f);
            Assert.AreEqual(22f, config.Enemies.Armor.HelmetDurability, 0.0001f);
            Assert.AreEqual(38f, config.Enemies.Armor.BodyDurability, 0.0001f);
            Assert.AreEqual(28f, config.Enemies.Armor.LegDurability, 0.0001f);
            Assert.AreEqual(2f, config.Enemies.Armor.ExposedHeadMultiplier, 0.0001f);
            Assert.AreEqual(1f, config.Enemies.Armor.ExposedUpperBodyMultiplier, 0.0001f);
            Assert.AreEqual(0.8f, config.Enemies.Armor.ExposedLowerBodyMultiplier, 0.0001f);

            Assert.AreEqual(0.25f, config.Enemies.Explosion.HeadShare, 0.0001f);
            Assert.AreEqual(0.45f, config.Enemies.Explosion.UpperBodyShare, 0.0001f);
            Assert.AreEqual(0.30f, config.Enemies.Explosion.LowerBodyShare, 0.0001f);

            Assert.AreEqual(100f, config.Player.MaxHealth, 0.0001f);
        }

        [Test]
        public void SevkiyatAsseti_Dec021ElleTanimliYariBosKalmis()
        {
            PrototypeConfig config = SevkiyatConfig();
            Assert.AreEqual(0, config.RoundDifficultyOverrides.Count,
                "SEVKİYAT ASSET'İNDE DEC-021 override listesi doldurulmuş. " +
                "Kullanıcı kararı t-0002 gereği bu liste boş kalmalı.");
        }

        // -------------------------------------------------------------- sahne sıralaması

        /// <summary>
        /// WP-011: the pilot art scene (Assets/_Art/Scenes/Pilot/AP001_GoldenSlice.unity) is the
        /// sixth and last entry so its assets are part of the release build and the build size
        /// measurement. The first five stay exactly as they were — Boot is still the only
        /// entry scene and the meta flow is untouched.
        /// </summary>
        [Test]
        public void BuildSettings_BesSahneDogruSiradaVeAcik()
        {
            // WP-015 (ADR-015-2): enabled entries = the release scene list (five prototype scenes
            // + the AP-005 world scene Run loads additively); disabled entries = dev-only scenes
            // (WP-011 pilot, library showcase) that only the development build carries.
            string[] beklenenRelease =
            {
                PrototypeScenes.Boot,
                PrototypeScenes.MainMenu,
                PrototypeScenes.Selection,
                PrototypeScenes.Run,
                PrototypeScenes.Results,
                "World_AP005"
            };
            string[] beklenenDev = { "AP001_GoldenSlice", "AP_LibraryShowcase" };

            EditorBuildSettingsScene[] sahneler = EditorBuildSettings.scenes;
            Assert.AreEqual(beklenenRelease.Length + beklenenDev.Length, sahneler.Length,
                "Build Settings'te tam olarak sekiz giriş olmalı (altı release sahnesi + iki dev-only sahne), " + sahneler.Length + " var.");
            Assert.AreEqual("Assets/_Art/Scenes/World_AP005.unity", sahneler[5].path,
                "Altıncı sahne WP-014 AP-005 dünya sahnesi olmalı (Run'a additive yüklenir).");
            Assert.AreEqual("Assets/_Art/Scenes/Pilot/AP001_GoldenSlice.unity", sahneler[6].path,
                "Yedinci giriş WP-011 pilot sahnesi olmalı (dev-only, kapalı).");

            for (int i = 0; i < beklenenRelease.Length; i++)
            {
                string ad = Path.GetFileNameWithoutExtension(sahneler[i].path);
                Assert.AreEqual(beklenenRelease[i], ad, i + ". sıradaki sahne yanlış.");
                Assert.IsTrue(sahneler[i].enabled, ad + " sahnesi Build Settings'te kapalı (release listesi).");
                Assert.IsTrue(File.Exists(sahneler[i].path), ad + " sahnesi diskte yok: " + sahneler[i].path);
            }

            for (int i = 0; i < beklenenDev.Length; i++)
            {
                EditorBuildSettingsScene s = sahneler[beklenenRelease.Length + i];
                string ad = Path.GetFileNameWithoutExtension(s.path);
                Assert.AreEqual(beklenenDev[i], ad, (beklenenRelease.Length + i) + ". sıradaki dev-only sahne yanlış.");
                Assert.IsFalse(s.enabled, ad + " release listesinde (etkin) olmamalı — yalnız dev build (ADR-015-2).");
                Assert.IsTrue(File.Exists(s.path), ad + " sahnesi diskte yok: " + s.path);
            }

            // NOT (GT · yöntem düzeltmesi): Application.CanStreamedLevelBeLoaded EditMode'da
            // güvenilir değildir — oynatma dışında derlenmiş sahne listesi kurulmadığı için
            // Boot dahil her sahne için false döner. Bu kontrol PlayMode duman testine
            // taşındı; orada gerçek yüklenebilirlik ölçülüyor.
        }

        // ------------------------------------------------- TS-105 kopuk referans taraması

        private static readonly string[] TarananUzantilar =
        {
            "*.unity", "*.prefab", "*.asset", "*.mat", "*.controller", "*.inputactions"
        };

        private static List<string> TaranacakDosyalar()
        {
            var sonuc = new List<string>();
            for (int i = 0; i < TarananUzantilar.Length; i++)
            {
                sonuc.AddRange(Directory.GetFiles(AssetsRoot, TarananUzantilar[i], SearchOption.AllDirectories));
            }

            return sonuc;
        }

        [Test]
        public void BuildPipeline_VersiyonDamgasiVeBetiklerYerinde()
        {
            // WP-025: the stamp resource is loadable by name and the shared scripts exist at
            // the repository root (tools/), so build.sh / smoke.sh / backup-repo.sh are part of
            // the tree every session and S-10 can rely on them.
            var text = Resources.Load<TextAsset>(DD.Prototype.Core.Build.BuildInfo.ResourceName);
            Assert.IsNotNull(text, "Resources/build-info.json yüklenebilir olmalı");
            Assert.IsNotNull(DD.Prototype.Core.Build.BuildInfo.Parse(text.text));

            string root = Path.GetDirectoryName(Application.dataPath);
            for (int i = 0; i < 6 && root != null && !Directory.Exists(Path.Combine(root, ".git")) && !File.Exists(Path.Combine(root, ".git")); i++)
            {
                root = Path.GetDirectoryName(root);
            }

            Assert.IsNotNull(root, "depo kökü (.git) bulunmalı");
            foreach (string script in new[] { "tools/build.sh", "tools/smoke.sh", "tools/backup-repo.sh", "tools/README.md" })
            {
                Assert.IsTrue(File.Exists(Path.Combine(root, script)), script + " yok");
            }
        }

        [Test]
        public void KopukReferans_ScriptFileIdSifirYok()
        {
            var bulgular = new List<string>();
            List<string> dosyalar = TaranacakDosyalar();
            Assert.Greater(dosyalar.Count, 0, "Tarama hiç dosya görmedi; yöntem yalanlanmıştır.");

            foreach (string yol in dosyalar)
            {
                string[] satirlar = File.ReadAllLines(yol);
                for (int i = 0; i < satirlar.Length; i++)
                {
                    if (satirlar[i].Contains("m_Script: {fileID: 0}"))
                    {
                        bulgular.Add(yol + ":" + (i + 1));
                    }
                }
            }

            Assert.IsEmpty(bulgular,
                "Kopuk script referansı bulundu (" + bulgular.Count + "):\n" + string.Join("\n", bulgular));
        }

        [Test]
        public void KopukReferans_CozulmeyenGuidYok()
        {
            var guidDeseni = new Regex(@"guid: ([0-9a-f]{32})", RegexOptions.Compiled);
            var cozulmeyen = new Dictionary<string, string>();
            var cozulen = new HashSet<string>();

            List<string> dosyalar = TaranacakDosyalar();
            Assert.Greater(dosyalar.Count, 0, "Tarama hiç dosya görmedi; yöntem yalanlanmıştır.");

            int atifSayisi = 0;

            foreach (string yol in dosyalar)
            {
                foreach (string satir in File.ReadAllLines(yol))
                {
                    Match m = guidDeseni.Match(satir);
                    if (!m.Success)
                    {
                        continue;
                    }

                    string guid = m.Groups[1].Value;
                    atifSayisi++;

                    if (cozulen.Contains(guid) || cozulmeyen.ContainsKey(guid))
                    {
                        continue;
                    }

                    // Unity yerleşik asset'leri (0000000000000000...) AssetDatabase'e görünmez.
                    if (guid.StartsWith("0000000000000000"))
                    {
                        cozulen.Add(guid);
                        continue;
                    }

                    string cozum = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(cozum))
                    {
                        cozulmeyen[guid] = yol;
                    }
                    else
                    {
                        cozulen.Add(guid);
                    }
                }
            }

            Assert.Greater(atifSayisi, 0, "Hiç GUID atfı görülmedi; tarama deseni yalanlanmıştır.");

            if (cozulmeyen.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Çözülmeyen GUID (" + cozulmeyen.Count + "), " + atifSayisi + " atıf tarandı:");
                foreach (KeyValuePair<string, string> kv in cozulmeyen)
                {
                    sb.AppendLine("  " + kv.Key + "  <- " + kv.Value);
                }

                Assert.Fail(sb.ToString());
            }
        }

        [Test]
        public void EksikMetaDosyasiYok()
        {
            var eksik = new List<string>();
            string[] hepsi = Directory.GetFiles(AssetsRoot, "*", SearchOption.AllDirectories);

            foreach (string yol in hepsi)
            {
                if (yol.EndsWith(".meta") || yol.EndsWith(".DS_Store") || Path.GetFileName(yol).StartsWith("."))
                {
                    continue;
                }

                if (!File.Exists(yol + ".meta"))
                {
                    eksik.Add(yol);
                }
            }

            Assert.IsEmpty(eksik, "Meta dosyası eksik (" + eksik.Count + "):\n" + string.Join("\n", eksik));
        }

        [Test]
        public void SahnelerdeEksikScriptBileseniYok()
        {
            var bulgular = new List<string>();

            foreach (EditorBuildSettingsScene sahne in EditorBuildSettings.scenes)
            {
                if (!sahne.enabled || !File.Exists(sahne.path))
                {
                    continue;
                }

                UnityEngine.SceneManagement.Scene acik =
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                        sahne.path, UnityEditor.SceneManagement.OpenSceneMode.Single);

                foreach (GameObject kok in acik.GetRootGameObjects())
                {
                    foreach (Component c in kok.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null)
                        {
                            bulgular.Add(sahne.path + " içinde eksik script bileşeni var.");
                        }
                    }
                }
            }

            Assert.IsEmpty(bulgular, string.Join("\n", bulgular));
        }

        // ------------------------------------------------------- çevrimdışılık (K6 kapısı)

        [Test]
        public void CevrimdisiKalir_AgApisiKullanilmiyor()
        {
            var yasakli = new Regex(
                @"UnityWebRequest|System\.Net|HttpClient|TcpClient|UdpClient|WebSocket|Application\.OpenURL",
                RegexOptions.Compiled);

            var bulgular = new List<string>();
            string kok = Path.Combine("Assets", "_Prototype");

            foreach (string yol in Directory.GetFiles(kok, "*.cs", SearchOption.AllDirectories))
            {
                if (yol.Replace('\\', '/').Contains("/Tests/"))
                {
                    continue;
                }

                string[] satirlar = File.ReadAllLines(yol);
                for (int i = 0; i < satirlar.Length; i++)
                {
                    if (yasakli.IsMatch(satirlar[i]))
                    {
                        bulgular.Add(yol + ":" + (i + 1) + "  " + satirlar[i].Trim());
                    }
                }
            }

            Assert.IsEmpty(bulgular,
                "Prototip çevrimdışı olmalı, ağ API'si bulundu:\n" + string.Join("\n", bulgular));
        }

        // ------------------------------------------------------- planning bütünlüğü (WP-015)

        /// <summary>
        /// WP-015 · The planning documents are the decision record the code answers to:
        /// DEC ids in planning/00 are unique and contiguous from 001, REQ ids in planning/01
        /// are unique (the table has authored gaps), and the WP-015 user decisions (DEC-076..080) and the DEC-077 readability
        /// criteria on REQ-104/105 are present. The repo root is two levels above the Unity
        /// project; skipped (inconclusive) only if the planning folder is absent.
        /// </summary>
        [Test]
        public void Planning_DecVeReqNumaralari_TekilVeArdisik_Wp015KararlariKayitli()
        {
            string kok = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "planning"));
            if (!Directory.Exists(kok))
            {
                Assert.Inconclusive("planning klasörü yok: " + kok);
            }

            string dec = File.ReadAllText(Path.Combine(kok, "00-PROJECT-PLAN.md"));
            string req = File.ReadAllText(Path.Combine(kok, "01-REQUIREMENTS.md"));

            List<int> decIds = TabloKimlikleri(dec, "DEC");
            List<int> reqIds = TabloKimlikleri(req, "REQ");
            Assert.Greater(decIds.Count, 0);
            Assert.Greater(reqIds.Count, 0);
            for (int i = 0; i < decIds.Count; i++) Assert.AreEqual(i + 1, decIds[i], "DEC numaraları ardışık/tekil değil: " + decIds[i]);
            // REQ ids are unique; the table has authored gaps (91–99, 116–119 were never issued).
            for (int i = 1; i < reqIds.Count; i++) Assert.Greater(reqIds[i], reqIds[i - 1], "REQ numarası tekrar ediyor: " + reqIds[i]);
            Assert.GreaterOrEqual(decIds.Count, 80, "DEC-076..080 (WP-015 kullanıcı kararları) kayıtlı olmalı.");
            Assert.AreEqual(155, reqIds[reqIds.Count - 1], "son REQ numarası");

            foreach (string id in new[] { "DEC-076", "DEC-077", "DEC-078", "DEC-079", "DEC-080" })
            {
                StringAssert.Contains("| " + id + " |", dec, id + " planning/00'da yok.");
            }

            Match r104 = Regex.Match(req, @"^\| REQ-104 \|.*$", RegexOptions.Multiline);
            Match r105 = Regex.Match(req, @"^\| REQ-105 \|.*$", RegexOptions.Multiline);
            Assert.IsTrue(r104.Success && r105.Success);
            StringAssert.Contains("DEC-077", r104.Value, "REQ-104 ölçülebilir ölçütü (DEC-077) yok.");
            StringAssert.Contains("0,06", r104.Value);
            StringAssert.Contains("%50", r104.Value);
            StringAssert.Contains("DEC-077", r105.Value, "REQ-105 ölçülebilir ölçütü (DEC-077) yok.");
            Debug.Log("[DD-TEST] wp015.planning dec=" + decIds.Count + " req=" + reqIds.Count);
        }

        private static List<int> TabloKimlikleri(string metin, string onek)
        {
            var ids = new List<int>();
            foreach (Match m in Regex.Matches(metin, @"^\| " + onek + @"-(\d{3}) \|", RegexOptions.Multiline))
            {
                ids.Add(int.Parse(m.Groups[1].Value));
            }

            ids.Sort();
            return ids;
        }
    }
}
