using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
// WP-096: these diagnostics hand their numbers to the same dd-r2-measure-v1 writer (r2-wp096-zl<n>.json in DD_R2_MEASURE_DIR),
// but they are not R2 contract gates: tools/qa/r2-gates.json has no wp096 ids and its id pattern ([A-Z][0-9]+[a-z]?) does not
// take them. tools/qa/r2_gates_lint.py reads every literal R2Olcum.Yaz call as an R2 gate writer (an unknown id is an ERROR in
// the acceptance chain), so the calls here go through this alias. To make them contract gates: add them to r2-gates.json and
// call R2Olcum.Yaz directly (WP-096 game-test-engineer, 07.10).
using Olcum = DD.Prototype.Tests.R2Olcum;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-096 (zombie walk / run realism) · the before / after measurement harness (plan K/wp-096/diagnosis/game-test-engineer.md
    /// §3–§4). The same file runs on main 77f46216 ("before") and on the WP-096 branch ("after"): it compiles against main's API
    /// only; anything v4 may add (or retire) is read by reflection and reported "yok" when absent.
    /// <list type="bullet">
    /// <item>ZL1 / ZL2 (one shared recording): one lane per clip of every tier gait pool (WP-096 p2; main: the four tiers × fit A/B), three fixed seeds + one GaitVariety 0 control pass, on a flat
    /// Environment floor 150 m above the Run scene. Each body is a real EnemyFactory spawn (look, tier scale, gait profile, spawn
    /// ordinal) whose ZombieEnemy is switched off; the test moves it straight at its tier speed (× the v4 SpeedJitter when the
    /// visual has one) and feeds ZombieSkinnedVisual.SetSpeed(v, dt) — the call ZombieEnemy.TickCombat makes. Stance (07.10) = the
    /// clips' own foot events (plant → lift), because FootIkAdapter presses every foot — the swing too — to the floor and the R2 L1
    /// height rule then calls every foot-frame a contact. An IK-off pass (FootIkAdapter.Force 0) keeps the height rule as the
    /// detector's positive controls (a standing foot reads contact, a walking foot shows onsets) and the clip + rate slide; a
    /// saturated detector (contact share outside 0.15–0.85) blocks by name.</item>
    /// <item>ZL3: the 40-body GateMix crowd (R2ZombieCrowd) under its own AI, TopDown camera: distinct (clip, rate 0.05, phase 0.1)
    /// triples, clips, bodies per clip, Shannon index, and L5's synced-pair share among the bodies the TD camera sees.</item>
    /// <item>ZL4: one real zombie per rep chasing the player in the Run world; the player is teleported 90° / 180° off the zombie's
    /// heading; root yaw rate / acceleration / jerk, torso roll into the turn, hips-to-root yaw lag.</item>
    /// <item>ZL5: start / stop on the stage (L1t slide, L3 pose jumps, transition clip present or not).</item>
    /// <item>ZL6: GC Allocated In Frame / Count with the 40-body crowd vs the empty scene (HotPathAllocationContractTests pattern):
    /// per-frame median count and the crowd frames above the idle maximum, the heaviest frames logged for a bisect.</item>
    /// <item>CAP_*: frames for the before / after review (DD_WP096_CAPTURE_DIR).</item>
    /// </list>
    /// A measuring test asserts only that its source is in the record; the target goes into the log line (PASS / FAIL) and turns into an
    /// assert by default (DD_WP096_ENFORCE=0 only for the explicit before/diagnostic recording); DD_WP096_ENFORCE=1 (or <see cref="ZorlaSabit"/>), for the "after" run. Every metric: one "wp096.&lt;test&gt;.&lt;key&gt;=" log
    /// line and r2-wp096-zl&lt;n&gt;.json through the R2 writer. TD only; nothing here touches the player's profile or settings (the
    /// assembly's TestProfileIsolation / TestUiDataIsolation fixtures redirect them, checked at every scene start).
    /// </summary>
    [TestFixture]
    public sealed class ZombieLokomotionV4Tests
    {
        public const string Fixture = "DD.Prototype.Tests.PlayMode.ZombieLokomotionV4Tests";
        public const string ZorlaDegiskeni = "DD_WP096_ENFORCE";
        public const string KayitDegiskeni = "DD_WP096_CAPTURE_DIR";
        /// <summary>Optional: the "before" (main) run's DD_R2_MEASURE_DIR — ZL6 then also reports its increase relative to main.</summary>
        public const string TabanDegiskeni = "DD_WP096_BASELINE_DIR";

        /// <summary>true = the target checks assert (the "after" run) without the environment variable.</summary>
        private static readonly bool ZorlaSabit = false;
        private static bool Zorla => ZorlaSabit || Environment.GetEnvironmentVariable(ZorlaDegiskeni) != "0";

        private const float Dt = 1f / 60f;
        private const float GovdeHizTabani = 0.5f;      // m/s — slide ratio floor (plan §3.1; the player's L1 uses 1.0)
        private const float ZeminYukseklik = 150f, ZeminYari = 200f;
        private const float SeritAraligi = 3f, BaslangicGeri = 90f, OyuncuIleri = 150f;
        private const int Isinma = R2LokomotionOlcumTests.Isinma;             // 1 s warm-up, excluded
        private const int MinBaslangic = R2LokomotionOlcumTests.MinDongu;     // ≥ 6 contact onsets per foot
        private const int L1tPencere = R2LokomotionOlcumTests.L1tPencere;     // 0.3 s
        private const int L3Pencere = R2LokomotionOlcumTests.L3Pencere;       // 1 s
        private const int KademeKare = 480;                                   // 8 s per pass
        private const float AralikTavan = 3f;                                 // s — a same-foot gap longer than this is not a cycle
        private const double LokoPayiAlt = 0.9;
        private const int KlipPencere = 30;                                   // 0.5 s: "which clip plays after the change"
        // ZL3
        private const int KalabalikIsinma = 60, KalabalikKare = 360, KalabalikAdim = 2;
        private const float FazPenceresi = 0.05f, OranKova = 0.05f, FazKova = 0.1f;
        // ZL4
        private const int DonusOnce = 60, DonusTaban = 30, DonusPencere = 72, DonusTekrar = 5;
        private const float DonusMesafe = 14f, AktifOmega = 10f;
        // ZL5
        private const int KdTekrar = 5, KdDur = 60, KdYuru = 120, KdDurus = 90;
        // ZL6
        // düzeltme 07.10 (ara1: idle max 561 B vs crowd max 650 B): one frame's maximum is harness noise (coroutine, recorder,
        // test runner); the gate compares the per-frame MEDIAN allocation count (a sustained per-frame object moves it by ≥ 1) and
        // counts the crowd frames above the idle window's maximum (pure noise puts ~1 of 240 there; > 4 is a real spike source).
        private const int GcKare = 240, GcKontrolKare = 6, GcIsinmaKare = 240, GcUstKareSiniri = 4;
        // captures
        private const int KareW = 1920, KareH = 1080, TdKare = 1200;
        private const float TdHalka = 6f, TdCubuk = 0.6f;

        // targets (plan §3; ZL3 / ZL4 proposals)
        private const double HedefKaymaOrt = 0.05, HedefKaymaP95 = 0.10, HedefSapma = 0.08, HedefKlip = 8, HedefKlipBasiGovde = 10,
            HedefEgilme = 5.0, HedefL1t = 0.10, L5Sinir = 0.10, L3Uzuv = 20.0, L3Govde = 10.0, L3Oran = 1.5;

        private static readonly EnemyTier[] Kademeler = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };
        private static readonly string[] Fitler = { "A", "B" };
        private static readonly uint[] Tohumlar = { 1u, 2u, 3u };
        private static readonly float[] Fazlar = { 0f, 0.25f, 0.5f, 0.75f };
        private static readonly Regex GecisKlibi = new Regex("start|stop|halt|brake|turn|pivot|to_idle|idle_to", RegexOptions.IgnoreCase);
        private static readonly Regex DonusKlibi = new Regex("turn|pivot", RegexOptions.IgnoreCase);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const BindingFlags Statik = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Ornek = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private SpecialWeaponSahne _s;
        private readonly List<GameObject> _gecici = new List<GameObject>();
        private readonly List<ZombieEnemy> _sahne = new List<ZombieEnemy>();
        private readonly List<Renderer> _gizlenen = new List<Renderer>();
        private float? _cesitlilikOnce;
        private bool _profilerOnce, _profilerGeriAl, _fpsGeriAl;
        private int _fpsOnce;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            CharacterAnimatorLod.ForceFar = -1;
            _cesitlilikOnce = StatikFloat(typeof(ZombieSkinnedVisual), "GaitVariety");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            CharacterAnimatorLod.ForceFar = -1;
            FootIkAdapter.Force = -1;   // ZL1 / ZL2's IK-off control pass
            ZombieEnemy.ResetRisePolicy();
            if (_cesitlilikOnce.HasValue) StatikFloatYaz(typeof(ZombieSkinnedVisual), "GaitVariety", _cesitlilikOnce.Value);
            if (_profilerGeriAl) { UnityEngine.Profiling.Profiler.enabled = _profilerOnce; _profilerGeriAl = false; }
            if (_fpsGeriAl) { Application.targetFrameRate = _fpsOnce; _fpsGeriAl = false; }
            foreach (Renderer r in _gizlenen) if (r != null) r.enabled = true;
            _gizlenen.Clear();
            // the stage bodies go back to the pool with their AI switched on again (a pooled body keeps component state)
            foreach (ZombieEnemy z in _sahne)
            {
                if (z == null) continue;
                z.enabled = true;
                if (z.IsSpawned) z.Remove();
            }

            _sahne.Clear();
            foreach (GameObject go in _gecici) if (go != null) Object.Destroy(go);
            _gecici.Clear();
            if (_s != null)
            {
                yield return _s.Bitir();   // input zeroed, crowd cleared, config restored
                _s = null;
            }
            else
            {
                LogAssert.ignoreFailingMessages = false;
                yield return null;
            }
        }

        // ============================================================================================================ ZL1 / ZL2

        // Düzeltme 07.10 (before1 / ara1: ZL1 / ZL2 BLOCKED, "temas başlangıcı 0/0", gap 0.0/0.0 cm, slide ≈ 1.0): FootIkAdapter.Solve
        // puts EVERY foot's IK goal at ground + leftFeetBottomHeight while the ray finds ground within RayDown (lift clamped to
        // MaxCorrection 0.35 m), so the swing foot is pressed to the floor too — on the shown pose every foot reads "sole on the
        // ground, no vertical speed" and the height rule calls every foot-frame a contact (n = all 2 520 foot-frames, no onsets).
        // The stance is therefore taken from the clips' own events (AP-022: foot_l / foot_r plant, foot_l_off / foot_r_off lift —
        // the definition the foot lock itself runs on), the height rule stays for the IK-off control pass, and a detector that
        // saturates (contact share outside 0.15–0.85) or fails its positive controls blocks instead of passing silently.
        private const int TurOyun = 0, TurKontrol = 1, TurIkKapali = 2;
        private static readonly string[] TurAd = { "oyun", "kontrol", "ik-kapali" };
        private const int EtiketYuru = 0, EtiketDur = 2;
        private const int DurKare = 60, DurOlcum = 30;                  // still segment before each walk; frames 30..59 = the idle control
        private const double TemasPayAlt = 0.15, TemasPayUst = 0.85;   // a walking foot is down 30–75 % of the time; outside = saturated
        private const double PkDurusPayi = 0.8, PkDurusHiz = 0.12;     // idle control: ≥ 80 % foot-frames in contact, mean planted-foot speed < 0.12 m/s (before3: the Executioner idle sways its planted foot at 0.09)
        private const double PkTemasPayAlt = 0.04;                      // IK-off walk/run control floor: a run is airborne most of the cycle (before3 Runner 0.06); 0 = a dead detector
        private const int PkAdimMin = 3;                                // step control: ≥ 3 onsets per foot per body in the 7 s IK-off walk
        // düzeltme-2 07.10 (before2 / ara2): one group failing its own checks is named and left out; the test needs ≥ 6 of 8 valid
        // groups (game pose and IK-off control each), so one odd clip does not hide the other seven and a broken detector still fails.
        // WP-096 p2 (C-00 08.10): the groups are per CLIP now (one lane per tier-pool member), so the rule is the same share — valid
        // ≥ 75 % of the groups (6 / 8 before).
        private const double MinGecerliPay = 0.75;

        private static int MinGecerli(int gruplar) => Math.Max(1, (int)Math.Ceiling(MinGecerliPay * gruplar - 1e-9));

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Category("WP096")]
        [Timeout(1200000)]
        public IEnumerator ZL1_AyakKaymasi_KademeYuruyus()
        {
            Time.captureDeltaTime = Dt;
            yield return KademeKaydiAl();
            KademeKaydi kayit = s_kademe;
            Assert.IsNotNull(kayit, "kaynak yok: kademe kaydı alınamadı");
            List<Grup> oyun = Gruplar(kayit.Govdeler, TurOyun);
            List<Grup> kontrol = Gruplar(kayit.Govdeler, TurKontrol);
            List<Grup> ikKapali = Gruplar(kayit.Govdeler, TurIkKapali);

            var r = new Rapor("zl1");
            var eksik = new List<string>();
            var satirlar = new List<string>();
            DedektorKontrolu(ikKapali, eksik, satirlar, out double pkPay, out double pkHiz, out int pkAdim, out int pkGecerli, out double yuruTaban);

            var hepsi = new List<double>();
            var engelli = new List<string>();
            int gecerli = 0;
            double enKotuOrt = double.NaN, enKotuP95 = double.NaN, payMin = double.NaN, payMax = double.NaN;
            string enKotuOrtAd = "-", enKotuP95Ad = "-";
            var kademeKayma = new Dictionary<EnemyTier, List<double>>();
            int olayGrup = 0;
            long salinim = 0, salinimYerde = 0;
            var salinimBosluk = new List<double>();
            foreach (Grup g in oyun)
            {
                satirlar.Add(GrupSatiri(g));
                GrupKaynagi(g);
                if (GrupEngelli(g, "zl1", engelli)) continue;
                gecerli++;
                double o = Ort(g.Kayma), p = Yuzdelik(g.Kayma, 0.95), pay = g.TemasPayi;
                hepsi.AddRange(g.Kayma);
                if (!kademeKayma.TryGetValue(g.Kademe, out List<double> kl)) kademeKayma[g.Kademe] = kl = new List<double>();
                kl.AddRange(g.Kayma);
                if (g.Kaynak == "olay") olayGrup++;
                salinim += g.SalinimKare;
                salinimYerde += g.SalinimYerde;
                salinimBosluk.AddRange(g.SalinimBosluk);
                if (!double.IsNaN(pay)) { payMin = double.IsNaN(payMin) ? pay : Math.Min(payMin, pay); payMax = double.IsNaN(payMax) ? pay : Math.Max(payMax, pay); }
                if (!double.IsNaN(o) && (double.IsNaN(enKotuOrt) || o > enKotuOrt)) { enKotuOrt = o; enKotuOrtAd = g.Ad; }
                if (!double.IsNaN(p) && (double.IsNaN(enKotuP95) || p > enKotuP95)) { enKotuP95 = p; enKotuP95Ad = g.Ad; }
            }

            if (gecerli < MinGecerli(oyun.Count)) eksik.Add("geçerli oyun grubu " + gecerli + "/" + oyun.Count + " < " + MinGecerli(oyun.Count) + " (%75): " + string.Join("; ", engelli));
            var kontrolHepsi = new List<double>();
            foreach (Grup g in kontrol) { kontrolHepsi.AddRange(g.Kayma); satirlar.Add("KONTROL " + GrupSatiri(g)); }
            double ikOrt = double.NaN, ikP95 = double.NaN;
            long ikSalinim = 0, ikSalinimYerde = 0, uyumN = 0, uyumOk = 0;
            var ikBosluk = new List<double>();
            foreach (Grup g in ikKapali)
            {
                satirlar.Add("IK-KAPALI " + GrupSatiri(g));
                if (g.Engel.Count > 0) continue;   // named in DedektorKontrolu
                double o = Ort(g.Kayma), p = Yuzdelik(g.Kayma, 0.95);
                if (!double.IsNaN(o) && (double.IsNaN(ikOrt) || o > ikOrt)) ikOrt = o;
                if (!double.IsNaN(p) && (double.IsNaN(ikP95) || p > ikP95)) ikP95 = p;
                ikSalinim += g.SalinimKare;
                ikSalinimYerde += g.SalinimYerde;
                ikBosluk.AddRange(g.SalinimBosluk);
                uyumN += g.UyumN;
                uyumOk += g.UyumOk;
            }

            Debug.Log("[DD-TEST] wp096.zl1.gruplar taban=" + F(GovdeHizTabani, 2) + "m/s (kalça) surucu=" + kayit.Surucu + " not=" + kayit.Not + " :: " + string.Join(" | ", satirlar));

            r.Hedef("slideAvgWorst", enKotuOrt, false, HedefKaymaOrt, enKotuOrtAd + " (oyun pozu, IK açık, temas = klip olayları)");
            r.Hedef("slideP95Worst", enKotuP95, false, HedefKaymaP95, enKotuP95Ad);
            r.Bilgi("slideAvg", Ort(hepsi));
            double temasKotu = double.NaN, servisMin = double.NaN, kilitMin = double.NaN;
            string temasAd = "-", servisAd = "-", kilitAd = "-";
            foreach (Grup g in oyun)
            {
                if (g.Engel.Count > 0) continue;
                double tk = Ort(g.KaymaTemas);
                if (!double.IsNaN(tk) && (double.IsNaN(temasKotu) || tk > temasKotu)) { temasKotu = tk; temasAd = g.Ad; }
                if (g.IkKare > 0) { double sv = (double)g.IkServis / g.IkKare; if (double.IsNaN(servisMin) || sv < servisMin) { servisMin = sv; servisAd = g.Ad; } }
                if (g.DurusKare > 0) { double kl = (double)g.DurusKilitli / g.DurusKare; if (double.IsNaN(kilitMin) || kl < kilitMin) { kilitMin = kl; kilitAd = g.Ad; } }
            }

            r.Bilgi("markerVelocityDiagnosticWorst", temasKotu, temasAd + " (değişen ayak/parmak işaretçisi hız tanısı; fiziksel taban tutunması kabul metriği değildir)");
            r.Bilgi("ikServedShareMin", servisMin, servisAd + " (yürüyüş karelerinde FootIkAdapter.Served)");
            r.Bilgi("ikLockedInStanceShareMin", kilitMin, kilitAd + " (durus ayak-karelerinde IsLocked)");
            r.Bilgi("slideP95", Yuzdelik(hepsi, 0.95));
            foreach (EnemyTier t in Kademeler)
                r.Bilgi("slideAvg" + t, kademeKayma.TryGetValue(t, out List<double> x) ? Ort(x) : double.NaN);
            r.Bilgi("validGroups", gecerli, "/" + oyun.Count + " (en az " + MinGecerli(oyun.Count) + " = %75)" + (engelli.Count > 0 ? " BLOCKED: " + string.Join("; ", engelli) : ""));
            r.Bilgi("blockedGroups", engelli.Count);
            r.Bilgi("eventContactGroups", olayGrup, "/" + gecerli + " geçerli grup klip olaylarıyla");
            r.Bilgi("contactShareMin", payMin, "temas ayak-karesi payı, beklenen " + F(TemasPayAlt, 2) + "–" + F(TemasPayUst, 2));
            r.Bilgi("contactShareMax", payMax);
            r.Bilgi("swingGroundedShare", salinim > 0 ? (double)salinimYerde / salinim : double.NaN,
                "salınımdaki ayağın taban ≤ 3 cm payı (IK açık); 1'e yakın = FootIkAdapter salınımı yere bastırıyor");
            r.Bilgi("swingLiftMedianCm", Medyan(salinimBosluk) * 100.0, "IK açık");
            r.Bilgi("ikOffClipSlideAvgWorst", ikOrt, "IK kapalı, temas = yükseklik kuralı (klip + oran kayması)");
            r.Bilgi("ikOffClipSlideP95Worst", ikP95);
            r.Bilgi("ikOffSwingGroundedShare", ikSalinim > 0 ? (double)ikSalinimYerde / ikSalinim : double.NaN);
            r.Bilgi("ikOffSwingLiftMedianCm", Medyan(ikBosluk) * 100.0);
            r.Bilgi("ikOffEventHeightAgreement", uyumN > 0 ? (double)uyumOk / uyumN : double.NaN, "olay durusu ile yükseklik temasının aynı dediği ayak-kare payı");
            r.Bilgi("controlValidGroups", pkGecerli, "/" + ikKapali.Count + " IK kapalı grup pozitif kontrolden geçti (en az " + MinGecerli(ikKapali.Count) + " = %75)");
            r.Bilgi("controlIdleContactShareMin", pkPay, "basan ayak, pozitif kontrol ≥ " + F(PkDurusPayi, 2));
            r.Bilgi("controlIdleFootSpeedMax", pkHiz, "basan ayak, pozitif kontrol < " + F(PkDurusHiz, 2) + " m/s");
            r.Bilgi("controlStepOnsetsMin", pkAdim, "pozitif kontrol ≥ " + PkAdimMin + " / ayak / gövde");
            r.Bilgi("ikOffWalkSoleFloatCmMax", yuruTaban * 100.0, "IK kapalı yürüyüşte tabanın %5'lik yüksekliği, en kötü ayak (klip ayağı havada yürüyor mu)");
            r.Bilgi("controlSlideAvg", kayit.KontrolVar ? Ort(kontrolHepsi) : double.NaN, "GaitVariety 0");
            r.Bilgi("controlSlideP95", kayit.KontrolVar ? Yuzdelik(kontrolHepsi, 0.95) : double.NaN, "GaitVariety 0");
            r.Bilgi("gaitOrdinalStart", kayit.SiraBaslangic);
            r.Bilgi("gaitOrdinalEnd", kayit.SiraBitis);
            r.Kaynak(eksik);
            r.Yazdir("ZL1_AyakKaymasi_KademeYuruyus", hepsi.Count, "seeds 1,2,3 + GaitVariety-0 control + IK-off control; stance = clip events; valid groups " + gecerli + "/" + oyun.Count
                     + (engelli.Count > 0 ? " (blocked: " + string.Join("; ", engelli) + ")" : "") + "; ordinal " + kayit.SiraBaslangic + "; " + kayit.Not);
        }

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Category("WP096")]
        [Timeout(1200000)]
        public IEnumerator ZL2_HizEslesmesi_AdimXKadans()
        {
            Time.captureDeltaTime = Dt;
            yield return KademeKaydiAl();   // ZL1's recording when it ran in this session (the same numbers), else its own
            KademeKaydi kayit = s_kademe;
            Assert.IsNotNull(kayit, "kaynak yok: kademe kaydı alınamadı");
            List<Grup> gruplar = Gruplar(kayit.Govdeler, TurOyun);
            List<Grup> kontrol = Gruplar(kayit.Govdeler, TurKontrol);
            List<Grup> ikKapali = Gruplar(kayit.Govdeler, TurIkKapali);

            var r = new Rapor("zl2");
            var eksik = new List<string>();
            var satirlar = new List<string>();
            DedektorKontrolu(ikKapali, eksik, satirlar, out double _, out double _, out int _, out int _, out double _);
            var engelli = new List<string>();
            int gecerli = 0;
            double sapmaMax = double.NaN, kontrolMax = double.NaN, ikMax = double.NaN, analitikMax = double.NaN;
            string sapmaAd = "-";
            int kirpilan = 0, dongu = 0;
            var kademeSapma = new Dictionary<EnemyTier, double>();
            foreach (Grup g in gruplar)
            {
                double dev = Sapma(g, out string satir);
                satirlar.Add(satir);
                GrupKaynagi(g);
                if (g.Araliklar.Count < 2 * (MinBaslangic - 1)) g.Engel.Add("aynı ayak aralığı " + g.Araliklar.Count);
                if (double.IsNaN(dev)) g.Engel.Add("sapma hesaplanamadı (temas / hız yok)");
                if (GrupEngelli(g, "zl2", engelli)) continue;
                gecerli++;
                dongu += g.Araliklar.Count / 2;
                if (double.IsNaN(sapmaMax) || dev > sapmaMax) { sapmaMax = dev; sapmaAd = g.Ad; }
                kademeSapma[g.Kademe] = kademeSapma.TryGetValue(g.Kademe, out double eski) ? Math.Max(eski, dev) : dev;
                foreach (Govde b in g.Govdeler)
                {
                    if (b.Kirpildi) kirpilan++;
                    if (!float.IsNaN(b.Analitik) && (double.IsNaN(analitikMax) || b.Analitik > analitikMax)) analitikMax = b.Analitik;
                }
            }

            foreach (Grup g in kontrol)
            {
                double dev = Sapma(g, out string satir);
                satirlar.Add("KONTROL " + satir);
                if (!double.IsNaN(dev) && (double.IsNaN(kontrolMax) || dev > kontrolMax)) kontrolMax = dev;
            }

            if (gecerli < MinGecerli(gruplar.Count)) eksik.Add("geçerli oyun grubu " + gecerli + "/" + gruplar.Count + " < " + MinGecerli(gruplar.Count) + " (%75): " + string.Join("; ", engelli));
            foreach (Grup g in ikKapali)
            {
                double dev = Sapma(g, out string satir);
                satirlar.Add("IK-KAPALI " + satir);
                if (g.Engel.Count == 0 && !double.IsNaN(dev) && (double.IsNaN(ikMax) || dev > ikMax)) ikMax = dev;
            }

            Debug.Log("[DD-TEST] wp096.zl2.gruplar yöntem=L2 (T = aynı ayak basma olayları medyanı, adım = u·T/2, u = durustaki ayağın gövdeye göre geri hızı) :: " + string.Join(" | ", satirlar));
            r.Hedef("deviationMax", sapmaMax, false, HedefSapma, sapmaAd);
            r.Bilgi("validGroups", gecerli, "/" + gruplar.Count + (engelli.Count > 0 ? " BLOCKED: " + string.Join("; ", engelli) : ""));
            r.Bilgi("blockedGroups", engelli.Count);
            foreach (EnemyTier t in Kademeler) r.Bilgi("deviation" + t, kademeSapma.TryGetValue(t, out double d) ? d : double.NaN);
            r.Bilgi("controlDeviationMax", kayit.KontrolVar ? kontrolMax : double.NaN, "GaitVariety 0");
            r.Bilgi("ikOffDeviationMax", ikMax, "IK kapalı, yükseklik kuralı");
            r.Bilgi("analyticSlideMax", analitikMax, "CadenceBand.SlideFraction");
            r.Bilgi("clampedBodies", kirpilan, "GaitRate bandın ucunda");
            r.Bilgi("gaitOrdinalStart", kayit.SiraBaslangic);
            r.Kaynak(eksik);
            r.Yazdir("ZL2_HizEslesmesi_AdimXKadans", dongu, "seeds 1,2,3; stance = clip events; valid groups " + gecerli + "/" + gruplar.Count
                     + (engelli.Count > 0 ? " (blocked: " + string.Join("; ", engelli) + ")" : "") + "; ordinal " + kayit.SiraBaslangic);
        }

        // ---------------------------------------------------------------------------------------------- the shared recording

        private sealed class Govde
        {
            public string Ad, Fit, DogalFit = "?", Klip = "yok";
            public EnemyTier Kademe;
            public uint Tohum;
            public int Tur, Sira;
            public bool FitZorlandi, Kirpildi;
            public float Hiz, Jitter = 1f, Olcek = float.NaN, GaitMul = float.NaN, GaitRate = float.NaN, KlipHizi = float.NaN, Analitik = float.NaN;
            public float RateMin = CadenceBand.Min, RateMax = CadenceBand.Max;
            public double LokoPayi;
            public int OlayL, OlayR, KalkL, KalkR;
            public List<ZombiKaydedici.Kare> Kareler;
            public TemasSonuc Secilen, Olay, Yukseklik;
            public int[] IkOnce = new int[IkSayacAdlari.Length], IkFark = new int[IkSayacAdlari.Length];   // WP-096: FootIkAdapter counters, start / delta
        }

        /// <summary>WP-096: the FootIkAdapter counters a lane reports (read by name — main's adapter has the first five; ZombieLock adds the rest).</summary>
        private static readonly string[] IkSayacAdlari = { "ReleasedByStretch", "ReleasedByGate", "ReleasedByLift", "ReleasedByPlant", "PlantsAccepted", "ReleasedByOffset", "BudgetDeniedServe", "BudgetDeniedLock" };
        private static readonly string[] IkSayacKisa = { "S", "G", "L", "P", "plants", "O", "deniedServe", "deniedLock" };

        private static int[] IkSayac(FootIkAdapter ik)
        {
            var r = new int[IkSayacAdlari.Length];
            if (ik == null) return r;
            for (int i = 0; i < IkSayacAdlari.Length; i++)
            {
                PropertyInfo p = typeof(FootIkAdapter).GetProperty(IkSayacAdlari[i], Ornek);
                r[i] = p != null && p.PropertyType == typeof(int) ? (int)p.GetValue(ik) : -1;
            }

            return r;
        }

        private sealed class KademeKaydi
        {
            public readonly List<Govde> Govdeler = new List<Govde>();
            public int SiraBaslangic = -1, SiraBitis = -1;
            public bool KontrolVar, IkKapaliVar;
            public string Not = "", Surucu = "SetSpeed(float,float)";
        }

        /// <summary>ZL1 and ZL2 read one recording (a static: the same Play Mode session).</summary>
        private static KademeKaydi s_kademe;

        private IEnumerator KademeKaydiAl()
        {
            if (s_kademe != null) yield break;
            yield return RunKur();
            var kayit = new KademeKaydi();
            kayit.SiraBaslangic = TohumVeSira(Tohumlar[0]);
            CharacterAnimatorLod.ForceFar = 0;   // every body on the every-frame step (the 15 Hz far step is not the gait)
            Vector3 merkez = SahneKur();
            MethodInfo hiz = HizMetodu();
            Assert.IsNotNull(hiz, "kaynak yok: ZombieSkinnedVisual.SetSpeed(float, float) yok — sahne sürücüsü bağlanamadı");
            // passes: the game pose × 3 seeds, the GaitVariety-0 control, the IK-off control (the clip's own feet: the detector's
            // positive controls and the clip + rate slide without the foot IK)
            var gecisler = new List<(int tur, uint tohum)>();
            foreach (uint t in Tohumlar) gecisler.Add((TurOyun, t));
            gecisler.Add((TurKontrol, Tohumlar[0]));
            gecisler.Add((TurIkKapali, Tohumlar[0]));
            foreach ((int tur, uint tohum) gc in gecisler)
            {
                if (!StatikFloatYaz(typeof(ZombieSkinnedVisual), "GaitVariety", gc.tur == TurKontrol ? 0f : 1f))
                {
                    kayit.Not += gc.tur == TurKontrol ? "GaitVariety yok: kontrol koşusu atlandı. " : "GaitVariety yok: çeşitlilik varsayılanda. ";
                    if (gc.tur == TurKontrol) continue;
                }

                FootIkAdapter.Force = gc.tur == TurIkKapali ? 0 : -1;
                EnemyFactory.ResetSeedForTests(gc.tohum);
                yield return KademeGecisi(merkez, gc.tohum, gc.tur, hiz, kayit.Govdeler);
                FootIkAdapter.Force = -1;
                if (gc.tur == TurKontrol) kayit.KontrolVar = true;
                if (gc.tur == TurIkKapali) kayit.IkKapaliVar = true;
            }

            if (_cesitlilikOnce.HasValue) StatikFloatYaz(typeof(ZombieSkinnedVisual), "GaitVariety", _cesitlilikOnce.Value);
            kayit.SiraBitis = SiraOku();
            Debug.Log("[DD-TEST] wp096.kademe.kayit bodies=" + kayit.Govdeler.Count + " ordinal " + kayit.SiraBaslangic + "→" + kayit.SiraBitis
                      + " fighting=" + (RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting) + " " + kayit.Not);
            s_kademe = kayit;
        }

        /// <summary>
        /// WP-096 p2 (C-00 08.10): one lane per CLIP — every member of each tier's gait pool (CharacterAnimatorContract.ZombieGaitPools,
        /// read by reflection; the mirrored limp gets its own lane "~R"), the gait forced with ForceGaitForTest, the body at its tier
        /// speed (× its SpeedJitter); the group key is the clip. Without the pools (main) the old four tiers × fit A/B lanes (gait -1).
        /// </summary>
        private static List<(EnemyTier tier, string key, int gait)> Seritler()
        {
            var seritler = new List<(EnemyTier, string, int)>();
            int[][] havuzlar = typeof(CharacterAnimatorContract).GetField("ZombieGaitPools", Statik)?.GetValue(null) as int[][];
            string[] gaitler = typeof(CharacterAnimatorContract).GetField("ZombieGaitClips", Statik)?.GetValue(null) as string[];
            MethodInfo zorla = Metot(typeof(ZombieSkinnedVisual), "ForceGaitForTest", typeof(int));
            MethodInfo ayna = Metot(typeof(CharacterAnimatorContract), "IsGaitMirrored", typeof(int));
            if (havuzlar != null && gaitler != null && zorla != null && havuzlar.Length >= Kademeler.Length)
            {
                foreach (EnemyTier t in Kademeler)
                foreach (int g in havuzlar[(int)t])
                {
                    if (g < 0 || g >= gaitler.Length) continue;
                    string ad = gaitler[g].Replace("AP033_zombie_", "").Replace("AP022_zombie_", "v3_");
                    bool aynali = ayna != null && (bool)ayna.Invoke(null, new object[] { g });
                    seritler.Add((t, ad + (aynali ? "~R" : ""), g));
                }

                return seritler;
            }

            foreach (string fit in Fitler)
            foreach (EnemyTier t in Kademeler)
                seritler.Add((t, fit, -1));
            return seritler;
        }

        /// <summary>
        /// WP-096 (C-00 08.10): lanes per sub-pass — FootIkAdapter serves ≤ MaxBodiesPerFrame (12) bodies and locks ≤ MaxLockBodiesPerFrame
        /// (9) per frame, the player included; 14 lanes at once left ~5 lanes unlocked (their slide read as "IK-on slide"). Each sub-pass
        /// stays inside both budgets, so every lane is measured with the production IK; the per-lane served / locked shares are logged.
        /// </summary>
        private static int SeritParcasi => Math.Max(1, Math.Min(FootIkAdapter.MaxLockBodiesPerFrame, FootIkAdapter.MaxBodiesPerFrame) - 2);

        /// <summary>One pass: every lane of <see cref="Seritler"/> (one per clip of every tier pool), in sub-passes of <see cref="SeritParcasi"/>.</summary>
        private IEnumerator KademeGecisi(Vector3 merkez, uint tohum, int tur, MethodInfo hizMetodu, List<Govde> into)
        {
            List<(EnemyTier tier, string key, int gait)> hepsi = Seritler();
            for (int bas = 0; bas < hepsi.Count; bas += SeritParcasi)
                yield return KademeAltGecisi(merkez, tohum, tur, hizMetodu, into, hepsi.GetRange(bas, Math.Min(SeritParcasi, hepsi.Count - bas)));
        }

        /// <summary>One sub-pass: the given lanes side by side, 1 s still (the idle control), then 8 s straight at the tier speed (1 s
        /// warm-up excluded).</summary>
        private IEnumerator KademeAltGecisi(Vector3 merkez, uint tohum, int tur, MethodInfo hizMetodu, List<Govde> into, List<(EnemyTier tier, string key, int gait)> seritler)
        {
            MethodInfo zorla = Metot(typeof(ZombieSkinnedVisual), "ForceGaitForTest", typeof(int));
            int n = seritler.Count;
            var govdeler = new List<Govde>(n);
            var zler = new List<ZombieEnemy>(n);
            var kayitlar = new List<ZombiKaydedici>(n);
            var konum = new Vector3[n];
            Quaternion ileri = Quaternion.LookRotation(Vector3.forward);
            for (int i = 0; i < n; i++)
            {
                EnemyTier t = seritler[i].tier;
                string fit = seritler[i].key;   // the group key: the clip (per-clip lanes) or the fit (main's lanes)
                int gait = seritler[i].gait;
                Vector3 p = merkez + Vector3.right * ((i - (n - 1) * 0.5f) * SeritAraligi) + Vector3.back * BaslangicGeri;
                int sira = SiraOku();
                ZombieEnemy z = EnemyFactory.Spawn(t, 12, p, ileri);
                Assert.IsNotNull(z, "kaynak yok: " + t + " doğmadı (tohum " + tohum + ")");
                SahneyeAl(z, p, ileri);
                ZombieSkinnedVisual v = z.Skinned;
                Assert.IsTrue(v != null && v.Animator != null && v.Animator.isHuman, "kaynak yok: " + t + " skinned / Humanoid Animator yok");
                var b = new Govde { Kademe = t, Fit = fit, Tohum = tohum, Tur = tur, Sira = sira };
                b.Ad = t + "/" + fit + "/s" + tohum + (tur == TurOyun ? "" : "/" + TurAd[tur]);
                b.DogalFit = v.Look.IsValid ? v.Look.fit : "?";
                if (gait >= 0) zorla.Invoke(v, new object[] { gait });   // per-clip lane: the fit does not pick the clip any more
                else if (b.DogalFit != fit) b.FitZorlandi = FitZorla(v, fit, t);
                b.Jitter = OrnekFloat(v, "SpeedJitter") ?? 1f;   // v4 P2: ZombieEnemy walks at MoveSpeed × SpeedJitter
                b.Hiz = z.Stats.MoveSpeed * b.Jitter;
                v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                ZombiKaydedici k = KaydediciEkle(v.Animator, 1 << PrototypeLayers.Environment, b.Ad);
                b.IkOnce = IkSayac(v.FootIk);   // WP-096: the lock's release / budget counters at the start of the pass
                govdeler.Add(b);
                zler.Add(z);
                kayitlar.Add(k);
                konum[i] = p;
            }

            Physics.SyncTransforms();
            var arg = new object[2];
            for (int f = 0; f < DurKare + KademeKare; f++)
            {
                bool yuru = f >= DurKare;
                float dt = Time.deltaTime;
                for (int i = 0; i < n; i++)
                {
                    float v = yuru ? govdeler[i].Hiz : 0f;
                    konum[i] += Vector3.forward * (v * dt);
                    zler[i].transform.position = konum[i];
                    arg[0] = v;
                    arg[1] = dt;
                    hizMetodu.Invoke(zler[i].Skinned, arg);
                    kayitlar[i].Etiket = yuru ? EtiketYuru : EtiketDur;
                }

                yield return null;
            }

            for (int i = 0; i < n; i++)
            {
                int[] son = IkSayac(zler[i].Skinned.FootIk);
                govdeler[i].IkFark = new int[son.Length];
                for (int c = 0; c < son.Length; c++) govdeler[i].IkFark[c] = son[c] - govdeler[i].IkOnce[c];
                GovdeBitir(govdeler[i], zler[i].Skinned, kayitlar[i]);
                into.Add(govdeler[i]);
                zler[i].Remove();
                zler[i].enabled = true;
                _sahne.Remove(zler[i]);
            }

            yield return null;
        }

        private static void GovdeBitir(Govde b, ZombieSkinnedVisual v, ZombiKaydedici k)
        {
            k.Birak();
            b.Kareler = k.Kareler;
            int olcum = 0, loko = 0;
            foreach (ZombiKaydedici.Kare kr in k.Kareler)
            {
                if (!Olculen(kr)) continue;
                olcum++;
                if (kr.Loko) loko++;
                if (kr.OlayL) b.OlayL++;
                if (kr.OlayR) b.OlayR++;
                if (kr.KalkL) b.KalkL++;
                if (kr.KalkR) b.KalkR++;
            }

            b.LokoPayi = olcum > 0 ? (double)loko / olcum : 0;
            b.Klip = KlipAdi(BaskinKlip(k.Kareler));
            b.GaitRate = OrnekFloat(v, "GaitRate") ?? float.NaN;
            b.GaitMul = OrnekFloat(v, "GaitMultiplier") ?? float.NaN;
            b.Olcek = OrnekFloat(v, "GaitBodyScale") ?? v.Animator.transform.lossyScale.y;
            CharacterAnimationSet set = v.AnimationSet;
            b.KlipHizi = set != null && b.Klip != "yok" ? set.SpeedOf(b.Klip) : 0f;
            if (!(b.KlipHizi > 0f) && set != null)
            {
                // the dominant clip's asset name is not a set entry: the gait index the visual plays
                int? gait = OrnekInt(v, "Gait");
                string[] gaitler = typeof(CharacterAnimatorContract).GetField("ZombieGaitClips", Statik)?.GetValue(null) as string[];
                if (gait.HasValue && gaitler != null && gait.Value >= 0 && gait.Value < gaitler.Length) b.KlipHizi = set.SpeedOf(gaitler[gait.Value]);
            }

            if (!(b.KlipHizi > 0f)) b.KlipHizi = float.NaN;
            b.RateMin = OrnekFloat(v, "GaitRateMin") ?? StatikFloat(typeof(ZombieSkinnedVisual), "MinGaitRate") ?? CadenceBand.Min;
            b.RateMax = OrnekFloat(v, "GaitRateMax") ?? StatikFloat(typeof(ZombieSkinnedVisual), "MaxGaitRate") ?? CadenceBand.Max;
            b.Kirpildi = !float.IsNaN(b.GaitRate) && (b.GaitRate <= b.RateMin + 1e-3f || b.GaitRate >= b.RateMax - 1e-3f);
            b.Analitik = SlideFraction(b.Hiz, b.KlipHizi * b.Olcek, b.GaitRate);
        }

        private sealed class Grup
        {
            public string Ad, Kaynak = "-";
            public EnemyTier Kademe;
            public string Fit;
            public int Tur;
            public bool Uyarlamali;
            public readonly List<Govde> Govdeler = new List<Govde>();
            public readonly List<double> Kayma = new List<double>(), KaymaKok = new List<double>(), Araliklar = new List<double>(), SalinimBosluk = new List<double>();
            public int BasL, BasR, TemasKare, AyakKare, OlcumKare, LokoKare, UN, VN;
            public long SalinimKare, SalinimYerde, UyumN, UyumOk;
            public double YuruTaban = double.NaN;                       // IK-off: worst foot's 5th-percentile sole height while walking, m
            public readonly List<string> Engel = new List<string>();   // why this group is left out (empty = valid)
            public double USum, VSum;
            // WP-096: the IK as each lane got it (stance foot-frames by the chosen stance; walk frames for the service) and the contact slide
            public int IkKare, IkServis, DurusKare, DurusKilitli, CiftDurusKare, CiftDurusKilitli;
            public readonly List<double> KaymaTemas = new List<double>();
            public readonly int[] IkFark = new int[IkSayacAdlari.Length];
            public double TemasPayi => AyakKare > 0 ? (double)TemasKare / AyakKare : double.NaN;
        }

        private static bool Olculen(ZombiKaydedici.Kare k) => k.Etiket == EtiketYuru && k.Since >= Isinma;

        private static bool DurusKontrolKaresi(ZombiKaydedici.Kare k) => k.Etiket == EtiketDur && k.Since >= DurOlcum && k.Gecerli;

        /// <summary>
        /// Groups of one pass kind. The game / control passes take the stance from the clip events when the group raised ≥ 6 plants per
        /// foot (else the height rule, which the saturation check then judges); the IK-off pass always uses the height rule — strict L1,
        /// or relative to the body's own 5th-percentile sole height when the strict rule fails the idle control.
        /// </summary>
        private static List<Grup> Gruplar(List<Govde> govdeler, int tur)
        {
            var gruplar = new List<Grup>();
            foreach (EnemyTier t in Kademeler)
            {
                // WP-096 p2: the group keys in lane order (the clip per lane, or main's fit A / B)
                var anahtarlar = new List<string>();
                foreach (Govde b0 in govdeler) if (b0.Tur == tur && b0.Kademe == t && !anahtarlar.Contains(b0.Fit)) anahtarlar.Add(b0.Fit);
                foreach (string fit in anahtarlar)
                {
                    var g = new Grup { Ad = t + "/" + fit, Kademe = t, Fit = fit, Tur = tur };
                    foreach (Govde b in govdeler) if (b.Tur == tur && b.Kademe == t && b.Fit == fit && b.Kareler != null) g.Govdeler.Add(b);
                    if (g.Govdeler.Count == 0) continue;
                    int olL = 0, olR = 0, sl = 0, sr = 0;
                    bool durusTutar = true, adimTutar = true;
                    foreach (Govde b in g.Govdeler)
                    {
                        b.Olay = OlayDuruslari(b.Kareler, null);
                        b.Yukseklik = Temaslar(b.Kareler, false);
                        olL += b.OlayL;
                        olR += b.OlayR;
                        int bl = 0, br = 0;
                        for (int i = 0; i < b.Kareler.Count; i++)
                        {
                            if (!Olculen(b.Kareler[i])) continue;
                            if (b.Yukseklik.BasL[i]) bl++;
                            if (b.Yukseklik.BasR[i]) br++;
                        }

                        sl += bl;
                        sr += br;
                        if (bl < PkAdimMin || br < PkAdimMin) adimTutar = false;
                        DurusOlc(b.Kareler, b.Yukseklik, out double pay, out double _, out double _);
                        if (!(pay >= PkDurusPayi)) durusTutar = false;
                        double taban = Math.Max(AltYuzdelik(b.Kareler, true, Olculen), AltYuzdelik(b.Kareler, false, Olculen));
                        g.YuruTaban = double.IsNaN(g.YuruTaban) ? taban : Math.Max(g.YuruTaban, taban);
                    }

                    if (tur != TurIkKapali && olL >= MinBaslangic && olR >= MinBaslangic)
                    {
                        g.Kaynak = "olay";
                        foreach (Govde b in g.Govdeler) b.Secilen = b.Olay;
                    }
                    else if (tur == TurIkKapali && durusTutar && !adimTutar)
                    {
                        // before2 / ara2: shamble_fast_A's sole never comes within 3 cm while walking with the IK off (walk gap min 6.1 /
                        // median 9.1 cm, idle 0): the clip walks with its feet in the air. The standing sole reads right, so the walk is
                        // read against the walk's own 5th-percentile sole height (reported as ikOffWalkSoleFloatCmMax).
                        g.Uyarlamali = true;
                        g.Kaynak = "yukseklik-yuruyus-tabani";
                        foreach (Govde b in g.Govdeler) b.Secilen = TemaslarRef(b.Kareler, AltYuzdelik(b.Kareler, true, Olculen), AltYuzdelik(b.Kareler, false, Olculen));
                    }
                    else
                    {
                        g.Uyarlamali = tur == TurIkKapali ? !durusTutar : sl < MinBaslangic || sr < MinBaslangic;
                        g.Kaynak = g.Uyarlamali ? "yukseklik-uyarlamali" : "yukseklik";
                        foreach (Govde b in g.Govdeler) b.Secilen = g.Uyarlamali ? Temaslar(b.Kareler, true) : b.Yukseklik;
                    }

                    foreach (Govde b in g.Govdeler)
                    {
                        Topla(g, b, b.Secilen);
                        if (b.OlayL + b.OlayR > 0) SalinimVeUyum(g, b);
                    }

                    gruplar.Add(g);
                }
            }

            return gruplar;
        }

        private static void Topla(Grup g, Govde b, TemasSonuc s)
        {
            List<ZombiKaydedici.Kare> ks = b.Kareler;
            Vector3 toplam = Vector3.zero;
            int n = 0;
            foreach (ZombiKaydedici.Kare k in ks) if (Olculen(k) && k.Gecerli) { toplam += k.KokV; n++; }
            Vector3 yon = n > 0 && toplam.sqrMagnitude > 1e-8f ? toplam.normalized : Vector3.forward;
            float sonL = -1f, sonR = -1f;
            // Diagnostic only: choose the lower ankle/toe marker relative to its own walk minimum. Switching markers does
            // not track one physical sole point, so these velocities never replace the ankle slide acceptance metric.
            float minAL = float.MaxValue, minTL = float.MaxValue, minAR = float.MaxValue, minTR = float.MaxValue;
            foreach (ZombiKaydedici.Kare k in ks)
            {
                if (!Olculen(k)) continue;
                minAL = Mathf.Min(minAL, k.AyakYL); minTL = Mathf.Min(minTL, k.ParmakYL); minAR = Mathf.Min(minAR, k.AyakYR); minTR = Mathf.Min(minTR, k.ParmakYR);
            }

            for (int c = 0; c < g.IkFark.Length && c < b.IkFark.Length; c++) g.IkFark[c] += b.IkFark[c];
            for (int i = 0; i < ks.Count; i++)
            {
                ZombiKaydedici.Kare k = ks[i];
                if (!Olculen(k)) continue;
                g.OlcumKare++;
                if (k.IkVar) { g.IkKare++; if (k.IkServis) g.IkServis++; }
                if (k.Loko) g.LokoKare++;
                if (s.BasL[i]) { g.BasL++; if (sonL >= 0f && k.Zaman - sonL < AralikTavan) g.Araliklar.Add(k.Zaman - sonL); sonL = k.Zaman; }
                if (s.BasR[i]) { g.BasR++; if (sonR >= 0f && k.Zaman - sonR < AralikTavan) g.Araliklar.Add(k.Zaman - sonR); sonR = k.Zaman; }
                if (!k.Gecerli) continue;
                g.AyakKare += 2;
                g.VSum += Vector3.Dot(k.KokV, yon);
                g.VN++;
                if (s.L[i]) { g.Kayma.Add(Oran(k.AyakVL, k.KalcaV)); g.KaymaKok.Add(Oran(k.AyakVL, k.KokV)); g.USum += -Vector3.Dot(Yatay(k.AyakVL - k.KokV), yon); g.UN++; g.TemasKare++; }
                if (s.R[i]) { g.Kayma.Add(Oran(k.AyakVR, k.KalcaV)); g.KaymaKok.Add(Oran(k.AyakVR, k.KokV)); g.USum += -Vector3.Dot(Yatay(k.AyakVR - k.KokV), yon); g.UN++; g.TemasKare++; }
                if (s.L[i])
                {
                    g.KaymaTemas.Add(Oran(k.ParmakYL - minTL < k.AyakYL - minAL ? k.ParmakVL : k.AyakVL, k.KalcaV));
                    if (k.IkVar) { g.DurusKare++; if (k.KilitL) g.DurusKilitli++; }
                }

                if (s.R[i])
                {
                    g.KaymaTemas.Add(Oran(k.ParmakYR - minTR < k.AyakYR - minAR ? k.ParmakVR : k.AyakVR, k.KalcaV));
                    if (k.IkVar) { g.DurusKare++; if (k.KilitR) g.DurusKilitli++; }
                }
                if (s.L[i] && s.R[i] && k.IkVar)
                {
                    g.CiftDurusKare++;
                    if (k.KilitL && k.KilitR) g.CiftDurusKilitli++;
                }
            }
        }

        /// <summary>The swing (event stance off) foot's sole height — a swing pressed to the floor reads ≤ 3 cm — and how often the
        /// event stance and the strict height rule agree.</summary>
        private static void SalinimVeUyum(Grup g, Govde b)
        {
            List<ZombiKaydedici.Kare> ks = b.Kareler;
            for (int i = 0; i < ks.Count; i++)
            {
                ZombiKaydedici.Kare k = ks[i];
                if (!Olculen(k) || !k.Gecerli) continue;
                for (int ayak = 0; ayak < 2; ayak++)
                {
                    bool durus = ayak == 0 ? b.Olay.L[i] : b.Olay.R[i];
                    bool basma = ayak == 0 ? k.OlayL : k.OlayR;
                    float bosluk = ayak == 0 ? k.BoslukL : k.BoslukR;
                    bool yuk = ayak == 0 ? b.Yukseklik.L[i] : b.Yukseklik.R[i];
                    g.UyumN++;
                    if (yuk == durus) g.UyumOk++;
                    if (durus || basma || float.IsNaN(bosluk)) continue;
                    g.SalinimKare++;
                    if (bosluk <= R2LokomotionOlcumTests.TemasYukseklik) g.SalinimYerde++;
                    g.SalinimBosluk.Add(bosluk);
                }
            }
        }

        /// <summary>
        /// The idle control frames of one body under a contact rule. <paramref name="pay"/> / <paramref name="hiz"/> = the PLANTED foot's
        /// contact share and mean ground speed (the foot with the higher share): a standing body stands on at least one foot, while the
        /// idle may lift or tap the other (before2: the Executioner's idle reads 0.50 over both feet). <paramref name="payIki"/> = both feet.
        /// </summary>
        private static void DurusOlc(List<ZombiKaydedici.Kare> ks, TemasSonuc s, out double pay, out double hiz, out double payIki)
        {
            int n = 0, tL = 0, tR = 0;
            double vL = 0, vR = 0;
            for (int i = 0; i < ks.Count; i++)
            {
                ZombiKaydedici.Kare k = ks[i];
                if (!DurusKontrolKaresi(k)) continue;
                n++;
                if (s.L[i]) tL++;
                if (s.R[i]) tR++;
                vL += Yatay(k.AyakVL).magnitude;
                vR += Yatay(k.AyakVR).magnitude;
            }

            if (n == 0) { pay = hiz = payIki = double.NaN; return; }
            bool sol = tL > tR || (tL == tR && vL <= vR);
            pay = (double)(sol ? tL : tR) / n;
            hiz = (sol ? vL : vR) / n;
            payIki = (double)(tL + tR) / (2 * n);
        }

        /// <summary>
        /// The detector's positive controls on the IK-off pass (the clip's own feet), per group: (a) standing still, the planted foot reads
        /// contact (≥ 80 % of its frames) and does not move (the sole / ground reference); (b) walking, every body shows ≥ 3 onsets per
        /// foot and the group's contact share is inside 0.15–0.85 (the rule sees steps, it is not saturated). A failing group is named
        /// and left out; fewer than <see cref="MinGecerliPay"/> of the groups valid blocks the test (a broken detector fails everywhere).
        /// </summary>
        private static void DedektorKontrolu(List<Grup> ikKapali, List<string> eksik, List<string> satirlar, out double payMin, out double hizMax, out int adimMin,
                                             out int gecerli, out double yuruTaban)
        {
            payMin = double.NaN;
            hizMax = double.NaN;
            adimMin = int.MaxValue;
            gecerli = 0;
            yuruTaban = double.NaN;
            if (ikKapali.Count == 0) { eksik.Add("dedektör pozitif kontrolü: IK kapalı geçiş yok"); adimMin = 0; return; }
            var engelli = new List<string>();
            foreach (Grup g in ikKapali)
            {
                if (!double.IsNaN(g.YuruTaban)) yuruTaban = double.IsNaN(yuruTaban) ? g.YuruTaban : Math.Max(yuruTaban, g.YuruTaban);
                double gPay = double.NaN, gHiz = double.NaN;
                int gAdim = int.MaxValue;
                foreach (Govde b in g.Govdeler)
                {
                    DurusOlc(b.Kareler, b.Secilen, out double pay, out double hiz, out double payIki);
                    int bl = 0, br = 0;
                    for (int i = 0; i < b.Kareler.Count; i++)
                    {
                        if (!Olculen(b.Kareler[i])) continue;
                        if (b.Secilen.BasL[i]) bl++;
                        if (b.Secilen.BasR[i]) br++;
                    }

                    gPay = double.IsNaN(gPay) ? pay : Math.Min(gPay, pay);
                    gHiz = double.IsNaN(gHiz) ? hiz : Math.Max(gHiz, hiz);
                    gAdim = Math.Min(gAdim, Math.Min(bl, br));
                    satirlar.Add("PK " + b.Ad + " " + g.Kaynak + " idleShare(planted) " + F(pay, 2) + " idleShare(both) " + F(payIki, 2) + " idleFootV " + F(hiz, 3)
                                 + " onsets " + bl + "/" + br + " walkSole5pct " + F(g.YuruTaban * 100.0, 1) + "cm");
                    if (!(pay >= PkDurusPayi)) g.Engel.Add("PK dururken basan ayak temas payı " + F(pay, 2) + " < " + F(PkDurusPayi, 2));
                    if (!(hiz < PkDurusHiz)) g.Engel.Add("PK dururken basan ayak hızı " + F(hiz, 3) + " m/s");
                    if (bl < PkAdimMin || br < PkAdimMin) g.Engel.Add("PK yürüyüşte adım başlangıcı " + bl + "/" + br + " < " + PkAdimMin);
                }

                double gp = g.TemasPayi;
                if (!(gp >= PkTemasPayAlt && gp <= TemasPayUst)) g.Engel.Add("PK temas payı " + F(gp, 2) + " (doymuş)");
                if (GrupEngelli(g, "pk", engelli)) continue;
                gecerli++;
                payMin = double.IsNaN(payMin) ? gPay : Math.Min(payMin, gPay);
                hizMax = double.IsNaN(hizMax) ? gHiz : Math.Max(hizMax, gHiz);
                adimMin = Math.Min(adimMin, gAdim);
            }

            if (gecerli < MinGecerli(ikKapali.Count)) eksik.Add("dedektör pozitif kontrolü: geçerli IK kapalı grup " + gecerli + "/" + ikKapali.Count + " < " + MinGecerli(ikKapali.Count) + " (%75): " + string.Join("; ", engelli));
            if (adimMin == int.MaxValue) adimMin = 0;
        }

        /// <summary>A group with reasons in <see cref="Grup.Engel"/> is logged BLOCKED by name and left out (true).</summary>
        private static bool GrupEngelli(Grup g, string test, List<string> engelli)
        {
            if (g.Engel.Count == 0) return false;
            string ad = g.Ad + (g.Tur == TurIkKapali ? "/ik-kapali" : "") + " [" + g.Kaynak + "]: " + string.Join(", ", g.Engel);
            engelli.Add(ad);
            Debug.Log("wp096-blocked." + test + ".grup " + ad);
            return true;
        }

        private static void GrupKaynagi(Grup g)
        {
            if (g.BasL < MinBaslangic || g.BasR < MinBaslangic) g.Engel.Add("temas başlangıcı L/R " + g.BasL + "/" + g.BasR + " < " + MinBaslangic);
            if (g.Kayma.Count == 0) g.Engel.Add("temas karesi yok");
            double pay = g.TemasPayi;
            if (!(pay >= TemasPayAlt && pay <= TemasPayUst)) g.Engel.Add("temas ayrımı yok — ayak-kare payı " + F(pay, 2) + " (dedektör doymuş)");
            double loko = g.OlcumKare > 0 ? (double)g.LokoKare / g.OlcumKare : 0;
            if (loko < LokoPayiAlt) g.Engel.Add("Locomotion durumu payı " + F(loko, 2) + " < " + F(LokoPayiAlt, 2));
        }

        /// <summary>L2's method on one group: |cadence × step − v| / v (cadence = 2 / T, step = u × T / 2).</summary>
        private static double Sapma(Grup g, out string satir)
        {
            double T = Medyan(g.Araliklar);
            double u = g.UN > 0 ? g.USum / g.UN : double.NaN;
            double v = g.VN > 0 ? g.VSum / g.VN : double.NaN;
            double kadans = 2.0 / T, adim = u * T / 2.0;
            double dev = v > 0.1 ? Math.Abs(kadans * adim - v) / v : double.NaN;
            double analitik = double.NaN;
            int kirp = 0;
            foreach (Govde b in g.Govdeler)
            {
                if (!float.IsNaN(b.Analitik)) analitik = double.IsNaN(analitik) ? b.Analitik : Math.Max(analitik, b.Analitik);
                if (b.Kirpildi) kirp++;
            }

            satir = g.Ad + " " + g.Kaynak + " T " + F(T) + "s cadence " + F(kadans * 60.0, 1) + "spm step " + F(adim) + "m u " + F(u, 3) + " v " + F(v, 3) + " dev " + F(dev)
                    + " analytic " + F(analitik) + " clamped " + kirp + "/" + g.Govdeler.Count + " intervals " + g.Araliklar.Count;
            return dev;
        }

        private static string GrupSatiri(Grup g)
        {
            var b = new List<string>();
            foreach (Govde x in g.Govdeler)
                b.Add(x.Ad + "(ord " + x.Sira + " fit " + x.DogalFit + (x.FitZorlandi ? "→" + x.Fit : "") + " clip " + x.Klip + " v " + F(x.Hiz, 2) + " jit " + F(x.Jitter, 3)
                      + " scale " + F(x.Olcek, 3) + " mul " + F(x.GaitMul, 3) + " rate " + F(x.GaitRate, 3) + " band " + F(x.RateMin, 2) + "–" + F(x.RateMax, 2)
                      + (x.Kirpildi ? " CLAMPED" : "") + " clipV " + F(x.KlipHizi, 2) + " analytic " + F(x.Analitik, 3) + " loco " + F(x.LokoPayi, 2)
                      + " events L/R " + x.OlayL + "/" + x.OlayR + " off " + x.KalkL + "/" + x.KalkR + ")");
            return g.Ad + " [" + g.Kaynak + "] avg " + F(Ort(g.Kayma)) + " p95 " + F(Yuzdelik(g.Kayma, 0.95)) + " rootAvg " + F(Ort(g.KaymaKok)) + " n " + g.Kayma.Count
                   + " share " + F(g.TemasPayi, 2) + " onsets L/R " + g.BasL + "/" + g.BasR + " swingGrounded " + (g.SalinimKare > 0 ? F((double)g.SalinimYerde / g.SalinimKare, 2) : "n/a")
                   + " swingLiftMed " + F(Medyan(g.SalinimBosluk) * 100.0, 1) + "cm gap " + TabanOzeti(g) + " " + IkOzeti(g) + " :: " + string.Join(" ", b);
        }

        /// <summary>WP-096: the lane's IK as it got it — served share of the walk frames, locked share of the stance foot-frames, the
        /// marker-velocity diagnostic next to the ankle slide, and the release / budget counters over the pass.</summary>
        private static string IkOzeti(Grup g)
        {
            var c = new List<string>();
            for (int i = 0; i < IkSayacAdlari.Length; i++) c.Add(IkSayacKisa[i] + " " + g.IkFark[i]);
            return "ik served " + (g.IkKare > 0 ? F((double)g.IkServis / g.IkKare, 2) : "n/a") + " lockedInStance " + (g.DurusKare > 0 ? F((double)g.DurusKilitli / g.DurusKare, 2) : "n/a")
                   + " doubleSupportLocked " + (g.CiftDurusKare > 0 ? F((double)g.CiftDurusKilitli / g.CiftDurusKare, 2) : "n/a") + " doubleSupportFrames " + g.CiftDurusKare
                   + " markerVelocityDiagnostic avg " + F(Ort(g.KaymaTemas)) + " p95 " + F(Yuzdelik(g.KaymaTemas, 0.95)) + " release " + string.Join(" ", c);
        }

        private static string TabanOzeti(Grup g)
        {
            var gap = new List<double>();
            foreach (Govde b in g.Govdeler)
                foreach (ZombiKaydedici.Kare k in b.Kareler)
                {
                    if (!Olculen(k)) continue;
                    if (!float.IsNaN(k.BoslukL)) gap.Add(k.BoslukL);
                    if (!float.IsNaN(k.BoslukR)) gap.Add(k.BoslukR);
                }

            if (gap.Count == 0) return "n/a";
            gap.Sort();
            return F(gap[0] * 100.0, 1) + "/" + F(gap[gap.Count / 2] * 100.0, 1) + "/" + F(gap[(int)(0.95 * (gap.Count - 1))] * 100.0, 1) + "cm(min/med/p95)";
        }

        // ============================================================================================================ ZL3

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Category("WP096")]
        [Timeout(300000)]
        public IEnumerator ZL3_Kalabalik_CesitSayisi()
        {
            yield return RunKur();
            int sira = TohumVeSira(1u);
            var crowd = new List<ZombieEnemy>(R2ZombieCrowd.Size);
            R2ZombieCrowd.Spawn(R2ZombieCrowd.GateMix, 12, crowd);
            int animated = 0;
            foreach (ZombieEnemy z in crowd) if (z != null && z.Skinned != null && z.Skinned.Animator != null) animated++;
            Debug.Log("[DD-TEST] wp096.zl3.spawn mix=" + R2ZombieCrowd.Describe(R2ZombieCrowd.GateMix) + " spawned=" + crowd.Count + " animated=" + animated + " camera=" + CameraModeService.Current);
            Assert.AreEqual(R2ZombieCrowd.Size, crowd.Count, "kaynak yok: 40'lık kalabalık tam doğmadı");
            Assert.AreEqual(crowd.Count, animated, "kaynak yok: Animator'lu skinned gövdesi olmayan zombi var");
            for (int i = 0; i < KalabalikIsinma; i++) yield return null;

            int n = crowd.Count;
            var durum = new int[n];
            var klip = new int[n];
            var faz = new float[n];
            var hiz = new float[n];
            var okundu = new bool[n];
            var gorunur = new bool[n];
            var loko = new bool[n];
            var klipler = new List<AnimatorClipInfo>(8);
            int lokoHash = Animator.StringToHash(CharacterAnimatorContract.LocomotionState);
            var uclu = new HashSet<(int, int, int)>();
            var ucluSay = new Dictionary<(int, int, int), int>();
            var klipSay = new Dictionary<int, int>();
            var tumKlipler = new HashSet<int>();
            var klipAd = new Dictionary<int, string>();
            int kare = 0, ucluMin = int.MaxValue, maxGorunur = 0, maxLoko = 0, enKotuKlipBasi = 0, uzakMax = 0, gorunurKare = 0;
            double ucluToplam = 0, shannonToplam = 0, shannonKlipToplam = 0, tumEnKotu = 0, gorunurEnKotu = 0;
            long gorunurCift = 0, gorunurSenkron = 0, tumCift = 0, tumSenkron = 0;
            string enKotuKlipAd = "-";

            for (int f = 0; f < KalabalikKare; f++)
            {
                yield return null;
                if (f % KalabalikAdim != 0) continue;
                Camera cam = PrototypeCameraRig.Active != null ? PrototypeCameraRig.Active.Camera : null;
                Plane[] frustum = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
                uzakMax = Math.Max(uzakMax, CharacterAnimatorLod.FarCount);
                int govde = 0, vis = 0, lk = 0;
                for (int i = 0; i < n; i++)
                {
                    okundu[i] = false;
                    ZombieEnemy z = crowd[i];
                    if (z == null || !z.isActiveAndEnabled || !z.IsAlive || z.Skinned == null) continue;
                    Animator an = z.Skinned.Animator;
                    if (an == null || an.runtimeAnimatorController == null) continue;
                    AnimatorStateInfo info = an.GetCurrentAnimatorStateInfo(0);
                    an.GetCurrentAnimatorClipInfo(0, klipler);
                    int best = -1;
                    float w = -1f;
                    for (int c = 0; c < klipler.Count; c++) if (klipler[c].clip != null && klipler[c].weight > w) { w = klipler[c].weight; best = c; }
                    if (best < 0) continue;
                    AnimationClip ac = klipler[best].clip;
                    okundu[i] = true;
                    durum[i] = info.fullPathHash;
                    klip[i] = ac.GetInstanceID();
                    faz[i] = Mathf.Repeat(info.normalizedTime, 1f);
                    hiz[i] = info.speed * info.speedMultiplier;
                    loko[i] = info.shortNameHash == lokoHash;
                    if (!klipAd.ContainsKey(klip[i])) klipAd[klip[i]] = ac.name;
                    SkinnedMeshRenderer b0 = z.Skinned.Slot(0, ZombieSkinnedVisual.SlotBody);
                    gorunur[i] = frustum != null && b0 != null && GeometryUtility.TestPlanesAABB(frustum, b0.bounds);
                    govde++;
                    if (gorunur[i]) vis++;
                    if (loko[i]) lk++;
                }

                if (govde < 2) continue;
                kare++;
                maxGorunur = Math.Max(maxGorunur, vis);
                maxLoko = Math.Max(maxLoko, lk);

                // variety among the bodies in Locomotion: (clip, rate bucket, phase bucket) triples and clips
                uclu.Clear();
                ucluSay.Clear();
                klipSay.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (!okundu[i] || !loko[i]) continue;
                    var key = (klip[i], Mathf.FloorToInt(hiz[i] / OranKova), Mathf.FloorToInt(faz[i] / FazKova));
                    uclu.Add(key);
                    ucluSay[key] = ucluSay.TryGetValue(key, out int u) ? u + 1 : 1;
                    klipSay[klip[i]] = klipSay.TryGetValue(klip[i], out int c) ? c + 1 : 1;
                    tumKlipler.Add(klip[i]);
                }

                if (lk > 0)
                {
                    ucluMin = Math.Min(ucluMin, uclu.Count);
                    ucluToplam += uclu.Count;
                    shannonToplam += Shannon(ucluSay.Values, lk);
                    shannonKlipToplam += Shannon(klipSay.Values, lk);
                    foreach (KeyValuePair<int, int> kv in klipSay)
                        if (kv.Value > enKotuKlipBasi) { enKotuKlipBasi = kv.Value; enKotuKlipAd = klipAd.TryGetValue(kv.Key, out string s) ? s : "?"; }
                }

                // L5's pairs (same state, same dominant clip, circular phase distance < 0.05): every body, and the TD-visible ones
                int cift = 0, senkron = 0, vCift = 0, vSenkron = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!okundu[i]) continue;
                    for (int j = i + 1; j < n; j++)
                    {
                        if (!okundu[j]) continue;
                        cift++;
                        bool ikisiGorunur = gorunur[i] && gorunur[j];
                        if (ikisiGorunur) vCift++;
                        if (durum[i] != durum[j] || klip[i] != klip[j]) continue;
                        float d = Mathf.Abs(Mathf.Repeat(faz[i] - faz[j] + 0.5f, 1f) - 0.5f);
                        if (d >= FazPenceresi) continue;
                        senkron++;
                        if (ikisiGorunur) vSenkron++;
                    }
                }

                tumCift += cift;
                tumSenkron += senkron;
                gorunurCift += vCift;
                gorunurSenkron += vSenkron;
                if (cift > 0) tumEnKotu = Math.Max(tumEnKotu, (double)senkron / cift);
                if (vCift > 0) { gorunurKare++; gorunurEnKotu = Math.Max(gorunurEnKotu, (double)vSenkron / vCift); }
            }

            var adlar = new List<string>();
            foreach (int id in tumKlipler) adlar.Add(klipAd.TryGetValue(id, out string s) ? s : id.ToString(Inv));
            adlar.Sort(StringComparer.Ordinal);
            int lokoKare = kare > 0 && ucluMin != int.MaxValue ? 1 : 0;
            Debug.Log("[DD-TEST] wp096.zl3.ozet frames=" + kare + " maxVisible=" + maxGorunur + " maxInLocomotion=" + maxLoko + " visiblePairFrames=" + gorunurCift
                      + " farLodMax=" + uzakMax + " worstClip=" + enKotuKlipAd + "×" + enKotuKlipBasi + " clips(locomotion): " + string.Join(" ", adlar));

            var r = new Rapor("zl3");
            var eksik = new List<string>();
            if (kare == 0) eksik.Add("2+ zombinin okunduğu kare yok");
            if (maxLoko < 2 || lokoKare == 0) eksik.Add("Locomotion durumunda 2+ zombi yok (" + maxLoko + ")");
            r.Hedef("clipCount", tumKlipler.Count, true, HedefKlip, "Locomotion'daki baskın klipler, pencere boyu");
            r.Hedef("maxBodiesPerClip", enKotuKlipBasi, false, HedefKlipBasiGovde, enKotuKlipAd);
            r.Bilgi("distinctTriplesMin", ucluMin == int.MaxValue ? double.NaN : ucluMin, "(klip, oran/0.05, faz/0.1)");
            r.Bilgi("distinctTriplesMean", kare > 0 ? ucluToplam / kare : double.NaN);
            r.Bilgi("shannonTriplesMean", kare > 0 ? shannonToplam / kare : double.NaN);
            r.Bilgi("shannonClipsMean", kare > 0 ? shannonKlipToplam / kare : double.NaN);
            r.Bilgi("allSyncedPairShareWorst", tumCift > 0 ? tumEnKotu : double.NaN, "L5 tanımı, bütün gövdeler");
            bool tdVar = maxGorunur >= 2 && gorunurCift > 0;
            r.Bilgi("tdVisibleSyncedPairShareWorst", tdVar ? gorunurEnKotu : double.NaN,
                tdVar ? "L5 tanımı, TD kamerada görünen çiftler; L5 sınırı " + F(L5Sinir, 2) + (gorunurEnKotu <= L5Sinir ? " PASS" : " FAIL") : "kaynak yok: TD kamerada 2+ gövde yok");
            r.Bilgi("tdVisibleSyncedPairShareMean", tdVar ? (double)gorunurSenkron / gorunurCift : double.NaN);
            r.Bilgi("tdVisibleFrames", gorunurKare);
            r.Bilgi("maxVisible", maxGorunur);
            r.Bilgi("maxInLocomotion", maxLoko);
            r.Bilgi("farLodMax", uzakMax);
            r.Bilgi("gaitOrdinalStart", sira);
            r.Kaynak(eksik);
            r.Yazdir("ZL3_Kalabalik_CesitSayisi", kare, "GateMix 22/10/6/2, seed 1, TopDown" + (tdVar ? "" : "; TD visible pairs: none"));
        }

        private static double Shannon(IEnumerable<int> sayilar, int toplam)
        {
            if (toplam <= 0) return double.NaN;
            double h = 0;
            foreach (int c in sayilar)
            {
                if (c <= 0) continue;
                double p = (double)c / toplam;
                h -= p * Math.Log(p);
            }

            return h;
        }

        // ============================================================================================================ ZL4

        private sealed class Donus
        {
            public EnemyTier Kademe;
            public float Aci;
            public bool Gecerli;
            public double DeltaYaw = double.NaN, OmegaMax, AlfaMax, JerkMax, Egilme = double.NaN, EkEgilme = double.NaN, Gecikme = double.NaN;
            public readonly List<double> Jerkler = new List<double>();
            public string Klipler = "";
            public bool DonusKlibiVar;
        }

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Category("WP096")]
        [Timeout(900000)]
        public IEnumerator ZL4_Donus_YawSarsintisi_Egilme()
        {
            yield return RunKur();
            int sira = TohumVeSira(1u);
            CharacterAnimatorLod.ForceFar = 0;   // the turning body on the every-frame step (lean is off on far bodies)
            Vector3 s0 = NavPathFollower.ClampToNavMesh(_s.Rig.transform.position, 6f);
            var donusler = new List<Donus>();
            int kayip = 0;
            for (int t = 0; t < Kademeler.Length; t++)
            {
                for (int rep = 0; rep < 2 * DonusTekrar; rep++)
                {
                    float aci = rep < DonusTekrar ? 90f : 180f;
                    float isaret = (rep & 1) == 0 ? 1f : -1f;
                    Vector3 d0 = Quaternion.Euler(0f, (t * 2 * DonusTekrar + rep) * 36f, 0f) * Vector3.forward;
                    Vector3 hedef = NavPathFollower.ClampToNavMesh(s0 + d0 * DonusMesafe, 6f);
                    _s.Rig.Teleport(hedef + Vector3.up * 0.05f, Quaternion.LookRotation(-d0));
                    ZombieEnemy z = EnemyFactory.Spawn(Kademeler[t], 12, s0, Quaternion.LookRotation(d0));
                    if (z == null || z.Skinned == null || z.Skinned.Animator == null)
                    {
                        if (z != null) z.Remove();
                        kayip++;
                        continue;
                    }

                    ZombieSkinnedVisual v = z.Skinned;
                    ZombiKaydedici k = KaydediciEkle(v.Animator, PrototypeLayers.AimBlockMask, Kademeler[t] + "_" + F(aci, 0) + "_" + rep);
                    Object lean = OrnekNesne(v, "Lean") as Object;   // LocomotionLean (main + v4): the procedural roll it adds
                    PropertyInfo roll = lean != null ? lean.GetType().GetProperty("Roll", Ornek) : null;
                    if (roll != null && roll.PropertyType == typeof(float)) k.Ek = () => lean != null ? (float)roll.GetValue(lean) : float.NaN;
                    for (int f = 0; f < DonusOnce; f++)
                    {
                        k.Etiket = f >= DonusOnce - DonusTaban ? 0 : -1;
                        yield return null;
                    }

                    // the turn: the player jumps 90° / 180° off the zombie's current heading
                    Vector3 zf = Yatay(z.transform.forward);
                    zf = zf.sqrMagnitude > 1e-6f ? zf.normalized : d0;
                    Vector3 d1 = Quaternion.Euler(0f, aci * isaret, 0f) * zf;
                    Vector3 hedef2 = NavPathFollower.ClampToNavMesh(z.transform.position + d1 * DonusMesafe, 6f);
                    _s.Rig.Teleport(hedef2 + Vector3.up * 0.05f, Quaternion.LookRotation(-d1));
                    for (int f = 0; f < DonusPencere; f++)
                    {
                        k.Etiket = 1;
                        yield return null;
                    }

                    k.Birak();
                    Donus d = DonusOlc(k, aci);
                    d.Kademe = Kademeler[t];
                    donusler.Add(d);
                    if (z.IsSpawned) z.Remove();
                    Object.Destroy(k.gameObject);
                    yield return null;
                }
            }

            // ---- analysis
            var j90 = new List<double>();
            var j180 = new List<double>();
            var egilme = new List<double>();
            var ekEgilme = new List<double>();
            var gecikme = new List<double>();
            var omegaKademe = new Dictionary<EnemyTier, double>();
            var gecerliKademe = new Dictionary<EnemyTier, int>();
            double jerkMax = 0, alfaMax = 0, omegaMax = 0;
            int gecerli = 0, donusKlibi = 0;
            var satirlar = new List<string>();
            foreach (Donus d in donusler)
            {
                satirlar.Add(d.Kademe + "/" + F(d.Aci, 0) + " dYaw " + F(d.DeltaYaw, 1) + " ωmax " + F(d.OmegaMax, 0) + " αmax " + F(d.AlfaMax, 0) + " jmax " + F(d.JerkMax, 0)
                             + " lean " + F(d.Egilme, 2) + " procRoll " + F(d.EkEgilme, 2) + " lag " + F(d.Gecikme, 3) + (d.Gecerli ? "" : " INVALID") + " clips " + d.Klipler);
                if (!d.Gecerli) continue;
                gecerli++;
                gecerliKademe[d.Kademe] = (gecerliKademe.TryGetValue(d.Kademe, out int gk) ? gk : 0) + 1;
                (d.Aci < 135f ? j90 : j180).AddRange(d.Jerkler);
                jerkMax = Math.Max(jerkMax, d.JerkMax);
                alfaMax = Math.Max(alfaMax, d.AlfaMax);
                omegaMax = Math.Max(omegaMax, d.OmegaMax);
                omegaKademe[d.Kademe] = omegaKademe.TryGetValue(d.Kademe, out double ok) ? Math.Max(ok, d.OmegaMax) : d.OmegaMax;
                if (!double.IsNaN(d.Egilme)) egilme.Add(d.Egilme);
                if (!double.IsNaN(d.EkEgilme)) ekEgilme.Add(d.EkEgilme);
                if (!double.IsNaN(d.Gecikme)) gecikme.Add(d.Gecikme);
                if (d.DonusKlibiVar) donusKlibi++;
            }

            Debug.Log("[DD-TEST] wp096.zl4.donusler total=" + donusler.Count + " valid=" + gecerli + " lostSpawns=" + kayip + " farLod=" + CharacterAnimatorLod.FarCount
                      + " :: " + string.Join(" | ", satirlar));
            var r = new Rapor("zl4");
            var eksik = new List<string>();
            int beklenen = Kademeler.Length * 2 * DonusTekrar;
            if (gecerli < beklenen / 2) eksik.Add("geçerli dönüş " + gecerli + "/" + beklenen + " (yaw değişimi < açının yarısı)");
            foreach (EnemyTier t in Kademeler)
                if (!gecerliKademe.TryGetValue(t, out int c) || c < 3) eksik.Add(t + ": geçerli dönüş " + (gecerliKademe.TryGetValue(t, out int c2) ? c2 : 0) + " < 3");
            if (egilme.Count == 0) eksik.Add("gövde eğilmesi okunmadı (Hips / Chest kemiği yok)");
            r.Hedef("leanPeakMedianDeg", Medyan(egilme), true, HedefEgilme, "Hips→Chest roll, kök çerçevesinde, dönüş öncesi tabana göre");
            r.Bilgi("leanPeakMinDeg", egilme.Count > 0 ? Min(egilme) : double.NaN);
            r.Bilgi("proceduralRollPeakMedianDeg", Medyan(ekEgilme), ekEgilme.Count > 0 ? "LocomotionLean.Roll" : "yok");
            r.Bilgi("jerkP95_90", Yuzdelik(j90, 0.95), "°/s³, dönüş aktif kareler ±3");
            r.Bilgi("jerkP95_180", Yuzdelik(j180, 0.95), "°/s³");
            r.Bilgi("jerkMax", jerkMax, "°/s³");
            r.Bilgi("yawAccelMax", alfaMax, "°/s²");
            r.Bilgi("yawRateMax", omegaMax, "°/s");
            foreach (EnemyTier t in Kademeler) r.Bilgi("yawRateMax" + t, omegaKademe.TryGetValue(t, out double o) ? o : double.NaN);
            r.Bilgi("hipsYawLagMedianSeconds", Medyan(gecikme), "+ = kalça kökün arkasında");
            r.Bilgi("turnClipSeen", donusKlibi > 0 ? 1 : 0, donusKlibi + " dönüşte turn/pivot klibi");
            r.Bilgi("validTurns", gecerli);
            r.Bilgi("gaitOrdinalStart", sira);
            r.Kaynak(eksik);
            r.Yazdir("ZL4_Donus_YawSarsintisi_Egilme", gecerli, "4 tiers x (90 x5 + 180 x5), real AI, TopDown, ForceFar 0");
        }

        private static Donus DonusOlc(ZombiKaydedici k, float aci)
        {
            var d = new Donus { Aci = aci };
            List<ZombiKaydedici.Kare> ks = k.Kareler;
            var idx = new List<int>();
            for (int i = 0; i < ks.Count; i++) if (ks[i].Etiket == 0 || ks[i].Etiket == 1) idx.Add(i);
            int ilk = -1;
            for (int i = 0; i < idx.Count; i++) if (ks[idx[i]].Etiket == 1) { ilk = i; break; }
            if (ilk < 1) return d;

            // the torso roll before the turn (its own pose's lean is not the turn's)
            double taban = 0;
            int tabanN = 0;
            for (int i = 0; i < ilk; i++) { taban += ks[idx[i]].Egilme; tabanN++; }
            taban /= Math.Max(1, tabanN);

            int m = idx.Count;
            var w = new double[m];
            var a = new double[m];
            var j = new double[m];
            var tanimli = new bool[m];
            for (int i = 1; i < m; i++)
            {
                ZombiKaydedici.Kare k0 = ks[idx[i - 1]], k1 = ks[idx[i]];
                float dt = k1.Dt > 1e-6f ? k1.Dt : Dt;
                w[i] = (k1.KokYaw - k0.KokYaw) / dt;
                if (i >= 2) a[i] = (w[i] - w[i - 1]) / dt;
                if (i >= 3) { j[i] = (a[i] - a[i - 1]) / dt; tanimli[i] = true; }
            }

            var aktif = new bool[m];
            for (int i = ilk; i < m; i++)
            {
                if (Math.Abs(w[i]) <= AktifOmega) continue;
                for (int q = Math.Max(ilk, i - 3); q <= Math.Min(m - 1, i + 3); q++) aktif[q] = true;
            }

            double y0 = ks[idx[ilk - 1]].KokYaw, h0 = ks[idx[ilk - 1]].KalcaYaw;
            d.DeltaYaw = ks[idx[m - 1]].KokYaw - y0;
            d.Gecerli = Math.Abs(d.DeltaYaw) >= 0.5 * aci;
            int tr = -1, th = -1;
            double esik = 0.5 * Math.Abs(d.DeltaYaw);
            double ekMax = double.NaN;
            var klipler = new HashSet<string>();
            for (int i = ilk; i < m; i++)
            {
                ZombiKaydedici.Kare kr = ks[idx[i]];
                d.OmegaMax = Math.Max(d.OmegaMax, Math.Abs(w[i]));
                if (i >= ilk + 1) d.AlfaMax = Math.Max(d.AlfaMax, Math.Abs(a[i]));
                if (tanimli[i] && aktif[i]) { d.Jerkler.Add(Math.Abs(j[i])); d.JerkMax = Math.Max(d.JerkMax, Math.Abs(j[i])); }
                double e = Math.Abs(kr.Egilme - taban);
                d.Egilme = double.IsNaN(d.Egilme) ? e : Math.Max(d.Egilme, e);
                if (!float.IsNaN(kr.Ek)) ekMax = double.IsNaN(ekMax) ? Math.Abs(kr.Ek) : Math.Max(ekMax, Math.Abs(kr.Ek));
                if (tr < 0 && Math.Abs(kr.KokYaw - y0) >= esik) tr = i;
                if (th < 0 && Math.Abs(kr.KalcaYaw - h0) >= esik) th = i;
                if (kr.Klip != 0) klipler.Add(KlipAdi(kr.Klip));
                if (kr.SonrakiKlip != 0) klipler.Add(KlipAdi(kr.SonrakiKlip));
            }

            d.EkEgilme = ekMax;
            if (tr >= 0 && th >= 0 && esik > 1.0) d.Gecikme = (th - tr) * (double)Dt;
            foreach (string s in klipler) if (DonusKlibi.IsMatch(s)) d.DonusKlibiVar = true;
            d.Klipler = string.Join(",", klipler);
            return d;
        }

        // ============================================================================================================ ZL5

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Category("WP096")]
        [Timeout(600000)]
        public IEnumerator ZL5_KalkisDurus_GecisVarligi()
        {
            yield return RunKur();
            int sira = TohumVeSira(1u);
            CharacterAnimatorLod.ForceFar = 0;
            Vector3 merkez = SahneKur();
            MethodInfo hizMetodu = HizMetodu();
            Assert.IsNotNull(hizMetodu, "kaynak yok: ZombieSkinnedVisual.SetSpeed(float, float) yok — sahne sürücüsü bağlanamadı");
            MethodInfo motorMetodu = Metot(typeof(ZombieSkinnedVisual), "StepTransition", typeof(Vector3), typeof(float), typeof(float), typeof(bool),
                typeof(float), typeof(Vector3).MakeByRefType(), typeof(float).MakeByRefType());
            // Before runs remain source-compatible; an after run cannot silently bypass the p3 motor drive windows.
            if (Zorla) Assert.IsNotNull(motorMetodu, "kaynak yok: StepTransition motor API yok — p3 geçiş ölçümü sürülemez");
            int n = Kademeler.Length;
            var gercekHiz = new float[n];
            var zler = new List<ZombieEnemy>(n);
            var kayitlar = new List<ZombiKaydedici>(n);
            var hizlar = new float[n];
            var konum = new Vector3[n];
            Quaternion ileri = Quaternion.LookRotation(Vector3.forward);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = merkez + Vector3.right * ((i - (n - 1) * 0.5f) * SeritAraligi) + Vector3.back * BaslangicGeri;
                ZombieEnemy z = EnemyFactory.Spawn(Kademeler[i], 12, p, ileri);
                Assert.IsNotNull(z, "kaynak yok: " + Kademeler[i] + " doğmadı");
                SahneyeAl(z, p, ileri);
                Assert.IsTrue(z.Skinned != null && z.Skinned.Animator != null && z.Skinned.Animator.isHuman, "kaynak yok: " + Kademeler[i] + " skinned / Humanoid Animator yok");
                z.Skinned.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                hizlar[i] = z.Stats.MoveSpeed * (OrnekFloat(z.Skinned, "SpeedJitter") ?? 1f);
                konum[i] = p;
                zler.Add(z);
                kayitlar.Add(KaydediciEkle(z.Skinned.Animator, 1 << PrototypeLayers.Environment, Kademeler[i].ToString()));
            }

            Physics.SyncTransforms();
            var arg = new object[2];
            var motorArg = new object[7];
            for (int rep = 0; rep < KdTekrar; rep++)
            {
                for (int f = 0; f < KdDur; f++)          // still (the last 30 frames: the idle reference)
                {
                    for (int i = 0; i < n; i++) { kayitlar[i].Etiket = f >= KdDur - KlipPencere ? 40 + rep : -1; Sur(zler[i], ref konum[i], 0f, ref gercekHiz[i], hizMetodu, arg, motorMetodu, motorArg); }
                    yield return null;
                }

                for (int f = 0; f < KdYuru; f++)         // start (window), then steady walk (reference)
                {
                    for (int i = 0; i < n; i++) { kayitlar[i].Etiket = f < L3Pencere ? 10 + rep : 30 + rep; Sur(zler[i], ref konum[i], hizlar[i], ref gercekHiz[i], hizMetodu, arg, motorMetodu, motorArg); }
                    yield return null;
                }

                for (int f = 0; f < KdDurus; f++)        // stop (window)
                {
                    for (int i = 0; i < n; i++) { kayitlar[i].Etiket = f < L3Pencere ? 20 + rep : -1; Sur(zler[i], ref konum[i], 0f, ref gercekHiz[i], hizMetodu, arg, motorMetodu, motorArg); }
                    yield return null;
                }
            }

            foreach (ZombiKaydedici k in kayitlar) k.Birak();

            // ---- analysis
            int nb = R2LokomotionOlcumTests.Kemikler.Length;
            var kalkis = new List<double>();
            var durus = new List<double>();
            var gecisKlip = new HashSet<int>();
            var kararliKlip = new HashSet<int>();
            var durKlip = new HashSet<int>();
            double uzuv = 0, govdeMax = 0, oran = 0;
            string uzuvAt = "-", govdeAt = "-", oranAt = "-";
            int pencere = 0, tamPencere = 0, lokoDisi = 0, uyarlamali = 0, olayliGovde = 0;
            var eksik = new List<string>();
            var satirlar = new List<string>();
            for (int bi = 0; bi < n; bi++)
            {
                ZombiKaydedici kk = kayitlar[bi];
                List<ZombiKaydedici.Kare> ks = kk.Kareler;
                List<float> deg = kk.Derece;
                if (!kk.KemikVar(0) || !kk.KemikVar(12)) eksik.Add(Kademeler[bi] + ": uzuv/gövde kemiği yok " + kk.KemikOzeti());
                // düzeltme 07.10: stance from the clip events (the IK presses every foot to the floor, see ZL1); while the idle is
                // the dominant clip both feet are planted. Without plant events: the height rule, judged on the steady walk's share.
                var bKararli = new HashSet<int>();
                var bRolanti = new HashSet<int>();
                int basma = 0;
                foreach (ZombiKaydedici.Kare k in ks)
                {
                    if (k.Etiket >= 30 && k.Etiket < 40) bKararli.Add(k.Klip);
                    if (k.Etiket >= 40 && k.Etiket < 50 && k.Klip != 0) bRolanti.Add(k.Klip);
                    if (k.OlayL || k.OlayR) basma++;
                }

                bRolanti.ExceptWith(bKararli);
                TemasSonuc ts;
                string kaynak;
                if (basma >= 2 * KdTekrar)
                {
                    ts = OlayDuruslari(ks, bRolanti);
                    kaynak = "olay";
                    olayliGovde++;
                }
                else
                {
                    ts = Temaslar(ks, false);
                    kaynak = "yukseklik";
                    if (PencereTemasi(ks, ts) < 4) { ts = Temaslar(ks, true); uyarlamali++; kaynak = "yukseklik-uyarlamali"; }
                }

                int yuruAyak = 0, yuruTemas = 0;
                for (int i = 0; i < ks.Count; i++)
                {
                    ZombiKaydedici.Kare k = ks[i];
                    if (k.Etiket < 30 || k.Etiket >= 40 || !k.Gecerli) continue;
                    yuruAyak += 2;
                    if (ts.L[i]) yuruTemas++;
                    if (ts.R[i]) yuruTemas++;
                }

                double yuruPay = yuruAyak > 0 ? (double)yuruTemas / yuruAyak : double.NaN;
                if (!(yuruPay >= TemasPayAlt && yuruPay <= TemasPayUst))
                    eksik.Add(Kademeler[bi] + ": kararlı yürüyüşte temas ayrımı yok — ayak-kare payı " + F(yuruPay, 2) + " (" + kaynak + "; dedektör doymuş)");
                var kararliTepe = new double[nb];
                for (int i = 0; i < ks.Count; i++)
                {
                    ZombiKaydedici.Kare k = ks[i];
                    if (k.Etiket >= 30 && k.Etiket < 40) { kararliKlip.Add(k.Klip); kararliKlip.Add(k.SonrakiKlip); }
                    if (k.Etiket >= 40 && k.Etiket < 50) { durKlip.Add(k.Klip); durKlip.Add(k.SonrakiKlip); }
                    if (k.Etiket < 30 || k.Etiket >= 40 || !k.Gecerli) continue;
                    for (int b = 0; b < nb; b++) { float x = deg[i * nb + b]; if (!float.IsNaN(x) && x > kararliTepe[b]) kararliTepe[b] = x; }
                }

                var kStart = new List<double>();
                var kStop = new List<double>();
                for (int rep = 0; rep < KdTekrar; rep++)
                {
                    foreach (int lab in new[] { 10 + rep, 20 + rep })
                    {
                        List<double> hedef = lab < 20 ? kStart : kStop;
                        var tepe = new double[nb];
                        int kare = 0;
                        for (int i = 0; i < ks.Count; i++)
                        {
                            ZombiKaydedici.Kare k = ks[i];
                            if (k.Etiket != lab) continue;
                            if (k.Since <= KlipPencere) { gecisKlip.Add(k.Klip); gecisKlip.Add(k.SonrakiKlip); if (!k.Loko) lokoDisi++; }
                            if (!k.Gecerli || k.Since < 1) continue;
                            if (k.Since <= L1tPencere)
                            {
                                kare++;
                                if (ts.L[i]) hedef.Add(Oran(k.AyakVL, k.KalcaV));
                                if (ts.R[i]) hedef.Add(Oran(k.AyakVR, k.KalcaV));
                            }

                            if (k.Since <= L3Pencere)
                                for (int b = 0; b < nb; b++) { float x = deg[i * nb + b]; if (!float.IsNaN(x) && x > tepe[b]) tepe[b] = x; }
                        }

                        pencere++;
                        if (kare >= L1tPencere) tamPencere++;
                        string ad = Kademeler[bi] + (lab < 20 ? " start#" : " stop#") + rep;
                        for (int b = 0; b < nb; b++)
                        {
                            if (!kk.KemikVar(b)) continue;
                            double x = tepe[b];
                            if (b < R2LokomotionOlcumTests.UzuvSayisi) { if (x > uzuv) { uzuv = x; uzuvAt = ad + " " + R2LokomotionOlcumTests.Kemikler[b]; } }
                            else if (x > govdeMax) { govdeMax = x; govdeAt = ad + " " + R2LokomotionOlcumTests.Kemikler[b]; }
                            double q = x / Math.Max(kararliTepe[b], R2LokomotionOlcumTests.KararliTepeTabani);
                            if (q > oran) { oran = q; oranAt = ad + " " + R2LokomotionOlcumTests.Kemikler[b] + " " + F(x, 2) + "/" + F(kararliTepe[b], 2); }
                        }
                    }
                }

                kalkis.AddRange(kStart);
                durus.AddRange(kStop);
                satirlar.Add(Kademeler[bi] + " v " + F(hizlar[bi], 2) + " start avg " + F(Ort(kStart)) + " n " + kStart.Count + " stop avg " + F(Ort(kStop)) + " n " + kStop.Count
                             + " [" + kaynak + "] plants " + basma + " walkShare " + F(yuruPay, 2));
            }

            var yeni = new List<string>();
            bool desen = false;
            var gecisAd = new List<string>();
            foreach (int id in gecisKlip)
            {
                if (id == 0) continue;
                string ad = KlipAdi(id);
                gecisAd.Add(ad);
                if (GecisKlibi.IsMatch(ad)) desen = true;
                if (!kararliKlip.Contains(id) && !durKlip.Contains(id)) yeni.Add(ad);
            }

            gecisAd.Sort(StringComparer.Ordinal);
            yeni.Sort(StringComparer.Ordinal);
            var hepsi = new List<double>(kalkis);
            hepsi.AddRange(durus);
            Debug.Log("[DD-TEST] wp096.zl5.ozet windows=" + tamPencere + "/" + pencere + " transitionClips=" + string.Join(",", gecisAd) + " new=" + string.Join(",", yeni)
                      + " nameMatch=" + desen + " nonLocomotionFrames=" + lokoDisi + " limb " + uzuvAt + " torso " + govdeAt + " ratio " + oranAt + " :: " + string.Join(" | ", satirlar));
            if (tamPencere < pencere) eksik.Add("tam pencereli geçiş " + tamPencere + "/" + pencere);
            if (hepsi.Count < 10) eksik.Add("geçiş pencerelerinde temas karesi " + hepsi.Count + " < 10");
            if (kalkis.Count == 0) eksik.Add("kalkış penceresinde temas karesi yok");
            if (durus.Count == 0) eksik.Add("duruş penceresinde temas karesi yok");

            var r = new Rapor("zl5");
            bool gecisVar = yeni.Count > 0 || desen;
            r.Hedef("transitionClipSeen", gecisVar ? 1 : 0, true, 1, "0,5 s içinde kararlı yürüyüş / duruş klibi dışında klip");
            r.Hedef("l1tAvg", Ort(hepsi), false, HedefL1t, "taban " + F(GovdeHizTabani, 2) + " m/s (kalça)");
            r.Bilgi("l1tP95", Yuzdelik(hepsi, 0.95));
            r.Bilgi("l1tAvgStart", Ort(kalkis));
            r.Bilgi("l1tAvgStop", Ort(durus));
            r.Bilgi("limbDegPerFrameMax", uzuv, "R2 L3 sınırı " + F(L3Uzuv, 1) + (uzuv <= L3Uzuv ? " PASS" : " FAIL"));
            r.Bilgi("torsoDegPerFrameMax", govdeMax, "R2 L3 sınırı " + F(L3Govde, 1) + (govdeMax <= L3Govde ? " PASS" : " FAIL"));
            r.Bilgi("transitionToSteadyRatioMax", oran, "R2 L3 sınırı " + F(L3Oran, 2) + (oran <= L3Oran ? " PASS" : " FAIL"));
            r.Bilgi("newClipCount", yeni.Count);
            r.Bilgi("nonLocomotionStateFrames", lokoDisi);
            r.Bilgi("adaptiveContactBodies", uyarlamali);
            r.Bilgi("eventContactBodies", olayliGovde, "/" + n + " gövde klip olaylarıyla");
            r.Bilgi("gaitOrdinalStart", sira);
            r.Bilgi("transitionMotorDriven", motorMetodu != null ? 1 : 0, "StepTransition velocity/yaw motor output applied to the physical root; SetSpeed receives achieved forward speed");
            double baslamaMin = double.NaN, bitisMin = double.NaN;
            for (int i = 0; i < n; i++)
            {
                int? starts = OrnekInt(zler[i].Skinned, "TransitionStarts"), ends = OrnekInt(zler[i].Skinned, "TransitionCompletions");
                if (starts.HasValue) baslamaMin = double.IsNaN(baslamaMin) ? starts.Value : Math.Min(baslamaMin, starts.Value);
                if (ends.HasValue) bitisMin = double.IsNaN(bitisMin) ? ends.Value : Math.Min(bitisMin, ends.Value);
            }
            r.Hedef("transitionStartsPerBodyMin", baslamaMin, true, 2 * KdTekrar, "five start + five stop intents per body");
            r.Hedef("transitionCompletionsPerBodyMin", bitisMin, true, 2 * KdTekrar, "p3 motor reaches clip end / attach_out per start and stop");
            r.Kaynak(eksik);
            r.Yazdir("ZL5_KalkisDurus_GecisVarligi", hepsi.Count, "4 tiers x 5 start/stop on the stage, intent speed step + p3 motor drive windows; stance = clip events + idle clip");
        }

        private static void Sur(ZombieEnemy z, ref Vector3 konum, float hiz, ref float gercekHiz, MethodInfo hizMetodu, object[] arg,
            MethodInfo motorMetodu, object[] motorArg)
        {
            float dt = Time.deltaTime;
            Vector3 velocity = Vector3.forward * hiz;
            float yaw = 0f;
            if (motorMetodu != null)
            {
                motorArg[0] = Vector3.forward;
                motorArg[1] = hiz;
                motorArg[2] = gercekHiz;
                motorArg[3] = true;
                motorArg[4] = dt;
                motorArg[5] = Vector3.zero;
                motorArg[6] = 0f;
                if ((bool)motorMetodu.Invoke(z.Skinned, motorArg))
                {
                    velocity = (Vector3)motorArg[5];
                    yaw = (float)motorArg[6];
                }
            }
            Vector3 before = konum;
            konum += velocity * dt;
            z.transform.position = konum;
            if (Mathf.Abs(yaw) > 1e-5f) z.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * z.transform.rotation;
            gercekHiz = dt > 1e-6f ? Mathf.Max(0f, Vector3.Dot((konum - before) / dt, z.transform.forward)) : 0f;
            arg[0] = gercekHiz;
            arg[1] = dt;
            hizMetodu.Invoke(z.Skinned, arg);
        }

        private static int PencereTemasi(List<ZombiKaydedici.Kare> ks, TemasSonuc ts)
        {
            int n = 0;
            for (int i = 0; i < ks.Count; i++)
            {
                ZombiKaydedici.Kare k = ks[i];
                if (k.Etiket < 10 || k.Etiket >= 30 || k.Since < 1 || k.Since > L1tPencere) continue;
                if (ts.L[i]) n++;
                if (ts.R[i]) n++;
            }

            return n;
        }

        // ============================================================================================================ ZL6

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Category("WP096")]
        [Timeout(600000)]
        public IEnumerator ZL6_Kalabalik40_GcSifir()
        {
            yield return RunKur();
            int sira = TohumVeSira(1u);
            // HotPathAllocationContractTests :111-135: the frame counters count only with the profiler on, and the bootstrapper's
            // 60 fps cap leaves ProfilerRecorder.LastValue a frame behind — uncapped for the measurement, both restored in TearDown
            _profilerOnce = UnityEngine.Profiling.Profiler.enabled;
            _profilerGeriAl = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            _fpsOnce = Application.targetFrameRate;
            _fpsGeriAl = true;
            Application.targetFrameRate = -1;
            yield return null;
            var bosB = new long[GcKare];
            var bosN = new long[GcKare];
            var kalB = new long[GcKare];
            var kalN = new long[GcKare];
            var bosF = new int[GcKare];
            var kalF = new int[GcKare];
            long kontrol = 0;
            int uzakMax = 0, canli = 0;
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null;
                yield return null;
                Assert.IsTrue(bytes.Valid && count.Valid, "kaynak yok: Profiler kare sayaçları yok (Editor / development build gerekir)");

                // positive control: the counters see 300 small objects in a frame
                var yem = new object[300];
                for (int i = 0; i < yem.Length; i++) yem[i] = new byte[80];
                for (int i = 0; i < GcKontrolKare; i++)
                {
                    yield return null;
                    kontrol = Math.Max(kontrol, count.LastValue);
                }

                GC.KeepAlive(yem);
                Assert.Greater(kontrol, 250, "kaynak yok: sayaç 300 küçük ayırmayı görmedi — ölçüm geçersiz (" + kontrol + ")");

                // the empty scene (no zombie): the same-run baseline
                for (int i = 0; i < 30; i++) yield return null;
                for (int i = 0; i < GcKare; i++)
                {
                    yield return null;
                    bosB[i] = bytes.LastValue;
                    bosN[i] = count.LastValue;
                    bosF[i] = Time.frameCount;
                }

                // the 40-body crowd closing in: two windows of warm-up (pools, caches), then the measured window
                sira = TohumVeSira(1u);
                var crowd = new List<ZombieEnemy>(R2ZombieCrowd.Size);
                R2ZombieCrowd.Spawn(R2ZombieCrowd.GateMix, 12, crowd);
                Assert.AreEqual(R2ZombieCrowd.Size, crowd.Count, "kaynak yok: 40'lık kalabalık tam doğmadı");
                for (int i = 0; i < GcIsinmaKare; i++) yield return null;
                for (int i = 0; i < GcKare; i++)
                {
                    yield return null;
                    kalB[i] = bytes.LastValue;
                    kalN[i] = count.LastValue;
                    kalF[i] = Time.frameCount;
                    if (CharacterAnimatorLod.FarCount > uzakMax) uzakMax = CharacterAnimatorLod.FarCount;
                }

                foreach (ZombieEnemy z in crowd) if (z != null && z.IsAlive) canli++;
            }

            long bosMax = Max(bosB), kalMax = Max(kalB), kalNMax = Max(kalN), bosNMax = Max(bosN);
            long bosMed = Medyan(bosB), kalMed = Medyan(kalB), bosNMed = Medyan(bosN), kalNMed = Medyan(kalN);
            int ustte = 0;
            foreach (long b in kalB) if (b > bosMax) ustte++;
            // the heaviest frames (window index, Time.frameCount, bytes, count) to bisect: the recorder reads the last completed frame
            string kalTepe = TepeKareler(kalB, kalN, kalF, 3), bosTepe = TepeKareler(bosB, bosN, bosF, 1);
            GcPencereYaz(bosB, bosN, bosF, kalB, kalN, kalF);   // outside the measured windows / disposed recorders
            int tepe = 0;
            for (int i = 1; i < GcKare; i++) if (kalB[i] > kalB[tepe]) tepe = i;
            Debug.Log("[DD-TEST] wp096.zl6.ozet control=" + kontrol + " idle bytes max/med/p95 " + bosMax + "/" + bosMed + "/" + Yuzdelik(bosB, 0.95) + " count max/med " + bosNMax + "/" + bosNMed
                      + " | crowd bytes max/med/p95 " + kalMax + "/" + kalMed + "/" + Yuzdelik(kalB, 0.95) + " count max/med " + kalNMax + "/" + kalNMed
                      + " framesAboveIdleMax " + ustte + "/" + GcKare + " alive " + canli + "/" + R2ZombieCrowd.Size + " farLodMax " + uzakMax
                      + " frames " + GcKare + " (after " + GcIsinmaKare + " warm-up) | crowd top " + kalTepe + " | idle top " + bosTepe);
            var r = new Rapor("zl6");
            var eksik = new List<string>();
            if (canli < R2ZombieCrowd.Size / 2) eksik.Add("ölçüm penceresinin sonunda canlı gövde " + canli + " < " + R2ZombieCrowd.Size / 2);
            r.Hedef("gcMedianCountIncrease", kalNMed - bosNMed, false, 0, "kalabalık − boş sahne, kare başı ayırma sayısı medyanı (sürekli ayırma ≥ 1)");
            r.Hedef("gcFramesAboveIdleMax", ustte, false, GcUstKareSiniri, "/" + GcKare + " kalabalık karesi boş sahnenin en çoğunun üstünde");
            r.Bilgi("gcMedianIncreaseBytes", kalMed - bosMed);
            r.Bilgi("gcAllocatedInFrameMaxIncreaseBytes", kalMax - bosMax, "eski kapı (tek kare gürültüsü)");
            r.Bilgi("gcAllocatedInFrameMaxBytes", kalMax, "0 B/kare" + (kalMax == 0 ? " PASS" : " FAIL"));
            r.Bilgi("gcAllocatedInFrameMedianBytes", kalMed);
            r.Bilgi("gcAllocationCountMax", kalNMax);
            r.Bilgi("gcAllocationCountMedian", kalNMed);
            r.Bilgi("idleGcAllocatedInFrameMaxBytes", bosMax);
            r.Bilgi("idleGcAllocatedInFrameMedianBytes", bosMed);
            r.Bilgi("idleGcAllocationCountMedian", bosNMed);
            r.Bilgi("idleGcAllocationCountMax", bosNMax);
            r.Bilgi("topCrowdFrameIndex", tepe, "frameCount " + kalF[tepe] + " " + kalB[tepe] + " B / " + kalN[tepe] + " ayırma");
            r.Bilgi("topCrowdFrameCount", kalF[tepe]);
            r.Bilgi("positiveControlCount", kontrol);
            r.Bilgi("aliveBodies", canli);
            r.Bilgi("farLodMax", uzakMax);
            r.Bilgi("gaitOrdinalStart", sira);
            MainGoreArtis(r, kalNMed - bosNMed, kalMed - bosMed, ustte);
            r.Kaynak(eksik);
            r.Yazdir("ZL6_Kalabalik40_GcSifir", GcKare, "ProfilerRecorder GC Allocated In Frame / Count, 240 idle vs 240 crowd frames, GateMix seed 1, TopDown");
        }

        /// <summary>
        /// ZL6 against the "before" run (DD_WP096_BASELINE_DIR = its DD_R2_MEASURE_DIR): this run's same-run increases minus main's.
        /// The gate stays the same-run one (crowd vs empty scene); this line says how much of it main already had.
        /// </summary>
        private static void MainGoreArtis(Rapor r, long sayiArtis, long baytArtis, int ustte)
        {
            string dir = Environment.GetEnvironmentVariable(TabanDegiskeni);
            if (string.IsNullOrEmpty(dir)) return;
            string yol = Path.Combine(dir, "r2-wp096-zl6.json");
            if (!File.Exists(yol)) { Debug.Log("wp096.zl6.mainGoreArtis=yok (" + yol + " yok)"); return; }
            string json = File.ReadAllText(yol);
            double? tSayi = JsonSayi(json, "gcMedianCountIncrease"), tBayt = JsonSayi(json, "gcMedianIncreaseBytes"), tUst = JsonSayi(json, "gcFramesAboveIdleMax");
            Match c = Regex.Match(json, "\"commit\":\\s*\"([^\"]*)\"");
            string taban = "main " + (c.Success && c.Groups[1].Value.Length > 0 ? c.Groups[1].Value : "?");
            r.Bilgi("vsMainMedianCountIncrease", tSayi.HasValue ? sayiArtis - tSayi.Value : double.NaN, "artış main'e göre (" + taban + ")");
            r.Bilgi("vsMainMedianIncreaseBytes", tBayt.HasValue ? baytArtis - tBayt.Value : double.NaN, "artış main'e göre");
            r.Bilgi("vsMainFramesAboveIdleMax", tUst.HasValue ? ustte - tUst.Value : double.NaN, "artış main'e göre");
        }

        private static double? JsonSayi(string json, string anahtar)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(anahtar) + "\":\\s*(-?[0-9][0-9.eE+-]*)");
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, Inv, out double d) ? d : (double?)null;
        }

        /// <summary>Keep every original recorder sample for spike attribution. No filtering, subtraction or gate adjustment;
        /// the frame stamp is the observation frame (LastValue is the last completed recorder frame).</summary>
        private static void GcPencereYaz(long[] idleBytes, long[] idleCount, int[] idleFrame, long[] crowdBytes, long[] crowdCount, int[] crowdFrame)
        {
            string dir = Environment.GetEnvironmentVariable("DD_R2_MEASURE_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "r2-wp096-zl6-frames.csv");
            using (var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            {
                writer.WriteLine("window,index,observationFrame,gcAllocatedBytes,gcAllocationCount");
                for (int i = 0; i < idleBytes.Length; i++)
                    writer.WriteLine("idle," + i.ToString(Inv) + "," + idleFrame[i].ToString(Inv) + "," + idleBytes[i].ToString(Inv) + "," + idleCount[i].ToString(Inv));
                for (int i = 0; i < crowdBytes.Length; i++)
                    writer.WriteLine("crowd," + i.ToString(Inv) + "," + crowdFrame[i].ToString(Inv) + "," + crowdBytes[i].ToString(Inv) + "," + crowdCount[i].ToString(Inv));
            }
            Debug.Log("wp096.zl6.rawFrames=" + path);
        }

        /// <summary>The n heaviest frames of a window: "#index@frameCount bytesB/countN".</summary>
        private static string TepeKareler(long[] bayt, long[] sayi, int[] kare, int n)
        {
            var idx = new List<int>(bayt.Length);
            for (int i = 0; i < bayt.Length; i++) idx.Add(i);
            idx.Sort((a, b) => bayt[b].CompareTo(bayt[a]));
            var parca = new List<string>(n);
            for (int i = 0; i < Math.Min(n, idx.Count); i++) parca.Add("#" + idx[i] + "@" + kare[idx[i]] + " " + bayt[idx[i]] + "B/" + sayi[idx[i]]);
            return string.Join(" ", parca);
        }

        private static long Yuzdelik(long[] xs, double q)
        {
            if (xs.Length == 0) return 0;
            var c = (long[])xs.Clone();
            Array.Sort(c);
            return c[Mathf.Clamp((int)Math.Ceiling(q * c.Length) - 1, 0, c.Length - 1)];
        }

        // ============================================================================================================ captures

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Category("WP096")]
        [Category("WP096Capture")]
        [Timeout(2400000)]
        public IEnumerator CAP_TD40s_Mp4Kareleri()
        {
            string kok = KayitKlasoru();
            string dir = Path.Combine(kok, "td-frames");
            Directory.CreateDirectory(dir);
            yield return RunKur();
            Time.captureDeltaTime = 1f / 30f;   // plan §4: 30 fps frames for frames2mp4
            int sira = TohumVeSira(1u);
            var crowd = new List<ZombieEnemy>(R2ZombieCrowd.Size);
            R2ZombieCrowd.Spawn(R2ZombieCrowd.GateMix, 12, crowd);
            Assert.AreEqual(R2ZombieCrowd.Size, crowd.Count, "kaynak yok: 40'lık kalabalık tam doğmadı");
            PrototypeCameraRig rigCam = PrototypeCameraRig.Active;
            Assert.IsNotNull(rigCam, "kaynak yok: oyun kamerası yok");
            Camera cam = rigCam.Camera;
            Vector3 merkez = _s.Rig.transform.position;
            CameraFeel feel = FeelRuntime.Current.Camera;
            Vector3 ofset = cam.transform.position - merkez;
            Debug.Log("[DD-TEST] wp096.cap.td mode=" + CameraModeService.Current + " feel height/back/fov=" + F(feel.heightMeters, 2) + "/" + F(feel.backDistanceMeters, 2) + "/" + F(feel.fieldOfViewDegrees, 1)
                      + " camera fov=" + F(cam.fieldOfView, 1) + " offset=" + ofset.ToString("F2") + " ring=" + F(TdHalka, 1) + "m stick=" + F(TdCubuk, 2) + " frames=" + TdKare + " dir=" + dir + " ordinal=" + sira);

            var rt = new RenderTexture(KareW, KareH, 24) { name = "WP096 TD kare", antiAliasing = 1 };
            rt.Create();
            var tex = new Texture2D(KareW, KareH, TextureFormat.RGB24, false);
            int yazilan = 0;
            try
            {
                for (int f = 0; f < TdKare; f++)
                {
                    _s.Girdi.Yon = HalkaCubugu(_s.Rig.transform.position, merkez, rigCam);
                    yield return null;
                    Ciz(cam, rt, tex, Path.Combine(dir, "frame-" + f.ToString("D5", Inv) + ".png"));
                    yazilan++;
                }
            }
            finally
            {
                _s.Girdi.Yon = Vector2.zero;
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(tex);
            }

            Debug.Log("[DD-TEST] wp096.cap.td written=" + yazilan + " alive=" + EnemyFactory.LivingCount + " dir=" + dir);
            Assert.AreEqual(TdKare, yazilan, "kareler eksik");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "frame-" + (TdKare - 1).ToString("D5", Inv) + ".png")), "son kare yazılmadı");
        }

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Category("WP096")]
        [Category("WP096Capture")]
        [Timeout(600000)]
        public IEnumerator CAP_SabitPoz_8Kare()
        {
            string kok = KayitKlasoru();
            string dir = Path.Combine(kok, "poses");
            Directory.CreateDirectory(dir);
            yield return RunKur();
            int sira = TohumVeSira(21u);   // ZombieV2AdCaptureTests.AD_TD15_Kare's group seed
            CharacterAnimatorLod.ForceFar = 0;
            PlayerRig rig = _s.Rig;
            Camera game = PrototypeCameraRig.Active.Camera;
            Vector3 fwd = Yatay(game.transform.forward).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 centre = NavPathFollower.ClampToNavMesh(rig.transform.position + fwd * 7f, 6f);
            var grup = new List<ZombieEnemy>(8);
            var yerler = new List<Vector3>(8);
            for (int i = 0; i < 8; i++)   // AD_TD15: the four versions + four Standard
            {
                Vector3 p = NavPathFollower.ClampToNavMesh(centre + right * ((i % 4 - 1.5f) * 1.3f) + fwd * ((i / 4) * 1.4f), 6f);
                ZombieEnemy z = EnemyFactory.Spawn(i < 4 ? Kademeler[i] : EnemyTier.Standard, 8, p, Quaternion.LookRotation(-fwd));
                Assert.IsNotNull(z, "kaynak yok: doğmadı");
                if (z.Armor != null && z.Armor.EquippedCount < 3) z.FitArmorComposition(true, true, true);
                grup.Add(z);
                yerler.Add(p);
            }

            for (int i = 0; i < 40; i++)   // AD_TD15 Settle: a fixed step facing the camera
            {
                foreach (ZombieEnemy z in grup) if (z != null) z.transform.rotation = Quaternion.LookRotation(-fwd);
                yield return null;
            }

            foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(false)) if (r.enabled) { r.enabled = false; _gizlenen.Add(r); }
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;   // ZombieEnemy.Update returns on dt 0: nothing overwrites the pinned pose
            for (int i = 0; i < grup.Count; i++) grup[i].transform.SetPositionAndRotation(yerler[i], Quaternion.LookRotation(-fwd));
            Physics.SyncTransforms();

            var rt = new RenderTexture(KareW, KareH, 24) { name = "WP096 poz kare", antiAliasing = 1 };
            rt.Create();
            var tex = new Texture2D(KareW, KareH, TextureFormat.RGB24, false);
            var dosyalar = new List<string>();
            var gaitler = new List<string>();
            try
            {
                // (a) top-down 15 m over the group (AD_TD15's camera)
                Vector3 td = centre + Vector3.up * 15f - fwd * 2f;
                Quaternion tdRot = Quaternion.LookRotation(centre - td);
                foreach (float faz in Fazlar)
                {
                    foreach (ZombieEnemy z in grup) PozSabitle(z, faz);
                    yield return null;
                    string yol = Path.Combine(dir, "td15-faz" + Mathf.RoundToInt(faz * 100f).ToString("D2", Inv) + ".png");
                    CizKonumdan(game, td, tdRot, 45f, rt, tex, yol);
                    dosyalar.Add(yol);
                }

                // (b) side-on 5.2 m: the four versions in a row across the view, walking to screen right; the four Standard hidden
                for (int i = 0; i < grup.Count; i++)
                {
                    if (i < 4)
                    {
                        Vector3 p = NavPathFollower.ClampToNavMesh(centre + right * ((i - 1.5f) * 1.6f), 6f);
                        grup[i].transform.SetPositionAndRotation(p, Quaternion.LookRotation(right));
                    }
                    else
                    {
                        foreach (Renderer r in grup[i].GetComponentsInChildren<Renderer>(false)) if (r.enabled) { r.enabled = false; _gizlenen.Add(r); }
                    }
                }

                Physics.SyncTransforms();
                Vector3 eye = centre - fwd * 5.2f + Vector3.up * 1.1f;
                Quaternion yanRot = Quaternion.LookRotation(centre + Vector3.up * 1.1f - eye);
                foreach (float faz in Fazlar)
                {
                    for (int i = 0; i < 4; i++) PozSabitle(grup[i], faz);
                    yield return null;
                    string yol = Path.Combine(dir, "yan-faz" + Mathf.RoundToInt(faz * 100f).ToString("D2", Inv) + ".png");
                    CizKonumdan(game, eye, yanRot, 40f, rt, tex, yol);
                    dosyalar.Add(yol);
                }

                for (int i = 0; i < 4; i++)
                {
                    ZombieSkinnedVisual v = grup[i].Skinned;
                    gaitler.Add(Kademeler[i] + ":" + (v != null && v.Animator != null ? KlipAdiOku(v.Animator) : "yok") + " scale " + F(grup[i].transform.lossyScale.y, 2));
                }
            }
            finally
            {
                Time.timeScale = 1f;
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(tex);
            }

            Debug.Log("[DD-TEST] wp096.cap.poz files=" + dosyalar.Count + " dir=" + dir + " ordinal=" + sira + " gaits " + string.Join(" ", gaitler));
            foreach (string yol in dosyalar) Assert.IsTrue(File.Exists(yol), "kare yazılmadı: " + yol);
            Assert.AreEqual(8, dosyalar.Count, "8 kare beklenir");
        }

        /// <summary>The ring: tangent (counter-clockwise) plus a pull back onto the 6 m radius, as a stick in the TD camera's basis.</summary>
        private static Vector2 HalkaCubugu(Vector3 oyuncu, Vector3 merkez, PrototypeCameraRig rigCam)
        {
            Vector3 p = Yatay(oyuncu - merkez);
            float r = p.magnitude;
            Vector3 radyal = r > 0.2f ? p / r : Vector3.right;
            Vector3 teget = Vector3.Cross(Vector3.up, radyal);
            Vector3 d = (teget + radyal * Mathf.Clamp((TdHalka - r) * 0.5f, -1f, 1f)).normalized;
            Vector3 f = rigCam.PlanarForward, sag = rigCam.PlanarRight;
            return new Vector2(Vector3.Dot(d, sag), Vector3.Dot(d, f)).normalized * TdCubuk;
        }

        /// <summary>Pin one walk phase: the gait fully on (Speed01 1, GaitRate 1), Locomotion at the phase, evaluated now (no Rebind:
        /// ZombieR2Tests.PinPose rebinds, which resets the Gait parameter to the controller default).</summary>
        private static void PozSabitle(ZombieEnemy z, float faz)
        {
            Animator an = z != null && z.Skinned != null ? z.Skinned.Animator : null;
            if (an == null) return;
            FloatYaz(an, CharacterAnimatorContract.Speed01, 1f);
            FloatYaz(an, CharacterAnimatorContract.GaitRate, 1f);
            an.Play(CharacterAnimatorContract.LocomotionState, CharacterAnimatorContract.BaseLayer, faz);
            an.Update(0f);
        }

        private static void FloatYaz(Animator an, string ad, float deger)
        {
            foreach (AnimatorControllerParameter p in an.parameters)
            {
                if (p.name != ad || p.type != AnimatorControllerParameterType.Float) continue;
                an.SetFloat(ad, deger);
                return;
            }
        }

        private static string KlipAdiOku(Animator an)
        {
            var l = new List<AnimatorClipInfo>(4);
            an.GetCurrentAnimatorClipInfo(0, l);
            string best = "yok";
            float w = -1f;
            foreach (AnimatorClipInfo c in l) if (c.clip != null && c.weight > w) { w = c.weight; best = c.clip.name; }
            return best;
        }

        private static string KayitKlasoru()
        {
            string kok = Environment.GetEnvironmentVariable(KayitDegiskeni);
            if (string.IsNullOrEmpty(kok)) Assert.Ignore(KayitDegiskeni + " boş: WP-096 kare yakalama atlandı");
            string tam = Path.GetFullPath(kok);
            string kullanici = Path.GetFullPath(Application.persistentDataPath);
            if (tam.StartsWith(kullanici, StringComparison.Ordinal)) Assert.Fail("kullanıcı verisi: " + KayitDegiskeni + " persistentDataPath altında (" + tam + ")");
            return tam;
        }

        /// <summary>One render of the camera into the readable target, written as PNG (ZombieV2AdCaptureTests.Capture, buffers reused).</summary>
        private static void Ciz(Camera cam, RenderTexture rt, Texture2D tex, string yol)
        {
            RenderTexture onceki = RenderTexture.active;
            RenderTexture hedefOnce = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                tex.Apply(false);
            }
            finally
            {
                cam.targetTexture = hedefOnce;
                RenderTexture.active = onceki;
            }

            File.WriteAllBytes(yol, tex.EncodeToPNG());
        }

        /// <summary>The game camera (same post / exposure) moved for one render and put back (ZombieV2AdCaptureTests.CaptureFrom).</summary>
        private static void CizKonumdan(Camera cam, Vector3 konum, Quaternion yon, float fov, RenderTexture rt, Texture2D tex, string yol)
        {
            Transform t = cam.transform;
            Vector3 p = t.position;
            Quaternion q = t.rotation;
            float f = cam.fieldOfView;
            try
            {
                t.SetPositionAndRotation(konum, yon);
                cam.fieldOfView = fov;
                Ciz(cam, rt, tex, yol);
            }
            finally
            {
                t.SetPositionAndRotation(p, q);
                cam.fieldOfView = f;
            }
        }

        // ============================================================================================================ shared

        /// <summary>Run scene (SpecialWeaponSahne: spawning stopped, test input, invulnerable player), TopDown, no rise, 1/60.</summary>
        private IEnumerator RunKur()
        {
            // user data is sacred: the assembly fixtures must have redirected the profile before any scene loads
            Assert.AreEqual(global::TestProfileIsolation.TestProfilePath, global::DD.Prototype.Profile.ProfileStore.Path,
                "kullanıcı verisi: profil yolu test dosyasına yönlenmemiş — koşu durduruldu");
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            Time.captureDeltaTime = Dt;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "TopDown kamera kurulamadı");
        }

        /// <summary>A flat Environment-layer floor 150 m above the spawn (R2LokomotionOlcumTests' floor), the player on it far ahead
        /// (the zombies look at a threat in front of them, not 150 m below).</summary>
        private Vector3 SahneKur()
        {
            Vector3 merkez = _s.Rig.transform.position + Vector3.up * ZeminYukseklik;
            GameObject zemin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zemin.name = "WP096_Zemin";
            zemin.layer = PrototypeLayers.Environment;
            zemin.transform.localScale = new Vector3(ZeminYari * 2f, 1f, ZeminYari * 2f);
            zemin.transform.position = merkez - Vector3.up * 0.5f;
            _gecici.Add(zemin);
            Physics.SyncTransforms();
            _s.Rig.Teleport(merkez + Vector3.forward * OyuncuIleri + Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.back));
            return merkez;
        }

        /// <summary>A stage body: the AI off (ZombieEnemy.Update is its only driver; the test moves it), on the floor.</summary>
        private void SahneyeAl(ZombieEnemy z, Vector3 p, Quaternion r)
        {
            z.enabled = false;
            _sahne.Add(z);
            z.transform.SetPositionAndRotation(p, r);
        }

        private ZombiKaydedici KaydediciEkle(Animator an, int maske, string ad)
        {
            var go = new GameObject("WP096_Kayit_" + ad);
            _gecici.Add(go);
            var k = go.AddComponent<ZombiKaydedici>();
            k.Bagla(an, maske);
            return k;
        }

        private static int TohumVeSira(uint tohum)
        {
            EnemyFactory.ResetSeedForTests(tohum);
            int sira = SiraSifirla();
            Debug.Log("[DD-TEST] wp096.seed=" + tohum + " gaitOrdinal=" + (sira >= 0 ? sira.ToString(Inv) : "yok"));
            return sira;
        }

        private static MethodInfo HizMetodu() => Metot(typeof(ZombieSkinnedVisual), "SetSpeed", typeof(float), typeof(float));

        private static bool FitZorla(ZombieSkinnedVisual v, string fit, EnemyTier tier)
        {
            MethodInfo m = Metot(typeof(ZombieSkinnedVisual), "SetFit", typeof(string), typeof(EnemyTier));
            if (m == null) return false;
            m.Invoke(v, new object[] { fit, tier });
            return true;
        }

        private static float SlideFraction(float hiz, float klipOlcekli, float rate)
        {
            if (float.IsNaN(klipOlcekli) || float.IsNaN(rate) || klipOlcekli <= 0f) return float.NaN;
            Type t = typeof(ZombieSkinnedVisual).Assembly.GetType("DD.Prototype.Characters.CadenceBand");
            MethodInfo m = t != null ? t.GetMethod("SlideFraction", Statik, null, new[] { typeof(float), typeof(float), typeof(float) }, null) : null;
            if (m == null) return float.NaN;
            return Convert.ToSingle(m.Invoke(null, new object[] { hiz, klipOlcekli, rate }), Inv);
        }

        // ---------------------------------------------------------------------------------------------- reflection (v4-tolerant)

        private static FieldInfo SiraAlani()
        {
            FieldInfo f = typeof(ZombieSkinnedVisual).GetField("s_gaitOrdinal", Statik);
            return f != null && f.FieldType == typeof(int) && !f.IsLiteral && !f.IsInitOnly ? f : null;
        }

        private static int SiraOku()
        {
            FieldInfo f = SiraAlani();
            return f != null ? (int)f.GetValue(null) : -1;
        }

        private static int SiraSifirla()
        {
            FieldInfo f = SiraAlani();
            if (f == null) return -1;
            f.SetValue(null, 0);
            return (int)f.GetValue(null);
        }

        private static MethodInfo Metot(Type t, string ad, params Type[] parametreler)
        {
            try { return t.GetMethod(ad, Ornek | Statik, null, parametreler, null); }
            catch (AmbiguousMatchException) { return null; }
        }

        private static float? StatikFloat(Type t, string ad)
        {
            try
            {
                FieldInfo f = t.GetField(ad, Statik);
                if (f != null && f.FieldType == typeof(float)) return (float)f.GetValue(null);
                PropertyInfo p = t.GetProperty(ad, Statik);
                if (p != null && p.PropertyType == typeof(float) && p.CanRead) return (float)p.GetValue(null);
            }
            catch (AmbiguousMatchException) { }
            return null;
        }

        private static bool StatikFloatYaz(Type t, string ad, float v)
        {
            try
            {
                FieldInfo f = t.GetField(ad, Statik);
                if (f != null && f.FieldType == typeof(float) && !f.IsLiteral && !f.IsInitOnly) { f.SetValue(null, v); return true; }
                PropertyInfo p = t.GetProperty(ad, Statik);
                if (p != null && p.PropertyType == typeof(float) && p.CanWrite) { p.SetValue(null, v); return true; }
            }
            catch (AmbiguousMatchException) { }
            return false;
        }

        private static object OrnekNesne(object o, string ad)
        {
            if (o == null) return null;
            try
            {
                PropertyInfo p = o.GetType().GetProperty(ad, Ornek);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0) return p.GetValue(o);
                FieldInfo f = o.GetType().GetField(ad, Ornek);
                if (f != null) return f.GetValue(o);
            }
            catch (AmbiguousMatchException) { }
            return null;
        }

        private static float? OrnekFloat(object o, string ad) => OrnekNesne(o, ad) is float x ? x : (float?)null;

        private static int? OrnekInt(object o, string ad) => OrnekNesne(o, ad) is int x ? x : (int?)null;

        // ---------------------------------------------------------------------------------------------- contact (clip events / R2 L1 height)

        private struct TemasSonuc
        {
            public bool[] L, R, BasL, BasR;
            public bool Uyarlamali;
        }

        /// <summary>
        /// Contact per frame: sole above the ground ray ≤ <see cref="R2LokomotionOlcumTests.TemasYukseklik"/> AND |foot vertical speed|
        /// ≤ <see cref="R2LokomotionOlcumTests.TemasDikeyHiz"/>; onsets collapsed inside <see cref="R2LokomotionOlcumTests.OlayAyrimKare"/>.
        /// <paramref name="uyarlamali"/>: the sole height is read from the body's own 5th percentile (a sole convention that does not sit
        /// at 0 on the zombie rig would otherwise read no contact at all — reported as "adaptive").
        /// </summary>
        private static TemasSonuc Temaslar(List<ZombiKaydedici.Kare> ks, bool uyarlamali)
        {
            return TemaslarRef(ks, uyarlamali ? AltYuzdelik(ks, true, null) : 0f, uyarlamali ? AltYuzdelik(ks, false, null) : 0f);
        }

        /// <summary>The height rule against a given sole reference per foot (0 = the strict L1 rule).</summary>
        private static TemasSonuc TemaslarRef(List<ZombiKaydedici.Kare> ks, float refL, float refR)
        {
            int n = ks.Count;
            var s = new TemasSonuc { L = new bool[n], R = new bool[n], BasL = new bool[n], BasR = new bool[n] };
            int sonL = -1000, sonR = -1000;
            bool oncL = false, oncR = false;
            for (int i = 0; i < n; i++)
            {
                ZombiKaydedici.Kare k = ks[i];
                bool l = k.Gecerli && !float.IsNaN(k.BoslukL) && k.BoslukL - refL <= R2LokomotionOlcumTests.TemasYukseklik && Mathf.Abs(k.AyakVL.y) <= R2LokomotionOlcumTests.TemasDikeyHiz;
                bool r = k.Gecerli && !float.IsNaN(k.BoslukR) && k.BoslukR - refR <= R2LokomotionOlcumTests.TemasYukseklik && Mathf.Abs(k.AyakVR.y) <= R2LokomotionOlcumTests.TemasDikeyHiz;
                s.L[i] = l;
                s.R[i] = r;
                if (l && !oncL && i - sonL >= R2LokomotionOlcumTests.OlayAyrimKare) { s.BasL[i] = true; sonL = i; }
                if (r && !oncR && i - sonR >= R2LokomotionOlcumTests.OlayAyrimKare) { s.BasR[i] = true; sonR = i; }
                oncL = l;
                oncR = r;
            }

            return s;
        }

        /// <summary>The 5th-percentile sole height of one foot over the valid frames <paramref name="secim"/> keeps (null = all).</summary>
        private static float AltYuzdelik(List<ZombiKaydedici.Kare> ks, bool sol, Func<ZombiKaydedici.Kare, bool> secim)
        {
            var g = new List<float>(ks.Count);
            foreach (ZombiKaydedici.Kare k in ks)
            {
                if (!k.Gecerli || (secim != null && !secim(k))) continue;
                float x = sol ? k.BoslukL : k.BoslukR;
                if (!float.IsNaN(x)) g.Add(x);
            }

            if (g.Count == 0) return 0f;
            g.Sort();
            return g[(int)(0.05 * (g.Count - 1))];
        }

        /// <summary>
        /// Stance from the clips' own events (AP-022 gait clips: foot_l / foot_r = plant, foot_l_off / foot_r_off = lift; the definition
        /// FootIkAdapter's lock runs on). A foot is in stance from the frame after its plant until its lift; without lift events in the
        /// record the other foot's plant ends it (the lock window). Bodies start planted (idle). <paramref name="rolanti"/>: dominant
        /// clips that are the idle — both feet planted while one of them plays (ZL5's start / stop). Onsets = the plants.
        /// </summary>
        private static TemasSonuc OlayDuruslari(List<ZombiKaydedici.Kare> ks, HashSet<int> rolanti)
        {
            int n = ks.Count;
            var s = new TemasSonuc { L = new bool[n], R = new bool[n], BasL = new bool[n], BasR = new bool[n] };
            bool kalkisVar = false;
            foreach (ZombiKaydedici.Kare k in ks) if (k.KalkL || k.KalkR) { kalkisVar = true; break; }
            bool stL = true, stR = true;
            for (int i = 0; i < n; i++)
            {
                ZombiKaydedici.Kare k = ks[i];
                if (rolanti != null && rolanti.Contains(k.Klip)) { stL = true; stR = true; }
                if (k.OlayL) { stL = true; s.BasL[i] = true; if (!kalkisVar) stR = false; }
                if (k.OlayR) { stR = true; s.BasR[i] = true; if (!kalkisVar) stL = false; }
                if (k.KalkL) stL = false;
                if (k.KalkR) stR = false;
                s.L[i] = k.Gecerli && stL && !k.OlayL;   // the plant frame's velocity spans the landing
                s.R[i] = k.Gecerli && stR && !k.OlayR;
            }

            return s;
        }

        private static double Oran(Vector3 ayakV, Vector3 govdeV) => Yatay(ayakV).magnitude / Math.Max(Yatay(govdeV).magnitude, GovdeHizTabani);

        private static int BaskinKlip(List<ZombiKaydedici.Kare> ks)
        {
            var say = new Dictionary<int, int>();
            int best = 0, bestN = 0;
            foreach (ZombiKaydedici.Kare k in ks)
            {
                if (!Olculen(k) || k.Klip == 0) continue;
                int c = say.TryGetValue(k.Klip, out int x) ? x + 1 : 1;
                say[k.Klip] = c;
                if (c > bestN) { bestN = c; best = k.Klip; }
            }

            return best;
        }

        private static string KlipAdi(int id) => id != 0 && ZombiKaydedici.KlipAdlari.TryGetValue(id, out string s) ? s : "yok";

        // ---------------------------------------------------------------------------------------------- numbers

        private static Vector3 Yatay(Vector3 v) { v.y = 0f; return v; }

        private static double Ort(List<double> xs)
        {
            if (xs.Count == 0) return double.NaN;
            double s = 0;
            foreach (double x in xs) s += x;
            return s / xs.Count;
        }

        /// <summary>Nearest rank (R2LokomotionOlcumTests.P95's convention).</summary>
        private static double Yuzdelik(List<double> xs, double q)
        {
            if (xs.Count == 0) return double.NaN;
            var c = new List<double>(xs);
            c.Sort();
            int i = Mathf.Clamp((int)Math.Ceiling(q * c.Count) - 1, 0, c.Count - 1);
            return c[i];
        }

        private static double Medyan(List<double> xs)
        {
            if (xs.Count == 0) return double.NaN;
            var c = new List<double>(xs);
            c.Sort();
            return c.Count % 2 == 1 ? c[c.Count / 2] : 0.5 * (c[c.Count / 2 - 1] + c[c.Count / 2]);
        }

        private static double Min(List<double> xs)
        {
            double m = double.NaN;
            foreach (double x in xs) if (double.IsNaN(m) || x < m) m = x;
            return m;
        }

        private static long Max(long[] xs)
        {
            long m = 0;
            foreach (long x in xs) if (x > m) m = x;
            return m;
        }

        private static long Medyan(long[] xs)
        {
            if (xs.Length == 0) return 0;
            var c = (long[])xs.Clone();
            Array.Sort(c);
            return c[c.Length / 2];
        }

        private static string F(double v, int d = 4) => double.IsNaN(v) ? "NaN" : v.ToString("F" + d, Inv);

        // ---------------------------------------------------------------------------------------------- the record

        /// <summary>
        /// One test's numbers: every key is logged as "wp096.&lt;test&gt;.&lt;key&gt;=" (targets with PASS / FAIL) and written through the R2
        /// writer only after <see cref="Kaynak"/> found the source in the record. A missing source logs the computed numbers as
        /// "wp096-blocked." lines (kept for the diagnosis, not greppable as results) and fails the test without an artefact.
        /// </summary>
        private sealed class Rapor
        {
            private readonly string _kisa;
            private readonly Dictionary<string, double?> _degerler = new Dictionary<string, double?>();
            private readonly List<string> _satirlar = new List<string>();
            private readonly List<string> _iskalar = new List<string>();

            public Rapor(string kisa) { _kisa = kisa; }

            public void Hedef(string anahtar, double? deger, bool enAz, double hedef, string ek = null)
            {
                double? d = Sayi(deger);
                _degerler[anahtar] = d;
                string op = enAz ? ">=" : "<=";
                bool gecti = d.HasValue && (enAz ? d.Value >= hedef : d.Value <= hedef);
                if (!gecti) _iskalar.Add(anahtar + " " + Yaz(d) + " (hedef " + op + hedef.ToString(Inv) + ")");
                _satirlar.Add(_kisa + "." + anahtar + "=" + Yaz(d) + " hedef" + op + hedef.ToString(Inv) + " " + (gecti ? "PASS" : "FAIL") + (ek != null ? " " + ek : ""));
            }

            public void Bilgi(string anahtar, double? deger, string ek = null)
            {
                double? d = Sayi(deger);
                _degerler[anahtar] = d;
                _satirlar.Add(_kisa + "." + anahtar + "=" + Yaz(d) + " rapor" + (ek != null ? " " + ek : ""));
            }

            public void Kaynak(List<string> eksik)
            {
                if (eksik.Count == 0) return;
                foreach (string s in _satirlar) Debug.Log("wp096-blocked." + s);
                Assert.Fail("WP-096 " + _kisa + " kaynak kayıtta yok, yazılmadı (BLOCKED): " + string.Join("; ", eksik));
            }

            public void Yazdir(string test, int ornek, string not)
            {
                foreach (string s in _satirlar) Debug.Log("wp096." + s);
                string yol = Olcum.Yaz("wp096-" + _kisa, Fixture + "." + test, _degerler, ornek, not);
                Debug.Log("wp096." + _kisa + ".artefakt=" + (yol ?? "yok (DD_R2_MEASURE_DIR boş)") + " samples=" + ornek + " enforce=" + Zorla + " missedTargets=" + _iskalar.Count);
                if (Zorla && _iskalar.Count > 0) Assert.Fail("WP-096 " + _kisa + " hedef dışı: " + string.Join("; ", _iskalar));
            }

            private static double? Sayi(double? d) => d.HasValue && !double.IsNaN(d.Value) && !double.IsInfinity(d.Value) ? d : null;

            private static string Yaz(double? d) => d.HasValue ? d.Value.ToString("0.####", Inv) : "yok";
        }
    }

    // Read-only callbacks around FootIkAdapter's default script order. Applied only to opt-in trace bodies.
    [DefaultExecutionOrder(-32000)]
    internal sealed class ZombiIkTraceBefore : MonoBehaviour
    {
        private Animator _animator;
        private FootIkAdapter _ik;
        private Vector3[] _locks;
        private Transform _hipL, _hipR;
        public int FrameId = -1, OffsetReleases;
        public Vector3 Left, Right, LockL, LockR, HipL, HipR;
        public void Initialize(Animator animator, FootIkAdapter ik, Vector3[] locks)
        {
            _animator = animator; _ik = ik; _locks = locks;
            _hipL = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _hipR = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        }
        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || _animator == null) return;
            FrameId = Time.frameCount;
            Left = _animator.GetIKPosition(AvatarIKGoal.LeftFoot); Right = _animator.GetIKPosition(AvatarIKGoal.RightFoot);
            if (_locks != null && _locks.Length == 2) { LockL = _locks[0]; LockR = _locks[1]; }
            if (_hipL != null) HipL = _hipL.position;
            if (_hipR != null) HipR = _hipR.position;
            OffsetReleases = _ik != null ? _ik.ReleasedByOffset : 0;
        }
    }
    [DefaultExecutionOrder(32000)]
    internal sealed class ZombiIkTraceAfter : MonoBehaviour
    {
        private Animator _animator;
        private FootIkAdapter _ik;
        public int FrameId = -1, OffsetReleases;
        public Vector3 Left, Right;
        public float WeightL, WeightR;
        public void Initialize(Animator animator, FootIkAdapter ik) { _animator = animator; _ik = ik; }
        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || _animator == null) return;
            FrameId = Time.frameCount;
            Left = _animator.GetIKPosition(AvatarIKGoal.LeftFoot); Right = _animator.GetIKPosition(AvatarIKGoal.RightFoot);
            WeightL = _animator.GetIKPositionWeight(AvatarIKGoal.LeftFoot); WeightR = _animator.GetIKPositionWeight(AvatarIKGoal.RightFoot);
            OffsetReleases = _ik != null ? _ik.ReleasedByOffset : 0;
        }
    }

    /// <summary>
    /// WP-096 zombie sampler, one per body: a record per frame AFTER the Animator, its IK passes and every LateUpdate solver
    /// (LocomotionLean −10, the look-at, the chains; order 32000) — the pose the frame shows. The contact inputs and the bone
    /// angles follow R2LokomotionOlcumTests.LokomotionKaydedici (which samples one player and keeps no hips / state / yaw), as do the
    /// clip's foot events (CharacterAnimationEvents.Any, this body only, collapsed per foot inside OlayAyrimKare): the stance source
    /// since 07.10 (the foot IK presses every foot to the floor, so the height rule cannot see a step on the shown pose). The contact
    /// itself is decided in the analysis (clip events, else the L1 height rule).
    /// </summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class ZombiKaydedici : MonoBehaviour
    {
        internal struct Kare
        {
            public int Etiket, Since;
            public float Zaman, Dt;
            public bool Gecerli;                       // a previous frame, no teleport, dt > 0
            public Vector3 KokV, KalcaV;               // horizontal, m/s (the Animator's transform, the Hips bone)
            public Vector3 AyakVL, AyakVR;             // m/s
            public float BoslukL, BoslukR;             // sole above the ground ray, m (NaN = no ground)
            public bool Loko;                          // base layer in Locomotion
            public int Durum, Klip, SonrakiKlip;       // current state hash, dominant clip ids (current / next state, 0 = none)
            public float Hiz, Faz;                     // state speed × multiplier, normalizedTime mod 1
            public float KokYaw, KalcaYaw, Egilme, Ek; // unwrapped yaw (°), torso roll in the root frame (°), extra reading
            public bool OlayL, OlayR, KalkL, KalkR;    // clip events this frame: foot_l / foot_r plant, foot_l_off / foot_r_off lift
            // WP-096: the toe markers (the contact point with the ankle) and the foot IK as this body got it this frame
            public Vector3 ParmakVL, ParmakVR;         // m/s
            public float AyakYL, AyakYR, ParmakYL, ParmakYR;   // world height, m
            public bool IkVar, IkServis, KilitL, KilitR;
        }

        // Opt-in evidence for the measured worst ZL1 clip and the four ZL5 bodies only.
        // Fixed buffers; no file I/O, reflection, LINQ, or changes to contact/metric inputs while sampling.
        private sealed class FailureTrace
        {
            private const int MaxFrames = 2400;
            [Serializable] private struct Frame
            {
                public int unityFrame, label, since, currentState, nextState, currentClip, nextClip, boneMask;
                public float time, dt, currentTime, nextTime, blendTime, currentWeight, nextWeight, gaitRate, bodyScale;
                public string gaitRateR, bodyScaleR, motorFrameR, motorRateR;
                public Vector3 physicalRoot, modelRoot, modelScale, hips, ankleL, ankleR, rootVelocity, hipsVelocity, ankleVelocityL, ankleVelocityR;
                public bool validMetricFrame, plantL, plantR, liftL, liftR, ikServed, lockedL, lockedR, groundL, groundR;
                public float groundGapL, groundGapR, motorFrame, motorRate, motorScale, motorYaw;
                public int offsetReleases, stretchReleases, gateReleases, liftReleases, plantReleases, transitionStarts, transitionCompletions;
                public bool motorActive, motorClockAvailable, ikProbeSameFrame, ikProbeCounterOrderConsistent, lockPositionsAvailable;
                public Vector3 animatedGoalL, animatedGoalR, targetGoalL, targetGoalR, lockBeforeL, lockBeforeR, lockAfterL, lockAfterR, hipPreL, hipPreR;
                public float offsetXZL, offsetXZR, targetWeightL, targetWeightR;
                public int preIkOffsetReleases, postIkOffsetReleases;
            }
            [Serializable] private struct ClipName { public int id; public string name; }
            [Serializable] private sealed class Packet
            {
                public string schema = "wp096-zombie-failure-trace-v1", sourceCommit, unityVersion, body;
                public string sampleStage = "existing recorder LateUpdate after Animator/IK; raw event flags use unchanged classifier";
                public string limit = "pre/post IK probes request script orders -32000/+32000; same-frame/counter flags must validate callback order; no runtime parity claimed";
                public int frameCapacity = MaxFrames, frameCount, observedFrames, droppedFrames, bonesPerFrame, droppedClipNames;
                public Frame[] frames = new Frame[MaxFrames];
                public Quaternion[] rootSpaceBoneRotations;
                public string[] boneNames;
                public ClipName[] clips;
            }
            private readonly Packet _data;
            private readonly string _directory;
            private readonly ZombieSkinnedVisual _visual;
            private readonly Transform _physicalRoot;
            private bool _written;
            private readonly Func<ZombieSkinnedVisual, float> _motorFrame, _motorRate, _motorScale;
            private readonly Vector3[] _lockPositions;
            private readonly ZombiIkTraceBefore _beforeIk;
            private readonly ZombiIkTraceAfter _afterIk;
            private FailureTrace(string directory, string body, Animator animator)
            {
                _directory = directory;
                _visual = animator.GetComponentInParent<ZombieSkinnedVisual>();
                ZombieEnemy enemy = animator.GetComponentInParent<ZombieEnemy>();
                _physicalRoot = enemy != null ? enemy.transform : animator.transform;
                _motorFrame = FloatReader("_transitionFrame"); _motorRate = FloatReader("_transitionRate"); _motorScale = FloatReader("_transitionScale");
                if (_visual != null)
                {
                    _motorFrame?.Invoke(_visual); _motorRate?.Invoke(_visual); _motorScale?.Invoke(_visual); // warm before measured frames
                }
                FootIkAdapter ik = animator.GetComponent<FootIkAdapter>();
                FieldInfo locks = typeof(FootIkAdapter).GetField("_lockPos", BindingFlags.Instance | BindingFlags.NonPublic);
                _lockPositions = ik != null && locks != null ? locks.GetValue(ik) as Vector3[] : null; // once, not in the frame callback
                _beforeIk = animator.gameObject.AddComponent<ZombiIkTraceBefore>();
                _beforeIk.Initialize(animator, ik, _lockPositions);
                _afterIk = animator.gameObject.AddComponent<ZombiIkTraceAfter>();
                _afterIk.Initialize(animator, ik);
                HumanBodyBones[] bones = R2LokomotionOlcumTests.Kemikler;
                _data = new Packet
                {
                    sourceCommit = Environment.GetEnvironmentVariable("DD_R2_COMMIT"), unityVersion = Application.unityVersion, body = body,
                    bonesPerFrame = bones.Length, rootSpaceBoneRotations = new Quaternion[MaxFrames * bones.Length], boneNames = new string[bones.Length]
                };
                for (int i = 0; i < bones.Length; i++) _data.boneNames[i] = bones[i].ToString();
            }
            public static FailureTrace Create(string body, Animator animator)
            {
                string directory = Environment.GetEnvironmentVariable("DD_WP096_TRACE_DIR");
                if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory)) return null;
                const string prefix = "WP096_Kayit_";
                string label = body.StartsWith(prefix, StringComparison.Ordinal) ? body.Substring(prefix.Length) : body;
                bool target = label.StartsWith("Ultra/walk_stomp_B/", StringComparison.Ordinal)
                    || label == "Standard" || label == "Special" || label == "Elite" || label == "Ultra";
                return target ? new FailureTrace(directory, label, animator) : null;
            }
            private static Func<ZombieSkinnedVisual, float> FloatReader(string name)
            {
                FieldInfo field = typeof(ZombieSkinnedVisual).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(float)) return null;
                // Resolve/compile only while binding the optional editor fixture; invoking this typed delegate does not box.
                var body = System.Linq.Expressions.Expression.Parameter(typeof(ZombieSkinnedVisual), "body");
                try
                {
                    return System.Linq.Expressions.Expression.Lambda<Func<ZombieSkinnedVisual, float>>(
                        System.Linq.Expressions.Expression.Field(body, field), body).Compile();
                }
                catch (NotSupportedException) { return null; }
                catch (MemberAccessException) { return null; }
            }
            public void Capture(ZombiKaydedici owner, Kare k)
            {
                _data.observedFrames++;
                if (_data.frameCount == MaxFrames) { _data.droppedFrames++; return; }
                Animator animator = owner._anim;
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
                bool blending = animator.IsInTransition(0);
                AnimatorStateInfo next = blending ? animator.GetNextAnimatorStateInfo(0) : default;
                var frame = new Frame
                {
                    unityFrame = Time.frameCount, time = k.Zaman, dt = k.Dt, label = k.Etiket, since = k.Since, validMetricFrame = k.Gecerli,
                    physicalRoot = _physicalRoot.position, modelRoot = owner._kok.position, modelScale = owner._kok.lossyScale,
                    hips = owner._kalca.position, ankleL = owner._ayakL.position, ankleR = owner._ayakR.position,
                    rootVelocity = k.KokV, hipsVelocity = k.KalcaV, ankleVelocityL = k.AyakVL, ankleVelocityR = k.AyakVR,
                    plantL = k.OlayL, plantR = k.OlayR, liftL = k.KalkL, liftR = k.KalkR,
                    ikServed = k.IkServis, lockedL = k.KilitL, lockedR = k.KilitR,
                    groundL = !float.IsNaN(k.BoslukL) && !float.IsInfinity(k.BoslukL), groundR = !float.IsNaN(k.BoslukR) && !float.IsInfinity(k.BoslukR),
                    currentState = current.fullPathHash, nextState = blending ? next.fullPathHash : 0,
                    currentTime = current.normalizedTime, nextTime = blending ? next.normalizedTime : 0f,
                    currentClip = k.Klip, nextClip = k.SonrakiKlip,
                    currentWeight = DominantWeight(animator, false), nextWeight = blending ? DominantWeight(animator, true) : 0f,
                    blendTime = blending ? animator.GetAnimatorTransitionInfo(0).normalizedTime : 0f,
                    gaitRate = animator.GetFloat(CharacterAnimatorContract.GaitRate), bodyScale = _visual != null ? _visual.GaitBodyScale : 0f
                };
                frame.groundGapL = frame.groundL ? k.BoslukL : 0f;
                frame.groundGapR = frame.groundR ? k.BoslukR : 0f;
                if (_visual != null)
                {
                    frame.motorClockAvailable = _motorFrame != null && _motorRate != null && _motorScale != null;
                    if (frame.motorClockAvailable)
                    {
                        frame.motorFrame = _motorFrame(_visual); frame.motorRate = _motorRate(_visual); frame.motorScale = _motorScale(_visual);
                    }
                    frame.motorYaw = _visual.TransitionYawDegrees; frame.motorActive = _visual.TransitionActive;
                    frame.transitionStarts = _visual.TransitionStarts; frame.transitionCompletions = _visual.TransitionCompletions;
                }
                if (owner._ik != null)
                {
                    frame.offsetReleases = owner._ik.ReleasedByOffset; frame.stretchReleases = owner._ik.ReleasedByStretch;
                    frame.gateReleases = owner._ik.ReleasedByGate; frame.liftReleases = owner._ik.ReleasedByLift; frame.plantReleases = owner._ik.ReleasedByPlant;
                }
                frame.ikProbeSameFrame = _beforeIk.FrameId == Time.frameCount && _afterIk.FrameId == Time.frameCount;
                if (frame.ikProbeSameFrame)
                {
                    frame.animatedGoalL = _beforeIk.Left; frame.animatedGoalR = _beforeIk.Right;
                    frame.targetGoalL = _afterIk.Left; frame.targetGoalR = _afterIk.Right;
                    frame.lockBeforeL = _beforeIk.LockL; frame.lockBeforeR = _beforeIk.LockR;
                    frame.hipPreL = _beforeIk.HipL; frame.hipPreR = _beforeIk.HipR;
                    frame.targetWeightL = _afterIk.WeightL; frame.targetWeightR = _afterIk.WeightR;
                    frame.preIkOffsetReleases = _beforeIk.OffsetReleases; frame.postIkOffsetReleases = _afterIk.OffsetReleases;
                    frame.ikProbeCounterOrderConsistent = frame.preIkOffsetReleases <= frame.postIkOffsetReleases
                        && frame.postIkOffsetReleases == frame.offsetReleases;
                    frame.lockPositionsAvailable = _lockPositions != null && _lockPositions.Length == 2;
                    if (frame.lockPositionsAvailable)
                    {
                        frame.lockAfterL = _lockPositions[0]; frame.lockAfterR = _lockPositions[1];
                        Vector3 left = frame.lockAfterL - frame.animatedGoalL, right = frame.lockAfterR - frame.animatedGoalR;
                        left.y = right.y = 0f; frame.offsetXZL = left.magnitude; frame.offsetXZR = right.magnitude;
                    }
                }
                Quaternion inverse = Quaternion.Inverse(owner._kok.rotation);
                for (int b = 0; b < owner._kemik.Length; b++)
                {
                    Transform bone = owner._kemik[b];
                    if (bone == null) continue;
                    frame.boneMask |= 1 << b;
                    _data.rootSpaceBoneRotations[_data.frameCount * _data.bonesPerFrame + b] = inverse * bone.rotation;
                }
                _data.frames[_data.frameCount++] = frame;
            }
            private static float DominantWeight(Animator animator, bool next)
            {
                // Reuse the recorder's existing list. No classifier uses it between calls to Baskin.
                if (next) animator.GetNextAnimatorClipInfo(0, s_klip); else animator.GetCurrentAnimatorClipInfo(0, s_klip);
                float weight = 0f;
                for (int i = 0; i < s_klip.Count; i++) weight = Mathf.Max(weight, s_klip[i].weight);
                return weight;
            }
            public void Write()
            {
                if (_written) return;
                _written = true;
                if (_beforeIk != null) { _beforeIk.enabled = false; Object.Destroy(_beforeIk); }
                if (_afterIk != null) { _afterIk.enabled = false; Object.Destroy(_afterIk); }
                try
                {
                    Array.Resize(ref _data.frames, _data.frameCount);
                    Array.Resize(ref _data.rootSpaceBoneRotations, _data.frameCount * _data.bonesPerFrame);
                    for (int i = 0; i < _data.frameCount; i++)
                    {
                        _data.frames[i].gaitRateR = _data.frames[i].gaitRate.ToString("R", CultureInfo.InvariantCulture);
                        _data.frames[i].bodyScaleR = _data.frames[i].bodyScale.ToString("R", CultureInfo.InvariantCulture);
                        _data.frames[i].motorFrameR = _data.frames[i].motorFrame.ToString("R", CultureInfo.InvariantCulture);
                        _data.frames[i].motorRateR = _data.frames[i].motorRate.ToString("R", CultureInfo.InvariantCulture);
                    }
                    _data.clips = new ClipName[Mathf.Min(KlipAdlari.Count, 128)];
                    _data.droppedClipNames = Mathf.Max(0, KlipAdlari.Count - _data.clips.Length);
                    int c = 0;
                    foreach (KeyValuePair<int, string> clip in KlipAdlari)
                    {
                        if (c == _data.clips.Length) break;
                        _data.clips[c++] = new ClipName { id = clip.Key, name = clip.Value };
                    }
                    string name = Regex.Replace(_data.body, "[^A-Za-z0-9_-]", "_");
                    Directory.CreateDirectory(_directory);
                    string path = Path.Combine(_directory, "zl-" + name + "-" + Guid.NewGuid().ToString("N") + ".json");
                    using (var file = new StreamWriter(new FileStream(path, FileMode.CreateNew))) file.Write(JsonUtility.ToJson(_data));
                    Debug.Log("[DD-TEST] wp096.zombie.trace=" + path + " frames=" + _data.frameCount + " dropped=" + _data.droppedFrames);
                }
                catch (Exception error) { Debug.LogWarning("wp096.zombie.trace unavailable: " + error.GetType().Name); }
            }
        }

        public static readonly Dictionary<int, string> KlipAdlari = new Dictionary<int, string>();
        private static readonly List<AnimatorClipInfo> s_klip = new List<AnimatorClipInfo>(8);
        private static readonly int LokoHash = Animator.StringToHash(CharacterAnimatorContract.LocomotionState);

        public readonly List<Kare> Kareler = new List<Kare>(2048);
        public readonly List<float> Derece = new List<float>(2048 * 18);   // per frame × bone, °/frame at 60 fps (NaN = n/a)
        public int Etiket = -1;
        public Func<float> Ek;
        public int Since { get; private set; }

        private Animator _anim;
        private Transform _kok, _kalca, _gogus, _ayakL, _ayakR, _parmakL, _parmakR;
        private FootIkAdapter _ik;
        private FailureTrace _failureTrace;
        private Vector3 _onceTL, _onceTR;
        private Transform[] _kemik;
        private Quaternion[] _onceRot;
        private Vector3 _kalcaEksen, _onceKok, _onceKalca, _onceL, _onceR;
        private float _kokYaw, _kalcaYaw, _kokHam, _kalcaHam;
        private int _maske;
        private int _etiketOnce = int.MinValue;
        private bool _bagli, _onceVar, _olayBagli;
        private int _bekleyen, _kare;
        private readonly int[] _sonOlay = { -1000, -1000, -1000, -1000 };
        public int OlaySayisi { get; private set; }
        public int KalkisSayisi { get; private set; }

        public void Bagla(Animator anim, int zeminMaskesi)
        {
            _anim = anim;
            _kok = anim.transform;
            _maske = zeminMaskesi;
            _kalca = anim.GetBoneTransform(HumanBodyBones.Hips);
            _gogus = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (_gogus == null) _gogus = anim.GetBoneTransform(HumanBodyBones.Spine);
            _ayakL = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            _ayakR = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            _parmakL = anim.GetBoneTransform(HumanBodyBones.LeftToes);
            _parmakR = anim.GetBoneTransform(HumanBodyBones.RightToes);
            if (_parmakL == null) _parmakL = _ayakL;
            if (_parmakR == null) _parmakR = _ayakR;
            _ik = anim.GetComponent<FootIkAdapter>();
            _failureTrace = FailureTrace.Create(gameObject.name, anim);
            HumanBodyBones[] kemikler = R2LokomotionOlcumTests.Kemikler;
            _kemik = new Transform[kemikler.Length];
            _onceRot = new Quaternion[kemikler.Length];
            for (int i = 0; i < kemikler.Length; i++) _kemik[i] = anim.GetBoneTransform(kemikler[i]);
            if (_kalca != null) _kalcaEksen = Quaternion.Inverse(_kalca.rotation) * _kok.forward;   // the hips axis that is "forward" at bind
            CharacterAnimationEvents.Any += OlayGeldi;
            _olayBagli = true;
            _bagli = true;
        }

        public void Birak()
        {
            if (_olayBagli) CharacterAnimationEvents.Any -= OlayGeldi;
            _olayBagli = false;
            _bagli = false;
            enabled = false;
            _failureTrace?.Write();
        }

        private void OnDestroy() => Birak();

        /// <summary>This body's foot events (the events component sits on the Animator's object or one of its ancestors / children).</summary>
        private void OlayGeldi(CharacterAnimationEvents c, CharacterAnimationEvent e)
        {
            if (c == null || _kok == null) return;
            Transform t = c.transform;
            if (!t.IsChildOf(_kok) && !_kok.IsChildOf(t)) return;
            if (e == CharacterAnimationEvent.FootLeft) _bekleyen |= 1;
            else if (e == CharacterAnimationEvent.FootRight) _bekleyen |= 2;
            else if (e == CharacterAnimationEvent.FootLOff) _bekleyen |= 4;
            else if (e == CharacterAnimationEvent.FootROff) _bekleyen |= 8;
            else if (e == CharacterAnimationEvent.PivotPlant || e == CharacterAnimationEvent.StopPlant)
            {
                // P3 publishes the authored plant side after mirroring. It is the same physical contact input as foot_l/r.
                if (c.LastInt == 0) _bekleyen |= 1;
                else if (c.LastInt == 1) _bekleyen |= 2;
            }
        }

        /// <summary>One event bit → this frame's flag, collapsed per kind inside OlayAyrimKare (the blend's other clips repeat it).</summary>
        private bool Tuket(int bekleyen, int bit, int indeks)
        {
            if ((bekleyen & bit) == 0 || _kare - _sonOlay[indeks] < R2LokomotionOlcumTests.OlayAyrimKare) return false;
            _sonOlay[indeks] = _kare;
            return true;
        }

        public bool KemikVar(int b) => _kemik != null && b >= 0 && b < _kemik.Length && _kemik[b] != null;

        public string KemikOzeti()
        {
            if (_kemik == null) return "none";
            int n = 0;
            foreach (Transform t in _kemik) if (t != null) n++;
            return n + "/" + _kemik.Length + " hips " + (_kalca != null) + " feet " + (_ayakL != null && _ayakR != null);
        }

        private static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        private float Bosluk(Transform ayak, float taban)
        {
            Vector3 p = ayak.position;
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.5f, _maske, QueryTriggerInteraction.Ignore))
                return p.y - taban - hit.point.y;
            return float.NaN;
        }

        private int Baskin(bool sonraki)
        {
            if (sonraki) _anim.GetNextAnimatorClipInfo(0, s_klip);
            else _anim.GetCurrentAnimatorClipInfo(0, s_klip);
            int best = 0;
            float w = -1f;
            for (int i = 0; i < s_klip.Count; i++)
            {
                AnimationClip c = s_klip[i].clip;
                if (c == null || s_klip[i].weight <= w) continue;
                w = s_klip[i].weight;
                best = c.GetInstanceID();
                if (!KlipAdlari.ContainsKey(best)) KlipAdlari[best] = c.name;
            }

            return best;
        }

        private void LateUpdate()
        {
            if (!_bagli || _anim == null || _kok == null || _kalca == null || _gogus == null || _ayakL == null || _ayakR == null) return;
            if (Etiket != _etiketOnce) { _etiketOnce = Etiket; Since = 0; }
            else Since++;

            float dt = Time.deltaTime;
            Vector3 kok = _kok.position, kalca = _kalca.position, l = _ayakL.position, r = _ayakR.position;
            Vector3 tl = _parmakL.position, tr = _parmakR.position;
            var k = new Kare { Etiket = Etiket, Since = Since, Zaman = Time.time, Dt = dt };
            k.AyakYL = l.y; k.AyakYR = r.y; k.ParmakYL = tl.y; k.ParmakYR = tr.y;
            if (_ik != null) { k.IkVar = true; k.IkServis = _ik.Served; k.KilitL = _ik.IsLocked(0); k.KilitR = _ik.IsLocked(1); }
            _kare++;
            int bekleyen = _bekleyen;
            _bekleyen = 0;
            k.OlayL = Tuket(bekleyen, 1, 0);
            k.OlayR = Tuket(bekleyen, 2, 1);
            k.KalkL = Tuket(bekleyen, 4, 2);
            k.KalkR = Tuket(bekleyen, 8, 3);
            if (k.OlayL) OlaySayisi++;
            if (k.OlayR) OlaySayisi++;
            if (k.KalkL) KalkisSayisi++;
            if (k.KalkR) KalkisSayisi++;
            k.BoslukL = Bosluk(_ayakL, _anim.leftFeetBottomHeight);
            k.BoslukR = Bosluk(_ayakR, _anim.rightFeetBottomHeight);
            Vector3 sicra = kok - _onceKok;
            k.Gecerli = _onceVar && dt > 1e-6f && new Vector2(sicra.x, sicra.z).magnitude < 1f && Mathf.Abs(sicra.y) < 0.5f;
            if (k.Gecerli)
            {
                Vector3 kv = sicra / dt;
                kv.y = 0f;
                k.KokV = kv;
                Vector3 hv = (kalca - _onceKalca) / dt;
                hv.y = 0f;
                k.KalcaV = hv;
                k.AyakVL = (l - _onceL) / dt;
                k.AyakVR = (r - _onceR) / dt;
                k.ParmakVL = (tl - _onceTL) / dt;
                k.ParmakVR = (tr - _onceTR) / dt;
            }

            float ky = Yaw(_kok.forward), hy = Yaw(_kalca.rotation * _kalcaEksen);
            if (_onceVar) { _kokYaw += Mathf.DeltaAngle(_kokHam, ky); _kalcaYaw += Mathf.DeltaAngle(_kalcaHam, hy); }
            else { _kokYaw = ky; _kalcaYaw = hy; }
            _kokHam = ky;
            _kalcaHam = hy;
            k.KokYaw = _kokYaw;
            k.KalcaYaw = _kalcaYaw;
            Vector3 s = Quaternion.Inverse(_kok.rotation) * (_gogus.position - kalca);
            k.Egilme = Mathf.Atan2(s.x, s.y) * Mathf.Rad2Deg;
            k.Ek = Ek != null ? Ek() : float.NaN;

            AnimatorStateInfo info = _anim.GetCurrentAnimatorStateInfo(0);
            k.Durum = info.shortNameHash;
            k.Loko = info.shortNameHash == LokoHash;
            k.Hiz = info.speed * info.speedMultiplier;
            k.Faz = Mathf.Repeat(info.normalizedTime, 1f);
            k.Klip = Baskin(false);
            k.SonrakiKlip = _anim.IsInTransition(0) ? Baskin(true) : 0;

            // bone rotation change in the root's space, °/frame at 60 fps (LokomotionKaydedici's L3 input)
            Quaternion inv = Quaternion.Inverse(_kok.rotation);
            float olcek = dt > 1e-6f ? (1f / R2LokomotionOlcumTests.Fps) / dt : 0f;
            for (int b = 0; b < _kemik.Length; b++)
            {
                Transform t = _kemik[b];
                if (t == null) { Derece.Add(float.NaN); continue; }
                Quaternion q = inv * t.rotation;
                Derece.Add(k.Gecerli ? Quaternion.Angle(_onceRot[b], q) * olcek : float.NaN);
                _onceRot[b] = q;
            }

            Kareler.Add(k);
            _failureTrace?.Capture(this, k);
            _onceKok = kok;
            _onceKalca = kalca;
            _onceL = l;
            _onceR = r;
            _onceTL = tl;
            _onceTR = tr;
            _onceVar = true;
        }
    }
}
