using DD.Prototype.Rendering;
using UnityEngine;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;

namespace DD.Prototype.Enemies
{
    /// <summary>What a burst represents. Drives colour, count, size and lifetime.</summary>
    public enum EnemyBurstKind
    {
        /// <summary>Flesh hit. The quietest tier of the REQ-125 hierarchy.</summary>
        Blood = 0,

        /// <summary>Bullet skating off an intact plate. Sparks, no blood.</summary>
        ArmorSpark = 1,

        /// <summary>A plate group broke. Metal shards thrown along the bullet.</summary>
        ArmorShards = 2,

        /// <summary>Death. Bigger, slower, settles on the ground.</summary>
        Death = 3
    }

    /// <summary>
    /// One pooled particle burst. Built entirely in code - no prefab asset, no editor step.
    ///
    /// Self-returning: it despawns itself when the system is done, so the caller never has
    /// to track it. Pools use RecycleOldest, which is the correct policy for FX: under a
    /// crowd it is better to steal the oldest puff than to skip the newest hit, because the
    /// newest hit is the one the player is looking at.
    /// </summary>
    public sealed class EnemyBurstEffect : PooledBehaviour
    {
        private ParticleSystem _system;
        private ParticleSystem.MainModule _main;
        private float _despawnAt;

        private void Awake()
        {
            _system = GetComponent<ParticleSystem>();
            if (_system == null)
            {
                return;
            }

            _main = _system.main;
        }

        /// <summary>Fire a burst. Direction shapes the cone; kind picks the look.</summary>
        public void Play(EnemyBurstKind kind, Vector3 direction, float scale)
        {
            if (_system == null)
            {
                Despawn();
                return;
            }

            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized);
            }

            int count;
            float lifetime;
            float speed;
            float size;
            Color colour;

            // PROTOTYPE-ONLY / INVENTED. Tuned so the four tiers are distinguishable at
            // the request's far, elevated camera without any of them fogging the crowd.
            switch (kind)
            {
                case EnemyBurstKind.ArmorSpark:
                    count = 8;
                    lifetime = 0.22f;
                    speed = 5.5f;
                    size = 0.045f;
                    colour = new Color(0.95f, 0.82f, 0.52f, 1f);
                    break;
                case EnemyBurstKind.ArmorShards:
                    count = 16;
                    lifetime = 0.75f;
                    speed = 4.2f;
                    size = 0.075f;
                    colour = new Color(0.42f, 0.45f, 0.41f, 1f);
                    break;
                case EnemyBurstKind.Death:
                    count = 22;
                    lifetime = 0.95f;
                    speed = 2.6f;
                    size = 0.09f;
                    colour = new Color(0.36f, 0.10f, 0.12f, 1f);
                    break;
                default:
                    count = 10;
                    lifetime = 0.45f;
                    speed = 3.4f;
                    size = 0.06f;
                    colour = new Color(0.48f, 0.09f, 0.10f, 1f);
                    break;
            }

            scale = Mathf.Clamp(scale, 0.5f, 2.5f);

            _main.startLifetime = lifetime;
            _main.startSpeed = speed * scale;
            _main.startSize = size * scale;
            _main.startColor = colour;

            // Play() first: particles pushed in with Emit only simulate while the system is
            // playing, and emission-over-time is disabled so Play alone spawns nothing.
            _system.Clear(true);
            _system.Play(true);
            _system.Emit(Mathf.RoundToInt(count * Mathf.Lerp(0.7f, 1.3f, Mathf.Clamp01(scale - 0.5f))));

            _despawnAt = Time.time + lifetime + 0.1f;
        }

        private void Update()
        {
            if (Time.time >= _despawnAt)
            {
                Despawn();
            }
        }

        protected override void OnDespawned()
        {
            if (_system != null)
            {
                _system.Clear(true);
            }
        }

        /// <summary>Build the pool template. One per kind is not needed - kind is a runtime parameter.</summary>
        internal static EnemyBurstEffect CreateTemplate()
        {
            var go = new GameObject("ZombieBurstEffect_Template");
            go.SetActive(false);

            var system = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.1f;
            main.maxParticles = 40;
            main.startRotation3D = false;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false; // burst-on-demand only

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.05f;

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1.6f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = ZombieEffectMaterial.Instance;

            return go.AddComponent<EnemyBurstEffect>();
        }
    }

    /// <summary>Single unlit additive-ish material shared by every burst, so they batch.</summary>
    internal static class ZombieEffectMaterial
    {
        private static Material s_instance;

        public static Material Instance
        {
            get
            {
                if (s_instance != null)
                {
                    return s_instance;
                }

                s_instance = new Material(PrototypeShaders.ParticlesUnlit)
                {
                    name = "DD_ZombieBurst",
                    hideFlags = HideFlags.DontSave
                };

                if (s_instance.HasProperty("_BaseColor"))
                {
                    s_instance.SetColor("_BaseColor", Color.white);
                }

                return s_instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }

    /// <summary>
    /// Effect budget and dispatch - REQ-128.
    ///
    /// A crowd of 40 zombies under automatic fire can request hundreds of bursts a second.
    /// Readability, not throughput, is the constraint the request names, so each category
    /// gets its own pool sized from FeedbackConfigSection. When a pool is full the OLDEST
    /// burst is recycled: the screen keeps a constant, legible density of feedback instead
    /// of either dropping the newest hit or drowning in puffs.
    ///
    /// Blood and impact share the readability budget deliberately; armour breaks get their
    /// own smaller, protected pool so a break is never starved out by ordinary fire. That
    /// is the REQ-125 hierarchy expressed as a resource decision rather than as a volume
    /// knob.
    /// </summary>
    public static class EnemyEffects
    {
        private static PrototypePool<EnemyBurstEffect> s_bloodPool;
        private static PrototypePool<EnemyBurstEffect> s_armorPool;
        private static PrototypePool<EnemyBurstEffect> s_breakPool;
        private static PrototypePool<EnemyBurstEffect> s_deathPool;

        /// <summary>Build or resize the pools from the live config. Safe to call repeatedly.</summary>
        public static void ApplyConfig(PrototypeConfig config)
        {
            if (config == null)
            {
                return;
            }

            FeedbackConfigSection feedback = config.Feedback;

            int blood = Mathf.Max(4, feedback.MaxBloodEffects);
            int impact = Mathf.Max(4, feedback.MaxImpactEffects);
            int breaks = Mathf.Max(2, feedback.MaxArmorBreakEffects);
            int deaths = Mathf.Max(2, feedback.MaxDeathEffects);

            // Separate pools of the SAME template would collide on the prefab instance id,
            // because PoolService keys by prefab. Four templates, one per category.
            s_bloodPool = EnsurePool(ref s_bloodTemplate, "Blood", blood, Mathf.Min(6, blood));
            s_armorPool = EnsurePool(ref s_armorTemplate, "ArmorSpark", impact, Mathf.Min(8, impact));
            s_breakPool = EnsurePool(ref s_breakTemplate, "ArmorShards", breaks, Mathf.Min(4, breaks));
            s_deathPool = EnsurePool(ref s_deathTemplate, "Death", deaths, Mathf.Min(4, deaths));
        }

        private static EnemyBurstEffect s_bloodTemplate;
        private static EnemyBurstEffect s_armorTemplate;
        private static EnemyBurstEffect s_breakTemplate;
        private static EnemyBurstEffect s_deathTemplate;

        private static PrototypePool<EnemyBurstEffect> EnsurePool(
            ref EnemyBurstEffect template, string suffix, int capacity, int prewarm)
        {
            if (template == null)
            {
                template = EnemyBurstEffect.CreateTemplate();
                template.gameObject.name = "ZombieBurst_" + suffix;
                Object.DontDestroyOnLoad(template.gameObject);
            }

            // RecycleOldest, per the task's rule for impact / blood / armour-shard effects.
            return PoolService.GetOrCreate(template, prewarm, capacity, PoolExhaustedPolicy.RecycleOldest);
        }

        /// <summary>Spawn a burst. Silently does nothing if the pools are not built yet.</summary>
        public static void Spawn(EnemyBurstKind kind, Vector3 position, Vector3 direction, float scale = 1f)
        {
            PrototypePool<EnemyBurstEffect> pool = PoolFor(kind);
            if (pool == null)
            {
                return;
            }

            EnemyBurstEffect effect = pool.Get(position, Quaternion.identity);
            if (effect == null)
            {
                return; // budget reached and nothing was recyclable this frame
            }

            effect.Play(kind, direction, scale);
        }

        private static PrototypePool<EnemyBurstEffect> PoolFor(EnemyBurstKind kind)
        {
            switch (kind)
            {
                case EnemyBurstKind.ArmorSpark: return s_armorPool;
                case EnemyBurstKind.ArmorShards: return s_breakPool;
                case EnemyBurstKind.Death: return s_deathPool;
                default: return s_bloodPool;
            }
        }

        /// <summary>Live burst counts, for the F1 readout.</summary>
        public static string DescribeBudget()
        {
            return string.Format(
                "blood {0}/{1}  spark {2}/{3}  shards {4}/{5}  death {6}/{7}",
                s_bloodPool != null ? s_bloodPool.CountSpawned : 0, s_bloodPool != null ? s_bloodPool.Capacity : 0,
                s_armorPool != null ? s_armorPool.CountSpawned : 0, s_armorPool != null ? s_armorPool.Capacity : 0,
                s_breakPool != null ? s_breakPool.CountSpawned : 0, s_breakPool != null ? s_breakPool.Capacity : 0,
                s_deathPool != null ? s_deathPool.CountSpawned : 0, s_deathPool != null ? s_deathPool.Capacity : 0);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_bloodTemplate = null;
            s_armorTemplate = null;
            s_breakTemplate = null;
            s_deathTemplate = null;
            s_bloodPool = null;
            s_armorPool = null;
            s_breakPool = null;
            s_deathPool = null;
        }
    }
}
