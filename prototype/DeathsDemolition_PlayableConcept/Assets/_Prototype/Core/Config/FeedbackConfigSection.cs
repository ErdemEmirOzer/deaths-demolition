using System;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// PROTOTYPE-ONLY hit-feel and readability budget. The request forbids a global
    /// hit-stop on every automatic bullet and demands the crowd stay readable, so the
    /// simultaneous-effect ceilings live here rather than being hard-coded.
    /// </summary>
    [Serializable]
    public sealed class FeedbackConfigSection
    {
        [Header("Hit stop - only for heavy impacts, armour breaks and heavy deaths")]
        [PrototypeOnly(PendingDecisions.None, "Never applied to ordinary automatic fire.", true)]
        public float HitStopArmorBreak = 0.045f;

        [PrototypeOnly(PendingDecisions.None, "Heavy impact (shotgun / explosion) hit stop.", true)]
        public float HitStopHeavyImpact = 0.035f;

        [PrototypeOnly(PendingDecisions.None, "Elite / Ultra death hit stop.", true)]
        public float HitStopHeavyDeath = 0.07f;

        [PrototypeOnly(PendingDecisions.None, "Minimum seconds between two hit stops, so bursts cannot chain them.", true)]
        public float HitStopMinInterval = 0.25f;

        [Header("Simultaneous effect ceilings - readability budget")]
        [PrototypeOnly(PendingDecisions.None, "Max impact effects alive at once.", true)]
        public int MaxImpactEffects = 40;

        [PrototypeOnly(PendingDecisions.None, "Max blood / flesh effects alive at once.", true)]
        public int MaxBloodEffects = 24;

        [PrototypeOnly(PendingDecisions.None, "Max armour-break bursts alive at once.", true)]
        public int MaxArmorBreakEffects = 12;

        [PrototypeOnly(PendingDecisions.None, "Max death reactions playing at once; the rest use the cheap variant.", true)]
        public int MaxDeathEffects = 12;

        [PrototypeOnly(PendingDecisions.None, "Max floating damage numbers alive at once.", true)]
        public int MaxDamageNumbers = 30;

        [Header("Camera response")]
        [PrototypeOnly(PendingDecisions.None, "Hard ceiling on shake amplitude in metres; aim must stay intact.", true)]
        public float MaxShakeAmplitude = 0.22f;

        [PrototypeOnly(PendingDecisions.None, "Shake decay per second.", true)]
        public float ShakeDecay = 6f;

        [PrototypeOnly(PendingDecisions.None, "Screen-edge damage vignette duration.", true)]
        public float DamageVignetteTime = 0.35f;

        [Header("Stagger discipline")]
        [PrototypeOnly(PendingDecisions.None, "Minimum seconds between two staggers on the same enemy; prevents stun-lock.", true)]
        public float StaggerCooldown = 1.1f;

        [PrototypeOnly(PendingDecisions.None, "Maximum stagger duration.", true)]
        public float MaxStaggerDuration = 0.45f;

        [Header("Audio budget")]
        [PrototypeOnly(PendingDecisions.None, "Max simultaneous one-shot combat sources before the quietest is stolen.", true)]
        public int MaxCombatAudioVoices = 16;

        [PrototypeOnly(PendingDecisions.None, "Seconds a duplicate one-shot is suppressed for.", true)]
        public float DuplicateAudioSuppression = 0.04f;
    }

    /// <summary>Runtime / platform knobs for the prototype build.</summary>
    [Serializable]
    public sealed class SystemConfigSection
    {
        [PrototypeOnly(PendingDecisions.None, "Prototype performance target: ~60 FPS at 1080p.")]
        public int TargetFrameRate = 60;

        [PrototypeOnly(PendingDecisions.None, "0 = uncapped by vsync, target frame rate governs instead.")]
        public int VSyncCount = 0;

        [PrototypeOnly(PendingDecisions.None,
            "Skip Main Menu and Selection, drop straight into a run. Development aid only.")]
        public bool DirectToRun = false;

        [PrototypeOnly(PendingDecisions.None, "Fixed timestep. 50 Hz is enough for a 40-enemy swarm.", true)]
        public float FixedDeltaTime = 0.02f;

        [PrototypeOnly(PendingDecisions.None, "Show the F1 debug panel at all. Off means the key does nothing.")]
        public bool DebugPanelEnabled = true;
    }
}
