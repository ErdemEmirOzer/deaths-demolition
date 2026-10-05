using System;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-081 B1 (C-00 05.10, R2 U3) · the order the loading key arts come in: a shuffle bag. Every art shows once a
    /// round of <see cref="Count"/> loading screens, in a fresh order each round, and a round never opens with the art
    /// the last one closed on - so the same art never shows on two loading screens in a row, and each comes back every
    /// round (with five arts, four times in twenty loads; any twenty in a row hold three full rounds). No memory across
    /// sessions: a new seed each launch is variety enough, and nothing is written to the player's data.
    /// </summary>
    public sealed class KeyArtRotation
    {
        private readonly int[] _bag;
        private readonly Random _random;
        private int _next;
        private int _last = -1;

        public KeyArtRotation(int count, int seed)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "a rotation needs at least one art");
            }

            _bag = new int[count];
            _random = new Random(seed);
            Refill();
        }

        public int Count
        {
            get { return _bag.Length; }
        }

        /// <summary>The art the next loading screen shows - known ahead, so the curtain can warm it.</summary>
        public int Upcoming
        {
            get { return _bag[_next]; }
        }

        /// <summary>The art the last loading screen showed; -1 before the first.</summary>
        public int Last
        {
            get { return _last; }
        }

        /// <summary>
        /// A loading screen showed <paramref name="index"/>. Normally the upcoming art; any other (a test, a capture)
        /// takes its turn out of this round instead, so the upcoming art is still never the one just shown.
        /// </summary>
        public void Shown(int index)
        {
            if (index < 0 || index >= _bag.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int at = Array.IndexOf(_bag, index, _next);
            if (at > _next)
            {
                _bag[at] = _bag[_next];
                _bag[_next] = index;
            }

            if (at >= 0)
            {
                _next++;
            }

            _last = index;
            if (_next >= _bag.Length)
            {
                Refill();
            }
        }

        private void Refill()
        {
            for (int i = 0; i < _bag.Length; i++)
            {
                _bag[i] = i;
            }

            for (int i = _bag.Length - 1; i > 0; i--)
            {
                Swap(i, _random.Next(i + 1));
            }

            if (_bag.Length > 1 && _bag[0] == _last)
            {
                Swap(0, 1 + _random.Next(_bag.Length - 1));   // the seam between two rounds is a load too
            }

            _next = 0;
        }

        private void Swap(int a, int b)
        {
            int kept = _bag[a];
            _bag[a] = _bag[b];
            _bag[b] = kept;
        }
    }
}
