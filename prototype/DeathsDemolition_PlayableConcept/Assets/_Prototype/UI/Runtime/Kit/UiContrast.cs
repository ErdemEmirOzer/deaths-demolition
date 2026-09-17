using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>One measured foreground/background pair.</summary>
    public readonly struct UiContrastCase
    {
        public readonly string Description;
        public readonly UiColor Foreground;
        public readonly UiColor Background;

        /// <summary>4.5 for body text, 3.0 for large text and for interface boundaries.</summary>
        public readonly float Required;

        /// <summary>
        /// When set, the foreground is first composited over this token, then over
        /// <see cref="Background"/>. Used for the HUD, whose backing is translucent.
        /// </summary>
        public readonly UiColor? CompositeOver;

        /// <summary>Extra alpha applied to the foreground before measuring, e.g. a dimmed state.</summary>
        public readonly float ForegroundAlpha;

        public UiContrastCase(string description, UiColor foreground, UiColor background, float required,
            UiColor? compositeOver = null, float foregroundAlpha = 1f)
        {
            Description = description;
            Foreground = foreground;
            Background = background;
            Required = required;
            CompositeOver = compositeOver;
            ForegroundAlpha = foregroundAlpha;
        }
    }

    /// <summary>
    /// WCAG 2.1 relative luminance and contrast ratio, plus the audit list that proves
    /// the palette in <see cref="UiTokens"/> actually clears its thresholds.
    ///
    /// The ratios quoted in the UiTokens comment block are produced by this code, so a
    /// palette edit that breaks a threshold is caught by running the audit rather than by
    /// somebody noticing on a screenshot. Run it from
    /// Tools &gt; Deaths Demolition &gt; UI &gt; Audit Accessibility, or call
    /// <see cref="BuildReport"/> from anywhere.
    /// </summary>
    public static class UiContrast
    {
        /// <summary>sRGB channel to linear light.</summary>
        private static float Linear(float channel)
        {
            return channel <= 0.03928f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>WCAG relative luminance of an opaque colour.</summary>
        public static float RelativeLuminance(Color c)
        {
            return 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
        }

        /// <summary>Source-over composite in sRGB space, which is what the GPU does for UI.</summary>
        public static Color Composite(Color foreground, Color background)
        {
            float a = Mathf.Clamp01(foreground.a);
            return new Color(
                foreground.r * a + background.r * (1f - a),
                foreground.g * a + background.g * (1f - a),
                foreground.b * a + background.b * (1f - a),
                1f);
        }

        /// <summary>Contrast ratio between two opaque colours. Always &gt;= 1.</summary>
        public static float Ratio(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float hi = Mathf.Max(la, lb);
            float lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        /// <summary>Measure one audit case, resolving tokens and any compositing it declares.</summary>
        public static float Measure(in UiContrastCase c)
        {
            Color background = UiTokens.Get(c.Background);

            if (c.CompositeOver.HasValue)
            {
                background = Composite(UiTokens.Get(c.CompositeOver.Value), background);
            }
            else
            {
                background = Composite(background, background); // flatten any alpha onto itself
            }

            Color foreground = UiTokens.Get(c.Foreground);
            foreground.a *= Mathf.Clamp01(c.ForegroundAlpha);
            foreground = Composite(foreground, background);

            return Ratio(foreground, background);
        }

        /// <summary>
        /// The audit set. Every foreground/background pairing this module actually puts on
        /// screen appears here. A pairing that is not in this list is not allowed to ship.
        /// </summary>
        public static IReadOnlyList<UiContrastCase> Cases
        {
            get { return s_cases; }
        }

        private static readonly UiContrastCase[] s_cases =
        {
            // ---------------------------------------------------------------- body text
            new UiContrastCase("Body text on a card", UiColor.TextPrimary, UiColor.SurfacePanel, 4.5f),
            new UiContrastCase("Body text on the screen background", UiColor.TextPrimary, UiColor.SurfaceBase, 4.5f),
            new UiContrastCase("Supporting text on a card", UiColor.TextSecondary, UiColor.SurfacePanel, 4.5f),
            new UiContrastCase("De-emphasised text on a card", UiColor.TextMuted, UiColor.SurfacePanel, 4.5f),
            new UiContrastCase("Accent text on a card", UiColor.TextAccent, UiColor.SurfacePanel, 4.5f),
            new UiContrastCase("Text on a raised panel", UiColor.TextPrimary, UiColor.SurfaceRaised, 4.5f),

            // WP-022C: the pairs the v2.x screens added - the UNLOCKED chip (moss text on
            // the inset well), the threat chips (danger/warning text on the inset well) and
            // the muted caption at 14 px on a card (DEC-077 UI comfort floor).
            new UiContrastCase("Unlocked chip text (moss) on the inset well", UiColor.BarHealth, UiColor.SurfaceInset, 4.5f),
            new UiContrastCase("Threat chip text (danger) on the inset well", UiColor.StateDanger, UiColor.SurfaceInset, 4.5f),
            new UiContrastCase("Threat chip text (warning) on the inset well", UiColor.StateWarning, UiColor.SurfaceInset, 4.5f),
            new UiContrastCase("Best-round chip text (success) on the inset well", UiColor.StateSuccess, UiColor.SurfaceInset, 4.5f),
            new UiContrastCase("Supporting text on a raised panel", UiColor.TextSecondary, UiColor.SurfaceRaised, 4.5f),

            // Added when the four screens were built. TextMuted is deliberately ABSENT
            // on SurfaceRaised: it measures 4.27:1 there, so the screens use
            // TextSecondary on any raised surface instead. That is the one rule this
            // palette needs a person to remember, and it is why the case is written
            // down here rather than left to be rediscovered.
            new UiContrastCase("Supporting text on the screen background", UiColor.TextSecondary,
                UiColor.SurfaceBase, 4.5f),
            new UiContrastCase("De-emphasised text on the screen background", UiColor.TextMuted,
                UiColor.SurfaceBase, 4.5f),
            new UiContrastCase("Chip text in a recessed well", UiColor.TextSecondary, UiColor.SurfaceInset, 4.5f),
            new UiContrastCase("Unlocked chip text in a recessed well", UiColor.StateSuccess,
                UiColor.SurfaceInset, 4.5f),
            // Dark-on-accent pills: EARNED, NEW, LEVEL UP. AccentMoss is deliberately
            // absent as a pill fill - it measures 3.52:1 with dark text and 4.66:1 with
            // light text, so neither direction has headroom. StateSuccess replaced it.
            new UiContrastCase("Earned / level-up pill label", UiColor.SurfaceInset, UiColor.StateSuccess, 4.5f),
            new UiContrastCase("New-unlock pill label", UiColor.SurfaceInset, UiColor.AccentSakura, 4.5f),
            new UiContrastCase("New-unlock title on a raised card", UiColor.AccentSakura,
                UiColor.SurfaceRaised, 4.5f),
            new UiContrastCase("Earned-achievement title on a raised card", UiColor.StateSuccess,
                UiColor.SurfaceRaised, 4.5f),
            new UiContrastCase("Earned-achievement card border", UiColor.StateSuccess, UiColor.SurfacePanel, 3f),

            // ------------------------------------------- round-end upgrade screen
            // The upgrade card's action shelf is a SurfaceInset well, and that is a
            // MEASUREMENT and not a style: AccentPrimary reads 2.99:1 on SurfacePanel,
            // i.e. BELOW the 3:1 floor for a non-text boundary, and 3.53:1 on
            // SurfaceInset. Putting the BUY pill and the tier ladder on an inset shelf is
            // what makes them legal. Same family of trap as TextMuted on SurfaceRaised
            // (4.27:1) and AccentMoss as a pill fill - both already excluded above.
            new UiContrastCase("Accent on a recessed well (BUY pill fill, owned tier pip)",
                UiColor.AccentPrimary, UiColor.SurfaceInset, 3f),
            new UiContrastCase("Upgrade price on the action shelf", UiColor.StateGold, UiColor.SurfaceInset, 4.5f),
            new UiContrastCase("Unaffordable price on the action shelf", UiColor.TextMuted,
                UiColor.SurfaceInset, 4.5f),
            new UiContrastCase("Gold shortfall sentence on the action shelf", UiColor.StateDanger,
                UiColor.SurfaceInset, 4.5f),

            // The header eyebrow every full screen draws, and the upgrade screen's
            // footer warning. Both sit on the backdrop rather than on a card, and neither
            // pair had a case until this screen was audited.
            new UiContrastCase("Accent eyebrow on the screen background", UiColor.TextAccent,
                UiColor.SurfaceBase, 4.5f),
            new UiContrastCase("Warning footnote on the screen background", UiColor.StateWarning,
                UiColor.SurfaceBase, 4.5f),

            // WHY THE UPGRADE SCREEN IS OPAQUE, in one measurement. Over the round-end
            // scrim laid on a worst-case white frozen frame the backdrop luminance rises
            // to roughly SurfaceRaised, where AccentPrimary would measure 2.48:1 and a
            // hairline border 1.33:1. Only BorderStrong survives it, and a screen whose
            // contrast depends on where the camera was pointing cannot be audited - so
            // the screen draws the standard opaque backdrop instead. This case is kept as
            // the evidence for that decision, and as the guard for the one element that
            // does still sit on a scrim.
            new UiContrastCase("Panel edge over the round-end scrim on a white world",
                UiColor.BorderStrong, UiColor.TextPrimary, 3f, UiColor.ScrimOverlay),

            // -------------------------------------------------------------- button text
            new UiContrastCase("Primary button label", UiColor.TextOnAccent, UiColor.AccentPrimary, 4.5f),
            new UiContrastCase("Selected chip label on the cream chip (AD v003)", UiColor.SurfaceBase, UiColor.TextPrimary, 4.5f),
            new UiContrastCase("Slider fill against its track (AD v003)", UiColor.TextPrimary, UiColor.BarTrack, 3f),
            new UiContrastCase("Slider knob edge against the panel", UiColor.TextPrimary, UiColor.SurfacePanel, 3f),
            new UiContrastCase("Primary button label, pressed", UiColor.TextOnAccent, UiColor.AccentPrimaryPressed, 4.5f),
            new UiContrastCase("Secondary button label", UiColor.TextPrimary, UiColor.SurfaceBase, 4.5f),
            new UiContrastCase("Disabled button label", UiColor.TextSecondary, UiColor.SurfaceRaised, 4.5f),

            // ----------------------------------------------------------- state colours
            new UiContrastCase("Danger readout on a card", UiColor.StateDanger, UiColor.SurfacePanel, 4.5f),
            new UiContrastCase("Warning readout on a card", UiColor.StateWarning, UiColor.SurfacePanel, 4.5f),
            new UiContrastCase("Success readout on a card", UiColor.StateSuccess, UiColor.SurfacePanel, 4.5f),
            new UiContrastCase("Gold readout on a card", UiColor.StateGold, UiColor.SurfacePanel, 4.5f),
            new UiContrastCase("New-unlock highlight text", UiColor.AccentSakura, UiColor.SurfacePanel, 4.5f),
            new UiContrastCase("Secondary highlight text", UiColor.AccentWisteria, UiColor.SurfacePanel, 4.5f),

            // ------------------------------------------------------------------- HUD
            // The HUD backing is translucent, so it is measured against the worst possible
            // world behind it. SurfaceInset stands in for a black world and TextPrimary for
            // a white one; the white case is the one that can fail, so it is the one listed.
            new UiContrastCase("HUD text over a white world", UiColor.TextPrimary, UiColor.TextPrimary, 4.5f,
                UiColor.HudInk),
            new UiContrastCase("HUD text over a black world", UiColor.TextPrimary, UiColor.SurfaceInset, 4.5f,
                UiColor.HudInk),
            new UiContrastCase("HUD danger readout over a white world", UiColor.StateDanger, UiColor.TextPrimary, 4.5f,
                UiColor.HudInk),
            new UiContrastCase("HUD gold readout over a white world", UiColor.StateGold, UiColor.TextPrimary, 4.5f,
                UiColor.HudInk),

            // REQ-057: the suspended classic weapon block. Dimmed, and still readable.
            new UiContrastCase("Suspended weapon block over a white world", UiColor.TextPrimary, UiColor.TextPrimary,
                4.5f, UiColor.HudInk, UiTokens.OpacitySuspended),

            // ------------------------------------- REQ-045: directional damage edge
            // The edge is a translucent overlay on LIVE GAMEPLAY, so the thing it has to
            // be distinguishable from is the world, and the world can be any colour. Both
            // bounds are therefore measured, with SurfaceInset standing in for the darkest
            // world and TextPrimary for the lightest, exactly as the HUD block above does.
            //
            // WHY THE ALPHA IS 0.92 AND NOT A NICER NUMBER. These two cases are what set
            // it. An alpha-blended overlay converges on whatever is behind it, so the dark
            // world is the binding side: the same colour reaches 2.65:1 at alpha 0.80,
            // 2.85:1 at 0.85 and 3.06:1 at 0.90. 0.92 is the first step on the 0.02 grid
            // with headroom. Anything softer is a decoration that measures as noise.
            //
            // THE CASE THAT IS DELIBERATELY NOT HERE, and it is the important one.
            // "Damage edge against the Special Weapon final-seconds veil" measures 1.96:1
            // over a black world and 3.16:1 over a white one - i.e. over a dark world the
            // two red edge treatments are NOT separable by luminance, and no alpha this
            // element is allowed to have would fix that. Writing the pair in as a 3:1 case
            // would simply be writing in a failure. The separation is carried by three
            // things a ratio cannot see, and they are load bearing:
            //   time     the damage edge lives 0.48 s and dies; the veil pulses until the
            //            Special Weapon expires. This is the separator that actually works.
            //   place    the veil only ever uses the top and bottom; the damage edge is the
            //            only thing in the HUD that can light the left or right edge.
            //   depth    96 px against the veil's 18% of the height.
            // Same species of written-down trap as TextMuted on SurfaceRaised (4.27:1) and
            // AccentPrimary on SurfacePanel (2.99:1): recorded here so the next person does
            // not rediscover it by shipping it.
            new UiContrastCase("Damage edge at peak over a black world", UiColor.DamageEdge,
                UiColor.SurfaceInset, 3f, null, UiTokens.DamageEdgePeakAlpha),
            new UiContrastCase("Damage edge at peak over a white world", UiColor.DamageEdge,
                UiColor.TextPrimary, 3f, null, UiTokens.DamageEdgePeakAlpha),

            // ------------------------------------------------- non-text, 3:1 threshold
            new UiContrastCase("Interactive boundary", UiColor.BorderStrong, UiColor.SurfaceBase, 3f),
            new UiContrastCase("Primary button fill against the screen", UiColor.AccentPrimary, UiColor.SurfaceBase, 3f),
            new UiContrastCase("Focus ring on a card", UiColor.BorderFocus, UiColor.SurfacePanel, 3f),
            new UiContrastCase("Focus ring on a primary button", UiColor.BorderFocus, UiColor.AccentPrimary, 3f),
            new UiContrastCase("Focus halo against the ring", UiColor.BorderFocusHalo, UiColor.BorderFocus, 3f),
            new UiContrastCase("Health fill against its track", UiColor.BarHealth, UiColor.BarTrack, 3f),
            new UiContrastCase("Low-health fill against its track", UiColor.BarHealthLow, UiColor.BarTrack, 3f),
            new UiContrastCase("XP fill against its track", UiColor.BarXp, UiColor.BarTrack, 3f),
            new UiContrastCase("Reload fill against its track", UiColor.BarReload, UiColor.BarTrack, 3f),
            new UiContrastCase("Special Weapon fill against its track", UiColor.BarSpecial, UiColor.BarTrack, 3f)
        };

        /// <summary>Number of audit cases that do not clear their threshold.</summary>
        public static int CountFailures()
        {
            int failures = 0;
            for (int i = 0; i < s_cases.Length; i++)
            {
                if (Measure(s_cases[i]) + 0.005f < s_cases[i].Required)
                {
                    failures++;
                }
            }

            return failures;
        }

        /// <summary>Human-readable audit table. English, safe to print anywhere.</summary>
        public static string BuildReport()
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("UI accessibility audit - contrast ratios (WCAG 2.1)");
            sb.AppendLine("---------------------------------------------------");

            int failures = 0;
            for (int i = 0; i < s_cases.Length; i++)
            {
                UiContrastCase c = s_cases[i];
                float ratio = Measure(c);
                bool ok = ratio + 0.005f >= c.Required;
                if (!ok)
                {
                    failures++;
                }

                sb.AppendLine(string.Format(
                    "{0}  {1,6:0.00}:1  (needs {2:0.0}:1)  {3}",
                    ok ? "PASS" : "FAIL", ratio, c.Required, c.Description));
            }

            sb.AppendLine("---------------------------------------------------");
            sb.AppendLine(string.Format("{0} cases, {1} failing.", s_cases.Length, failures));
            return sb.ToString();
        }
    }
}
