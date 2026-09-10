using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The workhorse: one particle burst, recoloured and rescaled per spawn.
    ///
    /// Impact sparks, blood, armour shards, the armour-break plate pop and the small
    /// explosion all use this one type. Recolouring is done through
    /// <c>ParticleSystem.MainModule.startColor</c>, which is per-system state and
    /// therefore does not instance a material - eight simultaneous bursts still share
    /// one material.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class BurstEffect : PooledEffect
    {
        private ParticleSystem _particles;
        private ParticleSystem.MainModule _main;
        private ParticleSystem.EmissionModule _emission;
        private bool _cached;

        // The template's authored size and speed. Captured before the first Play, because
        // startSizeMultiplier on a constant curve *replaces* the constant rather than
        // scaling it - so "scale" has to be applied against a remembered base or every
        // burst would come out the same size regardless of the template.
        private float _baseSize = 0.1f;
        private float _baseSpeed = 3f;

        private void CacheModules()
        {
            if (_cached)
            {
                return;
            }

            _cached = true;

            _particles = GetComponent<ParticleSystem>();
            if (_particles == null)
            {
                return;
            }

            _main = _particles.main;
            _emission = _particles.emission;

            _baseSize = Mathf.Max(0.001f, _main.startSizeMultiplier);
            _baseSpeed = Mathf.Max(0.001f, _main.startSpeedMultiplier);
        }

        /// <summary>
        /// Configure and fire. Call immediately after taking the instance out of the pool.
        /// </summary>
        /// <param name="color">Start colour of the particles.</param>
        /// <param name="scale">Uniform scale multiplier on size and speed.</param>
        /// <param name="count">Particles to emit. Clamped by the template's own burst.</param>
        /// <param name="lifetime">Seconds before the instance returns to the pool.</param>
        public void Play(Color color, float scale, int count, float lifetime)
        {
            CacheModules();

            Lifetime = Mathf.Max(0.05f, lifetime);

            if (_particles == null)
            {
                return;
            }

            float safeScale = Mathf.Max(0.05f, scale);

            _main.startColor = color;
            _main.startSizeMultiplier = _baseSize * safeScale;
            _main.startSpeedMultiplier = _baseSpeed * safeScale;
            _main.startLifetimeMultiplier = Lifetime * 0.85f;

            // Rebuilding a Burst[] every spawn would allocate; the burst count is a
            // scalar on the existing burst instead.
            if (_emission.burstCount > 0)
            {
                ParticleSystem.Burst burst = _emission.GetBurst(0);
                burst.count = new ParticleSystem.MinMaxCurve(Mathf.Max(1, count));
                _emission.SetBurst(0, burst);
            }

            _particles.Clear(true);
            _particles.Play(true);
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();
            CacheModules();
        }

        protected override void OnDespawned()
        {
            if (_particles != null)
            {
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
