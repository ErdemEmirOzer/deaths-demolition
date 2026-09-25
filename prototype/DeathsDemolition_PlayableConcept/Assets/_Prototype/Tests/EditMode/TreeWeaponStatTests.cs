using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-077 K10b aşama 2 · silah mühür / çatal / zirve sayıları (RunTreeHooks.WeaponStats): düğüm alınmadıysa her kanca
    /// nötr, alınan düğüm kataloğun args sayısını verir (gösterim birimi → çalışma birimi), başka silahın düğümü (ya da hata
    /// ayıklama panelinin silah değiştirmesi) hiçbir şey yapmaz. Birkaç kanca ClassicWeaponRuntime üstünde uçtan uca da sınanır.
    /// </summary>
    [TestFixture]
    public sealed class TreeWeaponStatTests
    {
        private PrototypeConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            RunTreeCatalog.TestSeam.Override(null);
            RunTreeHooks.ResetWeaponStatsState();
        }

        [TearDown]
        public void TearDown()
        {
            RunTreeHooks.TestValues = null;
            RunTreeHooks.ResetWeaponStatsState();
            Object.DestroyImmediate(_config);
        }

        /// <summary>A run with <paramref name="silah"/> owning <paramref name="dugumler"/>; its values drive the hooks.</summary>
        private RunTreeState Kos(ClassicWeaponId silah, params string[] dugumler)
        {
            var t = new RunTreeState();
            t.Begin(1, silah, _config, MetaRunModifiers.None);
            foreach (string id in dugumler)
            {
                int i = t.Graph.IndexOf(id);
                Assert.GreaterOrEqual(i, 0, id + " ağaçta yok");
                Assert.IsTrue(t.MarkOwned(i), id + " alınamadı");
            }

            RunTreeHooks.TestValues = t.Values;
            return t;
        }

        private static ClassicWeaponRuntime Kusan(ClassicWeaponId id)
        {
            PrototypeConfig config = Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);
            Assert.IsNotNull(config);
            config.Weapons.RebuildClassicView();
            ClassicWeaponSettings s = config.Weapons.Find(id);
            Assert.IsNotNull(s, id + " kopyası yok");
            var rt = new ClassicWeaponRuntime();
            rt.Equip(s, new WeaponModifierState());
            return rt;
        }

        // ------------------------------------------------------------------ nötr

        [Test]
        public void Sahipsiz_HerKancaNotr()
        {
            foreach (ClassicWeaponId silah in System.Enum.GetValues(typeof(ClassicWeaponId)))
            {
                Kos(silah);
                Assert.AreEqual(1f, RunTreeHooks.ForkFireRateScale(silah), silah + " atış hızı");
                Assert.AreEqual(1f, RunTreeHooks.WeaponFireRateScale(silah, 10f), silah + " atış hızı (Isınma yok)");
                Assert.AreEqual(1f, RunTreeHooks.BurstDelayScale(silah));
                Assert.AreEqual(3, RunTreeHooks.BurstCount(silah, 3));
                Assert.AreEqual(2f, RunTreeHooks.Bloom(silah, 2f, 20));
                Assert.AreEqual(0.4f, RunTreeHooks.BloomPerShot(silah, 0.4f));
                Assert.AreEqual(-0.4f, RunTreeHooks.BloomPerShot(silah, -0.4f));
                Assert.AreEqual(3.5f, RunTreeHooks.HipSpread(silah, 3.5f));
                Assert.AreEqual(1f, RunTreeHooks.SpreadScale(silah));
                Assert.AreEqual(8, RunTreeHooks.PelletCount(silah, 8, 2));
                Assert.AreEqual(1, RunTreeHooks.RoundsPerPull(silah, 2, 0f));
                Assert.AreEqual(100, RunTreeHooks.MagazineSize(silah, 100));
                Assert.AreEqual(1f, RunTreeHooks.ReloadTimeScale(silah));
                Assert.AreEqual(1f, RunTreeHooks.ShotDamageScale(silah, 0f, 5f, 0));
                Assert.AreEqual(1.15f, RunTreeHooks.HeadMultiplier(silah, 1.15f));
                Assert.AreEqual(1, RunTreeHooks.RicochetBounces(silah, 1));
                Assert.AreEqual(0.8f, RunTreeHooks.FiringMoveSpeed(silah, 0.8f));
                RunTreeHooks.CountWeaponHit(silah, 0f);
                Assert.AreEqual(0, RunTreeHooks.CapHitCount, silah + " zirve sayacı");
                Assert.IsFalse(RunTreeHooks.NoReloadWindow(silah, 0f));
            }
        }

        // ------------------------------------------------------------------ ar_1

        [Test]
        public void Muhur_ar_1_AltiAtistanSonraBloomYariya()
        {
            Kos(ClassicWeaponId.AssaultRifle, "seal_ar_1");
            Assert.AreEqual(2f, RunTreeHooks.Bloom(ClassicWeaponId.AssaultRifle, 2f, 5), 1e-5f, "5 atışta tam bloom");
            Assert.AreEqual(1f, RunTreeHooks.Bloom(ClassicWeaponId.AssaultRifle, 2f, 6), 1e-5f, "6 atıştan sonra −%50");
            Assert.AreEqual(-1f, RunTreeHooks.Bloom(ClassicWeaponId.AssaultRifle, -1f, 9), 1e-5f, "oturma (negatif) bloom'a dokunmaz");
        }

        [Test]
        public void Catal_ar_1_x_BloomHepYariya()
        {
            Kos(ClassicWeaponId.AssaultRifle, "fork_ar_1_x");
            Assert.AreEqual(1f, RunTreeHooks.Bloom(ClassicWeaponId.AssaultRifle, 2f, 0), 1e-5f);
            Assert.AreEqual(1f, RunTreeHooks.ForkFireRateScale(ClassicWeaponId.AssaultRifle), "x çatalı atış hızı vermez");
        }

        [Test]
        public void Catal_ar_1_y_AtisHiziVeBloomBuyumesi()
        {
            Kos(ClassicWeaponId.AssaultRifle, "fork_ar_1_y");
            Assert.AreEqual(1.15f, RunTreeHooks.ForkFireRateScale(ClassicWeaponId.AssaultRifle), 1e-5f);
            Assert.AreEqual(1.15f, RunTreeHooks.WeaponFireRateScale(ClassicWeaponId.AssaultRifle, 0f), 1e-5f);
            Assert.AreEqual(0.52f, RunTreeHooks.BloomPerShot(ClassicWeaponId.AssaultRifle, 0.4f), 1e-5f, "+%30 bloom büyümesi");
            Assert.AreEqual(2f, RunTreeHooks.Bloom(ClassicWeaponId.AssaultRifle, 2f, 10), 1e-5f, "y çatalı bloom'u kesmez");
        }

        [Test]
        public void Catal_ar_1_y_CalismaZamaninda_AtisHiziArtar()
        {
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.AssaultRifle);
            float taban = rt.EffectiveFireRate;
            Kos(ClassicWeaponId.AssaultRifle, "fork_ar_1_y");
            Assert.AreEqual(taban * 1.15f, rt.EffectiveFireRate, 1e-3f);
        }

        // ------------------------------------------------------------------ smg_1

        [Test]
        public void Muhur_smg_1_AtesEderkenHareketCarpani()
        {
            Kos(ClassicWeaponId.AutoSmg, "seal_smg_1");
            Assert.AreEqual(1.1f, RunTreeHooks.FiringMoveSpeed(ClassicWeaponId.AutoSmg, 1f), 1e-5f);
        }

        [Test]
        public void Catal_smg_1_x_SarjorBirBucukKat()
        {
            Kos(ClassicWeaponId.AutoSmg, "fork_smg_1_x");
            Assert.AreEqual(63, RunTreeHooks.MagazineSize(ClassicWeaponId.AutoSmg, 42));
            Assert.AreEqual(37, RunTreeHooks.MagazineSize(ClassicWeaponId.AutoSmg, 25), "aşağı yuvarlar");
        }

        [Test]
        public void Catal_smg_1_x_CalismaZamaninda_Sarjor()
        {
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.AutoSmg);
            int taban = rt.MagazineSize;
            Kos(ClassicWeaponId.AutoSmg, "fork_smg_1_x");
            Assert.AreEqual(Mathf.FloorToInt(taban * 1.5f), rt.MagazineSize);
        }

        [Test]
        public void Zirve_smg_1_SekmeIkiKez()
        {
            Kos(ClassicWeaponId.AutoSmg, "cap_smg_1");
            Assert.AreEqual(2, RunTreeHooks.RicochetBounces(ClassicWeaponId.AutoSmg, 1));
            Assert.AreEqual(3, RunTreeHooks.RicochetBounces(ClassicWeaponId.AutoSmg, 3), "fazlasını azaltmaz");
        }

        // ------------------------------------------------------------------ sg_1

        [Test]
        public void Muhur_sg_1_SacmaSekizdenOna()
        {
            Kos(ClassicWeaponId.CombatShotgun, "seal_sg_1");
            Assert.AreEqual(10, RunTreeHooks.PelletCount(ClassicWeaponId.CombatShotgun, 8, 7));
        }

        [Test]
        public void Catal_sg_1_x_KoniYuzdeKirkDar()
        {
            Kos(ClassicWeaponId.CombatShotgun, "fork_sg_1_x");
            Assert.AreEqual(0.6f, RunTreeHooks.SpreadScale(ClassicWeaponId.CombatShotgun), 1e-5f);
        }

        [Test]
        public void Catal_sg_1_x_CalismaZamaninda_Koni()
        {
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.CombatShotgun);
            float taban = rt.CurrentSpreadDegrees;
            Kos(ClassicWeaponId.CombatShotgun, "fork_sg_1_x");
            Assert.AreEqual(taban * 0.6f, rt.CurrentSpreadDegrees, 1e-4f);
        }

        [Test]
        public void Catal_sg_1_y_IkiSacmaFazla()
        {
            Kos(ClassicWeaponId.CombatShotgun, "fork_sg_1_y");
            Assert.AreEqual(10, RunTreeHooks.PelletCount(ClassicWeaponId.CombatShotgun, 8, 7));
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.CombatShotgun);
            Assert.AreEqual(10, rt.PelletCount, "çalışma zamanı 8 + 2");
        }

        // ------------------------------------------------------------------ ar_2

        [Test]
        public void Muhur_ar_2_KafaCarpaniBirBucuk()
        {
            Kos(ClassicWeaponId.Ar2, "seal_ar_2");
            Assert.AreEqual(1.5f, RunTreeHooks.HeadMultiplier(ClassicWeaponId.Ar2, 1.15f), 1e-5f);
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.Ar2);
            Assert.AreEqual(1.5f, rt.Launch(0).HeadMultiplier, 1e-5f, "fırlatma kafa çarpanı");
        }

        [Test]
        public void Catal_ar_2_x_SeriArasiGecikmeYuzdeYirmiKisa()
        {
            Kos(ClassicWeaponId.Ar2, "fork_ar_2_x");
            Assert.AreEqual(0.8f, RunTreeHooks.BurstDelayScale(ClassicWeaponId.Ar2), 1e-5f);
        }

        [Test]
        public void Catal_ar_2_y_SeriUctenDorde()
        {
            Kos(ClassicWeaponId.Ar2, "fork_ar_2_y");
            Assert.AreEqual(4, RunTreeHooks.BurstCount(ClassicWeaponId.Ar2, 3));
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.Ar2);
            rt.BeginBurst();
            int atis = 0;
            while (rt.BurstActive && atis < 10)
            {
                rt.ConsumeShot();
                atis++;
            }

            Assert.AreEqual(4, atis, "seri 4 mermi");
        }

        // ------------------------------------------------------------------ smg_2

        [Test]
        public void Catal_smg_2_y_AtisHiziYuzdeOnBes()
        {
            Kos(ClassicWeaponId.Smg2, "fork_smg_2_y");
            Assert.AreEqual(1.15f, RunTreeHooks.ForkFireRateScale(ClassicWeaponId.Smg2), 1e-5f);
        }

        // ------------------------------------------------------------------ sg_2

        [Test]
        public void Muhur_sg_2_IkiNamluTekTetik()
        {
            Kos(ClassicWeaponId.Sg2, "seal_sg_2");
            Assert.AreEqual(20, RunTreeHooks.PelletCount(ClassicWeaponId.Sg2, 10, 2));
            Assert.AreEqual(10, RunTreeHooks.PelletCount(ClassicWeaponId.Sg2, 10, 1), "tek mermi: tek namlu");
            Assert.AreEqual(2, RunTreeHooks.RoundsPerPull(ClassicWeaponId.Sg2, 2, 0f));
            Assert.AreEqual(1, RunTreeHooks.RoundsPerPull(ClassicWeaponId.Sg2, 1, 0f));
        }

        [Test]
        public void Muhur_sg_2_CalismaZamaninda_IkiMermiHarcar()
        {
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.Sg2);
            Assert.AreEqual(2, rt.Ammo);
            Kos(ClassicWeaponId.Sg2, "seal_sg_2");
            Assert.AreEqual(20, rt.PelletCount);
            rt.ConsumeShot();
            Assert.AreEqual(0, rt.Ammo, "iki namlu iki mermi");

            ClassicWeaponRuntime tek = Kusan(ClassicWeaponId.Sg2);
            RunTreeHooks.TestValues = null;
            tek.ConsumeShot();
            Assert.AreEqual(1, tek.Ammo);
            Kos(ClassicWeaponId.Sg2, "seal_sg_2");
            Assert.AreEqual(10, tek.PelletCount, "tek mermi kaldıysa tek namlu");
            tek.ConsumeShot();
            Assert.AreEqual(0, tek.Ammo);
        }

        [Test]
        public void Catal_sg_2_y_DoldurmaYuzdeYirmiBesKisa()
        {
            Kos(ClassicWeaponId.Sg2, "fork_sg_2_y");
            Assert.AreEqual(0.75f, RunTreeHooks.ReloadTimeScale(ClassicWeaponId.Sg2), 1e-5f);
        }

        // ------------------------------------------------------------------ lmg_1

        [Test]
        public void Muhur_lmg_1_SarjorYuzdenYuzElliye()
        {
            Kos(ClassicWeaponId.Lmg1, "seal_lmg_1");
            Assert.AreEqual(150, RunTreeHooks.MagazineSize(ClassicWeaponId.Lmg1, 100));
        }

        [Test]
        public void Catal_lmg_1_x_DururkenHasarYuzdeYirmiBes()
        {
            Kos(ClassicWeaponId.Lmg1, "fork_lmg_1_x");
            Assert.AreEqual(1.25f, RunTreeHooks.ShotDamageScale(ClassicWeaponId.Lmg1, 0.05f, 0f, 0), 1e-5f, "duruyor");
            Assert.AreEqual(1.25f, RunTreeHooks.ShotDamageScale(ClassicWeaponId.Lmg1, 0.1f, 0f, 0), 1e-5f, "eşik dahil");
            Assert.AreEqual(1f, RunTreeHooks.ShotDamageScale(ClassicWeaponId.Lmg1, 0.5f, 0f, 0), 1e-5f, "yürüyor");
        }

        [Test]
        public void Catal_lmg_1_y_AtesEderkenHareketCezasiYok()
        {
            Kos(ClassicWeaponId.Lmg1, "fork_lmg_1_y");
            Assert.AreEqual(1f, RunTreeHooks.FiringMoveSpeed(ClassicWeaponId.Lmg1, 0.8f), 1e-5f);
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.Lmg1);
            Assert.AreEqual(rt.MoveSpeedMultiplier(false), rt.MoveSpeedMultiplier(true), 1e-5f);
        }

        [Test]
        public void Zirve_lmg_1_KirkIsabettenSonraAltiSaniyeDoldurmaYok()
        {
            Kos(ClassicWeaponId.Lmg1, "cap_lmg_1");
            for (int i = 0; i < 39; i++) RunTreeHooks.CountWeaponHit(ClassicWeaponId.Lmg1, 10f);
            Assert.AreEqual(39, RunTreeHooks.CapHitCount);
            Assert.IsFalse(RunTreeHooks.NoReloadWindow(ClassicWeaponId.Lmg1, 10f));
            RunTreeHooks.CountWeaponHit(ClassicWeaponId.Lmg2, 10f);
            Assert.AreEqual(39, RunTreeHooks.CapHitCount, "başka silahın isabeti sayılmaz");

            RunTreeHooks.CountWeaponHit(ClassicWeaponId.Lmg1, 10f);
            Assert.AreEqual(0, RunTreeHooks.CapHitCount, "pencere açılınca sayaç sıfırlanır");
            Assert.IsTrue(RunTreeHooks.NoReloadWindow(ClassicWeaponId.Lmg1, 15.9f));
            Assert.AreEqual(0, RunTreeHooks.RoundsPerPull(ClassicWeaponId.Lmg1, 50, 12f), "pencerede mermi harcanmaz");
            RunTreeHooks.CountWeaponHit(ClassicWeaponId.Lmg1, 12f);
            Assert.AreEqual(0, RunTreeHooks.CapHitCount, "açık pencerede isabet sayılmaz");

            Assert.IsFalse(RunTreeHooks.NoReloadWindow(ClassicWeaponId.Lmg1, 16.01f), "6 s sonra biter");
            Assert.AreEqual(1, RunTreeHooks.RoundsPerPull(ClassicWeaponId.Lmg1, 50, 16.01f));

            RunTreeHooks.ResetWeaponStatsState();
            Assert.IsFalse(RunTreeHooks.NoReloadWindow(ClassicWeaponId.Lmg1, 11f), "yeni koşu pencereyi kapatır");
        }

        // ------------------------------------------------------------------ lmg_2

        [Test]
        public void Muhur_lmg_2_UcAtistaOturur_TabanKoni09()
        {
            Kos(ClassicWeaponId.Lmg2, "seal_lmg_2");
            Assert.AreEqual(-0.8f, RunTreeHooks.BloomPerShot(ClassicWeaponId.Lmg2, -0.4f), 1e-5f, "6 → 3 atış: adım ×2");
            Assert.AreEqual(0.9f, RunTreeHooks.HipSpread(ClassicWeaponId.Lmg2, 1.2f), 1e-5f, "1,2° → 0,9°");
        }

        [Test]
        public void Muhur_lmg_2_CalismaZamaninda_UcuncuAtistaTaban()
        {
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.Lmg2);
            float hip = rt.CurrentSpreadDegrees;
            Kos(ClassicWeaponId.Lmg2, "seal_lmg_2");
            float muhurlu = rt.CurrentSpreadDegrees;
            Assert.AreEqual(hip * 0.9f / 1.2f, muhurlu, 1e-4f, "hip × 0,75");
            float taban = muhurlu * rt.Definition.accuracy.settleFloorShare;
            rt.ConsumeShot();
            rt.ConsumeShot();
            Assert.Greater(rt.CurrentSpreadDegrees, taban + 1e-3f, "2 atışta henüz oturmadı");
            rt.ConsumeShot();
            Assert.AreEqual(taban, rt.CurrentSpreadDegrees, 1e-4f, "3. atışta taban");
        }

        [Test]
        public void Catal_lmg_2_x_AtisHiziYuzdeOnBes()
        {
            Kos(ClassicWeaponId.Lmg2, "fork_lmg_2_x");
            Assert.AreEqual(1.15f, RunTreeHooks.ForkFireRateScale(ClassicWeaponId.Lmg2), 1e-5f);
        }

        [Test]
        public void Catal_lmg_2_y_DoldurmadanSonraIlkOnMermiYuzdeOtuz()
        {
            Kos(ClassicWeaponId.Lmg2, "fork_lmg_2_y");
            Assert.AreEqual(1.3f, RunTreeHooks.ShotDamageScale(ClassicWeaponId.Lmg2, 1f, 0f, 0), 1e-5f);
            Assert.AreEqual(1.3f, RunTreeHooks.ShotDamageScale(ClassicWeaponId.Lmg2, 1f, 0f, 9), 1e-5f);
            Assert.AreEqual(1f, RunTreeHooks.ShotDamageScale(ClassicWeaponId.Lmg2, 1f, 0f, 10), 1e-5f);
        }

        [Test]
        public void Catal_lmg_2_y_CalismaZamaninda_YalnizTamamlananDoldurmadanSonra()
        {
            Kos(ClassicWeaponId.Lmg2, "fork_lmg_2_y");
            ClassicWeaponRuntime rt = Kusan(ClassicWeaponId.Lmg2);
            float taban = rt.BuildShot(Vector3.zero, Vector3.forward).Damage;
            RunTreeHooks.TestValues = null;
            Assert.AreEqual(taban, rt.BuildShot(Vector3.zero, Vector3.forward).Damage, 1e-4f, "kuşanınca bonus yok");
            Kos(ClassicWeaponId.Lmg2, "fork_lmg_2_y");

            rt.ConsumeShot();
            Assert.IsTrue(rt.TryBeginReload());
            rt.Tick(10f);
            Assert.IsTrue(rt.LastReloadCompleted);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(taban * 1.3f, rt.BuildShot(Vector3.zero, Vector3.forward).Damage, 1e-3f, "mermi " + (i + 1));
                rt.ConsumeShot();
            }

            Assert.AreEqual(taban, rt.BuildShot(Vector3.zero, Vector3.forward).Damage, 1e-4f, "11. mermi normal");
        }

        // ------------------------------------------------------------------ sr_1

        [Test]
        public void Catal_sr_1_x_IkiBucukSaniyeBekleyinceIkiKat()
        {
            // S-04: the wait is 2.5 s (the bolt cycle is already 1.25 s, so 1 s would make every shot ×2).
            Kos(ClassicWeaponId.Sr1, "fork_sr_1_x");
            Assert.AreEqual(2f, RunTreeHooks.ShotDamageScale(ClassicWeaponId.Sr1, 1f, 2.5f, 0), 1e-5f);
            Assert.AreEqual(1f, RunTreeHooks.ShotDamageScale(ClassicWeaponId.Sr1, 1f, 1.3f, 0), 1e-5f, "normal atış temposu (1,25 s) ×2 almaz");
        }

        [Test]
        public void Catal_sr_1_y_AtisHiziYuzdeOtuzBes()
        {
            Kos(ClassicWeaponId.Sr1, "fork_sr_1_y");
            Assert.AreEqual(1.35f, RunTreeHooks.ForkFireRateScale(ClassicWeaponId.Sr1), 1e-5f);
        }

        // ------------------------------------------------------------------ başka silah

        [Test]
        public void BaskaSilahinDugumu_Etkisiz()
        {
            // sg_1 koşusu mührü: sg_2 / ar_1 soran silah (hata ayıklama değiştirmesi) etkilenmez.
            Kos(ClassicWeaponId.CombatShotgun, "seal_sg_1", "fork_sg_1_x", "fork_sg_1_y");
            Assert.AreEqual(10, RunTreeHooks.PelletCount(ClassicWeaponId.Sg2, 10, 2));
            Assert.AreEqual(1, RunTreeHooks.PelletCount(ClassicWeaponId.AssaultRifle, 1, 30));
            Assert.AreEqual(1f, RunTreeHooks.SpreadScale(ClassicWeaponId.AssaultRifle));

            // ar_1 mührü + çatalları smg_1'e / ar_2'ye geçmez.
            Kos(ClassicWeaponId.AssaultRifle, "seal_ar_1", "fork_ar_1_x", "fork_ar_1_y");
            Assert.AreEqual(2f, RunTreeHooks.Bloom(ClassicWeaponId.AutoSmg, 2f, 20));
            Assert.AreEqual(1f, RunTreeHooks.ForkFireRateScale(ClassicWeaponId.Ar2));
            Assert.AreEqual(0.55f, RunTreeHooks.BloomPerShot(ClassicWeaponId.AutoSmg, 0.55f));
            Assert.AreEqual(3, RunTreeHooks.BurstCount(ClassicWeaponId.Ar2, 3));
            Assert.AreEqual(1.15f, RunTreeHooks.HeadMultiplier(ClassicWeaponId.Ar2, 1.15f));

            // lmg_1 zirvesi lmg_1 koşusunda bile lmg_2 isabetini saymaz; lmg_2 koşusunda lmg_1 düğümü yok.
            Kos(ClassicWeaponId.Lmg2, "seal_lmg_2", "fork_lmg_2_y");
            Assert.AreEqual(100, RunTreeHooks.MagazineSize(ClassicWeaponId.Lmg1, 100));
            Assert.AreEqual(1f, RunTreeHooks.ShotDamageScale(ClassicWeaponId.Lmg1, 0f, 5f, 0));
            Assert.AreEqual(3.5f, RunTreeHooks.HipSpread(ClassicWeaponId.Lmg1, 3.5f));
            RunTreeHooks.CountWeaponHit(ClassicWeaponId.Lmg1, 0f);
            Assert.AreEqual(0, RunTreeHooks.CapHitCount);
        }
    }
}
