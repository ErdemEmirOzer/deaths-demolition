using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>WP-072 B2: handling foley weight family (DIRECTION §2 / S-02 WP-069 §3 "same family, per-weapon variant").</summary>
    public enum WeaponHandlingFamily
    {
        Light = 0,
        Medium = 1,
        Heavy = 2
    }

    /// <summary>WP-072 B2: what falls out of the ejection port (DIRECTION §3).</summary>
    public enum CasingFamily
    {
        None = -1,
        Rifle = 0,
        Pistol = 1,
        Shell = 2
    }

    /// <summary>
    /// WP-072 B2: the per-weapon reload / handling table. Mechanism events speak with the weapon's own
    /// AP-011 v017 foley (S-02 WP-069 §3 sets under the WP-071 names); the WP-032C events the A/B/C clips
    /// still raise (mag_in, chamber, eject) map onto the nearest v3 action so the old clips keep sounding right.
    /// </summary>
    public static class WeaponFoleyV3
    {
        /// <summary>The equipped weapon's audioId: WP-069 WeaponDefinition.AudioId, else the A/B/C migration.</summary>
        public static string Equipped()
        {
            WeaponController c = WeaponRegistry.ActiveController;
            string id = c != null && c.Classic != null && c.Classic.Definition != null ? c.Classic.Definition.AudioId : null;
            return string.IsNullOrEmpty(id) ? WeaponAudioIdentity.ForClassic(WeaponRegistry.SelectedWeapon) : id;
        }

        /// <summary>The weapon's own mechanism sound for this clip event; None = the weapon has no such action (silent).</summary>
        /// <param name="v3Clip">The TP v3 set (AP-022) plays for this weapon; false = the AP-009 A/B/C class clip (legacy timing).</param>
        /// <param name="reloading">The weapon is reloading (a fire clip's eject / chamber is a per-shot cycle otherwise).</param>
        public static AudioSoundId Mechanism(string audioId, CharacterAnimationEvent e, bool v3Clip = false, bool reloading = true)
        {
            bool magIn = e == CharacterAnimationEvent.MagIn || e == CharacterAnimationEvent.MagIn2 || e == CharacterAnimationEvent.MagIn3;
            switch (audioId)
            {
                case "AR1":
                    if (e == CharacterAnimationEvent.MagOut) return AudioSoundId.FoleyAr1MagOut;
                    if (magIn) return AudioSoundId.FoleyAr1MagIn;
                    if (e == CharacterAnimationEvent.Chamber) return AudioSoundId.FoleyAr1Chamber;
                    if (e == CharacterAnimationEvent.Charge) return AudioSoundId.FoleyAr1Charge;
                    break;
                case "AR2":
                    if (e == CharacterAnimationEvent.MagOut) return AudioSoundId.FoleyAr2MagOut;
                    if (magIn) return AudioSoundId.FoleyAr2MagIn;
                    if (e == CharacterAnimationEvent.Charge || e == CharacterAnimationEvent.Chamber) return AudioSoundId.FoleyAr2Charge;
                    break;
                case "SMG1":
                    if (e == CharacterAnimationEvent.MagOut) return AudioSoundId.FoleySmg1MagOut;
                    if (magIn) return AudioSoundId.FoleySmg1MagIn;
                    if (e == CharacterAnimationEvent.Chamber || e == CharacterAnimationEvent.Charge) return AudioSoundId.FoleySmg1Chamber;
                    break;
                case "SMG2":
                    if (e == CharacterAnimationEvent.MagOut) return AudioSoundId.FoleySmg2MagOut;
                    if (magIn) return AudioSoundId.FoleySmg2MagIn;   // mag_in_2: the second drum seats with the same clunk (pitch jitter)
                    if (e == CharacterAnimationEvent.Chamber || e == CharacterAnimationEvent.Charge) return AudioSoundId.FoleySmg2Chamber;
                    break;
                case "SG1":
                    if (magIn || e == CharacterAnimationEvent.ShellIn) return AudioSoundId.FoleySg1ShellIn;
                    if (e == CharacterAnimationEvent.Eject) return AudioSoundId.FoleySg1Eject;
                    if (e == CharacterAnimationEvent.Chamber) return AudioSoundId.FoleySg1Chamber;
                    break;
                case "SG2":
                    // Break-action, two shells: on the shared C (pump) clips no per-shot cycle and no third shell (WP-072 V6 mix audit).
                    if (!v3Clip && !reloading && (e == CharacterAnimationEvent.Eject || e == CharacterAnimationEvent.Chamber)) return AudioSoundId.None;
                    if (!v3Clip && e == CharacterAnimationEvent.MagIn3) return AudioSoundId.None;
                    if (e == CharacterAnimationEvent.Eject) return AudioSoundId.FoleySg2Eject;
                    if (magIn || e == CharacterAnimationEvent.ShellIn) return AudioSoundId.FoleySg2ShellIn;
                    if (e == CharacterAnimationEvent.Chamber) return AudioSoundId.FoleySg2Chamber;
                    break;
                case "LMG1":
                case "LMG2":
                {
                    bool heavy = audioId == "LMG2";
                    // The v3 clips carry box_open / belt_seat / box_close; their mag_out / mag_in would open and close the box twice.
                    if (v3Clip && (e == CharacterAnimationEvent.MagOut || magIn)) return AudioSoundId.None;
                    if (e == CharacterAnimationEvent.BoxOpen || e == CharacterAnimationEvent.MagOut) return heavy ? AudioSoundId.FoleyLmg2BoxOpen : AudioSoundId.FoleyLmg1BoxOpen;
                    if (e == CharacterAnimationEvent.BeltSeat) return heavy ? AudioSoundId.FoleyLmg2BeltSeat : AudioSoundId.FoleyLmg1BeltSeat;
                    if (e == CharacterAnimationEvent.BoxClose || magIn) return heavy ? AudioSoundId.FoleyLmg2BoxClose : AudioSoundId.FoleyLmg1BoxClose;
                    if (e == CharacterAnimationEvent.Charge || e == CharacterAnimationEvent.Chamber) return heavy ? AudioSoundId.FoleyLmg2Charge : AudioSoundId.FoleyLmg1Charge;
                    break;
                }

                case "SR1":
                    // The v3 clips carry the whole bolt cycle; their eject / chamber sit on (or just before) bolt_back / bolt_down.
                    if (v3Clip && (e == CharacterAnimationEvent.Eject || e == CharacterAnimationEvent.Chamber)) return AudioSoundId.None;
                    if (e == CharacterAnimationEvent.MagOut) return AudioSoundId.FoleySr1MagOut;
                    if (magIn) return AudioSoundId.FoleySr1MagIn;
                    if (e == CharacterAnimationEvent.BoltUp) return AudioSoundId.FoleySr1BoltUp;
                    if (e == CharacterAnimationEvent.BoltBack || e == CharacterAnimationEvent.Eject) return AudioSoundId.FoleySr1BoltBack;
                    if (e == CharacterAnimationEvent.BoltFwd) return AudioSoundId.FoleySr1BoltFwd;
                    if (e == CharacterAnimationEvent.BoltDown || e == CharacterAnimationEvent.Chamber) return AudioSoundId.FoleySr1BoltDown;
                    break;
                case "RL1":
                    // The legacy A clip has no rocket_in: its mag_out is the rocket sliding into the tube (the v3 clip has no mag_out).
                    if (e == CharacterAnimationEvent.RocketIn || e == CharacterAnimationEvent.MagOut) return AudioSoundId.FoleyRl1RocketIn;
                    if (e == CharacterAnimationEvent.RocketSeat || magIn) return AudioSoundId.FoleyRl1RocketSeat;
                    break;
            }

            return AudioSoundId.None;
        }

        public static WeaponHandlingFamily Family(string audioId)
        {
            if (string.IsNullOrEmpty(audioId)) return WeaponHandlingFamily.Medium;
            if (audioId.StartsWith("SMG", System.StringComparison.Ordinal)) return WeaponHandlingFamily.Light;
            if (audioId.StartsWith("LMG", System.StringComparison.Ordinal) || audioId == "RL1") return WeaponHandlingFamily.Heavy;
            return WeaponHandlingFamily.Medium;
        }

        /// <summary>Per-weapon variant inside its handling family (semitones; DIRECTION §2 heavier = lower).</summary>
        public static float HandlingSemitones(string audioId)
        {
            switch (audioId)
            {
                case "AR2": return -1.5f;
                case "SMG1": return 1f;
                case "SMG2": return -1.5f;
                case "SG1": return -0.5f;
                case "SG2": return 0.8f;
                case "LMG2": return -2f;
                case "SR1": return -2f;
                case "RL1": return -1f;
                default: return 0f;
            }
        }

        /// <summary>Handling voice (switch / raised / lowered / ADS / dry fire) of a family; None for other events.</summary>
        public static AudioSoundId Handling(WeaponHandlingFamily family, CharacterAnimationEvent e)
        {
            int slot;
            switch (e)
            {
                case CharacterAnimationEvent.SwitchReady:
                case CharacterAnimationEvent.SwitchAway: slot = 0; break;
                case CharacterAnimationEvent.Raised: slot = 1; break;
                case CharacterAnimationEvent.Lowered: slot = 2; break;
                case CharacterAnimationEvent.AdsReady: slot = 3; break;
                default: return AudioSoundId.None;
            }

            return (AudioSoundId)((int)AudioSoundId.FoleyLightSwitch + (int)family * 5 + slot);
        }

        public static AudioSoundId DryFire(WeaponHandlingFamily family)
        {
            return (AudioSoundId)((int)AudioSoundId.FoleyLightDryFire + (int)family * 5);
        }

        public static CasingFamily Casing(string audioId)
        {
            if (string.IsNullOrEmpty(audioId) || audioId == "RL1") return CasingFamily.None;
            if (audioId.StartsWith("SMG", System.StringComparison.Ordinal)) return CasingFamily.Pistol;
            if (audioId.StartsWith("SG", System.StringComparison.Ordinal)) return CasingFamily.Shell;
            return CasingFamily.Rifle;
        }

        /// <summary>Calibre pitch inside a casing family (7.62 −2 st, .338 −4 st, .45 −2 st; DIRECTION §3).</summary>
        public static float CasingSemitones(string audioId)
        {
            switch (audioId)
            {
                case "AR2":
                case "LMG2":
                case "SMG2": return -2f;
                case "SR1": return -4f;
                default: return 0f;
            }
        }

        /// <summary>Casing voice for a surface; None where a casing makes no sound (water, foliage).</summary>
        public static AudioSoundId CasingOn(CasingFamily family, SurfaceKind surface)
        {
            if (family == CasingFamily.None) return AudioSoundId.None;
            int s;
            switch (surface)
            {
                case SurfaceKind.Water:
                case SurfaceKind.Foliage: return AudioSoundId.None;
                case SurfaceKind.Wood: s = 1; break;
                case SurfaceKind.Dirt: s = 2; break;
                default: s = 0; break;
            }

            return (AudioSoundId)((int)AudioSoundId.CasingRifleStone + (int)family * 3 + s);
        }

        public static float Pitch(float semitones) => Mathf.Pow(2f, semitones / 12f);
    }

    /// <summary>
    /// WP-072 B2: the player's casings land. A <see cref="WeaponMechanismKind.CasingEjected"/> slot becomes a drop
    /// under the ejection port, heard after its fall time (0.25–0.6 s, STANDARDS §6) on the surface it lands on;
    /// LMGs add a belt link. Level by camera mode (STANDARDS §4: top-down a whisper, FP near).
    /// </summary>
    public sealed class CasingAudio
    {
        private const int Capacity = 8;
        private const float Gravity = 9.81f;

        private struct Pending
        {
            public float At;
            public Vector3 Point;
            public AudioSoundId Sound;
            public float Pitch;
            public float GainDb;
        }

        private readonly Pending[] _queue = new Pending[Capacity];
        private readonly WeaponFireAudio.PlayFunc _play;
        private readonly AudioClipLibrary _library;
        private readonly RaycastHit[] _hits = new RaycastHit[4];
        private int _count;
        private bool _subscribed;

        public CasingAudio(AudioClipLibrary library, WeaponFireAudio.PlayFunc play)
        {
            _library = library;
            _play = play;
        }

        /// <summary>Casings heard (test / F1).</summary>
        public int Landed { get; private set; }

        public AudioSoundId LastSound { get; private set; }

        public void Enable()
        {
            if (_subscribed) return;
            WeaponEvents.Mechanism += OnMechanism;
            _subscribed = true;
        }

        public void Disable()
        {
            if (!_subscribed) return;
            WeaponEvents.Mechanism -= OnMechanism;
            _subscribed = false;
            _count = 0;
        }

        public void Clear() => _count = 0;

        private void OnMechanism(WeaponMechanismEvent m)
        {
            if (m.Kind != WeaponMechanismKind.CasingEjected) return;

            string audioId = WeaponFoleyV3.Equipped();
            CasingFamily family = WeaponFoleyV3.Casing(audioId);
            if (family == CasingFamily.None) return;

            // The casing leaves along the port direction and falls; where it lands is what it sounds like.
            Vector3 from = m.Position + (m.Direction.sqrMagnitude > 0.01f ? m.Direction.normalized * 0.6f : Vector3.zero);
            int mask = (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            int n = Physics.RaycastNonAlloc(from + Vector3.up * 0.2f, Vector3.down, _hits, 4f, mask, QueryTriggerInteraction.Ignore);
            if (n == 0) return;

            int best = 0;
            for (int i = 1; i < n; i++) if (_hits[i].distance < _hits[best].distance) best = i;
            RaycastHit hit = _hits[best];
            SurfaceKind surface = AudioSurfaceHints.Resolve(hit.collider, hit.point);
            float fall = Mathf.Clamp(Mathf.Sqrt(2f * Mathf.Max(0.05f, from.y - hit.point.y) / Gravity) + 0.08f, 0.25f, 0.6f);
            float pitch = WeaponFoleyV3.Pitch(WeaponFoleyV3.CasingSemitones(audioId));
            float db = PerspectiveDb(CameraModeService.Current);

            Enqueue(WeaponFoleyV3.CasingOn(family, surface), hit.point, fall, pitch, db);
            if (audioId.StartsWith("LMG", System.StringComparison.Ordinal) && surface != SurfaceKind.Water && surface != SurfaceKind.Foliage)
            {
                Enqueue(AudioSoundId.CasingLink, hit.point, fall + 0.03f, pitch, db - 6f);
            }
        }

        /// <summary>STANDARDS §4 casing audibility: top-down −18, TP −12, FP −8 dB (against the fire voice).</summary>
        public static float PerspectiveDb(CameraMode mode)
        {
            switch (mode)
            {
                case CameraMode.FirstPerson: return -8f;
                case CameraMode.ThirdPerson: return -12f;
                default: return -18f;
            }
        }

        private void Enqueue(AudioSoundId sound, Vector3 point, float delay, float pitch, float db)
        {
            if (sound == AudioSoundId.None || !_library.IsProduced(sound) || _count >= Capacity) return;
            _queue[_count++] = new Pending { At = Time.time + delay, Point = point, Sound = sound, Pitch = pitch, GainDb = db };
        }

        public void Tick()
        {
            float now = Time.time;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_queue[i].At > now) continue;
                Pending p = _queue[i];
                _queue[i] = _queue[--_count];

                AudioPlacement placement = AudioMixPlan.PlayerCasing(p.Sound);
                placement.Pitch = p.Pitch;
                placement.Spatial = CameraModeService.Current == CameraMode.FirstPerson ? 0f : 0.9f;
                // Level against the fire voice (gain 0.85, −18 LUFS files): casing files sit 4 LU lower (−22), PlayerCasing gain 0.5.
                if (_play(placement, p.Point, Mathf.Pow(10f, (p.GainDb + 4f) / 20f) * (0.85f / 0.5f), 8))
                {
                    Landed++;
                    LastSound = p.Sound;
                }
            }
        }
    }
}
