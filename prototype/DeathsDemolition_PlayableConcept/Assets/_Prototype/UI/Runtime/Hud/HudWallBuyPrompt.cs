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
    /// WP-107 (R4) · WP-107B · the wall buy prompt at the bottom centre of the HUD (S-09's rules, 12.10):
    /// - "[glyph] HINAWA · [koban 250]" on the AP-042 purchase frame; "[glyph] AMMO · [koban 125]" with the ammo icon on a
    ///   refill; a refused press on the AP-042 refused frame - the price dimmed and "NOT ENOUGH GOLD" with the crossed coin,
    ///   or "AMMO FULL" / "NOT WHILE A SPECIAL IS IN HAND" alone (words + icon, never colour alone);
    /// - gold (StateGold) only for money: the price digits; the weapon's name in TextPrimary; the HUD ink on every word;
    /// - the AP-042 price tag carries the coin in its left slice; the discount seal (AP-042 v002) rides right of it when the run
    ///   tree lowered the price;
    /// - event driven (<see cref="WallBuyEvents.PromptChanged"/>): the words are built only when the prompt changes, the chip is
    ///   measured to them (no ellipsis), α + a 6 px rise on the UiMotion presets in, α out; it lifts above the Special block.
    /// Without the AP-042 sprites it falls back to the kit chip and the AP-032 koban.
    /// </summary>
    public sealed class HudWallBuyPrompt : MonoBehaviour
    {
        public const float GlyphSize = 30f, RowHeight = 48f, BottomOffset = 112f, IconSize = 26f, TagHeight = 34f, SealSize = 34f, Rise = 6f;
        public const string FramePurchase = "ap042_wallbuy_frame_purchase", FrameRefused = "ap042_wallbuy_frame_insufficient",
            PriceTag = "ap042_wallbuy_price_tag", IconAmmo = "ap042_wallbuy_icon_ammo_64", IconRefused = "ap042_wallbuy_icon_insufficient_64",
            SealDiscount = "ap042_wallbuy_seal_discount";
        private const float FrameSourceHeight = 192f, TagSourceHeight = 160f, TagCoinSlice = 144f;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Image _back, _tag, _icon, _seal;
        private UiPrompt _glyph;
        private TMP_Text _name, _price, _note;
        private Sprite _framePurchase, _frameRefused, _tagSprite, _iconAmmo, _iconRefused, _sealSprite, _koban;
        private bool _shown;
        private Vector2 _home;
        private string _buy, _refill, _poor, _full, _special;

        /// <summary>The prompt's current words (test readout; empty while hidden).</summary>
        public string Text { get; private set; } = string.Empty;

        public bool Shown => _shown;

        /// <summary>The AP-042 frame is drawn (false: the kit chip fallback).</summary>
        public bool UsesAp042 => _framePurchase != null;

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

        private static Sprite Load(string file)
        {
            return Resources.Load<Sprite>(HudArt.Folder + file);
        }

        private void Build(RectTransform rect)
        {
            _rect = rect;
            _home = rect.anchoredPosition;
            _buy = UiStrings.Get("wallbuy.buy", "Buy {weapon} · {cost}");
            _refill = UiStrings.Get("wallbuy.refill", "Ammo · {cost}");
            _poor = UiStrings.Get("wallbuy.not_enough_gold", "Not enough gold");
            _full = UiStrings.Get("wallbuy.ammo_full", "Ammo full");
            _special = UiStrings.Get("wallbuy.special_active", "Not while a Special is in hand");
            _framePurchase = Load(FramePurchase);
            _frameRefused = Load(FrameRefused);
            _tagSprite = Load(PriceTag);
            _iconAmmo = Load(IconAmmo);
            _iconRefused = Load(IconRefused);
            _sealSprite = Load(SealDiscount);
            _koban = HudArt.GoldCoin(IconSize);

            _back = UiFactory.NewImage("Back", rect, _framePurchase != null ? UiColor.TextPrimary : UiColor.SurfaceInset,
                _framePurchase != null ? _framePurchase : UiSprites.Chip);
            _back.type = Image.Type.Sliced;
            if (_framePurchase != null) _back.pixelsPerUnitMultiplier = FrameSourceHeight / RowHeight;
            UiFactory.Fill((RectTransform)_back.transform);

            _glyph = UiInputGlyphs.Build("Glyph", rect, UiPromptAction.Interact, UiColor.TextPrimary, UiColor.SurfaceInset, GlyphSize);
            Left((RectTransform)_glyph.transform, GlyphSize, GlyphSize);

            _icon = UiFactory.NewImage("Icon", rect, UiColor.TextPrimary, null);
            _icon.preserveAspect = true;
            Left(_icon.rectTransform, IconSize, IconSize);

            _name = Label("Name", rect, UiType.H3, UiColor.TextPrimary);

            // The price: on the AP-042 tag (its coin in the left slice, the digits right of it) or the AP-032 koban + digits.
            _tag = UiFactory.NewImage("Tag", rect, UiColor.TextPrimary, _tagSprite != null ? _tagSprite : _koban);
            if (_tagSprite != null)
            {
                _tag.type = Image.Type.Sliced;
                _tag.pixelsPerUnitMultiplier = TagSourceHeight / TagHeight;
            }
            else
            {
                _tag.preserveAspect = true;
            }

            Left(_tag.rectTransform, TagHeight, TagHeight);
            _price = Label("Price", _tag.rectTransform, UiType.H3, UiColor.StateGold);

            _seal = UiFactory.NewImage("Seal", rect, UiColor.TextPrimary, _sealSprite);
            _seal.preserveAspect = true;
            _seal.raycastTarget = false;
            Left(_seal.rectTransform, SealSize, SealSize);

            _note = Label("Note", rect, UiType.Label, UiColor.TextSecondary);
            _note.fontSize = 16f;   // S-09: secondary words ≥ 16 px
            UiHudInk.Apply(_note);

            _group = UiFactory.Group(rect.gameObject);
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        private static TMP_Text Label(string labelName, RectTransform parent, UiType type, UiColor color)
        {
            TMP_Text t = UiFactory.Label(labelName, parent, string.Empty, type, color, TextAnchor.MiddleLeft);
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            UiHudInk.Apply(t);
            Left(t.rectTransform, 10f, RowHeight);
            return t;
        }

        private static void Left(RectTransform r, float width, float height)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.sizeDelta = new Vector2(width, height);
        }

        private void OnEnable()
        {
            WallBuyEvents.PromptChanged += Apply;
            if (_name != null) Apply(WallBuyEvents.CurrentPrompt);
        }

        private void OnDisable()
        {
            WallBuyEvents.PromptChanged -= Apply;
        }

        private void Apply(WallBuyPrompt prompt)
        {
            if (_name == null) return;
            if (!prompt.Visible)
            {
                Text = string.Empty;
                Show(false);
                return;
            }

            bool refused = prompt.Block != WallBuyBlock.None;
            bool priced = prompt.Block == WallBuyBlock.None || prompt.Block == WallBuyBlock.NotEnoughGold;
            string cost = prompt.Cost.ToString(CultureInfo.InvariantCulture);

            // The words: the offer's name half ("BUY HINAWA" / "AMMO") from the pack's line, the price apart (it sits on the tag).
            string offer = prompt.Action == WallBuyAction.Refill ? _refill : _buy.Replace("{weapon}", WeaponName(prompt.Weapon, prompt.WeaponKey));
            string name = priced ? offer.Replace("{cost}", string.Empty).TrimEnd(' ', '·') : string.Empty;
            string note = prompt.Block == WallBuyBlock.NotEnoughGold ? _poor : prompt.Block == WallBuyBlock.AmmoFull ? _full
                : prompt.Block == WallBuyBlock.SpecialActive ? _special : string.Empty;
            _name.text = UiStrings.Upper(name);
            _price.text = priced ? cost : string.Empty;
            _price.color = UiTokens.Get(prompt.Block == WallBuyBlock.None ? UiColor.StateGold : UiColor.TextSecondary);
            _note.text = UiStrings.Upper(note);
            // Alone, the refusal is the prompt's word (≥ 20 px, TextPrimary); beside a dimmed price it is the secondary line (16 px).
            _note.fontSize = priced ? 16f : UiTokens.FontSize(UiType.H3);
            _note.color = UiTokens.Get(priced ? UiColor.TextSecondary : UiColor.TextPrimary);
            UiHudInk.Apply(_note);
            Text = (_name.text + " " + _price.text + " " + _note.text).Trim();

            if (_framePurchase != null) _back.sprite = refused && _frameRefused != null ? _frameRefused : _framePurchase;
            Sprite icon = prompt.Block == WallBuyBlock.NotEnoughGold ? _iconRefused : prompt.Action == WallBuyAction.Refill && priced ? _iconAmmo : null;
            _icon.sprite = icon;
            _icon.enabled = icon != null;
            _glyph.gameObject.SetActive(priced);
            _tag.enabled = priced && _tag.sprite != null;
            _price.enabled = priced;
            _seal.enabled = priced && prompt.Discounted && _sealSprite != null;
            _note.enabled = note.Length > 0;
            Layout(priced);

            float lift = RunHudScreen.BottomCentreTop > 0f ? RunHudScreen.BottomCentreTop + (int)UiSpace.S : BottomOffset;
            _home = new Vector2(0f, Mathf.Max(BottomOffset, lift));
            Show(true);
        }

        /// <summary>Lay the pieces left to right at their measured widths and size the chip to them (state changes only).</summary>
        private void Layout(bool priced)
        {
            float gap = (int)UiSpace.S, x = gap * 1.5f;
            if (_glyph.gameObject.activeSelf) x = Place((RectTransform)_glyph.transform, x, GlyphSize) + gap;
            if (_icon.enabled) x = Place(_icon.rectTransform, x, IconSize) + gap * 0.5f;
            if (_name.text.Length > 0) x = Place(_name.rectTransform, x, Mathf.Ceil(_name.GetPreferredValues(_name.text).x)) + gap;
            if (priced)
            {
                float digits = Mathf.Ceil(_price.GetPreferredValues(_price.text).x);
                float coin = _tagSprite != null ? TagCoinSlice / (TagSourceHeight / TagHeight) : (_koban != null ? IconSize : 0f);
                float tagWidth = coin + digits + gap * 1.5f;
                RectTransform pr = _price.rectTransform;
                pr.anchoredPosition = new Vector2(coin, 0f);
                pr.sizeDelta = new Vector2(digits + gap, RowHeight);
                x = Place(_tag.rectTransform, x, _tagSprite != null ? tagWidth : coin);
                if (_tagSprite == null) pr.anchoredPosition = new Vector2(coin + gap * 0.5f, 0f);   // digits beside the koban
                x = (_tagSprite == null ? x + digits + gap : x) + gap * 0.5f;
                if (_seal.enabled) x = Place(_seal.rectTransform, x, SealSize) + gap * 0.5f;
            }

            if (_note.enabled) x = Place(_note.rectTransform, x, Mathf.Ceil(_note.GetPreferredValues(_note.text).x)) + gap;
            _rect.sizeDelta = new Vector2(x + gap * 0.5f, RowHeight);
        }

        private static float Place(RectTransform r, float x, float width)
        {
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(width, r.sizeDelta.y);
            return x + width;
        }

        private void Show(bool show)
        {
            if (show == _shown)
            {
                if (show) _rect.anchoredPosition = _home;
                return;
            }

            _shown = show;
            UiMotion.KillTarget(_rect, false);
            UiMotion.KillTarget(_group, false);
            if (show)
            {
                _rect.anchoredPosition = _home + new Vector2(0f, -Rise);
                UiMotion.Alpha(_group, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                UiMotion.Move(_rect, _home, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                return;
            }

            UiMotion.Alpha(_group, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
        }

        private static string WeaponName(ClassicWeaponId id, string key)
        {
            WeaponDefinition def = Arsenal.Get(id);
            string name = def != null ? UiStrings.Get("weapon." + def.weaponId + ".name", null) : null;
            return !string.IsNullOrEmpty(name) ? name : def != null ? def.displayName : key;
        }
    }
}
