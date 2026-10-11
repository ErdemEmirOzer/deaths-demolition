using System.Globalization;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-107 (R4) · the wall buy prompt at the bottom centre of the HUD: the Interact glyph (E / pad A), "Buy Hinawa · 250"
    /// or "Ammo · 125" and the koban; "Not enough gold", "Ammo full" or "Not while a Special is in hand" when the press would
    /// be refused (words + icon, never colour alone). A placeholder view S-09 restyles in WP-105. Event driven
    /// (<see cref="WallBuyEvents.PromptChanged"/>): no per-frame work, α in / out on the UiMotion presets, colours from the
    /// tokens.
    /// </summary>
    public sealed class HudWallBuyPrompt : MonoBehaviour
    {
        public const float GlyphSize = 30f, RowHeight = 40f, BottomOffset = 112f, CoinSize = 22f;

        private RectTransform _rect;
        private CanvasGroup _group;
        private UiPrompt _glyph;
        private TMP_Text _label;
        private Image _coin;
        private bool _shown, _hasCoin;
        private string _buy, _refill, _poor, _full, _special;

        /// <summary>The prompt's current words (test readout; empty while hidden).</summary>
        public string Text => _shown && _label != null ? _label.text : string.Empty;

        public bool Shown => _shown;

        public static HudWallBuyPrompt Create(RectTransform screen)
        {
            RectTransform rect = UiFactory.NewRect("WallBuyPrompt", screen);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, BottomOffset);
            rect.sizeDelta = new Vector2(560f, RowHeight);
            var view = rect.gameObject.AddComponent<HudWallBuyPrompt>();
            view.Build(rect);
            return view;
        }

        private void Build(RectTransform rect)
        {
            _rect = rect;
            _buy = UiStrings.Get("wallbuy.buy", "Buy {weapon} · {cost}");
            _refill = UiStrings.Get("wallbuy.refill", "Ammo · {cost}");
            _poor = UiStrings.Get("wallbuy.not_enough_gold", "Not enough gold");
            _full = UiStrings.Get("wallbuy.ammo_full", "Ammo full");
            _special = UiStrings.Get("wallbuy.special_active", "Not while a Special is in hand");

            Image back = UiFactory.NewImage("Back", rect, UiColor.SurfaceInset, UiSprites.Chip);
            back.type = Image.Type.Sliced;
            UiFactory.Fill((RectTransform)back.transform);

            _glyph = UiInputGlyphs.Build("Glyph", rect, UiPromptAction.Interact, UiColor.TextPrimary, UiColor.SurfaceInset, GlyphSize);
            var glyphRect = (RectTransform)_glyph.transform;
            glyphRect.anchorMin = glyphRect.anchorMax = new Vector2(0f, 0.5f);
            glyphRect.pivot = new Vector2(0f, 0.5f);
            glyphRect.anchoredPosition = new Vector2((int)UiSpace.S, 0f);

            _label = UiFactory.Label("Label", rect, string.Empty, UiType.Label, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            _label.enableWordWrapping = false;
            _label.overflowMode = TextOverflowModes.Overflow;
            RectTransform labelRect = _label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.offsetMin = new Vector2((int)UiSpace.S * 2 + GlyphSize, 0f);
            labelRect.offsetMax = new Vector2(-((int)UiSpace.S * 2 + CoinSize), 0f);

            // The AP-032 koban carries its own metal; tinted like the HUD's gold readout (TextPrimary), hidden without the art.
            Sprite koban = HudArt.GoldCoin(CoinSize);
            _coin = UiFactory.NewImage("Coin", rect, UiColor.TextPrimary, koban);
            _hasCoin = koban != null;
            _coin.preserveAspect = true;
            var coinRect = (RectTransform)_coin.transform;
            coinRect.anchorMin = coinRect.anchorMax = new Vector2(1f, 0.5f);
            coinRect.pivot = new Vector2(1f, 0.5f);
            coinRect.anchoredPosition = new Vector2(-(int)UiSpace.S, 0f);
            coinRect.sizeDelta = new Vector2(CoinSize, CoinSize);

            _group = UiFactory.Group(rect.gameObject);
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        private void OnEnable()
        {
            WallBuyEvents.PromptChanged += Apply;
            if (_label != null) Apply(WallBuyEvents.CurrentPrompt);
        }

        private void OnDisable()
        {
            WallBuyEvents.PromptChanged -= Apply;
        }

        private void Apply(WallBuyPrompt prompt)
        {
            if (_label == null) return;
            if (!prompt.Visible)
            {
                Show(false);
                return;
            }

            string cost = prompt.Cost.ToString(CultureInfo.InvariantCulture);
            string offer = prompt.Action == WallBuyAction.Refill
                ? _refill.Replace("{cost}", cost)
                : _buy.Replace("{weapon}", WeaponName(prompt.Weapon, prompt.WeaponKey)).Replace("{cost}", cost);
            bool canPress = prompt.Block == WallBuyBlock.None || prompt.Block == WallBuyBlock.NotEnoughGold;
            switch (prompt.Block)
            {
                case WallBuyBlock.NotEnoughGold: _label.text = UiStrings.Upper(offer + " · " + _poor); break;
                case WallBuyBlock.AmmoFull: _label.text = UiStrings.Upper(_full); break;
                case WallBuyBlock.SpecialActive: _label.text = UiStrings.Upper(_special); break;
                default: _label.text = UiStrings.Upper(offer); break;
            }

            _label.color = UiTokens.Get(prompt.Block == WallBuyBlock.None ? UiColor.TextPrimary : UiColor.TextSecondary);
            _glyph.gameObject.SetActive(canPress);
            _coin.enabled = _hasCoin && (prompt.Block == WallBuyBlock.None || prompt.Block == WallBuyBlock.NotEnoughGold);
            float width = (int)UiSpace.S * 4 + GlyphSize + CoinSize + Mathf.Ceil(_label.GetPreferredValues(_label.text).x);
            _rect.sizeDelta = new Vector2(width, RowHeight);
            float lift = RunHudScreen.BottomCentreTop > 0f ? RunHudScreen.BottomCentreTop + (int)UiSpace.S : BottomOffset;
            _rect.anchoredPosition = new Vector2(0f, Mathf.Max(BottomOffset, lift));
            Show(true);
        }

        private void Show(bool show)
        {
            if (show == _shown) return;
            _shown = show;
            UiMotion.KillTarget(_group, false);
            UiMotion.Alpha(_group, show ? 1f : 0f, UiDuration.Fast, show ? UiEase.Settle : UiEase.Exit, UiMotionClass.Decor);
        }

        private static string WeaponName(ClassicWeaponId id, string key)
        {
            WeaponDefinition def = Arsenal.Get(id);
            string name = def != null ? UiStrings.Get("weapon." + def.weaponId + ".name", null) : null;
            return !string.IsNullOrEmpty(name) ? name : def != null ? def.displayName : key;
        }
    }
}
