using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-095 (R3, C-00 07.10) · FeelEvents, the one game-feel event source: the bridge subscribes once, every derived
    /// moment (critical flag, trigger-pull group, multi-kill tiers, low-health hysteresis, last zombie, gold chain) follows
    /// the his sözleşmesi (K/wp-095/HIS-SOZLESMESI.md §4.4 / §4.7 / §4.9), and a throwing subscriber never reaches the producer.
    /// </summary>
    [TestFixture]
    public sealed class Wp095FeelEventsTests
    {
        [SetUp]
        public void SetUp()
        {
            FeelEvents.ResetForTests();
            FeelEvents.TestClock = 10f;
            FeelEvents.TestLastOfRound = 0;
        }

        [TearDown]
        public void TearDown()
        {
            FeelEvents.ResetForTests();
            FeelEvents.Disarm();
        }

        private static EnemyDeathInfo Death(int round = 3, int lifeId = 1) =>
            new EnemyDeathInfo(null, EnemyTier.Standard, new Vector3(1f, 0f, 2f), Vector3.forward, BodyRegion.UpperBody,
                DamageSourceKind.Bullet, false, false, round, 5, 1, 0f, 0f, lifeId);

        private static EnemyDamageInfo Damage(BodyRegion region, DamageSourceKind source, float health, bool armored = false)
        {
            var result = new DamageResult { HealthDamage = health, Region = region, WasArmored = armored };
            return new EnemyDamageInfo(null, EnemyTier.Standard, in result, Vector3.zero, Vector3.forward, source, 1f, 0.5f, false);
        }

        [Test]
        public void Rearm_Twice_OneKillRaisesOneEvent()
        {
            FeelEvents.Rearm();
            FeelEvents.Rearm();
            int kills = 0;
            FeelEvents.EnemyKilled += _ => kills++;

            DamageEvents.RaiseEnemyDied(Death());

            Assert.AreEqual(1, kills, "the bridge must be subscribed exactly once, however often it re-arms");
            Assert.AreEqual(1, FeelEvents.Count(FeelEventKind.EnemyKilled));
        }

        [Test]
        public void Hit_CriticalIsTheDamageRule_AndCarriesTheTriggerPullGroup()
        {
            var hits = new List<FeelHit>();
            FeelEvents.EnemyHit += h => hits.Add(h);

            int previous = FeelEvents.PushHitGroup(77);
            FeelEvents.OnEnemyDamaged(Damage(BodyRegion.Head, DamageSourceKind.Bullet, 30f));
            FeelEvents.PopHitGroup(previous);
            FeelEvents.OnEnemyDamaged(Damage(BodyRegion.Head, DamageSourceKind.Explosion, 30f));
            FeelEvents.OnEnemyDamaged(Damage(BodyRegion.Head, DamageSourceKind.Bullet, 0f, armored: true));
            FeelEvents.OnEnemyDamaged(Damage(BodyRegion.UpperBody, DamageSourceKind.Pellet, 12f));

            Assert.AreEqual(4, hits.Count);
            Assert.IsTrue(hits[0].Critical, "unarmoured head, direct hit, health taken");
            Assert.AreEqual(77, hits[0].GroupId);
            Assert.AreEqual(0, FeelEvents.CurrentHitGroup, "Pop restores the outer group");
            Assert.IsFalse(hits[1].Critical, "an explosion never crits (REQ-089)");
            Assert.IsFalse(hits[2].Critical, "an armoured head takes no health: no crit");
            Assert.IsFalse(hits[3].Critical);
            Assert.AreEqual(0, hits[1].GroupId);
            Assert.AreEqual(12f, hits[3].HealthDamage, 1e-6f);
        }

        [Test]
        public void MultiKill_OneTriggerPull_PromotesT1T2T3_OncePerTier()
        {
            var multi = new List<FeelMultiKill>();
            FeelEvents.MultiKill += m => multi.Add(m);

            int previous = FeelEvents.PushHitGroup(5);
            for (int i = 0; i < 8; i++) FeelEvents.OnEnemyDied(Death(lifeId: i + 1));
            FeelEvents.PopHitGroup(previous);

            Assert.AreEqual(3, multi.Count, "kills 3, 5 and 8 promote; 4, 6, 7 do not repeat");
            Assert.AreEqual(new[] { 1, 2, 3 }, new[] { multi[0].Tier, multi[1].Tier, multi[2].Tier });
            Assert.AreEqual(new[] { 3, 5, 8 }, new[] { multi[0].Count, multi[1].Count, multi[2].Count });
            Assert.IsTrue(multi[0].SameGroup && multi[2].SameGroup);
            Assert.AreEqual(5, multi[0].GroupId);
        }

        [Test]
        public void MultiKill_Chain_FourInTheWindow_ThenTheStreakEnds()
        {
            var multi = new List<FeelMultiKill>();
            var kills = new List<FeelKill>();
            FeelEvents.MultiKill += m => multi.Add(m);
            FeelEvents.EnemyKilled += k => kills.Add(k);

            float[] at = { 10f, 10.1f, 10.2f, 10.3f };
            for (int i = 0; i < at.Length; i++)
            {
                FeelEvents.TestClock = at[i];
                FeelEvents.OnEnemyDied(Death(lifeId: i + 1));
                if (i == 2) Assert.AreEqual(0, multi.Count, "three ungrouped kills are not yet a chain (≥ 4)");
            }

            Assert.AreEqual(1, multi.Count);
            Assert.AreEqual(1, multi[0].Tier);
            Assert.AreEqual(4, multi[0].Count);
            Assert.IsFalse(multi[0].SameGroup);
            Assert.AreEqual(4, kills[3].ChainKills);
            Assert.AreEqual(1, kills[3].GroupKills, "ungrouped");

            FeelEvents.TestClock = 11.5f;   // 1.2 s after the last one: outside the 0.6 s window
            FeelEvents.OnEnemyDied(Death(lifeId: 9));
            Assert.AreEqual(1, multi.Count);
            Assert.AreEqual(1, kills[4].ChainKills);
        }

        [Test]
        public void MultiKill_SustainedStream_EachTriggerPullsOwnTripleStillSpeaks()
        {
            // gt MUST-1 (07.10): kills every 0.1 s keep the chain alive; two separate pulls' triples inside it must each raise
            // their own same-group T1 (the old streak tier only reset after a 0.6 s gap and swallowed them)
            var multi = new List<FeelMultiKill>();
            FeelEvents.MultiKill += m => multi.Add(m);
            float t = 10f;
            int life = 1;
            void Ungrouped(int n) { for (int i = 0; i < n; i++) { FeelEvents.TestClock = t; FeelEvents.OnEnemyDied(Death(lifeId: life++)); t += 0.1f; } }
            void Triple(int group)
            {
                FeelEvents.TestClock = t;
                int previous = FeelEvents.PushHitGroup(group);
                for (int i = 0; i < 3; i++) FeelEvents.OnEnemyDied(Death(lifeId: life++));
                FeelEvents.PopHitGroup(previous);
                t += 0.1f;
            }

            Ungrouped(6);
            Triple(101);
            Ungrouped(5);
            Triple(102);
            Ungrouped(3);

            int own101 = 0, own102 = 0;
            foreach (FeelMultiKill m in multi)
            {
                if (m.SameGroup && m.GroupId == 101 && m.GroupTier == 1) own101++;
                if (m.SameGroup && m.GroupId == 102 && m.GroupTier == 1) own102++;
                Assert.GreaterOrEqual(m.Tier, m.GroupTier);
            }

            Assert.AreEqual(1, own101, "the first pull's triple inside the running chain");
            Assert.AreEqual(1, own102, "the second pull's triple, still inside the chain");
            Assert.IsTrue(multi.Exists(m => !m.SameGroup && m.Tier == 1), "the chain itself raised its T1");
            Assert.LessOrEqual(multi.Count, life - 1, "at most one MultiKill per kill");
        }

        [Test]
        public void MultiKill_OneBlastOfFour_IsOneT1()
        {
            var multi = new List<FeelMultiKill>();
            FeelEvents.MultiKill += m => multi.Add(m);
            int previous = FeelEvents.PushHitGroup(7);
            for (int i = 0; i < 4; i++) FeelEvents.OnEnemyDied(Death(lifeId: i + 1));
            FeelEvents.PopHitGroup(previous);

            Assert.AreEqual(1, multi.Count, "the 4th kill reaches the chain minimum but does not exceed what its own pull raised");
            Assert.AreEqual(1, multi[0].Tier);
            Assert.AreEqual(1, multi[0].GroupTier);
        }

        [Test]
        public void MultiKill_GroupIdleLongerThanTheWindow_StartsOver()
        {
            var kills = new List<FeelKill>();
            FeelEvents.EnemyKilled += k => kills.Add(k);
            int previous = FeelEvents.PushHitGroup(9);
            FeelEvents.TestClock = 10f; FeelEvents.OnEnemyDied(Death(lifeId: 1));
            FeelEvents.TestClock = 10.1f; FeelEvents.OnEnemyDied(Death(lifeId: 2));
            FeelEvents.TestClock = 11f; FeelEvents.OnEnemyDied(Death(lifeId: 3));
            FeelEvents.PopHitGroup(previous);

            Assert.AreEqual(new[] { 1, 2, 1 }, new[] { kills[0].GroupKills, kills[1].GroupKills, kills[2].GroupKills });
        }

        [Test]
        public void LowHealth_EntersAt30_LeavesAt38_DeathLeavesOnce()
        {
            var edges = new List<FeelLowHealth>();
            FeelEvents.LowHealth += e => edges.Add(e);

            FeelEvents.UpdateLowHealth(31f, 100f, false);
            Assert.AreEqual(0, edges.Count);
            FeelEvents.UpdateLowHealth(30f, 100f, false);
            Assert.AreEqual(1, edges.Count);
            Assert.IsTrue(edges[0].Entered);
            FeelEvents.UpdateLowHealth(35f, 100f, false);
            FeelEvents.UpdateLowHealth(37.9f, 100f, false);
            Assert.AreEqual(1, edges.Count, "no flicker inside the band");
            FeelEvents.UpdateLowHealth(38f, 100f, false);
            Assert.AreEqual(2, edges.Count);
            Assert.IsFalse(edges[1].Entered);
            Assert.IsFalse(edges[1].Dead);
            FeelEvents.UpdateLowHealth(20f, 100f, false);
            FeelEvents.UpdateLowHealth(0f, 100f, true);
            FeelEvents.UpdateLowHealth(0f, 100f, true);
            Assert.AreEqual(4, edges.Count, "enter, then death leaves once");
            Assert.IsFalse(edges[3].Entered);
            Assert.IsTrue(edges[3].Dead);
            Assert.IsFalse(FeelEvents.InLowHealth);
        }

        [Test]
        public void LastZombie_FollowsItsKill_InTheSameCall()
        {
            var order = new List<string>();
            FeelKill kill = default;
            FeelLastZombie last = default;
            FeelEvents.EnemyKilled += k => { order.Add("kill"); kill = k; };
            FeelEvents.LastZombie += z => { order.Add("last"); last = z; };

            FeelEvents.OnEnemyDied(Death(round: 4));
            Assert.AreEqual(new[] { "kill" }, order.ToArray(), "not the last one: no LastZombie");

            order.Clear();
            FeelEvents.TestLastOfRound = 1;
            FeelEvents.OnEnemyDied(Death(round: 4, lifeId: 2));
            Assert.AreEqual(new[] { "kill", "last" }, order.ToArray());
            Assert.IsTrue(kill.LastOfRound);
            Assert.AreEqual(4, last.Round);
            Assert.AreEqual(new Vector3(1f, 0f, 2f), last.Position);
            Assert.AreEqual(1, FeelEvents.Count(FeelEventKind.LastZombie));
        }

        [Test]
        public void PlayerDamaged_UsesTheHitDirection_AndFlagsHeavy()
        {
            var hurt = new List<FeelPlayerDamage>();
            FeelEvents.PlayerDamaged += d => hurt.Add(d);

            FeelEvents.OnPlayerDamaged(new PlayerDamageInfo(null, 30f, 30f, 100f, 70f, 100f, new Vector3(5f, 0f, 5f),
                Vector3.right, "zombie", false));
            FeelEvents.OnPlayerDamaged(new PlayerDamageInfo(null, 10f, 10f, 70f, 60f, 100f, new Vector3(5f, 0f, 5f),
                Vector3.left, "zombie", false));

            Assert.AreEqual(Vector3.right, hurt[0].Direction, "HitDirection, not WorldPoint − player (which is zero)");
            Assert.IsTrue(hurt[0].Heavy, "30 % of max ≥ 25 %");
            Assert.AreEqual(0.3f, hurt[0].Fraction, 1e-6f);
            Assert.IsFalse(hurt[1].Heavy);
        }

        [Test]
        public void Gold_ChainIndex_RisesInsideTheWindow_AndRestarts()
        {
            var gold = new List<FeelGold>();
            FeelEvents.GoldCollected += g => gold.Add(g);
            float[] at = { 10f, 10.5f, 10.9f, 11.6f };
            for (int i = 0; i < at.Length; i++)
            {
                FeelEvents.TestClock = at[i];
                FeelEvents.OnCredited(5, 5 * (i + 1), Vector3.zero);
            }

            Assert.AreEqual(new[] { 0, 1, 2, 0 }, new[] { gold[0].ChainIndex, gold[1].ChainIndex, gold[2].ChainIndex, gold[3].ChainIndex });
            Assert.AreEqual(20, gold[3].Balance);
        }

        [Test]
        public void SpecialReload_StartAndEnd_AreMapped_OtherUsageIgnored()
        {
            var started = new List<FeelReload>();
            var ended = new List<FeelReload>();
            FeelEvents.ReloadStarted += r => started.Add(r);
            FeelEvents.ReloadEnded += r => ended.Add(r);

            FeelEvents.OnSpecialUsage(new SpecialUsageEvent(SpecialWeaponId.Namekuji, SpecialUsageKind.ReloadStarted, "fx",
                Vector3.zero, Vector3.forward, 0f, 1.5f, 0));
            FeelEvents.OnSpecialUsage(new SpecialUsageEvent(SpecialWeaponId.Namekuji, SpecialUsageKind.Fired, "fx",
                Vector3.zero, Vector3.forward, 2f, 1f, 0));
            FeelEvents.OnSpecialUsage(new SpecialUsageEvent(SpecialWeaponId.Namekuji, SpecialUsageKind.ReloadEnded, "fx",
                Vector3.zero, Vector3.forward, 0f, 1.5f, 3));

            Assert.AreEqual(1, started.Count);
            Assert.AreEqual(1, ended.Count);
            Assert.IsTrue(started[0].Special && started[0].Empty);
            Assert.AreEqual(SpecialWeaponId.Namekuji, started[0].SpecialId);
            Assert.AreEqual(1.5f, started[0].DurationSeconds, 1e-6f);
            Assert.IsTrue(ended[0].Completed);
        }

        [Test]
        public void SpecialReload_DeactivatedMidReload_RaisesTheCutEdge_Once()
        {
            var ended = new List<FeelReload>();
            FeelEvents.ReloadEnded += r => ended.Add(r);
            var deactivated = new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.Deactivated, "fx", Vector3.zero, Vector3.forward, 0f, 0f, 0);

            FeelEvents.OnSpecialUsage(deactivated);
            Assert.AreEqual(0, ended.Count, "no reload running: nothing to cut");

            FeelEvents.OnSpecialUsage(new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.ReloadStarted, "fx",
                Vector3.zero, Vector3.forward, 0f, 2f, 1));
            FeelEvents.OnSpecialUsage(deactivated);
            FeelEvents.OnSpecialUsage(deactivated);

            Assert.AreEqual(1, ended.Count, "gt SHOULD-2: the time cap / cancel ends the reload with one cut edge");
            Assert.IsFalse(ended[0].Completed);
            Assert.AreEqual(2f, ended[0].DurationSeconds, 1e-6f);
        }

        [Test]
        public void Rounds_FromRunEvents()
        {
            var rounds = new List<string>();
            FeelEvents.RoundStarted += r => rounds.Add("start " + r.Round);
            FeelEvents.RoundEnded += r => rounds.Add("end " + r.Round);

            RunEvents.PublishRoundStarted(3);
            RunEvents.PublishRoundCompleted(3);

            Assert.AreEqual(new[] { "start 3", "end 3" }, rounds.ToArray());
        }

        [Test]
        public void Producers_RaiseAndCount()
        {
            FeelShot shot = default;
            int dry = 0, explosions = 0, reloads = 0;
            FeelEvents.ShotFired += s => shot = s;
            FeelEvents.DryFire += _ => dry++;
            FeelEvents.Explosion += _ => explosions++;
            FeelEvents.ReloadStarted += _ => reloads++;
            FeelEvents.ReloadEnded += _ => reloads++;

            FeelEvents.RaiseShot(new FeelShot(false, ClassicWeaponId.CombatShotgun, default, WeaponFeelId.CombatShotgun,
                Vector3.zero, Vector3.forward, 8, 12, 4, 1f, 0, 6));
            FeelEvents.RaiseDryFire(new FeelDryFire(ClassicWeaponId.CombatShotgun, Vector3.zero));
            FeelEvents.RaiseExplosion(Vector3.zero, 4f, 12, ExplosionSource.Rocket);
            FeelEvents.RaiseReloadStarted(new FeelReload(false, ClassicWeaponId.CombatShotgun, default, 2f, true, false, Vector3.zero));
            FeelEvents.RaiseReloadEnded(new FeelReload(false, ClassicWeaponId.CombatShotgun, default, 2f, false, false, Vector3.zero));

            Assert.AreEqual(8, shot.Pellets, "one event per trigger pull, the pellets in it");
            Assert.IsTrue(shot.LastRound);
            Assert.AreEqual(1, dry);
            Assert.AreEqual(1, explosions);
            Assert.AreEqual(2, reloads);
            Assert.AreEqual(1, FeelEvents.Count(FeelEventKind.ShotFired));
            Assert.AreEqual(1, FeelEvents.Count(FeelEventKind.ReloadEnded));
        }

        [Test]
        public void ThrowingSubscriber_IsLogged_NeverReachesTheProducer()
        {
            FeelEvents.EnemyKilled += _ => throw new InvalidOperationException("wp095-subscriber");
            LogAssert.Expect(LogType.Exception, new Regex("wp095-subscriber"));

            Assert.DoesNotThrow(() => FeelEvents.OnEnemyDied(Death()));
            Assert.AreEqual(1, FeelEvents.Count(FeelEventKind.EnemyKilled));
        }
    }
}
