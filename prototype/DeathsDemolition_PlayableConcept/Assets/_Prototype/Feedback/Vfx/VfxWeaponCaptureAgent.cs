#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 fire10 · AD evidence for the fire VFX of all ten classic weapons: <c>-ddVfxWeaponCapture &lt;folder&gt;</c> on a
    /// <c>-ddDirectToRun</c> run (still camera, no QA bot). Spawning stops and the field is cleared; then, per weapon (ar_1 …
    /// rl_1, or the keys of <c>-ddVfxWeaponCaptureOnly a,b</c>), it equips it through <see cref="WeaponQaHooks"/>, waits for the
    /// raise, taps the trigger through its own input source until a full-tracer round leaves (the class's every-Nth; a pellet
    /// or rocket weapon: the first shot) and frames <c>&lt;mode&gt;-&lt;key&gt;-flash.png</c> one frame after that shot and
    /// <c>&lt;mode&gt;-&lt;key&gt;-casing.png</c> 0.15 s after it (sg_1: 0.03 s after its actual pump event), logging the muzzle / ejection sockets against the camera, the
    /// round's origin, the newest casing's travel and the VFX counters. Opt-in only; writes nothing else.
    /// </summary>
    public sealed class VfxWeaponCaptureAgent : MonoBehaviour
    {
        private const string Flag = "-ddVfxWeaponCapture";
        private const string OnlyFlag = "-ddVfxWeaponCaptureOnly";
        private const float CasingDelay = 0.15f;

        private static readonly ClassicWeaponId[] Order =
        {
            ClassicWeaponId.AssaultRifle, ClassicWeaponId.AutoSmg, ClassicWeaponId.CombatShotgun, ClassicWeaponId.Ar2, ClassicWeaponId.Smg2,
            ClassicWeaponId.Sg2, ClassicWeaponId.Lmg1, ClassicWeaponId.Lmg2, ClassicWeaponId.Sr1, ClassicWeaponId.Rl1
        };

        private string _folder;
        private string _only;
        private string _mode;
        private readonly TapInput _input = new TapInput();

        // The last round spawned by the weapon under test (pellet 0 for a shotgun).
        private bool _spawned;
        private ProjectileSpawnInfo _spawn;
        private int _spawnFrame;
        private ClassicWeaponId _watch;
        private float _casingAt = -1f;
        private Vector3 _casingPort;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string folder = null, only = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == Flag) folder = args[i + 1];
                else if (args[i] == OnlyFlag) only = args[i + 1];
            }

            if (folder == null) return;
            // M-55 (C-00 30.09): a capture run writes frames and plays a run, so it only starts inside a sandbox
            // (-ddProfilePath → ProfileStore, or -ddUiDataRoot / QA bot → UiDataPaths; both set BeforeSceneLoad).
            if (!DD.Prototype.Profile.ProfileStore.HasPathOverride && !DD.Prototype.UI.UiDataPaths.IsProcessLocal)
            {
                Debug.LogError("[Capture] refused: no sandbox (" + Flag + ")");
                Application.Quit(2);
                return;
            }

            var go = new GameObject("VfxWeaponCaptureAgent");
            DontDestroyOnLoad(go);
            VfxWeaponCaptureAgent agent = go.AddComponent<VfxWeaponCaptureAgent>();
            agent._folder = folder;
            agent._only = only;
        }

        private void OnEnable() { ProjectileEvents.Spawned += OnSpawned; WeaponEvents.Mechanism += OnMechanism; }
        private void OnDisable() { ProjectileEvents.Spawned -= OnSpawned; WeaponEvents.Mechanism -= OnMechanism; }

        private void OnMechanism(WeaponMechanismEvent info)
        {
            if (info.Weapon != _watch || info.Kind != WeaponMechanismKind.CasingEjected) return;
            _casingAt = Time.time;
            _casingPort = info.Position;
        }

        private void OnSpawned(ProjectileSpawnInfo info)
        {
            if (info.WeaponId != _watch || info.PelletIndex != 0) return;
            _spawned = true;
            _spawn = info;
            _spawnFrame = Time.frameCount;
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            float deadline = Time.realtimeSinceStartup + 240f;
            while ((WeaponRegistry.ActiveController == null || Camera.main == null || PlayerRegistry.Primary == null || HitFeedbackDirector.Instance == null)
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return new WaitForSeconds(4f);
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null || WeaponRegistry.ActiveController == null)
            {
                Debug.Log("[VfxWeaponCapture] no player / weapon — abort");
                Application.Quit(3);
                yield break;
            }

            rig.Health.DiagnosisInvulnerable = true;   // evidence only
            StopSpawning();
            InputSourceRegistry.SetCurrent(_input);
            rig.Motor.SetInputSource(null);
            rig.Aim.SetInputSource(null);
            _mode = CameraModeService.Current == CameraMode.FirstPerson ? "fp" : CameraModeService.Current == CameraMode.ThirdPerson ? "tp" : "topdown";
            yield return new WaitForSeconds(0.5f);

            int done = 0, missed = 0;
            foreach (ClassicWeaponId id in Order)
            {
                WeaponDefinition def = Arsenal.Get(id);
                string key = def != null ? def.weaponId : id.ToString();
                if (!string.IsNullOrEmpty(_only) && Array.IndexOf(_only.Split(','), key) < 0) continue;
                StopSpawning();   // the director may have re-armed on a round edge
                if (!WeaponQaHooks.TrySetClassicWeapon(id))
                {
                    Debug.Log("[VfxWeaponCapture] " + key + " not equippable");
                    missed++;
                    continue;
                }

                yield return new WaitForSeconds(1.0f);   // the switch-in clip (0.6 s) and the FP pose settle
                bool ok = false;
                yield return Shoot(id, key, r => ok = r);
                if (ok) done++;
                else missed++;
                yield return new WaitForSeconds(0.6f);
            }

            Debug.Log("[VfxWeaponCapture] done weapons=" + done + " missed=" + missed + " mode=" + _mode + " tier=" + VfxTier.Current);
            Application.Quit(missed == 0 && done > 0 ? 0 : 4);   // M-55: a missed weapon (or none captured) fails the run
        }

        private static void StopSpawning()
        {
            EnemySpawnDirector dir = EnemySpawnDirector.Instance;
            if (dir != null)
            {
                System.Reflection.FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) f.SetValue(dir, false);
                dir.ClearAllEnemies();
            }

            EnemyFactory.ClearAll();
        }

        // Taps the trigger (one frame held) until the weapon's full-tracer round spawns, then frames it.
        private IEnumerator Shoot(ClassicWeaponId id, string key, Action<bool> result)
        {
            WeaponController wc = WeaponRegistry.ActiveController;
            VfxWeaponProfile profile = VfxWeaponProfiles.For(id);
            _watch = id;
            int taps = profile != null && profile.TracerEveryN > 1 ? profile.TracerEveryN + 2 : 2;
            for (int tap = 0; tap < taps; tap++)
            {
                _spawned = false;
                _casingAt = -1f;
                _input.Fire = true;
                float until = Time.time + 1.6f;
                while (!_spawned && Time.time < until) yield return null;
                _input.Fire = false;
                if (!_spawned)
                {
                    Debug.Log("[VfxWeaponCapture] " + key + " tap " + tap + ": no round (ammo=" + (wc.Classic != null ? wc.Classic.Ammo : -1) + ")");
                    continue;
                }

                // A burst keeps firing without the trigger: watch its later rounds too.
                while (true)
                {
                    bool tracerRound = _spawn.Kind == ProjectileKind.Pellet || _spawn.Kind == ProjectileKind.Rocket || profile == null
                                       || profile.TracerEveryN <= 0 || TracerRibbonRenderer.IsTracerRound(profile, _spawn.ProjectileId);
                    if (tracerRound)
                    {
                        yield return Capture(wc, key, profile);
                        result(true);
                        yield break;
                    }

                    int seen = _spawn.ProjectileId;
                    float burstUntil = Time.time + 0.25f;
                    while (_spawn.ProjectileId == seen && Time.time < burstUntil) yield return null;
                    if (_spawn.ProjectileId == seen) break;
                }

                float cycle = Time.time + 0.45f;   // past the weapon's cycle before the next tap
                while (Time.time < cycle) yield return null;
            }

            Debug.Log("[VfxWeaponCapture] " + key + " no tracer round after " + taps + " taps");
            result(false);
        }

        private IEnumerator Capture(WeaponController wc, string key, VfxWeaponProfile profile)
        {
            ProjectileSpawnInfo shot = _spawn;
            int shotFrame = _spawnFrame;
            float shotTime = Time.time;
            Transform muzzle = wc.Muzzle;
            WeaponVisualRig rig = muzzle != null ? muzzle.GetComponentInParent<WeaponVisualRig>() : null;
            Transform port = rig != null ? rig.Ejection : muzzle;
            Vector3 portAtShot = port != null ? port.position : Vector3.zero;
            VfxDressing dressing = HitFeedbackDirector.Instance != null ? HitFeedbackDirector.Instance.Vfx : null;

            // AD r4 (01.10): the sheet peaks at f2–4 — grab at shot + 3 frames, not the birth cell (one frame after).
            yield return null;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            CasingEffect casing = NewestCasing(dressing, portAtShot);
            Grab(key, "flash", shot, shotFrame, shotTime, muzzle, port, portAtShot, casing, dressing, profile);

            if (_watch == ClassicWeaponId.CombatShotgun)
            {
                float until = shotTime + 1.2f;
                while (_casingAt < shotTime && Time.time < until) yield return null;
                if (_casingAt >= shotTime)
                {
                    while (Time.time - _casingAt < 0.03f) yield return null;
                    portAtShot = _casingPort;
                    casing = NewestCasing(dressing, portAtShot);
                }
            }
            else
            {
                while (Time.time - shotTime < CasingDelay) yield return null;
            }
            yield return new WaitForEndOfFrame();
            Grab(key, "casing", shot, shotFrame, shotTime, muzzle, port, portAtShot, casing, dressing, profile);
        }

        // The casing nearest the port right after the shot (the one this shot ejected), or null.
        private static CasingEffect NewestCasing(VfxDressing dressing, Vector3 port)
        {
            if (dressing == null || dressing.CasingPool == null) return null;
            CasingEffect best = null;
            float bestD = 0.5f * 0.5f;
            var list = dressing.CasingPool.Spawned;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || list[i].IsResting) continue;
                float d = (list[i].transform.position - port).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = list[i];
                }
            }

            return best;
        }

        private void Grab(string key, string name, ProjectileSpawnInfo shot, int shotFrame, float shotTime, Transform muzzle, Transform port,
            Vector3 portAtShot, CasingEffect casing, VfxDressing dressing, VfxWeaponProfile profile)
        {
            string file = Path.Combine(_folder, _mode + "-" + key + "-" + name + ".png");
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(file, frame.EncodeToPNG());
            Destroy(frame);

            Camera cam = Camera.main;
            Transform c = cam != null ? cam.transform : null;
            string line = "[VfxWeaponCapture] " + file + " key=" + key + " dFrames=" + (Time.frameCount - shotFrame) + " dt=" + F(Time.time - shotTime)
                          + " round=" + shot.ProjectileId + " kind=" + shot.Kind
                          + " mechanismDt=" + (_casingAt >= shotTime ? F(_casingAt - shotTime) : "none");
            if (muzzle != null && c != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(muzzle.position);
                line += " muzzle=" + V(muzzle.position) + " muzzleVp=(" + F(vp.x) + "," + F(vp.y) + "," + F(vp.z) + ")"
                        + " fwd∠cam=" + F(Vector3.Angle(muzzle.forward, c.forward)) + " fwd∠round=" + F(Vector3.Angle(muzzle.forward, shot.Direction))
                        + " fwd·camRight=" + F(Vector3.Dot(muzzle.forward, c.right)) + " fwd·camUp=" + F(Vector3.Dot(muzzle.forward, c.up))
                        + " roundOrigin−muzzle=" + F(Vector3.Distance(shot.Position, muzzle.position));
            }

            if (port != null && c != null)
            {
                Vector3 pvp = cam.WorldToViewportPoint(port.position);
                line += " port=" + V(port.position) + " portVp=(" + F(pvp.x) + "," + F(pvp.y) + ")" + " portRight·camRight=" + F(Vector3.Dot(port.right, c.right));
            }

            if (casing != null && c != null && casing.IsSpawned)
            {
                Vector3 d = casing.transform.position - portAtShot;
                Vector3 cvp = cam.WorldToViewportPoint(casing.transform.position);
                line += " casingTravel=" + F(d.magnitude) + " casing·camRight=" + F(Vector3.Dot(d, c.right)) + " casing·up=" + F(d.y)
                        + " casing·camFwd=" + F(Vector3.Dot(d, c.forward)) + " casingVp=(" + F(cvp.x) + "," + F(cvp.y) + "," + F(cvp.z) + ")";
            }
            else
            {
                line += " casing=none";
            }

            // The in-flight round (tracer) as the renderer sees it this frame.
            for (int i = 0; i < ProjectileSystem.LiveCount; i++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(i);
                if (p.Generation != shot.ProjectileId) continue;
                Vector3 hvp = cam != null ? cam.WorldToViewportPoint(p.Position) : Vector3.zero;
                line += " roundTravelled=" + F(p.Travelled) + " roundVp=(" + F(hvp.x) + "," + F(hvp.y) + "," + F(hvp.z) + ")";
                break;
            }

            TracerRibbonRenderer r = TracerRibbonRenderer.Exists ? TracerRibbonRenderer.Instance : null;
            MuzzleSystem m = MuzzleSystem.Instance;
            line += " tracerEveryN=" + (profile != null ? profile.TracerEveryN : -1)
                    + " core=" + (r != null ? r.DrawnCore : -1) + " glow=" + (r != null ? r.DrawnGlow : -1) + " glint=" + (r != null ? r.DrawnGlint : -1)
                    + " muzzlePlays=" + (dressing != null ? dressing.MuzzlePlays : -1) + " variant=" + (dressing != null ? dressing.LastMuzzleVariant.ToString() : "-")
                    + " note=" + (dressing != null ? dressing.LastMuzzleNote : "-") + " casingsEjected=" + (dressing != null ? dressing.CasingsEjected : -1)
                    + " lights=" + (m != null ? m.LitCount : -1) + " smoke=" + (m != null ? m.SmokeAlive : -1) + " tier=" + VfxTier.Current;
            Debug.Log(line);
        }

        private static string F(float v) => v.ToString("0.000", Inv);
        private static string V(Vector3 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";

        /// <summary>Capture-driven input: the trigger on demand, the cursor at screen centre, nothing else.</summary>
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
                    if (_frame != Time.frameCount)
                    {
                        _frame = Time.frameCount;
                        _pressed = Fire && !_was;
                        _was = Fire;
                    }

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
#endif
