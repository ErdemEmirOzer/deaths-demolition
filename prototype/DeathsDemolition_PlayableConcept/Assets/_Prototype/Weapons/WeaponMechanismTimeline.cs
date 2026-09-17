using DD.Prototype.Core.Config;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-042 · the timer FALLBACK for the mechanism slots (<see cref="WeaponEvents.Mechanism"/>)
    /// until the AP-009C clips drive them: the same moments the clips carry, as fractions of the
    /// reload (the animation driver scales the reload clip to <c>ReloadTime</c>, so a fraction is
    /// exact) and as seconds after a shot for the casing. AP-009C v003 frames at 30 fps:
    /// A reload 45 f — mag_out 12, mag_in 30, chamber 40; B 51 f — 12 / 34 / 44; C 66 f — shells
    /// 14 / 30 / 46, chamber 60; casing A/B frame 1 (immediate), C pump-back frame 12 of the
    /// 22-frame fire clip (0.40 s). A weapon with pellets is the shotgun timeline (shells, no
    /// magazine); everything else is the magazine timeline. Data only, no allocation.
    /// </summary>
    public readonly struct WeaponMechanismTimeline
    {
        public readonly bool Shells;
        public readonly float MagOutProgress;
        public readonly float Seat1Progress;
        public readonly float Seat2Progress;
        public readonly float Seat3Progress;
        public readonly float ChamberProgress;
        public readonly float CasingDelaySeconds;

        public WeaponMechanismTimeline(bool shells, float magOut, float seat1, float seat2, float seat3, float chamber, float casingDelay)
        {
            Shells = shells;
            MagOutProgress = magOut;
            Seat1Progress = seat1;
            Seat2Progress = seat2;
            Seat3Progress = seat3;
            ChamberProgress = chamber;
            CasingDelaySeconds = casingDelay;
        }

        /// <summary>Number of seat (mag_in) slots on this timeline: three shells or one magazine.</summary>
        public int SeatCount => Shells ? 3 : 1;

        /// <summary>Progress of seat slot <paramref name="index"/> (0-based).</summary>
        public float SeatProgress(int index)
        {
            switch (index)
            {
                case 0: return Seat1Progress;
                case 1: return Seat2Progress;
                default: return Seat3Progress;
            }
        }

        /// <summary>A (rifle) and B (carbine) timeline — B's 12/34/44 of 51 f rounds to the same fractions within 0.03.</summary>
        public static readonly WeaponMechanismTimeline Magazine = new WeaponMechanismTimeline(false, 12f / 45f, 30f / 45f, 0f, 0f, 40f / 45f, 0f);

        /// <summary>C (shotgun) timeline — three shells from the belt and a pump; the casing flies at the pump-back.</summary>
        public static readonly WeaponMechanismTimeline Shotgun = new WeaponMechanismTimeline(true, 0f, 14f / 66f, 30f / 66f, 46f / 66f, 60f / 66f, 12f / 30f);

        public static WeaponMechanismTimeline For(ClassicWeaponSettings settings)
        {
            return settings != null && settings.PelletCount > 1 ? Shotgun : Magazine;
        }
    }
}
