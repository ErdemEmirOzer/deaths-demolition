using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-021 · DEC-064 / DEC-065: the hybrid Japanese-cinematic score as four vertically
    /// layered loops, mixed by one parametric number.
    ///
    ///   BASE        the tonal bed. Always on; pulled back (not silenced) between rounds
    ///               and in menus, because a score that stops reads as a bug.
    ///   PERCUSSION  taiko / pulse. Enters at <see cref="PercussionAt"/> intensity.
    ///   TENSION     the dissonant high layer. Enters at <see cref="TensionAt"/>.
    ///   ACCENT      the Elite / Ultra / Special Weapon presence layer - a tier layer,
    ///               deliberately NOT part of the intensity ramp so a single Ultra on an
    ///               empty map still changes the score.
    ///
    /// <see cref="Intensity"/> is built from three things (same recipe as WP-002's
    /// tension, now owned here): round progress (slow, monotonic), alive-enemy pressure
    /// (fast) and the round-start surge (decays over seven seconds). Max of the first two
    /// rather than sum, so the bed never pins.
    ///
    /// Round break (RoundComplete / Upgrading): every layer but BASE fades out on the
    /// <see cref="PullbackSeconds"/> ramp and BASE drops to its calm level. Death: the same,
    /// lower. The mixer snapshot (RoundBreak / Menu) adds its own group-level pullback on
    /// top; both are gentle on purpose - together they are the "geri çekilme".
    /// </summary>
    public sealed class MusicDirector
    {
        public const int LayerCount = 4;
        public const int Base = 0;
        public const int Percussion = 1;
        public const int Tension = 2;
        public const int Accent = 3;

        private static readonly AudioSoundId[] s_layers =
        {
            AudioSoundId.MusicBase, AudioSoundId.MusicPercussion, AudioSoundId.MusicTension, AudioSoundId.MusicAccent
        };

        // ---- PROTOTYPE-ONLY / INVENTED tuning (DEC-065 makes the SHAPE binding, not the numbers)
        public const float PercussionAt = 0.15f;
        public const float TensionAt = 0.45f;
        private const float PressureEnemies = 14f;
        private const float SurgeSeconds = 7f;
        private const float IntensityRate = 0.5f;       // per second, both directions
        private const float LayerRiseSeconds = 1.2f;
        private const float LayerFallSeconds = 2.2f;
        public const float PullbackSeconds = 3.0f;
        private const float TierPollSeconds = 0.25f;

        /// <summary>Layer levels (0-1, before bus gain) by state; BASE first.</summary>
        private const float BaseFighting = 0.62f, BaseBreak = 0.34f, BaseMenu = 0.28f, BaseDead = 0.22f;

        private readonly AudioSource[] _sources = new AudioSource[LayerCount];
        private readonly float[] _current = new float[LayerCount];
        private readonly float[] _target = new float[LayerCount];
        private readonly AudioClipLibrary _library;
        private readonly AudioBusMixer _mixer;

        private RunPhase _phase = RunPhase.None;
        private float _surge;
        private float _tierPoll;
        private bool _tierPresent;
        private bool _specialActive;

        /// <summary>The 0-1 driver of the percussion and tension layers. F1 readout, test seam.</summary>
        public float Intensity { get; private set; }

        /// <summary>True while an Elite/Ultra is alive or a Special Weapon is in hand: the accent layer's driver.</summary>
        public bool AccentActive => _tierPresent || _specialActive;

        public bool Started { get; private set; }

        /// <summary>Test seam: pin the intensity instead of reading the run.</summary>
        public float? IntensityOverride { get; set; }

        /// <summary>Test seam: pin the accent driver.</summary>
        public bool? AccentOverride { get; set; }

        public MusicDirector(Transform root, AudioClipLibrary library, AudioBusMixer mixer, AudioMixerBridge bridge)
        {
            _library = library;
            _mixer = mixer;

            for (int i = 0; i < LayerCount; i++)
            {
                var go = new GameObject("Music " + s_layers[i]);
                go.transform.SetParent(root, false);

                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 0f;
                source.dopplerLevel = 0f;
                source.bypassReverbZones = true;
                source.bypassEffects = true;
                source.priority = 0;
                bridge?.RouteMusic(source);
                _sources[i] = source;
            }
        }

        /// <summary>Starts all four loops in lock-step (same start time, same length) so the layers stay in phrase.</summary>
        public void Start()
        {
            if (Started)
            {
                return;
            }

            for (int i = 0; i < LayerCount; i++)
            {
                AudioClip clip = _library.First(s_layers[i]);
                if (clip == null)
                {
                    continue;
                }

                _sources[i].clip = clip;
            }

            // PlayScheduled at one DSP time is the only way four loops stay sample-aligned;
            // Play() on four sources lands them on four different mixer blocks.
            double at = AudioSettings.dspTime + 0.1;
            for (int i = 0; i < LayerCount; i++)
            {
                if (_sources[i].clip != null)
                {
                    _sources[i].PlayScheduled(at);
                }
            }

            Started = true;
        }

        public void SetPhase(RunPhase phase)
        {
            _phase = phase;
            if (phase == RunPhase.Countdown)
            {
                _surge = Mathf.Max(_surge, 0.5f);
            }
        }

        public void OnRoundStarted()
        {
            _surge = 1f;
        }

        public void OnRoundCompleted()
        {
            _surge = 0f;
        }

        public float Level(int layer)
        {
            return layer >= 0 && layer < LayerCount ? _current[layer] : 0f;
        }

        public void Tick(float unscaledDeltaTime, bool paused)
        {
            if (paused)
            {
                return;
            }

            _surge = Mathf.Max(0f, _surge - unscaledDeltaTime / SurgeSeconds);

            _tierPoll -= unscaledDeltaTime;
            if (_tierPoll <= 0f)
            {
                _tierPoll = TierPollSeconds;
                PollTier();
            }

            ResolveIntensity(unscaledDeltaTime);
            ResolveTargets();

            bool pullback = _phase != RunPhase.Fighting && _phase != RunPhase.Countdown;
            float busGain = _mixer.Gain(AudioBus.Ambience);

            for (int i = 0; i < LayerCount; i++)
            {
                float seconds = _target[i] > _current[i]
                    ? LayerRiseSeconds
                    : (pullback ? PullbackSeconds : LayerFallSeconds);

                _current[i] = Mathf.MoveTowards(_current[i], _target[i], unscaledDeltaTime / Mathf.Max(0.05f, seconds));
                _sources[i].volume = _current[i] * busGain;
            }
        }

        private void PollTier()
        {
            if (AccentOverride.HasValue)
            {
                _tierPresent = AccentOverride.Value;
                _specialActive = false;
                return;
            }

            bool tier = false;
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                ZombieEnemy z = live[i];
                if (z != null && z.IsAlive && (z.Tier == EnemyTier.Elite || z.Tier == EnemyTier.Ultra))
                {
                    tier = true;
                    break;
                }
            }

            _tierPresent = tier;
            _specialActive = WeaponEvents.Special.Phase == SpecialWeaponPhase.Active;
        }

        private void ResolveIntensity(float dt)
        {
            float target;
            if (IntensityOverride.HasValue)
            {
                target = Mathf.Clamp01(IntensityOverride.Value);
            }
            else if (_phase == RunPhase.Fighting || _phase == RunPhase.Countdown)
            {
                RunHudState state = RunEvents.Current;
                float progress = Mathf.Clamp01(state.RoundProgress01);
                float pressure = Mathf.Clamp01(state.AliveEnemies / PressureEnemies);
                target = Mathf.Max(0.20f + 0.55f * progress, pressure * 0.9f);
                target = Mathf.Clamp01(target + _surge * 0.35f);
            }
            else
            {
                target = 0f;
            }

            Intensity = Mathf.MoveTowards(Intensity, target, IntensityRate * dt);
        }

        private void ResolveTargets()
        {
            switch (_phase)
            {
                case RunPhase.Fighting:
                case RunPhase.Countdown:
                    _target[Base] = BaseFighting;
                    // Ramps rather than gates: a layer that snaps in on a threshold is a
                    // seam; one that rises over 0.2 of intensity is a swell.
                    _target[Percussion] = Mathf.Clamp01((Intensity - PercussionAt) / 0.25f) * 0.70f;
                    _target[Tension] = Mathf.Clamp01((Intensity - TensionAt) / 0.30f) * 0.62f;
                    _target[Accent] = AccentActive ? 0.66f : 0f;
                    break;

                case RunPhase.RoundComplete:
                case RunPhase.Upgrading:
                    _target[Base] = BaseBreak;
                    _target[Percussion] = 0f;
                    _target[Tension] = 0f;
                    _target[Accent] = 0f;
                    break;

                case RunPhase.Dead:
                    _target[Base] = BaseDead;
                    _target[Percussion] = 0f;
                    _target[Tension] = 0f;
                    _target[Accent] = 0f;
                    break;

                default:
                    _target[Base] = BaseMenu;
                    _target[Percussion] = 0f;
                    _target[Tension] = 0f;
                    _target[Accent] = 0f;
                    break;
            }
        }

        public void StopAll()
        {
            for (int i = 0; i < LayerCount; i++)
            {
                _sources[i].Stop();
                _current[i] = 0f;
                _target[i] = 0f;
            }

            Started = false;
            Intensity = 0f;
            _surge = 0f;
        }

        public string Describe()
        {
            return string.Format(
                "{0} · intensity {1:0.00} (surge {2:0.00}) · accent {3} · base {4:0.00} perc {5:0.00} tens {6:0.00} acc {7:0.00}",
                _phase, Intensity, _surge, AccentActive ? (_specialActive ? "special" : "tier") : "off",
                _current[Base], _current[Percussion], _current[Tension], _current[Accent]);
        }
    }
}
