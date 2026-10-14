using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-072 · DEC-102 "post-rain sunny courtyard" (AP-011 v022), ticked by <see cref="AmbienceDirector"/> while the
    /// wet look is on:
    ///   • distant birds replace the rain look's thunder (15–25 m out, 8–12 m up, above the eaves) — −8 dB while the listener
    ///     is inside a Hall / Corridor of the acoustic map (a roof between the ear and the sky); a bird due while the ambience is
    ///     ducked (gunfire, blasts) waits 4 s instead of being spent where nobody hears it (WP-072 B7, C-00 30.09 panel);
    ///   • eave drips at the nearest eave points (the fūrin hang points under the eaves, within 14 m); inside a space the
    ///     drops come from 2–4 m around the listener (the eave line of the room), in the open a slower ring 3–9 m out;
    ///   • the nearest bamboo grove within 35 m: a 3D rustle bed, a gust every 12–26 s, 1–2 leaf drips shaken loose after it.
    /// PROTOTYPE-ONLY timings (C-00 25.09 brief); every clip is optional (unproduced ids stay silent).
    /// </summary>
    public sealed class AfterRainAmbience
    {
        /// <summary>DEC-102: the sun is out after the rain — birds instead of thunder. Off = the WP-048 rain look (thunder).</summary>
        public static bool SunnyAfterRain = true;

        /// <summary>
        /// C-00 25.09: the synthetic birds wait for the user's listening pass — until then they stay sparse and in the
        /// background. One knob: 0 = no birds at all; 1 = the designed level. Default 0.35 (≈ −9 dB, every 14–30 s).
        /// </summary>
        public static float BirdLevel = 0.35f;

        public const float BirdMin = 14f, BirdMax = 30f;

        /// <summary>
        /// WP-072 B7 (C-00 30.09, review panel): the bird's own gain in every run phase — the old ×0.35 "fighting" factor keyed on
        /// RunPhase.Fighting, i.e. the whole round, and buried the birds 20–25 LU under the bed. Real combat is the duck's job.
        /// </summary>
        public const float BirdGainOpen = 0.6f, BirdRoofFactor = 0.4f, BirdDeferDuck = 0.5f, BirdDeferSeconds = 4f;
        public const float BirdRangeMin = 15f, BirdRangeMax = 25f, BirdHeightMin = 8f, BirdHeightMax = 12f;

        /// <summary>Test seam (measurement): the next birds sit at this offset from the listener instead of a random spot. Null = random.</summary>
        public static Vector3? PinnedBirdOffset;

        public static float BirdGain(bool inside, float birdLevel) => BirdGainOpen * (inside ? BirdRoofFactor : 1f) * Mathf.Clamp01(birdLevel);

        public static bool BirdDeferred(float ambienceDuck) => ambienceDuck < BirdDeferDuck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PinnedBirdOffset = null;
        public const float DripMin = 0.9f, DripMax = 2.6f;
        public const float GustMin = 12f, GustMax = 26f;
        public const float EaveRange = 14f, GroveRange = 35f;

        private readonly AudioClipLibrary _library;
        private readonly AudioVoicePool _pool;
        private readonly Transform _root;
        private readonly System.Random _rng = new System.Random(102);
        private const float SpaceRefreshSeconds = 0.25f;   // WP-111 R4 perf D4
        private AcousticSpace _space;
        private float _spaceAge;
        private AudioSource _grove;
        private float _groveLevel;
        private float _birdCountdown = 4f, _dripCountdown = 1f, _gustCountdown = 8f, _leafDripAt = -1f;
        private Vector3 _leafDripPoint;

        public AfterRainAmbience(AudioClipLibrary library, AudioVoicePool pool, Transform root)
        {
            _library = library;
            _pool = pool;
            _root = root;
        }

        public int BirdCount { get; private set; }
        public int BirdDeferCount { get; private set; }
        public int EaveDripCount { get; private set; }
        public int GustCount { get; private set; }
        public int LeafDripCount { get; private set; }
        public bool GrovePlaying => _grove != null && _grove.isPlaying && _groveLevel > 0.01f;
        public AcousticSpace LastSpace { get; private set; }

        /// <summary>Diagnosis of the last eave drip (tests).</summary>
        /// <summary>
        /// Diagnosis of the last eave drip (tests). Built only when read: the drip path runs in the director's Update and must not
        /// allocate (DEC-088 GC 0 — S-06 30.09 profiler: 8 allocations / 300 frames from the old per-drip string).
        /// </summary>
        public string LastDrip => _dripKind < 0 ? "none"
            : (_dripKind == 0 ? "eave" : _dripKind == 1 ? "inside" : "ring") + " at " + _dripAt + " d " + _dripDistance.ToString("0.0")
              + (_dripOk ? " played" : " dropped " + (_pool != null ? _pool.Describe() : "no pool"));

        private int _dripKind = -1;
        private Vector3 _dripAt;
        private float _dripDistance;
        private bool _dripOk;

        private float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        public void Tick(float dt, float busGain, float bedLevel, bool fighting, Vector3 listener, AudioMixerGroup bedGroup, float ambienceDuck = 1f)
        {
            // WP-111 R4 perf D4 (C-00 11.10): the listener's space at 4 Hz, not every frame (it changes as the player walks through a door).
            _spaceAge -= dt;
            if (_spaceAge <= 0f)
            {
                _space = AcousticSpaces.At(listener);
                _spaceAge = SpaceRefreshSeconds;
            }

            AcousticSpace space = _space;
            LastSpace = space;
            bool inside = space != AcousticSpace.Open;

            // Birds — the sky after rain; muffled under a roof.
            _birdCountdown -= dt;
            if (_birdCountdown <= 0f && BirdLevel > 0f)
            {
                if (BirdDeferred(ambienceDuck))
                {
                    _birdCountdown = BirdDeferSeconds;   // no RNG draw: the deferral is repeatable
                    BirdDeferCount++;
                }
                else
                {
                    _birdCountdown = Range(BirdMin, BirdMax);
                    float a = Range(0f, Mathf.PI * 2f), r = Range(BirdRangeMin, BirdRangeMax);
                    Vector3 at = listener + (PinnedBirdOffset ?? new Vector3(Mathf.Cos(a) * r, Range(BirdHeightMin, BirdHeightMax), Mathf.Sin(a) * r));
                    if (Play(AudioSoundId.AmbBirdsDistant, at, BirdGain(inside, BirdLevel))) BirdCount++;
                }
            }

            // Eave drips — from the eaves near the listener.
            _dripCountdown -= dt;
            if (_dripCountdown <= 0f)
            {
                bool eave = TryNearestEave(listener, out Vector3 point);
                _dripCountdown = Range(DripMin, DripMax) * (eave || inside ? 1f : 1.8f);
                Vector3 at;
                if (eave) at = point + new Vector3(Range(-0.8f, 0.8f), -Range(0.5f, 1.5f), Range(-0.8f, 0.8f));
                else
                {
                    float a = Range(0f, Mathf.PI * 2f), r = inside ? Range(2f, 4f) : Range(3f, 9f);
                    at = listener + new Vector3(Mathf.Cos(a) * r, -1.2f, Mathf.Sin(a) * r);
                }

                bool ok = Play(AudioSoundId.AmbEaveDrip, at, fighting ? 0.45f : 0.7f);
                if (ok) EaveDripCount++;
                _dripKind = eave ? 0 : inside ? 1 : 2;
                _dripAt = at;
                _dripDistance = Vector3.Distance(at, listener);
                _dripOk = ok;
            }

            TickGrove(dt, busGain, bedLevel, listener, bedGroup);
        }

        private void TickGrove(float dt, float busGain, float bedLevel, Vector3 listener, AudioMixerGroup bedGroup)
        {
            List<Vector3> groves = AudioSurfaceHints.BambooGroves;
            int best = -1; float bestD = GroveRange * GroveRange;
            for (int i = 0; i < groves.Count; i++)
            {
                float d = (groves[i] - listener).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }

            if (_grove == null)
            {
                AudioClip clip = _library.IsProduced(AudioSoundId.AmbBambooGroveLoop) ? _library.First(AudioSoundId.AmbBambooGroveLoop) : null;
                if (clip != null)
                {
                    var go = new GameObject("Ambience Bamboo Grove");
                    go.transform.SetParent(_root, false);
                    _grove = go.AddComponent<AudioSource>();
                    _grove.clip = clip; _grove.loop = true; _grove.playOnAwake = false; _grove.spatialBlend = 1f;
                    _grove.rolloffMode = AudioRolloffMode.Linear; _grove.minDistance = 6f; _grove.maxDistance = GroveRange;
                    _grove.dopplerLevel = 0f; _grove.bypassReverbZones = true; _grove.priority = 10; _grove.volume = 0f;
                    _grove.outputAudioMixerGroup = bedGroup;
                    _grove.time = Range(0f, Mathf.Max(0.01f, clip.length - 0.05f));
                }
            }

            if (_grove != null)
            {
                float target = best >= 0 ? bedLevel : 0f;
                if (best >= 0) _grove.transform.position = groves[best];
                _groveLevel = Mathf.MoveTowards(_groveLevel, target, dt / 2.5f);
                _grove.volume = _groveLevel * busGain;
                if (_groveLevel > 0.001f && !_grove.isPlaying) _grove.Play();
                else if (_groveLevel <= 0.001f && _grove.isPlaying) _grove.Stop();
            }

            if (best < 0) return;

            _gustCountdown -= dt;
            if (_gustCountdown <= 0f)
            {
                _gustCountdown = Range(GustMin, GustMax);
                if (Play(AudioSoundId.AmbBambooGust, groves[best] + Vector3.up * 3f, 0.8f))
                {
                    GustCount++;
                    _leafDripAt = Time.unscaledTime + Range(0.4f, 0.9f);
                    _leafDripPoint = groves[best];
                }
            }

            if (_leafDripAt > 0f && Time.unscaledTime >= _leafDripAt)
            {
                _leafDripAt = -1f;
                Vector3 at = _leafDripPoint + new Vector3(Range(-2f, 2f), 0.5f, Range(-2f, 2f));
                if (Play(AudioSoundId.AmbBambooDrip, at, 0.7f)) LeafDripCount++;
            }
        }

        private static bool TryNearestEave(Vector3 listener, out Vector3 point)
        {
            point = default;
            float best = EaveRange * EaveRange;
            bool found = Nearest(AudioSurfaceHints.WindChimes, listener, ref best, ref point);   // AP005: the fūrin hang under the eaves
            found |= Nearest(AudioSurfaceHints.EavePoints, listener, ref best, ref point);       // V6: the roof edges
            return found;
        }

        private static bool Nearest(List<Vector3> points, Vector3 listener, ref float best, ref Vector3 point)
        {
            bool found = false;
            for (int i = 0; i < points.Count; i++)
            {
                float d = (points[i] - listener).sqrMagnitude;
                if (d < best) { best = d; point = points[i]; found = true; }
            }

            return found;
        }

        private bool Play(AudioSoundId sound, Vector3 at, float gain)
        {
            if (_pool == null || !_library.IsProduced(sound)) return false;
            AudioClip clip = _library.Next(sound, out int _);
            if (clip == null) return false;
            AudioPlacement p = sound == AudioSoundId.AmbEaveDrip || sound == AudioSoundId.AmbBambooDrip
                ? AudioMixPlan.AmbientDrip(sound, gain)
                : AudioMixPlan.AmbientOneShot(sound, gain);
            return _pool.Play(p, clip, at, 1f, p.Priority);
        }

        /// <summary>Test seam: the chosen clocks due on the next tick (bird, drip, gust).</summary>
        public void ForceDue(bool bird = true, bool drip = true, bool gust = true)
        {
            if (bird) _birdCountdown = 0f;
            if (drip) _dripCountdown = 0f;
            if (gust) _gustCountdown = 0f;
            _spaceAge = 0f;   // WP-111 R4 perf D4: the next Tick re-reads the listener's space (tests force a resolver, then ForceDue)
        }

        public void Stop()
        {
            if (_grove != null) _grove.Stop();
            _groveLevel = 0f;
        }
    }
}
