using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>Easing curves of the top-down envelopes (his sözleşmesi §2 / §4).</summary>
    public enum TopDownFeelEase { Linear = 0, OutQuad = 1, OutCubic = 2, OutSine = 3, InOutSine = 4 }

    /// <summary>Per-channel read-out of the last composed top-down frame (tests, the `feel.trauma` telemetry hook).</summary>
    public struct TopDownFeelReadout
    {
        public float FireTrauma, WorldTrauma;
        /// <summary>K1 / K2 / K3 magnitudes before the aggregate clamp and the player's scale (metres).</summary>
        public float FireMeters, WorldMeters, KickMeters;
        /// <summary>K1 + K2 + K3 (+ direct CameraShakeService requests) after the clamp and the scale — the node offset without K5.</summary>
        public float TotalMeters;
        public float RollDegrees;
        /// <summary>K5 along the node's local +Z (positive = closer).</summary>
        public float ZoomMeters;
        public float FocusMeters;
        /// <summary>K4 before the look-ahead clamp.</summary>
        public float LeanMeters;
    }

    /// <summary>
    /// WP-095 (R3) · the top-down camera feel (his sözleşmesi K/wp-095/HIS-SOZLESMESI.md §2, C-00 K6 / K7 08.10). Pure state, no
    /// Unity objects, nothing allocates; the rig (<see cref="PrototypeCameraRig"/>) owns one and drives it every LateUpdate, the
    /// S-07 driver (CameraFeelDriver) feeds it from <see cref="FeelEvents"/> in top-down only. Values and switches come from
    /// <see cref="FeelProfileTD"/>.
    /// <list type="bullet">
    /// <item>K1 fire trauma — <c>T ← max(T, min(T + add, cap))</c> per shot; offset = fireMaxOffsetMeters × T^exp × noise
    /// (fireNoiseHz), the component across the aim × aimLateralShare, roll 0; decays fireDecayPerSecond on scaled time.</item>
    /// <item>K2 world trauma — the same rule (cap 1), noise worldNoiseHz equal in every direction, roll ≤ rollCapDegrees × T^exp.</item>
    /// <item>K3 kick — directional impulses into the rig's kick spring (the row's kickSpring W1–W4): a shot pushes −aim by its
    /// kickMeters and repeated shots saturate at sustainedKickCapMeters; one event ≤ <see cref="SingleEventKickCapMeters"/>.</item>
    /// <item>K4 fire lean — the stable pose leans along the aim by fireLeanMeters × an envelope (in easeOutCubic, hold after
    /// the trigger lets go, out easeInOutSine) on unscaled time; the rig clamps |look-ahead + lean|.</item>
    /// <item>K5 zoom punch — a fraction of the camera distance on the node's local +Z, outside the aggregate clamp.</item>
    /// </list>
    /// K1 + K2 + K3 ≤ aggregateCapMeters × the player's scale (× 0.3 reduced motion); K4 / K5 are 0 in reduced motion. The aim
    /// ray is rebuilt from the stable pose (ScreenPointToAimPlane), so nothing on the node moves the aim.
    /// </summary>
    public sealed class TopDownFeelCamera
    {
        /// <summary>His sözleşmesi §2 K3: one event's kick never exceeds this (the kickMeters range ceiling).</summary>
        public const float SingleEventKickCapMeters = 0.14f;

        /// <summary>C-00 K6 (08.10): the zoom punch stays within ±8 % of the camera distance.</summary>
        public const float ZoomCapFraction = 0.08f;

        /// <summary>His sözleşmesi §4.10: the round-start breath returns from +4 % to 0 over this long (easeInOutSine; FeelProfileTD).</summary>
        public static float RoundStartBreathSeconds => FeelProfileTD.Active.roundStartBreathSeconds;

        /// <summary>The breath eases in over this long (easeInOutSine; FeelProfileTD) — a breath, not a pop.</summary>
        public static float RoundStartBreathInSeconds => FeelProfileTD.Active.roundStartBreathInSeconds;

        /// <summary>His sözleşmesi §4.10: no breath while a zombie stands within this radius of the player (FeelProfileTD).</summary>
        public static float RoundStartClearRadiusMeters => FeelProfileTD.Active.roundStartClearRadiusMeters;

        // Fixed noise lanes (deterministic: the same inputs give the same frames — replay / tests).
        private const float FireSeedX = 11.31f, FireSeedY = 47.93f, WorldSeedX = 83.17f, WorldSeedY = 129.71f, WorldSeedRoll = 171.37f;
        private const float NoiseClockWrapSeconds = 1000f;

        private FeelProfileTD _profile;

        // ---- K1 / K2
        private float _fireTrauma, _worldTrauma, _noiseClock;
        private Vector3 _fireAim = Vector3.forward;   // planar world direction of the last shot

        // ---- K3 (world-space impulses, drained by the rig once per frame)
        private Vector3 _pendingFireKick, _pendingEventKick;
        private FeelWeight _kickWeight = FeelWeight.W2;

        // ---- K4
        private enum LeanPhase { Idle = 0, In = 1, Held = 2, Hold = 3, Out = 4 }
        private LeanPhase _leanPhase;
        private bool _fireHeld;
        private float _leanT, _leanFrom, _lean01, _leanMeters;
        private Vector3 _leanTarget = Vector3.forward, _leanDir, _leanDirVelocity;

        // ---- K5
        private enum ZoomPhase { Idle = 0, In = 1, Hold = 2, Out = 3 }
        private ZoomPhase _zoomPhase;
        private float _zoom, _zoomFrom, _zoomPeak, _zoomT, _zoomIn, _zoomHold, _zoomOut;
        private TopDownFeelEase _zoomInEase, _zoomOutEase;

        /// <summary>The last composed frame, per channel.</summary>
        public TopDownFeelReadout Last;

        /// <summary>The profile in use: the assigned one (tests) or <see cref="FeelProfileTD.Active"/>.</summary>
        public FeelProfileTD Profile
        {
            get { return _profile != null ? _profile : FeelProfileTD.Active; }
            set { _profile = value; }
        }

        public float FireTrauma => _fireTrauma;
        public float WorldTrauma => _worldTrauma;

        /// <summary>K3 spring weight: the last shot's row (W2 until the first shot).</summary>
        public FeelWeight KickWeight => _kickWeight;

        /// <summary>K4 envelope 0..1 (before the weapon's metres).</summary>
        public float Lean01 => _lean01;

        /// <summary>K5 fraction of the camera distance (negative = closer).</summary>
        public float ZoomFraction => _zoom;

        public int Shots { get; private set; }
        public int WorldEvents { get; private set; }
        public int EventKicks { get; private set; }
        public int ZoomPunches { get; private set; }

        // ------------------------------------------------------------------ inputs (CameraFeelDriver, top-down only)

        /// <summary>
        /// One trigger pull (FeelEvents.ShotFired): K1 trauma, K3 −aim kick, the K4 row and direction. <paramref name="pulse"/>:
        /// a shot without fire edges (a Special) leans once — in, hold, out.
        /// </summary>
        public void Shot(in WeaponFeelTD row, Vector3 aimDirection, bool pulse)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            FeelProfileTD p = Profile;
            Vector3 aim = Planar(aimDirection);
            if (aim.sqrMagnitude > 0f) _fireAim = aim;
            _kickWeight = row.kickSpring;
            if (p.fireTrauma) _fireTrauma = AddTrauma(_fireTrauma, row.fireTraumaAdd, row.fireTraumaCap);
            if (p.kick && row.kickMeters > 0f) _pendingFireKick -= _fireAim * Mathf.Min(row.kickMeters, SingleEventKickCapMeters);
            _leanMeters = row.fireLeanMeters;
            _leanTarget = _fireAim;
            if (pulse)
            {
                Press();
                Release();
            }

            Shots++;
        }

        /// <summary>K4 fire edges (WeaponEvents.FireStarted / FireStopped; a reload start and the round's end let go too).</summary>
        public void FireHeld(bool held)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            if (held) Press();
            else Release();
        }

        /// <summary>K2: <c>T ← max(T, min(T + add, 1))</c>.</summary>
        public void World(float add)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            if (add <= 0f || !Profile.worldTrauma) return;
            _worldTrauma = AddTrauma(_worldTrauma, add, 1f);
            WorldEvents++;
        }

        /// <summary>K3: one event's kick along the planar <paramref name="worldDirection"/>, ≤ <see cref="SingleEventKickCapMeters"/>.</summary>
        public void Kick(Vector3 worldDirection, float meters)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            if (meters <= 0f || !Profile.kick) return;
            Vector3 d = Planar(worldDirection);
            if (d.sqrMagnitude <= 0f) return;
            _pendingEventKick += d * Mathf.Min(meters, SingleEventKickCapMeters);
            EventKicks++;
        }

        /// <summary>An event row's K2, K3 (along <paramref name="kickDirection"/>) and K5, each × <paramref name="scale"/>.</summary>
        public void Event(FeelEventId id, Vector3 kickDirection, float scale = 1f)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            EventFeelTD row = Profile.Event(id);
            World(row.worldTraumaAdd * scale);
            Kick(kickDirection, row.kickMeters * scale);
            Zoom(row.zoomPunch * scale);
        }

        /// <summary>An event row's K2 and K5 only — its K3 joins the merged kill beat (CameraFeelDriver).</summary>
        public void EventWithoutKick(FeelEventId id)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            EventFeelTD row = Profile.Event(id);
            World(row.worldTraumaAdd);
            Zoom(row.zoomPunch);
        }

        /// <summary>§4.5: the Explosion row × A(d) (CameraModesFeel.ExplosionFalloff, d = player ↔ blast), kick away from the blast.</summary>
        public void Explosion(Vector3 origin, Vector3 player)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            Vector3 away = player - origin;
            away.y = 0f;
            float a = CameraModesFeel.ExplosionFalloff(away.magnitude);
            if (a <= 0f) return;
            Event(FeelEventId.Explosion, away, a);
        }

        /// <summary>
        /// §4.0: K2 from the Light row to the Heavy row, linear in the damage fraction up to the heavy threshold; K3 the Light /
        /// Heavy row away from the source (<paramref name="awayFromSource"/> = attacker → player).
        /// </summary>
        public void PlayerDamaged(float fraction, bool heavy, Vector3 awayFromSource)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            FeelProfileTD p = Profile;
            EventFeelTD light = p.Event(FeelEventId.PlayerDamaged_Light), hurt = p.Event(FeelEventId.PlayerDamaged_Heavy);
            float t = heavy ? 1f : Mathf.Clamp01(fraction / Mathf.Max(0.01f, FeelEvents.Thresholds.HeavyPlayerHitFraction));
            World(Mathf.Lerp(light.worldTraumaAdd, hurt.worldTraumaAdd, t));
            Kick(awayFromSource, heavy ? hurt.kickMeters : light.kickMeters);
            Zoom(heavy ? hurt.zoomPunch : light.zoomPunch);
        }

        /// <summary>§4.11: K2 and K5 of the last zombie; K5 rides the slow-motion envelope (in, hold, out) when that layer is on.</summary>
        public void LastZombie()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            FeelProfileTD p = Profile;
            EventFeelTD row = p.Event(FeelEventId.LastZombie);
            World(row.worldTraumaAdd);
            if (p.lastZombieSlowMo)
            {
                Zoom(row.zoomPunch, p.lastZombieInSeconds, TopDownFeelEase.OutCubic, p.lastZombieHoldSeconds, p.lastZombieOutSeconds,
                    TopDownFeelEase.InOutSine);
            }
            else
            {
                Zoom(row.zoomPunch);
            }
        }

        /// <summary>§4.10: the round-start breath (0 → +4 % eased in, → 0 easeInOutSine); skipped while a zombie is near. True when it plays.</summary>
        public bool RoundStart(bool zombieNear)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return false; }
#endif
            if (zombieNear) return false;
            float fraction = Profile.Event(FeelEventId.RoundStart).zoomPunch;
            if (fraction == 0f) return false;
            return Zoom(fraction, Profile.roundStartBreathInSeconds, TopDownFeelEase.InOutSine, 0f, Profile.roundStartBreathSeconds, TopDownFeelEase.InOutSine);
        }

        /// <summary>K5 with the profile's punch envelope (in easeOutQuad, out easeOutSine).</summary>
        public bool Zoom(float fraction)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel, "Zoom.Default");
            if (feelCostScope.Skip) { return false; }
#endif
            FeelProfileTD p = Profile;
            return Zoom(fraction, p.zoomInSeconds, TopDownFeelEase.OutQuad, 0f, p.zoomOutSeconds, TopDownFeelEase.OutSine);
        }

        /// <summary>
        /// K5: ± <see cref="ZoomCapFraction"/> of the camera distance. Merged, not stacked: a punch weaker than the running one
        /// (its peak while rising / holding, its value while returning) is dropped; a stronger one rises from where the camera is.
        /// </summary>
        public bool Zoom(float fraction, float inSeconds, TopDownFeelEase inEase, float holdSeconds, float outSeconds, TopDownFeelEase outEase)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel, "Zoom.Envelope");
            if (feelCostScope.Skip) { return false; }
#endif
            if (fraction == 0f || !Profile.zoomPunch) return false;
            fraction = Mathf.Clamp(fraction, -ZoomCapFraction, ZoomCapFraction);
            float running = _zoomPhase == ZoomPhase.In || _zoomPhase == ZoomPhase.Hold ? Mathf.Max(Mathf.Abs(_zoomPeak), Mathf.Abs(_zoom)) : Mathf.Abs(_zoom);
            if (Mathf.Abs(fraction) < running) return false;

            _zoomFrom = _zoom;
            _zoomPeak = fraction;
            _zoomIn = Mathf.Max(0f, inSeconds);
            _zoomHold = Mathf.Max(0f, holdSeconds);
            _zoomOut = Mathf.Max(0f, outSeconds);
            _zoomInEase = inEase;
            _zoomOutEase = outEase;
            _zoomT = 0f;
            if (_zoomIn > 0f)
            {
                _zoomPhase = ZoomPhase.In;
            }
            else
            {
                _zoom = fraction;
                _zoomPhase = _zoomHold > 0f ? ZoomPhase.Hold : ZoomPhase.Out;
            }

            ZoomPunches++;
            return true;
        }

        // ------------------------------------------------------------------ rig side (once per LateUpdate, top-down only)

        /// <summary>K4 / K5 envelopes on unscaled time (the rig passes 0 while RunFlowClock is frozen).</summary>
        public void TickEnvelopes(float unscaledDt, float aimSmoothSeconds)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            if (unscaledDt <= 0f) return;
            TickLean(unscaledDt, Profile, aimSmoothSeconds);
            TickZoom(unscaledDt);
        }

        /// <summary>K4: the lean in world metres (0 in reduced motion or with the switch off), before the look-ahead clamp.</summary>
        public Vector3 LeanOffset(bool reducedMotion)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return Vector3.zero; }
#endif
            FeelProfileTD p = Profile;
            Vector3 lean = reducedMotion || !p.fireLean || _lean01 <= 0f ? Vector3.zero : _leanDir * (_leanMeters * _lean01);
            Last.LeanMeters = lean.magnitude;
            return lean;
        }

        /// <summary>C-00 K7 (08.10): |look-ahead + lean| ≤ <paramref name="totalCap"/>; the look-ahead's own 2.5 m cap is untouched.</summary>
        public static Vector3 ClampLean(Vector3 lookAhead, Vector3 lean, float totalCap)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return Vector3.zero; }
#endif
            if (lean.sqrMagnitude <= 0f) return lean;
            return Vector3.ClampMagnitude(lookAhead + lean, Mathf.Max(0f, totalCap)) - lookAhead;
        }

        /// <summary>
        /// K3: the frame's impulses into the rig's kick-spring offset (rig-local). Fire kicks saturate along their axis at
        /// max(this kick, sustainedKickCapMeters) — a single shot keeps its own kick, a sustained burst stays ≤ the cap.
        /// </summary>
        public void ApplyKicks(ref Vector3 kickOffsetLocal, Quaternion rigRotation)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            if (_pendingFireKick.sqrMagnitude <= 0f && _pendingEventKick.sqrMagnitude <= 0f) return;
            Quaternion inverse = Quaternion.Inverse(rigRotation);
            if (_pendingFireKick.sqrMagnitude > 0f)
            {
                Vector3 local = inverse * _pendingFireKick;
                float m = local.magnitude;
                Vector3 axis = local / m;
                float along = Vector3.Dot(kickOffsetLocal, axis);
                float cap = Mathf.Max(m, Profile.sustainedKickCapMeters);
                float next = Mathf.Max(along, Mathf.Min(along + m, cap));
                kickOffsetLocal += axis * (next - along);
            }

            if (_pendingEventKick.sqrMagnitude > 0f) kickOffsetLocal += inverse * _pendingEventKick;
            _pendingFireKick = Vector3.zero;
            _pendingEventKick = Vector3.zero;
        }

        /// <summary>
        /// The shake node: <paramref name="legacyLocal"/> (direct CameraShakeService requests) + K1 + K2 + K3 clamped to
        /// aggregateCapMeters, × the player's scale (0–1) × 0.3 in reduced motion; roll ≤ rollCapDegrees × the same scale;
        /// then K5 on the local +Z after the clamp (× the player's scale, 0 in reduced motion).
        /// </summary>
        public void Compose(Quaternion rigRotation, Vector3 legacyLocal, float legacyRoll, Vector3 kickLocal, float kickRoll,
            float playerScale, bool reducedMotion, float cameraDistance, out Vector3 nodeLocal, out float nodeRoll)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { nodeLocal = Vector3.zero; nodeRoll = 0f; Last = default; return; }
#endif
            FeelProfileTD p = Profile;
            float player = Mathf.Clamp01(playerScale);
            float scale = player * (reducedMotion ? CameraModesFeel.ReducedShakeScale : 1f);

            Vector3 fire = p.fireTrauma ? FireOffset(rigRotation, p) : Vector3.zero;
            float worldRoll = 0f;
            Vector3 world = p.worldTrauma ? WorldOffset(p, out worldRoll) : Vector3.zero;
            Vector3 kick = p.kick ? kickLocal : Vector3.zero;
            float kickRollDeg = p.kick ? kickRoll : 0f;

            Vector3 sum = Vector3.ClampMagnitude(legacyLocal + fire + world + kick, p.aggregateCapMeters) * scale;
            float rollCap = p.rollCapDegrees;
            float roll = Mathf.Clamp(legacyRoll + worldRoll + kickRollDeg, -rollCap, rollCap) * scale;

            float zoomFraction = reducedMotion || !p.zoomPunch ? 0f : Mathf.Clamp(_zoom, -ZoomCapFraction, ZoomCapFraction);
            float zoomMeters = -zoomFraction * Mathf.Max(0f, cameraDistance) * player;   // −.08 = 8 % closer = forward on +Z

            // §4.11 the last zombie's focus: the camera leans toward the kill point (aim-safe on the node, after the clamp — a framing
            // move, not a shake; the player's scale and reduced motion apply)
            Vector3 focus = reducedMotion || !p.lastZombieSlowMo ? Vector3.zero : _focusShift * player;

            nodeLocal = new Vector3(sum.x + focus.x, sum.y + focus.y, sum.z + zoomMeters + focus.z);
            nodeRoll = roll;
            Last.FocusMeters = focus.magnitude;

            Last.FireTrauma = _fireTrauma;
            Last.WorldTrauma = _worldTrauma;
            Last.FireMeters = fire.magnitude;
            Last.WorldMeters = world.magnitude;
            Last.KickMeters = kick.magnitude;
            Last.TotalMeters = sum.magnitude;
            Last.RollDegrees = roll;
            Last.ZoomMeters = zoomMeters;
        }

        private Vector3 _focusShift;

        /// <summary>
        /// §4.11: the last zombie's focus shift for this frame, camera-local (the rig converts the world offset — lastZombieFocusBlend
        /// of the way from the focus to the kill point, on the slow motion's envelope). Zero when no moment runs.
        /// </summary>
        public void SetFocusShift(Vector3 cameraLocal)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            _focusShift = cameraLocal;
        }

        /// <summary>K1 / K2 decay and the noise clock on SCALED time (a hit-stop freezes the frame, slow motion slows it).</summary>
        public void AdvanceTrauma(float dt)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.CameraModel);
            if (feelCostScope.Skip) { return; }
#endif
            if (dt <= 0f) return;
            FeelProfileTD p = Profile;
            _fireTrauma = Mathf.Max(0f, _fireTrauma - p.fireDecayPerSecond * dt);
            _worldTrauma = Mathf.Max(0f, _worldTrauma - p.worldDecayPerSecond * dt);
            _noiseClock += dt;
            if (_fireTrauma <= 0f && _worldTrauma <= 0f && _noiseClock > NoiseClockWrapSeconds) _noiseClock = 0f;   // float precision, invisible at T = 0
        }

        /// <summary>Everything back to rest (mode change, new rig).</summary>
        public void Reset()
        {
            _fireTrauma = 0f;
            _worldTrauma = 0f;
            _noiseClock = 0f;
            _focusShift = Vector3.zero;
            _pendingFireKick = Vector3.zero;
            _pendingEventKick = Vector3.zero;
            _leanPhase = LeanPhase.Idle;
            _fireHeld = false;
            _leanT = 0f;
            _leanFrom = 0f;
            _lean01 = 0f;
            _leanDirVelocity = Vector3.zero;
            _zoomPhase = ZoomPhase.Idle;
            _zoom = 0f;
            _zoomT = 0f;
            _zoomPeak = 0f;
            Last = default;
        }

        // ------------------------------------------------------------------ K1 / K2 sampling

        private Vector3 FireOffset(Quaternion rigRotation, FeelProfileTD p)
        {
            if (_fireTrauma <= 0f) return Vector3.zero;
            float amplitude = p.fireMaxOffsetMeters * Mathf.Pow(_fireTrauma, p.traumaExponent);
            float t = _noiseClock * p.fireNoiseHz;
            Vector2 n = Vector2.ClampMagnitude(new Vector2(Noise(t, FireSeedX), Noise(FireSeedY, t)), 1f);

            // The aim's direction on screen: along it ×1, across it × aimLateralShare (his sözleşmesi §1.2).
            Vector3 aimLocal = Quaternion.Inverse(rigRotation) * _fireAim;
            Vector2 axis = new Vector2(aimLocal.x, aimLocal.y);
            axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector2.up;
            Vector2 side = new Vector2(-axis.y, axis.x);
            Vector2 o = (axis * Vector2.Dot(n, axis) + side * (Vector2.Dot(n, side) * p.aimLateralShare)) * amplitude;
            return new Vector3(o.x, o.y, 0f);
        }

        private Vector3 WorldOffset(FeelProfileTD p, out float roll)
        {
            roll = 0f;
            if (_worldTrauma <= 0f) return Vector3.zero;
            float shaped = Mathf.Pow(_worldTrauma, p.traumaExponent);
            float t = _noiseClock * p.worldNoiseHz;
            Vector2 n = Vector2.ClampMagnitude(new Vector2(Noise(t, WorldSeedX), Noise(WorldSeedY, t)), 1f);
            roll = p.rollCapDegrees * shaped * Mathf.Clamp(Noise(t, WorldSeedRoll), -1f, 1f);
            float amplitude = p.worldMaxOffsetMeters * shaped;
            return new Vector3(n.x * amplitude, n.y * amplitude, 0f);
        }

        private static float Noise(float x, float y) => Mathf.PerlinNoise(x, y) * 2f - 1f;

        private static float AddTrauma(float t, float add, float cap)
        {
            if (add <= 0f) return t;
            return Mathf.Clamp01(Mathf.Max(t, Mathf.Min(t + add, Mathf.Clamp01(cap))));
        }

        // ------------------------------------------------------------------ K4 / K5 envelopes

        private void Press()
        {
            _fireHeld = true;
            if (_leanPhase == LeanPhase.In || _leanPhase == LeanPhase.Held) return;
            if (_leanPhase == LeanPhase.Idle)
            {
                _leanDir = _leanTarget;   // a fresh lean starts on the shot's line, not swept from an old one
                _leanDirVelocity = Vector3.zero;
            }

            _leanFrom = _lean01;
            _leanT = 0f;
            _leanPhase = LeanPhase.In;
        }

        private void Release()
        {
            _fireHeld = false;
            if (_leanPhase != LeanPhase.Held) return;   // still rising: the ramp completes, then holds (a tap leans once)
            _leanPhase = LeanPhase.Hold;
            _leanT = 0f;
        }

        private void TickLean(float dt, FeelProfileTD p, float aimSmoothSeconds)
        {
            switch (_leanPhase)
            {
                case LeanPhase.In:
                    _leanT += p.leanInSeconds > 0f ? dt / p.leanInSeconds : 1f;
                    if (_leanT >= 1f)
                    {
                        _lean01 = 1f;
                        _leanT = 0f;
                        _leanPhase = _fireHeld ? LeanPhase.Held : LeanPhase.Hold;
                    }
                    else
                    {
                        _lean01 = Mathf.Lerp(_leanFrom, 1f, Ease(TopDownFeelEase.OutCubic, _leanT));
                    }

                    break;
                case LeanPhase.Held:
                    _lean01 = 1f;
                    break;
                case LeanPhase.Hold:
                    _leanT += dt;
                    if (_leanT >= p.leanHoldSeconds)
                    {
                        _leanFrom = _lean01;
                        _leanT = 0f;
                        _leanPhase = LeanPhase.Out;
                    }

                    break;
                case LeanPhase.Out:
                    _leanT += p.leanOutSeconds > 0f ? dt / p.leanOutSeconds : 1f;
                    if (_leanT >= 1f)
                    {
                        _lean01 = 0f;
                        _leanT = 0f;
                        _leanPhase = LeanPhase.Idle;
                    }
                    else
                    {
                        _lean01 = Mathf.Lerp(_leanFrom, 0f, Ease(TopDownFeelEase.InOutSine, _leanT));
                    }

                    break;
            }

            // The lean follows the shots' line on the look-ahead's own smoothing (a turn while firing sweeps, never snaps).
            if (_leanPhase != LeanPhase.Idle)
            {
                _leanDir = Vector3.SmoothDamp(_leanDir, _leanTarget, ref _leanDirVelocity, Mathf.Max(0.01f, aimSmoothSeconds), Mathf.Infinity, dt);
            }
        }

        private void TickZoom(float dt)
        {
            switch (_zoomPhase)
            {
                case ZoomPhase.In:
                    _zoomT += _zoomIn > 0f ? dt / _zoomIn : 1f;
                    if (_zoomT >= 1f)
                    {
                        _zoom = _zoomPeak;
                        _zoomT = 0f;
                        _zoomPhase = _zoomHold > 0f ? ZoomPhase.Hold : ZoomPhase.Out;
                    }
                    else
                    {
                        _zoom = Mathf.Lerp(_zoomFrom, _zoomPeak, Ease(_zoomInEase, _zoomT));
                    }

                    break;
                case ZoomPhase.Hold:
                    _zoomT += dt;
                    if (_zoomT >= _zoomHold)
                    {
                        _zoomT = 0f;
                        _zoomPhase = ZoomPhase.Out;
                    }

                    break;
                case ZoomPhase.Out:
                    _zoomT += _zoomOut > 0f ? dt / _zoomOut : 1f;
                    if (_zoomT >= 1f)
                    {
                        _zoom = 0f;
                        _zoomT = 0f;
                        _zoomPhase = ZoomPhase.Idle;
                    }
                    else
                    {
                        _zoom = Mathf.Lerp(_zoomPeak, 0f, Ease(_zoomOutEase, _zoomT));
                    }

                    break;
            }
        }

        public static float Ease(TopDownFeelEase ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case TopDownFeelEase.OutQuad:
                    return 1f - (1f - t) * (1f - t);
                case TopDownFeelEase.OutCubic:
                {
                    float u = 1f - t;
                    return 1f - u * u * u;
                }
                case TopDownFeelEase.OutSine:
                    return Mathf.Sin(t * Mathf.PI * 0.5f);
                case TopDownFeelEase.InOutSine:
                    return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
                default:
                    return t;
            }
        }

        private static Vector3 Planar(Vector3 v)
        {
            v.y = 0f;
            float m = v.magnitude;
            return m > 1e-5f ? v / m : Vector3.zero;
        }
    }
}
