using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-087 (C-00 06.10, council K/wp-087/konsey/KARAR-ArmsCarryKick-06.10.md) · the kick the weapon-first hold's arms carry: one
    /// way (back and muzzle-up never go below rest — the rig body's spring overshoots, ζ ≈ 0.5, and carried the pivot forward and
    /// muzzle-down out of the support hand's reach), critically damped, at full height on the shot's frame. One scale per shot keeps
    /// sustained fire's build-up (accumulation × the shot) under the absolute caps, so the per-shot pulse stays (no plateau, no
    /// per-frame clip). The step is exact, the same at any frame rate. Pure value state, no allocation.
    /// </summary>
    public struct HeldKickChannel
    {
        /// <summary>ω·t at which a critically damped return from rest velocity has 5 % left (x = (1 + ωt)·e^(−ωt)): ω = this / settle time.</summary>
        public const float SettleOmegaSeconds = 4.74f;

        private float _x, _v, _omega, _upPerMeter;

        /// <summary>Back along the aim (m, ≥ 0).</summary>
        public float Back => _x;

        /// <summary>Muzzle-up (°, ≥ 0).</summary>
        public float Degrees => _x * _upPerMeter;

        public bool AtRest => _x == 0f && _v == 0f;

        /// <summary>
        /// One shot: <paramref name="kick"/> m (the weapon's feel row), settling in <paramref name="settleRecoveries"/> ×
        /// <paramref name="recoverySeconds"/>, <paramref name="upPerMeter"/> °/m muzzle-up; the build-up stays ≤
        /// <paramref name="accumulation"/> × the scaled shot ≤ <paramref name="maxMeters"/> / <paramref name="maxDegrees"/>.
        /// </summary>
        public void Kick(float kick, float recoverySeconds, float upPerMeter, float accumulation, float maxMeters, float maxDegrees, float settleRecoveries)
        {
            _upPerMeter = Mathf.Max(0f, upPerMeter);
            _omega = SettleOmegaSeconds / (Mathf.Max(0.5f, settleRecoveries) * Mathf.Clamp(recoverySeconds, 0.02f, 1f));
            if (kick <= 0f) return;
            float acc = Mathf.Max(1f, accumulation);
            float scale = Mathf.Min(1f, Mathf.Max(0f, maxMeters) / (acc * kick));
            if (_upPerMeter > 0f) scale = Mathf.Min(scale, Mathf.Max(0f, maxDegrees) / (acc * kick * _upPerMeter));
            float shot = kick * scale;
            _x = Mathf.Min(_x + shot, acc * shot);
            _v = Mathf.Max(_v, -_omega * _x);   // v ≥ −ω·x: the critically damped return never crosses rest
        }

        /// <summary>The exact critically damped step: x = (x0 + (v0 + ω·x0)·t)·e^(−ωt).</summary>
        public void Step(float dt)
        {
            if (dt <= 0f || (_x == 0f && _v == 0f)) return;
            float e = Mathf.Exp(-_omega * dt), a = _v + _omega * _x;
            _x = (_x + a * dt) * e;
            _v = (_v - _omega * a * dt) * e;
            if (_x < 0f) _x = 0f;   // float rounding only (a ≥ 0 keeps it ≥ 0)
            if (_x < 1e-4f && Mathf.Abs(_v) < 1e-3f) Reset();
        }

        public void Reset()
        {
            _x = 0f;
            _v = 0f;
        }
    }
}
