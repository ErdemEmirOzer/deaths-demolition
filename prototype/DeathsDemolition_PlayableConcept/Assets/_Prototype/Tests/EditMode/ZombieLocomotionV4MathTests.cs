using System.Collections.Generic;
using System.Globalization;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-096 · zombie locomotion v4, the pure parts (<see cref="ZombieLocomotionMath"/>): P1 cadence speed and its critically
    /// damped smoothing (frame-rate independent), P2 move speed jitter (mean exactly 1), P3 honest rate inside the clip band
    /// (+ the shipped walk_stomp floor), P5 absolute gait switch, P6 acceleration table / ramp, P7 heading smoother and the
    /// facing-follow shares (a body that follows its facing still reaches every corner — no orbit).
    /// </summary>
    [Category("WP096")]
    public sealed class ZombieLocomotionV4MathTests
    {
        private const float Dt = 1f / 60f;

        // ------------------------------------------------------------------ P2

        [Test]
        public void HizJitter_OrtalamaTam1_AralikVeCesit()
        {
            foreach (int n in new[] { 2, 6, 10, 40, 1000 })
            {
                double sum = 0d;
                var distinct = new HashSet<int>();
                for (int g = 0; g < n; g++)
                {
                    float j = ZombieLocomotionMath.SpeedJitter(g, 1f);
                    Assert.That(j, Is.InRange(1f - ZombieLocomotionMath.SpeedSpread - 1e-6f, 1f + ZombieLocomotionMath.SpeedSpread + 1e-6f), "aralık g=" + g);
                    sum += j;
                    distinct.Add(Mathf.RoundToInt(j * 100000f));
                }

                Assert.AreEqual(1d, sum / n, 1e-6d, "ortalama tam 1 (çift sayıda ardışık doğuş) n=" + n);
                if (n >= 6 && n <= 40) Assert.GreaterOrEqual(distinct.Count, n * 9 / 10, "hız çeşitliliği n=" + n);
            }

            // an odd count: off by at most spread / n
            for (int n = 1; n < 60; n += 2)
            {
                double sum = 0d;
                for (int g = 0; g < n; g++) sum += ZombieLocomotionMath.SpeedJitter(g, 1f);
                Assert.LessOrEqual(System.Math.Abs(sum / n - 1d), ZombieLocomotionMath.SpeedSpread / n + 1e-6d, "tek sayı n=" + n);
            }

            Assert.AreEqual(0.04f, ZombieLocomotionMath.SpeedSpread, 1e-6f, "K1 varsayılanı ±%4");
            Assert.AreEqual(ZombieLocomotionMath.SpeedJitter(7, 1f), ZombieLocomotionMath.SpeedJitter(7, 1f), "deterministik");
        }

        [Test]
        public void HizJitter_VaryeteSifir_Kapali()
        {
            for (int g = 0; g < 50; g++) Assert.AreEqual(1f, ZombieLocomotionMath.SpeedJitter(g, 0f), "GaitVariety 0 = kapalı");
            Assert.AreEqual(0f, ZombieSkinnedVisual.GaitSpread, "WP-096 P2: kadans yayılımı emekli");
        }

        // ------------------------------------------------------------------ P3 / P5

        [Test]
        public void DurustOran_KlipBandi_YalnizSeyirdeTaban()
        {
            // Executioner: 1.7 m/s over walk_stomp 1.7 on a 1.48 body — honest 0.676
            float honest = 1.7f / (1.7f * 1.48f);
            Assert.AreEqual(honest, ZombieLocomotionMath.HonestRate(1.7f, 1.7f, 1.48f), 1e-5f);
            Assert.AreEqual(honest, ZombieLocomotionMath.GaitRate(1.7f, 1.7f, 1.48f, 0.6f, 1.2f, true), 1e-5f, "taban 0.6 → kırpılmaz");
            Assert.AreEqual(CadenceBand.Min, ZombieLocomotionMath.GaitRate(1.7f, 1.7f, 1.48f, CadenceBand.Min, CadenceBand.Max, true), 1e-5f, "eski bant 0.75 kırpardı (kontrol)");
            // Standard 2.7 over shamble_fast 2.4: honest 1.125, inside the band
            Assert.AreEqual(1.125f, ZombieLocomotionMath.GaitRate(2.7f, 2.4f, 1f, CadenceBand.Min, CadenceBand.Max, true), 1e-5f);
            // the cap holds (faster travel slides)
            Assert.AreEqual(1.2f, ZombieLocomotionMath.GaitRate(4f, 2.4f, 1f, CadenceBand.Min, 1.2f, true), 1e-5f);
            // P5 slow region (front-line creep ×0.15 = 0.405 m/s): honest, NOT floored — the feet never outrun the body
            float creep = ZombieLocomotionMath.GaitRate(0.405f, 2.4f, 1f, CadenceBand.Min, CadenceBand.Max, false);
            Assert.AreEqual(0.405f / 2.4f, creep, 1e-5f, "yavaş bölgede dürüst oran");
            Assert.AreEqual(0f, CadenceBand.SlideFraction(0.405f, 2.4f, creep), 1e-4f, "yavaş bölgede kayma 0");
            Assert.AreEqual(1f, ZombieLocomotionMath.HonestRate(1f, 0f, 1f), "bilinmeyen klip hızı → 1");
        }

        [Test]
        public void SevkSeti_Stomp_TabanAltiDurustOranKirpilmaz()
        {
            var zombie = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet);
            Assert.IsNotNull(zombie, "zombi animasyon seti yok");
            Assert.IsNotNull(zombie.Find(C.ZombieWalkStompV3), "walk_stomp sette yok");
            Assert.AreEqual(0.6f, zombie.MinRateOf(C.ZombieWalkStompV3), 1e-5f, "walk_stomp tabanı 0.6 (WP-096 P3)");
            Assert.AreEqual(CadenceBand.Max, zombie.MaxRateOf(C.ZombieWalkStompV3), 1e-5f, "walk_stomp tavanı varsayılan");
            Assert.AreEqual(CadenceBand.Min, zombie.MinRateOf(C.ZombieShambleFastAV3), 1e-5f, "diğer klipler varsayılan bant");

            // worst corner of the Executioner: slowest jitter, largest body (scale 1.48 × 1.06)
            float ultra = new EnemyConfigSection().Find(EnemyTier.Ultra).MoveSpeed;
            float clip = zombie.SpeedOf(C.ZombieWalkStompV3);
            float slowest = ultra * (1f - ZombieLocomotionMath.SpeedSpread) / (clip * 1.48f * 1.06f);
            float fastest = ultra * (1f + ZombieLocomotionMath.SpeedSpread) / (clip * 1.48f * 0.94f);
            Debug.Log("[DD-TEST] wp096.stomp ultra=" + ultra.ToString("F2", CultureInfo.InvariantCulture) + " clip=" + clip.ToString("F2", CultureInfo.InvariantCulture)
                      + " honest=" + slowest.ToString("F3", CultureInfo.InvariantCulture) + ".." + fastest.ToString("F3", CultureInfo.InvariantCulture));
            Assert.GreaterOrEqual(slowest, zombie.MinRateOf(C.ZombieWalkStompV3), "Executioner dürüst oranı tabanın altına inmemeli");
            Assert.LessOrEqual(fastest, zombie.MaxRateOf(C.ZombieWalkStompV3));
        }

        [Test]
        public void KlipBandi_VarsayilanCadenceBand()
        {
            var set = ScriptableObject.CreateInstance<CharacterAnimationSet>();
            try
            {
                var clips = new List<CharacterAnimationSet.Entry>
                {
                    new CharacterAnimationSet.Entry { name = "plain", speedMps = 2f },
                    new CharacterAnimationSet.Entry { name = "banded", speedMps = 2f, minRate = 0.5f, maxRate = 1.4f }
                };
                set.Configure(null, null, 1.4f, null, "test", clips, "", 1.485f);
                Assert.AreEqual(CadenceBand.Min, set.MinRateOf("plain"));
                Assert.AreEqual(CadenceBand.Max, set.MaxRateOf("plain"));
                Assert.AreEqual(0.5f, set.MinRateOf("banded"));
                Assert.AreEqual(1.4f, set.MaxRateOf("banded"));
                Assert.AreEqual(CadenceBand.Min, set.MinRateOf("missing"), "olmayan klip → varsayılan");
            }
            finally
            {
                Object.DestroyImmediate(set);
            }
        }

        [Test]
        public void YuruyusAnahtari_MutlakEsikHisterezis()
        {
            Assert.IsFalse(ZombieLocomotionMath.GaitOn(false, 0.29f));
            Assert.IsTrue(ZombieLocomotionMath.GaitOn(false, 0.30f));
            Assert.IsTrue(ZombieLocomotionMath.GaitOn(true, 0.25f), "histerezis: açıkken 0.22'ye kadar kalır");
            Assert.IsFalse(ZombieLocomotionMath.GaitOn(true, 0.21f));
            Assert.Less(ZombieLocomotionMath.GaitOffSpeed, ZombieLocomotionMath.GaitOnSpeed);
        }

        // ------------------------------------------------------------------ P1

        [Test]
        public void KadansHizi_ItmeVeSikismaRitmiSisirmez()
        {
            Assert.AreEqual(2.7f, ZombieLocomotionMath.CadenceSpeed(2.7f, 2.7f), 1e-6f, "serbest yürüyüş");
            Assert.AreEqual(2.7f, ZombieLocomotionMath.CadenceSpeed(5.5f, 2.7f), 1e-6f, "arkadan itme / knockback ritmi şişirmez");
            Assert.AreEqual(0f, ZombieLocomotionMath.CadenceSpeed(-3f, 2.7f), 1e-6f, "geri itilen gövde ileri adım atmaz");
            Assert.AreEqual(0f, ZombieLocomotionMath.CadenceSpeed(0f, 2.7f), 1e-6f, "sıkışan gövde yerinde yürümez");
            Assert.AreEqual(0f, ZombieLocomotionMath.CadenceSpeed(1.2f, 0f), 1e-6f, "kendi niyeti yokken itme adım attırmaz");
            Assert.AreEqual(0.4f, ZombieLocomotionMath.CadenceSpeed(0.4f, 2.7f), 1e-6f, "gerçek hız niyetin altında → gerçek hız");
        }

        [Test]
        public void KritikSonumlu_AsimYok_KareHizindanBagimsiz()
        {
            const float tau = 0.1f, target = 2.7f;
            var at = new List<float>();
            foreach (int fps in new[] { 30, 60, 120 })
            {
                float x = 0f, v = 0f, last = 0f, dt = 1f / fps;
                int steps = Mathf.RoundToInt(0.3f * fps);
                for (int i = 0; i < steps; i++)
                {
                    ZombieLocomotionMath.CriticallyDamped(ref x, ref v, target, tau, dt);
                    Assert.GreaterOrEqual(x, last - 1e-5f, "monoton (sıçrama yok) fps=" + fps);
                    Assert.LessOrEqual(x, target + 1e-4f, "aşım yok fps=" + fps);
                    last = x;
                }

                at.Add(x);
            }

            Assert.AreEqual(at[1], at[0], at[1] * 0.02f, "30 fps ↔ 60 fps ≤ %2");
            Assert.AreEqual(at[1], at[2], at[1] * 0.02f, "120 fps ↔ 60 fps ≤ %2");
            Assert.Greater(at[1], target * 0.9f, "3τ içinde ≥ %90");

            float y = 1f, w = 0f;
            ZombieLocomotionMath.CriticallyDamped(ref y, ref w, 5f, 0.1f, 0f);
            Assert.AreEqual(1f, y, "dt 0 → değişmez");
        }

        // ------------------------------------------------------------------ P6

        [Test]
        public void Ivme_TabloVeRampa()
        {
            Assert.AreEqual(6f, ZombieLocomotionMath.AccelerationFor(EnemyTier.Standard));
            Assert.AreEqual(10f, ZombieLocomotionMath.AccelerationFor(EnemyTier.Special));
            Assert.AreEqual(4f, ZombieLocomotionMath.AccelerationFor(EnemyTier.Elite));
            Assert.AreEqual(3f, ZombieLocomotionMath.AccelerationFor(EnemyTier.Ultra));

            float s = 0f;
            int frames = 0;
            while (s < 2.7f && frames < 1000)
            {
                float next = ZombieLocomotionMath.Accelerate(s, 2.7f, 6f, 12f, Dt);
                Assert.LessOrEqual(next - s, 6f * Dt + 1e-5f, "ivme sınırı");
                s = next;
                frames++;
            }

            Assert.AreEqual(Mathf.CeilToInt(2.7f / (6f * Dt) - 1e-3f), frames, 1, "0 → 2.7 m/s ≈ 0.45 s");
            float braked = ZombieLocomotionMath.Accelerate(2.7f, 0f, 6f, 12f, Dt);
            Assert.AreEqual(2.7f - 12f * Dt, braked, 1e-5f, "frende BrakeScale × ivme");
            Assert.AreEqual(1f, ZombieLocomotionMath.Accelerate(1f, 2f, 6f, 12f, 0f), "dt 0 → değişmez");
        }

        // ------------------------------------------------------------------ P7

        [Test]
        public void BasYumusatici_AcisalHizVeIvmeSiniri_AsimYok()
        {
            const float cap = 120f + 62f * 2.7f;   // the Standard's turn-rate cap
            float alpha = cap / ZombieLocomotionMath.TurnRampSeconds;
            foreach (float target in new[] { 90f, -90f, 180f, 30f })
            {
                float yaw = 0f, omega = 0f, lastOmega = 0f;
                int frames = 0;
                bool landed = false;
                for (; frames < 600; frames++)
                {
                    float before = Mathf.DeltaAngle(yaw, target);
                    yaw = ZombieLocomotionMath.StepYaw(yaw, ref omega, target, cap, alpha, Dt);
                    float after = Mathf.DeltaAngle(yaw, target);
                    Assert.LessOrEqual(Mathf.Abs(omega), cap + 1e-3f, "açısal hız tavanı");
                    if (omega != 0f) Assert.LessOrEqual(Mathf.Abs(omega - lastOmega), alpha * Dt + 1e-2f, "açısal ivme sınırı");
                    Assert.LessOrEqual(Mathf.Abs(after), Mathf.Abs(before) + 1e-3f, "hedefi aşmaz / geri dönmez");
                    lastOmega = omega;
                    if (Mathf.Abs(after) < 1e-3f) { landed = true; break; }
                }

                Assert.IsTrue(landed, "hedefe varmadı: " + target);
                float minSeconds = Mathf.Abs(target) / cap;
                Assert.GreaterOrEqual((frames + 1) * Dt, minSeconds - Dt, "tavandan hızlı dönemez");
                Assert.LessOrEqual((frames + 1) * Dt, minSeconds + 2f * ZombieLocomotionMath.TurnRampSeconds + 0.1f, "makul sürede varır: " + target);
            }

            // the target flips mid-turn: the angular velocity changes by at most alpha·dt per frame (no snap)
            float y = 0f, w = 0f;
            for (int i = 0; i < 10; i++) y = ZombieLocomotionMath.StepYaw(y, ref w, 90f, cap, alpha, Dt);
            float wBefore = w;
            y = ZombieLocomotionMath.StepYaw(y, ref w, -90f, cap, alpha, Dt);
            Assert.LessOrEqual(Mathf.Abs(w - wBefore), alpha * Dt + 1e-2f, "yön değişiminde açısal hız sıçramaz");

            // alpha 0 = the old constant-rate turn
            float o = 0f;
            Assert.AreEqual(cap * Dt, ZombieLocomotionMath.StepYaw(0f, ref o, 90f, cap, 0f, Dt), 1e-4f);
        }

        [Test]
        public void DonusPaylari_Monoton_UcDegerler()
        {
            Assert.AreEqual(1f, ZombieLocomotionMath.HeadingSpeedShare(0f));
            Assert.AreEqual(1f, ZombieLocomotionMath.HeadingSpeedShare(ZombieLocomotionMath.TurnFullSpeedError));
            Assert.AreEqual(ZombieLocomotionMath.TurnSpeedShareMin, ZombieLocomotionMath.HeadingSpeedShare(180f), 1e-5f);
            Assert.AreEqual(0f, ZombieLocomotionMath.FacingBlend(0f));
            Assert.AreEqual(1f, ZombieLocomotionMath.FacingBlend(ZombieLocomotionMath.PathFollowError));
            float lastShare = 2f, lastBlend = -1f;
            for (float e = 0f; e <= 180f; e += 1f)
            {
                float share = ZombieLocomotionMath.HeadingSpeedShare(e), blend = ZombieLocomotionMath.FacingBlend(e);
                Assert.LessOrEqual(share, lastShare + 1e-6f, "hız payı açıyla azalır");
                Assert.GreaterOrEqual(blend, lastBlend - 1e-6f, "yol payı açıyla artar");
                lastShare = share;
                lastBlend = blend;
            }
        }

        /// <summary>
        /// P7 safety: a body whose walk follows its facing (the SteerOwnVelocity composition) still reaches a corner from any
        /// start heading — the distance never grows (no orbit) and the corner reach radius (0.65 m) is entered in bounded time.
        /// </summary>
        [Test]
        public void YuzYonuTakibi_KoseyeUlasir_YorungeYok()
        {
            (EnemyTier tier, float speed)[] tiers = { (EnemyTier.Standard, 2.7f), (EnemyTier.Special, 4.6f), (EnemyTier.Elite, 2.2f), (EnemyTier.Ultra, 1.7f) };
            foreach ((EnemyTier tier, float speed) t in tiers)
            {
                float cap = 120f + 62f * t.speed, alpha = cap / ZombieLocomotionMath.TurnRampSeconds;
                float accel = ZombieLocomotionMath.AccelerationFor(t.tier);
                foreach (float startYaw in new[] { 0f, 45f, 90f, 135f, 180f, -100f })
                {
                    foreach (float dist in new[] { 0.8f, 1.5f, 5f })
                    {
                        Vector3 p = Vector3.zero, corner = new Vector3(0f, 0f, dist);
                        float yaw = startYaw, omega = 0f, steer = t.speed;   // already walking at full speed
                        float lastDistance = dist;
                        bool reached = false;
                        for (int f = 0; f < 600 && !reached; f++)
                        {
                            Vector3 heading = (corner - p).normalized;
                            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                            float error = Vector3.Angle(forward, heading);
                            float blend = ZombieLocomotionMath.FacingBlend(error);
                            Vector3 dir = blend >= 1f ? heading : blend <= 0f ? forward : Vector3.Slerp(forward, heading, blend).normalized;
                            steer = ZombieLocomotionMath.Accelerate(steer, t.speed * ZombieLocomotionMath.HeadingSpeedShare(error), accel, accel * ZombieLocomotionMath.BrakeScale, Dt);
                            p += dir * steer * Dt;
                            yaw = ZombieLocomotionMath.StepYaw(yaw, ref omega, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg, cap, alpha, Dt);
                            float d = Vector3.Distance(p, corner);
                            Assert.LessOrEqual(d, lastDistance + 1e-4f, "köşe uzaklaştı (yörünge) " + t.tier + " yaw=" + startYaw + " d=" + dist);
                            lastDistance = d;
                            reached = d <= 0.65f;
                        }

                        Assert.IsTrue(reached, "köşeye ulaşılmadı " + t.tier + " yaw=" + startYaw + " d=" + dist);
                    }
                }
            }
        }
    }
}
