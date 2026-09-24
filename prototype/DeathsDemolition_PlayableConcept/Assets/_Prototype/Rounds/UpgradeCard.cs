using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>Which half of the offer a card belongs to. Purely a presentation grouping.</summary>
    public enum UpgradeCardKind
    {
        /// <summary>Piercing, Ricochet, Explosion, Chain. What a bullet does.</summary>
        WeaponCore = 0,

        /// <summary>Fire Rate, Reload Speed and the player-side branches. How the build feels.</summary>
        Utility = 1
    }

    /// <summary>
    /// How a <see cref="RoundUpgradeShop.TryPurchase(int,int)"/> call ended. Every value
    /// but <see cref="Purchased"/> leaves the wallet and the build exactly as they were.
    /// </summary>
    public enum UpgradePurchaseResult
    {
        /// <summary>No purchase attempted on this session yet.</summary>
        None = 0,

        Purchased = 1,

        /// <summary>The shop is closed. A callback that arrived after the screen went away.</summary>
        ShopClosed = 2,

        /// <summary>The caller quoted an older screen session (previous round's button).</summary>
        SessionMismatch = 3,

        /// <summary>Index out of range or a padding slot.</summary>
        InvalidCard = 4,

        /// <summary>The card was already bought on this screen (double click).</summary>
        AlreadyPurchased = 5,

        /// <summary>No armed player to apply the tier to. Nothing was spent.</summary>
        NoWeapon = 6,

        /// <summary>The live tier no longer matches the card; the offer was re-read.</summary>
        StaleOffer = 7,

        /// <summary>The branch is at its ceiling on the live state.</summary>
        BranchMaxed = 8,

        InsufficientGold = 9,

        /// <summary>Assertion branch: the tier did not move after a spend. Logged as an error.</summary>
        TierDidNotChange = 10,

        /// <summary>The wallet refused the spend for a reason other than the balance (no run open, duplicate id). Nothing was spent.</summary>
        WalletRefused = 11,

        /// <summary>WP-008 (DEC-048): this round's one purchase was already made; the offer is locked. Nothing was spent.</summary>
        PurchaseLimitReached = 12,

        /// <summary>WP-008: the caller quoted an older offer revision (a screen drawn before a reroll). Nothing was spent.</summary>
        StaleRevision = 13,

        /// <summary>WP-008: the offer is in its configuration-error state and has no cards. Nothing was spent.</summary>
        OfferUnavailable = 14
    }

    /// <summary>WP-008: a per-round right on the upgrade session (one purchase, one reroll).</summary>
    public enum UpgradeEntitlement
    {
        /// <summary>Not used yet on this round's offer.</summary>
        Available = 0,

        /// <summary>Consumed on this round's offer. Reopening, pausing or reconnecting does not restore it.</summary>
        Used = 1,

        /// <summary>Closed by another action (the reroll after a purchase). Never usable again this round.</summary>
        Locked = 2
    }

    /// <summary>WP-008: the state of the round-end offer as a whole.</summary>
    public enum UpgradeOfferStatus
    {
        Closed = 0,

        /// <summary>Three distinct, eligible cards are on the table.</summary>
        Ready = 1,

        /// <summary>Three valid cards could not be produced. No card is shown, nothing was charged, CONTINUE works.</summary>
        ConfigurationError = 2
    }

    /// <summary>
    /// How a <see cref="RoundUpgradeShop.TryReroll(int,int)"/> call ended (WP-008, DEC-050).
    /// Every value but <see cref="Rerolled"/> leaves the wallet, the cards and the reroll
    /// right exactly as they were.
    /// </summary>
    public enum UpgradeRerollResult
    {
        None = 0,

        Rerolled = 1,

        ShopClosed = 2,

        SessionMismatch = 3,

        StaleRevision = 4,

        /// <summary>The offer is in its configuration-error state.</summary>
        OfferUnavailable = 5,

        /// <summary>A purchase was already made this round; the offer is locked (decision 14).</summary>
        OfferLocked = 6,

        /// <summary>The one reroll of this round was already used (decision 13).</summary>
        RerollUsed = 7,

        /// <summary>RerollCost is not a positive number. A configuration problem, reported, never a free reroll.</summary>
        InvalidCost = 8,

        InsufficientGold = 9,

        /// <summary>No eligible branch exists outside the current offer to swap in. Nothing charged, right kept.</summary>
        NoReplacementAvailable = 10,

        /// <summary>The computed offer failed validation (assertion branch). Nothing charged, right kept.</summary>
        InvalidResult = 11,

        /// <summary>The wallet refused the spend for a reason other than the balance. Nothing changed.</summary>
        WalletRefused = 12
    }

    /// <summary>WP-008: why the reroll button is, or is not, pressable right now. The screen's label reads this.</summary>
    public enum RerollAvailability
    {
        /// <summary>Pressable: one reroll left, no purchase yet, alternatives exist, gold suffices.</summary>
        Available = 0,

        /// <summary>The shop is closed or the offer is in its error state.</summary>
        Unavailable = 1,

        /// <summary>Used this round.</summary>
        Used = 2,

        /// <summary>A purchase locked the offer.</summary>
        LockedByPurchase = 3,

        /// <summary>No eligible branch outside the offer to swap in.</summary>
        NoAlternatives = 4,

        /// <summary>Everything else is fine, the wallet is short.</summary>
        TooExpensive = 5
    }

    /// <summary>
    /// One card on the round-end screen, fully resolved. G-08 draws exactly these fields
    /// and never asks the config a question of its own.
    ///
    /// A struct, and handed out through a read-only list, so a HUD cannot write a price.
    /// </summary>
    public struct UpgradeCardOffer
    {
        public UpgradeBranch Branch;

        /// <summary>Card title, from EconomyConfigSection.Branches[].DisplayName.</summary>
        public string DisplayName;

        /// <summary>One line of what the next tier does, built from the live tuning values.</summary>
        public string Description;

        public UpgradeCardKind Kind;

        /// <summary>Tiers already owned this run. 0 on a first purchase.</summary>
        public int OwnedTier;

        /// <summary>The tier this card sells, i.e. OwnedTier + 1.</summary>
        public int NextTier;

        public int MaxTier;

        /// <summary>Price of this card, from UpgradeBranchSettings.CostForTier(OwnedTier).</summary>
        public int Cost;

        /// <summary>True once bought. The card stays on screen, greyed out, and the offer is locked (DEC-048).</summary>
        public bool Purchased;

        /// <summary>
        /// True for every card the shop hands out. Since WP-008 an offer is either three
        /// valid cards or none (decision 17); the flag stays so a default-constructed
        /// struct is never mistaken for a card.
        /// </summary>
        public bool IsValid;

        /// <summary>Wallet transaction id of the purchase once <see cref="Purchased"/>. 0 before.</summary>
        public int TransactionId;

        public bool IsMaxed => OwnedTier >= MaxTier;

        /// <summary>"Piercing II" - the label G-08 puts on the card face.</summary>
        public string TitleWithTier =>
            string.IsNullOrEmpty(DisplayName) ? "-" : DisplayName + " " + UpgradeCardCatalog.Roman(NextTier);
    }

    /// <summary>
    /// The static half of the card system: which branches are worth offering, what a tier
    /// reads as in English, and the one honesty check in this task.
    ///
    /// THE SILENT-CARD PROBLEM
    /// -----------------------
    /// <see cref="WeaponModifierState"/> stores a tier for all ten branches, and buying
    /// any of them is a legal call that raises no error. A branch whose tier no live system
    /// reads therefore sells for real gold and changes nothing, without producing a single
    /// warning. <see cref="s_unbacked"/> is the guard against that: a branch listed there is
    /// kept out of the draw, and the exclusion is announced once at run start.
    ///
    /// ALL TEN BRANCHES ARE BACKED TODAY, so the list is empty:
    ///
    ///     Piercing / Ricochet / Explosion / Chain -> ClassicWeaponRuntime.BuildRequest
    ///     FireRate                                -> ClassicWeaponRuntime, fire interval
    ///     ReloadSpeed                             -> ClassicWeaponRuntime, reload time
    ///     MovementSpeed                           -> PlayerMotor.ApplyConfig, top speed
    ///     MaximumHealth                           -> PlayerHealth.ApplyConfig, ceiling
    ///     RegenerationDelay                       -> PlayerHealth.ApplyConfig, regen wait
    ///     RegenerationRate                        -> PlayerHealth.ApplyConfig, regen speed
    ///
    /// The four player-side branches were held back until G-11 wired them through
    /// <c>PlayerUpgradeBinding</c> / <c>PlayerUpgradeTuning</c>; they are now in the pool,
    /// which is what the request asks for ("Utility prototype kartlari").
    ///
    /// KEEP THE MECHANISM. If a branch is ever added to the enum before something reads it,
    /// put it in <see cref="s_unbacked"/> the same day.
    /// <see cref="RoundUpgradeShop.OfferUnbackedBranches"/> (F1 toggle) then puts it back for
    /// feel-testing the card layout, with the card labelled NO EFFECT YET.
    /// </summary>
    public static class UpgradeCardCatalog
    {
        /// <summary>
        /// Branches no live system reads. EMPTY on purpose - see the class remarks. Adding a
        /// branch here removes it from the draw; that is the entire mechanism.
        /// </summary>
        private static readonly UpgradeBranch[] s_unbacked =
        {
            // WP-077: the three WP-069 §6 weapon-core branches exist in the data from now on, but their
            // behaviour (armour multiplier, stagger, bomblets) is WP-069 B2/B3 (S-02). Until those hooks
            // land they are held out of the draw so no card is sold that does nothing.
            UpgradeBranch.ArmorBreak, UpgradeBranch.Impact, UpgradeBranch.Cluster
        };

        private static readonly string[] s_roman = { "0", "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        /// <summary>False for a branch no live system reads. See the class remarks.</summary>
        public static bool HasLiveConsumer(UpgradeBranch branch)
        {
            for (int i = 0; i < s_unbacked.Length; i++)
            {
                if (s_unbacked[i] == branch)
                {
                    return false;
                }
            }

            // WP-034 (ADR-034-1): a branch the equipped weapon's roster entry does not carry
            // has no live consumer on THIS weapon. Installed by WeaponRosterSource; null =
            // pre-roster behaviour (every backed branch is offered).
            System.Func<UpgradeBranch, bool> gate = WeaponBranchGate;
            return gate == null || gate(branch);
        }

        /// <summary>WP-034 seam: per-weapon branch gate (roster). Null keeps every backed branch live.</summary>
        public static System.Func<UpgradeBranch, bool> WeaponBranchGate { get; set; }

        /// <summary>True while some branch is being held out of the draw. False today.</summary>
        public static bool HasUnbackedBranches => s_unbacked.Length > 0;

        /// <summary>Comma separated list of the branches that would do nothing. F1 readout.</summary>
        public static string DescribeUnbacked()
        {
            return s_unbacked.Length == 0 ? "none" : string.Join(", ", s_unbacked);
        }

        public static UpgradeCardKind KindOf(UpgradeBranch branch)
        {
            switch (branch)
            {
                case UpgradeBranch.Piercing:
                case UpgradeBranch.Ricochet:
                case UpgradeBranch.Explosion:
                case UpgradeBranch.Chain:
                case UpgradeBranch.ArmorBreak:
                case UpgradeBranch.Impact:
                case UpgradeBranch.Cluster:
                    return UpgradeCardKind.WeaponCore;
                default:
                    return UpgradeCardKind.Utility;
            }
        }

        public static string Roman(int value)
        {
            return value >= 0 && value < s_roman.Length ? s_roman[value] : value.ToString();
        }

        /// <summary>
        /// What buying this card changes, in one line, computed from the LIVE tuning rather
        /// than written out as prose. Dragging RicochetChancePerTier in the F1 panel
        /// changes the card text on the next round end, so the card can never lie.
        /// </summary>
        public static string Describe(UpgradeBranch branch, int ownedTier)
        {
            WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
            int next = ownedTier + 1;

            switch (branch)
            {
                case UpgradeBranch.Piercing:
                    return string.Format("Bullets pass through {0} more enemy per shot ({1} total), {2:0}% damage kept per pass.",
                        Mathf.Max(0, t.PierceTargetsPerTier),
                        next * Mathf.Max(0, t.PierceTargetsPerTier),
                        t.PierceDamageFalloff * 100f);

                case UpgradeBranch.Ricochet:
                {
                    float chance = Mathf.Clamp01(t.RicochetBaseChance + t.RicochetChancePerTier * (next - 1));
                    int bounces = 1 + (next - 1) / Mathf.Max(1, t.RicochetTiersPerExtraBounce);
                    return string.Format("{0:0}% of hits bounce to another enemy within {1:0.#} m, up to {2} time(s), at {3:0}% damage.",
                        chance * 100f, t.RicochetRadius, bounces, t.RicochetDamageScale * 100f);
                }

                case UpgradeBranch.Explosion:
                {
                    float chance = Mathf.Clamp01(t.ExplosionBaseChance + t.ExplosionChancePerTier * (next - 1));
                    float radius = t.ExplosionBaseRadius + t.ExplosionRadiusPerTier * (next - 1);
                    float scale = t.ExplosionBaseDamageScale + t.ExplosionDamageScalePerTier * (next - 1);
                    return string.Format("{0:0}% of hits detonate for {1:0.#} m at {2:0.##}x the hit's damage.",
                        chance * 100f, radius, scale);
                }

                case UpgradeBranch.Chain:
                    return string.Format("Damage jumps to {0} more enem(y/ies) within {1:0.#} m at {2:0}% damage, falling {3:0}% per jump.",
                        Mathf.Max(0, t.ChainTargetsPerTier), t.ChainRadius,
                        t.ChainDamageScale * 100f, (1f - t.ChainDamageFalloff) * 100f);

                case UpgradeBranch.FireRate:
                {
                    float total = Mathf.Min(1f + t.FireRateBonusPerTier * next, Mathf.Max(1f, t.MaximumFireRateMultiplier));
                    return string.Format("+{0:0}% fire rate ({1:0.00}x total, capped at {2:0.00}x).",
                        t.FireRateBonusPerTier * 100f, total, t.MaximumFireRateMultiplier);
                }

                case UpgradeBranch.ReloadSpeed:
                {
                    float total = Mathf.Max(t.MinimumReloadFraction, 1f - t.ReloadSpeedBonusPerTier * next);
                    return string.Format("-{0:0}% reload time ({1:0.00}x total, floor {2:0.00}x).",
                        t.ReloadSpeedBonusPerTier * 100f, total, t.MinimumReloadFraction);
                }

                // ---- player-side branches, read by PlayerMotor and PlayerHealth --------
                // Same rule as above: the text is computed from the live tuning, so the card
                // cannot describe an effect the player does not get.

                case UpgradeBranch.MovementSpeed:
                {
                    PlayerUpgradeTuning p = PlayerUpgradeTuning.Active;
                    float total = p.MoveSpeedMultiplier(next);
                    return string.Format("+{0:0}% movement speed ({1:0.00}x total, capped at {2:0.00}x).",
                        p.MoveSpeedBonusPerTier * 100f, total, p.MaximumMoveSpeedMultiplier);
                }

                case UpgradeBranch.MaximumHealth:
                {
                    PlayerUpgradeTuning p = PlayerUpgradeTuning.Active;
                    float total = p.MaxHealthMultiplier(next);
                    float maxHealth = PrototypeConfig.Active != null
                        ? PrototypeConfig.Active.Player.MaxHealth * total
                        : 0f;
                    return string.Format("+{0:0}% maximum health ({1:0.00}x total{2}). Current health keeps its share.",
                        p.MaxHealthBonusPerTier * 100f, total,
                        maxHealth > 0f ? string.Format(", {0:0} HP", maxHealth) : string.Empty);
                }

                case UpgradeBranch.RegenerationDelay:
                {
                    PlayerUpgradeTuning p = PlayerUpgradeTuning.Active;
                    float total = p.RegenDelayMultiplier(next);
                    float delay = PrototypeConfig.Active != null
                        ? PrototypeConfig.Active.Player.HealthRegenDelay * total
                        : 0f;
                    return string.Format("-{0:0}% wait before health regenerates ({1:0.00}x total{2}).",
                        p.RegenDelayReductionPerTier * 100f, total,
                        delay > 0f ? string.Format(", {0:0.0} s", delay) : string.Empty);
                }

                case UpgradeBranch.RegenerationRate:
                {
                    PlayerUpgradeTuning p = PlayerUpgradeTuning.Active;
                    float total = p.RegenRateMultiplier(next);
                    float rate = PrototypeConfig.Active != null
                        ? PrototypeConfig.Active.Player.HealthRegenRate * total
                        : 0f;
                    return string.Format("+{0:0}% health regeneration speed ({1:0.00}x total{2}).",
                        p.RegenRateBonusPerTier * 100f, total,
                        rate > 0f ? string.Format(", {0:0.#} HP/s", rate) : string.Empty);
                }

                default:
                    return HasLiveConsumer(branch)
                        ? "Improves " + branch + "."
                        : "NO EFFECT YET - no live system reads this branch.";
            }
        }
    }
}
