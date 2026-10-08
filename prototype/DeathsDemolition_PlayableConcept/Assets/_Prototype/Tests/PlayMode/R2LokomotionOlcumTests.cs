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
using UnityEngine;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-083 R2 locomotion gates L1, L1t, L2, L3 (S-06; tools/qa/r2-gates.json, definitions K/wp-078/R2-KABUL-TASLAK.md §2,
    /// S-06 / C-00 05.10). One course on the default player path: the real Run scene's player, its motor, camera and animator,
    /// driven by stick input (third person, the body faces the view, so the eight stick directions are the eight strafe
    /// directions), sampled once per frame at a fixed 60 fps (Time.captureDeltaTime = 1/60) AFTER the animator, its IK passes and
    /// every LateUpdate solver (sampler order 32000), on a flat Environment-layer floor so that walls and slopes stay out of it.
    /// <list type="bullet">
    /// <item>Course A, steady gait: 8 directions × walk / run. Walk = the stick tilt that gives walk_fwd's no-slide speed; run = full
    /// tilt. Each segment: 1 s warm-up (excluded), then until both feet show ≥ 6 cycles (and ≥ 3 s), bounded at 12 s (walk) / 10 s (run).</item>
    /// <item>Course B, transitions × 5: start (still → run), stop (run → still), pivot 180° (run → run reversed), 90° cut
    /// (run → run ±90°). Base directions fwd, right, back, left, fwd_r; the cut alternates right / left.</item>
    /// </list>
    /// Contact (L1, L1t): the sole (foot bone − Animator.left/rightFeetBottomHeight, the convention FootIkAdapter uses) within 3 cm of
    /// the ground ray AND the foot's vertical speed ≤ 0.15 m/s. Slide ratio = the foot's horizontal world speed ÷ the body's horizontal
    /// speed, the body's speed floored at <see cref="GovdeHizTabani"/> (near standstill — a stop's last frames — the ratio is undefined;
    /// the floor reads it as m/s instead of hiding it).
    /// <list type="bullet">
    /// <item>L1 avg / p95 — every contact foot-frame of course A after the warm-up.</item>
    /// <item>L1t avg (p95 reported) — every contact foot-frame in the 0.3 s after each course B input change.</item>
    /// <item>L2 deviationMax — per steady segment |cadence × step − v| / v, the largest. Cadence = 2 / T steps/s, T = the median
    /// interval between consecutive same-foot plant events (foot_l / foot_r; the contact onsets if the clips raise no events).
    /// Step = u × T / 2: the distance the body travels per step if the planted foot held, u = the planted foot's backward speed
    /// under the body along the travel direction (mean over contact frames). The draft's literal "same-foot contact point to
    /// contact point / 2" taken in world space is D / T ≡ v for any periodic gait (a tautology at runtime), so it is logged only.</item>
    /// <item>L3 — every bone's rotation in the character root's space, the angle between consecutive frames (°/frame at 60 fps).
    /// limbDegPerFrameMax over Upper/LowerLeg, Foot, UpperArm, LowerArm, Hand (L+R); torsoDegPerFrameMax over Hips, Spine, Chest,
    /// UpperChest, Neck, Head; in the 1 s after each course B input change. transitionToSteadyRatioMax = a bone's transition peak ÷
    /// the same bone's steady run peak in the direction(s) the transition joins (floor <see cref="KararliTepeTabani"/> °/frame).</item>
    /// </list>
    /// [Explicit]: the R2 chain runs it by full name; each gate whose source is in the recording is written through R2Olcum, a gate
    /// whose source is missing is not written (BLOCKED by name) and the test fails naming it, after every other gate was written.
    /// </summary>
    [TestFixture]
    public sealed class R2LokomotionOlcumTests
    {
        public const string TestName = "DD.Prototype.Tests.PlayMode.R2LokomotionOlcumTests.L1_L3_Lokomotion_8Yon_KalkisDurusDonus";

        // ---- the contract's definitions (R2-KABUL-TASLAK §2)
        public const float TemasYukseklik = 0.03f;      // sole ↔ ground ray, m
        public const float TemasDikeyHiz = 0.15f;       // foot vertical speed, m/s
        public const int Fps = 60;
        public const int L1tPencere = 18;               // 0.3 s at 60 fps
        public const int L3Pencere = 60;                // 1 s at 60 fps after the input change (start / stop / pivot / cut)
        public const int Isinma = 60;                   // 1 s warm-up excluded from the steady segments (L2: "ilk 1 s ısınma hariç")
        public const int MinDongu = 6;                  // L2: ≥ 6 cycles per direction
        public const int Tekrar = 5;                    // L3 / L1t: each scenario × 5
        // ---- this test's choices (reported; S-06 / C-00 to confirm on the first run)
        public const float GovdeHizTabani = 1.0f;       // m/s — slide ratio denominator floor
        public const float KararliTepeTabani = 1.0f;    // °/frame — L3 ratio denominator floor
        public const int OlayAyrimKare = 12;            // 0.2 s: a second same-foot plant inside it is the same beat
        public const float ZeminYukseklik = 150f;       // the test floor above the spawn, m
        public const float ZeminYariGenislik = 200f;    // its half extent, m
        public const float MerkezYaricap = 90f;         // re-centre (teleport, standing) beyond this, m
        public const double KararliTolerans = 0.20;     // a steady segment's body speed within ±20 % of the motor's command

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

        private enum Tur { Kararli, Kalkis, Durus, Pivot180, Kesme90 }

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
        }

        private SpecialWeaponSahne _s;
        private GameObject _zemin;
        private LokomotionKaydedici _kayit;

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
            if (_kayit != null) { _kayit.Birak(); Object.Destroy(_kayit.gameObject); }
            _kayit = null;
            if (_zemin != null) Object.Destroy(_zemin);
            _zemin = null;
            // R3 (C-00 07.10, S-01 inventory): the next test starts in the process default — TopDown (user 06.10: tests run top-down only);
            // a ThirdPerson left here ran the PlayMode tests after this fixture in TP (R2FiksturKameraTests)
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

        /// <summary>n frames with the stick at <paramref name="yon"/>, recorded under <paramref name="etiket"/> (−1 = not measured).</summary>
        private IEnumerator Sur(int n, Vector2 yon, int etiket, Parca p)
        {
            for (int i = 0; i < n; i++)
            {
                if (!Canli()) yield break;
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
            _s.Rig.Teleport(merkez + Vector3.up * 0.05f, _s.Rig.transform.rotation);
            yield return Sur(45, Vector2.zero, -1, null);
        }

        [Category("WP083")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(900000)]
        public IEnumerator L1_L3_Lokomotion_8Yon_KalkisDurusDonus()
        {
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = 1f / Fps;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body, "skinned oyuncu yok");
            Assert.IsNotNull(body.Animator, "oyuncu Animator'ı yok");
            Assert.IsTrue(body.Authored, "oyuncu animasyon seti yazılmış değil (primitive yol) — lokomotion ölçülemez");
            Assert.IsTrue(body.Animator.isHuman, "oyuncu Animator'ı Humanoid değil");
            CharacterAnimationSet anim = body.AnimationSet;
            Assert.IsNotNull(anim, "CharacterAnimationSet yok");

            // the flat floor: Environment layer (the foot IK's and the motor's ground), above the spawn, away from the world
            Vector3 merkez = _s.Rig.transform.position + Vector3.up * ZeminYukseklik;
            _zemin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _zemin.name = "R2Loko_Zemin";
            _zemin.layer = PrototypeLayers.Environment;
            _zemin.transform.localScale = new Vector3(ZeminYariGenislik * 2f, 1f, ZeminYariGenislik * 2f);
            _zemin.transform.position = merkez - Vector3.up * 0.5f;

            var go = new GameObject("R2Loko_Kaydedici");
            _kayit = go.AddComponent<LokomotionKaydedici>();
            _kayit.Bagla(body.Animator, body.transform);

            // course definition: steady segments first (index = gait × 8 + direction), then the transitions
            var parcalar = new List<Parca>();
            for (int g = 0; g < 2; g++)
                for (int k = 0; k < 8; k++)
                    parcalar.Add(new Parca { Ad = (g == 0 ? "walk_" : "run_") + YonAd[k], Tur = Tur.Kararli, Yuruyus = g, Yon = k });
            int[] tabanlar = { 0, 2, 4, 6, 1 };

            float maxSpeed = _s.Rig.Motor.MaxSpeed;
            float walkClip = anim.SpeedOf(C.PlayerWalkFwd);
            float walkStick = maxSpeed > 0.1f && walkClip > 0.1f ? Mathf.Clamp(walkClip / maxSpeed, 0.15f, 0.7f) : 0.42f;
            int[] sira = { 0, 4, 2, 6, 1, 5, 3, 7 };   // opposite pairs keep the course near the centre

            yield return Merkezle(merkez, true);
            yield return Sur(60, Vector2.zero, -1, null);
            bool zeminde = _s.Rig.Motor.IsGrounded && Mathf.Abs(_s.Rig.transform.position.y - merkez.y) < 0.25f;
            Debug.Log("[DD-TEST] wp083.loco setup floor=" + zeminde + " y=" + _s.Rig.transform.position.y.ToString("F2", CultureInfo.InvariantCulture) +
                      " floorY=" + merkez.y.ToString("F2", CultureInfo.InvariantCulture) + " v4=" + body.HasLocomotionV4 + " footIk=" + (body.FootIk != null) +
                      " maxSpeed=" + maxSpeed.ToString("F2", CultureInfo.InvariantCulture) + " walkClip=" + walkClip.ToString("F2", CultureInfo.InvariantCulture) +
                      " walkStick=" + walkStick.ToString("F3", CultureInfo.InvariantCulture) + " ref=" + anim.LocomotionReferenceSpeed.ToString("F2", CultureInfo.InvariantCulture) +
                      " bones=" + _kayit.BulunanKemikler() + " camera=" + CameraModeService.Current);
            Assert.IsTrue(zeminde, "oyuncu test zeminine oturmadı (y " + _s.Rig.transform.position.y + " / zemin " + merkez.y + ")");

            // ---- course A: steady gait (the body faces the view while moving, so the stick directions are strafe directions)
            float yuzHata = 0f;
            for (int g = 0; g < 2 && _iptal == null; g++)
            {
                float m = g == 0 ? walkStick : 1f;
                int cap = Isinma + (g == 0 ? 720 : 600);
                for (int j = 0; j < sira.Length && _iptal == null; j++)
                {
                    int k = sira[j];
                    int idx = g * 8 + k;
                    Parca p = parcalar[idx];
                    yield return Merkezle(merkez, false);
                    for (int f = 0; f < cap; f++)
                    {
                        if (!Canli()) break;
                        _s.Girdi.Yon = Yon8(k, m);
                        _kayit.Etiket = idx;
                        yield return null;
                        p.Kare++;
                        if (_kayit.Since >= Isinma)
                        {
                            p.KomutHizToplam += _s.Rig.Motor.CommandedPlanarVelocity.magnitude;
                            p.KomutHizN++;
                            PrototypeCameraRig cam = PrototypeCameraRig.Active;
                            if (cam != null) yuzHata = Mathf.Max(yuzHata, Vector3.Angle(Yatay(_s.Rig.transform.forward), Yatay(cam.PlanarForward)));
                        }

                        if (_kayit.Since >= Isinma + 180 && _kayit.DonguYeter(MinDongu + 1)) break;
                    }
                }
            }

            // ---- course B: transitions × 5
            for (int r = 0; r < Tekrar && _iptal == null; r++)
            {
                int b = tabanlar[r], ters = (b + 4) & 7, yan = (ters + ((r & 1) == 0 ? 2 : 6)) & 7;
                var start = new Parca { Ad = "start_" + YonAd[b] + "#" + r, Tur = Tur.Kalkis, Yuruyus = 1, Yon = b, Ref = new[] { 8 + b } };
                var stop = new Parca { Ad = "stop_" + YonAd[b] + "#" + r, Tur = Tur.Durus, Yuruyus = 1, Yon = b, Ref = new[] { 8 + b } };
                var pivot = new Parca { Ad = "pivot180_" + YonAd[b] + ">" + YonAd[ters] + "#" + r, Tur = Tur.Pivot180, Yuruyus = 1, Yon = ters, Ref = new[] { 8 + b, 8 + ters } };
                var cut = new Parca { Ad = "cut90_" + YonAd[ters] + ">" + YonAd[yan] + "#" + r, Tur = Tur.Kesme90, Yuruyus = 1, Yon = yan, Ref = new[] { 8 + ters, 8 + yan } };
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
            Debug.Log("[DD-TEST] wp083.loco course frames=" + _kayit.Kareler.Count + " plantEvents=" + _kayit.OlaySayisi + " aborted=" + (_iptal ?? "no") + " facingVsViewMaxDeg=" + yuzHata.ToString("F1", CultureInfo.InvariantCulture) +
                      " footIk served=" + (body.FootIk != null && body.FootIk.Served) + " plants=" + (body.FootIk != null ? body.FootIk.PlantsAccepted : -1));

            // ---- analysis: every gate on its own; a gate without its source is named, not written
            var hatalar = new List<string>();
            if (_iptal != null) hatalar.Add("kurs yarıda kaldı: " + _iptal);
            var yazilacak = new List<KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>>>();

            Olc("L1", hatalar, yazilacak, () => L1Hesapla(parcalar));
            Olc("L1t", hatalar, yazilacak, () => L1tHesapla(parcalar));
            Olc("L2", hatalar, yazilacak, () => L2Hesapla(parcalar));
            Olc("L3", hatalar, yazilacak, () => L3Hesapla(parcalar));

            // writes first (every red value recorded), the thresholds after; one literal call per gate (r2_gates_lint reads the gate
            // id and the keys at the call)
            foreach (KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>> w in yazilacak)
            {
                Dictionary<string, double> v = w.Value.Key;
                int n = w.Value.Value;
                switch (w.Key)
                {
                    case "L1":
                        R2Olcum.Yaz("L1", TestName, new Dictionary<string, double> { { "avg", v["avg"] }, { "p95", v["p95"] } }, n);
                        break;
                    case "L1t":
                        R2Olcum.Yaz("L1t", TestName, new Dictionary<string, double> { { "avg", v["avg"] }, { "p95", v["p95"] } }, n);
                        break;
                    case "L2":
                        R2Olcum.Yaz("L2", TestName, new Dictionary<string, double> { { "deviationMax", v["deviationMax"] } }, n);
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

            if (hatalar.Count > 0) Assert.Fail(string.Join("\n", hatalar));
        }

        private struct Sonuc
        {
            public Dictionary<string, double> Degerler;
            public int Ornek;
            public string Eksik;   // the source is not in the recording (gate not written)
        }

        private static void Olc(string gate, List<string> hatalar, List<KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>>> yazilacak, Func<Sonuc> hesap)
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

            if (s.Eksik != null)
            {
                hatalar.Add("R2 " + gate + ": kaynak kayıtta yok, yazılmadı (BLOCKED) — " + s.Eksik);
                return;
            }

            yazilacak.Add(new KeyValuePair<string, KeyValuePair<Dictionary<string, double>, int>>(gate, new KeyValuePair<Dictionary<string, double>, int>(s.Degerler, s.Ornek)));
        }

        private static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
        private static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

        private static double P95(List<double> xs)
        {
            if (xs.Count == 0) return double.NaN;
            var c = new List<double>(xs);
            c.Sort();
            int i = Mathf.Clamp((int)Math.Ceiling(0.95 * c.Count) - 1, 0, c.Count - 1);
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

        private void Kaymalar(Parca p, int idx, int sinceMin, int sinceMax, List<double> into)
        {
            List<LokomotionKaydedici.Kare> ks = _kayit.Kareler;
            for (int i = 0; i < ks.Count; i++)
            {
                LokomotionKaydedici.Kare k = ks[i];
                if (k.Etiket != idx || !k.Gecerli || k.Since < sinceMin || k.Since > sinceMax) continue;
                if (k.TemasL) into.Add(Oran(k.FootVL, k.BodyV));
                if (k.TemasR) into.Add(Oran(k.FootVR, k.BodyV));
            }
        }

        private Sonuc L1Hesapla(List<Parca> parcalar)
        {
            var all = new List<double>();
            var rows = new List<string>();
            string eksik = null;
            for (int idx = 0; idx < 16; idx++)
            {
                Parca p = parcalar[idx];
                var seg = new List<double>();
                Kaymalar(p, idx, Isinma, int.MaxValue, seg);
                all.AddRange(seg);
                double v = _kayit.OrtalamaGovdeHizi(idx, Isinma);
                double cmd = p.KomutHizN > 0 ? p.KomutHizToplam / p.KomutHizN : double.NaN;
                bool kararli = !double.IsNaN(cmd) && cmd > 0.1 && Math.Abs(v - cmd) / cmd <= KararliTolerans;
                rows.Add(p.Ad + " avg " + F(Ort(seg)) + " p95 " + F(P95(seg)) + " n " + seg.Count + " v " + F2(v) + "/" + F2(cmd) + " m/s" +
                         " gap " + _kayit.TabanOzeti(idx, Isinma) + (kararli ? "" : " NOT-STEADY"));
                if (seg.Count == 0) eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": temas karesi yok";
                else if (!kararli) eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": kararlı yürüyüş yok (v " + F2(v) + " / komut " + F2(cmd) + ")";
            }

            double avg = Ort(all), p95 = P95(all);
            Debug.Log("[DD-TEST] wp083.l1 avg=" + F(avg) + " p95=" + F(p95) + " samples=" + all.Count + " floor=" + GovdeHizTabani + " m/s :: " + string.Join(" | ", rows));
            return new Sonuc { Degerler = new Dictionary<string, double> { { "avg", avg }, { "p95", p95 } }, Ornek = all.Count, Eksik = eksik };
        }

        private Sonuc L1tHesapla(List<Parca> parcalar)
        {
            var all = new List<double>();
            var byKind = new Dictionary<Tur, List<double>>();
            int pencereli = 0;
            for (int idx = 16; idx < parcalar.Count; idx++)
            {
                Parca p = parcalar[idx];
                if (!byKind.TryGetValue(p.Tur, out List<double> list)) byKind[p.Tur] = list = new List<double>();
                var seg = new List<double>();
                Kaymalar(p, idx, 1, L1tPencere, seg);
                list.AddRange(seg);
                all.AddRange(seg);
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

        private Sonuc L2Hesapla(List<Parca> parcalar)
        {
            double devMax = 0;
            int toplamDongu = 0;
            var rows = new List<string>();
            string eksik = null;
            List<LokomotionKaydedici.Kare> ks = _kayit.Kareler;
            for (int idx = 0; idx < 16; idx++)
            {
                Parca p = parcalar[idx];
                // the travel direction and the body speed along it
                Vector3 sum = Vector3.zero;
                int n = 0;
                for (int i = 0; i < ks.Count; i++)
                {
                    LokomotionKaydedici.Kare k = ks[i];
                    if (k.Etiket != idx || !k.Gecerli || k.Since < Isinma) continue;
                    sum += k.BodyV;
                    n++;
                }

                if (n == 0 || sum.sqrMagnitude < 1e-8f) { eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": gövde hareketi yok"; rows.Add(p.Ad + " n/a"); continue; }
                Vector3 dir = sum.normalized;
                double v = Vector3.Dot(sum / n, dir);

                // the planted foot's backward speed under the body along the travel direction (contact frames)
                double uSum = 0;
                int uN = 0;
                // plant intervals (s) per foot: events, else contact onsets
                var evL = new List<double>(); var evR = new List<double>(); var onL = new List<double>(); var onR = new List<double>();
                var posEvL = new List<Vector3>(); var posEvR = new List<Vector3>();
                for (int i = 0; i < ks.Count; i++)
                {
                    LokomotionKaydedici.Kare k = ks[i];
                    if (k.Etiket != idx || k.Since < Isinma) continue;
                    if (k.OlayL) { evL.Add(k.Zaman); posEvL.Add(k.PosL); }
                    if (k.OlayR) { evR.Add(k.Zaman); posEvR.Add(k.PosR); }
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
                int dongu = Math.Min(a.Count, bb.Count) - 1;
                double T = Medyan(araliklar);
                double u = uN > 0 ? uSum / uN : double.NaN;
                double kadans = 2.0 / T;                 // steps / s
                double adim = u * T / 2.0;               // m per step, planted-foot implied
                double dev = Math.Abs(kadans * adim - v) / v;
                // the draft's literal reading (world contact point → next contact point of the same foot / 2), for the record
                double literal = double.NaN;
                if (posEvL.Count >= 2 && evL.Count >= 2)
                {
                    Vector3 d = Yatay(posEvL[posEvL.Count - 1] - posEvL[0]);
                    literal = Math.Abs(d.magnitude / (evL[evL.Count - 1] - evL[0]) - v) / v;
                }

                rows.Add(p.Ad + " src " + (olay ? "events" : temas ? "contacts" : "SHORT") + " cycles " + dongu + " T " + F(T) + " s cadence " + F2(kadans * 60) + " spm step " + F(adim) +
                         " m v " + F2(v) + " u " + F2(u) + " dev " + F(dev) + " literal " + F(literal));
                if (!olay && !temas) { eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": < " + MinDongu + " döngü (olay L/R " + Math.Max(0, evL.Count - 1) + "/" + Math.Max(0, evR.Count - 1) + ", temas " + Math.Max(0, onL.Count - 1) + "/" + Math.Max(0, onR.Count - 1) + ")"; continue; }
                if (uN == 0 || double.IsNaN(dev)) { eksik = (eksik == null ? "" : eksik + "; ") + p.Ad + ": temas karesi yok"; continue; }
                devMax = Math.Max(devMax, dev);
                toplamDongu += dongu;
            }

            Debug.Log("[DD-TEST] wp083.l2 deviationMax=" + F(devMax) + " cycles=" + toplamDongu + " :: " + string.Join(" | ", rows));
            return new Sonuc { Degerler = new Dictionary<string, double> { { "deviationMax", devMax } }, Ornek = toplamDongu, Eksik = eksik };
        }

        private static Vector3 Yatay(Vector3 v) { v.y = 0f; return v; }

        private Sonuc L3Hesapla(List<Parca> parcalar)
        {
            int nb = Kemikler.Length;
            List<LokomotionKaydedici.Kare> ks = _kayit.Kareler;
            List<float> deg = _kayit.Derece;
            // steady run peaks per segment per bone
            var tepe = new double[parcalar.Count, nb];
            var tepeKare = new int[parcalar.Count, nb];
            var pencereKare = new int[parcalar.Count];
            for (int i = 0; i < ks.Count; i++)
            {
                LokomotionKaydedici.Kare k = ks[i];
                if (k.Etiket < 0 || !k.Gecerli) continue;
                Parca p = parcalar[k.Etiket];
                bool olc = p.Tur == Tur.Kararli ? k.Since >= Isinma : k.Since >= 1 && k.Since <= L3Pencere;
                if (!olc) continue;
                pencereKare[k.Etiket]++;
                for (int b = 0; b < nb; b++)
                {
                    float d = deg[i * nb + b];
                    if (float.IsNaN(d)) continue;
                    if (d > tepe[k.Etiket, b]) { tepe[k.Etiket, b] = d; tepeKare[k.Etiket, b] = k.Since; }
                }
            }

            double limb = 0, torso = 0, ratio = 0;
            string limbAt = "", torsoAt = "", ratioAt = "";
            int ornek = 0, gecis = 0;
            var rows = new List<string>();
            string eksik = null;
            for (int t = 16; t < parcalar.Count; t++)
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

            for (int r = 8; r < 16; r++)
                if (pencereKare[r] == 0) eksik = (eksik == null ? "" : eksik + "; ") + parcalar[r].Ad + ": kararlı koşu tepesi yok (oran tabanı)";
            if (gecis < 4 * Tekrar) eksik = (eksik == null ? "" : eksik + "; ") + "geçiş " + gecis + "/" + (4 * Tekrar);
            if (!_kayit.KemikVar(0) || !_kayit.KemikVar(12)) eksik = (eksik == null ? "" : eksik + "; ") + "uzuv/gövde kemiği yok: " + _kayit.BulunanKemikler();

            var steady = new StringBuilder();
            for (int r = 0; r < 16; r++)
            {
                double sl = 0, st = 0;
                for (int b = 0; b < nb; b++) { if (b < UzuvSayisi) sl = Math.Max(sl, tepe[r, b]); else st = Math.Max(st, tepe[r, b]); }
                steady.Append(parcalar[r].Ad).Append(' ').Append(F2(sl)).Append('/').Append(F2(st)).Append(r < 15 ? ", " : "");
            }

            Debug.Log("[DD-TEST] wp083.l3 limbDegPerFrameMax=" + F2(limb) + " (" + limbAt + ") torsoDegPerFrameMax=" + F2(torso) + " (" + torsoAt + ") transitionToSteadyRatioMax=" + F2(ratio) +
                      " (" + ratioAt + ") transitions=" + gecis + " frames=" + ornek + " window=" + L3Pencere + " floor=" + KararliTepeTabani + "°/frame :: steady limb/torso " + steady +
                      " :: " + string.Join(" | ", rows));
            return new Sonuc
            {
                Degerler = new Dictionary<string, double> { { "limbDegPerFrameMax", limb }, { "torsoDegPerFrameMax", torso }, { "transitionToSteadyRatioMax", ratio } },
                Ornek = ornek,
                Eksik = eksik,
            };
        }
    }

    /// <summary>
    /// The R2 locomotion sampler: one record per frame AFTER the animator, OnAnimatorIK and every LateUpdate solver of the player
    /// (order 32000 > WeaponHoldIk 890 · WeaponGripIk 910 · TwistCorrector 920 · MagazineContactIk 945) — the pose the frame shows.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class LokomotionKaydedici : MonoBehaviour
    {
        internal struct Kare
        {
            public int Etiket, Since;
            public float Zaman, Dt;
            public bool Gecerli;                  // a previous frame, no teleport, dt > 0
            public Vector3 BodyV;                 // horizontal, m/s
            public Vector3 FootVL, FootVR;        // m/s
            public Vector3 PosL, PosR;            // foot bone, world
            public float GapL, GapR;              // sole above the ground ray, m (NaN = no ground)
            public bool TemasL, TemasR;
            public bool OlayL, OlayR;             // plant events (collapsed)
            public bool BaslangicL, BaslangicR;   // contact onsets
        }

        public readonly List<Kare> Kareler = new List<Kare>(12000);
        public readonly List<float> Derece = new List<float>(12000 * 18);   // per frame × bone, °/frame at 60 fps (NaN = n/a)
        public int Etiket = -1;
        public int Since { get; private set; }
        public int OlaySayisi { get; private set; }

        private Animator _anim;
        private Transform _govde;
        private Transform _footL, _footR;
        private Transform[] _bones;
        private Quaternion[] _prevRot;
        private bool _hasPrev;
        private Vector3 _prevRoot, _prevL, _prevR;
        private bool _prevTemasL, _prevTemasR;
        private int _lastEvL = -1000, _lastEvR = -1000, _lastOnL = -1000, _lastOnR = -1000;
        private int _pending;
        private int _etiketOnce = int.MinValue;
        private int _frame;
        private int _evL, _evR, _onL, _onR;   // counted in the current label after the warm-up
        private bool _bagli;

        public void Bagla(Animator anim, Transform govde)
        {
            _anim = anim;
            _govde = govde;
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
        }

        /// <summary>Valid frames of a label with since in [sinceMin, sinceMax].</summary>
        public int PencereKareSayisi(int etiket, int sinceMin, int sinceMax)
        {
            int n = 0;
            foreach (Kare k in Kareler) if (k.Etiket == etiket && k.Gecerli && k.Since >= sinceMin && k.Since <= sinceMax) n++;
            return n;
        }

        public double OrtalamaGovdeHizi(int etiket, int sinceMin)
        {
            double s = 0;
            int n = 0;
            foreach (Kare k in Kareler)
            {
                if (k.Etiket != etiket || !k.Gecerli || k.Since < sinceMin) continue;
                s += k.BodyV.magnitude;
                n++;
            }

            return n > 0 ? s / n : double.NaN;
        }

        /// <summary>The sole gap (min / median, cm) over a label — the contact definition's input, for the first run's check.</summary>
        public string TabanOzeti(int etiket, int sinceMin)
        {
            var g = new List<float>();
            foreach (Kare k in Kareler)
            {
                if (k.Etiket != etiket || k.Since < sinceMin) continue;
                if (!float.IsNaN(k.GapL)) g.Add(k.GapL);
                if (!float.IsNaN(k.GapR)) g.Add(k.GapR);
            }

            if (g.Count == 0) return "n/a";
            g.Sort();
            return (g[0] * 100f).ToString("F1", CultureInfo.InvariantCulture) + "/" + (g[g.Count / 2] * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm";
        }

        private float Gap(Transform foot, float bottom)
        {
            Vector3 p = foot.position;
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.5f, PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore))
                return p.y - bottom - hit.point.y;
            return float.NaN;
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
            var k = new Kare { Etiket = Etiket, Since = Since, Zaman = Time.time, Dt = dt, PosL = l, PosR = r };
            k.GapL = Gap(_footL, _anim.leftFeetBottomHeight);
            k.GapR = Gap(_footR, _anim.rightFeetBottomHeight);
            Vector3 jump = rootPos - _prevRoot;
            k.Gecerli = _hasPrev && dt > 1e-6f && new Vector2(jump.x, jump.z).magnitude < 1f && Mathf.Abs(jump.y) < 0.5f;
            if (k.Gecerli)
            {
                Vector3 bv = jump / dt;
                bv.y = 0f;
                k.BodyV = bv;
                k.FootVL = (l - _prevL) / dt;
                k.FootVR = (r - _prevR) / dt;
                k.TemasL = !float.IsNaN(k.GapL) && k.GapL <= R2LokomotionOlcumTests.TemasYukseklik && Mathf.Abs(k.FootVL.y) <= R2LokomotionOlcumTests.TemasDikeyHiz;
                k.TemasR = !float.IsNaN(k.GapR) && k.GapR <= R2LokomotionOlcumTests.TemasYukseklik && Mathf.Abs(k.FootVR.y) <= R2LokomotionOlcumTests.TemasDikeyHiz;
            }

            // plant events (collapsed per foot inside OlayAyrimKare) and contact onsets
            if ((_pending & 1) != 0 && _frame - _lastEvL >= R2LokomotionOlcumTests.OlayAyrimKare) { k.OlayL = true; _lastEvL = _frame; OlaySayisi++; }
            if ((_pending & 2) != 0 && _frame - _lastEvR >= R2LokomotionOlcumTests.OlayAyrimKare) { k.OlayR = true; _lastEvR = _frame; OlaySayisi++; }
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

            // bone rotation change in the root's space, °/frame at 60 fps
            float scale = dt > 1e-6f ? (1f / R2LokomotionOlcumTests.Fps) / dt : 0f;
            for (int b = 0; b < _bones.Length; b++)
            {
                Transform t = _bones[b];
                if (t == null) { Derece.Add(float.NaN); continue; }
                Quaternion q = inv * t.rotation;
                Derece.Add(k.Gecerli ? Quaternion.Angle(_prevRot[b], q) * scale : float.NaN);
                _prevRot[b] = q;
            }

            Kareler.Add(k);
            _prevRoot = rootPos;
            _prevL = l;
            _prevR = r;
            _prevTemasL = k.TemasL;
            _prevTemasR = k.TemasR;
            _hasPrev = true;
        }
    }
}
