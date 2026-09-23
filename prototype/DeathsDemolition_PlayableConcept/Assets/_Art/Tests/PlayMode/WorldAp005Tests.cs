using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
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
    /// WP-014 · The authored world in play (Metal): the scene stands alone with its bootstrap
    /// (2664 instances after the AP-005B + AP-005C deltas, baked NavMesh, 12+12 markers, run systems), every spawn / weapon /
    /// route endpoint reaches the player start, the route sample points stay readable with
    /// the WP-011 capsule method, the corridor roof fades for the camera (REQ-111), and the
    /// Run scene loads the same world additively under WorldSource.AP005 and spawns a round.
    /// </summary>
    [TestFixture]
    [Category("WP014")]
    public sealed class WorldAp005Tests
    {
        private const float SahneZamanAsimi = 60f;
        private const float KurulumZamanAsimi = 60f;
        private const float OyuncuZamanAsimi = 30f;
        private const float DolusZamanAsimi = 90f;
        private const float YatismaZamanAsimi = 30f;
        private const float RotaOrnekAralikMetre = 2f;
        /// <summary>WP-015: the official threshold (DEC-077), not a working value.</summary>
        private const float OrtulmeEsigi = ReadabilityThresholds.OcclusionRatioMax;

        private EnemySpawnDirector _director;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            _director = null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_director != null) _director.ForceActiveTarget(0, false);
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi)
                {
                    yield return null;
                }
            }

            _director = null;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------ yardımcılar

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - t0 > saniye) Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                yield return null;
            }
        }

        private static IEnumerator BootTanMainMenuye()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
        }

        /// <summary>Standalone: the world scene as the active scene (own bootstrap, HUD, run systems).</summary>
        private IEnumerator DunyayiTekBasinaYukle()
        {
            yield return BootTanMainMenuye();
            SceneManager.LoadScene(ArtLibraryPaths.Ap005WorldSceneName, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == ArtLibraryPaths.Ap005WorldSceneName && SceneManager.GetActiveScene().isLoaded,
                SahneZamanAsimi, "World_AP005 etkin olmadı (Build Settings 7. sıra?). Şu an: " + SceneManager.GetActiveScene().name);
            yield return Bekle(() => Ap005WorldBootstrap.Last.Instances > 0, KurulumZamanAsimi, "Ap005WorldBootstrap raporu gelmedi.");
            yield return Bekle(() => PlayerRegistry.Primary != null, OyuncuZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null, KurulumZamanAsimi, "Run sistemleri kurulmadı.");
            _director = EnemySpawnDirector.Instance;
        }

        private static WorldPlacementAsset Placement()
        {
            var bootstrap = UnityEngine.Object.FindObjectOfType<Ap005WorldBootstrap>();
            Assert.IsNotNull(bootstrap, "Ap005WorldBootstrap sahnede yok");
            Assert.IsNotNull(bootstrap.Placement, "Placement asset bağlı değil");
            return bootstrap.Placement;
        }

        private static bool PathComplete(Vector3 from, Vector3 to, out string why)
        {
            why = string.Empty;
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 2f, NavMesh.AllAreas)) { why = "başlangıç NavMesh'te değil"; return false; }
            if (!NavMesh.SamplePosition(to, out NavMeshHit b, 2f, NavMesh.AllAreas)) { why = "hedef 2 m içinde NavMesh'te değil"; return false; }
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                why = "yol " + path.status + " (örnek " + b.position.ToString("F2") + ", " + Vector3.Distance(to, b.position).ToString("F2") + " m)";
                return false;
            }

            return true;
        }

        // ================================================================== dünya ve bootstrap

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Dunya_TekBasina_3143Ornek_NavMeshPismis_MarkerlarVeSistemler()
        {
            yield return DunyayiTekBasinaYukle();
            Ap005WorldBootstrap.Result r = Ap005WorldBootstrap.Last;
            Assert.IsTrue(r.Ok, "Bootstrap raporu FAIL:\n" + r.Report);
            Assert.AreEqual(3168, r.Instances);   // WP-038B: AP-017 v004 +456 -> 3120; WP-038I: AP-017 Faz 2 v005 +23 -> 3143; WP-038L: Faz 2b v006 +25 -> 3168 (name kept)
            Assert.Greater(r.NavMeshTriangles, 0, "NavMesh boş");
            Assert.AreEqual(0, r.Unreachable, "erişilemeyen marker:\n" + r.Report);
            Assert.AreEqual(12, UnityEngine.Object.FindObjectsOfType<SpawnPointMarker>().Length, "12 spawn marker");
            Assert.AreEqual(12, UnityEngine.Object.FindObjectsOfType<SpecialWeaponSpawnPoint>().Length, "12 silah noktası");
            Assert.Greater(r.DriftFields, 0, "drift alanı yok");
            Assert.IsNotNull(UnityEngine.Object.FindObjectOfType<WindDriver>(), "rüzgâr sürücüsü yok");
            Debug.Log("[DD-TEST] wp014.world instances=" + r.Instances + " renderers=" + r.Renderers + " lodGroups=" + r.LodGroups + " colliders=" + r.Colliders + " navTris=" + r.NavMeshTriangles + " drift=" + r.DriftFields);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Erisilebilirlik_12Spawn_12Silah_49RotaUcNoktasi()
        {
            yield return DunyayiTekBasinaYukle();
            WorldPlacementAsset p = Placement();
            Vector3 start = p.playerStart;
            var hatalar = new List<string>();
            int ok = 0, total = 0;
            foreach (WorldPlacementAsset.Point q in p.spawnPoints) { total++; if (PathComplete(start, q.position, out string why)) ok++; else hatalar.Add("spawn " + q.id + ": " + why); }
            foreach (WorldPlacementAsset.Point q in p.weaponPoints) { total++; if (PathComplete(start, q.position, out string why)) ok++; else hatalar.Add("weapon " + q.id + ": " + why); }
            int routeOk = 0;
            foreach (WorldPlacementAsset.Route rt in p.routes)
            {
                total += 2;
                bool a = PathComplete(start, rt.a, out string wa);
                bool b = PathComplete(start, rt.b, out string wb);
                if (a) ok++; else hatalar.Add("route " + rt.index + " a " + rt.a.ToString("F1") + ": " + wa);
                if (b) ok++; else hatalar.Add("route " + rt.index + " b " + rt.b.ToString("F1") + ": " + wb);
                if (a && b) routeOk++;
            }

            Debug.Log("[DD-TEST] wp014.reach ok=" + ok + "/" + total + " routesBothEnds=" + routeOk + "/" + p.routes.Count);
            Assert.IsEmpty(hatalar, "Erişilemeyen (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Assert.AreEqual(49, routeOk);
        }

        // ================================================================== okunabilirlik (WP-011 kapsül yöntemi, 49 rota)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Okunabilirlik_49RotaOrnekleri_Kapsul5Isin_YuzdeElliUstuOrtulmeYok()
        {
            yield return DunyayiTekBasinaYukle();
            Physics.SyncTransforms();
            WorldPlacementAsset p = Placement();
            int mask = PrototypeLayers.AimBlockMask;
            int envLayer = PrototypeLayers.Environment;
            var rigOffset = new Vector3(0f, 15f, -13f);
            float height = 1.8f, radius = 0.35f;
            var points = new Vector3[5];
            int samples = 0, worstBlocked = 0, worstBlockedEnv = 0, overHalfEnv = 0, overHalfTotal = 0, overHalfThin = 0;
            Vector3 worstAt = Vector3.zero;
            var overHalfList = new List<string>();
            var thinList = new List<string>();
            foreach (WorldPlacementAsset.Route rt in p.routes)
            {
                Vector3 a = rt.a, b = rt.b;
                float len = Vector3.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / RotaOrnekAralikMetre));
                for (int s = 0; s <= steps; s++)
                {
                    Vector3 feet = Vector3.Lerp(a, b, (float)s / steps);
                    if (NavMesh.SamplePosition(feet, out NavMeshHit hit, 2f, NavMesh.AllAreas)) feet = hit.position;
                    Vector3 cam = feet + rigOffset;
                    points[0] = feet + Vector3.up * 0.2f;
                    points[1] = feet + Vector3.up * (height * 0.5f);
                    points[2] = feet + Vector3.up * (height - 0.1f);
                    points[3] = feet + Vector3.up * (height * 0.75f) - Vector3.right * radius;
                    points[4] = feet + Vector3.up * (height * 0.75f) + Vector3.right * radius;
                    int blocked = 0, blockedEnv = 0, blockedThin = 0;
                    string hits = string.Empty, thinHits = string.Empty;
                    for (int i = 0; i < 5; i++)
                    {
                        Vector3 d = points[i] - cam;
                        float l = d.magnitude;
                        if (Physics.Raycast(cam, d / l, out RaycastHit h, l, mask, QueryTriggerInteraction.Ignore))
                        {
                            blocked++;
                            // A culm-thin capsule (≤ 0.15 m) on the camera plane cuts every axial
                            // ray of the capsule but hides none of a 0.7 m body (REQ-104 is about
                            // plants OBSCURING enemies); it is counted, reported, and not a failure.
                            if (h.collider is CapsuleCollider thin && thin.radius <= ReadabilityThresholds.ThinCapsuleRadius)
                            {
                                blockedThin++;
                                Transform culm = h.collider.transform;
                                while (culm.parent != null && culm.parent.name != Ap005WorldBootstrap.InstancesName && culm.parent.name != Ap005WorldBootstrap.RootName) culm = culm.parent;
                                thinHits += (thinHits.Length > 0 ? ", " : "") + culm.name + "@" + h.point.ToString("F2");
                                continue;
                            }

                            if (h.collider.gameObject.layer == envLayer)
                            {
                                blockedEnv++;
                                Transform owner = h.collider.transform;
                                while (owner.parent != null && owner.parent.name != Ap005WorldBootstrap.InstancesName && owner.parent.name != Ap005WorldBootstrap.RootName) owner = owner.parent;
                                hits += (hits.Length > 0 ? ", " : "") + owner.name + "/" + h.collider.name + "@" + h.point.ToString("F1");
                            }
                        }
                    }

                    samples++;
                    if (blockedThin / 5f > OrtulmeEsigi) { overHalfThin++; thinList.Add("route " + rt.index + " @" + feet.ToString("F2") + " culm " + blockedThin + "/5 [" + thinHits + "]"); }
                    if (blockedEnv > worstBlockedEnv) { worstBlockedEnv = blockedEnv; worstAt = feet; }
                    if (blocked > worstBlocked) worstBlocked = blocked;
                    if (blockedEnv / 5f > OrtulmeEsigi) { overHalfEnv++; overHalfList.Add("route " + rt.index + " @" + feet.ToString("F1") + " env " + blockedEnv + "/5 [" + hits + "]"); }
                    if (blocked / 5f > OrtulmeEsigi) overHalfTotal++;
                }
            }

            Debug.Log("[DD-TEST] wp014.readability samples=" + samples + " worstEnv=" + worstBlockedEnv + "/5 @" + worstAt.ToString("F1") + " worstTotal=" + worstBlocked + "/5 over50env=" + overHalfEnv + " over50thinCulm=" + overHalfThin + " over50total(fade+culm dahil)=" + overHalfTotal);
            Assert.Greater(samples, 49);
            Assert.AreEqual(0, overHalfEnv, "Sabit çevreyle >%50 örtülen rota noktası (DEC-077 eşiği; " + overHalfEnv + "):\n" + string.Join("\n", overHalfList));
            // WP-015: the three WP-014 camera-plane culms are nudged by the placement overrides;
            // culm-thin hits are still not failures, but with the overrides applied there are none.
            Assert.AreEqual(0, overHalfThin, "İnce gövde (culm) ile >%50 kesilen rota noktası; WP-015 override'ları sonrası 0 beklenir:\n" + string.Join("\n", thinList));
        }

        // ================================================================== fade (REQ-111, koridor çatısı)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Fade_KoridorCatisi_KameraArasindaykenSaydamlasir()
        {
            yield return DunyayiTekBasinaYukle();
            WorldPlacementAsset p = Placement();
            // The west corridor bays sit at x = −36.95; the rig looks from +15 up / −13 back (−z),
            // so a bay with a neighbour further south puts that neighbour's roof on the ray.
            var bays = new List<WorldPlacementAsset.Instance>();
            foreach (WorldPlacementAsset.Instance i in p.instances) if (i.family == "west_corridor_module") bays.Add(i);
            Assert.AreEqual(6, bays.Count, "6 koridor modülü");
            bays.Sort((x, y) => x.position.z.CompareTo(y.position.z));
            Vector3 stand = bays[2].position + Vector3.up * 0.15f;

            PlayerRig rig = PlayerRegistry.Primary;
            Assert.IsNotNull(rig);
            rig.Teleport(stand, Quaternion.identity);
            float t0 = Time.realtimeSinceStartup;
            OccluderFadeState fading = null;
            while (Time.realtimeSinceStartup - t0 < 4f && fading == null)
            {
                yield return null;
                foreach (OccluderFadeState s in UnityEngine.Object.FindObjectsOfType<OccluderFadeState>())
                {
                    if (s.enabled && s.Alpha < 0.99f) { fading = s; break; }
                }
            }

            Assert.IsNotNull(fading, "Koridorda dururken hiçbir occluder saydamlaşmadı (kamera " + (stand + new Vector3(0f, 15f, -13f)).ToString("F1") + ").");
            Debug.Log("[DD-TEST] wp014.fade state=" + fading.name + " alpha=" + fading.Alpha.ToString("F2", CultureInfo.InvariantCulture) + " after " + (Time.realtimeSinceStartup - t0).ToString("F2", CultureInfo.InvariantCulture) + " s at " + stand.ToString("F1"));

            rig.Teleport(p.playerStart, Quaternion.identity);
            float t1 = Time.realtimeSinceStartup;
            bool released = false;
            while (Time.realtimeSinceStartup - t1 < 4f && !released)
            {
                yield return null;
                released = true;
                foreach (OccluderFadeState s in UnityEngine.Object.FindObjectsOfType<OccluderFadeState>()) if (s.enabled) { released = false; break; }
            }

            Assert.IsTrue(released, "Saydamlaşma serbest bırakılmadı.");
        }

        // ================================================================== görsel karşılaştırma (AP-005 v002 gameplay kadrajları)

        /// <summary>
        /// The five AP-005 gameplay framings (02-plan camera_framings, 19.9 m / FOV 42 / −49°)
        /// captured in Unity at 1080p for the side-by-side with 06-renders/gameplay/*-v002.png;
        /// Blender (x, y, z) → Unity (x, z, y). Written to DD_ART_CAPTURE_DIR; non-black, no magenta.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Gorsel_5BolgeKadraji_1080pYakalama_SiyahVeMagentaDegil()
        {
            yield return DunyayiTekBasinaYukle();
            for (int i = 0; i < 10; i++) yield return null;

            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp014-captures");
            Directory.CreateDirectory(dir);

            var poses = new[]
            {
                new ArtVisualRegression.CameraPose("world-center", new Vector3(0f, 15f, -13.05f), new Vector3(0f, 0f, 0f), 42f),
                new ArtVisualRegression.CameraPose("world-north", new Vector3(0f, 15f, 11.95f), new Vector3(0f, 0f, 25f), 42f),
                new ArtVisualRegression.CameraPose("world-east", new Vector3(21.45f, 15f, 0.8f), new Vector3(34.5f, 0f, 0.8f), 42f),
                new ArtVisualRegression.CameraPose("world-south", new Vector3(4f, 15f, -15.95f), new Vector3(4f, 0f, -29f), 42f),
                new ArtVisualRegression.CameraPose("world-west", new Vector3(-36.95f, 15f, -13.05f), new Vector3(-36.95f, 0f, 0f), 42f),
                // WP-015: the ~90 m overview the WP-013 art review judged the fog on (DEC-076), for the world.
                new ArtVisualRegression.CameraPose("world-overview", new Vector3(-45f, 30f, -75f), new Vector3(5f, 0f, 0f), 42f)
            };

            foreach (ArtVisualRegression.CameraPose pose in poses)
            {
                // WP-015: mipmap streaming (ADR-015-1) needs a live camera at the pose before
                // the one-shot capture, or the frame shows whatever mips the world camera wanted.
                int warm = 0;
                yield return ArtVisualRegression.WarmStreaming(pose, 1920, 1080, 120, f => warm = f);
                string yol = Path.Combine(dir, pose.name + "-1920x1080.png");
                ArtVisualRegression.CaptureToPng(pose, 1920, 1080, yol);
                yield return null;
                Assert.IsTrue(File.Exists(yol), "Yakalama yazılmadı: " + yol);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                tex.LoadImage(File.ReadAllBytes(yol));
                Color32[] px = tex.GetPixels32();
                UnityEngine.Object.DestroyImmediate(tex);
                long sum = 0; int magenta = 0;
                for (int i = 0; i < px.Length; i++) { sum += px[i].r + px[i].g + px[i].b; if (px[i].r > 200 && px[i].b > 200 && px[i].g < 60) magenta++; }
                double mean = sum / (3.0 * px.Length);
                Debug.Log("[DD-TEST] wp014.capture " + pose.name + " mean=" + mean.ToString("F1", CultureInfo.InvariantCulture) + " magenta=" + ((double)magenta / px.Length * 100.0).ToString("F3", CultureInfo.InvariantCulture) + "% warmFrames=" + warm +
                          " streaming=" + Texture.streamingTextureCount + " pending=" + Texture.streamingTexturePendingLoadCount + " texMB current=" + (Texture.currentTextureMemory / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) +
                          " desired=" + (Texture.desiredTextureMemory / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " nonStreaming=" + (Texture.nonStreamingTextureMemory / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " -> " + yol);
                Assert.Greater(mean, 1.0, pose.name + ": kare siyah");
                Assert.Less((double)magenta / px.Length, 0.001, pose.name + ": magenta");

                // WP-015 fog A/B for the art-director judgment (DEC-076): the same frame with the
                // WP-013 ramp (52→138 m, background colour). The sky stays the WP-015 gradient.
                float fogEnd = RenderSettings.fogEndDistance;
                Color fogColor = RenderSettings.fogColor;
                RenderSettings.fogEndDistance = WorldAtmosphere.FogEndWp013;
                RenderSettings.fogColor = WorldAtmosphere.FogColorWp013;
                yield return null;
                string yolA = Path.Combine(dir, pose.name + "-fogWp013-1920x1080.png");
                ArtVisualRegression.CaptureToPng(pose, 1920, 1080, yolA);
                RenderSettings.fogEndDistance = fogEnd;
                RenderSettings.fogColor = fogColor;
                yield return null;
                Assert.IsTrue(File.Exists(yolA), "A/B yakalaması yazılmadı: " + yolA);
            }
        }

        // ================================================================== Run entegrasyonu (WorldSource.AP005)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Run_WorldSourceAP005_AdditiveDunya_HudVeSpawnAkisi()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, SahneZamanAsimi, "World_AP005 additive yüklenmedi");
            yield return Bekle(() => Ap005WorldBootstrap.Last.Instances > 0 && Ap005WorldBootstrap.Last.UnderRun, KurulumZamanAsimi, "Dünya bootstrap'i Run altında koşmadı");
            Assert.AreEqual(WorldSource.AP005, WorldBootstrap.ActiveSource, "WorldBootstrap kaynağı AP005 olmalı");
            Assert.AreEqual(PrototypeScenes.Run, SceneManager.GetActiveScene().name, "etkin sahne Run kalmalı");
            Assert.IsNull(GameObject.Find(WorldBuilder.RootName), "prosedürel dünya kurulmamalı");
            Assert.IsTrue(Ap005WorldBootstrap.Last.Ok, Ap005WorldBootstrap.Last.Report);

            yield return Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            yield return Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "run sistemleri");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            Assert.IsNotNull(UnityEngine.Object.FindObjectOfType<RunHudScreen>(), "HUD (Run installer) yok");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;

            // One round's worth of spawning through the real director on the AP-005 markers.
            _director.ForceActiveTarget(20, true);
            float t0 = Time.realtimeSinceStartup;
            yield return Bekle(() => _director.ActiveCount >= 20, DolusZamanAsimi, "20 düşman doğmadı (AP-005 spawn noktaları?)");
            int weaponPoints = UnityEngine.Object.FindObjectsOfType<SpecialWeaponSpawnPoint>().Length;
            Debug.Log("[DD-TEST] wp014.run active=" + _director.ActiveCount + " fillSeconds=" + (Time.realtimeSinceStartup - t0).ToString("F2", CultureInfo.InvariantCulture) + " weaponPoints=" + weaponPoints + " world=" + WorldBootstrap.ActiveSource);
            Assert.AreEqual(12, weaponPoints);
            _director.ForceActiveTarget(0, false);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Run_WorldSourceProcedural_FallbackDegismedi()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "Procedural");
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.StartRun();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return Bekle(() => WorldBootstrap.Last.Root != null, KurulumZamanAsimi, "prosedürel dünya kurulmadı");
            Assert.AreEqual(WorldSource.Procedural, WorldBootstrap.ActiveSource);
            Assert.IsFalse(SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, "AP005 sahnesi yüklenmemeli");
            Assert.IsTrue(WorldBootstrap.Last.Ok, WorldBootstrap.Last.Report);
            yield return Bekle(() => PlayerRegistry.Primary != null, OyuncuZamanAsimi, "oyuncu");
        }
    }
}
