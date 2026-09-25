using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
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

        /// <summary>WP-042: where the tracer starts when it differs from the damage ray's origin (the visual muzzle); Origin when unset.</summary>
        public bool HasTracerOrigin;
        public Vector3 TracerOrigin;
        public int PresentationGroupId;
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

        [DD.Prototype.Core.Config.PrototypeOnly(DD.Prototype.Core.Config.PendingDecisions.None,
            "Colliders one blast query can see. WP-006: raised from 64 so a full crowd at the " +
            "40 ceiling (3 hitboxes + 1 body each = 160) cannot overflow the buffer and drop " +
            "enemies from a blast silently; overflow is counted (OverlapSaturationCount).", true)]
        private const int OverlapBufferSize = 192;

        [DD.Prototype.Core.Config.PrototypeOnly(DD.Prototype.Core.Config.PendingDecisions.None,
            "Fraction of a blast's total damage a body at the very rim still takes (linear " +
            "falloff from 1.0 at the centre). Was an unlabelled literal before WP-006; value " +
            "unchanged, not a product decision.", true)]
        private const float ExplosionRimDamageFraction = 0.45f;

        private static int s_overlapSaturationCount;

        /// <summary>Blast queries that filled the overlap buffer completely (possible drop). Should stay 0.</summary>
        public static int OverlapSaturationCount => s_overlapSaturationCount;

        private static readonly RaycastHit[] s_rayBuffer = new RaycastHit[RayBufferSize];
        private static readonly Collider[] s_overlapBuffer = new Collider[OverlapBufferSize];

        /// <summary>Ids already damaged by the shot in flight. Cleared per shot, never reallocated.</summary>
        private static readonly List<int> s_touched = new List<int>(OverlapBufferSize);   // one blast can touch every body the buffer holds

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
            // One transaction per pellet/bullet, covering its pierce, chain, ricochet and
            // explosion branches. The enemy stamps it into its events and refuses a second
            // application inside it (WP-006 event contract).
            DamageTransaction.Begin();
            try
            {
                ShotRequest grouped = request;
                if (grouped.PresentationGroupId == 0) grouped.PresentationGroupId = FeelEventRouter.NextGroupId();
                return FireInTransaction(in grouped);
            }
            finally
            {
                DamageTransaction.End();
            }
        }

        private static ShotResult FireInTransaction(in ShotRequest request)
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

                    // WP-013 (REQ-113): a breakable prop takes the impact; no rule beyond "it breaks".
                    DD.Prototype.Props.PropImpacts.Report(collider, hit.point, request.Direction);

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
                    request.ImpactForce, request.Source, request.Power, hit.normal, request.PresentationGroupId);

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
                    request.HasTracerOrigin ? request.TracerOrigin : request.Origin, result.EndPoint, request.TracerWidth, request.TracerLifetime);
            }

            if (haveFirstEnemy)
            {
                ResolveBranches(request, firstEnemyPoint, ref result);
            }

            return result;
        }

        // ------------------------------------------------------------------ WP-069 · projectile segment (ADR-069-1)

        /// <summary>
        /// WP-069 ADR-069-1: resolve one projectile step — sweep <paramref name="length"/> metres along
        /// <paramref name="direction"/> (ray, or sphere when the projectile has a radius) and apply what it
        /// meets exactly as a hitscan shot would (world stops it; a body takes the hit, region, stagger
        /// governor and feedback; penetration carries it on with the kept damage share), inside the
        /// projectile's own resumed transaction. The first body hit resolves the per-shot branches once
        /// (chain / ricochet / explosion, ADR-034-1). Returns true when the projectile stops here.
        /// </summary>
        internal static bool ResolveProjectileSegment(ref ProjectileState p, Vector3 from, Vector3 direction, float length,
            out float travelled, out Vector3 end)
        {
            travelled = length;
            end = from + direction * length;

            int count = p.SweepRadius > 0f
                ? Physics.SphereCastNonAlloc(from, p.SweepRadius, direction, s_rayBuffer, length, PrototypeLayers.BulletHitMask, QueryTriggerInteraction.Collide)
                : Physics.RaycastNonAlloc(from, direction, s_rayBuffer, length, PrototypeLayers.BulletHitMask, QueryTriggerInteraction.Collide);

            if (count <= 0)
            {
                return false;
            }

            SortByDistance(count);

            DamageTransaction.Resume(p.TransactionId);
            try
            {
                s_touched.Clear();
                for (int t = 0; t < p.TouchedCount; t++) s_touched.Add(p.GetTouched(t));

                var result = new ShotResult();
                bool stopped = false;
                bool branchesNow = false;
                Vector3 branchPoint = Vector3.zero;
                float branchDamage = 0f;
                float speed = p.Velocity.magnitude;

                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = s_rayBuffer[i];
                    Collider collider = hit.collider;
                    if (collider == null)
                    {
                        continue;
                    }

                    // A sphere sweep that starts inside a collider reports distance 0 and point zero.
                    Vector3 point = hit.distance <= 0f && hit.point == Vector3.zero ? from : hit.point;
                    Vector3 normal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal : -direction;
                    float at = Mathf.Max(0f, hit.distance);

                    // WP-069 B4: a Special's orb / gel blob does not apply a bullet hit — its first contact is handed to
                    // SpecialImpacts (Raiju chain, Namekuji direct hit or splat) and the projectile stops there.
                    if (p.Kind == DD.Prototype.Core.Weapons.ProjectileKind.Orb || p.Kind == DD.Prototype.Core.Weapons.ProjectileKind.Gel)
                    {
                        bool body = DamageTargetResolver.TryResolve(collider, out DamageTarget special);
                        if (body) SpecialImpacts.OnBody(in p, in special, point);
                        else SpecialImpacts.OnWorld(in p, point, normal);
                        ProjectileEvents.RaiseImpact(new ProjectileImpactInfo(p.Generation, p.Slot, p.WeaponId, p.Kind, p.Caliber, point, normal, direction,
                            body ? SurfaceKind.Flesh : SurfaceResolver.Resolve(collider), speed, body, false, false, true, p.TransactionId));
                        travelled = at;
                        end = point;
                        stopped = true;
                        break;
                    }

                    bool rocket = p.Kind == DD.Prototype.Core.Weapons.ProjectileKind.Rocket;
                    if (!DamageTargetResolver.TryResolve(collider, out DamageTarget target))
                    {
                        // World geometry stops the projectile (M3 rule).
                        DD.Prototype.Props.PropImpacts.Report(collider, point, direction);
                        SurfaceKind surface = SurfaceResolver.Resolve(collider);
                        HitFeedbackDirector.Instance.ReportSurfaceImpact(point, normal, surface, ImpactScaleFor(p.Shot.Power));
                        ProjectileEvents.RaiseImpact(new ProjectileImpactInfo(p.Generation, p.Slot, p.WeaponId, p.Kind, p.Caliber, point, normal, direction,
                            surface, speed, false, false, false, true, p.TransactionId));
                        if (rocket) RocketImpacts.MarkContact(ref p, point, normal, 0, at);   // WP-069 B3: blast (armed) or dud here
                        travelled = at;
                        end = point;
                        stopped = true;
                        break;
                    }

                    if (s_touched.Contains(target.Id))
                    {
                        continue;   // a second hitbox of a body this projectile already damaged
                    }

                    s_touched.Add(target.Id);
                    p.AddTouched(target.Id);

                    BodyRegion region = DamageTargetResolver.ResolveRegion(collider, target, point);
                    float share = FalloffShareOf(in p, p.Travelled + at);
                    float damage = p.Shot.Damage * p.DamageScale * share;
                    if (rocket)
                    {
                        // WP-069 B3: the direct hit (110) lands here in the rocket's own transaction; the blast follows in a
                        // new one (RocketImpacts), so the struck body takes both. Inside the arming distance: the dud's 30 only.
                        RocketImpacts.MarkContact(ref p, point, normal, target.Id, at);
                        if (!p.ContactArmed) damage = RocketImpacts.DudDamageOf(p.WeaponId);
                    }
                    // WP-069 (ADR-069-1 §9): the weapon's head / armour multipliers and the hit identity ride beside the frozen DamageHit.
                    DamageHitContext.Set(p.HeadMultiplier, p.ArmorMultiplier, true, p.WeaponId, collider);
                    DamageResult r;
                    try
                    {
                        r = ApplyHit(target, damage, region, point, direction,
                            p.Shot.ImpactForce, p.Shot.Source, p.Shot.Power, normal, p.Shot.PresentationGroupId);
                    }
                    finally
                    {
                        DamageHitContext.Clear();
                    }

                    if (!p.BranchesResolved && !branchesNow && !rocket)   // a rocket's branches are its blast (Blast / Cluster)
                    {
                        branchesNow = true;
                        branchPoint = point;
                        branchDamage = p.Shot.Damage * share;
                    }

                    p.TargetsLeft--;
                    bool stop = p.TargetsLeft <= 0;
                    ProjectileEvents.RaiseImpact(new ProjectileImpactInfo(p.Generation, p.Slot, p.WeaponId, p.Kind, p.Caliber, point, normal, direction,
                        r.WasArmored ? SurfaceKind.Armor : SurfaceKind.Flesh, speed, true, r.WasArmored, r.Killed, stop, p.TransactionId));

                    if (stop)
                    {
                        travelled = at;
                        end = point;
                        stopped = true;
                        break;
                    }

                    // A pierced shot is a weakened shot: piercing buys reach, not free damage (WP-077 ks.piercing.line: not through the first bodies).
                    p.DamageScale *= DD.Prototype.Rounds.RunTreeHooks.PierceKeep(p.TouchedCount, p.PenetrationKeep);
                }

                if (branchesNow)
                {
                    p.BranchesResolved = true;
                    ShotRequest branches = p.Shot;
                    branches.Damage = branchDamage;
                    int before = s_touched.Count;
                    ResolveBranches(branches, branchPoint, ref result);
                    for (int t = before; t < s_touched.Count; t++) p.AddTouched(s_touched[t]);
                }

                return stopped;
            }
            finally
            {
                DamageTransaction.End();
            }
        }

        private static float FalloffShareOf(in ProjectileState p, float travelled)
        {
            if (p.FalloffEnd <= p.FalloffStart + 1e-4f)
            {
                return 1f;
            }

            float t = Mathf.InverseLerp(p.FalloffStart, p.FalloffEnd, travelled);
            return Mathf.Lerp(1f, Mathf.Clamp01(p.FalloffMinShare), t);
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
                    ref result,
                    request.PresentationGroupId);
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
                    DamageForce.Chain, DamageSourceKind.Chain, WeaponPower.Light, -direction, request.PresentationGroupId);

                result.EnemiesHit++;
                result.AnyKill |= r.Killed;
                result.AnyArmorBreak |= r.ArmorBroke;

                HitFeedbackDirector.Instance.ReportLink(current, point, false);

                // WP-077 ks.chain.web: the first jump splits — the extra arcs start where the chain did.
                if (jump == 0)
                {
                    for (int extra = 1; extra < DD.Prototype.Rounds.RunTreeHooks.ChainFirstJumpTargets; extra++)
                    {
                        if (!TryFindNearestUntouched(from, request.ChainRadius, out DamageTarget fork, out Vector3 forkPoint)) break;
                        s_touched.Add(fork.Id);
                        Vector3 forkDirection = (forkPoint - from).normalized;
                        DamageResult f = ApplyHit(
                            fork, damage, BodyRegion.UpperBody, forkPoint, forkDirection,
                            DamageForce.Chain, DamageSourceKind.Chain, WeaponPower.Light, -forkDirection, request.PresentationGroupId);
                        result.EnemiesHit++;
                        result.AnyKill |= f.Killed;
                        result.AnyArmorBreak |= f.ArmorBroke;
                        HitFeedbackDirector.Instance.ReportLink(from, forkPoint, false);
                    }
                }

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
                if (!TryFindNearestUntouched(current, DD.Prototype.Rounds.RunTreeHooks.RicochetRadius(request.RicochetRadius), out DamageTarget next, out Vector3 point))
                {
                    return;
                }

                s_touched.Add(next.Id);

                Vector3 direction = (point - current).normalized;

                DamageResult r = ApplyHit(
                    next, damage, BodyRegion.UpperBody, point, direction,
                    DamageForce.Ricochet, DamageSourceKind.Ricochet, request.Power, -direction, request.PresentationGroupId);

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
        public static void Explode(Vector3 origin, float totalDamage, float radius, ref ShotResult result, int presentationGroupId = 0)
        {
            int group = presentationGroupId != 0 ? presentationGroupId : FeelEventRouter.NextGroupId();
            HitFeedbackDirector.Instance.ReportExplosion(origin, radius, group);

            int count = Physics.OverlapSphereNonAlloc(
                origin, radius, s_overlapBuffer, EnemyQueryMask, QueryTriggerInteraction.Collide);

            if (count >= OverlapBufferSize)
            {
                // The query may have seen more colliders than the buffer holds; a body
                // could have been dropped from this blast. Counted so it is never silent.
                s_overlapSaturationCount++;
            }

            for (int i = 0; i < count; i++)
            {
                if (!DamageTargetResolver.TryResolve(s_overlapBuffer[i], out DamageTarget target))
                {
                    continue;
                }

                // ONE application per enemy per blast, whichever of its colliders (three
                // hitboxes plus the body) the sphere saw first - REQ-085: one budget.
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
                float amount = totalDamage * Mathf.Lerp(ExplosionRimDamageFraction, 1f, falloff);

                DamageResult r = target.Explosive.ApplyExplosion(amount, origin);

                if (DamageResults.IsEmpty(in r))
                {
                    // A corpse, or a receiver that refused the second application inside
                    // this transaction: nothing happened, so nothing is dressed or counted.
                    continue;
                }

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
                    FromExplosion = true,
                    PresentationGroupId = group,
                    SnapshotRevision = FeelRuntime.Current.Revision
                };

                HitFeedbackDirector.Instance.ReportEnemyHit(info);
            }
        }

        // ------------------------------------------------------------------ Special Weapon entry points

        /// <summary>
        /// WP-007 (DT-1/3/4): one independent external attack - a wave front's sweep of one
        /// frame, one web collapse, one well pull tick - is ONE damage transaction, and every
        /// target it reaches carries the same transaction id. Open the scope around the
        /// target loop; <see cref="ApplyExternalHit"/> calls inside it JOIN the open
        /// transaction instead of opening their own, so no per-target or nested
        /// transaction exists and <see cref="DamageTransaction.NestedBegins"/> stays 0.
        ///
        /// The scope owns the transaction only if none was open when it began; a scope
        /// opened inside another transaction is a no-op that joins it. <c>Dispose</c> ends
        /// only what it owns, always (use it in a <c>using</c>).
        /// </summary>
        public readonly struct ExternalAttackScope : System.IDisposable
        {
            private readonly bool _owner;

            internal ExternalAttackScope(bool owner)
            {
                _owner = owner;
            }

            /// <summary>Transaction id of this attack (the open one when the scope did not own it).</summary>
            public int TransactionId => DamageTransaction.Current;

            public void Dispose()
            {
                if (_owner)
                {
                    DamageTransaction.End();
                }
            }
        }

        /// <summary>Begin one independent external attack (see <see cref="ExternalAttackScope"/>).</summary>
        public static ExternalAttackScope BeginExternalAttack()
        {
            if (DamageTransaction.Current != 0)
            {
                return new ExternalAttackScope(false);
            }

            DamageTransaction.Begin();
            return new ExternalAttackScope(true);
        }

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
            WeaponPower power,
            int presentationGroupId = 0)
        {
            if (!target.IsValid)
            {
                return default;
            }

            Vector3 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

            // WP-007: inside an ExternalAttackScope this JOINS the attack's transaction
            // (one id for every target of the attack). Outside one - a single-target
            // caller with no scope - it still gets a transaction of its own, so the
            // receiver-side duplicate guard is never bypassed.
            bool owner = DamageTransaction.Current == 0;
            if (owner)
            {
                DamageTransaction.Begin();
            }

            try
            {
                return ApplyHit(
                    target, damage, BodyRegion.UpperBody, point, safeDirection,
                    impactForce, source, power, -safeDirection,
                    presentationGroupId != 0 ? presentationGroupId :
                        (DamageTransaction.Current != 0 ? DamageTransaction.Current : FeelEventRouter.NextGroupId()));
            }
            finally
            {
                if (owner)
                {
                    DamageTransaction.End();
                }
            }
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
            DamageTransaction.Begin();
            try
            {
                var result = new ShotResult();
                s_touched.Clear();
                Explode(origin, totalDamage, radius, ref result);
            }
            finally
            {
                DamageTransaction.End();
            }
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
            Vector3 normal,
            int presentationGroupId = 0)
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
                UngovernedImpactForce = impactForce,   // WP-076 review #10: the grab escape reads the weight before the governor
                Source = source
            };

            DamageResult result = target.Receiver.ApplyHit(hit);

            if (DamageResults.IsEmpty(in result))
            {
                // Dead receiver (late damage on a corpse) or a refused second application:
                // no feedback, no counters. The physical guard (hitboxes off at death) makes
                // this rare; this is the logical guarantee behind it.
                return result;
            }

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
                FromExplosion = false,
                PresentationGroupId = presentationGroupId != 0 ? presentationGroupId : FeelEventRouter.NextGroupId(),
                SnapshotRevision = FeelRuntime.Current.Revision
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
            s_overlapSaturationCount = 0;
            s_touched.Clear();
            s_maskResolved = false;
            s_enemyQueryMask = 0;
        }
    }
}
