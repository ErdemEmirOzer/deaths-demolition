using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-095 R3 son tur (C-00 10.10, S-02 for S-07): the visual evidence of the top-down blast after the brush path keeps the M-62 /
    /// M-56 rules (a13571fa). The Run scene, top-down camera, Cinematic VFX tier, no zombies (the round held). Three scenes through the
    /// game's own presentation path (HitFeedbackDirector.ReportExplosion: the director's reservation, then FeelEvents - the brush ring
    /// and light): A a rocket (rl_1's outer radius); B lmg_1 explosive rounds, five small blasts at 5/s; C rl_1 bomblets, the
    /// rocket-grade blast and three small ones on a ring (RocketImpacts' split). Four frames each from the game camera (2, 8, 20, 50
    /// frames at 60 Hz after the first blast), one contact sheet and the director / brush counters per frame.
    /// Output: DD_TD_PATLAMA_DIR (else the temp cache). Asserts only that the frames and counters were recorded and the brush owned
    /// the blast; what they show is the reviewer's.
    /// </summary>
    [TestFixture]
    [Category("Diagnostics")]
    public sealed class Wp095PatlamaTdKareTests
    {
        private const int W = 1920, H = 1080, CW = 480, CH = 270;
        private static readonly int[] Frames = { 2, 8, 20, 50 };

        [UnityTest]
        [Explicit("R3 görsel kanıt: yalnız hedefli koşulur")]
        [Timeout(300000)]
        public IEnumerator Patlama_Ustten_RoketLmgBombacik_KareSeti()
        {
            string dir = System.Environment.GetEnvironmentVariable("DD_TD_PATLAMA_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp095-td-patlama");
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            float limit = Time.realtimeSinceStartup + 120f;
            while (!(SceneManager.GetActiveScene().name == PrototypeScenes.Run && RunDirector.Instance != null && PlayerRegistry.Primary != null
                     && HitFeedbackDirector.Instance != null && PrototypeCameraRig.Active != null && RunDirector.Instance.Phase == RunPhase.Fighting))
            {
                Assert.Less(Time.realtimeSinceStartup, limit, "Run sahnesi / Fighting fazı gelmedi");
                yield return null;
            }

            if (EnemySpawnDirector.Instance != null) { EnemySpawnDirector.Instance.StopRound(); EnemySpawnDirector.Instance.ClearAllEnemies(); }
            EnemyFactory.ClearAll();
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            VfxTierLevel? tier = VfxTier.Override;
            VfxTier.Override = VfxTierLevel.Cinematic;
            Time.captureDeltaTime = 1f / 60f;
            var target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Wp095TdPatlama" };
            var read = new Texture2D(W, H, TextureFormat.RGB24, false);
            var sheet = new Texture2D(CW * Frames.Length, CH * 3, TextureFormat.RGB24, false);
            var log = new StringBuilder("{\n  \"frames\": [\n");
            try
            {
                for (int i = 0; i < 30; i++) yield return null;   // the camera settles on the player
                Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "TD kamera");
                Assert.IsTrue(FeelBrushVfxSystem.OwnsTdLayers, "fırça katmanları TD patlamasının sahibi değil");
                Vector3 p = PlayerRegistry.Primary.transform.position;
                float rocket = Arsenal.Get(ClassicWeaponId.Rl1).explosive.outerRadius;
                ExplosionDirector blast = ExplosionDirector.Instance;
                Assert.IsNotNull(blast, "ExplosionDirector yok");
                string[] names = { "A-roket", "B-lmg1-patlayici", "C-rl1-bombacik" };
                for (int scene = 0; scene < 3; scene++)
                {
                    FeelBrushVfxSystem.Instance.ResetForTest();
                    blast.Clear();
                    for (int i = 0; i < 90; i++) yield return null;   // the previous scene's smoke and debris end
                    int presented = blast.Presentations;
                    Vector3 at = p + new Vector3(0f, 0.1f, 5f);
                    int frame = 0, shot = 0;
                    for (int f = 0; f <= Frames[Frames.Length - 1]; f++)
                    {
                        if (f == 0 && scene != 1) HitFeedbackDirector.Instance.ReportExplosion(at, rocket, 0, ExplosionSource.Rocket);
                        if (scene == 1 && f % 12 == 0 && shot < 5)   // lmg_1 explosive rounds: 5/s along a short line
                        {
                            HitFeedbackDirector.Instance.ReportExplosion(p + new Vector3(-3f + 1.5f * shot, 0.1f, 4f + (shot % 2)), 3f, 0, ExplosionSource.Small);
                            shot++;
                        }
                        if (scene == 2 && f == 0)   // RocketImpacts' split: the first rocket-grade, the rest small, on a ring
                            for (int b = 0; b < 3; b++)
                                HitFeedbackDirector.Instance.ReportExplosion(at + Quaternion.Euler(0f, 30f + b * 120f, 0f) * Vector3.forward * (rocket * 0.6f),
                                    rocket * 0.5f, 0, ExplosionSource.Small);
                        if (frame < Frames.Length && f == Frames[frame])
                        {
                            Camera main = PrototypeCameraRig.Active.Camera;
                            RenderTexture old = main.targetTexture;
                            main.targetTexture = target; main.Render(); main.targetTexture = old;
                            RenderTexture prev = RenderTexture.active; RenderTexture.active = target;
                            read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply(); RenderTexture.active = prev;
                            string file = names[scene] + "-f" + Frames[frame].ToString("00", CultureInfo.InvariantCulture) + ".png";
                            File.WriteAllBytes(Path.Combine(dir, file), read.EncodeToPNG());
                            for (int y = 0; y < CH; y++)
                                for (int x = 0; x < CW; x++)
                                    sheet.SetPixel(frame * CW + x, (2 - scene) * CH + y, read.GetPixelBilinear((x + .5f) / CW, (y + .5f) / CH));
                            log.Append("    {\"file\": \"").Append(file).Append("\", \"presentations\": ").Append(blast.Presentations - presented)
                               .Append(", \"full\": ").Append(blast.FullPresentations).Append(", \"columns\": ").Append(blast.Columns)
                               .Append(", \"residues\": ").Append(blast.Residues).Append(", \"brushLiveRings\": ").Append(FeelBrushVfxSystem.Instance.LiveExplosions)
                               .Append(", \"brushShadowedLights\": ").Append(FeelBrushVfxSystem.Instance.ShadowedLightsLit).Append("},\n");
                            frame++;
                        }
                        yield return null;
                    }
                    Assert.AreEqual(Frames.Length, frame, names[scene] + ": dört kare");
                    Assert.Greater(blast.Presentations, presented, names[scene] + ": patlama sunulmadı");
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "contact.png"), sheet.EncodeToPNG());
                log.Append("    {}\n  ],\n  \"rocketOuterRadius\": ").Append(rocket.ToString("0.###", CultureInfo.InvariantCulture))
                   .Append(",\n  \"tier\": \"Cinematic\",\n  \"camera\": \"TopDown\"\n}\n");
                File.WriteAllText(Path.Combine(dir, "td-patlama.json"), log.ToString());
                Debug.Log("[DD-TEST] wp095.td-patlama dir=" + dir + " frames=" + (Frames.Length * 3));
                Assert.IsTrue(File.Exists(Path.Combine(dir, "contact.png")), "temas sayfası yazılmadı");
            }
            finally
            {
                VfxTier.Override = tier;
                Time.captureDeltaTime = 0f;
                target.Release(); Object.Destroy(target); Object.Destroy(read); Object.Destroy(sheet);
                if (PlayerRegistry.Primary != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }
        }
    }
}
