using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B2 · Special rezonansı okuma noktaları (KARAR-WP104 §6, S-04 b3d2ed5f gövdeleri): nötr ağaçta Raiju zinciri veri
    /// kadar gövdeye sıçrar ve süre sınırı veri kadardır; Storm Chain sahibinde (+2 sıçrama, süre ×1,3) zincir iki gövde daha
    /// tutar (tampona kırpılır), alımda başlayan süre sınırı ×1,3 olur; Namekuji etkilenmez.
    /// Namekuji'nin jel dondurması (ApplySlipOutcome → ApplyIce) bir ZombieEnemy ister: PlayMode kapsamında.
    /// </summary>
    [TestFixture]
    public sealed class Wp104B2ResonanceTests
    {
        [TearDown]
        public void TearDown() => TreeRuntime.ClearTestStats();

        private static void Use(float durationScale, int extraJumps)
        {
            TreeStats s = TreeStats.Neutral;
            s.RaijuDurationScale = durationScale;
            s.RaijuExtraJumps = extraJumps;
            TreeRuntime.SetTestStats(s);
        }

        [Test]
        public void RaijuZinciri_NotrdeVeriKadar_StormChainIleIkiFazla_TampondaKirpilir()
        {
            var arc = new WeaponArcChainBlock { maxTargets = 8 };
            TreeRuntime.SetTestStats(TreeStats.Neutral);
            Assert.AreEqual(8, SpecialImpacts.ChainTargetsOf(arc), "neutral tree: the data's maxTargets");
            Use(1.3f, 2);
            Assert.AreEqual(10, SpecialImpacts.ChainTargetsOf(arc), "Storm Chain: +2 jumps");
            arc.maxTargets = ChainTargetResolver.BufferSize;
            Assert.AreEqual(ChainTargetResolver.BufferSize, SpecialImpacts.ChainTargetsOf(arc), "never past the resolver's buffer");
        }

        [Test]
        public void RaijuSureSiniri_AlimdaBirKez_StormChainIle13Kat_NamekujiEtkilenmez()
        {
            TreeRuntime.SetTestStats(TreeStats.Neutral);
            var neutral = new SpecialWeaponRuntime();
            neutral.Begin(null, SpecialWeaponId.Raiju);
            float baseCap = neutral.Duration;
            var gelNeutral = new SpecialWeaponRuntime();
            gelNeutral.Begin(null, SpecialWeaponId.Namekuji);

            Use(1.3f, 2);
            var storm = new SpecialWeaponRuntime();
            storm.Begin(null, SpecialWeaponId.Raiju);
            Assert.AreEqual(baseCap * 1.3f, storm.Duration, 1e-3f, "Storm Chain: Raiju's time cap × 1.3");
            Assert.AreEqual(storm.Duration, storm.Remaining, 1e-4f, "the clock starts at the scaled cap");
            var gel = new SpecialWeaponRuntime();
            gel.Begin(null, SpecialWeaponId.Namekuji);
            Assert.AreEqual(gelNeutral.Duration, gel.Duration, 1e-4f, "Namekuji's cap is not Raiju's resonance");

            TreeRuntime.SetTestStats(TreeStats.Neutral);
            Assert.AreEqual(baseCap * 1.3f, storm.Duration, 1e-3f, "read once per pickup: a later tree change leaves the running cap");
        }
    }
}
