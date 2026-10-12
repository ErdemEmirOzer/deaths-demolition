using System.Collections;
using System.Globalization;
using System.Reflection;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-095 R3 (user 10.10 via C-00: "the bullets stop half way, hit the enemy further on, the hit is not seen; more visible,
    /// orange-ish, realistic"): the before / after evidence of the top-down tracer and hit. The Run scene, top-down camera, the round
    /// held; one Standard zombie east of the player (A 6 m, B 12 m - the right half of the TD frame), the real weapon (the run's
    /// classic, AR) fired through the test input with the cursor on the zombie's chest. Frames at 60 Hz after the
    /// trigger (1–6, 9, 14) from the game camera, a contact sheet and per-frame counters (live TD tracers, drawn core / glow, TD hit
    /// cards / flashes) with the zombie's chest and the player's screen points (the pixel measurements read them).
    /// Output: DD_TD_MERMI_DIR (else the temp cache). Asserts only that the frames and counters were recorded; what they show is
    /// the reviewer's.
    /// </summary>
    [TestFixture]
    [Category("Diagnostics")]
    public sealed class Wp095MermiTdKareTests
    {
        private const int W = 1920, H = 1080, CW = 240, CH = 135;
        // AD 11.10 M2: the arrival frames (A f01–f02, B f03–f04) and the frames after them
        private static readonly int[] Frames = { 1, 2, 3, 4, 5, 6, 9, 14 };
        // read by name so the same test also builds against the code before the user-10.10 change (the "before" set): -1 = none
        // The ribbons are Graphics.DrawMeshInstanced calls queued in the renderer's LateUpdate; a Camera.Render() from the test (after
        // Update, before LateUpdate - batch mode never returns WaitForEndOfFrame) would not include them. Queue this frame's TD draw
        // right before the render: layers core / smoke, plus the glow where the code has one (the "before" build has no TdGlowAlpha).
        private static void QueueTracers(TracerRibbonRenderer tracer)
        {
            MethodInfo draw = typeof(TracerRibbonRenderer).GetMethod("DrawTd", BindingFlags.Instance | BindingFlags.NonPublic);
            if (draw == null) return;
            int layers = typeof(TracerRibbonRenderer).GetField("TdGlowAlpha") != null || typeof(TracerRibbonRenderer).GetField("TdHaloAlpha") != null ? 3 : 2;   // WP-101: the halo
            for (int layer = 0; layer < layers; layer++) draw.Invoke(tracer, new object[] { layer });
        }
        private static int Counter(VfxDressing vfx, string name)
        {
            object v = vfx != null ? typeof(VfxDressing).GetProperty(name)?.GetValue(vfx) : null;
            return v is int n ? n : -1;
        }

        [UnityTest]
        [Explicit("R3 görsel kanıt: yalnız hedefli koşulur")]
        [Timeout(300000)]
        public IEnumerator Mermi_Ustten_IzciVeIsabet_KareSeti()
        {
            string dir = System.Environment.GetEnvironmentVariable("DD_TD_MERMI_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp095-td-mermi");
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;
            IInputSource previousInput = InputSourceRegistry.Current;

            int previousScene = SceneManager.GetActiveScene().handle;   // an earlier test's Run scene must not satisfy the wait
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            float limit = Time.realtimeSinceStartup + 120f;
            while (!(SceneManager.GetActiveScene().handle != previousScene && SceneManager.GetActiveScene().name == PrototypeScenes.Run && RunDirector.Instance != null && PlayerRegistry.Primary != null
                     && WeaponRegistry.ActiveController != null && PrototypeCameraRig.Active != null && RunDirector.Instance.Phase == RunPhase.Fighting))
            {
                Assert.Less(Time.realtimeSinceStartup, limit, "Run sahnesi / Fighting fazı gelmedi");
                yield return null;
            }
            if (EnemySpawnDirector.Instance != null) { EnemySpawnDirector.Instance.StopRound(); EnemySpawnDirector.Instance.ClearAllEnemies(); }
            EnemyFactory.ClearAll();
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = true;
            var girdi = new SavasTestGirdisi();
            InputSourceRegistry.SetCurrent(girdi); rig.Motor.SetInputSource(null); rig.Aim.SetInputSource(null);
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            var target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Wp095TdMermi" };
            var read = new Texture2D(W, H, TextureFormat.RGB24, false);
            var sheet = new Texture2D(CW * Frames.Length, CH * 2, TextureFormat.RGB24, false);
            var log = new StringBuilder("{\n  \"weapon\": \"" + WeaponRegistry.ActiveController.Classic.Id + "\",\n  \"frames\": [\n");
            try
            {
                for (int i = 0; i < 30; i++) yield return null;
                Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "TD kamera");
                TracerRibbonRenderer tracer = Object.FindObjectOfType<TracerRibbonRenderer>();
                Assert.IsNotNull(tracer, "TracerRibbonRenderer yok");
                VfxDressing vfx = HitFeedbackDirector.Instance != null ? HitFeedbackDirector.Instance.Vfx : null;
                string[] names = { "A-6m", "B-12m" };
                float[] metres = { 6f, 12f };
                for (int scene = 0; scene < 2; scene++)
                {
                    girdi.AtesBasili = false;
                    EnemyFactory.ClearAll();
                    for (int i = 0; i < 40; i++) yield return null;   // the previous scene's tracers and cards end, the gun settles
                    Vector3 p = rig.transform.position;
                    ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, p + Vector3.right * metres[scene], Quaternion.LookRotation(Vector3.left));
                    Assert.IsNotNull(z, names[scene] + ": zombi doğmadı");
                    Camera cam = PrototypeCameraRig.Active.Camera;
                    int cards = Counter(vfx, "TdHitCards"), flashes = Counter(vfx, "TdHitFlashes"), frame = 0;
                    Vector3 chest = Vector3.zero, feet = Vector3.zero;   // pixel coords of the capture (y from the top in the json)
                    for (int f = 0; f <= Frames[Frames.Length - 1]; f++)
                    {
                        if (z != null && z.IsAlive)
                        {
                            Vector3 s = cam.WorldToScreenPoint(z.transform.position + Vector3.up * 1.2f);
                            girdi.Imlec = new Vector2(s.x, s.y);
                        }
                        girdi.AtesBasili = true;
                        yield return null;
                        if (frame < Frames.Length && f == Frames[frame])
                        {
                            RenderTexture old = cam.targetTexture;
                            QueueTracers(tracer);
                            cam.targetTexture = target;   // screen points in the capture's own pixels (RT bound)
                            if (z != null) chest = cam.WorldToScreenPoint(z.transform.position + Vector3.up * 1.2f);
                            feet = cam.WorldToScreenPoint(rig.transform.position + Vector3.up * 1.2f);
                            cam.Render(); cam.targetTexture = old;
                            RenderTexture prev = RenderTexture.active; RenderTexture.active = target;
                            read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply(); RenderTexture.active = prev;
                            string file = names[scene] + "-f" + Frames[frame].ToString("00", CultureInfo.InvariantCulture) + ".png";
                            File.WriteAllBytes(Path.Combine(dir, file), read.EncodeToPNG());
                            for (int y = 0; y < CH; y++)
                                for (int x = 0; x < CW; x++)
                                    sheet.SetPixel(frame * CW + x, (1 - scene) * CH + y, read.GetPixelBilinear((x + .5f) / CW, (y + .5f) / CH));
                            log.Append("    {\"file\": \"").Append(file).Append("\", \"liveTdTracers\": ").Append(tracer.LiveTdTracers)
                               .Append(", \"drawnCore\": ").Append(tracer.DrawnCore).Append(", \"drawnGlow\": ").Append(tracer.DrawnGlow)
                               .Append(", \"tdHitCards\": ").Append(cards < 0 ? -1 : Counter(vfx, "TdHitCards") - cards)
                               .Append(", \"tdHitFlashes\": ").Append(flashes < 0 ? -1 : Counter(vfx, "TdHitFlashes") - flashes)
                               .Append(", \"zombieScreen\": [").Append(chest.x.ToString("0", CultureInfo.InvariantCulture)).Append(", ")
                               .Append((H - chest.y).ToString("0", CultureInfo.InvariantCulture)).Append("]")
                               .Append(", \"playerScreen\": [").Append(feet.x.ToString("0", CultureInfo.InvariantCulture)).Append(", ")
                               .Append((H - feet.y).ToString("0", CultureInfo.InvariantCulture)).Append("]")
                               .Append(", \"zombieAlive\": ").Append(z != null && z.IsAlive ? "true" : "false").Append("},\n");
                            frame++;
                        }
                    }
                    Assert.AreEqual(Frames.Length, frame, names[scene] + ": dört kare");
                }
                girdi.AtesBasili = false;
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "contact.png"), sheet.EncodeToPNG());
                log.Append("    {}\n  ],\n  \"camera\": \"TopDown\"\n}\n");
                File.WriteAllText(Path.Combine(dir, "td-mermi.json"), log.ToString());
                Debug.Log("[DD-TEST] wp095.td-mermi dir=" + dir + " frames=" + (Frames.Length * 2));
                Assert.IsTrue(File.Exists(Path.Combine(dir, "contact.png")), "temas sayfası yazılmadı");
            }
            finally
            {
                girdi.AtesBasili = false;
                InputSourceRegistry.SetCurrent(previousInput);
                Time.captureDeltaTime = 0f;
                target.Release(); Object.Destroy(target); Object.Destroy(read); Object.Destroy(sheet);
                if (PlayerRegistry.Primary != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }
        }
    }
}
