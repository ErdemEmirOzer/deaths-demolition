using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-072 B3: a projectile hitting the world speaks with its calibre (S-02 WP-069 §4 matrix, STANDARDS §6):
    /// <c>ImpactV3{Surface}{Light|Medium|Heavy}</c> from <see cref="ProjectileEvents.Impact"/>. The legacy
    /// SurfaceImpact cue HitscanEngine raises for the same hit (one call earlier) is held for the frame and
    /// dropped when this voice replaced it; a legacy-only impact still plays at LateUpdate. Flesh / armour stay
    /// on the damage path; rockets, orbs and gel on the explosion / Special paths.
    /// </summary>
    public sealed class ImpactAudio
    {
        private const int PendingCapacity = 16;
        private const float SameHitMeters = 0.15f;
        private const float PelletRadius = 1.6f;
        private const float PelletWindow = 0.06f;

        private struct Held
        {
            public CombatAudioCue Cue;
            public bool Replaced;
        }

        private readonly Held[] _held = new Held[PendingCapacity];
        private readonly AudioClipLibrary _library;
        private readonly WeaponFireAudio.PlayFunc _play;
        private int _heldCount;
        private bool _subscribed;

        private int _lastTransaction = -1;
        private Vector3 _lastPoint;
        private float _lastTime = -1f;
        private float _lastRicochet = -1f;

        public ImpactAudio(AudioClipLibrary library, WeaponFireAudio.PlayFunc play)
        {
            _library = library;
            _play = play;
        }

        /// <summary>v3 impacts played (test / F1).</summary>
        public int Played { get; private set; }

        /// <summary>Legacy surface cues this voice replaced (test / F1).</summary>
        public int Replaced { get; private set; }

        public AudioSoundId LastSound { get; private set; }

        /// <summary>True once the v018 set is in the catalog; until then legacy cues are never held.</summary>
        public bool Active => _library.IsProduced(AudioSoundId.ImpactV3StoneMedium);

        public static AudioSoundId For(SurfaceKind surface, WeaponCaliber caliber)
        {
            if ((int)surface > (int)SurfaceKind.Paper || caliber == WeaponCaliber.Explosive) return AudioSoundId.None;
            return (AudioSoundId)((int)AudioSoundId.ImpactV3DefaultLight + (int)surface * 3 + (int)caliber);
        }

        /// <summary>Calibre (L −3 / M 0 / H +2 dB) + surface mix (STANDARDS §6: stone 0, wood −1, dirt −3, water −2, foliage −4 on a −20 file, metal +1, paper −1).</summary>
        public static float GainDb(SurfaceKind surface, WeaponCaliber caliber)
        {
            float c = caliber == WeaponCaliber.Light ? -3f : caliber == WeaponCaliber.Heavy ? 2f : 0f;
            switch (surface)
            {
                case SurfaceKind.Wood: return c - 1f;
                case SurfaceKind.Dirt: return c - 3f;
                case SurfaceKind.Water: return c - 2f;
                case SurfaceKind.Foliage: return c - 4f;
                case SurfaceKind.Metal: return c + 1f;
                case SurfaceKind.Paper: return c - 1f;
                default: return c;
            }
        }

        public void Enable()
        {
            if (_subscribed) return;
            ProjectileEvents.Impact += OnImpact;
            _subscribed = true;
        }

        public void Disable()
        {
            if (!_subscribed) return;
            ProjectileEvents.Impact -= OnImpact;
            _subscribed = false;
            _heldCount = 0;
        }

        /// <summary>Director: hold a legacy SurfaceImpact cue until LateUpdate. False = play it now (v3 not produced / queue full).</summary>
        public bool TryHold(in CombatAudioCue cue)
        {
            // Only a live projectile can follow its legacy cue with an Impact; anything else (props, tests, hitscan) plays now.
            if (!Active || ProjectileSystem.LiveCount == 0 || _heldCount >= PendingCapacity) return false;
            _held[_heldCount++] = new Held { Cue = cue, Replaced = false };
            return true;
        }

        /// <summary>Director LateUpdate: the held cues nobody replaced, oldest first.</summary>
        public int DrainUnreplaced(CombatAudioCue[] into)
        {
            int n = 0;
            for (int i = 0; i < _heldCount; i++)
            {
                if (!_held[i].Replaced && n < into.Length) into[n++] = _held[i].Cue;
            }

            _heldCount = 0;
            return n;
        }

        private void OnImpact(ProjectileImpactInfo info)
        {
            if (info.Flesh || info.Armor || info.Kind == ProjectileKind.Rocket || info.Kind == ProjectileKind.Orb || info.Kind == ProjectileKind.Gel)
            {
                return;
            }

            AudioSoundId sound = For(info.Surface, info.Caliber);
            if (sound == AudioSoundId.None || !_library.IsProduced(sound))
            {
                return;
            }

            // The legacy cue of this very hit is replaced whether or not the pellet rule lets a new voice through.
            for (int i = 0; i < _heldCount; i++)
            {
                if (!_held[i].Replaced && (_held[i].Cue.Position - info.Position).sqrMagnitude <= SameHitMeters * SameHitMeters)
                {
                    _held[i].Replaced = true;
                    Replaced++;
                }
            }

            // Eight pellets on one wall are one impact (the REQ-128 rule the legacy cue had in AudioEvents).
            float now = Time.unscaledTime;
            if (info.TransactionId == _lastTransaction && now - _lastTime < PelletWindow && (info.Position - _lastPoint).sqrMagnitude < PelletRadius * PelletRadius)
            {
                return;
            }

            _lastTransaction = info.TransactionId;
            _lastPoint = info.Position;
            _lastTime = now;

            AudioPlacement p = AudioMixPlan.ProjectileImpact(sound);
            if (_play(p, info.Position, Mathf.Pow(10f, GainDb(info.Surface, info.Caliber) / 20f), 20))
            {
                Played++;
                LastSound = sound;
            }

            // A grazing light / medium round off stone or metal whines away (S-02 §4 "çıt + kıvılcım").
            bool hard = info.Surface == SurfaceKind.Stone || info.Surface == SurfaceKind.Metal;
            if (hard && info.Caliber != WeaponCaliber.Heavy && now - _lastRicochet > 0.15f
                && info.Normal.sqrMagnitude > 0.01f && info.Direction.sqrMagnitude > 0.01f
                && Vector3.Dot(-info.Direction.normalized, info.Normal.normalized) < 0.35f)
            {
                _lastRicochet = now;
                AudioPlacement r = AudioMixPlan.ProjectileImpact(AudioSoundId.Ricochet);
                _play(r, info.Position, Mathf.Pow(10f, -8f / 20f), 15);
            }
        }
    }
}
