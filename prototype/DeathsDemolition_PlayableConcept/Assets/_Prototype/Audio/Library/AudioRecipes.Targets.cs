namespace DD.Prototype.Audio
{
    public static partial class AudioRecipes
    {
        // ================================================================== flesh

        /// <summary>
        /// Flesh, and its critical variant.
        ///
        /// The flesh hit is the single most repeated sound in the game - an assault rifle
        /// into a crowd fires it eight times a second - so it is built to be SHORT (110 ms)
        /// and to own a band nothing else wants: 120-900 Hz, wet and damped. There is no
        /// bright transient at all. That is not a shortcut; a click on flesh is what makes
        /// a shooter sound like a stapler, and forty clicks a second is the fastest route to
        /// the long-session fatigue the acceptance criteria fail on.
        ///
        /// CRITICAL is not "the same sound, louder". It adds a 2.6 kHz snap in front and a
        /// second, higher body pass - a bone note over the meat note. The player hears a
        /// different EVENT, not a different volume, which is the whole point of separating
        /// it from a normal hit in a crowd.
        /// </summary>
        internal static float[] BuildFleshHit(bool critical, ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(critical ? 0.19f : 0.11f);

            // Wet slap. Band-limited noise with a soft-ish attack; the low pass at 900 Hz
            // is what makes it read as tissue instead of as gravel.
            float[] slap = SynthOps.Buffer(0.06f);
            SynthOps.AddNoise(slap, 0, slap.Length, 1f, ref rng);
            SynthOps.AddTone(slap, 0, slap.Length, 320f * Detune(ref rng, 140f), 120f, 0.7f);
            SynthOps.SweepResonantLowPass(slap, 1500f, 260f, 1.3f);
            SynthOps.ApplyPercussive(slap, 0, slap.Length, 0.0016f, 0.042f, 2.4f);
            SynthOps.NormalizePeak(slap, 0.9f);
            SynthOps.Mix(buf, 0, slap, 1f);

            if (critical)
            {
                // Bone. A short, hard, high crack sitting on top - the only bright thing
                // allowed anywhere in the flesh family, which is exactly why it reads.
                float[] bone = SynthOps.Buffer(0.045f);
                SynthOps.AddNoise(bone, 0, bone.Length, 1f, ref rng);
                float f = 2600f * Detune(ref rng, 90f);
                SynthOps.AddTone(bone, 0, bone.Length, f, f * 0.6f, 0.8f, SynthWave.Triangle);
                SynthOps.HighPass(bone, 1400f);
                SynthOps.ApplyPercussive(bone, 0, bone.Length, 0.0005f, 0.022f, 3.2f);
                SynthOps.NormalizePeak(bone, 0.75f);
                SynthOps.Mix(buf, 0, bone, 1f);

                // Second body pass, a fifth above the meat note. Two pitched layers is what
                // the ear reads as "that one went through something".
                AddBodyLayer(buf, SynthOps.Samples(0.004f), 0.085f, 480f, 165f, 2400f, 380f,
                    2.0f, 0.55f, 2.4f, 0.62f, ref rng);

                AddTailLayer(buf, 0.58f, 0.42f, 0.14f);
                return Finish(buf, 0.86f);
            }

            // Normal hits get almost no room. Eight tails a second is mud, and the flesh
            // hit is the one sound that must never be allowed to smear.
            AddTailLayer(buf, 0.4f, 0.7f, 0.06f);
            return Finish(buf, 0.70f);
        }

        // ================================================================== armour

        /// <summary>
        /// REQ-129 rungs 1 and 2, and the reason those two rungs are ONE function.
        ///
        /// The request insists ArmorHit and ArmorBreak must never be confused. The way to
        /// guarantee that is not to make them "different sounds" - it is to make the hit and
        /// the crack obviously members of one family and the break obviously not:
        ///
        ///   ArmorImpact / ArmorCrack   struck plate. Inharmonic partials on 430 Hz that
        ///                              RING for 140-220 ms. Pitch does not move. The plate
        ///                              is still there, and it says so by still ringing.
        ///   ArmorBreak (below)         shear. The partials are killed in 60 ms, the pitch
        ///                              collapses downward, and debris follows. The plate
        ///                              stops ringing - that silence is the message.
        ///
        /// So the two are separated by DECAY DIRECTION, not by timbre or level. That
        /// survives a crowd, survives distance and survives being half-masked by a shotgun,
        /// none of which a level difference does.
        ///
        /// <paramref name="cracking"/> is rung 2: the same plate with a scatter of stress
        /// ticks over the ring. It is a warning, so it is the same sound plus something,
        /// never a different sound.
        /// </summary>
        internal static float[] BuildArmorImpact(bool cracking, ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(cracking ? 0.30f : 0.24f);

            // Hard, high, very short strike. This is the "steel was struck" cue.
            AddMechanicLayer(buf, 0.006f, 2800f, 4600f * Detune(ref rng, 70f), 0.80f, ref rng);

            // The plate. Inharmonic on purpose: integer partials would fuse into a musical
            // note and the ear would hear a bell, not armour.
            float fundamental = 430f * Detune(ref rng, 110f);
            float[] ratios = { 1f, 2.13f, 3.41f, 4.77f, 6.05f };
            float[] gains = { 0.75f, 0.52f, 0.34f, 0.20f, 0.11f };

            float[] plate = SynthOps.Buffer(cracking ? 0.22f : 0.18f);
            SynthOps.AddPartials(plate, 0, plate.Length, fundamental, ratios, gains,
                cracking ? 0.15f : 0.12f, 0.66f);
            SynthOps.BandPass(plate, 260f, 6500f);
            SynthOps.NormalizePeak(plate, 0.88f);
            SynthOps.Mix(buf, SynthOps.Samples(0.001f), plate, 1f);

            if (cracking)
            {
                // Stress ticks: the plate is failing but has not gone yet. Sparse and dry,
                // so this stays a warning rather than becoming the break.
                SynthOps.AddGrains(buf, SynthOps.Samples(0.02f), SynthOps.Samples(0.20f),
                    9, 1800f, 5200f, 0.003f, 0.010f, 0.34f, ref rng);

                // A slight downward drift on a low partial: the first hint of collapse.
                float[] sag = SynthOps.Buffer(0.16f);
                SynthOps.AddTone(sag, 0, sag.Length, 268f, 214f, 0.4f);
                SynthOps.ApplyPercussive(sag, 0, sag.Length, 0.004f, 0.13f, 1.9f);
                SynthOps.Mix(buf, SynthOps.Samples(0.02f), sag, 0.55f);
            }

            AddTailLayer(buf, 0.66f, 0.30f, 0.17f);
            return Finish(buf, cracking ? 0.84f : 0.78f, 0.015f);
        }

        /// <summary>
        /// REQ-129 rungs 3 and 4 in one clip: the plate shears off, then the pieces land.
        ///
        /// Three beats, and the ORDER is the information:
        ///   0 ms    shear   - a hard, wide-band tear. Louder than any armour hit.
        ///   0-60 ms sag     - the plate's own note falls 520 -> 150 Hz and is gone. Every
        ///                     other armour sound holds its pitch; this one drops. That is
        ///                     the single feature that makes a break impossible to mistake
        ///                     for a hit, even when four zombies are being shot at once.
        ///   60-700  debris  - 22 scattered metal grains thinning out. Nothing else in the
        ///                     game has a tail this long and this granular.
        ///
        /// It is also the longest target sound at 800 ms, which is affordable because a run
        /// only produces a handful of them per round and the mix plan gives it a high enough
        /// priority to be worth the voice.
        /// </summary>
        internal static float[] BuildArmorBreak(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.80f);

            // Beat 1: the shear.
            AddMechanicLayer(buf, 0.018f, 1600f, 3200f * Detune(ref rng, 60f), 0.95f, ref rng);
            AddBodyLayer(buf, 0, 0.09f, 620f, 190f, 6800f, 700f, 2.4f, 0.82f, 3.2f, 0.95f, ref rng);

            // Beat 2: the plate's note collapsing. This is the whole identity of the sound.
            float[] sag = SynthOps.Buffer(0.16f);
            float[] ratios = { 1f, 2.13f, 3.41f };
            float[] gains = { 0.7f, 0.45f, 0.25f };
            SynthOps.AddPartials(sag, 0, sag.Length, 520f * Detune(ref rng, 80f), ratios, gains, 0.055f, 0.6f);
            SynthOps.AddTone(sag, 0, sag.Length, 520f, 150f, 0.55f, SynthWave.Triangle);
            SynthOps.ApplyPercussive(sag, 0, sag.Length, 0.002f, 0.09f, 2.2f);
            SynthOps.NormalizePeak(sag, 0.8f);
            SynthOps.Mix(buf, SynthOps.Samples(0.004f), sag, 1f);

            // Beat 3: debris. Two passes - metal shards up high, plate mass down low - so
            // the fall reads as pieces of different sizes rather than as a noise tail.
            SynthOps.AddGrains(buf, SynthOps.Samples(0.06f), SynthOps.Samples(0.62f),
                22, 1400f, 5600f, 0.006f, 0.026f, 0.40f, ref rng);
            SynthOps.AddGrains(buf, SynthOps.Samples(0.10f), SynthOps.Samples(0.55f),
                7, 260f, 720f, 0.012f, 0.045f, 0.30f, ref rng);

            AddTailLayer(buf, 0.80f, 0.26f, 0.26f);
            return Finish(buf, 0.96f, 0.05f);
        }

        // ================================================================== death

        /// <summary>
        /// Enemy death, and the elite / ultra version.
        ///
        /// A death is the payoff of every shot that came before it, so it is built as the
        /// only target sound with a VOCAL element: a short falling formant pair over a
        /// body fall. Formants are what make a noise burst read as a throat.
        ///
        /// <paramref name="heavy"/> is the elite / ultra tier and is a full octave lower,
        /// 55% longer, with a sub-bass drop and armour debris on the fall. The request asks
        /// for elite/ultra death to be separable from a standard one; an octave and a
        /// sub-bass are separable across a room, through a firefight, on laptop speakers.
        /// </summary>
        internal static float[] BuildEnemyDeath(bool heavy, ref SynthRandom rng)
        {
            float total = heavy ? 1.05f : 0.62f;
            float[] buf = SynthOps.Buffer(total);

            float pitch = heavy ? 0.5f : 1f;

            // Voice: two formants falling together. Sawtooth through a resonant sweep is
            // the cheapest thing that reads as a throat rather than as a synthesiser.
            float[] voice = SynthOps.Buffer(heavy ? 0.42f : 0.26f);
            SynthOps.AddTone(voice, 0, voice.Length, 210f * pitch * Detune(ref rng, 160f), 88f * pitch, 0.6f, SynthWave.Saw);
            SynthOps.AddTone(voice, 0, voice.Length, 318f * pitch, 132f * pitch, 0.3f, SynthWave.Saw);
            SynthOps.AddNoise(voice, 0, voice.Length, 0.28f, ref rng);
            SynthOps.SweepResonantLowPass(voice, 2600f * pitch, 420f * pitch, 3.2f);
            SynthOps.ApplyPercussive(voice, 0, voice.Length, 0.012f, heavy ? 0.34f : 0.20f, 1.7f);
            SynthOps.NormalizePeak(voice, 0.85f);
            SynthOps.Mix(buf, 0, voice, 1f);

            if (heavy)
            {
                // Sub drop. Felt more than heard, and nothing else in the target family
                // goes anywhere near 30 Hz, so it cannot be confused with a standard kill.
                float[] sub = SynthOps.Buffer(0.5f);
                SynthOps.AddTone(sub, 0, sub.Length, 78f, 29f, 0.9f);
                SynthOps.LowPass(sub, 160f);
                SynthOps.ApplyPercussive(sub, 0, sub.Length, 0.006f, 0.40f, 1.6f);
                SynthOps.Mix(buf, SynthOps.Samples(0.01f), sub, 0.75f);
            }

            // The body landing, a beat after the voice stops.
            int fallAt = SynthOps.Samples(heavy ? 0.34f : 0.20f);
            float[] fall = SynthOps.Buffer(heavy ? 0.30f : 0.18f);
            SynthOps.AddNoise(fall, 0, fall.Length, 1f, ref rng);
            SynthOps.AddTone(fall, 0, fall.Length, 150f * pitch, 55f * pitch, 0.8f);
            SynthOps.SweepResonantLowPass(fall, 900f, 180f, 1.6f);
            SynthOps.ApplyPercussive(fall, 0, fall.Length, 0.003f, heavy ? 0.22f : 0.13f, 2.2f);
            SynthOps.NormalizePeak(fall, heavy ? 0.8f : 0.6f);
            SynthOps.Mix(buf, fallAt, fall, 1f);

            if (heavy)
            {
                // Armour coming apart as it lands. Deliberately sparser and lower than
                // ArmorBreak's debris so the two do not trade places in the mix.
                SynthOps.AddGrains(buf, fallAt, SynthOps.Samples(0.5f),
                    10, 700f, 3000f, 0.008f, 0.03f, 0.26f, ref rng);
            }

            AddTailLayer(buf, heavy ? 0.84f : 0.66f, heavy ? 0.28f : 0.40f, heavy ? 0.28f : 0.18f);
            return Finish(buf, heavy ? 0.95f : 0.82f, 0.04f);
        }

        // ================================================================== upgrade branches

        /// <summary>
        /// Ricochet. A whistle, and only a whistle.
        ///
        /// The impact the bullet made was already played by the surface recipe, so putting
        /// another transient here would double the attack and make one bullet sound like
        /// two. What is missing from the surface sound is the round LEAVING, so that is all
        /// this contributes: a fast rising-then-falling whistle with a Doppler-shaped sweep,
        /// starting 8 ms late so it sits behind the impact instead of on it.
        /// </summary>
        internal static float[] BuildRicochet(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.34f);

            float top = rng.Range(2100f, 3400f);
            float[] whistle = SynthOps.Buffer(0.26f);

            // Up then down: a single monotonic sweep reads as a synthesiser, the turn is
            // what makes it read as something passing.
            int half = whistle.Length / 3;
            SynthOps.AddTone(whistle, 0, half, top * 0.55f, top, 0.7f, SynthWave.Sine, false);
            SynthOps.AddTone(whistle, half, whistle.Length - half, top, top * 0.28f, 0.7f);
            SynthOps.AddNoise(whistle, 0, whistle.Length, 0.10f, ref rng);
            SynthOps.BandPass(whistle, 900f, 9000f);
            SynthOps.ApplyAdsr(whistle, 0, whistle.Length, 0.008f, 0.05f, 0.55f, 0.18f);
            SynthOps.NormalizePeak(whistle, 0.62f);

            SynthOps.Mix(buf, SynthOps.Samples(0.008f), whistle, 1f);

            AddTailLayer(buf, 0.74f, 0.34f, 0.22f);
            return Finish(buf, 0.66f, 0.03f);
        }

        /// <summary>
        /// Chain lightning arc. The only sound in the game with no acoustic model behind it,
        /// because the mechanic has none either.
        ///
        /// Built from ring modulation, which produces sum-and-difference partials that no
        /// physical object makes. That inhumanity is the point: the player must never
        /// mistake a chain arc for a hit, and "sounds like nothing that exists" is an
        /// absolutely reliable way to guarantee that.
        /// </summary>
        internal static float[] BuildChainArc(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.30f);

            SynthOps.AddNoise(buf, 0, buf.Length, 0.8f, ref rng);
            SynthOps.AddTone(buf, 0, buf.Length, 1750f * Detune(ref rng, 200f), 640f, 0.7f, SynthWave.Square);
            SynthOps.BandPass(buf, 1100f, 9500f);

            // Two ring mods at non-harmonic rates: metallic, electrical, and unmistakably
            // not a struck object.
            SynthOps.RingMod(buf, 137f, 0.65f);
            SynthOps.RingMod(buf, 311f, 0.35f);

            SynthOps.ApplyPercussive(buf, 0, buf.Length, 0.001f, 0.11f, 2.8f);

            // The arc jumping to the next target: a few crackles trailing the strike.
            SynthOps.AddGrains(buf, SynthOps.Samples(0.02f), SynthOps.Samples(0.22f),
                8, 2200f, 8000f, 0.002f, 0.008f, 0.3f, ref rng);

            AddTailLayer(buf, 0.6f, 0.5f, 0.16f);
            return Finish(buf, 0.72f, 0.02f);
        }

        /// <summary>
        /// Explosion. The largest sound in the library and the only one allowed to be.
        ///
        /// Four layers over 1.6 seconds:
        ///   crack    2 ms of wide-band snap. Without it the blast has no location.
        ///   body     a 190 -> 26 Hz drop through a resonant sweep, driven hard. This is
        ///            the pressure wave and it is what makes the sound feel big at a
        ///            playback level that does not clip.
        ///   debris   35 grains over 900 ms, low and high mixed.
        ///   room     the longest reverb in the game, 0.92 room / 0.18 damping. The palace
        ///            courtyard answering back is most of the perceived size.
        ///
        /// Nothing else is permitted to occupy 26-90 Hz, so an explosion is never masked
        /// however busy the frame is.
        /// </summary>
        internal static float[] BuildExplosion(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(1.60f);

            AddMechanicLayer(buf, 0.014f, 1200f, 2600f, 0.85f, ref rng);

            AddBodyLayer(buf, 0, 0.36f, 190f * Detune(ref rng, 50f), 26f,
                5200f, 90f, 2.8f, 0.72f, 4.2f, 1.00f, ref rng);

            // Second, slower pressure wave. One sweep is a "boom"; two overlapping sweeps
            // at different rates are an explosion.
            AddBodyLayer(buf, SynthOps.Samples(0.03f), 0.72f, 96f, 31f,
                1400f, 60f, 3.4f, 0.5f, 3.0f, 0.78f, ref rng);

            SynthOps.AddGrains(buf, SynthOps.Samples(0.05f), SynthOps.Samples(0.90f),
                26, 900f, 6000f, 0.006f, 0.03f, 0.34f, ref rng);
            SynthOps.AddGrains(buf, SynthOps.Samples(0.09f), SynthOps.Samples(0.85f),
                9, 180f, 620f, 0.02f, 0.06f, 0.28f, ref rng);

            SynthOps.Delay(buf, 0.085f, 0.5f, 0.28f);
            AddTailLayer(buf, 0.92f, 0.18f, 0.40f);

            return Finish(buf, 1.00f, 0.12f);
        }

        // ================================================================== confirmation

        /// <summary>
        /// Hit marker. 45 ms, one band, no room.
        ///
        /// This is a UI sound wearing combat clothes: it confirms that the game registered
        /// the shot, so it must arrive with the frame and never be smeared by a tail. It
        /// lives at 1.9 kHz - deliberately between the flesh band (under 900 Hz) and the
        /// armour band (2.8 kHz and up) - so it stays audible when both are playing and
        /// steals nothing from either.
        /// </summary>
        internal static float[] BuildHitMarker(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.045f);

            float f = 1900f * Detune(ref rng, 25f);
            SynthOps.AddTone(buf, 0, buf.Length, f, f * 0.92f, 0.8f, SynthWave.Triangle);
            SynthOps.AddNoise(buf, 0, buf.Length, 0.18f, ref rng);
            SynthOps.HighPass(buf, 1100f);
            SynthOps.ApplyPercussive(buf, 0, buf.Length, 0.0006f, 0.026f, 3.0f);

            return Finish(buf, 0.42f, 0.006f);
        }

        /// <summary>
        /// Kill marker. The hit marker plus a perfect fifth below it, 30 ms later.
        ///
        /// A rising interval is the most reliable "that worked" signal there is, and
        /// building it out of the hit marker's own tone means the player hears a kill as a
        /// COMPLETION of the hit rather than as an unrelated new sound. Two notes, 110 ms
        /// total, still short enough to fire ten times in a good burst.
        /// </summary>
        internal static float[] BuildKillMarker(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.13f);

            float f = 1900f * Detune(ref rng, 20f);

            float[] first = SynthOps.Buffer(0.04f);
            SynthOps.AddTone(first, 0, first.Length, f, f, 0.8f, SynthWave.Triangle);
            SynthOps.ApplyPercussive(first, 0, first.Length, 0.0006f, 0.024f, 3f);
            SynthOps.Mix(buf, 0, first, 1f);

            // 1.5 : 1 is a fifth. Up, not down: down reads as a failure everywhere.
            float[] second = SynthOps.Buffer(0.085f);
            SynthOps.AddTone(second, 0, second.Length, f * 1.5f, f * 1.5f, 0.85f, SynthWave.Triangle);
            SynthOps.AddTone(second, 0, second.Length, f * 3f, f * 3f, 0.2f);
            SynthOps.ApplyPercussive(second, 0, second.Length, 0.001f, 0.055f, 2.6f);
            SynthOps.Mix(buf, SynthOps.Samples(0.030f), second, 1f);

            SynthOps.HighPass(buf, 900f);
            return Finish(buf, 0.52f, 0.012f);
        }
    }
}
