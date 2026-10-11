using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-077 B2b · meta YOL (DEC-095 katman 2) + WP-104 Uyanış: katalog (SPECIAL 10 / PASSIVES 16 / AWAKENING 12, 6×3 sayfa;
    /// silah sayfası emekli),
    /// yedi durum (seviye kapısı, merdiven silahı, bağlanmamış etki, ön koşul, dışlama, Kül), atomik satın alma
    /// (aday klon → doğrula → kaydet; başarısız kayıtta RAM = disk; ikinci alım AlreadyOwned), silah düğümü
    /// silahı açar, respec (silahlar kalır, ilk %100 sonra %75), koşu değiştiricileri (reroll 7, Ocak eğimi 27,
    /// Kese başlangıçta, doruk indirimi, K6 ustalık kapısı), S-09 anlık görüntüsü. İzole profil yolu.
    /// </summary>
    [TestFixture]
    public sealed class MetaPathTests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-editmode-wp077-meta", "prototype-profile.json");

        private static readonly string RealPath = Path.Combine(Application.persistentDataPath, ProfileStore.FileName);
        private string _realHashBefore;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realHashBefore = HashOf(RealPath);
            Directory.CreateDirectory(Path.GetDirectoryName(TestPath));
            ProfileStore.SetPathOverride(TestPath);
            Bosalt();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Bosalt();
            ProfileStore.SetPathOverride(null);
            try { Directory.Delete(Path.GetDirectoryName(TestPath), true); } catch (Exception) { }
            Assert.AreEqual(_realHashBefore, HashOf(RealPath), "gerçek profil dosyası değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            Assert.AreEqual(TestPath, ProfileStore.Path);
            ProfileStore.SaveStageHook = null;
            MetaPathEffects.TreatAllAsLive = false;
            ProfileService.ResetProfile();
        }

        [TearDown]
        public void TearDown()
        {
            ProfileStore.SaveStageHook = null;
            MetaPathEffects.TreatAllAsLive = false;
        }

        private static void Bosalt()
        {
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }

        private static string HashOf(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        private static string Disk() => File.Exists(TestPath) ? File.ReadAllText(TestPath) : "MISSING";

        /// <summary>Tek koşuyla seviye + Kül kazan (izole profil).</summary>
        private static void Kazan(int xp)
        {
            RunRewardReport r = ProfileService.ApplyRun(new RunSummary
            {
                CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(),
                ReachedRound = 3, CompletedRounds = 2, CompletedRoundXp = xp, TotalXpEarned = xp,
                Weapon = ClassicWeaponId.AssaultRifle, EndReason = RunEndReason.PlayerDeath
            });
            Assert.IsTrue(r.Applied);
        }

        private static RunTreeNodeStatus Durum(string id, out MetaLockReason neden) =>
            MetaPathRules.Status(ProfileService.Current, MetaPathCatalog.IndexOf(id), out neden, out _, out _);

        [Test]
        public void Katalog_Gecerli_Sayfalar_ve_Uyanis()
        {
            TextAsset t = Resources.Load<TextAsset>(MetaPathCatalog.ResourcePath);
            Assert.IsNotNull(t);
            var doc = JsonUtility.FromJson<MetaPathCatalogDocument>(t.text);
            var hatalar = MetaPathCatalog.Validate(doc);
            Assert.IsEmpty(hatalar, string.Join("; ", hatalar));
            int w = 0, s = 0, p = 0, a = 0, kul = 0;
            foreach (MetaPathNode n in MetaPathCatalog.Nodes)
            {
                if (n.Page == MetaPage.Weapons) w++;
                else if (n.Page == MetaPage.Special) s++;
                else if (n.Page == MetaPage.Passives) p++;
                else if (n.Page == MetaPage.Awakening) { a++; kul += n.AshCost; Assert.AreEqual(MetaNodeKind.Awakening, n.Kind, n.Id); }
                Assert.AreNotEqual(MetaNodeKind.Weapon, n.Kind, n.Id + ": WP-104 silah sayfası emekli");
            }

            // WP-104 (C-00 11.10 karar 2): silah sayfası emekli → Uyanış (GD-TREE §3, 12 düğüm ≈ 3 250 Kül); Special korunur;
            // Pasif'ten capstone indirimi (pa_eco_4) ve Kese başlangıçta (pa_eco_3, Kese emekli) çıktı.
            Assert.AreEqual(0, w); Assert.AreEqual(10, s); Assert.AreEqual(16, p); Assert.AreEqual(12, a);
            Assert.AreEqual(3250, kul, "Uyanış toplamı");
            Assert.IsNull(MetaPathCatalog.Find("w_ar_2"));
            Assert.IsNull(MetaPathCatalog.Find("pa_eco_3"));
            Assert.IsNull(MetaPathCatalog.Find("pa_eco_4"));
            Assert.AreEqual(400, MetaPathCatalog.Find("aw_twincore").AshCost);
            Assert.AreEqual(350, MetaPathCatalog.Find("aw_third").AshCost);
            MetaPathNode bes = MetaPathCatalog.Find("pa_eco_5");
            Assert.AreEqual(1, bes.Parents.Length);
            Assert.AreEqual("pa_eco_2", MetaPathCatalog.Nodes[bes.Parents[0]].Id, "zincir pa_eco_2'ye bağlanır");
        }

        [Test]
        public void Dogrulayici_SilahSayfasiniVeUyumsuzUyanisiReddeder()
        {
            var doc = new MetaPathCatalogDocument
            {
                schema = MetaPathCatalog.Schema,
                nodes = new[]
                {
                    new MetaPathCatalogNode { id = "w_ar_1", page = "weapons", kind = "Weapon", shortName = "Hinawa", weaponKey = "ar_1", effectId = "weapon.ar_1" },
                    new MetaPathCatalogNode { id = "aw_x", page = "passives", row = 1, kind = "Awakening", shortName = "X", effectId = "awakening.x" },
                    new MetaPathCatalogNode { id = "aw_y", page = "awakening", row = 2, kind = "Awakening", shortName = "Y", effectId = "meta.eco.reroll" },
                    new MetaPathCatalogNode { id = "aw_ok", page = "awakening", kind = "Awakening", shortName = "OK", effectId = "awakening.ok" }
                }
            };
            var hatalar = MetaPathCatalog.Validate(doc);
            Assert.IsTrue(hatalar.Exists(h => h.StartsWith("w_ar_1") && h.Contains("retired")), string.Join("; ", hatalar));
            Assert.IsTrue(hatalar.Exists(h => h.StartsWith("aw_x")), "Uyanış türü yalnız Uyanış sayfasında");
            Assert.IsTrue(hatalar.Exists(h => h.StartsWith("aw_y")), "Uyanış etki kimliği awakening. ile başlar");
            Assert.IsFalse(hatalar.Exists(h => h.StartsWith("aw_ok")));
            Assert.IsTrue(MetaPathEffects.IsLive("awakening.third_affinity"), "Uyanış etkileri canlı (TreeAwakening okur)");
        }

        [Test]
        public void TazeProfil_Durumlar()
        {
            Assert.AreEqual(RunTreeNodeStatus.Locked, Durum("pa_body_1", out MetaLockReason c));
            Assert.AreEqual(MetaLockReason.LevelGate, c, "L1 < L2");
            Assert.AreEqual(RunTreeNodeStatus.Unaffordable, Durum("aw_codex", out MetaLockReason d), "Uyanış: kapı L1, etki canlı, Kül yok");
            Assert.AreEqual(MetaLockReason.None, d);
        }

        [Test]
        public void UyanisSatinAlma_Atomik_Idempotan()
        {
            Kazan(20000);   // ≈ L14, Kül = 666 + başarımlar
            ProfileData p = ProfileService.Current;
            int once = p.AshBalance;
            int cost = MetaPathCatalog.Find("aw_codex").AshCost;

            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("aw_codex"));
            p = ProfileService.Current;
            Assert.AreEqual(once - cost, p.AshBalance);
            Assert.IsTrue(p.MetaNodes.Contains("aw_codex"));
            Assert.IsTrue(TreeAwakening.FromProfile(p).SynergyCodex, "koşu ağacı Uyanış'ı profilden okur");
            Assert.AreEqual(JsonUtility.ToJson(p), JsonUtility.ToJson(Oku()), "canlı = disk");
            Assert.AreEqual(MetaCommandResult.AlreadyOwned, ProfileService.TryBuyMetaNode("aw_codex"), "ikinci alım yok");
            Assert.AreEqual(once - cost, ProfileService.Current.AshBalance);
            Assert.AreEqual(MetaCommandResult.UnknownNode, ProfileService.TryBuyMetaNode("w_ar_2"), "WP-104: silah düğümü yok");
        }

        [Test]
        public void YetersizKul_KilitliEtki_OnKosul_HicbirSeyYazmaz()
        {
            Kazan(1500);   // L5 civarı, Kül ≈ 50 + başarımlar (DEC-098 XP/30)
            string disk = Disk();
            Assert.AreEqual(MetaCommandResult.NotAvailable, ProfileService.TryBuyMetaNode("sp_early_bell"), "seviye kapısı (L7)");
            Assert.AreEqual(MetaCommandResult.NotAvailable, ProfileService.TryBuyMetaNode("pa_body_1"), "bağlanmamış etki satılmaz");
            Assert.AreEqual(MetaCommandResult.NotAvailable, ProfileService.TryBuyMetaNode("pa_eco_2"), "ön koşul: pa_eco_1");
            Assert.AreEqual(MetaCommandResult.InsufficientAsh, ProfileService.TryBuyMetaNode("aw_twincore"), "400 Kül yok");
            Assert.AreEqual(MetaCommandResult.UnknownNode, ProfileService.TryBuyMetaNode("nope"));
            MetaPathEffects.TreatAllAsLive = true;
            ProfileData p = ProfileService.Current;
            if (p.AshBalance < MetaPathCatalog.Find("pa_eco_1").AshCost)
            {
                Assert.AreEqual(MetaCommandResult.InsufficientAsh, ProfileService.TryBuyMetaNode("pa_eco_1"));
            }

            Assert.AreEqual(disk, Disk(), "reddedilen komut diske dokunmaz");
        }

        [Test]
        public void KayitHatasi_RamDegismez()
        {
            Kazan(20000);
            string ram = JsonUtility.ToJson(ProfileService.Current);
            ProfileStore.SaveStageHook = s => { if (s == ProfileSaveStage.Replace) throw new IOException("injected"); };
            LogAssert.Expect(LogType.Warning, new Regex("Save failed at Replace \\(IOException\\)"));
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            Assert.AreEqual(MetaCommandResult.SaveFailed, ProfileService.TryBuyMetaNode("pa_eco_1"));
            ProfileStore.SaveStageHook = null;
            Assert.AreEqual(ram, JsonUtility.ToJson(ProfileService.Current), "başarısız kayıt RAM'i değiştirmez");
        }

        [Test]
        public void SilahKilidi_Secim_SatiriSorgusu()
        {
            // WP-104: no weapon opens on the PATH any more — every weapon reads its ladder / entitlement, never Ash.
            MetaPathRules.WeaponLockInfo ar1 = ProfileService.WeaponLock(ClassicWeaponId.AssaultRifle);
            Assert.IsTrue(ar1.Entitled && ar1.Ladder);
            foreach (ClassicWeaponId id in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                MetaPathRules.WeaponLockInfo info = ProfileService.WeaponLock(id);
                Assert.IsFalse(info.OnPath, id + ": meta YOL silah açmaz");
                Assert.AreEqual(0, info.AshCost, id + ": Kül yok");
            }
        }

        [Test]
        public void Respec_SilahlarKalir_IlkTam_SonraYuzde75()
        {
            Kazan(20000);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_1"));   // 40 (DEC-098)
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_2"));   // 70
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("aw_codex"));   // 100 (Uyanış da iade edilir)
            int once = ProfileService.Current.AshBalance;

            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryMetaRespec(out int iade));
            Assert.AreEqual(210, iade, "ilk respec %100");
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(once + 210, p.AshBalance);
            Assert.IsTrue(p.UnlockedWeapons.Contains("ar_1"), "silahlar kalır");
            Assert.IsFalse(p.MetaNodes.Contains("pa_eco_1"));
            Assert.IsFalse(p.MetaNodes.Contains("aw_codex"));

            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_1"));
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryMetaRespec(out int iade2));
            Assert.AreEqual(30, iade2, "ikinci respec %75 (40 → 30)");
            Assert.AreEqual(MetaCommandResult.NothingToRefund, ProfileService.TryMetaRespec(out _));
        }

        [Test]
        public void KosuDegistiricileri_DukkanVeAgac()
        {
            Kazan(40000);   // ≈ L19
            foreach (string id in new[] { "pa_eco_1", "pa_eco_2" })
                Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode(id), id);

            MetaRunModifiers m = MetaPathRules.RunModifiers(ProfileService.Current, "ar_1");
            Assert.AreEqual(7, m.RerollCostOverride);
            Assert.AreEqual(27, m.HearthCostStepOverride);
            Assert.IsFalse(m.PurseAtStart, "WP-104: pa_eco_3 emekli");
            Assert.AreEqual(0f, m.CapstoneDiscount, "WP-104: pa_eco_4 emekli");
            Assert.IsFalse(m.CapstoneMasteryMet, "K6: ar_1 ustalığı < 150 öldürme (M1)");

            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                var cuzdan = new GoldWallet();
                cuzdan.InitializeForRun(1);
                var dukkan = new RoundUpgradeShop { UnlockedPredicate = _ => true, Random = new SeededUpgradeOfferRandom(3), WeaponProvider = () => ClassicWeaponId.AssaultRifle, Meta = m };
                dukkan.Open(1, config, cuzdan, new WeaponModifierState(), 44);
                Assert.AreEqual(7, dukkan.RerollCost, "reroll 10 → 7");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void AnlikGoruntu_S09()
        {
            Kazan(20000);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.SetMetaPin("aw_codex"));
            var s = new MetaPathSnapshot();
            s.Refresh();
            Assert.AreEqual(38, s.Count, "Special 10 + Pasif 16 + Uyanış 12");
            Assert.AreEqual(s.IndexOf("aw_codex"), s.NextIndex);
            Assert.IsFalse(s.NextIsAuto);
            Assert.IsTrue(s.Nodes[s.NextIndex].IsPinned);
            Assert.Greater(s.LastRunAsh, 0);
            Assert.AreEqual(ProfileService.Current.AshBalance, s.AshBalance);
            Assert.IsTrue(s.RespecFree);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("aw_codex"));
            s.Refresh();
            Assert.IsTrue(s.NextIsAuto, "pin satın alındı → otomatik NEXT");
            Assert.IsTrue(s.HasNew);
            ProfileService.MarkMetaSeen();
            s.Refresh();
            Assert.IsFalse(s.HasNew);
        }

        private static ProfileData Oku()
        {
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(TestPath, out ProfileData d));
            return d;
        }
    }
}
