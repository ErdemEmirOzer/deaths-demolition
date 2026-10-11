using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Enemies;
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
    /// WP-101 (R4, user 11.10: "mermiler bu sefer çok küçük ve fazla turuncu olmuş. mermi projectileleri ultra gerçekçi olacak") ·
    /// per-class top-down bullet evidence, the same code path before and after (only the public API of b46a3ca4 + reflection, so
    /// this test runs on main as the "before"). Run scene, TD camera, round held; per weapon (AR, SMG, LMG, sniper, sg_2, sg_1) a
    /// Standard zombie 9 m east (r1: 14 m sat on the frame's edge), the trigger held on its chest for 0.8 s; every 2nd frame (60 Hz) is a
    /// video frame AND is measured; the weapon's fullest frame (most streak pixels) is saved as its still. Per frame, inside the
    /// 1.2 m-wide band from the muzzle to the zombie (the tracer layers only - see the dark frame):
    /// streak pixels (any channel ≥ 0.10 off the same pixel with the tracer layers held back — the tracer's own pixels),
    /// their orange share (hue 8–34°, S ≥ 0.45) and saturated-warm share (hue 8–50°, S ≥ 0.45: the R3 streak sat at 36–42°), and
    /// the core's cross width (px, median over the band's sample columns).
    /// Output DD_TD_MERMI101_DIR (else the temp cache): one still per weapon, video frames + frames.tsv (tdmux), mermi101.json (per
    /// weapon: frames with a streak, mean / max streak px, mean orange share, median core width px).
    /// Asserts that the frames and measures were recorded; the verdict is the AD's and the before/after comparison.
    /// </summary>
    [TestFixture]
    [Category("Diagnostics")]
    public sealed class Wp101MermiSinifTdTests
    {
        private const int W = 1920, H = 1080;
        private static readonly ClassicWeaponId[] Weapons =
        {
            ClassicWeaponId.AssaultRifle, ClassicWeaponId.AutoSmg, ClassicWeaponId.Lmg1, ClassicWeaponId.Sr1, ClassicWeaponId.Sg2, ClassicWeaponId.CombatShotgun
        };

        private static void QueueTracers(TracerRibbonRenderer tracer, bool on)
        {
            if (!on) return;
            MethodInfo draw = typeof(TracerRibbonRenderer).GetMethod("DrawTd", BindingFlags.Instance | BindingFlags.NonPublic);
            if (draw == null) return;
            for (int layer = 0; layer < 3; layer++) draw.Invoke(tracer, new object[] { layer });
        }

        [UnityTest]
        [Explicit("R4 görsel kanıt + ölçüm: yalnız hedefli koşulur")]
        [Timeout(900000)]
        public IEnumerator Mermi_Ustten_SinifBasinaIzVeOlcum()
        {
            string dir = System.Environment.GetEnvironmentVariable("DD_TD_MERMI101_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp101-td-mermi");
            Directory.CreateDirectory(Path.Combine(dir, "video"));
            LogAssert.ignoreFailingMessages = true;
            IInputSource previousInput = InputSourceRegistry.Current;
            ClassicWeaponId previousSelected = WeaponRegistry.SelectedWeapon;

            int previousScene = SceneManager.GetActiveScene().handle;
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            float limit = Time.realtimeSinceStartup + 120f;
            while (!(SceneManager.GetActiveScene().handle != previousScene && SceneManager.GetActiveScene().name == PrototypeScenes.Run && RunDirector.Instance != null
                     && PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null && PrototypeCameraRig.Active != null && RunDirector.Instance.Phase == RunPhase.Fighting))
            {
                Assert.Less(Time.realtimeSinceStartup, limit, "Run sahnesi / Fighting fazı gelmedi");
                yield return null;
            }
            if (EnemySpawnDirector.Instance != null) { EnemySpawnDirector.Instance.StopRound(); EnemySpawnDirector.Instance.ClearAllEnemies(); }
            EnemyFactory.ClearAll();
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = true;
            WeaponController weapon = WeaponRegistry.ActiveController;
            var girdi = new SavasTestGirdisi();
            InputSourceRegistry.SetCurrent(girdi); rig.Motor.SetInputSource(null); rig.Aim.SetInputSource(null);
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            PrototypeCameraRig.SuppressLookAheadDev = true;
            var target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Wp101TdMermi" };
            var read = new Texture2D(W, H, TextureFormat.RGB24, false);
            var log = new StringBuilder("{\n  \"weapons\": [\n");
            var tsv = new StringBuilder();
            int videoFrame = 0, stills = 0;
            try
            {
                for (int i = 0; i < 30; i++) yield return null;
                TracerRibbonRenderer tracer = Object.FindObjectOfType<TracerRibbonRenderer>();
                Assert.IsNotNull(tracer, "TracerRibbonRenderer yok");
                Camera cam = PrototypeCameraRig.Active.Camera;
                foreach (ClassicWeaponId id in Weapons)
                {
                    girdi.AtesBasili = false;
                    EnemyFactory.ClearAll();
                    weapon.EquipClassic(id);
                    for (int i = 0; i < 60; i++) yield return null;   // switch-in, the previous weapon's rounds end
                    Vector3 p = rig.transform.position;
                    ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, p + Vector3.right * 9f, Quaternion.LookRotation(Vector3.left));
                    Assert.IsNotNull(z, id + ": zombi doğmadı");
                    z.enabled = false;   // it stands as the target (no approach, no attack)
                    int frames = 0, bestStreak = -1; long streakSum = 0; float orangeSum = 0f, warmSum = 0f, metre = 0f;
                    var widths = new System.Collections.Generic.List<float>();
                    Color32[] best = null;
                    for (int f = 0; f <= 48; f++)
                    {
                        Vector3 chest = z != null ? z.transform.position + Vector3.up * 1.2f : p + Vector3.right * 9f + Vector3.up * 1.2f;
                        Vector3 s = cam.WorldToScreenPoint(chest);
                        girdi.Imlec = new Vector2(s.x, s.y);
                        girdi.AtesBasili = f < 48;
                        yield return null;
                        if ((f & 1) != 0) continue;
                        // the dark frame FIRST: a DrawMeshInstanced queued for the lit render stays in every later render of this frame
                        // (v3 grabbed it second and measured ≈ 20 px for a 280 px streak)
                        Color32[] dark = Grab(cam, target, read, tracer, false);   // the same frame with the TD tracer layers held back
                        Color32[] lit = Grab(cam, target, read, tracer, true);
                        File.WriteAllBytes(Path.Combine(dir, "video", "f-" + videoFrame.ToString("000000", CultureInfo.InvariantCulture) + ".jpg"), read.EncodeToJPG(92));
                        tsv.Append(videoFrame).Append('\t').Append((videoFrame / 30f).ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
                        videoFrame++;
                        Vector2 a = Px(cam, rig.transform.position + Vector3.up * 1.4f), b = Px(cam, chest);
                        metre = (Px(cam, chest + cam.transform.right) - b).magnitude;
                        Measure(lit, dark, a, b, metre * .6f, out int streak, out float orange, out float width, out float warm);
                        if (streak <= 0) continue;
                        frames++; streakSum += streak; orangeSum += orange; warmSum += warm; widths.Add(width);
                        if (streak > bestStreak) { bestStreak = streak; best = lit; }
                    }
                    if (best != null)
                    {
                        read.SetPixels32(best); read.Apply();
                        File.WriteAllBytes(Path.Combine(dir, id + ".png"), read.EncodeToPNG());
                        stills++;
                    }
                    widths.Sort();
                    log.Append("    {\"weapon\": \"").Append(id).Append("\", \"framesWithStreak\": ").Append(frames)
                       .Append(", \"meanStreakPx\": ").Append(frames > 0 ? (streakSum / frames).ToString(CultureInfo.InvariantCulture) : "0")
                       .Append(", \"maxStreakPx\": ").Append(bestStreak < 0 ? 0 : bestStreak)
                       .Append(", \"meanOrangeShare\": ").Append((frames > 0 ? orangeSum / frames : 0f).ToString("0.###", CultureInfo.InvariantCulture))
                       .Append(", \"meanSaturatedWarmShare\": ").Append((frames > 0 ? warmSum / frames : 0f).ToString("0.###", CultureInfo.InvariantCulture))
                       .Append(", \"medianCoreWidthPx\": ").Append((widths.Count > 0 ? widths[widths.Count / 2] : 0f).ToString("0.#", CultureInfo.InvariantCulture))
                       .Append(", \"pxPerMetre\": ").Append(metre.ToString("0.#", CultureInfo.InvariantCulture)).Append("},\n");
                    if (z != null) Object.Destroy(z.gameObject);
                }
                girdi.AtesBasili = false;
                File.WriteAllText(Path.Combine(dir, "video", "frames.tsv"), tsv.ToString());
                log.Append("    {}\n  ],\n  \"camera\": \"TopDown\", \"videoFrames\": ").Append(videoFrame).Append("\n}\n");
                File.WriteAllText(Path.Combine(dir, "mermi101.json"), log.ToString());
                Debug.Log("[DD-TEST] wp101.td-mermi dir=" + dir + " stills=" + stills + " video=" + videoFrame);
                Assert.AreEqual(Weapons.Length, stills, "her silahın izli bir karesi");
                Assert.Greater(videoFrame, 100, "video kareleri");
            }
            finally
            {
                girdi.AtesBasili = false;
                PrototypeCameraRig.SuppressLookAheadDev = false;
                InputSourceRegistry.SetCurrent(previousInput);
                WeaponRegistry.SelectedWeapon = previousSelected;   // EquipClassic writes the global selection
                Time.captureDeltaTime = 0f;
                target.Release(); Object.Destroy(target); Object.Destroy(read);
                if (PlayerRegistry.Primary != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }
        }

        private static Vector2 Px(Camera cam, Vector3 world)
        {
            Vector3 v = cam.WorldToViewportPoint(world);
            return new Vector2(v.x * W, v.y * H);
        }

        private static Color32[] Grab(Camera cam, RenderTexture target, Texture2D read, TracerRibbonRenderer tracer, bool tracers)
        {
            RenderTexture old = cam.targetTexture;
            QueueTracers(tracer, tracers);   // DrawMeshInstanced from LateUpdate is not in a test's Camera.Render (see Wp095)
            cam.targetTexture = target;
            cam.Render();
            cam.targetTexture = old;
            RenderTexture prev = RenderTexture.active; RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply();
            RenderTexture.active = prev;
            return read.GetPixels32();
        }

        // the band from the muzzle (a) to 1 m short of the target (b), half-width hw px
        private static void Measure(Color32[] lit, Color32[] dark, Vector2 a, Vector2 b, float hw, out int streak, out float orange, out float width, out float warm)
        {
            Vector2 ab = b - a; float len = ab.magnitude; streak = 0; orange = 0f; width = 0f; warm = 0f;
            if (len < 10f) return;
            Vector2 dir = ab / len, side = new Vector2(-dir.y, dir.x);
            int orangeN = 0, warmN = 0;
            var widths = new System.Collections.Generic.List<float>();
            for (float t = 0f; t < len; t += 1f)   // the dark frame differs only in the tracer layers: the hit card cancels out
            {
                int across = 0;
                for (float u = -hw; u <= hw; u += 1f)
                {
                    Vector2 q = a + dir * t + side * u;
                    int x = (int)q.x, y = (int)q.y;
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    int i = y * W + x;
                    Color c = lit[i], d = dark[i];
                    // the tracer layers' own pixels: any channel moved ≥ 0.10 (v2 asked for ≥ 0.12 more luminance - on the light stone
                    // the premultiplied glow darkens the ground, so the R3 tracer measured ≈ 3 px while the frame showed a 280 px streak)
                    if (Mathf.Max(Mathf.Abs(c.r - d.r), Mathf.Max(Mathf.Abs(c.g - d.g), Mathf.Abs(c.b - d.b))) < .10f) continue;
                    streak++; across++;
                    Color.RGBToHSV(c, out float h, out float sat, out _);
                    if (sat >= .45f && h * 360f >= 8f && h * 360f <= 34f) orangeN++;
                    // v5: the R3 streak measured hue 36–42° at S ≥ 0.45 (saturated amber) - the "too orange" the user saw; 8–34° missed it
                    if (sat >= .45f && h * 360f >= 8f && h * 360f <= 50f) warmN++;
                }
                if (across > 0 && ((int)t % 4) == 0) widths.Add(across);
            }
            orange = streak > 0 ? orangeN / (float)streak : 0f;
            warm = streak > 0 ? warmN / (float)streak : 0f;
            if (widths.Count > 0) { widths.Sort(); width = widths[widths.Count / 2]; }
        }
    }
}
