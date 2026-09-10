using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// Steps 1 and 3 of the twelve-step chain: procedural recoil and visible mechanical
    /// movement.
    ///
    /// The rig is a critically damped spring on two channels:
    ///   * the weapon body kicks backward and pitches up, then settles;
    ///   * a separate "action" part (slide / bolt / rotary barrel) cycles once per shot.
    ///
    /// The second one is what makes the difference between "the gun jerked" and "the gun
    /// worked". The request lists muzzle flash, mechanical movement and recoil as three
    /// separate items for exactly this reason - a flash alone reads as a decal.
    ///
    /// If no model is assigned the rig builds a primitive one so the prototype has
    /// something to move on day one. Assign <see cref="_body"/> and the auto-build is
    /// skipped entirely, so the player task can drop its own weapon model in later
    /// without touching this file.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponVisualRig : MonoBehaviour
    {
        [Header("Model - leave empty to auto-build a primitive placeholder")]
        [SerializeField] private Transform _body;
        [SerializeField] private Transform _action;
        [SerializeField] private Transform _muzzle;

        [Header("PROTOTYPE-ONLY spring")]
        [PrototypeOnly(PendingDecisions.None, "How fast the recoil spring returns to rest.", true)]
        [SerializeField] private float _returnSpeed = 16f;

        [PrototypeOnly(PendingDecisions.None, "Damping on the recoil spring. 1 is critically damped.", true)]
        [SerializeField] private float _damping = 0.82f;

        [PrototypeOnly(PendingDecisions.None, "Degrees of muzzle rise per unit of recoil kick.", true)]
        [SerializeField] private float _pitchPerKick = 90f;

        [PrototypeOnly(PendingDecisions.None, "Metres the action part travels on a cycle.", true)]
        [SerializeField] private float _actionTravel = 0.09f;

        [PrototypeOnly(PendingDecisions.None, "Seconds for one action cycle.", true)]
        [SerializeField] private float _actionCycleTime = 0.07f;

        [PrototypeOnly(PendingDecisions.None, "Degrees per second the rotary barrel spins at full spin-up.", true)]
        [SerializeField] private float _spinMaxSpeed = 1400f;

        [PrototypeOnly(PendingDecisions.None, "Seconds to reach full spin, and to fall back to rest.", true)]
        [SerializeField] private float _spinRampTime = 0.35f;

        private Vector3 _bodyRestPosition;
        private Quaternion _bodyRestRotation;
        private Vector3 _actionRestPosition;

        private float _kick;
        private float _kickVelocity;
        private float _actionTimer;
        private float _spin;
        private float _spinTarget;
        private float _reloadTilt;

        /// <summary>Where shots originate and where the muzzle flash is parented.</summary>
        public Transform Muzzle => _muzzle != null ? _muzzle : transform;

        /// <summary>The weapon body, for anything that wants to swap materials.</summary>
        public Transform Body => _body;

        private void Awake()
        {
            EnsureModel();

            if (_body != null)
            {
                _bodyRestPosition = _body.localPosition;
                _bodyRestRotation = _body.localRotation;
            }

            if (_action != null)
            {
                _actionRestPosition = _action.localPosition;
            }
        }

        /// <summary>Step 1 + step 3. Called once per trigger pull, not once per pellet.</summary>
        public void PlayFireMotion(float recoilKick)
        {
            _kick += Mathf.Max(0f, recoilKick);
            _kickVelocity -= Mathf.Max(0f, recoilKick) * 8f;
            _actionTimer = _actionCycleTime;
        }

        /// <summary>Visible reload: the weapon tips out of aim and comes back.</summary>
        public void PlayReloadMotion(float duration)
        {
            _reloadTilt = Mathf.Max(0.05f, duration);
        }

        public void CancelReloadMotion()
        {
            _reloadTilt = 0f;
        }

        /// <summary>Rotary cannon spin-up / spin-down. 0 = idle, 1 = full speed.</summary>
        public void SetSpin(float target01)
        {
            _spinTarget = Mathf.Clamp01(target01);
        }

        /// <summary>Show or hide the whole rig, for the temporary classic-weapon swap (REQ-057).</summary>
        public void SetVisible(bool visible)
        {
            if (_body != null && _body.gameObject.activeSelf != visible)
            {
                _body.gameObject.SetActive(visible);
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            UpdateKick(dt);
            UpdateAction(dt);
            UpdateSpin(dt);
        }

        private void UpdateKick(float dt)
        {
            if (_body == null)
            {
                return;
            }

            // Spring toward rest. Explicit integration is plenty at 50-60 Hz and stays
            // frame-rate independent because both terms are scaled by dt.
            _kickVelocity += -_kick * _returnSpeed * dt;
            _kickVelocity *= Mathf.Clamp01(1f - _damping * dt * _returnSpeed * 0.25f);
            _kick += _kickVelocity * dt;

            if (Mathf.Abs(_kick) < 0.0005f && Mathf.Abs(_kickVelocity) < 0.0005f)
            {
                _kick = 0f;
                _kickVelocity = 0f;
            }

            float tilt = 0f;
            if (_reloadTilt > 0f)
            {
                _reloadTilt -= dt;
                tilt = 28f;
            }

            _body.localPosition = _bodyRestPosition - new Vector3(0f, 0f, _kick);
            _body.localRotation = _bodyRestRotation * Quaternion.Euler(-_kick * _pitchPerKick - tilt, 0f, 0f);
        }

        private void UpdateAction(float dt)
        {
            if (_action == null)
            {
                return;
            }

            if (_actionTimer <= 0f)
            {
                _action.localPosition = _actionRestPosition;
                return;
            }

            _actionTimer -= dt;

            // Out fast, back slower: that asymmetry is what reads as a mechanism.
            float t = 1f - Mathf.Clamp01(_actionTimer / Mathf.Max(0.0001f, _actionCycleTime));
            float travel = t < 0.35f
                ? Mathf.Lerp(0f, 1f, t / 0.35f)
                : Mathf.Lerp(1f, 0f, (t - 0.35f) / 0.65f);

            _action.localPosition = _actionRestPosition - new Vector3(0f, 0f, travel * _actionTravel);
        }

        private void UpdateSpin(float dt)
        {
            if (_action == null)
            {
                return;
            }

            float rate = _spinRampTime > 0.001f ? dt / _spinRampTime : 1f;
            _spin = Mathf.MoveTowards(_spin, _spinTarget, rate);

            if (_spin <= 0.001f)
            {
                return;
            }

            _action.Rotate(Vector3.forward, _spin * _spinMaxSpeed * dt, Space.Self);
        }

        // ------------------------------------------------------------------ placeholder model

        private void EnsureModel()
        {
            if (_body != null)
            {
                if (_muzzle == null)
                {
                    _muzzle = CreateMarker("Muzzle", _body, new Vector3(0f, 0f, 0.55f));
                }

                return;
            }

            var bodyGo = new GameObject("WeaponBody");
            _body = bodyGo.transform;
            _body.SetParent(transform, false);
            _body.localPosition = new Vector3(0.22f, 0f, 0.28f);

            BuildBox(_body, "Receiver", new Vector3(0f, 0f, 0f),
                new Vector3(0.09f, 0.11f, 0.42f), new Color(0.16f, 0.16f, 0.18f, 1f));

            BuildBox(_body, "Barrel", new Vector3(0f, 0.02f, 0.36f),
                new Vector3(0.05f, 0.05f, 0.34f), new Color(0.22f, 0.22f, 0.24f, 1f));

            BuildBox(_body, "Grip", new Vector3(0f, -0.11f, -0.06f),
                new Vector3(0.06f, 0.16f, 0.08f), new Color(0.28f, 0.2f, 0.14f, 1f));

            if (_action == null)
            {
                Transform action = BuildBox(_body, "Action", new Vector3(0f, 0.08f, 0.06f),
                    new Vector3(0.06f, 0.05f, 0.24f), new Color(0.42f, 0.42f, 0.46f, 1f));
                _action = action;
            }

            if (_muzzle == null)
            {
                _muzzle = CreateMarker("Muzzle", _body, new Vector3(0f, 0.02f, 0.55f));
            }
        }

        private static Transform CreateMarker(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private static Transform BuildBox(Transform parent, string name, Vector3 localPosition, Vector3 size, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localScale = size;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = FeedbackMaterials.Opaque(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return t;
        }
    }
}
