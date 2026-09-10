namespace DD.Prototype.Enemies
{
    /// <summary>
    /// Tiny xorshift32 PRNG as a struct.
    ///
    /// Why not UnityEngine.Random: this one is per-enemy and seeded, so a zombie's gait,
    /// cloth tone and death reaction are reproducible from its spawn seed alone. That is
    /// what makes a spawn sequence replayable without recording every roll, and it keeps
    /// 40 enemies from all pulling on one shared global stream.
    ///
    /// Struct, no allocation, no virtual calls. Pass by ref when you want the sequence to
    /// advance for the caller.
    /// </summary>
    public struct EnemyRandom
    {
        private uint _state;

        public EnemyRandom(uint seed)
        {
            _state = seed == 0u ? 0x9E3779B9u : seed;
        }

        /// <summary>Current state, so a caller can persist or fork the stream.</summary>
        public uint State => _state;

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

        public float Range(float min, float max)
        {
            return min + (max - min) * Next01();
        }

        /// <summary>Uniform integer in [min, max).</summary>
        public int Range(int min, int max)
        {
            if (max <= min)
            {
                return min;
            }

            return min + (int)(NextUInt() % (uint)(max - min));
        }

        public bool Chance(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return Next01() < probability;
        }

        /// <summary>+1 or -1.</summary>
        public float Sign()
        {
            return (NextUInt() & 1u) == 0u ? 1f : -1f;
        }
    }
}
