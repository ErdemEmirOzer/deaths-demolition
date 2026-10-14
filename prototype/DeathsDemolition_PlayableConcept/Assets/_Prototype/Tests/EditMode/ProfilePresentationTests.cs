using System.Collections.Generic;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-005 · Profile / Statistics / Achievements sunum modelleri (saf; config gerekmez).
    /// Profil seviyesi güç gibi sunulmaz; 0 ile "unknown" karışmaz; görülmemiş tier vurgusuz;
    /// achievement yoksa empty state; unlock satırları prototip merdiveninden.
    /// </summary>
    [TestFixture]
    [Category("WP005")]
    public sealed class ProfilePresentationTests
    {
        /// <summary>Sahte lookup: 100 + 75(L-1) eğrisi ve istek merdiveni (SMG L2, Shotgun L3, Explosion L2, Chain L3).</summary>
        private sealed class SahteLookup : IProfileLookup
        {
            public List<AchievementSettings> Tanimlar = new List<AchievementSettings>();
            public bool Bozuk;

            public int Tavan;   // R1-B P2: > 0 → the cap level, whose requirement is int.MaxValue (ProfileService's rule)
            public int LevelRequirement(int level) => Tavan > 0 && level >= Tavan ? int.MaxValue : 100 + 75 * (Mathf.Max(1, level) - 1);
            public string WeaponDisplayName(ClassicWeaponId id) => "W-" + id;
            // WP-069 B2: the v3 weapons (3..9) are opened by the DEC-095 meta tree, never by the profile ladder.
            public int UnlockLevelFor(ClassicWeaponId id) => id == ClassicWeaponId.AutoSmg ? 2 : id == ClassicWeaponId.CombatShotgun ? 3
                : id == ClassicWeaponId.AssaultRifle ? 1 : ClassicWeaponSettings.NotOnProfileLadder;
            public string BranchDisplayName(UpgradeBranch branch) => "B-" + branch;
            public int UnlockLevelFor(UpgradeBranch branch) => branch == UpgradeBranch.Explosion ? 2 : branch == UpgradeBranch.Chain ? 3 : 1;
            public IReadOnlyList<AchievementSettings> Achievements => Tanimlar;
            public bool RecoveredFromCorruptSave => Bozuk;
            public string Bildirim = string.Empty;
            public string PersistenceNotice => Bildirim;
        }

        private static AchievementSettings Tanim(string id, string ad)
        {
            return new AchievementSettings { Id = id, DisplayName = ad, Description = ad + " desc" };
        }

        private static ProfileData Profil()
        {
            var p = new ProfileData
            {
                ProfileLevel = 2, XpIntoLevel = 40, TotalXpEarned = 140, HighestRound = 4, RunsPlayed = 3,
                RoundsCompleted = 6, TotalKills = 31, StandardKills = 31, ArmorPiecesBroken = 2, BestRunXp = 80
            };
            p.Achievements.Add("first_blood");

            // WP-009: unlock satırları KAZANILMIŞ entitlement listesini okur (seviye karşılaştırması değil);
            // L2 profilin merdivenden kazanmış olacağı içerik listede.
            p.UnlockedWeapons.Add("AssaultRifle");
            p.UnlockedWeapons.Add("AutoSmg");
            p.UnlockedBranches.Add("Explosion");
            return p;
        }

        [Test]
        public void Agac_OnceSonra_BirimleriVeIsaretiYazar()
        {
            // 12b U11: display units as S-04 sends them - percentages signed, seconds / HP after the number, counts bare.
            Assert.AreEqual("+20 %", UiRunTreePanel.StatText(20f, "%"));
            Assert.AreEqual("−10 %", UiRunTreePanel.StatText(-10f, "%"));
            Assert.AreEqual("4.5 s", UiRunTreePanel.StatText(4.5f, "s"));
            Assert.AreEqual("2.5 HP/s", UiRunTreePanel.StatText(2.5f, "HP/s"));
            Assert.AreEqual("3", UiRunTreePanel.StatText(3f, string.Empty));
        }

        /// <summary>
        /// AD 12c MUST-2: a compact tree row's word. AVAILABLE yields first and alone - never the chip word (OFFER / NEW), k/n
        /// or the price ("NEW · 50", "2/4 · 120"); still too wide, it breaks only after a separator, the phrase whole on its
        /// line ("OFFER ·" / "40 GOLD SHORT"). Measured here at 10 px a character.
        /// </summary>
        [Test]
        public void AgacKompaktSatir_UygunKelimeOnceGider_IfadeBolunmez()
        {
            System.Func<string, float> measure = t => t.Length * 10f;
            Assert.AreEqual("NEW · AVAILABLE · 50", UiRunTreePanel.CompactLine(new List<string> { "NEW", "AVAILABLE", "50" }, 1, measure, 400f), "sığıyorsa tamamı");
            Assert.AreEqual("NEW · 50", UiRunTreePanel.CompactLine(new List<string> { "NEW", "AVAILABLE", "50" }, 1, measure, 120f), "Hearth: NEW kalır, AVAILABLE gider");
            Assert.AreEqual("2/4 · 120", UiRunTreePanel.CompactLine(new List<string> { "2/4", "AVAILABLE", "120" }, 1, measure, 120f), "12b MUST-3");
            Assert.AreEqual("OFFER ·\n40 GOLD SHORT", UiRunTreePanel.CompactLine(new List<string> { "OFFER", "40 GOLD SHORT" }, -1, measure, 140f), "kırılma yalnız ayraçtan sonra");
            Assert.AreEqual("NEW · 2/4 ·\n80 GOLD SHORT", UiRunTreePanel.CompactLine(new List<string> { "NEW", "2/4", "80 GOLD SHORT" }, -1, measure, 140f), "son ifade tek başına ikinci satırda");
            Assert.AreEqual("OFFER · 15", UiRunTreePanel.CompactLine(new List<string> { "OFFER", "15" }, -1, measure, 100f));
        }

        [Test]
        public void Agac_Aciklama_PaketSatiri_ArgumanlarlaDolar_AcikKalirsaKatalog()
        {
            // K10b: the pack line filled from the node's args; a placeholder left open falls back to the catalog line.
            Assert.AreEqual("Wait 2.5 s before the shot for ×2 damage.",
                UiRunTreePanel.FillArgs("Wait {seconds} s before the shot for ×{value} damage.", new[] { "seconds", "value" }, new[] { 2.5f, 2f }));
            var n = new DD.Prototype.Rounds.RunTreeNodeView
            {
                StringKey = "tree.run.fork.sr_1.x",
                ArgNames = new[] { "seconds", "value" },
                ArgValues = new[] { 2.5f, 2f },
                Description = "catalog line",
            };
            StringAssert.Contains("2.5", UiRunTreePanel.DescriptionOf(n), "the pack line, filled");
            StringAssert.DoesNotContain("{", UiRunTreePanel.DescriptionOf(n));
            n.ArgNames = new string[0];
            n.ArgValues = new float[0];
            Assert.AreEqual("catalog line", UiRunTreePanel.DescriptionOf(n), "an open placeholder falls back to the catalog line");
        }

        [Test]
        public void Results_OldurenVurus_KatmanAdi_HamKimlikDegil()
        {
            // 12b: the Results H1 names the attacker's tier, never EnemyAttackState's raw source id.
            Assert.AreEqual("STANDARD ZOMBIE", ResultsViewModel.KillerName("Zombie.Standard.Overhead"));
            Assert.AreEqual("ULTRA ZOMBIE", ResultsViewModel.KillerName("Zombie.Ultra.BiteGrab"));
            Assert.AreEqual("ELITE ZOMBIE", ResultsViewModel.KillerName("Zombie.Elite"));
            Assert.AreEqual("UI-CAPTURE", ResultsViewModel.KillerName("ui-capture"), "an unknown id prints as it came");
            UiStrings.Language = UiStrings.Turkish;
            try
            {
                Assert.AreEqual("UI-CAPTURE", ResultsViewModel.KillerName("ui-capture"), "an id keeps invariant casing in Turkish (no İ)");
            }
            finally
            {
                UiStrings.Language = UiStrings.English;
            }
            Assert.AreEqual(string.Empty, ResultsViewModel.KillerName(null));
        }

        [Test]
        public void Results_SiradakiAcilis_EnDusukUstSeviye_TekilCogul_YoksaBos()
        {
            // 12b (AD R1-D SHOULD-1): the lowest weapon / branch unlock above the level the run ended on; the v3 weapons that
            // sit off the profile ladder never count; nothing left = empty.
            var lookup = new SahteLookup();
            Assert.AreEqual("Next unlock: W-AutoSmg at level 2 · 1 level away", ResultsViewModel.NextUnlockFor(1, lookup));
            StringAssert.Contains("at level 3 · 1 level away", ResultsViewModel.NextUnlockFor(2, lookup));
            Assert.AreEqual(string.Empty, ResultsViewModel.NextUnlockFor(3, lookup), "every unlock owned: no line");
            Assert.AreEqual(string.Empty, ResultsViewModel.NextUnlockFor(1, null));
        }

        [Test]
        public void Profil_SeviyeIlerlemeVeBasliklar()
        {
            var lookup = new SahteLookup();
            lookup.Tanimlar.Add(Tanim("first_blood", "First Blood"));
            lookup.Tanimlar.Add(Tanim("reach_round_5", "Reach Round 5"));

            ProfileViewModel vm = ProfileViewModel.From(Profil(), lookup);

            Assert.AreEqual("LEVEL 2", vm.LevelText);
            Assert.AreEqual(40f / 175f, vm.Progress01, 0.0001f);
            Assert.AreEqual("40 / 175 XP towards level 3", vm.ProgressCaption);

            Assert.AreEqual("Highest Round", vm.Headline[0].Label);
            Assert.AreEqual("4", vm.Headline[0].Value);
            Assert.AreEqual("Rounds Completed", vm.Headline[1].Label);
            Assert.AreEqual("6", vm.Headline[1].Value);
            Assert.AreEqual("Total Kills", vm.Headline[2].Label);
            Assert.AreEqual("31", vm.Headline[2].Value);

            Assert.AreEqual("1 of 2 achievements earned", vm.AchievementSummary);
            Assert.AreEqual(string.Empty, vm.Notice);
        }

        [Test]
        public void Profil_SeviyeTavani_MaxOkunur_CubukDolu_R1B_P2()
        {
            // R1-B P2 (S-04): at the cap the requirement is int.MaxValue; the caption never prints it.
            try
            {
                foreach (string language in new[] { UiStrings.English, UiStrings.Turkish })
                {
                    UiStrings.Language = language;
                    ProfileViewModel vm = ProfileViewModel.From(Profil(), new SahteLookup { Tavan = 2 });
                    Assert.AreEqual(UiStrings.Get("profile.progress.max"), vm.ProgressCaption, language);
                    Assert.IsFalse(vm.ProgressCaption.Contains(int.MaxValue.ToString()), language);
                    Assert.AreEqual(1f, vm.Progress01, language);
                }
            }
            finally
            {
                UiStrings.Language = UiStrings.English;
            }

            Assert.AreEqual("40 / 175 XP towards level 3", ProfileViewModel.From(Profil(), new SahteLookup()).ProgressCaption, "below the cap: unchanged");
        }

        [Test]
        public void Profil_UnlockSatirlari_MerdivendenVeSeviyeden()
        {
            ProfileViewModel vm = ProfileViewModel.From(Profil(), new SahteLookup());

            // 3 silah + 2 kilitli dal = 5 satır; L2: AssaultRifle ve AutoSmg açık, Shotgun L3 kilitli.
            Assert.AreEqual(5, vm.Unlocks.Count);
            Assert.IsTrue(vm.Unlocks[0].Unlocked, "AssaultRifle L1");
            Assert.IsTrue(vm.Unlocks[1].Unlocked, "AutoSmg L2");
            Assert.IsFalse(vm.Unlocks[2].Unlocked, "CombatShotgun L3 kilitli");
            Assert.AreEqual(3, vm.Unlocks[2].RequiredLevel);
            Assert.AreEqual("W-CombatShotgun", vm.Unlocks[2].Title);
            Assert.IsTrue(vm.Unlocks[3].Unlocked, "Explosion L2");
            Assert.IsFalse(vm.Unlocks[4].Unlocked, "Chain L3");
            StringAssert.EndsWith(" branch", vm.Unlocks[4].Title);
        }

        [Test]
        public void Profil_UnlockSatirlari_EntitlementListesiOtoritedir_SeviyeDegil()
        {
            // WP-009: seviye yükselmiş ama liste kazanımı içermiyorsa satır KİLİTLİ; liste içeriyorsa
            // seviye eşiğin altına düşse (config yükselse) bile AÇIK kalır.
            ProfileData p = Profil();
            p.ProfileLevel = 9;
            ProfileViewModel vm = ProfileViewModel.From(p, new SahteLookup());
            Assert.IsFalse(vm.Unlocks[2].Unlocked, "CombatShotgun listede yok → seviye 9 olsa da kilitli görünür");
            Assert.IsFalse(vm.Unlocks[4].Unlocked, "Chain listede yok → kilitli");

            p.ProfileLevel = 1;
            p.UnlockedWeapons.Add("CombatShotgun");
            p.UnlockedBranches.Add("Chain");
            vm = ProfileViewModel.From(p, new SahteLookup());
            Assert.IsTrue(vm.Unlocks[1].Unlocked, "AutoSmg listede → L1'de bile açık (kazanılmış entitlement geri alınmaz)");
            Assert.IsTrue(vm.Unlocks[2].Unlocked, "CombatShotgun listede → açık");
            Assert.IsTrue(vm.Unlocks[4].Unlocked, "Chain listede → açık");
            Assert.AreEqual(3, vm.Unlocks[2].RequiredLevel, "merdiven değeri bilgi olarak kalır");
        }

        [Test]
        public void Profil_KalicilikBildirimi_LookupTanOkunur()
        {
            var lookup = new SahteLookup { Bildirim = "Another copy of the game is already using this profile." };
            ProfileViewModel vm = ProfileViewModel.From(Profil(), lookup);
            Assert.AreEqual(lookup.Bildirim, vm.Notice);

            lookup = new SahteLookup { Bozuk = true };
            vm = ProfileViewModel.From(Profil(), lookup);
            StringAssert.Contains("could not be read", vm.Notice, "bildirim yokken bozuk-kayıt metni korunur");
        }

        [Test]
        public void Istatistik_AtanmamisKill_YalnizSifirdanBuyukseSatir()
        {
            ProfileData p = Profil();
            StatisticsViewModel vm = StatisticsViewModel.From(p);
            for (int i = 0; i < vm.Kills.Count; i++)
            {
                Assert.AreNotEqual("Recorded Before Tier Tracking", vm.Kills[i].Label, "0 iken satır yok");
            }

            p.TotalKills = 40; // legacy: toplam > tier toplamı → UnattributedKills 9
            p.Sanitize();
            Assert.AreEqual(9, p.UnattributedKills);
            Assert.AreEqual(40, p.TotalKills);
            vm = StatisticsViewModel.From(p);
            bool bulundu = false;
            for (int i = 0; i < vm.Kills.Count; i++)
            {
                if (vm.Kills[i].Label == "Recorded Before Tier Tracking")
                {
                    bulundu = true;
                    Assert.AreEqual("9", vm.Kills[i].Value);
                }
            }

            Assert.IsTrue(bulundu, "atanmamış kill satırı gösterilir, gizlenmez");
        }

        [Test]
        public void Profil_GucGostergesiTasimaz()
        {
            ProfileViewModel vm = ProfileViewModel.From(Profil(), new SahteLookup());
            string[] yasak = { "damage", "health bonus", "starting gold", "boost" };

            var metinler = new List<string> { vm.LevelText, vm.ProgressCaption, vm.AchievementSummary, ProfileViewModel.RuleNote };
            for (int i = 0; i < vm.Headline.Count; i++)
            {
                metinler.Add(vm.Headline[i].Label);
            }

            for (int i = 0; i < metinler.Count; i++)
            {
                string m = metinler[i].ToLowerInvariant();
                for (int j = 0; j < yasak.Length; j++)
                {
                    // Kural cümlesi "never grants starting damage, health or gold" der: yasağı söylemek serbest.
                    if (metinler[i] == ProfileViewModel.RuleNote)
                    {
                        continue;
                    }

                    Assert.IsFalse(m.Contains(yasak[j]), "'" + metinler[i] + "' güç göstergesi içeriyor: " + yasak[j]);
                }
            }

            StringAssert.Contains("never grants starting damage, health or gold", ProfileViewModel.RuleNote);
        }

        [Test]
        public void Profil_TazeProfil_TireVeSifir()
        {
            ProfileViewModel vm = ProfileViewModel.From(new ProfileData(), new SahteLookup());
            Assert.AreEqual("—", vm.Headline[0].Value, "hiç koşu yok → en yüksek round bilinmiyor");
            Assert.AreEqual("0", vm.Headline[2].Value, "kill sayısı gerçek sıfır");
            Assert.AreEqual("No achievements are available yet.", vm.AchievementSummary);   // WP-112 (f3123ab9): no "in this build" on screen
        }

        [Test]
        public void Profil_BozukKayitBildirimi()
        {
            ProfileViewModel vm = ProfileViewModel.From(new ProfileData(), new SahteLookup { Bozuk = true });
            StringAssert.Contains("could not be read", vm.Notice);
        }

        [Test]
        public void Istatistik_GruplarVeGorulmemisTierVurgusuz()
        {
            ProfileData p = Profil();
            p.EliteKills = 1;
            p.TotalKills = 32;

            StatisticsViewModel vm = StatisticsViewModel.From(p);

            Assert.AreEqual(string.Empty, vm.EmptyNotice);
            Assert.AreEqual("Runs Played", vm.Runs[0].Label);
            Assert.AreEqual("3", vm.Runs[0].Value);
            Assert.AreEqual("Highest Round", vm.Runs[1].Label);
            Assert.AreEqual("Rounds Completed", vm.Runs[2].Label);

            Assert.AreEqual("Total Kills", vm.Kills[0].Label);
            Assert.AreEqual("32", vm.Kills[0].Value);
            Assert.AreEqual("Standard", vm.Kills[1].Label);
            Assert.AreEqual("31", vm.Kills[1].Value);

            Assert.AreEqual("Special", vm.Kills[2].Label);
            Assert.AreEqual("0", vm.Kills[2].Value, "kayıtlı sıfır: sıfır yazılır, 'unknown' değil");
            Assert.AreEqual(UiColor.TextMuted, vm.Kills[2].Color, "görülmemiş tier vurgusuz");

            Assert.AreEqual("Elite", vm.Kills[3].Label);
            Assert.AreEqual("1", vm.Kills[3].Value);
            Assert.AreNotEqual(UiColor.TextMuted, vm.Kills[3].Color, "görülen tier normal/vurgulu");

            Assert.AreEqual("Ultra", vm.Kills[4].Label);
            Assert.AreEqual(UiColor.TextMuted, vm.Kills[4].Color);

            Assert.AreEqual("Armour Pieces Broken", vm.Kills[5].Label);
            Assert.AreEqual("2", vm.Kills[5].Value);

            Assert.IsNotEmpty(vm.TierCaption);
            Assert.AreEqual("Profile Level", vm.Progress[0].Label);
            Assert.AreEqual("Lifetime XP", vm.Progress[1].Label);
            Assert.AreEqual("140", vm.Progress[1].Value);
            Assert.AreEqual("Best Single Run XP", vm.Progress[2].Label);
        }

        [Test]
        public void Istatistik_KosuYok_EmptyNotice()
        {
            StatisticsViewModel vm = StatisticsViewModel.From(new ProfileData());
            Assert.IsNotEmpty(vm.EmptyNotice);
            Assert.AreEqual("0", vm.Runs[0].Value);
            Assert.AreEqual(6, vm.Kills.Count, "şema satırları yine mevcut");
        }

        [Test]
        public void Basarimlar_ListeVeIlerleme()
        {
            var tanimlar = new List<AchievementSettings>
            {
                Tanim("first_blood", "First Blood"), Tanim("armor_breaker", "Armor Breaker"), null,
                new AchievementSettings { Id = string.Empty, DisplayName = "Kimliksiz" }
            };

            AchievementsViewModel vm = AchievementsViewModel.From(Profil(), tanimlar);
            Assert.AreEqual(2, vm.Defined, "null ve kimliksiz tanım atlanır");
            Assert.AreEqual(1, vm.Earned);
            Assert.AreEqual("1 of 2 earned", vm.Progress);
            Assert.IsFalse(vm.IsEmpty);
            Assert.IsTrue(vm.Lines[0].Earned);
            Assert.AreEqual("First Blood", vm.Lines[0].Title);
            Assert.IsFalse(vm.Lines[1].Earned);
        }

        [Test]
        public void Basarimlar_TanimYok_EmptyState_HataYok()
        {
            AchievementsViewModel vm = AchievementsViewModel.From(Profil(), new List<AchievementSettings>());
            Assert.IsTrue(vm.IsEmpty);
            Assert.AreEqual(0, vm.Lines.Count);
            Assert.AreEqual(string.Empty, vm.Progress);
            Assert.IsNotEmpty(AchievementsViewModel.EmptyTitle);
            Assert.IsNotEmpty(AchievementsViewModel.EmptyBody);

            vm = AchievementsViewModel.From(null, null);
            Assert.IsTrue(vm.IsEmpty, "null girdiler de güvenli");
            StringAssert.Contains("by themselves", AchievementsViewModel.AutoNote);   // R4 WP-105 (ux-writer): no "claim" wording at all
        }

        // ------------------------------------------------------------ WP-022D (WP-035)

        private sealed class SahteIlerleme : IAchievementProgressSource
        {
            public List<AchievementProgressEntry> Girdiler = new List<AchievementProgressEntry>();
            public int GorulduCagri;
            public IReadOnlyList<AchievementProgressEntry> Entries => Girdiler;
            public int EarnedCount { get { int n = 0; foreach (var e in Girdiler) if (e.Earned) n++; return n; } }
            public int TotalCount => Girdiler.Count;
            public int NewCount { get { int n = 0; foreach (var e in Girdiler) if (e.IsNew) n++; return n; } }
            public string LastEarnedId => string.Empty;
            public void MarkAchievementsSeen() { GorulduCagri++; }
        }

        private sealed class SahteKozmetik : ICosmeticsSource
        {
            public List<CosmeticEntry> Unvanlar = new List<CosmeticEntry>();
            public List<CosmeticEntry> Rozetler = new List<CosmeticEntry>();
            public IReadOnlyList<CosmeticEntry> Titles => Unvanlar;
            public IReadOnlyList<CosmeticEntry> Badges => Rozetler;
            public IReadOnlyList<CosmeticEntry> Skins => new List<CosmeticEntry>();
            public string SelectedTitle = "veteran";
            public string SelectedBadge = string.Empty;
            string ICosmeticsSource.SelectedTitle => SelectedTitle;
            string ICosmeticsSource.SelectedBadge => SelectedBadge;
            public int NewCount => 1;
            public bool SelectTitle(string id) { SelectedTitle = id; return true; }
            public bool SelectBadge(string id) { SelectedBadge = id; return true; }
            public void MarkContentSeen() { }
        }

        /// <summary>WP-022D: pane model from the WP-035 progress source - counters, rarity, "new", caption.</summary>
        [Test]
        public void BasarimlarModeli_IlerlemeKaynagindan_SayacNadirlikYeni()
        {
            var kaynak = new SahteIlerleme();
            kaynak.Girdiler.Add(new AchievementProgressEntry("first_blood", "First Blood", "Kill one.", AchievementRarity.Common, AchievementScope.Lifetime, true, true, 1, 1));
            kaynak.Girdiler.Add(new AchievementProgressEntry("rounds_50", "Regular", "Finish 50 rounds.", AchievementRarity.Rare, AchievementScope.Lifetime, false, false, 7, 50));
            kaynak.Girdiler.Add(new AchievementProgressEntry("ultra", "Ultra Slayer", "Defeat an Ultra.", AchievementRarity.Epic, AchievementScope.SingleRun, false, false, 0, 1));

            AchievementsViewModel vm = AchievementsViewModel.From(kaynak);
            Assert.AreEqual(3, vm.Defined);
            Assert.AreEqual(1, vm.Earned);
            Assert.AreEqual(1, vm.NewCount);
            StringAssert.Contains("1 of 3 earned", vm.Progress);
            StringAssert.Contains("1 new", vm.Progress);
            Assert.IsTrue(vm.Lines[0].IsNew && vm.Lines[0].Earned);
            Assert.AreEqual(string.Empty, vm.Lines[0].ProgressCaption, "kazanılmışta sayaç yok");
            Assert.AreEqual(AchievementRarity.Rare, vm.Lines[1].Rarity);
            Assert.AreEqual("7 / 50", vm.Lines[1].ProgressCaption);
            Assert.AreEqual(0.14f, vm.Lines[1].Progress01, 0.001f);
            Assert.AreEqual(0f, vm.Lines[2].Progress01, 0.001f);
            Assert.AreEqual(0, kaynak.GorulduCagri, "model kurmak 'görüldü' yazmaz; pane açılışı yazar");
            Assert.IsTrue(AchievementsViewModel.From((IAchievementProgressSource)null).IsEmpty, "kaynak yokken boş durum");
        }

        /// <summary>WP-022D: title / badge picker model mirrors the cosmetics source, never recomputes.</summary>
        [Test]
        public void KozmetikModeli_UnvanRozetSecimi_KaynaktanBirebir()
        {
            var kaynak = new SahteKozmetik();
            kaynak.Unvanlar.Add(new CosmeticEntry("veteran", CosmeticKind.Title, "Veteran", "Finish 10 runs", true, false, true));
            kaynak.Unvanlar.Add(new CosmeticEntry("blooded", CosmeticKind.Title, "Blooded", "Kill 100", false, false, false));
            kaynak.Rozetler.Add(new CosmeticEntry("sakura", CosmeticKind.Badge, "Sakura", "Reach round 20", true, true, false));

            CosmeticsViewModel vm = CosmeticsViewModel.From(kaynak);
            Assert.AreEqual(2, vm.Titles.Count);
            Assert.AreEqual(1, vm.Badges.Count);
            Assert.AreEqual("veteran", vm.SelectedTitle);
            Assert.IsTrue(vm.Titles[0].Selected && vm.Titles[0].Unlocked);
            Assert.IsFalse(vm.Titles[1].Unlocked);
            Assert.IsTrue(vm.Badges[0].IsNew);
            Assert.AreEqual(1, vm.NewCount);
            Assert.IsTrue(CosmeticsViewModel.From(null).IsEmpty);

            // WP-022E name plate: selected + unlocked names without the data prefix; badge alone when no title.
            Assert.AreEqual("Veteran", CosmeticsViewModel.NamePlate(kaynak));
            kaynak.Rozetler[0] = new CosmeticEntry("sakura", CosmeticKind.Badge, "Badge: Sakura", "Reach round 20", true, true, true);
            Assert.AreEqual("Veteran  ·  Sakura", CosmeticsViewModel.NamePlate(kaynak));
            kaynak.Unvanlar[0] = new CosmeticEntry("veteran", CosmeticKind.Title, "Veteran", "Finish 10 runs", false, false, true);
            Assert.AreEqual("Sakura", CosmeticsViewModel.NamePlate(kaynak), "kilitli seçim plakaya çıkmaz");
            Assert.AreEqual(string.Empty, CosmeticsViewModel.NamePlate(null));
        }
    }
}
