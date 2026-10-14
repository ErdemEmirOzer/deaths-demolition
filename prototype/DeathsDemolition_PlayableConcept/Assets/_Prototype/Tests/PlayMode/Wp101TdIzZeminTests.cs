using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-101 (R4) AD r1 M1 / M3 / M4 / M7 on a controlled ground: a top-down camera at the game's scale (≈ 95 px/m, 1080p) over a
    /// light-stone grey (sRGB 0.75) and a wet-slate grey (0.20) half; an AR tracer and an AR body streak on each half (synthetic
    /// rounds through the TD record path, the real DrawTd layers). Measured against the same frame without the TD layers: the streak's
    /// width across (any channel ≥ 0.10), the body's mean luminance step off the ground, the tracer's saturated-warm share (S ≥ 0.45,
    /// hue 8–50°) and, with Reduce Flashes on, the core's peak (lower), the halo's HDR (lower) and α (kept: WP-104 B4-1 AD-B4 M3) and
    /// the body (unchanged). Writes zemin.json + the two frames to DD_TD_ZEMIN_DIR when set.
    /// </summary>
    [TestFixture]
    public sealed class Wp101TdIzZeminTests
    {
        private const int W = 1920, H = 1080;
        private const float Height = 20f, Fov = 31.7f;   // 1080 / (2 · 20 · tan 15.85°) ≈ 95 px/m
        // far from any scene an earlier test left loaded (sonra-r4: the Run scene's plaza filled this camera)
        private static readonly Vector3 O = new Vector3(5000f, 0f, 5000f);
        private GameObject _camGo, _light, _dark;
        private RenderTexture _rt;
        private Texture2D _read;
        private float _flash;

        [SetUp]
        public void SetUp()
        {
            _flash = GameFeelSettings.FlashScale;
            GameFeelSettings.FlashScale = 1f;
            VfxTier.Override = VfxTierLevel.High;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
        }

        [TearDown]
        public void TearDown()
        {
            GameFeelSettings.FlashScale = _flash;
            VfxTier.Override = null;
            if (TracerRibbonRenderer.Exists) TracerRibbonRenderer.Instance.Clear();
            if (_camGo != null) Object.Destroy(_camGo);
            if (_light != null) Object.Destroy(_light);
            if (_dark != null) Object.Destroy(_dark);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_read != null) Object.Destroy(_read);
        }

        private GameObject Ground(string name, float x, float grey)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(O + new Vector3(x, 0f, 0f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(20f, 20f, 1f);
            var m = new Material(DD.Prototype.Rendering.PrototypeShaders.Unlit);
            m.SetColor("_BaseColor", new Color(grey, grey, grey, 1f));
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        private Color32[] Render(Camera cam, TracerRibbonRenderer r, bool layers)
        {
            if (layers)
            {
                MethodInfo draw = typeof(TracerRibbonRenderer).GetMethod("DrawTd", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int layer = 0; layer < 3; layer++) draw.Invoke(r, new object[] { layer });
            }
            cam.Render();
            RenderTexture prev = RenderTexture.active; RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0); _read.Apply();
            RenderTexture.active = prev;
            return _read.GetPixels32();
        }

        // the layer-2 instances of pass B (the two SMG bodies) as DrawTd(2) writes them
        private static Vector4[] BodyColours(TracerRibbonRenderer r)
        {
            MethodInfo draw = typeof(TracerRibbonRenderer).GetMethod("DrawTd", BindingFlags.Instance | BindingFlags.NonPublic);
            draw.Invoke(r, new object[] { 2 });
            var head = (Vector4[])typeof(TracerRibbonRenderer).GetField("_headColor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(r);
            return new[] { head[0], head[1], head[0], head[1] };   // pass B layer 2: the two SMG bodies (the pellet bars are in layer 0)
        }

        // the brightest channel of the first tracer core the last DrawTd(0) wrote (the HDR head colour)
        private static float CoreHdr(TracerRibbonRenderer r)
        {
            MethodInfo draw = typeof(TracerRibbonRenderer).GetMethod("DrawTd", BindingFlags.Instance | BindingFlags.NonPublic);
            draw.Invoke(r, new object[] { 0 });
            var head = (Vector4[])typeof(TracerRibbonRenderer).GetField("_headColor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(r);
            return Mathf.Max(head[0].x, Mathf.Max(head[0].y, head[0].z));
        }

        // the first tracer halo the last DrawTd(2) wrote (pass A: the light-stone AR's): its HDR head colour and α
        private static Vector4 HaloColour(TracerRibbonRenderer r)
        {
            MethodInfo draw = typeof(TracerRibbonRenderer).GetMethod("DrawTd", BindingFlags.Instance | BindingFlags.NonPublic);
            draw.Invoke(r, new object[] { 2 });
            var head = (Vector4[])typeof(TracerRibbonRenderer).GetField("_headColor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(r);
            return head[0];
        }

        private static Vector2 Px(Camera cam, Vector3 w) { Vector3 v = cam.WorldToViewportPoint(w); return new Vector2(v.x * W, v.y * H); }

        private struct Cut { public int Width; public float LumStep; public float Warm; public float Peak; }

        // a cut across the streak (screen y, the streak runs along x) at world x: the changed run's width, its mean luminance step,
        // its saturated-warm share and its peak luminance
        private static Cut Across(Camera cam, Color32[] lit, Color32[] dark, float x, float z)
        {
            Vector2 c = Px(cam, O + new Vector3(x, 1f, z));
            var cut = new Cut();
            float step = 0f; int warm = 0;
            for (int dy = -40; dy <= 40; dy++)
            {
                int px = (int)c.x, py = (int)c.y + dy;
                if (py < 0 || py >= H) continue;
                Color a = lit[py * W + px], b = dark[py * W + px];
                if (Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))) < .10f) continue;
                cut.Width++;
                float la = .2126f * a.r + .7152f * a.g + .0722f * a.b, lb = .2126f * b.r + .7152f * b.g + .0722f * b.b;
                step += Mathf.Abs(la - lb);
                cut.Peak = Mathf.Max(cut.Peak, la);
                Color.RGBToHSV(a, out float h, out float s, out _);
                if (s >= .45f && h * 360f >= 8f && h * 360f <= 50f) warm++;
            }
            cut.LumStep = cut.Width > 0 ? step / cut.Width : 0f;
            cut.Warm = cut.Width > 0 ? warm / (float)cut.Width : 0f;
            return cut;
        }

        [UnityTest]
        public IEnumerator TdIz_AcikTasVeIslakArduvaz_IzciEniGovdeKontrastiTuruncuYokReduceFlashes()
        {
            LogAssert.ignoreFailingMessages = true;
            _light = Ground("WP101-light-stone", -10f, .75f);
            _dark = Ground("WP101-wet-slate", 10f, .20f);
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Wp101Zemin" };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false);
            _camGo = new GameObject("WP101-TD-camera");
            Camera cam = _camGo.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(O + new Vector3(0f, Height, 0f), Quaternion.Euler(90f, 0f, 0f));
            cam.allowHDR = true;
            // the game camera's tonemap and bloom (URP camera data by reflection: this test assembly does not reference URP)
            System.Type urp = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            Assert.IsNotNull(urp, "URP camera data type");
            Component data = _camGo.GetComponent(urp) ?? _camGo.AddComponent(urp);
            urp.GetProperty("renderPostProcessing").SetValue(data, true);
            cam.fieldOfView = Fov; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.targetTexture = _rt; cam.enabled = false;
            yield return null;

            TracerRibbonRenderer r = TracerRibbonRenderer.Instance;
            Assert.IsTrue(r.Ready, "DD/VFX Ribbon");
            r.Clear();
            // synthetic rounds (negative ids fly on the presentation clock). Two passes so one never blooms into the other's cut:
            // A = tracers (AR on each half, a sniper below), B = SMG body streaks (SMG rhythm 2 → odd ids are bodies)
            float t = .035f;
            void Round(int id, ClassicWeaponId weapon, float headX, float z, bool pellet = false)
            {
                float v = VfxWeaponProfiles.For(weapon).Speed;
                var info = new ProjectileSpawnInfo(id, 0, weapon, pellet ? ProjectileKind.Pellet : ProjectileKind.Bullet, WeaponCaliber.Medium,
                    O + new Vector3(headX - v * t, 1f, z), Vector3.right, v, 0);
                Assert.IsTrue(r.RecordTdProjectile(in info), "TD record " + id);
            }
            float pxPerM = (Px(cam, O + new Vector3(1f, 1f, 0f)) - Px(cam, O + Vector3.up)).magnitude;
            Color32[] dark = Render(cam, r, false);
            Round(-2, ClassicWeaponId.AssaultRifle, -3f, -2f); Round(-4, ClassicWeaponId.AssaultRifle, 6f, -2f);
            Round(-6, ClassicWeaponId.Sr1, -3f, 3f); Round(-8, ClassicWeaponId.Sr1, 6f, 3f);
            Round(-10, ClassicWeaponId.Sg2, -1.5f, -5f, true); Round(-12, ClassicWeaponId.Sg2, 4.5f, -5f, true);   // sg_2's centre tracer pellet
            r.TickTd(t);
            Assert.AreEqual(6, r.LiveTdTracerRounds, "pass A: six tracers");
            Color32[] lit = Render(cam, r, true);
            Cut tracerLight = Across(cam, lit, dark, -4.5f, -2f), tracerSlate = Across(cam, lit, dark, 4.5f, -2f);
            Cut sniperLight = Across(cam, lit, dark, -4.5f, 3f), sniperSlate = Across(cam, lit, dark, 4.5f, 3f);
            Cut sg2Light = Across(cam, lit, dark, -2.2f, -5f), sg2Slate = Across(cam, lit, dark, 3.8f, -5f);
            float corePeak = CoreHdr(r);
            Vector4 haloOn = HaloColour(r);
            GameFeelSettings.FlashScale = .4f;
            Color32[] litRf = Render(cam, r, true);
            float corePeakRf = CoreHdr(r);
            Vector4 haloRf = HaloColour(r);
            Cut tracerSlateRf = Across(cam, litRf, dark, 4.5f, -2f);
            GameFeelSettings.FlashScale = 1f;
            // pass B: the SMG body streaks alone (a new frame - the queued pass A draws are gone)
            r.Clear();
            yield return null;
            Round(-1, ClassicWeaponId.AutoSmg, -3f, 0f); Round(-3, ClassicWeaponId.AutoSmg, 6f, 0f);
            Round(-5, ClassicWeaponId.CombatShotgun, -2f, -4f, true); Round(-7, ClassicWeaponId.CombatShotgun, 5f, -4f, true);   // sg_1 plain pellets (bars)
            r.TickTd(t);
            Assert.AreEqual(0, r.LiveTdTracerRounds, "pass B: bodies only");
            Color32[] darkB = Render(cam, r, false), litB = Render(cam, r, true);
            float bodyX = -3f - TracerRibbonRenderer.TdBodyMin * .5f, bodyXs = 6f - TracerRibbonRenderer.TdBodyMin * .5f;
            Cut bodyLight = Across(cam, litB, darkB, bodyX, 0f), bodySlate = Across(cam, litB, darkB, bodyXs, 0f);
            Cut pelletLight = Across(cam, litB, darkB, -2.25f, -4f), pelletSlate = Across(cam, litB, darkB, 4.75f, -4f);
            // M7 for the bodies from the layer they draw with (a second render in this frame would draw them twice, a new frame
            // moves them): their colour and α do not follow Reduce Flashes
            Vector4[] bodyOn = BodyColours(r);
            GameFeelSettings.FlashScale = .4f;
            Vector4[] bodyRf = BodyColours(r);
            GameFeelSettings.FlashScale = 1f;

            string dir = System.Environment.GetEnvironmentVariable("DD_TD_ZEMIN_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                _read.SetPixels32(lit); _read.Apply(); File.WriteAllBytes(Path.Combine(dir, "zemin.png"), _read.EncodeToPNG());
                _read.SetPixels32(litRf); _read.Apply(); File.WriteAllBytes(Path.Combine(dir, "zemin-reduceflashes.png"), _read.EncodeToPNG());
                string J(Cut c) => "{\"widthPx\": " + c.Width + ", \"lumStep\": " + c.LumStep.ToString("0.###", CultureInfo.InvariantCulture)
                    + ", \"warmShare\": " + c.Warm.ToString("0.###", CultureInfo.InvariantCulture) + ", \"peak\": " + c.Peak.ToString("0.###", CultureInfo.InvariantCulture) + "}";
                File.WriteAllText(Path.Combine(dir, "zemin.json"), "{\n  \"pxPerMetre\": " + pxPerM.ToString("0.#", CultureInfo.InvariantCulture)
                    + ",\n  \"tracerLightStone\": " + J(tracerLight) + ",\n  \"tracerWetSlate\": " + J(tracerSlate)
                    + ",\n  \"sniperLightStone\": " + J(sniperLight) + ",\n  \"sniperWetSlate\": " + J(sniperSlate)
                    + ",\n  \"sg2TracerLightStone\": " + J(sg2Light) + ",\n  \"sg2TracerWetSlate\": " + J(sg2Slate)
                    + ",\n  \"pelletLightStone\": " + J(pelletLight) + ",\n  \"pelletWetSlate\": " + J(pelletSlate)
                    + ",\n  \"bodyLightStone\": " + J(bodyLight) + ",\n  \"bodyWetSlate\": " + J(bodySlate)
                    + ",\n  \"coreHdr\": " + corePeak.ToString("0.##", CultureInfo.InvariantCulture) + ", \"coreHdrReduceFlashes\": " + corePeakRf.ToString("0.##", CultureInfo.InvariantCulture) + ",\n  \"tracerWetSlateReduceFlashes\": " + J(tracerSlateRf)  + "\n}\n");
            }
            Debug.Log("[DD-TEST] wp101.zemin pxPerM=" + pxPerM.ToString("0.#") + " tracer light/slate " + tracerLight.Width + "/" + tracerSlate.Width
                + " body " + bodyLight.Width + "/" + bodySlate.Width + " step " + bodyLight.LumStep.ToString("0.00") + "/" + bodySlate.LumStep.ToString("0.00"));

            Assert.That(pxPerM, Is.InRange(85f, 105f), "the game's TD scale");
            // M1: the AR streak ≥ 5 px, the sniper's ≥ 7 px, on both grounds
            Assert.GreaterOrEqual(tracerLight.Width, 5, "M1 AR izi açık taşta");
            Assert.GreaterOrEqual(tracerSlate.Width, 5, "M1 AR izi ıslak arduvazda");
            Assert.GreaterOrEqual(sniperLight.Width, 7, "M1 SR izi açık taşta");
            Assert.GreaterOrEqual(sniperSlate.Width, 7, "M1 SR izi ıslak arduvazda");
            // M3: no saturated amber body to the streak
            Assert.LessOrEqual(tracerLight.Warm, .15f, "M3 açık taş");
            Assert.LessOrEqual(tracerSlate.Warm, .15f, "M3 ıslak arduvaz");
            foreach (Cut c in new[] { sniperLight, sniperSlate, sg2Light, sg2Slate, pelletLight, pelletSlate })
                Assert.LessOrEqual(c.Warm, .15f, "M3 SR / sg_2 / saçma");
            // M4: the body streak ≥ 3 px and ≥ 0.12 luminance off both grounds
            Assert.GreaterOrEqual(bodyLight.Width, 3, "M4 gövde eni açık taşta");
            Assert.GreaterOrEqual(bodySlate.Width, 3, "M4 gövde eni ıslak arduvazda");
            Assert.GreaterOrEqual(bodyLight.LumStep, .12f, "M4 gövde kontrastı açık taşta");
            Assert.GreaterOrEqual(bodySlate.LumStep, .12f, "M4 gövde kontrastı ıslak arduvazda");
            Assert.GreaterOrEqual(TracerRibbonRenderer.TdBodyMin, .5f, "M4 gövde boyu ≥ 0,5 m");
            // M7: Reduce Flashes lowers the tracer's peak, keeps the body
            // (the tonemap saturates the pixel peak - the core's HDR is read from the layer it draws with)
            Assert.Less(corePeakRf, corePeak * .7f, "M7 çekirdek HDR'ı bastırılır");
            // WP-104 B4-1 (AD-B4 M3): a steady trail is no flash - Reduce Flashes dims the halo's HDR and keeps its α (× 0.4 cost the
            // streak its form); one code path for the WP-101 neutral tracer and the tree's
            Assert.AreEqual(haloOn.w, haloRf.w, 1e-6f, "M3 hale α'sı Reduce Flashes'ta korunur");
            Assert.Less(Mathf.Max(haloRf.x, Mathf.Max(haloRf.y, haloRf.z)), Mathf.Max(haloOn.x, Mathf.Max(haloOn.y, haloOn.z)) * .7f, "M3 hale HDR'ı kısılır");
            for (int i = 0; i < 4; i++) Assert.AreEqual(bodyOn[i], bodyRf[i], "M7 gövde ve saçma korunur");
            Assert.GreaterOrEqual(pelletLight.Width, 3, "saçma çubuğu açık taşta (r11: koyu çubuk 0 px - artık R3'ün sıcak beyaz parıltısı)");
            Assert.GreaterOrEqual(pelletSlate.Width, 3, "saçma çubuğu ıslak arduvazda");
        }
    }
}
