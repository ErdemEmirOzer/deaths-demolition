using System;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-094 HUD v4 juice (R3, C-00 07.10; K/wp-094/HAREKET-HUD.md §2 "Çakışma / oran sınırı", §4 "His bütçesi") · a minimum
    /// interval on the unscaled clock: <see cref="Try"/> passes at most once per <see cref="Interval"/>. The gold punch
    /// (120 ms), the empty-trigger shake (250 ms), the brush knock (Fast) and the heal glow (1 s) each own one. A class (built
    /// once with the HUD) so a gate is never copied by value.
    /// </summary>
    public sealed class HudRateGate
    {
        /// <summary>Clock jitter allowance: a 60 fps frame stamp may land a hair under an exact multiple.</summary>
        public const float Slack = 1e-4f;

        private readonly float _interval;
        private float _last = float.NegativeInfinity;

        public HudRateGate(float interval)
        {
            _interval = Mathf.Max(0f, interval);
        }

        public float Interval => _interval;

        /// <summary>When the gate last passed (−∞ before the first).</summary>
        public float LastAt => _last;

        public bool Ready(float now)
        {
            return now - _last >= _interval - Slack;
        }

        /// <summary>True (and the interval restarts) when the gate is open at <paramref name="now"/>.</summary>
        public bool Try(float now)
        {
            if (!Ready(now))
            {
                return false;
            }

            _last = now;
            return true;
        }

        public void Reset()
        {
            _last = float.NegativeInfinity;
        }
    }

    /// <summary>What a shot plays on the HUD (HAREKET-HUD A1 / A2).</summary>
    [Flags]
    public enum HudShotAccent
    {
        None = 0,

        /// <summary>The spark at the emptied slot, the casing, the ladder's lean - at most every <see cref="UiMotionTokens.ShotAccentMinInterval"/>.</summary>
        Accent = 1,

        /// <summary>The ammo numeral pops 1.04 - a single-fire weapon, ≥ <see cref="UiMotionTokens.SlowShotInterval"/> since its last shot.</summary>
        PopNumeral = 2
    }

    /// <summary>
    /// WP-094 (HAREKET-HUD A1) · the shot cadence: the HUD accents at most ten shots a second (a faster weapon reads as every
    /// second shot accented - the gate does that by itself), and the numeral moves only for a slow single-fire weapon: an
    /// automatic weapon's count never moves per shot (§3 "Hareket yok"), the data stays readable.
    /// </summary>
    public sealed class HudShotCadence
    {
        public const float PopScale = 1.04f;

        private readonly HudRateGate _accent = new HudRateGate(UiMotionTokens.ShotAccentMinInterval);
        private float _lastShot = float.NegativeInfinity;

        /// <summary>When the last shot was noted (−∞ before the first).</summary>
        public float LastShotAt => _lastShot;

        /// <summary>A shot at <paramref name="now"/>; <paramref name="singleFire"/>: the weapon fires one round per trigger pull.</summary>
        public HudShotAccent Note(float now, bool singleFire)
        {
            float since = now - _lastShot;
            _lastShot = now;
            HudShotAccent result = HudShotAccent.None;
            if (_accent.Try(now))
            {
                result |= HudShotAccent.Accent;
            }

            if (singleFire && since >= UiMotionTokens.SlowShotInterval - HudRateGate.Slack)
            {
                result |= HudShotAccent.PopNumeral;
            }

            return result;
        }

        public void Reset()
        {
            _accent.Reset();
            _lastShot = float.NegativeInfinity;
        }
    }

    /// <summary>A heartbeat's half (HAREKET-HUD H5).</summary>
    public enum HudBeat
    {
        None = 0,
        Lub = 1,
        Dub = 2
    }

    /// <summary>
    /// WP-094 (HAREKET-HUD H5; the user: "düşük canda nabız") · the low-health heartbeat's clock: a lub every
    /// <see cref="UiTokens.PulsePeriod"/> (0.92 s = 1.087 Hz, under <see cref="UiMotionTokens.MaxLoopHz"/>) and its dub
    /// <see cref="UiMotionTokens.BeatDubDelay"/> later - event-driven beats (one Scale tween each), not a looping tween.
    /// S-08's FeelEvents.HeartBeat can drive it instead (<see cref="External"/>): the HUD's own clock stops and the visual beat
    /// stays at or under 1.1 Hz however fast the sound beats. One beat per <see cref="Tick"/>: a long frame drops a missed lub
    /// rather than playing two at once. No allocation.
    /// </summary>
    public sealed class HudHeartbeat
    {
        private bool _on;
        private bool _external;
        private float _nextLub;
        private float _dubAt = -1f;
        private float _lastLub = float.NegativeInfinity;

        /// <summary>The lub-to-lub period (s): the attention pulse's.</summary>
        public static float Period => UiTokens.PulsePeriod;

        /// <summary>The shortest lub-to-lub an external source may drive (1 / MaxLoopHz).</summary>
        public static float MinExternalPeriod => 1f / UiMotionTokens.MaxLoopHz;

        public bool Running => _on;

        /// <summary>When the last lub played (−∞ before the first).</summary>
        public float LastLubAt => _lastLub;

        /// <summary>Low health began: the first lub after <paramref name="firstDelay"/> (the cracked heart's stamp lands first).</summary>
        public void Start(float now, float firstDelay)
        {
            _on = true;
            _nextLub = now + Mathf.Max(0f, firstDelay);
            _dubAt = -1f;
        }

        public void Stop()
        {
            _on = false;
            _dubAt = -1f;
        }

        /// <summary>From now on an external clock (S-08's heartbeat) drives the lubs: the HUD's own clock only plays dubs.</summary>
        public bool UsesExternal
        {
            get { return _external; }
            set { _external = value; }
        }

        /// <summary>The beat due this frame (at most one).</summary>
        public HudBeat Tick(float now)
        {
            if (!_on)
            {
                return HudBeat.None;
            }

            if (_dubAt >= 0f && now >= _dubAt)
            {
                _dubAt = -1f;
                return HudBeat.Dub;
            }

            if (_external || now < _nextLub)
            {
                return HudBeat.None;
            }

            _nextLub += Period;
            if (_nextLub <= now)
            {
                _nextLub = now + Period;   // a hitch: resume the rhythm, never catch up in a burst
            }

            return Lub(now);
        }

        /// <summary>An external beat (FeelEvents.HeartBeat): a lub when running and ≥ 1 / MaxLoopHz after the last one.</summary>
        public HudBeat External(float now)
        {
            if (!_on || now - _lastLub < MinExternalPeriod - HudRateGate.Slack)
            {
                return HudBeat.None;
            }

            return Lub(now);
        }

        private HudBeat Lub(float now)
        {
            _lastLub = now;
            _dubAt = now + UiMotionTokens.BeatDubDelay;
            return HudBeat.Lub;
        }

        /// <summary>
        /// The beat's gain: × <see cref="UiMotionTokens.BeatCriticalGain"/> under <see cref="UiMotionTokens.BeatCriticalFraction"/>
        /// of max health - not while the damage edges show (HAREKET-HUD H5: "D1 açıkken genlik artmaz").
        /// </summary>
        public static float Gain(float healthNormalized, bool damageEdgeShowing)
        {
            return healthNormalized < UiMotionTokens.BeatCriticalFraction && !damageEdgeShowing ? UiMotionTokens.BeatCriticalGain : 1f;
        }

        /// <summary>The heart's peak scale for a beat at <paramref name="gain"/>.</summary>
        public static float Peak(HudBeat beat, float gain)
        {
            float base01 = beat == HudBeat.Lub ? UiMotionTokens.BeatLub : beat == HudBeat.Dub ? UiMotionTokens.BeatDub : 1f;
            return 1f + (base01 - 1f) * Mathf.Max(0f, gain);
        }

        /// <summary>The rim's alpha at a lub: rest + (peak − rest) × FlashScale × gain, never past 1.</summary>
        public static float RimPeak(float gain, float flashScale)
        {
            float lift = (UiMotionTokens.BeatRimPeak - UiMotionTokens.BeatRimRest) * Mathf.Clamp01(flashScale) * Mathf.Max(0f, gain);
            return Mathf.Min(1f, UiMotionTokens.BeatRimRest + lift);
        }
    }

    /// <summary>
    /// WP-094 (HAREKET-HUD R1 → R3; S-09 decision (c)) · the round transition's schedule, from the round change (t 0):
    /// R1 the old numeral leaves (Fast, y −4); at <see cref="UiMotionTokens.SealHandoffDelay"/> the new numeral is laid and its
    /// brush reveal opens it (<see cref="UiPresets.RomanRevealDuration"/> by its letters); at the reveal's end the seal stamps
    /// (1.12 → 1 over 90 ms) and on its contact frame StampLanded "round" rises for S-08. Reduced motion: the numeral swaps at
    /// once and the cue still plays (the sound is not motion).
    /// </summary>
    public static class HudRoundTransition
    {
        public const float OldOutDropPx = 4f;
        public const float ClusterKickPx = 2f;
        public const int CueSwap = 1;
        public const int CueSeal = 2;
        public const int CueContact = 3;

        public static float OldOutSeconds => UiMotionTokens.Seconds(UiDuration.Fast);

        public static float HandoffSeconds => UiMotionTokens.SealHandoffDelay;

        /// <summary>When the seal starts its stamp (s after the change) for a numeral of <paramref name="glyphCount"/> letters.</summary>
        public static float SealSeconds(int glyphCount)
        {
            return HandoffSeconds + UiMotionTokens.Seconds(UiPresets.RomanRevealDuration(glyphCount));
        }

        /// <summary>The seal's contact frame - the S-08 cue (s after the change).</summary>
        public static float ContactSeconds(int glyphCount)
        {
            return SealSeconds(glyphCount) + UiMotionTokens.StampInSeconds;
        }
    }

    /// <summary>
    /// WP-094 (HAREKET-HUD §4 "Havuzlar") · a fixed pool of one-shot slots by their end time: a free slot, or the oldest one
    /// reused (casing 6, chunk 3, wedge 2). Pure bookkeeping; the views own the graphics. No allocation after construction.
    /// </summary>
    public sealed class HudSlotPool
    {
        private readonly float[] _startedAt;
        private readonly float[] _endsAt;

        public HudSlotPool(int capacity)
        {
            _startedAt = new float[Mathf.Max(1, capacity)];
            _endsAt = new float[_startedAt.Length];
            Clear();
        }

        public int Capacity => _endsAt.Length;

        public bool Live(int slot, float now)
        {
            return now < _endsAt[slot];
        }

        public int LiveCount(float now)
        {
            int n = 0;
            for (int i = 0; i < _endsAt.Length; i++)
            {
                if (now < _endsAt[i])
                {
                    n++;
                }
            }

            return n;
        }

        public float StartedAt(int slot)
        {
            return _startedAt[slot];
        }

        public float EndsAt(int slot)
        {
            return _endsAt[slot];
        }

        /// <summary>The live slot started last, −1 when none is live.</summary>
        public int Newest(float now)
        {
            int best = -1;
            for (int i = 0; i < _endsAt.Length; i++)
            {
                if (now < _endsAt[i] && (best < 0 || _startedAt[i] > _startedAt[best]))
                {
                    best = i;
                }
            }

            return best;
        }

        /// <summary>A slot for a one-shot of <paramref name="seconds"/> from <paramref name="now"/>: a free one, else the oldest (reused).</summary>
        public int Take(float now, float seconds)
        {
            int slot = -1;
            for (int i = 0; i < _endsAt.Length; i++)
            {
                if (now >= _endsAt[i])
                {
                    slot = i;
                    break;
                }

                if (slot < 0 || _startedAt[i] < _startedAt[slot])
                {
                    slot = i;
                }
            }

            _startedAt[slot] = now;
            _endsAt[slot] = now + Mathf.Max(0f, seconds);
            return slot;
        }

        /// <summary>Restarts <paramref name="slot"/>'s one-shot from <paramref name="now"/> (a wedge re-flaring on its side).</summary>
        public void Restart(int slot, float now, float seconds)
        {
            _startedAt[slot] = now;
            _endsAt[slot] = now + Mathf.Max(0f, seconds);
        }

        /// <summary>Extends a live slot's end (a widened chunk keeps falling to its own end, never shorter).</summary>
        public void ExtendTo(int slot, float endsAt)
        {
            _endsAt[slot] = Mathf.Max(_endsAt[slot], endsAt);
        }

        /// <summary>
        /// H1's rule: a free slot for a new one-shot, or - every slot live - the NEWEST one, flagged <paramref name="extended"/>
        /// (the fourth chunk widens the newest instead of evicting one mid-fall). Its clock is not restarted.
        /// </summary>
        public int TakeOrExtendNewest(float now, float seconds, out bool extended)
        {
            extended = LiveCount(now) >= _endsAt.Length;
            return extended ? Newest(now) : Take(now, seconds);
        }

        public void Clear()
        {
            for (int i = 0; i < _endsAt.Length; i++)
            {
                _startedAt[i] = float.NegativeInfinity;
                _endsAt[i] = float.NegativeInfinity;
            }
        }
    }

    /// <summary>
    /// WP-094 (HAREKET-HUD D1: "eleman başına en çok 2") · a per-element onset limit over a rolling second, next to the motion
    /// core's global FlashGate (<see cref="UiMotion.FlashOnset"/>, ≤ 3 a second across the UI). A refused onset merges.
    /// </summary>
    public sealed class HudOnsetLimiter
    {
        private readonly int _per;
        private readonly float[] _times;
        private readonly int[] _heads;

        public HudOnsetLimiter(int elements, int perElementPerSecond)
        {
            _per = Mathf.Max(1, perElementPerSecond);
            _times = new float[Mathf.Max(1, elements) * _per];
            _heads = new int[Mathf.Max(1, elements)];
            Clear();
        }

        /// <summary>True (and recorded) when <paramref name="element"/> has had fewer than the limit of onsets in the last second.</summary>
        public bool Allow(int element, float now)
        {
            int at = element * _per + _heads[element];
            if (now - _times[at] < 1f)
            {
                return false;
            }

            _times[at] = now;
            _heads[element] = (_heads[element] + 1) % _per;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < _times.Length; i++)
            {
                _times[i] = float.NegativeInfinity;
            }

            for (int i = 0; i < _heads.Length; i++)
            {
                _heads[i] = 0;
            }
        }
    }

    /// <summary>
    /// WP-094 (HAREKET-HUD §4) · the HUD's feel budget: at most <see cref="MaxProminent"/> prominent one-shots at once (scale
    /// past 1.04, a move past 2 px, or a flash). The survival, combat, economy and ceremony layers (P0–P3) note theirs and
    /// always play; the ornament layer (P4: the leaf glint, the catch ring, the brush knock) asks first and is DROPPED - never
    /// queued - when the budget is full or the HUD is in its danger posture.
    /// </summary>
    public sealed class HudFeelBudget
    {
        public const int MaxProminent = 3;

        private readonly float[] _endsAt = new float[8];
        private int _next;

        public HudFeelBudget()
        {
            Clear();
        }

        /// <summary>A prominent one-shot lasting <paramref name="seconds"/> from <paramref name="now"/>.</summary>
        public void Note(float now, float seconds)
        {
            _endsAt[_next] = now + Mathf.Max(0f, seconds);
            _next = (_next + 1) % _endsAt.Length;
        }

        public int Live(float now)
        {
            int n = 0;
            for (int i = 0; i < _endsAt.Length; i++)
            {
                if (now < _endsAt[i])
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>May an ornament (P4) play now? Not in danger, not over budget.</summary>
        public bool OrnamentAllowed(float now, bool danger)
        {
            return !danger && Live(now) < MaxProminent;
        }

        public void Clear()
        {
            for (int i = 0; i < _endsAt.Length; i++)
            {
                _endsAt[i] = float.NegativeInfinity;
            }

            _next = 0;
        }
    }

    /// <summary>
    /// WP-094 (HAREKET-HUD G3) · "+N": gains landing within <see cref="UiMotionTokens.PlusMergeWindow"/> of the shown one add
    /// to it - a sweep of coins reads as one growing "+N", not a flicker of small ones.
    /// </summary>
    public sealed class HudPlusMerge
    {
        private int _shown;
        private float _at = float.NegativeInfinity;

        /// <summary>The "+N" to show for a <paramref name="gain"/> at <paramref name="now"/> (the window restarts).</summary>
        public int Add(float now, int gain)
        {
            _shown = now - _at < UiMotionTokens.PlusMergeWindow ? _shown + gain : gain;
            _at = now;
            return _shown;
        }

        public void Reset()
        {
            _shown = 0;
            _at = float.NegativeInfinity;
        }
    }
}
