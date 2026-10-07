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
            return DeniedNudge(rect, rect.anchoredPosition, owner);
        }

        /// <summary>
        /// WP-094 (HAREKET-HUD A6): the DeniedNudge about a known <paramref name="rest"/> - a second nudge before the first has
        /// settled shakes about the home, not about wherever the first left the rect.
        /// </summary>
        public static UiTweenHandle DeniedNudge(RectTransform rect, Vector2 rest, int owner = 0)
        {
            if (rect == null) return default(UiTweenHandle);
            return UiMotion.ShakeSeconds(rect, rest, UiMotionTokens.DeniedNudgePx, 2f, UiMotionTokens.DeniedNudgeSeconds, UiMotionClass.Decor, owner);
        }

        // ================================================================== WP-094 HUD v4 juice (HAREKET-HUD §6 API)

        /// <summary>
        /// Punch (G1): the rect jumps to <paramref name="peak"/> and settles to 1 over Base (220 ms, Settle). Decor: at rest under
        /// reduced motion. A glyph may take <see cref="UiMotionTokens.PunchPeakGlyph"/>; text never past
        /// <see cref="UiMotionTokens.TextPopMaxScale"/> (clamped here). Pivot it at its centre so a target point beside it holds.
        /// </summary>
        public static UiTweenHandle Punch(RectTransform rect, float peak, bool textBearing = false, int owner = 0)
        {
            if (rect == null) return default(UiTweenHandle);
            float top = textBearing ? Mathf.Min(peak, UiMotionTokens.TextPopMaxScale) : peak;
            return UiMotion.ScaleSeconds(rect, top, 1f, UiMotionTokens.Seconds(UiDuration.Base), UiEase.Settle, UiMotionClass.Decor, 0f, owner);
        }

        /// <summary>
        /// Flare: the graphic's CanvasRenderer alpha rises 0 → <paramref name="peak"/> over <paramref name="rise"/> (Settle), holds
        /// <paramref name="hold"/> s, and falls to 0 over <paramref name="fall"/> (Exit) - two channels (Tint, then TintAlpha from
        /// an explicit start), so neither retargets the other and the mesh is never rebuilt. Light, not motion: Flash class by
        /// default (plays under reduced motion); the caller multiplies the peak by FlashScale.
        /// </summary>
        public static void Flare(Graphic graphic, float peak, UiDuration rise, float hold, UiDuration fall, UiMotionClass motionClass = UiMotionClass.Flash, int owner = 0)
        {
            if (graphic == null) return;
            Color start = graphic.canvasRenderer.GetColor();
            start.a = 0f;
            graphic.canvasRenderer.SetColor(start);
            Color top = start;
            top.a = Mathf.Clamp01(peak);
            UiMotion.Tint(graphic, top, rise, UiEase.Settle, motionClass, 0f, UiClock.Unscaled, owner);
            UiMotion.TintAlphaSeconds(graphic, top.a, 0f, UiMotionTokens.Seconds(fall), UiEase.Exit, motionClass, UiMotionTokens.Seconds(rise) + Mathf.Max(0f, hold), owner);
        }

        /// <summary>
        /// The Roman numeral's brush reveal length by its letters (HAREKET-HUD R2): ≤ 2 Base, 3–4 Screen, 5–6 Emphasis, ≥ 7
        /// Reveal - I / IV / IX open in 220 ms, XIV in 280, XXIII in 400, XXXVIII in 600.
        /// </summary>
        public static UiDuration RomanRevealDuration(int glyphCount)
        {
            if (glyphCount <= 2) return UiDuration.Base;
            if (glyphCount <= 4) return UiDuration.Screen;
            if (glyphCount <= 6) return UiDuration.Emphasis;
            return UiDuration.Reveal;
        }

        /// <summary>
        /// RomanReveal (R2): the numeral's left-pivoted RectMask2D opens 0 → <paramref name="width"/> left to right over
        /// <see cref="RomanRevealDuration"/> on the Brush curve (the Size channel). Decor: open at once under reduced motion.
        /// </summary>
        public static UiTweenHandle RomanReveal(RectTransform clip, float width, int glyphCount, int owner = 0)
        {
            if (clip == null) return default(UiTweenHandle);
            float height = clip.sizeDelta.y;
            clip.sizeDelta = new Vector2(0f, height);
            return UiMotion.Resize(clip, new Vector2(width, height), RomanRevealDuration(glyphCount), UiEase.Brush, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner);
        }

        /// <summary>The casing's throw (A2): the rise of the arc and the fall that bends it - the apex lands ≈ 17 px over the slot at t ≈ 0.3.</summary>
        public const float EjectRisePx = 26f;
        public const float EjectFallPx = 38f;
        public const float EjectSpinDeg = 90f;

        /// <summary>
        /// Eject (A2): a spent round leaves its slot. The <paramref name="arc"/> rect (the casing's carrier) is thrown
        /// <paramref name="driftX"/> px sideways and <see cref="EjectRisePx"/> up on Settle (a decelerating throw) while the
        /// <paramref name="body"/> inside it falls <see cref="EjectFallPx"/> on Exit (gravity) and turns 90°, its ink fading
        /// (Exit) - the two moves add up to a ballistic arc over Base (220 ms). Four tweens; a reused casing restarts from its
        /// slot (the core's one-per-(target, channel) rule retargets). Decor: the caller plays none under reduced motion.
        /// </summary>
        public static void Eject(RectTransform arc, Vector2 from, float driftX, Graphic body, int owner = 0)
        {
            if (arc == null || body == null) return;
            float seconds = UiMotionTokens.Seconds(UiDuration.Base);
            RectTransform inner = body.rectTransform;
            arc.anchoredPosition = from;
            inner.anchoredPosition = Vector2.zero;
            inner.localRotation = Quaternion.identity;
            body.canvasRenderer.SetAlpha(1f);
            UiMotion.MoveSeconds(arc, from, from + new Vector2(driftX, EjectRisePx), seconds, UiEase.Settle, UiMotionClass.Decor, 0f, owner);
            UiMotion.MoveSeconds(inner, Vector2.zero, new Vector2(0f, -EjectFallPx), seconds, UiEase.Exit, UiMotionClass.Decor, 0f, owner);
            UiMotion.RotateSeconds(inner, 0f, driftX >= 0f ? -EjectSpinDeg : EjectSpinDeg, seconds, UiEase.Linear, UiMotionClass.Decor, 0f, owner);
            UiMotion.TintAlphaSeconds(body, 1f, 0f, seconds, UiEase.Exit, UiMotionClass.Decor, 0f, owner);
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
