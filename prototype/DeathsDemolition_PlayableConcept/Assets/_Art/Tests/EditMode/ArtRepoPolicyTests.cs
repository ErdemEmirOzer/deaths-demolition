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
            ".asset", ".mat", ".prefab", ".unity", ".shader", ".hlsl", ".cginc", ".controller", ".mask", ".anim",
            ".lighting"   // WP-029 LightingSettings (YAML text, like .asset); the binary LightingData.asset is covered by its own LFS rule
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
                ArtPipelinePaths.ArtRoot + "/Tests/PlayMode",
                // WP-012 (ADR-012-2): kütüphane kökleri, paylaşılan ışık tabanı, Resources gökyüzü
                ArtLibraryPaths.LibraryRoot("AP-002"),
                ArtLibraryPaths.LibraryRoot("AP-003"),
                ArtLibraryPaths.LibraryRoot("AP-003B"),
                ArtLibraryPaths.LightingFolder,
                ArtPipelinePaths.ArtRoot + "/Resources",
                // WP-016 (ADR-016-1): skinned characters
                ArtCharacterContract.PlayerFolder,
                ArtCharacterContract.ZombieFolder,
                ArtCharacterContract.ZombieFolder + "/Modules",
                ArtCharacterPrefabBuilder.PlayerTextures,
                ArtCharacterPrefabBuilder.ZombieTextures
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

        /// <summary>
        /// WP-015 (ADR-015-2) build list policy: the ENABLED entries are the release scenes
        /// (five prototype scenes unchanged + the AP-005 world), contiguous and first; the
        /// DISABLED entries that follow are dev-only (pilot, showcase) and ship only in the
        /// development build (LifecycleProbeBuild.DevelopmentScenes).
        /// </summary>
        [Test]
        public void BuildSettings_ReleaseAltiSahneEtkin_PilotVeVitrinDevOnlyKapali()
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

            Assert.AreEqual(9, sahneler.Length, "Build Settings'te tam 9 giriş olmalı (5 prototip + AP-005 dünyası + World v5 (V6) etkin; pilot + vitrin kapalı); " + sahneler.Length + " var:\n" + string.Join("\n", yollar));

            for (int i = 0; i < ilkBes.Length; i++)
            {
                Assert.AreEqual(ilkBes[i], sahneler[i].path, i + ". sıradaki sahne yolu değişmiş.");
                Assert.IsTrue(sahneler[i].enabled, ilkBes[i] + " kapalı.");
                Assert.IsTrue(File.Exists(sahneler[i].path), ilkBes[i] + " diskte yok.");
            }

            Assert.AreEqual(ArtLibraryPaths.Ap005WorldScene, sahneler[5].path, "6. sahne AP-005 dünyası olmalı (WP-014).");
            Assert.IsTrue(sahneler[5].enabled, "AP-005 dünya sahnesi release listesinde etkin olmalı.");
            Assert.IsTrue(File.Exists(sahneler[5].path), "AP-005 dünya sahnesi diskte yok: " + sahneler[5].path);
            Assert.AreEqual(ArtLibraryPaths.Ap005WorldSceneName, Path.GetFileNameWithoutExtension(sahneler[5].path), "AP-005 sahne adı sözleşmeyle aynı olmalı.");

            // C-00 25.09: World v5 (the v6 layout, World_V6_Greybox) is the default world — a release entry right after World_AP005
            // (GreyboxV6Builder registers it; DD_WorldSource on V6); World_AP005 stays behind -ddWorldSource AP005.
            Assert.AreEqual(ArtLibraryPaths.GreyboxV6Scene, sahneler[6].path, "7. giriş World v5 (V6) olmalı (varsayılan dünya).");
            Assert.IsTrue(sahneler[6].enabled, "World v5 release listesinde etkin olmalı (varsayılan dünya, C-00 25.09).");
            Assert.IsTrue(File.Exists(sahneler[6].path), "World v5 sahnesi diskte yok: " + sahneler[6].path);

            Assert.AreEqual(ArtPipelinePaths.PilotScene, sahneler[7].path, "8. giriş pilot olmalı (dev-only).");
            Assert.IsFalse(sahneler[7].enabled, "Pilot sahne release listesinde olmamalı (ADR-015-2: yalnız dev build).");
            Assert.IsTrue(File.Exists(sahneler[7].path), "Pilot sahne diskte yok: " + sahneler[7].path);
            Assert.AreEqual(ArtPipelinePaths.PilotSceneName, Path.GetFileNameWithoutExtension(sahneler[7].path), "Pilot sahne adı sözleşmeyle aynı olmalı.");

            Assert.AreEqual(ArtLibraryPaths.ShowcaseScene, sahneler[8].path, "9. giriş vitrin olmalı (dev-only).");
            Assert.IsFalse(sahneler[8].enabled, "Vitrin release listesinde olmamalı (ADR-015-2: yalnız dev build).");
            Assert.IsTrue(File.Exists(sahneler[8].path), "Vitrin sahnesi diskte yok: " + sahneler[8].path);

            // Release list contiguous: no enabled entry after a disabled one.
            bool disabledSeen = false;
            foreach (EditorBuildSettingsScene s in sahneler)
            {
                if (!s.enabled) disabledSeen = true;
                else Assert.IsFalse(disabledSeen, "Etkin sahne kapalı bir girişten sonra geliyor: " + s.path);
                Assert.AreNotEqual(ArtPipelinePaths.KitShelfScene, s.path, "Kit rafı sahnesi editor/test içindir; Build Settings'e girmez.");
            }

            // The development build carries release + dev-only, in that order (pure function).
            string[] dev = DD.Prototype.Core.EditorTools.LifecycleProbeBuild.DevelopmentScenes(sahneler);
            Assert.AreEqual(9, dev.Length);
            Assert.AreEqual(ilkBes[0], dev[0], "dev build giriş sahnesi Boot");
            Assert.AreEqual(ArtLibraryPaths.Ap005WorldScene, dev[5]);
            Assert.AreEqual(ArtLibraryPaths.GreyboxV6Scene, dev[6], "World v5 (V6) release listesinde, dev build'de de");
            Assert.AreEqual(ArtPipelinePaths.PilotScene, dev[7]);
            Assert.AreEqual(ArtLibraryPaths.ShowcaseScene, dev[8]);
            Debug.Log("[DD-TEST] wp015.buildlist release=7 devOnly=2");
        }

        /// <summary>WP-015 (ADR-015-1) → WP-075 C1 (ADR-015-1 v3): mipmap streaming is on at every quality level with its tier's budget (512 / 1024 / 2048 MB; Standalone default High).</summary>
        [Test]
        public void QualitySettings_MipmapStreaming_HerSeviyedeAcik_Butce()
        {
            int asil = QualitySettings.GetQualityLevel();
            try
            {
                string[] names = QualitySettings.names;
                Assert.Greater(names.Length, 0);
                for (int i = 0; i < names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    Assert.IsTrue(QualitySettings.streamingMipmapsActive, names[i] + ": streamingMipmapsActive kapalı (ADR-015-1).");
                    Assert.AreEqual(DD.Prototype.Core.QualityTiers.StreamingBudgetMb[(int)DD.Prototype.Core.QualityTiers.TierOfLevel(i, names.Length)], QualitySettings.streamingMipmapsMemoryBudget, 0.01f, names[i] + ": streaming bütçesi");
                    Assert.IsTrue(QualitySettings.streamingMipmapsAddAllCameras, names[i] + ": tüm kameralar streaming'e katılmalı");
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(asil, false);
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
