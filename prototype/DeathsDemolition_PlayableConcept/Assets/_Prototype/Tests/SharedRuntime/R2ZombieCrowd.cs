using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using UnityEngine;

namespace DD.Prototype.Tests
{
    /// <summary>
    /// R2 (C-00 05.10) · the 40-zombie crowd of the render-budget measurements (Z3b and the SetPassAttribution baseline) with
    /// a FIXED version mix, spawned through EnemyFactory — the director's own mix moves from run to run (zombie draws 377–449
    /// and textures 77–88 MB on one build), which no gate can stand on.
    /// <see cref="GateMix"/> is the gate mix; <see cref="WorstMix"/> the most expensive mix the config's late-round
    /// spawn weights produce (reported, never gated).
    /// </summary>
    public static class R2ZombieCrowd
    {
        public const int Size = 40;
        /// <summary>Standard / Special / Elite / Ultra (C-00 05.10).</summary>
        public static readonly int[] GateMix = { 22, 10, 6, 2 };
        public static readonly EnemyTier[] Tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };
        public const int WorstCaseRound = 30;

        /// <summary>The config's spawn weights at <see cref="WorstCaseRound"/> × 40; Special / Elite / Ultra rounded up (the
        /// costlier versions), Standard takes the rest.</summary>
        public static int[] WorstMix()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            var w = new float[Tiers.Length];
            float sum = 0f;
            for (int i = 0; i < Tiers.Length; i++)
            {
                EnemyTierSettings s = config != null && config.Enemies != null ? config.Enemies.Find(Tiers[i]) : null;
                w[i] = s != null ? Mathf.Max(0f, s.ResolveSpawnWeight(WorstCaseRound)) : 0f;
                sum += w[i];
            }

            var mix = new int[Tiers.Length];
            if (sum <= 0f) { Array.Copy(GateMix, mix, mix.Length); return mix; }
            int rest = Size;
            for (int i = Tiers.Length - 1; i >= 1; i--) { mix[i] = Mathf.Min(rest, Mathf.CeilToInt(Size * w[i] / sum)); rest -= mix[i]; }
            mix[0] = rest;
            return mix;
        }

        /// <summary>
        /// Spawn the mix interleaved (versions spread over the crowd, not in blocks) where the director would: all around the player
        /// at 22–40 m (the V6 spawn band, deterministic angles and radii), give every body the full armour composition (the
        /// WP-038J scenario) and fill <paramref name="spawned"/>. A crowd placed in front of the camera at close range is a
        /// different frame (every body at LOD0, attacking, hit effects): measured 22 SetPass / 1 511 draws against 10 / 377.
        /// </summary>
        public static void Spawn(int[] mix, int round, List<ZombieEnemy> spawned)
        {
            Transform player = PlayerRegistry.Primary.transform;

            var order = new List<EnemyTier>(Size);
            var left = (int[])mix.Clone();
            while (order.Count < Size)
            {
                bool any = false;
                for (int t = 0; t < Tiers.Length; t++) if (left[t] > 0) { order.Add(Tiers[t]); left[t]--; any = true; }
                if (!any) break;
            }

            for (int i = 0; i < order.Count; i++)
            {
                float angle = i * 137.508f;                       // golden-angle spread: every direction, no clumps
                float radius = 22f + (i * 7 % 19);                 // 22–40 m, deterministic
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 p = NavPathFollower.ClampToNavMesh(player.position + dir * radius, 8f);
                ZombieEnemy z = EnemyFactory.Spawn(order[i], round, p, Quaternion.LookRotation(-dir));
                if (z == null) continue;
                if (z.Armor != null && z.Armor.EquippedCount < 3) z.FitArmorComposition(true, true, true);
                spawned.Add(z);
            }
        }

        public static string Describe(int[] mix) => mix[0] + "/" + mix[1] + "/" + mix[2] + "/" + mix[3];
    }
}
