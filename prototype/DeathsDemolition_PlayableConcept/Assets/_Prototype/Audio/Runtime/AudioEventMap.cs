using System;
using System.Collections.Generic;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;

namespace DD.Prototype.Audio
{
    /// <summary>Where a game event reaches the audio system from.</summary>
    public enum AudioEventSource
    {
        /// <summary><c>AudioEvents.Cue</c> (Feedback contract, weapons / damage / special weapon).</summary>
        Combat = 0,

        /// <summary><c>RunAudioEvents.Cue</c> (gold, rounds, upgrades).</summary>
        Run = 1,

        /// <summary><c>PlayerHealth.Any*</c>.</summary>
        Player = 2,

        /// <summary><c>DamageEvents.*</c> (zombie side: damaged / died / player attacked).</summary>
        Damage = 3,

        /// <summary>Polled by <see cref="AudioEventRouter"/> (no event exists: zombie attack windup).</summary>
        Polled = 4,

        /// <summary><c>PrototypeAudio.*</c> static calls from the UI kit and, since WP-021C, the WP-022B <c>UiEvents.Raised</c> contract (<c>UiEvent.&lt;name&gt;</c> rows).</summary>
        Ui = 5,

        /// <summary>Continuous: ambience beds driven by region + phase.</summary>
        Ambience = 6,

        /// <summary>Continuous: music layers driven by intensity + tier + phase.</summary>
        Music = 7,

        /// <summary>WP-018 (ADR-018-1) animation events, bound since WP-021B via <c>CharacterAnimationEvents.Any</c>.</summary>
        Animation = 8
    }

    /// <summary>One row of the map: a game event, the sound it becomes, where it sits in the mix.</summary>
    public readonly struct AudioEventBinding
    {
        public readonly AudioEventSource Source;
        public readonly string Event;
        public readonly AudioSoundId Sound;
        public readonly AudioBus Bus;
        public readonly bool Spatial;
        public readonly bool Reserved;
        public readonly string Note;

        public AudioEventBinding(AudioEventSource source, string evt, AudioSoundId sound, AudioBus bus, bool spatial, string note, bool reserved = false)
        {
            Source = source;
            Event = evt;
            Sound = sound;
            Bus = bus;
            Spatial = spatial;
            Note = note;
            Reserved = reserved;
        }
    }

    /// <summary>
    /// ADR-021-1 · THE EVENT MAP. Every gameplay event that makes a sound, in one table,
    /// with the sound it resolves to, the bus it lives on and whether it is positional.
    ///
    /// This table is documentation that the EditMode test keeps honest: it must name every
    /// member of <see cref="CombatAudioEvent"/> and <see cref="RunAudioEvent"/>, the four UI
    /// calls, the player events, the zombie vocal set, the six ambience beds and the four
    /// music layers. Runtime routing itself still lives in <see cref="AudioMixPlan"/> (the
    /// mix numbers) and <see cref="AudioEventRouter"/> (the new listeners); this is the
    /// contract the sound designer reads and the one WP-018/WP-019 extend.
    ///
    /// Reserved rows (<see cref="AudioEventBinding.Reserved"/>) exist so a future producer
    /// (WP-018 footsteps, attack windows) has a named slot rather than inventing one.
    /// </summary>
    public static class AudioEventMap
    {
        private const AudioBus W = AudioBus.Weapon;
        private const AudioBus I = AudioBus.Impact;
        private const AudioBus T = AudioBus.Target;
        private const AudioBus U = AudioBus.Interface;
        private const AudioBus A = AudioBus.Ambience;
        private const AudioBus M = AudioBus.Music;

        public static readonly AudioEventBinding[] Entries =
        {
            // ---- weapons (Combat) ------------------------------------------------
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ClassicWeaponFire), AudioSoundId.RifleFire, W, true, "intensity picks SMG/Rifle/Shotgun (AudioMixPlan.ClassicWeaponFor)"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ClassicWeaponDryFire), AudioSoundId.DryFire, W, false, "WP-072 B2: Foley{Light,Medium,Heavy}DryFire by the equipped weapon family when produced; DryFire is the fallback"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ReloadStart), AudioSoundId.ReloadStart, W, false, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ReloadInsert), AudioSoundId.ReloadInsert, W, false, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ReloadEnd), AudioSoundId.ReloadEnd, W, false, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.HeavyWeaponSpinUp), AudioSoundId.HeavyMotorLoop, W, false, "loop; pitch ramp is the spin-up"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.HeavyWeaponFire), AudioSoundId.HeavyFire, W, true, "one per shot (WP-007 KS)"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.HeavyWeaponSpinDown), AudioSoundId.HeavySpinDown, W, false, "object sound; plays on cancel too (WP-007 K-7)"),

            // ---- surface impacts (Combat, S-07 SurfaceKind table) -----------------
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SurfaceImpactDefault), AudioSoundId.ImpactDefault, I, true, "SurfaceKind.Default"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SurfaceImpactStone), AudioSoundId.ImpactStone, I, true, "SurfaceKind.Stone · AP-011"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SurfaceImpactWood), AudioSoundId.ImpactWood, I, true, "SurfaceKind.Wood · AP-011"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SurfaceImpactFoliage), AudioSoundId.ImpactFoliage, I, true, "SurfaceKind.Foliage"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SurfaceImpactWater), AudioSoundId.ImpactWater, I, true, "SurfaceKind.Water · AP-011"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SurfaceImpactMetal), AudioSoundId.ImpactMetal, I, true, "SurfaceKind.Metal · AP-011"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SurfaceImpactDirt), AudioSoundId.ImpactDirt, I, true, "SurfaceKind.Dirt · AP-011"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SurfaceImpactPaper), AudioSoundId.ImpactPaper, I, true, "SurfaceKind.Paper"),

            // ---- target (Combat) --------------------------------------------------
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.FleshHit), AudioSoundId.FleshHit, T, true, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.CriticalHit), AudioSoundId.CriticalHit, T, true, "bare head, direct only (WP-006 K-7)"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ArmorHit), AudioSoundId.ArmorImpact, T, true, "ArmorCrack when the plate is failing (REQ-129)"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ArmorBreak), AudioSoundId.ArmorBreak, T, true, "one per hit (tier); AP-011"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.EnemyDeath), AudioSoundId.EnemyDeath, T, true, "body/impact layer; vocal layer = DamageEvents.EnemyDied"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.HeavyEnemyDeath), AudioSoundId.HeavyEnemyDeath, T, true, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.Ricochet), AudioSoundId.Ricochet, I, true, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ChainArc), AudioSoundId.ChainArc, I, true, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.Explosion), AudioSoundId.Explosion, T, true, ""),

            // ---- special weapon (Combat) — spawn sound is RESERVED to this event ---
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SpecialWeaponSpawn), AudioSoundId.SwSpawnGong, U, true, "REQ-053 reserved cue · AP-011 gong"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SpecialWeaponBeaconLoop), AudioSoundId.SwBeaconLoop, U, true, "loop; shared id with Tide well (WP-007 KS-12)"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SpecialWeaponPickup), AudioSoundId.SwPickup, U, false, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SpecialWeaponFinalSeconds), AudioSoundId.SwFinalSeconds, U, false, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SpecialWeaponExpired), AudioSoundId.SwExpired, U, false, "not on cancel (WP-007 K-7)"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.SpecialWeaponGroundExpired), AudioSoundId.SwGroundExpired, U, true, ""),

            // ---- Special Weapon USE per weapon (WP-037 SpecialWeaponUsage table → these rows; data-only mapping)
            B(AudioEventSource.Combat, "SpecialWeaponUsage.KaguraBell.FireCue/ActiveLoop", AudioSoundId.HeavyFire, W, true, "→ HeavyWeaponFire / HeavyWeaponSpinUp rows (WP-037 table; per-weapon voice = AP-011 v4 queue)"),
            B(AudioEventSource.Combat, "SpecialWeaponUsage.OfudaLoom.FireCue/ActiveLoop", AudioSoundId.HeavyFire, W, true, "→ HeavyWeaponFire / HeavyWeaponSpinUp rows"),
            B(AudioEventSource.Combat, "SpecialWeaponUsage.TideJewel.FireCue/ActiveLoop", AudioSoundId.HeavyFire, W, true, "→ HeavyWeaponFire / HeavyWeaponSpinUp rows"),

            // ---- WP-072 B4 · DEC-094 Specials (S-02 WP-069 B4 SpecialUsageEvent → SpecialAudio, AP-011 v019)
            B(AudioEventSource.Combat, "SpecialUsage.Activated → Raiju / Namekuji", AudioSoundId.SwArcReady, W, false, "ready tone: coil fills / chamber pressurises + rin (brass, Specials only); SwGelReady for Namekuji"),
            B(AudioEventSource.Combat, "SpecialUsage.ChargeStarted → Raiju", AudioSoundId.SwArcCharge, W, false, "0.25 s charge: rising coil + glass (one-shot, −2 dB); ChargeCancelled lets it end"),
            B(AudioEventSource.Combat, "SpecialUsage.Fired → Raiju / Namekuji", AudioSoundId.SwArcFire, W, false, "discharge + glass + air / copper hiss + plop (SwGelFire); the legacy HeavyWeaponFire gives way while a Special is in hand"),
            B(AudioEventSource.Combat, "SpecialUsage.Hit → Raiju (Count = hop)", AudioSoundId.SwArcChainJump, T, true, "one crackle per hop, +1 st per hop, 40 ms apart; hops ≥ 6 cluster"),
            B(AudioEventSource.Combat, "SpecialUsage.Hit → Namekuji", AudioSoundId.SwGelHitFlesh, T, true, "direct hit on a body (always flesh; the Specials ignore armour)"),
            B(AudioEventSource.Combat, "SpecialUsage.FieldSpawned → Namekuji (Count = puddle id)", AudioSoundId.SwGelPuddleLoop, I, true, "splat (SwGelHit) + spread (SwGelPuddleCreate) + bubbling loop per puddle, ≤ 2 loops, −4 dB; Raiju's FieldSpawned = the orb flight (ProjectileFlightAudio)"),
            B(AudioEventSource.Combat, "SpecialUsage.FieldEnded → Raiju / Namekuji", AudioSoundId.SwGelPuddleExpire, I, true, "Namekuji: puddle loop out + drying; Raiju: fizzle (SwArcShockEnd) only when no chain hop spoke in the last 0.15 s"),
            B(AudioEventSource.Combat, "SpecialUsage.SelfShock → Raiju", AudioSoundId.SwArcChainJump, T, false, "the player's own zap (−3 st, 2D)"),
            B(AudioEventSource.Combat, "SpecialUsage.ReloadStarted / ReloadEnded → Raiju / Namekuji", AudioSoundId.SwArcCellOut, W, false, "cell out / in (SwArcCellIn), canister out / in (SwGelCanister*)"),
            B(AudioEventSource.Combat, "SpecialUsage.PlayerSlideStarted → Namekuji", AudioSoundId.SwGelSlidePlayer, W, false, "the player slips: slide squelch + cloth"),
            B(AudioEventSource.Combat, "SpecialUsage.Deactivated / ChargeCancelled / Liquefied / PlayerSlideEnded", AudioSoundId.None, W, false, "covered: the weapon lowers with its own foley; the hit already spoke", true),

            // ---- confirmation (Combat) -------------------------------------------
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.HitMarker), AudioSoundId.HitMarker, U, false, ""),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.KillMarker), AudioSoundId.KillMarker, U, false, ""),

            // ---- run flow (Run) ---------------------------------------------------
            B(AudioEventSource.Run, nameof(RunAudioEvent.GoldSpawned), AudioSoundId.GoldSpawn, U, true, "AP-011 · coin drop"),
            B(AudioEventSource.Run, nameof(RunAudioEvent.GoldCollected), AudioSoundId.GoldPickup, U, false, "streak transposed (hirajoshi); AP-011 · magnet streak ≥ 2 uses GoldMagnet layer"),
            B(AudioEventSource.Run, nameof(RunAudioEvent.RoundPoolExhausted), AudioSoundId.RoundComplete, U, false, "soft variant"),
            B(AudioEventSource.Run, nameof(RunAudioEvent.RoundComplete), AudioSoundId.RoundComplete, U, false, "AP-011"),
            B(AudioEventSource.Run, nameof(RunAudioEvent.CountdownTick), AudioSoundId.RoundCountdownTick, U, false, ""),
            B(AudioEventSource.Run, nameof(RunAudioEvent.RoundStart), AudioSoundId.RoundStart, U, false, "AP-011"),
            B(AudioEventSource.Run, nameof(RunAudioEvent.UpgradePurchased), AudioSoundId.UpgradePurchase, U, false, ""),
            B(AudioEventSource.Run, nameof(RunAudioEvent.UpgradeRefused), AudioSoundId.UiDenied, U, false, ""),
            B(AudioEventSource.Run, nameof(RunAudioEvent.UpgradeSkipped), AudioSoundId.UiBack, U, false, ""),
            B(AudioEventSource.Run, nameof(RunAudioEvent.RunEnded), AudioSoundId.PlayerDeath, U, false, "stinger; music pulls back"),

            // ---- player (Player) --------------------------------------------------
            B(AudioEventSource.Player, "PlayerHealth.AnyDamaged", AudioSoundId.PlayerDamage, U, false, "severity + low-health gain"),
            B(AudioEventSource.Player, "PlayerHealth.AnyPlayerDied", AudioSoundId.PlayerDeath, U, false, ""),

            // ---- zombie vocal set — DEC-067; since WP-048 on the WP-040 beat surface (EnemyCombatEvents, ADR-040-1)
            B(AudioEventSource.Damage, "EnemyCombatEvents.telegraph_start", AudioSoundId.ZombieWindupStandard, T, true, "DEC-067: ≥ 0.3 s before the swing, positional (telegraph 0.55 s); Special/Elite/Ultra ids by tier (was the WP-021 AttackPhase poll)"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.attack_commit", AudioSoundId.ZombieAttackStandard, T, true, "the swing (hit window opens); Special/Elite/Ultra ids by tier"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.attack_miss", AudioSoundId.None, T, true, "the swing already sounded at commit; a whiff is the silence after it", true),
            B(AudioEventSource.Damage, "DamageEvents.EnemyDamaged", AudioSoundId.ZombieHitStandard, T, true, "pain grunt, throttled per enemy; Special/Elite/Ultra ids by tier (no beat row exists for a plain hit)"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.death_<tier>", AudioSoundId.ZombieDeathStandard, T, true, "vocal layer over CombatAudioEvent.EnemyDeath; Special/Elite/Ultra ids by tier (was DamageEvents.EnemyDied)"),
            B(AudioEventSource.Damage, "DamageEvents.PlayerAttacked", AudioSoundId.PlayerDamage, U, false, "same cue as AnyDamaged (single owner: PlayerHealth); listed for completeness"),
            B(AudioEventSource.Damage, "DamageEvents.ArmorBroken", AudioSoundId.ArmorBreak, T, true, "no second cue: the tier cue in ReportEnemyHit owns it (WP-006 N1)"),

            // ---- UI (Ui) ------------------------------------------------------------
            B(AudioEventSource.Ui, "PrototypeAudio.Navigate", AudioSoundId.UiNavigate, U, false, "AP-011"),
            B(AudioEventSource.Ui, "PrototypeAudio.Confirm", AudioSoundId.UiConfirm, U, false, "AP-011"),
            B(AudioEventSource.Ui, "PrototypeAudio.Back", AudioSoundId.UiBack, U, false, "AP-011"),
            B(AudioEventSource.Ui, "PrototypeAudio.Denied", AudioSoundId.UiDenied, U, false, "AP-011"),
            B(AudioEventSource.Ui, "PrototypeAudio.Reveal", AudioSoundId.UiReveal, U, false, "WP-021C: UiSound.Reveal now reaches the library (was a local synth)"),

            // ---- ambience (continuous) — DEC-066 / DEC-068 ---------------------------
            B(AudioEventSource.Ambience, "Region.common", AudioSoundId.AmbCommon, A, false, "after-rain drip, wind, bamboo, water — always on"),
            B(AudioEventSource.Ambience, "Region.center", AudioSoundId.AmbRegionCenter, A, false, "crossfade ≥ 2 s (AudioRegionMap)"),
            B(AudioEventSource.Ambience, "Region.north", AudioSoundId.AmbRegionNorth, A, false, ""),
            B(AudioEventSource.Ambience, "Region.east", AudioSoundId.AmbRegionEast, A, false, ""),
            B(AudioEventSource.Ambience, "Region.south", AudioSoundId.AmbRegionSouth, A, false, ""),
            B(AudioEventSource.Ambience, "Region.west", AudioSoundId.AmbRegionWest, A, false, ""),

            // ---- music (continuous) — DEC-064 / DEC-065 ------------------------------
            B(AudioEventSource.Music, "Music.base", AudioSoundId.MusicBase, M, false, "always; pulled back between rounds"),
            B(AudioEventSource.Music, "Music.percussion", AudioSoundId.MusicPercussion, M, false, "MusicIntensity ≥ 0.15"),
            B(AudioEventSource.Music, "Music.tension", AudioSoundId.MusicTension, M, false, "MusicIntensity ≥ 0.45"),
            B(AudioEventSource.Music, "Music.accent", AudioSoundId.MusicAccent, M, false, "Elite/Ultra alive or Special Weapon active"),
            // WP-057 · music v2: phase loops and stingers (AP-011 v008, derived from the stems, 0 cr); Music software bus
            B(AudioEventSource.Music, "Music.menu", AudioSoundId.MusicMenu, AudioBus.Music, false, "main menu / selection loop; cross-fades against the run layers (RunPhase.None, not after a run)"),
            B(AudioEventSource.Music, "Music.results", AudioSoundId.MusicResults, AudioBus.Music, false, "results loop after RunEnded / death until the next RunStarted"),
            B(AudioEventSource.Music, "Stinger.round_break", AudioSoundId.StingerRoundBreak, AudioBus.Music, false, "RunEvents.RoundCompleted: taiko + bell phrase over the layers (layers duck to 0.55, release 1.5 s)"),
            B(AudioEventSource.Music, "Stinger.special_pickup", AudioSoundId.StingerSpecialPickup, AudioBus.Music, false, "CombatAudioEvent.SpecialWeaponPickup while fighting"),
            B(AudioEventSource.Music, "Stinger.death", AudioSoundId.StingerDeath, AudioBus.Music, false, "PlayerHealth.AnyPlayerDied → results state"),
            B(AudioEventSource.Music, "Stinger.run_end", AudioSoundId.StingerRunEnd, AudioBus.Music, false, "RunAudioEvent.RunEnded without a death (one stinger per run end)"),

            // ---- v003 · coverage matrix rows (EVENT-COVERAGE-MATRIX.md) --------------
            B(AudioEventSource.Damage, "EnemyCombatEvents.attack_hit", AudioSoundId.AttackImpactBody, T, true, "the claw landing (3D, from the zombie); PlayerDamage is the 2D grunt (was DamageEvents.PlayerAttacked)"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.stagger", AudioSoundId.ZombieStagger, T, true, "heavy stagger grunt + stumble (REQ-124 window and the armour-break stagger; was EnemyDamaged.CausedStagger)"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.armor_break_<region>+0.42s", AudioSoundId.ArmorPieceDrop, I, true, "plate landing after the break cue (was DamageEvents.ArmorBroken)"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.spawn_rise", AudioSoundId.ZombieRise, T, true, "spawn / rise from the ground (DEC-081: only bodies that rise; was the first-live-frame poll)"),
            B(AudioEventSource.Polled, "ZombieEnemy idle 6–14 s ≤ 9 m", AudioSoundId.ZombieIdle, T, true, "near-range breath/growl, AttackPhase None only; Special/Elite/Ultra idle ids by tier (WP-021C)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke", AudioSoundId.BreakCeramic, I, true, "AP-003B small_breakable (ceramic)"),
            B(AudioEventSource.Ambience, "scatter.drip", AudioSoundId.AmbDrip, A, true, "after-rain single drops in the scatter ring"),
            B(AudioEventSource.Run, "RunEvents.RunStarted / Phase→None", AudioSoundId.UiTransition, U, false, "screen transition, same frame as the mixer snapshot"),

            // ---- WP-018 (ADR-018-1) animation events — bound in WP-021B via CharacterAnimationEvents.Any
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → player, surface stone", AudioSoundId.FootstepStone, W, true, "down ray → SurfaceResolver; untagged (AP-005 paving) reads as stone"),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → player, surface wood", AudioSoundId.FootstepWood, W, true, "veranda / bridge planks"),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → player, surface dirt", AudioSoundId.FootstepDirt, W, true, "earth / gravel"),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → player, surface foliage", AudioSoundId.FootstepFoliage, W, true, "grass / shrubs"),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → player, surface water", AudioSoundId.FootstepWater, W, true, "shore / shallow water"),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → zombie, Standard/Special", AudioSoundId.ZombieStepLight, I, true, "fit A/light tiers"),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → zombie, Elite/Ultra", AudioSoundId.ZombieStepHeavy, I, true, "heavy tiers (−1 st)"),
            B(AudioEventSource.Animation, "anim.breath", AudioSoundId.ZombieIdle, T, true, "re-arms the polled idle timer (no double)"),
            B(AudioEventSource.Animation, "anim.death_end", AudioSoundId.ZombieStepHeavy, I, true, "body settle layer (pitch 0.8)"),
            B(AudioEventSource.Animation, "anim.hit_window_start", AudioSoundId.ZombieAttackStandard, T, true, "covered by EnemyCombatEvents.attack_commit (same frame); no second cue", true),
            B(AudioEventSource.Animation, "anim.rise_end", AudioSoundId.ZombieAggroStandard, T, true, "covered by EnemyCombatEvents.spawn_land (WP-048): the aggro growl also reaches bodies that spawn without a rise clip", true),
            B(AudioEventSource.Damage, "EnemyCombatEvents.spawn_land", AudioSoundId.ZombieAggroStandard, T, true, "the body is up and has seen you - the aggro growl, ids by tier (ZombieRise is the earth, this is the voice)"),
            // WP-072 B4 · WP-076 Special beats (the body's side)
            B(AudioEventSource.Damage, "EnemyCombatEvents.sw_shock", AudioSoundId.SwArcShockLoop, T, true, "per-body crackle loop for the Electrocuted hold (≤ 4 bodies, random phase), SwArcShockEnd at its end"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.sw_slide / sw_fall", AudioSoundId.SwGelSlideEnemy, T, true, "slide squelch + body fall (60 ms crowd clustering); sw_fall = BodySettle by tier"),
            B(AudioEventSource.Animation, "anim.fire / anim.pickup / anim.stagger_end", AudioSoundId.None, T, false, "covered by weapon / Special / stagger cues from their own events", true),
            B(AudioEventSource.Animation, "anim.hit_window_end / anim.hit", AudioSoundId.None, T, false, "silent by design: the window closing has no sound; anim.hit is the enemy's own hit reaction frame (the grunt is DamageEvents.EnemyDamaged)", true),
            B(AudioEventSource.Animation, "anim.shift_l/shift_r", AudioSoundId.FootstepStone, W, true, "WP-032B idle weight shift: a footstep at half gain on the surface under the character (player or zombie step set)"),
            B(AudioEventSource.Animation, "anim.armor_break / anim.spawn_rise / anim.spawn_land (clip)", AudioSoundId.None, T, false, "covered by the EnemyCombatEvents beats of the same names (one owner)", true),
            // ---- WP-071 event contract v3 (S-06 1980e0f, 76 names). Plants carry steps now; foley / body rows wait for WP-072 B2–B4.
            B(AudioEventSource.Animation, "anim.pivot_plant/stop_plant", AudioSoundId.FootstepStone, W, true, "WP-071 turn / stop plant: a footstep at 0.8 gain on the surface under the character (player or zombie step set)"),
            B(AudioEventSource.Animation, "anim.weight_shift", AudioSoundId.FootstepStone, W, true, "WP-071 idle weight shift (side = int payload): half-gain step, same rule as shift_l/shift_r"),
            B(AudioEventSource.Animation, "anim.foot_l_off/foot_r_off", AudioSoundId.None, T, false, "silent by design: the toe-off has no sound, the plant (foot_l/foot_r) carries the step", true),
            B(AudioEventSource.Animation, "anim.attack_step / anim.attack_recover / anim.telegraph_start / anim.grab_start / anim.grab_end / anim.roar", AudioSoundId.None, T, false, "covered by EnemyCombatEvents (windup / commit / grab / aggro voice); WP-076 grab foley in WP-072 B3", true),
            B(AudioEventSource.Animation, "anim.ragdoll_handoff / anim.fall_start / anim.ground_impact / anim.knockback_end / anim.hurt_end", AudioSoundId.None, T, false, "body falls: ground_impact → BodySettle by part in WP-072 B3 (ragdoll proxy settle); the others are timing marks", true),
            B(AudioEventSource.Animation, "anim.blood_burst / anim.twitch / anim.spasm", AudioSoundId.None, T, false, "WP-072 B3 flesh layer (Target bus); silent until then", true),
            B(AudioEventSource.Animation, "anim.charge / anim.box_open/belt_seat/box_close / anim.bolt_up/bolt_back/bolt_fwd/bolt_down / anim.shell_in → equipped weapon", AudioSoundId.FoleyAr1Charge, W, false, "WP-072 B2 per-weapon mechanism foley (WeaponFoleyV3.Mechanism, AP-011 v017, 35 ids); mag_out/mag_in/chamber/eject also route here when v017 is produced (A/B/C rows are the fallback)"),
            B(AudioEventSource.Animation, "anim.mechanism_start/mechanism_end / anim.shell_eject", AudioSoundId.None, W, false, "timing marks: the action sound rides the named event; the casing lands via WeaponEvents.Mechanism (CasingAudio)", true),
            B(AudioEventSource.Combat, "ProjectileEvents.Spawned (Rocket / Orb / Gel) → flight loop until Impact.Stopped / Expired (ProjectileId)", AudioSoundId.RocketFlightLoop, I, true, "WP-072 B5 ProjectileFlightAudio: RocketFlightLoop / SwArcSphereLoop / SwGelFlightLoop, ≤ 4 voices (≤ 2 rockets), rocket −2 st over 0→45 m in FP/TP"),
            B(AudioEventSource.Combat, "Explosion → ExplosionNear / Mid / Far by listener distance (12 / 35 m)", AudioSoundId.ExplosionNear, T, true, "WP-072 B5 (STANDARDS §7): one layer picked at the blast; the Explosion duck is kept"),
            B(AudioEventSource.Combat, "ProjectileEvents.Impact (world) → ImpactV3{Surface}{Light|Medium|Heavy}", AudioSoundId.ImpactV3StoneMedium, I, true, "WP-072 B3 ImpactAudio: calibre voice replaces the same hit's legacy SurfaceImpact cue (held one frame while projectiles live); pellets of one shot = one impact; grazing light/medium on stone/metal + Ricochet −8 dB"),
            B(AudioEventSource.Combat, "WeaponEvents.Mechanism CasingEjected → landing (surface under the port)", AudioSoundId.CasingRifleStone, I, true, "WP-072 B2 CasingAudio: Rifle/Pistol/Shell × Stone/Wood/Dirt (+ CasingLink for LMG), fall 0.25–0.6 s, water/foliage silent; top-down −18 / TP −12 / FP −8 dB"),
            B(AudioEventSource.Animation, "anim.rocket_in/rocket_seat → RL1", AudioSoundId.FoleyRl1RocketIn, W, false, "WP-072 B2 launcher load (tube slide + seat clack)"),
            B(AudioEventSource.Animation, "anim.cell_out/cell_in / anim.canister_out/canister_in / anim.cover_open/cover_close / anim.sw_charge_peak / anim.spin_up/spin_down / anim.pump_down/pump_up", AudioSoundId.None, W, false, "WP-072 B2 B4 Special sets (SW_ARC coil / SW_GEL canister + lever pump)", true),
            B(AudioEventSource.Animation, "anim.ads_ready → equipped weapon family", AudioSoundId.FoleyMediumAds, W, false, "WP-072 B2 cloth rub + stock-to-shoulder click, Light/Medium/Heavy family at the weapon's pitch"),
            B(AudioEventSource.Animation, "anim.lh_release/lh_grab / anim.reload_commit / anim.reload_cancel_point / anim.inspect_peak", AudioSoundId.None, W, false, "hand-contact / timing marks: silent by design", true),

            // ---- WP-048 · Audio Wave 3 — AP-011 v007 identities. Rows marked reserved wait for their producer on main
            // (WP-032C clip events, WP-040 EnemyCombatEvents, AP-017 breakable families, AP-015P plates); Look B ambience is live.
            B(AudioEventSource.Animation, "anim.eject → weapon A", AudioSoundId.WeaponAEject, W, false, "WP-032C clip event on the player rig, A Rifle foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.chamber → weapon A", AudioSoundId.WeaponAChamber, W, false, "WP-032C clip event on the player rig, A Rifle foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.mag_out → weapon A", AudioSoundId.WeaponAMagOut, W, false, "WP-032C clip event on the player rig, A Rifle foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.mag_in/mag_in_2/mag_in_3 → weapon A", AudioSoundId.WeaponAMagIn, W, false, "WP-032C clip event, A Rifle foley (v007); bound when WP-032C lands", true),
            B(AudioEventSource.Animation, "anim.switch_ready/switch_away → weapon A", AudioSoundId.WeaponASwitch, W, false, "WP-032C clip event on the player rig, A Rifle foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.lowered → weapon A", AudioSoundId.WeaponALowered, W, false, "WP-032C clip event on the player rig, A Rifle foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.raised → weapon A", AudioSoundId.WeaponARaised, W, false, "WP-032C clip event on the player rig, A Rifle foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.eject → weapon B", AudioSoundId.WeaponBEject, W, false, "WP-032C clip event on the player rig, B Carbine foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.chamber → weapon B", AudioSoundId.WeaponBChamber, W, false, "WP-032C clip event on the player rig, B Carbine foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.mag_out → weapon B", AudioSoundId.WeaponBMagOut, W, false, "WP-032C clip event on the player rig, B Carbine foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.mag_in/mag_in_2/mag_in_3 → weapon B", AudioSoundId.WeaponBMagIn, W, false, "WP-032C clip event, B Carbine foley (v007); bound when WP-032C lands", true),
            B(AudioEventSource.Animation, "anim.switch_ready/switch_away → weapon B", AudioSoundId.WeaponBSwitch, W, false, "WP-032C clip event on the player rig, B Carbine foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.lowered → weapon B", AudioSoundId.WeaponBLowered, W, false, "WP-032C clip event on the player rig, B Carbine foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.raised → weapon B", AudioSoundId.WeaponBRaised, W, false, "WP-032C clip event on the player rig, B Carbine foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.eject → weapon C", AudioSoundId.WeaponCEject, W, false, "WP-032C clip event on the player rig, C Shotgun foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.chamber → weapon C", AudioSoundId.WeaponCChamber, W, false, "WP-032C clip event on the player rig, C Shotgun foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.mag_out → weapon C", AudioSoundId.WeaponCMagOut, W, false, "WP-032C clip event on the player rig, C Shotgun foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.mag_in/mag_in_2/mag_in_3 → weapon C", AudioSoundId.WeaponCMagIn, W, false, "WP-032C clip event, C Shotgun foley (v007); bound when WP-032C lands", true),
            B(AudioEventSource.Animation, "anim.switch_ready/switch_away → weapon C", AudioSoundId.WeaponCSwitch, W, false, "WP-032C clip event on the player rig, C Shotgun foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.lowered → weapon C", AudioSoundId.WeaponCLowered, W, false, "WP-032C clip event on the player rig, C Shotgun foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.raised → weapon C", AudioSoundId.WeaponCRaised, W, false, "WP-032C clip event on the player rig, C Shotgun foley (v007); weapon = WeaponRegistry.SelectedWeapon (WP-048B)"),
            B(AudioEventSource.Animation, "anim.pickup_grab", AudioSoundId.PickupGrab, W, false, "WP-032C: hand closes on the weapon (v007; WP-048B bound)"),
            B(AudioEventSource.Animation, "anim.pickup_done", AudioSoundId.PickupDone, W, false, "WP-032C: weapon seats (v007; WP-048B bound); the reward tail stays SwPickup / RunAudioEvent"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.armor_break_head", AudioSoundId.ArmorBreakHelmet, T, true, "WP-040 region voice (v007) over the tier ArmorBreak cue (ReportEnemyHit, WP-006 N1)"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.armor_break_torso", AudioSoundId.ArmorBreakBody, T, true, "WP-040 region voice (v007)"),
            B(AudioEventSource.Damage, "EnemyCombatEvents.armor_break_leg", AudioSoundId.ArmorBreakLeg, T, true, "WP-040 region voice (v007)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke · EventId BreakStone", AudioSoundId.BreakLanternStone, I, true, "WP-038B data key (AP-017 event table v001) → AudioEventRouter.BreakSoundForEvent; WP-060"),
            B(AudioEventSource.Combat, "BreakableProp.Broke · EventId BreakCeramic", AudioSoundId.BreakCeramic, I, true, "WP-038B data key; AP-017 planter refines to BreakPlanter by name, AP-003B small_breakable stays ceramic"),
            B(AudioEventSource.Combat, "BreakableProp.Broke · EventId BreakWood", AudioSoundId.BreakBench, I, true, "WP-038B data key; sign refines to BreakSign by name"),
            B(AudioEventSource.Combat, "BreakableProp.Broke · EventId BreakBamboo", AudioSoundId.BreakFence, I, true, "WP-038B data key (fence_panel)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke · EventId BreakPaper", AudioSoundId.BreakLanternPaper, I, true, "WP-038B data key; shoji refines to BreakShoji by name"),
            B(AudioEventSource.Combat, "BreakableProp.Broke · EventId BreakStraw", AudioSoundId.BreakBale, I, true, "WP-038B data key (strawbale)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke → lantern_stone", AudioSoundId.BreakLanternStone, I, true, "AP-017 BreakStone (v007); name refinement inside the event (AudioEventRouter.BreakSoundForEvent)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke → lantern_paper", AudioSoundId.BreakLanternPaper, I, true, "AP-017 BreakPaper (v007)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke → planter", AudioSoundId.BreakPlanter, I, true, "AP-017 BreakCeramic (v007)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke → fence", AudioSoundId.BreakFence, I, true, "AP-017 BreakBamboo (v007)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke → bale", AudioSoundId.BreakBale, I, true, "AP-017 BreakStraw (v007)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke → bench", AudioSoundId.BreakBench, I, true, "AP-017 BreakWood (v007)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke → sign", AudioSoundId.BreakSign, I, true, "AP-017 BreakWood (v007)"),
            B(AudioEventSource.Combat, "BreakableProp.Broke → shoji", AudioSoundId.BreakShoji, I, true, "AP-017 BreakPaper (v007)"),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → player, plate water_shallow", AudioSoundId.FootstepWaterShallow, W, true, "AP-015P water surface (WP-038) → Ap005SurfaceTags.FootstepVariantFor (WP-058B)"),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → player, plate water_deep", AudioSoundId.FootstepWaterDeep, W, true, "waits for a wading-depth signal (the pond basin is not walkable; the surface reads shallow)", true),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → player, plate gravel", AudioSoundId.FootstepGravel, W, true, "AP015_ground_center plate = the gravel courtyard (WP-058B)"),
            B(AudioEventSource.Animation, "anim.foot_l/foot_r → player, plate pavement", AudioSoundId.FootstepPavement, W, true, "AP015_paving_* plates = cut stone (WP-058B)"),
            B(AudioEventSource.Ambience, "scatter.thunder", AudioSoundId.AmbThunderDistant, A, true, "Look B: distant thunder every 40-90 s, far and low (v007)"),
            B(AudioEventSource.Ambience, "scatter.drip_slate", AudioSoundId.AmbDripSlate, A, true, "Look B: wet-slate drips in the scatter ring (v007)"),
            B(AudioEventSource.Ambience, "Pond.bed", AudioSoundId.AmbPondBed, A, true, "Look B: koi pond water bed at the pond (3D loop, distance-faded) (v007)"),
            B(AudioEventSource.Ambience, "WindChime.furin", AudioSoundId.AmbFurin, A, true, "WP-065C: AP-017 fūrin (eaves) ring with WorldWind.Strength, nearest chime ≤ 12 m, AmbienceDirector.TickFurin (v014)"),

            // ---- WP-021C · WP-022B UI events (EVENT-COVERAGE-MATRIX §UI) — one row per UiEvent member;
            // reserved = the same frame already plays the sound through another row (single owner, no double).
            B(AudioEventSource.Ui, "UiEvent.FocusMoved", AudioSoundId.UiNavigate, U, false, "same frame as UiSound.Move → PrototypeAudio.Navigate (owner)", true),
            B(AudioEventSource.Ui, "UiEvent.ButtonHover", AudioSoundId.UiHover, U, false, "pointer over a control; lighter and shorter than Navigate (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.ButtonPress", AudioSoundId.UiConfirm, U, false, "same frame as Confirm (owner: UiSound.Confirm)", true),
            B(AudioEventSource.Ui, "UiEvent.Confirm", AudioSoundId.UiConfirm, U, false, "owner: UiSound.Confirm → PrototypeAudio.Confirm", true),
            B(AudioEventSource.Ui, "UiEvent.Back", AudioSoundId.UiBack, U, false, "owner: UiSound.Back → PrototypeAudio.Back", true),
            B(AudioEventSource.Ui, "UiEvent.Denied", AudioSoundId.UiDenied, U, false, "owner: UiSound.Denied → PrototypeAudio.Denied", true),
            B(AudioEventSource.Ui, "UiEvent.ScreenEntered", AudioSoundId.UiTransition, U, false, "screen transition (detail = screen); the RunStarted / Phase→None cue is the same id and MinRetrigger 0.3 s folds a same-frame double"),
            B(AudioEventSource.Ui, "UiEvent.ScreenLeft", AudioSoundId.None, U, false, "silent by design: the next ScreenEntered carries the transition", true),
            B(AudioEventSource.Ui, "UiEvent.PaneOpened", AudioSoundId.UiPaneOpen, U, false, "main-menu pane: paper slide (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.ChoiceSelected", AudioSoundId.UiSelect, U, false, "skin / weapon card: wood 'tok' (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.SliderChanged", AudioSoundId.UiSliderTick, U, false, "one tick per step (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.ToggleChanged", AudioSoundId.UiToggle, U, false, "two-tone mechanical (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.DialogOpened", AudioSoundId.UiDialogOpen, U, false, "quit-run confirm: short low accent (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.DialogClosed", AudioSoundId.UiBack, U, false, "owner: UiSound.Back (PauseScreen.CloseConfirm)", true),
            // ---- WP-068 slice 11 · UI v3 contract (AP-011 v015 material set)
            B(AudioEventSource.Ui, "UiEvent.ScreenExiting", AudioSoundId.UiTransitionOut, U, false, "screen exit (Exit 140): short paper draw, the entry's reverse (v015)"),
            B(AudioEventSource.Ui, "UiEvent.CurtainClosed", AudioSoundId.UiCurtainClose, U, false, "fusuma slide + stop, ≥ 1 frame before LoadScene; the tail rides the freeze (director is DontDestroyOnLoad) (v015)"),
            B(AudioEventSource.Ui, "UiEvent.CurtainOpened", AudioSoundId.UiCurtainOpen, U, false, "fusuma opening slide on the new scene's first frame (v015)"),
            B(AudioEventSource.Ui, "UiEvent.PressDown", AudioSoundId.UiPressDown, U, false, "lacquer tick on pointer-down (Primary/Secondary); Confirm follows on release (v015)"),
            B(AudioEventSource.Ui, "UiEvent.BootLogo", AudioSoundId.UiBootSeal, U, false, "U7: ink spread → seal tok → one rin strike, every launch; on the controllable UI voice (skip = 120 ms fade) (v015)"),
            B(AudioEventSource.Ui, "UiEvent.TimelineSkipped", AudioSoundId.None, U, false, "control, not a sound: fades the UI loops (roll / hold) in 30 ms and the boot seal in 120 ms", true),
            B(AudioEventSource.Ui, "UiEvent.HudRoundStarted", AudioSoundId.RoundStart, U, false, "owner: RunAudioEvent.RoundStart (same frame)", true),
            B(AudioEventSource.Ui, "UiEvent.HudRoundCompleted", AudioSoundId.RoundComplete, U, false, "owner: RunAudioEvent.RoundComplete", true),
            B(AudioEventSource.Ui, "UiEvent.HudPoolExhausted", AudioSoundId.RoundComplete, U, false, "owner: RunAudioEvent.RoundPoolExhausted (soft variant)", true),
            B(AudioEventSource.Ui, "UiEvent.HudLowHealthEntered", AudioSoundId.HudLowHealth, U, false, "muffled double heartbeat when the bar turns danger (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.HudLowHealthLeft", AudioSoundId.None, U, false, "silent by design (S-09 row): the colour returning is enough", true),
            B(AudioEventSource.Ui, "UiEvent.HudMagazineEmpty", AudioSoundId.HudMagazineEmpty, U, false, "HUD chip, not the trigger: DryFire stays the pull on an empty magazine (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.HudSpecialSpawned", AudioSoundId.None, U, false, "owner: CombatAudioEvent.SpecialWeaponSpawn → SwSpawnGong (REQ-053: the gong is bound to that one row only)", true),
            B(AudioEventSource.Ui, "UiEvent.HudSpecialPickedUp", AudioSoundId.SwPickup, U, false, "owner: CombatAudioEvent.SpecialWeaponPickup", true),
            B(AudioEventSource.Ui, "UiEvent.HudSpecialFinalSeconds", AudioSoundId.SwFinalSeconds, U, false, "owner: CombatAudioEvent.SpecialWeaponFinalSeconds (REQ-058 audible warning)", true),
            B(AudioEventSource.Ui, "UiEvent.HudSpecialEnded", AudioSoundId.SwExpired, U, false, "owner: CombatAudioEvent.SpecialWeaponExpired", true),
            B(AudioEventSource.Ui, "UiEvent.HudSpecialExpired", AudioSoundId.SwGroundExpired, U, false, "owner: CombatAudioEvent.SpecialWeaponGroundExpired", true),
            B(AudioEventSource.Ui, "UiEvent.HudThreatEncircled", AudioSoundId.HudThreatEncircled, U, false, "SURROUNDED chip: low tension hit (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.HudThreatRearAttack", AudioSoundId.HudThreatRear, U, false, "BEHIND YOU chip: sharp reverse whoosh (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.HudThreatStrongVersion", AudioSoundId.HudThreatStrong, U, false, "ELITE/ULTRA NEARBY chip: dark short bell, distinct from the reserved gong (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.HudThreatWindup", AudioSoundId.None, U, false, "covered by the positional windup vocal (DEC-067); a 2D double would blur direction", true),
            B(AudioEventSource.Ui, "UiEvent.HudDamageEdge", AudioSoundId.PlayerDamage, U, false, "owner: PlayerHealth.AnyDamaged", true),
            B(AudioEventSource.Ui, "UiEvent.HudNewBestRound", AudioSoundId.HudNewBestRound, U, false, "U6 ribbon: short −6 dB rin (UiNewBest family), priority 55, no duck, held ≤ 3 s while a threat chip is up (v015)"),
            B(AudioEventSource.Ui, "UiEvent.HudEliteDown", AudioSoundId.HudEliteDown, U, false, "ELITE DOWN: lacquer tok + short dark bell - not a stinger (music never ducks for it), priority 60 (v015)"),
            B(AudioEventSource.Ui, "UiEvent.HudUltraDown", AudioSoundId.HudUltraDown, U, false, "ULTRA DOWN: the elite voice + gold-leaf shimmer, priority 60, no duck (v015)"),
            B(AudioEventSource.Ui, "UiEvent.UpgradePurchased", AudioSoundId.UpgradePurchase, U, false, "owner: RunAudioEvent.UpgradePurchased", true),
            B(AudioEventSource.Ui, "UiEvent.UpgradeRerolled", AudioSoundId.UiReroll, U, false, "three quick card rustles (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.UpgradeOfferLocked", AudioSoundId.UiLock, U, false, "metal latch (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.ResultsRevealed", AudioSoundId.UiReveal, U, false, "owner: UiSound.Reveal → PrototypeAudio.Reveal (same frame)", true),
            B(AudioEventSource.Ui, "UiEvent.LevelUp", AudioSoundId.UiLevelUp, U, false, "rising three-note (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.UnlockRevealed", AudioSoundId.UiUnlock, U, false, "pickup + magnet sparkle (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.AchievementRevealed", AudioSoundId.UiAchievement, U, false, "short round-complete + bell (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.NewBestRound", AudioSoundId.UiNewBest, U, false, "the brightest one (AP-011 v005)"),
            B(AudioEventSource.Ui, "UiEvent.SettingsChanged", AudioSoundId.None, U, false, "silent by design: SliderChanged / ToggleChanged carry it", true),
            B(AudioEventSource.Ui, "UiEvent.CardDealt", AudioSoundId.UiCardDealt, U, false, "washi slide per card, pitch +0/+2/+4 % by deal index; 'last' adds the settle tok (v015)"),
            B(AudioEventSource.Ui, "UiEvent.CardFlipped", AudioSoundId.UiCardFlipped, U, false, "one rustle per changed card at scaleX = 0 (reroll) (v015)"),
            B(AudioEventSource.Ui, "UiEvent.StampLanded", AudioSoundId.UiStampLanded, U, false, "hanko on the contact frame; detail rare / epic → UiStampRare / UiStampEpic (v015)"),
            B(AudioEventSource.Ui, "UiEvent.CounterRolling", AudioSoundId.UiCounterRoll, U, false, "soroban roll, controllable UI voice, stops at CounterSettled or 0.6 s (v015)"),
            B(AudioEventSource.Ui, "UiEvent.CounterSettled", AudioSoundId.UiCounterSettled, U, false, "go-stone tok as the counter lands; ends the roll (v015)"),
            B(AudioEventSource.Ui, "UiEvent.RerollHoldStarted", AudioSoundId.UiHoldRub, U, false, "U5 hold: brush rub for 600 ms on the UI voice; Confirm completes, Back (early release) fades it in 20 ms (v015)"),
            B(AudioEventSource.Run, "Director.PauseOpened", AudioSoundId.UiPauseOpen, U, false, "WP-068: pause = the curtain-close short variant on the Pause snapshot (director, RunFlowClock pause hold)"),
            B(AudioEventSource.Run, "Director.PauseClosed", AudioSoundId.UiPauseClose, U, false, "WP-068: resume = the curtain-open short variant"),
            B(AudioEventSource.Run, "Director.DeathInk", AudioSoundId.UiInkBleed, U, false, "WP-068: ink texture 0.6 s after death under StingerDeath, until the Results curtain (−26 LUFS)")
        };

        private static AudioEventBinding B(AudioEventSource s, string e, AudioSoundId id, AudioBus bus, bool spatial, string note, bool reserved = false)
        {
            return new AudioEventBinding(s, e, id, bus, spatial, note, reserved);
        }

        /// <summary>Rows for one source. Allocates; documentation/test use.</summary>
        public static List<AudioEventBinding> For(AudioEventSource source)
        {
            var list = new List<AudioEventBinding>();
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Source == source)
                {
                    list.Add(Entries[i]);
                }
            }

            return list;
        }

        public static bool TryFind(AudioEventSource source, string evt, out AudioEventBinding binding)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Source == source && string.Equals(Entries[i].Event, evt, StringComparison.Ordinal))
                {
                    binding = Entries[i];
                    return true;
                }
            }

            binding = default;
            return false;
        }

        /// <summary>Tier-specific vocal ids. Standard is the fallback for anything unexpected.</summary>
        public static AudioSoundId ZombieWindup(DD.Prototype.Core.Config.EnemyTier tier)
        {
            switch (tier)
            {
                case DD.Prototype.Core.Config.EnemyTier.Special: return AudioSoundId.ZombieWindupSpecial;
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieWindupElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieWindupUltra;
                default: return AudioSoundId.ZombieWindupStandard;
            }
        }

        public static AudioSoundId ZombieAttack(DD.Prototype.Core.Config.EnemyTier tier)
        {
            switch (tier)
            {
                case DD.Prototype.Core.Config.EnemyTier.Special: return AudioSoundId.ZombieAttackSpecial;
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieAttackElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieAttackUltra;
                default: return AudioSoundId.ZombieAttackStandard;
            }
        }

        public static AudioSoundId ZombieHit(DD.Prototype.Core.Config.EnemyTier tier)
        {
            switch (tier)
            {
                case DD.Prototype.Core.Config.EnemyTier.Special: return AudioSoundId.ZombieHitSpecial;
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieHitElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieHitUltra;
                default: return AudioSoundId.ZombieHitStandard;
            }
        }

        public static AudioSoundId ZombieDeath(DD.Prototype.Core.Config.EnemyTier tier)
        {
            switch (tier)
            {
                case DD.Prototype.Core.Config.EnemyTier.Special: return AudioSoundId.ZombieDeathSpecial;
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieDeathElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieDeathUltra;
                default: return AudioSoundId.ZombieDeathStandard;
            }
        }

        /// <summary>WP-021C: near-range idle breath per tier (Standard keeps the v003 clip).</summary>
        public static AudioSoundId ZombieIdle(DD.Prototype.Core.Config.EnemyTier tier)
        {
            switch (tier)
            {
                case DD.Prototype.Core.Config.EnemyTier.Special: return AudioSoundId.ZombieIdleSpecial;
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieIdleElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieIdleUltra;
                default: return AudioSoundId.ZombieIdle;
            }
        }

        /// <summary>
        /// WP-048B: WP-032C weapon-handling clip event → the equipped roster weapon's own foley
        /// (A Rifle = AssaultRifle, B Carbine = AutoSmg, C Shotgun = CombatShotgun). None for events that are not foley.
        /// </summary>
        public static AudioSoundId WeaponFoley(DD.Prototype.Core.Config.ClassicWeaponId weapon, DD.Prototype.Characters.CharacterAnimationEvent e)
        {
            // WP-069 B2: v3 weapons use their class sibling's foley row (0 rifle / 1 smg / 2 shotgun) until S-08's set.
            int w = DD.Prototype.Core.Weapons.Arsenal.LegacyPoseIndex(weapon);
            int slot;
            switch (e)
            {
                case DD.Prototype.Characters.CharacterAnimationEvent.Eject: slot = 0; break;
                case DD.Prototype.Characters.CharacterAnimationEvent.Chamber: slot = 1; break;
                case DD.Prototype.Characters.CharacterAnimationEvent.MagOut: slot = 2; break;
                case DD.Prototype.Characters.CharacterAnimationEvent.MagIn:
                case DD.Prototype.Characters.CharacterAnimationEvent.MagIn2:
                case DD.Prototype.Characters.CharacterAnimationEvent.MagIn3: slot = 3; break;
                case DD.Prototype.Characters.CharacterAnimationEvent.SwitchReady:
                case DD.Prototype.Characters.CharacterAnimationEvent.SwitchAway: slot = 4; break;
                case DD.Prototype.Characters.CharacterAnimationEvent.Lowered: slot = 5; break;
                case DD.Prototype.Characters.CharacterAnimationEvent.Raised: slot = 6; break;
                default: return AudioSoundId.None;
            }

            // Ids are laid out Weapon{A,B,C}{Eject,Chamber,MagOut,MagIn,Switch,Lowered,Raised} = 260 + w*7 + slot.
            return (AudioSoundId)((int)AudioSoundId.WeaponAEject + w * 7 + slot);
        }

        /// <summary>WP-048: the region voice of an armour break (WP-040 armor_break_&lt;region&gt;). Body is the fallback for None.</summary>
        public static AudioSoundId ArmorBreakRegion(DD.Prototype.Core.Config.ArmorSlot region)
        {
            switch (region)
            {
                case DD.Prototype.Core.Config.ArmorSlot.Helmet: return AudioSoundId.ArmorBreakHelmet;
                case DD.Prototype.Core.Config.ArmorSlot.Leg: return AudioSoundId.ArmorBreakLeg;
                default: return AudioSoundId.ArmorBreakBody;
            }
        }

        /// <summary>WP-021C: the aggro growl (since WP-048 on the spawn_land beat), per tier.</summary>
        public static AudioSoundId ZombieAggro(DD.Prototype.Core.Config.EnemyTier tier)
        {
            switch (tier)
            {
                case DD.Prototype.Core.Config.EnemyTier.Special: return AudioSoundId.ZombieAggroSpecial;
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieAggroElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieAggroUltra;
                default: return AudioSoundId.ZombieAggroStandard;
            }
        }
    }
}
