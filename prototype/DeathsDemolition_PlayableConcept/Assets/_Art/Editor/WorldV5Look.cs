using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 C6 · The World v5 (v6 layout) look profile: DD_Look_v5 is DD_Overcast_v1 (its Neutral tonemap / grade / bloom / grain /
    /// vignette, copied once) with the colour lookup on LUT v2 "Islak Arduvaz, Açılma" (DD_Look_v2.png, WP-075 Ek A §6.1;
    /// wp-074/tools/build-look-v2.py, ΔE gate in DD_Look_v2.recipe.json). World_AP005 keeps DD_Overcast_v1 + LUT v1 until World v5
    /// replaces it. The C7 post rules are written into this same profile.
    /// </summary>
    public static class WorldV5Look
    {
        public const string ProfilePath = ArtLibraryPaths.LightingFolder + "/DD_Look_v5.asset";
        public const string LutPath = LutBuilder.Folder + "/DD_Look_v2.png";
        public const string RecipePath = LutBuilder.Folder + "/DD_Look_v2.recipe.json";

        public static VolumeProfile EnsureProfile()
        {
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) == null && !AssetDatabase.CopyAsset(ArtLibraryPaths.OvercastVolumeProfile, ProfilePath))
                throw new InvalidOperationException("cannot copy " + ArtLibraryPaths.OvercastVolumeProfile + " → " + ProfilePath);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            var lut = AssetDatabase.LoadAssetAtPath<Texture2D>(LutPath);
            if (lut == null) throw new InvalidOperationException("LUT v2 missing: " + LutPath + " (wp-074/tools/build-look-v2.py)");
            if (!profile.TryGet(out ColorLookup lookup)) lookup = profile.Add<ColorLookup>();
            lookup.active = true;
            lookup.texture.overrideState = true;
            lookup.texture.value = lut;
            lookup.contribution.overrideState = true;
            lookup.contribution.value = 1f;
            EditorUtility.SetDirty(lookup);
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
