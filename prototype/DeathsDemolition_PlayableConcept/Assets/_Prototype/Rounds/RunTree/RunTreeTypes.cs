using System;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Rounds
{
    /// <summary>WP-077 (DEC-095): the two lanes of the in-run tree. LB/RB on the tree screen (S-09).</summary>
    public enum RunTreeLane
    {
        Weapon = 0,
        Body = 1
    }

    /// <summary>
    /// WP-077 K1 (user, 2026-09-24): how the round-end offer draws from the tree. Hybrid — three cards
    /// from the frontier + the pin (Nişan), reroll kept (DEC-047/050/087) — is the only playable model;
    /// FreeTree is kept as a named alternative for a later decision and currently behaves as Hybrid.
    /// </summary>
    public enum RunTreeOfferModel
    {
        Hybrid = 0,
        FreeTree = 1
    }

    /// <summary>
    /// WP-077 B2b: what the meta PATH's owned passives change in a run (read once at run start from the
    /// profile; the run never writes the profile). <see cref="None"/> = no meta effect (tests, no profile).
    /// </summary>
    public struct MetaRunModifiers
    {
        /// <summary>Reroll price override (−1 = config).</summary>
        public int RerollCostOverride;
        /// <summary>Hearth price step override (−1 = catalog 30).</summary>
        public int HearthCostStepOverride;
        /// <summary>The run starts with the Purse owned.</summary>
        public bool PurseAtStart;
        /// <summary>Capstone price discount share (0.10 = −10 %).</summary>
        public float CapstoneDiscount;
        /// <summary>K6: the weapon's mastery M3 lets its capstone into the offer (else only the pin brings it).</summary>
        public bool CapstoneMasteryMet;
        /// <summary>WP-077 B4 (armor_breaker_100): the ArmorBreak keystone is on the frontier from round 1 (no tier prerequisite).</summary>
        public bool ArmorBreakKeystoneOpen;

        public static MetaRunModifiers None => new MetaRunModifiers { RerollCostOverride = -1, HearthCostStepOverride = -1, CapstoneMasteryMet = true };
    }

    /// <summary>WP-077 node kinds (direction §2.1).</summary>
    public enum RunTreeNodeKind
    {
        /// <summary>An <see cref="UpgradeBranch"/> tier; bought again and again up to its MaxTier.</summary>
        Tier = 0,
        BranchKeystone = 1,
        Fork = 2,
        WeaponSeal = 3,
        Capstone = 4,
        SpecialLink = 5,
        /// <summary>The repeatable sink ("Ocak"): bought directly, never a card.</summary>
        Hearth = 6,
        Purse = 7
    }

    /// <summary>The seven node states of the S-09 tree screen (WP-068 R1; word + glyph, never colour alone).</summary>
    public enum RunTreeNodeStatus
    {
        Locked = 0,
        PrereqMissing = 1,
        Available = 2,
        Unaffordable = 3,
        Purchased = 4,
        Excluded = 5,
        MaxTier = 6
    }

    /// <summary>Why a node is <see cref="RunTreeNodeStatus.Locked"/>. The detail panel turns it into a sentence.</summary>
    public enum RunTreeLockReason
    {
        None = 0,
        /// <summary>The profile has not earned the branch yet (level ladder / meta PATH).</summary>
        ProfileLocked = 1,
        /// <summary>The equipped weapon does not carry this branch (roster / ADR-034-1).</summary>
        NotOnThisWeapon = 2,
        /// <summary>No live system reads the node's effect yet (RunTreeEffects); never offered or sold.</summary>
        EffectPending = 3,
        /// <summary>A Special link before any Special was picked up this run.</summary>
        NoSpecialYet = 4
    }

    /// <summary>Result of a tree command (UI → S-04). The screen maps each to a notice.</summary>
    public enum RunTreeCommandResult
    {
        Done = 0,
        UnknownNode = 1,
        NotAvailable = 2,
        InsufficientGold = 3,
        LimitReached = 4,
        NoRun = 5,
        WalletRefused = 6,
        /// <summary>K10: no purchase of this offer to undo (or it was already undone / a Reforge followed it).</summary>
        NothingToUndo = 7,
        /// <summary>K10: the screen closed the undo window (the countdown's last 3 s).</summary>
        UndoClosed = 8,
        /// <summary>K10: the run's one Reforge is spent.</summary>
        AlreadyUsed = 9,
        /// <summary>K10: Reforge opens from round 5.</summary>
        TooEarly = 10
    }

    /// <summary>One node of the catalog JSON (<c>Resources/ProgressionTree/run-tree.json</c>, dd-run-tree-v1).</summary>
    [Serializable]
    public sealed class RunTreeCatalogNode
    {
        public string id = string.Empty;
        /// <summary>≤ 12 characters: the one-line label under the 64 px seal (S-09 T3 truncation test).</summary>
        public string shortName = string.Empty;
        public string displayName = string.Empty;
        public string description = string.Empty;
        public string effectId = string.Empty;
        public int cost;
        public float offerWeight = 1f;
        public float effectValue;

        // Only on the entries that need them.
        public string branch = string.Empty;
        public string special = string.Empty;
        public int level;
        public int costStep;
        public int maxStacks;
        public int perRound;

        /// <summary>
        /// K10b: the node's named numbers — the R1 content pack placeholders ({value} {count} {seconds} {radius}
        /// {range} {max} {base}) in DISPLAY units (a percent as 20, not 0.2), plus runtime-only extras. One source
        /// for the card text and the effect hooks.
        /// </summary>
        public RunTreeCatalogArg[] args = new RunTreeCatalogArg[0];
    }

    [Serializable]
    public sealed class RunTreeCatalogArg
    {
        public string name = string.Empty;
        public float value;
    }

    [Serializable]
    public sealed class RunTreeCatalogWeapon
    {
        public string slot = string.Empty;
        public RunTreeCatalogNode seal = new RunTreeCatalogNode();
        public RunTreeCatalogNode forkX = new RunTreeCatalogNode();
        public RunTreeCatalogNode forkY = new RunTreeCatalogNode();
        public RunTreeCatalogNode capstone = new RunTreeCatalogNode();
    }

    [Serializable]
    public sealed class RunTreeCatalogDocument
    {
        public string schema = string.Empty;
        public RunTreeCatalogNode[] branchKeystones = new RunTreeCatalogNode[0];
        public RunTreeCatalogNode[] bodyForks = new RunTreeCatalogNode[0];
        public RunTreeCatalogNode bodyCapstone = new RunTreeCatalogNode();
        public RunTreeCatalogNode purse = new RunTreeCatalogNode();
        public RunTreeCatalogNode hearth = new RunTreeCatalogNode();
        public RunTreeCatalogNode[] specialLinks = new RunTreeCatalogNode[0];
        public RunTreeCatalogWeapon[] weapons = new RunTreeCatalogWeapon[0];
    }

    /// <summary>
    /// A node of one run's tree, built for the equipped weapon (<see cref="RunTreeGraph"/>).
    /// Immutable after the build; the run's ownership lives in <see cref="RunTreeState"/>.
    /// </summary>
    public sealed class RunTreeNode
    {
        public string Id;
        public string ShortName;
        public string DisplayName;
        public string Description;
        public string EffectId;
        public RunTreeLane Lane;
        /// <summary>Row on the 4 × 4 lane grid (0 top).</summary>
        public int Row;
        /// <summary>Column on the lane grid (0 = root tier … 3 = capstone).</summary>
        public int Column;
        public RunTreeNodeKind Kind;
        /// <summary>Valid for <see cref="RunTreeNodeKind.Tier"/> and <see cref="RunTreeNodeKind.BranchKeystone"/>.</summary>
        public UpgradeBranch Branch;
        public bool HasBranch;
        /// <summary>Indices into the graph's node list.</summary>
        public int[] Parents = new int[0];
        /// <summary>0 = none; nodes sharing a group exclude each other (fork pairs).</summary>
        public int ExclusionGroup;
        public int FlatCost;
        public int CostStep;
        public int MaxTier = 1;
        public int PerRoundLimit;
        public float OfferWeight = 1f;
        public float EffectValue;
        /// <summary>Special links: level 1 or 2 (the Special is the one picked up first in the run).</summary>
        public int LinkLevel;

        /// <summary>R1 content pack key stem (planning/content/r1-strings.json, name / desc × en / tr). Links resolve per Special (RunTreeState.StringKeyOf).</summary>
        public string StringKey = string.Empty;

        /// <summary>K10b: the catalog args (display units), parallel arrays; empty for tiers (their numbers come from the live tuning).</summary>
        public string[] ArgNames = Array.Empty<string>();
        public float[] ArgValues = Array.Empty<float>();

        /// <summary>The named arg in display units, else <paramref name="fallback"/>. No allocation.</summary>
        public float Arg(string name, float fallback = 0f) => RunTreeArgs.Find(ArgNames, ArgValues, name, fallback);
    }

    /// <summary>K10b: lookups over a node's parallel arg arrays (display units → runtime units).</summary>
    public static class RunTreeArgs
    {
        public static float Find(string[] names, float[] values, string name, float fallback)
        {
            if (names == null || values == null) return fallback;
            for (int i = 0; i < names.Length && i < values.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.Ordinal)) return values[i];
            }

            return fallback;
        }

        public static void Split(RunTreeCatalogArg[] args, out string[] names, out float[] values)
        {
            int n = args != null ? args.Length : 0;
            names = n == 0 ? Array.Empty<string>() : new string[n];
            values = n == 0 ? Array.Empty<float>() : new float[n];
            for (int i = 0; i < n; i++)
            {
                names[i] = args[i] != null ? args[i].name ?? string.Empty : string.Empty;
                values[i] = args[i] != null ? args[i].value : 0f;
            }
        }
    }
}
