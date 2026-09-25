using System;
using System.Collections.Generic;
using DD.Prototype.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 C3 · Sky v2 for the World v5 dev scene (P-05 AP-023 sky-v2-wp075 manifest v001; WP-075 direction Ek A §2). The layout's
    /// skybox is DD_Sky_v2 (band v2, <see cref="ArtSkyBuilder.BuildV2"/>; WorldAtmosphere picks it for the v6 layout). The AP-019 horizon
    /// rings keep their contract (260 / 330 / 420 m) with the v2 sheets. Two cloud planes (near cumulus y 500 m, 500 m tile, 3.5 m/s;
    /// far deck y 900 m, 333 m tile, 5 m/s) drift toward 52° with the cloud cookie. There are landmark billboards (pagoda NE 35° r 330
    /// 52 m, temple SSE 120° r 330 26 m, torii WSW 250° r 200 14 m) and the NW cedar cluster (az 305–335, r 255, 1.3× the near
    /// treeline). All are DD/SkyPlate (unlit, fog-excluded, depth pinned inside the far plane) with explicit queues, because
    /// every plate's bounds centre is the map origin and distance sorting cannot order them: clouds → far → mid → landmarks → near → NW cards.
    /// World_AP005 keeps sky v1 until World v5 replaces it.
    /// </summary>
    public static class WorldV5Sky
    {
        public const string RootName = "Sky v2 (C3)";
        public const float DriftCompassDegrees = 52f;
        public static string MeshFolder => GreyboxV6Builder.DataFolder + "/Sky";

        public static readonly (string layer, int queue)[] PlateQueues = { ("far", 2896), ("mid", 2897), ("near", 2899) };

        public static readonly (string name, string role, float height, float half, float tile, float speed, int queue)[] Clouds =
        {
            ("Clouds_Far", "cloud_far", 900f, 6000f, 2000f / 6f, 5.0f, 2894),
            ("Clouds_Near", "cloud_near", 500f, 4000f, 500f, 3.5f, 2895),
        };

        public static readonly (string name, string role, float compass, float radius, float height, int queue, bool flip)[] Cards =
        {
            ("Landmark_Pagoda", "landmark_pagoda", 35f, 330f, 52f, 2898, false),
            ("Landmark_Temple", "landmark_temple", 120f, 330f, 26f, 2898, false),
            ("Landmark_Torii", "landmark_torii", 250f, 200f, 14f, 2898, false),
            ("NW_Cluster_0", "horizon_nw_cluster", 305f, 255f, 66f, 2900, false),
            ("NW_Cluster_1", "horizon_nw_cluster", 320f, 255f, 60f, 2900, true),
            ("NW_Cluster_2", "horizon_nw_cluster", 335f, 255f, 70f, 2900, false),
        };

        /// <summary>The staged sky v2 sources under the contract (stage-ap023-sky-v2.py) and the derived v2 lat-long.</summary>
        public static IEnumerable<string> TextureFiles()
        {
            yield return ArtSkyBuilder.SkyBandV2SourcePath;
            yield return ArtSkyBuilder.SkyV2TexturePath;
            foreach (string role in new[] { "horizon_far_v2", "horizon_mid_v2", "horizon_near_v2", "cloud_near", "cloud_far", "landmark_pagoda", "landmark_temple", "landmark_torii", "horizon_nw_cluster" })
                yield return ArtPipelinePaths.SourceTexture(role, "Sheet");
        }

        /// <summary>Compass bearing (clockwise from north, +z) → unit XZ direction.</summary>
        public static Vector2 Compass(float degrees) => new Vector2(Mathf.Sin(degrees * Mathf.Deg2Rad), Mathf.Cos(degrees * Mathf.Deg2Rad));

        /// <summary>Builds the sky v2 dressing under <paramref name="dress"/>; the AP-019 rings must already be there (BuildHorizonPlates). Returns the objects placed.</summary>
        public static int Build(Transform dress)
        {
            ArtSkyBuilder.BuildV2();
            EnsureFolder(MeshFolder);
            int n = 0;
            Transform rings = dress.Find(ArtSurfaceImporter.SkyRootName);
            if (rings == null) throw new InvalidOperationException("AP-019 horizon rings missing under " + dress.name);
            foreach ((string layer, int queue) in PlateQueues)
            {
                Transform plate = rings.Find("Horizon_" + layer);
                if (plate == null) throw new InvalidOperationException("Horizon_" + layer + " missing");
                plate.GetComponent<MeshRenderer>().sharedMaterial = PlateMaterial("horizon_" + layer + "_v2", queue, Vector2.zero);
                n++;
            }

            var root = new GameObject(RootName) { layer = PrototypeLayers.Environment }.transform;
            root.SetParent(dress, false);
            Vector2 drift = Compass(DriftCompassDegrees);
            foreach ((string name, string role, float height, float half, float tile, float speed, int queue) c in Clouds)
            {
                Mesh mesh = Quad("SM_sky_" + c.name, new[] { new Vector3(-c.half, 0f, -c.half), new Vector3(-c.half, 0f, c.half), new Vector3(c.half, 0f, c.half), new Vector3(c.half, 0f, -c.half) },
                    new[] { new Vector2(-c.half / c.tile, -c.half / c.tile), new Vector2(-c.half / c.tile, c.half / c.tile), new Vector2(c.half / c.tile, c.half / c.tile), new Vector2(c.half / c.tile, -c.half / c.tile) });
                Plate(root, c.name, new Vector3(0f, c.height, 0f), Quaternion.identity, mesh, PlateMaterial(c.role, c.queue, drift * (c.speed / c.tile)));
                n++;
            }

            foreach ((string name, string role, float compass, float radius, float height, int queue, bool flip) card in Cards)
            {
                Material m = PlateMaterial(card.role, card.queue, Vector2.zero);
                Texture t = m.GetTexture("_BaseMap");
                float width = card.height * t.width / Mathf.Max(1f, t.height);
                float u0 = card.flip ? 1f : 0f, u1 = card.flip ? 0f : 1f;
                Mesh mesh = Quad("SM_sky_" + card.name, new[] { new Vector3(-width * 0.5f, 0f, 0f), new Vector3(-width * 0.5f, card.height, 0f), new Vector3(width * 0.5f, card.height, 0f), new Vector3(width * 0.5f, 0f, 0f) },
                    new[] { new Vector2(u0, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f) });
                Vector2 d = Compass(card.compass);
                var position = new Vector3(d.x * card.radius, ArtSurfaceImporter.HorizonBaseY, d.y * card.radius);
                // Face the map centre (the card's +z toward the origin; its u runs left → right as seen from inside).
                Plate(root, card.name, position, Quaternion.LookRotation(new Vector3(d.x, 0f, d.y), Vector3.up), mesh, m);
                n++;
            }

            return n;
        }

        private static void Plate(Transform parent, string name, Vector3 position, Quaternion rotation, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { layer = PrototypeLayers.Environment };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
        }

        /// <summary>V5_Sky_&lt;role&gt;.mat on DD/SkyPlate with the role's Sheet, an explicit queue and a UV drift (rebuilt in place).</summary>
        public static Material PlateMaterial(string role, int queue, Vector2 scroll)
        {
            Shader shader = Shader.Find(ArtSurfaceImporter.SkyPlateShaderName);
            if (shader == null) throw new InvalidOperationException(ArtSurfaceImporter.SkyPlateShaderName + " missing");
            string path = GreyboxV6Builder.DataFolder + "/Materials/V5_Sky_" + role + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = shader;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPipelinePaths.SourceTexture(role, "Sheet"));
            if (texture == null) throw new InvalidOperationException("sky v2 sheet missing: " + ArtPipelinePaths.SourceTexture(role, "Sheet"));
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", Color.white);
            m.SetVector("_ScrollUV", new Vector4(scroll.x, scroll.y, 0f, 0f));
            m.renderQueue = queue;
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Mesh Quad(string name, Vector3[] corners, Vector2[] uvs)
        {
            string path = MeshFolder + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.vertices = corners;
            mesh.uv = uvs;
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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
