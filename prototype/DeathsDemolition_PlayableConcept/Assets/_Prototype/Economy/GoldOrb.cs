using DD.Prototype.Core.Pooling;
using System;
using DD.Prototype.Feedback;
using DD.Prototype.UI;
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
    ///   Collected    taken on contact - credited first, then (WP-098B) a short flight to the HUD
    ///                gold symbol on UNSCALED time when the HUD answers, else today's short
    ///                flash in place; then the pool
    ///   AutoCollected the round-end sweep took it - credited first, then a short flight to
    ///                the player on UNSCALED time (the world is frozen), then the pool
    ///
    /// Everything before collection is scaled delta time: a paused game must not move a
    /// packet, and a frozen round end must not let packets drift toward the player. Only
    /// the flights run on unscaled time, because the sweep happens under the freeze and
    /// the gold they show is already in the wallet.
    ///
    /// WP-098B feel: Landed (glint) → MagnetStart → FeelEvents.GoldCollected (S-02's, off the wallet
    /// credit) → FlightArrived; the other three through <see cref="GoldFeelEvents"/>. The HUD flight is bound to
    /// S-09's <see cref="HudGoldTarget"/>: the point is read at flight start (and again when its
    /// LayoutVersion moves), Depart(amount) when the coin leaves, Arrive(amount) when it lands.
    /// No HUD point → no Depart, today's presentation. Reduced Motion → no flight, Arrive at once.
    ///
    /// Built from primitives at spawn, like every other visual in this prototype. No
    /// authored prefab means no missing reference.
    /// </summary>
    public sealed class GoldOrb : PooledBehaviour
    {
        /// <summary>Reads the HUD gold symbol's screen point. Signature of <see cref="HudGoldTarget.TryGetScreenPoint"/>.</summary>
        internal delegate bool HudPointProbe(out Vector2 screenPoint);

        /// <summary>
        /// EditMode seam: EditMode has no HUD, so a test answers the screen point. Null in the game
        /// (the packet reads <see cref="HudGoldTarget"/>). Depart/Arrive always go to HudGoldTarget.
        /// </summary>
        internal static HudPointProbe HudProbeForTests;

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

        // WP-098B HUD flight. _arrivalAmount > 0 while a Depart waits for its Arrive.
        private bool _flightToHud;
        private float _flightDuration;
        private float _flightEndScale;
        private float _hudDistance;
        private Vector2 _hudScreenPoint;
        private int _hudLayout;
        private Vector3 _flightTarget;
        private int _arrivalAmount;
        private GoldCollectKind _arrivalKind;

        /// <summary>Ownership state of this packet. The wallet-facing truth.</summary>
        public GoldPacketLifecycle Packet => _packet;

        /// <summary>Gold this packet is worth. 0 once recycled.</summary>
        public int Amount => _packet.Amount;

        /// <summary>True once the magnet has grabbed it. F1 readout.</summary>
        public bool IsAttracted => _packet.State == GoldPacketState.Magnetizing;

        /// <summary>True while this packet holds one of the light budget's slots.</summary>
        public bool HasLight => _lightOwned;

        /// <summary>WP-098B · True while the packet flies to the HUD gold symbol (Depart called, Arrive pending).</summary>
        public bool IsFlyingToHud => _flightToHud;

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
            _flightToHud = false;
            _arrivalAmount = 0;
            _bobPhase = UnityEngine.Random.value * Mathf.PI * 2f;

            _hopFrom = deathPosition + Vector3.up * 0.6f;

            Vector2 scatter = UnityEngine.Random.insideUnitCircle.normalized *
                              UnityEngine.Random.Range(_tuning.ScatterMin, _tuning.ScatterMax);
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
                _visual.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            }

            return true;
        }

        /// <summary>
        /// The sweep took this packet (already credited). Fly to the HUD gold symbol when one
        /// answers (WP-098B, Depart/Arrive per coin), else to the player as before, on unscaled
        /// time, and return to the pool. Called by the service; the state is already
        /// AutoCollected. <paramref name="creditedAmount"/> 0 = the wallet refused it: no HUD traffic.
        /// </summary>
        internal void BeginSweepFlight(int creditedAmount)
        {
            _flightTime = 0f;
            _flightFrom = transform.position;
            if (_visual != null)
            {
                _visual.localPosition = Vector3.zero;
            }

            if (creditedAmount > 0)
            {
                TryFlyToHud(creditedAmount, GoldCollectKind.RoundClearAuto, _owner != null ? _owner.SweepFlightDuration : 0.35f);
            }
        }

        protected override void OnSpawned()
        {
            // A pooled body always comes back recycled (OnDespawned), so Begin can arm it.
            EnsureVisual();
        }

        protected override void OnDespawned()
        {
            if (_ap010 != null)
            {
                _ap010.SetPulling(false, Vector3.zero);
            }

            // A coin that departed always arrives: a body pulled mid-flight (run end, F1 clear)
            // releases its amount now instead of leaving the HUD counter behind for 2 s.
            CompleteArrival();
            _flightToHud = false;

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

            Tick(Time.deltaTime, Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }

        /// <summary>One frame of the packet. Update feeds the real clocks; EditMode tests feed their own.</summary>
        internal void Tick(float dt, float unscaledDt)
        {
            if (_tuning == null)
            {
                return;
            }

            // The flights (sweep and HUD) run under the round-end freeze, so they are the one
            // thing here that must not wait for scaled time. The gold is already credited; if
            // the flight never finished the wallet would still be right.
            if (_packet.State == GoldPacketState.AutoCollected || _flightToHud)
            {
                TickFlight(unscaledDt);
                return;
            }

            // Scaled: pause and the round-end freeze stop the packet with everything else.
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
                if (_packet.State == GoldPacketState.Waiting)
                {
                    OnLanded();
                }
            }
        }

        private void OnLanded()
        {
            // WP-098B: the landing glint (AP-010 sparkle card) and the feel event (S-08 thud, S-07 VFX).
            if (_ap010 != null)
            {
                _ap010.Glint();
            }

            GoldFeelEvents.RaiseLanded(new GoldLandedFeel(transform.position, _packet.Amount));
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
                    GoldFeelEvents.RaiseMagnetStart(new GoldMagnetFeel(this, transform.position, _packet.Amount));
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
            if (_ap010 != null)
            {
                _ap010.SetPulling(true, player);   // REQ-014 pull trail
            }
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
            if (!_packet.TryCollect(GoldCollectKind.Manual, out int amount))
            {
                return;
            }

            _flashTime = 0f;

            // Credit first, animate second. If anything below throws, the player still
            // has the gold. The flight (WP-098B) never credits: it only moves the coin.
            bool credited = _owner != null && _owner.OnPacketCollected(this);
            if (credited)
            {
                _flightTime = 0f;
                _flightFrom = transform.position;
                TryFlyToHud(amount, GoldCollectKind.Manual, _owner.HudFlightDuration);
            }
        }

        // ------------------------------------------------------------------ HUD flight (WP-098B)

        /// <summary>
        /// Starts the coin's flight to the HUD gold symbol. False = the packet keeps today's
        /// presentation (flash in place / sweep flight to the player):
        ///   no HUD point        → no Depart, no Arrive (the HUD counts at the credit, as today);
        ///   Reduced Motion, a zero duration or no camera → no flight, Arrive at once.
        /// </summary>
        private bool TryFlyToHud(int amount, GoldCollectKind kind, float duration)
        {
            if (!TryReadHudPoint(out Vector2 point))
            {
                return false;
            }

            Camera cam = FeedbackCameraRef.Current;
            if (UiMotion.ReducedMotion || duration <= 0f || cam == null || _owner == null)
            {
                Arrive(amount, kind, false);
                return false;
            }

            // The end point lies on the camera ray through the symbol, nearer the lens than the
            // packet, so the coin rises out of the garden; at that depth it projects exactly
            // onto the symbol whatever the camera does meanwhile.
            Transform camT = cam.transform;
            float depth = Vector3.Dot(transform.position - camT.position, camT.forward);
            _hudDistance = Mathf.Max(cam.nearClipPlane + 0.5f, depth * _owner.HudFlightDepth);
            _hudScreenPoint = point;
            _hudLayout = HudGoldTarget.LayoutVersion;

            _flightToHud = true;
            _flightTime = 0f;
            _flightFrom = transform.position;
            _flightDuration = duration;
            _flightEndScale = _owner.HudFlightEndScale;
            _flightTarget = HudWorldPoint(cam);
            _arrivalAmount = amount;
            _arrivalKind = kind;

            if (_visual != null)
            {
                _visual.localPosition = Vector3.zero;
            }

            try { HudGoldTarget.Depart(amount); }
            catch (Exception e) { Debug.LogException(e, this); }

            return true;
        }

        private static bool TryReadHudPoint(out Vector2 point)
        {
            HudPointProbe probe = HudProbeForTests;
            return probe != null ? probe(out point) : HudGoldTarget.TryGetScreenPoint(out point);
        }

        /// <summary>The symbol's point at the flight's depth; re-reads the screen point when the HUD layout moved.</summary>
        private Vector3 HudWorldPoint(Camera cam)
        {
            int layout = HudGoldTarget.LayoutVersion;
            if (layout != _hudLayout && TryReadHudPoint(out Vector2 moved))
            {
                _hudScreenPoint = moved;
                _hudLayout = layout;
            }

            return cam.ScreenToWorldPoint(new Vector3(_hudScreenPoint.x, _hudScreenPoint.y, _hudDistance));
        }

        private void CompleteArrival()
        {
            if (_arrivalAmount <= 0)
            {
                return;
            }

            int amount = _arrivalAmount;
            _arrivalAmount = 0;
            Arrive(amount, _arrivalKind, true);
        }

        private void Arrive(int amount, GoldCollectKind kind, bool flew)
        {
            try { HudGoldTarget.Arrive(amount); }
            catch (Exception e) { Debug.LogException(e, this); }

            GoldFeelEvents.RaiseFlightArrived(new GoldArrivedFeel(amount, kind, flew));
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

        /// <summary>
        /// The one flight path: the round-end sweep to the player (as before) and, WP-098B, any
        /// taken packet to the HUD gold symbol. Unscaled; the gold is already in the wallet.
        /// </summary>
        private void TickFlight(float unscaledDt)
        {
            _flightTime += unscaledDt;

            float duration = _flightToHud ? _flightDuration : (_owner != null ? _owner.SweepFlightDuration : 0.35f);
            float t = Mathf.Clamp01(_flightTime / Mathf.Max(0.01f, duration));

            Vector3 target = _flightFrom;
            if (_flightToHud)
            {
                // Re-projected every frame: the camera follows the player while the coin flies.
                Camera cam = FeedbackCameraRef.Current;
                if (cam != null)
                {
                    _flightTarget = HudWorldPoint(cam);
                }

                target = _flightTarget;
            }
            else if (_owner != null && _owner.TryGetPlayerPoint(out Vector3 player))
            {
                target = player;
            }

            // Ease-in: the packet hesitates, then darts. Reads as "taken", not "teleported".
            float eased = t * t;
            transform.position = Vector3.Lerp(_flightFrom, target, eased);

            if (_visual != null)
            {
                float endScale = _flightToHud ? _flightEndScale : 0.35f;
                _visual.localScale = Vector3.one * (_baseScale * Mathf.Lerp(1f, endScale, t));
            }

            if (_flightToHud && _ap010 != null)
            {
                _ap010.SetPulling(t < 1f, target);   // the REQ-014 trail points at the symbol
            }

            if (_light != null && _light.enabled)
            {
                _light.intensity = _tuning.LightIntensity * (1f - t);
            }

            if (t >= 1f || _owner == null)
            {
                CompleteArrival();   // FlightArrived = the Arrive call, before the body leaves
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

            // WP-019: the AP-010 gold pack (mesh + emissive coins + sparkle/trail cards) when the
            // catalog is present; the WP-004 additive sphere otherwise. Presentation only.
            _ap010 = DD.Prototype.Feedback.Vfx.GoldPackVisual.TryAttach(transform, out MeshRenderer pouch);
            if (_ap010 != null)
            {
                _visual = pouch.transform;
                _visual.localScale = Vector3.one * _baseScale;
                _renderer = pouch;
                BuildLight();
                return;
            }

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Visual";

            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                // EditMode tests build packets too, and Destroy is play-mode only.
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
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
            BuildLight();
        }

        private DD.Prototype.Feedback.Vfx.GoldPackVisual _ap010;

        private void BuildLight()
        {
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
            if (_renderer == null || _ap010 != null)
            {
                return;   // the AP-010 pouch keeps its authored material
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
