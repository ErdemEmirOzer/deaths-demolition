using System;
using System.Collections.Generic;
using System.Diagnostics;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>What music / ambience / HUD / game-feel consumers code against (S-08 WP-021B, S-09 WP-022).</summary>
    public interface IThreatSource
    {
        /// <summary>This frame's snapshot (evaluated at most once per frame, on first read).</summary>
        ThreatSnapshot Current { get; }

        /// <summary>Raised when the music layer or a warning flag changes (not every frame).</summary>
        event Action<ThreatSnapshot> Changed;
    }

    /// <summary>
    /// WP-033 (ADR-033-1) · The threat director: gathers the live game state (enemies from
    /// <see cref="ZombieEnemy.Live"/>, player position / view / health, run phase and round
    /// progress) into fixed buffers and runs <see cref="ThreatModel"/> once per frame on first
    /// read. Read-only over the game: it changes no gameplay value. Allocation-free after warm-up
    /// (buffers grow to the enemy ceiling once); cost ≤ 0.05 ms at 40 enemies (measured in tests).
    /// </summary>
    public static class ThreatDirector
    {
        private const int InitialCapacity = 64;
        private static ThreatEnemyInput[] s_enemies = new ThreatEnemyInput[InitialCapacity];
        private static ThreatState s_state;
        private static ThreatSnapshot s_current;
        private static int s_frame = -1;
        private static float s_lastTime = -1f;
        private static Camera s_camera;
        private static readonly Stopwatch s_watch = new Stopwatch();
        private static readonly Source s_source = new Source();

        /// <summary>Test / probe seam: pin the context instead of reading the scene (null = live).</summary>
        public static ThreatContext? ContextOverride { get; set; }

        public static IThreatSource SourceInstance => s_source;
        public static double LastSampleMilliseconds { get; private set; }
        public static double AverageSampleMilliseconds { get; private set; }
        public static int SampleCount { get; private set; }

        /// <summary>Raised when the layer or a warning flag changes.</summary>
        public static event Action<ThreatSnapshot> Changed;

        /// <summary>This frame's snapshot; evaluated on the first read of the frame.</summary>
        public static ThreatSnapshot Current
        {
            get
            {
                if (s_frame != Time.frameCount) Sample();
                return s_current;
            }
        }

        /// <summary>Force an evaluation now (tests; per-frame callers use <see cref="Current"/>).</summary>
        public static ThreatSnapshot Sample()
        {
            s_watch.Restart();
            float now = Time.time;
            float dt = s_lastTime < 0f ? 0f : Mathf.Clamp(now - s_lastTime, 0f, 0.25f);
            s_lastTime = now;
            s_frame = Time.frameCount;

            ThreatContext ctx = ContextOverride ?? GatherContext();
            int count = GatherEnemies();
            ThreatSnapshot previous = s_current;
            s_current = ThreatModel.Evaluate(ThreatSettings.Active, in ctx, s_enemies, count, ref s_state, dt);
            DD.Prototype.Navigation.ThreatLevelFeed.Publish(s_current.Layer);   // WP-054: the spawn director weighs on-screen points by the layer
            s_watch.Stop();
            LastSampleMilliseconds = s_watch.Elapsed.TotalMilliseconds;
            SampleCount++;
            AverageSampleMilliseconds += (LastSampleMilliseconds - AverageSampleMilliseconds) / SampleCount;

            if (previous.Layer != s_current.Layer || previous.Encircled != s_current.Encircled || previous.RearAttack != s_current.RearAttack || previous.LowHealth != s_current.LowHealth || previous.StrongVersionPresent != s_current.StrongVersionPresent)
            {
                Changed?.Invoke(s_current);
                s_source.Raise(s_current);
            }

            return s_current;
        }

        public static void ResetStatics()
        {
            DD.Prototype.Navigation.ThreatLevelFeed.ResetStatics();
            s_state = default;
            s_current = default;
            s_frame = -1;
            s_lastTime = -1f;
            s_camera = null;
            ContextOverride = null;
            LastSampleMilliseconds = 0; AverageSampleMilliseconds = 0; SampleCount = 0;
            Changed = null;
        }

        private static ThreatContext GatherContext()
        {
            var ctx = new ThreatContext { InFight = false, Health01 = 1f, ViewForward = Vector3.forward };
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig != null)
            {
                ctx.PlayerPosition = rig.transform.position;
                ctx.Health01 = rig.Health != null ? Mathf.Clamp01(rig.Health.Normalized) : 1f;
                if (s_camera == null || !s_camera.isActiveAndEnabled) s_camera = Camera.main;
                Vector3 f = s_camera != null ? s_camera.transform.forward : rig.transform.forward;
                f.y = 0f;
                ctx.ViewForward = f.sqrMagnitude > 1e-6f ? f.normalized : rig.transform.forward;
            }

            RunDirector run = RunDirector.Instance;
            if (run != null)
            {
                ctx.InFight = run.Phase == RunPhase.Fighting;
                ctx.RoundProgress01 = Mathf.Clamp01(run.RoundProgress01);
            }

            return ctx;
        }

        private static int GatherEnemies()
        {
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            if (live.Count > s_enemies.Length) Array.Resize(ref s_enemies, Mathf.NextPowerOfTwo(live.Count));   // grows once to the ceiling (DEC-023: 40)
            int n = 0;
            for (int i = 0; i < live.Count; i++)
            {
                ZombieEnemy z = live[i];
                if (z == null || !z.IsAlive) continue;
                s_enemies[n].Position = z.transform.position;
                s_enemies[n].Tier = z.Tier;
                s_enemies[n].WindingUp = z.AttackPhase == ZombieAttackPhase.Windup;
                n++;
            }

            return n;
        }

        private sealed class Source : IThreatSource
        {
            public ThreatSnapshot Current => ThreatDirector.Current;
            public event Action<ThreatSnapshot> Changed;
            public void Raise(ThreatSnapshot s) => Changed?.Invoke(s);
        }
    }

    /// <summary>
    /// WP-033 · The adapter S-08 binds to <c>MusicDirector</c> (WP-021B) without this package
    /// touching the audio files: intensity (0..1, smoothed) and the DEC-065 layer (0..3, with
    /// hysteresis) of the current frame, plus the accent driver (strong version present).
    /// </summary>
    public static class ThreatMusicFeed
    {
        public static float Intensity => ThreatDirector.Current.Intensity;
        public static int Layer => ThreatDirector.Current.Layer;
        public static bool Accent => ThreatDirector.Current.StrongVersionPresent;
        public static bool InFight => ThreatDirector.Current.InFight;
    }

    /// <summary>WP-033 · The HUD warning signals S-09 reads (WP-022; REQ-105): encirclement, rear attack, low health, strong version.</summary>
    public static class ThreatHudFeed
    {
        public static bool Encircled => ThreatDirector.Current.Encircled;
        public static bool RearAttack => ThreatDirector.Current.RearAttack;
        public static bool LowHealth => ThreatDirector.Current.LowHealth;
        public static bool StrongVersionPresent => ThreatDirector.Current.StrongVersionPresent;
        public static int WindingUpNearby => ThreatDirector.Current.WindingUp;
    }
}
