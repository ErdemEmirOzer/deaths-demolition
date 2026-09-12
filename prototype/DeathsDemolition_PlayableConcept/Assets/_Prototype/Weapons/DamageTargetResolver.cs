using System.Collections.Generic;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Weapons
{
    /// <summary>One enemy, from the weapon's point of view. Everything is already resolved.</summary>
    public struct DamageTarget
    {
        public IDamageReceiver Receiver;
        public IExplosionReceiver Explosive;
        public Transform Root;

        /// <summary>Stable id used to de-duplicate pierce / chain / explosion targets.</summary>
        public int Id;

        /// <summary>Cached standing height, for geometric region resolution.</summary>
        public float Height;

        /// <summary>How much death spectacle this target earns.</summary>
        public FeedbackWeight Weight;

        public bool IsValid => Receiver != null && Root != null;
    }

    /// <summary>
    /// Collider to enemy, answered from a dictionary.
    ///
    /// This is the single most performance-load-bearing class on the weapon side. A
    /// combat shotgun fires eight pellets, each of which can pierce and chain; the
    /// rotary cannon fires fifteen times a second; an explosion sweeps up to sixty
    /// colliders. Doing <c>GetComponentInParent</c> on every one of those is precisely
    /// the "expensive per-frame lookup in a crowd" the request forbids.
    ///
    /// So each collider is walked exactly once in its lifetime and everything about it
    /// is remembered: the receiver, the explosion receiver, the root transform, the
    /// standing height and the feedback weight. Pooled enemies keep their colliders, so
    /// a cache entry stays correct for the whole session.
    ///
    /// REGION RESOLUTION is deliberately decoupled from the enemy implementation, and
    /// since WP-006 it is EXPLICIT about which path answered:
    ///   1. <see cref="HitboxRegistry"/> - the collider the enemy registered for a region
    ///      at Awake (instance-id dictionary; name-independent). This is the authority:
    ///      the gameplay region model, not the visual mesh or bone layout, decides.
    ///   2. Otherwise, if the collider is named with head / body / leg in it, that wins.
    ///      This costs one lowercase compare per collider, ever.
    ///   3. Otherwise the region comes from the hit height as a fraction of the cached
    ///      standing height.
    /// Paths 2 and 3 are FALLBACKS: they are counted (<see cref="RegionFallbackCount"/>),
    /// reported (<see cref="LastResolution"/>) and warned about once, so a hitbox that
    /// silently stopped registering can never route damage to the wrong region unseen.
    /// Neither path requires the weapon code to know a single type from the enemy task.
    /// </summary>
    public static class DamageTargetResolver
    {
        [DD.Prototype.Core.Config.PrototypeOnly(DD.Prototype.Core.Config.PendingDecisions.None,
            "Fallback only: fraction of standing height above which an UNREGISTERED, unnamed " +
            "collider hit counts as the head. The registered hitbox path never uses it.", true)]
        private const float HeadThreshold = 0.80f;

        [DD.Prototype.Core.Config.PrototypeOnly(DD.Prototype.Core.Config.PendingDecisions.None,
            "Fallback only: fraction of standing height above which an unregistered, unnamed " +
            "collider hit counts as the upper body; below it, the lower body.", true)]
        private const float UpperBodyThreshold = 0.45f;

        private static int s_regionFallbackCount;
        private static RegionResolution s_lastResolution = RegionResolution.None;
        private static bool s_fallbackWarned;

        /// <summary>
        /// How many region resolutions fell back to the collider name or the hit height
        /// because the collider was not in <see cref="HitboxRegistry"/>. 0 for a healthy
        /// enemy roster; every non-zero count names a collider that lost its registration.
        /// </summary>
        public static int RegionFallbackCount => s_regionFallbackCount;

        /// <summary>Which path answered the most recent <see cref="ResolveRegion"/> call. Test/probe readout.</summary>
        public static RegionResolution LastResolution => s_lastResolution;

        // PROTOTYPE-ONLY heights above which an unlabelled enemy is treated as heavy.
        private const float EliteHeightThreshold = 2.05f;
        private const float UltraHeightThreshold = 2.45f;

        private const float DefaultHeight = 1.8f;

        private struct ColliderInfo
        {
            public DamageTarget Target;
            public BodyRegion NamedRegion;
            public bool HasNamedRegion;
        }

        private static readonly Dictionary<int, ColliderInfo> s_cache = new Dictionary<int, ColliderInfo>(512);
        private static readonly List<Renderer> s_rendererScratch = new List<Renderer>(16);
        private static bool s_hooked;

        /// <summary>Cached collider count. F1 readout.</summary>
        public static int CachedColliders => s_cache.Count;

        /// <summary>Resolve the enemy behind a collider. False for world geometry.</summary>
        public static bool TryResolve(Collider collider, out DamageTarget target)
        {
            if (collider == null)
            {
                target = default;
                return false;
            }

            ColliderInfo info = GetInfo(collider);
            target = info.Target;

            // A pooled enemy that was destroyed leaves a stale entry; Unity's null
            // overload catches it.
            if (target.Root == null)
            {
                return false;
            }

            return target.Receiver != null;
        }

        /// <summary>Which body region was struck. See the class remarks for the three paths.</summary>
        public static BodyRegion ResolveRegion(Collider collider, in DamageTarget target, Vector3 point)
        {
            if (collider != null)
            {
                // 1. The authority: the region the enemy registered this collider for.
                if (DamageQuery.TryResolveRegion(collider, out BodyRegion registered))
                {
                    s_lastResolution = RegionResolution.Registry;
                    return registered;
                }

                // 2. Fallback: collider name. Counted and warned about once.
                ColliderInfo info = GetInfo(collider);
                if (info.HasNamedRegion)
                {
                    NoteFallback(RegionResolution.Name);
                    return info.NamedRegion;
                }
            }

            // 3. Fallback: hit height. Counted and warned about once.
            NoteFallback(RegionResolution.Height);

            if (target.Root == null)
            {
                return BodyRegion.UpperBody;
            }

            float height = target.Height > 0.1f ? target.Height : DefaultHeight;
            float local = (point.y - target.Root.position.y) / height;

            if (local >= HeadThreshold)
            {
                return BodyRegion.Head;
            }

            return local >= UpperBodyThreshold ? BodyRegion.UpperBody : BodyRegion.LowerBody;
        }

        /// <summary>Drop everything. Scene change, or an enemy prefab was rebuilt at runtime.</summary>
        public static void Invalidate()
        {
            s_cache.Clear();
        }

        /// <summary>Test hook: zero the fallback counter and forget the one-time warning.</summary>
        public static void ResetRegionDiagnostics()
        {
            s_regionFallbackCount = 0;
            s_lastResolution = RegionResolution.None;
            s_fallbackWarned = false;
        }

        private static void NoteFallback(RegionResolution path)
        {
            s_lastResolution = path;
            s_regionFallbackCount++;

            if (s_fallbackWarned)
            {
                return;
            }

            // Once per session, constant string: no per-hit allocation, no log spam.
            s_fallbackWarned = true;
            Debug.LogWarning(FallbackWarning);
        }

        private const string FallbackWarning =
            "[Damage] A hit collider was not registered in HitboxRegistry; the body region was " +
            "resolved from the collider name or the hit height instead (see " +
            "DamageTargetResolver.RegionFallbackCount / LastResolution). An enemy hitbox that " +
            "lost its registration routes damage by geometry, which is a fallback, not the model.";

        // ------------------------------------------------------------------ internals

        private static ColliderInfo GetInfo(Collider collider)
        {
            EnsureHooked();

            int key = collider.GetInstanceID();
            if (s_cache.TryGetValue(key, out ColliderInfo cached))
            {
                return cached;
            }

            var info = new ColliderInfo();

            IDamageReceiver receiver = collider.GetComponentInParent<IDamageReceiver>();
            if (receiver != null)
            {
                var asComponent = receiver as Component;
                Transform root = asComponent != null ? asComponent.transform : collider.transform.root;

                float height = MeasureHeight(root);

                info.Target = new DamageTarget
                {
                    Receiver = receiver,
                    Explosive = receiver as IExplosionReceiver
                                ?? (asComponent != null ? asComponent.GetComponent<IExplosionReceiver>() : null),
                    Root = root,
                    Id = asComponent != null ? asComponent.GetInstanceID() : root.GetInstanceID(),
                    Height = height,
                    Weight = ResolveWeight(root, height)
                };
            }

            info.HasNamedRegion = TryRegionFromName(collider.name, out info.NamedRegion);

            s_cache[key] = info;
            return info;
        }

        private static bool TryRegionFromName(string name, out BodyRegion region)
        {
            region = BodyRegion.UpperBody;

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();

            if (lower.Contains("head") || lower.Contains("helm") || lower.Contains("kabuto") || lower.Contains("skull"))
            {
                region = BodyRegion.Head;
                return true;
            }

            if (lower.Contains("leg") || lower.Contains("lower") || lower.Contains("shin")
                || lower.Contains("thigh") || lower.Contains("haidate") || lower.Contains("suneate"))
            {
                region = BodyRegion.LowerBody;
                return true;
            }

            if (lower.Contains("torso") || lower.Contains("chest") || lower.Contains("body")
                || lower.Contains("upper") || lower.Contains("arm") || lower.Contains("kote"))
            {
                region = BodyRegion.UpperBody;
                return true;
            }

            return false;
        }

        private static FeedbackWeight ResolveWeight(Transform root, float height)
        {
            // Exact answer when the enemy opts in.
            var source = root.GetComponentInParent<IFeedbackWeightSource>();
            if (source != null)
            {
                return source.Weight;
            }

            // Fallback: the request makes Elites "wider bodied" and Ultras "slow, large
            // and easily recognised", so silhouette size is a defensible stand-in until
            // the enemy task opts in.
            if (height >= UltraHeightThreshold)
            {
                return FeedbackWeight.Ultra;
            }

            return height >= EliteHeightThreshold ? FeedbackWeight.Elite : FeedbackWeight.Standard;
        }

        private static float MeasureHeight(Transform root)
        {
            s_rendererScratch.Clear();
            root.GetComponentsInChildren(true, s_rendererScratch);

            if (s_rendererScratch.Count == 0)
            {
                return DefaultHeight;
            }

            Bounds bounds = s_rendererScratch[0].bounds;
            for (int i = 1; i < s_rendererScratch.Count; i++)
            {
                bounds.Encapsulate(s_rendererScratch[i].bounds);
            }

            s_rendererScratch.Clear();

            float height = bounds.size.y;
            return height > 0.2f ? height : DefaultHeight;
        }

        private static void EnsureHooked()
        {
            if (s_hooked)
            {
                return;
            }

            s_hooked = true;
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private static void OnSceneChanged(Scene from, Scene to)
        {
            s_cache.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (s_hooked)
            {
                SceneManager.activeSceneChanged -= OnSceneChanged;
            }

            s_hooked = false;
            s_cache.Clear();
            s_rendererScratch.Clear();
            s_regionFallbackCount = 0;
            s_lastResolution = RegionResolution.None;
            s_fallbackWarned = false;
        }
    }

    /// <summary>Which path answered a region resolution. Registry is the model; the rest are fallbacks.</summary>
    public enum RegionResolution
    {
        None = 0,
        Registry = 1,
        Name = 2,
        Height = 3
    }
}
