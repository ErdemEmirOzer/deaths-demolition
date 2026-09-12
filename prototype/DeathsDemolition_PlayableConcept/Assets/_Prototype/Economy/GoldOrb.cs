using DD.Prototype.Core.Pooling;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// One gold packet lying in the garden: the visible, collectable body of a kill's
    /// reward.
    ///
    /// The packet's OWNERSHIP lives in <see cref="GoldPacketLifecycle"/> (<see cref="Packet"/>)
    /// and is what the tests prove; this class is the presentation and the physics of
    /// it: the landing hop, the idle bob, the magnet pull, the collection flash, and the
    /// round-end sweep flight.
    ///
    ///   Spawned      the hop from the death point to the resting point (scaled time)
    ///   Waiting      lying on the ground; NEVER expires (WP-004: gold does not vanish)
    ///   Magnetizing  the player came within MagnetRadius: accelerate at him
    ///   Collected    taken on contact - credited first, then a short flash, then the pool
    ///   AutoCollected the round-end sweep took it - credited first, then a short flight to
    ///                the player on UNSCALED time (the world is frozen), then the pool
    ///
    /// Everything before collection is scaled delta time: a paused game must not move a
    /// packet, and a frozen round end must not let packets drift toward the player. Only
    /// the sweep flight runs on unscaled time, because it happens under the freeze and
    /// the gold it shows is already in the wallet.
    ///
    /// Built from primitives at spawn, like every other visual in this prototype. No
    /// authored prefab means no missing reference.
    /// </summary>
    public sealed class GoldOrb : PooledBehaviour
    {
        private readonly GoldPacketLifecycle _packet = new GoldPacketLifecycle();

        private GoldService _owner;
        private GoldOrbTuning _tuning;

        private Transform _visual;
        private MeshRenderer _renderer;
        private Light _light;
        private bool _lightOwned;
        private Color _visualColor;

        private float _hopTime;
        private Vector3 _hopFrom;
        private Vector3 _hopTo;

        private float _magnetSpeed;

        private float _flashTime;
        private float _flightTime;
        private Vector3 _flightFrom;
        private float _bobPhase;
        private float _baseScale = 0.28f;

        /// <summary>Ownership state of this packet. The wallet-facing truth.</summary>
        public GoldPacketLifecycle Packet => _packet;

        /// <summary>Gold this packet is worth. 0 once recycled.</summary>
        public int Amount => _packet.Amount;

        /// <summary>True once the magnet has grabbed it. F1 readout.</summary>
        public bool IsAttracted => _packet.State == GoldPacketState.Magnetizing;

        /// <summary>True while this packet holds one of the light budget's slots.</summary>
        public bool HasLight => _lightOwned;

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

        /// <summary>
        /// Arms a freshly pooled packet with its gold and starts its hop. False, and the
        /// body untouched, if the packet already holds a life (the pool handed out a body
        /// that was not recycled) or the numbers are invalid.
        /// </summary>
        public bool Begin(int transactionId, int amount, int runId, int round, Vector3 deathPosition,
            GoldService owner, GoldOrbTuning tuning, bool withLight)
        {
            if (!_packet.Begin(transactionId, amount, runId, round))
            {
                return false;
            }

            _owner = owner;
            _tuning = tuning ?? new GoldOrbTuning();

            _hopTime = 0f;
            _magnetSpeed = 0f;
            _flashTime = 0f;
            _flightTime = 0f;
            _bobPhase = Random.value * Mathf.PI * 2f;

            _hopFrom = deathPosition + Vector3.up * 0.6f;

            Vector2 scatter = Random.insideUnitCircle.normalized *
                              Random.Range(_tuning.ScatterMin, _tuning.ScatterMax);
            _hopTo = deathPosition + new Vector3(scatter.x, _tuning.RestHeight, scatter.y);

            transform.position = _hopFrom;

            _baseScale = _tuning.ScaleForAmount(_packet.Amount);

            EnsureVisual();
            ApplyColor(_tuning.Color);
            ApplyLight(withLight);

            if (_visual != null)
            {
                _visual.localScale = Vector3.one * _baseScale;
                _visual.localPosition = Vector3.zero;
                _visual.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }

            return true;
        }

        /// <summary>
        /// The sweep took this packet (already credited). Fly to the player on unscaled
        /// time and return to the pool. Called by the service; the state is already
        /// AutoCollected.
        /// </summary>
        internal void BeginSweepFlight()
        {
            _flightTime = 0f;
            _flightFrom = transform.position;
            if (_visual != null)
            {
                _visual.localPosition = Vector3.zero;
            }
        }

        protected override void OnSpawned()
        {
            // A pooled body always comes back recycled (OnDespawned), so Begin can arm it.
            EnsureVisual();
        }

        protected override void OnDespawned()
        {
            bool lostGold = _packet.Recycle();

            if (_owner != null)
            {
                _owner.NotifyOrbReleased(this, _lightOwned, lostGold);
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
            if (!IsSpawned || _tuning == null)
            {
                return;
            }

            // The sweep flight runs under the round-end freeze, so it is the one thing
            // here that must not wait for scaled time. The gold is already credited; if
            // the flight never finished the wallet would still be right.
            if (_packet.State == GoldPacketState.AutoCollected)
            {
                TickSweepFlight(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
                return;
            }

            // Scaled: pause and the round-end freeze stop the packet with everything else.
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            if (_packet.State == GoldPacketState.Collected)
            {
                TickFlash(dt);
                return;
            }

            if (_packet.State == GoldPacketState.Spawned)
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

            if (t >= 1f)
            {
                _packet.Land();
            }
        }

        private void TickIdleAndMagnet(float dt)
        {
            if (_owner == null)
            {
                return;
            }

            if (!_owner.TryGetPlayerPoint(out Vector3 player))
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

            if (_packet.State == GoldPacketState.Waiting)
            {
                float magnet = _owner.MagnetRadius;
                if (distanceSqr <= magnet * magnet && _packet.Attract())
                {
                    _magnetSpeed = 0f;
                }
            }

            if (_packet.State != GoldPacketState.Magnetizing)
            {
                Bob(dt);
                return;
            }

            // "Pulled hard" (request): accelerate up to the magnet speed rather than
            // teleporting, so the pull reads as a decision the packet made.
            _packet.TickMagnet(dt);
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
            if (!_packet.TryCollect(GoldCollectKind.Manual, out _))
            {
                return;
            }

            _flashTime = 0f;

            // Credit first, animate second. If anything below throws, the player still
            // has the gold.
            _owner?.OnPacketCollected(this);
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

        private void TickSweepFlight(float unscaledDt)
        {
            _flightTime += unscaledDt;

            float duration = _owner != null ? _owner.SweepFlightDuration : 0.35f;
            float t = Mathf.Clamp01(_flightTime / duration);

            Vector3 target = _flightFrom;
            if (_owner != null && _owner.TryGetPlayerPoint(out Vector3 player))
            {
                target = player;
            }

            // Ease-in: the packet hesitates, then darts. Reads as "taken", not "teleported".
            float eased = t * t;
            transform.position = Vector3.Lerp(_flightFrom, target, eased);

            if (_visual != null)
            {
                _visual.localScale = Vector3.one * (_baseScale * Mathf.Lerp(1f, 0.35f, t));
            }

            if (_light != null && _light.enabled)
            {
                _light.intensity = _tuning.LightIntensity * (1f - t);
            }

            if (t >= 1f || _owner == null)
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

            _renderer = sphere.GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            // Additive so gold separates from wet stone and moss under the overcast
            // lighting without being a neon ball (request: clearly readable, not neon).
            // The colour is (re)applied in Begin once the tuning is known; this default
            // only covers a body warmed before its first life.
            ApplyColor(_tuning != null ? _tuning.Color : Color.yellow);

            var lightObject = new GameObject("Glow");
            lightObject.transform.SetParent(transform, false);

            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = _tuning != null ? _tuning.Color : Color.yellow;
            _light.enabled = false;
            _light.shadows = LightShadows.None;
        }

        private void ApplyColor(Color color)
        {
            if (_renderer == null)
            {
                return;
            }

            if (_renderer.sharedMaterial != null && _visualColor == color)
            {
                return;
            }

            // Shared per colour by FeedbackMaterials: no material instance per packet.
            _renderer.sharedMaterial = FeedbackMaterials.Additive(color);
            _visualColor = color;
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
