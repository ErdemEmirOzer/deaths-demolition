using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// WP-095 K1 / K2 (C-00 08.10; his sözleşmesi §4.0, §4.3, §4.4, §4.11, §5) · the top-down global hit-stop's requests. Reads
    /// <see cref="FeelEvents"/> only and asks <see cref="FeelTimeScale"/>; the frames come from <c>FeelProfile_TD</c>
    /// (<c>events[].globalStopFrames</c> × the weapon row's <c>hitStopScale</c> → nearest frame, [1, maxStopFrames]):
    /// <list type="bullet">
    /// <item>armour break → ArmorBreak row (2); Elite / Ultra death → HeavyDeath rows (3 / 4);</item>
    /// <item>a critical kill by a single-shot class weapon (ar_2, sg_1, sg_2, sr_1) → <see cref="CritSingleShotKillFrames"/> (2);
    /// any other kill — critical or not — stops nothing (§4.3: "normal öldürmede olmaz");</item>
    /// <item>a multi-kill of ONE trigger pull (<see cref="FeelMultiKill.SameGroup"/>) by its own tier
    /// (<see cref="FeelMultiKill.GroupTier"/>): T2 2, T3 3 — a chain never stops (§4.4);</item>
    /// <item>a heavy (≥ 25 % max health), non-fatal player hit → PlayerDamaged_Heavy row (2), scale 1;</item>
    /// <item>the last zombie → LastZombie row (4) in place of its kill's stop, plus the slow motion (§4.11) — not when the player
    /// is down (death wins).</item>
    /// </list>
    /// §5 auto-fire guard: a weapon of ≥ autoGuardMinRpm (500) whose trigger has been producing shots (WeaponEvents.FireStarted)
    /// for ≥ autoGuardHeldSeconds (0.35 s) lets only armour breaks, heavy deaths, multi-kill T2+ and the last zombie stop.
    /// A trigger pull's events share its group, so they escalate one stop instead of starting several. Ground-fire ticks
    /// never stop. Top-down only: every handler returns at once in first / third person. 0 B per event.
    /// Subscriptions: <see cref="Rearm"/> (remove-then-add) after load and on every scene change (FeelTimeScaleRunner).
    /// </summary>
    public static class FeelHitStopTD
    {
        /// <summary>
        /// His sözleşmesi §4.0 Kill row note ("kritik + tek-atış sınıfı 2"): a critical kill by a single-shot class weapon stops
        /// this many frames before the HS scale (FeelProfileTD.critKillGlobalFrames).
        /// </summary>
        public static int CritSingleShotKillFrames => FeelProfileTD.Active.critKillGlobalFrames;

        private static readonly Action<FeelHit> s_onHit = OnHit;
        private static readonly Action<FeelKill> s_onKill = OnKill;
        private static readonly Action<FeelMultiKill> s_onMultiKill = OnMultiKill;
        private static readonly Action<FeelPlayerDamage> s_onPlayerDamaged = OnPlayerDamaged;
        private static readonly Action<FeelLastZombie> s_onLastZombie = OnLastZombie;
        private static readonly Action<ClassicWeaponId> s_onFireStarted = OnFireStarted;
        private static readonly Action<ClassicWeaponId> s_onFireStopped = OnFireStopped;

        // the auto-fire guard
        private static bool s_triggerHeld;
        private static float s_heldSince;
        private static float s_heldRpm;

        // the kill that a MultiKill / LastZombie right after it belongs to (same call, FeelEvents.OnEnemyDied)
        private static int s_lastKillGroup;
        private static float s_lastKillScale = 1f;

        private static bool IsTopDown => CameraModeService.Current == CameraMode.TopDown;

        /// <summary>Subscribes once (remove-then-add): static buses are reset at SubsystemRegistration and by tests.</summary>
        public static void Rearm()
        {
            FeelEvents.EnemyHit -= s_onHit; FeelEvents.EnemyHit += s_onHit;
            FeelEvents.EnemyKilled -= s_onKill; FeelEvents.EnemyKilled += s_onKill;
            FeelEvents.MultiKill -= s_onMultiKill; FeelEvents.MultiKill += s_onMultiKill;
            FeelEvents.PlayerDamaged -= s_onPlayerDamaged; FeelEvents.PlayerDamaged += s_onPlayerDamaged;
            FeelEvents.LastZombie -= s_onLastZombie; FeelEvents.LastZombie += s_onLastZombie;
            WeaponEvents.FireStarted -= s_onFireStarted; WeaponEvents.FireStarted += s_onFireStarted;
            WeaponEvents.FireStopped -= s_onFireStopped; WeaponEvents.FireStopped += s_onFireStopped;
        }

        public static void Disarm()
        {
            FeelEvents.EnemyHit -= s_onHit;
            FeelEvents.EnemyKilled -= s_onKill;
            FeelEvents.MultiKill -= s_onMultiKill;
            FeelEvents.PlayerDamaged -= s_onPlayerDamaged;
            FeelEvents.LastZombie -= s_onLastZombie;
            WeaponEvents.FireStarted -= s_onFireStarted;
            WeaponEvents.FireStopped -= s_onFireStopped;
        }

        /// <summary>His sözleşmesi §4.0 / §4.3 "tek-atış sınıfı" — the profile row's flag (ar_2, sg_1, sg_2, sr_1 by default).</summary>
        public static bool IsSingleShotClass(ClassicWeaponId id) => FeelProfileTD.Active.Weapon(id).singleShotClass;

        /// <summary>§5: the trigger of a ≥ autoGuardMinRpm weapon has been producing shots for ≥ autoGuardHeldSeconds.</summary>
        public static bool AutoGuardActive
        {
            get
            {
                if (!s_triggerHeld) return false;
                FeelProfileTD profile = FeelProfileTD.Active;
                return s_heldRpm >= profile.autoGuardMinRpm && FeelTimeScale.Clock - s_heldSince >= profile.autoGuardHeldSeconds - 1e-4f;
            }
        }

        // ------------------------------------------------------------------ FeelEvents → requests

        internal static void OnHit(FeelHit hit)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.HitStop);
            if (feelCostScope.Skip) return;
#endif
            if (!hit.ArmorBroke || hit.GroundFire || !IsTopDown) return;
            Request(FeelEventId.ArmorBreak, FeelProfileTD.Active.Event(FeelEventId.ArmorBreak).globalStopFrames, hit.GroupId,
                WeaponScale(hit.FromClassic, hit.Classic, hit.FromSpecial, hit.SpecialId), false);
        }

        internal static void OnKill(FeelKill kill)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.HitStop);
            if (feelCostScope.Skip) return;
#endif
            if (!IsTopDown) return;
            float scale = WeaponScale(kill.FromClassic, kill.Classic, kill.FromSpecial, kill.SpecialId);
            int group = kill.GroupId != 0 ? kill.GroupId : FeelTimeScale.NewAnonymousGroup();
            s_lastKillGroup = group;
            s_lastKillScale = scale;
            if (kill.LastOfRound) return;   // §4.11: the LastZombie event right after takes this kill's stop

            FeelProfileTD profile = FeelProfileTD.Active;
            if (kill.Heavy)
            {
                FeelEventId id = kill.Tier == EnemyTier.Ultra ? FeelEventId.HeavyDeath_Ultra : FeelEventId.HeavyDeath_Elite;
                Request(id, profile.Event(id).globalStopFrames, group, scale, false);
            }
            else if (kill.Critical && kill.FromClassic && IsSingleShotClass(kill.Classic))
            {
                Request(FeelEventId.Kill, CritSingleShotKillFrames, group, scale, true);
            }
        }

        internal static void OnMultiKill(FeelMultiKill multi)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.HitStop);
            if (feelCostScope.Skip) return;
#endif
            if (!multi.SameGroup || multi.GroupTier < 2 || !IsTopDown) return;   // a chain never stops, T1 never stops
            FeelEventId id = multi.GroupTier >= 3 ? FeelEventId.MultiKill_T3 : FeelEventId.MultiKill_T2;
            Request(id, FeelProfileTD.Active.Event(id).globalStopFrames, multi.GroupId != 0 ? multi.GroupId : s_lastKillGroup,
                s_lastKillScale, false);
        }

        internal static void OnPlayerDamaged(FeelPlayerDamage damage)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.HitStop);
            if (feelCostScope.Skip) return;
#endif
            if (!damage.Heavy || damage.Fatal || !IsTopDown) return;   // a fatal hit is the death beat's
            Request(FeelEventId.PlayerDamaged_Heavy, FeelProfileTD.Active.Event(FeelEventId.PlayerDamaged_Heavy).globalStopFrames,
                0, 1f, true);
        }

        internal static void OnLastZombie(FeelLastZombie last)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.HitStop);
            if (feelCostScope.Skip) return;
#endif
            if (!IsTopDown || PlayerDown()) return;   // §4.11: the player's death (P 100) wins
            Request(FeelEventId.LastZombie, FeelProfileTD.Active.Event(FeelEventId.LastZombie).globalStopFrames, s_lastKillGroup,
                s_lastKillScale, false);
            FeelTimeScale.BeginLastZombie(last.Position);
        }

        internal static void OnFireStarted(ClassicWeaponId id)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = FeelCostMeasurement.Enter(FeelCostPath.HitStop);
            FeelCostMeasurement.Trigger(true);
            if (feelCostScope.Skip) return;
#endif
            NotifyTrigger(true, ActiveRpm());
        }

        internal static void OnFireStopped(ClassicWeaponId id)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = FeelCostMeasurement.Enter(FeelCostPath.HitStop);
            FeelCostMeasurement.Trigger(false);
            if (feelCostScope.Skip) return;
#endif
            NotifyTrigger(false, 0f);
        }

        /// <summary>The trigger edge with the weapon's rate (rounds per minute); tests drive it directly.</summary>
        internal static void NotifyTrigger(bool producing, float roundsPerMinute)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.HitStop);
            if (feelCostScope.Skip) return;
#endif
            if (producing && !s_triggerHeld) s_heldSince = FeelTimeScale.Clock;
            s_triggerHeld = producing;
            s_heldRpm = producing ? roundsPerMinute : 0f;
        }

        // ------------------------------------------------------------------ helpers

        private static void Request(FeelEventId id, int baseFrames, int groupId, float scale, bool guarded)
        {
            if (baseFrames <= 0) return;
            FeelProfileTD profile = FeelProfileTD.Active;
            if (!profile.hitStopGlobal) return;
            if (guarded && AutoGuardActive)
            {
                FeelTimeScale.CountGuarded();
                return;
            }

            FeelTimeScale.RequestStop(groupId, FeelTimeScale.FramesFor(baseFrames, scale, profile.maxStopFrames), id);
        }

        private static float WeaponScale(bool fromClassic, ClassicWeaponId classic, bool fromSpecial, SpecialWeaponId special)
        {
            if (!fromClassic && !fromSpecial) return 1f;
            FeelProfileTD profile = FeelProfileTD.Active;
            WeaponFeelTD row = fromSpecial ? profile.Weapon(special) : profile.Weapon(classic);
            return row.hitStopScale > 0f ? row.hitStopScale : 1f;
        }

        private static float ActiveRpm()
        {
            WeaponController controller = WeaponRegistry.ActiveController;
            ClassicWeaponRuntime runtime = controller != null ? controller.Classic : null;
            return runtime != null ? runtime.EffectiveFireRate * 60f : 0f;
        }

        private static bool PlayerDown()
        {
            if (DeathSequence.IsActive) return true;
            PlayerRig rig = PlayerRegistry.Primary;
            return rig != null && rig.Health != null && rig.Health.IsDead;
        }

        /// <summary>Tests: the guard and the last-kill memory cleared (subscriptions untouched).</summary>
        public static void ResetForTests()
        {
            s_triggerHeld = false;
            s_heldSince = 0f;
            s_heldRpm = 0f;
            s_lastKillGroup = 0;
            s_lastKillScale = 1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ResetForTests();
    }
}
