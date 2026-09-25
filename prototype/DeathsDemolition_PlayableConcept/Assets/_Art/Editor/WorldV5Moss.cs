using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 C7b · The World v5 ground outside the plaza on DD/GroundMoss. C-00's note (light v5 list): the kit moss read as a flat,
    /// saturated, single-tone "golf green", while the HF frames show dark, wet, patterned moss with gravel, soil and leaf patches and
    /// gravel / soil bands along the paths. The greybox ground box keeps its geometry and collider; its material becomes
    /// V5_GroundMoss (the AP-023 moss / gravel and the ADR-015-1 earth, world XZ), and the path bands come from a world mask
    /// (T_V5_PathMask.asset, 256² RG16 over the 170 m ground: R gravel core 1.2 m, G soil shoulder 2.6 m) drawn from the layout's own lanes —
    /// the bamboo lanes, the garden paths — and the gate approaches without one.
    /// </summary>
    public static class WorldV5Moss
    {
        public const string ShaderName = "DD/GroundMoss";
        public const int MaskSize = 256;
        public const float GravelHalfWidth = 1.2f, SoilHalfWidth = 2.6f;
        public static string MaterialPath => GreyboxV6Builder.DataFolder + "/Materials/V5_GroundMoss.mat";
        public static string OldMaterialPath => GreyboxV6Builder.DataFolder + "/Materials/V5_Ground.mat";
        public static string MaskPath => GreyboxV6Builder.DataFolder + "/Ground/T_V5_PathMask.asset";
        public static float MaskHalf => GreyboxV6Builder.VistaOuter;

        /// <summary>Gate approaches with no lane or garden path (v6 layout): the south shore to the causeway, the bridge's south
        /// end, the tea house from its gate, the service yard, the plaza's south exit to the causeway and the bridge.</summary>
        public static readonly Vector2[][] ApproachPaths =
        {
            new[] { new Vector2(-16f, -52f), new Vector2(-14f, -42f), new Vector2(-9f, -34f) },
            new[] { new Vector2(4f, -52f), new Vector2(3.5f, -37f) },
            new[] { new Vector2(-44f, -44f), new Vector2(-40f, -37f), new Vector2(-33.8f, -32f) },
            new[] { new Vector2(-46f, 46f), new Vector2(-40f, 42f) },
            new[] { new Vector2(0f, -18f), new Vector2(-5f, -19.5f), new Vector2(-8f, -20.5f) },
            new[] { new Vector2(0f, -18f), new Vector2(3.5f, -21f) },
        };

        public static IEnumerable<Vector2[]> Paths()
        {
            foreach (Vector2[] p in GreyboxV6Builder.BambooLanes) yield return p;
            foreach (Vector2[] p in GreyboxV6Builder.GardenPaths) yield return p;
            foreach (Vector2[] p in ApproachPaths) yield return p;
        }

        /// <summary>Puts the ground box on V5_GroundMoss (mask rebuilt in place). Returns the material.</summary>
        public static Material Apply(Transform greybox)
        {
            Transform ground = greybox.Find("Ground");
            if (ground == null) throw new InvalidOperationException("greybox Ground missing");
            Texture2D mask = BuildPathMask();
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException(ShaderName + " missing");
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, MaterialPath);
            }

            m.shader = shader;
            // The shader's defaults are the tuning (a rebuild re-reads them): reset, then bind the textures.
            var defaults = new Material(shader);
            m.CopyPropertiesFromMaterial(defaults);
            UnityEngine.Object.DestroyImmediate(defaults);
            Material moss = Library("moss"), gravel = Library("gravel");
            var earth = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.ArtRoot + "/Materials/M_earth.mat");
            if (earth == null) throw new InvalidOperationException("M_earth missing");
            m.SetTexture("_MossMap", moss.GetTexture("_BaseMap"));
            m.SetTexture("_MossNormal", moss.GetTexture("_BumpMap"));
            m.SetTexture("_GravelMap", gravel.GetTexture("_BaseMap"));
            m.SetTexture("_GravelNormal", gravel.GetTexture("_BumpMap"));
            m.SetTexture("_EarthMap", earth.GetTexture("_BaseMap"));
            m.SetTexture("_EarthNormal", earth.GetTexture("_BumpMap"));
            m.SetTexture("_PathMask", mask);
            m.SetVector("_PathBounds", new Vector4(-MaskHalf, -MaskHalf, 2f * MaskHalf, 2f * MaskHalf));
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            ground.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (AssetDatabase.LoadAssetAtPath<Material>(OldMaterialPath) != null) AssetDatabase.DeleteAsset(OldMaterialPath);   // the Lit moss copy it replaces
            return m;
        }

        /// <summary>R = gravel core (≤ 1.2 m from a path line), G = soil shoulder (≤ 2.6 m), soft 0.5–0.9 m edges; linear, clamped, mipped.</summary>
        public static Texture2D BuildPathMask()
        {
            var lines = new List<Vector2[]>(Paths());
            var data = new byte[MaskSize * MaskSize * 2];
            float texel = 2f * MaskHalf / MaskSize;
            for (int y = 0; y < MaskSize; y++)
            for (int x = 0; x < MaskSize; x++)
            {
                var p = new Vector2(-MaskHalf + (x + 0.5f) * texel, -MaskHalf + (y + 0.5f) * texel);
                float d = float.MaxValue;
                foreach (Vector2[] line in lines) d = Mathf.Min(d, GreyboxV6Builder.DistanceToPolyline(p, line));
                int i = (y * MaskSize + x) * 2;
                data[i] = (byte)Mathf.RoundToInt(255f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GravelHalfWidth - 0.25f, GravelHalfWidth + 0.25f, d))));
                data[i + 1] = (byte)Mathf.RoundToInt(255f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SoilHalfWidth - 0.4f, SoilHalfWidth + 0.5f, d))));
            }

            var built = new Texture2D(MaskSize, MaskSize, TextureFormat.RG16, true, true) { name = "T_V5_PathMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            built.SetPixelData(data, 0);
            built.Apply(true, false);
            EnsureFolder(System.IO.Path.GetDirectoryName(MaskPath).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(built, MaskPath);
                return built;
            }

            EditorUtility.CopySerialized(built, existing);
            UnityEngine.Object.DestroyImmediate(built);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Material Library(string role)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.ArtRoot + "/Materials/M_ap023_" + role + ".mat");
            if (m == null) throw new InvalidOperationException("M_ap023_" + role + " missing");
            return m;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
