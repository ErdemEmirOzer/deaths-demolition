using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using DamageTarget = DD.Prototype.Weapons.DamageTarget;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-077 K10b stage 2 · the run weapon's seal / fork / capstone effects that live on the HIT path (armour bypass,
    /// pierce, chains, shoves, splits, burning ground). Every public function is a read of <see cref="V"/> and changes
    /// nothing unless the asking weapon IS the run weapon (<see cref="RunTreeEffectValues.WeaponSlot"/>) and the node is
    /// owned. The engine calls in with one line each: <see cref="OnProjectileSpawned"/> (ProjectileSystem.Spawn tags the
    /// round), <see cref="BeforeTreeHit"/> / <see cref="AfterTreeHit"/> (HitscanEngine.ResolveProjectileSegment),
    /// <see cref="ShapeRocketBlast"/> / <see cref="RocketSplitCount"/> / <see cref="OnRocketBlast"/> (RocketImpacts),
    /// <see cref="OnClassicExplosion"/> (HitscanEngine.Explode), <see cref="TreeStaggerSeconds"/> /
    /// <see cref="AfterTreeStagger"/> (ZombieEnemy). Fixed buffers only — nothing allocates per shot or per hit.
    ///
    /// Numbers the cards do not state (design choices, PROTOTYPE-ONLY): the consts below.
    /// </summary>
    public static partial class RunTreeHooks
    {
        // ------------------------------------------------------------------ numbers not on the cards

        /// <summary>
        /// cap.rl_1 Ash Rain: each of the count split blasts is the (forked) blast's radius × 0.6 (inner and outer) and
        /// damage × RocketSplitDamageScale, centred on a ring 0.45 × the outer radius around the impact (random phase).
        /// DEC-106 (user, 2026-09-30, via C-00; K10b P2 A): full damage (× 1.0) — at × 0.5 the three small blasts dealt
        /// ≈ 0.54× the unsplit blast's damage × area (−40 % crowd, B5 audit), so the capstone weakened the launcher.
        /// </summary>
        public const float RocketSplitRadiusScale = 0.6f, RocketSplitDamageScale = 1.0f, RocketSplitRingScale = 0.45f;

        /// <summary>cap.sg_1 Ash Wall: the point-blank shove, metres over seconds (a shotgun's own knockback is 0.6 m / 0.25 s).</summary>
        public const float AshWallShoveMetres = 2.0f, AshWallShoveSeconds = 0.35f;

        /// <summary>cap.sg_2 Two Swords: the second barrel's blast = this many pellets' damage (11 × 5 = 55), radius m.</summary>
        public const float TwinBarrelBlastPellets = 5f, TwinBarrelBlastRadius = 2.0f;

        /// <summary>seal.smg_2 Tsuchi: a Runner (Special tier) staggered by smg_2 falls for this long (the 0.4 s fall included).</summary>
        public const float RunnerKnockdownSeconds = 1.2f;

        /// <summary>
        /// cap.ar_2 Third Eye: ar_2 rounds fired closer together than this belong to one burst (burst interval 0.05 s,
        /// burst delay 0.26 s ÷ fire rate). The runtime does not publish the shot-in-burst index, so the round is counted here.
        /// </summary>
        public const float BurstGapSeconds = 0.15f;

        /// <summary>cap.ar_2: the round of the burst that chains on a head hit.</summary>
        public const int ThirdEyeRound = 3;

        /// <summary>cap.ar_1 / cap.sr_1 "pierces without limit": the round's target budget (its touched table's size).</summary>
        public const int UnlimitedPierceTargets = ProjectileState.TouchedCapacity;

        // ------------------------------------------------------------------ round tags (ProjectileState.TreeFlags)

        /// <summary>Every body this round hits ignores armour (cap.ar_1 marked round).</summary>
        public const int TreeRoundIgnoresArmor = 1;
        /// <summary>The first body this round hits ignores armour (cap.sr_1).</summary>
        public const int TreeRoundFirstBodyIgnoresArmor = 2;
        /// <summary>The round pierces without limit (cap.ar_1 marked round, cap.sr_1).</summary>
        public const int TreeRoundUnlimitedPierce = 4;

        // ------------------------------------------------------------------ AfterTreeHit result bits

        /// <summary>Chain from this hit (cap.ar_2 / cap.smg_2) — the engine runs one chain after the segment.</summary>
        public const int TreeHitChain = 1;
        /// <summary>No pierce loss at this body (cap.lmg_2 broke a plate).</summary>
        public const int TreeHitKeepDamage = 2;

        // ------------------------------------------------------------------ state (reset per run)

        private const int ShotMemory = 32;
        private const int DeferredBlastCapacity = 16;

        private static int s_roninRounds;
        private static int s_burstOrdinal;
        private static float s_lastBurstShot = float.NegativeInfinity;
        private static int s_fangHits;

        private static int s_ashWallGroup;
        private static readonly int[] s_ashWallIds = new int[ShotMemory];
        private static int s_ashWallCount;

        private static int s_twinGroup, s_twinBase, s_twinPair;
        private static readonly int[] s_twinFirst = new int[ShotMemory];
        private static int s_twinFirstCount;
        private static readonly int[] s_twinBlown = new int[ShotMemory];
        private static int s_twinBlownCount;

        private static int s_treeHitFlags;
        private static bool s_ashWallArmed;

        private static readonly Vector3[] s_blastAt = new Vector3[DeferredBlastCapacity];
        private static readonly float[] s_blastDamage = new float[DeferredBlastCapacity];
        private static readonly float[] s_blastRadius = new float[DeferredBlastCapacity];
        private static int s_blastCount;

        /// <summary>Blasts waiting for <see cref="FlushTreeBlasts"/> (tests / F1).</summary>
        public static int TreeBlastsQueued => s_blastCount;

        private static bool RunWeapon(RunTreeEffectValues v, ClassicWeaponId weapon, ClassicWeaponId id, string slot) =>
            weapon == id && string.Equals(v.WeaponSlot, slot, StringComparison.Ordinal);

        // ------------------------------------------------------------------ spawn (ProjectileSystem.Spawn)

        /// <summary>Tags a round at spawn: cap.ar_1 every count-th round, cap.sr_1 every round, cap.ar_2 its burst round, cap.sg_2 its barrel.</summary>
        public static void OnProjectileSpawned(ref ProjectileState p)
        {
            RunTreeEffectValues v = V;
            if (!v.Capstone) return;   // every spawn tag belongs to a capstone

            switch (p.WeaponId)
            {
                case ClassicWeaponId.AssaultRifle:
                    if (!RunWeapon(v, p.WeaponId, ClassicWeaponId.AssaultRifle, "ar_1") || !RoninMarkRound(p.WeaponId, ++s_roninRounds)) return;
                    p.TreeFlags |= TreeRoundIgnoresArmor | TreeRoundUnlimitedPierce;
                    p.TargetsLeft = Math.Max(p.TargetsLeft, UnlimitedPierceTargets);
                    return;

                case ClassicWeaponId.Sr1:
                    if (!RunWeapon(v, p.WeaponId, ClassicWeaponId.Sr1, "sr_1")) return;
                    p.TreeFlags |= TreeRoundFirstBodyIgnoresArmor | TreeRoundUnlimitedPierce;
                    p.TargetsLeft = Math.Max(p.TargetsLeft, UnlimitedPierceTargets);
                    return;

                case ClassicWeaponId.Ar2:
                    if (RunWeapon(v, p.WeaponId, ClassicWeaponId.Ar2, "ar_2")) p.TreeOrdinal = NextBurstOrdinal(Time.time);
                    return;

                case ClassicWeaponId.Sg2:
                    if (!RunWeapon(v, p.WeaponId, ClassicWeaponId.Sg2, "sg_2")) return;
                    p.TreeOrdinal = TwinBarrelOf(p.Shot.PresentationGroupId, p.PelletIndex, TwinBarrelPellets(), Sg2FiredRounds());
                    p.TreeVolley = s_twinPair;
                    return;
            }
        }

        /// <summary>cap.ar_1 Ronin's Mark: round <paramref name="round"/> (1-based, per run) of ar_1 is a marked round.</summary>
        public static bool RoninMarkRound(ClassicWeaponId weapon, int round)
        {
            RunTreeEffectValues v = V;
            if (!v.Capstone || round <= 0 || !RunWeapon(v, weapon, ClassicWeaponId.AssaultRifle, "ar_1")) return false;
            return round % Math.Max(1, Mathf.RoundToInt(v.CapstoneArg("count", 5f))) == 0;
        }

        /// <summary>cap.ar_2: the burst round of an ar_2 shot fired at <paramref name="now"/> (1 = a new burst).</summary>
        public static int NextBurstOrdinal(float now)
        {
            s_burstOrdinal = now - s_lastBurstShot <= BurstGapSeconds ? s_burstOrdinal + 1 : 1;
            s_lastBurstShot = now;
            return s_burstOrdinal;
        }

        /// <summary>
        /// cap.sg_2: the barrel (0 left, 1 right) of pellet <paramref name="pelletIndex"/> of trigger pull
        /// <paramref name="group"/>. A pull's base barrel is the magazine parity (<paramref name="firedRounds"/> = rounds
        /// fired since full: 0 → left, 1 → right); in a both-barrels pull (seal.sg_2, 20 pellets) the pellets past one
        /// barrel's count are the right barrel. A pull starting on the left barrel opens a new pair.
        /// </summary>
        public static int TwinBarrelOf(int group, int pelletIndex, int barrelPellets, int firedRounds)
        {
            if (group != s_twinGroup)
            {
                s_twinGroup = group;
                s_twinBase = Math.Max(0, firedRounds) & 1;
                if (s_twinBase == 0)
                {
                    s_twinPair++;
                    s_twinFirstCount = 0;
                    s_twinBlownCount = 0;
                }
            }

            return Math.Min(1, s_twinBase + Math.Max(0, pelletIndex) / Math.Max(1, barrelPellets));
        }

        /// <summary>cap.sg_2: the pair the latest sg_2 pull belongs to.</summary>
        public static int TwinBarrelPair => s_twinPair;

        private static int TwinBarrelPellets()
        {
            DD.Prototype.Core.Weapons.WeaponDefinition d = DD.Prototype.Core.Weapons.Arsenal.Get(ClassicWeaponId.Sg2);
            return d != null ? Math.Max(1, d.fire.pelletCount) : 10;
        }

        private static int Sg2FiredRounds()
        {
            WeaponController c = WeaponRegistry.ActiveController;
            ClassicWeaponRuntime w = c != null ? c.Classic : null;
            return w != null && !w.InfiniteAmmo && w.Id == ClassicWeaponId.Sg2 ? Math.Max(0, w.MagazineSize - w.Ammo) : 0;
        }

        // ------------------------------------------------------------------ hit (HitscanEngine.ResolveProjectileSegment)

        /// <summary>
        /// Before a projectile's body hit is applied (DamageHitContext already Set): marks the armour bypass (seal.sr_1 head,
        /// cap.sr_1 first body, cap.ar_1 marked round) and the plate break (cap.sg_1), counts cap.smg_2, and returns the
        /// damage factor (fork.sg_2.x close range).
        /// </summary>
        public static float BeforeTreeHit(ref ProjectileState p, in DamageTarget target, BodyRegion region, Vector3 point)
        {
            s_treeHitFlags = 0;
            s_ashWallArmed = false;
            RunTreeEffectValues v = V;
            if (!v.Seal && !v.ForkX && !v.Capstone && p.TreeFlags == 0) return 1f;

            ClassicWeaponId weapon = p.WeaponId;
            if (IgnoresArmor(weapon, region, p.TreeFlags, p.TouchedCount)) DamageHitContext.MarkIgnoreArmor();

            float scale = 1f;
            if (v.ForkX || v.Capstone)
            {
                float distance = Vector3.Distance(p.Origin, point);
                scale = CloseRangeScale(weapon, distance);
                if (AshWallApplies(weapon, distance) && AshWallFirstThisShot(p.Shot.PresentationGroupId, target.Id))
                {
                    s_ashWallArmed = true;
                    if (v.CapstoneArg("count", 1f) >= 1f) DamageHitContext.MarkBreakPlate();
                }
            }

            if (v.Capstone && weapon == ClassicWeaponId.Smg2 && LightningFangChains(weapon, IsStaggering(in target)))
            {
                s_treeHitFlags |= TreeHitChain;
            }

            return scale;
        }

        /// <summary>
        /// After the hit: cap.ar_2 head chain, cap.sg_1 shove, cap.lmg_2 pass-through (one more target, no loss), cap.sg_2
        /// second-barrel blast (deferred to <see cref="FlushTreeBlasts"/>). Returns <see cref="TreeHitChain"/> /
        /// <see cref="TreeHitKeepDamage"/> bits for the engine.
        /// </summary>
        public static int AfterTreeHit(ref ProjectileState p, in DamageTarget target, BodyRegion region, Vector3 point, Vector3 direction, in DamageResult result)
        {
            int flags = s_treeHitFlags;
            bool shove = s_ashWallArmed;
            s_treeHitFlags = 0;
            s_ashWallArmed = false;
            if (DamageResults.IsEmpty(in result)) return 0;

            RunTreeEffectValues v = V;
            if (!v.Capstone) return flags;   // everything below is a capstone

            ClassicWeaponId weapon = p.WeaponId;
            if (ThirdEyeChains(weapon, p.TreeOrdinal, region)) flags |= TreeHitChain;

            if (shove && !result.Killed && target.Receiver is ZombieEnemy body) body.ApplyTreeShove(direction, AshWallShoveMetres, AshWallShoveSeconds);

            if (BlackIronPasses(weapon, result.ArmorBroke))
            {
                p.TargetsLeft++;
                flags |= TreeHitKeepDamage;
            }

            if (TwinBarrelHit(weapon, p.TreeOrdinal, p.TreeVolley, target.Id))
            {
                DeferBlast(point, p.Shot.Damage * TwinBarrelBlastPellets, TwinBarrelBlastRadius);
            }

            return flags;
        }

        /// <summary>seal.sr_1 head hits, cap.sr_1 the first body, cap.ar_1 a marked round: the hit passes regional armour.</summary>
        public static bool IgnoresArmor(ClassicWeaponId weapon, BodyRegion region, int roundFlags, int bodiesTouched)
        {
            if ((roundFlags & TreeRoundIgnoresArmor) != 0) return true;
            if ((roundFlags & TreeRoundFirstBodyIgnoresArmor) != 0 && bodiesTouched <= 1) return true;
            RunTreeEffectValues v = V;
            return v.Seal && region == BodyRegion.Head && RunWeapon(v, weapon, ClassicWeaponId.Sr1, "sr_1");
        }

        /// <summary>fork.sg_2.x Fire Mouth: sg_2 damage + value % within range m of the muzzle.</summary>
        public static float CloseRangeScale(ClassicWeaponId weapon, float distance)
        {
            RunTreeEffectValues v = V;
            if (!v.ForkX || !RunWeapon(v, weapon, ClassicWeaponId.Sg2, "sg_2")) return 1f;
            return distance <= v.ForkXArg("range", 4f) ? 1f + v.ForkXArg("value", 20f) / 100f : 1f;
        }

        /// <summary>cap.sg_1 Ash Wall: an sg_1 pellet within range m pushes back and breaks the struck plate.</summary>
        public static bool AshWallApplies(ClassicWeaponId weapon, float distance)
        {
            RunTreeEffectValues v = V;
            return v.Capstone && RunWeapon(v, weapon, ClassicWeaponId.CombatShotgun, "sg_1") && distance <= v.CapstoneArg("range", 3f);
        }

        /// <summary>cap.sg_1: true the first time trigger pull <paramref name="group"/> reaches body <paramref name="targetId"/> (once per enemy per shot).</summary>
        public static bool AshWallFirstThisShot(int group, int targetId)
        {
            if (group != s_ashWallGroup)
            {
                s_ashWallGroup = group;
                s_ashWallCount = 0;
            }

            if (HasId(s_ashWallIds, s_ashWallCount, targetId)) return false;
            AddId(s_ashWallIds, ref s_ashWallCount, targetId);
            return true;
        }

        /// <summary>cap.lmg_2 Black Iron: an lmg_2 round that broke a plate passes to the body behind at full damage.</summary>
        public static bool BlackIronPasses(ClassicWeaponId weapon, bool armorBroke)
        {
            if (!armorBroke) return false;
            RunTreeEffectValues v = V;
            return v.Capstone && RunWeapon(v, weapon, ClassicWeaponId.Lmg2, "lmg_2");
        }

        /// <summary>cap.smg_2 Lightning Fang: counts an smg_2 hit on a staggering body; every count-th one chains for sure.</summary>
        public static bool LightningFangChains(ClassicWeaponId weapon, bool targetStaggering)
        {
            RunTreeEffectValues v = V;
            if (!targetStaggering || !v.Capstone || !RunWeapon(v, weapon, ClassicWeaponId.Smg2, "smg_2")) return false;
            if (++s_fangHits < Math.Max(1, Mathf.RoundToInt(v.CapstoneArg("count", 3f)))) return false;
            s_fangHits = 0;
            return true;
        }

        /// <summary>cap.ar_2 Third Eye: the burst's 3rd round hitting a head ricochets into a chain.</summary>
        public static bool ThirdEyeChains(ClassicWeaponId weapon, int burstRound, BodyRegion region)
        {
            if (burstRound != ThirdEyeRound || region != BodyRegion.Head) return false;
            RunTreeEffectValues v = V;
            return v.Capstone && RunWeapon(v, weapon, ClassicWeaponId.Ar2, "ar_2");
        }

        /// <summary>
        /// cap.sg_2 Two Swords: one sg_2 pellet of <paramref name="barrel"/> in <paramref name="pair"/> landed on
        /// <paramref name="targetId"/>. Left-barrel hits are remembered; the first right-barrel hit on a body the left barrel
        /// hit in the same pair returns true (it explodes) — once per body per pair.
        /// </summary>
        public static bool TwinBarrelHit(ClassicWeaponId weapon, int barrel, int pair, int targetId)
        {
            RunTreeEffectValues v = V;
            if (!v.Capstone || pair != s_twinPair || !RunWeapon(v, weapon, ClassicWeaponId.Sg2, "sg_2")) return false;
            if (barrel <= 0)
            {
                AddId(s_twinFirst, ref s_twinFirstCount, targetId);
                return false;
            }

            if (!HasId(s_twinFirst, s_twinFirstCount, targetId) || HasId(s_twinBlown, s_twinBlownCount, targetId)) return false;
            AddId(s_twinBlown, ref s_twinBlownCount, targetId);
            return true;
        }

        /// <summary>A forced chain (cap.ar_2 / cap.smg_2) on a weapon without the Chain branch uses the branch's tier-1 numbers.</summary>
        public static void ForcedChainRequest(ref ShotRequest request)
        {
            WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
            if (request.ChainTargets <= 0) request.ChainTargets = t != null ? Math.Max(1, t.ChainTargetsPerTier) : 1;
            if (request.ChainRadius <= 0f) request.ChainRadius = t != null ? t.ChainRadius : 5f;
            if (request.ChainDamageScale <= 0f) request.ChainDamageScale = t != null ? t.ChainDamageScale : 0.5f;
            if (request.ChainDamageFalloff <= 0f) request.ChainDamageFalloff = t != null ? t.ChainDamageFalloff : 0.75f;
        }

        private static bool IsStaggering(in DamageTarget target)
        {
            return target.Receiver is ZombieEnemy body && body.Animator != null && body.Animator.IsStaggering;
        }

        // ------------------------------------------------------------------ stagger (ZombieEnemy.ProcessResult)

        /// <summary>The stagger length of the hit being applied (reads the ambient DamageHitContext weapon).</summary>
        public static float TreeStaggerSeconds(float seconds) =>
            DamageHitContext.FromClassicWeapon ? StaggerSecondsFor(DamageHitContext.Weapon, seconds) : seconds;

        /// <summary>seal.smg_2 Tsuchi: an smg_2 stagger × value / base (0.45 → 0.7 s); fork.smg_2.x Heavy Core: × (1 + value %).</summary>
        public static float StaggerSecondsFor(ClassicWeaponId weapon, float seconds)
        {
            RunTreeEffectValues v = V;
            if ((!v.Seal && !v.ForkX) || !RunWeapon(v, weapon, ClassicWeaponId.Smg2, "smg_2")) return seconds;
            if (v.Seal) seconds *= Mathf.Max(0.05f, v.SealArg("value", 0.7f)) / Mathf.Max(0.05f, v.SealArg("base", 0.45f));
            if (v.ForkX) seconds *= 1f + v.ForkXArg("value", 30f) / 100f;
            return seconds;
        }

        /// <summary>seal.smg_2 Tsuchi: a Runner (Special tier) staggered by smg_2 falls.</summary>
        public static bool RunnerFalls(ClassicWeaponId weapon, EnemyTier tier)
        {
            if (tier != EnemyTier.Special) return false;
            RunTreeEffectValues v = V;
            return v.Seal && RunWeapon(v, weapon, ClassicWeaponId.Smg2, "smg_2");
        }

        /// <summary>Right after a body began a stagger: seal.smg_2 knocks a Runner down (the gel Special's Falling → Downed → GettingUp).</summary>
        public static void AfterTreeStagger(ZombieEnemy body)
        {
            if (body == null || !DamageHitContext.FromClassicWeapon || !RunnerFalls(DamageHitContext.Weapon, body.Tier)) return;
            body.ApplyKnockdown(DamageTransaction.Current, RunnerKnockdownSeconds);
        }

        // ------------------------------------------------------------------ explosions (RocketImpacts / HitscanEngine.Explode)

        /// <summary>fork.rl_1.x Short Fuse: radius × radius %, damage + value %; fork.rl_1.y Long Fuse: radius + value %.</summary>
        public static void ShapeRocketBlast(ClassicWeaponId weapon, ref float damage, ref float inner, ref float outer)
        {
            RunTreeEffectValues v = V;
            if ((!v.ForkX && !v.ForkY) || !RunWeapon(v, weapon, ClassicWeaponId.Rl1, "rl_1")) return;
            float radius = 1f;
            if (v.ForkX)
            {
                radius *= Mathf.Max(0.1f, v.ForkXArg("radius", 75f) / 100f);
                damage *= 1f + v.ForkXArg("value", 30f) / 100f;
            }

            if (v.ForkY) radius *= 1f + v.ForkYArg("value", 25f) / 100f;
            inner *= radius;
            outer *= radius;
        }

        /// <summary>cap.rl_1 Ash Rain: the blast splits into this many (≤ 1 = no split).</summary>
        public static int RocketSplitCount(ClassicWeaponId weapon)
        {
            RunTreeEffectValues v = V;
            return v.Capstone && RunWeapon(v, weapon, ClassicWeaponId.Rl1, "rl_1") ? Math.Max(0, Mathf.RoundToInt(v.CapstoneArg("count", 3f))) : 0;
        }

        /// <summary>A rocket's main blast (not its bomblets): Ash Cloud and Hibana leave the blast area burning.</summary>
        public static void OnRocketBlast(ClassicWeaponId weapon, Vector3 origin, float damage, float outerRadius)
        {
            if (AshCloudFor(damage, out float dps, out float seconds)) RunTreeGroundFire.Ignite(origin, outerRadius, dps, seconds, true, weapon);
            if (HibanaFor(weapon, out dps, out seconds)) RunTreeGroundFire.Ignite(origin, outerRadius, dps, seconds, true, weapon);
        }

        /// <summary>Every classic explosion (the Explosion branch, a Two Swords blast): Ash Cloud leaves its area burning.</summary>
        public static void OnClassicExplosion(Vector3 origin, float totalDamage, float radius)
        {
            if (AshCloudFor(totalDamage, out float dps, out float seconds)) RunTreeGroundFire.Ignite(origin, radius, dps, seconds);
        }

        /// <summary>ks.explosion.ashcloud: the burning ground a blast of <paramref name="blastDamage"/> leaves (share × damage per second, seconds).</summary>
        public static bool AshCloudFor(float blastDamage, out float damagePerSecond, out float seconds)
        {
            RunTreeEffectValues v = V;
            damagePerSecond = blastDamage * v.AshCloudShare;
            seconds = v.AshCloudSeconds;
            return damagePerSecond > 0f && seconds > 0f;
        }

        /// <summary>seal.rl_1 Hibana: the rocket's blast area burns value HP/s for seconds.</summary>
        public static bool HibanaFor(ClassicWeaponId weapon, out float damagePerSecond, out float seconds)
        {
            RunTreeEffectValues v = V;
            bool owned = v.Seal && RunWeapon(v, weapon, ClassicWeaponId.Rl1, "rl_1");
            damagePerSecond = owned ? v.SealArg("value", 4f) : 0f;
            seconds = owned ? v.SealArg("seconds", 3f) : 0f;
            return owned && damagePerSecond > 0f && seconds > 0f;
        }

        private static void DeferBlast(Vector3 at, float damage, float radius)
        {
            if (s_blastCount >= DeferredBlastCapacity || damage <= 0f || radius <= 0f) return;
            s_blastAt[s_blastCount] = at;
            s_blastDamage[s_blastCount] = damage;
            s_blastRadius[s_blastCount] = radius;
            s_blastCount++;
        }

        /// <summary>
        /// Runs the blasts queued during a projectile step (cap.sg_2), each in its own transaction so the struck body takes
        /// it too — ProjectileSystem.Step calls this after its loop, outside any transaction.
        /// </summary>
        public static void FlushTreeBlasts()
        {
            int n = s_blastCount;
            if (n == 0) return;
            s_blastCount = 0;
            for (int i = 0; i < n; i++) HitscanEngine.Explode(s_blastAt[i], s_blastDamage[i], s_blastRadius[i]);
        }

        // ------------------------------------------------------------------ id sets (fixed, no allocation)

        private static bool HasId(int[] ids, int count, int id)
        {
            for (int i = 0; i < count; i++) if (ids[i] == id) return true;
            return false;
        }

        private static void AddId(int[] ids, ref int count, int id)
        {
            if (count >= ids.Length || HasId(ids, count, id)) return;
            ids[count++] = id;
        }

        // ------------------------------------------------------------------ run / scene boundary

        /// <summary>A new run (or its end, a scene change) clears the counters, the per-shot sets and the queued blasts.</summary>
        public static void ResetHitEffectsState()
        {
            s_roninRounds = 0;
            s_burstOrdinal = 0;
            s_lastBurstShot = float.NegativeInfinity;
            s_fangHits = 0;
            s_ashWallGroup = 0;
            s_ashWallCount = 0;
            s_twinGroup = 0;
            s_twinBase = 0;
            s_twinPair = 0;
            s_twinFirstCount = 0;
            s_twinBlownCount = 0;
            s_treeHitFlags = 0;
            s_ashWallArmed = false;
            s_blastCount = 0;
        }

        private static void OnHitEffectsRunBoundary(int run) => ResetHitEffectsState();

        /// <summary>Idempotent (remove-then-add), like <see cref="EnsureHooked"/>.</summary>
        public static void EnsureHitEffectsHooked()
        {
            SpecialWeaponRoundBridge.RunStarted -= OnHitEffectsRunBoundary;
            SpecialWeaponRoundBridge.RunStarted += OnHitEffectsRunBoundary;
            SpecialWeaponRoundBridge.RunEnded -= ResetHitEffectsState;
            SpecialWeaponRoundBridge.RunEnded += ResetHitEffectsState;
            RunTreeGroundFire.EnsureHooked();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHitEffectsStatics() => ResetHitEffectsState();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHitEffects()
        {
            EnsureHitEffectsHooked();
            // Re-arm on every active-scene change (static bus rearm lesson).
            SceneManager.activeSceneChanged -= OnHitEffectsSceneChanged;
            SceneManager.activeSceneChanged += OnHitEffectsSceneChanged;
        }

        private static void OnHitEffectsSceneChanged(Scene from, Scene to)
        {
            ResetHitEffectsState();
            EnsureHitEffectsHooked();
        }
    }
}
