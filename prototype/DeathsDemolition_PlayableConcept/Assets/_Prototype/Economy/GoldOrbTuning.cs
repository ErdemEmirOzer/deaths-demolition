using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// Presentation of one gold orb. Everything here is PROTOTYPE-ONLY and every value
    /// is invented: the request describes what a pickup should FEEL like ("a visible
    /// orb, pulled hard from about 3.5 m, a short light and sound on collection, clearly
    /// readable but not neon") and states none of these numbers.
    ///
    /// The gameplay numbers - magnet radius, magnet speed, orb lifetime, contact radius,
    /// orb ceiling - are NOT here. They live in <c>PrototypeConfig.Economy</c> where the
    /// request put them. This class only holds the look.
    ///
    /// It is a serialized field on <see cref="GoldService"/> rather than constants in
    /// <see cref="GoldOrb"/> because the orb is built from code at runtime and has no
    /// Inspector of its own; an orb constant would have been an unreachable number.
    /// </summary>
    [Serializable]
    public sealed class GoldOrbTuning
    {
        [Header("Spawn hop")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Seconds the orb takes to arc from the corpse to its resting spot.", true)]
        public float HopDuration = 0.45f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Peak height of that arc, metres.", true)]
        public float HopHeight = 1.1f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Minimum scatter from the death point, metres.", true)]
        public float ScatterMin = 0.35f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Maximum scatter from the death point, metres.", true)]
        public float ScatterMax = 0.95f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Resting height above the ground, metres.", true)]
        public float RestHeight = 0.45f;

        [Header("Idle")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Vertical bob amplitude, metres.", true)]
        public float BobAmplitude = 0.09f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Bob cycles per second.", true)]
        public float BobFrequency = 1.6f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Spin speed, degrees per second.", true)]
        public float SpinSpeed = 95f;

        [Header("Collection")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Seconds of the collect flash before the orb returns to the pool.", true)]
        public float FlashDuration = 0.14f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Scale the flash pops out to.", true)]
        public float FlashScale = 2.6f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Seconds after the last pickup that the collection streak resets. Drives audio pitch.", true)]
        public float StreakWindow = 0.9f;

        [Header("Look")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Orb diameter, metres.", true)]
        public float Diameter = 0.28f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Warm gold that separates from moss/stone/wood without reading as neon.", true)]
        public Color Color = new Color(1f, 0.78f, 0.30f, 1f);

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Orbs that may carry a real point light at once. Beyond this they are unlit but still visible.", true)]
        public int LightBudget = 10;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Point light range, metres.", true)]
        public float LightRange = 2.4f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Point light intensity.", true)]
        public float LightIntensity = 1.4f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Diameter multiplier applied to a 25-gold Ultra orb; smaller rewards scale down to 1.", true)]
        public float HighValueScale = 1.5f;

        /// <summary>Clamps everything into a range that cannot produce a degenerate orb.</summary>
        public void Sanitize()
        {
            HopDuration = Mathf.Clamp(HopDuration, 0.05f, 2f);
            HopHeight = Mathf.Clamp(HopHeight, 0f, 4f);
            ScatterMin = Mathf.Clamp(ScatterMin, 0f, 4f);
            ScatterMax = Mathf.Clamp(ScatterMax, ScatterMin, 6f);
            RestHeight = Mathf.Clamp(RestHeight, 0.05f, 2f);
            BobAmplitude = Mathf.Clamp(BobAmplitude, 0f, 1f);
            BobFrequency = Mathf.Clamp(BobFrequency, 0f, 8f);
            SpinSpeed = Mathf.Clamp(SpinSpeed, 0f, 720f);
            FlashDuration = Mathf.Clamp(FlashDuration, 0.02f, 1f);
            FlashScale = Mathf.Clamp(FlashScale, 1f, 8f);
            StreakWindow = Mathf.Clamp(StreakWindow, 0f, 5f);
            Diameter = Mathf.Clamp(Diameter, 0.05f, 2f);
            LightBudget = Mathf.Clamp(LightBudget, 0, 64);
            LightRange = Mathf.Clamp(LightRange, 0.2f, 20f);
            LightIntensity = Mathf.Clamp(LightIntensity, 0f, 10f);
            HighValueScale = Mathf.Clamp(HighValueScale, 1f, 4f);
        }

        /// <summary>
        /// Visual size of an orb worth <paramref name="amount"/> gold. The request's
        /// rewards are 2 / 5 / 10 / 25, so a scale that reaches its maximum at 25 makes
        /// an Ultra drop read as a prize without a second asset.
        /// </summary>
        public float ScaleForAmount(int amount)
        {
            float t = Mathf.InverseLerp(2f, 25f, Mathf.Max(0, amount));
            return Diameter * Mathf.Lerp(1f, Mathf.Max(1f, HighValueScale), t);
        }
    }
}
