using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// Step 2 of the hit chain. A billboarded additive quad plus one short-range point
    /// light, parented to the muzzle so it tracks the weapon during the recoil kick
    /// instead of hanging in the air behind it.
    ///
    /// One light, not one per shot: the flash is pooled and the pool ceiling for it is
    /// small, so a 15/s rotary cannon never has more than a couple of lights alive.
    /// </summary>
    public sealed class MuzzleFlashEffect : PooledEffect
    {
        private Transform _quad;
        private Renderer _renderer;
        private Light _light;
        private MaterialPropertyBlock _block;

        private Color _color;
        private float _baseScale;
        private float _baseIntensity;
        private bool _cached;

        private void Cache()
        {
            if (_cached)
            {
                return;
            }

            _cached = true;
            _quad = transform.childCount > 0 ? transform.GetChild(0) : null;
            _renderer = _quad != null ? _quad.GetComponent<Renderer>() : null;
            _light = GetComponent<Light>();
            _block = new MaterialPropertyBlock();
        }

        /// <summary>Fire the flash. <paramref name="scale"/> is the weapon's flash size in metres.</summary>
        public void Play(Color color, float scale, float lightIntensity, float lifetime)
        {
            Cache();

            _color = color;
            _baseScale = Mathf.Max(0.02f, scale);
            _baseIntensity = Mathf.Max(0f, lightIntensity);
            Lifetime = Mathf.Max(0.02f, lifetime);

            if (_quad != null)
            {
                // A little random roll so a held trigger does not look like one frozen sprite.
                _quad.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                _quad.localScale = Vector3.one * _baseScale;
            }

            if (_light != null)
            {
                _light.enabled = _baseIntensity > 0f;
                _light.intensity = _baseIntensity;
                _light.color = color;
                _light.range = Mathf.Max(1.5f, _baseScale * 8f);
            }

            Tick(0f);
        }

        protected override void Tick(float t)
        {
            float fade = 1f - t;

            if (_quad != null)
            {
                _quad.localScale = Vector3.one * (_baseScale * (0.75f + 0.35f * fade));

                if (_renderer != null)
                {
                    Color c = _color;
                    c.a *= fade;
                    _renderer.GetPropertyBlock(_block);
                    FeedbackMaterials.ApplyColor(_block, c);
                    _renderer.SetPropertyBlock(_block);
                }
            }

            if (_light != null && _light.enabled)
            {
                _light.intensity = _baseIntensity * fade * fade;
            }
        }

        protected override void OnDespawned()
        {
            if (_light != null)
            {
                _light.enabled = false;
            }
        }
    }
}
