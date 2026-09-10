using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Weapons
{
    /// <summary>One pellet or one bullet, fully described. Built fresh by the weapon each shot.</summary>
    public struct ShotRequest
    {
        public Vector3 Origin;
        public Vector3 Direction;

        public float Damage;
        public float Range;

        /// <summary>Stagger impulse handed to the enemy. Governed before it is sent.</summary>
        public float ImpactForce;

        public DamageSourceKind Source;
        public WeaponPower Power;

        /// <summary>Extra enemies the shot passes through. 0 = stops at the first.</summary>
        public int PierceTargets;

        public float PierceDamageFalloff;

        // ---- upgrade branches. All zero means "no branch owned", and the whole block
        // ---- is skipped without a single extra query.
        public float RicochetChance;
        public int RicochetBounces;
        public float RicochetRadius;
        public float RicochetDamageScale;

        public int ChainTargets;
        public float ChainRadius;
        public float ChainDamageScale;
        public float ChainDamageFalloff;

        public float ExplosionChance;
        public float ExplosionRadius;
        public float ExplosionDamageScale;

        // ---- presentation
        public bool DrawTracer;
        public float TracerWidth;
        public float TracerLifetime;
    }

    /// <summary>What one shot did. Enough for the weapon to decide on recoil and audio.</summary>
    public struct ShotResult
    {
        public bool HitSomething;
        public Vector3 EndPoint;
        public int EnemiesHit;
        public bool AnyKill;
        public bool AnyArmorBreak;
    }

    /// <summary>
    /// Resolves a shot. Raycast, not a travelling projectile.
    ///
    /// WHY HITSCAN
    /// The prototype targets forty simultaneous enemies at 60 fps, and the weapons fire
    /// at up to 15 shots/s with 8 pellets a shot. A pooled physical projectile for each
    /// would be 120 rigidbodies a second, all of which have to be stepped, swept and
    /// pooled, to model a bullet that crosses a 22-45 m range in a few milliseconds. The
    /// request asks for "a tracer OR a readable projectile line" - the tracer answer is
    /// the one that survives the crowd. The visible line still sweeps, so the shot reads
    /// as travelling even though it resolved instantly.
    ///
    /// ALLOCATION
    /// All buffers are static and reused. One shot allocates nothing: no LINQ, no
    /// GetComponent (the resolver's dictionary answers that), no array creation, no
    /// closure. That is deliberate - a shotgun blast into a pack is the single worst
    /// allocation moment in the whole prototype.
    /// </summary>
    public static class HitscanEngine
    {
        private const int RayBufferSize = 24;
        private const int OverlapBufferSize = 64;

        private static readonly RaycastHit[] s_rayBuffer = new RaycastHit[RayBufferSize];
        private static readonly Collider[] s_overlapBuffer = new Collider[OverlapBufferSize];

        /// <summary>Ids already damaged by the shot in flight. Cleared per shot, never reallocated.</summary>
        private static readonly List<int> s_touched = new List<int>(24);

        private static int s_enemyQueryMask;
        private static bool s_maskResolved;

        /// <summary>Enemy bodies and enemy hitboxes, for radius queries.</summary>
        public static int EnemyQueryMask
        {
            get
            {
                if (!s_maskResolved)
                {
                    s_maskResolved = true;
                    s_enemyQueryMask = (1 << PrototypeLayers.Enemy) | (1 << PrototypeLayers.EnemyHitbox);
                }

                return s_enemyQueryMask;
            }
        }

        /// <summary>Fire one pellet. Everything downstream - pierce, ricochet, chain, blast - happens here.</summary>
        public static ShotResult Fire(in ShotRequest request)
        {
            var result = new ShotResult
            {
                EndPoint = request.Origin + request.Direction * request.Range
            };

            s_touched.Clear();

            int count = Physics.RaycastNonAlloc(
                request.Origin,
                request.Direction,
                s_rayBuffer,
                request.Range,
                PrototypeLayers.BulletHitMask,
                QueryTriggerInteraction.Collide);

            SortByDistance(count);

            int allowedTargets = 1 + Mathf.Max(0, request.PierceTargets);
            float damage = request.Damage;

            bool haveFirstEnemy = false;
            Vector3 firstEnemyPoint = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = s_rayBuffer[i];
                Collider collider = hit.collider;

                if (collider == null)
                {
                    continue;
                }

                if (!DamageTargetResolver.TryResolve(collider, out DamageTarget target))
                {
                    // World geometry. The shot stops here (steps 5 of the chain).
                    result.HitSomething = true;
                    result.EndPoint = hit.point;

                    HitFeedbackDirector.Instance.ReportSurfaceImpact(
                        hit.point,
                        hit.normal,
                        SurfaceResolver.Resolve(collider),
                        ImpactScaleFor(request.Power));

                    break;
                }

                if (s_touched.Contains(target.Id))
                {
                    // A second hitbox on an enemy we already damaged this shot.
                    continue;
                }

                s_touched.Add(target.Id);

                BodyRegion region = DamageTargetResolver.ResolveRegion(collider, target, hit.point);
                DamageResult damageResult = ApplyHit(
                    target, damage, region, hit.point, request.Direction,
                    request.ImpactForce, request.Source, request.Power, hit.normal);

                result.HitSomething = true;
                result.EndPoint = hit.point;
                result.EnemiesHit++;
                result.AnyKill |= damageResult.Killed;
                result.AnyArmorBreak |= damageResult.ArmorBroke;

                if (!haveFirstEnemy)
                {
                    haveFirstEnemy = true;
                    firstEnemyPoint = hit.point;
                }

                allowedTargets--;
                if (allowedTargets <= 0)
                {
                    break;
                }

                // A pierced shot is a weakened shot: piercing buys reach, not free damage.
                damage *= Mathf.Clamp(request.PierceDamageFalloff, 0.1f, 1f);
            }

            if (request.DrawTracer)
            {
                HitFeedbackDirector.Instance.ReportTracer(
                    request.Origin, result.EndPoint, request.TracerWidth, request.TracerLifetime);
            }

            if (haveFirstEnemy)
            {
                ResolveBranches(request, firstEnemyPoint, ref result);
            }

            return result;
        }

        // ------------------------------------------------------------------ upgrade branches

        private static void ResolveBranches(in ShotRequest request, Vector3 originPoint, ref ShotResult result)
        {
            if (request.ChainTargets > 0)
            {
                ApplyChain(request, originPoint, ref result);
            }

            if (request.RicochetBounces > 0 && request.RicochetChance > 0f
                && Random.value <= request.RicochetChance)
            {
                ApplyRicochet(request, originPoint, ref result);
            }

            if (request.ExplosionRadius > 0f && request.ExplosionChance > 0f
                && Random.value <= request.ExplosionChance)
            {
                Explode(
                    originPoint,
                    request.Damage * Mathf.Max(0f, request.ExplosionDamageScale),
                    request.ExplosionRadius,
                    ref result);
            }
        }

        private static void ApplyChain(in ShotRequest request, Vector3 from, ref ShotResult result)
        {
            float damage = request.Damage * Mathf.Clamp(request.ChainDamageScale, 0.05f, 1f);
            Vector3 current = from;

            for (int jump = 0; jump < request.ChainTargets; jump++)
            {
                if (!TryFindNearestUntouched(current, request.ChainRadius, out DamageTarget next, out Vector3 point))
                {
                    return;
                }

                s_touched.Add(next.Id);

                Vector3 direction = (point - current).normalized;

                // Chain arcs carry the contract's own weak force, not a fraction of the
                // firing weapon's: the request wants chains to never be a stagger engine.
                DamageResult r = ApplyHit(
                    next, damage, BodyRegion.UpperBody, point, direction,
                    DamageForce.Chain, DamageSourceKind.Chain, WeaponPower.Light, -direction);

                result.EnemiesHit++;
                result.AnyKill |= r.Killed;
                result.AnyArmorBreak |= r.ArmorBroke;

                HitFeedbackDirector.Instance.ReportLink(current, point, false);

                current = point;
                damage *= Mathf.Clamp(request.ChainDamageFalloff, 0.1f, 1f);
            }
        }

        private static void ApplyRicochet(in ShotRequest request, Vector3 from, ref ShotResult result)
        {
            float damage = request.Damage * Mathf.Clamp(request.RicochetDamageScale, 0.05f, 1f);
            Vector3 current = from;

            for (int bounce = 0; bounce < request.RicochetBounces; bounce++)
            {
                if (!TryFindNearestUntouched(current, request.RicochetRadius, out DamageTarget next, out Vector3 point))
                {
                    return;
                }

                s_touched.Add(next.Id);

                Vector3 direction = (point - current).normalized;

                DamageResult r = ApplyHit(
                    next, damage, BodyRegion.UpperBody, point, direction,
                    DamageForce.Ricochet, DamageSourceKind.Ricochet, request.Power, -direction);

                result.EnemiesHit++;
                result.AnyKill |= r.Killed;
                result.AnyArmorBreak |= r.ArmorBroke;

                HitFeedbackDirector.Instance.ReportLink(current, point, true);

                current = point;
                damage *= Mathf.Clamp(request.RicochetDamageScale, 0.1f, 1f);
            }
        }

        /// <summary>
        /// Small blast. The three-region armour split is NOT applied here: the damage
        /// system owns that rule, and it is reached through <see cref="IExplosionReceiver"/>
        /// exactly so this class cannot get it subtly wrong.
        /// </summary>
        public static void Explode(Vector3 origin, float totalDamage, float radius, ref ShotResult result)
        {
            HitFeedbackDirector.Instance.ReportExplosion(origin, radius);

            int count = Physics.OverlapSphereNonAlloc(
                origin, radius, s_overlapBuffer, EnemyQueryMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                if (!DamageTargetResolver.TryResolve(s_overlapBuffer[i], out DamageTarget target))
                {
                    continue;
                }

                if (s_touched.Contains(target.Id))
                {
                    continue;
                }

                s_touched.Add(target.Id);

                if (target.Explosive == null)
                {
                    continue;
                }

                Vector3 point = target.Root.position + Vector3.up * (target.Height * 0.5f);

                // Linear falloff. The interface takes a total, so the falloff has to be
                // applied before the call rather than inside the damage system.
                float distance = Vector3.Distance(origin, point);
                float falloff = radius > 0.001f ? Mathf.Clamp01(1f - distance / radius) : 1f;
                float amount = totalDamage * Mathf.Lerp(0.45f, 1f, falloff);

                DamageResult r = target.Explosive.ApplyExplosion(amount, origin);

                result.EnemiesHit++;
                result.AnyKill |= r.Killed;
                result.AnyArmorBreak |= r.ArmorBroke;

                var info = new EnemyHitFeedbackInfo
                {
                    Point = point,
                    Direction = (point - origin).normalized,
                    Normal = (point - origin).normalized,
                    Result = r,
                    Power = WeaponPower.Heavy,
                    TargetId = target.Id,
                    TargetRoot = target.Root,
                    Weight = target.Weight,
                    ImpactForce = 1f,
                    FromExplosion = true
                };

                HitFeedbackDirector.Instance.ReportEnemyHit(info);
            }
        }

        // ------------------------------------------------------------------ Special Weapon entry points

        /// <summary>
        /// Apply one already-resolved hit, with the full twelve-step dressing, from
        /// something that is not a bullet.
        ///
        /// This exists for the three Special Weapons (G-21), whose damage events are a
        /// pressure wave, a talisman web and a gravity well - none of which is a ray, and
        /// none of which should therefore have to fake one. Routing them through here
        /// rather than letting each weapon talk to <see cref="IDamageReceiver"/> directly
        /// is what keeps ONE stagger governor, ONE feedback director call and ONE place
        /// where REQ-124 is enforced, no matter how exotic the weapon gets.
        ///
        /// <paramref name="direction"/> is the reaction direction handed to the enemy. It
        /// is what makes the Tide Jewel a PULL: the same contract that shoves a body away
        /// from a shotgun drags it toward a well when the vector is reversed, with no
        /// enemy-side code at all.
        /// </summary>
        public static DamageResult ApplyExternalHit(
            in DamageTarget target,
            float damage,
            Vector3 point,
            Vector3 direction,
            float impactForce,
            DamageSourceKind source,
            WeaponPower power)
        {
            if (!target.IsValid)
            {
                return default;
            }

            Vector3 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

            return ApplyHit(
                target, damage, BodyRegion.UpperBody, point, safeDirection,
                impactForce, source, power, -safeDirection);
        }

        /// <summary>
        /// Enemies inside a sphere, de-duplicated by enemy id rather than by collider.
        ///
        /// Allocation-free for the caller: the list is the caller's and is only cleared
        /// and filled. The overlap buffer is this class's own static one, which is why
        /// this must never be called from inside another overlap loop.
        /// </summary>
        public static int OverlapEnemies(Vector3 origin, float radius, List<DamageTarget> results)
        {
            if (results == null)
            {
                return 0;
            }

            results.Clear();

            if (radius <= 0f)
            {
                return 0;
            }

            int count = Physics.OverlapSphereNonAlloc(
                origin, radius, s_overlapBuffer, EnemyQueryMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                if (!DamageTargetResolver.TryResolve(s_overlapBuffer[i], out DamageTarget candidate))
                {
                    continue;
                }

                bool seen = false;
                for (int j = 0; j < results.Count; j++)
                {
                    if (results[j].Id != candidate.Id)
                    {
                        continue;
                    }

                    seen = true;
                    break;
                }

                if (!seen)
                {
                    results.Add(candidate);
                }
            }

            return results.Count;
        }

        /// <summary>Centre of mass of a resolved target, for effects and for hit points.</summary>
        public static Vector3 CentreOf(in DamageTarget target)
        {
            if (target.Root == null)
            {
                return Vector3.zero;
            }

            float height = target.Height > 0.1f ? target.Height : 1.8f;
            return target.Root.position + Vector3.up * (height * 0.55f);
        }

        /// <summary>Standalone blast for callers outside a shot (debug panel, future grenades).</summary>
        public static void Explode(Vector3 origin, float totalDamage, float radius)
        {
            var result = new ShotResult();
            s_touched.Clear();
            Explode(origin, totalDamage, radius, ref result);
        }

        // ------------------------------------------------------------------ shared

        private static DamageResult ApplyHit(
            in DamageTarget target,
            float damage,
            BodyRegion region,
            Vector3 point,
            Vector3 direction,
            float impactForce,
            DamageSourceKind source,
            WeaponPower power,
            Vector3 normal)
        {
            // REQ-124: throttle the impulse before it ever reaches the enemy.
            float governed = StaggerGovernor.Govern(target.Id, impactForce);

            var hit = new DamageHit
            {
                Amount = damage,
                Region = region,
                Point = point,
                Direction = direction,
                ImpactForce = governed,
                Source = source
            };

            DamageResult result = target.Receiver.ApplyHit(hit);

            var info = new EnemyHitFeedbackInfo
            {
                Point = point,
                Direction = direction,
                Normal = normal,
                Result = result,
                Power = power,
                TargetId = target.Id,
                TargetRoot = target.Root,
                Weight = target.Weight,

                // Feedback wants a burst-size multiplier around 1, and the DamageForce
                // scale already puts a rifle bullet at exactly 1. Clamped so a pathological
                // build cannot produce a blood spray the size of a building.
                ImpactForce = Mathf.Clamp(governed, 0.35f, 2.5f),
                FromExplosion = false
            };

            HitFeedbackDirector.Instance.ReportEnemyHit(info);
            return result;
        }

        private static bool TryFindNearestUntouched(
            Vector3 from, float radius, out DamageTarget target, out Vector3 point)
        {
            target = default;
            point = Vector3.zero;

            if (radius <= 0f)
            {
                return false;
            }

            int count = Physics.OverlapSphereNonAlloc(
                from, radius, s_overlapBuffer, EnemyQueryMask, QueryTriggerInteraction.Collide);

            float bestSqr = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                if (!DamageTargetResolver.TryResolve(s_overlapBuffer[i], out DamageTarget candidate))
                {
                    continue;
                }

                if (s_touched.Contains(candidate.Id))
                {
                    continue;
                }

                Vector3 candidatePoint = candidate.Root.position + Vector3.up * (candidate.Height * 0.55f);
                float sqr = (candidatePoint - from).sqrMagnitude;

                if (sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                target = candidate;
                point = candidatePoint;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// RaycastNonAlloc does not sort. Insertion sort over a handful of hits is both
        /// faster than Array.Sort here and allocation-free, which Array.Sort with a
        /// comparer would not be.
        /// </summary>
        private static void SortByDistance(int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit key = s_rayBuffer[i];
                int j = i - 1;

                while (j >= 0 && s_rayBuffer[j].distance > key.distance)
                {
                    s_rayBuffer[j + 1] = s_rayBuffer[j];
                    j--;
                }

                s_rayBuffer[j + 1] = key;
            }
        }

        private static float ImpactScaleFor(WeaponPower power)
        {
            switch (power)
            {
                case WeaponPower.Heavy: return 1.5f;
                case WeaponPower.Medium: return 1f;
                default: return 0.75f;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_touched.Clear();
            s_maskResolved = false;
            s_enemyQueryMask = 0;
        }
    }
}
