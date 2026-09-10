using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// The recipe book. One function per <see cref="AudioSoundId"/>, split over five
    /// partial files by family.
    ///
    /// HOW TO READ A RECIPE
    /// Every non-trivial sound here is built from the same three-layer skeleton the
    /// request demands for weapons in REQ-121, because it turns out to be the right
    /// skeleton for almost everything:
    ///
    ///   MECHANIC  - the very short, bright transient that says an object was touched.
    ///               6-20 ms, high-passed, no low end. This is the layer the ear uses to
    ///               tell two similar sounds apart, so it is where most of the
    ///               differentiation budget goes.
    ///   BODY      - the 40-300 ms mass of the sound. Noise plus a falling tone through a
    ///               swept resonant low pass. This is the layer that carries weight.
    ///   TAIL      - reverb, and sometimes a discrete slap-back. This is the layer that
    ///               says WHERE it happened - a courtyard in an empty palace.
    ///
    /// Weapon weight is expressed as a rebalance of exactly those three layers rather
    /// than as a volume change: the SMG is mechanic-forward with almost no tail, the
    /// shotgun is body-forward with the longest tail, and the rifle sits between them.
    /// That is why they are still distinguishable at the same playback level.
    ///
    /// DETERMINISM: <paramref name="variation"/> only ever enters through the seed. Two
    /// runs of the game produce bit-identical clips; two variations of the same event
    /// differ in noise seed, partial detune and envelope times, never in structure.
    /// </summary>
    public static partial class AudioRecipes
    {
        /// <summary>
        /// How many distinct clips are baked for each sound.
        ///
        /// Rule of thumb applied throughout: the more often a sound repeats, the more
        /// variations it needs. A rifle at 8.5 rounds/second is the extreme case; a round
        /// completion fanfare plays once every couple of minutes and needs exactly one.
        /// Memory cost is linear in this number, so it is not generous anywhere.
        /// </summary>
        public static int VariationCount(AudioSoundId id)
        {
            switch (id)
            {
                // Fired several times per second - the repetition is the whole problem.
                case AudioSoundId.RifleFire:
                case AudioSoundId.SmgFire:
                case AudioSoundId.HeavyFire:
                case AudioSoundId.FleshHit:
                case AudioSoundId.HitMarker:
                    return 6;

                case AudioSoundId.ShotgunFire:
                case AudioSoundId.ImpactDefault:
                case AudioSoundId.ImpactStone:
                case AudioSoundId.ImpactWood:
                case AudioSoundId.ImpactFoliage:
                case AudioSoundId.ImpactWater:
                case AudioSoundId.ImpactMetal:
                case AudioSoundId.ImpactDirt:
                case AudioSoundId.ImpactPaper:
                case AudioSoundId.ArmorImpact:
                case AudioSoundId.CriticalHit:
                case AudioSoundId.Ricochet:
                    return 4;

                case AudioSoundId.ArmorCrack:
                case AudioSoundId.ArmorBreak:
                case AudioSoundId.EnemyDeath:
                case AudioSoundId.HeavyEnemyDeath:
                case AudioSoundId.ChainArc:
                case AudioSoundId.Explosion:
                case AudioSoundId.PlayerDamage:
                case AudioSoundId.ReloadInsert:
                case AudioSoundId.AmbWoodCreak:
                case AudioSoundId.AmbBambooKnock:
                case AudioSoundId.AmbStoneSettle:
                    return 3;

                // Measured exception: the metal ring is 2.2 s long, so a third variation
                // costs 388 KB - more than the entire UI palette - for a sound the
                // scatterer plays roughly once a minute. Two is enough for the no-repeat
                // rule, which is the only thing the third one was buying.
                case AudioSoundId.AmbMetalRing:
                    return 2;

                case AudioSoundId.DryFire:
                case AudioSoundId.ReloadStart:
                case AudioSoundId.ReloadEnd:
                case AudioSoundId.KillMarker:
                case AudioSoundId.GoldPickup:
                case AudioSoundId.UiNavigate:
                    return 2;

                // Everything else is a landmark: it should sound the same every time so
                // the player can learn it. One clip, no variation, on purpose.
                default:
                    return 1;
            }
        }

        /// <summary>Builds one variation of one sound. Never returns null.</summary>
        public static float[] Build(AudioSoundId id, int variation)
        {
            SynthRandom rng = new SynthRandom(SynthRandom.Seed((int)id, variation));

            switch (id)
            {
                // ---- weapons --------------------------------------------------
                case AudioSoundId.RifleFire: return BuildWeaponFire(WeaponVoice.Rifle, ref rng);
                case AudioSoundId.SmgFire: return BuildWeaponFire(WeaponVoice.Smg, ref rng);
                case AudioSoundId.ShotgunFire: return BuildWeaponFire(WeaponVoice.Shotgun, ref rng);
                case AudioSoundId.HeavyFire: return BuildWeaponFire(WeaponVoice.Heavy, ref rng);
                case AudioSoundId.DryFire: return BuildDryFire(ref rng);
                case AudioSoundId.ReloadStart: return BuildReloadStart(ref rng);
                case AudioSoundId.ReloadInsert: return BuildReloadInsert(ref rng);
                case AudioSoundId.ReloadEnd: return BuildReloadEnd(ref rng);
                case AudioSoundId.HeavyMotorLoop: return BuildHeavyMotorLoop(ref rng);
                case AudioSoundId.HeavySpinDown: return BuildHeavySpinDown(ref rng);

                // ---- surfaces -------------------------------------------------
                case AudioSoundId.ImpactDefault: return BuildImpactDefault(ref rng);
                case AudioSoundId.ImpactStone: return BuildImpactStone(ref rng);
                case AudioSoundId.ImpactWood: return BuildImpactWood(ref rng);
                case AudioSoundId.ImpactFoliage: return BuildImpactFoliage(ref rng);
                case AudioSoundId.ImpactWater: return BuildImpactWater(ref rng);
                case AudioSoundId.ImpactMetal: return BuildImpactMetal(ref rng);
                case AudioSoundId.ImpactDirt: return BuildImpactDirt(ref rng);
                case AudioSoundId.ImpactPaper: return BuildImpactPaper(ref rng);

                // ---- targets --------------------------------------------------
                case AudioSoundId.FleshHit: return BuildFleshHit(false, ref rng);
                case AudioSoundId.CriticalHit: return BuildFleshHit(true, ref rng);
                case AudioSoundId.ArmorImpact: return BuildArmorImpact(false, ref rng);
                case AudioSoundId.ArmorCrack: return BuildArmorImpact(true, ref rng);
                case AudioSoundId.ArmorBreak: return BuildArmorBreak(ref rng);
                case AudioSoundId.EnemyDeath: return BuildEnemyDeath(false, ref rng);
                case AudioSoundId.HeavyEnemyDeath: return BuildEnemyDeath(true, ref rng);

                // ---- upgrade branches -----------------------------------------
                case AudioSoundId.Ricochet: return BuildRicochet(ref rng);
                case AudioSoundId.ChainArc: return BuildChainArc(ref rng);
                case AudioSoundId.Explosion: return BuildExplosion(ref rng);

                // ---- special weapon -------------------------------------------
                case AudioSoundId.SwSpawnGong: return BuildSpawnGong(ref rng);
                case AudioSoundId.SwBeaconLoop: return BuildBeaconLoop(ref rng);
                case AudioSoundId.SwPickup: return BuildSwPickup(ref rng);
                case AudioSoundId.SwFinalSeconds: return BuildSwFinalSeconds(ref rng);
                case AudioSoundId.SwExpired: return BuildSwExpired(false, ref rng);
                case AudioSoundId.SwGroundExpired: return BuildSwExpired(true, ref rng);

                // ---- confirmation ---------------------------------------------
                case AudioSoundId.HitMarker: return BuildHitMarker(ref rng);
                case AudioSoundId.KillMarker: return BuildKillMarker(ref rng);

                // ---- meta ------------------------------------------------------
                case AudioSoundId.PlayerDamage: return BuildPlayerDamage(ref rng);
                case AudioSoundId.PlayerDeath: return BuildPlayerDeath(ref rng);
                case AudioSoundId.GoldPickup: return BuildGoldPickup(ref rng);
                case AudioSoundId.RoundCountdownTick: return BuildCountdownTick(ref rng);
                case AudioSoundId.RoundStart: return BuildRoundStart(ref rng);
                case AudioSoundId.RoundComplete: return BuildRoundComplete(ref rng);
                case AudioSoundId.UiNavigate: return BuildUiNavigate(ref rng);
                case AudioSoundId.UiConfirm: return BuildUiTone(true, ref rng);
                case AudioSoundId.UiBack: return BuildUiTone(false, ref rng);
                case AudioSoundId.UiDenied: return BuildUiDenied(ref rng);
                case AudioSoundId.UpgradePurchase: return BuildUpgradePurchase(ref rng);

                // ---- ambience --------------------------------------------------
                case AudioSoundId.AmbWind: return BuildWindBed(ref rng);
                case AudioSoundId.AmbLeaves: return BuildLeafBed(ref rng);
                case AudioSoundId.AmbWater: return BuildWaterBed(ref rng);
                case AudioSoundId.AmbPalaceTone: return BuildPalaceTone(ref rng);
                case AudioSoundId.AmbTensionLow: return BuildTensionBed(false, ref rng);
                case AudioSoundId.AmbTensionHigh: return BuildTensionBed(true, ref rng);
                case AudioSoundId.AmbWoodCreak: return BuildWoodCreak(ref rng);
                case AudioSoundId.AmbMetalRing: return BuildMetalRing(ref rng);
                case AudioSoundId.AmbBambooKnock: return BuildBambooKnock(ref rng);
                case AudioSoundId.AmbStoneSettle: return BuildStoneSettle(ref rng);

                default:
                    return BuildImpactDefault(ref rng);
            }
        }

        /// <summary>True for the beds that must be played as seamless loops.</summary>
        public static bool IsLoopBed(AudioSoundId id)
        {
            switch (id)
            {
                case AudioSoundId.AmbWind:
                case AudioSoundId.AmbLeaves:
                case AudioSoundId.AmbWater:
                case AudioSoundId.AmbPalaceTone:
                case AudioSoundId.AmbTensionLow:
                case AudioSoundId.AmbTensionHigh:
                case AudioSoundId.SwBeaconLoop:
                case AudioSoundId.HeavyMotorLoop:
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ shared helpers

        /// <summary>
        /// The MECHANIC layer: a very short, high-passed transient. Always the first
        /// thing in the buffer and always the loudest thing in its own band.
        /// </summary>
        internal static void AddMechanicLayer(
            float[] buf, float lengthSeconds, float highPassHz, float clickFreq, float gain, ref SynthRandom rng)
        {
            int count = SynthOps.Samples(lengthSeconds);
            if (count >= buf.Length)
            {
                count = buf.Length - 1;
            }

            if (count <= 4)
            {
                return;
            }

            float[] layer = new float[count];
            SynthOps.AddNoise(layer, 0, count, 1f, ref rng);
            SynthOps.AddTone(layer, 0, count, clickFreq, clickFreq * 0.55f, 0.7f, SynthWave.Square);
            SynthOps.HighPass(layer, highPassHz);
            SynthOps.ApplyPercussive(layer, 0, count, 0.0004f, lengthSeconds * 0.7f, 3.2f);
            SynthOps.NormalizePeak(layer, gain);

            SynthOps.Mix(buf, 0, layer, 1f);
        }

        /// <summary>
        /// The BODY layer: noise plus a falling tone driven through a swept resonant low
        /// pass, then saturated. Everything heavy in the game is some tuning of this.
        /// </summary>
        internal static void AddBodyLayer(
            float[] buf, int startSample, float lengthSeconds,
            float toneStartHz, float toneEndHz,
            float cutoffStartHz, float cutoffEndHz, float q,
            float noiseAmount, float drive, float gain, ref SynthRandom rng)
        {
            int count = SynthOps.Samples(lengthSeconds);
            if (startSample + count >= buf.Length)
            {
                count = buf.Length - startSample - 1;
            }

            if (count <= 8)
            {
                return;
            }

            float[] layer = new float[count];
            SynthOps.AddNoise(layer, 0, count, noiseAmount, ref rng);
            SynthOps.AddTone(layer, 0, count, toneStartHz, toneEndHz, 1f - noiseAmount * 0.5f);
            SynthOps.SweepResonantLowPass(layer, cutoffStartHz, cutoffEndHz, q);
            SynthOps.Saturate(layer, drive);
            SynthOps.ApplyPercussive(layer, 0, count, 0.0018f, lengthSeconds * 0.85f, 1.9f);
            SynthOps.NormalizePeak(layer, gain);

            SynthOps.Mix(buf, startSample, layer, 1f);
        }

        /// <summary>
        /// The TAIL layer: the courtyard. Applied to the finished buffer, so it picks up
        /// whatever the mechanic and body layers put in front of it.
        /// </summary>
        internal static void AddTailLayer(float[] buf, float roomSize, float damping, float mix)
        {
            SynthOps.Reverb(buf, roomSize, damping, mix);
        }

        /// <summary>Final pass every recipe ends with: de-click, normalise, done.</summary>
        internal static float[] Finish(float[] buf, float peak = 0.92f, float fadeOut = 0.008f)
        {
            SynthOps.FadeEdges(buf, 0.0012f, fadeOut);
            SynthOps.NormalizePeak(buf, peak);
            return buf;
        }

        internal static float Detune(ref SynthRandom rng, float cents)
        {
            return Mathf.Pow(2f, rng.Range(-cents, cents) / 1200f);
        }
    }
}
