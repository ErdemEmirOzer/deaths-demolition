using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// Everything the voice pool needs to know about one sound, resolved per cue.
    /// </summary>
    public struct AudioPlacement
    {
        public AudioSoundId Sound;
        public AudioBus Bus;

        /// <summary>Linear gain before bus and master. Set by design, never by the producer.</summary>
        public float Gain;

        /// <summary>
        /// 0-100. This is the DESIGN priority and it is the authority; the producer's own
        /// number only nudges it (see <see cref="AudioMixPlan.EffectivePriority"/>).
        /// </summary>
        public float Priority;

        /// <summary>0 = centred in the head, 1 = fully positional.</summary>
        public float Spatial;

        /// <summary>Simultaneous copies of THIS sound allowed. The oldest is recycled beyond it.</summary>
        public int MaxInstances;

        /// <summary>Seconds before this sound may retrigger at all. 0 = no limit.</summary>
        public float MinRetrigger;

        /// <summary>Random pitch spread as a ratio, e.g. 0.04 = +/- 4%.</summary>
        public float PitchJitter;

        /// <summary>Fixed pitch multiplier, applied after the jitter. 1 = as baked.</summary>
        public float Pitch;

        /// <summary>How far this sound pulls the ambience down, 1 = not at all.</summary>
        public float DuckAmbience;

        /// <summary>How far this sound pulls the combat buses down. 1 = not at all.</summary>
        public float DuckCombat;

        /// <summary>Seconds the duck is held before it releases.</summary>
        public float DuckSeconds;

        /// <summary>WP-057: gain floor for the Music bus while this plays (1 = no duck). HudLowHealth / threat chips / stingers use it.</summary>
        public float DuckMusic;

        public bool IsValid => Sound != AudioSoundId.None;
    }

    /// <summary>
    /// The routing table: which sound a gameplay event becomes, how loud, how important,
    /// how many at once, and what it pushes out of the way.
    ///
    /// WHY THIS IS A SEPARATE FILE FROM THE RECIPES
    /// A recipe answers "what does an armour break sound like". This answers "what is an
    /// armour break worth when nineteen other things are trying to be heard". They change
    /// for completely different reasons - the first when the sound is wrong, the second
    /// when the mix is crowded - and keeping them apart means retuning the crowd never
    /// risks retuning the sound.
    ///
    /// THE PRIORITY LADDER, and the reasoning behind each rung:
    ///
    ///   100  player death, player damage, Special Weapon spawn
    ///        The player must hear these or the run is unfair. Nothing may steal them.
    ///    90  round start / complete, upgrade purchase, SW pickup / final seconds
    ///        Run-flow landmarks. They happen a handful of times a round and they are
    ///        instructions, not decoration.
    ///    80  armour break, heavy enemy death, explosion
    ///        The three loudest combat facts. Losing one of these in a crowd is exactly
    ///        the failure the request names.
    ///    70  enemy death, gold
    ///        Common but still payoffs.
    ///    60  heavy weapon fire, kill marker, armour hit / crack
    ///    50  classic weapon fire, critical hit, UI
    ///    40  reloads, ricochet, chain arc, dry fire
    ///    30  flesh hit, hit marker
    ///        The two most frequent sounds in the game, and therefore the two cheapest to
    ///        lose: the twentieth flesh hit in a second carries no information the first
    ///        nineteen did not.
    ///    20  surface impacts
    ///    10  ambient one-shots
    ///        Furniture. First to go, always, and nobody notices.
    /// </summary>
    /// <summary>Light = Standard/Special tier, Heavy = Elite/Ultra. Only the mix cares.</summary>
    public enum EnemyTierWeight
    {
        Light = 0,
        Heavy = 1
    }

    public static class AudioMixPlan
    {
        // ---- classic weapon resolution ---------------------------------------

        /// <summary>
        /// Which of the three classic weapons fired, read from the cue intensity.
        ///
        /// WeaponController sends PowerScale(_classic.Power): 0.75 for Light, 1.0 for
        /// Medium, 1.5 for Heavy - i.e. the SMG, the assault rifle and the combat shotgun.
        /// Reading the weapon identity out of a float that was meant as a loudness hint is
        /// not pretty, but the alternative is a second enum on the combat contract, and
        /// that contract belongs to another task. The thresholds sit halfway between the
        /// three values so a retuned PowerScale has to move a long way to break this.
        /// </summary>
        public static AudioSoundId ClassicWeaponFor(float intensity)
        {
            if (intensity <= 0.87f)
            {
                return AudioSoundId.SmgFire;
            }

            if (intensity >= 1.25f)
            {
                return AudioSoundId.ShotgunFire;
            }

            return AudioSoundId.RifleFire;
        }

        /// <summary>
        /// Gold streak transposition, in the palace pentatonic.
        ///
        /// Six orbs swept up by the magnet play A - B - C - E - F - A' rather than six
        /// identical dings. The scale is the same hirajoshi set the UI and the round-flow
        /// sounds use, so a streak landing on top of a round-complete phrase is consonant
        /// with it by construction.
        /// </summary>
        public static float GoldStreakPitch(int streakIndex)
        {
            switch (Mathf.Clamp(streakIndex, 0, 5))
            {
                case 1: return 1.1225f;  // B
                case 2: return 1.1892f;  // C
                case 3: return 1.4983f;  // E
                case 4: return 1.5874f;  // F
                case 5: return 2.0000f;  // A, octave
                default: return 1f;      // A
            }
        }

        // ---- combat events ---------------------------------------------------

        /// <summary>Resolves a combat cue to a placement. <see cref="AudioSoundId.None"/> means "play nothing".</summary>
        public static AudioPlacement Resolve(in CombatAudioCue cue, bool armourIsFailing)
        {
            switch (cue.Event)
            {
                // ---- weapon mechanics ------------------------------------------
                // WP-060: continuous fire built a −14…−15 LUFS-S wall over vocals / steps / beds on
                // the Player capture; −1 dB and one instance fewer open a ~2 dB window for them.
                case CombatAudioEvent.ClassicWeaponFire:
                    return Weapon(ClassicWeaponFor(cue.Intensity), 0.85f, 50f, 3, 0.03f);

                case CombatAudioEvent.HeavyWeaponFire:
                    return Weapon(AudioSoundId.HeavyFire, 0.90f, 60f, 5, 0.05f);

                case CombatAudioEvent.ClassicWeaponDryFire:
                    return Weapon(AudioSoundId.DryFire, 0.70f, 40f, 1, 0.02f);

                case CombatAudioEvent.ReloadStart:
                    return Weapon(AudioSoundId.ReloadStart, 0.80f, 42f, 1, 0.02f);

                case CombatAudioEvent.ReloadInsert:
                    return Weapon(AudioSoundId.ReloadInsert, 0.80f, 42f, 1, 0.02f);

                case CombatAudioEvent.ReloadEnd:
                    return Weapon(AudioSoundId.ReloadEnd, 0.80f, 42f, 1, 0.02f);

                case CombatAudioEvent.HeavyWeaponSpinUp:
                    // The loop the director ramps into. Not a one-shot; it never competes
                    // for the one-shot budget.
                    return Weapon(AudioSoundId.HeavyMotorLoop, 0.55f, 85f, 1, 0f);

                case CombatAudioEvent.HeavyWeaponSpinDown:
                    return Weapon(AudioSoundId.HeavySpinDown, 0.70f, 85f, 1, 0f);

                // ---- surfaces ---------------------------------------------------
                case CombatAudioEvent.SurfaceImpactDefault: return Impact(AudioSoundId.ImpactDefault);
                case CombatAudioEvent.SurfaceImpactStone: return Impact(AudioSoundId.ImpactStone);
                case CombatAudioEvent.SurfaceImpactWood: return Impact(AudioSoundId.ImpactWood);
                case CombatAudioEvent.SurfaceImpactFoliage: return Impact(AudioSoundId.ImpactFoliage);
                case CombatAudioEvent.SurfaceImpactWater: return Impact(AudioSoundId.ImpactWater);
                case CombatAudioEvent.SurfaceImpactMetal: return Impact(AudioSoundId.ImpactMetal);
                case CombatAudioEvent.SurfaceImpactDirt: return Impact(AudioSoundId.ImpactDirt);
                case CombatAudioEvent.SurfaceImpactPaper: return Impact(AudioSoundId.ImpactPaper);

                // ---- targets ----------------------------------------------------
                case CombatAudioEvent.FleshHit:
                    return Target(AudioSoundId.FleshHit, 0.72f, 30f, 4, 0.05f);

                case CombatAudioEvent.CriticalHit:
                    return Target(AudioSoundId.CriticalHit, 0.90f, 50f, 3, 0.04f);

                case CombatAudioEvent.ArmorHit:
                    // REQ-129 rungs 1 and 2. The escalation is decided by the fatigue
                    // tracker, not by the cue, because the producer sends a constant
                    // intensity for every armour hit.
                    return Target(
                        armourIsFailing ? AudioSoundId.ArmorCrack : AudioSoundId.ArmorImpact,
                        0.85f, 60f, 3, 0.035f);

                case CombatAudioEvent.ArmorBreak:
                {
                    AudioPlacement p = Target(AudioSoundId.ArmorBreak, 1.00f, 80f, 3, 0.02f);
                    // The break makes room for itself. Without this it lands inside the
                    // burst that caused it and the tail - which is the whole cue - is lost.
                    p.DuckAmbience = 0.45f;
                    p.DuckCombat = 0.70f;
                    p.DuckSeconds = 0.28f;
                    p.MinRetrigger = 0.05f;
                    return p;
                }

                case CombatAudioEvent.EnemyDeath:
                    return Target(AudioSoundId.EnemyDeath, 0.85f, 70f, 3, 0.05f);

                case CombatAudioEvent.HeavyEnemyDeath:
                {
                    AudioPlacement p = Target(AudioSoundId.HeavyEnemyDeath, 1.00f, 80f, 2, 0.03f);
                    p.DuckAmbience = 0.40f;
                    p.DuckCombat = 0.75f;
                    p.DuckSeconds = 0.45f;
                    return p;
                }

                // ---- upgrade branches -------------------------------------------
                case CombatAudioEvent.Ricochet:
                    return Impact(AudioSoundId.Ricochet, 0.55f, 40f, 3);

                case CombatAudioEvent.ChainArc:
                    return Impact(AudioSoundId.ChainArc, 0.65f, 40f, 3);

                case CombatAudioEvent.Explosion:
                {
                    AudioPlacement p = Target(AudioSoundId.Explosion, 1.00f, 80f, 2, 0.02f);
                    p.DuckAmbience = 0.30f;
                    p.DuckCombat = 0.60f;
                    p.DuckSeconds = 0.55f;
                    p.MinRetrigger = 0.06f;
                    p.Spatial = 0.55f;
                    return p;
                }

                // ---- special weapon ---------------------------------------------
                case CombatAudioEvent.SpecialWeaponSpawn:
                {
                    AudioPlacement p = Interface(AudioSoundId.SwSpawnGong, 1.00f, 100f);
                    // The gong is the loudest instruction in the game. It flattens the
                    // ambience for a second and a half and pushes combat down with it.
                    p.DuckAmbience = 0.20f;
                    p.DuckCombat = 0.65f;
                    p.DuckSeconds = 1.40f;
                    p.Spatial = 0.35f;
                    p.MinRetrigger = 2f;
                    return p;
                }

                case CombatAudioEvent.SpecialWeaponBeaconLoop:
                    return Interface(AudioSoundId.SwBeaconLoop, 0.55f, 60f, 0.95f);

                case CombatAudioEvent.SpecialWeaponPickup:
                {
                    AudioPlacement p = Interface(AudioSoundId.SwPickup, 0.85f, 90f);
                    p.DuckAmbience = 0.45f;
                    p.DuckSeconds = 0.6f;
                    return p;
                }

                case CombatAudioEvent.SpecialWeaponFinalSeconds:
                {
                    AudioPlacement p = Interface(AudioSoundId.SwFinalSeconds, 0.80f, 90f);
                    p.DuckAmbience = 0.55f;
                    p.DuckSeconds = 0.35f;
                    p.MinRetrigger = 0.5f;
                    return p;
                }

                case CombatAudioEvent.SpecialWeaponExpired:
                    return Interface(AudioSoundId.SwExpired, 0.75f, 88f);

                case CombatAudioEvent.SpecialWeaponGroundExpired:
                    // It ran out somewhere the player may not be. Positional and quiet on
                    // purpose: a full-strength alarm for something that never mattered is a
                    // false alarm, and false alarms are how a player learns to ignore a cue.
                    return Interface(AudioSoundId.SwGroundExpired, 0.45f, 55f, 0.85f);

                // ---- confirmation -------------------------------------------------
                case CombatAudioEvent.HitMarker:
                    // 2D. It is a statement about the interface, not about the world, and
                    // panning it would make the player look for it.
                    return Interface(AudioSoundId.HitMarker, 0.42f, 30f, 0f, 4, 0.02f);

                case CombatAudioEvent.KillMarker:
                    return Interface(AudioSoundId.KillMarker, 0.55f, 60f, 0f, 3, 0.02f);

                default:
                    return default;
            }
        }

        // ---- run flow events --------------------------------------------------

        /// <summary>Resolves a run-flow cue. Gold streak pitch is applied by the caller.</summary>
        public static AudioPlacement Resolve(in RunAudioCue cue)
        {
            switch (cue.Event)
            {
                case RunAudioEvent.GoldCollected:
                {
                    // WP-021 (AP-011): a magnet streak (index >= 2) speaks with the coin-slide
                    // layer, the single pickup with the clink; both keep the hirajoshi transposition.
                    AudioSoundId sound = cue.StreakIndex >= 2 ? AudioSoundId.GoldMagnet : AudioSoundId.GoldPickup;
                    // Coin at 0.40 (sound-designer M3: -4 dB under the rest of the UI bus - it is
                    // the most repeated sound of a run after the flesh hit).
                    AudioPlacement p = Interface(sound, sound == AudioSoundId.GoldPickup ? 0.40f : 0.55f, 70f, 0.35f, 4, 0.015f);
                    p.Pitch = GoldStreakPitch(cue.StreakIndex);
                    return p;
                }

                case RunAudioEvent.GoldSpawned:
                    // WP-021 (AP-011): its own clip now - the coin bag landing on stone.
                    // Quieter and positional: obviously related to a pickup, obviously not one.
                    {
                        AudioPlacement p = Interface(AudioSoundId.GoldSpawn, 0.30f, 20f, 0.7f, 3, 0.03f);
                        p.PitchJitter = 0.05f;
                        return p;
                    }

                case RunAudioEvent.RoundPoolExhausted:
                {
                    // A temple bell across the courtyard. Deliberately an AMBIENCE sound
                    // rather than an interface one: "the pool is dry" is information about
                    // the world, and it must not be confused with the round actually ending.
                    AudioPlacement p = Interface(AudioSoundId.AmbMetalRing, 0.65f, 88f, 0f);
                    p.Pitch = 0.75f;
                    p.MinRetrigger = 3f;
                    p.DuckAmbience = 0.55f;
                    p.DuckSeconds = 1.2f;
                    return p;
                }

                case RunAudioEvent.RoundComplete:
                {
                    AudioPlacement p = Interface(AudioSoundId.RoundComplete, 0.90f, 95f, 0f);
                    p.DuckAmbience = 0.35f;
                    p.DuckCombat = 0.45f;
                    p.DuckSeconds = 2.0f;
                    p.MinRetrigger = 3f;
                    return p;
                }

                case RunAudioEvent.CountdownTick:
                    return Interface(AudioSoundId.RoundCountdownTick, 0.62f, 90f, 0f, 2, 0f);

                case RunAudioEvent.RoundStart:
                {
                    AudioPlacement p = Interface(AudioSoundId.RoundStart, 0.90f, 95f, 0f);
                    p.DuckAmbience = 0.40f;
                    p.DuckSeconds = 1.4f;
                    p.MinRetrigger = 3f;
                    return p;
                }

                case RunAudioEvent.UpgradePurchased:
                    return Interface(AudioSoundId.UpgradePurchase, 0.75f, 90f, 0f, 2, 0.01f);

                case RunAudioEvent.UpgradeRefused:
                    return Interface(AudioSoundId.UiDenied, 0.70f, 85f, 0f, 2, 0.05f);

                case RunAudioEvent.UpgradeSkipped:
                    return Interface(AudioSoundId.UiBack, 0.60f, 80f, 0f, 2, 0.05f);

                case RunAudioEvent.RunEnded:
                {
                    // PlayerHealth.AnyPlayerDied usually got here first; MinRetrigger is
                    // what stops the run ending twice in the same second.
                    AudioPlacement p = Interface(AudioSoundId.PlayerDeath, 1.00f, 100f, 0f);
                    p.DuckAmbience = 0.10f;
                    p.DuckCombat = 0.15f;
                    p.DuckSeconds = 2.6f;
                    p.MinRetrigger = 4f;
                    return p;
                }

                default:
                    return default;
            }
        }

        // ---- player and interface, raised without a cue struct ----------------

        /// <summary>Player damage. The one sound with an absolute claim on being audible.</summary>
        public static AudioPlacement PlayerDamage()
        {
            AudioPlacement p = Interface(AudioSoundId.PlayerDamage, 0.95f, 100f, 0f, 2, 0.06f);
            p.PitchJitter = 0.05f;
            p.DuckAmbience = 0.35f;
            p.DuckCombat = 0.55f;
            p.DuckSeconds = 0.16f;
            return p;
        }

        /// <summary>Player death. Everything else in the mix gets out of its way.</summary>
        public static AudioPlacement PlayerDeath()
        {
            AudioPlacement p = Interface(AudioSoundId.PlayerDeath, 1.00f, 100f, 0f);
            p.DuckAmbience = 0.10f;
            p.DuckCombat = 0.15f;
            p.DuckSeconds = 2.6f;
            p.MinRetrigger = 4f;
            return p;
        }

        /// <summary>
        /// The four interface sounds, for the UI task to call directly.
        /// ⚠ No producer for these exists on disk yet - the UI kit under
        /// Assets/_Prototype/UI has no navigation events. They are reachable through
        /// <see cref="PrototypeAudio"/> and through the F1 panel until it does.
        /// </summary>
        public static AudioPlacement Ui(AudioSoundId sound)
        {
            switch (sound)
            {
                case AudioSoundId.UiNavigate:
                    return Interface(AudioSoundId.UiNavigate, 0.45f, 50f, 0f, 2, 0.03f);
                case AudioSoundId.UiConfirm:
                    return Interface(AudioSoundId.UiConfirm, 0.60f, 55f, 0f, 2, 0.04f);
                case AudioSoundId.UiBack:
                    return Interface(AudioSoundId.UiBack, 0.55f, 55f, 0f, 2, 0.04f);
                case AudioSoundId.UiDenied:
                    return Interface(AudioSoundId.UiDenied, 0.70f, 60f, 0f, 2, 0.06f);
                case AudioSoundId.UiReveal:
                    return UiEvent(AudioSoundId.UiReveal);
                default:
                    return Interface(sound, 0.6f, 50f, 0f, 2, 0.03f);
            }
        }

        // ---- WP-021 · zombie vocal set (DEC-067) ------------------------------

        /// <summary>
        /// Attack telegraph. Priority 75: above a flesh hit and an enemy death, below an
        /// armour break, because "one is about to hit you" is the most useful thing the
        /// target bus can say (REQ-104 audible readability). Fully positional; the ear
        /// must be able to point at it.
        /// </summary>
        public static AudioPlacement ZombieWindup(AudioSoundId sound, EnemyTierWeight weight)
        {
            AudioPlacement p = Target(sound, weight == EnemyTierWeight.Heavy ? 0.95f : 0.80f, 75f, 3, 0.05f);
            p.Spatial = 1f;
            p.PitchJitter = 0.04f;
            return p;
        }

        /// <summary>The swing itself. Short, and cheap to lose in a crowd (the windup already warned).</summary>
        public static AudioPlacement ZombieAttack(AudioSoundId sound, EnemyTierWeight weight)
        {
            AudioPlacement p = Target(sound, weight == EnemyTierWeight.Heavy ? 0.85f : 0.70f, 55f, 3, 0.04f);
            p.Spatial = 1f;
            p.PitchJitter = 0.05f;
            return p;
        }

        /// <summary>Pain grunt. Throttled per enemy by the router; here it is the second-cheapest target sound.</summary>
        public static AudioPlacement ZombieHit(AudioSoundId sound, EnemyTierWeight weight)
        {
            AudioPlacement p = Target(sound, weight == EnemyTierWeight.Heavy ? 0.70f : 0.55f, 32f, 4, 0.06f);
            p.Spatial = 0.9f;
            p.PitchJitter = 0.07f;
            return p;
        }

        /// <summary>The death vocal, layered over the body/impact cue the feedback director already raises.</summary>
        public static AudioPlacement ZombieDeath(AudioSoundId sound, EnemyTierWeight weight)
        {
            AudioPlacement p = Target(sound, weight == EnemyTierWeight.Heavy ? 0.95f : 0.75f, weight == EnemyTierWeight.Heavy ? 80f : 68f, 3, 0.05f);
            p.Spatial = 0.9f;
            p.PitchJitter = 0.05f;
            if (weight == EnemyTierWeight.Heavy)
            {
                p.DuckAmbience = 0.6f;
                p.DuckSeconds = 0.5f;
            }

            return p;
        }

        // ---- WP-021 v003 · coverage matrix rows -------------------------------

        /// <summary>Near-range idle breath/growl. Ambience-priced: never competes with combat.</summary>
        public static AudioPlacement ZombieIdle(AudioSoundId sound, EnemyTierWeight weight)
        {
            AudioPlacement p = Target(sound, weight == EnemyTierWeight.Heavy ? 0.55f : 0.42f, 18f, 3, 0.4f);
            p.Spatial = 1f;
            p.PitchJitter = 0.08f;
            // WP-021C: the tier clips carry their own register; only the shared Standard clip is bent for a heavy body.
            p.Pitch = sound == AudioSoundId.ZombieIdle && weight == EnemyTierWeight.Heavy ? 0.86f : 1f;
            return p;
        }

        /// <summary>WP-021C (spawn_land since WP-048): the aggro growl - the voice that follows the rise's earth. Positional, priced like a windup minus a step.</summary>
        public static AudioPlacement ZombieAggro(AudioSoundId sound, EnemyTierWeight weight)
        {
            AudioPlacement p = Target(sound, weight == EnemyTierWeight.Heavy ? 0.9f : 0.72f, 64f, 4, 0.12f);
            p.Spatial = 1f;
            p.PitchJitter = 0.05f;
            return p;
        }

        /// <summary>
        /// WP-048 (WP-040 armor_break_&lt;region&gt;): the region's own voice - helmet thin and high, torso the
        /// heaviest, leg short with the strap - layered over the tier break cue from ReportEnemyHit. Slightly
        /// under that cue so the pair reads as one break with a place, not two breaks.
        /// </summary>
        public static AudioPlacement ArmorBreakRegion(AudioSoundId sound, EnemyTierWeight weight)
        {
            AudioPlacement p = Target(sound, weight == EnemyTierWeight.Heavy ? 0.8f : 0.66f, 66f, 3, 0.06f);
            p.Spatial = 0.9f;
            p.PitchJitter = 0.04f;
            return p;
        }

        /// <summary>
        /// WP-021C · WP-022B UI events (EVENT-COVERAGE-MATRIX §UI). Every bound row has its own
        /// gain / priority / retrigger so a slider drag never floods and a threat chip is never lost.
        /// </summary>
        public static AudioPlacement UiEvent(AudioSoundId sound)
        {
            switch (sound)
            {
                case AudioSoundId.UiHover: return Interface(sound, 0.30f, 35f, 0f, 2, 0.05f);
                case AudioSoundId.UiSliderTick:
                {
                    // WP-068 M2: ≤ 12 ticks/s while dragging; the router sets the pitch from the value (no jitter on a map).
                    AudioPlacement p = Interface(sound, 0.32f, 35f, 0f, 2, 1f / 12f);
                    p.PitchJitter = 0f;
                    return p;
                }
                case AudioSoundId.UiToggle: return Interface(sound, 0.45f, 45f, 0f, 1, 0.05f);
                case AudioSoundId.UiPaneOpen: return Interface(sound, 0.85f, 50f, 0f, 1, 0.12f);   // WP-068 s11: v015 washi slide sits lower than the v005 leaf rustle
                case AudioSoundId.UiSelect: return Interface(sound, 1.00f, 55f, 0f, 2, 0.05f);   // WP-068 s11: +4.7 dB (menu bed measure 3.7 → ≥ 6 dB)
                case AudioSoundId.UiDialogOpen: return Interface(sound, 0.55f, 60f, 0f, 1, 0.2f);
                // Threat / danger chips (v005 gate M10): −20 LUFS under a full stack is 6–8 LU
                // down, so they duck the world buses ≈ −4 dB and the beds ≈ −3 dB for 0.3 s -
                // the sidechain the software mixer already owns, no snapshot churn.
                case AudioSoundId.HudLowHealth:
                {
                    AudioPlacement p = Interface(sound, 0.75f, 78f, 0f, 1, 1.0f);
                    p.DuckAmbience = 0.7f;
                    p.DuckCombat = 0.63f;
                    p.DuckMusic = 0.7f;      // WP-057 mix pass: the taiko band is where HudLowHealth lives (gate M10)
                    p.DuckSeconds = 0.35f;
                    return p;
                }
                case AudioSoundId.HudMagazineEmpty: return Interface(sound, 0.5f, 52f, 0f, 1, 0.5f);
                case AudioSoundId.HudThreatEncircled:
                {
                    AudioPlacement p = Interface(sound, 0.8f, 82f, 0f, 1, 1.5f);
                    p.DuckAmbience = 0.7f;
                    p.DuckCombat = 0.63f;
                    p.DuckMusic = 0.7f;
                    p.DuckSeconds = 0.35f;
                    return p;
                }
                case AudioSoundId.HudThreatRear:
                {
                    AudioPlacement p = Interface(sound, 0.75f, 80f, 0f, 1, 0.8f);
                    p.DuckAmbience = 0.7f;
                    p.DuckCombat = 0.63f;
                    p.DuckMusic = 0.7f;
                    p.DuckSeconds = 0.3f;
                    return p;
                }
                case AudioSoundId.HudThreatStrong:
                {
                    AudioPlacement p = Interface(sound, 0.7f, 76f, 0f, 1, 2.0f);
                    p.DuckAmbience = 0.7f;
                    p.DuckCombat = 0.63f;
                    p.DuckMusic = 0.7f;
                    p.DuckSeconds = 0.35f;
                    return p;
                }
                case AudioSoundId.UiReroll: return Interface(sound, 0.5f, 55f, 0f, 1, 0.2f);
                case AudioSoundId.UiLock: return Interface(sound, 0.55f, 55f, 0f, 1, 0.2f);
                case AudioSoundId.UiLevelUp: return Interface(sound, 0.7f, 70f, 0f, 1, 0.5f);
                case AudioSoundId.UiUnlock: return Interface(sound, 0.65f, 68f, 0f, 2, 0.25f);
                case AudioSoundId.UiAchievement: return Interface(sound, 0.65f, 66f, 0f, 2, 0.25f);
                case AudioSoundId.UiNewBest: return Interface(sound, 0.75f, 72f, 0f, 1, 0.5f);
                case AudioSoundId.UiReveal: return Interface(sound, 0.55f, 58f, 0f, 2, 0.08f);

                // ---- WP-068 slice 11 · UI v3 (menus: priority by weight; run cues ≤ 60 and never duck - M3)
                case AudioSoundId.UiPressDown: return Interface(sound, 0.62f, 40f, 0f, 2, 0.04f);   // light by design, but ≥ 6 dB over the menu bed (measured 5.9 at 0.35)
                case AudioSoundId.UiTransitionOut: return Interface(sound, 1.00f, 50f, 0f, 1, 0.25f);
                case AudioSoundId.UiCurtainClose: return Interface(sound, 1.00f, 62f, 0f, 1, 0.3f);
                case AudioSoundId.UiCurtainOpen: return Interface(sound, 0.95f, 60f, 0f, 1, 0.3f);
                case AudioSoundId.UiPauseOpen: return Interface(sound, 0.50f, 62f, 0f, 1, 0.2f);
                case AudioSoundId.UiPauseClose: return Interface(sound, 0.45f, 60f, 0f, 1, 0.2f);
                case AudioSoundId.UiBootSeal: return Interface(sound, 0.70f, 70f, 0f, 1, 1.0f);
                case AudioSoundId.UiCardDealt: return Interface(sound, 0.85f, 52f, 0f, 3, 0.04f);     // 3 cards, 60 ms stagger (≥ 6 LU over the bed: 2.6 LU at 0.45)
                case AudioSoundId.UiCardFlipped: return Interface(sound, 0.80f, 52f, 0f, 3, 0.04f);
                case AudioSoundId.UiStampLanded: return Interface(sound, 1.00f, 64f, 0f, 2, 0.11f);   // at its −3 dBTP file ceiling: the gain carries the ≥ 6 LU   // stamps never stack (≤ 110 ms)
                case AudioSoundId.UiStampRare: return Interface(sound, 0.65f, 66f, 0f, 2, 0.11f);
                case AudioSoundId.UiStampEpic: return Interface(sound, 0.70f, 68f, 0f, 2, 0.11f);
                case AudioSoundId.UiCounterRoll: return Interface(sound, 0.35f, 45f, 0f, 1, 0.1f);
                case AudioSoundId.UiCounterSettled: return Interface(sound, 0.45f, 50f, 0f, 2, 0.08f);
                case AudioSoundId.UiHoldRub: return Interface(sound, 0.30f, 45f, 0f, 1, 0.1f);
                case AudioSoundId.UiInkBleed: return Interface(sound, 0.40f, 40f, 0f, 1, 1.0f);
                case AudioSoundId.HudNewBestRound: return Interface(sound, 0.60f, 55f, 0f, 1, 2.0f);  // ribbon: no duck, 2 s retrigger
                case AudioSoundId.HudEliteDown: return Interface(sound, 0.60f, 60f, 0f, 1, 0.6f);     // not a stinger: no duck
                case AudioSoundId.HudUltraDown: return Interface(sound, 0.70f, 60f, 0f, 1, 0.6f);
                default: return Interface(sound, 0.5f, 50f, 0f, 2, 0.05f);
            }
        }

        /// <summary>The claw landing on the player. Heard from the player's side: near, not fully positional.</summary>
        public static AudioPlacement AttackImpactBody()
        {
            AudioPlacement p = Target(AudioSoundId.AttackImpactBody, 0.85f, 90f, 2, 0.05f);
            p.Spatial = 0.4f;
            p.PitchJitter = 0.06f;
            return p;
        }

        public static AudioPlacement ZombieStagger(EnemyTierWeight weight)
        {
            AudioPlacement p = Target(AudioSoundId.ZombieStagger, weight == EnemyTierWeight.Heavy ? 0.75f : 0.6f, 45f, 3, 0.08f);
            p.Spatial = 0.9f;
            p.PitchJitter = 0.06f;
            p.Pitch = weight == EnemyTierWeight.Heavy ? 0.9f : 1f;
            return p;
        }

        /// <summary>Spawn / rise from the ground. World information, priced like a death.</summary>
        public static AudioPlacement ZombieRise(EnemyTierWeight weight)
        {
            AudioPlacement p = Target(AudioSoundId.ZombieRise, weight == EnemyTierWeight.Heavy ? 0.8f : 0.65f, 62f, 4, 0.1f);
            p.Spatial = 1f;
            p.PitchJitter = 0.07f;
            p.Pitch = weight == EnemyTierWeight.Heavy ? 0.88f : 1f;
            return p;
        }

        /// <summary>The broken plate landing, ~0.4 s after the break cue.</summary>
        public static AudioPlacement ArmorPieceDrop()
        {
            AudioPlacement p = Impact(AudioSoundId.ArmorPieceDrop, 0.6f, 28f, 3);
            p.Spatial = 1f;
            return p;
        }

        public static AudioPlacement BreakCeramic()
        {
            return Breakable(AudioSoundId.BreakCeramic);
        }

        /// <summary>WP-048: any breakable family (AP-003B ceramic, AP-017 stone / paper / bamboo / straw / wood) - world, positional, one per prop.</summary>
        public static AudioPlacement Breakable(AudioSoundId sound)
        {
            AudioPlacement p = Impact(sound, sound == AudioSoundId.BreakLanternStone || sound == AudioSoundId.BreakBench ? 0.95f : 0.85f, 58f, 3);
            p.Spatial = 1f;
            p.MinRetrigger = 0.05f;
            p.PitchJitter = 0.05f;
            return p;
        }

        /// <summary>Screen transition (menu ↔ run ↔ results).</summary>
        public static AudioPlacement UiTransition()
        {
            return Interface(AudioSoundId.UiTransition, 0.55f, 85f, 0f, 1, 0.3f);
        }

        /// <summary>WP-058C: the body's reaction to a Special (WP-055 sw_* beats) - positional, under the field touch, heavy tiers deeper.</summary>
        public static AudioPlacement SpecialReaction(AudioSoundId sound, EnemyTierWeight weight)
        {
            AudioPlacement p = Target(sound, weight == EnemyTierWeight.Heavy ? 0.7f : 0.58f, 42f, 6, 0.04f);
            p.Spatial = 0.9f;
            p.PitchJitter = 0.06f;
            p.Pitch = weight == EnemyTierWeight.Heavy ? 0.84f : 1f;
            return p;
        }

        /// <summary>WP-058: the Special's own fire character over the shared HeavyWeaponFire body (player's hand, near).</summary>
        public static AudioPlacement SpecialFire(AudioSoundId sound)
        {
            AudioPlacement p = Weapon(sound, 0.7f, 58f, 3, 0.05f);
            p.Spatial = 0.25f;
            p.PitchJitter = 0.03f;
            return p;
        }

        /// <summary>WP-058: field spawn / end at the field origin - world, positional, wide.</summary>
        public static AudioPlacement SpecialField(AudioSoundId sound)
        {
            AudioPlacement p = Impact(sound, 0.9f, 64f, 2);
            p.Spatial = 0.9f;
            p.MinRetrigger = 0.1f;
            p.DuckAmbience = 0.75f;
            p.DuckSeconds = 0.4f;
            return p;
        }

        /// <summary>WP-058: one body touched by a field - cheap, many at once.</summary>
        public static AudioPlacement SpecialHit(AudioSoundId sound)
        {
            AudioPlacement p = Target(sound, 0.6f, 36f, 6, 0.03f);
            p.Spatial = 0.9f;
            p.PitchJitter = 0.07f;
            return p;
        }

        /// <summary>WP-072 B1: one v016 fire voice (dry shot or hybrid transient) - the legacy classic-fire placement.</summary>
        public static AudioPlacement PlayerWeapon(AudioSoundId sound)
        {
            return Weapon(sound, 0.85f, 50f, 3, 0.03f);
        }

        /// <summary>WP-072 B1: the wet space tail after a shot / release - lower priority, never piles past two.</summary>
        public static AudioPlacement PlayerWeaponTail(AudioSoundId sound)
        {
            AudioPlacement p = Weapon(sound, 0.85f, 30f, 2, 0.05f);
            p.PitchJitter = 0f;
            return p;
        }

        /// <summary>WP-072 B3: a projectile hitting the world - the legacy impact placement, one more instance for auto fire.</summary>
        public static AudioPlacement ProjectileImpact(AudioSoundId sound)
        {
            return Impact(sound, 0.62f, 20f, 4);
        }

        /// <summary>WP-072 B2: a player casing landing - world, near, cheap, never piles past three.</summary>
        public static AudioPlacement PlayerCasing(AudioSoundId sound)
        {
            AudioPlacement p = Impact(sound, 0.5f, 12f, 3);
            p.MinRetrigger = 0.05f;
            p.PitchJitter = 0.05f;
            return p;
        }

        /// <summary>WP-048B: weapon-handling foley on the player's own weapon (WP-032C clip events) - dry, near, never positional.</summary>
        public static AudioPlacement WeaponFoley(AudioSoundId sound)
        {
            AudioPlacement p = Weapon(sound, 0.55f, 40f, 2, 0.03f);
            p.Spatial = 0.15f;
            p.PitchJitter = 0.03f;
            return p;
        }

        /// <summary>WP-048B: pickup grab / seat on the upper body (WP-032C).</summary>
        public static AudioPlacement Pickup(AudioSoundId sound)
        {
            AudioPlacement p = Weapon(sound, 0.6f, 45f, 1, 0.08f);
            p.Spatial = 0.15f;
            return p;
        }

        /// <summary>WP-021B (ADR-018-1 foot_l/foot_r): the player's own step - near, mostly dry, the cheapest player sound.</summary>
        public static AudioPlacement PlayerFootstep(AudioSoundId sound, bool running)
        {
            AudioPlacement p = Weapon(sound, running ? 0.42f : 0.32f, 24f, 2, 0.08f);
            p.Spatial = 0.3f;
            p.PitchJitter = 0.06f;
            return p;
        }

        /// <summary>WP-021B: a zombie's step - world, positional, priced under a flesh hit.</summary>
        public static AudioPlacement ZombieFootstep(AudioSoundId sound, EnemyTierWeight weight)
        {
            AudioPlacement p = Impact(sound, weight == EnemyTierWeight.Heavy ? 0.5f : 0.36f, 16f, 6);
            p.Spatial = 1f;
            p.MinRetrigger = 0.04f;
            p.PitchJitter = 0.07f;
            p.Pitch = weight == EnemyTierWeight.Heavy ? 0.92f : 1f;
            return p;
        }

        /// <summary>WP-021B (death_end): the body settling on the ground - the heavy step clip, low and quiet.</summary>
        public static AudioPlacement BodySettle(EnemyTierWeight weight)
        {
            AudioPlacement p = Impact(AudioSoundId.ZombieStepHeavy, weight == EnemyTierWeight.Heavy ? 0.6f : 0.45f, 30f, 3);
            p.Spatial = 1f;
            p.Pitch = 0.8f;
            p.PitchJitter = 0.05f;
            return p;
        }

        /// <summary>An ambience scatter one-shot. The lowest priority in the game, by design.</summary>
        /// <summary>WP-072 · DEC-102: an eave / leaf drip — drops come 0.9–2.6 s apart and may overlap (the scatter's 1.5 s / 1 voice would drop half).</summary>
        public static AudioPlacement AmbientDrip(AudioSoundId sound, float gain)
        {
            AudioPlacement p = AmbientOneShot(sound, gain);
            p.MaxInstances = 3;
            p.MinRetrigger = 0.25f;
            p.PitchJitter = 0.08f;
            return p;
        }

        public static AudioPlacement AmbientOneShot(AudioSoundId sound, float gain)
        {
            return new AudioPlacement
            {
                Sound = sound,
                Bus = AudioBus.Ambience,
                Gain = gain,
                Priority = 10f,
                Spatial = 0.8f,
                MaxInstances = 1,
                MinRetrigger = 1.5f,
                PitchJitter = 0.06f,
                Pitch = 1f,
                DuckAmbience = 1f,
                DuckCombat = 1f,
                DuckMusic = 1f,
                DuckSeconds = 0f
            };
        }

        /// <summary>WP-065C: fūrin tinkle at the chime (3D, Ambience bus: snapshot and threat-chip ducks apply), light pitch spread, one at a time.</summary>
        public static AudioPlacement WindChime(float gain, float pitch = 1f)
        {
            AudioPlacement p = AmbientOneShot(AudioSoundId.AmbFurin, gain);
            p.Spatial = 1f;
            p.Pitch = pitch;           // the chime's own size (±1 st by index)
            p.PitchJitter = 0.03f;     // ±0.5 st per play
            p.MinRetrigger = 1.2f;     // global cooldown across the 8 chimes
            p.MaxInstances = 2;
            return p;
        }

        // ---- priority arithmetic ----------------------------------------------

        /// <summary>
        /// Combines the design priority with the producer's.
        ///
        /// The plan is the authority and the cue is a nudge worth at most 12.5 points -
        /// a quarter of one rung on the ladder above. That is deliberate: a weapon that
        /// decides its own shots are worth 100 must not be able to evict an armour break,
        /// but a producer that has genuinely extra information (this explosion is the one
        /// that killed the ultra) should be able to win a tie.
        /// </summary>
        public static float EffectivePriority(in AudioPlacement placement, int cuePriority)
        {
            return placement.Priority + Mathf.Clamp(cuePriority, 0, 100) * 0.125f;
        }

        // ---- constructors ------------------------------------------------------

        private static AudioPlacement Weapon(AudioSoundId sound, float gain, float priority, int maxInstances, float minRetrigger)
        {
            return new AudioPlacement
            {
                Sound = sound,
                Bus = AudioBus.Weapon,
                Gain = gain,
                Priority = priority,
                Spatial = 0.45f,
                MaxInstances = maxInstances,
                MinRetrigger = minRetrigger,
                PitchJitter = 0.03f,
                Pitch = 1f,
                DuckAmbience = 1f,
                DuckCombat = 1f,
                DuckMusic = 1f,
                DuckSeconds = 0f
            };
        }

        private static AudioPlacement Impact(AudioSoundId sound, float gain = 0.62f, float priority = 20f, int maxInstances = 3)
        {
            return new AudioPlacement
            {
                Sound = sound,
                Bus = AudioBus.Impact,
                Gain = gain,
                Priority = priority,
                Spatial = 0.85f,
                MaxInstances = maxInstances,
                MinRetrigger = 0.02f,
                // Impacts get the widest pitch spread in the game. Four baked variations
                // times a live +/- 7% is what makes a shotgun into a stone wall stop
                // sounding like one sound played eight times.
                PitchJitter = 0.07f,
                Pitch = 1f,
                DuckAmbience = 1f,
                DuckCombat = 1f,
                DuckMusic = 1f,
                DuckSeconds = 0f
            };
        }

        private static AudioPlacement Target(AudioSoundId sound, float gain, float priority, int maxInstances, float minRetrigger)
        {
            return new AudioPlacement
            {
                Sound = sound,
                Bus = AudioBus.Target,
                Gain = gain,
                Priority = priority,
                Spatial = 0.8f,
                MaxInstances = maxInstances,
                MinRetrigger = minRetrigger,
                PitchJitter = 0.05f,
                Pitch = 1f,
                DuckAmbience = 1f,
                DuckCombat = 1f,
                DuckMusic = 1f,
                DuckSeconds = 0f
            };
        }

        private static AudioPlacement Interface(
            AudioSoundId sound, float gain, float priority, float spatial = 0f,
            int maxInstances = 1, float minRetrigger = 0.02f)
        {
            return new AudioPlacement
            {
                Sound = sound,
                Bus = AudioBus.Interface,
                Gain = gain,
                Priority = priority,
                Spatial = spatial,
                MaxInstances = maxInstances,
                MinRetrigger = minRetrigger,
                PitchJitter = 0.03f,   // WP-068 M2: ±3 % on every interface cue (a repeated click never sounds sampled)
                Pitch = 1f,
                DuckAmbience = 1f,
                DuckCombat = 1f,
                DuckMusic = 1f,
                DuckSeconds = 0f
            };
        }
    }

    /// <summary>
    /// Decides when an armour hit becomes an armour CRACK - REQ-129's second rung.
    ///
    /// The problem it solves: HitFeedbackDirector sends a constant intensity for every
    /// armour hit (0.6, derived from the tier index), so the cue itself carries no
    /// information about how close the plate is to failing. Rather than change a contract
    /// another task owns, this watches the cues arrive: three or more hits on the same
    /// plate inside two and a half seconds means that plate is going, and the third one
    /// onward plays the cracking variant.
    ///
    /// The result is that the request's four rungs are audible IN ORDER during a sustained
    /// burst - ring, ring, crackle, shear - which is more than "ArmorHit and ArmorBreak do
    /// not get confused"; it makes the break audibly earned.
    /// </summary>
    public sealed class ArmorFatigueTracker
    {
        private const int Capacity = 12;

        /// <summary>PROTOTYPE-ONLY / INVENTED: two hits this close are the same plate.</summary>
        private const float SamePlateRadius = 1.4f;

        /// <summary>PROTOTYPE-ONLY / INVENTED: how long a plate stays "recently hit".</summary>
        private const float MemorySeconds = 2.5f;

        /// <summary>PROTOTYPE-ONLY / INVENTED: hit number at which the plate starts to crack.</summary>
        private const int CrackAtHit = 3;

        private struct Plate
        {
            public Vector3 Position;
            public float LastTime;
            public int Hits;
        }

        private readonly Plate[] _plates = new Plate[Capacity];
        private int _head;

        /// <summary>Records an armour hit and reports whether the plate should sound like it is failing.</summary>
        public bool RegisterHit(Vector3 position)
        {
            float now = Time.unscaledTime;
            float sqrRadius = SamePlateRadius * SamePlateRadius;

            for (int i = 0; i < Capacity; i++)
            {
                if (_plates[i].Hits <= 0)
                {
                    continue;
                }

                if (now - _plates[i].LastTime > MemorySeconds)
                {
                    _plates[i].Hits = 0;
                    continue;
                }

                if ((_plates[i].Position - position).sqrMagnitude > sqrRadius)
                {
                    continue;
                }

                _plates[i].Hits++;
                _plates[i].LastTime = now;
                // Track the plate as it moves; a zombie walking while being shot is the
                // normal case, not the exception.
                _plates[i].Position = position;
                return _plates[i].Hits >= CrackAtHit;
            }

            _plates[_head] = new Plate { Position = position, LastTime = now, Hits = 1 };
            _head = (_head + 1) % Capacity;
            return false;
        }

        /// <summary>Called when a plate actually breaks, so the next plate starts from rung one.</summary>
        public void Forget(Vector3 position)
        {
            float sqrRadius = SamePlateRadius * SamePlateRadius;
            for (int i = 0; i < Capacity; i++)
            {
                if (_plates[i].Hits > 0 && (_plates[i].Position - position).sqrMagnitude <= sqrRadius)
                {
                    _plates[i].Hits = 0;
                }
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                _plates[i].Hits = 0;
            }

            _head = 0;
        }
    }
}
