using System;
using System.Text;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-104 B4 (S-09) · the round-end tree screen's rows: the AD order (AD-VISUAL §5.1, AP-042 v002 tree-screen-layout.json
    /// row_order) - Shot · Speed · Magazine · Element · Chaos · Survival · Economy, so the likely synergy pairs sit next to each
    /// other (Shot–Speed, Element–Chaos). <see cref="TreeBranch"/>'s values are the contract's serialised ints (Shot 0,
    /// Magazine 1, Speed 2 …) and are NOT the screen order; the screen asks this table.
    /// </summary>
    public static class TreeV2Rows
    {
        public const int Count = 7;

        private static readonly TreeBranch[] s_order =
        {
            TreeBranch.Shot, TreeBranch.Speed, TreeBranch.Magazine, TreeBranch.Element, TreeBranch.Chaos, TreeBranch.Survival,
            TreeBranch.Economy
        };

        /// <summary>The branch drawn on row <paramref name="row"/> (0 = top).</summary>
        public static TreeBranch BranchAt(int row)
        {
            return s_order[Mathf.Clamp(row, 0, Count - 1)];
        }

        /// <summary>The row <paramref name="branch"/> is drawn on (an unknown value lands on the last row).</summary>
        public static int RowOf(TreeBranch branch)
        {
            for (int i = 0; i < s_order.Length; i++)
            {
                if (s_order[i] == branch)
                {
                    return i;
                }
            }

            return Count - 1;
        }
    }

    /// <summary>
    /// WP-104 B4 (S-09) · the tree screen's geometry, measured - never hard-placed per node. Reference: AP-042 v002
    /// <c>preview/tree-screen-layout.json</c> at 1920 × 1080 (header 96, branch labels 128 wide after a 48 px emblem, five tier
    /// columns of 208, the synergy column 184, the 400 px detail panel; a row is 96 tall, twice that when a tier cell holds three
    /// or four nodes; a node is an 88 px frame round a 64 px icon on a 96 px hit). Every node is placed from the snapshot alone:
    /// row = <see cref="TreeNodeView.Branch"/> (<see cref="TreeV2Rows"/>), column = <see cref="TreeNodeView.Tier"/>, place in the
    /// cell = <see cref="TreeNodeView.Slot"/> in reading order on a two-wide grid (1: centred; 2: side by side; 3: two over one;
    /// 4: two by two). A cell's node count sets its row's height (one unit per two-wide line).
    /// FITTING: the canvas is the UI's Expand canvas (≥ 1920 × 1080 at UI scale 1; 1600 × 900 at 1.2). The block is centred and at
    /// most 1920 wide (21:9 keeps the tree in the middle). When it does not fit, the synergy column (to 104) and the detail panel
    /// (to 352) give way first, then the whole tree scales (<see cref="Scale"/>, floor <see cref="MinScale"/>); spare height goes
    /// into the rows, at most +15 % each (AD §5.1), the rest stays below. Pure arithmetic - EditMode tested (TreeV2LayoutTests).
    /// Coordinates: block space, origin top-left, y down.
    /// </summary>
    public sealed class TreeV2Layout
    {
        public const float BlockMaxWidth = 1920f;
        public const float Margin = 24f;
        public const float HeaderHeight = 96f;
        /// <summary>The band at the header's foot that carries the tier numerals I–V over their columns.</summary>
        public const float TierBand = 28f;
        public const float EmblemSize = 48f;
        public const float LabelGap = 12f;
        public const float LabelWidthMin = 72f;
        public const float LabelWidthMax = 128f;
        public const float TierColumn = 208f;
        public const float SynergyColumn = 184f;
        public const float SynergyColumnMin = 104f;
        public const float DetailWidth = 400f;
        public const float DetailWidthMin = 352f;
        public const float ColumnGap = 16f;
        public const float DetailGap = 24f;
        public const float Unit = 96f;
        public const float Frame = 88f;
        public const float Icon = 64f;
        public const float NodeGap = 16f;
        public const float SynergyBadge = 64f;
        public const float SynergyPitch = 88f;
        public const float SynergyHeader = 28f;
        /// <summary>Spare height a row may take, as a share of its own height (AD §5.1: "+%15 tolerans").</summary>
        public const float RowStretch = 0.15f;
        /// <summary>Below this the tree would draw its 64 px icons under ~38 px: the layout says it does not fit.</summary>
        public const float MinScale = 0.6f;
        public const int Tiers = 5;

        private int[] _row = new int[0];
        private int[] _countInCell = new int[0];
        private Vector2[] _centre = new Vector2[0];
        private Vector2[] _synergy = new Vector2[0];
        private readonly float[] _rowY = new float[TreeV2Rows.Count];
        private readonly float[] _rowHeight = new float[TreeV2Rows.Count];
        private readonly int[] _rowUnits = new int[TreeV2Rows.Count];
        private readonly float[] _tierX = new float[Tiers];

        public Vector2 Canvas { get; private set; }
        /// <summary>The block's left edge in the canvas (the block is centred, ≤ <see cref="BlockMaxWidth"/>).</summary>
        public float BlockX { get; private set; }
        public float BlockWidth { get; private set; }
        public float Scale { get; private set; } = 1f;
        public bool Fits { get; private set; } = true;
        public float LabelWidth { get; private set; }
        public float LabelColumn { get; private set; }
        public float TierWidth { get; private set; }
        public float UnitSize { get; private set; }
        public float FrameSize { get; private set; }
        public float IconSize { get; private set; }
        public float HitSize { get; private set; }
        public float SynergyX { get; private set; }
        public float SynergyWidth { get; private set; }
        public float SynergyBadgeSize { get; private set; }
        public float DetailX { get; private set; }
        public float DetailW { get; private set; }
        public float TreeTop { get; private set; }
        public float TreeBottom { get; private set; }
        public int TotalUnits { get; private set; }
        public int NodeCount => _centre.Length;
        public int SynergyCount => _synergy.Length;

        public float TierX(int tier)
        {
            return _tierX[Mathf.Clamp(tier, 1, Tiers) - 1];
        }

        public float RowY(int row) => _rowY[Mathf.Clamp(row, 0, TreeV2Rows.Count - 1)];
        public float RowHeight(int row) => _rowHeight[Mathf.Clamp(row, 0, TreeV2Rows.Count - 1)];
        public int RowUnits(int row) => _rowUnits[Mathf.Clamp(row, 0, TreeV2Rows.Count - 1)];

        /// <summary>The row node <paramref name="index"/> is drawn on.</summary>
        public int RowOfNode(int index) => index >= 0 && index < _row.Length ? _row[index] : -1;

        /// <summary>How many nodes share node <paramref name="index"/>'s cell (branch × tier).</summary>
        public int CellCount(int index) => index >= 0 && index < _countInCell.Length ? _countInCell[index] : 0;

        /// <summary>Node <paramref name="index"/>'s centre (block space).</summary>
        public Vector2 Centre(int index) => index >= 0 && index < _centre.Length ? _centre[index] : Vector2.zero;

        /// <summary>Node <paramref name="index"/>'s hit box (block space, top-left origin).</summary>
        public Rect HitRect(int index)
        {
            Vector2 c = Centre(index);
            return new Rect(c.x - HitSize * 0.5f, c.y - HitSize * 0.5f, HitSize, HitSize);
        }

        public Vector2 SynergyCentre(int index) => index >= 0 && index < _synergy.Length ? _synergy[index] : Vector2.zero;

        /// <summary>The fog's left edge: the first column past <paramref name="openTier"/> (the block's tree end when every tier is open).</summary>
        public float FogX(int openTier)
        {
            int first = Mathf.Clamp(openTier, 0, Tiers);
            return first >= Tiers ? TierX(Tiers) + TierWidth : TierX(first + 1);
        }

        /// <summary>The fog's right edge: the last tier column's end.</summary>
        public float FogRight => TierX(Tiers) + TierWidth;

        /// <summary>
        /// Lays out <paramref name="snapshot"/> on a <paramref name="canvas"/>-sized canvas. <paramref name="labelWidth"/> is the
        /// widest branch label's widest word as drawn (the view measures it at the live text scale; labels wrap to two lines).
        /// Arrays are reused while the node and synergy counts hold.
        /// </summary>
        public void Compute(Vector2 canvas, TreeScreenSnapshot snapshot, float labelWidth)
        {
            Canvas = canvas;
            TreeNodeView[] nodes = snapshot != null && snapshot.Nodes != null ? snapshot.Nodes : new TreeNodeView[0];
            int synergies = snapshot != null && snapshot.Synergies != null ? snapshot.Synergies.Length : 0;
            if (_centre.Length != nodes.Length)
            {
                _centre = new Vector2[nodes.Length];
                _row = new int[nodes.Length];
                _countInCell = new int[nodes.Length];
            }

            if (_synergy.Length != synergies)
            {
                _synergy = new Vector2[synergies];
            }

            // Cells: how many nodes each (row, tier) holds - the highest slot + 1, so a gap in the slots keeps its place.
            var cells = new int[TreeV2Rows.Count * Tiers];
            for (int i = 0; i < nodes.Length; i++)
            {
                int row = TreeV2Rows.RowOf(nodes[i].Branch);
                int tier = Mathf.Clamp(nodes[i].Tier, 1, Tiers);
                _row[i] = row;
                int c = row * Tiers + tier - 1;
                cells[c] = Mathf.Max(cells[c], Mathf.Max(0, nodes[i].Slot) + 1);
            }

            TotalUnits = 0;
            for (int r = 0; r < TreeV2Rows.Count; r++)
            {
                int lines = 1;
                for (int t = 0; t < Tiers; t++)
                {
                    lines = Mathf.Max(lines, (cells[r * Tiers + t] + 1) / 2);
                }

                _rowUnits[r] = lines;
                TotalUnits += lines;
            }

            BlockWidth = Mathf.Min(canvas.x, BlockMaxWidth);
            BlockX = Mathf.Max(0f, (canvas.x - BlockWidth) * 0.5f);
            LabelWidth = Mathf.Clamp(labelWidth, LabelWidthMin, LabelWidthMax);
            LabelColumn = Margin + EmblemSize + LabelGap + LabelWidth + LabelGap;

            // Width: the synergy column and the detail panel give way first, then the tree scales.
            float synergy = SynergyColumn, detail = DetailWidth;
            float fixedWidth = LabelColumn + ColumnGap + DetailGap + Margin;
            float over = fixedWidth + Tiers * TierColumn + synergy + detail - BlockWidth;
            if (over > 0f)
            {
                float give = Mathf.Min(over, SynergyColumn - SynergyColumnMin);
                synergy -= give;
                over -= give;
                give = Mathf.Min(over, DetailWidth - DetailWidthMin);
                detail -= give;
            }

            float widthScale = (BlockWidth - fixedWidth - synergy - detail) / (Tiers * TierColumn);
            TreeTop = HeaderHeight;
            float room = canvas.y - HeaderHeight - Margin;
            float heightScale = room / Mathf.Max(1, TotalUnits * Unit);
            float scale = Mathf.Min(1f, Mathf.Min(widthScale, heightScale));
            Fits = scale >= MinScale - 1e-4f;
            Scale = Mathf.Max(MinScale, scale);

            UnitSize = Unit * Scale;
            FrameSize = Frame * Scale;
            IconSize = Icon * Scale;
            HitSize = UnitSize;
            TierWidth = TierColumn * Scale;

            // Rows: spare height stretches each row up to +15 %, the rest stays below the tree.
            float used = TotalUnits * UnitSize;
            float stretch = Mathf.Clamp((room - used) / Mathf.Max(1f, used), 0f, RowStretch);
            float y = TreeTop;
            for (int r = 0; r < TreeV2Rows.Count; r++)
            {
                _rowY[r] = y;
                _rowHeight[r] = _rowUnits[r] * UnitSize * (1f + stretch);
                y += _rowHeight[r];
            }

            TreeBottom = y;
            for (int t = 0; t < Tiers; t++)
            {
                _tierX[t] = LabelColumn + t * TierWidth;
            }

            SynergyX = LabelColumn + Tiers * TierWidth + ColumnGap;
            SynergyWidth = synergy;   // already given way to SynergyColumnMin when the width is short: it ends DetailGap before the panel
            DetailW = detail;
            DetailX = BlockWidth - Margin - DetailW;

            float pitch = FrameSize + NodeGap * Scale;
            for (int i = 0; i < nodes.Length; i++)
            {
                int row = _row[i];
                int tier = Mathf.Clamp(nodes[i].Tier, 1, Tiers);
                int count = cells[row * Tiers + tier - 1];
                _countInCell[i] = count;
                int slot = Mathf.Clamp(nodes[i].Slot, 0, Mathf.Max(0, count - 1));
                int cols = count <= 1 ? 1 : 2;
                int lines = Mathf.Max(1, (count + 1) / 2);
                int col = cols == 1 ? 0 : slot % 2;
                int line = slot / 2;
                float cx = _tierX[tier - 1] + TierWidth * 0.5f + (cols == 1 ? 0f : (col == 0 ? -0.5f : 0.5f) * pitch);
                float lineHeight = _rowHeight[row] / _rowUnits[row];
                float rowMid = _rowY[row] + _rowHeight[row] * 0.5f;
                float cy = rowMid + (line - (lines - 1) * 0.5f) * lineHeight;
                _centre[i] = new Vector2(cx, cy);
            }

            // Synergy column: a header band, then one badge per synergy on an even pitch inside the tree's height.
            float columnTop = TreeTop + SynergyHeader * Scale;
            float columnRoom = Mathf.Max(1f, TreeBottom - columnTop);
            float synergyPitch = synergies > 0 ? Mathf.Min(SynergyPitch * Scale, columnRoom / synergies) : 0f;
            SynergyBadgeSize = Mathf.Max(1f, Mathf.Min(SynergyBadge * Scale, synergyPitch - 8f));
            for (int s = 0; s < synergies; s++)
            {
                _synergy[s] = new Vector2(SynergyX + SynergyWidth * 0.5f, columnTop + synergyPitch * (s + 0.5f));
            }
        }
    }

    /// <summary>
    /// WP-104 B4 (S-09) · pad / keyboard navigation over the tree as a grid (AD §5.5): right / left walk the tiers along a
    /// branch, up / down walk the branches (the nearest node of the next row, the same tier first), the last tier's right edge
    /// steps into the synergy column, and the column's left steps back. Built from the layout's centres, so every node - fogged and
    /// locked ones too, they are information - is reachable and none is a dead end (A16, TreeV2NavTests). Targets: nodes are
    /// 0 … N−1, synergy badges N … N+S−1; −1 = no move (the view links its own controls there).
    /// </summary>
    public sealed class TreeV2Nav
    {
        public const int Up = 0, Down = 1, Left = 2, Right = 3;

        private int[,] _next = new int[0, 4];
        private int _nodes;
        private int _synergies;

        public int Count => _nodes + _synergies;
        public int NodeCount => _nodes;

        public int Next(int from, int direction)
        {
            return from >= 0 && from < Count && direction >= 0 && direction < 4 ? _next[from, direction] : -1;
        }

        public bool IsSynergy(int target) => target >= _nodes && target < Count;

        public void Build(TreeV2Layout layout)
        {
            _nodes = layout.NodeCount;
            _synergies = layout.SynergyCount;
            int count = Count;
            if (_next.GetLength(0) != count)
            {
                _next = new int[count, 4];
            }

            for (int i = 0; i < count; i++)
            {
                for (int d = 0; d < 4; d++)
                {
                    _next[i, d] = Find(layout, i, d);
                }
            }
        }

        private Vector2 At(TreeV2Layout layout, int target)
        {
            return target < _nodes ? layout.Centre(target) : layout.SynergyCentre(target - _nodes);
        }

        private int Find(TreeV2Layout layout, int from, int direction)
        {
            Vector2 a = At(layout, from);
            bool fromSynergy = from >= _nodes;
            int fromRow = fromSynergy ? -1 : layout.RowOfNode(from);
            float eps = 1f;
            int best = -1;
            float bestScore = float.MaxValue;
            int count = Count;
            for (int j = 0; j < count; j++)
            {
                if (j == from)
                {
                    continue;
                }

                bool synergy = j >= _nodes;
                Vector2 b = At(layout, j);
                float dx = b.x - a.x, dy = b.y - a.y;
                float score;
                switch (direction)
                {
                    case Right:
                        if (dx <= eps) continue;
                        if (fromSynergy) continue;   // the column's right is the view's (the detail panel)
                        if (synergy)
                        {
                            score = 100000f + dx + 3f * Mathf.Abs(dy);   // only when the row has nothing further right
                        }
                        else if (layout.RowOfNode(j) == fromRow)
                        {
                            score = dx + 3f * Mathf.Abs(dy);
                        }
                        else
                        {
                            continue;
                        }

                        break;
                    case Left:
                        if (dx >= -eps) continue;
                        if (fromSynergy)
                        {
                            if (synergy) continue;
                            score = 3f * Mathf.Abs(dy) - dx * 0.25f;   // the nearest node of the badge's height, rightmost first
                        }
                        else
                        {
                            if (synergy || layout.RowOfNode(j) != fromRow) continue;
                            score = -dx + 3f * Mathf.Abs(dy);
                        }

                        break;
                    case Down:
                        if (dy <= eps) continue;
                        if (synergy != fromSynergy) continue;
                        score = dy + 2.5f * Mathf.Abs(dx);
                        break;
                    default: // Up
                        if (dy >= -eps) continue;
                        if (synergy != fromSynergy) continue;
                        score = -dy + 2.5f * Mathf.Abs(dx);
                        break;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = j;
                }
            }

            return best;
        }

        /// <summary>
        /// LB / RB, Q / E: the first node of the branch <paramref name="step"/> rows away from <paramref name="currentRow"/>
        /// (wrapping): <paramref name="remembered"/>[row] when it holds a node of that row, else its first buyable node, else its
        /// leftmost. −1 when the snapshot has no node on that row.
        /// </summary>
        public static int BranchJump(TreeV2Layout layout, TreeScreenSnapshot snapshot, int currentRow, int step, int[] remembered)
        {
            if (snapshot == null || snapshot.Nodes == null || snapshot.Nodes.Length == 0)
            {
                return -1;
            }

            for (int k = 1; k <= TreeV2Rows.Count; k++)
            {
                int row = ((currentRow + step * k) % TreeV2Rows.Count + TreeV2Rows.Count) % TreeV2Rows.Count;
                int target = FirstOfRow(layout, snapshot, row, remembered);
                if (target >= 0)
                {
                    return target;
                }
            }

            return -1;
        }

        /// <summary>The row's remembered node, else its first buyable node (reading order), else its leftmost node; −1 if empty.</summary>
        public static int FirstOfRow(TreeV2Layout layout, TreeScreenSnapshot snapshot, int row, int[] remembered)
        {
            if (remembered != null && row >= 0 && row < remembered.Length)
            {
                int r = remembered[row];
                if (r >= 0 && r < snapshot.Nodes.Length && layout.RowOfNode(r) == row)
                {
                    return r;
                }
            }

            int first = -1, buyable = -1;
            for (int i = 0; i < snapshot.Nodes.Length; i++)
            {
                if (layout.RowOfNode(i) != row)
                {
                    continue;
                }

                if (first < 0 || Before(layout, i, first))
                {
                    first = i;
                }

                if (snapshot.Nodes[i].State == TreeNodeState.Available && (buyable < 0 || Before(layout, i, buyable)))
                {
                    buyable = i;
                }
            }

            return buyable >= 0 ? buyable : first;
        }

        private static bool Before(TreeV2Layout layout, int a, int b)
        {
            Vector2 pa = layout.Centre(a), pb = layout.Centre(b);
            return pa.x < pb.x - 0.5f || (Mathf.Abs(pa.x - pb.x) <= 0.5f && pa.y < pb.y);
        }
    }

    /// <summary>
    /// WP-104 B4 (S-09; after S-04 B5a: synergy arguments, the closer index, the Hearth caps) · every word the tree screen prints, from the snapshot and the string pack - never a sentence of the
    /// screen's own (O5: the numbers come from the snapshot, the words from <see cref="UiStrings"/>). Node and synergy words:
    /// <c>&lt;StringKey&gt;.name / .desc</c> (S-04's R4 string pack); a node whose name is not in the pack prints an em dash,
    /// never its id. Placeholders: <see cref="TreeNodeView.ArgNames"/> / <see cref="TreeNodeView.ArgValues"/> (display units,
    /// the contract's); a placeholder with no argument prints an em dash. Built on a refresh or a focus change, never per frame.
    /// </summary>
    public static class TreeV2Text
    {
        public const string Missing = "—";
        public const string Minus = "−";

        public static string Name(in TreeNodeView node)
        {
            return string.IsNullOrEmpty(node.StringKey) ? Missing : UiStrings.Get(node.StringKey + ".name", Missing);
        }

        public static string Description(in TreeNodeView node)
        {
            if (string.IsNullOrEmpty(node.StringKey))
            {
                return string.Empty;
            }

            return FillArgs(UiStrings.Get(node.StringKey + ".desc", string.Empty), node.ArgNames, node.ArgValues);
        }

        public static string SynergyName(in TreeSynergyView synergy)
        {
            return string.IsNullOrEmpty(synergy.StringKey) ? Missing : UiStrings.Get(synergy.StringKey + ".name", Missing);
        }

        public static string SynergyDescription(in TreeSynergyView synergy)
        {
            // B5a: the synergy view carries its own arguments (display units), as a node does.
            return string.IsNullOrEmpty(synergy.StringKey) ? string.Empty : FillArgs(UiStrings.Get(synergy.StringKey + ".desc", string.Empty), synergy.ArgNames, synergy.ArgValues);
        }

        /// <summary>
        /// "{name}" → the argument's value in the language's number form (≤ 1 decimal, trailing zero dropped, TR comma); an
        /// unknown or unfilled placeholder → an em dash (a brace never reaches the screen).
        /// </summary>
        public static string FillArgs(string text, string[] names, float[] values)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
            {
                return text ?? string.Empty;
            }

            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int close = c == '{' ? text.IndexOf('}', i + 1) : -1;
                if (close < 0)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                string name = text.Substring(i + 1, close - i - 1);
                sb.Append(ArgValue(name, names, values));
                i = close + 1;
            }

            return sb.ToString();
        }

        private static string ArgValue(string name, string[] names, float[] values)
        {
            if (names != null && values != null)
            {
                int n = Mathf.Min(names.Length, values.Length);
                for (int k = 0; k < n; k++)
                {
                    if (string.Equals(names[k], name, StringComparison.Ordinal))
                    {
                        return UiStrings.Number(values[k], "0.#");
                    }
                }
            }

            return Missing;
        }

        /// <summary>"{value} GOLD" / "{value} ALTIN".</summary>
        public static string Gold(int amount)
        {
            return UiStrings.Format("shop.gold_amount", "value", UiStrings.Number(Mathf.Max(0, amount)), "{value} GOLD");
        }

        /// <summary>"{value} ASH" / "{value} KÜL".</summary>
        public static string Ash(int amount)
        {
            return UiStrings.Format("awakening.ash_amount", "value", UiStrings.Number(Mathf.Max(0, amount)), "{value} ASH");
        }

        /// <summary>The strip's discount on a node's price, "−10%" / "−%10" (0 → empty).</summary>
        public static string Discount(in TreeNodeView node)
        {
            if (node.Cost <= 0 || node.Price >= node.Cost)
            {
                return string.Empty;
            }

            int percent = Mathf.RoundToInt((1f - node.Price / (float)node.Cost) * 100f);
            return percent > 0 ? UiStrings.Percent(Minus + UiStrings.Number(percent)) : string.Empty;
        }

        /// <summary>The tier word and numeral, "TIER III" / "KADEME III".</summary>
        public static string Tier(int tier)
        {
            return UiStrings.Get("tree.kind.Tier", "TIER") + " " + RomanNumerals.Cached(Mathf.Clamp(tier, 1, TreeV2Layout.Tiers));
        }

        public static string Branch(TreeBranch branch)
        {
            switch (branch)
            {
                case TreeBranch.Shot: return UiStrings.Get("tree.v2.branch.Shot", "SHOT");
                case TreeBranch.Magazine: return UiStrings.Get("tree.v2.branch.Magazine", "MAGAZINE");
                case TreeBranch.Speed: return UiStrings.Get("tree.v2.branch.Speed", "SPEED");
                case TreeBranch.Element: return UiStrings.Get("tree.v2.branch.Element", "ELEMENT");
                case TreeBranch.Chaos: return UiStrings.Get("tree.v2.branch.Chaos", "CHAOS");
                case TreeBranch.Survival: return UiStrings.Get("tree.v2.branch.Survival", "SURVIVAL");
                default: return UiStrings.Get("tree.v2.branch.Economy", "ECONOMY");
            }
        }

        /// <summary>"UNLOCKS AT ROUND n" / "n. ROUNDDA AÇILIR" (the fog line, <see cref="TreeScreenSnapshot.NextTierRound"/>).</summary>
        public static string Fog(int round)
        {
            return UiStrings.Format("tree.v2.screen.fog", "round", UiStrings.Number(Mathf.Max(1, round)), "UNLOCKS AT ROUND {round}");
        }

        /// <summary>The node's state as words (never colour alone): the detail panel's state line.</summary>
        public static string State(TreeScreenSnapshot snapshot, int index)
        {
            if (snapshot == null || snapshot.Nodes == null || index < 0 || index >= snapshot.Nodes.Length)
            {
                return string.Empty;
            }

            TreeNodeView n = snapshot.Nodes[index];
            switch (n.State)
            {
                case TreeNodeState.Fogged:
                    return UiStrings.Format("tree.v2.state.Fogged", "round", UiStrings.Number(Mathf.Max(1, n.RoundGate)), "UNLOCKS AT ROUND {round}");
                case TreeNodeState.PrereqMissing:
                    return UiStrings.Format("tree.v2.state.PrereqMissing", "node", UiStrings.Upper(MissingParentName(snapshot, index)), "NEEDS {node}");
                case TreeNodeState.Available:
                    return UiStrings.Get("tree.v2.state.Available", "AVAILABLE");
                case TreeNodeState.Unaffordable:
                    return UiStrings.Format("tree.v2.state.Unaffordable", "value", UiStrings.Number(Mathf.Max(0, n.Shortfall)), "{value} GOLD SHORT");
                case TreeNodeState.Purchased:
                    return UiStrings.Get("tree.v2.state.Purchased", "OWNED");
                case TreeNodeState.Excluded:
                    return UiStrings.Format("tree.v2.state.Excluded", "node", UiStrings.Upper(ClosedByName(snapshot, index)), "CLOSED BY {node}");
                case TreeNodeState.AwakeningLocked:
                    return UiStrings.Get("tree.v2.state.AwakeningLocked", "UNLOCKS IN AWAKENING");
                default:
                    // B1 appends EffectPending (7): no system reads the effect yet, so it is never sold.
                    return UiStrings.Get("tree.v2.state.EffectPending", "NOT IN THIS BUILD");
            }
        }

        /// <summary>The first parent not owned (any-parent nodes: the first listed), by name.</summary>
        public static string MissingParentName(TreeScreenSnapshot snapshot, int index)
        {
            int[] parents = snapshot.Nodes[index].ParentIndices;
            if (parents == null)
            {
                return Missing;
            }

            for (int p = 0; p < parents.Length; p++)
            {
                int k = parents[p];
                if (k >= 0 && k < snapshot.Nodes.Length && snapshot.Nodes[k].State != TreeNodeState.Purchased)
                {
                    return Name(snapshot.Nodes[k]);
                }
            }

            return Missing;
        }

        /// <summary>
        /// Who closed an Excluded node: B5a's <see cref="TreeNodeView.ClosedByIndex"/> (the closer; F1: the last element line taken);
        /// −1 falls back to the screen's reading - an owned node of the same fork group, else the owned node sharing its cell.
        /// </summary>
        public static string ClosedByName(TreeScreenSnapshot snapshot, int index)
        {
            TreeNodeView self = snapshot.Nodes[index];
            int closer = self.ClosedByIndex;
            if (closer >= 0 && closer < snapshot.Nodes.Length && closer != index)
            {
                return Name(snapshot.Nodes[closer]);
            }

            for (int k = 0; k < snapshot.Nodes.Length; k++)
            {
                TreeNodeView o = snapshot.Nodes[k];
                if (k == index || o.State != TreeNodeState.Purchased)
                {
                    continue;
                }

                bool sameFork = self.ForkGroup != 0 && o.ForkGroup == self.ForkGroup;
                bool sameCell = self.ForkGroup == 0 && o.Branch == self.Branch && o.Tier == self.Tier;
                if (sameFork || sameCell)
                {
                    return Name(o);
                }
            }

            return Missing;
        }

        /// <summary>The target line under the gold: "{value} GOLD TO TARGET" / "ENOUGH FOR TARGET" (empty without a Target).</summary>
        public static string TargetLine(TreeScreenSnapshot snapshot)
        {
            if (snapshot == null || snapshot.TargetIndex < 0)
            {
                return string.Empty;
            }

            int left = Mathf.Max(0, snapshot.TargetGoldRemaining - Mathf.Max(0, snapshot.Gold));
            return left <= 0
                ? UiStrings.Get("tree.v2.screen.target_ready", "ENOUGH FOR TARGET")
                : UiStrings.Format("tree.v2.screen.target_left", "value", UiStrings.Number(left), "{value} GOLD TO TARGET");
        }

        /// <summary>
        /// B5a · the Hearth's state beside its control: the price while it can be stoked, "THIS ROUND n/m" at the round's cap, "MAX"
        /// at the run's (<see cref="TreeScreenSnapshot.HearthPerRound"/> / <see cref="TreeScreenSnapshot.HearthMax"/>).
        /// </summary>
        public static string HearthState(TreeScreenSnapshot s)
        {
            if (s.HearthAvailable)
            {
                return Gold(s.HearthCost);
            }

            if (s.HearthMax > 0 && s.HearthStokesTotal >= s.HearthMax)
            {
                return UiStrings.Get("tree.state.max_tier", "MAX");
            }

            return UiStrings.Format("tree.state.hearth_round", "value", UiStrings.Number(s.HearthStokesThisRound), "max",
                UiStrings.Number(Mathf.Max(s.HearthStokesThisRound, s.HearthPerRound)), "THIS ROUND {value}/{max}");
        }

        /// <summary>"NEEDS {a} + {b}" for a synergy (Barrage's rocket condition is in its description).</summary>
        public static string SynergyRequires(TreeScreenSnapshot snapshot, in TreeSynergyView synergy)
        {
            string a = NodeNameAt(snapshot, synergy.NodeA), b = NodeNameAt(snapshot, synergy.NodeB);
            if (synergy.NodeB < 0)
            {
                return UiStrings.Upper(a);
            }

            return UiStrings.Format("tree.v2.screen.synergy_requires", "a", UiStrings.Upper(a), "b", UiStrings.Upper(b), "NEEDS {a} + {b}");
        }

        private static string NodeNameAt(TreeScreenSnapshot snapshot, int index)
        {
            return snapshot != null && snapshot.Nodes != null && index >= 0 && index < snapshot.Nodes.Length ? Name(snapshot.Nodes[index]) : Missing;
        }
    }
}
