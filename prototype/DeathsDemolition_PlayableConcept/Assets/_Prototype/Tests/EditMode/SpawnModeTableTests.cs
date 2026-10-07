using DD.Prototype.Core.Config;
using DD.Prototype.Navigation;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B6 · §5.1 through the real SpawnWeightCalculator: TopDown keeps the WP-030/054 rules; TP / FP raise the
    /// minimum spawn distance (14 / 16 m) and the behind-camera reject (16 / 18 m) and lower the on-screen factor
    /// (0.30 / 0.25); the tier path gate rejects points beyond its path limit (Elite / Ultra 55 m, Special 75 m).
    /// WP-089 R3 (user 06.10: only TopDown): the TopDown rows stand alone; the TP / FP rows carry [Category("FPTP")].
    /// </summary>
    [Category("WP076")]
    public sealed class SpawnModeTableTests
    {
        private SpawnConfigSection _config;
        private SpawnWeightTuning _tuning;

        [SetUp] public void SetUp() { _config = new SpawnConfigSection(); _tuning = new SpawnWeightTuning(); _tuning.Sanitize(); CrowdFeelSettings.ResetStatics(); }
        [TearDown] public void TearDown() { CrowdFeelSettings.ResetStatics(); }

        private static SpawnPointRuntime Nokta(float yol, bool ekranda = false, bool arkada = false)
        {
            var tanim = new SpawnPointDefinition { Id = "T", Region = RingRegion.North, Position = new Vector3(0f, 0f, yol), BaseWeight = 1f, Enabled = true, FaceMapCenter = true };
            return new SpawnPointRuntime(0, tanim) { PathDistance = yol, StraightDistance = yol, PathStatus = NavPathStatus.Complete, LastUsedTime = -999f, IsOnScreen = ekranda, IsBehindCamera = arkada };
        }

        private static SpawnWeightInputs Girdi(int mod, float yolKapisi = 0f) => new SpawnWeightInputs
        {
            PlayerPosition = Vector3.zero, PlayerRegion = RingRegionMap.Classify(Vector3.zero, Vector3.zero, 14f), Now = 100f,
            ThreatLayer = 0, CameraMode = mod, MaxPathDistance = yolKapisi
        };

        private float W(SpawnPointRuntime p, in SpawnWeightInputs g, out SpawnWeightBreakdown b) =>
            SpawnWeightCalculator.Evaluate(p, in g, _config, _tuning, SpawnRelaxation.None, out b);

        [Test]
        public void Tepeden_MinMesafe_ArkaRet_Degismez()
        {
            Assert.Greater(W(Nokta(13f), Girdi(0), out _), 0f, "tepeden 12 m tabanı (değişmez)");
            Assert.Greater(W(Nokta(17f, arkada: true), Girdi(0), out _), 0f, "tepeden 15 m (değişmez)");
        }

        [Test]
        [Category("FPTP")]
        public void TP_FP_MinMesafe_ArkaRet()
        {
            Assert.AreEqual(0f, W(Nokta(13f), Girdi(1), out SpawnWeightBreakdown b), "TP 14 m"); Assert.AreEqual("too close", b.Reason);
            Assert.Greater(W(Nokta(15f), Girdi(1), out _), 0f);
            Assert.AreEqual(0f, W(Nokta(15f), Girdi(2), out _), "FP 16 m");

            Assert.Greater(W(Nokta(17f, arkada: true), Girdi(1), out _), 0f, "TP arka ret 16 m");
            Assert.AreEqual(0f, W(Nokta(17f, arkada: true), Girdi(2), out b), "FP arka ret 18 m"); Assert.AreEqual("behind camera", b.Reason);
        }

        /// <summary>TopDown: the on-screen factor is the threat layer's row (WP-054) — 0.35 at layer 0, the layer-3 row (0.10) at
        /// layer 3; no camera-mode floor applies (SpawnWeightCalculator §9: the mode row only when mode != 0).</summary>
        [Test]
        public void EkrandaCarpani_Tepeden_TehditKatmaniSatiri()
        {
            W(Nokta(30f, ekranda: true), Girdi(0), out SpawnWeightBreakdown t); Assert.AreEqual(0.35f, t.Visibility, 1e-5f);
            var g = Girdi(0); g.ThreatLayer = 3;
            W(Nokta(30f, ekranda: true), in g, out SpawnWeightBreakdown k3); Assert.AreEqual(0.10f, k3.Visibility, 1e-5f, "tepeden tehdit katmanı 3 satırı (0,10)");
        }

        [Test]
        [Category("FPTP")]
        public void EkrandaCarpani_TP_FP_TehditKatmaniDahaDusukseOKazanir()
        {
            W(Nokta(30f, ekranda: true), Girdi(1), out SpawnWeightBreakdown tp); Assert.AreEqual(0.30f, tp.Visibility, 1e-5f);
            W(Nokta(30f, ekranda: true), Girdi(2), out SpawnWeightBreakdown fp); Assert.AreEqual(0.25f, fp.Visibility, 1e-5f);
            var g = Girdi(2); g.ThreatLayer = 3;
            W(Nokta(30f, ekranda: true), in g, out SpawnWeightBreakdown k3); Assert.AreEqual(0.10f, k3.Visibility, 1e-5f, "tehdit katmanı 3 (0,10) daha düşük → o");
        }

        [Test]
        public void TierYolKapisi()
        {
            Assert.AreEqual(55f, SpawnModeTable.TierPathMax(EnemyTier.Elite)); Assert.AreEqual(55f, SpawnModeTable.TierPathMax(EnemyTier.Ultra));
            Assert.AreEqual(75f, SpawnModeTable.TierPathMax(EnemyTier.Special)); Assert.AreEqual(0f, SpawnModeTable.TierPathMax(EnemyTier.Standard));
            Assert.AreEqual(0f, W(Nokta(58f), Girdi(0, 55f), out SpawnWeightBreakdown b)); Assert.AreEqual("tier path gate", b.Reason);
            Assert.Greater(W(Nokta(50f), Girdi(0, 55f), out _), 0f);
            Assert.Greater(W(Nokta(58f), Girdi(0), out _), 0f, "kapı yok → uzak nokta geçerli");
        }
    }
}
