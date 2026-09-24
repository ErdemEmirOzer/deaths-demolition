using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-072 B5: the 3D flight loop of a slow projectile, keyed by its ProjectileId (S-02 R1-A #5): a rocket
    /// (RocketFlightLoop, STANDARDS §7), a Raiju orb (SwArcSphereLoop) or a Namekuji gel blob (SwGelFlightLoop).
    /// Starts on <see cref="ProjectileEvents.Spawned"/>, follows the projectile's pool slot every frame, and fades
    /// out when that projectile ends — exactly once, on an Impact with <c>Stopped</c> or on Expired. Bullets and
    /// pellets have no loop. At most <see cref="Voices"/> loops (two rockets); a produced clip is required.
    /// </summary>
    public sealed class ProjectileFlightAudio
    {
        public const int Voices = 4;
        private const float FadeSeconds = 0.06f;
        private const float RampMeters = 45f;

        private struct Voice
        {
            public AudioSource Source;
            public int ProjectileId;   // 0 = free
            public int Slot;
            public ProjectileKind Kind;
            public float Volume;
            public float Target;
            public bool Ending;
        }

        private readonly Voice[] _voices = new Voice[Voices];
        private readonly AudioClipLibrary _library;
        private readonly AudioBusMixer _mixer;
        private readonly System.Func<Vector3> _listener;
        private bool _subscribed;

        public ProjectileFlightAudio(AudioClipLibrary library, AudioBusMixer mixer, AudioMixerBridge bridge, Transform root, System.Func<Vector3> listener)
        {
            _library = library;
            _mixer = mixer;
            _listener = listener;
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("FlightLoop " + i);
                go.transform.SetParent(root, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 4f;
                s.maxDistance = 45f;
                s.dopplerLevel = 0f;
                s.priority = 60;
                bridge?.Route(s, AudioBus.Impact);
                _voices[i].Source = s;
            }
        }

        /// <summary>Loops started (test / F1).</summary>
        public int Started { get; private set; }

        /// <summary>Loops ended by their projectile's end event (test / F1).</summary>
        public int Ended { get; private set; }

        public int Playing
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Voices; i++) if (_voices[i].ProjectileId != 0 && !_voices[i].Ending) n++;
                return n;
            }
        }

        public static AudioSoundId LoopFor(ProjectileKind kind)
        {
            switch (kind)
            {
                case ProjectileKind.Rocket: return AudioSoundId.RocketFlightLoop;
                case ProjectileKind.Orb: return AudioSoundId.SwArcSphereLoop;
                case ProjectileKind.Gel: return AudioSoundId.SwGelFlightLoop;
                default: return AudioSoundId.None;
            }
        }

        /// <summary>STANDARDS §7 baked Doppler: −2 st from 0 to 45 m in FP/TP; top-down stays at pitch (the camera is above the flight).</summary>
        public static float PitchFor(float listenerMeters, CameraMode mode)
        {
            if (mode == CameraMode.TopDown) return 1f;
            float st = -2f * Mathf.Clamp01(listenerMeters / RampMeters);
            return Mathf.Pow(2f, st / 12f);
        }

        public void Enable()
        {
            if (_subscribed) return;
            ProjectileEvents.Spawned += OnSpawned;
            ProjectileEvents.Impact += OnImpact;
            ProjectileEvents.Expired += OnExpired;
            _subscribed = true;
        }

        public void Disable()
        {
            if (!_subscribed) return;
            ProjectileEvents.Spawned -= OnSpawned;
            ProjectileEvents.Impact -= OnImpact;
            ProjectileEvents.Expired -= OnExpired;
            _subscribed = false;
            StopAll();
        }

        public void StopAll()
        {
            for (int i = 0; i < Voices; i++)
            {
                if (_voices[i].Source != null) _voices[i].Source.Stop();
                _voices[i].ProjectileId = 0;
                _voices[i].Ending = false;
            }
        }

        private void OnSpawned(ProjectileSpawnInfo info)
        {
            AudioSoundId sound = LoopFor(info.Kind);
            if (sound == AudioSoundId.None || !_library.IsProduced(sound)) return;

            int rockets = 0, free = -1;
            for (int i = 0; i < Voices; i++)
            {
                if (_voices[i].ProjectileId == 0) { if (free < 0) free = i; continue; }
                if (_voices[i].Kind == ProjectileKind.Rocket && !_voices[i].Ending) rockets++;
            }

            if (free < 0 || (info.Kind == ProjectileKind.Rocket && rockets >= 2)) return;   // STANDARDS §7: at most two rocket voices

            ref Voice v = ref _voices[free];
            v.ProjectileId = info.ProjectileId;
            v.Slot = info.Index;
            v.Kind = info.Kind;
            v.Volume = 0f;
            v.Target = info.Kind == ProjectileKind.Rocket ? 0.9f : 0.75f;
            v.Ending = false;
            v.Source.clip = _library.First(sound);
            v.Source.transform.position = info.Position;
            v.Source.pitch = 1f;
            v.Source.volume = 0f;
            v.Source.Play();
            Started++;
        }

        private void OnImpact(ProjectileImpactInfo info)
        {
            if (info.Stopped) End(info.ProjectileId, info.Position);
        }

        private void OnExpired(ProjectileExpireInfo info)
        {
            End(info.ProjectileId, info.Position);
        }

        private void End(int projectileId, Vector3 at)
        {
            for (int i = 0; i < Voices; i++)
            {
                if (_voices[i].ProjectileId != projectileId || _voices[i].Ending) continue;
                _voices[i].Ending = true;
                _voices[i].Source.transform.position = at;
                Ended++;
            }
        }

        /// <summary>Per frame, unpaused: follow each projectile's slot, apply the pitch ramp, fade the ended ones out.</summary>
        public void Tick(float dt)
        {
            Vector3 listener = _listener != null ? _listener() : Vector3.zero;
            CameraMode mode = CameraModeService.Current;
            float bus = _mixer.Gain(AudioBus.Impact);
            for (int i = 0; i < Voices; i++)
            {
                ref Voice v = ref _voices[i];
                if (v.ProjectileId == 0) continue;

                if (!v.Ending)
                {
                    for (int k = 0; k < ProjectileSystem.LiveCount; k++)
                    {
                        ref readonly ProjectileState p = ref ProjectileSystem.Get(k);
                        if (p.Slot == v.Slot && p.Alive)
                        {
                            v.Source.transform.position = p.Position;
                            break;
                        }
                    }

                    v.Volume = Mathf.MoveTowards(v.Volume, v.Target, dt / FadeSeconds);
                }
                else
                {
                    v.Volume = Mathf.MoveTowards(v.Volume, 0f, dt / FadeSeconds);
                    if (v.Volume <= 0.001f)
                    {
                        v.Source.Stop();
                        v.ProjectileId = 0;
                        v.Ending = false;
                        continue;
                    }
                }

                if (v.Kind == ProjectileKind.Rocket)
                {
                    v.Source.pitch = PitchFor(Vector3.Distance(listener, v.Source.transform.position), mode);
                }

                v.Source.volume = v.Volume * bus;
            }
        }
    }
}
