#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using DD.Prototype.Audio;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>AP-043, opt-in native TD capture, ONE named case per guarded DEV Player.
    /// Scope: normal Arsenal Bullet/Pellet FLIGHT only. Real WeaponController input and authored flight parameters. Fixed 60 Hz simulation timestamps are NOT a
    /// real-time performance result. Never moves/replaces the runtime camera or changes projectile state/speed.
    /// Staged outside the worktree: must compile and run before any measurement/ready claim.</summary>
    public sealed class Ap043ProjectileCaptureAgent : MonoBehaviour
    {
        public const string Flag = "-ddAp043Capture";
        private const string AllowedRoot = "/Users/Erdem/Dev/KADRO/context/projects/deaths-demolition/asset-production/AP-043/06-renders/";
        private const long DiskFloor = 20L * 1024 * 1024 * 1024;
        private const int JpegQuality = 95, FireStart = 60, PulsePeriod = 90, RequiredCrowd = 40, CrowdFillTarget = RequiredCrowd;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static bool s_seeded;
        private static int s_seed;
        private string _out, _caseName, _family, _ground, _population, _element, _look;
        private int _tier, _frames = 1500, _rawMb = 2048, _caseFrame = -1, _shots, _impacts;
        private long _bytes, _limit;
        private bool _done, _configured, _oldSpawn, _oldInvulnerable, _preview;
        private bool _crowdClearFlight;
        private string _treeMode = "none";
        private string[] _targetedNodes;
        private StreamWriter _targetedTreeStates, _targetedEvents;
        private TargetedSetupSample _targetedSetup;
        private int ActualFireStart => _treeMode == "seeker" ? 12 : FireStart;
        private float _oldCaptureDelta;
        private double _captureReal0;
        private PlayerRig _rig;
        private WeaponController _weapon;
        private Camera _camera;
        private Renderer _visualGroundRenderer;
        private bool _visualGroundSearched;
        private EnemySpawnDirector _director;
        private IInputSource _oldInput;
        private ClassicWeaponId _classic;
        private readonly CaptureInput _input = new CaptureInput();
        private readonly Vector3[] _path = new Vector3[64];
        private ProjectileSpawnInfo _lastSpawn;
        private int _lastSpawnFrame = -1;
        private Vector3 _requestedPlayer, _actualPlayer, _aim, _lastTerminal;
        private string _terminalKind;
        private StreamWriter _times, _frameMeta, _eventMeta, _audioTimes;
        private CaseReport _report;

        [Serializable] private sealed class CaseReport
        {
            public string schema = "AP043-capture-v1", caseName, lookRequested, status = "STAGED_UNMEASURED", stopReason;
            public bool lookApplicationVerified = false;
            public string lookVerificationMeaning = "Runtime startup enum+selected private material keyword/uniform configuration ONLY; actual pixel appearance/GPU shader usage NOT_DONE";
            public bool actualRenderAppearanceMeasured = false, shaderGpuUsageMeasured = false;
            public int lookBindingFramesVerified, lookBindingFramesUnknown;
            public RuntimeOptionProof lastRuntimeOptionProof;
            public string family, requestedGround, population, element, treeCoverage, sceneName;
            public string screenPointOrigin = "Unity bottom-left; native PNG top-left convention requires y_image=H-1-y_screen validation";
            public string bodyAnchorDefinition = "rig.position feet; rig.position+up*1.8m nominal head, NOT measured mesh silhouette";
            public string source = "native DEV Player backbuffer, normal Bullet/Pellet flight, EndOfFrame, actual WeaponController input";
            public string scope = "9 normal weapons FLIGHT ONLY; muzzle/impact/casing/decal remain existing context";
            public string clock = "fixed simulation index / 60; realtime and DSP anchors recorded separately";
            public string preRollAimProtocol = "Actual CaptureInput pointer aim initialized at install and updated every180warmup/crowdfill frame; natural TD camera unchanged";
            public int seed, width, height, tier, framesRequested, framesWritten, shots, impacts, initialCrowdWithin15m, startRound;
            public int requestedAltElementTier, requestedPreviewRank;
            public string treeCaseCaption;
            public TreeStateSample initialTreeState;
            public WeaponIdentitySample actualWeaponProof;
            public int requiredCrowdWithin15m, minCrowdWithin15mDuringCapture = -1, crowdFramesBelowRequired;
            public string crowdProof = "Initial live population >=40 within15m required for crowd; each captured frame/minimum recorded, not inferred from target";
            public string resolutionPolicy = "Native1920x1080(primary),2560x1440,1496x842; no scale/resample; every screenshot must match initial native Screen size";
            public int videoJpegQuality = JpegQuality;
            public string videoSourceCodec = "JPEG EncodeToJPG95; lossy viewing source, never pixel metrics";
            public string nativeStillCodec = "PNG lossless independent encode from same captured texture";
            public int nativeHeroShotLimit = 6;
            public string nativeHeroAges = "1,2,3,6,9 engine frames after actual spawn; O3 sparse tracer cadence reviewed from actual records";
            public long writtenBytes, rawLimitBytes;
            public bool measured = false, pixelMetricsDone = false, sameFrameVfxDiff = false;
            public bool cameraDefaultsPreserved = true, reflectionEnvironmentPreserved = true;
            public bool bitExactDeterminismProven = false, realTime60FpsProven = false;
            public bool actualControllerShots = true, projectilePhysicsChanged = false;
            public Vector3 playerRequested, playerActual, aim;
            public float simulationSeconds, wallSeconds, captureDeltaBefore, fixedDeltaTime;
            public GroundSample playerGround, aimGround;
            public VisualGroundSample visualGround; // Scenario aim anchor only; not projectile ROI proof.
            public CameraSample camera;
        }
        [Serializable] private sealed class GroundSample
        {
            public bool hit, requestedLabelVerified = false;
            public string collider, renderer;
            public string[] materialNames, materialShaderNames;
            public GroundMaterialSample[] sharedMaterialSamples;
            public int selectedMaterialSlot = -1;
            public string materialSlotStatus = "UNKNOWN", selectedSharedMaterialName, selectedShaderName;
            public string materialIdentityMeaning = "Readonly sharedMaterial.name/instanceId; asset path/GUID unavailable in Player";
            public Vector3 point, normal;
            public int triangleIndex;
            public string note = "Actual world ray hit; no synthetic Unlit test plane. Label awaits material/pixel review.";
        }
        [Serializable] private sealed class CameraSample
        {
            public Vector3 position, euler;
            public float fov, aspect, near, far, projectionM11, heightAbovePlayer;
            public float[] projection;
            public int cullingMask, screenWidth, screenHeight, cameraPixelWidth, cameraPixelHeight;
            public bool nativeBackbuffer, allowHdr, postEnabled, postKnown;
            public string postRead = "URP AdditionalCameraData reflection";
        }
        [Serializable] private sealed class EventSample
        {
            public string kind, weapon, projectileKind, surface, terminal;
            public int simulationFrame, engineFrame, shotIndex, projectileId, pelletIndex, volleyIndex;
            public float time, speed, sizeScale;
            public Vector3 position, direction;
            public bool flesh, stopped;
        }
        [Serializable] private sealed class FrameSample
        {
            public int index, engineFrame, shotIndex, projectileId, ageFrames, crowdWithin15m;
            public double simulationT, realtimeT, dspT;
            public long audioFrames;
            public bool projectileLive, sameFrameVfxOnlyMask = false;
            public int nativeScreenshotWidth, nativeScreenshotHeight;
            public int videoJpegQuality = JpegQuality;
            public string nativeStillCodec = "PNG lossless; same captured texture";
            public string actualOption;
            public bool optionActive;
            public RuntimeOptionProof runtimeOptionProof;
            public TreeStateSample actualTreeState;
            public GroundSample nativeFlightHeadGround;
            public VisualGroundSample visualGround; // Same tracked projectileId/headWorld or null; no scenario fallback.
            public string nativeStillPath; // PNG from this same EndOfFrame texture; never a decoded JPEG/video still.
            public Vector3 projectileHead, projectileOrigin, projectileVelocity;
            public Vector3 muzzleWorld, bodyFeetScreen, bodyHeadScreen, muzzleScreen;
            public Vector3[] retainedProjectilePath;
            public int pathCount, liveProjectiles, tracerCore, tracerGlow, tracerGlint, tracerTailOuts;
            public int muzzleLights, muzzleSmoke, impactSparks, impactDebris, impactCount;
            public FlightGeometry[] tracerGeometry;
            public string tracerGeometryReadStatus;
            public string maskStatus = "NATIVE_MASK_NOT_DONE: readonly geometry is ROI provenance, not per-layer coverage";
            public CameraSample camera;
        }

        [Serializable] private sealed class CrowdEnemyPose
        {
            public int instanceId;
            public bool alive;
            public Vector3 position;
            public float distanceFromHero, outwardDot, firingDot;
        }
        [Serializable] private sealed class TargetedSetupSample
        {
            public string schema = "AP043-targeted-setup-v1", mode = "crowd-clear-flight", status = "PREPARING", failure;
            public string protocol = "Existing live40 naturally converge within6m; only hero teleports to NavMesh8m outside, aim9m away; natural TD camera30frame settle";
            public bool crowdClearFlightRequested, performed, verified;
            public bool npcTransformsWritten = false, npcHealthWritten = false, npcInvulnerabilityWritten = false;
            public bool npcAiOrSpeedWritten = false, projectilePhysicsWritten = false, damageWritten = false;
            public bool cameraLensOrRigWritten = false, existingPlayerDiagnosisInvulnerable;
            public string writeFlagsMeaning = "Direct capture-agent mutations only; explicit TreeMode grants authored existing nodes and their normal mechanics apply";
            public int safetyCeilingBefore, safetyCeilingAfter, liveBefore, liveAfter;
            public int crowdWithin15mBefore, crowdWithin15mAfter, packedWithin6m, packedWithinRequestedRadius;
            public int packEngineFrame, teleportEngineFrame, settleEngineFrame, settleFrames, naturalWaitFrames, captureFinishedEngineFrame;
            public float naturalWaitWallSeconds, requestedOuterMetres = 8f, actualOuterMetres, aimDistanceMetres = 9f;
            public Vector3 oldHeroPose, outerRequestedHeroPose, outerNavMeshHeroPose, actualHeroPose, outwardDirection, aim;
            public CameraSample cameraBefore, cameraAfter;
            public CrowdEnemyPose[] beforeEnemies, afterEnemies;
            public TreeStateSample actualInitialTreeState;
            public string treeModeRequested;
            public string[] grantedNodeIds;
            public TargetedTreeState initialTreeModeState;
            public int firstFireFrame;
            public float requiredNaturalPackRadiusMetres;
            public Vector3 originalRequestedHeroPose, packCentroidBefore, firingDirection;
        }
        [Serializable] private sealed class TargetedTreeState
        {
            public int engineFrame, actualShotTier, actualChaosTier, extraShots;
            public float extraShotSeekDegrees, seekDegrees, holeReadyAt, observedTime;
            public string treeMode, core, meaning = "Readonly real existing tree Stats/Owning nodes; no armed/cooldown/path writes";
            public bool allRequestedNodesOwned, verified, holeReadyBySource;
            public string[] requestedNodes;
        }
        [Serializable] private sealed class TargetedSpawnState
        {
            public int simulationFrame, engineFrame, shotIndex, projectileId, pelletIndex, volleyIndex, actualTreeFlags;
            public bool projectileStateKnown, actualMarkedChaosBit;
            public string meaning = "Readonly actual ProjectileSystem state at real Spawned callback; no tags/trajectory modification";
        }
        private static bool HasArg(string key) => Array.IndexOf(Environment.GetCommandLineArgs(), key) >= 0;

        private static string Arg(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
            return null;
        }
        private static int IntArg(string key, int fallback, int min, int max)
        {
            string value = Arg(key);
            if (value == null) return fallback;
            int n;
            if (!int.TryParse(value, NumberStyles.Integer, Inv, out n) || n < min || n > max)
                throw new ArgumentException(key + " is outside " + min + ".." + max);
            return n;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() { s_seeded = false; s_seed = 0; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SeedEarly()
        {
            if (Arg(Flag) == null) return;
            try { s_seed = IntArg("-ddAp043Seed", 26, 0, int.MaxValue); UnityEngine.Random.InitState(s_seed); s_seeded = true; }
            catch (Exception e) { Debug.LogError("[AP043Capture] " + e.Message); Application.Quit(64); }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string folder = Arg(Flag);
            if (folder == null) return;
            try
            {
                if (!ProfileStore.HasPathOverride || !UiDataPaths.IsProcessLocal || Arg("-ddTelemetryDir") == null)
                    throw new ArgumentException("Full sandbox profile/UI/telemetry required");
                // These managers own input, camera positioning or quit. Never combine them with this agent.
                string[] conflicts = { "-ddTdCapture", "-ddMkCapture", "-ddQaBot", "-ddVfxWeaponCapture", "-ddVfxFireCapture",
                                       "-ddVfxLauncherCapture", "-ddVfxSpecialCapture", "-ddVfxImpactCapture" };
                foreach (string key in conflicts) if (Arg(key) != null) throw new ArgumentException("Concurrent capture manager: " + key);
                string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
                RefuseReparseAncestors(full);
                if (!full.StartsWith(AllowedRoot, StringComparison.Ordinal) || File.Exists(Path.Combine(full, "scenario.json")) || Directory.Exists(Path.Combine(full, "video", "frames")))
                    throw new ArgumentException("Output must be a fresh AP-043/06-renders session");
                foreach (string key in new[] { "-ddProfilePath", "-ddUiDataRoot", "-ddTelemetryDir" })
                    if (Arg(key) == null || !Path.GetFullPath(Arg(key)).StartsWith(full + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new ArgumentException("Sandbox input outside this AP session: " + key);
                foreach (string key in new[] { "-ddProfilePath", "-ddUiDataRoot", "-ddTelemetryDir" }) RefuseReparseAncestors(Path.GetFullPath(Arg(key)));
                if (!s_seeded) throw new ArgumentException("BeforeSceneLoad seed absent");
                var host = new GameObject("AP043 Projectile Capture");
                DontDestroyOnLoad(host);
                var a = host.AddComponent<Ap043ProjectileCaptureAgent>();
                a._out = full;
                a._caseName = Arg("-ddAp043Case") ?? "ar_1.slate.empty.none.1";
                a._frames = IntArg("-ddAp043Frames", 1500, 120, 1800);
                a._rawMb = IntArg("-ddAp043RawMB", 2048, 64, 2048);
                a._limit = a._rawMb * 1024L * 1024;
                a._look = Arg("-ddBulletLook") ?? "O2";
                a._crowdClearFlight = HasArg("-ddAp043CrowdClearFlight");
                a._treeMode = Arg("-ddAp043TreeMode") ?? "none";
                a.ParseCase();
            }
            catch (Exception e) { Debug.LogError("[AP043Capture] refused " + e); Application.Quit(64); }
        }
        private static void RefuseReparseAncestors(string path)
        {
            for (string cursor = path; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
                if ((Directory.Exists(cursor) || File.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Capture output/sandbox symlink refused");
        }
        private void ParseCase()
        {
            string[] t = _caseName.Split('.');
            if (t.Length != 5) throw new ArgumentException("Case: normal-weapon.slate|lightstone.empty|crowd.none|fire|ice|shock|venom|fusion|ladder.1..5");
            _family = t[0]; _ground = t[1]; _population = t[2]; _element = t[3];
            if (!int.TryParse(t[4], out _tier) || _tier < 1 || _tier > 5) throw new ArgumentException("Tier 1..5 required");
            if (_ground != "slate" && _ground != "lightstone") throw new ArgumentException("Unknown world ground candidate");
            if (_population != "empty" && _population != "crowd") throw new ArgumentException("Population empty|crowd required");
            if (_crowdClearFlight && _population != "crowd") throw new ArgumentException("Clear-flight pose is an explicit crowd-only diagnostic");
            if (_treeMode != "none" && _treeMode != "marked" && _treeMode != "seeker") throw new ArgumentException("TreeMode none|marked|seeker required");
            if (_treeMode != "none" && _element != "none") throw new ArgumentException("Targeted real-node mode must use the neutral element case");
            if (_treeMode == "seeker" && (!_crowdClearFlight || _population != "crowd")) throw new ArgumentException("Seeker mode requires explicit clear-flight crowd pose");
            if (_element != "none" && _element != "fire" && _element != "ice" && _element != "shock" && _element != "venom" && _element != "fusion" && _element != "ladder")
                throw new ArgumentException("Unknown element");
            if (_element == "fusion" && _tier < 2) throw new ArgumentException("Fusion requires two distinct elementII parents; fusion.1 is not normally reachable");
            if (_element != "none" && _element != "ladder" && _tier > 3) throw new ArgumentException("Real element nodes have tiers I-III only");
            if (!WeaponIdMigration.TryGetRuntimeId(_family, out _classic) || _classic == ClassicWeaponId.Rl1)
                throw new ArgumentException("Scope is 9 normal Bullet/Pellet weapons; rocket/Special excluded, no independent pistol entry");
            _requestedPlayer = _ground == "slate" ? new Vector3(0f, .1f, -3f) : new Vector3(0f, .1f, 11f);
            string pos = Arg("-ddAp043PlayerXZ");
            if (pos != null) _requestedPlayer = XZ(pos, .1f);
        }
        private static Vector3 XZ(string value, float y)
        {
            string[] t = value.Split(','); float x, z;
            if (t.Length != 2 || !float.TryParse(t[0], NumberStyles.Float, Inv, out x) || !float.TryParse(t[1], NumberStyles.Float, Inv, out z) ||
                float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z) || Mathf.Abs(x) > 55 || Mathf.Abs(z) > 55)
                throw new ArgumentException("Expected map X,Z within +/-55m");
            return new Vector3(x, y, z);
        }
        private void OnEnable() { ProjectileEvents.Spawned += OnSpawn; ProjectileEvents.Impact += OnImpact; ProjectileEvents.Expired += OnExpire; }
        private void OnDisable() { ProjectileEvents.Spawned -= OnSpawn; ProjectileEvents.Impact -= OnImpact; ProjectileEvents.Expired -= OnExpire; Restore(); }
        private bool Watches(ProjectileKind kind, ClassicWeaponId id)
        {
            return (kind == ProjectileKind.Bullet || kind == ProjectileKind.Pellet) && id == _classic;
        }
        private void OnSpawn(ProjectileSpawnInfo p)
        {
            if (_caseFrame < 0 || !Watches(p.Kind, p.WeaponId)) return;
            if (p.PelletIndex == 0 && p.VolleyIndex == 0) _shots++;
            _lastSpawn = p; _lastSpawnFrame = Time.frameCount;
            WriteEvent(new EventSample { kind = "spawn", weapon = p.WeaponId.ToString(), projectileKind = p.Kind.ToString(),
                simulationFrame = _caseFrame, engineFrame = Time.frameCount, shotIndex = _shots, projectileId = p.ProjectileId,
                pelletIndex = p.PelletIndex, volleyIndex = p.VolleyIndex, time = Time.time, speed = p.Speed, sizeScale = p.SizeScale,
                position = p.Position, direction = p.Direction });
            if (_targetedEvents != null)
            {
                var sample = new TargetedSpawnState { simulationFrame = _caseFrame, engineFrame = Time.frameCount,
                    shotIndex = _shots, projectileId = p.ProjectileId, pelletIndex = p.PelletIndex, volleyIndex = p.VolleyIndex };
                for (int i = 0; i < ProjectileSystem.LiveCount; i++)
                {
                    ref readonly ProjectileState state = ref ProjectileSystem.Get(i);
                    if (state.Generation != p.ProjectileId) continue;
                    sample.projectileStateKnown = true; sample.actualTreeFlags = Convert.ToInt32(state.TreeFlags, Inv);
                    sample.actualMarkedChaosBit = (sample.actualTreeFlags & RunTreeHooks.TreeRoundMarkedChaos) != 0; break;
                }
                _targetedEvents.WriteLine(JsonUtility.ToJson(sample));
            }
        }
        private void OnImpact(ProjectileImpactInfo p)
        {
            if (_caseFrame < 0 || !Watches(p.Kind, p.WeaponId)) return;
            _impacts++; _lastTerminal = p.Position; _terminalKind = "impact:" + p.Surface;
            WriteEvent(new EventSample { kind = "impact", weapon = p.WeaponId.ToString(), projectileKind = p.Kind.ToString(),
                simulationFrame = _caseFrame, engineFrame = Time.frameCount, shotIndex = _shots, projectileId = p.ProjectileId,
                time = Time.time, speed = p.Speed, position = p.Position, direction = p.Direction,
                surface = p.Surface.ToString(), flesh = p.Flesh, stopped = p.Stopped });
        }
        private void OnExpire(ProjectileExpireInfo p)
        {
            if (_caseFrame < 0 || p.ProjectileId != _lastSpawn.ProjectileId) return;
            _lastTerminal = p.Position; _terminalKind = "expired:" + p.Reason;
            WriteEvent(new EventSample { kind = "expired", simulationFrame = _caseFrame, engineFrame = Time.frameCount,
                shotIndex = _shots, projectileId = p.ProjectileId, position = p.Position, terminal = p.Reason.ToString(), time = Time.time });
        }
        private void WriteEvent(EventSample e) { if (_eventMeta != null) _eventMeta.WriteLine(JsonUtility.ToJson(e)); }

        private IEnumerator Start()
        {
            _oldCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            // Yield outside try-catch: C# iterator permits try-finally, not yielding from a try with catch.
            float readyDeadline = Time.realtimeSinceStartup + 240f;
            while ((PlayerRegistry.Primary == null || WeaponRegistry.ActiveController == null || PrototypeCameraRig.Active == null ||
                    RunDirector.Instance == null || RunDirector.Instance.Phase != RunPhase.Fighting) && Time.realtimeSinceStartup < readyDeadline)
                yield return null;
            string problem = null;
            try { Configure(); } catch (Exception e) { problem = e.ToString(); }
            if (problem != null) { Finish("setup-failed:" + problem, 3); yield break; }
            // Native world settles for the same number of simulation frames. Camera lens/target/culling is untouched.
            for (int i = 0; i < 180; i++)
            {
                _input.Fire = false; _input.Aim = _camera.WorldToScreenPoint(_aim);
                yield return null;
            }
            if (_population == "crowd")
            {
                _director.SpawningEnabledDev = true;
                if (_director.SafetyCeiling < CrowdFillTarget) { Finish("crowd-safety-ceiling-below-fill-target-40", 4); yield break; }
                _director.ForceActiveTarget(CrowdFillTarget, true);
                for (int n = 0; n < 3600 && CrowdWithin() < CrowdFillTarget; n++)
                {
                    _input.Fire = false; _input.Aim = _camera.WorldToScreenPoint(_aim);
                    yield return null;
                }
                _report.initialCrowdWithin15m = CrowdWithin();
                if (CrowdWithin() < CrowdFillTarget) { Finish("crowd-below-fill-target-40-within-15m", 4); yield break; }
            }
            if (_crowdClearFlight)
            {
                yield return PrepareClearFlightCrowd();
                if (_done) yield break;
            }
            _report.initialCrowdWithin15m = CrowdWithin();
            _report.playerGround = Ground(_rig.transform.position);
            _report.aimGround = Ground(_aim);
            _report.visualGround = ReadVisualGround(_aim, -1, Time.frameCount);
            _report.camera = CameraInfo();
            _report.sceneName = SceneManager.GetActiveScene().name;
            _captureReal0 = Time.realtimeSinceStartupAsDouble;
            // Seed AGAIN at capture start: future spread/effects share an explicit seed; setup history is recorded, not hidden.
            UnityEngine.Random.InitState(s_seed);
            for (int f = 0; f < _frames && !_done; f++)
            {
                _caseFrame = f;
                _input.Fire = f >= ActualFireStart && (f - ActualFireStart) % PulsePeriod == 0;
                _input.Aim = _camera.WorldToScreenPoint(_aim);
                // Set input before the next normal Update; controller firing and fixed projectile simulation remain owners.
                yield return null;
                yield return new WaitForEndOfFrame();
                try
                {
                    if (CameraModeService.Current != CameraMode.TopDown || _camera.targetTexture != null)
                        throw new InvalidOperationException("TD native backbuffer lost");
                    if (File.Exists(Path.Combine(_out, "boundary-alert.json"))) { Finish("driver-alert-selfquit", 74); yield break; }
                    if (AvailableDisk() < DiskFloor) { Finish("disk-floor-selfquit", 74); yield break; }
                    if (!CaptureFrame(f)) { Finish("raw-byte-cap-selfquit", 74); yield break; }
                }
                catch (Exception e) { Finish("capture-failed:" + e, 4); yield break; }
            }
            Finish(_shots > 0 ? "complete" : "no-actual-shot", _shots > 0 ? 0 : 4);
        }
        private static ZombieEnemy[] LiveCrowdEnemies()
        {
            var live = new System.Collections.Generic.List<ZombieEnemy>();
            foreach (ZombieEnemy enemy in FindObjectsOfType<ZombieEnemy>()) if (enemy.IsAlive) live.Add(enemy);
            return live.ToArray();
        }
        private static int Within(ZombieEnemy[] enemies, Vector3 centre, float radius)
        {
            int count = 0;
            foreach (ZombieEnemy enemy in enemies)
                if (enemy != null && enemy.IsAlive && Vector3.Distance(enemy.transform.position, centre) <= radius) count++;
            return count;
        }
        private CrowdEnemyPose[] EnemyPoseProof(ZombieEnemy[] enemies, Vector3 outward)
        {
            var rows = new CrowdEnemyPose[enemies.Length];
            for (int i = 0; i < enemies.Length; i++)
            {
                ZombieEnemy enemy = enemies[i]; Vector3 relative = enemy.transform.position - _rig.transform.position;
                rows[i] = new CrowdEnemyPose { instanceId = enemy.GetInstanceID(), alive = enemy.IsAlive,
                    position = enemy.transform.position, distanceFromHero = relative.magnitude,
                    outwardDot = Vector3.Dot(relative, outward), firingDot = Vector3.Dot(relative, (_aim - _rig.transform.position).normalized) };
            }
            return rows;
        }
        private bool TryOuterPose(Vector3 centre, ZombieEnemy[] packed, out Vector3 requested, out Vector3 actual, out Vector3 outward)
        {
            requested = actual = outward = Vector3.zero;
            float outerMetres = _treeMode == "seeker" ? 10f : 8f;
            // Explicit deterministic candidates; no Random consumption and no NPC or camera writes.
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f; Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 asked = centre + direction * outerMetres; NavMeshHit hit;
                if (!NavMesh.SamplePosition(asked, out hit, .5f, NavMesh.AllAreas)) continue;
                Vector3 delta = hit.position - centre; delta.y = 0f;
                if (Mathf.Abs(delta.magnitude - outerMetres) > .25f) continue;
                Vector3 away = delta.normalized, firing = _treeMode == "seeker" ? -away : away;
                Vector3 end = hit.position + firing * 9f;
                NavMeshHit obstruction;
                if (NavMesh.Raycast(hit.position, end, out obstruction, NavMesh.AllAreas)) continue;
                bool valid = true;
                foreach (ZombieEnemy enemy in packed)
                {
                    Vector3 relative = enemy.transform.position - hit.position;
                    if (!enemy.IsAlive || relative.magnitude > 15f || Vector3.Dot(relative, away) >= 0f) { valid = false; break; }
                }
                if (!valid) continue;
                // Read-only world collision query: keep the first9m flight corridor clear at the authored shot height.
                Vector3 rayOrigin = hit.position + Vector3.up * _weapon.ShotRayHeight;
                foreach (RaycastHit block in Physics.RaycastAll(rayOrigin, firing, 9f,
                    (1 << 0) | (1 << PrototypeLayers.Environment), QueryTriggerInteraction.Ignore))
                {
                    if (block.collider != null && !block.collider.transform.IsChildOf(_rig.transform)) { valid = false; break; }
                }
                if (!valid) continue;
                requested = asked; actual = hit.position; outward = away; return true;
            }
            return false;
        }
        private IEnumerator PrepareClearFlightCrowd()
        {
            _targetedSetup = _targetedSetup ?? new TargetedSetupSample();
            _targetedSetup.crowdClearFlightRequested = true;
            _targetedSetup.safetyCeilingBefore = _director.SafetyCeiling;
            _targetedSetup.existingPlayerDiagnosisInvulnerable = _rig.Health.DiagnosisInvulnerable;
            _targetedSetup.originalRequestedHeroPose = _requestedPlayer;
            _targetedSetup.treeModeRequested = _treeMode;
            _targetedSetup.firstFireFrame = ActualFireStart;
            float packRadius = _treeMode == "seeker" ? 3.5f : 6f;
            _targetedSetup.requiredNaturalPackRadiusMetres = packRadius;
            _targetedSetup.requestedOuterMetres = _treeMode == "seeker" ? 10f : 8f;
            if (_treeMode == "seeker") _targetedSetup.protocol = "Real seeker nodes; existing live40 naturally converge within3.5m; only hero teleports to NavMesh10m outside, aim actual pack centroid; natural TD camera30frame settle, firstFireFrame12";
            float started = Time.realtimeSinceStartup, deadline = started + 60f;
            ZombieEnemy[] packed = null;
            for (int frame = 0; frame < 3600 && Time.realtimeSinceStartup < deadline; frame++)
            {
                ZombieEnemy[] live = LiveCrowdEnemies();
                if (live.Length == RequiredCrowd && Within(live, _rig.transform.position, packRadius) == RequiredCrowd) { packed = live; break; }
                _targetedSetup.naturalWaitFrames++;
                _input.Fire = false; _input.Aim = _camera.WorldToScreenPoint(_aim);
                yield return null;
            }
            _targetedSetup.naturalWaitWallSeconds = Time.realtimeSinceStartup - started;
            if (packed == null)
            {
                _targetedSetup.status = "FAILED"; _targetedSetup.failure = "Natural live40 pack did not converge within requested radius before bounded timeout";
                Finish("clear-flight-natural-pack-timeout", 4); yield break;
            }
            string problem = null;
            try
            {
                Vector3 centre = _rig.transform.position, requested, actual, outward;
                _targetedSetup.oldHeroPose = centre; _targetedSetup.packEngineFrame = Time.frameCount;
                _targetedSetup.liveBefore = packed.Length; _targetedSetup.packedWithin6m = Within(packed, centre, 6f);
                _targetedSetup.packedWithinRequestedRadius = Within(packed, centre, packRadius);
                Vector3 centroid = Vector3.zero; foreach (ZombieEnemy enemy in packed) centroid += enemy.transform.position;
                centroid /= packed.Length; _targetedSetup.packCentroidBefore = centroid;
                _targetedSetup.crowdWithin15mBefore = CrowdWithin(); _targetedSetup.cameraBefore = CameraInfo();
                if (!TryOuterPose(centre, packed, out requested, out actual, out outward)) throw new InvalidOperationException("No valid requested NavMesh outer pose with live40 behind and clear9m world corridor");
                _targetedSetup.beforeEnemies = EnemyPoseProof(packed, outward);
                _targetedSetup.outerRequestedHeroPose = requested; _targetedSetup.outerNavMeshHeroPose = actual;
                _targetedSetup.outwardDirection = outward;
                Vector3 firing = _treeMode == "seeker" ? centroid - actual : outward; firing.y = 0f; firing.Normalize();
                _rig.Teleport(actual, Quaternion.LookRotation(firing));
                _actualPlayer = _rig.transform.position;
                _aim = _treeMode == "seeker" ? new Vector3(centroid.x, _actualPlayer.y + _weapon.ShotRayHeight, centroid.z) :
                    _actualPlayer + outward * 9f + Vector3.up * _weapon.ShotRayHeight;
                _targetedSetup.firingDirection = firing;
                _targetedSetup.aimDistanceMetres = Vector3.Distance(_actualPlayer, new Vector3(_aim.x, _actualPlayer.y, _aim.z));
                _targetedSetup.teleportEngineFrame = Time.frameCount; _targetedSetup.performed = true;
                _input.Fire = false; _input.Aim = _camera.WorldToScreenPoint(_aim);
            }
            catch (Exception e) { problem = e.ToString(); }
            if (problem != null)
            {
                _targetedSetup.status = "FAILED"; _targetedSetup.failure = problem;
                Finish("clear-flight-pose-failed:" + problem, 4); yield break;
            }
            // Follow the normal rig, motor and camera updates; no manual camera positioning/rendering.
            for (int i = 0; i < 30; i++)
            {
                _input.Fire = false; _input.Aim = _camera.WorldToScreenPoint(_aim);
                _targetedSetup.settleFrames++;
                yield return null;
            }
            try
            {
                ZombieEnemy[] after = LiveCrowdEnemies();
                _targetedSetup.liveAfter = after.Length; _targetedSetup.crowdWithin15mAfter = CrowdWithin();
                _targetedSetup.afterEnemies = EnemyPoseProof(after, _targetedSetup.outwardDirection);
                _targetedSetup.actualHeroPose = _rig.transform.position; _actualPlayer = _rig.transform.position;
                Vector3 distance = _actualPlayer - _targetedSetup.oldHeroPose; distance.y = 0f;
                _targetedSetup.actualOuterMetres = distance.magnitude; _targetedSetup.aim = _aim;
                _targetedSetup.settleEngineFrame = Time.frameCount; _targetedSetup.cameraAfter = CameraInfo();
                _targetedSetup.safetyCeilingAfter = _director.SafetyCeiling;
                _targetedSetup.actualInitialTreeState = ReadTreeState();
                if (after.Length != RequiredCrowd || _targetedSetup.crowdWithin15mAfter != RequiredCrowd ||
                    _targetedSetup.safetyCeilingAfter != _targetedSetup.safetyCeilingBefore)
                    throw new InvalidOperationException("Actual live40/within15m/safety ceiling was not preserved after natural settle");
                foreach (CrowdEnemyPose enemy in _targetedSetup.afterEnemies)
                    if (!enemy.alive || enemy.outwardDot >= 0f) throw new InvalidOperationException("Packed NPC is no longer behind the outward pose direction");
                _report.playerActual = _actualPlayer; _report.aim = _aim;
                _targetedSetup.verified = true; _targetedSetup.status = "SETUP_VERIFIED_NATIVE_APPEARANCE_NOT_DONE";
            }
            catch (Exception e) { problem = e.ToString(); }
            if (problem != null)
            {
                _targetedSetup.status = "FAILED"; _targetedSetup.failure = problem;
                Finish("clear-flight-settle-failed:" + problem, 4);
            }
        }

        private void Configure()
        {
            _rig = PlayerRegistry.Primary; _weapon = WeaponRegistry.ActiveController; _director = EnemySpawnDirector.Instance;
            if (_rig == null || _weapon == null || _director == null || PrototypeCameraRig.Active == null)
                throw new InvalidOperationException("Run/player/controller/camera/spawn director missing");
            _camera = PrototypeCameraRig.Active.Camera;
            if (_camera == null || CameraModeService.Current != CameraMode.TopDown || _camera.targetTexture != null)
                throw new InvalidOperationException("Start the run with -ddCameraMode topdown; camera is not changed by the agent");
            if (!SupportedNativeSize(Screen.width, Screen.height)) throw new InvalidOperationException("Native backbuffer must be1920x1080,2560x1440,1496x842; no upscale/resample fallback");
            if (AvailableDisk() < DiskFloor + 3L * 1024 * 1024 * 1024) throw new InvalidOperationException("20GiB+3GiB bounded capture/movie reserve required");
            _oldInput = InputSourceRegistry.Current;
            _oldSpawn = _director.SpawningEnabledDev;
            _oldInvulnerable = _rig.Health.DiagnosisInvulnerable;
            _configured = true;
            _rig.Health.DiagnosisInvulnerable = true;
            _director.SpawningEnabledDev = false;
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            NavMeshHit mesh;
            if (!NavMesh.SamplePosition(_requestedPlayer, out mesh, 2f, NavMesh.AllAreas)) throw new InvalidOperationException("Requested native world position misses NavMesh");
            _rig.Teleport(mesh.position, Quaternion.LookRotation(Vector3.right));
            _actualPlayer = mesh.position;
            _aim = _actualPlayer + Vector3.right * 9f + Vector3.up * _weapon.ShotRayHeight;
            string aim = Arg("-ddAp043AimXZ");
            if (aim != null) _aim = XZ(aim, _actualPlayer.y + _weapon.ShotRayHeight);
            if (_weapon.SpecialWeaponActive) throw new InvalidOperationException("Normal flight capture cannot run with Special active");
            bool hookReportedSuccess = WeaponQaHooks.TrySetClassicWeapon(_classic);
            var tree = RunDirector.Instance.Shop.TreeV2;
            if (tree == null || !tree.IsBuilt) throw new InvalidOperationException("Live run tree missing");
            WeaponIdentitySample actualWeaponProof = ReadWeaponIdentity(tree, hookReportedSuccess);
            if (!actualWeaponProof.verified) throw new InvalidOperationException("Actual requested weapon identity missing/mismatched: " + actualWeaponProof.status);
            string treeCoverage = "actual neutral controller gameplay";
            if (_element == "ladder")
            {
                TreeStats visual = TreeStats.Neutral;
                visual.SetBranchTier(TreeBranch.Shot, _tier);
                TracerStyle.SetPreview(visual); _preview = true;
                treeCoverage = "TRACER-ONLY visual I-V preview: impact/muzzle tier not covered";
            }
            else if (_element != "none")
            {
                if (_element == "fusion") { GrantElement(tree, "fire", _tier); GrantElement(tree, "ice", _tier); Grant(tree, "el_fusion"); }
                else GrantElement(tree, _element, _tier);
                treeCoverage = "actual existing tree nodes selected; tracer/impact/muzzle visual coverage requires native-frame review";
            }
            if (_treeMode != "none")
            {
                _targetedNodes = _treeMode == "marked" ? new[] { "kx_chain", "kx_hole" } :
                    new[] { "sh_heavy", "sh_double", "sh_triple", "sh_fan", "sh_cap" };
                foreach (string node in _targetedNodes) Grant(tree, node);
                _targetedSetup = new TargetedSetupSample { mode = "tree-mode-" + _treeMode,
                    treeModeRequested = _treeMode, grantedNodeIds = _targetedNodes, firstFireFrame = ActualFireStart,
                    originalRequestedHeroPose = _requestedPlayer, oldHeroPose = _rig.transform.position,
                    actualHeroPose = _rig.transform.position, aim = _aim,
                    existingPlayerDiagnosisInvulnerable = _rig.Health.DiagnosisInvulnerable,
                    actualInitialTreeState = ReadTreeState(), initialTreeModeState = ReadTargetedTreeState() };
                if (!_targetedSetup.initialTreeModeState.verified) throw new InvalidOperationException("Actual targeted real tree nodes/stats mismatch");
                _targetedSetup.verified = true; _targetedSetup.status = "REAL_NODE_STATE_VERIFIED_FLIGHT_NOT_DONE";
            }
            TreeStateSample initialTreeState = ReadTreeState();
            if (!initialTreeState.verified || (_element == "fusion" && !initialTreeState.legalFusionStateVerified))
                throw new InvalidOperationException("Actual tree/element/FusionIV proof missing/mismatched: " + initialTreeState.status);
            // Install the real pointer aim before the first natural controller/camera update. No camera setters.
            _input.Fire = false; _input.Aim = _camera.WorldToScreenPoint(_aim);
            InputSourceRegistry.SetCurrent(_input); _rig.Motor.SetInputSource(null); _rig.Aim.SetInputSource(null);
            Directory.CreateDirectory(_out); Directory.CreateDirectory(Path.Combine(_out, "video", "frames"));
            Directory.CreateDirectory(Path.Combine(_out, "stills"));
            _times = Writer("video/frames.tsv"); _times.WriteLine("# idx\tt_simulation_seconds");
            _frameMeta = Writer("frame-metadata.jsonl"); _eventMeta = Writer("projectile-events.jsonl");
            _audioTimes = Writer("video/audio-map.tsv"); _audioTimes.WriteLine("# idx\tsimulation_t\trealtime_t\tdsp_t\taudio_frames");
            _report = new CaseReport { caseName = _caseName, lookRequested = _look, family = _family, requestedGround = _ground,
                population = _population, element = _element, tier = _tier, treeCoverage = treeCoverage, seed = s_seed,
                requiredCrowdWithin15m = _population == "crowd" ? RequiredCrowd : 0,
                requestedAltElementTier = _element == "fusion" ? _tier : 0, requestedPreviewRank = _preview ? _tier : 0,
                treeCaseCaption = _element == "fusion" ? "Fire " + _tier + " + Ice " + _tier + " / actual Element branchIV (Fusion)" :
                    (_preview ? "Tracer-only Shot preview " + _tier + "; actual gameplay branch tracked separately" : "Actual existing tree stats, requested input tier tracked separately"),
                actualWeaponProof = actualWeaponProof, initialTreeState = initialTreeState,
                width = Screen.width, height = Screen.height, framesRequested = _frames, rawLimitBytes = _limit,
                playerRequested = _requestedPlayer, playerActual = _actualPlayer, aim = _aim,
                captureDeltaBefore = _oldCaptureDelta, fixedDeltaTime = Time.fixedDeltaTime, startRound = RunDirector.Instance.Round, status = "RUNNING_NOT_MEASURED" };
            if (_treeMode != "none")
            {
                _targetedTreeStates = Writer("targeted-tree-state.jsonl");
                _targetedEvents = Writer("targeted-projectile-events.jsonl");
            }
            File.WriteAllText(Path.Combine(_out, "scenario.json"), JsonUtility.ToJson(_report, true));
        }
        private TargetedTreeState ReadTargetedTreeState()
        {
            var tree = RunDirector.Instance.Shop.TreeV2; TreeStats stats = tree.Stats;
            bool owned = true; foreach (string node in _targetedNodes) owned &= tree.Owns(node);
            bool hole = (stats.Core & TreeChaosCore.Hole) != 0;
            return new TargetedTreeState { engineFrame = Time.frameCount, treeMode = _treeMode, requestedNodes = _targetedNodes,
                allRequestedNodesOwned = owned, actualShotTier = stats.BranchTier(TreeBranch.Shot), actualChaosTier = stats.BranchTier(TreeBranch.Chaos),
                extraShots = stats.ExtraShots, extraShotSeekDegrees = stats.ExtraShotSeekDegrees, seekDegrees = stats.SeekDegrees,
                core = stats.Core.ToString(), observedTime = Time.time, holeReadyAt = TreeGravityWells.ReadyAt,
                holeReadyBySource = hole && Time.time >= TreeGravityWells.ReadyAt,
                verified = owned && (_treeMode == "marked" ? hole : _treeMode == "seeker" && stats.ExtraShots >= 1 && stats.ExtraShotSeekDegrees == 25f) };
        }
        private static void Grant(RunTreeV2 tree, string node)
        {
            if (!tree.Owns(node) && !tree.GrantFree(node)) throw new InvalidOperationException("Tree node unavailable: " + node);
        }
        private static void GrantElement(RunTreeV2 tree, string name, int tier)
        {
            Grant(tree, "el_" + name);
            for (int n = 2; n <= tier; n++) Grant(tree, "el_" + name + n);
        }
        private StreamWriter Writer(string relative) => new StreamWriter(Path.Combine(_out, relative), false) { NewLine = "\n" };
        private long AvailableDisk() => new DriveInfo(Path.GetPathRoot(_out)).AvailableFreeSpace;
        private bool WriteBytes(string relative, byte[] bytes)
        {
            // Reserve metadata/guard headroom; self-quit rather than external watchdog kill.
            if (bytes == null || _bytes + bytes.Length > _limit - 32L * 1024 * 1024) return false;
            File.WriteAllBytes(Path.Combine(_out, relative), bytes); _bytes += bytes.Length; return true;
        }
        private static bool SupportedNativeSize(int width, int height) =>
            (width == 1920 && height == 1080) || (width == 2560 && height == 1440) || (width == 1496 && height == 842);

        private bool CaptureFrame(int index)
        {
            Texture2D native = ScreenCapture.CaptureScreenshotAsTexture();
            if (native == null) throw new InvalidOperationException("No native screenshot");
            try
            {
                if (native.width != _report.width || native.height != _report.height || Screen.width != _report.width || Screen.height != _report.height)
                    throw new InvalidOperationException("Screenshot/native Screen changed from initial capture size; no resample fallback");
                if (!WriteBytes("video/frames/f-" + index.ToString("D6", Inv) + ".jpg", native.EncodeToJPG(JpegQuality))) return false;
                _times.WriteLine(index + "\t" + (index / 60d).ToString("F6", Inv));
                string nativeStillPath = null;
                if (index == ActualFireStart - 1)
                {
                    nativeStillPath = "stills/background-before-first-shot.png";
                    if (!WriteBytes(nativeStillPath, native.EncodeToPNG())) return false;
                }
                int age = _lastSpawnFrame < 0 ? -1 : Time.frameCount - _lastSpawnFrame;
                if (_shots > 0 && _shots <= 6 && (age == 1 || age == 2 || age == 3 || age == 6 || age == 9))
                {
                    string png = "stills/flight-shot-" + _shots.ToString("D3", Inv) + "-f" + age.ToString("D2", Inv) + ".png";
                    if (!File.Exists(Path.Combine(_out, png)))
                    {
                        if (!WriteBytes(png, native.EncodeToPNG())) return false;
                        nativeStillPath = png;
                    }
                }
                FrameSample row = Snapshot(index, age, nativeStillPath != null && nativeStillPath.StartsWith("stills/flight-shot-", StringComparison.Ordinal));
                row.nativeStillPath = nativeStillPath;
                if (nativeStillPath != null && nativeStillPath.StartsWith("stills/flight-shot-", StringComparison.Ordinal))
                    row.nativeFlightHeadGround = Ground(row.projectileHead);
                row.nativeScreenshotWidth = native.width; row.nativeScreenshotHeight = native.height;
                _report.lastRuntimeOptionProof = row.runtimeOptionProof;
                if (row.runtimeOptionProof != null && row.runtimeOptionProof.verified) _report.lookBindingFramesVerified++;
                else _report.lookBindingFramesUnknown++;
                if (_report.minCrowdWithin15mDuringCapture < 0 || row.crowdWithin15m < _report.minCrowdWithin15mDuringCapture)
                    _report.minCrowdWithin15mDuringCapture = row.crowdWithin15m;
                if (_population == "crowd" && row.crowdWithin15m < RequiredCrowd) _report.crowdFramesBelowRequired++;
                _frameMeta.WriteLine(JsonUtility.ToJson(row));
                if (_targetedTreeStates != null) _targetedTreeStates.WriteLine(JsonUtility.ToJson(ReadTargetedTreeState()));
                _audioTimes.WriteLine(index + "\t" + row.simulationT.ToString("F6", Inv) + "\t" + row.realtimeT.ToString("F6", Inv) + "\t" +
                                      row.dspT.ToString("F6", Inv) + "\t" + row.audioFrames);
                _report.framesWritten++;
                return true;
            }
            finally { Destroy(native); }
        }
        private FrameSample Snapshot(int index, int age, bool includeVisualGround)
        {
            var tr = TracerRibbonRenderer.Instance; var mz = MuzzleSystem.Instance; var impact = ImpactSystem.Instance; 
            var row = new FrameSample { index = index, engineFrame = Time.frameCount, shotIndex = _shots, projectileId = _lastSpawn.ProjectileId,
                ageFrames = age, simulationT = index / 60d, realtimeT = Time.realtimeSinceStartupAsDouble - _captureReal0, dspT = AudioSettings.dspTime,
                audioFrames = AudioOutputCapture.Armed != null ? AudioOutputCapture.Armed.FramesCaptured : -1,
                crowdWithin15m = CrowdWithin(), liveProjectiles = ProjectileSystem.LiveCount, camera = CameraInfo(),
                bodyFeetScreen = _camera.WorldToScreenPoint(_rig.transform.position), bodyHeadScreen = _camera.WorldToScreenPoint(_rig.transform.position + Vector3.up * 1.8f),
                muzzleWorld = _weapon.Muzzle.position, muzzleScreen = _camera.WorldToScreenPoint(_weapon.Muzzle.position),
                tracerCore = tr != null ? tr.DrawnCore : -1, tracerGlow = tr != null ? tr.DrawnGlow : -1, tracerGlint = tr != null ? tr.DrawnGlint : -1,
                tracerTailOuts = tr != null ? tr.TailOuts : -1, muzzleLights = mz != null ? mz.LitCount : -1, muzzleSmoke = mz != null ? mz.SmokeAlive : -1,
                impactSparks = impact != null ? impact.SparksAlive : -1, impactDebris = impact != null ? impact.DebrisAlive : -1,
                impactCount = impact != null ? impact.Impacts : -1 };
            for (int i = 0; i < ProjectileSystem.LiveCount; i++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(i);
                if (p.Generation != _lastSpawn.ProjectileId) continue;
                row.projectileLive = true; row.projectileHead = p.Position; row.projectileOrigin = p.Origin; row.projectileVelocity = p.Velocity; break;
            }
            if (!row.projectileLive) { row.projectileHead = _lastTerminal; row.projectileOrigin = _lastSpawn.Position; }
            row.pathCount = ProjectileSystem.TryGetPath(_lastSpawn.ProjectileId, _path);
            if (row.pathCount > 0) { row.retainedProjectilePath = new Vector3[row.pathCount]; Array.Copy(_path, row.retainedProjectilePath, row.pathCount); }
            row.tracerGeometry = ReadFlightGeometry(tr, out row.tracerGeometryReadStatus, includeVisualGround);
            if (row.tracerGeometry != null)
                foreach (FlightGeometry geometry in row.tracerGeometry)
                    if (geometry.projectileId == row.projectileId) { row.visualGround = geometry.visualGround; break; }
            row.runtimeOptionProof = ReadRuntimeOptionProof(tr, row.engineFrame);
            row.actualTreeState = ReadTreeState();
            row.actualOption = row.runtimeOptionProof.actualOption;
            row.optionActive = row.runtimeOptionProof.optionActive;
            return row;
        }
        [Serializable] private sealed class FlightGeometry
        {
            public int projectileId, frameCount, look, seekPoints, actualOptionValue, ap043Tier = -1, ap043SecondaryLook = -1;
            public bool ap043TierKnown, ap043SecondaryLookKnown;
            public string lookMeaning = "TracerStyle element Look, NOT Ap043BulletLook selector", actualOption;
            public bool ap043EligibleKnown, ap043Eligible, actualOptionKnown;
            public bool alive, eligibleCore, tracer, pellet, stopped, sniperVapourActive;
            public bool baselineSniperVapourWouldBeActive, sniperVapourPredictionKnown, actualStoppedTailOutKnown;
            public bool provenanceExpectedBySource = true, shaderGpuVerification = false;
            public float stoppedTailOutSeconds, predictedBaseTrailMetres;
            public string trailPredictionStatus, sniperVapourMeaning = "Source-expected baseline-only layer; GPU layer usage NOT_DONE";
            public Vector3 originWorld, headWorld, tailWorld, headScreen, tailScreen;
            public float visualSpeed, age, coreMetres, haloMetres, trailMetres, coreMinPx, coreCapPx, extraHaloPx;
            public VisualGroundSample visualGround; // Head query bound to this visual record only; emitted on native hero PNG rows.
            public string provenance = "readonly TD record + pure source trail/core predicates and actual option envelope tailout; SOURCE EXPECTATION ONLY, GPU draw/pixel mask NOT_DONE";
        }
        [Serializable] private sealed class WeaponIdentitySample
        {
            public string requestedFamily, requestedClassic, actualClassic, definitionWeaponId, treeWeaponId, treeWeaponKey, status;
            public bool hookReportedSuccess, controllerClassicPresent, definitionPresent, verified;
            public string meaning = "Readonly actual controller Classic.Id/Definition.weaponId and live tree Weapon.Id/Key; hook boolean alone insufficient";
        }
        private WeaponIdentitySample ReadWeaponIdentity(RunTreeV2 tree, bool hook)
        {
            var proof = new WeaponIdentitySample { requestedFamily = _family, requestedClassic = _classic.ToString(),
                hookReportedSuccess = hook, status = "UNKNOWN" };
            if (_weapon == null || _weapon.Classic == null || tree == null) return proof;
            proof.controllerClassicPresent = true; proof.actualClassic = _weapon.Classic.Id.ToString();
            proof.definitionPresent = _weapon.Classic.Definition != null;
            proof.definitionWeaponId = proof.definitionPresent ? _weapon.Classic.Definition.weaponId : null;
            proof.treeWeaponId = tree.Weapon.Id.ToString(); proof.treeWeaponKey = tree.Weapon.Key;
            proof.verified = proof.definitionPresent && _weapon.Classic.Id == _classic && tree.Weapon.Id == _classic &&
                string.Equals(proof.definitionWeaponId, _family, StringComparison.Ordinal) &&
                string.Equals(proof.treeWeaponKey, _family, StringComparison.Ordinal);
            proof.status = proof.verified ? (hook ? "VERIFIED_ACTUAL_CONTROLLER_AND_TREE_IDENTITY" : "HOOK_FALSE_BUT_ACTUAL_REQUESTED_IDENTITY_VERIFIED") : "REQUESTED_WEAPON_IDENTITY_MISMATCH";
            return proof;
        }
        [Serializable] private sealed class TreeStateSample
        {
            public int engineFrame, requestedCaseTier, requestedAltElementTier, requestedPreviewRank;
            public int actualBranchTierElement = -1, actualBranchTierShot = -1, elementsMask, expectedFusionElementsMask;
            public int tracerShotTier = -1, tracerHaloTier = -1, tracerStyleLook = -1, tracerSecondaryLook = -1;
            public string elements, dominantElement, tracerDominantElement, status = "UNKNOWN";
            public bool fusion, previewActive, verified, legalFusionStateVerified;
            public string meaning = "Readonly actual RunTreeV2.Stats and effective TracerStyle; preview rank is separate from gameplay branch rank";
        }
        private TreeStateSample ReadTreeState()
        {
            var row = new TreeStateSample { engineFrame = Time.frameCount, requestedCaseTier = _tier,
                requestedAltElementTier = _element == "fusion" ? _tier : 0, requestedPreviewRank = _preview ? _tier : 0 };
            try
            {
                var run = RunDirector.Instance;
                var tree = run != null && run.Shop != null ? run.Shop.TreeV2 : null;
                if (tree == null || !tree.IsBuilt) { row.status = "LIVE_TREE_MISSING"; return row; }
                TreeStats stats = tree.Stats;
                row.actualBranchTierElement = stats.BranchTier(TreeBranch.Element);
                row.actualBranchTierShot = stats.BranchTier(TreeBranch.Shot);
                row.elements = stats.Elements.ToString(); row.elementsMask = Convert.ToInt32(stats.Elements, Inv);
                row.fusion = stats.Fusion; row.dominantElement = stats.DominantElement.ToString();
                row.previewActive = TracerStyle.PreviewActive;
                row.tracerShotTier = TracerStyle.ShotTier; row.tracerHaloTier = TracerStyle.HaloTier;
                row.tracerStyleLook = TracerStyle.Look; row.tracerSecondaryLook = TracerStyle.Ap043SecondaryLook;
                row.tracerDominantElement = TracerStyle.DominantElement.ToString();
                row.expectedFusionElementsMask = Convert.ToInt32(Enum.Parse(stats.Elements.GetType(), "Fire"), Inv) |
                    Convert.ToInt32(Enum.Parse(stats.Elements.GetType(), "Ice"), Inv);
                row.legalFusionStateVerified = _element == "fusion" && _tier >= 2 && _tier <= 3 && row.fusion &&
                    row.actualBranchTierElement == 4 && row.elementsMask == row.expectedFusionElementsMask &&
                    row.dominantElement == "Ice" && row.tracerStyleLook == 2 && row.tracerSecondaryLook == 1 && row.tracerHaloTier == 4;
                row.verified = row.engineFrame == Time.frameCount;
                row.status = row.verified ? "READONLY_ACTUAL_STATS_AND_EFFECTIVE_STYLE_SAME_FRAME" : "FRAME_CHANGED";
            }
            catch (Exception e) { row.status = "UNKNOWN_TREE_STATS:" + e.GetType().Name; }
            return row;
        }
        [Serializable] private sealed class GroundScalarSample
        {
            public string property; public bool present, finite; public float value;
        }
        [Serializable] private sealed class GroundMaterialSample
        {
            public int slot, instanceId; public bool exists;
            public string sharedMaterialName, shaderName, status = "UNKNOWN";
            public GroundScalarSample[] scalars;
        }

        [Serializable] private sealed class MaterialOptionSample
        {
            public string field, name, shaderName, keyword = "DD_AP043", readStatus;
            public int instanceId, expectedLayer;
            public bool exists, shaderExists, keywordEnabled, modePropertyPresent, layerPropertyPresent;
            public float mode, layer;
        }
        [Serializable] private sealed class RuntimeOptionProof
        {
            public int engineFrame, actualOptionValue = -1;
            public string actualOption, requestedOption, status = "UNKNOWN", error;
            public string source = "Public Ap043BulletLooks.Current/Active read-only reflection; private selected material fields read-only";
            public string verifiedMeaning = "Runtime selector and material configuration only; pixel appearance/GPU-used variant NOT_DONE";
            public bool startupChoiceKnown, optionActive, rendererReady, sameEngineFrame, verified;
            public bool actualRenderAppearanceMeasured = false, shaderGpuUsageMeasured = false;
            public MaterialOptionSample[] materials;
        }
        private static string OptionName(int value)
        {
            if (value == 0) return "R4";
            if (value == 1) return "O1";
            if (value == 2) return "O2";
            if (value == 3) return "O3";
            return "UNKNOWN";
        }
        private static bool OptionalRecordBool(object record, string name)
        {
            FieldInfo f = record.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f != null && f.FieldType == typeof(bool) && (bool)f.GetValue(record);
        }
        private static int OptionalRecordEnum(object record, string name)
        {
            FieldInfo f = record.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f != null ? Convert.ToInt32(f.GetValue(record), Inv) : -1;
        }
        private static MaterialOptionSample ReadMaterialOption(TracerRibbonRenderer renderer, string field, int layer)
        {
            var row = new MaterialOptionSample { field = field, expectedLayer = layer, readStatus = "UNKNOWN" };
            FieldInfo info = typeof(TracerRibbonRenderer).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null) { row.readStatus = "FIELD_MISSING"; return row; }
            Material material = info.GetValue(renderer) as Material;
            if (material == null) { row.readStatus = "MATERIAL_MISSING"; return row; }
            row.exists = true; row.name = material.name; row.instanceId = material.GetInstanceID();
            row.shaderExists = material.shader != null;
            row.shaderName = row.shaderExists ? material.shader.name : null;
            row.keywordEnabled = material.IsKeywordEnabled("DD_AP043");
            row.modePropertyPresent = material.HasProperty("_Ap043Mode");
            row.layerPropertyPresent = material.HasProperty("_Ap043Layer");
            if (row.modePropertyPresent) row.mode = material.GetFloat("_Ap043Mode");
            if (row.layerPropertyPresent) row.layer = material.GetFloat("_Ap043Layer");
            row.readStatus = "READONLY_SAME_ENGINE_FRAME";
            return row;
        }
        private RuntimeOptionProof ReadRuntimeOptionProof(TracerRibbonRenderer renderer, int engineFrame)
        {
            var proof = new RuntimeOptionProof { engineFrame = engineFrame, requestedOption = _look,
                sameEngineFrame = engineFrame == Time.frameCount };
            try
            {
                Type api = typeof(TracerRibbonRenderer).Assembly.GetType("DD.Prototype.Feedback.Vfx.Ap043BulletLooks");
                if (api == null) { proof.error = "PUBLIC_SELECTOR_TYPE_ABSENT"; return proof; }
                PropertyInfo current = api.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                PropertyInfo active = api.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
                if (current == null || active == null) { proof.error = "PUBLIC_SELECTOR_PROPERTY_ABSENT"; return proof; }
                proof.actualOptionValue = Convert.ToInt32(current.GetValue(null), Inv);
                proof.actualOption = OptionName(proof.actualOptionValue);
                proof.optionActive = (bool)active.GetValue(null);
                proof.startupChoiceKnown = proof.actualOption != "UNKNOWN";
                if (renderer == null) { proof.error = "RENDERER_NOT_READY"; return proof; }
                string prefix = proof.optionActive ? "_ap043" : "_td";
                proof.materials = new[] { ReadMaterialOption(renderer, prefix + "CoreMaterial", 0),
                    ReadMaterialOption(renderer, prefix + "GlowMaterial", 1), ReadMaterialOption(renderer, prefix + "InkMaterial", 2) };
                bool bindings = true;
                foreach (MaterialOptionSample material in proof.materials)
                {
                    bindings &= material.exists && material.shaderExists && material.readStatus == "READONLY_SAME_ENGINE_FRAME";
                    if (proof.optionActive)
                        bindings &= material.keywordEnabled && material.modePropertyPresent && material.layerPropertyPresent &&
                            material.mode == proof.actualOptionValue && material.layer == material.expectedLayer;
                    else bindings &= !material.keywordEnabled;
                }
                proof.rendererReady = bindings;
                proof.sameEngineFrame &= engineFrame == Time.frameCount;
                proof.verified = proof.startupChoiceKnown && proof.sameEngineFrame && bindings && proof.actualOption == _look &&
                    proof.optionActive == (proof.actualOptionValue != 0);
                proof.status = proof.verified ? "VERIFIED_RUNTIME_CHOICE_AND_MATERIAL_CONFIGURATION_ONLY" : "UNKNOWN_OR_MISMATCH";
                if (!proof.verified) proof.error = "Enum/active/requested/material keyword-mode-layer/frame mismatch";
            }
            catch (Exception e) { proof.status = "UNKNOWN_READ_FAILED"; proof.error = e.GetType().Name + ":" + e.Message; }
            return proof;
        }

        private static T RecordField<T>(object record, string name)
        {
            FieldInfo f = record.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null) throw new MissingFieldException(record.GetType().FullName, name);
            return (T)f.GetValue(record);
        }
        private FlightGeometry[] ReadFlightGeometry(TracerRibbonRenderer renderer, out string status, bool includeVisualGround)
        {
            status = "NOT_AVAILABLE";
            if (renderer == null) return null;
            try
            {
                Type t = typeof(TracerRibbonRenderer);
                FieldInfo field = t.GetField("_tdRounds", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo trailFn = t.GetMethod("TdTrail", BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo liveFn = t.GetMethod("TdCoreLive", BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null || trailFn == null || liveFn == null) { status = "SCHEMA_UNAVAILABLE_IN_CURRENT_PATCH"; return null; }
                Array records = field.GetValue(renderer) as Array;
                if (records == null) return null;
                var rows = new System.Collections.Generic.List<FlightGeometry>();
                foreach (object record in records)
                {
                    if (!RecordField<bool>(record, "Alive")) continue;
                    Vector3 head = RecordField<Vector3>(record, "Head"), origin = RecordField<Vector3>(record, "Origin");
                    Vector3 direction = RecordField<Vector3>(record, "Direction");
                    float age = RecordField<float>(record, "Age");
                    float trail = Mathf.Min((head - origin).magnitude, (float)trailFn.Invoke(null, new[] { record }));
                    bool stopped = RecordField<bool>(record, "Stopped");
                    bool eligibleKnown = record.GetType().GetField("Ap043Eligible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                    bool apEligible = OptionalRecordBool(record, "Ap043Eligible");
                    bool optionKnown = record.GetType().GetField("Ap043Option", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                    int apOption = OptionalRecordEnum(record, "Ap043Option");
                    optionKnown &= apOption >= 0 && apOption <= 3;
                    bool drawPathKnown = eligibleKnown && optionKnown;
                    bool optionDrawPath = drawPathKnown && apEligible && apOption != 0;
                    float tailOutSeconds = TracerRibbonRenderer.TdTailOutSeconds;
                    bool tailOutKnown = drawPathKnown;
                    if (optionDrawPath)
                    {
                        FieldInfo envelopeField = record.GetType().GetField("Ap043Envelope", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        object envelope = envelopeField != null ? envelopeField.GetValue(record) : null;
                        FieldInfo duration = envelope != null ? envelope.GetType().GetField("TailOutSeconds", BindingFlags.Instance | BindingFlags.Public) : null;
                        tailOutKnown = duration != null;
                        if (tailOutKnown)
                        {
                            tailOutSeconds = Convert.ToSingle(duration.GetValue(envelope), Inv);
                            tailOutKnown = tailOutSeconds > 0f && !float.IsNaN(tailOutSeconds) && !float.IsInfinity(tailOutSeconds);
                        }
                    }
                    float baseTrail = trail;
                    if (stopped)
                    {
                        float elapsed = age - RecordField<float>(record, "StopAge");
                        baseTrail *= 1f - Mathf.Clamp01(elapsed / TracerRibbonRenderer.TdTailOutSeconds);
                        trail = tailOutKnown ? trail * (1f - Mathf.Clamp01(elapsed / tailOutSeconds)) : -1f;
                    }
                    Vector3 tail = trail >= 0f ? head - direction * trail : head;
                    bool baseVapour = RecordField<bool>(record, "Sniper") && age < .4f;
                    rows.Add(new FlightGeometry { projectileId = RecordField<int>(record, "Id"), frameCount = Time.frameCount,
                        alive = true, eligibleCore = (bool)liveFn.Invoke(null, new[] { record }), tracer = RecordField<bool>(record, "Tracer"),
                        pellet = RecordField<bool>(record, "Pellet"), stopped = stopped,
                        baselineSniperVapourWouldBeActive = baseVapour,
                        sniperVapourPredictionKnown = drawPathKnown,
                        sniperVapourActive = drawPathKnown && !optionDrawPath && baseVapour,
                        actualStoppedTailOutKnown = tailOutKnown, stoppedTailOutSeconds = tailOutKnown ? tailOutSeconds : -1f,
                        predictedBaseTrailMetres = baseTrail,
                        trailPredictionStatus = stopped && !tailOutKnown ? "UNKNOWN_ACTUAL_STOPPED_TRAIL; trailMetres=-1/tailProjectionInvalid; predictedBaseTrail separate" : "SOURCE_EXPECTED_ACTUAL_OPTION_OR_BASELINE_TAILOUT; GPU_NOT_DONE",
                        originWorld = origin, headWorld = head, tailWorld = tail,
                        headScreen = _camera.WorldToScreenPoint(head), tailScreen = _camera.WorldToScreenPoint(tail),
                        visualSpeed = RecordField<float>(record, "Speed"), age = age, coreMetres = RecordField<float>(record, "Core"),
                        haloMetres = RecordField<float>(record, "Halo"), trailMetres = trail, coreMinPx = RecordField<float>(record, "CoreMinPx"),
                        coreCapPx = RecordField<float>(record, "CapPx"), extraHaloPx = RecordField<float>(record, "HaloPx"),
                        look = RecordField<int>(record, "Look"), seekPoints = RecordField<int>(record, "SeekPoints"),
                        ap043EligibleKnown = eligibleKnown, ap043Eligible = apEligible,
                        actualOptionKnown = optionKnown, actualOptionValue = apOption, actualOption = OptionName(apOption),
                        ap043TierKnown = record.GetType().GetField("Ap043Tier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null,
                        ap043Tier = OptionalRecordEnum(record, "Ap043Tier"),
                        ap043SecondaryLookKnown = record.GetType().GetField("Ap043SecondaryLook", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null,
                        ap043SecondaryLook = OptionalRecordEnum(record, "Ap043SecondaryLook"),
                        visualGround = includeVisualGround ? ReadVisualGround(head, RecordField<int>(record, "Id"), Time.frameCount) : null });
                }
                status = "READONLY_BASE_SCHEMA; same native frame; no render/tick/state-write calls";
                return rows.ToArray();
            }
            catch (Exception e) { status = "SCHEMA_READ_FAILED:" + e.GetType().Name; return null; }
        }

        private int CrowdWithin()
        {
            if (_rig == null) return 0;
            int n = 0;
            foreach (ZombieEnemy z in FindObjectsOfType<ZombieEnemy>())
                if (z.IsAlive && Vector3.Distance(z.transform.position, _rig.transform.position) <= 15f) n++;
            return n;
        }
        private CameraSample CameraInfo()
        {
            Matrix4x4 m = _camera.projectionMatrix; float[] values = new float[16];
            for (int i = 0; i < 16; i++) values[i] = m[i];
            bool post = false, postKnown = false;
            Type type = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (type != null)
            {
                Component c = _camera.GetComponent(type); PropertyInfo prop = type.GetProperty("renderPostProcessing");
                if (c != null && prop != null) { post = (bool)prop.GetValue(c); postKnown = true; }
            }
            return new CameraSample { position = _camera.transform.position, euler = _camera.transform.eulerAngles, fov = _camera.fieldOfView,
                aspect = _camera.aspect, near = _camera.nearClipPlane, far = _camera.farClipPlane, projectionM11 = m.m11, projection = values,
                cullingMask = _camera.cullingMask, screenWidth = Screen.width, screenHeight = Screen.height,
                cameraPixelWidth = _camera.pixelWidth, cameraPixelHeight = _camera.pixelHeight,
                nativeBackbuffer = _camera.targetTexture == null, allowHdr = _camera.allowHDR, postEnabled = post, postKnown = postKnown,
                heightAbovePlayer = _rig != null ? _camera.transform.position.y - _rig.transform.position.y : 0f };
        }
        [Serializable] private sealed class VisualGroundSample
        {
            public int engineFrame, projectileId, rendererInstanceId;
            public bool sameEngineFrame, rendererIdentityKnown, visualPlaneMatch;
            public bool requestedLabelVerified = false, wetnessPixelProof = false, shaderGpuVerification = false;
            public bool projectedRendererCandidateOnly = true, pixelCoverageVerified = false;
            public string readStatus = "UNKNOWN", rendererName, rendererPath;
            public string materialSlotStatus = "UNKNOWN_MULTIMATERIAL_PATCH; all shared materials are renderer context";
            public string method = "Readonly real named renderer bounds-plane + camera-to-visual-head ray intersection; no scene plane/object/material/camera mutation";
            public Vector3 atWorld, worldHit, planeNormal, cameraRayOrigin, cameraRayDirection;
            public Vector3 sourceTransformPosition, sourceTransformEuler, sourceTransformScale, boundsCenter, boundsSize;
            public float planeY;
            public GroundMaterialSample[] sharedMaterialSamples;
        }
        private static string TransformPath(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
        private VisualGroundSample ReadVisualGround(Vector3 atWorld, int projectileId, int engineFrame)
        {
            var sample = new VisualGroundSample { engineFrame = engineFrame, projectileId = projectileId, atWorld = atWorld,
                sameEngineFrame = engineFrame == Time.frameCount, planeNormal = Vector3.up };
            try
            {
                if (!_visualGroundSearched)
                {
                    _visualGroundSearched = true; int matches = 0;
                    foreach (MeshRenderer renderer in FindObjectsOfType<MeshRenderer>())
                        if (renderer.name == "Plaza_Slate" && renderer.gameObject.activeInHierarchy)
                        { _visualGroundRenderer = renderer; matches++; }
                    if (matches != 1) _visualGroundRenderer = null;
                }
                Renderer visual = _visualGroundRenderer;
                if (visual == null || !visual.enabled || !visual.gameObject.activeInHierarchy || _camera == null)
                { sample.readStatus = "UNKNOWN_NAMED_VISUAL_RENDERER_ABSENT_AMBIGUOUS_OR_NOT_READY"; return sample; }
                Bounds bounds = visual.bounds;
                sample.rendererName = visual.name; sample.rendererPath = TransformPath(visual.transform);
                sample.rendererInstanceId = visual.GetInstanceID(); sample.rendererIdentityKnown = true;
                sample.sourceTransformPosition = visual.transform.position; sample.sourceTransformEuler = visual.transform.eulerAngles;
                sample.sourceTransformScale = visual.transform.lossyScale; sample.boundsCenter = bounds.center; sample.boundsSize = bounds.size;
                sample.planeY = bounds.center.y;
                sample.sharedMaterialSamples = ReadVisualGroundMaterials(visual.sharedMaterials);
                if (bounds.size.y > .05f) { sample.readStatus = "UNKNOWN_NONPLANAR_VISUAL_BOUNDS"; return sample; }
                Vector3 screen = _camera.WorldToScreenPoint(atWorld);
                if (screen.z <= 0f) { sample.readStatus = "UNKNOWN_QUERY_BEHIND_CAMERA"; return sample; }
                Ray ray = _camera.ScreenPointToRay(screen);
                sample.cameraRayOrigin = ray.origin; sample.cameraRayDirection = ray.direction;
                // Mathematical intersection with the actual renderer plane; nothing is created/rendered in the scene.
                if (Mathf.Abs(ray.direction.y) < 1e-6f) { sample.readStatus = "UNKNOWN_RAY_PARALLEL_TO_VISUAL_PLANE"; return sample; }
                float distance = (sample.planeY - ray.origin.y) / ray.direction.y;
                if (distance < 0f) { sample.readStatus = "UNKNOWN_VISUAL_PLANE_BEHIND_RAY"; return sample; }
                sample.worldHit = ray.GetPoint(distance);
                sample.visualPlaneMatch = sample.worldHit.x >= bounds.min.x && sample.worldHit.x <= bounds.max.x &&
                    sample.worldHit.z >= bounds.min.z && sample.worldHit.z <= bounds.max.z;
                sample.sameEngineFrame &= engineFrame == Time.frameCount;
                sample.readStatus = sample.sameEngineFrame && sample.visualPlaneMatch ?
                    "READONLY_SAME_FRAME_NAMED_VISUAL_BOUNDS_PLANE_CONTEXT; NOT_PIXEL_COVERAGE_WETNESS_PROOF" : "UNKNOWN_OUTSIDE_VISUAL_BOUNDS_OR_FRAME_CHANGED";
            }
            catch (Exception e) { sample.readStatus = "UNKNOWN_VISUAL_GROUND_READ:" + e.GetType().Name; }
            return sample;
        }
        private static GroundMaterialSample[] ReadVisualGroundMaterials(Material[] materials)
        {
            var samples = new GroundMaterialSample[materials.Length];
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material material = materials[slot];
                var sample = new GroundMaterialSample { slot = slot, exists = material != null };
                samples[slot] = sample;
                if (material == null || material.shader == null) continue;
                sample.instanceId = material.GetInstanceID(); sample.sharedMaterialName = material.name;
                sample.shaderName = material.shader.name;
                var scalars = new System.Collections.Generic.List<GroundScalarSample>();
                Shader shader = material.shader;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    string name = shader.GetPropertyName(i), type = shader.GetPropertyType(i).ToString();
                    if ((type != "Float" && type != "Range") ||
                        !(name.StartsWith("_Wet", StringComparison.Ordinal) || name == "_Smoothness" || name == "_Roughness" || name == "_Glossiness")) continue;
                    var scalar = new GroundScalarSample { property = name, present = material.HasProperty(name) };
                    if (scalar.present)
                    {
                        float value = material.GetFloat(name); scalar.finite = !float.IsNaN(value) && !float.IsInfinity(value);
                        if (scalar.finite) scalar.value = value;
                    }
                    scalars.Add(scalar);
                }
                sample.scalars = scalars.ToArray();
                sample.status = "READONLY_VISUAL_RENDERER_SHARED_MATERIAL; NOT_WETNESS_OR_GPU_PIXEL_PROOF";
            }
            return samples;
        }

        private static GroundSample Ground(Vector3 at)
        {
            var sample = new GroundSample(); RaycastHit hit;
            if (!Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out hit, 10f, (1 << 0) | (1 << PrototypeLayers.Environment), QueryTriggerInteraction.Ignore)) return sample;
            sample.hit = true; sample.point = hit.point; sample.normal = hit.normal; sample.triangleIndex = hit.triangleIndex; sample.collider = hit.collider.name;
            Renderer renderer = hit.collider.GetComponent<Renderer>() ?? hit.collider.GetComponentInParent<Renderer>();
            if (renderer != null)
            {
                sample.renderer = renderer.name;
                // sharedMaterials is readonly here: never touch renderer.material(s), which can instantiate copies.
                Material[] materials = renderer.sharedMaterials;
                sample.materialNames = new string[materials.Length]; sample.materialShaderNames = new string[materials.Length];
                sample.sharedMaterialSamples = new GroundMaterialSample[materials.Length];
                string[] properties = { "_Wetness", "_WetStrength", "_RainWetness", "_PuddleAmount", "_Smoothness", "_Glossiness", "_GlossMapScale", "_Roughness", "_Metallic" };
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    var item = new GroundMaterialSample { slot = i, exists = material != null, status = "UNKNOWN_MATERIAL" };
                    sample.sharedMaterialSamples[i] = item;
                    sample.materialNames[i] = material != null ? material.name : "null";
                    if (material == null) continue;
                    item.instanceId = material.GetInstanceID(); item.sharedMaterialName = material.name;
                    item.shaderName = material.shader != null ? material.shader.name : null;
                    sample.materialShaderNames[i] = item.shaderName;
                    item.scalars = new GroundScalarSample[properties.Length];
                    for (int prop = 0; prop < properties.Length; prop++)
                    {
                        var scalar = new GroundScalarSample { property = properties[prop], present = material.HasProperty(properties[prop]) };
                        if (scalar.present)
                        {
                            float value = material.GetFloat(properties[prop]);
                            scalar.finite = !float.IsNaN(value) && !float.IsInfinity(value);
                            if (scalar.finite) scalar.value = value;
                        }
                        item.scalars[prop] = scalar;
                    }
                    item.status = item.shaderName != null ? "READONLY_SHARED_MATERIAL_PROPERTIES; NOT_WETNESS_OR_PIXEL_REVIEW" : "UNKNOWN_SHADER";
                }
                // Conservative exact slot: only a non-convex matching MeshCollider/MeshFilter single triangle submesh.
                // Multiple/ambiguous slots, BoxCollider, parent proxy or absent material remain UNKNOWN.
                MeshCollider meshCollider = hit.collider as MeshCollider;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = meshCollider != null ? meshCollider.sharedMesh : null;
                if (mesh != null && !meshCollider.convex && filter != null && filter.sharedMesh == mesh &&
                    meshCollider.transform == renderer.transform && hit.triangleIndex >= 0 && mesh.subMeshCount == 1 &&
                    mesh.GetTopology(0) == MeshTopology.Triangles && (long)hit.triangleIndex * 3L < (long)mesh.GetIndexCount(0) &&
                    materials.Length == 1 && materials[0] != null)
                {
                    sample.selectedMaterialSlot = 0; sample.selectedSharedMaterialName = materials[0].name;
                    sample.selectedShaderName = materials[0].shader != null ? materials[0].shader.name : null;
                    sample.materialSlotStatus = sample.selectedShaderName != null ? "READONLY_MATCHED_SINGLE_MESH_TRIANGLE_SLOT" : "UNKNOWN_SELECTED_SHADER";
                }
                else sample.materialSlotStatus = "UNKNOWN_TRIANGLE_RENDERER_MESH_OR_MATERIAL_SLOT; samples are renderer context only";
            }
            return sample;
        }
        private void Restore()
        {
            _input.Fire = false;
            Time.captureDeltaTime = _oldCaptureDelta;
            if (_preview) { TracerStyle.ClearPreview(); _preview = false; }
            if (_configured)
            {
                _configured = false; InputSourceRegistry.SetCurrent(_oldInput);
                if (_rig != null) { _rig.Health.DiagnosisInvulnerable = _oldInvulnerable; _rig.Motor.SetInputSource(null); _rig.Aim.SetInputSource(null); }
                if (_director != null) _director.SpawningEnabledDev = _oldSpawn;
            }
            Close(ref _times); Close(ref _frameMeta); Close(ref _eventMeta); Close(ref _audioTimes);
            Close(ref _targetedTreeStates); Close(ref _targetedEvents);
        }
        private static void Close(ref StreamWriter writer) { if (writer != null) { writer.Flush(); writer.Dispose(); writer = null; } }
        private void Finish(string reason, int exit)
        {
            if (_done) return; _done = true;
            if (_report != null)
            {
                _report.lookApplicationVerified = _report.framesWritten > 0 && _report.lookBindingFramesUnknown == 0 &&
                    _report.lookBindingFramesVerified == _report.framesWritten;
                _report.stopReason = reason; _report.status = exit == 0 ? "CAPTURED_MEASUREMENT_NOT_DONE" : "INCOMPLETE_MEASUREMENT_NOT_DONE";
                _report.shots = _shots; _report.impacts = _impacts; _report.writtenBytes = _bytes;
                _report.simulationSeconds = _report.framesWritten / 60f;
                _report.wallSeconds = (float)(Time.realtimeSinceStartupAsDouble - _captureReal0);
                File.WriteAllText(Path.Combine(_out, "scenario.json"), JsonUtility.ToJson(_report, true));
            }
            if (_targetedSetup != null)
            {
                _targetedSetup.captureFinishedEngineFrame = Time.frameCount;
                try
                {
                    using (var file = new FileStream(Path.Combine(_out, "targeted-setup.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(file)) writer.Write(JsonUtility.ToJson(_targetedSetup, true));
                }
                catch (Exception e) { Debug.LogError("[AP043Capture] targeted receipt failed: " + e); exit = 4; }
            }
            Restore(); Debug.Log("[AP043Capture] " + reason + " exit=" + exit + " MEASUREMENT_NOT_DONE"); Application.Quit(exit);
        }
        private sealed class CaptureInput : IInputSource
        {
            public bool Fire; public Vector2 Aim;
            private bool _was, _pressed; private int _frame = -1;
            public bool IsEnabled => true;
            public Vector2 Move => Vector2.zero;
            public AimSourceKind AimKind => AimSourceKind.Pointer;
            public Vector2 AimScreenPosition => Aim;
            public Vector2 AimStick => Vector2.zero;
            public bool FireHeld => Fire;
            public bool FirePressed { get { if (_frame != Time.frameCount) { _frame = Time.frameCount; _pressed = Fire && !_was; _was = Fire; } return _pressed; } }
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
