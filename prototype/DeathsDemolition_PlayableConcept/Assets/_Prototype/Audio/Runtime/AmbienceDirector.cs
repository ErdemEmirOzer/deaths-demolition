using System.Collections.Generic;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// The ambience beds, their balance per run phase and per REGION, and the scatter of
    /// one-shots over the top.
    ///
    /// WP-021 (DEC-066, DEC-068): six beds, vertically layered -
    ///   COMMON     the after-rain atmosphere the whole map shares (drip, wind, bamboo,
    ///              distant water). Always running; only its level moves with the phase.
    ///   CENTER / NORTH / EAST / SOUTH / WEST
    ///              one bed per AP-005 region. Exactly one is the target at a time,
    ///              chosen from the player's position through <see cref="AudioRegionMap"/>;
    ///              the others fade out. The cross-fade is <see cref="CrossfadeSeconds"/>
    ///              (≥ 2 s per DEC-066) and it is a level ramp, so a player pacing on a
    ///              boundary hears a slow blend and never a switch.
    ///
    /// Tension no longer lives here: DEC-064/065 make that the music's job
    /// (<see cref="MusicDirector"/>), and a bed that duplicated it would fight the layers.
    /// The phase shaping that remains is the WP-002 brief - louder and more present on
    /// the upgrade screen, ducked and darker in a fight, quiet after death.
    ///
    /// WHY VERTICAL LAYERING AND NOT A SECOND TRACK
    /// Six loops at independent volumes cost six voices and no CPU, and any transition
    /// between any two states is a ramp that cannot click, arrive late or be interrupted.
    /// </summary>
    public sealed class AmbienceDirector
    {
        public const int BedCount = 6;
        public const int Common = 0;

        /// <summary>DEC-066: region cross-fade. PROTOTYPE-ONLY value above the 2 s floor.</summary>
        public const float CrossfadeSeconds = 2.5f;

        private static readonly AudioSoundId[] s_beds =
        {
            AudioSoundId.AmbCommon,
            AudioSoundId.AmbRegionCenter,
            AudioSoundId.AmbRegionNorth,
            AudioSoundId.AmbRegionEast,
            AudioSoundId.AmbRegionSouth,
            AudioSoundId.AmbRegionWest
        };

        private static readonly AudioSoundId[] s_scatter =
        {
            AudioSoundId.AmbWoodCreak,
            AudioSoundId.AmbMetalRing,
            AudioSoundId.AmbBambooKnock,
            AudioSoundId.AmbStoneSettle,
            AudioSoundId.AmbDrip,      // v003: after-rain drips, the most frequent scatter
            AudioSoundId.AmbDrip
        };

        /// <summary>WP-048 Look B: the wet set - slate drips join the ring, thunder has its own slow clock.</summary>
        private static readonly AudioSoundId[] s_scatterWet =
        {
            AudioSoundId.AmbWoodCreak,
            AudioSoundId.AmbMetalRing,
            AudioSoundId.AmbBambooKnock,
            AudioSoundId.AmbStoneSettle,
            AudioSoundId.AmbDrip,
            AudioSoundId.AmbDripSlate,
            AudioSoundId.AmbDrip,
            AudioSoundId.AmbDripSlate
        };

        /// <summary>
        /// WP-048 · Look B ("wet"): distant thunder every <see cref="ThunderMin"/>–<see cref="ThunderMax"/> s,
        /// slate drips in the scatter ring, and a koi-pond water bed anchored at the pond (3D, distance-faded).
        /// On: WP-029 chose Look B "Islak Arduvaz" (B4, 2026-09-17) - the wet after-rain palette is the shipped look
        /// (WP-048B). A future look switch flips this from its own code; nothing else writes it.
        /// PROTOTYPE-ONLY / INVENTED timings.
        /// </summary>
        public static bool WetLook = true;
        public const float ThunderMin = 40f;
        public const float ThunderMax = 90f;
        private float _thunderCountdown = 25f;
        private AudioSource _pond;
        private float _pondLevel;
        public int ThunderCount { get; private set; }

        private readonly AfterRainAmbience _afterRain;

        private System.Func<Vector3> _listenerAt;

        /// <summary>The ear the world voices are heard from (the player-anchored listener, ADR-021-2).</summary>
        public void SetListener(System.Func<Vector3> listener) => _listenerAt = listener;

        /// <summary>WP-072 · DEC-102 post-rain sunny courtyard layers (test / F1).</summary>
        public AfterRainAmbience AfterRain => _afterRain;
        public bool PondPlaying => _pond != null && _pond.isPlaying;

        private float _healClock;
        /// <summary>WP-065E: beds found stopped under a started ambience and restarted.</summary>
        public int BedRestarts { get; private set; }

        // ---- WP-065C · fūrin wind chimes (AP-017 Phase 2b, eaves): wind-driven, positional, nearest only
        // Numbers: design note (asset-production/AP-011/v014/FURIN-DESIGN.md) — glass edo-fūrin, not iron nanbu.
        /// <summary>Wind below this is still air: the chimes hang silent.</summary>
        public const float FurinWindThreshold = 0.15f;
        /// <summary>Seconds between tinkle clusters just above the threshold / in full wind (× 0.8–1.3 jitter, smoothstep on the wind).</summary>
        public const float FurinIntervalCalm = 22f, FurinIntervalGale = 3f;
        /// <summary>A chime is heard only within this range of the player (a small bell: its own space, not the courtyard's).</summary>
        public const float FurinRange = 12f;
        /// <summary>A gust (wind up ≥ 0.2 within ~1 s) rings the chime at once, at most every 1.5 s.</summary>
        public const float FurinGustRise = 0.2f, FurinGustCooldown = 1.5f;
        private float _furinCountdown = 3f, _furinWindLag, _furinLagClock, _furinGustBlock;
        /// <summary>Test seam: overrides WorldWind.Strength for the chime clock.</summary>
        public static float? FurinWindOverride;
        public int FurinCount { get; private set; }
        public float PondLevel => _pondLevel;

        /// <summary>
        /// v003 (ADR-021-2 spatial depth): one <see cref="AudioReverbZone"/> per region,
        /// centred on the polygon, sized to its extent. World SFX voices join these
        /// (bypass off); the player's hands, UI, beds and music stay dry.
        /// Presets are Unity's, chosen for the region's material: courtyard stone → Stoneroom
        /// (short, hard), wooden corridor → Hallway, bamboo grove → Forest (dense, damped),
        /// pond → Plain (open), cliff terrace → Mountains (sparse, distant).
        /// </summary>
        private static readonly AudioReverbPreset[] s_regionReverb =
        {
            AudioReverbPreset.Stoneroom, AudioReverbPreset.Hallway, AudioReverbPreset.Forest, AudioReverbPreset.Plain, AudioReverbPreset.Mountains
        };

        private readonly AudioReverbZone[] _reverb = new AudioReverbZone[5];

        /// <summary>Reverb zones built (0 without a region map). Test readout.</summary>
        public int ReverbZoneCount { get; private set; }

        public AudioReverbZone ReverbZone(AudioRegion region)
        {
            return region == AudioRegion.None ? null : _reverb[(int)region];
        }

        private readonly AudioSource[] _sources = new AudioSource[BedCount];
        private readonly AudioLowPassFilter[] _filters = new AudioLowPassFilter[BedCount];
        private readonly float[] _current = new float[BedCount];
        private readonly float[] _target = new float[BedCount];
        private readonly float[] _fadeFrom = new float[BedCount];   // WP-021C: last target a bed was given

        private readonly float[] _fadeRate = new float[BedCount];   // WP-021C: per-bed rate fixed at that change

        private readonly AudioClipLibrary _library;
        private readonly AudioBusMixer _mixer;
        private readonly AudioVoicePool _worldPool;
        private readonly Transform _root;
        private readonly AudioRegionMap _map;

        private SynthRandom _rng = new SynthRandom(0x0A1B2C3D);
        private RunPhase _phase = RunPhase.None;
        private float _scatterCountdown;
        private int _lastScatter = -1;
        private float _regionPoll;
        private float _regionLevel = 0.4f;

        /// <summary>Seconds between player-position polls. Regions are tens of metres wide; four times a second is plenty.</summary>
        private const float RegionPollSeconds = 0.25f;

        // ---- PROTOTYPE-ONLY / INVENTED tuning --------------------------------
        private const float ScatterMin = 9f;
        private const float ScatterMax = 24f;

        /// <summary>Common bed level change per second. Slower down than up: calm arrives, it does not cut.</summary>
        private const float RiseRate = 0.45f;
        private const float FallRate = 0.30f;

        /// <summary>The region the beds are fading towards. F1 readout and test seam.</summary>
        public AudioRegion CurrentRegion { get; private set; } = AudioRegion.None;

        /// <summary>Region changes seen since start. Test readout.</summary>
        public int RegionChanges { get; private set; }

        /// <summary>Ambient one-shots fired since the last reset. F1 readout.</summary>
        public int ScatterCount { get; private set; }

        public bool Started { get; private set; }

        /// <summary>Test seam: forces the region instead of polling the player.</summary>
        public AudioRegion? RegionOverride { get; set; }

        public AmbienceDirector(Transform root, AudioClipLibrary library, AudioBusMixer mixer, AudioVoicePool worldPool, AudioMixerBridge bridge, AudioRegionMap map)
        {
            _root = root;
            _library = library;
            _mixer = mixer;
            _worldPool = worldPool;
            _map = map;
            _afterRain = new AfterRainAmbience(library, worldPool, root);   // WP-072 · DEC-102

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
                bridge?.Route(source, AudioBus.Ambience);
                _sources[i] = source;

                // The duck is a filter sweep as well as a level drop. A bed that only gets
                // quieter still occupies its band; a bed that also gets darker genuinely
                // leaves room, and the recovery reads as the world coming back.
                AudioLowPassFilter filter = go.AddComponent<AudioLowPassFilter>();
                filter.cutoffFrequency = 22000f;
                filter.lowpassResonanceQ = 1f;
                _filters[i] = filter;
            }

            _scatterCountdown = ScatterMin;
            BuildReverbZones();
        }

        private void BuildReverbZones()
        {
            if (_map == null)
            {
                return;
            }

            for (int i = 0; i < _map.Polygons.Count; i++)
            {
                AudioRegionMap.Polygon p = _map.Polygons[i];
                if (p.Region == AudioRegion.None || _reverb[(int)p.Region] != null)
                {
                    continue;
                }

                // Extent from the polygon's bounding box: the zone's full-reverb radius is
                // the inscribed half-size, its fade radius the circumscribed one, so a
                // player on the boundary hears a blend and never a step.
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                for (int k = 0; k < p.XZ.Length; k += 2)
                {
                    minX = Mathf.Min(minX, p.XZ[k]); maxX = Mathf.Max(maxX, p.XZ[k]);
                    minZ = Mathf.Min(minZ, p.XZ[k + 1]); maxZ = Mathf.Max(maxZ, p.XZ[k + 1]);
                }

                float halfW = (maxX - minX) * 0.5f, halfD = (maxZ - minZ) * 0.5f;
                var go = new GameObject("Reverb " + p.Region);
                go.transform.SetParent(_root, false);
                go.transform.position = new Vector3(p.Centroid.x, 1.5f, p.Centroid.y);
                AudioReverbZone zone = go.AddComponent<AudioReverbZone>();
                zone.reverbPreset = s_regionReverb[(int)p.Region];
                zone.minDistance = Mathf.Max(4f, Mathf.Min(halfW, halfD));
                zone.maxDistance = Mathf.Max(zone.minDistance + 6f, Mathf.Sqrt(halfW * halfW + halfD * halfD) + 4f);
                _reverb[(int)p.Region] = zone;
                ReverbZoneCount++;
            }
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

                // Each bed starts at a different point in its own loop, so the composite
                // never repeats on one period.
                _sources[i].time = _rng.Range(0f, Mathf.Max(0.01f, clip.length - 0.05f));
                _sources[i].Play();
            }

            Started = true;
        }

        public void SetPhase(RunPhase phase)
        {
            _phase = phase;
        }

        /// <summary>Current level of one bed, 0-1 before bus gain. Test readout.</summary>
        public float Level(int bed)
        {
            return bed >= 0 && bed < BedCount ? _current[bed] : 0f;
        }

        public static int BedIndex(AudioRegion region)
        {
            return region == AudioRegion.None ? -1 : 1 + (int)region;
        }

        public void Tick(float unscaledDeltaTime, bool paused)
        {
            if (paused)
            {
                return;
            }

            // WP-065E: a bed stopped from outside (audio configuration reset) comes back; Started alone cannot tell.
            _healClock -= unscaledDeltaTime;
            if (Started && _healClock <= 0f)
            {
                _healClock = 0.5f;
                for (int i = 0; i < BedCount; i++)
                {
                    if (_sources[i] != null && _sources[i].clip != null && !_sources[i].isPlaying) { _sources[i].Play(); BedRestarts++; }
                }
            }

            _regionPoll -= unscaledDeltaTime;
            if (_regionPoll <= 0f)
            {
                _regionPoll = RegionPollSeconds;
                ResolveRegion();
            }

            ResolveTargets();

            float busGain = _mixer.Gain(AudioBus.Ambience);
            float duck = _mixer.DuckLevel(AudioBus.Ambience);
            float cutoff = Mathf.Lerp(600f, 22000f, duck * duck);

            for (int i = 0; i < BedCount; i++)
            {
                float rate;
                if (i == Common)
                {
                    rate = _target[i] > _current[i] ? RiseRate : FallRate;
                }
                else
                {
                    // The cross-fade is measured bed-to-bed and takes CrossfadeSeconds from
                    // WHATEVER level the bed is at when its target changes (WP-021C, WP-039 P1:
                    // a bed switched while still rising at 0.3 faded out in 1.84 s under the old
                    // "phase level / seconds" rate; DEC-066 ≥ 2 s must hold for every swing).
                    if (!Mathf.Approximately(_target[i], _fadeFrom[i]) || (_fadeRate[i] <= 0f && !Mathf.Approximately(_target[i], _current[i])))
                    {
                        _fadeFrom[i] = _target[i];
                        _fadeRate[i] = Mathf.Max(0.05f, Mathf.Abs(_target[i] - _current[i])) / CrossfadeSeconds;
                    }

                    rate = _fadeRate[i];
                }

                _current[i] = Mathf.MoveTowards(_current[i], _target[i], rate * unscaledDeltaTime);
                _sources[i].volume = _current[i] * busGain;
                _filters[i].cutoffFrequency = cutoff;
            }

            TickScatter(unscaledDeltaTime);
            TickWetLook(unscaledDeltaTime, busGain);
            TickFurin(unscaledDeltaTime);
        }

        /// <summary>
        /// WP-065C: the eave fūrin ring with the wind (the same WorldWind the foliage and the chimes'
        /// own sway read). Stronger wind = shorter gaps; only the chime nearest the player within
        /// <see cref="FurinRange"/> rings, gain falling off with distance, on the Ambience bus so the
        /// snapshot / threat-chip ducks apply. Silent after death and in still air.
        /// </summary>
        private void TickFurin(float unscaledDeltaTime)
        {
            List<Vector3> chimes = AudioSurfaceHints.WindChimes;
            if (chimes.Count == 0 || _worldPool == null || _phase == RunPhase.Dead)
            {
                return;
            }

            float wind = FurinWindOverride ?? DD.Prototype.World.WorldWind.Strength;
            bool gust = wind - _furinWindLag >= FurinGustRise && _furinGustBlock <= 0f;
            _furinLagClock -= unscaledDeltaTime;
            if (_furinLagClock <= 0f) { _furinLagClock = 1f; _furinWindLag = wind; }
            _furinGustBlock -= unscaledDeltaTime;
            if (wind < FurinWindThreshold)
            {
                _furinCountdown = Mathf.Max(_furinCountdown, 1f);
                return;
            }

            _furinCountdown -= unscaledDeltaTime;
            if (_furinCountdown > 0f && !gust)
            {
                return;
            }

            float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FurinWindThreshold, 1f, wind));
            _furinCountdown = Mathf.Lerp(FurinIntervalCalm, FurinIntervalGale, w) * _rng.Range(0.8f, 1.3f);
            _furinGustBlock = FurinGustCooldown;

            PlayerRig rig = PlayerRegistry.Primary;
            Vector3 who = rig != null ? rig.transform.position : (_root != null ? _root.position : Vector3.zero);
            int best = -1; float bestD = FurinRange * FurinRange;
            for (int i = 0; i < chimes.Count; i++)
            {
                float d = (chimes[i] - who).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }

            if (best < 0)
            {
                return;
            }

            AudioClip clip = _library.Next(AudioSoundId.AmbFurin, out int _);
            if (clip == null)
            {
                return;
            }

            // Linear 1.5 → 12 m falloff here (the pool's own 8–60 m rolloff is for the courtyard); each chime
            // keeps its own size (fixed ±1 st by index) under the per-play ±0.5 st jitter.
            float dist = Mathf.Sqrt(bestD);
            float near = dist <= 1.5f ? 1f : 1f - (dist - 1.5f) / (FurinRange - 1.5f);
            float gain = (_phase == RunPhase.Fighting ? 0.30f : 0.55f) * near;
            float size = ((best * 37) % 21 - 10) * 0.1f;
            AudioPlacement placement = AudioMixPlan.WindChime(gain, Mathf.Pow(2f, size / 12f));
            if (_worldPool.Play(placement, clip, chimes[best], 1f, placement.Priority))
            {
                FurinCount++;
            }
        }

        /// <summary>Look B clocks: thunder (slow, far, low) and the pond bed level by distance to the nearest pond centre.</summary>
        private void TickWetLook(float unscaledDeltaTime, float busGain)
        {
            if (!WetLook)
            {
                if (_pond != null && _pond.isPlaying) { _pond.Stop(); _pondLevel = 0f; }
                _afterRain.Stop();
                return;
            }

            // WP-072 · DEC-102: the sun is out after the rain — birds, eave / bamboo drips and the grove instead of thunder.
            if (AfterRainAmbience.SunnyAfterRain)
            {
                // The listener sits at the player's head (ADR-021-2), not at the camera: drips and grove are near the ear.
                Vector3 listenerAt = _listenerAt != null ? _listenerAt() : (_root != null ? _root.position : Vector3.zero);
                _afterRain.Tick(unscaledDeltaTime, busGain, _regionLevel, _phase == RunPhase.Fighting, listenerAt, _sources[Common].outputAudioMixerGroup, _mixer.DuckLevel(AudioBus.Ambience));
            }
            else
            {
                _afterRain.Stop();
            }

            _thunderCountdown -= unscaledDeltaTime;
            if (!AfterRainAmbience.SunnyAfterRain && _thunderCountdown <= 0f)
            {
                _thunderCountdown = _rng.Range(ThunderMin, ThunderMax);
                AudioClip clip = _library.Next(AudioSoundId.AmbThunderDistant, out int _);
                if (clip != null && _worldPool != null)
                {
                    // Far: 45-60 m out on a random bearing, above the horizon - the LPF-by-distance darkens it.
                    Vector3 origin = _root != null ? _root.position : Vector3.zero;
                    if (DD.Prototype.Feedback.FeedbackCameraRef.TryGetPosition(out Vector3 listener)) origin = listener;
                    float angle = _rng.Range(0f, Mathf.PI * 2f);
                    Vector3 at = origin + new Vector3(Mathf.Cos(angle) * 52f, 18f, Mathf.Sin(angle) * 52f);
                    AudioPlacement placement = AudioMixPlan.AmbientOneShot(AudioSoundId.AmbThunderDistant, _phase == RunPhase.Fighting ? 0.45f : 0.7f);
                    if (_worldPool.Play(placement, clip, at, 1f, placement.Priority)) ThunderCount++;
                }
            }

            // Pond bed: one looping 3D source at the nearest pond centre (registered by the world assembly).
            List<Vector3> ponds = AudioSurfaceHints.PondCenters;
            if (ponds.Count == 0)
            {
                return;
            }

            if (_pond == null)
            {
                AudioClip clip = _library.First(AudioSoundId.AmbPondBed);
                if (clip == null) return;
                var go = new GameObject("Ambience Pond");
                go.transform.SetParent(_root, false);
                _pond = go.AddComponent<AudioSource>();
                _pond.clip = clip; _pond.loop = true; _pond.playOnAwake = false; _pond.spatialBlend = 1f;
                _pond.rolloffMode = AudioRolloffMode.Linear; _pond.minDistance = 6f; _pond.maxDistance = 30f;
                _pond.dopplerLevel = 0f; _pond.bypassReverbZones = true; _pond.priority = 8; _pond.volume = 0f;
                _pond.outputAudioMixerGroup = _sources[Common].outputAudioMixerGroup;   // same routing as the beds
                _pond.time = _rng.Range(0f, Mathf.Max(0.01f, clip.length - 0.05f));
            }

            Vector3 who = _root != null ? _root.position : Vector3.zero;
            if (DD.Prototype.Feedback.FeedbackCameraRef.TryGetPosition(out Vector3 ear)) who = ear;
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < ponds.Count; i++)
            {
                float d = (ponds[i] - who).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }

            _pond.transform.position = ponds[best];
            float target = _regionLevel * 0.9f;
            _pondLevel = Mathf.MoveTowards(_pondLevel, target, unscaledDeltaTime / CrossfadeSeconds);
            _pond.volume = _pondLevel * busGain;
            if (!_pond.isPlaying) _pond.Play();
        }

        private void ResolveRegion()
        {
            AudioRegion region;
            if (RegionOverride.HasValue)
            {
                region = RegionOverride.Value;
            }
            else if (_map == null)
            {
                region = AudioRegion.None;
            }
            else
            {
                PlayerRig rig = PlayerRegistry.Primary;
                region = rig != null ? _map.Resolve(rig.transform.position) : AudioRegion.None;
            }

            if (region != CurrentRegion)
            {
                CurrentRegion = region;
                RegionChanges++;
            }
        }

        /// <summary>
        /// The per-phase bed balance. Numbers are PROTOTYPE-ONLY / INVENTED; the SHAPE is
        /// the brief: the world is quieter and darker in a fight, fuller between rounds,
        /// nearly alone after death.
        /// </summary>
        private void ResolveTargets()
        {
            float common, region;
            switch (_phase)
            {
                case RunPhase.Fighting:
                    common = 0.55f;
                    region = 0.42f;
                    break;

                case RunPhase.Countdown:
                    common = 0.60f;
                    region = 0.48f;
                    break;

                case RunPhase.RoundComplete:
                    common = 0.66f;
                    region = 0.56f;
                    break;

                case RunPhase.Upgrading:
                    // The calm: the fullest the beds ever get (WP-002 brief).
                    common = 0.74f;
                    region = 0.62f;
                    break;

                case RunPhase.Dead:
                    // WP-060: the results screen sat 10 dB under the fight with almost no low-band
                    // fill; the beds come up so the death stinger lands on something.
                    common = 0.45f;
                    region = 0.30f;
                    break;

                default: // None - menus, selection, results
                    common = 0.62f;
                    region = 0.40f;
                    break;
            }

            _target[Common] = common;
            _regionLevel = region;
            int active = BedIndex(CurrentRegion);
            for (int i = 1; i < BedCount; i++)
            {
                _target[i] = i == active ? region : 0f;
            }
        }

        /// <summary>
        /// The one-shot scatterer: never the same one-shot twice running, slower during a
        /// fight (a creak in a firefight is masked or misread), placed in a ring around the
        /// listener so it is ambience and not an event.
        /// </summary>
        private void TickScatter(float unscaledDeltaTime)
        {
            _scatterCountdown -= unscaledDeltaTime;
            if (_scatterCountdown > 0f)
            {
                return;
            }

            // WP-060: with the arena nearly empty (last few enemies) the fight rate 1.6 left
            // the world dead-quiet between music bars; the calm rate keeps it alive.
            bool lastStand = _phase == RunPhase.Fighting && RunEvents.Current.AliveEnemies <= 3;
            float multiplier = _phase == RunPhase.Fighting ? (lastStand ? 1f : 1.6f)
                : _phase == RunPhase.Upgrading || _phase == RunPhase.None ? 0.7f
                : 1f;

            _scatterCountdown = _rng.Range(ScatterMin, ScatterMax) * multiplier;

            AudioSoundId[] set = WetLook ? s_scatterWet : s_scatter;
            int pick = _rng.RangeInt(0, set.Length);
            if (pick == _lastScatter)
            {
                pick = (pick + 1 + _rng.RangeInt(0, set.Length - 1)) % set.Length;
            }

            _lastScatter = pick;

            AudioSoundId sound = set[pick];
            AudioClip clip = _library.Next(sound, out int _);
            if (clip == null || _worldPool == null)
            {
                return;
            }

            float angle = _rng.Range(0f, Mathf.PI * 2f);
            float distance = _rng.Range(9f, 22f);
            Vector3 origin = _root != null ? _root.position : Vector3.zero;

            if (DD.Prototype.Feedback.FeedbackCameraRef.TryGetPosition(out Vector3 listener))
            {
                origin = listener;
            }

            Vector3 position = origin + new Vector3(
                Mathf.Cos(angle) * distance,
                _rng.Range(-1f, 5f),
                Mathf.Sin(angle) * distance);

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

            if (_pond != null) { _pond.Stop(); _pondLevel = 0f; }
            _afterRain?.Stop();
            Started = false;
        }

        /// <summary>One line for the F1 panel.</summary>
        public string Describe()
        {
            return string.Format(
                "{0} · region {1} ({2} changes) · common {3:0.00} · C {4:0.00} N {5:0.00} E {6:0.00} S {7:0.00} W {8:0.00} · scatter {9}",
                _phase, CurrentRegion, RegionChanges, _current[0],
                _current[1], _current[2], _current[3], _current[4], _current[5], ScatterCount);
        }
    }
}
