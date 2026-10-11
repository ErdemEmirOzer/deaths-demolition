using DD.Prototype.Profile;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B1 (C-00 11.10 decision 2): which Awakening unlocks ("Uyanış", GD-TREE §3) the profile owns, read ONCE at run
    /// start by the run tree. Variety, never power: an alternative capstone is a side-grade, the forks widen F1/F2, the
    /// rest are information or a one-time refund. A plain value; the run never writes the profile.
    /// </summary>
    public struct TreeAwakening
    {
        /// <summary>aw_third: F1 allows three elements instead of two.</summary>
        public bool ThirdAffinity;
        /// <summary>aw_twincore: F2 allows Mini Nuke and Black Hole together (the second at reduced power).</summary>
        public bool TwinCores;
        /// <summary>aw_codex: locked synergies are previewed on the tree screen.</summary>
        public bool SynergyCodex;
        /// <summary>aw_headstart: round 1 starts with extra gold (Normal / Hard only - the tree applies the mode rule).</summary>
        public bool HeadStart;
        /// <summary>aw_rethink: one node per run can be refunded in full.</summary>
        public bool SecondThoughts;
        /// <summary>Bit per <see cref="TreeBranch"/> index: that branch's alternative capstone is unlocked (aw_alt_*).</summary>
        public byte AltCapstones;

        /// <summary>Nothing unlocked (no profile, a fresh profile, or a profile this build cannot read).</summary>
        public static TreeAwakening None => default;

        // Node id → what it opens. The ids are the catalog's (meta-tree.json, page "awakening"); an id this build does not
        // know is ignored, and a known id that the catalog no longer carries cannot be owned through a purchase.
        public const string AltShot = "aw_alt_sh";
        public const string AltMagazine = "aw_alt_mg";
        public const string AltSpeed = "aw_alt_sp";
        public const string AltElement = "aw_alt_el";
        public const string AltChaos = "aw_alt_kx";
        public const string AltSurvival = "aw_alt_sv";
        public const string AltEconomy = "aw_alt_ec";
        public const string ThirdAffinityId = "aw_third";
        public const string TwinCoresId = "aw_twincore";
        public const string SynergyCodexId = "aw_codex";
        public const string HeadStartId = "aw_headstart";
        public const string SecondThoughtsId = "aw_rethink";

        public bool HasAltCapstone(TreeBranch branch)
        {
            int i = (int)branch;
            return i >= 0 && i < 8 && (AltCapstones & (1 << i)) != 0;
        }

        /// <summary>
        /// The Awakening the profile owns (<see cref="ProfileData.MetaNodes"/>). Null or unknown → <see cref="None"/>.
        /// Run-start only (walks a short list, no allocation).
        /// </summary>
        public static TreeAwakening FromProfile(ProfileData profile)
        {
            TreeAwakening a = None;
            if (profile == null || profile.MetaNodes == null) return a;
            for (int i = 0; i < profile.MetaNodes.Count; i++)
            {
                switch (profile.MetaNodes[i])
                {
                    case AltShot: a.AltCapstones |= Bit(TreeBranch.Shot); break;
                    case AltMagazine: a.AltCapstones |= Bit(TreeBranch.Magazine); break;
                    case AltSpeed: a.AltCapstones |= Bit(TreeBranch.Speed); break;
                    case AltElement: a.AltCapstones |= Bit(TreeBranch.Element); break;
                    case AltChaos: a.AltCapstones |= Bit(TreeBranch.Chaos); break;
                    case AltSurvival: a.AltCapstones |= Bit(TreeBranch.Survival); break;
                    case AltEconomy: a.AltCapstones |= Bit(TreeBranch.Economy); break;
                    case ThirdAffinityId: a.ThirdAffinity = true; break;
                    case TwinCoresId: a.TwinCores = true; break;
                    case SynergyCodexId: a.SynergyCodex = true; break;
                    case HeadStartId: a.HeadStart = true; break;
                    case SecondThoughtsId: a.SecondThoughts = true; break;
                }
            }

            return a;
        }

        private static byte Bit(TreeBranch branch) => (byte)(1 << (int)branch);
    }
}
