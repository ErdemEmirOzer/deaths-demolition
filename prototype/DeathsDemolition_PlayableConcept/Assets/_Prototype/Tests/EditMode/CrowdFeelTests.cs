using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Navigation;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-054 · Crowd / AI feel v1, the pure parts: (1) the shipped knob defaults are the game-designer turn's
    /// values, (2) the flank goal geometry (side by sign, grows with distance, straightens inside the radius,
    /// offset capped), (3) the corpse linger (tier, jitter, ceiling rule, Ultra floor), (4) the gate mass
    /// factor per tier, (5) the threat-aware on-screen factor through the real spawn weight calculator,
    /// (6) the bridge cost 1.35 in the region profile defaults.
    /// </summary>
    [Category("WP054")]
    public sealed class CrowdFeelTests
    {
        private CrowdFeelSettings _feel;

        [SetUp] public void SetUp() { _feel = ScriptableObject.CreateInstance<CrowdFeelSettings>(); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(_feel); CrowdFeelSettings.ResetStatics(); ThreatLevelFeed.ResetStatics(); RegionNavProfile.ResetStatics(); }

        [Test]
        public void Varsayilanlar_TasarimTuru()
        {
            CollectionAssert.AreEqual(new[] { 0.35f, 0.60f, 0.10f, 0f }, _feel.FlankShare);
            Assert.AreEqual(35f, _feel.FlankAngleMin); Assert.AreEqual(70f, _feel.FlankAngleMax); Assert.AreEqual(3f, _feel.FlankStraightRadius);
            Assert.AreEqual(0.80f, _feel.WallFriction, 1e-6f); Assert.AreEqual(0.10f, _feel.WallEnterSeconds, 1e-6f); Assert.AreEqual(0.35f, _feel.WallExitSeconds, 1e-6f);
            Assert.AreEqual(1.5f, _feel.GateMassHeavy, 1e-6f); Assert.AreEqual(0.6f, _feel.GateMassLight, 1e-6f);
            Assert.AreEqual(0.45f, _feel.StallSecondsMin, 1e-6f); Assert.AreEqual(0.85f, _feel.StallSecondsMax, 1e-6f);
            Assert.AreEqual(0.08f, _feel.ShuffleSpeed, 1e-6f); Assert.AreEqual(0.12f, _feel.ShuffleAmplitude, 1e-6f);
            CollectionAssert.AreEqual(new[] { 4.0f, 4.0f, 5.0f, 6.0f }, _feel.CorpseLinger);   // WP-076 v2 (DEC-096)
            CollectionAssert.AreEqual(new[] { 0.35f, 0.35f, 0.20f, 0.10f }, _feel.OnScreenFactorByThreatLayer);
            Assert.AreEqual(0.35f, _feel.FlankShareOf(EnemyTier.Standard), 1e-6f); Assert.AreEqual(0f, _feel.FlankShareOf(EnemyTier.Ultra), 1e-6f);
            Assert.IsTrue(_feel.IsHeavy(EnemyTier.Elite) && _feel.IsHeavy(EnemyTier.Ultra) && !_feel.IsHeavy(EnemyTier.Special));
        }

        [Test]
        public void KanatHedefi_IsaretYonu_MesafeyleBuyur_YakindaDuzelir_Tavan()
        {
            Vector3 target = Vector3.zero;
            Assert.AreEqual(target, _feel.FlankGoal(new Vector3(0f, 0f, -10f), target, 0, 50f), "işaret 0 → hedefin kendisi");
            Assert.AreEqual(target, _feel.FlankGoal(new Vector3(0f, 0f, -2f), target, 1, 50f), "düz yarıçap içinde hedef");
            Vector3 far = _feel.FlankGoal(new Vector3(0f, 0f, -12f), target, 1, 50f);
            Vector3 farL = _feel.FlankGoal(new Vector3(0f, 0f, -12f), target, -1, 50f);
            Assert.Greater(Mathf.Abs(far.x), 1f, "uzakta yanal kayma"); Assert.AreEqual(-far.x, farL.x, 1e-5f, "işaret tarafı belirler");
            Assert.LessOrEqual(Mathf.Abs(far.x), _feel.FlankOffsetMax + 1e-4f, "tavan");
            Vector3 mid = _feel.FlankGoal(new Vector3(0f, 0f, -4f), target, 1, 50f);
            Assert.Greater(Mathf.Abs(mid.x), 0f); Assert.Less(Mathf.Abs(mid.x), Mathf.Abs(far.x), "yaklaşırken kayma azalır (blend)");
            Assert.AreEqual(0f, far.y, 1e-6f);
        }

        [Test]
        public void CesetSuresi_Tier_Jitter_TavanKurali_UltraTabani()
        {
            Assert.AreEqual(4.0f, _feel.CorpseLingerFor(EnemyTier.Standard, 0.5f, false), 1e-5f, "jitter 0,5 = taban (Yüksek)");
            Assert.AreEqual(6.0f, _feel.CorpseLingerFor(EnemyTier.Ultra, 0.5f, false), 1e-5f);
            Assert.AreEqual(5.0f * 1.15f, _feel.CorpseLingerFor(EnemyTier.Elite, 1f, false), 1e-5f, "jitter +%15");
            Assert.AreEqual(4.0f * 0.85f, _feel.CorpseLingerFor(EnemyTier.Special, 0f, false), 1e-5f, "jitter −%15");
            Assert.AreEqual(4.0f * 0.4f, _feel.CorpseLingerFor(EnemyTier.Standard, 0.5f, true), 1e-5f, "tavana yakın ×0,4");
            Assert.AreEqual(4.0f * 0.6f, _feel.CorpseLingerFor(EnemyTier.Standard, 0.5f, false, QualityTier.Performans), 1e-5f, "Performans ×0,6");
            Assert.AreEqual(4.0f * 1.25f, _feel.CorpseLingerFor(EnemyTier.Standard, 0.5f, false, QualityTier.Sinematik), 1e-5f, "Sinematik ×1,25");
            Assert.AreEqual(6, _feel.CorpseCeilingFor(QualityTier.Performans)); Assert.AreEqual(10, _feel.CorpseCeilingFor(QualityTier.Yuksek)); Assert.AreEqual(16, _feel.CorpseCeilingFor(QualityTier.Sinematik));
            _feel.CorpseLinger[3] = 2.0f;
            Assert.AreEqual(1.2f, _feel.CorpseLingerFor(EnemyTier.Ultra, 0.5f, true), 1e-5f, "Ultra tabanı 1,2 (2,0 × 0,4 = 0,8 → 1,2)");
            Assert.AreEqual(1.5f, _feel.GateMassFor(EnemyTier.Ultra), 1e-6f); Assert.AreEqual(0.6f, _feel.GateMassFor(EnemyTier.Standard), 1e-6f);
        }

        private static SpawnPointRuntime Nokta(bool onScreen)
        {
            var tanim = new SpawnPointDefinition { Id = onScreen ? "on" : "off", Region = RingRegion.North, Position = new Vector3(0f, 0f, 24f), BaseWeight = 1f, Enabled = true, FaceMapCenter = true };
            return new SpawnPointRuntime(0, tanim) { PathDistance = 24f, StraightDistance = 24f, PathStatus = NavPathStatus.Complete, LastUsedTime = -999f, IsOnScreen = onScreen };
        }

        [Test]
        public void TehditeGoreGorunurluk_KatmanYukseldikceKameraIciAgirligiDuser()
        {
            CrowdFeelSettings.SetActive(_feel);
            var config = new SpawnConfigSection(); var tuning = new SpawnWeightTuning();
            float Vis(int layer)
            {
                var inputs = new SpawnWeightInputs { PlayerPosition = Vector3.zero, PlayerRegion = RingRegion.Center, PlayerVelocity = Vector3.zero, Now = 100f, NavDegraded = false, ThreatLayer = layer };
                SpawnWeightCalculator.Evaluate(Nokta(true), in inputs, config, tuning, SpawnRelaxation.None, out SpawnWeightBreakdown b);
                return b.Visibility;
            }
            Assert.AreEqual(tuning.OnScreenFactor, Vis(-1), 1e-6f, "tehdit örneği yokken WP-030 çarpanı");
            Assert.AreEqual(0.35f, Vis(0), 1e-6f); Assert.AreEqual(0.35f, Vis(1), 1e-6f);
            Assert.AreEqual(0.20f, Vis(2), 1e-6f, "katman 2"); Assert.AreEqual(0.10f, Vis(3), 1e-6f, "katman 3");
            var inputsOff = new SpawnWeightInputs { PlayerPosition = Vector3.zero, PlayerRegion = RingRegion.Center, Now = 100f, ThreatLayer = 3 };
            SpawnWeightCalculator.Evaluate(Nokta(false), in inputsOff, config, tuning, SpawnRelaxation.None, out SpawnWeightBreakdown off);
            Assert.AreEqual(1f, off.Visibility, 1e-6f, "kamera dışı nokta her katmanda 1");
            Assert.AreEqual(-1, ThreatLevelFeed.Layer, "yayın yokken -1");
            ThreatLevelFeed.Publish(2); Assert.AreEqual(2, ThreatLevelFeed.Layer); ThreatLevelFeed.ResetStatics(); Assert.AreEqual(-1, ThreatLevelFeed.Layer);
        }

        [Test]
        public void KopruMaliyeti_v3_1_35()
        {
            RegionNavProfile p = RegionNavProfile.CreateDefault();
            try
            {
                NavRegionVolume bridge = p.Find(new Vector3(4f, 0f, -28f));
                Assert.IsNotNull(bridge); Assert.AreEqual("S_Bridge", bridge.id);
                Assert.AreEqual(1.35f, bridge.costMultiplier, 1e-6f, "WP-054: v3 NavMesh'te köprü ×1,35 (1,6 her gövdeyi kıyıya yolluyordu)");
                Assert.AreEqual(2, bridge.capacity);
                Assert.AreEqual(NavRegionKind.Gate, p.Find(new Vector3(-8f, 0f, 34f)).kind, "kuzey batı kapısı Gate hacmi (kütle çarpanı burada)");
            }
            finally { Object.DestroyImmediate(p); }
        }
    }
}
