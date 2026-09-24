using System;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // WP-051C: Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-005 · HUD presenter sözleşmesi: yalnız değişen alan biçimlenir; sessiz kare
    /// 0 B; yeni koşuda sıfırlama; düşük sağlık eşiği; reload durumları; "—" yer tutucu.
    /// </summary>
    [TestFixture]
    [Category("WP005")]
    public sealed class RunHudViewModelTests
    {
        private RunHudViewModel _vm;

        [SetUp]
        public void SetUp()
        {
            _vm = new RunHudViewModel();
        }

        private static RunHudState Durum(int round, int pool, int alive, int gold, bool exhausted = false)
        {
            return new RunHudState
            {
                Round = round, PoolRemaining = pool, AliveEnemies = alive, Gold = gold,
                PoolExhausted = exhausted, Phase = RunPhase.Fighting
            };
        }

        private static ClassicWeaponHudState Silah(string ad, int ammo, int mag, bool reloading = false,
            float progress = 0f, bool suspended = false)
        {
            return new ClassicWeaponHudState
            {
                Id = ClassicWeaponId.AssaultRifle, DisplayName = ad, AmmoInMagazine = ammo, MagazineSize = mag,
                IsReloading = reloading, ReloadProgress01 = progress, TemporarilyDisabled = suspended
            };
        }

        private static PlayerHealthSnapshot Saglik(float current, float max, bool dead = false,
            bool regen = false, float untilRegen = 0f)
        {
            return new PlayerHealthSnapshot(null, current, max, regen, untilRegen, dead);
        }

        [Test]
        public void IlkDurum_YerTutucular()
        {
            Assert.AreEqual("—", _vm.RoundText);
            Assert.AreEqual("—", _vm.EnemiesText);
            Assert.AreEqual("—", _vm.HealthText);
            Assert.AreEqual("—", _vm.AmmoText);
            Assert.AreEqual("—", _vm.WeaponName);
            Assert.AreEqual("no upgrades", _vm.BuildText);
            Assert.AreEqual("0", _vm.GoldText);
            Assert.IsFalse(_vm.HealthKnown);
        }

        [Test]
        public void Round1_Dogru_Bicimlenir_VeYalnizDegisenAlanBayraklanir()
        {
            RunHudField f = _vm.ApplyRun(Durum(1, 10, 2, 0));
            Assert.AreEqual("1", _vm.RoundText);
            Assert.AreEqual("12", _vm.EnemiesText);
            Assert.AreEqual("10 left to arrive  ·  2 on the map", _vm.EnemiesBreakdown);
            Assert.AreEqual("0", _vm.GoldText);
            Assert.IsTrue((f & RunHudField.Round) != 0);
            Assert.IsTrue((f & RunHudField.Enemies) != 0);
            Assert.IsTrue((f & RunHudField.Gold) != 0, "0 ilk kez yazılır (int.MinValue tabanı)");

            f = _vm.ApplyRun(Durum(1, 10, 2, 7));
            Assert.AreEqual(RunHudField.Gold, f, "yalnız gold değişti");
            Assert.AreEqual("7", _vm.GoldText);

            f = _vm.ApplyRun(Durum(1, 9, 3, 7));
            Assert.AreEqual(RunHudField.Enemies, f, "toplam 12 aynı kalsa da kırılım değişti");
            Assert.AreEqual("12", _vm.EnemiesText);
            Assert.AreEqual("9 left to arrive  ·  3 on the map", _vm.EnemiesBreakdown);

            f = _vm.ApplyRun(Durum(1, 0, 3, 7, true));
            Assert.AreEqual("pool empty  ·  3 still on the map", _vm.EnemiesBreakdown);
            Assert.IsTrue((f & RunHudField.Enemies) != 0);

            f = _vm.ApplyRun(Durum(2, 20, 0, 7));
            Assert.IsTrue((f & RunHudField.Round) != 0);
            Assert.AreEqual("2", _vm.RoundText);
        }

        [Test]
        public void SessizKare_SifirAlanVeSifirAyirma()
        {
            RunHudState durum = Durum(3, 5, 4, 42);
            ClassicWeaponHudState silah = Silah("Assault Rifle", 17, 30);
            PlayerHealthSnapshot saglik = Saglik(64f, 100f);
            string build = "P1 / FR2";

            _vm.ApplyRun(durum);
            _vm.ApplyWeapon(silah);
            _vm.ApplyBuild(build);
            _vm.ApplyHealth(saglik, 0.3f);

            // Isınma: aynı çağrıları bir kez daha yap, sonra ölçüm.
            _vm.ApplyRun(durum);
            _vm.ApplyWeapon(silah);
            _vm.ApplyBuild(build);
            _vm.ApplyHealth(saglik, 0.3f);

            // WP-051C: the process-wide Mono heap (HeapDelta) also sees other Editor threads' allocations; the test
            // framework's GC.Alloc recorder is filtered to the calling thread only, and a positive control proves it is
            // not blind. (Unity Mono'da GC.GetAllocatedBytesForCurrentThread HER ZAMAN 0 döner, kullanılmaz.)
            byte[] yem = null;
            Assert.That(() => { yem = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            RunHudField toplam = RunHudField.None;
            Assert.That(() =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    toplam |= _vm.ApplyRun(durum);
                    toplam |= _vm.ApplyWeapon(silah);
                    toplam |= _vm.ApplyBuild(build);
                    toplam |= _vm.ApplyHealth(saglik, 0.3f);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "sessiz kare ayırmamalı (10000 kare; bu iş parçacığında GC.Alloc örneği görüldü; yem " + (yem != null ? yem.Length : 0) + " B)");

            Debug.Log("[DD-TEST] wp005.hudvm quiet10000 threadAlloc=none fields=" + toplam);
            Assert.AreEqual(RunHudField.None, toplam, "sessiz karede hiçbir alan bayraklanmaz");
        }

        [Test]
        public void Gold_OlayIleAyriGelir()
        {
            _vm.ApplyRun(Durum(1, 1, 1, 0));
            Assert.AreEqual(RunHudField.Gold, _vm.ApplyGold(5));
            Assert.AreEqual("5", _vm.GoldText);
            Assert.AreEqual(RunHudField.None, _vm.ApplyGold(5));
            Assert.AreEqual(RunHudField.None, _vm.ApplyRun(Durum(1, 1, 1, 5)), "snapshot aynı değeri getirince yeniden biçimlenmez");
        }

        [Test]
        public void Saglik_MetinTamSayidaBarSurekli_DusukEsik()
        {
            RunHudField f = _vm.ApplyHealth(Saglik(100f, 100f), 0.3f);
            Assert.AreEqual("100 / 100", _vm.HealthText);
            Assert.IsTrue((f & RunHudField.Health) != 0);
            Assert.IsTrue((f & RunHudField.HealthBar) != 0);
            Assert.IsFalse(_vm.HealthLow);
            Assert.IsTrue(_vm.HealthKnown);

            f = _vm.ApplyHealth(Saglik(99.6f, 100f), 0.3f);
            Assert.AreEqual("100 / 100", _vm.HealthText, "tavana yuvarlama: 99.6 → 100");
            Assert.IsTrue((f & RunHudField.Health) == 0, "tam sayı değişmedi → metin yeniden biçimlenmez");
            Assert.IsTrue((f & RunHudField.HealthBar) != 0, "bar sürekli hareket eder");

            f = _vm.ApplyHealth(Saglik(29f, 100f), 0.3f);
            Assert.IsTrue(_vm.HealthLow, "0.29 ≤ 0.30 → düşük");
            Assert.AreEqual("29 / 100", _vm.HealthText);

            f = _vm.ApplyHealth(Saglik(0f, 100f, dead: true), 0.3f);
            Assert.AreEqual("DOWN", _vm.HealthStateText);
            Assert.AreEqual(RunHudTone.Danger, _vm.HealthStateTone);
            Assert.AreEqual("0 / 100", _vm.HealthText);
        }

        [Test]
        public void SaglikDurumSatiri_RegenVeGeriSayim()
        {
            _vm.ApplyHealth(Saglik(50f, 100f, regen: false, untilRegen: 2.34f), 0.3f);
            Assert.AreEqual("recovery in 2.4s", _vm.HealthStateText, "onda birlik tavana yuvarlama");
            Assert.AreEqual(RunHudTone.Neutral, _vm.HealthStateTone);

            RunHudField f = _vm.ApplyHealth(Saglik(50f, 100f, regen: false, untilRegen: 2.31f), 0.3f);
            Assert.IsTrue((f & RunHudField.HealthState) == 0, "aynı onda birlik → yeniden biçimlenmez");

            _vm.ApplyHealth(Saglik(50f, 100f, regen: true), 0.3f);
            Assert.AreEqual("RECOVERING", _vm.HealthStateText);
            Assert.AreEqual(RunHudTone.Positive, _vm.HealthStateTone);

            _vm.ApplyHealth(Saglik(100f, 100f), 0.3f);
            Assert.AreEqual(string.Empty, _vm.HealthStateText);
        }

        [Test]
        public void Silah_AdSarjorReloadSuspended()
        {
            RunHudField f = _vm.ApplyWeapon(Silah("Assault Rifle", 30, 30));
            Assert.AreEqual("ASSAULT RIFLE", _vm.WeaponName);
            Assert.AreEqual("30 / 30", _vm.AmmoText);
            Assert.AreEqual(30, _vm.Ammo, "WP-068 E11: the numbers behind the fraction and the ladder");
            Assert.AreEqual(30, _vm.Magazine);
            Assert.IsFalse(_vm.AmmoLow);
            Assert.AreEqual(RunHudReloadState.Ready, _vm.ReloadState);
            Assert.AreEqual(1f, _vm.ReloadProgress01);
            Assert.IsTrue((f & RunHudField.WeaponName) != 0);
            Assert.IsTrue((f & RunHudField.Ammo) != 0);

            _vm.ApplyWeapon(Silah("Assault Rifle", 6, 30));
            Assert.IsTrue(_vm.AmmoLow, "E11: ≤ 20 % of the magazine is LOW");
            _vm.ApplyWeapon(Silah("Assault Rifle", 7, 30));
            Assert.IsFalse(_vm.AmmoLow);

            f = _vm.ApplyWeapon(Silah("Assault Rifle", 0, 30));
            Assert.AreEqual("0 / 30", _vm.AmmoText);
            Assert.IsFalse(_vm.AmmoLow, "empty is EMPTY, not LOW");
            Assert.AreEqual(RunHudReloadState.MagazineEmpty, _vm.ReloadState);
            Assert.AreEqual(0f, _vm.ReloadProgress01);
            Assert.IsTrue((f & RunHudField.WeaponName) == 0, "ad aynı → yeniden biçimlenmez");

            f = _vm.ApplyWeapon(Silah("Assault Rifle", 0, 30, reloading: true, progress: 0.5f));
            Assert.AreEqual(RunHudReloadState.Reloading, _vm.ReloadState);
            Assert.AreEqual(0.5f, _vm.ReloadProgress01, 0.001f);
            Assert.IsTrue((f & RunHudField.Reload) != 0);

            f = _vm.ApplyWeapon(Silah("Assault Rifle", 30, 30, suspended: true));
            Assert.IsTrue(_vm.Suspended);
            Assert.IsTrue((f & RunHudField.Suspended) != 0);

            _vm.ApplyWeapon(Silah(null, 0, 0));
            Assert.AreEqual("—", _vm.WeaponName);
            Assert.AreEqual("—", _vm.AmmoText, "şarjörsüz (silah yok) → yer tutucu");
        }

        [Test]
        public void Build_KaynakDegisince_MetinDegisir()
        {
            RunHudField f = _vm.ApplyBuild("Stock");
            Assert.AreEqual("no upgrades", _vm.BuildText);

            f = _vm.ApplyBuild("P1 / FR2 / MS1 / HP1");
            Assert.AreEqual(RunHudField.Build, f);
            Assert.AreEqual("P1 / FR2 / MS1 / HP1", _vm.BuildText);

            string uzun = "P3 / R3 / X3 / C3 / FR3 / RL3 / MS3 / HP3 / RD3 / RR3";
            _vm.ApplyBuild(uzun);
            Assert.LessOrEqual(_vm.BuildText.Length, RunHudViewModel.BuildMaxCharacters, "HUD satırı kırpılır");
            Assert.IsTrue(_vm.BuildText.EndsWith("…"), "kırpma işareti");

            Assert.AreEqual(RunHudField.None, _vm.ApplyBuild(uzun), "aynı referans → 0 alan");
        }

        [Test]
        public void Reset_YeniKosudaEskiDegerlerGitmez()
        {
            _vm.ApplyRun(Durum(4, 0, 0, 120, true));
            _vm.ApplyWeapon(Silah("Auto SMG", 3, 40));
            _vm.ApplyBuild("P2 / FR1");
            _vm.ApplyHealth(Saglik(12f, 100f), 0.3f);

            _vm.Reset();

            Assert.AreEqual("—", _vm.RoundText);
            Assert.AreEqual("0", _vm.GoldText);
            Assert.AreEqual("—", _vm.AmmoText);
            Assert.AreEqual("—", _vm.WeaponName);
            Assert.AreEqual("no upgrades", _vm.BuildText);
            Assert.AreEqual("—", _vm.HealthText);
            Assert.IsFalse(_vm.HealthLow);
            Assert.IsFalse(_vm.HealthKnown);
            Assert.AreEqual("waiting for the round to start", _vm.EnemiesBreakdown);

            // Sıfırlama sonrası ilk snapshot her alanı yeniden bayraklar.
            RunHudField f = _vm.ApplyRun(Durum(1, 8, 0, 0));
            Assert.IsTrue((f & RunHudField.Round) != 0 && (f & RunHudField.Gold) != 0 && (f & RunHudField.Enemies) != 0);
            Assert.AreEqual("1", _vm.RoundText);
            Assert.AreEqual("0", _vm.GoldText);
        }

        /// <summary>WP-068 8c · E22: the death line names the killing blow in words; an unknown source is "DOWN".</summary>
        [Test]
        public void OlumSatiri_KaynakKelimeOlur()
        {
            Assert.AreEqual("DOWN  ·  killed by a Standard's swipe", RunHudViewModel.DeathLine("Zombie.Standard.Swipe"));
            Assert.AreEqual("DOWN  ·  killed by a Special's lunge", RunHudViewModel.DeathLine("Zombie.Special.Lunge"));
            Assert.AreEqual("DOWN  ·  killed by an Elite's slam", RunHudViewModel.DeathLine("Zombie.Elite.Slam"));
            Assert.AreEqual("DOWN  ·  killed by an Ultra", RunHudViewModel.DeathLine("Zombie.Ultra"));
            Assert.AreEqual("DOWN  ·  killed by hazard fire", RunHudViewModel.DeathLine("Hazard.Fire"));
            Assert.AreEqual("DOWN  ·  killed by a zombie", RunHudViewModel.DeathLine("Zombie"));
            Assert.AreEqual("DOWN", RunHudViewModel.DeathLine(null));
            Assert.AreEqual("DOWN", RunHudViewModel.DeathLine(string.Empty));
            Assert.AreEqual("DOWN", RunHudViewModel.DeathLine("."));
            string longest = RunHudViewModel.DeathLine("Zombie.Ultra.AVeryLongAttackNameThatWouldOverflow");
            Assert.LessOrEqual(longest.Length, "DOWN  ·  killed by ".Length + RunHudViewModel.KillerMaxCharacters, "satır can kümesinin altyazı satırına sığar");
        }
    }
}
