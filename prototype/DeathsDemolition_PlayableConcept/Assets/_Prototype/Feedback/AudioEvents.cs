using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// Every distinct combat sound this prototype is able to ask for.
    ///
    /// The list is the contract with the audio task (G-09): weapons and the feedback
    /// director only ever raise one of these, they never touch an AudioSource, an
    /// AudioClip or a mixer. Adding a synthesised clip for a value here is a pure
    /// audio-side change.
    ///
    /// The request's "at least these events must be separable" list maps as:
    ///   Classic weapon fire      -> ClassicWeaponFire
    ///   Heavy weapon fire        -> HeavyWeaponFire
    ///   Flesh hit                -> FleshHit / CriticalHit
    ///   Armor hit                -> ArmorHit
    ///   Armor break              -> ArmorBreak
    ///   Enemy death              -> EnemyDeath
    ///   Elite/Ultra death        -> HeavyEnemyDeath
    ///   Special Weapon spawn     -> SpecialWeaponSpawn
    ///   Special Weapon final sec -> SpecialWeaponFinalSeconds
    /// The remaining request events (player damage, gold, round countdown, UI) belong
    /// to other tasks and are intentionally absent from this enum.
    /// </summary>
    public enum CombatAudioEvent
    {
        // ---- weapon mechanics ------------------------------------------------
        ClassicWeaponFire = 0,
        ClassicWeaponDryFire = 1,
        ReloadStart = 2,
        ReloadInsert = 3,
        ReloadEnd = 4,

        HeavyWeaponSpinUp = 10,
        HeavyWeaponFire = 11,
        HeavyWeaponSpinDown = 12,

        // ---- impact ----------------------------------------------------------
        SurfaceImpactDefault = 20,
        SurfaceImpactStone = 21,
        SurfaceImpactWood = 22,
        SurfaceImpactFoliage = 23,
        SurfaceImpactWater = 24,
        SurfaceImpactMetal = 25,
        SurfaceImpactDirt = 26,
        SurfaceImpactPaper = 27,

        // ---- target ----------------------------------------------------------
        FleshHit = 40,
        CriticalHit = 41,
        ArmorHit = 42,
        ArmorBreak = 43,
        EnemyDeath = 44,
        HeavyEnemyDeath = 45,

        // ---- upgrade branches -------------------------------------------------
        Ricochet = 60,
        ChainArc = 61,
        Explosion = 62,

        // ---- special weapon lifecycle ----------------------------------------
        SpecialWeaponSpawn = 80,
        SpecialWeaponBeaconLoop = 81,
        SpecialWeaponPickup = 82,
        SpecialWeaponFinalSeconds = 83,
        SpecialWeaponExpired = 84,
        SpecialWeaponGroundExpired = 85,

        // ---- confirmation ----------------------------------------------------
        HitMarker = 100,
        KillMarker = 101
    }

    /// <summary>One request for a sound. Passed by value; the listener may ignore it.</summary>
    public readonly struct CombatAudioCue
    {
        /// <summary>Which sound.</summary>
        public readonly CombatAudioEvent Event;

        /// <summary>World position. <see cref="Vector3.zero"/> means "non-positional".</summary>
        public readonly Vector3 Position;

        /// <summary>0..1-ish loudness / variation hint. 1 is the nominal presentation.</summary>
        public readonly float Intensity;

        /// <summary>
        /// Higher wins when the voice budget is full. The request demands important
        /// events survive a crowd, so armour breaks and deaths ship with a high value
        /// and ordinary flesh hits with a low one.
        /// </summary>
        public readonly int Priority;

        /// <summary>True when this cue starts a loop that must later be stopped.</summary>
        public readonly bool IsLoop;

        public CombatAudioCue(CombatAudioEvent evt, Vector3 position, float intensity, int priority, bool isLoop)
        {
            Event = evt;
            Position = position;
            Intensity = intensity;
            Priority = priority;
            IsLoop = isLoop;
        }
    }

    /// <summary>
    /// The one-way audio surface.
    ///
    /// Producers call <see cref="Raise"/>. The audio task subscribes to
    /// <see cref="Cue"/> once and does everything else on its side - synthesis, voice
    /// stealing, ducking, spatialisation.
    ///
    /// This class does exactly one piece of budgeting itself, and only because it is
    /// the only place that can: it drops a cue that repeats the same event at almost
    /// the same place within <c>Feedback.DuplicateAudioSuppression</c> seconds. That is
    /// the shotgun-pellet case - eight pellets landing on one torso must be one flesh
    /// hit, not eight (REQ-128). Voice-count budgeting stays on the audio side, which
    /// is the only side that knows what is actually still ringing.
    /// </summary>
    public static class AudioEvents
    {
        private const int RecentCapacity = 32;

        /// <summary>PROTOTYPE-ONLY: two cues closer than this count as the same sound.</summary>
        private const float DuplicateRadius = 1.6f;

        private struct RecentCue
        {
            public CombatAudioEvent Event;
            public Vector3 Position;
            public float Time;
        }

        private static readonly RecentCue[] s_recent = new RecentCue[RecentCapacity];
        private static int s_recentHead;

        /// <summary>Subscribe once, from the audio system. Never raised for suppressed duplicates.</summary>
        public static event Action<CombatAudioCue> Cue;

        /// <summary>
        /// Raised for a cue that was suppressed as a duplicate. Only useful for the F1
        /// panel and for proving the budget is doing something; nothing should play here.
        /// </summary>
        public static event Action<CombatAudioCue> CueSuppressed;

        /// <summary>Number of cues suppressed since the last counter reset. F1 readout.</summary>
        public static int SuppressedCount { get; private set; }

        /// <summary>Number of cues delivered since the last counter reset. F1 readout.</summary>
        public static int DeliveredCount { get; private set; }

        public static void Raise(CombatAudioEvent evt, Vector3 position, float intensity = 1f, int priority = 0)
        {
            RaiseInternal(new CombatAudioCue(evt, position, intensity, priority, false), true);
        }

        /// <summary>Bypasses duplicate suppression. Use for lifecycle events that must never be lost.</summary>
        public static void RaiseCritical(CombatAudioEvent evt, Vector3 position, float intensity = 1f, int priority = 100)
        {
            RaiseInternal(new CombatAudioCue(evt, position, intensity, priority, false), false);
        }

        /// <summary>Starts a looping cue (Special Weapon beacon, rotary spin). Never suppressed.</summary>
        public static void RaiseLoop(CombatAudioEvent evt, Vector3 position, float intensity = 1f, int priority = 80)
        {
            RaiseInternal(new CombatAudioCue(evt, position, intensity, priority, true), false);
        }

        /// <summary>Stops a looping cue previously started with <see cref="RaiseLoop"/>.</summary>
        public static event Action<CombatAudioEvent> LoopStopped;

        /// <summary>Position-aware stop for shared spatial loops. The legacy event remains for observers.</summary>
        public static event Action<CombatAudioEvent, Vector3> LoopStoppedAt;

        public static void StopLoop(CombatAudioEvent evt)
        {
            StopLoop(evt, Vector3.zero);
        }

        public static void StopLoop(CombatAudioEvent evt, Vector3 position)
        {
            LoopStopped?.Invoke(evt);
            LoopStoppedAt?.Invoke(evt, position);
        }

        public static void ResetCounters()
        {
            SuppressedCount = 0;
            DeliveredCount = 0;
        }

        /// <summary>Maps a struck surface to its impact sound.</summary>
        public static CombatAudioEvent ImpactEventFor(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Stone: return CombatAudioEvent.SurfaceImpactStone;
                case SurfaceKind.Wood: return CombatAudioEvent.SurfaceImpactWood;
                case SurfaceKind.Foliage: return CombatAudioEvent.SurfaceImpactFoliage;
                case SurfaceKind.Water: return CombatAudioEvent.SurfaceImpactWater;
                case SurfaceKind.Metal: return CombatAudioEvent.SurfaceImpactMetal;
                case SurfaceKind.Dirt: return CombatAudioEvent.SurfaceImpactDirt;
                case SurfaceKind.Paper: return CombatAudioEvent.SurfaceImpactPaper;
                default: return CombatAudioEvent.SurfaceImpactDefault;
            }
        }

        /// <summary>Maps a resolved feedback tier to its target sound.</summary>
        public static CombatAudioEvent TargetEventFor(HitFeedbackTier tier)
        {
            switch (tier)
            {
                case HitFeedbackTier.ArmorHit: return CombatAudioEvent.ArmorHit;
                case HitFeedbackTier.ArmorBreak: return CombatAudioEvent.ArmorBreak;
                case HitFeedbackTier.CriticalHit: return CombatAudioEvent.CriticalHit;
                case HitFeedbackTier.Death: return CombatAudioEvent.EnemyDeath;
                case HitFeedbackTier.HeavyDeath: return CombatAudioEvent.HeavyEnemyDeath;
                default: return CombatAudioEvent.FleshHit;
            }
        }

        private static void RaiseInternal(CombatAudioCue cue, bool allowSuppression)
        {
            if (allowSuppression && IsDuplicate(cue))
            {
                SuppressedCount++;
                CueSuppressed?.Invoke(cue);
                return;
            }

            Remember(cue);
            DeliveredCount++;

            Action<CombatAudioCue> handler = Cue;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(cue);
            }
            catch (Exception e)
            {
                // A broken listener must never take a weapon down with it.
                Debug.LogException(e);
            }
        }

        private static bool IsDuplicate(in CombatAudioCue cue)
        {
            float window = SuppressionWindow();
            if (window <= 0f)
            {
                return false;
            }

            float now = Time.unscaledTime;
            float sqrRadius = DuplicateRadius * DuplicateRadius;

            for (int i = 0; i < RecentCapacity; i++)
            {
                RecentCue r = s_recent[i];
                if (r.Event != cue.Event)
                {
                    continue;
                }

                if (now - r.Time > window)
                {
                    continue;
                }

                if ((r.Position - cue.Position).sqrMagnitude <= sqrRadius)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Remember(in CombatAudioCue cue)
        {
            s_recent[s_recentHead] = new RecentCue
            {
                Event = cue.Event,
                Position = cue.Position,
                Time = Time.unscaledTime
            };

            s_recentHead = (s_recentHead + 1) % RecentCapacity;
        }

        private static float SuppressionWindow()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            return config != null ? config.Feedback.DuplicateAudioSuppression : 0.04f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cue = null;
            CueSuppressed = null;
            LoopStopped = null;
            LoopStoppedAt = null;
            SuppressedCount = 0;
            DeliveredCount = 0;
            s_recentHead = 0;
            Array.Clear(s_recent, 0, s_recent.Length);
        }
    }
}
