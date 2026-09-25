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

        public void Configure(GameObject modelAsset, CharacterAnimationSet set, System.Func<Vector3> velocity, System.Func<bool> alive, Renderer materialSource)
        {
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
            if (WeaponSet != null && _animator != null) Apply();
        }

        public void Fire() { if (Visible) _animator.SetTrigger(FireId); }
        public void SwitchIn() { if (Visible) _animator.SetTrigger(SwitchInId); }
        public void SwitchOut() { if (Visible) _animator.SetTrigger(SwitchOutId); }

        /// <summary>Reload at the gameplay duration (the FP twin has the TP clip's frames, so the TP rate fits).</summary>
        public void Reload(float seconds, bool empty)
        {
            if (!Visible || WeaponSet == null) return;
            string clip = CharacterAnimatorContract.PlayerFpClipV3(WeaponSet, empty ? "reload_empty" : "reload_tac");
            float clipSeconds = _set.Find(clip)?.seconds ?? 0f;
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
            Transform socket = FindDeep(_instance.transform, PlayerSkinnedVisual.WeaponSocketName);
            if (socket != null)
            {
                _frame = new GameObject("fp_weapon_frame").transform;
                _frame.SetParent(socket, false);
                _frame.gameObject.layer = socket.gameObject.layer;
            }

            _applied = null;
            _instance.SetActive(false);
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
            if (_instance == null) { if (Shown == this) Shown = null; return; }
            if (_instance.activeSelf != on)
            {
                _instance.SetActive(on);
                if (on) { Apply(); _animator.Update(0f); }
            }

            if (!on) { if (Shown == this) Shown = null; LastAnchorError = float.NaN; return; }
            Shown = this;
            Place(anchor);
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
            Vector3 local = root.InverseTransformPoint(_fpCamera.position);
            root.position = anchor.position - root.rotation * Vector3.Scale(local, root.lossyScale);
            LastAnchorError = Vector3.Distance(_fpCamera.position, anchor.position);
        }

        private void OnDisable()
        {
            if (_instance != null) _instance.SetActive(false);
            if (Shown == this) Shown = null;
        }

        private void OnDestroy()
        {
            if (_instance != null) Destroy(_instance);
            if (Shown == this) Shown = null;
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
