using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-109 (R4, user note 10) · the top-down crown reveal in the Run scene, measured and filmed. The crowns are found from the
    /// scene itself (DD/Foliage Wind cutout renderers over 2 m, ≥ 2.5 m wide, the NavMesh under their centre); the hero is stood under
    /// each and three frames are rendered from the game camera in one frame: ON, OFF (the strength zeroed - the main build) and with
    /// the crown renderers shadow-only (the hero as the crowns would leave him). Inside a disc round the hero's chest (the
    /// component's radius projected) a pixel is "covered" when OFF differs from the bare frame (crowns shadow-only, so their shade stays); the reveal's share is the covered
    /// pixels that ON shows bare. Outside 2 × the disc nothing may change (a crown that hides nothing stays as authored). The three
    /// crowns that cover the hero most are reported; then a walk under the best one, OFF and ON, at 30 fps for the video.
    /// Output DD_TD_AGAC_DIR (else the temp cache): before/after PNGs (n-once / n-sonra), walk frames + frames.tsv (tools/td-capture/
    /// tdmux), agac.json. Asserts the recorded measures: ≥ 1 covering crown, the reveal's share ≥ 0.25 on it, and ≤ 0.2 % changed
    /// pixels outside the disc over the render-to-render noise floor (OFF rendered twice in the same frame).
    /// </summary>
    [TestFixture]
    [Category("Diagnostics")]
    public sealed class Wp109AgacSaydamlikTdTests
    {
        private const int W = 1920, H = 1080;
        private const float Diff = .06f;          // per-channel max difference that counts as "changed" (sRGB 0..1)
        private const int MaxCandidates = 48;

        private sealed class Crown
        {
            public Renderer Renderer;
            public Vector3 Stand;
            public int Covered;
            public float Share;
            public float OutsideChanged;
            public float OutsideNoise;
            public string Name;
        }

        [UnityTest]
        [Explicit("R4 görsel kanıt + ölçüm: yalnız hedefli koşulur")]
        [Timeout(900000)]
        public IEnumerator Agac_Ustten_TacKahramaniOrtunceSeyrelir_OlcumVeKare()
        {
            string dir = System.Environment.GetEnvironmentVariable("DD_TD_AGAC_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp109-td-agac");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "walk"));
            LogAssert.ignoreFailingMessages = true;
            IInputSource previousInput = InputSourceRegistry.Current;

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
            var girdi = new SavasTestGirdisi();
            InputSourceRegistry.SetCurrent(girdi); rig.Motor.SetInputSource(null); rig.Aim.SetInputSource(null);
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            PrototypeCameraRig.SuppressLookAheadDev = true;
            var target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Wp109TdAgac" };
            var read = new Texture2D(W, H, TextureFormat.RGB24, false);
            var crowns = new List<Renderer>();
            try
            {
                for (int i = 0; i < 30; i++) yield return null;
                PrototypeCameraRig camRig = PrototypeCameraRig.Active;
                TreeCanopyReveal reveal = camRig.GetComponent<TreeCanopyReveal>();
                Assert.IsNotNull(reveal, "TreeCanopyReveal kamera düzeneğinde yok");
                Assert.AreEqual(1f, reveal.Strength, 1e-3f, "TD'de güç 1");

                // the scene's crowns
                foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
                {
                    Material m = r.sharedMaterial;
                    if (!r.enabled || m == null || m.shader == null || m.shader.name != "DD/Foliage Wind" || !m.IsKeywordEnabled("_ALPHATEST_ON")) continue;
                    Bounds b = r.bounds;
                    if (b.min.y < rig.transform.position.y + 2f || Mathf.Max(b.size.x, b.size.z) < 2.5f) continue;
                    crowns.Add(r);
                }
                Assert.Greater(crowns.Count, 0, "sahnede taç bulunamadı");

                // candidates: a crown with the NavMesh under its centre, ≥ 8 m from every earlier one
                var candidates = new List<Crown>();
                foreach (Renderer r in crowns)
                {
                    Vector3 c = r.bounds.center;
                    if (!NavMesh.SamplePosition(new Vector3(c.x, rig.transform.position.y, c.z), out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                    bool near = false;
                    foreach (Crown k in candidates) near |= (k.Stand - hit.position).sqrMagnitude < 64f;
                    if (near) continue;
                    candidates.Add(new Crown { Renderer = r, Stand = hit.position, Name = r.name });
                    if (candidates.Count >= MaxCandidates) break;
                }
                Assert.Greater(candidates.Count, 0, "altına girilebilen taç yok");

                Color32[] off = null, on = null, bare = null, off2 = null;
                var candidateLog = new StringBuilder();
                Camera cam = camRig.Camera;
                foreach (Crown k in candidates)
                {
                    Stand(rig, camRig, k.Stand);
                    for (int i = 0; i < 4; i++) yield return null;
                    camRig.SnapTopDownDev();
                    yield return null;
                    Vector3 chest = rig.transform.position + Vector3.up * TreeCanopyReveal.FocusHeight;
                    float discPx = DiscPixels(cam, chest);
                    Vector2 centre = ToPx(cam, chest);
                    for (int i = 0; i < 20; i++) yield return null;   // eased in (0.15 s)
                    Assert.AreEqual(1f, reveal.Strength, 1e-3f, "güç 1");
                    // the three frames in ONE frame (the wind moves the cards between frames): ON as the game draws it, OFF with the
                    // component disabled (the authored crowns), bare with the crowns shadow-only (their shade stays)
                    on = Grab(cam, target, read);
                    reveal.enabled = false;   // the global is written per camera; disabled = the authored crowns (strength kept)
                    off = Grab(cam, target, read);
                    off2 = Grab(cam, target, read);   // the render-to-render noise floor (same frame, same state)
                    SetCrowns(crowns, false);
                    bare = Grab(cam, target, read);
                    SetCrowns(crowns, true);
                    reveal.enabled = true;
                    Measure(off, on, bare, centre, discPx, out k.Covered, out k.Share, out k.OutsideChanged);
                    Measure(off, off2, bare, centre, discPx, out _, out _, out k.OutsideNoise);
                    candidateLog.Append("    {\"crown\": \"").Append(k.Name).Append("\", \"stand\": [").Append(F(k.Stand.x)).Append(", ").Append(F(k.Stand.z))
                        .Append("], \"coveredPx\": ").Append(k.Covered).Append(", \"revealShare\": ").Append(F(k.Share))
                        .Append(", \"outsideChanged\": ").Append(F(k.OutsideChanged)).Append(", \"outsideNoise\": ").Append(F(k.OutsideNoise)).Append("},\n");
                }
                candidates.Sort((a, b) => b.Covered.CompareTo(a.Covered));
                int report = Mathf.Min(3, candidates.Count);
                var log = new StringBuilder("{\n  \"crownsInScene\": " + crowns.Count + ",\n  \"candidates\": " + candidates.Count + ",\n");
                log.Append("  \"shape\": {\"radius\": ").Append(F(TreeCanopyReveal.Radius)).Append(", \"softRim\": ").Append(F(TreeCanopyReveal.SoftRim))
                   .Append(", \"dropped\": ").Append(F(TreeCanopyReveal.DroppedShare)).Append(", \"minHeight\": ").Append(F(TreeCanopyReveal.MinHeightOverFocus)).Append("},\n  \"poses\": [\n");
                float worstOutside = 0f;
                foreach (Crown k in candidates) worstOutside = Mathf.Max(worstOutside, k.OutsideChanged - k.OutsideNoise);
                for (int n = 0; n < report; n++)
                {
                    Crown k = candidates[n];
                    Stand(rig, camRig, k.Stand);
                    for (int i = 0; i < 4; i++) yield return null;
                    camRig.SnapTopDownDev();
                    for (int i = 0; i < 20; i++) yield return null;
                    // one frame: after (as drawn), then before (the strength zeroed by hand)
                    Save(Grab(cam, target, read), read, Path.Combine(dir, (n + 1) + "-sonra.png"));
                    reveal.enabled = false;
                    Save(Grab(cam, target, read), read, Path.Combine(dir, (n + 1) + "-once.png"));
                    reveal.enabled = true;
                    log.Append("    {\"pose\": ").Append(n + 1).Append(", \"crown\": \"").Append(k.Name).Append("\", \"stand\": [").Append(F(k.Stand.x)).Append(", ").Append(F(k.Stand.z))
                       .Append("], \"coveredPx\": ").Append(k.Covered).Append(", \"revealShare\": ").Append(F(k.Share)).Append(", \"outsideChanged\": ").Append(F(k.OutsideChanged)).Append("},\n");
                }
                log.Append("    {}\n  ],\n  \"worstOutsideChangedOverNoiseAllCandidates\": ").Append(F(worstOutside)).Append(",\n  \"all\": [\n")
                   .Append(candidateLog).Append("    {}\n  ],\n");

                // the walk under the best crown: 7 m before it to 7 m past it along the camera's right, OFF then ON
                Crown best = candidates[0];
                Vector3 right = camRig.PlanarRight;
                var tsv = new StringBuilder();
                int frameIndex = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    reveal.enabled = pass == 1;
                    Vector3 start = best.Stand - right * 7f;
                    if (NavMesh.SamplePosition(start, out NavMeshHit s, 3f, NavMesh.AllAreas)) start = s.position;
                    Stand(rig, camRig, start);
                    for (int i = 0; i < 20; i++) yield return null;
                    camRig.SnapTopDownDev();
                    girdi.Yon = new Vector2(1f, 0f);   // camera right
                    for (int f = 0; f < 300; f++)
                    {
                        girdi.Imlec = ToPx(cam, rig.transform.position + right * 4f + Vector3.up * 1.2f);
                        yield return null;
                        if ((f & 1) == 0)
                        {
                            Grab(cam, target, read);
                            File.WriteAllBytes(Path.Combine(dir, "walk", "f-" + frameIndex.ToString("000000", CultureInfo.InvariantCulture) + ".jpg"), read.EncodeToJPG(92));
                            tsv.Append(frameIndex).Append('\t').Append(F(frameIndex / 30f)).Append('\n');
                            frameIndex++;
                        }
                        if (Vector3.Dot(rig.transform.position - best.Stand, right) > 7f) break;
                    }
                    girdi.Yon = Vector2.zero;
                    for (int f = 0; f < 15; f++)   // a half-second hold between the passes
                    {
                        yield return null; yield return null;
                        Grab(cam, target, read);
                        File.WriteAllBytes(Path.Combine(dir, "walk", "f-" + frameIndex.ToString("000000", CultureInfo.InvariantCulture) + ".jpg"), read.EncodeToJPG(92));
                        tsv.Append(frameIndex).Append('\t').Append(F(frameIndex / 30f)).Append('\n');
                        frameIndex++;
                    }
                }
                File.WriteAllText(Path.Combine(dir, "walk", "frames.tsv"), tsv.ToString());
                log.Append("  \"walkFrames\": ").Append(frameIndex).Append(",\n  \"camera\": \"TopDown\"\n}\n");
                File.WriteAllText(Path.Combine(dir, "agac.json"), log.ToString());
                Debug.Log("[DD-TEST] wp109.td-agac dir=" + dir + " best=" + best.Name + " share=" + F(best.Share) + " covered=" + best.Covered + " outside=" + F(worstOutside));

                Assert.Greater(best.Covered, 200, "kahramanı örten taç yok (kapsanan piksel)");
                // r1 (DroppedShare .65): 0.29–0.30 measured on the three densest crowns - stacked cards and the soft rim keep part of
                // the disc covered; the floor guards the mechanism (OFF reveals 0 by definition), the look is the AD's
                Assert.GreaterOrEqual(best.Share, .25f, "taç altında kahramanın açılan payı");
                // r2: ON vs OFF outside the disc measured 0.7–1.3 % on every candidate while the same-frame stills differ 0.06–0.18 %
                // there, scattered - the manual renders are not bit-identical; the reveal's own share is the excess over that floor
                Assert.LessOrEqual(worstOutside, .002f, "disk dışında değişen piksel payı (gürültü tabanı üstü)");
                Assert.Greater(frameIndex, 60, "yürüyüş kareleri");
            }
            finally
            {
                SetCrowns(crowns, true);
                girdi.Yon = Vector2.zero;
                PrototypeCameraRig.SuppressLookAheadDev = false;
                InputSourceRegistry.SetCurrent(previousInput);
                Time.captureDeltaTime = 0f;
                target.Release(); Object.Destroy(target); Object.Destroy(read);
                if (PlayerRegistry.Primary != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }
        }

        private static void Stand(PlayerRig rig, PrototypeCameraRig camRig, Vector3 at)
        {
            Vector3 toCamera = -camRig.PlanarForward;
            rig.Teleport(at, toCamera.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toCamera, Vector3.up) : Quaternion.identity);
            camRig.SnapTopDownDev();
        }

        private static readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> s_shadow = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();

        // off = shadow-only (the crown's shade on the hero and the ground stays; only the crown itself leaves the frame)
        private static void SetCrowns(List<Renderer> crowns, bool on)
        {
            foreach (Renderer r in crowns)
            {
                if (r == null) continue;
                if (!on)
                {
                    if (!s_shadow.ContainsKey(r)) s_shadow[r] = r.shadowCastingMode;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
                else if (s_shadow.TryGetValue(r, out UnityEngine.Rendering.ShadowCastingMode mode)) r.shadowCastingMode = mode;
            }
            if (on) s_shadow.Clear();
        }

        // capture-pixel coordinates (y up, as ReadPixels)
        private static Vector2 ToPx(Camera cam, Vector3 world)
        {
            Vector3 s = cam.WorldToViewportPoint(world);
            return new Vector2(s.x * W, s.y * H);
        }

        // the component's reveal radius at the focus, in capture pixels
        private static float DiscPixels(Camera cam, Vector3 chest)
        {
            Vector2 a = ToPx(cam, chest), b = ToPx(cam, chest + cam.transform.right * TreeCanopyReveal.Radius);
            return (b - a).magnitude;
        }

        private static Color32[] Grab(Camera cam, RenderTexture target, Texture2D read)
        {
            RenderTexture old = cam.targetTexture;
            cam.targetTexture = target;
            cam.Render();
            cam.targetTexture = old;
            RenderTexture prev = RenderTexture.active; RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply();
            RenderTexture.active = prev;
            return read.GetPixels32();
        }

        private static void Save(Color32[] px, Texture2D read, string path)
        {
            read.SetPixels32(px); read.Apply();
            File.WriteAllBytes(path, read.EncodeToPNG());
        }

        private static bool Same(Color32 a, Color32 b) =>
            Mathf.Abs(a.r - b.r) <= Diff * 255f && Mathf.Abs(a.g - b.g) <= Diff * 255f && Mathf.Abs(a.b - b.b) <= Diff * 255f;

        // covered: OFF ≠ bare inside the disc (the soft rim's inner 85 %); share: of those, ON = bare. Outside 2 × the disc: ON ≠ OFF.
        private static void Measure(Color32[] off, Color32[] on, Color32[] bare, Vector2 centre, float discPx, out int covered, out float share, out float outside)
        {
            float inner = discPx * .85f, outer = discPx * 2f;
            int shown = 0, outsideN = 0, outsideChanged = 0;
            covered = 0;
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float dx = x + .5f - centre.x, dy = y + .5f - centre.y;
                    float d2 = dx * dx + dy * dy;
                    int i = y * W + x;
                    if (d2 <= inner * inner)
                    {
                        if (Same(off[i], bare[i])) continue;
                        covered++;
                        if (Same(on[i], bare[i])) shown++;
                    }
                    else if (d2 > outer * outer)
                    {
                        outsideN++;
                        if (!Same(on[i], off[i])) outsideChanged++;
                    }
                }
            }
            share = covered > 0 ? shown / (float)covered : 0f;
            outside = outsideN > 0 ? outsideChanged / (float)outsideN : 0f;
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
