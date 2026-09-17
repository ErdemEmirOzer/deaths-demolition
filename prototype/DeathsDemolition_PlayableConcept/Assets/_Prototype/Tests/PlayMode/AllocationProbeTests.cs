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
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-053 · allocation probe (S-02). Frame-counter measurement kept as a permanent observation test (log lines only, no budget asserts): copied per commit during the WP-053 bisect (
    /// commit, never committed there): frame-counter measurement of (a) the idle frame with no
    /// zombies, (b) the idle frame with four settled zombies, (c) one 40-hit window frame, and
    /// (d) the idle-4 frame with every Behaviour TYPE on the zombies disabled in turn — the
    /// saving per type names the per-zombie culprit. Uses only APIs that exist since WP-007.
    /// </summary>
    [TestFixture]
    [Category("WP053")]
    public sealed class AllocationProbeTests
    {
        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        private static IEnumerator Sample(ProfilerRecorder bytes, ProfilerRecorder count, int frames, Action<long, long> result)
        {
            long b = 0, c = 0;
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                b += bytes.LastValue;
                c += count.LastValue;
            }

            result(b / frames, c / frames);
        }

        private static void Vur(ZombieEnemy z, BodyRegion bolge)
        {
            Collider hitbox = z.HitboxFor(bolge);
            if (hitbox == null) return;
            Vector3 hedef = hitbox.bounds.center;
            Vector3 geri = -z.transform.forward;
            geri.y = 0f;
            geri = geri.sqrMagnitude > 0.0001f ? geri.normalized : Vector3.back;
            Vector3 kaynak = hedef - geri * 2.5f;
            kaynak.y = hedef.y;
            HitscanEngine.Fire(new ShotRequest
            {
                Origin = kaynak, Direction = (hedef - kaynak).normalized, Damage = 1f, Range = 6f,
                ImpactForce = DamageForce.RifleBullet, Source = DamageSourceKind.Bullet, Power = WeaponPower.Medium,
                PierceDamageFalloff = 1f, DrawTracer = true, TracerWidth = 0.03f, TracerLifetime = 0.09f
            });
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AyirmaFirtinasi_BosKare_DortZombi_VurusPenceresi_BilesenBazinda()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            LogAssert.ignoreFailingMessages = true;
            bool profilerWas = UnityEngine.Profiling.Profiler.enabled;
            UnityEngine.Profiling.Profiler.enabled = true;
            var sb = new System.Text.StringBuilder();
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null;
                yield return null;
                Assert.IsTrue(bytes.Valid && count.Valid, "frame counters unavailable");

                // Positive control.
                var yem = new object[300];
                for (int i = 0; i < yem.Length; i++) yem[i] = new byte[80];
                yield return null;
                sb.Append("[DD-BISECT] control count=").Append(count.LastValue).Append(" bytes=").Append(bytes.LastValue).Append('\n');
                GC.KeepAlive(yem);

                // (a) idle, no zombies (1 s settle, 5-frame average).
                yield return new WaitForSeconds(1f);
                long ib = 0, ic = 0;
                yield return Sample(bytes, count, 5, (b, c) => { ib = b; ic = c; });
                sb.Append("[DD-BISECT] idle0 avg count=").Append(ic).Append(" bytes=").Append(ib).Append('\n');

                // (b) four zombies, settled (rise + blend), 5-frame average.
                Vector3 merkez = _s.Zemin(0f, 8f);
                var zombiler = new List<ZombieEnemy>();
                for (int i = 0; i < 4; i++) zombiler.Add(_s.Dogur(merkez + new Vector3((i - 1.5f) * 0.9f, 0f, 0f)));
                yield return new WaitForSeconds(2.5f);
                long zb = 0, zc = 0;
                yield return Sample(bytes, count, 5, (b, c) => { zb = b; zc = c; });
                sb.Append("[DD-BISECT] idle4 avg count=").Append(zc).Append(" bytes=").Append(zb).Append('\n');

                // (c) a 40-hit window in one frame (warm-up window first), then the settled idle again.
                for (int i = 0; i < 40; i++) Vur(zombiler[i % 4], (BodyRegion)(i % 3));
                yield return null;
                for (int i = 0; i < 40; i++) Vur(zombiler[i % 4], (BodyRegion)(i % 3));
                yield return null;
                sb.Append("[DD-BISECT] hits40 frame count=").Append(count.LastValue).Append(" bytes=").Append(bytes.LastValue).Append('\n');
                yield return new WaitForSeconds(1f);
                yield return Sample(bytes, count, 5, (b, c) => { zb = b; zc = c; });
                sb.Append("[DD-BISECT] idle4-after-hits avg count=").Append(zc).Append(" bytes=").Append(zb).Append('\n');

                // (d) per-Behaviour-type on the zombies: disable every instance of one type, 5 frames, re-enable.
                var byType = new Dictionary<Type, List<Behaviour>>();
                foreach (ZombieEnemy z in zombiler)
                {
                    foreach (Behaviour b in z.GetComponentsInChildren<Behaviour>(true))
                    {
                        if (b == null) continue;
                        Type t = b.GetType();
                        if (!byType.TryGetValue(t, out List<Behaviour> list)) byType[t] = list = new List<Behaviour>();
                        list.Add(b);
                    }
                }

                sb.Append("[DD-BISECT] zombie behaviour types=").Append(byType.Count).Append('\n');
                foreach (KeyValuePair<Type, List<Behaviour>> kv in byType)
                {
                    var wasEnabled = new bool[kv.Value.Count];
                    for (int i = 0; i < kv.Value.Count; i++) { wasEnabled[i] = kv.Value[i].enabled; kv.Value[i].enabled = false; }
                    yield return null;
                    long tb = 0, tc = 0;
                    yield return Sample(bytes, count, 5, (b, c) => { tb = b; tc = c; });
                    for (int i = 0; i < kv.Value.Count; i++) if (kv.Value[i] != null) kv.Value[i].enabled = wasEnabled[i];
                    yield return null;
                    sb.Append("[DD-BISECT] without ").Append(kv.Key.Name).Append(" x").Append(kv.Value.Count)
                      .Append(" count=").Append(tc).Append(" bytes=").Append(tb).Append(" saved=").Append(zc - tc).Append('\n');
                }

                // (e) scene-wide: every other Behaviour type NOT on a zombie or the player rig, disabled in turn (top 12 by instance count).
                var sceneTypes = new Dictionary<Type, List<Behaviour>>();
                foreach (Behaviour b in UnityEngine.Object.FindObjectsOfType<Behaviour>())
                {
                    if (b == null || b.GetComponentInParent<ZombieEnemy>() != null || b.transform.root == _s.Rig.transform.root) continue;
                    Type t = b.GetType();
                    if (t == typeof(Camera) || t.Name.Contains("Test") || t.Name.Contains("Runner")) continue;
                    if (!sceneTypes.TryGetValue(t, out List<Behaviour> list)) sceneTypes[t] = list = new List<Behaviour>();
                    list.Add(b);
                }

                sb.Append("[DD-BISECT] scene behaviour types=").Append(sceneTypes.Count).Append('\n');
                foreach (KeyValuePair<Type, List<Behaviour>> kv in sceneTypes)
                {
                    var wasEnabled = new bool[kv.Value.Count];
                    for (int i = 0; i < kv.Value.Count; i++) { wasEnabled[i] = kv.Value[i].enabled; kv.Value[i].enabled = false; }
                    yield return null;
                    long tb = 0, tc = 0;
                    yield return Sample(bytes, count, 3, (b, c) => { tb = b; tc = c; });
                    for (int i = 0; i < kv.Value.Count; i++) if (kv.Value[i] != null) kv.Value[i].enabled = wasEnabled[i];
                    yield return null;
                    if (zc - tc > 50 || tc > zc + 50)
                        sb.Append("[DD-BISECT] scene without ").Append(kv.Key.FullName).Append(" x").Append(kv.Value.Count)
                          .Append(" count=").Append(tc).Append(" bytes=").Append(tb).Append(" saved=").Append(zc - tc).Append('\n');
                }
            }

            Debug.Log(sb.ToString());
            sb.Length = 0;

            // (f) HEAVY window like the WP-037 audit (armoured targets, 300 hits + 30 explosions, twice), then the
            // frames AFTER it: persistent effects / numbers / telemetry that keep allocating once combat happened.
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                Vector3 merkez = _s.Zemin(0f, 8f);
                var zombiler = new List<ZombieEnemy>();
                foreach (ZombieEnemy z in UnityEngine.Object.FindObjectsOfType<ZombieEnemy>()) if (z.IsAlive) zombiler.Add(z);
                while (zombiler.Count < 4) zombiler.Add(_s.Dogur(merkez + new Vector3(zombiler.Count * 0.9f - 1.5f, 0f, 0f)));
                yield return new WaitForSeconds(2f);
                foreach (ZombieEnemy z in zombiler)
                {
                    z.FitArmorComposition(true, true, true);
                    z.Armor.Equip(ArmorSlot.Helmet, 100000f);
                    z.Armor.Equip(ArmorSlot.Body, 100000f);
                    z.Armor.Equip(ArmorSlot.Leg, 100000f);
                }

                yield return new WaitForSeconds(1f);
                for (int w = 0; w < 2; w++)
                {
                    for (int i = 0; i < 300; i++) Vur(zombiler[i % 4], (BodyRegion)(i % 3));
                    for (int i = 0; i < 30; i++) HitscanEngine.Explode(merkez + Vector3.up * 0.9f, 1f, 3f);
                    yield return null;
                    sb.Append("[DD-BISECT] heavyWindow").Append(w).Append(" frame count=").Append(count.LastValue).Append(" bytes=").Append(bytes.LastValue).Append('\n');
                }

                for (int k = 0; k < 6; k++)
                {
                    yield return new WaitForSeconds(0.5f);
                    long hb = 0, hc = 0;
                    yield return Sample(bytes, count, 3, (b, c) => { hb = b; hc = c; });
                    sb.Append("[DD-BISECT] afterHeavy t+").Append((k + 1) * 0.5f).Append("s avg count=").Append(hc).Append(" bytes=").Append(hb).Append('\n');
                }

                HitFeedbackDirector fd = HitFeedbackDirector.Instance;
                if (fd != null)
                {
                    bool n0 = fd.ShowDamageNumbers, m0 = fd.ShowHitMarkers;
                    fd.ShowDamageNumbers = false;
                    yield return null;
                    long xb = 0, xc = 0;
                    yield return Sample(bytes, count, 3, (b, c) => { xb = b; xc = c; });
                    sb.Append("[DD-BISECT] afterHeavy numbersOff avg count=").Append(xc).Append(" bytes=").Append(xb).Append('\n');
                    fd.ShowHitMarkers = false;
                    yield return null;
                    yield return Sample(bytes, count, 3, (b, c) => { xb = b; xc = c; });
                    sb.Append("[DD-BISECT] afterHeavy numbers+markersOff avg count=").Append(xc).Append(" bytes=").Append(xb).Append('\n');
                    fd.ShowDamageNumbers = n0; fd.ShowHitMarkers = m0;
                }

                // Scene-wide type bisect against the after-heavy idle (top offenders only).
                long refB = 0, refC = 0;
                yield return Sample(bytes, count, 3, (b, c) => { refB = b; refC = c; });
                var sceneTypes = new Dictionary<Type, List<Behaviour>>();
                foreach (Behaviour b in UnityEngine.Object.FindObjectsOfType<Behaviour>())
                {
                    if (b == null) continue;
                    Type t = b.GetType();
                    if (t == typeof(Camera) || t.Name.Contains("Test") || t.Name.Contains("Runner")) continue;
                    if (!sceneTypes.TryGetValue(t, out List<Behaviour> list)) sceneTypes[t] = list = new List<Behaviour>();
                    list.Add(b);
                }

                sb.Append("[DD-BISECT] afterHeavy ref count=").Append(refC).Append(" bytes=").Append(refB).Append(" sceneTypes=").Append(sceneTypes.Count).Append('\n');
                foreach (KeyValuePair<Type, List<Behaviour>> kv in sceneTypes)
                {
                    var wasEnabled = new bool[kv.Value.Count];
                    for (int i = 0; i < kv.Value.Count; i++) { if (kv.Value[i] == null) continue; wasEnabled[i] = kv.Value[i].enabled; kv.Value[i].enabled = false; }
                    yield return null;
                    long tb = 0, tc = 0;
                    yield return Sample(bytes, count, 2, (b, c) => { tb = b; tc = c; });
                    for (int i = 0; i < kv.Value.Count; i++) if (kv.Value[i] != null) kv.Value[i].enabled = wasEnabled[i];
                    yield return null;
                    if (Math.Abs(refC - tc) > 40)
                        sb.Append("[DD-BISECT] afterHeavy without ").Append(kv.Key.FullName).Append(" x").Append(kv.Value.Count)
                          .Append(" count=").Append(tc).Append(" bytes=").Append(tb).Append(" saved=").Append(refC - tc).Append('\n');
                }

                // Every ParticleSystem / TextMesh / Renderer alive right now (persistent effect population).
                sb.Append("[DD-BISECT] alive particleSystems=").Append(UnityEngine.Object.FindObjectsOfType<ParticleSystem>().Length)
                  .Append(" textMeshes=").Append(UnityEngine.Object.FindObjectsOfType<TextMesh>().Length)
                  .Append(" trailRenderers=").Append(UnityEngine.Object.FindObjectsOfType<TrailRenderer>().Length)
                  .Append(" lineRenderers=").Append(UnityEngine.Object.FindObjectsOfType<LineRenderer>().Length).Append('\n');
            }

            UnityEngine.Profiling.Profiler.enabled = profilerWas;
            Debug.Log(sb.ToString());
        }
    }
}
