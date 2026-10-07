using System;
using System.Collections.Generic;
using DD.Prototype.Characters;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-071 · event contract v3 (WP-071-ANIMATION-V3-DIRECTION Bölüm I K2, III A8.4): the timing rules P-06's
    /// AP-022 clips must meet. Applies to <c>AP022_</c> clips (the AP-009 set predates it); names must be canonical
    /// (<see cref="CharacterAnimationEvents.Names"/>; a known alias is rejected with its canonical spelling).
    /// WP-096 (S-03 07.10, P-06 AP-033 contract S4): the AP-033 zombie locomotion clips (<c>AP033_</c>) are held to the same rules
    /// (P-06's Python validator copy applies them before delivery).
    /// </summary>
    public static class ArtAnimationEventContract
    {
        public const string V3Prefix = "AP022_";
        /// <summary>WP-096: the AP-033 zombie locomotion clips (same v3 rules; no TP / FP pairs).</summary>
        public const string Ap033Prefix = "AP033_";
        /// <summary>WP-096: every clip prefix the v3 rules apply to.</summary>
        public static readonly string[] V3Prefixes = { V3Prefix, Ap033Prefix };

        /// <summary>WP-096: the clip is held to the v3 event rules (AP022_ / AP033_).</summary>
        public static bool AppliesTo(string clip)
        {
            if (clip == null) return false;
            for (int i = 0; i < V3Prefixes.Length; i++) if (clip.StartsWith(V3Prefixes[i], StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Frame-0 events allowed (III A8.4 #76).</summary>
        private static readonly string[] FrameZero = { "fire", "hit", "breath", "spawn_rise", "foot_l", "foot_r", "telegraph_start" };

        /// <summary>Mechanism events whose TP / FP pair frames must match exactly (III A8.4 #75).</summary>
        public static readonly string[] Mechanism =
        {
            "fire", "eject", "chamber", "mag_out", "mag_in", "mag_in_2", "mag_in_3", "reload_commit", "reload_cancel_point",
            "charge", "box_open", "belt_seat", "box_close", "bolt_up", "bolt_back", "bolt_fwd", "bolt_down", "shell_in",
            "rocket_in", "rocket_seat", "cell_out", "cell_in", "canister_out", "canister_in", "cover_open", "cover_close", "shell_eject", "pump_down", "pump_up"
        };

        /// <summary>Every rule the clip breaks; empty = compliant.</summary>
        public static List<string> Validate(string clip, int frames, bool loop, IList<KeyValuePair<string, int>> events)
        {
            var v = new List<string>();
            if (events == null) return v;
            for (int i = 0; i < events.Count; i++)
            {
                string n = events[i].Key;
                int f = events[i].Value;
                if (CharacterAnimationEvents.Parse(n) == CharacterAnimationEvent.None)
                {
                    string canon = CanonicalOf(n);
                    v.Add(clip + ": unknown event '" + n + "'" + (canon != null ? " (use '" + canon + "')" : ""));
                }

                if (f < 0 || f > frames - 1) v.Add(clip + ": " + n + "@" + f + " outside 0.." + (frames - 1));
                if (f == 0 && Array.IndexOf(FrameZero, n) < 0) v.Add(clip + ": " + n + " on frame 0 (only fire/hit/breath/spawn_rise/foot_*/telegraph_start)");
                if (n != "shell_in" && n != "twitch" && n != "spasm")
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (events[j].Key == n && Math.Abs(events[j].Value - f) <= 2) v.Add(clip + ": " + n + " repeats within 2 frames");
                    }
                }
            }

            // hit window: one per attack, 3..8 frames, not in the first 6 frames; attack_recover ≥ 2 after the close
            int ws = Frame(events, "hit_window_start"), we = Frame(events, "hit_window_end");
            if (ws >= 0 || we >= 0)
            {
                if (ws < 0 || we < 0) v.Add(clip + ": hit window needs both start and end");
                else
                {
                    if (Count(events, "hit_window_start") != 1 || Count(events, "hit_window_end") != 1) v.Add(clip + ": exactly one hit window");
                    int len = we - ws;
                    if (len < 3 || len > 8) v.Add(clip + ": hit window " + len + " frames (3..8)");
                    if (ws < 6) v.Add(clip + ": hit window opens at " + ws + " (not before frame 6 — the telegraph must read)");
                    int rec = Frame(events, "attack_recover");
                    if (rec >= 0 && rec < we + 2) v.Add(clip + ": attack_recover@" + rec + " < hit_window_end + 2");
                }
            }

            // ragdoll hand-off: frame 4..14, the clip runs ≥ 14 frames past it (the powered target)
            int rh = Frame(events, "ragdoll_handoff");
            if (rh >= 0)
            {
                if (rh < 4 || rh > 14) v.Add(clip + ": ragdoll_handoff@" + rh + " outside 4..14");
                if (frames - 1 - rh < 14) v.Add(clip + ": only " + (frames - 1 - rh) + " frames after ragdoll_handoff (≥ 14)");
                int fs = Frame(events, "fall_start");
                if (fs > rh) v.Add(clip + ": fall_start@" + fs + " after ragdoll_handoff@" + rh);
            }

            // foot plant / toe-off in loops: one plant + one off per foot, plant → off ≥ 4 frames
            if (loop)
            {
                CheckFoot(v, clip, events, "foot_l", "foot_l_off", frames);
                CheckFoot(v, clip, events, "foot_r", "foot_r_off", frames);
            }

            // reload order: mag_out < mag_in / reload_commit < chamber; cancel point ≥ commit + 1; lh_release ≥ 2 before mag_out
            int mo = Frame(events, "mag_out"), mi = Frame(events, "mag_in"), rc = Frame(events, "reload_commit"), ch = Frame(events, "chamber");
            if (mo >= 0 && mi >= 0 && mi <= mo) v.Add(clip + ": mag_in@" + mi + " not after mag_out@" + mo);
            if (mo >= 0 && rc >= 0 && rc <= mo) v.Add(clip + ": reload_commit@" + rc + " not after mag_out@" + mo);
            int load = Math.Max(mi, rc);
            if (load >= 0 && ch >= 0 && mo >= 0 && ch <= load) v.Add(clip + ": chamber@" + ch + " not after the load@" + load);
            int cp = Frame(events, "reload_cancel_point");
            if (cp >= 0 && rc >= 0 && cp < rc + 1) v.Add(clip + ": reload_cancel_point@" + cp + " < reload_commit + 1");
            int lr = Frame(events, "lh_release");
            if (lr >= 0 && mo >= 0 && lr > mo - 2) v.Add(clip + ": lh_release@" + lr + " must be ≥ 2 frames before mag_out@" + mo);
            int lg = Frame(events, "lh_grab");
            if (lg >= 0 && ch >= 0 && (lg < ch || lg > ch + 4)) v.Add(clip + ": lh_grab@" + lg + " must be 0..4 frames after chamber@" + ch);
            return v;
        }

        /// <summary>TP ↔ FP pair of the same action: every mechanism event on the same frame (tolerance 0).</summary>
        public static List<string> ComparePair(string tpClip, IList<KeyValuePair<string, int>> tp, string fpClip, IList<KeyValuePair<string, int>> fp)
        {
            var v = new List<string>();
            foreach (string n in Mechanism)
            {
                List<int> a = Frames(tp, n), b = Frames(fp, n);
                if (a.Count != b.Count) { v.Add(tpClip + "/" + fpClip + ": " + n + " ×" + a.Count + " vs ×" + b.Count); continue; }
                for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) v.Add(tpClip + "/" + fpClip + ": " + n + "@" + a[i] + " vs @" + b[i]);
            }

            return v;
        }

        /// <summary>The whole manifest: v3 rules on AP022_ / AP033_ clips (WP-096) + every AP022_tp_X / AP022_fp_X pair.</summary>
        public static List<string> ValidateManifest(Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest)
        {
            var v = new List<string>();
            if (manifest == null) return v;
            foreach (KeyValuePair<string, ArtAnimationClipContract.ClipSpec> kv in manifest)
            {
                if (!AppliesTo(kv.Key)) continue;
                v.AddRange(Validate(kv.Key, kv.Value.frames, kv.Value.loop, kv.Value.events));
                const string tpPrefix = V3Prefix + "tp_";
                if (kv.Key.StartsWith(tpPrefix, StringComparison.Ordinal)
                    && manifest.TryGetValue(V3Prefix + "fp_" + kv.Key.Substring(tpPrefix.Length), out ArtAnimationClipContract.ClipSpec fp))
                {
                    v.AddRange(ComparePair(kv.Key, kv.Value.events, fp.name, fp.events));
                }
            }

            return v;
        }

        public static string CanonicalOf(string alias)
        {
            for (int i = 0; i < CharacterAnimationEvents.Aliases.GetLength(0); i++)
            {
                if (CharacterAnimationEvents.Aliases[i, 0] == alias) return CharacterAnimationEvents.Aliases[i, 1];
            }

            return null;
        }

        private static void CheckFoot(List<string> v, string clip, IList<KeyValuePair<string, int>> events, string plant, string off, int frames)
        {
            int pc = Count(events, plant), oc = Count(events, off);
            if (oc == 0) return;   // toe-off optional until the foot lock needs it; when given, the pair is strict
            if (pc != 1 || oc != 1) { v.Add(clip + ": " + plant + " ×" + pc + " / " + off + " ×" + oc + " (one each per cycle)"); return; }
            int p = Frame(events, plant), o = Frame(events, off);
            int gap = (o - p + frames) % frames;
            if (gap < 4) v.Add(clip + ": " + plant + "@" + p + " → " + off + "@" + o + " = " + gap + " frames (≥ 4)");
        }

        private static int Frame(IList<KeyValuePair<string, int>> events, string name)
        {
            for (int i = 0; i < events.Count; i++) if (events[i].Key == name) return events[i].Value;
            return -1;
        }

        private static int Count(IList<KeyValuePair<string, int>> events, string name)
        {
            int c = 0;
            for (int i = 0; i < events.Count; i++) if (events[i].Key == name) c++;
            return c;
        }

        private static List<int> Frames(IList<KeyValuePair<string, int>> events, string name)
        {
            var l = new List<int>();
            for (int i = 0; i < events.Count; i++) if (events[i].Key == name) l.Add(events[i].Value);
            l.Sort();
            return l;
        }
    }
}
