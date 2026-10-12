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
                    var p90s = new System.Collections.Generic.List<float>();
                    var g = new GroundSums[3];   // AD r1: the light stone and the wet slate apart
                    for (int k = 1; k < 3; k++) g[k] = new GroundSums();
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
                        frames++; streakSum += streak; orangeSum += orange; warmSum += warm; widths.Add(width); p90s.Add(s_lastP90);
                        for (int k = 1; k < 3; k++)
                        {
                            Measure(lit, dark, a, b, metre * .6f, k, out int gs, out _, out float gw, out float gwarm);
                            if (gs <= 0) continue;
                            g[k].Frames++; g[k].Streak += gs; g[k].Warm += gwarm; g[k].Widths.Add(gw);
                        }
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
                       // r6: the median mixes the thin body / tail-out columns with the streak - p90 is the streak's own width
                       .Append(", \"medianOfFrameP90WidthPx\": ").Append(Median(p90s).ToString("0.#", CultureInfo.InvariantCulture))
                       .Append(", \"pxPerMetre\": ").Append(metre.ToString("0.#", CultureInfo.InvariantCulture))
                       .Append(", \"lightStone\": ").Append(g[1].Json()).Append(", \"wetSlate\": ").Append(g[2].Json()).Append("},\n");
                    if (z != null) Object.Destroy(z.gameObject);
                }
                girdi.AtesBasili = false;

                // AD r1 M7: the AR again with Reduce Flashes on (FlashScale 0.4) - its fullest frame beside the normal one
                float flashScale = GameFeelSettings.FlashScale;
                try
                {
                    GameFeelSettings.FlashScale = .4f;
                    EnemyFactory.ClearAll();
                    weapon.EquipClassic(ClassicWeaponId.AssaultRifle);
                    for (int i = 0; i < 60; i++) yield return null;
                    Vector3 p = rig.transform.position;
                    ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, p + Vector3.right * 9f, Quaternion.LookRotation(Vector3.left));
                    if (z != null) z.enabled = false;
                    int best = -1; Color32[] bestPx = null;
                    for (int f = 0; f <= 48; f++)
                    {
                        Vector3 chest = z != null ? z.transform.position + Vector3.up * 1.2f : p + Vector3.right * 9f + Vector3.up * 1.2f;
                        Vector3 sc = cam.WorldToScreenPoint(chest);
                        girdi.Imlec = new Vector2(sc.x, sc.y);
                        girdi.AtesBasili = f < 48;
                        yield return null;
                        if ((f & 1) != 0) continue;
                        Color32[] dark = Grab(cam, target, read, tracer, false);
                        Color32[] lit = Grab(cam, target, read, tracer, true);
                        Measure(lit, dark, Px(cam, rig.transform.position + Vector3.up * 1.4f), Px(cam, chest), 57f, out int st, out _, out _, out _);
                        if (st > best) { best = st; bestPx = lit; }
                    }
                    girdi.AtesBasili = false;
                    if (bestPx != null) { read.SetPixels32(bestPx); read.Apply(); File.WriteAllBytes(Path.Combine(dir, "AssaultRifle-reduceflashes.png"), read.EncodeToPNG()); }
                    log.Append("    {\"weapon\": \"AssaultRifle-reduceFlashes\", \"maxStreakPx\": ").Append(best).Append("},\n");
                    if (z != null) Object.Destroy(z.gameObject);
                }
                finally { GameFeelSettings.FlashScale = flashScale; }

                // AD r1 M6: the AP-041 holes as the TD camera sees them - stone / wood / metal on the ground beside the hero (a medium
                // round), 4× crops and the mark's outer size in pixels (the changed region against the frame before the impact)
                var vfx = HitFeedbackDirector.Instance != null ? HitFeedbackDirector.Instance.Vfx : null;
                var holeLog = new StringBuilder();
                if (vfx != null)
                {
                    SurfaceKind[] kinds = { SurfaceKind.Stone, SurfaceKind.Wood, SurfaceKind.Metal, SurfaceKind.Dirt };   // Dirt = the AP-010 earth mark (control)
                    for (int k = 0; k < kinds.Length; k++)
                    {
                        Vector3 at = rig.transform.position + cam.transform.right * (2.5f + 1.5f * k) - Vector3.ProjectOnPlane(cam.transform.up, Vector3.up).normalized * 1.5f;
                        if (Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out RaycastHit gh, 10f, ~0, QueryTriggerInteraction.Ignore)) at = gh.point;
                        at += Vector3.up * .06f;   // sonra-r12: the plaza's visual tiles sit over its collider - a hole on the collider hid under them
                        for (int i = 0; i < 3; i++) yield return null;
                        Color32[] before = Grab(cam, target, read, tracer, false);
                        int decals0 = vfx.DecalsPlayed;
                        bool played = vfx.PlaySurfaceImpact(kinds[k], at, Vector3.up, 1f);
                        vfx.RefineSurfaceImpact(at, kinds[k], DD.Prototype.Core.Weapons.WeaponCaliber.Medium);
                        for (int i = 0; i < 45; i++) yield return null;   // the card and its dust are gone, the hole stays
                        Color32[] after = Grab(cam, target, read, tracer, false);
                        Vector2 c = Px(cam, at);
                        int outer = HoleCrop(before, after, c, Path.Combine(dir, "delik-" + kinds[k] + "-x4.png"));
                        float metreH = (Px(cam, at + cam.transform.right) - c).magnitude;
                        // diagnostics (sonra-r4: no mark in the crops): the nearest spawned decal, its size, row and renderer
                        DD.Prototype.Feedback.Vfx.DecalEffect near = null; float nd = float.MaxValue;
                        foreach (DD.Prototype.Feedback.Vfx.DecalEffect d in vfx.DecalPool.Spawned)
                        {
                            float dd = (d.transform.position - at).sqrMagnitude;
                            if (dd < nd) { nd = dd; near = d; }
                        }
                        Renderer nr = near != null && near.Quad != null ? near.Quad.GetComponent<Renderer>() : null;
                        holeLog.Append(k == 0 ? "" : ", ").Append("{\"played\": ").Append(played ? "true" : "false").Append(", \"decalsPlayed\": ").Append(vfx.DecalsPlayed - decals0)
                               .Append(", \"nearestM\": ").Append(Mathf.Sqrt(nd).ToString("0.###", CultureInfo.InvariantCulture))
                               .Append(", \"row\": \"").Append(near != null && near.Row != null ? near.Row.Id : "-").Append("\", \"size\": ")
                               .Append(near != null ? near.CurrentSize.ToString("0.###", CultureInfo.InvariantCulture) : "0")
                               .Append(", \"rendererOn\": ").Append(nr != null && nr.enabled ? "true" : "false")
                               .Append(", \"isVisible\": ").Append(nr != null && nr.isVisible ? "true" : "false")
                               .Append(", \"material\": \"").Append(nr != null && nr.sharedMaterial != null ? nr.sharedMaterial.name + "/" + nr.sharedMaterial.shader.name : "-").Append('"')
                               .Append(", \"quadUp\": \"").Append(near != null && near.Quad != null ? near.Quad.forward.ToString("F2") : "-").Append('"')
                               .Append(", \"boundsY\": ").Append(nr != null ? nr.bounds.center.y.ToString("0.###", CultureInfo.InvariantCulture) : "0")
                               .Append(", \"scale\": \"").Append(near != null && near.Quad != null ? near.Quad.lossyScale.ToString("F3") : "-").Append('"')
                               .Append(", \"atY\": ").Append(at.y.ToString("0.##", CultureInfo.InvariantCulture))
                               // the mark's outer size on screen = its drawn size × px/m (the crop diff caught 45 frames of wind)
                               .Append(", \"drawnOuterPx\": ").Append(near != null ? (near.CurrentSize * metreH).ToString("0.#", CultureInfo.InvariantCulture) : "0").Append(", ");
                        holeLog.Append("\"surface\": \"").Append(kinds[k]).Append("\", \"outerPx\": ").Append(outer)
                               .Append(", \"pxPerMetre\": ").Append(metreH.ToString("0.#", CultureInfo.InvariantCulture)).Append('}');
                    }
                }
                log.Append("    {\"holes\": [").Append(holeLog).Append("]},\n");
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

        private sealed class GroundSums
        {
            public int Frames; public long Streak; public float Warm;
            public readonly System.Collections.Generic.List<float> Widths = new System.Collections.Generic.List<float>();
            public string Json()
            {
                Widths.Sort();
                return "{\"framesWithStreak\": " + Frames + ", \"meanStreakPx\": " + (Frames > 0 ? Streak / Frames : 0)
                    + ", \"meanSaturatedWarmShare\": " + (Frames > 0 ? Warm / Frames : 0f).ToString("0.###", CultureInfo.InvariantCulture)
                    + ", \"medianCoreWidthPx\": " + (Widths.Count > 0 ? Widths[Widths.Count / 2] : 0f).ToString("0.#", CultureInfo.InvariantCulture) + "}";
            }
        }

        // a 32×32 px crop round c, scaled ×4 (nearest) to a PNG; returns the changed region's larger side (any channel ≥ 0.10)
        private static int HoleCrop(Color32[] before, Color32[] after, Vector2 c, string path)
        {
            const int R = 16;
            var tex = new Texture2D(R * 2 * 4, R * 2 * 4, TextureFormat.RGB24, false);
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int y = -R; y < R; y++)
                for (int x = -R; x < R; x++)
                {
                    int px = Mathf.Clamp((int)c.x + x, 0, W - 1), py = Mathf.Clamp((int)c.y + y, 0, H - 1), i = py * W + px;
                    Color32 a = after[i], b = before[i];
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++) tex.SetPixel((x + R) * 4 + sx, (y + R) * 4 + sy, a);
                    if (Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))) < 26) continue;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            return maxX < minX ? 0 : Mathf.Max(maxX - minX + 1, maxY - minY + 1);
        }

        private static float Median(System.Collections.Generic.List<float> v)
        {
            if (v.Count == 0) return 0f;
            v.Sort();
            return v[v.Count / 2];
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
        // r6: the frame's 90th-percentile column width (the streak's own width; the median mixes in the thin body / tail-out columns)
        private static float s_lastP90;

        private static void Measure(Color32[] lit, Color32[] dark, Vector2 a, Vector2 b, float hw, out int streak, out float orange, out float width, out float warm)
            => Measure(lit, dark, a, b, hw, 0, out streak, out orange, out width, out warm);

        // ground 0 = every band pixel; 1 = the light stone (the dark frame's luminance ≥ 0.55); 2 = the wet slate (≤ 0.40) - AD r1
        private static void Measure(Color32[] lit, Color32[] dark, Vector2 a, Vector2 b, float hw, int ground, out int streak, out float orange, out float width, out float warm)
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
                    if (ground != 0)
                    {
                        float ld = .2126f * d.r + .7152f * d.g + .0722f * d.b;
                        if (ground == 1 ? ld < .55f : ld > .40f) continue;
                    }
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
            if (widths.Count > 0) { widths.Sort(); width = widths[widths.Count / 2]; s_lastP90 = widths[(int)(widths.Count * .9f)]; }
            else s_lastP90 = 0f;
        }
    }
}
