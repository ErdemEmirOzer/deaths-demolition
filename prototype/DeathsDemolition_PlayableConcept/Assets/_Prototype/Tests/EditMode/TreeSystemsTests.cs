using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension (WP-051C pattern)

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B3 · ağaç sistemleri (S-04): durum (yanma / zehir / buz yönlendirme), alan havuzu, patlama kuyruğu, kara delik,
    /// koşucu. Sahne yok: sahte <see cref="ITreeStatusReceiver"/> S-03 yüzeyini (ApplyIce / ApplyStun / ApplyPull /
    /// ApplyStatusDamage / ApplyExplosion) en yalın hâliyle taklit eder; kademe kuralları S-03'ün testindedir. Saat elle sürülür
    /// (<see cref="TreeSystemsRunner.Tick"/>), kare = bir Tick.
    /// </summary>
    [TestFixture]
    public sealed class TreeSystemsTests
    {
        private const float Step = 1f / 16f;
        private float _now;
        private static readonly int[] s_procs = new int[64];

        [SetUp]
        public void SetUp()
        {
            TreeEvents.ResetForTests();
            TreeRuntime.ClearTestStats();
            TreeAreaPool.ResetForTests();
            TreeSystemsRunner.BeforeTick = null;
            TreeSystemsRunner.AfterReset = null;
            TreeSystemsRunner.Reset(12345u);
            for (int i = 0; i < s_procs.Length; i++) s_procs[i] = 0;
            _now = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            TreeSystemsRunner.Reset();
            TreeAreaPool.ResetForTests();
            TreeRuntime.ClearTestStats();
            TreeEvents.ResetForTests();
        }

        // ================================================================== durum: slotlar ve fazlar

        [Test]
        public void StatusSlots_FitOneMask()
        {
            Assert.LessOrEqual(TreeGuardrails.StatusSlots, 64, "durum maskeleri tek ulong");
            Assert.AreEqual(TreeGuardrails.StatusSlots, TreeStatusSystem.Capacity);
        }

        [Test]
        public void Register_StableSlot_ReleaseFrees_Idempotent()
        {
            Use(Base());
            FakeBody a = Body(0f), b = Body(1f);
            Assert.AreEqual(0, a.TreeSlot);
            Assert.AreEqual(1, b.TreeSlot);
            Assert.IsTrue(TreeStatusSystem.Register(a), "ikinci kayıt aynı slotu verir");
            Assert.AreEqual(0, a.TreeSlot);
            Assert.AreEqual(2, TreeStatusSystem.ActiveSlots);
            TreeStatusSystem.Release(a);
            Assert.AreEqual(TreeStatusSystem.NoSlot, a.TreeSlot);
            Assert.AreEqual(1, TreeStatusSystem.ActiveSlots);
            FakeBody c = Body(2f);
            Assert.AreEqual(0, c.TreeSlot, "boşalan slot yeniden verilir");
            Assert.AreEqual(TreeStatusSystem.NoSlot, TreeStatusSystem.SlotOf(a), "eski numara eşleşmez");
        }

        [Test]
        public void Register_FullTable_Refused()
        {
            Use(Base());
            for (int i = 0; i < TreeGuardrails.StatusSlots; i++) Body(i);
            var extra = new FakeBody();
            Assert.IsFalse(TreeStatusSystem.Register(extra));
            Assert.AreEqual(1, TreeStatusSystem.RegisterRefused);
            Assert.AreEqual(TreeStatusSystem.NoSlot, extra.TreeSlot);
        }

        [Test]
        public void Tick_FourPhases_QuarterOfSlotsPerStep()
        {
            Use(Base());
            var bodies = new FakeBody[8];
            for (int i = 0; i < 8; i++)
            {
                bodies[i] = Body(i * 10f);
                TreeStatusSystem.Ignite(bodies[i], 10f, 3f);
            }

            Frame();
            for (int i = 0; i < 8; i++) Assert.AreEqual(i % 4 == 0 ? 1 : 0, bodies[i].StatusCalls, "1. adım yalnız faz 0: slot " + i);
            Frame();
            Frame();
            Frame();
            for (int i = 0; i < 8; i++) Assert.AreEqual(1, bodies[i].StatusCalls, "4 adım = her slot bir tik: slot " + i);
            Assert.AreEqual(2.5f, bodies[3].StatusDamage, 1e-4f, "tik = dps × 0,25 s");
        }

        // ================================================================== yanma

        [Test]
        public void Burn_DotTotal_IsDpsTimesSeconds()
        {
            TreeStats s = Base();
            s.Elements = TreeElement.Fire;
            s.FireDotShare = 0.15f;
            s.FireSeconds = 3f;
            Use(s);
            FakeBody b = Body(0f);
            Assert.AreEqual(TreeElement.Fire, TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts()));
            Run(4f);
            Assert.AreEqual(45f, b.StatusDamage, 1e-3f, "0,15 × SG 100 × 3 s");
            Assert.AreEqual(45f, b.FireDamage, 1e-3f);
            Assert.AreEqual(1, TreeStatusSystem.Ignites);
            Assert.AreEqual(TreeDamageKind.Dot, b.LastKind);
            Assert.AreEqual(0, b.BlastCalls, "DoT patlama yolu değil");
        }

        [Test]
        public void Burn_Reapply_KeepsHigherDps_RefreshesNeverSums()
        {
            Use(Base());
            FakeBody b = Body(0f);
            TreeStatusSystem.Ignite(b, 10f, 3f);
            Run(1f);
            Assert.AreEqual(10f, b.StatusDamage, 1e-3f);
            TreeStatusSystem.Ignite(b, 5f, 3f);
            Run(4f);
            Assert.AreEqual(40f, b.StatusDamage, 1e-3f, "dps 10 korunur, süre 3 s'ye tazelenir");
        }

        [Test]
        public void Burn_Tint_SingleTintOnThenOff()
        {
            Use(Base());
            FakeBody b = Body(0f);
            TreeStatusSystem.Ignite(b, 10f, 1f);
            Assert.AreEqual(TreeElement.Fire, b.TintElement);
            Assert.AreEqual(1f, b.TintStrength);
            Run(1.5f);
            Assert.AreEqual(TreeElement.None, b.TintElement, "yanma bitti: ton temizlenir");
            Assert.AreEqual(0f, b.TintStrength);
            Assert.IsFalse(TreeStatusSystem.IsBurning(b));
        }

        [Test]
        public void Burn_DeathFreesSlot_AndLightsWildfireGround()
        {
            TreeStats s = Base();
            s.Elements = TreeElement.Fire;
            s.FireDotShare = 0.25f;
            s.FireGroundRadius = 2f;
            s.FireGroundSeconds = 3f;
            Use(s);
            FakeBody b = Body(0f, hp: 10f);
            TreeStatusSystem.Ignite(b, 100f, 3f);
            Frame();
            Assert.IsFalse(b.IsAlive);
            Assert.AreEqual(TreeStatusSystem.NoSlot, b.TreeSlot, "ölümde slot boşalır");
            Assert.AreEqual(0, TreeStatusSystem.ActiveSlots);
            Assert.AreEqual(1, TreeAreaPool.CountOf(TreeAreaKind.Fire), "yanan ölünce yangın zemini");
        }

        [Test]
        public void Inferno_SpreadsToOneNeighbourPerSecond()
        {
            TreeStats s = Base();
            s.Elements = TreeElement.Fire;
            s.FireSpreadRadius = 1.5f;
            s.FireSeconds = 3f;
            Use(s);
            FakeBody a = Body(0f), b = Body(1f), c = Body(5f);
            TreeStatusSystem.Ignite(a, 10f, 3f);
            Run(1.25f);
            Assert.IsTrue(TreeStatusSystem.IsBurning(b), "1,5 m içindeki komşu tutuştu");
            Assert.IsFalse(TreeStatusSystem.IsBurning(c));
            Assert.AreEqual(1, TreeStatusSystem.Spreads, "saniyede bir komşu");
            Run(1f);
            Assert.AreEqual(1, TreeStatusSystem.Spreads, "yanan komşuya yeniden yayılmaz");
            Run(1f);
            Assert.IsFalse(TreeStatusSystem.IsBurning(c), "5 m uzaktaki hiç tutuşmaz");
        }

        // ================================================================== zehir

        [Test]
        public void Venom_StackCap_AndDotTotal()
        {
            Use(Venom(Base()));
            FakeBody capped = Body(0f), single = Body(10f);
            TreeStatusSystem.AddVenomStacks(capped, 10);
            Assert.AreEqual(8, TreeStatusSystem.VenomStacks(capped), "tavan 8");
            TreeStatusSystem.AddVenomStacks(single, 1);
            Run(6f);
            Assert.AreEqual(20f, single.VenomDamage, 1e-3f, "0,04 × SG 100 × 1 yığın × 5 s");
            Assert.AreEqual(0, TreeStatusSystem.VenomStacks(single));
        }

        [Test]
        public void Venom_Corrosion_Threshold_OnAndOff()
        {
            TreeStats s = Venom(Base());
            s.VenomArmorIgnoreStacks = 6;
            s.VenomVulnerability = 1.2f;
            Use(s);
            FakeBody b = Body(0f);
            TreeStatusSystem.AddVenomStacks(b, 5);
            Assert.IsFalse(TreeStatusSystem.IsCorroded(b));
            Assert.AreEqual(1f, TreeStatusSystem.DamageTakenScale(b), 1e-5f);
            TreeStatusSystem.AddVenomStacks(b, 1);
            Assert.IsTrue(TreeStatusSystem.IsCorroded(b), "6 yığında zırh yok sayılır");
            Assert.AreEqual(1.2f, TreeStatusSystem.DamageTakenScale(b), 1e-5f);
            Assert.AreEqual(TreeElement.Venom, b.TintElement);
            Assert.AreEqual(0.75f, b.TintStrength, 1e-5f, "ton = yığın / tavan");
            Run(6f);
            Assert.IsFalse(TreeStatusSystem.IsCorroded(b));
            Assert.AreEqual(TreeElement.None, b.TintElement);
        }

        [Test]
        public void DamageTakenScale_FireTimesVenom_NeutralOne()
        {
            TreeStats s = Venom(Base());
            s.FireVulnerability = 1.15f;
            s.VenomArmorIgnoreStacks = 6;
            s.VenomVulnerability = 1.2f;
            Use(s);
            FakeBody b = Body(0f);
            TreeStatusSystem.Ignite(b, 1f, 3f);
            TreeStatusSystem.AddVenomStacks(b, 6);
            Assert.AreEqual(1.15f * 1.2f, TreeStatusSystem.DamageTakenScale(b), 1e-5f, "kırılganlıklar çarpılır (S5)");
            Assert.AreEqual(1f, TreeStatusSystem.DamageTakenScale(new FakeBody()), "kayıtsız gövde 1");
            Use(Base());
            Assert.AreEqual(1f, TreeStatusSystem.DamageTakenScale(b), 1e-5f, "ağaç yokken 1");
        }

        // ================================================================== buz (gövdeye yönlendirme), şok, element sırası

        [Test]
        public void Ice_RoutedToBody_FreezeRaisesProc_UltraCounted()
        {
            Use(Ice(Base()));
            TreeEvents.Proc += CountProc;
            FakeBody std = Body(0f), ultra = Body(10f, tier: EnemyTier.Ultra);
            for (int i = 0; i < 4; i++) TreeSystemsRunner.OnWeaponHit(std, std.Position, Facts());
            Assert.AreEqual(4, std.IceCalls, "isabet başına IceStacksPerHit");
            Assert.IsTrue(std.Frozen);
            Assert.AreEqual(1, TreeStatusSystem.Freezes);
            Assert.AreEqual(1, s_procs[(int)TreeProcKind.Freeze]);
            for (int i = 0; i < 4; i++) TreeSystemsRunner.OnWeaponHit(ultra, ultra.Position, Facts());
            Assert.IsFalse(ultra.Frozen);
            Assert.AreEqual(1, TreeStatusSystem.UltraSlows);
            Assert.AreEqual(1, s_procs[(int)TreeProcKind.Freeze], "Ultra donmaz: Freeze olayı yok");
        }

        [Test]
        public void Ice_NotInBuild_BodyNeverCalled()
        {
            Use(Base());
            FakeBody b = Body(0f);
            Assert.AreEqual(IceResult.None, TreeStatusSystem.ApplyIce(b, 4));
            Assert.AreEqual(0, b.IceCalls);
        }

        [Test]
        public void Shock_ChainStagger_ThroughApplyTreeStagger()
        {
            TreeStats s = Base();
            s.ShockStaggerSeconds = 0.4f;
            Use(s);
            FakeBody b = Body(2f);
            Assert.IsTrue(TreeSystemsRunner.OnChainShock(b, Vector3.zero));
            Assert.AreEqual(0.4f, b.StaggerSeconds, 1e-5f);
            Assert.Greater(b.StaggerDirection.x, 0f, "yön kaynaktan gövdeye");
            Use(Base());
            Assert.IsFalse(TreeSystemsRunner.OnChainShock(b, Vector3.zero), "nötr: sendeleme yok");
        }

        [Test]
        public void Elements_OnePerHitInTurn_FusionAll()
        {
            TreeStats s = Venom(Base());
            s.Elements = TreeElement.Fire | TreeElement.Venom;
            s.FireDotShare = 0.1f;
            s.FireSeconds = 3f;
            Use(s);
            FakeBody b = Body(0f);
            Assert.AreEqual(TreeElement.Fire, TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts()));
            Assert.AreEqual(TreeElement.Venom, TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts()));
            Assert.AreEqual(TreeElement.Fire, TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts()));
            s.Fusion = true;
            Use(s);
            Assert.AreEqual(TreeElement.Fire | TreeElement.Venom, TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts()));
        }

        [Test]
        public void ToxicRail_PiercedRoundLeavesTwoVenomStacks()
        {
            TreeStats s = Venom(Base());
            s.Elements = TreeElement.Fire | TreeElement.Venom;
            s.FireDotShare = 0.1f;
            s.FireSeconds = 3f;
            s.Synergies = TreeSynergy.ToxicRail;
            Use(s);
            FakeBody b = Body(0f);
            TreeHitFacts f = Facts();
            f.Pierced = true;
            Assert.AreEqual(TreeElement.Fire | TreeElement.Venom, TreeSystemsRunner.OnWeaponHit(b, b.Position, f));
            Assert.AreEqual(2, TreeStatusSystem.VenomStacks(b));
        }

        // ================================================================== alan havuzu

        [Test]
        public void AreaPool_FireCap8_OldestGoesOut()
        {
            Use(Base());
            var ids = new int[10];
            for (int i = 0; i < 10; i++) ids[i] = TreeAreaPool.Ignite(TreeAreaKind.Fire, new Vector3(i * 10f, 0f, 0f), 1f, 5f, 1f);
            Assert.AreEqual(TreeGuardrails.FireGroundsActive, TreeAreaPool.CountOf(TreeAreaKind.Fire));
            Assert.AreEqual(2, TreeAreaPool.Replaced);
            Assert.IsFalse(TreeAreaPool.IsLive(ids[0]), "en eskisi söner");
            Assert.IsFalse(TreeAreaPool.IsLive(ids[1]));
            Assert.IsTrue(TreeAreaPool.IsLive(ids[2]));
            Assert.IsTrue(TreeAreaPool.IsLive(ids[9]));
        }

        [Test]
        public void AreaPool_VenomCap6_SharedPoolWithinZones()
        {
            Use(Base());
            for (int i = 0; i < 7; i++) TreeAreaPool.Ignite(TreeAreaKind.Venom, new Vector3(i * 10f, 0f, 0f), 1f, 5f, 0f);
            for (int i = 0; i < 8; i++) TreeAreaPool.Ignite(TreeAreaKind.Fire, new Vector3(i * 10f, 0f, 50f), 1f, 5f, 1f);
            Assert.AreEqual(TreeGuardrails.VenomCloudsActive, TreeAreaPool.CountOf(TreeAreaKind.Venom));
            Assert.LessOrEqual(TreeAreaPool.Count, TreeGuardrails.AreaZones);
        }

        [Test]
        public void AreaPool_FireTicksEveryHalfSecond_TotalKept()
        {
            Use(Base());
            FakeBody near = Body(1f), far = Body(5f);
            TreeAreaPool.Ignite(TreeAreaKind.Fire, Vector3.zero, 2f, 3f, 10f);
            Run(0.25f);
            Assert.AreEqual(0, near.StatusCalls, "ilk tik ateşlemeden 0,5 s sonra");
            Run(2.875f);
            Assert.AreEqual(30f, near.FireDamage, 1e-3f, "6 tik × 10 × 0,5");
            Assert.AreEqual(0f, far.StatusDamage);
            Assert.AreEqual(0, TreeAreaPool.Count, "süre bitince söner");
            Assert.AreEqual(TreeDamageKind.Area, near.LastKind);
        }

        [Test]
        public void AreaPool_VenomCloud_AddsStacksPerTick()
        {
            Use(Venom(Base()));
            FakeBody b = Body(0.5f);
            TreeAreaPool.Ignite(TreeAreaKind.Venom, Vector3.zero, 2f, 4f, 0f);
            Run(2f);
            Assert.AreEqual(4, TreeStatusSystem.VenomStacks(b), "saniyede 2 yığın");
        }

        [Test]
        public void AreaPool_IgniteDuringItsOwnTick_IsDeferredNotLost()
        {
            TreeStats s = Base();
            s.Elements = TreeElement.Fire;
            s.FireDotShare = 0.25f;
            s.FireGroundRadius = 2f;
            s.FireGroundSeconds = 3f;
            Use(s);
            FakeBody b = Body(0f, hp: 3f);
            TreeStatusSystem.Ignite(b, 1f, 10f);
            TreeAreaPool.Ignite(TreeAreaKind.Fire, Vector3.zero, 2f, 3f, 10f);
            Run(0.5f);
            Assert.IsFalse(b.IsAlive, "zemin tiki öldürdü");
            Assert.AreEqual(2, TreeAreaPool.Count, "tik içinde yakılan zemin tik bitince girdi");
            Assert.AreEqual(0, TreeAreaPool.PendingCount);
            Assert.AreEqual(0, TreeAreaPool.Dropped);
        }

        // ================================================================== patlama kuyruğu

        [Test]
        public void Blasts_AtMostFourPerFrame()
        {
            Use(Base());
            for (int i = 0; i < 10; i++) TreeBlastQueue.Enqueue(TreeProcKind.ExplosiveRound, new Vector3(i * 20f, 0f, 0f), 1f, 10f);
            Frame();
            Assert.AreEqual(TreeGuardrails.TreeBlastsPerFrame, TreeBlastQueue.Detonated);
            Assert.AreEqual(6, TreeBlastQueue.Count);
        }

        [Test]
        public void Blasts_TwelvePerSecondPresented_RestLandQuietly_DamageKept()
        {
            Use(Base());
            for (int i = 0; i < 30; i++) TreeBlastQueue.Enqueue(TreeProcKind.ExplosiveRound, new Vector3(i * 20f, 0f, 0f), 1f, 10f);
            Run(0.2f, 1f / 60f);
            Assert.AreEqual(TreeGuardrails.TreeBlastsPerSecond, TreeBlastQueue.Presented);
            Assert.AreEqual(0, TreeBlastQueue.Quiet, "0,25 s dolmadan sessiz yok");
            Run(0.2f, 1f / 60f);
            Assert.AreEqual(0, TreeBlastQueue.Count);
            Assert.AreEqual(TreeGuardrails.TreeBlastsPerSecond, TreeBlastQueue.Presented, "1 s penceresinde en çok 12 sunum");
            Assert.AreEqual(18, TreeBlastQueue.Quiet);
            Assert.AreEqual(TreeGuardrails.TreeBlastsPerFrame, TreeBlastQueue.PeakPresentedPerFrame);
            Assert.AreEqual(TreeBlastQueue.DamageEnqueued, TreeBlastQueue.DamageDetonated, 1e-3f, "güç düşmez (K6)");
        }

        [Test]
        public void Blasts_QueueFull_FuseIntoNearest_DamageKept()
        {
            Use(Base());
            for (int i = 0; i < 40; i++) TreeBlastQueue.Enqueue(TreeProcKind.ExplosiveRound, new Vector3(i * 20f, 0f, 0f), 1f, 10f);
            Assert.AreEqual(TreeGuardrails.TreeBlastQueue, TreeBlastQueue.Count);
            Assert.AreEqual(8, TreeBlastQueue.OverflowMerges);
            Run(1.5f, 1f / 60f);
            Assert.AreEqual(0, TreeBlastQueue.Count);
            Assert.AreEqual(400f, TreeBlastQueue.DamageDetonated, 1e-3f);
        }

        [Test]
        public void Blasts_SameSpotFuse_OnlyWhenOverBudget()
        {
            Use(Base());
            FakeBody b = Body(0f);
            for (int i = 0; i < 3; i++) TreeBlastQueue.Enqueue(TreeProcKind.ExplosiveRound, Vector3.zero, 2f, 10f);
            Frame();
            Assert.AreEqual(0, TreeBlastQueue.Merged, "bütçe içinde birleşme yok");
            Assert.AreEqual(3, b.BlastCalls);

            for (int i = 0; i < 6; i++) TreeBlastQueue.Enqueue(TreeProcKind.ExplosiveRound, Vector3.zero, 2f, 10f);
            Frame();
            Assert.AreEqual(2, TreeBlastQueue.Merged, "6 > 4: yakın olanlar birleşir, gereği kadar");
            Assert.AreEqual(0, TreeBlastQueue.Count);
            Assert.AreEqual(90f, b.BlastDamage, 1e-3f, "hasar toplanır");
        }

        [Test]
        public void Blasts_DeeperThanGuardrail_Refused()
        {
            Use(Base());
            Assert.IsFalse(TreeBlastQueue.Enqueue(TreeProcKind.ChainBlast, Vector3.zero, 2f, 10f, TreeElement.None, TreeGuardrails.ChainDepth + 1));
            Assert.AreEqual(1, TreeBlastQueue.DepthRefused);
        }

        [Test]
        public void ChainReaction_NeverDeeperThanGuardrail()
        {
            TreeStats s = Base();
            s.ChainChance = 1f;
            s.ChainFalloff = 1f;
            s.ChainRadius = 3f;
            s.ChainHealthShare = 1f;
            Use(s);
            var line = new FakeBody[11];
            for (int i = 0; i < line.Length; i++) line[i] = Body(i * 2f, hp: 100f);
            line[0].IsAlive = false;
            TreeSystemsRunner.OnBodyDied(line[0]);   // a weapon kill: depth 0 → blast depth 1
            Run(1f);
            Assert.AreEqual(TreeGuardrails.ChainDepth, TreeBlastQueue.MaxDepthDetonated);
            Assert.IsFalse(line[3].IsAlive, "3. kuşak öldürür");
            Assert.IsTrue(line[4].IsAlive, "4. kuşak yok");
            Assert.AreEqual(3, line[3].LastDepth);
        }

        [Test]
        public void ChainReaction_ChanceFalloff_ThunderCascade()
        {
            TreeStats s = Base();
            s.ChainChance = 0.2f;
            s.ChainFalloff = 0.5f;
            Assert.AreEqual(0.2f, TreeSystemsRunner.ChainChanceAt(in s, 0), 1e-6f);
            Assert.AreEqual(0.1f, TreeSystemsRunner.ChainChanceAt(in s, 1), 1e-6f);
            Assert.AreEqual(0.05f, TreeSystemsRunner.ChainChanceAt(in s, 2), 1e-6f);
            s.Synergies = TreeSynergy.ThunderCascade;
            Assert.AreEqual(0.14f, TreeSystemsRunner.ChainChanceAt(in s, 1), 1e-6f, "Gök Şelalesi ×0,7");
            Assert.AreEqual(0.098f, TreeSystemsRunner.ChainChanceAt(in s, 2), 1e-6f);
        }

        [Test]
        public void Reentrancy_DeathInsideBlast_QueuesNeverNests()
        {
            TreeStats s = Base();
            s.ChainChance = 1f;
            s.ChainFalloff = 1f;
            s.ChainRadius = 2f;
            s.ChainHealthShare = 0.5f;
            Use(s);
            Body(0f, hp: 10f);
            TreeBlastQueue.Enqueue(TreeProcKind.ExplosiveRound, Vector3.zero, 1f, 50f);
            Frame();
            Assert.AreEqual(1, TreeBlastQueue.Detonated);
            Assert.AreEqual(0, TreeBlastQueue.NestedDetonations, "ölüm geri çağrısı patlatmaz");
            Assert.AreEqual(1, TreeBlastQueue.Count, "zincir patlaması kuyrukta");
            Assert.AreEqual(1, TreeSystemsRunner.ChainBlastsQueued);
            Frame();
            Assert.AreEqual(2, TreeBlastQueue.Detonated, "bir sonraki karede patlar");
            Assert.AreEqual(0, TreeBlastQueue.NestedDetonations);
        }

        [Test]
        public void MassDeath_SpreadOverFourFrames_DamageKept()
        {
            Use(Base());
            var ring = new FakeBody[20];
            for (int i = 0; i < ring.Length; i++)
            {
                float a = i * Mathf.PI * 2f / ring.Length;
                ring[i] = Body(Mathf.Cos(a) * 2f, Mathf.Sin(a) * 2f, hp: 50f);
            }

            TreeBlastQueue.Enqueue(TreeProcKind.MiniNuke, Vector3.zero, 5f, 100f);
            Frame();
            Assert.AreEqual(5, Dead(ring), "1. kare: ¼");
            Assert.IsTrue(TreeBlastQueue.SpreadActive);
            Frame();
            Assert.AreEqual(10, Dead(ring));
            Frame();
            Assert.AreEqual(15, Dead(ring));
            Frame();
            Assert.AreEqual(20, Dead(ring), "4 karede hepsi");
            Assert.IsFalse(TreeBlastQueue.SpreadActive);
            for (int i = 0; i < ring.Length; i++) Assert.AreEqual(100f, ring[i].BlastDamage, 1e-3f);
        }

        // ================================================================== nükleer

        [Test]
        public void Nuke_ArmsAfterN_NextHitDetonates_WithStun()
        {
            Use(Nuke(Base()));
            FakeBody b = Body(1f);
            for (int i = 0; i < 10; i++) TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts());
            Assert.IsTrue(TreeSystemsRunner.NukeArmed, "N = 10 × 1 tetik/sn");
            Assert.AreEqual(0, TreeBlastQueue.Count, "N. isabet değil, sonraki isabet");
            TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts());
            Assert.AreEqual(1, TreeSystemsRunner.Nukes);
            Frame();
            Assert.AreEqual(300f, b.BlastDamage, 1e-3f, "3 × SG");
            Assert.AreEqual(TreeStunKind.Nuke, b.StunKind);
            Assert.AreEqual(1f, b.StunSeconds, 1e-5f, "ham süre; Elite/Ultra payını gövde uygular");
        }

        [Test]
        public void Nuke_AtMostOnePerThreeSeconds()
        {
            Use(Nuke(Base()));
            FakeBody b = Body(1f);
            for (int i = 0; i < 11; i++) TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts());
            Assert.AreEqual(1, TreeSystemsRunner.Nukes);
            for (int i = 0; i < 10; i++) TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts());
            Run(1f);
            TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts());
            Assert.AreEqual(1, TreeSystemsRunner.Nukes, "soğuma bitmedi: sayaç bekler");
            Assert.IsTrue(TreeSystemsRunner.NukeArmed);
            Run(2.0625f);
            TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts());
            Assert.AreEqual(2, TreeSystemsRunner.Nukes);
        }

        [Test]
        public void Nuke_CriticalMeltdown_CritCountsThree()
        {
            TreeStats s = Nuke(Base());
            s.Synergies = TreeSynergy.CriticalMeltdown;
            Use(s);
            TreeHitFacts f = Facts();
            f.Crit = true;
            for (int i = 0; i < 3; i++) TreeSystemsRunner.OnWeaponHit(null, Vector3.zero, f);
            Assert.IsFalse(TreeSystemsRunner.NukeArmed, "9 < 10");
            TreeSystemsRunner.OnWeaponHit(null, Vector3.zero, f);
            Assert.IsTrue(TreeSystemsRunner.NukeArmed, "12 ≥ 10");
        }

        // ================================================================== kara delik

        [Test]
        public void BlackHole_OneActive_Cooldown()
        {
            Use(Hole(Base()));
            TreeSystemsRunner.OnWeaponHit(null, Vector3.zero, Facts());
            Assert.AreEqual(1, TreeGravityWells.Opened);
            Assert.AreEqual(1, TreeGravityWells.MaxActive);
            Run(1f, 0.1f);
            TreeSystemsRunner.OnWeaponHit(null, new Vector3(20f, 0f, 0f), Facts());
            Assert.AreEqual(1, TreeGravityWells.Opened, "7 s bekleme");
            Assert.IsFalse(TreeGravityWells.Open(new Vector3(20f, 0f, 0f), TreeSystemsRunner.Now), "aynı anda tek kuyu");
            Assert.AreEqual(1, TreeGravityWells.Refused);
        }

        [Test]
        public void BlackHole_TwinCores_TwoActive()
        {
            TreeStats s = Hole(Base());
            s.Core = TreeChaosCore.Nuke | TreeChaosCore.Hole;
            Use(s);
            Assert.AreEqual(TreeSystemsTuning.TwinCoresBlackHoles, TreeGravityWells.MaxActive);
            Assert.IsTrue(TreeGravityWells.Open(Vector3.zero, 0f));
            Assert.IsTrue(TreeGravityWells.Open(new Vector3(20f, 0f, 0f), 0f));
            Assert.AreEqual(2, TreeGravityWells.ActiveCount);
            Assert.IsFalse(TreeGravityWells.Open(new Vector3(40f, 0f, 0f), 0f));
        }

        [Test]
        public void BlackHole_PullsAt10Hz_RawSpeedAndHold()
        {
            TreeStats s = Hole(Base());
            s.HolePullSpeed = 4f;
            Use(s);
            FakeBody std = Body(2f), elite = Body(-2f, tier: EnemyTier.Elite), outside = Body(10f);
            TreeGravityWells.Open(Vector3.zero, 0f);
            Run(1f, 0.1f);
            Assert.AreEqual(10, std.PullCalls, "10 Hz");
            Assert.AreEqual(10, elite.PullCalls);
            Assert.AreEqual(0, outside.PullCalls);
            Assert.AreEqual(4f, elite.LastPullSpeed, 1e-5f, "ham HolePullSpeed: kademe payını gövde uygular (S4)");
            Assert.AreEqual(TreeSystemsTuning.HolePullHoldSeconds, std.LastPullHold, 1e-5f);
        }

        [Test]
        public void BlackHole_Collapses_IntoQueuedBlast()
        {
            Use(Hole(Base()));
            TreeEvents.Proc += CountProc;
            FakeBody b = Body(1f);
            TreeGravityWells.Open(Vector3.zero, 0f);
            Run(2.6f, 0.1f);
            Assert.AreEqual(1, TreeGravityWells.Collapsed);
            Assert.AreEqual(0, TreeGravityWells.ActiveCount);
            Assert.AreEqual(1, s_procs[(int)TreeProcKind.BlackHoleCollapse]);
            Assert.AreEqual(150f, b.LargestHit, 1e-3f, "çöküş 1,5 × SG");
            Assert.AreEqual(150f, b.StatusDamage, 1e-3f, "içeride 0,6 × SG/sn × 2,5 s (5 tik × 30)");
        }

        [Test]
        public void FrozenGravity_FreezesAfterOneSecond_CollapseBonus_IceBlast()
        {
            TreeStats s = Ice(Hole(Base()));
            s.Synergies = TreeSynergy.FrozenGravity;
            Use(s);
            FakeBody b = Body(1f);
            TreeGravityWells.Open(Vector3.zero, 0f);
            Run(0.8f, 0.1f);
            Assert.IsFalse(b.Frozen, "1 s dolmadı");
            Run(0.4f, 0.1f);
            Assert.IsTrue(b.Frozen);
            Assert.AreEqual(1, TreeGravityWells.FrozenByGravity);
            Assert.IsTrue(b.LastIgnoreCooldown, "Frozen Gravity freezes through the cooldown (S-03 ApplyIce overload)");
            Run(1.4f, 0.1f);
            Assert.AreEqual(225f, b.LargestHit, 1e-3f, "çöküş donmuşa ×1,5");
            Assert.AreEqual(100f, b.IceDamage, 1e-3f, "3 m buz patlaması 1,0 × SG");
        }

        // ================================================================== ölüm procları, çapa nabızları, S-02 yardımcıları

        [Test]
        public void Shatter_FrozenAtDeath_QueuesIceBlastWithStacks()
        {
            TreeStats s = Ice(Base());
            s.ShatterRadius = 3f;
            s.ShatterShare = 1f;
            Use(s);
            FakeBody dead = Body(0f), near = Body(1f);
            dead.IsAlive = false;
            dead.FrozenAtDeathFlag = true;
            TreeSystemsRunner.OnBodyDied(dead);
            Frame();
            Assert.AreEqual(100f, near.IceDamage, 1e-3f);
            Assert.AreEqual(1, near.IceCalls, "çevreye 2 yığın");
            Assert.AreEqual(TreeSystemsTuning.ShatterIceStacks, near.IceStacksGiven);
        }

        [Test]
        public void DoomsdayBell_StunKindBell_ElementNova_FullStates()
        {
            TreeStats s = Ice(Base());
            s.Elements = TreeElement.Fire | TreeElement.Ice;
            s.FireDotShare = 0.1f;
            s.FireSeconds = 3f;
            s.BellCooldown = 45f;
            s.BellRadius = 12f;
            s.BellShare = 6f;
            s.BellStunSeconds = 2f;
            s.NovaCooldown = 8f;
            s.NovaRadius = 6f;
            s.NovaShare = 2.5f;
            Use(s);
            FakeBody std = Body(2f), elite = Body(-2f, tier: EnemyTier.Elite);
            TreeSystemsRunner.SetAnchor(Vector3.zero);
            Frame();
            Frame();
            Assert.AreEqual(1, TreeSystemsRunner.Bells);
            Assert.AreEqual(1, TreeSystemsRunner.Novas);
            Assert.AreEqual(TreeStunKind.Bell, elite.StunKind);
            Assert.AreEqual(2f, elite.StunSeconds, 1e-5f, "ham; StunHeavyShare gövdede");
            Assert.AreEqual(600f + 250f, std.BlastDamage, 1e-3f);
            Assert.IsTrue(TreeStatusSystem.IsBurning(std), "nova: ateş durumu");
            Assert.AreEqual(s.IceMaxStacks, std.IceStacksGiven, "nova: tam buz yığını");
            Run(1f);
            Assert.AreEqual(1, TreeSystemsRunner.Bells, "45 s bekleme");
        }

        [Test]
        public void GuardWave_StoneGuardianAndUndying_StunAndPushAway_NoSecondProc()
        {
            TreeStats s = Base();
            s.BulwarkRadius = 4f;
            s.BulwarkStunSeconds = 1f;
            Use(s);
            Assert.IsTrue(TreeSystemsRunner.Needed(in s), "a bulwark build needs the systems");
            FakeBody near = Body(2f), far = Body(9f);
            TreeSystemsRunner.SetAnchor(Vector3.zero);
            int procs = TreeEvents.ProcCount;
            Assert.IsTrue(TreeSystemsRunner.GuardWave(Vector3.zero, s.BulwarkRadius, s.BulwarkStunSeconds));
            Frame();
            Assert.AreEqual(TreeStunKind.Bulwark, near.StunKind);
            Assert.AreEqual(1f, near.StunSeconds, 1e-5f);
            Assert.AreEqual(1, near.PullCalls, "pushed");
            Assert.Greater((near.LastPullCenter - Vector3.zero).magnitude, near.Position.magnitude, "the push aims beyond the body, away from the player");
            Assert.AreEqual(0, far.StunCalls, "outside the radius");
            Assert.AreEqual(0, far.PullCalls);
            Assert.AreEqual(procs, TreeEvents.ProcCount, "the source (TreeHooks) already raised the moment: no second proc");
            Assert.AreEqual(0f, near.BlastDamage, "a guard wave deals no damage");
            Assert.IsFalse(TreeSystemsRunner.GuardWave(Vector3.zero, 0f, 1f), "no radius, no wave");
        }

        [Test]
        public void ExplosiveRound_WildfireRounds_LeavesGround()
        {
            TreeStats s = Base();
            s.ExplosiveRadius = 2f;
            s.ExplosiveAreaShare = 0.5f;
            s.FireDotShare = 0.25f;
            s.Synergies = TreeSynergy.WildfireRounds;
            Use(s);
            FakeBody b = Body(1f);
            Assert.IsTrue(TreeBlastQueue.EnqueueExplosiveRound(Vector3.zero, 100f));
            Frame();
            Assert.AreEqual(50f, b.BlastDamage, 1e-3f);
            Assert.AreEqual(1, TreeAreaPool.CountOf(TreeAreaKind.Fire));
        }

        [Test]
        public void Barrage_EveryFourthRocket_SplitsIntoThree()
        {
            TreeStats s = Base();
            s.Synergies = TreeSynergy.Barrage;
            Use(s);
            for (int i = 0; i < 3; i++) Assert.AreEqual(0, TreeBlastQueue.OnBarrageRocketImpact(Vector3.zero, 100f));
            Assert.AreEqual(3, TreeBlastQueue.OnBarrageRocketImpact(Vector3.zero, 100f));
            Assert.AreEqual(3, TreeBlastQueue.Count);
            Assert.AreEqual(90f, TreeBlastQueue.DamageEnqueued, 1e-3f, "3 × 0,3 × roket");
        }

        // ================================================================== korkuluklar ve nötr

        [Test]
        public void OverlapQueries_NeverAboveGuardrailPerFrame()
        {
            Use(Venom(Base()));
            for (int i = 0; i < 8; i++) TreeAreaPool.Ignite(TreeAreaKind.Fire, new Vector3(i * 10f, 0f, 0f), 1f, 3f, 1f);
            for (int i = 0; i < 6; i++) TreeAreaPool.Ignite(TreeAreaKind.Venom, new Vector3(i * 10f, 0f, 30f), 1f, 3f, 0f);
            Run(0.4375f);
            for (int i = 0; i < 20; i++) TreeBlastQueue.Enqueue(TreeProcKind.ExplosiveRound, new Vector3(i * 10f, 0f, 60f), 1f, 1f);
            Run(2f);
            Assert.LessOrEqual(TreeTargetQuery.PeakPerFrame, TreeGuardrails.OverlapQueriesPerFrame);
            Assert.Greater(TreeTargetQuery.Deferred, 0, "bütçe gerçekten zorlandı");
            Assert.AreEqual(0, TreeBlastQueue.Count);
        }

        [Test]
        public void Neutral_NothingHappens()
        {
            Use(Base());
            FakeBody b = Body(0f);
            TreeSystemsRunner.SetAnchor(Vector3.zero);
            Assert.AreEqual(TreeElement.None, TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts()));
            Assert.IsFalse(TreeBlastQueue.EnqueueExplosiveRound(Vector3.zero, 100f));
            Assert.IsFalse(TreeBlastQueue.EnqueueCritRing(Vector3.zero, 100f));
            Assert.AreEqual(0, TreeBlastQueue.OnBarrageRocketImpact(Vector3.zero, 100f));
            Assert.IsFalse(TreeSystemsRunner.Needed(TreeStats.Neutral));
            Run(2f);
            Assert.AreEqual(0, b.StatusCalls + b.BlastCalls + b.IceCalls + b.PullCalls + b.StunCalls + b.StaggerCalls);
            Assert.AreEqual(0, TreeStatusSystem.StatusBodies);
            Assert.AreEqual(0, TreeAreaPool.Count);
            Assert.AreEqual(0, TreeGravityWells.ActiveCount);
            Assert.AreEqual(0, TreeSystemsRunner.Nukes + TreeSystemsRunner.Novas + TreeSystemsRunner.Bells);
        }

        [Test]
        public void Reset_ClearsEverySystem()
        {
            Use(Nuke(Hole(Venom(Base()))));
            FakeBody b = Body(0f);
            TreeStatusSystem.AddVenomStacks(b, 3);
            TreeAreaPool.Ignite(TreeAreaKind.Fire, Vector3.zero, 1f, 3f, 1f);
            TreeBlastQueue.Enqueue(TreeProcKind.ExplosiveRound, Vector3.zero, 1f, 1f);
            TreeGravityWells.Open(Vector3.zero, 0f);
            TreeSystemsRunner.Reset();
            Assert.AreEqual(0, TreeStatusSystem.ActiveSlots);
            Assert.AreEqual(TreeStatusSystem.NoSlot, TreeStatusSystem.SlotOf(b));
            Assert.AreEqual(0, TreeAreaPool.Count);
            Assert.AreEqual(0, TreeBlastQueue.Count);
            Assert.AreEqual(0, TreeGravityWells.ActiveCount);
            Assert.AreEqual(0, TreeTargetQuery.PeakPerFrame);
        }

        [Test]
        public void HotLoop_FortyBodies_NoCallingThreadAllocation()
        {
            TreeStats s = Nuke(Hole(Ice(Venom(Base()))));
            s.Elements = TreeElement.Fire | TreeElement.Ice | TreeElement.Venom;
            s.FireDotShare = 0.25f;
            s.FireSeconds = 3f;
            s.FireSpreadRadius = 1.5f;
            s.ExplosiveRadius = 1.5f;
            s.ExplosiveAreaShare = 0.5f;
            Use(s);
            var bodies = new FakeBody[40];
            for (int i = 0; i < bodies.Length; i++) bodies[i] = Body((i % 8) * 1.2f, (i / 8) * 1.2f, hp: 1e9f);
            TreeHitFacts facts = Facts();
            float now = 0f;

            for (int f = 0; f < 120; f++)   // warm every path once (spreads, zones, wells, nuke, full queue)
            {
                now += 1f / 60f;
                for (int i = 0; i < bodies.Length; i += 4) TreeSystemsRunner.OnWeaponHit(bodies[i], bodies[i].Position, facts);
                TreeBlastQueue.EnqueueExplosiveRound(bodies[f % 40].Position, 50f);
                TreeSystemsRunner.Tick(1f / 60f, now);
            }

            byte[] control = null;
            Assert.That(() => { control = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "pozitif kontrol: çağıran iş parçacığı GC.Alloc kaydı ayırmayı görmeli");
            Assert.That(() =>
            {
                for (int f = 0; f < 200; f++)
                {
                    now += 1f / 60f;
                    for (int i = 0; i < bodies.Length; i += 4) TreeSystemsRunner.OnWeaponHit(bodies[i], bodies[i].Position, facts);
                    TreeBlastQueue.EnqueueExplosiveRound(bodies[f % 40].Position, 50f);
                    TreeSystemsRunner.Tick(1f / 60f, now);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "ağaç sistemleri sıcak döngüde yönetilen bellek ayırdı");
            Assert.Greater(TreeStatusSystem.DotTicks, 0, "döngü DoT yolunu gerçekten çalıştırdı");
            Assert.Greater(TreeBlastQueue.Detonated, 0);
            Assert.Greater(TreeGravityWells.PullsApplied, 0);
            System.GC.KeepAlive(control);
        }

        // ================================================================== S-03 taslağıyla uyum kuralları (C-00 12.10)

        [Test]
        public void Rule_a_DeathInsideDotTick_SlotFreedAfterTheLoop()
        {
            Use(Base());
            FakeBody dying = Body(0f, hp: 1f), other = Body(1f), same = Body(2f), sibling = Body(3f), samePhase = Body(4f);
            TreeStatusSystem.Ignite(dying, 100f, 3f);
            TreeStatusSystem.Ignite(samePhase, 10f, 3f);   // slot 4: the same phase as slot 0
            Frame();
            Assert.IsFalse(dying.IsAlive);
            Assert.AreEqual(1, dying.PendingAtDeath, "ölüm tik döngüsü içindeyken slot hâlâ bekleyen-bırak listesinde");
            Assert.AreEqual(0, TreeStatusSystem.PendingReleases, "döngü bitince boşaldı");
            Assert.AreEqual(1, TreeStatusSystem.DeferredReleases);
            Assert.AreEqual(1, samePhase.StatusCalls, "aynı fazdaki sonraki gövde tikini aldı");
            Assert.AreEqual(4, TreeStatusSystem.ActiveSlots);
            Assert.AreEqual(TreeStatusSystem.NoSlot, dying.TreeSlot);
            Assert.AreEqual(0, other.StatusCalls + same.StatusCalls + sibling.StatusCalls);
        }

        [Test]
        public void Rule_b_OneStatusCallPerBodyPerTick_DominantElement()
        {
            Use(Venom(Base()));
            FakeBody b = Body(0f);
            TreeStatusSystem.Ignite(b, 10f, 3f);
            TreeStatusSystem.AddVenomStacks(b, 1);
            Frame();
            Assert.AreEqual(1, b.StatusCalls, "yanma + zehir tek çağrı");
            Assert.AreEqual(2.5f + 1f, b.LastStatusAmount, 1e-4f, "tutarlar toplanır");
            Assert.AreEqual(TreeElement.Fire, b.LastStatusElement, "baskın pay: ateş 2,5 > zehir 1");
            TreeStatusSystem.AddVenomStacks(b, 7);
            Run(0.25f);
            Assert.AreEqual(2, b.StatusCalls);
            Assert.AreEqual(TreeElement.Venom, b.LastStatusElement, "8 yığın × 4 × 0,25 = 8 > 2,5");
        }

        [Test]
        public void Rule_c_PullSpeedPassedWithoutShare_EveryTier()
        {
            TreeStats s = Hole(Base());
            s.HolePullSpeed = 4f;
            s.HolePullSpecialShare = 0f;   // an immune tier is the body's call, not S-04's
            s.HolePullHeavyShare = 0.2f;
            Use(s);
            FakeBody std = Body(2f), special = Body(-2f, tier: EnemyTier.Special), ultra = Body(0f, 2f, EnemyTier.Ultra);
            TreeGravityWells.Open(Vector3.zero, 0f);
            Frame(0.1f);
            Assert.AreEqual(4f, std.LastPullSpeed, 1e-5f);
            Assert.AreEqual(4f, special.LastPullSpeed, 1e-5f, "pay 0 olsa da çağrı paysız gider; bağışıklığı alıcı uygular");
            Assert.AreEqual(4f, ultra.LastPullSpeed, 1e-5f);
        }

        [Test]
        public void Rule_d_FreezeCooldownIsTheBodys_S04KeepsNoFreezeClock()
        {
            Use(Ice(Base()));
            TreeEvents.Proc += CountProc;
            FakeBody b = Body(0f);
            b.RefuseFreeze = true;   // the body's cooldown (counted from the end of its last freeze) is running
            for (int i = 0; i < 8; i++) TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts());
            Assert.AreEqual(8, b.IceCalls, "S-04 her isabeti gövdeye geçirir, kendi bekleme saati yok");
            Assert.AreEqual(0, TreeStatusSystem.Freezes);
            Assert.AreEqual(0, s_procs[(int)TreeProcKind.Freeze]);
            b.RefuseFreeze = false;
            TreeSystemsRunner.OnWeaponHit(b, b.Position, Facts());
            Assert.AreEqual(1, TreeStatusSystem.Freezes, "donmayı gövde bildirince olay");
            Assert.AreEqual(1, s_procs[(int)TreeProcKind.Freeze]);
            Assert.AreEqual(TreeElement.Ice, b.TintElement, "donmuş gövdenin tonu buz");
        }

        [Test]
        public void Rule_e_NeutralStunHeavyShareHalf_StunSecondsPassedRaw()
        {
            Assert.AreEqual(0.5f, TreeStats.Neutral.StunHeavyShare, 1e-6f, "B1b: nötrde Elite/Ultra stun payı 0,5");
            Use(Nuke(Base()));
            FakeBody elite = Body(1f, tier: EnemyTier.Elite);
            for (int i = 0; i < 11; i++) TreeSystemsRunner.OnWeaponHit(elite, elite.Position, Facts());
            Frame();
            Assert.AreEqual(1f, elite.StunSeconds, 1e-5f, "S-04 ham süreyi geçirir; pay alıcıda");
            Assert.AreEqual(TreeStunKind.Nuke, elite.StunKind);
        }

        // ================================================================== B6 uyanış alternatifleri

        [Test]
        public void Uyanis_ElementAvatari_FormdaHerIsabetNova_KisitliVeSureli()
        {
            TreeStats s = Base();
            s.Elements = TreeElement.Fire;
            s.FireDotShare = 0.1f;
            s.FireSeconds = 3f;
            s.AvatarCooldown = 25f;
            s.AvatarSeconds = 10f;
            s.AvatarRadius = 2f;
            s.AvatarShare = 0.4f;
            Use(s);
            Assert.IsTrue(TreeSystemsRunner.Needed(in s));
            FakeBody a = Body(0f), near = Body(1.5f), far = Body(5f);
            TreeSystemsRunner.SetAnchor(Vector3.zero);
            Frame();
            Assert.AreEqual(1, TreeSystemsRunner.AvatarForms);
            Assert.IsTrue(TreeSystemsRunner.AvatarActive);

            TreeSystemsRunner.OnWeaponHit(a, a.Position, Facts());
            TreeSystemsRunner.OnWeaponHit(a, a.Position, Facts());
            Frame();
            Assert.AreEqual(1, TreeSystemsRunner.AvatarPulses, "0,2 sn kısıt: aynı anda ikinci nova yok");
            Assert.AreEqual(40f * TreeStatusSystem.ElementScale(in s), near.BlastDamage, 1e-3f, "0,4 × SG");
            Assert.IsTrue(TreeStatusSystem.IsBurning(near), "nova element durumunu verir");
            Assert.AreEqual(0f, far.BlastDamage, "2 m dışında");

            Run(0.25f);
            TreeSystemsRunner.OnWeaponHit(a, a.Position, Facts());
            Frame();
            Assert.AreEqual(2, TreeSystemsRunner.AvatarPulses);

            Run(10f);
            Assert.IsFalse(TreeSystemsRunner.AvatarActive, "10 sn form");
            TreeSystemsRunner.OnWeaponHit(a, a.Position, Facts());
            Frame();
            Assert.AreEqual(2, TreeSystemsRunner.AvatarPulses, "form dışında nova yok");
            Run(15f);
            Assert.AreEqual(2, TreeSystemsRunner.AvatarForms, "25 sn bekleme");
        }

        [Test]
        public void Uyanis_OlayUfku_OyuncununEtrafindaDonerCekerHasarVerir_SlotDisi()
        {
            TreeStats s = Base();
            s.HorizonRadius = 2f;
            s.HorizonShare = 0.4f;
            s.HorizonOrbit = 3f;
            Use(s);
            Assert.IsTrue(TreeSystemsRunner.Needed(in s));
            FakeBody onPath = Body(3f), behind = Body(-3f);
            Frame();
            Assert.IsFalse(TreeGravityWells.HorizonActive, "oyuncu yokken kuyu yok");

            TreeSystemsRunner.SetAnchor(Vector3.zero);
            Frame();
            Assert.IsTrue(TreeGravityWells.HorizonActive);
            Assert.AreEqual(3f, TreeGravityWells.HorizonCenter.magnitude, 1e-3f, "yörünge 3 m");
            Assert.AreEqual(1, onPath.PullCalls);
            Assert.AreEqual(TreeGravityWells.PullSpeed(in s) * TreeSystemsTuning.HorizonPullShare, onPath.LastPullSpeed, 1e-5f, "küçük kuyu");
            Run(0.6f);
            Assert.AreEqual(20f, onPath.StatusDamage, 1e-3f, "0,4 × SG × 0,5 sn tik");
            Assert.AreEqual(TreeDamageKind.Hole, onPath.LastKind);
            Assert.AreEqual(0, behind.PullCalls, "yörüngenin öbür yanı");
            Assert.AreEqual(0, TreeGravityWells.ActiveCount, "kara delik slotlarını tutmaz");

            Vector3 before = TreeGravityWells.HorizonCenter;
            Run(1f);
            Assert.Greater((TreeGravityWells.HorizonCenter - before).magnitude, 0.5f, "döner");
            TreeSystemsRunner.ClearAnchor();
            Frame();
            Assert.IsFalse(TreeGravityWells.HorizonActive);
        }

        [Test]
        public void Uyanis_AnkaPatlamasi_AltiMetreAtesIttirmeTekOlay()
        {
            TreeStats s = Base();
            s.PhoenixRadius = 6f;
            Use(s);
            Assert.IsTrue(TreeSystemsRunner.Needed(in s));
            FakeBody near = Body(3f), far = Body(9f);
            TreeSystemsRunner.SetAnchor(Vector3.zero);
            int procs = TreeEvents.ProcCount;
            Assert.IsTrue(TreeSystemsRunner.PhoenixBlast(Vector3.zero, 6f, 1.5f));
            Frame();
            Assert.AreEqual(150f, near.BlastDamage, 1e-3f, "1,5 × SG");
            Assert.AreEqual(1, near.PullCalls, "itilir");
            Assert.AreEqual(0f, far.BlastDamage);
            Assert.AreEqual(procs, TreeEvents.ProcCount, "anı kanca yükseltti: ikinci olay yok");
            Assert.AreEqual(1, TreeSystemsRunner.PhoenixBlasts);
            Assert.IsFalse(TreeSystemsRunner.PhoenixBlast(Vector3.zero, 0f, 1.5f));
        }

        // ================================================================== yardımcılar

        private void Frame(float dt = Step)
        {
            _now += dt;
            TreeSystemsRunner.Tick(dt, _now);
        }

        private void Run(float seconds, float dt = Step)
        {
            int n = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < n; i++) Frame(dt);
        }

        private static void Use(TreeStats s) => TreeRuntime.SetTestStats(in s);

        private static void CountProc(TreeProcInfo info) => s_procs[(int)info.Kind]++;

        private static TreeHitFacts Facts() => new TreeHitFacts { PullsPerSecond = 1f, HitWeight = 1f };

        private static TreeStats Base()
        {
            TreeStats s = TreeStats.Neutral;
            s.WeaponPower = 100f;
            return s;
        }

        private static TreeStats Venom(TreeStats s)
        {
            s.Elements |= TreeElement.Venom;
            s.VenomStackShare = 0.04f;
            s.VenomMaxStacks = 8;
            s.VenomStacksPerHit = 1;
            s.VenomSeconds = 5f;
            return s;
        }

        private static TreeStats Ice(TreeStats s)
        {
            s.Elements |= TreeElement.Ice;
            s.IceSlowPerStack = 0.1f;
            s.IceMaxStacks = 4;
            s.IceStacksPerHit = 1;
            s.IceSeconds = 2.5f;
            s.FreezeSeconds = 1.5f;
            s.FreezeCooldown = 4f;
            return s;
        }

        private static TreeStats Nuke(TreeStats s)
        {
            s.Core |= TreeChaosCore.Nuke;
            s.NukeHitsPerPullRate = 10f;
            s.NukeRadius = 5f;
            s.NukeShare = 3f;
            s.NukeStunSeconds = 1f;
            return s;
        }

        private static TreeStats Hole(TreeStats s)
        {
            s.Core |= TreeChaosCore.Hole;
            s.HoleCooldown = 7f;
            s.HoleRadius = 3f;
            s.HoleSeconds = 2.5f;
            s.HoleShare = 0.6f;
            s.HoleCollapseShare = 1.5f;
            return s;
        }

        private static FakeBody Body(float x, float z = 0f, EnemyTier tier = EnemyTier.Standard, float hp = 1000f)
        {
            var b = new FakeBody { Position = new Vector3(x, 0f, z), Tier = tier, Health = hp, MaxHealth = hp };
            Assert.IsTrue(TreeStatusSystem.Register(b));
            return b;
        }

        private static int Dead(FakeBody[] bodies)
        {
            int n = 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (!bodies[i].IsAlive) n++;
            }

            return n;
        }

        /// <summary>
        /// S-03 yüzeyinin en yalın taklidi: buz yığını tavanda donar (bekleme yok; Ultra donmaz), stun Max, çekim kaydedilir,
        /// can biterse Die gibi <see cref="TreeSystemsRunner.OnBodyDied"/> çağrılır (gerçekte TreeStatusEnded → bağlama).
        /// </summary>
        private sealed class FakeBody : ITreeStatusReceiver
        {
            public Component Unit => null;

            public int TreeSlot { get; set; } = TreeStatusSystem.NoSlot;
            public EnemyTier Tier { get; set; }
            public bool IsAlive { get; set; } = true;
            public Vector3 Position { get; set; }
            public float MaxHealth { get; set; } = 1000f;
            public float Health = 1000f;
            public bool NotifyDeath = true;

            public float BlastDamage, StatusDamage, FireDamage, VenomDamage, IceDamage, LargestHit;
            public int BlastCalls, StatusCalls;
            public int LastDepth = -1;
            public TreeDamageKind LastKind;

            public int IceMax = 4, Stacks, IceCalls, IceStacksGiven;
            public bool Frozen, FrozenAtDeathFlag, RefuseFreeze;
            public int PendingAtDeath = -1;
            public TreeElement LastStatusElement;
            public float LastStatusAmount;
            public float StunSeconds;
            public TreeStunKind StunKind;
            public int StunCalls;
            public float StaggerSeconds;
            public Vector3 StaggerDirection;
            public int StaggerCalls;
            public int PullCalls;
            public float LastPullSpeed, LastPullHold;
            public TreeElement TintElement;
            public float TintStrength;
            public int TintCalls;

            public bool IsFrozen => Frozen;
            public bool FrozenAtDeath => FrozenAtDeathFlag;
            public int IceStacks => Frozen ? 0 : Stacks;

            public DamageResult ApplyExplosion(float totalDamage, Vector3 origin)
            {
                if (!IsAlive) return default;
                BlastCalls++;
                BlastDamage += totalDamage;
                return Take(totalDamage, TreeDamageContext.Element);
            }

            public DamageResult ApplyStatusDamage(float amount, TreeElement element)
            {
                if (!IsAlive) return default;
                StatusCalls++;
                StatusDamage += amount;
                LastStatusElement = element;
                LastStatusAmount = amount;
                return Take(amount, element);
            }

            private DamageResult Take(float amount, TreeElement element)
            {
                LastDepth = TreeDamageContext.Depth;
                LastKind = TreeDamageContext.Kind;
                if (amount > LargestHit) LargestHit = amount;
                if (element == TreeElement.Fire) FireDamage += amount;
                if (element == TreeElement.Venom) VenomDamage += amount;
                if (element == TreeElement.Ice) IceDamage += amount;
                Health -= amount;
                if (Health > 0f) return new DamageResult { HealthDamage = amount };
                IsAlive = false;
                FrozenAtDeathFlag = Frozen;
                if (NotifyDeath)
                {
                    TreeSystemsRunner.OnBodyDied(this);   // S-03: TreeStatusEnded fires synchronously inside the lethal call
                    PendingAtDeath = TreeStatusSystem.PendingReleases;
                }

                return new DamageResult { HealthDamage = amount, Killed = true };
            }

            public bool ApplyTreeStagger(float seconds, Vector3 direction)
            {
                StaggerCalls++;
                StaggerSeconds = seconds;
                StaggerDirection = direction;
                return true;
            }

            public bool LastIgnoreCooldown;

            public IceResult ApplyIce(int stacks, bool ignoreCooldown = false)
            {
                IceCalls++;
                LastIgnoreCooldown = ignoreCooldown;
                IceStacksGiven = stacks;
                if (!IsAlive || stacks <= 0) return IceResult.None;
                if (Frozen) return IceResult.None;
                Stacks = Mathf.Min(IceMax, Stacks + stacks);
                if (Stacks < IceMax || RefuseFreeze) return IceResult.Slowed;   // RefuseFreeze: the body's own cooldown
                Stacks = 0;
                if (Tier == EnemyTier.Ultra) return IceResult.UltraSlowed;
                Frozen = true;
                return IceResult.Frozen;
            }

            public bool ApplyStun(float seconds, TreeStunKind kind)
            {
                StunCalls++;
                StunSeconds = Mathf.Max(StunSeconds, seconds);
                StunKind = kind;
                return true;
            }

            public Vector3 LastPullCenter;

            public void ApplyPull(Vector3 center, float speed, float hold)
            {
                PullCalls++;
                LastPullCenter = center;
                LastPullSpeed = speed;
                LastPullHold = hold;
            }

            public void SetStatusTint(TreeElement element, float strength)
            {
                TintCalls++;
                TintElement = element;
                TintStrength = strength;
            }
        }
    }
}
