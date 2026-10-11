using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-107 (R4) görsel kanıt: TD 1080p kareler ve kısa video kare dizisi. Run sahnesi, üstten kamera, tur tutulur.
    /// (1) Avlu: istasyonlar gizli (önce) / açık (sonra) aynı pozda, (2) roketatar istasyonu, (3) video: oyuncu Shippu rafına yürür,
    /// istem çıkar, satın alır (silah değişir), 6 m'deki zombiye yeni silahla ateş eder, rafa döner ve yarı fiyata cephane alır.
    /// HUD yakalama süresince ScreenSpaceCamera'ya alınır (istem karede görünsün), sonra geri konur. Kareler DD_WALLBUY_DIR'e:
    /// stills/*.png, video/f-NNNNNN.jpg + frames.tsv (tdmux girdisi), wallbuy-capture.json. Yalnız kaydın yazıldığını doğrular.
    /// </summary>
    [TestFixture]
    [Category("Diagnostics")]
    public sealed class Wp107WallBuyTdKareTests
    {
        private const int W = 1920, H = 1080;

        [UnityTest]
        [Explicit("R4 görsel kanıt: yalnız hedefli koşulur")]
        [Timeout(300000)]
        public IEnumerator WallBuy_Ustten_KareVeVideo()
        {
            string dir = System.Environment.GetEnvironmentVariable("DD_WALLBUY_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp107-wallbuy-capture");
            string stills = Path.Combine(dir, "stills"), video = Path.Combine(dir, "video");
            Directory.CreateDirectory(stills);
            Directory.CreateDirectory(video);
            LogAssert.ignoreFailingMessages = true;
            IInputSource previousInput = InputSourceRegistry.Current;

            int previousScene = SceneManager.GetActiveScene().handle;
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            float limit = Time.realtimeSinceStartup + 120f;
            while (!(SceneManager.GetActiveScene().handle != previousScene && SceneManager.GetActiveScene().name == PrototypeScenes.Run && RunDirector.Instance != null
                     && PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null && PrototypeCameraRig.Active != null && GoldService.Instance != null
                     && RunDirector.Instance.Phase == RunPhase.Fighting && WallBuyDirector.Instance != null && WallBuyDirector.Instance.Stations.Count > 0))
            {
                Assert.Less(Time.realtimeSinceStartup, limit, "Run sahnesi / istasyonlar gelmedi");
                yield return null;
            }

            if (EnemySpawnDirector.Instance != null) { EnemySpawnDirector.Instance.StopRound(); EnemySpawnDirector.Instance.ClearAllEnemies(); }
            EnemyFactory.ClearAll();
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = true;
            var girdi = new SavasTestGirdisi();
            InputSourceRegistry.SetCurrent(girdi); rig.Motor.SetInputSource(null); rig.Aim.SetInputSource(null);
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 1f / 30f;
            WallBuyDirector director = WallBuyDirector.Instance;
            WeaponController controller = WeaponRegistry.ActiveController;
            GoldWallet wallet = GoldService.Instance.Wallet;
            ClassicWeaponId selected = WeaponRegistry.SelectedWeapon;
            HudWallBuyPrompt prompt = Object.FindObjectOfType<HudWallBuyPrompt>();
            Canvas hud = prompt != null ? prompt.GetComponentInParent<Canvas>().rootCanvas : null;
            RenderMode hudMode = hud != null ? hud.renderMode : RenderMode.ScreenSpaceOverlay;
            var target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Wp107WallBuy" };
            var read = new Texture2D(W, H, TextureFormat.RGB24, false);
            var times = new StringBuilder();
            var log = new StringBuilder("{\n  \"weapon\": \"" + controller.Classic.Id + "\",\n  \"shots\": [\n");
            Camera cam = PrototypeCameraRig.Active.Camera;
            int frame = 0;
            float t0 = 0f;
            try
            {
                if (hud != null) { hud.renderMode = RenderMode.ScreenSpaceCamera; hud.worldCamera = cam; hud.planeDistance = cam.nearClipPlane + 0.5f; }
                for (int i = 0; i < 20; i++) yield return null;

                void Grab(string still, bool toVideo)
                {
                    RenderTexture old = cam.targetTexture;
                    cam.targetTexture = target;
                    cam.Render();
                    cam.targetTexture = old;
                    RenderTexture prev = RenderTexture.active; RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply(); RenderTexture.active = prev;
                    if (still != null)
                    {
                        File.WriteAllBytes(Path.Combine(stills, still + ".png"), read.EncodeToPNG());
                        WallBuyPrompt p = WallBuyEvents.CurrentPrompt;
                        log.Append("    {\"still\": \"").Append(still).Append("\", \"weapon\": \"").Append(controller.Classic.Id).Append("\", \"gold\": ").Append(wallet.Balance)
                           .Append(", \"prompt\": ").Append(p.Visible ? "\"" + p.Action + " " + p.Cost + " " + p.Block + "\"" : "null").Append("},\n");
                    }

                    if (toVideo)
                    {
                        File.WriteAllBytes(Path.Combine(video, "f-" + frame.ToString("000000", CultureInfo.InvariantCulture) + ".jpg"), read.EncodeToJPG(90));
                        times.Append(frame).Append('\t').Append((frame / 30f).ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
                        frame++;
                    }
                }

                // ---- (1) courtyard, the same pose without / with the stations
                rig.Teleport(new Vector3(0f, 0.1f, 2f), Quaternion.identity);
                director.transform.Find("WallBuyStations")?.gameObject.SetActive(false);
                for (int i = 0; i < 15; i++) yield return null;
                Grab("01-avlu-once", false);
                director.transform.Find("WallBuyStations")?.gameObject.SetActive(true);
                for (int i = 0; i < 5; i++) yield return null;
                Grab("02-avlu-sonra", false);

                // ---- (2) every station close up with its prompt (stills only)
                int n = 3;
                foreach (WallBuyStation s in director.Stations)
                {
                    rig.Teleport(s.transform.position + s.transform.forward * (WallBuyStation.RackDepth * 0.5f + 0.9f), Quaternion.LookRotation(-s.transform.forward));
                    for (int i = 0; i < 12; i++) yield return null;
                    Grab(n.ToString("00", CultureInfo.InvariantCulture) + "-" + s.Id, false);
                    n++;
                }

                // ---- (3) the video: walk to Shippu's rack, buy, shoot, refill
                WallBuyStation smg = director.Stations.First(s => s.Weapon != controller.Classic.Id && s.Weapon != ClassicWeaponId.Rl1);
                int price = director.Catalog.PriceOf(smg.Weapon);
                wallet.Credit(price + director.Catalog.RefillPriceOf(smg.Weapon) + 40, GoldSource.EnemyPickup, wallet.NextTransactionId());
                Vector3 front = smg.transform.position + smg.transform.forward * (WallBuyStation.RackDepth * 0.5f + 0.9f);
                Vector3 startAt = front + smg.transform.forward * 4.5f;
                rig.Teleport(startAt, Quaternion.LookRotation(-smg.transform.forward));
                for (int i = 0; i < 10; i++) yield return null;
                t0 = Time.time;
                for (int i = 0; i < 20; i++) { Grab(null, true); yield return null; }   // stand
                Vector3 walk = (front - rig.transform.position); walk.y = 0f;
                Vector2 dirXZ = new Vector2(walk.x, walk.z).normalized;
                int guard = 0;
                while ((new Vector2(rig.transform.position.x - front.x, rig.transform.position.z - front.z)).magnitude > 0.35f && guard++ < 120)
                {
                    Vector3 to = front - rig.transform.position;
                    girdi.Yon = new Vector2(to.x, to.z).normalized;   // camera-relative is TopDown north-up: world XZ
                    Vector3 sc = cam.WorldToScreenPoint(smg.transform.position + Vector3.up);
                    girdi.Imlec = new Vector2(sc.x, sc.y);
                    Grab(null, true);
                    yield return null;
                }

                girdi.Yon = Vector2.zero;
                for (int i = 0; i < 18; i++) { Grab(i == 10 ? "20-istem" : null, true); yield return null; }
                director.TestPress = true;
                for (int i = 0; i < 24; i++) { Grab(i == 12 ? "21-satin-alindi" : null, true); yield return null; }

                // a zombie 6 m out, fire the bought weapon at it
                Vector3 zAt = rig.transform.position + smg.transform.forward * 6f;
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, zAt, Quaternion.LookRotation(-smg.transform.forward));
                for (int i = 0; i < 45; i++)
                {
                    if (z != null && z.IsAlive)
                    {
                        Vector3 s2 = cam.WorldToScreenPoint(z.transform.position + Vector3.up * 1.2f);
                        girdi.Imlec = new Vector2(s2.x, s2.y);
                    }

                    girdi.AtesBasili = true;
                    Grab(i == 20 ? "22-yeni-silahla-ates" : null, true);
                    yield return null;
                }

                girdi.AtesBasili = false;
                for (int i = 0; i < 10; i++) { Grab(null, true); yield return null; }
                rig.Teleport(front, Quaternion.LookRotation(-smg.transform.forward));
                for (int i = 0; i < 10; i++) { Grab(i == 6 ? "23-cephane-istemi" : null, true); yield return null; }
                director.TestPress = true;
                for (int i = 0; i < 24; i++) { Grab(i == 12 ? "24-cephane-alindi" : null, true); yield return null; }

                File.WriteAllText(Path.Combine(video, "frames.tsv"), times.ToString());
                log.Append("    {}\n  ],\n  \"videoFrames\": ").Append(frame).Append(", \"fps\": 30, \"bought\": \"").Append(smg.Weapon).Append("\", \"selectedAfter\": \"")
                   .Append(WeaponRegistry.SelectedWeapon).Append("\"\n}\n");
                File.WriteAllText(Path.Combine(dir, "wallbuy-capture.json"), log.ToString());
                Debug.Log("[DD-TEST] wp107.wallbuy capture dir=" + dir + " frames=" + frame);
                Assert.Greater(frame, 60, "video kareleri yazılmadı");
                Assert.AreEqual(smg.Weapon, controller.Classic.Id, "satın alma karede: silah değişti");
            }
            finally
            {
                if (hud != null) { hud.renderMode = hudMode; hud.worldCamera = null; }
                girdi.AtesBasili = false; girdi.Yon = Vector2.zero;
                InputSourceRegistry.SetCurrent(previousInput);
                Time.captureDeltaTime = 0f;
                WeaponRegistry.SelectedWeapon = selected;
                target.Release(); Object.Destroy(target); Object.Destroy(read);
                if (PlayerRegistry.Primary != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }
        }
    }
}
