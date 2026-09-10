using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// A fixed set of AudioSources, and the rules for who gets one when they have all run
    /// out.
    ///
    /// THE BUDGET IS THE DESIGN
    /// A rifle firing into eight zombies produces, per second: 8 shots, 8-24 impacts,
    /// 8 flesh hits, 8 hit markers, a kill or two and whatever the armour is doing. That
    /// is comfortably over sixty requests a second against sixteen voices. Letting Unity
    /// handle it by virtualising sources gives the player a mix decided by distance and
    /// nothing else - the armour break dies because it happened four metres further away
    /// than a wall impact. So the decision is made here, on the design ladder in
    /// <see cref="AudioMixPlan"/>, and the sounds that lose are always the ones that repeat.
    ///
    /// THREE GATES, IN ORDER
    ///   1. RETRIGGER   the same sound cannot restart inside its MinRetrigger window.
    ///                  This is what stops a 20 ms double-hit from producing two copies
    ///                  of one event a few samples apart, which is not "twice as loud",
    ///                  it is a comb filter.
    ///   2. INSTANCES   a sound may only have MaxInstances copies alive. Past that the
    ///                  OLDEST copy is recycled - the newest hit is always the one the
    ///                  player is looking at, and the copy being taken is the one already
    ///                  decayed furthest.
    ///   3. PRIORITY    with the pool full, the lowest-priority active voice is stolen if
    ///                  and only if it is worth less than the incoming one. Ties are broken
    ///                  by whichever voice has least time left to play, so what gets cut is
    ///                  the tail of something already almost silent. That is why stealing
    ///                  is not audible as a click.
    /// If all three fail the cue is DROPPED and counted, and the count is on the F1 panel.
    /// A budget you cannot see the cost of is a budget nobody will ever tune.
    /// </summary>
    public sealed class AudioVoicePool
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public AudioSoundId Sound;
            public AudioBus Bus;
            public float Priority;
            public float BaseVolume;
            public float Remaining;
            public float Age;
            public bool Active;
        }

        private readonly Voice[] _voices;
        private readonly AudioBusMixer _mixer;
        private readonly Dictionary<AudioSoundId, float> _lastPlayed = new Dictionary<AudioSoundId, float>(48);

        private SynthRandom _jitter = new SynthRandom(0x1234ABCD);

        /// <summary>Name of the pool, for the F1 readout.</summary>
        public string Name { get; }

        /// <summary>Cues this pool refused since the last reset. F1 readout.</summary>
        public int DroppedCount { get; private set; }

        /// <summary>Voices stolen from a lower-priority sound since the last reset.</summary>
        public int StolenCount { get; private set; }

        /// <summary>Cues refused by the retrigger gate. Usually the largest of the three.</summary>
        public int RetriggerBlockedCount { get; private set; }

        /// <summary>The last sound that lost. Names the victim, which is what makes the number useful.</summary>
        public AudioSoundId LastDropped { get; private set; }

        /// <summary>Peak simultaneous voices seen. The number the budget should be set from.</summary>
        public int PeakActive { get; private set; }

        /// <summary>Hard number of AudioSources allocated. Never changes after construction.</summary>
        public int HardCapacity => _voices.Length;

        /// <summary>
        /// The budget actually in force, which is what
        /// <c>PrototypeConfig.Feedback.MaxCombatAudioVoices</c> and the F1 slider move.
        ///
        /// Sources are allocated once at the hard capacity and the budget is enforced by
        /// only LOOKING at the first N of them. Reallocating a pool mid-run to honour a
        /// slider would stop every sound that happened to be playing, which is precisely
        /// the moment a tester is trying to listen to something.
        /// </summary>
        public int Capacity
        {
            get => _softCapacity;
            set => _softCapacity = Mathf.Clamp(value, 1, _voices.Length);
        }

        private int _softCapacity;

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _voices.Length; i++)
                {
                    if (_voices[i].Active)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public AudioVoicePool(string name, Transform parent, int hardCapacity, AudioBusMixer mixer)
        {
            Name = name;
            _mixer = mixer;
            _voices = new Voice[Mathf.Max(1, hardCapacity)];
            _softCapacity = _voices.Length;

            for (int i = 0; i < _voices.Length; i++)
            {
                var go = new GameObject(name + " Voice " + i);
                go.transform.SetParent(parent, false);

                AudioSource source = go.AddComponent<AudioSource>();
                ConfigureSource(source);

                _voices[i] = new Voice { Source = source };
            }
        }

        /// <summary>
        /// Shared source setup.
        ///
        /// The rolloff numbers are PROTOTYPE-ONLY / INVENTED and they are unusual on
        /// purpose: min distance 8, max 60, LINEAR. The listener rides the camera, which in
        /// this game sits some ten metres above and behind the player, so a textbook
        /// logarithmic rolloff with a 1 m minimum would make the player's own gunshot
        /// quieter than an enemy standing next to them. An 8 m minimum means everything
        /// inside the player's immediate space is at full level and the falloff only starts
        /// to work at the edge of the arena, which is where it is actually wanted.
        /// </summary>
        private static void ConfigureSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 8f;
            source.maxDistance = 60f;
            source.dopplerLevel = 0f;      // a top-down shooter with Doppler sounds seasick
            source.bypassReverbZones = true;
            source.priority = 128;
        }

        /// <summary>
        /// Tries to play one cue. Returns false when the budget refused it, which the
        /// caller should count rather than retry.
        /// </summary>
        public bool Play(in AudioPlacement placement, AudioClip clip, Vector3 position, float gainScale, float priority)
        {
            if (clip == null)
            {
                return false;
            }

            float now = Time.unscaledTime;

            // Gate 1: retrigger window.
            if (placement.MinRetrigger > 0f
                && _lastPlayed.TryGetValue(placement.Sound, out float last)
                && now - last < placement.MinRetrigger)
            {
                RetriggerBlockedCount++;
                return false;
            }

            Voice voice = Acquire(placement, priority);
            if (voice == null)
            {
                DroppedCount++;
                LastDropped = placement.Sound;
                return false;
            }

            float pitch = placement.Pitch <= 0f ? 1f : placement.Pitch;
            if (placement.PitchJitter > 0f)
            {
                pitch *= 1f + _jitter.Range(-placement.PitchJitter, placement.PitchJitter);
            }

            AudioSource source = voice.Source;
            source.clip = clip;
            source.loop = false;
            source.pitch = pitch;
            source.spatialBlend = placement.Spatial;
            source.transform.position = position;

            voice.Sound = placement.Sound;
            voice.Bus = placement.Bus;
            voice.Priority = priority;
            voice.BaseVolume = Mathf.Clamp01(placement.Gain * gainScale);
            voice.Remaining = clip.length / Mathf.Max(0.05f, pitch) + 0.05f;
            voice.Age = 0f;
            voice.Active = true;

            source.volume = voice.BaseVolume * _mixer.Gain(placement.Bus);
            source.Play();

            _lastPlayed[placement.Sound] = now;

            int active = ActiveCount;
            if (active > PeakActive)
            {
                PeakActive = active;
            }

            return true;
        }

        /// <summary>
        /// Per-frame upkeep. Must be driven with UNSCALED delta and must be told about the
        /// pause, or a paused game silently ages out every voice it is holding.
        /// </summary>
        public void Tick(float unscaledDeltaTime, bool paused)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                Voice v = _voices[i];
                if (!v.Active)
                {
                    continue;
                }

                if (!paused)
                {
                    v.Remaining -= unscaledDeltaTime;
                    v.Age += unscaledDeltaTime;
                }

                if (v.Remaining <= 0f)
                {
                    v.Source.Stop();
                    v.Source.clip = null;
                    v.Active = false;
                    continue;
                }

                // Ducking is applied here rather than at Play, so a voice that is already
                // ringing when a landmark lands gets out of the way too. Ducking only the
                // sounds that start after the event would be inaudible in a busy frame,
                // which is the only frame that needs it.
                v.Source.volume = v.BaseVolume * _mixer.Gain(v.Bus);
            }
        }

        public void StopAll()
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i].Source.Stop();
                _voices[i].Source.clip = null;
                _voices[i].Active = false;
            }

            _lastPlayed.Clear();
        }

        public void ResetCounters()
        {
            DroppedCount = 0;
            StolenCount = 0;
            RetriggerBlockedCount = 0;
            PeakActive = 0;
            LastDropped = AudioSoundId.None;
        }

        /// <summary>One line for the F1 panel.</summary>
        public string Describe()
        {
            return string.Format(
                "{0}: {1}/{2} voices (peak {3}) · dropped {4} · stolen {5} · retrig {6}{7}",
                Name, ActiveCount, _softCapacity, PeakActive,
                DroppedCount, StolenCount, RetriggerBlockedCount,
                LastDropped == AudioSoundId.None ? "" : " · last cut " + LastDropped);
        }

        private Voice Acquire(in AudioPlacement placement, float priority)
        {
            Voice free = null;

            // Gate 2: how many copies of this exact sound are already ringing.
            int sameSound = 0;
            Voice oldestSame = null;

            Voice weakest = null;
            float weakestPriority = float.MaxValue;
            float weakestRemaining = float.MaxValue;

            // Only the first _softCapacity voices exist as far as this function is
            // concerned. Anything already playing above the line is left alone and simply
            // ages out - shrinking the budget must not cut a sound that is mid-flight.
            for (int i = 0; i < _softCapacity; i++)
            {
                Voice v = _voices[i];

                if (!v.Active)
                {
                    if (free == null)
                    {
                        free = v;
                    }

                    continue;
                }

                if (v.Sound == placement.Sound)
                {
                    sameSound++;
                    if (oldestSame == null || v.Age > oldestSame.Age)
                    {
                        oldestSame = v;
                    }
                }

                if (v.Priority < weakestPriority
                    || (Mathf.Approximately(v.Priority, weakestPriority) && v.Remaining < weakestRemaining))
                {
                    weakest = v;
                    weakestPriority = v.Priority;
                    weakestRemaining = v.Remaining;
                }
            }

            int maxInstances = Mathf.Max(1, placement.MaxInstances);
            if (sameSound >= maxInstances && oldestSame != null)
            {
                // Recycling a copy of the same sound is not a loss of information, so it
                // does not count as a steal and is not reported as one.
                oldestSame.Source.Stop();
                oldestSame.Active = false;
                return oldestSame;
            }

            if (free != null)
            {
                return free;
            }

            // Gate 3: steal, but only downwards. A tie goes to the incumbent - a sound
            // already playing has already been heard starting, and cutting it to start an
            // identically important one trades nothing for a click.
            if (weakest != null && weakestPriority < priority)
            {
                weakest.Source.Stop();
                weakest.Active = false;
                StolenCount++;
                return weakest;
            }

            return null;
        }
    }
}
