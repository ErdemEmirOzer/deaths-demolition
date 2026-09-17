using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The Special Weapon lying on the map.
    ///
    /// It owns the GROUND timer and nothing else - the use timer belongs to
    /// <see cref="SpecialWeaponRuntime"/> and does not start until this object is
    /// collected. That separation is REQ-055 verbatim, and it is why the two timers are
    /// in two different objects rather than two fields in one.
    ///
    /// Pooled, because a run can produce a dozen of these and each one carries a light.
    /// Built from primitives at spawn, like every other visual here.
    ///
    /// G-21: THE GROUND TIMER HAS NO NUMBER ANY MORE
    /// The HUD used to print the remaining seconds. It does not: the drop now BLINKS, and
    /// the blink accelerates geometrically as the timer runs out
    /// (<see cref="GroundBlinkClock"/>). The phase is integrated HERE, once, and published
    /// on the HUD snapshot - so the beacon on the map and the marker on the screen are
    /// guaranteed to flash on the same beat rather than merely at the same rate.
    /// That is why <see cref="BeaconEffect"/> no longer runs a clock of its own.
    /// </summary>
    public sealed class SpecialWeaponPickup : PooledBehaviour
    {
        /// <summary>PROTOTYPE-ONLY: how close the player must get to collect it.</summary>
        public const float PickupRadius = 1.9f;

        /// <summary>PROTOTYPE-ONLY: fraction of the ground timer at which the beacon turns urgent.</summary>
        public const float UrgencyStart = 0.35f;

        /// <summary>PROTOTYPE-ONLY: beacon column height in metres.</summary>
        public const float BeaconHeight = 14f;

        private static PrototypePool<BeaconEffect> s_beaconPool;

        /// <summary>WP-037: LODGroup screen heights for the authored pickup model (LOD0 near, LOD2 far).</summary>
        private static readonly float[] s_pickupLodHeights = { 0.12f, 0.05f, 0.01f };

        /// <summary>WP-037: true when the visual is the AP-014 model rather than the WP-007 primitives (readout for tests/probe).</summary>
        public bool UsesAuthoredModel { get; private set; }

        private Transform _visual;
        private BeaconEffect _beacon;
        private SpecialWeaponId _builtId = SpecialWeaponId.KaguraBell;
        private bool _visualBuilt;

        private float _remaining;
        private float _duration;
        private float _blinkPhase;
        private bool _collected;
        private bool _beaconLoopRunning;
        private SpecialWeaponId _weaponId = SpecialWeaponId.KaguraBell;
        private string _displayName = "Special Weapon";

        /// <summary>Raised when this pickup leaves the ground, either way. Argument: was it collected.</summary>
        [System.NonSerialized] public System.Action<SpecialWeaponPickup, bool> Finished;

        public float Remaining => _remaining;

        public float Duration => _duration;

        /// <summary>Which of the three weapons is lying here.</summary>
        public SpecialWeaponId WeaponId => _weaponId;

        /// <summary>The integrated blink phase. Read by the beacon and published to the HUD.</summary>
        public float BlinkPhase => _blinkPhase;

        /// <summary>Place it. <paramref name="groundLifetime"/> is the request's separate ground timer.</summary>
        public void Begin(SpecialWeaponId weaponId, string displayName, float groundLifetime)
        {
            _weaponId = weaponId;
            _displayName = displayName;
            _duration = Mathf.Max(0.5f, groundLifetime);
            _remaining = _duration;

            // Phase starts at 0 so the very first frame on the map is a flash. A drop that
            // appeared dark and lit up half a second later would waste the one moment the
            // player is most likely to be looking at that part of the screen.
            _blinkPhase = 0f;
            _collected = false;

            EnsureVisual();

            if (_visual != null)
            {
                // A pooled pickup that comes back as the SAME weapon skips the rebuild, so
                // reset the blink scale here rather than relying on the first Update.
                _visual.localScale = Vector3.one;
            }

            AttachBeacon();

            // REQ-053: loud spawn - light column here, sound here, HUD marker through the
            // event. All three, because the request lists all three.
            AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponSpawn, transform.position, 1f, 100);
            AudioEvents.RaiseLoop(CombatAudioEvent.SpecialWeaponBeaconLoop, transform.position, 0.6f, 60);
            _beaconLoopRunning = true;

            WeaponEvents.PublishSpecialSpawned(transform.position, _duration);
            PublishState(true);
        }

        /// <summary>
        /// WP-007 (AP-10): the process is stopped from outside its own timer (run end, death,
        /// new run, teardown). Ground visuals, the beacon loop and the OnGround snapshot are
        /// torn down exactly as on expiry, but no "ground expired" news is raised and
        /// <see cref="Finished"/> is NOT invoked - the director that cancelled already knows.
        /// Safe to call on an already collected / despawned pickup.
        /// </summary>
        public void Cancel()
        {
            if (!IsSpawned || _collected)
            {
                return;
            }

            _collected = true;
            ClearGroundState();
            TearDownGroundVisuals();
            Finished = null;
            Despawn();
        }

        protected override void OnSpawned()
        {
            _collected = false;
            EnsureVisual();
        }

        protected override void OnDespawned()
        {
            // The beacon is released before Despawn is called, not here: the pool
            // contract forbids releasing another pooled object inside OnDespawned. This
            // is only a safety net for a teardown that skipped the normal path.
            _beacon = null;
            Finished = null;

            // WP-007 (AP-10 T7): a teardown that skipped the normal path (scene unload
            // releasing every pooled instance - the abort-to-menu route) must not leave the
            // beacon loop playing in the DontDestroyOnLoad audio director. Stopping a loop is
            // an event, not a pool release, so it is allowed here; the normal paths have
            // already stopped it and the flag keeps this from stopping it twice.
            if (_beaconLoopRunning)
            {
                _beaconLoopRunning = false;
                AudioEvents.StopLoop(CombatAudioEvent.SpecialWeaponBeaconLoop);
            }
        }

        /// <summary>
        /// The pooled template, built from primitives. Same reasoning as the feedback
        /// templates: no authored prefab means no missing reference.
        /// </summary>
        public static SpecialWeaponPickup CreateTemplate()
        {
            var go = new GameObject("SpecialWeaponPickup");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.NotEditable;
            return go.AddComponent<SpecialWeaponPickup>();
        }

        private void Update()
        {
            if (!IsSpawned || _collected)
            {
                return;
            }

            // Scaled time: a paused game must not burn the ground timer.
            float dt = Time.deltaTime;
            _remaining -= dt;

            float remaining01 = _duration > 0f ? Mathf.Clamp01(_remaining / _duration) : 0f;

            // The one clock. Integrated, never evaluated from a wall clock - see
            // GroundBlinkClock for why that distinction is load-bearing.
            _blinkPhase = GroundBlinkClock.Advance(_blinkPhase, remaining01, dt);
            float blink = GroundBlinkClock.Intensity01(_blinkPhase);

            if (_visual != null)
            {
                _visual.Rotate(Vector3.up, 55f * dt, Space.World);

                // The object itself breathes on the beat too, not only its light column.
                // A player looking straight at the weapon rather than at its beacon must
                // get the same reading.
                float bob = Mathf.Sin(Time.time * 1.7f) * 0.12f;
                _visual.localPosition = new Vector3(0f, bob + (blink - GroundBlinkClock.Floor) * 0.1f, 0f);

                float scale = Mathf.Lerp(0.94f, 1.1f, blink);
                _visual.localScale = new Vector3(scale, scale, scale);
            }

            if (_beacon != null)
            {
                // Urgency is now COLOUR only. The rate lives in the shared phase, so the
                // beacon and the HUD marker cannot drift apart.
                _beacon.SetUrgency(remaining01 < UrgencyStart ? 1f - remaining01 / UrgencyStart : 0f);
                _beacon.SetPulse(blink);
            }

            PublishState(false);

            if (TryCollect())
            {
                return;
            }

            if (_remaining <= 0f)
            {
                Expire();
            }
        }

        private bool TryCollect()
        {
            WeaponController controller = WeaponRegistry.ActiveController;
            if (controller == null)
            {
                return false;
            }

            Vector3 delta = controller.transform.position - transform.position;
            delta.y *= 0.5f;

            if (delta.sqrMagnitude > PickupRadius * PickupRadius)
            {
                return false;
            }

            _collected = true;

            // REQ-055: the use timer starts HERE, on collection. The weapon that was
            // rolled when the drop landed is the weapon that comes up.
            // The controller hands the HUD straight from ground phase to active phase in the same
            // frame (one Active notify, then PickedUp — WP-059C); going through "None" first would
            // flash the marker off and the timer on in two different frames, which reads as a bug.
            controller.ActivateSpecialWeapon(_weaponId);

            TearDownGroundVisuals();

            System.Action<SpecialWeaponPickup, bool> finished = Finished;
            Despawn();
            finished?.Invoke(this, true);
            return true;
        }

        private void Expire()
        {
            _collected = true;

            AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponGroundExpired, transform.position, 1f, 80);
            WeaponEvents.PublishSpecialGroundExpired();

            ClearGroundState();
            TearDownGroundVisuals();

            System.Action<SpecialWeaponPickup, bool> finished = Finished;
            Despawn();
            finished?.Invoke(this, false);
        }

        private void TearDownGroundVisuals()
        {
            ReleaseBeacon();

            if (_beaconLoopRunning)
            {
                _beaconLoopRunning = false;
                AudioEvents.StopLoop(CombatAudioEvent.SpecialWeaponBeaconLoop);
            }
        }

        private void ClearGroundState()
        {
            SpecialWeaponHudState state = WeaponEvents.Special;
            if (state.Phase != SpecialWeaponPhase.OnGround)
            {
                return;
            }

            state.Phase = SpecialWeaponPhase.None;
            state.GroundRemaining = 0f;
            state.GroundBlinkPhase = 0f;
            WeaponEvents.PublishSpecial(state, true);
        }

        private void PublishState(bool notify)
        {
            SpecialWeaponHudState state = WeaponEvents.Special;

            state.Phase = SpecialWeaponPhase.OnGround;
            state.WeaponId = _weaponId;
            state.DisplayName = _displayName;
            state.WorldPosition = transform.position;

            // Still published, because the F1 panel and telemetry want the truth. The HUD
            // no longer draws it as a number - the blink phase below is what it draws.
            state.GroundRemaining = Mathf.Max(0f, _remaining);
            state.GroundDuration = _duration;
            state.GroundBlinkPhase = _blinkPhase;

            WeaponEvents.PublishSpecial(state, notify);
        }

        // ------------------------------------------------------------------ visuals

        /// <summary>
        /// PROTOTYPE-ONLY silhouettes, one per weapon.
        ///
        /// The three have to be tellable apart from across the courtyard, because with the
        /// numeric timer gone the shape and the beacon colour are the whole answer to
        /// "what am I running for". So they differ by SILHOUETTE first and colour second:
        /// a stacked bell tree, a flat upright tablet, a floating sphere. Colour alone
        /// would fail the one player who needs it most.
        ///
        /// Rebuilt when a pooled pickup comes back as a different weapon. The guard is on
        /// the built id rather than on the transform, which is the bug the old
        /// "if (_visual != null) return" would have become the moment there was more than
        /// one weapon.
        /// </summary>
        private void EnsureVisual()
        {
            if (_visual != null && _builtId == _weaponId && _visualBuilt)
            {
                return;
            }

            if (_visual == null)
            {
                var root = new GameObject("Visual");
                _visual = root.transform;
                _visual.SetParent(transform, false);
            }
            else
            {
                for (int i = _visual.childCount - 1; i >= 0; i--)
                {
                    Destroy(_visual.GetChild(i).gameObject);
                }
            }

            _visual.localScale = Vector3.one;
            _builtId = _weaponId;
            _visualBuilt = true;

            // WP-037 (AP-014): the authored pickup model when the art package is imported;
            // the WP-007 primitives stay for a weapon without one (Tide Jewel) or a project
            // without the library.
            SpecialWeaponVisualLibrary library = SpecialWeaponVisualLibrary.Current;
            SpecialWeaponVisualLibrary.Entry authored = library != null ? library.Find(_weaponId) : null;
            if (authored != null && authored.HasPickup && library.material != null)
            {
                GameObject model = SpecialWeaponVisualLibrary.Instantiate("Model", authored.pickupLods, library.material, _visual, s_pickupLodHeights);
                if (model != null)
                {
                    UsesAuthoredModel = true;
                    return;
                }
            }

            UsesAuthoredModel = false;
            switch (_weaponId)
            {
                case SpecialWeaponId.OfudaLoom:
                    BuildLoom();
                    break;

                case SpecialWeaponId.TideJewel:
                    BuildJewel();
                    break;

                default:
                    BuildBell();
                    break;
            }
        }

        /// <summary>Kagura Bell: three bronze bells stacked on a shaft, widest at the bottom.</summary>
        private void BuildBell()
        {
            Color bronze = new Color(0.72f, 0.55f, 0.24f, 1f);
            Color lacquer = new Color(0.24f, 0.11f, 0.12f, 1f);

            BuildPart(PrimitiveType.Cylinder, "Shaft", new Vector3(0f, 0f, 0f),
                new Vector3(0.06f, 0.42f, 0.06f), lacquer);

            BuildPart(PrimitiveType.Sphere, "BellLow", new Vector3(0f, -0.2f, 0f),
                new Vector3(0.34f, 0.26f, 0.34f), bronze);

            BuildPart(PrimitiveType.Sphere, "BellMid", new Vector3(0f, 0.06f, 0f),
                new Vector3(0.26f, 0.2f, 0.26f), bronze);

            BuildPart(PrimitiveType.Sphere, "BellTop", new Vector3(0f, 0.28f, 0f),
                new Vector3(0.18f, 0.14f, 0.18f), bronze);
        }

        /// <summary>Ofuda Loom: an upright paper tablet on a lacquer frame.</summary>
        private void BuildLoom()
        {
            Color paper = new Color(0.94f, 0.9f, 0.8f, 1f);
            Color ink = new Color(0.7f, 0.18f, 0.14f, 1f);
            Color frame = new Color(0.16f, 0.13f, 0.12f, 1f);

            BuildPart(PrimitiveType.Cube, "Frame", new Vector3(0f, 0f, 0f),
                new Vector3(0.42f, 0.62f, 0.06f), frame);

            BuildPart(PrimitiveType.Cube, "Paper", new Vector3(0f, 0.04f, -0.04f),
                new Vector3(0.3f, 0.5f, 0.02f), paper);

            BuildPart(PrimitiveType.Cube, "Ink", new Vector3(0f, 0.04f, -0.06f),
                new Vector3(0.06f, 0.34f, 0.02f), ink);
        }

        /// <summary>Tide Jewel: a pale sphere resting in an open lacquer cradle.</summary>
        private void BuildJewel()
        {
            Color pearl = new Color(0.68f, 0.85f, 0.9f, 1f);
            Color cradle = new Color(0.2f, 0.14f, 0.12f, 1f);

            BuildPart(PrimitiveType.Sphere, "Jewel", new Vector3(0f, 0.1f, 0f),
                new Vector3(0.36f, 0.36f, 0.36f), pearl);

            BuildPart(PrimitiveType.Cylinder, "Cradle", new Vector3(0f, -0.16f, 0f),
                new Vector3(0.4f, 0.05f, 0.4f), cradle);

            BuildPart(PrimitiveType.Cylinder, "Stem", new Vector3(0f, -0.26f, 0f),
                new Vector3(0.12f, 0.09f, 0.12f), cradle);
        }

        private void BuildPart(PrimitiveType type, string name, Vector3 localPosition, Vector3 scale,
            Color color, Vector3 euler = default)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Transform t = go.transform;
            t.SetParent(_visual, false);
            t.localPosition = localPosition;
            t.localScale = scale;
            t.localRotation = Quaternion.Euler(euler);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = FeedbackMaterials.Opaque(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void AttachBeacon()
        {
            if (s_beaconPool == null)
            {
                BeaconEffect template = FeedbackEffectFactory.BeaconTemplate;
                if (template == null)
                {
                    return;
                }

                // Only ever one Special Weapon at a time (REQ-052), so a capacity of two
                // is generous and makes the ceiling itself a check on that rule.
                s_beaconPool = PoolService.GetOrCreate(template, 1, 2, PoolExhaustedPolicy.RecycleOldest);
            }

            if (s_beaconPool == null)
            {
                return;
            }

            _beacon = s_beaconPool.Get(transform.position, Quaternion.identity, transform);
            if (_beacon == null)
            {
                return;
            }

            // The calm colour names the WEAPON; the urgent colour is the same ember for
            // all three, because "it is about to go" is one message and must not be three.
            _beacon.Begin(BeaconHeight, 0.5f, CalmBeaconColor(_weaponId), UrgentBeaconColor);
        }

        /// <summary>PROTOTYPE-ONLY: the shared "running out" colour. One message, one colour.</summary>
        private static readonly Color UrgentBeaconColor = new Color(1f, 0.36f, 0.2f, 0.5f);

        /// <summary>
        /// PROTOTYPE-ONLY beacon colours. All three sit inside the palette the garden
        /// already owns - struck bronze, shrine paper, pond water - so the column reads as
        /// something the place could contain rather than as a science-fiction light.
        /// </summary>
        private static Color CalmBeaconColor(SpecialWeaponId id)
        {
            switch (id)
            {
                case SpecialWeaponId.OfudaLoom:
                    return new Color(0.96f, 0.93f, 0.84f, 0.3f);

                case SpecialWeaponId.TideJewel:
                    return new Color(0.55f, 0.78f, 0.86f, 0.3f);

                default:
                    return new Color(1f, 0.82f, 0.35f, 0.3f);
            }
        }

        private void ReleaseBeacon()
        {
            if (_beacon == null)
            {
                return;
            }

            _beacon.Despawn();
            _beacon = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_beaconPool = null;
        }
    }
}
