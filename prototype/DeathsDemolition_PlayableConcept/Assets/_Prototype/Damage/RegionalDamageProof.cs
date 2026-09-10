using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DD.Prototype.Core.Config;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Damage
{
    /// <summary>
    /// Executable proof of the regional armour and explosion rules.
    ///
    /// The rules in REQ-082..REQ-090 are the kind that look obviously implemented and are
    /// quietly wrong: surplus that leaks into health, a blast that gets stronger the more
    /// armour it hits, a head multiplier sneaking into explosion damage. This class asserts
    /// each of them against the real RegionalDamageBody with real config sections, so the
    /// claim "the model is correct" is a measurement rather than an opinion.
    ///
    /// It is plain C#. No scene, no Unity object, no play mode: it can be called from the
    /// F1 panel, from a test assembly, or headlessly.
    ///
    /// Determinism note: the model contains no randomness at all. VerifyDeterminism runs
    /// the same hit sequence twice on two fresh bodies and requires byte-identical results,
    /// which is what makes a recorded input sequence replayable.
    /// </summary>
    public static class RegionalDamageProof
    {
        /// <summary>Run everything. Returns true when every rule holds.</summary>
        public static bool Verify(List<string> failures = null, StringBuilder report = null)
        {
            failures = failures ?? new List<string>();
            int before = failures.Count;

            var armour = new ArmorConfigSection();      // 22 / 38 / 28, head 2x, leg 0.8x
            var explosion = new ExplosionConfigSection(); // 25 / 45 / 30 %

            VerifyDirectHits(armour, failures, report);
            VerifyExplosions(armour, explosion, failures, report);
            VerifyDeterminism(armour, explosion, failures, report);

            bool passed = failures.Count == before;
            report?.AppendLine(passed
                ? "[DamageProof] PASS - every regional armour and explosion rule holds."
                : $"[DamageProof] FAIL - {failures.Count - before} rule(s) violated.");

            return passed;
        }

        /// <summary>Convenience for the F1 panel: runs and logs.</summary>
        public static string VerifyAndDescribe()
        {
            var failures = new List<string>();
            var report = new StringBuilder(512);
            bool passed = Verify(failures, report);

            for (int i = 0; i < failures.Count; i++)
            {
                Debug.LogError("[DamageProof] " + failures[i]);
            }

            Debug.Log(report.ToString());
            return passed ? "PASS" : $"FAIL ({failures.Count})";
        }

        // ============================================================== direct hits

        private static void VerifyDirectHits(ArmorConfigSection armour, List<string> failures, StringBuilder report)
        {
            // ---- REQ-082: an armoured region takes zero health damage.
            {
                var body = NewBody(100f);
                body.Armor.Equip(ArmorSlot.Body, armour.BodyDurability); // 38
                DamageResult r = body.ApplyDirectHit(Hit(11f, BodyRegion.UpperBody), armour);

                Check(failures, "REQ-082 armoured torso hit deals no health damage",
                    Mathf.Approximately(r.HealthDamage, 0f), r.HealthDamage);
                Check(failures, "REQ-082 armoured torso hit deals 11 armour damage",
                    Approx(r.ArmorDamage, 11f), r.ArmorDamage);
                Check(failures, "REQ-082 WasArmored is reported", r.WasArmored, r.WasArmored);
                Check(failures, "REQ-082 health untouched", Approx(body.Health, 100f), body.Health);
            }

            // ---- REQ-083: surplus over the plate's remaining durability is DESTROYED.
            {
                var body = NewBody(100f);
                body.Armor.Equip(ArmorSlot.Helmet, armour.HelmetDurability); // 22
                DamageResult r = body.ApplyDirectHit(Hit(500f, BodyRegion.Head), armour);

                Check(failures, "REQ-083 overkill hit on a helmet still deals 0 health damage",
                    Mathf.Approximately(r.HealthDamage, 0f), r.HealthDamage);
                Check(failures, "REQ-083 armour damage is clamped to the 22 that existed",
                    Approx(r.ArmorDamage, 22f), r.ArmorDamage);
                Check(failures, "REQ-083 the plate broke", r.ArmorBroke, r.ArmorBroke);
                Check(failures, "REQ-083 the enemy is untouched at full health",
                    Approx(body.Health, 100f), body.Health);
            }

            // ---- REQ-084: after the break, the region is exposed and hits land on health.
            {
                var body = NewBody(100f);
                body.Armor.Equip(ArmorSlot.Helmet, armour.HelmetDurability);
                body.ApplyDirectHit(Hit(30f, BodyRegion.Head), armour); // breaks it
                DamageResult r = body.ApplyDirectHit(Hit(10f, BodyRegion.Head), armour);

                Check(failures, "REQ-084 the next head hit reaches health",
                    r.HealthDamage > 0f, r.HealthDamage);
                Check(failures, "REQ-084 exposed head applies the 2x multiplier",
                    Approx(r.HealthDamage, 20f), r.HealthDamage);
                Check(failures, "REQ-084 the broken region no longer reports as armoured",
                    !r.WasArmored, r.WasArmored);
            }

            // ---- Exposed multipliers, PROTOTYPE-ONLY values from the request.
            {
                var body = NewBody(1000f);
                DamageResult head = body.ApplyDirectHit(Hit(10f, BodyRegion.Head), armour);
                DamageResult torso = body.ApplyDirectHit(Hit(10f, BodyRegion.UpperBody), armour);
                DamageResult leg = body.ApplyDirectHit(Hit(10f, BodyRegion.LowerBody), armour);

                Check(failures, "exposed head multiplier is 2x", Approx(head.HealthDamage, 20f), head.HealthDamage);
                Check(failures, "exposed upper body multiplier is 1x", Approx(torso.HealthDamage, 10f), torso.HealthDamage);
                Check(failures, "exposed lower body multiplier is 0.8x", Approx(leg.HealthDamage, 8f), leg.HealthDamage);
            }

            // ---- REQ-081: 0, 1, 2 or 3 pieces are all valid states.
            {
                var body = NewBody(100f);
                Check(failures, "REQ-081 zero-armour enemy is legal", body.Armor.IsBare, body.Armor.EquippedCount);

                body.Armor.Equip(ArmorSlot.Leg, armour.LegDurability);
                Check(failures, "REQ-081 one-piece enemy is legal",
                    body.Armor.EquippedCount == 1 && !body.Armor.FullyProtected, body.Armor.EquippedCount);

                body.Armor.Equip(ArmorSlot.Helmet, armour.HelmetDurability);
                body.Armor.Equip(ArmorSlot.Body, armour.BodyDurability);
                Check(failures, "REQ-081 three-piece enemy is fully protected",
                    body.Armor.FullyProtected, body.Armor.ProtectingCount);
            }

            // ---- Killing works and only ever kills once.
            {
                var body = NewBody(20f);
                DamageResult first = body.ApplyDirectHit(Hit(50f, BodyRegion.UpperBody), armour);
                DamageResult second = body.ApplyDirectHit(Hit(50f, BodyRegion.UpperBody), armour);

                Check(failures, "a lethal hit reports Killed", first.Killed, first.Killed);
                Check(failures, "a hit on a corpse reports nothing", !second.Killed && second.HealthDamage <= 0f,
                    second.HealthDamage);
            }

            report?.AppendLine("[DamageProof] direct-hit rules checked.");
        }

        // =============================================================== explosions

        private static void VerifyExplosions(ArmorConfigSection armour, ExplosionConfigSection explosion,
            List<string> failures, StringBuilder report)
        {
            const float blast = 100f;

            // ---- REQ-088: three plates present -> zero health damage.
            // A 40-point blast splits into 10 / 18 / 12, all inside their plates, so the
            // full blast is accounted for on armour and nothing is discarded.
            {
                var body = FullyArmoured(armour, 200f);
                DamageResult r = body.ApplyExplosion(40f, armour, explosion);

                Check(failures, "REQ-088 fully armoured enemy takes no explosion health damage",
                    Mathf.Approximately(r.HealthDamage, 0f), r.HealthDamage);
                Check(failures, "REQ-088 all three plates absorbed their own share (10+18+12)",
                    Approx(r.ArmorDamage, 40f), r.ArmorDamage);
                Check(failures, "REQ-088 no plate broke on a 40-point blast",
                    body.LastBreakCount == 0, body.LastBreakCount);
                Check(failures, "REQ-088 health is untouched", Approx(body.Health, 200f), body.Health);
            }

            // ---- REQ-088 at overkill: a bigger blast still cannot leak into health.
            {
                var body = FullyArmoured(armour, 200f);
                DamageResult r = body.ApplyExplosion(blast, armour, explosion);

                Check(failures, "REQ-088 a 100-point blast on full armour still deals 0 health damage",
                    Mathf.Approximately(r.HealthDamage, 0f), r.HealthDamage);
                Check(failures, "REQ-088 only 88 of the 100 could be absorbed; 12 were discarded",
                    Approx(r.ArmorDamage, 88f), r.ArmorDamage);
            }

            // ---- REQ-085: the total is not multiplied by the number of plates.
            {
                var bare = NewBody(500f);
                var armoured = FullyArmoured(armour, 500f);

                DamageResult bareResult = bare.ApplyExplosion(blast, armour, explosion);
                DamageResult armouredResult = armoured.ApplyExplosion(blast, armour, explosion);

                float bareTotal = bareResult.HealthDamage + bareResult.ArmorDamage;
                float armouredTotal = armouredResult.HealthDamage + armouredResult.ArmorDamage;

                Check(failures, "REQ-085 a bare enemy absorbs exactly the blast total",
                    Approx(bareTotal, blast), bareTotal);
                Check(failures, "REQ-085 an armoured enemy absorbs no MORE than the blast total",
                    armouredTotal <= blast + 0.001f, armouredTotal);
            }

            // ---- REQ-087: only the exposed shares reach health.
            {
                // Helmet only. Exposed = upper 45% + lower 30% = 75 of a 100 blast.
                var body = NewBody(500f);
                body.Armor.Equip(ArmorSlot.Helmet, armour.HelmetDurability);
                DamageResult r = body.ApplyExplosion(blast, armour, explosion);

                Check(failures, "REQ-087 exposed shares (45% + 30%) reach health as 75",
                    Approx(r.HealthDamage, 75f), r.HealthDamage);
                Check(failures, "REQ-086 the helmet share (25%) landed on the helmet, clamped to 22",
                    Approx(r.ArmorDamage, 22f), r.ArmorDamage);
                Check(failures, "REQ-090 the 3 surplus points did NOT reach health",
                    Approx(body.Health, 500f - 75f), body.Health);
            }

            // ---- REQ-089: no regional critical multiplier on blast health damage.
            {
                // Bare enemy. Head share is 25 of 100. If the 2x head multiplier leaked in,
                // total health damage would be 125 instead of 100.
                var body = NewBody(500f);
                DamageResult r = body.ApplyExplosion(blast, armour, explosion);

                Check(failures, "REQ-089 a bare enemy takes exactly the blast total, not 125",
                    Approx(r.HealthDamage, 100f), r.HealthDamage);
            }

            // ---- REQ-086: one blast can damage all three plates at once.
            {
                var body = FullyArmoured(armour, 200f);
                body.ApplyExplosion(30f, armour, explosion); // 7.5 / 13.5 / 9

                Check(failures, "REQ-086 helmet lost its 25% share",
                    Approx(body.Armor.Current(ArmorSlot.Helmet), 22f - 7.5f),
                    body.Armor.Current(ArmorSlot.Helmet));
                Check(failures, "REQ-086 body armour lost its 45% share",
                    Approx(body.Armor.Current(ArmorSlot.Body), 38f - 13.5f),
                    body.Armor.Current(ArmorSlot.Body));
                Check(failures, "REQ-086 leg armour lost its 30% share",
                    Approx(body.Armor.Current(ArmorSlot.Leg), 28f - 9f),
                    body.Armor.Current(ArmorSlot.Leg));
            }

            // ---- REQ-090 in isolation: a share that breaks a plate stops there.
            {
                var body = FullyArmoured(armour, 200f);
                // 400 blast -> head 100 (plate has 22), upper 180 (38), lower 120 (28).
                DamageResult r = body.ApplyExplosion(400f, armour, explosion);

                Check(failures, "REQ-090 a blast that shatters every plate still deals 0 health damage",
                    Mathf.Approximately(r.HealthDamage, 0f), r.HealthDamage);
                Check(failures, "REQ-090 absorbed damage equals the plates' durability, not the blast",
                    Approx(r.ArmorDamage, 22f + 38f + 28f), r.ArmorDamage);
                Check(failures, "REQ-090 three plates broke in one blast",
                    body.LastBreakCount == 3, body.LastBreakCount);
                Check(failures, "REQ-090 health is still full", Approx(body.Health, 200f), body.Health);
            }

            // ---- REQ-088 follow-up: once a plate is gone, the blast finds a way in.
            {
                var body = FullyArmoured(armour, 200f);
                body.ApplyExplosion(400f, armour, explosion);   // shatters everything
                DamageResult r = body.ApplyExplosion(100f, armour, explosion);

                Check(failures, "REQ-088 after the plates break, the blast reaches health in full",
                    Approx(r.HealthDamage, 100f), r.HealthDamage);
            }

            // ---- Share normalisation: mis-authored shares cannot inflate the blast.
            {
                var skewed = new ExplosionConfigSection { HeadShare = 1f, UpperBodyShare = 1f, LowerBodyShare = 1f };
                var body = NewBody(500f);
                DamageResult r = body.ApplyExplosion(blast, armour, skewed);

                Check(failures, "shares are normalised, so 1/1/1 still totals one blast",
                    Approx(r.HealthDamage, 100f), r.HealthDamage);
            }

            report?.AppendLine("[DamageProof] explosion rules checked.");
        }

        // ============================================================== determinism

        private static void VerifyDeterminism(ArmorConfigSection armour, ExplosionConfigSection explosion,
            List<string> failures, StringBuilder report)
        {
            const int steps = 24;

            var a = FullyArmoured(armour, 340f);
            var b = FullyArmoured(armour, 340f);

            bool identical = true;
            for (int i = 0; i < steps; i++)
            {
                var region = (BodyRegion)(i % 3);
                float amount = 3f + (i % 7) * 2.5f;

                DamageResult ra;
                DamageResult rb;

                if (i % 5 == 4)
                {
                    ra = a.ApplyExplosion(amount * 3f, armour, explosion);
                    rb = b.ApplyExplosion(amount * 3f, armour, explosion);
                }
                else
                {
                    ra = a.ApplyDirectHit(Hit(amount, region), armour);
                    rb = b.ApplyDirectHit(Hit(amount, region), armour);
                }

                if (!Same(ra, rb))
                {
                    identical = false;
                    failures.Add($"determinism: step {i} diverged " +
                                 $"(A hp {ra.HealthDamage} armour {ra.ArmorDamage} killed {ra.Killed} / " +
                                 $"B hp {rb.HealthDamage} armour {rb.ArmorDamage} killed {rb.Killed})");
                    break;
                }
            }

            Check(failures, "determinism: identical input sequences produce identical results",
                identical && Approx(a.Health, b.Health), a.Health - b.Health);

            report?.AppendLine("[DamageProof] determinism replay checked over " + steps + " steps.");
        }

        private static bool Same(in DamageResult a, in DamageResult b)
        {
            return Approx(a.HealthDamage, b.HealthDamage)
                   && Approx(a.ArmorDamage, b.ArmorDamage)
                   && a.ArmorBroke == b.ArmorBroke
                   && a.Killed == b.Killed
                   && a.WasArmored == b.WasArmored
                   && a.Region == b.Region;
        }

        // ================================================================== helpers

        private static RegionalDamageBody NewBody(float health)
        {
            var body = new RegionalDamageBody();
            body.Reset(health);
            return body;
        }

        private static RegionalDamageBody FullyArmoured(ArmorConfigSection armour, float health)
        {
            RegionalDamageBody body = NewBody(health);
            body.Armor.Equip(ArmorSlot.Helmet, armour.HelmetDurability);
            body.Armor.Equip(ArmorSlot.Body, armour.BodyDurability);
            body.Armor.Equip(ArmorSlot.Leg, armour.LegDurability);
            return body;
        }

        private static DamageHit Hit(float amount, BodyRegion region)
        {
            return new DamageHit
            {
                Amount = amount,
                Region = region,
                Point = Vector3.zero,
                Direction = Vector3.forward,
                ImpactForce = DamageForce.RifleBullet,
                Source = DamageSourceKind.Bullet
            };
        }

        private static bool Approx(float a, float b)
        {
            return Mathf.Abs(a - b) < 0.001f;
        }

        private static void Check(List<string> failures, string what, bool condition, object actual)
        {
            if (!condition)
            {
                failures.Add($"{what}  (actual: {actual})");
            }
        }
    }
}
