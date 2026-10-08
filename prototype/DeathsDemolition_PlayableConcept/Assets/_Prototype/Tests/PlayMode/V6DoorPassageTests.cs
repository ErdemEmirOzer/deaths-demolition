using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
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
    /// WP-096 P4: seven side-door cases, each with ONE 30-second window for all five bodies:
    /// two Standard + Runner + Guard + Executioner. All must physically cross in 12 s; Executioner in 6 s.
    /// This is separate from the 17 PLAYER walks. Requires the WP-092 588a3b74 world/bake
    /// (S05 60c94dd8 / S07 22551b09); source compilation alone does not validate those colliders.
    /// No production steering, collision, gate cost or ordinary-path policy is overridden.
    /// </summary>
    [TestFixture]
    public sealed class V6DoorPassageTests
    {
        private const float Window = 30f, BodyDeadline = 12f, ExecutionerDeadline = 6f;
        private const float ProjectionRadius = 0.5f, FloorTolerance = 0.30f, DoorWidth = 2f;
        private const float SlowSpeed = 0.2f, JambSeconds = 3f, PhysicalStepLimit = 0.75f;
        private const float Outside = 8f, Inside = 3f, KitWallLine = 2.235f;
        private const uint FactorySeed = 9652u; // Fifth life seed 499922791: skinned first scale draw ~=0.996884, height ~=2.822844m.
        private const string ExplicitReason = "P4 guarded engine allocation and merged WP-092 world required";

        private sealed class Door
        {
            public readonly string Id, InteriorId;
            public readonly Vector3 AuthoredGate, Plane, Inward;
            public Vector3 Tangent => Vector3.Cross(Vector3.up, Inward);
            public Door(string id, string interior, float axisX, float z, bool east, bool corridor = false)
            {
                Id = id;
                InteriorId = interior;
                Inward = east ? Vector3.left : Vector3.right;
                // New nagaya rects describe the floor edge at +/-2.37; physical side walls are at +/-2.235.
                // K1_W0's authored gate already lies on the physical wall line.
                Plane = new Vector3(axisX + (east ? KitWallLine : -KitWallLine), 0.25f, z);
                AuthoredGate = Plane + (corridor ? Vector3.zero : -Inward * 0.135f);
            }
        }

        private static readonly Door[] Doors =
        {
            new Door("B1_E0", "B1", 25.37f, -51.2f, true),
            new Door("B2_W0", "B2", 29.97f, 51.2f, false),
            new Door("B3_E0", "B3", -51.83f, -51.2f, true),
            new Door("B4_E0", "B4", -51.83f, -33.8f, true),
            new Door("B5_E0", "B5", 35.37f, 51.2f, true),
            new Door("B6_E0", "B6", 11.57f, -51.2f, true),
            new Door("K1_W0", "K1_WestKairo", -33f, 0f, false, true),
        };
        private static readonly EnemyTier[] Tiers =
            { EnemyTier.Standard, EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };
        // A transverse front, 8 m outside the plane; the Executioner starts on the centreline.
        // Separation is checked against ACTUAL spawned capsule radii before any movement frame.
        private static readonly float[] Offsets = { -1.6f, 1.6f, -2.8f, 3f, 0f };

        private sealed class Body
        {
            public ZombieEnemy Zombie;
            public CharacterController Capsule;
            public int Life;
            public string Name;
            public Vector3 Last, Crossing;
            public float LastTime, CrossedAt = -1f, InsideAt = -1f, SlowFor, LongestSlow;
            public bool Failed;
        }

        private PlayerRig _rig;
        private readonly List<Body> _bodies = new List<Body>();
        private readonly List<string> _failures = new List<string>();
        private bool _started, _oldInvulnerable, _oldControl;
        private string _oldSource, _oldScene;
        private float _oldCapture, _oldScale, _oldStuckWindow;
        private CameraMode _oldCamera;
        private ZombieEnemy.RisePolicy _oldRise;

        [SetUp]
        public void SetUp()
        {
            _oldSource = Environment.GetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable);
            _oldScene = Environment.GetEnvironmentVariable(WorldSourceSettings.SceneEnvironmentVariable);
            _oldCapture = Time.captureDeltaTime;
            _oldScale = Time.timeScale;
            _oldCamera = CameraModeService.Current;
            _oldRise = ZombieEnemy.Rise;
            _oldStuckWindow = ZombieEnemy.StuckWindowOverride;
            _started = false;
            _bodies.Clear();
            _failures.Clear();
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            try
            {
                if (_started)
                {
                    foreach (Body b in _bodies)
                        if (b.Zombie != null && b.Zombie.LifeId == b.Life && b.Zombie.gameObject.activeSelf) b.Zombie.Remove();
                    if (_rig != null)
                    {
                        if (_rig.Motor != null) _rig.Motor.ControlEnabled = _oldControl;
                        if (_rig.Health != null) _rig.Health.DiagnosisInvulnerable = _oldInvulnerable;
                    }
                    RunFlowClock.ClearHolds();
                    Time.timeScale = 1f;
                    PrototypeUiFlow.GoToMainMenu();
                    yield return WaitFor(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu, 60f, "P4 cleanup");
                }
            }
            finally
            {
                if (_started)
                {
                    ZombieEnemy.Rise = _oldRise;
                    ZombieEnemy.StuckWindowOverride = _oldStuckWindow;
                    EnemyFactory.ResetSeedForTests();
                    CameraModeService.Request(_oldCamera, instant: true);
                    Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, _oldSource);
                    Environment.SetEnvironmentVariable(WorldSourceSettings.SceneEnvironmentVariable, _oldScene);
                    Time.captureDeltaTime = _oldCapture;
                    Time.timeScale = _oldScale;
                }
                _rig = null;
                _started = false;
                _bodies.Clear();
            }
        }

        [UnityTest, Category("WP096"), Category("Diagnostics"), Explicit(ExplicitReason), Timeout(240000)]
        public IEnumerator P4_B1_E0_DortTier_GercekKapiGecisi() => RunDoor(0);
        [UnityTest, Category("WP096"), Category("Diagnostics"), Explicit(ExplicitReason), Timeout(240000)]
        public IEnumerator P4_B2_W0_DortTier_GercekKapiGecisi() => RunDoor(1);
        [UnityTest, Category("WP096"), Category("Diagnostics"), Explicit(ExplicitReason), Timeout(240000)]
        public IEnumerator P4_B3_E0_DortTier_GercekKapiGecisi() => RunDoor(2);
        [UnityTest, Category("WP096"), Category("Diagnostics"), Explicit(ExplicitReason), Timeout(240000)]
        public IEnumerator P4_B4_E0_DortTier_GercekKapiGecisi() => RunDoor(3);
        [UnityTest, Category("WP096"), Category("Diagnostics"), Explicit(ExplicitReason), Timeout(240000)]
        public IEnumerator P4_B5_E0_DortTier_GercekKapiGecisi() => RunDoor(4);
        [UnityTest, Category("WP096"), Category("Diagnostics"), Explicit(ExplicitReason), Timeout(240000)]
        public IEnumerator P4_B6_E0_DortTier_GercekKapiGecisi() => RunDoor(5);
        [UnityTest, Category("WP096"), Category("Diagnostics"), Explicit(ExplicitReason), Timeout(240000)]
        public IEnumerator P4_K1_W0_DortTier_GercekKapiGecisi() => RunDoor(6);

        private IEnumerator Prepare()
        {
            _started = true;
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "V6");
            Environment.SetEnvironmentVariable(WorldSourceSettings.SceneEnvironmentVariable, WorldSourceSettings.V6SceneName);
            Assert.AreEqual(WorldSource.V6, WorldSourceSettings.Resolve(out string scene));
            Assert.AreEqual(WorldSourceSettings.V6SceneName, scene, "command-line world override");
            RunFlowClock.ClearHolds();
            Time.timeScale = 1f;
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return WaitFor(() => SceneManager.GetSceneByName(WorldSourceSettings.V6SceneName).isLoaded &&
                RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting &&
                EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null,
                60f, "P4 V6 Run/Fighting");
            Assert.AreEqual(WorldLayoutKind.V6, WorldLayout.Current);
            Assert.IsTrue(SpawnWorldProfile.IsV6);
            Assert.IsNull(RegionNavRouter.AlternativeQueryOverride, "test query cannot replace the actual NavMesh");
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;
            Assert.AreEqual(0, EnemyFactory.SpawnedCount, "başka gövdeler P4 ölçümünü kirletiyor");
            _rig = PlayerRegistry.Primary;
            Assert.IsNotNull(_rig.Motor);
            Assert.IsNotNull(_rig.Health);
            _oldControl = _rig.Motor.ControlEnabled;
            _oldInvulnerable = _rig.Health.DiagnosisInvulnerable;
            _rig.Motor.ControlEnabled = false;
            _rig.Health.DiagnosisInvulnerable = true;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            ZombieEnemy.StuckWindowOverride = 0f; // Shipped watchdog window, not a shortened test window.
            Time.captureDeltaTime = 1f / 60f;
        }

        private IEnumerator RunDoor(int index)
        {
            yield return Prepare();
            Door door = Doors[index];
            NavRegionVolume interior = AssertGeometry(door);
            Vector3 rawGoal = door.Plane + door.Inward * Inside;
            Vector3 goal = Project(rawGoal);
            Assert.GreaterOrEqual(Signed(door, goal), Inside - ProjectionRadius);
            Assert.IsTrue(interior.Contains(goal), "oyuncu hedefi aynı oda/koridorda olmalı");
            CharacterController player = _rig.Motor.GetComponent<CharacterController>();
            Assert.IsNotNull(player);
            AssertCapsuleClear(goal, player);
            _rig.Teleport(goal + Vector3.up * 0.05f, Quaternion.LookRotation(-door.Inward));
            yield return null;
            int rem0 = ZombieEnemy.StuckRemovals, recover0 = ZombieEnemy.StuckRecoveries, oow0 = ZombieEnemy.OutOfWorldRemovals;
            int queue0 = RegionNavRouter.QueueEvents, reroute0 = RegionNavRouter.RerouteCount;
            int holds = 0;
            EnemyFactory.ResetSeedForTests(FactorySeed);
            // No yield between spawns: every body's deadline uses the same t0.
            for (int i = 0; i < Tiers.Length; i++)
            {
                Vector3 raw = door.Plane - door.Inward * Outside + door.Tangent * Offsets[i];
                Vector3 at = Project(raw);
                Assert.LessOrEqual(Signed(door, at), -Outside + ProjectionRadius, "başlangıç kapının yanlış tarafında");
                Assert.IsFalse(interior.Contains(at), "başlangıç oda içinde olamaz");
                AssertCompletePath(at, goal);
                ZombieEnemy z = EnemyFactory.Spawn(Tiers[i], 5, at + Vector3.up * 0.05f, Quaternion.LookRotation(door.Inward));
                Assert.IsNotNull(z, "P4 gövdesi doğmadı: " + Tiers[i]);
                var body = new Body { Zombie = z, Capsule = z.GetComponent<CharacterController>(), Life = z.LifeId,
                    Name = TierLabel(Tiers[i]) + "#" + i, Last = z.transform.position };
                _bodies.Add(body); // Cleanup owns even a body whose next prerequisite fails.
                Assert.AreEqual(Tiers[i], z.Tier);
                Assert.IsNotNull(body.Capsule);
                Assert.IsTrue(body.Capsule.enabled && z.gameObject.activeInHierarchy);
                Assert.IsFalse(z.IsRising);
                Assert.Less(Vector3.Distance(z.transform.lossyScale, Vector3.one), 0.001f, "kapsül world ölçeği");
                Assert.AreSame(z.transform, body.Capsule.transform);
                AssertCapsuleClear(at, body.Capsule);
                if (z.Tier == EnemyTier.Ultra)
                {
                    Assert.IsNotNull(z.Skinned, "seçilen uzun varyant AP-025 skinned görünümü gerektirir");
                    Assert.GreaterOrEqual(body.Capsule.height, 2.80f, "doğal uzun Executioner varyantı doğmadı; resize/fallback yok");
                    Assert.AreEqual(0.9f, body.Capsule.radius, 0.001f, "Executioner fizik yarıçapı");
                }
                Debug.Log("[DD-TEST] wp096.p4.spawn gate=" + door.Id + " body=" + body.Name + " life=" + body.Life +
                    " raw=" + raw.ToString("F3") + " sampled=" + at.ToString("F3") + " radius=" + body.Capsule.radius +
                    " height=" + body.Capsule.height + " skin=" + body.Capsule.skinWidth +
                    " holdsChokepoints=" + (z.PathFollower != null && z.PathFollower.HoldsChokepoints));
            }
            Assert.AreEqual(5, _bodies.Count);
            for (int a = 0; a < _bodies.Count; a++)
            for (int c = a + 1; c < _bodies.Count; c++)
                Assert.Greater(Horizontal(_bodies[a].Last, _bodies[c].Last),
                    _bodies[a].Capsule.radius + _bodies[c].Capsule.radius + _bodies[a].Capsule.skinWidth + _bodies[c].Capsule.skinWidth,
                    "başlangıç kapsülleri üst üste");
            Debug.Log("[DD-TEST] wp096.p4.begin gate=" + door.Id + " plane=" + door.Plane.ToString("F3") +
                " authoredGate=" + door.AuthoredGate.ToString("F3") + " inward=" + door.Inward.ToString("F1") +
                " goal=" + goal.ToString("F3") + " window=30 deadlines=12/Exec6");
            float t0 = Time.time, wall0 = Time.realtimeSinceStartup;
            while (Time.time - t0 < Window)
            {
                yield return null;
                float elapsed = Time.time - t0;
                Assert.Less(Time.realtimeSinceStartup - wall0, 60f, "30s ortak pencere wall timeout; pause/freeze");
                Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
                Assert.IsNotNull(_rig, "oyuncu kayboldu");
                if (Horizontal(_rig.transform.position, goal) > 0.1f ||
                    Mathf.Abs(_rig.transform.position.y - goal.y) > FloorTolerance)
                {
                    // Only the stationary TARGET is held; no zombie is teleported, nudged or re-spawned.
                    _rig.Teleport(goal + Vector3.up * 0.05f, _rig.transform.rotation);
                    holds++;
                }
                foreach (Body body in _bodies) Observe(door, interior, body, elapsed);
            }
            int reached = 0, jambStalls = 0;
            var report = new StringBuilder("[DD-TEST] wp096.p4.end gate=" + door.Id + " seconds=" + (Time.time - t0).ToString("F3"));
            foreach (Body body in _bodies)
            {
                if (body.LongestSlow > JambSeconds) jambStalls++;
                if (body.InsideAt >= 0f) reached++;
                else Fail(body, "30s pencere sonunda gerçek iç geçiş yok");
                report.Append("\n ").Append(body.Name).Append(" crossedAt=").Append(body.CrossedAt.ToString("F3"))
                    .Append(" cross=").Append(body.Crossing.ToString("F3")).Append(" insideAt=").Append(body.InsideAt.ToString("F3"))
                    .Append(" maxJambSlow=").Append(body.LongestSlow.ToString("F3")).Append(" failed=").Append(body.Failed);
            }
            int queue = RegionNavRouter.QueueEvents - queue0, reroute = RegionNavRouter.RerouteCount - reroute0;
            report.Append("\n reached=").Append(reached).Append("/5 queue=").Append(queue).Append(" reroute=").Append(reroute)
                .Append(" stuckRemovals=").Append(ZombieEnemy.StuckRemovals - rem0)
                .Append(" recoveries=").Append(ZombieEnemy.StuckRecoveries - recover0)
                .Append(" outOfWorld=").Append(ZombieEnemy.OutOfWorldRemovals - oow0).Append(" targetHolds=").Append(holds).Append(" jambStalls=").Append(jambStalls);
            Debug.Log(report.ToString());
            Assert.AreEqual(rem0, ZombieEnemy.StuckRemovals, "watchdog removal");
            Assert.AreEqual(recover0, ZombieEnemy.StuckRecoveries, "recovery/nudge gerçek kapı geçişinin yerine geçemez");
            Assert.AreEqual(oow0, ZombieEnemy.OutOfWorldRemovals);
            Assert.Greater(queue + reroute, 0, "kuyruk/reroute gözlemi yok");
            Assert.AreEqual(0, jambStalls, "pervazda >3s hız<0.2m/s gövde");
            Assert.IsEmpty(_failures, string.Join("\n", _failures));
            Assert.AreEqual(5, reached, "hiçbir tier/gövde atlanamaz");
        }

        private void Observe(Door door, NavRegionVolume interior, Body body, float elapsed)
        {
            ZombieEnemy z = body.Zombie;
            if (z == null || !z.IsAlive || !z.gameObject.activeInHierarchy || z.LifeId != body.Life ||
                body.Capsule == null || !body.Capsule.enabled)
            { Fail(body, "gövde kayboldu/öldü/pooled-life değişti"); return; }
            Vector3 at = body.Capsule.transform.TransformPoint(body.Capsule.center) - Vector3.up * (body.Capsule.height * 0.5f);
            float dt = elapsed - body.LastTime;
            if (dt <= 0f) { Fail(body, "pozitif örnek aralığı yok"); return; }
            float moved = Horizontal(at, body.Last);
            if (moved > PhysicalStepLimit) Fail(body, "bir karede fizik dışı sıçrama=" + moved.ToString("F3"));
            float before = Signed(door, body.Last), now = Signed(door, at);
            float clearance = body.Capsule.radius + body.Capsule.skinWidth;
            if (body.CrossedAt < 0f && before <= 0f && now > 0f)
            {
                if (CrossesAperture(body.Last, at, door.Plane, door.Inward, clearance, out Vector3 crossing))
                {
                    body.Crossing = crossing;
                    body.CrossedAt = ThresholdTime(before, now, body.LastTime, elapsed, 0f);
                    if (Mathf.Abs(crossing.y - door.Plane.y) > FloorTolerance) Fail(body, "kapı yanlış yükseklikte geçildi");
                }
                else Fail(body, "2m gerçek kapı açıklığı dışından geçiş/açık-uç dolaşması");
            }
            bool fullInside = FullyInside(body.CrossedAt >= 0f, now, clearance, interior.Contains(at),
                Mathf.Abs(at.y - door.Plane.y) <= FloorTolerance && body.Capsule.isGrounded);
            if (body.InsideAt < 0f && fullInside)
            {
                body.InsideAt = before < clearance ? ThresholdTime(before, now, body.LastTime, elapsed, clearance) : elapsed;
                float deadline = z.Tier == EnemyTier.Ultra ? ExecutionerDeadline : BodyDeadline;
                if (body.InsideAt > deadline) Fail(body, "geçiş süresi " + body.InsideAt.ToString("F3") + " > " + deadline);
            }
            if (body.InsideAt < 0f && elapsed > (z.Tier == EnemyTier.Ultra ? ExecutionerDeadline : BodyDeadline))
                Fail(body, "ortak t0'dan geçiş deadline aşıldı");
            if (body.CrossedAt < 0f && interior.Contains(at) && now > clearance)
                Fail(body, "kapı-crossing olmadan oda/koridor içine varış");
            // Real displacement, no queue / waiting / attacking exemption: those were the old watchdog's blind spot.
            Vector3 jambA = door.Plane + door.Tangent * (DoorWidth * 0.5f);
            Vector3 jambB = door.Plane - door.Tangent * (DoorWidth * 0.5f);
            bool nearJamb = Mathf.Min(Horizontal(at, jambA), Horizontal(at, jambB)) <= body.Capsule.radius + 0.25f;
            bool slow = nearJamb && moved / dt < SlowSpeed;
            body.SlowFor = slow ? body.SlowFor + dt : 0f;
            body.LongestSlow = Mathf.Max(body.LongestSlow, body.SlowFor);
            if (body.SlowFor > JambSeconds) Fail(body, "pervazda >3s hız<0.2m/s; queued=" + (z.PathFollower != null && z.PathFollower.IsQueued));
            if (at.y < -1f) Fail(body, "düştü");
            body.Last = at;
            body.LastTime = elapsed;
        }

        private static NavRegionVolume AssertGeometry(Door door)
        {
            var volumes = new Dictionary<string, NavRegionVolume>(StringComparer.Ordinal);
            foreach (NavRegionMarker marker in UnityEngine.Object.FindObjectsOfType<NavRegionMarker>())
            {
                Assert.AreEqual(WorldSourceSettings.V6SceneName, marker.gameObject.scene.name);
                NavRegionVolume v = marker.Volume;
                Assert.IsNotNull(v);
                Assert.IsFalse(string.IsNullOrEmpty(v.id));
                Assert.IsFalse(volumes.ContainsKey(v.id), "duplicate volume " + v.id);
                volumes.Add(v.id, v);
            }
            Assert.IsTrue(volumes.TryGetValue(door.Id, out NavRegionVolume gate), "world merge eksik: " + door.Id);
            Assert.IsTrue(volumes.TryGetValue(door.InteriorId, out NavRegionVolume interior), "interior eksik");
            Assert.AreEqual(NavRegionKind.Gate, gate.kind);
            Assert.AreEqual(door.InteriorId, gate.roomId);
            Assert.AreEqual(door.InteriorId == "K1_WestKairo" ? NavRegionKind.Corridor : NavRegionKind.Room, interior.kind);
            Assert.LessOrEqual(Vector3.Distance(gate.center, door.AuthoredGate + Vector3.up * 1.2f), 0.01f, "gate geometry changed");
            Assert.LessOrEqual(Vector3.Distance(gate.size, new Vector3(1.6f, 2.4f, DoorWidth)), 0.01f);
            Assert.AreEqual(2, gate.capacity);
            Assert.AreEqual(1.15f, gate.costMultiplier, 0.001f);
            Assert.IsTrue(gate.Contains(door.Plane), "physical wall line outside gate");
            return interior;
        }

        private static Vector3 Project(Vector3 raw)
        {
            Assert.IsTrue(NavMesh.SamplePosition(raw, out NavMeshHit hit, ProjectionRadius, NavMesh.AllAreas), "NavMesh yok @" + raw.ToString("F3"));
            Assert.LessOrEqual(Horizontal(raw, hit.position), ProjectionRadius, "projection kayması");
            Assert.LessOrEqual(Mathf.Abs(raw.y - hit.position.y), FloorTolerance, "projection yanlış kat");
            return hit.position;
        }

        private static void AssertCapsuleClear(Vector3 feet, CharacterController capsule)
        {
            int mask = LayerMask.GetMask("Environment", "CameraOccluder");
            Assert.AreNotEqual(0, LayerMask.GetMask("Environment"));
            Assert.AreNotEqual(0, LayerMask.GetMask("CameraOccluder"));
            Assert.AreEqual(0, mask & (1 << capsule.gameObject.layer), "query own capsule layer");
            float lift = capsule.skinWidth + 0.02f;
            Vector3 bottom = feet + Vector3.up * (capsule.radius + lift);
            Vector3 top = feet + Vector3.up * (Mathf.Max(capsule.radius, capsule.height - capsule.radius) + lift);
            Assert.IsFalse(Physics.CheckCapsule(bottom, top, capsule.radius, mask, QueryTriggerInteraction.Ignore),
                "fixture endpoint kapsülü solid içinde; skip/fallback yok @" + feet.ToString("F3"));
        }

        private static void AssertCompletePath(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            Assert.IsTrue(NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path));
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status);
            Vector3[] corners = path.corners;
            Assert.GreaterOrEqual(corners.Length, 2);
            Assert.LessOrEqual(Vector3.Distance(corners[corners.Length - 1], to), 0.25f);
        }

        private void Fail(Body body, string why)
        {
            if (body.Failed) return;
            body.Failed = true;
            _failures.Add(body.Name + ": " + why);
        }

        // Public enum names: Special=Runner, Elite=Guard, Ultra=Executioner (PrototypeConfigEnums).
        private static string TierLabel(EnemyTier tier)
        {
            switch (tier)
            {
                case EnemyTier.Special: return "Runner";
                case EnemyTier.Elite: return "Guard";
                case EnemyTier.Ultra: return "Executioner";
                default: return "Standard";
            }
        }

        private static float Signed(Door door, Vector3 at) => Vector3.Dot(at - door.Plane, door.Inward);
        private static float Horizontal(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
        private static float ThresholdTime(float before, float after, float t0, float t1, float threshold) =>
            Mathf.Lerp(t0, t1, Mathf.Clamp01((threshold - before) / Mathf.Max(1e-6f, after - before)));

        private static bool CrossesAperture(Vector3 from, Vector3 to, Vector3 plane, Vector3 inward,
            float clearance, out Vector3 crossing)
        {
            crossing = Vector3.zero;
            float a = Vector3.Dot(from - plane, inward), b = Vector3.Dot(to - plane, inward);
            if (a > 0f || b <= 0f || b <= a || clearance > DoorWidth * 0.5f) return false;
            crossing = Vector3.Lerp(from, to, -a / (b - a));
            Vector3 tangent = Vector3.Cross(Vector3.up, inward);
            return Mathf.Abs(Vector3.Dot(crossing - plane, tangent)) + clearance <= DoorWidth * 0.5f;
        }

        private static bool FullyInside(bool crossed, float signed, float clearance, bool sameInterior, bool floor) =>
            crossed && signed >= clearance && sameInterior && floor;

        private static IEnumerator WaitFor(Func<bool> predicate, float seconds, string name)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!predicate() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
            Assert.IsTrue(predicate(), name + " timeout");
        }

        [Test, Category("WP096")]
        public void P4_KabulKorumasi_AcikUctanDolasmakTersYonVeYarimGovdeReddedilir()
        {
            Vector3 from = Vector3.left, to = Vector3.right;
            Assert.IsTrue(CrossesAperture(from, to, Vector3.zero, Vector3.right, 0.93f, out _));
            Assert.IsFalse(CrossesAperture(from + Vector3.forward * 0.2f, to + Vector3.forward * 0.2f,
                Vector3.zero, Vector3.right, 0.93f, out _));
            Assert.IsFalse(CrossesAperture(to, from, Vector3.zero, Vector3.right, 0.48f, out _));
            Assert.IsFalse(CrossesAperture(from + Vector3.forward * 4f, to + Vector3.forward * 4f,
                Vector3.zero, Vector3.right, 0.48f, out _));
            Assert.IsTrue(FullyInside(true, 1f, 0.93f, true, true));
            Assert.IsFalse(FullyInside(false, 3f, 0.93f, true, true));
            Assert.IsFalse(FullyInside(true, 0.5f, 0.93f, true, true));
            Assert.IsFalse(FullyInside(true, 1f, 0.93f, false, true));
            Assert.IsFalse(FullyInside(true, 1f, 0.93f, true, false));
        }
    }
}
