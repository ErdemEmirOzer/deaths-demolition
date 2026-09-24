using System.Collections;
using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using Unity.Profiling;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 slice 4 · DD/UI/Fx and the frozen frame in a live URP frame:
    ///   1. `_ClipRect` (RectMask2D) and CanvasGroup alpha behave as on UI-Default; an ink reveal held at t=0 draws
    ///      nothing, at t=1 draws the element; reduced motion shows the end state at once.
    ///   2. Batching: 12 Fx elements in six modes cost at most one SetPass / batch more than 12 default images.
    ///   3. 16 animating Fx elements + 18 sparks: 0 B managed allocation per frame (the GPU owns the timeline).
    ///   4. UiFrozenFrame: one capture per hold (¼ resolution, non-black), 0 captures on later frames, owners stack.
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiFxSliceTests
    {
        private const int UiLayer = 5;
        private const string TestHold = "wp068-frozen-test";   // Pause / the shop freeze the run clock; the frozen frame is only legitimate then
        private readonly List<Object> _temp = new List<Object>();
        private int _fpsWas;

        [SetUp]
        public void SetUp()
        {
            _fpsWas = Application.targetFrameRate;
            Application.targetFrameRate = -1;   // ProfilerRecorder LastValue stays 0 under a cap in the Editor runner (S-02 F3)
            UiFxGlobals.EnsureInstalled();
        }

        [TearDown]
        public void TearDown()
        {
            UiFxGlobals.TimeOverride = null;
            UiFxGlobals.MotionOverride = null;
            UiFxGlobals.FlashOverride = null;
            UiFrozenFrame.ReleaseAll();
            UiFrozenFrame.CameraOverride = null;
            RunFlowClock.RemoveHold(TestHold);
            foreach (Object o in _temp) if (o != null) Object.Destroy(o);
            _temp.Clear();
            Application.targetFrameRate = _fpsWas;
        }

        // ------------------------------------------------------------------ 1 · clip rect, canvas group, ink reveal, RM

        [UnityTest]
        public IEnumerator ClipRect_CanvasGroup_InkReveal_ReducedMotion()
        {
            RenderTexture rt = NewTarget(256, 128);
            Camera cam = NewUiCamera(rt, false);
            Canvas canvas = NewCanvas(cam);
            var root = (RectTransform)canvas.transform;

            // Left half: RectMask2D over the left quarter; the Fx image spans the whole half → only the quarter shows.
            RectTransform maskRect = Rect("Mask", root, new Vector2(0f, 0f), new Vector2(0.25f, 1f));
            maskRect.gameObject.AddComponent<RectMask2D>();
            Image clipped = Img("Clipped", maskRect, Color.white);
            ((RectTransform)clipped.transform).anchorMax = new Vector2(2f, 1f);   // twice the mask's width
            clipped.gameObject.AddComponent<UiFx>().Configure(UiFxMode.InkReveal, 0.5f, Vector3.zero);

            // Right half: CanvasGroup 0.5 over an Fx image at its end state.
            RectTransform groupRect = Rect("Group", root, new Vector2(0.5f, 0f), new Vector2(1f, 1f));
            groupRect.gameObject.AddComponent<CanvasGroup>().alpha = 0.5f;
            Image faded = Img("Faded", groupRect, Color.white);
            faded.gameObject.AddComponent<UiFx>().Configure(UiFxMode.InkReveal, 0.5f, Vector3.zero);

            yield return Render(cam);
            Color[] px = Read(rt);
            Assert.Greater(At(px, rt, 0.12f, 0.5f).r, 0.9f, "maskenin içi çizilmeli");
            Assert.Less(At(px, rt, 0.37f, 0.5f).r, 0.02f, "RectMask2D dışı kırpılmalı (_ClipRect)");
            float half = At(px, rt, 0.75f, 0.5f).r;
            Assert.That(half, Is.InRange(0.18f, 0.80f), "CanvasGroup 0.5 yarı saydam çizilmeli (sRGB/lineer kodlamaya göre 0.21–0.73): " + half);

            // Ink reveal: held at t=0 → nothing; t=1 → full; reduced motion shows the end state even when held.
            UiFx inkLeft = clipped.GetComponent<UiFx>();
            inkLeft.Hold();
            yield return Render(cam);
            Assert.Less(At(Read(rt), rt, 0.12f, 0.5f).a + At(Read(rt), rt, 0.12f, 0.5f).r, 0.02f, "t=0'da mürekkep görünmemeli");

            UiFxGlobals.MotionOverride = false;   // reduced motion
            yield return Render(cam);
            Assert.Greater(At(Read(rt), rt, 0.12f, 0.5f).r, 0.9f, "azaltılmış harekette son durum anında");
            UiFxGlobals.MotionOverride = null;

            UiFxGlobals.TimeOverride = 500f;
            inkLeft.Play();
            UiFxGlobals.TimeOverride = 500f + 0.5f + 0.01f;   // past the duration
            yield return Render(cam);
            Assert.Greater(At(Read(rt), rt, 0.12f, 0.5f).r, 0.9f, "t=1'de tamamı mürekkepli");

            // Early in the stroke: part of the visible quarter revealed (brush inks left → right).
            UiFxGlobals.TimeOverride = 500f + 0.1f;
            yield return Render(cam);
            Color[] mid = Read(rt);
            int lit = 0, total = 0;
            for (int x = 2; x < rt.width / 4 - 2; x++) { total++; if (At(mid, rt, (x + 0.5f) / rt.width, 0.5f).r > 0.5f) lit++; }
            Assert.That(lit, Is.InRange(1, total - 1), "yarı yolda kısmi açılma: " + lit + "/" + total);

            // Slice 4c (AP-012 v004 kit): the mark's shape clips — outside the underline stroke (its time map = 1.0) the
            // finished reveal stays empty; wipe mode (prm.y = 1) inks the whole rect.
            UiFxGlobals.TimeOverride = 500f + 0.5f + 0.01f;
            yield return Render(cam);
            Assert.Less(At(Read(rt), rt, 0.01f, 0.5f).r, 0.02f, "işaret kipinde fırça izinin dışı boş kalmalı");
            inkLeft.Configure(UiFxMode.InkReveal, 0.5f, new Vector3(0f, 1f, 0f));
            yield return Render(cam);
            Assert.Greater(At(Read(rt), rt, 0.01f, 0.5f).r, 0.9f, "silme kipinde tüm dikdörtgen mürekkeplenir");
        }

        // ------------------------------------------------------------------ 1b · particles are drawn (quads expand in the vertex stage)

        [UnityTest]
        public IEnumerator Parcaciklar_KivilcimOrtaOmurde_Cizilir_OmurSonundaSoner()
        {
            RenderTexture rt = NewTarget(256, 256);
            Camera cam = NewUiCamera(rt, false);
            Canvas canvas = NewCanvas(cam);
            RectTransform area = Rect("Burst", (RectTransform)canvas.transform, Vector2.zero, Vector2.one);
            UiFxParticles sparks = area.gameObject.AddComponent<UiFxParticles>();
            sparks.color = Color.white;
            sparks.Configure(18, 10f, 200f, 0f, 0.3f);
            UiFxGlobals.TimeOverride = 200f;
            sparks.Play();

            UiFxGlobals.TimeOverride = 200.12f;   // mid-life
            yield return Render(cam);
            int lit = Lit(Read(rt));
            UiFxGlobals.TimeOverride = 200.5f;    // past the 0.3 s life
            yield return Render(cam);
            int after = Lit(Read(rt));
            Debug.Log("[DD-TEST] wp068.particles midLife=" + lit + " afterLife=" + after);
            Assert.Greater(lit, 20, "kıvılcımlar çizilmedi (dejenere quad?)");
            Assert.AreEqual(0, after, "ömür bitince kıvılcım kalmamalı");
        }

        [UnityTest]
        public IEnumerator Parcaciklar_BaslangicYaricapi_HalkadanDisari()
        {
            // E18b (AD 8c SHOULD-2): a burst with startRadius leaves the ring's stroke, not the centre over the timer.
            RenderTexture rt = NewTarget(256, 256);
            Camera cam = NewUiCamera(rt, false);
            Canvas canvas = NewCanvas(cam);
            RectTransform area = Rect("Burst", (RectTransform)canvas.transform, Vector2.zero, Vector2.one);
            UiFxParticles sparks = area.gameObject.AddComponent<UiFxParticles>();
            sparks.color = Color.white;
            sparks.Configure(12, 10f, 200f, 0f, 0.3f, 0f, null, 66f);
            UiFxGlobals.TimeOverride = 300f;
            sparks.Play();

            UiFxGlobals.TimeOverride = 300.08f;   // early: every spark has left the start circle, none reached the centre
            yield return Render(cam);
            Color[] px = Read(rt);
            int centre = 0, ring = 0;
            for (int y = 0; y < rt.height; y++)
            {
                for (int x = 0; x < rt.width; x++)
                {
                    if (px[y * rt.width + x].r <= 0.2f) continue;
                    float d = new Vector2(x - rt.width * 0.5f, y - rt.height * 0.5f).magnitude;
                    if (d < 40f) centre++;
                    else if (d <= 110f) ring++;
                }
            }

            Debug.Log("[DD-TEST] e18b.sparks centre=" + centre + " ring=" + ring);
            Assert.AreEqual(0, centre, "kıvılcım merkezden (sayacın üstünden) çıkmamalı");
            Assert.Greater(ring, 20, "kıvılcımlar halka çizgisinden dışarı çizilmeli");
        }

        private static float RowRedness(Color[] px, RenderTexture rt, float v0, float v1)
        {
            float sum = 0f; int n = 0;
            for (int y = (int)(v0 * rt.height); y < (int)(v1 * rt.height); y++)
            {
                for (int x = 0; x < rt.width; x++) { Color c = px[y * rt.width + x]; sum += c.r - (c.g + c.b) * 0.5f; n++; }
            }

            return n > 0 ? sum / n : 0f;
        }

        private static int Lit(Color[] px)
        {
            int n = 0;
            foreach (Color c in px) if (c.r > 0.2f) n++;
            return n;
        }

        // ------------------------------------------------------------------ 2 · batching

        [UnityTest]
        public IEnumerator OnIkiFxOgesi_AltiMod_TekMateryal_BatchArtisi1()
        {
            RenderTexture rt = NewTarget(512, 256);
            Camera cam = NewUiCamera(rt, true);
            Canvas canvas = NewCanvas(cam);
            var root = (RectTransform)canvas.transform;

            var plain = new List<Image>();
            for (int i = 0; i < 12; i++)
            {
                Image img = Img("Plain" + i, root, Color.gray);
                Place(img, i);
                plain.Add(img);
            }

            using (var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count"))
            using (var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count"))
            {
                for (int i = 0; i < 4; i++) yield return null;
                long setPassPlain = setPass.LastValue, batchesPlain = batches.LastValue;

                UiFxMode[] modes = { UiFxMode.InkReveal, UiFxMode.Leaf, UiFxMode.Stamp, UiFxMode.Sheen, UiFxMode.SegmentBar, UiFxMode.SdfGlyph };
                for (int i = 0; i < plain.Count; i++)
                {
                    plain[i].gameObject.AddComponent<UiFx>().Configure(modes[i % modes.Length], 0.4f, new Vector3(0.5f, 4f, 0.7f));
                }

                for (int i = 0; i < 4; i++) yield return null;
                long setPassFx = setPass.LastValue, batchesFx = batches.LastValue;
                Debug.Log("[DD-TEST] wp068.batch plain setPass=" + setPassPlain + " batches=" + batchesPlain + " fx setPass=" + setPassFx + " batches=" + batchesFx);
                Assert.Greater(setPassPlain + batchesPlain, 0, "pozitif kontrol: kayıtçı kare sayıyor");
                Assert.LessOrEqual(setPassFx, setPassPlain + 1, "Fx tek materyal: SetPass artışı ≤ 1");
                Assert.LessOrEqual(batchesFx, batchesPlain + 1, "Fx tek materyal: batch artışı ≤ 1");
            }
        }

        // ------------------------------------------------------------------ 3 · zero allocation per frame

        [UnityTest]
        public IEnumerator Animasyonlu16Fx_Kivilcim_KareBasiSifirAyirma()
        {
            RenderTexture rt = NewTarget(512, 256);
            Camera cam = NewUiCamera(rt, true);
            Canvas canvas = NewCanvas(cam);
            var root = (RectTransform)canvas.transform;
            var fx = new List<UiFx>();
            for (int i = 0; i < 16; i++)
            {
                Image img = Img("Anim" + i, root, Color.white);
                Place(img, i);
                UiFx f = img.gameObject.AddComponent<UiFx>();
                f.Configure(i % 2 == 0 ? UiFxMode.InkReveal : UiFxMode.Leaf, 5f, new Vector3(1f, 0f, 0f), UiInkChannel.Underline, i % 2 == 0 ? 0f : 1.2f);
                fx.Add(f);
            }

            RectTransform burst = Rect("Burst", root, Vector2.zero, Vector2.one);
            UiFxParticles sparks = burst.gameObject.AddComponent<UiFxParticles>();
            sparks.Configure(18, 6f, 300f, 600f, 3f);
            yield return null;
            foreach (UiFx f in fx) f.Play();
            sparks.Play();
            for (int i = 0; i < 5; i++) yield return null;   // the one vertex rebuild per Play happens here

            UnityEngine.Profiling.Profiler.enabled = true;
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                // Positive control: the recorder must see a small managed allocation.
                var bait = new object[300];
                for (int i = 0; i < bait.Length; i++) bait[i] = new byte[80];
                yield return null;
                long control = bytes.LastValue;
                System.GC.KeepAlive(bait);

                // Baseline: the SAME canvas drawn as plain uGUI (Fx effects + sparks off) — whatever the runner, URP and
                // canvas rendering allocate per frame. Then the Fx frames; the median must not exceed the baseline's.
                foreach (UiFx f in fx) { f.enabled = false; f.GetComponent<Graphic>().material = null; }
                sparks.enabled = false;
                for (int i = 0; i < 5; i++) yield return null;
                var plain = new List<long>(40);
                for (int i = 0; i < 40; i++) { yield return null; plain.Add(bytes.LastValue); }
                long plainMedian = Median(plain, out long plainWorst);

                foreach (UiFx f in fx) f.enabled = true;
                sparks.enabled = true;
                for (int i = 0; i < 5; i++) yield return null;   // one re-batch after the switch
                var fxFrames = new List<long>(60);
                for (int i = 0; i < 60; i++) { yield return null; fxFrames.Add(bytes.LastValue); }
                long fxMedian = Median(fxFrames, out long fxWorst);

                Debug.Log("[DD-TEST] wp068.alloc control=" + control + " plain median/worst=" + plainMedian + "/" + plainWorst + " fx median/worst=" + fxMedian + "/" + fxWorst);
                Assert.Greater(control, 0, "pozitif kontrol: kayıtçı ayırmayı görmüyor");
                Assert.LessOrEqual(fxMedian, plainMedian, "animasyon GPU'da: Fx düz uGUI'ye göre kare başına 0 B ek ayırma");
            }
        }

        private static long Median(List<long> samples, out long worst)
        {
            samples.Sort();
            worst = samples[samples.Count - 1];
            return samples[samples.Count / 2];
        }

        // ------------------------------------------------------------------ 4 · frozen frame

        [UnityTest]
        public IEnumerator DonmusKare_TutmaBasinaTekYakalama_CeyrekCozunurluk_SahiplerYiginlanir()
        {
            Camera world = NewWorldCamera();
            yield return null;
            yield return null;

            RunFlowClock.AddHold(TestHold);
            int before = UiFrozenFrame.CaptureCount;
            UiFrozenFrame.Hold("test-a");
            for (int i = 0; i < 6 && !UiFrozenFrame.IsShowing; i++) yield return null;
            Assert.IsTrue(UiFrozenFrame.IsShowing, "donmuş kare katmanı görünmedi");
            Assert.AreEqual(before + 1, UiFrozenFrame.CaptureCount, "tutma başına bir yakalama");
            RenderTexture target = UiFrozenFrame.Target;
            Assert.IsNotNull(target);
            Assert.AreEqual(world.pixelWidth / UiFrozenFrame.Downscale, target.width, "¼ çözünürlük");

            Color[] px = Read(target);
            float max = 0f;
            foreach (Color c in px) max = Mathf.Max(max, c.r + c.g + c.b);
            Assert.Greater(max, 0.2f, "yakalanan kare siyah");

            // Orientation: the red marker sits in the top half of the camera view → the top rows of the frozen frame
            // (Texture2D rows go bottom-up, as the RawImage samples them).
            float top = RowRedness(px, target, 0.62f, 0.95f), bottom = RowRedness(px, target, 0.05f, 0.38f);
            Debug.Log("[DD-TEST] wp068.frozen orientation topRed=" + top.ToString("F3") + " bottomRed=" + bottom.ToString("F3") + " uvTop=" + SystemInfo.graphicsUVStartsAtTop);
            Assert.Greater(top, bottom + 0.05f, "donmuş kare ters (üstteki kırmızı işaret altta çıktı)");

            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(before + 1, UiFrozenFrame.CaptureCount, "sonraki karelerde 0 yakalama");

            UiFrozenFrame.Hold("test-b");
            UiFrozenFrame.Release("test-a");
            yield return null;
            Assert.IsTrue(UiFrozenFrame.IsShowing, "ikinci sahip varken katman kalmalı");
            Assert.AreEqual(before + 1, UiFrozenFrame.CaptureCount, "yığınlanan sahip yeniden yakalamaz");
            UiFrozenFrame.Release("test-b");
            yield return null;
            Assert.IsFalse(UiFrozenFrame.IsShowing, "son sahip bırakınca katman kapanmalı");

            UiFrozenFrame.Hold("test-c");
            for (int i = 0; i < 6 && !UiFrozenFrame.IsShowing; i++) yield return null;
            Assert.AreEqual(before + 2, UiFrozenFrame.CaptureCount, "yeni tutma yeni yakalama");
        }

        // ------------------------------------------------------------------ 5 · hotfix: a hold never outlives its scene / the frozen clock

        [UnityTest]
        public IEnumerator DonmusKare_SahneDegisimindeVeSaatAkarkenBirakilir()
        {
            Camera world = NewWorldCamera();
            yield return null;

            // (a) Pause → END RUN: the owner forgets to release, the scene changes → every owner dropped, layer hidden.
            RunFlowClock.AddHold(TestHold);
            UiFrozenFrame.Hold("pause");
            for (int i = 0; i < 6 && !UiFrozenFrame.IsShowing; i++) yield return null;
            Assert.IsTrue(UiFrozenFrame.IsShowing);
            int forced = UiFrozenFrame.ForcedReleases;
            Scene next = SceneManager.CreateScene("wp068-hotfix-next");
            Scene was = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(next);
            yield return null;
            Assert.IsFalse(UiFrozenFrame.IsHeld, "sahne değişimi sahibi bırakmalı");
            Assert.IsFalse(UiFrozenFrame.IsShowing, "katman yeni sahneyi örtmemeli");
            Assert.AreEqual(forced + 1, UiFrozenFrame.ForcedReleases);
            SceneManager.SetActiveScene(was);
            yield return SceneManager.UnloadSceneAsync(next);
            LogAssert.ignoreFailingMessages = true;   // the release is logged as a warning by design

            // (b) Safety: a hold while the run clock is RUNNING (nothing freezes the world) is released after SafetySeconds.
            RunFlowClock.RemoveHold(TestHold);
            UiFrozenFrame.Hold("orphan");
            for (int i = 0; i < 6 && !UiFrozenFrame.IsShowing; i++) yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (UiFrozenFrame.IsHeld && Time.realtimeSinceStartup - t0 < UiFrozenFrame.SafetySeconds + 1f) yield return null;
            Assert.IsFalse(UiFrozenFrame.IsHeld, "saat akarken sahipsiz tutma güvenlikle bırakılmalı");
            Assert.IsFalse(UiFrozenFrame.IsShowing);

            // (c) A legitimate hold (clock frozen) stays well past the safety window.
            RunFlowClock.AddHold(TestHold);
            UiFrozenFrame.Hold("pause");
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < UiFrozenFrame.SafetySeconds + 0.5f) yield return null;
            Assert.IsTrue(UiFrozenFrame.IsShowing, "donmuş saatle meşru tutma kalmalı");
        }

        // ------------------------------------------------------------------ helpers

        private RenderTexture NewTarget(int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            _temp.Add(rt);
            return rt;
        }

        private Camera NewUiCamera(RenderTexture rt, bool enabled)
        {
            var go = new GameObject("UiFxTestCamera");
            _temp.Add(go);
            Camera cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << UiLayer;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 10f;
            cam.targetTexture = rt;
            cam.enabled = enabled;
            return cam;
        }

        private Camera NewWorldCamera()
        {
            var go = new GameObject("UiFxWorldCamera") { tag = "MainCamera" };
            _temp.Add(go);
            Camera cam = go.AddComponent<Camera>();
            cam.targetTexture = NewTarget(640, 360);   // batchmode: a screen camera does not render
            UiFrozenFrame.CameraOverride = cam;          // earlier fixtures leave their scenes' MainCamera behind
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.3f, 0.5f);
            go.transform.position = new Vector3(0f, 1f, -6f);
            for (int i = 0; i < 6; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = new Vector3(-2.5f + i, 1f + (i % 2), 0f);
                _temp.Add(cube);
            }

            // Orientation marker: an unlit red quad filling the upper part of the view.
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _temp.Add(marker);
            marker.transform.position = new Vector3(0f, 2.9f, -2f);
            marker.transform.localScale = new Vector3(8f, 1.2f, 1f);
            var red = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.red };
            red.SetColor("_BaseColor", Color.red);
            marker.GetComponent<Renderer>().sharedMaterial = red;
            _temp.Add(red);

            var light = new GameObject("UiFxLight");
            _temp.Add(light);
            light.AddComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, 30f, 0f);
            return cam;
        }

        private Canvas NewCanvas(Camera cam)
        {
            var go = new GameObject("UiFxTestCanvas", typeof(RectTransform), typeof(Canvas));
            _temp.Add(go);
            go.layer = UiLayer;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            return canvas;
        }

        private static RectTransform Rect(string name, RectTransform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Image Img(string name, RectTransform parent, Color color)
        {
            RectTransform rt = Rect(name, parent, Vector2.zero, Vector2.one);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static void Place(Graphic g, int i)
        {
            var rt = g.rectTransform;
            float x = (i % 4) / 4f, y = (i / 4) / 4f;
            rt.anchorMin = new Vector2(x + 0.01f, y + 0.01f);
            rt.anchorMax = new Vector2(x + 0.24f, y + 0.24f);
        }

        private static IEnumerator Render(Camera cam)
        {
            UiFxGlobals.Write();
            Canvas.ForceUpdateCanvases();
            yield return null;
            UiFxGlobals.Write();
            cam.Render();
        }

        private static Color[] Read(RenderTexture rt)
        {
            RenderTexture was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = was;
            Color[] px = tex.GetPixels();
            Object.Destroy(tex);
            return px;
        }

        private static Color At(Color[] px, RenderTexture rt, float u, float v)
        {
            int x = Mathf.Clamp((int)(u * rt.width), 0, rt.width - 1), y = Mathf.Clamp((int)(v * rt.height), 0, rt.height - 1);
            return px[y * rt.width + x];
        }
    }
}
