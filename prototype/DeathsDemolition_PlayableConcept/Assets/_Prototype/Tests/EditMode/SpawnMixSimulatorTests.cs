using System.Collections.Generic;
using System.IO;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-027 · SpawnMixSimulator (ADR-027-1) and AttackWindowParityCheck. The simulator runs the
    /// shipped policies (tier draw with the DEC-022 guarantee, armour odds, garment rules) over
    /// rounds 1-10 × ≥1000 spawns and reports; these tests pin its determinism, its rule
    /// checks and the crowd ceilings on the shipped numbers. The parity check reads the frozen
    /// AP-009 attack-clip fixture and compares the animated hit windows with the timer windows.
    /// </summary>
    [Category("WP027")]
    public sealed class SpawnMixSimulatorTests
    {
        private PrototypeConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();   // class defaults = the shipped formula numbers (WP-010 parity tests)
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null) Object.DestroyImmediate(_config);
        }

        private SpawnMixSimulator.Options Secenekler(uint seed = 7u, int spawns = 1000)
        {
            return new SpawnMixSimulator.Options { config = _config, seed = seed, spawnsPerRound = spawns, firstRound = 1, lastRound = 10 };
        }

        [Test]
        public void Simulator_AyniSeedAyniRapor_FarkliSeedFarkliDizi()
        {
            SpawnMixSimulator.Report a = SpawnMixSimulator.Run(Secenekler(11u, 200));
            SpawnMixSimulator.Report b = SpawnMixSimulator.Run(Secenekler(11u, 200));
            SpawnMixSimulator.Report c = SpawnMixSimulator.Run(Secenekler(12u, 200));
            a.utc = b.utc = c.utc = "";
            Assert.AreEqual(SpawnMixSimulator.ToJson(a), SpawnMixSimulator.ToJson(b), "aynı seed aynı rapor");
            Assert.AreNotEqual(SpawnMixSimulator.ToJson(a), SpawnMixSimulator.ToJson(c), "farklı seed farklı rapor (pozitif kontrol)");
        }

        [Test]
        public void Simulator_R1_10_x1000_KurallarIhlalsiz_GarantiTamBirKez_TanitimOncesiSifir()
        {
            SpawnMixSimulator.Report r = SpawnMixSimulator.Run(Secenekler());
            Debug.Log("[DD-TEST] wp027.spawnmix\n" + SpawnMixSimulator.Summarize(r));
            Assert.AreEqual(10, r.rounds.Length);
            Assert.GreaterOrEqual(r.totalSpawns, 10000);
            Assert.IsEmpty(r.violations, string.Join("\n", r.violations));
            for (int i = 0; i < r.rounds.Length; i++)
            {
                SpawnMixSimulator.RoundResult round = r.rounds[i];
                Assert.GreaterOrEqual(round.spawns, 1000, "R" + round.round);
                Assert.AreEqual(round.pool * round.repetitions, round.spawns, "R" + round.round + " havuz × tekrar");
                Assert.AreEqual(0, round.shortfall, "R" + round.round + " shortfall");
                Assert.AreEqual(0, round.repetitionsWithWrongGuaranteeCount, "R" + round.round + " garanti sayısı");
                for (int t = 1; t < SpawnMixSimulator.TierCount; t++)
                {
                    bool debut = r.introductionRound[t] == round.round;
                    Assert.AreEqual(debut ? round.repetitions : 0, round.guaranteedFlags[t], "R" + round.round + " " + r.tierNames[t] + " garanti bayrağı = tekrar sayısı");
                    if (round.round < r.introductionRound[t]) Assert.AreEqual(0, round.tierCounts[t], "R" + round.round + " " + r.tierNames[t] + " tanıtım öncesi");
                    if (round.round >= r.introductionRound[t]) Assert.Greater(round.tierCounts[t], 0, "R" + round.round + " " + r.tierNames[t] + " tanıtım sonrası hiç doğmadı");
                }
            }
        }

        [Test]
        public void Simulator_TierPaylari_AgirlikOraniyla_Yakin_ArmorPaylari_OlasiliklaYakin()
        {
            SpawnMixSimulator.Report r = SpawnMixSimulator.Run(Secenekler(3u, 4000));
            for (int i = 0; i < r.rounds.Length; i++)
            {
                SpawnMixSimulator.RoundResult round = r.rounds[i];
                float total = 0f;
                for (int t = 0; t < SpawnMixSimulator.TierCount; t++) total += round.tierWeight[t];
                for (int t = 0; t < SpawnMixSimulator.TierCount; t++)
                {
                    float beklenen = total > 0f ? round.tierWeight[t] / total : (t == 0 ? 1f : 0f);
                    // the debut guarantee adds ≤ 1 spawn per repetition; the rest is the weighted draw
                    float tolerans = 0.03f + (r.introductionRound[t] == round.round ? 1f / round.pool : 0f);
                    Assert.AreEqual(beklenen, round.tierShare[t], tolerans, "R" + round.round + " " + r.tierNames[t] + " payı (w/Σw)");
                }

                for (int s = 0; s < 3; s++)
                {
                    SpawnMixSimulator.SlotCounts slot = round.armour[s];
                    for (int t = 0; t < SpawnMixSimulator.TierCount; t++)
                    {
                        if (round.tierCounts[t] < 1000) continue;
                        float pay = (float)slot.equippedByTier[t] / round.tierCounts[t];
                        Assert.AreEqual(slot.expectedChanceByTier[t], pay, 0.05f, "R" + round.round + " " + r.tierNames[t] + " " + slot.slot + " zırh payı ≈ ArmorChance");
                    }
                }
            }
        }

        [Test]
        public void Simulator_GorunumKurallari_KutuphaneVarsa_YasakCift0_TavanlarIcinde()
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            if (set == null || set.ZombieLibrary == null)
            {
                Assert.Inconclusive("DD_Characters / ZombieModuleLibrary yok (WP-016 asset'i) — görünüm bölümü bu makinede simüle edilemedi.");
            }

            SpawnMixSimulator.Report r = SpawnMixSimulator.Run(Secenekler(5u, 1000));
            Assert.IsTrue(r.lookSimulated, "kütüphane yüklendi, görünüm simüle edilmeli");
            int forbidden = 0, invalid = 0, excluded = 0, samples = 0, mino = 0, bald = 0, cw = 0, faded = 0, forced = 0, minoFree = 0, baldFree = 0, cwFree = 0, fadedFree = 0;
            foreach (SpawnMixSimulator.RoundResult round in r.rounds)
            {
                forbidden += round.forbiddenPairs; invalid += round.invalidLooks; excluded += round.excludedModuleHits; samples += round.lookSamples;
                mino += round.minoCape; bald += round.bald; cw += round.colourwayPicks; faded += round.fadedPicks; forced += round.fadedForcedByArmour;
                minoFree += round.freeMinoCape; baldFree += round.freeBald; cwFree += round.freeColourwayPicks; fadedFree += round.freeFadedPicks;
            }

            Assert.GreaterOrEqual(samples, 10000);
            Assert.AreEqual(0, forbidden, "T6 + B5 birlikte");
            Assert.AreEqual(0, invalid, "geçersiz görünüm");
            Assert.AreEqual(0, excluded, "zırhın dışladığı modül görünümde");
            // README ceilings hold on the free (WP-016) draw; the armour-constrained crowd is reported (notes), not asserted (WP-017 rules bite by design).
            Assert.LessOrEqual((float)minoFree / samples, ZombieLookSelector.MaxMinoShare, "T7 tavanı (serbest çekiliş)");
            Assert.LessOrEqual((float)baldFree / samples, ZombieLookSelector.MaxBaldShare, "H0 tavanı (serbest çekiliş)");
            Assert.LessOrEqual((float)fadedFree / cwFree, ZombieLookSelector.MaxFadedShare, "cw2 tavanı (serbest çekiliş)");
            Assert.IsEmpty(r.violations, string.Join("\n", r.violations));
            Debug.Log("[DD-TEST] wp027.look samples=" + samples + " free T7/H0/cw2=" + ((float)minoFree / samples).ToString("0.000") + "/" + ((float)baldFree / samples).ToString("0.000") + "/" + ((float)fadedFree / cwFree).ToString("0.000") +
                      " constrained T7/H0/cw2=" + ((float)mino / samples).ToString("0.000") + "/" + ((float)bald / samples).ToString("0.000") + "/" + ((float)faded / cw).ToString("0.000") + " (forced cw2 " + forced + ") armourVariants=" + r.armourVariantsSimulated +
                      " notes=" + r.notes.Length + "\n" + string.Join("\n", r.notes));
        }

        [Test]
        public void Simulator_Json_SemaVeGeriOkuma()
        {
            SpawnMixSimulator.Report r = SpawnMixSimulator.Run(Secenekler(9u, 100));
            string json = SpawnMixSimulator.ToJson(r);
            StringAssert.Contains("\"schema\": \"" + SpawnMixSimulator.Schema + "\"", json);
            SpawnMixSimulator.Report geri = JsonUtility.FromJson<SpawnMixSimulator.Report>(json);
            Assert.AreEqual(r.totalSpawns, geri.totalSpawns);
            Assert.AreEqual(r.rounds.Length, geri.rounds.Length);
            Assert.AreEqual(r.rounds[2].guaranteedFlags[1], geri.rounds[2].guaranteedFlags[1]);
            string path = Path.Combine(Application.temporaryCachePath, "dd-spawn-mix-test.json");
            SpawnMixSimulator.RunAndWrite(Secenekler(9u, 100), path);
            Assert.IsTrue(File.Exists(path));
            File.Delete(path);
        }

        // ------------------------------------------------------------------ attack window parity (WP-018 consumer)

        private static string Fixture()
        {
            string p = Path.Combine(Application.dataPath, "_Prototype", "Tests", "EditMode", "Fixtures", "AP009-attack-clips.json");
            Assert.IsTrue(File.Exists(p), "fixture: " + p);
            return File.ReadAllText(p);
        }

        [Test]
        public void SaldiriPenceresi_ManifestOkuyucu_UcSaldiriKlibi_TierEslemesi()
        {
            List<AttackWindowParityCheck.Clip> clips = AttackWindowParityCheck.ParseAttackClips(Fixture());
            Assert.AreEqual(3, clips.Count, "hit_window taşıyan klip sayısı");
            AttackWindowParityCheck.Clip swipe = clips.Find(c => c.name == "AP009_zombie_attack_swipe");
            AttackWindowParityCheck.Clip lunge = clips.Find(c => c.name == "AP009_zombie_attack_lunge");
            AttackWindowParityCheck.Clip sweep = clips.Find(c => c.name == "AP009_zombie_attack_sweep");
            Assert.IsNotNull(swipe); Assert.IsNotNull(lunge); Assert.IsNotNull(sweep);
            Assert.AreEqual(EnemyTier.Standard, swipe.tier); Assert.AreEqual(EnemyTier.Special, lunge.tier); Assert.AreEqual(EnemyTier.Elite, sweep.tier);
            Assert.AreEqual(34, swipe.frames); Assert.AreEqual(16, swipe.start); Assert.AreEqual(20, swipe.end); Assert.AreEqual(30, swipe.fps);
            Assert.AreEqual(30, lunge.frames); Assert.AreEqual(10, lunge.start); Assert.AreEqual(13, lunge.end);
            Assert.AreEqual(51, sweep.frames); Assert.AreEqual(24, sweep.start); Assert.AreEqual(30, sweep.end);
        }

        [Test]
        public void SaldiriPenceresi_ZamanlayiciVsAnimasyon_Sapma15YuzdeAltinda_UltraKlipYok()
        {
            List<AttackWindowParityCheck.Row> rows = AttackWindowParityCheck.Compare(_config, Fixture());
            Debug.Log("[DD-TEST] wp027.attackwindow\n" + AttackWindowParityCheck.Table(rows));
            Assert.AreEqual(4, rows.Count);
            for (int i = 0; i < 3; i++)
            {
                AttackWindowParityCheck.Row r = rows[i];
                Assert.IsTrue(r.HasClip, r.tier + " klip");
                Assert.LessOrEqual(r.DevTelegraph, AttackWindowParityCheck.DeviationThreshold, r.tier + " telegraph sapması");
                Assert.LessOrEqual(r.DevActive, AttackWindowParityCheck.DeviationThreshold, r.tier + " aktif pencere sapması");
                Assert.LessOrEqual(r.DevRecovery, AttackWindowParityCheck.DeviationThreshold, r.tier + " recovery sapması");
                Assert.IsFalse(r.OverThreshold, r.tier.ToString());
            }

            Assert.IsFalse(rows[3].HasClip, "Ultra için AP-009 saldırı klibi yok (Overhead) — WP-018/AP-014 devri");
            // the exact numbers of the AP-009 v001 export (regression pin for the fixture)
            Assert.AreEqual(0.533f, rows[0].clipTelegraph, 0.001f); Assert.AreEqual(0.133f, rows[0].clipActive, 0.001f); Assert.AreEqual(0.467f, rows[0].clipRecovery, 0.001f);
            Assert.AreEqual(0.8f, rows[2].clipTelegraph, 0.001f); Assert.AreEqual(0.2f, rows[2].clipActive, 0.001f); Assert.AreEqual(0.7f, rows[2].clipRecovery, 0.001f);
        }
    }
}
