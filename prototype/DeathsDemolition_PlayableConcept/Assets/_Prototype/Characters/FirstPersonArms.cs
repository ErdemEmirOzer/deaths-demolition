using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-071 B4b-3 (A6.4 #49/#56) · the FP viewmodel arms: a copy of the player model on the <c>FirstPersonView</c> layer, its own
    /// Animator playing the AP-022 slice 5 clips (authored on the same PlayerRig with <c>fp_camera</c> held constant), placed each
    /// frame so the rig's <c>fp_camera</c> sits on <see cref="CameraView.ViewmodelAnchor"/> (the rig faces the camera). Only the LOD0
    /// body renders (head / LOD1–2 off, no shadows — the world body stays ShadowsOnly, S-01). Shown only in first person with a
    /// weapon that has an FP set; <see cref="WeaponFrame"/> (<c>fp_weapon_frame</c> under the rig's <c>socket_weapon_r</c>) is where
    /// S-02 seats the weapon (grip decision B: root = frame − R·gripOffset). The TP visual forwards equip / fire / reload / switch.
    /// </summary>
    [DefaultExecutionOrder(940)]
    [DisallowMultipleComponent]
    public sealed class FirstPersonArms : MonoBehaviour
    {
        /// <summary>The arms currently shown (null outside first person / without an FP set).</summary>
        public static FirstPersonArms Shown { get; private set; }

        [PrototypeOnly(PendingDecisions.None, "WP-071 FP sway: ground speed (m/s) at full walk sway weight, and the walk→run blend range. Invented / A6.4.", true)]
        public const float SwayFullWalk = 1.5f, SwayRunFrom = 1.5f, SwayRunTo = 4.5f;

        private GameObject _modelAsset;
        private CharacterAnimationSet _set;
        private System.Func<Vector3> _velocity;
        private System.Func<bool> _alive;
        private GameObject _instance;
        private Animator _animator;
        private Transform _fpCamera, _frame;
        private AnimatorOverrideController _overrides;
        private readonly List<string> _keys = new List<string>();
        private readonly List<List<KeyValuePair<AnimationClip, AnimationClip>>> _pairs = new List<List<KeyValuePair<AnimationClip, AnimationClip>>>();
        private int _swayLayer = -1;
        private Vector3 _cameraOffset;
        private Transform _hand;
        // hand → weapon frame per set, calibrated on the set's rest pose (P-06 contract: the rest weapon sits exactly on fpView)
        private readonly Dictionary<string, Pose> _handToFrame = new Dictionary<string, Pose>();
        private System.Func<DD.Prototype.Core.Weapons.WeaponDefinition> _weapon;
        private int _restHash;
        /// <summary>The set's hand → weapon frame is calibrated (the hook is published only then; before it S-02's fpView pose holds).</summary>
        public bool FrameCalibrated => WeaponSet != null && _handToFrame.ContainsKey(WeaponSet);
        private string _applied;

        public string WeaponSet { get; private set; }
        public Animator Animator => _animator;
        public Transform FpCamera => _fpCamera;
        public Transform WeaponFrame => _frame;
        public bool Visible => _instance != null && _instance.activeSelf;
        /// <summary>Distance fp_camera → anchor after the last placement (m; tests).</summary>
        public float LastAnchorError { get; private set; } = float.NaN;

        private static readonly int FireId = Animator.StringToHash(CharacterAnimatorContract.Fire);
        private static readonly int ReloadId = Animator.StringToHash(CharacterAnimatorContract.Reload);
        private static readonly int ReloadRateId = Animator.StringToHash(CharacterAnimatorContract.ReloadRate);
        private static readonly int ReloadEmptyId = Animator.StringToHash(CharacterAnimatorContract.ReloadEmpty);
        private static readonly int SwitchInId = Animator.StringToHash(CharacterAnimatorContract.SwitchIn);
        private static readonly int SwitchOutId = Animator.StringToHash(CharacterAnimatorContract.SwitchOut);
        private static readonly int FpRunId = Animator.StringToHash(CharacterAnimatorContract.FpRun);

        private Renderer _materialSource;
        private MaterialPropertyBlock _block;

        public void Configure(GameObject modelAsset, CharacterAnimationSet set, System.Func<Vector3> velocity, System.Func<bool> alive, Renderer materialSource,
            System.Func<DD.Prototype.Core.Weapons.WeaponDefinition> weapon = null)
        {
            _weapon = weapon;
            _materialSource = materialSource;
            _modelAsset = modelAsset;
            _set = set;
            _velocity = velocity;
            _alive = alive;
        }

        /// <summary>The rig's uniform scale = the weapon's fpView scale (C-00 25.09: fpView owns FP placement; the arms scale with the gun around fp_camera).</summary>
        public float RigScale { get; private set; } = 1f;

        /// <summary>The weapon's FP set (WeaponDefinition.AnimSet) or null (no FP arms for this weapon: S-02's fpView pose path).</summary>
        public void SetWeaponSet(string animSet, float fpViewScale = 1f)
        {
            RigScale = fpViewScale > 0.01f ? fpViewScale : 1f;
            WeaponSet = HasSet(animSet) ? animSet : null;
            _cameraOffset = Vector3.zero;
            for (int i = 0; WeaponSet != null && i < _set.FpWeaponSets.Count; i++)
            {
                CharacterAnimationSet.WeaponAnimSet ws = _set.FpWeaponSets[i];
                if (ws != null && ws.animSet == WeaponSet) _cameraOffset = ws.fpCameraOffset;
            }

            ApplyFrame();
            if (WeaponSet != null && _animator != null) Apply();
        }

        public void Fire() { if (Visible) _animator.SetTrigger(FireId); }
        public void SwitchIn() { if (Visible) _animator.SetTrigger(SwitchInId); }
        public void SwitchOut() { if (Visible) _animator.SetTrigger(SwitchOutId); }

        /// <summary>Reload at the gameplay duration (the FP twin has the TP clip's frames, so the TP rate fits).</summary>
        public void Reload(float seconds, bool empty)
        {
            if (!Visible || WeaponSet == null) return;
            string slot = empty ? "reload_empty" : "reload_tac";
            CharacterAnimationSet.Entry e = _set.Find("AP022_fp_" + WeaponSet + "_" + slot) ?? _set.Find(CharacterAnimatorContract.PlayerFpClipV3(WeaponSet, slot));
            float clipSeconds = e != null ? e.seconds : 0f;
            _animator.SetFloat(ReloadRateId, seconds > 0.05f && clipSeconds > 0.05f ? clipSeconds / seconds : 1f);
            _animator.SetBool(ReloadEmptyId, empty);
            _animator.SetTrigger(ReloadId);
        }

        private bool HasSet(string animSet)
        {
            if (string.IsNullOrEmpty(animSet) || _set == null || _set.FpWeaponSets == null) return false;
            for (int i = 0; i < _set.FpWeaponSets.Count; i++) if (_set.FpWeaponSets[i] != null && _set.FpWeaponSets[i].animSet == animSet) return true;
            return false;
        }

        private void Build()
        {
            if (_instance != null || _modelAsset == null || _set == null || _set.FpController == null) return;
            _instance = Instantiate(_modelAsset);
            _instance.name = "FP_Arms";
            int layer = CameraLayers.FirstPersonView;
            foreach (Transform t in _instance.GetComponentsInChildren<Transform>(true)) if (layer >= 0) t.gameObject.layer = layer;
            foreach (SkinnedMeshRenderer r in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool body0 = r.name.IndexOf("LOD0", System.StringComparison.Ordinal) >= 0 && r.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) < 0;
                r.enabled = body0;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.updateWhenOffscreen = true;   // the overlay camera sits inside the bounds
                if (_materialSource != null) r.sharedMaterial = _materialSource.sharedMaterial;
            }

            SyncSkin();

            _animator = _instance.GetComponent<Animator>() ?? _instance.AddComponent<Animator>();
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _overrides = new AnimatorOverrideController(_set.FpController) { name = "DD_Player_FP_runtime" };
            _animator.runtimeAnimatorController = _overrides;
            for (int i = 0; i < _set.FpWeaponSets.Count; i++)
            {
                CharacterAnimationSet.WeaponAnimSet ws = _set.FpWeaponSets[i];
                if (ws == null || ws.overrides == null) continue;
                var list = new List<KeyValuePair<AnimationClip, AnimationClip>>(ws.overrides.overridesCount);
                ws.overrides.GetOverrides(list);
                _keys.Add(ws.animSet);
                _pairs.Add(list);
            }

            Transform head = _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.Head) : null;
            _fpCamera = head != null ? head.Find("fp_camera") : null;
            // the weapon frame rides the Humanoid hand (socket_weapon_r is an extra transform: Unity drops its clip keys)
            _hand = _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (_hand != null)
            {
                _frame = new GameObject("fp_weapon_frame").transform;
                _frame.SetParent(_hand, false);
                _frame.gameObject.layer = _hand.gameObject.layer;
            }

            _restHash = Animator.StringToHash(CharacterAnimatorContract.AimV3State);

            _applied = null;
            _instance.SetActive(false);
        }

        /// <summary>
        /// On the set's rest pose (base layer resting in Aim_V3 near its loop start, no transition) the weapon frame is where fpView puts
        /// the grip socket: root = anchor · fpView.position, R = anchor · fpView.euler, socket = root + R·gripOffset·scale. Stored in the
        /// hand's space once per set; from then on the frame follows the hand (reload / fire / switch).
        /// </summary>
        private void Calibrate(Transform anchor)
        {
            if (_hand == null || _frame == null || WeaponSet == null || _handToFrame.ContainsKey(WeaponSet) || _weapon == null) return;
            DD.Prototype.Core.Weapons.WeaponDefinition def = _weapon();
            if (def == null || def.AnimSet != WeaponSet) return;
            if (_animator.IsInTransition(0)) return;
            AnimatorStateInfo st = _animator.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash != _restHash || Mathf.Repeat(st.normalizedTime, 1f) > 0.1f) return;
            var pose = def.FpViewPose;
            Quaternion r = anchor.rotation * Quaternion.Euler(pose.euler);
            Vector3 socket = anchor.TransformPoint(pose.position) + r * (def.GripOffset * pose.scale);
            _handToFrame[WeaponSet] = new Pose(_hand.InverseTransformPoint(socket), Quaternion.Inverse(_hand.rotation) * r);
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            if (_frame == null) return;
            if (WeaponSet != null && _handToFrame.TryGetValue(WeaponSet, out Pose p)) { _frame.localPosition = p.position; _frame.localRotation = p.rotation; }
            else { _frame.localPosition = Vector3.zero; _frame.localRotation = Quaternion.identity; }
        }

        /// <summary>The TP body's albedo block (skin) onto the arms — call after a skin change.</summary>
        public void SyncSkin()
        {
            if (_instance == null || _materialSource == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            _materialSource.GetPropertyBlock(_block);
            foreach (SkinnedMeshRenderer r in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.SetPropertyBlock(_block);
        }

        private void Apply()
        {
            if (_overrides == null || WeaponSet == null || WeaponSet == _applied) return;
            int k = _keys.IndexOf(WeaponSet);
            if (k < 0) return;
            _overrides.ApplyOverrides(_pairs[k]);
            _applied = WeaponSet;
        }

        private void LateUpdate()
        {
            Transform anchor = CameraView.ViewmodelAnchor;
            bool on = anchor != null && WeaponSet != null && (_alive == null || _alive());
            if (on && _instance == null) Build();
            if (_instance == null) { Release(); return; }
            if (_instance.activeSelf != on)
            {
                _instance.SetActive(on);
                if (on) { Apply(); _animator.Update(0f); }
            }

            if (!on) { Release(); LastAnchorError = float.NaN; return; }
            Shown = this;
            Place(anchor);
            Calibrate(anchor);
            // S-02 ce9f9d95: the FP weapon rides the arms (root = frame − R·gripOffset·scale) once the set's hand → weapon is known
            DD.Prototype.Weapons.WeaponController.FirstPersonWeaponSocket = FrameCalibrated ? _frame : null;
            if (_swayLayer < 0) for (int i = 0; i < _animator.layerCount; i++) if (_animator.GetLayerName(i) == CharacterAnimatorContract.FpSwayLayer) _swayLayer = i;   // lazily: 0 layers before the Animator initialises (B4 lesson)
            if (_swayLayer >= 0)
            {
                Vector3 v = _velocity != null ? _velocity() : Vector3.zero;
                float speed = new Vector2(v.x, v.z).magnitude;
                _animator.SetLayerWeight(_swayLayer, Mathf.Clamp01(speed / SwayFullWalk));
                _animator.SetFloat(FpRunId, Mathf.Clamp01((speed - SwayRunFrom) / (SwayRunTo - SwayRunFrom)));
            }
        }

        /// <summary>The rig faces the camera and its fp_camera lands on the anchor (the clips keep fp_camera constant, so this is a rigid offset).</summary>
        private void Place(Transform anchor)
        {
            Transform root = _instance.transform;
            root.rotation = anchor.rotation;
            root.localScale = Vector3.one * RigScale;
            if (_fpCamera == null) { root.position = anchor.position; return; }
            // the virtual camera = fp_camera + the set's constant offset (P-06; right / look in the rig)
            Vector3 local = root.InverseTransformPoint(_fpCamera.position) + _cameraOffset;
            root.position = anchor.position - root.rotation * Vector3.Scale(local, root.lossyScale);
            LastAnchorError = Vector3.Distance(root.TransformPoint(root.InverseTransformPoint(_fpCamera.position) + _cameraOffset), anchor.position);
        }

        // Hand the FP weapon back to S-02's fpView pose (only if it is ours).
        private void Release()
        {
            if (Shown == this) Shown = null;
            if (_frame != null && DD.Prototype.Weapons.WeaponController.FirstPersonWeaponSocket == _frame) DD.Prototype.Weapons.WeaponController.FirstPersonWeaponSocket = null;
        }

        private void OnDisable()
        {
            if (_instance != null) _instance.SetActive(false);
            Release();
        }

        private void OnDestroy()
        {
            Release();
            if (_instance != null) Destroy(_instance);
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }

            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Shown = null;
    }
}
