using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>WP-076 §4.1: what a chain hop needs to know about a body (ZombieEnemy implements it; other receivers chain freely).</summary>
    public interface IChainTarget
    {
        /// <summary>Alive, not rising from the ground, not sliding / downed in gel.</summary>
        bool CanChain { get; }
    }

    /// <summary>
    /// WP-076 B4 · chain lightning target order, agreed with S-02 (WP-069 DIRECTION §7a, 24.09): the first target is the
    /// body the orb hit (a ground hit fizzles — S-02); then greedy nearest unvisited within the weapon's hop radius
    /// (data, `WeaponArcChainBlock.jumpRadius`) of the current body, one chest→chest Linecast per hop against Environment
    /// (a blocked hop ends the chain), corpses (unregistered) / rising / sliding bodies skipped. The target count is a
    /// game rule (`maxTargets`, tier-independent — a quality tier may thin only the visual arcs, S-07). The Ultra may be
    /// anywhere in the chain and the chain continues from it (🔴 #2 default). S-02 applies the damage hop by hop on its
    /// own schedule and calls <see cref="ZombieEnemy.ApplyShock"/> per target. Pure over the registry, 0 allocation.
    /// </summary>
    public static class ChainTargetResolver
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076: chest height (m) of the hop line of sight.", true)]
        public const float ChestHeight = 1.2f;

        /// <summary>Output capacity callers should allocate once.</summary>
        public const int BufferSize = 24;

        private static readonly List<DamageTarget> s_candidates = new List<DamageTarget>(64);
        private static readonly Transform[] s_visited = new Transform[BufferSize];

        /// <summary>Tests / probes: linecasts issued by the last Resolve.</summary>
        public static int LastLinecasts { get; private set; }

        /// <summary>
        /// Fills <paramref name="output"/> with the chain in hop order (index 0 = <paramref name="first"/>); returns the count.
        /// <paramref name="origin"/> is the orb's contact point (kept for the arc visuals; the order starts at the body).
        /// </summary>
        public static int Resolve(in DamageTarget first, Vector3 origin, int maxTargets, float hopRadius, DamageTarget[] output)
        {
            LastLinecasts = 0;
            if (output == null || output.Length == 0 || !first.IsValid || maxTargets <= 0) return 0;
            output[0] = first;
            s_visited[0] = first.Transform;
            int count = 1;
            int cap = Mathf.Min(maxTargets, Mathf.Min(output.Length, BufferSize));
            float hop = Mathf.Max(0f, hopRadius);
            int mask = 1 << PrototypeLayers.Environment;

            Transform current = first.Transform;
            while (count < cap && current != null)
            {
                Vector3 at = current.position;
                DamageTargetRegistry.Query(at, hop, s_candidates, current);
                int best = -1; float bestSq = float.MaxValue;
                for (int i = 0; i < s_candidates.Count; i++)
                {
                    DamageTarget c = s_candidates[i];
                    if (!c.IsValid || Visited(c.Transform, count)) continue;
                    if (c.Receiver is IChainTarget t && !t.CanChain) continue;
                    Vector3 d = c.Transform.position - at; d.y = 0f;
                    float sq = d.sqrMagnitude;
                    if (sq > hop * hop) continue;   // the registry adds the body radius; the hop is centre to centre
                    if (sq < bestSq) { bestSq = sq; best = i; }
                }
                if (best < 0) break;

                DamageTarget next = s_candidates[best];
                LastLinecasts++;
                if (Physics.Linecast(at + Vector3.up * ChestHeight, next.Transform.position + Vector3.up * ChestHeight, mask, QueryTriggerInteraction.Ignore)) break;
                s_visited[count] = next.Transform;
                output[count++] = next;
                current = next.Transform;
            }
            for (int i = 0; i < count; i++) s_visited[i] = null;
            return count;
        }

        private static bool Visited(Transform t, int count)
        {
            for (int i = 0; i < count; i++) if (s_visited[i] == t) return true;
            return false;
        }
    }
}
