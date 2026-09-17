using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-008 · M2 ekonomi ve round-sonu geliştirme KURALLARI nöbetçisi — gerçek sahne,
    /// gerçek zincir (ZombieEnemy spawn snapshot → Kill → EnemyDied → GoldService → GoldOrb →
    /// GoldWallet; RoundCleared → RunDirector → RoundUpgradeShop → UpgradeScreen → Results),
    /// gerçek kare, gerçek Metal. DEC-043/047/048/049/050 · REQ-018/059/074/078/079.
    ///
    /// Sayılar kilitlenmez: base/versiyon/zırh bonusları ve reroll ücreti config'ten
    /// (PrototypeOnly) okunur. Profil izole (TestProfileIsolation, L1) → Explosion/Chain
    /// kilitli; teklif bu gerçekle koşar.
    /// </summary>
    [TestFixture]
    public sealed class EconomyUpgradeRulesSliceTests
    {
        private const float KurulumZamanAsimi = 40f;
        private const float FazZamanAsimi = 25f;

        private RunDirector _run;
        private EnemySpawnDirector _director;
        private GoldService _gold;
        private PlayerRig _rig;
        private PrototypeConfig _config;

        private readonly List<string> _hatalar = new List<string>();
        private SpecialWeaponSahne _sahne;

        /// <summary>P4 kiti bir assert'te yarım kalırsa girdi/config kopyası geri alınsın.</summary>
        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            if (_sahne != null)
            {
                SpecialWeaponSahne kalan = _sahne;
                _sahne = null;
                yield return kalan.Bitir();
            }
        }

        [SetUp]
        public void SetUp()
        {
            _hatalar.Clear();
            RunFlowClock.ClearHolds();
            UiNavigation.Model.Clear();
            Application.logMessageReceived += HataTopla;
        }


        // WP-049C (DEC-087): sevk artık round başına 2 kart. Bu WP-008 sözleşme testi "limit dolunca kilit"
        // davranışını limit = 1 ile belgeler (config değeri olarak hâlâ geçerli); DEC-087 sevk limiti
        // UpgradePurchaseLimitTests'te, ekranın ikinci seçim akışı S-09 UI paketinde doğrulanır.
        private PrototypeConfig _sevkConfigDec071, _dec071Klon;
        private void Dec071Klonu()
        {
            _sevkConfigDec071 = PrototypeConfig.Active;
            _dec071Klon = UnityEngine.Object.Instantiate(_sevkConfigDec071);
            _dec071Klon.Economy.PurchasesPerRound = 1;
            PrototypeConfig.SetActive(_dec071Klon);
        }
        private void Dec071KlonunuGeriAl()
        {
            if (_sevkConfigDec071 != null && PrototypeConfig.Active != _sevkConfigDec071) PrototypeConfig.SetActive(_sevkConfigDec071);
            if (_dec071Klon != null) UnityEngine.Object.DestroyImmediate(_dec071Klon);
            _sevkConfigDec071 = null; _dec071Klon = null;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= HataTopla;
            Dec071KlonunuGeriAl();
            Time.captureDeltaTime = 0f;
            RunFlowClock.ClearHolds();
            LogAssert.ignoreFailingMessages = false;
            if (EnemySpawnDirector.Instance != null)
            {
                SpawnAyarla(true);
            }

            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            Assert.AreEqual(0, _hatalar.Count, "Test boyunca ekonomi/koşu/UI hatası loglandı:\n" + string.Join("\n", _hatalar));
        }

        private void HataTopla(string mesaj, string iz, LogType tur)
        {
            if (tur != LogType.Error && tur != LogType.Exception && tur != LogType.Assert) return;
            if (mesaj.Contains("[Run]") || mesaj.Contains("[Gold]") || mesaj.Contains("[UI]") ||
                mesaj.Contains("NullReferenceException") || mesaj.Contains("MissingReferenceException") ||
                mesaj.Contains("InvalidOperationException") || mesaj.Contains("ArgumentException"))
            {
                _hatalar.Add(tur + ": " + mesaj);
            }
        }

        // ============================================================== kurulum

        private IEnumerator KosuyaGir(bool kontrollu)
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return Bekle(() => EnemySpawnDirector.Instance != null, KurulumZamanAsimi, "EnemySpawnDirector yok.");
            yield return Bekle(() => GoldService.Instance != null, KurulumZamanAsimi, "GoldService yok.");

            _run = RunDirector.Instance;
            _director = EnemySpawnDirector.Instance;
            _gold = GoldService.Instance;
            _rig = PlayerRegistry.Primary;
            _config = PrototypeConfig.Active;
            Assert.IsNotNull(_config);
            _rig.Health.DiagnosisInvulnerable = true;

            if (kontrollu)
            {
                SpawnAyarla(false);
                _director.ClearAllEnemies();
                EnemyFactory.ClearAll();
                _gold.ClearOrbs(false);
                yield return null;
                Assert.AreEqual(0, ZombieEnemy.Live.Count);
                Time.captureDeltaTime = 1f / 60f;
            }

            Assert.AreEqual(UnityEngine.Rendering.GraphicsDeviceType.Metal, SystemInfo.graphicsDeviceType,
                "WP-008 PlayMode testleri gerçek Metal yürütmesinde koşmalı.");
        }

        private static void SpawnAyarla(bool acik)
        {
            FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f);
            f.SetValue(EnemySpawnDirector.Instance, acik);
        }

        private static bool DebugButonunaBas(string etiketParcasi)
        {
            IReadOnlyList<DebugPanelEntry> girdiler = DebugPanelRegistry.Entries;
            for (int i = 0; i < girdiler.Count; i++)
            {
                if (girdiler[i] is DebugPanelButton buton && buton.Label != null &&
                    buton.Label.Contains(etiketParcasi) && buton.OnPressed != null)
                {
                    buton.OnPressed.Invoke();
                    return true;
                }
            }

            return false;
        }

        private IEnumerator RoundTamamla()
        {
            Assert.IsTrue(DebugButonunaBas("Complete this round"), "F1 'Complete this round' butonu yok.");
            yield return Bekle(() => _run.Phase == RunPhase.Upgrading, FazZamanAsimi, "Upgrade ekranı açılmadı.");
            yield return null;
            yield return null;
        }

        private static UiButton Buton(string ad)
        {
            UpgradeScreen ekran = UnityEngine.Object.FindObjectOfType<UpgradeScreen>();
            Assert.IsNotNull(ekran, "UpgradeScreen sahnede yok.");
            UiButton[] butonlar = ekran.GetComponentsInChildren<UiButton>(true);
            for (int i = 0; i < butonlar.Length; i++)
            {
                if (butonlar[i].gameObject.name == ad) return butonlar[i];
            }

            Assert.Fail("UpgradeScreen içinde '" + ad + "' butonu yok.");
            return null;
        }

        private static bool MetinVar(string parca)
        {
            UpgradeScreen ekran = UnityEngine.Object.FindObjectOfType<UpgradeScreen>();
            if (ekran == null) return false;
            TMP_Text[] metinler = ekran.GetComponentsInChildren<TMP_Text>(false);
            for (int i = 0; i < metinler.Length; i++)
            {
                if (metinler[i].text != null && metinler[i].text.Contains(parca)) return true;
            }

            return false;
        }

        /// <summary>Pozitif kontrol: yığın sayacı bilerek ayıran döngüde yükselmeli (aksi hâlde ölçüm geçersiz).</summary>
        private static IEnumerator SayacPozitifKontrol()
        {
            long once = GC.GetTotalMemory(false);
            var yem = new object[4000];
            for (int i = 0; i < yem.Length; i++) yem[i] = new byte[64];
            long fark = GC.GetTotalMemory(false) - once;
            Debug.Log("[DD-TEST] wp008.perf positiveControl allocB=" + fark + " keep=" + yem.Length);
            Assert.Greater(fark, 0, "yığın sayacı bilerek ayıran döngüde yükselmedi — ölçüm geçersiz");
            yield return null;
        }

        /// <summary>N karede yığın büyümesi; pencerede gen0 GC koşarsa bir kez daha dener, yine koşarsa −1.</summary>
        private static IEnumerator AyirmaOlc(int kare, Action<long> sonuc)
        {
            for (int deneme = 0; deneme < 2; deneme++)
            {
                for (int i = 0; i < 10; i++) yield return null;
                long c0 = GC.CollectionCount(0);
                long once = GC.GetTotalMemory(false);
                for (int i = 0; i < kare; i++) yield return null;
                long fark = GC.GetTotalMemory(false) - once;
                if (GC.CollectionCount(0) == c0)
                {
                    sonuc(fark);
                    yield break;
                }

                Debug.Log("[DD-TEST] wp008.perf window gc-interrupted, retrying");
            }

            sonuc(-1);
        }

        private static List<UpgradeBranch> Dallar(RoundUpgradeShop shop)
        {
            var list = new List<UpgradeBranch>();
            for (int i = 0; i < shop.Cards.Count; i++) list.Add(shop.Cards[i].Branch);
            return list;
        }

        private static GoldOrb TekPaket()
        {
            GoldOrb[] hepsi = UnityEngine.Object.FindObjectsOfType<GoldOrb>(false);
            GoldOrb bulunan = null;
            int aktif = 0;
            for (int i = 0; i < hepsi.Length; i++)
            {
                if (hepsi[i].IsSpawned) { aktif++; bulunan = hepsi[i]; }
            }

            Assert.AreEqual(1, aktif, "Tam bir aktif paket beklenirken " + aktif + " bulundu.");
            return bulunan;
        }

        // ============================================================== P1 · spawn snapshot, armour break, one packet, one credit

        [UnityTest]
        [Category("WP008")]
        [Timeout(180000)]
        public IEnumerator P1_ZirhliDusman_SpawnSnapshot_KirilmaOduluDegistirmez_TekPaket_TekKredi()
        {
            yield return KosuyaGir(kontrollu: true);
            int baseOdul = GoldRewardCalculator.BaseReward(_config);
            int tamZirh = GoldRewardCalculator.ArmorBonus(new SpawnArmorComposition(true, true, true), _config);
            Assert.Greater(tamZirh, 0, "Test öncülü: zırh bonusları pozitif (PrototypeOnly).");

            Vector3 konum = _rig.transform.position + new Vector3(12f, 0f, 0f);
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, _run.Round, konum, Quaternion.identity);
            Assert.IsNotNull(z);
            Assert.AreEqual(3, z.FitArmorComposition(true, true, true));
            EnemyRewardSnapshot spawnAni = z.Reward;
            Assert.IsTrue(spawnAni.IsValid);
            Assert.AreEqual(baseOdul, spawnAni.Base);
            Assert.AreEqual(0, spawnAni.VersionBonus, "Standard versiyon bonusu 0.");
            Assert.AreEqual(tamZirh, spawnAni.ArmorBonus);
            Assert.AreEqual(baseOdul + tamZirh, spawnAni.Total);
            Assert.AreEqual(3, spawnAni.Armor.PieceCount);

            // Dövüş sırasında bütün plakalar kırılır: snapshot değişmez.
            Assert.AreEqual(3, z.BreakAllArmor());
            Assert.AreEqual(0, z.Armor.ProtectingCount, "Test öncülü: zırh gerçekten kırıldı.");
            Assert.AreEqual(spawnAni.Total, z.Reward.Total, "Zırh kaybı ödülü azaltmaz (karar 6).");
            Assert.AreEqual(spawnAni.ArmorBonus, z.Reward.ArmorBonus);

            int lifeId = z.LifeId;
            int hakEdis = _gold.Ledger.Granted;
            int paketOnce = _gold.PacketsSpawned;
            z.Kill();
            yield return null;
            GoldOrb paket = TekPaket();
            Assert.AreEqual(paketOnce + 1, _gold.PacketsSpawned, "Tek paket.");
            Assert.AreEqual(hakEdis + 1, _gold.Ledger.Granted);
            Assert.AreEqual(spawnAni.Total, paket.Amount, "Paket spawn snapshot'ının toplamını taşır (kırılmış zırha rağmen).");
            Assert.AreEqual(spawnAni.Total, _gold.LastRewardValue.Total);
            Assert.AreEqual(tamZirh, _gold.LastRewardValue.ArmorContribution);
            Assert.AreEqual(baseOdul, _gold.LastRewardValue.Base);

            // Aynı gövdeye ikinci ölüm çağrısı (replay): olay yok, paket yok.
            z.Kill();
            yield return null;
            Assert.AreEqual(paketOnce + 1, _gold.PacketsSpawned, "Ölü hedefe ikinci Kill ikinci paket üretmez.");
            Assert.AreEqual(hakEdis + 1, _gold.Ledger.Granted);
            Assert.IsTrue(_gold.Ledger.IsClaimed(lifeId));

            // Versiyon bonusu gerçek spawn'da: Elite/Ultra snapshot'ı base + versiyon + rulet zırhı.
            foreach (EnemyTier tier in new[] { EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra })
            {
                ZombieEnemy v = EnemyFactory.Spawn(tier, _run.Round, _rig.transform.position + new Vector3(-12f, 0f, 4f), Quaternion.identity);
                Assert.IsNotNull(v, tier + " doğmadı.");
                EnemyRewardSnapshot vs = v.Reward;
                Assert.AreEqual(tier, vs.Tier);
                Assert.AreEqual(baseOdul, vs.Base);
                Assert.AreEqual(GoldRewardCalculator.VersionBonus(tier, _config), vs.VersionBonus);
                Assert.AreEqual(GoldRewardCalculator.ArmorBonus(vs.Armor, _config), vs.ArmorBonus);
                Assert.AreEqual(_config.GoldReward(tier) + vs.ArmorBonus, vs.Total, tier + ": zırhsız config değeri + rulet zırh bonusu");
                v.Remove();
                yield return null;
            }

            // Toplama: paket tam bir kez, tam snapshot değeriyle kredi olur.
            int krediler = 0; int krediTutari = 0;
            Action<int, int, Vector3> kredi = (a, b, p) => { krediler++; krediTutari += a; };
            _gold.Wallet.Credited += kredi;
            int bakiyeOnce = _gold.Wallet.Balance;
            Vector3 yakin = paket.transform.position + new Vector3(-(_gold.MagnetRadius - 1.5f), 0f, 0f);
            yakin.y = _rig.transform.position.y;
            _rig.Teleport(yakin, Quaternion.identity);
            yield return Bekle(() => krediler > 0, 12f, "Magnet kredisi gelmedi.");
            for (int i = 0; i < 30; i++) yield return null;
            _gold.Wallet.Credited -= kredi;
            Assert.AreEqual(1, krediler);
            Assert.AreEqual(spawnAni.Total, krediTutari);
            Assert.AreEqual(bakiyeOnce + spawnAni.Total, _gold.Wallet.Balance);
        }

        // ============================================================== P2 · the round-end rules on the real screen

        [UnityTest]
        [Category("WP008")]
        [Timeout(300000)]
        public IEnumerator P2_RoundSonuEkrani_UcKart_PahaliDisabled_TekReroll_IkiSatinAlma_Kilit_Reopen_Continue()
        {
            yield return KosuyaGir(kontrollu: true);   // WP-049C/022L: sevk politikası (DEC-087, 2 kart) — klon yok, shop.PurchaseLimit okunur
            UpgradeScreen ekran = UnityEngine.Object.FindObjectOfType<UpgradeScreen>();
            Assert.IsNotNull(ekran);

            // Birkaç kill, sonra F1 ile round tamamla → ekran.
            Vector3 merkez = _rig.transform.position;
            for (int i = 0; i < 4; i++)
            {
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, _run.Round, merkez + new Vector3(14f + i * 1.2f, 0f, 3f), Quaternion.identity);
                Assert.IsNotNull(z);
                z.Kill();
                yield return null;
            }

            yield return RoundTamamla();
            RoundUpgradeShop shop = _run.Shop;
            Assert.IsTrue(shop.IsOpen);
            Assert.AreEqual(UpgradeOfferStatus.Ready, shop.Status);
            Assert.AreEqual(UpgradeOfferPolicy.CardsPerOffer, shop.Cards.Count, "Gerçek ekranda üç kart.");
            Assert.AreEqual(UiContext.RoundUpgrade, UiNavigation.Layer);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(Buton("Card." + i).gameObject.activeInHierarchy, "Kart " + i + " görünür.");
                for (int j = i + 1; j < 3; j++) Assert.AreNotEqual(shop.Cards[i].Branch, shop.Cards[j].Branch, "Kartlar farklı.");
                Assert.AreNotEqual(UpgradeBranch.Chain, shop.Cards[i].Branch, "L1 profil: kilitli dal teklif edilmez.");
                Assert.AreNotEqual(UpgradeBranch.Explosion, shop.Cards[i].Branch);
            }

            Assert.IsTrue(Buton("Reroll").gameObject.activeInHierarchy, "Reroll düğmesi var.");
            Assert.IsTrue(Buton("Continue").gameObject.activeInHierarchy);
            Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Ekran donuk dünyada.");

            // Pahalı kart: görünür, düğme devre dışı, "TOO EXPENSIVE".
            _gold.Wallet.DebugSet(0);
            yield return null;
            yield return null;
            for (int i = 0; i < 3; i++)
            {
                UiButton kartButonu = Buton("Card." + i);
                Assert.IsTrue(kartButonu.gameObject.activeInHierarchy, "Pahalı kart havuzdan çıkmaz, görünür kalır.");
                Assert.IsFalse(kartButonu.IsInteractable(), "Pahalı kartın satın alma düğmesi devre dışı.");
                Assert.IsFalse(shop.CanAfford(i));
            }

            Assert.IsTrue(MetinVar("TOO EXPENSIVE"), "Pahalı kart durumu ekranda yazılı.");
            Assert.IsFalse(Buton("Reroll").IsInteractable(), "0 gold: reroll devre dışı.");
            Assert.IsTrue(MetinVar("REROLL · NEED " + shop.RerollCost + " GOLD"), "Reroll etiketi fiyatı ve yetersiz gold durumunu yazar.");
            Assert.IsTrue(Buton("Continue").IsInteractable(), "Continue her zaman açık.");

            // Devre dışı düğmeye basmak hiçbir şey yapmaz.
            int bakiye0 = _gold.Wallet.Balance;
            Buton("Card.0").OnSubmit(null);
            Buton("Reroll").OnSubmit(null);
            yield return null;
            Assert.AreEqual(bakiye0, _gold.Wallet.Balance);
            Assert.AreEqual(UpgradeEntitlement.Available, shop.PurchaseEntitlement);
            Assert.AreEqual(UpgradeEntitlement.Available, shop.RerollEntitlement);

            // Yeterli gold: reroll bir kez ekran düğmesiyle.
            int enPahali = 0;
            for (int i = 0; i < 3; i++) enPahali = Mathf.Max(enPahali, shop.Cards[i].Cost);
            int hedefBakiye = enPahali * 2 + shop.RerollCost * 2;
            Assert.IsTrue(_gold.Wallet.Credit(hedefBakiye, _rig.transform.position));
            yield return null;
            yield return null;
            Assert.IsTrue(Buton("Reroll").IsInteractable(), "Yeterli gold: reroll açık.");
            Assert.IsTrue(MetinVar("REROLL · " + shop.RerollCost + " GOLD · ONCE"), "Reroll etiketi fiyatı ve tek hakkı yazar.");
            List<UpgradeBranch> once = Dallar(shop);
            int bakiyeReroll = _gold.Wallet.Balance;
            int rerollUcreti = shop.RerollCost;
            int rerollOlayi = 0;
            shop.Rerolled += c => rerollOlayi++;

            int satinAlmaOnce = 0;
            shop.Purchased += c => satinAlmaOnce++;
            int bakiyeAyniKare;
            Buton("Reroll").OnSubmit(null);
            Buton("Reroll").OnSubmit(null);   // çift tıklama aynı karede
            bakiyeAyniKare = _gold.Wallet.Balance;
            Buton("Card.0").OnSubmit(null);   // aynı karede görülmemiş yeni karta satın alma: reddedilir
            Buton("Card.1").OnSubmit(null);
            Assert.AreEqual(0, satinAlmaOnce, "Reroll ile aynı karede satın alma yok (yeni kartlar henüz görülmedi).");
            Assert.AreEqual(bakiyeAyniKare, _gold.Wallet.Balance, "Aynı kare satın alma denemesi harcamadı.");
            Assert.AreEqual(UpgradeEntitlement.Available, shop.PurchaseEntitlement);
            yield return null;
            yield return null;

            Assert.AreEqual(1, rerollOlayi, "Çift tıklama tek reroll.");
            List<UpgradeBranch> sonra = Dallar(shop);
            int fark = 0;
            for (int i = 0; i < 3; i++) if (once[i] != sonra[i]) fark++;
            Assert.That(fark, Is.InRange(1, 2), "Reroll bir veya iki kartı değiştirir.");
            Assert.AreEqual(3, sonra.Count);
            Assert.AreEqual(bakiyeReroll - rerollUcreti, _gold.Wallet.Balance, "Reroll ücreti tam bir kez.");
            Assert.AreEqual(UpgradeEntitlement.Used, shop.RerollEntitlement);
            Assert.IsFalse(Buton("Reroll").IsInteractable(), "Reroll kullanıldı: düğme kapalı.");
            Assert.IsTrue(MetinVar("REROLL · USED THIS ROUND"), "Reroll hakkının kullanıldığı ekranda yazılı.");
            Assert.AreEqual(rerollUcreti, _run.Stats.GoldSpentOnRerolls);
            Assert.AreEqual(1, _run.Stats.RerollsUsed);

            // Pause/timeScale: ekran zaten timeScale 0'da; gerçek pause ekranı üstte açılır,
            // shop etkileşimsiz kalır, işlem üretilmez; resume hakları geri vermez.
            PauseScreen pause = UnityEngine.Object.FindObjectOfType<PauseScreen>();
            Assert.IsNotNull(pause, "PauseScreen sahnede yok.");
            pause.SetPaused(true);
            yield return null;
            yield return null;
            Assert.IsTrue(UiNavigation.IsPaused, "Pause overlay açık.");
            Assert.IsTrue(pause.IsPaused);
            int bakiyePause = _gold.Wallet.Balance;
            Assert.IsFalse(Buton("Card.0").IsInteractable(), "Pause altında kartlar etkileşimsiz.");
            Buton("Card.0").OnSubmit(null);
            yield return null;
            pause.SetPaused(false);
            yield return null;
            yield return null;
            Assert.IsFalse(UiNavigation.IsPaused);
            Assert.AreEqual(bakiyePause, _gold.Wallet.Balance, "Pause altında satın alma işlemi üretilmedi (ekran etkileşimsiz).");
            Assert.AreEqual(UpgradeEntitlement.Available, shop.PurchaseEntitlement);
            Assert.AreEqual(UpgradeEntitlement.Used, shop.RerollEntitlement, "Pause/resume reroll hakkını geri vermez.");
            Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Round sonu donması sürüyor.");

            // DEC-087 (WP-049C/022L): round başına shop.PurchaseLimit (sevk 2) kart. İlk satın alma ekran düğmesiyle;
            // aynı kartta çift tıklama tek işlem (ikinci kart aynı karede ayrı, meşru bir seçimdir — aşağıda).
            WeaponController silah = WeaponRegistry.ActiveController;
            int limit = shop.PurchaseLimit;
            Assert.AreEqual(UpgradeOfferPolicy.PurchaseLimit(PrototypeConfig.Active), limit, "dükkân limiti config'den (DEC-087 sevk 2)");
            Assert.AreEqual(2, limit, "DEC-087: sevk politikası iki kart");
            UpgradeCardOffer kart = shop.Cards[0];
            int tierOnce = silah.Modifiers.GetTier(kart.Branch);
            int bakiyeOnce = _gold.Wallet.Balance;
            int satinAlmaOlayi = 0;
            shop.Purchased += c => satinAlmaOlayi++;
            Assert.IsTrue(Buton("Card.0").IsInteractable());
            Buton("Card.0").OnSubmit(null);
            Buton("Card.0").OnSubmit(null);   // çift tıklama
            yield return null;
            yield return null;

            Assert.AreEqual(1, satinAlmaOlayi, "Aynı kartta çift tıklama tek transaction.");
            Assert.AreEqual(bakiyeOnce - kart.Cost, _gold.Wallet.Balance);
            Assert.AreEqual(tierOnce + 1, silah.Modifiers.GetTier(kart.Branch));
            Assert.IsTrue(shop.Cards[0].Purchased);
            Assert.IsFalse(shop.Cards[1].Purchased);
            Assert.IsFalse(shop.IsLocked, "Limit 2: ilk alımdan sonra teklif açık kalır.");
            Assert.AreEqual(1, shop.PurchasesThisRound);
            Assert.AreEqual(UpgradeEntitlement.Used, shop.RerollEntitlement, "DEC-050: reroll bu round'da zaten kullanıldı; satın alma onu geri vermez.");
            Assert.IsFalse(shop.CanReroll, "İlk satın alma sonrası reroll yok (kullanılmış olsa da olmasa da kapalı).");
            Assert.IsFalse(Buton("Reroll").IsInteractable());
            Assert.IsTrue(MetinVar("PURCHASED"));
            Assert.IsFalse(Buton("Card.0").IsInteractable(), "Alınan kart tekrar alınamaz.");
            Assert.IsTrue(Buton("Card.1").IsInteractable() || Buton("Card.2").IsInteractable(), "İkinci seçim açık: en az bir kart alınabilir (limit 2).");
            Assert.IsTrue(MetinVar("Choose another upgrade, or continue."), "Gold şeridi ikinci seçimi söyler (WP-022L).");

            // İkinci kart: alınabilir olan ilk kart; limit dolar → teklif kilitlenir, yalnız Continue.
            int ikinciIndeks = -1;
            for (int i = 1; i < 3; i++) if (shop.IsPurchasable(i) && Buton("Card." + i).IsInteractable()) { ikinciIndeks = i; break; }
            Assert.GreaterOrEqual(ikinciIndeks, 0, "Alınabilir ikinci kart yok (bakiye/maks tier?).");
            UpgradeCardOffer kart2 = shop.Cards[ikinciIndeks];
            int tier2Once = silah.Modifiers.GetTier(kart2.Branch);
            int bakiye2Once = _gold.Wallet.Balance;
            Buton("Card." + ikinciIndeks).OnSubmit(null);
            Buton("Card." + ikinciIndeks).OnSubmit(null);   // çift tıklama
            yield return null;
            yield return null;
            Assert.AreEqual(2, satinAlmaOlayi, "İkinci kart tek transaction.");
            Assert.AreEqual(bakiye2Once - kart2.Cost, _gold.Wallet.Balance);
            Assert.AreEqual(tier2Once + 1, silah.Modifiers.GetTier(kart2.Branch));
            Assert.AreEqual(2, shop.PurchasesThisRound);
            Assert.IsTrue(shop.IsLocked, "Limit doldu: teklif kilitli.");
            Assert.IsTrue(shop.Cards[ikinciIndeks].Purchased);
            for (int i = 0; i < 3; i++) Assert.IsFalse(Buton("Card." + i).IsInteractable(), "Limit sonrası bütün kartlar kilitli.");
            Assert.IsTrue(MetinVar("OFFER LOCKED"), "Kilitli teklif kart chip'inde yazılı.");
            Assert.IsTrue(MetinVar("REROLL · LOCKED"), "Reroll etiketi kilidi yazar.");
            Assert.IsTrue(MetinVar("Upgrades applied. Continue when ready."), "Gold şeridi notu iki alımı ve kilidi söyler (WP-022L).");
            Assert.IsFalse(MetinVar("None of these is affordable"), "Kilitli teklif 'karşılanamıyor' diye okunmaz.");
            Assert.IsFalse(Buton("Reroll").IsInteractable());
            Assert.IsTrue(Buton("Continue").IsInteractable());
            GameObject odak = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            Assert.IsNotNull(odak, "Kilit sonrası odak yaşayan bir kontrolde.");
            Assert.AreEqual("Continue", odak.name, "Kilit sonrası odak CONTINUE'da.");
            int alimToplam = kart.Cost + kart2.Cost;
            Assert.AreEqual(alimToplam + rerollUcreti, _gold.Wallet.TotalSpent, "Cüzdan: iki satın alma + reroll.");
            Assert.AreEqual(alimToplam + rerollUcreti, _run.Stats.GoldSpent, "Tracker: iki satın alma + reroll.");

            // GC penceresi (WP-008 kuralı "her kare string/arama/allocation yok", kilitli teklif + reroll bloğu):
            // yönetilen yığın deltası 600 kare ekran açık − 600 kare bileşen kapalı, pozitif kontrol ile
            // (WP-005/007 ölçüm kalıbı; GetAllocatedBytesForCurrentThread Mono'da 0 döndüğü için yığın deltası).
            yield return SayacPozitifKontrol();
            long acik = 0;
            yield return AyirmaOlc(600, v => acik = v);
            ekran.enabled = false;
            long kapali = 0;
            yield return AyirmaOlc(600, v => kapali = v);
            ekran.enabled = true;
            yield return null;
            yield return Bekle(() => UiNavigation.Layer == UiContext.RoundUpgrade, 5f, "Ekran yeniden açılmadı.");
            yield return null;
            long gcFarki = acik - kapali;
            Debug.Log("[DD-TEST] wp008.perf lockedIdle600 heapB=" + acik + " screenOff600 heapB=" + kapali + " delta600=" + gcFarki +
                      " (B/frame≈" + (gcFarki / 600f).ToString("0.0") + ")");
            if (acik >= 0 && kapali >= 0)
            {
                Assert.LessOrEqual(gcFarki, 128 * 1024, "Kilitli upgrade ekranı 600 idle karede yığını 128 KB'den fazla büyüttü (kare başı biçimleme/arama?).");
            }

            Assert.IsTrue(shop.IsLocked, "GC penceresi kilidi değiştirmedi.");
            Assert.AreEqual(2, satinAlmaOlayi);

            // UI yeniden bağlanma: ekran kapatılıp aynı round için yeniden açılır → aynı teklif, aynı kilit.
            int oturum = shop.SessionId; int revizyon = shop.Revision; List<UpgradeBranch> kilitliKartlar = Dallar(shop);
            ekran.gameObject.SetActive(false);   // OnDisable: Unbind + ExitLayer
            yield return null;
            ekran.gameObject.SetActive(true);
            yield return null;
            yield return null;
            Assert.AreEqual(oturum, shop.SessionId, "Yeniden bağlanma yeni teklif üretmez.");
            Assert.AreEqual(revizyon, shop.Revision);
            CollectionAssert.AreEqual(kilitliKartlar, Dallar(shop));
            Assert.IsTrue(shop.IsLocked, "Yeniden bağlanma hakkı yenilemez.");
            Assert.IsTrue(shop.IsOpen);
            for (int i = 0; i < 3; i++) Assert.IsFalse(Buton("Card." + i).IsInteractable());
            Assert.IsFalse(Buton("Reroll").IsInteractable());
            int bakiyeReopen = _gold.Wallet.Balance;
            Buton("Card.1").OnSubmit(null);
            yield return null;
            Assert.AreEqual(bakiyeReopen, _gold.Wallet.Balance);
            Assert.AreEqual(2, satinAlmaOlayi);

            // Continue → round 2: gold ve build korunur; dünya çözülür.
            int bakiyeR1 = _gold.Wallet.Balance; string buildR1 = _run.Build.DescribeBuild(); int spentR1 = _gold.Wallet.TotalSpent;
            Buton("Continue").OnSubmit(null);
            yield return null;
            Assert.AreEqual(UiContext.None, UiNavigation.Layer, "Ekran kapandı.");
            yield return Bekle(() => _run.Phase == RunPhase.Fighting && _run.Round == 2, FazZamanAsimi, "Round 2 başlamadı.");
            yield return SimulasyonSerbestOlmali("wp008.r2");
            Assert.AreEqual(bakiyeR1, _gold.Wallet.Balance);
            Assert.AreEqual(buildR1, _run.Build.DescribeBuild());
            Assert.AreEqual(spentR1, _gold.Wallet.TotalSpent);
            Assert.IsFalse(shop.IsOpen);

            // Round 2 ekranı: taze haklar (yeni round), önceki oturum reddedilir.
            SpawnAyarla(false);
            _director.ClearAllEnemies();
            yield return null;
            yield return RoundTamamla();
            Assert.AreNotEqual(oturum, shop.SessionId, "Yeni round: yeni teklif kimliği.");
            Assert.AreEqual(UpgradeEntitlement.Available, shop.PurchaseEntitlement);
            Assert.AreEqual(UpgradeEntitlement.Available, shop.RerollEntitlement);
            Assert.IsFalse(shop.TryPurchase(0, oturum, revizyon), "Önceki round'un komutu yeni teklife satın alamaz.");
            Assert.AreEqual(UpgradePurchaseResult.SessionMismatch, shop.LastResult);
            Assert.AreEqual(3, shop.Cards.Count);

            // Ölüm → Results: Gold Spent = satın alma + reroll (tek satır), profil XP yalnız özetten.
            int oncekiRunId = _run.RunId;
            RunSummary ozet = default; int bitti = 0;
            Action<RunSummary> bittiDinle = s => { ozet = s; bitti++; };
            RunEvents.RunEnded += bittiDinle;
            int xpOnce = ProfileService.Current.TotalXpEarned;
            _run.CloseUpgradeScreen();
            yield return Bekle(() => _run.Phase == RunPhase.Fighting && _run.Round == 3, FazZamanAsimi, "Round 3 başlamadı.");
            _rig.Health.DiagnosisInvulnerable = false;
            _rig.Health.ApplyDamage(_rig.Health.Max * 10f, _rig.transform.position + Vector3.forward, "wp008-test");
            yield return Bekle(() => RunDirector.Instance == null || RunDirector.Instance.Phase == RunPhase.Dead, FazZamanAsimi, "Dead gelmedi.");
            RunEvents.RunEnded -= bittiDinle;
            Assert.AreEqual(1, bitti);
            Assert.AreEqual(alimToplam + rerollUcreti, ozet.GoldSpent, "Results Gold Spent = iki satın alma + reroll.");
            Assert.AreEqual(alimToplam, ozet.GoldSpentOnUpgrades);
            Assert.AreEqual(rerollUcreti, ozet.GoldSpentOnRerolls);
            Assert.AreEqual(2, ozet.UpgradesPurchased, "DEC-087: iki kart");
            Assert.AreEqual(1, ozet.RerollsUsed);
            Assert.AreEqual(bakiyeR1, ozet.GoldUnspent);
            Assert.AreEqual(buildR1, ozet.BuildDescription);

            yield return SahneyiBekle(PrototypeScenes.Results);
            yield return null;
            yield return null;
            ResultsScreen results = UnityEngine.Object.FindObjectOfType<ResultsScreen>();
            Assert.IsNotNull(results);
            bool goldSpentSatiri = false; bool rerollSatiri = false;
            foreach (TMP_Text t in results.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.text == "Gold Spent") goldSpentSatiri = true;
                if (t.text != null && t.text.Contains("Reroll")) rerollSatiri = true;
            }

            Assert.IsTrue(goldSpentSatiri, "Results'ta 'Gold Spent' satırı var.");
            Assert.IsFalse(rerollSatiri, "Results'ta ayrı reroll satırı yok (karar 20).");
            Assert.AreEqual(xpOnce + ozet.TotalXpEarned, ProfileService.Current.TotalXpEarned, "Profil XP yalnız özetin XP'si kadar arttı; gold katkısı 0.");

            // Yeni koşu: gold 0, build stock, haklar taze.
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return Bekle(() => GoldService.Instance != null && GoldService.Instance != _gold, KurulumZamanAsimi, "Yeni GoldService yok.");
            RunDirector run2 = RunDirector.Instance;
            Assert.AreEqual(0, GoldService.Instance.Wallet.Balance);
            Assert.AreEqual(0, GoldService.Instance.Wallet.TotalSpent);
            Assert.AreEqual("Stock", run2.Build.DescribeBuild());
            Assert.AreEqual(0, WeaponRegistry.ActiveController.Modifiers.TotalTiers);
            Assert.IsFalse(run2.Shop.IsOpen);
            Assert.AreEqual(0, run2.Shop.PurchasesThisRound);
            Assert.Greater(run2.RunId, oncekiRunId, "Yeni koşu yeni kimlik.");
        }

        // ============================================================== P4 · Special Weapon active: purchase and reroll leave it untouched (REQ-059)

        [UnityTest]
        [Category("WP008")]
        [Timeout(180000)]
        public IEnumerator P4_SpecialWeaponAktifken_SatinAlmaVeReroll_SpecialiEtkilemez_KlasikBuildKorunur()
        {
            var sahne = new SpecialWeaponSahne();
            _sahne = sahne;
            yield return sahne.Kur();
            sahne.Config.Economy.PurchasesPerRound = 1;   // WP-049C: limit = 1 sözleşmesi (sahnenin kendi klonu üzerinde; Bitir() sevki geri koyar)
            {
                RunDirector run = RunDirector.Instance;
                GoldService gold = GoldService.Instance;
                Assert.IsNotNull(gold);
                WeaponController controller = sahne.Controller;
                ClassicWeaponRuntime klasik = controller.Classic;
                string buildOnce = controller.Modifiers.DescribeBuild();

                // Special Weapon eline geçer (WP-007 kiti: pencere 0.5, deterministik).
                yield return sahne.SpawnEt(0.5f);
                sahne.PickupaGit(sahne.CanliPickup());
                yield return SpecialWeaponSahne.Bekle(() => controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");
                yield return null;
                Assert.IsTrue(WeaponEvents.Classic.TemporarilyDisabled, "Klasik park edildi.");
                SpecialWeaponRuntime special = controller.Special;
                Assert.IsTrue(special.IsActive);
                float kalanOnce = special.Remaining;

                // Special aktifken teklif açılır (F1 yolu), reroll + satın alma yapılır.
                Assert.IsTrue(gold.Wallet.Credit(500, sahne.Rig.transform.position));
                run.OpenUpgradeScreen();
                yield return null;
                RoundUpgradeShop shop = run.Shop;
                Assert.IsTrue(shop.IsOpen);
                Assert.AreEqual(UpgradeOfferStatus.Ready, shop.Status);
                Assert.IsTrue(shop.TryReroll(shop.SessionId, shop.Revision), "Reroll: " + shop.LastRerollResult);
                int kartIndeksi = 0;
                UpgradeCardOffer kart = shop.Cards[kartIndeksi];
                Assert.IsTrue(shop.TryPurchase(kartIndeksi, shop.SessionId, shop.Revision), "Satın alma: " + shop.LastResult);
                yield return null;

                Assert.IsTrue(controller.SpecialWeaponActive, "Satın alma/reroll Special'ı kapatmadı.");
                Assert.AreSame(special, controller.Special, "Special runtime örneği değişmedi.");
                Assert.AreEqual(1, controller.Modifiers.GetTier(kart.Branch), "Klasik build'e tier uygulandı.");
                Assert.AreEqual(1, controller.Modifiers.TotalTiers, "Tam bir tier (DescribeBuild yalnız silah-çekirdek dallarını yazar; oyuncu dalı 'Stock' kalabilir).");
                Assert.AreEqual("Stock", buildOnce);
                Assert.AreSame(klasik, controller.Classic, "Klasik runtime park hâlinde aynı örnek (REQ-057).");
                Assert.IsTrue(WeaponEvents.Classic.TemporarilyDisabled, "Klasik hâlâ park hâlinde.");
                Assert.AreEqual(SpecialWeaponPhase.Active, WeaponEvents.Special.Phase, "HUD Special fazı Active kaldı.");
                string buildSonra = controller.Modifiers.DescribeBuild();

                // Continue → Special süresi dolar → klasik yeni build ile geri gelir.
                run.CloseUpgradeScreen();
                yield return null;
                float adim = Mathf.Max(0.01f, sahne.Special.ActiveDuration / 60f);
                Time.captureDeltaTime = adim;
                yield return SpecialWeaponSahne.Bekle(() => !controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi + sahne.Special.ActiveDuration, "Special süresi bitmedi.");
                yield return SpecialWeaponSahne.Kareler(12);
                Time.captureDeltaTime = 0f;

                Assert.IsFalse(WeaponEvents.Classic.TemporarilyDisabled, "Klasik geri dönmedi.");
                Assert.AreSame(klasik, controller.Classic);
                Assert.AreEqual(buildSonra, controller.Modifiers.DescribeBuild(), "Klasik dönüşte satın alınan build ile geldi.");
                Assert.AreEqual(1, controller.Modifiers.GetTier(kart.Branch), "Dönüşte tier korunur.");
                Assert.AreEqual(1, controller.Modifiers.TotalTiers);
                Assert.AreEqual(1, run.Build.PurchaseCount);
                Assert.Greater(kalanOnce, 0f);
            }

            yield return sahne.Bitir();
            _sahne = null;
        }

        // ============================================================== P3 · configuration error on the real screen: no soft-lock

        [UnityTest]
        [Category("WP008")]
        [Timeout(180000)]
        public IEnumerator P3_GecerliTeklifUretilemez_EkranGuvenliHata_GoldKesilmez_ContinueCalisir()
        {
            yield return KosuyaGir(kontrollu: true);
            RoundUpgradeShop shop = _run.Shop;

            // Yalnız iki dal açık → üç geçerli kart kurulamaz (karar 17).
            shop.UnlockedPredicate = b => b == UpgradeBranch.Piercing || b == UpgradeBranch.FireRate;
            try
            {
                Assert.IsTrue(_gold.Wallet.Credit(100, _rig.transform.position));
                int bakiye = _gold.Wallet.Balance;
                bool hataGoruldu = false;
                Application.LogCallback dinle = (m, s, t) => { if (t == LogType.Error && m.Contains("Upgrade offer configuration error")) hataGoruldu = true; };
                Application.logMessageReceived += dinle;
                _hatalar.Clear();
                yield return RoundTamamla();
                Application.logMessageReceived -= dinle;
                _hatalar.RemoveAll(h => h.Contains("Upgrade offer configuration error"));   // beklenen açık hata

                Assert.IsTrue(hataGoruldu, "Sistem açık bir configuration error üretir.");
                Assert.IsTrue(shop.IsOpen, "Ekran açık (güvenli hata durumu).");
                Assert.AreEqual(UpgradeOfferStatus.ConfigurationError, shop.Status);
                Assert.AreEqual(0, shop.Cards.Count, "Kart uydurulmaz.");
                Assert.AreEqual(bakiye, _gold.Wallet.Balance, "Gold kesilmez.");
                Assert.AreEqual(UiContext.RoundUpgrade, UiNavigation.Layer);
                Assert.IsTrue(MetinVar("No valid upgrade offer"), "Güvenli İngilizce durum mesajı.");
                Assert.IsTrue(MetinVar("No gold was taken"), "Gold alınmadığı yazılı.");
                Assert.IsFalse(Buton("Reroll").IsInteractable());
                Assert.IsTrue(Buton("Continue").IsInteractable(), "Continue açık.");
                GameObject odak = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                Assert.IsNotNull(odak);
                Assert.AreEqual("Continue", odak.name, "Odak CONTINUE'da (soft-lock yok).");

                Buton("Continue").OnSubmit(null);
                yield return null;
                yield return Bekle(() => _run.Phase == RunPhase.Fighting && _run.Round == 2, FazZamanAsimi, "Continue sonraki roundu başlatmadı.");
                yield return SimulasyonSerbestOlmali("wp008.cfgerr");
                Assert.AreEqual(bakiye, _gold.Wallet.Balance);
            }
            finally
            {
                shop.UnlockedPredicate = null;
            }
        }
    }
}
