using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The "readable thin connection" the request asks for on the Chain upgrade, and
    /// also the ricochet arc.
    ///
    /// It is deliberately thin and short-lived. A chain at tier 3 fires three of these
    /// in one frame; anything thicker turns a crowd into a light show and costs exactly
    /// the readability REQ-128 is protecting.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class LinkEffect : PooledEffect
    {
        private const int Segments = 6;

        private LineRenderer _line;
        private MaterialPropertyBlock _block;

        private Vector3 _from;
        private Vector3 _to;
        private Color _color;
        private float _jitter;

        private void CacheLine()
        {
            if (_line != null)
            {
                return;
            }

            _line = GetComponent<LineRenderer>();
            _block = new MaterialPropertyBlock();
        }

        public void Play(Vector3 from, Vector3 to, Color color, float width, float jitter, float lifetime)
        {
            CacheLine();

            _from = from;
            _to = to;
            _color = color;
            _jitter = jitter;
            Lifetime = Mathf.Max(0.04f, lifetime);

            if (_line == null)
            {
                return;
            }

            _line.widthMultiplier = width;
            _line.positionCount = Segments;
            BuildPoints();
            ApplyColor(1f);
        }

        private void BuildPoints()
        {
            Vector3 delta = _to - _from;
            Vector3 side = Vector3.Cross(delta.normalized, Vector3.up);
            if (side.sqrMagnitude < 0.0001f)
            {
                side = Vector3.right;
            }

            side.Normalize();

            for (int i = 0; i < Segments; i++)
            {
                float t = i / (float)(Segments - 1);
                Vector3 p = _from + delta * t;

                // Endpoints stay clean so the link visibly starts and ends on a target.
                if (i > 0 && i < Segments - 1)
                {
                    p += side * Random.Range(-_jitter, _jitter);
                    p += Vector3.up * Random.Range(-_jitter * 0.5f, _jitter * 0.5f);
                }

                _line.SetPosition(i, p);
            }
        }

        protected override void Tick(float t)
        {
            ApplyColor(1f - t);
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
