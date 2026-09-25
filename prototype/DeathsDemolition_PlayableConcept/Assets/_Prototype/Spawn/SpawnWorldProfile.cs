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
    /// weighs ×0.25 (not "next to me"); inner share over the last 24 spawns above 50 % → inner ×0.7 (target 65 / 35).
    /// </summary>
    public static class SpawnWorldProfile
    {
        /// <summary>The one-line switch (C-00 A): false = v6 values only in a v6 scene.</summary>
        public const bool AlwaysV6 = false;

        [PrototypeOnly(PendingDecisions.None, "WP-076 §5.1 v6 world: preferred band (m), MaxUseful (m); inner gate near-player distance (m) and factor; inner share window (spawns), cap, factor above it; v6 scene name prefix.", true)]
        public const float V6PreferredMin = 22f, V6PreferredMax = 45f, V6MaxUseful = 75f;
        public const float InnerNearMetres = 10f, InnerNearFactor = 0.25f, InnerShareCap = 0.5f, InnerShareFactor = 0.7f;
        public const int InnerShareWindow = 24;
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
            bool v6 = AlwaysV6 || DD.Prototype.World.WorldLayout.Current == DD.Prototype.World.WorldLayoutKind.V6;
            for (int i = 0; i < SceneManager.sceneCount && !v6; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name.StartsWith(V6ScenePrefix, System.StringComparison.Ordinal)) v6 = true;
            }
            IsV6 = v6;
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

        /// <summary>Weight factor on an inner gate (1 for an edge gate or outside the v6 profile).</summary>
        public static float InnerFactor(SpawnPointKind kind, float straightDistance, float innerShare)
        {
            if (kind != SpawnPointKind.Inner) return 1f;
            float f = straightDistance <= InnerNearMetres ? InnerNearFactor : 1f;
            if (innerShare > InnerShareCap) f *= InnerShareFactor;
            return f;
        }

        public static void ResetStatics() { Override = null; IsV6 = false; s_head = 0; s_count = 0; for (int i = 0; i < s_ring.Length; i++) s_ring[i] = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDomain() => ResetStatics();
    }
}
