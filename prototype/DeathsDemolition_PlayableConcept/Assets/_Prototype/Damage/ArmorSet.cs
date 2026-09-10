using UnityEngine;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Damage
{
    /// <summary>
    /// Three independent armour pieces on one body - REQ-080, REQ-081.
    ///
    /// Pure C#: no MonoBehaviour, no Unity object, allocation-free after construction.
    /// One instance is created per pooled enemy and reused for its whole life, so a
    /// crowd of 40 allocates 40 of these once and never again.
    ///
    /// A piece has three states:
    ///   not equipped  - the enemy spawned without it, region is exposed
    ///   protecting    - equipped and durability &gt; 0, region absorbs into armour
    ///   broken        - equipped but durability reached 0, region is exposed again (REQ-084)
    /// The broken state is kept distinct from "not equipped" because the visuals differ:
    /// a broken piece must show the under-cloth where a plate used to be (REQ-063).
    /// </summary>
    public sealed class ArmorSet
    {
        // Indexed by (int)ArmorSlot. Index 0 (None) stays unused so no branch is needed.
        private const int SlotCount = 4;

        private readonly float[] _current = new float[SlotCount];
        private readonly float[] _max = new float[SlotCount];
        private readonly bool[] _equipped = new bool[SlotCount];

        /// <summary>Pieces the enemy spawned with, broken or not.</summary>
        public int EquippedCount { get; private set; }

        /// <summary>Pieces still absorbing damage.</summary>
        public int ProtectingCount { get; private set; }

        /// <summary>True while all three regions are still protected - the REQ-088 case.</summary>
        public bool FullyProtected => ProtectingCount >= 3;

        /// <summary>True when the enemy spawned with no armour at all.</summary>
        public bool IsBare => EquippedCount == 0;

        public bool IsEquipped(ArmorSlot slot)
        {
            int i = (int)slot;
            return i > 0 && i < SlotCount && _equipped[i];
        }

        /// <summary>The piece exists AND still has durability. This is the only test the damage model uses.</summary>
        public bool Protects(ArmorSlot slot)
        {
            int i = (int)slot;
            return i > 0 && i < SlotCount && _equipped[i] && _current[i] > 0f;
        }

        /// <summary>Equipped but out of durability. Drives the "under-cloth showing" visual.</summary>
        public bool IsBroken(ArmorSlot slot)
        {
            int i = (int)slot;
            return i > 0 && i < SlotCount && _equipped[i] && _current[i] <= 0f;
        }

        public bool ProtectsRegion(BodyRegion region) => Protects(DamageRegions.SlotFor(region));

        public float Current(ArmorSlot slot)
        {
            int i = (int)slot;
            return i > 0 && i < SlotCount ? _current[i] : 0f;
        }

        public float Max(ArmorSlot slot)
        {
            int i = (int)slot;
            return i > 0 && i < SlotCount ? _max[i] : 0f;
        }

        /// <summary>0..1 durability. Returns 0 for a piece that was never equipped.</summary>
        public float Ratio(ArmorSlot slot)
        {
            int i = (int)slot;
            if (i <= 0 || i >= SlotCount || !_equipped[i] || _max[i] <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(_current[i] / _max[i]);
        }

        /// <summary>Fit a piece at full durability. Call from spawn only.</summary>
        public void Equip(ArmorSlot slot, float durability)
        {
            int i = (int)slot;
            if (i <= 0 || i >= SlotCount || durability <= 0f)
            {
                return;
            }

            if (!_equipped[i])
            {
                EquippedCount++;
            }
            else if (_current[i] > 0f)
            {
                ProtectingCount--;
            }

            _equipped[i] = true;
            _max[i] = durability;
            _current[i] = durability;
            ProtectingCount++;
        }

        /// <summary>Strip everything. Called on despawn so a pooled instance never leaks state.</summary>
        public void Clear()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                _current[i] = 0f;
                _max[i] = 0f;
                _equipped[i] = false;
            }

            EquippedCount = 0;
            ProtectingCount = 0;
        }

        /// <summary>
        /// Debug / cheat path: shatter every protecting piece at once.
        /// Returns how many pieces actually broke, and writes them into <paramref name="broken"/>
        /// (length must be at least 3) so the caller can fire one event per piece.
        /// </summary>
        public int BreakAll(ArmorSlot[] broken)
        {
            int count = 0;
            for (int i = 1; i < SlotCount; i++)
            {
                if (!_equipped[i] || _current[i] <= 0f)
                {
                    continue;
                }

                _current[i] = 0f;
                ProtectingCount--;
                if (broken != null && count < broken.Length)
                {
                    broken[count] = (ArmorSlot)i;
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// Remove durability from one piece.
        ///
        /// REQ-083 / REQ-090: the return value is CLAMPED to what the piece could still
        /// absorb. Everything above that is thrown away here and is never seen by health.
        /// This method is the single place in the codebase where that surplus dies.
        /// </summary>
        /// <param name="absorbed">Durability actually removed. Always &lt;= amount.</param>
        /// <param name="broke">True when this call took the piece from protecting to broken.</param>
        /// <returns>False when the piece was not protecting and nothing happened.</returns>
        public bool Absorb(ArmorSlot slot, float amount, out float absorbed, out bool broke)
        {
            absorbed = 0f;
            broke = false;

            int i = (int)slot;
            if (i <= 0 || i >= SlotCount || !_equipped[i] || _current[i] <= 0f || amount <= 0f)
            {
                return false;
            }

            absorbed = Mathf.Min(amount, _current[i]);
            _current[i] -= absorbed;

            if (_current[i] <= 0f)
            {
                _current[i] = 0f;
                broke = true;
                ProtectingCount--;
            }

            return true;
        }
    }
}
