using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// Uniform spatial hash over the XZ plane, built fresh every solver tick.
    ///
    /// BUDGET NOTE
    /// -----------
    /// The naive alternative is 40*39/2 = 780 pair tests per tick. With a 1.8 m cell and
    /// a real crowd that clumps in front of the player, the 3x3 neighbourhood scan lands
    /// around 150-350 pair tests, and - more importantly - the cost does not explode when
    /// the debug panel forces all 40 agents into one corridor.
    ///
    /// ALLOCATION: every buffer is allocated once in <see cref="Configure"/> and reused.
    /// A tick allocates nothing. Bucket collisions are resolved exactly (not
    /// approximately) by storing the packed cell key per item and comparing it during
    /// iteration, so an item is never counted twice.
    /// </summary>
    internal sealed class CrowdSpatialGrid
    {
        private int[] _bucketHead = System.Array.Empty<int>();
        private int[] _next = System.Array.Empty<int>();
        private int[] _cellKey = System.Array.Empty<int>();
        private int _bucketMask;
        private float _cellSize = 1.8f;
        private float _invCellSize = 1f / 1.8f;
        private int _count;

        public float CellSize => _cellSize;

        /// <summary>Item count of the last build. Debug readout only.</summary>
        public int Count => _count;

        /// <summary>Sizes the buffers. Safe to call every tick; only reallocates on growth.</summary>
        public void Configure(int capacity, float cellSize)
        {
            cellSize = Mathf.Max(0.25f, cellSize);
            _cellSize = cellSize;
            _invCellSize = 1f / cellSize;

            if (_next.Length < capacity)
            {
                _next = new int[Mathf.NextPowerOfTwo(Mathf.Max(16, capacity))];
                _cellKey = new int[_next.Length];
            }

            int buckets = Mathf.NextPowerOfTwo(Mathf.Max(32, capacity * 4));
            if (_bucketHead.Length < buckets)
            {
                _bucketHead = new int[buckets];
                _bucketMask = buckets - 1;
            }
        }

        /// <summary>Empties the grid. O(bucketCount), a few hundred int writes.</summary>
        public void Clear(int count)
        {
            _count = count;
            for (int i = 0; i < _bucketHead.Length; i++)
            {
                _bucketHead[i] = -1;
            }
        }

        public void Insert(int index, Vector3 position)
        {
            int cx = Mathf.FloorToInt(position.x * _invCellSize);
            int cz = Mathf.FloorToInt(position.z * _invCellSize);
            int key = PackKey(cx, cz);
            int bucket = BucketOf(cx, cz);

            _cellKey[index] = key;
            _next[index] = _bucketHead[bucket];
            _bucketHead[bucket] = index;
        }

        public int CellCoordinate(float value)
        {
            return Mathf.FloorToInt(value * _invCellSize);
        }

        /// <summary>First item index in the given cell, or -1.</summary>
        public int First(int cx, int cz, out int key)
        {
            key = PackKey(cx, cz);
            int index = _bucketHead[BucketOf(cx, cz)];
            while (index >= 0 && _cellKey[index] != key)
            {
                index = _next[index];
            }

            return index;
        }

        /// <summary>Next item in the same cell, or -1.</summary>
        public int Next(int index, int key)
        {
            int next = _next[index];
            while (next >= 0 && _cellKey[next] != key)
            {
                next = _next[next];
            }

            return next;
        }

        private static int PackKey(int cx, int cz)
        {
            return ((cx & 0xFFFF) << 16) | (cz & 0xFFFF);
        }

        private int BucketOf(int cx, int cz)
        {
            unchecked
            {
                int h = (cx * 73856093) ^ (cz * 19349663);
                return (h & 0x7FFFFFFF) & _bucketMask;
            }
        }
    }
}
