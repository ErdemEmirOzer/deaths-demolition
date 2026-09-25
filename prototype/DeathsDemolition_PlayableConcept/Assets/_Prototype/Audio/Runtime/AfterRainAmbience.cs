using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-072 · DEC-102 "post-rain sunny courtyard" (AP-011 v022), ticked by <see cref="AmbienceDirector"/> while the
    /// wet look is on:
    ///   • distant birds replace the rain look's thunder (25–40 m out, above the eaves) — −8 dB while the listener is
    ///     inside a Hall / Corridor of the acoustic map (a roof between the ear and the sky);
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
        public const float DripMin = 0.9f, DripMax = 2.6f;
        public const float GustMin = 12f, GustMax = 26f;
        public const float EaveRange = 14f, GroveRange = 35f;

        private readonly AudioClipLibrary _library;
        private readonly AudioVoicePool _pool;
        private readonly Transform _root;
        private readonly System.Random _rng = new System.Random(102);
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
        public int EaveDripCount { get; private set; }
        public int GustCount { get; private set; }
        public int LeafDripCount { get; private set; }
        public bool GrovePlaying => _grove != null && _grove.isPlaying && _groveLevel > 0.01f;
        public AcousticSpace LastSpace { get; private set; }

        /// <summary>Diagnosis of the last eave drip (tests).</summary>
        public string LastDrip { get; private set; } = "none";

        private float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        public void Tick(float dt, float busGain, float bedLevel, bool fighting, Vector3 listener, AudioMixerGroup bedGroup)
        {
            AcousticSpace space = AcousticSpaces.At(listener);
            LastSpace = space;
            bool inside = space != AcousticSpace.Open;

            // Birds — the sky after rain; muffled under a roof.
            _birdCountdown -= dt;
            if (_birdCountdown <= 0f && BirdLevel > 0f)
            {
                _birdCountdown = Range(BirdMin, BirdMax);
                float a = Range(0f, Mathf.PI * 2f), r = Range(25f, 40f);
                Vector3 at = listener + new Vector3(Mathf.Cos(a) * r, Range(8f, 15f), Mathf.Sin(a) * r);
                float gain = (fighting ? 0.35f : 0.6f) * (inside ? 0.4f : 1f) * Mathf.Clamp01(BirdLevel);
                if (Play(AudioSoundId.AmbBirdsDistant, at, gain)) BirdCount++;
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
                LastDrip = (eave ? "eave" : inside ? "inside" : "ring") + " at " + at + " d " + Vector3.Distance(at, listener).ToString("0.0") + (ok ? " played" : " dropped " + (_pool != null ? _pool.Describe() + " last " + _pool.LastPlayedAt(AudioSoundId.AmbEaveDrip).ToString("0.000") + " now " + Time.unscaledTime.ToString("0.000") : "no pool"));
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
            List<Vector3> eaves = AudioSurfaceHints.WindChimes;
            point = default;
            float best = EaveRange * EaveRange; bool found = false;
            for (int i = 0; i < eaves.Count; i++)
            {
                float d = (eaves[i] - listener).sqrMagnitude;
                if (d < best) { best = d; point = eaves[i]; found = true; }
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
        }

        public void Stop()
        {
            if (_grove != null) _grove.Stop();
            _groveLevel = 0f;
        }
    }
}
