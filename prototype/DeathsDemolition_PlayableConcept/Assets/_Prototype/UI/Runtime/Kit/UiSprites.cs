using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Every sprite the prototype UI draws, generated procedurally at runtime.
    ///
    /// No imported texture, no atlas, no external asset - which keeps the module inside
    /// its file ownership, keeps the project offline, and means a colour change never
    /// requires re-exporting art. Shapes are anti-aliased by sampling a signed distance
    /// against the pixel centre, so a 14 px corner radius looks like a 14 px corner
    /// radius and not like a staircase.
    ///
    /// Textures are created with HideAndDontSave: they never leak into a scene file and
    /// they survive scene loads, so the cache is built once per session.
    /// </summary>
    public static class UiSprites
    {
        private static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>(16);

        /// <summary>1x1 opaque white. Fills, dividers, bar tracks.</summary>
        public static Sprite Solid
        {
            get { return GetOrCreate("solid", CreateSolid); }
        }

        /// <summary>Rounded rectangle, 9-sliced. Cards and panels.</summary>
        public static Sprite Card
        {
            get { return GetOrCreate("card", () => CreateRounded(UiTokens.RadiusCard)); }
        }

        /// <summary>Rounded rectangle, 9-sliced. Buttons and inputs.</summary>
        public static Sprite Control
        {
            get { return GetOrCreate("control", () => CreateRounded(UiTokens.RadiusControl)); }
        }

        /// <summary>Rounded rectangle, 9-sliced. Chips and tags.</summary>
        public static Sprite Chip
        {
            get { return GetOrCreate("chip", () => CreateRounded(UiTokens.RadiusChip)); }
        }

        /// <summary>WP-068: the chip's 1 px hollow rim (rounded rect outline, 9-sliced) - the v3 replacement for the uGUI Outline effect, which is banned (direction §3).</summary>
        public static Sprite ChipRim
        {
            get { return GetOrCreate("chipRim", () => CreateRounded(UiTokens.RadiusChip, 1.2f)); }
        }

        /// <summary>WP-068 slice 5 (§4 #2): four 2 px corner arms, 10 px long, 9-sliced so the arms keep their length on any rect - the travelling focus brackets.</summary>
        public static Sprite FocusBrackets
        {
            get { return GetOrCreate("focusBrackets", () => CreateBrackets(10, 2f)); }
        }

        /// <summary>
        /// WP-098A (KIT-KILAVUZU §1 C7) · the cream focus brackets: four corner arms <see cref="UiBrushTokens.BracketArm"/> (10)
        /// long and <see cref="UiBrushTokens.BracketStroke"/> (3) thick, 9-sliced so the arms keep their length on any rect.
        /// Tinted TextPrimary over <see cref="FocusBracketsHalo"/>.
        /// </summary>
        public static Sprite FocusBracketsInk
        {
            get { return GetOrCreate("focusBracketsInk", () => CreateBrackets(UiBrushTokens.BracketArm, UiBrushTokens.BracketStroke)); }
        }

        /// <summary>
        /// WP-098A · the brackets' ink halo: <see cref="FocusBracketsInk"/> grown by <see cref="UiBrushTokens.BracketHalo"/> (2) on
        /// every side - drawn in a rect <see cref="UiBrushTokens.BracketHalo"/> px larger than the arms', so the halo rims the
        /// cream inside, outside and past each tip (16.40:1 on any world).
        /// </summary>
        public static Sprite FocusBracketsHalo
        {
            get
            {
                const int halo = UiBrushTokens.BracketHalo;
                return GetOrCreate("focusBracketsHalo", () => CreateBrackets(UiBrushTokens.BracketArm + halo * 2, UiBrushTokens.BracketStroke + halo * 2));
            }
        }

        /// <summary>
        /// WP-068 slice 5: the one soft shadow (direction §2: a 9-slice Gaussian - uGUI Shadow is
        /// banned). The falloff is <see cref="SoftShadowReach"/> px at pixelsPerUnitMultiplier 1;
        /// Elev1 (8 px) draws it at multiplier 3.
        /// </summary>
        public static Sprite SoftShadow
        {
            get { return GetOrCreate("softShadow", () => CreateSoftShadow(SoftShadowReach, UiTokens.RadiusCard)); }
        }

        public const int SoftShadowReach = 24;

        /// <summary>
        /// WP-068 slice 5: hollow focus ring / halo for controls with no fill of their own (Ghost,
        /// a card surface): the v2 ring and halo are filled shapes hidden behind the control's
        /// fill, which a Ghost does not have (kit b2: the focused card turned solid yellow).
        /// Corner radius = the control's radius + the outset, so the bands stay concentric.
        /// </summary>
        public static Sprite FocusRingHollow
        {
            get { return GetOrCreate("focusRingHollow", () => CreateRounded(UiTokens.RadiusControl + UiTokens.StrokeFocusRing, UiTokens.StrokeFocusRing)); }
        }

        public static Sprite FocusHaloHollow
        {
            get
            {
                const int outset = UiTokens.StrokeFocusRing + UiTokens.StrokeFocusHalo;
                return GetOrCreate("focusHaloHollow", () => CreateRounded(UiTokens.RadiusControl + outset, outset));
            }
        }

        /// <summary>Filled circle. Bullet points, coin glyph, status dots.</summary>
        public static Sprite Disc
        {
            get { return GetOrCreate("disc", () => CreateDisc(64)); }
        }

        /// <summary>
        /// Annulus for radial countdowns. Use with Image.type = Filled,
        /// fillMethod = Radial360, fillOrigin = Top, clockwise = false.
        /// </summary>
        public static Sprite Ring
        {
            get { return GetOrCreate("ring", () => CreateRing(128, 0.16f)); }
        }

        /// <summary>WP-068 slice 10b: the loading ring's interim stroke - 16 px at Ø 240 (AD 10b-1 SHOULD-1) until the ensō brush (10b-2).</summary>
        public static Sprite RingEnso
        {
            get { return GetOrCreate("ringEnso", () => CreateRing(256, 16f / 240f)); }
        }

        /// <summary>
        /// WP-068 slice 10b-2 (direction §15.3): the ensō brush stroke of the loading ring, drawn
        /// clockwise from 12 o'clock (the Radial360 fill order). It opens with a loaded brush
        /// (1.15 × the 14 px stroke at Ø 240) and settles to 1.0; from 0.80 it tapers to 0.55
        /// and the bristles run dry (streaks drop out one by one); the centre line breathes
        /// ±1.5 px. White + alpha, tinted at runtime (cream stroke, TextSecondary wet edge).
        /// </summary>
        public static Sprite EnsoBrush
        {
            get { return GetOrCreate("ensoBrush", () => CreateEnsoBrush(512, 14f / 240f)); }
        }

        /// <summary>WP-068 slice 8 (E11, AD 8a SHOULD-1): a hairline annulus - 3 px at Ø 100 - for the reload ring around the weapon glyph.</summary>
        public static Sprite RingHairline
        {
            get { return GetOrCreate("ringHairline", () => CreateRing(256, 0.03f)); }
        }

        /// <summary>Thin annulus, for the secondary timer track.</summary>
        public static Sprite RingThin
        {
            get { return GetOrCreate("ringThin", () => CreateRing(128, 0.09f)); }
        }

        /// <summary>Right-pointing triangle. Off-screen marker, list affordances.</summary>
        public static Sprite Chevron
        {
            get { return GetOrCreate("chevron", () => CreateChevron(48)); }
        }

        /// <summary>
        /// Five-petal sakura silhouette. The one decorative glyph in the whole module;
        /// used at small sizes as a section mark, never as wallpaper.
        /// </summary>
        public static Sprite Petal
        {
            get { return GetOrCreate("petal", () => CreatePetal(64)); }
        }

        /// <summary>Vertical alpha ramp, opaque at the bottom. Edge vignettes and veils.</summary>
        public static Sprite FadeUp
        {
            get { return GetOrCreate("fadeUp", () => CreateFade(64, true)); }
        }

        /// <summary>Vertical alpha ramp, opaque at the top.</summary>
        public static Sprite FadeDown
        {
            get { return GetOrCreate("fadeDown", () => CreateFade(64, false)); }
        }

        /// <summary>
        /// Horizontal alpha ramp, opaque at the LEFT edge.
        ///
        /// The vertical pair above cannot serve a left or right screen edge by rotating
        /// the Image: a rotated Image rotates its RectTransform with it, so a band that
        /// was stretched to the full screen height stops being full height the moment it
        /// turns. A second, genuinely horizontal ramp is cheaper than that correction.
        /// </summary>
        public static Sprite FadeLeft
        {
            get { return GetOrCreate("fadeLeft", () => CreateFadeHorizontal(64, true)); }
        }

        /// <summary>Horizontal alpha ramp, opaque at the RIGHT edge.</summary>
        public static Sprite FadeRight
        {
            get { return GetOrCreate("fadeRight", () => CreateFadeHorizontal(64, false)); }
        }

        /// <summary>
        /// WP-068 slice 8 (E11) · one magazine tick: a 4 px bar in an 8 px pitch, 16 tall, point-sharp edges. Tiled
        /// along the HUD ladder (repeat wrap → one quad); the pitch is the Image's pixelsPerUnitMultiplier.
        /// </summary>
        public static Sprite AmmoTick
        {
            get { return GetOrCreate("ammoTick", () => CreateTick(8, 16, 4)); }
        }

        /// <summary>
        /// WP-068 slice 8b (E13 rear arc, AD 8b MUST-2) · a 60° hairline arc at 12 o'clock of a square, 3 px at r 76
        /// (stroke 3.9 % of the radius), crisp ends. Rotate the image to face a direction; its size is the diameter.
        /// </summary>
        public static Sprite ArcHairline60
        {
            get { return GetOrCreate("arcHair60", () => CreateArc(304, 60f, 3f / 76f, 1f)); }
        }

        /// <summary>
        /// WP-068 slice 8c (AD 8c MUST-1) · the banner's brush ribbon, 9-sliced: a loaded head (40 px), a full body that
        /// stretches under the words (≥ 0.85 with ragged edges and bristle streaks along the stroke), a dry, streaky tail
        /// (80 px) that only begins past the last letter. Revealed left → right by the InkReveal wipe (its time map orders
        /// the ink, this sprite gives the shape) - the AP-012 underline mark tapers to a hairline and read as a smear.
        /// </summary>
        public static Sprite BrushRibbon
        {
            get { return GetOrCreate("brushRibbon", CreateBrushRibbon); }
        }

        public const int BrushRibbonHead = 40;
        public const int BrushRibbonTail = 80;

        private static Sprite CreateBrushRibbon()
        {
            const int width = 256, height = 96;
            Texture2D texture = NewTexture(width, height);
            var pixels = new Color32[width * height];
            float mid = height * 0.5f;
            for (int y = 0; y < height; y++)
            {
                // Bristle streaks: each row carries its own ink load (hash per row), lighter rows break up in the tail.
                float row = RibbonHash(y * 12.9898f);
                float load = 0.86f + 0.14f * row;
                for (int x = 0; x < width; x++)
                {
                    // The ribbon's half-thickness: 36 % of the height, ragged by a slow edge noise; the head swells.
                    float headT = Mathf.Clamp01(x / (float)BrushRibbonHead);
                    float swell = 1f + 0.25f * (1f - headT) * (1f - headT);
                    float ragged = (RibbonHash(Mathf.Floor(x / 6f) * 4.1f + (y > mid ? 7.3f : 0f)) - 0.5f) * 5f;
                    float half = height * 0.36f * swell + ragged;
                    float across = Mathf.Clamp01(half - Mathf.Abs(y + 0.5f - mid) + 0.5f);
                    // The head's rounded start.
                    float start = Mathf.Clamp01((x + 0.5f) - (1f - Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((y + 0.5f - mid) / (half + 1f), 2f)))) * 14f);
                    float alpha = across * start * load;
                    int tailStart = width - BrushRibbonTail;
                    if (x >= tailStart)
                    {
                        // Dry tail: the load thins toward the end, and weak rows drop out first (streaks).
                        float tailT = (x - tailStart) / (float)BrushRibbonTail;
                        float thin = 1f - tailT;
                        float gap = RibbonHash(y * 3.7f + Mathf.Floor(x / 3f) * 0.37f);
                        alpha *= Mathf.Clamp01(thin * 1.4f - (1f - row) * tailT * 1.6f) * (gap > tailT * 0.9f ? 1f : 0.2f);
                    }

                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, new Vector4(BrushRibbonHead, 0f, BrushRibbonTail, 0f));
        }

        private static float RibbonHash(float n)
        {
            float s = Mathf.Sin(n * 127.1f + 311.7f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        /// <summary>
        /// WP-068 slice 8b (E17 player arc, AD 8b MUST-2, direction "Arc.Soft") · a brush wedge at 12 o'clock: a soft band
        /// between 0.75 and 1.0 of the radius (48–64 px at r 64), fullest at its middle and tapering to its ends.
        /// </summary>
        public static Sprite ArcSoft60
        {
            get { return GetOrCreate("arcSoft60", () => CreateSoftArc(256, 60f, 0.75f)); }
        }

        /// <summary>
        /// WP-068 12b (AD R1-A SHOULD-6, shoulder half) · the rear-edge arc: ArcSoft60's band with shoulders - the stroke
        /// holds ≥ 0.5 of its peak out to 80 % of the chord (ArcSoft60's quadratic taper is 0.36 there, so the visible core
        /// was ≈ 80 px of the 200 px sprite). The E17 damage wedge keeps ArcSoft60.
        /// </summary>
        public static Sprite ArcShouldered60
        {
            get { return GetOrCreate("arcShouldered60", () => CreateSoftArc(256, 60f, 0.75f, 4f)); }
        }

        /// <summary>
        /// WP-068 12b (AD R1-C SHOULD-1) · the NEEDS seal ring: a dotted hairline of 20 round dots, so "waiting for a
        /// prerequisite" is a shape of its own and not only a muted tint of AVAILABLE's solid ring.
        /// </summary>
        /// <summary>
        /// WP-068 12b (D2, the translucent re-measure) · the rear-edge arc's HudInk backing: a wider band (0.66 of the radius
        /// to the rim) over a slightly wider span, drawn behind ArcShouldered60 at 1.06 × its size so it rims the coral stroke
        /// on both sides - over a cream-white world the stroke alone reached 1.57:1 and no α could lift it to 3:1.
        /// </summary>
        public static Sprite ArcShoulderedBacking60
        {
            get { return GetOrCreate("arcShoulderedBacking60", () => CreateSoftArc(256, 66f, 0.66f, 4f)); }
        }

        public static Sprite RingDotted
        {
            get { return GetOrCreate("ringDotted", () => CreateDottedRing(128, 0.05f, 20)); }
        }

        /// <summary>
        /// WP-068 slice 8b (AD 8a SHOULD-8) · the HUD panel fill with a translucent margin: 9-sliced, rounded like the card,
        /// the outer <see cref="PanelBandWidth"/> px at <see cref="PanelBandAlpha"/> of the fill, a 4 px ramp to full in the
        /// text zone. Tinted HudInk (0.98) the band renders 0.86: the world ghosts through the rim, never behind a glyph.
        /// </summary>
        public static Sprite PanelBanded
        {
            get { return GetOrCreate("panelBanded", () => CreateBandedPanel(UiTokens.RadiusCard, PanelBandWidth, PanelBandRamp, PanelBandAlpha)); }
        }

        public const int PanelBandWidth = 12;
        public const int PanelBandRamp = 4;
        public const float PanelBandAlpha = 0.86f / UiTokens.HudInkAlpha;

        /// <summary>WP-068 slice 8 (E11) · one pellet pip: an 8 px disc in a 12 px pitch (shotgun shells on the ladder).</summary>
        public static Sprite AmmoPip
        {
            get { return GetOrCreate("ammoPip", () => CreatePip(12, 8f)); }
        }

        // ------------------------------------------------------------------ plumbing

        // ---- WP-094 (AD SHOULD-1, 08.10): the damage edge in the brush language

        /// <summary>The brush edge's texture: its length along the screen edge and its depth into the screen (texels).</summary>
        public const int BrushEdgeLength = 512;
        public const int BrushEdgeDepth = 64;

        /// <summary>
        /// The damage edge's dry-brush ramp for <paramref name="edge"/> (opaque at that screen edge): a stroke laid along the edge
        /// - a ragged inner reach (the brush's pressure, low frequency along the edge), bristle streaks parallel to it that dry
        /// toward the inner end, short dry breaks in its inner third and soft ends - instead of the generic smooth gradient
        /// (HIS-SOZLESMESI principle 7). Procedural (0 credits), one 512 × 64 texture per edge, built once.
        /// </summary>
        public static Sprite BrushEdge(UiScreenEdge edge)
        {
            switch (edge)
            {
                case UiScreenEdge.Left: return GetOrCreate("brushEdgeLeft", () => CreateBrushEdge(UiScreenEdge.Left));
                case UiScreenEdge.Right: return GetOrCreate("brushEdgeRight", () => CreateBrushEdge(UiScreenEdge.Right));
                case UiScreenEdge.Top: return GetOrCreate("brushEdgeTop", () => CreateBrushEdge(UiScreenEdge.Top));
                default: return GetOrCreate("brushEdgeBottom", () => CreateBrushEdge(UiScreenEdge.Bottom));
            }
        }

        /// <summary>
        /// The brush edge's α at <paramref name="along"/> (0..1 along the screen edge) and <paramref name="depth"/> (0 at the
        /// screen edge, 1 at the texture's inner end). Public for the tests: full at the edge, nothing past the reach.
        /// </summary>
        public static float BrushEdgeAlpha(float along, float depth)
        {
            float wave = 0.6f * Mathf.Sin(2f * Mathf.PI * (along * 2.3f + 0.17f)) + 0.4f * Mathf.Sin(2f * Mathf.PI * (along * 5.1f + 0.6f));
            float reach = 0.68f + 0.2f * wave;                       // 0.48 … 0.88 of the depth
            float k = Mathf.Clamp01(depth / reach);
            float body = 1f - k * k * (3f - 2f * k);
            int band = Mathf.FloorToInt(depth * 40f);
            float streak = 1f - 0.45f * Hash01(band) * k;            // bristle streaks, drier toward the inner end
            int segment = Mathf.FloorToInt(along * 24f);
            float dry = k > 0.55f && Hash01(band * 31 + segment * 7 + 3) > 0.72f ? 0.25f : 1f;
            float ends = Smooth01(along / 0.06f) * Smooth01((1f - along) / 0.06f);
            return Mathf.Clamp01(body * streak * dry * ends);
        }

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static float Hash01(int n)
        {
            float v = Mathf.Sin(n * 12.9898f + 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static Sprite CreateBrushEdge(UiScreenEdge edge)
        {
            bool vertical = edge == UiScreenEdge.Left || edge == UiScreenEdge.Right;
            int width = vertical ? BrushEdgeDepth : BrushEdgeLength;
            int height = vertical ? BrushEdgeLength : BrushEdgeDepth;
            Texture2D texture = NewTexture(width, height);
            var pixels = new Color32[width * height];
            for (int a = 0; a < BrushEdgeLength; a++)
            {
                float along = a / (BrushEdgeLength - 1f);
                for (int d = 0; d < BrushEdgeDepth; d++)
                {
                    byte alpha = (byte)Mathf.RoundToInt(BrushEdgeAlpha(along, d / (BrushEdgeDepth - 1f)) * 255f);
                    int x, y;
                    switch (edge)
                    {
                        case UiScreenEdge.Left: x = d; y = a; break;
                        case UiScreenEdge.Right: x = width - 1 - d; y = a; break;
                        case UiScreenEdge.Top: x = a; y = height - 1 - d; break;
                        default: x = a; y = d; break;
                    }

                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static Sprite GetOrCreate(string key, System.Func<Sprite> factory)
        {
            Sprite sprite;
            if (s_cache.TryGetValue(key, out sprite) && sprite != null)
            {
                return sprite;
            }

            sprite = factory();
            sprite.name = "UI_" + key;
            s_cache[key] = sprite;
            return sprite;
        }

        private static Texture2D NewTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            return texture;
        }

        private static Sprite Finish(Texture2D texture, Vector4 border)
        {
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateSolid()
        {
            Texture2D texture = NewTexture(4, 4);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        /// <summary>
        /// Rounded rectangle sized so the 9-slice border is exactly the corner radius plus
        /// one pixel of anti-aliasing bleed. Stretching it never distorts the corner.
        /// </summary>
        private static Sprite CreateRounded(int radius)
        {
            return CreateRounded(radius, 0f);
        }

        /// <summary>Rounded rect; <paramref name="rim"/> &gt; 0 keeps only an edge band that many pixels wide (hollow).</summary>
        private static Sprite CreateRounded(int radius, float rim)
        {
            int r = Mathf.Max(1, radius);
            int size = r * 2 + 4;
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];

            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance from the rounded-rect boundary, in pixels.
                    float dx = Mathf.Abs(x + 0.5f - half) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - half) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float distance = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;

                    float alpha = Mathf.Clamp01(0.5f - distance);
                    if (rim > 0f)
                    {
                        alpha *= Mathf.Clamp01(distance + rim + 0.5f); // fade the inside out beyond the rim band
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            float border = r + 1;
            return Finish(texture, new Vector4(border, border, border, border));
        }

        /// <summary>
        /// Corner brackets: an arm of <paramref name="arm"/> px along each edge from every
        /// corner, <paramref name="stroke"/> px wide, anti-aliased. The 9-slice border is the
        /// arm length plus a pixel, so the stretched edge and centre cells are empty.
        /// </summary>
        private static Sprite CreateBrackets(int arm, float stroke)
        {
            int border = arm + 1;
            int size = border * 2 + 2;
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance into the nearest corner cell, measured from the outer edges.
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float ex = Mathf.Min(fx, size - fx);   // distance from the nearest vertical edge
                    float ey = Mathf.Min(fy, size - fy);   // distance from the nearest horizontal edge
                    float along = Mathf.Max(ex, ey);       // how far along an arm from the corner
                    float across = Mathf.Min(ex, ey);      // how far into the stroke
                    float coverStroke = Mathf.Clamp01(stroke + 0.5f - across);
                    float coverArm = Mathf.Clamp01(arm + 0.5f - along);
                    float alpha = coverStroke * coverArm;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, new Vector4(border, border, border, border));
        }

        /// <summary>A rounded-rect core (<paramref name="radius"/>) with a Gaussian falloff <paramref name="reach"/> px wide; border = reach + radius.</summary>
        private static Sprite CreateSoftShadow(int reach, int radius)
        {
            int border = reach + radius + 1;
            int size = border * 2 + 2;
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float core = half - reach; // half-extent of the rounded core
            float sigma = reach / 2.6f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - half) - (core - radius);
                    float dy = Mathf.Abs(y + 0.5f - half) - (core - radius);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float distance = Mathf.Max(0f, outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius);
                    float alpha = Mathf.Exp(-(distance * distance) / (2f * sigma * sigma));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, new Vector4(border, border, border, border));
        }

        private static Sprite CreateDisc(int size)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float radius = half - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    float alpha = Mathf.Clamp01(0.5f - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static Sprite CreateRing(int size, float thicknessFraction)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float outer = half - 1f;
            float inner = outer - size * thicknessFraction;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = Mathf.Clamp01(0.5f - (d - outer)) * Mathf.Clamp01(0.5f + (d - inner));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static Sprite CreateDottedRing(int size, float thicknessFraction, int dots)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float dot = size * thicknessFraction * 0.5f;          // dot radius = half the stroke
            float mid = half - 1f - dot;
            float period = 2f * Mathf.PI / dots;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float phase = Mathf.Repeat(Mathf.Atan2(dy, dx) + period * 0.5f, period) - period * 0.5f;   // to the nearest dot centre
                    float along = phase * mid, across = d - mid;
                    float alpha = Mathf.Clamp01(dot + 0.5f - Mathf.Sqrt(along * along + across * across));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static Sprite CreateEnsoBrush(int size, float strokeFraction)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float stroke = size * strokeFraction;
            float mid = half - 2f - stroke * 0.6f;   // the centre line, room for the loaded start
            const int bristles = 9;
            var dropAt = new float[bristles];
            for (int b = 0; b < bristles; b++)
            {
                dropAt[b] = 0.78f + 0.2f * Hash01(b * 7.13f + 1.7f);   // where each bristle runs dry
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Atan2(dx, dy) / (2f * Mathf.PI);   // 0 at 12 o'clock, clockwise
                    if (t < 0f)
                    {
                        t += 1f;
                    }

                    float width = stroke * (t < 0.08f ? 1.15f - 0.15f * (t / 0.08f) : 1f);
                    // Body pressure (AD 10b-2 SHOULD-1): ±10 % over two slow periods, so the mid-stroke is a
                    // brush pass, not an arc; zero at 12 o'clock, so the loaded head keeps its 1.15.
                    width *= 1f - 0.10f * Mathf.Sin(2f * Mathf.PI * 2f * t);
                    if (t > 0.8f)
                    {
                        width *= 1f - 0.45f * ((t - 0.8f) / 0.2f);
                    }

                    float centre = mid + 1.5f * Mathf.Sin(2f * Mathf.PI * (2f * t) + 0.7f);
                    float edge = width * 0.5f - Mathf.Abs(d - centre);
                    float alpha = Mathf.Clamp01(edge + 0.5f);
                    if (alpha > 0f && t > 0.6f)
                    {
                        // Dry brush: across the stroke nine bristles; past its own point each one
                        // lets the paper through in streaks along the stroke.
                        float across = Mathf.Clamp01((d - (centre - width * 0.5f)) / Mathf.Max(1f, width));
                        int bristle = Mathf.Min(bristles - 1, Mathf.FloorToInt(across * bristles));
                        if (t > dropAt[bristle])
                        {
                            float streak = Hash01(bristle * 13.7f + Mathf.Floor(t * 180f) * 0.37f);
                            alpha *= Mathf.Lerp(1f, streak < 0.55f ? 0.15f : 0.8f, Mathf.Clamp01((t - dropAt[bristle]) / 0.06f));
                        }
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static Sprite CreateSoftArc(int size, float spanDegrees, float innerFraction, float shoulderPower = 2f)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float outer = half - 2f;
            float inner = outer * innerFraction;
            float mid = (inner + outer) * 0.5f;
            float halfWidth = (outer - inner) * 0.5f;
            float halfSpan = spanDegrees * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Abs(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg) / halfSpan;   // 0 centre, 1 ends
                    float taper = Mathf.Clamp01(1f - Mathf.Pow(angle, shoulderPower));          // the wedge thins to its ends (2: quadratic)
                    float across = Mathf.Clamp01(1f - Mathf.Abs(d - mid) / Mathf.Max(1f, halfWidth * Mathf.Lerp(0.35f, 1f, taper)));
                    float alpha = Mathf.SmoothStep(0f, 1f, across) * taper;                    // soft, brush-like edges
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static Sprite CreateArc(int size, float spanDegrees, float strokeFraction, float endFeatherDegrees = 6f)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float radius = half - 2f;
            float stroke = radius * strokeFraction;
            float halfSpan = spanDegrees * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Abs(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);   // 0 at 12 o'clock
                    float across = Mathf.Clamp01(stroke * 0.5f - Mathf.Abs(d - (radius - stroke * 0.5f)) + 0.5f);
                    float along = Mathf.Clamp01((halfSpan - angle) / Mathf.Max(0.5f, endFeatherDegrees));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(across * along * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static Sprite CreateBandedPanel(int radius, int band, int ramp, float bandAlpha)
        {
            int r = Mathf.Max(1, radius);
            int border = Mathf.Max(r, band + ramp) + 1;
            int size = border * 2 + 4;
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - half) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - half) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float distance = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;   // < 0 inside
                    float depth = -distance;
                    float alpha = Mathf.Clamp01(0.5f - distance)
                                  * (depth < band ? bandAlpha : depth < band + ramp ? Mathf.Lerp(bandAlpha, 1f, (depth - band) / ramp) : 1f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, new Vector4(border, border, border, border));
        }

        private static Sprite CreateTick(int pitch, int height, int bar)
        {
            Texture2D texture = NewTexture(pitch, height);
            texture.wrapMode = TextureWrapMode.Repeat;
            var pixels = new Color32[pitch * height];
            int left = (pitch - bar) / 2;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    bool on = x >= left && x < left + bar;
                    pixels[y * pitch + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static Sprite CreatePip(int pitch, float diameter)
        {
            Texture2D texture = NewTexture(pitch, pitch);
            texture.wrapMode = TextureWrapMode.Repeat;
            var pixels = new Color32[pitch * pitch];
            float c = pitch * 0.5f, r = diameter * 0.5f;
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    pixels[y * pitch + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(r - d + 0.5f) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static float Hash01(float x)
        {
            float s = Mathf.Sin(x * 12.9898f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        private static Sprite CreateChevron(int size)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Triangle pointing +X, apex at the right edge.
                    float ny = Mathf.Abs(y + 0.5f - half) / half;
                    float nx = (x + 0.5f) / size;
                    float edge = 1f - ny;
                    float alpha = Mathf.Clamp01((edge - nx) * size * 0.5f + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        /// <summary>Five overlapping discs on a ring - readable as a blossom at 16-24 px.</summary>
        private static Sprite CreatePetal(int size)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float petalRadius = size * 0.20f;
            float orbit = size * 0.26f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half;
                    float py = y + 0.5f - half;

                    float best = float.MaxValue;
                    for (int i = 0; i < 5; i++)
                    {
                        float angle = Mathf.PI * 0.5f + i * (Mathf.PI * 2f / 5f);
                        float cx = Mathf.Cos(angle) * orbit;
                        float cy = Mathf.Sin(angle) * orbit;
                        float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - petalRadius;
                        if (d < best)
                        {
                            best = d;
                        }
                    }

                    float alpha = Mathf.Clamp01(0.5f - best);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        private static Sprite CreateFade(int height, bool opaqueAtBottom)
        {
            Texture2D texture = NewTexture(4, height);
            var pixels = new Color32[4 * height];

            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);
                if (!opaqueAtBottom)
                {
                    t = 1f - t;
                }

                // Smoothstep, so the veil has no visible banding edge.
                float alpha = 1f - (t * t * (3f - 2f * t));
                var value = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                for (int x = 0; x < 4; x++)
                {
                    pixels[y * 4 + x] = value;
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        /// <summary>
        /// Same smoothstep ramp as <see cref="CreateFade"/>, on the other axis. Kept as a
        /// separate method rather than an axis flag so neither ramp can be changed by
        /// accident while tuning the other.
        /// </summary>
        private static Sprite CreateFadeHorizontal(int width, bool opaqueAtLeft)
        {
            Texture2D texture = NewTexture(width, 4);
            var pixels = new Color32[width * 4];

            for (int x = 0; x < width; x++)
            {
                // Same polarity rule as CreateFade: the ramp runs opaque -> clear from
                // t = 0, so the axis is inverted for the FAR end, not the near one.
                float t = x / (float)(width - 1);
                if (!opaqueAtLeft)
                {
                    t = 1f - t;
                }

                float alpha = 1f - (t * t * (3f - 2f * t));
                var value = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                for (int y = 0; y < 4; y++)
                {
                    pixels[y * width + x] = value;
                }
            }

            texture.SetPixels32(pixels);
            return Finish(texture, Vector4.zero);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cache.Clear();
        }
    }
}
