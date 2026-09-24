using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-037: the effect / sound identifiers of one Special Weapon's USE, as data for
    /// the VFX (WP-019) and audio (WP-021) layers and for the AP-014 Special Weapon sheets.
    /// Lifecycle cues are already on the bus (spawn gong, beacon loop, pickup, final
    /// seconds, expired / ground expired, spin-up loop / spin-down — WP-007 + WP-021 rows);
    /// this table names what the WEAPON itself looks and sounds like while in use. The
    /// VFX family slots are filled when the AP-014 sheets land (empty string = no sheet
    /// yet, the placeholder effect stays). Data only — nothing here changes behaviour.
    /// </summary>
    public readonly struct SpecialWeaponUsageIds
    {
        public SpecialWeaponUsageIds(SpecialWeaponId id, CombatAudioEvent fireCue, CombatAudioEvent activeLoop,
            string fireVfxFamily, string fieldVfxFamily, string impactVfxFamily, string hudIconId)
        {
            Id = id;
            FireCue = fireCue;
            ActiveLoop = activeLoop;
            FireVfxFamily = fireVfxFamily;
            FieldVfxFamily = fieldVfxFamily;
            ImpactVfxFamily = impactVfxFamily;
            HudIconId = hudIconId;
        }

        public SpecialWeaponId Id { get; }

        /// <summary>Cue raised per trigger pull / attack (today: HeavyWeaponFire for every Special).</summary>
        public CombatAudioEvent FireCue { get; }

        /// <summary>Loop that runs while the weapon is in hand (today: HeavyWeaponSpinUp for every Special).</summary>
        public CombatAudioEvent ActiveLoop { get; }

        /// <summary>AP-014 VFX family for the attack itself (Kagura wave band / Ofuda dart + web / Tide well). Empty until the sheet exists.</summary>
        public string FireVfxFamily { get; }

        /// <summary>AP-014 VFX family for the persistent field object (wave / seal web / well).</summary>
        public string FieldVfxFamily { get; }

        /// <summary>AP-010 impact family used for Special hits (shared with the classic path).</summary>
        public string ImpactVfxFamily { get; }

        /// <summary>HUD icon key (S-09) for the Special slot.</summary>
        public string HudIconId { get; }
    }

    /// <summary>The usage-id table (WP-037). PrototypeOnly fixtures; not a decision on the final Special Weapon set (KS-5).</summary>
    public static class SpecialWeaponUsage
    {
        private static readonly SpecialWeaponUsageIds[] s_table =
        {
            // WP-069 B4 (DEC-094): the two Specials. Families are the DIRECTION §2 VFX rows (S-07 fills the sheets).
            new SpecialWeaponUsageIds(SpecialWeaponId.Raiju, CombatAudioEvent.HeavyWeaponFire, CombatAudioEvent.HeavyWeaponSpinUp,
                "sw_arc_orb", "sw_arc_chain", "impact", "special_raiju"),
            new SpecialWeaponUsageIds(SpecialWeaponId.Namekuji, CombatAudioEvent.HeavyWeaponFire, CombatAudioEvent.HeavyWeaponSpinUp,
                "sw_gel_blob", "sw_gel_puddle", "impact", "special_namekuji"),
        };

        public static int Count => s_table.Length;

        public static SpecialWeaponUsageIds Get(int index)
        {
            return s_table[index];
        }

        /// <summary>The row of a weapon; the first row when the id is unknown (never throws on the hot path).</summary>
        public static SpecialWeaponUsageIds For(SpecialWeaponId id)
        {
            for (int i = 0; i < s_table.Length; i++)
            {
                if (s_table[i].Id == id) return s_table[i];
            }

            return s_table[0];
        }

        // ---- WP-042 · AP-009C pickup clip events (DEC-082: no movement lock; the upper-body layer carries the grab)

        /// <summary>Clip event: the left hand closes on the Special Weapon on the ground (hide the ground pickup).</summary>
        public const string PickupGrabEvent = "pickup_grab";

        /// <summary>Clip event: the Special Weapon is in the right hand at socket_weapon_r (show the AP-014 hand model).</summary>
        public const string PickupDoneEvent = "pickup_done";

        /// <summary>
        /// Seconds after activation the hand model shows if no <see cref="PickupDoneEvent"/>
        /// arrives — the AP-009C clip fires it at frame 22 (0.73 s); the fallback keeps a missing
        /// or interrupted clip (hit, death) from leaving the weapon invisible.
        /// </summary>
        public const float PickupDoneFallbackSeconds = 0.8f;

        /// <summary>
        /// Clip event names the weapon controller listens for (AP-009C v003 <c>new_events</c>):
        /// index = <see cref="WeaponClipEvent"/>. Resolved by name against
        /// <c>CharacterAnimationEvents.Names</c> at install, so the slots come alive when the
        /// S-06 enum carries them and stay inert (timer fallback) until then.
        /// </summary>
        public static readonly string[] ClipEventNames = { "eject", "mag_out", "mag_in", "chamber", PickupGrabEvent, PickupDoneEvent, "mag_in_2", "mag_in_3" };
    }

    /// <summary>The clip events the weapon layer consumes; index into <see cref="SpecialWeaponUsage.ClipEventNames"/>.</summary>
    public enum WeaponClipEvent
    {
        Eject = 0,
        MagOut = 1,
        MagIn = 2,
        Chamber = 3,
        PickupGrab = 4,
        PickupDone = 5,
        /// <summary>The shotgun's second / third shell (AP-009C reload: mag_in / mag_in_2 / mag_in_3).</summary>
        MagIn2 = 6,
        MagIn3 = 7
    }
}
