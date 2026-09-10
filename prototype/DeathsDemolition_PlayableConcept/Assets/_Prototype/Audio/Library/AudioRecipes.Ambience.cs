namespace DD.Prototype.Audio
{
    public static partial class AudioRecipes
    {
        /// <summary>
        /// Loop length for every ambience bed, in seconds.
        ///
        /// Six seconds is a compromise between two failures. Shorter and the ear finds the
        /// repeat inside a minute; longer and six beds cost more memory than the entire
        /// rest of the library. Six seconds at 44.1 kHz mono float is 1.06 MB per bed, and
        /// every modulation in the beds below has a period that divides six exactly, so
        /// nothing lands mid-cycle at the loop point.
        ///
        /// The real defence against hearing the loop is not the length - it is that the
        /// director runs four beds at once at independent volumes, plus a one-shot
        /// scatterer, so the composite never repeats.
        /// </summary>
        private const float BedSeconds = 6f;

        /// <summary>
        /// Crossfade used to close every bed. 0.6 s is long enough to hide a noise seam
        /// completely and short enough that the bed keeps its shape.
        /// </summary>
        private const float BedCrossfade = 0.6f;

        private static float[] FinishBed(float[] buf, float peak)
        {
            float[] seamless = SynthOps.MakeSeamless(buf, BedCrossfade);
            SynthOps.NormalizePeak(seamless, peak);
            return seamless;
        }

        // ================================================================== beds

        /// <summary>
        /// Wind. The bed that is never off, so it is the bed most likely to cause fatigue.
        ///
        /// Pink noise, not white. The -3 dB/octave tilt is the entire difference between
        /// "wind" and "hiss", and white noise held for a fifteen minute session is the
        /// single most reliable way to give a listener a headache. On top of that:
        ///   * a resonant low pass around 500 Hz gives it a hollow, courtyard-shaped colour;
        ///   * two tremolos at 1/6 Hz and 1/3 Hz, deliberately not harmonically related to
        ///     each other, make the gusts arrive at times the ear cannot predict;
        ///   * everything above 4 kHz is removed. Wind has no top end at this distance and
        ///     the top end is exactly where fatigue lives.
        /// </summary>
        internal static float[] BuildWindBed(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(BedSeconds + BedCrossfade);

            SynthOps.AddPinkNoise(buf, 0, buf.Length, 1f, ref rng);
            SynthOps.BandPass(buf, 90f, 4000f);

            // A second, narrower band an octave up gives the gusts an edge without adding
            // any real high frequency content.
            float[] edge = SynthOps.Buffer(BedSeconds + BedCrossfade);
            SynthOps.AddPinkNoise(edge, 0, edge.Length, 1f, ref rng);
            SynthOps.BandPass(edge, 700f, 2200f);
            SynthOps.ApplyTremolo(edge, 1f / 3f, 0.85f, 0.31f);
            SynthOps.NormalizePeak(edge, 0.30f);
            SynthOps.Mix(buf, 0, edge, 1f);

            // Slow gusting. One full cycle over the loop, so the loop point is a trough.
            SynthOps.ApplyTremolo(buf, 1f / BedSeconds, 0.45f, 0.75f);
            SynthOps.ApplyTremolo(buf, 1f / 2f, 0.14f);

            return FinishBed(buf, 0.55f);
        }

        /// <summary>
        /// Leaves. Sakura in a courtyard: a soft high wash with individual leaf ticks
        /// riding on it.
        ///
        /// It occupies 2-9 kHz, which is above every weapon body and below nothing, so it
        /// can be present without touching the combat mix. The ticks are scattered, not
        /// periodic - a periodic tick in a quiet bed is the loudest thing in any room.
        /// </summary>
        internal static float[] BuildLeafBed(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(BedSeconds + BedCrossfade);

            SynthOps.AddPinkNoise(buf, 0, buf.Length, 1f, ref rng);
            SynthOps.BandPass(buf, 2200f, 9000f);
            SynthOps.ApplyTremolo(buf, 1f / BedSeconds, 0.55f, 0.20f);
            SynthOps.ApplyTremolo(buf, 1f / 1.5f, 0.30f, 0.6f);

            // Individual leaves. 40 over six seconds is sparse enough to hear each one.
            SynthOps.AddGrains(buf, 0, buf.Length - SynthOps.Samples(0.05f),
                40, 2600f, 8500f, 0.004f, 0.016f, 0.30f, ref rng);

            // The grain scatterer skews towards the start; a second pass over the back
            // half evens it out so the bed does not have a busy beginning.
            SynthOps.AddGrains(buf, buf.Length / 2, buf.Length / 2 - SynthOps.Samples(0.05f),
                22, 2600f, 8500f, 0.004f, 0.016f, 0.26f, ref rng);

            return FinishBed(buf, 0.42f);
        }

        /// <summary>
        /// Distant water. A stream or a basin somewhere off the courtyard.
        ///
        /// The trick is that "distant" is not a volume, it is a spectrum: everything above
        /// 2.5 kHz is gone, there is no transient anywhere, and there is a lot of reverb.
        /// Underneath the wash sit slow bubble tones - short falling pitches, the same
        /// physical signature the water impact uses - so the bed and the surface impact
        /// obviously belong to the same substance.
        /// </summary>
        internal static float[] BuildWaterBed(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(BedSeconds + BedCrossfade);

            SynthOps.AddPinkNoise(buf, 0, buf.Length, 1f, ref rng);
            SynthOps.BandPass(buf, 300f, 2500f);
            SynthOps.ApplyTremolo(buf, 1f / 2f, 0.22f, 0.4f);

            // Bubbles: falling pitches, sparse, low. Sixteen over six seconds.
            for (int i = 0; i < 16; i++)
            {
                float t = rng.Range(0.02f, BedSeconds - 0.15f);
                float len = rng.Range(0.05f, 0.13f);
                float f = rng.Range(320f, 1150f);

                float[] bubble = SynthOps.Buffer(len);
                SynthOps.AddTone(bubble, 0, bubble.Length, f, f * 0.34f, 0.8f);
                SynthOps.LowPass(bubble, 1600f);
                SynthOps.ApplyPercussive(bubble, 0, bubble.Length, 0.004f, len * 0.7f, 2.0f);
                SynthOps.NormalizePeak(bubble, rng.Range(0.10f, 0.26f));
                SynthOps.Mix(buf, SynthOps.Samples(t), bubble, 1f);
            }

            SynthOps.Reverb(buf, 0.88f, 0.42f, 0.30f);
            return FinishBed(buf, 0.34f);
        }

        /// <summary>
        /// The palace itself. The request's "sessiz ve boş saray hissi", and the hardest
        /// item in the whole audio brief, because the correct sound for an empty hall is
        /// almost nothing.
        ///
        /// So it is almost nothing: a very quiet 55 Hz / 82.5 Hz pair - the tonic and its
        /// fifth, two octaves below the interface palette - under a narrow band of noise
        /// swept slowly around 200-600 Hz, then run through the longest reverb in the
        /// library. There is no event in it and there is nothing to listen to. What it
        /// does is give every other sound in the game a floor to sit on; mute it and the
        /// courtyard immediately sounds like a menu.
        ///
        /// Peak is 0.22, the quietest clip in the library by a factor of two.
        /// </summary>
        internal static float[] BuildPalaceTone(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(BedSeconds + BedCrossfade);

            // The room's own resonance. Two octaves below A, plus its fifth, plus a very
            // slightly detuned copy so the pair beats once every few seconds.
            SynthOps.AddTone(buf, 0, buf.Length, 55f, 55f, 0.55f);
            SynthOps.AddTone(buf, 0, buf.Length, 55.12f, 55.12f, 0.35f);
            SynthOps.AddTone(buf, 0, buf.Length, 82.5f, 82.5f, 0.22f);
            SynthOps.AddTone(buf, 0, buf.Length, 165f, 165f, 0.07f);

            // Air in a large volume. Narrow, low, and slowly moving.
            float[] air = SynthOps.Buffer(BedSeconds + BedCrossfade);
            SynthOps.AddPinkNoise(air, 0, air.Length, 1f, ref rng);
            SynthOps.SweepResonantLowPass(air, 600f, 200f, 3.0f);
            SynthOps.HighPass(air, 120f);
            SynthOps.ApplyTremolo(air, 1f / BedSeconds, 0.5f, 0.1f);
            SynthOps.NormalizePeak(air, 0.30f);
            SynthOps.Mix(buf, 0, air, 1f);

            SynthOps.Reverb(buf, 0.95f, 0.14f, 0.45f);
            SynthOps.LowPass(buf, 2200f);

            return FinishBed(buf, 0.22f);
        }

        /// <summary>
        /// The tension beds, low (<paramref name="high"/> false) and high (true).
        ///
        /// This is the request's "round başlangıcında gerilim yükselsin", and it is built
        /// as VERTICAL LAYERING rather than as one bed with a volume knob:
        ///
        ///   LOW   a slow 55 Hz drone with a 0.5 Hz swell and a bowed 110 Hz partial. It
        ///         has no rhythm. Under a firefight it is felt, not heard.
        ///   HIGH  the same drone plus (a) a 2 Hz pulse, which is the first thing in the
        ///         mix with a tempo, and (b) a minor second at 220/233 Hz that beats
        ///         against itself 13 times a second.
        ///
        /// Because HIGH contains everything LOW has, the director can cross-fade between
        /// them without a phase or level jump, and the transition reads as the same music
        /// intensifying rather than as a second piece of music starting. Dropping both to
        /// zero on the upgrade screen leaves the wind, leaves, water and palace beds
        /// playing on their own, which is precisely the "sakinleşsin" the request wants -
        /// the calm is the ABSENCE of these two, not a separate calm bed.
        /// </summary>
        internal static float[] BuildTensionBed(bool high, ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(BedSeconds + BedCrossfade);

            // The drone. Saw through a low pass, which has far more body than a sine and
            // still leaves the 200 Hz-4 kHz combat window empty.
            SynthOps.AddTone(buf, 0, buf.Length, 55f, 55f, 0.55f, SynthWave.Saw);
            SynthOps.AddTone(buf, 0, buf.Length, 55f * 1.004f, 55f * 1.004f, 0.40f, SynthWave.Saw);
            SynthOps.AddTone(buf, 0, buf.Length, 110f, 110f, 0.20f, SynthWave.Triangle);
            SynthOps.LowPass(buf, high ? 900f : 420f);

            // Bowed upper partial: the string-like element that makes it read as music
            // rather than as machinery.
            float[] bowed = SynthOps.Buffer(BedSeconds + BedCrossfade);
            SynthOps.AddTone(bowed, 0, bowed.Length, 220f, 220f, 0.5f, SynthWave.Saw);
            if (high)
            {
                // Minor second. 220 against 233 beats 13 times a second - fast enough to
                // be heard as roughness rather than as two notes, which is the whole point.
                SynthOps.AddTone(bowed, 0, bowed.Length, 233.08f, 233.08f, 0.45f, SynthWave.Saw);
                SynthOps.AddTone(bowed, 0, bowed.Length, 330f, 330f, 0.18f);
            }

            SynthOps.SweepResonantLowPass(bowed, high ? 1500f : 700f, high ? 700f : 400f, 2.6f);
            SynthOps.ApplyTremolo(bowed, 1f / BedSeconds, 0.6f, 0.25f);
            SynthOps.NormalizePeak(bowed, high ? 0.34f : 0.18f);
            SynthOps.Mix(buf, 0, bowed, 1f);

            if (high)
            {
                // The pulse. 2 Hz is twelve beats over the loop, so it never lands on the
                // seam, and it is the only tempo anywhere in the ambience.
                SynthOps.ApplyTremolo(buf, 2f, 0.45f, 0.25f);

                float[] hits = SynthOps.Buffer(BedSeconds + BedCrossfade);
                for (int i = 0; i < 12; i++)
                {
                    float[] hit = SynthOps.Buffer(0.16f);
                    SynthOps.AddNoise(hit, 0, hit.Length, 1f, ref rng);
                    SynthOps.AddTone(hit, 0, hit.Length, 82f, 48f, 0.8f);
                    SynthOps.LowPass(hit, 260f);
                    SynthOps.ApplyPercussive(hit, 0, hit.Length, 0.004f, 0.11f, 2.4f);
                    SynthOps.NormalizePeak(hit, 0.30f);
                    SynthOps.Mix(hits, SynthOps.Samples(i * 0.5f), hit, 1f);
                }

                SynthOps.Mix(buf, 0, hits, 1f);
            }
            else
            {
                SynthOps.ApplyTremolo(buf, 0.5f, 0.25f, 0.1f);
            }

            SynthOps.Reverb(buf, 0.85f, 0.35f, 0.22f);
            return FinishBed(buf, high ? 0.58f : 0.38f);
        }

        // ================================================================== one-shots

        /// <summary>
        /// A beam settling somewhere overhead. The scatterer fires one of these every ten
        /// to twenty-five seconds.
        ///
        /// A creak is a stick-slip event, so it is built as one: a low tone whose pitch
        /// walks upward in irregular steps while a resonant filter squeezes it. The
        /// irregularity is the whole sound; a smooth glide reads as a synthesiser sweep.
        /// </summary>
        internal static float[] BuildWoodCreak(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(1.10f);

            float f = rng.Range(120f, 210f);
            float t = 0f;

            // Six to nine slip events, each a little higher and shorter than the last.
            int steps = rng.RangeInt(6, 10);
            for (int i = 0; i < steps; i++)
            {
                float len = rng.Range(0.05f, 0.14f);
                float[] slip = SynthOps.Buffer(len);

                SynthOps.AddTone(slip, 0, slip.Length, f, f * rng.Range(1.0f, 1.12f), 0.8f, SynthWave.Saw, false);
                SynthOps.AddNoise(slip, 0, slip.Length, 0.25f, ref rng);
                SynthOps.SweepResonantLowPass(slip, f * 6f, f * 3f, 6.0f);
                SynthOps.ApplyAdsr(slip, 0, slip.Length, 0.01f, 0.02f, 0.7f, len * 0.4f);
                SynthOps.NormalizePeak(slip, rng.Range(0.35f, 0.9f) * (1f - i / (float)steps * 0.4f));

                SynthOps.Mix(buf, SynthOps.Samples(t), slip, 1f);

                t += len * rng.Range(0.75f, 1.3f);
                f *= rng.Range(1.02f, 1.16f);
            }

            AddTailLayer(buf, 0.82f, 0.34f, 0.28f);
            return Finish(buf, 0.42f, 0.10f);
        }

        /// <summary>
        /// Something metal ringing in the distance - a hanging lantern, a fitting, a temple
        /// bell across the courtyard.
        ///
        /// This is the ambience family's tonal member and it is tuned to the palace scale
        /// (E5, A5 or C6, chosen per variation), so a random ambient ring is never
        /// dissonant against a UI sound that happens to land at the same moment. It also
        /// doubles as the "pool exhausted" cue in the mix plan, which is why it is the one
        /// ambient one-shot with real note content.
        /// </summary>
        internal static float[] BuildMetalRing(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(2.20f);

            int pick = rng.RangeInt(0, 3);
            float f = pick == 0 ? NoteE5 : pick == 1 ? NoteA5 : NoteC6;
            f *= Detune(ref rng, 12f);

            AddBellVoice(buf, 0, 1.90f, f * 0.5f, 1.15f, 0.72f, 0.8f);

            // The strike, tiny and dull. It is far away.
            float[] tap = SynthOps.Buffer(0.02f);
            SynthOps.AddNoise(tap, 0, tap.Length, 1f, ref rng);
            SynthOps.BandPass(tap, 1500f, 6000f);
            SynthOps.ApplyPercussive(tap, 0, tap.Length, 0.0004f, 0.012f, 3f);
            SynthOps.NormalizePeak(tap, 0.16f);
            SynthOps.Mix(buf, 0, tap, 1f);

            SynthOps.LowPass(buf, 5200f);
            AddTailLayer(buf, 0.90f, 0.22f, 0.36f);
            return Finish(buf, 0.46f, 0.25f);
        }

        /// <summary>
        /// Bamboo. Two hollow knocks a beat apart - the shishi-odoshi of every courtyard
        /// garden, and the most recognisably Japanese sound in the set.
        ///
        /// Hollow means the partials are close to 1 : 3 : 5 with almost nothing at 2 : 4,
        /// which is the odd-harmonic series of a closed tube. That is what stops it from
        /// sounding like the wood impact, which uses a full mode stack.
        /// </summary>
        internal static float[] BuildBambooKnock(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(1.05f);

            float f = rng.Range(300f, 430f);
            float[] ratios = { 1f, 3.02f, 5.11f, 7.3f };
            float[] gains = { 0.8f, 0.30f, 0.14f, 0.06f };

            for (int i = 0; i < 2; i++)
            {
                float at = i == 0 ? 0f : rng.Range(0.24f, 0.42f);
                float[] knock = SynthOps.Buffer(0.34f);

                SynthOps.AddPartials(knock, 0, knock.Length, f * (i == 0 ? 1f : 0.94f), ratios, gains, 0.13f, 0.5f);
                SynthOps.AddNoise(knock, 0, SynthOps.Samples(0.004f), 0.5f, ref rng);
                SynthOps.BandPass(knock, 180f, 4200f);
                SynthOps.NormalizePeak(knock, i == 0 ? 0.85f : 0.55f);

                SynthOps.Mix(buf, SynthOps.Samples(at), knock, 1f);
            }

            AddTailLayer(buf, 0.80f, 0.36f, 0.26f);
            return Finish(buf, 0.44f, 0.08f);
        }

        /// <summary>
        /// Stone settling - grit falling off a wall, a flag shifting underfoot somewhere
        /// nobody is standing.
        ///
        /// Pure grain scatter with no tone at all. It is the only ambient one-shot with no
        /// pitch and no envelope shape of its own, which makes it the one that reads as
        /// "the building is old" rather than as "something happened".
        /// </summary>
        internal static float[] BuildStoneSettle(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.90f);

            SynthOps.AddGrains(buf, 0, SynthOps.Samples(0.70f),
                18, 900f, 4200f, 0.004f, 0.018f, 0.35f, ref rng);
            SynthOps.AddGrains(buf, SynthOps.Samples(0.05f), SynthOps.Samples(0.60f),
                6, 180f, 520f, 0.02f, 0.05f, 0.22f, ref rng);

            SynthOps.BandPass(buf, 160f, 6000f);
            AddTailLayer(buf, 0.84f, 0.30f, 0.30f);

            return Finish(buf, 0.38f, 0.12f);
        }
    }
}
