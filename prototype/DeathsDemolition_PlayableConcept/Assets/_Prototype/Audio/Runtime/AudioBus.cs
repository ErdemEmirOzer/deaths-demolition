using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// The five mix buses. Every sound belongs to exactly one, and the frequency plan is
    /// written per bus rather than per sound, because masking is a property of a crowd and
    /// not of a clip.
    ///
    ///   BUS         BAND IT OWNS            WHAT LIVES HERE
    ///   Weapon      120 Hz - 5 kHz          fire, reload, dry fire, rotary motor
    ///   Impact      700 Hz - 9 kHz          the eight surface materials, ricochet, arc
    ///   Target      55 - 900 Hz + 2.6 kHz   flesh, armour, deaths, explosion
    ///   Interface   1.9 - 4 kHz, pitched    hit markers, gold, UI, round flow
    ///   Ambience    below 90 / above 2 kHz  the six beds and the four scatter one-shots
    ///
    /// The bands overlap on paper; what matters is that the LOUDEST element of each bus
    /// sits where no other bus is loud. A weapon body peaks at 200-600 Hz, a surface impact
    /// at 2-5 kHz, a flesh hit under 900 Hz with no transient, a hit marker at exactly
    /// 1.9 kHz in the gap between them. That is why a shotgun into stone with three hit
    /// markers is still four legible events and not one wall.
    /// </summary>
    public enum AudioBus
    {
        Weapon = 0,
        Impact = 1,
        Target = 2,
        Interface = 3,
        Ambience = 4,
        /// <summary>WP-057: the score. Software gain + duck for the music layers and stingers (mixer group "Music").</summary>
        Music = 5
    }

    /// <summary>
    /// Per-bus volume plus the ducking envelope.
    ///
    /// WHY DUCKING AND NOT LEVEL
    /// The request's last audio sentence is "sesler kalabalıkta birbirini boğmasın;
    /// önemli olaylara öncelik ver". The wrong way to honour it is to mix important events
    /// louder: a prototype has maybe 12 dB of usable headroom, and spending it on the
    /// armour break means the armour break is the only thing with any left. The right way
    /// is to make room. When a landmark event fires, this class pulls the buses below it
    /// down by a few dB for 150-400 ms and lets them back up. The landmark does not get
    /// louder; everything else gets quieter, which is both cheaper and far more audible.
    ///
    /// Release is always slower than attack (30 ms down, 350 ms up). A fast release pumps,
    /// and pumping over a fifteen minute session is its own kind of fatigue.
    /// </summary>
    public sealed class AudioBusMixer
    {
        private const int BusCount = 6;

        /// <summary>PROTOTYPE-ONLY / INVENTED: seconds to reach a new duck floor.</summary>
        private const float AttackSeconds = 0.03f;

        /// <summary>PROTOTYPE-ONLY / INVENTED: seconds to recover to unity once released.</summary>
        private const float ReleaseSeconds = 0.35f;

        private readonly float[] _userVolume = new float[BusCount];
        private readonly float[] _playerVolume = { 1f, 1f, 1f, 1f, 1f, 1f };
        private readonly float[] _duckFloor = new float[BusCount];
        private readonly float[] _duckUntil = new float[BusCount];
        private readonly float[] _current = new float[BusCount];

        /// <summary>Master volume, applied on top of every bus. F1 slider.</summary>
        public float MasterVolume { get; set; } = 1f;

        /// <summary>True while the pause menu has the world stopped; every bus reads silent.</summary>
        public bool Muted { get; set; }

        public AudioBusMixer()
        {
            for (int i = 0; i < BusCount; i++)
            {
                // PROTOTYPE-ONLY / INVENTED starting balance. These five numbers ARE the
                // mix: the ambience sits 10 dB under combat, the interface sits just above
                // it so a hit marker is never lost, and impacts are pulled down because
                // there are simply more of them than of anything else.
                _userVolume[i] = 1f;
                _duckFloor[i] = 1f;
                _current[i] = 1f;
            }

            _userVolume[(int)AudioBus.Weapon] = 0.80f;
            _userVolume[(int)AudioBus.Impact] = 0.55f;
            _userVolume[(int)AudioBus.Target] = 0.85f;
            _userVolume[(int)AudioBus.Interface] = 0.70f;
            _userVolume[(int)AudioBus.Ambience] = 0.45f;
        }

        public float GetUserVolume(AudioBus bus)
        {
            return _userVolume[(int)bus];
        }

        /// <summary>WP-068 U8: the player's own multiplier per bus (Settings; today the Interface bus), on top of the design balance above.</summary>
        public void SetPlayerVolume(AudioBus bus, float value)
        {
            _playerVolume[(int)bus] = Mathf.Clamp01(value);
        }

        public float GetPlayerVolume(AudioBus bus) => _playerVolume[(int)bus];

        public void SetUserVolume(AudioBus bus, float value)
        {
            _userVolume[(int)bus] = Mathf.Clamp01(value);
        }

        /// <summary>The number a voice on this bus should be multiplied by right now.</summary>
        public float Gain(AudioBus bus)
        {
            // WP-068 s11: the pause mutes the world, never the interface - the pause menu's own
            // cues (and the pause open / close curtain) must be heard.
            if (Muted && bus != AudioBus.Interface)
            {
                return 0f;
            }

            return _userVolume[(int)bus] * _playerVolume[(int)bus] * _current[(int)bus] * MasterVolume;
        }

        /// <summary>Current duck depth of a bus, 1 = untouched. F1 readout.</summary>
        public float DuckLevel(AudioBus bus)
        {
            return _current[(int)bus];
        }

        /// <summary>
        /// Pull a bus down to <paramref name="floor"/> for <paramref name="seconds"/>.
        /// Deeper requests win over shallower ones; a longer request extends the hold.
        /// </summary>
        public void Duck(AudioBus bus, float floor, float seconds)
        {
            int i = (int)bus;
            floor = Mathf.Clamp01(floor);

            if (floor < _duckFloor[i])
            {
                _duckFloor[i] = floor;
            }

            float until = Time.unscaledTime + Mathf.Max(0.02f, seconds);
            if (until > _duckUntil[i])
            {
                _duckUntil[i] = until;
            }
        }

        /// <summary>Call once per frame with unscaled delta - ducking must survive a hit stop.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            float now = Time.unscaledTime;

            for (int i = 0; i < BusCount; i++)
            {
                bool held = now < _duckUntil[i];
                if (!held)
                {
                    _duckFloor[i] = 1f;
                }

                float target = held ? _duckFloor[i] : 1f;
                float rate = target < _current[i] ? AttackSeconds : ReleaseSeconds;
                float step = unscaledDeltaTime / Mathf.Max(0.001f, rate);

                _current[i] = Mathf.MoveTowards(_current[i], target, step);
            }
        }

        public void Reset()
        {
            for (int i = 0; i < BusCount; i++)
            {
                _duckFloor[i] = 1f;
                _duckUntil[i] = 0f;
                _current[i] = 1f;
            }
        }
    }
}
