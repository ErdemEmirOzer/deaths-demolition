using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// Weighted choice among the registered spawn points (REQ-065, REQ-066, REQ-070).
    ///
    /// Pure and scene-free: it evaluates every point through
    /// <see cref="SpawnWeightCalculator"/>, keeps the weights in a buffer it owns, and
    /// draws one point with an injected random source, so EditMode can prove the
    /// selection rules without a NavMesh, a camera or UnityEngine.Random.
    ///
    /// WHAT IT CAN NEVER DO
    /// --------------------
    /// Return a position. It returns one of the points it was given or null. There is no
    /// code path that fabricates a coordinate; when every point is rejected the answer is
    /// "no spawn this tick", never "spawn somewhere else".
    ///
    /// THE RELAXATION LADDER
    /// ---------------------
    /// <see cref="SelectWithLadder"/> tries the strict pass first and then drops one gate
    /// at a time - screen fairness, then the per-point cooldown, then reachability - the
    /// same order the director used before this class existed. The level that produced
    /// the choice is reported so the director can count how often the map squeezed it.
    /// </summary>
    public sealed class SpawnPointSelector
    {
        private float[] _weights = new float[16];

        /// <summary>Weights from the last evaluation, per point index. Debug / test readout.</summary>
        public IReadOnlyList<float> LastWeights => _weights;

        /// <summary>Sum of the last evaluation's weights. 0 means nothing was eligible.</summary>
        public float LastTotalWeight { get; private set; }

        /// <summary>How many points had a positive weight in the last evaluation.</summary>
        public int LastEligibleCount { get; private set; }

        /// <summary>
        /// Evaluates every point at the given relaxation and stores the weights. Returns
        /// the total; 0 when no point is eligible. Never allocates after the buffer has
        /// grown to the point count.
        /// </summary>
        public float Evaluate(IReadOnlyList<SpawnPointRuntime> points, in SpawnWeightInputs inputs,
            SpawnConfigSection config, SpawnWeightTuning tuning, SpawnRelaxation relaxation)
        {
            LastTotalWeight = 0f;
            LastEligibleCount = 0;

            if (points == null)
            {
                return 0f;
            }

            int count = points.Count;
            if (count == 0)
            {
                return 0f;
            }

            EnsureBuffer(count);

            float total = 0f;
            int eligible = 0;

            for (int i = 0; i < count; i++)
            {
                float weight = SpawnWeightCalculator.Evaluate(points[i], in inputs, config, tuning, relaxation, out _);

                if (weight > 0f)
                {
                    eligible++;
                }

                _weights[i] = weight;
                total += weight;
            }

            LastTotalWeight = total;
            LastEligibleCount = eligible;
            return total;
        }

        /// <summary>
        /// One weighted draw at one relaxation level. <paramref name="random01"/> must
        /// return a value in [0, 1). Null when nothing is eligible.
        /// </summary>
        public SpawnPointRuntime Select(IReadOnlyList<SpawnPointRuntime> points, in SpawnWeightInputs inputs,
            SpawnConfigSection config, SpawnWeightTuning tuning, SpawnRelaxation relaxation, Func<float> random01)
        {
            float total = Evaluate(points, in inputs, config, tuning, relaxation);
            if (total <= 0f)
            {
                return null;
            }

            int count = points.Count;
            float roll = Mathf.Clamp01(random01 != null ? random01() : 0f) * total;

            for (int i = 0; i < count; i++)
            {
                float weight = _weights[i];
                if (weight <= 0f)
                {
                    continue;
                }

                roll -= weight;
                if (roll <= 0f)
                {
                    return points[i];
                }
            }

            // Floating-point tail: the roll outran the sum by an epsilon. Hand back the
            // last eligible point rather than nothing - it IS eligible.
            for (int i = count - 1; i >= 0; i--)
            {
                if (_weights[i] > 0f)
                {
                    return points[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Strict pass, then Screen, then Screen|Cooldown, then everything relaxed.
        /// <paramref name="usedRelaxation"/> reports the level that produced the choice
        /// (None when strict succeeded, or when nothing succeeded at all).
        /// </summary>
        public SpawnPointRuntime SelectWithLadder(IReadOnlyList<SpawnPointRuntime> points,
            in SpawnWeightInputs inputs, SpawnConfigSection config, SpawnWeightTuning tuning,
            Func<float> random01, out SpawnRelaxation usedRelaxation)
        {
            usedRelaxation = SpawnRelaxation.None;

            SpawnPointRuntime chosen = Select(points, in inputs, config, tuning, SpawnRelaxation.None, random01);
            if (chosen != null)
            {
                return chosen;
            }

            chosen = Select(points, in inputs, config, tuning, SpawnRelaxation.Screen, random01);
            if (chosen != null)
            {
                usedRelaxation = SpawnRelaxation.Screen;
                return chosen;
            }

            chosen = Select(points, in inputs, config, tuning,
                SpawnRelaxation.Screen | SpawnRelaxation.Cooldown, random01);
            if (chosen != null)
            {
                usedRelaxation = SpawnRelaxation.Screen | SpawnRelaxation.Cooldown;
                return chosen;
            }

            chosen = Select(points, in inputs, config, tuning,
                SpawnRelaxation.Screen | SpawnRelaxation.Cooldown | SpawnRelaxation.Reachability, random01);
            if (chosen != null)
            {
                usedRelaxation = SpawnRelaxation.Screen | SpawnRelaxation.Cooldown | SpawnRelaxation.Reachability;
                return chosen;
            }

            // WP-076 B9 (C-00 30.09): only the rolling inner cap is left in the way — release it rather than stop spawning
            if (inputs.InnerQuotaFull)
            {
                const SpawnRelaxation valve = SpawnRelaxation.Screen | SpawnRelaxation.Cooldown | SpawnRelaxation.Reachability | SpawnRelaxation.InnerCap;
                chosen = Select(points, in inputs, config, tuning, valve, random01);
                if (chosen != null) usedRelaxation = valve;
            }

            return chosen;
        }

        private void EnsureBuffer(int count)
        {
            if (_weights.Length >= count)
            {
                return;
            }

            _weights = new float[Mathf.NextPowerOfTwo(count)];
        }
    }
}
