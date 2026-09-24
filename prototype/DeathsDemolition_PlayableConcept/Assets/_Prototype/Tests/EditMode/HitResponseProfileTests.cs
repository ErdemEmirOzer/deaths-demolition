using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B3 · §3.1 on the unchanged REQ-121..129 accumulator: automatic classes never stagger at their steady state
    /// (AR 1.0 @ 600 rpm, SMG 0.55 @ 900, LMG 0.8 @ 750), one shotgun shell staggers and earns the knockback, the Ultra
    /// is never pushed, the governor damps a double shell, and the 0.6 m / 0.25 s ease-out profile integrates to its distance.
    /// </summary>
    [Category("WP076")]
    public sealed class HitResponseProfileTests
    {
        private static DamageHit Hit(float impact, DamageSourceKind source = DamageSourceKind.Bullet) =>
            new DamageHit { Amount = 1f, Region = BodyRegion.UpperBody, Direction = Vector3.forward, ImpactForce = impact, Source = source };

        private static readonly DamageResult Open = new DamageResult { HealthDamage = 1f, Region = BodyRegion.UpperBody };

        private static EnemyHitReactionState State(EnemyTier tier)
        {
            var s = new EnemyHitReactionState();
            s.ApplyConfig(PrototypeConfig.Active, tier);
            s.ResetForSpawn();
            return s;
        }

        private static int Staggers(EnemyTier tier, float impact, float rpm, float seconds)
        {
            EnemyHitReactionState s = State(tier);
            float step = 1f / 60f, gap = 60f / rpm, next = 0f; int staggers = 0;
            for (float t = 0f; t < seconds; t += step)
            {
                s.Tick(step);
                while (next <= t) { if (s.Evaluate(Hit(impact), Open, false, false, false, false).Stagger) staggers++; next += gap; }
            }
            return staggers;
        }

        [Test]
        public void KararliDurum_OtomatikSiniflarSendelemez()
        {
            Assert.IsNotNull(PrototypeConfig.Active);
            Assert.AreEqual(0, Staggers(EnemyTier.Standard, 1.0f, 600f, 3f), "AR 1,0 @ 600 rpm");
            Assert.AreEqual(0, Staggers(EnemyTier.Standard, 0.55f, 900f, 3f), "SMG 0,55 @ 900 rpm");
            Assert.AreEqual(0, Staggers(EnemyTier.Standard, 0.80f, 750f, 3f), "LMG 0,8 @ 750 rpm");
            // the table's steady state: f / (1 − 0.5^(Δt/0.12))
            Assert.Less(1.0f / (1f - Mathf.Pow(0.5f, 0.1f / 0.12f)), EnemyHitReactionState.StaggerImpactThreshold);
        }

        [Test]
        public void Pompali_SendelerVeIter_UltraAsla_Elite()
        {
            EnemyHitReactionState std = State(EnemyTier.Standard);
            int staggerAt = -1, shoveAt = -1, shoves = 0;
            for (int i = 0; i < 8; i++)
            {
                HitReaction r = std.Evaluate(Hit(0.85f, DamageSourceKind.Pellet), Open, false, false, false, false);
                if (r.Stagger && staggerAt < 0) staggerAt = i;
                if (r.KnockbackEarned) { shoves++; if (shoveAt < 0) shoveAt = i; }
            }
            Assert.AreEqual(2, staggerAt, "3. saçmada sendeleme (2,55)");
            Assert.AreEqual(5, shoveAt, "aynı şelin 6. saçması 4,5'i geçer → itme");
            Assert.AreEqual(1, shoves, "şel başına tek itme");
            EnemyHitReactionState eliteShell = State(EnemyTier.Elite);
            bool elitePushed = false;
            for (int i = 0; i < 8; i++) elitePushed |= eliteShell.Evaluate(Hit(0.85f, DamageSourceKind.Pellet), Open, false, false, false, true).KnockbackEarned;
            Assert.IsFalse(elitePushed, "Elite: 6,8 × 0,45 = 3,06 < 4,5 → sendeler, itilmez");

            EnemyHitReactionState ultra = State(EnemyTier.Ultra);
            bool pushed = false, staggered = false;
            for (int i = 0; i < 8; i++) { HitReaction r = ultra.Evaluate(Hit(0.85f, DamageSourceKind.Pellet), Open, true, false, false, true); pushed |= r.KnockbackEarned; staggered |= r.Stagger; }
            Assert.IsFalse(pushed || staggered, "Ultra: 6,8 × 0,12 × 1,35 < 2,4 → asla");
            Assert.AreEqual(0f, HitReactionProfiles.KnockbackTierScale(EnemyTier.Ultra));
            Assert.AreEqual(0.5f, HitReactionProfiles.KnockbackTierScale(EnemyTier.Elite));
            Assert.AreEqual(0f, HitReactionProfiles.SuppressionTierScale(EnemyTier.Ultra));

            EnemyHitReactionState elite = State(EnemyTier.Elite);
            Assert.IsFalse(elite.Evaluate(Hit(3.2f), Open, false, false, false, true).Stagger, "Elite: 3,2 × 0,45 = 1,44 < 2,4 (sniper ağır flinch, sendeleme yok)");
            Assert.AreEqual(1.33f, HitReactionProfiles.SniperHeavyFlinch, 1e-6f);
            Assert.IsTrue(State(EnemyTier.Standard).Evaluate(Hit(3.2f), Open, false, false, false, false).Stagger, "Standard sniper → her zaman");
        }

        [Test]
        public void Governor_CiftSel_IkinciKisilir()
        {
            StaggerGovernor.Clear();
            float first = 0f, second = 0f;
            for (int i = 0; i < 8; i++) first += StaggerGovernor.Govern(77, 0.85f);
            for (int i = 0; i < 8; i++) second += StaggerGovernor.Govern(77, 0.85f);
            Assert.AreEqual(6.8f, first, 1e-4f, "ilk şel dokunulmaz");
            Assert.Less(second, 6.8f * 0.5f, "ikinci şel tavanı aşınca %15");
            StaggerGovernor.Clear();
        }

        [Test]
        public void ItmeProfili_KolaylasanHiz_MesafeyeIntegralEder()
        {
            KnockbackProfile p = HitReactionProfiles.Knockback(DD.Prototype.Core.Weapons.WeaponClass.Shotgun, true);
            Assert.AreEqual(0.6f, p.Distance, 1e-6f); Assert.AreEqual(0.25f, p.Seconds, 1e-6f);
            Assert.AreEqual(4.8f, p.SpeedAt(0f), 1e-4f, "2D/T");
            float d = 0f, dt = 1f / 240f;
            for (float t = 0f; t < 0.3f; t += dt) d += p.SpeedAt(t) * dt;
            Assert.AreEqual(0.6f, d, 0.02f);
            Assert.AreEqual(1.2f, HitReactionProfiles.Knockback(DD.Prototype.Core.Weapons.WeaponClass.Launcher, true).Distance, 1e-6f);
            Assert.AreEqual(0f, p.SpeedAt(0.25f));
        }
    }
}
