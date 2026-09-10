namespace DD.Prototype.Audio
{
    public static partial class AudioRecipes
    {
        /// <summary>
        /// Surface impacts, one per SurfaceKind.
        ///
        /// These eight are the most crowded corner of the mix: a shotgun into a stone wall
        /// produces up to eight of them, and the request still wants the material to be
        /// legible. Two rules keep them apart:
        ///
        ///   1. EACH MATERIAL OWNS A BAND. Dirt lives under 700 Hz, wood in its three
        ///      resonant modes at 190/380/620, foliage and paper above 1.2 kHz, metal in
        ///      inharmonic partials at 1.2-4 kHz, stone in the 1.6-5 kHz crack. Two
        ///      materials never compete for the same band, so overlapping impacts sum
        ///      instead of masking.
        ///   2. EACH MATERIAL OWNS A DECAY SHAPE. Dirt has no tail at all, foliage has a
        ///      soft attack and no transient, metal rings for a quarter of a second. The
        ///      envelope is recognisable even when the band is buried.
        ///
        /// All of them are short. The longest is metal at 300 ms; the shortest is paper at
        /// 75 ms. Nothing here is allowed to sustain, because sustain is what turns forty
        /// impacts a second into mud.
        /// </summary>
        internal static float[] BuildImpactDefault(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.11f);

            AddMechanicLayer(buf, 0.006f, 1400f, 2200f * Detune(ref rng, 80f), 0.55f, ref rng);
            AddBodyLayer(buf, 0, 0.055f, 380f, 160f, 3000f, 500f, 1.4f, 0.85f, 1.8f, 0.7f, ref rng);
            AddTailLayer(buf, 0.5f, 0.5f, 0.10f);

            return Finish(buf, 0.72f);
        }

        /// <summary>Stone: bright crack plus a scatter of grit. Band 1.6-5 kHz.</summary>
        internal static float[] BuildImpactStone(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.20f);

            AddMechanicLayer(buf, 0.007f, 2200f, 3600f * Detune(ref rng, 90f), 0.85f, ref rng);
            AddBodyLayer(buf, 0, 0.045f, 520f, 240f, 5200f, 1200f, 1.8f, 0.9f, 2.2f, 0.6f, ref rng);

            // Grit. Not decoration - it is the only part of a stone impact that survives
            // being heard from twenty metres away.
            SynthOps.AddGrains(buf, SynthOps.Samples(0.012f), SynthOps.Samples(0.12f),
                7, 2400f, 6200f, 0.004f, 0.014f, 0.28f, ref rng);

            AddTailLayer(buf, 0.72f, 0.30f, 0.18f);
            return Finish(buf, 0.78f);
        }

        /// <summary>Wood: three resonant modes. The modes ARE the material.</summary>
        internal static float[] BuildImpactWood(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.22f);

            AddMechanicLayer(buf, 0.005f, 900f, 1600f, 0.45f, ref rng);

            float fundamental = 190f * Detune(ref rng, 120f);
            float[] ratios = { 1f, 2.0f, 3.26f, 4.9f };
            float[] gains = { 0.7f, 0.42f, 0.22f, 0.10f };
            float[] modes = SynthOps.Buffer(0.16f);
            SynthOps.AddPartials(modes, 0, modes.Length, fundamental, ratios, gains, 0.11f, 0.55f);
            SynthOps.LowPass(modes, 2400f);
            SynthOps.NormalizePeak(modes, 0.85f);
            SynthOps.Mix(buf, 0, modes, 1f);

            AddBodyLayer(buf, 0, 0.03f, 300f, 190f, 2000f, 600f, 1.2f, 0.8f, 1.4f, 0.35f, ref rng);
            AddTailLayer(buf, 0.55f, 0.55f, 0.12f);

            return Finish(buf, 0.76f);
        }

        /// <summary>Foliage: no transient at all. Soft attack is the identifier.</summary>
        internal static float[] BuildImpactFoliage(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.17f);

            SynthOps.AddNoise(buf, 0, buf.Length, 1f, ref rng);
            SynthOps.BandPass(buf, 1800f, 7500f);

            // 6 ms attack instead of the usual 0.5 ms. Leaves do not click.
            SynthOps.ApplyPercussive(buf, 0, buf.Length, 0.006f, 0.10f, 2.4f);

            // A handful of individual leaf ticks riding on the wash.
            SynthOps.AddGrains(buf, 0, SynthOps.Samples(0.11f),
                5, 3000f, 8000f, 0.003f, 0.009f, 0.22f, ref rng);

            AddTailLayer(buf, 0.45f, 0.7f, 0.08f);
            return Finish(buf, 0.55f);
        }

        /// <summary>Water: a plop with a falling bubble pitch, then a splash wash.</summary>
        internal static float[] BuildImpactWater(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.30f);

            // The falling pitch is the physical signature of a cavity collapsing. Nothing
            // else in the game falls this fast, so water is unmistakable.
            float[] plop = SynthOps.Buffer(0.09f);
            SynthOps.AddTone(plop, 0, plop.Length, 760f * Detune(ref rng, 100f), 190f, 0.9f);
            SynthOps.LowPass(plop, 1400f);
            SynthOps.ApplyPercussive(plop, 0, plop.Length, 0.002f, 0.06f, 2.2f);
            SynthOps.Mix(buf, 0, plop, 1f);

            float[] splash = SynthOps.Buffer(0.22f);
            SynthOps.AddNoise(splash, 0, splash.Length, 1f, ref rng);
            SynthOps.BandPass(splash, 700f, 5000f);
            SynthOps.ApplyPercussive(splash, 0, splash.Length, 0.004f, 0.14f, 2.6f);
            SynthOps.NormalizePeak(splash, 0.45f);
            SynthOps.Mix(buf, SynthOps.Samples(0.004f), splash, 1f);

            SynthOps.AddGrains(buf, SynthOps.Samples(0.02f), SynthOps.Samples(0.20f),
                6, 500f, 2200f, 0.006f, 0.02f, 0.18f, ref rng);

            AddTailLayer(buf, 0.5f, 0.6f, 0.12f);
            return Finish(buf, 0.70f);
        }

        /// <summary>Metal: inharmonic partials, the longest decay of the eight.</summary>
        internal static float[] BuildImpactMetal(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.32f);

            AddMechanicLayer(buf, 0.005f, 2600f, 4200f, 0.65f, ref rng);

            float fundamental = 1180f * Detune(ref rng, 150f);
            float[] ratios = { 1f, 1.52f, 2.27f, 3.31f, 4.62f };
            float[] gains = { 0.7f, 0.5f, 0.36f, 0.22f, 0.12f };
            float[] ring = SynthOps.Buffer(0.28f);
            SynthOps.AddPartials(ring, 0, ring.Length, fundamental, ratios, gains, 0.20f, 0.70f);
            SynthOps.RingMod(ring, 73f, 0.28f);
            SynthOps.NormalizePeak(ring, 0.85f);
            SynthOps.Mix(buf, 0, ring, 1f);

            AddTailLayer(buf, 0.68f, 0.28f, 0.16f);
            return Finish(buf, 0.74f, 0.02f);
        }

        /// <summary>Dirt: everything under 700 Hz, no tail whatsoever.</summary>
        internal static float[] BuildImpactDirt(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.10f);

            SynthOps.AddNoise(buf, 0, buf.Length, 1f, ref rng);
            SynthOps.AddTone(buf, 0, buf.Length, 210f, 90f, 0.6f);
            SynthOps.LowPass(buf, 700f);
            SynthOps.ApplyPercussive(buf, 0, buf.Length, 0.002f, 0.055f, 2.8f);

            // No reverb: soft ground absorbs. The absence of a tail is the material cue.
            return Finish(buf, 0.62f);
        }

        /// <summary>Shoji paper: the shortest and thinnest sound in the game.</summary>
        internal static float[] BuildImpactPaper(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.09f);

            SynthOps.AddNoise(buf, 0, buf.Length, 1f, ref rng);
            SynthOps.BandPass(buf, 1200f, 8000f);
            SynthOps.ApplyPercussive(buf, 0, buf.Length, 0.0008f, 0.035f, 3.4f);

            // Tear: a couple of tiny ticks trailing the burst.
            SynthOps.AddGrains(buf, SynthOps.Samples(0.008f), SynthOps.Samples(0.05f),
                4, 2500f, 9000f, 0.002f, 0.006f, 0.3f, ref rng);

            AddTailLayer(buf, 0.4f, 0.7f, 0.06f);
            return Finish(buf, 0.50f);
        }
    }
}
