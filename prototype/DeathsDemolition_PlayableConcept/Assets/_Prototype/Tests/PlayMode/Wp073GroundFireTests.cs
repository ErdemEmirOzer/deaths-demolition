using System.Collections;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-073 · burning ground for S-04's K10b zones (RunTreeGroundFire): the pool's Ignited / Extinguished reach
    /// GroundFireSystem; a zone burns on the ground under the blast (flames, embers, glow) inside the tier caps, ramps in and
    /// dies out; Ash Cloud + Hibana at one point share one visual zone; and 0 B per event + frame on the calling thread
    /// (WP-059D pattern with a positive control). No run scene: the system is driven directly.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073GroundFireTests
    {
        private static readonly Vector3 P = new Vector3(40f, 0f, 40f);
        private GameObject _ground;

        [SetUp]
        public void SetUp()
        {
            VfxTier.Override = VfxTierLevel.Cinematic;
            MuzzleSystem.Ensure();
            GroundFireSystem.Ensure().Clear();
            RunTreeGroundFire.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            VfxTier.Override = null;
            RunTreeGroundFire.Clear();
            if (GroundFireSystem.Instance != null) GroundFireSystem.Instance.Clear();
            if (_ground != null) Object.Destroy(_ground);
        }

        private void Ground()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);   // Default layer: in the system's ground mask
            _ground.transform.position = P;
            _ground.transform.localScale = Vector3.one * 3f;
            Physics.SyncTransforms();
        }

        private static GroundFireInfo Zone(int id, Vector3 at, float radius = 3f, float seconds = 2f)
        {
            return new GroundFireInfo(id, at, radius, 10f, seconds, seconds);
        }

        [UnityTest]
        public IEnumerator Havuz_Olaylari_ZemindeYanar_KademeTavani()
        {
            Ground();
            GroundFireSystem fire = GroundFireSystem.Instance;
            int started = fire.ZonesStarted;
            // The real pool: its Ignited reaches the system (a blast 1 m up burns on the ground under it).
            int id = RunTreeGroundFire.Ignite(P + Vector3.up * 1f, 3f, 10f, 2f);
            Assert.Greater(id, 0);
            Assert.AreEqual(started + 1, fire.ZonesStarted, "Ignited → GroundFireSystem");

            float t1 = Time.time;
            while (Time.time - t1 < 0.6f) yield return null;   // game time: batch-mode frames are a few ms each
            Assert.AreEqual(1, fire.LiveZones);
            Assert.Greater(fire.FlamesLive, 10, "alevler");
            Assert.Greater(fire.EmbersLive, 0, "köz");
            Assert.Greater(fire.GlowLive, 0, "zemin parıltısı");
            Assert.LessOrEqual(fire.FlamesLive, GroundFireSystem.FlameCap);
            ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[GroundFireSystem.FlameCap];
            int n = fire.SystemOf(0).GetParticles(buffer);
            float lowest = float.MaxValue;
            for (int i = 0; i < n; i++) lowest = Mathf.Min(lowest, buffer[i].position.y);
            Assert.Less(lowest, P.y + 0.6f, "alev zeminde (patlama 1 m yukarıda olsa da)");

            // Performance: the caps drop.
            VfxTier.Override = VfxTierLevel.Performance;
            yield return null;
            Assert.AreEqual(GroundFireSystem.FlameCap, fire.SystemOf(0).main.maxParticles);
            Assert.AreEqual(90, GroundFireSystem.FlameCap);

            // Extinguished → out within OutSeconds, then the slot frees.
            RunTreeGroundFire.Clear();
            float t0 = Time.time;
            while (fire.LiveZones > 0 && Time.time - t0 < 2f) yield return null;
            Assert.AreEqual(0, fire.LiveZones, "söndü");
        }

        [Test]
        public void AshCloud_Hibana_AyniNokta_TekGorselBolge()
        {
            GroundFireSystem fire = GroundFireSystem.Instance;
            int merged = fire.ZonesMerged;
            fire.OnIgnited(Zone(9001, P, 3f, 2f));
            fire.OnIgnited(Zone(9002, P + Vector3.right * 0.3f, 3.5f, 3f));
            fire.Step(Time.time + 0.1f, 0.016f);
            Assert.AreEqual(merged + 1, fire.ZonesMerged);
            Assert.AreEqual(1, fire.LiveZones, "iki bölge tek görsel");
            fire.OnExtinguished(9001);
            fire.Step(Time.time + 0.2f, 0.016f);
            Assert.AreEqual(1, fire.LiveZones, "biri sönünce diğeri yanar");
            fire.OnExtinguished(9002);
            fire.Step(Time.time + 0.2f + GroundFireSystem.OutSeconds + 0.05f, 0.016f);
            Assert.AreEqual(0, fire.LiveZones);
        }

        [Test]
        public void Yogunluk_Rampa_Sonus_Kararma()
        {
            // 0.25 s ignition ramp, full while burning, a quarter by the end, then out.
            Assert.AreEqual(0f, GroundFireSystem.Intensity(0f, 2f, false, 0f, 0f), 1e-4f);
            Assert.AreEqual(1f, GroundFireSystem.Intensity(0f, 2f, false, 0f, 1f), 1e-4f);
            Assert.AreEqual(0.25f, GroundFireSystem.Intensity(0f, 2f, false, 0f, 2f), 1e-3f);
            Assert.Less(GroundFireSystem.Intensity(0f, 2f, true, 2f, 2.3f), 0.25f);
            Assert.AreEqual(0f, GroundFireSystem.Intensity(0f, 2f, true, 2f, 2f + GroundFireSystem.OutSeconds), 1e-4f);
        }

        [UnityTest]
        public IEnumerator ZeminAtesi_GcSifir_IsParcacigiKaydedici()
        {
            Ground();
            GroundFireSystem fire = GroundFireSystem.Instance;
            int next = 95000;

            void Rep()
            {
                float now = Time.time;
                for (int i = 0; i < 4; i++) fire.OnIgnited(Zone(++next, P + Vector3.right * (i * 4f)));
                fire.Step(now, 0.016f);
                for (int i = 0; i < 4; i++) fire.OnExtinguished(next - i);
                fire.Step(now + 0.01f, 0.016f);
            }

            for (int rep = 0; rep < 3; rep++) Rep();
            yield return null;
            yield return null;

            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.That(() => Rep(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "zemin ateşi olay + kare 0 B");
            Assert.IsNotNull(bait);
        }
    }
}
