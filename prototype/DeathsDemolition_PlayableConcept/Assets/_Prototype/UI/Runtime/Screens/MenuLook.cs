using System;
using System.Globalization;
using DD.Prototype.World;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>The menu words' own ink over the live world (UiHudInk on the spine's text, an ink outline on its glyphs).</summary>
    public enum MenuHalo
    {
        None = 0,
        Small = 1,   // the Text-face lines in the spine (the PLAY sub-line, the build line)
        All = 2      // every spine word and glyph
    }

    /// <summary>
    /// WP-081 · how the main menu sits on the live world (C-00 05.10: the legibility of the spine over World V6's daylight is
    /// a decision, taken on real Player frames). One profile, applied when the world shows:
    ///   Exposure    EV added in the menu only (a menu Volume over the world's own; the run never sees it)
    ///   ScrimAlpha  the spine's text-safe scrim (C-00 decision b: 0.55)
    ///   Halo        the HUD's ink under the spine's words / glyphs
    ///   LookB       a menu-only Look B light ("wet slate after rain", the approved previz): the overcast sky, a low warm dimmed
    ///               sun, a nearer cool haze, a cool low-saturation grade - the world's light is untouched outside the menu
    /// The shipping profile is <see cref="Shipped"/>; dev builds pick another with <c>-ddMenuLook now|A|B|C|D|E</c> and tune
    /// any field with <c>-ddMenuEv</c>, <c>-ddMenuScrim</c>, <c>-ddMenuHalo none|small|all</c>, <c>-ddMenuSun elev,yaw,scale</c>,
    /// <c>-ddMenuFog start,end</c>, <c>-ddMenuGrade saturation,contrast</c> (the capture sessions behind C-00's choice).
    /// </summary>
    public sealed class MenuLook
    {
        public string Name = "now";
        public float Exposure;
        public float ScrimAlpha = UiBackdrop.ScrimAlpha;
        public MenuHalo Halo = MenuHalo.None;

        /// <summary>B (C-00 05.10): the spine's scrim as one wide soft profile - edge, column, end (UiBackdrop.SetWorldScrim).</summary>
        public bool GradientScrim;
        public float ScrimEdge = 0.92f;
        public float ScrimColumn = 0.6f;
        public float ScrimEnd = 0.48f;

        /// <summary>The fall from the column's edge to 0 is never narrower than this (fraction of the width): where the column is
        /// wider (4:3, the Mac at UI 1.2) the end moves out with it - a squeezed fall read as a line (bfinal 4:3 3.0×, Mac 5.0×).</summary>
        public float ScrimMinFall = 0.15f;

        /// <summary>Soft ink pads (SoftShadow, α <see cref="ReadoutPadAlpha"/>) behind the small words that stand on the world:
        /// the focused word with its sub-line, the level readout.</summary>
        public bool Pads;

        /// <summary>The ground of the words in the top corners (B, S-09 06.10, AD round MUSTs 1-2): each corner's own lift in the
        /// scrim's language (<see cref="UiBackdrop.SetWorldCorner(bool, RectTransform, UiBackdrop.CornerSpec)"/>) - the title top-left (S01's rest
        /// frame - Reduced Motion, an open pane, an unfocused window hold it - put V6's blossoms behind its strokes at 2.3-2.9:1), the
        /// level readout top-right instead of a pad (the pad took the empty XP rail's size and read as a smoked-glass box in the sky).</summary>
        public bool CornerLift;

        public bool LookB;
        public float SunElevation = 14f;
        public float SunYaw = 160f;
        public float SunScale = 0.5f;
        public Color SunColor = new Color(1f, 0.70f, 0.48f);          // ≈ 2900 K, low and warm
        public float FogStart = 22f;
        public float FogEnd = 150f;
        public Color FogColor = new Color(0.36f, 0.41f, 0.46f);        // slate, wet air (DEC-076's mist, darkened)
        public float Saturation = -20f;
        public float Contrast = 10f;
        public Color ColorFilter = new Color(0.86f, 0.92f, 1f);         // cold shadows

        public const string Flag = "-ddMenuLook";

        private static MenuLook s_current;

        /// <summary>The profile the menu ships with: C-00 05.10 decision B + pads (the world beside the spine untouched).</summary>
        public static MenuLook Shipped()
        {
            return Option("B");
        }

        /// <summary>The C-00 05.10 candidates.</summary>
        public static MenuLook Option(string name)
        {
            var look = new MenuLook { Name = name };
            switch ((name ?? string.Empty).ToUpperInvariant())
            {
                case "A": look.Exposure = -2.3f; break;
                case "B": look.GradientScrim = true; look.Pads = true; look.CornerLift = true; break;
                case "B0": look.ScrimAlpha = 0.92f; break;   // the first B frames: the third at 0.92 with its short fade (the edge read as a line)
                case "C": look.Halo = MenuHalo.All; break;
                case "D": look.Exposure = -1f; look.ScrimAlpha = 0.75f; look.Halo = MenuHalo.Small; break;
                case "E": look.LookB = true; look.Exposure = -0.8f; break;
                case "EB": look.LookB = true; look.Exposure = -0.8f; look.ScrimAlpha = 0.70f; break;
                case "EC": look.LookB = true; look.Exposure = -0.8f; look.Halo = MenuHalo.Small; break;
                case "NOW": break;   // the look bcde16dc shipped: the third at 0.55
                default: return Shipped();
            }

            return look;
        }

        /// <summary>The profile in force: the shipped one, or in dev builds the one the command line names and tunes.</summary>
        public static MenuLook Current
        {
            get
            {
                if (s_current == null)
                {
                    s_current = Debug.isDebugBuild ? FromArgs(Environment.GetCommandLineArgs()) : Shipped();
                }

                return s_current;
            }
        }

        /// <summary>Dev: the named option, then the per-field overrides.</summary>
        public static MenuLook FromArgs(string[] args)
        {
            string name = Arg(args, Flag);
            MenuLook look = name != null ? Option(name) : Shipped();
            if (name != null && name.ToUpperInvariant() == "NOW")
            {
                look.Name = "now";
            }
            float v;
            if (Float(Arg(args, "-ddMenuEv"), out v)) look.Exposure = v;
            if (Float(Arg(args, "-ddMenuScrim"), out v)) look.ScrimAlpha = Mathf.Clamp01(v);
            string halo = Arg(args, "-ddMenuHalo");
            if (halo != null) look.Halo = halo == "all" ? MenuHalo.All : halo == "small" ? MenuHalo.Small : MenuHalo.None;
            float[] sun = Floats(Arg(args, "-ddMenuSun"));
            if (sun.Length == 3) { look.LookB = true; look.SunElevation = sun[0]; look.SunYaw = sun[1]; look.SunScale = sun[2]; }
            float[] fog = Floats(Arg(args, "-ddMenuFog"));
            if (fog.Length == 2) { look.FogStart = fog[0]; look.FogEnd = fog[1]; }
            float[] grade = Floats(Arg(args, "-ddMenuGrade"));
            if (grade.Length == 2) { look.Saturation = grade[0]; look.Contrast = grade[1]; }
            return look;
        }

        /// <summary>Tests: forget the parsed profile.</summary>
        public static void ResetForTests()
        {
            s_current = null;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} (EV {1:+0.0;-0.0;0}, scrim {2}, halo {3}, pads {4}, corners {6}, LookB {5})",
                Name, Exposure, GradientScrim ? string.Format(CultureInfo.InvariantCulture, "{0:0.00}→{1:0.00}→0 @{2:0.00}", ScrimEdge, ScrimColumn, ScrimEnd) : ScrimAlpha.ToString("0.00", CultureInfo.InvariantCulture),
                Halo, Pads ? "on" : "off", LookB ? SunElevation + "°/" + SunYaw + "° ×" + SunScale : "off", CornerLift ? "on" : "off");
        }

        // ======================================================================== the world (menu scene only)

        /// <summary>
        /// The menu's grade and light over the world just loaded: a Volume in the menu's scene (gone with it) and, with Look B,
        /// the sky, the sun, the haze and the ambient of the menu scene (RenderSettings belong to the active scene - the menu's).
        /// Returns the Volume's object (null when nothing is graded).
        /// </summary>
        public GameObject ApplyWorld()
        {
            GameObject host = null;
            if (!Mathf.Approximately(Exposure, 0f) || LookB)
            {
                host = new GameObject("MenuLook Volume");
                var volume = host.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100f;   // over the world's Global Volume
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "MenuLook";
                ColorAdjustments grade = profile.Add<ColorAdjustments>(true);
                grade.postExposure.Override(Exposure);
                if (LookB)
                {
                    grade.saturation.Override(Saturation);
                    grade.contrast.Override(Contrast);
                    grade.colorFilter.Override(ColorFilter);
                }
                else
                {
                    grade.saturation.overrideState = false;
                    grade.contrast.overrideState = false;
                    grade.colorFilter.overrideState = false;
                    grade.hueShift.overrideState = false;
                }

                volume.sharedProfile = profile;
            }

            if (LookB)
            {
                var sky = Resources.Load<Material>(WorldAtmosphere.SharedSkyResourceName);   // the overcast Look B sky (AP-019 era)
                if (sky != null)
                {
                    RenderSettings.skybox = sky;
                }

                RenderSettings.fogColor = FogColor;
                RenderSettings.fogStartDistance = FogStart;
                RenderSettings.fogEndDistance = FogEnd;
                RenderSettings.ambientSkyColor *= 0.7f;
                RenderSettings.ambientEquatorColor *= 0.7f;
                RenderSettings.ambientGroundColor *= 0.7f;
                Light sun = CloudShadowDriver.Active != null ? CloudShadowDriver.Active.Sun : RenderSettings.sun;
                if (sun != null)
                {
                    sun.transform.rotation = Quaternion.Euler(SunElevation, SunYaw, 0f);
                    sun.color = SunColor;
                    sun.intensity *= SunScale;
                }
            }

            Debug.Log("[UI] Menu look: " + this);
            return host;
        }

        // ======================================================================== the menu's words

        /// <summary>The scrim at this profile's alpha and the halo on the spine's words and glyphs.</summary>
        public void ApplyUi(Transform spine, UiBackdrop backdrop)
        {
            if (backdrop != null)
            {
                if (GradientScrim)
                {
                    backdrop.SetWorldScrim(ScrimEdge, ScrimColumn, ScrimEnd, ScrimMinFall);
                }
                else
                {
                    backdrop.SetTextSafeAlpha(ScrimAlpha);
                }
            }

            if (Halo == MenuHalo.None || spine == null)
            {
                return;
            }

            foreach (TMP_Text text in spine.GetComponentsInChildren<TMP_Text>(true))
            {
                bool display = text.font != null && text.font.name.IndexOf("Display", StringComparison.Ordinal) >= 0;
                if (display && Halo == MenuHalo.All)
                {
                    UiHudInk.Apply(text);   // the large face takes the HUD's outline ink
                }
                else if (!display)
                {
                    ApplyUnderlayInk(text);   // a small, thin face: a soft ink ground behind it (an outline would eat its strokes)
                }
            }

            if (Halo != MenuHalo.All)
            {
                return;
            }

            Color ink = UiTokens.Get(UiColor.HudInk);
            foreach (Image image in spine.GetComponentsInChildren<Image>(true))
            {
                if (image.name == "Glyph" && image.GetComponent<Outline>() == null)
                {
                    Outline outline = image.gameObject.AddComponent<Outline>();
                    outline.effectColor = ink;
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                    outline.useGraphicAlpha = true;
                }
            }
        }

        /// <summary>
        /// The readouts that stand on the open world (the profile level top-right): the small-text ink under every word, whatever
        /// the look - the HUD's outline alone measured 2.9:1 as drawn against V6's sky (C-00 05.10 option frames).
        /// </summary>
        public static void ApplyReadoutInk(Transform readout, bool pad)
        {
            if (readout == null)
            {
                return;
            }

            foreach (TMP_Text text in readout.GetComponentsInChildren<TMP_Text>(true))
            {
                ApplyUnderlayInk(text);
            }

            // An 18 px word over open sky (luminance ~0.6) needs its ground ≥ α 0.77 SurfaceBase for 4.5:1 - no ink around
            // the strokes reaches that (drawn: 2.7:1). A soft ink pad (the kit's 9-slice soft shadow, 24 px falloff) behind the
            // readout, first sibling, the readout's rect plus its falloff.
            if (pad && readout.Find(ReadoutPadName) == null)
            {
                AddPad(readout, readout.Find(ReadoutPadName));
            }
        }

        /// <summary>The title's corner (AD round 06.10 MUST-2(b)(1), the preferred path): α ≥ 0.85 over the title's box + 24; the
        /// horizontal fall ends by 0.40 W, the vertical one 160 below the box; the column's ramp under it is its own and unchanged.
        /// The wordmark itself untouched (no outline, halo, colour or weight change).</summary>
        public const float TitleLiftAlpha = 0.85f;
        public const float TitleLiftMargin = 24f;
        public const float TitleLiftEndX = 0.40f;

        /// <summary>AD r2 (MUST-2r, 06.10): the title's horizontal fall is never shorter than 0.10 W - at the Mac's UI scale and at
        /// 21:9 the box + 24 already reaches ~0.35 W, and a fall squeezed to 0.044 W read as a seam in the sky; it ends by 0.48 W at
        /// the evidence aspects (the column ramp's own end), 0.40 W stays where the box leaves room for 0.10 W.</summary>
        public const float TitleLiftMinFallX = 0.10f;
        public const float TitleLiftMaxEndX = 0.48f;
        public const float TitleLiftBelow = 160f;

        /// <summary>The level readout's corner (AD MUST-1, path A): α over the readout's reach (+ <see cref="CornerMargin"/>), 0 by
        /// <see cref="CornerFall"/> of the width both ways (AD: ≥ 0.12 W). 0.88 holds TextPrimary at ≥ 4.5:1 on its plain ground, no
        /// ink counted, over V6's brightest cloud (luminance ~1.0; the old pad at 0.82 measured 3.9:1 there at 21:9).</summary>
        public const float CornerAlpha = 0.88f;
        public const float CornerMargin = 8f;
        public const float CornerFall = 0.14f;

        public static UiBackdrop.CornerSpec TitleCorner => new UiBackdrop.CornerSpec
        {
            Margin = TitleLiftMargin, Alpha = TitleLiftAlpha, EndX = TitleLiftEndX, MinFallX = TitleLiftMinFallX, EndBelow = TitleLiftBelow
        };

        public static UiBackdrop.CornerSpec ReadoutCorner => new UiBackdrop.CornerSpec { Margin = CornerMargin, Alpha = CornerAlpha, Fall = CornerFall };

        /// <summary>The words in the top corners on their lifts (<see cref="CornerLift"/>): the title top-left, the level readout
        /// top-right; the backdrop keeps them on their words when the screen changes size. False when this look has none or there
        /// is no backdrop - the caller keeps the readout's pad.</summary>
        public bool LiftCorners(RectTransform title, RectTransform readout, UiBackdrop backdrop)
        {
            if (!CornerLift || backdrop == null)
            {
                return false;
            }

            Canvas.ForceUpdateCanvases();
            if (title != null)
            {
                backdrop.SetWorldCorner(false, title, TitleCorner);
            }

            if (readout != null)
            {
                backdrop.SetWorldCorner(true, readout, ReadoutCorner);
            }

            return readout != null;
        }

        /// <summary>A soft ink pad as <paramref name="host"/>'s first child: the host's rect plus the pad's falloff, SurfaceBase α 0.82.</summary>
        public static Image AddPad(Transform host, Transform existing)
        {
            if (existing != null)
            {
                return existing.GetComponent<Image>();
            }

            Image pad = UiFactory.NewImage(ReadoutPadName, host, UiColor.SurfaceBase, UiSprites.SoftShadow);
            pad.type = Image.Type.Sliced;
            pad.raycastTarget = false;
            Color c = pad.color;
            c.a = ReadoutPadAlpha;
            pad.color = c;
            RectTransform rect = UiFactory.Fill(pad.rectTransform);
            rect.offsetMin = new Vector2(-PadReach, -PadReach);
            rect.offsetMax = new Vector2(PadReach, PadReach);
            pad.transform.SetAsFirstSibling();
            return pad;
        }

        /// <summary>
        /// A pad behind one text that has no container of its own (the build line): a host with the text's rect, before it in
        /// draw order, holding the pad over the text's drawn bounds plus the reach. Built once; the words do not change.
        /// </summary>
        public static Image PadBehind(TMP_Text text)
        {
            if (text == null || text.transform.parent == null)
            {
                return null;
            }

            Transform parent = text.transform.parent;
            string hostName = text.name + "." + ReadoutPadName;
            Transform existing = parent.Find(hostName);
            if (existing != null)
            {
                return existing.GetComponentInChildren<Image>(true);
            }

            RectTransform source = text.rectTransform;
            RectTransform host = UiFactory.NewRect(hostName, parent);
            host.anchorMin = source.anchorMin;
            host.anchorMax = source.anchorMax;
            host.pivot = source.pivot;
            host.anchoredPosition = source.anchoredPosition;
            host.sizeDelta = source.sizeDelta;
            host.SetSiblingIndex(source.GetSiblingIndex());
            text.ForceMeshUpdate();
            Bounds bounds = text.textBounds;
            Image pad = UiFactory.NewImage(ReadoutPadName, host, UiColor.SurfaceBase, UiSprites.SoftShadow);
            pad.type = Image.Type.Sliced;
            pad.raycastTarget = false;
            Color c = pad.color;
            c.a = ReadoutPadAlpha;
            pad.color = c;
            RectTransform rect = pad.rectTransform;
            rect.anchorMin = host.pivot;
            rect.anchorMax = host.pivot;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = bounds.center;
            rect.sizeDelta = new Vector2(bounds.size.x + 2f * PadReach, bounds.size.y + 2f * PadReach);
            return pad;
        }

        /// <summary>How far a pad reaches past its host on each side (the soft shadow's falloff plus a small space).</summary>
        public static float PadReach => UiSprites.SoftShadowReach + (int)UiSpace.S;

        public const string ReadoutPadName = "InkPad";

        /// <summary>The readout pad's alpha (≥ 0.77 holds TextPrimary at 4.5:1 over V6's brightest sky; the edge is soft).</summary>
        public const float ReadoutPadAlpha = 0.82f;

        private static readonly System.Collections.Generic.Dictionary<int, Material> s_underlay = new System.Collections.Generic.Dictionary<int, Material>();

        /// <summary>The small-text ink: TMP's underlay only - HudInk, centred, dilated past the strokes and soft - no outline.</summary>
        private static void ApplyUnderlayInk(TMP_Text text)
        {
            if (text == null || text.font == null || text.font.material == null || !text.font.material.HasProperty(ShaderUtilities.ID_UnderlayColor))
            {
                return;
            }

            int key = text.font.GetInstanceID();
            if (!s_underlay.TryGetValue(key, out Material material) || material == null)
            {
                material = new Material(text.font.material) { name = text.font.name + " (MenuInk)", hideFlags = HideFlags.DontSave };
                Color ink = UiTokens.Get(UiColor.HudInk);
                ink.a = 0.92f;
                material.DisableKeyword(ShaderUtilities.Keyword_Outline);
                material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                material.SetColor(ShaderUtilities.ID_UnderlayColor, ink);
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
                material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 1f);
                material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.6f);
                s_underlay[key] = material;
            }

            text.fontSharedMaterial = material;
        }

        // ======================================================================== args

        private static string Arg(string[] args, string flag)
        {
            for (int i = 0; args != null && i + 1 < args.Length; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool Float(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static float[] Floats(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new float[0];
            }

            string[] parts = text.Split(',');
            var values = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!Float(parts[i], out values[i]))
                {
                    return new float[0];
                }
            }

            return values;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null;
        }
    }
}
