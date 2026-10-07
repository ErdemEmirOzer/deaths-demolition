using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using NUnit.Framework;
using UnityEngine;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-096 · AP-033 p1 · the per-spawn tier gait pool (<see cref="ZombieSkinnedVisual.GaitIndexFor(EnemyTier, string, CharacterAnimationSet, int)"/>,
    /// <see cref="ZombieSkinnedVisual.NextGaitPick"/>, <see cref="CharacterAnimatorContract.ZombieGaitPools"/>): the pick is deterministic,
    /// every pool member is reachable, consecutive spawns of a tier differ, a tier only ever walks its own pool, a member the set
    /// lacks is skipped (AP-022-only set → the AP-022 gaits; no set → the AP-009 fallback), the Gait indices 0–8 keep their
    /// numbers and only the limp R child is mirrored. Pure — synthetic sets, no asset import (the shipped set:
    /// ArtAnimationTests.Gait_TierBaglama_AP022_MUSTI1 / Ap033_SetHizlari_DurustOranBir).
    /// </summary>
    [Category("WP096")]
    public sealed class ZombieGaitPoolTests
    {
        private static readonly EnemyTier[] Tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };

        private readonly List<Object> _temp = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            ZombieSkinnedVisual.TierGaits = true;
            ZombieSkinnedVisual.GaitVariety = 1f;
            ZombieSkinnedVisual.ResetGaitPicksForTests();
            foreach (Object o in _temp) if (o != null) Object.DestroyImmediate(o);
            _temp.Clear();
        }

        /// <summary>A set whose table names the given clips (speed data only — the pick reads names).</summary>
        private CharacterAnimationSet Set(IEnumerable<string> names)
        {
            var set = ScriptableObject.CreateInstance<CharacterAnimationSet>();
            _temp.Add(set);
            var table = new List<CharacterAnimationSet.Entry>();
            var seen = new HashSet<string>();
            foreach (string n in names)
            {
                if (!seen.Add(n)) continue;
                table.Add(new CharacterAnimationSet.Entry { name = n, loop = true, seconds = 1f, speedMps = 2f, layer = "base", frames = 31 });
            }

            set.Configure(null, null, 1.4f, null, "WP096 test", table, "test", 6.2f);
            return set;
        }

        private CharacterAnimationSet FullSet() => Set(C.ZombieGaitClips);

        [Test]
        public void Havuz_Deterministik_HerUyeErisilir_TierKimligi()
        {
            CharacterAnimationSet set = FullSet();
            Assert.AreEqual(Tiers.Length, C.ZombieGaitPools.Length, "kademe başına bir havuz");
            foreach (EnemyTier tier in Tiers)
            {
                int[] pool = C.ZombieGaitPools[(int)tier];
                var members = new HashSet<int>(pool);
                var reached = new HashSet<int>();
                int previous = -1;
                for (int k = 0; k < pool.Length * 3; k++)
                {
                    int g = ZombieSkinnedVisual.GaitIndexFor(tier, "A", set, k);
                    Assert.AreEqual(g, ZombieSkinnedVisual.GaitIndexFor(tier, "A", set, k), tier + " pick " + k + " deterministik");
                    Assert.IsTrue(members.Contains(g), tier + " pick " + k + " → Gait " + g + " kademenin havuzunda değil (kimlik)");
                    Assert.AreEqual(g, ZombieSkinnedVisual.GaitIndexFor(tier, "B", set, k), tier + ": fit havuz seçimini değiştirmez");
                    if (pool.Length > 1 && previous >= 0) Assert.AreNotEqual(previous, g, tier + ": ardışık doğuşlar aynı görünüm");
                    previous = g;
                    reached.Add(g);
                }

                Assert.AreEqual(members.Count, reached.Count, tier + ": her havuz üyesi erişilebilir");
                Assert.AreEqual(pool[0], ZombieSkinnedVisual.GaitIndexFor(tier, "A", set), tier + ": 3 argümanlı = birincil (pick 0)");
                Assert.AreEqual(pool[pool.Length - 1], ZombieSkinnedVisual.GaitIndexFor(tier, "A", set, -1), tier + ": negatif pick sarar");
            }

            // the tiers' pools are disjoint (a gait identifies its tier) and the Standard pool gives the five AP-033 SPEC looks
            var owner = new Dictionary<int, int>();
            for (int t = 0; t < C.ZombieGaitPools.Length; t++)
            foreach (int g in C.ZombieGaitPools[t])
            {
                Assert.IsFalse(owner.ContainsKey(g), "Gait " + g + " iki kademenin havuzunda");
                owner[g] = t;
            }

            Assert.AreEqual(5, C.ZombieGaitPools[(int)EnemyTier.Standard].Length, "Standard ≥ 5 görünüm (SPEC §2)");
            Assert.AreEqual(C.GaitWalkLurchAV4, C.ZombieGaitPools[(int)EnemyTier.Standard][0]);
            Assert.AreEqual(C.GaitRunFeralAV4, C.ZombieGaitPools[(int)EnemyTier.Special][0]);
            Assert.AreEqual(C.GaitWalkHeavyV3, C.ZombieGaitPools[(int)EnemyTier.Elite][0]);
            Assert.AreEqual(C.GaitWalkStompAV4, C.ZombieGaitPools[(int)EnemyTier.Ultra][0]);
        }

        [Test]
        public void GaitIndeksleri_EskilerYerinde_YalnizLimpRAyna()
        {
            string[] old = { C.ZombieShambleA, C.ZombieShambleB, C.ZombieShambleC, C.ZombieRun, C.ZombieShambleFastAV3, C.ZombieShambleFastBV3, C.ZombieRunV3, C.ZombieWalkHeavyV3, C.ZombieWalkStompV3 };
            for (int i = 0; i < old.Length; i++) Assert.AreEqual(old[i], C.ZombieGaitClips[i], "Gait " + i + " yeniden numaralandı");
            Assert.AreEqual(14, C.ZombieGaitClips.Length);
            Assert.AreEqual(C.ZombieWalkLurchAV4, C.ZombieGaitClips[C.GaitWalkLurchAV4]);
            Assert.AreEqual(C.ZombieWalkLimpAV4, C.ZombieGaitClips[C.GaitWalkLimpAV4]);
            Assert.AreEqual(C.ZombieWalkLimpAV4, C.ZombieGaitClips[C.GaitWalkLimpRV4], "limp R = aynı klip");
            Assert.AreEqual(C.ZombieRunFeralAV4, C.ZombieGaitClips[C.GaitRunFeralAV4]);
            Assert.AreEqual(C.ZombieWalkStompAV4, C.ZombieGaitClips[C.GaitWalkStompAV4]);
            int mirrored = 0;
            for (int i = 0; i < C.ZombieGaitClips.Length; i++) if (C.IsGaitMirrored(i)) { mirrored++; Assert.AreEqual(C.GaitWalkLimpRV4, i); }
            Assert.AreEqual(1, mirrored, "tek ayna çocuğu");
            Assert.Contains(C.GaitWalkLimpRV4, C.ZombieGaitPools[(int)EnemyTier.Standard], "limp R Standard havuzunda");
            foreach (string clip in new[] { C.ZombieWalkLurchAV4, C.ZombieWalkLimpAV4, C.ZombieRunFeralAV4, C.ZombieWalkStompAV4 })
                Assert.Contains(clip, C.ZombieClips, clip + " ZombieClips listesinde");
        }

        [Test]
        public void Havuz_EksikUyeAtlanir_SetYokAP009()
        {
            // a set without AP-033 (the pre-WP-096 table): the pools fall back to their AP-022 members only
            CharacterAnimationSet ap022 = Set(new[] { C.ZombieShambleA, C.ZombieShambleB, C.ZombieShambleC, C.ZombieRun, C.ZombieShambleFastAV3, C.ZombieShambleFastBV3, C.ZombieRunV3, C.ZombieWalkHeavyV3, C.ZombieWalkStompV3 });
            for (int k = 0; k < 6; k++)
            {
                Assert.AreEqual(k % 2 == 0 ? C.GaitShambleFastAV3 : C.GaitShambleFastBV3, ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Standard, "A", ap022, k), "Standard AP-022 " + k);
                Assert.AreEqual(C.GaitRunV3, ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Special, "A", ap022, k));
                Assert.AreEqual(C.GaitWalkHeavyV3, ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Elite, "A", ap022, k));
                Assert.AreEqual(C.GaitWalkStompV3, ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Ultra, "A", ap022, k));
            }

            // no tier gait at all / no set / the A/B switch off → the AP-009 fallback, fit-bound as before
            CharacterAnimationSet ap009 = Set(new[] { C.ZombieShambleA, C.ZombieShambleB, C.ZombieShambleC, C.ZombieRun });
            Assert.AreEqual(1, ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Standard, "B", ap009, 3));
            Assert.AreEqual(3, ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Special, "A", ap009, 3));
            Assert.AreEqual(2, ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Standard, "C", null, 0));
            Assert.AreEqual(0, ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Ultra, "A", null, 4));
            ZombieSkinnedVisual.TierGaits = false;
            Assert.AreEqual(0, ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Standard, "A", FullSet(), 2), "TierGaits kapalı → AP-009");
        }

        [Test]
        public void SiradakiSecim_KademeBasinaSayac_VaryeteSifirBirincil()
        {
            ZombieSkinnedVisual.ResetGaitPicksForTests();
            ZombieSkinnedVisual.GaitVariety = 1f;
            Assert.AreEqual(0, ZombieSkinnedVisual.NextGaitPick(EnemyTier.Standard));
            Assert.AreEqual(0, ZombieSkinnedVisual.NextGaitPick(EnemyTier.Special), "kademeler ayrı sayaç");
            Assert.AreEqual(1, ZombieSkinnedVisual.NextGaitPick(EnemyTier.Standard));
            Assert.AreEqual(0, ZombieSkinnedVisual.NextGaitPick(EnemyTier.Ultra));
            Assert.AreEqual(2, ZombieSkinnedVisual.NextGaitPick(EnemyTier.Standard), "araya giren kademeler Standard sırasını bozmaz");
            Assert.AreEqual(1, ZombieSkinnedVisual.NextGaitPick(EnemyTier.Special));

            // GaitVariety 0 (exact-rate tests): the primary look, the counter untouched
            ZombieSkinnedVisual.GaitVariety = 0f;
            for (int i = 0; i < 3; i++) Assert.AreEqual(0, ZombieSkinnedVisual.NextGaitPick(EnemyTier.Standard), "varyete 0 → birincil");
            ZombieSkinnedVisual.GaitVariety = 1f;
            Assert.AreEqual(3, ZombieSkinnedVisual.NextGaitPick(EnemyTier.Standard), "varyete 0 sayacı ilerletmedi");

            // interleaved spawns: every Standard pool member within one pool length of Standard spawns, consecutive ones differ
            ZombieSkinnedVisual.ResetGaitPicksForTests();
            CharacterAnimationSet set = FullSet();
            var seen = new HashSet<int>();
            int last = -1;
            int[] pool = C.ZombieGaitPools[(int)EnemyTier.Standard];
            for (int s = 0; s < pool.Length; s++)
            {
                ZombieSkinnedVisual.NextGaitPick(EnemyTier.Special);   // other tiers spawn in between
                ZombieSkinnedVisual.NextGaitPick(EnemyTier.Ultra);
                int g = ZombieSkinnedVisual.GaitIndexFor(EnemyTier.Standard, "A", set, ZombieSkinnedVisual.NextGaitPick(EnemyTier.Standard));
                Assert.AreNotEqual(last, g, "ardışık Standard doğuşları farklı");
                last = g;
                seen.Add(g);
            }

            Assert.AreEqual(pool.Length, seen.Count, "bir havuz boyu doğuşta her üye");
        }

        [Test]
        public void Secim_SicakYol_SifirAyirma()
        {
            CharacterAnimationSet set = FullSet();
            int sink = 0;
            long control = HeapDelta.PositiveControl();
            long seen = HeapDelta.Min(() =>
            {
                for (int i = 0; i < 20000; i++)
                {
                    EnemyTier tier = Tiers[i & 3];
                    sink += ZombieSkinnedVisual.GaitIndexFor(tier, "A", set, ZombieSkinnedVisual.NextGaitPick(tier));
                    sink += C.IsGaitMirrored(i % C.ZombieGaitClips.Length) ? 1 : 0;
                }
            });
            Debug.Log("[DD-TEST] wp096.gaitpool.alloc bytes=" + seen + " control=" + control + " sink=" + sink);
            Assert.GreaterOrEqual(control, HeapDelta.PositiveControlBytes, "ölçüm kör (pozitif kontrol)");
            Assert.Less(seen, 4096, "havuz seçimi ayırma yapıyor");
        }
    }
}
