using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-076 B7 · §6 per-body rear awareness (S-01 WP-070 A-7). A body is "rear" when it is out of the view
    /// (CameraView.IsInView at the chest, mode margin) and in the rear half-plane of the view; without a rig, the mode's
    /// fallback angle. Behind the player it raises one <c>rear_threat</c> beat per approach when it comes within
    /// <see cref="ThreatSettings.RearBeatMetres"/> (Special / Elite / Ultra: <see cref="ThreatSettings.RearBeatMetresHeavy"/>);
    /// it re-arms beyond <see cref="ThreatSettings.RearBeatRearmMetres"/> or once it is seen (so a player who turns away
    /// from a close body warns at once). The fairness gate: a rear body starts no swing until
    /// <see cref="ThreatSettings.RearWarnLeadSeconds"/> after its beat (FP 0.8 s, TP 0.5 s; a wind-up already running is
    /// never cut). Top-down raises nothing (REQ-105 as before). No stealth behaviour is added.
    /// </summary>
    public sealed partial class ZombieEnemy
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076 §6: chest height (m) of the rear visibility test.", true)]
        public const float RearChestHeight = 1.2f;

        private bool _rearArmed = true, _isRear;
        private float _rearWarnedAt = -1f;

        public static int RearBeats { get; private set; }
        public static int RearLeadHolds { get; private set; }

        /// <summary>Behind the view this frame (readout).</summary>
        public bool IsRearOfView => _isRear;
        /// <summary>Time of this approach's rear_threat beat (−1 = none).</summary>
        public float RearWarnedAt => _rearWarnedAt;

        /// <summary>Per frame from TickCombat; returns false while the fairness lead still holds the swing.</summary>
        private bool TickRear(Transform target, float distance)
        {
            int mode = (int)CameraModeService.Current;
            if (mode == (int)CameraMode.TopDown || target == null)
            {
                _isRear = false; _rearArmed = true; _rearWarnedAt = -1f;
                return true;
            }

            ThreatSettings s = ThreatSettings.Active;
            if (distance > s.RearBeatRearmMetres)
            {
                _isRear = false; _rearArmed = true; _rearWarnedAt = -1f;
                return true;
            }

            Vector3 chest = transform.position + Vector3.up * RearChestHeight;
            Vector3 fwd = CameraView.Forward; fwd.y = 0f;
            Vector3 toBody = transform.position - target.position; toBody.y = 0f;
            bool behindPlane = fwd.sqrMagnitude > 1e-6f && toBody.sqrMagnitude > 1e-6f && Vector3.Dot(fwd, toBody) < 0f;
            if (PrototypeCameraRig.Active != null)
                _isRear = behindPlane && !CameraView.IsInView(chest, s.ByMode(s.RearViewMarginByMode, mode, 0f));
            else
                _isRear = toBody.sqrMagnitude > 1e-6f && Vector3.Angle(fwd.sqrMagnitude > 1e-6f ? fwd : Vector3.forward, toBody) > s.ByMode(s.RearAngleByMode, mode, 100f);

            if (!_isRear)
            {
                _rearArmed = true;   // seen: the next time it is left behind it warns again
                return true;
            }

            float raise = _tier == EnemyTier.Standard ? s.RearBeatMetres : s.RearBeatMetresHeavy;
            if (_rearArmed && distance <= raise)
            {
                _rearArmed = false;
                _rearWarnedAt = Time.time;
                RearBeats++;
                float toSwing = Mathf.Max(0f, (distance - (_attack != null ? _attack.Range : 1.7f)) / Mathf.Max(0.1f, _moveSpeed)) + (_attack != null ? _attack.TelegraphSeconds : 0f);
                Vector3 dir = -toBody; dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
                RaiseBeat(EnemyCombatEventKind.RearThreat, chest, dir, ArmorSlot.None, _animator.Driven, toSwing);
            }

            float lead = s.ByMode(s.RearWarnLeadSeconds, mode, 0f);
            if (lead <= 0f) return true;
            if (_rearWarnedAt < 0f) return true;   // not yet inside the beat range: nothing it could reach from there
            bool ok = Time.time - _rearWarnedAt >= lead;
            if (!ok) RearLeadHolds++;
            return ok;
        }

        private void ResetRear() { _rearArmed = true; _isRear = false; _rearWarnedAt = -1f; }

        public static void ResetRearStatics() { RearBeats = 0; RearLeadHolds = 0; }
    }
}
