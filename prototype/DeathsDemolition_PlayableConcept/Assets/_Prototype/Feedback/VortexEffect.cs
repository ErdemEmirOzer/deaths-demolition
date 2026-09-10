using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The Tide Jewel's well: a column of drawn-in water with a spinning surface ring and
    /// a light that brightens as the well winds up to implode.
    ///
    /// Like <see cref="BeaconEffect"/> and unlike everything else in this folder, it is
    /// NOT fire-and-forget: the well owns it and releases it, so its lifetime is
    /// effectively infinite and its animation is driven from outside through
    /// <see cref="SetCharge"/>. That is deliberate - the visual has to be a function of
    /// how close the implosion is, and only the well knows that.
    ///
    /// WHY THE RINGS CONTRACT INSTEAD OF EXPANDING
    /// Every other effect in this prototype grows. This one shrinks, on purpose: an
    /// inward motion is the only unambiguous way to say "you are being pulled" without a
    /// UI label, and it is what stops the well being mistaken for the Kagura Bell's
    /// outward front.
    /// </summary>
    public sealed class VortexEffect : PooledEffect
    {
        /// <summary>PROTOTYPE-ONLY: vertices per ring.</summary>
        public const int Segments = 32;

        /// <summary>PROTOTYPE-ONLY: how many concentric rings fall inward at once.</summary>
        public const int Rings = 3;

        /// <summary>PROTOTYPE-ONLY: metres above the ground the surface rings sit at.</summary>
        public const float GroundOffset = 0.1f;

        /// <summary>PROTOTYPE-ONLY: revolutions per second of the swirl.</summary>
        public const float SpinHz = 0.85f;

        /// <summary>PROTOTYPE-ONLY: inward falls per second.</summary>
        public const float FallHz = 1.6f;

        private readonly LineRenderer[] _rings = new LineRenderer[Rings];

        private Transform _core;
        private Renderer _coreRenderer;
        private Light _light;
        private MaterialPropertyBlock _block;

        private Color _color = new Color(0.55f, 0.78f, 0.86f, 0.85f);
        private float _radius = 7f;
        private float _charge;
        private float _spin;
        private float _fall;
        private bool _cached;

        private void Cache()
        {
            if (_cached)
            {
                return;
            }

            _cached = true;
            _block = new MaterialPropertyBlock();
            _light = GetComponent<Light>();

            int found = 0;
            for (int i = 0; i < transform.childCount && found < Rings; i++)
            {
                LineRenderer line = transform.GetChild(i).GetComponent<LineRenderer>();
                if (line == null)
                {
                    continue;
                }

                _rings[found] = line;
                found++;
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<LineRenderer>() != null)
                {
                    continue;
                }

                _core = child;
                _coreRenderer = child.GetComponent<Renderer>();
                break;
            }
        }

        /// <summary>Open the well. It stays until the owner despawns it.</summary>
        public void Begin(float radius, Color color)
        {
            Cache();

            _radius = Mathf.Max(0.5f, radius);
            _color = color;
            _charge = 0f;
            _spin = 0f;
            _fall = 0f;

            Lifetime = float.MaxValue;

            if (_light != null)
            {
                _light.enabled = true;
                _light.range = _radius * 1.4f;
                _light.color = color;
            }

            Apply();
        }

        /// <summary>0 the moment it opens, 1 the instant before it implodes.</summary>
        public void SetCharge(float charge01)
        {
            _charge = Mathf.Clamp01(charge01);
        }

        protected override void Tick(float t)
        {
            Cache();

            float dt = Time.deltaTime;
            _spin += SpinHz * dt;

            // The fall accelerates with the charge, so the well visibly winds UP rather
            // than merely staying open for a fixed time.
            _fall += FallHz * Mathf.Lerp(1f, 2.6f, _charge) * dt;

            Apply();
        }

        private void Apply()
        {
            float twist = _spin * Mathf.PI * 2f;

            for (int i = 0; i < Rings; i++)
            {
                LineRenderer ring = _rings[i];
                if (ring == null)
                {
                    continue;
                }

                // Each ring is offset a third of a cycle, so one is always near the rim
                // and one is always near the core: the inward motion never has a gap.
                float phase = _fall + i / (float)Rings;
                float fraction = phase - Mathf.Floor(phase);

                // Falls from the rim to the centre, so 1 - fraction is the radius.
                float radius = _radius * (1f - fraction);
                float alpha = Mathf.Sin(fraction * Mathf.PI);

                DrawRing(ring, radius, twist + fraction * 1.4f, alpha);
            }

            if (_core != null)
            {
                float pull = 0.55f + 0.45f * _charge;
                float breathe = 1f + 0.12f * Mathf.Sin(_spin * Mathf.PI * 4f);
                _core.localScale = new Vector3(pull * breathe, pull * 1.6f, pull * breathe);
            }

            Color core = Color.Lerp(_color, new Color(1f, 0.92f, 0.74f, 1f), _charge * 0.65f);
            SetColor(_coreRenderer, core);

            if (_light != null)
            {
                _light.color = core;
                _light.intensity = Mathf.Lerp(1.4f, 5.2f, _charge);
            }
        }

        private void DrawRing(LineRenderer ring, float radius, float twist, float alpha)
        {
            if (ring.positionCount != Segments)
            {
                ring.positionCount = Segments;
            }

            for (int i = 0; i < Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f + twist;
                ring.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    GroundOffset,
                    Mathf.Sin(angle) * radius));
            }

            Color c = _color;
            c.a *= Mathf.Clamp01(alpha) * Mathf.Lerp(0.7f, 1f, _charge);

            ring.GetPropertyBlock(_block);
            FeedbackMaterials.ApplyColor(_block, c);
            ring.SetPropertyBlock(_block);
        }

        private void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetPropertyBlock(_block);
            FeedbackMaterials.ApplyColor(_block, color);
            renderer.SetPropertyBlock(_block);
        }

        protected override void OnDespawned()
        {
            if (_light != null)
            {
                _light.enabled = false;
            }

            for (int i = 0; i < Rings; i++)
            {
                if (_rings[i] != null)
                {
                    _rings[i].positionCount = 0;
                }
            }

            _charge = 0f;
        }
    }
}
