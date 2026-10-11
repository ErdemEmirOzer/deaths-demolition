using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Difficulty;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-104 B3 on real bodies (S-03 receivers of the run tree's statuses, S-04 12.10 push model):
    ///   (a) ice stacks slow, the cap freezes — no walk, no swing, the Animator held (speed 0); thawed, speed 1 and the gait's
    ///       cadence back where it was; Elite half the freeze, the Ultra slows instead;
    ///   (b) a black hole pulling twenty bodies for 2.5 s never takes one off the NavMesh (and the watchdogs remove none);
    ///   (c) death clears every status (FrozenAtDeath latched, tint event, TreeStatusEnded once per life) and the body the pool
    ///       hands out again is clean;
    ///   (d) WP-102: a held / pulled pursuit body keeps its role (the stall clock waits);
    ///   (e) neutral tree: the receivers read exactly 1 / zero and change no damage;
    ///   S5: a status tick passes armour and the flag never leaks into the next blast (MUST).
    /// (b) also logs the well's jitter (radial sign flips) and the crowd solver's contacts (S03-RECEIVER §5.3).
    /// </summary>
    [Category("WP104")]
    public sealed class TreeStatusSliceTests
    {
        private PlayerRig _rig;
        private int _olum;
        private ZombieEnemy _izlenen;
        private int _bitti, _tintOlay;
        private TreeElement _sonTint;

        private void OnDied(EnemyDeathInfo _) => _olum++;
        private void OnEnded(ZombieEnemy z) { if (z == _izlenen) _bitti++; }
        private void OnTint(ZombieEnemy z, TreeElement e, float k) { if (z == _izlenen) { _tintOlay++; _sonTint = e; } }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        private IEnumerator Hazirla()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null, 30f, "Sahne kurulmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, 30f, "Koşu Fighting fazına girmedi.");
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            DamageEvents.EnemyDied += OnDied;
            ZombieEnemy.TreeStatusEnded += OnEnded;
            ZombieEnemy.StatusTintChanged += OnTint;
            _izlenen = null; _bitti = 0; _tintOlay = 0; _sonTint = TreeElement.None;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            DamageEvents.EnemyDied -= OnDied;
            ZombieEnemy.TreeStatusEnded -= OnEnded;
            ZombieEnemy.StatusTintChanged -= OnTint;
            TreeRuntime.ClearTestStats();
            TreeRuntime.Reset();
            ZombieEnemy.ResetRisePolicy();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        /// <summary>Deep Freeze + Black Hole + the stuns, at the GD-TREE §2 numbers (S-04 resolves the same from run-tree-v2.json).</summary>
        private static TreeStats Agac()
        {
            TreeStats s = TreeStats.Neutral;
            s.Elements = TreeElement.Ice;
            s.IceSlowPerStack = 0.10f; s.IceMaxStacks = 4; s.IceStacksPerHit = 1; s.IceSeconds = 2.5f;
            s.FreezeSeconds = 1.5f; s.FreezeCooldown = 4f; s.FreezeVulnerability = 1.25f;
            s.FreezeEliteShare = 0.5f; s.FreezeUltraSlow = 0.5f;
            s.StunHeavyShare = 0.5f; s.NukeStunSeconds = 1f; s.BellStunSeconds = 2f; s.BulwarkStunSeconds = 1f;
            s.Core = TreeChaosCore.Hole; s.HoleRadius = 3f; s.HoleSeconds = 2.5f;
            s.HolePullSpeed = 5f; s.HolePullSpecialShare = 0.5f; s.HolePullHeavyShare = 0.2f;
            return s;
        }

        private Vector3 Ileri => Vector3.ProjectOnPlane(_rig.transform.forward, Vector3.up).normalized;

        private ZombieEnemy Dogur(EnemyTier tier, Vector3 konum)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, 1, NavPathFollower.ClampToNavMesh(konum, 6f), Quaternion.identity);
            Assert.IsNotNull(z, "spawn (havuz)");
            return z;
        }

        private static float Duz(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        private static bool Surulen(ZombieEnemy z) => z.Skinned != null && z.Skinned.Authored;

        // ------------------------------------------------------------------------------------------------ (a)

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Donma_AnimatorDurur_CozulunceKadansDoner()
        {
            yield return Hazirla();
            TreeStats agac = Agac();
            agac.FreezeCooldown = 30f;   // the §4.4 check at the end must still sit inside the cooldown after the walk / cadence wait
            TreeRuntime.SetTestStats(agac);

            ZombieEnemy z = Dogur(EnemyTier.Standard, _rig.transform.position + Ileri * 14f);
            yield return Bekle(() => z.RealGroundSpeed > 1f, 5f, "zombi yürümeye başlamadı");
            yield return new WaitForSeconds(0.5f);
            bool surulen = Surulen(z);
            float oran0 = surulen ? z.Skinned.GaitRate : 0f;

            Assert.AreEqual(IceResult.Slowed, z.ApplyIce(1));
            Assert.AreEqual(IceResult.Slowed, z.ApplyIce(1));
            Assert.AreEqual(IceResult.Slowed, z.ApplyIce(1));
            Assert.AreEqual(3, z.IceStacks);
            Assert.AreEqual(0.7f, z.TreeSlowFactor, 1e-4f, "3 yığın × %10");
            Assert.AreEqual(IceResult.Frozen, z.ApplyIce(1), "4. yığın dondurur");
            Assert.IsTrue(z.IsFrozen);
            Assert.AreEqual(0, z.IceStacks, "S1: donma yığını sıfırlar");
            Assert.AreEqual(IceResult.None, z.ApplyIce(1), "donmuş gövde yığın almaz");
            Assert.AreEqual(1.25f, z.TreeVulnerabilityScale, 1e-6f, "donmuş gövde +%25 hasar alır");

            yield return null; yield return null;
            Assert.AreEqual(ZombieSpecialVisual.Netted, z.SpecialVisual, "donma görseli (Animator tutulur)");
            if (surulen) Assert.AreEqual(0f, z.Skinned.Animator.speed, "donmuş gövdede Animator durur");
            Vector3 p0 = z.transform.position;
            yield return new WaitForSeconds(1.0f);
            Assert.Less(Duz(z.transform.position, p0), 0.15f, "donmuşken yürümez");
            Assert.AreEqual(ZombieAttackPhase.None, z.AttackPhase, "donmuşken saldırmaz");

            yield return Bekle(() => !z.IsFrozen, 1.5f, "donma 1,5 sn'de bitmedi");
            yield return null; yield return null;
            Assert.AreEqual(ZombieSpecialVisual.None, z.SpecialVisual);
            Assert.AreEqual(1f, z.TreeVulnerabilityScale);
            Assert.AreEqual(1f, z.TreeSlowFactor, "yığınlar donmayla harcandı");
            if (surulen) Assert.AreEqual(1f, z.Skinned.Animator.speed, "çözülünce Animator hızı 1");
            yield return Bekle(() => z.RealGroundSpeed > 1f, 3f, "çözülünce yürümedi");
            yield return new WaitForSeconds(0.8f);
            Debug.Log("[DD-TEST] wp104.b3.freeze gait0=" + oran0.ToString("F2") + " gait1=" + (surulen ? z.Skinned.GaitRate : 0f).ToString("F2")
                      + " honest=" + (surulen && z.Skinned.GaitRateHonest) + " driven=" + surulen);
            if (surulen) Assert.AreEqual(oran0, z.Skinned.GaitRate, 0.15f, "çözülünce kadans eski yerine dönmedi");

            // §4.4: inside the body's FreezeCooldown the cap only slows — Frozen Gravity's ignoreCooldown freezes anyway
            Assert.AreEqual(IceResult.Slowed, z.ApplyIce(4), "bekleme dolmadan bayraksız donmaz");
            Assert.IsFalse(z.IsFrozen);
            Assert.AreEqual(IceResult.Frozen, z.ApplyIce(4, true), "ignoreCooldown beklemeyi atlar");
            Assert.IsTrue(z.IsFrozen);
            EnemyFactory.ClearAll(); yield return null;

            // Elite: half the freeze; Ultra: no freeze, ×0.5 slow
            ZombieEnemy e = Dogur(EnemyTier.Elite, _rig.transform.position + Ileri * 14f);
            yield return null; yield return null;
            Assert.AreEqual(IceResult.Frozen, e.ApplyIce(4));
            float t0 = Time.time;
            yield return Bekle(() => !e.IsFrozen, 2f, "Elite donması bitmedi");
            Assert.That(Time.time - t0, Is.InRange(0.6f, 0.95f), "Elite 0,75 sn donar");

            ZombieEnemy u = Dogur(EnemyTier.Ultra, _rig.transform.position - Ileri * 14f);
            yield return null; yield return null;
            Assert.AreEqual(IceResult.UltraSlowed, u.ApplyIce(4));
            Assert.IsFalse(u.IsFrozen, "Ultra donmaz");
            Assert.AreEqual(0.5f, u.TreeSlowFactor, 1e-4f, "Ultra %50 yavaşlar");
            EnemyFactory.ClearAll(); yield return null;
        }

        // ------------------------------------------------------------------------------------------------ (b)

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Cekim_NavMeshDisinaCikarmaz_KalabalikMerkezeToplanir()
        {
            yield return Hazirla();
            TreeRuntime.SetTestStats(Agac());
            Vector3 merkez = NavPathFollower.ClampToNavMesh(_rig.transform.position + Ileri * 8f, 4f);

            var zler = new List<ZombieEnemy>();
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI * 2f / 20f;
                float r = 1.8f + (i % 3) * 0.5f;
                zler.Add(Dogur(i % 10 == 0 ? EnemyTier.Elite : EnemyTier.Standard, merkez + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r)));
            }

            yield return null; yield return null;
            int cikarilan0 = ZombieEnemy.OutOfWorldRemovals + ZombieEnemy.StuckRemovals;
            float ilk = 0f; int n0 = 0;
            foreach (ZombieEnemy z in zler) if (z.Tier == EnemyTier.Standard) { ilk += Duz(z.transform.position, merkez); n0++; }
            ilk /= Mathf.Max(1, n0);

            float enKotu = 0f, siradaki = 0f, t0 = Time.time;
            int cekilen = 0;
            // S03-RECEIVER §5.3 (measured, logged, not asserted): jitter = a body's radial step (toward / away from the core)
            // changing sign from one frame to the next (steps under 2 mm count as rest); the solver's contacts per frame.
            var sonKonum = new Vector3[zler.Count];
            var sonIsaret = new int[zler.Count];
            for (int i = 0; i < zler.Count; i++) sonKonum[i] = zler[i].transform.position;
            int titreme = 0, kontakTepe = 0;
            long kontakToplam = 0;
            int kare = 0;
            while (Time.time - t0 < 2.5f)
            {
                if (Time.time >= siradaki)   // TreeGravityWells' 10 Hz refresh
                {
                    siradaki = Time.time + 0.1f;
                    foreach (ZombieEnemy z in zler)
                        if (z != null && z.IsAlive && Duz(z.transform.position, merkez) <= 3f) { z.ApplyPull(merkez, 5f, 0.15f); cekilen++; }
                }

                yield return null;
                kare++;
                int kontak = CrowdService.Solver != null ? CrowdService.Solver.LastContactCount : 0;
                kontakToplam += kontak;
                if (kontak > kontakTepe) kontakTepe = kontak;
                for (int i = 0; i < zler.Count; i++)
                {
                    ZombieEnemy z = zler[i];
                    if (z == null || !z.IsAlive) continue;
                    Vector3 p = z.transform.position;
                    float sapma = NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? Duz(hit.position, p) : 99f;
                    if (sapma > enKotu) enKotu = sapma;

                    Vector3 ice = merkez - p; ice.y = 0f;
                    Vector3 adim = p - sonKonum[i]; adim.y = 0f;
                    float radyal = ice.sqrMagnitude > 1e-6f ? Vector3.Dot(adim, ice.normalized) : 0f;
                    int isaret = radyal > 0.002f ? 1 : radyal < -0.002f ? -1 : 0;
                    if (isaret != 0 && sonIsaret[i] != 0 && isaret != sonIsaret[i]) titreme++;
                    if (isaret != 0) sonIsaret[i] = isaret;
                    sonKonum[i] = p;
                }
            }

            float sure = Mathf.Max(0.01f, Time.time - t0);
            Debug.Log("[DD-TEST] wp104.b3.pull.jitter flips=" + titreme + " flipsPerBodySecond=" + (titreme / (zler.Count * sure)).ToString("F2")
                      + " contactsPeak=" + kontakTepe + " contactsMean=" + (kare > 0 ? (float)kontakToplam / kare : 0f).ToString("F1") + " frames=" + kare);

            float son = 0f; int n1 = 0;
            foreach (ZombieEnemy z in zler) if (z != null && z.IsAlive && z.Tier == EnemyTier.Standard) { son += Duz(z.transform.position, merkez); n1++; }
            son /= Mathf.Max(1, n1);
            Debug.Log("[DD-TEST] wp104.b3.pull worstOffMesh=" + enKotu.ToString("F3") + " meanDist " + ilk.ToString("F2") + "→" + son.ToString("F2")
                      + " pulls=" + cekilen + " removed=" + (ZombieEnemy.OutOfWorldRemovals + ZombieEnemy.StuckRemovals - cikarilan0));
            Assert.LessOrEqual(enKotu, 0.1f, "çekilen gövde NavMesh dışına çıktı");
            Assert.AreEqual(cikarilan0, ZombieEnemy.OutOfWorldRemovals + ZombieEnemy.StuckRemovals, "bekçi çekilen gövdeyi kaldırdı");
            Assert.Greater(cekilen, 0);
            Assert.Less(son, ilk, "çekim kalabalığı merkeze toplamadı");
            EnemyFactory.ClearAll(); yield return null;
        }

        // ------------------------------------------------------------------------------------------------ (c)

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Olum_DurumlarTemizlenir_HavuzdanTemizDoner()
        {
            yield return Hazirla();
            TreeRuntime.SetTestStats(Agac());

            ZombieEnemy k = Dogur(EnemyTier.Standard, _rig.transform.position + Ileri * 12f);
            _izlenen = k;
            yield return null; yield return null;
            k.BreakAllArmor();   // a plate eats a hit and discards the surplus — the ticks must reach health
            yield return null;

            // a DoT tick: health falls, no stagger
            float can0 = k.Health;
            DamageResult tik = k.ApplyStatusDamage(5f, TreeElement.Fire);
            Assert.IsFalse(DamageResults.IsEmpty(in tik), "DoT tiki işlemedi");
            Assert.Less(k.Health, can0);
            Assert.IsFalse(k.Animator.IsStaggering, "DoT tiki sendeletmez");
            Assert.AreEqual(TreeElement.Fire, k.LastStatusElement);

            k.SetStatusTint(TreeElement.Ice, 0.8f);
            Assert.AreEqual(1, _tintOlay);
            k.SetStatusTint(TreeElement.Ice, 0.82f);
            Assert.AreEqual(1, _tintOlay, "küçük değişim olay üretmez");

            for (int i = 0; i < 4; i++) k.ApplyIce(1);
            Assert.IsTrue(k.ApplyStun(1f, TreeStunKind.Nuke));
            k.ApplyPull(k.transform.position + Ileri * 2f, 5f, 1f);
            Assert.IsTrue(k.IsFrozen && k.IsStunned && k.IsPulled);
            yield return null;

            _olum = 0;
            DamageResult olum = k.ApplyStatusDamage(99999f, TreeElement.Ice);
            Assert.IsTrue(olum.Killed, "öldürücü tik öldürmedi");
            Assert.AreEqual(1, _olum, "ölüm ve ödül normal Die yolundan");
            Assert.IsTrue(k.FrozenAtDeath, "Shatter için donmuş ölüm kilitlenir");
            Assert.AreEqual(1, _bitti, "TreeStatusEnded ölümde");
            Assert.AreEqual(TreeElement.None, _sonTint, "ölümde renk temizlenir");
            Assert.IsFalse(k.IsFrozen || k.IsStunned || k.IsPulled);

            EnemyFactory.ClearAll(); yield return null;
            Assert.AreEqual(1, _bitti, "ömür başına bir kez");

            // the pool hands the same instance out again: spawn until it comes back
            ZombieEnemy geri = null;
            Vector3 yer = NavPathFollower.ClampToNavMesh(_rig.transform.position + Ileri * 12f, 6f);
            for (int i = 0; i < 40 && geri == null; i++)
            {
                ZombieEnemy y = EnemyFactory.Spawn(EnemyTier.Standard, 1, yer + new Vector3((i % 6) * 0.8f, 0f, (i / 6) * 0.8f), Quaternion.identity);
                if (y == null) break;
                if (y == k) geri = y;
            }

            Assert.IsNotNull(geri, "havuz aynı gövdeyi geri vermedi");
            yield return null; yield return null;
            Assert.IsFalse(geri.IsFrozen); Assert.IsFalse(geri.IsStunned); Assert.IsFalse(geri.IsPulled); Assert.IsFalse(geri.TreeHolds);
            Assert.AreEqual(0, geri.IceStacks);
            Assert.AreEqual(1f, geri.TreeSlowFactor);
            Assert.AreEqual(1f, geri.TreeVulnerabilityScale);
            Assert.AreEqual(Vector3.zero, geri.TreePullVelocity);
            Assert.IsFalse(geri.FrozenAtDeath);
            Assert.AreEqual(TreeElement.None, geri.StatusTintElement);
            Assert.AreEqual(ZombieSpecialVisual.None, geri.SpecialVisual);
            if (Surulen(geri)) Assert.AreEqual(1f, geri.Skinned.Animator.speed, "havuzdan dönen gövde donuk kalmaz");

            // a living body taken by the pool reports its end once
            int b0 = _bitti;
            EnemyFactory.ClearAll(); yield return null;
            Assert.AreEqual(b0 + 1, _bitti, "canlı gövde havuza dönerken TreeStatusEnded");
        }

        // ------------------------------------------------------------------------------------------------ S5 · DoT and armour

        /// <summary>
        /// S5 (S03-RECEIVER §4.3): a status tick passes the regional armour (whole amount to health, no plate touched) — and the
        /// flag does NOT leak (MUST): the next blast without a context and a burning-ground tick (GroundFire alone) still settle
        /// on the plates exactly as before.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator DoT_ZirhiDeler_BayrakSizmaz_SonrakiPatlamaZirhaCarpar()
        {
            yield return Hazirla();
            TreeRuntime.SetTestStats(Agac());
            ZombieEnemy k = Dogur(EnemyTier.Standard, _rig.transform.position + Ileri * 12f);
            yield return null; yield return null;
            Assert.AreEqual(3, k.FitArmorComposition(true, true, true), "üç plaka takılmadı");
            yield return null;

            float can0 = k.Health, zirh0 = Zirh(k);
            DamageResult r = k.ApplyStatusDamage(10f, TreeElement.Fire);
            Assert.AreEqual(can0 - 10f, k.Health, 1e-3f, "DoT zırhı deler: tutarın tamamı sağlığa");
            Assert.AreEqual(zirh0, Zirh(k), "DoT plakaya dokunmaz");
            Assert.IsFalse(r.ArmorBroke);
            Assert.IsFalse(DamageHitContext.IgnoreArmor, "IgnoreArmor bayrağı sızdı");
            Assert.IsFalse(DamageHitContext.GroundFire, "GroundFire bayrağı sızdı");

            // MUST: the next blast (no context) is armour-split again — fully plated, so no health damage
            float can1 = k.Health;
            k.ApplyExplosion(3f, k.transform.position + Ileri);
            Assert.AreEqual(can1, k.Health, "sonraki patlama zırhı deldi (bayrak sızdı)");
            Assert.Less(Zirh(k), zirh0, "sonraki patlama plakalara çarpmadı");

            // a burning-ground tick (GroundFire without IgnoreArmor, the RunTreeGroundFire pattern) stays on the plates as before
            float can2 = k.Health;
            DamageHitContext.Set(1f, 1f, false, default, null);
            DamageHitContext.MarkGroundFire();
            try { k.ApplyExplosion(3f, k.transform.position); }
            finally { DamageHitContext.Clear(); }
            Assert.AreEqual(can2, k.Health, "yangın zemini tiki zırhı deldi (eski davranış değişti)");
            EnemyFactory.ClearAll(); yield return null;
        }

        private static float Zirh(ZombieEnemy z) =>
            z.Armor.Current(ArmorSlot.Helmet) + z.Armor.Current(ArmorSlot.Body) + z.Armor.Current(ArmorSlot.Leg);

        // ------------------------------------------------------------------------------------------------ (d)

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Wp102_TutmaVeCekim_TakipRolunuDusurmez()
        {
            yield return Hazirla();
            TreeRuntime.SetTestStats(Agac());
            DifficultyService.Selected = DifficultyMode.Hard;
            DifficultyService.BeginRun();
            try
            {
                Assert.IsNotNull(DifficultyService.Current, "Hard ayarı etkin değil");
                ZombieEnemy z = Dogur(EnemyTier.Standard, _rig.transform.position + Ileri * 14f);
                z.SetAiRoleForTests(ZombieAiRole.Intercept);
                int dusme0 = ZombieEnemy.PursuitFallbacks;
                yield return new WaitForSeconds(0.5f);   // the goal is read, the progress window runs

                // freeze 1.5 s, then a bell stun before it thaws: ~3.4 s held > PursuitStallSeconds (3 s)
                for (int i = 0; i < 4; i++) z.ApplyIce(1);
                Assert.IsTrue(z.IsFrozen);
                yield return new WaitForSeconds(1.4f);
                Assert.IsTrue(z.ApplyStun(2f, TreeStunKind.Bell));
                yield return Bekle(() => !z.TreeHolds, 3f, "tutma bitmedi");
                yield return new WaitForSeconds(0.5f);
                Assert.AreEqual(ZombieAiRole.Intercept, z.AiRole, "tutulan gövde takip rolünü kaybetti");
                Assert.AreEqual(dusme0, ZombieEnemy.PursuitFallbacks);

                // pulled away from the player for 3.5 s (10 Hz refresh): displaced, never "no progress"
                Vector3 uzak = NavPathFollower.ClampToNavMesh(z.transform.position + Ileri * 3f, 2f);
                float t0 = Time.time, siradaki = 0f;
                while (Time.time - t0 < 3.5f)
                {
                    if (Time.time >= siradaki) { siradaki = Time.time + 0.1f; z.ApplyPull(uzak, 5f, 0.15f); }
                    yield return null;
                }

                yield return new WaitForSeconds(0.5f);
                Debug.Log("[DD-TEST] wp104.b3.wp102 role=" + z.AiRole + " fallbacks=" + (ZombieEnemy.PursuitFallbacks - dusme0));
                Assert.AreEqual(ZombieAiRole.Intercept, z.AiRole, "çekilen gövde takip rolünü kaybetti");
                Assert.AreEqual(dusme0, ZombieEnemy.PursuitFallbacks);
            }
            finally
            {
                DifficultyService.EndRun();
                DifficultyService.Selected = DifficultyMode.Normal;
            }

            EnemyFactory.ClearAll(); yield return null;
        }

        // ------------------------------------------------------------------------------------------------ (e)

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator NotrAgac_AlicilarBirebir_HasarBaytEsit()
        {
            yield return Hazirla();
            TreeRuntime.ClearTestStats();
            TreeRuntime.Reset();   // no tree: TreeStats.Neutral
            int tint0 = _tintOlay;

            var zler = new List<ZombieEnemy>();
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                zler.Add(Dogur(EnemyTier.Standard, _rig.transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 12f));
            }

            // neutral calls (S-04 makes none without the tree; even if it did, nothing moves)
            ZombieEnemy b = zler[1];
            _izlenen = b;
            Assert.AreEqual(IceResult.None, b.ApplyIce(3), "nötr: IceMaxStacks 0");
            b.ApplyPull(b.transform.position, 0f, 1f);
            b.SetStatusTint(TreeElement.None, 0f);
            Assert.AreEqual(tint0, _tintOlay);

            for (int f = 0; f < 240; f++)
            {
                yield return null;
                foreach (ZombieEnemy z in zler)
                {
                    if (z == null || !z.IsAlive) continue;
                    Assert.IsTrue(z.TreeSlowFactor == 1f, "nötr yavaşlatma tam 1f değil");
                    Assert.IsTrue(z.TreeVulnerabilityScale == 1f, "nötr kırılganlık tam 1f değil");
                    Vector3 v = z.TreePullVelocity;
                    Assert.IsTrue(v.x == 0f && v.y == 0f && v.z == 0f, "nötr çekim sıfır değil");
                    Assert.IsFalse(z.TreeHolds || z.IsPulled);
                    Assert.AreEqual(0, z.IceStacks);
                }
            }

            // the same hit on a body the receivers were called on and one they were not: the damage is bit-equal
            ZombieEnemy a = zler[0];
            a.BreakAllArmor(); b.BreakAllArmor();
            yield return null;
            float aOnce = a.Health, bOnce = b.Health;
            a.ApplyHit(Vurus(a)); b.ApplyHit(Vurus(b));
            Assert.AreEqual(BitConverter.SingleToInt32Bits(aOnce - a.Health), BitConverter.SingleToInt32Bits(bOnce - b.Health), "nötr alıcı hasarı değiştirdi");
            EnemyFactory.ClearAll(); yield return null;
        }

        private static DamageHit Vurus(ZombieEnemy z) => new DamageHit { Amount = 10f, Region = BodyRegion.UpperBody,
            Point = z.transform.position + Vector3.up, Direction = z.transform.forward, ImpactForce = 0.25f, Source = DamageSourceKind.Bullet };
    }
}
