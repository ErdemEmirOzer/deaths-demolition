namespace DD.Prototype.Audio
{
    public static partial class AudioRecipes
    {
        /// <summary>
        /// The tonal centre of everything non-combat in the game.
        ///
        /// Every UI sound, every run-flow sound and the Special Weapon gong are built from
        /// the SAME five notes - the hirajoshi scale on A, which is the standard Japanese
        /// pentatonic and the audible half of the sakura / palace theme the request asks
        /// for. Using one scale everywhere means the interface, the round flow and the
        /// palace read as one place instead of as a pile of beeps, and it means two of
        /// these sounds can overlap without producing an interval that grates.
        ///
        /// Combat sounds are deliberately NOT in the scale. They are noise-based and
        /// unpitched, which is what keeps the two families from ever being confused:
        /// if it has a note, it is the game talking to you; if it does not, it is the
        /// world.
        /// </summary>
        private const float NoteA4 = 440.00f;
        private const float NoteB4 = 493.88f;
        private const float NoteC5 = 523.25f;
        private const float NoteE5 = 659.25f;
        private const float NoteF5 = 698.46f;
        private const float NoteA5 = 880.00f;
        private const float NoteC6 = 1046.50f;
        private const float NoteE6 = 1318.51f;

        /// <summary>
        /// A struck-metal voice: inharmonic partials, slow-ish attack, long decay.
        /// Bells, gongs and the coin chime all come from here, which is why they sound
        /// like objects in the same palace rather than like three different synthesisers.
        /// </summary>
        private static void AddBellVoice(
            float[] buf, int start, float lengthSeconds, float fundamental,
            float decaySeconds, float spread, float gain)
        {
            int count = SynthOps.Samples(lengthSeconds);
            if (start + count >= buf.Length)
            {
                count = buf.Length - start - 1;
            }

            if (count <= 8)
            {
                return;
            }

            // Ratios from a struck bronze plate: the minor-third partial at 2.4 and the
            // stretched octave at 5.43 are what say "bronze" rather than "sine wave".
            float[] ratios = { 1f, 2.00f, 2.40f, 3.01f, 4.07f, 5.43f };
            float[] gains = { 0.80f, 0.34f, 0.46f, 0.22f, 0.16f, 0.09f };

            float[] bell = new float[count];
            SynthOps.AddPartials(bell, 0, count, fundamental, ratios, gains, decaySeconds, spread);
            SynthOps.NormalizePeak(bell, gain);
            SynthOps.Mix(buf, start, bell, 1f);
        }

        /// <summary>A single clean scale note. The building block of every UI phrase.</summary>
        private static void AddNote(
            float[] buf, int start, float lengthSeconds, float freq,
            float gain, float attack = 0.004f, float decayCurve = 2.2f)
        {
            int count = SynthOps.Samples(lengthSeconds);
            if (start + count >= buf.Length)
            {
                count = buf.Length - start - 1;
            }

            if (count <= 8)
            {
                return;
            }

            float[] note = new float[count];
            SynthOps.AddTone(note, 0, count, freq, freq, 0.75f, SynthWave.Triangle);
            SynthOps.AddTone(note, 0, count, freq * 2f, freq * 2f, 0.14f);
            SynthOps.AddTone(note, 0, count, freq * 3.01f, freq * 3.01f, 0.06f);
            SynthOps.ApplyPercussive(note, 0, count, attack, lengthSeconds * 0.8f, decayCurve);
            SynthOps.NormalizePeak(note, gain);
            SynthOps.Mix(buf, start, note, 1f);
        }

        // ================================================================== special weapon

        /// <summary>
        /// The Special Weapon spawn gong. The single most important non-combat cue in the
        /// game: it is the only thing that makes a player abandon a firing position.
        ///
        /// It is therefore built to be the LOUDEST-READING and LONGEST sound in the
        /// library - 3.4 seconds, a 110 Hz bronze fundamental with a stretched partial
        /// stack, a strike transient and a 2.6 second tail. It is also the only sound in
        /// the game with significant energy at 110 Hz that is not an explosion, so it cuts
        /// through a firefight without needing to be mixed above one.
        ///
        /// It gets no variation on purpose: this is a landmark, and a landmark that sounds
        /// slightly different each time is not a landmark.
        /// </summary>
        internal static float[] BuildSpawnGong(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(3.40f);

            // The mallet. Wide-band, short, and 40% of why a gong sounds struck.
            AddMechanicLayer(buf, 0.022f, 300f, 520f, 0.55f, ref rng);

            AddBellVoice(buf, 0, 3.10f, 110f, 2.20f, 0.80f, 0.95f);

            // Shimmer: high partials arriving late, the way a real gong "blooms" a beat
            // after it is hit.
            float[] shimmer = SynthOps.Buffer(2.20f);
            AddBellVoice(shimmer, 0, 2.10f, 440f, 1.10f, 0.72f, 0.5f);
            SynthOps.ApplyPercussive(shimmer, 0, shimmer.Length, 0.16f, 1.9f, 1.5f);
            SynthOps.Mix(buf, SynthOps.Samples(0.05f), shimmer, 0.6f);

            // Slow beating between two very close partials. This is the wobble that makes
            // a gong sound alive instead of sampled.
            SynthOps.ApplyTremolo(buf, 3.1f, 0.16f);

            AddTailLayer(buf, 0.94f, 0.16f, 0.42f);
            return Finish(buf, 1.00f, 0.35f);
        }

        /// <summary>
        /// The beacon loop that marks a Special Weapon lying on the ground.
        ///
        /// Two seconds, seamless, and deliberately almost featureless: a soft pulsing
        /// fifth with a slow tremolo. It has to survive being audible for up to a full
        /// round without ever becoming the thing the player notices, which means no
        /// transient, no rhythm the ear can lock onto and nothing above 3 kHz.
        ///
        /// It is spatialised at the pickup, so the pulse plus 3D falloff is the actual
        /// navigation instrument - the player finds the weapon by walking into the
        /// loudness, not by hearing a direction cue.
        /// </summary>
        internal static float[] BuildBeaconLoop(ref SynthRandom rng)
        {
            const float loopSeconds = 2.0f;
            float[] buf = SynthOps.Buffer(loopSeconds + 0.25f);

            SynthOps.AddTone(buf, 0, buf.Length, NoteA4, NoteA4, 0.55f, SynthWave.Sine);
            SynthOps.AddTone(buf, 0, buf.Length, NoteE5, NoteE5, 0.30f, SynthWave.Sine);
            SynthOps.AddTone(buf, 0, buf.Length, NoteA4 * 2.005f, NoteA4 * 2.005f, 0.12f);
            SynthOps.AddPinkNoise(buf, 0, buf.Length, 0.06f, ref rng);
            SynthOps.LowPass(buf, 3000f);

            // Exactly one pulse per second, so the loop point lands on a trough.
            SynthOps.ApplyTremolo(buf, 1f, 0.75f, 0.75f);

            float[] seamless = SynthOps.MakeSeamless(buf, 0.25f);
            SynthOps.NormalizePeak(seamless, 0.55f);
            return seamless;
        }

        /// <summary>
        /// Picking the Special Weapon up. A rising sweep resolving onto the gong's own
        /// fundamental two octaves up - so the pickup audibly ANSWERS the spawn gong.
        /// </summary>
        internal static float[] BuildSwPickup(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.90f);

            // The charge: filtered noise sweeping up. Short, so it does not delay the
            // confirmation the player is waiting for.
            float[] sweep = SynthOps.Buffer(0.22f);
            SynthOps.AddNoise(sweep, 0, sweep.Length, 1f, ref rng);
            SynthOps.SweepResonantLowPass(sweep, 400f, 5200f, 4.5f);
            SynthOps.ApplyAdsr(sweep, 0, sweep.Length, 0.06f, 0.10f, 0.7f, 0.05f);
            SynthOps.NormalizePeak(sweep, 0.55f);
            SynthOps.Mix(buf, 0, sweep, 1f);

            AddNote(buf, SynthOps.Samples(0.20f), 0.20f, NoteA5, 0.62f, 0.002f);
            AddNote(buf, SynthOps.Samples(0.26f), 0.55f, NoteE6, 0.70f, 0.003f);
            AddBellVoice(buf, SynthOps.Samples(0.20f), 0.65f, 440f, 0.40f, 0.70f, 0.45f);

            AddTailLayer(buf, 0.78f, 0.32f, 0.28f);
            return Finish(buf, 0.85f, 0.06f);
        }

        /// <summary>
        /// The final-seconds warning, fired once per remaining second by the director.
        ///
        /// A tritone - the one interval in the scale system above that is NOT in the
        /// hirajoshi set. Every other tonal sound in the game is consonant with every
        /// other; this one is not consonant with anything, which is exactly how a player
        /// learns in one run that it means "you are about to lose the weapon".
        ///
        /// Two pulses rather than one, so it cannot be mistaken for a countdown tick.
        /// </summary>
        internal static float[] BuildSwFinalSeconds(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.55f);

            // A5 against D#6: a tritone, deliberately outside the palace scale.
            for (int pulse = 0; pulse < 2; pulse++)
            {
                int at = SynthOps.Samples(pulse * 0.17f);
                AddNote(buf, at, 0.15f, NoteA5, 0.55f, 0.0015f, 2.8f);
                AddNote(buf, at, 0.15f, NoteA5 * 1.4142f, 0.50f, 0.0015f, 2.8f);
            }

            SynthOps.AddNoise(buf, 0, SynthOps.Samples(0.02f), 0.12f, ref rng);
            SynthOps.HighPass(buf, 500f);

            AddTailLayer(buf, 0.6f, 0.45f, 0.16f);
            return Finish(buf, 0.72f, 0.04f);
        }

        /// <summary>
        /// The Special Weapon running out, in the hands (<paramref name="onGround"/> false)
        /// or on the ground (true).
        ///
        /// Both are the beacon's own note collapsing - the sound the game has been making
        /// about this weapon for the last minute, dying. The ground version is a third of
        /// the length and much duller, because the player may be nowhere near it and a
        /// full-strength cue for something that never mattered is a false alarm.
        /// </summary>
        internal static float[] BuildSwExpired(bool onGround, ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(onGround ? 0.55f : 1.20f);

            float len = onGround ? 0.38f : 0.85f;
            float[] fall = SynthOps.Buffer(len);
            SynthOps.AddTone(fall, 0, fall.Length, NoteA4, NoteA4 * 0.42f, 0.7f, SynthWave.Triangle);
            SynthOps.AddTone(fall, 0, fall.Length, NoteE5, NoteE5 * 0.40f, 0.35f);
            SynthOps.AddPinkNoise(fall, 0, fall.Length, 0.10f, ref rng);
            SynthOps.LowPass(fall, onGround ? 1400f : 2600f);
            SynthOps.ApplyAdsr(fall, 0, fall.Length, 0.006f, 0.20f, 0.55f, len * 0.45f);
            SynthOps.NormalizePeak(fall, onGround ? 0.5f : 0.8f);
            SynthOps.Mix(buf, 0, fall, 1f);

            if (!onGround)
            {
                // A last mechanical click: the barrel stops. Only in the hands version,
                // because only there is there a mechanism to stop.
                AddMechanicLayer(buf, 0.012f, 900f, 1400f, 0.4f, ref rng);
            }

            AddTailLayer(buf, onGround ? 0.55f : 0.82f, 0.4f, onGround ? 0.14f : 0.26f);
            return Finish(buf, onGround ? 0.55f : 0.80f, 0.06f);
        }

        // ================================================================== player

        /// <summary>
        /// Taking damage. The one sound that must be heard through absolutely anything,
        /// because the player has no other warning that the run is about to end.
        ///
        /// Three tricks make it survive a crowd without being loud:
        ///   * It owns 55-160 Hz, a band no combat sound uses (the explosion goes lower,
        ///     the flesh hit goes higher).
        ///   * It has a sharp intake of breath at 4-7 kHz, above every weapon body.
        ///   * The mix plan ducks everything else for 140 ms when it plays. A short duck
        ///     is far more audible than 6 dB of extra level and costs no headroom.
        /// </summary>
        internal static float[] BuildPlayerDamage(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.55f);

            // The impact on the body: low, dull, close.
            float[] thud = SynthOps.Buffer(0.22f);
            SynthOps.AddNoise(thud, 0, thud.Length, 0.7f, ref rng);
            SynthOps.AddTone(thud, 0, thud.Length, 132f * Detune(ref rng, 120f), 55f, 1f);
            SynthOps.SweepResonantLowPass(thud, 700f, 90f, 2.4f);
            SynthOps.Saturate(thud, 2.6f);
            SynthOps.ApplyPercussive(thud, 0, thud.Length, 0.002f, 0.16f, 2.0f);
            SynthOps.NormalizePeak(thud, 0.95f);
            SynthOps.Mix(buf, 0, thud, 1f);

            // The intake of breath. High, thin, and the part that reads as "that was ME".
            float[] breath = SynthOps.Buffer(0.26f);
            SynthOps.AddNoise(breath, 0, breath.Length, 1f, ref rng);
            SynthOps.BandPass(breath, 3800f, 7200f);
            SynthOps.ApplyAdsr(breath, 0, breath.Length, 0.05f, 0.08f, 0.4f, 0.12f);
            SynthOps.NormalizePeak(breath, 0.30f);
            SynthOps.Mix(buf, SynthOps.Samples(0.02f), breath, 1f);

            // A single heartbeat under it, so repeated damage builds a pulse.
            float[] beat = SynthOps.Buffer(0.18f);
            SynthOps.AddTone(beat, 0, beat.Length, 62f, 41f, 1f);
            SynthOps.LowPass(beat, 140f);
            SynthOps.ApplyPercussive(beat, 0, beat.Length, 0.006f, 0.12f, 2.4f);
            SynthOps.Mix(buf, SynthOps.Samples(0.06f), beat, 0.45f);

            return Finish(buf, 0.90f, 0.05f);
        }

        /// <summary>
        /// Death. The end of the run, and the only sound in the game longer than three
        /// seconds that is not a loop.
        ///
        /// Structure is a deliberate inversion of every other cue: instead of a transient
        /// followed by decay, it is a slow collapse followed by silence. Two detuned tones
        /// 12 cents apart fall an octave and a half over 2.4 seconds while the low pass
        /// closes; three heartbeats slow down underneath and stop. The mix plan mutes the
        /// entire combat bus for its first second, so what the player mostly hears is the
        /// game stopping.
        /// </summary>
        internal static float[] BuildPlayerDeath(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(3.60f);

            float[] collapse = SynthOps.Buffer(2.60f);
            SynthOps.AddTone(collapse, 0, collapse.Length, 220f, 62f, 0.55f, SynthWave.Triangle, false);
            SynthOps.AddTone(collapse, 0, collapse.Length, 220f * 1.007f, 62f * 1.007f, 0.45f, SynthWave.Triangle, false);
            SynthOps.AddTone(collapse, 0, collapse.Length, 330f, 93f, 0.22f, SynthWave.Sine, false);
            SynthOps.AddPinkNoise(collapse, 0, collapse.Length, 0.16f, ref rng);
            SynthOps.SweepResonantLowPass(collapse, 3200f, 220f, 2.2f);
            SynthOps.ApplyAdsr(collapse, 0, collapse.Length, 0.02f, 0.6f, 0.62f, 1.6f);
            SynthOps.NormalizePeak(collapse, 0.85f);
            SynthOps.Mix(buf, 0, collapse, 1f);

            // Three heartbeats, each slower and quieter than the last, then nothing.
            float at = 0.10f;
            float interval = 0.62f;
            for (int i = 0; i < 3; i++)
            {
                float[] beat = SynthOps.Buffer(0.30f);
                SynthOps.AddTone(beat, 0, beat.Length, 58f, 34f, 1f);
                SynthOps.LowPass(beat, 120f);
                SynthOps.ApplyPercussive(beat, 0, beat.Length, 0.008f, 0.20f, 2.2f);
                SynthOps.NormalizePeak(beat, 0.55f * (1f - i * 0.28f));
                SynthOps.Mix(buf, SynthOps.Samples(at), beat, 1f);

                at += interval;
                interval *= 1.45f;
            }

            AddTailLayer(buf, 0.93f, 0.20f, 0.34f);
            return Finish(buf, 0.88f, 0.5f);
        }

        // ================================================================== economy

        /// <summary>
        /// Gold. A 150 ms bell on C6, and nothing else.
        ///
        /// The rising-streak arpeggio the request wants is NOT baked in - the runtime
        /// transposes this one clip by scale degree per pickup (see AudioMixPlan). One
        /// clip plus a pitch table gives an eight-orb magnet sweep a real rising phrase for
        /// the memory cost of a single 150 ms buffer, and it stays in the palace scale
        /// because the pitch table is the scale.
        /// </summary>
        internal static float[] BuildGoldPickup(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.24f);

            AddBellVoice(buf, 0, 0.20f, NoteC6, 0.10f, 0.62f, 0.75f);

            // A breath of coin noise on the attack, so it is metal rather than a synth ding.
            float[] chink = SynthOps.Buffer(0.03f);
            SynthOps.AddNoise(chink, 0, chink.Length, 1f, ref rng);
            SynthOps.BandPass(chink, 4000f, 11000f);
            SynthOps.ApplyPercussive(chink, 0, chink.Length, 0.0004f, 0.018f, 3.2f);
            SynthOps.NormalizePeak(chink, 0.22f);
            SynthOps.Mix(buf, 0, chink, 1f);

            return Finish(buf, 0.48f, 0.03f);
        }

        // ================================================================== run flow

        /// <summary>
        /// One second of the pre-round countdown. A temple wood block: dry, unpitched,
        /// 70 ms, no tail.
        ///
        /// Unpitched on purpose. Three ticks and then a pitched round-start bell means the
        /// bell ARRIVES; three pitched ticks and then a bell is just a fourth note.
        /// </summary>
        internal static float[] BuildCountdownTick(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.09f);

            float[] block = SynthOps.Buffer(0.06f);
            SynthOps.AddNoise(block, 0, block.Length, 0.5f, ref rng);
            float f = 860f * Detune(ref rng, 15f);
            SynthOps.AddTone(block, 0, block.Length, f, f * 0.86f, 0.9f, SynthWave.Triangle);
            SynthOps.AddTone(block, 0, block.Length, f * 2.71f, f * 2.4f, 0.25f);
            SynthOps.BandPass(block, 400f, 5000f);
            SynthOps.ApplyPercussive(block, 0, block.Length, 0.0008f, 0.030f, 3.0f);
            SynthOps.NormalizePeak(block, 0.7f);
            SynthOps.Mix(buf, 0, block, 1f);

            AddTailLayer(buf, 0.55f, 0.5f, 0.08f);
            return Finish(buf, 0.58f, 0.01f);
        }

        /// <summary>
        /// Round start. The request wants tension to RISE here, and this clip is the
        /// leading edge of that rise: a 1.1 second noise swell climbing into a struck bell
        /// on the tonic, with the low A arriving underneath it.
        ///
        /// The rest of the rise is not in this clip at all - the ambience director takes
        /// the tension beds up over the following seconds. This is the strike; the beds
        /// are the sustain. Baking the whole rise into one clip would have locked its
        /// length to a number the round flow does not know.
        /// </summary>
        internal static float[] BuildRoundStart(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(2.40f);

            // Swell: filtered noise rising into the downbeat.
            float[] swell = SynthOps.Buffer(0.62f);
            SynthOps.AddPinkNoise(swell, 0, swell.Length, 1f, ref rng);
            SynthOps.SweepResonantLowPass(swell, 220f, 3400f, 2.0f);
            SynthOps.ApplyAdsr(swell, 0, swell.Length, 0.50f, 0.06f, 0.55f, 0.05f);
            SynthOps.NormalizePeak(swell, 0.42f);
            SynthOps.Mix(buf, 0, swell, 1f);

            // The downbeat: a bell on the tonic, with the octave below it for weight.
            AddBellVoice(buf, SynthOps.Samples(0.58f), 1.60f, NoteA4, 0.85f, 0.74f, 0.85f);
            AddBellVoice(buf, SynthOps.Samples(0.60f), 1.50f, 220f, 1.00f, 0.80f, 0.55f);

            // A fifth arriving a beat later: the interval that says "this is beginning",
            // not "this is finished".
            AddNote(buf, SynthOps.Samples(0.92f), 0.90f, NoteE5, 0.34f, 0.02f, 1.8f);

            AddTailLayer(buf, 0.90f, 0.22f, 0.34f);
            return Finish(buf, 0.90f, 0.20f);
        }

        /// <summary>
        /// Round complete. The answer to RoundStart, and the audible instruction to stop
        /// holding the trigger.
        ///
        /// Three notes falling to the tonic (E5 - C5 - A4) over a soft gong, with the
        /// longest non-death tail in the game. A descending resolution is universally read
        /// as "that is over"; the round-start phrase rises, this one falls, and the two are
        /// built from the same five notes so they are obviously two halves of one idea.
        /// </summary>
        internal static float[] BuildRoundComplete(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(3.00f);

            AddBellVoice(buf, 0, 2.40f, 220f, 1.60f, 0.78f, 0.60f);

            AddNote(buf, SynthOps.Samples(0.00f), 0.60f, NoteE5, 0.62f, 0.006f, 2.0f);
            AddNote(buf, SynthOps.Samples(0.26f), 0.70f, NoteC5, 0.58f, 0.006f, 2.0f);
            AddNote(buf, SynthOps.Samples(0.54f), 1.60f, NoteA4, 0.72f, 0.008f, 1.5f);

            // The fifth held underneath the resolution. Consonant, calm, and the first
            // thing in the round that has not been an attack.
            AddNote(buf, SynthOps.Samples(0.54f), 1.80f, NoteE5 * 0.5f, 0.26f, 0.10f, 1.4f);

            SynthOps.AddPinkNoise(buf, 0, SynthOps.Samples(0.05f), 0.05f, ref rng);

            AddTailLayer(buf, 0.92f, 0.20f, 0.38f);
            return Finish(buf, 0.84f, 0.40f);
        }

        // ================================================================== interface

        /// <summary>
        /// Menu navigation. 35 ms, one soft tick, quieter than everything else in the game.
        ///
        /// This is the sound a player will hear more times than any other, so it is built
        /// against fatigue first and character second: no transient above 5 kHz, no pitch
        /// the ear can get stuck on, and a peak 12 dB below the combat clips. A navigation
        /// sound that is interesting is a navigation sound you will mute by round four.
        /// </summary>
        internal static float[] BuildUiNavigate(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.05f);

            float f = NoteC6 * Detune(ref rng, 30f);
            SynthOps.AddTone(buf, 0, buf.Length, f, f * 0.94f, 0.7f, SynthWave.Triangle);
            SynthOps.AddNoise(buf, 0, SynthOps.Samples(0.004f), 0.25f, ref rng);
            SynthOps.BandPass(buf, 700f, 4800f);
            SynthOps.ApplyPercussive(buf, 0, buf.Length, 0.0012f, 0.028f, 2.6f);

            return Finish(buf, 0.26f, 0.008f);
        }

        /// <summary>
        /// Confirm (<paramref name="rising"/> true) and back (false). Deliberately the same
        /// two notes in opposite order: A5 - E6 accepts, E6 - A5 retreats. One phrase, two
        /// directions, no extra memory and no possible confusion about which happened.
        /// </summary>
        internal static float[] BuildUiTone(bool rising, ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.30f);

            float first = rising ? NoteA5 : NoteE6;
            float second = rising ? NoteE6 : NoteA5;

            AddNote(buf, 0, 0.10f, first, 0.5f, 0.002f, 2.6f);
            AddNote(buf, SynthOps.Samples(0.055f), 0.22f, second, 0.55f, 0.002f, 2.2f);

            SynthOps.AddNoise(buf, 0, SynthOps.Samples(0.003f), 0.08f, ref rng);
            AddTailLayer(buf, 0.5f, 0.55f, 0.10f);

            return Finish(buf, 0.40f, 0.02f);
        }

        /// <summary>
        /// Refused: not enough gold, locked card, invalid input.
        ///
        /// A minor second (A5 against B flat 5) plus a low damped thud. The interval is the
        /// only true dissonance in the interface palette, so "no" is legible on the first
        /// hearing without ever being loud or harsh - it does not need to be, because
        /// nothing else in the game beats like that.
        /// </summary>
        internal static float[] BuildUiDenied(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(0.26f);

            AddNote(buf, 0, 0.18f, NoteA5, 0.45f, 0.002f, 2.4f);
            AddNote(buf, 0, 0.18f, NoteA5 * 1.0595f, 0.45f, 0.002f, 2.4f);

            float[] thud = SynthOps.Buffer(0.12f);
            SynthOps.AddTone(thud, 0, thud.Length, 175f, 120f, 1f);
            SynthOps.AddNoise(thud, 0, thud.Length, 0.3f, ref rng);
            SynthOps.LowPass(thud, 450f);
            SynthOps.ApplyPercussive(thud, 0, thud.Length, 0.002f, 0.07f, 2.8f);
            SynthOps.NormalizePeak(thud, 0.35f);
            SynthOps.Mix(buf, 0, thud, 1f);

            return Finish(buf, 0.44f, 0.02f);
        }

        /// <summary>
        /// An upgrade card bought. The reward sound of the whole run economy, so it is the
        /// only interface sound allowed a real tail.
        ///
        /// A three-note rising arpeggio on the palace scale (A4 - C5 - E5) over a soft
        /// bell, with a coin shimmer on the attack. It resolves upward and it resolves
        /// slowly, which is the difference between "transaction accepted" and "you got
        /// stronger".
        /// </summary>
        internal static float[] BuildUpgradePurchase(ref SynthRandom rng)
        {
            float[] buf = SynthOps.Buffer(1.30f);

            AddNote(buf, 0, 0.22f, NoteA4, 0.55f, 0.003f, 2.4f);
            AddNote(buf, SynthOps.Samples(0.075f), 0.26f, NoteC5, 0.58f, 0.003f, 2.4f);
            AddNote(buf, SynthOps.Samples(0.150f), 0.75f, NoteE5, 0.68f, 0.003f, 1.8f);

            AddBellVoice(buf, SynthOps.Samples(0.150f), 0.95f, NoteA5, 0.55f, 0.70f, 0.42f);

            // Coins. Six short high grains scattered over the first 200 ms.
            SynthOps.AddGrains(buf, 0, SynthOps.Samples(0.20f),
                6, 5000f, 11000f, 0.002f, 0.007f, 0.16f, ref rng);

            AddTailLayer(buf, 0.80f, 0.30f, 0.26f);
            return Finish(buf, 0.62f, 0.12f);
        }
    }
}
