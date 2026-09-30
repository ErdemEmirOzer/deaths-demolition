using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-069 B5 (C-00 30.09, top-down head decision B) · the measurement that follows B: with the top-down ray flat at
    /// root + ShotRayHeight (1.39 m), a head hit can only come from a POSE that brings the Standard's head sphere down to that
    /// line. Six Standards ring a still, invulnerable player (their own attack styles: swipe / sweep / lunge / bite_grab),
    /// two more walk in from 10 m, some are staggered (REQ-124 pose) and some knocked down by the gel path (Falling / Downed /
    /// GettingUp). Every frame, per pose, the head sphere's bottom against the shot line: the lowest gap and the share of
    /// frames at or below it. Numbers for C-00 and S-04 (the simulator's top-down head share); asserts only that each pose
    /// was sampled. Explicit: run by name.
    /// </summary>
    [Category("WP069KafaPayi")]
    public sealed class TopDownHeadExposureTests
    {
        private const float KurulumZamanAsimi = 30f;
        private readonly Dictionary<int, ZombieAttackStyle> _style = new Dictionary<int, ZombieAttackStyle>();
        private void Kaydet(EnemyCombatEventInfo i) { if (i.Kind == EnemyCombatEventKind.TelegraphStart) _style[i.LifeId] = i.Style; }
        private PlayerRig _rig;

        private sealed class Olcu
        {
            public int N, Alti;
            public float MinGap = float.MaxValue, MinHeadBottom = float.MaxValue;
        }

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        [TearDown]
        public void TearDown()
        {
            EnemyCombatEvents.Raised -= Kaydet;
            ZombieEnemy.ResetRisePolicy();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        [UnityTest, Explicit]
        [Timeout(300000)]
        public IEnumerator Tepeden_PozdanKafaPayi_AtisHattinaGore()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null, KurulumZamanAsimi, "sahne");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting");
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            EnemyCombatEvents.ResetStatics();
            EnemyAttackState.ResetStatics();
            ZombieEnemy.ResetClipEventStatics();
            EnemyCombatEvents.Raised += Kaydet;
            yield return null;

            Vector3 c = _rig.transform.position;
            var bodies = new List<ZombieEnemy>(8);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3f; p.y = c.y;
                bodies.Add(Dogur(p, c));
            }

            for (int i = 0; i < 2; i++)
            {
                Vector3 p = c + new Vector3(i == 0 ? -1.5f : 1.5f, 0f, 10f); p.y = c.y;
                bodies.Add(Dogur(p, c));
            }

            foreach (ZombieEnemy z in bodies) z.FitArmorComposition(false, false, false);
            WeaponController silah = WeaponRegistry.ActiveController;
            Assert.IsNotNull(silah, "silah yok");
            var olcu = new SortedDictionary<string, Olcu>();
            int transaction = 900000;
            float t0 = Time.time, nextStagger = 4f;
            bool knocked = false;
            while (Time.time - t0 < 28f)
            {
                float t = Time.time - t0;
                if (t >= nextStagger)
                {
                    // REQ-124 pose: a body not attacking, staggered as a heavy hit would
                    foreach (ZombieEnemy z in bodies)
                    {
                        if (z == null || !z.IsAlive || z.AttackPhase != ZombieAttackPhase.None || z.Animator == null || z.Animator.IsStaggering) continue;
                        Vector3 back = z.transform.position - _rig.transform.position; back.y = 0f;
                        z.Animator.BeginStagger(back.sqrMagnitude > 1e-4f ? back.normalized : -z.transform.forward, 1f, 0.6f, z.transform);
                        break;
                    }

                    nextStagger += 2.5f;
                }

                if (!knocked && t >= 16f)
                {
                    knocked = true;
                    int n = 0;
                    foreach (ZombieEnemy z in bodies)
                    {
                        if (z == null || !z.IsAlive || n >= 3) continue;
                        if (z.ApplyKnockdown(++transaction, 2.2f)) n++;
                    }
                }

                yield return null;
                Physics.SyncTransforms();
                float line = _rig.transform.position.y + silah.ShotRayHeight;
                foreach (ZombieEnemy z in bodies)
                {
                    if (z == null || !z.IsAlive) continue;
                    Collider head = z.HitboxFor(DD.Prototype.Damage.BodyRegion.Head);
                    if (head == null || !head.enabled) continue;
                    string pose = PoseOf(z);
                    if (!olcu.TryGetValue(pose, out Olcu o)) olcu[pose] = o = new Olcu();
                    float bottom = head.bounds.min.y;
                    o.N++;
                    o.MinGap = Mathf.Min(o.MinGap, bottom - line);
                    o.MinHeadBottom = Mathf.Min(o.MinHeadBottom, bottom - z.transform.position.y);
                    if (bottom <= line) o.Alti++;
                }
            }

            var sb = new StringBuilder("[DD-TEST] wp069.kafapayi shotLine root+" + silah.ShotRayHeight.ToString("0.00") + " m (C-00 B, tepeden düz ışın)\n");
            foreach (KeyValuePair<string, Olcu> kv in olcu)
            {
                Olcu o = kv.Value;
                sb.AppendLine("  " + kv.Key + ": n " + o.N + ", kafa altı − hat en az " + (o.MinGap * 100f).ToString("0.0") + " cm, kafa altı yerden en az " +
                              o.MinHeadBottom.ToString("0.000") + " m, hatta/altında " + (100f * o.Alti / Mathf.Max(1, o.N)).ToString("0.0") + " % (" + o.Alti + " kare)");
            }

            Debug.Log(sb.ToString());
            Assert.IsTrue(olcu.ContainsKey("ayakta/yürüyüş"), "ayakta poz örneklenmedi\n" + sb);
            Assert.IsTrue(olcu.ContainsKey("sendeleme"), "sendeleme örneklenmedi\n" + sb);
        }

        private string PoseOf(ZombieEnemy z)
        {
            GelState gel = z.Gel;
            if (gel == GelState.Falling || gel == GelState.Downed || gel == GelState.GettingUp) return "jel-" + gel;
            if (z.Animator != null && z.Animator.IsStaggering) return "sendeleme";
            ZombieAttackPhase phase = z.AttackPhase;
            if (phase != ZombieAttackPhase.None)
            {
                string style = _style.TryGetValue(z.LifeId, out ZombieAttackStyle s) ? s.ToString() : "?";
                return "saldırı-" + style + "-" + phase;
            }

            return "ayakta/yürüyüş";
        }

        private static ZombieEnemy Dogur(Vector3 p, Vector3 c)
        {
            Vector3 d = c - p; d.y = 0f;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 6, p, Quaternion.LookRotation(d.normalized, Vector3.up), SpawnVisibility.Unknown);
            Assert.IsNotNull(z, "spawn");
            return z;
        }
    }
}
