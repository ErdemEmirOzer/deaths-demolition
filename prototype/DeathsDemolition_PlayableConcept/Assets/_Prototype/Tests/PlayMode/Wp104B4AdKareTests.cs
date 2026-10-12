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
using UnityEngine.SceneManagement;
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
    /// <item>B4-2 (12.10): 33 · 34 S2's aura III on each whole ground (lumStepMean: E's light inner line); 35 · 36 B9, Seeker Swarm's
    /// bending trail on each whole ground — Shot V AR extra-volley TD records: the designed curve (3 m + a 30° turn + 3 m) and its
    /// straight twin laid by F's SetTdPathForTests, and (optional) a real round turning toward a body at the 25° cone edge whose path
    /// S-02 keeps (TryGetPath) and F's TickTd reads — every new API by reflection ("missing" without it); 27–29 again for M9post with
    /// F's socket glint cards (hidden while the glow-slot mask and M9's raw binding frame render).</item>
    /// <item>B4-3 (12.10): 37 · 38 S-04's Event Horizon (B6 alternative) — its well orbiting a hero stand-in at two moments of the orbit, 37
    /// on the whole light stone, 38 on the whole wet slate (the orbit runs on between them: two places on the circle), through the real
    /// systems the EditMode way (GD-TREE aw_alt_kx stats, TreeSystemsRunner.SetAnchor + Tick once a frame on the 60 Hz game clock, the
    /// DDOL driver parked) and the binder's LateUpdate; each frame then takes the anchor away and times the set out ("ad" B43-E1).</item>
    /// </list>
    /// Every "alone on black" render (the ground off: the tracer core layer, the halos) is taken beside a bare ground-off render of the
    /// same Unity frame with nothing of the capture queued: coreLeakPx / haloLeakPx count what that bare render holds (0 = the measure saw
    /// only its own frame's draws) and the core / halo measures count a pixel's rise over it. The camera leaves the Environment layer out
    /// (b4-r3: an earlier fixture's Run scene veiled every ground-off render through the world's volumetric light, ad.r3RootCause);
    /// groundVeilLevels proves each frame's ground is the bare ground.
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
        private static readonly SortedDictionary<int, int> s_coreLeak = new SortedDictionary<int, int>(), s_haloLeak = new SortedDictionary<int, int>();
        private static readonly SortedDictionary<int, float> s_veil = new SortedDictionary<int, float>();
        private static readonly List<SeekM> s_seeks = new List<SeekM>();
        private static readonly List<HorizonM> s_horizons = new List<HorizonM>();
        private static string s_header, s_blindKey, s_seekMissing, s_realMissing, s_seekNote, s_cardJson;
        private static readonly Color32 Black32 = new Color32(0, 0, 0, 255);
        /// <summary>B4-2 B9 (GD-TREE sh_cap Seeker Swarm): extra shots lock a body inside ExtraShotSeekDegrees 25° up to ProjectileSystem's
        /// SeekRangeMetres 20 m. The capture's body sits at both edges — the nearest an AR round can still reach: 2 · v / ω · sin 25° ≈
        /// 17.5 m at 260 m/s and 720°/s.</summary>
        private const float SeekConeDegrees = 25f, SeekBodyMetres = 20f, SeekBandMetres = 1f;
        private const string GroupMust = "MUST", GroupShould = "SHOULD", GroupB42 = "B4-2", GroupB43 = "B4-3";
        /// <summary>The B4-2 summary line (C-00 12.10): S2, S3, S4, M8 at FireUnderAlpha .225, M9post (F's socket card), B9.</summary>
        private static readonly string[] B42Ids = { "S2", "S3", "S4", "M8", "M9post", "B9" };
        /// <summary>The B4-3 summary line (C-00 12.10): E1, the Event Horizon set on its orbiting well.</summary>
        private static readonly string[] B43Ids = { "B43-E1" };
        /// <summary>B4-3 (37 / 38): GD-TREE aw_alt_kx (run-tree-v2.json: radius 2 m, share 0.4, orbit 3 m) and the two moments after the well
        /// comes up (0.6 s: ≈ 43° on the orbit; 1.8 s after it came up again: ≈ 172° — the angle runs on from where it stopped).</summary>
        private const float HorizonRadiusM = 2f, HorizonShare = .4f, HorizonOrbitM = 3f;
        private static readonly float[] HorizonMoments = { .6f, 1.8f };
        private const string AdKabulA1 = "C-00 12.10: M5 A1 AD kabul - ölçülmeye devam (kademe I, ıslak arduvaz)";
        private const string AdKabulA4 = "C-00 12.10: M6 A4 AD kabul - ölçülmeye devam (iki zemin)";

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

        /// <summary>One tracer frame's renders: ground only (raw / post), the bare ground-off render, layer 0 alone on black, every layer
        /// (raw / post / HDR luminance); on the next Unity frame the bare ground-off render again and the halos alone on black. The leaks
        /// and the ground veil are their checks (see <see cref="Shoot"/>).</summary>
        private sealed class Shots
        {
            public Color32[] Dark, DarkLook, Black, Core, Lit, Look, HaloBlack, Halo;
            public float[] Hdr;
            public int Ink, DarkLeak, CoreLeak, HaloLeak, BlackMax, HaloInstances;
            public float Veil;
        }

        /// <summary>B4-2 B9: one record of a seeker frame (35 / 36): KIVRILAN / DUZ (designed, synthetic) or GERCEK (a real round).</summary>
        private sealed class SeekLane
        {
            public string Key, Label;
            public bool Designed, Laid;
            public int Slot, Generation, PathCount;
            /// <summary>GERCEK: the body it turns to; designed: the path's newest point.</summary>
            public Vector3 Body;
            public Vector3[] Path;
            /// <summary>GERCEK: its position after every fixed step, from the muzzle; designed: the path's points.</summary>
            public readonly List<Vector3> Flight = new List<Vector3>();
        }

        /// <summary>B4-2 B9: what one lane of a seeker frame measured (the "ad" B9 criterion reads it).</summary>
        private sealed class SeekM
        {
            public int Frame, PathPoints, TrailPx, Visible025, Visible05, Layer2025, Layer205, Bright025, Quad025, RecordSeekPoints;
            public string Key, Ground;
            public bool Designed;
            public bool? Tracer;
            public float PathBendM, FlightBendM, DrawnBendM, DrawnLengthM, RecordHaloPx;
            /// <summary>The same bend over every pixel the streak moved ≥ 8 levels (its faint tail too): what is drawn, against what reads.</summary>
            public float DrawnBendFaintM, DrawnLengthFaintM;
        }

        /// <summary>B4-3: one Event Horizon frame (37 / 38) — what B43-E1 reads. Offsets are world xz from HorizonCenter (the cards) and the
        /// changed pixels' centroid against HorizonCenter's pixel at the ground's px/m (the drawn centre); FadeS = game seconds from the tick
        /// that took the anchor away until both cards were back in the pool (NaN: not within 2 s).</summary>
        private sealed class HorizonM
        {
            public int Frame, Ticks, ChangedPx, DrawnPx, AfterPx, PeakCards, PeakHoles, Procs, Sets;
            public string Ground;
            public bool Up, Down;
            public float Moment, TActual, AngleDeg, OrbitM, CoreOffsetM = float.NaN, RingOffsetM = float.NaN, DrawnOffsetM = float.NaN, CoreLiftM = float.NaN;
            public float CoreCardM = float.NaN, RingCardM = float.NaN, FadeS = float.NaN;
            public Vector3 Centre;
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
            /// <summary>B4-2: the same ON mean with F's socket cards shown (MeanLumOn is the slot alone, cards hidden: M9's binding).</summary>
            public float MeanLumOnCard = float.NaN;
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
        // B4-3: the DDOL TreeSystemsDriver frames 37–38 parked (and whether it was there before them: one their own ticks made is
        // destroyed at the tear-down), TreeSystemsRunner.BeforeTick while it is off, whether the tree systems need a reset
        private TreeSystemsDriver _parkedDriver;
        private bool _parkedDriverWasThere, _usedHorizon;
        private Action _savedBeforeTick;

        [OneTimeSetUp]
        public void Once()
        {
            s_frames.Clear();
            s_rounds.Clear();
            s_halves.Clear();
            s_glows.Clear();
            s_darkLeak.Clear();
            s_coreLeak.Clear();
            s_haloLeak.Clear();
            s_veil.Clear();
            s_seeks.Clear();
            s_horizons.Clear();
            s_header = null;
            s_blindKey = null;
            s_seekMissing = null;
            s_realMissing = null;
            s_seekNote = null;
            s_cardJson = null;
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
            if (_usedHorizon)
            {
                TreeSystemsRunner.Reset();   // B4-3: the Horizon down, the systems' statics clean for the next fixture
                TreeSystemsRunner.BeforeTick = _savedBeforeTick;
                _savedBeforeTick = null;
                _usedHorizon = false;
            }

            if (_parkedDriver != null)
            {
                if (_parkedDriverWasThere) _parkedDriver.enabled = true;
                else Object.Destroy(_parkedDriver.gameObject);   // made by this capture's own ticks: the world as it was found
            }

            _parkedDriver = null;
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

            // 35 · 36 — B4-2 B9: Seeker Swarm's bending trail on each whole ground (SeekFrames)
            yield return SeekFrames(tracer);
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
            // M9's "ek maskesi": each add-on's glow-slot pixels as the rig sees them (GlowMask: slot 1 white, the rest black, unlit) — with
            // F's socket glint cards hidden (S-07 12.10: an added card over the slot saturates the 8-bit mask render), shown again after
            string cardsForMask = SetCards(glow, false);
            bool[] sideMask = GlowMask(mount.SideAttachment, out int sidePx), ringMask = GlowMask(mount.RingAttachment, out int ringPx);
            SetCards(glow, true);
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
                // M9 (B4-1, the binding) reads the glow slot alone, as b4-r4 did: the raw ON frame with F's cards hidden (they follow
                // FlashScale, so the OFF frame has none); then the cards back for the PNG, M9post's bloom spot and the ×4 crop
                SetCards(glow, false);
                Color32[] onBare = Grab(false, true);
                float[] hdrBare = GrabHdrLum();   // the shared HDR buffer: read before the next GrabHdrLum
                float lumSideBare = MeanLum(onBare, sideMask), lumRingBare = MeanLum(onBare, ringMask);
                float hdrSideBare = MeanOf(hdrBare, sideMask), hdrRingBare = MeanOf(hdrBare, ringMask);
                SetCards(glow, true);
                yield return null;   // the cards repainted (Paint) and tracked onto their slots (LateUpdate)
                yield return null;
                Color sideOn = glow.SideEmission, ringOn = glow.RingEmission;
                Color32[] onRaw = Grab(false, true);
                Color32[] onLook = _lookPost ? Grab(true, true) : onRaw;
                float[] hdr = GrabHdrLum();
                string cardOn = CardJson(glow, glowGo, model.gameObject);   // M9post (B4-2): F's socket glow card, by reflection
                var e4 = new GlowM
                {
                    Ground = groundKey, AddOn = "E4", Name = "E4 ates III (side)", Emission = sideOn.r, MaskPx = sidePx,
                    MeanLumOn = lumSideBare, MeanHdrOn = hdrSideBare, MeanLumOnCard = MeanLum(onRaw, sideMask), SlotOn = SlotEmission(mount.SideAttachment, scratch)
                };
                var e5 = new GlowM
                {
                    Ground = groundKey, AddOn = "E5", Name = "E5 V (ring)", Emission = ringOn.r, MaskPx = ringPx,
                    MeanLumOn = lumRingBare, MeanHdrOn = hdrRingBare, MeanLumOnCard = MeanLum(onRaw, ringMask), SlotOn = SlotEmission(mount.RingAttachment, scratch)
                };

                // OFF: the ladder × 0 — the same model and add-ons, the glow slot black
                yield return SetFlash(glow, 0f);
                Color32[] offRaw = Grab(false, true);
                Color32[] offLook = _lookPost ? Grab(true, true) : offRaw;
                hdr = GrabHdrLum();
                string cardOff = CardJson(glow, glowGo, model.gameObject);
                float veil = GroundVeil(offRaw);
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
                    P("mountPainted", glow.Mount == mount), P("visibleRenderers", VisibleRenderers(model.gameObject)),
                    P("cardsForGlowMask", cardsForMask), P("m9RawCards", "hidden (the glow slot alone; CardsEnabled false for the ON raw frame)"));
                var marks = new List<Mark>();
                Bounds? side = BoundsOf(mount.SideAttachment), ring = BoundsOf(mount.RingAttachment);
                if (side.HasValue) marks.Add(MarkAt(side.Value.center + new Vector3(0f, 0f, -.45f), "E4"));
                if (ring.HasValue) marks.Add(MarkAt(ring.Value.center + new Vector3(0f, 0f, .35f), "E5"));
                marks.AddRange(FloorMarks());
                int n = 27 + pass;
                s_veil[n] = veil;
                if (pass == 0) s_cardJson = cardOn;
                string file = n + (pass == 0 ? "-ek-isima-e4-ates-III-e5-V-acik-tas.png" : "-ek-isima-e4-ates-III-e5-V-islak-arduvaz.png");
                string label = n + " E4 ATES III (" + F(e4.Emission) + ") + E5 V (" + F(e5.Emission) + ") / " + ground;
                AddFrame(n, file, label, onLook, onRaw, W, H, Roi(centre, 4f),
                    Obj(P("n", n), P("file", file), P("label", label), P("kind", "addOnGlow"), P("ground", FloorName()), P("view", "td-rig"),
                        P("viewDetail", viewDetail), P("order", "FlashScale 1, then 0; the glow-slot masks before both"), P("groundVeilLevels", veil),
                        PR("mount", common), PR("socketCardOn", cardOn), PR("socketCardOff", cardOff), PR("e4", GlowJson(e4)), PR("e5", GlowJson(e5))), marks);

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

        // ------------------------------------------------------------------ 37–38 B4-3 Event Horizon (need the AP-041 tree rows)

        /// <summary>
        /// B4-3 (S-04 7606e452, B6 Event Horizon): the orbiting well's set at two moments of its orbit, one on each whole ground — 37 light
        /// stone <see cref="HorizonMoments"/>[0] after the well came up, 38 wet slate [1] after it came up again (the angle runs on: two places
        /// on the circle). S-04's frame is the parked driver's step done here once a frame (TreeSystemsRunner.Tick on the 60 Hz game clock),
        /// the binder's LateUpdate places the set after it; every capture is taken with no Tick since that LateUpdate, so the cards stand
        /// where HorizonCenter is. Then the anchor goes (S-04: no player → the well is down) and the set is timed out (B43-E1).
        /// </summary>
        [UnityTest, Order(4), Explicit(HedefliKosu), Timeout(180000)]
        public IEnumerator Kare37_38_OlayUfku_YorungedeIkiAn_IkiZemin()
        {
            LogAssert.ignoreFailingMessages = true;
            TreeVfxBinder binder = Binder();
            TakeTreeSystems();
            TreeSystemsRunner.Reset();   // the orbit from angle 0
            TreeStats s = TreeStats.Neutral;
            s.WeaponPower = 100f;
            s.SetBranchTier(TreeBranch.Chaos, 5);
            s.HorizonRadius = HorizonRadiusM;
            s.HorizonShare = HorizonShare;
            s.HorizonOrbit = HorizonOrbitM;
            TreeRuntime.SetTestStats(s);
            Transform hero = StandIn("WP104-AD olay ufku kahraman", O, HeroGrey);
            s_horizons.Clear();
            yield return null;
            Physics.SyncTransforms();
            yield return HorizonFrame(binder, hero, 37, Floor.Light, HorizonMoments[0]);
            yield return HorizonFrame(binder, hero, 38, Floor.Slate, HorizonMoments[1]);
            TreeSystemsRunner.Reset();
            TreeRuntime.ClearTestStats();
            SetFloor(Floor.Split);
            AssertWritten();
        }

        /// <summary>
        /// One Event Horizon frame: the well down for the dark frame, up on the hero (SetAnchor), ticked once a frame until
        /// <paramref name="moment"/> game seconds have passed, captured (the cards against HorizonCenter, the drawn centre), then down again
        /// (ClearAnchor) and the frames counted until both cards are back in the pool.
        /// </summary>
        private IEnumerator HorizonFrame(TreeVfxBinder binder, Transform hero, int n, Floor floor, float moment)
        {
            SetFloor(floor);
            ReleaseDecals();
            TreeSystemsRunner.ClearAnchor();
            HorizonTick();   // the well down for the dark frame
            binder.Clear();
            binder.ResetCountersForTests();
            yield return null;
            yield return null;
            Color32[] dark = Grab(false, true);
            float veil = GroundVeil(dark);
            s_veil[n] = veil;

            var m = new HorizonM { Frame = n, Ground = floor == Floor.Light ? KeyStone : KeySlate, Moment = moment };
            TreeSystemsRunner.SetAnchor(hero.position);
            float t0 = VfxClock.Now;
            while (VfxClock.Now - t0 < moment - 1e-4f && m.Ticks < 600)
            {
                HorizonTick();   // S-04's frame (the parked driver's step)
                m.Ticks++;
                yield return null;   // the binder's LateUpdate placed the set after it
                m.PeakCards = Mathf.Max(m.PeakCards, Mathf.Max(binder.LiveHorizons, binder.LiveHorizonRings));
                m.PeakHoles = Mathf.Max(m.PeakHoles, binder.LiveHoles);
            }

            m.TActual = VfxClock.Now - t0;
            m.Up = TreeGravityWells.HorizonActive;
            Vector3 centre = m.Centre = TreeGravityWells.HorizonCenter;   // no Tick since that LateUpdate
            FlipbookEffect core = binder.HorizonCore, ring = binder.HorizonRing;
            Color32[] lit = Grab(false, true);
            Color32[] look = _lookPost ? Grab(true, true) : lit;
            float ppm = PxPerMetre(0f);
            Stats all = Region(lit, dark, 0, W - 1, 0, H - 1);
            Vector2 cpx = Px(centre), drawn = ChangedCentroid(lit, dark, out m.DrawnPx);
            Radial rad = RadialOf(lit, dark, look, cpx, 0, W - 1);
            m.ChangedPx = all.Changed;
            m.AngleDeg = Mathf.Repeat(Mathf.Atan2(centre.z - hero.position.z, centre.x - hero.position.x) * Mathf.Rad2Deg, 360f);
            m.OrbitM = XzDistance(centre, hero.position);
            m.Procs = binder.HorizonProcs;
            m.Sets = binder.HorizonSets;
            if (core != null)
            {
                m.CoreOffsetM = XzDistance(core.transform.position, centre);
                m.CoreLiftM = core.transform.position.y - centre.y;
                m.CoreCardM = core.transform.GetChild(0).localScale.x;
            }

            if (ring != null)
            {
                m.RingOffsetM = XzDistance(ring.transform.position, centre);
                m.RingCardM = ring.transform.GetChild(0).localScale.x;
            }

            if (m.DrawnPx > 0) m.DrawnOffsetM = (drawn - cpx).magnitude / ppm;
            int coreFrame = core != null ? core.CurrentFrame : -1;
            float coreAlpha = core != null ? core.AlphaCap : float.NaN, ringAlpha = ring != null ? ring.AlphaCap : float.NaN;

            // down: the anchor goes (S-04: no player → HorizonActive false, no moment) — both cards back in the pool within 0.3 s
            TreeSystemsRunner.ClearAnchor();
            HorizonTick();
            m.Down = !TreeGravityWells.HorizonActive;
            float off = VfxClock.Now;
            for (int f = 0; f < 120 && float.IsNaN(m.FadeS); f++)
            {
                yield return null;
                if (binder.LiveHorizons == 0 && binder.LiveHorizonRings == 0) m.FadeS = VfxClock.Now - off;
            }

            m.AfterPx = Region(Grab(false, true), dark, 0, W - 1, 0, H - 1).Changed;
            s_horizons.RemoveAll(h => h.Frame == n);
            s_horizons.Add(m);
            AddHalf(n, "eventHorizon", m.Ground, 0, moment, all, ppm, rad, float.NaN);

            string nn = n.ToString("00", CultureInfo.InvariantCulture);
            string groundSlug = floor == Floor.Light ? "acik-tas" : "islak-arduvaz", groundName = floor == Floor.Light ? "ACIK TAS" : "ISLAK ARDUVAZ";
            string file = nn + "-olay-ufku-yorunge-" + groundSlug + "-t" + Mathf.RoundToInt(moment * 1000f).ToString("0000", CultureInfo.InvariantCulture) + ".png";
            string label = nn + " B4-3 OLAY UFKU R" + F(HorizonRadiusM) + " YORUNGE " + F(HorizonOrbitM) + " M, " + Mathf.RoundToInt(m.AngleDeg).ToString(CultureInfo.InvariantCulture)
                           + " DERECE T+" + moment.ToString("0.00", CultureInfo.InvariantCulture) + "S / " + groundName;
            string json = Obj(P("n", n), P("file", file), P("label", label), P("kind", "eventHorizon"), P("ground", FloorName()), P("groundVeilLevels", veil),
                P("tTargetS", moment), P("tActualS", m.TActual), P("ticks", m.Ticks), P("clock", "game time, captureDeltaTime 1/60; TreeSystemsRunner.Tick once a frame (the driver parked)"),
                P("horizonActive", m.Up), P("radiusM", HorizonRadiusM), P("orbitM", m.OrbitM), P("angleDeg", m.AngleDeg), PR("heroXZ", XZ(hero.position)), PR("centreXZ", XZ(centre)),
                PR("coreXZ", core != null ? XZ(core.transform.position) : "null"), PR("ringXZ", ring != null ? XZ(ring.transform.position) : "null"),
                P("coreOffsetM", m.CoreOffsetM), P("ringOffsetM", m.RingOffsetM), P("coreLiftM", m.CoreLiftM), P("coreCardM", m.CoreCardM), P("ringCardM", m.RingCardM),
                P("coreFrame", coreFrame), P("coreAlphaCap", coreAlpha), P("ringAlphaCap", ringAlpha),
                PR("centrePx", "[" + F(cpx.x) + ", " + F(cpx.y) + "]"), PR("drawnCentrePx", "[" + F(drawn.x) + ", " + F(drawn.y) + "]"), P("drawnPx", m.DrawnPx),
                P("drawnOffsetM", m.DrawnOffsetM), P("peakLiveCards", m.PeakCards), P("peakLiveHoles", m.PeakHoles), P("horizonProcs", m.Procs), P("horizonSets", m.Sets),
                P("downAfterTick", m.Down), P("fadeS", m.FadeS), P("afterFadeChangedPx", m.AfterPx), P("nonEmpty", all.Changed > 200),
                PR("binder", BinderJson(binder)), PR(m.Ground, HalfJson(all, ppm, rad, float.NaN)));
            AddFrame(n, file, label, look, lit, W, H, Roi(O, 16f), json, FloorMarks());
            Debug.Log("[DD-TEST] wp104b4.ad " + file + " up=" + m.Up + " angle=" + F(m.AngleDeg) + " coreOff=" + F(m.CoreOffsetM) + " ringOff=" + F(m.RingOffsetM)
                      + " drawnOff=" + F(m.DrawnOffsetM) + " fade=" + F(m.FadeS) + " changed=" + all.Changed);
            Assert.IsTrue(m.Up, "kare " + n + ": Olay Ufku açılmadı (HorizonActive)");
            Assert.AreEqual(HorizonOrbitM, m.OrbitM, .05f, "kare " + n + ": kuyu kahramanın etrafında dönmüyor (merkez " + XZ(centre)
                                                           + " O'dan) — başka bir TreeSystemsDriver anchor'ı değiştirdi (b43-r1)");
            Assert.IsNotNull(core, "kare " + n + ": Olay Ufku seti yok (TreeVfxBinder.HorizonCore)");
            Assert.Greater(all.Changed, 200, "olay ufku " + groundName + ": görsel yok (boş kare)");
        }

        /// <summary>B4-3: the frame's changed pixels (raw lit vs dark, a channel moved ≥ 0.10 — Region's rule) and their centroid (px).</summary>
        private static Vector2 ChangedCentroid(Color32[] lit, Color32[] dark, out int n)
        {
            double sx = 0, sy = 0;
            n = 0;
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    if (MaxAbs(lit[row + x], dark[row + x]) < Moved) continue;
                    n++;
                    sx += x + .5;
                    sy += y + .5;
                }
            }

            return n > 0 ? new Vector2((float)(sx / n), (float)(sy / n)) : new Vector2(float.NaN, float.NaN);
        }

        private static float XzDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>
        /// B4-3: 37–38 are S-04's driver. b43-r1 (12.10): an earlier fixture's Run scene was still loaded (header.world: Run, 2 scenes). The
        /// first TreeSystemsRunner.Tick ran BeforeTick (TreeZombieBinding.Sync), which bound that scene's live zombies, and
        /// TreeStatusSystem.Register made a TreeSystemsDriver (EnsureExists) after the start-of-test park. From the next frame its Update
        /// re-anchored the well on that scene's player (EnemyTargeting.Player, near the world origin) and ticked it once more a frame: frame
        /// 37's well orbited ≈ 7.9 km from the rig (centre −5599.9 / 5600.0 from O, 135°, the cards 0.0625 m = one extra tick behind,
        /// changed 0 px, never down). Now BeforeTick is off (the EditMode way, TreeSystemsTests: no production body binding), every driver
        /// is parked at the start and after each tick (before its first Update), and the tear-down gives the world back.
        /// </summary>
        private void TakeTreeSystems()
        {
            _usedHorizon = true;
            _savedBeforeTick = TreeSystemsRunner.BeforeTick;
            TreeSystemsRunner.BeforeTick = null;
            ParkTreeDriver(true);
        }

        private void ParkTreeDriver(bool atStart)
        {
            TreeSystemsDriver driver = Object.FindObjectOfType<TreeSystemsDriver>();
            if (driver == null || !driver.enabled) return;
            driver.enabled = false;
            if (_parkedDriver != null) return;
            _parkedDriver = driver;
            _parkedDriverWasThere = atStart;
        }

        /// <summary>B4-3: one S-04 frame — TreeSystemsRunner.Tick on the game clock, then any driver it made parked before its first Update.</summary>
        private void HorizonTick()
        {
            TreeSystemsRunner.Tick(Time.deltaTime > 0f ? Time.deltaTime : 1f / 60f, Time.time);
            ParkTreeDriver(false);
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
        /// One tracer capture's renders, the first on a Unity frame of its own (AD-B4 M2: DrawTd queues Graphics.DrawMeshInstanced for
        /// EVERY render of the Unity frame, and the previous capture queued its halo pass after its own yield — b4-r2's frames 02 / 07
        /// rendered 01's / 06's halos as their core): ground only (raw + post) and the bare ground-off render → layer 0 alone on black
        /// (A5's bright core) → every layer + ink (raw, post, HDR luminance); on the next Unity frame the bare ground-off render again,
        /// then the halos alone on black. The checks: darkLeakPx (the ground-only render is the two ground colours), coreLeakPx /
        /// haloLeakPx (each bare ground-off render is black: nothing queued earlier in its Unity frame or left from the previous one,
        /// nothing a loaded world draws over black — b4-r3), groundVeilLevels (the ground is the bare ground).
        /// </summary>
        private IEnumerator Shoot(TracerRibbonRenderer tracer, Shots s)
        {
            yield return null;
            s.Dark = Grab(false, true);
            s.DarkLook = _lookPost ? Grab(true, true) : s.Dark;
            s.Black = Grab(false, false);
            DrawTd(tracer, 0);
            s.Core = Grab(false, false);
            DrawTd(tracer, 1);
            DrawTd(tracer, 2);
            s.Ink = InvokeInk(tracer);
            s.Lit = Grab(false, true);
            s.Look = _lookPost ? Grab(true, true) : s.Lit;
            s.Hdr = GrabHdrLum();
            yield return null;   // a new frame: the queued draws are gone (haloLeakPx proves it)
            s.HaloBlack = Grab(false, false);
            s.HaloInstances = DrawTd(tracer, 2);
            s.Halo = Grab(false, false);
            s.DarkLeak = DarkLeak(s.Dark);
            s.CoreLeak = BlackLeak(s.Black, out int coreMax);
            s.HaloLeak = BlackLeak(s.HaloBlack, out int haloMax);
            s.BlackMax = Mathf.Max(coreMax, haloMax);
            s.Veil = GroundVeil(s.Dark);
        }

        private static void Leaks(int n, Shots s)
        {
            s_darkLeak[n] = s.DarkLeak;
            s_coreLeak[n] = s.CoreLeak;
            s_haloLeak[n] = s.HaloLeak;
            s_veil[n] = s.Veil;
        }

        /// <summary>One tracer frame (<see cref="Shoot"/>): measures each round, writes the PNG.</summary>
        private IEnumerator CaptureTracers(TracerRibbonRenderer tracer, int n, string file, string label, List<Lane> lanes)
        {
            var s = new Shots();
            yield return Shoot(tracer, s);
            Leaks(n, s);
            int ink = s.Ink, leak = s.DarkLeak;
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
                P("flashScale", GameFeelSettings.FlashScale), P("inkDrawn", ink), P("changedPx", all.Changed), P("darkLeakPx", leak), P("coreLeakPx", s.CoreLeak),
                P("haloLeakPx", s.HaloLeak), P("blackMaxLevel", s.BlackMax), P("groundVeilLevels", s.Veil), P("hdr", s.Hdr != null),
                P("nonEmpty", all.Changed > 1000), PR("lanes", Arr(lanesJson)));
            AddFrame(n, file, label, s.Look, s.Lit, W, H, new RectInt(0, 0, W, H), json, marks);
            Debug.Log("[DD-TEST] wp104b4.ad " + file + " changed=" + all.Changed + " lanes=" + lanes.Count + " ink=" + ink + " darkLeak=" + leak
                      + " coreLeak=" + s.CoreLeak + " haloLeak=" + s.HaloLeak + " veil=" + F(s.Veil));
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
        /// on black, brightest channel ≥ 254: A5), the quad's faint edge (a channel risen ≥ 8), the halo alone on black (risen ≥ 0.10) —
        /// each against the bare ground-off render of its own Unity frame (black when clean: then the plain thresholds) — the visible streak
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
                int b = ColumnOver(s.Core, s.Black, c, across, 254, true), h = ColumnOver(s.Halo, s.HaloBlack, c, across, Moved, false);
                if (i == 0)
                {
                    bright = b;
                    quad = ColumnOver(s.Core, s.Black, c, across, 8, false);
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
                    if (Over(s.Core[i], s.Black != null ? s.Black[i] : Black32, 8, false)) continue;   // the core quad (risen ≥ 8 on black): the glow is the rest
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

        /// <summary>coreLeakPx / haloLeakPx: pixels of a bare ground-off render (nothing of the capture queued in its Unity frame) whose
        /// brightest channel is ≥ 8 — the lowest threshold a ground-off measure uses (coreQuadPx). 0 = the core / halo measure saw only its
        /// own frame's draws. b4-r3 would read the whole frame here: a loaded world's volumetric in-scatter (≥ 8 everywhere, ≥ 26 east).
        /// <paramref name="max"/>: the brightest channel seen (blackMaxLevel).</summary>
        private static int BlackLeak(Color32[] bare, out int max)
        {
            int n = 0;
            max = 0;
            for (int i = 0; i < bare.Length; i++)
            {
                int m = MaxC(bare[i]);
                if (m > max) max = m;
                if (m >= 8) n++;
            }

            return n;
        }

        /// <summary>groundVeilLevels: how far (8-bit levels, the worst channel) the four 5 × 5 corner patches of a ground-only render sit
        /// from the bare ground — URP/Unlit sRGB 0.75 → 191 (light stone), 0.20 → 51 (wet slate), each corner its own half. ≤ 1 on a clean
        /// frame (b4-r4: 191 / 51 exactly); b4-r3's frames carried a loaded world's in-scatter (wet slate 54–60, light stone 189–190).</summary>
        private float GroundVeil(Color32[] ground)
        {
            float worst = 0f;
            for (int corner = 0; corner < 4; corner++)
            {
                bool west = (corner & 1) == 0;
                int cx = west ? 10 : W - 11, cy = (corner & 2) == 0 ? 10 : H - 11;
                float expect = 255f * (_floor == Floor.Light || (_floor == Floor.Split && west) ? LightGrey : SlateGrey);
                float r = 0f, g = 0f, b = 0f;
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        Color32 p = ground[(cy + dy) * W + cx + dx];
                        r += p.r;
                        g += p.g;
                        b += p.b;
                    }
                }

                worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(r / 25f - expect), Mathf.Max(Mathf.Abs(g / 25f - expect), Mathf.Abs(b / 25f - expect))));
            }

            return worst;
        }

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

        // ------------------------------------------------------------------ 35 · 36 B4-2 B9 seeker trail

        /// <summary>
        /// S-02's seeker path and its lock (d97fd189), read by reflection: this file builds before and after F, and without them the B9
        /// frames are "missing". <see cref="Lock"/> does to a round what ProjectileSystem.LockSeek does to one that found a body — the
        /// rig has no zombie, so the lock is the capture's: SeekTrail on, the path started where the round is, the next point one
        /// PathStepSeconds of flight later. The kept points themselves are ProjectileSystem's own (StepOne pushes them).
        /// </summary>
        private sealed class SeekApi
        {
            public MethodInfo TryGetPath, PathPush;
            public FieldInfo SeekTrail, Live, PathNext, PathCount;
            public float PathStep = float.NaN, Turn = 720f;
            public int PathPoints;
            public string Missing;

            public static SeekApi Resolve()
            {
                const BindingFlags pub = BindingFlags.Public | BindingFlags.Static, priv = BindingFlags.NonPublic | BindingFlags.Static;
                Type t = typeof(ProjectileSystem);
                var a = new SeekApi
                {
                    TryGetPath = t.GetMethod("TryGetPath", pub, null, new[] { typeof(int), typeof(Vector3[]) }, null),
                    PathPush = t.GetMethod("PathPush", priv, null, new[] { typeof(int), typeof(Vector3) }, null),
                    SeekTrail = typeof(ProjectileState).GetField("SeekTrail", BindingFlags.Public | BindingFlags.Instance),
                    Live = t.GetField("s_live", priv),
                    PathNext = t.GetField("s_pathNext", priv),
                    PathCount = t.GetField("s_pathCount", priv)
                };
                FieldInfo step = t.GetField("PathStepSeconds", pub), points = t.GetField("PathPoints", pub), turn = t.GetField("SeekTurnDegreesPerSecond", pub);
                if (step != null && step.FieldType == typeof(float)) a.PathStep = (float)step.GetValue(null);
                if (points != null && points.FieldType == typeof(int)) a.PathPoints = (int)points.GetValue(null);
                if (turn != null && turn.FieldType == typeof(float)) a.Turn = (float)turn.GetValue(null);
                var missing = new List<string>();
                if (a.TryGetPath == null || a.TryGetPath.ReturnType != typeof(int)) missing.Add("ProjectileSystem.TryGetPath");
                if (a.SeekTrail == null || a.SeekTrail.FieldType != typeof(bool)) missing.Add("ProjectileState.SeekTrail");
                if (a.PathPush == null) missing.Add("ProjectileSystem.PathPush");
                if (a.Live == null || a.Live.FieldType != typeof(ProjectileState[])) missing.Add("ProjectileSystem.s_live");
                if (a.PathNext == null || a.PathNext.FieldType != typeof(float[])) missing.Add("ProjectileSystem.s_pathNext");
                if (float.IsNaN(a.PathStep) || a.PathPoints <= 0) missing.Add("ProjectileSystem.PathStepSeconds / PathPoints");
                a.Missing = missing.Count > 0 ? string.Join(", ", missing) : null;
                return a;
            }

            public ProjectileState[] States => (ProjectileState[])Live.GetValue(null);

            public void Lock(int slot)
            {
                ProjectileState[] live = States;
                object boxed = live[slot];
                SeekTrail.SetValue(boxed, true);
                live[slot] = (ProjectileState)boxed;
                if (PathCount != null && PathCount.GetValue(null) is int[] counts) counts[slot] = 0;
                PathPush.Invoke(null, new object[] { slot, live[slot].Position });
                ((float[])PathNext.GetValue(null))[slot] = PathStep;
            }

            public int Path(int generation, Vector3[] into) => (int)TryGetPath.Invoke(null, new object[] { generation, into });
        }

        /// <summary>
        /// 35 · 36 — B9 (B4-2) on each whole ground: Shot V AR extra-volley TD records (VolleyIndex 1, sh_cap's seeking shot: a body
        /// streak in TD; sh_cap is the Shot branch's tier-V capstone), their streak laid along the kept path up to 10 m (S-07 12.10
        /// decision (a)):
        /// <list type="bullet">
        /// <item>KIVRILAN — the designed curve (C-00 / S-07 12.10): 3 m straight, a 30° turn, 3 m — a synthetic record whose kept path
        /// F's <c>SetTdPathForTests</c> lays (reflection), its head on the path's newest point (StopTdProjectile, as the tracer frames);</item>
        /// <item>DUZ — the same record kind on a straight 6 m path through the same draw path: the width reference and the bend
        /// measure's control;</item>
        /// <item>GERCEK (optional) — B9's whole chain: a real AR round at the weapon's speed, locked the capture's way
        /// (<see cref="SeekApi.Lock"/>) and turned before every fixed step as ProjectileSystem.Steer turns a round toward a body 20 m off
        /// at the 25° cone edge until its next step would leave the frame or come within 1.5 m of it; S-02 keeps its path, F's TickTd
        /// reads it. Its steps run first: TickTd ages every record, and the designed ones are stopped streaks.</item>
        /// </list>
        /// The game clock is then held (timeScale 0) while the two grounds are captured: ProjectileDriver cannot move the real round.
        /// </summary>
        private IEnumerator SeekFrames(TracerRibbonRenderer tracer)
        {
            tracer.Clear();
            ProjectileSystem.Clear();
            TreeStats stats = TreeStats.Neutral;
            stats.SetBranchTier(TreeBranch.Shot, 5);
            TreeRuntime.SetTestStats(stats);
            s_seeks.Clear();
            s_seekMissing = s_realMissing = s_seekNote = null;
            yield return null;
            float speed = WeaponSpeed(ClassicWeaponId.AssaultRifle, out WeaponCaliber caliber);
            var lanes = new List<SeekLane>();
            SeekApi api = SeekApi.Resolve();
            if (api.Missing != null) s_realMissing = api.Missing;
            else
            {
                SeekLane real = RealSeeker(tracer, api, speed);
                if (real != null) lanes.Add(real);
            }

            MethodInfo setPath = typeof(TracerRibbonRenderer).GetMethod("SetTdPathForTests", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(int), typeof(Vector3[]), typeof(int) }, null);
            if (setPath == null)
            {
                s_seekMissing = "TracerRibbonRenderer.SetTdPathForTests (F)";
            }
            else
            {
                lanes.Insert(0, Designed(tracer, setPath, "KIVRILAN", "KIVRILAN: 3 M + 30 DERECE + 3 M", O + new Vector3(-9f, LaneY, 3.8f), -30f, speed, caliber));
                lanes.Insert(1, Designed(tracer, setPath, "DUZ", "DUZ: AYNI KAYIT TURU, 6 M", O + new Vector3(-9f, LaneY, .4f), 0f, speed, caliber));
            }

            Debug.Log("[DD-TEST] wp104b4.ad B9 lanes=" + lanes.Count + " designed=" + (s_seekMissing ?? "ok") + " real=" + (s_realMissing ?? "ok"));
            if (lanes.Count == 0) yield break;
            float previousScale = Time.timeScale;
            Time.timeScale = 0f;   // ProjectileDriver ticks on Time.deltaTime: the real round stays where its kept path ends
            SetFloor(Floor.Light);
            yield return CaptureSeek(tracer, 35, "35-iz-b9-kivrilan-seeker-acik-tas.png", "35 B4-2 B9 KIVRILAN SEEKER IZI (SH_CAP EK ATIS, ATIS V) / ACIK TAS", lanes);
            SetFloor(Floor.Slate);
            yield return CaptureSeek(tracer, 36, "36-iz-b9-kivrilan-seeker-islak-arduvaz.png", "36 B4-2 B9 KIVRILAN SEEKER IZI (SH_CAP EK ATIS, ATIS V) / ISLAK ARDUVAZ", lanes);
            Time.timeScale = previousScale;
            ProjectileSystem.Clear();
            tracer.Clear();
        }

        /// <summary>GERCEK (see <see cref="SeekFrames"/>): null, with s_realMissing, when the round cannot fly its path.</summary>
        private SeekLane RealSeeker(TracerRibbonRenderer tracer, SeekApi api, float speed)
        {
            float dt = ProjectileSystem.FixedStep, cone = SeekConeDegrees * Mathf.Deg2Rad;
            Vector3 muzzle = O + new Vector3(-9.3f, LaneY, -4.3f);
            var lane = new SeekLane { Key = "GERCEK", Label = "GERCEK SEEKER: AR " + Mathf.RoundToInt(speed) + " M/S, GOVDE 20 M / 25 DERECE" };
            lane.Body = muzzle + new Vector3(Mathf.Cos(cone), 0f, Mathf.Sin(cone)) * SeekBodyMetres;
            int total = SeekSteps(muzzle, lane.Body, speed, api.Turn, dt);
            if (total < 1)
            {
                s_realMissing = "GERCEK: ilk adımda kareden çıkıyor (" + F(speed) + " m/s)";
                return null;
            }

            ProjectileState[] live = api.States;
            lane.Slot = SpawnVolley(muzzle, speed, out lane.Generation);
            api.Lock(lane.Slot);
            lane.Flight.Add(muzzle);
            for (int k = 0; k < total; k++)
            {
                SteerToward(live, lane.Slot, lane.Body, api.Turn, dt);
                ProjectileSystem.Step(dt);
                tracer.TickTd(dt);
                lane.Flight.Add(live[lane.Slot].Position);
            }

            if (!live[lane.Slot].Alive || live[lane.Slot].Generation != lane.Generation)
            {
                s_realMissing = "GERCEK: mermi uçuşta bitti";
                ProjectileSystem.Clear();
                return null;
            }

            lane.Path = new Vector3[api.PathPoints + 1];
            lane.PathCount = api.Path(lane.Generation, lane.Path);
            s_seekNote = "GERCEK: AR " + F(speed) + " m/s, sabit adım 1/" + Mathf.RoundToInt(1f / dt) + " s, " + total + " adım (" + F(total * dt) + " s); S-02 yolu her "
                         + F(api.PathStep) + " s'de bir nokta = " + F(speed * api.PathStep) + " m uçuş; dönüş " + F(api.Turn) + "°/s (R = v / ω = "
                         + F(speed / Mathf.Max(1e-3f, api.Turn * Mathf.Deg2Rad)) + " m)";
            return lane;
        }

        /// <summary>KIVRILAN / DUZ: a synthetic Shot V AR extra-volley TD record (VolleyIndex 1, as S-02 spawns sh_cap's shots) from
        /// <paramref name="start"/>; its kept path — 3 m along +x, then 3 m turned by <paramref name="turnDegrees"/> — laid by F's
        /// SetTdPathForTests; the head on the path's newest point (StopTdProjectile).</summary>
        private SeekLane Designed(TracerRibbonRenderer tracer, MethodInfo setPath, string key, string label, Vector3 start, float turnDegrees, float speed,
            WeaponCaliber caliber)
        {
            float a = turnDegrees * Mathf.Deg2Rad;
            Vector3 p1 = start + Vector3.right * 3f, p2 = p1 + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3f;
            var lane = new SeekLane { Key = key, Label = label, Designed = true, Generation = _nextId++, Path = new[] { start, p1, p2 }, PathCount = 3, Body = p2 };
            lane.Flight.AddRange(lane.Path);
            var info = new ProjectileSpawnInfo(lane.Generation, int.MaxValue, ClassicWeaponId.AssaultRifle, ProjectileKind.Bullet, caliber, start, Vector3.right, speed, 0, 1, 1f);
            Assert.IsTrue(tracer.RecordTdProjectile(info), "B9 TD kaydı alınmadı: " + key);
            lane.Laid = setPath.Invoke(tracer, new object[] { lane.Generation, lane.Path, lane.PathCount }) is bool laid && laid;
            tracer.StopTdProjectile(lane.Generation, p2);
            return lane;
        }

        /// <summary>The fixed steps SEEK flies before its next one would leave the frame (x &gt; 9 m, |z| &gt; 5 m from O at 1 m high) or
        /// come within 1.5 m of its body (in the game it hits there): ProjectileSystem's step (v · dt along the turned velocity, no gravity)
        /// with Steer's turn, without a projectile.</summary>
        private static int SeekSteps(Vector3 from, Vector3 body, float speed, float turnDegrees, float dt)
        {
            Vector3 pos = from, dir = Vector3.right;
            for (int k = 0; k < 64; k++)
            {
                Vector3 to = body - pos;
                if (to.sqrMagnitude > 1e-4f) dir = Vector3.RotateTowards(dir, to.normalized, turnDegrees * Mathf.Deg2Rad * dt, 0f);
                Vector3 next = pos + dir * (speed * dt), local = next - O;
                if (local.x > 9f || Mathf.Abs(local.z) > 5f || (body - next).sqrMagnitude < 1.5f * 1.5f) return k;
                pos = next;
            }

            return 64;
        }

        /// <summary>A real AR extra-volley round (VolleyIndex 1: sh_cap's seeking shot, a body streak in TD) from <paramref name="muzzle"/>
        /// along +x: its pool slot, and its generation (the TD record id).</summary>
        private static int SpawnVolley(Vector3 muzzle, float speed, out int generation)
        {
            var shot = new ShotRequest { Origin = muzzle, Direction = Vector3.right, Damage = 1f, Range = 200f, PierceDamageFalloff = 1f, VolleyIndex = 1 };
            var launch = new ProjectileLaunch
            {
                WeaponId = ClassicWeaponId.AssaultRifle, Kind = ProjectileKind.Bullet, Speed = speed, MaxRange = 200f, MaxLifetime = 10f,
                FalloffStart = 200f, FalloffEnd = 200f, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
            };
            int slot = ProjectileSystem.Spawn(in shot, in launch);
            Assert.GreaterOrEqual(slot, 0, "ProjectileSystem.Spawn (B9)");
            generation = ProjectileSystem.Get(ProjectileSystem.LiveCount - 1).Generation;
            return slot;
        }

        /// <summary>ProjectileSystem.Steer's turn toward <paramref name="body"/> (before the step: the swept segment is the turned one).</summary>
        private static void SteerToward(ProjectileState[] live, int slot, Vector3 body, float degreesPerSecond, float dt)
        {
            ref ProjectileState p = ref live[slot];
            Vector3 to = body - p.Position;
            float speed = p.Velocity.magnitude;
            if (to.sqrMagnitude < 1e-4f || speed < 1e-3f) return;
            p.Velocity = Vector3.RotateTowards(p.Velocity / speed, to.normalized, degreesPerSecond * Mathf.Deg2Rad * dt, 0f) * speed;
        }

        /// <summary>One B9 frame (<see cref="Shoot"/>: the same renders and leak checks as a tracer frame), every lane measured, F's
        /// readouts beside them (reflection): LiveTdSeekPaths, the TdBentInstances the frame's draws added, and the halo frame's layer-2
        /// instances laid along a path (DD/VFX Ribbon <c>_Span.w</c> &lt; 0).</summary>
        private IEnumerator CaptureSeek(TracerRibbonRenderer tracer, int n, string file, string label, List<SeekLane> lanes)
        {
            int bentBefore = IntProperty(tracer, "TdBentInstances");
            var s = new Shots();
            yield return Shoot(tracer, s);
            Leaks(n, s);
            int bentAfter = IntProperty(tracer, "TdBentInstances"), seekPaths = IntProperty(tracer, "LiveTdSeekPaths"), bentSpans = BentSpans(tracer, s.HaloInstances);
            s_seeks.RemoveAll(m => m.Frame == n);
            float ppm = PxPerMetre(LaneY);
            var lanesJson = new List<string>();
            var marks = new List<Mark>();
            foreach (SeekLane lane in lanes)
            {
                lanesJson.Add(SeekJson(tracer, n, lane, s, ppm));
                // the designed lanes' labels above their start (KIVRILAN turns down), the real one's under it (the frame title holds the
                // top-left); the body the real round turns to as a "+" with its name below
                marks.Add(MarkAt(lane.Flight[0] + new Vector3(-.05f, 0f, lane.Designed ? .45f : -.4f), lane.Label));
                if (lane.Designed) continue;
                marks.Add(MarkAt(lane.Body + new Vector3(-.05f, 0f, 0f), "+"));
                marks.Add(MarkAt(lane.Body + new Vector3(-1.4f, 0f, -.4f), "SEEK HEDEFI"));
            }

            marks.AddRange(FloorMarks());
            Stats all = Region(s.Lit, s.Dark, 0, W - 1, 0, H - 1);
            string json = Obj(P("n", n), P("file", file), P("label", label), P("kind", "seekTrail"), P("ground", FloorName()), P("flashScale", GameFeelSettings.FlashScale),
                P("inkDrawn", s.Ink), P("changedPx", all.Changed), P("darkLeakPx", s.DarkLeak), P("coreLeakPx", s.CoreLeak), P("haloLeakPx", s.HaloLeak),
                P("blackMaxLevel", s.BlackMax), P("groundVeilLevels", s.Veil), P("hdr", s.Hdr != null),
                P("liveTdSeekPaths", seekPaths), P("tdBentInstancesDelta", bentBefore >= 0 && bentAfter >= 0 ? bentAfter - bentBefore : -1),
                P("haloFrameLayer2Instances", s.HaloInstances), P("haloFrameBentSpans", bentSpans),
                P("designed", "KIVRILAN / DUZ: synthetic Shot V AR extra-volley records (VolleyIndex 1), kept path laid by TracerRibbonRenderer.SetTdPathForTests (F), "
                              + "head = the path's newest point (StopTdProjectile); streak length = min(kept path, 10 m) (S-07 12.10 decision (a))"),
                P("designedMissing", s_seekMissing), P("real", s_seekNote),
                P("realLock", "GERCEK: the rig has no zombie - SeekTrail set and the path started as ProjectileSystem.LockSeek does (reflection), turned before every fixed "
                              + "step as Steer turns a round; ProjectileSystem kept the path itself; the game clock held during the capture"),
                P("realMissing", s_realMissing), P("nonEmpty", all.Changed > 200), PR("lanes", Arr(lanesJson)));
            AddFrame(n, file, label, s.Look, s.Lit, W, H, new RectInt(0, 0, W, H), json, marks);
            Debug.Log("[DD-TEST] wp104b4.ad " + file + " changed=" + all.Changed + " darkLeak=" + s.DarkLeak + " coreLeak=" + s.CoreLeak + " haloLeak=" + s.HaloLeak
                      + " veil=" + F(s.Veil) + " seekPaths=" + seekPaths + " bentSpans=" + bentSpans + " " + SeekLog(n));
            Assert.Greater(all.Changed, 200, file + ": iz çizilmedi (boş kare)");
        }

        /// <summary>A public int property by name (F's B9 readouts), −1 when there is none.</summary>
        private static int IntProperty(object target, string name)
        {
            PropertyInfo p = target != null ? target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) : null;
            return p != null && p.PropertyType == typeof(int) && p.GetIndexParameters().Length == 0 ? (int)p.GetValue(target) : -1;
        }

        /// <summary>Of the first <paramref name="n"/> instances DrawTd last wrote, those laid along a kept path (DD/VFX Ribbon's
        /// <c>_Span.w</c> &lt; 0: −the bent streak's length); −1 without the array.</summary>
        private static int BentSpans(TracerRibbonRenderer tracer, int n)
        {
            FieldInfo f = typeof(TracerRibbonRenderer).GetField("_span", Private);
            if (f == null || !(f.GetValue(tracer) is Vector4[] span)) return -1;
            int bent = 0;
            for (int i = 0; i < n && i < span.Length; i++)
            {
                if (span[i].w < 0f) bent++;
            }

            return bent;
        }

        /// <summary>
        /// One B9 lane: the drawn trail's pixels (<see cref="TrailPixels"/>) and their bend (<see cref="CentreBend"/>), the kept path's and the
        /// flight's own bend (world, <see cref="BendOf"/>), and across the flight's direction 0.25 / 0.5 m behind the head the visible width
        /// (lit vs dark), the layer-2 width (halo or body, the halo-only render over its bare one) and layer 0's bright core and quad.
        /// </summary>
        private string SeekJson(TracerRibbonRenderer tracer, int n, SeekLane lane, Shots s, float ppm)
        {
            var flight = new List<Vector2>(lane.Flight.Count);
            foreach (Vector3 p in lane.Flight) flight.Add(Px(p));
            var kept = new List<Vector2>(lane.PathCount);
            for (int i = 0; i < lane.PathCount; i++) kept.Add(Px(lane.Path[i]));
            Vector3 headW = lane.Flight[lane.Flight.Count - 1];
            Vector3 dirHead = (headW - lane.Flight[lane.Flight.Count - 2]).normalized;
            // what reads (a channel moved ≥ 0.10, WP-101's visibility) and what is drawn (≥ 8 levels: the faint end of the tail too)
            List<Vector2> trail = TrailPixels(s, flight, kept, SeekBandMetres * ppm, Moved), faint = TrailPixels(s, flight, kept, SeekBandMetres * ppm, 8);
            float bendPx = CentreBend(trail, Px(headW), out float lengthPx), faintPx = CentreBend(faint, Px(headW), out float faintLengthPx);
            int across = Mathf.CeilToInt(ppm * .6f);
            var vis = new int[2];
            var layer2 = new int[2];
            int bright = 0, quad = 0;
            float[] behind = { .25f, .5f };
            for (int b = 0; b < behind.Length; b++)
            {
                Vector3 at = Behind(lane.Flight, behind[b], out Vector3 dir);
                Vector2 c = Px(at), d = (Px(at + dir * .1f) - c).normalized, nrm = new Vector2(-d.y, d.x);
                vis[b] = Across(s.Lit, s.Dark, c, nrm, across, Moved, 0);
                layer2[b] = Across(s.Halo, s.HaloBlack, c, nrm, across, Moved, 1);
                if (b != 0) continue;
                bright = Across(s.Core, s.Black, c, nrm, across, 254, 2);
                quad = Across(s.Core, s.Black, c, nrm, across, 8, 1);
            }

            object tracerFlag = RecordValue(tracer, lane.Generation, "Tracer"), haloStep = RecordValue(tracer, lane.Generation, "HaloPx");
            object look = RecordValue(tracer, lane.Generation, "Look"), seekPoints = RecordValue(tracer, lane.Generation, "SeekPoints");
            object trailK = RecordValue(tracer, lane.Generation, "TrailScale"), coreM = RecordValue(tracer, lane.Generation, "Core"), haloM = RecordValue(tracer, lane.Generation, "Halo");
            var m = new SeekM
            {
                Frame = n, Key = lane.Key, Ground = HalfName('C'), Designed = lane.Designed, PathPoints = lane.PathCount, TrailPx = trail.Count,
                PathBendM = BendOf(lane.Path, lane.PathCount), FlightBendM = BendOf(lane.Flight.ToArray(), lane.Flight.Count),
                DrawnBendM = float.IsNaN(bendPx) ? float.NaN : bendPx / ppm, DrawnLengthM = lengthPx / ppm,
                DrawnBendFaintM = float.IsNaN(faintPx) ? float.NaN : faintPx / ppm, DrawnLengthFaintM = faintLengthPx / ppm,
                Visible025 = vis[0], Visible05 = vis[1], Layer2025 = layer2[0], Layer205 = layer2[1], Bright025 = bright, Quad025 = quad,
                Tracer = tracerFlag is bool tb ? tb : (bool?)null, RecordHaloPx = haloStep is float hs ? hs : float.NaN, RecordSeekPoints = seekPoints is int sp ? sp : -1
            };
            s_seeks.Add(m);
            var path = new List<string>(lane.PathCount);
            for (int i = 0; i < lane.PathCount; i++) path.Add(XZ(lane.Path[i]));
            return Obj(P("key", lane.Key), P("label", lane.Label), P("ground", m.Ground), P("designed", lane.Designed), PB("pathLaid", lane.Designed ? lane.Laid : (bool?)null),
                P("id", lane.Generation), P("volleyIndex", 1), P("steps", lane.Flight.Count - 1),
                P("recordLook", look is int lk ? lk : -1), PR("recordTracer", tracerFlag is bool tf ? (tf ? "true" : "false") : "null"), P("recordHaloPx", m.RecordHaloPx),
                P("recordTrailScale", trailK is float tk ? tk : float.NaN), P("recordCoreM", coreM is float cm ? cm : float.NaN), P("recordHaloM", haloM is float hm ? hm : float.NaN),
                P("recordSeekPoints", m.RecordSeekPoints), P("pathPoints", lane.PathCount), PR("pathXZ", Arr(path)), PR("headXZ", XZ(headW)),
                P("headingDeg", Mathf.Atan2(dirHead.z, dirHead.x) * Mathf.Rad2Deg), P("pathBendM", m.PathBendM), P("flightBendM", m.FlightBendM),
                P("drawnBendM", m.DrawnBendM), P("drawnLengthM", m.DrawnLengthM), P("trailPx", m.TrailPx), P("drawnBendFaintM", m.DrawnBendFaintM),
                P("drawnLengthFaintM", m.DrawnLengthFaintM), P("trailPxFaint", faint.Count),
                PR("widthsPx", Obj(P("visible025", m.Visible025), P("visible05", m.Visible05), P("layer2025", m.Layer2025), P("layer205", m.Layer205),
                    P("brightCore025", m.Bright025), P("coreQuad025", m.Quad025))));
        }

        private static string XZ(Vector3 w) => "[" + F(w.x - O.x) + ", " + F(w.z - O.z) + "]";

        private static string SeekLog(int n)
        {
            var sb = new StringBuilder();
            foreach (SeekM m in s_seeks)
            {
                if (m.Frame != n) continue;
                sb.Append(m.Key).Append(" path=").Append(m.PathPoints).Append(" bend=").Append(F(m.DrawnBendM)).Append("m (kept ").Append(F(m.PathBendM)).Append(", flight ")
                    .Append(F(m.FlightBendM)).Append(") len=").Append(F(m.DrawnLengthM)).Append("m vis=").Append(m.Visible025).Append(" l2=").Append(m.Layer2025).Append(' ');
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>B9: the changed pixels (lit vs dark, a channel moved ≥ <paramref name="threshold"/> levels) within <paramref name="band"/>
        /// px of the round's flight or of its kept path (whichever the streak is laid along), over their screen box.</summary>
        private static List<Vector2> TrailPixels(Shots s, List<Vector2> flight, List<Vector2> kept, float band, int threshold)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (List<Vector2> line in new[] { flight, kept })
            {
                foreach (Vector2 p in line)
                {
                    x0 = Mathf.Min(x0, p.x);
                    y0 = Mathf.Min(y0, p.y);
                    x1 = Mathf.Max(x1, p.x);
                    y1 = Mathf.Max(y1, p.y);
                }
            }

            var px = new List<Vector2>();
            if (x0 > x1) return px;
            int xa = Mathf.Clamp(Mathf.FloorToInt(x0 - band), 0, W - 1), xb = Mathf.Clamp(Mathf.CeilToInt(x1 + band), 0, W - 1);
            int ya = Mathf.Clamp(Mathf.FloorToInt(y0 - band), 0, H - 1), yb = Mathf.Clamp(Mathf.CeilToInt(y1 + band), 0, H - 1);
            float band2 = band * band;
            for (int y = ya; y <= yb; y++)
            {
                for (int x = xa; x <= xb; x++)
                {
                    int i = y * W + x;
                    if (MaxAbs(s.Lit[i], s.Dark[i]) < threshold) continue;
                    var q = new Vector2(x, y);
                    if (Dist2(q, flight) > band2 && Dist2(q, kept) > band2) continue;
                    px.Add(q);
                }
            }

            return px;
        }

        /// <summary>Squared distance from <paramref name="q"/> to a polyline (to its one point when it has one; +∞ when empty).</summary>
        private static float Dist2(Vector2 q, List<Vector2> line)
        {
            if (line.Count == 0) return float.PositiveInfinity;
            if (line.Count == 1) return (q - line[0]).sqrMagnitude;
            float best = float.PositiveInfinity;
            for (int i = 1; i < line.Count; i++)
            {
                Vector2 a = line[i - 1], ab = line[i] - a;
                float l2 = ab.sqrMagnitude, t = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / l2) : 0f;
                best = Mathf.Min(best, (q - (a + ab * t)).sqrMagnitude);
            }

            return best;
        }

        /// <summary>
        /// B9's drawn bend (px): the trail's pixels binned (32) along the axis from the pixel farthest from the head to the head; each bin's
        /// centroid is the trail's centre line there; the largest distance of a centroid from the chord of the first and the last centroid.
        /// Width-free — a straight streak of any width reads ≈ 0 — and blind to where the streak ends. NaN without a trail.
        /// </summary>
        private static float CentreBend(List<Vector2> px, Vector2 head, out float lengthPx)
        {
            lengthPx = 0f;
            if (px.Count < 12) return float.NaN;
            Vector2 far = head;
            float best = -1f;
            foreach (Vector2 q in px)
            {
                float d = (q - head).sqrMagnitude;
                if (d <= best) continue;
                best = d;
                far = q;
            }

            Vector2 axis = head - far;
            float len = axis.magnitude;
            lengthPx = len;
            if (len < 4f) return float.NaN;
            axis /= len;
            const int bins = 32;
            var sx = new double[bins];
            var sy = new double[bins];
            var count = new int[bins];
            foreach (Vector2 q in px)
            {
                int k = Mathf.Clamp((int)(Vector2.Dot(q - far, axis) / len * bins), 0, bins - 1);
                sx[k] += q.x;
                sy[k] += q.y;
                count[k]++;
            }

            var centres = new List<Vector2>(bins);
            for (int k = 0; k < bins; k++)
            {
                if (count[k] >= 3) centres.Add(new Vector2((float)(sx[k] / count[k]), (float)(sy[k] / count[k])));
            }

            if (centres.Count < 3) return 0f;
            Vector2 a = centres[0], ab = centres[centres.Count - 1] - a;
            float l = ab.magnitude;
            if (l < 1e-3f) return 0f;
            float bend = 0f;
            foreach (Vector2 q in centres) bend = Mathf.Max(bend, Mathf.Abs((q.x - a.x) * ab.y - (q.y - a.y) * ab.x) / l);
            return bend;
        }

        /// <summary>World xz: the farthest of the inner <paramref name="count"/> points from the chord of the first and the last (m); 0 for
        /// two points, NaN for none.</summary>
        private static float BendOf(Vector3[] pts, int count)
        {
            if (pts == null || count < 1) return float.NaN;
            if (count < 3) return 0f;
            var a = new Vector2(pts[0].x, pts[0].z);
            Vector2 ab = new Vector2(pts[count - 1].x, pts[count - 1].z) - a;
            float l = ab.magnitude;
            if (l < 1e-4f) return 0f;
            float best = 0f;
            for (int i = 1; i < count - 1; i++)
            {
                Vector2 q = new Vector2(pts[i].x, pts[i].z) - a;
                best = Mathf.Max(best, Mathf.Abs(q.x * ab.y - q.y * ab.x) / l);
            }

            return best;
        }

        /// <summary>The point <paramref name="metres"/> back from the flight's end along it, and the flight's direction there.</summary>
        private static Vector3 Behind(List<Vector3> flight, float metres, out Vector3 dir)
        {
            int last = flight.Count - 1;
            dir = last > 0 ? (flight[last] - flight[last - 1]).normalized : Vector3.right;
            float left = metres;
            for (int i = last; i > 0; i--)
            {
                Vector3 seg = flight[i] - flight[i - 1];
                float len = seg.magnitude;
                if (len < 1e-5f) continue;
                dir = seg / len;
                if (left <= len) return flight[i] - dir * left;
                left -= len;
            }

            return flight[0];
        }

        /// <summary>Pixels along the screen line through <paramref name="c"/> along <paramref name="nrm"/> (±<paramref name="half"/> px)
        /// that pass: mode 0 a channel moved ≥ threshold against <paramref name="reference"/> (lit vs dark), 1 risen over it (alone on
        /// black vs its bare render), 2 saturated where the bare render is not (A5).</summary>
        private static int Across(Color32[] img, Color32[] reference, Vector2 c, Vector2 nrm, int half, int threshold, int mode)
        {
            int count = 0;
            for (int k = -half; k <= half; k++)
            {
                int x = Mathf.RoundToInt(c.x + nrm.x * k), y = Mathf.RoundToInt(c.y + nrm.y * k);
                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                int i = y * W + x;
                Color32 r = reference != null ? reference[i] : Black32;
                if (mode == 0 ? MaxAbs(img[i], r) >= threshold : Over(img[i], r, threshold, mode == 2)) count++;
            }

            return count;
        }

        private static int MaxAbs(Color32 a, Color32 b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

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
            float veil = GroundVeil(dark);
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
                s_veil[n] = veil;
                var pairs = new List<string>
                {
                    P("n", n), P("file", file), P("label", label), P("kind", info.Kind.ToString()), P("tier", info.Tier), P("radiusM", info.Radius),
                    P("seconds", info.Seconds), P("depth", info.Depth), P("element", info.Element.ToString()), P("tTargetS", moments[k]), P("tActualS", age),
                    P("played", played), P("preview", TreeVfxPreview.Active), P("clock", TreeVfxPreview.Active ? "unscaled (preview), real time" : "game time, captureDeltaTime 1/60"),
                    P("ground", FloorName()), P("groundVeilLevels", veil), P("nonEmpty", west.Changed + east.Changed > 200), PR("centrePx", "[" + F(centre.x) + ", " + F(centre.y) + "]"),
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
            float veil = GroundVeil(dark);
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
                s_veil[n] = veil;
                string json = Obj(P("n", n), P("file", file), P("label", label), P("kind", "aura"), P("level", level), P("tint", tint),
                    P("radiusM", TreeVfxLadder.AuraRadius(level)), P("alphaCap", TreeVfxLadder.AuraAlpha(level)), P("sparksPerSecond", TreeVfxLadder.AuraSparksPerSecond(level)),
                    P("tActualS", VfxClock.Now - t0), P("drawnLevel", aura.Level), P("ringVisible", aura.RingVisible), P("sparksVisible", aura.SparksVisible),
                    P("smokeVisible", aura.SmokeVisible), P("domeVisible", aura.DomeVisible), P("sparkAlphaCap", aura.SparkAlphaCap), P("ringScaleM", aura.RingScale),
                    P("ringRow", aura.RingRow != null ? aura.RingRow.Id : null), P("ground", FloorName()), P("groundVeilLevels", veil), P("nonEmpty", west.Changed + east.Changed > 200),
                    PR(HalfKey(true), HalfJson(west, ppm, rw, float.NaN)), PR(HalfKey(false), HalfJson(east, ppm, re, float.NaN)));
                AddFrame(n, file, label, look, lit, W, H, Roi(O, 8f), json, FloorMarks());
                Debug.Log("[DD-TEST] wp104b4.ad " + file + " ring=" + aura.RingVisible + " changed=" + (west.Changed + east.Changed));
                Assert.Greater(west.Changed + east.Changed, 200, "aura " + level + ": görsel yok (boş kare)");
            }

            // 33 · 34 — B4-2 S2: aura III on each whole ground (E: a light inner line, the ring's value step for the red-weak eye),
            // lumStepMean over the ring's changed pixels on both grounds; the dark frame at level 0 after binder.Clear (no entry pulse left)
            for (int g = 0; g < 2; g++)
            {
                Floor floorW = g == 0 ? Floor.Light : Floor.Slate;
                s.AuraLevel = 0;
                TreeVfxPreview.SetStats(s);
                binder.Clear();
                SetFloor(floorW);
                yield return null;
                yield return null;   // TreeAuraVfx hid its layers (level 0)
                Color32[] darkW = Grab(false, true);
                float veilW = GroundVeil(darkW);
                s.AuraLevel = 3;
                TreeVfxPreview.SetStats(s);
                float t0W = VfxClock.Now;
                yield return WaitPresentation(t0W, .4f);
                Color32[] litW = Grab(false, true);
                Color32[] lookW = _lookPost ? Grab(true, true) : litW;
                int nW = 33 + g;
                string keyW = g == 0 ? KeyStone : KeySlate, groundW = g == 0 ? "ACIK TAS" : "ISLAK ARDUVAZ";
                string tintW = "#" + ColorUtility.ToHtmlStringRGB(TreeVfxLadder.AuraTint(3));
                string fileW = nW + (g == 0 ? "-aura-III-acik-tas.png" : "-aura-III-islak-arduvaz.png");
                string labelW = nW + " B11 AURA III " + tintW + " / " + groundW + " (S2)";
                Stats allW = Region(litW, darkW, 0, W - 1, 0, H - 1);
                float ppmW = PxPerMetre(0f);
                Radial radW = RadialOf(litW, darkW, lookW, Px(O), 0, W - 1);
                AddHalf(nW, "auraIII", keyW, 3, .4f, allW, ppmW, radW, float.NaN);
                s_veil[nW] = veilW;
                string jsonW = Obj(P("n", nW), P("file", fileW), P("label", labelW), P("kind", "auraWhole"), P("level", 3), P("tint", tintW),
                    P("radiusM", TreeVfxLadder.AuraRadius(3)), P("alphaCap", TreeVfxLadder.AuraAlpha(3)), P("tActualS", VfxClock.Now - t0W), P("drawnLevel", aura.Level),
                    P("ringVisible", aura.RingVisible), P("sparksVisible", aura.SparksVisible), P("smokeVisible", aura.SmokeVisible), P("ground", FloorName()),
                    P("groundVeilLevels", veilW), P("nonEmpty", allW.Changed > 200), PR(keyW, HalfJson(allW, ppmW, radW, float.NaN)));
                AddFrame(nW, fileW, labelW, lookW, litW, W, H, Roi(O, 8f), jsonW, FloorMarks());
                Debug.Log("[DD-TEST] wp104b4.ad " + fileW + " ring=" + aura.RingVisible + " changed=" + allW.Changed + " lumStep=" + F(LumStepOf(allW)));
                Assert.Greater(allW.Changed, 200, "aura III " + groundW + ": görsel yok (boş kare)");
            }

            s.AuraLevel = 0;
            TreeVfxPreview.SetStats(s);
            SetFloor(Floor.Split);
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
            float veil = GroundVeil(dark);
            s_veil[n] = veil;
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
                P("played", played), P("tTargetS", .55f), P("tActualS", VfxClock.Now - t0), P("ground", FloorName()), P("groundVeilLevels", veil),
                P("nonEmpty", west.Changed + east.Changed > 200),
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
                P("maskLumOnRaw", g.MeanLumOn), P("maskLumOnRawWithCard", g.MeanLumOnCard), P("maskLumDeltaRaw", delta), P("maskLumHdrOff", g.MeanHdrOff), P("maskLumHdrOn", g.MeanHdrOn),
                P("slotEmissionOff", g.SlotOff), P("slotEmissionOn", g.SlotOn), P("m9Raw", m9Raw), P("m9Post", g.GlowSpot >= g.AreaPx));
        }

        /// <summary>
        /// M9post (B4-2, F): what the capture can see of the socket glow card — TreeAttachGlow's public int / float / bool members whose
        /// name holds "Card" or "Socket" (by reflection: this file builds before and after F), the component types under the glow host
        /// and the weapon whose name holds "Card", and the enabled renderers under the glow host with their materials (a card F parents
        /// there). "missing" when there is nothing of the kind. A card drawn with Graphics.DrawMesh from a LateUpdate never reaches this
        /// capture's renders (they run in the coroutine, before LateUpdate): it must be a renderer, or be queued earlier in the frame.
        /// </summary>
        private static string CardJson(TreeAttachGlow glow, GameObject host, GameObject weapon)
        {
            var pairs = new List<string>();
            const BindingFlags members = BindingFlags.Public | BindingFlags.Instance;
            if (glow != null)
            {
                foreach (PropertyInfo p in typeof(TreeAttachGlow).GetProperties(members))
                {
                    if (p.GetIndexParameters().Length != 0 || !CardName(p.Name)) continue;
                    object v;
                    try
                    {
                        v = p.GetValue(glow);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    AddScalar(pairs, p.Name, v);
                }

                foreach (FieldInfo f in typeof(TreeAttachGlow).GetFields(members | BindingFlags.Static))
                {
                    if (CardName(f.Name)) AddScalar(pairs, f.Name, f.GetValue(f.IsStatic ? null : glow));
                }
            }

            var types = new List<string>();
            foreach (GameObject root in new[] { host, weapon })
            {
                if (root == null) continue;
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    string name = c.GetType().Name;
                    if (name.IndexOf("Card", StringComparison.Ordinal) >= 0 && !types.Contains(Q(name))) types.Add(Q(name));
                }
            }

            var renderers = new List<string>();
            if (host != null)
            {
                foreach (Renderer r in host.GetComponentsInChildren<Renderer>(false))
                {
                    if (r != null && r.enabled && renderers.Count < 8) renderers.Add(Q(r.name + " / " + (r.sharedMaterial != null ? r.sharedMaterial.name : "-")));
                }
            }

            if (pairs.Count == 0 && types.Count == 0 && renderers.Count == 0) return Q("missing");
            pairs.Add(PR("cardComponents", Arr(types)));
            pairs.Add(PR("hostRenderers", Arr(renderers)));
            return Obj(pairs);
        }

        private static bool CardName(string name) => name.IndexOf("Card", StringComparison.Ordinal) >= 0 || name.IndexOf("Socket", StringComparison.Ordinal) >= 0;

        /// <summary>One readout as JSON: numbers, flags, text, a tint (Vector4 / Color), a renderer (name, enabled, last seen, material).</summary>
        private static void AddScalar(List<string> pairs, string name, object v)
        {
            if (v is int iv) pairs.Add(P(name, iv));
            else if (v is float fv) pairs.Add(P(name, fv));
            else if (v is bool bv) pairs.Add(P(name, bv));
            else if (v is string sv) pairs.Add(P(name, sv));
            else if (v is Vector4 v4) pairs.Add(PR(name, "[" + F(v4.x) + ", " + F(v4.y) + ", " + F(v4.z) + ", " + F(v4.w) + "]"));
            else if (v is Color col) pairs.Add(PR(name, "[" + F(col.r) + ", " + F(col.g) + ", " + F(col.b) + ", " + F(col.a) + "]"));
            else if (v is Renderer rr && rr != null)
            {
                pairs.Add(PR(name, Obj(P("name", rr.name), P("enabled", rr.enabled), P("visibleLastFrame", rr.isVisible),
                    P("material", rr.sharedMaterial != null ? rr.sharedMaterial.name : null))));
            }
            else if (v == null || v is Object) pairs.Add(PR(name, "null"));
        }

        /// <summary>F's TreeAttachGlow.CardsEnabled (reflection; S-07 12.10: hide the socket cards while the glow-slot mask renders): "on" /
        /// "off" as set, "missing" when there is no such property.</summary>
        private static string SetCards(TreeAttachGlow glow, bool on)
        {
            PropertyInfo p = glow != null ? typeof(TreeAttachGlow).GetProperty("CardsEnabled", BindingFlags.Public | BindingFlags.Instance) : null;
            if (p == null || !p.CanWrite || p.PropertyType != typeof(bool)) return "missing";
            p.SetValue(glow, on);
            return on ? "on" : "off";
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
            _cam.cullingMask = CaptureMask();   // b4-r3: never one of the world's cameras (see CaptureMask)
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
        /// b4-r3 (12.10): Wp101MermiSinifTdTests / Wp095MermiTdKareTests load the Run scene (LoadScene Single) and leave it; its v6 world
        /// keeps DD.Art.VolumetricLight live, and VolumetricLightFeature (BeforeRenderingTransparents, one full-screen composite) and
        /// PlanarReflection draw into every base game camera whose culling mask holds the Environment layer (PlanarReflection.RendersWorld)
        /// — this camera, with Everything, took the world's in-scatter on every render. Every layer but Environment: nothing this capture
        /// draws is on it (the grounds and stand-ins are Default, the tracers DrawMeshInstanced on 0, the binder's cards and the add-ons
        /// their own), so no world pass ever draws into the capture, loaded world or not.
        /// </summary>
        private static int CaptureMask()
        {
            int environment = DD.Prototype.Core.PrototypeLayers.Environment;
            return environment >= 0 && environment < 32 ? ~(1 << environment) : ~0;
        }

        /// <summary>A loaded world's state the header records (by type name: this assembly does not reference DD.Art): "missing" when the
        /// type or member is not there.</summary>
        private static string WorldFlag(string typeName, string member)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try
                {
                    t = a.GetType(typeName, false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (t == null) continue;
                PropertyInfo p = t.GetProperty(member, BindingFlags.Public | BindingFlags.Static);
                if (p != null) return Convert.ToString(p.GetValue(null), CultureInfo.InvariantCulture);
                FieldInfo f = t.GetField(member, BindingFlags.Public | BindingFlags.Static);
                return f != null ? Convert.ToString(f.GetValue(null), CultureInfo.InvariantCulture) : "missing";
            }

            return "missing";
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
                    P("tracerFrame", "each tracer capture starts on a Unity frame of its own (AD-B4 M2); darkLeakPx = 0 proves the ground-only render is bare; "
                                     + "coreLeakPx / haloLeakPx = 0 that the bare ground-off render of the core's / the halo's Unity frame is black"),
                    P("cullingMask", "0x" + _cam.cullingMask.ToString("X8", CultureInfo.InvariantCulture)),
                    P("environmentLayer", DD.Prototype.Core.PrototypeLayers.Environment),
                    P("worldPasses", "the capture camera leaves the Environment layer out: PlanarReflection.RendersWorld is false for it, so VolumetricLightFeature "
                                     + "and the planar reflection never draw into it (b4-r3), whatever world an earlier fixture left loaded"))),
                PR("world", Obj(P("activeScene", SceneManager.GetActiveScene().name), P("sceneCount", SceneManager.sceneCount),
                    P("volumetricLightActive", WorldFlag("DD.Art.VolumetricLight", "Active")), P("screenAtmosphereWorldActive", WorldFlag("DD.Art.ScreenAtmosphereFeature", "WorldActive")),
                    P("note", "the state at the first SetUp: a loaded world (e.g. the Run scene an earlier fixture left) with volumetricLightActive True is the b4-r3 condition; "
                              + "haloLeakPx / coreLeakPx / groundVeilLevels show whether it still reached the frames"))),
                P("vfxTier", VfxTier.Current.ToString()), P("captureDeltaTime", "1/60 (game time) until the preview frames 21-26, then 0 (real time); 37-38 1/60"),
                P("fog", false), P("reducedMotion", false),
                PR("tracerStyle", Constants(typeof(TracerStyle), null)), PR("tracerTd", Constants(typeof(TracerRibbonRenderer), "Td")),
                PR("ladder", Constants(typeof(TreeVfxLadder), null)),
                PR("definitions", Obj(
                    P("brightCorePx", "tracer layer 0 alone on black: pixels whose brightest channel >= 254 (and not already in the bare ground-off render of that Unity frame) across the streak 0.1 m behind the head (A5); Max over 0.1-2 m"),
                    P("coreQuadPx", "the same column, a channel risen >= 8 over the bare ground-off render (the core quad with its soft edge)"),
                    P("haloPx", "tracer halos alone on black: a channel risen >= 0.10 over the bare ground-off render of the halo's Unity frame, across the streak 0.25 m behind the head; Max over 0.1-2 m"),
                    P("coreLeakPx", "tracer frame: pixels of the bare ground-off render taken in the core's Unity frame before any of the capture's draws whose brightest channel >= 8 (0 = layer 0's measures are its own draws)"),
                    P("haloLeakPx", "tracer frame: the same for the halo's Unity frame, before DrawTd(2) (0 = nothing of the previous frame left queued, nothing a loaded world draws over black; b4-r3 had its whole frame here)"),
                    P("blackMaxLevel", "tracer frame: the brightest channel in either bare ground-off render (0 on a clean frame)"),
                    P("groundVeilLevels", "every frame: the four 5 x 5 corner patches of its ground-only render against the bare ground (Unlit sRGB 0.75 -> 191, 0.20 -> 51), worst channel, 8-bit levels (<= 2 clean; b4-r3's world in-scatter: 3-9)"),
                    P("seekTrail", "35-36 (B4-2 B9): KIVRILAN / DUZ = designed paths laid by SetTdPathForTests (pathPoints 3), GERCEK = a real round (pathPoints = ProjectileSystem.TryGetPath's kept + now); "
                                   + "recordSeekPoints = the TD record's SeekPoints (F); pathBendM / flightBendM = the kept path's / the driven flight's farthest point from its chord (world xz); "
                                   + "drawnBendM = the drawn trail's pixels (lit vs dark >= 0.10, within 1 m of the flight or the kept path) binned along the axis from the pixel farthest from the head to the head: the largest distance of a bin centroid from the chord of the first and last centroid (width-free); drawnBendFaintM the same over pixels moved >= 8 levels (the faint tail end too); "
                                   + "widths across the flight's direction 0.25 / 0.5 m behind the head: visiblePx (lit vs dark >= 0.10), layer2Px (halo-only render risen >= 0.10 over its bare render), brightCorePx / coreQuadPx (layer 0)"),
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
                    P("deltaE", "CIE76 on sRGB -> linear -> XYZ (D65) -> L*a*b* (AD-VISUAL §6 A1 / A4)"),
                    P("eventHorizon", "37-38 (B4-3): S-04's Event Horizon well (TreeGravityWells, GD-TREE aw_alt_kx stats) on a hero stand-in at the frame centre, ticked once a frame "
                                      + "(TreeSystemsRunner.Tick, game time 1/60, the DDOL driver parked); coreOffsetM / ringOffsetM = the binder's set cards (HorizonCore / HorizonRing) "
                                      + "from HorizonCenter in world xz; drawnOffsetM = the changed pixels' centroid (lit vs the well-down dark, a channel moved >= 0.10) from "
                                      + "HorizonCenter's pixel / ground px/m; fadeS = game seconds from the tick that took the anchor away until both cards were back in the pool; "
                                      + "afterFadeChangedPx = changed pixels left on the frame then; peakLiveCards / peakLiveHoles = the most Horizon cards of one kind / event "
                                      + "holes alive on any frame while it was up"))));
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

        /// <summary>A screen column of an "alone on black" render counted against <paramref name="bare"/>, the bare ground-off render of the
        /// same Unity frame (black on a clean frame: the plain thresholds then) — see <see cref="Over"/>.</summary>
        private static int ColumnOver(Color32[] img, Color32[] bare, Vector2 c, int half, int threshold, bool saturation)
        {
            int x = (int)c.x, n = 0;
            if (x < 0 || x >= W) return 0;
            for (int dy = -half; dy <= half; dy++)
            {
                int y = (int)c.y + dy;
                if (y < 0 || y >= H) continue;
                int i = y * W + x;
                if (Over(img[i], bare != null ? bare[i] : Black32, threshold, saturation)) n++;
            }

            return n;
        }

        /// <summary>A pixel of an alone-on-black render that is the capture's own: a channel risen ≥ <paramref name="threshold"/> over the
        /// bare render; for A5's bright core (<paramref name="saturation"/>) its brightest channel ≥ the threshold where the bare one is not.</summary>
        private static bool Over(Color32 k, Color32 bare, int threshold, bool saturation) =>
            saturation ? MaxC(k) >= threshold && MaxC(bare) < threshold : Mathf.Max(k.r - bare.r, Mathf.Max(k.g - bare.g, k.b - bare.b)) >= threshold;

        private static int MaxC(Color32 k) => Mathf.Max(k.r, Mathf.Max(k.g, k.b));

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

        /// <summary>One AD-B4 MUST / SHOULD or B4-2 criterion: its checks (measured, threshold, pass), the data it lacked and notes. PASS =
        /// every check passed with nothing missing; AD KABUL = the only misses are checks the AD accepted (C-00 12.10: M5 A1, M6 A4 — still
        /// measured); FAIL = a check failed; open (null, "?") = data missing (a test not run, a frame absent, an API not there yet).</summary>
        private sealed class Crit
        {
            public readonly string Id, Title, Basis, Group;
            public readonly List<string> Checks = new List<string>(), Missing = new List<string>(), Notes = new List<string>();
            public string Detail;
            public int Passed, Failed, Accepted;

            public Crit(string id, string group, string title, string basis)
            {
                Id = id;
                Group = group;
                Title = title;
                Basis = basis;
            }

            public bool Must => Group == GroupMust;

            /// <summary>A check the AD accepted under its threshold: measured and written; a miss reads "AD kabul" and never fails the
            /// criterion.</summary>
            public void AddAccepted(string name, float measured, string threshold, bool pass, string why)
            {
                if (float.IsNaN(measured) || float.IsInfinity(measured))
                {
                    Missing.Add(name);
                    return;
                }

                Checks.Add(Obj(P("check", name), P("measured", measured), P("threshold", threshold), P("pass", pass), P("verdict", pass ? "PASS" : "AD kabul"),
                    P("adKabul", !pass), P("why", why)));
                if (pass) Passed++;
                else Accepted++;
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

            public bool? Pass => Failed > 0 ? false : Missing.Count > 0 || Passed + Accepted == 0 ? (bool?)null : true;

            public string Word => Pass == true ? (Accepted > 0 ? "AD KABUL" : "PASS") : Pass == false ? "FAIL" : "?";

            public string Json() => Obj(P("id", Id), P("group", Group), P("must", Must), P("title", Title), P("basis", Basis), PB("pass", Pass), P("verdict", Word),
                P("passed", Passed), P("failed", Failed), P("accepted", Accepted), PR("missing", Strs(Missing)), PR("notes", Strs(Notes)), PR("detail", Detail ?? "null"),
                PR("checks", Arr(Checks)));
        }

        private static string AdJson(out string mustLine, out string shouldLine, out string b42Line, out string b43Line)
        {
            var crits = new List<Crit> { M1(), M2(), M3(), M4(), M5(), M6(), M7(), M8(), M9(), M10(), S1(), S2(), S3(), S4(), M9Post(), B9(), B43E1() };
            var must = new StringBuilder("AD-B4 MUST:");
            var should = new StringBuilder("AD-B4 SHOULD:");
            var b42 = new StringBuilder("B4-2:");
            var b43 = new StringBuilder("B4-3:");
            int pass = 0, accepted = 0, fail = 0, open = 0;
            var items = new List<string>();
            foreach (Crit c in crits)
            {
                if (c.Group == GroupMust) must.Append(' ').Append(c.Id).Append(' ').Append(c.Word).Append(" |");
                else if (c.Group == GroupShould) should.Append(' ').Append(c.Id).Append(' ').Append(c.Word).Append(" |");
                if (c.Must)
                {
                    if (c.Pass == true) pass++;
                    else if (c.Pass == false) fail++;
                    else open++;
                    if (c.Pass == true && c.Accepted > 0) accepted++;
                }

                items.Add(c.Json());
            }

            foreach (string id in B42Ids)
            {
                Crit c = crits.Find(x => x.Id == id);
                b42.Append(' ').Append(id).Append(' ').Append(c != null ? c.Word : "?").Append(" |");
            }

            foreach (string id in B43Ids)
            {
                Crit c = crits.Find(x => x.Id == id);
                b43.Append(' ').Append(id).Append(' ').Append(c != null ? c.Word : "?").Append(" |");
            }

            mustLine = must.ToString().TrimEnd('|', ' ');
            shouldLine = should.ToString().TrimEnd('|', ' ');
            b42Line = b42.ToString().TrimEnd('|', ' ');
            b43Line = b43.ToString().TrimEnd('|', ' ');
            return Obj(P("source", "K/wp-104/b4-s07/evidence/AD-B4.md §3 MUST / §4 SHOULD (12.10) + B4-2 / B4-3 (C-00 12.10): every threshold evaluated on this run's frames; PASS / FAIL is never asserted"),
                P("mustSummary", mustLine), P("shouldSummary", shouldLine), P("b42Summary", b42Line), PR("b42", Strs(new List<string>(B42Ids))),
                P("b43Summary", b43Line), PR("b43", Strs(new List<string>(B43Ids))),
                P("mustPass", pass), P("mustAdKabul", accepted), P("mustFail", fail), P("mustOpen", open),
                P("adKabul", "C-00 12.10: M5 A1 (ΔE ≥ 25, kademe I) and M6 A4 (ΔE ≥ 30, halka r farkı) accepted by the AD — measured on, a miss reads 'AD kabul', "
                             + "the criterion 'AD KABUL' when nothing else fails"),
                PR("a11Key", s_blindKey ?? "null"),
                P("m2RootCause", "b4-r2: CaptureTracers 02 and 07 ran in the Unity frame where 01 / 06 had queued their halo-only pass (DrawTd(2) after their own yield); "
                    + "Graphics.DrawMeshInstanced draws in every render of its frame, so 02 / 07 rendered those halos into their dark, core and lit frames (brightCorePx 33 / 24 = "
                    + "halo pixels, visiblePx 19 / 6 = lit minus a dark that held the halo). Fixed: every tracer capture starts on a fresh Unity frame; darkLeakPx per frame"),
                P("r3RootCause", "b4-r3 (haloPx I / IV / V 64 / 91 / 123 on frames 01-02, coreQuadPx 123 = the whole +-0.6 m column on every round, every wet-slate half 123): "
                    + "the run had Wp101MermiSinifTdTests / Wp095MermiTdKareTests before this fixture; they load the Run scene (LoadScene Single) and leave it, and its v6 world "
                    + "keeps DD.Art.VolumetricLight live. VolumetricLightFeature composites its raymarched in-scatter (BeforeRenderingTransparents, full screen) into every base "
                    + "camera whose culling mask holds the Environment layer (PlanarReflection.RendersWorld) - the capture camera had Everything. With the ground on, the veil is "
                    + "the same in the lit and the dark render (lit - dark clean, darkLeakPx 0; only the ground drifted: wet slate 54-60 for 51, brighter east, light stone 189-190 "
                    + "for 191 in b4-r3's raw PNGs); in the ground-off renders (layer 0 alone, the halos alone) the ray ends in the sky (30 m) and the veil lies on black: >= 8/255 "
                    + "everywhere (coreQuadPx = the column), >= 26/255 where the cloud cookie lets the sun through (the halo columns of lanes I / IV / V, every east half). "
                    + "b4-r4 ran without those fixtures: clean. Fixed: the capture camera leaves the Environment layer out (no world pass draws into it); the core / halo "
                    + "measures count a pixel's rise over a bare ground-off render of the same Unity frame; coreLeakPx / haloLeakPx (those bare renders are black) and "
                    + "groundVeilLevels (each frame's ground is the bare ground) prove every frame, M2 checks them"),
                PR("criteria", Arr(items)));
        }

        private static RoundM FindRound(int frame, string key, string ground) => s_rounds.Find(r => r.Frame == frame && r.Key == key && r.Ground == ground);

        private static HalfM FindHalf(int frame, string key) => s_halves.Find(h => h.Frame == frame && h.Key == key);

        private static GlowM FindGlow(string ground, string addOn) => s_glows.Find(g => g.Ground == ground && g.AddOn == addOn);

        private static string GroundTag(int g) => g == 0 ? "açık taş" : "ıslak arduvaz";

        private static float Ratio(float a, float b) => b > 0f ? a / b : float.NaN;

        private static Crit M1()
        {
            var c = new Crit("M1", GroupMust, "Merdiven: AR I-V hale +5 px / çekirdek sabit / iz boyu; LMG V, SR V; A11",
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
            var c = new Crit("M2", GroupMust, "Ölçüm tutarlılığı",
                "AD-B4 §2.2, §3 M2 · aynı WP-101 AR I izi: kare 02 ↔ 03 (ıslak arduvaz), 01 ↔ 03 (açık taş); bütün iz kareleri; her ölçü kendi karesinde: darkLeakPx, coreLeakPx, haloLeakPx (b4-r3), bütün karelerde groundVeilLevels");
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
            // b4-r3: each alone-on-black measure is its own Unity frame's draws only — the bare ground-off render of that frame is black
            foreach (KeyValuePair<int, int> kv in s_coreLeak) c.Add("kare " + kv.Key + " coreLeakPx (çekirdek karesinin zeminsiz çıplak render'ı)", kv.Value, "0", kv.Value == 0);
            foreach (KeyValuePair<int, int> kv in s_haloLeak) c.Add("kare " + kv.Key + " haloLeakPx (hale karesinin zeminsiz çıplak render'ı)", kv.Value, "0", kv.Value == 0);
            int veiled = 0;
            float worst = 0f;
            var veils = new List<string>();
            foreach (KeyValuePair<int, float> kv in s_veil)
            {
                worst = Mathf.Max(worst, kv.Value);
                if (kv.Value <= 2f) continue;
                veiled++;
                if (veils.Count < 12) veils.Add(Q("kare " + kv.Key + ": " + F(kv.Value)));
            }

            if (s_veil.Count == 0) c.Missing.Add("groundVeilLevels");
            else c.AddRaw("bütün kareler: zemin peçesi groundVeilLevels ≤ 2 seviye (yüklü dünya peçesi / sis yok)",
                Obj(P("frames", s_veil.Count), P("over", veiled), P("worstLevels", worst), PR("offenders", Arr(veils))), "over = 0", veiled == 0);
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
            var c = new Crit("M3", GroupMust, "Reduce Flashes: biçim korunur, HDR kısılır", "AD-B4 §3 M3 · kare 06 (RF kapalı) ↔ 07 (RF açık 0.4): ATES V ve NOTR (WP-101), iki zemin");
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
            var c = new Crit("M4", GroupMust, "K3 işaretli mermi", "AD-B4 §3 M4 · kare 05 (işaretli + normal, iki zemin)");
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
            var c = new Crit("M5", GroupMust, "Element kimliği açık taşta; A1 kademe I", "AD-B4 §3 M5, AD-VISUAL §6 A1 · kare 03 (kademe I, Atış = Element = I)");
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
                        c.AddAccepted("A1 ıslak arduvaz · ΔE76 " + ElementWord(Elements[i]) + "-" + ElementWord(Elements[j]) + " (#" + Hex(med[i]) + " / #" + Hex(med[j]) + ")", de,
                            "≥ 25", de >= 25f, AdKabulA1);
                    }
                }
            }

            c.Notes.Add("ΔE = CIE76 (AD-VISUAL §6 A1): glow medyanı = post karede, şeridin değişen pikselleri içinde çekirdek dörtgeni (katman 0 ≥ 8) dışındakilerin kanal başına medyanı.");
            c.Notes.Add(AdKabulA1 + ": A1 çiftleri eşiğin altında kalırsa 'AD kabul' yazılır, M5'i düşürmez; açık taş colouredShare / ton denetimleri olağan PASS / FAIL.");
            return c;
        }

        private static Crit M6()
        {
            var c = new Crit("M6", GroupMust, "Aura açık taşta; A4; A17", "AD-B4 §3 M6, AD-VISUAL §6 A4 / A17 · kare 22-24 (dikiş: batı açık taş, doğu ıslak arduvaz)");
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
                        c.AddAccepted(GroundTag(g) + " · A4 ΔE76 aura " + Roman(a + 1) + "-" + Roman(b + 1) + " (#" + Hex(x.R.MeanPost) + " / #" + Hex(y.R.MeanPost) + ")", de,
                            "≥ 30", de >= 30f, AdKabulA4);
                    }
                }

                for (int a = 0; a < 2; a++)
                {
                    int ra = RingPx(halves[a, g]), rb = RingPx(halves[a + 1, g]);
                    c.AddAccepted(GroundTag(g) + " · A4 halka r " + Roman(a + 1) + "-" + Roman(a + 2) + " (" + ra + " → " + rb + " px)", rb - ra, "≥ 12 px", rb - ra >= 12,
                        AdKabulA4);
                }
            }

            c.Notes.Add(AdKabulA4 + ": A4 (ΔE ve halka r farkı) eşiğin altında kalırsa 'AD kabul' yazılır, M6'yı düşürmez; açık taş changedPx ve A17 olağan PASS / FAIL.");

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
            var c = new Crit("M7", GroupMust, "Can emme ipliği açık taşta", "AD-B4 §3 M7 · kare 21 (kurban doğuda / ıslak arduvaz, kahraman batıda / açık taş)");
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
            var c = new Crit("M8", GroupMust, "Ateş zemini açık taşta (B4-2: FireUnderAlpha .225, iki eşik birlikte)",
                "AD-B4 §3 M8 · kare 17 (t 0.3 s), 18 (t 1.5 s); açık taş satMean ≥ .35 / ton 10-35 ve ıslak arduvaz b4-r2'ye göre ±%10, aynı koşuda; C-00 12.10: alt çizim α .225");
            // C-00 12.10: M8 is measured at E's .225 underlay — the constant in effect is part of the evidence (by name: E owns it)
            float alpha = LadderFloat("FireUnderAlpha");
            if (float.IsNaN(alpha)) c.Notes.Add("TreeVfxLadder.FireUnderAlpha: missing (yansımayla bulunamadı)");
            else c.Add("TreeVfxLadder.FireUnderAlpha (alt çizim #E0702A α; AD ≤ .30)", alpha, "0.21 ±0.005 (C-00 12.10: .225 b42-r1 geçmedi)", Mathf.Abs(alpha - .21f) <= .005f && alpha <= .30f);
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

        /// <summary>M9 (B4-1): the binding — the glow slot shows and its raw-frame luminance steps up FlashScale 0 → 1. The post bloom
        /// spot is <see cref="M9Post"/> (AD-B4 §6: it moved to B4-2 and does not hold B4-1's PASS).</summary>
        private static Crit M9()
        {
            var c = new Crit("M9", GroupMust, "Ek ışıması bağlaması, ham kare (E4 ateş III, E5 V)",
                "AD-B4 §3 M9 · kare 27 (açık taş), 28 (ıslak arduvaz), td-rig görünüşü: FlashScale 0 → 1, ışıma yuvası (yuva 1) piksellerinde; post bloom eşiği M9post'ta (AD-B4 §6, B4-2)");
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
                    c.Notes.Add(tag + " " + addOn + ": emisyon (FlashScale 1) " + F(g.Emission) + ", yuva 1 MPB _EmissionColor.r kapalı / açık " + F(g.SlotOff) + " / " + F(g.SlotOn));
                }
            }

            c.Notes.Add("Görünüş td-rig (C, 12.10): ışıma yuvası eklerin yana bakan penceresidir; dik kamerada E4 0 px, E5 ≈ 3 px görünür, oyunun ≈ 49° TD rig açısından 12-31 px.");
            return c;
        }

        /// <summary>M9post (B4-2, AD-B4 §6): the add-on glow's bloom spot on the post frame — with F's socket glint card — covers at least
        /// the add-on's own screen area (td-rig view), each add-on on each ground.</summary>
        private static Crit M9Post()
        {
            var c = new Crit("M9post", GroupB42, "Ek ışıması post: soket parıltı kartı ile glowSpotPx ≥ ekin ekran alanı",
                "AD-B4 §3 M9 (post eşiği) + §6 (B4-2: AP-041 parıltı sayfasından soket kartı, yeni varlık yok) · kare 27 (açık taş), 28 (ıslak arduvaz), 29 (×4), td-rig görünüşü; RF 0.4'te bloom yok: AD kabul");
            foreach (string ground in Grounds)
            {
                string tag = ground == KeyStone ? GroundTag(0) : GroundTag(1);
                foreach (string addOn in new[] { "E4", "E5" })
                {
                    GlowM g = FindGlow(ground, addOn);
                    if (g == null || !g.Present)
                    {
                        c.Missing.Add(tag + " " + addOn + (g == null ? "" : " ekran alanı"));
                        continue;
                    }

                    c.Add(tag + " · " + addOn + " post glowSpotPx (ekran alanı " + F(g.SizeX) + " × " + F(g.SizeY) + "; tepe lum kapalı / açık " + F(g.PeakOff) + " / " + F(g.PeakOn) + ")",
                        g.GlowSpot, "≥ " + F(g.AreaPx) + " (AD ≈ " + (addOn == "E4" ? 100 : 58) + ")", g.GlowSpot >= g.AreaPx);
                }
            }

            c.Notes.Add("Soket parıltı kartı (F, yansımayla; FlashScale 1, açık taş): " + (s_cardJson ?? "kare 27-28 koşmadı"));
            c.Notes.Add("glowSpotPx = ekin merkezinin 24 px içinde post luminansı FlashScale 0 → 1 en az 0.06 artan pikseller (ışıma + bloom + kart); ekran alanı = ekin sınır kutusunun td-rig izdüşümü.");
            return c;
        }

        private static Crit M10()
        {
            var c = new Crit("M10", GroupMust, "B8 ölçeği", "AD-B4 §3 M10 · kare 14-16 (R 3 m, III, birinci kuşak): halka dış çapı 0.8-1.0 × 2R × 0.9");
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

        /// <summary>The same for <see cref="TreeVfxLadder"/> (E owns its B4-2 values: FireUnderAlpha …).</summary>
        private static float LadderFloat(string name)
        {
            FieldInfo f = typeof(TreeVfxLadder).GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (f != null && f.FieldType == typeof(float)) return (float)f.GetValue(null);
            PropertyInfo p = typeof(TreeVfxLadder).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (p != null && p.PropertyType == typeof(float) && p.GetIndexParameters().Length == 0) return (float)p.GetValue(null);
            return float.NaN;
        }

        private static Crit S1()
        {
            var c = new Crit("S1", GroupShould, "V'te hale HDR ×1.5", "AD-B4 §4 S1 · kare 02 (ıslak arduvaz), post karede");
            RoundM iv = FindRound(2, "ATIS IV", KeySlate), v = FindRound(2, "ATIS V", KeySlate);
            if (iv == null || v == null) c.Missing.Add("kare 02 ATIS IV / V");
            else c.Add("post visiblePx V − IV (" + v.VisiblePost + " − " + iv.VisiblePost + ")", v.VisiblePost - iv.VisiblePost, "≥ 6", v.VisiblePost - iv.VisiblePost >= 6);
            return c;
        }

        /// <summary>S2 (B4-2: E's light inner line on aura III): the ring's value step on BOTH grounds — the AD's ≥ .08 on the wet slate,
        /// and the same floor on the light stone (C-00 12.10 "iki zemin": the line must not wash out the light stone's reading) — on the
        /// whole-ground frames 33 / 34; frame 24's split halves beside it in the notes.</summary>
        private static Crit S2()
        {
            var c = new Crit("S2", GroupShould, "Aura III değer artışı, iki zemin",
                "AD-B4 §4 S2 (ıslak arduvaz lumStepMean ≥ .08, #E8302A ×1.3) + B4-2 (C-00 12.10: iki zemin) · kare 33 (aura III, tüm kare açık taş), 34 (tüm kare ıslak arduvaz)");
            for (int g = 0; g < 2; g++)
            {
                int f = 33 + g;
                HalfM h = FindHalf(f, Grounds[g]);
                if (h == null)
                {
                    c.Missing.Add("kare " + f);
                    continue;
                }

                c.Add(GroundTag(g) + " · aura III lumStepMean (changedPx " + h.S.Changed + ")", h.S.Changed > 0 ? LumStepOf(h.S) : float.NaN, "≥ 0.08", LumStepOf(h.S) >= .08f);
            }

            HalfM wl = FindHalf(24, KeyStone), ww = FindHalf(24, KeySlate);
            if (wl != null && ww != null)
            {
                c.Notes.Add("kare 24 (ayrık zemin) lumStepMean: açık taş yarısı " + F(LumStepOf(wl.S)) + ", ıslak arduvaz yarısı " + F(LumStepOf(ww.S)) + " (b4-r4: .1814 / .0182)");
            }

            c.Notes.Add("Açık taşta aynı ≥ .08 tabanı: açık iç çizgi açık taşın değer okumasını silmemeli (C-00 12.10 'iki zemin').");
            return c;
        }

        private static Crit S3()
        {
            var c = new Crit("S3", GroupShould, "Sumi halkalar ıslak arduvazda", "AD-B4 §4 S3 · kare 10 (B5 III t 0.35), 16 (B8 t 0.35): halka kenarı = açık taş yarısının r98 halkası (6 px)");
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
            var c = new Crit("S4", GroupShould, "Zehir bulutu açık taşta", "AD-B4 §4 S4 · kare 19 (t 0.3), 20 (t 1.5), M8 yöntemi");
            foreach (int f in new[] { 19, 20 })
            {
                HalfM l = FindHalf(f, KeyStone);
                if (l == null) c.Missing.Add("kare " + f);
                else c.Add("kare " + f + " açık taş colouredShare", ColouredOf(l.S), "≥ 0.50", ColouredOf(l.S) >= .50f);
            }

            return c;
        }

        private static SeekM FindSeek(int frame, string key) => s_seeks.Find(m => m.Frame == frame && m.Key == key);

        /// <summary>
        /// B9 (B4-2, F): Seeker Swarm's trail bends where its round turned — on the designed curve (KIVRILAN: 3 m + 30° + 3 m, F's
        /// SetTdPathForTests) the drawn trail's centre line strays ≥ 0.5 m from its chord (C-00 12.10; the design's own 0.78 m) — while its
        /// width stays the round's own: against the same record kind on a straight path through the same draw path (DUZ, ±2 px; also the
        /// measure's control, ≤ 0.15 m), and for a tracer its halo on the B4-1 ladder (base + the tier step ±2 px). GERCEK, when it ran,
        /// proves the chain on a real round: S-02 kept its path (TryGetPath ≥ 3) and F's TickTd read it (SeekPoints ≥ 3); its drawn bend
        /// is written as a note (it depends on S-02's path spacing at the weapon's speed, not on F's drawing).
        /// </summary>
        private static Crit B9()
        {
            var c = new Crit("B9", GroupB42, "Kıvrılan seeker izi (sh_cap ek atışı): kıvrım görünür, en / hale B4-1 merdiveniyle tutarlı",
                "B4-2 B9 (F, C-00 / S-07 12.10) · kare 35 (açık taş), 36 (ıslak arduvaz): KIVRILAN = tasarlanmış yol 3 m + 30° + 3 m (F SetTdPathForTests), DUZ = aynı kayıt türü, "
                + "düz 6 m; ikisi Atış V AR ek atışı (VolleyIndex 1, TD'de gövde izi); iz boyu = min(tutulan yol, 10 m) (karar a); GERCEK = gerçek AR mermisi, "
                + "S-02 TryGetPath → F TickTd (isteğe bağlı)");
            if (s_seekMissing != null) c.Missing.Add("missing: " + s_seekMissing);
            for (int g = 0; g < 2; g++)
            {
                int f = 35 + g;
                string tag = GroundTag(g);
                SeekM k = FindSeek(f, "KIVRILAN"), d = FindSeek(f, "DUZ"), r = FindSeek(f, "GERCEK");
                if (k == null || d == null)
                {
                    if (s_seekMissing == null) c.Missing.Add("kare " + f);
                }
                else
                {
                    c.Add(tag + " · KIVRILAN kayıt SeekPoints (F SetTdPathForTests yolu serdi)", k.RecordSeekPoints, "≥ 3", k.RecordSeekPoints >= 3);
                    c.Add(tag + " · KIVRILAN çizilen kıvrım (merkez çizgisinin kirişten en uzak sapması; tasarım " + F(k.PathBendM) + " m; çizilen boy " + F(k.DrawnLengthM) + " m)",
                        k.DrawnBendM, "≥ 0.5 m", k.DrawnBendM >= .5f);
                    c.Notes.Add(tag + " KIVRILAN: soluk kuyruk dahil (≥ 8 seviye) kıvrım " + F(k.DrawnBendFaintM) + " m, boy " + F(k.DrawnLengthFaintM)
                                + " m — çizilen geometri; eşik görünür izde (≥ 0.10, WP-101): kuyruğun sönen ucu kıvrımın bir bacağını kısaltabilir");
                    c.Add(tag + " · DUZ kontrolü: düz izin kıvrımı (çizilen boy " + F(d.DrawnLengthM) + " m)", d.DrawnBendM, "≤ 0.15 m", d.DrawnBendM <= .15f);
                    if (k.Tracer == d.Tracer)
                    {
                        c.Add(tag + " · görünür en KIVRILAN − DUZ, başın 0.25 m gerisi (" + k.Visible025 + " / " + d.Visible025 + " px)", k.Visible025 - d.Visible025, "±2 px",
                            Mathf.Abs(k.Visible025 - d.Visible025) <= 2);
                        c.Add(tag + " · katman 2 (hale / gövde) en KIVRILAN − DUZ, 0.25 m (" + k.Layer2025 + " / " + d.Layer2025 + " px)", k.Layer2025 - d.Layer2025, "±2 px",
                            Mathf.Abs(k.Layer2025 - d.Layer2025) <= 2);
                    }
                    else
                    {
                        c.Notes.Add(tag + ": KIVRILAN ve DUZ farklı türde çizildi (iz " + k.Tracer + " / " + d.Tracer + "): en karşılaştırması yapılmadı");
                    }

                    if (k.Tracer == true)
                    {
                        int target = ArHaloPx + Mathf.RoundToInt(float.IsNaN(k.RecordHaloPx) ? 4 * HaloStepPx : k.RecordHaloPx);
                        c.Add(tag + " · KIVRILAN hale, B4-1 merdiveni (taban 44 + kayıt adımı " + F(k.RecordHaloPx) + ")", k.Layer2025, target + " ±2",
                            Mathf.Abs(k.Layer2025 - target) <= 2);
                    }
                }

                if (r == null) continue;
                c.Add(tag + " · GERCEK: S-02 tutulan yol noktası (TryGetPath, tutulan + şimdiki)", r.PathPoints, "≥ 3", r.PathPoints >= 3);
                c.Add(tag + " · GERCEK: kayıt SeekPoints (F TickTd yolu okudu)", r.RecordSeekPoints, "≥ 3", r.RecordSeekPoints >= 3);
                c.Notes.Add(tag + " GERCEK: çizilen kıvrım " + F(r.DrawnBendM) + " m (çizilen boy " + F(r.DrawnLengthM) + " m), tutulan yol " + F(r.PathBendM) + " m, uçuş "
                            + F(r.FlightBendM) + " m; en 0.25 m: görünür " + r.Visible025 + " / katman 2 " + r.Layer2025 + " px"
                            + (d != null ? " (DUZ " + d.Visible025 + " / " + d.Layer2025 + ")" : ""));
            }

            if (s_realMissing != null) c.Notes.Add("GERCEK seeker (isteğe bağlı): missing: " + s_realMissing);
            if (s_seekNote != null) c.Notes.Add(s_seekNote);
            c.Notes.Add("Ek atış TD'de gövde izidir (S-02: VolleyIndex > 0 tracer olmaz); B4-1 hale merdiveni tracer içindir: gövde izinin görünür eni ve katman 2 eni DUZ ile karşılaştırılır.");
            return c;
        }

        /// <summary>
        /// B43-E1 (B4-3, C-00 12.10): the Event Horizon set rides S-04's orbiting well and goes out with it — on frames 37 / 38 (two moments
        /// of the orbit: light stone, wet slate) both cards' centres within 0.3 m of HorizonCenter (world xz) and the drawn centre (the frame's
        /// changed pixels) too; once HorizonActive is false both cards back in the pool within 0.3 s and nothing left on the frame; one set,
        /// never an event hole at the player; the two moments two places on the orbit.
        /// </summary>
        private static Crit B43E1()
        {
            var c = new Crit("B43-E1", GroupB43, "Olay Ufku: kart merkezi HorizonCenter'a ≤ 0,3 m (iki an), HorizonActive false → ≤ 0,3 s'de söner",
                "B4-3 (C-00 12.10; S-04 7606e452 TreeGravityWells.HorizonActive / HorizonCenter, GD-TREE aw_alt_kx r " + F(HorizonRadiusM) + " m, yörünge " + F(HorizonOrbitM)
                + " m) · kare 37 (açık taş, yörüngenin 1. anı), 38 (ıslak arduvaz, 2. an): TreeVfxBinder.LateUpdate seti; kapanış = SetAnchor kalkar (oyuncu yok)");
            HorizonM[] at = new HorizonM[2];
            for (int g = 0; g < 2; g++)
            {
                int f = 37 + g;
                HorizonM h = at[g] = s_horizons.Find(x => x.Frame == f);
                if (h == null)
                {
                    c.Missing.Add("kare " + f);
                    continue;
                }

                string tag = "kare " + f + " " + GroundTag(g);
                c.Add(tag + " · HorizonActive (çekimde)", h.Up ? 1f : 0f, "1", h.Up);
                c.Add(tag + " · kuyu kahramanın yörüngesinde (HorizonCenter − kahraman, xz)", h.OrbitM, F(HorizonOrbitM) + " ±0.05 m",
                    Mathf.Abs(h.OrbitM - HorizonOrbitM) <= .05f);   // b43-r1: another driver's anchor sent it ≈ 7.9 km away
                c.Add(tag + " · tekillik kartı merkezi − HorizonCenter (xz)", h.CoreOffsetM, "≤ 0.3 m", h.CoreOffsetM <= .3f);
                c.Add(tag + " · halka kartı merkezi − HorizonCenter (xz)", h.RingOffsetM, "≤ 0.3 m", h.RingOffsetM <= .3f);
                c.Add(tag + " · çizilen merkez (değişen piksellerin ağırlık merkezi, " + h.DrawnPx + " px) − HorizonCenter", h.DrawnOffsetM, "≤ 0.3 m", h.DrawnOffsetM <= .3f);
                c.Add(tag + " · HorizonActive false → iki kart havuzda", h.FadeS, "≤ 0.3 s", h.FadeS <= .3f + 1e-4f);
                c.Add(tag + " · söndükten sonra karede kalan değişen piksel", h.AfterPx, "≤ 200 (boş kare)", h.AfterPx <= 200);
                c.Add(tag + " · tek set (canlı tekillik / halka kartı, en çok)", h.PeakCards, "≤ 1", h.PeakCards <= 1);
                c.Add(tag + " · oyuncuda olay deliği (B6 Hole, en çok)", h.PeakHoles, "0", h.PeakHoles == 0);
                c.Notes.Add(tag + ": açı " + F(h.AngleDeg) + "°, yörünge " + F(h.OrbitM) + " m, t " + F(h.TActual) + " s (" + h.Ticks + " tik), değişen " + h.ChangedPx
                            + " px; kart çekirdek " + F(h.CoreCardM) + " m (" + F(h.CoreLiftM) + " m yukarıda), halka " + F(h.RingCardM) + " m; açılış anı " + h.Procs + ", set " + h.Sets);
            }

            if (at[0] != null && at[1] != null)
            {
                float apart = XzDistance(at[0].Centre, at[1].Centre);
                c.Add("iki an yörüngede iki ayrı yer (merkezler arası, açı " + F(at[0].AngleDeg) + "° / " + F(at[1].AngleDeg) + "°)", apart, "≥ 1 m", apart >= 1f);
            }

            c.Notes.Add("Çizilen merkez düz aşağı kamerada ölçülür: çekirdek kartı 1 m yukarıda (olay deliğinin isabet yüksekliği), yörüngede kamera ekseninden 3 m uzakta "
                        + "≈ 0.16 m paralaks taşır; kart merkezleri dünya xz'de ölçülür.");
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
            string ad = AdJson(out string mustLine, out string shouldLine, out string b42Line, out string b43Line);
            var sb = new StringBuilder();
            sb.Append("{\n  \"header\": ").Append(s_header ?? "{}").Append(",\n  \"ad\": ").Append(ad).Append(",\n  \"frames\": [\n");
            int k = 0;
            foreach (Frame f in s_frames.Values) sb.Append(k++ == 0 ? "    " : ",\n    ").Append(f.Json);
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(Path.Combine(_dir, "b4ad.json"), sb.ToString());
            Debug.Log("[DD-TEST] wp104b4.ad " + mustLine + " || " + shouldLine + " || " + b42Line + " || " + b43Line);

            const int band = 4 * LabelH + 10;   // the MUST / SHOULD / B4-2 / B4-3 summary above the tiles
            int rows = (s_frames.Count + Columns - 1) / Columns, cellH = TH + LabelH, sw = Columns * TW, sh = rows * cellH + band;
            var sheet = new Color32[sw * sh];
            for (int i = 0; i < sheet.Length; i++) sheet[i] = new Color32(18, 18, 20, 255);
            Text(sheet, sw, sh, 8, sh - 6, mustLine, 2, Ink, false, sw - 16);
            Text(sheet, sw, sh, 8, sh - 6 - LabelH, shouldLine, 2, Ink, false, sw - 16);
            Text(sheet, sw, sh, 8, sh - 6 - 2 * LabelH, b42Line, 2, Ink, false, sw - 16);
            Text(sheet, sw, sh, 8, sh - 6 - 3 * LabelH, b43Line, 2, Ink, false, sw - 16);
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
