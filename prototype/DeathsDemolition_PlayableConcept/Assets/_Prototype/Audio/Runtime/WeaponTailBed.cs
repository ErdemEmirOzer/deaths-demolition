using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-097 K1 (03 §3, 04 decision 2) · the pure rules of the top-down string tail bed, with an injected clock so EditMode can
    /// drive a 900 RPM string frame by frame. Two slots ping-pong:
    ///   * a string shot starts the space tail on the idle slot only when ≥ <see cref="BedRetrigger"/> passed since the last start,
    ///     at <see cref="StringBedDb"/> under the full wet level, and fades the other slot out over <see cref="CrossfadeOutSeconds"/>
    ///     — so at most two tails ever sound, and the energy of N overlapping tails (+10·log10 N) never builds up (no mud);
    ///   * the trigger release ramps the slot still sounding up to the full wet level over <see cref="ReleaseRampSeconds"/> — the
    ///     last shot's long tail, with no second tail started on top (no doubling);
    ///   * stop: every slot ramps to silence over <see cref="StopRampSeconds"/>.
    /// Levels are linear and relative to the full wet level (1 = WetDb). Value type, no allocation.
    /// </summary>
    public struct TailBedPolicy
    {
        public const int Voices = 2;
        public const float BedRetrigger = 0.15f;
        public const float CrossfadeOutSeconds = 0.12f;
        public const float ReleaseRampSeconds = 0.06f;
        public const float StopRampSeconds = 0.05f;

        /// <summary>The bed under the full wet level while the trigger is held (dB; SE 01 M2: −6, 04 decision 2).</summary>
        public const float StringBedDb = -6f;

        /// <summary>A release ramps the bed only when this much of its tail is still to come (s); less → the caller rings a fresh tail.</summary>
        public const float MinReleaseRemaining = 0.3f;

        /// <summary>10^(−6/20): the bed's linear level against the full wet level.</summary>
        public static readonly float BedLevel = Mathf.Pow(10f, StringBedDb / 20f);

        private int _slotPlusOne;   // the slot the string's bed is on, + 1 (0 = none)
        private float _lastStart;
        private float _level0, _level1;
        private float _target0, _target1;
        private float _rate0, _rate1;   // level units per second towards the target
        private float _end0, _end1;     // clock time the slot's clip runs out
        private int _starts;
        private int _peakSounding;

        /// <summary>The slot the held string's bed is on, −1 when none.</summary>
        public int Current => _slotPlusOne - 1;

        /// <summary>Beds started since construction / <see cref="ResetCounters"/>.</summary>
        public int Starts => _starts;

        /// <summary>Most slots heard at once (level or target above 0).</summary>
        public int PeakSounding => _peakSounding;

        public float Level(int slot) => slot == 0 ? _level0 : _level1;
        public float Target(int slot) => slot == 0 ? _target0 : _target1;
        public float EndsAt(int slot) => slot == 0 ? _end0 : _end1;

        /// <summary>A slot is heard while its level or its target is above 0 and its clip has not run out.</summary>
        public bool Sounding(int slot) => Level(slot) > 0f || Target(slot) > 0f;

        public int SoundingCount => (Sounding(0) ? 1 : 0) + (Sounding(1) ? 1 : 0);

        /// <summary>True when a string shot at <paramref name="now"/> starts a new bed (first shot, or ≥ 0.15 s since the last start).</summary>
        public bool Due(float now) => _slotPlusOne == 0 || now - _lastStart >= BedRetrigger;

        /// <summary>Starts the bed on the idle slot (the other fades out) and returns that slot. <paramref name="clipSeconds"/> = delay + clip length.</summary>
        public int Start(float now, float clipSeconds)
        {
            int previous = _slotPlusOne - 1;
            int next = previous == 0 ? 1 : 0;
            if (previous >= 0)
            {
                FadeOut(previous, CrossfadeOutSeconds);
            }

            Set(next, BedLevel, BedLevel, 0f, now + Mathf.Max(0f, clipSeconds));
            _slotPlusOne = next + 1;
            _lastStart = now;
            _starts++;
            NotePeak();
            return next;
        }

        /// <summary>
        /// The trigger let go: the bed still sounding ramps to the full wet level over 0.06 s and is returned; −1 when there is no
        /// bed, it is already fading, or less than <see cref="MinReleaseRemaining"/> of its tail is left (the caller rings a fresh tail).
        /// </summary>
        public int Release(float now)
        {
            int current = _slotPlusOne - 1;
            _slotPlusOne = 0;
            if (current < 0 || Target(current) <= 0f || EndsAt(current) - now < MinReleaseRemaining)
            {
                return -1;
            }

            float level = Level(current);
            Set(current, level, 1f, Mathf.Max(0f, 1f - level) / ReleaseRampSeconds, EndsAt(current));
            return current;
        }

        /// <summary>Run end / death / disable: every slot to silence over 0.05 s.</summary>
        public void StopAll()
        {
            FadeOut(0, StopRampSeconds);
            FadeOut(1, StopRampSeconds);
            _slotPlusOne = 0;
        }

        /// <summary>Advances the ramps by <paramref name="dt"/>; a slot whose clip ran out (<paramref name="now"/> ≥ its end) is silent.</summary>
        public void Tick(float dt, float now)
        {
            _level0 = Step(_level0, _target0, _rate0, dt);
            _level1 = Step(_level1, _target1, _rate1, dt);
            if (now >= _end0) { _level0 = 0f; _target0 = 0f; }
            if (now >= _end1) { _level1 = 0f; _target1 = 0f; }
            if (_slotPlusOne > 0 && !Sounding(_slotPlusOne - 1)) _slotPlusOne = 0;
            NotePeak();
        }

        /// <summary>Paused time does not use up a clip (AudioListener.pause holds the sources): push the ends out by <paramref name="dt"/>.</summary>
        public void ShiftEnds(float dt)
        {
            _end0 += dt;
            _end1 += dt;
        }

        public void ResetCounters()
        {
            _starts = 0;
            _peakSounding = SoundingCount;
        }

        private void FadeOut(int slot, float seconds)
        {
            float level = Level(slot);
            Set(slot, level, 0f, level / Mathf.Max(0.001f, seconds), EndsAt(slot));
        }

        private void Set(int slot, float level, float target, float rate, float end)
        {
            if (slot == 0)
            {
                _level0 = level; _target0 = target; _rate0 = rate; _end0 = end;
            }
            else
            {
                _level1 = level; _target1 = target; _rate1 = rate; _end1 = end;
            }
        }

        private void NotePeak()
        {
            int n = SoundingCount;
            if (n > _peakSounding) _peakSounding = n;
        }

        private static float Step(float level, float target, float rate, float dt)
        {
            // rate 0 = hold (the bed sits at its level while the trigger is held)
            return rate <= 0f ? level : Mathf.MoveTowards(level, target, rate * dt);
        }
    }

    /// <summary>
    /// WP-097 K1 (03 §3) · the top-down string tail bed's two AudioSources (Weapon bus, dry of the region reverb, as every weapon
    /// voice), driven by <see cref="TailBedPolicy"/>. Owned and ticked by <see cref="PrototypeAudioDirector"/>; used by
    /// <see cref="WeaponFireAudio"/> only in top-down with <see cref="WeaponReflections.Enabled"/> — third-person and first-person
    /// keep the R2 release tail exactly. The bus gain (duck) is followed every tick. Allocation-free after construction.
    /// </summary>
    public sealed class WeaponTailBed
    {
        private readonly AudioSource[] _sources = new AudioSource[TailBedPolicy.Voices];
        private readonly float[] _fullGain = new float[TailBedPolicy.Voices];
        private readonly AudioBusMixer _mixer;
        private TailBedPolicy _policy;
        private float _maxHeldLevel;

        public WeaponTailBed(Transform parent, AudioBusMixer mixer, AudioMixerBridge bridge)
        {
            _mixer = mixer;
            for (int i = 0; i < _sources.Length; i++)
            {
                var go = new GameObject("Tail Bed " + i);
                go.transform.SetParent(parent, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 8f;
                source.maxDistance = 60f;
                source.dopplerLevel = 0f;
                source.bypassReverbZones = true;
                source.priority = 128;
                source.volume = 0f;
                bridge?.Route(source, AudioBus.Weapon);
                _sources[i] = source;
            }
        }

        /// <summary>Beds started (one per ≥ 0.15 s of a held string). Test / F1 readout.</summary>
        public int Starts => _policy.Starts;

        /// <summary>Most bed voices heard at once (contract ≤ 2).</summary>
        public int PeakSounding => _policy.PeakSounding;

        public int Sounding => _policy.SoundingCount;

        /// <summary>Highest bed level (relative to the full wet level, linear) while a trigger was held; ≤ 10^(−6/20) by contract.</summary>
        public float MaxHeldLevel => _maxHeldLevel;

        /// <summary>Releases that ramped a bed (the rest rang a fresh tail).</summary>
        public int Ramps { get; private set; }

        /// <summary>The held string's bed slot's level (relative to full wet), 0 when none.</summary>
        public float CurrentLevel => _policy.Current >= 0 ? _policy.Level(_policy.Current) : 0f;

        public bool Due(float now) => _policy.Due(now);

        /// <summary>
        /// Starts the space tail as the string's bed (call only when <see cref="Due"/>). <paramref name="fullGain"/> = the linear
        /// gain of the full wet tail (placement gain × TailDb × WetDb); the bed plays at <see cref="TailBedPolicy.StringBedDb"/> under it.
        /// </summary>
        public bool Start(AudioClip clip, Vector3 position, float delay, float spatial, float fullGain, float now)
        {
            if (clip == null)
            {
                return false;
            }

            float d = Mathf.Max(0f, delay);
            int slot = _policy.Start(now, d + clip.length);
            AudioSource source = _sources[slot];
            source.Stop();
            source.clip = clip;
            source.pitch = 1f;
            source.spatialBlend = spatial;
            source.transform.position = position;
            _fullGain[slot] = fullGain;
            Apply(slot);
            if (d > 0f) source.PlayDelayed(d);
            else source.Play();
            float level = _policy.Level(slot);
            if (level > _maxHeldLevel) _maxHeldLevel = level;
            return true;
        }

        /// <summary>The trigger let go: true when the bed took the release (ramped to full wet), false = ring a fresh tail.</summary>
        public bool OnRelease(float now)
        {
            if (_policy.Release(now) < 0)
            {
                return false;
            }

            Ramps++;
            return true;
        }

        /// <summary>Run end / death / disable: silence over 0.05 s.</summary>
        public void StopAll()
        {
            _policy.StopAll();
        }

        public void ResetCounters()
        {
            _policy.ResetCounters();
            _maxHeldLevel = 0f;
            Ramps = 0;
        }

        /// <summary>Per frame (unscaled). Paused: the sources are held by the listener pause and their clips keep their remaining time.</summary>
        public void Tick(float unscaledDeltaTime, bool paused, float now)
        {
            if (paused)
            {
                _policy.ShiftEnds(unscaledDeltaTime);
                return;
            }

            _policy.Tick(unscaledDeltaTime, now);
            int held = _policy.Current;
            if (held >= 0 && _policy.Target(held) < 1f)
            {
                float level = _policy.Level(held);
                if (level > _maxHeldLevel) _maxHeldLevel = level;
            }

            for (int i = 0; i < _sources.Length; i++)
            {
                AudioSource source = _sources[i];
                if (source.clip == null)
                {
                    continue;
                }

                if (!_policy.Sounding(i))
                {
                    source.Stop();
                    source.clip = null;
                    source.volume = 0f;
                    continue;
                }

                Apply(i);
            }
        }

        private void Apply(int slot)
        {
            float bus = _mixer != null ? _mixer.Gain(AudioBus.Weapon) : 1f;
            _sources[slot].volume = Mathf.Clamp01(_fullGain[slot] * _policy.Level(slot)) * bus;
        }
    }
}
