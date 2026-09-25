using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// Builds every effect template in code at runtime.
    ///
    /// WHY THERE ARE NO PREFABS HERE
    /// -----------------------------
    /// Four tasks are writing into this Unity project in parallel in this wave and the
    /// editor must not be opened. A hand-written .prefab YAML file is the single most
    /// fragile artefact you can produce blind - one wrong fileID and the run scene
    /// comes up with a missing reference, which is an explicit acceptance-criteria
    /// failure. Templates built from primitives in code cannot have a missing
    /// reference, cost one construction per template per session, and are still
    /// ordinary GameObjects that <see cref="Core.Pooling.PoolService"/> pools normally.
    ///
    /// Every template is created inactive under a DontDestroyOnLoad root, so the pool's
    /// Instantiate never fires an Awake in the wrong order.
    /// </summary>
    public static class FeedbackEffectFactory
    {
        private const string RootName = "[Feedback Templates]";

        private static Transform s_root;

        private static BurstEffect s_impactTemplate;
        private static BurstEffect s_bloodTemplate;
        private static BurstEffect s_armorHitTemplate;
        private static BurstEffect s_armorBreakTemplate;
        private static BurstEffect s_deathTemplate;
        private static BurstEffect s_explosionTemplate;
        private static TracerEffect s_tracerTemplate;
        private static MuzzleFlashEffect s_muzzleTemplate;
        private static LinkEffect s_linkTemplate;
        private static BeaconEffect s_beaconTemplate;


        /// <summary>WP-019: the inactive template root, for the AP-010 pool templates.</summary>
        public static Transform TemplateRoot => Root;

        private static Transform Root
        {
            get
            {
                if (s_root == null)
                {
                    var go = new GameObject(RootName);
                    go.SetActive(false);
                    Object.DontDestroyOnLoad(go);
                    go.hideFlags = HideFlags.NotEditable;
                    s_root = go.transform;
                }

                return s_root;
            }
        }

        // ------------------------------------------------------------------ templates

        /// <summary>Sparks and dust off a surface. Step 5.</summary>
        public static BurstEffect ImpactTemplate => s_impactTemplate != null
            ? s_impactTemplate
            : s_impactTemplate = BuildBurst("FX_Impact", 0.35f, 26f, 0.055f, 4.2f, 0.35f, 24);

        /// <summary>Flesh feedback. Step 9. Heavier gravity so it arcs and reads as wet.</summary>
        public static BurstEffect BloodTemplate => s_bloodTemplate != null
            ? s_bloodTemplate
            : s_bloodTemplate = BuildBurst("FX_Blood", 1.35f, 34f, 0.105f, 3.1f, 0.45f, 32);

        /// <summary>Armour was hit but held. Step 7. Sharp, short, metallic scatter.</summary>
        public static BurstEffect ArmorHitTemplate => s_armorHitTemplate != null
            ? s_armorHitTemplate
            : s_armorHitTemplate = BuildBurst("FX_ArmorHit", 0.9f, 20f, 0.05f, 5.6f, 0.28f, 24);

        /// <summary>Plate came off. Step 8. Big, slow shards.</summary>
        public static BurstEffect ArmorBreakTemplate => s_armorBreakTemplate != null
            ? s_armorBreakTemplate
            : s_armorBreakTemplate = BuildBurst("FX_ArmorBreak", 2.1f, 55f, 0.13f, 4.6f, 0.7f, 40);

        /// <summary>Death reaction. Step 10.</summary>
        public static BurstEffect DeathTemplate => s_deathTemplate != null
            ? s_deathTemplate
            : s_deathTemplate = BuildBurst("FX_Death", 0.8f, 60f, 0.16f, 2.6f, 0.75f, 48);

        /// <summary>Explosion upgrade branch.</summary>
        public static BurstEffect ExplosionTemplate => s_explosionTemplate != null
            ? s_explosionTemplate
            : s_explosionTemplate = BuildBurst("FX_Explosion", 0.15f, 90f, 0.45f, 6.5f, 0.55f, 64);

        public static TracerEffect TracerTemplate => s_tracerTemplate != null
            ? s_tracerTemplate
            : s_tracerTemplate = BuildTracer();

        public static MuzzleFlashEffect MuzzleTemplate => s_muzzleTemplate != null
            ? s_muzzleTemplate
            : s_muzzleTemplate = BuildMuzzleFlash();

        public static LinkEffect LinkTemplate => s_linkTemplate != null
            ? s_linkTemplate
            : s_linkTemplate = BuildLink();

        public static BeaconEffect BeaconTemplate => s_beaconTemplate != null
            ? s_beaconTemplate
            : s_beaconTemplate = BuildBeacon();

        // ------------------------------------------------------------------- builders

        private static GameObject NewChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.SetActive(false);
            return go;
        }

        private static BurstEffect BuildBurst(
            string name, float gravity, float coneAngle, float size, float speed, float life, int maxParticles)
        {
            GameObject go = NewChild(name);

            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = Color.white;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            // Explicit short: ParticleSystem.Burst has both a (float, short) and a
            // (float, MinMaxCurve) constructor and an untyped integer literal is
            // convertible to both.
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)8) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.03f;

            // Fade out, so nothing pops off screen. This is what stops a crowd of
            // twenty impacts from looking like flickering confetti.
            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = FeedbackMaterials.ParticleAlpha();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 1;

            return go.AddComponent<BurstEffect>();
        }

        private static TracerEffect BuildTracer()
        {
            GameObject go = NewChild("FX_Tracer");

            var line = go.AddComponent<LineRenderer>();
            ConfigureLine(line, 0.035f);

            return go.AddComponent<TracerEffect>();
        }

        private static LinkEffect BuildLink()
        {
            GameObject go = NewChild("FX_Link");

            var line = go.AddComponent<LineRenderer>();
            ConfigureLine(line, 0.02f);

            return go.AddComponent<LinkEffect>();
        }

        private static void ConfigureLine(LineRenderer line, float width)
        {
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = FeedbackMaterials.Additive(Color.white);
        }

        private static MuzzleFlashEffect BuildMuzzleFlash()
        {
            GameObject go = NewChild("FX_MuzzleFlash");

            GameObject quad = BuildPrimitive(PrimitiveType.Quad, "Flash");
            quad.transform.SetParent(go.transform, false);
            var quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = FeedbackMaterials.Additive(Color.white);
            quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;

            return go.AddComponent<MuzzleFlashEffect>();
        }

        private static BeaconEffect BuildBeacon()
        {
            GameObject go = NewChild("FX_Beacon");

            GameObject column = BuildPrimitive(PrimitiveType.Cylinder, "Column");
            column.transform.SetParent(go.transform, false);
            var columnRenderer = column.GetComponent<MeshRenderer>();
            columnRenderer.sharedMaterial = FeedbackMaterials.Additive(Color.white);
            columnRenderer.shadowCastingMode = ShadowCastingMode.Off;
            columnRenderer.receiveShadows = false;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 12f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
            light.enabled = false;

            return go.AddComponent<BeaconEffect>();
        }

        // ---------------------------------------------------------- Special Weapon templates

        /// <summary>
        /// A closed line loop in LOCAL space. Every other line here is world-space, which
        /// is correct for a tracer between two fixed world points and wrong for a ring
        /// that has to move with its owner - so this configures its own rather than
        /// reusing <see cref="ConfigureLine"/> and then fighting it.
        /// </summary>
        private static LineRenderer BuildRingLine(Transform parent, string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 0;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = FeedbackMaterials.Additive(Color.white);

            return line;
        }

        /// <summary>Primitive without its collider. Effects must never touch physics.</summary>
        private static GameObject BuildPrimitive(PrimitiveType type, string name)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            return go;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_root = null;
            s_impactTemplate = null;
            s_bloodTemplate = null;
            s_armorHitTemplate = null;
            s_armorBreakTemplate = null;
            s_deathTemplate = null;
            s_explosionTemplate = null;
            s_tracerTemplate = null;
            s_muzzleTemplate = null;
            s_linkTemplate = null;
            s_beaconTemplate = null;
        }
    }
}
