using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 C6 · The World v5 (v6 layout) look profile: DD_Look_v5 is DD_Overcast_v1 (its Neutral tonemap / grade / bloom / grain /
    /// vignette, copied once) with the colour lookup on LUT v2b "Islak Arduvaz, Soğuk" — DEC-101 (user 25.09): the tone is cold like
    /// the HF targets (cold blue-grey shadows, low saturation, dark wet ground, cold white point, contrast up, no milky lift; warmth
    /// only in the sun-hit highlights and the shafts). v2b's global sliders are fitted to HF-center's mean Lab / chroma / L std
    /// (wp-074/tools/build-look-v2b.py; the fit, the ΔE gate and the HF comparison in DD_Look_v2b.recipe.json); LUT v2 "Açılma"
    /// (Ek A §6.1) stays as the fit's input. World_AP005 keeps DD_Overcast_v1 + LUT v1 until World v5 replaces it. WP-075 C7: the
    /// post rules' Cinematic top-down baseline is written into this same profile (LookV3Post).
    /// </summary>
    public static class WorldV5Look
    {
        public const string ProfilePath = ArtLibraryPaths.LightingFolder + "/DD_Look_v5.asset";
        public const string LutPath = LutBuilder.Folder + "/DD_Look_v2b.png";
        public const string RecipePath = LutBuilder.Folder + "/DD_Look_v2b.recipe.json";

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
            // WP-075 C7: the post baseline the editor shows (Cinematic, top-down); LookV3Post re-applies by tier / camera mode at runtime.
            LookV3Post.Write(profile, LookV3Post.RulesFor(QualityTier.Sinematik, CameraMode.TopDown), 10f);
            foreach (VolumeComponent component in profile.components)
            {
                if (!AssetDatabase.Contains(component))
                {
                    component.name = component.GetType().Name;
                    component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(component, profile);
                }

                EditorUtility.SetDirty(component);
            }

            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
