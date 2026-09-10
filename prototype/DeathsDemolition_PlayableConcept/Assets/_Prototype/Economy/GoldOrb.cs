using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// One gold pickup lying in the garden.
    ///
    /// Two of the three collection modes use this object and one does not:
    ///
    ///   DirectCredit    - no orb is ever created; the service credits on the death event.
    ///   ContactPickup   - the orb waits and is taken when the player walks over it.
    ///   ProximityMagnet - the orb waits until the player is inside the magnet radius,
    ///                     then accelerates at him and is taken on contact.
    ///
    /// The magnet radius, magnet speed, acceleration, lifetime and contact radius all
    /// come from <c>PrototypeConfig.Economy</c> through the service, re-read every
    /// frame, so switching mode or dragging the magnet radius in the F1 panel is felt by
    /// orbs that are already on the ground.
    ///
    /// Everything is scaled delta time: a paused game must not burn an orb's lifetime,
    /// and a frozen round-end must not let orbs drift toward the player.
    ///
    /// Built from primitives at spawn, like every other visual in this prototype. No
    /// authored prefab means no missing reference.
    /// </summary>
    public sealed class GoldOrb : PooledBehaviour
    {
        private GoldService _owner;
        private GoldOrbTuning _tuning;

        private Transform _visual;
        private Light _light;
        private bool _lightOwned;

        private int _amount;
        private float _age;
        private float _hopTime;
        private Vector3 _hopFrom;
        private Vector3 _hopTo;

        private bool _attracted;
        private float _magnetSpeed;

        private bool _collected;
        private float _flashTime;
        private float _bobPhase;
        private float _baseScale = 0.28f;

        /// <summary>Gold this orb is worth.</summary>
        public int Amount => _amount;

        /// <summary>True once the magnet has grabbed it. F1 readout.</summary>
        public bool IsAttracted => _attracted;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// The pooled template. Inactive, hidden and DontDestroyOnLoad, exactly like the
        /// Special Weapon pickup template it is modelled on.
        /// </summary>
        public static GoldOrb CreateTemplate()
        {
            var go = new GameObject("GoldOrb");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.NotEditable;
            return go.AddComponent<GoldOrb>();
        }

        /// <summary>Place a freshly pooled orb and start its hop.</summary>
        public void Begin(int amount, Vector3 deathPosition, GoldService owner, GoldOrbTuning tuning, bool withLight)
        {
            _owner = owner;
            _tuning = tuning ?? new GoldOrbTuning();
            _amount = Mathf.Max(0, amount);

            _age = 0f;
            _hopTime = 0f;
            _attracted = false;
            _magnetSpeed = 0f;
            _collected = false;
            _flashTime = 0f;
            _bobPhase = Random.value * Mathf.PI * 2f;

            _hopFrom = deathPosition + Vector3.up * 0.6f;

            Vector2 scatter = Random.insideUnitCircle.normalized *
                              Random.Range(_tuning.ScatterMin, _tuning.ScatterMax);
            _hopTo = deathPosition + new Vector3(scatter.x, _tuning.RestHeight, scatter.y);

            transform.position = _hopFrom;

            _baseScale = _tuning.ScaleForAmount(_amount);

            EnsureVisual();
            ApplyLight(withLight);

            if (_visual != null)
            {
                _visual.localScale = Vector3.one * _baseScale;
                _visual.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }
        }

        protected override void OnSpawned()
        {
            _collected = false;
            EnsureVisual();
        }

        protected override void OnDespawned()
        {
            if (_owner != null)
            {
                _owner.NotifyOrbReleased(this, _lightOwned);
            }

            _lightOwned = false;

            if (_light != null)
            {
                _light.enabled = false;
            }

            _owner = null;
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            // Scaled: pause and the round-end freeze stop the orb with everything else.
            float dt = Time.deltaTime;
            if (dt <= 0f || _tuning == null)
            {
                return;
            }

            if (_collected)
            {
                TickFlash(dt);
                return;
            }

            _age += dt;

            if (_hopTime < _tuning.HopDuration)
            {
                TickHop(dt);
            }
            else
            {
                TickIdleAndMagnet(dt);
            }

            if (_visual != null && _tuning.SpinSpeed > 0f)
            {
                _visual.Rotate(Vector3.up, _tuning.SpinSpeed * dt, Space.World);
            }

            if (_owner != null && _age >= _owner.OrbLifetime)
            {
                // Uncollected gold expires. Silently: a sound for gold the player never
                // reached would be a punishment cue, and the request never asks for one.
                Despawn();
            }
        }

        private void TickHop(float dt)
        {
            _hopTime += dt;

            float t = _tuning.HopDuration > 0f ? Mathf.Clamp01(_hopTime / _tuning.HopDuration) : 1f;
            Vector3 flat = Vector3.Lerp(_hopFrom, _hopTo, t);

            // Parabola, not a curve asset: 4t(1-t) peaks at 1 in the middle and is 0 at
            // both ends, so the arc lands exactly on the resting point.
            flat.y += _tuning.HopHeight * 4f * t * (1f - t);

            transform.position = flat;
        }

        private void TickIdleAndMagnet(float dt)
        {
            if (_owner == null)
            {
                return;
            }

            bool hasPlayer = _owner.TryGetPlayerPoint(out Vector3 player);
            PrototypeGoldCollectionMode mode = _owner.Mode;

            if (!hasPlayer)
            {
                Bob(dt);
                return;
            }

            Vector3 delta = player - transform.position;
            float distanceSqr = delta.sqrMagnitude;

            float capture = _owner.ContactRadius;
            if (distanceSqr <= capture * capture)
            {
                Collect();
                return;
            }

            if (mode != PrototypeGoldCollectionMode.ProximityMagnet)
            {
                // ContactPickup: no attraction at all, the player has to walk over it.
                Bob(dt);
                return;
            }

            float magnet = _owner.MagnetRadius;
            if (!_attracted && distanceSqr <= magnet * magnet)
            {
                _attracted = true;
                _magnetSpeed = 0f;
            }

            if (!_attracted)
            {
                Bob(dt);
                return;
            }

            // "Pulled hard" (request): accelerate up to the magnet speed rather than
            // teleporting, so the pull reads as a decision the orb made.
            _magnetSpeed = Mathf.Min(_owner.MagnetSpeed, _magnetSpeed + _owner.MagnetAcceleration * dt);
            transform.position = Vector3.MoveTowards(transform.position, player, _magnetSpeed * dt);
        }

        private void Bob(float dt)
        {
            if (_visual == null || _tuning.BobAmplitude <= 0f)
            {
                return;
            }

            _bobPhase += dt * _tuning.BobFrequency * Mathf.PI * 2f;
            _visual.localPosition = new Vector3(0f, Mathf.Sin(_bobPhase) * _tuning.BobAmplitude, 0f);
        }

        private void Collect()
        {
            if (_collected)
            {
                return;
            }

            _collected = true;
            _flashTime = 0f;

            // Credit first, animate second. If anything below throws, the player still
            // has the gold.
            _owner?.OnOrbCollected(_amount, transform.position);
        }

        private void TickFlash(float dt)
        {
            _flashTime += dt;

            float duration = Mathf.Max(0.01f, _tuning.FlashDuration);
            float t = Mathf.Clamp01(_flashTime / duration);

            if (_visual != null)
            {
                _visual.localScale = Vector3.one * (_baseScale * Mathf.Lerp(1f, _tuning.FlashScale, t));
            }

            if (_light != null && _light.enabled)
            {
                _light.intensity = _tuning.LightIntensity * Mathf.Lerp(2.2f, 0f, t);
                _light.range = _tuning.LightRange * Mathf.Lerp(1.6f, 0.4f, t);
            }

            if (t >= 1f)
            {
                Despawn();
            }
        }

        // ------------------------------------------------------------------ visuals

        private void EnsureVisual()
        {
            if (_visual != null)
            {
                return;
            }

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Visual";

            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            _visual = sphere.transform;
            _visual.SetParent(transform, false);
            _visual.localPosition = Vector3.zero;
            _visual.localScale = Vector3.one * _baseScale;

            var renderer = sphere.GetComponent<MeshRenderer>();

            // Additive so gold separates from wet stone and moss under the overcast
            // lighting without being a neon ball (request: clearly readable, not neon).
            renderer.sharedMaterial = FeedbackMaterials.Additive(_tuning != null ? _tuning.Color : Color.yellow);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var lightObject = new GameObject("Glow");
            lightObject.transform.SetParent(transform, false);

            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = _tuning != null ? _tuning.Color : Color.yellow;
            _light.enabled = false;
            _light.shadows = LightShadows.None;
        }

        private void ApplyLight(bool withLight)
        {
            _lightOwned = withLight;

            if (_light == null)
            {
                return;
            }

            _light.enabled = withLight;
            _light.range = _tuning.LightRange;
            _light.intensity = _tuning.LightIntensity;
            _light.color = _tuning.Color;
        }
    }
}
