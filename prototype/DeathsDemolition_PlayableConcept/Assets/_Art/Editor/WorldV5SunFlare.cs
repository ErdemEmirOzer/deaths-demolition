using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 · DEC-102 · The high sun's flare: an SRP lens flare (core RP 14 LensFlareComponentSRP, drawn by URP's post pass) on the
    /// v6 world's baked sun — a warm procedural glow at the sun and two faint cool ghosts along the flare line, occluded by the depth
    /// buffer (roofs, trees, the bamboo hide it). Kept subtle: the bloom does the glare; the flare only says "strong sun".
    /// </summary>
    public static class WorldV5SunFlare
    {
        public static string AssetPath => GreyboxV6Builder.DataFolder + "/Sky/DD_SunFlare.asset";

        public static LensFlareDataSRP EnsureAsset()
        {
            var data = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(AssetPath);
            bool created = data == null;
            if (created) data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            data.elements = new[]
            {
                // A linear cone (edge offset 0, fall-off 1): 0.35 flattened the circle into a pale 25° "moon" in pass 2.
                Element(SRPLensFlareType.Circle, 0f, 0.14f, 2.5f, new Color(1f, 0.90f, 0.76f), 1f),
                Element(SRPLensFlareType.Polygon, 0.55f, 0.05f, 0.7f, new Color(0.75f, 0.85f, 1f), 1f),
                Element(SRPLensFlareType.Circle, 1.2f, 0.04f, 0.4f, new Color(0.85f, 0.90f, 1f), 1f),
            };
            if (created) AssetDatabase.CreateAsset(data, AssetPath);
            else EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>Puts the flare on <paramref name="sun"/> (idempotent).</summary>
        public static void Attach(Light sun)
        {
            var flare = sun.GetComponent<LensFlareComponentSRP>();
            if (flare == null) flare = sun.gameObject.AddComponent<LensFlareComponentSRP>();
            flare.lensFlareData = EnsureAsset();
            flare.intensity = 1f;
            flare.scale = 1f;
            flare.useOcclusion = true;
            flare.occlusionRadius = 0.02f;
            flare.sampleCount = 16;
            flare.allowOffScreen = false;
        }

        private static LensFlareDataElementSRP Element(SRPLensFlareType type, float position, float intensity, float scale, Color tint, float fallOff)
        {
            return new LensFlareDataElementSRP
            {
                flareType = type,
                position = position,
                localIntensity = intensity,
                uniformScale = scale,
                tint = tint,
                fallOff = fallOff,
                edgeOffset = type == SRPLensFlareType.Circle ? 0f : 0.1f,   // a circle's full-radius ramp; the polygon scales its SDF by it
                modulateByLightColor = true,
                sideCount = 6,
            };
        }
    }
}
