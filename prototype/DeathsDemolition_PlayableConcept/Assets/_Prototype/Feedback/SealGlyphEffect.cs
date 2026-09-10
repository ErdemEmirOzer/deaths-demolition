using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The Ofuda Loom's talisman: a strip of paper stuck to an enemy, burning down until
    /// it either expires or is spent in a collapse.
    ///
    /// WHY IT FOLLOWS THE BODY INSTEAD OF BEING PARENTED TO IT
    /// The enemies are pooled. Parenting a pooled effect to a pooled enemy means the
    /// effect's despawn order is decided by the enemy's, and a body that returns to its
    /// pool mid-seal would drag a live effect into the enemy pool with it. Following a
    /// remembered transform and giving up the moment it goes null is the version that
    /// cannot corrupt either pool.
    ///
    /// It is deliberately a FLAT PAPER STRIP, not a glow. The seal has to be legible as a
    /// count - the player needs to know at a glance roughly how many are out there - and
    /// a rectangle of paper against a body reads as a countable object where a soft light
    /// reads as atmosphere.
    /// </summary>
    public sealed class SealGlyphEffect : PooledEffect
    {
        /// <summary>PROTOTYPE-ONLY: height above the target's feet, as a fraction of its height.</summary>
        public const float BodyFraction = 0.78f;

        /// <summary>PROTOTYPE-ONLY: metres the strip floats in front of the body so it never z-fights.</summary>
        public const float ForwardOffset = 0.18f;

        private Transform _paper;
        private Renderer _paperRenderer;
        private Renderer _bandRenderer;
        private MaterialPropertyBlock _block;

        private Transform _follow;
        private float _followHeight = 1.8f;
        private Color _color = Color.white;
        private bool _cached;

        /// <summary>The enemy id this seal belongs to. The registry's key, not used here.</summary>
        public int TargetId { get; private set; }

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
                _paper = transform.GetChild(0);
                _paperRenderer = _paper.GetComponent<Renderer>();
                _bandRenderer = transform.GetChild(1).GetComponent<Renderer>();
            }
        }

        /// <summary>Stick a seal on a body for <paramref name="seconds"/>.</summary>
        public void Play(Transform follow, float height, int targetId, float seconds, Color color)
        {
            Cache();

            _follow = follow;
            _followHeight = Mathf.Max(0.5f, height);
            _color = color;
            TargetId = targetId;
            Lifetime = Mathf.Max(0.1f, seconds);

            Track();
            Apply(0f);
        }

        /// <summary>
        /// Consume the seal early, because the web collapsed. Kept separate from
        /// <see cref="PooledEffect.Despawn"/> so the registry's intent is visible at the
        /// call site.
        /// </summary>
        public void Consume()
        {
            Despawn();
        }

        protected override void Tick(float t)
        {
            if (!Track())
            {
                Despawn();
                return;
            }

            Apply(t);
        }

        private bool Track()
        {
            if (_follow == null)
            {
                return false;
            }

            Vector3 position = _follow.position + Vector3.up * (_followHeight * BodyFraction);

            if (FeedbackCameraRef.TryGetPosition(out Vector3 viewer))
            {
                Vector3 toViewer = viewer - position;
                toViewer.y = 0f;

                if (toViewer.sqrMagnitude > 0.0001f)
                {
                    toViewer.Normalize();
                    position += toViewer * ForwardOffset;
                    transform.rotation = Quaternion.LookRotation(-toViewer, Vector3.up);
                }
            }

            transform.position = position;
            return true;
        }

        private void Apply(float t)
        {
            // The paper holds full strength and only gives up in its last fifth, so a seal
            // reads as "on" for almost its whole life. A seal that fades the whole way
            // would make the player second-guess a web that is still perfectly good.
            float fade = t < 0.8f ? 1f : Mathf.InverseLerp(1f, 0.8f, t);

            // The burning edge runs down the strip. This is the "it is going to expire"
            // channel, kept separate from alpha so both can be read at once.
            float burn = Mathf.Clamp01(1f - t);

            Color paper = _color;
            paper.a *= fade;
            SetColor(_paperRenderer, paper);

            Color band = Color.Lerp(new Color(1f, 0.42f, 0.16f, 1f), _color, burn);
            band.a *= fade;
            SetColor(_bandRenderer, band);

            if (_paper != null)
            {
                _paper.localScale = new Vector3(0.16f, 0.34f * Mathf.Lerp(0.55f, 1f, burn), 1f);
            }
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
            _follow = null;
            TargetId = 0;
        }
    }
}
