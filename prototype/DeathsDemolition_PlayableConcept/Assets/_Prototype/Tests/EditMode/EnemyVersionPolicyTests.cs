using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // WP-051C: Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-010 · M2 ENEMY VERSIONS &amp; HYBRID ROUND DIFFICULTY — saf politika nöbetçisi.
    /// REQ-071 (garantili tanıtım), REQ-072 (havuz), DEC-021 (hibrit override), REQ-060/061
    /// (versiyon stat profili), DEC-015/019/022/023 (formül tarafı). Unity nesnesi yok;
    /// <see cref="TierSelectionPolicy"/> enjekte RNG ile koşar, config
    /// <c>ScriptableObject.CreateInstance</c> varsayılan tablosudur (tanıtım 1/3/5/8 —
    /// PrototypeOnly; testler sayıları config'ten TÜRETİR, kilitlemez).
    ///
    /// Ana prompt §8.1 maddeleri yorumda <c>[8.1-…]</c> ile işaretlidir.
    /// </summary>
    [TestFixture]
    [Category("WP010")]
    public sealed class EnemyVersionPolicyTests
    {
        private PrototypeConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
            {
                Object.DestroyImmediate(_config);
            }
        }

        // ------------------------------------------------------------------ yardımcılar

        private int Intro(EnemyTier tier) => RoundDifficultyOverrideRules.IntroductionRoundOf(_config, tier);

        /// <summary>Bir roundun tamamını (havuz kadar pick) politika ile çeker; çekiliş dizisini döner.</summary>
        private List<TierPick> KosuRound(TierSelectionPolicy policy, int round, uint seed, float releaseFraction = 0.3f, int poolOverride = -1)
        {
            int pool = poolOverride > 0 ? poolOverride : _config.TotalEnemyPool(round);
            var rng = new SeededTierRandom(seed);
            policy.BeginRound(round, _config, pool, releaseFraction);
            var picks = new List<TierPick>(pool);
            for (int i = 0; i < pool; i++)
            {
                picks.Add(policy.Next(_config, i, pool - i, rng));
            }

            return picks;
        }

        private static int Say(List<TierPick> picks, EnemyTier tier)
        {
            int n = 0;
            for (int i = 0; i < picks.Count; i++) if (picks[i].Tier == tier) n++;
            return n;
        }

        private static string Dizi(List<TierPick> picks)
        {
            var sb = new System.Text.StringBuilder(picks.Count);
            for (int i = 0; i < picks.Count; i++) sb.Append((int)picks[i].Tier);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ [8.1-1] determinizm

        [Test]
        public void AyniSeed_AyniKompozisyon_FarkliSeedFarkli()
        {
            uint[] seeds = { 11u, 4242u, 900001u };
            int[] rounds = { Intro(EnemyTier.Special), Intro(EnemyTier.Elite), Intro(EnemyTier.Ultra) + 2 };
            int farkli = 0;
            foreach (int round in rounds)
            {
                string[] diziler = new string[seeds.Length];
                for (int s = 0; s < seeds.Length; s++)
                {
                    string a = Dizi(KosuRound(new TierSelectionPolicy(), round, seeds[s]));
                    string b = Dizi(KosuRound(new TierSelectionPolicy(), round, seeds[s]));
                    Assert.AreEqual(a, b, "Aynı seed aynı round farklı kompozisyon üretti (round " + round + ", seed " + seeds[s] + ").");
                    diziler[s] = a;
                }

                if (diziler[0] != diziler[1] || diziler[1] != diziler[2]) farkli++;
            }

            Assert.Greater(farkli, 0, "Üç farklı seed üç roundun hiçbirinde farklı dizi üretmedi — RNG enjeksiyonu etkisiz.");
        }

        // ------------------------------------------------------------------ [8.1-2] tanıtım öncesi 0

        [TestCase(EnemyTier.Special)]
        [TestCase(EnemyTier.Elite)]
        [TestCase(EnemyTier.Ultra)]
        public void TanitimOncesiRound_TierHicSpawnOlmaz_1000Cekilis(EnemyTier tier)
        {
            int round = Intro(tier) - 1;
            Assume.That(round >= 1, "Tanıtım roundu 1 olan tier için öncesi yok.");
            var policy = new TierSelectionPolicy();
            var rng = new SeededTierRandom(7u);
            policy.BeginRound(round, _config, 1000, 0.3f);
            for (int i = 0; i < 1000; i++)
            {
                TierPick p = policy.Next(_config, i, 1000 - i, rng);
                Assert.AreNotEqual(tier, p.Tier, "Tanıtım öncesi roundda " + tier + " çekildi (çekiliş " + i + ").");
            }

            Assert.AreEqual(0, policy.PendingObligations);
        }

        // ------------------------------------------------------------------ [8.1-3] tanıtım roundunda garanti tam bir kez

        [TestCase(EnemyTier.Special)]
        [TestCase(EnemyTier.Elite)]
        [TestCase(EnemyTier.Ultra)]
        public void TanitimRoundu_GarantiTamBirKez_FazlasiAgirliktan(EnemyTier tier)
        {
            int round = Intro(tier);
            for (uint seed = 1; seed <= 25; seed++)
            {
                var policy = new TierSelectionPolicy();
                List<TierPick> picks = KosuRound(policy, round, seed);
                int guaranteed = 0, forced = 0;
                for (int i = 0; i < picks.Count; i++)
                {
                    if (picks[i].Tier == tier && picks[i].Guaranteed) guaranteed++;
                    if (picks[i].Forced) forced++;
                }

                Assert.AreEqual(1, guaranteed, tier + " garantisi tam bir kez olmalı (seed " + seed + ").");
                Assert.AreEqual(0, forced, "Override yokken zorunlu adet yok.");
                Assert.GreaterOrEqual(Say(picks, tier), 1, "Tanıtım roundunda tier hiç görünmedi.");
                Assert.AreEqual(0, policy.PendingObligations, "Round bitti, borç kaldı.");
                Assert.AreEqual(0, policy.ShortfallCount);
            }
        }

        // ------------------------------------------------------------------ [8.1-4] garanti konumu her zaman ilk değil (dağılım)

        [Test]
        public void GarantiKonumu_HerZamanIlkDusmanDegil_Dagilim()
        {
            int round = Intro(EnemyTier.Special);
            int pool = _config.TotalEnemyPool(round);
            int ilkte = 0, gecte = 0;
            var konumlar = new HashSet<int>();
            for (uint seed = 1; seed <= 60; seed++)
            {
                List<TierPick> picks = KosuRound(new TierSelectionPolicy(), round, seed);
                for (int i = 0; i < picks.Count; i++)
                {
                    if (picks[i].Guaranteed)
                    {
                        konumlar.Add(i);
                        if (i == 0) ilkte++;
                        if (i >= pool * 0.3f) gecte++;
                        break;
                    }
                }
            }

            // §8.1-4 "her zaman ilk düşman değil": borç pacing'den önce salıverilmez; ilk düşman ancak
            // ağırlıklı çekilişin ŞANSLA o tier'ı vermesiyle olabilir (R3'te ≈ %15) — bu yüzden
            // "hiçbir koşuda ilk" değil, "her koşuda ilk değil" ve pacing/dağılım kanıtı istenir.
            Assert.Less(ilkte, 60, "Garanti her koşuda ilk düşman oldu.");
            Assert.Greater(gecte, 0, "Garanti pacing eşiğinden sonra hiç yerleşmedi — pacing çalışmıyor?");
            Assert.Greater(konumlar.Count, 1, "Garanti hep aynı konuma düştü; şans dizisi (erken şansla ödeme) etkisiz.");
            Debug.Log("[DD-TEST] wp010.p04 firstEnemyRuns=" + ilkte + "/60 afterPacing=" + gecte + " distinctPositions=" + konumlar.Count);
        }

        // ------------------------------------------------------------------ [8.1-5] havuz = 1 ve 2 zorlama

        [TestCase(1)]
        [TestCase(2)]
        public void KucukHavuz_GarantiZorlanir(int pool)
        {
            int round = Intro(EnemyTier.Special);
            var policy = new TierSelectionPolicy();
            List<TierPick> picks = KosuRound(policy, round, 3u, 0.3f, pool);
            Assert.AreEqual(pool, picks.Count);
            Assert.AreEqual(1, Say(picks, EnemyTier.Special), "Havuz " + pool + ": tanıtım garantisi kayboldu.");
            Assert.IsTrue(picks[0].Guaranteed || (pool == 2 && picks[1].Guaranteed));
            Assert.AreEqual(0, policy.ShortfallCount, "Havuz garantiye yetiyor; kayıp olmamalı.");
        }

        // ------------------------------------------------------------------ [8.1-6] havuz < bekleyen garanti: mümkün olan kadarı + uyarı

        [Test]
        public void HavuzBekleyendenKucuk_MumkunOlanKadari_SessizKayipYok()
        {
            // İki tier'ı aynı roundda tanıt, havuz 1 → biri sığar, biri sığmaz; kayıp SAYILIR ve UYARILIR.
            int round = Intro(EnemyTier.Elite);
            _config.Enemies.Find(EnemyTier.Ultra).IntroductionRound = round;
            var policy = new TierSelectionPolicy();
            LogAssert.Expect(LogType.Warning, new Regex("owed tier spawn\\(s\\) do not fit a pool of 1"));
            List<TierPick> picks = KosuRound(policy, round, 5u, 0.3f, 1);
            Assert.AreEqual(1, picks.Count);
            Assert.IsTrue(picks[0].Guaranteed);
            Assert.AreEqual(1, policy.ShortfallCount, "Sığmayan garanti sayılmalı.");
            Assert.AreEqual(round, policy.LastShortfallRound);
            Assert.AreEqual(0, policy.PendingObligations);
        }

        // ------------------------------------------------------------------ [8.1-7] aynı roundda iki tanıtım: deterministik sıra

        [Test]
        public void AyniRoundIkiTanitim_SiraDeterministik_ConfigListesiSirasi()
        {
            int round = Intro(EnemyTier.Elite);
            _config.Enemies.Find(EnemyTier.Ultra).IntroductionRound = round;
            string ilkSira = null;
            for (uint seed = 1; seed <= 10; seed++)
            {
                var policy = new TierSelectionPolicy();
                policy.BeginRound(round, _config, 2, 0f);   // pacing 0: ikisi de hemen
                var rng = new SeededTierRandom(seed);
                TierPick a = policy.Next(_config, 0, 2, rng);
                TierPick b = policy.Next(_config, 1, 1, rng);
                Assert.IsTrue(a.Guaranteed && b.Guaranteed);
                string sira = a.Tier + ">" + b.Tier;
                if (ilkSira == null) ilkSira = sira;
                Assert.AreEqual(ilkSira, sira, "Çoklu tanıtım sırası seed'e göre değişti.");
            }

            // Sıra = config tier listesi sırası (Elite listede Ultra'dan önce).
            Assert.AreEqual(EnemyTier.Elite + ">" + EnemyTier.Ultra, ilkSira);
        }

        // ------------------------------------------------------------------ [8.1-8] havuz tükenirken bekleyen garanti zorlanır

        [Test]
        public void HavuzTukenirken_BekleyenGarantiZorlanir()
        {
            int round = Intro(EnemyTier.Ultra);
            var policy = new TierSelectionPolicy();
            int pool = 10;
            // Pacing 1.0 → normal yoldan hiç salıverilmez; yalnız "kalan ≤ borç" zorlar.
            policy.BeginRound(round, _config, pool, 1f);
            var rng = new SeededTierRandom(99u);
            // Ağırlıklı çekilişte Ultra erken gelirse borç şansla ödenir; bu yüzden her adımda kontrol.
            bool odendi = false;
            for (int i = 0; i < pool; i++)
            {
                TierPick p = policy.Next(_config, i, pool - i, rng);
                if (p.Guaranteed) { odendi = true; Assert.AreEqual(EnemyTier.Ultra, p.Tier); }
            }

            Assert.IsTrue(odendi, "Havuz tükendi, garanti ödenmedi.");
            Assert.AreEqual(0, policy.PendingObligations);
            Assert.AreEqual(0, policy.ShortfallCount);
        }

        // ------------------------------------------------------------------ Ç-1: geri alınan rezervasyon garantiyi yitirmez

        [Test]
        public void GeriAlinanPick_GarantiYenidenBorclanir_DuzPickUnutulur()
        {
            int round = Intro(EnemyTier.Elite);
            var policy = new TierSelectionPolicy();
            policy.BeginRound(round, _config, 10, 0f);   // pacing 0: garanti ilk çağrıda
            var rng = new SeededTierRandom(2u);
            TierPick g = policy.Next(_config, 0, 10, rng);
            Assert.IsTrue(g.Guaranteed && g.Tier == EnemyTier.Elite);
            Assert.AreEqual(0, policy.PendingGuaranteed);
            Assert.AreEqual(1, policy.Picks);

            policy.Return(in g);   // aktivasyon başarısız → rezervasyon geri alındı
            Assert.AreEqual(1, policy.PendingGuaranteed, "Geri alınan garanti yeniden borçlanmalı.");
            Assert.AreEqual(0, policy.Picks);
            TierPick again = policy.Next(_config, 0, 10, rng);
            Assert.IsTrue(again.Guaranteed && again.Tier == EnemyTier.Elite, "Geri verilen garanti sıradaki ilk pick olmalı.");
            Assert.AreEqual(0, policy.PendingGuaranteed);

            TierPick plain = policy.Next(_config, 1, 9, rng);
            Assert.IsFalse(plain.Guaranteed || plain.Forced);
            policy.Return(in plain);
            policy.Return(in plain);
            Assert.AreEqual(0, policy.PendingObligations, "Düz pick geri verilince borç doğmaz.");
        }

        // ------------------------------------------------------------------ [8.1-9] ağırlık büyümesi ve tavanı monoton

        [TestCase(EnemyTier.Special)]
        [TestCase(EnemyTier.Elite)]
        [TestCase(EnemyTier.Ultra)]
        public void AgirlikBuyumesi_MonotonVeTavanli(EnemyTier tier)
        {
            EnemyTierSettings t = _config.Enemies.Find(tier);
            float onceki = -1f;
            for (int round = 1; round <= 60; round++)
            {
                float w = _config.TierSpawnWeight(tier, round);
                if (round < t.IntroductionRound)
                {
                    Assert.AreEqual(0f, w, tier + " tanıtım öncesi ağırlık 0 olmalı (round " + round + ").");
                    continue;
                }

                Assert.GreaterOrEqual(w, onceki, tier + " ağırlığı düştü (round " + round + ").");
                Assert.LessOrEqual(w, t.SpawnWeightMax + 1e-4f, tier + " ağırlığı tavanı aştı.");
                onceki = w;
            }

            Assert.AreEqual(t.SpawnWeightMax, _config.TierSpawnWeight(tier, 60), 1e-4f, "60. roundda tavan.");
        }

        // ------------------------------------------------------------------ [8.1-10] boş tablo = formül paritesi

        [Test]
        public void BosOverrideTablosu_FormulIleBitEsit()
        {
            Assert.AreEqual(0, _config.RoundDifficultyOverrides.Count, "Öncül: taze config tablosu boş.");
            for (int round = 1; round <= 12; round++)
            {
                string bos = Dizi(KosuRound(new TierSelectionPolicy(), round, 77u));

                // Tabloya "hiçbir alanı override etmeyen" bir satır ekle → aynı dizi.
                var satir = new RoundDifficultyOverride { Round = round };
                Assert.IsTrue(_config.ReplaceRoundDifficultyOverrides(new[] { satir }, out List<string> hatalar), string.Join("; ", hatalar));
                string notr = Dizi(KosuRound(new TierSelectionPolicy(), round, 77u));
                Assert.AreEqual(bos, notr, "Alansız override satırı formül paritesini bozdu (round " + round + ").");

                Assert.IsTrue(_config.ReplaceRoundDifficultyOverrides(null, out _));
                foreach (EnemyTier tier in new[] { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra })
                {
                    Assert.AreEqual(_config.Enemies.Find(tier).ResolveSpawnWeight(round), _config.TierSpawnWeight(tier, round), 1e-5f);
                    foreach (ArmorSlot slot in new[] { ArmorSlot.Helmet, ArmorSlot.Body, ArmorSlot.Leg })
                    {
                        Assert.AreEqual(_config.Enemies.Find(tier).ArmorChances.Resolve(slot, round), _config.ArmorChance(tier, slot, round), 1e-6f);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ [8.1-11] override: tam / kısmi / yalnız zırh / yalnız ağırlık / yalnız adet

        [Test]
        public void Override_YalnizZirh_AgirlikFormuldeKalir()
        {
            int round = Intro(EnemyTier.Ultra) + 1;
            var satir = new RoundDifficultyOverride
            {
                Round = round, OverrideArmorChances = true,
                ArmorChances = new ArmorChanceSettings { HelmetChance = 0.5f, BodyChance = 0.25f, LegChance = 0f, ChanceGrowthPerRound = 0f, MaxChance = 1f }
            };
            Assert.IsTrue(_config.ReplaceRoundDifficultyOverrides(new[] { satir }, out List<string> h), string.Join("; ", h));
            foreach (EnemyTier tier in new[] { EnemyTier.Standard, EnemyTier.Elite })
            {
                Assert.AreEqual(0.5f, _config.ArmorChance(tier, ArmorSlot.Helmet, round), 1e-6f, "Zırh override'ı tier'dan bağımsız (round bazlı).");
                Assert.AreEqual(0f, _config.ArmorChance(tier, ArmorSlot.Leg, round), 1e-6f);
                Assert.AreEqual(_config.Enemies.Find(tier).ResolveSpawnWeight(round), _config.TierSpawnWeight(tier, round), 1e-5f, "Ağırlık formülde kalmalı.");
                Assert.AreEqual(_config.Enemies.Find(tier).ArmorChances.Resolve(ArmorSlot.Helmet, round + 1), _config.ArmorChance(tier, ArmorSlot.Helmet, round + 1), 1e-6f, "Komşu round formülde.");
            }
        }

        [Test]
        public void Override_YalnizAgirlik_KismiSatir_ListelenmeyenTierFormulde()
        {
            int round = Intro(EnemyTier.Ultra) + 1;
            var satir = new RoundDifficultyOverride
            {
                Round = round, OverrideTierWeights = true,
                TierWeights = new List<TierWeightOverride> { new TierWeightOverride { Tier = EnemyTier.Ultra, Weight = 500f }, new TierWeightOverride { Tier = EnemyTier.Special, Weight = 0f } }
            };
            Assert.IsTrue(_config.ReplaceRoundDifficultyOverrides(new[] { satir }, out List<string> h), string.Join("; ", h));
            Assert.AreEqual(500f, _config.TierSpawnWeight(EnemyTier.Ultra, round), 1e-5f, "Listelenen tier satır ağırlığı.");
            Assert.AreEqual(0f, _config.TierSpawnWeight(EnemyTier.Special, round), 1e-5f, "Listede 0 verilen tier çekilişten çıkar.");
            Assert.AreEqual(_config.Enemies.Find(EnemyTier.Elite).ResolveSpawnWeight(round), _config.TierSpawnWeight(EnemyTier.Elite, round), 1e-5f, "Listelenmeyen tier formülde (kısmi satır).");
            Assert.AreEqual(_config.Enemies.Find(EnemyTier.Standard).ResolveSpawnWeight(round), _config.TierSpawnWeight(EnemyTier.Standard, round), 1e-5f);
            Assert.AreEqual(_config.Enemies.Find(EnemyTier.Standard).ArmorChances.Resolve(ArmorSlot.Helmet, round), _config.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, round), 1e-6f, "Zırh formülde.");

            // Etki: 1000 çekilişte Ultra baskın, Special 0.
            var policy = new TierSelectionPolicy();
            policy.BeginRound(round, _config, 1000, 0f);
            var rng = new SeededTierRandom(3u);
            int ultra = 0, special = 0;
            for (int i = 0; i < 1000; i++)
            {
                EnemyTier t = policy.Next(_config, i, 1000 - i, rng).Tier;
                if (t == EnemyTier.Ultra) ultra++;
                if (t == EnemyTier.Special) special++;
            }

            Assert.Greater(ultra, 500);
            Assert.AreEqual(0, special);
        }

        [Test]
        public void Override_YalnizZorunluAdet_GarantiyleBirlesik_HavuzuAsmaz()
        {
            int round = Intro(EnemyTier.Ultra);   // tanıtım roundu: garanti 1 + zorunlu 3 → toplam en az 3 Ultra
            var satir = new RoundDifficultyOverride
            {
                Round = round, OverrideForcedCounts = true,
                ForcedTierCounts = new List<TierCountOverride> { new TierCountOverride { Tier = EnemyTier.Ultra, Count = 3 }, new TierCountOverride { Tier = EnemyTier.Elite, Count = 2 } }
            };
            Assert.IsTrue(_config.ReplaceRoundDifficultyOverrides(new[] { satir }, out List<string> h), string.Join("; ", h));
            Assert.AreEqual(3, _config.ForcedTierCount(EnemyTier.Ultra, round));
            Assert.AreEqual(0, _config.ForcedTierCount(EnemyTier.Ultra, round + 1));

            var policy = new TierSelectionPolicy();
            List<TierPick> picks = KosuRound(policy, round, 21u, 0.3f);
            Assert.AreEqual(5, picks.Count > 5 ? 5 : picks.Count, "Öncül: havuz ≥ 5.");
            Assert.GreaterOrEqual(Say(picks, EnemyTier.Ultra), 3, "Zorunlu adet + garanti: en az 3 Ultra.");
            Assert.GreaterOrEqual(Say(picks, EnemyTier.Elite), 2);
            int guaranteed = 0, forced = 0;
            for (int i = 0; i < picks.Count; i++) { if (picks[i].Guaranteed) guaranteed++; if (picks[i].Forced) forced++; }
            Assert.AreEqual(1, guaranteed, "Garanti bir kez (zorunlu adet ayrı işaretlenir).");
            Assert.AreEqual(2 + 2, forced, "Zorunlu: Ultra 3−1 (garanti sayılır) + Elite 2.");
            Assert.AreEqual(0, policy.PendingObligations);
            Assert.AreEqual(0, policy.ShortfallCount);
            Assert.AreEqual(0, policy.IgnoredForcedCounts);
        }

        [Test]
        public void Override_TamSatir_UcAlanBirlikte()
        {
            int round = Intro(EnemyTier.Ultra) + 3;
            var satir = new RoundDifficultyOverride
            {
                Round = round,
                OverrideArmorChances = true, ArmorChances = new ArmorChanceSettings { HelmetChance = 1f, BodyChance = 1f, LegChance = 1f, ChanceGrowthPerRound = 0f, MaxChance = 1f },
                OverrideTierWeights = true, TierWeights = new List<TierWeightOverride> { new TierWeightOverride { Tier = EnemyTier.Standard, Weight = 0f } },
                OverrideForcedCounts = true, ForcedTierCounts = new List<TierCountOverride> { new TierCountOverride { Tier = EnemyTier.Elite, Count = 4 } }
            };
            Assert.IsTrue(_config.ReplaceRoundDifficultyOverrides(new[] { satir }, out List<string> h), string.Join("; ", h));
            Assert.AreEqual(1f, _config.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, round), 1e-6f);
            Assert.AreEqual(0f, _config.TierSpawnWeight(EnemyTier.Standard, round));
            List<TierPick> picks = KosuRound(new TierSelectionPolicy(), round, 8u);
            Assert.AreEqual(0, Say(picks, EnemyTier.Standard), "Standard ağırlığı 0 → hiç çekilmez.");
            Assert.GreaterOrEqual(Say(picks, EnemyTier.Elite), 4);
        }

        // ------------------------------------------------------------------ [8.1-12] override hataları

        [Test]
        public void Override_AyniRoundIkiSatir_Hata()
        {
            var rows = new[] { new RoundDifficultyOverride { Round = 6 }, new RoundDifficultyOverride { Round = 6 } };
            Assert.IsFalse(_config.ReplaceRoundDifficultyOverrides(rows, out List<string> h));
            Assert.IsTrue(h.Exists(x => x.Contains("duplicate row")), string.Join("; ", h));
            Assert.AreEqual(0, _config.RoundDifficultyOverrides.Count, "Reddedilen tablo uygulanmamalı.");
        }

        [Test]
        public void Override_TanitilmamisTieraAgirlikVeyaAdet_Hata()
        {
            int round = Intro(EnemyTier.Ultra) - 1;
            var w = new RoundDifficultyOverride { Round = round, OverrideTierWeights = true, TierWeights = new List<TierWeightOverride> { new TierWeightOverride { Tier = EnemyTier.Ultra, Weight = 10f } } };
            Assert.IsFalse(_config.ReplaceRoundDifficultyOverrides(new[] { w }, out List<string> h1));
            Assert.IsTrue(h1.Exists(x => x.Contains("before its introduction round")), string.Join("; ", h1));

            var c = new RoundDifficultyOverride { Round = round, OverrideForcedCounts = true, ForcedTierCounts = new List<TierCountOverride> { new TierCountOverride { Tier = EnemyTier.Ultra, Count = 1 } } };
            Assert.IsFalse(_config.ReplaceRoundDifficultyOverrides(new[] { c }, out List<string> h2));
            Assert.IsTrue(h2.Exists(x => x.Contains("forced before its introduction round")), string.Join("; ", h2));

            // Ret, tabloyu değiştirmez.
            Assert.AreEqual(0, _config.RoundDifficultyOverrides.Count);
        }

        [Test]
        public void Override_OlasilikAralikDisi_Hata()
        {
            var r = new RoundDifficultyOverride { Round = 4, OverrideArmorChances = true, ArmorChances = new ArmorChanceSettings { HelmetChance = 1.5f, BodyChance = -0.1f, LegChance = 0.2f, MaxChance = 1f } };
            Assert.IsFalse(_config.ReplaceRoundDifficultyOverrides(new[] { r }, out List<string> h));
            Assert.AreEqual(2, h.FindAll(x => x.Contains("within [0, 1]")).Count, string.Join("; ", h));
        }

        [Test]
        public void Override_ZorunluAdetHavuzdanBuyuk_Hata()
        {
            int round = Intro(EnemyTier.Ultra);
            int pool = _config.TotalEnemyPool(round);
            var r = new RoundDifficultyOverride { Round = round, OverrideForcedCounts = true, ForcedTierCounts = new List<TierCountOverride> { new TierCountOverride { Tier = EnemyTier.Elite, Count = pool } } };
            // Elite adet = havuz, Ultra tanıtımı (kapsanmamış) +1 → aşar.
            Assert.IsFalse(_config.ReplaceRoundDifficultyOverrides(new[] { r }, out List<string> h));
            Assert.IsTrue(h.Exists(x => x.Contains("exceed the round's pool")), string.Join("; ", h));
        }

        // ------------------------------------------------------------------ [8.1-13] zorunlu adet + garanti havuzu aşmaz (politika tarafı)

        [Test]
        public void ZorunluAdetVeGaranti_HavuzuAsmaz_KayipSayilir()
        {
            int round = Intro(EnemyTier.Ultra);
            var r = new RoundDifficultyOverride { Round = round, OverrideForcedCounts = true, ForcedTierCounts = new List<TierCountOverride> { new TierCountOverride { Tier = EnemyTier.Elite, Count = 3 } } };
            Assert.IsTrue(_config.ReplaceRoundDifficultyOverrides(new[] { r }, out List<string> h), string.Join("; ", h));
            var policy = new TierSelectionPolicy();
            LogAssert.Expect(LogType.Warning, new Regex("do not fit a pool of 2"));
            List<TierPick> picks = KosuRound(policy, round, 5u, 0.3f, 2);   // borç 1 Ultra + 3 Elite = 4 > 2
            Assert.AreEqual(2, picks.Count);
            Assert.AreEqual(2, policy.ShortfallCount);
            Assert.IsTrue(picks[0].Guaranteed && picks[0].Tier == EnemyTier.Ultra, "Tanıtım borcu zorunlu adetten önce ödenir.");
            Assert.IsTrue(picks[1].Forced && picks[1].Tier == EnemyTier.Elite);
        }

        // ------------------------------------------------------------------ [8.1-14] formül monotonluk / sınır

        [Test]
        public void Formul_MonotonVeSinirlar()
        {
            int oncekiPool = 0; int oncekiActive = 0; float oncekiH = 0f; float oncekiD = 0f;
            for (int round = 1; round <= 50; round++)
            {
                int pool = _config.TotalEnemyPool(round);
                int active = _config.TargetActiveEnemies(round);
                float hm = _config.EnemyHealthMultiplier(round);
                float dm = _config.EnemyDamageMultiplier(round);
                Assert.GreaterOrEqual(pool, oncekiPool); Assert.GreaterOrEqual(active, oncekiActive);
                Assert.GreaterOrEqual(hm, oncekiH); Assert.GreaterOrEqual(dm, oncekiD);
                Assert.LessOrEqual(active, _config.AbsoluteSafetyCeiling);
                Assert.GreaterOrEqual(pool, 1);
                oncekiPool = pool; oncekiActive = active; oncekiH = hm; oncekiD = dm;
            }

            Assert.AreEqual(_config.TotalEnemyPool(1), _config.TotalEnemyPool(0), "Round < 1 → round 1 gibi.");
            Assert.AreEqual(_config.TotalEnemyPool(1), _config.TotalEnemyPool(-7));
            Assert.Greater(_config.TotalEnemyPool(int.MaxValue), 0, "Uç round taşmıyor.");
            Assert.LessOrEqual(_config.TargetActiveEnemies(int.MaxValue), _config.AbsoluteSafetyCeiling);
            Assert.AreEqual(1f, _config.EnemyHealthMultiplier(1), 1e-6f);
            Assert.AreEqual(1f, _config.EnemyDamageMultiplier(1), 1e-6f);
            Assert.AreEqual(1f, _config.EnemyHealthMultiplier(-3), 1e-6f, "Negatif round çarpanı 1.");
        }

        [Test]
        public void Formul_NaNVeNegatifConfig_Cokmez()
        {
            _config.Rounds.PoolPowerExponent = float.NaN;
            int p = _config.TotalEnemyPool(5);
            Assert.GreaterOrEqual(p, 1, "NaN üs ile havuz ≥ 1 (RoundToInt(NaN) → 0 → Max 1).");
            _config.Rounds.PoolPowerExponent = 1.15f;
            _config.Rounds.PoolBase = -100f;
            Assert.GreaterOrEqual(_config.TotalEnemyPool(1), 1, "Negatif taban havuzu 1'in altına indirmez.");
            _config.Rounds.HealthMultiplierPerRound = -1f;
            Assert.LessOrEqual(_config.EnemyHealthMultiplier(5), 1f);
            // Politika NaN ağırlığı atlar.
            _config.Enemies.Find(EnemyTier.Standard).SpawnWeight = float.NaN;
            var policy = new TierSelectionPolicy();
            policy.BeginRound(1, _config, 5, 0.3f);
            Assert.AreEqual(EnemyTier.Standard, policy.Next(_config, 0, 5, new SeededTierRandom(1u)).Tier, "Aday yoksa Standard.");
        }

        // ------------------------------------------------------------------ [8.1-15] stat snapshot spawn'da dondurulur

        [Test]
        public void StatSnapshot_SpawnRoundundaCozulur_RoundCarpaniDegisseDeSabit()
        {
            EnemyStatSnapshot r3 = EnemyStatSnapshot.Resolve(_config, EnemyTier.Elite, 3);
            EnemyStatSnapshot r7 = EnemyStatSnapshot.Resolve(_config, EnemyTier.Elite, 7);
            EnemyTierSettings t = _config.Enemies.Find(EnemyTier.Elite);
            Assert.IsTrue(r3.IsValid);
            Assert.AreEqual(3, r3.SpawnRound);
            Assert.AreEqual(_config.Enemies.StandardBaseHealth * t.HealthMultiplier * _config.EnemyHealthMultiplier(3), r3.MaxHealth, 1e-4f);
            Assert.AreEqual(t.AttackDamage * _config.EnemyDamageMultiplier(3), r3.AttackDamage, 1e-4f);
            Assert.AreEqual(t.MoveSpeed, r3.MoveSpeed, 1e-6f);
            Assert.AreEqual(t.StaggerSusceptibility, r3.StaggerSusceptibility, 1e-6f);
            Assert.AreEqual(t.TelegraphTime, r3.TelegraphTime, 1e-6f);
            Assert.Greater(r7.MaxHealth, r3.MaxHealth, "Round 7 profili daha güçlü (formül).");
            Assert.IsFalse(r3.SameProfileAs(in r7));
            Assert.IsTrue(r3.SameProfileAs(EnemyStatSnapshot.Resolve(_config, EnemyTier.Elite, 3)), "Aynı girdi aynı profil (deterministik).");

            // (i) r ↔ r+3: yalnız round çarpanına bağlı alanlar farklı; tier ayarı alanları birebir (pozitif kontrol).
            Assert.AreNotEqual(r3.HealthMultiplier, r7.HealthMultiplier);
            Assert.AreNotEqual(r3.DamageMultiplier, r7.DamageMultiplier);
            Assert.AreEqual(r3.MoveSpeed, r7.MoveSpeed, 1e-6f);
            Assert.AreEqual(r3.TelegraphTime, r7.TelegraphTime, 1e-6f);
            Assert.AreEqual(r3.AttackActiveTime, r7.AttackActiveTime, 1e-6f);
            Assert.AreEqual(r3.AttackRecoveryTime, r7.AttackRecoveryTime, 1e-6f);
            Assert.AreEqual(r3.AttackRange, r7.AttackRange, 1e-6f);
            Assert.AreEqual(r3.AttackCooldown, r7.AttackCooldown, 1e-6f);
            Assert.AreEqual(r3.BodyRadius, r7.BodyRadius, 1e-6f);
            Assert.AreEqual(r3.SeparationMass, r7.SeparationMass, 1e-6f);
            Assert.AreEqual(r3.StaggerSusceptibility, r7.StaggerSusceptibility, 1e-6f);
            // (iii) canlı config düzenlemesi: yeni çözüm yeni katsayıyı aynı spawn roundu ile görür (canlı gövde S05'te).
            _config.Rounds.HealthMultiplierPerRound *= 3f;
            EnemyStatSnapshot yeniden = EnemyStatSnapshot.Resolve(_config, EnemyTier.Elite, 3);
            Assert.AreEqual(3, yeniden.SpawnRound);
            Assert.AreNotEqual(r3.MaxHealth, yeniden.MaxHealth, "Yeni çözüm yeni katsayıyı görür (canlı config yolu).");
            Assert.IsFalse(EnemyStatSnapshot.Resolve(null, EnemyTier.Elite, 3).IsValid);
        }

        // ------------------------------------------------------------------ [8.1-16] DisplayName bağımsızlığı

        [Test]
        public void DisplayName_YalnizKozmetik_EnumOtoritesi()
        {
            EnemyTierSettings t = _config.Enemies.Find(EnemyTier.Elite);
            string ad = t.DisplayName;
            t.DisplayName = "TOTALLY-DIFFERENT";
            Assert.AreEqual(EnemyTier.Elite, EnemyStatSnapshot.Resolve(_config, EnemyTier.Elite, 5).Tier);
            Assert.AreEqual(_config.GoldReward(EnemyTier.Elite), _config.GoldReward(EnemyTier.Elite));
            Assert.AreEqual(t.GoldReward, _config.GoldReward(EnemyTier.Elite), "Gold enum ile bulunur.");
            Assert.AreEqual(t.HealthMultiplier * _config.Enemies.StandardBaseHealth, _config.EnemyHealth(EnemyTier.Elite, 1), 1e-4f);
            // Kaynak taraması: üretim kodunda tier DisplayName tüketicisi 0 (yalnız config tanımı).
            string root = Path.Combine(Application.dataPath, "_Prototype");
            var okuyanlar = new List<string>();
            foreach (string f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = f.Substring(root.Length + 1).Replace('\\', '/');
                if (rel.StartsWith("Tests/") || rel == "Core/Config/EnemyConfigSection.cs") continue;
                string src = File.ReadAllText(f);
                if (Regex.IsMatch(src, @"EnemyTierSettings\b[^;\n]*\.DisplayName|\bsettings\.DisplayName\b[^\n]*Tier|Enemies\.Find\([^)]*\)\.DisplayName"))
                {
                    okuyanlar.Add(rel);
                }
            }

            Assert.IsEmpty(okuyanlar, "Tier DisplayName üretim kodunda okunuyor: " + string.Join(", ", okuyanlar));
            t.DisplayName = ad;
        }

        // ------------------------------------------------------------------ [8.1-17] kaynak taraması: seçim yolu temiz

        [Test]
        public void KaynakTaramasi_SecimYolundaRandomAramaLinqStringYok()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype");
            string[] dosyalar = { "Spawn/TierSelectionPolicy.cs", "Spawn/SpawnTierSelector.cs", "Enemies/EnemyStatSnapshot.cs", "Core/Config/RoundDifficultyOverrideRules.cs", "Core/EnemyTierAuthorityBridge.cs", "Core/Debug/HeapWindow.cs" };
            var yasak = new Regex(@"UnityEngine\.Random\.|\bRandom\.(value|Range)\b|FindObjectOfType|FindObjectsOfType|GameObject\.Find\(|System\.Linq|string\.Format|\$""|GetAllocatedBytesForCurrentThread|ProfilerRecorder");
            var ihlaller = new List<string>();
            foreach (string d in dosyalar)
            {
                string p = Path.Combine(root, d);
                Assert.IsTrue(File.Exists(p), "Tarama kapsamı kırık: " + d);
                string[] satirlar = File.ReadAllLines(p);
                for (int i = 0; i < satirlar.Length; i++)
                {
                    string s = satirlar[i].Trim();
                    if (s.StartsWith("//") || s.StartsWith("///") || s.StartsWith("*")) continue;
                    if (!yasak.IsMatch(s)) continue;
                    // Tek gerekçeli istisna: sevk edilen RNG kaynağının kendisi (UnityTierRandomSource.Next01).
                    if (d == "Spawn/TierSelectionPolicy.cs" && s.Contains("public float Next01() => Random.value;")) continue;
                    ihlaller.Add(d + ":" + (i + 1) + " " + s);
                }
            }

            Assert.IsEmpty(ihlaller, string.Join("\n", ihlaller));
            // EnemyTierScheduler kaldırıldı: tek otorite.
            Assert.IsFalse(File.Exists(Path.Combine(root, "Enemies", "EnemyTierScheduler.cs")), "İkinci tier otoritesi hâlâ var.");

            // P21: ReplaceRoundDifficultyOverrides harness'inin üretim çağıranı 0 (Tests/ ve Diagnostics/ hariç); pozitif kontrol: hariç tutulanlarda > 0.
            int uretim = 0, haric = 0;
            // P22: Enemies/ yön kilidi — yalnız ZombieSpawnBinding.cs Spawn'a dokunur; director enemy tipi öğrenmez.
            var yonIhlal = new List<string>();
            foreach (string f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = f.Substring(root.Length + 1).Replace('\\', '/');
                string src = File.ReadAllText(f);
                if (src.Contains("ReplaceRoundDifficultyOverrides(") && !rel.EndsWith("Core/Config/PrototypeConfig.cs"))
                {
                    if (rel.StartsWith("Tests/") || rel.StartsWith("Diagnostics/")) haric++; else uretim++;
                }

                // Yorumlar ve string literal'ler kod değildir (uyarı metinleri director'ı adıyla anar).
                string kod = Regex.Replace(Regex.Replace(src, @"(?m)^\s*//.*$|/\*[\s\S]*?\*/", ""), @"\$?""(?:\\.|[^""\\])*""", "\"\"");
                if (rel.StartsWith("Enemies/") && rel != "Enemies/ZombieSpawnBinding.cs" && Regex.IsMatch(kod, @"DD\.Prototype\.Spawn\b|\bEnemySpawnDirector\b|\bSpawnTierSelector\b"))
                {
                    yonIhlal.Add(rel);
                }

                if (rel.StartsWith("Spawn/") && Regex.IsMatch(kod, @"using DD\.Prototype\.Enemies;|\bZombieEnemy\b|\bEnemyFactory\b"))
                {
                    yonIhlal.Add(rel + " (Spawn→Enemies)");
                }
            }

            Assert.AreEqual(0, uretim, "ReplaceRoundDifficultyOverrides üretim kodundan çağrılıyor.");
            Assert.Greater(haric, 0, "Pozitif kontrol: harness test/probe tarafından çağrılmalı.");
            Assert.IsEmpty(yonIhlal, "Enemies/Spawn yön kilidi ihlali: " + string.Join(", ", yonIhlal));
        }

        // ------------------------------------------------------------------ [8.1-18] ArmorProbe 09 ölçüm yardımcısı (HeapWindow) pozitif kontrol

        [Test]
        public void HeapWindow_PozitifKontrol_BosPencere_Gen0Yenileme()
        {
            DD.Prototype.Core.DebugTools.HeapWindow.Prepare();

            // Pozitif kontrol: bilinen 64 KB ayırma sayaçları oynatır — aksi hâlde ölçüm kör.
            byte[] yem = null;
            DD.Prototype.Core.DebugTools.HeapWindow.Result pozitif = DD.Prototype.Core.DebugTools.HeapWindow.Measure(() => { yem = new byte[64 * 1024]; }, 0);
            Assert.Greater(pozitif.Delta, 0, "64 KB pozitif kontrol görünmedi (yem " + (yem != null ? yem.Length : -1) + ").");
            Assert.AreEqual(0, pozitif.Retries);

            // Boş pencere: gen0 0, delta ~0 (nöbetçi: küçük ayırmaya duyarlı sınıfta bile ≤ 4 KB).
            DD.Prototype.Core.DebugTools.HeapWindow.Result bos = DD.Prototype.Core.DebugTools.HeapWindow.Measure(() => { }, 1);
            Assert.AreEqual(0, bos.Gen0);
            Assert.LessOrEqual(bos.Delta, 4 * 1024);

            // Sayaç nöbetçisi: açık bir toplama CollectionCount(0)'ı artırmalı — aksi hâlde gen0 ölçümü kör.
            int sayacOnce = System.GC.CollectionCount(0);
            System.GC.Collect();
            Assert.Greater(System.GC.CollectionCount(0), sayacOnce, "GC.CollectionCount(0) açık toplamada artmadı — gen0 nöbetçisi geçersiz.");

            // Enjekte gen0 (K-4): pencere gövdesi ilk denemede kendisi bir toplama tetikler → yardımcı
            // tam bir kez yeniler; ikinci deneme temizdir. Gövde iki kez koştuğu için olay sayaçları
            // DENEME BAŞINA sıfırlanmalı — tek pencere beklentisi (1) korunur, çift sayım olmaz.
            int cagri = 0, buDeneme = 0;
            bool ilk = true;
            DD.Prototype.Core.DebugTools.HeapWindow.Result enjekte = DD.Prototype.Core.DebugTools.HeapWindow.Measure(() =>
            {
                cagri++;
                buDeneme = 0;
                if (ilk) { ilk = false; System.GC.Collect(); }
                buDeneme++;
            }, 1);
            Assert.AreEqual(1, enjekte.Retries, "Gen0 görülünce tam bir kez yenilenmeli.");
            Assert.AreEqual(0, enjekte.Gen0, "Yenilenen (ikinci) pencere temiz olmalı.");
            Assert.AreEqual(2, cagri, "Yenileme pencere gövdesini ikinci kez koşturmalı.");
            Assert.AreEqual(1, buDeneme, "Deneme başına sıfırlanan sayaç tek pencere beklentisine eşit kalmalı (çift sayım yok).");

            // maxRetries 0: yenileme yok, pencere içindeki toplama olduğu gibi raporlanır.
            DD.Prototype.Core.DebugTools.HeapWindow.Result tek = DD.Prototype.Core.DebugTools.HeapWindow.Measure(() => System.GC.Collect(), 0);
            Assert.AreEqual(0, tek.Retries);
            Assert.Greater(tek.Gen0, 0);

            // İki ardışık yenileme gereksinimi: her denemede toplama → maxRetries kadar yeniler, sonra sonucu gizlemez.
            DD.Prototype.Core.DebugTools.HeapWindow.Result surekli = DD.Prototype.Core.DebugTools.HeapWindow.Measure(() => System.GC.Collect(), 1);
            Assert.AreEqual(1, surekli.Retries);
            Assert.Greater(surekli.Gen0, 0, "Yenileme sonrası hâlâ gen0 varsa gizlenmez.");
        }

        // ------------------------------------------------------------------ seçim yolu ayırma (EditMode: pozitif kontrollü yığın deltası)

        [Test]
        public void SecimYolu_KareBasinaAyirmaYok_PozitifKontrollu()
        {
            var policy = new TierSelectionPolicy();
            var rng = new SeededTierRandom(5u);
            int round = Intro(EnemyTier.Ultra) + 2;
            int pool = 200;
            policy.BeginRound(round, _config, pool, 0.3f);
            for (int i = 0; i < 50; i++) policy.Next(_config, i, pool - i, rng);   // ısınma

            // WP-051C: the process-wide used heap (GC.GetTotalMemory) also sees other Editor threads' allocations; the test
            // framework's GC.Alloc recorder is filtered to the calling thread only, and a positive control proves it is not blind.
            byte[] yem = null;
            Assert.That(() => { yem = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "Pozitif kontrol görünmedi — ölçüm geçersiz.");

            policy.BeginRound(round, _config, pool, 0.3f);
            Assert.That(() =>
            {
                for (int i = 0; i < pool; i++) policy.Next(_config, i, pool - i, rng);
                for (int r = 0; r < 20; r++) { policy.BeginRound(round + r, _config, pool, 0.3f); EnemyStatSnapshot.Resolve(_config, EnemyTier.Elite, round + r); }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "Seçim/snapshot yolu ayırdı (bu iş parçacığında GC.Alloc örneği; 200 pick + 20 BeginRound; yem " + (yem != null ? yem.Length : 0) + " B).");
        }
    }
}
