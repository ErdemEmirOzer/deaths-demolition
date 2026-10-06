using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-087 (C-00 06.10, council K/wp-087/konsey/KARAR-ArmsCarryKick-06.10.md) · the weapon-first hold's arms channel
    /// (HeldKickChannel): at full height on the shot's frame; one way (never forward / muzzle-down — the rig body's spring overshoot
    /// was the support hand's miss); sustained fire stays under the caps (4 cm / 5°, build-up 1.25 × the scaled shot) with a pulse per
    /// shot; a single shot settles in 2 × its recovery (5 % left); the same at 30 / 60 / 120 fps (≤ 2 %).
    /// </summary>
    [TestFixture]
    [Category("WP087")]
    public sealed class Wp087TutusTepmeKanalTests
    {
        private const float Acc = 1.25f, MaxM = 0.04f, MaxDeg = 5f, Settle = 2f;

        // the feel rows' range: AR 0.10 m / 0.10 s, light 0.05 / 0.07, heavy 0.07 / 0.125; 90 °/m × upShare / 0.15
        private static readonly float[][] Rows =
        {
            new[] { 0.10f, 0.10f, 90f }, new[] { 0.05f, 0.07f, 60f }, new[] { 0.07f, 0.125f, 90f }, new[] { 0.10f, 0.125f, 120f },
            new[] { 0.02f, 0.10f, 90f }
        };

        private static readonly float[] Rpm = { 120f, 420f, 600f, 900f, 1200f };

        private static string F(float v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

        // ten shots at rpm, sampled at fps for 2.5 s; min / max back and degrees, the back right after each shot
        private static void Burst(float kick, float rec, float up, float rpm, float fps, out float minBack, out float maxBack,
            out float minDeg, out float maxDeg, List<float> trace = null, float sampleEvery = 0f)
        {
            var c = new HeldKickChannel();
            float dt = 1f / fps, interval = 60f / rpm, t = 0f, next = 0f;
            int shots = 0;
            minBack = minDeg = float.MaxValue;
            maxBack = maxDeg = float.MinValue;
            float nextSample = 0f;
            for (int f = 0; t < 2.5f; f++)
            {
                if (shots < 10 && t >= next - 1e-5f)
                {
                    c.Kick(kick, rec, up, Acc, MaxM, MaxDeg, Settle);
                    shots++;
                    next += interval;
                    Assert.Greater(c.Back, 0f, "atış karesinde kol kanalı > 0");
                }

                minBack = Mathf.Min(minBack, c.Back); maxBack = Mathf.Max(maxBack, c.Back);
                minDeg = Mathf.Min(minDeg, c.Degrees); maxDeg = Mathf.Max(maxDeg, c.Degrees);
                if (trace != null && t >= nextSample - 1e-5f) { trace.Add(c.Back); nextSample += sampleEvery; }
                c.Step(dt);
                t += dt;
            }
        }

        [Test]
        public void TekYon_Tavanli_AtisKaresinde_HerSatirHerHizda()
        {
            var rows = new List<string>();
            foreach (float[] r in Rows)
            {
                foreach (float rpm in Rpm)
                {
                    Burst(r[0], r[1], r[2], rpm, 60f, out float minB, out float maxB, out float minD, out float maxD);
                    rows.Add(r[0] + "/" + r[1] + "/" + r[2] + " @" + rpm + " back " + F(minB) + ".." + F(maxB) + " deg " + F(minD) + ".." + F(maxD));
                    Assert.GreaterOrEqual(minB, 0f, "geri hiç negatif değil (ileri aşım yok) " + rows[rows.Count - 1]);
                    Assert.GreaterOrEqual(minD, 0f, "namlu hiç aşağı değil " + rows[rows.Count - 1]);
                    Assert.LessOrEqual(maxB, MaxM + 1e-6f, "geri ≤ 4 cm " + rows[rows.Count - 1]);
                    Assert.LessOrEqual(maxD, MaxDeg + 1e-4f, "yukarı ≤ 5° " + rows[rows.Count - 1]);
                }
            }

            Debug.Log("[DD-TEST] wp087.channel\n" + string.Join("\n", rows));
        }

        [Test]
        public void SurekliAtis_HerAtistaNabizVar_PlatoYok()
        {
            // AR at 600 rpm: after the build-up each shot still lifts the channel from a lower value (the pulse reads)
            var c = new HeldKickChannel();
            float dt = 1f / 60f, before = 0f;
            int pulses = 0;
            for (int shot = 0; shot < 10; shot++)
            {
                before = c.Back;
                c.Kick(0.10f, 0.10f, 90f, Acc, MaxM, MaxDeg, Settle);
                if (shot >= 2 && c.Back - before > 0.2f * c.Back) pulses++;
                for (int f = 0; f < 6; f++) c.Step(dt);   // 600 rpm = 6 frames
            }

            Assert.GreaterOrEqual(pulses, 7, "sürekli atışta her atış görünür nabız (≥ %20 sıçrama)");
        }

        [Test]
        public void TekAtis_IkiToparlanmadaOturur()
        {
            const float kick = 0.07f, rec = 0.125f;
            var c = new HeldKickChannel();
            c.Kick(kick, rec, 90f, Acc, MaxM, MaxDeg, Settle);
            float x0 = c.Back;
            Assert.Greater(x0, 0f);
            float settle = Settle * rec, dt = 1f / 240f, t = 0f, atHalf = -1f;
            while (t < settle - 1e-6f)
            {
                c.Step(dt);
                t += dt;
                if (atHalf < 0f && t >= 0.5f * settle) atHalf = c.Back;
            }

            Assert.Greater(atHalf, 0.1f * x0, "yarı sürede hâlâ görünür");
            Assert.LessOrEqual(c.Back, 0.06f * x0, "2 × toparlanmada ≤ %5 (+%1)");
            for (int i = 0; i < 240; i++) c.Step(dt);
            Assert.IsTrue(c.AtRest, "dinlenir ve durur");
        }

        [Test]
        public void KareHizindanBagimsiz_30_60_120_Fps()
        {
            // shots every 0.1 s and samples every 1/30 s — both on frame boundaries at 30 / 60 / 120 fps
            var traces = new Dictionary<float, List<float>>();
            foreach (float fps in new[] { 30f, 60f, 120f })
            {
                var trace = new List<float>();
                Burst(0.10f, 0.10f, 90f, 600f, fps, out _, out _, out _, out _, trace, 1f / 30f);
                traces[fps] = trace;
            }

            int n = Mathf.Min(traces[30f].Count, Mathf.Min(traces[60f].Count, traces[120f].Count));
            Assert.Greater(n, 20);
            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, traces[60f][i]);
            for (int i = 0; i < n; i++)
            {
                Assert.LessOrEqual(Mathf.Abs(traces[30f][i] - traces[60f][i]), 0.02f * peak, "30 vs 60 fps @" + (i / 30f));
                Assert.LessOrEqual(Mathf.Abs(traces[120f][i] - traces[60f][i]), 0.02f * peak, "120 vs 60 fps @" + (i / 30f));
            }
        }

        /// <summary>
        /// The packaged DD_FeelConfig asset does not spell the four new recoil fields (no Resources change): Unity gives them the
        /// code defaults, and the profile still validates — an invalid packaged profile would silently fall back to CreateDefaults
        /// for EVERY feel number (FeelRuntime).
        /// </summary>
        [Test]
        public void PaketliHisProfili_YeniAlanlarKodVarsayilani_DogrulamadanGecer()
        {
            var config = Resources.Load<FeelConfig>(FeelConfig.ResourcesPath);
            Assert.IsNotNull(config, "DD_FeelConfig yok");
            Assert.IsNotNull(config.AuthoredData);
            RecoilFeelData r = config.AuthoredData.recoil, d = new RecoilFeelData();
            Assert.AreEqual(d.heldKickAccumulation, r.heldKickAccumulation, "heldKickAccumulation");
            Assert.AreEqual(d.heldKickMaxMeters, r.heldKickMaxMeters, "heldKickMaxMeters");
            Assert.AreEqual(d.heldKickMaxDegrees, r.heldKickMaxDegrees, "heldKickMaxDegrees");
            Assert.AreEqual(d.heldKickSettleRecoveries, r.heldKickSettleRecoveries, "heldKickSettleRecoveries");
            Assert.IsTrue(FeelConfigValidator.Validate(config.AuthoredData, out string error), error);
            Assert.IsTrue(FeelConfigValidator.Validate(FeelConfig.CreateDefaults(), out error), error);
        }

        [Test]
        public void SifirAtis_VeSifirlama_Dinlenmede()
        {
            var c = new HeldKickChannel();
            c.Kick(0f, 0.1f, 90f, Acc, MaxM, MaxDeg, Settle);
            Assert.IsTrue(c.AtRest, "0 tepme kanalı oynatmaz");
            c.Kick(0.1f, 0.1f, 90f, Acc, MaxM, MaxDeg, Settle);
            Assert.IsFalse(c.AtRest);
            c.Reset();
            Assert.IsTrue(c.AtRest);
            Assert.AreEqual(0f, c.Back);
            Assert.AreEqual(0f, c.Degrees);
        }
    }
}
