using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>WP-072: the recorded-tail family a weapon rings in (STANDARDS §5: class × space).</summary>
    public enum WeaponTailClass
    {
        Ar = 0,
        Smg = 1,
        Sg = 2,
        Lmg = 3,
        Sr = 4,
        Rl = 5
    }

    /// <summary>WP-072 / WP-074: the acoustic space a shot rings in (S-05 Enclosure).</summary>
    public enum AcousticSpace
    {
        Open = 0,
        Corridor = 1,
        Hall = 2
    }

    /// <summary>
    /// WP-072: where the listener is, acoustically. S-05's WP-074 layout (acoustic-spaces-v6.json)
    /// installs <see cref="Resolver"/>; until then, and outside every listed space, it is Open.
    /// </summary>
    public static class AcousticSpaces
    {
        /// <summary>Position → space. Null = everything is Open.</summary>
        public static System.Func<Vector3, AcousticSpace> Resolver;

        public static AcousticSpace At(Vector3 position)
        {
            System.Func<Vector3, AcousticSpace> resolver = Resolver;
            return resolver != null ? resolver(position) : AcousticSpace.Open;
        }
    }

    /// <summary>One weapon's fire voices (WP-069 audioId → AP-011 v016 identities).</summary>
    public readonly struct WeaponVoiceSet
    {
        public readonly string AudioId;
        public readonly AudioSoundId Fire;
        public readonly AudioSoundId Transient;
        public readonly AudioSoundId Loop;
        public readonly float LoopRpm;
        public readonly WeaponTailClass TailClass;

        /// <summary>AP-011 v016: the transient files sit on the peak ceiling; this takes them back to the Fire pre-mix relation.</summary>
        public readonly float TransientTrimDb;

        public WeaponVoiceSet(string audioId, AudioSoundId fire, WeaponTailClass tail)
            : this(audioId, fire, AudioSoundId.None, AudioSoundId.None, 0f, tail, 0f)
        {
        }

        public WeaponVoiceSet(string audioId, AudioSoundId fire, AudioSoundId transient, AudioSoundId loop, float loopRpm, WeaponTailClass tail, float transientTrimDb)
        {
            TransientTrimDb = transientTrimDb;
            AudioId = audioId;
            Fire = fire;
            Transient = transient;
            Loop = loop;
            LoopRpm = loopRpm;
            TailClass = tail;
        }

        /// <summary>Auto ∧ RPM ≥ 600 (WP-069 §0.3): transient per shot over an RPM-locked body loop.</summary>
        public bool IsHybrid => Loop != AudioSoundId.None;
    }

    /// <summary>
    /// WP-072: the weapon identity table. audioIds are S-02's (WP-069 A1/A4); the loop RPM is the
    /// rate the v016 body loop was rendered at, not a gameplay number — a retuned RPM bends the
    /// loop's playback rate (±15 %) rather than breaking it.
    /// </summary>
    public static class WeaponAudioIdentity
    {
        public static readonly WeaponVoiceSet[] All =
        {
            new WeaponVoiceSet("AR1", AudioSoundId.WeaponAr1Fire, AudioSoundId.WeaponAr1Transient, AudioSoundId.WeaponAr1Loop, 600f, WeaponTailClass.Ar, -3.6f),
            new WeaponVoiceSet("AR2", AudioSoundId.WeaponAr2Fire, WeaponTailClass.Ar),
            new WeaponVoiceSet("SMG1", AudioSoundId.WeaponSmg1Fire, AudioSoundId.WeaponSmg1Transient, AudioSoundId.WeaponSmg1Loop, 900f, WeaponTailClass.Smg, -9.4f),
            new WeaponVoiceSet("SMG2", AudioSoundId.WeaponSmg2Fire, WeaponTailClass.Smg),
            new WeaponVoiceSet("SG1", AudioSoundId.WeaponSg1Fire, WeaponTailClass.Sg),
            new WeaponVoiceSet("SG2", AudioSoundId.WeaponSg2Fire, WeaponTailClass.Sg),
            new WeaponVoiceSet("LMG1", AudioSoundId.WeaponLmg1Fire, AudioSoundId.WeaponLmg1Transient, AudioSoundId.WeaponLmg1Loop, 720f, WeaponTailClass.Lmg, -4.36f),
            new WeaponVoiceSet("LMG2", AudioSoundId.WeaponLmg2Fire, WeaponTailClass.Lmg),
            new WeaponVoiceSet("SR1", AudioSoundId.WeaponSr1Fire, WeaponTailClass.Sr),
            new WeaponVoiceSet("RL1", AudioSoundId.WeaponRl1Fire, WeaponTailClass.Rl)
        };

        public static bool TryGet(string audioId, out WeaponVoiceSet set)
        {
            if (!string.IsNullOrEmpty(audioId))
            {
                for (int i = 0; i < All.Length; i++)
                {
                    if (string.Equals(All[i].AudioId, audioId, System.StringComparison.Ordinal))
                    {
                        set = All[i];
                        return true;
                    }
                }
            }

            set = default;
            return false;
        }

        /// <summary>WP-069 A4 migration: A → ar_1, B → smg_1, C → sg_1 until WeaponDefinition v3 carries audioId.</summary>
        public static string ForClassic(ClassicWeaponId id)
        {
            switch (id)
            {
                case ClassicWeaponId.AutoSmg: return "SMG1";
                case ClassicWeaponId.CombatShotgun: return "SG1";
                default: return "AR1";
            }
        }

        public static AudioSoundId Tail(WeaponTailClass tailClass, AcousticSpace space)
        {
            return (AudioSoundId)((int)AudioSoundId.WeaponTailArOpen + (int)tailClass * 3 + (int)space);
        }
    }

    /// <summary>WP-072 STANDARDS §4: the player-weapon mix per camera mode (same files, different placement).</summary>
    public readonly struct WeaponPerspectiveMix
    {
        public readonly float Spatial;
        public readonly float NearFieldDb;
        public readonly float MechanicalDb;
        public readonly float TailDb;

        public WeaponPerspectiveMix(float spatial, float nearFieldDb, float mechanicalDb, float tailDb)
        {
            Spatial = spatial;
            NearFieldDb = nearFieldDb;
            MechanicalDb = mechanicalDb;
            TailDb = tailDb;
        }

        /// <summary>Relative to top-down, the mode the v016 pre-mixes were balanced in.</summary>
        public static WeaponPerspectiveMix For(CameraMode mode)
        {
            switch (mode)
            {
                case CameraMode.ThirdPerson: return new WeaponPerspectiveMix(0.25f, 1f, 3f, 1f);
                case CameraMode.FirstPerson: return new WeaponPerspectiveMix(0f, 2f, 7f, 2f);
                default: return new WeaponPerspectiveMix(0.35f, 0f, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// WP-072 B1: the player's fire voice. One-shot weapons play the dry shot and the space tail on
    /// every shot. Hybrid weapons (AR1/SMG1/LMG1) play the full shot first, then a transient per shot
    /// over the body loop, and ring the tail once when the trigger lets go (STANDARDS §3).
    /// Owned and ticked by <see cref="PrototypeAudioDirector"/>; takes a cue only when the weapon's
    /// v016 clips are produced, so a missing catalog falls back to the legacy RifleFire/SmgFire path.
    /// </summary>
    public sealed class WeaponFireAudio
    {
        public delegate bool PlayFunc(in AudioPlacement placement, Vector3 position, float gainScale, int priority);

        /// <summary>No shot for this many periods = the trigger let go.</summary>
        private const float ReleasePeriods = 1.6f;
        private const float LoopFadeSeconds = 0.04f;
        private const float LoopGain = 0.8f;

        private readonly AudioClipLibrary _library;
        private readonly AudioBusMixer _mixer;
        private readonly PlayFunc _play;
        private readonly AudioSource _loop;

        private bool _active;
        private WeaponVoiceSet _set;
        private float _lastShot;
        private int _lastShotFrame = -10;
        private float _interval;
        private int _shots;
        private Vector3 _position;
        private float _loopVolume;
        private bool _loopFading;

        public WeaponFireAudio(AudioClipLibrary library, AudioBusMixer mixer, AudioMixerBridge bridge, Transform root, PlayFunc play)
        {
            _library = library;
            _mixer = mixer;
            _play = play;

            var go = new GameObject("WeaponFireLoop");
            go.transform.SetParent(root, false);
            _loop = go.AddComponent<AudioSource>();
            _loop.loop = true;
            _loop.playOnAwake = false;
            _loop.rolloffMode = AudioRolloffMode.Linear;
            _loop.minDistance = 6f;
            _loop.maxDistance = 45f;
            _loop.dopplerLevel = 0f;
            _loop.bypassReverbZones = true;
            _loop.priority = 30;
            bridge?.Route(_loop, AudioBus.Weapon);
        }

        /// <summary>Hybrid loop starts (shot 2 of a string). Test / F1 readout.</summary>
        public int LoopStarts { get; private set; }

        /// <summary>Trigger releases heard (tail rung once). Test / F1 readout.</summary>
        public int Releases { get; private set; }

        public bool LoopPlaying => _loop != null && _loop.isPlaying && !_loopFading;

        /// <summary>The tail rung last (class × space), None before the first.</summary>
        public AudioSoundId LastTail { get; private set; }

        /// <summary>The fire voice (Fire / Transient) played last, None before the first.</summary>
        public AudioSoundId LastVoice { get; private set; }

        public bool TryHandle(in CombatAudioCue cue)
        {
            if (!WeaponAudioIdentity.TryGet(cue.WeaponAudioId, out WeaponVoiceSet set) || !_library.IsProduced(set.Fire))
            {
                return false;
            }

            float now = Time.time;
            WeaponPerspectiveMix mix = WeaponPerspectiveMix.For(CameraModeService.Current);

            if (!set.IsHybrid)
            {
                if (_active) Release();
                PlayVoice(set.Fire, cue, mix, mix.NearFieldDb);
                PlayTail(set, cue.Position, mix);
                return true;
            }

            float period = 60f / set.LoopRpm;
            bool continuing = _active && string.Equals(_set.AudioId, set.AudioId, System.StringComparison.Ordinal)
                && now - _lastShot <= period * ReleasePeriods + Time.deltaTime;

            if (!continuing)
            {
                if (_active) Release();
                _active = true;
                _set = set;
                _shots = 1;
                _interval = period;
                PlayVoice(set.Fire, cue, mix, mix.NearFieldDb);
            }
            else
            {
                _interval = Mathf.Lerp(_interval, Mathf.Max(0.01f, now - _lastShot), 0.5f);
                _shots++;
                PlayVoice(set.Transient, cue, mix, mix.NearFieldDb + mix.MechanicalDb + set.TransientTrimDb);
                if (!LoopPlaying) StartLoop(set, cue.Position, mix);
            }

            _lastShot = now;
            _lastShotFrame = Time.frameCount;
            _position = cue.Position;
            if (_loop.isPlaying)
            {
                _loop.transform.position = cue.Position;
                _loop.pitch = Mathf.Clamp(period / _interval, 0.85f, 1.15f);
            }

            return true;
        }

        /// <summary>Per frame, unpaused: release on trigger let-go, fade the body loop out.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            // + this frame's delta: a hitch longer than the release window must not end a burst the weapon is still firing
            // (the next shot lands on the frame after the hitch; WP-051C flake, LoopStarts 2 in one burst).
            // R1-B A1: and at least two frames without a shot — the director and the weapon have no execution order, so one slow
            // frame (GC hitch, < 9 fps) must never read as a trigger release mid-string.
            if (_active && _set.IsHybrid && Time.frameCount - _lastShotFrame >= 2
                && Time.time - _lastShot > (60f / _set.LoopRpm) * ReleasePeriods + Time.deltaTime)
            {
                Release();
            }

            if (!_loop.isPlaying)
            {
                return;
            }

            if (_loopFading)
            {
                _loopVolume = Mathf.MoveTowards(_loopVolume, 0f, unscaledDeltaTime / LoopFadeSeconds);
                if (_loopVolume <= 0.001f)
                {
                    _loop.Stop();
                    _loopFading = false;
                }
            }

            _loop.volume = _loopVolume * _mixer.Gain(AudioBus.Weapon);
        }

        /// <summary>Run end, death, disable: silence without a tail.</summary>
        public void StopAll()
        {
            _active = false;
            _shots = 0;
            _loopFading = false;
            _loopVolume = 0f;
            if (_loop != null) _loop.Stop();
        }

        private void Release()
        {
            _active = false;
            if (_loop.isPlaying) _loopFading = true;
            PlayTail(_set, _position, WeaponPerspectiveMix.For(CameraModeService.Current));
            Releases++;
        }

        private void StartLoop(in WeaponVoiceSet set, Vector3 position, in WeaponPerspectiveMix mix)
        {
            AudioClip clip = _library.First(set.Loop);
            if (clip == null)
            {
                return;
            }

            _loop.clip = clip;
            _loop.spatialBlend = position == Vector3.zero ? 0f : mix.Spatial;
            _loop.transform.position = position;
            _loopVolume = LoopGain * Db(mix.NearFieldDb);
            _loopFading = false;
            _loop.volume = _loopVolume * _mixer.Gain(AudioBus.Weapon);
            _loop.Play();
            LoopStarts++;
        }

        private void PlayVoice(AudioSoundId sound, in CombatAudioCue cue, in WeaponPerspectiveMix mix, float db)
        {
            AudioPlacement p = AudioMixPlan.PlayerWeapon(sound);
            p.Spatial = cue.Position == Vector3.zero ? 0f : mix.Spatial;
            if (_play(p, cue.Position, Db(db), cue.Priority)) LastVoice = sound;
        }

        private void PlayTail(in WeaponVoiceSet set, Vector3 position, in WeaponPerspectiveMix mix)
        {
            AcousticSpace space = AcousticSpaces.At(position);
            AudioSoundId tail = WeaponAudioIdentity.Tail(set.TailClass, space);
            if (!_library.IsProduced(tail))
            {
                return;
            }

            AudioPlacement p = AudioMixPlan.PlayerWeaponTail(tail);
            p.Spatial = position == Vector3.zero ? 0f : mix.Spatial;
            if (_play(p, position, Db(mix.TailDb + WetDb(space)), 30)) LastTail = tail;
        }

        /// <summary>
        /// STANDARDS §5 wet level against the dry shot (−14 / −10 / −8 dB), less the 2 LU the tail files
        /// sit under the dry files (tails −20 LUFS-I, shots −18) — the files stay at a Vorbis-safe level.
        /// </summary>
        public static float WetDb(AcousticSpace space)
        {
            switch (space)
            {
                case AcousticSpace.Corridor: return -8f;
                case AcousticSpace.Hall: return -6f;
                default: return -12f;
            }
        }

        private static float Db(float db) => Mathf.Pow(10f, db / 20f);
    }
}
