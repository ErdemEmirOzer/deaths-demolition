using System;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>The AP-009 animation events (ADR-018-1). Names = the manifest's event names.</summary>
    public enum CharacterAnimationEvent
    {
        None = 0,
        Fire = 1,
        HitWindowStart = 2,
        HitWindowEnd = 3,
        FootLeft = 4,
        FootRight = 5,
        DeathEnd = 6,
        RiseEnd = 7,
        Pickup = 8,
        Breath = 9,
        Hit = 10,
        StaggerEnd = 11,
        /// <summary>WP-032B · idle_queue weight shifts (AP-009B v002) — a foot-shuffle cue for S-07/S-08, no gameplay meaning.</summary>
        ShiftLeft = 12,
        ShiftRight = 13,
        // WP-032C · AP-009C v003 (weapon handling, pickup swap, armour break, spawn beats)
        Eject = 14,
        Chamber = 15,
        MagOut = 16,
        MagIn = 17,
        MagIn2 = 18,
        MagIn3 = 19,
        SwitchReady = 20,
        SwitchAway = 21,
        Lowered = 22,
        Raised = 23,
        PickupGrab = 24,
        PickupDone = 25,
        ArmorBreak = 26,
        SpawnRise = 27,
        SpawnLand = 28,
        // WP-071 · event contract v3 (WP-071-ANIMATION-V3-DIRECTION Bölüm I K2 / III A8.2); int payload = index / side
        FootLOff = 29,
        FootROff = 30,
        PivotPlant = 31,
        StopPlant = 32,
        WeightShift = 33,
        AttackRecover = 34,
        GrabStart = 35,
        GrabEnd = 36,
        AttackStep = 37,
        Roar = 38,
        RagdollHandoff = 39,
        KnockbackEnd = 40,
        HurtEnd = 41,
        GroundImpact = 42,
        BloodBurst = 43,
        Twitch = 44,
        Spasm = 45,
        LeftHandRelease = 46,
        LeftHandGrab = 47,
        AdsReady = 48,
        ReloadCommit = 49,
        ReloadCancelPoint = 50,
        MechanismStart = 51,
        MechanismEnd = 52,
        SpinUp = 53,
        SpinDown = 54,
        SwChargePeak = 55,
        ShellEject = 56,
        InspectPeak = 57,
        Charge = 58,
        BoxOpen = 59,
        BeltSeat = 60,
        BoxClose = 61,
        BoltUp = 62,
        BoltBack = 63,
        BoltFwd = 64,
        BoltDown = 65,
        ShellIn = 66,
        RocketIn = 67,
        RocketSeat = 68,
        CellOut = 69,
        CellIn = 70,
        CanisterOut = 71,
        CanisterIn = 72,
        CoverOpen = 73,
        CoverClose = 74,
        // P-06 AP-022 (24.09): balance lost (≤ ragdoll_handoff) / attack wind-up begins
        FallStart = 75,
        TelegraphStart = 76,
        // S-02 WP-069 (24.09): the gel Special's side lever pump (part_lever)
        PumpDown = 77,
        PumpUp = 78,
        // WP-082 / WP-083 B2 (C-00 05.10, S-02 magazine contract v1): the spent magazine leaves the hand and the fresh one arrives
        // (between mag_detach = MagOut and mag_insert = MagIn) — the one genuinely new reload beat
        MagHandAttach = 79
    }

    /// <summary>
    /// WP-018 · ADR-018-1 · THE ANIMATION EVENT CONTRACT. One component on the same GameObject
    /// as the character's Animator (AnimationEvents can only call methods there); the AP-009
    /// clips carry <c>AnimationEvent</c>s named after the manifest (written at import by
    /// <c>ArtAnimationClipContract</c>) and Unity calls the same-named methods below.
    ///
    /// Consumers: the owner (ZombieSkinnedVisual / PlayerSkinnedVisual) subscribes to
    /// <see cref="Raised"/> for gameplay (DEC-029 hit window, death end); S-07 effects and
    /// S-08 audio subscribe to the static <see cref="Any"/> (footsteps, fire, breath) — read,
    /// never re-published. Zero allocation per event; the payload is the component (whose
    /// transform is the character) and the enum.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAnimationEvents : MonoBehaviour
    {
        /// <summary>The manifest event names, index = (int)CharacterAnimationEvent.</summary>
        public static readonly string[] Names =
        {
            "", "fire", "hit_window_start", "hit_window_end", "foot_l", "foot_r",
            "death_end", "rise_end", "pickup", "breath", "hit", "stagger_end", "shift_l", "shift_r",
            "eject", "chamber", "mag_out", "mag_in", "mag_in_2", "mag_in_3", "switch_ready", "switch_away", "lowered", "raised",
            "pickup_grab", "pickup_done", "armor_break", "spawn_rise", "spawn_land",
            // WP-071 v3
            "foot_l_off", "foot_r_off", "pivot_plant", "stop_plant", "weight_shift", "attack_recover", "grab_start", "grab_end", "attack_step", "roar", "ragdoll_handoff", "knockback_end", "hurt_end", "ground_impact", "blood_burst", "twitch", "spasm", "lh_release", "lh_grab", "ads_ready", "reload_commit", "reload_cancel_point", "mechanism_start", "mechanism_end", "spin_up", "spin_down", "sw_charge_peak", "shell_eject", "inspect_peak", "charge", "box_open", "belt_seat", "box_close", "bolt_up", "bolt_back", "bolt_fwd", "bolt_down", "shell_in", "rocket_in", "rocket_seat", "cell_out", "cell_in", "canister_out", "canister_in", "cover_open", "cover_close", "fall_start", "telegraph_start", "pump_down", "pump_up",
            // WP-082 / WP-083 B2 (S-02 magazine contract v1)
            "mag_hand_attach"
        };

        /// <summary>
        /// WP-082 / WP-083 B2 (C-00 05.10, S-02 magazine contract v1): the contract's spellings for beats the AP-009C names already carry —
        /// accepted from clips (not rejected like <see cref="Aliases"/>) and raised as the existing event, so every listener of mag_out /
        /// mag_in / chamber keeps working: { contract name, existing name }.
        /// </summary>
        public static readonly string[,] Synonyms =
        {
            { "mag_detach", "mag_out" }, { "mag_insert", "mag_in" }, { "bolt_release", "chamber" }
        };

        /// <summary>WP-071: v3 names that were proposed under another spelling (the validator rejects the alias and names the canonical one).</summary>
        public static readonly string[,] Aliases =
        {
            { "knock_end", "knockback_end" }, { "ads_in_done", "ads_ready" }, { "interrupt_ok", "reload_cancel_point" },
            { "reload_interrupt_ok", "reload_cancel_point" }, { "recover", "hurt_end" }, { "ragdoll_on", "ragdoll_handoff" }, { "impact_land", "ground_impact" }
        };

        public static CharacterAnimationEvent Parse(string name)
        {
            for (int i = 1; i < Names.Length; i++)
            {
                if (Names[i] == name) return (CharacterAnimationEvent)i;
            }

            for (int i = 0; i < Synonyms.GetLength(0); i++)
            {
                if (Synonyms[i, 0] == name) return Parse(Synonyms[i, 1]);
            }

            return CharacterAnimationEvent.None;
        }

        /// <summary>Per-character subscribers (owner).</summary>
        public event Action<CharacterAnimationEvent> Raised;

        /// <summary>Project-wide subscribers (effects, audio).</summary>
        public static event Action<CharacterAnimationEvents, CharacterAnimationEvent> Any;

        /// <summary>Total events raised since the last reset (tests / probes).</summary>
        public static int RaisedCount { get; private set; }

        /// <summary>WP-071: the int payload of the last event (index / side; 0 = none).</summary>
        public int LastInt { get; private set; }

        /// <summary>Last event raised on this component (tests).</summary>
        public CharacterAnimationEvent Last { get; private set; }

        /// <summary>Time.time of the last event (tests).</summary>
        public float LastTime { get; private set; }

        public static void ResetStatics()
        {
            Any = null;
            RaisedCount = 0;
        }

        // ------------------------------------------ AnimationEvent targets (manifest names)
        public void fire() => Raise(CharacterAnimationEvent.Fire);
        public void hit_window_start() => Raise(CharacterAnimationEvent.HitWindowStart);
        public void hit_window_end() => Raise(CharacterAnimationEvent.HitWindowEnd);
        public void foot_l() => Raise(CharacterAnimationEvent.FootLeft);
        public void foot_r() => Raise(CharacterAnimationEvent.FootRight);
        public void death_end() => Raise(CharacterAnimationEvent.DeathEnd);
        public void rise_end() => Raise(CharacterAnimationEvent.RiseEnd);
        public void pickup() => Raise(CharacterAnimationEvent.Pickup);
        public void breath() => Raise(CharacterAnimationEvent.Breath);
        public void hit() => Raise(CharacterAnimationEvent.Hit);
        public void stagger_end() => Raise(CharacterAnimationEvent.StaggerEnd);
        public void shift_l() => Raise(CharacterAnimationEvent.ShiftLeft);
        public void shift_r() => Raise(CharacterAnimationEvent.ShiftRight);
        public void eject() => Raise(CharacterAnimationEvent.Eject);
        public void chamber() => Raise(CharacterAnimationEvent.Chamber);
        public void mag_out() => Raise(CharacterAnimationEvent.MagOut);
        public void mag_in() => Raise(CharacterAnimationEvent.MagIn);
        public void mag_in_2() => Raise(CharacterAnimationEvent.MagIn2);
        public void mag_in_3() => Raise(CharacterAnimationEvent.MagIn3);
        public void switch_ready() => Raise(CharacterAnimationEvent.SwitchReady);
        public void switch_away() => Raise(CharacterAnimationEvent.SwitchAway);
        public void lowered() => Raise(CharacterAnimationEvent.Lowered);
        public void raised() => Raise(CharacterAnimationEvent.Raised);
        public void pickup_grab() => Raise(CharacterAnimationEvent.PickupGrab);
        public void pickup_done() => Raise(CharacterAnimationEvent.PickupDone);
        public void armor_break() => Raise(CharacterAnimationEvent.ArmorBreak);
        public void spawn_rise() => Raise(CharacterAnimationEvent.SpawnRise);
        public void spawn_land() => Raise(CharacterAnimationEvent.SpawnLand);
        // WP-071 v3 (Unity passes AnimationEvent.intParameter to an int overload; 0 when the clip sets none)
        public void foot_l_off(int value) => Raise(CharacterAnimationEvent.FootLOff, value);
        public void foot_r_off(int value) => Raise(CharacterAnimationEvent.FootROff, value);
        public void pivot_plant(int value) => Raise(CharacterAnimationEvent.PivotPlant, value);
        public void stop_plant(int value) => Raise(CharacterAnimationEvent.StopPlant, value);
        public void weight_shift(int value) => Raise(CharacterAnimationEvent.WeightShift, value);
        public void attack_recover(int value) => Raise(CharacterAnimationEvent.AttackRecover, value);
        public void grab_start(int value) => Raise(CharacterAnimationEvent.GrabStart, value);
        public void grab_end(int value) => Raise(CharacterAnimationEvent.GrabEnd, value);
        public void attack_step(int value) => Raise(CharacterAnimationEvent.AttackStep, value);
        public void roar(int value) => Raise(CharacterAnimationEvent.Roar, value);
        public void ragdoll_handoff(int value) => Raise(CharacterAnimationEvent.RagdollHandoff, value);
        public void knockback_end(int value) => Raise(CharacterAnimationEvent.KnockbackEnd, value);
        public void hurt_end(int value) => Raise(CharacterAnimationEvent.HurtEnd, value);
        public void ground_impact(int value) => Raise(CharacterAnimationEvent.GroundImpact, value);
        public void blood_burst(int value) => Raise(CharacterAnimationEvent.BloodBurst, value);
        public void twitch(int value) => Raise(CharacterAnimationEvent.Twitch, value);
        public void spasm(int value) => Raise(CharacterAnimationEvent.Spasm, value);
        public void lh_release(int value) => Raise(CharacterAnimationEvent.LeftHandRelease, value);
        public void lh_grab(int value) => Raise(CharacterAnimationEvent.LeftHandGrab, value);
        public void ads_ready(int value) => Raise(CharacterAnimationEvent.AdsReady, value);
        public void reload_commit(int value) => Raise(CharacterAnimationEvent.ReloadCommit, value);
        public void reload_cancel_point(int value) => Raise(CharacterAnimationEvent.ReloadCancelPoint, value);
        public void mechanism_start(int value) => Raise(CharacterAnimationEvent.MechanismStart, value);
        public void mechanism_end(int value) => Raise(CharacterAnimationEvent.MechanismEnd, value);
        public void spin_up(int value) => Raise(CharacterAnimationEvent.SpinUp, value);
        public void spin_down(int value) => Raise(CharacterAnimationEvent.SpinDown, value);
        public void sw_charge_peak(int value) => Raise(CharacterAnimationEvent.SwChargePeak, value);
        public void shell_eject(int value) => Raise(CharacterAnimationEvent.ShellEject, value);
        public void inspect_peak(int value) => Raise(CharacterAnimationEvent.InspectPeak, value);
        public void charge(int value) => Raise(CharacterAnimationEvent.Charge, value);
        public void box_open(int value) => Raise(CharacterAnimationEvent.BoxOpen, value);
        public void belt_seat(int value) => Raise(CharacterAnimationEvent.BeltSeat, value);
        public void box_close(int value) => Raise(CharacterAnimationEvent.BoxClose, value);
        public void bolt_up(int value) => Raise(CharacterAnimationEvent.BoltUp, value);
        public void bolt_back(int value) => Raise(CharacterAnimationEvent.BoltBack, value);
        public void bolt_fwd(int value) => Raise(CharacterAnimationEvent.BoltFwd, value);
        public void bolt_down(int value) => Raise(CharacterAnimationEvent.BoltDown, value);
        public void shell_in(int value) => Raise(CharacterAnimationEvent.ShellIn, value);
        public void rocket_in(int value) => Raise(CharacterAnimationEvent.RocketIn, value);
        public void rocket_seat(int value) => Raise(CharacterAnimationEvent.RocketSeat, value);
        public void cell_out(int value) => Raise(CharacterAnimationEvent.CellOut, value);
        public void cell_in(int value) => Raise(CharacterAnimationEvent.CellIn, value);
        public void canister_out(int value) => Raise(CharacterAnimationEvent.CanisterOut, value);
        public void canister_in(int value) => Raise(CharacterAnimationEvent.CanisterIn, value);
        public void cover_open(int value) => Raise(CharacterAnimationEvent.CoverOpen, value);
        public void cover_close(int value) => Raise(CharacterAnimationEvent.CoverClose, value);
        public void fall_start(int value) => Raise(CharacterAnimationEvent.FallStart, value);
        public void telegraph_start(int value) => Raise(CharacterAnimationEvent.TelegraphStart, value);
        public void pump_down(int value) => Raise(CharacterAnimationEvent.PumpDown, value);
        public void pump_up(int value) => Raise(CharacterAnimationEvent.PumpUp, value);
        // WP-082 / WP-083 B2 (S-02 magazine contract v1): the new beat and the contract's spellings of the existing three
        public void mag_hand_attach(int value) => Raise(CharacterAnimationEvent.MagHandAttach, value);
        public void mag_detach(int value) => Raise(CharacterAnimationEvent.MagOut, value);
        public void mag_insert(int value) => Raise(CharacterAnimationEvent.MagIn, value);
        public void bolt_release(int value) => Raise(CharacterAnimationEvent.Chamber, value);

        /// <summary>Tests / probes: raise an event as if the clip had fired it.</summary>
        public void RaiseForTest(CharacterAnimationEvent e) => Raise(e);

        private void Raise(CharacterAnimationEvent e) => Raise(e, 0);

        private void Raise(CharacterAnimationEvent e, int value)
        {
            LastInt = value;
            Last = e;
            LastTime = Time.time;
            RaisedCount++;
            Raised?.Invoke(e);
            Any?.Invoke(this, e);
        }
    }
}
