using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 2 · the preset layer (direction §3 "preset'ler tek giriş"). Screens ask
    /// for a named motion; the preset owns the channels, tokens, curve and MotionClass, so
    /// timing and the Reduced Motion / flash policy live in one place. Raw durations,
    /// Lerp / MoveTowards / unscaledDeltaTime in Screens/ are ratcheted down by the T6 test.
    ///
    /// Presets that need components from later slices (CardDeal / CardWipe with the shop
    /// card, BannerReveal / RibbonIn with the banner, InkBleed / Glint with the DD/UI/Fx
    /// shader, CurtainClose / Open, ParallaxFollow, AmbientParticles, RegenTrack) are added
    /// by those slices on the same pattern.
    /// </summary>
    public static class UiPresets
    {
        /// <summary>Screen / pane content rise-and-settle distance and exit push (px).</summary>
        public const float Travel = 8f;

        /// <summary>Modal entry scale (0.96 → 1).</summary>
        public const float ModalFromScale = 0.96f;

        /// <summary>Press depth (scale while held).</summary>
        public const float PressScale = 0.98f;

        /// <summary>EnterScreen: 280 ms Settle fade + 8 px rise (Decor).</summary>
        public static void EnterScreen(CanvasGroup group, RectTransform rect, int owner = 0)
        {
            if (group != null)
            {
                group.alpha = 0f;
                UiMotion.Alpha(group, 1f, UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
            }

            if (rect != null)
            {
                Vector2 rest = rect.anchoredPosition;
                rect.anchoredPosition = rest - new Vector2(0f, Travel);
                UiMotion.Move(rect, rest, UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
            }
        }

        /// <summary>ExitScreen: 140 ms Exit fade + 8 px push down (Decor; exits are one step shorter than entries).</summary>
        public static UiTweenHandle ExitScreen(CanvasGroup group, RectTransform rect, int owner = 0)
        {
            if (rect != null)
            {
                UiMotion.Move(rect, rect.anchoredPosition - new Vector2(0f, Travel), UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
            }

            return group != null
                ? UiMotion.Alpha(group, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner)
                : default(UiTweenHandle);
        }

        /// <summary>PaneOut: 140 ms Exit fade (Decor).</summary>
        public static UiTweenHandle PaneOut(CanvasGroup group, int owner = 0)
        {
            return UiMotion.Alpha(group, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
        }

        /// <summary>PaneIn: 220 ms Settle fade + 8 px rise, overlapping the out by starting after <paramref name="delay"/> (Decor).</summary>
        public static void PaneIn(CanvasGroup group, RectTransform rect, float delay = 0.06f, int owner = 0)
        {
            if (group != null)
            {
                group.alpha = 0f;
                UiMotion.Alpha(group, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor, delay, UiClock.Unscaled, owner);
            }

            if (rect != null)
            {
                Vector2 rest = rect.anchoredPosition;
                rect.anchoredPosition = rest - new Vector2(0f, Travel);
                UiMotion.Move(rect, rest, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor, delay, UiClock.Unscaled, owner);
            }
        }

        /// <summary>EnterModal: scrim 140 ms, card 0.96 → 1 + fade over 220 ms Settle (Decor).</summary>
        public static void EnterModal(CanvasGroup scrim, CanvasGroup card, RectTransform cardRect, int owner = 0)
        {
            if (scrim != null)
            {
                scrim.alpha = 0f;
                UiMotion.Alpha(scrim, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
            }

            if (card != null)
            {
                card.alpha = 0f;
                UiMotion.Alpha(card, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
            }

            if (cardRect != null)
            {
                cardRect.localScale = new Vector3(ModalFromScale, ModalFromScale, 1f);
                UiMotion.Scale(cardRect, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
            }
        }

        /// <summary>
        /// StampIn: 1.12 → 1.00 over 90 ms (InCubic - the Exit curve), then held; never below 1
        /// (seal, PURCHASED, medal, coin). Decor: under RM it simply appears at 1.0. The contact
        /// frame is at 90 ms - put the sound cue there (<see cref="StampContactSeconds"/>).
        /// </summary>
        public static UiTweenHandle StampIn(RectTransform rect, float delay = 0f, int owner = 0)
        {
            return UiMotion.ScaleSeconds(rect, UiMotionTokens.StampPeakScale, 1f, UiMotionTokens.StampInSeconds, UiEase.Exit, UiMotionClass.Decor, delay, owner);
        }

        public const float StampContactSeconds = UiMotionTokens.StampInSeconds;

        /// <summary>CounterRoll: an integer count-up over 600 ms Settle (Delta: RM shows the final value).</summary>
        public static UiTweenHandle CounterRoll(TMP_Text text, int from, int to, float delay = 0f, int owner = 0)
        {
            return UiMotion.Count(text, from, to, UiDuration.Reveal, UiEase.Settle, UiMotionClass.Delta, delay, UiClock.Unscaled, owner);
        }

        /// <summary>ChipPop: 0.96 → 1 + fade over 140 ms Settle (Decor). Text-bearing, so the scale stays ≤ 1.06.</summary>
        public static void ChipPop(CanvasGroup group, RectTransform rect, int owner = 0)
        {
            if (group != null)
            {
                group.alpha = 0f;
                UiMotion.Alpha(group, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
            }

            if (rect != null)
            {
                UiMotion.ScaleSeconds(rect, ModalFromScale, 1f, UiMotionTokens.Seconds(UiDuration.Fast), UiEase.Settle, UiMotionClass.Decor, 0f, owner);
            }
        }

        /// <summary>PressDepth: down to 0.98 in 80 ms while held; <see cref="PressRelease"/> returns it (Decor).</summary>
        public static UiTweenHandle PressDepth(RectTransform rect, int owner = 0)
        {
            return UiMotion.Scale(rect, PressScale, UiDuration.Instant, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
        }

        public static UiTweenHandle PressRelease(RectTransform rect, int owner = 0)
        {
            return UiMotion.Scale(rect, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
        }

        /// <summary>HoldFill: Linear 0 → 1 over the hold-to-confirm token (500 ms). Data: RM never changes it.</summary>
        public static UiTweenHandle HoldFill(Image image, int owner = 0)
        {
            return UiMotion.FillSeconds(image, 0f, 1f, UiMotionTokens.HoldConfirm, UiEase.Linear, UiMotionClass.Data, 0f, owner);
        }

        /// <summary>HoldCancel: the fill drains in 140 ms (Data).</summary>
        public static UiTweenHandle HoldCancel(Image image, int owner = 0)
        {
            return UiMotion.Fill(image, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Data, 0f, UiClock.Unscaled, owner);
        }

        /// <summary>
        /// DeniedNudge: ±4 px, two cycles, 160 ms, back at rest (Decor - off under RM). Never in
        /// the HUD play band (the caller's rule); never on a text-bearing element that must stay
        /// readable mid-shake (ui-ux r2) - put it on the control's frame.
        /// </summary>
        public static UiTweenHandle DeniedNudge(RectTransform rect, int owner = 0)
        {
            if (rect == null) return default(UiTweenHandle);
            return UiMotion.ShakeSeconds(rect, rect.anchoredPosition, UiMotionTokens.DeniedNudgePx, 2f, 0.16f, UiMotionClass.Decor, owner);
        }

        /// <summary>FocusTravel: the travelling focus marker moves between controls in 140 ms (Move curve, Decor).</summary>
        public static UiTweenHandle FocusTravel(RectTransform marker, Vector2 to, int owner = 0)
        {
            return UiMotion.Move(marker, to, UiDuration.Fast, UiEase.Move, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
        }

        /// <summary>BarGhost: the lost-value ghost holds 350 ms, then drops to the value over 220 ms (Delta: RM shows it statically for the hold, then removes it).</summary>
        public static UiTweenHandle BarGhost(Image ghost, float from, float to, int owner = 0)
        {
            if (ghost == null) return default(UiTweenHandle);
            if (UiMotion.ReducedMotion)
            {
                ghost.fillAmount = from;
                return UiMotion.FillSeconds(ghost, from, to, 0f, UiEase.Linear, UiMotionClass.Data, UiMotionTokens.TrailHold, owner);
            }

            return UiMotion.FillSeconds(ghost, from, to, UiMotionTokens.Seconds(UiDuration.Base), UiEase.Settle, UiMotionClass.Delta, UiMotionTokens.TrailHold, owner);
        }
    }
}
