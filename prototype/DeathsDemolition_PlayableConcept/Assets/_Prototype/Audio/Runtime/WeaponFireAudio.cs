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

    /// <summary>One weapon's fire voice (WP-069 audioId → AP-011 identities).</summary>
    public readonly struct WeaponVoiceSet
    {
        public readonly string AudioId;
        public readonly AudioSoundId Fire;
        public readonly WeaponTailClass TailClass;

        /// <summary>
        /// The weapon's cyclic rate (WD fire.rpm; burst: the in-burst rate) when it fires strings — Auto or Burst — else 0.
        /// A string rings its space tail once, when the trigger lets go; a single-shot weapon rings it on every shot.
        /// </summary>
        public readonly float StringRpm;

        /// <summary>
        /// Per-weapon trim on the fire voice (dB). WP-072 B7 (user 01.10, option B): SFX-Player +3 / SFX-World −1 moved the shot
        /// 4 dB against everything on the world bus; the launcher's own explosion (Target, gain 1.0 — no headroom under the voice clamp)
        /// keeps its STANDARDS §2 +2 dB over the launcher shot only if RL1's fire comes down 4 dB.
        /// WP-085 B1: an automatic now plays the full shot on every round; its trim brings a held string at its own RPM back to the
        /// level of the 8 shots/s kit the user chose the mix by (wp-072/tools/fire_thin.py, 4-voice cap: AR1 −0.9, SMG1 −2.6,
        /// SMG2 −0.4, LMG1 −1.6; LMG2 and the 3-round AR2 burst sit at or under it).
        /// </summary>
        public readonly float FireTrimDb;

        public WeaponVoiceSet(string audioId, AudioSoundId fire, WeaponTailClass tail, float stringRpm = 0f, float fireTrimDb = 0f)
        {
            AudioId = audioId;
            Fire = fire;
            TailClass = tail;
            StringRpm = stringRpm;
            FireTrimDb = fireTrimDb;
        }

        /// <summary>Fires strings (Auto / Burst): tail on release, one more live copy of the shot.</summary>
        public bool FiresStrings => StringRpm > 0f;
    }

    /// <summary>
    /// WP-072: the weapon identity table. audioIds are S-02's (WP-069 A1/A4); the string RPM mirrors the weapon's WD fire block
    /// (EditMode test keeps them in step) and only sets how long a pause reads as a trigger release.
    /// WP-085 B1: the v3 hybrid (per-shot transient over an RPM-locked body loop) is gone — every round is the full shot.
    /// </summary>
    public static class WeaponAudioIdentity
    {
        public static readonly WeaponVoiceSet[] All =
        {
            new WeaponVoiceSet("AR1", AudioSoundId.WeaponAr1Fire, WeaponTailClass.Ar, 600f, -0.9f),
            new WeaponVoiceSet("AR2", AudioSoundId.WeaponAr2Fire, WeaponTailClass.Ar, 1200f),   // 3-round burst, 0.05 s apart
            new WeaponVoiceSet("SMG1", AudioSoundId.WeaponSmg1Fire, WeaponTailClass.Smg, 900f, -2.6f),
            new WeaponVoiceSet("SMG2", AudioSoundId.WeaponSmg2Fire, WeaponTailClass.Smg, 540f, -0.4f),
            new WeaponVoiceSet("SG1", AudioSoundId.WeaponSg1Fire, WeaponTailClass.Sg),
            new WeaponVoiceSet("SG2", AudioSoundId.WeaponSg2Fire, WeaponTailClass.Sg),
            new WeaponVoiceSet("LMG1", AudioSoundId.WeaponLmg1Fire, WeaponTailClass.Lmg, 720f, -1.6f),
            new WeaponVoiceSet("LMG2", AudioSoundId.WeaponLmg2Fire, WeaponTailClass.Lmg, 420f),
            new WeaponVoiceSet("SR1", AudioSoundId.WeaponSr1Fire, WeaponTailClass.Sr),
            new WeaponVoiceSet("RL1", AudioSoundId.WeaponRl1Fire, WeaponTailClass.Rl, fireTrimDb: -4f)   // WP-072 B7: explosion keeps +2 dB over the shot
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
        /// <summary>Mechanical layer lift (STANDARDS §4). WP-085 B1: no separate mechanical voice until the AP-027 v4 set (B5).</summary>
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
    /// WP-072 B1 / WP-085 B1: the player's fire voice — the full dry shot on every round (shuffled variations, pitch jitter,
    /// same-sound copies capped and recycled oldest-first by the pool). Single-shot weapons ring the space tail on every shot;
    /// string weapons (Auto / Burst) ring it once, when the trigger lets go (STANDARDS §3).
    /// Owned and ticked by <see cref="PrototypeAudioDirector"/>; takes a cue only when the weapon's clips are produced,
    /// so a missing catalog falls back to the legacy RifleFire/SmgFire path.
    /// </summary>
    public sealed class WeaponFireAudio
    {
        public delegate bool PlayFunc(in AudioPlacement placement, Vector3 position, float gainScale, int priority);

        /// <summary>No shot for this many periods = the trigger let go.</summary>
        private const float ReleasePeriods = 1.6f;

        /// <summary>Live copies of one automatic's shot: a 0.26–0.36 s shot at 900 RPM overlaps four deep (WP-085 B1).</summary>
        public const int StringVoices = 4;

        private readonly AudioClipLibrary _library;
        private readonly PlayFunc _play;

        private bool _active;
        private WeaponVoiceSet _set;
        private float _lastShot;
        private int _lastShotFrame = -10;
        private Vector3 _position;

        public WeaponFireAudio(AudioClipLibrary library, PlayFunc play)
        {
            _library = library;
            _play = play;
        }

        /// <summary>Full fire voices asked for — one per handled shot, never a lighter layer. Test / F1 readout (R2 S2).</summary>
        public int FireVoices { get; private set; }

        /// <summary>Fire voices the voice pool refused (retrigger window / budget). Test / F1 readout.</summary>
        public int FireRefused { get; private set; }

        /// <summary>Trigger releases heard (tail rung once). Test / F1 readout.</summary>
        public int Releases { get; private set; }

        /// <summary>The tail rung last (class × space), None before the first.</summary>
        public AudioSoundId LastTail { get; private set; }

        /// <summary>The fire voice played last, None before the first.</summary>
        public AudioSoundId LastVoice { get; private set; }

        /// <summary>
        /// WP-085 B4 listening kit only (variant B): true → no fire cue is taken here, the director plays the pre-v3 voice
        /// (RifleFire / SmgFire / ShotgunFire by intensity, room baked in, no space tail). The kit test sets and clears it.
        /// </summary>
        public static bool LegacyVoicesForKit;

        public bool TryHandle(in CombatAudioCue cue)
        {
            if (LegacyVoicesForKit) return false;
            if (!WeaponAudioIdentity.TryGet(cue.WeaponAudioId, out WeaponVoiceSet set) || !_library.IsProduced(set.Fire))
            {
                return false;
            }

            WeaponPerspectiveMix mix = WeaponPerspectiveMix.For(CameraModeService.Current);
            if (!set.FiresStrings)
            {
                if (_active) Release();
                PlayVoice(set, cue, mix);
                PlayTail(set, cue.Position, mix);
                return true;
            }

            float now = Time.time;
            bool continuing = _active && string.Equals(_set.AudioId, set.AudioId, System.StringComparison.Ordinal)
                && now - _lastShot <= (60f / set.StringRpm) * ReleasePeriods + Time.deltaTime;
            if (!continuing)
            {
                if (_active) Release();
                _active = true;
                _set = set;
            }

            PlayVoice(set, cue, mix);
            _lastShot = now;
            _lastShotFrame = Time.frameCount;
            _position = cue.Position;
            return true;
        }

        /// <summary>Per frame, unpaused: a string's trigger release rings its tail.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            // + this frame's delta: a hitch longer than the release window must not end a string the weapon is still firing
            // (the next shot lands on the frame after the hitch; WP-051C flake). R1-B A1: and at least two frames without a
            // shot — the director and the weapon have no execution order, so one slow frame (GC hitch, < 9 fps) must never
            // read as a trigger release mid-string.
            if (_active && Time.frameCount - _lastShotFrame >= 2
                && Time.time - _lastShot > (60f / _set.StringRpm) * ReleasePeriods + Time.deltaTime)
            {
                Release();
            }
        }

        /// <summary>Run end, death, disable: silence without a tail.</summary>
        public void StopAll()
        {
            _active = false;
        }

        private void Release()
        {
            _active = false;
            PlayTail(_set, _position, WeaponPerspectiveMix.For(CameraModeService.Current));
            Releases++;
        }

        private void PlayVoice(in WeaponVoiceSet set, in CombatAudioCue cue, in WeaponPerspectiveMix mix)
        {
            AudioPlacement p = AudioMixPlan.PlayerWeapon(set.Fire, set.FiresStrings ? StringVoices : AudioMixPlan.PlayerWeaponVoices);
            p.Spatial = cue.Position == Vector3.zero ? 0f : mix.Spatial;
            FireVoices++;
            if (_play(p, cue.Position, Db(mix.NearFieldDb + set.FireTrimDb), cue.Priority)) LastVoice = set.Fire;
            else FireRefused++;
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
