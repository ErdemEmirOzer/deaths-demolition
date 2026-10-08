using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-096 Faz2: only the 17 walks missing from S-05's 18-route evidence.
    /// Requires WP-092 world/bake 588a3b74 (S-05 tip 60c94dd8; S-07 22551b09 carries the same world).
    /// This fixture uses runtime APIs available before that merge; an old world must FAIL its prerequisites.
    /// PlayerMotor/CharacterController walking does not prove Executioner door passage, crowd flow or a soak.
    /// </summary>
    [TestFixture]
    public sealed class V6WorldDependencyTests
    {
        private const float ProjectionRadius = 0.5f, FloorTolerance = 0.30f, ArrivalRadius = 0.8f;
        private const float RepathSeconds = 0.25f, ProgressMetres = 0.5f, StallSeconds = 3f, RouteSeconds = 60f;
        private static readonly Vector3 Start = new Vector3(0f, 0.1f, -3f);
        private static readonly string[] InnerIds =
        {
            "I_WestKairoSouth", "I_CloisterWest", "I_R7SouthWest", "I_R7NorthWest", "I_ServiceYardSouth",
            "I_HallRear", "I_CloisterEast", "I_ShoreWest", "I_ServiceYardEast", "I_PlazaSouthWest",
        };
        private static readonly string[] EdgeIds =
        {
            "N_PalaceGateWest", "N_PalaceGateEast", "NE_StonePathCorner", "E_SakuraGroveNorth",
            "E_SakuraGroveSouth", "SE_PondApproach", "S_BridgeSouthEnd", "S_ShorePathWest",
            "SW_BambooEntry", "W_BambooCorridorSouth", "W_BambooCorridorNorth", "NW_ServiceGate",
        };
        // Frozen layout-v6.json interiorClear midpoints, not broad nearest-point searches.
        // R7's centre contains a sakura; this explicit interior anchor must itself be walkable.
        private static readonly (string id, Vector3 at)[] RoomTargets =
        {
            ("B1", new Vector3(25.37f, 0.25f, -49.61f)),
            ("B2", new Vector3(29.97f, 0.25f, 49.61f)),
            ("B3", new Vector3(-51.83f, 0.25f, -49.61f)),
            ("B4", new Vector3(-51.83f, 0.25f, -35.39f)),
            ("B5", new Vector3(35.37f, 0.25f, 50.81f)),
            ("B6", new Vector3(11.57f, 0.25f, -50.81f)),
            ("R7_SakuraCourt", new Vector3(29f, 0f, -5f)),
        };

        private sealed class WalkInput : IInputSource
        {
            public Vector2 Value;
            public bool IsEnabled => true;
            public Vector2 Move => Value;
            public AimSourceKind AimKind => AimSourceKind.Pointer;
            public Vector2 AimScreenPosition => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            public Vector2 AimStick => Vector2.zero;
            public bool FireHeld => false;
            public bool FirePressed => false;
            public bool FireReleased => false;
            public bool ReloadPressed => false;
            public bool PausePressed => false;
            public bool DebugPanelPressed => false;
            public event Action<AimSourceKind> AimKindChanged { add { } remove { } }
            public void Enable() { }
            public void Disable() { Value = Vector2.zero; }
        }

        private sealed class Route
        {
            public string Id;
            public Vector3 From, To;
            public NavRegionVolume TargetRoom;
        }

        private PlayerRig _rig;
        private CharacterController _body;
        private WalkInput _input;
        private string _oldWorld, _oldScene;
        private float _oldCaptureDelta, _oldTimeScale;
        private bool _oldIgnoreLogs, _started, _oldInvulnerable, _oldControl;
        private CameraMode _oldCamera;
        private int _environmentMask;
        private readonly List<string> _failures = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _oldWorld = Environment.GetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable);
            _oldScene = Environment.GetEnvironmentVariable(WorldSourceSettings.SceneEnvironmentVariable);
            _oldCaptureDelta = Time.captureDeltaTime;
            _oldTimeScale = Time.timeScale;
            _oldIgnoreLogs = LogAssert.ignoreFailingMessages;
            _oldCamera = CameraModeService.Current;
            _started = false;
            _failures.Clear();
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            if (_input != null) _input.Value = Vector2.zero;
            if (_rig != null)
            {
                if (_rig.Motor != null) { _rig.Motor.SetInputSource(null); _rig.Motor.ControlEnabled = _oldControl; }
                if (_rig.Health != null) _rig.Health.DiagnosisInvulnerable = _oldInvulnerable;
            }
            try
            {
                if (_started)
                {
                    RunFlowClock.ClearHolds();
                    Time.timeScale = 1f;
                    PrototypeUiFlow.GoToMainMenu();
                    yield return WaitFor(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu, 60f, "cleanup MainMenu");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, _oldWorld);
                Environment.SetEnvironmentVariable(WorldSourceSettings.SceneEnvironmentVariable, _oldScene);
                Time.captureDeltaTime = _oldCaptureDelta;
                Time.timeScale = _oldTimeScale;
                if (_started) CameraModeService.Request(_oldCamera, instant: true);
                LogAssert.ignoreFailingMessages = _oldIgnoreLogs;
                _rig = null;
                _body = null;
                _input = null;
                _started = false;
            }
        }

        [UnityTest]
        [Category("WP096")]
        [Timeout(1200000)]
        public IEnumerator V6_YeniOdalarR7VeOnIcSpawn_BaslangicaBagli_FizikselYuruyus17()
        {
            _started = true;
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "V6");
            Environment.SetEnvironmentVariable(WorldSourceSettings.SceneEnvironmentVariable, WorldSourceSettings.V6SceneName);
            Assert.AreEqual(WorldSource.V6, WorldSourceSettings.Resolve(out string resolvedScene), "command-line override V6'yı değiştiriyor");
            Assert.AreEqual(WorldSourceSettings.V6SceneName, resolvedScene);
            RunFlowClock.ClearHolds();
            Time.timeScale = 1f;
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return WaitFor(() => SceneManager.GetSceneByName(WorldSourceSettings.V6SceneName).isLoaded &&
                PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null && RunDirector.Instance != null &&
                EnemySpawnDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting, 60f, "V6 Run/Fighting");
            Assert.AreEqual(WorldLayoutKind.V6, WorldLayout.Current);
            Assert.IsTrue(SpawnWorldProfile.IsV6);
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            _rig = PlayerRegistry.Primary;
            Assert.IsNotNull(_rig.Motor);
            Assert.IsNotNull(_rig.Health);
            _body = _rig.Motor.GetComponent<CharacterController>();
            Assert.IsNotNull(_body, "gerçek motorun CC'si yok");
            Assert.AreSame(_rig.transform, _body.transform, "rig kökü motorun ayak kökü olmalı");
            Assert.Less(Vector3.Distance(_body.transform.lossyScale, Vector3.one), 0.001f, "CC ölçeği değişmiş");
            Assert.IsTrue(_body.enabled && _body.gameObject.activeInHierarchy);
            _oldInvulnerable = _rig.Health.DiagnosisInvulnerable;
            _oldControl = _rig.Motor.ControlEnabled;
            _rig.Health.DiagnosisInvulnerable = true;
            _rig.Motor.ControlEnabled = true;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
            _environmentMask = LayerMask.GetMask("Environment", "CameraOccluder");
            Assert.AreNotEqual(0, _environmentMask, "fizik sorgusu katmansız kalamaz");
            Assert.AreNotEqual(0, LayerMask.GetMask("Environment"), "Environment katmanı yok");
            Assert.AreNotEqual(0, LayerMask.GetMask("CameraOccluder"), "CameraOccluder katmanı yok");
            Assert.AreEqual(0, _environmentMask & (1 << _body.gameObject.layer), "fizik sorgusu oyuncunun kendi katmanını içeriyor");

            _input = new WalkInput();
            _rig.Motor.SetInputSource(_input);
            Assert.LessOrEqual(Horizontal(Feet(), Start), 0.5f, "canlı oyuncu başlangıcı frozen sözleşmeden farklı");
            List<Route> routes = CollectRoutes();
            Assert.AreEqual(17, routes.Count);
            Time.captureDeltaTime = 1f / 60f;
            Debug.Log("[DD-TEST] wp096.faz2.begin routes=17 world=" + resolvedScene +
                " ccRadius=" + _body.radius + " height=" + _body.height + " skin=" + _body.skinWidth +
                " NOTE=player-walk-only;ExecutionerP4-separate");
            int completed = 0;
            foreach (Route route in routes)
            {
                int before = _failures.Count;
                yield return Walk(route);
                if (_failures.Count == before) completed++;
            }
            Debug.Log("[DD-TEST] wp096.faz2.end routes=" + routes.Count + " completed=" + completed + " failures=" + _failures.Count);
            Assert.IsEmpty(_failures, string.Join("\n", _failures));
            Assert.AreEqual(17, completed, "hiçbir rota atlanamaz");
        }

        private List<Route> CollectRoutes()
        {
            var volumes = new Dictionary<string, NavRegionVolume>(StringComparer.Ordinal);
            foreach (NavRegionMarker marker in UnityEngine.Object.FindObjectsOfType<NavRegionMarker>())
            {
                Assert.AreEqual(WorldSourceSettings.V6SceneName, marker.gameObject.scene.name, marker.name + ": başka sahnede bölge");
                NavRegionVolume v = marker.Volume;
                Assert.IsNotNull(v, marker.name + ": kayıtsız hacim");
                Assert.IsFalse(string.IsNullOrEmpty(v.id));
                Assert.IsFalse(volumes.ContainsKey(v.id), "yinelenen bölge: " + v.id);
                volumes.Add(v.id, v);
            }
            var markers = new Dictionary<string, SpawnPointMarker>(StringComparer.Ordinal);
            foreach (SpawnPointMarker marker in UnityEngine.Object.FindObjectsOfType<SpawnPointMarker>())
            {
                Assert.AreEqual(WorldSourceSettings.V6SceneName, marker.gameObject.scene.name, marker.name + ": başka sahnede spawn");
                Assert.IsFalse(string.IsNullOrEmpty(marker.PointId));
                Assert.IsFalse(markers.ContainsKey(marker.PointId), "yinelenen spawn: " + marker.PointId);
                markers.Add(marker.PointId, marker);
            }
            Assert.IsTrue(ExactIds(markers.Keys, InnerIds, EdgeIds), "canlı spawn seti12Edge+10Inner exact değil");
            Assert.IsTrue(SpawnPointRegistry.IsBuilt);
            var registered = new List<string>();
            foreach (SpawnPointRuntime point in SpawnPointRegistry.Points) registered.Add(point.Id);
            Assert.IsTrue(ExactIds(registered, InnerIds, EdgeIds), "registry fallback/eksik/fazla spawn");
            foreach (string id in EdgeIds) CheckMarker(markers[id], SpawnPointKind.Edge);
            var routes = new List<Route>();
            foreach (string id in InnerIds)
            {
                SpawnPointMarker marker = markers[id];
                CheckMarker(marker, SpawnPointKind.Inner);
                routes.Add(new Route { Id = id + "->start", From = marker.transform.position, To = Start });
            }
            foreach ((string id, Vector3 at) in RoomTargets)
            {
                Assert.IsTrue(volumes.TryGetValue(id, out NavRegionVolume room), "dünya merge eksik: " + id);
                Assert.AreEqual(NavRegionKind.Room, room.kind, id);
                Assert.IsTrue(room.Contains(at), id + ": frozen anchor oda dışında");
                routes.Add(new Route { Id = "start->" + id, From = Start, To = at, TargetRoom = room });
            }
            return routes;
        }

        private static void CheckMarker(SpawnPointMarker marker, SpawnPointKind kind)
        {
            Assert.AreEqual(kind, marker.Kind, marker.PointId);
            Assert.IsTrue(marker.PointEnabled, marker.PointId + ": devre dışı");
            SpawnPointRuntime point = SpawnPointRegistry.FindById(marker.PointId);
            Assert.IsNotNull(point);
            Assert.IsTrue(point.HasMarker && point.Enabled, marker.PointId + ": registry marker'a bağlı değil");
            Assert.AreEqual(kind, point.Definition.Kind, marker.PointId + ": registry kind farklı");
            Assert.LessOrEqual(Vector3.Distance(marker.transform.position, point.WorldPosition), 0.01f, marker.PointId + ": fallback konum");
        }

        private IEnumerator Walk(Route route)
        {
            var log = new StringBuilder("[DD-TEST] wp096.faz2.route " + route.Id +
                " rawFrom=" + route.From.ToString("F3") + " rawTo=" + route.To.ToString("F3"));
            Vector3 from = Vector3.zero, to = Vector3.zero;
            string failure = null;
            if (!Project(route.From, null, true, out from, out failure) ||
                !Project(route.To, route.TargetRoom, true, out to, out failure))
            {
                log.Append(" FAIL=").Append(failure);
                Debug.Log(log.ToString());
                _failures.Add(route.Id + ": " + failure);
                yield break;
            }
            log.Append(" sampledFrom=").Append(from.ToString("F3")).Append(" sampledTo=").Append(to.ToString("F3"));
            var path = new NavMeshPath();
            if (Horizontal(from, to) <= ArrivalRadius) failure = "başlangıç/hedef aynı kabul bölgesinde";
            if (failure != null || !Repath(from, to, path, out failure))
            {
                log.Append(" FAIL=").Append(failure);
                Debug.Log(log.ToString());
                _failures.Add(route.Id + ": " + failure);
                yield break;
            }
            log.Append(" initialCorners=");
            foreach (Vector3 c in path.corners) log.Append(c.ToString("F3"));
            log.Append(" pathLength=").Append(Remaining(from, path.corners).ToString("F3"));
            _input.Value = Vector2.zero;
            _rig.Teleport(from + Vector3.up * 0.05f, Quaternion.identity); // The only teleport in this route.
            yield return null;
            float elapsed = 0f, nextRepath = 0f, best = float.MaxValue, lastImprove = 0f, maxNoProgress = 0f;
            float wallStart = Time.realtimeSinceStartup;
            Vector3 corner = to;
            while (failure == null)
            {
                Vector3 feet = Feet();
                if (!_body.enabled || !_rig.Motor.ControlEnabled || CameraModeService.Current != CameraMode.TopDown)
                { failure = "motor/CC/TD koşulu kayboldu"; break; }
                if (Horizontal(feet, to) <= ArrivalRadius && Mathf.Abs(feet.y - to.y) <= FloorTolerance && _body.isGrounded)
                {
                    if (route.TargetRoom != null && !route.TargetRoom.Contains(feet)) failure = "gerçek ayak oda dışında";
                    break;
                }
                if (feet.y < -1f) { failure = "düştü"; break; }
                if (elapsed > RouteSeconds || Time.realtimeSinceStartup - wallStart > 90f) { failure = "süre aşımı"; break; }
                if (elapsed >= nextRepath)
                {
                    nextRepath = elapsed + RepathSeconds;
                    if (!Project(feet, null, false, out Vector3 here, out failure) || !Repath(here, to, path, out failure)) break;
                    Vector3[] corners = path.corners;
                    corner = to;
                    for (int c = 1; c < corners.Length; c++)
                        if (Horizontal(feet, corners[c]) > 0.35f) { corner = corners[c]; break; }
                    float rem = Remaining(feet, corners);
                    if (rem <= best - ProgressMetres) { best = rem; lastImprove = elapsed; }
                    maxNoProgress = Mathf.Max(maxNoProgress, elapsed - lastImprove);
                    if (elapsed - lastImprove > StallSeconds) { failure = "3s içinde0.5m yol ilerlemesi yok; remaining=" + rem; break; }
                }
                Vector3 dir = corner - feet;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.zero;
                PrototypeCameraRig camera = PrototypeCameraRig.Active;
                if (camera == null) { failure = "kamera kayboldu"; break; }
                _input.Value = new Vector2(Vector3.Dot(dir, camera.PlanarRight), Vector3.Dot(dir, camera.PlanarForward));
                yield return null; // PlayerMotor.Update performs the real swept CharacterController.Move.
                elapsed += Time.deltaTime;
            }
            _input.Value = Vector2.zero;
            log.Append(" endFeet=").Append(Feet().ToString("F3")).Append(" seconds=").Append(elapsed.ToString("F3"))
                .Append(" maxNoProgress=").Append(maxNoProgress.ToString("F3")).Append(" status=").Append(path.status)
                .Append(" outcome=").Append(failure ?? "REACHED");
            Debug.Log(log.ToString());
            if (failure != null) _failures.Add(route.Id + ": " + failure);
        }

        private Vector3 Feet() => _body.transform.TransformPoint(_body.center) - Vector3.up * (_body.height * 0.5f);

        private bool Project(Vector3 raw, NavRegionVolume room, bool endpoint, out Vector3 sampled, out string failure)
        {
            sampled = Vector3.zero;
            failure = null;
            if (!NavMesh.SamplePosition(raw, out NavMeshHit hit, ProjectionRadius, NavMesh.AllAreas))
            { failure = "NavMesh0.5m içinde yok @" + raw.ToString("F3"); return false; }
            sampled = hit.position;
            if (!AcceptProjection(raw, sampled, room))
            { failure = "projection floor/oda/kayma sınırı @" + raw.ToString("F3") + "->" + sampled.ToString("F3"); return false; }
            if (room != null)
            {
                float margin = _body.radius + _body.skinWidth;
                if (!room.Contains(sampled + Vector3.right * margin) || !room.Contains(sampled - Vector3.right * margin) ||
                    !room.Contains(sampled + Vector3.forward * margin) || !room.Contains(sampled - Vector3.forward * margin))
                { failure = "anchor kapsülü oda sınırında: " + room.id; return false; }
            }
            // Mid-route wall contacts are resolved by the real CC; a static overlap is not a walk verdict.
            if (!endpoint) return true;
            // Floor is below the capsule bottom; Environment/CameraOccluder solids at the endpoint must be clear.
            float lift = _body.skinWidth + 0.02f;
            Vector3 bottom = sampled + Vector3.up * (_body.radius + lift);
            Vector3 top = sampled + Vector3.up * (Mathf.Max(_body.radius, _body.height - _body.radius) + lift);
            if (Physics.CheckCapsule(bottom, top, _body.radius, _environmentMask, QueryTriggerInteraction.Ignore))
            { failure = "endpoint gerçek kapsül için kapalı @" + sampled.ToString("F3"); return false; }
            return true;
        }

        private static bool Repath(Vector3 from, Vector3 to, NavMeshPath path, out string failure)
        {
            bool calculated = NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path);
            Vector3[] corners = path.corners;
            bool valid = AcceptPath(calculated, path.status, corners, from, to);
            failure = valid ? null : "complete endpoint path yok; status=" + path.status + " corners=" + corners.Length;
            return valid;
        }

        private static bool AcceptProjection(Vector3 raw, Vector3 sampled, NavRegionVolume room) =>
            Horizontal(raw, sampled) <= ProjectionRadius && Mathf.Abs(raw.y - sampled.y) <= FloorTolerance &&
            (room == null || (room.Contains(raw) && room.Contains(sampled)));

        private static bool AcceptPath(bool calculated, NavMeshPathStatus status, Vector3[] corners, Vector3 from, Vector3 to) =>
            calculated && status == NavMeshPathStatus.PathComplete && corners != null && corners.Length >= 2 &&
            Horizontal(from, to) > 0.001f && Vector3.Distance(corners[corners.Length - 1], to) <= 0.25f &&
            Vector3.Distance(corners[0], from) <= ProjectionRadius;

        private static bool ExactIds(IEnumerable<string> actual, string[] a, string[] b)
        {
            var wanted = new HashSet<string>(a, StringComparer.Ordinal);
            foreach (string id in b) if (!wanted.Add(id)) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in actual)
                if (string.IsNullOrEmpty(id) || !wanted.Contains(id) || !seen.Add(id)) return false;
            return seen.SetEquals(wanted);
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float Remaining(Vector3 at, Vector3[] corners)
        {
            if (corners == null || corners.Length < 2) return float.PositiveInfinity;
            float length = Vector3.Distance(at, corners[1]);
            for (int c = 2; c < corners.Length; c++) length += Vector3.Distance(corners[c - 1], corners[c]);
            return length;
        }

        private static IEnumerator WaitFor(Func<bool> condition, float seconds, string name)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition() && Time.realtimeSinceStartup - start < seconds) yield return null;
            Assert.IsTrue(condition(), name + " zaman aşımı");
        }

        [Test]
        [Category("WP096")]
        public void Faz2_KabulKorumasi_EksikYinelenenVeFazlaKimlikReddedilir()
        {
            Assert.IsTrue(ExactIds(new[] { "a", "b" }, new[] { "a" }, new[] { "b" }));
            Assert.IsFalse(ExactIds(new[] { "a" }, new[] { "a" }, new[] { "b" }));
            Assert.IsFalse(ExactIds(new[] { "a", "a", "b" }, new[] { "a" }, new[] { "b" }));
            Assert.IsFalse(ExactIds(new[] { "a", "b", "c" }, new[] { "a" }, new[] { "b" }));
        }

        [Test]
        [Category("WP096")]
        public void Faz2_KabulKorumasi_DisOdaYanlisKatVePartialPathReddedilir()
        {
            var room = new NavRegionVolume { center = Vector3.zero, size = new Vector3(2f, 3f, 2f) };
            Assert.IsTrue(AcceptProjection(Vector3.zero, new Vector3(0.2f, 0.1f, 0f), room));
            Assert.IsFalse(AcceptProjection(new Vector3(0.9f, 0f, 0f), new Vector3(1.1f, 0f, 0f), room));
            Assert.IsFalse(AcceptProjection(Vector3.zero, Vector3.up, room));
            Assert.IsFalse(AcceptProjection(Vector3.zero, Vector3.right, room));
            var end = new Vector3(2f, 0f, 0f);
            var corners = new[] { Vector3.zero, end };
            Assert.IsTrue(AcceptPath(true, NavMeshPathStatus.PathComplete, corners, Vector3.zero, end));
            Assert.IsFalse(AcceptPath(true, NavMeshPathStatus.PathPartial, corners, Vector3.zero, end));
            Assert.IsFalse(AcceptPath(false, NavMeshPathStatus.PathComplete, corners, Vector3.zero, end));
            Assert.IsFalse(AcceptPath(true, NavMeshPathStatus.PathComplete, new Vector3[0], Vector3.zero, end));
            Assert.IsFalse(AcceptPath(true, NavMeshPathStatus.PathComplete, new[] { Vector3.zero, Vector3.right }, Vector3.zero, end));
        }
    }
}
