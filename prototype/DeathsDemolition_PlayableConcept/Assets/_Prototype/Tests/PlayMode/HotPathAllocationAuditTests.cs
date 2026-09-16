using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-037 · attribution of managed allocations on the combat hot path. Runs the
    /// WP-006 window (300 direct hits + 30 explosions on 4 unbreakable armoured zombies)
    /// several times per configuration and reports the mono heap growth; then repeats
    /// with each bus subscriber (DamageEvents / AudioEvents / HitFeedbackDirector,
    /// resolved through the invocation lists by reflection) detached one at a time and
    /// with all of them detached. Output: <c>[DD-AUDIT]</c> lines, one per site. This is a
    /// diagnostic: it asserts only that the measurement is valid; the contract lives in
    /// RegionalArmorSliceTests.VurusYolu_SifirAyirma.
    /// </summary>
    [TestFixture]
    [Category("WP037")]
    public sealed class HotPathAllocationAuditTests
    {
        private const int Rounds = 3;   // windows per configuration (the heap grows in 16-64 KB steps; more windows = better resolution)

        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        private static readonly (Type type, string evt)[] Buses =
        {
            (typeof(DamageEvents), "EnemyDamaged"),
            (typeof(DamageEvents), "ArmorBroken"),
            (typeof(DamageEvents), "EnemyDied"),
            (typeof(DamageEvents), "PlayerAttacked"),
            (typeof(AudioEvents), "Cue"),
            (typeof(AudioEvents), "CueSuppressed"),
            (typeof(HitFeedbackDirector), "EnemyHitDressed"),
        };

        private sealed class Site
        {
            public Type Bus;
            public string Event;
            public FieldInfo Field;
            public Delegate Handler;
            public string Name => Event + " <- " + (Handler.Target != null ? Handler.Target.GetType().Name : Handler.Method.DeclaringType?.Name) + "." + Handler.Method.Name;
        }

        private static FieldInfo BackingField(Type type, string evt)
        {
            return type.GetField(evt, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }

        private static List<Site> CollectSites()
        {
            var sites = new List<Site>();
            foreach ((Type type, string evt) in Buses)
            {
                FieldInfo f = BackingField(type, evt);
                if (f == null)
                {
                    continue;
                }

                var d = f.GetValue(null) as Delegate;
                if (d == null)
                {
                    continue;
                }

                foreach (Delegate h in d.GetInvocationList())
                {
                    // Skip test-owned listeners (this assembly).
                    if (h.Method.DeclaringType != null && h.Method.DeclaringType.Assembly == typeof(HotPathAllocationAuditTests).Assembly)
                    {
                        continue;
                    }

                    sites.Add(new Site { Bus = type, Event = evt, Field = f, Handler = h });
                }
            }

            return sites;
        }

        private static void Detach(Site s)
        {
            var current = s.Field.GetValue(null) as Delegate;
            s.Field.SetValue(null, Delegate.Remove(current, s.Handler));
        }

        private static void Attach(Site s)
        {
            var current = s.Field.GetValue(null) as Delegate;
            s.Field.SetValue(null, Delegate.Combine(current, s.Handler));
        }

        private static ShotResult Vur(ZombieEnemy z, BodyRegion bolge)
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
            return HitscanEngine.Fire(in istek);
        }

        private static void Window(List<ZombieEnemy> zombiler, Vector3 merkez)
        {
            for (int i = 0; i < 300; i++)
            {
                Vur(zombiler[i % 4], (BodyRegion)(i % 3));
            }

            for (int i = 0; i < 30; i++)
            {
                HitscanEngine.Explode(merkez + Vector3.up * 0.9f, 1f, 3f);
            }
        }

        private static long Measure(List<ZombieEnemy> zombiler, Vector3 merkez, int rounds)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long m0 = Profiler.GetMonoUsedSizeLong();
            long g0 = GC.GetTotalMemory(false);
            for (int r = 0; r < rounds; r++)
            {
                Window(zombiler, merkez);
            }

            long dm = Profiler.GetMonoUsedSizeLong() - m0;
            long dg = GC.GetTotalMemory(false) - g0;
            return Math.Max(dm, dg);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator VurusYolu_AyirmaSiteleri_AboneBazinda()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            Vector3 merkez = _s.Zemin(0f, 8f);
            var zombiler = new List<ZombieEnemy>();
            for (int i = 0; i < 4; i++)
            {
                zombiler.Add(_s.Dogur(merkez + new Vector3((i - 1.5f) * 0.9f, 0f, 0f)));
            }

            yield return null;
            foreach (ZombieEnemy z in zombiler)
            {
                z.FitArmorComposition(true, true, true);
                z.Armor.Equip(ArmorSlot.Helmet, 100000f);
                z.Armor.Equip(ArmorSlot.Body, 100000f);
                z.Armor.Equip(ArmorSlot.Leg, 100000f);
            }

            // Warm-up (caches, capacities, first-time lazies).
            for (int i = 0; i < 40; i++)
            {
                Vur(zombiler[i % 4], BodyRegion.UpperBody);
                HitscanEngine.Explode(merkez + Vector3.up * 0.9f, 1f, 3f);
            }

            yield return null;

            // Positive control: 300 x 80 B must be visible.
            GC.Collect();
            long c0 = Profiler.GetMonoUsedSizeLong();
            var yem = new object[300];
            for (int i = 0; i < yem.Length; i++) yem[i] = new byte[80];
            long control = Profiler.GetMonoUsedSizeLong() - c0;
            Debug.Log("[DD-AUDIT] control(300x80B) mono=" + control + " B keep=" + yem.Length);
            Assert.Greater(control, 0, "Sayaç küçük ayırmayı görmüyor — denetim geçersiz.");

            List<Site> sites = CollectSites();
            var sb = new System.Text.StringBuilder();
            sb.Append("[DD-AUDIT] subscribers=").Append(sites.Count).Append(": ");
            foreach (Site s in sites) sb.Append(s.Name).Append("; ");
            Debug.Log(sb.ToString());

            long baseline = Measure(zombiler, merkez, Rounds);
            long baseline2 = Measure(zombiler, merkez, Rounds);
            Debug.Log("[DD-AUDIT] baseline windows=" + Rounds + " growth=" + baseline + " B (repeat " + baseline2 + " B)");
            yield return null;

            // Each subscriber detached alone.
            foreach (Site s in sites)
            {
                Detach(s);
                long without = Measure(zombiler, merkez, Rounds);
                Attach(s);
                Debug.Log("[DD-AUDIT] site=" + s.Name + " withoutSite=" + without + " B (baseline " + baseline + ") saved=" + (baseline - without));
                yield return null;
            }

            // All detached: what the raise path + non-subscriber consumers (VFX/feedback director, tracers, HUD polling) allocate.
            foreach (Site s in sites) Detach(s);
            long none = Measure(zombiler, merkez, Rounds);
            foreach (Site s in sites) Attach(s);
            Debug.Log("[DD-AUDIT] allSubscribersDetached growth=" + none + " B (baseline " + baseline + ")");

            // Feedback director off (the raise side of the VFX): what remains is the damage core itself.
            HitFeedbackDirector fd = HitFeedbackDirector.Instance;
            if (fd != null)
            {
                foreach (Site s in sites) Detach(s);
                fd.enabled = false;
                long coreOnly = Measure(zombiler, merkez, Rounds);
                fd.enabled = true;
                foreach (Site s in sites) Attach(s);
                Debug.Log("[DD-AUDIT] feedbackDirectorDisabled+noSubscribers growth=" + coreOnly + " B");
            }

            Assert.GreaterOrEqual(sites.Count, 1, "Hiç üretim abonesi bulunamadı — denetim anlamsız.");
        }

        /// <summary>
        /// Exact per-frame managed allocation (bytes and count) from the Unity profiler
        /// counters — sees every allocation, including pool / list growth the heap-growth
        /// counters hide behind block granularity. Warm-up mirrors the WP-006 test (20
        /// pairs), then: frame 1 = first measured window (lazy growth shows here), frame 2 =
        /// steady state. Repeated with each subscriber detached (both frames) so the owner
        /// of a lazy site and of a per-hit site are both attributable.
        /// </summary>
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator VurusYolu_KareBasiAyirma_SayacAboneBazinda()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            Vector3 merkez = _s.Zemin(0f, 8f);
            var zombiler = new List<ZombieEnemy>();
            for (int i = 0; i < 4; i++)
            {
                zombiler.Add(_s.Dogur(merkez + new Vector3((i - 1.5f) * 0.9f, 0f, 0f)));
            }

            yield return null;
            foreach (ZombieEnemy z in zombiler)
            {
                z.FitArmorComposition(true, true, true);
                z.Armor.Equip(ArmorSlot.Helmet, 100000f);
                z.Armor.Equip(ArmorSlot.Body, 100000f);
                z.Armor.Equip(ArmorSlot.Leg, 100000f);
            }

            List<Site> sites = CollectSites();
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null;
                yield return null;
                Assert.IsTrue(bytes.Valid, "'GC Allocated In Frame' sayacı yok (Editor/dev build gerekir).");

                // Idle frame reference (test runner + scene overhead with no hits).
                yield return null;
                long idleBytes = bytes.LastValue, idleCount = count.LastValue;
                Debug.Log("[DD-AUDIT2] idleFrame bytes=" + idleBytes + " count=" + idleCount);

                // Positive control in a frame.
                var yem = new object[300];
                for (int i = 0; i < yem.Length; i++) yem[i] = new byte[80];
                yield return null;
                Debug.Log("[DD-AUDIT2] controlFrame(300x80B) bytes=" + bytes.LastValue + " count=" + count.LastValue + " keep=" + yem.Length);
                Assert.Greater(count.LastValue, 250, "Sayaç kontrol ayırmalarını görmüyor.");

                // Baseline: 20-pair warm-up (as WP-006), then two measured frames.
                for (int i = 0; i < 20; i++)
                {
                    Vur(zombiler[i % 4], BodyRegion.UpperBody);
                    HitscanEngine.Explode(merkez + Vector3.up * 0.9f, 1f, 3f);
                }

                yield return null;
                Window(zombiler, merkez);
                yield return null;
                long b1 = bytes.LastValue, c1 = count.LastValue;
                Window(zombiler, merkez);
                yield return null;
                long b2 = bytes.LastValue, c2 = count.LastValue;
                Debug.Log("[DD-AUDIT2] baseline frame1 bytes=" + b1 + " count=" + c1 + " | frame2 bytes=" + b2 + " count=" + c2 + " (idle " + idleBytes + "/" + idleCount + ")");

                // Per-subscriber: detach, one window frame (steady state now), reattach.
                foreach (Site s in sites)
                {
                    Detach(s);
                    Window(zombiler, merkez);
                    yield return null;
                    long wb = bytes.LastValue, wc = count.LastValue;
                    Attach(s);
                    Debug.Log("[DD-AUDIT2] site=" + s.Name + " frameWithout bytes=" + wb + " count=" + wc + " (steady baseline " + b2 + "/" + c2 + ")");
                }

                foreach (Site s in sites) Detach(s);
                Window(zombiler, merkez);
                yield return null;
                Debug.Log("[DD-AUDIT2] allDetached bytes=" + bytes.LastValue + " count=" + count.LastValue);
                foreach (Site s in sites) Attach(s);

                HitFeedbackDirector fd = HitFeedbackDirector.Instance;
                if (fd != null)
                {
                    foreach (Site s in sites) Detach(s);
                    fd.enabled = false;
                    Window(zombiler, merkez);
                    yield return null;
                    Debug.Log("[DD-AUDIT2] allDetached+feedbackOff bytes=" + bytes.LastValue + " count=" + count.LastValue);
                    fd.enabled = true;
                    foreach (Site s in sites) Attach(s);
                }

                // Bisect the raise side: what allocates per hit regardless of subscribers.
                if (fd != null)
                {
                    bool numbers = fd.ShowDamageNumbers;
                    fd.ShowDamageNumbers = false;
                    Window(zombiler, merkez);
                    yield return null;
                    Debug.Log("[DD-AUDIT2] damageNumbersOff bytes=" + bytes.LastValue + " count=" + count.LastValue);
                    fd.ShowDamageNumbers = numbers;

                    bool markers = fd.ShowHitMarkers;
                    fd.ShowHitMarkers = false;
                    Window(zombiler, merkez);
                    yield return null;
                    Debug.Log("[DD-AUDIT2] hitMarkersOff bytes=" + bytes.LastValue + " count=" + count.LastValue);
                    fd.ShowHitMarkers = markers;

                    fd.ShowDamageNumbers = false;
                    fd.ShowHitMarkers = false;
                    Window(zombiler, merkez);
                    yield return null;
                    Debug.Log("[DD-AUDIT2] numbersAndMarkersOff bytes=" + bytes.LastValue + " count=" + count.LastValue);
                    fd.ShowDamageNumbers = numbers;
                    fd.ShowHitMarkers = markers;
                }

                for (int i = 0; i < 300; i++) Vur(zombiler[i % 4], (BodyRegion)(i % 3));
                yield return null;
                Debug.Log("[DD-AUDIT2] hitsOnly(300) bytes=" + bytes.LastValue + " count=" + count.LastValue);

                for (int i = 0; i < 30; i++) HitscanEngine.Explode(merkez + Vector3.up * 0.9f, 1f, 3f);
                yield return null;
                Debug.Log("[DD-AUDIT2] explosionsOnly(30) bytes=" + bytes.LastValue + " count=" + count.LastValue);

                // Misses: same ray count, no target → physics + tracer-less request path only.
                for (int i = 0; i < 300; i++)
                {
                    var miss = new ShotRequest
                    {
                        Origin = merkez + Vector3.up * 30f, Direction = Vector3.up, Damage = 1f, Range = 6f,
                        ImpactForce = DamageForce.RifleBullet, Source = DamageSourceKind.Bullet, Power = WeaponPower.Medium,
                        PierceDamageFalloff = 1f, DrawTracer = false
                    };
                    HitscanEngine.Fire(in miss);
                }

                yield return null;
                Debug.Log("[DD-AUDIT2] missesOnly(300) bytes=" + bytes.LastValue + " count=" + count.LastValue);

                // One hit per region to see the per-hit cost at low count (merging / budget effects).
                for (int i = 0; i < 12; i++) Vur(zombiler[i % 4], (BodyRegion)(i % 3));
                yield return null;
                Debug.Log("[DD-AUDIT2] hits(12) bytes=" + bytes.LastValue + " count=" + count.LastValue);

                // Idle again: what the frame costs with nothing happening (HUD / directors polling).
                yield return null;
                yield return null;
                Debug.Log("[DD-AUDIT2] idleFrameAfter bytes=" + bytes.LastValue + " count=" + count.LastValue);
            }
        }
    }
}
