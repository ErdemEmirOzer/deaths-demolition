using System;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Props;
using DD.Prototype.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-013 · Authors the VFX assets, generated like every other pipeline asset (re-run
    /// restores the contract; GUIDs stable):
    ///
    ///   Assets/_Art/VFX/Resources/DD_Pollen.prefab        drift field, <see cref="AtmosphereDrift"/>
    ///   Assets/_Art/VFX/Resources/DD_SakuraPetals.prefab  drift field, <see cref="AtmosphereDrift"/>
    ///   Assets/_Art/VFX/Resources/DD_BreakBurst.prefab    one-shot chips + dust for <see cref="BreakableProp"/>
    ///   Assets/_Art/VFX/Materials/M_vfx_*.mat             URP Particles/Unlit, alpha blended
    ///   Assets/_Art/VFX/Textures/T_vfx_*.asset            procedural sprites as Texture2D assets
    ///                                                      (not PNG: the PNG gate is the AP texture contract)
    ///
    /// The prefabs live in a Resources folder because <see cref="WorldAtmosphere"/> (DD.Prototype)
    /// loads them by name — the same door the shared sky uses; DD.Prototype cannot reference
    /// DD.Art. Sizes, counts and lifetimes are the WP-002 numbers WorldAtmosphere shipped with
    /// (about 440 alive over the map, nothing over 18 cm); the budget test pins them.
    /// </summary>
    public static class ArtVfxBuilder
    {
        public const string VfxFolder = ArtPipelinePaths.ArtRoot + "/VFX";
        public const string ResourcesFolder = VfxFolder + "/Resources";
        public const string MaterialsFolder = VfxFolder + "/Materials";
        public const string TexturesFolder = VfxFolder + "/Textures";

        public const string PollenPrefab = ResourcesFolder + "/" + WorldAtmosphere.PollenResourceName + ".prefab";
        public const string PetalsPrefab = ResourcesFolder + "/" + WorldAtmosphere.PetalsResourceName + ".prefab";
        public const string BreakBurstPrefab = ResourcesFolder + "/" + BreakableProp.BurstResourceName + ".prefab";

        public const string ParticlesUnlitShaderName = "Universal Render Pipeline/Particles/Unlit";

        // ---- budgets (PROTOTYPE-ONLY; the README table and ArtVfxGateTests read these)
        public const int PollenMaxParticles = 240;
        public const float PollenEmission = 20f;
        public const int PetalsMaxParticles = 130;
        public const float PetalsEmission = 15f;
        public const float PetalSizeMax = 0.18f;
        public const int BurstChips = 18;
        public const int BurstDust = 10;

        private const string LogPrefix = "[ArtVfx] ";

        public static void BuildAll()
        {
            EnsureFolder(ResourcesFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(TexturesFolder);

            Texture2D pollenTex = BuildTexture("T_vfx_pollen", 32, PollenPixel);
            Texture2D petalTex = BuildTexture("T_vfx_petal", 64, PetalPixel);
            Texture2D chipTex = BuildTexture("T_vfx_chip", 16, ChipPixel);
            Texture2D dustTex = BuildTexture("T_vfx_dust", 64, DustPixel);

            Material pollenMat = BuildMaterial("M_vfx_pollen", pollenTex, new Color(0.949f, 0.925f, 0.804f, 0.55f));
            Material petalMat = BuildMaterial("M_vfx_petal", petalTex, new Color(0.925f, 0.702f, 0.780f, 0.85f));
            Material chipMat = BuildMaterial("M_vfx_chip", chipTex, new Color(0.42f, 0.33f, 0.24f, 1f));
            Material dustMat = BuildMaterial("M_vfx_dust", dustTex, new Color(0.62f, 0.58f, 0.50f, 0.45f));

            BuildDrift(PollenPrefab, WorldAtmosphere.PollenResourceName, pollenMat,
                PollenMaxParticles, PollenEmission, 12f, 0.03f, 0.06f, 0.004f, new Vector3(86f, 5f, 86f), 3.2f);
            BuildDrift(PetalsPrefab, WorldAtmosphere.PetalsResourceName, petalMat,
                PetalsMaxParticles, PetalsEmission, 8.5f, 0.08f, PetalSizeMax, 0.06f, new Vector3(26f, 1f, 52f), 10f);
            BuildBurst(chipMat, dustMat);

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "VFX assets built: " + PollenPrefab + ", " + PetalsPrefab + ", " + BreakBurstPrefab);
        }

        // ------------------------------------------------------------------ drift prefabs

        private static void BuildDrift(string path, string name, Material material, int maxParticles, float emission,
            float lifetime, float sizeMin, float sizeMax, float gravity, Vector3 box, float centreHeight)
        {
            GameObject go = LoadOrCreatePrefabRoot(path, name);
            go.transform.position = Vector3.zero;

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                ps = go.AddComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.duration = 12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = Color.white;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.ringBufferMode = ParticleSystemRingBufferMode.Disabled;

            ParticleSystem.EmissionModule emissionModule = ps.emission;
            emissionModule.enabled = true;
            emissionModule.rateOverTime = emission;
            emissionModule.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            shape.position = Vector3.zero;

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // All three in the same MinMaxCurve mode (WorldAtmosphere.MakeDrift comment).
            velocity.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.07f, 0.07f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            // The wind: WindZone force through External Forces (the WindDriver sets windMain).
            ParticleSystem.ExternalForcesModule external = ps.externalForces;
            external.enabled = true;
            external.multiplier = 1f;
            external.influenceFilter = ParticleSystemGameObjectFilter.LayerMask;
            external.influenceMask = ~0;

            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.16f),
                    new GradientAlphaKey(1f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            // Mesh quad + GPU instancing: one instanced draw per field instead of a CPU-built
            // billboard strip. View alignment keeps the quad facing the camera.
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = QuadMesh();
            renderer.enableGPUInstancing = true;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 0.02f;
            renderer.sortMode = ParticleSystemSortMode.None;

            var drift = go.GetComponent<AtmosphereDrift>();
            if (drift == null)
            {
                drift = go.AddComponent<AtmosphereDrift>();
            }

            var so = new SerializedObject(drift);
            so.FindProperty("_maxParticles").intValue = maxParticles;
            so.FindProperty("_emissionAtDensityOne").floatValue = emission;
            so.FindProperty("_region").enumValueIndex = 0;
            so.FindProperty("_density").floatValue = 1f;
            so.FindProperty("_box").vector3Value = box;
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(go, path);
        }

        // ------------------------------------------------------------------ break burst

        private static void BuildBurst(Material chipMat, Material dustMat)
        {
            GameObject go = LoadOrCreatePrefabRoot(BreakBurstPrefab, BreakableProp.BurstResourceName);

            // Chips on the root system, dust on a child: two materials, one prefab.
            // Explicit null test: '??' bypasses Unity's fake-null and hands a destroyed object on.
            var chips = go.GetComponent<ParticleSystem>();
            if (chips == null)
            {
                chips = go.AddComponent<ParticleSystem>();
            }

            ConfigureBurst(chips, chipMat, BurstChips, 0.03f, 0.07f, 1.2f, 2.6f, 0.9f, 1.4f, new Color(0.42f, 0.33f, 0.24f, 1f));

            Transform dustT = go.transform.Find("Dust");
            GameObject dustGo = dustT != null ? dustT.gameObject : new GameObject("Dust");
            dustGo.transform.SetParent(go.transform, false);
            var dust = dustGo.GetComponent<ParticleSystem>();
            if (dust == null)
            {
                dust = dustGo.AddComponent<ParticleSystem>();
            }

            ConfigureBurst(dust, dustMat, BurstDust, 0.25f, 0.5f, 0.3f, 0.9f, 0.7f, 1.1f, new Color(0.62f, 0.58f, 0.50f, 0.45f));
            ParticleSystem.MainModule dustMain = dust.main;
            dustMain.gravityModifier = -0.02f;

            SavePrefab(go, BreakBurstPrefab);
        }

        private static void ConfigureBurst(ParticleSystem ps, Material material, int count, float sizeMin, float sizeMax,
            float speedMin, float speedMax, float lifeMin, float lifeMax, Color color)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.prewarm = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.9f;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = Color.white;
            main.stopAction = ParticleSystemStopAction.Destroy;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.08f;

            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 0.05f;
        }

        // ------------------------------------------------------------------ post-processing data

        public const string RendererAssetPath = "Assets/Settings/DD_URP_Renderer.asset";
        public const string PostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        /// <summary>
        /// WP-013 · The URP renderer shipped with <c>postProcessData: none</c>, which switches
        /// URP post-processing off for every camera whatever the camera flag says — the real
        /// reason the authored grade never showed (WP-011 "post-fx effectively off"). Assigns
        /// the package's default PostProcessData (the uber/bloom/LUT shaders). Idempotent.
        /// </summary>
        public static bool EnablePostProcessData()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(RendererAssetPath);
            if (renderer == null)
            {
                throw new InvalidOperationException(LogPrefix + "Renderer asset not found: " + RendererAssetPath);
            }

            var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.PostProcessData>(PostProcessDataPath);
            if (data == null)
            {
                throw new InvalidOperationException(LogPrefix + "URP PostProcessData not found: " + PostProcessDataPath);
            }

            var so = new SerializedObject(renderer);
            SerializedProperty property = so.FindProperty("postProcessData");
            if (property == null)
            {
                throw new InvalidOperationException(LogPrefix + "Renderer has no postProcessData field.");
            }

            bool changed = property.objectReferenceValue != data;
            if (changed)
            {
                property.objectReferenceValue = data;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderer);
                AssetDatabase.SaveAssets();
            }

            Debug.Log(LogPrefix + "Renderer post-process data " + (changed ? "assigned" : "already assigned") + ": " + PostProcessDataPath);
            return changed;
        }

        // ------------------------------------------------------------------ breakables

        /// <summary>
        /// Puts <see cref="BreakableProp"/> on every library prefab that has a Broken child
        /// (the AP-003B small_breakable variations). Idempotent; the prefab builder does the
        /// same for future rebuilds, this is for the prefabs already on disk.
        /// </summary>
        public static int BindBreakables()
        {
            int bound = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ArtPipelinePaths.ArtRoot + "/Environment" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.transform.Find(BreakableProp.BrokenName) == null)
                {
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (Bind(root))
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        bound++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Debug.Log(LogPrefix + "Breakables bound: " + bound + " prefab(s) changed.");
            return bound;
        }

        /// <summary>Adds/refreshes the component on a prefab root with a Broken child. Returns true when something changed.</summary>
        public static bool Bind(GameObject root)
        {
            return Bind(root, null, null);
        }

        /// <summary>WP-038B · As <see cref="Bind(GameObject)"/>, also writing the AP-017 break event id / residue cell (null = keep the prop's current values, i.e. the AP-003B defaults).</summary>
        public static bool Bind(GameObject root, string breakEventId, string residueCell)
        {
            Transform broken = root.transform.Find(BreakableProp.BrokenName);
            Transform geometry = root.transform.Find(BreakableProp.GeometryName);
            if (broken == null || geometry == null)
            {
                return false;
            }

            var prop = root.GetComponent<BreakableProp>();
            bool changed = prop == null;
            if (prop == null)
            {
                prop = root.AddComponent<BreakableProp>();
            }

            var so = new SerializedObject(prop);
            SerializedProperty g = so.FindProperty("_geometry");
            SerializedProperty b = so.FindProperty("_broken");
            changed |= g.objectReferenceValue != geometry || b.objectReferenceValue != broken;
            g.objectReferenceValue = geometry;
            b.objectReferenceValue = broken;
            if (!string.IsNullOrEmpty(breakEventId))
            {
                SerializedProperty e = so.FindProperty("_eventId");
                changed |= e.stringValue != breakEventId;
                e.stringValue = breakEventId;
            }

            if (!string.IsNullOrEmpty(residueCell))
            {
                SerializedProperty r = so.FindProperty("_residueCell");
                changed |= r.stringValue != residueCell;
                r.stringValue = residueCell;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            // Intact by default; the Broken child is inactive in the generated prefab already.
            if (broken.gameObject.activeSelf)
            {
                broken.gameObject.SetActive(false);
                changed = true;
            }

            return changed;
        }

        // ------------------------------------------------------------------ materials / textures

        private static Material BuildMaterial(string name, Texture2D texture, Color color)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            Shader shader = Shader.Find(ParticlesUnlitShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(LogPrefix + "Shader '" + ParticlesUnlitShaderName + "' not found.");
            }

            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = m == null;
            if (created)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", color);
            OccluderFadeState.MakeTransparent(m);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Cull", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Texture2D BuildTexture(string name, int size, Func<float, float, Color> pixel)
        {
            string path = TexturesFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    pixels[y * size + x] = pixel(u, v);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true, false);

            if (existing == null)
            {
                AssetDatabase.CreateAsset(tex, path);
                return tex;
            }

            EditorUtility.CopySerialized(tex, existing);
            UnityEngine.Object.DestroyImmediate(tex);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Color PollenPixel(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01(1f - r);
            a = a * a * (3f - 2f * a);
            return new Color(1f, 1f, 1f, a);
        }

        private static Color PetalPixel(float u, float v)
        {
            // A sakura petal: an ellipse with a notch at the tip (+v).
            float ex = u / 0.62f;
            float ey = v / 0.95f;
            float inside = 1f - (ex * ex + ey * ey);
            float notch = v > 0.35f ? Mathf.Max(0f, 0.22f - Mathf.Abs(u)) * (v - 0.35f) * 3f : 0f;
            float a = Mathf.Clamp01((inside - notch) * 5f);
            float shade = 0.85f + 0.15f * Mathf.Clamp01(-v);
            return new Color(shade, shade, shade, a);
        }

        private static Color ChipPixel(float u, float v)
        {
            float a = Mathf.Abs(u) < 0.8f && Mathf.Abs(v) < 0.55f ? 1f : 0f;
            float shade = 0.75f + 0.25f * Mathf.Clamp01(v);
            return new Color(shade, shade, shade, a);
        }

        private static Color DustPixel(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01(1f - r) * 0.8f;
            a = a * a;
            return new Color(1f, 1f, 1f, a);
        }

        // ------------------------------------------------------------------ helpers

        private static Mesh QuadMesh()
        {
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (quad == null)
            {
                throw new InvalidOperationException(LogPrefix + "Built-in Quad mesh not found.");
            }

            return quad;
        }

        private static GameObject LoadOrCreatePrefabRoot(string path, string name)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject go = existing != null ? PrefabUtility.InstantiatePrefab(existing) as GameObject : null;
            if (go != null)
            {
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            else
            {
                go = new GameObject(name);
            }

            go.name = name;
            return go;
        }

        private static void SavePrefab(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
            UnityEngine.Object.DestroyImmediate(go);
            if (!ok)
            {
                throw new InvalidOperationException(LogPrefix + "Could not save prefab " + path);
            }

            Debug.Log(LogPrefix + "Prefab written: " + path);
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
