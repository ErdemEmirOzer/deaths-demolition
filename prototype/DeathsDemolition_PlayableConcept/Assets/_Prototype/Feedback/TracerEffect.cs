using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// Step 4 of the hit chain: the readable line between muzzle and impact.
    ///
    /// The prototype resolves shots by raycast, so there is no travelling projectile to
    /// look at. A tracer that simply appears and fades reads as an instant hit, which is
    /// honest about what happened but gives the eye nothing to follow. So the line is
    /// drawn as a short segment that sweeps from muzzle to impact over
    /// <see cref="TravelTime"/> and then fades: the hit is still instant, the eye still
    /// gets a direction.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class TracerEffect : PooledEffect
    {
        /// <summary>PROTOTYPE-ONLY: seconds for the visual segment to cross the full distance.</summary>
        public const float TravelTime = 0.035f;

        private LineRenderer _line;
        private MaterialPropertyBlock _block;

        private Vector3 _from;
        private Vector3 _to;
        private Color _color;
        private float _segmentLength;
        private float _fadeStart;

        private void CacheLine()
        {
            if (_line != null)
            {
                return;
            }

            _line = GetComponent<LineRenderer>();
            _block = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Draw a tracer. <paramref name="width"/> is the line width in metres,
        /// <paramref name="lifetime"/> the total time on screen including the fade.
        /// </summary>
        public void Play(Vector3 from, Vector3 to, Color color, float width, float lifetime, float segmentLength)
        {
            CacheLine();

            _from = from;
            _to = to;
            _color = color;
            _segmentLength = Mathf.Max(0.5f, segmentLength);
            Lifetime = Mathf.Max(0.05f, lifetime);
            _fadeStart = Mathf.Clamp01(TravelTime / Lifetime);

            if (_line == null)
            {
                return;
            }

            _line.widthMultiplier = width;
            _line.positionCount = 2;
            _line.SetPosition(0, from);
            _line.SetPosition(1, from);

            ApplyColor(1f);
        }

        protected override void Tick(float t)
        {
            if (_line == null)
            {
                return;
            }

            float total = (_to - _from).magnitude;
            if (total <= 0.0001f)
            {
                return;
            }

            Vector3 dir = (_to - _from) / total;

            // Head sweeps out over TravelTime, then parks on the impact point.
            float travel01 = _fadeStart > 0f ? Mathf.Clamp01(t / _fadeStart) : 1f;
            float head = Mathf.Min(total, total * travel01);
            float tail = Mathf.Max(0f, head - _segmentLength);

            _line.SetPosition(0, _from + dir * tail);
            _line.SetPosition(1, _from + dir * head);

            float fade = t <= _fadeStart ? 1f : 1f - Mathf.InverseLerp(_fadeStart, 1f, t);
            ApplyColor(fade);
        }

        private void ApplyColor(float alpha)
        {
            if (_line == null)
            {
                return;
            }

            Color c = _color;
            c.a *= alpha;

            _line.GetPropertyBlock(_block);
            FeedbackMaterials.ApplyColor(_block, c);
            _line.SetPropertyBlock(_block);
        }

        protected override void OnDespawned()
        {
            if (_line != null)
            {
                _line.positionCount = 0;
            }
        }
    }
}
