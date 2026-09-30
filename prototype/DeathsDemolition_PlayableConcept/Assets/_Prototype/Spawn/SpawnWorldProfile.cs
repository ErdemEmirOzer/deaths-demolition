using DD.Prototype.Core.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Spawn
{
    /// <summary>WP-076 B6 §5.1: an edge gate (the ±55 ring) or an inner gate (behind cover, 34–62 m of path).</summary>
    public enum SpawnPointKind
    {
        Edge = 0,
        Inner = 1
    }

    /// <summary>
    /// WP-076 B6 · the world profile (C-00 decision A, 25.09): the v6 spawn values — preferred band 22–45 m, MaxUseful
    /// 75 m, the inner-gate rules — apply only while a v6 world scene is loaded (World_V6_*); World_AP005 keeps today's
    /// values. When S-05 moves AP005 to ±55 (or v6 becomes the default world) the switch is <see cref="AlwaysV6"/> = true.
    /// Inner gates (SpawnPointMarker Kind = Inner, S-05's v6 builder): a body at ≤ 10 m straight distance from the gate
    /// weighs ×0.25 (not "next to me"); the inner weight is steered toward a 35 % share of the last 24 spawns (target 65 / 35, see InnerSteer).
    /// </summary>
    public static class SpawnWorldProfile
    {
        /// <summary>The one-line switch (C-00 A): false = v6 values only in a v6 scene.</summary>
        public const bool AlwaysV6 = false;

        [PrototypeOnly(PendingDecisions.None, "WP-076 §5.1 v6 world: preferred band (m), MaxUseful (m); inner gate near-player distance (m) and factor; inner share window (spawns) and the hard 50 % cap (a backstop only: relaxed as InnerQuota before Reachability, C-00 30.09 Y5); v6 scene name prefix.", true)]
        public const float V6PreferredMin = 22f, V6PreferredMax = 45f, V6MaxUseful = 75f;
        public const float InnerNearMetres = 10f, InnerNearFactor = 0.25f, InnerShareCap = 0.5f;
        public const int InnerShareWindow = 24;

        /// <summary>
        /// WP-076 (R1 28.09: innerShare24 0.58 with the old step rule "> 50 % → ×0.7", replaced): the inner weight is steered toward
        /// <see cref="InnerShareTarget"/> — factor (odds(target) / odds(share))², floor <see cref="InnerSteerFloor"/>, never above 1 —
        /// on a share with <see cref="InnerSharePrior"/> pseudo-spawns at the target, so the first spawns of a run do not swing it.
        /// Target K10 (C-00 30.09, M-26): the 24-window mean ≤ 0.45 (65 / 35 ± 10). Simulated means (240 draws, 200 seeds) for an
        /// unsteered inner mass of 55 / 65 / 75 / 85 %: 0.415 / 0.444 / 0.466 / 0.484 — K10 holds up to ≈ 65 %; the real V6 mass is
        /// measured per round ([SpawnV6] innerMass). The hard 50 % cap (<see cref="CanRecordInner"/>) is only the backstop.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None, "WP-076 §5.1 / M-26 (C-00 30.09): inner-share steering target, steering floor, prior pseudo-spawns; K10 = window mean ≤ 0.45, holds up to ≈ 65 % unsteered inner mass (OPEN above: exponent / target by the innerMass measurement).", true)]
        public const float InnerShareTarget = 0.35f, InnerSteerFloor = 0.1f;
        public const int InnerSharePrior = 6;
        public const string V6ScenePrefix = "World_V6";

        /// <summary>Tests: force the profile (null = read the loaded scenes).</summary>
        public static bool? Override { get; set; }
        public static bool IsV6 { get; private set; }

        private static readonly bool[] s_ring = new bool[InnerShareWindow];
        private static int s_head, s_count;

        /// <summary>Called at round start (scenes do not change inside a round).</summary>
        public static void Refresh()
        {
            if (Override.HasValue) { IsV6 = Override.Value; return; }
            // WP-076 B6c: the world bootstrap's layout signal (S-05 WP-074 B1b) — the greybox and the World v5 scene share the
            // v6 layout under different names; the scene-name check stays only as a fallback for a v6 scene without the signal
            IsV6 = Resolve(DD.Prototype.World.WorldLayout.Current, AnyV6SceneLoaded());
        }

        /// <summary>The profile for a layout signal and whether a <see cref="V6ScenePrefix"/> scene is loaded (pure; tests).</summary>
        public static bool Resolve(DD.Prototype.World.WorldLayoutKind layout, bool v6SceneLoaded)
            => AlwaysV6 || layout == DD.Prototype.World.WorldLayoutKind.V6 || v6SceneLoaded;

        public static bool AnyV6SceneLoaded()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name.StartsWith(V6ScenePrefix, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BindLayout()
        {
            DD.Prototype.World.WorldLayout.Changed -= OnLayoutChanged;
            DD.Prototype.World.WorldLayout.Changed += OnLayoutChanged;
        }

        private static void OnLayoutChanged(DD.Prototype.World.WorldLayoutKind layout) => Refresh();

        /// <summary>Writes the v6 distance values into <paramref name="target"/> (a per-round copy of the config).</summary>
        public static void ApplyV6(SpawnConfigSection target)
        {
            if (target == null) return;
            target.PreferredDistanceMin = V6PreferredMin;
            target.PreferredDistanceMax = V6PreferredMax;
            target.MaxUsefulDistance = V6MaxUseful;
        }

        public static void RecordSpawn(SpawnPointKind kind)
        {
            s_ring[s_head] = kind == SpawnPointKind.Inner;
            s_head = (s_head + 1) % s_ring.Length;
            if (s_count < s_ring.Length) s_count++;
        }

        public static float InnerShare
        {
            get
            {
                if (s_count == 0) return 0f;
                int inner = 0;
                for (int i = 0; i < s_count; i++) if (s_ring[i]) inner++;
                return (float)inner / s_count;
            }
        }

        /// <summary>
        /// Whether the next inner spawn keeps the rolling share at the cap, accounting for the expiring oldest record. WP-076 B9 (Y5):
        /// on the same prior as <see cref="SteeringShare"/> (<see cref="InnerSharePrior"/> pseudo-spawns at the target), so the
        /// first spawn of a session may be inner; the raw 24-window share still stays ≤ 0.5 at 12 and 24 spawns.
        /// </summary>
        public static bool CanRecordInner
        {
            get
            {
                int inner = 0;
                for (int i = 0; i < s_count; i++) if (s_ring[i]) inner++;
                if (s_count == s_ring.Length && s_ring[s_head]) inner--;
                int nextCount = Mathf.Min(s_count + 1, s_ring.Length);
                return inner + 1 + InnerShareTarget * InnerSharePrior <= InnerShareCap * (nextCount + InnerSharePrior);
            }
        }

        /// <summary>The share the weights steer on: the last <see cref="InnerShareWindow"/> spawns plus the prior at the target.</summary>
        public static float SteeringShare
        {
            get
            {
                int inner = 0;
                for (int i = 0; i < s_count; i++) if (s_ring[i]) inner++;
                return (inner + InnerShareTarget * InnerSharePrior) / (s_count + InnerSharePrior);
            }
        }

        /// <summary>Weight factor on an inner gate (1 for an edge gate or outside the v6 profile).</summary>
        public static float InnerFactor(SpawnPointKind kind, float straightDistance, float innerShare)
        {
            if (kind != SpawnPointKind.Inner) return 1f;
            float f = straightDistance <= InnerNearMetres ? InnerNearFactor : 1f;
            return f * InnerSteer(innerShare);
        }

        /// <summary>(odds(target) / odds(share))², in [<see cref="InnerSteerFloor"/>, 1]; 1 at or below the target.</summary>
        public static float InnerSteer(float share)
        {
            if (share <= InnerShareTarget) return 1f;
            if (share >= 0.999f) return InnerSteerFloor;
            float r = (InnerShareTarget / (1f - InnerShareTarget)) / (share / (1f - share));
            return Mathf.Clamp(r * r, InnerSteerFloor, 1f);
        }

        public static void ResetStatics() { Override = null; IsV6 = false; s_head = 0; s_count = 0; for (int i = 0; i < s_ring.Length; i++) s_ring[i] = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDomain() => ResetStatics();
    }
}
