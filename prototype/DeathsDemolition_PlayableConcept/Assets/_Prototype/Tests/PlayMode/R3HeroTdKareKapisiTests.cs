using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-082 R3 (S-06 E4, C-00 08.10; AD-AP024C2 MUST-1 G1/G2, AD-AP024D-D1 §5): the top-down frame gate on hero v2, from the
    /// game's top-down camera (CameraModeService TopDown, Camera.main), the player's default path (no test switch).
    /// <para>
    /// <b>G1 "exit", the definition this test measures.</b> A pixel of the 1080-line top-down frame counts as exit when, along that
    /// pixel's camera ray: (1) the body's nearest surface is there (the body drawn alone, front faces, depth-tested); (2) the held
    /// weapon (its model and, while it is in a hand, its magazine part) has a surface IN FRONT of that body surface — the weapon pixel
    /// is what the player sees there; (3) the body's surface point lies INSIDE the weapon's solid — of the weapon's surfaces in front
    /// of it, the front- and back-facing ones differ in number (a closed solid entered and not yet left) — and the weapon also has a
    /// surface BEHIND it (both faces, ZTest Greater against the body's depth): the weapon crosses the body's visible skin at that
    /// pixel — it comes out through it (or goes in through it, the seam of G2). A part lying on the plate with another part of the
    /// same weapon beyond the far side of the body on the same ray is not a crossing. (4) The body point under the pixel lies in a G1 region: shoulder plate, upper arm, collar, hood
    /// (head: hood and mask), back of the torso (the regions of P-06 D3a: plate_shell, upper_arm, collar, hood, mask, torso_back).
    /// A weapon in front of the body that does not cross it (lying on top of the plate) is reported as "over", not as exit; a weapon
    /// beside the body is neither. Hands and forearms are the grip's contact zone and are not G1 (K6 below). Regions come from the
    /// body point (the pixel's depth unprojected) against the hero's humanoid bone segments, scaled by a radius per segment;
    /// torso front / back by the body's forward.
    /// </para>
    /// <para>
    /// <b>Frames.</b> Twelve weapons (the ten classic ones and the two Specials) × three moments × eight yaws (the character turned
    /// by the aim cursor to 0, 45 … 315° clockwise from the screen's up, the camera the game's own): (a) aim rest (the hold at full
    /// weight, facing settled); (b) the recoil's peak — the frame of the largest muzzle / model travel in the chest frame after one
    /// shot, learnt once per weapon from a first shot; (c) the reload's grasp window — every third frame while the magazine part is
    /// in a hand (WeaponMagazineOwnership owner ≠ Weapon), or between 30 and 70 % of the reload for the shell / Special reloads (the
    /// Specials have no reload clip in game: their pose is the aim pose, recorded). The frame's state is copied in the render
    /// pipeline's beginCameraRendering for the game camera — after LateUpdate, the state that frame draws — and drawn into the probe
    /// next frame.
    /// </para>
    /// <para>
    /// <b>K6 reload hand ↔ part</b> (AD-D1 §5, C-00): in the same reload windows, ar_1 / smg_1 / sr_1 / sw_arc (and every weapon whose
    /// part reaches the hand): the gap in pixels between the part's silhouette and the left hand's (0 = touching or overlapping), and the
    /// part's exit pixels through the left hand / forearm (the same crossing rule).
    /// </para>
    /// <para>
    /// <b>Controls.</b> Positive: ar_1 at yaw 0, aim rest, the model moved (inside the test, put back the same step) so its lowest point
    /// is 5 cm under the right shoulder plate's top along the camera ray through that point → exit must read &gt; 0. Negative: the same
    /// model 60 cm above that point on the same ray → exit 0 and over &gt; 0.
    /// </para>
    /// <para>
    /// Output: g1-td-gate.json / .md to DD_G1_OUT_DIR (else DD_G1_FRAME_DIR, else the temp cache); the worst frame per weapon (real
    /// frame | false colour), the worst hand ↔ part frame, the two controls and one contact sheet to DD_G1_FRAME_DIR when set.
    /// Asserts: the probe works (shader, orientation, controls), every weapon and every moment × yaw was measured with the weapon in the
    /// record. The threshold (G1 = 0 px) is reported, not asserted — the gate decision is S-06's (C-00 08.10); a non-zero row is a
    /// warning in the log and the report.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class R3HeroTdKareKapisiTests
    {
        public const string TestName = "DD.Prototype.Tests.PlayMode.R3HeroTdKareKapisiTests.G1_TdCikis_12Silah_3An_8Yaw_ReloadElParca";

        private const int ProbeHeight = 1080;
        private const int YawCount = 8;
        private const float AimDistance = 2.5f;
        private const float EmbedMetres = 0.05f;
        private const float LiftMetres = 0.60f;
        private const int ClassicReloadStride = 3, SpecialReloadStride = 4;

        // regions (P-06 D3a names; G1 = 1…7)
        private const int RNone = 0, RPlateR = 1, RPlateL = 2, RUpperArmR = 3, RUpperArmL = 4, RCollar = 5, RHood = 6, RTorsoBack = 7,
            RTorsoFront = 8, RForearmR = 9, RForearmL = 10, RHandR = 11, RHandL = 12, RLower = 13, RCount = 14;
        private static readonly string[] RegionNames =
            { "-", "plate_r", "plate_l", "upper_arm_r", "upper_arm_l", "collar", "hood", "torso_back", "torso_front", "forearm_r", "forearm_l", "hand_r", "hand_l", "lower_body" };
        private static bool IsG1(int r) => r >= RPlateR && r <= RTorsoBack;

        // pixel flags
        private const byte FBody = 1, FWeapon = 2, FExit = 4, FOver = 8, FPartSil = 16, FPartExit = 32, FPartVis = 64;

        private static readonly string[] Moments = { "aim", "recoil", "reload" };
        private static readonly string[] PartWeapons = { "ar_1", "smg_1", "sr_1", "sw_arc" };

        private struct Silah
        {
            public string Key;
            public bool Special;
            public ClassicWeaponId Classic;
            public SpecialWeaponId SpecialId;
        }

        private static readonly Silah[] Silahlar =
        {
            new Silah { Key = "ar_1", Classic = ClassicWeaponId.AssaultRifle }, new Silah { Key = "ar_2", Classic = ClassicWeaponId.Ar2 },
            new Silah { Key = "smg_1", Classic = ClassicWeaponId.AutoSmg }, new Silah { Key = "smg_2", Classic = ClassicWeaponId.Smg2 },
            new Silah { Key = "sg_1", Classic = ClassicWeaponId.CombatShotgun }, new Silah { Key = "sg_2", Classic = ClassicWeaponId.Sg2 },
            new Silah { Key = "lmg_1", Classic = ClassicWeaponId.Lmg1 }, new Silah { Key = "lmg_2", Classic = ClassicWeaponId.Lmg2 },
            new Silah { Key = "sr_1", Classic = ClassicWeaponId.Sr1 }, new Silah { Key = "rl_1", Classic = ClassicWeaponId.Rl1 },
            new Silah { Key = "sw_arc", Special = true, SpecialId = SpecialWeaponId.Raiju }, new Silah { Key = "sw_gel", Special = true, SpecialId = SpecialWeaponId.Namekuji },
        };

        private static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };
        private const int BHips = 0, BSpine = 1, BChest = 2, BUpperChest = 3, BNeck = 4, BHead = 5, BLShoulder = 6, BRShoulder = 7, BLUpperArm = 8, BRUpperArm = 9,
            BLLowerArm = 10, BRLowerArm = 11, BLHand = 12, BRHand = 13, BLMiddle = 14, BRMiddle = 15, BLUpperLeg = 16, BRUpperLeg = 17, BLLowerLeg = 18, BRLowerLeg = 19,
            BLFoot = 20, BRFoot = 21;

        private const string ProbeShaderSource = @"Shader ""Hidden/DD/R3TdKareProbe""
{
    Properties { _Out (""out"", Vector) = (1,0,0,0) _Cull (""cull"", Float) = 0 _ZTest (""ztest"", Float) = 4 _ZWrite (""zwrite"", Float) = 1 _ColorMask (""mask"", Float) = 15 _SrcBlend (""src"", Float) = 1 _DstBlend (""dst"", Float) = 0 }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        Pass
        {
            Cull [_Cull] ZTest [_ZTest] ZWrite [_ZWrite] ColorMask [_ColorMask] Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include ""UnityCG.cginc""
            float4x4 _DDView;
            float4x4 _DDProj;
            float4 _Out;
            struct a2v { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float eye : TEXCOORD0; };
            v2f vert (a2v v)
            {
                v2f o;
                float4 w = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0));
                float4 vp = mul(_DDView, w);
                o.pos = mul(_DDProj, vp);
                o.eye = -vp.z;
                return o;
            }
            float4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                if (_Out.z > 0.5)
                {
                    float f = facing > 0 ? 1.0 : 0.0;
                    return _Out.w > 0.5 ? float4(0.0, 0.0, f, 1.0 - f) : float4(f, 1.0 - f, 0.0, 0.0);
                }
                return float4(_Out.x, _Out.y, i.eye, 1.0);
            }
            ENDCG
        }
    }
}";

        // ---------------------------------------------------------------------------------------------------- state

        private sealed class Meta
        {
            public string Key, Moment, Phase;
            public int Yaw, Frame;
            public bool PartInHand, RestReached = true;
        }

        private struct Draw
        {
            public Mesh Mesh;
            public Matrix4x4 M;
            public int Kind;   // 0 body, 1 weapon, 2 magazine part
        }

        private sealed class Snap
        {
            public Matrix4x4 View, Proj;
            public int W, H;
            public Vector3 CamPos, BodyForward;
            public readonly List<Draw> Draws = new List<Draw>();
            public readonly Vector3[] Bone = new Vector3[Bones.Length];
            public readonly bool[] HasBone = new bool[Bones.Length];
            public int BodyLod = -1, WeaponLod = -1;
            public Meta Meta;
            public string Source;
        }

        private sealed class Result
        {
            public int Exit, Over, WeaponVis, Body, PartSil, PartVis, PartExit, HandL, WeaponDraws, PartDraws;
            public readonly int[] ExitBy = new int[RCount];
            public float ExitMm, GapPx = -1f, MmPerPx;
            public RectInt Roi;
            public int BodyLod, WeaponLod;
            public Meta Meta;
            public string Source;
            public float Score => Exit * 1e6f + Over;
            public float PartScore => PartExit * 1e6f + Mathf.Max(0f, GapPx) * 1e3f + PartSil * 1e-3f;
        }

        private sealed class Kanit
        {
            public string Label;
            public int W, H;
            public Color32[] Color;
            public byte[] Flags, Region;
        }

        private sealed class Kayit
        {
            public string Key;
            public bool Special, Authored;
            public int PeakOffset = -1;
            public float PeakMm, MmPerPx;
            public readonly HashSet<int> BodyLods = new HashSet<int>(), WeaponLods = new HashSet<int>();
            public readonly Dictionary<string, Result[]> Worst = new Dictionary<string, Result[]>();
            public readonly Dictionary<string, int[]> Samples = new Dictionary<string, int[]>();
            public readonly Result[] PartWorst = new Result[YawCount];
            public readonly int[] PartSamples = new int[YawCount];
            public int MinWeaponDraws = int.MaxValue, WeaponVisTotal, BodyTotal;
            public bool PartInHandSeen;
            public Result WorstAll, WorstPart;
            public Kanit Evidence, PartEvidence;
            public readonly List<string> Notes = new List<string>();
        }

        private SpecialWeaponSahne _s;
        private ClassicWeaponId _selectedBefore;
        private Camera _cam;
        private PlayerSkinnedVisual _body;
        private Transform _mount;
        private Vector3 _spot, _upDir;
        private float _yaw;
        private bool _aimOn;
        private Shader _shader;
        private Material _mBody, _mBodyDepth, _mBehindW, _mBehindP, _mFrontW, _mFrontP, _mCountW, _mCountP;
        private CommandBuffer _cmd;
        private RenderTexture _rtA, _rtB, _rtC, _rtD, _rtColor;
        private Texture2D _readF, _readH, _readC;
        private Meta _istek;
        private Snap _yakalanan;
        private int _fromRender, _fromCoroutine, _lodOverrides;
        private bool _flipKnown, _flipY;
        private readonly Dictionary<Renderer, Mesh> _bakePool = new Dictionary<Renderer, Mesh>();
        private readonly Dictionary<Renderer, bool> _bakeWorldScale = new Dictionary<Renderer, bool>();
        private readonly Dictionary<string, Kayit> _kayit = new Dictionary<string, Kayit>();
        private byte[] _flags = new byte[0], _region = new byte[0];
        private Result _ctlPos, _ctlNeg;
        private Kanit _ctlPosEv, _ctlNegEv;
        private string _ctlNote = "";

        private WeaponController Ctl => _s.Controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            WeaponHoldIk.ResetStatics();
            WeaponGripIk.ResetStatics();
            _selectedBefore = WeaponRegistry.SelectedWeapon;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            _aimOn = false;
            Time.captureDeltaTime = 0f;
            if (_s != null && _s.Controller != null && _s.Controller.SpecialWeaponActive) _s.Controller.CancelSpecialWeapon();
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            if (_s != null) yield return _s.Bitir();
            _s = null;
            WeaponRegistry.SelectedWeapon = _selectedBefore;   // EquipClassic writes the global selection (ders: test seçim sızıntısı)
            WeaponHoldIk.ResetStatics();
            WeaponGripIk.ResetStatics();
            foreach (Mesh m in _bakePool.Values) if (m != null) Object.Destroy(m);
            _bakePool.Clear();
            _bakeWorldScale.Clear();
            Release(ref _rtA);
            Release(ref _rtB);
            Release(ref _rtC);
            Release(ref _rtD);
            Release(ref _rtColor);
            if (_readF != null) Object.Destroy(_readF);
            if (_readH != null) Object.Destroy(_readH);
            if (_readC != null) Object.Destroy(_readC);
            foreach (Material m in new[] { _mBody, _mBodyDepth, _mBehindW, _mBehindP, _mFrontW, _mFrontP, _mCountW, _mCountP }) if (m != null) Object.Destroy(m);
            if (_shader != null) Object.Destroy(_shader);
            _cmd?.Release();
            _cmd = null;
        }

        private static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.Destroy(rt);
            rt = null;
        }

        // ---------------------------------------------------------------------------------------------------- the test

        [UnityTest]
        [Timeout(3600000)]
        public IEnumerator G1_TdCikis_12Silah_3An_8Yaw_ReloadElParca()
        {
            float started = Time.realtimeSinceStartup;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 1f / 60f;
            yield return SpecialWeaponSahne.Kareler(10);
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "TD kamera etkin değil");
            _cam = Camera.main;
            Assert.IsNotNull(_cam, "ölçüm kurulamadı: oyun kamerası (Camera.main) yok");
            _body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(_body, "ölçüm kurulamadı: skinned oyuncu yok");
            Assert.IsNotNull(_body.Animator, "ölçüm kurulamadı: Animator yok");
            Assert.IsTrue(_body.Renderers != null && _body.Renderers.Length > 0, "ölçüm kurulamadı: gövde renderer'ı yok");
            var binding = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>();
            _mount = binding != null ? binding.WeaponMount : null;
            Assert.IsNotNull(_mount, "ölçüm kurulamadı: silah montajı yok");
            KurProbe();
            _spot = _s.Rig.transform.position;
            _upDir = Vector3.ProjectOnPlane(_cam.transform.up, Vector3.up);
            if (_upDir.sqrMagnitude < 1e-6f) _upDir = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up);
            _upDir.Normalize();
            _aimOn = true;

            foreach (Silah w in Silahlar)
            {
                var k = new Kayit { Key = w.Key, Special = w.Special };
                foreach (string m in Moments)
                {
                    k.Worst[m] = new Result[YawCount];
                    k.Samples[m] = new int[YawCount];
                }

                _kayit[w.Key] = k;
                if (w.Special) yield return SpecialOlc(w, k);
                else yield return KlasikOlc(w, k);
                Debug.Log("[DD-TEST] r3.g1td " + w.Key + " g1MaxPx=" + (k.WorstAll != null ? k.WorstAll.Exit : -1) + " at " + At(k.WorstAll) + " overMaxPx=" + MaxOver(k) +
                          " partExitMaxPx=" + (k.WorstPart != null ? k.WorstPart.PartExit : -1) + " gapMaxPx=" + (k.WorstPart != null ? F1(k.WorstPart.GapPx) : "-") +
                          " peakOffset=" + k.PeakOffset + " lods body " + Join(k.BodyLods) + " weapon " + Join(k.WeaponLods) + (k.Notes.Count > 0 ? " notes: " + string.Join("; ", k.Notes) : ""));
            }

            _aimOn = false;
            float seconds = Time.realtimeSinceStartup - started;
            string outDir = System.Environment.GetEnvironmentVariable("DD_G1_OUT_DIR");
            string frameDir = System.Environment.GetEnvironmentVariable("DD_G1_FRAME_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = !string.IsNullOrEmpty(frameDir) ? frameDir : Path.Combine(Application.temporaryCachePath, "g1-td-gate");
            Directory.CreateDirectory(outDir);
            string json = Path.Combine(outDir, "g1-td-gate.json");
            File.WriteAllText(json, Json(seconds));
            File.WriteAllText(Path.Combine(outDir, "g1-td-gate.md"), Markdown(seconds));
            int frames = !string.IsNullOrEmpty(frameDir) ? KareleriYaz(frameDir) : 0;
            Debug.Log("[DD-TEST] r3.g1td done " + _kayit.Count + " weapons, " + seconds.ToString("F0", CultureInfo.InvariantCulture) + " s, snapshots render " + _fromRender +
                      " / coroutine " + _fromCoroutine + ", flipY " + _flipY + ", lod overrides " + _lodOverrides + " → " + json + (frames > 0 ? ", " + frames + " PNG → " + frameDir : ", frames off (DD_G1_FRAME_DIR)"));

            // the record (S-10 R2 standard: the measured thing is in the record)
            string text = File.ReadAllText(json);
            var missing = new List<string>();
            foreach (Silah w in Silahlar)
            {
                Kayit k = _kayit[w.Key];
                Assert.IsTrue(text.Contains("\"key\": \"" + w.Key + "\""), w.Key + ": JSON kaydında yok");
                if (!k.Authored) missing.Add(w.Key + " yazılmış model yok");
                if (k.MinWeaponDraws == int.MaxValue || k.MinWeaponDraws <= 0) missing.Add(w.Key + " bir karede silah çizimi yok");
                if (k.WeaponVisTotal <= 0) missing.Add(w.Key + " TD'de hiç silah pikseli görünmedi");
                if (k.BodyTotal <= 0) missing.Add(w.Key + " gövde pikseli yok");
                foreach (string m in Moments)
                    for (int y = 0; y < YawCount; y++)
                        if (k.Samples[m][y] <= 0) missing.Add(w.Key + " " + m + " yaw " + (y * 45));
            }

            Assert.IsEmpty(missing, "ölçüm kurulamadı / kayıt eksik:\n" + string.Join("\n", missing));
            foreach (string key in PartWeapons)
            {
                Kayit k = _kayit[key];
                if (!k.PartInHandSeen) Debug.LogWarning("[DD-TEST] r3.g1td.part " + key + ": parça oyunda ele gelmiyor — el–parça ölçülemedi (" + (k.Special ? "Special: oyunda reload klibi / parça bağlama yok" : "klasik: part_mag ele bağlanmadı") + ")");
            }

            // the threshold (G1 = 0 px, K6 gap / exit = 0 px) is reported, not asserted (C-00 08.10: the gate decision is S-06's)
            var red = new List<string>();
            foreach (Kayit k in _kayit.Values)
            {
                if (k.WorstAll != null && k.WorstAll.Exit > 0) red.Add(k.Key + " G1 " + k.WorstAll.Exit + " px @ " + At(k.WorstAll));
                if (k.WorstPart != null && (k.WorstPart.PartExit > 0 || k.WorstPart.GapPx > 0f)) red.Add(k.Key + " K6 exit " + k.WorstPart.PartExit + " px gap " + F1(k.WorstPart.GapPx) + " px @ " + At(k.WorstPart));
            }

            if (red.Count > 0) Debug.LogWarning("[DD-TEST] r3.g1td UYARI (eşik 0 px, kapı kararı S-06): " + string.Join(" | ", red));
        }

        // ---------------------------------------------------------------------------------------------------- weapons

        private IEnumerator KlasikOlc(Silah w, Kayit k)
        {
            _yaw = 0f;
            Ctl.EquipClassic(w.Classic, _s.Config);
            _s.Rig.Motor.Teleport(_spot);
            _s.Girdi.Yon = Vector2.zero;
            yield return Kareler(70);
            k.Authored = Ctl.ClassicRigHasAuthoredModel;
            string key = Ctl.Classic.Definition != null ? Ctl.Classic.Definition.weaponId : "?";
            Assert.AreEqual(w.Key, key, "ölçüm kurulamadı: " + w.Classic + " beklenen silah değil");
            Transform rig = _mount.Find("ClassicWeaponRig");
            Assert.IsNotNull(rig, "ölçüm kurulamadı: ClassicWeaponRig yok");

            // the recoil's peak frame, learnt once from one shot
            yield return Dinlen(false, 4f, null);
            yield return TepeOgren(false, rig, k);
            yield return Doldur(false, null, k);

            for (int y = 0; y < YawCount; y++)
            {
                _yaw = y * 45f;
                var rest = new Meta();
                yield return Dinlen(false, 4f, rest);
                Iste(new Meta { Key = w.Key, Moment = "aim", Yaw = y, Phase = "rest", RestReached = rest.RestReached });
                yield return Kare();
                if (w.Key == "ar_1" && y == 0) Kontroller(rig);
                yield return AtisVeTepe(false, w.Key, y, k);
                yield return Dinlen(false, 4f, null);
                yield return Doldur(false, new Meta { Key = w.Key, Moment = "reload", Yaw = y }, k);
            }
        }

        private IEnumerator SpecialOlc(Silah w, Kayit k)
        {
            _yaw = 0f;
            _s.Rig.Motor.Teleport(_spot);
            _s.Girdi.Yon = Vector2.zero;
            // the kit caps a Special at its 11 s ActiveDuration; one activation here (pickup, aim, shot, a full reload) can pass it —
            // the cap is read at activation, on the test's config copy
            _s.Special.CapOverrideSeconds = 120f;
            bool learnt = false;
            for (int y = -1; y < YawCount; y++)
            {
                _yaw = Mathf.Max(0, y) * 45f;
                Ctl.ActivateSpecialWeapon(w.SpecialId);
                float t0 = Time.time;
                while (!Ctl.SpecialRigVisible && Time.time - t0 < 5f) yield return Kare();
                yield return Kareler(30);
                if (!Ctl.SpecialWeaponActive || !Ctl.SpecialRigVisible)
                {
                    k.Notes.Add("yaw " + (y * 45) + ": Special etkinleşmedi / görünmedi");
                    if (Ctl.SpecialWeaponActive) Ctl.DeactivateSpecialWeapon();
                    yield return Kareler(30);
                    continue;
                }

                k.Authored = Ctl.SpecialRigHasAuthoredModel;
                string key = Ctl.Special.Definition != null ? Ctl.Special.Definition.weaponId : "?";
                Assert.AreEqual(w.Key, key, "ölçüm kurulamadı: " + w.SpecialId + " beklenen Special değil");
                Transform rig = _mount.Find("SpecialWeaponRig");
                Assert.IsNotNull(rig, "ölçüm kurulamadı: SpecialWeaponRig yok");
                if (y < 0)
                {
                    yield return Dinlen(true, 3f, null);
                    yield return TepeOgren(true, rig, k);
                    learnt = true;
                }
                else
                {
                    var rest = new Meta();
                    yield return Dinlen(true, 3f, rest);
                    Iste(new Meta { Key = w.Key, Moment = "aim", Yaw = y, Phase = "rest", RestReached = rest.RestReached });
                    yield return Kare();
                    yield return AtisVeTepe(true, w.Key, y, k);
                    yield return Dinlen(true, 2f, null);
                    yield return Doldur(true, new Meta { Key = w.Key, Moment = "reload", Yaw = y }, k);
                }

                if (Ctl.SpecialWeaponActive) Ctl.DeactivateSpecialWeapon();
                yield return Kareler(40);
            }

            if (!learnt) k.Notes.Add("tepme tepesi öğrenilemedi");
            k.Notes.Add("Special: oyunda reload klibi yok (WeaponController reload sunumu yalnız klasik) — 'reload' anı reload sırasındaki nişan pozu");
        }

        /// <summary>Waits for the aim rest: facing within 3° of the yaw, the hold at full weight (classic), not reloading.</summary>
        private IEnumerator Dinlen(bool special, float seconds, Meta into)
        {
            float t0 = Time.time;
            bool ok = false;
            while (Time.time - t0 < seconds)
            {
                yield return Kare();
                bool reloading = special ? Ctl.Special.IsReloading : Ctl.Classic.IsReloading;
                WeaponHoldIk hold = _body.HoldIk;
                bool held = special || hold == null || !WeaponHoldIk.Enabled || hold.Weight >= 0.999f;
                if (FacingOk() && held && !reloading)
                {
                    ok = true;
                    break;
                }
            }

            yield return Kareler(8);
            if (into != null) into.RestReached = ok;
        }

        private bool FacingOk()
        {
            Vector3 f = Vector3.ProjectOnPlane(_s.Rig.Aim.transform.forward, Vector3.up);
            return Vector3.Angle(f, YawDir()) <= 3f;
        }

        private IEnumerator Ates(bool special)
        {
            int serial = Ctl.ShotSerial, ammo = special ? Ctl.Special.Ammo : 0;
            _s.Girdi.AtesBasti = true;
            _s.Girdi.AtesBasili = true;
            for (int i = 0; i < 90; i++)
            {
                yield return Kare();
                if (!special) _s.Girdi.AtesBasti = false;
                if (special ? Ctl.Special.Ammo < ammo : Ctl.ShotSerial != serial) break;
            }

            _s.Girdi.AtesBasti = false;
            _s.Girdi.AtesBasili = false;
            _atesOldu = special ? Ctl.Special.Ammo < ammo : Ctl.ShotSerial != serial;
        }

        private bool _atesOldu;

        /// <summary>One shot; the frame (after the shot frame) whose drawn pose has the largest muzzle / model travel in the chest frame.</summary>
        private IEnumerator TepeOgren(bool special, Transform rig, Kayit k)
        {
            Transform chest = _body.Animator.GetBoneTransform(HumanBodyBones.Chest);
            Transform model = special ? Ctl.SpecialRigBody : rig;
            if (chest == null || model == null)
            {
                k.PeakOffset = 3;
                k.Notes.Add("tepe öğrenme: göğüs / model yok, ofset 3");
                yield break;
            }

            Vector3 restM = InChest(chest, Ctl.Muzzle.position), restB = InChest(chest, model.position);
            yield return Ates(special);
            if (!_atesOldu)
            {
                k.PeakOffset = 3;
                k.Notes.Add("tepe öğrenme: atış olmadı, ofset 3");
                yield break;
            }

            float best = 0f;
            int bestJ = 1;
            for (int j = 1; j <= 24; j++)
            {
                yield return Kare();
                // read at step j: the pose frame (shot + j − 1) drew
                float d = Mathf.Max(Vector3.Distance(InChest(chest, Ctl.Muzzle.position), restM), Vector3.Distance(InChest(chest, model.position), restB));
                if (d > best)
                {
                    best = d;
                    bestJ = j;
                }
            }

            k.PeakOffset = best > 0.001f ? bestJ - 1 : 3;
            k.PeakMm = best * 1000f;
            if (best <= 0.001f) k.Notes.Add("tepme ≤ 1 mm: tepe ofseti 3 kare (KareM1 gibi)");
        }

        private IEnumerator AtisVeTepe(bool special, string key, int y, Kayit k)
        {
            yield return Ates(special);
            if (!_atesOldu)
            {
                k.Notes.Add("yaw " + (y * 45) + ": atış olmadı — tepme karesi yok");
                yield break;
            }

            int p = Mathf.Max(0, k.PeakOffset);
            for (int j = 0; j <= p; j++)
            {
                if (j == p) Iste(new Meta { Key = key, Moment = "recoil", Yaw = y, Phase = "shot+" + p });
                yield return Kare();
            }
        }

        /// <summary>One reload; with <paramref name="meta"/> the grasp window is sampled (every 3rd / 4th frame).</summary>
        private IEnumerator Doldur(bool special, Meta meta, Kayit k)
        {
            bool started;
            if (special) started = Ctl.Special.TryBeginReload();
            else
            {
                ClassicWeaponRuntime c = Ctl.Classic;
                if (!c.ReserveInfinite && c.ReserveAmmo <= 0) c.RefillReserveForRound(1);
                started = c.TryBeginReload();
            }

            if (!started)
            {
                if (meta != null) k.Notes.Add("yaw " + (meta.Yaw * 45) + ": reload başlamadı");
                yield break;
            }

            yield return Kare();   // the reload presentation (the ownership's Begin) runs in the next Update
            bool magazine = !special && Ctl.Magazine.Active;
            int stride = special ? SpecialReloadStride : ClassicReloadStride, inWindow = 0, frame = 0;
            float t0 = Time.time;
            while ((special ? Ctl.Special.IsReloading : Ctl.Classic.IsReloading) && Time.time - t0 < 15f)
            {
                frame++;
                bool window;
                string phase;
                if (magazine)
                {
                    window = Ctl.Magazine.Owner != MagazineOwner.Weapon;
                    phase = Ctl.Magazine.Owner.ToString();
                }
                else
                {
                    float pr = special ? Ctl.Special.ReloadProgress01 : Ctl.Classic.ReloadProgress01;
                    window = pr >= 0.3f && pr <= 0.7f;
                    phase = "p" + pr.ToString("F2", CultureInfo.InvariantCulture);
                }

                if (meta != null && window && inWindow++ % stride == 0)
                    Iste(new Meta { Key = meta.Key, Moment = "reload", Yaw = meta.Yaw, Frame = frame, Phase = phase, PartInHand = !special && Ctl.MagazineVisual.InHand });
                yield return Kare();
            }

            if (meta != null && inWindow == 0) k.Notes.Add("yaw " + (meta.Yaw * 45) + ": reload penceresi bulunamadı");
            yield return Kareler(10);
        }

        // ---------------------------------------------------------------------------------------------------- frames, aim

        private Vector3 YawDir() => Quaternion.AngleAxis(_yaw, Vector3.up) * _upDir;

        private void Nisan()
        {
            if (!_aimOn || _cam == null || _s == null || _s.Rig == null) return;
            Vector3 p = _s.Rig.transform.position + YawDir() * AimDistance;
            p.y = _s.Rig.Aim.AimPlaneY;
            Vector3 sp = _cam.WorldToScreenPoint(p);
            _s.Girdi.Imlec = new Vector2(sp.x, sp.y);
        }

        private IEnumerator Kare()
        {
            Nisan();
            yield return null;
            Servis();
        }

        private IEnumerator Kareler(int n)
        {
            for (int i = 0; i < n; i++) yield return Kare();
        }

        private void Iste(Meta m)
        {
            _istek = m;
            _yakalanan = null;
        }

        // the frame's state as that frame draws it (after LateUpdate): copied when the game camera starts rendering
        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_istek == null || _yakalanan != null || camera == null || camera != _cam) return;
            _yakalanan = Capture(_istek, "render");
            _istek = null;
            _fromRender++;
        }

        private void Servis()
        {
            if (_yakalanan == null && _istek != null)
            {
                // the pipeline's callback did not come (no SRP, camera off): the coroutine's state, one frame's Update later
                _yakalanan = Capture(_istek, "coroutine");
                _fromCoroutine++;
            }

            _istek = null;
            if (_yakalanan == null) return;
            Snap s = _yakalanan;
            _yakalanan = null;
            Isle(s);
        }

        private void Isle(Snap s)
        {
            Result r = Process(s);
            Kayit k = _kayit[s.Meta.Key];
            int y = s.Meta.Yaw;
            k.Samples[s.Meta.Moment][y]++;
            k.MinWeaponDraws = Mathf.Min(k.MinWeaponDraws, r.WeaponDraws);
            k.WeaponVisTotal += r.WeaponVis;
            k.BodyTotal += r.Body;
            k.BodyLods.Add(r.BodyLod);
            k.WeaponLods.Add(r.WeaponLod);
            k.MmPerPx = r.MmPerPx;
            Result[] worst = k.Worst[s.Meta.Moment];
            if (worst[y] == null || r.Score > worst[y].Score) worst[y] = r;
            if (k.WorstAll == null || r.Score > k.WorstAll.Score)
            {
                k.WorstAll = r;
                k.Evidence = Kanitla(s, r, k.Key + " " + s.Meta.Moment + " yaw " + (y * 45));
            }

            if (s.Meta.Moment == "reload" && r.PartDraws > 0 && s.Meta.PartInHand)
            {
                k.PartInHandSeen = true;
                k.PartSamples[y]++;
                if (k.PartWorst[y] == null || r.PartScore > k.PartWorst[y].PartScore) k.PartWorst[y] = r;
                if (k.WorstPart == null || r.PartScore > k.WorstPart.PartScore)
                {
                    k.WorstPart = r;
                    k.PartEvidence = Kanitla(s, r, k.Key + " reload part yaw " + (y * 45));
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------- controls

        private void Kontroller(Transform rig)
        {
            Snap s0 = Capture(new Meta { Key = "ar_1", Moment = "aim", Yaw = 0, Phase = "control-base" }, "coroutine");
            Result r0 = Process(s0);
            Assert.Greater(r0.Body, 0, "ölçüm kurulamadı: kontrol karesinde gövde pikseli yok");
            Assert.Greater(r0.WeaponDraws, 0, "ölçüm kurulamadı: kontrol karesinde silah çizimi yok");
            // the right plate's top under the camera: the plate pixel nearest the right upper arm's projection
            Vector2 sh = ToPixel(s0.HasBone[BRUpperArm] ? s0.Bone[BRUpperArm] : s0.Bone[BChest], s0);
            int w = r0.Roi.width, best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _region.Length; i++)
            {
                if (_region[i] != RPlateR) continue;
                float dx = r0.Roi.x + i % w - sh.x, dy = r0.Roi.y + i / w - sh.y, d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            Assert.GreaterOrEqual(best, 0, "pozitif kontrol kurulamadı: TD'de sağ omuz plakası pikseli yok");
            Vector3 surface = Unproject(r0.Roi.x + best % w, r0.Roi.y + best / w, _depth[best], s0);
            Vector3 n = (s0.CamPos - surface).normalized;
            // the weapon's corners along n (its renderers' local bounds), and its centre
            var b = new Bounds();
            bool any = false;
            float low = float.MaxValue;
            foreach (Draw d in s0.Draws)
            {
                if (d.Kind == 0) continue;
                foreach (Vector3 c in Corners(d.Mesh.bounds, d.M))
                {
                    if (!any)
                    {
                        b = new Bounds(c, Vector3.zero);
                        any = true;
                    }
                    else b.Encapsulate(c);
                }
            }

            Assert.IsTrue(any, "pozitif kontrol kurulamadı: silah çizimi yok");
            foreach (Draw d in s0.Draws)
                if (d.Kind != 0)
                    foreach (Vector3 c in Corners(d.Mesh.bounds, d.M))
                        low = Mathf.Min(low, Vector3.Dot(c - b.center, n));
            Vector3 home = rig.localPosition;
            try
            {
                rig.position += surface + n * (-EmbedMetres - low) - b.center;
                Snap s1 = Capture(new Meta { Key = "ar_1", Moment = "aim", Yaw = 0, Phase = "control+5cm" }, "coroutine");
                _ctlPos = Process(s1);
                _ctlPosEv = Kanitla(s1, _ctlPos, "kontrol + (5 cm gömülü)");
                rig.localPosition = home;
                rig.position += surface + n * (LiftMetres - low) - b.center;
                Snap s2 = Capture(new Meta { Key = "ar_1", Moment = "aim", Yaw = 0, Phase = "control-lift" }, "coroutine");
                _ctlNeg = Process(s2);
                _ctlNegEv = Kanitla(s2, _ctlNeg, "kontrol − (60 cm üstte)");
            }
            finally
            {
                rig.localPosition = home;
            }

            _ctlNote = "plate_r point " + V(surface) + ", camera ray " + V(n) + ", weapon lowest corner " + (low * 1000f).ToString("F0", CultureInfo.InvariantCulture) + " mm from its centre";
            Debug.Log("[DD-TEST] r3.g1td.control positive exit " + _ctlPos.Exit + " px (over " + _ctlPos.Over + ") negative exit " + _ctlNeg.Exit + " px over " + _ctlNeg.Over + " px :: " + _ctlNote);
            Assert.Greater(_ctlPos.Exit, 0, "pozitif kontrol: 5 cm gömülü silahta çıkış okunmadı — ölçüm çıkışı göremiyor");
            Assert.AreEqual(0, _ctlNeg.Exit, "negatif kontrol: gövdenin 60 cm üstündeki silah çıkış sayıldı");
            Assert.Greater(_ctlNeg.Over, 0, "negatif kontrol: gövdenin üstündeki silah 'üstte' sayılmadı");
        }

        private float[] _depth = new float[0];

        // ---------------------------------------------------------------------------------------------------- capture

        private static bool Drawable(Renderer r) =>
            r != null && r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly &&
            (r is MeshRenderer || r is SkinnedMeshRenderer);

        private Snap Capture(Meta meta, string source)
        {
            var s = new Snap { Meta = meta, Source = source };
            Camera cam = _cam;
            s.View = cam.worldToCameraMatrix;
            s.Proj = cam.projectionMatrix;
            s.H = ProbeHeight;
            s.W = Mathf.Max(64, Mathf.RoundToInt(ProbeHeight * cam.aspect));
            s.CamPos = cam.transform.position;
            s.BodyForward = Vector3.ProjectOnPlane(_body.transform.forward, Vector3.up).normalized;
            Animator a = _body.Animator;
            for (int i = 0; i < Bones.Length; i++)
            {
                Transform t = a.GetBoneTransform(Bones[i]);
                s.HasBone[i] = t != null;
                if (t != null) s.Bone[i] = t.position;
            }

            // the body: the LOD the game camera draws
            LODGroup group = BodyLodGroup();
            HashSet<Renderer> bodyLod = LodRenderers(group, cam, out s.BodyLod);
            foreach (SkinnedMeshRenderer r in _body.Renderers)
            {
                if (!Drawable(r) || r.sharedMesh == null) continue;
                if (bodyLod != null && InGroup(group, r) && !bodyLod.Contains(r)) continue;
                Add(s, r, 0);
            }

            // the held weapon (the rig shown) and its magazine part wherever it is
            var weapon = new List<Renderer>();
            Ctl.CollectWeaponRenderers(weapon);
            Transform part = Ctl.MagazineVisual != null ? Ctl.MagazineVisual.Part : null;
            if (part != null)
                foreach (Renderer r in part.GetComponentsInChildren<Renderer>(true))
                    if (!weapon.Contains(r)) weapon.Add(r);
            var groups = new Dictionary<LODGroup, HashSet<Renderer>>();
            Transform rigRoot = Ctl.SpecialWeaponActive ? _mount.Find("SpecialWeaponRig") : _mount.Find("ClassicWeaponRig");
            if (rigRoot != null)
                foreach (LODGroup g in rigRoot.GetComponentsInChildren<LODGroup>(true))
                {
                    groups[g] = LodRenderers(g, cam, out int lod);
                    s.WeaponLod = Mathf.Max(s.WeaponLod, lod);
                }

            foreach (Renderer r in weapon)
            {
                if (!Drawable(r)) continue;
                bool skip = false;
                foreach (KeyValuePair<LODGroup, HashSet<Renderer>> g in groups)
                    if (InGroup(g.Key, r) && g.Value != null && !g.Value.Contains(r)) skip = true;
                if (skip) continue;
                Add(s, r, part != null && r.transform.IsChildOf(part) ? 2 : 1);
            }

            return s;
        }

        private LODGroup BodyLodGroup()
        {
            LODGroup g = _body.LodGroup;
            if (g == null) g = _body.GetComponentInChildren<LODGroup>(true);
            if (g == null) g = _body.GetComponentInParent<LODGroup>();
            return g;
        }

        private void Add(Snap s, Renderer r, int kind)
        {
            Mesh mesh;
            Matrix4x4 m;
            if (r is SkinnedMeshRenderer smr)
            {
                if (smr.sharedMesh == null) return;
                if (!_bakePool.TryGetValue(r, out mesh) || mesh == null)
                {
                    mesh = new Mesh { name = "R3TdKare_bake_" + r.name };
                    mesh.MarkDynamic();
                    _bakePool[r] = mesh;
                }

                smr.BakeMesh(mesh);
                mesh.RecalculateBounds();
                m = BakeMatrix(smr, mesh);
            }
            else
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                mesh = f != null ? f.sharedMesh : null;
                if (mesh == null) return;
                m = r.transform.localToWorldMatrix;
            }

            s.Draws.Add(new Draw { Mesh = mesh, M = m, Kind = kind });
        }

        // BakeMesh's vertices are in the renderer's local space; which matrix carries them to the world is decided once per renderer
        // (a unit scale needs no decision) by the world size the renderer itself reports
        private Matrix4x4 BakeMatrix(SkinnedMeshRenderer r, Mesh baked)
        {
            Transform t = r.transform;
            Vector3 ls = t.lossyScale;
            if (Mathf.Abs(ls.x - 1f) < 1e-3f && Mathf.Abs(ls.y - 1f) < 1e-3f && Mathf.Abs(ls.z - 1f) < 1e-3f) return t.localToWorldMatrix;
            Matrix4x4 local = t.localToWorldMatrix, unscaled = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            if (!_bakeWorldScale.TryGetValue(r, out bool world))
            {
                float target = r.bounds.size.magnitude;
                float a = Size(baked.bounds, local), b = Size(baked.bounds, unscaled);
                world = Mathf.Abs(b - target) < Mathf.Abs(a - target);
                _bakeWorldScale[r] = world;
                Debug.Log("[DD-TEST] r3.g1td.bake " + r.name + " lossyScale " + V(ls) + " → " + (world ? "TRS(pos, rot, 1)" : "localToWorld") + " (sizes " + F1(a) + " / " + F1(b) + " vs " + F1(target) + ")");
            }

            return world ? unscaled : local;
        }

        private static float Size(Bounds b, Matrix4x4 m)
        {
            var w = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            foreach (Vector3 c in Corners(b, m)) w.Encapsulate(c);
            return w.size.magnitude;
        }

        private static bool InGroup(LODGroup g, Renderer r)
        {
            if (g == null) return false;
            foreach (LOD l in g.GetLODs())
                foreach (Renderer x in l.renderers)
                    if (x == r) return true;
            return false;
        }

        /// <summary>The renderers of the LOD <paramref name="g"/> shows to <paramref name="cam"/>: Unity's relative-height rule
        /// (LODGroup size × largest scale, lodBias, maximumLODLevel), cross-checked against Renderer.isVisible of the last frame.</summary>
        private HashSet<Renderer> LodRenderers(LODGroup g, Camera cam, out int index)
        {
            index = -1;
            if (g == null || !g.enabled) return null;
            LOD[] lods = g.GetLODs();
            if (lods.Length == 0) return null;
            Vector3 ls = g.transform.lossyScale;
            float size = g.size * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)), rel;
            if (cam.orthographic) rel = size * 0.5f / Mathf.Max(1e-4f, cam.orthographicSize);
            else
            {
                float d = Vector3.Distance(g.transform.TransformPoint(g.localReferencePoint), cam.transform.position) / Mathf.Max(1e-4f, QualitySettings.lodBias);
                rel = size * 0.5f / Mathf.Max(1e-4f, d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            }

            index = lods.Length;
            for (int i = 0; i < lods.Length; i++)
                if (rel >= lods[i].screenRelativeTransitionHeight)
                {
                    index = i;
                    break;
                }

            index = Mathf.Max(index, QualitySettings.maximumLODLevel);
            int visible = -1, visibleCount = 0;
            for (int i = 0; i < lods.Length; i++)
            {
                bool v = false;
                foreach (Renderer r in lods[i].renderers) v |= r != null && r.isVisible;
                if (!v) continue;
                visible = i;
                visibleCount++;
            }

            bool chosenVisible = false;
            if (index < lods.Length)
                foreach (Renderer r in lods[index].renderers) chosenVisible |= r != null && r.isVisible;
            if (!chosenVisible && visibleCount == 1)
            {
                index = visible;   // the game drew another LOD (a ForceLOD, a bias the rule above misses)
                _lodOverrides++;
            }

            var set = new HashSet<Renderer>();
            if (index < lods.Length)
                foreach (Renderer r in lods[index].renderers)
                    if (r != null) set.Add(r);
            return set;
        }

        // ---------------------------------------------------------------------------------------------------- probe

        private void KurProbe()
        {
#if UNITY_EDITOR
            _shader = UnityEditor.ShaderUtil.CreateShaderAsset(ProbeShaderSource, true);
            Assert.IsNotNull(_shader, "ölçüm kurulamadı: prob shader'ı oluşmadı");
            Assert.IsFalse(UnityEditor.ShaderUtil.ShaderHasError(_shader), "ölçüm kurulamadı: prob shader'ı derlenmedi");
#else
            Assert.Fail("ölçüm kurulamadı: prob shader'ı yalnız editörde (ShaderUtil) kurulur");
#endif
            Assert.IsTrue(_shader.isSupported, "ölçüm kurulamadı: prob shader'ı bu aygıtta desteklenmiyor");
            // every pass draws both faces: the probe's flipped projection (GL.GetGPUProjectionMatrix into a texture) reverses the
            // winding, so the nearest surface is taken by depth alone, and the face counts below are read sign-free
            _mBody = Mat(1f, 0f, CompareFunction.LessEqual, true, ColorWriteMask.All);
            _mBodyDepth = Mat(0f, 0f, CompareFunction.LessEqual, true, 0);
            _mBehindW = Mat(1f, 1f, CompareFunction.Greater, false, ColorWriteMask.Red);
            _mBehindP = Mat(1f, 1f, CompareFunction.Greater, false, ColorWriteMask.Green);
            _mFrontW = Mat(1f, 0f, CompareFunction.LessEqual, true, ColorWriteMask.All);
            _mFrontP = Mat(0f, 1f, CompareFunction.LessEqual, true, ColorWriteMask.All);
            // face counts of the weapon (RG) / part (BA) surfaces strictly in front of the body's surface, added (half float)
            _mCountW = Mat(0f, 0f, CompareFunction.Less, false, ColorWriteMask.All, true, 0f);
            _mCountP = Mat(0f, 0f, CompareFunction.Less, false, ColorWriteMask.All, true, 1f);
            _cmd = new CommandBuffer { name = "R3TdKareProbe" };
        }

        private Material Mat(float r, float g, CompareFunction z, bool zWrite, ColorWriteMask mask, bool count = false, float pair = 0f)
        {
            var m = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };
            m.SetVector("_Out", new Vector4(r, g, count ? 1f : 0f, pair));
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_ZTest", (float)z);
            m.SetFloat("_ZWrite", zWrite ? 1f : 0f);
            m.SetFloat("_ColorMask", (float)mask);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)(count ? BlendMode.One : BlendMode.Zero));
            return m;
        }

        private void EnsureTargets(int w, int h)
        {
            if (_rtA != null && _rtA.width == w && _rtA.height == h) return;
            Release(ref _rtA);
            Release(ref _rtB);
            Release(ref _rtC);
            Release(ref _rtD);
            Release(ref _rtColor);
            _rtA = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBFloat) { name = "R3TdKare_A" };
            _rtB = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBFloat) { name = "R3TdKare_B" };
            _rtC = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBFloat) { name = "R3TdKare_C" };
            _rtD = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf) { name = "R3TdKare_D" };
            _rtColor = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "R3TdKare_Color" };
            _rtA.Create();
            _rtB.Create();
            _rtC.Create();
            _rtD.Create();
            _rtColor.Create();
        }

        private void Pass(RenderTexture target, bool clearDepth, Snap s, Material body, Material weapon, Material part)
        {
            _cmd.Clear();
#if UNITY_EDITOR
            UnityEditor.ShaderUtil.SetAsyncCompilation(_cmd, false);
#endif
            _cmd.SetGlobalMatrix("_DDView", s.View);
            _cmd.SetGlobalMatrix("_DDProj", GL.GetGPUProjectionMatrix(s.Proj, true));
            _cmd.SetRenderTarget(target);
            _cmd.ClearRenderTarget(clearDepth, true, Color.clear, 1f);
            for (int kind = 0; kind < 3; kind++)   // the body first: its depth is what the weapon passes test against
            {
                Material m = kind == 0 ? body : kind == 1 ? weapon : part;
                if (m == null) continue;
                foreach (Draw d in s.Draws)
                {
                    if (d.Kind != kind) continue;
                    for (int sub = 0; sub < d.Mesh.subMeshCount; sub++) _cmd.DrawMesh(d.Mesh, d.M, m, sub, 0);
                }
            }

            Graphics.ExecuteCommandBuffer(_cmd);
        }

        private Color[] Read(RenderTexture rt, RectInt roi, int H)
        {
            bool half = rt.format == RenderTextureFormat.ARGBHalf;
            Texture2D t = half ? _readH : _readF;
            if (t == null || t.width != roi.width || t.height != roi.height)
            {
                if (t != null) Object.Destroy(t);
                t = new Texture2D(roi.width, roi.height, half ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat, false, true);
                if (half) _readH = t;
                else _readF = t;
            }

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            int y = _flipY ? H - roi.yMax : roi.y;
            t.ReadPixels(new Rect(roi.x, y, roi.width, roi.height), 0, 0, false);
            RenderTexture.active = prev;
            Color[] px = t.GetPixels();
            if (_flipY)
            {
                int w = roi.width;
                for (int row = 0; row < roi.height / 2; row++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = row * w + x, j = (roi.height - 1 - row) * w + x;
                        Color swap = px[i];
                        px[i] = px[j];
                        px[j] = swap;
                    }
            }

            return px;
        }

        private Result Process(Snap s)
        {
            EnsureTargets(s.W, s.H);
            var r = new Result { Meta = s.Meta, Source = s.Source, BodyLod = s.BodyLod, WeaponLod = s.WeaponLod };
            foreach (Draw d in s.Draws)
            {
                if (d.Kind == 1) r.WeaponDraws++;
                if (d.Kind == 2) r.PartDraws++;
            }

            // the region of interest: every drawn mesh's bounds, projected, + 6 px
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (Draw d in s.Draws)
                foreach (Vector3 c in Corners(d.Mesh.bounds, d.M))
                {
                    Vector2 p = ToPixel(c, s);
                    x0 = Mathf.Min(x0, p.x);
                    y0 = Mathf.Min(y0, p.y);
                    x1 = Mathf.Max(x1, p.x);
                    y1 = Mathf.Max(y1, p.y);
                }

            int rx0 = Mathf.Clamp(Mathf.FloorToInt(x0) - 6, 0, s.W - 1), ry0 = Mathf.Clamp(Mathf.FloorToInt(y0) - 6, 0, s.H - 1);
            int rx1 = Mathf.Clamp(Mathf.CeilToInt(x1) + 6, rx0 + 1, s.W), ry1 = Mathf.Clamp(Mathf.CeilToInt(y1) + 6, ry0 + 1, s.H);
            r.Roi = new RectInt(rx0, ry0, rx1 - rx0, ry1 - ry0);
            Assert.Greater(r.Roi.width * r.Roi.height, 16, "ölçüm kurulamadı: ilgi alanı boş (" + s.Meta.Key + ")");

            Pass(_rtA, true, s, _mBody, null, null);   // A: the body's nearest surface (flag, eye depth)
            if (!_flipKnown) Yonelim(s);
            Color[] a = Read(_rtA, r.Roi, s.H);
            Pass(_rtA, false, s, null, _mBehindW, _mBehindP);   // A': keeps A's depth — only weapon / part fragments behind the body's surface pass
            Color[] behind = Read(_rtA, r.Roi, s.H);
            Pass(_rtB, true, s, null, _mFrontW, _mFrontP);   // B: the weapon's (and part's) nearest surface
            Color[] front = Read(_rtB, r.Roi, s.H);
            Color[] part = null;
            if (r.PartDraws > 0)
            {
                Pass(_rtC, true, s, null, null, _mFrontP);   // C: the part alone (its silhouette)
                part = Read(_rtC, r.Roi, s.H);
            }

            // D: the weapon's / part's front- and back-facing surfaces in front of the body's surface, counted — a closed solid's
            // counts differ by one exactly when the body's surface point is inside it
            Pass(_rtD, true, s, _mBodyDepth, _mCountW, _mCountP);
            Color[] count = Read(_rtD, r.Roi, s.H);

            int n = r.Roi.width * r.Roi.height;
            if (_flags.Length != n)
            {
                _flags = new byte[n];
                _region = new byte[n];
                _depth = new float[n];
            }

            Segments(s);
            Matrix4x4 inv = s.View.inverse;
            var handL = new List<int>();
            var partPx = new List<int>();
            for (int i = 0; i < n; i++)
            {
                byte f = 0;
                int reg = RNone;
                bool body = a[i].r > 0.5f;
                float dB = a[i].b;
                _depth[i] = body ? dB : 0f;
                bool wFront = front[i].r > 0.5f || front[i].g > 0.5f;
                float dW = front[i].b;
                bool visible = wFront && (!body || dW < dB);
                if (body)
                {
                    f |= FBody;
                    r.Body++;
                    reg = Classify(UnprojectView(r.Roi.x + i % r.Roi.width, r.Roi.y + i / r.Roi.width, dB, s, inv), s);
                    if (reg == RHandL) handL.Add(i);
                }

                if (visible)
                {
                    f |= FWeapon;
                    r.WeaponVis++;
                    if (body && IsG1(reg))
                    {
                        bool inside = Mathf.Abs(count[i].r - count[i].g) > 0.5f || Mathf.Abs(count[i].b - count[i].a) > 0.5f;
                        bool crosses = inside && (behind[i].r > 0.5f || behind[i].g > 0.5f);
                        if (crosses)
                        {
                            f |= FExit;
                            r.Exit++;
                            r.ExitBy[reg]++;
                            r.ExitMm = Mathf.Max(r.ExitMm, (dB - dW) * 1000f);
                        }
                        else
                        {
                            f |= FOver;
                            r.Over++;
                        }
                    }
                }

                if (part != null && part[i].g > 0.5f)
                {
                    f |= FPartSil;
                    r.PartSil++;
                    partPx.Add(i);
                    float dP = part[i].b;
                    bool pVis = (!body || dP < dB) && (!wFront || dP <= dW + 1e-4f);
                    if (pVis)
                    {
                        f |= FPartVis;
                        r.PartVis++;
                    }

                    if (body && (reg == RHandL || reg == RForearmL) && dP < dB && behind[i].g > 0.5f && Mathf.Abs(count[i].b - count[i].a) > 0.5f)
                    {
                        f |= FPartExit;
                        r.PartExit++;
                    }
                }

                _flags[i] = f;
                _region[i] = (byte)reg;
            }

            r.HandL = handL.Count;
            if (partPx.Count > 0 && handL.Count > 0)
            {
                // the gap between the part's and the left hand's silhouettes: centre distance − 1 (0 = touching or overlapping)
                int w = r.Roi.width;
                float best = float.MaxValue;
                foreach (int p in partPx)
                {
                    int px = p % w, py = p / w;
                    foreach (int h in handL)
                    {
                        float dx = h % w - px, dy = h / w - py, d = dx * dx + dy * dy;
                        if (d < best) best = d;
                    }

                    if (best <= 2f) break;
                }

                r.GapPx = Mathf.Max(0f, Mathf.Sqrt(best) - 1f);
                if (best <= 2f) r.GapPx = 0f;
            }

            // mm per pixel at the chest
            Vector3 chest = s.HasBone[BChest] ? s.Bone[BChest] : s.Bone[BHips];
            float eye = -s.View.MultiplyPoint3x4(chest).z;
            bool ortho = Mathf.Abs(s.Proj.m33 - 1f) < 1e-4f;
            r.MmPerPx = (ortho ? 2f / s.Proj.m11 : 2f * eye / s.Proj.m11) / s.H * 1000f;
            return r;
        }

        // the frame's orientation in the probe target, once: the head's projected pixel must land on the body
        private void Yonelim(Snap s)
        {
            Vector3 headW = s.HasBone[BHead] ? s.Bone[BHead] : s.Bone[BChest];
            Vector2 p = ToPixel(headW, s);
            int x = Mathf.Clamp(Mathf.RoundToInt(p.x) - 3, 0, s.W - 7), y = Mathf.Clamp(Mathf.RoundToInt(p.y) - 3, 0, s.H - 7);
            var roi = new RectInt(x, y, 7, 7);
            _flipY = false;
            int straight = 0, flipped = 0;
            foreach (Color c in Read(_rtA, roi, s.H)) if (c.r > 0.5f) straight++;
            _flipY = true;
            foreach (Color c in Read(_rtA, roi, s.H)) if (c.r > 0.5f) flipped++;
            Assert.IsTrue(straight > 0 || flipped > 0, "ölçüm kurulamadı: başın izdüşümünde gövde pikseli yok (prob yönelimi / matrisler)");
            _flipY = flipped > straight;
            _flipKnown = true;
            Debug.Log("[DD-TEST] r3.g1td.orientation head px " + V2(p) + " straight " + straight + "/49 flipped " + flipped + "/49 → flipY " + _flipY);
        }

        // ---------------------------------------------------------------------------------------------------- geometry

        private struct Seg
        {
            public Vector3 A, B;
            public float R;
            public int Region;   // −1 torso (front / back by the body's forward)
        }

        private readonly List<Seg> _segs = new List<Seg>(32);

        private void Segments(Snap s)
        {
            _segs.Clear();
            Vector3 B(int i) => s.Bone[i];
            bool H(int i) => s.HasBone[i];
            void S(int a, int b, float r, int region)
            {
                if (H(a) && H(b)) _segs.Add(new Seg { A = B(a), B = B(b), R = r, Region = region });
            }

            int top = H(BUpperChest) ? BUpperChest : BChest;
            S(BHips, BSpine, 0.15f, -1);
            S(BSpine, BChest, 0.15f, -1);
            if (H(BUpperChest)) S(BChest, BUpperChest, 0.15f, -1);
            S(top, BNeck, 0.13f, -1);
            S(BNeck, BHead, 0.08f, RCollar);
            if (H(BHead) && H(BNeck))
            {
                Vector3 up = (B(BHead) - B(BNeck)).normalized;
                _segs.Add(new Seg { A = B(BHead), B = B(BHead) + up * 0.16f, R = 0.12f, Region = RHood });
            }

            if (H(BRShoulder)) S(BRShoulder, BRUpperArm, 0.09f, RPlateR);
            else S(top, BRUpperArm, 0.09f, RPlateR);
            if (H(BLShoulder)) S(BLShoulder, BLUpperArm, 0.09f, RPlateL);
            else S(top, BLUpperArm, 0.09f, RPlateL);
            S(BRUpperArm, BRLowerArm, 0.065f, RUpperArmR);
            S(BLUpperArm, BLLowerArm, 0.065f, RUpperArmL);
            S(BRLowerArm, BRHand, 0.055f, RForearmR);
            S(BLLowerArm, BLHand, 0.055f, RForearmL);
            Hand(s, BRHand, BRMiddle, BRLowerArm, RHandR);
            Hand(s, BLHand, BLMiddle, BLLowerArm, RHandL);
            S(BHips, BRUpperLeg, 0.12f, RLower);
            S(BHips, BLUpperLeg, 0.12f, RLower);
            S(BRUpperLeg, BRLowerLeg, 0.09f, RLower);
            S(BLUpperLeg, BLLowerLeg, 0.09f, RLower);
            S(BRLowerLeg, BRFoot, 0.07f, RLower);
            S(BLLowerLeg, BLFoot, 0.07f, RLower);
        }

        private void Hand(Snap s, int hand, int middle, int lower, int region)
        {
            if (!s.HasBone[hand]) return;
            Vector3 h = s.Bone[hand];
            Vector3 tip = s.HasBone[middle] ? h + (s.Bone[middle] - h) * 1.8f : s.HasBone[lower] ? h + (h - s.Bone[lower]).normalized * 0.09f : h;
            _segs.Add(new Seg { A = h, B = tip, R = 0.05f, Region = region });
        }

        private int Classify(Vector3 p, Snap s)
        {
            float best = float.MaxValue;
            int region = RNone;
            foreach (Seg g in _segs)
            {
                Vector3 ab = g.B - g.A;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - g.A, ab) / ab.sqrMagnitude) : 0f;
                Vector3 q = g.A + ab * t;
                float d = Vector3.Distance(p, q) / g.R;
                if (d >= best) continue;
                best = d;
                region = g.Region >= 0 ? g.Region : Vector3.Dot(p - q, s.BodyForward) < 0f ? RTorsoBack : RTorsoFront;
            }

            return region;
        }

        private static Vector2 ToPixel(Vector3 w, Snap s)
        {
            Vector4 clip = s.Proj * (s.View * new Vector4(w.x, w.y, w.z, 1f));
            float iw = Mathf.Abs(clip.w) > 1e-6f ? 1f / clip.w : 1f;
            return new Vector2((clip.x * iw * 0.5f + 0.5f) * s.W, (clip.y * iw * 0.5f + 0.5f) * s.H);
        }

        private static Vector3 Unproject(int px, int py, float eye, Snap s) => UnprojectView(px, py, eye, s, s.View.inverse);

        // pixel + eye depth (view-space distance along −Z, what the probe writes) → world, with the snapshot's GL-style projection
        private static Vector3 UnprojectView(int px, int py, float eye, Snap s, Matrix4x4 inv)
        {
            float nx = (px + 0.5f) / s.W * 2f - 1f, ny = (py + 0.5f) / s.H * 2f - 1f;
            Matrix4x4 p = s.Proj;
            float x, y;
            if (Mathf.Abs(p.m33 - 1f) < 1e-4f)
            {
                x = (nx - p.m03) / p.m00;
                y = (ny - p.m13) / p.m11;
            }
            else
            {
                x = eye * (nx + p.m02) / p.m00;
                y = eye * (ny + p.m12) / p.m11;
            }

            return inv.MultiplyPoint3x4(new Vector3(x, y, -eye));
        }

        private static Vector3[] Corners(Bounds b, Matrix4x4 m)
        {
            Vector3 c = b.center, e = b.extents;
            var o = new Vector3[8];
            for (int i = 0; i < 8; i++)
                o[i] = m.MultiplyPoint3x4(c + new Vector3((i & 1) != 0 ? e.x : -e.x, (i & 2) != 0 ? e.y : -e.y, (i & 4) != 0 ? e.z : -e.z));
            return o;
        }

        private static Vector3 InChest(Transform chest, Vector3 world) => Quaternion.Inverse(chest.rotation) * (world - chest.position);

        // ---------------------------------------------------------------------------------------------------- evidence

        private Kanit Kanitla(Snap s, Result r, string label)
        {
            var k = new Kanit { Label = label, W = r.Roi.width, H = r.Roi.height, Flags = (byte[])_flags.Clone(), Region = (byte[])_region.Clone() };
            // the game camera's own frame (lit), cropped to the same region — rendered now, one frame after the copied state
            Camera cam = _cam;
            RenderTexture prevTarget = cam.targetTexture, prev = RenderTexture.active;
            try
            {
                cam.targetTexture = _rtColor;
                cam.Render();
                if (_readC == null || _readC.width != k.W || _readC.height != k.H)
                {
                    if (_readC != null) Object.Destroy(_readC);
                    _readC = new Texture2D(k.W, k.H, TextureFormat.RGBA32, false);
                }

                RenderTexture.active = _rtColor;
                _readC.ReadPixels(new Rect(r.Roi.x, r.Roi.y, k.W, k.H), 0, 0, false);
                k.Color = _readC.GetPixels32();
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prev;
            }

            return k;
        }

        private static Color32 RegionColor(int r)
        {
            switch (r)
            {
                case RPlateR: case RPlateL: return new Color32(70, 110, 200, 255);
                case RUpperArmR: case RUpperArmL: return new Color32(70, 150, 190, 255);
                case RCollar: return new Color32(110, 90, 200, 255);
                case RHood: return new Color32(140, 110, 220, 255);
                case RTorsoBack: return new Color32(60, 130, 130, 255);
                case RTorsoFront: return new Color32(95, 95, 95, 255);
                case RForearmR: case RForearmL: case RHandR: return new Color32(120, 100, 80, 255);
                case RHandL: return new Color32(170, 125, 70, 255);
                default: return new Color32(70, 70, 70, 255);
            }
        }

        // [ the lit frame (exit pixels tinted) | false colour ], each half scaled to fit half × half
        private static Color32[] Tile(Kanit k, int half, out int tw, out int th)
        {
            tw = half * 2;
            th = half;
            var o = new Color32[tw * th];
            for (int i = 0; i < o.Length; i++) o[i] = new Color32(16, 16, 18, 255);
            if (k == null) return o;
            float scale = Mathf.Min((float)half / k.W, (float)half / k.H);
            int ox = (half - Mathf.FloorToInt(k.W * scale)) / 2, oy = (half - Mathf.FloorToInt(k.H * scale)) / 2;
            for (int y = 0; y < half; y++)
                for (int x = 0; x < half; x++)
                {
                    int sx = Mathf.FloorToInt((x - ox) / scale), sy = Mathf.FloorToInt((y - oy) / scale);
                    if (sx < 0 || sy < 0 || sx >= k.W || sy >= k.H) continue;
                    int i = sy * k.W + sx;
                    byte f = k.Flags[i];
                    Color32 lit = k.Color != null && i < k.Color.Length ? k.Color[i] : new Color32(0, 0, 0, 255);
                    if ((f & (FExit | FPartExit)) != 0) lit = Color32.Lerp(lit, new Color32(255, 0, 255, 255), 0.7f);
                    o[y * tw + x] = lit;
                    Color32 c = new Color32(24, 24, 28, 255);
                    if ((f & FBody) != 0) c = RegionColor(k.Region[i]);
                    if ((f & FPartSil) != 0 && (f & FPartVis) == 0) c = new Color32(0, 110, 110, 255);
                    if ((f & FWeapon) != 0) c = new Color32(215, 215, 215, 255);
                    if ((f & FPartVis) != 0) c = new Color32(0, 220, 220, 255);
                    if ((f & FOver) != 0) c = new Color32(255, 210, 0, 255);
                    if ((f & FExit) != 0) c = new Color32(255, 0, 255, 255);
                    if ((f & FPartExit) != 0) c = new Color32(255, 40, 40, 255);
                    o[y * tw + half + x] = c;
                }

            return o;
        }

        private static void Png(string file, Color32[] px, int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                t.SetPixels32(px);
                t.Apply(false);
                File.WriteAllBytes(file, t.EncodeToPNG());
            }
            finally
            {
                Object.Destroy(t);
            }
        }

        private int KareleriYaz(string dir)
        {
            Directory.CreateDirectory(dir);
            int written = 0;
            var sheet = new List<Kanit>();
            foreach (Silah w in Silahlar)
            {
                Kayit k = _kayit[w.Key];
                sheet.Add(k.Evidence);
                if (k.Evidence == null) continue;
                Color32[] t = Tile(k.Evidence, 512, out int tw, out int th);
                Png(Path.Combine(dir, "g1_" + w.Key + "_worst.png"), t, tw, th);
                written++;
            }

            foreach (string key in PartWeapons)
            {
                Kayit k = _kayit[key];
                sheet.Add(k.PartEvidence);
                if (k.PartEvidence == null) continue;
                Color32[] t = Tile(k.PartEvidence, 512, out int tw, out int th);
                Png(Path.Combine(dir, "g1part_" + key + "_worst.png"), t, tw, th);
                written++;
            }

            sheet.Add(_ctlPosEv);
            sheet.Add(_ctlNegEv);
            foreach ((Kanit ev, string name) in new[] { (_ctlPosEv, "g1_kontrol_pozitif.png"), (_ctlNegEv, "g1_kontrol_negatif.png") })
            {
                if (ev == null) continue;
                Color32[] t = Tile(ev, 512, out int tw, out int th);
                Png(Path.Combine(dir, name), t, tw, th);
                written++;
            }

            // the contact sheet: 4 columns, rows of 512 × 256 tiles (order: the 12 weapons, the 4 hand ↔ part, the 2 controls)
            const int cols = 4, half = 256, gap = 4;
            int rows = (sheet.Count + cols - 1) / cols, cw = half * 2 + gap, ch = half + gap, sw = cols * cw, shh = rows * ch;
            var px = new Color32[sw * shh];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 255);
            for (int n = 0; n < sheet.Count; n++)
            {
                Color32[] t = Tile(sheet[n], half, out int tw, out int th);
                int col = n % cols, row = rows - 1 - n / cols;   // the first row at the top of the PNG
                for (int y = 0; y < th; y++)
                    for (int x = 0; x < tw; x++)
                        px[(row * ch + y) * sw + col * cw + x] = t[y * tw + x];
            }

            Png(Path.Combine(dir, "g1_kontakt.png"), px, sw, shh);
            return written + 1;
        }

        // ---------------------------------------------------------------------------------------------------- report

        private static string F1(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => "(" + F1(v.x) + ", " + F1(v.y) + ", " + F1(v.z) + ")";
        private static string V2(Vector2 v) => "(" + F1(v.x) + ", " + F1(v.y) + ")";
        private static string Join(HashSet<int> s) => string.Join("/", s);
        private static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        private static string At(Result r) => r == null ? "-" : r.Meta.Moment + " yaw " + (r.Meta.Yaw * 45) + (string.IsNullOrEmpty(r.Meta.Phase) ? "" : " " + r.Meta.Phase);

        private static int MaxOver(Kayit k)
        {
            int m = 0;
            foreach (Result[] rs in k.Worst.Values)
                foreach (Result r in rs)
                    if (r != null)
                        m = Mathf.Max(m, r.Over);
            return m;
        }

        private static string Regions(Result r)
        {
            var parts = new List<string>();
            for (int i = 1; i < RCount; i++)
                if (r.ExitBy[i] > 0)
                    parts.Add(Q(RegionNames[i]) + ": " + r.ExitBy[i]);
            return "{" + string.Join(", ", parts) + "}";
        }

        private static string RowJson(Result r, int yaw, int samples)
        {
            if (r == null) return "{\"yaw\": " + yaw + ", \"samples\": 0}";
            return "{\"yaw\": " + yaw + ", \"samples\": " + samples + ", \"exitPx\": " + r.Exit + ", \"exitByRegion\": " + Regions(r) + ", \"overPx\": " + r.Over +
                   ", \"exitMaxMm\": " + F1(r.ExitMm) + ", \"weaponVisiblePx\": " + r.WeaponVis + ", \"bodyPx\": " + r.Body + ", \"phase\": " + Q(r.Meta.Phase) +
                   ", \"frame\": " + r.Meta.Frame + ", \"restReached\": " + (r.Meta.RestReached ? "true" : "false") + ", \"snapshot\": " + Q(r.Source) +
                   ", \"bodyLod\": " + r.BodyLod + ", \"weaponLod\": " + r.WeaponLod + "}";
        }

        private static string PartJson(Result r, int yaw, int samples)
        {
            if (r == null) return "{\"yaw\": " + yaw + ", \"samples\": 0}";
            return "{\"yaw\": " + yaw + ", \"samples\": " + samples + ", \"gapPx\": " + F1(r.GapPx) + ", \"partExitPx\": " + r.PartExit + ", \"partSilhouettePx\": " + r.PartSil +
                   ", \"partVisiblePx\": " + r.PartVis + ", \"leftHandPx\": " + r.HandL + ", \"phase\": " + Q(r.Meta.Phase) + ", \"frame\": " + r.Meta.Frame + "}";
        }

        private string Json(float seconds)
        {
            var b = new StringBuilder();
            b.Append("{\n  \"test\": ").Append(Q(TestName)).Append(",\n");
            b.Append("  \"build\": ").Append(Q(System.Environment.GetEnvironmentVariable("DD_G1_BUILD") ?? "")).Append(",\n");
            b.Append("  \"probe\": {\"height\": ").Append(ProbeHeight).Append(", \"aspect\": ").Append(F1(_cam != null ? _cam.aspect : 0f))
                .Append(", \"fov\": ").Append(F1(_cam != null ? _cam.fieldOfView : 0f)).Append(", \"orthographic\": ").Append(_cam != null && _cam.orthographic ? "true" : "false")
                .Append(", \"cameraPitchDeg\": ").Append(F1(_cam != null ? Vector3.Angle(_cam.transform.forward, Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up)) : 0f))
                .Append(", \"flipY\": ").Append(_flipY ? "true" : "false").Append(", \"snapshotsRender\": ").Append(_fromRender).Append(", \"snapshotsCoroutine\": ").Append(_fromCoroutine)
                .Append(", \"lodOverrides\": ").Append(_lodOverrides).Append(", \"seconds\": ").Append(F1(seconds)).Append("},\n");
            b.Append("  \"definition\": ").Append(Q("G1 exit px = weapon (model + magazine part) visible in front of the body's nearest surface AND that surface point inside the weapon's solid (front/back face counts of the weapon surfaces in front of it differ) AND a weapon surface behind it on the pixel's ray (it crosses the visible skin), body point in a G1 region (plate_r/l, upper_arm_r/l, collar, hood, torso_back). over px = weapon in front of a G1 region without crossing. K6: gapPx = left hand ↔ part silhouette gap (0 touching), partExitPx = part crossing the left hand / forearm surface.")).Append(",\n");
            b.Append("  \"g1Regions\": [\"plate_r\", \"plate_l\", \"upper_arm_r\", \"upper_arm_l\", \"collar\", \"hood\", \"torso_back\"],\n");
            b.Append("  \"controls\": {\"positive\": {\"exitPx\": ").Append(_ctlPos != null ? _ctlPos.Exit : -1).Append(", \"overPx\": ").Append(_ctlPos != null ? _ctlPos.Over : -1)
                .Append(", \"exitByRegion\": ").Append(_ctlPos != null ? Regions(_ctlPos) : "{}").Append(", \"embedMm\": ").Append(F1(EmbedMetres * 1000f)).Append("}, \"negative\": {\"exitPx\": ")
                .Append(_ctlNeg != null ? _ctlNeg.Exit : -1).Append(", \"overPx\": ").Append(_ctlNeg != null ? _ctlNeg.Over : -1).Append(", \"liftMm\": ").Append(F1(LiftMetres * 1000f))
                .Append("}, \"note\": ").Append(Q(_ctlNote)).Append("},\n");
            b.Append("  \"weapons\": [\n");
            int n = 0;
            foreach (Silah w in Silahlar)
            {
                Kayit k = _kayit[w.Key];
                b.Append("    {\"key\": ").Append(Q(k.Key)).Append(", \"special\": ").Append(k.Special ? "true" : "false").Append(", \"authoredModel\": ").Append(k.Authored ? "true" : "false")
                    .Append(", \"g1MaxPx\": ").Append(k.WorstAll != null ? k.WorstAll.Exit : -1).Append(", \"g1MaxAt\": ").Append(Q(At(k.WorstAll)))
                    .Append(", \"g1MaxExitMm\": ").Append(F1(k.WorstAll != null ? k.WorstAll.ExitMm : 0f)).Append(", \"overMaxPx\": ").Append(MaxOver(k))
                    .Append(", \"kickPeakFrameOffset\": ").Append(k.PeakOffset).Append(", \"kickPeakMm\": ").Append(F1(k.PeakMm)).Append(", \"mmPerPx\": ").Append(F1(k.MmPerPx))
                    .Append(", \"bodyLods\": [").Append(string.Join(", ", k.BodyLods)).Append("], \"weaponLods\": [").Append(string.Join(", ", k.WeaponLods)).Append("]")
                    .Append(", \"minWeaponDraws\": ").Append(k.MinWeaponDraws == int.MaxValue ? 0 : k.MinWeaponDraws).Append(",\n      \"moments\": {");
                for (int m = 0; m < Moments.Length; m++)
                {
                    b.Append(m == 0 ? "\n" : ",\n").Append("        ").Append(Q(Moments[m])).Append(": [");
                    for (int y = 0; y < YawCount; y++) b.Append(y == 0 ? "" : ", ").Append(RowJson(k.Worst[Moments[m]][y], y * 45, k.Samples[Moments[m]][y]));
                    b.Append("]");
                }

                b.Append("\n      },\n      \"reloadPart\": {\"partInHandSeen\": ").Append(k.PartInHandSeen ? "true" : "false")
                    .Append(", \"gapMaxPx\": ").Append(F1(k.WorstPart != null ? k.WorstPart.GapPx : -1f)).Append(", \"partExitMaxPx\": ").Append(k.WorstPart != null ? k.WorstPart.PartExit : -1)
                    .Append(", \"worstAt\": ").Append(Q(At(k.WorstPart))).Append(", \"perYaw\": [");
                for (int y = 0; y < YawCount; y++) b.Append(y == 0 ? "" : ", ").Append(PartJson(k.PartWorst[y], y * 45, k.PartSamples[y]));
                b.Append("]},\n      \"notes\": [");
                for (int i = 0; i < k.Notes.Count; i++) b.Append(i == 0 ? "" : ", ").Append(Q(k.Notes[i]));
                b.Append("]}").Append(++n < Silahlar.Length ? ",\n" : "\n");
            }

            b.Append("  ]\n}\n");
            return b.ToString();
        }

        private string Markdown(float seconds)
        {
            var b = new StringBuilder();
            b.Append("# G1 TD kare kapısı (WP-082 R3, kahraman v2)\n\n");
            b.Append("Test: `").Append(TestName).Append("` · build `").Append(System.Environment.GetEnvironmentVariable("DD_G1_BUILD") ?? "?").Append("` · süre ")
                .Append(seconds.ToString("F0", CultureInfo.InvariantCulture)).Append(" s · prob ").Append(ProbeHeight).Append(" satır, kamera FOV ")
                .Append(F1(_cam != null ? _cam.fieldOfView : 0f)).Append("° · anlık görüntü render/coroutine ").Append(_fromRender).Append("/").Append(_fromCoroutine)
                .Append(" · LOD düzeltme ").Append(_lodOverrides).Append("\n\n");
            b.Append("**Tanım.** G1 çıkış pikseli: silah (model + eldeki şarjör parçası) gövdenin en yakın yüzeyinin ÖNÜNDE görünür, o yüzey noktası silahın katı hacminin İÇİNDE (önündeki silah yüzeylerinin ön/arka yüz sayıları farklı) ve aynı ışında silahın gövde yüzeyinin ARKASINDA da yüzeyi var (görünen deriyi kesiyor); gövde noktası G1 bölgesinde (omuz plakası, üst kol, yaka, kapüşon/baş, sırt). \"Üstte\": G1 bölgesinin önünde, kesmeden. Eşik 0 px; kapı kararı S-06'da (bu test yalnız raporlar).\n\n");
            b.Append("**Kontroller.** pozitif (5 cm gömülü ar_1): çıkış ").Append(_ctlPos != null ? _ctlPos.Exit.ToString() : "-").Append(" px · negatif (60 cm üstte): çıkış ")
                .Append(_ctlNeg != null ? _ctlNeg.Exit.ToString() : "-").Append(" px, üstte ").Append(_ctlNeg != null ? _ctlNeg.Over.ToString() : "-").Append(" px\n\n");
            b.Append("| Silah | G1 max px | en kötü (an, yaw) | çıkıntı mm | bölgeler | nişan max | tepme max | reload max | üstte max px | gövde LOD | silah LOD | mm/px |\n");
            b.Append("|---|---|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (Silah w in Silahlar)
            {
                Kayit k = _kayit[w.Key];
                b.Append("| ").Append(k.Key).Append(" | ").Append(k.WorstAll != null ? k.WorstAll.Exit.ToString() : "-").Append(" | ").Append(At(k.WorstAll)).Append(" | ")
                    .Append(F1(k.WorstAll != null ? k.WorstAll.ExitMm : 0f)).Append(" | ").Append(k.WorstAll != null ? Regions(k.WorstAll).Replace("\"", "") : "-").Append(" | ");
                foreach (string m in Moments)
                {
                    int max = 0, yaw = -1;
                    for (int y = 0; y < YawCount; y++)
                        if (k.Worst[m][y] != null && k.Worst[m][y].Exit > max)
                        {
                            max = k.Worst[m][y].Exit;
                            yaw = y * 45;
                        }

                    b.Append(max).Append(yaw >= 0 ? " (" + yaw + "°)" : "").Append(" | ");
                }

                b.Append(MaxOver(k)).Append(" | ").Append(Join(k.BodyLods)).Append(" | ").Append(Join(k.WeaponLods)).Append(" | ").Append(F1(k.MmPerPx)).Append(" |\n");
            }

            b.Append("\n## Reload el ↔ parça (K6, AD-D1 §5)\n\n| Silah | parça elde görüldü | boşluk max px | parça çıkışı max px | en kötü | yaw başına boşluk / çıkış |\n|---|---|---|---|---|---|\n");
            foreach (Silah w in Silahlar)
            {
                Kayit k = _kayit[w.Key];
                if (!k.PartInHandSeen && System.Array.IndexOf(PartWeapons, k.Key) < 0) continue;
                b.Append("| ").Append(k.Key).Append(" | ").Append(k.PartInHandSeen ? "evet" : "HAYIR (ölçülemedi)").Append(" | ").Append(k.WorstPart != null ? F1(k.WorstPart.GapPx) : "-")
                    .Append(" | ").Append(k.WorstPart != null ? k.WorstPart.PartExit.ToString() : "-").Append(" | ").Append(At(k.WorstPart)).Append(" | ");
                for (int y = 0; y < YawCount; y++)
                {
                    Result r = k.PartWorst[y];
                    b.Append(y == 0 ? "" : " · ").Append(y * 45).Append("°: ").Append(r != null ? F1(r.GapPx) + "/" + r.PartExit : "-");
                }

                b.Append(" |\n");
            }

            b.Append("\n## Notlar\n\n");
            foreach (Silah w in Silahlar)
                if (_kayit[w.Key].Notes.Count > 0)
                    b.Append("- ").Append(w.Key).Append(": ").Append(string.Join("; ", _kayit[w.Key].Notes)).Append("\n");
            b.Append("- Kareler: en kötü kare silah başına (sol: oyun kamerasının kendi karesi bir kare sonra, çıkış pikselleri macenta; sağ: yanlış renk — mavi tonlar G1 bölgeleri, gri silah, sarı üstte, macenta çıkış, camgöbeği parça, kırmızı parça çıkışı) ve `g1_kontakt.png` (sıra: 12 silah, 4 el–parça, 2 kontrol).\n");
            return b.ToString();
        }
    }
}
