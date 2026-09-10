using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The light column that marks a Special Weapon on the ground (REQ-053).
    ///
    /// Unlike every other effect here it is not fire-and-forget: the pickup owns it for
    /// as long as the weapon is on the ground and releases it explicitly. So its
    /// lifetime is effectively infinite and <see cref="SetUrgency"/> is what turns it
    /// from a calm marker into a warning as the ground timer runs out.
    ///
    /// G-21: IT NO LONGER OWNS ITS OWN RHYTHM.
    /// It used to pulse on <c>Time.time * rate</c>, with the rate derived from urgency.
    /// That was correct while the beacon was the only surface saying "hurry". It is not
    /// any more: the HUD marker now blinks too, and the numeric ground timer it used to
    /// sit next to is gone. Two surfaces running two clocks at the same NOMINAL rate
    /// still drift apart in phase, and a player reads the drift as two different events.
    /// So the owner integrates one phase (<see cref="GroundBlinkClock"/>) and pushes it
    /// here through <see cref="SetPulse"/>. Urgency now only chooses the COLOUR.
    /// </summary>
    public sealed class BeaconEffect : PooledEffect
    {
        private Transform _column;
        private Renderer _columnRenderer;
        private Light _light;
        private MaterialPropertyBlock _block;

        private Color _calmColor = new Color(1f, 0.82f, 0.35f, 0.35f);
        private Color _urgentColor = new Color(1f, 0.35f, 0.2f, 0.55f);
        private float _urgency;
        private float _pulse = 1f;
        private float _baseHeight = 12f;
        private float _baseRadius = 0.55f;
        private bool _cached;

        private void Cache()
        {
            if (_cached)
            {
                return;
            }

            _cached = true;
            _column = transform.childCount > 0 ? transform.GetChild(0) : null;
            _columnRenderer = _column != null ? _column.GetComponent<Renderer>() : null;
            _light = GetComponent<Light>();
            _block = new MaterialPropertyBlock();
        }

        /// <summary>Start the marker. It stays until <see cref="PooledEffect.Despawn"/> is called by the owner.</summary>
        public void Begin(float height, float radius, Color calm, Color urgent)
        {
            Cache();

            _baseHeight = Mathf.Max(1f, height);
            _baseRadius = Mathf.Max(0.05f, radius);
            _calmColor = calm;
            _urgentColor = urgent;
            _urgency = 0f;
            _pulse = 1f;

            // Effectively "never expires on its own".
            Lifetime = float.MaxValue;

            if (_column != null)
            {
                _column.localScale = new Vector3(_baseRadius * 2f, _baseHeight * 0.5f, _baseRadius * 2f);
                _column.localPosition = new Vector3(0f, _baseHeight * 0.5f, 0f);
            }

            if (_light != null)
            {
                _light.enabled = true;
                _light.range = _baseHeight;
                _light.color = calm;
            }

            Tick(0f);
        }

        /// <summary>0 = calm marker, 1 = about to disappear. Drives COLOUR only (G-21).</summary>
        public void SetUrgency(float urgency01)
        {
            _urgency = Mathf.Clamp01(urgency01);
        }

        /// <summary>
        /// The blink envelope for this frame, from the owner's integrated phase.
        /// Feed it <see cref="GroundBlinkClock.Intensity01"/>; anything else will read as
        /// a different beat from the HUD marker, which is the failure this exists to stop.
        /// </summary>
        public void SetPulse(float intensity01)
        {
            _pulse = Mathf.Clamp01(intensity01);
        }

        protected override void Tick(float t)
        {
            Cache();

            // The rhythm arrives from outside (see the class remarks). What is left here
            // is only the mapping from "how bright right now" to what the column does.
            float pulse = _pulse;

            Color c = Color.Lerp(_calmColor, _urgentColor, _urgency);
            c.a *= pulse;

            if (_columnRenderer != null)
            {
                _columnRenderer.GetPropertyBlock(_block);
                FeedbackMaterials.ApplyColor(_block, c);
                _columnRenderer.SetPropertyBlock(_block);
            }

            if (_column != null)
            {
                float r = _baseRadius * (0.85f + 0.35f * pulse) * 2f;
                _column.localScale = new Vector3(r, _baseHeight * 0.5f, r);
            }

            if (_light != null)
            {
                _light.color = Color.Lerp(_calmColor, _urgentColor, _urgency);
                _light.intensity = Mathf.Lerp(1.6f, 3.6f, _urgency) * pulse;
            }
        }

        protected override void OnDespawned()
        {
            if (_light != null)
            {
                _light.enabled = false;
            }

            _urgency = 0f;
            _pulse = 1f;
        }
    }
}
