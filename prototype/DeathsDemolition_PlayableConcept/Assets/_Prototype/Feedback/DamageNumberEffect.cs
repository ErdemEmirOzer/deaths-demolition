using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// A floating damage readout.
    ///
    /// It merges rather than multiplies: a shotgun blast that lands eight pellets on
    /// one torso produces one number that counts up to 56, not eight numbers fighting
    /// for the same pixel. <see cref="Add"/> is what the feedback director calls for
    /// every later hit inside the merge window.
    ///
    /// The whole thing self-disables when no font can be resolved on the machine, which
    /// keeps the "no console errors during normal use" criterion safe on a build with
    /// no font assets.
    /// </summary>
    public sealed class DamageNumberEffect : PooledEffect
    {
        private TextMesh _text;
        private Renderer _renderer;
        private Transform _tf;

        private float _amount;
        private Color _color;
        private Vector3 _anchor;
        private Vector3 _drift;
        private float _popTimer;

        /// <summary>Identity of the target this number belongs to, for merging. 0 means unowned.</summary>
        public int TargetId { get; private set; }

        /// <summary>Seconds since the last <see cref="Add"/>. The director uses it to decide on merging.</summary>
        public float TimeSinceLastAdd { get; private set; }

        private void Cache()
        {
            if (_tf != null)
            {
                return;
            }

            _tf = transform;
            _text = GetComponentInChildren<TextMesh>();
            _renderer = _text != null ? _text.GetComponent<Renderer>() : null;
        }

        public void Play(int targetId, Vector3 worldPosition, float amount, Color color, float lifetime)
        {
            Cache();

            TargetId = targetId;
            _anchor = worldPosition;
            _amount = amount;
            _color = color;
            _popTimer = 0f;
            TimeSinceLastAdd = 0f;
            Lifetime = Mathf.Max(0.2f, lifetime);

            // PROTOTYPE-ONLY drift: mostly up, slightly sideways so two numbers on
            // neighbouring enemies do not overlap exactly.
            _drift = new Vector3(Random.Range(-0.35f, 0.35f), 1.4f, Random.Range(-0.2f, 0.2f));

            Refresh();
        }

        /// <summary>Fold another hit on the same target into this number and re-pop it.</summary>
        public void Add(float amount, Color color, Vector3 worldPosition)
        {
            _amount += amount;
            _color = color;
            _anchor = Vector3.Lerp(_anchor, worldPosition, 0.35f);
            _popTimer = 0.12f;
            TimeSinceLastAdd = 0f;

            // Adding resets the clock so a sustained burst keeps one number alive.
            ResetAge();
            Refresh();
        }

        private void ResetAge()
        {
            RestartLife();
        }

        private void Refresh()
        {
            if (_text == null)
            {
                return;
            }

            int rounded = Mathf.Max(1, Mathf.RoundToInt(_amount));
            _text.text = rounded.ToString();
            _text.color = _color;
        }

        protected override void Tick(float t)
        {
            if (_tf == null)
            {
                return;
            }

            TimeSinceLastAdd += Time.deltaTime;

            _tf.position = _anchor + _drift * (t * 0.9f);

            Camera cam = FeedbackCameraRef.Current;
            if (cam != null)
            {
                _tf.rotation = cam.transform.rotation;
            }

            float pop = 1f;
            if (_popTimer > 0f)
            {
                _popTimer -= Time.deltaTime;
                pop = 1.25f;
            }

            // Fade out over the last 40% of the life.
            float alpha = t < 0.6f ? 1f : 1f - Mathf.InverseLerp(0.6f, 1f, t);
            _tf.localScale = Vector3.one * (pop * Mathf.Lerp(1f, 0.85f, t));

            if (_text != null)
            {
                Color c = _color;
                c.a = alpha;
                _text.color = c;
            }

            if (_renderer != null)
            {
                _renderer.enabled = alpha > 0.01f;
            }
        }

        protected override void OnDespawned()
        {
            TargetId = 0;
        }
    }
}
