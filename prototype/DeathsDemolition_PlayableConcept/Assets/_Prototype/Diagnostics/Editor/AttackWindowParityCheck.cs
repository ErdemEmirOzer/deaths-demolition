using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Diagnostics.EditorTools
{
    /// <summary>
    /// WP-027 · Attack window parity: the timer windows the combat code runs today
    /// (<c>EnemyTierSettings.TelegraphTime / AttackActiveTime / AttackRecoveryTime</c>, DEC-029
    /// hitbox ON only in the active window) against the AP-009 clip events
    /// (<c>hit_window_start / hit_window_end</c>, frame index at the clip's fps). WP-018 moves
    /// the hitbox to the animation event; this table says, per tier, how far the animated
    /// window sits from the timer window so that move cannot change the balance silently.
    /// Read-only: nothing here changes a value (a deviation above the threshold is a note to
    /// S-06, not a fix). The manifest is read with a minimal regex reader (no JSON library
    /// dependency in the Editor assembly); the frozen fixture under Tests/EditMode/Fixtures
    /// mirrors the AP-009 export.
    /// </summary>
    public static class AttackWindowParityCheck
    {
        public const float DeviationThreshold = 0.15f;   // > 15 % → note to S-06 (package rule)

        public sealed class Row
        {
            public EnemyTier tier;
            public string clip;
            public int fps, frames, windowStart, windowEnd;
            public float timerTelegraph, timerActive, timerRecovery;    // seconds (config)
            public float clipTelegraph, clipActive, clipRecovery, clipTotal;   // seconds (events; active = end - start frames, exclusive end)
            public float DevTelegraph => Dev(timerTelegraph, clipTelegraph);
            public float DevActive => Dev(timerActive, clipActive);
            public float DevRecovery => Dev(timerRecovery, clipRecovery);
            public float DevTotal => Dev(timerTelegraph + timerActive + timerRecovery, clipTotal);
            public bool OverThreshold => DevTelegraph > DeviationThreshold || DevActive > DeviationThreshold || DevRecovery > DeviationThreshold;
            public bool HasClip => !string.IsNullOrEmpty(clip);

            private static float Dev(float timer, float clip) => timer <= 0f ? (clip <= 0f ? 0f : 1f) : Mathf.Abs(clip - timer) / timer;
        }

        /// <summary>Build the table for the four tiers from the config and the manifest text; tiers with no attack clip in the manifest get a row with <c>HasClip == false</c>.</summary>
        public static List<Row> Compare(PrototypeConfig config, string manifestJson)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var rows = new List<Row>(4);
            List<Clip> clips = ParseAttackClips(manifestJson);
            for (int t = 0; t < 4; t++)
            {
                var tier = (EnemyTier)t;
                EnemyTierSettings s = config.Enemies.Find(tier);
                var row = new Row { tier = tier };
                if (s != null)
                {
                    row.timerTelegraph = s.TelegraphTime;
                    row.timerActive = s.AttackActiveTime;
                    row.timerRecovery = s.AttackRecoveryTime;
                }

                Clip c = clips.Find(x => x.tier == tier);
                if (c != null)
                {
                    row.clip = c.name; row.fps = c.fps; row.frames = c.frames; row.windowStart = c.start; row.windowEnd = c.end;
                    float f = c.fps > 0 ? c.fps : 30f;
                    row.clipTelegraph = c.start / f;
                    row.clipActive = (c.end - c.start) / f;
                    row.clipRecovery = (c.frames - c.end) / f;
                    row.clipTotal = c.frames / f;
                }

                rows.Add(row);
            }

            return rows;
        }

        public static string Table(List<Row> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Tier | Clip | fps | frames | window | timer T/A/R (s) | clip T/A/R (s) | dev T/A/R | total timer→clip | note |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (Row r in rows)
            {
                if (!r.HasClip)
                {
                    sb.AppendLine("| " + r.tier + " | — | | | | " + F(r.timerTelegraph) + "/" + F(r.timerActive) + "/" + F(r.timerRecovery) + " | — | — | — | no AP-009 attack clip for this tier |");
                    continue;
                }

                sb.AppendLine("| " + r.tier + " | " + r.clip + " | " + r.fps + " | " + r.frames + " | " + r.windowStart + "–" + r.windowEnd + " | " +
                              F(r.timerTelegraph) + "/" + F(r.timerActive) + "/" + F(r.timerRecovery) + " | " +
                              F(r.clipTelegraph) + "/" + F(r.clipActive) + "/" + F(r.clipRecovery) + " | " +
                              P(r.DevTelegraph) + "/" + P(r.DevActive) + "/" + P(r.DevRecovery) + " | " +
                              F(r.timerTelegraph + r.timerActive + r.timerRecovery) + "→" + F(r.clipTotal) + " (" + P(r.DevTotal) + ") | " +
                              (r.OverThreshold ? "> " + P(DeviationThreshold) + " → S-06 note" : "within " + P(DeviationThreshold)) + " |");
            }

            return sb.ToString();
        }

        // ---------------------------------------------------------------- minimal manifest reader

        public sealed class Clip
        {
            public string name;
            public EnemyTier tier;
            public int fps, frames, start, end;
        }

        private static readonly Regex ClipBlock = new Regex("\\{[^{}]*\"clip\"\\s*:\\s*\"([^\"]+)\"[^{}]*?\"events\"\\s*:\\s*\\{([^{}]*)\\}[^{}]*\\}", RegexOptions.Singleline);
        private static readonly Regex Field = new Regex("\"(clip|frames|fps|tier)\"\\s*:\\s*(\"([^\"]*)\"|(-?\\d+(?:\\.\\d+)?))");
        private static readonly Regex Event = new Regex("\"(hit_window_start|hit_window_end)\"\\s*:\\s*(\\d+)");

        /// <summary>The attack clips (those carrying hit_window events) of the manifest; the tier comes from the manifest's "tier" field (Standard / Runner / Guard / Executioner or the enum name).</summary>
        public static List<Clip> ParseAttackClips(string json)
        {
            var list = new List<Clip>();
            if (string.IsNullOrEmpty(json)) return list;
            // Clip objects nest a "validation" object; strip nested braces below depth 2 so the block regex sees one flat object per clip.
            string flat = Flatten(json);
            foreach (Match m in ClipBlock.Matches(flat))
            {
                string block = m.Value;
                string events = m.Groups[2].Value;
                int start = -1, end = -1;
                foreach (Match e in Event.Matches(events))
                {
                    if (e.Groups[1].Value == "hit_window_start") start = int.Parse(e.Groups[2].Value, CultureInfo.InvariantCulture);
                    else end = int.Parse(e.Groups[2].Value, CultureInfo.InvariantCulture);
                }

                if (start < 0 || end < 0) continue;
                var c = new Clip { start = start, end = end, fps = 30 };
                foreach (Match f in Field.Matches(block))
                {
                    string key = f.Groups[1].Value, str = f.Groups[3].Value, num = f.Groups[4].Value;
                    switch (key)
                    {
                        case "clip": c.name = str; break;
                        case "frames": c.frames = (int)float.Parse(num, CultureInfo.InvariantCulture); break;
                        case "fps": c.fps = (int)float.Parse(num, CultureInfo.InvariantCulture); break;
                        case "tier": c.tier = TierOf(str); break;
                    }
                }

                list.Add(c);
            }

            return list;
        }

        /// <summary>Manifest tier labels: the AP-007/009 display names or the enum names.</summary>
        public static EnemyTier TierOf(string label)
        {
            switch ((label ?? "").Trim().ToLowerInvariant())
            {
                case "runner": case "special": return EnemyTier.Special;
                case "guard": case "elite": return EnemyTier.Elite;
                case "executioner": case "ultra": return EnemyTier.Ultra;
                default: return EnemyTier.Standard;
            }
        }

        private static string Flatten(string json)
        {
            // Remove any object nested at depth >= 3 (top → clips[] → clip → nested) so each clip is a single-level block; events stay (depth 3) by re-inserting them.
            var sb = new StringBuilder(json.Length);
            int depth = 0; bool inString = false;
            var eventsBuffer = new StringBuilder(); bool captureEvents = false; int eventsDepth = -1;
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (inString) { sb.Append(ch); if (ch == '\\') { sb.Append(json[++i]); } else if (ch == '"') inString = false; continue; }
                if (ch == '"')
                {
                    inString = true;
                    if (depth == 3 && json.Length > i + 8 && string.CompareOrdinal(json, i, "\"events\"", 0, 8) == 0) captureEvents = true;
                    sb.Append(ch); continue;
                }

                if (ch == '{')
                {
                    depth++;
                    if (depth >= 4)
                    {
                        if (captureEvents && eventsDepth < 0) { eventsDepth = depth; sb.Append(ch); continue; }
                        if (eventsDepth > 0) { sb.Append(ch); continue; }
                        // skip nested object entirely
                        int d = 1; while (d > 0 && ++i < json.Length) { if (json[i] == '{') d++; else if (json[i] == '}') d--; }
                        depth--; sb.Append("null"); continue;
                    }

                    sb.Append(ch); continue;
                }

                if (ch == '}')
                {
                    if (eventsDepth == depth) { eventsDepth = -1; captureEvents = false; }
                    depth--; sb.Append(ch); continue;
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        private static string P(float v) => (v * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }
}
