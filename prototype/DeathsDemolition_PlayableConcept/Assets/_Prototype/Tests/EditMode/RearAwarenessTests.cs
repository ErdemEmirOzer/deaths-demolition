using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B7 · §6 the pure rear model per camera mode (fallback angle, no rig): FP Aware ≤ 7 / Warn ≤ 4 / Alert ≤ 2.2,
    /// the 0.45 s FP hold, TP's pair rule, top-down never Aware, a mode change clears the level; §5.1 the rear spawn
    /// share window (8) and its penalty by mode. WP-089 R3 (user 06.10: only TopDown): the TopDown rows stand alone; the TP / FP
    /// rows carry [Category("FPTP")].
    /// </summary>
    [Category("WP076")]
    public sealed class RearAwarenessTests
    {
        private ThreatSettings _s;
        private readonly ThreatEnemyInput[] _e = new ThreatEnemyInput[4];

        [SetUp] public void SetUp() { _s = ScriptableObject.CreateInstance<ThreatSettings>(); RearSpawnShare.ResetStatics(); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(_s); RearSpawnShare.ResetStatics(); }

        private static ThreatContext Ctx(int mode) => new ThreatContext { PlayerPosition = Vector3.zero, ViewForward = Vector3.forward, Health01 = 1f, InFight = true, Mode = mode };

        private ThreatSnapshot Eval(int mode, ref ThreatState st, float dt, params float[] behind)
        {
            for (int i = 0; i < behind.Length; i++) _e[i] = new ThreatEnemyInput { Position = new Vector3(0f, 0f, -behind[i]), Tier = EnemyTier.Standard, LifeId = 10 + i };
            ThreatContext c = Ctx(mode);
            return ThreatModel.Evaluate(_s, in c, _e, behind.Length, ref st, dt);
        }

        [Test]
        [Category("FPTP")]
        public void FP_Aware_Warn_Alert_Tutma()
        {
            var st = new ThreatState();
            Assert.AreEqual(ThreatRearLevel.None, Eval(2, ref st, 0.1f, 8f).RearLevel, "7 m ötesi");
            st = new ThreatState();
            ThreatSnapshot a = Eval(2, ref st, 0.1f, 6.5f);
            Assert.AreEqual(ThreatRearLevel.Aware, a.RearLevel); Assert.AreEqual(6.5f, a.RearNearestDistance, 1e-4f); Assert.AreEqual(10, a.RearNearestLifeId);
            Assert.AreEqual(ThreatRearLevel.Warn, Eval(2, ref st, 0.1f, 3.8f).RearLevel, "FP ≤ 4 m");
            Assert.AreEqual(ThreatRearLevel.Alert, Eval(2, ref st, 0.1f, 2.0f).RearLevel, "FP ≤ 2,2 m (ısırma bandı)");
            Assert.AreEqual(ThreatRearLevel.Alert, Eval(2, ref st, 0.3f).RearLevel, "0,45 s tutma");
            Assert.AreEqual(ThreatRearLevel.None, Eval(2, ref st, 0.2f).RearLevel, "tutma bitti → temiz");
            Assert.AreEqual(-1f, Eval(2, ref st, 0.1f).RearNearestDistance);
        }

        [Test]
        public void Tepeden_ArkaSeviyeAsla_OnUyariYok()
        {
            var st = new ThreatState();
            Assert.AreEqual(ThreatRearLevel.None, Eval(0, ref st, 0.1f, 3f).RearLevel, "tepeden: Aware yok");
            Assert.AreEqual(0f, _s.RearWarnLeadSeconds[0], "tepeden ön uyarı süresi 0");
        }

        [Test]
        [Category("FPTP")]
        public void TP_Cift_ModDegisimiSifirlar()
        {
            var st = new ThreatState();
            Assert.AreEqual(ThreatRearLevel.Warn, Eval(1, ref st, 0.1f, 4.5f, 4.6f).RearLevel, "TP: ≥ 2 beden ≤ 5 m");
            st = new ThreatState();
            Assert.AreEqual(ThreatRearLevel.Aware, Eval(1, ref st, 0.1f, 4.5f).RearLevel, "TP tek beden ≤ 6 m");
            Assert.AreEqual(ThreatRearLevel.None, Eval(0, ref st, 0.1f, 3f).RearLevel, "mod değişimi sıfırlar");
            CollectionAssert.AreEqual(new[] { 0f, 0.5f, 0.8f }, _s.RearWarnLeadSeconds, "ön uyarı süresi");
        }

        [Test]
        public void ArkaDogusPayi_Pencere8_TepedenTavanYok()
        {
            for (int i = 0; i < 8; i++) RearSpawnShare.Record(i < 3);
            Assert.AreEqual(3f / 8f, RearSpawnShare.Share(8), 1e-6f);
            Assert.AreEqual(0f, RearSpawnShare.Penalty(0, true), "tepeden tavan yok");
            for (int i = 0; i < 8; i++) RearSpawnShare.Record(false);
            Assert.AreEqual(0f, RearSpawnShare.Share(8), "kayan pencere");
        }

        [Test]
        [Category("FPTP")]
        public void ArkaDogusPayi_TP_FP_Ceza()
        {
            for (int i = 0; i < 8; i++) RearSpawnShare.Record(i < 3);
            Assert.AreEqual(1f, RearSpawnShare.Penalty(2, true), 1e-6f, "FP %37,5 ≥ %35 → arka ×0");
            Assert.AreEqual(0.75f, RearSpawnShare.Penalty(2, false), 1e-6f, "başka yol-hazır nokta yok → ×0,25");
            Assert.AreEqual(0.3f, RearSpawnShare.Penalty(1, true), 1e-6f, "TP %45 altında → ×0,7");
        }
    }
}
