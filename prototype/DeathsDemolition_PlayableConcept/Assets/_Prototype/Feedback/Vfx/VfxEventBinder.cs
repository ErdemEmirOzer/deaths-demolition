using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-047 A/B · The event hooks WP-019 left in the catalog: the enemy combat beats
    /// (<see cref="EnemyCombatEvents"/>, WP-040 — attack_commit → claw swipe, spawn_rise →
    /// spawn dust; breaks / deaths keep their director path), the animation foot events
    /// (<see cref="CharacterAnimationEvents.Any"/>, WP-032B — dust_step with the surface tint,
    /// throttled per body and by camera distance) and the weapon events (reload → mag drop;
    /// the Specials are ArcSystem / GelSystem's, WP-073 B6). Owned by <see cref="VfxDressing"/>; every handler is
    /// allocation-free (readonly-struct events, fixed tables, no closures — ADR-037-1).
    /// </summary>
    public sealed class VfxEventBinder
    {
        public const float FootStepMinInterval = 0.22f;
        public const float FootStepMaxCameraMetres = 26f;
        public const float MagDropDelaySeconds = 0.45f;
        public const float PumpSmokeDelaySeconds = 0.35f;
        public const int ThrottleCapacity = 96;

        private readonly VfxDressing _owner;
        private readonly Dictionary<int, float> _lastStep = new Dictionary<int, float>(ThrottleCapacity);
        private readonly RaycastHit[] _hits = new RaycastHit[1];
        private int _mask;
        private bool _subscribed;
        private float _magDropAt = -1f;
        private float _pumpSmokeAt = -1f;
        private Vector3 _pumpSmokeAnchor;
        private Vector3 _pumpSmokeDir;

        public int ClawSwipes { get; private set; }
        public int SpawnDusts { get; private set; }
        public int FootSteps { get; private set; }
        public int FootStepsThrottled { get; private set; }
        public int MagDrops { get; private set; }
        public int PumpSmokes { get; private set; }
        public int BeatsSeen { get; private set; }

        public VfxEventBinder(VfxDressing owner)
        {
            _owner = owner;
        }

        public void Subscribe()
        {
            if (!_subscribed)
            {
                _subscribed = true;
                // AP005 walkable surfaces are split between Environment and CameraOccluder.
                // Keep Default for prototype/test stand-ins while matching the authoritative
                // surface mask used by the audio footstep router.
                _mask = (1 << 0) | (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
                SceneManager.activeSceneChanged += OnSceneChanged;
            }

            Arm();
        }

        public void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            _subscribed = false;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Disarm();
        }

        // WP-062J: the owner is process-long (DDOL) but the buses are static events a caller may clear
        // (CharacterAnimationEvents.ResetStatics sets Any = null) — re-arm on every scene change so a
        // cleared bus costs at most one scene, and arming twice never doubles a handler.
        private void OnSceneChanged(Scene from, Scene to) => Arm();

        private void Arm()
        {
            Disarm();
            EnemyCombatEvents.Raised += OnCombatBeat;
            CharacterAnimationEvents.Any += OnAnimationEvent;
            WeaponEvents.ReloadStarted += OnReloadStarted;
        }

        private void Disarm()
        {
            EnemyCombatEvents.Raised -= OnCombatBeat;
            CharacterAnimationEvents.Any -= OnAnimationEvent;
            WeaponEvents.ReloadStarted -= OnReloadStarted;
        }

        /// <summary>Delayed effects (the magazine hits the ground, the pump vents after the shot).</summary>
        public void Tick(float now)
        {
            if (_magDropAt >= 0f && now >= _magDropAt)
            {
                _magDropAt = -1f;
                if (_owner.PlayMagDrop()) MagDrops++;
            }

            if (_pumpSmokeAt >= 0f && now >= _pumpSmokeAt)
            {
                _pumpSmokeAt = -1f;
                if (_owner.PlayPumpSmoke(_pumpSmokeAnchor, _pumpSmokeDir)) PumpSmokes++;
            }
        }

        /// <summary>Called by the muzzle path: a heavy (shotgun) shot schedules the pump vent.</summary>
        public void SchedulePumpSmoke(Vector3 anchor, Vector3 direction, float now)
        {
            _pumpSmokeAnchor = anchor;
            _pumpSmokeDir = direction;
            _pumpSmokeAt = now + PumpSmokeDelaySeconds;
        }

        // ------------------------------------------------------------------ enemy beats

        private void OnCombatBeat(EnemyCombatEventInfo info)
        {
            BeatsSeen++;
            switch (info.Kind)
            {
                case EnemyCombatEventKind.AttackCommit:
                    if (_owner.PlayClawSwipe(info.Position, info.Direction, info.Tier)) ClawSwipes++;
                    break;
                case EnemyCombatEventKind.SpawnRise:
                    // DEC-081: the rise only plays off-screen; the dust marks the ground breaking where the body comes up.
                    if (_owner.PlaySpawnDust(info.Position, info.Direction)) SpawnDusts++;
                    break;
            }
        }

        // ------------------------------------------------------------------ foot events

        private void OnAnimationEvent(CharacterAnimationEvents sender, CharacterAnimationEvent evt)
        {
            if (evt != CharacterAnimationEvent.FootLeft && evt != CharacterAnimationEvent.FootRight)
            {
                return;
            }

            if (sender == null)
            {
                return;
            }

            Transform body = sender.transform;
            float now = Time.time;
            int key = body.GetInstanceID();
            if (_lastStep.TryGetValue(key, out float last) && now - last < FootStepMinInterval)
            {
                FootStepsThrottled++;
                return;
            }

            if (FeedbackCameraRef.TryGetPosition(out Vector3 camera) && (camera - body.position).sqrMagnitude > FootStepMaxCameraMetres * FootStepMaxCameraMetres)
            {
                FootStepsThrottled++;
                return;
            }

            // The foot: root + 0.2 m forward, ± 0.14 m sideways; the ground under it gives the surface tint.
            Vector3 side = body.right * (evt == CharacterAnimationEvent.FootLeft ? -0.14f : 0.14f);
            Vector3 probe = body.position + body.forward * 0.2f + side + Vector3.up * 0.4f;
            if (Physics.RaycastNonAlloc(probe, Vector3.down, _hits, 1.2f, _mask, QueryTriggerInteraction.Ignore) == 0)
            {
                return;
            }

            SurfaceKind surface = SurfaceResolver.Resolve(_hits[0].collider);
            if (_owner.PlayDustStep(_hits[0].point, _hits[0].normal, body.forward, surface))
            {
                if (_lastStep.Count >= ThrottleCapacity && !_lastStep.ContainsKey(key))
                {
                    _lastStep.Clear();   // bounded: the table never grows past the crowd + player
                }

                _lastStep[key] = now;
                FootSteps++;
            }
        }

        // ------------------------------------------------------------------ weapons

        private void OnReloadStarted(float seconds)
        {
            _magDropAt = Time.time + Mathf.Min(MagDropDelaySeconds, Mathf.Max(0.1f, seconds * 0.5f));
        }

        /// <summary>Muzzle variant per classic weapon (DEC-083 A/B/C): A = rifle long cone, B = SMG short flash, C = shotgun wide bloom.</summary>
        public static MuzzleVariant VariantFor(ClassicWeaponId id)
        {
            // WP-069 B2: v3 weapons take their class sibling's variant until S-07's per-weapon pass.
            switch ((ClassicWeaponId)DD.Prototype.Core.Weapons.Arsenal.LegacyPoseIndex(id))
            {
                case ClassicWeaponId.AutoSmg: return MuzzleVariant.B;
                case ClassicWeaponId.CombatShotgun: return MuzzleVariant.C;
                default: return MuzzleVariant.A;
            }
        }
    }

    public enum MuzzleVariant
    {
        /// <summary>Rifle: long cone — stretch 1.35 along the barrel, manifest duration.</summary>
        A = 0,
        /// <summary>SMG: short flash — size 0.75, 60 % duration.</summary>
        B = 1,
        /// <summary>Shotgun: wide bloom — stretch 1.3 across, size 1.25, pump vent 0.35 s after.</summary>
        C = 2
    }
}
