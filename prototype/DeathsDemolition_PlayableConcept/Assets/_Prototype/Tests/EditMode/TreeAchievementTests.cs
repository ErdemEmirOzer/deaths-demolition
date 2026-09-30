using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-077 B4 · başarım bağları (direction §5): sekiz yeni ağaç başarımı (koşu içi koşullar RunSummary → RunRewardInput,
    /// yaşam boyu doruk silahı / ustalık kaydı istatistikle aynı birleştirmede), ağaç etkisi canlı değilken listelenmez ve
    /// değerlendirilmez ("bağlanmamış = KİLİTLİ"), kapsam doğrulaması, erişim bağları (ultra_hunter → sg_1 doruğu ustalıksız,
    /// armor_breaker_100 → ArmorBreak keystone R1'den, special_pickup_10 → SPECIAL 1. sütun seviye kapısız). Kül = nadirlik.
    /// İzole profil yolu; gerçek profil dosyası değişmez.
    /// </summary>
    [TestFixture]
    public sealed class TreeAchievementTests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-editmode-wp077-b4", "prototype-profile.json");

        private static readonly string RealPath = Path.Combine(Application.persistentDataPath, ProfileStore.FileName);
        private string _realHashBefore;

        private static readonly string[] Yeni =
        {
            "first_capstone", "three_keystones_one_run", "hearth_10_one_run", "hearth_20", "full_lane",
            "capstones_5_weapons", "both_special_links", "mastery_5_any"
        };

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
            RunTreeEffects.TreatAllAsLive = false;
            MetaPathEffects.TreatAllAsLive = false;
            ProfileService.ResetProfile();
        }

        [TearDown]
        public void TearDown()
        {
            RunTreeEffects.TreatAllAsLive = false;
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

        private static AchievementDefinition Tanim(string id)
        {
            foreach (AchievementDefinition d in AchievementCatalog.All) if (d.Id == id) return d;
            return null;
        }

        private static bool Listede(string id)
        {
            foreach (AchievementDefinition d in AchievementCatalog.Definitions) if (d.Id == id) return true;
            return false;
        }

        /// <summary>A copy of a shipped definition gated behind an effect no system reads (the "unwired = LOCKED" proof).</summary>
        private static AchievementDefinition Kapili(string id)
        {
            AchievementDefinition d = Tanim(id);
            return new AchievementDefinition
            {
                Id = d.Id, DisplayName = d.DisplayName, Description = d.Description, Condition = d.Condition, Threshold = d.Threshold,
                TargetTier = d.TargetTier, Scope = d.Scope, Rarity = d.Rarity, RequiresEffect = "test.unwired.", Feat = d.Feat
            };
        }

        [Test]
        public void Katalog_SekizYeni_AgacKapiliOlanlarEtkiCanliOlanaDekGizli()
        {
            foreach (string id in Yeni) Assert.IsNotNull(Tanim(id), id);
            Assert.IsTrue(Listede("mastery_5_any"), "ustalık kapısız");
            Assert.IsTrue(Listede("first_blood"));

            // K10b: every shipped tree effect is live and the offer sells node cards → the eight are listed.
            if (RunTreeEffects.NodeCardsShipped)
            {
                foreach (string id in Yeni) Assert.IsTrue(Listede(id), id + " sevk edilen ağaçta listede");
            }

            // The rule itself: a tree achievement whose effect no system reads is neither listed nor evaluated.
            try
            {
                AchievementCatalog.TestSeam.Override(new List<AchievementDefinition> { Kapili("first_capstone"), Tanim("first_blood") });
                Assert.IsFalse(Listede("first_capstone"), "bağlanmamış etki → listelenmez");
                Assert.IsTrue(Listede("first_blood"));
                RunTreeEffects.TreatAllAsLive = true;
                Assert.IsTrue(Listede("first_capstone"), "canlıyken listede");
                RunTreeEffects.TreatAllAsLive = false;
                Assert.IsFalse(Listede("first_capstone"), "görünüm canlılık değişince yenilenir");
            }
            finally
            {
                RunTreeEffects.TreatAllAsLive = false;
                AchievementCatalog.TestSeam.Override(null);
            }
        }

        [Test]
        public void Kapsam_KosuSayaclariTekKosu_SilahKayitlariYasamBoyu()
        {
            string Json(string cond, string scope) =>
                "{\"Version\":1,\"Definitions\":[{\"Id\":\"x\",\"Condition\":\"" + cond + "\",\"Threshold\":1,\"Scope\":\"" + scope + "\"}]}";
            foreach (string c in new[] { "TreeCapstones", "TreeKeystones", "HearthStacks", "TreeFullLanes", "SpecialLinkII" })
            {
                Assert.AreEqual(0, AchievementCatalog.Parse(Json(c, "Lifetime"), out int d1).Count, c + " yaşam boyu olamaz");
                Assert.AreEqual(1, AchievementCatalog.Parse(Json(c, "SingleRun"), out _).Count, c);
            }

            foreach (string c in new[] { "CapstoneWeapons", "WeaponMastery" })
            {
                Assert.AreEqual(0, AchievementCatalog.Parse(Json(c, "SingleRun"), out _).Count, c + " tek koşu olamaz");
                Assert.AreEqual(1, AchievementCatalog.Parse(Json(c, "Lifetime"), out _).Count, c);
            }
        }

        [Test]
        public void KosuKosullari_OzettenOkunur()
        {
            var input = new RunRewardInput(new RunSummary
            {
                ReachedRound = 12, CompletedRounds = 11, Weapon = ClassicWeaponId.AssaultRifle,
                TreeKeystones = 3, TreeCapstones = 1, TreeHearthStacks = 20, TreeLinkII = 1, TreeWeaponCapstone = 1, TreeFullLanes = 1
            });
            var p = new ProfileData();
            RunTreeEffects.TreatAllAsLive = true;
            foreach (string id in new[] { "first_capstone", "three_keystones_one_run", "hearth_10_one_run", "hearth_20", "full_lane", "both_special_links" })
            {
                Assert.IsTrue(AchievementEngine.IsSatisfied(p, input, RunFeatCounters.None, Tanim(id)), id);
            }

            var bos = new RunRewardInput(new RunSummary { ReachedRound = 12, CompletedRounds = 11, TreeHearthStacks = 9, TreeKeystones = 2 });
            Assert.IsFalse(AchievementEngine.IsSatisfied(p, bos, RunFeatCounters.None, Tanim("hearth_10_one_run")));
            Assert.IsFalse(AchievementEngine.IsSatisfied(p, bos, RunFeatCounters.None, Tanim("three_keystones_one_run")));
        }

        [Test]
        public void YasamBoyu_BesSilahDorugu_Ustalik5_AyniKosudaVerilir()
        {
            RunTreeEffects.TreatAllAsLive = true;
            ClassicWeaponId[] silahlar = { ClassicWeaponId.AssaultRifle, ClassicWeaponId.AutoSmg, ClassicWeaponId.CombatShotgun, ClassicWeaponId.Ar2, ClassicWeaponId.Sr1 };
            RunRewardReport son = null;
            for (int i = 0; i < silahlar.Length; i++)
            {
                son = ProfileService.ApplyRun(new RunSummary
                {
                    CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(),
                    ReachedRound = 5, CompletedRounds = 4, StandardKills = i == 0 ? 3500 : 10, TotalKills = i == 0 ? 3500 : 10,
                    ClassicWeaponKills = i == 0 ? 3500 : 10,   // DEC-111: mastery reads the classic weapon's kills
                    Weapon = silahlar[i], TreeCapstones = 1, TreeWeaponCapstone = 1, EndReason = RunEndReason.PlayerDeath
                });
                Assert.IsTrue(son.Applied);
                if (i == 0) Assert.IsTrue(ProfileService.Current.Has(ProfileService.Current.Achievements, "mastery_5_any"), "M5'i geçen koşunun kendisi verir");
                Assert.AreEqual(i == silahlar.Length - 1, ProfileService.Current.Has(ProfileService.Current.Achievements, "capstones_5_weapons"), "koşu " + i);
            }

            Assert.AreEqual(5, ProfileService.Current.CapstoneWeaponCount);
            Assert.AreEqual(3500, ProfileService.Current.MasteryKills("ar_1"));
        }

        [Test]
        public void EtkiCanliDegilken_AgacBasarimiVerilmez()
        {
            // K10b: the shipped tree is fully live, so the rule is proven on gated copies of two tree achievements.
            AchievementCatalog.TestSeam.Override(new List<AchievementDefinition> { Kapili("first_capstone"), Kapili("hearth_20") });
            try
            {
                KapiliKosu();
            }
            finally
            {
                AchievementCatalog.TestSeam.Override(null);
            }
        }

        private static void KapiliKosu()
        {
            RunRewardReport r = ProfileService.ApplyRun(new RunSummary
            {
                CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(),
                ReachedRound = 5, CompletedRounds = 4, Weapon = ClassicWeaponId.AssaultRifle,
                TreeCapstones = 1, TreeWeaponCapstone = 1, TreeHearthStacks = 20, EndReason = RunEndReason.PlayerDeath
            });
            Assert.IsTrue(r.Applied);
            Assert.IsFalse(ProfileService.Current.Has(ProfileService.Current.Achievements, "first_capstone"));
            Assert.IsFalse(ProfileService.Current.Has(ProfileService.Current.Achievements, "hearth_20"));
        }

        [Test]
        public void Baglar_UltraAvcisi_ZirhSokucu_SpecialToplayici()
        {
            var p = new ProfileData();
            Assert.IsFalse(MetaPathRules.RunModifiers(p, "sg_1").CapstoneMasteryMet);
            p.Grant(p.Achievements, MetaPathRules.LinkUltraHunter);
            Assert.IsTrue(MetaPathRules.RunModifiers(p, "sg_1").CapstoneMasteryMet, "ultra_hunter → sg_1 doruğu ustalıksız");
            Assert.IsFalse(MetaPathRules.RunModifiers(p, "ar_1").CapstoneMasteryMet, "yalnız sg_1");

            Assert.IsFalse(MetaPathRules.RunModifiers(p, "ar_1").ArmorBreakKeystoneOpen);
            p.Grant(p.Achievements, MetaPathRules.LinkArmorBreaker);
            MetaRunModifiers m = MetaPathRules.RunModifiers(p, "ar_1");
            Assert.IsTrue(m.ArmorBreakKeystoneOpen);

            ClassicWeaponId? zirhli = null;
            foreach (ClassicWeaponId w in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                if (Array.IndexOf(RunTreeGraph.CoreBranchesOf(w), UpgradeBranch.ArmorBreak) >= 0) { zirhli = w; break; }
            }

            if (zirhli.HasValue)
            {
                var config = ScriptableObject.CreateInstance<PrototypeConfig>();
                try
                {
                    var t = new RunTreeState();
                    t.Begin(1, zirhli.Value, config, m);
                    int ks = t.Graph.IndexOf("ks_armorbreak");
                    Assert.IsTrue(t.PrereqMet(ks, new WeaponModifierState(), out _), "ArmorBreak keystone R1'den cephede");
                    var yok = new RunTreeState();
                    yok.Begin(1, zirhli.Value, config, MetaRunModifiers.None);
                    Assert.IsFalse(yok.PrereqMet(ks, new WeaponModifierState(), out _), "bağ yokken kademe 2 ister");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(config);
                }
            }

            int draw = MetaPathCatalog.IndexOf("sp_arc_draw");
            MetaPathEffects.TreatAllAsLive = true;
            p.ProfileLevel = 1;
            Assert.AreEqual(RunTreeNodeStatus.Locked, MetaPathRules.Status(p, draw, out MetaLockReason neden, out _, out _));
            Assert.AreEqual(MetaLockReason.LevelGate, neden);
            p.Grant(p.Achievements, MetaPathRules.LinkSpecialPickups);
            Assert.AreNotEqual(RunTreeNodeStatus.Locked, MetaPathRules.Status(p, draw, out _, out _, out _), "special_pickup_10 → SPECIAL 1. sütun seviye kapısız");
            Assert.AreEqual(RunTreeNodeStatus.Locked, MetaPathRules.Status(p, MetaPathCatalog.IndexOf("sp_arc_omen"), out _, out _, out _), "2. sütun kapısı kalır");
        }

        [Test]
        public void TamSerit_DorukVeMaksKademe()
        {
            RunTreeEffects.TreatAllAsLive = true;
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                var t = new RunTreeState();
                t.Begin(1, ClassicWeaponId.AssaultRifle, config, MetaRunModifiers.None);
                var build = new WeaponModifierState();
                Assert.AreEqual(0, t.FullLaneCount(build));
                t.MarkOwned(t.Graph.WeaponCapstoneIndex);
                Assert.AreEqual(0, t.FullLaneCount(build), "doruk var, maks kademe yok");
                UpgradeBranch b = RunTreeGraph.CoreBranchesOf(ClassicWeaponId.AssaultRifle)[0];
                int tier = t.Graph.IndexOf(RunTreeGraph.TierId(b));
                build.SetTier(b, t.Graph[tier].MaxTier, config);
                Assert.AreEqual(1, t.FullLaneCount(build));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }
    }
}
