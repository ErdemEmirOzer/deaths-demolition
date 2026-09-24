#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-022M: keeps TMP's dynamic fallback font assets out of the working tree.
    ///
    /// In the Editor a play session (every PlayMode test) draws text through the project's
    /// persistent font assets. The primary face is static, but its fallback
    /// (<c>LiberationSans SDF - Fallback</c>) is Dynamic: every glyph it rasterises is written
    /// into the asset and its atlas texture, TMP marks both dirty, and the next asset save
    /// puts them on disk - a dirty tree, and a release build stamped <c>-dirty</c>.
    ///
    /// For the length of the play session each persistent Dynamic fallback in the default
    /// and UI font chains is swapped for an in-memory twin created from the same source
    /// font with the same sampling size, padding, render mode and atlas size, and a copy of
    /// the original material (so the SDF look is identical). Glyph coverage is unchanged: the
    /// twin rasterises on demand from the same font file. The persistent asset is never
    /// touched, so nothing is dirtied; the original list entries are put back when play
    /// ends. Editor-only: a Player has no persistent assets, and TMP's
    /// "Clear Dynamic Data On Build" already empties the fallback in the build.
    /// </summary>
    internal static class UiFontFallbackGuard
    {
        private struct Swap
        {
            public TMP_FontAsset Owner;
            public int Index;
            public TMP_FontAsset Original;
            public TMP_FontAsset Twin;
        }

        private static readonly List<Swap> s_swaps = new List<Swap>();

        /// <summary>Swaps performed in this play session (test readout).</summary>
        internal static int ActiveSwapCount => s_swaps.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            Restore(); // a previous session without domain reload
            Guard(TMP_Settings.defaultFontAsset);
            Guard(Resources.Load<TMP_FontAsset>(UiFonts.ProjectFontResource));
            // WP-068 slice 3: the three v3 role faces (their fallback chains too).
            Guard(Resources.Load<TMP_FontAsset>(UiFonts.DisplayFontResource));
            Guard(Resources.Load<TMP_FontAsset>(UiFonts.TextFontResource));
            Guard(Resources.Load<TMP_FontAsset>(UiFonts.NumericFontResource));
            if (s_swaps.Count > 0)
            {
                Application.quitting -= Restore;
                Application.quitting += Restore; // fires when the Editor leaves play mode
            }
        }

        private static void Guard(TMP_FontAsset owner)
        {
            if (owner == null || owner.fallbackFontAssetTable == null)
            {
                return;
            }

            List<TMP_FontAsset> table = owner.fallbackFontAssetTable;
            for (int i = 0; i < table.Count; i++)
            {
                TMP_FontAsset original = table[i];
                if (original == null
                    || original.atlasPopulationMode != AtlasPopulationMode.Dynamic
                    || !EditorUtility.IsPersistent(original)
                    || original.sourceFontFile == null)
                {
                    continue;
                }

                TMP_FontAsset twin = CreateTwin(original);
                if (twin == null)
                {
                    continue;
                }

                table[i] = twin;
                s_swaps.Add(new Swap { Owner = owner, Index = i, Original = original, Twin = twin });
            }
        }

        private static TMP_FontAsset CreateTwin(TMP_FontAsset original)
        {
            TMP_FontAsset twin = TMP_FontAsset.CreateFontAsset(
                original.sourceFontFile,
                original.creationSettings.pointSize > 0 ? original.creationSettings.pointSize : (int)original.faceInfo.pointSize,
                original.atlasPadding,
                original.atlasRenderMode,
                original.atlasWidth,
                original.atlasHeight,
                AtlasPopulationMode.Dynamic,
                original.isMultiAtlasTexturesEnabled);
            if (twin == null)
            {
                return null;
            }

            twin.name = original.name + " (play session)";
            twin.hideFlags = HideFlags.DontSave;
            twin.fallbackFontAssetTable = new List<TMP_FontAsset>(original.fallbackFontAssetTable ?? new List<TMP_FontAsset>());
            if (twin.atlasTexture != null)
            {
                twin.atlasTexture.hideFlags = HideFlags.DontSave;
            }

            if (original.material != null && twin.material != null)
            {
                // Same shader and SDF properties as the authored material; only the atlas differs.
                Texture atlas = twin.material.GetTexture(ShaderUtilities.ID_MainTex);
                twin.material.shader = original.material.shader;
                twin.material.CopyPropertiesFromMaterial(original.material);
                twin.material.SetTexture(ShaderUtilities.ID_MainTex, atlas);
                twin.material.hideFlags = HideFlags.DontSave;
            }

            return twin;
        }

        private static void Restore()
        {
            Application.quitting -= Restore;
            for (int i = s_swaps.Count - 1; i >= 0; i--)
            {
                Swap swap = s_swaps[i];
                List<TMP_FontAsset> table = swap.Owner != null ? swap.Owner.fallbackFontAssetTable : null;
                if (table != null && swap.Index < table.Count && table[swap.Index] == swap.Twin)
                {
                    table[swap.Index] = swap.Original;
                }

                // The twin is DontSave and is not destroyed here: text objects still being torn
                // down after `quitting` may reference its material; the next domain reload frees it.
            }

            s_swaps.Clear();
        }
    }
}
#endif
