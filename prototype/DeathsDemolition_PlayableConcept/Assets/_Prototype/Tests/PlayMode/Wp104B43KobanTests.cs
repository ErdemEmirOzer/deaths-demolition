using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Economy;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-104 B4-3 (S-07) · the koban orb in play (AD-VISUAL K8 / X10 B18, <see cref="TreeGoldOrbCue"/>) on real pooled GoldOrb bodies
    /// (an own template through PoolService, as GoldService pools them; TreeRuntime.SetTestStats sets the tree's GoldScale):
    /// <list type="bullet">
    /// <item>GoldScale 1 → the orb exactly as before (WP-004 scale, nothing added to a body that never had a koban life, no glint);</item>
    /// <item>Coin Sense I / II / III (the catalog's values) → ×1.25 on the orb's scale, the glint drawn (added, the orb's hue, facing the
    /// gameplay camera, size and rate climbing with the tier), the orb's material and light colour untouched; Reduce Flashes dims it;</item>
    /// <item>the pool return turns the glint off (and a taken packet's), the body's next GoldScale 1 life is the old orb;</item>
    /// <item>a koban life on a built body — Get, Begin, a frame, the pool return — allocates 0 B;</item>
    /// <item>DD_B4_AD_DIR set: the AD frames 39 / 40 — normal + koban I / II / III side by side at the TD rig angle on the light stone
    /// (sRGB .75) and the wet slate (.20), Wp104B4AdKareTests' camera (20 m, FOV 31.7°, 1920 × 1080, the game's post stack) — and
    /// koban.json (orb screen diameter px, the glint's glowSpotPx at rest and at the twinkle's peak). Unset: ignored.</item>
    /// </list>
    /// The glint needs AP-041's glint row (the B4 gate, as Wp104B4VfxPlayTests): without it these tests fail loudly.
    /// </summary>
    [TestFixture, Category("WP104"), Category("TD")]
    public sealed class Wp104B43KobanTests
    {
        private const int Amount = 5;
        private const int W = 1920, H = 1080, CropW = 640, CropH = 180, Zoom = 3, LabelScale = 3;
        private const float CamDistance = 20f, Fov = 31.7f, LightGrey = .75f, SlateGrey = .20f, Spacing = 1.5f, OrbRoiMetres = .7f;
        /// <summary>Wp101: a channel moved this much (0.10) against the frame without it = its own pixel.</summary>
        private const int Moved = 26;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        // far from any scene an earlier test left loaded
        private static readonly Vector3 O = new Vector3(-5600f, 0f, 5600f);
        private static readonly Color32 Ink = new Color32(242, 240, 234, 255);
        private static readonly string[] Labels = { "NORMAL", "KOBAN I", "KOBAN II", "KOBAN III" };
        private static readonly string[] CoinIds = { null, "ec_gold1", "ec_gold2", "ec_gold3" };

        private static GoldOrb s_template;
        private static PrototypePool<GoldOrb> s_pool;

        private readonly List<GoldOrb> _orbs = new List<GoldOrb>();
        private readonly List<Object> _owned = new List<Object>();
        private GoldOrbTuning _tuning;
        private GameObject _root;
        private Camera _cam, _previousFeedbackCamera;
        private float _previousFlash, _previousTimeScale;
        private int _tx = 71000;

        // capture rig (frames 39 / 40)
        private RenderTexture _rt;
        private Texture2D _read;
        private Component _camData;
        private PropertyInfo _post;
        private MeshRenderer _ground;
        private Material _lightMat, _slateMat;
        private bool _lookPost, _captured, _previousFog, _previousAsync;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousFeedbackCamera = FeedbackCameraRef.Current;
            _previousFlash = GameFeelSettings.FlashScale;
            _previousTimeScale = Time.timeScale;
            GameFeelSettings.FlashScale = 1f;
            Time.timeScale = 1f;
            TreeRuntime.ClearTestStats();
            _tuning = new GoldOrbTuning();
            _root = new GameObject("WP104 B4-3 koban test root");
            var camGo = new GameObject("WP104-B43 gameplay camera");
            camGo.transform.SetParent(_root.transform, false);
            camGo.transform.SetPositionAndRotation(O + new Vector3(0f, 14f, -12f), Quaternion.LookRotation(new Vector3(0f, -14f, 12f)));
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;   // only its transform: the glint faces FeedbackCameraRef
            FeedbackCameraRef.Set(_cam);
            if (s_template == null)
            {
                s_template = GoldOrb.CreateTemplate();
                s_template.gameObject.name = "WP104-B43 koban test orb";
                s_pool = null;
            }

            if (s_pool == null) s_pool = PoolService.GetOrCreate(s_template, 0, 12, PoolExhaustedPolicy.ReturnNull);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = 0; i < _orbs.Count; i++)
            {
                if (_orbs[i] != null && _orbs[i].IsSpawned) _orbs[i].Despawn();
            }

            _orbs.Clear();
            TreeRuntime.ClearTestStats();
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Object.Destroy(_rt);
                _rt = null;
            }

            if (_read != null) Object.Destroy(_read);
            _read = null;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i] != null) Object.Destroy(_owned[i]);
            }

            _owned.Clear();
            if (_root != null) Object.Destroy(_root);
            FeedbackCameraRef.Set(_previousFeedbackCamera);
            GameFeelSettings.FlashScale = _previousFlash;
            Time.timeScale = _previousTimeScale;
            if (_captured)
            {
                Time.captureDeltaTime = 0f;
                RenderSettings.fog = _previousFog;
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation = _previousAsync;
#endif
                _captured = false;
            }

            yield return null;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (s_pool != null) s_pool.DestroyAll();
            s_pool = null;
            if (s_template != null)
            {
                if (Application.isPlaying) Object.Destroy(s_template.gameObject);
                else Object.DestroyImmediate(s_template.gameObject);
            }

            s_template = null;
        }

        // ------------------------------------------------------------------ rules in play

        [UnityTest]
        public IEnumerator GoldScale1_OrbBirebirEski_OlcekX1_ParlamaYok()
        {
            // a body that never had a koban life: nothing is added to it
            var bareGo = new GameObject("WP104-B43 bare orb");
            bareGo.transform.SetParent(_root.transform, false);
            GoldOrb bare = bareGo.AddComponent<GoldOrb>();
            UseGoldScale(1f);
            Assert.IsTrue(bare.Begin(++_tx, Amount, 1, 1, O, null, _tuning, false), "Begin");
            float expected = _tuning.ScaleForAmount(Amount);
            Assert.IsTrue(Visual(bare) != null, "GoldOrb görseli (Visual)");
            Assert.AreEqual(expected, Visual(bare).localScale.x, 1e-6f, "GoldScale 1: WP-004 ölçeği birebir");
            Assert.IsFalse(bare.TryGetComponent(out TreeGoldOrbCue _), "GoldScale 1: koban bileşeni eklenmez");
            Assert.IsTrue(bare.transform.Find(TreeGoldOrbCue.CardName) == null, "GoldScale 1: parıltı kartı kurulmaz");

            // a pooled body at GoldScale 1 (an earlier koban life of it, if any, is off)
            GoldOrb orb = Spawn(1f, O + Vector3.right * 3f);
            yield return null;
            yield return null;
            Assert.AreEqual(expected, Visual(orb).localScale.x, 1e-6f, "havuzlu gövde, GoldScale 1: ölçek birebir");
            if (orb.TryGetComponent(out TreeGoldOrbCue cue))
            {
                Assert.IsFalse(cue.Armed, "GoldScale 1: koban yaşamı değil");
                Assert.IsFalse(cue.CardShown, "GoldScale 1: parıltı yok");
                Assert.IsFalse(cue.enabled, "GoldScale 1: kare başı iş yok");
            }

            Light light = orb.GetComponentInChildren<Light>(true);
            if (light != null) Assert.AreEqual(_tuning.Color, light.color, "ışık rengi orb'un rengi");
        }

        [UnityTest]
        public IEnumerator GoldScaleUst1_KobanI_II_III_Olcek125_Parlama_RenkAyni()
        {
            AssertRow();
            GoldOrb normal = Spawn(1f, O);
            var kobans = new GoldOrb[3];
            for (int i = 0; i < 3; i++) kobans[i] = Spawn(CoinSense(CoinIds[i + 1]), O + Vector3.right * (Spacing * (i + 1)));
            yield return null;
            yield return null;

            float baseScale = _tuning.ScaleForAmount(Amount);
            Renderer normalRenderer = Visual(normal).GetComponent<Renderer>();
            Light normalLight = normal.GetComponentInChildren<Light>(true);
            Color hue = TreeGoldOrbCue.Hue(_tuning.Color);
            float lastSize = 0f, lastPeriod = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                GoldOrb k = kobans[i];
                string tag = Labels[i + 1] + " (GoldScale " + F(CoinSense(CoinIds[i + 1])) + ")";
                Assert.AreEqual(baseScale * TreeGoldOrbCue.KobanScale, Visual(k).localScale.x, 1e-5f, tag + ": AD K8 ×1,25");
                Assert.IsTrue(k.TryGetComponent(out TreeGoldOrbCue cue), tag + ": koban bileşeni");
                Assert.IsTrue(cue.Armed, tag + ": koban yaşamı");
                Assert.AreEqual(i + 1, cue.Tier, tag + ": kademe");
                Assert.AreEqual(baseScale * TreeGoldOrbCue.KobanScale, cue.Diameter, 1e-5f, tag + ": parıltı koban çapına göre");
                Assert.IsTrue(cue.CardShown, tag + ": parıltı çiziliyor");

                Renderer kobanRenderer = Visual(k).GetComponent<Renderer>();
                Assert.AreSame(normalRenderer.sharedMaterial, kobanRenderer.sharedMaterial, tag + ": orb malzemesi aynı (renk değişmez, K8)");
                Assert.IsFalse(kobanRenderer.HasPropertyBlock(), tag + ": orb'a renk bloğu yok");
                Light kobanLight = k.GetComponentInChildren<Light>(true);
                if (normalLight != null && kobanLight != null) Assert.AreEqual(normalLight.color, kobanLight.color, tag + ": ışık rengi aynı");

                Vector4 tint = cue.CardTint;
                Assert.Greater(tint.x, 0f, tag + ": parıltı rengi");
                Assert.AreEqual(hue.g / hue.r, tint.y / tint.x, 1e-4f, tag + ": parıltı tonu orb'un rengi (g/r)");
                Assert.AreEqual(hue.b / hue.r, tint.z / tint.x, 1e-4f, tag + ": parıltı tonu orb'un rengi (b/r)");
                Material cardMaterial = cue.CardRenderer.sharedMaterial;
                Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.One, cardMaterial.GetFloat("_DstBlend"), tag + ": eklemeli (One One)");

                cue.CardEnabled = true;   // pose now: the coroutine resumes after Update (the hop moved the orb), before LateUpdate
                Transform card = cue.CardRenderer.transform;
                Assert.Less(Vector3.Distance(card.position, kobanRenderer.bounds.center), 2f * cue.Diameter, tag + ": parıltı orb'un kenarında");
                Assert.Less(Quaternion.Angle(card.rotation, _cam.transform.rotation), .01f, tag + ": oyun kamerasına dönük");
                Assert.Greater(Vector3.Dot(card.position - kobanRenderer.bounds.center, _cam.transform.position - kobanRenderer.bounds.center), 0f,
                    tag + ": orb'un kameraya bakan yüzünde");

                float size = TreeGoldOrbCue.GlintSize(cue.Tier, cue.Diameter);
                Assert.Greater(size, lastSize, tag + ": parıltı boyu kademeyle artar");
                Assert.Less(cue.Period, lastPeriod, tag + ": parıltı sıklığı kademeyle artar");
                lastSize = size;
                lastPeriod = cue.Period;
            }

            // Reduce Flashes: × FlashScale (the pose reads it every frame); 0 takes the glint off; back at 1 it returns
            TreeGoldOrbCue third = kobans[2].GetComponent<TreeGoldOrbCue>();
            GameFeelSettings.FlashScale = .4f;
            third.SetPhase(TreeGoldOrbCue.PeakPhase);
            Vector4 dim = TreeGoldOrbCue.Tint(_tuning.Color, TreeGoldOrbCue.Level(1f, .4f));
            Assert.AreEqual(dim.x, third.CardTint.x, 1e-4f, "Reduce Flashes: tepe × 0,4");
            Assert.Less(third.CardTint.x, TreeVfxLadder.BloomThreshold, "Reduce Flashes: bloom yok");
            GameFeelSettings.FlashScale = 0f;
            third.SetPhase(TreeGoldOrbCue.PeakPhase);
            Assert.IsFalse(third.CardShown, "FlashScale 0: parıltı yok");
            GameFeelSettings.FlashScale = 1f;
            yield return null;
            Assert.IsTrue(third.CardShown, "FlashScale 1: parıltı geri");
            Debug.Log("[DD-TEST] wp104b43.koban base=" + F(baseScale) + " koban=" + F(Visual(kobans[0]).localScale.x) + " sizes=" + F(lastSize)
                      + " row=" + TreeVfxIds.Id(TreeGoldOrbCue.Row));
        }

        [UnityTest]
        public IEnumerator HavuzaDonus_ParlamaKapanir_AlinincaKapanir_SonrakiYasamEski()
        {
            AssertRow();
            GoldOrb k = Spawn(CoinSense("ec_gold3"), O);
            yield return null;
            Assert.IsTrue(k.TryGetComponent(out TreeGoldOrbCue cue), "koban bileşeni");
            Renderer card = cue.CardRenderer;
            Assert.IsTrue(cue.CardShown, "koban III: parıltı çiziliyor");

            k.Despawn();   // the pool: GoldOrb.OnDespawned → Off
            Assert.IsFalse(k.IsSpawned);
            Assert.IsFalse(cue.Armed, "havuz dönüşü: koban kapalı");
            Assert.IsFalse(cue.enabled, "havuz dönüşü: kare başı iş yok");
            Assert.IsFalse(card.enabled, "havuz dönüşü: parıltı kapalı");

            // the same body's next life at GoldScale 1: the old orb exactly, the glint stays off
            GoldOrb again = Spawn(1f, O);
            Assert.AreSame(k, again, "havuz aynı gövdeyi verdi (LIFO)");
            yield return null;
            Assert.AreEqual(_tuning.ScaleForAmount(Amount), Visual(again).localScale.x, 1e-6f, "GoldScale 1 yaşamı: ölçek birebir eski");
            Assert.IsFalse(cue.Armed);
            Assert.IsFalse(card.enabled, "GoldScale 1 yaşamı: parıltı yok");
            again.Despawn();

            // a koban life taken (the round-end sweep): the coin flies alone
            GoldOrb taken = Spawn(CoinSense("ec_gold1"), O);
            Assert.AreSame(k, taken);
            yield return null;
            Assert.IsTrue(cue.CardShown, "yeni koban yaşamı: parıltı yeniden");
            Assert.AreEqual(1, cue.Tier);
            Assert.IsTrue(taken.Packet.TryCollect(GoldCollectKind.RoundClearAuto, out _), "sweep paketi aldı");
            cue.SetPhase(TreeGoldOrbCue.PeakPhase);   // the per-frame pose, now
            Assert.IsFalse(cue.CardShown, "alınan paket: parıltı kapalı (uçuş/flaş yalnız sikke)");
            Assert.IsFalse(cue.enabled, "alınan paket: kare başı iş yok");
            yield return null;   // the orb's flight ends (no owner) and the pool takes it
            Assert.IsFalse(taken.IsSpawned);
            Assert.IsFalse(card.enabled);
        }

        [UnityTest]
        public IEnumerator KobanYolu_Spawn_Kare_HavuzDonusu_0B()
        {
            AssertRow();
            float scale = CoinSense("ec_gold2");
            GoldOrb body = Spawn(scale, O);   // the body's one-time build (the component and its quad) happens here
            yield return null;
            Assert.IsTrue(body.TryGetComponent(out TreeGoldOrbCue cue));
            body.Despawn();
            UseGoldScale(scale);

            var late = (Action)Delegate.CreateDelegate(typeof(Action), cue, typeof(TreeGoldOrbCue).GetMethod("LateUpdate", Private));
            PrototypePool<GoldOrb> pool = s_pool;
            GoldOrbTuning tuning = _tuning;
            Vector3 at = O;
            int tx = _tx + 1000, shown = 0;
            GoldOrb last = null;
            Action cycle = () =>
            {
                GoldOrb o = pool.Get(at, Quaternion.identity);
                o.Begin(++tx, Amount, 1, 1, at, null, tuning, false);
                o.Tick(1f / 60f, 1f / 60f);
                late();
                if (cue.CardShown) shown++;
                last = o;
                o.Despawn();
            };

            for (int i = 0; i < 4; i++) cycle();   // warm: the pool's comparer, the first block upload
            Assert.AreSame(body, last, "havuz aynı gövdeyi verdi: ölçülen yol kurulmuş gövdenin yolu");
            Assert.That(() =>
            {
                for (int i = 0; i < 20; i++) cycle();
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "koban spawn + kare + havuz dönüşü ayırmasız (0 B)");
            Assert.AreEqual(24, shown, "her koban yaşamında parıltı çizildi");
            Assert.IsFalse(cue.CardShown, "son havuz dönüşünde kapalı");
            _tx = tx;
        }

        // ------------------------------------------------------------------ AD frames 39 / 40 (DD_B4_AD_DIR)

        [UnityTest, Timeout(180000)]
        public IEnumerator Kare39_40_KobanOrb_NormalVeKobanYanYana_IkiZemin()
        {
            string dir = Environment.GetEnvironmentVariable("DD_B4_AD_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("DD_B4_AD_DIR yok: koban kareleri (39 / 40) yalnız hedefli AD koşusunda yazılır");
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;
            AssertRow();
            BeginCapture();
            CameraFeel td = FeelRuntime.Current.Camera;
            Vector3 rigDir = Quaternion.Euler(0f, td.yawDegrees, 0f) * new Vector3(0f, td.heightMeters, -td.backDistanceMeters);
            rigDir = rigDir.sqrMagnitude > 1e-6f ? rigDir.normalized : Vector3.up;
            Vector3 row = Quaternion.Euler(0f, td.yawDegrees, 0f) * Vector3.right;   // the screen's horizontal on the ground
            Vector3 centre = O + Vector3.up * _tuning.RestHeight;
            BuildRig(centre, rigDir);
            string viewDetail = "TD rig angle (DD_FeelConfig height " + F(td.heightMeters) + " / back " + F(td.backDistanceMeters) + " / yaw " + F(td.yawDegrees)
                                + "): camera along (" + F(rigDir.x) + ", " + F(rigDir.y) + ", " + F(rigDir.z) + ") x " + F(CamDistance) + " m from the row, FOV " + F(Fov);

            var orbs = new GoldOrb[4];
            var cues = new TreeGoldOrbCue[4];
            var scales = new float[4];
            for (int i = 0; i < 4; i++)
            {
                scales[i] = CoinSense(CoinIds[i]);
                Vector3 feet = O + row * (Spacing * (i - 1.5f));
                orbs[i] = Spawn(scales[i], feet);
                orbs[i].Tick(1f, 1f);   // the hop at once: landed
                orbs[i].transform.position = feet + Vector3.up * _tuning.RestHeight;
                if (i > 0) Assert.IsTrue(orbs[i].TryGetComponent(out cues[i]), Labels[i] + ": koban bileşeni");
            }

            yield return null;
            Time.timeScale = 0f;   // the spin, the sparkle's pulse and the twinkle clock hold
            for (int i = 0; i < 4; i++)
            {
                Visual(orbs[i]).localRotation = Quaternion.Euler(0f, 25f, 0f);   // one pose for all four
                GoldPackVisual pack = orbs[i].GetComponentInChildren<GoldPackVisual>(true);
                if (pack != null)
                {
                    SetPrivate(pack, "_clock", 0f);   // the AP-010 sparkle at one pulse for all four
                    SetPrivate(pack, "_glint", 0f);   // the landing swell over
                }
            }

            yield return null;
            var json = new List<string>();
            for (int pass = 0; pass < 2; pass++)
            {
                bool light = pass == 0;
                _ground.sharedMaterial = light ? _lightMat : _slateMat;
                yield return null;
                SetPhase(cues, TreeGoldOrbCue.RestPhase);
                Color32[] bare = BareGrab(orbs);
                SetCards(cues, false);
                Color32[] offRaw = Grab(false), offLook = _lookPost ? Grab(true) : offRaw;
                SetCards(cues, true);
                SetPhase(cues, TreeGoldOrbCue.RestPhase);
                Color32[] restRaw = Grab(false), restLook = _lookPost ? Grab(true) : restRaw;
                var restEdge = new float[4];
                var restTint = new Vector4[4];
                for (int i = 1; i < 4; i++)
                {
                    restEdge[i] = cues[i].CardEdge;
                    restTint[i] = cues[i].CardTint;
                }

                SetPhase(cues, TreeGoldOrbCue.PeakPhase);
                Color32[] peakRaw = Grab(false), peakLook = _lookPost ? Grab(true) : peakRaw;

                int n = light ? 39 : 40;
                string ground = light ? "lightStone" : "wetSlate", groundWord = light ? "ACIK TAS" : "ISLAK ARDUVAZ";
                string file = n + (light ? "-koban-orb-acik-tas.png" : "-koban-orb-islak-arduvaz.png");
                var orbJson = new List<string>();
                Vector2 mid = Vector2.zero;
                float normalScale = Visual(orbs[0]).localScale.x, normalDiameterPx = 0f, normalHue = float.NaN;
                for (int i = 0; i < 4; i++)
                {
                    Renderer vr = Visual(orbs[i]).GetComponent<Renderer>();
                    Bounds b = vr.bounds;
                    Vector2 c = Px(b.center);
                    mid += c / 4f;
                    float ppm = PpmAt(b.center);
                    float boundsDiameter = 2f * Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
                    float diameterPx = boundsDiameter * ppm;
                    Vector2 screen = ScreenSize(b);
                    OrbPixels(offRaw, bare, offLook, c, OrbRoiMetres * ppm, out int areaPx, out Color32 mean);
                    Color.RGBToHSV(mean, out float hueN, out float sat, out float val);
                    float hueDeg = hueN * 360f;
                    if (i == 0)
                    {
                        normalDiameterPx = diameterPx;
                        normalHue = hueDeg;
                    }

                    TreeGoldOrbCue cue = cues[i];
                    var pairs = new List<string>
                    {
                        P("label", Labels[i]), P("goldScale", scales[i]), P("tier", cue != null ? cue.Tier : 0), P("armed", cue != null && cue.Armed),
                        P("visualScale", Visual(orbs[i]).localScale.x), P("scaleRatioToNormal", Visual(orbs[i]).localScale.x / normalScale),
                        P("boundsDiameterM", boundsDiameter), P("diameterPx", diameterPx), P("diameterRatioToNormal", normalDiameterPx > 0f ? diameterPx / normalDiameterPx : float.NaN),
                        PR("screenPx", "[" + F(screen.x) + ", " + F(screen.y) + "]"), PR("orbScreen", "[" + F(c.x) + ", " + F(c.y) + "]"), P("ppm", ppm),
                        P("orbAreaPx", areaPx), P("orbMeanPost", "#" + ColorUtility.ToHtmlStringRGB(mean)), P("orbHueDeg", hueDeg), P("orbSat", sat),
                        P("hueDeltaToNormalDeg", HueDelta(hueDeg, normalHue))
                    };

                    // the glint: its card's screen point (a normal orb is measured at its own centre: the control, nothing switches there)
                    Vector2 g = cue != null && cue.CardRenderer != null ? Px(cue.CardRenderer.transform.position) : c;
                    float peakEdgePx = cue != null ? cue.CardEdge * ppm : 0f;
                    float radius = Mathf.Max(24f, .5f * peakEdgePx + 4f);
                    Spot(restLook, offLook, g, radius, out int spotRest, out float lumOff, out float lumRest, out _);
                    Spot(peakLook, offLook, g, radius, out int spotPeak, out _, out float lumPeak, out _);
                    Spot(peakRaw, offRaw, g, radius, out int spotPeakRaw, out _, out _, out int saturatedPeak);
                    pairs.Add(P("glint", cue != null && cue.Armed));
                    pairs.Add(PR("glintScreen", "[" + F(g.x) + ", " + F(g.y) + "]"));
                    pairs.Add(P("glowSpotRadiusPx", radius));
                    pairs.Add(P("glowSpotPxRest", spotRest));
                    pairs.Add(P("glowSpotPxPeak", spotPeak));
                    pairs.Add(P("glowSpotPxPeakRaw", spotPeakRaw));
                    pairs.Add(P("peakLumOff", lumOff));
                    pairs.Add(P("peakLumRest", lumRest));
                    pairs.Add(P("peakLumPeak", lumPeak));
                    pairs.Add(P("rawSaturatedPxPeak", saturatedPeak));
                    if (cue != null)
                    {
                        pairs.Add(P("periodS", cue.Period));
                        pairs.Add(P("cardEdgeMRest", restEdge[i]));
                        pairs.Add(P("cardEdgeMPeak", cue.CardEdge));
                        pairs.Add(P("cardEdgePxRest", restEdge[i] * ppm));
                        pairs.Add(P("cardEdgePxPeak", peakEdgePx));
                        pairs.Add(PR("tintRest", V4(restTint[i])));
                        pairs.Add(PR("tintPeak", V4(cue.CardTint)));
                    }

                    orbJson.Add(Obj(pairs));
                }

                // the PNG: ×3 nearest crops of the row, the rest pose above, the twinkle's peak below (1:1 pixel blocks = gameplay scale)
                int x0 = Mathf.Clamp(Mathf.RoundToInt(mid.x) - CropW / 2, 0, W - CropW), y0 = Mathf.Clamp(Mathf.RoundToInt(mid.y) - CropH / 2 + 12, 0, H - CropH);
                var sheet = new Color32[W * H];
                Blit(sheet, restLook, x0, y0, H / 2);
                Blit(sheet, peakLook, x0, y0, 0);
                for (int x = 0; x < W; x++) sheet[(H / 2) * W + x] = Ink;
                Text(sheet, W, H, 14, H - 8, n + " KOBAN ORB / " + groundWord + " / UST: DINLENME (" + F(TreeGoldOrbCue.RestLevel) + ")  X3", LabelScale, Ink, true, W - 28);
                Text(sheet, W, H, 14, H / 2 - 8, "ALT: PARILTI TEPESI (" + F(TreeGoldOrbCue.PeakValue) + ")  X3", LabelScale, Ink, true, W - 28);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 c = Px(Visual(orbs[i]).GetComponent<Renderer>().bounds.center);
                    int lx = Mathf.RoundToInt((c.x - x0) * Zoom) - Labels[i].Length * 6 * 2 / 2;
                    Text(sheet, W, H, lx, H - 52, Labels[i], 2, Ink, true, W - lx);
                    Text(sheet, W, H, lx, H / 2 - 52, Labels[i], 2, Ink, true, W - lx);
                }

                string path = Path.Combine(dir, file);
                WritePng(path, sheet, W, H);
                json.Add(Obj(P("n", n), P("file", file), P("ground", ground), P("groundSrgb", light ? LightGrey : SlateGrey), P("view", "td-rig"),
                    P("viewDetail", viewDetail), P("lookPost", _lookPost), P("amount", Amount), P("orbLights", false), P("flashScale", GameFeelSettings.FlashScale),
                    PR("cropPx", "[" + x0 + ", " + y0 + ", " + CropW + ", " + CropH + "]"), P("zoom", Zoom),
                    P("layout", "top: rest (the glint at RestLevel); bottom: the twinkle's peak; left to right NORMAL, KOBAN I, II, III"),
                    P("measure", "orbs: renderer bounds (diameterPx = largest extent x2 x px/m); orb pixels: glint-off raw vs orbs hidden, >= 26 levels within 0.7 m; glowSpotPx: post luminance up >= 0.06 vs glint off, within glowSpotRadiusPx of the card"),
                    PR("orbs", Arr(orbJson))));
                Assert.IsTrue(File.Exists(path), "kare yazılmadı: " + path);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 c = Px(Visual(orbs[i]).GetComponent<Renderer>().bounds.center);
                    OrbPixels(offRaw, bare, offLook, c, OrbRoiMetres * PpmAt(Visual(orbs[i]).GetComponent<Renderer>().bounds.center), out int area, out _);
                    Assert.Greater(area, 0, groundWord + " " + Labels[i] + ": orb karede görünmüyor");
                    if (i > 0)
                    {
                        Vector2 g = Px(cues[i].CardRenderer.transform.position);
                        Spot(peakLook, offLook, g, Mathf.Max(24f, .5f * cues[i].CardEdge * PpmAt(cues[i].CardRenderer.transform.position) + 4f), out int spot, out _, out _, out _);
                        Assert.Greater(spot, 0, groundWord + " " + Labels[i] + ": parıltı karede görünmüyor (glowSpotPx 0)");
                    }
                }
            }

            string header = Obj(P("test", "DD.Prototype.Tests.PlayMode.Wp104B43KobanTests"), P("unity", Application.unityVersion),
                P("row", TreeVfxIds.Id(TreeGoldOrbCue.Row)), P("frame", TreeGoldOrbCue.Frame), P("kobanScale", TreeGoldOrbCue.KobanScale),
                P("tierIIFrom", TreeGoldOrbCue.TierIIFrom), P("tierIIIFrom", TreeGoldOrbCue.TierIIIFrom), P("restLevel", TreeGoldOrbCue.RestLevel),
                P("peakValue", TreeGoldOrbCue.PeakValue), P("bloomThreshold", TreeVfxLadder.BloomThreshold),
                PR("glintPeriodS", "[" + F(TreeGoldOrbCue.GlintPeriod(1)) + ", " + F(TreeGoldOrbCue.GlintPeriod(2)) + ", " + F(TreeGoldOrbCue.GlintPeriod(3)) + "]"),
                PR("coinSense", "[" + F(scales[1]) + ", " + F(scales[2]) + ", " + F(scales[3]) + "]"), P("orbColour", "#" + ColorUtility.ToHtmlStringRGB(_tuning.Color)));
            File.WriteAllText(Path.Combine(dir, "koban.json"), "{\n  \"header\": " + header + ",\n  \"frames\": [\n    " + string.Join(",\n    ", json) + "\n  ]\n}\n");
            Debug.Log("[DD-TEST] wp104b43.koban frames=39,40 dir=" + dir + " lookPost=" + _lookPost);
        }

        // ------------------------------------------------------------------ helpers

        private GoldOrb Spawn(float goldScale, Vector3 at)
        {
            UseGoldScale(goldScale);
            GoldOrb orb = s_pool.Get(at, Quaternion.identity);
            Assert.IsTrue(orb != null, "test havuzu boş");
            Assert.IsTrue(orb.Begin(++_tx, Amount, 1, 1, at, null, _tuning, false), "Begin");
            _orbs.Add(orb);
            return orb;
        }

        private static void UseGoldScale(float goldScale)
        {
            TreeStats s = TreeStats.Neutral;
            s.GoldScale = goldScale;
            TreeRuntime.SetTestStats(in s);
        }

        private static float CoinSense(string id)
        {
            if (id == null) return 1f;
            TreeNodeDefV2 n = TreeCatalogV2.Find(id);
            Assert.IsNotNull(n, id + " katalogda yok");
            return n.Arg("gold", 1f);
        }

        private static Transform Visual(GoldOrb orb) => orb.transform.Find(TreeGoldOrbCue.VisualName);

        private static void AssertRow() =>
            Assert.IsTrue(TreeGoldOrbCue.RowReady, "AP-041 " + TreeVfxIds.Id(TreeGoldOrbCue.Row) + " katalogda yok: ArtAp041TreeIntake.Run (B4 kapısı)");

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, Private);
            if (f != null) f.SetValue(target, value);
        }

        private static void SetPhase(TreeGoldOrbCue[] cues, float phase)
        {
            foreach (TreeGoldOrbCue c in cues)
            {
                if (c != null) c.SetPhase(phase);
            }
        }

        private static void SetCards(TreeGoldOrbCue[] cues, bool on)
        {
            foreach (TreeGoldOrbCue c in cues)
            {
                if (c != null) c.CardEnabled = on;
            }
        }

        // ------------------------------------------------------------------ capture rig (Wp104B4AdKareTests' set-up, the TD rig view)

        private void BeginCapture()
        {
            _captured = true;
            _previousFog = RenderSettings.fog;
            RenderSettings.fog = false;
            Time.captureDeltaTime = 1f / 60f;
#if UNITY_EDITOR
            _previousAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;   // never the cyan placeholder of a variant in flight
#endif
        }

        private void BuildRig(Vector3 centre, Vector3 rigDir)
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.IsNotNull(unlit, "Universal Render Pipeline/Unlit");
            _lightMat = Unlit(unlit, new Color(LightGrey, LightGrey, LightGrey, 1f));
            _slateMat = Unlit(unlit, new Color(SlateGrey, SlateGrey, SlateGrey, 1f));
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ground.name = "WP104-B43 ground";
            ground.transform.SetParent(_root.transform, false);
            ground.transform.SetPositionAndRotation(O, Quaternion.Euler(90f, 0f, 0f));
            ground.transform.localScale = new Vector3(80f, 80f, 1f);
            _ground = ground.GetComponent<MeshRenderer>();
            _ground.sharedMaterial = _lightMat;

            var sunGo = new GameObject("WP104-B43 sun");
            sunGo.transform.SetParent(_root.transform, false);
            sunGo.transform.rotation = Quaternion.Euler(50f, 160f, 0f);   // the V6 sky's sun (as frames 27–29)
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, .886f, .776f);
            sun.intensity = 2f;
            sun.shadows = LightShadows.None;

            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Wp104B43Koban" };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false);
            _cam.transform.SetPositionAndRotation(centre + rigDir * CamDistance, Quaternion.LookRotation(-rigDir, Vector3.up));
            _cam.allowHDR = true;
            _cam.fieldOfView = Fov;
            _cam.nearClipPlane = .3f;
            _cam.farClipPlane = 200f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            int environment = DD.Prototype.Core.PrototypeLayers.Environment;
            _cam.cullingMask = environment >= 0 && environment < 32 ? ~(1 << environment) : ~0;   // never a loaded world's passes (b4-r3)
            _cam.targetTexture = _rt;
            _cam.aspect = (float)W / H;
            _cam.enabled = false;
            Type urp = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urp != null)
            {
                _camData = _cam.GetComponent(urp);
                if (_camData == null) _camData = _cam.gameObject.AddComponent(urp);
                _post = urp.GetProperty("renderPostProcessing");
                PropertyInfo aa = urp.GetProperty("antialiasing");
                if (aa != null && aa.PropertyType.IsEnum && aa.CanWrite) aa.SetValue(_camData, Enum.ToObject(aa.PropertyType, 0));
            }

            _lookPost = _post != null && BuildLookVolume();
            FeedbackCameraRef.Set(_cam);
            for (int i = 0; i < 2; i++)
            {
                Grab(false);
                if (_lookPost) Grab(true);
            }
        }

        /// <summary>The game's grade (WorldAtmosphere.EnsureVolume's runtime volume) by reflection, as Wp104B4AdKareTests.</summary>
        private bool BuildLookVolume()
        {
            MethodInfo ensure = typeof(DD.Prototype.World.WorldAtmosphere).GetMethod("EnsureVolume", BindingFlags.Public | BindingFlags.Static);
            if (ensure == null) return false;
            Component volume;
            try
            {
                volume = ensure.Invoke(null, new object[] { _root.transform, null }) as Component;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DD-TEST] wp104b43 volume: " + e.Message);
                return false;
            }

            if (volume == null) return false;
            PropertyInfo profile = volume.GetType().GetProperty("sharedProfile");
            if (profile != null && profile.GetValue(volume) is ScriptableObject so)
            {
                FieldInfo components = so.GetType().GetField("components", BindingFlags.Public | BindingFlags.Instance);
                if (components != null && components.GetValue(so) is IList list)
                {
                    foreach (object c in list)
                    {
                        if (c is Object o) _owned.Add(o);
                    }
                }

                _owned.Add(so);
            }

            return true;
        }

        private Material Unlit(Shader shader, Color colour)
        {
            var m = new Material(shader) { name = "WP104-B43 unlit" };
            m.SetColor("_BaseColor", colour);
            _owned.Add(m);
            return m;
        }

        private Color32[] Grab(bool look)
        {
            if (_post != null) _post.SetValue(_camData, look && _lookPost);
            _cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _read.Apply(false);
            RenderTexture.active = prev;
            return _read.GetPixels32();
        }

        /// <summary>The raw frame with every renderer of the orbs off (the ground alone under them).</summary>
        private Color32[] BareGrab(GoldOrb[] orbs)
        {
            var hidden = new List<Renderer>();
            foreach (GoldOrb o in orbs)
            {
                foreach (Renderer r in o.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled) continue;
                    r.enabled = false;
                    hidden.Add(r);
                }
            }

            try
            {
                return Grab(false);
            }
            finally
            {
                foreach (Renderer r in hidden) r.enabled = true;
            }
        }

        private Vector2 Px(Vector3 world)
        {
            Vector3 v = _cam.WorldToViewportPoint(world);
            return new Vector2(v.x * W, v.y * H);
        }

        private float PpmAt(Vector3 world)
        {
            Vector3 right = _cam.transform.right * .5f;
            return (Px(world + right) - Px(world - right)).magnitude;
        }

        private Vector2 ScreenSize(Bounds b)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector2 p = Px(corner);
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            return new Vector2(maxX - minX, maxY - minY);
        }

        /// <summary>The orb's own pixels within <paramref name="radius"/> of <paramref name="c"/>: the glint-off raw frame moved ≥ 26 levels
        /// against the orbs-hidden frame; their mean colour in the post frame.</summary>
        private static void OrbPixels(Color32[] raw, Color32[] bare, Color32[] look, Vector2 c, float radius, out int count, out Color32 mean)
        {
            count = 0;
            long r = 0, g = 0, b = 0;
            int ir = Mathf.CeilToInt(radius);
            for (int dy = -ir; dy <= ir; dy++)
            {
                for (int dx = -ir; dx <= ir; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int x = (int)c.x + dx, y = (int)c.y + dy;
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    int i = y * W + x;
                    if (MaxAbs(raw[i], bare[i]) < Moved) continue;
                    count++;
                    r += look[i].r;
                    g += look[i].g;
                    b += look[i].b;
                }
            }

            mean = count > 0 ? new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255) : new Color32(0, 0, 0, 255);
        }

        /// <summary>AD M9's glowSpotPx (Wp104B4AdKareTests.Spot): pixels within <paramref name="radius"/> whose luminance rose ≥ 0.06 against
        /// the glint-off frame; the peak luminance off / on; the saturated pixels (≥ 254) of <paramref name="on"/>.</summary>
        private static void Spot(Color32[] on, Color32[] off, Vector2 c, float radius, out int spot, out float peakOff, out float peakOn, out int saturated)
        {
            spot = saturated = 0;
            peakOff = peakOn = 0f;
            int ir = Mathf.CeilToInt(radius);
            for (int dy = -ir; dy <= ir; dy++)
            {
                for (int dx = -ir; dx <= ir; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int x = (int)c.x + dx, y = (int)c.y + dy;
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    int i = y * W + x;
                    float a = Lum(on[i]), z = Lum(off[i]);
                    if (a - z >= .06f) spot++;
                    peakOn = Mathf.Max(peakOn, a);
                    peakOff = Mathf.Max(peakOff, z);
                    if (Mathf.Max(on[i].r, Mathf.Max(on[i].g, on[i].b)) >= 254) saturated++;
                }
            }
        }

        private static float Lum(Color32 c) => (.2126f * c.r + .7152f * c.g + .0722f * c.b) / 255f;

        private static int MaxAbs(Color32 a, Color32 b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

        private static float HueDelta(float a, float b)
        {
            if (float.IsNaN(a) || float.IsNaN(b)) return float.NaN;
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        /// <summary>A ×<see cref="Zoom"/> nearest crop (<see cref="CropW"/> × <see cref="CropH"/> at x0, y0) into rows from
        /// <paramref name="rowBase"/> of the bottom-up sheet.</summary>
        private static void Blit(Color32[] sheet, Color32[] src, int x0, int y0, int rowBase)
        {
            for (int y = 0; y < CropH * Zoom && rowBase + y < H; y++)
            {
                for (int x = 0; x < CropW * Zoom && x < W; x++) sheet[(rowBase + y) * W + x] = src[(y0 + y / Zoom) * W + x0 + x / Zoom];
            }
        }

        private static void WritePng(string path, Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                Object.Destroy(tex);
            }
        }

        // ------------------------------------------------------------------ 5 × 7 pixel font (Wp104B4AdKareTests' labels)

        private const string GlyphChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-./:+()%=,_#<>?|";

        private static readonly byte[] Glyphs =
        {
            0x0E, 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, // A
            0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E, // B
            0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E, // C
            0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C, // D
            0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F, // E
            0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10, // F
            0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F, // G
            0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11, // H
            0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E, // I
            0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C, // J
            0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11, // K
            0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F, // L
            0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11, // M
            0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, // N
            0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E, // O
            0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10, // P
            0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D, // Q
            0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11, // R
            0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E, // S
            0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, // T
            0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E, // U
            0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04, // V
            0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A, // W
            0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11, // X
            0x11, 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, // Y
            0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F, // Z
            0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E, // 0
            0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E, // 1
            0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F, // 2
            0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E, // 3
            0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02, // 4
            0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E, // 5
            0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E, // 6
            0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08, // 7
            0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E, // 8
            0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C, // 9
            0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00, // -
            0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C, // .
            0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x00, // /
            0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00, // :
            0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00, // +
            0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02, // (
            0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08, // )
            0x18, 0x19, 0x02, 0x04, 0x08, 0x13, 0x03, // %
            0x00, 0x00, 0x1F, 0x00, 0x1F, 0x00, 0x00, // =
            0x00, 0x00, 0x00, 0x00, 0x0C, 0x04, 0x08, // ,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F, // _
            0x0A, 0x0A, 0x1F, 0x0A, 0x1F, 0x0A, 0x0A, // #
            0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02, // <
            0x08, 0x04, 0x02, 0x01, 0x02, 0x04, 0x08, // >
            0x0E, 0x11, 0x01, 0x02, 0x04, 0x00, 0x04, // ?
            0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04  // |
        };

        /// <summary>Draws <paramref name="text"/> (upper-cased, Turkish letters folded to ASCII) with its top-left at (x, top) in a bottom-up
        /// pixel buffer; <paramref name="box"/> darkens a backing box first.</summary>
        private static void Text(Color32[] buf, int w, int h, int x, int top, string text, int scale, Color32 ink, bool box, int maxWidth)
        {
            string s = Ascii(text);
            int advance = 6 * scale;
            int chars = Mathf.Min(s.Length, Mathf.Max(0, (maxWidth + scale) / advance));
            if (chars == 0) return;
            if (box)
            {
                int pad = 2 * scale;
                Shade(buf, w, h, x - pad, top - 7 * scale - pad + 1, chars * advance - scale + 2 * pad, 7 * scale + 2 * pad, .6f);
            }

            for (int i = 0; i < chars; i++)
            {
                int g = GlyphChars.IndexOf(s[i]);
                if (g < 0) continue;
                for (int row = 0; row < 7; row++)
                {
                    int bits = Glyphs[g * 7 + row];
                    for (int col = 0; col < 5; col++)
                    {
                        if (((bits >> (4 - col)) & 1) == 0) continue;
                        int px0 = x + i * advance + col * scale, py0 = top - (row + 1) * scale + 1;
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int xx = px0 + sx, yy = py0 + sy;
                                if (xx >= 0 && xx < w && yy >= 0 && yy < h) buf[yy * w + xx] = ink;
                            }
                        }
                    }
                }
            }
        }

        private static void Shade(Color32[] buf, int w, int h, int x0, int y0, int rw, int rh, float alpha)
        {
            float keep = 1f - alpha;
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y0 + rh); y++)
            {
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x0 + rw); x++)
                {
                    Color32 c = buf[y * w + x];
                    buf[y * w + x] = new Color32((byte)(c.r * keep), (byte)(c.g * keep), (byte)(c.b * keep), 255);
                }
            }
        }

        private static string Ascii(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                char c = char.ToUpperInvariant(ch);
                switch (c)
                {
                    case 'Ç': c = 'C'; break;
                    case 'Ğ': c = 'G'; break;
                    case 'İ': c = 'I'; break;
                    case 'Ö': c = 'O'; break;
                    case 'Ş': c = 'S'; break;
                    case 'Ü': c = 'U'; break;
                    case '×': c = 'X'; break;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ JSON (InvariantCulture, NaN → null)

        private static string Q(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }

            return sb.Append('"').ToString();
        }

        private static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("0.####", CultureInfo.InvariantCulture);

        private static string P(string key, string value) => Q(key) + ": " + (value == null ? "null" : Q(value));

        private static string P(string key, float value) => Q(key) + ": " + F(value);

        private static string P(string key, int value) => Q(key) + ": " + value.ToString(CultureInfo.InvariantCulture);

        private static string P(string key, bool value) => Q(key) + ": " + (value ? "true" : "false");

        private static string PR(string key, string rawJson) => Q(key) + ": " + rawJson;

        private static string V4(Vector4 v) => "[" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ", " + F(v.w) + "]";

        private static string Obj(params string[] pairs) => "{" + string.Join(", ", pairs) + "}";

        private static string Obj(List<string> pairs) => "{" + string.Join(", ", pairs) + "}";

        private static string Arr(List<string> items) => "[" + string.Join(", ", items) + "]";
    }
}
