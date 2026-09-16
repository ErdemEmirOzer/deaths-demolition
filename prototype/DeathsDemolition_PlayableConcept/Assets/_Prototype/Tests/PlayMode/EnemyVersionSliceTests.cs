using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-010 · M2 ENEMY VERSIONS &amp; HYBRID ROUND DIFFICULTY — Run sahnesi nöbetçisi.
    /// Gerçek <see cref="EnemySpawnDirector"/>, gerçek havuz, gerçek <see cref="RunDirector"/>
    /// round akışı (F1 "Skip to round" = <c>RunDirector.BeginRound</c>), gerçek olay busu, Metal.
    /// REQ-071/072 (tanıtım + havuz), DEC-021 (enjekte override), REQ-060/061 (stat profili),
    /// DEC-043/069 (gold version bonus paritesi), WP-009 profil istatistik girdisi.
    ///
    /// Ana prompt §8.2/§8.3 maddeleri yorumda <c>[8.2-…]</c> ile işaretlidir. Tanıtım roundları
    /// (3/5/8) ve havuz sayıları config'ten TÜRETİLİR (PrototypeOnly); sevk edilen asset'in
    /// override tablosu boştur ve öyle kalır — satırlar yalnız test klonuna enjekte edilir.
    /// </summary>
    [TestFixture]
    [Category("WP010")]
    public sealed class EnemyVersionSliceTests
    {
        private const float KurulumZamanAsimi = 40f;
        private const float HizliAdim = 0.45f;   // = refill aralığı: her kare bir refill tiki (PrototypeOnly; nöbetçi ile)

        private RunDirector _run;
        private EnemySpawnDirector _director;
        private PlayerRig _rig;
        private PrototypeConfig _sevkConfig;
        private PrototypeConfig _klon;

        // ============================================================== kurulum

        private IEnumerator RunSahnesiniHazirla()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null, KurulumZamanAsimi, "RunDirector oluşmadı.");
            yield return Bekle(() => EnemySpawnDirector.Instance != null, KurulumZamanAsimi, "EnemySpawnDirector oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, KurulumZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");

            _run = RunDirector.Instance;
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;   // ölçümler oyuncu ölümüyle kesilmesin (tanı)
            if (_sevkConfig == null) _sevkConfig = PrototypeConfig.Active;   // WP-027: aynı test içinde ikinci sahne kurulumu (klon aktifken) sevk referansını ezmez
            Assert.IsNotNull(_sevkConfig);
            Assert.AreEqual(0, _sevkConfig.RoundDifficultyOverrides.Count, "Sevk edilen config'in DEC-021 tablosu boş olmalı (öncül).");
            Assert.AreEqual(HizliAdim, _sevkConfig.Rounds.SpawnRefillInterval, 0.0001f, "Refill aralığı sabiti saptı; HizliAdim'i yenile.");
            Assert.IsNotNull(_director.TierSelector, "Tek tier otoritesi (director.TierSelector) yok.");
            Assert.AreEqual(0, _director.TierSelector.PendingObligations);
        }

        /// <summary>Ölüm/EndRun sonrası bekleyen Results geçişi kırmızı bir assert'te de tüketilir; yoksa geç yükleme sonraki testin sahnesini değiştirir (WP-003 dersi, P6 Ö-18).</summary>
        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_run != null && _run.Phase == RunPhase.Dead)
            {
                yield return AkisiYatistir();
            }
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            RunFlowClock.ClearHolds();
            if (_sevkConfig != null && PrototypeConfig.Active != _sevkConfig)
            {
                PrototypeConfig.SetActive(_sevkConfig);
            }

            _sevkConfig = null;   // WP-027: bir sonraki test sevk config'ini yeniden yakalar

            if (_klon != null)
            {
                UnityEngine.Object.Destroy(_klon);
                _klon = null;
            }

            if (EnemySpawnDirector.Instance != null)
            {
                EnemySpawnDirector.Instance.TierSelector.SetRandom(null);
            }

            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            LogAssert.ignoreFailingMessages = false;
        }

        // ============================================================== yardımcılar

        private int Intro(EnemyTier tier) => RoundDifficultyOverrideRules.IntroductionRoundOf(PrototypeConfig.Active, tier);

        /// <summary>EnemyVersionProbe 02 ile aynı sabit seed'ler (Special 21, Elite 22, Ultra 23): deterministik; tanıtım tier'ı ilk düşman değil.</summary>
        private static uint IntroSeed(EnemyTier tier) => tier == EnemyTier.Special ? 21u : tier == EnemyTier.Elite ? 22u : 23u;

        /// <summary>
        /// WP-027 (WP-011 devri): "tanıtım roundu başında borç 1" okuması üretim çekilişiyle şansa bağlıydı — ağırlıklı
        /// çekiliş tanıtım tier'ını ilk spawn'larda verirse borç erken ödenir (R5 Elite ≈ %7, R8 Ultra ≈ %2 / spawn;
        /// ADR-010-2 şansla ödeme) ve <c>PendingGuaranteed</c> 0 okunur. Çözüm testte: klon config'e o round için tier
        /// ağırlığı 0 satırı konur (DEC-021 kısmi satır; sevk asset'i değişmez) → tier yalnız garantiyle doğar, borç pacing
        /// salıvermesine (havuzun %25'i) kadar 1 kalır. Politika davranışı değişmedi.
        /// </summary>
        private void TanitimTieriSanslaCekilmesin(EnemyTier tier, int round)
        {
            _klon = UnityEngine.Object.Instantiate(_sevkConfig);
            var satir = new RoundDifficultyOverride
            {
                Round = round,
                OverrideTierWeights = true,
                TierWeights = new List<TierWeightOverride> { new TierWeightOverride { Tier = tier, Weight = 0f } }
            };
            Assert.IsTrue(_klon.ReplaceRoundDifficultyOverrides(new[] { satir }, out List<string> hatalar), string.Join("; ", hatalar));
            PrototypeConfig.SetActive(_klon);
            Assert.AreEqual(0, _sevkConfig.RoundDifficultyOverrides.Count, "Sevk edilen asset'e satır sızdı.");
        }

        private sealed class SpawnKaydi : IDisposable
        {
            private readonly EnemySpawnDirector _d;
            private readonly Action<EnemySpawnContext> _h;
            public readonly List<EnemySpawnContext> Kayitlar = new List<EnemySpawnContext>(128);

            /// <summary>Kayıt sayısı işaret anında — "bu tur" sayımları buradan başlar.</summary>
            public int OncekiSayim { get; private set; }

            public void Isaretle() => OncekiSayim = Kayitlar.Count;

            public int SayBuTur(EnemyTier tier)
            {
                int n = 0;
                for (int i = OncekiSayim; i < Kayitlar.Count; i++) if (Kayitlar[i].Tier == tier) n++;
                return n;
            }

            public int GarantiliBuTur(EnemyTier tier)
            {
                int n = 0;
                for (int i = OncekiSayim; i < Kayitlar.Count; i++) if (Kayitlar[i].Tier == tier && Kayitlar[i].GuaranteedIntroduction) n++;
                return n;
            }

            public SpawnKaydi(EnemySpawnDirector d)
            {
                _d = d;
                _h = c => Kayitlar.Add(c);
                _d.EnemySpawned += _h;
            }

            public int Say(EnemyTier tier)
            {
                int n = 0;
                for (int i = 0; i < Kayitlar.Count; i++) if (Kayitlar[i].Tier == tier) n++;
                return n;
            }

            public int IlkIndeksBuTur(EnemyTier tier)
            {
                for (int i = OncekiSayim; i < Kayitlar.Count; i++) if (Kayitlar[i].Tier == tier) return i - OncekiSayim;
                return -1;
            }

            public int GarantiIndeksBuTur(EnemyTier tier)
            {
                for (int i = OncekiSayim; i < Kayitlar.Count; i++) if (Kayitlar[i].Tier == tier && Kayitlar[i].GuaranteedIntroduction) return i - OncekiSayim;
                return -1;
            }

            public int Garantili(EnemyTier tier)
            {
                int n = 0;
                for (int i = 0; i < Kayitlar.Count; i++) if (Kayitlar[i].Tier == tier && Kayitlar[i].GuaranteedIntroduction) n++;
                return n;
            }

            public string Dizi()
            {
                var sb = new System.Text.StringBuilder(Kayitlar.Count);
                for (int i = 0; i < Kayitlar.Count; i++) sb.Append((int)Kayitlar[i].Tier);
                return sb.ToString();
            }

            public void Dispose() => _d.EnemySpawned -= _h;
        }

        /// <summary>Ölüm sonrası akış (1.6 s sonra Results) yatışmadan başka sahne yüklenirse geç Results yüklemesi sonraki teste sızar (WP-003 dersi).</summary>
        private static IEnumerator AkisiYatistir()
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 4f && SceneManager.GetActiveScene().name != PrototypeScenes.Results)
            {
                yield return null;
            }

            yield return null;
            Debug.Log("[DD-TEST] wp010 akış yatıştı: scene=" + SceneManager.GetActiveScene().name);
        }

        private static int TumYasayanlariOldur()
        {
            int n = 0;
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (live[i] != null && live[i].IsAlive) { live[i].Kill(); n++; }
            }

            return n;
        }

        /// <summary>Round'u başlatır ve havuzu ölümle tüketir (hızlandırılmış kare); RoundCleared beklenmez (director StopRound ile kapatılır).</summary>
        private IEnumerator RounduTuket(int round, SpawnKaydi kayit, float zamanAsimi = 60f)
        {
            RunFlowClock.ClearHolds();
            _director.ForceActiveTarget(0, false);
            kayit?.Isaretle();
            _run.BeginRound(round);
            yield return null;
            Assert.AreEqual(round, _director.Round);
            Time.captureDeltaTime = HizliAdim;
            float t0 = Time.realtimeSinceStartup;
            while (_director.RemainingInPool > 0 && Time.realtimeSinceStartup - t0 < zamanAsimi)
            {
                TumYasayanlariOldur();
                yield return null;
            }

            Time.captureDeltaTime = 0f;
            Assert.AreEqual(0, _director.RemainingInPool, "Havuz tüketilemedi: " + _director.DescribeFlow());
            Assert.AreEqual(_director.Population.SpawnedTotal, kayit != null ? kayit.Kayitlar.Count - kayit.OncekiSayim : _director.Population.SpawnedTotal, "Spawn olayı sayısı havuzla eşleşmiyor.");
            Assert.AreEqual(0, _director.TierSelector.PendingObligations, "Havuz bitti, borç kaldı: " + _director.TierSelector.DescribePending());
        }

        // ============================================================== [8.2-1][8.2-2] tanıtım roundları gerçek spawn'da

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TanitimRoundlari_GarantiGercekSpawnda_OncesindeSifir()
        {
            yield return RunSahnesiniHazirla();
            using (var kayit = new SpawnKaydi(_director))
            {
                foreach (EnemyTier tier in new[] { EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra })
                {
                    int intro = Intro(tier);

                    // [8.2-2] tanıtım öncesi round: tier 0 (havuzun tamamı).
                    yield return RounduTuket(intro - 1, kayit);
                    Assert.AreEqual(0, kayit.SayBuTur(tier), tier + " tanıtım öncesi roundda (" + (intro - 1) + ") doğdu.");

                    // [8.2-1] tanıtım roundu: en az bir garanti, tam bir kez işaretli. Üretim çekilişi
                    // tier'ı şansla ilk düşman yapabilir (R3 ≈ %15, R5 ≈ %7, R8 ≈ %2 — ADR-010-2 şansla
                    // ödeme); bu yüzden "ilk değil" ve "salıverme indeksinden geç değil" iddiaları
                    // EnemyVersionProbe 02 ile aynı sabit seed'de okunur (dağılım: P04/V02).
                    _director.TierSelector.SetRandom(new SeededTierRandom(IntroSeed(tier)));
                    yield return RounduTuket(intro, kayit);
                    _director.TierSelector.SetRandom(null);
                    Assert.GreaterOrEqual(kayit.SayBuTur(tier), 1, tier + " tanıtım roundunda doğmadı.");
                    Assert.AreEqual(1, kayit.GarantiliBuTur(tier), tier + " garanti bayrağı tam bir kez olmalı.");
                    int ilk = kayit.IlkIndeksBuTur(tier), garanti = kayit.GarantiIndeksBuTur(tier);
                    int saliverme = Mathf.CeilToInt(_director.Population.SpawnedTotal * _director.GuaranteedIntroPoolFraction);
                    Assert.Greater(ilk, 0, tier + " roundun ilk düşmanı oldu (seed " + IntroSeed(tier) + ").");
                    Assert.LessOrEqual(garanti, saliverme, tier + " garanti bayrağı pacing salıvermesinden geç (" + garanti + " > " + saliverme + ").");
                    Debug.Log("[DD-TEST] wp010.intro tier=" + tier + " round=" + intro + " pool=" + _director.Population.SpawnedTotal + " count=" + kayit.SayBuTur(tier) + " guaranteed=" + kayit.GarantiliBuTur(tier) + " first=" + ilk + " debut=" + garanti + " release=" + saliverme + " seed=" + IntroSeed(tier));
                }
            }
        }

        // ============================================================== [8.2-3] havuz tükenirken bekleyen garanti zorlanır

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HavuzTukenirken_BekleyenGarantiZorlanir()
        {
            yield return RunSahnesiniHazirla();
            int intro = Intro(EnemyTier.Ultra);
            _klon = UnityEngine.Object.Instantiate(_sevkConfig);
            // Pacing'i imkânsız kıl: tanıtım tier'ının formül ağırlığı 0 ve garanti salıverme oranı 1.0'a yakın →
            // yalnız "kalan ≤ borç" zorlar. Ağırlık 0: SpawnWeight/Max 0 (kısmi satır DEĞİL, tier tablosu; klon).
            EnemyTierSettings u = _klon.Enemies.Find(EnemyTier.Ultra);
            u.SpawnWeight = 0f; u.SpawnWeightGrowthPerRound = 0f; u.SpawnWeightMax = 0f;
            PrototypeConfig.SetActive(_klon);
            yield return null;
            using (var kayit = new SpawnKaydi(_director))
            {
                yield return RounduTuket(intro, kayit);
                Assert.AreEqual(1, kayit.Say(EnemyTier.Ultra), "Ağırlık 0 iken garanti yine tam bir kez (zorlama).");
                Assert.AreEqual(1, kayit.Garantili(EnemyTier.Ultra));
                Assert.AreEqual(0, _director.TierSelector.Policy.ShortfallCount);
            }
        }

        // ============================================================== [8.2-4] ölüm round ortasında; ikinci koşu temiz; abone/statik sayıları eşit

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator OlumRoundOrtasinda_BekleyenGarantiDuser_IkinciKosuTemiz_AboneSayilariEsit()
        {
            yield return RunSahnesiniHazirla();
            Dictionary<string, int> kosu1 = LifecycleTestKit.AboneAnlikGoruntu();
            int intro = Intro(EnemyTier.Ultra);
            TanitimTieriSanslaCekilmesin(EnemyTier.Ultra, intro);   // WP-027: borç şansla erken ödenmesin (deterministik)
            _director.ForceActiveTarget(0, false);
            _run.BeginRound(intro);
            yield return null;
            Assert.AreEqual(1, _director.TierSelector.PendingGuaranteed, "Tanıtım roundu başında Ultra borcu 1 olmalı.");

            // Round ortasında ölüm.
            _rig.Health.DiagnosisInvulnerable = false;
            int ended = 0;
            Action<RunSummary> onEnded = s => ended++;
            RunEvents.RunEnded += onEnded;
            try
            {
                _run.EndRun(RunEndReason.PlayerDeath, "test");
                yield return null;
                Assert.AreEqual(1, ended);
                Assert.AreEqual(RunPhase.Dead, _run.Phase);
                Assert.AreEqual(0, _director.TierSelector.PendingObligations, "Ölüm bekleyen garantiyi düşürmedi.");
                Assert.AreEqual(0, _director.TierSelector.PendingGuaranteed);
            }
            finally
            {
                RunEvents.RunEnded -= onEnded;
            }

            // İkinci koşu: ölüm akışı (Results) yatıştıktan sonra temiz başlar; round 1'de borç 0; Ultra yok.
            yield return AkisiYatistir();
            PrototypeConfig.SetActive(_sevkConfig);   // WP-027: klon satırı yalnız tanıtım roundu içindi; ikinci koşu sevk config'iyle
            yield return RunSahnesiniHazirla();
            Assert.AreEqual(0, _director.TierSelector.PendingObligations);
            Assert.AreEqual(1, _director.Round);
            Assert.AreEqual(0, _director.TierSelector.Policy.ShortfallCount, "Şortfall sayacı ikinci koşuya taşındı.");
            Dictionary<string, int> kosu2 = LifecycleTestKit.AboneAnlikGoruntu();
            LifecycleTestKit.AboneleriKarsilastir("koşu2 vs koşu1 (WP-010 tier otoritesi abone büyütmüyor)", kosu1, kosu2);
            int directorSayisi = UnityEngine.Object.FindObjectsOfType<EnemySpawnDirector>(false).Length;
            Assert.AreEqual(1, directorSayisi, "Duplicate director.");
            Assert.IsTrue(DD.Prototype.Core.EnemyTierAuthorityBridge.IsBound, "İkinci koşuda tier otoritesi köprüsü bağlı olmalı (K-7).");
            Assert.AreEqual(_director.Round, DD.Prototype.Core.EnemyTierAuthorityBridge.CurrentRound());
        }

        // ============================================================== [8.2-5] round geçişinde canlı düşman stat'ı sabit

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator RoundGecisinde_CanliDusmanStatiSabit_CanliConfigDuzenlemesiSpawnRoundIle()
        {
            yield return RunSahnesiniHazirla();
            int r = Intro(EnemyTier.Elite);
            _director.ForceActiveTarget(0, false);
            _run.BeginRound(r);
            yield return null;
            // Director'a ait olmayan (debug/test yolu) bir Elite: round sınırında temizlenmez → gerçekten "canlı düşman round geçer".
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Elite, r, _rig.transform.position + new Vector3(0f, 0f, 8f), Quaternion.identity);
            Assert.IsNotNull(z);
            EnemyStatSnapshot once = z.Stats;
            Assert.IsTrue(once.IsValid);
            Assert.AreEqual(EnemyTier.Elite, once.Tier);
            Assert.AreEqual(r, once.SpawnRound);
            Assert.AreEqual(_sevkConfig.EnemyHealth(EnemyTier.Elite, r), once.MaxHealth, 1e-3f);
            Assert.AreEqual(once.MaxHealth, z.MaxHealth, 1e-3f, "Beden snapshot canıyla koşmuyor.");
            Assert.AreEqual(_sevkConfig.EnemyAttackDamage(EnemyTier.Elite, r), once.AttackDamage, 1e-3f);
            float can = z.Health;

            _run.BeginRound(r + 3);
            yield return null;
            yield return null;
            Assert.IsTrue(z.IsAlive, "Debug zombi round geçişinde silindi (öncül bozuk).");
            Assert.IsTrue(z.Stats.SameProfileAs(in once), "Round geçişi canlı düşmanın profilini değiştirdi.");
            Assert.AreEqual(can, z.Health, 1e-3f);
            Assert.AreEqual(r, z.SpawnRound);
            Assert.AreNotEqual(_sevkConfig.EnemyHealth(EnemyTier.Elite, r + 3), z.MaxHealth, "Canlı düşman yeni round çarpanına geçmiş.");

            // Canlı config düzenlemesi (F1): yeniden çözüm SPAWN roundu ile — mevcut davranış, belgelenir.
            float katsayi = _sevkConfig.Rounds.HealthMultiplierPerRound;
            try
            {
                _sevkConfig.Rounds.HealthMultiplierPerRound = katsayi * 2f;
                _sevkConfig.NotifyChanged();
                yield return null;
                yield return null;
                Assert.AreEqual(r, z.Stats.SpawnRound, "Canlı düzenleme spawn roundunu değiştirdi.");
                Assert.AreEqual(_sevkConfig.EnemyHealth(EnemyTier.Elite, r), z.Stats.MaxHealth, 1e-3f, "Canlı düzenleme spawn roundu ile yeniden çözülmeli.");
            }
            finally
            {
                _sevkConfig.Rounds.HealthMultiplierPerRound = katsayi;
                _sevkConfig.NotifyChanged();
            }

            z.Remove();
        }

        // ============================================================== [8.2-6][8.3] tier kill → stats → summary → profil girdisi; gold version bonus paritesi

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TierKill_StatsSummaryProfilGirdisi_UnattributedSifir_GoldVersionBonusParite()
        {
            yield return RunSahnesiniHazirla();
            int r = Intro(EnemyTier.Ultra);
            _director.ForceActiveTarget(0, false);
            _run.BeginRound(r);
            yield return null;
            _director.StopRound();
            EnemyFactory.ClearAll();
            yield return null;

            var olumler = new List<EnemyDeathInfo>();
            Action<EnemyDeathInfo> onDied = i => olumler.Add(i);
            DamageEvents.EnemyDied += onDied;
            RunSummary ozet = default; int ended = 0;
            Action<RunSummary> onEnded = s => { ozet = s; ended++; };
            RunEvents.RunEnded += onEnded;
            try
            {
                int[] adet = { 3, 2, 2, 1 };   // Standard, Special, Elite, Ultra
                EnemyTier[] tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };
                for (int t = 0; t < tiers.Length; t++)
                {
                    for (int k = 0; k < adet[t]; k++)
                    {
                        ZombieEnemy z = EnemyFactory.Spawn(tiers[t], r, _rig.transform.position + new Vector3(3f + k, 0f, 6f + t), Quaternion.identity);
                        Assert.IsNotNull(z);
                        Assert.AreEqual(tiers[t], z.Stats.Tier, "Snapshot tier kanonik enum.");
                        z.Kill();
                    }
                }

                yield return null;
                Assert.AreEqual(8, olumler.Count);
                for (int t = 0; t < tiers.Length; t++)
                {
                    Assert.AreEqual(adet[t], _run.Stats.KillsFor(tiers[t]), tiers[t] + " kill sayacı.");
                }

                Assert.AreEqual(8, _run.Stats.TotalKills);

                // [8.3] gold version bonus = aile değeri − Standard (WP-008), zırhsız sentetik spawn'da zırh eki yalnız kompozisyona bağlı.
                for (int i = 0; i < olumler.Count; i++)
                {
                    EnemyDeathInfo o = olumler[i];
                    Assert.IsTrue(o.Reward.IsValid);
                    Assert.AreEqual(GoldRewardCalculator.VersionBonus(o.Tier, _sevkConfig), o.Reward.Value.VariantContribution, o.Tier + " version bonus WP-008 ile farklı.");
                    Assert.AreEqual(GoldRewardCalculator.BaseReward(_sevkConfig), o.Reward.Value.Base);
                    Assert.AreEqual(GoldRewardCalculator.ArmorBonus(o.Reward.Armor, _sevkConfig), o.Reward.Value.ArmorContribution);
                    Assert.AreEqual(o.Reward.Total, o.GoldReward);
                }

                _rig.Health.DiagnosisInvulnerable = false;
                _run.EndRun(RunEndReason.PlayerDeath, "test");
                yield return null;
                Assert.AreEqual(1, ended);
                var girdi = new RunRewardInput(in ozet);
                Assert.AreEqual(3, girdi.StandardKills); Assert.AreEqual(2, girdi.SpecialKills); Assert.AreEqual(2, girdi.EliteKills); Assert.AreEqual(1, girdi.UltraKills);
                Assert.AreEqual(0, girdi.UnattributedKills, "TotalKills = Standard+Special+Elite+Ultra invariantı (WP-009).");
                Assert.AreEqual(8, ozet.TotalKills);
                Assert.AreEqual(EnemyTier.Ultra, ozet.HighestTierKilled);
            }
            finally
            {
                DamageEvents.EnemyDied -= onDied;
                RunEvents.RunEnded -= onEnded;
            }

            yield return AkisiYatistir();
        }

        // ============================================================== [8.2-7] enjekte override satırı gerçek spawn'da (asset dokunulmaz)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator EnjekteOverride_GercekSpawnda_ZirhAgirlikZorunluAdet_AssetBos()
        {
            yield return RunSahnesiniHazirla();
            int r = Intro(EnemyTier.Ultra) + 1;
            _klon = UnityEngine.Object.Instantiate(_sevkConfig);
            var satir = new RoundDifficultyOverride
            {
                Round = r,
                OverrideArmorChances = true,
                ArmorChances = new ArmorChanceSettings { HelmetChance = 1f, BodyChance = 0f, LegChance = 0f, ChanceGrowthPerRound = 0f, MaxChance = 1f },
                OverrideTierWeights = true,
                TierWeights = new List<TierWeightOverride> { new TierWeightOverride { Tier = EnemyTier.Special, Weight = 0f }, new TierWeightOverride { Tier = EnemyTier.Elite, Weight = 0f } },
                OverrideForcedCounts = true,
                ForcedTierCounts = new List<TierCountOverride> { new TierCountOverride { Tier = EnemyTier.Elite, Count = 4 } }
            };
            Assert.IsTrue(_klon.ReplaceRoundDifficultyOverrides(new[] { satir }, out List<string> hatalar), string.Join("; ", hatalar));
            PrototypeConfig.SetActive(_klon);
            yield return null;
            Assert.AreEqual(0, _sevkConfig.RoundDifficultyOverrides.Count, "Sevk edilen asset'e satır sızdı.");

            var kasklar = new List<bool>();
            using (var kayit = new SpawnKaydi(_director))
            {
                Action<EnemySpawnContext> kaskKaydi = c =>
                {
                    IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                    for (int i = live.Count - 1; i >= 0; i--)
                    {
                        if (live[i] != null && live[i].IsAlive && live[i].SpawnTicket == c.Ticket) { kasklar.Add(live[i].Armor.IsEquipped(ArmorSlot.Helmet)); break; }
                    }
                };
                _director.EnemySpawned += kaskKaydi;
                try
                {
                    yield return RounduTuket(r, kayit);
                }
                finally
                {
                    _director.EnemySpawned -= kaskKaydi;
                }

                Assert.AreEqual(4, kayit.Say(EnemyTier.Elite), "Zorunlu adet gerçek spawn'da tam olarak uygulanmadı (ağırlık 0 + zorunlu 4 → yalnız zorunlu).");
                Assert.AreEqual(0, kayit.Say(EnemyTier.Special), "Ağırlık 0 verilen tier gerçek spawn'da doğdu.");
                Assert.AreEqual(kayit.Kayitlar.Count, kasklar.Count, "Her spawn için kask okuması alınmalı.");
                Assert.IsFalse(kasklar.Contains(false), "Zırh olasılığı 1.0 iken kasksız spawn.");
                Assert.AreEqual(0, _director.TierSelector.Policy.ShortfallCount);
            }

            // Aktif asset'te harness reddedilir (Y-1 sertleştirmesi); sevk asset'i geri alınınca klon temizlenir ve formül geri döner.
            Assert.IsFalse(_klon.ReplaceRoundDifficultyOverrides(null, out List<string> ret), "Aktif config üzerinde harness reddedilmeli.");
            Assert.IsTrue(ret.Count > 0 && ret[0].Contains("refused"));
            PrototypeConfig.SetActive(_sevkConfig);
            yield return null;
            Assert.IsTrue(_klon.ReplaceRoundDifficultyOverrides(null, out _));
            Assert.AreEqual(_sevkConfig.TierSpawnWeight(EnemyTier.Special, r), _klon.TierSpawnWeight(EnemyTier.Special, r), 1e-5f);
            Assert.IsFalse(_sevkConfig.ReplaceRoundDifficultyOverrides(null, out _), "Sevk edilen (aktif) asset harness'i reddetmeli.");
        }

        // ============================================================== [8.2-8] pause altında seçim/garanti donar

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Pause_SecimVeGarantiSayaclariDonar()
        {
            yield return RunSahnesiniHazirla();
            int r = Intro(EnemyTier.Elite);
            TanitimTieriSanslaCekilmesin(EnemyTier.Elite, r);   // WP-027: borç şansla erken ödenmesin (deterministik)
            _director.ForceActiveTarget(0, false);
            _run.BeginRound(r);
            yield return null;
            Assert.AreEqual(1, _director.TierSelector.PendingGuaranteed);
            int spawnedOnce = _director.Population.SpawnedTotal;
            int picksOnce = _director.TierSelector.Policy.Picks;
            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);
            float t0 = Time.realtimeSinceStartup;
            int kare = 0;
            while (Time.realtimeSinceStartup - t0 < 1.2f)
            {
                Assert.AreEqual(spawnedOnce, _director.Population.SpawnedTotal, "Pause altında spawn ilerledi.");
                Assert.AreEqual(picksOnce, _director.TierSelector.Policy.Picks, "Pause altında tier seçimi ilerledi.");
                Assert.AreEqual(1, _director.TierSelector.PendingGuaranteed, "Pause altında garanti sayacı değişti.");
                kare++;
                yield return null;
            }

            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            yield return Bekle(() => _director.Population.SpawnedTotal > spawnedOnce, 6f, "Pause kalkınca spawn devam etmedi (pozitif kontrol).");
            Assert.Greater(kare, 10);
        }

        // ============================================================== [8.2-9] havuzdan yeniden çıkan düşman önceki tier'ın stat'ını taşımaz

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HavuzIkinciYasam_OncekiTierStatiTasinmaz()
        {
            yield return RunSahnesiniHazirla();
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;
            Vector3 p = _rig.transform.position + new Vector3(0f, 0f, 7f);
            ZombieEnemy ultra = EnemyFactory.Spawn(EnemyTier.Ultra, 9, p, Quaternion.identity);
            Assert.IsNotNull(ultra);
            EnemyStatSnapshot u = ultra.Stats;
            Assert.AreEqual(EnemyTier.Ultra, u.Tier);
            Assert.AreEqual(EnemyTier.Ultra, ultra.VersionVisual.AppliedVersion);
            float ultraScale = ultra.transform.localScale.y * VisualScaleY(ultra);
            ultra.Remove();
            yield return null;
            Assert.IsFalse(ultra.Stats.IsValid, "Havuza dönen gövde eski profili taşıyor.");

            ZombieEnemy std = EnemyFactory.Spawn(EnemyTier.Standard, 1, p, Quaternion.identity);
            Assert.IsNotNull(std);
            Assert.AreEqual(EnemyTier.Standard, std.Stats.Tier);
            Assert.AreEqual(1, std.Stats.SpawnRound);
            Assert.AreEqual(_sevkConfig.EnemyHealth(EnemyTier.Standard, 1), std.MaxHealth, 1e-3f, "İkinci yaşam Standard canı değil.");
            Assert.Less(std.MaxHealth, u.MaxHealth);
            Assert.AreEqual(_sevkConfig.Enemies.Find(EnemyTier.Standard).MoveSpeed, std.Stats.MoveSpeed, 1e-4f);
            Assert.AreEqual(EnemyTier.Standard, std.VersionVisual.AppliedVersion, "Görünüm sözleşmesi yeni tier'a geçmedi.");
            // P20 placeholder paritesi: siluet tier'a göre uygulanır (Ultra "büyük", WP-002); aynı gövde Standard'a dönünce ölçek de döner.
            float stdScale = std.transform.localScale.y * VisualScaleY(std);
            Assert.Greater(ultraScale, stdScale, "Ultra silueti Standard'dan büyük olmalı (placeholder siluet paritesi).");
            Debug.Log("[DD-TEST] wp010.pool sameBody=" + ReferenceEquals(ultra, std) + " ultraHp=" + u.MaxHealth + " stdHp=" + std.MaxHealth + " ultraScale=" + ultraScale + " stdScale=" + stdScale);
            std.Remove();
        }

        // ============================================================== [8.2-10] determinizm gerçek director'da: aynı seed aynı kompozisyon

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AyniSeed_AyniKompozisyon_GercekDirector()
        {
            yield return RunSahnesiniHazirla();
            int r = Intro(EnemyTier.Ultra);
            string[] diziler = new string[2];
            for (int k = 0; k < 2; k++)
            {
                _director.TierSelector.SetRandom(new SeededTierRandom(0xC0FFEEu));
                using (var kayit = new SpawnKaydi(_director))
                {
                    yield return RounduTuket(r, kayit);
                    diziler[k] = kayit.Dizi();
                }
            }

            _director.TierSelector.SetRandom(null);
            Assert.AreEqual(diziler[0], diziler[1], "Aynı seed aynı round gerçek director'da farklı kompozisyon.");
            Assert.IsTrue(diziler[0].Contains(((int)EnemyTier.Ultra).ToString()), "Tanıtım roundunda Ultra yok.");
            Debug.Log("[DD-TEST] wp010.seed round=" + r + " len=" + diziler[0].Length + " seq=" + diziler[0]);
        }

        // ============================================================== yardımcı

        /// <summary>Rig'in "Visual" düğümünün y ölçeği (siluet); bulunamazsa 1.</summary>
        private static float VisualScaleY(ZombieEnemy z)
        {
            Transform v = z.transform.Find("Visual");
            return v != null ? v.localScale.y : 1f;
        }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float baslangic = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - baslangic > saniye)
                {
                    Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                }

                yield return null;
            }
        }
    }
}
