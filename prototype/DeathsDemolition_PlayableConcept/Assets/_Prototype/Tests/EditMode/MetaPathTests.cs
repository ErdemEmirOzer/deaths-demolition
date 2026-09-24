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
    /// WP-077 B2b · meta YOL (DEC-095 katman 2): katalog (WEAPONS 10 / SPECIAL 10 / PASSIVES 18, 6×3 sayfa),
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

            Assert.AreEqual(10, w); Assert.AreEqual(10, s); Assert.AreEqual(18, p);
            Assert.AreEqual(120, MetaPathCatalog.Find("w_ar_2").AshCost);
            Assert.AreEqual(30, MetaPathCatalog.Find("w_rl_1").LevelGate);
        }

        [Test]
        public void TazeProfil_Durumlar()
        {
            Assert.AreEqual(RunTreeNodeStatus.Purchased, Durum("w_ar_1", out _), "varsayılan silah açık");
            Assert.AreEqual(RunTreeNodeStatus.Locked, Durum("w_smg_1", out MetaLockReason a));
            Assert.AreEqual(MetaLockReason.LadderWeapon, a, "smg_1 seviye merdiveninde (DEC-085)");
            Assert.AreEqual(RunTreeNodeStatus.Locked, Durum("w_ar_2", out MetaLockReason b));
            Assert.AreEqual(MetaLockReason.LevelGate, b);
            Assert.AreEqual(RunTreeNodeStatus.Locked, Durum("pa_body_1", out MetaLockReason c));
            Assert.AreEqual(MetaLockReason.LevelGate, c, "L1 < L2");
        }

        [Test]
        public void SilahSatinAlma_Atomik_Idempotan_SilahAcar()
        {
            Kazan(20000);   // ≈ L14, Kül = 500 + başarımlar
            ProfileData p = ProfileService.Current;
            Assert.GreaterOrEqual(p.ProfileLevel, 8);
            Assert.AreEqual(RunTreeNodeStatus.Purchased, Durum("w_smg_1", out _), "L3 merdiveni smg_1'i verdi");
            int once = p.AshBalance;

            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("w_ar_2"));
            p = ProfileService.Current;
            Assert.AreEqual(once - 120, p.AshBalance);
            Assert.IsTrue(p.UnlockedWeapons.Contains("ar_2"), "silah düğümü silahı açar");
            Assert.IsTrue(p.MetaNodes.Contains("w_ar_2"));
            Assert.AreEqual(JsonUtility.ToJson(p), JsonUtility.ToJson(Oku()), "canlı = disk");
            Assert.AreEqual(MetaCommandResult.AlreadyOwned, ProfileService.TryBuyMetaNode("w_ar_2"), "ikinci alım yok");
            Assert.AreEqual(once - 120, ProfileService.Current.AshBalance);
        }

        [Test]
        public void YetersizKul_KilitliEtki_OnKosul_HicbirSeyYazmaz()
        {
            Kazan(1500);   // L5 civarı, Kül ≈ 50 + başarımlar (DEC-098 XP/30)
            string disk = Disk();
            Assert.AreEqual(MetaCommandResult.NotAvailable, ProfileService.TryBuyMetaNode("w_ar_2"), "seviye kapısı");
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
        public void Respec_SilahlarKalir_IlkTam_SonraYuzde75()
        {
            Kazan(20000);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("w_ar_2"));
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_1"));   // 40 (DEC-098)
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("pa_eco_2"));   // 70
            int once = ProfileService.Current.AshBalance;

            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryMetaRespec(out int iade));
            Assert.AreEqual(110, iade, "ilk respec %100");
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(once + 110, p.AshBalance);
            Assert.IsTrue(p.UnlockedWeapons.Contains("ar_2") && p.MetaNodes.Contains("w_ar_2"), "silah iade edilmez");
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
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.SetMetaPin("w_lmg_1"));
            var s = new MetaPathSnapshot();
            s.Refresh();
            Assert.AreEqual(38, s.Count);
            Assert.AreEqual(s.IndexOf("w_lmg_1"), s.NextIndex);
            Assert.IsFalse(s.NextIsAuto);
            Assert.IsTrue(s.Nodes[s.NextIndex].IsPinned);
            Assert.Greater(s.LastRunAsh, 0);
            Assert.AreEqual(ProfileService.Current.AshBalance, s.AshBalance);
            Assert.IsTrue(s.RespecFree);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("w_lmg_1"));
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
