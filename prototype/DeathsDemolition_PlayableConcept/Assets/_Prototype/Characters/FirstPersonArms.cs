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
        // C-00 25.09: the rig is placed by the WEAPON (its body is invisible) — per weapon, the anchor-space shift that puts the rest
        // pose's right-hand grip socket on fpView's grip socket; the camera anchor only orients it
        private readonly Dictionary<string, Vector3> _shift = new Dictionary<string, Vector3>();
        private bool _calibratedThisFrame, _frameValid;
        private System.Func<DD.Prototype.Core.Weapons.WeaponDefinition> _weapon;
        private int _restHash;
        /// <summary>The set's hand → weapon frame is calibrated (the hook is published only then; before it S-02's fpView pose holds).</summary>
        public bool FrameCalibrated => _forAnimSet != null && _handToFrame.ContainsKey(_forAnimSet);
        private string _applied;

        public string WeaponSet { get; private set; }
        public Animator Animator => _animator;
        public Transform FpCamera => _fpCamera;
        public Transform WeaponFrame => _frame;
        /// <summary>WP-082 / WP-083 B2b: the FP left hand's magazine socket (same name as the third-person body's).</summary>
        public Transform MagazineSocket => _magSocket;
        private Transform _magSocket;
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

        private Mesh _armsMesh;
        private Material _armsMaterial;
        private Texture2D[] _armsAlbedos;
        private System.Func<int> _skin;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <param name="armsMaterial">AP-022 FP arms v2: the arms' own material (own UVs / maps); null = the TP body's material and albedo block.</param>
        /// <param name="armsAlbedos">that material's albedo per skin (PlayerSkin index), swapped by <see cref="SyncSkin"/>.</param>
        public void Configure(GameObject modelAsset, CharacterAnimationSet set, System.Func<Vector3> velocity, System.Func<bool> alive, Renderer materialSource,
            System.Func<DD.Prototype.Core.Weapons.WeaponDefinition> weapon = null, Mesh armsMesh = null, Material armsMaterial = null, Texture2D[] armsAlbedos = null, System.Func<int> skin = null)
        {
            _armsMesh = armsMesh;
            _armsMaterial = armsMaterial;
            _armsAlbedos = armsAlbedos;
            _skin = skin;
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
            _lReleased = false;
            WeaponSet = ResolveSet(animSet);
            _forAnimSet = WeaponSet != null ? animSet : null;
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
        /// <summary>
        /// Tests / previews: force the ADS weight (on = 1, off = S-02's live weight). In the game the ADS and the view-model feel come from
        /// the player's WeaponController (FpWeaponFeel.AdsEased, Position, Rotation) — see <see cref="FollowWeaponView"/>.
        /// </summary>
        public void SetAds(bool on) => _adsForced = on;
        private bool _adsForced;
        private Quaternion _viewRotation = Quaternion.identity;   // FollowWeaponView's rotation this frame (anchor space) — the rig-offset lift turns with it
        private DD.Prototype.Weapons.WeaponController _controller;
        private DD.Prototype.Core.Weapons.WeaponDefinition _sightDef;
        private bool _hasSight;
        private Vector3 _sightLocal;

        /// <summary>
        /// WP-071 × S-02 WP-069 FP feel (C-00 28.09): the arms follow S-02's view-model pose — the rest is calibrated on fpView, then the whole
        /// rig moves rigidly so the weapon lands where S-02's fpView path would put it: fpView blended to the sight (the model's sight_ads on the
        /// anchor, the barrel along the view) by the eased ADS weight, plus the feel layer's sway / bob / kick offset. The hands keep their grip
        /// (the IK runs after); the clips' own ADS poses are not used. Runs after the rest calibration.
        /// </summary>
        private void FollowWeaponView(DD.Prototype.Core.Weapons.WeaponDefinition def)
        {
            if (def == null || _anchor == null) return;
            if (_controller == null) _controller = GetComponentInParent<DD.Prototype.Weapons.WeaponController>();
            DD.Prototype.Weapons.FpWeaponFeel feel = _controller != null ? _controller.ClassicFeel : null;
            float ads = _adsForced ? 1f : feel != null ? feel.AdsEased : 0f;
            var pose = def.FpViewPose;
            Quaternion restRot = Quaternion.Euler(pose.euler);
            Vector3 viewPos = pose.position;
            Quaternion viewRot = restRot;
            if (ads > 0f && SightAds(def, out Vector3 sight))
            {
                viewPos = Vector3.Lerp(viewPos, -(sight * pose.scale), ads);
                viewRot = Quaternion.Slerp(viewRot, Quaternion.identity, ads);
            }

            if (feel != null && !_adsForced) { viewPos += feel.Position; viewRot = feel.Rotation * viewRot; }
            Quaternion dq = viewRot * Quaternion.Inverse(restRot);   // anchor space, about the weapon root at rest
            _viewRotation = dq;
            if (dq == Quaternion.identity && viewPos == pose.position) return;
            Transform root = _instance.transform;
            Vector3 rp = _anchor.InverseTransformPoint(root.position);
            Quaternion rr = Quaternion.Inverse(_anchor.rotation) * root.rotation;
            root.SetPositionAndRotation(_anchor.TransformPoint(viewPos + dq * (rp - pose.position)), _anchor.rotation * dq * rr);
        }

        /// <summary>The weapon model's sight_ads in its root frame (the rig under the player's WeaponController; cached per definition).</summary>
        private bool SightAds(DD.Prototype.Core.Weapons.WeaponDefinition def, out Vector3 local)
        {
            if (_sightDef != def)
            {
                _sightDef = def;
                _hasSight = false;
                Transform muzzle = _controller != null ? _controller.Muzzle : null;
                var rig = muzzle != null ? muzzle.GetComponentInParent<DD.Prototype.Weapons.WeaponVisualRig>() : null;
                Transform node = rig != null ? DD.Prototype.Core.Weapons.WeaponSocketContract.Find(rig.transform, def.presentation.sockets.sightAds) : null;
                if (node != null) { _hasSight = true; _sightLocal = rig.transform.InverseTransformPoint(node.position); }
            }

            local = _sightLocal;
            return _hasSight;
        }
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
            if (_contactIk != null) _contactIk.BeginReload(seconds, e != null ? e.clip : null);   // WP-083 B2e
        }

        /// <summary>WP-083 B2e: the reload ended early — the left hand leaves the magazine well to the clip.</summary>
        public void CancelContact()
        {
            if (_contactIk != null) _contactIk.CancelReload();
        }

        /// <summary>WP-083 B2e: the FP reload's magazine contact (tests).</summary>
        public MagazineContactIk ContactIk => _contactIk;
        private MagazineContactIk _contactIk;

        private string _forAnimSet;

        /// <summary>The arms hold this weapon: an FP set of its own, or of its class (the same rule SetWeaponSet applies).</summary>
        public bool HasFpSetFor(string animSet) => ResolveSet(animSet) != null;

        /// <summary>
        /// The weapon's own FP set, else a set of the same class (smg_2 → smg_1: the class pose, and the IK puts the hands on this
        /// weapon's own grip / handguard), else none (the weapon stays on S-02's fpView pose without arms).
        /// </summary>
        private string ResolveSet(string animSet)
        {
            if (string.IsNullOrEmpty(animSet) || _set == null || _set.FpWeaponSets == null) return null;
            string cls = CharacterAnimatorContract.WeaponClassOf(animSet), sameClass = null;
            for (int i = 0; i < _set.FpWeaponSets.Count; i++)
            {
                CharacterAnimationSet.WeaponAnimSet ws = _set.FpWeaponSets[i];
                if (ws == null) continue;
                if (ws.animSet == animSet) return animSet;
                if (sameClass == null && CharacterAnimatorContract.WeaponClassOf(ws.animSet) == cls) sameClass = ws.animSet;
            }

            return sameClass;
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
                if (body0 && _armsMesh != null) r.sharedMesh = _armsMesh;   // arms only (the torso in front of the overlay camera covered the frame)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.updateWhenOffscreen = true;   // the overlay camera sits inside the bounds
                if (_armsMaterial != null) r.sharedMaterial = _armsMaterial;
                else if (_materialSource != null) r.sharedMaterial = _materialSource.sharedMaterial;
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
                // free under the rig root: set each frame from the PRE-IK hand (the IK then brings the hands to the weapon)
                _frame = new GameObject("fp_weapon_frame").transform;
                _frame.SetParent(_instance.transform, false);
                _frame.gameObject.layer = _hand.gameObject.layer;
                _rUpper = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm); _rLower = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                _lUpper = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); _lLower = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                _lHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                _magSocket = Characters.MagazineSocket.Ensure(_animator);   // WP-083 B2b
                // WP-083 B2e: the left hand on the FP weapon's magazine well on the reload beats (after this component's own IK, 945 > 940)
                _contactIk = _instance.GetComponent<MagazineContactIk>() ?? _instance.AddComponent<MagazineContactIk>();
                _contactIk.Configure(_animator, _magSocket, PlayerSkinnedVisual.HeldMagazinePart, () => Visible);
                _rFingers = FingerBones(_animator, HumanBodyBones.RightThumbProximal);
                _lFingers = FingerBones(_animator, HumanBodyBones.LeftThumbProximal);
                Transform socket = FindDeep(_hand, PlayerSkinnedVisual.WeaponSocketName);
                _socketInHand = socket != null ? socket.localPosition : Vector3.zero;
            }

            // the FP clips' lh_release / lh_grab (AP-022) open / close the left hand's IK — the same events the TP visual reads
            _events = _instance.GetComponent<CharacterAnimationEvents>() ?? _instance.AddComponent<CharacterAnimationEvents>();
            _events.Raised += OnFpEvent;

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
            if (_hand == null || _frame == null || WeaponSet == null || _forAnimSet == null || _handToFrame.ContainsKey(_forAnimSet) || _weapon == null) return;
            DD.Prototype.Core.Weapons.WeaponDefinition def = _weapon();
            if (def == null || def.AnimSet != _forAnimSet) return;
            if (_animator.IsInTransition(0)) return;
            AnimatorStateInfo st = _animator.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash != _restHash || Mathf.Repeat(st.normalizedTime, 1f) > 0.1f) return;
            var pose = def.FpViewPose;
            Quaternion r = anchor.rotation * Quaternion.Euler(pose.euler);
            Vector3 socket = anchor.TransformPoint(pose.position) + r * (def.GripOffset * pose.scale);
            // shift the rig so the rest hand's own grip socket sits on fpView's; the weapon then rides that socket (no hand offset)
            // (the rig already carries RigOffset this frame; the shift is taken without it, so the hand rests RigOffset off the socket)
            _shift[_forAnimSet] = anchor.InverseTransformVector(socket - _hand.TransformPoint(_socketInHand)) + RigOffsetOf(_forAnimSet);
            _handToFrame[_forAnimSet] = new Pose(_socketInHand, Quaternion.Inverse(_hand.rotation) * r);
            _calibratedThisFrame = true;
            ApplyFrame();
        }

        private void ApplyFrame() { }   // the frame is set each frame in HoldWeapon

        // ---------------------------------------------------------------- C-00 25.09: the hands on the weapon (IK over the clip)

        [PrototypeOnly(PendingDecisions.None, "WP-071 FP hand IK: blend time (s) of the right (switch) and left (lh_release / lh_grab) hand IK. Invented / A6.3.", true)]
        public const float HandIkBlendSeconds = 0.10f;

        [PrototypeOnly(PendingDecisions.None, "WP-071 FP rig offset in the viewmodel camera's space (m; x right, y up, z look): the arms rig (shoulders) sits 10 cm lower and 8 cm further forward than the rest clip puts it; the weapon frame is lifted back by the same amount and the hand IK closes the gap — the weapon stays on fpView, the arms bend and enter from the bottom edge with the elbows off screen (C-00 25.09; S-06 sweep f29 with the per-weapon left-hand points, S-02 WeaponDefinition.FpLeftHandOffset).", true)]
        public static readonly Vector3 RigOffset = new Vector3(0f, -0.10f, 0.08f);

        [PrototypeOnly(PendingDecisions.None, "WP-071 ar_2's own FP rig offset (the long handguard keeps its left arm straighter): 14 cm lower, 16 cm forward (C-00 25.09, from the f29 sweep trend).", true)]
        public static readonly Vector3 Ar2RigOffset = new Vector3(0f, -0.14f, 0.16f);

        /// <summary>The rig offset of an FP set.</summary>
        public static Vector3 RigOffsetOf(string animSet) => animSet == "ar_2" ? Ar2RigOffset : RigOffset;

        // ------------------------------------------------ C-00 25.09: procedural finger grip (the clips' fingers are straight — mitten era)

        [PrototypeOnly(PendingDecisions.None, "WP-071 FP finger grip curls (degrees; proximal / intermediate / distal) over the clip's straight fingers until P-06's finger clips: fingers wrap the grip / handguard, the right index rests near the trigger guard, the thumb closes lightly. Invented, judged on the f31 frames.", true)]
        public static readonly Vector3 FingerCurl = new Vector3(50f, 60f, 40f), TriggerCurl = new Vector3(15f, 20f, 10f), ThumbCurl = new Vector3(20f, 25f, 15f);

        /// <summary>C-00 26.09: OFF — P-06's contact-based finger clips carry the grip; the procedural curl stays as a fallback for clips without it.</summary>
        public static readonly bool ProceduralFingerGrip = false;   // readonly, not const: no unreachable-code warning

        [PrototypeOnly(PendingDecisions.None, "WP-071 FP left elbow pole in the viewmodel camera's space (m; x right, y up, z look): down and slightly back — the left elbow off screen (C-00 26.09 from P-06's measurement).", true)]
        public static readonly Vector3 LeftElbowPole = new Vector3(-0.2f, -1.0f, -0.3f);

        private Transform[] _rFingers, _lFingers;   // thumb, index, middle, ring, little × proximal / intermediate / distal

        private static Transform[] FingerBones(Animator a, HumanBodyBones thumbProximal)
        {
            var t = new Transform[15];
            for (int i = 0; i < 15; i++) t[i] = a.GetBoneTransform((HumanBodyBones)((int)thumbProximal + i));
            return t;
        }

        /// <summary>
        /// Curls the hand's fingers by <paramref name="weight"/> about the knuckle line, each finger toward <paramref name="target"/> (the side
        /// that brings the middle finger's tip nearer — the palm side facing the weapon); the thumb about the axis across its own base.
        /// Runs after the hand IK (the hand's final pose), over the clip's fingers.
        /// </summary>
        private static void GripFingers(Transform[] f, float weight, Vector3 target, bool right)
        {
            if (f == null || weight <= 0f || f[3] == null || f[12] == null || f[6] == null || f[8] == null) return;
            Vector3 across = f[12].position - f[3].position;   // index → little knuckles
            if (across.sqrMagnitude < 1e-8f) return;
            across.Normalize();
            Vector3 root = f[6].position, tip = f[8].position - root;   // middle proximal → distal
            float s = (Quaternion.AngleAxis(30f, across) * tip + root - target).sqrMagnitude < (Quaternion.AngleAxis(-30f, across) * tip + root - target).sqrMagnitude ? 1f : -1f;
            for (int finger = 1; finger < 5; finger++)
            {
                Vector3 c = right && finger == 1 ? TriggerCurl : FingerCurl;
                for (int j = 0; j < 3; j++)
                {
                    Transform b = f[finger * 3 + j];
                    if (b != null) b.rotation = Quaternion.AngleAxis(s * c[j] * weight, across) * b.rotation;
                }
            }

            if (f[0] == null || f[1] == null || f[2] == null) return;
            Vector3 thumbDir = f[1].position - f[0].position;
            Vector3 thumbAxis = Vector3.Cross(thumbDir, across);
            if (thumbAxis.sqrMagnitude < 1e-8f) return;
            thumbAxis.Normalize();
            Vector3 tr = f[0].position, tt = f[2].position - tr;
            float ts = (Quaternion.AngleAxis(20f, thumbAxis) * tt + tr - target).sqrMagnitude < (Quaternion.AngleAxis(-20f, thumbAxis) * tt + tr - target).sqrMagnitude ? 1f : -1f;
            for (int j = 0; j < 3; j++) f[j].rotation = Quaternion.AngleAxis(ts * ThumbCurl[j] * weight, thumbAxis) * f[j].rotation;
        }
        private Transform _anchor;
        private Transform _rUpper, _rLower, _lUpper, _lLower, _lHand;
        private Vector3 _socketInHand;
        private CharacterAnimationEvents _events;
        private float _rWeight, _lWeight;
        private bool _lReleased;
        /// <summary>After the IK: right hand's grip socket → weapon frame, left hand → its point on the weapon (m; tests).</summary>
        public float LastRightError { get; private set; } = float.NaN;
        public float LastLeftError { get; private set; } = float.NaN;

        private void OnFpEvent(CharacterAnimationEvent e)
        {
            if (e == CharacterAnimationEvent.LeftHandRelease) _lReleased = true;
            else if (e == CharacterAnimationEvent.LeftHandGrab) _lReleased = false;
        }

        /// <summary>
        /// The weapon frame from the PRE-IK hand (rest = fpView, the clip's hand motion carries it through reload / fire), then two-bone
        /// IK: the right hand's grip socket onto the frame (off while switching), the left hand onto the set's handguard point
        /// (AP-022 hands_in_weapon_frame, weapon frame) unless the clip released it (lh_release → lh_grab).
        /// </summary>
        private void HoldWeapon()
        {
            LastRightError = LastLeftError = float.NaN;
            _frameValid = false;
            if (_calibratedThisFrame) { _calibratedThisFrame = false; return; }   // the shift applies from the next placement
            if (_frame == null || _hand == null || !FrameCalibrated) return;
            _viewRotation = Quaternion.identity;
            FollowWeaponView(_weapon != null ? _weapon() : null);
            Pose h = _handToFrame[_forAnimSet];
            _frame.SetPositionAndRotation(_hand.TransformPoint(h.position) - _anchor.TransformVector(_viewRotation * RigOffsetOf(_forAnimSet)), _hand.rotation * h.rotation);   // lifted back by the rig offset
            _frameValid = true;
            float dt = Time.deltaTime;
            AnimatorStateInfo st = _animator.GetCurrentAnimatorStateInfo(0);
            bool switching = st.IsName(CharacterAnimatorContract.SwitchInV3State) || st.IsName(CharacterAnimatorContract.SwitchOutV3State);
            _rWeight = Mathf.MoveTowards(_rWeight, switching ? 0f : 1f, dt / HandIkBlendSeconds);
            _lWeight = Mathf.MoveTowards(_lWeight, switching || _lReleased ? 0f : 1f, dt / HandIkBlendSeconds);
            if (_rWeight > 0f && _rUpper != null && _rLower != null)
            {
                Vector3 handGoal = _frame.position - _hand.TransformVector(_socketInHand);   // the hand where its socket sits on the grip
                WeaponGripIk.Solve(_rUpper, _rLower, _hand, Vector3.Lerp(_hand.position, handGoal, _rWeight));   // the clip's own elbow plane (poles measured worse, f29)
                LastRightError = Vector3.Distance(_hand.TransformPoint(_socketInHand), _frame.position);
            }

            CharacterAnimationSet.WeaponAnimSet ws = FpSet(WeaponSet);
            DD.Prototype.Core.Weapons.WeaponDefinition def = _weapon != null ? _weapon() : null;
            if (_lWeight > 0f && _lUpper != null && _lLower != null && _lHand != null && ws != null && ws.hasLeftHand && def != null)
            {
                float scale = RigScale;
                Vector3 root = _frame.position - _frame.rotation * (def.GripOffset * scale);
                Vector3 goal = root + _frame.rotation * ((ws.leftHandInWeapon + def.FpLeftHandOffset) * scale);   // + S-02's per-weapon grip offset (a42c431f)
                WeaponGripIk.Solve(_lUpper, _lLower, _lHand, Vector3.Lerp(_lHand.position, goal, _lWeight), _anchor.TransformPoint(LeftElbowPole));
                LastLeftError = Vector3.Distance(_lHand.position, goal);
                if (ProceduralFingerGrip) GripFingers(_lFingers, _lWeight, _lHand.position + _frame.up * 0.05f, false);   // around the handguard / receiver above the palm
            }

            if (ProceduralFingerGrip) GripFingers(_rFingers, _rWeight, _frame.position, true);   // around the grip (the socket sits in it)
        }

        private CharacterAnimationSet.WeaponAnimSet FpSet(string animSet)
        {
            for (int i = 0; _set != null && i < _set.FpWeaponSets.Count; i++) if (_set.FpWeaponSets[i] != null && _set.FpWeaponSets[i].animSet == animSet) return _set.FpWeaponSets[i];
            return null;
        }

        /// <summary>The skin onto the arms — call after a skin change: the FP arms material's own albedo for the skin (AP-022 v2), else the TP body's albedo block.</summary>
        public void SyncSkin()
        {
            if (_instance == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            if (_armsMaterial != null)
            {
                _block.Clear();
                int n = _armsAlbedos != null ? _armsAlbedos.Length : 0;
                Texture2D albedo = n > 0 ? _armsAlbedos[Mathf.Clamp(_skin != null ? _skin() : 0, 0, n - 1)] : null;
                if (albedo != null) _block.SetTexture(BaseMapId, albedo);
            }
            else if (_materialSource != null) _materialSource.GetPropertyBlock(_block);
            else return;
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
            _anchor = anchor;
            Place(anchor);
            Calibrate(anchor);
            HoldWeapon();
            // S-02 ce9f9d95: the FP weapon rides the arms (root = frame − R·gripOffset·scale) once the weapon's hand → frame is known
            DD.Prototype.Weapons.WeaponController.FirstPersonWeaponSocket = _frameValid ? _frame : null;
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
            if (_forAnimSet != null && _shift.TryGetValue(_forAnimSet, out Vector3 shift)) root.position += anchor.TransformVector(shift);
            root.position += anchor.TransformVector(RigOffsetOf(_forAnimSet));
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
            if (_events != null) _events.Raised -= OnFpEvent;
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
