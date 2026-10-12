using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-037 (ADR-037-1) · the hot-path zero-allocation contract measured with the
    /// profiler's per-frame counters (every managed allocation, no block granularity):
    /// a frame carrying 300 direct hits + 30 explosions on armoured targets, with the
    /// damage numbers switched off, allocates nothing beyond the idle frame — that is the
    /// damage core, the hitscan engine, the audio / feedback raise path, the HUD hit
    /// markers and every bus subscriber. The damage-number text (Feedback,
    /// S-07: TextMesh.text = int.ToString()) is the one known per-hit allocator; it is
    /// measured and logged as an observation until its owner caches the strings, after
    /// which the second assert flips on.
    /// </summary>
    [TestFixture]
    [Category("WP037")]
    public sealed class HotPathAllocationContractTests
    {
        /// <summary>Allocations the test harness itself may add to a measured frame (coroutine step, recorder). Idle frames read 6.</summary>
        private const int HarnessAllocationSlack = 12;

        /// <summary>The Special's steady active frame is the least of this many frames in a row (a one-off burst cannot fail it).</summary>
        private const int SteadyFrames = 5;
        private const int RecorderSampleFrames = 6;

        private SpecialWeaponSahne _s;
        private int _targetFrameRateWas;
        private bool _restoreTargetFrameRate;
        private bool _profilerWas;
        private bool _restoreProfiler;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_restoreProfiler)
            {
                UnityEngine.Profiling.Profiler.enabled = _profilerWas;
                _restoreProfiler = false;
            }

            if (_restoreTargetFrameRate)
            {
                Application.targetFrameRate = _targetFrameRateWas;
                _restoreTargetFrameRate = false;
            }

            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        private static void Vur(ZombieEnemy z, BodyRegion bolge)
        {
            Collider hitbox = z.HitboxFor(bolge);
            Vector3 hedef = hitbox.bounds.center;
            Vector3 geri = -z.transform.forward;
            geri.y = 0f;
            geri = geri.sqrMagnitude > 0.0001f ? geri.normalized : Vector3.back;
            Vector3 kaynak = hedef - geri * 2.5f;
            kaynak.y = hedef.y;
            var istek = new ShotRequest
            {
                Origin = kaynak, Direction = (hedef - kaynak).normalized, Damage = 1f, Range = 6f,
                ImpactForce = DamageForce.RifleBullet, Source = DamageSourceKind.Bullet, Power = WeaponPower.Medium,
                PierceTargets = 0, PierceDamageFalloff = 1f, DrawTracer = false
            };
            HitscanEngine.Fire(in istek);
        }

        private static void Window(List<ZombieEnemy> zombiler, Vector3 merkez)
        {
            for (int i = 0; i < 300; i++) Vur(zombiler[i % 4], (BodyRegion)(i % 3));
            for (int i = 0; i < 30; i++) HitscanEngine.Explode(merkez + Vector3.up * 0.9f, 1f, 3f);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator VurusYolu_KareBasiAyirma_SifirSozlesmesi()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            Vector3 merkez = _s.Zemin(0f, 8f);
            var zombiler = new List<ZombieEnemy>();
            for (int i = 0; i < 4; i++) zombiler.Add(_s.Dogur(merkez + new Vector3((i - 1.5f) * 0.9f, 0f, 0f)));
            yield return null;
            foreach (ZombieEnemy z in zombiler)
            {
                z.FitArmorComposition(true, true, true);
                z.Armor.Equip(ArmorSlot.Helmet, 100000f);
                z.Armor.Equip(ArmorSlot.Body, 100000f);
                z.Armor.Equip(ArmorSlot.Leg, 100000f);
            }

            HitFeedbackDirector fd = HitFeedbackDirector.Instance;
            Assert.IsNotNull(fd, "HitFeedbackDirector yok.");
            bool numbersWere = fd.ShowDamageNumbers;

            // WP-042B: the GC frame counters only count while the profiler is enabled — in a batch run the test
            // framework enables it, in the Editor GUI with the Profiler window closed they read 0/1 (S-07 saw the
            // positive control fail that way). Enable it for the measurement and restore afterwards.
            _profilerWas = UnityEngine.Profiling.Profiler.enabled;
            _restoreProfiler = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            // The bootstrapper caps the player loop at 60. In the Editor test runner that
            // leaves ProfilerRecorder.LastValue one frame behind (the 300-object control
            // reads as 1). Uncap only this measurement and restore it in UnityTearDown,
            // including assertion failures.
            _targetFrameRateWas = Application.targetFrameRate;
            _restoreTargetFrameRate = true;
            Application.targetFrameRate = -1;
            yield return null;
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null;
                yield return null;
                Assert.IsTrue(bytes.Valid && count.Valid, "Profiler kare sayaçları yok (Editor / development build gerekir).");

                // Positive control: the counters see 300 small objects in a frame.
                var yem = new object[300];
                for (int i = 0; i < yem.Length; i++) yem[i] = new byte[80];
                long controlCount = 0;
                for (int i = 0; i < RecorderSampleFrames; i++)
                {
                    yield return null;
                    controlCount = Math.Max(controlCount, count.LastValue);
                }
                Assert.Greater(controlCount, 250, "Sayaç 300 küçük ayırmayı görmedi — ölçüm geçersiz (" + controlCount + ").");
                GC.KeepAlive(yem);

                // Warm-up: caches, pools, capacities. Two windows.
                Window(zombiler, merkez);
                yield return null;
                Window(zombiler, merkez);
                yield return null;
                yield return null;
                long idleCount = count.LastValue;
                long idleBytes = bytes.LastValue;

                // CONTRACT: damage numbers off → the whole path allocates nothing.
                fd.ShowDamageNumbers = false;
                Window(zombiler, merkez);
                yield return null;
                long coreCount = count.LastValue;
                long coreBytes = bytes.LastValue;
                fd.ShowDamageNumbers = numbersWere;
                yield return null;   // let the setter-frame's own allocations (if any) pass

                // OBSERVATION (S-07 item): damage numbers on.
                Window(zombiler, merkez);
                yield return null;
                long fullCount = count.LastValue;
                long fullBytes = bytes.LastValue;
                yield return null;

                Debug.Log("[DD-TEST] wp037.hotpath idle=" + idleCount + "/" + idleBytes + "B core(numbersOff)=" + coreCount + "/" + coreBytes +
                          "B full(numbersOn)=" + fullCount + "/" + fullBytes + "B window=300hits+30explosions");

                Assert.LessOrEqual(coreCount, idleCount + HarnessAllocationSlack,
                    "Vuruş/patlama yolu (hasar sayıları kapalı) karede ayırma yaptı: " + coreCount + " ayırma / " + coreBytes + " B (idle " + idleCount + ").");

                // Flip-on assert for the damage-number strings: passes once Feedback caches them.
                // Until then the observation line above carries the number (WP-006 M4, S-07).
                if (fullCount <= idleCount + HarnessAllocationSlack)
                {
                    Debug.Log("[DD-TEST] wp037.hotpath damageNumbers=zero-alloc (S-07 düzeltmesi yerinde)");
                }
                else
                {
                    Debug.Log("[DD-OBS] wp037.hotpath damageNumbers allocate " + (fullCount - coreCount) + " objects / " + (fullBytes - coreBytes) +
                              " B per window — UI/Runtime/Fx/UiWorldNumbers.cs (WP-068 E18; eski TextMesh.text = int.ToString(), S-07, WP-006 M4 kalemi)");
                }
            }

        }

        /// <summary>Special Weapon pickup → active → final seconds → end: the lifecycle frames measured the same way (observation + subscriber-side contract for Weapons).</summary>
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator SpecialAlmaVeBitim_KareBasiAyirma_Gozlem()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();

            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null;
                yield return null;
                Assert.IsTrue(count.Valid);

                yield return _s.SpawnEt(0.5f);
                yield return null;
                yield return null;
                long idle = count.LastValue;

                _s.PickupaGit(_s.CanliPickup());
                yield return null;   // pickup happens in this frame's Update
                long pickupFrame = count.LastValue, pickupBytes = bytes.LastValue;
                yield return null;
                long afterPickup = count.LastValue;
                Assert.IsTrue(_s.Controller.SpecialWeaponActive, "Special aktif olmadı.");

                // Active steady frame: the least of SteadyFrames frames in a row (C-00 12.10, S-02 bisect K/wp-104/b2/evidence/
                // BISECT-SpecialAlmaVeBitim.txt). A real per-frame allocation shows in every one of them and still fails; a one-off
                // burst after the pickup (it landed in one frame or another run to run) shows only in its own frame, logged below.
                long activeFrame = long.MaxValue, activeBytes = 0, burstFrame = 0, burstBytes = 0;
                var steady = new System.Text.StringBuilder();
                for (int f = 0; f < SteadyFrames; f++)
                {
                    yield return null;
                    long c = count.LastValue, b = bytes.LastValue;
                    steady.Append(f == 0 ? "" : ",").Append(c);
                    if (c < activeFrame) { activeFrame = c; activeBytes = b; }
                    if (c > burstFrame) { burstFrame = c; burstBytes = b; }
                }

                // Fast-forward to the end (final seconds + end frames inside).
                float adim = 0.05f;
                Time.captureDeltaTime = adim;
                long maxDuringActive = 0;
                float t0 = Time.realtimeSinceStartup;
                while (_s.Controller.SpecialWeaponActive && Time.realtimeSinceStartup - t0 < 60f)
                {
                    yield return null;
                    if (count.LastValue > maxDuringActive) maxDuringActive = count.LastValue;
                }

                Time.captureDeltaTime = 0f;
                long endFrame = count.LastValue, endBytes = bytes.LastValue;
                yield return null;
                yield return null;

                Debug.Log("[DD-TEST] wp037.special idle=" + idle + " pickupFrame=" + pickupFrame + "/" + pickupBytes + "B afterPickup=" + afterPickup +
                          " activeFrame=" + activeFrame + "/" + activeBytes + "B maxFrameWhileActive=" + maxDuringActive + " endFrame=" + endFrame + "/" + endBytes + "B" +
                          " steady[" + steady + "] burst=" + burstFrame + "/" + burstBytes + "B");

                // Contract on the Weapons side: a steady active frame (weapon in hand, no shots) adds nothing over idle.
                Assert.LessOrEqual(activeFrame, idle + HarnessAllocationSlack, "Aktif Special sabit karesi ayırma yapıyor: " + activeFrame + " (idle " + idle + ").");
            }
        }
    }
}
