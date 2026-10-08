using System;
using DD.Prototype.Audio;
using DD.Prototype.Audio.EditorTools;
using DD.Prototype.CameraSystem;
using DD.Prototype.Feedback;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension
using Is = UnityEngine.TestTools.Constraints.Is;   // derives from NUnit's Is (Is.InRange resolves); no CS0104

namespace DD.Prototype.Tests.EditMode.Audio
{
    /// <summary>
    /// WP-097 K1 (03 §6 EditMode, 04 decisions 1–7) · top-down weapon reflections, pure and in-editor:
    /// slap delay (2d/343) and level (−6.02 dB per doubling, floor / ceiling), cutoff bounds, greedy tap choice (90° / 6 ms),
    /// reflector filter, the probe's 1 m / 0.5 s cache and enclosure guess with hysteresis, the string tail bed policy at 900 RPM,
    /// the 900-block identities and their import rule, the top-down-only mix values (FP / TP untouched) and GC 0 on the hot calls.
    /// </summary>
    [TestFixture]
    [Category("WP097")]
    public sealed class WeaponReflectionTests
    {
        /// <summary>Far from anything the editor scene may hold.</summary>
        private static readonly Vector3 O = new Vector3(5000f, 1.4f, 5000f);

        private const int TestLayerMask = 1 << 0;   // Default: the primitives below

        // ------------------------------------------------------------------ pure arithmetic

        [Test]
        public void Gecikme_IkiKatMesafe_SesHizi()
        {
            Assert.AreEqual(0.046647f, WeaponReflections.DelaySeconds(8f), 1e-5f, "8 m → 46,6 ms (2d/343)");
            Assert.AreEqual(0.017493f, WeaponReflections.DelaySeconds(3f), 1e-5f, "3 m → 17,5 ms");
            Assert.AreEqual(0.233236f, WeaponReflections.DelaySeconds(40f), 1e-5f, "40 m → 233 ms");
            Assert.AreEqual(343f, WeaponReflections.SpeedOfSound);
        }

        [Test]
        public void Seviye_CiftMesafe_Eksi6dB_TabanVeTavan()
        {
            Assert.AreEqual(-6.0206f, WeaponReflections.SlapDb(16f) - WeaponReflections.SlapDb(8f), 0.01f, "mesafe iki katı → −6,02 dB");
            Assert.AreEqual(-10.06f, WeaponReflections.SlapDb(4f), 0.02f, "4 m");
            Assert.AreEqual(-18.02f, WeaponReflections.SlapDb(10f), 0.02f, "10 m");
            Assert.AreEqual(-24.04f, WeaponReflections.SlapDb(20f), 0.02f, "20 m");
            Assert.Less(WeaponReflections.SlapDb(40f), WeaponReflections.SlapFloorDb, "40 m tabanın altında (−30,1)");

            // Taban altı tap düşer: yalnız 40 m'de bir duvar → tap yok.
            var tek = new[] { 40f, Inf, Inf, Inf, Inf, Inf, Inf, Inf };
            var hepsi = new[] { true, true, true, true, true, true, true, true };
            var taps = new SlapTap[3];
            Assert.AreEqual(0, WeaponReflections.SelectTaps(tek, hepsi, 3, taps), "−30 dB altı tap çalınmaz");

            // Tavan: 2,5 m'de SlapDb −6 dB ama hiçbir tap −10 dB'den güçlü olamaz (TP payı).
            var yakin = new[] { 2.5f, Inf, Inf, Inf, Inf, Inf, Inf, Inf };
            Assert.AreEqual(1, WeaponReflections.SelectTaps(yakin, hepsi, 3, taps));
            Assert.Greater(WeaponReflections.SlapDb(2.5f), WeaponReflections.SlapCeilingDb, "öncül: 2,5 m tavanın üstünde");
            Assert.AreEqual(WeaponReflections.SlapCeilingDb, taps[0].GainDb, 1e-5f, "tap tavana kırpılır");
            Assert.AreEqual(WeaponReflections.DelaySeconds(2.5f), taps[0].Delay, 1e-6f);
        }

        [Test]
        public void Kesim_MonotonVeSinirli()
        {
            float onceki = float.MaxValue;
            for (float d = 0f; d <= 80f; d += 0.25f)
            {
                float c = WeaponReflections.SlapCutoffHz(d);
                Assert.That(c, Is.InRange(2500f, 16000f), "kesim [2,5; 16] kHz, d = " + d);
                Assert.LessOrEqual(c, onceki + 1e-3f, "kesim mesafeyle artmaz, d = " + d);
                onceki = c;
            }

            Assert.AreEqual(12709f, WeaponReflections.SlapCutoffHz(4f), 15f, "4 m ≈ 12,7 kHz");
            Assert.AreEqual(8997f, WeaponReflections.SlapCutoffHz(10f), 15f, "10 m ≈ 9,0 kHz");
            Assert.AreEqual(5060f, WeaponReflections.SlapCutoffHz(20f), 15f, "20 m ≈ 5,1 kHz");
            Assert.AreEqual(2500f, WeaponReflections.SlapCutoffHz(40f), 1e-3f, "≥ 33 m tabanda");
            Assert.AreEqual(16000f, WeaponReflections.SlapCutoffHz(0f), 1e-3f);

            // Echo (03 §2): gecikme sınırları, seviye ve kesim açıklıkla.
            Assert.AreEqual(WeaponReflections.EchoDelayDefaultSeconds, WeaponReflections.EchoDelay(-1f), 1e-6f, "uzak isabet yok → 0,18 s");
            Assert.AreEqual(0.08f, WeaponReflections.EchoDelay(5f), 1e-6f, "alt sınır 0,08 s");
            Assert.AreEqual(0.35f, WeaponReflections.EchoDelay(119f), 1e-6f, "üst sınır 0,35 s");
            Assert.AreEqual(-20f, WeaponReflections.EchoDb(0f), 1e-6f);
            Assert.AreEqual(-14f, WeaponReflections.EchoDb(1f), 1e-6f);
            Assert.AreEqual(3000f, WeaponReflections.EchoCutoffHz(0f), 1e-3f);
            Assert.AreEqual(5000f, WeaponReflections.EchoCutoffHz(1f), 1e-3f);
        }

        [Test]
        public void TapSecimi_KomsuIsinElenir_AciVeGecikmeAyrimi()
        {
            var dist = new[] { 5f, 5.2f, Inf, 12f, Inf, Inf, 30f, Inf };
            var valid = new[] { true, true, false, true, false, false, true, false };
            var taps = new SlapTap[3];

            Assert.AreEqual(2, WeaponReflections.SelectTaps(dist, valid, WeaponReflections.MaxTapsString, taps), "string: 2 tap");
            Assert.AreEqual(0, taps[0].Ray, "en yakın 0°");
            Assert.AreEqual(3, taps[1].Ray, "135° (45° komşu 5,2 m elenir)");

            Assert.AreEqual(3, WeaponReflections.SelectTaps(dist, valid, WeaponReflections.MaxTapsSingle, taps), "tek atış: 3 tap");
            Assert.AreEqual(0, taps[0].Ray);
            Assert.AreEqual(3, taps[1].Ray);
            Assert.AreEqual(6, taps[2].Ray, "270°");
            Assert.AreEqual(0f, Vector3.Angle(AcousticProbe.Direction(0), taps[0].Dir), 1e-3f);
            Assert.AreEqual(270f, Mathf.Repeat(Mathf.Atan2(taps[2].Dir.z, taps[2].Dir.x) * Mathf.Rad2Deg, 360f), 1e-3f, "ışın 6 = 270°");
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(WeaponReflections.DelaySeconds(taps[i].Distance), taps[i].Delay, 1e-6f);
                Assert.AreEqual(Mathf.Min(WeaponReflections.SlapDb(taps[i].Distance), WeaponReflections.SlapCeilingDb), taps[i].GainDb, 1e-5f);
                Assert.AreEqual(WeaponReflections.SlapCutoffHz(taps[i].Distance), taps[i].CutoffHz, 1e-2f);
            }

            // 6 ms kuralı: 90° ayrı ama gecikmesi 6 ms'den yakın iki duvar tek yansımadır (tarak filtresi olmaz).
            var yakinGecikme = new[] { 8f, Inf, 8.5f, Inf, Inf, Inf, Inf, Inf };   // 46,6 / 49,6 ms → |Δ| 2,9 ms
            Assert.AreEqual(1, WeaponReflections.SelectTaps(yakinGecikme, new[] { true, false, true, false, false, false, false, false }, 3, taps), "|Δgecikme| < 6 ms elenir");

            // Yansıtıcı süzgeci: yüzü dönük olmayan, dar ya da alçak collider ve 2,5–40 m dışı sayılmaz.
            Vector3 dir = Vector3.right;
            var duvar = new Bounds(Vector3.zero, new Vector3(0.5f, 4f, 10f));
            Assert.IsTrue(AcousticProbe.IsReflector(8f, dir, -dir, duvar), "yüzü dönük geniş duvar");
            Assert.IsFalse(AcousticProbe.IsReflector(8f, dir, Vector3.forward, duvar), "yüzü dönük değil (90°)");
            Assert.IsFalse(AcousticProbe.IsReflector(8f, dir, (-dir + Vector3.forward * 2f).normalized, duvar), "yüz ışına 63° → elenir (≥ 0,5 şartı)");
            Assert.IsFalse(AcousticProbe.IsReflector(8f, dir, -dir, new Bounds(Vector3.zero, new Vector3(0.6f, 6f, 0.6f))), "ağaç gövdesi / fener: dar");
            Assert.IsFalse(AcousticProbe.IsReflector(8f, dir, -dir, new Bounds(Vector3.zero, new Vector3(0.5f, 1.2f, 10f))), "çit: alçak");
            Assert.IsFalse(AcousticProbe.IsReflector(2f, dir, -dir, duvar), "2,5 m'den yakın");
            Assert.IsFalse(AcousticProbe.IsReflector(41f, dir, -dir, duvar), "40 m'den uzak");
        }

        // ------------------------------------------------------------------ probe (editor physics)

        [Test]
        public void Prob_Onbellek_1mVe05s_VeIsabet()
        {
            GameObject duvar = Kutu("wp097_wall", O + new Vector3(8.25f, 0.6f, 0f), new Vector3(0.5f, 4f, 10f));
            try
            {
                Physics.SyncTransforms();
                var probe = new AcousticProbe(TestLayerMask);
                Assert.IsTrue(probe.Refresh(O, 0f, false), "ilk çağrı ışın atar");
                Assert.AreEqual(9, probe.Casts, "8 yatay + 1 çatı ışını");
                Assert.AreEqual(8f, probe.Distances[0], 0.01f, "+X duvarı 8 m");
                Assert.IsTrue(probe.Valid[0], "duvar yansıtıcı");
                Assert.AreEqual(1, probe.ValidCount);
                Assert.IsFalse(probe.Roof);

                Assert.IsFalse(probe.Refresh(O + new Vector3(0.9f, 0f, 0f), 0.1f, false), "0,9 m: önbellek");
                Assert.AreEqual(9, probe.Casts, "0,9 m → Casts değişmez");
                Assert.IsTrue(probe.Refresh(O + new Vector3(1f, 0f, 0f), 0.25f, false), "1,0 m: yenile");
                Assert.AreEqual(18, probe.Casts, "1,0 m → +9");
                Assert.AreEqual(7f, probe.Distances[0], 0.01f);
                Assert.IsFalse(probe.Refresh(O + new Vector3(1f, 0f, 0f), 0.74f, false), "0,49 s: önbellek");
                Assert.IsTrue(probe.Refresh(O + new Vector3(1f, 0f, 0f), 0.75f, false), "0,5 s: yenile");
                Assert.AreEqual(27, probe.Casts);
                Assert.IsTrue(probe.Refresh(O + new Vector3(1f, 0f, 0f), 0.76f, true), "zorla");
                Assert.AreEqual(36, probe.Casts);

                // GC 0: ısınmış prob yenilemesi (zorla, her çağrı 9 ışın + açık havada uzak ışınlar) ayırmaz.
                Assert.That(() =>
                {
                    for (int i = 0; i < 50; i++) probe.Refresh(O + new Vector3(i * 0.01f, 0f, 0f), 1f + i, true);
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "prob yenilemesi ayırdı (DEC-088 GC 0)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(duvar);
            }
        }

        [Test]
        public void Prob_IcMekanTahmini_CatiVeDuvarlar_IkiYenilemeHisterezis()
        {
            // Saf kural (C-00 K3: dar → Corridor; 20–90 m² ve çatı ≤ 4 m → Room; daha büyük / yüksek → Hall).
            Assert.AreEqual(AcousticSpace.Open, AcousticProbe.RawGuess(Inf, new[] { 3f, 3f, 3f, 3f, 3f, 3f, 3f, 3f }), "çatı yok → açık");
            Assert.AreEqual(AcousticSpace.Open, AcousticProbe.RawGuess(3f, new[] { 3f, 3f, 3f, 3f, 3f, Inf, Inf, Inf }), "5/8 → açık (saçak altı)");
            Assert.AreEqual(AcousticSpace.Corridor, AcousticProbe.RawGuess(3f, new[] { 1.5f, 8f, 9f, 8f, 1.5f, 8f, 9f, 8f }), "en dar karşılıklı çift 3 m → koridor");
            var oda8 = new[] { 4f, 5.657f, 4f, 5.657f, 4f, 5.657f, 4f, 5.657f };
            Assert.AreEqual(64f, AcousticProbe.EstimateArea(oda8), 0.1f, "8 × 8 m oda merkezden 64 m²");
            Assert.AreEqual(AcousticSpace.Room, AcousticProbe.RawGuess(2.6f, oda8), "64 m², tavan 2,6 m → Room");
            Assert.AreEqual(AcousticSpace.Hall, AcousticProbe.RawGuess(5f, oda8), "64 m² ama tavan 5 m → Hall");
            var salon12 = new[] { 6f, 8.485f, 6f, 8.485f, 6f, 8.485f, 6f, 8.485f };
            Assert.AreEqual(144f, AcousticProbe.EstimateArea(salon12), 0.2f);
            Assert.AreEqual(AcousticSpace.Hall, AcousticProbe.RawGuess(3f, salon12), "144 m² > 90 → Hall");
            var kucuk = new[] { 2f, 2.5f, 2f, 2.5f, 2f, 2.5f, 2f, 2.5f };   // 4 × 4 m kabin: en dar çift 4 m, alan 14 m²
            Assert.AreEqual(AcousticSpace.Hall, AcousticProbe.RawGuess(2.6f, kucuk), "< 20 m² Room değil (C-00 K3 aralığı), dar değil → Hall");

            // Editör fiziğiyle: 8 × 8 m oda + çatı (2,6 m) → Room, ama yalnız ikinci yenilemede (histerezis).
            Vector3 c = O + new Vector3(100f, 0f, 0f);
            GameObject[] oda =
            {
                Kutu("wp097_n", c + new Vector3(0f, 0.6f, 4.25f), new Vector3(9f, 4f, 0.5f)),
                Kutu("wp097_s", c + new Vector3(0f, 0.6f, -4.25f), new Vector3(9f, 4f, 0.5f)),
                Kutu("wp097_e", c + new Vector3(4.25f, 0.6f, 0f), new Vector3(0.5f, 4f, 9f)),
                Kutu("wp097_w", c + new Vector3(-4.25f, 0.6f, 0f), new Vector3(0.5f, 4f, 9f)),
                Kutu("wp097_roof", c + new Vector3(0f, 2.7f, 0f), new Vector3(9f, 0.2f, 9f))
            };

            try
            {
                Physics.SyncTransforms();
                var probe = new AcousticProbe(TestLayerMask);
                probe.Refresh(c, 0f, true);
                Assert.IsTrue(probe.Roof, "çatı ışını isabet");
                Assert.AreEqual(2.6f, probe.RoofDistance, 0.01f, "çatı 2,6 m üstte");
                Assert.AreEqual(1f - 8f / 8f, probe.Openness, 1e-5f, "8/8 duvar 10 m içinde → açıklık 0");
                Assert.AreEqual(AcousticSpace.Open, probe.Guess, "ilk yenileme: henüz açık (histerezis)");
                Assert.IsFalse(probe.TryGuess(c, 0f, out _));
                probe.Refresh(c, 0.1f, true);
                Assert.AreEqual(AcousticSpace.Room, probe.Guess, "ikinci ardışık yenileme: oda (64 m², tavan ≤ 4 m)");
                Assert.IsTrue(probe.TryGuess(c + Vector3.up * 0.5f, 0.2f, out AcousticSpace s));
                Assert.AreEqual(AcousticSpace.Room, s);
                Assert.IsFalse(probe.TryGuess(c + Vector3.right * 3f, 0.2f, out _), "2 m dışındaki nokta için konuşmaz");
                Assert.IsFalse(probe.TryGuess(c, 0.1f + AcousticProbe.StaleSeconds + 0.01f, out _), "bayat prob konuşmaz");
                Assert.AreEqual(-1f, probe.FarMedian, "çatı altında uzak ışın yok");

                // Dışarı: ilk açık yenileme tahmini değiştirmez, ikincisi açar.
                Vector3 disari = c + new Vector3(30f, 0f, 0f);
                probe.Refresh(disari, 1f, true);
                Assert.AreEqual(AcousticSpace.Room, probe.Guess, "tek açık yenileme yetmez");
                probe.Refresh(disari, 1.1f, true);
                Assert.AreEqual(AcousticSpace.Open, probe.Guess, "ikinci açık yenileme");
            }
            finally
            {
                foreach (GameObject g in oda) UnityEngine.Object.DestroyImmediate(g);
            }
        }

        // ------------------------------------------------------------------ string tail bed

        [Test]
        public void KuyrukYatagi_900Rpm_EnFazla2Ses_BirakistaRampa()
        {
            var p = new TailBedPolicy();
            const float dt = 1f / 60f;
            const float period = 60f / 900f;
            const float clip = 1.8f;
            float t = 0f, nextShot = 0f;
            int shots = 0;
            float bed = TailBedPolicy.BedLevel;
            Assert.AreEqual(0.5012f, bed, 1e-3f, "yatak −6 dB");

            while (t < 2f)
            {
                if (t >= nextShot - 1e-5f)
                {
                    shots++;
                    nextShot += period;
                    if (p.Due(t)) p.Start(t, clip);
                }

                p.Tick(dt, t);
                t += dt;
                Assert.LessOrEqual(p.SoundingCount, 2, "en çok 2 yatak sesi, t = " + t);
                int cur = p.Current;
                Assert.GreaterOrEqual(cur, 0, "basılıyken yatak çalar, t = " + t);
                Assert.LessOrEqual(p.Level(cur), bed + 1e-5f, "basılıyken ıslaklık WetDb − 6 dB'i aşmaz");
            }

            Assert.AreEqual(2, p.PeakSounding, "çapraz geçişte iki ses");
            Assert.That(p.Starts, Is.InRange(8, 12), "0,15 s'de bir yeniden başlar (her atışta değil)");
            Assert.Less(p.Starts, shots / 2, "kuyruk her atışta yeniden başlamaz");

            // Bırakış: çalan yatak 0,06 s'de tam ıslak seviyeye rampa çıkar; yeni kuyruk yok, diğer yuva sessiz.
            int slot = p.Release(t);
            Assert.GreaterOrEqual(slot, 0, "bırakışta rampa");
            Assert.AreEqual(1f, p.Target(slot), 1e-6f);
            Assert.AreEqual(-1, p.Current, "bırakıştan sonra basılı yatak yok");
            int starts = p.Starts;
            for (int i = 0; i < 4; i++) { p.Tick(dt, t); t += dt; }
            Assert.AreEqual(1f, p.Level(slot), 1e-4f, "0,06 s (+ bir kare) içinde tam ıslak");
            // the other slot may still be in its 0.12 s crossfade from the last bed start (≤ 0.15 s before the release): silent once it ends
            for (int i = 0; i < 4; i++) { p.Tick(dt, t); t += dt; }
            Assert.AreEqual(0f, p.Level(1 - slot), 1e-6f, "öbür yuva çapraz geçişini bitirince sessiz (0,12 s)");
            Assert.AreEqual(1f, p.Level(slot), 1e-4f, "rampa tam ıslakta kalır");
            Assert.AreEqual(starts, p.Starts, "bırakış yeni kuyruk başlatmaz");

            // Klip bitince sessiz.
            for (int i = 0; i < 200; i++) { p.Tick(dt, t); t += dt; }
            Assert.AreEqual(0, p.SoundingCount);

            // Kuyruğu bitmek üzere olan yatak rampa almaz (çağıran taze kuyruk çalar).
            var q = new TailBedPolicy();
            q.Start(0f, 0.2f);
            q.Tick(dt, 0.15f);
            Assert.AreEqual(-1, q.Release(0.15f), "< 0,3 s kalmış → rampa yok");

            // Durdurma: 0,05 s rampa ile sessiz.
            var r = new TailBedPolicy();
            r.Start(0f, 2f);
            r.StopAll();
            for (int i = 0; i < 4; i++) r.Tick(dt, i * dt);
            Assert.AreEqual(0, r.SoundingCount, "StopAll 0,05 s'de susar");
        }

        // ------------------------------------------------------------------ identities, import rule, mix values

        [Test]
        public void Kimlik_900Blogu_IceAlmaKurali_VarsayilanDal()
        {
            Assert.AreEqual(900, (int)WeaponAudioIdentity.Echo(WeaponTailClass.Ar));
            Assert.AreEqual(901, (int)WeaponAudioIdentity.ReleaseTail(WeaponTailClass.Ar));
            Assert.AreEqual(950, (int)WeaponAudioIdentity.Echo(WeaponTailClass.Rl));
            Assert.AreEqual(951, (int)WeaponAudioIdentity.ReleaseTail(WeaponTailClass.Rl));
            Assert.AreEqual(990, (int)AudioSoundId.WeaponSlap);
            Assert.AreEqual(902, (int)WeaponAudioIdentity.Room(WeaponTailClass.Ar), "C-00 K3: Room = +2");
            Assert.AreEqual(952, (int)WeaponAudioIdentity.Room(WeaponTailClass.Rl));
            Assert.AreEqual(WeaponAudioIdentity.Room(WeaponTailClass.Sg), WeaponAudioIdentity.Tail(WeaponTailClass.Sg, AcousticSpace.Room), "Tail(Room) 900 bloğuna gider, sonraki sınıfın Open'ına değil");
            Assert.AreEqual(AudioSoundId.WeaponTailSmgOpen, WeaponAudioIdentity.Tail(WeaponTailClass.Smg, AcousticSpace.Open), "v016 aritmetiği değişmez");

            // Açık eşleme: Room hiçbir zaman DistanceMid'e düşmez.
            Assert.AreEqual(WeaponLayer.TailOpen, WeaponAudioIdentity.TailLayer(AcousticSpace.Open));
            Assert.AreEqual(WeaponLayer.TailCorridor, WeaponAudioIdentity.TailLayer(AcousticSpace.Corridor));
            Assert.AreEqual(WeaponLayer.TailHall, WeaponAudioIdentity.TailLayer(AcousticSpace.Hall));
            Assert.AreEqual(WeaponLayer.TailCorridor, WeaponAudioIdentity.TailLayer(AcousticSpace.Room), "Room geri dönüşü TailCorridor");
            Assert.AreEqual(0.005f, WeaponAudioIdentity.EarlyOffsetSeconds(AcousticSpace.Room), 1e-6f, "Room ilk yansıma 5 ms");
            Assert.AreEqual(3, (int)AcousticSpace.Room, "enum sonuna eklendi (serileştirilmiş 0–2 değişmez)");
            foreach (WeaponTailClass c in Enum.GetValues(typeof(WeaponTailClass)))
            {
                string oda = WeaponAudioIdentity.Room(c).ToString();
                Assert.AreEqual("WeaponTail" + c + "Room", oda);
                Assert.IsTrue(AudioCatalogBuilder.IsStereoTail(oda), oda + " stereo q9 kuyruk kuralı");
                Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, AudioCatalogBuilder.WantedLoadType(oda, false), oda + " oda kuyruğu diğer kuyruklar gibi");
                float[] ornek = AudioRecipes.Build(WeaponAudioIdentity.Room(c), 0);
                Assert.IsTrue(ornek != null && ornek.Length > 64, "recipe varsayılan dalı boş değil: " + oda);
            }
            Assert.AreEqual("WeaponTailArEcho", WeaponAudioIdentity.Echo(WeaponTailClass.Ar).ToString());

            foreach (WeaponTailClass c in Enum.GetValues(typeof(WeaponTailClass)))
            {
                foreach (AudioSoundId id in new[] { WeaponAudioIdentity.Echo(c), WeaponAudioIdentity.ReleaseTail(c) })
                {
                    Assert.IsTrue(Enum.IsDefined(typeof(AudioSoundId), id), c + " " + (int)id + " AudioSoundId'de tanımlı");
                    string ad = id.ToString();
                    StringAssert.StartsWith("WeaponTail" + c, ad);
                    Assert.IsTrue(AudioCatalogBuilder.IsStereoTail(ad), ad + " stereo q9 kuyruk kuralı");
                    Assert.IsTrue(AudioCatalogBuilder.IsEchoOrRelease(ad), ad);
                    Assert.AreEqual(AudioClipLoadType.CompressedInMemory, AudioCatalogBuilder.WantedLoadType(ad, false), ad + " CompressedInMemory");
                    float[] ornek = AudioRecipes.Build(id, 0);
                    Assert.IsTrue(ornek != null && ornek.Length > 64, "recipe varsayılan dalı boş değil: " + ad);
                }
            }

            Assert.IsFalse(AudioCatalogBuilder.IsEchoOrRelease("WeaponAr1TailOpen"));
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, AudioCatalogBuilder.WantedLoadType("WeaponAr1Fire", false), "diğer sesler değişmez");
            Assert.AreEqual(AudioClipLoadType.Streaming, AudioCatalogBuilder.WantedLoadType("WeaponTailArEcho", true), "≥ 8 s akış kalır");
            Assert.AreEqual(6, Enum.GetValues(typeof(WeaponLayer)).Length, "WeaponLayer'a değer eklenmez (AtesV4_AP027Ithal 10 silah × katman)");

            // Üretilmemiş kimlik sessiz: katalogsuz kütüphane onları üretmiş saymaz.
            var lib = new AudioClipLibrary(null);
            Assert.IsFalse(lib.IsProduced(WeaponAudioIdentity.Echo(WeaponTailClass.Ar)));
            Assert.IsFalse(lib.IsProduced(WeaponAudioIdentity.ReleaseTail(WeaponTailClass.Sg)));
        }

        [Test]
        public void YalnizTopDown_IslakEksi6_Spatial020_Duck1dB_FpTpAyni()
        {
            // Decision 4: açık avlu ıslak −9 → −6 yalnız top-down; eski imza ve FP / TP R2 değerleri.
            Assert.AreEqual(-6f, WeaponFireAudio.WetDb(AcousticSpace.Open, CameraMode.TopDown), 1e-6f);
            Assert.AreEqual(-9f, WeaponFireAudio.WetDb(AcousticSpace.Open), 1e-6f, "eski imza R2");
            foreach (CameraMode m in new[] { CameraMode.ThirdPerson, CameraMode.FirstPerson })
            {
                foreach (AcousticSpace a in Enum.GetValues(typeof(AcousticSpace)))
                {
                    Assert.AreEqual(WeaponFireAudio.WetDb(a), WeaponFireAudio.WetDb(a, m), 1e-6f, m + " " + a + " R2 ile aynı");
                }
            }

            Assert.AreEqual(-8f, WeaponFireAudio.WetDb(AcousticSpace.Corridor, CameraMode.TopDown), 1e-6f, "koridor değişmez");
            Assert.AreEqual(-7f, WeaponFireAudio.WetDb(AcousticSpace.Hall, CameraMode.TopDown), 1e-6f, "salon değişmez");
            Assert.AreEqual(-6f, WeaponFireAudio.WetDb(AcousticSpace.Room, CameraMode.TopDown), 1e-6f, "C-00 K3: TD oda kuyruğu −6");
            Assert.AreEqual(-8f, WeaponFireAudio.WetDb(AcousticSpace.Room), 1e-6f, "TD dışı / dosyasız oda = koridor −8");

            // Decision 5b: top-down Spatial 0,20; NearField / Tail / Mechanical değişmez; TP / FP birebir.
            WeaponPerspectiveMix td = WeaponPerspectiveMix.For(CameraMode.TopDown);
            Assert.AreEqual(0.20f, td.Spatial, 1e-6f);
            Assert.AreEqual(0.8f, td.NearFieldDb, 1e-6f);
            Assert.AreEqual(0.8f, td.TailDb, 1e-6f);
            Assert.AreEqual(0.25f, WeaponPerspectiveMix.For(CameraMode.ThirdPerson).Spatial, 1e-6f, "TP R2");
            Assert.AreEqual(0f, WeaponPerspectiveMix.For(CameraMode.FirstPerson).Spatial, 1e-6f, "FP R2");

            // Decision 5: −1 dB, 150 ms tutma.
            Assert.AreEqual(0.8913f, WeaponFireAudio.TopDownWorldDuckFloor, 1e-4f);
            Assert.AreEqual(0.15f, WeaponFireAudio.TopDownWorldDuckHoldSeconds, 1e-6f);

            // Decision 7: anahtar varsayılan açık.
            Assert.IsTrue(WeaponReflections.Enabled, "WeaponReflections.Enabled varsayılan true (bir test kapatıp geri açmadı mı?)");
        }

        [Test]
        public void SonDagitilan_AyniKlip_KarismazDesteDagitmaz()
        {
            var lib = new AudioClipLibrary(null);
            AudioClip a = AudioClip.Create("wp097_a", 480, 1, 48000, false);
            AudioClip b = AudioClip.Create("wp097_b", 480, 1, 48000, false);
            AudioClip c = AudioClip.Create("wp097_c", 480, 1, 48000, false);
            try
            {
                lib.OverrideForKit(AudioSoundId.WeaponAr1Fire, new[] { a, b, c });
                Assert.IsNull(lib.LastDealt(AudioSoundId.WeaponAr1Fire), "dağıtılmadan önce yok");
                for (int i = 0; i < 9; i++)
                {
                    AudioClip next = lib.Next(AudioSoundId.WeaponAr1Fire, out int _);
                    Assert.AreSame(next, lib.LastDealt(AudioSoundId.WeaponAr1Fire), "slap kuru atışın aynı klibini çalar");
                    Assert.AreSame(next, lib.LastDealt(AudioSoundId.WeaponAr1Fire), "LastDealt desteyi ilerletmez");
                }

                lib.OverrideForKit(AudioSoundId.WeaponSr1Fire, new[] { a });
                Assert.AreSame(a, lib.LastDealt(AudioSoundId.WeaponSr1Fire), "tek klipli ses");
            }
            finally
            {
                lib.ClearKitOverrides();
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
                UnityEngine.Object.DestroyImmediate(c);
            }
        }

        // ------------------------------------------------------------------ GC 0 on the hot calls

        [Test]
        public void GC_SelectTaps_VeTryHandle_200Atis_Ayirmaz()
        {
            var dist = new[] { 5f, 5.2f, Inf, 12f, Inf, Inf, 30f, Inf };
            var valid = new[] { true, true, false, true, false, false, true, false };
            var taps = new SlapTap[3];
            WeaponReflections.SelectTaps(dist, valid, 3, taps);
            Assert.That(() =>
            {
                for (int i = 0; i < 500; i++) WeaponReflections.SelectTaps(dist, valid, 1 + i % 3, taps);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "SelectTaps ayırdı");

            // TryHandle: sahte PlayFunc, top-down, 8 m duvar, tek atış (SR1) + seri (AR1) karışık 200 atış.
            CameraMode oncekiMod = CameraModeService.Current;
            if (oncekiMod != CameraMode.TopDown) CameraModeService.Request(CameraMode.TopDown, true);
            Func<Vector3, AcousticSpace> oncekiCozucu = AcousticSpaces.Resolver;
            AcousticSpaces.Resolver = null;
            bool oncekiAnahtar = WeaponReflections.Enabled;
            WeaponReflections.Enabled = true;
            GameObject duvar = Kutu("wp097_gc_wall", O + new Vector3(8.25f, 0.6f, 0f), new Vector3(0.5f, 4f, 10f));
            AudioClip kuru = AudioClip.Create("wp097_dry", 4800, 1, 48000, false);
            var lib = new AudioClipLibrary(null);
            try
            {
                Physics.SyncTransforms();
                lib.OverrideForKit(AudioSoundId.WeaponSr1Fire, new[] { kuru });
                lib.OverrideForKit(AudioSoundId.WeaponAr1Fire, new[] { kuru });
                int kuruSes = 0, slap = 0;
                WeaponFireAudio.PlayFunc play = (in AudioPlacement p, Vector3 pos, float g, int pr) => { kuruSes++; return true; };
                WeaponFireAudio.PlayClipFunc playClip = (in AudioPlacement p, AudioClip clip, Vector3 pos, float g, int pr) => { slap++; return clip != null; };
                var f = new WeaponFireAudio(lib, play);
                var probe = new AcousticProbe(TestLayerMask);
                f.SetReflections(probe, null, playClip, new AudioBusMixer());
                f.SetListener(() => O);
                var tek = new CombatAudioCue(CombatAudioEvent.ClassicWeaponFire, O + Vector3.forward * 0.3f, 1f, 50, false, "SR1");
                var seri = new CombatAudioCue(CombatAudioEvent.ClassicWeaponFire, O + Vector3.forward * 0.3f, 1f, 50, false, "AR1");

                // Isınma (ilk çağrılar: tablo, deste, JIT).
                Assert.IsTrue(f.TryHandle(tek));
                Assert.IsTrue(f.TryHandle(seri));
                Assert.IsTrue(f.TryHandle(tek));
                Assert.AreEqual(1, f.LastSlapCount, "tek duvar → tek slap (yol koştu)");
                Assert.AreEqual(WeaponReflections.DelaySeconds(8f), f.LastSlapDelay0, 1e-3f, "slap gecikmesi 2·8/343");
                Assert.AreEqual(WeaponReflections.SlapDb(8f), f.LastSlapGainDb0, 0.05f, "slap seviyesi SlapDb(8)");
                Assert.Greater(f.WorldDucks, 0, "top-down dünya duck'ı istendi");

                int slap0 = slap;
                Assert.That(() =>
                {
                    for (int i = 0; i < 200; i++) f.TryHandle((i & 3) == 0 ? tek : seri);
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "TryHandle (top-down yansıma yolu) ayırdı — DEC-088 GC 0");
                Assert.Greater(slap, slap0, "ölçüm penceresinde slap çaldı");
                Assert.Greater(kuruSes, 200, "kuru sesler çaldı");
                Debug.Log("[DD-TEST] wp097.k1.gc tryHandle=200 dry=" + kuruSes + " slaps=" + slap + " lastTaps=" + f.LastSlapCount + " delay0=" + f.LastSlapDelay0.ToString("0.0000") + " threadAlloc=none");
            }
            finally
            {
                lib.ClearKitOverrides();
                UnityEngine.Object.DestroyImmediate(kuru);
                UnityEngine.Object.DestroyImmediate(duvar);
                WeaponReflections.Enabled = oncekiAnahtar;
                AcousticSpaces.Resolver = oncekiCozucu;
                if (oncekiMod != CameraMode.TopDown) CameraModeService.Request(oncekiMod, true);
            }
        }

        // ------------------------------------------------------------------ helpers

        private const float Inf = float.PositiveInfinity;

        private static GameObject Kutu(string ad, Vector3 merkez, Vector3 boyut)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = ad;
            g.layer = 0;
            g.transform.position = merkez;
            g.transform.localScale = boyut;
            return g;
        }
    }
}
