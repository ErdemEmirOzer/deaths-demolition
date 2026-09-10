namespace DD.Prototype.Audio
{
    /// <summary>Which gun is speaking. Resolved from the cue intensity, see AudioMixPlan.</summary>
    public enum WeaponVoice
    {
        Smg = 0,
        Rifle = 1,
        Shotgun = 2,
        Heavy = 3
    }

    public static partial class AudioRecipes
    {
        /// <summary>
        /// REQ-121, the layered shot. Three layers, and the WEIGHT of the gun is
        /// expressed purely as their balance:
        ///
        ///                 mechanic   body length   body pitch   tail
        ///   Auto SMG      loud       55 ms         300 -> 110   short and dry
        ///   Assault Rifle medium     85 ms         220 ->  75   medium
        ///   Shotgun       quiet      165 ms        150 ->  45   long, wet
        ///   Rotary Heavy  loudest    100 ms        110 ->  38   medium, metallic
        ///
        /// Read down the mechanic column: the SMG is nearly all bolt clatter and the
        /// shotgun is nearly all chest. That single inversion is what makes them
        /// recognisable at a glance, far more than the 6 dB of level between them - and it
        /// keeps working when they are quiet, when they overlap, and when the player is
        /// half-deafened by a crowd.
        ///
        /// The rotary cannon is deliberately NOT "a big rifle". Its mechanic layer is the
        /// loudest of the four and lands on every shot, so at 15 rounds/second the barrel
        /// clatter fuses into a continuous mechanical texture that nothing else in the
        /// game produces. That is the "ağır mekanik ses" the request asks for.
        /// </summary>
        internal static float[] BuildWeaponFire(WeaponVoice voice, ref SynthRandom rng)
        {
            float total;
            float mechLength, mechHighPass, mechClick, mechGain;
            float bodyLength, toneStart, toneEnd, cutStart, cutEnd, q, noise, drive, bodyGain;
            float slapDelay, slapMix, room, damp, reverbMix;

            switch (voice)
            {
                case WeaponVoice.Smg:
                    total = 0.30f;
                    mechLength = 0.014f; mechHighPass = 2600f; mechClick = 3400f; mechGain = 0.62f;
                    bodyLength = 0.055f; toneStart = 300f; toneEnd = 110f;
                    cutStart = 5200f; cutEnd = 900f; q = 1.5f; noise = 0.75f; drive = 2.2f; bodyGain = 0.80f;
                    slapDelay = 0.031f; slapMix = 0.16f; room = 0.55f; damp = 0.42f; reverbMix = 0.16f;
                    break;

                case WeaponVoice.Shotgun:
                    total = 0.78f;
                    mechLength = 0.020f; mechHighPass = 1500f; mechClick = 1900f; mechGain = 0.30f;
                    bodyLength = 0.165f; toneStart = 150f; toneEnd = 45f;
                    cutStart = 2600f; cutEnd = 260f; q = 2.6f; noise = 0.62f; drive = 3.4f; bodyGain = 1.00f;
                    slapDelay = 0.062f; slapMix = 0.30f; room = 0.86f; damp = 0.24f; reverbMix = 0.34f;
                    break;

                case WeaponVoice.Heavy:
                    total = 0.36f;
                    mechLength = 0.026f; mechHighPass = 900f; mechClick = 1250f; mechGain = 0.72f;
                    bodyLength = 0.100f; toneStart = 110f; toneEnd = 38f;
                    cutStart = 1800f; cutEnd = 180f; q = 2.2f; noise = 0.58f; drive = 3.0f; bodyGain = 0.94f;
                    slapDelay = 0.044f; slapMix = 0.20f; room = 0.62f; damp = 0.38f; reverbMix = 0.20f;
                    break;

                default: // Rifle
                    total = 0.47f;
                    mechLength = 0.016f; mechHighPass = 2200f; mechClick = 2800f; mechGain = 0.46f;
                    bodyLength = 0.085f; toneStart = 220f; toneEnd = 75f;
                    cutStart = 4200f; cutEnd = 600f; q = 2.0f; noise = 0.70f; drive = 2.8f; bodyGain = 0.92f;
                    slapDelay = 0.045f; slapMix = 0.24f; room = 0.72f; damp = 0.34f; reverbMix = 0.24f;
                    break;
            }

            // Per-variation drift. Small enough that the gun keeps its identity, large
            // enough that five shots in a row are not a machine stamping.
            float drift = Detune(ref rng, 45f);
            toneStart *= drift;
            toneEnd *= drift;
            cutStart *= rng.Range(0.94f, 1.07f);
            bodyLength *= rng.Range(0.94f, 1.06f);

            float[] buf = SynthOps.Buffer(total);

            // Layer 1: mechanic.
            AddMechanicLayer(buf, mechLength, mechHighPass, mechClick * drift, mechGain, ref rng);

            // Layer 2: body. Starts a hair after the mechanic - a simultaneous start
            // fuses the two into one flat blast and loses the mechanism.
            int bodyStart = SynthOps.Samples(mechLength * 0.45f);
            AddBodyLayer(buf, bodyStart, bodyLength, toneStart, toneEnd,
                cutStart, cutEnd, q, noise, drive, bodyGain, ref rng);

            // The shotgun gets a second, lower body pass. Eight pellets leave one barrel
            // as one event, but the barrel is wide: the extra pass is the chest thump the
            // single sweep cannot reach.
            if (voice == WeaponVoice.Shotgun)
            {
                AddBodyLayer(buf, bodyStart + SynthOps.Samples(0.006f), bodyLength * 1.4f,
                    92f, 32f, 900f, 120f, 3.0f, 0.35f, 2.4f, 0.55f, ref rng);
            }

            // Layer 3a: discrete slap-back off the far courtyard wall.
            SynthOps.Delay(buf, slapDelay, 0.55f, slapMix);

            // Layer 3b: the room itself.
            AddTailLayer(buf, room, damp, reverbMix);

            return Finish(buf, 0.94f, 0.02f);
        }

        /// <summary>
        /// Empty chamber. Mechanic layer ONLY - no body, no tail. That absence is the
        /// message: the gun moved but nothing left it.
        /// </summary>
        internal static float[] BuildDryFire(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.10f);

            AddMechanicLayer(buf, 0.012f, 2400f, 3100f * Detune(ref rng, 60f), 0.80f, ref rng);

            // A single dry thunk under the click, so it reads as a firing pin and not as a
            // UI error beep.
            float[] thunk = SynthOps.Buffer(0.05f);
            SynthOps.AddTone(thunk, 0, thunk.Length, 190f, 140f, 0.5f);
            SynthOps.LowPass(thunk, 700f);
            SynthOps.ApplyPercussive(thunk, 0, thunk.Length, 0.001f, 0.03f, 2.6f);
            SynthOps.Mix(buf, SynthOps.Samples(0.004f), thunk, 0.5f);

            return Finish(buf, 0.55f);
        }

        /// <summary>Magazine release: two clicks and a spring.</summary>
        internal static float[] BuildReloadStart(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.24f);

            AddMechanicLayer(buf, 0.010f, 1800f, 2100f, 0.7f, ref rng);

            float[] second = SynthOps.Buffer(0.03f);
            SynthOps.AddNoise(second, 0, second.Length, 1f, ref rng);
            SynthOps.AddTone(second, 0, second.Length, 1450f, 1050f, 0.6f, SynthWave.Square);
            SynthOps.HighPass(second, 900f);
            SynthOps.ApplyPercussive(second, 0, second.Length, 0.0006f, 0.018f, 3f);
            SynthOps.NormalizePeak(second, 0.55f);
            SynthOps.Mix(buf, SynthOps.Samples(0.042f), second, 1f);

            // Spring ring. Two close partials beat against each other, which is what a
            // magazine spring actually does.
            float[] spring = SynthOps.Buffer(0.13f);
            SynthOps.AddTone(spring, 0, spring.Length, 618f, 604f, 0.35f);
            SynthOps.AddTone(spring, 0, spring.Length, 931f, 918f, 0.22f);
            SynthOps.ApplyPercussive(spring, 0, spring.Length, 0.002f, 0.10f, 2.2f);
            SynthOps.Mix(buf, SynthOps.Samples(0.05f), spring, 0.6f);

            AddTailLayer(buf, 0.5f, 0.5f, 0.10f);
            return Finish(buf, 0.62f);
        }

        /// <summary>Fresh magazine seated: heavier, lower, no ring.</summary>
        internal static float[] BuildReloadInsert(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.20f);

            AddMechanicLayer(buf, 0.009f, 1200f, 1500f * Detune(ref rng, 40f), 0.55f, ref rng);
            AddBodyLayer(buf, SynthOps.Samples(0.002f), 0.06f, 265f, 150f, 1600f, 420f, 2.4f, 0.5f, 1.6f, 0.85f, ref rng);

            AddTailLayer(buf, 0.5f, 0.5f, 0.12f);
            return Finish(buf, 0.72f);
        }

        /// <summary>Bolt released: the brightest of the three reload beats, and the last.</summary>
        internal static float[] BuildReloadEnd(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.26f);

            AddMechanicLayer(buf, 0.011f, 2600f, 3300f * Detune(ref rng, 40f), 0.85f, ref rng);

            float[] ring = SynthOps.Buffer(0.16f);
            float[] ratios = { 1f, 2.41f, 3.87f };
            float[] gains = { 0.5f, 0.3f, 0.16f };
            SynthOps.AddPartials(ring, 0, ring.Length, 1120f, ratios, gains, 0.10f, 0.62f);
            SynthOps.Mix(buf, SynthOps.Samples(0.006f), ring, 0.7f);

            AddTailLayer(buf, 0.55f, 0.45f, 0.14f);
            return Finish(buf, 0.68f);
        }

        /// <summary>
        /// The rotary cannon's motor, as a seamless loop.
        ///
        /// The spin-UP is not baked into the clip. The loop is a steady barrel rotation at
        /// full speed and the director ramps <c>AudioSource.pitch</c> and volume into it
        /// over ~0.55 s. Doing it that way means the spin-up length can follow the weapon's
        /// real timing without rebuilding a clip, and the motor is genuinely continuous
        /// for the whole 11-second window instead of being retriggered.
        ///
        /// Rotation frequency is 26 Hz - low enough to be felt as individual barrel
        /// passes rather than heard as a pitch, which is what keeps it reading as
        /// machinery.
        /// </summary>
        internal static float[] BuildHeavyMotorLoop(ref SynthRandom rng)
        {
            const float rotationHz = 26f;
            const float loopSeconds = 1.0f; // exactly 26 rotations, so the crossfade is trivial

            float[] buf = SynthOps.Buffer(loopSeconds + 0.08f);

            // Motor whine: saw + fifth, the classic electric-motor pair.
            SynthOps.AddTone(buf, 0, buf.Length, 156f, 156f, 0.30f, SynthWave.Saw);
            SynthOps.AddTone(buf, 0, buf.Length, 234f, 234f, 0.12f, SynthWave.Saw);
            SynthOps.LowPass(buf, 2400f);

            // Barrel pass: one short metallic knock per rotation.
            int period = SynthOps.Samples(1f / rotationHz);
            for (int i = 0; i * period < buf.Length - 400; i++)
            {
                float[] knock = SynthOps.Buffer(0.012f);
                SynthOps.AddNoise(knock, 0, knock.Length, 1f, ref rng);
                SynthOps.AddTone(knock, 0, knock.Length, 880f, 640f, 0.6f);
                SynthOps.BandPass(knock, 400f, 5200f);
                SynthOps.ApplyPercussive(knock, 0, knock.Length, 0.0004f, 0.008f, 3f);
                SynthOps.NormalizePeak(knock, 0.55f);
                SynthOps.Mix(buf, i * period, knock, 1f);
            }

            // Air moving through the mechanism.
            SynthOps.AddPinkNoise(buf, 0, buf.Length, 0.35f, ref rng);
            SynthOps.Saturate(buf, 1.6f);

            float[] seamless = SynthOps.MakeSeamless(buf, 0.08f);
            SynthOps.NormalizePeak(seamless, 0.80f);
            return seamless;
        }

        /// <summary>Motor coasting to a stop. Falling pitch is the whole content.</summary>
        internal static float[] BuildHeavySpinDown(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.85f);

            SynthOps.AddTone(buf, 0, buf.Length, 156f, 34f, 0.45f, SynthWave.Saw, false);
            SynthOps.AddTone(buf, 0, buf.Length, 234f, 51f, 0.18f, SynthWave.Saw, false);
            SynthOps.AddPinkNoise(buf, 0, buf.Length, 0.30f, ref rng);
            SynthOps.LowPass(buf, 2200f);

            // Barrel knocks slowing down with the motor - the tell that this is the same
            // machine that was just spinning.
            float t = 0f;
            float interval = 1f / 26f;
            while (t < 0.78f)
            {
                float[] knock = SynthOps.Buffer(0.014f);
                SynthOps.AddNoise(knock, 0, knock.Length, 1f, ref rng);
                SynthOps.BandPass(knock, 380f, 4200f);
                SynthOps.ApplyPercussive(knock, 0, knock.Length, 0.0004f, 0.009f, 3f);
                SynthOps.NormalizePeak(knock, 0.45f * (1f - t));
                SynthOps.Mix(buf, SynthOps.Samples(t), knock, 1f);

                interval *= 1.16f; // slows geometrically, like a real flywheel
                t += interval;
            }

            SynthOps.ApplyAdsr(buf, 0, buf.Length, 0.01f, 0.4f, 0.55f, 0.35f);
            AddTailLayer(buf, 0.6f, 0.4f, 0.16f);
            return Finish(buf, 0.80f, 0.03f);
        }
    }
}
