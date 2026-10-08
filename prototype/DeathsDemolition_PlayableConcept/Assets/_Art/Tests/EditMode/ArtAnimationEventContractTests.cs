using System.Collections.Generic;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using NUnit.Framework;

namespace DD.Art.Tests.EditMode
{
    /// <summary>WP-071 · event contract v3 (III A8.4): the rules P-06's AP-022 clips are gated by.</summary>
    [TestFixture]
    [Category("WP071")]
    public sealed class ArtAnimationEventContractTests
    {
        private static List<KeyValuePair<string, int>> E(params object[] nameFrame)
        {
            var l = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < nameFrame.Length; i += 2) l.Add(new KeyValuePair<string, int>((string)nameFrame[i], (int)nameFrame[i + 1]));
            return l;
        }

        private static void Ok(List<string> v) => Assert.IsEmpty(v, string.Join("\n", v));
        private static void Fails(List<string> v, string fragment) => Assert.IsTrue(v.Exists(x => x.Contains(fragment)), "expected '" + fragment + "' in: " + string.Join(" | ", v));

        [Test]
        public void Tablo_V3Adlari_Kanonik_Tekil_EskiAdlarDegismez()
        {
            var seen = new HashSet<string>();
            foreach (string n in CharacterAnimationEvents.Names) Assert.IsTrue(seen.Add(n), "çift ad " + n);
            Assert.AreEqual("spawn_land", CharacterAnimationEvents.Names[28], "mevcut 28 ad yerinde");
            foreach (string n in new[] { "ragdoll_handoff", "ground_impact", "attack_recover", "grab_start", "reload_commit", "shell_in", "cover_open", "foot_l_off" })
                Assert.AreNotEqual(CharacterAnimationEvent.None, CharacterAnimationEvents.Parse(n), n);
            for (int i = 0; i < CharacterAnimationEvents.Aliases.GetLength(0); i++)
            {
                Assert.AreEqual(CharacterAnimationEvent.None, CharacterAnimationEvents.Parse(CharacterAnimationEvents.Aliases[i, 0]), "takma ad tabloda olmamalı");
                Assert.AreNotEqual(CharacterAnimationEvent.None, CharacterAnimationEvents.Parse(CharacterAnimationEvents.Aliases[i, 1]), "kanonik ad tabloda");
            }
        }

        [Test]
        public void Saldiri_Pencere_3ile8Kare_Kare6Once_Acilmaz_RecoverSonra()
        {
            Ok(ArtAnimationEventContract.Validate("AP022_zombie_attack_swipe_R", 36, false, E("hit_window_start", 15, "hit_window_end", 19, "attack_recover", 30)));
            Fails(ArtAnimationEventContract.Validate("a", 36, false, E("hit_window_start", 15, "hit_window_end", 16)), "(3..8)");
            Fails(ArtAnimationEventContract.Validate("a", 36, false, E("hit_window_start", 10, "hit_window_end", 20)), "(3..8)");
            Fails(ArtAnimationEventContract.Validate("a", 36, false, E("hit_window_start", 4, "hit_window_end", 8)), "not before frame 6");
            Fails(ArtAnimationEventContract.Validate("a", 36, false, E("hit_window_start", 15, "hit_window_end", 19, "attack_recover", 20)), "attack_recover");
            Fails(ArtAnimationEventContract.Validate("a", 36, false, E("hit_window_start", 15)), "both start and end");
        }

        [Test]
        public void Olum_RagdollHandoff_4ile14_SonrasindaEn14Kare()
        {
            Ok(ArtAnimationEventContract.Validate("AP022_zombie_death_fall_back", 42, false, E("hit", 0, "ragdoll_handoff", 8, "ground_impact", 16, "death_end", 41)));
            Fails(ArtAnimationEventContract.Validate("d", 42, false, E("ragdoll_handoff", 2)), "outside 4..14");
            Fails(ArtAnimationEventContract.Validate("d", 20, false, E("ragdoll_handoff", 10)), "after ragdoll_handoff");
            Fails(ArtAnimationEventContract.Validate("d", 42, false, E("fall_start", 12, "ragdoll_handoff", 8)), "fall_start@12 after");
            Ok(ArtAnimationEventContract.Validate("AP022_zombie_attack_lunge", 32, false, E("telegraph_start", 0, "hit_window_start", 12, "hit_window_end", 15, "attack_recover", 26)));
        }

        [Test]
        public void Adlar_TakmaAdOnerilir_Kare0Kurali_IkiKareIcindeTekrar()
        {
            Fails(ArtAnimationEventContract.Validate("x", 30, false, E("knock_end", 20)), "use 'knockback_end'");
            Fails(ArtAnimationEventContract.Validate("x", 30, false, E("nonsense", 20)), "unknown event");
            Fails(ArtAnimationEventContract.Validate("x", 30, false, E("ground_impact", 0)), "frame 0");
            Fails(ArtAnimationEventContract.Validate("x", 30, false, E("roar", 10, "roar", 11)), "within 2 frames");
            Ok(ArtAnimationEventContract.Validate("x", 90, false, E("shell_in", 20, "shell_in", 21)));   // shell_in repeats by index
        }

        [Test]
        public void Dongu_AyakBasmaKalkma_BirerTane_En4Kare()
        {
            Ok(ArtAnimationEventContract.Validate("AP022_tp_walk_fwd", 16, true, E("foot_l", 0, "foot_l_off", 5, "foot_r", 8, "foot_r_off", 13)));
            Fails(ArtAnimationEventContract.Validate("w", 16, true, E("foot_l", 0, "foot_l_off", 2)), "(≥ 4)");
            Ok(ArtAnimationEventContract.Validate("w", 16, true, E("foot_l", 0, "foot_r", 8)));   // toe-off optional
        }

        [Test]
        public void Reload_Sira_IptalNoktasi_SolEl()
        {
            Ok(ArtAnimationEventContract.Validate("AP022_tp_ar_reload_tac", 57, false,
                E("lh_release", 10, "mag_out", 14, "mag_in", 36, "reload_commit", 36, "reload_cancel_point", 38, "chamber", 44, "lh_grab", 46)));
            Fails(ArtAnimationEventContract.Validate("r", 57, false, E("mag_out", 30, "mag_in", 20)), "not after mag_out");
            Fails(ArtAnimationEventContract.Validate("r", 57, false, E("mag_out", 14, "reload_commit", 36, "reload_cancel_point", 36)), "reload_cancel_point");
            Fails(ArtAnimationEventContract.Validate("r", 57, false, E("lh_release", 13, "mag_out", 14)), "lh_release");
            Fails(ArtAnimationEventContract.Validate("r", 57, false, E("mag_out", 14, "mag_in", 36, "chamber", 44, "lh_grab", 50)), "lh_grab");
        }

        [Test]
        public void TpFp_MekanizmaOlaylari_AyniKare_Tolerans0()
        {
            var tp = E("mag_out", 14, "mag_in", 36, "chamber", 44, "breath", 0);
            Ok(ArtAnimationEventContract.ComparePair("tp", tp, "fp", E("mag_out", 14, "mag_in", 36, "chamber", 44)));
            Fails(ArtAnimationEventContract.ComparePair("tp", tp, "fp", E("mag_out", 14, "mag_in", 37, "chamber", 44)), "mag_in@36 vs @37");
        }

        [Test]
        public void Manifest_Guncel_V3KuralariniGecer()
        {
            Ok(ArtAnimationEventContract.ValidateManifest(ArtAnimationClipContract.Manifest()));
        }

        /// <summary>
        /// WP-096 (P-06 AP-033 contract S4, S-03 07.10): the v3 rules cover the AP-033 zombie locomotion clips (AP033_) as well as
        /// AP022_; the AP-009 set (AP009_) stays outside. The shipped manifest's AP-033 rows are in the gate and pass it.
        /// </summary>
        [Test]
        [Category("WP096")]
        public void Onek_AP033_KapsanirAP009Disarida()
        {
            Assert.IsTrue(ArtAnimationEventContract.AppliesTo("AP022_zombie_run"));
            Assert.IsTrue(ArtAnimationEventContract.AppliesTo("AP033_zombie_walk_lurch_A"));
            Assert.IsFalse(ArtAnimationEventContract.AppliesTo("AP009_zombie_run"));
            Assert.IsFalse(ArtAnimationEventContract.AppliesTo(null));

            // the same broken loop (toe-off 2 frames after the plant) fails under AP033_ / AP022_ and is ignored under AP009_
            var bad = new Dictionary<string, ArtAnimationClipContract.ClipSpec>();
            foreach (string name in new[] { "AP033_zombie_walk_x", "AP022_zombie_walk_x", "AP009_zombie_walk_x" })
            {
                var s = new ArtAnimationClipContract.ClipSpec { name = name, frames = 25, loop = true };
                s.events.AddRange(E("foot_l", 0, "foot_l_off", 2, "foot_r", 12, "foot_r_off", 23));
                bad[name] = s;
            }

            List<string> v = ArtAnimationEventContract.ValidateManifest(bad);
            Fails(v, "AP033_zombie_walk_x: foot_l@0 → foot_l_off@2");
            Fails(v, "AP022_zombie_walk_x: foot_l@0 → foot_l_off@2");
            Assert.IsFalse(v.Exists(x => x.StartsWith("AP009_", System.StringComparison.Ordinal)), "AP009_ kapsam dışı: " + string.Join(" | ", v));

            // the four p1 loops as delivered (anim-events.json, frames = cycle + 1) pass
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_walk_lurch_A", 25, true, E("foot_l", 0, "foot_l_off", 11, "foot_r", 12, "foot_r_off", 23)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_walk_limp_A", 24, true, E("foot_l", 1, "foot_r_off", 1, "foot_l_off", 11, "foot_r", 11)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_run_feral_A", 17, true, E("foot_l", 0, "foot_l_off", 5, "foot_r", 8, "foot_r_off", 14)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_walk_stomp_A", 45, true, E("foot_l", 0, "foot_r_off", 2, "foot_r", 22, "foot_l_off", 25)));
            // p2 (anim-events.json p2-v001)
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_walk_lurch_B", 27, true, E("foot_l", 0, "foot_l_off", 8, "foot_r", 13, "foot_r_off", 23)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_walk_drag_A", 28, true, E("foot_l", 0, "foot_l_off", 11, "foot_r", 15, "foot_r_off", 24)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_walk_drag_B", 26, true, E("foot_l", 0, "foot_l_off", 8, "foot_r", 12, "foot_r_off", 21)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_run_feral_B", 19, true, E("foot_l", 0, "foot_l_off", 5, "foot_r", 9, "foot_r_off", 14)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_run_stumble_A", 18, true, E("foot_l", 0, "foot_l_off", 4, "foot_r", 7, "foot_r_off", 13)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_walk_heavy_A", 30, true, E("foot_l", 1, "foot_r_off", 1, "foot_r", 15, "foot_l_off", 16)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_walk_heavy_B", 30, true, E("foot_l", 1, "foot_r_off", 1, "foot_r", 13, "foot_l_off", 14)));
            Ok(ArtAnimationEventContract.Validate("AP033_zombie_walk_stomp_B", 43, true, E("foot_l", 0, "foot_r_off", 2, "foot_r", 21, "foot_l_off", 25)));

            var m = ArtAnimationClipContract.Manifest();
            var loops = new HashSet<string>();
            var transitions = new HashSet<string>();
            foreach (var entry in m)
            {
                if (!entry.Key.StartsWith(ArtAnimationEventContract.Ap033Prefix, System.StringComparison.Ordinal)) continue;
                (entry.Value.loop ? loops : transitions).Add(entry.Key);
            }
            var expectedLoops = new HashSet<string>(ArtAp033Intake.P1Clips);
            expectedLoops.UnionWith(ArtAp033Intake.P2Clips);
            Assert.AreEqual(12, loops.Count, "AP-033 p1 4 + p2 8 looping gait clips");
            Assert.IsTrue(loops.SetEquals(expectedLoops), "looping AP-033 names must be exactly the p1+p2 delivery");
            Assert.AreEqual(10, transitions.Count, "AP-033 p3 10 non-looping transitions");
            Assert.IsTrue(transitions.SetEquals(ZombieTransitionData.Clips), "non-looping AP-033 names must be exactly p3");
            Assert.AreEqual(22, loops.Count + transitions.Count, "AP-033 p1+p2+p3 total");
        }
    }
}
