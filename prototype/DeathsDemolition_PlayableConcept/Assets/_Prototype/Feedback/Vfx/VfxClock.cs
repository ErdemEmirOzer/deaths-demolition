using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-104 B4 (S-07) · The presentation clock. Outside the tree preview it IS the game clock — <see cref="Delta"/> = Time.deltaTime,
    /// <see cref="Now"/> = Time.time + the offset earlier previews left — so the game path does not change. While the round-end tree's
    /// live diorama runs (AD-VISUAL §5.4; the world stands at timeScale 0) the six presentation systems read the unscaled clock:
    /// <list type="bullet">
    /// <item>TracerRibbonRenderer — its LateUpdate dt (a diorama shot is a TD synthetic record, <c>Emit</c>);</item>
    /// <item>PooledEffect — per instance: an effect spawned while the preview runs ages on the unscaled clock, one spawned before it
    /// keeps the scaled clock, so the world's holes, scorches and lantern flames do not age away behind the tree screen;</item>
    /// <item>CasingEffect — its fall dt, on the same per-instance clock;</item>
    /// <item>ImpactSystem — the concurrency window of its slots and its two particle systems;</item>
    /// <item>MuzzleSystem — the light fades and the smoke particle system;</item>
    /// <item>ExplosionDirector — the stage clock, the edge smear and the B3Particles systems (RocketVisualSystem shares them).</item>
    /// </list>
    /// <see cref="Now"/> never runs backwards: what a preview advanced is kept as an offset, so time-stamped windows (ImpactSystem
    /// slots, light fades) stay valid after the tree closes. Particle systems registered with <see cref="TrackParticles"/> simulate on
    /// unscaled time while the preview runs and go back to their own setting after it. 0 B per read; no per-frame tick.
    /// </summary>
    public static class VfxClock
    {
        /// <summary>Particle systems the preview switches to unscaled time (ImpactSystem 2, MuzzleSystem 1, B3Particles ≈ 6).</summary>
        public const int ParticleCapacity = 32;

        private static readonly ParticleSystem[] s_particles = new ParticleSystem[ParticleCapacity];
        private static readonly bool[] s_wasUnscaled = new bool[ParticleCapacity];
        private static int s_particleCount;
        private static bool s_preview;
        private static float s_offset;
        private static float s_previewBase;
        private static float s_beginUnscaled;
        private static bool s_warnedFull;

        /// <summary>True while the tree preview drives the presentation (between <see cref="BeginPreview"/> and <see cref="EndPreview"/>).</summary>
        public static bool PreviewActive => s_preview;

        /// <summary>Seconds the presentation clock is ahead of Time.time (what past previews advanced).</summary>
        public static float Offset => s_offset;

        /// <summary>Particle systems registered (tests / F1).</summary>
        public static int TrackedParticles => s_particleCount;

        /// <summary>This frame's presentation step: unscaled while the preview runs, else Time.deltaTime.</summary>
        public static float Delta => s_preview ? Time.unscaledDeltaTime : Time.deltaTime;

        /// <summary>The presentation step of an effect that captured <paramref name="previewClock"/> when it spawned.</summary>
        public static float DeltaFor(bool previewClock) => previewClock ? Time.unscaledDeltaTime : Time.deltaTime;

        /// <summary>Presentation seconds: Time.time + <see cref="Offset"/>; while the preview runs it follows the unscaled clock.</summary>
        public static float Now => s_preview ? s_previewBase + (Time.unscaledTime - s_beginUnscaled) : Time.time + s_offset;

        /// <summary>The value <see cref="Now"/> takes during a preview begun at (<paramref name="beginScaled"/>, <paramref name="beginUnscaled"/>) — pure, for tests.</summary>
        public static float PreviewNow(float beginScaled, float offset, float beginUnscaled, float unscaledNow) =>
            beginScaled + offset + (unscaledNow - beginUnscaled);

        /// <summary>The offset left after a preview that ended at (<paramref name="scaledNow"/>, <paramref name="unscaledNow"/>) — pure, for
        /// tests. Never smaller than before (a preview run at timeScale &gt; 1 must not pull <see cref="Now"/> back).</summary>
        public static float OffsetAfter(float beginScaled, float offset, float beginUnscaled, float scaledNow, float unscaledNow) =>
            Mathf.Max(offset, PreviewNow(beginScaled, offset, beginUnscaled, unscaledNow) - scaledNow);

        /// <summary>The tree preview starts (S-09 diorama). A second call while it runs does nothing.</summary>
        public static void BeginPreview()
        {
            if (s_preview) return;
            s_previewBase = Time.time + s_offset;
            s_beginUnscaled = Time.unscaledTime;
            s_preview = true;
            for (int i = 0; i < s_particleCount; i++)
            {
                ParticleSystem ps = s_particles[i];
                if (ps == null) continue;
                ParticleSystem.MainModule main = ps.main;
                s_wasUnscaled[i] = main.useUnscaledTime;
                main.useUnscaledTime = true;
            }
        }

        /// <summary>The tree preview ends: the clock keeps what it advanced as <see cref="Offset"/>; particle systems go back.</summary>
        public static void EndPreview()
        {
            if (!s_preview) return;
            float now = s_previewBase + (Time.unscaledTime - s_beginUnscaled);
            s_preview = false;
            s_offset = Mathf.Max(s_offset, now - Time.time);   // = OffsetAfter(...): Now continues from `now`, never backwards
            for (int i = 0; i < s_particleCount; i++)
            {
                ParticleSystem ps = s_particles[i];
                if (ps == null) continue;
                ParticleSystem.MainModule main = ps.main;
                main.useUnscaledTime = s_wasUnscaled[i];
            }
        }

        /// <summary>
        /// A presentation particle system that must run in the preview (call once, where it is built). Destroyed systems free
        /// their slot on the next registration; over <see cref="ParticleCapacity"/> the system stays scaled (warned once).
        /// </summary>
        public static void TrackParticles(ParticleSystem ps)
        {
            if (ps == null) return;
            int free = -1;
            for (int i = 0; i < s_particleCount; i++)
            {
                if (ReferenceEquals(s_particles[i], ps)) return;
                if (free < 0 && s_particles[i] == null) free = i;
            }

            if (free < 0)
            {
                if (s_particleCount >= ParticleCapacity)
                {
                    if (!s_warnedFull) Debug.LogWarning("[VfxClock] " + ParticleCapacity + " particle systems tracked; '" + ps.name + "' keeps scaled time in the tree preview.");
                    s_warnedFull = true;
                    return;
                }

                free = s_particleCount++;
            }

            s_particles[free] = ps;
            ParticleSystem.MainModule main = ps.main;
            s_wasUnscaled[free] = main.useUnscaledTime;
            if (s_preview) main.useUnscaledTime = true;
        }

        /// <summary>Tests: end any preview and zero the offset (a later test passing Time.time to a Tick then lines up again).</summary>
        public static void ResetForTests()
        {
            EndPreview();
            s_offset = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_preview = false;
            s_offset = 0f;
            s_previewBase = 0f;
            s_beginUnscaled = 0f;
            s_warnedFull = false;
            for (int i = 0; i < ParticleCapacity; i++)
            {
                s_particles[i] = null;
                s_wasUnscaled[i] = false;
            }

            s_particleCount = 0;
        }
    }
}
