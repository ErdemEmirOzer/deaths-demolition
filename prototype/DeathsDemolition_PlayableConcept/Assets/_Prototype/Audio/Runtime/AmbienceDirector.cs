using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// The six ambience beds, their balance per run phase, and the scatter of one-shots
    /// over the top.
    ///
    /// WHAT THE REQUEST ASKS FOR, AND HOW EACH SENTENCE IS ANSWERED
    ///   "hafif rüzgâr, yaprak hareketi, uzak su"      three beds, always running, at
    ///                                                 volumes that move with the phase.
    ///   "seyrek metal/ahşap sesleri"                  the scatterer below: one one-shot
    ///                                                 every 9-24 seconds, never twice the
    ///                                                 same in a row, placed in the world
    ///                                                 8-22 m from the listener.
    ///   "sessiz ve boş saray hissi"                   the palace tone bed, which is the
    ///                                                 quietest thing in the game and the
    ///                                                 only one that gets LOUDER when
    ///                                                 everything else stops.
    ///   "round başlangıcında gerilim katmanı yükselsin"
    ///                                                 two tension beds cross-fading on a
    ///                                                 single 0-1 intensity, plus a surge
    ///                                                 fired by RoundStarted so the rise
    ///                                                 happens at the start rather than
    ///                                                 waiting for the kill count.
    ///   "round sonunda upgrade ekranında atmosfer sakinleşsin"
    ///                                                 the Upgrading preset: tension to
    ///                                                 zero, wind down a third, water and
    ///                                                 palace UP. The calm is not a new bed,
    ///                                                 it is the absence of the tension ones
    ///                                                 and it fades in slowly on purpose.
    ///
    /// WHY VERTICAL LAYERING AND NOT A SECOND TRACK
    /// Six loops running continuously at independent volumes cost six voices and no CPU,
    /// and any transition between any two states is a volume ramp that cannot click, cannot
    /// arrive late and cannot be interrupted half way. A horizontal re-sequencer would need
    /// transition points, and this game changes state on a kill count nobody can predict.
    /// </summary>
    public sealed class AmbienceDirector
    {
        private const int BedCount = 6;

        private static readonly AudioSoundId[] s_beds =
        {
            AudioSoundId.AmbWind,
            AudioSoundId.AmbLeaves,
            AudioSoundId.AmbWater,
            AudioSoundId.AmbPalaceTone,
            AudioSoundId.AmbTensionLow,
            AudioSoundId.AmbTensionHigh
        };

        private static readonly AudioSoundId[] s_scatter =
        {
            AudioSoundId.AmbWoodCreak,
            AudioSoundId.AmbMetalRing,
            AudioSoundId.AmbBambooKnock,
            AudioSoundId.AmbStoneSettle
        };

        private const int Wind = 0;
        private const int Leaves = 1;
        private const int Water = 2;
        private const int Palace = 3;
        private const int TensionLow = 4;
        private const int TensionHigh = 5;

        private readonly AudioSource[] _sources = new AudioSource[BedCount];
        private readonly AudioLowPassFilter[] _filters = new AudioLowPassFilter[BedCount];
        private readonly float[] _current = new float[BedCount];
        private readonly float[] _target = new float[BedCount];

        private readonly AudioClipLibrary _library;
        private readonly AudioBusMixer _mixer;
        private readonly AudioVoicePool _worldPool;
        private readonly Transform _root;

        private SynthRandom _rng = new SynthRandom(0x0A1B2C3D);

        private RunPhase _phase = RunPhase.None;
        private float _scatterCountdown;
        private int _lastScatter = -1;

        /// <summary>0-1 intensity the two tension beds are cross-faded on. F1 readout.</summary>
        public float Tension { get; private set; }

        /// <summary>Extra tension injected by a round start, decaying to zero. F1 readout.</summary>
        public float Surge { get; private set; }

        /// <summary>Ambient one-shots fired since the last reset. F1 readout.</summary>
        public int ScatterCount { get; private set; }

        public bool Started { get; private set; }

        // ---- PROTOTYPE-ONLY / INVENTED tuning --------------------------------

        /// <summary>Seconds between ambient one-shots, before the phase multiplier.</summary>
        private const float ScatterMin = 9f;
        private const float ScatterMax = 24f;

        /// <summary>Bed volume change per second, up and down. Down is slower: calm arrives, it does not cut.</summary>
        private const float RiseRate = 0.45f;
        private const float FallRate = 0.30f;

        /// <summary>Tension release is the one fast ramp. A round that is over is over.</summary>
        private const float TensionFallRate = 0.85f;

        /// <summary>Enemies alive that count as maximum pressure.</summary>
        private const float PressureEnemies = 14f;

        /// <summary>Seconds the round-start surge takes to decay away.</summary>
        private const float SurgeSeconds = 7f;

        public AmbienceDirector(Transform root, AudioClipLibrary library, AudioBusMixer mixer, AudioVoicePool worldPool)
        {
            _root = root;
            _library = library;
            _mixer = mixer;
            _worldPool = worldPool;

            for (int i = 0; i < BedCount; i++)
            {
                var go = new GameObject("Ambience " + s_beds[i]);
                go.transform.SetParent(root, false);

                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;    // beds are the room, not an object in it
                source.volume = 0f;
                source.dopplerLevel = 0f;
                source.bypassReverbZones = true;
                source.priority = 0;         // never virtualised by Unity, whatever else happens
                _sources[i] = source;

                // The duck is a filter sweep as well as a level drop. A bed that only gets
                // quieter still occupies its band; a bed that also gets darker genuinely
                // leaves room, and the recovery reads as the world coming back rather than
                // as a fader moving.
                AudioLowPassFilter filter = go.AddComponent<AudioLowPassFilter>();
                filter.cutoffFrequency = 22000f;
                filter.lowpassResonanceQ = 1f;
                _filters[i] = filter;
            }

            _scatterCountdown = ScatterMin;
        }

        /// <summary>Starts every bed. Safe to call twice; the beds are only ever started once.</summary>
        public void Start()
        {
            if (Started)
            {
                return;
            }

            for (int i = 0; i < BedCount; i++)
            {
                AudioClip clip = _library.First(s_beds[i]);
                if (clip == null)
                {
                    continue;
                }

                _sources[i].clip = clip;

                // Each bed starts at a different point in its own loop. Six beds all
                // starting at sample zero would put every modulation cycle in phase, and
                // the composite would then repeat exactly every six seconds - the one thing
                // the whole bed design exists to prevent.
                _sources[i].time = _rng.Range(0f, Mathf.Max(0.01f, clip.length - 0.05f));
                _sources[i].Play();
            }

            Started = true;
        }

        public void SetPhase(RunPhase phase)
        {
            _phase = phase;

            if (phase == RunPhase.Countdown)
            {
                // The countdown is the wind-up. Half a surge here means the tension is
                // already climbing when the round-start bell lands on top of it.
                Surge = Mathf.Max(Surge, 0.5f);
            }
        }

        /// <summary>The round actually started. This is the request's tension rise, at its loudest.</summary>
        public void OnRoundStarted()
        {
            Surge = 1f;
        }

        /// <summary>The round ended. Tension is released fast; the calm beds take over.</summary>
        public void OnRoundCompleted()
        {
            Surge = 0f;
            Tension = 0f;
        }

        public void Tick(float unscaledDeltaTime, bool paused)
        {
            if (paused)
            {
                return;
            }

            Surge = Mathf.Max(0f, Surge - unscaledDeltaTime / SurgeSeconds);

            ResolveTargets();

            for (int i = 0; i < BedCount; i++)
            {
                bool tension = i >= TensionLow;
                float rate = _target[i] > _current[i]
                    ? RiseRate
                    : (tension ? TensionFallRate : FallRate);

                _current[i] = Mathf.MoveTowards(_current[i], _target[i], rate * unscaledDeltaTime);

                float busGain = _mixer.Gain(AudioBus.Ambience);
                _sources[i].volume = _current[i] * busGain;

                float duck = _mixer.DuckLevel(AudioBus.Ambience);
                _filters[i].cutoffFrequency = Mathf.Lerp(600f, 22000f, duck * duck);
            }

            TickScatter(unscaledDeltaTime);
        }

        /// <summary>
        /// The per-phase bed balance. Every number here is PROTOTYPE-ONLY / INVENTED; what
        /// is not invented is the SHAPE - wind and leaves lead while fighting, water and
        /// palace lead while not, and the palace tone is the only bed that rises as the
        /// game gets quieter.
        /// </summary>
        private void ResolveTargets()
        {
            switch (_phase)
            {
                case RunPhase.Fighting:
                    _target[Wind] = 0.62f;
                    _target[Leaves] = 0.34f;
                    _target[Water] = 0.18f;
                    _target[Palace] = 0.42f;
                    ResolveTension();
                    break;

                case RunPhase.Countdown:
                    _target[Wind] = 0.58f;
                    _target[Leaves] = 0.30f;
                    _target[Water] = 0.20f;
                    _target[Palace] = 0.50f;
                    ResolveTension();
                    break;

                case RunPhase.RoundComplete:
                    _target[Wind] = 0.48f;
                    _target[Leaves] = 0.32f;
                    _target[Water] = 0.30f;
                    _target[Palace] = 0.58f;
                    _target[TensionLow] = 0f;
                    _target[TensionHigh] = 0f;
                    Tension = 0f;
                    break;

                case RunPhase.Upgrading:
                    // The calm. Wind down a third, water and palace up, no tension at all.
                    // Read against the Fighting row: only two numbers went UP, and both of
                    // them are the ones with no transient in them anywhere.
                    _target[Wind] = 0.36f;
                    _target[Leaves] = 0.26f;
                    _target[Water] = 0.34f;
                    _target[Palace] = 0.72f;
                    _target[TensionLow] = 0f;
                    _target[TensionHigh] = 0f;
                    Tension = 0f;
                    break;

                case RunPhase.Dead:
                    _target[Wind] = 0.20f;
                    _target[Leaves] = 0.10f;
                    _target[Water] = 0.12f;
                    _target[Palace] = 0.85f;
                    _target[TensionLow] = 0f;
                    _target[TensionHigh] = 0f;
                    Tension = 0f;
                    break;

                default: // None - menus, selection, results
                    _target[Wind] = 0.55f;
                    _target[Leaves] = 0.30f;
                    _target[Water] = 0.24f;
                    _target[Palace] = 0.60f;
                    _target[TensionLow] = 0f;
                    _target[TensionHigh] = 0f;
                    Tension = 0f;
                    break;
            }
        }

        /// <summary>
        /// The single 0-1 number both tension beds hang off.
        ///
        /// It is built from three things, and the mix of the three is the point:
        ///   progress   how much of the round's pool is dead. Slow, monotonic, and the
        ///              reason a round feels like it is going somewhere.
        ///   pressure   how many enemies are alive right now. Fast, and the reason being
        ///              surrounded feels different from mopping up.
        ///   surge      the round-start kick, decaying over seven seconds.
        ///
        /// Progress alone would make the first minute of every round identical; pressure
        /// alone would make a cleared round silent even at 95% completion. Taking the
        /// maximum of the two rather than the sum keeps the bed from ever pinning.
        /// </summary>
        private void ResolveTension()
        {
            RunHudState state = RunEvents.Current;

            float progress = Mathf.Clamp01(state.RoundProgress01);
            float pressure = Mathf.Clamp01(state.AliveEnemies / PressureEnemies);

            float target = Mathf.Max(0.25f + 0.55f * progress, pressure * 0.9f);
            target = Mathf.Clamp01(target + Surge * 0.35f);

            // Tension moves slowly by itself - the ramps above do the rest - so that a
            // single enemy wandering off does not audibly change the music.
            Tension = Mathf.MoveTowards(Tension, target, 0.5f * Time.unscaledDeltaTime);

            // Cross-fade: LOW owns the bottom half of the range, HIGH the top. They
            // overlap through the middle because HIGH contains LOW's drone, so the sum
            // never dips.
            _target[TensionLow] = Mathf.Clamp01(Tension * 2f) * 0.55f;
            _target[TensionHigh] = Mathf.Clamp01((Tension - 0.45f) * 1.9f) * 0.65f;
        }

        /// <summary>
        /// The one-shot scatterer.
        ///
        /// Two rules keep it from becoming noticeable in the wrong way: it never plays the
        /// same one-shot twice running, and it slows DOWN during a fight. A creak in the
        /// middle of a firefight is either masked (wasted) or heard as a gameplay event
        /// (worse than wasted); the same creak thirty seconds into an upgrade screen is the
        /// entire "empty palace" brief in one sound.
        /// </summary>
        private void TickScatter(float unscaledDeltaTime)
        {
            _scatterCountdown -= unscaledDeltaTime;
            if (_scatterCountdown > 0f)
            {
                return;
            }

            float multiplier = _phase == RunPhase.Fighting ? 1.6f
                : _phase == RunPhase.Upgrading || _phase == RunPhase.None ? 0.7f
                : 1f;

            _scatterCountdown = _rng.Range(ScatterMin, ScatterMax) * multiplier;

            int pick = _rng.RangeInt(0, s_scatter.Length);
            if (pick == _lastScatter)
            {
                pick = (pick + 1 + _rng.RangeInt(0, s_scatter.Length - 1)) % s_scatter.Length;
            }

            _lastScatter = pick;

            AudioSoundId sound = s_scatter[pick];
            AudioClip clip = _library.Next(sound, out int _);
            if (clip == null || _worldPool == null)
            {
                return;
            }

            // Somewhere out there. A ring rather than a disc, because a one-shot at the
            // listener's own position is not ambience, it is an event.
            float angle = _rng.Range(0f, Mathf.PI * 2f);
            float distance = _rng.Range(9f, 22f);
            Vector3 origin = _root != null ? _root.position : Vector3.zero;

            // The AudioListener rides the gameplay camera (CameraRigFactory puts it there),
            // so the scatter ring has to be centred on the camera and not on the player, or
            // half of it would land behind the listener's own back at zero distance.
            if (DD.Prototype.Feedback.FeedbackCameraRef.TryGetPosition(out Vector3 listener))
            {
                origin = listener;
            }

            Vector3 position = origin + new Vector3(
                Mathf.Cos(angle) * distance,
                _rng.Range(-1f, 5f),
                Mathf.Sin(angle) * distance);

            // Quieter during a fight even after the slower rate, because the whole point of
            // the bed family is that it never competes.
            float gain = _phase == RunPhase.Fighting ? 0.35f : 0.62f;

            AudioPlacement placement = AudioMixPlan.AmbientOneShot(sound, gain);
            if (_worldPool.Play(placement, clip, position, 1f, placement.Priority))
            {
                ScatterCount++;
            }
        }

        public void StopAll()
        {
            for (int i = 0; i < BedCount; i++)
            {
                _sources[i].Stop();
                _current[i] = 0f;
                _target[i] = 0f;
            }

            Started = false;
            Tension = 0f;
            Surge = 0f;
        }

        /// <summary>One line for the F1 panel.</summary>
        public string Describe()
        {
            return string.Format(
                "{0} · tension {1:0.00} (surge {2:0.00}) · wind {3:0.00} leaf {4:0.00} water {5:0.00} palace {6:0.00} · lo {7:0.00} hi {8:0.00} · scatter {9}",
                _phase, Tension, Surge,
                _current[Wind], _current[Leaves], _current[Water], _current[Palace],
                _current[TensionLow], _current[TensionHigh], ScatterCount);
        }
    }
}
