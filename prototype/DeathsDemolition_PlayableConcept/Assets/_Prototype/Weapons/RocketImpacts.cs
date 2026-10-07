using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DD.Prototype.Weapons
{
    /// <summary>WP-069 B3: where a rocket's blast came from.</summary>
    public enum RocketBlastKind
    {
        /// <summary>The rocket met a body or the world past the arming distance.</summary>
        Impact = 0,

        /// <summary>70 m or 2.2 s without a contact: it bursts in the air (DIRECTION §5).</summary>
        AirBurst = 1,

        /// <summary>One Cluster bomblet (WP-069 §6 Cluster branch).</summary>
        Bomblet = 2
    }

    /// <summary>WP-069 B3 · one blast for the VFX / audio / camera consumers (S-07 / S-08). Readonly, no allocation.</summary>
    public readonly struct RocketBlastInfo
    {
        public readonly ClassicWeaponId WeaponId;
        public readonly RocketBlastKind Kind;
        public readonly Vector3 Origin;
        public readonly Vector3 Normal;
        public readonly float Damage;
        public readonly float InnerRadius;
        public readonly float OuterRadius;
        public readonly int DirectTargetId;
        public readonly int EnemiesHit;
        public readonly float SelfDamage;
        public readonly int TransactionId;

        public RocketBlastInfo(ClassicWeaponId weaponId, RocketBlastKind kind, Vector3 origin, Vector3 normal, float damage, float innerRadius, float outerRadius,
            int directTargetId, int enemiesHit, float selfDamage, int transactionId)
        {
            WeaponId = weaponId; Kind = kind; Origin = origin; Normal = normal; Damage = damage; InnerRadius = innerRadius; OuterRadius = outerRadius;
            DirectTargetId = directTargetId; EnemiesHit = enemiesHit; SelfDamage = selfDamage; TransactionId = transactionId;
        }
    }

    /// <summary>WP-069 B3 · a rocket that met something inside the arming distance: no blast, it drops and fizzles.</summary>
    public readonly struct RocketDudInfo
    {
        public readonly ClassicWeaponId WeaponId;
        public readonly Vector3 Position;
        public readonly Vector3 Direction;
        public readonly float FizzleSeconds;
        public readonly int TargetId;

        public RocketDudInfo(ClassicWeaponId weaponId, Vector3 position, Vector3 direction, float fizzleSeconds, int targetId)
        {
            WeaponId = weaponId; Position = position; Direction = direction; FizzleSeconds = fizzleSeconds; TargetId = targetId;
        }
    }

    /// <summary>
    /// WP-069 B3 · rl_1 Bohiya's rocket (DIRECTION §5, DEC-097 / DEC-099).
    ///
    /// FLIGHT (<see cref="Advance"/>, from ProjectileSystem's fixed step): launch 24 m/s, +60 m/s² to 46 m/s during the
    /// 0.6 s boost, gravity 3.0 while boosting and 6.0 after, a random walk of 0.3° every 0.1 s (a visual yaw; 30 m ≤ 0.5 m).
    ///
    /// CONTACT (<see cref="OnContact"/>): the struck body already took the DIRECT hit (110, its region's multipliers) in the
    /// rocket's own transaction; the BLAST (160, full inside 1.8 m, linear to zero at 5.0 m) runs in a NEW transaction,
    /// so that body takes its own blast too (DEC-099 D1: armour counts — the region shares of the damage system decide,
    /// through <see cref="IExplosionReceiver"/>). Inside the 4.0 m arming distance there is no blast: a body takes the
    /// dud's 30, the rocket drops and fizzles after 1.0 s (<see cref="Dud"/>). 70 m / 2.2 s without a contact: an air
    /// burst (<see cref="OnSelfDetonate"/>).
    ///
    /// SELF DAMAGE: 45 % of the blast at the player's distance, the same falloff, capped at 72, no armour.
    /// BRANCHES (§6): Explosion reads "Blast" on rl_1 — per tier inner +0.3 m, outer +0.5 m, damage +12 %; Cluster drops
    /// 2 / 3 / 4 bomblets (40 each, r 1.5 m, the 45 % rule too) around the blast.
    /// </summary>
    public static class RocketImpacts
    {
        [PrototypeOnly(PendingDecisions.None, "WP-069 §6 Blast semantics per Explosion tier on rl_1: inner +0.3 m, outer +0.5 m, damage +12 %.", true)]
        public const float BlastInnerPerTier = 0.3f, BlastOuterPerTier = 0.5f, BlastDamagePerTier = 0.12f;

        [PrototypeOnly(PendingDecisions.None, "WP-069 §6 Cluster: 40 damage, r 1.5 m (full inside 0.3 m), ring 2.2 m around the blast.", true)]
        public const float BombletDamage = 40f, BombletRadius = 1.5f, BombletInner = 0.3f, BombletRing = 2.2f;

        /// <summary>Centre height of the player the self-damage measures to (m above the root).</summary>
        public const float PlayerCentreHeight = 1f;

        public static event Action<RocketBlastInfo> Detonated;
        public static event Action<RocketDudInfo> Dud;

        public static int Detonations { get; private set; }
        public static int AirBursts { get; private set; }
        public static int Duds { get; private set; }
        public static int Bomblets { get; private set; }
        public static int SelfHits { get; private set; }
        public static float LastSelfDamage { get; private set; }
        public static RocketBlastInfo LastBlast { get; private set; }

        /// <summary>Test seam: the player the self-damage measures against (null = the active weapon controller's player).</summary>
        public static Transform PlayerOverride { get; set; }

        private static readonly List<DamageTarget> s_targets = new List<DamageTarget>(64);

        // ------------------------------------------------------------------ flight

        /// <summary>The velocity at the end of a fixed step (boost, gravity per phase, wobble).</summary>
        internal static Vector3 Advance(ref ProjectileState p, Vector3 v0, float dt)
        {
            Vector3 v = v0;
            bool boosting = p.Age < p.RocketBoostSeconds;
            float speed = v.magnitude;
            if (boosting && p.RocketAcceleration > 0f && speed > 1e-5f && speed < p.RocketMaxSpeed)
            {
                v *= Mathf.Min(p.RocketMaxSpeed, speed + p.RocketAcceleration * dt) / speed;
            }

            v.y -= (boosting ? p.GravityBoost : p.GravityCoast) * dt;

            if (p.WobbleDegrees > 0f && p.WobbleStepSeconds > 0f)
            {
                p.WobbleTimer += dt;
                while (p.WobbleTimer >= p.WobbleStepSeconds)
                {
                    p.WobbleTimer -= p.WobbleStepSeconds;
                    Vector3 axis = Vector3.Cross(v, Random.onUnitSphere);
                    if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(v, Vector3.up);
                    if (axis.sqrMagnitude < 1e-8f) break;
                    v = Quaternion.AngleAxis(p.WobbleDegrees * (Random.value < 0.5f ? -1f : 1f), axis.normalized) * v;
                }
            }

            return v;
        }

        // ------------------------------------------------------------------ contact (HitscanEngine / ProjectileSystem)

        /// <summary>Record where the rocket stopped and whether it had flown its arming distance by then.</summary>
        internal static void MarkContact(ref ProjectileState p, Vector3 point, Vector3 normal, int targetId, float at)
        {
            WeaponDefinition def = Arsenal.Get(p.WeaponId);
            float arming = def != null && def.explosive != null ? Mathf.Max(0f, def.explosive.armingDistance) : 0f;
            p.ContactPoint = point;
            p.ContactNormal = normal;
            p.ContactTargetId = targetId;
            p.ContactArmed = p.Travelled + at >= arming;
        }

        internal static float DudDamageOf(ClassicWeaponId id)
        {
            WeaponDefinition def = Arsenal.Get(id);
            return def != null && def.explosive != null ? Mathf.Max(0f, def.explosive.dudDamage) : 0f;
        }

        /// <summary>The rocket stopped on a body or the world (its direct hit, if any, is done and its transaction closed).</summary>
        internal static void OnContact(ref ProjectileState p)
        {
            WeaponDefinition def = Arsenal.Get(p.WeaponId);
            if (def == null || def.explosive == null || !def.explosive.enabled) return;
            if (!p.ContactArmed)
            {
                Duds++;
                Dud?.Invoke(new RocketDudInfo(p.WeaponId, p.ContactPoint, p.Direction, def.explosive.dudFizzleSeconds, p.ContactTargetId));
                return;
            }

            Detonate(def, p.WeaponId, p.ContactPoint, p.ContactNormal, RocketBlastKind.Impact, p.ContactTargetId, p.BlastTier, p.ClusterBomblets,
                p.Shot.PresentationGroupId);
        }

        /// <summary>Range or lifetime ran out in the air: burst there (a rocket that never armed just ends).</summary>
        internal static void OnSelfDetonate(ref ProjectileState p)
        {
            WeaponDefinition def = Arsenal.Get(p.WeaponId);
            if (def == null || def.explosive == null || !def.explosive.enabled) return;
            if (p.Travelled < def.explosive.armingDistance)
            {
                Duds++;
                Dud?.Invoke(new RocketDudInfo(p.WeaponId, p.Position, p.Direction, def.explosive.dudFizzleSeconds, 0));
                return;
            }

            Detonate(def, p.WeaponId, p.Position, Vector3.up, RocketBlastKind.AirBurst, 0, p.BlastTier, p.ClusterBomblets,
                p.Shot.PresentationGroupId);
        }

        // ------------------------------------------------------------------ blast

        /// <summary>Linear (exponent 1) falloff: 1 inside <paramref name="inner"/>, 0 at <paramref name="outer"/> and beyond.</summary>
        public static float FalloffShare(float distance, float inner, float outer, float exponent = 1f)
        {
            if (distance <= inner) return 1f;
            if (distance >= outer || outer <= inner) return 0f;
            float t = 1f - (distance - inner) / (outer - inner);
            return exponent == 1f ? t : Mathf.Pow(t, Mathf.Max(0.01f, exponent));
        }

        /// <summary>The player's share of a blast: selfShare of the falloff damage, capped (no armour).</summary>
        public static float SelfDamageAt(float distance, float blastDamage, float inner, float outer, float selfShare, float selfMax, float exponent = 1f)
        {
            float amount = blastDamage * FalloffShare(distance, inner, outer, exponent) * Mathf.Clamp01(selfShare);
            return selfMax > 0f ? Mathf.Min(selfMax, amount) : amount;
        }

        private static void Detonate(WeaponDefinition def, ClassicWeaponId weapon, Vector3 origin, Vector3 normal, RocketBlastKind kind, int directTargetId,
            int blastTier, int bomblets, int shotGroup)
        {
            // WP-095 (gt SHOULD-1): FeelEvents sees the blast's kills and the blast itself in the trigger pull's group, like its direct
            // hit — the router keeps its own blast group (the one-shake-per-group rule is unchanged)
            int previousFeelGroup = FeelEvents.PushHitGroup(shotGroup != 0 ? shotGroup : FeelEvents.CurrentHitGroup);
            try
            {
                DetonateInGroup(def, weapon, origin, normal, kind, directTargetId, blastTier, bomblets);
            }
            finally
            {
                FeelEvents.PopHitGroup(previousFeelGroup);
            }
        }

        private static void DetonateInGroup(WeaponDefinition def, ClassicWeaponId weapon, Vector3 origin, Vector3 normal, RocketBlastKind kind,
            int directTargetId, int blastTier, int bomblets)
        {
            WeaponExplosiveBlock e = def.explosive;
            int tier = Mathf.Max(0, blastTier);
            float damage = e.blastDamage * (1f + BlastDamagePerTier * tier);
            float inner = e.innerRadius + BlastInnerPerTier * tier;
            float outer = e.outerRadius + BlastOuterPerTier * tier;
            DD.Prototype.Rounds.RunTreeHooks.ShapeRocketBlast(weapon, ref damage, ref inner, ref outer);   // WP-077 fork.rl_1.x / fork.rl_1.y
            int group = FeelEventRouter.NextGroupId();

            int split = DD.Prototype.Rounds.RunTreeHooks.RocketSplitCount(weapon);   // WP-077 cap.rl_1 Ash Rain
            int hits;
            int transaction = split > 1
                ? SplitBlast(weapon, origin, damage, inner, outer, e.falloffExponent, e.impulse, group, split, out hits)
                : Blast(weapon, origin, damage, inner, outer, e.falloffExponent, e.impulse, group, ExplosionSource.Rocket, out hits);
            float self = HurtPlayer(weapon, origin, damage, inner, outer, e);   // the unsplit blast's share (one hit on the player)
            DD.Prototype.Rounds.RunTreeHooks.OnRocketBlast(weapon, origin, damage, outer);   // WP-077 ks.explosion.ashcloud / seal.rl_1 burning ground

            Detonations++;
            if (kind == RocketBlastKind.AirBurst) AirBursts++;
            var info = new RocketBlastInfo(weapon, kind, origin, normal, damage, inner, outer, directTargetId, hits, self, transaction);
            LastBlast = info;
            Detonated?.Invoke(info);

            for (int i = 0; i < bomblets; i++)
            {
                float angle = (i * 360f / bomblets) + Random.value * 30f;
                Vector3 at = origin + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * BombletRing;
                int bt = Blast(weapon, at, BombletDamage, BombletInner, BombletRadius, 1f, e.impulse * 0.5f, group, ExplosionSource.Small, out int bh);
                float bs = HurtPlayer(weapon, at, BombletDamage, BombletInner, BombletRadius, e);
                Bomblets++;
                var b = new RocketBlastInfo(weapon, RocketBlastKind.Bomblet, at, normal, BombletDamage, BombletInner, BombletRadius, 0, bh, bs, bt);
                Detonated?.Invoke(b);
            }
        }

        /// <summary>
        /// WP-077 cap.rl_1 (Ash Rain): the blast as <paramref name="count"/> smaller ones on a ring around the impact
        /// (RunTreeHooks.RocketSplit*: radius × 0.6, damage × RocketSplitDamageScale — 1.0 since DEC-106, ring 0.45 × outer), each its own transaction — a body
        /// between two takes both. Returns the first one's transaction; <paramref name="hits"/> sums them.
        /// </summary>
        private static int SplitBlast(ClassicWeaponId weapon, Vector3 origin, float damage, float inner, float outer, float exponent, float force, int group,
            int count, out int hits)
        {
            hits = 0;
            int first = 0;
            float scale = DD.Prototype.Rounds.RunTreeHooks.RocketSplitRadiusScale;
            float ring = outer * DD.Prototype.Rounds.RunTreeHooks.RocketSplitRingScale;
            float phase = Random.value * 360f;
            for (int i = 0; i < count; i++)
            {
                Vector3 at = origin + Quaternion.Euler(0f, phase + i * 360f / count, 0f) * Vector3.forward * ring;
                int t = Blast(weapon, at, damage * DD.Prototype.Rounds.RunTreeHooks.RocketSplitDamageScale, inner * scale, outer * scale, exponent,
                    force * scale, group, i == 0 ? ExplosionSource.Rocket : ExplosionSource.Small, out int h);   // M-62: one rocket-grade blast per split
                hits += h;
                if (i == 0) first = t;
            }

            return first;
        }

        /// <summary>One blast in its own damage transaction; every enemy inside <paramref name="outer"/> once, through the damage system's region split.</summary>
        private static int Blast(ClassicWeaponId weapon, Vector3 origin, float damage, float inner, float outer, float exponent, float force, int group, ExplosionSource source, out int hits)
        {
            hits = 0;
            HitFeedbackDirector.Instance.ReportExplosion(origin, outer, group, source);
            int transaction = DamageTransaction.NextId();
            DamageTransaction.Resume(transaction);
            try
            {
                HitscanEngine.OverlapEnemies(origin, outer, s_targets);
                for (int i = 0; i < s_targets.Count; i++)
                {
                    DamageTarget target = s_targets[i];
                    if (target.Explosive == null || target.Root == null) continue;
                    Vector3 point = HitscanEngine.CentreOf(target);
                    float share = FalloffShare(Vector3.Distance(origin, point), inner, outer, exponent);
                    if (share <= 0f) continue;

                    DamageHitContext.Set(1f, 1f, true, weapon, null);
                    // WP-095: the blast's kills are one group — the trigger pull's when Detonate pushed it
                    int previousGroup = FeelEvents.PushHitGroup(FeelEvents.CurrentHitGroup != 0 ? FeelEvents.CurrentHitGroup : group);
                    DamageResult r;
                    try
                    {
                        r = target.Explosive.ApplyExplosion(damage * share, origin);
                    }
                    finally
                    {
                        FeelEvents.PopHitGroup(previousGroup);
                        DamageHitContext.Clear();
                    }

                    if (DamageResults.IsEmpty(in r)) continue;
                    hits++;
                    Vector3 away = point - origin;
                    Vector3 dir = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.up;
                    HitFeedbackDirector.Instance.ReportEnemyHit(new EnemyHitFeedbackInfo
                    {
                        Point = point, Direction = dir, Normal = dir, Result = r, Power = WeaponPower.Heavy, TargetId = target.Id,
                        TargetRoot = target.Root, Weight = target.Weight, ImpactForce = force * share, FromExplosion = true,
                        PresentationGroupId = group, SnapshotRevision = FeelRuntime.Current.Revision
                    });
                }
            }
            finally
            {
                s_targets.Clear();
                DamageTransaction.End();
            }

            return transaction;
        }

        private static float HurtPlayer(ClassicWeaponId weapon, Vector3 origin, float damage, float inner, float outer, WeaponExplosiveBlock e)
        {
            Transform player = PlayerOverride;
            WeaponController controller = WeaponRegistry.ActiveController;
            if (player == null && controller != null) player = controller.transform.root;
            if (player == null || e.selfDamageShare <= 0f) return 0f;

            float distance = Vector3.Distance(origin, player.position + Vector3.up * PlayerCentreHeight);
            float amount = SelfDamageAt(distance, damage, inner, outer, e.selfDamageShare, e.selfDamageMax, e.falloffExponent);
            if (amount <= 0.01f) return 0f;

            PlayerHealth health = player.GetComponentInChildren<PlayerHealth>();
            if (health != null) health.ApplyDamage(amount, origin, Arsenal.Get(weapon)?.weaponId ?? weapon.ToString());
            SelfHits++;
            LastSelfDamage = amount;
            return amount;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Detonations = 0;
            AirBursts = 0;
            Duds = 0;
            Bomblets = 0;
            SelfHits = 0;
            LastSelfDamage = 0f;
            LastBlast = default;
            PlayerOverride = null;
            Detonated = null;
            Dud = null;
            s_targets.Clear();
        }
    }
}
