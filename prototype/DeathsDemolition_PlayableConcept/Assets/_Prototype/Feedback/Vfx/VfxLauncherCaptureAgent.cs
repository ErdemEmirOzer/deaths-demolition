#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>Opt-in B3 QA: six controller shots, disposable target, three modes × two flash settings.
    /// A missing stage/mode/blast is a nonzero result. Actual earlier world contacts remain labelled.
    /// Development builds / editor only (as the Diagnostics probes). No reflection: spawning goes through the spawn director's
    /// own F1 toggle, and the rig's input overrides are released to null (no runtime code sets them; only QA agents and tests).</summary>
    public sealed class VfxLauncherCaptureAgent : MonoBehaviour
    {
        private string _folder;
        private readonly TapInput _input = new TapInput();
        private ProjectileSpawnInfo _shot;
        private bool _spawned, _ended, _validEnd, _watching;
        private string _endKind;
        private Vector3 _endPosition;
        private int _shotFrame, _endFrame, _misses, _shots;
        private float _shotTime, _endTime;
        private GameObject _target;
        private PlayerRig _rig;
        private IInputSource _previous;
        private CameraMode _oldMode;
        private ClassicWeaponId _oldWeapon;
        private bool _oldInvulnerable, _hasSnapshot;
        private EnemySpawnDirector _spawnDirector;
        private DebugPanelToggle _spawnToggle;
        private bool _spawnEnabled;
        private float _flash;
        private float _runDeadline;
        private bool _completed;
        private int _blastBefore, _flashBefore, _columnBefore, _scorchBefore;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-ddVfxLauncherCapture") continue;
                var go = new GameObject("VfxLauncherCaptureAgent");
                DontDestroyOnLoad(go);
                go.AddComponent<VfxLauncherCaptureAgent>()._folder = args[i + 1];
                break;
            }
        }
        private void Awake() => _runDeadline = Time.realtimeSinceStartup + 180f;
        private void Update()
        {
            if (_completed || Time.realtimeSinceStartup < _runDeadline) return;
            _completed = true;
            Miss("global realtime deadline exceeded");
            Cleanup();
            Application.Quit(5);
        }
        private void OnEnable()
        {
            ProjectileEvents.Spawned += OnSpawn;
            ProjectileEvents.Impact += OnImpact;
            ProjectileEvents.Expired += OnExpire;
        }
        private void OnDisable()
        {
            ProjectileEvents.Spawned -= OnSpawn;
            ProjectileEvents.Impact -= OnImpact;
            ProjectileEvents.Expired -= OnExpire;
            Cleanup();
        }
        private void Cleanup()
        {
            _watching = false;
            _input.Fire = false;
            if (_hasSnapshot)
            {
                _hasSnapshot = false;
                GameFeelSettings.FlashScale = _flash;
                InputSourceRegistry.SetCurrent(_previous);
                if (_rig != null)
                {
                    _rig.Health.DiagnosisInvulnerable = _oldInvulnerable;
                    _rig.Motor.SetInputSource(null);
                    _rig.Aim.SetInputSource(null);
                }
                if (_spawnToggle != null) _spawnToggle.Set(_spawnEnabled);
                WeaponQaHooks.TrySetClassicWeapon(_oldWeapon);
                CameraModeService.Request(_oldMode);
            }
            if (_target != null) { Destroy(_target); _target = null; }
        }
        private void OnSpawn(ProjectileSpawnInfo p)
        {
            if (!_watching || _spawned || _target == null || p.Kind != DD.Prototype.Core.Weapons.ProjectileKind.Rocket) return;
            _shot = p;
            _spawned = true;
            _shotFrame = Time.frameCount;
            _shotTime = Time.time;
            // QA geometry; actual sweep, gravity, arming and damage still use normal S02 code.
            _target.transform.SetPositionAndRotation(p.Position + p.Direction * 12f, Quaternion.LookRotation(p.Direction));
            Physics.SyncTransforms();
        }
        private void OnImpact(ProjectileImpactInfo p)
        {
            if (!_watching || !_spawned || p.ProjectileId != _shot.ProjectileId || !p.Stopped) return;
            Terminal("impact:" + p.Surface, p.Position, true);
        }
        private void OnExpire(ProjectileExpireInfo p)
        {
            if (!_watching || !_spawned || p.ProjectileId != _shot.ProjectileId) return;
            Terminal("expired:" + p.Reason, p.Position, p.Reason == ProjectileEndReason.Range || p.Reason == ProjectileEndReason.Lifetime);
        }
        private void Terminal(string kind, Vector3 position, bool valid)
        {
            _ended = true;
            _validEnd = valid;
            _endKind = kind;
            _endPosition = position;
            _endFrame = Time.frameCount;
            _endTime = Time.time;
        }
        private void Miss(string detail)
        {
            _misses++;
            Debug.Log("[B3Capture] MISS " + detail + " timeScale=" + Time.timeScale + " mode=" + CameraModeService.Current);
        }
        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            if (Directory.GetFileSystemEntries(_folder).Length != 0)
            { Miss("capture directory is not empty"); _completed = true; Application.Quit(6); yield break; }
            float deadline = Time.realtimeSinceStartup + 120f;
            while ((PlayerRegistry.Primary == null || WeaponRegistry.ActiveController == null || Camera.main == null || PrototypeCameraRig.Active == null)
                   && Time.realtimeSinceStartup < deadline) yield return null;
            if (PlayerRegistry.Primary == null || WeaponRegistry.ActiveController == null || Camera.main == null || PrototypeCameraRig.Active == null)
            { Miss("startup unavailable"); Application.Quit(3); yield break; }
            yield return new WaitForSecondsRealtime(4f);
            if (RocketVisualSystem.Instance == null || ExplosionDirector.Instance == null || !HitFeedbackDirector.Instance.Vfx.IsReady)
            { Miss("VFX systems not ready"); _completed = true; Application.Quit(3); yield break; }
            _rig = PlayerRegistry.Primary;
            _previous = InputSourceRegistry.Current;
            _flash = GameFeelSettings.FlashScale;
            _oldMode = CameraModeService.Current;
            _oldWeapon = WeaponRegistry.ActiveController.Classic.Id;
            _oldInvulnerable = _rig.Health.DiagnosisInvulnerable;
            _spawnDirector = EnemySpawnDirector.Instance;
            _spawnToggle = _spawnDirector != null ? SpawnToggle() : null;
            if (_spawnDirector != null && _spawnToggle == null)
            { Miss("spawn director has no 'Spawning enabled' F1 toggle"); _completed = true; Application.Quit(3); yield break; }
            if (_spawnToggle != null) _spawnEnabled = _spawnToggle.Get();
            _hasSnapshot = true;
            try
            {
                _rig.Health.DiagnosisInvulnerable = true;
                _rig.Motor.SetInputSource(null);
                _rig.Aim.SetInputSource(null);
                InputSourceRegistry.SetCurrent(_input);
                _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _target.name = "B3 isolated QA target";
                _target.layer = PrototypeLayers.Environment;
                _target.transform.localScale = new Vector3(8f, 8f, .3f);
                _target.transform.position = new Vector3(0f, -500f, 0f);
                foreach (CameraMode mode in new[] { CameraMode.TopDown, CameraMode.ThirdPerson, CameraMode.FirstPerson })
                {
                    CameraModeService.Request(mode);
                    deadline = Time.realtimeSinceStartup + 6f;
                    while ((CameraModeService.Current != mode || CameraModeService.IsTransitioning) && Time.realtimeSinceStartup < deadline) yield return null;
                    if (CameraModeService.Current != mode || CameraModeService.IsTransitioning || PrototypeCameraRig.Active == null)
                    { Miss("mode not applied: " + mode); continue; }
                    yield return new WaitForSecondsRealtime(.7f);
                    for (int reduced = 0; reduced < 2; reduced++)
                    {
                        if (Time.timeScale <= 0f) { Miss("scaled simulation frozen"); continue; }
                        _watching = false;
                        if (_spawnDirector != null) { _spawnToggle.Set(false); _spawnDirector.ClearAllEnemies(); }
                        EnemyFactory.ClearAll();
                        ProjectileSystem.Clear();
                        RocketVisualSystem.Instance.Clear();
                        ExplosionDirector.Instance.Clear();
                        _target.transform.position = new Vector3(0f, -500f, 0f);
                        GameFeelSettings.FlashScale = reduced == 0 ? 1f : .4f;
                        if (!WeaponQaHooks.TrySetClassicWeapon(ClassicWeaponId.Rl1) || WeaponRegistry.ActiveController.SpecialWeaponActive || WeaponRegistry.ActiveController.Classic.Id != ClassicWeaponId.Rl1)
                        { Miss("launcher not equipped"); continue; }
                        yield return new WaitForSecondsRealtime(1f);
                        _spawned = false; _ended = false; _validEnd = false; _endKind = "NONE";
                        _endFrame = -1; _endTime = -1f; _endPosition = default;
                        var blast = ExplosionDirector.Instance;
                        _blastBefore = blast.Presentations; _flashBefore = blast.FlashCards; _columnBefore = blast.Columns; _scorchBefore = blast.Scorches;
                        string key = mode.ToString().ToLowerInvariant() + "-" + (reduced == 0 ? "default" : "reduced");
                        _watching = true; _input.Fire = true;
                        deadline = Time.realtimeSinceStartup + 2f;
                        while (!_spawned && Time.realtimeSinceStartup < deadline) yield return null;
                        _input.Fire = false;
                        if (!_spawned) { Miss(key + " no actual rocket"); _watching = false; continue; }
                        _shots++;
                        yield return new WaitForEndOfFrame(); Grab(key, "fire", mode);
                        deadline = Time.realtimeSinceStartup + 2f;
                        while (Time.time - _shotTime < .15f && !_ended && Time.realtimeSinceStartup < deadline) yield return null;
                        yield return new WaitForEndOfFrame();
                        if (!RocketVisualSystem.Instance.TryVisual(_shot.ProjectileId, out _)) Miss(key + " no live flight at flight capture");
                        if (Time.time - _shotTime < .15f) Miss(key + " flight clock did not reach .15s");
                        Grab(key, "flight", mode);
                        deadline = Time.realtimeSinceStartup + 3f;
                        while (!_ended && Time.realtimeSinceStartup < deadline) yield return null;
                        if (!_ended || !_validEnd) { Miss(key + " valid terminal event absent"); _watching = false; continue; }
                        yield return new WaitForEndOfFrame(); Grab(key, "terminal", mode);
                        if (RocketVisualSystem.Instance.TryVisual(_shot.ProjectileId, out _)) Miss(key + " terminal identity still active");
                        deadline = Time.realtimeSinceStartup + 2f;
                        while (Time.time - _endTime < .21f && Time.realtimeSinceStartup < deadline) yield return null;
                        yield return new WaitForEndOfFrame();
                        if (Time.time - _endTime < .21f) Miss(key + " stage clock did not reach .21s");
                        Grab(key, "stages", mode);
                        if (blast.Columns - _columnBefore != 1) Miss(key + " nonflash column missing");
                        if (reduced == 0 && blast.FlashCards - _flashBefore != 1) Miss(key + " default flash baseline missing");
                        if (blast.Presentations - _blastBefore != 1) Miss(key + " expected one armed blast");
                        if (reduced == 1 && blast.FlashCards - _flashBefore != 0) Miss(key + " reduced flash card present");
                        _watching = false;
                        yield return new WaitForSecondsRealtime(1.7f);
                    }
                }
            }
            finally { Cleanup(); }
            Debug.Log("[B3Capture] DONE actualShots=" + _shots + " misses=" + _misses);
            _completed = true;
            Application.Quit(_misses == 0 && _shots == 6 ? 0 : 4);
        }
        /// <summary>The spawn director's own F1 toggle ("Spawning enabled", Spawn tab) — its public switch, not its private field.</summary>
        private static DebugPanelToggle SpawnToggle()
        {
            var entries = DebugPanelRegistry.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] is DebugPanelToggle t && t.Tab == DebugPanelTabs.Spawn && t.Label == "Spawning enabled") return t;
            return null;
        }
        private void Grab(string key, string stage, CameraMode requested)
        {
            if (CameraModeService.Current != requested || CameraModeService.IsTransitioning || PrototypeCameraRig.Active == null || !PrototypeCameraRig.Active.isActiveAndEnabled || Camera.main == null || PrototypeCameraRig.Active.IsViewmodelOverlayStacked != (requested == CameraMode.FirstPerson)) { Miss(key + " camera mismatch"); return; }
            if (stage == "fire" && (!RocketVisualSystem.Instance.TryVisual(_shot.ProjectileId, out _) || RocketVisualSystem.Instance.ExhaustAlive <= 0)) Miss(key + " first captured fire frame lacks live rocket/exhaust");
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            string name = key + "-" + stage + ".png";
            File.WriteAllBytes(Path.Combine(_folder, name), texture.EncodeToPNG());
            Destroy(texture);
            var rocket = RocketVisualSystem.Instance;
            var blast = ExplosionDirector.Instance;
            bool live = rocket.TryVisual(_shot.ProjectileId, out Vector3 position);
            string projected = live ? "live" : _ended ? "terminal" : "spawn";
            Vector3 viewport = Camera.main.WorldToViewportPoint(live ? position : _ended ? _endPosition : _shot.Position);
            var bounds = _target.GetComponent<Collider>().bounds; bounds.Expand(.5f);
            string line = "[B3Capture] " + name + " projectile=" + _shot.ProjectileId + " source=actual-controller-QA-input target=isolated-environment-12m mode=" + CameraModeService.Current + " rigActive=" + PrototypeCameraRig.Active.isActiveAndEnabled + " fpOverlay=" + PrototypeCameraRig.Active.IsViewmodelOverlayStacked + " frame=" + Time.frameCount + " shotFrame=" + _shotFrame + " shotTime=" + _shotTime + " dt=" + (Time.time - _shotTime) + " end=" + _endKind + " endFrame=" + _endFrame + " endTime=" + _endTime + " targetReached=" + (_ended && bounds.Contains(_endPosition)) + " projected=" + projected + " live=" + live + " viewport=" + viewport + " authoredRocket=" + rocket.UsesAuthoredRocket + " seatHidden=" + rocket.SeatedRocketHidden + " seatRenderers=" + rocket.SeatedRocketRenderers + " exhaust=" + rocket.ExhaustAlive + " smoke=" + rocket.Smoke.particleCount + " blastDelta=" + (blast.Presentations - _blastBefore) + " flashScale=" + GameFeelSettings.FlashScale + " flashDelta=" + (blast.FlashCards - _flashBefore) + " columnDelta=" + (blast.Columns - _columnBefore) + " scorchDelta=" + (blast.Scorches - _scorchBefore);
            Debug.Log(line);
            File.AppendAllText(Path.Combine(_folder, "capture-ledger.txt"), line + "\n");
        }
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
