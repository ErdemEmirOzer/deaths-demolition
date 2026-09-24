using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-072 B4: the two DEC-094 Specials (S-02 WP-069 B4 usage events + S-03 WP-076 beats) → AP-011 v019.
    /// Raiju (SW_ARC): charge, fire, one crackle per chain hop (+1 st per hop, 40 ms apart, hops ≥ 6 cluster into
    /// the ones before), fizzle when the orb dies without a body, self-shock, cell out/in, ready tone; per-body shock
    /// loop for the sw_shock hold. Namekuji (SW_GEL): fire, flesh hit, puddle splat + create + loop (per puddle id)
    /// + dry-out, player slide, canister out/in, ready tone; enemy slide / fall. The orb and gel flights are
    /// ProjectileSystem projectiles: their loops are <see cref="ProjectileFlightAudio"/>'s.
    /// </summary>
    public sealed class SpecialAudio
    {
        private const int ShockVoices = 4;
        private const int PuddleVoices = 2;
        private const int MaxHops = 6;
        private const float HopSpacing = 0.04f;
        private const float LoopFade = 0.08f;

        private struct Keyed
        {
            public AudioSource Source;
            public int Key;          // 0 = free
            public float Volume;
            public float Target;
            public float EndAt;      // < 0: until stopped
            public bool Ending;
        }

        private struct Hop
        {
            public float At;
            public Vector3 Point;
            public int Index;
        }

        private readonly Keyed[] _shock = new Keyed[ShockVoices];
        private readonly Keyed[] _puddle = new Keyed[PuddleVoices];
        private readonly Hop[] _hops = new Hop[8];
        private readonly AudioClipLibrary _library;
        private readonly AudioBusMixer _mixer;
        private readonly WeaponFireAudio.PlayFunc _play;
        private int _hopCount;
        private float _lastRaijuHit = -1f;
        private float _lastSlide = -1f;

        public SpecialAudio(AudioClipLibrary library, AudioBusMixer mixer, AudioMixerBridge bridge, Transform root, WeaponFireAudio.PlayFunc play)
        {
            _library = library;
            _mixer = mixer;
            _play = play;
            Build(_shock, "SpecialShock", bridge, root);
            Build(_puddle, "SpecialPuddle", bridge, root);
        }

        /// <summary>A Special is in hand (Activated … Deactivated): the legacy HeavyWeaponFire cue gives way to its own fire.</summary>
        public bool InHand { get; private set; }

        /// <summary>True once the v019 set is in the catalog.</summary>
        public bool Produced => _library.IsProduced(AudioSoundId.SwArcFire);

        public int Played { get; private set; }

        public AudioSoundId LastSound { get; private set; }

        public int LoopsPlaying
        {
            get
            {
                int n = 0;
                for (int i = 0; i < ShockVoices; i++) if (_shock[i].Key != 0 && !_shock[i].Ending) n++;
                for (int i = 0; i < PuddleVoices; i++) if (_puddle[i].Key != 0 && !_puddle[i].Ending) n++;
                return n;
            }
        }

        public int PuddlesPlaying
        {
            get
            {
                int n = 0;
                for (int i = 0; i < PuddleVoices; i++) if (_puddle[i].Key != 0 && !_puddle[i].Ending) n++;
                return n;
            }
        }

        private static void Build(Keyed[] set, string name, AudioMixerBridge bridge, Transform root)
        {
            for (int i = 0; i < set.Length; i++)
            {
                var go = new GameObject(name + " " + i);
                go.transform.SetParent(root, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = true; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 4f; s.maxDistance = 40f; s.dopplerLevel = 0f; s.priority = 24; s.volume = 0f;
                bridge?.Route(s, AudioBus.Impact);
                set[i].Source = s;
            }
        }

        // ------------------------------------------------------------------ usage (S-02 WP-069 B4)

        public bool OnUsage(in SpecialUsageEvent e)
        {
            if (!Produced) return false;
            bool arc = e.Id == SpecialWeaponId.Raiju;
            switch (e.Kind)
            {
                case SpecialUsageKind.Activated:
                    InHand = true;
                    return Play(AudioMixPlan.SpecialFire(arc ? AudioSoundId.SwArcReady : AudioSoundId.SwGelReady), e.Origin, 1f, 50);
                case SpecialUsageKind.Deactivated:
                    InHand = false;
                    return false;
                case SpecialUsageKind.ChargeStarted:
                    return arc && Play(AudioMixPlan.SpecialFire(AudioSoundId.SwArcCharge), e.Origin, 0.8f, 50);   // STANDARDS §2: charge −2 under the fire
                case SpecialUsageKind.Fired:
                    return Play(AudioMixPlan.SpecialFire(arc ? AudioSoundId.SwArcFire : AudioSoundId.SwGelFire), e.Origin, 1f, 55);
                case SpecialUsageKind.ReloadStarted:
                    return Play(AudioMixPlan.WeaponFoley(arc ? AudioSoundId.SwArcCellOut : AudioSoundId.SwGelCanisterOut), e.Origin, 1f, 30);
                case SpecialUsageKind.ReloadEnded:
                    return Play(AudioMixPlan.WeaponFoley(arc ? AudioSoundId.SwArcCellIn : AudioSoundId.SwGelCanisterIn), e.Origin, 1f, 30);
                case SpecialUsageKind.Hit:
                    if (arc)
                    {
                        _lastRaijuHit = Time.time;
                        if (e.Count < MaxHops && _hopCount < _hops.Length)
                        {
                            _hops[_hopCount++] = new Hop { At = Time.time + e.Count * HopSpacing, Point = e.Origin + Vector3.up * 0.6f, Index = e.Count };
                        }

                        return true;
                    }

                    return Play(AudioMixPlan.SpecialHit(AudioSoundId.SwGelHitFlesh), e.Origin + Vector3.up * 0.6f, 1f, 40);
                case SpecialUsageKind.FieldSpawned:
                    if (arc) return false;   // the orb's flight: ProjectileFlightAudio (Orb) owns it
                    Play(AudioMixPlan.SpecialHit(AudioSoundId.SwGelHit), e.Origin, 1f, 40);
                    Play(AudioMixPlan.SpecialField(AudioSoundId.SwGelPuddleCreate), e.Origin, 1f, 50);
                    return StartLoop(_puddle, e.Count != 0 ? e.Count : e.Origin.GetHashCode() | 1, AudioSoundId.SwGelPuddleLoop, e.Origin, 0.63f, -1f);   // −4 dB (STANDARDS §2 −26)
                case SpecialUsageKind.FieldEnded:
                    if (arc)
                    {
                        // The orb died: at its first body the chain speaks; on ground / wall it fizzles.
                        return Time.time - _lastRaijuHit > 0.15f && Play(AudioMixPlan.SpecialField(AudioSoundId.SwArcShockEnd), e.Origin, 1f, 45);
                    }

                    StopLoop(_puddle, e.Count, e.Origin);
                    return Play(AudioMixPlan.SpecialField(AudioSoundId.SwGelPuddleExpire), e.Origin, 1f, 40);
                case SpecialUsageKind.SelfShock:
                {
                    AudioPlacement p = AudioMixPlan.SpecialHit(AudioSoundId.SwArcChainJump);
                    p.Pitch = Mathf.Pow(2f, -3f / 12f);
                    p.Spatial = 0f;
                    return Play(p, e.Origin, 1f, 60);
                }

                case SpecialUsageKind.PlayerSlideStarted:
                    return Play(AudioMixPlan.WeaponFoley(AudioSoundId.SwGelSlidePlayer), e.Origin, 1f, 35);
                default:
                    return false;   // ChargeCancelled (the rising coil is 0.5 s; nothing to cut), Liquefied (the hit spoke), PlayerSlideEnded
            }
        }

        // ------------------------------------------------------------------ enemy beats (S-03 WP-076)

        public bool OnBeat(in EnemyCombatEventInfo info, EnemyTierWeight weight, Vector3 head)
        {
            if (!Produced) return false;
            switch (info.Kind)
            {
                case EnemyCombatEventKind.SpecialShock:
                    return StartLoop(_shock, info.LifeId != 0 ? info.LifeId : 1, AudioSoundId.SwArcShockLoop, head, 0.8f, Mathf.Max(0.3f, info.Seconds));
                case EnemyCombatEventKind.SpecialSlide:
                {
                    float now = Time.unscaledTime;
                    if (now - _lastSlide < 0.06f) return false;   // a crowd slides as one (60 ms clustering)
                    _lastSlide = now;
                    return Play(AudioMixPlan.SpecialReaction(AudioSoundId.SwGelSlideEnemy, weight), info.Position, 1f, 36);
                }

                case EnemyCombatEventKind.SpecialFall:
                    return Play(AudioMixPlan.BodySettle(weight), info.Position, 1f, 30);
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ tick / stop

        public void Tick(float dt)
        {
            float now = Time.time;
            for (int i = _hopCount - 1; i >= 0; i--)
            {
                if (_hops[i].At > now) continue;
                Hop h = _hops[i];
                _hops[i] = _hops[--_hopCount];
                AudioPlacement p = AudioMixPlan.SpecialHit(AudioSoundId.SwArcChainJump);
                p.Pitch = Mathf.Pow(2f, h.Index / 12f);   // +1 st per hop
                p.MinRetrigger = 0.02f;
                Play(p, h.Point, 1f, 45);
            }

            float bus = _mixer.Gain(AudioBus.Impact);
            TickSet(_shock, dt, now, bus, true);
            TickSet(_puddle, dt, now, bus, false);
        }

        public void StopAll()
        {
            _hopCount = 0;
            InHand = false;
            StopSet(_shock);
            StopSet(_puddle);
        }

        private void TickSet(Keyed[] set, float dt, float now, float bus, bool shock)
        {
            for (int i = 0; i < set.Length; i++)
            {
                ref Keyed k = ref set[i];
                if (k.Key == 0) continue;
                if (!k.Ending && k.EndAt > 0f && now >= k.EndAt)
                {
                    k.Ending = true;
                    if (shock) Play(AudioMixPlan.SpecialField(AudioSoundId.SwArcShockEnd), k.Source.transform.position, 0.7f, 30);
                }

                k.Volume = Mathf.MoveTowards(k.Volume, k.Ending ? 0f : k.Target, dt / LoopFade);
                if (k.Ending && k.Volume <= 0.001f)
                {
                    k.Source.Stop();
                    k.Key = 0;
                    k.Ending = false;
                    continue;
                }

                k.Source.volume = k.Volume * bus;
            }
        }

        private static void StopSet(Keyed[] set)
        {
            for (int i = 0; i < set.Length; i++)
            {
                if (set[i].Source != null) set[i].Source.Stop();
                set[i].Key = 0;
                set[i].Ending = false;
            }
        }

        private bool StartLoop(Keyed[] set, int key, AudioSoundId sound, Vector3 at, float gain, float seconds)
        {
            AudioClip clip = _library.First(sound);
            if (clip == null) return false;
            int slot = -1;
            for (int i = 0; i < set.Length; i++)
            {
                if (set[i].Key == key) { slot = i; break; }         // the same body shocked again: extend
                if (slot < 0 && set[i].Key == 0) slot = i;
            }

            if (slot < 0) return false;                             // the pool's cap (4 bodies / 2 puddles)
            ref Keyed k = ref set[slot];
            bool fresh = k.Key != key;
            k.Key = key;
            k.Target = gain;
            k.Ending = false;
            k.EndAt = seconds > 0f ? Time.time + seconds : -1f;
            k.Source.transform.position = at;
            if (fresh)
            {
                k.Volume = 0f;
                k.Source.clip = clip;
                k.Source.time = Random.Range(0f, clip.length * 0.9f);   // shocked bodies do not crackle in phase
                k.Source.Play();
                LastSound = sound;
            }

            return fresh;   // an extension (the same body shocked again, the same puddle republished) is not a new voice
        }

        private static void StopLoop(Keyed[] set, int key, Vector3 at)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < set.Length; i++)
            {
                if (set[i].Key == 0 || set[i].Ending) continue;
                if (set[i].Key == key) { best = i; break; }
                float d = (set[i].Source.transform.position - at).sqrMagnitude;   // no id: the nearest one
                if (key == 0 && d < bestD) { bestD = d; best = i; }
            }

            if (best >= 0) set[best].Ending = true;
        }

        private bool Play(in AudioPlacement p, Vector3 at, float gain, int priority)
        {
            if (!_play(p, at, gain, priority)) return false;
            Played++;
            LastSound = p.Sound;
            return true;
        }
    }
}
