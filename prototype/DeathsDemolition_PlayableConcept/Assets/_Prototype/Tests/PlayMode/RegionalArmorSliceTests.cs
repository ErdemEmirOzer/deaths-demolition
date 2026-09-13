using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-006 · M2 BÖLGESEL ZIRH &amp; PATLAMA nöbetçisi — Run sahnesinde, gerçek silah yolu
    /// (<c>HitscanEngine.Fire</c> ışını gerçek hitbox'a, <c>HitscanEngine.Explode</c> gerçek
    /// alan sorgusu), gerçek zombi, gerçek olay busu, gerçek Metal.
    /// REQ-080…090 · REQ-063 · REQ-129 · FLOW-007/008 · WP-006 olay/transaction sözleşmesi.
    ///
    /// Ana prompt matris numaraları: [#1–#6] bölge/slot yönlendirmesi gerçek ışınla · [#7]
    /// başka bölge korunmaz · [#9] kırılınca cana · [#16]/[#17] tek patlama üç parça, bütçe
    /// çoğalmaz · [#21] havuz ikinci yaşam · [#22]/[#24] ölü düşman tekrar yok, gold/ledger tek
    /// · [#23] Standard kill · [#25] pause · [#26] ikinci sahnede abone sayıları · [#27]
    /// 0/1/2/3 harness · [#28] feedback olayları tam birer kez.
    ///
    /// SAYILAR PROTOTYPE-ONLY (sabit tazelik nöbetçisi ile): dayanıklılık 22/38/28, açık
    /// çarpanlar 2/1/0.8, Standard can 34 (R1), paylar 25/45/30. Hiçbiri ürün kararı değil.
    /// </summary>
    [TestFixture]
    public sealed class RegionalArmorSliceTests
    {
        private const float KurulumZamanAsimi = 40f;
        private const float StandardCanR1 = 34f;

        private PlayerRig _rig;
        private int _dupOnce;   // süreç ömürlü tanı sayacı: her test kendi farkını ölçer
        private readonly List<ZombieEnemy> _dogurulan = new List<ZombieEnemy>(8);

        // ============================================================== kurulum

        private IEnumerator RunSahnesiniHazirla()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null, KurulumZamanAsimi, "RunDirector oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, KurulumZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "Silah bağlanmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");

            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;   // ölçümler oyuncu ölümüyle kesilmesin (tanı)

            if (EnemySpawnDirector.Instance != null)
            {
                EnemySpawnDirector.Instance.StopRound();
                EnemySpawnDirector.Instance.ClearAllEnemies();
            }

            EnemyFactory.ClearAll();
            _dogurulan.Clear();
            DamageTargetResolver.ResetRegionDiagnostics();
            _dupOnce = ZombieEnemy.DuplicateApplicationCount;
            yield return null;

            SabitTazeligiDogrula();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            EnemyFactory.ClearAll();
            _dogurulan.Clear();
            DamageTransaction.End();
            LogAssert.ignoreFailingMessages = false;
        }

        private static void SabitTazeligiDogrula()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            Assert.IsNotNull(config, "PrototypeConfig.Active yok.");
            Assert.AreEqual(22f, config.Enemies.Armor.HelmetDurability, 0.0001f, "Kask dayanıklılığı sabiti saptı; türetmeyi yenile.");
            Assert.AreEqual(38f, config.Enemies.Armor.BodyDurability, 0.0001f);
            Assert.AreEqual(28f, config.Enemies.Armor.LegDurability, 0.0001f);
            Assert.AreEqual(StandardCanR1, config.EnemyHealth(EnemyTier.Standard, 1), 0.01f, "Standard R1 canı sabiti saptı; türetmeyi yenile.");
            Assert.AreNotEqual(1f, config.Enemies.Armor.ExposedHeadMultiplier, "Açık kafa çarpanı 1 ise açık/zırhlı ayrımının ayırt gücü yok (nöbetçi).");
            Assert.AreEqual(0, config.RoundDifficultyOverrides.Count, "DEC-021 tablosu boş kalmalı.");
        }

        // ============================================================== yardımcılar

        /// <summary>Oyuncudan uzakta, düz zeminde, oyuncuya bakan bir Standard doğurur (biletsiz debug/test yolu).</summary>
        private ZombieEnemy Dogur(Vector3 konum)
        {
            Vector3 d = _rig.transform.position - konum;
            d.y = 0f;
            Quaternion bakis = d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, konum, bakis);
            Assert.IsNotNull(z, "Zombi doğmadı (havuz/tavan).");
            _dogurulan.Add(z);
            return z;
        }

        /// <summary>Gerçek silah yolu: hitbox merkezine 2.5 m'den bir tüfek mermisi (pierce/chain/ricochet/explosion kapalı).</summary>
        private static ShotResult Vur(ZombieEnemy z, BodyRegion bolge, float hasar, float patlamaSansi = 0f, float patlamaYaricapi = 0f, float patlamaOlcegi = 1f)
        {
            Collider hitbox = z.HitboxFor(bolge);
            if (hitbox == null)
            {
                Assert.Fail("Rig'de " + bolge + " hitbox'ı yok.");   // yalnız hata dalında string kurulur (L26 ölçüm penceresi temiz)
            }

            Vector3 hedef = hitbox.bounds.center;

            // Zombinin ÖNÜNDEN (bakış yönünden) ateş: kendi diğer hitbox'larını kesmesin diye
            // yatay bir ışın, hedefin yüksekliğinde.
            Vector3 geri = -z.transform.forward;
            geri.y = 0f;
            geri = geri.sqrMagnitude > 0.0001f ? geri.normalized : Vector3.back;
            Vector3 kaynak = hedef - geri * 2.5f;
            kaynak.y = hedef.y;

            var istek = new ShotRequest
            {
                Origin = kaynak,
                Direction = (hedef - kaynak).normalized,
                Damage = hasar,
                Range = 6f,
                ImpactForce = DamageForce.RifleBullet,
                Source = DamageSourceKind.Bullet,
                Power = WeaponPower.Medium,
                PierceTargets = 0,
                PierceDamageFalloff = 1f,
                ExplosionChance = patlamaSansi,
                ExplosionRadius = patlamaYaricapi,
                ExplosionDamageScale = patlamaOlcegi,
                DrawTracer = false
            };

            return HitscanEngine.Fire(in istek);
        }

        private sealed class OlaySayaci : IDisposable
        {
            public readonly Dictionary<int, int> DamagedByLife = new Dictionary<int, int>();
            public readonly Dictionary<int, int> DressedByTarget = new Dictionary<int, int>();
            public readonly List<EnemyDamageInfo> Damaged;
            public readonly List<ArmorBreakInfo> Broken = new List<ArmorBreakInfo>();
            public readonly List<EnemyDeathInfo> Died = new List<EnemyDeathInfo>();
            public int DressedTotal;
            public HitFeedbackTier LastTier;

            private readonly Action<EnemyDamageInfo> _d;
            private readonly Action<ArmorBreakInfo> _b;
            private readonly Action<EnemyDeathInfo> _k;
            private readonly Action<EnemyHitFeedbackInfo, HitFeedbackTier> _f;

            /// <param name="kapasite">Olay listesi ön kapasitesi — GC ölçüm penceresinde sayacın kendisi büyümesin.</param>
            public OlaySayaci(int kapasite = 16)
            {
                Damaged = new List<EnemyDamageInfo>(kapasite);
                _d = i =>
                {
                    Damaged.Add(i);
                    DamagedByLife.TryGetValue(i.LifeId, out int n);
                    DamagedByLife[i.LifeId] = n + 1;
                };
                _b = i => Broken.Add(i);
                _k = i => Died.Add(i);
                _f = (i, t) =>
                {
                    DressedTotal++;
                    LastTier = t;
                    DressedByTarget.TryGetValue(i.TargetId, out int n);
                    DressedByTarget[i.TargetId] = n + 1;
                };

                DamageEvents.EnemyDamaged += _d;
                DamageEvents.ArmorBroken += _b;
                DamageEvents.EnemyDied += _k;
                HitFeedbackDirector.EnemyHitDressed += _f;
            }

            public void Dispose()
            {
                DamageEvents.EnemyDamaged -= _d;
                DamageEvents.ArmorBroken -= _b;
                DamageEvents.EnemyDied -= _k;
                HitFeedbackDirector.EnemyHitDressed -= _f;
            }

            public int DamagedFor(int lifeId) => DamagedByLife.TryGetValue(lifeId, out int n) ? n : 0;
        }

        private Vector3 TestZemini(float xOffset, float zOffset)
        {
            // Oyuncunun önünde, avlu içinde (merkez düz zemin); kamera/duvar etkileşimi yok.
            Vector3 p = _rig.transform.position + new Vector3(xOffset, 0f, zOffset);
            p.y = _rig.transform.position.y;
            return p;
        }

        // ============================================================== [#1–#7] [#9] D10 gerçek yol

        [UnityTest]
        [Category("WP006")]
        [Timeout(300000)]
        public IEnumerator DogrudanIsabet_GercekHitbox_BolgeSlotYonlendirmesi()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy z = Dogur(TestZemini(0f, 7f));
            yield return null;
            z.FitArmorComposition(true, true, true);
            Assert.AreEqual(3, z.Armor.EquippedCount);

            var rapor = new StringBuilder("[DD-TEST] wp006.direct\n");
            using (var sayac = new OlaySayaci())
            {
                // [#4] [#5] [#6]: her bölge yalnız kendi zırhına.
                foreach (BodyRegion bolge in new[] { BodyRegion.Head, BodyRegion.UpperBody, BodyRegion.LowerBody })
                {
                    ArmorSlot slot = DamageRegions.SlotFor(bolge);
                    float once = z.Armor.Current(slot);
                    float can = z.Health;
                    int fallbackOnce = DamageTargetResolver.RegionFallbackCount;

                    ShotResult atis = Vur(z, bolge, 5f);
                    Assert.IsTrue(atis.HitSomething && atis.EnemiesHit == 1, bolge + ": ışın hitbox'a değmedi (" + atis.EnemiesHit + ").");
                    Assert.AreEqual(RegionResolution.Registry, DamageTargetResolver.LastResolution, bolge + ": bölge registry'den çözülmedi.");
                    Assert.AreEqual(fallbackOnce, DamageTargetResolver.RegionFallbackCount, bolge + ": fallback sayacı arttı.");

                    EnemyDamageInfo son = sayac.Damaged[sayac.Damaged.Count - 1];
                    Assert.AreEqual(bolge, son.Result.Region, "Yayınlanan bölge farklı.");
                    Assert.AreEqual(slot, son.Meta.Slot, "Meta slot farklı.");
                    Assert.AreEqual(DamageKind.Direct, son.Meta.Kind);
                    Assert.AreEqual(z.LifeId, son.LifeId);
                    Assert.IsTrue(son.Result.WasArmored, bolge + " zırhlıyken açık sayıldı.");
                    Assert.AreEqual(0f, son.Result.HealthDamage, bolge + ": zırhlıyken cana hasar.");
                    Assert.AreEqual(5f, son.Result.ArmorDamage, 0.001f);
                    Assert.AreEqual(once, son.Meta.ArmorBefore(slot), 0.001f);
                    Assert.AreEqual(once - 5f, son.Meta.ArmorAfter(slot), 0.001f);
                    Assert.AreEqual(once - 5f, z.Armor.Current(slot), 0.001f);
                    Assert.AreEqual(can, z.Health, 0.001f);

                    // Diğer iki slot dokunulmadı [#7].
                    foreach (ArmorSlot diger in new[] { ArmorSlot.Helmet, ArmorSlot.Body, ArmorSlot.Leg })
                    {
                        if (diger == slot) continue;
                        Assert.AreEqual(son.Meta.ArmorBefore(diger), son.Meta.ArmorAfter(diger), 0.0001f, diger + " başka bölgenin vuruşunda değişti.");
                    }

                    Assert.AreEqual(ArmorVisualState.Damaged, z.ArmorVisualStateOf(slot), "K-12: hasarlı plaka görsel durumu Damaged olmalı.");
                    Assert.AreEqual(HitFeedbackTier.ArmorHit, sayac.LastTier, "[#28] zırh isabeti ArmorHit katmanında giydirilmeli.");
                    rapor.AppendLine("  " + bolge + " armored: armor " + once + "→" + z.Armor.Current(slot) + " health " + z.Health + " resolution=" + DamageTargetResolver.LastResolution);
                }

                Assert.AreEqual(3, sayac.DressedTotal, "[#28] üç zırh isabeti üç dressing üretmeli (ne eksik ne fazla).");
                Assert.AreEqual(0, sayac.Broken.Count);

                // [#9] kaskı kır → açık kafa; sonraki isabet cana (mevcut PrototypeOnly 2x çarpanla — ürün kararı değil).
                int kirilmaOnce = sayac.Broken.Count;
                ShotResult kir = Vur(z, BodyRegion.Head, 1000f);
                Assert.IsTrue(kir.AnyArmorBreak);
                Assert.AreEqual(kirilmaOnce + 1, sayac.Broken.Count, "[#10] kırılma tam bir kez yayınlanmalı.");
                ArmorBreakInfo kirik = sayac.Broken[sayac.Broken.Count - 1];
                Assert.AreEqual(ArmorSlot.Helmet, kirik.Slot);
                Assert.AreEqual(z.LifeId, kirik.LifeId);
                Assert.Greater(kirik.Meta.ArmorBefore(ArmorSlot.Helmet), 0f);
                Assert.AreEqual(0f, kirik.Meta.ArmorAfter(ArmorSlot.Helmet));
                Assert.AreEqual(StandardCanR1, z.Health, 0.001f, "[#8] taşma cana geçti.");
                Assert.AreEqual(ArmorVisualState.Broken, z.ArmorVisualStateOf(ArmorSlot.Helmet));
                Assert.AreEqual(HitFeedbackTier.ArmorBreak, sayac.LastTier, "[#28] kırılma ArmorBreak katmanında giydirilmeli.");

                float canOnce = z.Health;
                Vur(z, BodyRegion.Head, 1f);
                EnemyDamageInfo acik = sayac.Damaged[sayac.Damaged.Count - 1];
                Assert.IsFalse(acik.Result.WasArmored, "Kırık kask hâlâ koruyor.");
                Assert.Greater(acik.Result.HealthDamage, 0f, "[#9] kırıldıktan sonra isabet cana ulaşmadı.");
                Assert.AreEqual(1f * PrototypeConfig.Active.Enemies.Armor.ExposedHeadMultiplier, acik.Result.HealthDamage, 0.001f,
                    "Açık kafa hasarı mevcut PrototypeOnly çarpanla (config'ten) eşleşmeli.");
                Assert.AreEqual(canOnce - acik.Result.HealthDamage, z.Health, 0.001f);
                Assert.AreEqual(canOnce, acik.Meta.HealthBefore, 0.001f);
                Assert.AreEqual(z.Health, acik.Meta.HealthAfter, 0.001f);
                Assert.AreEqual(DamageEventClass.FleshHit | DamageEventClass.DirectHealthDamage, DamageEventClassifier.Classify(in acik));
                Assert.AreEqual(HitFeedbackTier.CriticalHit, sayac.LastTier, "[#28] açık kafa doğrudan isabeti CriticalHit (mevcut sözleşme, REQ-089 doğrudan dal).");

                // [#1–#3] zırhsız üç bölge cana (kompozisyon 0).
                z.FitArmorComposition(false, false, false);
                Assert.IsTrue(z.Armor.IsBare);
                foreach (BodyRegion bolge in new[] { BodyRegion.Head, BodyRegion.UpperBody, BodyRegion.LowerBody })
                {
                    float can = z.Health;
                    Vur(z, bolge, 1f);
                    EnemyDamageInfo son = sayac.Damaged[sayac.Damaged.Count - 1];
                    Assert.AreEqual(bolge, son.Result.Region);
                    Assert.IsFalse(son.Result.WasArmored);
                    Assert.Greater(son.Result.HealthDamage, 0f, bolge + " zırhsızken cana hasar vermedi.");
                    Assert.Less(z.Health, can);
                    Assert.AreEqual(ArmorVisualState.Absent, z.ArmorVisualStateOf(DamageRegions.SlotFor(bolge)));
                }

                Assert.AreEqual(0, DamageTargetResolver.RegionFallbackCount, "Gerçek zombi hitbox'ları için fallback 0 olmalı.");
                Assert.AreEqual(_dupOnce, ZombieEnemy.DuplicateApplicationCount);
                rapor.AppendLine("  events: damaged=" + sayac.Damaged.Count + " broken=" + sayac.Broken.Count + " dressed=" + sayac.DressedTotal + " fallback=" + DamageTargetResolver.RegionFallbackCount);
            }

            Debug.Log(rapor.ToString());
        }

        // ============================================================== [#27] L21 + [#21] L22 + [#25]

        [UnityTest]
        [Category("WP006")]
        [Timeout(300000)]
        public IEnumerator Kompozisyon0123_Deterministik_HavuzIkinciYasam_Pause()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy z = Dogur(TestZemini(0f, 7f));
            yield return null;

            // [#27] sekiz maske: model ↔ görsel durum birebir, DEC-021 tablosu boş.
            for (int mask = 0; mask < 8; mask++)
            {
                bool h = (mask & 1) != 0, b = (mask & 2) != 0, l = (mask & 4) != 0;
                int fitted = z.FitArmorComposition(h, b, l);
                Assert.AreEqual((h ? 1 : 0) + (b ? 1 : 0) + (l ? 1 : 0), fitted, "mask " + mask);
                Assert.AreEqual(fitted, z.Armor.EquippedCount);
                Assert.AreEqual(fitted, z.Armor.ProtectingCount);
                Assert.AreEqual(h, z.Armor.IsEquipped(ArmorSlot.Helmet));
                Assert.AreEqual(b, z.Armor.IsEquipped(ArmorSlot.Body));
                Assert.AreEqual(l, z.Armor.IsEquipped(ArmorSlot.Leg));
                Assert.AreEqual(h ? ArmorVisualState.Present : ArmorVisualState.Absent, z.ArmorVisualStateOf(ArmorSlot.Helmet));
                Assert.AreEqual(b ? ArmorVisualState.Present : ArmorVisualState.Absent, z.ArmorVisualStateOf(ArmorSlot.Body));
                Assert.AreEqual(l ? ArmorVisualState.Present : ArmorVisualState.Absent, z.ArmorVisualStateOf(ArmorSlot.Leg));
                Assert.AreEqual(fitted == 3, z.Armor.FullyProtected);
                Assert.IsNotNull(z.ArmorAnchors.Helmet); Assert.IsNotNull(z.ArmorAnchors.Body); Assert.IsNotNull(z.ArmorAnchors.Leg);
            }

            Assert.AreEqual(0, PrototypeConfig.Active.RoundDifficultyOverrides.Count, "Harness DEC-021 tablosunu doldurdu.");

            // [#25] pause: zırh/can/yaşam durumu donar. Pozitif kontrol: aynı anda bir ceset (despawn geri sayımı
            // 0.55 s) pause altında 1.2 s gerçek zaman boyunca sahnede kalır; pause kalkınca geri sayım koşar ve ceset
            // havuza döner — donma gerçekten simülasyonu durdurmasaydı ceset pause içinde kaybolurdu.
            z.FitArmorComposition(true, false, true);
            Vur(z, BodyRegion.Head, 5f);
            float kask = z.Armor.Current(ArmorSlot.Helmet);
            float can = z.Health;
            int life = z.LifeId;
            ZombieEnemy ceset = Dogur(TestZemini(3f, 7f));
            yield return null;
            ceset.Kill();
            Assert.IsFalse(ceset.IsAlive);
            Assert.IsTrue(ceset.IsSpawned, "Ceset ölür ölmez havuza döndü (linger yok).");
            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);
            float pauseBaslangic = Time.realtimeSinceStartup;
            int donmusKare = 0;
            while (Time.realtimeSinceStartup - pauseBaslangic < 1.2f)
            {
                Assert.AreEqual(0f, Time.deltaTime, 0f, "Pause'ta ölçekli delta 0 değil.");
                Assert.AreEqual(kask, z.Armor.Current(ArmorSlot.Helmet), 0f, "Pause'ta zırh değişti.");
                Assert.AreEqual(can, z.Health, 0f, "Pause'ta can değişti.");
                Assert.IsTrue(z.IsAlive && z.LifeId == life);
                Assert.AreEqual(ArmorVisualState.Damaged, z.ArmorVisualStateOf(ArmorSlot.Helmet));
                Assert.IsTrue(ceset.IsSpawned, "Pause altında ceset despawn geri sayımı ilerledi (donma delinmiş).");
                donmusKare++;
                yield return null;
            }
            Assert.Greater(donmusKare, 10, "Pause penceresinde yeterli kare gözlenmedi.");
            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            yield return null;
            yield return Bekle(() => !ceset.IsSpawned, 3f, "Pause kalktıktan sonra ceset geri sayımı koşmadı (pozitif kontrol).");
            Debug.Log("[DD-TEST] wp006.pause frozenFrames=" + donmusKare + " corpseDespawnedAfterResume=" + !ceset.IsSpawned);

            // [#21] havuza dön → ikinci yaşam: eski zırh/kırık/geçiş durumu yok.
            Vur(z, BodyRegion.Head, 1000f);
            Assert.IsTrue(z.Armor.IsBroken(ArmorSlot.Helmet));
            Assert.AreEqual(ArmorVisualState.Broken, z.ArmorVisualStateOf(ArmorSlot.Helmet));
            int eskiLife = z.LifeId;
            Vector3 konum = z.transform.position;
            z.Remove();
            yield return null;
            Assert.IsFalse(z.IsSpawned);
            Assert.IsTrue(z.Armor.IsBare, "Havuza dönen gövde zırh taşıyor (OnDespawned temizliği).");

            ZombieEnemy z2 = Dogur(konum);   // havuz aynı gövdeyi geri verebilir; her iki hâlde de temiz olmalı
            yield return null;
            Assert.Greater(z2.LifeId, eskiLife, "Yeni yaşam id'si artmalı.");
            Assert.IsTrue(z2.IsAlive);
            Assert.IsFalse(z2.Armor.IsBroken(ArmorSlot.Helmet), "İkinci yaşamda eski kırık kask.");
            Assert.IsFalse(z2.Armor.IsBroken(ArmorSlot.Body));
            Assert.IsFalse(z2.Armor.IsBroken(ArmorSlot.Leg));
            Assert.AreEqual(StandardCanR1, z2.Health, 0.01f, "İkinci yaşamda can tam değil.");
            foreach (ArmorSlot slot in new[] { ArmorSlot.Helmet, ArmorSlot.Body, ArmorSlot.Leg })
            {
                Assert.AreNotEqual(ArmorVisualState.Broken, z2.ArmorVisualStateOf(slot), slot + " ikinci yaşamda Broken görünüyor.");
                Assert.AreNotEqual(ArmorVisualState.Damaged, z2.ArmorVisualStateOf(slot));
                if (z2.Armor.IsEquipped(slot))
                {
                    Assert.AreEqual(z2.Armor.Max(slot), z2.Armor.Current(slot), 0.0001f, slot + " ikinci yaşamda tam dayanıklılıkta değil.");
                    Assert.AreEqual(ArmorVisualState.Present, z2.ArmorVisualStateOf(slot));
                }
                else
                {
                    Assert.AreEqual(ArmorVisualState.Absent, z2.ArmorVisualStateOf(slot));
                }
            }

            Assert.IsNotNull(z2.HitboxFor(BodyRegion.Head));
            Assert.IsTrue(z2.HitboxFor(BodyRegion.Head).enabled, "İkinci yaşamda hitbox kapalı.");
            Assert.AreEqual(default(DamageEventMeta).IsPresent, z2.LastDamageMeta.IsPresent, "İkinci yaşam eski meta'yı taşıdı.");
            Debug.Log("[DD-TEST] wp006.pool life " + eskiLife + "→" + z2.LifeId + " sameBody=" + ReferenceEquals(z, z2) + " bare=" + z2.Armor.IsBare);
        }

        // ============================================================== [#16] [#17] E18 E20: patlama tek uygulama, tek dressing

        [UnityTest]
        [Category("WP006")]
        [Timeout(300000)]
        public IEnumerator Patlama_DusmanBasinaTekUygulama_TekDressing_ButceCogalmaz()
        {
            yield return RunSahnesiniHazirla();
            Vector3 merkez = TestZemini(0f, 8f);
            ZombieEnemy a = Dogur(merkez + new Vector3(-0.9f, 0f, 0f));
            ZombieEnemy b = Dogur(merkez + new Vector3(0.9f, 0f, 0f));
            ZombieEnemy c = Dogur(merkez + new Vector3(0f, 0f, 0.9f));
            yield return null;
            a.FitArmorComposition(true, true, true);
            b.FitArmorComposition(true, false, false);
            c.FitArmorComposition(false, false, false);

            const float toplam = 30f;
            int saturationOnce = HitscanEngine.OverlapSaturationCount;
            using (var sayac = new OlaySayaci())
            {
                HitscanEngine.Explode(merkez + Vector3.up * 0.9f, toplam, 3f);
                yield return null;

                foreach (ZombieEnemy z in new[] { a, b, c })
                {
                    Assert.AreEqual(1, sayac.DamagedFor(z.LifeId), "[#17] düşman " + z.LifeId + " birden fazla / hiç uygulama aldı: " + sayac.DamagedFor(z.LifeId));
                    Assert.AreEqual(1, sayac.DressedByTarget.TryGetValue(z.GetInstanceID(), out int d) ? d : 0, "[#28] düşman başına tek dressing.");
                    DamageEventMeta m = z.LastDamageMeta;
                    Assert.AreEqual(DamageKind.Explosion, m.Kind);
                    Assert.LessOrEqual(m.ArmorAbsorbed + m.HealthRemoved, toplam + 0.001f, "Bütçe parça sayısıyla çoğaldı.");
                    Assert.Greater(m.ArmorAbsorbed + m.HealthRemoved, 0f);
                }

                // Aynı patlama = aynı transaction id, farklı event id.
                int tx = a.LastDamageMeta.TransactionId;
                Assert.Greater(tx, 0);
                Assert.AreEqual(tx, b.LastDamageMeta.TransactionId);
                Assert.AreEqual(tx, c.LastDamageMeta.TransactionId);
                Assert.AreNotEqual(a.LastDamageMeta.EventId, b.LastDamageMeta.EventId);

                // [#14] [#16] tam zırhlı a: üç parça hasar aldı, can dokunulmadı.
                Assert.AreEqual(0f, a.LastDamageMeta.HealthRemoved, "Üç sağlam parça varken cana hasar.");
                Assert.Greater(a.LastDamageMeta.HeadArmorDamage, 0f);
                Assert.Greater(a.LastDamageMeta.UpperArmorDamage, 0f);
                Assert.Greater(a.LastDamageMeta.LowerArmorDamage, 0f);
                Assert.AreEqual(StandardCanR1, a.Health, 0.001f);

                // b: yalnız kask → kafa payı zırha, gövde+bacak payları cana.
                Assert.Greater(b.LastDamageMeta.HeadArmorDamage, 0f);
                Assert.AreEqual(0f, b.LastDamageMeta.HeadHealthDamage);
                Assert.Greater(b.LastDamageMeta.UpperHealthDamage, 0f);
                Assert.Greater(b.LastDamageMeta.LowerHealthDamage, 0f);

                // c: zırhsız → hepsi cana.
                Assert.AreEqual(0f, c.LastDamageMeta.ArmorAbsorbed);
                Assert.Greater(c.LastDamageMeta.HealthRemoved, 0f);

                Assert.AreEqual(0, sayac.Broken.Count, "30 hasarlı patlama hiçbir parçayı kırmamalı (22/38/28).");
                Assert.AreEqual(_dupOnce, ZombieEnemy.DuplicateApplicationCount, "Aynı patlama bir düşmana iki kez ulaştı (guard sayacı).");
                Assert.AreEqual(saturationOnce, HitscanEngine.OverlapSaturationCount, "Alan sorgusu tamponu doldu.");

                // Patlama kritik dressing üretmez (REQ-089): son dressing FleshHit/ArmorHit sınıfında kalır.
                Assert.AreNotEqual(HitFeedbackTier.CriticalHit, sayac.LastTier, "Patlama dressing'i CriticalHit üretti (K-7).");
                Debug.Log("[DD-TEST] wp006.explosion tx=" + tx + " a=" + a.LastDamageMeta.ArmorAbsorbed + "/" + a.LastDamageMeta.HealthRemoved +
                          " b=" + b.LastDamageMeta.ArmorAbsorbed + "/" + b.LastDamageMeta.HealthRemoved +
                          " c=" + c.LastDamageMeta.ArmorAbsorbed + "/" + c.LastDamageMeta.HealthRemoved + " dressed=" + sayac.DressedTotal);
            }
        }

        // ============================================================== E19: patlama dalı + birincil hedef (?U-2 mevcut davranış)

        [UnityTest]
        [Category("WP006")]
        [Timeout(300000)]
        public IEnumerator Patlama_AyriCanYoluYok_BirincilHedefTekOlay()
        {
            yield return RunSahnesiniHazirla();
            Vector3 merkez = TestZemini(0f, 8f);
            ZombieEnemy a = Dogur(merkez);
            ZombieEnemy b = Dogur(merkez + new Vector3(1.2f, 0f, 0f));
            yield return null;
            a.FitArmorComposition(false, true, false);
            b.FitArmorComposition(true, false, false);

            using (var sayac = new OlaySayaci())
            {
                ShotResult atis = Vur(a, BodyRegion.UpperBody, 4f, patlamaSansi: 1f, patlamaYaricapi: 3f, patlamaOlcegi: 1f);
                yield return null;

                Assert.AreEqual(1, sayac.DamagedFor(a.LifeId), "Birincil hedef aynı atışta birden fazla hasar olayı aldı (mermi + patlama).");
                Assert.AreEqual(DamageKind.Direct, a.LastDamageMeta.Kind, "Birincil hedefin olayı DOĞRUDAN yol olmalı (?U-2: patlama payından muaf).");
                Assert.AreEqual(1, sayac.DamagedFor(b.LifeId), "Yakındaki düşman patlamadan tam bir olay almalı.");
                Assert.AreEqual(DamageKind.Explosion, b.LastDamageMeta.Kind);
                Assert.AreEqual(a.LastDamageMeta.TransactionId, b.LastDamageMeta.TransactionId, "Mermi ve patlaması aynı transaction.");
                Assert.AreEqual(2, sayac.DressedTotal, "İki düşman → iki dressing.");
                Assert.GreaterOrEqual(atis.EnemiesHit, 2);
                Assert.AreEqual(_dupOnce, ZombieEnemy.DuplicateApplicationCount);
            }
        }

        // ============================================================== [#22] [#23] [#24] L24 L25: ölü düşman, kill/gold/ledger tek

        [UnityTest]
        [Category("WP006")]
        [Timeout(300000)]
        public IEnumerator OluDusman_GecHasar_YeniOlumGoldKillYok_StandardKill()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy z = Dogur(TestZemini(0f, 7f));
            yield return null;
            z.FitArmorComposition(true, true, true);

            RunDirector run = RunDirector.Instance;
            GoldService gold = GoldService.Instance;
            Assert.IsNotNull(gold);
            int killsOnce = run.Stats.KillsFor(EnemyTier.Standard);
            int totalOnce = run.Stats.TotalKills;
            int armorBreaksOnce = run.Stats.ArmorPiecesBroken;
            int grantedOnce = gold.Ledger.Granted;
            int dupOnce = gold.Ledger.RejectedDuplicate;

            using (var sayac = new OlaySayaci())
            {
                // Kaskı kır, sonra açık kafadan öldür (gerçek silah yolu).
                Vur(z, BodyRegion.Head, 1000f);
                Assert.IsTrue(z.Armor.IsBroken(ArmorSlot.Helmet));
                Assert.AreEqual(1, sayac.Broken.Count);

                int guard = 0;
                while (z.IsAlive && guard++ < 20)
                {
                    Vur(z, BodyRegion.Head, 50f);
                }

                Assert.IsFalse(z.IsAlive, "Zombi ölmedi.");
                Assert.AreEqual(1, sayac.Died.Count, "[#22] EnemyDied bir kez.");
                EnemyDeathInfo olum = sayac.Died[0];
                Assert.AreEqual(EnemyTier.Standard, olum.Tier, "[#23] kill Standard olarak işlenmeli; yeni tür yok.");
                Assert.AreEqual(z.LifeId, olum.LifeId);
                Assert.AreEqual(DamageKind.Direct, olum.Meta.Kind);
                Assert.Greater(olum.TransactionId, 0);
                // WP-008 (DEC-043/069): the DEC-043 hook is now open. A Standard fitted with H+B+L at
                // spawn pays base + the three spawn-armour bonuses, and breaking the helmet before
                // the kill changes nothing (the reward is the spawn snapshot).
                int beklenenOdul = PrototypeConfig.Active.GoldReward(EnemyTier.Standard) +
                                   DD.Prototype.Economy.GoldRewardCalculator.ArmorBonus(new DD.Prototype.Economy.SpawnArmorComposition(true, true, true), PrototypeConfig.Active);
                Assert.AreEqual(beklenenOdul, olum.GoldReward, "Gold = Standard base + spawn zırh bonusu (H+B+L); kırılan kask ödülü azaltmaz (DEC-043/069).");
                Assert.IsTrue(olum.Reward.IsValid);
                Assert.AreEqual(3, olum.Reward.Armor.PieceCount, "Snapshot spawn'daki üç parçayı taşır (kırılma sonrası da).");
                yield return null;

                int dressedAtDeath = sayac.DressedTotal;
                int damagedAtDeath = sayac.Damaged.Count;

                Assert.AreEqual(killsOnce + 1, run.Stats.KillsFor(EnemyTier.Standard), "[#23] Standard kill sayacı +1.");
                Assert.AreEqual(totalOnce + 1, run.Stats.TotalKills);
                Assert.AreEqual(armorBreaksOnce + 1, run.Stats.ArmorPiecesBroken, "Kırılma sayacı tam bir kez.");
                Assert.AreEqual(grantedOnce + 1, gold.Ledger.Granted, "[#24] gold ledger tam bir kez.");

                // [#22] geç hasar: doğrudan ışın (hitbox'lar kapalı → ışın değmez) ve doğrudan çağrı + patlama.
                Vur(z, BodyRegion.UpperBody, 50f);
                Vur(z, BodyRegion.LowerBody, 50f);
                DamageResult gec = z.ApplyHit(new DamageHit { Amount = 100f, Region = BodyRegion.UpperBody, Point = z.transform.position, Direction = Vector3.forward, ImpactForce = 1f, Source = DamageSourceKind.Bullet });
                Assert.IsTrue(DamageResults.IsEmpty(in gec), "Ölü düşman hasar sonucu üretti.");
                HitscanEngine.Explode(z.transform.position + Vector3.up, 500f, 3f);
                yield return null;

                Assert.AreEqual(1, sayac.Died.Count, "[#22] ölü düşmana geç hasar ikinci ölüm üretti.");
                Assert.AreEqual(1, sayac.Broken.Count, "[#22] ölü düşmanda armor-break tekrarlandı.");
                Assert.AreEqual(damagedAtDeath, sayac.Damaged.Count, "Ölü düşman EnemyDamaged yayınladı.");
                Assert.AreEqual(dressedAtDeath, sayac.DressedTotal, "Ölü düşmana geç hasar dressing üretti.");
                Assert.AreEqual(killsOnce + 1, run.Stats.KillsFor(EnemyTier.Standard));
                Assert.AreEqual(armorBreaksOnce + 1, run.Stats.ArmorPiecesBroken);
                Assert.AreEqual(grantedOnce + 1, gold.Ledger.Granted, "[#24] gold ikinci kez verildi.");
                Assert.AreEqual(dupOnce, gold.Ledger.RejectedDuplicate, "Ledger'a ikinci ölüm ulaştı.");
                Debug.Log("[DD-TEST] wp006.dead kills=" + run.Stats.TotalKills + " breaks=" + run.Stats.ArmorPiecesBroken + " granted=" + gold.Ledger.Granted + " died=" + sayac.Died.Count);
            }
        }

        // ============================================================== L25: patlamayla ölüm tek ölüm, KillSource Explosion, kritik değil

        [UnityTest]
        [Category("WP006")]
        [Timeout(300000)]
        public IEnumerator PatlamaylaOlum_TekOlum_KritikDegil()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy z = Dogur(TestZemini(0f, 7f));
            yield return null;
            z.FitArmorComposition(false, false, false);
            RunDirector run = RunDirector.Instance;
            int totalOnce = run.Stats.TotalKills;

            using (var sayac = new OlaySayaci())
            {
                HitscanEngine.Explode(z.transform.position + Vector3.up * 0.9f, 500f, 3f);
                yield return null;
                Assert.IsFalse(z.IsAlive);
                Assert.AreEqual(1, sayac.Died.Count);
                Assert.AreEqual(DamageSourceKind.Explosion, sayac.Died[0].KillSource);
                Assert.AreEqual(DamageKind.Explosion, sayac.Died[0].Meta.Kind);
                Assert.IsFalse(sayac.Died[0].WasCritical, "REQ-089: patlama ölümü kritik olamaz.");
                Assert.AreEqual(1, sayac.DressedTotal);
                Assert.AreEqual(totalOnce + 1, run.Stats.TotalKills);
            }
        }

        // ============================================================== L23: aynı transaction ikinci uygulama reddi

        [UnityTest]
        [Category("WP006")]
        [Timeout(300000)]
        public IEnumerator AyniTransaction_IkinciUygulama_Reddedilir()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy z = Dogur(TestZemini(0f, 7f));
            yield return null;
            z.FitArmorComposition(true, true, true);
            int dupOnce = ZombieEnemy.DuplicateApplicationCount;

            using (var sayac = new OlaySayaci())
            {
                DamageTransaction.Begin();
                try
                {
                    var hit = new DamageHit { Amount = 1000f, Region = BodyRegion.Head, Point = z.transform.position, Direction = Vector3.forward, ImpactForce = 1f, Source = DamageSourceKind.Bullet };
                    DamageResult ilk = z.ApplyHit(in hit);
                    Assert.IsTrue(ilk.ArmorBroke);
                    DamageResult ikinci = z.ApplyHit(in hit);
                    Assert.IsTrue(DamageResults.IsEmpty(in ikinci), "Aynı transaction'da ikinci uygulama kabul edildi.");
                    DamageResult ucuncu = z.ApplyExplosion(500f, z.transform.position);
                    Assert.IsTrue(DamageResults.IsEmpty(in ucuncu));
                }
                finally
                {
                    DamageTransaction.End();
                }

                Assert.AreEqual(dupOnce + 2, ZombieEnemy.DuplicateApplicationCount, "Reddedilen uygulamalar sayılmalı.");
                Assert.AreEqual(1, sayac.Damaged.Count, "İkinci uygulama olay yayınladı.");
                Assert.AreEqual(1, sayac.Broken.Count, "Aynı kırılma iki kez.");
                Assert.IsTrue(z.IsAlive);
                Assert.AreEqual(StandardCanR1, z.Health, 0.001f);

                // Yeni transaction'da yine hasar alır.
                DamageTransaction.Begin();
                try
                {
                    DamageResult yeni = z.ApplyHit(new DamageHit { Amount = 1f, Region = BodyRegion.Head, Point = z.transform.position, Direction = Vector3.forward, ImpactForce = 1f, Source = DamageSourceKind.Bullet });
                    Assert.Greater(yeni.HealthDamage, 0f);
                }
                finally
                {
                    DamageTransaction.End();
                }

                Assert.AreEqual(2, sayac.Damaged.Count);
            }
        }

        // ============================================================== [#26] L27: ikinci sahnede abone sayıları sabit

        [UnityTest]
        [Category("WP006")]
        [Timeout(300000)]
        public IEnumerator IkinciRunSahnesi_HasarAboneleriCogalmaz()
        {
            yield return RunSahnesiniHazirla();
            Dictionary<string, int> run1 = LifecycleTestKit.AboneAnlikGoruntu();
            LifecycleTestKit.Yazdir("wp006.run1", run1);

            yield return RunSahnesiniHazirla();
            Dictionary<string, int> run2 = LifecycleTestKit.AboneAnlikGoruntu();
            LifecycleTestKit.Yazdir("wp006.run2", run2);

            LifecycleTestKit.AboneleriKarsilastir("run2 vs run1 (DamageEvents.* + EnemyHitDressed dahil)", run1, run2);
            Assert.IsTrue(run1.ContainsKey("DamageEvents.EnemyDied") && run1.ContainsKey("HitFeedbackDirector.EnemyHitDressed"),
                "LifecycleTestKit tablosu WP-006 olaylarını izlemiyor (ölçüm yok).");
        }

        // ============================================================== L26: vuruş yolunda allocation

        [UnityTest]
        [Category("WP006")]
        [Timeout(300000)]
        public IEnumerator VurusYolu_SifirAyirma()
        {
            yield return RunSahnesiniHazirla();
            Vector3 merkez = TestZemini(0f, 8f);
            var zombiler = new List<ZombieEnemy>();
            for (int i = 0; i < 4; i++)
            {
                zombiler.Add(Dogur(merkez + new Vector3((i - 1.5f) * 0.9f, 0f, 0f)));
            }
            yield return null;
            foreach (ZombieEnemy z in zombiler)
            {
                z.FitArmorComposition(true, true, true);
                z.Armor.Equip(ArmorSlot.Helmet, 100000f);   // ölçüm sırasında kırılma/ölüm olmasın (test düzeneği)
                z.Armor.Equip(ArmorSlot.Body, 100000f);
                z.Armor.Equip(ArmorSlot.Leg, 100000f);
            }

            // Isınma: ilk vuruşlar tek seferlik önbellekleri (resolver, liste kapasiteleri, sayaç sözlükleri) doldurur;
            // sayaç ısınmadan önce açılır ki pencere içinde kendi listesi/sözlüğü büyümesin.
            var sayac = new OlaySayaci(2048);
            for (int i = 0; i < 20; i++)
            {
                Vur(zombiler[i % 4], BodyRegion.UpperBody, 1f);
                HitscanEngine.Explode(merkez + Vector3.up * 0.9f, 1f, 3f);
            }
            yield return null;

            // Ölçüm yöntemi (WP-005 §35 + test-06 T06-02): iki sayaç birlikte okunur — GC.GetTotalMemory(false)
            // ve Profiler.GetMonoUsedSizeLong(). İki pozitif kontrol kalibre eder: A = tek 64 KB blok (büyük nesne
            // sınıfı), B = 300 küçük nesne (~96 B; vuruş başına kaçak ayırmanın sınıfı). B'yi gören sayaç "küçük
            // ayırmaya duyarlı" sayılır ve pencere onunla değerlendirilir; hiçbiri görmüyorsa yalnız büyük sınıf
            // kanıtlanır ve sonuç gözlem sınıfında loglanır (kanıt boşluğu gizlenmez).
            using (sayac)
            {
                long a0 = GC.GetTotalMemory(false); long m0 = Profiler.GetMonoUsedSizeLong();
                var yemA = new byte[64 * 1024];
                long kontrolA_gc = GC.GetTotalMemory(false) - a0; long kontrolA_mono = Profiler.GetMonoUsedSizeLong() - m0;

                long b0 = GC.GetTotalMemory(false); long n0 = Profiler.GetMonoUsedSizeLong();
                var yemB = new object[300];
                for (int i = 0; i < yemB.Length; i++) yemB[i] = new byte[80];
                long kontrolB_gc = GC.GetTotalMemory(false) - b0; long kontrolB_mono = Profiler.GetMonoUsedSizeLong() - n0;
                bool kucukDuyarliGc = kontrolB_gc > 0, kucukDuyarliMono = kontrolB_mono > 0;
                Assert.Greater(kontrolA_gc + kontrolA_mono, 0, "Hiçbir sayaç 64 KB bloğu görmedi — ölçüm geçersiz (yem " + yemA.Length + "/" + yemB.Length + ").");

                long gen0Once = GC.CollectionCount(0);
                long gOnce = GC.GetTotalMemory(false); long mOnce = Profiler.GetMonoUsedSizeLong();
                int olayOnce = sayac.Damaged.Count;
                for (int i = 0; i < 300; i++)
                {
                    ZombieEnemy z = zombiler[i % 4];
                    Vur(z, (BodyRegion)(i % 3), 1f);
                }
                for (int i = 0; i < 30; i++)
                {
                    HitscanEngine.Explode(merkez + Vector3.up * 0.9f, 1f, 3f);
                }
                long farkGc = GC.GetTotalMemory(false) - gOnce; long farkMono = Profiler.GetMonoUsedSizeLong() - mOnce;
                long gen0 = GC.CollectionCount(0) - gen0Once;
                int olaylar = sayac.Damaged.Count - olayOnce;

                // Pencere gerçekten koştu: 300 doğrudan + 30 patlama × 4 beden = 420 hasar olayı.
                Assert.AreEqual(300 + 30 * 4, olaylar, "Ölçüm penceresinde beklenen hasar olayı sayısı üretilmedi (pencere boş koşmuş olabilir).");

                string sinif = kucukDuyarliMono ? "mono-kucuk-duyarli" : kucukDuyarliGc ? "gc-kucuk-duyarli" : "yalniz-buyuk-sinif-GOZLEM";
                Debug.Log("[DD-OBS] wp006.alloc controlA(64KB) gc=" + kontrolA_gc + " mono=" + kontrolA_mono + " B; controlB(300x80B) gc=" + kontrolB_gc + " mono=" + kontrolB_mono +
                          " B; window(300 direct + 30 explode, " + olaylar + " events) gc=" + farkGc + " mono=" + farkMono + " B gen0=" + gen0 + " class=" + sinif +
                          " dup=" + ZombieEnemy.DuplicateApplicationCount);

                if (kucukDuyarliMono)
                {
                    Assert.LessOrEqual(farkMono, 4 * 1024, "Vuruş/patlama yolu küçük nesne ayırdı (mono " + farkMono + " B / " + olaylar + " olay).");
                }

                if (kucukDuyarliGc)
                {
                    Assert.LessOrEqual(farkGc, 4 * 1024, "Vuruş/patlama yolu küçük nesne ayırdı (gc " + farkGc + " B / " + olaylar + " olay).");
                }

                Assert.LessOrEqual(farkGc, 16 * 1024, "Vuruş/patlama yolu yığını büyüttü (" + farkGc + " B / " + olaylar + " olay).");
                Assert.AreEqual(0, gen0, "Ölçüm penceresinde gen0 GC koştu (ayırma baskısı).");
            }
        }

        // ============================================================== yardımcı

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
