using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 5 · the component kit sheet (direction §4, AD evidence): every v3 component
    /// in its states on one opaque page, built by <c>-ddUiCapture kit</c> over the main menu.
    /// <c>-ddUiKitPalette cb</c> and <c>-ddUiKitScale 1.25</c> give the {Default, CB} × {1.0, 1.25}
    /// set; the palette and scale are set for this process only (nothing is saved).
    /// Opt-in and capture-only: nothing in the game builds it.
    /// </summary>
    public static class UiKitSheet
    {
        // Column widths are the widths these components get on real screens: a settings row
        // (580), a pane's notice column (480), a HUD / Results readout (300), a tile (420).
        public const float ControlsWidth = 580f;
        public const float NoticesWidth = 480f;
        public const float ReadoutsWidth = 300f;
        public const float TilesWidth = 420f;
        public const float Gutter = 24f;
        public const float Margin = 40f;

        /// <summary>What the capture drives after the build: the closed modal and the controls it can focus.</summary>
        public struct Handles
        {
            public UiModal Modal;
            public UiButton Primary;
            public UiButton Switch;
            public UiButton Stepper;
        }

        /// <summary>Build the sheet; the capture focuses one control and opens the modal for the second frame.</summary>
        public static Handles Build()
        {
            var handles = new Handles();
            Canvas canvas = UiFactory.CreateCanvas("KitSheet", UiLayer.Modal, null, UiFactory.EnsureUiCamera());
            var root = (RectTransform)canvas.transform;
            Image page = UiFactory.NewImage("Page", root, UiColor.SurfaceBase, UiSprites.Solid);
            UiFactory.Fill((RectTransform)page.transform);

            var title = new Column(root, Margin, Margin, 1400f);
            title.Text("COMPONENT KIT", UiType.H1, UiColor.TextPrimary, 1400f);
            title.Text(string.Format("PALETTE {0} · TEXT SCALE {1:0.00} · REDUCED MOTION {2}",
                UiTokens.Palette == UiPalette.ColorBlindSafe ? "COLOUR-BLIND SAFE" : "DEFAULT", UiTokens.TextScale,
                UiMotion.ReducedMotion ? "ON" : "OFF"), UiType.Label, UiColor.TextSecondary, 1400f);
            float top = title.Y + (int)UiSpace.XL;

            // A · buttons and controls
            var a = new Column(root, Margin, top, ControlsWidth);
            a.Section("BUTTONS");
            UiButton primary = a.Control(UiFactory.Button("Primary", root, "START RUN", UiButtonVariant.Primary, null));
            a.Control(UiFactory.Button("Secondary", root, "SETTINGS", UiButtonVariant.Secondary, null));
            UiButton disabled = a.Control(UiFactory.Button("Disabled", root, "CONTINUE", UiButtonVariant.Secondary, null));
            disabled.interactable = false;
            a.Control(UiFactory.Button("Danger", root, "END RUN", UiButtonVariant.Danger, null));
            a.Section("SWITCHES & STEPPERS");
            handles.Switch = a.Control(UiSwitch.Create("SwitchOff", root, "REDUCE FLASHES", false, null).Button);
            a.Control(UiSwitch.Create("SwitchOn", root, "RUN TELEMETRY", true, null).Button);
            handles.Stepper = a.Control(UiStepper.Create("StepperEnd", root, "QUALITY", new[] { "LOW", "MEDIUM", "HIGH" }, 0, false, null).Button);
            a.Control(UiStepper.Create("StepperWrap", root, "PALETTE", new[] { "DEFAULT", "COLOUR-BLIND SAFE" }, 1, true, null).Button);

            // B · chips and notices
            float bx = Margin + ControlsWidth + Gutter;
            var b = new Column(root, bx, top, NoticesWidth);
            b.Section("CHIPS");
            b.Chips(UiChips.Create("Neutral", root, UiChipKind.Neutral, "LOCKED"),
                UiChips.Create("New", root, UiChipKind.New, "NEW"),
                UiChips.Create("Value", root, UiChipKind.Value, "LEVEL 3"));
            b.Chips(UiChips.Create("Status", root, UiChipKind.State, "NET", UiChipSize.Card, UiChipState.Status, "Status.Netted"),
                UiChips.Create("Selected", root, UiChipKind.Selected, "SELECTED"),
                UiChips.Create("Row", root, UiChipKind.Value, "x8", UiChipSize.Row));
            b.Chips(UiChips.Create("Warning", root, UiChipKind.State, "ELITE NEAR", UiChipSize.Hud, UiChipState.Warning, "Status.Dazed"),
                UiChips.Create("Danger", root, UiChipKind.State, "SURROUNDED", UiChipSize.Hud, UiChipState.Danger, "Status.Dazed"));
            b.Section("NOTICES");
            b.Block(UiNotices.Notice("Info", root, UiNoticeKind.Info, "Bindings are saved when you leave this page.", NoticesWidth));
            b.Block(UiNotices.Notice("Warning", root, UiNoticeKind.Warning,
                "Another copy of the game is using this profile, so it is read-only here.", NoticesWidth));
            b.Block(UiNotices.Notice("Error", root, UiNoticeKind.Error, "The offer could not be read. Nothing was spent.", NoticesWidth));

            // C · readouts
            float cx = bx + NoticesWidth + Gutter;
            var c = new Column(root, cx, top, ReadoutsWidth);
            c.Section("COUNTERS");
            c.Counter("GOLD", UiCounter.Create("Currency", root, UiCounterStyle.Currency, 1240, UiType.NumericL, UiColor.StateGold));
            c.Counter("KILLS", UiCounter.Create("Plain", root, UiCounterStyle.Plain, 386));
            c.Counter("AMMO", UiCounter.Create("Fraction", root, UiCounterStyle.Fraction, 24, UiType.NumericL, UiColor.TextPrimary, 30));
            c.Counter("TIME", UiCounter.Create("Timer", root, UiCounterStyle.Timer, 754));
            c.Section("RINGS & PIPS");
            UiRing reload = UiRing.Create("Reload", root, 64f, UiColor.BarReload);
            reload.Set(0.62f);
            UiRing special = UiRing.Create("Special", root, 132f, UiColor.BarSpecial);
            special.Set(0.35f);
            c.Pair((RectTransform)reload.transform, (RectTransform)special.transform);
            c.Block((RectTransform)UiPips.Create("Tier", root, 5, 2).transform);
            c.Block((RectTransform)UiPips.Create("Picks", root, 2, 1, 12f, 16f).transform);

            // D · hero tile and empty state
            var d = new Column(root, cx + ReadoutsWidth + Gutter, top, TilesWidth);
            d.Section("HERO TILE");
            d.Block((RectTransform)UiHeroTiles.Create("Hero", root, "BEST ROUND", 14).transform.parent.parent);
            d.Section("EMPTY STATE");
            RectTransform pane = UiFactory.FramedCard("EmptyPane", root, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
            pane.sizeDelta = new Vector2(TilesWidth, 300f);
            d.Block(pane);
            UiNotices.EmptyState("Empty", pane, "Hud.Special", "No runs yet. Your first run's numbers land here.", "START RUN", () => { });

            // The Confirm preset (S, height from content) with the shipped Pause wording.
            UiModal modal = UiModal.Confirm("KitModal", root, "END THIS RUN?",
                "Your kills, gold and time so far are recorded. The run cannot be resumed.",
                "END RUN", null, "KEEP PLAYING", null);
            handles.Modal = modal;
            handles.Primary = primary;
            return handles;
        }

        /// <summary>A top-down cursor for one column (reference px from the top-left).</summary>
        private struct Column
        {
            private readonly RectTransform _root;
            private readonly float _x;
            private readonly float _width;
            public float Y;

            public Column(RectTransform root, float x, float y, float width)
            {
                _root = root;
                _x = x;
                _width = width;
                Y = y;
            }

            private void Place(RectTransform rect, float x, float width, float height)
            {
                rect.SetParent(_root, false);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(width, height);
                rect.anchoredPosition = new Vector2(x, -Y);
            }

            public void Text(string text, UiType type, UiColor colour, float width)
            {
                TMP_Text label = UiFactory.Label("Text", _root, text, type, colour, TextAnchor.UpperLeft);
                float height = UiFactory.ScaledBand(UiFactory.LineHeight(type));
                Place(label.rectTransform, _x, width, height);
                Y += height + (int)UiSpace.XS;
            }

            public void Section(string title)
            {
                Y += (int)UiSpace.M;
                RectTransform heading = UiFactory.Heading(title, _root, UiType.H3);
                float height = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.H3));
                Place(heading, _x, _width, height);
                Y += height + (int)UiSpace.S;
            }

            public T Control<T>(T control) where T : Component
            {
                Place((RectTransform)control.transform, _x, _width, UiSize.Target);
                Y += UiSize.Target + (int)UiSpace.S;
                return control;
            }

            public void Chips(params TMP_Text[] labels)
            {
                float x = _x, height = 0f;
                for (int i = 0; i < labels.Length; i++)
                {
                    height = Mathf.Max(height, ((RectTransform)labels[i].transform.parent).sizeDelta.y);
                }

                float top = Y;
                for (int i = 0; i < labels.Length; i++)
                {
                    var chip = (RectTransform)labels[i].transform.parent;
                    Vector2 size = chip.sizeDelta;
                    Y = top + Mathf.Round((height - size.y) * 0.5f); // a row of mixed chip heights shares a centre line
                    Place(chip, x, size.x, size.y);
                    x += size.x + (int)UiSpace.S;
                }

                Y = top + height + (int)UiSpace.S;
            }

            public void Block(RectTransform rect)
            {
                Vector2 size = rect.sizeDelta;
                Place(rect, _x, size.x, size.y);
                Y += size.y + (int)UiSpace.S;
            }

            public void Pair(RectTransform left, RectTransform right)
            {
                Vector2 l = left.sizeDelta, r = right.sizeDelta;
                float height = Mathf.Max(l.y, r.y);
                Place(left, _x, l.x, l.y);
                left.anchoredPosition += new Vector2(0f, -(height - l.y) * 0.5f);
                Place(right, _x + l.x + (int)UiSpace.XL, r.x, r.y);
                Y += height + (int)UiSpace.S;
            }

            public void Counter(string caption, UiCounter counter)
            {
                float height = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.NumericL));
                TMP_Text label = UiFactory.Label("Caption", _root, caption, UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleLeft);
                Place(label.rectTransform, _x, 96f, height);
                Place((RectTransform)counter.transform, _x + 104f, _width - 104f, height);
                Y += height + (int)UiSpace.XS;
            }
        }
    }
}
