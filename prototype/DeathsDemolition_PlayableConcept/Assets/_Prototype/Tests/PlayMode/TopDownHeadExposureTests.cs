using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        [Category("WP069KafaPayi")]
        [UnityTest, Explicit]
        [Category("Diagnostics")]
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

        // ------------------------------------------------------------------ C-00 30.09: the flat line and the neck seam

        private sealed class Dikis
        {
            public int N, TdHitbox, TdKapsul, TdHic, TdIsin, TdIsinKapsul, TdIsinHic, TpHitbox, TpKapsul, TpHic, TpIsin, TpIsinKapsul;
        }

        private static readonly RaycastHit[] s_isabet = new RaycastHit[32];
        private static readonly float[] Yanal = { -0.2f, -0.1f, 0f, 0.1f, 0.2f };

        /// <summary>2 = a hitbox of <paramref name="z"/> on the line, 1 = only its body capsule (Enemy layer), 0 = neither.</summary>
        private static int Sinifla(ZombieEnemy z, Vector3 from, Vector3 dir, float len)
        {
            int n = Physics.RaycastNonAlloc(from, dir, s_isabet, len, ~0, QueryTriggerInteraction.Collide);
            bool hitbox = false, kapsul = false;
            for (int i = 0; i < n; i++)
            {
                Collider col = s_isabet[i].collider;
                if (col == null || col.GetComponentInParent<ZombieEnemy>() != z) continue;
                if (col.gameObject.layer == DD.Prototype.Core.PrototypeLayers.EnemyHitbox) hitbox = true;
                else if (col.gameObject.layer == DD.Prototype.Core.PrototypeLayers.Enemy) kapsul = true;
            }

            return hitbox ? 2 : kapsul ? 1 : 0;
        }

        /// <summary>
        /// C-00 30.09 (S-02 b5p finding: the top-down line at root + ShotRayHeight runs along the Standard's neck seam — UpperBody
        /// top 1.42, Head bottom 1.40): per tier and pose, how often the flat line meets NO hitbox of a body it passes. Every live
        /// body, every frame: the centre line (player → body, pitch 0 at the shot height) and a lateral sweep of five parallel
        /// lines (±0.2 m) — each classed hitbox / capsule only (the "through a visible zombie" case) / nothing. The same for the
        /// TP/FP line (eye 1.6 m → the body's visual centre) for comparison. Standard / Special / Elite / Ultra; idle, walk, run,
        /// attack phases, stagger, gel. Numbers for C-00 (the fix is decided on them); asserts only that the poses were sampled.
        /// </summary>
        [Category("WP069KafaPayi")]
        [UnityTest, Explicit]
        [Category("Diagnostics")]
        [Timeout(420000)]
        public IEnumerator Tepeden_DuzHat_HitboxsuzKareOrani_KademeVePoz()
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
            var bodies = new List<ZombieEnemy>(12);
            EnemyTier[] tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };
            for (int t = 0; t < tiers.Length; t++)
            {
                for (int k = 0; k < 2; k++)
                {
                    float a = (t * 2 + k) * Mathf.PI / 4f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.5f; p.y = c.y;
                    bodies.Add(Dogur(tiers[t], p, c));
                }

                Vector3 w = c + new Vector3(-3f + t * 2f, 0f, 11f); w.y = c.y;
                bodies.Add(Dogur(tiers[t], w, c));
            }

            foreach (ZombieEnemy z in bodies) z.FitArmorComposition(false, false, false);
            WeaponController silah = WeaponRegistry.ActiveController;
            Assert.IsNotNull(silah, "silah yok");
            var gorsel = new Dictionary<ZombieEnemy, SkinnedMeshRenderer[]>();
            var onceki = new Dictionary<ZombieEnemy, Vector3>();
            foreach (ZombieEnemy z in bodies) { gorsel[z] = z.GetComponentsInChildren<SkinnedMeshRenderer>(true); onceki[z] = z.transform.position; }
            var olcu = new SortedDictionary<string, Dikis>();
            int transaction = 910000;
            float t0 = Time.time, nextStagger = 3f;
            bool knocked = false;
            while (Time.time - t0 < 36f)
            {
                float t = Time.time - t0;
                if (t >= nextStagger)
                {
                    foreach (ZombieEnemy z in bodies)
                    {
                        if (z == null || !z.IsAlive || z.AttackPhase != ZombieAttackPhase.None || z.Animator == null || z.Animator.IsStaggering) continue;
                        Vector3 back = z.transform.position - _rig.transform.position; back.y = 0f;
                        z.Animator.BeginStagger(back.sqrMagnitude > 1e-4f ? back.normalized : -z.transform.forward, 1f, 0.6f, z.transform);
                        break;
                    }

                    nextStagger += 3f;
                }

                if (!knocked && t >= 20f)
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
                float dt = Mathf.Max(1e-4f, Time.deltaTime);
                Vector3 player = _rig.transform.position;
                Vector3 tdFrom = player + Vector3.up * silah.ShotRayHeight;
                Vector3 eye = player + Vector3.up * 1.6f;
                foreach (ZombieEnemy z in bodies)
                {
                    if (z == null || !z.IsAlive) continue;
                    Vector3 pos = z.transform.position, prev = onceki[z];
                    onceki[z] = pos;
                    Vector3 moved = pos - prev; moved.y = 0f;
                    string pose = z.Tier + "/" + PozHizla(z, moved.magnitude / dt);
                    if (!olcu.TryGetValue(pose, out Dikis d)) olcu[pose] = d = new Dikis();
                    d.N++;

                    // top-down: the flat line at the shot height, player → body
                    Vector3 flat = pos - player; flat.y = 0f;
                    float len = flat.magnitude + 2f;
                    if (flat.sqrMagnitude < 1e-4f) continue;
                    Vector3 dir = flat.normalized, right = Vector3.Cross(Vector3.up, dir);
                    int k0 = Sinifla(z, tdFrom, dir, len);
                    if (k0 == 2) d.TdHitbox++; else if (k0 == 1) d.TdKapsul++; else d.TdHic++;
                    foreach (float o in Yanal)
                    {
                        int k = Sinifla(z, tdFrom + right * o, dir, len);
                        d.TdIsin++;
                        if (k == 1) d.TdIsinKapsul++; else if (k == 0) d.TdIsinHic++;
                    }

                    // TP/FP: the eye → the body's visual centre (the renderer bounds)
                    Bounds b = default; bool any = false;
                    foreach (SkinnedMeshRenderer r in gorsel[z]) { if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                    Vector3 aim = any ? b.center : pos + Vector3.up;
                    Vector3 tpDir = (aim - eye).normalized; float tpLen = Vector3.Distance(eye, aim) + 2f;
                    Vector3 tpRight = Vector3.Cross(Vector3.up, tpDir).normalized;
                    int k1 = Sinifla(z, eye, tpDir, tpLen);
                    if (k1 == 2) d.TpHitbox++; else if (k1 == 1) d.TpKapsul++; else d.TpHic++;
                    foreach (float o in Yanal)
                    {
                        int k = Sinifla(z, eye + tpRight * o, tpDir, tpLen);
                        d.TpIsin++;
                        if (k == 1) d.TpIsinKapsul++;
                    }
                }
            }

            string Yuzde(int a, int n) => (100f * a / Mathf.Max(1, n)).ToString("0.0");
            var sb = new StringBuilder("[DD-TEST] wp069.dikis shotLine root+" + silah.ShotRayHeight.ToString("0.00") + " m (C-00 30.09) — pose: n | TOP-DOWN centre hitbox/capsule-only/nothing % | sweep ×5 capsule-only / nothing % | TP/FP centre hitbox/capsule-only/nothing % | sweep capsule-only %\n");
            int tdKapsul = 0, tdN = 0;
            foreach (KeyValuePair<string, Dikis> kv in olcu)
            {
                Dikis d = kv.Value;
                tdKapsul += d.TdKapsul; tdN += d.N;
                sb.AppendLine("  " + kv.Key + ": n " + d.N + " | TD " + Yuzde(d.TdHitbox, d.N) + " / " + Yuzde(d.TdKapsul, d.N) + " / " + Yuzde(d.TdHic, d.N) +
                              " | sweep " + Yuzde(d.TdIsinKapsul, d.TdIsin) + " / " + Yuzde(d.TdIsinHic, d.TdIsin) +
                              " | TP " + Yuzde(d.TpHitbox, d.N) + " / " + Yuzde(d.TpKapsul, d.N) + " / " + Yuzde(d.TpHic, d.N) + " | sweep " + Yuzde(d.TpIsinKapsul, d.TpIsin));
            }

            sb.AppendLine("  ALL: top-down centre line capsule-only (through a visible body) " + Yuzde(tdKapsul, tdN) + " % of " + tdN + " body-frames");
            Debug.Log(sb.ToString());
            Assert.IsTrue(olcu.Keys.Any(k => k.StartsWith("Standard/")), "Standard örneklenmedi\n" + sb);
            Assert.IsTrue(olcu.Keys.Any(k => k.Contains("/saldırı")), "saldırı pozu örneklenmedi\n" + sb);
        }

        private string PozHizla(ZombieEnemy z, float speed)
        {
            string p = PoseOf(z);
            if (p != "ayakta/yürüyüş") return p;
            return speed < 0.25f ? "bekleme" : speed < 2.2f ? "yürüyüş" : "koşu";
        }

        private static ZombieEnemy Dogur(EnemyTier tier, Vector3 p, Vector3 c)
        {
            Vector3 d = c - p; d.y = 0f;
            ZombieEnemy z = EnemyFactory.Spawn(tier, 6, p, Quaternion.LookRotation(d.normalized, Vector3.up), SpawnVisibility.Unknown);
            Assert.IsNotNull(z, tier + " spawn");
            return z;
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
