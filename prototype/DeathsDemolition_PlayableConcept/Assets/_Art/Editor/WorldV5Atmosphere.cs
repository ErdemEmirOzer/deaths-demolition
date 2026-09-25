using System;
using System.Collections.Generic;
using DD.Prototype.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 C4 / C5 in the World v5 dev scene (WP-075 direction Ek A §3 A and §5.1; the regions translated to the v6 layout).
    /// Sun shaft cards (DD/ShaftCard) sit in the canopy gaps and openings the 232° sun reaches through: the west bamboo lanes (the
    /// signature shafts), the east sakura court, the palace veranda, the south bridge, the K1 kairō's west door and the azumaya.
    /// None is within 10 m of the centre. Ground fog sheets (DD/FogSheet, ≤ 12 % alpha) lie over each pond, at the bamboo base,
    /// along the rear kairō and under the sakura. Both groups are ordered by priority and cut per tier by AtmosphereTierGroup
    /// (QUALITY-TIERS-V2 r2: shafts 6 ×0.7 / 10 / 20, fog sheets 4 ×0.6 / 7 ×0.8 / 10). Visual only: no colliders, out of the
    /// bake (Environment layer + modifier).
    /// </summary>
    public static class WorldV5Atmosphere
    {
        public const string ShaftGroupName = "Shafts (C4)", FogGroupName = "GroundFog (C5)", VistaMistGroupName = "VistaMist (DEC-101)", GroveMistGroupName = "GroveMist (DEC-101)";
        public const float GroveScatter = 0.35f;

        /// <summary>DEC-101 (C-00: the west grove read too dark and clear): stand-level mist over the bamboo stands either side of the main
        /// lane (not over the lanes, so the walk band keeps the Ek A ≤ 12 % local fog), y 3.5 m; High / Cinematic only.</summary>
        public static readonly (Vector2 at, Vector2 size, float y, float alpha)[] GroveMist =
        {
            (new Vector2(-50.5f, 0f), new Vector2(5f, 50f), 3.5f, 0.10f), (new Vector2(-40f, 0f), new Vector2(3.5f, 50f), 3.5f, 0.10f),
        };

        /// <summary>
        /// DEC-101 aerial perspective: mist sheets over the vista band outside the ring (58–90 m), two layers (y 3 m / 8 m), so the
        /// sugi and the hills fade layer by layer like the HF frames (0.06 / 0.04: the near forest stays dark and green like HF's pines, the plates carry the haze). Outside the play area, so the ≤ 12 % local-fog rule (Ek A §5)
        /// does not bind them; tiered 4 / 8 / 8.
        /// </summary>
        public static readonly (Vector2 at, Vector2 size, float y, float alpha)[] VistaMist =
        {
            (new Vector2(0f, 74f), new Vector2(180f, 32f), 3f, 0.06f), (new Vector2(0f, -74f), new Vector2(180f, 32f), 3f, 0.06f),
            (new Vector2(74f, 0f), new Vector2(32f, 116f), 3f, 0.06f), (new Vector2(-74f, 0f), new Vector2(32f, 116f), 3f, 0.06f),
            (new Vector2(0f, 76f), new Vector2(180f, 28f), 8f, 0.04f), (new Vector2(0f, -76f), new Vector2(180f, 28f), 8f, 0.04f),
            (new Vector2(76f, 0f), new Vector2(28f, 124f), 8f, 0.04f), (new Vector2(-76f, 0f), new Vector2(28f, 124f), 8f, 0.04f),
        };
        public const float CentreClear = 10f;
        public static string MeshFolder => GreyboxV6Builder.DataFolder + "/Atmosphere";

        /// <summary>Priority order (tier counts cut from the end): region, top point XZ, top height, length, width, peak alpha.</summary>
        public static readonly (string region, Vector2 at, float top, float length, float width, float alpha)[] Shafts =
        {
            ("Bamboo", new Vector2(-45f, -20f), 11f, 12f, 2.0f, 0.22f), ("Bamboo", new Vector2(-45f, -6f), 12f, 14f, 2.5f, 0.22f),
            ("Bamboo", new Vector2(-45f, 8f), 11f, 10f, 1.5f, 0.22f), ("Bamboo", new Vector2(-45f, 20f), 12f, 13f, 2.2f, 0.22f),
            ("Sakura", new Vector2(33f, 4f), 9f, 10f, 2.5f, 0.20f), ("Sakura", new Vector2(39f, -2f), 9f, 12f, 3.0f, 0.20f),
            ("Veranda", new Vector2(-10f, 37f), 7f, 8f, 1.5f, 0.18f), ("Veranda", new Vector2(10f, 37f), 7f, 9f, 1.5f, 0.18f),
            ("Bridge", new Vector2(4f, -24f), 9f, 8f, 2.0f, 0.14f), ("Kairo", new Vector2(-35.2f, 0f), 3.2f, 8f, 1.2f, 0.22f),
            // High ends here (10); Cinematic (r2: 20) adds the rest.
            ("Sakura", new Vector2(36f, 6f), 8f, 8f, 2.0f, 0.20f), ("Bridge", new Vector2(3f, -33f), 9f, 8f, 2.0f, 0.14f),
            ("Kairo", new Vector2(-35.2f, -1f), 3.2f, 7f, 1.2f, 0.22f), ("Kairo", new Vector2(-35.2f, 1f), 3.2f, 6f, 1.2f, 0.22f),
            ("Pavilion", new Vector2(33f, -33f), 6f, 6f, 1.2f, 0.14f), ("Bamboo", new Vector2(-40f, -14f), 11f, 11f, 1.8f, 0.22f),
            ("Bamboo", new Vector2(-40f, 14f), 11f, 12f, 2.0f, 0.22f), ("Sakura", new Vector2(42f, 6f), 9f, 10f, 2.5f, 0.20f),
            ("Sakura", new Vector2(30f, -4f), 8f, 9f, 2.0f, 0.20f), ("Veranda", new Vector2(0f, 37f), 7f, 7f, 1.5f, 0.18f),
        };

        /// <summary>Fog sheets after the ponds (one per pond first): region, centre XZ, size XZ, height, peak alpha, noise scale, drift.</summary>
        public static readonly (string region, Vector2 at, Vector2 size, float y, float alpha, float noise, float speed)[] FogSheets =
        {
            ("Bamboo", new Vector2(-45f, -12f), new Vector2(16f, 26f), 0.6f, 0.12f, 18f, 0.35f),   // DEC-101: 0.08 → 0.12 (the Ek A cap)
            ("Bamboo", new Vector2(-45f, 14f), new Vector2(16f, 26f), 0.6f, 0.12f, 18f, 0.35f),
            ("Rear", new Vector2(0f, 52.5f), new Vector2(40f, 6f), 0.75f, 0.06f, 24f, 0.3f),
            ("Sakura", new Vector2(36f, 1f), new Vector2(18f, 14f), 0.5f, 0.04f, 20f, 0.4f),
            ("Causeway", new Vector2(-8f, -27f), new Vector2(10f, 14f), 0.4f, 0.10f, 20f, 0.4f),
            ("NorthEast", new Vector2(44f, 40f), new Vector2(16f, 12f), 0.5f, 0.04f, 20f, 0.35f),
            ("SouthWest", new Vector2(-38f, -44f), new Vector2(14f, 12f), 0.5f, 0.05f, 20f, 0.35f),
        };

        public const float PondFogAlpha = 0.12f, PondFogY = 0.4f, PondFogNoise = 20f, PondFogSpeed = 0.4f;

        /// <summary>DEC-102 volumetric field (VolumetricLight on the dress root): where the shafts should read — the bamboo grove, the
        /// ponds, the sakura court, the veranda, the karamon forecourt, the rear kairō and the NE pines; the plaza centre stays clear
        /// (Ek A K10: ≤ 3 % local fog around the top-down player). r2 (C-00 25.09, HF v002: shafts read in every camera): denser, and
        /// the court edges under the cloister eaves (x ±20) join as their own regions.</summary>
        public static readonly (Vector3 centre, float radiusXZ, float radiusY, float density)[] VolumeRegions =
        {
            (new Vector3(-45f, 3f, -14f), 14f, 6f, 0.030f), (new Vector3(-45f, 3f, 14f), 14f, 6f, 0.030f),
            (new Vector3(-2f, 1.5f, -28f), 16f, 3f, 0.026f), (new Vector3(36f, 3f, 0f), 12f, 5f, 0.022f),
            (new Vector3(0f, 4f, 34f), 16f, 5f, 0.018f), (new Vector3(0f, 3f, 20f), 8f, 4f, 0.016f),
            (new Vector3(0f, 3f, 52f), 22f, 4f, 0.006f), (new Vector3(42f, 4f, 40f), 12f, 6f, 0.018f),
            (new Vector3(-20f, 3f, 29f), 7f, 4f, 0.020f), (new Vector3(20f, 3f, 29f), 7f, 4f, 0.020f),
        };

        public static int Build(Transform dress, Transform greybox)
        {
            var field = dress.gameObject.AddComponent<VolumetricLight>();
            // r2: a taller haze (halves every ~15 m, not ~4 m) so the crepuscular columns from the cloud gaps reach ~40 m and read from
            // the top-down camera; the ambient share low so the columns stand out of the shade (0.2 read milky-flat between them).
            field.BaseDensity = 0.0010f;   // DEC-102 AD MUST-B1/C1 (25.09): the eye-level veil toward the sun washed the roofs and the treeline (0.0035)
            field.HeightFalloff = 0.045f;
            field.ScatterScale = 0.30f;   // DEC-102 first frames read milky at 1.0; r2 0.7 washed the backlit HF views (Δ mean +0.13, contrast 0.21 → 0.15); AD 25.09 0.5 → 0.30 (the warm in-scatter outshone the dark crowns at 60–90 m)
            field.AmbientScatter = 0.06f;
            // r2: the lifted band (18 ± 9 m) — the crepuscular columns from the cloud gaps read from the top-down camera (C-00 25.09).
            field.BandHeight = 18f;
            field.BandHalfThickness = 9f;
            field.BandDensity = 0.0015f;   // 0.004 milked the backlit TP / ¾ views (the sky rays crossed 70 m of it toward the sun)
            field.Regions = new VolumetricLight.Region[VolumeRegions.Length];
            for (int i = 0; i < VolumeRegions.Length; i++)
                field.Regions[i] = new VolumetricLight.Region { Centre = VolumeRegions[i].centre, RadiusXZ = VolumeRegions[i].radiusXZ, RadiusY = VolumeRegions[i].radiusY, Density = VolumeRegions[i].density };

            EnsureFolder(MeshFolder);
            int n = 0;
            var shafts = Group(dress, ShaftGroupName);
            // DEC-102: the raymarched VolumetricLight carries the shafts on High / Cinematic; the cards stay as Performance's stand-in.
            shafts.gameObject.AddComponent<AtmosphereTierGroup>().Configure(6, 0, 0, 0.7f, 1f, 1f);
            Mesh shaftMesh = Quad("SM_ShaftCard", new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(0.5f, 0f, 0f) },
                new Bounds(Vector3.zero, Vector3.one * 2f));
            for (int i = 0; i < Shafts.Length; i++)
            {
                var s = Shafts[i];
                if (s.at.magnitude < CentreClear) throw new InvalidOperationException("shaft " + i + " inside the centre's 10 m (Ek A §3 ban)");
                // DEC-101: under the overcast the bamboo shafts keep a faint scattering glow (MinLit 0.35); the rest light in the gaps only.
                Material m = MaterialFor("V5_Shaft_" + s.region, "DD/ShaftCard", mat => { mat.SetFloat("_PeakAlpha", s.alpha); mat.SetFloat("_MinLit", s.region == "Bamboo" ? GroveScatter : 0f); });
                // Scale: x = width, y = length (the shader reads both); z = length too, so the culling bounds hold the slanted shaft.
                Piece(shafts, "Shaft_" + i.ToString("00") + "_" + s.region, new Vector3(s.at.x, s.top, s.at.y), new Vector3(s.width, s.length, s.length), shaftMesh, m);
                n++;
            }

            var fog = Group(dress, FogGroupName);
            fog.gameObject.AddComponent<AtmosphereTierGroup>().Configure(4, 7, 10, 0.6f, 0.8f, 1f);
            Mesh fogMesh = Quad("SM_FogSheet", new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) },
                new Bounds(Vector3.zero, new Vector3(1f, 0.2f, 1f)));
            var sheets = new List<(string region, Vector2 at, Vector2 size, float y, float alpha, float noise, float speed)>();
            foreach (Transform t in greybox)
                if (t.name.StartsWith("Pond", StringComparison.Ordinal) && t.name.EndsWith("_Water", StringComparison.Ordinal))
                    sheets.Add(("Pond", new Vector2(t.position.x, t.position.z), new Vector2(t.localScale.x + 4f, t.localScale.z + 4f), PondFogY, PondFogAlpha, PondFogNoise, PondFogSpeed));
            sheets.Sort((a, b) => a.at.x.CompareTo(b.at.x));
            sheets.AddRange(FogSheets);
            for (int i = 0; i < sheets.Count; i++)
            {
                var f = sheets[i];
                Material m = MaterialFor("V5_Fog_" + f.region, "DD/FogSheet", mat =>
                {
                    mat.SetFloat("_PeakAlpha", f.alpha);
                    mat.SetFloat("_NoiseScale", f.noise);
                    mat.SetFloat("_NoiseSpeed", f.speed);
                });
                Piece(fog, "Fog_" + i.ToString("00") + "_" + f.region, new Vector3(f.at.x, f.y, f.at.y), new Vector3(f.size.x, 1f, f.size.y), fogMesh, m);
                n++;
            }

            var mist = Group(dress, VistaMistGroupName);
            mist.gameObject.AddComponent<AtmosphereTierGroup>().Configure(4, 8, 8, 0.8f, 1f, 1f);
            for (int i = 0; i < VistaMist.Length; i++)
            {
                var v = VistaMist[i];
                Material m = MaterialFor("V5_Fog_VistaMist_" + (v.y < 5f ? "Low" : "High"), "DD/FogSheet", mat =>
                {
                    mat.SetFloat("_PeakAlpha", v.alpha);   // over the property's 0.12 play-area cap on purpose (outside the ring)
                    mat.SetFloat("_EdgeSoftness", 8f);
                    mat.SetFloat("_NoiseScale", 32f);
                    mat.SetFloat("_NoiseSpeed", 0.25f);
                });
                Piece(mist, "Mist_" + i.ToString("00"), new Vector3(v.at.x, v.y, v.at.y), new Vector3(v.size.x, 1f, v.size.y), fogMesh, m);
                n++;
            }

            var grove = Group(dress, GroveMistGroupName);
            grove.gameObject.AddComponent<AtmosphereTierGroup>().Configure(0, 2, 2, 0f, 1f, 1f);
            for (int i = 0; i < GroveMist.Length; i++)
            {
                var v = GroveMist[i];
                Material m = MaterialFor("V5_Fog_GroveMist", "DD/FogSheet", mat =>
                {
                    mat.SetFloat("_PeakAlpha", v.alpha);
                    mat.SetFloat("_EdgeSoftness", 2f);
                    mat.SetFloat("_NoiseScale", 12f);
                    mat.SetFloat("_NoiseSpeed", 0.3f);
                });
                Piece(grove, "GroveMist_" + i.ToString("00"), new Vector3(v.at.x, v.y, v.at.y), new Vector3(v.size.x, 1f, v.size.y), fogMesh, m);
                n++;
            }

            return n;
        }

        private static Transform Group(Transform dress, string name)
        {
            var g = new GameObject(name) { layer = PrototypeLayers.Environment }.transform;
            g.SetParent(dress, false);
            g.gameObject.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild = true;
            return g;
        }

        private static void Piece(Transform parent, string name, Vector3 position, Vector3 scale, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { layer = PrototypeLayers.Environment };
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private static Material MaterialFor(string name, string shaderName, Action<Material> configure)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException(shaderName + " missing");
            string path = GreyboxV6Builder.DataFolder + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = shader;
            configure(m);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Mesh Quad(string name, Vector3[] corners, Bounds bounds)
        {
            string path = MeshFolder + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.vertices = corners;
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.bounds = bounds;
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
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
