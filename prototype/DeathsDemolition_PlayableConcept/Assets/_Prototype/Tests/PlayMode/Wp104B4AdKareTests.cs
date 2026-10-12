using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using DD.Prototype.Weapons.Attachments;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-104 B4 (S-07) · the art director's review frames of the run tree's VFX at the game's top-down scale: a straight-down camera
    /// 20 m up (FOV 31.7°, 1920 × 1080 ≈ 95 px/m on the ground, ≈ 100 px/m at the 1 m tracer height — the A5 / Wp101TdIzZeminTests
    /// set-up) over a light-stone (sRGB 0.75) and a wet-slate (0.20) ground, the whole frame or split west | east at the frame centre.
    /// <list type="number">
    /// <item>01–07, 30–32 tracers (synthetic TD records through the real DrawTd layers, as Wp104B4VfxPlayTests; the marked round a
    /// real projectile): the Shot ladder I–V (AR, neutral) on each ground; K1 element tracers at tier I and tier V (Shot = Element =
    /// the tier) beside the neutral one; K3's marked chaos round beside a normal round; fire V and WP-101's neutral AR tracer with
    /// Reduce Flashes off / on; LMG and SR at tiers I and V on each ground; A11's blind frame (AR I / III / V shuffled, lanes A / B / C,
    /// the key in b4ad.json).</item>
    /// <item>08–26 (need the imported AP-041 tree rows, as the B4 gate): B5 explosive round, B7 mini nuke, B8 chain blast, B1 fire
    /// ground (+ GroundFireSystem), B4 venom cloud — each centred on the seam, captured at fixed game times — then, through the S-09
    /// preview on a hero stand-in: the B15 leech thread, the B11 aura levels I–III and the X8 ensō on each ground.</item>
    /// <item>27–29 S-06's add-on glow (E4 fire III + E5 V on ar_1 through the MaterialPropertyBlock) at TD scale on each ground and
    /// ×4 glow off | on, seen from the game's TD rig angle ("td-rig": the glow slot is each add-on's side-facing window, which a
    /// straight-down camera never sees), measured on the glow-slot pixels (ignored while DD_WeaponAttach is not built, as
    /// Wp104B4VfxPlayTests).</item>
    /// </list>
    /// Numbers come from the raw render (HDR camera without post-processing: A5's bright-core definition, WP-101's streak / warm
    /// metrics; an ARGBHalf twin for the unclamped peaks); the PNGs are the same frame through the game's post stack
    /// (WorldAtmosphere's runtime volume: Neutral tonemap, bloom 1.25). The "dark frame first" rule of Wp101MermiSinifTdTests holds —
    /// a DrawMeshInstanced queued for one render stays in every later render of that Unity frame — and so every tracer capture starts
    /// on a Unity frame of its own (AD-B4 M2: b4-r2's frames 02 and 07 ran in the frame the previous capture had queued its halo
    /// pass in, and measured that halo as their core). Output DD_B4_AD_DIR (nothing is written when unset; the asserts still run):
    /// NN-what.png, b4ad.json (per-frame measures + "ad": every AD-B4 MUST / SHOULD threshold evaluated on this run), contact.png (a
    /// labelled 4-column sheet under a MUST / SHOULD summary line); DD_B4_AD_RAW=1 adds NN-what-raw.png. The asserts only say that
    /// every frame was rendered and is not empty — PASS / FAIL against the AD's thresholds is written to the JSON, never asserted.
    /// </summary>
    [TestFixture, Category("WP104"), Category("TD"), Category("Diagnostics")]
    public sealed class Wp104B4AdKareTests
    {
        private const string HedefliKosu = "AD inceleme kareleri (WP-104 B4): yalnız hedefli koşulur";
        private const int W = 1920, H = 1080, TW = 480, TH = 270, LabelH = 22, Columns = 4;
        private const float CamHeight = 20f, Fov = 31.7f, LaneY = 1f, LightGrey = .75f, SlateGrey = .20f;
        /// <summary>Wp101: a channel moved this much (0.10) against the frame without the effect = the effect's own pixel.</summary>
        private const int Moved = 26;
        /// <summary>AD-B4 §1 (12.10): WP-101's AR class base at 1080p — bright core 8 px, halo 44 px — and the halo's step a tier
        /// (+5 px: I 44 … V 64).</summary>
        private const int ArCorePx = 8, ArHaloPx = 44, HaloStepPx = 5;
        /// <summary>How far behind its head a synthetic TD record starts (no streak is drawn longer than that): a split frame's half
        /// holds 7.5 m (AR tier V 4.33 m × 1.45 ≈ 6.3 m), a whole-frame ground 12 m (the sniper's 10 m). A round's length band reaches
        /// back as far (≤ 10.5 m) — b4-r2 cut it at the tier-I trail, so a longer streak could never measure longer.</summary>
        private const float FlightSplit = 7.5f, FlightWhole = 12f, BandReachMax = 10.5f;
        /// <summary>b4-r2 (12.10, the run the AD reviewed), clean frames only: WP-101's AR tier-I streak length (band bboxM[0]) on the
        /// light stone (frame 01) and the wet slate (frame 03 NOTR I — frame 02 measured the leak).</summary>
        private const float RefLengthLight = 4.1053f, RefLengthWet = 4.2951f;
        /// <summary>b4-r2's wet-slate B1 fire ground at t 0.3 / 1.5 s (M8: the wet slate moves ≤ ±10 %).</summary>
        private static readonly float[] RefFireWetSat = { .6219f, .6244f }, RefFireWetWarm = { .9762f, .946f }, RefFireWetHue = { 21.6153f, 22.5246f };
        private const string KeyStone = "lightStone", KeySlate = "wetSlate";
        private static readonly string[] Grounds = { KeyStone, KeySlate };
        private const string KeyRfFire = "ATES V", KeyRfNeutral = "NOTR (WP-101)", KeyNormal = "NORMAL", KeyMarked = "K3 ISARETLI";
        private static readonly string[] BlindNames = { "A", "B", "C" };
        private static readonly TreeElement[] Elements = { TreeElement.Fire, TreeElement.Ice, TreeElement.Shock, TreeElement.Venom };
        private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        // far from any scene an earlier test left loaded (Wp101TdIzZeminTests: the Run scene's plaza filled the camera)
        private static readonly Vector3 O = new Vector3(5600f, 0f, -5600f);
        private static readonly Color32 Ink = new Color32(242, 240, 234, 255);
        private static readonly Color HeroGrey = new Color(.16f, .15f, .14f, 1f), VictimGrey = new Color(.27f, .30f, .24f, 1f);
        private static readonly SortedDictionary<int, Frame> s_frames = new SortedDictionary<int, Frame>();
        private static readonly List<RoundM> s_rounds = new List<RoundM>();
        private static readonly List<HalfM> s_halves = new List<HalfM>();
        private static readonly List<GlowM> s_glows = new List<GlowM>();
        private static readonly SortedDictionary<int, int> s_darkLeak = new SortedDictionary<int, int>();
        private static string s_header, s_blindKey;

        private enum Floor { Light, Slate, Split }

        private sealed class Frame
        {
            public int N;
            public string File, Label, Json;
            public Color32[] Tile;
        }

        private struct Mark
        {
            public int X, Top;
            public string Text;
        }

        private sealed class Lane
        {
            /// <summary>Label: what the frame shows; Key: the lane's identity for the AD checks (the blind frame's A / B / C hide it).</summary>
            public string Label, Key;
            public TreeStats Stats;
            public int ShotTier, ElementTier, Look, HaloTier;
            public TreeElement Element;
            public ClassicWeaponId Weapon = ClassicWeaponId.AssaultRifle;
            public float Z, CoreScale, HaloScale, Speed;
            /// <summary>The ladder's readouts at the lane's birth when TracerStyle has them (HaloPx, TrailScale, HaloHdrBoost), else NaN.</summary>
            public float StyleHaloPx = float.NaN, StyleTrail = float.NaN, StyleHaloHdr = float.NaN;
            public bool Marked;
            public readonly List<Rec> Recs = new List<Rec>();
        }

        private struct Rec
        {
            public int Id;
            public Vector3 Head;
            /// <summary>'C' the whole-frame ground, 'L' the west half (light stone when split), 'R' the east half (wet slate).</summary>
            public char Half;
            /// <summary>Trail: the WP-101 one-exposure streak at tier I (where the samples stop); Flight: how far behind the head the
            /// record starts (the length band's reach).</summary>
            public float Trail, Flight;
        }

        private struct Stats
        {
            public int Changed, Warm, Coloured, Saturated, MinX, MinY, MaxX, MaxY;
            public float LumStep, PeakLum, SatSum, HueX, HueY;
        }

        /// <summary>One tracer frame's renders: ground only (raw / post), layer 0 alone on black, every layer (raw / post / HDR
        /// luminance), the halos alone on black (taken on the next Unity frame).</summary>
        private sealed class Shots
        {
            public Color32[] Dark, DarkLook, Core, Lit, Look, Halo;
            public float[] Hdr;
        }

        /// <summary>One round of a tracer frame (one lane on one ground): what the AD thresholds read.</summary>
        private sealed class RoundM
        {
            public int Frame, ShotTier, ElementTier, HaloTier;
            public string Lane, Key, Ground;
            public ClassicWeaponId Weapon;
            public TreeElement Element;
            public bool Marked;
            public float Flash;
            public int BrightCore, BrightCoreMax, CoreQuad, Halo, HaloMax, Visible, VisibleMax, VisiblePost, InkDark05, InkDark15, GlowPx;
            public float LengthM, PeakLum, PeakLumHdr, PeakLumPost, SatMean, HueMean, ColouredShare, LumStep, WarmShare;
            public Color32 GlowMedianPost;
        }

        /// <summary>One half of a proc / aura / ensō frame.</summary>
        private sealed class HalfM
        {
            public int Frame, Level;
            public string Kind, Key;
            public float T, Ppm, EdgeLum;
            public Stats S;
            public Radial R;
        }

        /// <summary>One add-on (E4 / E5) on one ground, FlashScale 0 → 1.</summary>
        private sealed class GlowM
        {
            public string Ground, AddOn, Name;
            public bool Present;
            public float Emission, AreaPx, SizeX, SizeY, CentreX, CentreY, PeakOn, PeakOff;
            public float MeanLumOff = float.NaN, MeanLumOn = float.NaN, MeanHdrOff = float.NaN, MeanHdrOn = float.NaN, SlotOff = float.NaN, SlotOn = float.NaN;
            public int MaskPx, GlowSpot, RawSaturated;
        }

        /// <summary>A half's changed pixels around the effect centre: count, radius holding 98 % / all of them (px), the post frame's mean
        /// colour over them and its gold pixels (A17: HSV hue 35–65°, S &gt; 0.30), the raw frame's gold pixels.</summary>
        private struct Radial
        {
            public int N, GoldPost, GoldRaw;
            public float R98, RMax;
            public Color32 MeanPost;
        }

        private readonly List<KeyValuePair<FieldInfo, object>> _savedDelegates = new List<KeyValuePair<FieldInfo, object>>();
        private readonly List<Object> _owned = new List<Object>();
        private readonly List<string> _written = new List<string>();
        private GameObject _root;
        private Camera _cam;
        private Component _camData;
        private PropertyInfo _post;
        private RenderTexture _rt, _rtHdr;
        private Texture2D _read, _readHdr;
        private float[] _hdrLum;
        private Shader _unlit;
        private MeshRenderer _west, _east;
        private Material _lightMat, _slateMat;
        private Floor _floor;
        private bool _lookPost, _raw, _usedDecals, _hasFireCamera, _hdrOk;
        private string _dir, _modelNote;
        private int _nextId = 41000;
        private TracerRibbonRenderer _tracer, _previousTracer;
        private bool _previousTracerEnabled, _previousInterimTrails, _hasTracerFixture;
        private CameraMode _previousMode;
        private float _previousFlash, _previousTimeScale;
        private VfxTierLevel? _previousTier;
        private Camera _previousFeedbackCamera, _previousFireCamera;
        private bool _previousFog, _previousReducedMotion, _previousAsync;

        [OneTimeSetUp]
        public void Once()
        {
            s_frames.Clear();
            s_rounds.Clear();
            s_halves.Clear();
            s_glows.Clear();
            s_darkLeak.Clear();
            s_header = null;
            s_blindKey = null;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _dir = Environment.GetEnvironmentVariable("DD_B4_AD_DIR");
            _raw = Environment.GetEnvironmentVariable("DD_B4_AD_RAW") == "1";
            if (!string.IsNullOrEmpty(_dir)) Directory.CreateDirectory(_dir);
            _written.Clear();
            _previousMode = CameraModeService.Current;
            _previousFlash = GameFeelSettings.FlashScale;
            _previousTimeScale = Time.timeScale;
            _previousTier = VfxTier.Override;
            _previousFeedbackCamera = FeedbackCameraRef.Current;
            _previousFog = RenderSettings.fog;
            _previousReducedMotion = DD.Prototype.UI.UiMotion.ReducedMotion;
#if UNITY_EDITOR
            _previousAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;   // a capture must never hold the cyan placeholder of a variant in flight
#endif
            SetModeState(CameraMode.TopDown);
            GameFeelSettings.FlashScale = 1f;
            Time.timeScale = 1f;
            Time.captureDeltaTime = 1f / 60f;   // game time 60 Hz whatever the batch frame costs (the proc frames' ages)
            VfxTier.Override = VfxTierLevel.High;
            DD.Prototype.UI.UiMotion.ReducedMotion = false;
            RenderSettings.fog = false;
            VfxClock.ResetForTests();
            TracerStyle.ResetForTests();
            TreeRuntime.ClearTestStats();
            BuildRig();
            for (int i = 0; i < 3; i++)   // warm-up: the post stack's first frame, the shader variants, the HDR twin
            {
                Grab(false, true);
                if (_lookPost) Grab(true, true);
                if (i == 0) GrabHdrLum();
                yield return null;
            }

            if (s_header == null) s_header = Header();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            WriteOutputs();
            TreeVfxPreview.End();
            VfxClock.ResetForTests();
            TreeRuntime.ClearTestStats();
            TracerStyle.ResetForTests();
            ProjectileSystem.Clear();
            TreeVfxBinder binder = TreeVfxBinder.Instance;
            if (binder != null)
            {
                binder.Clear();
                binder.ResetCountersForTests();
            }

            GroundFireSystem flames = GroundFireSystem.Instance;
            if (flames != null && _hasFireCamera)
            {
                flames.Clear();
                flames.CameraOverride = _previousFireCamera;
            }

            if (_usedDecals) ReleaseDecals();
            if (_hasTracerFixture)
            {
                typeof(TracerRibbonRenderer).GetField("s_instance", StaticFields).SetValue(null, _previousTracer);
                if (_previousTracer != null) _previousTracer.enabled = _previousTracerEnabled;
                ProjectileDriver.DrawInterimTrails = _previousInterimTrails;
            }

            _tracer = _previousTracer = null;
            _hasTracerFixture = _usedDecals = _hasFireCamera = false;
            if (_cam != null) _cam.targetTexture = null;
            if (_root != null) Object.Destroy(_root);
            ReleaseTarget(ref _rt);
            ReleaseTarget(ref _rtHdr);
            if (_read != null) Object.Destroy(_read);
            if (_readHdr != null) Object.Destroy(_readHdr);
            _read = _readHdr = null;
            _hdrLum = null;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i] != null) Object.Destroy(_owned[i]);
            }

            _owned.Clear();
            for (int i = _savedDelegates.Count - 1; i >= 0; i--) _savedDelegates[i].Key.SetValue(null, _savedDelegates[i].Value);
            _savedDelegates.Clear();
            if (binder != null) binder.Rearm();
            GameFeelSettings.FlashScale = _previousFlash;
            Time.timeScale = _previousTimeScale;
            Time.captureDeltaTime = 0f;
            VfxTier.Override = _previousTier;
            FeedbackCameraRef.Set(_previousFeedbackCamera);
            RenderSettings.fog = _previousFog;
            DD.Prototype.UI.UiMotion.ReducedMotion = _previousReducedMotion;
#if UNITY_EDITOR
            UnityEditor.ShaderUtil.allowAsyncCompilation = _previousAsync;
#endif
            SetModeState(_previousMode);
            yield return null;
        }

        private static void ReleaseTarget(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.Destroy(rt);
            rt = null;
        }

        // ------------------------------------------------------------------ 01–07, 30–32 tracers (no catalog rows needed)

        [UnityTest, Order(1), Explicit(HedefliKosu), Timeout(300000)]
        public IEnumerator Kare01_07_Iz_AtisMerdiveni_Element_K3_ReduceFlashes()
        {
            LogAssert.ignoreFailingMessages = true;
            TracerRibbonRenderer tracer = AttachTdTracer();
            ProjectileSystem.Clear();
            tracer.Clear();

            // 01 · 02 — the Shot ladder I–V (AR, neutral element): five lanes in one framing, light stone, then the same records on wet slate
            var ladder = new List<Lane>();
            for (int t = 1; t <= 5; t++) ladder.Add(NewLane("ATIS " + Roman(t), t, 0, TreeElement.None, LaneZ(t - 1, 5)));
            foreach (Lane lane in ladder) RecordLane(tracer, lane, false);
            SetFloor(Floor.Light);
            yield return CaptureTracers(tracer, 1, "01-iz-atis-merdiveni-acik-tas.png", "01 ATIS I-V (AR, NOTR) / ACIK TAS", ladder);
            SetFloor(Floor.Slate);
            yield return CaptureTracers(tracer, 2, "02-iz-atis-merdiveni-islak-arduvaz.png", "02 ATIS I-V (AR, NOTR) / ISLAK ARDUVAZ", ladder);

            // 03 · 04 — K1 element tracers at tier I and tier V (Shot = Element = the tier), the neutral one on top, split ground
            for (int pass = 0; pass < 2; pass++)
            {
                int tier = pass == 0 ? 1 : 5;
                tracer.Clear();
                yield return null;
                var lanes = new List<Lane>
                {
                    NewLane("NOTR " + Roman(tier), tier, 0, TreeElement.None, LaneZ(0, 5)),
                    NewLane(ElementWord(TreeElement.Fire) + " " + Roman(tier), tier, tier, TreeElement.Fire, LaneZ(1, 5)),
                    NewLane(ElementWord(TreeElement.Ice) + " " + Roman(tier), tier, tier, TreeElement.Ice, LaneZ(2, 5)),
                    NewLane(ElementWord(TreeElement.Shock) + " " + Roman(tier), tier, tier, TreeElement.Shock, LaneZ(3, 5)),
                    NewLane(ElementWord(TreeElement.Venom) + " " + Roman(tier), tier, tier, TreeElement.Venom, LaneZ(4, 5))
                };
                foreach (Lane lane in lanes) RecordLane(tracer, lane, true);
                SetFloor(Floor.Split);
                yield return CaptureTracers(tracer, 3 + pass, pass == 0 ? "03-iz-element-kademe-I.png" : "04-iz-element-kademe-V.png",
                    (pass == 0 ? "03" : "04") + " ELEMENT IZLERI " + Roman(tier) + " (ATIS=ELEMENT=" + Roman(tier) + ")", lanes);
            }

            // 05 — K3: the marked chaos round (a real projectile carrying RunTreeHooks.TreeRoundMarkedChaos) beside a normal round
            tracer.Clear();
            TreeRuntime.SetTestStats(TreeStats.Neutral);
            ProjectileSystem.Clear();
            yield return null;
            float speed = WeaponSpeed(ClassicWeaponId.AssaultRifle, out _);
            float trail = NominalTrail(ClassicWeaponId.AssaultRifle, speed);
            Lane normal = NewLane(KeyNormal, 1, 0, TreeElement.None, 1.5f);
            Lane marked = NewLane(KeyMarked, 1, 0, TreeElement.None, -1.5f);
            marked.Marked = true;
            foreach (Lane lane in new[] { normal, marked })
            {
                lane.Look = lane.Marked ? TracerStyle.MarkedLook : TracerStyle.NeutralLook;
                lane.CoreScale = TracerStyle.CoreScale;   // the neutral tree's ladder (×1): K3 changes the palette and the ceiling only
                lane.HaloScale = TracerStyle.HaloScale;
                lane.HaloTier = TracerStyle.HaloTier;
                ReadStyle(lane);
                lane.Speed = speed;
                foreach (char half in new[] { 'L', 'R' })
                {
                    int generation = SpawnReal(O + new Vector3(HeadX(half), LaneY, lane.Z), speed, FlightSplit, lane.Marked ? RunTreeHooks.TreeRoundMarkedChaos : 0);
                    lane.Recs.Add(new Rec { Id = generation, Half = half, Trail = trail, Flight = FlightSplit });
                }
            }

            ProjectileSystem.Step(FlightSplit / speed);
            tracer.TickTd(FlightSplit / speed);
            foreach (Lane lane in new[] { normal, marked })
            {
                for (int i = 0; i < lane.Recs.Count; i++)
                {
                    Rec r = lane.Recs[i];
                    r.Head = ProjectilePosition(r.Id);
                    lane.Recs[i] = r;
                }
            }

            SetFloor(Floor.Split);
            yield return CaptureTracers(tracer, 5, "05-iz-k3-isaretli-mermi.png", "05 K3 ISARETLI MERMI / NORMAL MERMI", new List<Lane> { normal, marked });
            ProjectileSystem.Clear();

            // 06 · 07 — Reduce Flashes off / on (FlashScale 1 / 0.4), the same records: fire V and WP-101's neutral AR tracer (AD-B4
            // M3: the same code path dims the neutral tracer)
            tracer.Clear();
            yield return null;
            var rf = new List<Lane> { NewLane(KeyRfFire, 5, 5, TreeElement.Fire, LaneZ(0, 2)), NeutralLane(KeyRfNeutral, LaneZ(1, 2)) };
            foreach (Lane lane in rf) RecordLane(tracer, lane, true);
            SetFloor(Floor.Split);
            GameFeelSettings.FlashScale = 1f;
            yield return CaptureTracers(tracer, 6, "06-iz-ates-V-reduce-flashes-kapali.png", "06 ATES V + NOTR (WP-101) / REDUCE FLASHES KAPALI", rf);
            GameFeelSettings.FlashScale = .4f;
            yield return CaptureTracers(tracer, 7, "07-iz-ates-V-reduce-flashes-acik.png", "07 ATES V + NOTR (WP-101) / REDUCE FLASHES ACIK (0.4)", rf);
            GameFeelSettings.FlashScale = 1f;

            // 30 · 31 — M1's extra lanes: LMG and SR at tiers I (their WP-101 base) and V (neutral), each ground on the whole frame
            tracer.Clear();
            TreeRuntime.ClearTestStats();
            yield return null;
            var heavy = new List<Lane>
            {
                NewLane("LMG I", 1, 0, TreeElement.None, LaneZ(0, 4), ClassicWeaponId.Lmg1),
                NewLane("LMG V", 5, 0, TreeElement.None, LaneZ(1, 4), ClassicWeaponId.Lmg1),
                NewLane("SR I", 1, 0, TreeElement.None, LaneZ(2, 4), ClassicWeaponId.Sr1),
                NewLane("SR V", 5, 0, TreeElement.None, LaneZ(3, 4), ClassicWeaponId.Sr1)
            };
            foreach (Lane lane in heavy) RecordLane(tracer, lane, false);
            SetFloor(Floor.Light);
            yield return CaptureTracers(tracer, 30, "30-iz-lmg-sr-I-V-acik-tas.png", "30 LMG / SR I + V (NOTR) / ACIK TAS", heavy);
            SetFloor(Floor.Slate);
            yield return CaptureTracers(tracer, 31, "31-iz-lmg-sr-I-V-islak-arduvaz.png", "31 LMG / SR I + V (NOTR) / ISLAK ARDUVAZ", heavy);

            // 32 — A11: AR neutral I / III / V in a shuffled (never monotonic) order, labelled A / B / C; the key goes to b4ad.json only
            tracer.Clear();
            TreeRuntime.ClearTestStats();
            yield return null;
            int seed = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
            int[] order = BlindOrder(seed);
            var blind = new List<Lane>();
            for (int i = 0; i < 3; i++)
            {
                Lane lane = NewLane(BlindNames[i], order[i], 0, TreeElement.None, LaneZ(i, 3));
                lane.Key = "ATIS " + Roman(order[i]);
                blind.Add(lane);
            }

            foreach (Lane lane in blind) RecordLane(tracer, lane, true);
            s_blindKey = Obj(P("frame", 32), P(BlindNames[0], Roman(order[0])), P(BlindNames[1], Roman(order[1])), P(BlindNames[2], Roman(order[2])), P("seed", seed));
            SetFloor(Floor.Split);
            yield return CaptureTracers(tracer, 32, "32-iz-a11-kor-siralama.png", "32 A11 KOR SIRALAMA: A / B / C (ANAHTAR B4AD.JSON)", blind);
            tracer.Clear();
            TreeRuntime.ClearTestStats();
            AssertWritten();
        }

        // ------------------------------------------------------------------ 08–26 procs, aura, ensō (need the AP-041 tree rows)

        [UnityTest, Order(2), Explicit(HedefliKosu), Timeout(300000)]
        public IEnumerator Kare08_26_Proc_Aura_Enso()
        {
            LogAssert.ignoreFailingMessages = true;
            TreeVfxBinder binder = Binder();
            _usedDecals = true;
            GroundFireSystem flames = GroundFireSystem.Instance;
            if (flames != null)
            {
                _previousFireCamera = flames.CameraOverride;
                _hasFireCamera = true;
                flames.CameraOverride = _cam;   // its lights and heat haze pick the nearest zones to this camera
            }

            SetFloor(Floor.Split);
            yield return null;
            Physics.SyncTransforms();

            // S-04: a blast's Point is the hit (a body ≈ 1 m up); the ground layers raycast under it. 0.3 m north (+z, screen up) of the
            // camera axis: a camera-facing card right under a straight-down camera has no defined roll (LookRotation(down, up)); a card
            // off the axis takes its up from the offset's direction (FlipbookEffect.Face), so north of it the sheet stands screen-up, as
            // it does under the game's tilted TD camera
            Vector3 body = O + new Vector3(0f, 1f, .3f);
            yield return Proc(binder, flames, 8, "b5-patlayici-mermi", "B5 PATLAYICI MERMI R2 III", Info(TreeProcKind.ExplosiveRound, body, 2f, 3), new[] { .05f, .15f, .35f }, 10f);
            yield return Proc(binder, flames, 11, "b7-mini-nukleer", "B7 MINI NUKLEER R5 III", Info(TreeProcKind.MiniNuke, body, 5f, 3), new[] { .1f, .5f, 1.5f }, 0f);
            TreeProcInfo chain = Info(TreeProcKind.ChainBlast, body, 3f, 3);
            chain.Depth = 1;   // S-04: a kill's first chain generation is 1 (full size + the sumi layer)
            yield return Proc(binder, flames, 14, "b8-zincir-patlama", "B8 ZINCIR PATLAMA K1 III", chain, new[] { .05f, .15f, .35f }, 8f);
            TreeProcInfo fire = Info(TreeProcKind.FireGround, O, 2.5f, 2);
            fire.Element = TreeElement.Fire;
            fire.Damage = 12f;
            fire.Seconds = 3f;   // S-04 §5: the zone's life
            yield return Proc(binder, flames, 17, "b1-ates-zemini", "B1 ATES ZEMINI R2.5 3S", fire, new[] { .3f, 1.5f }, 10f);
            TreeProcInfo cloud = Info(TreeProcKind.VenomCloud, O, 2f, 2);
            cloud.Element = TreeElement.Venom;
            cloud.Seconds = 4f;
            yield return Proc(binder, flames, 19, "b4-zehir-bulutu", "B4 ZEHIR BULUTU R2 4S", cloud, new[] { .3f, 1.5f }, 10f);

            // the player-side layers anchor on a mannequin through the S-09 preview (TreeVfxPreview: the unscaled presentation clock)
            Transform hero = StandIn("WP104-AD kahraman", O + new Vector3(-1.8f, 0f, 0f), HeroGrey);
            Transform victim = StandIn("WP104-AD kurban", O + new Vector3(1.8f, 0f, 0f), VictimGrey);
            yield return null;
            Physics.SyncTransforms();
            // the preview ages its effects on Time.unscaledDeltaTime and VfxClock.Now on Time.unscaledTime; captureDeltaTime fixes the
            // game clock only (ders: batch kare süresi) — real time from here on keeps the waits and the effects' ages on one clock
            Time.captureDeltaTime = 0f;
            TreeVfxPreview.Begin(TreeStats.Neutral, hero);
            yield return null;
            yield return Proc(binder, flames, 21, "b15-can-emme", "B15 CAN EMME IPLIGI", Info(TreeProcKind.Leech, victim.position, 0f, 2), new[] { .15f }, 8f);
            victim.gameObject.SetActive(false);
            hero.position = O;
            Physics.SyncTransforms();
            yield return Aura(binder, 22);
            yield return Enso(binder, hero, 25, Floor.Light);
            yield return Enso(binder, hero, 26, Floor.Slate);
            TreeVfxPreview.End();
            AssertWritten();
        }

        // ------------------------------------------------------------------ 27–29 S-06 add-on glow (needs DD_WeaponAttach)

        [UnityTest, Order(3), Explicit(HedefliKosu), Timeout(180000)]
        public IEnumerator Kare27_29_EkIsimasi_E4AtesIII_E5V()
        {
            LogAssert.ignoreFailingMessages = true;
            WeaponAttachLibrary.ResetCache();
            WeaponAttachLibrary lib = WeaponAttachLibrary.Current;
            if (lib == null) Assert.Ignore("DD_WeaponAttach henüz kurulmadı (Resources/" + WeaponAttachLibrary.ResourcePath + "): S-06 ArtAp040WeaponAttachBuilder");
            WeaponAttachLibrary.Row row = lib.Find(ClassicWeaponId.AssaultRifle);
            Assert.IsNotNull(row, "ar_1 satırı (DD_WeaponAttach)");
            SaveAndClearDelegates(typeof(WeaponAttachEvents));   // only this capture's glow listens
            var sunGo = new GameObject("WP104-AD sun");
            sunGo.transform.SetParent(_root.transform, false);
            sunGo.transform.rotation = Quaternion.Euler(50f, 160f, 0f);   // the V6 sky's sun (WorldAtmosphere.V6Sky.SunEuler)
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, .886f, .776f);
            sun.intensity = 2f;
            sun.shadows = LightShadows.None;
            var glowGo = new GameObject("WP104-AD glow");
            glowGo.transform.SetParent(_root.transform, false);
            TreeAttachGlow glow = glowGo.AddComponent<TreeAttachGlow>();
            Transform model = WeaponModel();
            WeaponAttachMount mount = WeaponAttachMount.Ensure(model, row);
            Assert.IsNotNull(mount, "WeaponAttachMount (ar_1)");
            var set = new WeaponAttachSet { Side = TreeElement.Fire, SideTier = 3, Ring = 1, RingTier = 5 };
            mount.Apply(in set, lib);   // S-06 raises WeaponAttachEvents.Changed → the ladder
            yield return null;

            Bounds? all = BoundsOf(model.gameObject);
            Vector3 centre = all.HasValue ? all.Value.center : model.position;
            // AD-B4 M9 (C, 12.10): S-06's glow slot is each add-on's side-facing window — from straight above E4 V01 shows 0 px of it and
            // E5 V01 ≈ 3 px, which is why b4-r2's 27–29 did not move. These frames look from the game's TD rig angle (DD_FeelConfig
            // height / back / yaw, ≈ 49°) at this capture's own distance and FOV (≈ 95 px/m on the weapon); the ground halves grow so
            // the rig's longer view never runs off them.
            CameraFeel td = FeelRuntime.Current.Camera;
            Vector3 rigDir = (Quaternion.Euler(0f, td.yawDegrees, 0f) * new Vector3(0f, td.heightMeters, -td.backDistanceMeters)).normalized;
            _cam.transform.SetPositionAndRotation(centre + rigDir * CamHeight, Quaternion.LookRotation(-rigDir, Vector3.up));
            _west.transform.SetPositionAndRotation(O + new Vector3(-30f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f));
            _west.transform.localScale = new Vector3(60f, 80f, 1f);
            _east.transform.SetPositionAndRotation(O + new Vector3(30f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f));
            _east.transform.localScale = new Vector3(60f, 80f, 1f);
            yield return null;
            string viewDetail = "TD rig angle (DD_FeelConfig height " + F(td.heightMeters) + " / back " + F(td.backDistanceMeters) + " / yaw " + F(td.yawDegrees)
                                + "): camera along (" + F(rigDir.x) + ", " + F(rigDir.y) + ", " + F(rigDir.z) + ") x " + F(CamHeight) + " m from the weapon, FOV " + F(Fov);
            Vector2 c = Px(centre);
            // M9's "ek maskesi": each add-on's glow-slot pixels as the rig sees them (GlowMask: slot 1 white, the rest black, unlit)
            bool[] sideMask = GlowMask(mount.SideAttachment, out int sidePx), ringMask = GlowMask(mount.RingAttachment, out int ringPx);
            const int cw = 160, ch = 90, k = 4;   // ×4 nearest-neighbour crops of the same 160 × 90 px around the weapon
            int cellW = cw * k, cellH = ch * k, gw = cellW * 2, gh = cellH * 2;
            var grid = new Color32[gw * gh];   // light stone above, wet slate below; glow off | glow on
            var gridMarks = new List<Mark>();
            var scratch = new MaterialPropertyBlock();
            int glowChanged = 0;
            s_glows.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                Floor floor = pass == 0 ? Floor.Light : Floor.Slate;
                string groundKey = pass == 0 ? KeyStone : KeySlate;
                SetFloor(floor);

                // ON first: FlashScale 1 and TreeAttachGlow repainted (it compares Reduce Flashes once a frame)
                yield return SetFlash(glow, 1f);
                Color sideOn = glow.SideEmission, ringOn = glow.RingEmission;
                Color32[] onRaw = Grab(false, true);
                Color32[] onLook = _lookPost ? Grab(true, true) : onRaw;
                float[] hdr = GrabHdrLum();
                var e4 = new GlowM
                {
                    Ground = groundKey, AddOn = "E4", Name = "E4 ates III (side)", Emission = sideOn.r, MaskPx = sidePx,
                    MeanLumOn = MeanLum(onRaw, sideMask), MeanHdrOn = MeanOf(hdr, sideMask), SlotOn = SlotEmission(mount.SideAttachment, scratch)
                };
                var e5 = new GlowM
                {
                    Ground = groundKey, AddOn = "E5", Name = "E5 V (ring)", Emission = ringOn.r, MaskPx = ringPx,
                    MeanLumOn = MeanLum(onRaw, ringMask), MeanHdrOn = MeanOf(hdr, ringMask), SlotOn = SlotEmission(mount.RingAttachment, scratch)
                };

                // OFF: the ladder × 0 — the same model and add-ons, the glow slot black
                yield return SetFlash(glow, 0f);
                Color32[] offRaw = Grab(false, true);
                Color32[] offLook = _lookPost ? Grab(true, true) : offRaw;
                hdr = GrabHdrLum();
                e4.MeanLumOff = MeanLum(offRaw, sideMask);
                e4.MeanHdrOff = MeanOf(hdr, sideMask);
                e4.SlotOff = SlotEmission(mount.SideAttachment, scratch);
                e5.MeanLumOff = MeanLum(offRaw, ringMask);
                e5.MeanHdrOff = MeanOf(hdr, ringMask);
                e5.SlotOff = SlotEmission(mount.RingAttachment, scratch);
                Spot(e4, mount.SideAttachment, onLook, offLook, onRaw);
                Spot(e5, mount.RingAttachment, onLook, offLook, onRaw);
                s_glows.Add(e4);
                s_glows.Add(e5);
                glowChanged += Region(onRaw, offRaw, 0, W - 1, 0, H - 1).Changed + Region(onLook, offLook, 0, W - 1, 0, H - 1).Changed;

                string ground = floor == Floor.Light ? "ACIK TAS" : "ISLAK ARDUVAZ";
                string common = Obj(P("model", _modelNote), P("set", "Side Fire / SideTier 3 / Ring 1 / RingTier 5"), P("glowRenderers", glow.GlowRenderers),
                    P("slotlessRenderers", glow.SlotlessRenderers), P("emissionOffRenderers", glow.EmissionOffRenderers), P("applies", glow.Applies),
                    P("mountPainted", glow.Mount == mount), P("visibleRenderers", VisibleRenderers(model.gameObject)));
                var marks = new List<Mark>();
                Bounds? side = BoundsOf(mount.SideAttachment), ring = BoundsOf(mount.RingAttachment);
                if (side.HasValue) marks.Add(MarkAt(side.Value.center + new Vector3(0f, 0f, -.45f), "E4"));
                if (ring.HasValue) marks.Add(MarkAt(ring.Value.center + new Vector3(0f, 0f, .35f), "E5"));
                marks.AddRange(FloorMarks());
                int n = 27 + pass;
                string file = n + (pass == 0 ? "-ek-isima-e4-ates-III-e5-V-acik-tas.png" : "-ek-isima-e4-ates-III-e5-V-islak-arduvaz.png");
                string label = n + " E4 ATES III (" + F(e4.Emission) + ") + E5 V (" + F(e5.Emission) + ") / " + ground;
                AddFrame(n, file, label, onLook, onRaw, W, H, Roi(centre, 4f),
                    Obj(P("n", n), P("file", file), P("label", label), P("kind", "addOnGlow"), P("ground", FloorName()), P("view", "td-rig"),
                        P("viewDetail", viewDetail), P("order", "FlashScale 1, then 0; the glow-slot masks before both"),
                        PR("mount", common), PR("e4", GlowJson(e4)), PR("e5", GlowJson(e5))), marks);

                Color32[] off = Crop(offLook, c, cw, ch, k), on = Crop(onLook, c, cw, ch, k);
                int rowBase = pass == 0 ? cellH : 0;   // bottom-up buffer: the first pass is the upper row
                for (int y = 0; y < cellH; y++)
                {
                    for (int x = 0; x < cellW; x++)
                    {
                        grid[(rowBase + y) * gw + x] = off[y * cellW + x];
                        grid[(rowBase + y) * gw + cellW + x] = on[y * cellW + x];
                    }
                }

                gridMarks.Add(new Mark { X = 12, Top = rowBase + 24, Text = ground + " / KAPALI (FLASHSCALE 0)" });
                gridMarks.Add(new Mark { X = cellW + 12, Top = rowBase + 24, Text = ground + " / ACIK (E4 " + F(e4.Emission) + ", E5 " + F(e5.Emission) + ")" });
            }

            GameFeelSettings.FlashScale = 1f;
            const string file29 = "29-ek-isima-x4-kapali-acik.png", label29 = "29 E4/E5 ISIMA X4: KAPALI | ACIK";
            AddFrame(29, file29, label29, grid, null, gw, gh, new RectInt(cellW, cellH, cellW, cellH),
                Obj(P("n", 29), P("file", file29), P("label", label29), P("kind", "addOnGlowCrop"), P("view", "td-rig"), P("viewDetail", viewDetail),
                    P("cropPx", "160 x 90 around the weapon, x4 nearest"),
                    P("layout", "rows: light stone (top), wet slate (bottom); columns: glow off (FlashScale 0), glow on (FlashScale 1)")), gridMarks);
            Debug.Log("[DD-TEST] wp104b4.ad glow model=" + _modelNote + " glowRenderers=" + glow.GlowRenderers + " slotless=" + glow.SlotlessRenderers + " changed=" + glowChanged
                      + " maskE4Px=" + sidePx + " maskE5Px=" + ringPx + " view=td-rig");
            Assert.Greater(glow.GlowRenderers, 0, "E4 ateş + E5: M_AP040_Glow yuvası (S-06)");
            Assert.AreEqual(0, glow.EmissionOffRenderers, "ışıma yuvasında _EMISSION kapalı");
            Assert.Greater(sidePx, 0, "E4 ışıma penceresi TD rig açısından görünmüyor (glowMaskPx 0)");
            Assert.Greater(glowChanged, 0, "ışıma kareyi değiştirmedi (ekler TD'de çizilmiyor olabilir: visibleRenderers)");
            AssertWritten();
        }

        /// <summary>
        /// M9: FlashScale to <paramref name="value"/>, then frames until TreeAttachGlow has repainted — it compares Reduce Flashes once
        /// a frame and paints on a change (<see cref="TreeAttachGlow.Applies"/> counts the paints) — at least two frames, at most 30,
        /// and one more so the next manual render reads the new block whichever player-loop phase painted it.
        /// </summary>
        private static IEnumerator SetFlash(TreeAttachGlow glow, float value)
        {
            bool change = Mathf.Abs(GameFeelSettings.FlashScale - value) > 1e-4f;
            int before = glow != null ? glow.Applies : 0;
            GameFeelSettings.FlashScale = value;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                if (i >= 1 && (!change || glow == null || glow.Applies != before)) break;
            }

            yield return null;
        }

        // ------------------------------------------------------------------ tracer capture

        /// <summary>
        /// One tracer frame, on a Unity frame of its own: dark (ground only, raw + post) → layer 0 alone on black (A5's bright core)
        /// → every layer + ink (raw, post, HDR luminance); the next frame, the halos alone on black. Measures each round, writes the PNG.
        /// </summary>
        private IEnumerator CaptureTracers(TracerRibbonRenderer tracer, int n, string file, string label, List<Lane> lanes)
        {
            // M2 (AD-B4 §2.2): DrawTd queues Graphics.DrawMeshInstanced for EVERY render of the Unity frame, and the previous capture
            // queued its halo pass (DrawTd(2)) after its own yield — a capture called straight after another one rendered those halos
            // into its dark, core and lit frames (b4-r2: frames 02 / 07 after 01 / 06, brightCorePx 33 / 24 and visiblePx 19 / 6
            // against 8 and 41). A fresh frame first; darkLeakPx proves the dark render is bare ground.
            yield return null;
            var s = new Shots();
            s.Dark = Grab(false, true);
            s.DarkLook = _lookPost ? Grab(true, true) : s.Dark;
            DrawTd(tracer, 0);
            s.Core = Grab(false, false);
            DrawTd(tracer, 1);
            DrawTd(tracer, 2);
            int ink = InvokeInk(tracer);
            s.Lit = Grab(false, true);
            s.Look = _lookPost ? Grab(true, true) : s.Lit;
            s.Hdr = GrabHdrLum();
            yield return null;   // a new frame: the queued draws are gone
            DrawTd(tracer, 2);
            s.Halo = Grab(false, false);
            int leak = DarkLeak(s.Dark);
            s_darkLeak[n] = leak;
            s_rounds.RemoveAll(r => r.Frame == n);

            var lanesJson = new List<string>();
            var marks = new List<Mark>();
            foreach (Lane lane in lanes)
            {
                lanesJson.Add(LaneJson(tracer, n, lane, s));
                // the label 0.55 m above its lane: a tier-V tail (≈ 6.3 m) reaches under a label on the lane line
                marks.Add(MarkAt(O + new Vector3(-9.35f, LaneY, lane.Z + .55f), lane.Label));
            }

            marks.AddRange(FloorMarks());
            Stats all = Region(s.Lit, s.Dark, 0, W - 1, 0, H - 1);
            string json = Obj(P("n", n), P("file", file), P("label", label), P("kind", "tracer"), P("ground", FloorName()),
                P("flashScale", GameFeelSettings.FlashScale), P("inkDrawn", ink), P("changedPx", all.Changed), P("darkLeakPx", leak), P("hdr", s.Hdr != null),
                P("nonEmpty", all.Changed > 1000), PR("lanes", Arr(lanesJson)));
            AddFrame(n, file, label, s.Look, s.Lit, W, H, new RectInt(0, 0, W, H), json, marks);
            Debug.Log("[DD-TEST] wp104b4.ad " + file + " changed=" + all.Changed + " lanes=" + lanes.Count + " ink=" + ink + " darkLeak=" + leak);
            Assert.Greater(all.Changed, 1000, file + ": iz çizilmedi (boş kare)");
        }

        private string LaneJson(TracerRibbonRenderer tracer, int n, Lane lane, Shots s)
        {
            var rounds = new List<string>();
            foreach (Rec r in lane.Recs) rounds.Add(RoundJson(tracer, n, lane, r, s));
            // the AD palette the round should read as (K1 element glow / core; K3 violet edge / sumi ink) — beside the measured hue
            bool coloured = lane.Marked || lane.Element != TreeElement.None;
            Color glow = lane.Marked ? TreeVfxLadder.ChaosEdge : TreeVfxLadder.ElementGlow(lane.Element);
            Color corePalette = lane.Marked ? TreeVfxLadder.ChaosInk : TreeVfxLadder.ElementCore(lane.Element);
            Color.RGBToHSV(glow, out float glowHue, out _, out _);
            return Obj(P("label", lane.Label), P("key", lane.Key), P("weapon", lane.Weapon.ToString()), P("speedMps", lane.Speed), P("shotTier", lane.ShotTier),
                P("elementTier", lane.ElementTier), P("element", lane.Element.ToString()), P("look", lane.Look), P("coreScale", lane.CoreScale),
                P("haloScale", lane.HaloScale), P("haloTier", lane.HaloTier), P("styleHaloPx", lane.StyleHaloPx), P("styleTrailScale", lane.StyleTrail),
                P("styleHaloHdrBoost", lane.StyleHaloHdr), P("marked", lane.Marked),
                P("paletteGlow", coloured ? "#" + ColorUtility.ToHtmlStringRGB(glow) : "WP-101 neutral"), P("paletteGlowHueDeg", coloured ? glowHue * 360f : float.NaN),
                P("paletteCore", coloured ? "#" + ColorUtility.ToHtmlStringRGB(corePalette) : "WP-101 neutral"), PR("rounds", Arr(rounds)));
        }

        /// <summary>
        /// One round: across the streak (a screen column) at 0.1 / 0.25 / 0.5 / 1 / 2 m behind the head — the bright core (layer 0
        /// on black, brightest channel ≥ 254: A5), the quad's faint edge (≥ 8), the halo alone on black (≥ 0.10), the visible streak
        /// (lit vs dark, a channel moved ≥ 0.10: WP-101; and the same on the post frames) — and, over the streak's band (from the
        /// record's origin, so a longer streak measures longer, to 0.15 m past the head): WP-101's saturated-warm share (S ≥ 0.45, hue
        /// 8–50°), the mean hue of the coloured pixels (S ≥ 0.15), the raw / HDR / post peaks and the glow's post median (A1).
        /// </summary>
        private string RoundJson(TracerRibbonRenderer tracer, int n, Lane lane, Rec rec, Shots s)
        {
            float ppm = PxPerMetre(rec.Head.y);
            int across = Mathf.CeilToInt(ppm * .6f);   // ±0.6 m: lanes are 2 m apart, the widest halo (SR V ≈ 84 px) is ±0.42 m
            float[] behind = { .1f, .25f, .5f, 1f, 2f };
            int bright = 0, brightMax = 0, quad = 0, haloPx = 0, haloMax = 0, ink05 = 0, ink15 = 0;
            var visible = new List<int>();
            var visiblePost = new List<int>();
            for (int i = 0; i < behind.Length; i++)
            {
                if (behind[i] > rec.Trail - .1f) continue;
                Vector2 c = Px(rec.Head + Vector3.left * behind[i]);
                int b = Column(s.Core, c, across, 254), h = Column(s.Halo, c, across, Moved);
                if (i == 0)
                {
                    bright = b;
                    quad = Column(s.Core, c, across, 8);
                }

                if (i == 1)
                {
                    haloPx = h;
                    if (lane.Marked)
                    {
                        ink05 = ColumnDarker(s.Lit, s.Dark, c, across, .05f);
                        ink15 = ColumnDarker(s.Lit, s.Dark, c, across, .15f);
                    }
                }

                brightMax = Mathf.Max(brightMax, b);
                haloMax = Mathf.Max(haloMax, h);
                visible.Add(ColumnDiff(s.Lit, s.Dark, c, across, Moved));
                visiblePost.Add(ColumnDiff(s.Look, s.DarkLook, c, across, Moved));
            }

            visible.Sort();
            visiblePost.Sort();
            float reach = Mathf.Min(rec.Flight > 0f ? rec.Flight : rec.Trail, BandReachMax);
            Vector2 tail = Px(rec.Head + Vector3.left * reach), tip = Px(rec.Head + Vector3.right * .15f), mid = Px(rec.Head);
            int band = Mathf.CeilToInt(ppm * .9f);
            int x0 = (int)tail.x, x1 = (int)tip.x, y0 = (int)mid.y - band, y1 = (int)mid.y + band;
            Stats st = Region(s.Lit, s.Dark, x0, x1, y0, y1);
            BandExtras(s, x0, x1, y0, y1, out float peakHdr, out float peakPost, out Color32 glowMedian, out int glowPx);
            float length = st.Changed > 0 ? (st.MaxX - st.MinX + 1) / ppm : 0f;
            string ground = HalfName(rec.Half);
            s_rounds.Add(new RoundM
            {
                Frame = n, Lane = lane.Label, Key = lane.Key, Ground = ground, Weapon = lane.Weapon, ShotTier = lane.ShotTier, ElementTier = lane.ElementTier,
                HaloTier = lane.HaloTier, Element = lane.Element, Marked = lane.Marked, Flash = GameFeelSettings.FlashScale,
                BrightCore = bright, BrightCoreMax = brightMax, CoreQuad = quad, Halo = haloPx, HaloMax = haloMax,
                Visible = visible.Count > 0 ? visible[visible.Count / 2] : 0, VisibleMax = visible.Count > 0 ? visible[visible.Count - 1] : 0,
                VisiblePost = visiblePost.Count > 0 ? visiblePost[visiblePost.Count / 2] : 0, InkDark05 = ink05, InkDark15 = ink15,
                LengthM = length, PeakLum = st.PeakLum, PeakLumHdr = peakHdr, PeakLumPost = peakPost, SatMean = SatOf(st), HueMean = HueOf(st),
                ColouredShare = ColouredOf(st), LumStep = LumStepOf(st), WarmShare = WarmOf(st), GlowMedianPost = glowMedian, GlowPx = glowPx
            });

            object coreM = RecordValue(tracer, rec.Id, "Core"), haloM = RecordValue(tracer, rec.Id, "Halo"), cap = RecordValue(tracer, rec.Id, "CapPx");
            object look = RecordValue(tracer, rec.Id, "Look");
            // the ladder's per-record fields when the record has them (WP-104 B4-1: the halo step, the trail factor, the halo HDR)
            object haloStep = RecordValue(tracer, rec.Id, "HaloPx"), trailK = RecordValue(tracer, rec.Id, "TrailScale"), haloHdr = RecordValue(tracer, rec.Id, "HaloHdr");
            var pairs = new List<string>
            {
                P("ground", ground), P("id", rec.Id), P("headXM", rec.Head.x - O.x), P("headZM", rec.Head.z - O.z), P("trailM", rec.Trail), P("flightM", rec.Flight),
                P("pxPerMetre", ppm), P("recordLook", look is int l ? l : -1), P("recordCoreM", coreM is float cm ? cm : float.NaN),
                P("recordHaloM", haloM is float hm ? hm : float.NaN), P("recordCapPx", cap is float cp ? cp : float.NaN),
                P("recordHaloPx", haloStep is float hs ? hs : float.NaN), P("recordTrailScale", trailK is float tk ? tk : float.NaN),
                P("recordHaloHdr", haloHdr is float hh ? hh : float.NaN),
                P("brightCorePx", bright), P("brightCorePxMax", brightMax), P("coreQuadPx", quad), P("haloPx", haloPx), P("haloPxMax", haloMax),
                P("visiblePx", visible.Count > 0 ? visible[visible.Count / 2] : 0), P("visiblePxMax", visible.Count > 0 ? visible[visible.Count - 1] : 0),
                P("visiblePxPost", visiblePost.Count > 0 ? visiblePost[visiblePost.Count / 2] : 0), P("lengthM", length),
                P("peakLumHdr", peakHdr), P("peakLumPost", peakPost), P("glowMedianPost", glowPx > 0 ? "#" + Hex(glowMedian) : null), P("glowPx", glowPx)
            };
            if (lane.Marked)
            {
                pairs.Add(P("inkDarkPx", ink05));
                pairs.Add(P("inkDarkPx15", ink15));
            }

            pairs.Add(PR("band", StatsJson(st, ppm)));
            return Obj(pairs);
        }

        /// <summary>Over a round's band: the HDR and post peaks of its changed pixels, and the post frame's per-channel median of the
        /// glow — changed pixels outside the core quad (layer 0 alone ≥ 8): AD-VISUAL §6 A1's "glow medyanı".</summary>
        private static void BandExtras(Shots s, int x0, int x1, int y0, int y1, out float peakHdr, out float peakPost, out Color32 median, out int glowPx)
        {
            if (x0 > x1)
            {
                int t = x0;
                x0 = x1;
                x1 = t;
            }

            if (y0 > y1)
            {
                int t = y0;
                y0 = y1;
                y1 = t;
            }

            x0 = Mathf.Clamp(x0, 0, W - 1);
            x1 = Mathf.Clamp(x1, 0, W - 1);
            y0 = Mathf.Clamp(y0, 0, H - 1);
            y1 = Mathf.Clamp(y1, 0, H - 1);
            var hr = new int[256];
            var hg = new int[256];
            var hb = new int[256];
            float ph = 0f;
            bool any = false;
            peakPost = 0f;
            glowPx = 0;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * W;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    Color32 a = s.Lit[i], b = s.Dark[i];
                    if (Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))) < Moved) continue;
                    if (s.Hdr != null)
                    {
                        ph = Mathf.Max(ph, s.Hdr[i]);
                        any = true;
                    }

                    Color32 p = s.Look[i];
                    peakPost = Mathf.Max(peakPost, Lum(p));
                    Color32 k = s.Core[i];
                    if (Mathf.Max(k.r, Mathf.Max(k.g, k.b)) >= 8) continue;   // the core quad: the glow is the rest
                    hr[p.r]++;
                    hg[p.g]++;
                    hb[p.b]++;
                    glowPx++;
                }
            }

            peakHdr = any ? ph : float.NaN;
            median = glowPx > 0 ? new Color32(Median(hr, glowPx), Median(hg, glowPx), Median(hb, glowPx), 255) : new Color32(0, 0, 0, 255);
        }

        private static byte Median(int[] hist, int n)
        {
            int half = (n + 1) / 2, acc = 0;
            for (int v = 0; v < hist.Length; v++)
            {
                acc += hist[v];
                if (acc >= half) return (byte)v;
            }

            return 255;
        }

        /// <summary>M2: pixels of a ground-only render that are neither of the two ground colours (a corner of each half) — a draw left
        /// queued by an earlier render of the same Unity frame shows here; 0 on a clean frame.</summary>
        private static int DarkLeak(Color32[] dark)
        {
            Color32 a = dark[8 * W + 8], b = dark[8 * W + W - 9];
            int n = 0;
            for (int i = 0; i < dark.Length; i++)
            {
                Color32 p = dark[i];
                if (Far(p, a) && Far(p, b)) n++;
            }

            return n;
        }

        private static bool Far(Color32 p, Color32 q) => Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Max(Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b))) >= Moved;

        private static Lane NewLane(string label, int shotTier, int elementTier, TreeElement element, float z, ClassicWeaponId weapon = ClassicWeaponId.AssaultRifle)
        {
            TreeStats s = TreeStats.Neutral;
            s.SetBranchTier(TreeBranch.Shot, shotTier);
            if (element != TreeElement.None)
            {
                s.SetBranchTier(TreeBranch.Element, elementTier);
                s.Elements = element;
                s.DominantElement = element;
            }

            return new Lane { Label = label, Key = label, Stats = s, ShotTier = shotTier, ElementTier = elementTier, Element = element, Z = z, Weapon = weapon };
        }

        /// <summary>WP-101's neutral AR tracer: no tree at all (<see cref="TreeStats.Neutral"/>, no Shot tier set).</summary>
        private static Lane NeutralLane(string label, float z) => new Lane { Label = label, Key = label, Stats = TreeStats.Neutral, Z = z };

        /// <summary>The lane's look is the one in effect when its rounds are born (TracerStyle): set the stats, then record.</summary>
        private void RecordLane(TracerRibbonRenderer tracer, Lane lane, bool split)
        {
            TreeRuntime.SetTestStats(lane.Stats);
            lane.CoreScale = TracerStyle.CoreScale;
            lane.HaloScale = TracerStyle.HaloScale;
            lane.Look = TracerStyle.Look;
            lane.HaloTier = TracerStyle.HaloTier;
            ReadStyle(lane);
            lane.Speed = WeaponSpeed(lane.Weapon, out WeaponCaliber caliber);
            if (split)
            {
                RecordOne(tracer, lane, 'L', caliber);
                RecordOne(tracer, lane, 'R', caliber);
            }
            else
            {
                RecordOne(tracer, lane, 'C', caliber);
            }
        }

        /// <summary>The ladder's per-tier readouts in effect now (by name; absent ones stay NaN).</summary>
        private static void ReadStyle(Lane lane)
        {
            lane.StyleHaloPx = StyleFloat("HaloPx");
            lane.StyleTrail = StyleFloat("TrailScale");
            lane.StyleHaloHdr = StyleFloat("HaloHdrBoost");
        }

        private void RecordOne(TracerRibbonRenderer tracer, Lane lane, char half, WeaponCaliber caliber)
        {
            Vector3 head = O + new Vector3(HeadX(half), LaneY, lane.Z);
            float flight = half == 'C' ? FlightWhole : FlightSplit;
            int id = _nextId++;
            Assert.IsTrue(tracer.RecordTdProjectile(TdSpawn(id, lane.Weapon, caliber, head + Vector3.left * flight, lane.Speed)), "TD kaydı alınmadı: " + lane.Label);
            tracer.StopTdProjectile(id, head);   // a drawable streak: the head on the impact, the one-exposure tail behind it
            lane.Recs.Add(new Rec { Id = id, Head = head, Half = half, Trail = NominalTrail(lane.Weapon, lane.Speed), Flight = flight });
        }

        /// <summary>Heads: whole-frame ground +3 m (origin −9 m); split: west −2 m (origin −9.5 m) / east +7.5 m (origin on the seam)
        /// — a 7.5 m streak stays in its half.</summary>
        private static float HeadX(char half) => half == 'L' ? -2f : half == 'R' ? 7.5f : 3f;

        /// <summary>Lanes 2 m apart around z 0: five +4 … −4, four +3 … −3, three +2 … −2, two +1 / −1 (screen top to bottom).</summary>
        private static float LaneZ(int index, int count) => (count - 1) - index * 2f;

        private static string Roman(int tier) => tier <= 1 ? "I" : tier == 2 ? "II" : tier == 3 ? "III" : tier == 4 ? "IV" : "V";

        private static string ElementWord(TreeElement e) =>
            e == TreeElement.Fire ? "ATES" : e == TreeElement.Ice ? "BUZ" : e == TreeElement.Shock ? "ELEKTRIK" : e == TreeElement.Venom ? "ZEHIR" : "NOTR";

        /// <summary>A11: I / III / V in an order that is neither rising nor falling (the seed is in b4ad.json).</summary>
        private static int[] BlindOrder(int seed)
        {
            int[][] mixed = { new[] { 3, 1, 5 }, new[] { 3, 5, 1 }, new[] { 1, 5, 3 }, new[] { 5, 1, 3 } };
            return mixed[(seed & int.MaxValue) % mixed.Length];
        }

        /// <summary>The weapon's projectile speed and calibre from its definition (S-02), the VFX profile's speed when there is none.</summary>
        private static float WeaponSpeed(ClassicWeaponId id, out WeaponCaliber caliber)
        {
            WeaponDefinition d = Arsenal.Get(id);
            ProjectileLaunch launch = ProjectileLaunch.From(d, id, 200f);
            caliber = launch.Caliber;
            return launch.Speed > 1f ? launch.Speed : VfxWeaponProfiles.For(id).Speed;
        }

        /// <summary>WP-101's tier-I one-exposure tracer streak (TdTrail): v × 1/60 s in 1.5–5.5 m, the sniper v × 1/40 s ≤ 10 m.</summary>
        private static float NominalTrail(ClassicWeaponId id, float speed)
        {
            bool sniper = VfxWeaponProfiles.For(id).Class == VfxWeaponClass.Sniper;
            return sniper
                ? Mathf.Clamp(speed * TracerRibbonRenderer.TdSniperExposureSeconds, TracerRibbonRenderer.TdTailMin, TracerRibbonRenderer.TdSniperTailMax)
                : Mathf.Clamp(speed * TracerRibbonRenderer.TdExposureSeconds, TracerRibbonRenderer.TdTailMin, TracerRibbonRenderer.TdTailMax);
        }

        /// <summary>A real AR projectile whose head reaches <paramref name="head"/> after <paramref name="flight"/> metres (K3 needs
        /// ProjectileState.TreeFlags: TdMarkedAtBirth / TickTd read it). Returns its generation = the TD record id.</summary>
        private static int SpawnReal(Vector3 head, float speed, float flight, int treeFlags)
        {
            var shot = new ShotRequest
            {
                Origin = head + Vector3.left * flight, Direction = Vector3.right, Damage = 1f, Range = 200f, PierceDamageFalloff = 1f, TreeFlags = treeFlags
            };
            var launch = new ProjectileLaunch
            {
                WeaponId = ClassicWeaponId.AssaultRifle, Kind = ProjectileKind.Bullet, Speed = speed, MaxRange = 200f, MaxLifetime = 10f,
                FalloffStart = 200f, FalloffEnd = 200f, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
            };
            Assert.GreaterOrEqual(ProjectileSystem.Spawn(in shot, in launch), 0, "ProjectileSystem.Spawn");
            return ProjectileSystem.Get(ProjectileSystem.LiveCount - 1).Generation;
        }

        private static Vector3 ProjectilePosition(int generation)
        {
            for (int i = 0; i < ProjectileSystem.LiveCount; i++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(i);
                if (p.Generation == generation) return p.Position;
            }

            Assert.Fail("canlı mermi yok: " + generation);
            return default;
        }

        // ------------------------------------------------------------------ proc / aura / ensō capture

        /// <summary>One tree moment through the binder (TreeVfxBinder.Play: no audio, no telemetry), centred on the seam, captured at
        /// fixed presentation times after it (the game clock outside the preview — 60 Hz capture time — the unscaled one inside).</summary>
        private IEnumerator Proc(TreeVfxBinder binder, GroundFireSystem flames, int n0, string slug, string what, TreeProcInfo info, float[] moments, float roiMetres)
        {
            binder.Clear();
            binder.ResetCountersForTests();
            ReleaseDecals();
            if (flames != null) flames.Clear();
            yield return null;
            Color32[] dark = Grab(false, true);
            float t0 = VfxClock.Now;
            bool played = binder.Play(in info);
            RectInt tile = roiMetres > 0f ? Roi(O, roiMetres) : new RectInt(0, 0, W, H);
            int best = 0;
            for (int k = 0; k < moments.Length; k++)
            {
                yield return WaitPresentation(t0, moments[k]);
                float age = VfxClock.Now - t0;
                Color32[] lit = Grab(false, true);
                Color32[] look = _lookPost ? Grab(true, true) : lit;
                int n = n0 + k;
                string nn = n.ToString("00", CultureInfo.InvariantCulture);
                string file = nn + "-proc-" + slug + "-t" + Mathf.RoundToInt(moments[k] * 1000f).ToString("0000", CultureInfo.InvariantCulture) + ".png";
                string label = nn + " " + what + " T+" + moments[k].ToString("0.00", CultureInfo.InvariantCulture) + "S";
                int seam = Mathf.RoundToInt(Px(O).x);
                Stats west = Region(lit, dark, 0, seam - 1, 0, H - 1), east = Region(lit, dark, seam, W - 1, 0, H - 1);
                best = Mathf.Max(best, west.Changed + east.Changed);
                float ppm = PxPerMetre(0f);
                // the effect's centre (a blast's hit point, a zone's ground point); the sumi ring's edge (S3) at the light half's
                // outer radius, read on both halves
                Vector2 centre = Px(info.Point);
                Radial rw = RadialOf(lit, dark, look, centre, 0, seam - 1), re = RadialOf(lit, dark, look, centre, seam, W - 1);
                float edgeW = EdgeLum(lit, dark, centre, 0, seam - 1, rw.R98 - 6f, rw.R98), edgeE = EdgeLum(lit, dark, centre, seam, W - 1, rw.R98 - 6f, rw.R98);
                AddHalf(n, info.Kind.ToString(), HalfKey(true), 0, moments[k], west, ppm, rw, edgeW);
                AddHalf(n, info.Kind.ToString(), HalfKey(false), 0, moments[k], east, ppm, re, edgeE);
                var pairs = new List<string>
                {
                    P("n", n), P("file", file), P("label", label), P("kind", info.Kind.ToString()), P("tier", info.Tier), P("radiusM", info.Radius),
                    P("seconds", info.Seconds), P("depth", info.Depth), P("element", info.Element.ToString()), P("tTargetS", moments[k]), P("tActualS", age),
                    P("played", played), P("preview", TreeVfxPreview.Active), P("clock", TreeVfxPreview.Active ? "unscaled (preview), real time" : "game time, captureDeltaTime 1/60"),
                    P("ground", FloorName()), P("nonEmpty", west.Changed + east.Changed > 200), PR("centrePx", "[" + F(centre.x) + ", " + F(centre.y) + "]"),
                    PR("binder", BinderJson(binder)), PR(HalfKey(true), HalfJson(west, ppm, rw, edgeW)), PR(HalfKey(false), HalfJson(east, ppm, re, edgeE))
                };
                if (flames != null && info.Kind == TreeProcKind.FireGround)
                {
                    pairs.Add(PR("groundFire", Obj(P("liveZones", flames.LiveZones), P("zonesStarted", flames.ZonesStarted), P("flames", flames.FlamesLive),
                        P("embers", flames.EmbersLive), P("char", flames.CharLive))));
                }

                AddFrame(n, file, label, look, lit, W, H, tile, Obj(pairs), FloorMarks());
                Debug.Log("[DD-TEST] wp104b4.ad " + file + " played=" + played + " age=" + age.ToString("0.000", CultureInfo.InvariantCulture)
                          + " changed=" + (west.Changed + east.Changed) + " lastRow=" + binder.LastSpawnedRow);
            }

            Assert.Greater(best, 200, what + ": görsel yok (boş kare)");
        }

        /// <summary>B11 aura I–III on the hero stand-in (TreeAuraVfx draws the preview's AuraLevel every LateUpdate), on the seam.</summary>
        private IEnumerator Aura(TreeVfxBinder binder, int n0)
        {
            binder.Clear();
            ReleaseDecals();
            SetFloor(Floor.Split);
            TreeStats s = TreeStats.Neutral;
            TreeVfxPreview.SetStats(s);
            yield return null;
            yield return null;   // TreeAuraVfx hid its layers (level 0)
            Color32[] dark = Grab(false, true);
            TreeAuraVfx aura = binder.Aura;
            Assert.IsNotNull(aura, "TreeAuraVfx");
            for (int level = 1; level <= TreeVfxLadder.AuraLevels; level++)
            {
                s.AuraLevel = level;
                TreeVfxPreview.SetStats(s);
                float t0 = VfxClock.Now;
                yield return WaitPresentation(t0, .4f);
                Color32[] lit = Grab(false, true);
                Color32[] look = _lookPost ? Grab(true, true) : lit;
                int n = n0 + level - 1;
                string nn = n.ToString("00", CultureInfo.InvariantCulture);
                string tint = "#" + ColorUtility.ToHtmlStringRGB(TreeVfxLadder.AuraTint(level));
                string file = nn + "-aura-seviye-" + level + ".png";
                string label = nn + " B11 AURA " + Roman(level) + " " + tint + " R" + TreeVfxLadder.AuraRadius(level).ToString("0.0", CultureInfo.InvariantCulture) + " M";
                int seam = Mathf.RoundToInt(Px(O).x);
                Stats west = Region(lit, dark, 0, seam - 1, 0, H - 1), east = Region(lit, dark, seam, W - 1, 0, H - 1);
                float ppm = PxPerMetre(0f);
                Vector2 centre = Px(O);   // the hero's feet
                Radial rw = RadialOf(lit, dark, look, centre, 0, seam - 1), re = RadialOf(lit, dark, look, centre, seam, W - 1);
                AddHalf(n, "aura", HalfKey(true), level, .4f, west, ppm, rw, float.NaN);
                AddHalf(n, "aura", HalfKey(false), level, .4f, east, ppm, re, float.NaN);
                string json = Obj(P("n", n), P("file", file), P("label", label), P("kind", "aura"), P("level", level), P("tint", tint),
                    P("radiusM", TreeVfxLadder.AuraRadius(level)), P("alphaCap", TreeVfxLadder.AuraAlpha(level)), P("sparksPerSecond", TreeVfxLadder.AuraSparksPerSecond(level)),
                    P("tActualS", VfxClock.Now - t0), P("drawnLevel", aura.Level), P("ringVisible", aura.RingVisible), P("sparksVisible", aura.SparksVisible),
                    P("smokeVisible", aura.SmokeVisible), P("domeVisible", aura.DomeVisible), P("sparkAlphaCap", aura.SparkAlphaCap), P("ringScaleM", aura.RingScale),
                    P("ringRow", aura.RingRow != null ? aura.RingRow.Id : null), P("ground", FloorName()), P("nonEmpty", west.Changed + east.Changed > 200),
                    PR(HalfKey(true), HalfJson(west, ppm, rw, float.NaN)), PR(HalfKey(false), HalfJson(east, ppm, re, float.NaN)));
                AddFrame(n, file, label, look, lit, W, H, Roi(O, 8f), json, FloorMarks());
                Debug.Log("[DD-TEST] wp104b4.ad " + file + " ring=" + aura.RingVisible + " changed=" + (west.Changed + east.Changed));
                Assert.Greater(west.Changed + east.Changed, 200, "aura " + level + ": görsel yok (boş kare)");
            }

            s.AuraLevel = 0;
            TreeVfxPreview.SetStats(s);
            yield return null;
        }

        /// <summary>X8: the synergy's first-trigger ensō (Frozen Gravity: teal | violet) at the hero's feet, on one whole-frame ground.</summary>
        private IEnumerator Enso(TreeVfxBinder binder, Transform hero, int n, Floor floor)
        {
            binder.Clear();
            binder.ResetCountersForTests();   // forget "once per synergy per run": the same synergy shows again on the other ground
            ReleaseDecals();
            SetFloor(floor);
            yield return null;
            yield return null;
            Color32[] dark = Grab(false, true);
            TreeProcInfo e = Info(TreeProcKind.SynergyFirstTrigger, hero.position, 0f, 1);
            e.Synergy = TreeSynergy.FrozenGravity;
            float t0 = VfxClock.Now;
            bool played = binder.Play(in e);
            yield return WaitPresentation(t0, .55f);   // the stroke mostly drawn, before the last 25 % fade (0.8 s life)
            Color32[] lit = Grab(false, true);
            Color32[] look = _lookPost ? Grab(true, true) : lit;
            string nn = n.ToString("00", CultureInfo.InvariantCulture);
            string ground = floor == Floor.Light ? "acik-tas" : "islak-arduvaz";
            string file = nn + "-sinerji-enso-" + ground + "-t0550.png";
            string label = nn + " X8 ENSO / " + (floor == Floor.Light ? "ACIK TAS" : "ISLAK ARDUVAZ") + " T+0.55S";
            int seam = Mathf.RoundToInt(Px(hero.position).x);
            Stats west = Region(lit, dark, 0, seam - 1, 0, H - 1), east = Region(lit, dark, seam, W - 1, 0, H - 1);
            float ppm = PxPerMetre(0f);
            Vector2 centre = Px(hero.position);
            Radial rw = RadialOf(lit, dark, look, centre, 0, seam - 1), re = RadialOf(lit, dark, look, centre, seam, W - 1);
            AddHalf(n, "synergyEnso", "leftHalf", 0, .55f, west, ppm, rw, float.NaN);
            AddHalf(n, "synergyEnso", "rightHalf", 0, .55f, east, ppm, re, float.NaN);
            string json = Obj(P("n", n), P("file", file), P("label", label), P("kind", "synergyEnso"), P("synergy", "FrozenGravity"), P("tints", EnsoTints(TreeSynergy.FrozenGravity)),
                P("played", played), P("tTargetS", .55f), P("tActualS", VfxClock.Now - t0), P("ground", FloorName()), P("nonEmpty", west.Changed + east.Changed > 200),
                PR("binder", BinderJson(binder)), PR("leftHalf", HalfJson(west, ppm, rw, float.NaN)), PR("rightHalf", HalfJson(east, ppm, re, float.NaN)));
            AddFrame(n, file, label, look, lit, W, H, Roi(hero.position, 10f), json, FloorMarks());
            Debug.Log("[DD-TEST] wp104b4.ad " + file + " played=" + played + " changed=" + (west.Changed + east.Changed));
            Assert.Greater(west.Changed + east.Changed, 200, "ensō " + ground + ": görsel yok (boş kare)");
        }

        private static void AddHalf(int n, string kind, string key, int level, float t, Stats s, float ppm, Radial r, float edge)
        {
            s_halves.RemoveAll(h => h.Frame == n && h.Key == key);
            s_halves.Add(new HalfM { Frame = n, Kind = kind, Key = key, Level = level, T = t, S = s, Ppm = ppm, R = r, EdgeLum = edge });
        }

        private static IEnumerator WaitPresentation(float t0, float seconds)
        {
            float start = Time.realtimeSinceStartup;
            int frames = 0;
            while (VfxClock.Now - t0 < seconds && frames < 3000 && Time.realtimeSinceStartup - start < 20f)
            {
                frames++;
                yield return null;
            }
        }

        private static string EnsoTints(TreeSynergy synergy)
        {
            TreeVfxLadder.SynergyTints(synergy, out Color left, out Color right);
            return "#" + ColorUtility.ToHtmlStringRGB(left) + " | #" + ColorUtility.ToHtmlStringRGB(right);
        }

        private static string BinderJson(TreeVfxBinder b) =>
            Obj(P("played", b.Played), P("merged", b.Merged), P("replaced", b.Replaced), P("dropped", b.Dropped), P("deferred", b.Deferred), P("missing", b.Missing),
                P("liveBlasts", b.LiveBlasts), P("liveNukes", b.LiveNukes), P("liveGlints", b.LiveGlints), P("liveSumi", b.LiveSumi), P("liveClouds", b.LiveClouds),
                P("liveCeremonies", b.LiveCeremonies), P("fireZonesFed", b.FireZonesFed), P("leechUnresolved", b.LeechUnresolved), P("lastRow", b.LastSpawnedRow.ToString()));

        private static TreeProcInfo Info(TreeProcKind kind, Vector3 point, float radius, int tier) =>
            new TreeProcInfo { Kind = kind, Point = point, Radius = radius, Tier = tier, Weapon = ClassicWeaponId.AssaultRifle };

        private TreeVfxBinder Binder()
        {
            VfxDressing vfx = HitFeedbackDirector.Instance.Vfx;
            TreeVfxBinder binder = TreeVfxBinder.Instance != null ? TreeVfxBinder.Instance : TreeVfxBinder.Ensure(vfx);
            Assert.IsNotNull(binder, "TreeVfxBinder yok (VfxDressing.Configure)");
            Assert.IsTrue(binder.RowsReady, "AP-041 ağaç satırları katalogda yok (" + binder.RowsResolved + "/" + (int)TreeVfxRow.Count + "): ArtAp041TreeIntake.Run");
            SaveAndClearDelegates(typeof(TreeEvents));   // only the binder listens: no S-08 audio, no telemetry
            binder.Rearm();
            binder.Clear();
            binder.ResetCountersForTests();
            return binder;
        }

        /// <summary>The scorch decals (B1 ground scorch, blast scorches) live seconds in VfxDressing's shared pool; binder.Clear leaves
        /// them, so each scenario starts from a clean ground.</summary>
        private static void ReleaseDecals()
        {
            VfxDressing vfx = HitFeedbackDirector.Instance != null ? HitFeedbackDirector.Instance.Vfx : null;
            if (vfx != null && vfx.DecalPool != null) vfx.DecalPool.ReleaseAllSpawned();
        }

        private Transform StandIn(string name, Vector3 feet, Color colour)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(_root.transform, false);
            anchor.transform.position = feet;   // the anchor is the feet: TreeAuraVfx / the binder read it as the ground point
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = name + " body";
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(anchor.transform, false);
            body.transform.localPosition = new Vector3(0f, .9f, 0f);
            body.transform.localScale = new Vector3(.7f, .9f, .7f);   // r 0.35 m, 1.8 m tall
            body.GetComponent<MeshRenderer>().sharedMaterial = Unlit(colour);
            return anchor.transform;
        }

        // ------------------------------------------------------------------ add-on glow

        /// <summary>ar_1's AP-021 model from DD_ClassicWeapons when it is there (LODs as in hand), else a bare root as Wp104B4VfxPlayTests;
        /// barrel along world +X at hand height.</summary>
        private Transform WeaponModel()
        {
            ClassicWeaponVisualLibrary visuals = ClassicWeaponVisualLibrary.Current;
            ClassicWeaponVisualLibrary.Entry entry = visuals != null ? visuals.Find(ClassicWeaponId.AssaultRifle) : null;
            GameObject go = entry != null && entry.HasModel ? ClassicWeaponVisualLibrary.Instantiate(entry, visuals.material, AuthoredWeaponModel.DefaultScreenHeights) : null;
            _modelNote = go != null ? "ar_1 model (DD_ClassicWeapons, " + (entry.ownMaterials ? entry.modelPackage : "AP-013 atlas") + ")" : "bare root (no ar_1 model in DD_ClassicWeapons)";
            if (go == null) go = new GameObject("WP104-AD ar_1 model root");
            go.transform.SetParent(_root.transform, false);
            go.transform.SetPositionAndRotation(O + new Vector3(0f, 1.3f, 0f), Quaternion.Euler(0f, 90f, 0f));
            return go.transform;
        }

        /// <summary>One add-on's screen size (its renderers' bounds; the AD's "ekran alanı" E4 ≈ 100 / E5 ≈ 58 px) and, within 24 px of its
        /// centre, the glow's own pixels (look on vs look off: luminance up ≥ 0.06 — emission and its bloom), the peak luminance on / off
        /// and the raw saturated pixels (≥ 254).</summary>
        private void Spot(GlowM g, GameObject addOn, Color32[] onLook, Color32[] offLook, Color32[] onRaw)
        {
            Bounds? b = BoundsOf(addOn);
            if (!b.HasValue) return;
            g.Present = true;
            Vector2 c = Px(b.Value.center);
            Vector2 size = ScreenSize(b.Value);
            g.SizeX = size.x;
            g.SizeY = size.y;
            g.AreaPx = size.x * size.y;
            g.CentreX = c.x;
            g.CentreY = c.y;
            const int r = 24;
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int x = (int)c.x + dx, y = (int)c.y + dy;
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    int i = y * W + x;
                    float on = Lum(onLook[i]), off = Lum(offLook[i]);
                    if (on - off >= .06f) g.GlowSpot++;
                    g.PeakOn = Mathf.Max(g.PeakOn, on);
                    g.PeakOff = Mathf.Max(g.PeakOff, off);
                    if (Mathf.Max(onRaw[i].r, Mathf.Max(onRaw[i].g, onRaw[i].b)) >= 254) g.RawSaturated++;
                }
            }
        }

        private static string GlowJson(GlowM g)
        {
            float delta = g.MeanLumOn - g.MeanLumOff;
            bool m9Raw = g.MaskPx > 0 && delta >= .15f;
            if (!g.Present)
            {
                return Obj(P("name", g.Name), P("present", false), P("emission", g.Emission), P("glowMaskPx", g.MaskPx), P("maskLumDeltaRaw", delta), P("m9Raw", m9Raw));
            }

            return Obj(P("name", g.Name), P("present", true), P("emission", g.Emission), PR("screenPx", "[" + F(g.SizeX) + ", " + F(g.SizeY) + "]"),
                P("screenAreaPx", g.AreaPx), P("centreX", g.CentreX), P("centreY", g.CentreY), P("glowSpotPx", g.GlowSpot), P("peakLumOn", g.PeakOn),
                P("peakLumOff", g.PeakOff), P("rawSaturatedPx", g.RawSaturated), P("glowMaskPx", g.MaskPx), P("maskLumOffRaw", g.MeanLumOff),
                P("maskLumOnRaw", g.MeanLumOn), P("maskLumDeltaRaw", delta), P("maskLumHdrOff", g.MeanHdrOff), P("maskLumHdrOn", g.MeanHdrOn),
                P("slotEmissionOff", g.SlotOff), P("slotEmissionOn", g.SlotOn), P("m9Raw", m9Raw), P("m9Post", g.GlowSpot >= g.AreaPx));
        }

        /// <summary>AD-B4 M9's "ek maskesi" (C, 12.10): the add-on's glow-slot pixels in the raw frame — its renderers drawn unlit with
        /// slot 1 white against all black (the weapon and the ground as they are: whatever hides the slot hides it here too).</summary>
        private bool[] GlowMask(GameObject addOn, out int count)
        {
            var mask = new bool[W * H];
            count = 0;
            if (addOn == null) return mask;
            Material black = Unlit(Color.black), white = Unlit(Color.white);
            Renderer[] rs = addOn.GetComponentsInChildren<Renderer>(false);
            var saved = new Material[rs.Length][];
            for (int i = 0; i < rs.Length; i++) saved[i] = rs[i].sharedMaterials;
            Color32[] lit, dark;
            try
            {
                for (int i = 0; i < rs.Length; i++) rs[i].sharedMaterials = Slots(saved[i].Length, black, white);
                lit = Grab(false, true);
                for (int i = 0; i < rs.Length; i++) rs[i].sharedMaterials = Slots(saved[i].Length, black, black);
                dark = Grab(false, true);
            }
            finally
            {
                for (int i = 0; i < rs.Length; i++) rs[i].sharedMaterials = saved[i];
            }

            for (int i = 0; i < mask.Length; i++)
            {
                if (Lum(lit[i]) - Lum(dark[i]) < .5f) continue;
                mask[i] = true;
                count++;
            }

            return mask;
        }

        private static Material[] Slots(int n, Material other, Material glowSlot)
        {
            var m = new Material[n];
            for (int i = 0; i < n; i++) m[i] = i == TreeAttachGlow.GlowSlot ? glowSlot : other;
            return m;
        }

        private static float MeanLum(Color32[] img, bool[] mask)
        {
            if (img == null || mask == null) return float.NaN;
            double sum = 0;
            int n = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) continue;
                sum += Lum(img[i]);
                n++;
            }

            return n > 0 ? (float)(sum / n) : float.NaN;
        }

        private static float MeanOf(float[] values, bool[] mask)
        {
            if (values == null || mask == null) return float.NaN;
            double sum = 0;
            int n = 0;
            for (int i = 0; i < mask.Length && i < values.Length; i++)
            {
                if (!mask[i]) continue;
                sum += values[i];
                n++;
            }

            return n > 0 ? (float)(sum / n) : float.NaN;
        }

        /// <summary>What slot 1's block of the add-on's first <c>M_AP040_Glow</c> renderer holds (_EmissionColor.r; 0 for an empty
        /// block, NaN without such a renderer) — the binding's own record beside the pixels.</summary>
        private static float SlotEmission(GameObject addOn, MaterialPropertyBlock scratch)
        {
            if (addOn == null) return float.NaN;
            foreach (Renderer r in addOn.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                if (mats.Length < 2 || mats[1] == null || !mats[1].name.StartsWith("M_AP040_Glow", StringComparison.Ordinal)) continue;
                r.GetPropertyBlock(scratch, 1);
                return scratch.isEmpty ? 0f : scratch.GetColor("_EmissionColor").r;
            }

            return float.NaN;
        }

        /// <summary>Renderers some camera drew last (LODGroup culling at TD distance shows here as a low count).</summary>
        private static int VisibleRenderers(GameObject go)
        {
            int n = 0;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r.enabled && r.isVisible) n++;
            }

            return n;
        }

        private static Bounds? BoundsOf(GameObject go)
        {
            if (go == null) return null;
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                if (!any) b = r.bounds;
                else b.Encapsulate(r.bounds);
                any = true;
            }

            return any ? b : (Bounds?)null;
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

        private static Color32[] Crop(Color32[] src, Vector2 centre, int cw, int ch, int k)
        {
            int x0 = Mathf.Clamp((int)centre.x - cw / 2, 0, W - cw), y0 = Mathf.Clamp((int)centre.y - ch / 2, 0, H - ch);
            var dst = new Color32[cw * k * ch * k];
            for (int y = 0; y < ch * k; y++)
            {
                for (int x = 0; x < cw * k; x++) dst[y * cw * k + x] = src[(y0 + y / k) * W + x0 + x / k];
            }

            return dst;
        }

        // ------------------------------------------------------------------ capture rig

        private void BuildRig()
        {
            _root = new GameObject("WP104 B4 AD capture root");
            _unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.IsNotNull(_unlit, "Universal Render Pipeline/Unlit");
            _lightMat = Unlit(new Color(LightGrey, LightGrey, LightGrey, 1f));
            _slateMat = Unlit(new Color(SlateGrey, SlateGrey, SlateGrey, 1f));
            _west = FloorHalf("WP104-AD ground west", -6f);
            _east = FloorHalf("WP104-AD ground east", 6f);
            SetFloor(Floor.Split);
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Wp104B4AdKare" };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false);
            // the HDR twin (M3's peaks, M9's HDR means): the same camera into a linear half-float target, read without a clamp
            _hdrOk = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) && SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf);
            if (_hdrOk)
            {
                _rtHdr = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { name = "Wp104B4AdKareHdr" };
                _readHdr = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true);
            }

            var camGo = new GameObject("WP104-B4-AD-camera");
            camGo.transform.SetParent(_root.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.transform.SetPositionAndRotation(O + new Vector3(0f, CamHeight, 0f), Quaternion.Euler(90f, 0f, 0f));
            _cam.allowHDR = true;
            _cam.fieldOfView = Fov;   // 1080 / (2 · 20 · tan 15.85°) ≈ 95 px/m on the ground
            _cam.nearClipPlane = .3f;
            _cam.farClipPlane = 200f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.targetTexture = _rt;
            _cam.aspect = (float)W / H;
            _cam.enabled = false;
            // URP camera data by reflection (this test assembly does not reference URP — Wp101TdIzZeminTests)
            Type urp = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urp != null)
            {
                _camData = camGo.GetComponent(urp);
                if (_camData == null) _camData = camGo.AddComponent(urp);
                _post = urp.GetProperty("renderPostProcessing");
                PropertyInfo aa = urp.GetProperty("antialiasing");
                if (aa != null && aa.PropertyType.IsEnum && aa.CanWrite) aa.SetValue(_camData, Enum.ToObject(aa.PropertyType, 0));   // None (WorldAtmosphere.ConfigureCamera)
            }

            _lookPost = _post != null && BuildLookVolume();
            FeedbackCameraRef.Set(_cam);   // camera-facing cards billboard to this camera
            Physics.SyncTransforms();
        }

        /// <summary>
        /// The game's grade as a global volume: WorldAtmosphere.EnsureVolume(root, null) builds the runtime twin of
        /// DD_GlobalVolumeProfile (Neutral tonemap, contrast 8, bloom 1.25 / 0.5 / 0.62, vignette 0.2). By reflection: its signature
        /// carries URP's Volume types, which this assembly does not reference. False = the PNGs are the raw render.
        /// </summary>
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
                Debug.LogWarning("[DD-TEST] wp104b4.ad volume: " + e.Message);
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

        private MeshRenderer FloorHalf(string name, float x)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);   // its MeshCollider stays: the procs raycast the ground (layer 0)
            go.name = name;
            go.transform.SetParent(_root.transform, false);
            go.transform.SetPositionAndRotation(O + new Vector3(x, 0f, 0f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(12f, 16f, 1f);
            return go.GetComponent<MeshRenderer>();
        }

        private Material Unlit(Color colour)
        {
            var m = new Material(_unlit) { name = "WP104-AD unlit" };
            m.SetColor("_BaseColor", colour);
            _owned.Add(m);
            return m;
        }

        private void SetFloor(Floor floor)
        {
            _floor = floor;
            _west.sharedMaterial = floor == Floor.Slate ? _slateMat : _lightMat;
            _east.sharedMaterial = floor == Floor.Light ? _lightMat : _slateMat;
        }

        private string FloorName() => _floor == Floor.Light ? KeyStone : _floor == Floor.Slate ? KeySlate : "split: west lightStone | east wetSlate";

        private string HalfName(char half)
        {
            bool light = _floor == Floor.Light || (_floor == Floor.Split && half == 'L');
            return light ? KeyStone : KeySlate;
        }

        private string HalfKey(bool west) => _floor == Floor.Split ? (west ? KeyStone : KeySlate) : (west ? "westHalf" : "eastHalf");

        private List<Mark> FloorMarks()
        {
            var marks = new List<Mark>();
            if (_floor == Floor.Split)
            {
                marks.Add(MarkAt(O + new Vector3(-9.6f, 0f, -5.2f), "ACIK TAS"));
                marks.Add(MarkAt(O + new Vector3(.3f, 0f, -5.2f), "ISLAK ARDUVAZ"));
            }
            else
            {
                marks.Add(MarkAt(O + new Vector3(-9.6f, 0f, -5.2f), _floor == Floor.Light ? "ACIK TAS" : "ISLAK ARDUVAZ"));
            }

            return marks;
        }

        private Mark MarkAt(Vector3 world, string text)
        {
            Vector2 p = Px(world);
            return new Mark { X = Mathf.RoundToInt(p.x), Top = Mathf.RoundToInt(p.y) + 7, Text = text };
        }

        /// <summary>One render to the RT and its pixels. <paramref name="look"/>: through the game's post stack (else raw HDR, no
        /// post); <paramref name="ground"/> false: the ground halves switched off (black — the layers alone).</summary>
        private Color32[] Grab(bool look, bool ground)
        {
            if (_post != null) _post.SetValue(_camData, look && _lookPost);
            _west.enabled = _east.enabled = ground;
            _cam.Render();
            _west.enabled = _east.enabled = true;
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _read.Apply(false);
            RenderTexture.active = prev;
            return _read.GetPixels32();
        }

        /// <summary>
        /// The raw frame (no post, the ground on) rendered into the linear ARGBHalf twin: per-pixel linear luminance, unclamped — the
        /// 8-bit raw frame saturates every tracer core at 1.0, so M3's "peakLum(RF) &lt; peakLum(off)" reads here. One shared buffer
        /// (read it before the next call); null when the platform has no half-float read-back.
        /// </summary>
        private float[] GrabHdrLum()
        {
            if (!_hdrOk || _rtHdr == null || _readHdr == null) return null;
            try
            {
                if (_post != null) _post.SetValue(_camData, false);
                _west.enabled = _east.enabled = true;
                _cam.targetTexture = _rtHdr;
                _cam.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = _rtHdr;
                _readHdr.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
                RenderTexture.active = prev;
                NativeArray<ushort> raw = _readHdr.GetRawTextureData<ushort>();
                if (raw.Length < W * H * 4) throw new InvalidOperationException("RGBAHalf read-back " + raw.Length + " < " + W * H * 4);
                if (_hdrLum == null) _hdrLum = new float[W * H];
                for (int i = 0, j = 0; i < _hdrLum.Length; i++, j += 4)
                {
                    _hdrLum[i] = .2126f * Mathf.HalfToFloat(raw[j]) + .7152f * Mathf.HalfToFloat(raw[j + 1]) + .0722f * Mathf.HalfToFloat(raw[j + 2]);
                }

                // a read-back that silently failed is black: the ground corner is never (light stone .52, wet slate .03 linear)
                if (!(_hdrLum[8 * W + 8] > 1e-4f)) throw new InvalidOperationException("HDR read-back is black");
                return _hdrLum;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DD-TEST] wp104b4.ad hdr off: " + e.Message);
                _hdrOk = false;
                return null;
            }
            finally
            {
                _cam.targetTexture = _rt;
            }
        }

        private Vector2 Px(Vector3 world)
        {
            Vector3 v = _cam.WorldToViewportPoint(world);
            return new Vector2(v.x * W, v.y * H);
        }

        private float PxPerMetre(float height) => (Px(O + new Vector3(1f, height, 0f)) - Px(O + new Vector3(0f, height, 0f))).magnitude;

        private RectInt Roi(Vector3 centre, float metres)
        {
            Vector2 c = Px(centre);
            int w = Mathf.Clamp(Mathf.RoundToInt(metres * PxPerMetre(centre.y)), 64, W);
            int h = Mathf.Clamp(w * 9 / 16, 36, H);
            int x = Mathf.Clamp(Mathf.RoundToInt(c.x) - w / 2, 0, W - w), y = Mathf.Clamp(Mathf.RoundToInt(c.y) - h / 2, 0, H - h);
            return new RectInt(x, y, w, h);
        }

        private string Header()
        {
            return Obj(P("test", "DD.Prototype.Tests.PlayMode.Wp104B4AdKareTests"), P("unity", Application.unityVersion),
                P("utc", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                PR("camera", Obj(P("width", W), P("height", H), P("heightM", CamHeight), P("fovDeg", Fov), P("view", "straight down; screen +x = world +x, screen +y = world +z"),
                    P("pxPerMetreGround", PxPerMetre(0f)), P("pxPerMetreAt1m", PxPerMetre(1f)))),
                PR("grounds", Obj(P("lightStoneSrgb", LightGrey), P("wetSlateSrgb", SlateGrey), P("shader", "Universal Render Pipeline/Unlit"),
                    P("split", "west light stone | east wet slate, the seam at the frame centre"))),
                PR("render", Obj(P("lookPost", _lookPost),
                    P("png", _lookPost ? "the game's post stack: WorldAtmosphere.EnsureVolume runtime profile (Neutral tonemap, contrast 8, bloom 1.25 / 0.5 / 0.62, vignette 0.2)" : "raw (URP post-processing unavailable)"),
                    P("measured", "raw: HDR camera without post-processing into an 8-bit sRGB target (the A5 / Wp101TdIzZeminTests set-up)"),
                    P("hdr", _hdrOk), P("hdrMeasure", _hdrOk ? "the same raw render into a linear ARGBHalf target, read unclamped: peakLumHdr, meanLumHdr" : "unavailable"),
                    P("tracerFrame", "each tracer capture starts on a Unity frame of its own (AD-B4 M2); darkLeakPx = 0 proves the ground-only render is bare"))),
                P("vfxTier", VfxTier.Current.ToString()), P("captureDeltaTime", "1/60 (game time) until the preview frames 21-26, then 0 (real time)"),
                P("fog", false), P("reducedMotion", false),
                PR("tracerStyle", Constants(typeof(TracerStyle), null)), PR("tracerTd", Constants(typeof(TracerRibbonRenderer), "Td")),
                PR("ladder", Constants(typeof(TreeVfxLadder), null)),
                PR("definitions", Obj(
                    P("brightCorePx", "tracer layer 0 alone on black: pixels whose brightest channel >= 254 across the streak 0.1 m behind the head (A5); Max over 0.1-2 m"),
                    P("coreQuadPx", "the same column, brightest channel >= 8 (the core quad with its soft edge)"),
                    P("haloPx", "tracer halos alone on black: brightest channel >= 0.10 across the streak 0.25 m behind the head; Max over 0.1-2 m"),
                    P("visiblePx", "lit vs dark (ground only), a channel moved >= 0.10, across the streak: median / max over 0.1-2 m (WP-101)"),
                    P("visiblePxPost", "the same on the post frames (lit vs ground-only, both through the post stack): median over 0.1-2 m (S1)"),
                    P("lengthM", "band bboxM[0]: the changed pixels' width from the record's origin (<= 10.5 m behind the head) to 0.15 m past it, in metres at the 1 m tracer height"),
                    P("peakLum", "band: the brightest changed pixel of the raw 8-bit frame (saturates at 1)"),
                    P("peakLumHdr", "band: the brightest changed pixel's linear luminance in the ARGBHalf twin (unclamped; M3)"),
                    P("peakLumPost", "band: the brightest changed pixel of the post frame"),
                    P("glowMedianPost", "band: per-channel median of the post frame over the changed pixels outside the core quad (layer 0 >= 8): AD-VISUAL A1's glow median"),
                    P("inkDarkPx15", "K3: pixels darker than the bare ground by >= 0.15 luminance (sRGB-encoded), 0.25 m behind the head (M4); inkDarkPx the same at 0.05"),
                    P("darkLeakPx", "tracer frame: pixels of the ground-only render matching neither ground colour (M2: 0 = nothing left queued from an earlier render of that Unity frame)"),
                    P("warmShare", "share of changed pixels with S >= 0.45 and hue 8-50 deg (WP-101 saturated warm)"),
                    P("hueMeanDeg", "circular mean hue of changed pixels with S >= 0.15 (colouredShare = their share)"),
                    P("bboxM", "changed pixels' bounding box on screen in metres (tracers at the 1 m height, procs / aura / enso at ground scale)"),
                    P("radial", "per half, around the effect centre (centrePx): r98Px / rMaxPx = radius holding 98 % / all changed pixels; meanPost = the post frame's mean colour over them; goldPxPost / goldPxRaw = A17 gold pixels (HSV hue 35-65 deg, S > 0.30) among them; edgeLumAbs = mean |luminance step| over every pixel of the annulus r98(light half) - 6 .. r98(light half) px (S3)"),
                    P("glowSpotPx", "within 24 px of an add-on: post-stack luminance up >= 0.06 from FlashScale 0 to 1 (emission + bloom)"),
                    P("glowMaskPx", "27-28: the add-on's glow-slot pixels in the raw frame (its renderers unlit, slot 1 white vs black) - AD-B4 M9 'ek maskesi'"),
                    P("maskLumDeltaRaw", "27-28: mean raw luminance over glowMaskPx, FlashScale 1 minus 0 (m9Raw: >= 0.15); m9Post: glowSpotPx >= screenAreaPx; maskLumHdr the same in the ARGBHalf twin"),
                    P("slotEmission", "add-on: _EmissionColor.r in slot 1's MaterialPropertyBlock of its first M_AP040_Glow renderer (the binding's record)"),
                    P("view27to29", "td-rig: the game's TD rig angle (DD_FeelConfig height / back / yaw) at heightM distance - straight down shows no E4 glow slot"),
                    P("deltaE", "CIE76 on sRGB -> linear -> XYZ (D65) -> L*a*b* (AD-VISUAL §6 A1 / A4)"))));
        }

        /// <summary>The public numeric constants / static fields of <paramref name="t"/> (whatever the ladder owners define today).</summary>
        private static string Constants(Type t, string prefix)
        {
            var pairs = new List<string>();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (prefix != null && !f.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                object v;
                try
                {
                    v = f.GetValue(null);
                }
                catch (Exception)
                {
                    continue;
                }

                if (v is float fv) pairs.Add(P(f.Name, fv));
                else if (v is int iv) pairs.Add(P(f.Name, iv));
            }

            return Obj(pairs);
        }

        // ------------------------------------------------------------------ measures

        private static float Lum(Color c) => .2126f * c.r + .7152f * c.g + .0722f * c.b;

        private static int Column(Color32[] img, Vector2 c, int half, int threshold)
        {
            int x = (int)c.x, n = 0;
            if (x < 0 || x >= W) return 0;
            for (int dy = -half; dy <= half; dy++)
            {
                int y = (int)c.y + dy;
                if (y < 0 || y >= H) continue;
                Color32 k = img[y * W + x];
                if (Mathf.Max(k.r, Mathf.Max(k.g, k.b)) >= threshold) n++;
            }

            return n;
        }

        private static int ColumnDiff(Color32[] lit, Color32[] dark, Vector2 c, int half, int threshold)
        {
            int x = (int)c.x, n = 0;
            if (x < 0 || x >= W) return 0;
            for (int dy = -half; dy <= half; dy++)
            {
                int y = (int)c.y + dy;
                if (y < 0 || y >= H) continue;
                Color32 a = lit[y * W + x], b = dark[y * W + x];
                if (Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))) >= threshold) n++;
            }

            return n;
        }

        /// <summary>K3's sumi core: pixels the frame draws darker than the bare ground by ≥ <paramref name="delta"/> luminance.</summary>
        private static int ColumnDarker(Color32[] lit, Color32[] dark, Vector2 c, int half, float delta)
        {
            int x = (int)c.x, n = 0;
            if (x < 0 || x >= W) return 0;
            for (int dy = -half; dy <= half; dy++)
            {
                int y = (int)c.y + dy;
                if (y < 0 || y >= H) continue;
                if (Lum(lit[y * W + x]) < Lum(dark[y * W + x]) - delta) n++;
            }

            return n;
        }

        private static Stats Region(Color32[] lit, Color32[] dark, int x0, int x1, int y0, int y1)
        {
            var s = new Stats { MinX = int.MaxValue, MinY = int.MaxValue, MaxX = int.MinValue, MaxY = int.MinValue };
            if (x0 > x1)
            {
                int t = x0;
                x0 = x1;
                x1 = t;
            }

            if (y0 > y1)
            {
                int t = y0;
                y0 = y1;
                y1 = t;
            }

            x0 = Mathf.Clamp(x0, 0, W - 1);
            x1 = Mathf.Clamp(x1, 0, W - 1);
            y0 = Mathf.Clamp(y0, 0, H - 1);
            y1 = Mathf.Clamp(y1, 0, H - 1);
            for (int y = y0; y <= y1; y++)
            {
                int row = y * W;
                for (int x = x0; x <= x1; x++)
                {
                    Color32 a = lit[row + x], b = dark[row + x];
                    if (Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))) < Moved) continue;
                    s.Changed++;
                    if (x < s.MinX) s.MinX = x;
                    if (x > s.MaxX) s.MaxX = x;
                    if (y < s.MinY) s.MinY = y;
                    if (y > s.MaxY) s.MaxY = y;
                    Color ca = a;
                    float la = Lum(ca);
                    s.LumStep += Mathf.Abs(la - Lum(b));
                    if (la > s.PeakLum) s.PeakLum = la;
                    if (Mathf.Max(a.r, Mathf.Max(a.g, a.b)) >= 254) s.Saturated++;
                    Color.RGBToHSV(ca, out float hue, out float sat, out _);
                    float deg = hue * 360f;
                    if (sat >= .45f && deg >= 8f && deg <= 50f) s.Warm++;
                    if (sat < .15f) continue;
                    s.Coloured++;
                    s.SatSum += sat;
                    s.HueX += Mathf.Cos(hue * 2f * Mathf.PI);
                    s.HueY += Mathf.Sin(hue * 2f * Mathf.PI);
                }
            }

            return s;
        }

        private static float SatOf(Stats s) => s.Coloured > 0 ? s.SatSum / s.Coloured : 0f;

        private static float HueOf(Stats s) => s.Coloured > 0 ? Mathf.Repeat(Mathf.Atan2(s.HueY, s.HueX) * Mathf.Rad2Deg, 360f) : float.NaN;

        private static float ColouredOf(Stats s) => s.Changed > 0 ? s.Coloured / (float)s.Changed : 0f;

        private static float WarmOf(Stats s) => s.Changed > 0 ? s.Warm / (float)s.Changed : 0f;

        private static float LumStepOf(Stats s) => s.Changed > 0 ? s.LumStep / s.Changed : 0f;

        private static string StatsJson(Stats s, float ppm)
        {
            float w = s.Changed > 0 ? (s.MaxX - s.MinX + 1) / ppm : 0f, h = s.Changed > 0 ? (s.MaxY - s.MinY + 1) / ppm : 0f;
            return Obj(P("changedPx", s.Changed), PR("bboxM", "[" + F(w) + ", " + F(h) + "]"), P("warmShare", WarmOf(s)), P("colouredShare", ColouredOf(s)),
                P("hueMeanDeg", HueOf(s)), P("satMean", SatOf(s)), P("lumStepMean", LumStepOf(s)), P("peakLum", s.PeakLum), P("saturatedPx", s.Saturated));
        }

        private static string HalfJson(Stats s, float ppm, Radial r, float edge)
        {
            string stats = StatsJson(s, ppm);
            string radial = Obj(P("bboxWidthPx", s.Changed > 0 ? s.MaxX - s.MinX + 1 : 0), P("bboxHeightPx", s.Changed > 0 ? s.MaxY - s.MinY + 1 : 0),
                P("r98Px", r.R98), P("rMaxPx", r.RMax), P("meanPost", r.N > 0 ? "#" + Hex(r.MeanPost) : null), P("goldPxPost", r.GoldPost), P("goldPxRaw", r.GoldRaw),
                P("edgeLumAbs", edge));
            return stats.Substring(0, stats.Length - 1) + ", " + Q("radial") + ": " + radial + "}";
        }

        /// <summary>A half's changed pixels (raw lit vs dark, a channel moved ≥ 0.10) around <paramref name="centre"/>: see <see cref="Radial"/>.</summary>
        private static Radial RadialOf(Color32[] lit, Color32[] dark, Color32[] look, Vector2 centre, int x0, int x1)
        {
            var r = new Radial { R98 = float.NaN, RMax = float.NaN, MeanPost = new Color32(0, 0, 0, 255) };
            x0 = Mathf.Clamp(x0, 0, W - 1);
            x1 = Mathf.Clamp(x1, 0, W - 1);
            var hist = new int[2400];
            double sr = 0, sg = 0, sb = 0;
            for (int y = 0; y < H; y++)
            {
                float dy = y - centre.y;
                int row = y * W;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    Color32 a = lit[i], b = dark[i];
                    if (Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))) < Moved) continue;
                    r.N++;
                    float dx = x - centre.x;
                    hist[Mathf.Min(hist.Length - 1, (int)Mathf.Sqrt(dx * dx + dy * dy))]++;
                    Color32 p = look[i];
                    sr += p.r;
                    sg += p.g;
                    sb += p.b;
                    if (Gold(p)) r.GoldPost++;
                    if (Gold(a)) r.GoldRaw++;
                }
            }

            if (r.N == 0) return r;
            int acc = 0, target = Mathf.CeilToInt(.98f * r.N);
            for (int d = 0; d < hist.Length; d++)
            {
                acc += hist[d];
                if (acc < target) continue;
                r.R98 = d;
                break;
            }

            for (int d = hist.Length - 1; d >= 0; d--)
            {
                if (hist[d] == 0) continue;
                r.RMax = d;
                break;
            }

            r.MeanPost = new Color32((byte)(sr / r.N), (byte)(sg / r.N), (byte)(sb / r.N), 255);
            return r;
        }

        /// <summary>AD-VISUAL §6 A17 / style guide §2.2: a gold pixel is HSV hue 35–65° with S &gt; 0.30.</summary>
        private static bool Gold(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out float s, out _);
            float deg = h * 360f;
            return s > .30f && deg >= 35f && deg <= 65f;
        }

        /// <summary>S3: the mean |Δ luminance| (frame vs bare ground) over every pixel of the annulus <paramref name="rIn"/> –
        /// <paramref name="rOut"/> px around <paramref name="centre"/> within columns <paramref name="x0"/> – <paramref name="x1"/>.</summary>
        private static float EdgeLum(Color32[] lit, Color32[] dark, Vector2 centre, int x0, int x1, float rIn, float rOut)
        {
            if (float.IsNaN(rIn) || float.IsNaN(rOut) || rOut <= 0f) return float.NaN;
            rIn = Mathf.Max(0f, rIn);
            int ya = Mathf.Clamp((int)(centre.y - rOut) - 1, 0, H - 1), yb = Mathf.Clamp((int)(centre.y + rOut) + 1, 0, H - 1);
            x0 = Mathf.Clamp(Mathf.Max(x0, (int)(centre.x - rOut) - 1), 0, W - 1);
            x1 = Mathf.Clamp(Mathf.Min(x1, (int)(centre.x + rOut) + 1), 0, W - 1);
            float in2 = rIn * rIn, out2 = rOut * rOut;
            double sum = 0;
            int n = 0;
            for (int y = ya; y <= yb; y++)
            {
                float dy = y - centre.y;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - centre.x, d2 = dx * dx + dy * dy;
                    if (d2 < in2 || d2 > out2) continue;
                    int i = y * W + x;
                    sum += Mathf.Abs(Lum(lit[i]) - Lum(dark[i]));
                    n++;
                }
            }

            return n > 0 ? (float)(sum / n) : float.NaN;
        }

        // ------------------------------------------------------------------ colour (CIE76, AD-VISUAL §6 A1 / A4)

        private static Vector3 Lab(Color32 c)
        {
            float r = SrgbToLinear(c.r / 255f), g = SrgbToLinear(c.g / 255f), b = SrgbToLinear(c.b / 255f);
            float x = (.4124f * r + .3576f * g + .1805f * b) / .95047f;
            float y = .2126f * r + .7152f * g + .0722f * b;
            float z = (.0193f * r + .1192f * g + .9505f * b) / 1.08883f;
            float fx = LabF(x), fy = LabF(y), fz = LabF(z);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float SrgbToLinear(float v) => v <= .04045f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f);

        private static float LabF(float t) => t > .008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;

        private static float DeltaE76(Color32 a, Color32 b) => Vector3.Distance(Lab(a), Lab(b));

        private static string Hex(Color32 c) => ColorUtility.ToHtmlStringRGB(c);

        // ------------------------------------------------------------------ AD-B4 §3 / §4 thresholds (b4ad.json "ad")

        /// <summary>One AD-B4 MUST / SHOULD: its checks (measured, threshold, pass), the data it lacked and notes. PASS = every check
        /// passed with nothing missing; FAIL = a check failed; open (null) = data missing (a test not run, a frame absent).</summary>
        private sealed class Crit
        {
            public readonly string Id, Title, Basis;
            public readonly bool Must;
            public readonly List<string> Checks = new List<string>(), Missing = new List<string>(), Notes = new List<string>();
            public string Detail;
            public int Passed, Failed;

            public Crit(string id, bool must, string title, string basis)
            {
                Id = id;
                Must = must;
                Title = title;
                Basis = basis;
            }

            public void Add(string name, float measured, string threshold, bool pass)
            {
                if (float.IsNaN(measured) || float.IsInfinity(measured))
                {
                    Missing.Add(name);
                    return;
                }

                Checks.Add(Obj(P("check", name), P("measured", measured), P("threshold", threshold), P("pass", pass)));
                if (pass) Passed++;
                else Failed++;
            }

            public void AddRaw(string name, string measuredJson, string threshold, bool pass)
            {
                Checks.Add(Obj(P("check", name), PR("measured", measuredJson), P("threshold", threshold), P("pass", pass)));
                if (pass) Passed++;
                else Failed++;
            }

            public bool? Pass => Failed > 0 ? false : Missing.Count > 0 || Passed == 0 ? (bool?)null : true;

            public string Word => Pass == true ? "PASS" : Pass == false ? "FAIL" : "?";

            public string Json() => Obj(P("id", Id), P("must", Must), P("title", Title), P("basis", Basis), PB("pass", Pass), P("passed", Passed),
                P("failed", Failed), PR("missing", Strs(Missing)), PR("notes", Strs(Notes)), PR("detail", Detail ?? "null"), PR("checks", Arr(Checks)));
        }

        private static string AdJson(out string mustLine, out string shouldLine)
        {
            var crits = new List<Crit> { M1(), M2(), M3(), M4(), M5(), M6(), M7(), M8(), M9(), M10(), S1(), S2(), S3(), S4() };
            var must = new StringBuilder("AD-B4 MUST:");
            var should = new StringBuilder("AD-B4 SHOULD:");
            int pass = 0, fail = 0, open = 0;
            var items = new List<string>();
            foreach (Crit c in crits)
            {
                (c.Must ? must : should).Append(' ').Append(c.Id).Append(' ').Append(c.Word).Append(" |");
                if (c.Must)
                {
                    if (c.Pass == true) pass++;
                    else if (c.Pass == false) fail++;
                    else open++;
                }

                items.Add(c.Json());
            }

            mustLine = must.ToString().TrimEnd('|', ' ');
            shouldLine = should.ToString().TrimEnd('|', ' ');
            return Obj(P("source", "K/wp-104/b4-s07/evidence/AD-B4.md §3 MUST / §4 SHOULD (12.10): every threshold evaluated on this run's frames; PASS / FAIL is never asserted"),
                P("mustSummary", mustLine), P("shouldSummary", shouldLine), P("mustPass", pass), P("mustFail", fail), P("mustOpen", open),
                PR("a11Key", s_blindKey ?? "null"),
                P("m2RootCause", "b4-r2: CaptureTracers 02 and 07 ran in the Unity frame where 01 / 06 had queued their halo-only pass (DrawTd(2) after their own yield); "
                    + "Graphics.DrawMeshInstanced draws in every render of its frame, so 02 / 07 rendered those halos into their dark, core and lit frames (brightCorePx 33 / 24 = "
                    + "halo pixels, visiblePx 19 / 6 = lit minus a dark that held the halo). Fixed: every tracer capture starts on a fresh Unity frame; darkLeakPx per frame"),
                PR("criteria", Arr(items)));
        }

        private static RoundM FindRound(int frame, string key, string ground) => s_rounds.Find(r => r.Frame == frame && r.Key == key && r.Ground == ground);

        private static HalfM FindHalf(int frame, string key) => s_halves.Find(h => h.Frame == frame && h.Key == key);

        private static GlowM FindGlow(string ground, string addOn) => s_glows.Find(g => g.Ground == ground && g.AddOn == addOn);

        private static string GroundTag(int g) => g == 0 ? "açık taş" : "ıslak arduvaz";

        private static float Ratio(float a, float b) => b > 0f ? a / b : float.NaN;

        private static Crit M1()
        {
            var c = new Crit("M1", true, "Merdiven: AR I-V hale +5 px / çekirdek sabit / iz boyu; LMG V, SR V; A11",
                "AD-B4 §1, §3 M1 · kare 01 (açık taş) / 02 (ıslak arduvaz) AR I-V; 30 / 31 LMG ve SR I + V; 32 A11 kör sıralama");
            for (int g = 0; g < 2; g++)
            {
                int f = g == 0 ? 1 : 2;
                string tag = GroundTag(g);
                var t = new RoundM[5];
                bool all = true;
                for (int k = 0; k < 5; k++)
                {
                    t[k] = FindRound(f, "ATIS " + Roman(k + 1), Grounds[g]);
                    if (t[k] != null) continue;
                    c.Missing.Add("kare " + f + " ATIS " + Roman(k + 1));
                    all = false;
                }

                if (!all) continue;
                for (int k = 0; k < 5; k++)
                {
                    int target = ArHaloPx + HaloStepPx * k;
                    c.Add(tag + " · haloPx " + Roman(k + 1), t[k].Halo, target + " ±2", Mathf.Abs(t[k].Halo - target) <= 2);
                }

                for (int k = 0; k < 4; k++)
                {
                    int d = t[k + 1].Halo - t[k].Halo;
                    c.Add(tag + " · haloPx farkı " + Roman(k + 1) + "-" + Roman(k + 2), d, "≥ 4", d >= 4);
                }

                for (int k = 0; k < 5; k++) c.Add(tag + " · brightCorePx " + Roman(k + 1), t[k].BrightCore, ArCorePx + " ±1", Mathf.Abs(t[k].BrightCore - ArCorePx) <= 1);
                float l1 = t[0].LengthM, refL = g == 0 ? RefLengthLight : RefLengthWet;
                c.Add(tag + " · iz boyu I / WP-101 (b4-r2 " + F(refL) + " m; I " + F(l1) + " m)", Ratio(l1, refL), "0.97-1.03", Mathf.Abs(l1 / refL - 1f) <= .03f);
                c.Add(tag + " · iz boyu II / I", Ratio(t[1].LengthM, l1), "0.97-1.03", Mathf.Abs(t[1].LengthM / l1 - 1f) <= .03f);
                c.Add(tag + " · iz boyu III / I", Ratio(t[2].LengthM, l1), "≥ 1.08", t[2].LengthM / l1 >= 1.08f);
                c.Add(tag + " · iz boyu IV / I", Ratio(t[3].LengthM, l1), "≥ 1.22", t[3].LengthM / l1 >= 1.22f);
                c.Add(tag + " · iz boyu V / I (" + F(t[4].LengthM) + " m)", Ratio(t[4].LengthM, l1), "≥ 1.40", t[4].LengthM / l1 >= 1.40f);
            }

            // LMG V / SR V: the core stays on the class base (the class's tier-I tracer, AD ≈ 9 / ≈ 11 px), the halo is base + 20
            for (int g = 0; g < 2; g++)
            {
                int f = g == 0 ? 30 : 31;
                foreach (string cls in new[] { "LMG", "SR" })
                {
                    RoundM one = FindRound(f, cls + " I", Grounds[g]), five = FindRound(f, cls + " V", Grounds[g]);
                    if (one == null || five == null)
                    {
                        c.Missing.Add("kare " + f + " " + cls + " I / V");
                        continue;
                    }

                    string tag = GroundTag(g) + " · " + cls + " V";
                    c.Add(tag + " brightCorePx (taban " + cls + " I " + one.BrightCore + ", AD ≈ " + (cls == "LMG" ? 9 : 11) + ")", five.BrightCore,
                        one.BrightCore + " ±1", Mathf.Abs(five.BrightCore - one.BrightCore) <= 1);
                    c.Add(tag + " haloPx − " + cls + " I (" + five.Halo + " − " + one.Halo + ")", five.Halo - one.Halo, "+20 ±2", Mathf.Abs(five.Halo - one.Halo - 20) <= 2);
                }
            }

            // A11: the blind order is the AD's (+ 1 observer); its proxy — halo and length rise I < III < V on both grounds
            for (int g = 0; g < 2; g++)
            {
                RoundM a = FindRound(32, "ATIS I", Grounds[g]), b = FindRound(32, "ATIS III", Grounds[g]), v = FindRound(32, "ATIS V", Grounds[g]);
                if (a == null || b == null || v == null)
                {
                    c.Missing.Add("kare 32 " + Grounds[g]);
                    continue;
                }

                c.AddRaw(GroundTag(g) + " · A11 vekil: haloPx I < III < V", "[" + a.Halo + ", " + b.Halo + ", " + v.Halo + "]", "artan", a.Halo < b.Halo && b.Halo < v.Halo);
                c.AddRaw(GroundTag(g) + " · A11 vekil: iz boyu I < III < V (m)", "[" + F(a.LengthM) + ", " + F(b.LengthM) + ", " + F(v.LengthM) + "]", "artan",
                    a.LengthM < b.LengthM && b.LengthM < v.LengthM);
            }

            c.Notes.Add("A11 kör sıralama elle yapılır (AD + 1 gözlemci, 3/3): kare 32 (32-iz-a11-kor-siralama.png), şeritler A / B / C; anahtar ad.a11Key. Buradaki vekil yalnız ölçülerin sırasını gösterir.");
            c.Detail = s_blindKey;
            return c;
        }

        private static Crit M2()
        {
            var c = new Crit("M2", true, "Ölçüm tutarlılığı", "AD-B4 §2.2, §3 M2 · aynı WP-101 AR I izi: kare 02 ↔ 03 (ıslak arduvaz), 01 ↔ 03 (açık taş); bütün iz kareleri");
            for (int g = 0; g < 2; g++)
            {
                int f = g == 0 ? 1 : 2;
                RoundM a = FindRound(f, "ATIS I", Grounds[g]), b = FindRound(3, "NOTR I", Grounds[g]);
                if (a == null || b == null)
                {
                    c.Missing.Add("kare 0" + f + " / 03 " + Grounds[g]);
                    continue;
                }

                string tag = GroundTag(g) + " · kare 0" + f + " − kare 03";
                c.Add(tag + " brightCorePx (" + a.BrightCore + " / " + b.BrightCore + ")", a.BrightCore - b.BrightCore, "±1", Mathf.Abs(a.BrightCore - b.BrightCore) <= 1);
                c.Add(tag + " coreQuadPx (" + a.CoreQuad + " / " + b.CoreQuad + ")", a.CoreQuad - b.CoreQuad, "±1", Mathf.Abs(a.CoreQuad - b.CoreQuad) <= 1);
            }

            int rounds = 0, over = 0;
            var offenders = new List<string>();
            foreach (RoundM r in s_rounds)
            {
                rounds++;
                int cap = Mathf.Max(ArCorePx, ClassBase(r));
                if (r.BrightCore <= cap) continue;
                over++;
                if (offenders.Count < 12) offenders.Add(Q("kare " + r.Frame + " " + r.Lane + " " + r.Ground + ": " + r.BrightCore + " > " + cap));
            }

            if (rounds == 0) c.Missing.Add("iz kareleri");
            else c.AddRaw("bütün iz mermileri: brightCorePx ≤ max(8, sınıf tabanı)", Obj(P("rounds", rounds), P("over", over), PR("offenders", Arr(offenders))), "over = 0", over == 0);
            foreach (KeyValuePair<int, int> kv in s_darkLeak) c.Add("kare " + kv.Key + " darkLeakPx", kv.Value, "0", kv.Value == 0);
            return c;
        }

        /// <summary>A round's class base bright core: AR 8 px (AD-B4 §1); LMG / SR their tier-I tracer on the same ground (frames 30 /
        /// 31), else the AD's ≈ 9 / ≈ 11 px.</summary>
        private static int ClassBase(RoundM r)
        {
            VfxWeaponClass cls = VfxWeaponProfiles.For(r.Weapon).Class;
            if (cls != VfxWeaponClass.Lmg && cls != VfxWeaponClass.Sniper) return ArCorePx;
            RoundM one = s_rounds.Find(x => x.Weapon == r.Weapon && x.ShotTier == 1 && x.Ground == r.Ground && (x.Frame == 30 || x.Frame == 31));
            return one != null ? one.BrightCore : cls == VfxWeaponClass.Sniper ? 11 : 9;
        }

        private static Crit M3()
        {
            var c = new Crit("M3", true, "Reduce Flashes: biçim korunur, HDR kısılır", "AD-B4 §3 M3 · kare 06 (RF kapalı) ↔ 07 (RF açık 0.4): ATES V ve NOTR (WP-101), iki zemin");
            foreach (string key in new[] { KeyRfFire, KeyRfNeutral })
            {
                for (int g = 0; g < 2; g++)
                {
                    RoundM off = FindRound(6, key, Grounds[g]), on = FindRound(7, key, Grounds[g]);
                    if (off == null || on == null)
                    {
                        c.Missing.Add("kare 06 / 07 " + key + " " + Grounds[g]);
                        continue;
                    }

                    string tag = GroundTag(g) + " · " + key;
                    c.Add(tag + " · visiblePx RF / kapalı (" + on.Visible + " / " + off.Visible + ")", Ratio(on.Visible, off.Visible), "≥ 0.6", on.Visible >= .6f * off.Visible);
                    c.Add(tag + " · iz boyu RF / kapalı (" + F(on.LengthM) + " / " + F(off.LengthM) + " m)", Ratio(on.LengthM, off.LengthM), "≥ 0.85", on.LengthM >= .85f * off.LengthM);
                    bool hdr = !float.IsNaN(on.PeakLumHdr) && !float.IsNaN(off.PeakLumHdr);
                    float a = hdr ? on.PeakLumHdr : on.PeakLum, b = hdr ? off.PeakLumHdr : off.PeakLum;
                    c.Add(tag + (hdr ? " · peakLumHdr" : " · peakLum (8 bit, HDR yok)") + " RF − kapalı (" + F(a) + " / " + F(b) + ")", a - b, "< 0", a < b);
                }
            }

            // "Hale α çarpanı ≥ 0,70": the ladder's own constant (HaloAlphaFlashFactor) or function (HaloFlash) when one is there —
            // by reflection, the ladder's API is its owner's; without either the pixels above carry it alone
            float alphaFactor = StyleFloat("HaloAlphaFlashFactor");
            MethodInfo haloFlash = typeof(TracerStyle).GetMethod("HaloFlash", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int), typeof(float) }, null);
            if (!float.IsNaN(alphaFactor))
            {
                c.Add("TracerStyle.HaloAlphaFlashFactor: hale α çarpanı (RF)", alphaFactor, "≥ 0.70", alphaFactor >= .70f - 1e-4f);
            }
            else if (haloFlash != null && haloFlash.ReturnType == typeof(float))
            {
                foreach (int look in new[] { TracerStyle.NeutralLook, TracerStyle.FireLook })
                {
                    float v = (float)haloFlash.Invoke(null, new object[] { look, .4f });
                    c.Add("TracerStyle.HaloFlash(" + (look == TracerStyle.NeutralLook ? "nötr" : "ateş") + ", 0.4): hale α çarpanı", v, "≥ 0.70", v >= .70f - 1e-4f);
                }
            }
            else
            {
                c.Notes.Add("TracerStyle'da hale α çarpanı (HaloAlphaFlashFactor / HaloFlash) bulunamadı: yalnız visiblePx ile okunur");
            }

            return c;
        }

        private static Crit M4()
        {
            var c = new Crit("M4", true, "K3 işaretli mermi", "AD-B4 §3 M4 · kare 05 (işaretli + normal, iki zemin)");
            RoundM n = FindRound(5, KeyNormal, KeyStone), m = FindRound(5, KeyMarked, KeyStone), mw = FindRound(5, KeyMarked, KeySlate);
            if (n == null || m == null)
            {
                c.Missing.Add("kare 05 açık taş");
            }
            else
            {
                float need = Mathf.Max(48f, 1.2f * n.Visible);
                c.Add("açık taş · visiblePx işaretli (normal " + n.Visible + ")", m.Visible, "≥ max(48, 1.2 × normal) = " + F(need), m.Visible >= need);
                c.Add("açık taş · haloPx işaretli − normal (" + m.Halo + " − " + n.Halo + ")", m.Halo - n.Halo, "≥ 15", m.Halo - n.Halo >= 15);
                c.Add("açık taş · koyu çekirdek px (zemin lum − 0.15 altı)", m.InkDark15, "≥ 4", m.InkDark15 >= 4);
            }

            if (mw == null)
            {
                c.Missing.Add("kare 05 ıslak arduvaz");
            }
            else
            {
                c.Add("ıslak arduvaz · satMean", mw.SatMean, "≥ 0.45", mw.SatMean >= .45f);
                c.Add("ıslak arduvaz · hueMeanDeg", mw.HueMean, "250-275", mw.HueMean >= 250f && mw.HueMean <= 275f);
            }

            return c;
        }

        private static Crit M5()
        {
            var c = new Crit("M5", true, "Element kimliği açık taşta; A1 kademe I", "AD-B4 §3 M5, AD-VISUAL §6 A1 · kare 03 (kademe I, Atış = Element = I)");
            foreach (TreeElement e in Elements)
            {
                RoundM r = FindRound(3, ElementWord(e) + " I", KeyStone);
                if (r == null)
                {
                    c.Missing.Add("kare 03 " + ElementWord(e) + " I açık taş");
                    continue;
                }

                Color.RGBToHSV(TreeVfxLadder.ElementGlow(e), out float h, out _, out _);
                float palette = h * 360f, d = Mathf.Abs(Mathf.DeltaAngle(r.HueMean, palette));
                c.Add("açık taş · " + ElementWord(e) + " colouredShare", r.ColouredShare, "≥ 0.60", r.ColouredShare >= .60f);
                c.Add("açık taş · " + ElementWord(e) + " ton farkı (ölçü " + F(r.HueMean) + "°, palet glow " + F(palette) + "°)", d, "≤ 15°", d <= 15f);
            }

            // A1: the four element tracers' glow median on the post frame, wet slate, tier I — CIE76 every pair
            var med = new Color32[Elements.Length];
            bool ok = true;
            for (int i = 0; i < Elements.Length; i++)
            {
                RoundM r = FindRound(3, ElementWord(Elements[i]) + " I", KeySlate);
                if (r == null || r.GlowPx == 0)
                {
                    c.Missing.Add("kare 03 " + ElementWord(Elements[i]) + " I ıslak arduvaz glow");
                    ok = false;
                }
                else
                {
                    med[i] = r.GlowMedianPost;
                }
            }

            if (ok)
            {
                for (int i = 0; i < Elements.Length; i++)
                {
                    for (int j = i + 1; j < Elements.Length; j++)
                    {
                        float de = DeltaE76(med[i], med[j]);
                        c.Add("A1 ıslak arduvaz · ΔE76 " + ElementWord(Elements[i]) + "-" + ElementWord(Elements[j]) + " (#" + Hex(med[i]) + " / #" + Hex(med[j]) + ")", de, "≥ 25", de >= 25f);
                    }
                }
            }

            c.Notes.Add("ΔE = CIE76 (AD-VISUAL §6 A1): glow medyanı = post karede, şeridin değişen pikselleri içinde çekirdek dörtgeni (katman 0 ≥ 8) dışındakilerin kanal başına medyanı.");
            return c;
        }

        private static Crit M6()
        {
            var c = new Crit("M6", true, "Aura açık taşta; A4; A17", "AD-B4 §3 M6, AD-VISUAL §6 A4 / A17 · kare 22-24 (dikiş: batı açık taş, doğu ıslak arduvaz)");
            var halves = new HalfM[3, 2];
            for (int lv = 0; lv < 3; lv++)
            {
                for (int g = 0; g < 2; g++)
                {
                    halves[lv, g] = FindHalf(22 + lv, Grounds[g]);
                    if (halves[lv, g] == null) c.Missing.Add("kare " + (22 + lv) + " " + Grounds[g]);
                }
            }

            for (int lv = 0; lv < 3; lv++)
            {
                HalfM l = halves[lv, 0], w = halves[lv, 1];
                if (l == null || w == null) continue;
                c.Add("aura " + Roman(lv + 1) + " · açık taş changedPx", l.S.Changed, "≥ 1000", l.S.Changed >= 1000);
                c.Add("aura " + Roman(lv + 1) + " · açık taş / ıslak arduvaz changedPx (" + l.S.Changed + " / " + w.S.Changed + ")", Ratio(l.S.Changed, w.S.Changed), "≥ 0.5",
                    l.S.Changed >= .5f * w.S.Changed);
            }

            for (int g = 0; g < 2; g++)
            {
                if (halves[0, g] == null || halves[1, g] == null || halves[2, g] == null) continue;
                for (int a = 0; a < 3; a++)
                {
                    for (int b = a + 1; b < 3; b++)
                    {
                        HalfM x = halves[a, g], y = halves[b, g];
                        if (x.R.N == 0 || y.R.N == 0)
                        {
                            c.Missing.Add(GroundTag(g) + " aura " + Roman(a + 1) + "-" + Roman(b + 1) + " renk");
                            continue;
                        }

                        float de = DeltaE76(x.R.MeanPost, y.R.MeanPost);
                        c.Add(GroundTag(g) + " · A4 ΔE76 aura " + Roman(a + 1) + "-" + Roman(b + 1) + " (#" + Hex(x.R.MeanPost) + " / #" + Hex(y.R.MeanPost) + ")", de, "≥ 30", de >= 30f);
                    }
                }

                for (int a = 0; a < 2; a++)
                {
                    int ra = RingPx(halves[a, g]), rb = RingPx(halves[a + 1, g]);
                    c.Add(GroundTag(g) + " · A4 halka r " + Roman(a + 1) + "-" + Roman(a + 2) + " (" + ra + " → " + rb + " px)", rb - ra, "≥ 12 px", rb - ra >= 12);
                }
            }

            if (halves[1, 0] != null && halves[1, 1] != null)
            {
                int gold = halves[1, 0].R.GoldPost + halves[1, 1].R.GoldPost;
                c.Add("A17 aura II altın piksel (post kare, iki yarı: " + halves[1, 0].R.GoldPost + " + " + halves[1, 1].R.GoldPost + "; ham " +
                      (halves[1, 0].R.GoldRaw + halves[1, 1].R.GoldRaw) + ")", gold, "0", gold == 0);
            }

            c.Notes.Add("Halka r = yarının değişen piksel kutusu genişliği (dikişten, px) = AD'nin bboxM[0] × px/m okuması; renk = post karede değişen piksellerin ortalaması; ΔE CIE76.");
            c.Notes.Add("Altın piksel (A17, AD-VISUAL §6 / stil kılavuzu §2.2): HSV ton 35-65°, S > 0.30; aura efektinin değişen pikselleri içinde, post karede.");
            return c;
        }

        /// <summary>A4's ring radius: the half's changed-pixel box width from the seam (px) — the AD's bboxM[0] × px/m.</summary>
        private static int RingPx(HalfM h) => h.S.Changed > 0 ? h.S.MaxX - h.S.MinX + 1 : 0;

        private static Crit M7()
        {
            var c = new Crit("M7", true, "Can emme ipliği açık taşta", "AD-B4 §3 M7 · kare 21 (kurban doğuda / ıslak arduvaz, kahraman batıda / açık taş)");
            HalfM l = FindHalf(21, KeyStone), w = FindHalf(21, KeySlate);
            if (l == null || w == null)
            {
                c.Missing.Add("kare 21");
                return c;
            }

            float need = Mathf.Max(400f, .6f * w.S.Changed);
            c.Add("açık taş changedPx (ıslak arduvaz " + w.S.Changed + ")", l.S.Changed, "≥ max(400, 0.6 × ıslak) = " + F(need), l.S.Changed >= need);
            float step = LumStepOf(l.S);
            c.Add("açık taş lumStepMean", l.S.Changed > 0 ? step : float.NaN, "≥ 0.10", step >= .10f);
            return c;
        }

        private static Crit M8()
        {
            var c = new Crit("M8", true, "Ateş zemini açık taşta", "AD-B4 §3 M8 · kare 17 (t 0.3 s), 18 (t 1.5 s); ıslak arduvaz b4-r2'ye göre ±%10");
            for (int k = 0; k < 2; k++)
            {
                int f = 17 + k;
                string t = k == 0 ? "t 0.3" : "t 1.5";
                HalfM l = FindHalf(f, KeyStone), w = FindHalf(f, KeySlate);
                if (l == null || w == null)
                {
                    c.Missing.Add("kare " + f);
                    continue;
                }

                float sat = SatOf(l.S), hue = HueOf(l.S);
                c.Add(t + " · açık taş satMean", sat, "≥ 0.35", sat >= .35f);
                c.Add(t + " · açık taş hueMeanDeg", hue, "10-35", hue >= 10f && hue <= 35f);
                float sr = Ratio(SatOf(w.S), RefFireWetSat[k]), wr = Ratio(WarmOf(w.S), RefFireWetWarm[k]);
                c.Add(t + " · ıslak arduvaz satMean / b4-r2 (" + F(SatOf(w.S)) + " / " + F(RefFireWetSat[k]) + ")", sr, "0.90-1.10", Mathf.Abs(sr - 1f) <= .10f);
                c.Add(t + " · ıslak arduvaz warmShare / b4-r2 (" + F(WarmOf(w.S)) + " / " + F(RefFireWetWarm[k]) + ")", wr, "0.90-1.10", Mathf.Abs(wr - 1f) <= .10f);
                c.Notes.Add(t + " ıslak arduvaz hueMeanDeg " + F(HueOf(w.S)) + " (b4-r2 " + F(RefFireWetHue[k]) + ")");
            }

            return c;
        }

        private static Crit M9()
        {
            var c = new Crit("M9", true, "Ek ışıması (E4 ateş III, E5 V)",
                "AD-B4 §3 M9 · kare 27 (açık taş), 28 (ıslak arduvaz), td-rig görünüşü: FlashScale 0 → 1, ışıma yuvası (yuva 1) piksellerinde");
            foreach (string ground in Grounds)
            {
                string tag = ground == KeyStone ? GroundTag(0) : GroundTag(1);
                foreach (string addOn in new[] { "E4", "E5" })
                {
                    GlowM g = FindGlow(ground, addOn);
                    if (g == null)
                    {
                        c.Missing.Add(tag + " " + addOn);
                        continue;
                    }

                    c.Add(tag + " · " + addOn + " glowMaskPx (görünen ışıma yuvası pikselleri)", g.MaskPx, "> 0", g.MaskPx > 0);
                    float d = g.MeanLumOn - g.MeanLumOff;
                    c.Add(tag + " · " + addOn + " ham kare yuva maskesi ort. luminans 0 → 1 (" + F(g.MeanLumOff) + " → " + F(g.MeanLumOn) + "; HDR " + F(g.MeanHdrOff) + " → " + F(g.MeanHdrOn) + ")",
                        d, "≥ +0.15", d >= .15f);
                    if (!g.Present)
                    {
                        c.Missing.Add(tag + " " + addOn + " ekran alanı");
                        continue;
                    }

                    c.Add(tag + " · " + addOn + " post glowSpotPx (ekran alanı " + F(g.SizeX) + " × " + F(g.SizeY) + ")", g.GlowSpot,
                        "≥ " + F(g.AreaPx) + " (AD ≈ " + (addOn == "E4" ? 100 : 58) + ")", g.GlowSpot >= g.AreaPx);
                    c.Notes.Add(tag + " " + addOn + ": emisyon (FlashScale 1) " + F(g.Emission) + ", yuva 1 MPB _EmissionColor.r kapalı / açık " + F(g.SlotOff) + " / " + F(g.SlotOn));
                }
            }

            c.Notes.Add("Görünüş td-rig (C, 12.10): ışıma yuvası eklerin yana bakan penceresidir; dik kamerada E4 0 px, E5 ≈ 3 px görünür, oyunun ≈ 49° TD rig açısından 12-31 px. "
                        + "Ekran alanı = ekin sınır kutusunun bu görünüşteki izdüşümü (AD'nin ≈ 100 / ≈ 58 px'i dik kameradandı).");
            return c;
        }

        private static Crit M10()
        {
            var c = new Crit("M10", true, "B8 ölçeği", "AD-B4 §3 M10 · kare 14-16 (R 3 m, III, birinci kuşak): halka dış çapı 0.8-1.0 × 2R × 0.9");
            float best = float.NaN, bestWet = float.NaN;
            var per = new List<string>();
            for (int f = 14; f <= 16; f++)
            {
                HalfM l = FindHalf(f, KeyStone), w = FindHalf(f, KeySlate);
                if (l == null)
                {
                    c.Missing.Add("kare " + f);
                    continue;
                }

                float dl = Diameter(l), dw = w != null ? Diameter(w) : float.NaN;
                if (!float.IsNaN(dl) && (float.IsNaN(best) || dl > best)) best = dl;
                if (!float.IsNaN(dw) && (float.IsNaN(bestWet) || dw > bestWet)) bestWet = dw;
                per.Add(Obj(P("frame", f), P("tS", l.T), P("lightStoneM", dl), P("wetSlateM", dw), P("lightStoneR98M", 2f * l.R.R98 / l.Ppm),
                    P("wetSlateR98M", w != null ? 2f * w.R.R98 / w.Ppm : float.NaN)));
            }

            c.Add("açık taş halka dış çapı (karelerin en büyüğü, bboxM[1])", best, "4.3-5.4 m", best >= 4.3f && best <= 5.4f);
            c.Notes.Add("ıslak arduvaz en büyük çap " + F(bestWet) + " m (bilgi; okunurluğu S3)");
            c.Notes.Add("çap = yarının değişen piksel kutusu yüksekliği / zemin px/m (AD'nin b4-r2 okuması: bboxM[1]); r98 iki katı yanında");
            c.Detail = Arr(per);
            return c;
        }

        private static float Diameter(HalfM h) => h.S.Changed > 0 ? (h.S.MaxY - h.S.MinY + 1) / h.Ppm : float.NaN;

        /// <summary>A public static float field / constant / property of <see cref="TracerStyle"/> by name (the ladder owner's API, read
        /// by reflection so this capture never depends on it); NaN when there is none.</summary>
        private static float StyleFloat(string name)
        {
            FieldInfo f = typeof(TracerStyle).GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (f != null && f.FieldType == typeof(float)) return (float)f.GetValue(null);
            PropertyInfo p = typeof(TracerStyle).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (p != null && p.PropertyType == typeof(float) && p.GetIndexParameters().Length == 0) return (float)p.GetValue(null);
            return float.NaN;
        }

        private static Crit S1()
        {
            var c = new Crit("S1", false, "V'te hale HDR ×1.5", "AD-B4 §4 S1 · kare 02 (ıslak arduvaz), post karede");
            RoundM iv = FindRound(2, "ATIS IV", KeySlate), v = FindRound(2, "ATIS V", KeySlate);
            if (iv == null || v == null) c.Missing.Add("kare 02 ATIS IV / V");
            else c.Add("post visiblePx V − IV (" + v.VisiblePost + " − " + iv.VisiblePost + ")", v.VisiblePost - iv.VisiblePost, "≥ 6", v.VisiblePost - iv.VisiblePost >= 6);
            return c;
        }

        private static Crit S2()
        {
            var c = new Crit("S2", false, "Aura III değer artışı", "AD-B4 §4 S2 · kare 24 ıslak arduvaz");
            HalfM w = FindHalf(24, KeySlate);
            if (w == null) c.Missing.Add("kare 24");
            else c.Add("ıslak arduvaz lumStepMean", w.S.Changed > 0 ? LumStepOf(w.S) : float.NaN, "≥ 0.08", LumStepOf(w.S) >= .08f);
            return c;
        }

        private static Crit S3()
        {
            var c = new Crit("S3", false, "Sumi halkalar ıslak arduvazda", "AD-B4 §4 S3 · kare 10 (B5 III t 0.35), 16 (B8 t 0.35): halka kenarı = açık taş yarısının r98 halkası (6 px)");
            foreach (int f in new[] { 10, 16 })
            {
                HalfM w = FindHalf(f, KeySlate), l = FindHalf(f, KeyStone);
                if (w == null || l == null)
                {
                    c.Missing.Add("kare " + f);
                    continue;
                }

                c.Add("kare " + f + " ıslak arduvaz kenar luminans farkı (r " + F(l.R.R98 - 6f) + "-" + F(l.R.R98) + " px; açık taş " + F(l.EdgeLum) + ")", w.EdgeLum, "≥ 0.06", w.EdgeLum >= .06f);
            }

            return c;
        }

        private static Crit S4()
        {
            var c = new Crit("S4", false, "Zehir bulutu açık taşta", "AD-B4 §4 S4 · kare 19 (t 0.3), 20 (t 1.5), M8 yöntemi");
            foreach (int f in new[] { 19, 20 })
            {
                HalfM l = FindHalf(f, KeyStone);
                if (l == null) c.Missing.Add("kare " + f);
                else c.Add("kare " + f + " açık taş colouredShare", ColouredOf(l.S), "≥ 0.50", ColouredOf(l.S) >= .50f);
            }

            return c;
        }

        // ------------------------------------------------------------------ output: PNG, b4ad.json, contact.png

        private void AddFrame(int n, string file, string label, Color32[] img, Color32[] raw, int w, int h, RectInt tile, string json, List<Mark> marks)
        {
            if (!string.IsNullOrEmpty(_dir))
            {
                Color32[] stamped = (Color32[])img.Clone();
                Text(stamped, w, h, 14, h - 14, label, w >= 1280 ? 3 : 2, Ink, true, w - 28);
                if (marks != null)
                {
                    foreach (Mark m in marks) Text(stamped, w, h, m.X, m.Top, m.Text, 2, Ink, true, w - m.X);
                }

                string path = Path.Combine(_dir, file);
                WritePng(path, stamped, w, h);
                _written.Add(path);
                if (_raw && raw != null && !ReferenceEquals(raw, img))
                {
                    WritePng(Path.Combine(_dir, Path.GetFileNameWithoutExtension(file) + "-raw.png"), raw, w, h);
                }
            }

            s_frames[n] = new Frame { N = n, File = file, Label = label, Json = json, Tile = Resample(img, w, h, tile, TW, TH) };
        }

        private void AssertWritten()
        {
            foreach (string path in _written) Assert.IsTrue(File.Exists(path), "kare yazılmadı: " + path);
            Debug.Log("[DD-TEST] wp104b4.ad frames=" + s_frames.Count + " written=" + _written.Count + " dir=" + (_dir ?? "-") + " lookPost=" + _lookPost + " hdr=" + _hdrOk);
        }

        /// <summary>b4ad.json and contact.png with every frame so far (each test rewrites them: the last one run leaves the full set),
        /// the AD thresholds evaluated on whatever frames exist ("ad"; a criterion whose frames are missing stays open).</summary>
        private void WriteOutputs()
        {
            if (string.IsNullOrEmpty(_dir) || s_frames.Count == 0) return;
            string ad = AdJson(out string mustLine, out string shouldLine);
            var sb = new StringBuilder();
            sb.Append("{\n  \"header\": ").Append(s_header ?? "{}").Append(",\n  \"ad\": ").Append(ad).Append(",\n  \"frames\": [\n");
            int k = 0;
            foreach (Frame f in s_frames.Values) sb.Append(k++ == 0 ? "    " : ",\n    ").Append(f.Json);
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(Path.Combine(_dir, "b4ad.json"), sb.ToString());
            Debug.Log("[DD-TEST] wp104b4.ad " + mustLine + " || " + shouldLine);

            const int band = 2 * LabelH + 10;   // the MUST / SHOULD summary above the tiles
            int rows = (s_frames.Count + Columns - 1) / Columns, cellH = TH + LabelH, sw = Columns * TW, sh = rows * cellH + band;
            var sheet = new Color32[sw * sh];
            for (int i = 0; i < sheet.Length; i++) sheet[i] = new Color32(18, 18, 20, 255);
            Text(sheet, sw, sh, 8, sh - 6, mustLine, 2, Ink, false, sw - 16);
            Text(sheet, sw, sh, 8, sh - 6 - LabelH, shouldLine, 2, Ink, false, sw - 16);
            k = 0;
            foreach (Frame f in s_frames.Values)
            {
                int x0 = (k % Columns) * TW, bottom = sh - band - (k / Columns + 1) * cellH;
                for (int y = 0; y < TH; y++)
                {
                    for (int x = 0; x < TW; x++) sheet[(bottom + LabelH + y) * sw + x0 + x] = f.Tile[y * TW + x];
                }

                Text(sheet, sw, sh, x0 + 4, bottom + LabelH - 4, f.Label, 2, Ink, false, TW - 8);
                k++;
            }

            WritePng(Path.Combine(_dir, "contact.png"), sheet, sw, sh);
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

        /// <summary>Box-filtered (down) / nearest (up) resample of <paramref name="roi"/> to a tw × th tile.</summary>
        private static Color32[] Resample(Color32[] src, int sw, int sh, RectInt roi, int tw, int th)
        {
            var dst = new Color32[tw * th];
            for (int ty = 0; ty < th; ty++)
            {
                int y0 = roi.y + ty * roi.height / th, y1 = Mathf.Max(y0 + 1, roi.y + (ty + 1) * roi.height / th);
                for (int tx = 0; tx < tw; tx++)
                {
                    int x0 = roi.x + tx * roi.width / tw, x1 = Mathf.Max(x0 + 1, roi.x + (tx + 1) * roi.width / tw);
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = Mathf.Clamp(y, 0, sh - 1) * sw;
                        for (int x = x0; x < x1; x++)
                        {
                            Color32 c = src[row + Mathf.Clamp(x, 0, sw - 1)];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                            n++;
                        }
                    }

                    dst[ty * tw + tx] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            }

            return dst;
        }

        // ------------------------------------------------------------------ 5 × 7 pixel font (labels on the frames and the sheet)

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

        /// <summary>Draws <paramref name="text"/> (upper-cased, Turkish letters folded to ASCII) with its top-left at (x, top) in a
        /// bottom-up pixel buffer; <paramref name="box"/> darkens a backing box first.</summary>
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
                    case 'Ō': c = 'O'; break;
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

        private static string PB(string key, bool? value) => Q(key) + ": " + (value.HasValue ? (value.Value ? "true" : "false") : "null");

        private static string PR(string key, string rawJson) => Q(key) + ": " + rawJson;

        private static string Obj(params string[] pairs) => "{" + string.Join(", ", pairs) + "}";

        private static string Obj(List<string> pairs) => "{" + string.Join(", ", pairs) + "}";

        private static string Arr(List<string> items) => "[" + string.Join(", ", items) + "]";

        private static string Strs(List<string> items)
        {
            var quoted = new List<string>(items.Count);
            foreach (string s in items) quoted.Add(Q(s));
            return Arr(quoted);
        }

        // ------------------------------------------------------------------ fixtures (Wp104B4VfxPlayTests)

        private TracerRibbonRenderer AttachTdTracer()
        {
            _hasTracerFixture = true;
            FieldInfo instance = typeof(TracerRibbonRenderer).GetField("s_instance", StaticFields);
            _previousTracer = (TracerRibbonRenderer)instance.GetValue(null);
            _previousInterimTrails = ProjectileDriver.DrawInterimTrails;
            if (_previousTracer != null)
            {
                _previousTracerEnabled = _previousTracer.enabled;
                _previousTracer.enabled = false;
            }

            SaveAndClearDelegates(typeof(ProjectileEvents));
            instance.SetValue(null, null);   // avoid Awake's duplicate-owner Destroy path
            var owner = new GameObject("WP104-AD TD tracer fixture");
            owner.transform.SetParent(_root.transform, false);
            _tracer = owner.AddComponent<TracerRibbonRenderer>();
            _tracer.enabled = false;   // nothing ages or draws on its own: the capture queues DrawTd itself
            Assert.IsTrue(_tracer.Ready, "real DD/VFX Ribbon shader must be present");
            return _tracer;
        }

        private static ProjectileSpawnInfo TdSpawn(int id, ClassicWeaponId weapon, WeaponCaliber caliber, Vector3 origin, float speed) =>
            new ProjectileSpawnInfo(id, int.MaxValue, weapon, ProjectileKind.Bullet, caliber, origin, Vector3.right, speed, 0);

        private static int DrawTd(TracerRibbonRenderer tracer, int layer) =>
            (int)typeof(TracerRibbonRenderer).GetMethod("DrawTd", Private).Invoke(tracer, new object[] { layer });

        private static int InvokeInk(TracerRibbonRenderer tracer) =>
            (int)typeof(TracerRibbonRenderer).GetMethod("DrawTdInk", Private).Invoke(tracer, null);

        /// <summary>A field of the live TD record with this id (reflection on the private records); null when there is none.</summary>
        private static object RecordValue(TracerRibbonRenderer tracer, int id, string field)
        {
            var rounds = (Array)typeof(TracerRibbonRenderer).GetField("_tdRounds", Private).GetValue(tracer);
            foreach (object r in rounds)
            {
                Type t = r.GetType();
                FieldInfo f = t.GetField(field);
                if ((bool)t.GetField("Alive").GetValue(r) && (int)t.GetField("Id").GetValue(r) == id) return f != null ? f.GetValue(r) : null;
            }

            return null;
        }

        private void SaveAndClearDelegates(Type type)
        {
            foreach (EventInfo signal in type.GetEvents(StaticFields))
            {
                FieldInfo field = type.GetField(signal.Name, StaticFields);
                Assert.IsNotNull(field, "event backing field: " + signal.Name);
                _savedDelegates.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
                field.SetValue(null, null);
            }
        }

        private static void SetModeState(CameraMode mode) =>
            typeof(CameraModeService).GetField("s_current", StaticFields).SetValue(null, mode);
    }
}
