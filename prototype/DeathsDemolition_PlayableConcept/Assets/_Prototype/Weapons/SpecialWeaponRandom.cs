using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-007 (AP-1d, AP-2f): the Special Weapon director's only source of chance, so a
    /// test can inject a seeded sequence and prove "same seed + same inputs = same
    /// schedule / same drop point" (TM-4, TM-18). The three calls mirror
    /// <see cref="Random"/> one to one; the shipped implementation delegates to it, which
    /// keeps the production distribution exactly what it was.
    /// </summary>
    public interface ISpecialWeaponRandom
    {
        /// <summary>Uniform 0..1 (inclusive), as <see cref="Random.value"/>.</summary>
        float Value();

        /// <summary>Uniform in [min, max] (inclusive), as <see cref="Random.Range(float, float)"/>.</summary>
        float Range(float min, float max);

        /// <summary>Uniform integer in [min, maxExclusive), as <see cref="Random.Range(int, int)"/>.</summary>
        int Range(int min, int maxExclusive);
    }

    /// <summary>The shipped source: <see cref="Random"/>, untouched. Stateless, allocation-free, one shared instance.</summary>
    public sealed class UnityRandomSource : ISpecialWeaponRandom
    {
        public static readonly UnityRandomSource Instance = new UnityRandomSource();

        private UnityRandomSource()
        {
        }

        public float Value() => Random.value;

        public float Range(float min, float max) => Random.Range(min, max);

        public int Range(int min, int maxExclusive) => Random.Range(min, maxExclusive);
    }

    /// <summary>
    /// Deterministic source for tests and the development probe: a <see cref="System.Random"/>
    /// seed drives every roll, so two directors fed the same seed and the same inputs make the
    /// same decisions. Not used by production code.
    /// </summary>
    public sealed class SeededSpecialWeaponRandom : ISpecialWeaponRandom
    {
        private readonly System.Random _random;

        /// <summary>How many rolls were consumed. Test readout.</summary>
        public int Calls { get; private set; }

        public SeededSpecialWeaponRandom(int seed)
        {
            _random = new System.Random(seed);
        }

        public float Value()
        {
            Calls++;
            return (float)_random.NextDouble();
        }

        public float Range(float min, float max)
        {
            Calls++;
            return min + (max - min) * (float)_random.NextDouble();
        }

        public int Range(int min, int maxExclusive)
        {
            Calls++;
            return maxExclusive <= min ? min : _random.Next(min, maxExclusive);
        }
    }
}
