using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-038M · On the real Run over World_AP005 (soak seed-61 fall, 611–622 s: out through the south gate, off the band at
    /// z ≈ −80): the player walked at every edge — the south gate the bot used included — stops on the ±45 m line and never
    /// drops, nor does one walked into a koi pond (the ground has a hole there); a player below the world is put back on the
    /// NavMesh by the fall guard, unharmed. WP-038N: every NavMesh path between the regions (and over the south bridge, and the
    /// soak seed-61 centre → east-gate stall) is physically walkable by a body with the player's capsule and 0.35 m step.
    /// </summary>
    public sealed class WorldBoundaryPlayTests
    {
        private const float SahneZamanAsimi = 120f, KurulumZamanAsimi = 60f;
        private const float Sinir = 45f;

        private sealed class Girdi : IInputSource
        {
            public Vector2 Deger;
            public bool IsEnabled => true;
            public Vector2 Move => Deger;
            public AimSourceKind AimKind => AimSourceKind.Pointer;
            public Vector2 AimScreenPosition => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            public Vector2 AimStick => Vector2.zero;
            public bool FireHeld => false;
            public bool FirePressed => false;
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

        private PlayerRig _rig;
        private Girdi _girdi;

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_rig != null && _rig.Motor != null) _rig.Motor.SetInputSource(null);
            if (_rig != null && _rig.Health != null) _rig.Health.DiagnosisInvulnerable = false;
            _rig = null;
            _girdi = null;
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            }
        }

        [TearDown]
        public void TearDown() { LogAssert.ignoreFailingMessages = false; }

        private IEnumerator RunAp005()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, SahneZamanAsimi, "World_AP005");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, KurulumZamanAsimi, "oyuncu/kamera");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            // Only the player and the world: the crowd would push the capsule and blur the edge reading.
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            Assert.IsNotNull(UnityEngine.Object.FindObjectOfType<WorldFallGuard>(), "World_AP005'te düşme güvenlik ağı yok");
            yield return null;
            Time.captureDeltaTime = 1f / 60f;
            _girdi = new Girdi();
            _rig.Motor.SetInputSource(_girdi);
        }

        private static Vector3 Zemin(float x, float z)
        {
            return NavMesh.SamplePosition(new Vector3(x, 0f, z), out NavMeshHit hit, 3f, NavMesh.AllAreas) ? hit.position + Vector3.up * 0.1f : new Vector3(x, 0.1f, z);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_KenaraYurur_SinirdaDurur_Dusmez()
        {
            yield return RunAp005();
            // (start x, start z, outward direction, must reach the line) — south: the gate the soak bot left by (x ≈ −1…−13), plus
            // two more south points; then one per remaining edge. Starts 4 m inside the line.
            // Last: straight at the centre of south pond 02 (−8.4, −29; WorldV4Tests) from 7.5 m north — the rim must hold.
            var probes = new (float x, float z, Vector3 dir, bool gate)[]
            {
                (-7f, -41f, Vector3.back, true), (-30f, -41f, Vector3.back, false), (20f, -41f, Vector3.back, false),
                (0f, 41f, Vector3.forward, false), (41f, 0f, Vector3.right, false), (-41f, 0f, Vector3.left, false),
                (-8.4f, -21.5f, Vector3.back, false),
            };
            int recoveries0 = WorldFallGuard.PlayerRecoveries;
            var rows = new List<string>();
            foreach (var pr in probes)
            {
                _girdi.Deger = Vector2.zero;
                _rig.Teleport(Zemin(pr.x, pr.z), Quaternion.LookRotation(pr.dir));
                yield return null;
                yield return null;
                float maxOut = float.MinValue, minY = float.MaxValue;
                for (int f = 0; f < 180; f++)   // 3 s at 6.2 m/s: 4 m to the line, then 2+ s pushing on it
                {
                    PrototypeCameraRig cam = PrototypeCameraRig.Active;
                    _girdi.Deger = new Vector2(Vector3.Dot(pr.dir, cam.PlanarRight), Vector3.Dot(pr.dir, cam.PlanarForward));
                    yield return null;
                    Vector3 p = _rig.transform.position;
                    maxOut = Mathf.Max(maxOut, Vector3.Dot(p, pr.dir));
                    minY = Mathf.Min(minY, p.y);
                }

                _girdi.Deger = Vector2.zero;
                rows.Add(pr.dir + " @(" + pr.x + "," + pr.z + ") maxOut=" + maxOut.ToString("F2") + " minY=" + minY.ToString("F2"));
                Assert.LessOrEqual(maxOut, Sinir, "oyuncu ±45 m çizgisini geçti: " + rows[rows.Count - 1]);
                Assert.Greater(minY, -1f, "oyuncu düştü: " + rows[rows.Count - 1]);
                if (pr.gate) Assert.GreaterOrEqual(maxOut, Sinir - 1.2f, "güney kapısında çizgiye varılmadı (yol kapalı mı?): " + rows[rows.Count - 1]);
            }

            Debug.Log("[DD-TEST] wp038m.edges " + string.Join(" | ", rows));
            Assert.AreEqual(recoveries0, WorldFallGuard.PlayerRecoveries, "kenar yürüyüşünde güvenlik ağı devreye girmemeli (halka tutmalı)");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_DunyaninAltinda_NavMeshUzerineGeriKonur_HasarYok()
        {
            yield return RunAp005();
            _rig.Health.DiagnosisInvulnerable = false;
            float health0 = _rig.Health.Current;
            // Under the ground inside the square, and far out past the south band (where the soak bot fell).
            foreach (Vector3 drop in new[] { new Vector3(5f, -6f, 5f), new Vector3(-7f, -12f, -80f) })
            {
                int r0 = WorldFallGuard.PlayerRecoveries;
                _rig.Teleport(drop, Quaternion.identity);
                yield return CharacterTests.Bekle(() => WorldFallGuard.PlayerRecoveries > r0 && _rig.transform.position.y > WorldFallGuard.FallY, 5f, "güvenlik ağı oyuncuyu geri koymadı: " + drop);
                for (int i = 0; i < 10; i++) yield return null;
                Vector3 p = _rig.transform.position;
                Assert.AreEqual(r0 + 1, WorldFallGuard.PlayerRecoveries, "tek kurtarma: " + drop);
                Assert.Greater(p.y, -1f, "geri konan oyuncu zeminde: " + p);
                Assert.LessOrEqual(Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)), Sinir, "geri konum oynanabilir alanda: " + p);
                Assert.IsTrue(NavMesh.SamplePosition(p, out _, 1.5f, NavMesh.AllAreas), "geri konum NavMesh üzerinde: " + p);
                Debug.Log("[DD-TEST] wp038m.recover " + drop + " -> " + p);
            }

            Assert.AreEqual(health0, _rig.Health.Current, 0.001f, "güvenlik ağı hasar vermez");
        }

        // WP-038N · Region anchors (on the NavMesh within 3 m): centre (run start), north, east, the soak seed-61 last zombie
        // (east-north), south-east, the south bank, west, north-west.
        private static readonly (string name, Vector3 p)[] Anchors =
        {
            ("merkez", new Vector3(0f, 0f, -3f)), ("kuzey", new Vector3(0f, 0f, 30f)), ("dogu", new Vector3(30f, 0f, 0f)),
            ("dogu-kuzey", new Vector3(24.5f, 0f, 16.2f)), ("guney-dogu", new Vector3(30f, 0f, -30f)), ("guney-kiyi", new Vector3(4f, 0f, -40f)),
            ("bati", new Vector3(-30f, 0f, 0f)), ("bati-kuzey", new Vector3(-30f, 0f, 30f)),
        };

        private sealed class Walker
        {
            public string Name;
            public CharacterController Body;
            public Vector3 Goal;
            public NavMeshPath Path = new NavMeshPath();
            public int Corner;
            public float VerticalSpeed, NextRepath, Best = float.MaxValue, LastImprove, Limit;
            public bool Done;
            public string Fail;
        }

        private static float Remaining(Vector3 from, NavMeshPath path, int corner)
        {
            if (path.corners.Length == 0) return float.MaxValue;
            float sum = 0f;
            Vector3 prev = from;
            for (int i = Mathf.Max(1, corner); i < path.corners.Length; i++) { sum += Vector3.Distance(Flat(prev), Flat(path.corners[i])); prev = path.corners[i]; }
            return sum;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator NavMeshYollari_BolgelerArasiVeKopru_BedenKapsuluyleFizikselGecilir()
        {
            yield return RunAp005();
            _rig.Motor.SetInputSource(null);
            var pairs = new List<(string name, Vector3 a, Vector3 b)>();
            for (int i = 0; i < Anchors.Length; i++)
            for (int j = 0; j < Anchors.Length; j++)
                if (i != j) pairs.Add((Anchors[i].name + "→" + Anchors[j].name, Anchors[i].p, Anchors[j].p));
            pairs.Add(("soak61 (9.6,4.7)→dogu-kuzey", new Vector3(9.6f, 0f, 4.7f), new Vector3(24.5f, 0f, 16.2f)));
            pairs.Add(("soak61 (12.2,2.9)→dogu-kuzey", new Vector3(12.2f, 0f, 2.9f), new Vector3(24.5f, 0f, 16.2f)));
            pairs.Add(("kopru K→G", new Vector3(4f, 0f, -22.5f), new Vector3(4f, 0f, -36f)));
            pairs.Add(("kopru G→K", new Vector3(4f, 0f, -36f), new Vector3(4f, 0f, -22.5f)));

            var walkers = new List<Walker>();
            var partial = new List<string>();
            CharacterController player = _rig.GetComponent<CharacterController>();
            foreach (var pr in pairs)
            {
                Assert.IsTrue(NavMesh.SamplePosition(pr.a, out NavMeshHit ha, 3f, NavMesh.AllAreas), pr.name + ": başlangıç NavMesh dışı");
                Assert.IsTrue(NavMesh.SamplePosition(pr.b, out NavMeshHit hb, 3f, NavMesh.AllAreas), pr.name + ": hedef NavMesh dışı");
                var w = new Walker { Name = pr.name, Goal = hb.position };
                NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, w.Path);
                if (w.Path.status != NavMeshPathStatus.PathComplete) { partial.Add(pr.name + " " + w.Path.status); continue; }
                var go = new GameObject("WP-038N walker " + pr.name) { layer = PrototypeLayers.Player };
                go.transform.position = ha.position + Vector3.up * 0.05f;
                CharacterController cc = go.AddComponent<CharacterController>();
                cc.radius = 0.45f; cc.height = 1.8f; cc.center = new Vector3(0f, 0.9f, 0f);
                cc.stepOffset = 0.35f; cc.slopeLimit = 45f; cc.skinWidth = 0.03f; cc.minMoveDistance = 0f;
                if (player != null) Physics.IgnoreCollision(cc, player);
                foreach (Walker other in walkers) Physics.IgnoreCollision(cc, other.Body);
                w.Body = cc;
                w.Limit = Remaining(ha.position, w.Path, 1) / 6.2f * 1.6f + 6f;
                walkers.Add(w);
            }

            Assert.IsEmpty(partial, "NavMesh bölgeler arası tam yol vermiyor: " + string.Join(", ", partial));
            float t = 0f;
            int active = walkers.Count;
            while (active > 0 && t < 60f)
            {
                if (EnemySpawnDirector.Instance != null && EnemySpawnDirector.Instance.ActiveCount > 0) EnemySpawnDirector.Instance.ClearAllEnemies();
                yield return null;
                float dt = Time.deltaTime;
                t += dt;
                active = 0;
                foreach (Walker w in walkers)
                {
                    if (w.Done || w.Fail != null) continue;
                    active++;
                    Vector3 pos = w.Body.transform.position;
                    if (Vector3.Distance(Flat(pos), Flat(w.Goal)) < 0.7f) { w.Done = true; continue; }
                    if (pos.y < -2f) { w.Fail = "düştü @" + pos.ToString("F1"); continue; }
                    if (t >= w.NextRepath)
                    {
                        w.NextRepath = t + 0.5f;
                        if (NavMesh.SamplePosition(pos, out NavMeshHit here, 1.5f, NavMesh.AllAreas)) NavMesh.CalculatePath(here.position, w.Goal, NavMesh.AllAreas, w.Path);
                        w.Corner = 1;
                        float rem = Remaining(pos, w.Path, 1);
                        if (rem < w.Best - 0.5f) { w.Best = rem; w.LastImprove = t; }
                        else if (t - w.LastImprove > 3f) { w.Fail = "takıldı @" + pos.ToString("F2") + " kalan " + rem.ToString("F1") + " m"; continue; }
                    }

                    if (t > w.Limit) { w.Fail = "süre aşımı @" + pos.ToString("F2"); continue; }
                    Vector3[] corners = w.Path.corners;
                    while (w.Corner < corners.Length - 1 && Vector3.Distance(Flat(pos), Flat(corners[w.Corner])) < 0.3f) w.Corner++;
                    Vector3 target = corners.Length > 0 ? corners[Mathf.Min(w.Corner, corners.Length - 1)] : w.Goal;
                    Vector3 dir = Flat(target - pos);
                    dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.zero;
                    w.VerticalSpeed = w.Body.isGrounded ? -2f : w.VerticalSpeed - 24f * dt;
                    w.Body.Move(dir * 6.2f * dt + Vector3.up * (w.VerticalSpeed * dt));
                }
            }

            var fails = new List<string>();
            int done = 0;
            foreach (Walker w in walkers)
            {
                if (w.Done) done++;
                else fails.Add(w.Name + ": " + (w.Fail ?? "bitmedi @" + w.Body.transform.position.ToString("F1")));
                UnityEngine.Object.Destroy(w.Body.gameObject);
            }

            Debug.Log("[DD-TEST] wp038n.walk pairs=" + walkers.Count + " done=" + done + " simSeconds=" + t.ToString("F1") + (fails.Count > 0 ? " FAIL: " + string.Join(" | ", fails) : ""));
            Assert.IsEmpty(fails, "NavMesh yolu fiziksel olarak geçilemiyor (" + fails.Count + "):\n" + string.Join("\n", fails));
        }
    }
}
