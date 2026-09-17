using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Weapons;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-047 (S-07, S-02 WP-053 finding): the HotPathAllocationContractTests window (300 armoured hits + 30 explosions
    /// in one frame, damage numbers off) split by VFX source — the whole AP-010 layer muted (WP-002 fallbacks), hits
    /// alone, explosions alone, blood steps — so the ~100 extra allocations per window have an owner line each.
    /// Same fixture, same shots, same counters (`Profiler.enabled`). Runs alone or first in a batch: after other PlayMode
    /// fixtures the frame counters read 0/1 for the positive control (WP-042B / WP-053 note) and the test aborts as invalid.
    /// </summary>
    [TestFixture]
    [Category("WP047")]
    public sealed class VfxHotPathBisectTests
    {
        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            VfxDressing.AuditMute = false;
            VfxDressing.AuditExplosionMask = -1;
            BloodSystem.AuditStepMask = -1;
            if (HitFeedbackDirector.Instance != null) HitFeedbackDirector.Instance.ShowDamageNumbers = true;
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

        private static void Hits(List<ZombieEnemy> zombiler) { for (int i = 0; i < 300; i++) Vur(zombiler[i % 4], (BodyRegion)(i % 3)); }
        private static void Explosions(Vector3 merkez) { for (int i = 0; i < 30; i++) HitscanEngine.Explode(merkez + Vector3.up * 0.9f, 1f, 3f); }

        /// <summary>Three single-frame windows of <paramref name="burst"/>, one settle frame between; the smallest count wins (scene jitter).</summary>
        private static IEnumerator Min3(ProfilerRecorder count, ProfilerRecorder bytes, Action burst, long[] result)
        {
            result[0] = long.MaxValue; result[1] = 0;
            for (int r = 0; r < 3; r++)
            {
                burst();
                yield return null;
                if (count.LastValue < result[0]) { result[0] = count.LastValue; result[1] = bytes.LastValue; }
                yield return null;
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator VurusYolu_VfxKaynaklarinaGoreAyirma()
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
            Assert.IsNotNull(fd);
            fd.ShowDamageNumbers = false;
            bool profilerWas = UnityEngine.Profiling.Profiler.enabled;
            UnityEngine.Profiling.Profiler.enabled = true;
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null; yield return null;
                var yem = new object[300];
                for (int i = 0; i < yem.Length; i++) yem[i] = new byte[80];
                yield return null;
                long control = count.LastValue;
                GC.KeepAlive(yem);
                Assert.Greater(control, 250, "sayaç 300 küçük ayırmayı görmedi — ölçüm geçersiz (" + control + ")");

                // Warm-up: two full windows with everything on, then settle.
                Hits(zombiler); Explosions(merkez); yield return null;
                Hits(zombiler); Explosions(merkez); yield return null;
                for (int i = 0; i < 30; i++) yield return null;
                var idle = new long[2]; yield return Min3(count, bytes, () => { }, idle);

                var full = new long[2]; yield return Min3(count, bytes, () => { Hits(zombiler); Explosions(merkez); }, full);
                var hitsOnly = new long[2]; yield return Min3(count, bytes, () => Hits(zombiler), hitsOnly);
                var explOnly = new long[2]; yield return Min3(count, bytes, () => Explosions(merkez), explOnly);
                VfxDressing.AuditMute = true;
                var muted = new long[2]; yield return Min3(count, bytes, () => { Hits(zombiler); Explosions(merkez); }, muted);
                var mutedHits = new long[2]; yield return Min3(count, bytes, () => Hits(zombiler), mutedHits);
                var mutedExpl = new long[2]; yield return Min3(count, bytes, () => Explosions(merkez), mutedExpl);
                VfxDressing.AuditMute = false;
                var steps = new long[4][];
                for (int m = 0; m < 4; m++)
                {
                    VfxDressing.AuditExplosionMask = 1 << m;
                    steps[m] = new long[2];
                    yield return Min3(count, bytes, () => Explosions(merkez), steps[m]);
                }
                VfxDressing.AuditExplosionMask = -1;
                fd.ShowDamageNumbers = true;
                yield return null;
                var numbers = new long[2]; yield return Min3(count, bytes, () => Hits(zombiler), numbers);
                fd.ShowDamageNumbers = false;

                Debug.Log("[DD-TEST] wp047.bisect control=" + control + " idle=" + idle[0] + "/" + idle[1] + "B" +
                          " | full=" + full[0] + "/" + full[1] + " hits=" + hitsOnly[0] + "/" + hitsOnly[1] + " expl=" + explOnly[0] + "/" + explOnly[1] +
                          " | muted: full=" + muted[0] + "/" + muted[1] + " hits=" + mutedHits[0] + "/" + mutedHits[1] + " expl=" + mutedExpl[0] + "/" + mutedExpl[1] +
                          " | explosion steps card=" + steps[0][0] + " shockwave=" + steps[1][0] + " smoke=" + steps[2][0] + " scorch=" + steps[3][0] +
                          " | numbersOn hits=" + numbers[0] + "/" + numbers[1] +
                          " | pools flipbook=" + fd.Vfx.FlipbookPool.CountSpawned + "/" + fd.Vfx.FlipbookPool.CountTotal + " decal=" + fd.Vfx.DecalPool.CountSpawned + "/" + fd.Vfx.DecalPool.CountTotal +
                          " played=" + fd.Vfx.Played + " wave2=" + fd.Vfx.Wave2Played);

                // Contract (ADR-037-1), each on the smallest of three frames: the AP-010 layer adds nothing to the idle frame
                // — full window, hits alone, explosions alone (the scorch decal pool used to grow here), numbers on.
                const int Slack = 12;
                Assert.LessOrEqual(full[0], idle[0] + Slack, "vuruş + patlama penceresi ayırıyor: " + full[0] + " (idle " + idle[0] + ")");
                Assert.LessOrEqual(hitsOnly[0], idle[0] + Slack, "vuruş yolu ayırıyor: " + hitsOnly[0] + " (idle " + idle[0] + ")");
                Assert.LessOrEqual(explOnly[0], idle[0] + Slack, "patlama yolu ayırıyor: " + explOnly[0] + " (idle " + idle[0] + ")");
                Assert.LessOrEqual(numbers[0], idle[0] + Slack, "hasar sayısı yolu ayırıyor: " + numbers[0] + " (idle " + idle[0] + ")");
            }

            UnityEngine.Profiling.Profiler.enabled = profilerWas;
        }
    }
}
