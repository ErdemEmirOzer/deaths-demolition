namespace DD.Prototype.Audio
{
    /// <summary>
    /// Deterministic xorshift32 PRNG used by every recipe in this folder.
    ///
    /// WHY NOT UnityEngine.Random: the whole point of the clip library is that a given
    /// event always sounds like itself. UnityEngine.Random is a shared global stream, so
    /// the order in which other systems draw from it would change the character of a
    /// gunshot between two runs. This struct is seeded from a stable hash of
    /// (event, variation index) and nothing else, so variation #2 of the shotgun is
    /// byte-identical every session while still being audibly different from #1.
    /// </summary>
    public struct SynthRandom
    {
        private uint _state;

        public SynthRandom(int seed)
        {
            // 0 is a fixed point of xorshift, so it must never be the state.
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
            if (_state == 0u)
            {
                _state = 0x9E3779B9u;
            }
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform in [0,1).</summary>
        public float Next01()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>Uniform in [-1,1).</summary>
        public float Bipolar()
        {
            return Next01() * 2f - 1f;
        }

        public float Range(float min, float max)
        {
            return min + (max - min) * Next01();
        }

        /// <summary>Uniform integer in [min,max).</summary>
        public int RangeInt(int min, int max)
        {
            if (max <= min)
            {
                return min;
            }

            return min + (int)(NextUInt() % (uint)(max - min));
        }

        public bool Chance(float probability)
        {
            return Next01() < probability;
        }

        /// <summary>
        /// Stable seed for one (event, variation) pair. Deliberately not
        /// string.GetHashCode: that is not guaranteed stable across runtimes.
        /// </summary>
        public static int Seed(int eventId, int variation)
        {
            unchecked
            {
                int h = 17;
                h = h * 486187739 + eventId;
                h = h * 486187739 + variation;
                h ^= h >> 15;
                return h;
            }
        }
    }
}
