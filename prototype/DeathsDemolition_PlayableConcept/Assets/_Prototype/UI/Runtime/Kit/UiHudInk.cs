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
