using System;
using System.Reflection;
using DD.Prototype.Enemies;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 R1-B (code review, C-00 30.09): AI4 — the knockback covers exactly its distance at any frame rate (eased delta);
    /// (i) — EnemyCombatEvents.Raised is cleared at SubsystemRegistration (a stale subscriber of the last Play session does not survive).
    /// </summary>
    [Category("WP076")]
    public sealed class R1BAiFixTests
    {
        [TestCase(20f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void Itme_KareHizindanBagimsiz_ToplamMesafeTam(float fps)
        {
            var p = new KnockbackProfile(0.6f, 0.25f);
            float dt = 1f / fps, t = 0f, eased = 0f, euler = 0f;
            while (t < p.Seconds)
            {
                eased += p.DistanceAt(t + dt) - p.DistanceAt(t);   // the fixed integration (ZombieHitResponse.TickHitResponse)
                euler += p.SpeedAt(t) * dt;                         // the old explicit Euler, for the record
                t += dt;
            }
            Assert.AreEqual(0.6f, eased, 1e-4f, "kolaylaştırılmış delta toplamı = mesafe (" + fps + " fps)");
            if (fps <= 20f) Assert.Greater(euler, 0.65f, "eski Euler 20 fps'te taşıyordu (inceleme: 0,72 m)");
        }

        [Test]
        public void SavasOlaylari_SubsystemRegistration_AboneleriTemizler()
        {
            int calls = 0;
            Action<EnemyCombatEventInfo> handler = _ => calls++;
            EnemyCombatEvents.Raised += handler;
            try
            {
                MethodInfo reset = typeof(EnemyCombatEvents).GetMethod("ResetRaisedDomain", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(reset, "SubsystemRegistration sıfırlaması yok");
                reset.Invoke(null, null);
                FieldInfo field = typeof(EnemyCombatEvents).GetField("Raised", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(field);
                Assert.IsNull(field.GetValue(null), "Raised sıfırlanmadı");
            }
            finally { EnemyCombatEvents.Raised -= handler; }
        }
    }
}
