using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-083 R2 locomotion gates L1, L1t, L2, L3 (S-06; tools/qa/r2-gates.json, definitions K/wp-078/R2-KABUL-TASLAK.md §2),
    /// TOP-DOWN since WP-082 R3 (C-00 07.10: tests run top-down only). One course on the default player path: the real Run scene's
    /// player, its motor, camera and animator, driven by stick input, sampled once per frame at a fixed 60 fps
    /// (Time.captureDeltaTime = 1/60) AFTER the animator, its IK passes and every LateUpdate solver (sampler order 32000), on a flat
    /// Environment-layer floor so that walls and slopes stay out of it. In top-down the body turns to the AIM, not to the view: the
    /// aim is held on one world direction (the camera's planar forward, pointer input), so the eight stick directions are the eight
    /// strafe directions in the body's space.
    /// <list type="bullet">
    /// <item>Course A, steady gait (WP-082 R3: the AP-026 8-way tree): 8 directions × walk / run / sprint. Walk and run = the stick tilt
    /// that puts the motor on the ring's threshold speed (the 1-D blend's pure ring); sprint = full tilt. Each segment: 1 s warm-up
    /// (excluded), then until both feet show ≥ 6 cycles (and ≥ 3 s), bounded at 12 s (walk) / 10 s (run, sprint).</item>
    /// <item>Course K, the writer's positive controls (WP-082 R3 PLAN §5.1): sprint_fwd and walk_fwd with the foot IK off, sprint_fwd with the
    /// Animator's LocoRate forced to ×0.5 with foot IK still off (a known slide of ≈ 0.5, subject to the same positive-control gate).</item>
    /// <item>Course S: run forward with the aim swung ±100° (PLAN §4.3; L1 / L3 reported, not gated).</item>
    /// <item>Course B, transitions × 5: start (still → run), stop (run → still), pivot 180° (run → run reversed), 90° cut
    /// (run → run ±90°). Base directions fwd, right, back, left, fwd_r; the cut alternates right / left.</item>
    /// </list>
    /// Contact (L1, L1t, L2) — WP-082 R3. The R2 classifier (the SHOWN sole within 3 cm of the ground and vertical speed ≤ 0.15 m/s) read
    /// every frame of both feet as contact: FootIkAdapter drops BOTH feet onto the ground every frame (its target height is hit +
    /// feetBottomHeight whatever the clip's lift, clamped at 0.35 m), so L1 measured the foot's whole-cycle mean speed ≈ the body speed
    /// (≈ 1.00 in R2). The animated (pre-IK) sole does not separate stance either (e3-once1: with the foot IK off the walk's sole sits
    /// 13.6 cm up at its median, the height rule kept 0–3 % of the frames). Contact is therefore the clip's own PHASE: the base layer's
    /// clips (GetCurrentAnimatorClipInfo, and the next state's during a transition, by the transition weight) at their normalized time,
    /// each clip's foot_x → foot_x_off window on its loop, Σ weight ≥ 0.5 (a clip without foot events — the stand — counts as both feet
    /// down). It is checked against the pose in the same run (control (a2)): the animated sole lower in stance than in swing and the
    /// stance foot travelling backward under the body. The pose classifier (animated sole within 3 cm of its segment floor) and the R2
    /// classifier are reported beside it. Slide ratio = the shown foot's horizontal world speed ÷ the body's horizontal speed, floored
    /// at <see cref="GovdeHizTabani"/>.
    /// <list type="bullet">
    /// <item>L1 avg / p95 — every contact foot-frame of course A after the warm-up.</item>
    /// <item>L1t avg (p95 reported) — every contact foot-frame in the 0.3 s after each course B input change.</item>
    /// <item>L2 deviationMax — per steady segment |cadence × step − v| / v, the largest (cadence from the plant events, step from the
    /// planted foot's backward speed under the body).</item>
    /// <item>L3 — every bone's rotation in the character root's space, the angle between consecutive frames (°/frame at 60 fps), in the
    /// 1 s after each course B input change; the source of each peak (hand / arm, a foot-lock release, the foot IK's pin) is logged.</item>
    /// </list>
    /// Positive controls (same run; a control that fails keeps the gates that depend on it unwritten): (a) every steady segment's
    /// contact share per foot within ±10 points of the clip events' stance share (foot_x → foot_x_off) AND the pose agreeing with the
    /// phase (a2) — gates L1, L1t, L2;
    /// (b) the ×0.5 LocoRate segment has verified IK-off / actual-rate isolation and reads L1 0.5 ± 0.1 — gates L1; (c) StrideEslemesi on the pure run directions: the stride-match slide
    /// (CadenceBand.SlideFraction at the measured speed and the clip rate LocoRate × child time scale) within 0.03 of the segment's
    /// L1 avg — an information row since S-06 08.10 (the slide it misses is the AP-022 v008 clips' own stance motion). L1 / L1t sample
    /// (S-06 08.10): phase contact ∧ the animated foot's |vertical speed| ≤ 0.15 m/s (R2's vertical rule; logged as wp082.l1Def);
    /// (a1)'s expected share comes from each frame's real clip weights. [Explicit]: the R2 chain runs it by full name; each gate whose source is in the recording is written through
    /// R2Olcum, a gate whose source or control is missing is not written (BLOCKED by name) and the test fails naming it.
    /// </summary>
    [TestFixture]
    public sealed class R2LokomotionOlcumTests
    {
        public const string TestName = "DD.Prototype.Tests.PlayMode.R2LokomotionOlcumTests.L1_L3_Lokomotion_8Yon_KalkisDurusDonus";
        public const string GcTestName = "DD.Prototype.Tests.PlayMode.R2LokomotionOlcumTests.GC_TdSprint_YonCevirme_600Kare";

        // ---- the contract's definitions (R2-KABUL-TASLAK §2)
        public const float TemasYukseklik = 0.03f;      // sole ↔ its floor, m
        public const float TemasDikeyHiz = 0.15f;       // foot vertical speed, m/s
        public const int Fps = 60;
        public const int L1tPencere = 18;               // 0.3 s at 60 fps
        public const int L3Pencere = 60;                // 1 s at 60 fps after the input change (start / stop / pivot / cut)
        public const int Isinma = 60;                   // 1 s warm-up excluded from the steady segments (L2: "ilk 1 s ısınma hariç")
        public const int MinDongu = 6;                  // L2: ≥ 6 cycles per direction
        public const int Tekrar = 5;                    // L3 / L1t: each scenario × 5
        // ---- this test's choices (reported)
        public const float GovdeHizTabani = 1.0f;       // m/s — slide ratio denominator floor
        public const float KararliTepeTabani = 1.0f;    // °/frame — L3 ratio denominator floor
        public const int OlayAyrimKare = 12;            // 0.2 s: a second same-foot plant inside it is the same beat
        public const float ZeminYukseklik = 150f;       // the test floor above the spawn, m
        public const float ZeminYariGenislik = 200f;    // its half extent, m
        public const float MerkezYaricap = 90f;         // re-centre (teleport, standing) beyond this, m
        public const double KararliTolerans = 0.20;     // a steady segment's body speed within ±20 % of the motor's command
        public const float NisanMesafe = 10f;           // the pointer's world point ahead of the body, m
        public const float NisanHataSiniri = 10f;       // ° — steady frames' body facing vs the held aim (else the direction labels are wrong)
        public const float NisanSalinimDerece = 100f;   // course S: ±100° (PLAN §4.3)
        public const int NisanSalinimPeriyot = 120;     // frames (2 s)
        public const float TabanYuzdelik = 0.02f;       // the animated sole's floor per foot: its 2nd percentile over course A
        // ---- WP-082 R3 PLAN §5.1 positive controls
        /// <summary>S-10 08.10: the contact definition, written as the top-level "l1Def" of r2-L1 / L1t / L2.json (clip phase ∧ |vy| ≤ 0.15 m/s).</summary>
        public const string L1Tanim = "clipPhase+vy0.15";
        /// <summary>WP-082 R3: the AP-026 8-way rings (walk, run, sprint) × 8 directions = the steady segments.</summary>
        public const int GaitSayisi = 3, NKararli = GaitSayisi * 8;
        private static readonly string[] GaitAd = { "walk", "run", "sprint" };
        public const double TemasPayiTolerans = 0.10;   // (a1) ±10 points
        public const float PozAyrim = 0.02f;            // (a2) animated sole: swing median − stance median ≥ 2 cm
        public const float KontrolRate = 0.5f;          // (b) LocoRate ×0.5
        public const double KontrolKayma = 0.5, KontrolKaymaTolerans = 0.1;
        public const double StrideFarkSiniri = 0.03;    // (c)
        // ---- GC (PLAN §5.2 (4))
        public const int GcKare = 600;
        public const int GcYonKare = 40;                // a direction change every 40 frames
        public const int GcKontrolKare = 30;
        public const int GcKontrolBayt = 1024;
        public const int GcBosKare = 300, GcBosOturma = 30;   // the empty-scene windows (S-06 08.10)

        // ---- the gates' thresholds (r2-gates.json)
        private const double L1Avg = 0.05, L1P95 = 0.10, L1tAvg = 0.10, L2Dev = 0.08, L3Limb = 20.0, L3Torso = 10.0, L3Ratio = 1.5;

        internal static readonly HumanBodyBones[] Kemikler =
        {
            // limbs (0..11)
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            // torso chain (12..17)
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
        };

        internal const int UzuvSayisi = 12;
        private static readonly string[] YonAd = LocomotionV4.Directions;   // fwd, fwd_r, right, back_r, back, back_l, left, fwd_l

        private enum Tur { Kararli, Kontrol, NisanSalinim, Kalkis, Durus, Pivot180, Kesme90 }

        private sealed class Parca
        {
            public string Ad;
            public Tur Tur;
            public int Yuruyus;   // 0 walk, 1 run
            public int Yon;
            public int[] Ref;     // steady segments a transition is compared against (L3 ratio)
            public double KomutHizToplam;
            public int KomutHizN;
            public int Kare;
            public float Cubuk;   // stick tilt
            public float NisanHataMax;
            public Vector3 Bas, Son;   // position at the segment's start / end relative to the floor centre
        }

        /// <summary>Per-segment readout (the olcum table's row).</summary>
        private sealed class Istat
        {
            public int N;
            public double V, VYon, Komut;
            public double T = double.NaN, U = double.NaN;
            public string Kaynak = "SHORT";
            public int Dongu;
            public List<double> Kayma = new List<double>();
            public double L1Avg = double.NaN, L1P95 = double.NaN;
            public double PayL = double.NaN, PayR = double.NaN, PayEskiL = double.NaN, PayEskiR = double.NaN;
            public double BekL = double.NaN, BekR = double.NaN;                    // (a1) expected share: per-frame clip weights × window shares
            public double FazOrtL = double.NaN, FazOrtR = double.NaN;              // (a1) measured: the mean stance weight Σ w × in-window (same quantity)
            public List<double> KaymaFaz = new List<double>();                     // every phase-contact foot-frame (before the |vy| rule), reported
            public double L1FazAvg = double.NaN;
            public double Rate = double.NaN;
            public double Honest = double.NaN;                                     // PlayerSkinnedVisual.HonestSpeed (m/s), mean
            public int Kabul, BirakS, BirakG, BirakL, BirakP, KilitliKare;
            public double PostGapMed = double.NaN, AnimGapMed = double.NaN, PimMed = double.NaN;
            public int RayDiger;
            public double SalinimAnimMed = double.NaN, SalinimPostMed = double.NaN;   // phase-swing frames: animated / shown sole height (median)
            public double SalinimYerdePay = double.NaN;                                // phase-swing frames shown within 1 cm of the ground
            public double PayPozL = double.NaN, PayPozR = double.NaN;                  // the pose classifier (animated sole near its segment floor)
            public double Uyum = double.NaN;                                           // frame agreement phase vs pose classifier
            public readonly double[] DurusMed = { double.NaN, double.NaN }, SalinimMed = { double.NaN, double.NaN };   // animated sole per foot, phase stance / swing
            public readonly double[] UDurus = { double.NaN, double.NaN }, USalinim = { double.NaN, double.NaN };       // foot speed backward under the body (m/s)
            public double PelvisMed = double.NaN;
        }

        private SpecialWeaponSahne _s;
        private GameObject _zemin;
        private LokomotionKaydedici _kayit;
        private LocoRateCarpani _carpan;
        private Vector3 _nisan = Vector3.forward;       // the held aim (world, planar)
        private Vector3 _nisanAnlik = Vector3.forward;  // this frame's aim (course S swings it)
        private Vector3 _ileri = Vector3.forward, _sag = Vector3.right;   // the motor's movement basis (camera planar)
        private int _gecisBasi;
        private bool _profilerOnce;
        private int _fpsOnce = int.MinValue;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // the default player path whatever ran before in this session (hold / grip IK on, foot IK defaults)
            FootIkAdapter.ResetStatics();
            WeaponHoldIk.ResetStatics();
            WeaponGripIk.ResetStatics();
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_fpsOnce != int.MinValue) { Application.targetFrameRate = _fpsOnce; UnityEngine.Profiling.Profiler.enabled = _profilerOnce; }
            _fpsOnce = int.MinValue;
            if (_kayit != null) { _kayit.Birak(); Object.Destroy(_kayit.gameObject); }
            _kayit = null;
            if (_carpan != null) Object.Destroy(_carpan.gameObject);
            _carpan = null;
            if (_zemin != null) Object.Destroy(_zemin);
            _zemin = null;
            // R3 (C-00 07.10, S-01 inventory): the next test starts in the process default — TopDown (user 06.10: tests run top-down only)
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            if (_s != null) yield return _s.Bitir();
            _s = null;
            FootIkAdapter.ResetStatics();
            WeaponHoldIk.ResetStatics();
            WeaponGripIk.ResetStatics();
        }

        private static Vector2 Yon8(int k, float m)
        {
            float a = (k & 7) * 45f * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * m;
        }

        private Vector3 Dunya(Vector2 stick) => _sag * stick.x + _ileri * stick.y;

        private string _iptal;

        private bool Canli()
        {
            if (_iptal != null) return false;
            if (_s == null || _s.Rig == null) { _iptal = "oyuncu kayboldu"; return false; }
            if (RunDirector.Instance == null || RunDirector.Instance.Phase != RunPhase.Fighting)
            {
                _iptal = "koşu Fighting fazından çıktı (" + (RunDirector.Instance != null ? RunDirector.Instance.Phase.ToString() : "RunDirector yok") + ")";
                return false;
            }

            return true;
        }

        /// <summary>The pointer on the world point <see cref="NisanMesafe"/> ahead of the body along this frame's aim, on the aim plane.</summary>
        private void Nisanla()
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            Camera cam = rig != null ? rig.Camera : Camera.main;
            if (cam == null || _s == null || _s.Rig == null) return;
            Vector3 p = _s.Rig.transform.position + _nisanAnlik * NisanMesafe;
            p.y = _s.Rig.Aim.AimPlaneY;
            Vector3 sp = cam.WorldToScreenPoint(p);
            _s.Girdi.Imlec = new Vector2(sp.x, sp.y);
        }

        /// <summary>n frames with the stick at <paramref name="yon"/>, recorded under <paramref name="etiket"/> (−1 = not measured).</summary>
        private IEnumerator Sur(int n, Vector2 yon, int etiket, Parca p)
        {
            for (int i = 0; i < n; i++)
            {
                if (!Canli()) yield break;
                Nisanla();
                _s.Girdi.Yon = yon;
                _kayit.Etiket = etiket;
                yield return null;
                if (p != null && _kayit.Since >= Isinma) { p.KomutHizToplam += _s.Rig.Motor.CommandedPlanarVelocity.magnitude; p.KomutHizN++; }
            }
        }

        private IEnumerator Merkezle(Vector3 merkez, bool zorla)
        {
            if (!Canli()) yield break;
            Vector3 d = _s.Rig.transform.position - merkez;
            d.y = 0f;
            if (!zorla && d.magnitude < MerkezYaricap) yield break;
            yield return Sur(30, Vector2.zero, -1, null);
            if (!Canli()) yield break;
            _s.Rig.Teleport(merkez + Vector3.up * 0.05f, Quaternion.LookRotation(_nisan, Vector3.up));
            yield return Sur(45, Vector2.zero, -1, null);
        }

        /// <summary>A steady segment: warm-up, then until both feet show MinDongu + 1 beats (≥ 3 s), at most <paramref name="cap"/> frames.</summary>
        private IEnumerator Kararli(Vector3 merkez, Parca p, int idx, Vector2 stick, int cap)
        {
            // every steady segment from the centre (e3-once1: walk_fwd_l and run_fwd drifted out and stood against world geometry that
            // reaches the y + 150 m floor — body 0 m/s under a 2.35 / 6.2 m/s command, the gap ray on another collider)
            yield return Merkezle(merkez, true);
            if (_s != null && _s.Rig != null) p.Bas = _s.Rig.transform.position - merkez;
            for (int f = 0; f < cap; f++)
            {
                if (!Canli()) break;
                Nisanla();
                _s.Girdi.Yon = stick;
                _kayit.Etiket = idx;
                yield return null;
                p.Kare++;
                if (_kayit.Since >= Isinma)
                {
                    p.KomutHizToplam += _s.Rig.Motor.CommandedPlanarVelocity.magnitude;
                    p.KomutHizN++;
                    p.NisanHataMax = Mathf.Max(p.NisanHataMax, Vector3.Angle(Yatay(_s.Rig.transform.forward), _nisan));
                }

                if (_kayit.Since >= Isinma + 180 && _kayit.DonguYeter(MinDongu + 1)) break;
            }

            if (_s != null && _s.Rig != null) p.Son = _s.Rig.transform.position - merkez;
        }

        private Vector3 ZeminKur()
        {
            // the flat floor: Environment layer (the foot IK's and the motor's ground), above the spawn, away from the world
            Vector3 merkez = _s.Rig.transform.position + Vector3.up * ZeminYukseklik;
            _zemin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _zemin.name = "R2Loko_Zemin";
            _zemin.layer = PrototypeLayers.Environment;
            _zemin.transform.localScale = new Vector3(ZeminYariGenislik * 2f, 1f, ZeminYariGenislik * 2f);
            _zemin.transform.position = merkez - Vector3.up * 0.5f;
            return merkez;
        }

        private void TemelYonler()
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            _ileri = rig != null ? Yatay(rig.PlanarForward).normalized : Vector3.forward;
            _sag = rig != null ? Yatay(rig.PlanarRight).normalized : Vector3.right;
            if (_ileri.sqrMagnitude < 0.5f) _ileri = Vector3.forward;
            if (_sag.sqrMagnitude < 0.5f) _sag = Vector3.right;
            _nisan = _ileri;   // the aim held on the camera's planar forward: stick up = toward the aim
            _nisanAnlik = _nisan;
        }

        // --------------------------------------------------------------------------------------------------- the course

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(1500000)]
        [Order(1)]
        public IEnumerator L1_L3_Lokomotion_8Yon_KalkisDurusDonus()
        {
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 1f / Fps;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Assert.IsNotNull(body.Animator, "oyuncu Animator'ı yok");
            Assert.IsTrue(body.Authored, "oyuncu animasyon seti yazılmış değil (primitive yol) — lokomotion ölçülemez");
            Assert.IsTrue(body.Animator.isHuman, "oyuncu Animator'ı Humanoid değil");
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "TopDown isteği uygulanmadı");
            CharacterAnimationSet anim = body.AnimationSet;
            Assert.IsNotNull(anim, "CharacterAnimationSet yok");
            FootIkAdapter ik = body.FootIk;
            Assert.IsNotNull(ik, "FootIkAdapter yok — animasyon pozunun taban yüksekliği okunamaz");

            Vector3 merkez = ZeminKur();
            yield return null;
            TemelYonler();

            var go = new GameObject("R2Loko_Kaydedici");
            _kayit = go.AddComponent<LokomotionKaydedici>();
            _kayit.Bagla(body.Animator, body.transform, ik, _zemin.GetComponent<Collider>(), anim);
            _kayit.Govde(body);
            _carpan = new GameObject("R2Loko_LocoRateCarpani").AddComponent<LocoRateCarpani>();
            _carpan.Bagla(body.Animator);

            // course definition: steady segments first (index = gait × 8 + direction), the controls, the aim swing, then the transitions
            var parcalar = new List<Parca>();
            // WP-082 R3 (sonra): the AP-026 8-way tree — walk / run / sprint rings × 8 directions; walk and run at their ring threshold
            // speed (the 1-D blend's pure ring), sprint at full tilt (the motor's top speed in that direction)
            Assert.IsTrue(body.Locomotion8, "oyuncu controller'ı 8 yönlü ağaç değil (LocoSpeed yok) — bu yazıcı AP-026 halkalarını ölçer");
            float[] esik = anim.Locomotion.ringThresholdMps;
            Assert.GreaterOrEqual(esik.Length, 4, "set halka eşikleri eksik");
            for (int g = 0; g < GaitSayisi; g++)
                for (int k = 0; k < 8; k++)
                    parcalar.Add(new Parca { Ad = GaitAd[g] + "_" + YonAd[k], Tur = Tur.Kararli, Yuruyus = g, Yon = k });
            const int iIkOffRun = NKararli, iIkOffWalk = NKararli + 1, iRate = NKararli + 2, iSalinim = NKararli + 3;
            parcalar.Add(new Parca { Ad = "K_ikOff_sprint_fwd", Tur = Tur.Kontrol, Yuruyus = 2, Yon = 0 });
            parcalar.Add(new Parca { Ad = "K_ikOff_walk_fwd", Tur = Tur.Kontrol, Yuruyus = 0, Yon = 0 });
            parcalar.Add(new Parca { Ad = "K_rate05_sprint_fwd", Tur = Tur.Kontrol, Yuruyus = 2, Yon = 0 });
            parcalar.Add(new Parca { Ad = "S_aimSwing100_sprint_fwd", Tur = Tur.NisanSalinim, Yuruyus = 2, Yon = 0 });
            _gecisBasi = parcalar.Count;
            int[] tabanlar = { 0, 2, 4, 6, 1 };

            float maxSpeed = _s.Rig.Motor.MaxSpeed;
            float reference = anim.LocomotionReferenceSpeed;
            int[] sira = { 0, 4, 2, 6, 1, 5, 3, 7 };   // opposite pairs keep the course near the centre
            var cubuk = new float[GaitSayisi, 8];
            for (int k = 0; k < 8; k++)
            {
                Vector2 d = Yon8(k, 1f);
                float bs = Mathf.Max(0.1f, PlayerMotor.BackwardScale(Dunya(d), _nisan));
                for (int g = 0; g < GaitSayisi; g++)
                {
                    float hedef = g < 2 ? esik[g + 1] : float.MaxValue;   // walk / run: the ring threshold; sprint: full tilt
                    cubuk[g, k] = maxSpeed > 0.1f ? Mathf.Clamp(hedef / (maxSpeed * bs), 0.1f, 1f) : 1f;
                    parcalar[g * 8 + k].Cubuk = cubuk[g, k];
                }
            }

            yield return Merkezle(merkez, true);
            yield return Sur(60, Vector2.zero, -1, null);
            bool zeminde = _s.Rig.Motor.IsGrounded && Mathf.Abs(_s.Rig.transform.position.y - merkez.y) < 0.25f;
            Debug.Log("[DD-TEST] wp083.loco setup floor=" + zeminde + " y=" + F2(_s.Rig.transform.position.y) + " floorY=" + F2(merkez.y) + " v4=" + body.HasLocomotionV4 +
                      " footIk=" + (body.FootIk != null) + " maxSpeed=" + F2(maxSpeed) + " ref=" + F2(reference) + " k=" + F(anim.Locomotion.heroStrideScale) + " thresholds=" + F3(esik[1]) + "/" + F3(esik[2]) + "/" + F3(esik[3]) +
                      " loco8=" + body.Locomotion8 + " sticks=" + string.Join(",", Sticks(cubuk)) + " bones=" + _kayit.BulunanKemikler() + " camera=" + CameraModeService.Current +
                      " aim=" + V(_nisan) + " basisFwd=" + V(_ileri) + " basisRight=" + V(_sag) + " feetBottomL/R=" + F(body.Animator.leftFeetBottomHeight) + "/" + F(body.Animator.rightFeetBottomHeight) +
                      " humanScale=" + F(body.Animator.humanScale) + " legLength=" + F(ik.LegLength));
            Assert.IsTrue(zeminde, "oyuncu test zeminine oturmadı (y " + _s.Rig.transform.position.y + " / zemin " + merkez.y + ")");

            // ---- course A: steady gait (the aim held on one world direction, the stick in eight)
            for (int g = 0; g < GaitSayisi && _iptal == null; g++)
            {
                int cap = Isinma + (g == 0 ? 720 : 600);
                for (int j = 0; j < sira.Length && _iptal == null; j++)
                {
                    int k = sira[j];
                    int idx = g * 8 + k;
                    yield return Kararli(merkez, parcalar[idx], idx, Yon8(k, cubuk[g, k]), cap);
                }
            }

            // ---- course K: the positive controls (foot IK off ×2, then LocoRate ×0.5 with IK still off).
            // A foot lock would alter the shown foot speed and invalidate control (b); restore both overrides even on iterator failure.
            if (_iptal == null)
            {
                int forceBefore = FootIkAdapter.Force;
                float multiplierBefore = _carpan.Carpan;
                try
                {
                    FootIkAdapter.Force = 0;
                    yield return Kararli(merkez, parcalar[iIkOffRun], iIkOffRun, Yon8(0, cubuk[2, 0]), Isinma + 600);
                    yield return Kararli(merkez, parcalar[iIkOffWalk], iIkOffWalk, Yon8(0, cubuk[0, 0]), Isinma + 720);
                    _carpan.Carpan = KontrolRate;
                    yield return Kararli(merkez, parcalar[iRate], iRate, Yon8(0, cubuk[2, 0]), Isinma + 600);
                }
                finally
                {
                    FootIkAdapter.Force = forceBefore;
                    if (_carpan != null) _carpan.Carpan = multiplierBefore;
                    if (_kayit != null) _kayit.Etiket = -1;   // restored frames must not remain labelled as the half-rate control
                }
            }

            // ---- course S: run forward, the aim swung ±100° (2 s period)
            if (_iptal == null)
            {
                Parca ps = parcalar[iSalinim];
                yield return Merkezle(merkez, false);
                for (int f = 0; f < Isinma + 4 * NisanSalinimPeriyot && Canli(); f++)
                {
                    float a = NisanSalinimDerece * Mathf.Sin(2f * Mathf.PI * f / NisanSalinimPeriyot);
                    _nisanAnlik = Quaternion.AngleAxis(a, Vector3.up) * _nisan;
                    Nisanla();
                    _s.Girdi.Yon = Yon8(0, 1f);
                    _kayit.Etiket = iSalinim;
                    yield return null;
                    ps.Kare++;
                    if (_kayit.Since >= Isinma) { ps.KomutHizToplam += _s.Rig.Motor.CommandedPlanarVelocity.magnitude; ps.KomutHizN++; }
                }

                _nisanAnlik = _nisan;
            }

            // ---- course B: transitions × 5
            for (int r = 0; r < Tekrar && _iptal == null; r++)
            {
                int b = tabanlar[r], ters = (b + 4) & 7, yan = (ters + ((r & 1) == 0 ? 2 : 6)) & 7;
                var start = new Parca { Ad = "start_" + YonAd[b] + "#" + r, Tur = Tur.Kalkis, Yuruyus = 2, Yon = b, Ref = new[] { 16 + b } };
                var stop = new Parca { Ad = "stop_" + YonAd[b] + "#" + r, Tur = Tur.Durus, Yuruyus = 2, Yon = b, Ref = new[] { 16 + b } };
                var pivot = new Parca { Ad = "pivot180_" + YonAd[b] + ">" + YonAd[ters] + "#" + r, Tur = Tur.Pivot180, Yuruyus = 2, Yon = ters, Ref = new[] { 16 + b, 16 + ters } };
                var cut = new Parca { Ad = "cut90_" + YonAd[ters] + ">" + YonAd[yan] + "#" + r, Tur = Tur.Kesme90, Yuruyus = 2, Yon = yan, Ref = new[] { 16 + ters, 16 + yan } };
                int iStart = parcalar.Count; parcalar.Add(start);
                int iStop = parcalar.Count; parcalar.Add(stop);
                int iPivot = parcalar.Count; parcalar.Add(pivot);
                int iCut = parcalar.Count; parcalar.Add(cut);

                yield return Merkezle(merkez, false);
                yield return Sur(60, Vector2.zero, -1, null);          // still (StillHoldSeconds 0.2 → breath)
                yield return Sur(90, Yon8(b, 1f), iStart, null);       // start → run
                yield return Sur(60, Vector2.zero, iStop, null);       // stop
                yield return Sur(90, Yon8(b, 1f), -1, null);           // run again (not a window)
                yield return Sur(90, Yon8(ters, 1f), iPivot, null);    // pivot 180°
                yield return Sur(60, Yon8(yan, 1f), iCut, null);       // 90° cut
            }

            _s.Girdi.Yon = Vector2.zero;
            _kayit.Etiket = -1;
            _kayit.Birak();
            FootIkAdapter.Force = -1;
            _carpan.Carpan = 1f;

            // ---- the classifier: the animated sole's floor per foot over course A, then every frame's contact
            _kayit.TabanHesapla(NKararli, Isinma, TabanYuzdelik, out float tabanL, out float tabanR);
            _kayit.Siniflandir(Isinma, TabanYuzdelik);
            Dok(parcalar, iIkOffRun, iIkOffWalk, iRate);
            float nisanHata = 0f;
            for (int i = 0; i < NKararli; i++) nisanHata = Mathf.Max(nisanHata, parcalar[i].NisanHataMax);
            Debug.Log("[DD-TEST] wp083.loco course frames=" + _kayit.Kareler.Count + " plantEvents=" + _kayit.OlaySayisi + " aborted=" + (_iptal ?? "no") +
                      " facingVsAimMaxDeg=" + F2(nisanHata) + " animSoleFloorL/R(course A p" + (TabanYuzdelik * 100f).ToString("F0", CultureInfo.InvariantCulture) + ")=" + F(tabanL) + "/" + F(tabanR) + " m" +
                      " phaseClips=" + _kayit.FazKlipSayisi + " rayOtherFirst=" + (_kayit.RayDigerAd ?? "-") +
                      " rayHitOther=" + _kayit.RayDigerSayisi + " footIk served=" + ik.Served + " plants=" + ik.PlantsAccepted + " debounced=" + ik.PlantsDebounced +
                      " released S/G/L/P=" + ik.ReleasedByStretch + "/" + ik.ReleasedByGate + "/" + ik.ReleasedByLift + "/" + ik.ReleasedByPlant);

            var ist = new Istat[parcalar.Count];
            for (int i = 0; i < _gecisBasi; i++) ist[i] = Hesapla(i, Isinma, int.MaxValue);
            for (int i = _gecisBasi; i < parcalar.Count; i++) ist[i] = Hesapla(i, 1, L1tPencere);

            // ---- analysis: every gate on its own; a gate without its source (or its control) is named, not written
            var hatalar = new List<string>();
            if (_iptal != null) hatalar.Add("kurs yarıda kaldı: " + _iptal);
            if (nisanHata > NisanHataSiniri) hatalar.Add("gövde nişana dönmedi: kararlı karelerde en çok " + F2(nisanHata) + "° > " + NisanHataSiniri + "° (yön etiketleri geçersiz)");
            bool kontrolA = KontrolA(anim, parcalar, ist, hatalar);
            bool kontrolB = KontrolB(ist, iRate, hatalar);
            bool kontrolC = KontrolC(parcalar, ist, hatalar);
            bool yonGecerli = nisanHata <= NisanHataSiniri;
            KaynakKaniti(anim, parcalar, ist, iIkOffRun, iIkOffWalk);

            var yazilacak = new List<KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>>>();
            // S-06 08.10: (c) StrideEslemesi is an information row, not a gate (the slide it misses is the AP-022 v008 clips' own motion)
            Debug.Log("[DD-TEST] wp082.l1Def contact=phase (base-layer clips x their foot_x->foot_x_off windows, sum of weights >= 0.5) sample=contact AND |animated foot vertical speed| <= " +
                      TemasDikeyHiz + " m/s (L1, L1t; L2's planted-foot speed uses every phase-contact frame) ratio=|shown foot v_xz| / max(|body v_xz|, " + GovdeHizTabani + ")" +
                      " controls: a1 (expected from the frame's clip weights, +-" + TemasPayiTolerans + ") + a2 + b gate L1; c informational (S-06 08.10) c=" + kontrolC);
            Olc("L1", hatalar, yazilacak, () => L1Hesapla(parcalar, ist), Engel(("(a) temas payı", kontrolA), ("(b) bilinen kayma", kontrolB), ("nişan yönü", yonGecerli)));
            Olc("L1t", hatalar, yazilacak, () => L1tHesapla(parcalar, ist), Engel(("(a) temas payı", kontrolA)));
            Olc("L2", hatalar, yazilacak, () => L2Hesapla(parcalar, ist), Engel(("(a) temas payı", kontrolA), ("nişan yönü", yonGecerli)));
            Olc("L3", hatalar, yazilacak, () => L3Hesapla(parcalar), null);
            Tablo(anim, parcalar, ist, iIkOffRun, iIkOffWalk, iRate, iSalinim);

            // S-10 08.10: the positive controls are decided BEFORE any write — a failing (a) / (b) keeps r2-L1 / L1t / L2.json unwritten
            // (Olc above left them out of yazilacak) and the test fails through hatalar
            Debug.Log("[DD-TEST] wp082.controlsBeforeWrite a(a1+a2)=" + kontrolA + " b=" + kontrolB + " aimDirections=" + yonGecerli + " c(info)=" + kontrolC +
                      " -> write L1=" + Yazilir(yazilacak, "L1") + " L1t=" + Yazilir(yazilacak, "L1t") + " L2=" + Yazilir(yazilacak, "L2") + " L3=" + Yazilir(yazilacak, "L3") + " l1Def=" + L1Tanim);
            // writes first (every red value recorded), the thresholds after; one literal call per gate (r2_gates_lint reads the gate
            // id and the keys at the call)
            foreach (KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>> w in yazilacak)
            {
                Dictionary<string, double> v = w.Value.Key;
                int n = w.Value.Value;
                switch (w.Key)
                {
                    case "L1":
                        R2Olcum.Yaz("L1", TestName, new Dictionary<string, double> { { "avg", v["avg"] }, { "p95", v["p95"] } }, n, "l1Def", L1Tanim);
                        break;
                    case "L1t":
                        R2Olcum.Yaz("L1t", TestName, new Dictionary<string, double> { { "avg", v["avg"] }, { "p95", v["p95"] } }, n, "l1Def", L1Tanim);
                        break;
                    case "L2":
                        R2Olcum.Yaz("L2", TestName, new Dictionary<string, double> { { "deviationMax", v["deviationMax"] } }, n, "l1Def", L1Tanim);
                        break;
                    case "L3":
                        R2Olcum.Yaz("L3", TestName, new Dictionary<string, double>
                        {
                            { "limbDegPerFrameMax", v["limbDegPerFrameMax"] }, { "torsoDegPerFrameMax", v["torsoDegPerFrameMax"] },
                            { "transitionToSteadyRatioMax", v["transitionToSteadyRatioMax"] },
                        }, n);
                        break;
                }
            }
            foreach (KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>> w in yazilacak)
            {
                Dictionary<string, double> v = w.Value.Key;
                switch (w.Key)
                {
                    case "L1":
                        if (v["avg"] > L1Avg) hatalar.Add("R2 L1: avg " + F(v["avg"]) + " > " + L1Avg);
                        if (v["p95"] > L1P95) hatalar.Add("R2 L1: p95 " + F(v["p95"]) + " > " + L1P95);
                        break;
                    case "L1t":
                        if (v["avg"] > L1tAvg) hatalar.Add("R2 L1t: avg " + F(v["avg"]) + " > " + L1tAvg);
                        break;
                    case "L2":
                        if (v["deviationMax"] > L2Dev) hatalar.Add("R2 L2: deviationMax " + F(v["deviationMax"]) + " > " + L2Dev);
                        break;
                    case "L3":
                        if (v["limbDegPerFrameMax"] > L3Limb) hatalar.Add("R2 L3: limbDegPerFrameMax " + F(v["limbDegPerFrameMax"]) + " > " + L3Limb);
                        if (v["torsoDegPerFrameMax"] > L3Torso) hatalar.Add("R2 L3: torsoDegPerFrameMax " + F(v["torsoDegPerFrameMax"]) + " > " + L3Torso);
                        if (v["transitionToSteadyRatioMax"] > L3Ratio) hatalar.Add("R2 L3: transitionToSteadyRatioMax " + F(v["transitionToSteadyRatioMax"]) + " > " + L3Ratio);
                        break;
                }
            }

            Debug.Log("[DD-TEST] wp083.loco written=" + string.Join(",", Anahtarlar(yazilacak)) + " controls a/b/c=" + kontrolA + "/" + kontrolB + "/" + kontrolC);
            if (hatalar.Count > 0) Assert.Fail(string.Join("\n", hatalar));
        }

        private static bool Yazilir(List<KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>>> l, string gate)
        {
            foreach (var kv in l) if (kv.Key == gate) return true;
            return false;
        }

        private static IEnumerable<string> Anahtarlar(List<KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>>> l)
        {
            foreach (var kv in l) yield return kv.Key;
        }

        private static IEnumerable<string> Sticks(float[,] c)
        {
            for (int k = 0; k < 8; k++) yield return YonAd[k] + ":" + c[0, k].ToString("F3", CultureInfo.InvariantCulture);
        }

        /// <summary>The walk ring's contact speed along a body-space direction (PlayerSkinnedVisual.RingAlong's rule: fwd / back by |y|, the side by |x|).</summary>
        private static float HalkaHizi(Vector2 move, float[] speeds)
        {
            float ax = Mathf.Abs(move.x), ay = Mathf.Abs(move.y);
            if (ax + ay < 1e-4f) return speeds[0];
            float fb = move.y >= 0f ? speeds[0] : speeds[1];
            float side = move.x < 0f ? speeds[2] : speeds[3];
            return (ax * side + ay * fb) / (ax + ay);
        }

        private static string Engel(params (string ad, bool gecti)[] kosullar)
        {
            string e = null;
            foreach ((string ad, bool gecti) k in kosullar)
                if (!k.gecti) e = (e == null ? "" : e + ", ") + k.ad;
            return e == null ? null : "pozitif kontrol / ön koşul tutmadı: " + e;
        }

        private struct Sonuc
        {
            public Dictionary<string, double> Degerler;
            public int Ornek;
            public string Eksik;   // the source is not in the recording (gate not written)
        }

        private static void Olc(string gate, List<string> hatalar, List<KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>>> yazilacak, Func<Sonuc> hesap, string engel)
        {
            Sonuc s;
            try { s = hesap(); }
            catch (Exception e)
            {
                Debug.Log("[DD-TEST] wp083." + gate.ToLowerInvariant() + " EXCEPTION " + e);
                hatalar.Add("R2 " + gate + ": ölçüm hatası (yazılmadı) — " + e.GetType().Name + ": " + e.Message);
                return;
            }

            if (s.Eksik == null && s.Degerler != null)
            {
                foreach (KeyValuePair<string, double> kv in s.Degerler)
                    if (double.IsNaN(kv.Value) || double.IsInfinity(kv.Value)) { s.Eksik = kv.Key + " sayı değil"; break; }
                if (s.Ornek <= 0) s.Eksik = "örnek yok";
            }

            if (s.Eksik == null && engel != null) s.Eksik = engel;
            if (s.Eksik != null)
            {
                hatalar.Add("R2 " + gate + ": kaynak kayıtta yok ya da doğrulanmadı, yazılmadı (BLOCKED) — " + s.Eksik);
                return;
            }

            yazilacak.Add(new KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>>(gate, new KeyValuePair<Dictionary<string, double>, int>(s.Degerler, s.Ornek)));
        }

        private static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
        private static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        private static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => "(" + F2(v.x) + "," + F2(v.y) + "," + F2(v.z) + ")";

        private static double P95(List<double> xs) => Yuzdelik(xs, 0.95);

        private static double Yuzdelik(List<double> xs, double q)
        {
            if (xs.Count == 0) return double.NaN;
            var c = new List<double>(xs);
            c.Sort();
            int i = Mathf.Clamp((int)Math.Ceiling(q * c.Count) - 1, 0, c.Count - 1);
            return c[i];
        }

        private static double Ort(List<double> xs)
        {
            if (xs.Count == 0) return double.NaN;
            double s = 0;
            foreach (double x in xs) s += x;
            return s / xs.Count;
        }

        private static double Medyan(List<double> xs)
        {
            if (xs.Count == 0) return double.NaN;
            var c = new List<double>(xs);
            c.Sort();
            return c.Count % 2 == 1 ? c[c.Count / 2] : 0.5 * (c[c.Count / 2 - 1] + c[c.Count / 2]);
        }

        /// <summary>Slide ratio of one contact foot-frame.</summary>
        private static double Oran(Vector3 footV, Vector3 bodyV)
        {
            footV.y = 0f;
            bodyV.y = 0f;
            return footV.magnitude / Math.Max(bodyV.magnitude, GovdeHizTabani);
        }

        private static Vector3 Yatay(Vector3 v) { v.y = 0f; return v; }

        // --------------------------------------------------------------------------------------------------- per segment

        private Istat Hesapla(int idx, int sinceMin, int sinceMax)
        {
            var s = new Istat();
            List<LokomotionKaydedici.Kare> ks = _kayit.Kareler;
            Vector3 sum = Vector3.zero;
            double vs = 0, rate = 0, honest = 0;
            int nL = 0, nR = 0, nEL = 0, nER = 0, first = -1, last = -1;
            double bekL = 0, bekR = 0, fazL = 0, fazR = 0;
            var post = new List<double>(); var animG = new List<double>(); var pin = new List<double>(); var swA = new List<double>(); var swP = new List<double>();
            var stM = new[] { new List<double>(), new List<double>() }; var swM = new[] { new List<double>(), new List<double>() };
            var uSt = new[] { new List<double>(), new List<double>() }; var uSw = new[] { new List<double>(), new List<double>() };
            var pelvis = new List<double>();
            int nPL = 0, nPR = 0, uyum = 0, swYer = 0;
            Vector3 dirAll = Vector3.zero;
            for (int i = 0; i < ks.Count; i++)
                if (ks[i].Etiket == idx && ks[i].Since >= sinceMin && ks[i].Since <= sinceMax && ks[i].Gecerli) dirAll += ks[i].BodyV;
            dirAll = dirAll.sqrMagnitude > 1e-8f ? dirAll.normalized : Vector3.zero;
            for (int i = 0; i < ks.Count; i++)
            {
                LokomotionKaydedici.Kare k = ks[i];
                if (k.Etiket != idx || k.Since < sinceMin || k.Since > sinceMax) continue;
                if (first < 0) first = i;
                last = i;
                if (!k.Gecerli) continue;
                s.N++;
                sum += k.BodyV;
                vs += k.BodyV.magnitude;
                rate += k.Rate;
                honest += k.Honest;
                // L1 sample (S-06 08.10): phase contact ∧ the animated foot's |vertical speed| ≤ 0.15 m/s (R2's vertical rule)
                if (k.TemasL) { nL++; s.KaymaFaz.Add(Oran(k.FootVL, k.BodyV)); if (Mathf.Abs(k.AnimVyL) <= TemasDikeyHiz) s.Kayma.Add(Oran(k.FootVL, k.BodyV)); }
                if (k.TemasR) { nR++; s.KaymaFaz.Add(Oran(k.FootVR, k.BodyV)); if (Mathf.Abs(k.AnimVyR) <= TemasDikeyHiz) s.Kayma.Add(Oran(k.FootVR, k.BodyV)); }
                bekL += k.BekL; bekR += k.BekR; fazL += k.FazL; fazR += k.FazR;
                if (k.TemasEskiL) nEL++;
                if (k.TemasEskiR) nER++;
                if (k.Locked > 0) s.KilitliKare++;
                if (k.RayDigerL || k.RayDigerR) s.RayDiger++;
                post.Add(k.GapL); post.Add(k.GapR);
                animG.Add(k.AnimGapL); animG.Add(k.AnimGapR);
                pin.Add(k.AnimGapL - k.GapL); pin.Add(k.AnimGapR - k.GapR);
                pelvis.Add(k.Pelvis);
                if (k.TemasPozL) nPL++;
                if (k.TemasPozR) nPR++;
                if (k.TemasPozL == k.TemasL) uyum++;
                if (k.TemasPozR == k.TemasR) uyum++;
                for (int f = 0; f < 2; f++)
                {
                    bool st = f == 0 ? k.TemasL : k.TemasR;
                    float ag = f == 0 ? k.AnimGapL : k.AnimGapR, sg = f == 0 ? k.GapL : k.GapR;
                    double u = -Vector3.Dot(Yatay((f == 0 ? k.FootVL : k.FootVR) - k.BodyV), dirAll);
                    (st ? stM[f] : swM[f]).Add(ag);
                    (st ? uSt[f] : uSw[f]).Add(u);
                    if (!st) { swA.Add(ag); swP.Add(sg); if (sg <= 0.01f) swYer++; }
                }
            }

            if (s.N > 0)
            {
                s.V = vs / s.N;
                Vector3 dir = sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector3.zero;
                s.VYon = Vector3.Dot(sum / s.N, dir);
                s.Rate = rate / s.N;
                s.Honest = honest / s.N;
                s.PayL = (double)nL / s.N; s.PayR = (double)nR / s.N;
                s.PayEskiL = (double)nEL / s.N; s.PayEskiR = (double)nER / s.N;
                s.L1Avg = Ort(s.Kayma); s.L1P95 = P95(s.Kayma); s.L1FazAvg = Ort(s.KaymaFaz);
                s.BekL = bekL / s.N; s.BekR = bekR / s.N; s.FazOrtL = fazL / s.N; s.FazOrtR = fazR / s.N;
                s.PostGapMed = Medyan(post); s.AnimGapMed = Medyan(animG); s.PimMed = Medyan(pin);
                s.SalinimAnimMed = Medyan(swA); s.SalinimPostMed = Medyan(swP);
                s.SalinimYerdePay = swA.Count > 0 ? (double)swYer / swA.Count : double.NaN;
                s.PayPozL = (double)nPL / s.N; s.PayPozR = (double)nPR / s.N;
                s.Uyum = uyum / (2.0 * s.N);
                for (int f = 0; f < 2; f++) { s.DurusMed[f] = Medyan(stM[f]); s.SalinimMed[f] = Medyan(swM[f]); s.UDurus[f] = Ort(uSt[f]); s.USalinim[f] = Ort(uSw[f]); }
                s.PelvisMed = Medyan(pelvis);
            }

            if (first >= 0)
            {
                LokomotionKaydedici.Kare a = ks[Math.Max(0, first - 1)], b = ks[last];
                s.Kabul = b.Acc - a.Acc; s.BirakS = b.RelS - a.RelS; s.BirakG = b.RelG - a.RelG; s.BirakL = b.RelL - a.RelL; s.BirakP = b.RelP - a.RelP;
            }

            // cadence (plant events, else contact onsets) and the planted foot's backward speed under the body
            if (s.N > 0 && sinceMax == int.MaxValue)
            {
                Vector3 dir = sum.normalized;
                double uSum = 0;
                int uN = 0;
                var evL = new List<double>(); var evR = new List<double>(); var onL = new List<double>(); var onR = new List<double>();
                for (int i = 0; i < ks.Count; i++)
                {
                    LokomotionKaydedici.Kare k = ks[i];
                    if (k.Etiket != idx || k.Since < sinceMin) continue;
                    if (k.OlayL) evL.Add(k.Zaman);
                    if (k.OlayR) evR.Add(k.Zaman);
                    if (k.BaslangicL) onL.Add(k.Zaman);
                    if (k.BaslangicR) onR.Add(k.Zaman);
                    if (!k.Gecerli) continue;
                    if (k.TemasL) { uSum += -Vector3.Dot(Yatay(k.FootVL - k.BodyV), dir); uN++; }
                    if (k.TemasR) { uSum += -Vector3.Dot(Yatay(k.FootVR - k.BodyV), dir); uN++; }
                }

                bool olay = evL.Count - 1 >= MinDongu && evR.Count - 1 >= MinDongu;
                bool temas = onL.Count - 1 >= MinDongu && onR.Count - 1 >= MinDongu;
                List<double> a = olay ? evL : onL, bb = olay ? evR : onR;
                var araliklar = new List<double>();
                for (int i = 1; i < a.Count; i++) araliklar.Add(a[i] - a[i - 1]);
                for (int i = 1; i < bb.Count; i++) araliklar.Add(bb[i] - bb[i - 1]);
                s.Kaynak = olay ? "events" : temas ? "contacts" : "SHORT";
                s.Dongu = Math.Max(0, Math.Min(a.Count, bb.Count) - 1);
                s.T = Medyan(araliklar);
                s.U = uN > 0 ? uSum / uN : double.NaN;
            }

            return s;
        }

        // --------------------------------------------------------------------------------------------------- positive controls

        /// <summary>WP-082 R3: the AP-026 loop of a ring and a body-space direction (one clip per direction on the 8-way tree).</summary>
        private static string KlipAdi(int gait, int yon) => (gait == 0 ? C.PlayerWalk : gait == 1 ? C.PlayerRun : C.PlayerSprint)[yon & 7];

        /// <summary>One clip's stance share per foot from its events (foot_x → foot_x_off over the loop period); the manifest stance frames for the record.</summary>
        private static bool OlayPayi(CharacterAnimationSet anim, string clip, out double l, out double r, out double stance, out int period)
        {
            l = r = stance = double.NaN;
            period = 0;
            CharacterAnimationSet.Entry e = anim.Find(clip);
            if (e == null || string.IsNullOrEmpty(e.events)) return false;
            period = Mathf.RoundToInt(e.seconds * 30f);
            if (period <= 0) period = Math.Max(1, e.frames - 1);
            int pl = -1, pr = -1, ofl = -1, ofr = -1;
            foreach (string part in e.events.Split(';'))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2 || !int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int f)) continue;
                switch (kv[0].Trim())
                {
                    case "foot_l": pl = f; break;
                    case "foot_r": pr = f; break;
                    case "foot_l_off": ofl = f; break;
                    case "foot_r_off": ofr = f; break;
                }
            }

            if (e.stanceFrames > 0) stance = (double)e.stanceFrames / period;
            if (pl < 0 || pr < 0 || ofl < 0 || ofr < 0) return false;
            l = (double)(((ofl - pl) % period + period) % period) / period;
            r = (double)(((ofr - pr) % period + period) % period) / period;
            return true;
        }

        private static bool Beklenen(CharacterAnimationSet anim, int gait, int yon, out double l, out double r, out double stance, out string klipler)
        {
            l = r = stance = 0;
            klipler = "";
            string c = KlipAdi(gait, yon);
            klipler = c;
            if (!OlayPayi(anim, c, out double cl, out double cr, out double st, out _)) { l = r = stance = double.NaN; return false; }
            l = cl; r = cr; stance = st;
            return true;
        }

        /// <summary>(a) every steady segment's contact share per foot within ±10 points of the clip events' stance share.</summary>
        private bool KontrolA(CharacterAnimationSet anim, List<Parca> parcalar, Istat[] ist, List<string> hatalar)
        {
            bool ok = true;
            var rows = new List<string>();
            for (int i = 0; i < NKararli; i++)
            {
                Parca p = parcalar[i];
                Istat s = ist[i];
                bool varmi = Beklenen(anim, p.Yuruyus, p.Yon, out double el, out double er, out double st, out string kl);
                // (a1, S-06 08.10) the expected share from the frame's real clip weights (GetCurrentAnimatorClipInfo) × each clip's window
                // e3-once5: the thresholded share (Σ w × in-window ≥ 0.5) is not the weighted share where the blended clips' windows differ
                // (walk_back off 8/20 vs strafe off 12/20 → 0.40 vs 0.49); (a1) compares like with like — the mean stance weight
                bool a1 = varmi && s.N > 0 && Math.Abs(s.FazOrtL - s.BekL) <= TemasPayiTolerans && Math.Abs(s.FazOrtR - s.BekR) <= TemasPayiTolerans;
                // (a2) the pose agrees with the phase: per foot the animated sole lower in stance than in swing by ≥ 2 cm, and the stance
                // foot travelling backward under the body while the swing foot travels forward
                bool a2 = true;
                for (int f = 0; f < 2; f++)
                    a2 &= s.SalinimMed[f] - s.DurusMed[f] >= PozAyrim && s.UDurus[f] > 0 && s.USalinim[f] < 0;
                bool satir = a1 && a2;
                ok &= satir;
                rows.Add(p.Ad + " exp(weights) L/R " + F3(s.BekL) + "/" + F3(s.BekR) + " exp(50/50) " + F3(el) + "/" + F3(er) +  " (stanceFrames " + F3(st) + ") meanStanceWeight L/R " + F3(s.FazOrtL) + "/" + F3(s.FazOrtR) + " phase(thresholded) L/R " + F3(s.PayL) + "/" + F3(s.PayR) + " pose L/R " + F3(s.PayPozL) + "/" + F3(s.PayPozR) +
                         " agree " + F3(s.Uyum) + " R2-classifier L/R " + F3(s.PayEskiL) + "/" + F3(s.PayEskiR) + " · animSole stance/swing L " + F3(s.DurusMed[0]) + "/" + F3(s.SalinimMed[0]) +
                         " R " + F3(s.DurusMed[1]) + "/" + F3(s.SalinimMed[1]) + " m · u stance/swing L " + F2(s.UDurus[0]) + "/" + F2(s.USalinim[0]) + " R " + F2(s.UDurus[1]) + "/" + F2(s.USalinim[1]) +
                         " m/s n " + s.N + (a1 ? "" : " a1-OUT") + (a2 ? "" : " a2-OUT") + " [" + kl + "]");
            }

            Debug.Log("[DD-TEST] wp082.controlA contactShare tol=±" + TemasPayiTolerans + " pass=" + ok + " :: " + string.Join(" | ", rows));
            if (!ok) hatalar.Add("pozitif kontrol (a) temas payı FAIL: en az bir kararlı segment klip olaylarının ±" + TemasPayiTolerans + " dışında ya da poz faza uymuyor (wp082.controlA satırı)");
            return ok;
        }

        /// <summary>(b) Verified IK-off and actual LocoRate ×0.5 on sprint_fwd: L1 still must read 0.5 ± 0.1.</summary>
        private bool KontrolB(Istat[] ist, int iRate, List<string> hatalar)
        {
            Istat s = ist[iRate], b = ist[16];
            int frames = 0, wrongForce = 0, served = 0, weighted = 0, locked = 0, wrongRate = 0;
            float maxRateError = 0f, maxWeight = 0f;
            foreach (LokomotionKaydedici.Kare k in _kayit.Kareler)
            {
                if (k.Etiket != iRate || k.Since < Isinma || !k.Gecerli) continue;
                frames++;
                if (k.IkForce != 0) wrongForce++;
                if (k.IkAdapterServed) served++;
                if (float.IsNaN(k.IkWeight) || float.IsInfinity(k.IkWeight) || k.IkWeight > 0.001f) weighted++;
                if (k.Locked != 0) locked++;
                maxWeight = Mathf.Max(maxWeight, k.IkWeight);
                float expectedRate = k.OwnerRate * KontrolRate;
                float error = Mathf.Abs(k.Rate - expectedRate);
                if (float.IsNaN(error) || float.IsInfinity(error) || expectedRate <= 0f || error > 1e-4f) wrongRate++;
                else maxRateError = Mathf.Max(maxRateError, error);
            }

            bool isolated = frames > 0 && wrongForce == 0 && served == 0 && weighted == 0 && locked == 0 && wrongRate == 0;
            bool knownSlide = s.Kayma.Count > 0 && Math.Abs(s.L1Avg - KontrolKayma) <= KontrolKaymaTolerans;
            bool ok = isolated && knownSlide;
            Debug.Log("[DD-TEST] wp082.controlB isolation frames=" + frames + " wrongForce=" + wrongForce + " served=" + served + " residualWeightFrames=" + weighted + " maxWeight=" + F(maxWeight) +
                      " locked=" + locked + " wrongRate=" + wrongRate + " rateErrorMax=" + F(maxRateError) + " pass=" + isolated);
            Debug.Log("[DD-TEST] wp082.controlB knownSlide rate×" + KontrolRate + " L1 avg " + F(s.L1Avg) + " p95 " + F(s.L1P95) + " n " + s.Kayma.Count + " (expected " + KontrolKayma + " ± " + KontrolKaymaTolerans +
                      ") LocoRate " + F3(s.Rate) + " v " + F2(s.V) + " — base run_fwd L1 " + F(b.L1Avg) + " LocoRate " + F3(b.Rate) + " pass=" + ok);
            if (!isolated) hatalar.Add("pozitif kontrol (b) izolasyon FAIL: kare=" + frames + " force=" + wrongForce + " served=" + served + " weight=" + weighted + " locked=" + locked + " rate=" + wrongRate);
            if (!knownSlide) hatalar.Add("pozitif kontrol (b) bilinen kayma FAIL: L1 " + F(s.L1Avg) + " (beklenen " + KontrolKayma + " ± " + KontrolKaymaTolerans + ")");
            return ok;
        }

        /// <summary>(c) StrideEslemesi on the pure run directions: the stride-match slide at the measured speed and clip rate vs the segment's L1 avg.</summary>
        private bool KontrolC(List<Parca> parcalar, Istat[] ist, List<string> hatalar)
        {
            bool ok = true;
            var rows = new List<string>();
            // WP-082 R3: the 8-way tree's own model — the honest speed (Σ w·stride×k / Σ w·cycle) × LocoRate carries the body; the rest slides
            for (int g = 0; g < GaitSayisi; g++)
                foreach (int k in new[] { 0, 2, 4, 6 })
                {
                    int idx = g * 8 + k;
                    Istat s = ist[idx];
                    double slide = CadenceBand.SlideFraction((float)s.VYon, (float)s.Honest, (float)s.Rate);
                    double diff = Math.Abs(s.L1Avg - slide);
                    bool satir = s.Kayma.Count > 0 && diff <= StrideFarkSiniri;
                    ok &= satir;
                    rows.Add(parcalar[idx].Ad + " L1 " + F(s.L1Avg) + " modelSlide " + F(slide) + " diff " + F(diff) + (satir ? " ok" : " OUT") + " (v " + F2(s.VYon) + " honest " + F3(s.Honest) + " m/s, LocoRate " + F3(s.Rate) + ")");
                }

            Debug.Log("[DD-TEST] wp082.controlC strideMatch tol=" + StrideFarkSiniri + " pass=" + ok + " :: " + string.Join(" | ", rows));
            // S-06 08.10: information row — no failure
            return ok;
        }

        /// <summary>The R2 writer's error, from the recording: the shown vs animated sole height, the R2 classifier's share with and without the foot IK.</summary>
        private void KaynakKaniti(CharacterAnimationSet anim, List<Parca> parcalar, Istat[] ist, int iIkOffRun, int iIkOffWalk)
        {
            var rows = new List<string>();
            foreach (int i in new[] { 0, 8, 16, iIkOffWalk, iIkOffRun })
            {
                Istat s = ist[i];
                Beklenen(anim, parcalar[i].Yuruyus, 0, out double el, out double er, out _, out _);
                rows.Add(parcalar[i].Ad + " swingSole animated/shown med " + F3(s.SalinimAnimMed) + "/" + F3(s.SalinimPostMed) + " m (swing shown ≤1 cm " + F2(s.SalinimYerdePay) + ") · pelvis med " + F3(s.PelvisMed) +
                         " m · sole med animated/shown " + F3(s.AnimGapMed) + "/" + F3(s.PostGapMed) +
                         " · IK pin med " + F3(s.PimMed) + " m · share R2-classifier L/R " + F3(s.PayEskiL) + "/" + F3(s.PayEskiR) + " new L/R " + F3(s.PayL) + "/" + F3(s.PayR) + " exp " + F3(el) + "/" + F3(er) +
                         " · R2-classifier L1 would read " + F(R2SiniflayiciL1(i)) + " · rayHitOther " + s.RayDiger);
            }

            Debug.Log("[DD-TEST] wp082.cause :: " + string.Join(" | ", rows));
        }

        /// <summary>L1 of one segment with the R2 classifier (the shown pose), for the cause record.</summary>
        private double R2SiniflayiciL1(int idx)
        {
            var l = new List<double>();
            foreach (LokomotionKaydedici.Kare k in _kayit.Kareler)
            {
                if (k.Etiket != idx || !k.Gecerli || k.Since < Isinma) continue;
                if (k.TemasEskiL) l.Add(Oran(k.FootVL, k.BodyV));
                if (k.TemasEskiR) l.Add(Oran(k.FootVR, k.BodyV));
            }

            return Ort(l);
        }

        // --------------------------------------------------------------------------------------------------- gates

        private Sonuc L1Hesapla(List<Parca> parcalar, Istat[] ist)
        {
            var all = new List<double>();
            var rows = new List<string>();
            string eksik = null;
            for (int idx = 0; idx < NKararli; idx++)
            {
                Parca p = parcalar[idx];
                Istat s = ist[idx];
                all.AddRange(s.Kayma);
                double cmd = p.KomutHizN > 0 ? p.KomutHizToplam / p.KomutHizN : double.NaN;
                bool kararli = !double.IsNaN(cmd) && cmd > 0.1 && Math.Abs(s.V - cmd) / cmd <= KararliTolerans;
                rows.Add(p.Ad + " avg " + F(s.L1Avg) + " p95 " + F(s.L1P95) + " n " + s.Kayma.Count + " (phase-all " + F(s.L1FazAvg) + " n " + s.KaymaFaz.Count + ") v " + F2(s.V) + "/" + F2(cmd) + " m/s share L/R " + F3(s.PayL) + "/" + F3(s.PayR) +
                         " pos " + V(p.Bas) + ">" + V(p.Son) + " rayOther " + s.RayDiger +
                         (kararli ? "" : " NOT-STEADY"));
                if (s.Kayma.Count == 0) eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": temas karesi yok";
                else if (!kararli) eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": kararlı yürüyüş yok (v " + F2(s.V) + " / komut " + F2(cmd) + ")";
            }

            double avg = Ort(all), p95 = P95(all);
            Debug.Log("[DD-TEST] wp083.l1 avg=" + F(avg) + " p95=" + F(p95) + " samples=" + all.Count + " floor=" + GovdeHizTabani + " m/s :: " + string.Join(" | ", rows));
            return new Sonuc { Degerler = new Dictionary<string, double> { { "avg", avg }, { "p95", p95 } }, Ornek = all.Count, Eksik = eksik };
        }

        private Sonuc L1tHesapla(List<Parca> parcalar, Istat[] ist)
        {
            var all = new List<double>();
            var byKind = new Dictionary<Tur, List<double>>();
            int pencereli = 0;
            for (int idx = _gecisBasi; idx < parcalar.Count; idx++)
            {
                Parca p = parcalar[idx];
                if (!byKind.TryGetValue(p.Tur, out List<double> list)) byKind[p.Tur] = list = new List<double>();
                list.AddRange(ist[idx].Kayma);
                all.AddRange(ist[idx].Kayma);
                if (_kayit.PencereKareSayisi(idx, 1, L1tPencere) >= L1tPencere) pencereli++;
            }

            var rows = new List<string>();
            string eksik = null;
            if (pencereli < 4 * Tekrar) eksik = "tam pencereli geçiş " + pencereli + "/" + (4 * Tekrar);
            foreach (Tur t in new[] { Tur.Kalkis, Tur.Durus, Tur.Pivot180, Tur.Kesme90 })
            {
                List<double> l = byKind.TryGetValue(t, out List<double> x) ? x : new List<double>();
                rows.Add(t + " avg " + F(Ort(l)) + " p95 " + F(P95(l)) + " n " + l.Count);
                if (l.Count == 0) eksik = (eksik == null ? "" : eksik + "; ") + t + ": geçiş penceresinde temas karesi yok";
            }

            double avg = Ort(all), p95 = P95(all);
            Debug.Log("[DD-TEST] wp083.l1t avg=" + F(avg) + " p95=" + F(p95) + " samples=" + all.Count + " window=" + L1tPencere + " frames :: " + string.Join(" | ", rows));
            return new Sonuc { Degerler = new Dictionary<string, double> { { "avg", avg }, { "p95", p95 } }, Ornek = all.Count, Eksik = eksik };
        }

        private Sonuc L2Hesapla(List<Parca> parcalar, Istat[] ist)
        {
            double devMax = 0;
            int toplamDongu = 0;
            var rows = new List<string>();
            string eksik = null;
            for (int idx = 0; idx < NKararli; idx++)
            {
                Parca p = parcalar[idx];
                Istat s = ist[idx];
                if (s.N == 0 || s.VYon < 1e-3) { eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": gövde hareketi yok"; rows.Add(p.Ad + " n/a"); continue; }
                double kadans = 2.0 / s.T;            // steps / s
                double adim = s.U * s.T / 2.0;         // m per step, planted-foot implied
                double dev = Math.Abs(kadans * adim - s.VYon) / s.VYon;
                rows.Add(p.Ad + " src " + s.Kaynak + " cycles " + s.Dongu + " T " + F(s.T) + " s cadence " + F2(kadans * 60) + " spm step(u) " + F(adim) + " m step(v) " + F(s.VYon * s.T / 2.0) +
                         " m v " + F2(s.VYon) + " u " + F2(s.U) + " dev " + F(dev));
                if (s.Kaynak == "SHORT") { eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": < " + MinDongu + " döngü"; continue; }
                if (double.IsNaN(dev)) { eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": temas karesi yok"; continue; }
                devMax = Math.Max(devMax, dev);
                toplamDongu += s.Dongu;
            }

            Debug.Log("[DD-TEST] wp083.l2 deviationMax=" + F(devMax) + " cycles=" + toplamDongu + " :: " + string.Join(" | ", rows));
            return new Sonuc { Degerler = new Dictionary<string, double> { { "deviationMax", devMax } }, Ornek = toplamDongu, Eksik = eksik };
        }

        private bool L3Olcer(Parca p, LokomotionKaydedici.Kare k) =>
            p.Tur == Tur.Kararli || p.Tur == Tur.Kontrol || p.Tur == Tur.NisanSalinim ? k.Since >= Isinma : k.Since >= 1 && k.Since <= L3Pencere;

        private Sonuc L3Hesapla(List<Parca> parcalar)
        {
            int nb = Kemikler.Length;
            List<LokomotionKaydedici.Kare> ks = _kayit.Kareler;
            List<float> deg = _kayit.Derece;
            // peaks per segment per bone (steady: after the warm-up; transitions: the 1 s window)
            var tepe = new double[parcalar.Count, nb];
            var tepeKare = new int[parcalar.Count, nb];
            var pencereKare = new int[parcalar.Count];
            var buyuk = new List<KeyValuePair<float, int>>();   // (°/frame, i * nb + b) of limb frames over the limb limit
            for (int i = 0; i < ks.Count; i++)
            {
                LokomotionKaydedici.Kare k = ks[i];
                if (k.Etiket < 0 || !k.Gecerli) continue;
                Parca p = parcalar[k.Etiket];
                if (!L3Olcer(p, k)) continue;
                pencereKare[k.Etiket]++;
                for (int b = 0; b < nb; b++)
                {
                    float d = deg[i * nb + b];
                    if (float.IsNaN(d)) continue;
                    if (d > tepe[k.Etiket, b]) { tepe[k.Etiket, b] = d; tepeKare[k.Etiket, b] = k.Since; }
                    if (b < UzuvSayisi && d > L3Limb) buyuk.Add(new KeyValuePair<float, int>(d, i * nb + b));
                }
            }

            double limb = 0, torso = 0, ratio = 0;
            string limbAt = "", torsoAt = "", ratioAt = "";
            int ornek = 0, gecis = 0;
            var rows = new List<string>();
            string eksik = null;
            for (int t = _gecisBasi; t < parcalar.Count; t++)
            {
                Parca p = parcalar[t];
                if (pencereKare[t] == 0) { eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": pencerede kare yok"; continue; }
                gecis++;
                ornek += pencereKare[t];
                double pl = 0, pt = 0, pr = 0;
                string prAt = "";
                for (int b = 0; b < nb; b++)
                {
                    if (!_kayit.KemikVar(b)) continue;
                    double v = tepe[t, b];
                    if (b < UzuvSayisi) { if (v > pl) pl = v; if (v > limb) { limb = v; limbAt = p.Ad + " " + Kemikler[b] + " @" + tepeKare[t, b]; } }
                    else { if (v > pt) pt = v; if (v > torso) { torso = v; torsoAt = p.Ad + " " + Kemikler[b] + " @" + tepeKare[t, b]; } }
                    double refPeak = 0;
                    foreach (int r in p.Ref) refPeak = Math.Max(refPeak, tepe[r, b]);
                    double q = v / Math.Max(refPeak, KararliTepeTabani);
                    if (q > pr) { pr = q; prAt = Kemikler[b] + " " + F2(v) + "/" + F2(refPeak); }
                    if (q > ratio) { ratio = q; ratioAt = p.Ad + " " + Kemikler[b] + " " + F2(v) + "°/" + F2(refPeak) + "° @" + tepeKare[t, b]; }
                }

                rows.Add(p.Ad + " limb " + F2(pl) + " torso " + F2(pt) + " ratio " + F2(pr) + " (" + prAt + ")");
            }

            for (int r = 16; r < NKararli; r++)
                if (pencereKare[r] == 0) eksik = (eksik == null ? "" : eksik + "; ") + parcalar[r].Ad + ": kararlı koşu tepesi yok (oran tabanı)";
            if (gecis < 4 * Tekrar) eksik = (eksik == null ? "" : eksik + "; ") + "geçiş " + gecis + "/" + (4 * Tekrar);
            if (!_kayit.KemikVar(0) || !_kayit.KemikVar(12)) eksik = (eksik == null ? "" : eksik + "; ") + "uzuv/gövde kemiği yok: " + _kayit.BulunanKemikler();

            var steady = new StringBuilder();
            for (int r = 0; r < _gecisBasi; r++)
            {
                double sl = 0, st = 0;
                for (int b = 0; b < nb; b++) { if (b < UzuvSayisi) sl = Math.Max(sl, tepe[r, b]); else st = Math.Max(st, tepe[r, b]); }
                steady.Append(parcalar[r].Ad).Append(' ').Append(F2(sl)).Append('/').Append(F2(st)).Append(r < _gecisBasi - 1 ? ", " : "");
            }

            Debug.Log("[DD-TEST] wp083.l3 limbDegPerFrameMax=" + F2(limb) + " (" + limbAt + ") torsoDegPerFrameMax=" + F2(torso) + " (" + torsoAt + ") transitionToSteadyRatioMax=" + F2(ratio) +
                      " (" + ratioAt + ") transitions=" + gecis + " frames=" + ornek + " window=" + L3Pencere + " floor=" + KararliTepeTabani + "°/frame :: steady limb/torso " + steady +
                      " :: " + string.Join(" | ", rows));
            L3Kaynak(parcalar, tepe, buyuk);
            return new Sonuc
            {
                Degerler = new Dictionary<string, double> { { "limbDegPerFrameMax", limb }, { "torsoDegPerFrameMax", torso }, { "transitionToSteadyRatioMax", ratio } },
                Ornek = ornek,
                Eksik = eksik,
            };
        }

        /// <summary>
        /// L3's source (PLAN §5.1 (4)): every limb frame over 20°/frame classified — a hand / arm bone (the hold and grip IK, WeaponGripIk θ),
        /// a leg bone within ±2 frames of a foot-lock release, a leg bone where the foot IK's pin (animated − shown sole) jumped ≥ 5 cm
        /// within ±1 frame, other leg; the bone-by-bone peak table and the top 12 frames with their context.
        /// </summary>
        private void L3Kaynak(List<Parca> parcalar, double[,] tepe, List<KeyValuePair<float, int>> buyuk)
        {
            int nb = Kemikler.Length;
            List<LokomotionKaydedici.Kare> ks = _kayit.Kareler;
            int el = 0, kilit = 0, pim = 0, pelvis = 0, diger = 0;
            foreach (KeyValuePair<float, int> x in buyuk)
            {
                int i = x.Value / nb, b = x.Value % nb;
                if (b >= 6) { el++; continue; }
                if (BirakmaYakin(i, 2)) { kilit++; continue; }
                if (ks[i].Pelvis <= -0.10f) { pelvis++; continue; }
                if (PimSicrama(i, b, 1) >= 0.05f) { pim++; continue; }
                diger++;
            }

            var kemik = new StringBuilder();
            for (int b = 0; b < nb; b++)
            {
                double gs = 0, gt = 0;
                for (int r = 0; r < parcalar.Count; r++) { if (r < _gecisBasi) gs = Math.Max(gs, tepe[r, b]); else gt = Math.Max(gt, tepe[r, b]); }
                kemik.Append(Kemikler[b]).Append(' ').Append(F2(gs)).Append('/').Append(F2(gt)).Append(b < nb - 1 ? ", " : "");
            }

            buyuk.Sort((a, c) => c.Key.CompareTo(a.Key));
            var top = new List<string>();
            for (int t = 0; t < Math.Min(12, buyuk.Count); t++)
            {
                int i = buyuk[t].Value / nb, b = buyuk[t].Value % nb;
                LokomotionKaydedici.Kare k = ks[i];
                top.Add(parcalar[k.Etiket].Ad + " @" + k.Since + " " + Kemikler[b] + " " + F2(buyuk[t].Key) + "° release±2 " + BirakmaYakin(i, 2) + " pinJump±1 " + F3(PimSicrama(i, b, 1)) +
                        " locked " + k.Locked + " soleAnim L/R " + F3(k.AnimGapL) + "/" + F3(k.AnimGapR) + " pelvis " + F3(k.Pelvis) + " rate " + F2(k.Rate));
            }

            Debug.Log("[DD-TEST] wp082.l3source limbFrames>" + L3Limb + "°=" + buyuk.Count + " hand/arm " + el + " · leg+lockRelease " + kilit + " · leg+pelvisDrop≥10cm " + pelvis + " · leg+IKpinJump≥5cm " + pim + " · leg other " + diger +
                      " :: bone peak steady/transition " + kemik + " :: top " + string.Join(" | ", top));
        }

        private bool BirakmaYakin(int i, int w)
        {
            List<LokomotionKaydedici.Kare> ks = _kayit.Kareler;
            int a = Math.Max(1, i - w), b = Math.Min(ks.Count - 1, i + w);
            for (int j = a; j <= b; j++)
            {
                LokomotionKaydedici.Kare k = ks[j], o = ks[j - 1];
                if (k.RelS + k.RelG + k.RelL + k.RelP != o.RelS + o.RelG + o.RelL + o.RelP) return true;
            }

            return false;
        }

        private float PimSicrama(int i, int b, int w)
        {
            List<LokomotionKaydedici.Kare> ks = _kayit.Kareler;
            bool sol = (b & 1) == 0;
            float m = 0f;
            int a = Math.Max(1, i - w), e = Math.Min(ks.Count - 1, i + w);
            for (int j = a; j <= e; j++)
            {
                float p0 = sol ? ks[j - 1].AnimGapL - ks[j - 1].GapL : ks[j - 1].AnimGapR - ks[j - 1].GapR;
                float p1 = sol ? ks[j].AnimGapL - ks[j].GapL : ks[j].AnimGapR - ks[j].GapR;
                if (!float.IsNaN(p0) && !float.IsNaN(p1)) m = Mathf.Max(m, Mathf.Abs(p1 - p0));
            }

            return m;
        }

        /// <summary>Diagnostic CSV: existing cause/control segments, aim swing, and every course B transition. Acceptance is unchanged.</summary>
        private void Dok(List<Parca> parcalar, int iIkOffRun, int iIkOffWalk, int iRate)
        {
            string dir = Environment.GetEnvironmentVariable("DD_LOKO_DUMP_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            var sec = new HashSet<int> { 0, 8, 16, 20, iIkOffRun, iIkOffWalk, iRate };
            for (int i = 0; i < parcalar.Count; i++)
                if (parcalar[i].Tur == Tur.NisanSalinim || parcalar[i].Tur == Tur.Kalkis || parcalar[i].Tur == Tur.Durus ||
                    parcalar[i].Tur == Tur.Pivot180 || parcalar[i].Tur == Tur.Kesme90) sec.Add(i);
            var sb = new StringBuilder("label,since,frame,valid,phaseL,phaseR,contactL,contactR,poseL,poseR,r2L,r2R,animSoleL,animSoleR,shownSoleL,shownSoleR,animVyL,animVyR," +
                                       "footVxzL,footVxzR,bodyVxz,pelvis,locked,rate,plantL,plantR,offL,offR,ikServed,ownerRate,ikForce,ikAdapterServed,ikWeight," +
                                       "unityFrame,rawGoalFrame,rawGoalSameFrameL,rawGoalSameFrameR,rawGoalVelocityValidL,rawGoalVelocityValidR," +
                                       "rawGoalLx,rawGoalLy,rawGoalLz,rawGoalRx,rawGoalRy,rawGoalRz,rawGoalVxL,rawGoalVyL,rawGoalVzL,rawGoalVxR,rawGoalVyR,rawGoalVzR," +
                                       "rawGoalMinusShownBoneLx,rawGoalMinusShownBoneLy,rawGoalMinusShownBoneLz,rawGoalMinusShownBoneRx,rawGoalMinusShownBoneRy,rawGoalMinusShownBoneRz,oldEstimateVyL,oldEstimateVyR," +
                                       "diagCaptured,dt,baseCurrentHash,baseCurrentNorm,baseNextHash,baseNextNorm,baseTransition,baseTransProgress,baseTransitionHash,layersJson," +
                                       "v4Kind,v4Id,v4T,v4DeltaT,v4DeltaValid,v4EntryT,v4ClipParam,v4TimeParam,v4NaturalDeltaT,v4StopTD,v4AimDriven,locoSpeed,moveX,moveY," +
                                       "rootQx,rootQy,rootQz,rootQw,worstLimbBone,worstLimbDeg,worstLimbPrevQx,worstLimbPrevQy,worstLimbPrevQz,worstLimbPrevQw,worstLimbQx,worstLimbQy,worstLimbQz,worstLimbQw," +
                                       "worstTorsoBone,worstTorsoDeg,worstTorsoPrevQx,worstTorsoPrevQy,worstTorsoPrevQz,worstTorsoPrevQw,worstTorsoQx,worstTorsoQy,worstTorsoQz,worstTorsoQw,releaseStretch,releaseGate,releaseLift,releasePlant\n");
            foreach (LokomotionKaydedici.Kare k in _kayit.Kareler)
            {
                if (!sec.Contains(k.Etiket)) continue;
                sb.Append(Csv(parcalar[k.Etiket].Ad)).Append(',').Append(k.Since).Append(',').Append(k.Frame).Append(',').Append(k.Gecerli ? 1 : 0).Append(',')
                  .Append(F3(k.FazL)).Append(',').Append(F3(k.FazR)).Append(',').Append(k.TemasL ? 1 : 0).Append(',').Append(k.TemasR ? 1 : 0).Append(',')
                  .Append(k.TemasPozL ? 1 : 0).Append(',').Append(k.TemasPozR ? 1 : 0).Append(',').Append(k.TemasEskiL ? 1 : 0).Append(',').Append(k.TemasEskiR ? 1 : 0).Append(',')
                  .Append(F(k.AnimGapL)).Append(',').Append(F(k.AnimGapR)).Append(',').Append(F(k.GapL)).Append(',').Append(F(k.GapR)).Append(',')
                  .Append(F3(k.AnimVyL)).Append(',').Append(F3(k.AnimVyR)).Append(',').Append(F3(Yatay(k.FootVL).magnitude)).Append(',').Append(F3(Yatay(k.FootVR).magnitude)).Append(',')
                  .Append(F3(k.BodyV.magnitude)).Append(',').Append(F(k.Pelvis)).Append(',').Append(k.Locked).Append(',').Append(F3(k.Rate)).Append(',')
                  .Append(k.OlayL ? 1 : 0).Append(',').Append(k.OlayR ? 1 : 0).Append(',').Append(k.OffL ? 1 : 0).Append(',').Append(k.OffR ? 1 : 0).Append(',').Append(k.IkServed ? 1 : 0).Append(',')
                  .Append(F(k.OwnerRate)).Append(',').Append(k.IkForce).Append(',').Append(k.IkAdapterServed ? 1 : 0).Append(',').Append(F(k.IkWeight)).Append(',')
                  .Append(k.UnityFrame).Append(',').Append(k.RawGoalFrame).Append(',').Append(k.RawGoalL ? 1 : 0).Append(',').Append(k.RawGoalR ? 1 : 0).Append(',')
                  .Append(k.RawGoalVelocityL ? 1 : 0).Append(',').Append(k.RawGoalVelocityR ? 1 : 0).Append(',')
                  .Append(F(k.RawGoalPosL.x)).Append(',').Append(F(k.RawGoalPosL.y)).Append(',').Append(F(k.RawGoalPosL.z)).Append(',')
                  .Append(F(k.RawGoalPosR.x)).Append(',').Append(F(k.RawGoalPosR.y)).Append(',').Append(F(k.RawGoalPosR.z)).Append(',')
                  .Append(F3(k.RawGoalVL.x)).Append(',').Append(F3(k.RawGoalVL.y)).Append(',').Append(F3(k.RawGoalVL.z)).Append(',')
                  .Append(F3(k.RawGoalVR.x)).Append(',').Append(F3(k.RawGoalVR.y)).Append(',').Append(F3(k.RawGoalVR.z)).Append(',')
                  .Append(F(k.RawGoalMinusShownBoneL.x)).Append(',').Append(F(k.RawGoalMinusShownBoneL.y)).Append(',').Append(F(k.RawGoalMinusShownBoneL.z)).Append(',')
                  .Append(F(k.RawGoalMinusShownBoneR.x)).Append(',').Append(F(k.RawGoalMinusShownBoneR.y)).Append(',').Append(F(k.RawGoalMinusShownBoneR.z)).Append(',')
                  .Append(F3(k.AnimVyL)).Append(',').Append(F3(k.AnimVyR)).Append(',')
                  .Append(k.DiagCaptured ? 1 : 0).Append(',').Append(DR(k.Dt)).Append(',').Append(k.BaseCurrentHash).Append(',').Append(DR(k.BaseCurrentNorm)).Append(',')
                  .Append(k.BaseNextHash).Append(',').Append(DR(k.BaseNextNorm)).Append(',').Append(k.BaseTransition ? 1 : 0).Append(',').Append(DR(k.BaseTransProgress)).Append(',')
                  .Append(k.BaseTransitionHash).Append(',').Append(Csv(k.LayersJson)).Append(',').Append(k.V4Kind).Append(',').Append(k.V4Id).Append(',').Append(DR(k.V4T)).Append(',')
                  .Append(DR(k.V4DeltaT)).Append(',').Append(k.V4DeltaValid ? 1 : 0).Append(',').Append(DR(k.V4EntryT)).Append(',').Append(k.V4ClipParam).Append(',').Append(DR(k.V4TimeParam)).Append(',')
                  .Append(DR(k.V4NaturalDeltaT)).Append(',').Append(DR(k.V4StopTD)).Append(',').Append(k.V4AimDriven ? 1 : 0).Append(',').Append(DR(k.LocoSpeed)).Append(',')
                  .Append(DR(k.MoveX)).Append(',').Append(DR(k.MoveY)).Append(',');
                AppendQ(sb, k.RootRotation);
                AppendWorst(sb, k.WorstLimbBone, k.WorstLimbDeg, k.WorstLimbPrev, k.WorstLimbNow);
                AppendWorst(sb, k.WorstTorsoBone, k.WorstTorsoDeg, k.WorstTorsoPrev, k.WorstTorsoNow);
                sb.Append(',').Append(k.RelS).Append(',').Append(k.RelG).Append(',').Append(k.RelL).Append(',').Append(k.RelP).Append('\n');
            }

            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "loko-frames.csv");
            System.IO.File.WriteAllText(path, sb.ToString());
            DokIkTrace(parcalar, sec, dir);
            Debug.Log("[DD-TEST] wp082.dump " + path + " rows " + sb.Length);
        }

        // Diagnostic sidecar. Internal goals and final bones keep distinct bases; no acceptance input is replaced.
        private void DokIkTrace(List<Parca> parcalar, HashSet<int> selected, string dir)
        {
            if (_kayit.IkTraceSamples == null) return;
            var trace = new StringBuilder("label,since,recorderFrame,unityFrame,adapterInstanceId,dt,goalAvailable,samePassBothFeet,goalFrame,goalLayerIndex,goalSequence,goalPairCaptured,goalOutcome,forceAtEntry,allowed,budget,internalEntryRootWorldQx,internalEntryRootWorldQy,internalEntryRootWorldQz,internalEntryRootWorldQw,internalEntryLeftWorldQx,internalEntryLeftWorldQy,internalEntryLeftWorldQz,internalEntryLeftWorldQw,internalEntryRightWorldQx,internalEntryRightWorldQy,internalEntryRightWorldQz,internalEntryRightWorldQw,internalEntryLeftWorldPosx,internalEntryLeftWorldPosy,internalEntryLeftWorldPosz,internalEntryRightWorldPosx,internalEntryRightWorldPosy,internalEntryRightWorldPosz,internalEntryLeftPositionWeight,internalEntryRightPositionWeight,internalEntryLeftRotationWeight,internalEntryRightRotationWeight,internalAfterPairRootWorldQx,internalAfterPairRootWorldQy,internalAfterPairRootWorldQz,internalAfterPairRootWorldQw,internalAfterPairLeftWorldQx,internalAfterPairLeftWorldQy,internalAfterPairLeftWorldQz,internalAfterPairLeftWorldQw,internalAfterPairRightWorldQx,internalAfterPairRightWorldQy,internalAfterPairRightWorldQz,internalAfterPairRightWorldQw,internalAfterPairLeftWorldPosx,internalAfterPairLeftWorldPosy,internalAfterPairLeftWorldPosz,internalAfterPairRightWorldPosx,internalAfterPairRightWorldPosy,internalAfterPairRightWorldPosz,internalAfterPairLeftPositionWeight,internalAfterPairRightPositionWeight,internalAfterPairLeftRotationWeight,internalAfterPairRightRotationWeight,prePairLLocked,prePairLReleasePending,prePairLGateOpenCached,prePairLGateEvaluatedThisPass,prePairLServed,prePairLSupportsZombieData,prePairLLockPositionx,prePairLLockPositiony,prePairLLockPositionz,prePairLLockWeight,prePairLAdapterWeight,prePairLPlantPending,prePairLZombiePlantPending,prePairLReleaseStretch,prePairLReleaseGate,prePairLReleaseLift,prePairLReleasePlant,prePairLReleaseOffset,prePairRLocked,prePairRReleasePending,prePairRGateOpenCached,prePairRGateEvaluatedThisPass,prePairRServed,prePairRSupportsZombieData,prePairRLockPositionx,prePairRLockPositiony,prePairRLockPositionz,prePairRLockWeight,prePairRAdapterWeight,prePairRPlantPending,prePairRZombiePlantPending,prePairRReleaseStretch,prePairRReleaseGate,prePairRReleaseLift,prePairRReleasePlant,prePairRReleaseOffset,prePairPelvisOffset,prePairCommonDelta,postPairLLocked,postPairLReleasePending,postPairLGateOpenCached,postPairLGateEvaluatedThisPass,postPairLServed,postPairLSupportsZombieData,postPairLLockPositionx,postPairLLockPositiony,postPairLLockPositionz,postPairLLockWeight,postPairLAdapterWeight,postPairLPlantPending,postPairLZombiePlantPending,postPairLReleaseStretch,postPairLReleaseGate,postPairLReleaseLift,postPairLReleasePlant,postPairLReleaseOffset,postPairRLocked,postPairRReleasePending,postPairRGateOpenCached,postPairRGateEvaluatedThisPass,postPairRServed,postPairRSupportsZombieData,postPairRLockPositionx,postPairRLockPositiony,postPairRLockPositionz,postPairRLockWeight,postPairRAdapterWeight,postPairRPlantPending,postPairRZombiePlantPending,postPairRReleaseStretch,postPairRReleaseGate,postPairRReleaseLift,postPairRReleasePlant,postPairRReleaseOffset,postPairPelvisOffset,postPairCommonDelta,adapterExitLLocked,adapterExitLReleasePending,adapterExitLGateOpenCached,adapterExitLGateEvaluatedThisPass,adapterExitLServed,adapterExitLSupportsZombieData,adapterExitLLockPositionx,adapterExitLLockPositiony,adapterExitLLockPositionz,adapterExitLLockWeight,adapterExitLAdapterWeight,adapterExitLPlantPending,adapterExitLZombiePlantPending,adapterExitLReleaseStretch,adapterExitLReleaseGate,adapterExitLReleaseLift,adapterExitLReleasePlant,adapterExitLReleaseOffset,adapterExitRLocked,adapterExitRReleasePending,adapterExitRGateOpenCached,adapterExitRGateEvaluatedThisPass,adapterExitRServed,adapterExitRSupportsZombieData,adapterExitRLockPositionx,adapterExitRLockPositiony,adapterExitRLockPositionz,adapterExitRLockWeight,adapterExitRAdapterWeight,adapterExitRPlantPending,adapterExitRZombiePlantPending,adapterExitRReleaseStretch,adapterExitRReleaseGate,adapterExitRReleaseLift,adapterExitRReleasePlant,adapterExitRReleaseOffset,adapterExitPelvisOffset,adapterExitCommonDelta,lockLAvailable,lockLFrame,lockLLayerIndex,lockLSequence,lockLFoot,lockLOffsetEvaluated,lockLAnimatedInputx,lockLAnimatedInputy,lockLAnimatedInputz,lockLTargetInputx,lockLTargetInputy,lockLTargetInputz,lockLOffXZx,lockLOffXZy,lockLOffXZz,lockLOffSquared,lockLReturnedTargetx,lockLReturnedTargety,lockLReturnedTargetz,lockLBranches,lockLBeforeLocked,lockLBeforeReleasePending,lockLBeforeGateOpenCached,lockLBeforeGateEvaluatedThisPass,lockLBeforeServed,lockLBeforeSupportsZombieData,lockLBeforeLockPositionx,lockLBeforeLockPositiony,lockLBeforeLockPositionz,lockLBeforeLockWeight,lockLBeforeAdapterWeight,lockLBeforePlantPending,lockLBeforeZombiePlantPending,lockLBeforeReleaseStretch,lockLBeforeReleaseGate,lockLBeforeReleaseLift,lockLBeforeReleasePlant,lockLBeforeReleaseOffset,lockLAfterLocked,lockLAfterReleasePending,lockLAfterGateOpenCached,lockLAfterGateEvaluatedThisPass,lockLAfterServed,lockLAfterSupportsZombieData,lockLAfterLockPositionx,lockLAfterLockPositiony,lockLAfterLockPositionz,lockLAfterLockWeight,lockLAfterAdapterWeight,lockLAfterPlantPending,lockLAfterZombiePlantPending,lockLAfterReleaseStretch,lockLAfterReleaseGate,lockLAfterReleaseLift,lockLAfterReleasePlant,lockLAfterReleaseOffset,lockRAvailable,lockRFrame,lockRLayerIndex,lockRSequence,lockRFoot,lockROffsetEvaluated,lockRAnimatedInputx,lockRAnimatedInputy,lockRAnimatedInputz,lockRTargetInputx,lockRTargetInputy,lockRTargetInputz,lockROffXZx,lockROffXZy,lockROffXZz,lockROffSquared,lockRReturnedTargetx,lockRReturnedTargety,lockRReturnedTargetz,lockRBranches,lockRBeforeLocked,lockRBeforeReleasePending,lockRBeforeGateOpenCached,lockRBeforeGateEvaluatedThisPass,lockRBeforeServed,lockRBeforeSupportsZombieData,lockRBeforeLockPositionx,lockRBeforeLockPositiony,lockRBeforeLockPositionz,lockRBeforeLockWeight,lockRBeforeAdapterWeight,lockRBeforePlantPending,lockRBeforeZombiePlantPending,lockRBeforeReleaseStretch,lockRBeforeReleaseGate,lockRBeforeReleaseLift,lockRBeforeReleasePlant,lockRBeforeReleaseOffset,lockRAfterLocked,lockRAfterReleasePending,lockRAfterGateOpenCached,lockRAfterGateEvaluatedThisPass,lockRAfterServed,lockRAfterSupportsZombieData,lockRAfterLockPositionx,lockRAfterLockPositiony,lockRAfterLockPositionz,lockRAfterLockWeight,lockRAfterAdapterWeight,lockRAfterPlantPending,lockRAfterZombiePlantPending,lockRAfterReleaseStretch,lockRAfterReleaseGate,lockRAfterReleaseLift,lockRAfterReleasePlant,lockRAfterReleaseOffset,lateRootWorldQx,lateRootWorldQy,lateRootWorldQz,lateRootWorldQw,lateLeftRootQx,lateLeftRootQy,lateLeftRootQz,lateLeftRootQw,lateRightRootQx,lateRightRootQy,lateRightRootQz,lateRightRootQw\n");
            int traceWritten = 0;
            foreach (LokomotionKaydedici.IkTraceSample t in _kayit.IkTraceSamples)
            {
                if (!selected.Contains(t.Label)) continue;
                traceWritten++;
                trace.Append(Csv(parcalar[t.Label].Ad)); trace.Append(',');
                trace.Append(t.Since); trace.Append(',');
                trace.Append(t.RecorderFrame); trace.Append(',');
                trace.Append(t.UnityFrame); trace.Append(',');
                trace.Append(t.AdapterInstanceId); trace.Append(',');
                trace.Append(DR(t.Dt)); trace.Append(',');
                trace.Append((t.GoalAvailable ? 1 : 0)); trace.Append(',');
                trace.Append((t.SamePassBothFeet ? 1 : 0)); trace.Append(',');
                trace.Append(t.Goal.Stamp.Frame); trace.Append(',');
                trace.Append(t.Goal.Stamp.LayerIndex); trace.Append(',');
                trace.Append(t.Goal.Stamp.Sequence); trace.Append(',');
                trace.Append((t.Goal.PairCaptured ? 1 : 0)); trace.Append(',');
                trace.Append((int)t.Goal.Outcome); trace.Append(',');
                trace.Append(t.Goal.ForceAtEntry); trace.Append(',');
                trace.Append((t.Goal.Allowed ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.Budget ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RootWorldRotation.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RootWorldRotation.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RootWorldRotation.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RootWorldRotation.w)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.LeftWorldRotation.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.LeftWorldRotation.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.LeftWorldRotation.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.LeftWorldRotation.w)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RightWorldRotation.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RightWorldRotation.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RightWorldRotation.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RightWorldRotation.w)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.LeftWorldPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.LeftWorldPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.LeftWorldPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RightWorldPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RightWorldPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RightWorldPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.LeftPositionWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RightPositionWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.LeftRotationWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalEntry.RightRotationWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RootWorldRotation.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RootWorldRotation.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RootWorldRotation.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RootWorldRotation.w)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.LeftWorldRotation.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.LeftWorldRotation.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.LeftWorldRotation.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.LeftWorldRotation.w)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RightWorldRotation.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RightWorldRotation.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RightWorldRotation.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RightWorldRotation.w)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.LeftWorldPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.LeftWorldPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.LeftWorldPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RightWorldPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RightWorldPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RightWorldPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.LeftPositionWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RightPositionWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.LeftRotationWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.InternalGoalAfterPair.RightRotationWeight)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Left.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Left.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Left.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Left.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Left.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Left.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Left.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Left.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Left.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Left.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Left.AdapterWeight)); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Left.PlantPending); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Left.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Left.Releases.Stretch); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Left.Releases.Gate); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Left.Releases.Lift); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Left.Releases.Plant); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Left.Releases.Offset); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Right.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Right.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Right.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Right.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Right.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.BeforePair.Right.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Right.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Right.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Right.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Right.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.Right.AdapterWeight)); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Right.PlantPending); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Right.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Right.Releases.Stretch); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Right.Releases.Gate); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Right.Releases.Lift); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Right.Releases.Plant); trace.Append(',');
                trace.Append(t.Goal.BeforePair.Right.Releases.Offset); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.PelvisOffset)); trace.Append(',');
                trace.Append(DR(t.Goal.BeforePair.CommonDelta)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Left.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Left.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Left.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Left.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Left.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Left.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Left.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Left.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Left.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Left.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Left.AdapterWeight)); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Left.PlantPending); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Left.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Left.Releases.Stretch); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Left.Releases.Gate); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Left.Releases.Lift); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Left.Releases.Plant); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Left.Releases.Offset); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Right.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Right.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Right.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Right.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Right.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.AfterPair.Right.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Right.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Right.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Right.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Right.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.Right.AdapterWeight)); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Right.PlantPending); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Right.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Right.Releases.Stretch); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Right.Releases.Gate); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Right.Releases.Lift); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Right.Releases.Plant); trace.Append(',');
                trace.Append(t.Goal.AfterPair.Right.Releases.Offset); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.PelvisOffset)); trace.Append(',');
                trace.Append(DR(t.Goal.AfterPair.CommonDelta)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Left.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Left.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Left.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Left.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Left.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Left.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Left.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Left.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Left.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Left.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Left.AdapterWeight)); trace.Append(',');
                trace.Append(t.Goal.ExitState.Left.PlantPending); trace.Append(',');
                trace.Append(t.Goal.ExitState.Left.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Goal.ExitState.Left.Releases.Stretch); trace.Append(',');
                trace.Append(t.Goal.ExitState.Left.Releases.Gate); trace.Append(',');
                trace.Append(t.Goal.ExitState.Left.Releases.Lift); trace.Append(',');
                trace.Append(t.Goal.ExitState.Left.Releases.Plant); trace.Append(',');
                trace.Append(t.Goal.ExitState.Left.Releases.Offset); trace.Append(',');
                trace.Append((t.Goal.ExitState.Right.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Right.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Right.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Right.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Right.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Goal.ExitState.Right.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Right.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Right.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Right.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Right.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.Right.AdapterWeight)); trace.Append(',');
                trace.Append(t.Goal.ExitState.Right.PlantPending); trace.Append(',');
                trace.Append(t.Goal.ExitState.Right.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Goal.ExitState.Right.Releases.Stretch); trace.Append(',');
                trace.Append(t.Goal.ExitState.Right.Releases.Gate); trace.Append(',');
                trace.Append(t.Goal.ExitState.Right.Releases.Lift); trace.Append(',');
                trace.Append(t.Goal.ExitState.Right.Releases.Plant); trace.Append(',');
                trace.Append(t.Goal.ExitState.Right.Releases.Offset); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.PelvisOffset)); trace.Append(',');
                trace.Append(DR(t.Goal.ExitState.CommonDelta)); trace.Append(',');
                trace.Append((t.LeftAvailable ? 1 : 0)); trace.Append(',');
                trace.Append(t.Left.Stamp.Frame); trace.Append(',');
                trace.Append(t.Left.Stamp.LayerIndex); trace.Append(',');
                trace.Append(t.Left.Stamp.Sequence); trace.Append(',');
                trace.Append(t.Left.Foot); trace.Append(',');
                trace.Append((t.Left.OffsetEvaluated ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Left.AnimatedInput.x)); trace.Append(',');
                trace.Append(DR(t.Left.AnimatedInput.y)); trace.Append(',');
                trace.Append(DR(t.Left.AnimatedInput.z)); trace.Append(',');
                trace.Append(DR(t.Left.TargetInput.x)); trace.Append(',');
                trace.Append(DR(t.Left.TargetInput.y)); trace.Append(',');
                trace.Append(DR(t.Left.TargetInput.z)); trace.Append(',');
                trace.Append(DR(t.Left.OffsetXZ.x)); trace.Append(',');
                trace.Append(DR(t.Left.OffsetXZ.y)); trace.Append(',');
                trace.Append(DR(t.Left.OffsetXZ.z)); trace.Append(',');
                trace.Append(DR(t.Left.OffSquared)); trace.Append(',');
                trace.Append(DR(t.Left.ReturnedTarget.x)); trace.Append(',');
                trace.Append(DR(t.Left.ReturnedTarget.y)); trace.Append(',');
                trace.Append(DR(t.Left.ReturnedTarget.z)); trace.Append(',');
                trace.Append((int)t.Left.Branches); trace.Append(',');
                trace.Append((t.Left.Before.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.Before.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.Before.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.Before.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.Before.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.Before.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Left.Before.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Left.Before.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Left.Before.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Left.Before.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Left.Before.AdapterWeight)); trace.Append(',');
                trace.Append(t.Left.Before.PlantPending); trace.Append(',');
                trace.Append(t.Left.Before.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Left.Before.Releases.Stretch); trace.Append(',');
                trace.Append(t.Left.Before.Releases.Gate); trace.Append(',');
                trace.Append(t.Left.Before.Releases.Lift); trace.Append(',');
                trace.Append(t.Left.Before.Releases.Plant); trace.Append(',');
                trace.Append(t.Left.Before.Releases.Offset); trace.Append(',');
                trace.Append((t.Left.After.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.After.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.After.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.After.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.After.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Left.After.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Left.After.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Left.After.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Left.After.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Left.After.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Left.After.AdapterWeight)); trace.Append(',');
                trace.Append(t.Left.After.PlantPending); trace.Append(',');
                trace.Append(t.Left.After.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Left.After.Releases.Stretch); trace.Append(',');
                trace.Append(t.Left.After.Releases.Gate); trace.Append(',');
                trace.Append(t.Left.After.Releases.Lift); trace.Append(',');
                trace.Append(t.Left.After.Releases.Plant); trace.Append(',');
                trace.Append(t.Left.After.Releases.Offset); trace.Append(',');
                trace.Append((t.RightAvailable ? 1 : 0)); trace.Append(',');
                trace.Append(t.Right.Stamp.Frame); trace.Append(',');
                trace.Append(t.Right.Stamp.LayerIndex); trace.Append(',');
                trace.Append(t.Right.Stamp.Sequence); trace.Append(',');
                trace.Append(t.Right.Foot); trace.Append(',');
                trace.Append((t.Right.OffsetEvaluated ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Right.AnimatedInput.x)); trace.Append(',');
                trace.Append(DR(t.Right.AnimatedInput.y)); trace.Append(',');
                trace.Append(DR(t.Right.AnimatedInput.z)); trace.Append(',');
                trace.Append(DR(t.Right.TargetInput.x)); trace.Append(',');
                trace.Append(DR(t.Right.TargetInput.y)); trace.Append(',');
                trace.Append(DR(t.Right.TargetInput.z)); trace.Append(',');
                trace.Append(DR(t.Right.OffsetXZ.x)); trace.Append(',');
                trace.Append(DR(t.Right.OffsetXZ.y)); trace.Append(',');
                trace.Append(DR(t.Right.OffsetXZ.z)); trace.Append(',');
                trace.Append(DR(t.Right.OffSquared)); trace.Append(',');
                trace.Append(DR(t.Right.ReturnedTarget.x)); trace.Append(',');
                trace.Append(DR(t.Right.ReturnedTarget.y)); trace.Append(',');
                trace.Append(DR(t.Right.ReturnedTarget.z)); trace.Append(',');
                trace.Append((int)t.Right.Branches); trace.Append(',');
                trace.Append((t.Right.Before.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.Before.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.Before.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.Before.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.Before.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.Before.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Right.Before.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Right.Before.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Right.Before.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Right.Before.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Right.Before.AdapterWeight)); trace.Append(',');
                trace.Append(t.Right.Before.PlantPending); trace.Append(',');
                trace.Append(t.Right.Before.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Right.Before.Releases.Stretch); trace.Append(',');
                trace.Append(t.Right.Before.Releases.Gate); trace.Append(',');
                trace.Append(t.Right.Before.Releases.Lift); trace.Append(',');
                trace.Append(t.Right.Before.Releases.Plant); trace.Append(',');
                trace.Append(t.Right.Before.Releases.Offset); trace.Append(',');
                trace.Append((t.Right.After.Locked ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.After.ReleasePending ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.After.GateOpenCached ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.After.GateEvaluatedThisPass ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.After.Served ? 1 : 0)); trace.Append(',');
                trace.Append((t.Right.After.SupportsZombieData ? 1 : 0)); trace.Append(',');
                trace.Append(DR(t.Right.After.LockPosition.x)); trace.Append(',');
                trace.Append(DR(t.Right.After.LockPosition.y)); trace.Append(',');
                trace.Append(DR(t.Right.After.LockPosition.z)); trace.Append(',');
                trace.Append(DR(t.Right.After.LockWeight)); trace.Append(',');
                trace.Append(DR(t.Right.After.AdapterWeight)); trace.Append(',');
                trace.Append(t.Right.After.PlantPending); trace.Append(',');
                trace.Append(t.Right.After.ZombiePlantPending); trace.Append(',');
                trace.Append(t.Right.After.Releases.Stretch); trace.Append(',');
                trace.Append(t.Right.After.Releases.Gate); trace.Append(',');
                trace.Append(t.Right.After.Releases.Lift); trace.Append(',');
                trace.Append(t.Right.After.Releases.Plant); trace.Append(',');
                trace.Append(t.Right.After.Releases.Offset); trace.Append(',');
                trace.Append(DR(t.RootWorld.x)); trace.Append(',');
                trace.Append(DR(t.RootWorld.y)); trace.Append(',');
                trace.Append(DR(t.RootWorld.z)); trace.Append(',');
                trace.Append(DR(t.RootWorld.w)); trace.Append(',');
                trace.Append(DR(t.LeftRoot.x)); trace.Append(',');
                trace.Append(DR(t.LeftRoot.y)); trace.Append(',');
                trace.Append(DR(t.LeftRoot.z)); trace.Append(',');
                trace.Append(DR(t.LeftRoot.w)); trace.Append(',');
                trace.Append(DR(t.RightRoot.x)); trace.Append(',');
                trace.Append(DR(t.RightRoot.y)); trace.Append(',');
                trace.Append(DR(t.RightRoot.z)); trace.Append(',');
                trace.Append(DR(t.RightRoot.w)); trace.Append('\n');
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "loko-ik-trace.csv"), trace.ToString());
            var capacity = new IkTraceCapacityMetadata { capacity = LokomotionKaydedici.MaxIkTraceSamples,
                observed = _kayit.IkTraceObserved, stored = _kayit.IkTraceSamples.Count,
                written = traceWritten, dropped = _kayit.IkTraceDropped,
                scope = "observed/stored/dropped cover all diagnostic samples; written covers selected CSV segments" };
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "loko-ik-trace-capacity.json"), JsonUtility.ToJson(capacity, true));
        }
        [Serializable] private sealed class IkTraceCapacityMetadata
        {
            public int capacity, observed, stored, written, dropped;
            public string scope;
        }

        // Compact layer JSON and asset/layer names are CSV-escaped; old simple labels retain their spelling.
        private static string Csv(string value) => string.IsNullOrEmpty(value) ? "" :
            value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0
                ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
        private static string DR(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static void AppendQ(StringBuilder sb, Quaternion q) =>
            sb.Append(DR(q.x)).Append(',').Append(DR(q.y)).Append(',').Append(DR(q.z)).Append(',').Append(DR(q.w));
        private static void AppendWorst(StringBuilder sb, int bone, float degrees, Quaternion previous, Quaternion current)
        {
            sb.Append(',').Append(Csv(bone >= 0 ? Kemikler[bone].ToString() : "")).Append(',').Append(DR(degrees)).Append(',');
            AppendQ(sb, previous); sb.Append(','); AppendQ(sb, current);
        }

        // --------------------------------------------------------------------------------------------------- the "önce / sonra" table

        /// <summary>K/wp-082/evidence/olcum.md rows (PLAN §5.2): one log line per row, markdown, prefixed "[DD-TEST] wp082.olcum|".</summary>
        private void Tablo(CharacterAnimationSet anim, List<Parca> parcalar, Istat[] ist, int iIkOffRun, int iIkOffWalk, int iRate, int iSalinim)
        {
            Debug.Log("[DD-TEST] wp082.olcum| satır | v (m/s) | komut v | kadans (adım/dk) | adım (m) | L1 avg | L1 p95 | temas payı beklenen L/R | ölçülen L/R | LocoRate | kilit kabul / bırakma S·G·L·P | kilitli kare | n |");
            for (int i = 0; i < _gecisBasi; i++)
            {
                Parca p = parcalar[i];
                Istat s = ist[i];
                double cmd = p.KomutHizN > 0 ? p.KomutHizToplam / p.KomutHizN : double.NaN;
                string exp = "—";
                if (p.Tur == Tur.Kararli || p.Tur == Tur.Kontrol)
                    exp = F2(s.BekL) + " / " + F2(s.BekR);
                Debug.Log("[DD-TEST] wp082.olcum| " + p.Ad + " | " + F2(s.VYon) + " | " + F2(cmd) + " | " + F2(120.0 / s.T) + " | " + F3(s.VYon * s.T / 2.0) + " | " + F3(s.L1Avg) + " | " + F3(s.L1P95) +
                          " | " + exp + " | " + F2(s.PayL) + " / " + F2(s.PayR) + " | " + F3(s.Rate) + " | " + s.Kabul + " / " + s.BirakS + "·" + s.BirakG + "·" + s.BirakL + "·" + s.BirakP + " | " +
                          F2(s.N > 0 ? (double)s.KilitliKare / s.N : double.NaN) + " | " + s.N + " |");
            }

            foreach (Tur t in new[] { Tur.Kalkis, Tur.Durus, Tur.Pivot180, Tur.Kesme90 })
            {
                var l = new List<double>();
                double rate = 0, v = 0, pl = 0, pr = 0;
                int n = 0, kab = 0, bs = 0, bg = 0, bl = 0, bp = 0, kil = 0, seg = 0;
                for (int i = _gecisBasi; i < parcalar.Count; i++)
                {
                    if (parcalar[i].Tur != t) continue;
                    Istat s = ist[i];
                    seg++;
                    l.AddRange(s.Kayma);
                    if (s.N > 0) { rate += s.Rate * s.N; v += s.V * s.N; pl += s.PayL * s.N; pr += s.PayR * s.N; n += s.N; kil += s.KilitliKare; }
                    kab += s.Kabul; bs += s.BirakS; bg += s.BirakG; bl += s.BirakL; bp += s.BirakP;
                }

                Debug.Log("[DD-TEST] wp082.olcum| " + t + " ×" + seg + " (0,3 s) | " + F2(n > 0 ? v / n : double.NaN) + " | — | — | — | " + F3(Ort(l)) + " | " + F3(P95(l)) + " | — | " + F2(n > 0 ? pl / n : double.NaN) + " / " +
                          F2(n > 0 ? pr / n : double.NaN) + " | " + F3(n > 0 ? rate / n : double.NaN) + " | " + kab + " / " + bs + "·" + bg + "·" + bl + "·" + bp + " | " + F2(n > 0 ? (double)kil / n : double.NaN) + " | " + n + " |");
            }
        }

        // --------------------------------------------------------------------------------------------------- GC (PLAN §5.2 (4))

        /// <summary>
        /// GC per frame on the top-down sprint path: 600 frames of full-tilt running with a direction change every 40 frames (8 directions,
        /// aim held), "GC Allocated In Frame" read every frame through ProfilerRecorder (Profiler on, frame rate uncapped — with the cap the
        /// frame counters read 0, VfxHotPathBisectTests). Positive control in the same run: 30 frames each allocating a 1 KB array must read
        /// ≥ 1024 B. GC.GetAllocatedBytesForCurrentThread is not used (always 0 on Unity Mono). Reported, not gated (the "önce" number).
        /// </summary>
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(300000)]
        [Order(2)]   // last: its empty-scene windows switch the Run scene's and the game's DontDestroyOnLoad roots off (restored, then the scene reloads)
        public IEnumerator GC_TdSprint_YonCevirme_600Kare()
        {
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            Time.captureDeltaTime = 1f / Fps;
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "TopDown isteği uygulanmadı");
            Vector3 merkez = ZeminKur();
            yield return null;
            TemelYonler();
            _s.Rig.Teleport(merkez + Vector3.up * 0.05f, Quaternion.LookRotation(_nisan, Vector3.up));
            _profilerOnce = UnityEngine.Profiling.Profiler.enabled;
            _fpsOnce = Application.targetFrameRate;
            UnityEngine.Profiling.Profiler.enabled = true;
            Application.targetFrameRate = -1;
            int[] sira = { 0, 4, 2, 6, 1, 5, 3, 7 };
            for (int f = 0; f < 120 && Canli(); f++) { Nisanla(); _s.Girdi.Yon = Yon8(sira[(f / GcYonKare) & 7], 1f); yield return null; }   // warm-up (not measured)

            var oyun = new long[GcKare];
            var adet = new long[GcKare];
            var kontrol = new long[GcKontrolKare];
            var bosSahne = new long[GcBosKare];
            var bosTaban = new long[GcBosKare];
            int okunan = 0, kontrolOkunan = 0, bosSahneN = 0, bosTabanN = 0;
            bool gecerli;
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                gecerli = bytes.Valid && count.Valid;
                yield return null;
                yield return null;
                for (int f = 0; f < GcKare && Canli(); f++)
                {
                    Nisanla();
                    _s.Girdi.Yon = Yon8(sira[(f / GcYonKare) & 7], 1f);
                    yield return null;
                    oyun[okunan] = bytes.LastValue;   // the frame the input above ran in
                    adet[okunan] = count.LastValue;
                    okunan++;
                    if (Vector3.Distance(Yatay(_s.Rig.transform.position), Yatay(merkez)) > MerkezYaricap) break;
                }

                _s.Girdi.Yon = Vector2.zero;
                for (int f = 0; f < 20; f++) yield return null;
                for (int f = 0; f < GcKontrolKare; f++)
                {
                    s_gcYem = new byte[GcKontrolBayt];
                    yield return null;
                    kontrol[kontrolOkunan++] = bytes.LastValue;
                }

                // S-06 08.10: the source split, same run — B the Run scene's roots off (no player, camera, world; the game's
                // DontDestroyOnLoad systems and the Editor / test framework on), C also the game's DontDestroyOnLoad roots off
                // (only the test framework's roots and the Editor). Restored before the asserts.
                var sahneKapali = new List<GameObject>();
                var ddolKapali = new List<GameObject>();
                var ddolKalan = new List<string>();
                try
                {
                    foreach (GameObject g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                        if (g != null && g.activeSelf) { g.SetActive(false); sahneKapali.Add(g); }
                    for (int f = 0; f < GcBosOturma; f++) yield return null;
                    for (int f = 0; f < GcBosKare; f++) { yield return null; bosSahne[bosSahneN++] = bytes.LastValue; }

                    var probe = new GameObject("wp082-gc-ddol-probe");
                    Object.DontDestroyOnLoad(probe);
                    foreach (GameObject g in probe.scene.GetRootGameObjects())
                    {
                        if (g == null || g == probe || !g.activeSelf) continue;
                        if (TestCercevesi(g)) { ddolKalan.Add(g.name); continue; }
                        g.SetActive(false);
                        ddolKapali.Add(g);
                    }

                    Object.Destroy(probe);
                    for (int f = 0; f < GcBosOturma; f++) yield return null;
                    for (int f = 0; f < GcBosKare; f++) { yield return null; bosTaban[bosTabanN++] = bytes.LastValue; }
                }
                finally
                {
                    for (int i = ddolKapali.Count - 1; i >= 0; i--) if (ddolKapali[i] != null) ddolKapali[i].SetActive(true);
                    for (int i = sahneKapali.Count - 1; i >= 0; i--) if (sahneKapali[i] != null) sahneKapali[i].SetActive(true);
                }

                var ad = new List<string>();
                foreach (GameObject g in ddolKapali) if (g != null && ad.Count < 24) ad.Add(g.name);
                Debug.Log("[DD-TEST] wp082.gcSplit sceneRootsOff=" + sahneKapali.Count + " ddolGameRootsOff=" + ddolKapali.Count + " [" + string.Join(", ", ad) + "] ddolKept(test framework)=[" + string.Join(", ", ddolKalan) + "]" +
                          " :: A game sprint mean " + F2(Ort(oyun, okunan)) + " median " + Med(oyun, okunan) + " · B scene off mean " + F2(Ort(bosSahne, bosSahneN)) + " median " + Med(bosSahne, bosSahneN) +
                          " · C scene+game DDOL off (Editor/test only) mean " + F2(Ort(bosTaban, bosTabanN)) + " median " + Med(bosTaban, bosTabanN) +
                          " :: game share A−C mean " + F2(Ort(oyun, okunan) - Ort(bosTaban, bosTabanN)) + " (scene A−B " + F2(Ort(oyun, okunan) - Ort(bosSahne, bosSahneN)) + ", game DDOL B−C " + F2(Ort(bosSahne, bosSahneN) - Ort(bosTaban, bosTabanN)) + ") B/frame");
            }

            long oyunMax = 0, oyunSum = 0, kontrolMin = long.MaxValue, adetSum = 0;
            int sifirOlmayan = 0;
            for (int i = 0; i < okunan; i++) { oyunMax = Math.Max(oyunMax, oyun[i]); oyunSum += oyun[i]; adetSum += adet[i]; if (oyun[i] > 0) sifirOlmayan++; }
            for (int i = 0; i < kontrolOkunan; i++) kontrolMin = Math.Min(kontrolMin, kontrol[i]);
            var ornek = new StringBuilder();
            for (int i = 0; i < Math.Min(okunan, 40); i++) ornek.Append(oyun[i]).Append(i < 39 ? "," : "");
            bool kontrolOk = gecerli && kontrolOkunan == GcKontrolKare && kontrolMin >= GcKontrolBayt;
            Debug.Log("[DD-TEST] wp082.gc recorderValid=" + gecerli + " frames=" + okunan + " bytes/frame mean=" + F2(okunan > 0 ? (double)oyunSum / okunan : double.NaN) + " max=" + oyunMax +
                      " nonZeroFrames=" + sifirOlmayan + " allocs/frame mean=" + F2(okunan > 0 ? (double)adetSum / okunan : double.NaN) + " :: control " + GcKontrolBayt + " B × " + kontrolOkunan +
                      " frames min=" + (kontrolOkunan > 0 ? kontrolMin : -1) + " pass=" + kontrolOk + " aborted=" + (_iptal ?? "no") + " :: first frames " + ornek);
            GC.KeepAlive(s_gcYem);
            Assert.IsTrue(gecerli, "ProfilerRecorder 'GC Allocated In Frame' geçersiz — GC ölçülemedi");
            Assert.IsTrue(kontrolOk, "GC pozitif kontrolü: " + GcKontrolBayt + " B'lık bilinçli ayırma kayıtta yok (en az " + kontrolMin + " B) — ölçüm geçersiz");
            Assert.AreEqual(GcKare, okunan, "TD sprint " + GcKare + " karenin hepsi kaydedilmedi (" + (_iptal ?? "merkezden uzaklaştı") + ")");
            Assert.AreEqual(GcBosKare, bosSahneN, "boş sahne penceresi eksik");
            Assert.AreEqual(GcBosKare, bosTabanN, "Editor/test taban penceresi eksik");
        }

        private static byte[] s_gcYem;

        /// <summary>A DontDestroyOnLoad root that belongs to the test framework (kept on in the Editor / test baseline window).</summary>
        private static bool TestCercevesi(GameObject g)
        {
            if (g.name.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (Component c in g.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string n = c.GetType().FullName ?? "";
                if (n.StartsWith("UnityEngine.TestTools", StringComparison.Ordinal) || n.StartsWith("UnityEditor", StringComparison.Ordinal) || n.StartsWith("NUnit", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static double Ort(long[] a, int n)
        {
            if (n <= 0) return double.NaN;
            double t = 0;
            for (int i = 0; i < n; i++) t += a[i];
            return t / n;
        }

        private static long Med(long[] a, int n)
        {
            if (n <= 0) return -1;
            var c = new long[n];
            Array.Copy(a, c, n);
            Array.Sort(c);
            return c[n / 2];
        }
    }

    /// <summary>
    /// WP-082 R3 positive control (b): forces the Animator's LocoRate to <see cref="Carpan"/> × what PlayerSkinnedVisual wrote this frame
    /// (order 31000: after its Update, before the Animator evaluates).
    /// </summary>
    [DefaultExecutionOrder(31000)]
    internal sealed class LocoRateCarpani : MonoBehaviour
    {
        private static readonly int RateId = Animator.StringToHash(C.LocoRate);
        public float Carpan = 1f;
        private Animator _anim;

        public void Bagla(Animator anim) => _anim = anim;

        private void Update()
        {
            if (_anim == null || Mathf.Approximately(Carpan, 1f)) return;
            _anim.SetFloat(RateId, _anim.GetFloat(RateId) * Carpan);
        }
    }

    /// <summary>
    /// The R2 locomotion sampler: one record per frame AFTER the animator, OnAnimatorIK and every LateUpdate solver of the player
    /// (order 32000 > WeaponHoldIk 890 · WeaponGripIk 910 · TwistCorrector 920 · MagazineContactIk 945) — the pose the frame shows —
    /// plus the animated (pre-foot-IK) sole height FootIkAdapter read for this frame.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class LokomotionKaydedici : MonoBehaviour
    {
        internal struct Kare
        {
            public int Etiket, Since, Frame;
            public float Zaman, Dt;
            public bool Gecerli;                  // a previous frame, no teleport, dt > 0
            public Vector3 BodyV;                 // horizontal, m/s
            public Vector3 FootVL, FootVR;        // shown pose, m/s
            public Vector3 PosL, PosR;            // foot bone, world
            public float GapL, GapR;              // shown sole above the ground ray, m (NaN = no ground)
            public float AnimGapL, AnimGapR;      // animated sole (before the foot IK), m
            public float AnimVyL, AnimVyR;        // its vertical speed, m/s
            // Diagnostic only. Acceptance AnimGap/AnimVy and phase classifiers continue using their existing source.
            public int UnityFrame, RawGoalFrame;
            public bool RawGoalL, RawGoalR, RawGoalVelocityL, RawGoalVelocityR;
            public Vector3 RawGoalPosL, RawGoalPosR, RawGoalVL, RawGoalVR, RawGoalMinusShownBoneL, RawGoalMinusShownBoneR;
            // Diagnostic-only final-pose / Animator data; never used by acceptance calculations.
            public bool DiagCaptured, BaseTransition, V4DeltaValid, V4AimDriven;
            public int BaseCurrentHash, BaseNextHash, BaseTransitionHash, V4Kind, V4Id, V4ClipParam, WorstLimbBone, WorstTorsoBone;
            public float BaseCurrentNorm, BaseNextNorm, BaseTransProgress, V4T, V4DeltaT, V4EntryT, V4TimeParam, V4NaturalDeltaT, V4StopTD;
            public float LocoSpeed, MoveX, MoveY, WorstLimbDeg, WorstTorsoDeg;
            public Quaternion RootRotation, WorstLimbPrev, WorstLimbNow, WorstTorsoPrev, WorstTorsoNow;
            public string LayersJson;
            public bool IkServed;
            public int IkForce;                   // actual static override in this sampled frame (control b)
            public bool IkAdapterServed;          // raw Served, including a solve before its blend weight reaches 0.001
            public float IkWeight;               // remaining IK blend weight; Force=0 alone does not prove the pose is IK-free
            public float OwnerRate;              // PlayerSkinnedVisual's unmultiplied rate in this same frame
            public float Rate;                    // the Animator's LocoRate this frame
            public float Honest;                  // PlayerSkinnedVisual.HonestSpeed this frame (m/s)
            public byte Locked;                   // FootIkAdapter.LockedFeet
            public int Acc, RelS, RelG, RelL, RelP;   // FootIkAdapter counters (cumulative)
            public bool RayDigerL, RayDigerR;     // the gap ray hit a collider other than the test floor
            public bool TemasL, TemasR;           // contact (animated pose)
            public bool TemasEskiL, TemasEskiR;   // the R2 classifier (shown pose) — the cause record
            public bool TemasPozL, TemasPozR;     // the pose classifier (animated sole within 3 cm of its segment floor, vertical speed ≤ 0.15)
            public float FazL, FazR;              // phase stance weight 0..1 (the base layer's clips × their own foot_x → foot_x_off windows)
            public float BekL, BekR;              // (a1) expected stance share this frame: Σ clip weight × that clip's window share (S-06 08.10)
            public float Pelvis;                  // FootIkAdapter.PelvisOffset (m, < 0 = lowered)
            public bool OffL, OffR;               // foot_x_off events this frame
            public bool OlayL, OlayR;             // plant events (collapsed)
            public bool BaslangicL, BaslangicR;   // contact onsets
        }

        private static readonly int RateId = Animator.StringToHash(C.LocoRate);
        public readonly List<Kare> Kareler = new List<Kare>(16000);
        public readonly List<float> Derece = new List<float>(16000 * 18);   // per frame × bone, °/frame at 60 fps (NaN = n/a)
        public int Etiket = -1;
        public int Since { get; private set; }
        public int OlaySayisi { get; private set; }
        public int RayDigerSayisi { get; private set; }
        public float TabanL { get; private set; }
        public float TabanR { get; private set; }
        public string RayDigerAd { get; private set; }
        public int FazKlipSayisi => _faz.Count;

        /// <summary>A clip's stance windows on its own loop (frames at 30 fps of its period): plant → off per foot; null = no foot events (both feet down).</summary>
        private sealed class FazKlip { public float P; public int PL, OL, PR, OR; public bool Ayakta; }
        private readonly Dictionary<AnimationClip, FazKlip> _faz = new Dictionary<AnimationClip, FazKlip>();
        private readonly List<AnimatorClipInfo> _ci = new List<AnimatorClipInfo>(16);
        private readonly List<AnimatorClipInfo> _diagClips = new List<AnimatorClipInfo>(16);
        private bool _diagEnabled;
        public struct IkTraceSample
        {
            public int Label, Since, RecorderFrame, UnityFrame, AdapterInstanceId;
            public float Dt;
            public bool GoalAvailable, LeftAvailable, RightAvailable, SamePassBothFeet;
            public FootIkAdapter.GoalPassTraceSnapshot Goal;
            public FootIkAdapter.LockTraceSnapshot Left, Right;
            public Quaternion RootWorld, LeftRoot, RightRoot;
        }
        public const int MaxIkTraceSamples = 16000;
        public List<IkTraceSample> IkTraceSamples { get; private set; }
        public int IkTraceObserved { get; private set; }
        public int IkTraceDropped { get; private set; }
        private bool _traceOptInApplied, _previousGoalTrace, _previousLockTrace;
        private int _traceAdapterInstanceId;
        [Serializable] private sealed class DiagClip { public string name; public int instanceId; public float weight; }
        [Serializable] private sealed class DiagLayer
        {
            public int index, currentHash, nextHash;
            public string name;
            public float weight;
            public DiagClip[] currentClips, nextClips;
        }
        [Serializable] private sealed class DiagLayers { public DiagLayer[] layers; }

        private Animator _anim;
        private Transform _govde;
        private FootIkAdapter _ik;
        private PlayerSkinnedVisual _pv;
        public void Govde(PlayerSkinnedVisual pv) => _pv = pv;
        private Collider _zemin;
        private Transform _footL, _footR;
        private Transform[] _bones;
        private Quaternion[] _prevRot;
        private bool _hasPrev;
        private Vector3 _prevRoot, _prevL, _prevR;
        private float _prevAnimL, _prevAnimR;
        private Vector3 _prevRawGoalL, _prevRawGoalR;
        private int _prevRawGoalFrame = -1;
        private bool _prevRawGoalValidL, _prevRawGoalValidR;
        private bool _prevTemasL, _prevTemasR;
        private int _lastEvL = -1000, _lastEvR = -1000, _lastOnL = -1000, _lastOnR = -1000;
        private int _pending;
        private int _etiketOnce = int.MinValue;
        private int _frame;
        private int _evL, _evR, _onL, _onR;   // counted in the current label after the warm-up
        private bool _bagli;

        public void Bagla(Animator anim, Transform govde, FootIkAdapter ik, Collider zemin, CharacterAnimationSet set)
        {
            if (set != null)
            {
                foreach (CharacterAnimationSet.Entry e in set.Clips)
                {
                    if (e == null || e.clip == null || _faz.ContainsKey(e.clip)) continue;
                    _faz[e.clip] = FazOku(e);
                }
            }

            _diagEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DD_LOKO_DUMP_DIR"));
            _anim = anim;
            _govde = govde;
            _ik = ik;
            if (_diagEnabled && _ik != null)
            {
                IkTraceSamples = new List<IkTraceSample>(MaxIkTraceSamples);
                IkTraceObserved = IkTraceDropped = 0;
                _previousGoalTrace = _ik.TraceFootIkGoals;
                _previousLockTrace = _ik.TraceZombieLock;
                _ik.TraceFootIkGoals = _ik.TraceZombieLock = true;
                _traceOptInApplied = true;
                _traceAdapterInstanceId = _ik.GetInstanceID();
            }
            _zemin = zemin;
            _footL = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            _footR = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            _bones = new Transform[R2LokomotionOlcumTests.Kemikler.Length];
            _prevRot = new Quaternion[_bones.Length];
            for (int i = 0; i < _bones.Length; i++) _bones[i] = anim.GetBoneTransform(R2LokomotionOlcumTests.Kemikler[i]);
            CharacterAnimationEvents.Any += OnEvent;
            _bagli = true;
        }

        public void Birak()
        {
            if (_bagli) CharacterAnimationEvents.Any -= OnEvent;
            if (_traceOptInApplied)
            {
                if (_ik != null) { _ik.TraceFootIkGoals = _previousGoalTrace; _ik.TraceZombieLock = _previousLockTrace; }
                _traceOptInApplied = false;
            }
            _bagli = false;
            enabled = false;
        }

        private void OnDestroy() => Birak();

        public bool KemikVar(int b) => _bones != null && b < _bones.Length && _bones[b] != null;

        public string BulunanKemikler()
        {
            if (_bones == null) return "none";
            var missing = new List<string>();
            for (int i = 0; i < _bones.Length; i++) if (_bones[i] == null) missing.Add(R2LokomotionOlcumTests.Kemikler[i].ToString());
            return (_bones.Length - missing.Count) + "/" + _bones.Length + (missing.Count > 0 ? " missing " + string.Join(",", missing) : "") +
                   " feet " + (_footL != null && _footR != null);
        }

        /// <summary>Both feet have ≥ n plant events (or n contact onsets) in the current label after the warm-up.</summary>
        public bool DonguYeter(int n) => (_evL >= n && _evR >= n) || (_onL >= n && _onR >= n);

        private void OnEvent(CharacterAnimationEvents c, CharacterAnimationEvent e)
        {
            if (c == null || _govde == null || !c.transform.IsChildOf(_govde) && !_govde.IsChildOf(c.transform)) return;
            if (e == CharacterAnimationEvent.FootLeft) _pending |= 1;
            else if (e == CharacterAnimationEvent.FootRight) _pending |= 2;
            else if (e == CharacterAnimationEvent.FootLOff) _pending |= 4;
            else if (e == CharacterAnimationEvent.FootROff) _pending |= 8;
        }

        private static FazKlip FazOku(CharacterAnimationSet.Entry e)
        {
            var f = new FazKlip { P = Mathf.Max(1f, Mathf.Round(e.seconds * 30f)), PL = -1, OL = -1, PR = -1, OR = -1 };
            if (!string.IsNullOrEmpty(e.events))
            {
                foreach (string part in e.events.Split(';'))
                {
                    string[] kv = part.Split('=');
                    if (kv.Length != 2 || !int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fr)) continue;
                    switch (kv[0].Trim())
                    {
                        case "foot_l": f.PL = fr; break;
                        case "foot_r": f.PR = fr; break;
                        case "foot_l_off": f.OL = fr; break;
                        case "foot_r_off": f.OR = fr; break;
                    }
                }
            }

            // no plant events: a stand / idle (both feet down); a plant without its off: the manifest stance frames
            f.Ayakta = f.PL < 0 || f.PR < 0;
            if (!f.Ayakta && f.OL < 0) f.OL = f.PL + Math.Max(1, e.stanceFrames);
            if (!f.Ayakta && f.OR < 0) f.OR = f.PR + Math.Max(1, e.stanceFrames);
            return f;
        }

        private static bool Pencerede(float fr, int a, int b, float p)
        {
            float len = ((b - a) % p + p) % p;
            float d = ((fr - a) % p + p) % p;
            return d < len;
        }

        /// <summary>Σ weight × stance over the clips of one state at its normalized time; clips without a table count as both feet down.</summary>
        private void FazTopla(float phase, float scale, ref float l, ref float r, ref float w, ref float el, ref float er)
        {
            for (int i = 0; i < _ci.Count; i++)
            {
                AnimatorClipInfo c = _ci[i];
                float cw = c.weight * scale;
                if (cw <= 0f) continue;
                w += cw;
                if (c.clip == null || !_faz.TryGetValue(c.clip, out FazKlip f) || f.Ayakta) { l += cw; r += cw; el += cw; er += cw; continue; }
                el += cw * (((f.OL - f.PL) % f.P + f.P) % f.P) / f.P;
                er += cw * (((f.OR - f.PR) % f.P + f.P) % f.P) / f.P;
                float fr = phase * f.P;
                if (Pencerede(fr, f.PL, f.OL, f.P)) l += cw;
                if (Pencerede(fr, f.PR, f.OR, f.P)) r += cw;
            }
        }

        private void FazOlc(out float l, out float r, out float el, out float er)
        {
            l = r = el = er = 0f;
            float w = 0f;
            AnimatorStateInfo cur = _anim.GetCurrentAnimatorStateInfo(0);
            bool tr = _anim.IsInTransition(0);
            float t = tr ? Mathf.Clamp01(_anim.GetAnimatorTransitionInfo(0).normalizedTime) : 0f;
            _anim.GetCurrentAnimatorClipInfo(0, _ci);
            FazTopla(cur.normalizedTime - Mathf.Floor(cur.normalizedTime), 1f - t, ref l, ref r, ref w, ref el, ref er);
            if (tr)
            {
                AnimatorStateInfo next = _anim.GetNextAnimatorStateInfo(0);
                _anim.GetNextAnimatorClipInfo(0, _ci);
                FazTopla(next.normalizedTime - Mathf.Floor(next.normalizedTime), t, ref l, ref r, ref w, ref el, ref er);
            }

            if (w > 1e-5f) { l /= w; r /= w; el /= w; er /= w; }
            else { l = r = el = er = 1f; }
        }

        /// <summary>Valid frames of a label with since in [sinceMin, sinceMax].</summary>
        public int PencereKareSayisi(int etiket, int sinceMin, int sinceMax)
        {
            int n = 0;
            foreach (Kare k in Kareler) if (k.Etiket == etiket && k.Gecerli && k.Since >= sinceMin && k.Since <= sinceMax) n++;
            return n;
        }

        /// <summary>The animated sole's floor per foot: its <paramref name="q"/> percentile over labels [0, <paramref name="etiketSon"/>) after the warm-up.</summary>
        public void TabanHesapla(int etiketSon, int sinceMin, float q, out float l, out float r)
        {
            var gl = new List<float>(); var gr = new List<float>();
            foreach (Kare k in Kareler)
            {
                if (k.Etiket < 0 || k.Etiket >= etiketSon || k.Since < sinceMin || !k.Gecerli) continue;
                if (!float.IsNaN(k.AnimGapL)) gl.Add(k.AnimGapL);
                if (!float.IsNaN(k.AnimGapR)) gr.Add(k.AnimGapR);
            }

            l = Yuzde(gl, q);
            r = Yuzde(gr, q);
        }

        private static float Yuzde(List<float> xs, float q)
        {
            if (xs.Count == 0) return 0f;
            xs.Sort();
            return xs[Mathf.Clamp(Mathf.CeilToInt(q * xs.Count) - 1, 0, xs.Count - 1)];
        }

        /// <summary>
        /// Contact of every frame: the PHASE classifier (stance weight ≥ 0.5) — the gates' contact; the POSE classifier (animated sole
        /// within 3 cm of its label's floor, the <paramref name="q"/> percentile per foot after <paramref name="sinceMin"/>, and vertical
        /// speed ≤ 0.15 m/s) — reported beside it. Contact onsets are rebuilt from the phase contact with the same 0.2 s spacing.
        /// </summary>
        public void Siniflandir(int sinceMin, float q)
        {
            var tabanlar = new Dictionary<int, float[]>();
            var gl = new Dictionary<int, List<float>[]>();
            foreach (Kare k in Kareler)
            {
                if (!k.Gecerli) continue;
                if (!gl.TryGetValue(k.Etiket, out List<float>[] g)) gl[k.Etiket] = g = new[] { new List<float>(), new List<float>(), new List<float>(), new List<float>() };
                int o = k.Since >= sinceMin ? 0 : 2;   // warm-up frames feed the floor only when the label has no later frame (transitions)
                if (!float.IsNaN(k.AnimGapL)) g[o].Add(k.AnimGapL);
                if (!float.IsNaN(k.AnimGapR)) g[o + 1].Add(k.AnimGapR);
            }

            foreach (KeyValuePair<int, List<float>[]> kv in gl)
            {
                List<float> a = kv.Value[0].Count > 0 ? kv.Value[0] : kv.Value[2], b = kv.Value[1].Count > 0 ? kv.Value[1] : kv.Value[3];
                tabanlar[kv.Key] = new[] { Yuzde(a, q), Yuzde(b, q) };
            }

            int lastOnL = -1000, lastOnR = -1000;
            bool prevL = false, prevR = false;
            for (int i = 0; i < Kareler.Count; i++)
            {
                Kare k = Kareler[i];
                float[] t = tabanlar.TryGetValue(k.Etiket, out float[] tt) ? tt : new[] { 0f, 0f };
                k.TemasPozL = k.Gecerli && Temas(k.AnimGapL - t[0], k.AnimVyL);
                k.TemasPozR = k.Gecerli && Temas(k.AnimGapR - t[1], k.AnimVyR);
                k.TemasL = k.Gecerli && k.FazL >= 0.5f;
                k.TemasR = k.Gecerli && k.FazR >= 0.5f;
                k.BaslangicL = k.TemasL && !prevL && k.Frame - lastOnL >= R2LokomotionOlcumTests.OlayAyrimKare;
                k.BaslangicR = k.TemasR && !prevR && k.Frame - lastOnR >= R2LokomotionOlcumTests.OlayAyrimKare;
                if (k.BaslangicL) lastOnL = k.Frame;
                if (k.BaslangicR) lastOnR = k.Frame;
                prevL = k.TemasL;
                prevR = k.TemasR;
                Kareler[i] = k;
            }
        }

        private static bool Temas(float gap, float vy) =>
            !float.IsNaN(gap) && gap <= R2LokomotionOlcumTests.TemasYukseklik && Mathf.Abs(vy) <= R2LokomotionOlcumTests.TemasDikeyHiz;

        private float Gap(Transform foot, float bottom, out bool diger)
        {
            diger = false;
            Vector3 p = foot.position;
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.5f, PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore))
            {
                diger = _zemin != null && hit.collider != _zemin;
                if (diger && RayDigerAd == null) RayDigerAd = hit.collider.name + " (layer " + LayerMask.LayerToName(hit.collider.gameObject.layer) + ", y " + hit.point.y.ToString("F2", CultureInfo.InvariantCulture) + ")";
                return p.y - bottom - hit.point.y;
            }

            return float.NaN;
        }

        // Existing CSV opt-in only; allocating non-performance diagnostics, sampled in final LateUpdate.
        // No assumption that GetBoneTransform in OnAnimatorIK is this frame's pre-solve pose.
        private void CaptureDiagnostics(ref Kare k, Transform root)
        {
            k.DiagCaptured = true;
            AnimatorStateInfo current = _anim.GetCurrentAnimatorStateInfo(0);
            k.BaseCurrentHash = current.fullPathHash; k.BaseCurrentNorm = current.normalizedTime;
            k.BaseTransition = _anim.IsInTransition(0);
            k.BaseNextNorm = k.BaseTransProgress = float.NaN;
            if (k.BaseTransition) {
                AnimatorStateInfo next = _anim.GetNextAnimatorStateInfo(0);
                AnimatorTransitionInfo transition = _anim.GetAnimatorTransitionInfo(0);
                k.BaseNextHash = next.fullPathHash; k.BaseNextNorm = next.normalizedTime;
                k.BaseTransProgress = transition.normalizedTime; k.BaseTransitionHash = transition.fullPathHash;
            }
            var layers = new DiagLayer[_anim.layerCount];
            for (int layer = 0; layer < layers.Length; layer++) {
                bool transitioning = _anim.IsInTransition(layer);
                layers[layer] = new DiagLayer { index = layer, name = _anim.GetLayerName(layer), weight = _anim.GetLayerWeight(layer),
                    currentHash = _anim.GetCurrentAnimatorStateInfo(layer).fullPathHash,
                    nextHash = transitioning ? _anim.GetNextAnimatorStateInfo(layer).fullPathHash : 0,
                    currentClips = ReadDiagnosticClips(layer, false), nextClips = transitioning ? ReadDiagnosticClips(layer, true) : new DiagClip[0] };
            }
            k.LayersJson = JsonUtility.ToJson(new DiagLayers { layers = layers });
            k.LocoSpeed = _anim.GetFloat(C.LocoSpeed); k.MoveX = _anim.GetFloat(C.MoveX); k.MoveY = _anim.GetFloat(C.MoveY);
            k.V4ClipParam = _anim.GetInteger(LocomotionV4.ClipParam); k.V4TimeParam = _anim.GetFloat(LocomotionV4.TimeParam);
            k.V4Id = LocomotionV4.None; k.V4T = k.V4DeltaT = k.V4EntryT = k.V4NaturalDeltaT = k.V4StopTD = float.NaN;
            if (_pv != null) {
                LocomotionV4Clock clock = _pv.V4Clock;
                k.V4Kind = (int)clock.Current; k.V4Id = clock.Id; k.V4T = clock.T;
                k.V4EntryT = _pv.V4Events.EntryT; k.V4AimDriven = _pv.V4Events.AimDriven;
                if (clock.Current != LocomotionV4Clock.Kind.None && _pv.V4Tables != null && _pv.V4Tables.Has(clock.Id)) {
                    k.V4NaturalDeltaT = k.Dt / Mathf.Max(0.05f, _pv.V4Tables.Seconds(clock.Id));
                    if (clock.Current == LocomotionV4Clock.Kind.Stop)
                        k.V4StopTD = _pv.V4Tables.StopTimeAt(clock.Id, k.LocoSpeed * k.LocoSpeed / (2f * Mathf.Max(0.1f, _pv.V4Deceleration)));
                    if (Kareler.Count > 0) {
                        Kare previous = Kareler[Kareler.Count - 1];
                        k.V4DeltaValid = previous.DiagCaptured && previous.UnityFrame == k.UnityFrame - 1 &&
                            previous.V4Id == k.V4Id && previous.V4Kind == k.V4Kind && k.V4ClipParam == LocomotionV4.None;
                        if (k.V4DeltaValid) k.V4DeltaT = k.V4T - previous.V4T;
                    }
                }
            }
            k.RootRotation = root.rotation;
            if (IkTraceSamples != null && _ik != null)
            {
                IkTraceObserved++;
                if (IkTraceSamples.Count < MaxIkTraceSamples)
                {
                    var sample = new IkTraceSample { Label = k.Etiket, Since = k.Since, RecorderFrame = k.Frame,
                        UnityFrame = k.UnityFrame, AdapterInstanceId = _traceAdapterInstanceId, Dt = k.Dt,
                        RootWorld = root.rotation, LeftRoot = Quaternion.Inverse(root.rotation) * _footL.rotation,
                        RightRoot = Quaternion.Inverse(root.rotation) * _footR.rotation };
                    sample.GoalAvailable = _ik.TryReadGoalPassTrace(out sample.Goal);
                    sample.LeftAvailable = _ik.TryReadLockTrace(0, out sample.Left);
                    sample.RightAvailable = _ik.TryReadLockTrace(1, out sample.Right);
                    sample.SamePassBothFeet = sample.GoalAvailable && sample.LeftAvailable && sample.RightAvailable &&
                        sample.Goal.Stamp.Frame == sample.Left.Stamp.Frame && sample.Goal.Stamp.Frame == sample.Right.Stamp.Frame &&
                        sample.Goal.Stamp.LayerIndex == sample.Left.Stamp.LayerIndex && sample.Goal.Stamp.LayerIndex == sample.Right.Stamp.LayerIndex &&
                        sample.Goal.Stamp.Sequence == sample.Left.Stamp.Sequence && sample.Goal.Stamp.Sequence == sample.Right.Stamp.Sequence;
                    IkTraceSamples.Add(sample);
                }
                else IkTraceDropped++;
            }
            k.WorstLimbBone = k.WorstTorsoBone = -1;
            k.WorstLimbDeg = k.WorstTorsoDeg = float.NaN;
        }
        private DiagClip[] ReadDiagnosticClips(int layer, bool next)
        {
            if (next) _anim.GetNextAnimatorClipInfo(layer, _diagClips); else _anim.GetCurrentAnimatorClipInfo(layer, _diagClips);
            var clips = new DiagClip[_diagClips.Count];
            for (int i = 0; i < clips.Length; i++) {
                AnimatorClipInfo info = _diagClips[i];
                clips[i] = new DiagClip { name = info.clip != null ? info.clip.name : "", instanceId = info.clip != null ? info.clip.GetInstanceID() : 0, weight = info.weight };
            }
            return clips;
        }

        private void LateUpdate()
        {
            if (!_bagli || _anim == null || _footL == null || _footR == null) return;
            _frame++;
            if (Etiket != _etiketOnce) { _etiketOnce = Etiket; Since = 0; _evL = _evR = _onL = _onR = 0; }
            else Since++;

            float dt = Time.deltaTime;
            Transform root = _anim.transform;
            Vector3 rootPos = root.position, l = _footL.position, r = _footR.position;
            Quaternion inv = Quaternion.Inverse(root.rotation);
            var k = new Kare { Etiket = Etiket, Since = Since, Frame = _frame, Zaman = Time.time, Dt = dt, PosL = l, PosR = r };
            k.GapL = Gap(_footL, _anim.leftFeetBottomHeight, out k.RayDigerL);
            k.GapR = Gap(_footR, _anim.rightFeetBottomHeight, out k.RayDigerR);
            if (k.RayDigerL || k.RayDigerR) RayDigerSayisi++;
            FazOlc(out k.FazL, out k.FazR, out k.BekL, out k.BekR);
            // the animated sole = the shown sole − the height the foot IK added this frame (weight × LeftLift / RightLift). Holds for the
            // per-foot drop (shown ≈ hit + feetBottomHeight → animated = −delta) and for S-03's PreserveSwingLift common correction
            // (WP-096 c1a2a8a4, where Lift is the shared delta + the foot's ground step); no served IK → the shown pose
            k.IkForce = FootIkAdapter.Force;
            k.IkAdapterServed = _ik != null && _ik.Served;
            k.IkWeight = _ik != null ? _ik.Weight : 0f;
            k.OwnerRate = _pv != null ? _pv.LocoRate : float.NaN;
            k.IkServed = _ik != null && _ik.Served && _ik.Weight > 0.001f;
            k.AnimGapL = k.IkServed ? k.GapL - _ik.Weight * _ik.LeftLift : k.GapL;
            k.AnimGapR = k.IkServed ? k.GapR - _ik.Weight * _ik.RightLift : k.GapR;
            // The raw goal is diagnostic until IK-off flat-floor geometry is measured. Never substitute it into AnimGap/AnimVy.
            Vector3 missingGoal = new Vector3(float.NaN, float.NaN, float.NaN);
            k.UnityFrame = Time.frameCount;
            k.RawGoalFrame = _ik != null ? _ik.AnimatedGoalSampleFrame : -1;
            k.RawGoalPosL = k.RawGoalPosR = k.RawGoalVL = k.RawGoalVR = missingGoal;
            k.RawGoalL = _ik != null && _ik.TryGetAnimatedFootGoal(0, out k.RawGoalPosL);
            k.RawGoalR = _ik != null && _ik.TryGetAnimatedFootGoal(1, out k.RawGoalPosR);
            // With IK on this includes the IK/body correction. Only Force=0, weight=0, lock=0 frames isolate goal/bone geometry.
            k.RawGoalMinusShownBoneL = k.RawGoalL ? k.RawGoalPosL - l : missingGoal;
            k.RawGoalMinusShownBoneR = k.RawGoalR ? k.RawGoalPosR - r : missingGoal;
            k.Rate = _anim.GetFloat(RateId);
            k.Honest = _pv != null ? _pv.HonestSpeed : 0f;
            if (_ik != null)
            {
                k.Locked = (byte)_ik.LockedFeet;
                k.Pelvis = _ik.Served ? _ik.PelvisOffset : 0f;
                k.Acc = _ik.PlantsAccepted; k.RelS = _ik.ReleasedByStretch; k.RelG = _ik.ReleasedByGate; k.RelL = _ik.ReleasedByLift; k.RelP = _ik.ReleasedByPlant;
            }

            Vector3 jump = rootPos - _prevRoot;
            k.Gecerli = _hasPrev && dt > 1e-6f && new Vector2(jump.x, jump.z).magnitude < 1f && Mathf.Abs(jump.y) < 0.5f;
            if (k.Gecerli)
            {
                Vector3 bv = jump / dt;
                bv.y = 0f;
                k.BodyV = bv;
                k.FootVL = (l - _prevL) / dt;
                k.FootVR = (r - _prevR) / dt;
                k.AnimVyL = (k.AnimGapL - _prevAnimL) / dt;
                k.AnimVyR = (k.AnimGapR - _prevAnimR) / dt;
                // Two consecutive Unity-frame samples are required; recorder Frame is a different counter.
                if (k.RawGoalL && _prevRawGoalValidL && _prevRawGoalFrame == k.UnityFrame - 1)
                {
                    k.RawGoalVL = (k.RawGoalPosL - _prevRawGoalL) / dt;
                    k.RawGoalVelocityL = true;
                }
                if (k.RawGoalR && _prevRawGoalValidR && _prevRawGoalFrame == k.UnityFrame - 1)
                {
                    k.RawGoalVR = (k.RawGoalPosR - _prevRawGoalR) / dt;
                    k.RawGoalVelocityR = true;
                }
                k.TemasL = k.FazL >= 0.5f;   // online: the loop's cycle count only; Siniflandir rebuilds it
                k.TemasR = k.FazR >= 0.5f;
                k.TemasEskiL = !float.IsNaN(k.GapL) && k.GapL <= R2LokomotionOlcumTests.TemasYukseklik && Mathf.Abs(k.FootVL.y) <= R2LokomotionOlcumTests.TemasDikeyHiz;
                k.TemasEskiR = !float.IsNaN(k.GapR) && k.GapR <= R2LokomotionOlcumTests.TemasYukseklik && Mathf.Abs(k.FootVR.y) <= R2LokomotionOlcumTests.TemasDikeyHiz;
            }

            // plant events (collapsed per foot inside OlayAyrimKare) and contact onsets
            if ((_pending & 1) != 0 && _frame - _lastEvL >= R2LokomotionOlcumTests.OlayAyrimKare) { k.OlayL = true; _lastEvL = _frame; OlaySayisi++; }
            if ((_pending & 2) != 0 && _frame - _lastEvR >= R2LokomotionOlcumTests.OlayAyrimKare) { k.OlayR = true; _lastEvR = _frame; OlaySayisi++; }
            k.OffL = (_pending & 4) != 0;
            k.OffR = (_pending & 8) != 0;
            _pending = 0;
            if (k.TemasL && !_prevTemasL && _frame - _lastOnL >= R2LokomotionOlcumTests.OlayAyrimKare) { k.BaslangicL = true; _lastOnL = _frame; }
            if (k.TemasR && !_prevTemasR && _frame - _lastOnR >= R2LokomotionOlcumTests.OlayAyrimKare) { k.BaslangicR = true; _lastOnR = _frame; }
            if (Etiket >= 0 && Since >= R2LokomotionOlcumTests.Isinma)
            {
                if (k.OlayL) _evL++;
                if (k.OlayR) _evR++;
                if (k.BaslangicL) _onL++;
                if (k.BaslangicR) _onR++;
            }

            if (_diagEnabled) CaptureDiagnostics(ref k, root);

            // bone rotation change in the root's space, °/frame at 60 fps
            float scale = dt > 1e-6f ? (1f / R2LokomotionOlcumTests.Fps) / dt : 0f;
            for (int b = 0; b < _bones.Length; b++)
            {
                Transform t = _bones[b];
                if (t == null) { Derece.Add(float.NaN); continue; }
                Quaternion q = inv * t.rotation;
                Derece.Add(k.Gecerli ? Quaternion.Angle(_prevRot[b], q) * scale : float.NaN);
                if (_diagEnabled && k.Gecerli) {
                    float degrees = Derece[Derece.Count - 1];   // exact existing acceptance sample
                    if (!float.IsNaN(degrees)) {
                        if (b < R2LokomotionOlcumTests.UzuvSayisi && (k.WorstLimbBone < 0 || degrees > k.WorstLimbDeg)) {
                            k.WorstLimbBone = b; k.WorstLimbDeg = degrees; k.WorstLimbPrev = _prevRot[b]; k.WorstLimbNow = q;
                        }
                        else if (b >= R2LokomotionOlcumTests.UzuvSayisi && (k.WorstTorsoBone < 0 || degrees > k.WorstTorsoDeg)) {
                            k.WorstTorsoBone = b; k.WorstTorsoDeg = degrees; k.WorstTorsoPrev = _prevRot[b]; k.WorstTorsoNow = q;
                        }
                    }
                }
                _prevRot[b] = q;
            }

            Kareler.Add(k);
            _prevRoot = rootPos;
            _prevL = l;
            _prevR = r;
            _prevAnimL = k.AnimGapL;
            _prevAnimR = k.AnimGapR;
            _prevRawGoalL = k.RawGoalPosL;
            _prevRawGoalR = k.RawGoalPosR;
            _prevRawGoalFrame = k.RawGoalFrame;
            _prevRawGoalValidL = k.RawGoalL;
            _prevRawGoalValidR = k.RawGoalR;
            _prevTemasL = k.TemasL;
            _prevTemasR = k.TemasR;
            _hasPrev = true;
        }
    }
}
