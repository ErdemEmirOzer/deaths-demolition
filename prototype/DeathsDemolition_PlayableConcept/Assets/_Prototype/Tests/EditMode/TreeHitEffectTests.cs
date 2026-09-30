using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-077 K10b aşama 2 · vuruş yolundaki ağaç etkileri (RunTreeHooks.HitEffects) ve yanan zemin havuzu
    /// (RunTreeGroundFire): sahip olunmayan düğüm nötr, sayılar kart args'ından, başka silahın düğümü hiçbir şey yapmaz.
    /// Sahnesiz: saf kanca işlevleri ve havuz defteri (ekle / tikle / sön / temizle / kapasite).
    /// </summary>
    [TestFixture]
    public sealed class TreeHitEffectTests
    {
        private PrototypeConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            RunTreeCatalog.TestSeam.Override(null);
            RunTreeHooks.ResetHitEffectsState();
            RunTreeGroundFire.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            RunTreeHooks.TestValues = null;
            RunTreeHooks.ResetHitEffectsState();
            RunTreeGroundFire.Clear();
            Object.DestroyImmediate(_config);
        }

        private void Sahip(ClassicWeaponId silah, params string[] idler)
        {
            var t = new RunTreeState();
            t.Begin(1, silah, _config, MetaRunModifiers.None);
            foreach (string id in idler) Assert.IsTrue(t.MarkOwned(t.Graph.IndexOf(id)), id);
            RunTreeHooks.TestValues = t.Values;
        }

        // ------------------------------------------------------------------ nötr

        [Test]
        public void Sahipsiz_KancalarNotr()
        {
            RunTreeHooks.TestValues = new RunTreeEffectValues();

            Assert.IsFalse(RunTreeHooks.IgnoresArmor(ClassicWeaponId.Sr1, BodyRegion.Head, 0, 1));
            Assert.AreEqual(1f, RunTreeHooks.CloseRangeScale(ClassicWeaponId.Sg2, 1f));
            Assert.IsFalse(RunTreeHooks.AshWallApplies(ClassicWeaponId.CombatShotgun, 1f));
            Assert.IsFalse(RunTreeHooks.BlackIronStaggers(ClassicWeaponId.Lmg2, true));
            for (int i = 0; i < 6; i++) Assert.IsFalse(RunTreeHooks.LightningFangChains(ClassicWeaponId.Smg2, true));
            Assert.AreEqual(0f, RunTreeHooks.LightningFangBonus(ClassicWeaponId.Smg2));
            Assert.IsFalse(RunTreeHooks.ThirdEyeStrikes(ClassicWeaponId.Ar2, 3));
            Assert.IsFalse(RunTreeHooks.RoninMarkRound(ClassicWeaponId.AssaultRifle, 5));
            Assert.AreEqual(0.45f, RunTreeHooks.StaggerSecondsFor(ClassicWeaponId.Smg2, 0.45f));
            Assert.IsFalse(RunTreeHooks.RunnerFalls(ClassicWeaponId.Smg2, EnemyTier.Special));
            Assert.AreEqual(0, RunTreeHooks.RocketSplitCount(ClassicWeaponId.Rl1));
            Assert.IsFalse(RunTreeHooks.AshCloudFor(160f, out _, out _));
            Assert.IsFalse(RunTreeHooks.HibanaFor(ClassicWeaponId.Rl1, out _, out _));
            Assert.IsFalse(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 0, RunTreeHooks.TwinBarrelPair, 7));
            Assert.IsFalse(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 1, RunTreeHooks.TwinBarrelPair, 7));

            float hasar = 160f, ic = 1.8f, dis = 5f;
            RunTreeHooks.ShapeRocketBlast(ClassicWeaponId.Rl1, ref hasar, ref ic, ref dis);
            Assert.AreEqual(160f, hasar);
            Assert.AreEqual(1.8f, ic);
            Assert.AreEqual(5f, dis);

            var p = new ProjectileState { WeaponId = ClassicWeaponId.Sr1, TargetsLeft = 5 };
            RunTreeHooks.OnProjectileSpawned(ref p);
            Assert.AreEqual(0, p.TreeFlags);
            Assert.AreEqual(5, p.TargetsLeft);

            RunTreeHooks.OnRocketBlast(ClassicWeaponId.Rl1, Vector3.zero, 160f, 5f);
            RunTreeHooks.OnClassicExplosion(Vector3.zero, 40f, 3f);
            Assert.AreEqual(0, RunTreeGroundFire.Count, "düğüm yokken zemin yanmaz");
        }

        [Test]
        public void BaskaSilahinDugumu_EtkisizKalir()
        {
            Sahip(ClassicWeaponId.Sr1, "seal_sr_1", "cap_sr_1");

            Assert.AreEqual(0.45f, RunTreeHooks.StaggerSecondsFor(ClassicWeaponId.Smg2, 0.45f));
            Assert.IsFalse(RunTreeHooks.RunnerFalls(ClassicWeaponId.Smg2, EnemyTier.Special));
            Assert.IsFalse(RunTreeHooks.HibanaFor(ClassicWeaponId.Rl1, out _, out _));
            Assert.AreEqual(0, RunTreeHooks.RocketSplitCount(ClassicWeaponId.Rl1));
            Assert.IsFalse(RunTreeHooks.BlackIronStaggers(ClassicWeaponId.Lmg2, true));
            Assert.IsFalse(RunTreeHooks.AshWallApplies(ClassicWeaponId.CombatShotgun, 1f));
            Assert.IsFalse(RunTreeHooks.ThirdEyeStrikes(ClassicWeaponId.Ar2, 3));
            Assert.IsFalse(RunTreeHooks.RoninMarkRound(ClassicWeaponId.AssaultRifle, 5));
            Assert.IsFalse(RunTreeHooks.IgnoresArmor(ClassicWeaponId.Ar2, BodyRegion.Head, 0, 1), "sr_1 mührü ar_2 kafasına işlemez");

            var p = new ProjectileState { WeaponId = ClassicWeaponId.AssaultRifle, TargetsLeft = 2 };
            for (int i = 0; i < 5; i++) RunTreeHooks.OnProjectileSpawned(ref p);
            Assert.AreEqual(0, p.TreeFlags, "sr_1 bitiricisi ar_1 mermisini etiketlemez");
            Assert.AreEqual(2, p.TargetsLeft);
        }

        // ------------------------------------------------------------------ sr_1

        [Test]
        public void Muhur_sr_1_KafaVurusuZirhiYoksayar()
        {
            Sahip(ClassicWeaponId.Sr1, "seal_sr_1");
            Assert.IsTrue(RunTreeHooks.IgnoresArmor(ClassicWeaponId.Sr1, BodyRegion.Head, 0, 3), "kafa: zırh yok (kaçıncı gövde olursa olsun)");
            Assert.IsFalse(RunTreeHooks.IgnoresArmor(ClassicWeaponId.Sr1, BodyRegion.UpperBody, 0, 1), "gövde: zırh işler");
            Assert.IsFalse(RunTreeHooks.IgnoresArmor(ClassicWeaponId.Sr1, BodyRegion.LowerBody, 0, 1));
        }

        [Test]
        public void Bitirici_sr_1_IlkGovdeZirhsizSinirsizDelme()
        {
            Sahip(ClassicWeaponId.Sr1, "cap_sr_1");
            var p = new ProjectileState { WeaponId = ClassicWeaponId.Sr1, TargetsLeft = 5 };
            RunTreeHooks.OnProjectileSpawned(ref p);

            Assert.AreEqual(RunTreeHooks.UnlimitedPierceTargets, p.TargetsLeft, "sınırsız delme");
            Assert.AreNotEqual(0, p.TreeFlags & RunTreeHooks.TreeRoundFirstBodyIgnoresArmor);
            Assert.IsTrue(RunTreeHooks.IgnoresArmor(ClassicWeaponId.Sr1, BodyRegion.UpperBody, p.TreeFlags, 1), "ilk gövde zırhsız");
            Assert.IsFalse(RunTreeHooks.IgnoresArmor(ClassicWeaponId.Sr1, BodyRegion.UpperBody, p.TreeFlags, 2), "ikinci gövdede zırh işler");
            Assert.IsFalse(RunTreeHooks.IgnoresArmor(ClassicWeaponId.Sr1, BodyRegion.Head, p.TreeFlags, 2), "mühür yok: ikinci kafa da zırhlı");
        }

        // ------------------------------------------------------------------ ar_1 / ar_2

        [Test]
        public void Bitirici_ar_1_HerBesinciMermiZirhDelerSinirsiz()
        {
            Sahip(ClassicWeaponId.AssaultRifle, "cap_ar_1");
            int isaretli = 0;
            for (int mermi = 1; mermi <= 10; mermi++)
            {
                var p = new ProjectileState { WeaponId = ClassicWeaponId.AssaultRifle, TargetsLeft = 2 };
                RunTreeHooks.OnProjectileSpawned(ref p);
                bool beklenen = mermi % 5 == 0;
                Assert.AreEqual(beklenen, (p.TreeFlags & RunTreeHooks.TreeRoundIgnoresArmor) != 0, "mermi " + mermi);
                Assert.AreEqual(beklenen ? RunTreeHooks.UnlimitedPierceTargets : 2, p.TargetsLeft, "mermi " + mermi);
                if (beklenen)
                {
                    isaretli++;
                    Assert.IsTrue(RunTreeHooks.IgnoresArmor(ClassicWeaponId.AssaultRifle, BodyRegion.UpperBody, p.TreeFlags, 4), "her gövdede zırhsız");
                }
            }

            Assert.AreEqual(2, isaretli);
            RunTreeHooks.ResetHitEffectsState();
            Assert.IsFalse(RunTreeHooks.RoninMarkRound(ClassicWeaponId.AssaultRifle, 4));
            Assert.IsTrue(RunTreeHooks.RoninMarkRound(ClassicWeaponId.AssaultRifle, 5));
        }

        /// <summary>
        /// WP-069 (C-00 30.09, DEC-112): at a high fire rate (FR3 + ks_firerate + fork X) the bursts sit closer than
        /// BurstGapSeconds and the timing guess merged them; the runtime's BurstRound marks every burst's 3rd round. Nine
        /// spawns in one frame (zero gap): 1-2-3 ×3. Without a BurstRound the timing guess still answers.
        /// </summary>
        [Test]
        public void Bitirici_ar_2_YuksekAtisHizinda_HerSerininUcuncuMermisi()
        {
            Sahip(ClassicWeaponId.Ar2, "cap_ar_2");
            var sira = new System.Collections.Generic.List<int>();
            for (int seri = 0; seri < 3; seri++)
            {
                for (int tur = 1; tur <= 3; tur++)
                {
                    var p = new ProjectileState { WeaponId = ClassicWeaponId.Ar2, Shot = new ShotRequest { BurstRound = tur } };
                    RunTreeHooks.OnProjectileSpawned(ref p);
                    sira.Add(p.TreeOrdinal);
                    Assert.AreEqual(tur == 3, (p.TreeFlags & RunTreeHooks.TreeRoundThirdEye) != 0, "iz bayrağı yalnız 3. mermide (seri " + seri + ", tur " + tur + ")");
                }
            }

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 1, 2, 3, 1, 2, 3 }, sira, "her serinin 3. mermisi");
            var yedek = new ProjectileState { WeaponId = ClassicWeaponId.Ar2 };
            RunTreeHooks.OnProjectileSpawned(ref yedek);
            Assert.Greater(yedek.TreeOrdinal, 0, "BurstRound yok → zaman tahmini (yedek)");
        }

        [Test]
        public void Bitirici_ar_2_DEC112_HerSerininUcuncuMermisiSertVurupZincirler()
        {
            Sahip(ClassicWeaponId.Ar2, "cap_ar_2");
            Assert.AreEqual(1, RunTreeHooks.NextBurstOrdinal(10f));
            Assert.AreEqual(2, RunTreeHooks.NextBurstOrdinal(10.05f));
            Assert.AreEqual(3, RunTreeHooks.NextBurstOrdinal(10.10f));
            Assert.AreEqual(1, RunTreeHooks.NextBurstOrdinal(10.36f), "0,26 s ara: yeni seri");

            Assert.IsTrue(RunTreeHooks.ThirdEyeStrikes(ClassicWeaponId.Ar2, 3), "kafa şartı yok (DEC-112)");
            Assert.IsFalse(RunTreeHooks.ThirdEyeStrikes(ClassicWeaponId.Ar2, 2), "3. mermi değil");
            Assert.IsFalse(RunTreeHooks.ThirdEyeStrikes(ClassicWeaponId.Smg2, 3), "başka silah");
            Assert.AreEqual(1.3f, RunTreeHooks.ThirdEyeScale(), 1e-5f, "+%30 (kart args)");

            // Vuruş yolu: 3. mermi gövdeye (kafa değil) +%30 ve zincir bayrağı; olay ilk gövdede bir kez.
            int olay = 0;
            CapstoneProcInfo son = default;
            System.Action<CapstoneProcInfo> dinle = e => { olay++; son = e; };
            RunTreeHooks.CapstoneProc += dinle;
            try
            {
                var hedef = new DamageTarget { Id = 7 };
                var isabet = new DD.Prototype.Damage.DamageResult { HealthDamage = 5f };
                var p = new ProjectileState { WeaponId = ClassicWeaponId.Ar2, TreeOrdinal = 3 };
                p.TouchedCount = 1;   // ilk gövde (BeforeTreeHit anında sayılmış)
                Assert.AreEqual(1.3f, RunTreeHooks.BeforeTreeHit(ref p, in hedef, BodyRegion.UpperBody, Vector3.one), 1e-5f);
                Assert.AreEqual(RunTreeHooks.TreeHitChain, RunTreeHooks.AfterTreeHit(ref p, in hedef, BodyRegion.UpperBody, Vector3.one, Vector3.forward, in isabet) & RunTreeHooks.TreeHitChain);
                Assert.AreEqual(1, olay);
                Assert.AreEqual(CapstoneProcKind.ThirdEyeArc, son.Kind);
                Assert.AreEqual(ClassicWeaponId.Ar2, son.Weapon);
                Assert.AreEqual(Vector3.one, son.Point);

                p.TouchedCount = 2;
                RunTreeHooks.BeforeTreeHit(ref p, in hedef, BodyRegion.UpperBody, Vector3.one);
                Assert.AreEqual(1, olay, "delip geçtiği ikinci gövdede olay yok");

                var ikinci = new ProjectileState { WeaponId = ClassicWeaponId.Ar2, TreeOrdinal = 2 };
                Assert.AreEqual(1f, RunTreeHooks.BeforeTreeHit(ref ikinci, in hedef, BodyRegion.Head, Vector3.one), "2. mermi normal");
                Assert.AreEqual(0, RunTreeHooks.AfterTreeHit(ref ikinci, in hedef, BodyRegion.Head, Vector3.one, Vector3.forward, in isabet) & RunTreeHooks.TreeHitChain);
            }
            finally
            {
                RunTreeHooks.CapstoneProc -= dinle;
            }

            var chain = new ShotRequest();
            RunTreeHooks.ForcedChainRequest(ref chain);
            Assert.GreaterOrEqual(chain.ChainTargets, 1, "Zincir dalı yokken 1. kademe sayıları");
            Assert.Greater(chain.ChainRadius, 0f);
            Assert.Greater(chain.ChainDamageScale, 0f);
        }

        // ------------------------------------------------------------------ smg_2

        [Test]
        public void Muhur_smg_2_SersemlemeUzarKosucuDuser()
        {
            Sahip(ClassicWeaponId.Smg2, "seal_smg_2");
            Assert.AreEqual(0.7f, RunTreeHooks.StaggerSecondsFor(ClassicWeaponId.Smg2, 0.45f), 1e-4f, "0,45 → 0,7 s");
            Assert.AreEqual(0.45f, RunTreeHooks.StaggerSecondsFor(ClassicWeaponId.AssaultRifle, 0.45f), "başka silahın sersemletmesi aynı");
            Assert.IsTrue(RunTreeHooks.RunnerFalls(ClassicWeaponId.Smg2, EnemyTier.Special), "Koşucu düşer");
            Assert.IsFalse(RunTreeHooks.RunnerFalls(ClassicWeaponId.Smg2, EnemyTier.Standard));
            Assert.IsFalse(RunTreeHooks.RunnerFalls(ClassicWeaponId.Ar2, EnemyTier.Special));
        }

        [Test]
        public void Catal_smg_2_x_SersemlemeUzar()
        {
            Sahip(ClassicWeaponId.Smg2, "fork_smg_2_x");
            Assert.AreEqual(0.45f * 1.3f, RunTreeHooks.StaggerSecondsFor(ClassicWeaponId.Smg2, 0.45f), 1e-4f, "+%30");
            Sahip(ClassicWeaponId.Smg2, "seal_smg_2", "fork_smg_2_x");
            Assert.AreEqual(0.7f * 1.3f, RunTreeHooks.StaggerSecondsFor(ClassicWeaponId.Smg2, 0.45f), 1e-4f, "mühür × çatal");
            Assert.IsFalse(RunTreeHooks.RunnerFalls(ClassicWeaponId.Smg2, EnemyTier.Standard));
        }

        [Test]
        public void Bitirici_smg_2_SersemlemisHedefeUcuncuVurusZincir()
        {
            Sahip(ClassicWeaponId.Smg2, "cap_smg_2");
            Assert.IsFalse(RunTreeHooks.LightningFangChains(ClassicWeaponId.Smg2, false), "sersemlemeyen hedef sayılmaz");
            bool[] beklenen = { false, false, true, false, false, true };
            for (int i = 0; i < beklenen.Length; i++)
            {
                Assert.AreEqual(beklenen[i], RunTreeHooks.LightningFangChains(ClassicWeaponId.Smg2, true), "vuruş " + (i + 1));
            }

            Assert.IsFalse(RunTreeHooks.LightningFangChains(ClassicWeaponId.AutoSmg, true), "smg_1 sayılmaz");

            // DEC-112: sendeleyen gövde smg_2'den +%25 alır (StaggeredScale'de ks.impact ile toplanır).
            Assert.AreEqual(0.25f, RunTreeHooks.LightningFangBonus(ClassicWeaponId.Smg2), 1e-5f);
            Assert.AreEqual(0f, RunTreeHooks.LightningFangBonus(ClassicWeaponId.AutoSmg), "başka silah");
            Assert.AreEqual(1f, RunTreeHooks.StaggeredScale(null), "gövde yok");
        }

        // ------------------------------------------------------------------ sg_1 / sg_2 / lmg_2

        [Test]
        public void Bitirici_sg_1_YakindaItmeVeZirhKirar()
        {
            Sahip(ClassicWeaponId.CombatShotgun, "cap_sg_1");
            Assert.IsTrue(RunTreeHooks.AshWallApplies(ClassicWeaponId.CombatShotgun, 2.9f), "3 m içinde");
            Assert.IsFalse(RunTreeHooks.AshWallApplies(ClassicWeaponId.CombatShotgun, 3.1f), "3 m dışında");
            Assert.IsFalse(RunTreeHooks.AshWallApplies(ClassicWeaponId.Sg2, 1f), "başka silah");

            Assert.IsTrue(RunTreeHooks.AshWallFirstThisShot(100, 7), "atışta düşmana ilk saçma");
            Assert.IsFalse(RunTreeHooks.AshWallFirstThisShot(100, 7), "aynı atışta ikinci saçma yok");
            Assert.IsTrue(RunTreeHooks.AshWallFirstThisShot(100, 8), "başka düşman");
            Assert.IsTrue(RunTreeHooks.AshWallFirstThisShot(101, 7), "yeni atış");
        }

        [Test]
        public void Catal_sg_2_x_YakinHasarArtar()
        {
            Sahip(ClassicWeaponId.Sg2, "fork_sg_2_x");
            Assert.AreEqual(1.2f, RunTreeHooks.CloseRangeScale(ClassicWeaponId.Sg2, 3.9f), 1e-5f, "4 m içinde +%20");
            Assert.AreEqual(1f, RunTreeHooks.CloseRangeScale(ClassicWeaponId.Sg2, 4.1f));
            Assert.AreEqual(1f, RunTreeHooks.CloseRangeScale(ClassicWeaponId.CombatShotgun, 1f), "başka silah");
        }

        [Test]
        public void Bitirici_sg_2_IkinciNamluAyniHedefiPatlatir()
        {
            Sahip(ClassicWeaponId.Sg2, "cap_sg_2");

            // Mühürsüz: sol namlu (dolu şarjör) sonra sağ namlu, iki tetik.
            Assert.AreEqual(0, RunTreeHooks.TwinBarrelOf(1, 0, 10, 0), "dolu şarjör: sol namlu");
            int cift = RunTreeHooks.TwinBarrelPair;
            Assert.IsFalse(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 0, cift, 7));
            Assert.AreEqual(1, RunTreeHooks.TwinBarrelOf(2, 3, 10, 1), "bir mermi atılmış: sağ namlu");
            Assert.AreEqual(cift, RunTreeHooks.TwinBarrelPair, "aynı çift");
            Assert.IsTrue(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 1, cift, 7), "iki namlu aynı hedef: patlar");
            Assert.IsFalse(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 1, cift, 7), "hedef başına bir kez");
            Assert.IsFalse(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 1, cift, 8), "sol namlunun vurmadığı hedef");

            // Mühürlü: tek tetikte 20 saçma, 10'dan sonrası sağ namlu; yeni çift açılır.
            Assert.AreEqual(0, RunTreeHooks.TwinBarrelOf(3, 9, 10, 0));
            Assert.AreEqual(cift + 1, RunTreeHooks.TwinBarrelPair, "sol namluyla yeni çift");
            Assert.AreEqual(1, RunTreeHooks.TwinBarrelOf(3, 12, 10, 0), "aynı tetik, 12. saçma: sağ namlu");
            Assert.IsFalse(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 1, cift, 7), "eski çiftin saçması sayılmaz");
            Assert.IsFalse(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 1, cift + 1, 7), "yeni çiftte sol namlu henüz vurmadı");
            Assert.IsFalse(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 0, cift + 1, 7));
            Assert.IsTrue(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.Sg2, 1, cift + 1, 7));
            Assert.IsFalse(RunTreeHooks.TwinBarrelHit(ClassicWeaponId.CombatShotgun, 1, cift + 1, 9), "başka silah");
        }

        [Test]
        public void Bitirici_lmg_2_DEC112_ZirhKirinciSendeletirHasarVeGecisYok()
        {
            Sahip(ClassicWeaponId.Lmg2, "cap_lmg_2");
            Assert.IsTrue(RunTreeHooks.BlackIronStaggers(ClassicWeaponId.Lmg2, true));
            Assert.IsFalse(RunTreeHooks.BlackIronStaggers(ClassicWeaponId.Lmg2, false), "plaka kırılmadı");
            Assert.IsFalse(RunTreeHooks.BlackIronStaggers(ClassicWeaponId.Lmg1, true), "başka silah");

            // Zombi olmayan alıcı: sendeleme yok, eski geçiş (TargetsLeft++ / TreeHitKeepDamage) ve hasar çarpanı yok.
            var hedef = new DamageTarget { Id = 3 };
            var kirdi = new DD.Prototype.Damage.DamageResult { ArmorDamage = 10f, ArmorBroke = true };
            var p = new ProjectileState { WeaponId = ClassicWeaponId.Lmg2, TargetsLeft = 1 };
            Assert.AreEqual(1f, RunTreeHooks.BeforeTreeHit(ref p, in hedef, BodyRegion.UpperBody, Vector3.zero));
            Assert.AreEqual(0, RunTreeHooks.AfterTreeHit(ref p, in hedef, BodyRegion.UpperBody, Vector3.zero, Vector3.forward, in kirdi));
            Assert.AreEqual(1, p.TargetsLeft, "geçiş kalktı");
        }

        // ------------------------------------------------------------------ rl_1 / Ash Cloud

        [Test]
        public void Catallar_rl_1_YaricapVeHasar()
        {
            Sahip(ClassicWeaponId.Rl1, "fork_rl_1_x");
            float hasar = 160f, ic = 1.8f, dis = 5f;
            RunTreeHooks.ShapeRocketBlast(ClassicWeaponId.Rl1, ref hasar, ref ic, ref dis);
            Assert.AreEqual(240f, hasar, 1e-3f, "+%50 hasar");
            Assert.AreEqual(1.44f, ic, 1e-4f, "yarıçap ×0,80");
            Assert.AreEqual(4f, dis, 1e-4f);

            Sahip(ClassicWeaponId.Rl1, "fork_rl_1_y");
            hasar = 160f; ic = 1.8f; dis = 5f;
            RunTreeHooks.ShapeRocketBlast(ClassicWeaponId.Rl1, ref hasar, ref ic, ref dis);
            Assert.AreEqual(160f, hasar);
            Assert.AreEqual(5.75f, dis, 1e-4f, "yarıçap +%15");

            hasar = 160f; ic = 1.8f; dis = 5f;
            RunTreeHooks.ShapeRocketBlast(ClassicWeaponId.Sg2, ref hasar, ref ic, ref dis);
            Assert.AreEqual(5f, dis, "başka silah");
        }

        [Test]
        public void Bitirici_rl_1_PatlamaUceBolunur()
        {
            Sahip(ClassicWeaponId.Rl1, "cap_rl_1");
            Assert.AreEqual(3, RunTreeHooks.RocketSplitCount(ClassicWeaponId.Rl1));
            Assert.AreEqual(0, RunTreeHooks.RocketSplitCount(ClassicWeaponId.Ar2));
            Assert.AreEqual(1f, RunTreeHooks.RocketSplitDamageScale, "DEC-106: küçük patlamalar tam hasar");
            Assert.AreEqual(0.6f, RunTreeHooks.RocketSplitRadiusScale, "yarıçap ×0,6 değişmedi");
        }

        [Test]
        public void Muhur_rl_1_PatlamaAlaniYanar()
        {
            Sahip(ClassicWeaponId.Rl1, "seal_rl_1");
            Assert.IsTrue(RunTreeHooks.HibanaFor(ClassicWeaponId.Rl1, out float dps, out float sure));
            Assert.AreEqual(4f, dps);
            Assert.AreEqual(3f, sure);
            Assert.IsFalse(RunTreeHooks.HibanaFor(ClassicWeaponId.Sg2, out _, out _));

            RunTreeHooks.OnRocketBlast(ClassicWeaponId.Rl1, new Vector3(1f, 0f, 2f), 160f, 5f);
            Assert.AreEqual(1, RunTreeGroundFire.Count);
            GroundFireInfo z = RunTreeGroundFire.Get(0);
            Assert.AreEqual(4f, z.DamagePerSecond);
            Assert.AreEqual(5f, z.Radius);
            Assert.AreEqual(3f, z.Seconds);
            Assert.AreEqual(new Vector3(1f, 0f, 2f), z.Center);

            RunTreeGroundFire.Clear();
            RunTreeHooks.OnClassicExplosion(Vector3.zero, 40f, 3f);
            Assert.AreEqual(0, RunTreeGroundFire.Count, "Kül Bulutu yok: klasik patlama yakmaz");
        }

        [Test]
        public void KulBulutu_HerPatlamaZeminBirakir()
        {
            Sahip(ClassicWeaponId.Rl1, "ks_explosion");   // Patlama rl_1'in çekirdek dalı (sg_1 / sg_2 / rl_1)
            Assert.IsTrue(RunTreeHooks.AshCloudFor(160f, out float dps, out float sure));
            Assert.AreEqual(40f, dps, 1e-4f, "%25 × 160");
            Assert.AreEqual(2f, sure);

            RunTreeHooks.OnClassicExplosion(Vector3.zero, 20f, 3f);
            Assert.AreEqual(1, RunTreeGroundFire.Count);
            Assert.AreEqual(5f, RunTreeGroundFire.Get(0).DamagePerSecond, 1e-4f);
            Assert.AreEqual(3f, RunTreeGroundFire.Get(0).Radius);

            RunTreeHooks.OnRocketBlast(ClassicWeaponId.Rl1, Vector3.one, 160f, 5f);
            Assert.AreEqual(2, RunTreeGroundFire.Count, "roket patlaması da (Hibana yok: tek bölge)");
        }

        // ------------------------------------------------------------------ yanan zemin havuzu

        [Test]
        public void ZeminAtesi_EkleTikleSon()
        {
            int sondu = 0;
            System.Action<int> dinle = id => sondu++;
            RunTreeGroundFire.Extinguished += dinle;
            try
            {
                Assert.AreEqual(0, RunTreeGroundFire.Ignite(Vector3.zero, 0f, 5f, 2f), "yarıçap 0: yanmaz");
                Assert.AreEqual(0, RunTreeGroundFire.Ignite(Vector3.zero, 3f, 0f, 2f), "hasar 0: yanmaz");
                Assert.AreEqual(0, RunTreeGroundFire.Ignite(Vector3.zero, 3f, 5f, 0f), "süre 0: yanmaz");
                Assert.AreEqual(0, RunTreeGroundFire.Count);

                int id = RunTreeGroundFire.Ignite(new Vector3(0f, -500f, 0f), 3f, 5f, 2f);
                Assert.Greater(id, 0);
                Assert.AreEqual(1, RunTreeGroundFire.Count);

                int tik = RunTreeGroundFire.BurnTicks;
                RunTreeGroundFire.Tick(0.49f);
                Assert.AreEqual(tik, RunTreeGroundFire.BurnTicks, "0,5 s dolmadan yakmaz");
                RunTreeGroundFire.Tick(0.01f);
                Assert.AreEqual(tik + 1, RunTreeGroundFire.BurnTicks, "0,5 s: ilk yakma");
                Assert.AreEqual(1.5f, RunTreeGroundFire.Get(0).SecondsLeft, 1e-3f);

                for (int i = 0; i < 3; i++) RunTreeGroundFire.Tick(0.5f);
                Assert.AreEqual(tik + 4, RunTreeGroundFire.BurnTicks, "2 s: dört yakma");
                Assert.AreEqual(0, RunTreeGroundFire.Count, "süre bitti: söndü");
                Assert.AreEqual(1, sondu);
            }
            finally
            {
                RunTreeGroundFire.Extinguished -= dinle;
            }
        }

        [Test]
        public void ZeminAtesi_UzunKareTumTikleriYakar()
        {
            RunTreeGroundFire.Ignite(new Vector3(0f, -500f, 0f), 3f, 4f, 3f);
            int tik = RunTreeGroundFire.BurnTicks;
            RunTreeGroundFire.Tick(5f);
            Assert.AreEqual(tik + 6, RunTreeGroundFire.BurnTicks, "3 s bölge: altı yakma, fazlası yok");
            Assert.AreEqual(0, RunTreeGroundFire.Count);
        }

        [Test]
        public void ZeminAtesi_KapasiteVeTemizlik()
        {
            int degisti = RunTreeGroundFire.Replaced;
            for (int i = 0; i < RunTreeGroundFire.Capacity; i++)
            {
                Assert.Greater(RunTreeGroundFire.Ignite(new Vector3(i, -500f, 0f), 1f, 1f, 1f + i), 0);
            }

            Assert.AreEqual(RunTreeGroundFire.Capacity, RunTreeGroundFire.Count);
            int yeni = RunTreeGroundFire.Ignite(new Vector3(99f, -500f, 0f), 1f, 1f, 30f);
            Assert.Greater(yeni, 0, "dolu havuz: en az süresi kalan değişir");
            Assert.AreEqual(RunTreeGroundFire.Capacity, RunTreeGroundFire.Count);
            Assert.AreEqual(degisti + 1, RunTreeGroundFire.Replaced);

            bool enKisaVar = false, yeniVar = false;
            for (int i = 0; i < RunTreeGroundFire.Count; i++)
            {
                GroundFireInfo z = RunTreeGroundFire.Get(i);
                if (Mathf.Approximately(z.Seconds, 1f)) enKisaVar = true;
                if (z.Id == yeni) yeniVar = true;
            }

            Assert.IsFalse(enKisaVar, "1 s'lik bölge gitti");
            Assert.IsTrue(yeniVar);

            RunTreeGroundFire.Clear();
            Assert.AreEqual(0, RunTreeGroundFire.Count);
            RunTreeGroundFire.Tick(1f);
            Assert.AreEqual(0, RunTreeGroundFire.Count);
        }
    }
}
