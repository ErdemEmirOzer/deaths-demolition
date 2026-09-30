using System;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-077 K10b · the in-run tree's gameplay hooks. The effect LOGIC lives here (S-04); the weapon, damage and
    /// player code calls these on its existing paths with a one-line read (HitscanEngine, ClassicWeaponRuntime,
    /// RegionalDamageBody, ZombieEnemy, PlayerHealth, PlayerMotor). Every read goes through
    /// <see cref="RunTreeEffects.Active"/> (the run's resolved <see cref="RunTreeEffectValues"/>), so a node the run
    /// does not own is a neutral value (×1, +0, false) and no path allocates. <see cref="TestValues"/> lets a test
    /// drive the hooks without a run.
    /// </summary>
    public static partial class RunTreeHooks
    {
        /// <summary>Tests: values to use instead of the live run's (null = the run).</summary>
        public static RunTreeEffectValues TestValues;

        private static RunTreeEffectValues V => TestValues ?? RunTreeEffects.Active;

        private static float s_evadeUntil = -1f;
        private static float s_invulnerableUntil = -1f;
        private static bool s_secondWindUsed;
        private static PlayerHealth s_player;

        /// <summary>Counters for tests / F1 (never gameplay).</summary>
        public static int SecondWindsUsed { get; private set; }
        public static int KillRecoveries { get; private set; }

        // ------------------------------------------------------------------ weapon lane

        /// <summary>
        /// ks.piercing.line: the damage kept after a pierced body. <paramref name="touched"/> = bodies hit so far by
        /// this projectile (the one just hit included); while it is below the keystone's count nothing is lost.
        /// </summary>
        public static float PierceKeep(int touched, float keep)
        {
            int full = V.PierceFullBodies;
            return full > 0 && touched < full ? 1f : keep;
        }

        /// <summary>ks.chain.web: how many bodies the chain's FIRST jump strikes (1 = the normal chain).</summary>
        public static int ChainFirstJumpTargets => Math.Max(1, V.ChainFirstSplit);

        /// <summary>ks.ricochet.seeker: the radius a ricochet searches, scaled (the seeker reaches twice as far).</summary>
        public static float RicochetRadius(float radius) => V.RicochetSeek ? radius * RicochetSeekerReach : radius;

        public const float RicochetSeekerReach = 2f;

        /// <summary>ks.firerate.warmup: the fire-rate factor after <paramref name="heldSeconds"/> of continuous fire.</summary>
        public static float FireRateScale(float heldSeconds)
        {
            RunTreeEffectValues v = V;
            return v.WarmupFireRate > 0f && heldSeconds >= v.WarmupSeconds ? 1f + v.WarmupFireRate : 1f;
        }

        /// <summary>ks.reload.fullmag: rounds loaded PAST full by a reload from empty (0 for a tactical reload).</summary>
        public static int FullMagExtraRounds(bool emptyReload, int magazine)
        {
            float bonus = V.FullMagBonus;
            return emptyReload && bonus > 0f && magazine > 0 ? Mathf.Max(1, Mathf.FloorToInt(magazine * bonus)) : 0;
        }

        /// <summary>ks.cluster.seedling: extra bomblets on a launcher that already scatters (Cluster tier ≥ 1).</summary>
        public static int ClusterExtraBomblets => V.ClusterExtraBomblets;

        /// <summary>ks.armorbreak.plate: the factor on what a plate takes; ×N only while the plate is untouched.</summary>
        public static float PlateHitScale(bool plateUntouched) => plateUntouched ? V.PlateFirstHitMultiplier : 1f;

        /// <summary>
        /// ks.impact.stagger, and cap.smg_2 Lightning Fang for an smg_2 hit (DEC-112, additive): the factor on damage to a
        /// body that is staggering from a hit right now (the Black Iron tree stagger feeds no bonus — IsHitStaggering).
        /// </summary>
        public static float StaggeredScale(ZombieEnemy body)
        {
            if (body == null) return 1f;
            float bonus = V.StaggeredDamageBonus;
            float fang = DamageHitContext.FromClassicWeapon ? LightningFangBonus(DamageHitContext.Weapon) : 0f;
            if (bonus <= 0f && fang <= 0f) return 1f;
            ZombieAnimator animator = body.Animator;
            if (animator == null || !animator.IsHitStaggering) return 1f;
            if (fang > 0f) RaiseCapstoneProc(CapstoneProcKind.LightningFangBonus, DamageHitContext.Weapon, body.transform.position, body);
            return 1f + Mathf.Max(0f, bonus) + fang;
        }

        // ------------------------------------------------------------------ body lane

        /// <summary>ks.body.evade: the move-speed factor this frame (above the weapon clamp; PlayerMotor multiplies it in).</summary>
        public static float MoveSpeedScale => Time.time < s_evadeUntil ? 1f + V.EvadeSpeedBonus : 1f;

        /// <summary>
        /// The tree's move-speed factor above PlayerMotor's weapon clamp (≤ 1): Evade, and the part of the weapon's own
        /// factor above 1 that only a tree node can give (seal.smg_1 "move speed while firing ×1.1").
        /// </summary>
        public static float MoveSpeedScaleAbove(float weaponSpeedMultiplier) => MoveSpeedScale * Mathf.Max(1f, weaponSpeedMultiplier);

        /// <summary>cap.body.secondbreath: the player ignores hits for the few seconds after Second Wind caught a killing blow.</summary>
        public static bool PlayerInvulnerable => Time.time < s_invulnerableUntil;

        /// <summary>
        /// The amount a hit on the player deals after the body lane: Thick Skin at full health, Ash Step while moving,
        /// Iron Knee while standing still (multiplicative).
        /// </summary>
        public static float PlayerDamage(float amount, bool fullHealth, bool moving)
        {
            RunTreeEffectValues v = V;
            float scale = 1f;
            if (fullHealth && v.ThickSkinReduction > 0f) scale *= 1f - v.ThickSkinReduction;
            if (moving && v.MovingDamageReduction > 0f) scale *= 1f - v.MovingDamageReduction;
            if (!moving && v.StillDamageReduction > 0f) scale *= 1f - v.StillDamageReduction;
            return amount * Mathf.Clamp01(scale);
        }

        /// <summary>
        /// cap.body.secondbreath: once per run a killing blow leaves <paramref name="health"/> and a short window of
        /// invulnerability. Returns false when the node is not owned or was already used this run.
        /// </summary>
        public static bool TrySecondWind(out float health)
        {
            RunTreeEffectValues v = V;
            health = 0f;
            if (!v.SecondWind || s_secondWindUsed) return false;
            s_secondWindUsed = true;
            SecondWindsUsed++;
            health = Mathf.Max(1f, v.SecondWindHealth);
            s_invulnerableUntil = Time.time + Mathf.Max(0f, v.SecondWindInvulnerableSeconds);
            return true;
        }

        private static void OnPlayerDamaged(PlayerDamageInfo info)
        {
            if (info.Player != null) s_player = info.Player;
            RunTreeEffectValues v = V;
            if (v.EvadeSpeedBonus > 0f && info.AppliedAmount > 0f) s_evadeUntil = Time.time + v.EvadeSeconds;
        }

        private static void OnEnemyDied(EnemyDeathInfo info)
        {
            RunTreeEffectValues v = V;
            if (v.BloodStepHeal <= 0f && v.BreathCutSeconds <= 0f) return;
            PlayerHealth player = s_player != null ? s_player : (s_player = UnityEngine.Object.FindObjectOfType<PlayerHealth>());
            if (player == null) return;
            player.ApplyTreeRecovery(v.BloodStepHeal, v.BreathCutSeconds);
            KillRecoveries++;
        }

        private static void OnRunBoundary(int run) => ResetRunState();

        /// <summary>A new run (or its end) clears Second Wind, Evade and the invulnerability window.</summary>
        public static void ResetRunState()
        {
            s_secondWindUsed = false;
            s_evadeUntil = -1f;
            s_invulnerableUntil = -1f;
        }

        // ------------------------------------------------------------------ install

        /// <summary>Idempotent (remove-then-add): safe after a test cleared a bus, never two copies.</summary>
        public static void EnsureHooked()
        {
            EnsureWeaponStatsHooked();
            PlayerHealth.AnyDamaged -= OnPlayerDamaged;
            PlayerHealth.AnyDamaged += OnPlayerDamaged;
            DamageEvents.EnemyDied -= OnEnemyDied;
            DamageEvents.EnemyDied += OnEnemyDied;
            SpecialWeaponRoundBridge.RunStarted -= OnRunBoundary;
            SpecialWeaponRoundBridge.RunStarted += OnRunBoundary;
            SpecialWeaponRoundBridge.RunEnded -= ResetRunState;
            SpecialWeaponRoundBridge.RunEnded += ResetRunState;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetRunState();
            TestValues = null;
            s_player = null;
            SecondWindsUsed = 0;
            KillRecoveries = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            EnsureHooked();
            // Re-arm on every active-scene change (static bus rearm lesson): a bus a test reset stays wired.
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private static void OnActiveSceneChanged(Scene from, Scene to)
        {
            s_player = null;
            EnsureHooked();
        }
    }
}
