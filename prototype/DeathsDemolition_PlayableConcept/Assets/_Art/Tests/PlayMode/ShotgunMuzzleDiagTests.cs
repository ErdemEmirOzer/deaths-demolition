using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-071 (C-00 01.10, S-07 AD r4 M3) diagnostic: after one shot, per frame for 0.75 s — the muzzle's pitch against the
    /// camera (FP) / the round (TP), its viewport point, the weapon rig root's pitch, the body's socket_weapon_r and right hand
    /// pitch against the body, and the clips playing (FP arms / TP body, every layer with weight). sg_1 / sg_2, ar_1 as the
    /// control. Tells a clip-driven lift (the socket / hand turns) from a runtime one (the rig turns on a still socket).
    /// </summary>
    [TestFixture]
    [Explicit("S-06 diagnostic — shotgun muzzle pitch after the shot (AD r4 M3)")]
    [Category("Diagnostics")]
    public sealed class ShotgunMuzzleDiagTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private bool _armsBefore;
        private readonly TapInput _input = new TapInput();
        private bool _spawned;
        private ProjectileSpawnInfo _spawn;
        private ClassicWeaponId _watch;

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); _armsBefore = PlayerSkinnedVisual.FirstPersonV3Arms; ProjectileEvents.Spawned += OnSpawned; }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ProjectileEvents.Spawned -= OnSpawned;
            Time.captureDeltaTime = 0f;
            PlayerSkinnedVisual.FirstPersonV3Arms = _armsBefore;
            RunFlowClock.ClearHolds();
            LogAssert.ignoreFailingMessages = false;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            }
        }

        private void OnSpawned(ProjectileSpawnInfo info)
        {
            if (info.WeaponId != _watch || info.PelletIndex != 0) return;
            _spawned = true;
            _spawn = info;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Tani_Pompali_NamluAcisi_AtesSonrasi()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, 60f, "MainMenu");
            PlayerSkinnedVisual.FirstPersonV3Arms = true;
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, 60f, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null && PrototypeCameraRig.Active != null && WeaponRegistry.ActiveController != null, 60f, "oyuncu/kamera");
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = true;
            PlayerSkinnedVisual pv = rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            InputSourceRegistry.SetCurrent(_input);
            rig.Motor.SetInputSource(null);
            rig.Aim.SetInputSource(null);
            var summary = new List<string>();

            foreach (CameraMode mode in new[] { CameraMode.FirstPerson, CameraMode.ThirdPerson })
            {
                CameraModeService.Request(mode, instant: true);
                yield return CharacterTests.Bekle(() => CameraModeService.Current == mode, 5f, mode + " kamera");
                foreach (ClassicWeaponId id in new[] { ClassicWeaponId.AssaultRifle, ClassicWeaponId.CombatShotgun, ClassicWeaponId.Sg2 })
                {
                    StopSpawning();
                    Assert.IsTrue(WeaponQaHooks.TrySetClassicWeapon(id), id + " kuşanılamadı");
                    Time.captureDeltaTime = 0f;
                    float settle = Time.time + 1.2f;
                    while (Time.time < settle) yield return null;
                    WeaponDefinition def = Arsenal.Get(id);
                    string key = (mode == CameraMode.FirstPerson ? "fp-" : "tp-") + (def != null ? def.weaponId : id.ToString());
                    _watch = id;
                    _spawned = false;
                    Time.captureDeltaTime = 1f / 60f;
                    _input.Fire = true;
                    for (int i = 0; i < 90 && !_spawned; i++) yield return null;
                    _input.Fire = false;
                    if (!_spawned) { summary.Add(key + " no round"); continue; }

                    WeaponController wc = WeaponRegistry.ActiveController;
                    FirstPersonArms arms = pv.FirstPersonArms;
                    var rows = new StringBuilder();
                    float peakMuzzle = 0f, peakRig = 0f, peakSocket = 0f, peakHand = 0f;
                    int peakFrame = -1;
                    Quaternion socketRest = Quaternion.identity, handRest = Quaternion.identity;
                    bool haveRest = false;
                    for (int f = 0; f <= 45; f++)
                    {
                        if (f > 0) yield return null;
                        Camera cam = Camera.main;
                        Transform muzzle = wc != null ? wc.Muzzle : null;
                        if (cam == null || muzzle == null) break;
                        Transform c = cam.transform;
                        WeaponVisualRig wr = muzzle.GetComponentInParent<WeaponVisualRig>();
                        Transform body = pv.transform;
                        Transform socket = FindDeep(pv.Animator != null ? pv.Animator.transform : null, "socket_weapon_r");
                        Transform hand = pv.Animator != null ? pv.Animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                        Vector3 refFwd = mode == CameraMode.FirstPerson ? c.forward : _spawn.Direction;
                        float muzzlePitch = Pitch(muzzle.forward, refFwd);
                        float rigPitch = wr != null ? Pitch(wr.transform.forward, refFwd) : float.NaN;
                        if (!haveRest && socket != null && hand != null) { socketRest = Quaternion.Inverse(body.rotation) * socket.rotation; handRest = Quaternion.Inverse(body.rotation) * hand.rotation; haveRest = true; }
                        float socketTurn = socket != null ? Quaternion.Angle(socketRest, Quaternion.Inverse(body.rotation) * socket.rotation) : float.NaN;
                        float handTurn = hand != null ? Quaternion.Angle(handRest, Quaternion.Inverse(body.rotation) * hand.rotation) : float.NaN;
                        if (Mathf.Abs(muzzlePitch) > Mathf.Abs(peakMuzzle)) { peakMuzzle = muzzlePitch; peakFrame = f; }
                        if (Mathf.Abs(rigPitch) > Mathf.Abs(peakRig)) peakRig = rigPitch;
                        if (socketTurn > peakSocket) peakSocket = socketTurn;
                        if (handTurn > peakHand) peakHand = handTurn;
                        Vector3 vp = cam.WorldToViewportPoint(muzzle.position);
                        if (f <= 12 || f % 5 == 0)
                        {
                            rows.Append("\n   f").Append(f).Append(" muzzlePitch=").Append(F(muzzlePitch)).Append(" rigPitch=").Append(F(rigPitch))
                                .Append(" vp=(").Append(F(vp.x)).Append(',').Append(F(vp.y)).Append(") socketTurn=").Append(F(socketTurn)).Append(" handTurn=").Append(F(handTurn))
                                .Append(" rigParent=").Append(wr != null && wr.transform.parent != null ? wr.transform.parent.name : "-");
                            if (f == 1 || f == 2 || f == 6)
                            {
                                rows.Append(" | fpArms=").Append(arms != null && arms.Visible ? arms.WeaponSet ?? "(none)" : "off").Append(' ').Append(Clips(arms != null && arms.Visible ? arms.Animator : null));
                                rows.Append(" | tp ").Append(Clips(pv.Animator));
                            }
                        }
                    }

                    string line = key + " peakMuzzlePitch=" + F(peakMuzzle) + "°@f" + peakFrame + " peakRigPitch=" + F(peakRig) + "° peakSocketTurn=" + F(peakSocket) + "° peakHandTurn=" + F(peakHand) + "°";
                    summary.Add(line);
                    Debug.Log("[DD-TEST] sgdiag " + line + rows);
                    Time.captureDeltaTime = 0f;
                    float cycle = Time.time + 1.0f;
                    while (Time.time < cycle) yield return null;
                }
            }

            Debug.Log("[DD-TEST] sgdiag summary\n  " + string.Join("\n  ", summary));
        }

        // Signed pitch of v above the reference forward, degrees (positive = up).
        private static float Pitch(Vector3 v, Vector3 reference)
        {
            float a = Mathf.Asin(Mathf.Clamp(v.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            float b = Mathf.Asin(Mathf.Clamp(reference.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            return a - b;
        }

        private static string Clips(Animator a)
        {
            if (a == null) return "-";
            var sb = new StringBuilder();
            for (int l = 0; l < a.layerCount; l++)
            {
                float w = l == 0 ? 1f : a.GetLayerWeight(l);
                if (w <= 0.01f) continue;
                AnimatorClipInfo[] infos = a.GetCurrentAnimatorClipInfo(l);
                foreach (AnimatorClipInfo ci in infos)
                {
                    if (ci.weight < 0.05f || ci.clip == null) continue;
                    sb.Append(a.GetLayerName(l)).Append(':').Append(ci.clip.name.Replace("AP022_", "").Replace("AP009_player_", "")).Append('·').Append(F(ci.weight * w)).Append(' ');
                }
            }

            return sb.ToString();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++) { Transform t = FindDeep(root.GetChild(i), name); if (t != null) return t; }
            return null;
        }

        private static string F(float v) => v.ToString("0.0", Inv);

        private static void StopSpawning()
        {
            EnemySpawnDirector dir = EnemySpawnDirector.Instance;
            if (dir != null)
            {
                System.Reflection.FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) f.SetValue(dir, false);
                dir.ClearAllEnemies();
            }

            EnemyFactory.ClearAll();
        }

        /// <summary>The trigger on demand, the cursor at screen centre (as VfxWeaponCaptureAgent).</summary>
        private sealed class TapInput : IInputSource
        {
            public bool Fire;
            private bool _was;
            private int _frame = -1;
            private bool _pressed;
            public bool IsEnabled => true;
            public Vector2 Move => Vector2.zero;
            public AimSourceKind AimKind => AimSourceKind.Pointer;
            public Vector2 AimScreenPosition => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            public Vector2 AimStick => Vector2.zero;
            public bool FireHeld => Fire;
            public bool FirePressed
            {
                get
                {
                    if (_frame != Time.frameCount) { _frame = Time.frameCount; _pressed = Fire && !_was; _was = Fire; }
                    return _pressed;
                }
            }

            public bool FireReleased => false;
            public bool ReloadPressed => false;
            public bool PausePressed => false;
            public bool DebugPanelPressed => false;
#pragma warning disable 67
            public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
            public void Enable() { }
            public void Disable() { }
        }
    }
}
