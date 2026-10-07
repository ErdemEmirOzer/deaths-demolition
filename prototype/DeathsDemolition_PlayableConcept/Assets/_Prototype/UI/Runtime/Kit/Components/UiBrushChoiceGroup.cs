using System;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-098A (KIT-API §2, §4 "Choice group") · the C12 tab strip / C17 segment row: items in a row, ←/→ wired explicitly (no
    /// wrap), and ONE C8 (<see cref="UiBrushMark"/>, Selection) that moves under the choice. Selecting tints the items -
    /// TextPrimary for the choice, TextSecondary for the others (word and glyph) - and redraws the C8 under it (Reselect 400,
    /// a cut under Reduced Motion). The items' click closures are built once, at construction; Select / Under allocate
    /// nothing. No Update.
    ///
    /// USAGE: <see cref="UiBrushKit.Segments"/> (C17, labelled C11 segments) and <see cref="UiBrushKit.Tabs"/> (C12, icon tabs:
    /// Submit selects, focus never does - a pane is not rebuilt on every arrow). Wire the rows above and below with
    /// <see cref="LinkVertical"/>.
    /// DON'T: add a second selection mark or an underline per item, colour the choice gold, or select on focus.
    /// ACCESSIBILITY: the choice is shape (the C8 under it) + luminance (TextPrimary vs TextSecondary, ≥ 5.10:1 on the
    /// window ink) - never hue alone; every item is a ≥ 48 px target with its own reader name.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiBrushChoiceGroup : MonoBehaviour
    {
        private UiButton[] _items;
        private UiIconButton[] _tabs;
        private UiBrushMark _mark;
        private Action<int> _onSelect;
        private string _eventName;
        private int _selected = -1;

        public int Count
        {
            get { return _items != null ? _items.Length : 0; }
        }

        public int Selected
        {
            get { return _selected; }
        }

        public UiButton this[int index]
        {
            get { return _items != null && index >= 0 && index < _items.Length ? _items[index] : null; }
        }

        /// <summary>The C12 tab at <paramref name="index"/>; null in a segment row.</summary>
        public UiIconButton Tab(int index)
        {
            return _tabs != null && index >= 0 && index < _tabs.Length ? _tabs[index] : null;
        }

        /// <summary>The group's one C8.</summary>
        public UiBrushMark Mark
        {
            get { return _mark; }
        }

        /// <summary>
        /// Kit-only builder (<see cref="UiBrushKit.Segments"/>, <see cref="UiBrushKit.Tabs"/>): <paramref name="items"/> already
        /// laid out in a row under <paramref name="root"/> (<paramref name="tabs"/> parallel to them for C12, else null). Wires
        /// each item's click to <see cref="Select"/>(i, true), the row's ←/→ (explicit, no wrap), builds the one C8 and lands it
        /// under <paramref name="selected"/>.
        /// </summary>
        internal static UiBrushChoiceGroup Build(RectTransform root, UiButton[] items, UiIconButton[] tabs, int selected, Action<int> onSelect)
        {
            var group = root.gameObject.AddComponent<UiBrushChoiceGroup>();
            group._items = items ?? new UiButton[0];
            group._tabs = tabs;
            group._onSelect = onSelect;
            group._eventName = root.name;

            for (int i = 0; i < group._items.Length; i++)
            {
                UiButton item = group._items[i];
                if (item == null)
                {
                    continue;
                }

                int index = i;
                item.SetSegment(true);   // Brush: the word / glyph tone follows the choice
                item.SetOnClick(() => group.Select(index, true));

                UnityEngine.UI.Navigation nav = item.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                nav.selectOnLeft = i > 0 ? group._items[i - 1] : null;
                nav.selectOnRight = i < group._items.Length - 1 ? group._items[i + 1] : null;
                item.navigation = nav;
            }

            group._mark = UiBrushMark.Create(UiBrushMarkKind.Selection, root, "SelectionMark");
            group._mark.Rect.SetAsLastSibling();
            group.Select(selected, false);
            return group;
        }

        /// <summary>Make <paramref name="index"/> the choice: tints, C8 redraws under it; <paramref name="notify"/> → onSelect + UiEvent.ChoiceSelected.</summary>
        public void Select(int index, bool notify)
        {
            if (_items == null || _items.Length == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, _items.Length - 1);
            bool changed = index != _selected;
            bool first = _selected < 0;
            _selected = index;

            for (int i = 0; i < _items.Length; i++)
            {
                bool chosen = i == index;
                UiIconButton tab = _tabs != null && i < _tabs.Length ? _tabs[i] : null;
                if (tab != null)
                {
                    tab.SetSelected(chosen);
                }
                else if (_items[i] != null)
                {
                    _items[i].SetSelectedChoice(chosen);
                }
            }

            if (_mark != null && _items[index] != null)
            {
                _mark.Under((RectTransform)_items[index].transform, changed && !first);
            }

            if (notify)
            {
                UiEvents.Raise(UiEvent.ChoiceSelected, _eventName);
                if (_onSelect != null)
                {
                    _onSelect(index);
                }
            }
        }

        /// <summary>Explicit up / down for every item (the neighbours above and below the row); null leaves that side as it was.</summary>
        public void LinkVertical(Selectable up, Selectable down)
        {
            if (_items == null)
            {
                return;
            }

            for (int i = 0; i < _items.Length; i++)
            {
                UiInput.LinkVertical(_items[i], up, down);
            }
        }
    }
}
