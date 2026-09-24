using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Enemies;
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

        // WP-038P · Zombies where the soak lost one (beside the south bridge-end post, now skirted) and inside every pocket the
        // bake's erosion used to cut off (now linked: NavMeshIslandLinker) walk out to the player. WP-038R: + the body-walk pockets
        // and the west ring spot of the S-10 stability soak seed 26.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zombiler_KopruUcuVeCepAdaciklarindan_OyuncuyaUlasir()
        {
            yield return RunAp005();
            _rig.Motor.SetInputSource(null);
            var target = new Vector3(-7.5f, 0.05f, 0f);
            _rig.Teleport(target, Quaternion.identity);
            var spots = new[]
            {
                new Vector3(3.2f, 0.1f, -36.6f), new Vector3(-37.63f, 0.05f, 17.0f), new Vector3(-21.08f, 0.05f, 35.33f), new Vector3(7.93f, 0.15f, -32.23f),
                new Vector3(-1.53f, 0.05f, -24.36f), new Vector3(-41.85f, 0.05f, -19.83f), new Vector3(-15.73f, 0.05f, 40.92f),
                // WP-038R: the pockets a body walks into (linked since the body-walk criterion) and the west ring where stab26 lost the last bodies
                new Vector3(-40.5f, 0.05f, -15.8f), new Vector3(-16.75f, 0.1f, 36.2f), new Vector3(-43.9f, 0.05f, 0.1f),
            };
            var zombies = new List<(Vector3 spot, ZombieEnemy z)>();
            foreach (Vector3 spot in spots)
            {
                Vector3 p = NavMesh.SamplePosition(spot, out NavMeshHit hit, 0.8f, NavMesh.AllAreas) ? hit.position + Vector3.up * 0.05f : spot;
                zombies.Add((spot, EnemyFactory.Spawn(EnemyTier.Standard, 5, p, Quaternion.identity)));
            }

            var reached = new HashSet<int>();
            float t = 0f;
            while (reached.Count < zombies.Count && t < 90f)
            {
                _rig.Teleport(target, Quaternion.identity);
                yield return null;
                t += Time.deltaTime;
                for (int i = 0; i < zombies.Count; i++)
                {
                    ZombieEnemy z = zombies[i].z;
                    if (z == null || !z.IsAlive || reached.Contains(i)) continue;
                    Vector3 d = z.transform.position - target; d.y = 0f;
                    if (d.magnitude < 3f) reached.Add(i);
                }
            }

            var stuck = new List<string>();
            for (int i = 0; i < zombies.Count; i++)
                if (!reached.Contains(i)) stuck.Add(zombies[i].spot.ToString("F1") + " → " + (zombies[i].z != null ? zombies[i].z.transform.position.ToString("F2") : "yok"));
            Debug.Log("[DD-TEST] wp038p.zombies reached " + reached.Count + "/" + zombies.Count + " in " + t.ToString("F1") + " s" + (stuck.Count > 0 ? " stuck: " + string.Join(" | ", stuck) : ""));
            Assert.IsEmpty(stuck, "oyuncuya ulaşamayan zombi (" + stuck.Count + "): " + string.Join(" | ", stuck));
        }

        /// <summary>
        /// WP-038Q/R · Drives the real player body (PlayerMotor, 0.35 m step) along each route like the soak bot — a NavMesh path
        /// re-planned every 0.25 s, steering at the next corner — and adds "name: reason" to <paramref name="fails"/> when it
        /// finds no path, makes no 0.5 m progress in 3 s, runs past 60 s or falls.
        /// </summary>
        private IEnumerator WalkRoutes(List<(string name, Vector3 from, Vector3 to)> routes, List<string> fails)
        {
            var path = new NavMeshPath();
            foreach ((string name, Vector3 from, Vector3 to) in routes)
            {
                Assert.IsTrue(NavMesh.SamplePosition(from, out NavMeshHit hs, 1.5f, NavMesh.AllAreas), name + ": başlangıç NavMesh dışı");
                Assert.IsTrue(NavMesh.SamplePosition(to, out NavMeshHit ht, 1.5f, NavMesh.AllAreas), name + ": hedef NavMesh dışı");
                _girdi.Deger = Vector2.zero;
                _rig.Teleport(hs.position + Vector3.up * 0.05f, Quaternion.identity);
                yield return null;
                float t = 0f, next = 0f, best = float.MaxValue, lastImprove = 0f;
                string fail = null;
                Vector3 corner = ht.position;
                while (true)
                {
                    Vector3 pos = _rig.transform.position;
                    if (Vector3.Distance(Flat(pos), Flat(ht.position)) < 0.8f) break;
                    if (t >= next)
                    {
                        next = t + 0.25f;
                        Vector3 start = NavMesh.SamplePosition(pos, out NavMeshHit hp, 1.5f, NavMesh.AllAreas) ? hp.position : pos;
                        if (!NavMesh.CalculatePath(start, ht.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) { fail = "yol yok @" + pos.ToString("F2"); break; }
                        corner = ht.position;
                        for (int c = 1; c < path.corners.Length; c++) if (Vector3.Distance(Flat(pos), Flat(path.corners[c])) > 0.35f) { corner = path.corners[c]; break; }
                        float rem = Remaining(pos, path, 1);
                        if (rem < best - 0.5f) { best = rem; lastImprove = t; }
                        else if (t - lastImprove > 3f) { fail = "takıldı @" + pos.ToString("F2") + " kalan " + rem.ToString("F1") + " m"; break; }
                    }

                    if (t > 60f) { fail = "süre aşımı @" + pos.ToString("F2"); break; }
                    Vector3 dir = Flat(corner - pos);
                    dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.zero;
                    PrototypeCameraRig cam = PrototypeCameraRig.Active;
                    _girdi.Deger = new Vector2(Vector3.Dot(dir, cam.PlanarRight), Vector3.Dot(dir, cam.PlanarForward));
                    yield return null;
                    t += Time.deltaTime;
                    if (_rig.transform.position.y < -1f) { fail = "düştü"; break; }
                }

                _girdi.Deger = Vector2.zero;
                if (fail != null) fails.Add(name + ": " + fail);
            }

        }

        // WP-038Q · The raised south crossing (the bridge) with the real player body (PlayerMotor: acceleration, braking, the
        // 0.35 m step) driven like the soak bot along a NavMesh path re-planned every 0.25 s: the soak seed-61 run-25 route and
        // stuck point (4.0, 0.3, −31.3), and both ends entered from eight directions to the far bank. No route may stall 3 s.
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Oyuncu_GuneyGecidi_SekizYondenGirisCikis_VeSoakNoktasi_Takilmaz()
        {
            yield return RunAp005();
            var routes = new List<(string name, Vector3 from, Vector3 to)>
            {
                ("soak61-25", new Vector3(7.2f, 0f, -38.8f), new Vector3(-18f, 0f, -11.6f)),
                ("stuck→N", new Vector3(4f, 0.3f, -31.3f), new Vector3(-18f, 0f, -11.6f)),
                ("stuck→S", new Vector3(4f, 0.3f, -31.3f), new Vector3(7.2f, 0f, -38.8f)),
            };
            foreach ((string end, Vector3 centre, Vector3 far) in new[] { ("S", new Vector3(4f, 0f, -38.6f), new Vector3(4f, 0f, -20.4f)), ("N", new Vector3(4f, 0f, -20.9f), new Vector3(4f, 0f, -40f)) })
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                Vector3 p = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3.5f;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;   // a point in the pond
                routes.Add((end + (k * 45) + "°", hit.position, far));
            }

            var fails = new List<string>();
            yield return WalkRoutes(routes, fails);
            Debug.Log("[DD-TEST] wp038q.crossing routes=" + routes.Count + " stalls=" + fails.Count + (fails.Count > 0 ? " " + string.Join(" | ", fails) : ""));
            Assert.Greater(routes.Count, 12, "rota sayısı (8 yön × 2 uç + soak)");
            Assert.IsEmpty(fails, "Güney geçidinde oyuncu gövdesi takıldı (" + fails.Count + "):\n" + string.Join("\n", fails));
        }

        // WP-038Q · On the deck the player slides along the skirts whatever the stick says: pushed straight, 45° and 70° off the
        // deck axis, both ways, from the pinch the pond fence made, the segment joints and the soak's stuck point — never 0 m.
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Oyuncu_KopruGuvertesinde_EgikGirdiyle_Kayar_Takilmaz()
        {
            yield return RunAp005();
            var stuck = new List<string>();
            int cases = 0;
            foreach (float z0 in new[] { -35.0f, -33.6f, -32.6f, -31.3f, -29.0f, -25.9f, -24.2f })
            foreach (float heading in new[] { 0f, 180f })
            foreach (float off in new[] { 0f, 45f, -45f, 70f, -70f })
            {
                _girdi.Deger = Vector2.zero;
                _rig.Teleport(new Vector3(4f, 0.35f, z0), Quaternion.identity);
                for (int i = 0; i < 3; i++) yield return null;
                Vector3 p0 = _rig.transform.position;
                float a = (heading + off) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                for (int f = 0; f < 120; f++)
                {
                    PrototypeCameraRig cam = PrototypeCameraRig.Active;
                    _girdi.Deger = new Vector2(Vector3.Dot(dir, cam.PlanarRight), Vector3.Dot(dir, cam.PlanarForward));
                    yield return null;
                }

                cases++;
                float dz = Mathf.Abs(_rig.transform.position.z - p0.z);
                if (dz < 0.8f) stuck.Add("z0 " + z0 + " yön " + (heading + off) + "° → " + dz.ToString("F2") + " m @" + _rig.transform.position.ToString("F2"));
            }

            _girdi.Deger = Vector2.zero;
            Debug.Log("[DD-TEST] wp038q.deck cases=" + cases + " stuck=" + stuck.Count + (stuck.Count > 0 ? " " + string.Join(" | ", stuck) : ""));
            Assert.IsEmpty(stuck, "güvertede eğik girdiyle duran oyuncu (" + stuck.Count + "):\n" + string.Join("\n", stuck));
        }

        // WP-038R · The S-10 stability soak seed 26 round 12: the player in the bamboo pocket west of the corridor (−39.3, −16.5)
        // — a body walks in and out, the NavMesh did not join it — and the last zombies could not reach it for 300 s. A zombie from
        // the north reaches the player standing there through the new link.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zombiler_BatiBambuCebindekiOyuncuya_Ulasir()
        {
            yield return RunAp005();
            _rig.Motor.SetInputSource(null);
            var target = new Vector3(-39.3f, 0.05f, -16.5f);
            var stuck = new List<string>();
            var corners = new NavMeshPath();
            var times = new List<string>();
            // (Zombies from the south / south-east / north-east run the grove's east edge and stop at (−30.4, −23.9) with a complete
            // path — the player body walks that leg (Oyuncu_BatiBambuCebine_DortYandanGirerCikar_Takilmaz): the follower's 0.65 m
            // corner arrival cuts into culm AP005_i1641. Zombie steering, S-03 WP-061L; the pocket's own link is the north one.)
            foreach (Vector3 spot in new[] { new Vector3(-38.5f, 0.05f, -8f) })
            {
                _rig.Teleport(target, Quaternion.identity);
                Vector3 p = NavMesh.SamplePosition(spot, out NavMeshHit hit, 1f, NavMesh.AllAreas) ? hit.position + Vector3.up * 0.05f : spot;
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 5, p, Quaternion.identity);
                float t = 0f;
                bool reached = false;
                while (!reached && t < 40f)
                {
                    _rig.Teleport(target, Quaternion.identity);
                    yield return null;
                    t += Time.deltaTime;
                    if (z == null || !z.IsAlive) break;
                    Vector3 d = z.transform.position - target; d.y = 0f;
                    reached = d.magnitude < 2.5f;
                }

                times.Add(spot.ToString("F0") + " " + (reached ? t.ToString("F1") + " s" : "—"));
                if (!reached)
                {
                    string row = spot.ToString("F1") + " → " + (z != null ? z.transform.position.ToString("F2") : "yok");
                    if (z != null && NavMesh.SamplePosition(z.transform.position, out NavMeshHit zh, 1f, NavMesh.AllAreas) &&
                        NavMesh.SamplePosition(target, out NavMeshHit th, 1f, NavMesh.AllAreas) && NavMesh.CalculatePath(zh.position, th.position, NavMesh.AllAreas, corners))
                    {
                        row += " path " + corners.status;
                        foreach (Vector3 c in corners.corners) row += " " + c.ToString("F1");
                    }

                    stuck.Add(row);
                }

                if (z != null) UnityEngine.Object.Destroy(z.gameObject);
                yield return null;
            }

            Debug.Log("[DD-TEST] wp038r.pocket " + string.Join(" | ", times) + (stuck.Count > 0 ? " stuck: " + string.Join(" | ", stuck) : ""));
            Assert.IsEmpty(stuck, "batı cebindeki oyuncuya ulaşamayan zombi (" + stuck.Count + "): " + string.Join(" | ", stuck));
        }

        // WP-038R · The same pocket with the real player body: in from the four sides of the grove (the zombie spawn spots of the
        // test above and the SW bamboo edge a zombie stopped at) and back out to the centre. No route may stall 3 s.
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Oyuncu_BatiBambuCebine_DortYandanGirerCikar_Takilmaz()
        {
            yield return RunAp005();
            var pocket = new Vector3(-39.3f, 0f, -16.5f);
            var routes = new List<(string name, Vector3 from, Vector3 to)>();
            foreach ((string name, Vector3 p) in new[] { ("NE", new Vector3(-30f, 0f, -10f)), ("SE", new Vector3(-33f, 0f, -24f)), ("N", new Vector3(-38.5f, 0f, -8f)),
                                                         ("S", new Vector3(-36.5f, 0f, -22.5f)), ("SW-edge", new Vector3(-30.3f, 0f, -24.2f)) })
                routes.Add((name + "→cep", p, pocket));
            routes.Add(("cep→merkez", pocket, new Vector3(-7.5f, 0f, 0f)));
            var fails = new List<string>();
            yield return WalkRoutes(routes, fails);
            Debug.Log("[DD-TEST] wp038r.routes routes=" + routes.Count + " stalls=" + fails.Count + (fails.Count > 0 ? " " + string.Join(" | ", fails) : ""));
            Assert.IsEmpty(fails, "batı cebine / cepten oyuncu yürüyüşünde takılma (" + fails.Count + "):\n" + string.Join("\n", fails));
        }
    }
}
