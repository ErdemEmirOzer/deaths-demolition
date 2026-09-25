using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-077 K10b stage 2 · the run weapon's own seal / fork / capstone numbers. Pure reads of <see cref="V"/>:
    /// every hook takes the weapon that is asking (ClassicWeaponRuntime passes its <c>Id</c>) and changes nothing
    /// unless that weapon IS the run weapon (<see cref="RunTreeEffectValues.WeaponSlot"/>) and the node is owned —
    /// a debug-panel swap or another weapon's node is neutral (×1, +0, the input unchanged). Args are the catalog's
    /// display units (a percent as 20, a factor as 1.5). No hook allocates.
    /// </summary>
    public static partial class RunTreeHooks
    {
        /// <summary>seal.lmg_2: the card's "base spread 1.2° → {spread}°" — the hip cone is scaled by spread / this in every camera mode.</summary>
        public const float LmgTwoCardHipDegrees = 1.2f;

        /// <summary>fork.lmg_1.x: the player counts as standing still at or below this share of top speed.</summary>
        public const float StillSpeed01 = 0.1f;

        private static int s_capHits;
        private static float s_noReloadUntil = -1f;

        /// <summary>cap.lmg_1: hits counted towards the next free window (tests / F1).</summary>
        public static int CapHitCount => s_capHits;

        private static bool Owns(RunTreeEffectValues v, ClassicWeaponId weapon, ClassicWeaponId id, string slot) =>
            weapon == id && string.Equals(v.WeaponSlot, slot, StringComparison.Ordinal);

        private static float Percent(float display) => display / 100f;

        // ------------------------------------------------------------------ fire rate / burst

        /// <summary>
        /// The fire-rate factor for <paramref name="weapon"/>: Warm-Up (<see cref="FireRateScale"/>) times the fork bonuses —
        /// fork.ar_1.y, fork.smg_2.y, fork.lmg_2.x, fork.sr_1.y (+value %).
        /// </summary>
        public static float WeaponFireRateScale(ClassicWeaponId weapon, float heldSeconds) =>
            FireRateScale(heldSeconds) * ForkFireRateScale(weapon);

        /// <summary>The forks' fire-rate factor alone (1 when none is owned).</summary>
        public static float ForkFireRateScale(ClassicWeaponId weapon)
        {
            RunTreeEffectValues v = V;
            if (v.ForkY && (Owns(v, weapon, ClassicWeaponId.AssaultRifle, "ar_1") || Owns(v, weapon, ClassicWeaponId.Smg2, "smg_2")
                            || Owns(v, weapon, ClassicWeaponId.Sr1, "sr_1")))
            {
                return 1f + Percent(v.ForkYArg("value", 15f));
            }

            if (v.ForkX && Owns(v, weapon, ClassicWeaponId.Lmg2, "lmg_2")) return 1f + Percent(v.ForkXArg("value", 15f));
            return 1f;
        }

        /// <summary>fork.ar_2.x: the delay between bursts × (1 − value %).</summary>
        public static float BurstDelayScale(ClassicWeaponId weapon)
        {
            RunTreeEffectValues v = V;
            return v.ForkX && Owns(v, weapon, ClassicWeaponId.Ar2, "ar_2") ? Mathf.Max(0.05f, 1f - Percent(v.ForkXArg("value", 20f))) : 1f;
        }

        /// <summary>fork.ar_2.y: the burst's round count, base → value (the difference is added to the definition's count).</summary>
        public static int BurstCount(ClassicWeaponId weapon, int count)
        {
            RunTreeEffectValues v = V;
            if (!v.ForkY || !Owns(v, weapon, ClassicWeaponId.Ar2, "ar_2")) return count;
            return Math.Max(1, count + Mathf.RoundToInt(v.ForkYArg("value", 4f) - v.ForkYArg("base", 3f)));
        }

        // ------------------------------------------------------------------ spread / bloom

        /// <summary>
        /// The bloom that widens the cone this shot (positive bloom only; a settle curve's negative bloom is untouched):
        /// fork.ar_1.x × (1 − value %) always, seal.ar_1 × (1 − value %) once the string has fired <c>count</c> shots.
        /// </summary>
        public static float Bloom(ClassicWeaponId weapon, float bloom, int shotInString)
        {
            if (bloom <= 0f) return bloom;
            RunTreeEffectValues v = V;
            if ((!v.Seal && !v.ForkX) || !Owns(v, weapon, ClassicWeaponId.AssaultRifle, "ar_1")) return bloom;
            float scale = 1f;
            if (v.ForkX) scale *= 1f - Percent(v.ForkXArg("value", 50f));
            if (v.Seal && shotInString >= Mathf.RoundToInt(v.SealArg("count", 6f))) scale *= 1f - Percent(v.SealArg("value", 50f));
            return bloom * Mathf.Clamp01(scale);
        }

        /// <summary>
        /// The bloom added per shot: fork.ar_1.y grows a positive bloom + bloom %; seal.lmg_2 steps a negative (settle)
        /// bloom base / value times as far, so the cone reaches its settle floor in value shots instead of base.
        /// </summary>
        public static float BloomPerShot(ClassicWeaponId weapon, float perShot)
        {
            RunTreeEffectValues v = V;
            if (perShot > 0f && v.ForkY && Owns(v, weapon, ClassicWeaponId.AssaultRifle, "ar_1"))
            {
                return perShot * (1f + Percent(v.ForkYArg("bloom", 30f)));
            }

            if (perShot < 0f && v.Seal && Owns(v, weapon, ClassicWeaponId.Lmg2, "lmg_2"))
            {
                float shots = Mathf.Max(1f, v.SealArg("value", 3f));
                return perShot * Mathf.Max(1f, v.SealArg("base", 6f)) / shots;
            }

            return perShot;
        }

        /// <summary>seal.lmg_2: the hip cone × spread / <see cref="LmgTwoCardHipDegrees"/> (the card's 1.2° → 0.9°).</summary>
        public static float HipSpread(ClassicWeaponId weapon, float hip)
        {
            RunTreeEffectValues v = V;
            if (!v.Seal || !Owns(v, weapon, ClassicWeaponId.Lmg2, "lmg_2")) return hip;
            return hip * Mathf.Max(0f, v.SealArg("spread", LmgTwoCardHipDegrees)) / LmgTwoCardHipDegrees;
        }

        /// <summary>fork.sg_1.x: the whole pellet cone (base + bloom) × (1 − value %).</summary>
        public static float SpreadScale(ClassicWeaponId weapon)
        {
            RunTreeEffectValues v = V;
            return v.ForkX && Owns(v, weapon, ClassicWeaponId.CombatShotgun, "sg_1") ? Mathf.Clamp01(1f - Percent(v.ForkXArg("value", 40f))) : 1f;
        }

        // ------------------------------------------------------------------ pellets / rounds

        /// <summary>
        /// Pellets in one trigger pull: seal.sg_1 + (value − base), fork.sg_1.y + count; seal.sg_2 fires both barrels
        /// (count pellets together) while at least two rounds are loaded.
        /// </summary>
        public static int PelletCount(ClassicWeaponId weapon, int pellets, int loadedRounds)
        {
            RunTreeEffectValues v = V;
            if (Owns(v, weapon, ClassicWeaponId.CombatShotgun, "sg_1"))
            {
                if (v.Seal) pellets += Mathf.RoundToInt(v.SealArg("value", 10f) - v.SealArg("base", 8f));
                if (v.ForkY) pellets += Mathf.RoundToInt(v.ForkYArg("count", 2f));
                return Math.Max(1, pellets);
            }

            if (v.Seal && loadedRounds >= 2 && Owns(v, weapon, ClassicWeaponId.Sg2, "sg_2"))
            {
                return Math.Max(pellets, Mathf.RoundToInt(v.SealArg("count", pellets * 2)));
            }

            return pellets;
        }

        /// <summary>Rounds one trigger pull takes from the magazine (live clock); see <see cref="RoundsPerPull(ClassicWeaponId, int, float)"/>.</summary>
        public static int RoundsPerPull(ClassicWeaponId weapon, int loadedRounds) => RoundsPerPull(weapon, loadedRounds, Time.time);

        /// <summary>
        /// Rounds one trigger pull takes: 2 for seal.sg_2 with both barrels loaded, 0 inside cap.lmg_1's free window,
        /// otherwise 1.
        /// </summary>
        public static int RoundsPerPull(ClassicWeaponId weapon, int loadedRounds, float now)
        {
            RunTreeEffectValues v = V;
            if (v.Seal && loadedRounds >= 2 && Owns(v, weapon, ClassicWeaponId.Sg2, "sg_2")) return 2;
            if (v.Capstone && now < s_noReloadUntil && Owns(v, weapon, ClassicWeaponId.Lmg1, "lmg_1")) return 0;
            return 1;
        }

        /// <summary>The magazine: fork.smg_1.x × value (rounded down), seal.lmg_1 × value / base (100 → 150).</summary>
        public static int MagazineSize(ClassicWeaponId weapon, int size)
        {
            RunTreeEffectValues v = V;
            if (v.ForkX && Owns(v, weapon, ClassicWeaponId.AutoSmg, "smg_1")) return Math.Max(1, Mathf.FloorToInt(size * v.ForkXArg("value", 1.5f)));
            if (v.Seal && Owns(v, weapon, ClassicWeaponId.Lmg1, "lmg_1"))
            {
                return Math.Max(1, Mathf.FloorToInt(size * v.SealArg("value", 150f) / Mathf.Max(1f, v.SealArg("base", 100f))));
            }

            return size;
        }

        /// <summary>fork.sg_2.y: reload time × (1 − value %).</summary>
        public static float ReloadTimeScale(ClassicWeaponId weapon)
        {
            RunTreeEffectValues v = V;
            return v.ForkY && Owns(v, weapon, ClassicWeaponId.Sg2, "sg_2") ? Mathf.Max(0.05f, 1f - Percent(v.ForkYArg("value", 25f))) : 1f;
        }

        // ------------------------------------------------------------------ damage / shot request

        /// <summary>
        /// The damage factor of one shot: fork.lmg_1.x + value % while the player stands still (speed01 ≤
        /// <see cref="StillSpeed01"/>), fork.lmg_2.y + value % on the first count rounds after a completed reload,
        /// fork.sr_1.x × value when at least seconds passed since the last shot.
        /// </summary>
        public static float ShotDamageScale(ClassicWeaponId weapon, float speed01, float sinceLastShot, int roundsSinceReload)
        {
            RunTreeEffectValues v = V;
            if (!v.ForkX && !v.ForkY) return 1f;
            if (v.ForkX && Owns(v, weapon, ClassicWeaponId.Lmg1, "lmg_1"))
            {
                return speed01 <= StillSpeed01 ? 1f + Percent(v.ForkXArg("value", 25f)) : 1f;
            }

            if (v.ForkY && Owns(v, weapon, ClassicWeaponId.Lmg2, "lmg_2"))
            {
                return roundsSinceReload >= 0 && roundsSinceReload < Mathf.RoundToInt(v.ForkYArg("count", 10f)) ? 1f + Percent(v.ForkYArg("value", 30f)) : 1f;
            }

            if (v.ForkX && Owns(v, weapon, ClassicWeaponId.Sr1, "sr_1"))
            {
                return sinceLastShot >= v.ForkXArg("seconds", 1f) ? Mathf.Max(1f, v.ForkXArg("value", 2f)) : 1f;
            }

            return 1f;
        }

        /// <summary>seal.ar_2: the weapon's extra head multiplier becomes value (×1.15 → ×1.5).</summary>
        public static float HeadMultiplier(ClassicWeaponId weapon, float head)
        {
            RunTreeEffectValues v = V;
            return v.Seal && Owns(v, weapon, ClassicWeaponId.Ar2, "ar_2") ? Mathf.Max(head, v.SealArg("value", 1.5f)) : head;
        }

        /// <summary>cap.smg_1: ricochets bounce count times (the Ricochet branch still decides whether a shot ricochets).</summary>
        public static int RicochetBounces(ClassicWeaponId weapon, int bounces)
        {
            RunTreeEffectValues v = V;
            return v.Capstone && Owns(v, weapon, ClassicWeaponId.AutoSmg, "smg_1") ? Math.Max(bounces, Mathf.RoundToInt(v.CapstoneArg("count", 2f))) : bounces;
        }

        // ------------------------------------------------------------------ movement

        /// <summary>
        /// The weapon's "while firing" move factor: fork.lmg_1.y lifts the penalty (1), seal.smg_1 × value.
        /// NOTE: PlayerMotor clamps the weapon factor to ≤ 1, so seal.smg_1 above 1 needs that clamp lifted to show.
        /// </summary>
        public static float FiringMoveSpeed(ClassicWeaponId weapon, float multiplier)
        {
            RunTreeEffectValues v = V;
            if (v.ForkY && Owns(v, weapon, ClassicWeaponId.Lmg1, "lmg_1")) return Mathf.Max(1f, multiplier);
            if (v.Seal && Owns(v, weapon, ClassicWeaponId.AutoSmg, "smg_1")) return multiplier * Mathf.Max(0.1f, v.SealArg("value", 1.1f));
            return multiplier;
        }

        // ------------------------------------------------------------------ cap.lmg_1 free window

        /// <summary>cap.lmg_1: rounds are not consumed right now (live clock).</summary>
        public static bool NoReloadWindow(ClassicWeaponId weapon) => NoReloadWindow(weapon, Time.time);

        public static bool NoReloadWindow(ClassicWeaponId weapon, float now)
        {
            RunTreeEffectValues v = V;
            return v.Capstone && now < s_noReloadUntil && Owns(v, weapon, ClassicWeaponId.Lmg1, "lmg_1");
        }

        /// <summary>
        /// cap.lmg_1: one direct hit by <paramref name="weapon"/>. The count-th hit opens a window of seconds with no
        /// round consumed and restarts the count; hits inside an open window are not counted.
        /// </summary>
        public static void CountWeaponHit(ClassicWeaponId weapon, float now)
        {
            RunTreeEffectValues v = V;
            if (!v.Capstone || !Owns(v, weapon, ClassicWeaponId.Lmg1, "lmg_1") || now < s_noReloadUntil) return;
            if (++s_capHits < Math.Max(1, Mathf.RoundToInt(v.CapstoneArg("count", 40f)))) return;
            s_capHits = 0;
            s_noReloadUntil = now + Mathf.Max(0f, v.CapstoneArg("seconds", 6f));
        }

        private static void OnWeaponStatsEnemyDamaged(EnemyDamageInfo info)
        {
            // Read inside the handler (DamageHitContext is the ambient weapon of the hit being applied right now).
            if (DamageHitContext.FromClassicWeapon) CountWeaponHit(DamageHitContext.Weapon, Time.time);
        }

        /// <summary>A new run (or its end) clears the capstone's hit count and window.</summary>
        public static void ResetWeaponStatsState()
        {
            s_capHits = 0;
            s_noReloadUntil = -1f;
        }

        private static void OnWeaponStatsRunBoundary(int run) => ResetWeaponStatsState();

        /// <summary>Idempotent (remove-then-add), like <see cref="EnsureHooked"/>.</summary>
        public static void EnsureWeaponStatsHooked()
        {
            DamageEvents.EnemyDamaged -= OnWeaponStatsEnemyDamaged;
            DamageEvents.EnemyDamaged += OnWeaponStatsEnemyDamaged;
            SpecialWeaponRoundBridge.RunStarted -= OnWeaponStatsRunBoundary;
            SpecialWeaponRoundBridge.RunStarted += OnWeaponStatsRunBoundary;
            SpecialWeaponRoundBridge.RunEnded -= ResetWeaponStatsState;
            SpecialWeaponRoundBridge.RunEnded += ResetWeaponStatsState;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWeaponStatsStatics() => ResetWeaponStatsState();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallWeaponStats()
        {
            EnsureWeaponStatsHooked();
            // Re-arm on every active-scene change (static bus rearm lesson).
            SceneManager.activeSceneChanged -= OnWeaponStatsSceneChanged;
            SceneManager.activeSceneChanged += OnWeaponStatsSceneChanged;
        }

        private static void OnWeaponStatsSceneChanged(Scene from, Scene to) => EnsureWeaponStatsHooked();
    }
}
