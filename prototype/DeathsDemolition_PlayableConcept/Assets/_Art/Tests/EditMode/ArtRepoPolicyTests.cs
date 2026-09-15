using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Core.Boot;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-011 · Depo politikası nöbetçisi (ADR-011-2): ikili sanat varlıkları Git LFS'te,
    /// layout JSON'u kaynak GLB'nin sha256'sıyla izlenir, Build Settings'te pilot sahne
    /// 6. sırada ve ilk beş dokunulmamış.
    ///
    /// Depo kökü Unity projesinin iki üstündedir; test yolu sabitlemez, <c>.gitattributes</c>
    /// bulunana kadar yukarı yürür (worktree'de <c>.git</c> bir dosyadır, klasör değil).
    /// Provenance manifesti (82 giriş) depoda değil, wp-011/manifest altındadır; onun yerine
    /// depo içindeki JSON'un sha kaydı ve sayımları doğrulanır.
    /// </summary>
    [TestFixture]
    [Category("WP011")]
    public sealed class ArtRepoPolicyTests
    {
        /// <summary>Assets/_Art altında metin sayılan uzantılar; kalan her uzantı ikilidir ve LFS kuralı ister.</summary>
        private static readonly HashSet<string> MetinUzantilar = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".asmdef", ".asmref", ".json", ".md", ".txt", ".meta",
            ".asset", ".mat", ".prefab", ".unity", ".shader", ".hlsl", ".cginc"
        };

        private static readonly Regex HexSha256 = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled);

        // ------------------------------------------------------------------ depo kökü

        private static string DepoKoku()
        {
            string dizin = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dizin); i++)
            {
                if (File.Exists(Path.Combine(dizin, ".gitattributes")))
                {
                    return dizin;
                }

                DirectoryInfo ust = Directory.GetParent(dizin);
                dizin = ust != null ? ust.FullName : null;
            }

            Assert.Fail(".gitattributes bulunamadı: Unity projesinden yukarı 8 seviye tarandı (" + Application.dataPath + ").");
            return null;
        }

        /// <summary>.gitattributes'ta 'filter=lfs' taşıyan desenler (örn. "*.fbx"). Yorumlar ve boş satırlar atlanır.</summary>
        private static HashSet<string> LfsDesenleri(string gitattributes)
        {
            var desenler = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string hamSatir in File.ReadAllLines(gitattributes))
            {
                string satir = hamSatir.Trim();
                if (satir.Length == 0 || satir.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parcalar = satir.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parcalar.Length < 2)
                {
                    continue;
                }

                bool lfs = false;
                for (int i = 1; i < parcalar.Length; i++)
                {
                    if (parcalar[i] == "filter=lfs")
                    {
                        lfs = true;
                    }
                }

                if (lfs)
                {
                    desenler.Add(parcalar[0]);
                }
            }

            return desenler;
        }

        // ------------------------------------------------------------------ LFS kapsamı

        [Test]
        public void GitAttributes_ArtAltindakiHerIkiliUzanti_LfsFiltresinde()
        {
            string kok = DepoKoku();
            string gitattributes = Path.Combine(kok, ".gitattributes");
            HashSet<string> lfs = LfsDesenleri(gitattributes);
            Assert.Greater(lfs.Count, 0, ".gitattributes'ta hiç filter=lfs kuralı yok: " + gitattributes);

            var ikiliUzantilar = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string yol in Directory.GetFiles(ArtImportContract.ArtRoot, "*", SearchOption.AllDirectories))
            {
                string ad = Path.GetFileName(yol);
                if (ad.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                string uzanti = Path.GetExtension(yol);
                if (string.IsNullOrEmpty(uzanti) || MetinUzantilar.Contains(uzanti))
                {
                    continue;
                }

                ikiliUzantilar.Add(uzanti.ToLowerInvariant());
            }

            Assert.IsTrue(ikiliUzantilar.Contains(".fbx") && ikiliUzantilar.Contains(".png"),
                "Tarama .fbx ve .png görmeliydi; görülen: " + string.Join(" ", ikiliUzantilar));

            var kapsamDisi = new List<string>();
            foreach (string uzanti in ikiliUzantilar)
            {
                if (!lfs.Contains("*" + uzanti))
                {
                    kapsamDisi.Add(uzanti);
                }
            }

            Assert.IsEmpty(kapsamDisi, "LFS kuralı olmayan ikili uzantı (" + kapsamDisi.Count + "): " + string.Join(" ", kapsamDisi) + " — " + gitattributes);
            Debug.Log("[DD-TEST] wp011.repo.lfs root=" + kok + " binaryExt=" + string.Join(",", ikiliUzantilar) + " lfsPatterns=" + lfs.Count);
        }

        [Test]
        public void GitAttributes_LfsKurali_DiffVeMergeSurucusuVeMetinDegil()
        {
            // "*.fbx filter=lfs diff=lfs merge=lfs -text": yalnız filter yetmez; -text olmadan
            // satır sonu normalizasyonu ikiliyi bozar, diff/merge sürücüsü olmadan çakışma çözülmez.
            string gitattributes = Path.Combine(DepoKoku(), ".gitattributes");
            var eksik = new List<string>();
            foreach (string hamSatir in File.ReadAllLines(gitattributes))
            {
                string satir = hamSatir.Trim();
                if (satir.StartsWith("#", StringComparison.Ordinal) || !satir.Contains("filter=lfs"))
                {
                    continue;
                }

                if (!satir.Contains("diff=lfs") || !satir.Contains("merge=lfs") || !satir.Contains("-text"))
                {
                    eksik.Add(satir);
                }
            }

            Assert.IsEmpty(eksik, "Eksik LFS öznitelikli satır (" + eksik.Count + "):\n" + string.Join("\n", eksik));
        }

        // ------------------------------------------------------------------ provenance (depo içi)

        [Test]
        public void LayoutJson_VarSourceGlbSha256VeSayimlar()
        {
            Assert.IsTrue(File.Exists(ArtPipelinePaths.LayoutJson), "Layout JSON yok: " + ArtPipelinePaths.LayoutJson);

            string sha = ArtLayoutJson.SourceGlbSha256();
            Assert.IsTrue(sha != null && HexSha256.IsMatch(sha), "source_glb.sha256 64-hex değil: '" + sha + "'");
            Assert.AreEqual(823, ArtLayoutJson.IntField("family_instance_count"));
            Assert.AreEqual(875, ArtLayoutJson.IntField("instance_count_total"));
            Assert.AreEqual(52, ArtLayoutJson.IntField("non_family_unique_count"));
            Assert.AreEqual(823, ArtLayoutJson.FamilyInstances().Count, "family_instances giriş sayısı");
            Assert.AreEqual(18, ArtLayoutJson.Families().Count);
        }

        [Test]
        public void ArtKlasorAgaci_ADR0112IleAyni()
        {
            // ADR-011-2 kökleri: sanat içeriği yalnız bu altında ve bu şekilde örgütlü.
            string[] klasorler =
            {
                ArtPipelinePaths.FamiliesRoot,
                ArtPipelinePaths.GoldenSliceRoot,
                ArtPipelinePaths.TexturesSource,
                ArtPipelinePaths.TexturesDerived,
                ArtPipelinePaths.Materials,
                ArtPipelinePaths.ArtRoot + "/Runtime",
                ArtPipelinePaths.ArtRoot + "/Editor",
                ArtPipelinePaths.ArtRoot + "/Scenes/Pilot",
                ArtPipelinePaths.ArtRoot + "/Tests/EditMode",
                ArtPipelinePaths.ArtRoot + "/Tests/PlayMode"
            };

            var eksik = new List<string>();
            foreach (string k in klasorler)
            {
                if (!Directory.Exists(k)) eksik.Add(k);
            }

            Assert.IsEmpty(eksik, "ADR-011-2 klasörü yok (" + eksik.Count + "):\n" + string.Join("\n", eksik));
            Assert.AreEqual("Assets/_Art", ArtPipelinePaths.ArtRoot);
            Assert.AreEqual(ArtImportContract.ArtRoot, ArtPipelinePaths.ArtRoot, "Sözleşme kökü ile yol kökü aynı olmalı.");
            Assert.IsFalse(Directory.Exists("Assets/_Art/Environment/GoldenSlice/AP-001"), "AP-00x klasörleri depoya kopyalanmaz.");
        }

        // ------------------------------------------------------------------ build settings

        [Test]
        public void BuildSettings_AltiSahne_IlkBesDegismemis_AltinciPilot()
        {
            string[] ilkBes =
            {
                "Assets/_Prototype/Scenes/" + PrototypeScenes.Boot + ".unity",
                "Assets/_Prototype/Scenes/" + PrototypeScenes.MainMenu + ".unity",
                "Assets/_Prototype/Scenes/" + PrototypeScenes.Selection + ".unity",
                "Assets/_Prototype/Scenes/" + PrototypeScenes.Run + ".unity",
                "Assets/_Prototype/Scenes/" + PrototypeScenes.Results + ".unity"
            };

            EditorBuildSettingsScene[] sahneler = EditorBuildSettings.scenes;
            var yollar = new List<string>();
            foreach (EditorBuildSettingsScene s in sahneler)
            {
                yollar.Add((s.enabled ? "" : "[kapalı] ") + s.path);
            }

            Assert.AreEqual(6, sahneler.Length, "Build Settings'te tam 6 sahne olmalı (5 prototip + pilot); " + sahneler.Length + " var:\n" + string.Join("\n", yollar));

            for (int i = 0; i < ilkBes.Length; i++)
            {
                Assert.AreEqual(ilkBes[i], sahneler[i].path, i + ". sıradaki sahne yolu değişmiş.");
                Assert.IsTrue(sahneler[i].enabled, ilkBes[i] + " kapalı.");
                Assert.IsTrue(File.Exists(sahneler[i].path), ilkBes[i] + " diskte yok.");
            }

            Assert.AreEqual(ArtPipelinePaths.PilotScene, sahneler[5].path, "6. sahne pilot olmalı.");
            Assert.IsTrue(sahneler[5].enabled, "Pilot sahne kapalı.");
            Assert.IsTrue(File.Exists(sahneler[5].path), "Pilot sahne diskte yok: " + sahneler[5].path);
            Assert.AreEqual(ArtPipelinePaths.PilotSceneName, Path.GetFileNameWithoutExtension(sahneler[5].path), "Pilot sahne adı sözleşmeyle aynı olmalı.");

            foreach (EditorBuildSettingsScene s in sahneler)
            {
                Assert.AreNotEqual(ArtPipelinePaths.KitShelfScene, s.path, "Kit rafı sahnesi editor/test içindir; Build Settings'e girmez.");
            }
        }

        [Test]
        public void KitRafiSahnesi_DiskteVar_BuildDisi()
        {
            Assert.IsTrue(File.Exists(ArtPipelinePaths.KitShelfScene), "Kit rafı sahnesi yok: " + ArtPipelinePaths.KitShelfScene);
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                Assert.AreNotEqual(ArtPipelinePaths.KitShelfScene, s.path);
            }
        }
    }
}
