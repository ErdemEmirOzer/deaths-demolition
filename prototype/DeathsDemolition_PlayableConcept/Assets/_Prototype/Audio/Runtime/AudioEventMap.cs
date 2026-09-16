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

        /// <summary><c>PrototypeAudio.*</c> static calls from the UI kit.</summary>
        Ui = 5,

        /// <summary>Continuous: ambience beds driven by region + phase.</summary>
        Ambience = 6,

        /// <summary>Continuous: music layers driven by intensity + tier + phase.</summary>
        Music = 7,

        /// <summary>Reserved for WP-018 (ADR-018-1 animation events) — bound when it lands on main.</summary>
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

        public static readonly AudioEventBinding[] Entries =
        {
            // ---- weapons (Combat) ------------------------------------------------
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ClassicWeaponFire), AudioSoundId.RifleFire, W, true, "intensity picks SMG/Rifle/Shotgun (AudioMixPlan.ClassicWeaponFor)"),
            B(AudioEventSource.Combat, nameof(CombatAudioEvent.ClassicWeaponDryFire), AudioSoundId.DryFire, W, false, ""),
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

            // ---- zombie vocal set (Damage + Polled) — DEC-067 -----------------------
            B(AudioEventSource.Polled, "ZombieEnemy.AttackPhase→Windup", AudioSoundId.ZombieWindupStandard, T, true, "DEC-067: ≥ 0.3 s before the swing, positional (telegraph 0.55 s); Elite/Ultra ids by tier"),
            B(AudioEventSource.Polled, "ZombieEnemy.AttackPhase→Active", AudioSoundId.ZombieAttackStandard, T, true, "the swing; Elite/Ultra ids by tier"),
            B(AudioEventSource.Damage, "DamageEvents.EnemyDamaged", AudioSoundId.ZombieHitStandard, T, true, "pain grunt, throttled per enemy; Elite/Ultra ids by tier"),
            B(AudioEventSource.Damage, "DamageEvents.EnemyDied", AudioSoundId.ZombieDeathStandard, T, true, "vocal layer over CombatAudioEvent.EnemyDeath; Elite/Ultra ids by tier"),
            B(AudioEventSource.Damage, "DamageEvents.PlayerAttacked", AudioSoundId.PlayerDamage, U, false, "same cue as AnyDamaged (single owner: PlayerHealth); listed for completeness"),
            B(AudioEventSource.Damage, "DamageEvents.ArmorBroken", AudioSoundId.ArmorBreak, T, true, "no second cue: the tier cue in ReportEnemyHit owns it (WP-006 N1)"),

            // ---- UI (Ui) ------------------------------------------------------------
            B(AudioEventSource.Ui, "PrototypeAudio.Navigate", AudioSoundId.UiNavigate, U, false, "AP-011"),
            B(AudioEventSource.Ui, "PrototypeAudio.Confirm", AudioSoundId.UiConfirm, U, false, "AP-011"),
            B(AudioEventSource.Ui, "PrototypeAudio.Back", AudioSoundId.UiBack, U, false, "AP-011"),
            B(AudioEventSource.Ui, "PrototypeAudio.Denied", AudioSoundId.UiDenied, U, false, "AP-011"),

            // ---- ambience (continuous) — DEC-066 / DEC-068 ---------------------------
            B(AudioEventSource.Ambience, "Region.common", AudioSoundId.AmbCommon, A, false, "after-rain drip, wind, bamboo, water — always on"),
            B(AudioEventSource.Ambience, "Region.center", AudioSoundId.AmbRegionCenter, A, false, "crossfade ≥ 2 s (AudioRegionMap)"),
            B(AudioEventSource.Ambience, "Region.north", AudioSoundId.AmbRegionNorth, A, false, ""),
            B(AudioEventSource.Ambience, "Region.east", AudioSoundId.AmbRegionEast, A, false, ""),
            B(AudioEventSource.Ambience, "Region.south", AudioSoundId.AmbRegionSouth, A, false, ""),
            B(AudioEventSource.Ambience, "Region.west", AudioSoundId.AmbRegionWest, A, false, ""),

            // ---- music (continuous) — DEC-064 / DEC-065 ------------------------------
            B(AudioEventSource.Music, "Music.base", AudioSoundId.MusicBase, A, false, "always; pulled back between rounds"),
            B(AudioEventSource.Music, "Music.percussion", AudioSoundId.MusicPercussion, A, false, "MusicIntensity ≥ 0.15"),
            B(AudioEventSource.Music, "Music.tension", AudioSoundId.MusicTension, A, false, "MusicIntensity ≥ 0.45"),
            B(AudioEventSource.Music, "Music.accent", AudioSoundId.MusicAccent, A, false, "Elite/Ultra alive or Special Weapon active"),

            // ---- reserved for WP-018 (ADR-018-1) -------------------------------------
            B(AudioEventSource.Animation, "anim.footstep", AudioSoundId.None, I, true, "WP-018: bind to surface set when the event lands", true),
            B(AudioEventSource.Animation, "anim.hit_window", AudioSoundId.None, T, true, "WP-018: replaces the polled windup/active edge", true),
            B(AudioEventSource.Animation, "anim.death_end", AudioSoundId.None, T, true, "WP-018: body-settle layer", true)
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
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieWindupElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieWindupUltra;
                default: return AudioSoundId.ZombieWindupStandard;
            }
        }

        public static AudioSoundId ZombieAttack(DD.Prototype.Core.Config.EnemyTier tier)
        {
            switch (tier)
            {
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieAttackElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieAttackUltra;
                default: return AudioSoundId.ZombieAttackStandard;
            }
        }

        public static AudioSoundId ZombieHit(DD.Prototype.Core.Config.EnemyTier tier)
        {
            switch (tier)
            {
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieHitElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieHitUltra;
                default: return AudioSoundId.ZombieHitStandard;
            }
        }

        public static AudioSoundId ZombieDeath(DD.Prototype.Core.Config.EnemyTier tier)
        {
            switch (tier)
            {
                case DD.Prototype.Core.Config.EnemyTier.Elite: return AudioSoundId.ZombieDeathElite;
                case DD.Prototype.Core.Config.EnemyTier.Ultra: return AudioSoundId.ZombieDeathUltra;
                default: return AudioSoundId.ZombieDeathStandard;
            }
        }
    }
}
