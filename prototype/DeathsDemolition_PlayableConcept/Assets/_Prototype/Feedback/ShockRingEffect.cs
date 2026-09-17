using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The Kagura Bell's pressure front: a ring on the ground that expands away from the
    /// player and fades as it goes.
    ///
    /// WHY A LINE LOOP AND NOT A PARTICLE BURST
    /// A burst is a cloud, and a cloud has no edge. This weapon's whole read is WHERE THE
    /// FRONT IS RIGHT NOW - the player has to be able to see it about to reach a body, so
    /// that when the body is thrown the two events are obviously the same event. A closed
    /// LineRenderer has exactly one unambiguous edge, costs one draw call, and stays
    /// readable behind forty enemies because it is a line rather than fill.
    ///
    /// TWO RINGS, ONE OBJECT: a bright leading edge plus a wider, dimmer trailing echo.
    /// This is the cheapest thing that makes the front read as HAVING A DIRECTION rather
    /// than as a circle that happens to be growing.
    /// </summary>
    public sealed class ShockRingEffect : PooledEffect
    {
        /// <summary>
        /// PROTOTYPE-ONLY: vertices in the loop. 40 is where the polygon stops reading as
        /// a polygon at the 9.5 m radius this weapon uses.
        /// </summary>
        // WP-047 AD M3: 40 read as a polygon at the 4 m front under the AP-014 sheet; 64 keeps the edge round.
        public const int Segments = 64;

        /// <summary>PROTOTYPE-ONLY: metres above ground, so the ring never z-fights the floor.</summary>
        public const float GroundOffset = 0.12f;

        private LineRenderer _lead;
        private LineRenderer _echo;
        private MaterialPropertyBlock _block;

        private Color _color = Color.white;
        private float _radius = 9.5f;
        private float _width = 0.16f;
        private bool _cached;

        private void Cache()
        {
            if (_cached)
            {
                return;
            }

            _cached = true;
            _block = new MaterialPropertyBlock();

            if (transform.childCount >= 2)
            {
                _lead = transform.GetChild(0).GetComponent<LineRenderer>();
                _echo = transform.GetChild(1).GetComponent<LineRenderer>();
            }
        }

        /// <summary>Start a front at this position. It expands to <paramref name="radius"/> over its lifetime.</summary>
        public void Play(Vector3 origin, float radius, float travelSeconds, Color color, float width)
        {
            Cache();

            transform.position = origin + Vector3.up * GroundOffset;

            _radius = Mathf.Max(0.5f, radius);
            _color = color;
            _width = Mathf.Max(0.02f, width);

            // Outlives the damage front by a little so the ring is still visible on the
            // frame the bodies actually move. Without that overlap the cause has already
            // vanished when the effect appears.
            Lifetime = Mathf.Max(0.08f, travelSeconds) * 1.35f;

            Apply(0f);
        }

        protected override void Tick(float t)
        {
            Apply(t);
        }

        private void Apply(float t)
        {
            Cache();

            // Ease-out: the front is fastest in its first metres, which is where it has to
            // read as an impulse leaving the bell rather than as a growing circle.
            float eased = 1f - (1f - t) * (1f - t);

            float leadRadius = _radius * eased;
            float echoRadius = _radius * eased * 0.82f;

            float fade = 1f - t;

            Draw(_lead, leadRadius, _width, _color, fade);
            Draw(_echo, echoRadius, _width * 0.55f, _color, fade * 0.45f);
        }

        private void Draw(LineRenderer line, float radius, float width, Color color, float alpha)
        {
            if (line == null)
            {
                return;
            }

            line.widthMultiplier = width;

            if (line.positionCount != Segments)
            {
                line.positionCount = Segments;
            }

            for (int i = 0; i < Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            Color c = color;
            c.a *= Mathf.Clamp01(alpha);

            line.GetPropertyBlock(_block);
            FeedbackMaterials.ApplyColor(_block, c);
            line.SetPropertyBlock(_block);
        }

        protected override void OnDespawned()
        {
            if (_lead != null)
            {
                _lead.positionCount = 0;
            }

            if (_echo != null)
            {
                _echo.positionCount = 0;
            }
        }
    }
}
