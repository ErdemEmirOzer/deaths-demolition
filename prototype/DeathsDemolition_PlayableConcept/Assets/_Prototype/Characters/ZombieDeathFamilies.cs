namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-071 · the user's "4 different deaths" (N6) as families (WP-071-ANIMATION-V3-DIRECTION Bölüm II A3): each is an
    /// authored onset that hands the body to the ragdoll in the push direction; Ultra and the two Special reactions are extra.
    /// </summary>
    public enum ZombieDeathFamily
    {
        FallBack = 0,      // shot from the front: knees give, torso thrown back
        FallForward = 1,   // shot from behind / killed running: pitches onto the face
        SpinSide = 2,      // side or head hit: twists and drops sideways
        LegsOut = 3,       // leg hit / explosion from below: legs taken, falls on the seat then back
        UltraTimber = 4,   // Executioner: the slow topple
        ArcElectro = 5,    // chain lightning Special (B3)
        GelSlip = 6        // gel Special (B3)
    }

    public static class ZombieDeathFamilies
    {
        public const int Count = 4;

        /// <summary>WP-078 (S-10): deaths per family since reset (index = <see cref="ZombieDeathFamily"/>), for the round log.</summary>
        public static readonly int[] Counts = new int[7];
        public static void Record(ZombieDeathFamily f) { int i = (int)f; if (i >= 0 && i < Counts.Length) Counts[i]++; }
        public static void ResetCounts() => System.Array.Clear(Counts, 0, Counts.Length);

        /// <summary>
        /// The AP-009 death index (ZombieAnimator's pick: 0 fwd, 1 back, 2 side, 3 kneel, 4 side_R, 5 special_A, 6 special_B,
        /// 7 ultra) as a family, until AP-022 ships one onset clip per family.
        /// </summary>
        public static ZombieDeathFamily OfDeathIndex(int deathIndex)
        {
            switch (deathIndex)
            {
                case 0: return ZombieDeathFamily.FallForward;
                case 1: return ZombieDeathFamily.FallBack;
                case 2: case 4: return ZombieDeathFamily.SpinSide;
                case 3: return ZombieDeathFamily.LegsOut;
                case 5: return ZombieDeathFamily.FallForward;
                case 6: return ZombieDeathFamily.SpinSide;
                case 7: return ZombieDeathFamily.UltraTimber;
                default: return ZombieDeathFamily.FallBack;
            }
        }
    }
}
