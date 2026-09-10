using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The one clock behind "a Special Weapon on the ground blinks faster as it runs out".
    ///
    /// WHY THERE IS NO NUMBER ANY MORE
    /// -------------------------------
    /// The ground timer answers ONE question: "is it worth crossing the map for this?"
    /// That is a coarse commitment decision, and a two-digit readout is the wrong shape
    /// for it - it can only be read while standing still, which is the one thing the
    /// player is not doing when it matters. A rhythm is read peripherally, from the world
    /// object itself, while running and dodging.
    ///
    /// WHY THE CURVE IS GEOMETRIC AND NOT LINEAR
    /// -----------------------------------------
    /// Human rhythm perception is ratio-based, not difference-based (Weber-Fechner). A
    /// linear ramp from 0.8 Hz to 6.5 Hz spends its first half in a band the ear and eye
    /// cannot tell apart (0.8 -> 1.6 Hz feels like "no change") and then dumps the entire
    /// perceptible change into the last few seconds, which reads as "nothing, nothing,
    /// nothing, PANIC" - exactly the failure mode the player complained about with the
    /// number.
    ///
    /// So the frequency grows GEOMETRICALLY in elapsed fraction:
    ///
    ///     f(r) = Slow * (Fast / Slow) ^ (1 - r)        r = remaining / duration
    ///
    /// which makes the PERCEIVED rate of change constant: the interval shortens by the
    /// same ratio every second. Over the shipped 22 s ground life the frequency doubles
    /// every 7.3 s, so the lifetime contains three clearly separable gears:
    ///
    ///     t = 0 s   0.80 Hz   a slow heartbeat - "there is something out there"
    ///     t = 7 s   1.56 Hz   "it is going"
    ///     t = 15 s  3.34 Hz   "it is going NOW"
    ///     t = 22 s  6.50 Hz   a strobe
    ///
    /// <see cref="FastHz"/> stops at 6.5 rather than climbing further on purpose: past
    /// roughly 8 Hz a flash stops reading as a beat and starts reading as noise, and it
    /// is also where photosensitivity guidance puts its floor.
    ///
    /// PHASE MUST BE INTEGRATED, NOT EVALUATED
    /// ---------------------------------------
    /// <c>frequency * Time.time</c> looks equivalent and is not: the moment the frequency
    /// changes, the product jumps and the blink visibly stutters. <see cref="Advance"/>
    /// integrates instead, so the phase is continuous no matter how the frequency moves.
    /// That is also what lets the HUD marker and the world beacon share ONE phase value
    /// and be guaranteed to blink on the same beat rather than merely at the same rate.
    /// </summary>
    public static class GroundBlinkClock
    {
        /// <summary>PROTOTYPE-ONLY: blink rate the moment the weapon lands, in Hz.</summary>
        [PrototypeOnly(PendingDecisions.SpecialWeapon,
            "Calm end of the blink curve. Slow enough not to compete with combat for attention.", true)]
        public const float SlowHz = 0.8f;

        /// <summary>PROTOTYPE-ONLY: blink rate as the ground timer reaches zero, in Hz.</summary>
        [PrototypeOnly(PendingDecisions.SpecialWeapon,
            "Urgent end of the blink curve. Held under ~8 Hz so it still reads as a beat.", true)]
        public const float FastHz = 6.5f;

        /// <summary>
        /// PROTOTYPE-ONLY: decay constant of the pulse shape. A sine is too soft to read
        /// as a beat at 0.8 Hz; a struck-bell envelope (instant attack, exponential decay)
        /// reads as a beat at the slow end AND as a strobe at the fast end, from the same
        /// curve. It is also the right shape for the object it is drawn on.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None, "Exponential decay of one blink. Higher = sharper.", true)]
        public const float PulseDecay = 4.2f;

        /// <summary>
        /// PROTOTYPE-ONLY: the blink never goes fully dark. A marker at zero alpha is
        /// indistinguishable from "there is no weapon", which is a different message.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None, "Floor of the blink envelope. Never 0.", true)]
        public const float Floor = 0.28f;

        /// <summary>Phase is wrapped here so a long session cannot lose float precision.</summary>
        private const float PhaseWrap = 1024f;

        /// <summary>
        /// Blink frequency in Hz for a given remaining fraction.
        /// </summary>
        /// <param name="remaining01">1 the moment it lands, 0 the moment it expires.</param>
        public static float FrequencyHz(float remaining01)
        {
            float elapsed = 1f - Mathf.Clamp01(remaining01);
            return SlowHz * Mathf.Pow(FastHz / SlowHz, elapsed);
        }

        /// <summary>
        /// Move the phase forward by one frame. Always integrate; never recompute the
        /// phase from a wall clock, or the blink stutters every time the rate changes.
        /// </summary>
        public static float Advance(float phase, float remaining01, float deltaTime)
        {
            float next = phase + FrequencyHz(remaining01) * Mathf.Max(0f, deltaTime);
            return next >= PhaseWrap ? next - PhaseWrap : next;
        }

        /// <summary>
        /// The blink envelope for a phase: 1 at the instant of the flash, decaying to
        /// <see cref="Floor"/> before the next one.
        /// </summary>
        public static float Intensity01(float phase)
        {
            float fraction = phase - Mathf.Floor(phase);
            float envelope = Mathf.Exp(-PulseDecay * fraction);
            return Floor + (1f - Floor) * envelope;
        }

    }
}
