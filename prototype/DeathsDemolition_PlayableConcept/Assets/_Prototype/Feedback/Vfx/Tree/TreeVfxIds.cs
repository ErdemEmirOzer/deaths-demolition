using DD.Prototype.Rounds;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>WP-104 B4 (S-07) · the AP-041 sheets the run tree plays, one catalog row each (ArtAp041TreeIntake writes them).</summary>
    public enum TreeVfxRow
    {
        B1Ignition = 0,
        B1GroundScorch,
        BlastScorch1,
        BlastScorch2,
        BlastScorch3,
        BlastScorch4,
        B2IceImpact,
        B2Shards,
        B2Coating,
        B3Impact,
        B3Flash,
        B3SparkRing,
        B4Cloud,
        B5Explosive,
        B5Shock,
        B6Singularity,
        B6Rings,
        B7Nuclear,
        B7Shock,
        B7HeatGlint,
        B8Chain1,
        B8Chain2,
        B8Chain3,
        B10MiniFlame,
        B11Ring1,
        B11Ring2,
        B11Ring3,
        B11Sparks1,
        B11Sparks2,
        B11Sparks3,
        B11Smoke3,
        B12WhiteHot,
        B13Crit,
        B13LevelUp,
        B14Dome,
        B14Hit,
        B14Break,
        B15Thread,
        B16Ghost,
        B16Shadow,
        B17Pulse,
        T3InkShock,
        T4Enso,
        T5Spatter,
        X8Enso,
        Count
    }

    /// <summary>
    /// WP-104 B4 (S-07) · Catalog ids of <see cref="TreeVfxRow"/> (one place, so a typo is a compile error) and the AD §3 / AP-041
    /// family each <see cref="TreeProcKind"/> plays — the table B4-TASARIM.md prints and Wp104B4VfxTests checks for completeness.
    /// </summary>
    public static class TreeVfxIds
    {
        public const string Prefix = "fb_ap041t_";
        public const string DecalPrefix = "decal_ap041t_";

        private static readonly string[] s_ids =
        {
            "fb_ap041t_b1_ignition",
            "decal_ap041t_b1_ground_scorch",
            "decal_ap041t_scorch_01",
            "decal_ap041t_scorch_02",
            "decal_ap041t_scorch_03",
            "decal_ap041t_scorch_04",
            "fb_ap041t_b2_ice_impact",
            "fb_ap041t_b2_shards",
            "fb_ap041t_b2_coating",
            "fb_ap041t_b3_impact",
            "fb_ap041t_b3_flash",
            "fb_ap041t_b3_spark_ring",
            "fb_ap041t_b4_cloud",
            "fb_ap041t_b5_explosive",
            "fb_ap041t_b5_shock",
            "fb_ap041t_b6_singularity",
            "fb_ap041t_b6_rings",
            "fb_ap041t_b7_nuclear",
            "fb_ap041t_b7_shock",
            "fb_ap041t_b7_heat_glint",
            "fb_ap041t_b8_chain_01",
            "fb_ap041t_b8_chain_02",
            "fb_ap041t_b8_chain_03",
            "fb_ap041t_b10_mini_flame",
            "fb_ap041t_b11_ring_1",
            "fb_ap041t_b11_ring_2",
            "fb_ap041t_b11_ring_3",
            "fb_ap041t_b11_sparks_1",
            "fb_ap041t_b11_sparks_2",
            "fb_ap041t_b11_sparks_3",
            "fb_ap041t_b11_smoke_3",
            "fb_ap041t_b12_white_hot",
            "fb_ap041t_b13_crit",
            "fb_ap041t_b13_levelup",
            "fb_ap041t_b14_dome",
            "fb_ap041t_b14_hit",
            "fb_ap041t_b14_break",
            "fb_ap041t_b15_thread",
            "fb_ap041t_b16_ghost",
            "fb_ap041t_b16_shadow",
            "fb_ap041t_b17_pulse",
            "fb_ap041t_t3_ink_shock",
            "fb_ap041t_t4_enso",
            "fb_ap041t_t5_spatter",
            "fb_ap041t_x8_enso"
        };

        public static int Count => s_ids.Length;

        public static string Id(TreeVfxRow row) => s_ids[(int)row];

        /// <summary>The B8 chain sheet of a chain generation (three variants in turn).</summary>
        public static TreeVfxRow ChainRow(int depth) => TreeVfxRow.B8Chain1 + ((depth % 3) + 3) % 3;

        /// <summary>The aura ring / sparks rows of a level (1–3).</summary>
        public static TreeVfxRow AuraRing(int level) => TreeVfxRow.B11Ring1 + (TreeVfxLadder.ClampAura(level) - 1);

        public static TreeVfxRow AuraSparks(int level) => TreeVfxRow.B11Sparks1 + (TreeVfxLadder.ClampAura(level) - 1);

        /// <summary>The blast scorch decal of a turn counter (four variants).</summary>
        public static TreeVfxRow ScorchRow(int turn) => TreeVfxRow.BlastScorch1 + ((turn % 4) + 4) % 4;

        /// <summary>An element's hit sheet (B1–B4); the nova and the secondary-element hit use it.</summary>
        public static TreeVfxRow ElementImpact(TreeElement e)
        {
            switch (e)
            {
                case TreeElement.Fire: return TreeVfxRow.B1Ignition;
                case TreeElement.Ice: return TreeVfxRow.B2IceImpact;
                case TreeElement.Shock: return TreeVfxRow.B3Impact;
                case TreeElement.Venom: return TreeVfxRow.B4Cloud;
                default: return TreeVfxRow.Count;
            }
        }

        /// <summary>AD §3 / AP-041 family of a proc ("—" = no B4 world visual of its own: the round's tracer and impact, the rocket's
        /// RocketVisualSystem or the HUD carry it). Every value of <see cref="TreeProcKind"/> on main (B0 + B1b, 1–28) has a row.</summary>
        public static string FamilyOf(TreeProcKind kind)
        {
            switch (kind)
            {
                case TreeProcKind.Crit: return "B13";
                case TreeProcKind.CritRing: return "B13+T3";
                case TreeProcKind.ExplosiveRound: return "B5";
                case TreeProcKind.ChainBlast: return "B8";
                case TreeProcKind.MiniNuke: return "B7";
                case TreeProcKind.BlackHoleOpen: return "B6";
                case TreeProcKind.BlackHoleCollapse: return "B6+T3";
                case TreeProcKind.Ignite: return "B1";
                case TreeProcKind.FireGround: return "B1";
                case TreeProcKind.Freeze: return "B2";
                case TreeProcKind.Shatter: return "B2";
                case TreeProcKind.ShockChain: return "B3";
                case TreeProcKind.VenomCloud: return "B4";
                case TreeProcKind.ElementNova: return "B1-B4+T5";
                case TreeProcKind.DoomsdayBell: return "B7+T5";
                case TreeProcKind.ShieldBreak: return "B14";
                case TreeProcKind.Dodge: return "B16";
                case TreeProcKind.Leech: return "B15";
                case TreeProcKind.UndyingOath: return "B17+B13";
                case TreeProcKind.EndlessWindowOpen: return "T4";
                case TreeProcKind.EndlessWindowClose: return "T4";
                case TreeProcKind.OverclockPeak: return "B12";
                case TreeProcKind.SynergyFirstTrigger: return "X8";
                case TreeProcKind.ClusterSplit: return "B10+B5";
                case TreeProcKind.ShieldUp: return "B14";
                case TreeProcKind.ShieldHit: return "B14";
                case TreeProcKind.Ricochet: return "—";
                case TreeProcKind.BarrageRocket: return "—";
                default: return null;
            }
        }

        /// <summary>The row a proc's main visual draws (<see cref="TreeVfxRow.Count"/> = none of its own, e.g. a close).</summary>
        public static TreeVfxRow PrimaryRow(TreeProcKind kind)
        {
            switch (kind)
            {
                case TreeProcKind.Crit: return TreeVfxRow.B13Crit;
                case TreeProcKind.CritRing: return TreeVfxRow.B13Crit;
                case TreeProcKind.ExplosiveRound: return TreeVfxRow.B5Explosive;
                case TreeProcKind.ChainBlast: return TreeVfxRow.B8Chain1;
                case TreeProcKind.MiniNuke: return TreeVfxRow.B7Nuclear;
                case TreeProcKind.BlackHoleOpen: return TreeVfxRow.B6Singularity;
                case TreeProcKind.BlackHoleCollapse: return TreeVfxRow.T3InkShock;
                case TreeProcKind.Ignite: return TreeVfxRow.B1Ignition;
                case TreeProcKind.FireGround: return TreeVfxRow.B1GroundScorch;
                case TreeProcKind.Freeze: return TreeVfxRow.B2IceImpact;
                case TreeProcKind.Shatter: return TreeVfxRow.B2Shards;
                case TreeProcKind.ShockChain: return TreeVfxRow.B3Impact;
                case TreeProcKind.VenomCloud: return TreeVfxRow.B4Cloud;
                case TreeProcKind.ElementNova: return TreeVfxRow.T5Spatter;
                case TreeProcKind.DoomsdayBell: return TreeVfxRow.B7Shock;
                case TreeProcKind.ShieldBreak: return TreeVfxRow.B14Break;
                case TreeProcKind.Dodge: return TreeVfxRow.B16Ghost;
                case TreeProcKind.Leech: return TreeVfxRow.B15Thread;
                case TreeProcKind.UndyingOath: return TreeVfxRow.B17Pulse;
                case TreeProcKind.EndlessWindowOpen: return TreeVfxRow.T4Enso;
                case TreeProcKind.OverclockPeak: return TreeVfxRow.B12WhiteHot;
                case TreeProcKind.SynergyFirstTrigger: return TreeVfxRow.X8Enso;
                case TreeProcKind.ClusterSplit: return TreeVfxRow.B10MiniFlame;
                case TreeProcKind.ShieldUp: return TreeVfxRow.B14Dome;
                case TreeProcKind.ShieldHit: return TreeVfxRow.B14Hit;
                default: return TreeVfxRow.Count;
            }
        }
    }
}
