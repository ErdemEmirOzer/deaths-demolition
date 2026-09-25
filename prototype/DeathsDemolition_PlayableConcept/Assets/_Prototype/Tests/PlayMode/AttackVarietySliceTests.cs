using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-076 B1 on real bodies in the Run scene: six Standards on a standing, invulnerable player for 20 s. The crowd
    /// uses more than one style, never three of one style in a row per body, never more than 4 wind-ups at once, never
    /// two grabs at once, and Standards never overhead (weight 0).
    /// </summary>
    [Category("WP076")]
    public sealed class AttackVarietySliceTests
    {
        private const float KurulumZamanAsimi = 30f;
        private readonly List<EnemyCombatEventInfo> _beats = new List<EnemyCombatEventInfo>();
        private void Kaydet(EnemyCombatEventInfo i) { if (i.Kind == EnemyCombatEventKind.TelegraphStart) _beats.Add(i); }
        private PlayerRig _rig;

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

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator AltiStandard_YirmiSaniye_StilCesitliligi_UcTekrarYok_WindupTavani_TekKapma()
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
            ZombieEnemy.ResetClipEventStatics();   // WP-076: AP-022 attack_step / grab_end
            EnemyCombatEvents.Raised += Kaydet;
            yield return null;

            Vector3 c = _rig.transform.position;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3f; p.y = c.y;
                Vector3 d = c - p; d.y = 0f;
                Assert.IsNotNull(EnemyFactory.Spawn(EnemyTier.Standard, 6, p, Quaternion.LookRotation(d.normalized, Vector3.up), SpawnVisibility.Unknown), "spawn " + i);
            }

            int peakGrab = 0, maxWindup = 0;
            float t0 = Time.time;
            while (Time.time - t0 < 20f)
            {
                yield return null;
                maxWindup = Mathf.Max(maxWindup, AttackSlotBoard.Windups);
                peakGrab = Mathf.Max(peakGrab, GrabState.Active ? 1 : 0);
            }

            var perLife = new Dictionary<int, List<ZombieAttackStyle>>();
            var styles = new HashSet<ZombieAttackStyle>();
            int overhead = 0;
            foreach (EnemyCombatEventInfo b in _beats)
            {
                if (!perLife.TryGetValue(b.LifeId, out List<ZombieAttackStyle> list)) perLife[b.LifeId] = list = new List<ZombieAttackStyle>();
                list.Add(b.Style);
                styles.Add(b.Style);
                if (b.Style == ZombieAttackStyle.Overhead) overhead++;
            }

            int triples = 0;
            foreach (List<ZombieAttackStyle> seq in perLife.Values)
                for (int i = 2; i < seq.Count; i++) if (seq[i] == seq[i - 1] && seq[i] == seq[i - 2]) triples++;

            var counts = new Dictionary<ZombieAttackStyle, int>();
            foreach (EnemyCombatEventInfo b in _beats) counts[b.Style] = counts.TryGetValue(b.Style, out int n) ? n + 1 : 1;
            var sb = new System.Text.StringBuilder("[DD-TEST] wp076.b1 swings=" + _beats.Count + " ");
            foreach (var kv in counts) sb.Append(kv.Key + "=" + kv.Value + " ");
            sb.Append("peakWindups=" + AttackSlotBoard.PeakWindups + " grabs=" + GrabState.StartedCount + " escaped=" + GrabState.EscapedCount + " triples=" + triples);
            Debug.Log(sb.ToString());

            Debug.Log("[DD-OBS] wp076.clipEvents steps=" + ZombieEnemy.ClipSteps + " grabEnds=" + ZombieEnemy.ClipGrabEnds + " grabs=" + GrabState.StartedCount);
            Assert.Greater(_beats.Count, 10, "the crowd swung");
            Assert.Greater(ZombieEnemy.ClipSteps, 0, "AP-022 attack_step reached S-03 (clips in place, S-03 steps the body)");
            Assert.GreaterOrEqual(styles.Count, 2, "more than one style");
            Assert.AreEqual(0, triples, "never three of one style in a row");
            Assert.AreEqual(0, overhead, "Standard never overheads");
            Assert.LessOrEqual(AttackSlotBoard.PeakWindups, AttackSlotBoard.WindupCap, "≤ 4 wind-ups at once");
            Assert.LessOrEqual(peakGrab, 1);
            EnemyFactory.ClearAll();
            yield return null;
            Assert.AreEqual(0, AttackSlotBoard.Windups, "cleared bodies give their slots back");
            Assert.IsFalse(GrabState.Active, "no grab survives the bodies");
        }
    }
}
