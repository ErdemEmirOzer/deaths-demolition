using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// The refill metronome. Counts scaled seconds down to the next refill tick and says
    /// when one is due. A non-positive delta (pause, freeze, round-end hold) leaves it
    /// untouched, so a frozen world never banks spawns for the moment it resumes.
    /// </summary>
    public sealed class SpawnRefillClock
    {
        private float _remaining;
        private int _ticks;

        /// <summary>Seconds until the next tick is due. Never negative after <see cref="Advance"/>.</summary>
        public float Remaining => _remaining;

        /// <summary>Ticks reported since the last <see cref="Reset"/>. Debug readout.</summary>
        public int Ticks => _ticks;

        /// <summary>Arms the clock so the very next positive delta reports a tick.</summary>
        public void Reset()
        {
            _remaining = 0f;
            _ticks = 0;
        }

        /// <summary>
        /// Advances by <paramref name="deltaTime"/>. Returns true when a tick is due and
        /// re-arms with <paramref name="interval"/>. dt &lt;= 0 returns false and changes
        /// nothing: that is the pause contract (REQ-006).
        /// </summary>
        public bool Advance(float deltaTime, float interval)
        {
            if (deltaTime <= 0f)
            {
                return false;
            }

            _remaining -= deltaTime;
            if (_remaining > 0f)
            {
                return false;
            }

            _remaining = Mathf.Max(0.001f, interval);
            _ticks++;
            return true;
        }
    }
}
