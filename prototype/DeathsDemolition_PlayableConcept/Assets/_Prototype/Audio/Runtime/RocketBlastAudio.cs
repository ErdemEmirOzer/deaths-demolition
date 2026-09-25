using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-072 · WP-069 B3: rl_1 Bohiya's cluster bomblets and dud. Every blast (the rocket's and each bomblet's) raises
    /// the legacy Explosion cue through HitFeedbackDirector one call before <see cref="RocketImpacts.Detonated"/> names
    /// it, so while the v021 set is produced the director holds Explosion cues for the frame: a Bomblet replaces its own
    /// cue with a small <c>BombletPop</c>, staggered 60–110 ms so a cluster crackles instead of stacking 2–4 full blasts;
    /// the rocket's own blast (and any other explosion) plays at LateUpdate through the distance layers.
    /// <see cref="RocketImpacts.Dud"/> → <c>RocketDud</c> (clank + ~1 s fizzle).
    /// </summary>
    public sealed class RocketBlastAudio
    {
        private const int Capacity = 8;
        private const float SameBlastMeters = 0.2f;

        private struct Held
        {
            public CombatAudioCue Cue;
            public bool Replaced;
        }

        private struct Pop
        {
            public float At;
            public Vector3 Point;
        }

        private readonly Held[] _held = new Held[Capacity];
        private readonly Pop[] _pops = new Pop[Capacity];
        private readonly AudioClipLibrary _library;
        private readonly WeaponFireAudio.PlayFunc _play;
        private int _heldCount;
        private int _popCount;
        private int _clusterIndex;
        private int _clusterFrame = -1;
        private bool _subscribed;

        public RocketBlastAudio(AudioClipLibrary library, WeaponFireAudio.PlayFunc play)
        {
            _library = library;
            _play = play;
        }

        public bool Active => _library.IsProduced(AudioSoundId.BombletPop);

        public int Bomblets { get; private set; }

        public int Duds { get; private set; }

        public AudioSoundId LastSound { get; private set; }

        public void Enable()
        {
            if (_subscribed) return;
            RocketImpacts.Detonated += OnDetonated;
            RocketImpacts.Dud += OnDud;
            _subscribed = true;
        }

        public void Disable()
        {
            if (!_subscribed) return;
            RocketImpacts.Detonated -= OnDetonated;
            RocketImpacts.Dud -= OnDud;
            _subscribed = false;
            _heldCount = 0;
            _popCount = 0;
        }

        /// <summary>Director: hold an Explosion cue until LateUpdate. False = play it now.</summary>
        public bool TryHold(in CombatAudioCue cue)
        {
            // Only inside a projectile step can a Detonated follow (rl_1 blasts run from ProjectileSystem); tests / other sources play now.
            if (!Active || ProjectileSystem.LiveCount == 0 || _heldCount >= Capacity) return false;
            _held[_heldCount++] = new Held { Cue = cue };
            return true;
        }

        /// <summary>Director LateUpdate: the held Explosion cues no bomblet replaced.</summary>
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

        private void OnDetonated(RocketBlastInfo info)
        {
            if (info.Kind != RocketBlastKind.Bomblet || !Active) return;

            for (int i = 0; i < _heldCount; i++)
            {
                if (!_held[i].Replaced && (_held[i].Cue.Position - info.Origin).sqrMagnitude <= SameBlastMeters * SameBlastMeters)
                {
                    _held[i].Replaced = true;
                    break;
                }
            }

            if (_clusterFrame != Time.frameCount)
            {
                _clusterFrame = Time.frameCount;
                _clusterIndex = 0;
            }

            if (_popCount < Capacity)
            {
                float delay = 0.06f + _clusterIndex * 0.05f + Random.value * 0.02f;   // 60–110 ms apart: a crackle, not a stack
                _pops[_popCount++] = new Pop { At = Time.time + delay, Point = info.Origin };
            }

            _clusterIndex++;
        }

        private void OnDud(RocketDudInfo info)
        {
            if (!_library.IsProduced(AudioSoundId.RocketDud)) return;
            AudioPlacement p = AudioMixPlan.ProjectileImpact(AudioSoundId.RocketDud);
            p.MaxInstances = 2;
            if (_play(p, info.Position, 1f, 40))
            {
                Duds++;
                LastSound = AudioSoundId.RocketDud;
            }
        }

        public void Tick()
        {
            float now = Time.time;
            for (int i = _popCount - 1; i >= 0; i--)
            {
                if (_pops[i].At > now) continue;
                Vector3 at = _pops[i].Point;
                _pops[i] = _pops[--_popCount];
                AudioPlacement p = AudioMixPlan.ProjectileImpact(AudioSoundId.BombletPop);
                p.MinRetrigger = 0.03f;
                p.DuckCombat = 0.8f;
                p.DuckSeconds = 0.2f;
                if (_play(p, at, 1f, 60))
                {
                    Bomblets++;
                    LastSound = AudioSoundId.BombletPop;
                }
            }
        }
    }
}
