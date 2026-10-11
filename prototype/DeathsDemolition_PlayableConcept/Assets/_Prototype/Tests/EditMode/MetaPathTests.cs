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
    /// WP-077 B2b · meta YOL (DEC-095 katman 2): katalog (WEAPONS 9 - WP-107: Kül'süz merdiven, w_rl_1 yok / SPECIAL 10 / PASSIVES 18, 6×3 sayfa),
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
        public void Katalog_Gecerli_Sayfalar_ve_Silahlar()
        {
            TextAsset t = Resources.Load<TextAsset>(MetaPathCatalog.ResourcePath);
            Assert.IsNotNull(t);
            var doc = JsonUtility.FromJson<MetaPathCatalogDocument>(t.text);
            var hatalar = MetaPathCatalog.Validate(doc);
            Assert.IsEmpty(hatalar, string.Join("; ", hatalar));
            int w = 0, s = 0, p = 0;
            foreach (MetaPathNode n in MetaPathCatalog.Nodes)
            {
                if (n.Page == MetaPage.Weapons) w++; else if (n.Page == MetaPage.Special) s++; else p++;
            }

            // WP-107 (R4, kullanıcı 11.10 not 8): silahlar seviyeyle / Kül'le açılmaz - silah sayfası yalnız merdiven düğümleri
            // (seviye 1, Kül yok); roketatar düğümü kalktı (duvardan satın alınır).
            Assert.AreEqual(9, w); Assert.AreEqual(10, s); Assert.AreEqual(18, p);
            Assert.AreEqual(0, MetaPathCatalog.Find("w_ar_2").AshCost);
            Assert.AreEqual(1, MetaPathCatalog.Find("w_ar_2").LevelGate);
            Assert.IsNull(MetaPathCatalog.Find("w_rl_1"));
        }

        [Test]
        public void TazeProfil_Durumlar()
        {
            Assert.AreEqual(RunTreeNodeStatus.Purchased, Durum("w_ar_1", out _), "varsayılan silah açık");
            Assert.AreEqual(RunTreeNodeStatus.Purchased, Durum("w_smg_1", out _), "WP-107: baştan açık");
            Assert.AreEqual(RunTreeNodeStatus.Purchased, Durum("w_ar_2", out _), "WP-107: baştan açık (Kül yok)");
            Assert.AreEqual(RunTreeNodeStatus.Locked, Durum("pa_body_1", out MetaLockReason c));
            Assert.AreEqual(MetaLockReason.LevelGate, c, "L1 < L2");
        }

        [Test]
        public void SatinAlma_Atomik_Idempotan_SilahDugumuSatilmaz()
        {
            Kazan(20000);   // ≈ L14, Kül = 500 + başarımlar
            ProfileData p = ProfileService.Current;
            Assert.GreaterOrEqual(p.ProfileLevel, 8);
            Assert.AreEqual(RunTreeNodeStatus.Purchased, Durum("w_smg_1", out _), "WP-107: silahlar baştan açık");
            int once = p.AshBalance;
            Assert.AreEqual(MetaCommandResult.AlreadyOwned, ProfileService.TryBuyMetaNode("w_ar_2"), "WP-107: silah düğümü Kül'le satılmaz");
            Assert.AreEqual(once, ProfileService.Current.AshBalance);

            int cost = MetaPathCatalog.Find("pa_eco_1").AshCost;
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_1"));
            p = ProfileService.Current;
            Assert.AreEqual(once - cost, p.AshBalance);
            Assert.IsTrue(p.MetaNodes.Contains("pa_eco_1"));
            Assert.AreEqual(JsonUtility.ToJson(p), JsonUtility.ToJson(Oku()), "canlı = disk");
            Assert.AreEqual(MetaCommandResult.AlreadyOwned, ProfileService.TryBuyMetaNode("pa_eco_1"), "ikinci alım yok");
            Assert.AreEqual(once - cost, ProfileService.Current.AshBalance);
        }

        [Test]
        public void YetersizKul_KilitliEtki_OnKosul_HicbirSeyYazmaz()
        {
            Kazan(1500);   // L5 civarı, Kül ≈ 50 + başarımlar (DEC-098 XP/30)
            string disk = Disk();
            Assert.AreEqual(MetaCommandResult.NotAvailable, ProfileService.TryBuyMetaNode("sp_early_bell"), "seviye kapısı (L7)");
            Assert.AreEqual(MetaCommandResult.AlreadyOwned, ProfileService.TryBuyMetaNode("w_ar_2"), "WP-107: silah baştan açık");
            Assert.AreEqual(MetaCommandResult.NotAvailable, ProfileService.TryBuyMetaNode("pa_body_1"), "bağlanmamış etki satılmaz");
            Assert.AreEqual(MetaCommandResult.NotAvailable, ProfileService.TryBuyMetaNode("pa_eco_2"), "ön koşul: pa_eco_1");
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
            MetaPathRules.WeaponLockInfo ar1 = ProfileService.WeaponLock(ClassicWeaponId.AssaultRifle);
            Assert.IsTrue(ar1.Entitled && ar1.Ladder);
            MetaPathRules.WeaponLockInfo smg1 = ProfileService.WeaponLock(ClassicWeaponId.AutoSmg);
            Assert.IsTrue(smg1.Ladder, "smg_1 merdivende, Kül yok");
            Assert.AreEqual(0, smg1.AshCost);
            // WP-107 (R4): Sanren de merdivende ve baştan sahip; roketatar yalnız duvardan (seçim satırı "WALL BUY").
            MetaPathRules.WeaponLockInfo ar2 = ProfileService.WeaponLock(ClassicWeaponId.Ar2);
            Assert.IsTrue(ar2.Ladder && ar2.Entitled && !ar2.OnPath);
            Assert.AreEqual(0, ar2.AshCost);
            MetaPathRules.WeaponLockInfo rl1 = ProfileService.WeaponLock(ClassicWeaponId.Rl1);
            Assert.IsFalse(rl1.Entitled);
            Assert.AreEqual(MetaLockReason.WallBuyOnly, rl1.LockReason);
            Assert.AreEqual(RunTreeNodeStatus.Locked, rl1.Status);
        }

        [Test]
        public void Respec_SilahlarKalir_IlkTam_SonraYuzde75()
        {
            Kazan(20000);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_1"));   // 40 (DEC-098)
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_2"));   // 70
            int once = ProfileService.Current.AshBalance;

            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryMetaRespec(out int iade));
            Assert.AreEqual(110, iade, "ilk respec %100");
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(once + 110, p.AshBalance);
            Assert.IsTrue(p.UnlockedWeapons.Contains("ar_2"), "silahlar kalır (WP-107: baştan açık)");
            Assert.IsFalse(p.MetaNodes.Contains("pa_eco_1"));

            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_1"));
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryMetaRespec(out int iade2));
            Assert.AreEqual(30, iade2, "ikinci respec %75 (40 → 30)");
            Assert.AreEqual(MetaCommandResult.NothingToRefund, ProfileService.TryMetaRespec(out _));
        }

        [Test]
        public void KosuDegistiricileri_DukkanVeAgac()
        {
            Kazan(40000);   // ≈ L19
            foreach (string id in new[] { "pa_eco_1", "pa_eco_2", "pa_eco_3", "pa_eco_4" })
                Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode(id), id);

            MetaRunModifiers m = MetaPathRules.RunModifiers(ProfileService.Current, "ar_1");
            Assert.AreEqual(7, m.RerollCostOverride);
            Assert.AreEqual(27, m.HearthCostStepOverride);
            Assert.IsTrue(m.PurseAtStart);
            Assert.AreEqual(0.10f, m.CapstoneDiscount, 1e-5f);
            Assert.IsFalse(m.CapstoneMasteryMet, "K6: ar_1 ustalığı < 150 öldürme (M1)");

            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                var cuzdan = new GoldWallet();
                cuzdan.InitializeForRun(1);
                var dukkan = new RoundUpgradeShop { UnlockedPredicate = _ => true, Random = new SeededUpgradeOfferRandom(3), WeaponProvider = () => ClassicWeaponId.AssaultRifle, Meta = m };
                dukkan.Open(1, config, cuzdan, new WeaponModifierState(), 44);
                Assert.AreEqual(7, dukkan.RerollCost, "reroll 10 → 7");
                Assert.IsTrue(dukkan.Tree.IsOwned(dukkan.Tree.Graph.PurseIndex), "Kese başlangıçta");
                Assert.AreEqual(342, dukkan.Tree.Cost(dukkan.Tree.Graph.WeaponCapstoneIndex, null), "doruk 380 × 0,90");
                RunTreeEffects.TreatAllAsLive = true;
                Assert.IsFalse(dukkan.Tree.IsOfferable(dukkan.Tree.Graph.WeaponCapstoneIndex, null), "M1 öncesi doruk (pin yoksa) teklife girmez");
            }
            finally
            {
                RunTreeEffects.TreatAllAsLive = false;
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void AnlikGoruntu_S09()
        {
            Kazan(20000);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.SetMetaPin("pa_eco_1"));
            var s = new MetaPathSnapshot();
            s.Refresh();
            Assert.AreEqual(37, s.Count, "WP-107: w_rl_1 kalktı");
            Assert.AreEqual(s.IndexOf("pa_eco_1"), s.NextIndex);
            Assert.IsFalse(s.NextIsAuto);
            Assert.IsTrue(s.Nodes[s.NextIndex].IsPinned);
            Assert.Greater(s.LastRunAsh, 0);
            Assert.AreEqual(ProfileService.Current.AshBalance, s.AshBalance);
            Assert.IsTrue(s.RespecFree);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_1"));
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
