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
        private const float BaseFighting = 0.62f, BaseBreak = 0.34f;   // WP-057: menu / dead levels moved to the phase loops

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

        // ---- WP-057 · music v2: threat-layer driver, stingers, menu / results music
        /// <summary>Layer levels the WP-033 threat layer (0–3, hysteresis inside ThreatDirector) guarantees while fighting. PROTOTYPE-ONLY.</summary>
        private static readonly float[] s_percByLayer = { 0f, 0.55f, 0.70f, 0.72f };
        private static readonly float[] s_tensByLayer = { 0f, 0f, 0.50f, 0.66f };
        private const float StingerDuck = 0.55f;
        private const float StingerDuckRelease = 1.5f;
        private const float PhaseLoopRiseSeconds = 2.5f;
        private const float PhaseLoopFallSeconds = 1.8f;
        private const float MenuLevel = 0.55f, ResultsLevel = 0.5f;

        private AudioSource _stinger;
        private AudioSource _phaseLoop;
        private AudioSoundId _phaseLoopSound = AudioSoundId.None;
        private float _phaseLoopLevel, _phaseLoopTarget;
        private float _stingerHold;           // seconds the layer duck stays at StingerDuck
        private float _stingerDuckLevel = 1f;
        private bool _afterRun;               // results state: RunEnded / Dead until the next RunStarted

        /// <summary>Stingers played (test / F1 readout).</summary>
        public int StingerCount { get; private set; }
        public AudioSoundId LastStinger { get; private set; }
        /// <summary>The menu / results loop currently faded in (None while running).</summary>
        public AudioSoundId PhaseLoop => _phaseLoopSound;
        public float PhaseLoopLevel => _phaseLoopLevel;
        /// <summary>Threat layer (0–3) the driver saw on the last tick.</summary>
        public int ThreatLayer { get; private set; }

        /// <summary>The 0-1 driver of the percussion and tension layers. F1 readout, test seam.</summary>
        public float Intensity { get; private set; }

        /// <summary>True while an Elite/Ultra is alive or a Special Weapon is in hand: the accent layer's driver.</summary>
        public bool AccentActive => _tierPresent || _specialActive;

        public bool Started { get; private set; }

        /// <summary>Test seam: pin the intensity instead of reading the run.</summary>
        public float? IntensityOverride { get; set; }

        /// <summary>Test seam: pin the accent driver.</summary>
        public bool? AccentOverride { get; set; }

        /// <summary>Test seam (WP-057): pin the threat layer (0–3) instead of reading ThreatMusicFeed.</summary>
        public int? ThreatLayerOverride { get; set; }

        /// <summary>Current layer duck from a stinger (1 = none). Test readout.</summary>
        public float StingerDuckLevel => _stingerDuckLevel;

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

            _stinger = MakeSource(root, "Music Stinger", false, bridge);
            _phaseLoop = MakeSource(root, "Music Phase Loop", true, bridge);
        }

        private static AudioSource MakeSource(Transform root, string name, bool loop, AudioMixerBridge bridge)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.dopplerLevel = 0f;
            source.bypassReverbZones = true;
            source.bypassEffects = true;
            source.priority = 0;
            bridge?.RouteMusic(source);
            return source;
        }

        /// <summary>WP-057: a one-shot phrase over the layers (round break, Special pickup, death, run end). The layers duck to <see cref="StingerDuck"/> for its length.</summary>
        public bool PlayStinger(AudioSoundId sound)
        {
            AudioClip clip = _library.First(sound);
            if (clip == null || _stinger == null)
            {
                return false;
            }

            _stinger.Stop();
            _stinger.clip = clip;
            _stinger.volume = 0.9f * _mixer.Gain(AudioBus.Music);
            _stinger.Play();
            _stingerHold = Mathf.Max(0.2f, clip.length - StingerDuckRelease * 0.5f);
            _stingerDuckLevel = StingerDuck;
            StingerCount++;
            LastStinger = sound;
            return true;
        }

        /// <summary>WP-057: RunEnded / death → results state until the next RunStarted (results loop instead of the menu loop).</summary>
        public void OnRunEnded(bool died)
        {
            if (_afterRun)
            {
                return;   // death already ended the run (RunEnded follows the death frame); one stinger
            }

            _afterRun = true;
            PlayStinger(died ? AudioSoundId.StingerDeath : AudioSoundId.StingerRunEnd);
        }

        public void OnSpecialPickup()
        {
            if (_phase == RunPhase.Fighting || _phase == RunPhase.Countdown)
            {
                PlayStinger(AudioSoundId.StingerSpecialPickup);
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

        public void OnRunStarted()
        {
            _afterRun = false;
        }

        public void OnRoundStarted()
        {
            _surge = 1f;
        }

        public void OnRoundCompleted()
        {
            _surge = 0f;
            PlayStinger(AudioSoundId.StingerRoundBreak);   // WP-057
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
            float busGain = _mixer.Gain(AudioBus.Music);   // WP-057: the score has its own software bus (duck + user volume)

            // Stinger duck: hold, then release over StingerDuckRelease.
            if (_stingerHold > 0f)
            {
                _stingerHold -= unscaledDeltaTime;
            }
            else
            {
                _stingerDuckLevel = Mathf.MoveTowards(_stingerDuckLevel, 1f, unscaledDeltaTime / StingerDuckRelease);
            }

            for (int i = 0; i < LayerCount; i++)
            {
                float seconds = _target[i] > _current[i]
                    ? LayerRiseSeconds
                    : (pullback ? PullbackSeconds : LayerFallSeconds);

                _current[i] = Mathf.MoveTowards(_current[i], _target[i], unscaledDeltaTime / Mathf.Max(0.05f, seconds));
                _sources[i].volume = _current[i] * busGain * _stingerDuckLevel;
            }

            if (_stinger != null && _stinger.isPlaying)
            {
                _stinger.volume = 0.9f * busGain;
            }

            TickPhaseLoop(unscaledDeltaTime, busGain);
        }

        /// <summary>
        /// WP-057: menu and results music are their own loops, cross-faded against the run layers
        /// (which fall to 0 outside a run instead of idling at a low base level).
        /// </summary>
        private void TickPhaseLoop(float dt, float busGain)
        {
            AudioSoundId want = AudioSoundId.None;
            float level = 0f;
            if (_phase == RunPhase.None || _phase == RunPhase.Dead)
            {
                want = _afterRun || _phase == RunPhase.Dead ? AudioSoundId.MusicResults : AudioSoundId.MusicMenu;
                level = want == AudioSoundId.MusicMenu ? MenuLevel : ResultsLevel;
            }

            if (want != _phaseLoopSound)
            {
                // Fade the old loop out first; swap the clip when it is silent.
                _phaseLoopTarget = 0f;
                if (_phaseLoopLevel <= 0.001f)
                {
                    _phaseLoop.Stop();
                    _phaseLoopSound = want;
                    if (want != AudioSoundId.None)
                    {
                        AudioClip clip = _library.First(want);
                        if (clip != null)
                        {
                            _phaseLoop.clip = clip;
                            _phaseLoop.time = 0f;
                            _phaseLoop.Play();
                        }
                    }
                }
            }
            else
            {
                _phaseLoopTarget = level;
            }

            float seconds = _phaseLoopTarget > _phaseLoopLevel ? PhaseLoopRiseSeconds : PhaseLoopFallSeconds;
            _phaseLoopLevel = Mathf.MoveTowards(_phaseLoopLevel, _phaseLoopTarget, dt / seconds);
            _phaseLoop.volume = _phaseLoopLevel * busGain * _stingerDuckLevel;
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

            _tierPresent = tier || ThreatMusicFeed.Accent;   // WP-033: strong version present per the threat director
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
                // WP-033 threat director (WP-021B binding): encirclement / attack pressure /
                // low health raise the score; the feed is the loudest voice, never the only one.
                target = Mathf.Max(target, Mathf.Clamp01(ThreatMusicFeed.Intensity));
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
                {
                    // Ramps rather than gates: a layer that snaps in on a threshold is a
                    // seam; one that rises over 0.2 of intensity is a swell. WP-057: the WP-033
                    // threat layer (0–3, hysteresis in ThreatDirector) is a FLOOR under the ramp,
                    // so a layer never drops while the director still says "surrounded".
                    int layer = ThreatLayerOverride.HasValue ? Mathf.Clamp(ThreatLayerOverride.Value, 0, 3) : IntensityOverride.HasValue ? 0 : Mathf.Clamp(ThreatMusicFeed.Layer, 0, 3);
                    ThreatLayer = layer;
                    _target[Base] = layer >= 3 ? 0.70f : BaseFighting;
                    _target[Percussion] = Mathf.Max(Mathf.Clamp01((Intensity - PercussionAt) / 0.25f) * 0.70f, s_percByLayer[layer]);
                    _target[Tension] = Mathf.Max(Mathf.Clamp01((Intensity - TensionAt) / 0.30f) * 0.62f, s_tensByLayer[layer]);
                    _target[Accent] = AccentActive ? (layer >= 3 ? 0.76f : 0.66f) : 0f;
                    break;
                }

                case RunPhase.RoundComplete:
                case RunPhase.Upgrading:
                    _target[Base] = BaseBreak;
                    _target[Percussion] = 0f;
                    _target[Tension] = 0f;
                    _target[Accent] = 0f;
                    break;

                case RunPhase.Dead:
                default:
                    // WP-057: menus / results / death have their own loops (TickPhaseLoop); the run layers leave.
                    _target[Base] = 0f;
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

            if (_stinger != null) _stinger.Stop();
            if (_phaseLoop != null) { _phaseLoop.Stop(); _phaseLoopLevel = 0f; _phaseLoopSound = AudioSoundId.None; }
            _stingerDuckLevel = 1f; _stingerHold = 0f;
            Started = false;
            Intensity = 0f;
            _surge = 0f;
        }

        public string Describe()
        {
            return string.Format(
                "{0} · intensity {1:0.00} (surge {2:0.00}, threat L{8}) · accent {3} · base {4:0.00} perc {5:0.00} tens {6:0.00} acc {7:0.00} · loop {9} {10:0.00} · stingers {11}",
                _phase, Intensity, _surge, AccentActive ? (_specialActive ? "special" : "tier") : "off",
                _current[Base], _current[Percussion], _current[Tension], _current[Accent], ThreatLayer, _phaseLoopSound, _phaseLoopLevel, StingerCount);
        }
    }
}
