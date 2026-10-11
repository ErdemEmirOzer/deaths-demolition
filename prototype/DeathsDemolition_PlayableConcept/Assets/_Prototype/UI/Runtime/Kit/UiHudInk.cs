using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 8-R · the boxless HUD's text ink. With no panel behind them, numbers and words carry their own ground:
    /// - an outline in HudInk (Sumi900 α 0.98, not InkStroke 0.90: over a white pixel 0.98 gives 12.1:1, 0.90 only 5.9);
    /// - a soft underlay shadow down-right (α 0.85);
    /// - a slight face dilate, so the ink sits mostly outside the letter and the counters stay open at 16 px.
    /// Widths are per size bucket, so every size gets about 1.2–1.5 px of ink outside the face. The outline is in SDF
    /// units, so it scales with TextScale and UI scale.
    /// One material per (face, bucket), built once per process; <see cref="Apply"/> swaps a label's shared material.
    /// </summary>
    public static class UiHudInk
    {
        private struct Bucket
        {
            public float MaxSize;
            public float Width;
            public float Dilate;
        }

        // Width in TMP SDF units per atlas: px outside ≈ Width × GradientScale × size / pointSize / 2 + dilate share.
        private static readonly Bucket[] Buckets =
        {
            new Bucket { MaxSize = 17f, Width = 0.42f, Dilate = 0.05f },
            new Bucket { MaxSize = 34f, Width = 0.30f, Dilate = 0.06f },
            new Bucket { MaxSize = 999f, Width = 0.22f, Dilate = 0.06f }
        };

        // A soft, faint contact shadow (depth), not a second outline: the crisp edge is the outline's job (8-R r1 frames
        // read "sticker" at 0.85 / 0.4 / 0.25).
        private const float UnderlayAlpha = 0.55f;
        private const float UnderlayOffset = 0.25f;
        private const float UnderlaySoftness = 0.6f;

        private static readonly Dictionary<long, Material> s_materials = new Dictionary<long, Material>();

        /// <summary>
        /// WP-105 (R4 audit V-52 / V-53): the HALO weight - the same HudInk, as a ring round every letter instead of an edge
        /// plus a contact shadow. The words the player reads under fire (RELOAD, LOW AMMO, LOW, SURROUNDED ...) and the HUD's
        /// secondary lines (the weapon line, "/ max", the gold, "recovery in") measured 1.84–3.61:1 over the bright courtyard
        /// stone with the base ink: its outline is ≈ 0.5 px at 15 px on the Text face (padding 6 at 44 pt) and eats into the
        /// face by about as much. The halo is the underlay, centred (no offset) and dilated to the atlas's reach, in HudInk at
        /// the outline's alpha: it is drawn BEHIND the face (it never thins a stem), fills the letters' counters, and reaches
        /// (estimated from the atlases' gradient scales and TMP's ScaleRatioC) ≈ 1.1 px solid round a 20 px word on the Text
        /// face (GS 7 at 44 pt) and ≈ 1.7 px round a 22 px numeral on the Numeric face (GS 13 at 64 pt), against ≈ 0.5 px for
        /// the base outline - the most the two distance fields hold (ScaleRatioC clamps any larger dilate back to it; a wider
        /// halo needs a Text SDF with more padding). The outline runs at width 0: the halo is the ground, and the face
        /// keeps its full weight. Not a surface: the ink follows the letters and nothing else (KIT-KILAVUZU: a trace, not a surface).
        /// The menus keep <see cref="Apply"/>; only the HUD's alert and secondary words call this.
        /// </summary>
        public const float HaloAlpha = UiTokens.HudInkAlpha;

        /// <summary>
        /// The halo underlay's dilate and softness in TMP SDF units (ScaleRatioC scales them to what the atlas holds). Softness
        /// 0: any softness is taken out of the solid reach (ScaleRatioC divides by dilate + softness); the SDF's own edge is the
        /// halo's anti-aliasing.
        /// R4 WP-105 (C-00 12.10): 0.9, not 1 - at 1 the persistent HUD measured 1.50019 % of a 1080p screen at XXXVIII against
        /// the 1.5 % cap (HudCoverageTests; the base was 1.43 %). ScaleRatioC divides by max(1, dilate + softness), so below 1
        /// the reach shrinks with the dilate: ≈ 1.0 px round a 20 px word, ≈ 1.5 px round the 22 px gold. The contrast is the
        /// halo's alpha over the world (HudOkunurlukTests.Hale_*), which this does not touch.
        /// </summary>
        public const float HaloDilate = 0.9f;
        public const float HaloSoftness = 0f;

        /// <summary>The material key's halo bit (buckets use bits 0-1).</summary>
        private const long HaloKeyBit = 8L;

        /// <summary>Give a label the HUD ink at its current font size (call after the size is set; not per frame).</summary>
        public static void Apply(TMP_Text text)
        {
            if (text == null || text.font == null || text.font.material == null)
            {
                return;
            }

            int bucket = 0;
            while (bucket < Buckets.Length - 1 && text.fontSize > Buckets[bucket].MaxSize)
            {
                bucket++;
            }

            long key = ((long)text.font.GetInstanceID() << 4) | (long)bucket;
            Material material;
            if (!s_materials.TryGetValue(key, out material) || material == null)
            {
                material = Build(text.font, Buckets[bucket]);
                s_materials[key] = material;
            }

            if (material != null && text.fontSharedMaterial != material)
            {
                text.fontSharedMaterial = material;
            }
        }

        /// <summary>
        /// WP-105: give a HUD label the halo (see <see cref="HaloAlpha"/>). One material per face, built once per process; call
        /// after the size is set, never per frame. A face whose shader has no underlay keeps the base ink.
        /// </summary>
        public static void ApplyHalo(TMP_Text text)
        {
            if (text == null || text.font == null || text.font.material == null)
            {
                return;
            }

            long key = ((long)text.font.GetInstanceID() << 4) | HaloKeyBit;
            Material material;
            if (!s_materials.TryGetValue(key, out material) || material == null)
            {
                material = BuildHalo(text.font);
                s_materials[key] = material;
            }

            if (material == null)
            {
                Apply(text);
                return;
            }

            if (text.fontSharedMaterial != material)
            {
                text.fontSharedMaterial = material;
            }
        }

        /// <summary>The label wears the halo material (tests, captures).</summary>
        public static bool HasHalo(TMP_Text text)
        {
            if (text == null || text.font == null)
            {
                return false;
            }

            Material material;
            return s_materials.TryGetValue(((long)text.font.GetInstanceID() << 4) | HaloKeyBit, out material) && material != null &&
                   text.fontSharedMaterial == material;
        }

        private static Material BuildHalo(TMP_FontAsset font)
        {
            Material face = font.material;
            if (!face.HasProperty(ShaderUtilities.ID_UnderlayColor) || !face.HasProperty(ShaderUtilities.ID_UnderlayDilate))
            {
                return null;
            }

            Color ink = UiTokens.Get(UiColor.HudInk);
            ink.a = HaloAlpha;
            var material = new Material(face) { name = font.name + " (HudInk halo)", hideFlags = HideFlags.DontSave };
            // OUTLINE_ON stays on at width 0 (the face draws exactly as with no outline): the mobile SDF shader's keywords are
            // shader_feature, so a player build holds only the combinations some build material uses. OUTLINE_ON + UNDERLAY_ON
            // ships (TMP's own "LiberationSans SDF - Drop Shadow" in Resources - the pair Apply has always run on);
            // UNDERLAY_ON alone has no build material and could be stripped.
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            if (material.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
            }

            if (material.HasProperty(ShaderUtilities.ID_OutlineColor))
            {
                material.SetColor(ShaderUtilities.ID_OutlineColor, ink);
            }

            if (material.HasProperty(ShaderUtilities.ID_FaceDilate))
            {
                material.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
            }

            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, ink);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, HaloDilate);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, HaloSoftness);
            ShaderUtilities.UpdateShaderRatios(material);
            return material;
        }

        private static Material Build(TMP_FontAsset font, Bucket bucket)
        {
            Material face = font.material;
            if (!face.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                return null;
            }

            Color ink = UiTokens.Get(UiColor.HudInk);
            Color shadow = ink;
            shadow.a = UnderlayAlpha;
            var material = new Material(face) { name = font.name + " (HudInk " + bucket.Width.ToString("0.00") + ")", hideFlags = HideFlags.DontSave };
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, bucket.Width);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, bucket.Dilate);
            material.SetColor(ShaderUtilities.ID_OutlineColor, ink);
            if (material.HasProperty(ShaderUtilities.ID_UnderlayColor))
            {
                material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                material.SetColor(ShaderUtilities.ID_UnderlayColor, shadow);
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, UnderlayOffset);
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -UnderlayOffset);
                material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, UnderlaySoftness);
            }

            return material;
        }
    }
}
