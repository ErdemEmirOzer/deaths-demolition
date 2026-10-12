using System;
using System.Collections.Generic;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>The Awakening page's reading of one node (words and state are the source's; the page draws them).</summary>
    public enum AwakeningEntryState
    {
        /// <summary>Not in this build's catalog yet (before S-04 B1): drawn with its icon and an em dash, nothing to unlock.</summary>
        Pending = 0,
        Locked = 1,
        Available = 2,
        Short = 3,
        Unlocked = 4
    }

    /// <summary>One Awakening node on the page: its catalog id, words stem, place on the 6 × 3 grid, Ash price and state.</summary>
    public struct AwakeningEntry
    {
        public string Id;
        public string StringKey;
        public int Row;
        public int Column;
        public int Cost;
        public AwakeningEntryState State;
    }

    /// <summary>
    /// WP-104 (S-09) · the Awakening page's data seam (KARAR-WP104-11.10 decision 2): what the page shows and the one command it
    /// sends. S-04 owns the rules (B1a: meta-tree.json's Awakening page; the command is <c>ProfileService.TryBuyMetaNode</c>,
    /// S-04 12.10). <see cref="CanUnlock"/> false = the read-only page (a build without the Awakening catalog, or purchases off).
    /// </summary>
    public interface IAwakeningSource
    {
        bool CanUnlock { get; }
        void Read(AwakeningPageModel into);
        /// <summary>Unlocks <paramref name="id"/> with Ash; false with the reason's words in <paramref name="refusal"/>.</summary>
        bool TryUnlock(string id, out string refusal);
    }

    /// <summary>
    /// WP-104 (S-09) · the page's model: the twelve Awakening nodes in GD-TREE §3's order on the PATH page grid (6 × 3, the
    /// B1a catalog's rows / columns), the Ash balance, and whether UNLOCK is live. Built on open and after a command, never per
    /// frame. The fixed list below is only the page's LAYOUT (which node sits where, so the read-only page before B1 still shows
    /// the twelve AP-042 icons); price, state and words always come from the source.
    /// </summary>
    public sealed class AwakeningPageModel
    {
        /// <summary>The AP-042 Awakening ids in grid order: seven alternative capstones, two fork widenings, three aids.</summary>
        public static readonly string[] LayoutIds =
        {
            "aw_alt_sh", "aw_alt_mg", "aw_alt_sp", "aw_alt_el", "aw_alt_kx", "aw_alt_sv",
            "aw_alt_ec", "aw_third", "aw_twincore",
            "aw_codex", "aw_headstart"   // WP-112 (C-00 12.10): aw_rethink retired for R4; S-09 re-arranges the 11 cells
        };

        private static readonly int[] s_row = { 0, 0, 0, 0, 0, 0, 1, 1, 1, 2, 2 };
        private static readonly int[] s_column = { 0, 1, 2, 3, 4, 5, 0, 3, 4, 0, 1 };

        public const int Columns = 6;
        public const int Rows = 3;

        public readonly List<AwakeningEntry> Entries = new List<AwakeningEntry>(12);
        public int AshBalance;
        public bool CanUnlock;
        /// <summary>True when the catalog carried none of the nodes (the build before B1): the page shows its pending line.</summary>
        public bool Pending;

        /// <summary>Resets to the layout's twelve nodes, all <see cref="AwakeningEntryState.Pending"/>.</summary>
        public void ResetToLayout()
        {
            Entries.Clear();
            for (int i = 0; i < LayoutIds.Length; i++)
            {
                Entries.Add(new AwakeningEntry { Id = LayoutIds[i], Row = s_row[i], Column = s_column[i], State = AwakeningEntryState.Pending });
            }

            Pending = true;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (string.Equals(Entries[i].Id, id, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The node's name, or an em dash when the pack has none (never its id).</summary>
        public static string Name(in AwakeningEntry entry)
        {
            return string.IsNullOrEmpty(entry.StringKey) ? TreeV2Text.Missing : UiStrings.Get(entry.StringKey + ".name", TreeV2Text.Missing);
        }

        public static string Description(in AwakeningEntry entry)
        {
            return string.IsNullOrEmpty(entry.StringKey) ? string.Empty : UiStrings.Get(entry.StringKey + ".desc", string.Empty);
        }

        /// <summary>The state's words: UNLOCKED, "{value} ASH SHORT", the price, or the pending line.</summary>
        public static string StateLine(in AwakeningEntry entry, int balance)
        {
            switch (entry.State)
            {
                case AwakeningEntryState.Unlocked:
                    return UiStrings.Get("awakening.state.unlocked", "UNLOCKED");
                case AwakeningEntryState.Short:
                    return UiStrings.Format("awakening.state.short", "value", UiStrings.Number(Mathf.Max(0, entry.Cost - balance)), "{value} ASH SHORT");
                case AwakeningEntryState.Available:
                case AwakeningEntryState.Locked:
                    return TreeV2Text.Ash(entry.Cost);
                default:
                    return TreeV2Text.Missing;
            }
        }

        /// <summary>The tile's lock glyph (a node not yet affordable or not yet open).</summary>
        public const string LockGlyph = "State.Locked";

        /// <summary>The tile's unlocked glyph.</summary>
        public const string UnlockedGlyph = "State.Unlocked";

        /// <summary>
        /// fix2 · the grid tile's short line (the detail keeps the full <see cref="StateLine"/>): the price for every node still to
        /// open, UNLOCKED once open, an em dash when pending. "300 ASH SHORT" (and its Turkish line) is a sentence that depends on the
        /// balance, so it stands in the detail beside UNLOCK; the tile says what the node costs and, with
        /// <see cref="TileGlyph"/>, whether it can be bought now.
        /// </summary>
        public static string TileLine(in AwakeningEntry entry)
        {
            switch (entry.State)
            {
                case AwakeningEntryState.Unlocked:
                    return UiStrings.Get("awakening.state.unlocked", "UNLOCKED");
                case AwakeningEntryState.Short:
                case AwakeningEntryState.Available:
                case AwakeningEntryState.Locked:
                    return TreeV2Text.Ash(entry.Cost);
                default:
                    return TreeV2Text.Missing;
            }
        }

        /// <summary>The tile's glyph: the lock while the node cannot be bought now (Ash short, locked), the open mark once unlocked.</summary>
        public static string TileGlyph(in AwakeningEntry entry)
        {
            switch (entry.State)
            {
                case AwakeningEntryState.Unlocked:
                    return UnlockedGlyph;
                case AwakeningEntryState.Short:
                case AwakeningEntryState.Locked:
                    return LockGlyph;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The base node an alternative capstone stands in for (GD-TREE §3: Shot / Magazine / Speed / Element / Chaos / Survival V,
        /// Economy IV) - its run-tree id, whose name the "ALTERNATIVE TO …" line prints; null for the other Awakening nodes.
        /// </summary>
        public static string AlternativeOf(string id)
        {
            switch (id)
            {
                case "aw_alt_sh": return "sh_cap";
                case "aw_alt_mg": return "mg_cap";
                case "aw_alt_sp": return "sp_cap";
                case "aw_alt_el": return "el_cap";
                case "aw_alt_kx": return "kx_cap";
                case "aw_alt_sv": return "sv_cap";
                case "aw_alt_ec": return "ec_midas";
                default: return null;
            }
        }

        /// <summary>The base capstone's name (S-04's run-tree words "tree.v2.&lt;id&gt;.name"), an em dash when absent.</summary>
        public static string AlternativeName(string id)
        {
            string baseId = AlternativeOf(id);
            return baseId == null ? null : UiStrings.Get("tree.v2." + baseId + ".name", TreeV2Text.Missing);
        }
    }

    /// <summary>
    /// WP-104 (S-09) · the Awakening page's source in the game: S-04's meta catalog (<see cref="MetaPathCatalog"/>, B1a's
    /// Awakening page: nodes whose effect id starts with <see cref="EffectPrefix"/>) and the live profile (Ash balance, owned
    /// nodes, <see cref="MetaPathRules.Status"/>). UNLOCK → <see cref="ProfileService.TryBuyMetaNode"/> when constructed with
    /// purchases on (<see cref="UiAwakeningRoute.PurchaseEnabled"/>, on by default); the page never decides a price or a state.
    /// Words: the catalog's <c>StringKey</c> - B1a's <c>tree.meta.awakening.*</c> (the page's context; <c>tree.v2.aw_alt_*</c> are the
    /// in-run alternative capstones' words, another context).
    /// </summary>
    public sealed class ProfileAwakeningSource : IAwakeningSource
    {
        /// <summary>B1a's <see cref="MetaPathEffects.AwakeningPrefix"/>.</summary>
        public const string EffectPrefix = MetaPathEffects.AwakeningPrefix;

        private readonly bool _canUnlock;

        public ProfileAwakeningSource(bool canUnlock)
        {
            _canUnlock = canUnlock;
        }

        public bool CanUnlock => _canUnlock;

        public void Read(AwakeningPageModel into)
        {
            into.ResetToLayout();
            ProfileData profile = ProfileService.Current;
            into.AshBalance = profile != null ? profile.AshBalance : 0;
            into.CanUnlock = false;
            IReadOnlyList<MetaPathNode> nodes = MetaPathCatalog.Nodes;
            bool any = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                MetaPathNode n = nodes[i];
                if (n == null || n.EffectId == null || !n.EffectId.StartsWith(EffectPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                int at = into.IndexOf(n.Id);
                AwakeningEntry e = at >= 0 ? into.Entries[at] : new AwakeningEntry { Id = n.Id, Row = n.Row, Column = n.Column };
                e.StringKey = n.StringKey;
                e.Cost = n.AshCost;
                e.Row = n.Row;
                e.Column = n.Column;
                e.State = profile == null ? AwakeningEntryState.Locked : Map(MetaPathRules.Status(profile, i, out _, out _, out _));
                if (at >= 0)
                {
                    into.Entries[at] = e;
                }
                else
                {
                    into.Entries.Add(e);
                }

                any = true;
            }

            into.Pending = !any;
            into.CanUnlock = any && _canUnlock;
        }

        private static AwakeningEntryState Map(RunTreeNodeStatus status)
        {
            switch (status)
            {
                case RunTreeNodeStatus.Purchased: return AwakeningEntryState.Unlocked;
                case RunTreeNodeStatus.Available: return AwakeningEntryState.Available;
                case RunTreeNodeStatus.Unaffordable: return AwakeningEntryState.Short;
                default: return AwakeningEntryState.Locked;
            }
        }

        public bool TryUnlock(string id, out string refusal)
        {
            refusal = string.Empty;
            if (!_canUnlock)
            {
                refusal = UiStrings.Get("awakening.pending", "Not in this build yet. Your Ash is kept.");
                return false;
            }

            MetaCommandResult result = ProfileService.TryBuyMetaNode(id);
            if (result == MetaCommandResult.Done)
            {
                return true;
            }

            refusal = result == MetaCommandResult.InsufficientAsh
                ? UiStrings.Get("awakening.state.short_sentence", "Not enough Ash for that yet. Nothing was spent.")
                : UiStrings.Get("awakening.refuse", "That can't be unlocked right now. Nothing was spent.");
            return false;
        }
    }
}
