using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension (WP-051C pattern)

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B2 · ağaç kancaları (TreeHooks, S-04 gövdeleri): her kanca nötr istatistikte sevk değerini döner ve rastgele sayı
    /// tüketmez; tipik değerler (GD-TREE §2); tavanlar (tetik 1 200 rpm → hasar, ağaç rpm ×3, tetik başı 24 mermi, saçmalı yelpaze
    /// ×1,6, mermi/sn 60, kaçınma %30); deterministik kritik + kötü şans koruması; Sonsuz Pencere öldürme sayar; kalkan emme / dolum /
    /// kırılma olayı; Ölümsüz Ant round başına 1; can emme saniye tavanı; wall buy / cephane fiyatı + Silah Tüccarı; taramalı roketatar
    /// 12'lik kutu, yedeği ve 3,0 sn dolumu; sıcak yol 0 B.
    /// </summary>
    [TestFixture]
    public sealed class TreeHooksTests
    {
        private readonly List<TreeProcInfo> _procs = new List<TreeProcInfo>();

        private static TreeWeaponProfile Ar => new TreeWeaponProfile
        {
            Id = ClassicWeaponId.AssaultRifle, Key = "ar_1", FireMode = WeaponFireMode.Auto, BaseRpm = 600f, BaseDamage = 12f, Pellets = 1, Magazine = 30
        };

        private static TreeWeaponProfile Smg => new TreeWeaponProfile
        {
            Id = ClassicWeaponId.AutoSmg, Key = "smg_1", FireMode = WeaponFireMode.Auto, BaseRpm = 900f, BaseDamage = 8f, Pellets = 1, Magazine = 42
        };

        private static TreeWeaponProfile Sg1 => new TreeWeaponProfile
        {
            Id = ClassicWeaponId.CombatShotgun, Key = "sg_1", FireMode = WeaponFireMode.Single, BaseRpm = 72f, BaseDamage = 9f, Pellets = 8, Magazine = 7,
            Projectile = ProjectileKind.Pellet
        };

        private static TreeWeaponProfile Sg2 => new TreeWeaponProfile
        {
            Id = ClassicWeaponId.Sg2, Key = "sg_2", FireMode = WeaponFireMode.Single, BaseRpm = 200f, BaseDamage = 11f, Pellets = 10, Magazine = 2,
            Projectile = ProjectileKind.Pellet
        };

        private static TreeWeaponProfile Sr => new TreeWeaponProfile
        {
            Id = ClassicWeaponId.Sr1, Key = "sr_1", FireMode = WeaponFireMode.Single, BaseRpm = 48f, BaseDamage = 80f, Pellets = 1, Magazine = 5
        };

        private static TreeWeaponProfile Rl => new TreeWeaponProfile
        {
            Id = ClassicWeaponId.Rl1, Key = "rl_1", FireMode = WeaponFireMode.Single, BaseRpm = 20f, BaseDamage = 110f, Pellets = 1, Magazine = 1,
            Projectile = ProjectileKind.Rocket
        };

        private static TreeWeaponProfile[] All => new[] { Ar, Smg, Sg1, Sg2, Sr, Rl };

        [SetUp]
        public void SetUp()
        {
            TreeCatalogV2.SetForTests(null);
            TreeRuntime.ClearTestStats();
            TreeRuntime.Reset();
            TreeHooks.ResetRunState();
            TreeEvents.ResetForTests();
            TreeEvents.ResetCounters();
            _procs.Clear();
            TreeEvents.Proc += OnProc;
        }

        [TearDown]
        public void TearDown()
        {
            TreeEvents.ResetForTests();
            TreeRuntime.ClearTestStats();
            TreeRuntime.Reset();
            TreeHooks.ResetRunState();
        }

        private void OnProc(TreeProcInfo info) => _procs.Add(info);

        private static void Use(in TreeStats stats) => TreeRuntime.SetTestStats(stats);

        private int Count(TreeProcKind kind)
        {
            int n = 0;
            for (int i = 0; i < _procs.Count; i++) if (_procs[i].Kind == kind) n++;
            return n;
        }

        private static float PullDamage(in TreeVolleyPlan plan)
        {
            float sum = 0f;
            for (int v = 0; v < plan.Count; v++) sum += plan.PelletsPerVolley * plan.DamageScale(v);
            return sum;
        }

        // ------------------------------------------------------------------ neutral = shipped

        [Test]
        public void Notr_AtisKancalari_SevkDegeriniDoner()
        {
            foreach (TreeWeaponProfile w in All)
            {
                TreeHooks.PlanVolley(w, w.Pellets, 20f, out TreeVolleyPlan plan);
                Assert.AreEqual(1, plan.Count, w.Key);
                Assert.AreEqual(w.Pellets, plan.PelletsPerVolley, w.Key);
                Assert.AreEqual(1f, plan.MergedDamageScale, w.Key);
                Assert.AreEqual(1f, plan.MergedSizeScale, w.Key);
                Assert.AreEqual(0f, plan.YawDegrees(0), w.Key);
                Assert.AreEqual(1f, plan.DamageScale(0), w.Key);
                Assert.AreEqual(1, TreeHooks.VolleyCount(w));
                Assert.AreEqual(1f, TreeHooks.ShotDamageScale(w, 10f, 5f, 99999, 1, w.Magazine), w.Key + " hasar");
                Assert.AreEqual(1f, TreeHooks.FireRateScale(w, 10f, 5f), w.Key + " atış hızı");
                Assert.AreEqual(TreeFeelOverride.None, TreeHooks.PowerOverride(w));
                Assert.AreEqual(WeaponPower.Heavy, TreeHooks.ShotPower(WeaponPower.Heavy, w));
                Assert.IsFalse(TreeHooks.RocketSelfImmune(w));
                Assert.IsFalse(TreeHooks.IsConverted(w));
            }

            Assert.AreEqual(0, TreeEvents.ShapedShots, "nötr çekiş ağaç sayacını artırmaz");
            Assert.AreEqual(WeaponFireMode.Single, TreeHooks.FireMode(WeaponFireMode.Single));
            Assert.AreEqual(WeaponFireMode.Burst, TreeHooks.FireMode(WeaponFireMode.Burst));
            Assert.AreEqual(WeaponFireMode.Auto, TreeHooks.FireMode(WeaponFireMode.Auto));
            Assert.AreEqual(1f, TreeHooks.ProjectileSizeScale);
            Assert.AreEqual(3, TreeHooks.PierceCount(3));
            Assert.AreEqual(0.8f, TreeHooks.PierceKeep(0.8f));
            Assert.AreEqual(2, TreeHooks.RicochetBounces(2));
            Assert.AreEqual(4f, TreeHooks.RicochetRadius(4f));
            Assert.AreEqual(0.6f, TreeHooks.RicochetKeep(0.6f));
            Assert.AreEqual(0.25f, TreeHooks.RicochetChance(0.25f));
            Assert.IsTrue(TreeHooks.VolleyCarriesRicochet(0));
            Assert.AreEqual(0f, TreeHooks.SeekDegrees);
            Assert.IsFalse(TreeHooks.ExplosiveRound(out float r, out float a));
            Assert.AreEqual(0f, r);
            Assert.AreEqual(0f, a);
            Assert.AreEqual(1f, TreeHooks.OwnBlastRadiusScale);
            Assert.AreEqual(0f, TreeHooks.VolleyYawDegrees(0, 1));
            Assert.AreEqual(1f, TreeHooks.VolleyDamageScale(0));
        }

        [Test]
        public void Notr_SarjorKancalari_SevkDegeriniDoner()
        {
            foreach (TreeWeaponProfile w in All)
            {
                Assert.AreEqual(w.Magazine, TreeHooks.MagazineSize(w.Magazine, w), w.Key);
                Assert.AreEqual(1f, TreeHooks.ReserveScale(w.Magazine, w), w.Key);
                Assert.AreEqual(1f, TreeHooks.ReloadTimeScale(w, 2.8f), w.Key);
            }

            Assert.AreEqual(8, TreeHooks.ReserveAllowance(8, Rl));
            Assert.AreEqual(-1, TreeHooks.ReserveAllowance(-1, Ar), "sonsuz yedek");
            Assert.IsFalse(TreeHooks.ReloadMoveFree);
            float carry = 0.4f;
            Assert.AreEqual(0, TreeHooks.AutoReloadTick(1f, 10f, 5, 30, ref carry));
            Assert.AreEqual(0f, carry);
            for (int i = 0; i < 100; i++) TreeHooks.OnKill(1f);
            Assert.AreEqual(0, TreeHooks.EndlessKillCount);
            Assert.IsFalse(TreeHooks.NoReloadWindow(1f));
            Assert.IsFalse(TreeHooks.TwinFreeRound(2));
            Assert.IsTrue(TreeHooks.ConsumesAmmo(2, 1f));
            CollectionAssert.IsEmpty(_procs);
        }

        [Test]
        public void Notr_HayattaKalmaVeEkonomi_SevkDegeriniDoner()
        {
            Assert.AreEqual(1f, TreeHooks.MaxHealthScale);
            Assert.AreEqual(1f, TreeHooks.RegenDelayScale);
            Assert.AreEqual(1f, TreeHooks.MoveScale(5f));
            TreeHooks.OnKill(5f);
            Assert.AreEqual(1f, TreeHooks.MoveScale(5.1f), "öldürme hızı yok");
            Assert.AreEqual(0.8f, TreeHooks.FiringMoveFactor(0.8f));
            Assert.AreEqual(0f, TreeHooks.ShieldMax(100f));

            TreeShieldState st = TreeShieldState.Create(12345u);
            TreeHitResult hit = TreeHooks.IncomingDamage(30f, 1f, 100f, 100f, ref st);
            Assert.AreEqual(30f, hit.Damage);
            Assert.AreEqual(TreeHitOutcome.None, hit.Outcome);
            Assert.AreEqual(0f, hit.Absorbed);
            Assert.AreEqual(0f, hit.HealthFloor);
            hit = TreeHooks.IncomingDamage(150f, 2f, 100f, 100f, ref st);
            Assert.AreEqual(150f, hit.Damage, "ölümcül vuruş nötrde aynen geçer");
            Assert.AreEqual(TreeHitOutcome.None, hit.Outcome);
            Assert.AreEqual(12345u, st.Rng, "nötrde kaçınma zarı atılmaz");
            Assert.AreEqual(0f, TreeHooks.OnKillHeal(EnemyTier.Ultra, 1f));

            Assert.AreEqual(1f, TreeHooks.MagnetRadiusScale);
            Assert.AreEqual(2500, TreeHooks.WallBuyPrice(2500));
            Assert.AreEqual(0, TreeHooks.WallBuyPrice(0));
            Assert.AreEqual(1250, TreeHooks.AmmoPrice(2500, 0.5f));
            Assert.AreEqual(WallBuyRules.RefillCost(2500, 0.5f), TreeHooks.AmmoPrice(2500, 0.5f));
            Assert.AreEqual(WallBuyRules.RefillCost(45, 0.5f), TreeHooks.AmmoPrice(45, 0.5f));
            Assert.AreEqual(0, TreeHooks.AmmoPrice(0, 0.5f));
            CollectionAssert.IsEmpty(_procs);
        }

        [Test]
        public void Notr_Kritik_AtilmazVeRngDegismez()
        {
            uint rng = 777u;
            for (int i = 0; i < 50; i++)
            {
                Assert.IsFalse(TreeHooks.RollCrit(ref rng, out float mult));
                Assert.AreEqual(1f, mult);
            }

            Assert.AreEqual(777u, rng);
            Assert.AreEqual(0, TreeHooks.PullsSinceCrit);
        }

        // ------------------------------------------------------------------ volley

        [Test]
        public void Volley_CiftAtis_IkiMermiAltiDereceAyri()
        {
            TreeStats s = TreeStats.Neutral;
            s.ExtraShots = 1;
            s.ExtraShotDamage = 0.6f;
            s.FanArcDegrees = 6f;
            Use(s);

            TreeHooks.PlanVolley(Ar, 1, out TreeVolleyPlan plan);
            Assert.AreEqual(1, TreeEvents.ShapedShots, "çift atış: çekiş başına bir sayım");
            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(1, plan.PelletsPerVolley);
            Assert.AreEqual(-3f, plan.YawDegrees(0), 1e-5f);
            Assert.AreEqual(3f, plan.YawDegrees(1), 1e-5f);
            Assert.AreEqual(1f, plan.DamageScale(0));
            Assert.AreEqual(0.6f, plan.DamageScale(1), 1e-5f);
            Assert.AreEqual(1f, plan.MergedDamageScale);
            Assert.AreEqual(-3f, TreeHooks.VolleyYawDegrees(0, 2), 1e-5f);
            Assert.AreEqual(0.6f, TreeHooks.VolleyDamageScale(1), 1e-5f);
        }

        [Test]
        public void Volley_UcluAtis_AnaMerkezdeYanlarSimetrik()
        {
            TreeStats s = TreeStats.Neutral;
            s.ExtraShots = 2;
            s.ExtraShotDamage = 0.55f;
            s.FanArcDegrees = 12f;
            Use(s);

            TreeHooks.PlanVolley(Ar, 1, out TreeVolleyPlan plan);
            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(0f, plan.YawDegrees(0), 1e-5f);
            Assert.AreEqual(-6f, plan.YawDegrees(1), 1e-5f);
            Assert.AreEqual(6f, plan.YawDegrees(2), 1e-5f);
            Assert.AreEqual(0.55f, plan.DamageScale(2), 1e-5f);
            Assert.AreEqual(2.1f, PullDamage(plan), 1e-4f, "GD sh_triple ×2,1");
        }

        [Test]
        public void Volley_EjderYelpazesi_BesMermiYayIcinde()
        {
            TreeStats s = TreeStats.Neutral;
            s.ExtraShots = 4;
            s.ExtraShotDamage = 0.45f;
            s.FanArcDegrees = 40f;
            Use(s);

            TreeHooks.PlanVolley(Ar, 1, out TreeVolleyPlan plan);
            Assert.AreEqual(5, plan.Count);
            float[] want = { 0f, -10f, 10f, -20f, 20f };
            for (int i = 0; i < 5; i++) Assert.AreEqual(want[i], plan.YawDegrees(i), 1e-4f, "yelpaze " + i);
            Assert.AreEqual(2.8f, PullDamage(plan), 1e-4f, "GD sh_fan kalabalıkta ×2,8");
        }

        [Test]
        public void Volley_EkAtisTavani_ToplamBes()
        {
            TreeStats s = TreeStats.Neutral;
            s.ExtraShots = 9;
            s.ExtraShotDamage = 0.45f;
            s.FanArcDegrees = 40f;
            Use(s);

            Assert.AreEqual(1 + TreeGuardrails.ExtraShotsMax, TreeHooks.VolleyCount(Ar));
            TreeHooks.PlanVolley(Ar, 1, out TreeVolleyPlan plan);
            Assert.AreEqual(5, plan.Count);
        }

        [Test]
        public void Volley_SacmaliCiftAtis_ButunSacmalarKalir()
        {
            TreeStats s = TreeStats.Neutral;
            s.ExtraShots = 1;
            s.ExtraShotDamage = 0.6f;
            s.FanArcDegrees = 6f;
            Use(s);

            TreeHooks.PlanVolley(Sg1, Sg1.Pellets, out TreeVolleyPlan plan);
            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(8, plan.PelletsPerVolley);
            Assert.AreEqual(16, plan.TotalShots, "GD V1: pompalıdan 16 saçma");
            Assert.AreEqual(1f, plan.MergedDamageScale);
            Assert.IsFalse(plan.Merged);
        }

        [Test]
        public void Volley_SacmaliYelpaze_SacmaSayisiBirAltiKatSiniri_HasarKorunur()
        {
            TreeStats s = TreeStats.Neutral;
            s.ExtraShots = 4;
            s.ExtraShotDamage = 0.45f;
            s.FanArcDegrees = 40f;
            Use(s);

            TreeHooks.PlanVolley(Sg1, Sg1.Pellets, out TreeVolleyPlan plan);
            Assert.AreEqual(5, plan.Count, "yönler korunur");
            Assert.LessOrEqual(plan.TotalShots, Mathf.RoundToInt(8 * TreeHooks.PelletFanShare), "GD §2.9: 8 × 1,6 → 13");
            Assert.IsTrue(plan.Merged);
            Assert.AreEqual(8f * 2.8f, PullDamage(plan), 1e-3f, "K6: birleşen saçma gücü hasara taşır");
            Assert.AreEqual(TreeHooks.MergedSizeMax, plan.MergedSizeScale, 1e-5f);
        }

        [Test]
        public void Volley_TetikBasiYirmiDortTavani()
        {
            TreeStats s = TreeStats.Neutral;
            s.ExtraShots = 2;
            s.ExtraShotDamage = 0.55f;
            s.FanArcDegrees = 12f;
            Use(s);

            TreeHooks.PlanVolley(Sg2, Sg2.Pellets, out TreeVolleyPlan plan);
            Assert.AreEqual(3, plan.Count);
            Assert.LessOrEqual(plan.TotalShots, TreeGuardrails.ShotsPerPull, "10 × 3 = 30 → ≤ 24");
            Assert.AreEqual(10f * 2.1f, PullDamage(plan), 1e-3f);
            Assert.Greater(plan.MergedSizeScale, 1f);
        }

        [Test]
        public void Volley_MermiSaniyeTavani_YonlerBirlesir_HasarKorunur()
        {
            TreeStats s = TreeStats.Neutral;
            s.ExtraShots = 4;
            s.ExtraShotDamage = 0.45f;
            s.FanArcDegrees = 40f;
            Use(s);

            TreeHooks.PlanVolley(Smg, 1, 20f, out TreeVolleyPlan plan);   // 1 200 rpm
            Assert.LessOrEqual(plan.TotalShots * 20f, TreeGuardrails.ProjectileSpawnsPerSecond);
            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(2.8f, PullDamage(plan), 1e-4f);
        }

        // ------------------------------------------------------------------ projectile

        [Test]
        public void Mermi_BoyutDelmeSekme_TipikVeTavan()
        {
            TreeStats s = TreeStats.Neutral;
            s.ProjectileScale = 1.5f;
            s.PierceCount = 2;
            s.PierceKeep = 0.75f;
            s.RicochetBounces = 1;
            s.RicochetRadius = 6f;
            s.RicochetKeep = 0.7f;
            s.SeekDegrees = 25f;
            Use(s);

            Assert.AreEqual(1.5f, TreeHooks.ProjectileSizeScale);
            Assert.AreEqual(2, TreeHooks.PierceCount(0));
            Assert.AreEqual(5, TreeHooks.PierceCount(5), "silahın kendi delmesi korunur");
            Assert.AreEqual(0.75f, TreeHooks.PierceKeep(0.5f));
            Assert.AreEqual(0.9f, TreeHooks.PierceKeep(0.9f), "ağaç kötüleştirmez");
            Assert.AreEqual(1, TreeHooks.RicochetBounces(0));
            Assert.AreEqual(6f, TreeHooks.RicochetRadius(4f));
            Assert.AreEqual(0.7f, TreeHooks.RicochetKeep(0.5f));
            Assert.AreEqual(1f, TreeHooks.RicochetChance(0.25f), "ağaç sekmesi kesin");
            Assert.IsFalse(TreeHooks.VolleyCarriesRicochet(1), "Bilardo Fırtınası yok: ek atış sekmez");
            Assert.AreEqual(25f, TreeHooks.SeekDegrees);

            s.PierceCount = 20;
            s.RicochetBounces = 9;
            Use(s);
            s.Synergies = TreeSynergy.PinballStorm;
            Use(s);
            Assert.IsTrue(TreeHooks.VolleyCarriesRicochet(3), "Bilardo Fırtınası: ek atışlar da seker");
            Assert.AreEqual(TreeGuardrails.PierceMax, TreeHooks.PierceCount(0));
            Assert.AreEqual(TreeGuardrails.RicochetMax, TreeHooks.RicochetBounces(0));
        }

        [Test]
        public void PatlayanMermi_TipikVeRoketYaricapi()
        {
            TreeStats s = TreeStats.Neutral;
            s.ExplosiveRadius = 2f;
            s.ExplosiveAreaShare = 0.35f;
            s.OwnBlastRadiusScale = 1.3f;
            Use(s);

            Assert.IsTrue(TreeHooks.ExplosiveRound(out float radius, out float area));
            Assert.AreEqual(2f, radius);
            Assert.AreEqual(0.35f, area);
            Assert.AreEqual(1.3f, TreeHooks.OwnBlastRadiusScale);
        }

        // ------------------------------------------------------------------ damage / rate / mode

        [Test]
        public void Hasar_AgirMermiOcakMidasKuyruk_Carpilir()
        {
            TreeStats s = TreeStats.Neutral;
            s.DamageScale = 1.15f;
            s.HearthDamageScale = 1.04f;
            s.MidasGoldStep = 500;
            s.MidasDamageStep = 0.05f;
            s.MidasDamageMax = 0.4f;
            s.MagazineTailShare = 0.2f;
            s.MagazineTailDamage = 1.3f;
            Use(s);

            float body = 1.15f * 1.04f * 1.10f;
            Assert.AreEqual(body, TreeHooks.ShotDamageScale(Ar, 0f, 1f, 1200, 7, 30), 1e-4f, "kuyruk dışı: son 6 mermi değil");
            Assert.AreEqual(body * 1.3f, TreeHooks.ShotDamageScale(Ar, 0f, 1f, 1200, 6, 30), 1e-4f, "son %20 (6 mermi) +%30");
            Assert.AreEqual(1.15f * 1.04f * 1.3f, TreeHooks.ShotDamageScale(Ar, 0f, 1f, 499, 1, 30), 1e-4f, "499 altın: Midas yok");
        }

        [Test]
        public void Midas_KirkYuzdeTavani()
        {
            TreeStats s = TreeStats.Neutral;
            s.MidasGoldStep = 500;
            s.MidasDamageStep = 0.05f;
            s.MidasDamageMax = 0.4f;
            Use(s);

            Assert.AreEqual(1.4f, TreeHooks.ShotDamageScale(Ar, 0f, 1f, 10000, 30, 30), 1e-5f);
        }

        [Test]
        public void TaramaliRoket_HasarVeDevir()
        {
            TreeStats s = TreeStats.Neutral;
            s.FullAuto = true;
            s.FullAutoRpmScale = 10f;
            s.FullAutoDamageScale = 0.4f;
            Use(s);

            Assert.AreEqual(0.4f, TreeHooks.ShotDamageScale(Rl, 0f, 1f, 0, 12, 12), 1e-5f, "44/roket");
            Assert.AreEqual(10f, TreeHooks.FireRateScale(Rl, 0f, 1f), 1e-4f, "20 → 200 rpm");
            Assert.AreEqual(WeaponFireMode.Auto, TreeHooks.FireMode(WeaponFireMode.Single));
            Assert.AreEqual(WeaponFireMode.Auto, TreeHooks.FireMode(WeaponFireMode.Burst));
            Assert.IsTrue(TreeHooks.IsConverted(Rl));
            Assert.IsFalse(TreeHooks.IsConverted(Ar), "zaten otomatik");
            TreeHooks.PlanVolley(Rl, 1, out _);
            Assert.AreEqual(1, TreeEvents.ShapedShots, "taramalı dönüşüm tek volley'de de sayılır");
        }

        [Test]
        public void PowerOverride_TekMermiliDonusenOrta_SacmaliAgirKalir()
        {
            TreeStats s = TreeStats.Neutral;
            s.FullAuto = true;
            Use(s);

            Assert.AreEqual(TreeFeelOverride.NoHeavyFireShake | TreeFeelOverride.NoHitStop, TreeHooks.PowerOverride(Sr));
            Assert.AreEqual(WeaponPower.Medium, TreeHooks.ShotPower(WeaponPower.Heavy, Sr));
            Assert.AreEqual(TreeFeelOverride.NoHeavyFireShake | TreeFeelOverride.NoHitStop | TreeFeelOverride.LightBlastShake, TreeHooks.PowerOverride(Rl));
            Assert.AreEqual(TreeFeelOverride.None, TreeHooks.PowerOverride(Sg1), "saçmalı Heavy kalır (12.10 kararı)");
            Assert.AreEqual(WeaponPower.Heavy, TreeHooks.ShotPower(WeaponPower.Heavy, Sg1));
            Assert.AreEqual(TreeFeelOverride.None, TreeHooks.PowerOverride(Ar));
            Assert.IsTrue(TreeHooks.RocketSelfImmune(Rl));
            Assert.IsFalse(TreeHooks.RocketSelfImmune(Sr));
        }

        [Test]
        public void AtisHizi_Tipik()
        {
            TreeStats s = TreeStats.Neutral;
            s.FireRateScale = 1.4f;
            Use(s);

            Assert.AreEqual(1.4f, TreeHooks.FireRateScale(Ar, 0f, 1f), 1e-5f);
            Assert.AreEqual(1f, TreeHooks.ShotDamageScale(Ar, 0f, 1f, 0, 30, 30), 1e-5f, "tavan altı: hasara taşma yok");
        }

        [Test]
        public void AtisHizi_Tetik1200Tavani_FazlasiHasara()
        {
            TreeStats s = TreeStats.Neutral;
            s.FireRateScale = 1.4f;
            Use(s);

            float rate = TreeHooks.FireRateScale(Smg, 0f, 1f);
            Assert.AreEqual(TreeGuardrails.TriggerRpmMax, Smg.BaseRpm * rate, 1e-2f, "900 × 1,4 = 1 260 → 1 200");
            Assert.AreEqual(1260f / 1200f, TreeHooks.ShotDamageScale(Smg, 0f, 1f, 0, 42, 42), 1e-4f, "K6");
        }

        [Test]
        public void AtisHizi_AgacUcKatTavani_FazlasiHasara()
        {
            TreeStats s = TreeStats.Neutral;
            s.FireRateScale = 2.5f;
            s.OverclockStep = 0.08f;
            s.OverclockMax = 0.6f;
            Use(s);

            for (int i = 0; i <= 40; i++) TreeHooks.OnShotFired(i * 0.1f);   // 4 s of sustained fire
            Assert.AreEqual(TreeGuardrails.TreeFireRateScaleMax, TreeHooks.FireRateScale(Sr, 0f, 4.05f), 1e-4f, "2,5 × 1,6 = 4 → 3");
            Assert.AreEqual(4f / 3f, TreeHooks.ShotDamageScale(Sr, 0f, 4.05f, 0, 5, 5), 1e-4f);
        }

        [Test]
        public void AsiriHiz_AdimTavanVeZirveOlayiBirKez()
        {
            TreeStats s = TreeStats.Neutral;
            s.OverclockStep = 0.08f;
            s.OverclockMax = 0.6f;
            Use(s);

            TreeHooks.OnShotFired(0f);
            Assert.AreEqual(1f, TreeHooks.FireRateScale(Ar, 0f, 0.2f), 1e-5f);
            for (int i = 1; i <= 10; i++) TreeHooks.OnShotFired(i * 0.1f);
            Assert.AreEqual(1.16f, TreeHooks.FireRateScale(Ar, 0f, 1.05f), 1e-5f, "1 sn: 2 adım");
            for (int i = 11; i <= 37; i++) TreeHooks.OnShotFired(i * 0.1f);
            Assert.AreEqual(1.56f, TreeHooks.FireRateScale(Ar, 0f, 3.75f), 1e-5f);
            Assert.AreEqual(0, Count(TreeProcKind.OverclockPeak));
            for (int i = 38; i <= 40; i++) TreeHooks.OnShotFired(i * 0.1f);
            Assert.AreEqual(1.6f, TreeHooks.FireRateScale(Ar, 0f, 4.05f), 1e-5f, "tavan +%60");
            TreeHooks.FireRateScale(Ar, 0f, 4.5f);
            Assert.AreEqual(1, Count(TreeProcKind.OverclockPeak), "zirve bir kez");

            Assert.AreEqual(1f, TreeHooks.FireRateScale(Ar, 0f, 5.2f), 1e-5f, "1 sn'den uzun ara: sıfır");
            const float restart = 5.2f;
            for (int i = 0; i <= 42; i++) TreeHooks.OnShotFired(restart + i * 0.1f);
            TreeHooks.FireRateScale(Ar, 0f, restart + 4.25f);
            Assert.AreEqual(2, Count(TreeProcKind.OverclockPeak), "bırakıp yeniden ısınınca yine");
        }

        [Test]
        public void AsiriHiz_KendiSaati_BirSaniyeAradaSifirlanir()
        {
            TreeStats s = TreeStats.Neutral;
            s.OverclockStep = 0.08f;
            s.OverclockMax = 0.6f;
            Use(s);

            for (int i = 0; i <= 20; i++) TreeHooks.OnShotFired(i * 0.1f);   // 0 … 2 s
            Assert.AreEqual(2f, TreeHooks.SustainedFireSeconds(2.0f), 1e-4f);
            Assert.AreEqual(1.32f, TreeHooks.FireRateScale(Ar, 99f, 2.0f), 1e-4f, "heldSeconds okunmaz: kendi saati (4 adım)");
            Assert.AreEqual(2.9f, TreeHooks.SustainedFireSeconds(2.9f), 1e-4f, "0,9 sn ara: birikim sürer");
            Assert.AreEqual(0f, TreeHooks.SustainedFireSeconds(3.1f), "1 sn'den uzun ara");
            Assert.AreEqual(1f, TreeHooks.FireRateScale(Ar, 99f, 3.1f), 1e-5f);
            TreeHooks.OnShotFired(3.1f);
            Assert.AreEqual(0f, TreeHooks.SustainedFireSeconds(3.1f), "ara sonrası ilk atış yeni seri başlatır");
            TreeHooks.OnClassicEquipping(Sr);
            Assert.AreEqual(0f, TreeHooks.SustainedFireSeconds(3.15f), "silah değişince sıfır");
        }

        [Test]
        public void Roketatar_DakikadaIkiYuzTavani_FazlasiHasara()
        {
            TreeStats s = TreeStats.Neutral;
            s.FullAuto = true;
            s.FullAutoRpmScale = 10f;
            s.FullAutoDamageScale = 0.4f;
            s.FireRateScale = 1.35f;   // sp_twin
            Use(s);

            float rate = TreeHooks.FireRateScale(Rl, 0f, 1f);
            Assert.AreEqual(TreeGuardrails.RocketAutoRpm, Rl.BaseRpm * rate, 1e-2f, "20 × 10 × 1,35 = 270 → 200");
            float damage = TreeHooks.ShotDamageScale(Rl, 0f, 1f, 0, 12, 12);
            Assert.AreEqual(0.4f * 1.35f, damage, 1e-4f, "K6: taşan devir hasara");
            Assert.AreEqual(10f * 1.35f * 0.4f, rate * damage, 1e-3f, "DPS korunur");
            Assert.AreEqual(13.5f, TreeHooks.FireRateScale(Sr, 0f, 1f), 1e-4f, "roket olmayan silaha roket tavanı uygulanmaz (48 × 13,5 = 648 rpm)");
        }

        // ------------------------------------------------------------------ magazine

        [Test]
        public void Sarjor_UzunSarjorYukariYuvarlarEnAzArtiBir()
        {
            TreeStats s = TreeStats.Neutral;
            s.MagazineScale = 1.5f;
            Use(s);

            Assert.AreEqual(1, TreeHooks.MagazineSize(1, Rl), "küçük şarjör: rl_1 boyu aynı (B5 b)");
            Assert.AreEqual(8, TreeHooks.MagazineSize(5, Sr), "sr 5 → 8");
            Assert.AreEqual(2, TreeHooks.MagazineSize(2, Sg2), "küçük şarjör: sg_2 boyu aynı (B5 b)");
            Assert.AreEqual(45, TreeHooks.MagazineSize(30, Ar));
            Assert.AreEqual(1f / 1.5f, TreeHooks.ReloadTimeScale(Rl, 2.8f), 1e-5f, "küçük şarjör ölçeği doldurmaya: ×1/m");
            Assert.AreEqual(1f / 1.5f, TreeHooks.ReloadTimeScale(Sg2, 1.5f), 1e-5f);
            Assert.AreEqual(1f, TreeHooks.ReloadTimeScale(Ar, 2f), 1e-5f, "normal şarjörde doldurma değişmez");

            s.MagazineScale = 3f;
            Use(s);
            Assert.AreEqual(15, TreeHooks.MagazineSize(5, Sr), "mg_ext + mg_double ×3");
        }

        [Test]
        public void TaramaliRoket_OnIkiKutu_YedekOlceklenir()
        {
            TreeStats s = TreeStats.Neutral;
            s.FullAuto = true;
            s.FullAutoMagazine = 12;
            Use(s);

            Assert.AreEqual(12, TreeHooks.MagazineSize(1, Rl), "E6 kutu");
            Assert.AreEqual(12f, TreeHooks.ReserveScale(1, Rl), 1e-5f);
            Assert.AreEqual(96, TreeHooks.ReserveAllowance(8, Rl), "8 roket = 8 kutu");
            Assert.GreaterOrEqual(TreeHooks.ReserveAllowance(1, Rl), 12, "yedek en az bir kutu");
            Assert.AreEqual(30, TreeHooks.MagazineSize(30, Ar), "otomatik silah dönüşmez");
            Assert.AreEqual(1f, TreeHooks.ReserveScale(30, Ar));

            s.MagazineScale = 1.5f;
            Use(s);
            Assert.AreEqual(18, TreeHooks.MagazineSize(1, Rl));
            Assert.AreEqual(144, TreeHooks.ReserveAllowance(8, Rl));
        }

        [Test]
        public void Doldurma_HizliElVeTaramaliRoketKutuSuresi()
        {
            TreeStats s = TreeStats.Neutral;
            s.ReloadScale = 0.45f;
            s.ReloadMoveFree = true;
            Use(s);
            Assert.AreEqual(0.45f, TreeHooks.ReloadTimeScale(Ar, 2f), 1e-5f);
            Assert.IsTrue(TreeHooks.ReloadMoveFree);

            s = TreeStats.Neutral;
            s.FullAuto = true;
            Use(s);
            Assert.AreEqual(3.0f / 2.8f, TreeHooks.ReloadTimeScale(Rl, 2.8f), 1e-4f, "fullAuto rl_1 reload 3,0 s (katalog)");
            Assert.AreEqual(1f, TreeHooks.ReloadTimeScale(Sg1, 0.75f), 1e-5f, "sg_1 doldurma aynı");

            s.Synergies = TreeSynergy.Barrage;
            Use(s);
            Assert.AreEqual(2.4f / 2.8f, TreeHooks.ReloadTimeScale(Rl, 2.8f), 1e-4f, "Barrage 2,4 s");

            s.ReloadScale = 0.8f;
            Use(s);
            Assert.AreEqual(0.8f * 2.4f / 2.8f, TreeHooks.ReloadTimeScale(Rl, 2.8f), 1e-4f, "Hızlı El üstüne");
        }

        [Test]
        public void KendiDolan_BeklerSonraSaniyedeCeyrek()
        {
            TreeStats s = TreeStats.Neutral;
            s.AutoReloadIdleSeconds = 1f;
            s.AutoReloadPerSecond = 0.25f;
            Use(s);

            float carry = 0f;
            Assert.AreEqual(0, TreeHooks.AutoReloadTick(0.5f, 0.5f, 20, 30, ref carry), "1 sn dolmadı");
            Assert.AreEqual(3, TreeHooks.AutoReloadTick(0.5f, 1.0f, 20, 30, ref carry), "30 × 0,25 × 0,5 = 3,75");
            Assert.AreEqual(0.75f, carry, 1e-4f);
            Assert.AreEqual(4, TreeHooks.AutoReloadTick(0.5f, 1.5f, 20, 30, ref carry));
            Assert.AreEqual(2, TreeHooks.AutoReloadTick(0.5f, 2.0f, 2, 30, ref carry), "eksik kadar");
            TreeHooks.AutoReloadTick(0.016f, 0f, 20, 30, ref carry);
            Assert.AreEqual(0f, carry, "ateş edince sıfırlanır");
        }

        [Test]
        public void SonsuzPencere_OldurmeSayar_AcilirKapanir()
        {
            TreeStats s = TreeStats.Neutral;
            s.EndlessKills = 25;
            s.EndlessSeconds = 4f;
            Use(s);

            for (int i = 0; i < 24; i++) TreeHooks.OnKill(1f);
            Assert.AreEqual(24, TreeHooks.EndlessKillCount);
            Assert.IsFalse(TreeHooks.NoReloadWindow(1f));
            TreeHooks.OnKill(1f);
            Assert.AreEqual(1, Count(TreeProcKind.EndlessWindowOpen));
            Assert.AreEqual(0, TreeHooks.EndlessKillCount);
            Assert.IsTrue(TreeHooks.NoReloadWindow(2f));
            Assert.IsFalse(TreeHooks.ConsumesAmmo(1, 2f), "pencerede şarjör düşmez");

            for (int i = 0; i < 30; i++) TreeHooks.OnKill(3f);
            Assert.AreEqual(0, TreeHooks.EndlessKillCount, "pencere içindeki öldürme sayılmaz");
            Assert.AreEqual(1, Count(TreeProcKind.EndlessWindowOpen));

            TreeHooks.Tick(5.1f);
            Assert.AreEqual(1, Count(TreeProcKind.EndlessWindowClose));
            Assert.IsFalse(TreeHooks.NoReloadWindow(5.2f));
            Assert.IsTrue(TreeHooks.ConsumesAmmo(1, 5.2f));
        }

        [Test]
        public void SonsuzlukMotoru_PencereIcindeDevirArtar()
        {
            TreeStats s = TreeStats.Neutral;
            s.EndlessKills = 15;
            s.EndlessSeconds = 8f;
            s.EndlessFireRateScale = 1.25f;
            Use(s);

            Assert.AreEqual(1f, TreeHooks.FireRateScale(Ar, 0f, 1f), 1e-5f);
            for (int i = 0; i < 15; i++) TreeHooks.OnKill(1f);
            Assert.AreEqual(1.25f, TreeHooks.FireRateScale(Ar, 0f, 2f), 1e-5f);
            Assert.AreEqual(1f, TreeHooks.FireRateScale(Ar, 0f, 9.5f), 1e-5f);
        }

        [Test]
        public void CiftNamlu_HerIkinciMermiBedava()
        {
            TreeStats s = TreeStats.Neutral;
            s.TwinFreeEvery = 2;
            Use(s);

            Assert.IsFalse(TreeHooks.TwinFreeRound(1));
            Assert.IsTrue(TreeHooks.TwinFreeRound(2));
            Assert.IsFalse(TreeHooks.TwinFreeRound(3));
            Assert.IsTrue(TreeHooks.TwinFreeRound(4));
            Assert.IsFalse(TreeHooks.ConsumesAmmo(4, 0f));
            Assert.IsTrue(TreeHooks.ConsumesAmmo(5, 0f));
        }

        // ------------------------------------------------------------------ crit

        [Test]
        public void Kritik_AyniTohumAyniDizi_Oran()
        {
            TreeStats s = TreeStats.Neutral;
            s.CritChance = 0.1f;
            s.CritMultiplier = 2.5f;
            Use(s);

            var first = new List<bool>(2000);
            uint rng = TreeHooks.SeedFor(42, 1u);
            int crits = 0;
            for (int i = 0; i < 2000; i++)
            {
                bool c = TreeHooks.RollCrit(ref rng, out float mult);
                first.Add(c);
                if (c)
                {
                    crits++;
                    Assert.AreEqual(2.5f, mult);
                }
                else
                {
                    Assert.AreEqual(1f, mult);
                }
            }

            TreeHooks.ResetRunState();
            rng = TreeHooks.SeedFor(42, 1u);
            for (int i = 0; i < 2000; i++) Assert.AreEqual(first[i], TreeHooks.RollCrit(ref rng, out _), "deterministik #" + i);
            Assert.That(crits / 2000f, NUnit.Framework.Is.InRange(0.07f, 0.13f));
        }

        [Test]
        public void Kritik_KotuSansKorumasi_SekizTetiktenSonraKesin()
        {
            TreeStats s = TreeStats.Neutral;
            s.CritChance = 1e-6f;
            s.CritMultiplier = 2.5f;
            s.CritPity = 8;
            Use(s);

            uint rng = TreeHooks.SeedFor(7, 3u);
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < 8; i++) Assert.IsFalse(TreeHooks.RollCrit(ref rng, out _), "kritsiz çekiş " + i);
                Assert.AreEqual(8, TreeHooks.PullsSinceCrit);
                Assert.IsTrue(TreeHooks.RollCrit(ref rng, out float mult), "9. çekiş kesin kritik");
                Assert.AreEqual(2.5f, mult);
                Assert.AreEqual(0, TreeHooks.PullsSinceCrit);
            }
        }

        // ------------------------------------------------------------------ survival

        [Test]
        public void HayattaKalma_CanYenilenmeHareket_Tipik()
        {
            TreeStats s = TreeStats.Neutral;
            s.MaxHealthScale = 1.6f;
            s.RegenDelayScale = 0.7f;
            s.MoveScale = 1.14f;
            s.KillSpeedBoost = 1.2f;
            s.KillSpeedBoostSeconds = 1.5f;
            s.FiringMovePenaltyScale = 0.5f;
            Use(s);

            Assert.AreEqual(1.6f, TreeHooks.MaxHealthScale);
            Assert.AreEqual(0.7f, TreeHooks.RegenDelayScale);
            Assert.AreEqual(1.14f, TreeHooks.MoveScale(10f), 1e-5f);
            TreeHooks.OnKill(10f);
            Assert.AreEqual(1.14f * 1.2f, TreeHooks.MoveScale(11f), 1e-5f, "öldürmeden sonra 1,5 sn");
            Assert.AreEqual(1.14f, TreeHooks.MoveScale(11.6f), 1e-5f);
            Assert.AreEqual(0.9f, TreeHooks.FiringMoveFactor(0.8f), 1e-5f, "ceza yarıya");
            Assert.AreEqual(1f, TreeHooks.FiringMoveFactor(1f));
        }

        [Test]
        public void Kalkan_EmerSonraKirilir_Olay()
        {
            TreeStats s = TreeStats.Neutral;
            s.ShieldShare = 0.25f;
            s.ShieldDelaySeconds = 4f;
            s.ShieldRegenPerSecond = 0.2f;
            Use(s);

            TreeShieldState st = TreeShieldState.Create(1u);
            TreeHooks.ShieldFill(100f, ref st);
            Assert.AreEqual(25f, st.Shield);

            TreeHitResult hit = TreeHooks.IncomingDamage(10f, 1f, 100f, 100f, ref st);
            Assert.AreEqual(0f, hit.Damage);
            Assert.AreEqual(10f, hit.Absorbed);
            Assert.AreEqual(TreeHitOutcome.ShieldAbsorbed, hit.Outcome);
            Assert.AreEqual(15f, st.Shield, 1e-5f);

            hit = TreeHooks.IncomingDamage(20f, 2f, 100f, 100f, ref st);
            Assert.AreEqual(15f, hit.Absorbed, 1e-5f);
            Assert.AreEqual(5f, hit.Damage, 1e-5f);
            Assert.AreEqual(TreeHitOutcome.ShieldBroken, hit.Outcome);
            Assert.AreEqual(1, Count(TreeProcKind.ShieldBreak));
            Assert.AreEqual(0f, _procs[0].Radius, "Taş Muhafız yok: dalga yok");

            hit = TreeHooks.IncomingDamage(10f, 3f, 95f, 100f, ref st);
            Assert.AreEqual(10f, hit.Damage);
            Assert.AreEqual(TreeHitOutcome.None, hit.Outcome);
            Assert.AreEqual(1, Count(TreeProcKind.ShieldBreak));
        }

        [Test]
        public void Kalkan_DortSaniyeSonraSaniyedeYirmiYuzdeDolar()
        {
            TreeStats s = TreeStats.Neutral;
            s.ShieldShare = 0.25f;
            s.ShieldDelaySeconds = 4f;
            s.ShieldRegenPerSecond = 0.2f;
            Use(s);

            TreeShieldState st = TreeShieldState.Create(1u);
            TreeHooks.IncomingDamage(10f, 2f, 100f, 100f, ref st);   // empty shield, the hit starts the delay
            TreeHooks.ShieldTick(1f, 5.9f, 100f, ref st);
            Assert.AreEqual(0f, st.Shield, "gecikme dolmadı");
            TreeHooks.ShieldTick(1f, 6.0f, 100f, ref st);
            Assert.AreEqual(5f, st.Shield, 1e-4f, "25 × 0,2 × 1 sn");
            TreeHooks.ShieldTick(10f, 20f, 100f, ref st);
            Assert.AreEqual(25f, st.Shield, 1e-4f, "tavan");
        }

        [Test]
        public void TasMuhafiz_KirilmaDalgasiYaricapVeBekleme()
        {
            TreeStats s = TreeStats.Neutral;
            s.ShieldShare = 0.5f;
            s.BulwarkRadius = 4f;
            s.BulwarkCooldown = 12f;
            Use(s);

            TreeShieldState st = TreeShieldState.Create(1u);
            TreeHooks.ShieldFill(100f, ref st);
            TreeHooks.IncomingDamage(60f, 1f, 100f, 100f, ref st);
            TreeHooks.ShieldFill(100f, ref st);
            TreeHooks.IncomingDamage(60f, 5f, 100f, 100f, ref st);
            TreeHooks.ShieldFill(100f, ref st);
            TreeHooks.IncomingDamage(60f, 14f, 100f, 100f, ref st);

            Assert.AreEqual(3, Count(TreeProcKind.ShieldBreak));
            Assert.AreEqual(4f, _procs[0].Radius);
            Assert.AreEqual(0f, _procs[1].Radius, "12 sn bekleme");
            Assert.AreEqual(4f, _procs[2].Radius);
        }

        [Test]
        public void Kacinma_YuzdeOtuzTavani_SonraHayalet()
        {
            TreeStats s = TreeStats.Neutral;
            s.DodgeChance = 0.9f;   // a resolver bypass: the hook clamps too
            Use(s);

            TreeShieldState st = TreeShieldState.Create(99u);
            int dodged = 0;
            for (int i = 0; i < 10000; i++)
            {
                if (TreeHooks.IncomingDamage(10f, i, 100f, 100f, ref st).Outcome == TreeHitOutcome.Dodged) dodged++;
            }

            Assert.That(dodged / 10000f, NUnit.Framework.Is.InRange(0.27f, 0.33f), "kaçınma ≤ %30");
            Assert.AreEqual(dodged, Count(TreeProcKind.Dodge));

            s.DodgeChance = 0.3f;
            s.DodgeGhostSeconds = 0.5f;
            Use(s);
            st = TreeShieldState.Create(5u);
            float t = 100f;
            while (TreeHooks.IncomingDamage(10f, t, 100f, 100f, ref st).Outcome != TreeHitOutcome.Dodged) t += 1f;
            TreeHitResult ghost = TreeHooks.IncomingDamage(10f, t + 0.2f, 100f, 100f, ref st);
            Assert.AreEqual(TreeHitOutcome.Ghost, ghost.Outcome);
            Assert.AreEqual(0f, ghost.Damage);
            Assert.AreNotEqual(TreeHitOutcome.Ghost, TreeHooks.IncomingDamage(10f, t + 0.6f, 100f, 100f, ref st).Outcome);
        }

        [Test]
        public void OlumsuzAnt_RoundBasinaBir()
        {
            TreeStats s = TreeStats.Neutral;
            s.UndyingPerRound = 1;
            s.UndyingSeconds = 3f;
            s.UndyingHealthShare = 0.5f;
            Use(s);

            TreeShieldState st = TreeShieldState.Create(1u);
            TreeHitResult hit = TreeHooks.IncomingDamage(50f, 1f, 20f, 100f, ref st);
            Assert.AreEqual(TreeHitOutcome.Undying, hit.Outcome);
            Assert.AreEqual(50f, hit.HealthFloor);
            Assert.AreEqual(50f, Mathf.Max(hit.HealthFloor, 20f - hit.Damage), "%50 canla kalkış");
            Assert.AreEqual(1, Count(TreeProcKind.UndyingOath));
            Assert.AreEqual(1, TreeHooks.UndyingUsedThisRound);

            Assert.AreEqual(TreeHitOutcome.Invulnerable, TreeHooks.IncomingDamage(80f, 3.9f, 50f, 100f, ref st).Outcome, "3 sn ölümsüz");
            hit = TreeHooks.IncomingDamage(80f, 5f, 30f, 100f, ref st);
            Assert.AreEqual(TreeHitOutcome.None, hit.Outcome, "aynı round ikinci kez yok");
            Assert.AreEqual(80f, hit.Damage);

            TreeHooks.OnRoundStarted(2);
            Assert.AreEqual(0, TreeHooks.UndyingUsedThisRound);
            Assert.AreEqual(TreeHitOutcome.Undying, TreeHooks.IncomingDamage(80f, 6f, 30f, 100f, ref st).Outcome, "yeni round yeniden");
            Assert.AreEqual(2, Count(TreeProcKind.UndyingOath));
        }

        [Test]
        public void CanEmme_KademePaylariVeSaniyeTavani()
        {
            TreeStats s = TreeStats.Neutral;
            s.LeechStandard = 0.01f;
            s.LeechElite = 0.05f;
            s.LeechUltra = 0.15f;
            s.LeechCapPerSecond = 0.06f;
            Use(s);

            Assert.AreEqual(0.01f, TreeHooks.OnKillHeal(EnemyTier.Standard, 1f), 1e-5f);
            Assert.AreEqual(0.05f, TreeHooks.OnKillHeal(EnemyTier.Elite, 1.1f), 1e-5f);
            Assert.AreEqual(0f, TreeHooks.OnKillHeal(EnemyTier.Ultra, 1.2f), "saniyede %6 doldu");
            Assert.AreEqual(2, Count(TreeProcKind.Leech));
            Assert.AreEqual(0.06f, TreeHooks.OnKillHeal(EnemyTier.Ultra, 2.05f), 1e-5f, "yeni saniye, Ultra %15 → tavan %6");
            Assert.AreEqual(0f, TreeHooks.OnKillHeal(EnemyTier.Special, 2.1f));
            Assert.AreEqual(0.01f, TreeHooks.OnKillHeal(EnemyTier.Special, 3.1f), 1e-5f, "Special = standart pay");
        }

        // ------------------------------------------------------------------ economy

        [Test]
        public void WallBuy_SilahTuccari_FiyatVeCephane()
        {
            TreeStats s = TreeStats.Neutral;
            s.WallBuyScale = 0.8f;
            s.AmmoBuyScale = 0.8f;
            Use(s);

            Assert.AreEqual(2000, TreeHooks.WallBuyPrice(2500), "Bohiya 2 500 → 2 000");
            Assert.AreEqual(36, TreeHooks.WallBuyPrice(45));
            Assert.AreEqual(1, TreeHooks.WallBuyPrice(1), "en az 1");
            Assert.AreEqual(0, TreeHooks.WallBuyPrice(0), "satılmayan silah");
            Assert.AreEqual(1000, TreeHooks.AmmoPrice(2500, 0.5f), "yarı fiyat × 0,8");
            Assert.AreEqual(18, TreeHooks.AmmoPrice(45, 0.5f));
            Assert.AreEqual(1, TreeHooks.AmmoPrice(1, 0.5f));

            s.MagnetScale = 1.5f;
            Use(s);
            Assert.AreEqual(1.5f, TreeHooks.MagnetRadiusScale, 1e-5f, "Altın Sezgisi III mıknatıs ×1,5");
        }

        // ------------------------------------------------------------------ run boundary / equip / GC

        [Test]
        public void KosuBasi_SayaclarVePencerelerSifirlanir()
        {
            TreeStats s = TreeStats.Neutral;
            s.EndlessKills = 3;
            s.EndlessSeconds = 4f;
            s.CritChance = 1e-6f;
            s.CritMultiplier = 2f;
            s.CritPity = 8;
            s.UndyingPerRound = 1;
            s.UndyingHealthShare = 0.5f;
            Use(s);

            TreeHooks.OnKill(1f);
            TreeHooks.OnKill(1f);
            uint rng = 3u;
            TreeHooks.RollCrit(ref rng, out _);
            TreeShieldState st = TreeShieldState.Create(1u);
            TreeHooks.IncomingDamage(500f, 1f, 10f, 100f, ref st);
            Assert.AreEqual(2, TreeHooks.EndlessKillCount);
            Assert.AreEqual(1, TreeHooks.PullsSinceCrit);
            Assert.AreEqual(1, TreeHooks.UndyingUsedThisRound);

            TreeHooks.ResetRunState();
            Assert.AreEqual(0, TreeHooks.EndlessKillCount);
            Assert.AreEqual(0, TreeHooks.PullsSinceCrit);
            Assert.AreEqual(0, TreeHooks.UndyingUsedThisRound);
            Assert.IsFalse(TreeHooks.NoReloadWindow(1.5f));
            Assert.Greater(TreeHooks.SinceKill(1.5f), 1e6f);
        }

        [Test]
        public void Kusanma_ElSilahiniKaydeder_PencereyiKapatir()
        {
            TreeStats s = TreeStats.Neutral;
            s.EndlessKills = 1;
            s.EndlessSeconds = 10f;
            Use(s);

            TreeWeaponProfile seen = default;
            System.Action<TreeWeaponProfile> listener = w => seen = w;
            TreeHooks.Equipping += listener;
            try
            {
                TreeHooks.OnKill(1f);
                Assert.IsTrue(TreeHooks.NoReloadWindow(1.5f));

                TreeHooks.OnClassicEquipping(Rl);
                Assert.AreEqual(ClassicWeaponId.Rl1, TreeHooks.InHand.Id);
                Assert.AreEqual("rl_1", seen.Key);
                Assert.IsFalse(TreeHooks.NoReloadWindow(1.6f), "silah değişince pencere kapanır");
                Assert.AreEqual(1, Count(TreeProcKind.EndlessWindowClose));
            }
            finally
            {
                TreeHooks.Equipping -= listener;
            }
        }

        // ------------------------------------------------------------------ B6 awakening alternatives

        [Test]
        public void Uyanis_YikimHalkasi_HerDorduncuTetikSekizliTamTur()
        {
            TreeStats s = TreeStats.Neutral;
            s.RingEvery = 4;
            s.RingShots = 8;
            s.RingShare = 0.5f;
            Use(s);

            TreeVolleyPlan plan;
            for (int i = 0; i < 3; i++)
            {
                TreeHooks.PlanVolley(Ar, 1, out plan);
                Assert.AreEqual(1, plan.Count, "tetik " + (i + 1) + ": tek mermi");
            }

            TreeHooks.PlanVolley(Ar, 1, out plan);
            Assert.AreEqual(8, plan.Count, "4. tetik: 8'li halka");
            Assert.AreEqual(-22.5f, plan.YawDegrees(0), 1e-3f, "ana atış merkeze en yakın");
            Assert.AreEqual(22.5f, plan.YawDegrees(1), 1e-3f);
            Assert.AreEqual(-157.5f, plan.YawDegrees(6), 1e-3f);
            Assert.AreEqual(157.5f, plan.YawDegrees(7), 1e-3f, "45° aralık, tam tur");
            Assert.AreEqual(1f, plan.DamageScale(0));
            Assert.AreEqual(0.5f, plan.DamageScale(1), 1e-5f, "halka mermisi %50");
            TreeHooks.PlanVolley(Ar, 1, out plan);
            Assert.AreEqual(1, plan.Count, "5. tetik yine tek");
        }

        [Test]
        public void Uyanis_MermiCesmesi_OldurmeSarjoruDoldururVeHasarUcSaniye()
        {
            TreeStats s = TreeStats.Neutral;
            s.FountainChance = 1f;
            s.FountainDamage = 1.15f;
            s.FountainSeconds = 3f;
            Use(s);

            float carry = 0f;
            Assert.AreEqual(0, TreeHooks.AutoReloadTick(0.016f, 0f, 20, 30, ref carry), "öldürme yokken dolum yok");
            TreeHooks.OnKill(1f);
            Assert.AreEqual(20, TreeHooks.AutoReloadTick(0.016f, 0f, 20, 30, ref carry), "bütün şarjör bir kez");
            Assert.AreEqual(0, TreeHooks.AutoReloadTick(0.016f, 0f, 20, 30, ref carry), "tek dolum");
            Assert.AreEqual(1.15f, TreeHooks.ShotDamageScale(Ar, 0f, 2f, 0, 30, 30), 1e-5f);
            Assert.AreEqual(1f, TreeHooks.ShotDamageScale(Ar, 0f, 4.1f, 0, 30, 30), 1e-5f, "3 sn sonra biter");

            s.FountainChance = 0.03f;
            Use(s);
            TreeHooks.ResetRunState();
            int refills = 0;
            for (int i = 0; i < 10000; i++)
            {
                TreeHooks.OnKill(10f + i);
                carry = 0f;
                if (TreeHooks.AutoReloadTick(0.016f, 0f, 1, 30, ref carry) > 0) refills++;
            }

            Assert.That(refills / 10000f, NUnit.Framework.Is.InRange(0.02f, 0.04f), "%3 şans");
        }

        [Test]
        public void Uyanis_GozKirpmaAtesi_OldurmeVeKacinmaSonrasiBirSaniyeIkiKat()
        {
            TreeStats s = TreeStats.Neutral;
            s.BlinkRpm = 2f;
            s.BlinkSeconds = 1f;
            Use(s);

            Assert.AreEqual(1f, TreeHooks.FireRateScale(Ar, 0f, 1f), 1e-5f);
            TreeHooks.OnKill(1f);
            Assert.AreEqual(2f, TreeHooks.FireRateScale(Ar, 0f, 1.5f), 1e-5f, "600 → 1 200 rpm");
            Assert.AreEqual(1f, TreeHooks.FireRateScale(Ar, 0f, 2.1f), 1e-5f, "1 sn sonra biter");

            s.DodgeChance = 0.3f;
            Use(s);
            TreeShieldState st = TreeShieldState.Create(5u);
            float t = 100f;
            while (TreeHooks.IncomingDamage(10f, t, 100f, 100f, ref st).Outcome != TreeHitOutcome.Dodged) t += 1f;
            Assert.AreEqual(2f, TreeHooks.FireRateScale(Ar, 0f, t + 0.5f), 1e-5f, "kaçınma da açar");
        }

        [Test]
        public void Uyanis_AltinYagmuru_HerBinAltindaOnSaniye()
        {
            TreeStats s = TreeStats.Neutral;
            s.RainGoldStep = 1000;
            s.RainDamage = 1.3f;
            s.RainSeconds = 10f;
            Use(s);

            TreeHooks.OnGoldSpent(2500, 1f);
            Assert.AreEqual(1f, TreeHooks.ShotDamageScale(Ar, 0f, 1.5f, 0, 30, 30), 1e-5f, "düğümden önceki harcama yağdırmaz");
            TreeHooks.OnGoldSpent(3499, 2f);
            Assert.AreEqual(1f, TreeHooks.ShotDamageScale(Ar, 0f, 2.5f, 0, 30, 30), 1e-5f);
            TreeHooks.OnGoldSpent(3500, 3f);
            Assert.AreEqual(1.3f, TreeHooks.ShotDamageScale(Ar, 0f, 5f, 0, 30, 30), 1e-5f, "+%30");
            Assert.AreEqual(1f, TreeHooks.ShotDamageScale(Ar, 0f, 13.1f, 0, 30, 30), 1e-5f, "10 sn");
            TreeHooks.OnGoldSpent(5600, 20f);
            Assert.AreEqual(1.3f, TreeHooks.ShotDamageScale(Ar, 0f, 29.9f, 0, 30, 30), 1e-5f, "iki adım: süre tazelenir, toplanmaz");
            Assert.AreEqual(1f, TreeHooks.ShotDamageScale(Ar, 0f, 30.1f, 0, 30, 30), 1e-5f);
        }

        [Test]
        public void Uyanis_AnkaKusu_KosudaBirKezAtesPatlamasiVeOtuzYuzdeCan()
        {
            TreeStats s = TreeStats.Neutral;
            s.WeaponPower = 100f;
            s.PhoenixRadius = 6f;
            s.PhoenixHealthShare = 0.3f;
            s.PhoenixShare = 1.5f;
            s.PhoenixSeconds = 2f;
            Use(s);
            try
            {
                TreeShieldState st = TreeShieldState.Create(1u);
                TreeHitResult hit = TreeHooks.IncomingDamage(50f, 1f, 20f, 100f, ref st);
                Assert.AreEqual(TreeHitOutcome.Undying, hit.Outcome);
                Assert.AreEqual(30f, hit.HealthFloor, 1e-4f, "%30 canla kalkış");
                Assert.AreEqual(1, Count(TreeProcKind.UndyingOath));
                Assert.AreEqual(6f, _procs[_procs.Count - 1].Radius, 1e-5f, "6 m ateş patlaması anı");
                Assert.AreEqual(1, TreeSystemsRunner.PhoenixBlasts);
                Assert.AreEqual(TreeHitOutcome.Invulnerable, TreeHooks.IncomingDamage(80f, 2.9f, 30f, 100f, ref st).Outcome, "2 sn dokunulmaz");

                TreeHooks.OnRoundStarted(2);
                hit = TreeHooks.IncomingDamage(80f, 5f, 30f, 100f, ref st);
                Assert.AreEqual(TreeHitOutcome.None, hit.Outcome, "koşuda bir kez: yeni round yenilemez");
                Assert.AreEqual(80f, hit.Damage);
                Assert.AreEqual(1, TreeSystemsRunner.PhoenixBlasts);
            }
            finally
            {
                TreeSystemsRunner.Reset();   // the blast sits in the static queue: leave nothing for the next fixture
            }
        }

        [Test]
        public void SicakYol_AyirmaYok()
        {
            TreeStats s = TreeStats.Neutral;
            s.DamageScale = 1.15f;
            s.ExtraShots = 4;
            s.ExtraShotDamage = 0.45f;
            s.FanArcDegrees = 40f;
            s.FireRateScale = 1.4f;
            s.OverclockStep = 0.08f;
            s.OverclockMax = 0.6f;
            s.FullAuto = true;
            s.FullAutoMagazine = 12;
            s.FullAutoRpmScale = 10f;
            s.FullAutoDamageScale = 0.4f;
            s.MagazineScale = 1.5f;
            s.MagazineTailShare = 0.2f;
            s.MagazineTailDamage = 1.3f;
            s.EndlessKills = 5;
            s.EndlessSeconds = 2f;
            s.TwinFreeEvery = 2;
            s.CritChance = 0.18f;
            s.CritMultiplier = 3f;
            s.CritPity = 8;
            s.ShieldShare = 0.5f;
            s.ShieldDelaySeconds = 4f;
            s.ShieldRegenPerSecond = 0.2f;
            s.DodgeChance = 0.15f;
            s.DodgeGhostSeconds = 0.5f;
            s.LeechStandard = 0.01f;
            s.LeechCapPerSecond = 0.06f;
            s.UndyingPerRound = 1;
            s.UndyingHealthShare = 0.5f;
            s.MidasGoldStep = 500;
            s.MidasDamageStep = 0.05f;
            s.MidasDamageMax = 0.4f;
            Use(s);
            TreeEvents.ResetForTests();   // no listener: measure the hooks, not the test's list

            TreeWeaponProfile rl = Rl;
            TreeWeaponProfile sg = Sg1;
            uint rng = TreeHooks.SeedFor(1, 1u);
            TreeShieldState st = TreeShieldState.Create(2u);
            float carry = 0f;
            float sink = 0f;

            void Cycle(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    float now = i * 0.05f;
                    TreeHooks.OnShotFired(now);
                    TreeHooks.PlanVolley(sg, 8, 4f, out TreeVolleyPlan plan);
                    for (int v = 0; v < plan.Count; v++) sink += plan.DamageScale(v) + plan.YawDegrees(v);
                    sink += TreeHooks.ShotDamageScale(rl, i * 0.1f, now, 1200, 3, 12);
                    sink += TreeHooks.FireRateScale(rl, i * 0.1f, now);
                    sink += TreeHooks.MagazineSize(1, rl) + TreeHooks.ReserveAllowance(8, rl) + TreeHooks.ReloadTimeScale(rl, 2.8f);
                    sink += TreeHooks.AutoReloadTick(0.016f, 2f, 5, 12, ref carry);
                    sink += TreeHooks.ConsumesAmmo(i, now) ? 1f : 0f;
                    sink += TreeHooks.RollCrit(ref rng, out float mult) ? mult : 0f;
                    TreeHooks.OnKill(now);
                    TreeHooks.Tick(now);
                    TreeHooks.ShieldTick(0.016f, now, 100f, ref st);
                    sink += TreeHooks.IncomingDamage(5f, now, 100f, 100f, ref st).Damage;
                    sink += TreeHooks.OnKillHeal(EnemyTier.Standard, now);
                    sink += TreeHooks.MoveScale(now) + TreeHooks.WallBuyPrice(2500) + TreeHooks.AmmoPrice(2500, 0.5f);
                    sink += (int)TreeHooks.PowerOverride(rl) + (TreeHooks.RocketSelfImmune(rl) ? 1f : 0f);
                }
            }

            Cycle(8);   // warm-up: JIT, the catalog read behind the reload cache

            byte[] control = null;
            Assert.That(() => { control = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol");
            Assert.That(() => Cycle(400), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "ağaç kancaları ayırmamalı");
            Assert.IsNotNull(control);
            Assert.IsFalse(float.IsNaN(sink));
        }
    }
}
