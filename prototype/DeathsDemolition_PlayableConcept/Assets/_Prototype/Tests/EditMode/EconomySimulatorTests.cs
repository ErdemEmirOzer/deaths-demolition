using System;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-041 · EconomySimulator (ADR-041-1): determinizm (aynı seed → aynı rapor), sevk edilen verilerle
    /// bant assert'leri (DEC-070 açılış fiyat bandı, DEC-071 tek satın alma modeli, DEC-021 giriş roundları,
    /// DEC-008 ilerleme) ve çıktı biçimleri. Hiçbir veri değişmez; simülatör yalnız okur.
    /// </summary>
    [Category("WP041")]
    public sealed class EconomySimulatorTests
    {
        private static EconomySimulator.Options Secenekler(uint seed = 1u, int last = 15)
        {
            Assert.IsNotNull(PrototypeConfig.Active, "EditMode'da Resources config yüklenmeli");
            return new EconomySimulator.Options { config = PrototypeConfig.Active, seed = seed, firstRound = 1, lastRound = last };
        }

        private static string UtcSiz(string json)
        {
            return Regex.Replace(json, "\"utc\": \"[^\"]*\"", "\"utc\": \"\"");
        }

        [Test]
        public void Determinizm_AyniSeedAyniRapor_FarkliSeedYineGecerli()
        {
            EconomySimulator.Report a = EconomySimulator.Run(Secenekler(5u, 12));
            EconomySimulator.Report b = EconomySimulator.Run(Secenekler(5u, 12));
            Assert.AreEqual(UtcSiz(EconomySimulator.ToJson(a)), UtcSiz(EconomySimulator.ToJson(b)), "aynı seed → byte-aynı rapor");
            EconomySimulator.Report c = EconomySimulator.Run(Secenekler(6u, 12));
            Assert.AreEqual(a.weapons.Length, c.weapons.Length);
            Assert.AreEqual(a.weapons[0].rounds.Length, c.weapons[0].rounds.Length);
            for (int i = 0; i < a.weapons[0].rounds.Length; i++)
            {
                Assert.AreEqual(a.weapons[0].rounds[i].pool, c.weapons[0].rounds[i].pool, "havuz seed'den bağımsız (formül)");
            }
        }

        [Test]
        public void SevkVerisi_RosterABC_GirisRoundlari_AcilisFiyatBandi()
        {
            EconomySimulator.Report r = EconomySimulator.Run(Secenekler());
            Assert.AreEqual(3, r.weapons.Length, "A/B/C etkin, D devre dışı (DEC-083)");
            Assert.AreEqual("A", r.weapons[0].rosterKey); Assert.AreEqual(1, r.weapons[0].unlockLevel);
            Assert.AreEqual(3, r.weapons[1].unlockLevel, "B seviye 3 (DEC-085 Ö-3, roster verisi)");
            Assert.AreEqual(5, r.weapons[2].unlockLevel, "C seviye 5");
            CollectionAssert.AreEqual(new[] { 1, 3, 5, 8 }, r.introductionRound, "DEC-021/022 giriş roundları");
            PrototypeConfig config = PrototypeConfig.Active;
            for (int i = 0; i < 10; i++)
            {
                Assert.GreaterOrEqual(r.openingCardCost[i], config.Economy.StartingCostMin, "DEC-070 alt bant");
                Assert.LessOrEqual(r.openingCardCost[i], config.Economy.StartingCostMax, "DEC-070 üst bant");
            }

            Assert.AreEqual(100, r.levelRequirement[0]); Assert.AreEqual(175, r.levelRequirement[1]);
            Assert.AreEqual(100, r.xpToReachLevel[0]); Assert.AreEqual(275, r.xpToReachLevel[1]);
        }

        [Test]
        public void Model_TekSatinAlmaPerRound_CuzdanNegatifOlmaz_HavuzVeGelirArtar()
        {
            EconomySimulator.Report r = EconomySimulator.Run(Secenekler());
            foreach (EconomySimulator.WeaponTrack w in r.weapons)
            {
                int wallet = 0;
                for (int i = 0; i < w.rounds.Length; i++)
                {
                    EconomySimulator.RoundRow row = w.rounds[i];
                    Assert.AreEqual(i + 1, row.round);
                    wallet += row.goldIncome - row.goldSpent;
                    Assert.AreEqual(wallet, row.walletAfter, "cüzdan muhasebesi");
                    Assert.GreaterOrEqual(row.walletAfter, 0, "DEC-074: satın alma cüzdanı eksiye düşürmez");
                    Assert.IsTrue(row.goldSpent == 0 || row.purchase != "-", "harcama varsa satın alma satırı var (DEC-071 tek kart)");
                    if (i > 0)
                    {
                        Assert.GreaterOrEqual(row.pool, w.rounds[i - 1].pool, "havuz azalmaz");
                        Assert.GreaterOrEqual(row.xpCumulative, w.rounds[i - 1].xpCumulative);
                    }

                    Assert.Greater(row.sustainedDps, 0f); Assert.GreaterOrEqual(row.crowdDps, row.sustainedDps);
                    Assert.GreaterOrEqual(row.clearSeconds, row.pacingFloorSeconds);
                    for (int t = 0; t < 4; t++)
                    {
                        Assert.IsTrue(row.ttk[t].count == 0 || row.round >= r.introductionRound[t], "tier giriş roundundan önce doğmaz");
                    }
                }

                Assert.Greater(w.rounds[w.rounds.Length - 1].goldIncome, w.rounds[0].goldIncome, "gelir R15 > R1");
                Assert.Greater(w.rounds[0].ttk[0].ttkNoArmour, 0f);
                Assert.Less(w.rounds[0].ttk[0].ttkNoArmour, 5f, "R1 Standard TTK makul (model)");
                Assert.Less(w.rounds[0].ttk[0].ttkNoArmour, w.rounds[0].ttk[0].ttkArmoured, "zırh TTK'yı artırır");
            }
        }

        [Test]
        public void Cikti_JsonVeMarkdown_YazilirVeSchemaTasir()
        {
            string dir = Path.Combine(Application.temporaryCachePath, "dd-wp041-sim");
            EconomySimulator.Report r = EconomySimulator.RunAndWrite(Secenekler(3u, 8), Path.Combine(dir, "balance-v1.json"), Path.Combine(dir, "balance-v1.md"));
            string json = File.ReadAllText(Path.Combine(dir, "balance-v1.json"));
            StringAssert.Contains("\"schema\": \"dd-balance-v1\"", json);
            string md = File.ReadAllText(Path.Combine(dir, "balance-v1.md"));
            StringAssert.Contains("## A · AssaultRifle", md);
            StringAssert.Contains("## Flags", md);
            Assert.IsNotNull(r.flags); Assert.IsNotNull(r.notes);
            Assert.AreEqual(8, r.weapons[0].rounds.Length);
            Directory.Delete(dir, true);
        }

        [Test]
        public void CrowdMultiplier_SifirBuildBir_MonotonArtar()
        {
            var t = WeaponUpgradeTuning.Active;
            var none = new int[10];
            Assert.AreEqual(1f, EconomySimulator.CrowdMultiplier(none, t), 1e-5f);
            var p1 = new int[10]; p1[(int)UpgradeBranch.Piercing] = 1;
            var p2 = new int[10]; p2[(int)UpgradeBranch.Piercing] = 2;
            Assert.Greater(EconomySimulator.CrowdMultiplier(p1, t), 1f);
            Assert.Greater(EconomySimulator.CrowdMultiplier(p2, t), EconomySimulator.CrowdMultiplier(p1, t));
            Assert.AreEqual("-", EconomySimulator.DescribeTiers(none));
            Assert.AreEqual("P2", EconomySimulator.DescribeTiers(p2));
        }
    }
}
