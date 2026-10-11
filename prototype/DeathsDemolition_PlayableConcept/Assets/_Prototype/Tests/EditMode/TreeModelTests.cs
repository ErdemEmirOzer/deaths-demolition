using System;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Rounds;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-077 B3 · EconomySimulator.TreeModel (direction §4.2 / §4.3, DEC-098): the shipped tree on the WP-041
    /// round model with four bots (spend-max / rush / human / hoarder) at 90 % collection, seeds 1–20 × 4
    /// weapons, and the Kül tempo of the §3.4 rings. DD_TREE_OUT=&lt;dir&gt; writes the report.
    /// </summary>
    [Category("WP041")]
    public sealed class TreeModelTests
    {
        private static EconomySimulator.TreeModel.Result Kos(bool ustalik, string etiket = "shipped", int ocakTavan = 0, int gecKart = 0)
        {
            Assert.IsNotNull(PrototypeConfig.Active);
            MetaRunModifiers meta = MetaRunModifiers.None;
            meta.CapstoneMasteryMet = ustalik;
            return EconomySimulator.TreeModel.Run(new EconomySimulator.TreeModel.Options
            {
                config = PrototypeConfig.Active, meta = meta, label = etiket, hearthMaxStacks = ocakTavan, lateCardLimit = gecKart
            });
        }

        [Test]
        public void Rapor_Yazilir()
        {
            string dir = Environment.GetEnvironmentVariable("DD_TREE_OUT");
            EconomySimulator.TreeModel.AshTempo tempo = EconomySimulator.TreeModel.Tempo();
            EconomySimulator.TreeModel.Result m0 = Kos(false, "new weapon, before M1"), m1 = Kos(true, "M1 met");
            string md = EconomySimulator.TreeModel.ToMarkdown(m0, tempo) + "\n" + EconomySimulator.TreeModel.ToMarkdown(m1, null)
                        + "\n" + EconomySimulator.TreeModel.ToMarkdown(Kos(true, "what-if Hearth cap 30", 30), null)
                        + "\n" + EconomySimulator.TreeModel.ToMarkdown(Kos(true, "what-if 3 cards from R10", 0, 3), null)
                        + "\n" + EconomySimulator.TreeModel.ToMarkdown(Kos(true, "what-if both", 30, 3), null);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "tree-model.md"), md);
                File.WriteAllText(Path.Combine(dir, "tree-model-runs.csv"), EconomySimulator.TreeModel.ToCsv(m1));
            }

            StringAssert.Contains("SpendMax", md);
        }

        private static EconomySimulator.TreeModel.BotSummary Bot(EconomySimulator.TreeModel.Result r, EconomySimulator.TreeModel.Bot b) => r.bots[(int)b];

        [Test]
        public void Determinizm_AyniSecenekAyniKosular()
        {
            Assert.AreEqual(EconomySimulator.TreeModel.ToCsv(Kos(false)), EconomySimulator.TreeModel.ToCsv(Kos(false)));
        }

        [Test]
        public void PinliBot_KeystoneR4_6_DorukR9_11_M1OncesiDePinGetirir()
        {
            // DEC-098 K6: the pin brings the capstone before M1; §4.3 keystone / capstone bands read on the pinned (rush) bot.
            // S1 (user 30.09, DEC-098 note): bands at 100 % collection — the first keystone band is R3–6.
            EconomySimulator.TreeModel.BotSummary rush = Bot(Kos(false), EconomySimulator.TreeModel.Bot.Rush);
            Assert.That(rush.keystoneMedian, Is.InRange(3, 6), "ilk keystone medyanı");
            Assert.That(rush.capstoneMedian, Is.InRange(9, 11), "doruk medyanı");
            Assert.LessOrEqual(rush.capstoneP90, 13, "doruk p90 ≤ R13");
            Assert.AreEqual(1f, rush.capstoneByR15, "pin her koşuda doruğa ulaşır");
        }

        [Test]
        public void HarcamaBotu_UstSinir_HavuzBitmez()
        {
            EconomySimulator.TreeModel.Result r = Kos(true);
            EconomySimulator.TreeModel.BotSummary max = Bot(r, EconomySimulator.TreeModel.Bot.SpendMax);
            Assert.LessOrEqual(max.shareMedian, 0.99f, "bot üst sınırı ≤ %99");
            foreach (EconomySimulator.TreeModel.BotSummary b in r.bots) Assert.AreEqual(0, b.poolExhausted, b.bot + ": R20 öncesi havuz bitmez");
        }

        [Test]
        public void Insan_Pay_ve_CuzdanMedyani()
        {
            // §4.3 on the human bot (30 % cushion, no pin): shares not red, R10–15 ≥ 60 %, R15 wallet median ≤ 900.
            EconomySimulator.TreeModel.BotSummary h = Bot(Kos(true), EconomySimulator.TreeModel.Bot.Human);
            Assert.GreaterOrEqual(h.shareMedian, 0.55f, "R1–R15 pay kırmızı değil");
            Assert.GreaterOrEqual(h.shareLateMedian, 0.60f, "R10–R15 pay");
            Assert.LessOrEqual(h.walletMedian, 900, "R15 cüzdan medyanı");
        }

        [Test]
        public void Biriktirici_Cuzdan_YalnizRapor()
        {
            // DEC-098 (C-00, 2026-09-25): the hoarder (50 % cushion, ≤ 1 Hearth per round) keeps ≈ 3 400 gold at R15 —
            // hoarding is the player's own choice, no gold → Kül exchange. Report-only: printed, never a gate.
            EconomySimulator.TreeModel.BotSummary h = Bot(Kos(true), EconomySimulator.TreeModel.Bot.Hoarder);
            TestContext.WriteLine("hoarder R15 wallet median " + h.walletMedian + " / p90 " + h.walletP90 + ", share " + h.shareMedian.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Greater(h.runs, 0);
        }

        [Test]
        public void KulTemposu_Halka1_BirSaat_Halka2_OnIkiSaatAlti()
        {
            // DEC-098 (audit #5): XP / 30, ring 1 = 150 Kül → ≤ 1 h at the direction's 4 500 XP/h; ring 1+2 ≈ 11 h (target ≤ 10 h).
            EconomySimulator.TreeModel.AshTempo t = EconomySimulator.TreeModel.Tempo();
            EconomySimulator.TreeModel.AshProfile reference = t.profiles[t.profiles.Length - 1];
            Assert.AreEqual(EconomySimulator.TreeModel.ReferenceXpPerHour, reference.xpPerHour);
            Assert.LessOrEqual(reference.ringHours[0], 1.01f, "halka 1");
            Assert.LessOrEqual(reference.ring2HoursWithOneOffs, 12f, "halka 1+2 (tek seferlikler yarısı)");
            // WP-107 (R4, kullanıcı 11.10 not 8): silah sayfasının Kül'ü (2 080) kalktı - tüm meta 26,3 saate indi; uzun kuyruğu
            // WP-104'ün "Uyanış" sayfası (S-04, C-00 kararı 2) yeniden kurar. Eşik 25 saat.
            Assert.GreaterOrEqual(reference.ringHours[2], 25f, "tüm meta 25+ saat (WP-107 sonrası)");
        }
    }
}
