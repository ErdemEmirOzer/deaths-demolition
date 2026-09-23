using System.Diagnostics;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-033 (ADR-033-1) · The pure threat model on scenarios: empty, encirclement (8 sectors),
    /// clustered attackers, tier presence, between-round pull-back (DEC-065), asymmetric
    /// smoothing + layer hysteresis, rear-attack / low-health flags with hold, determinism,
    /// allocation and cost.
    /// </summary>
    [Category("WP033")]
    public sealed class ThreatDirectorTests
    {
        private ThreatSettings _s;
        private readonly ThreatEnemyInput[] _enemies = new ThreatEnemyInput[64];

        [SetUp] public void SetUp() { _s = ScriptableObject.CreateInstance<ThreatSettings>(); ThreatSettings.SetActive(_s); ThreatDirector.ResetStatics(); }
        [TearDown] public void TearDown() { ThreatSettings.ResetStatics(); ThreatDirector.ResetStatics(); if (_s != null) Object.DestroyImmediate(_s); }

        private static ThreatContext Ctx(bool inFight = true, float health = 1f, float round = 0f, Vector3? forward = null) =>
            new ThreatContext { PlayerPosition = Vector3.zero, ViewForward = forward ?? Vector3.forward, Health01 = health, RoundProgress01 = round, InFight = inFight };

        private int Ring(int count, float radius, EnemyTier tier = EnemyTier.Standard, bool windup = false, float startDeg = 0f, float spanDeg = 360f)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (startDeg + spanDeg * i / count) * Mathf.Deg2Rad;
                _enemies[i] = new ThreatEnemyInput { Position = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius), Tier = tier, WindingUp = windup };
            }

            return count;
        }

        private ThreatSnapshot Eval(in ThreatContext ctx, int count, ref ThreatState st, float dt = 0.1f) => ThreatModel.Evaluate(_s, in ctx, _enemies, count, ref st, dt);

        [Test]
        public void Bos_SifirTehdit_Bayraklar0()
        {
            var st = new ThreatState();
            ThreatSnapshot s = Eval(Ctx(), 0, ref st);
            Assert.AreEqual(0f, s.Intensity, 1e-6f); Assert.AreEqual(0, s.Layer); Assert.AreEqual(0, s.NearCount); Assert.AreEqual(0, s.OccupiedSectors);
            Assert.IsFalse(s.Encircled || s.RearAttack || s.LowHealth || s.StrongVersionPresent); Assert.AreEqual(EnemyTier.Standard, s.StrongestTier);
        }

        [Test]
        public void Kusatilma_8Dilim_HalkaEvet_TekKumeHayir_6mSiniri()
        {
            var st = new ThreatState();
            ThreatSnapshot ring = Eval(Ctx(), Ring(8, 4f, startDeg: 22.5f), ref st);   // sector centres (a body exactly on a boundary rounds either way)
            Assert.AreEqual(8, ring.OccupiedSectors); Assert.IsTrue(ring.Encircled); Assert.AreEqual(8, ring.NearCount); Assert.AreEqual(1f, ring.Encirclement, 1e-6f);
            st = new ThreatState();
            ThreatSnapshot five = Eval(Ctx(), Ring(5, 4f, startDeg: 22.5f, spanDeg: 225f), ref st);   // 5 sector centres → encircled (≥ 5)
            Assert.GreaterOrEqual(five.OccupiedSectors, 5); Assert.IsTrue(five.Encircled);
            st = new ThreatState();
            ThreatSnapshot cluster = Eval(Ctx(), Ring(8, 4f, spanDeg: 40f), ref st);   // 8 enemies inside a 40° wedge → ≤ 2 sectors
            Assert.LessOrEqual(cluster.OccupiedSectors, 2); Assert.IsFalse(cluster.Encircled); Assert.AreEqual(8, cluster.NearCount, "yakın sayısı kümeye bağlı değil");
            st = new ThreatState();
            ThreatSnapshot far = Eval(Ctx(), Ring(8, 6.5f, startDeg: 22.5f), ref st);   // beyond NearRadius 6
            Assert.AreEqual(0, far.NearCount); Assert.AreEqual(0, far.OccupiedSectors); Assert.IsFalse(far.Encircled);
        }

        [Test]
        public void TierVarligi_UltraElite_Special_Standard()
        {
            var st = new ThreatState();
            int n = Ring(3, 10f);
            _enemies[1].Tier = EnemyTier.Ultra;
            ThreatSnapshot ultra = Eval(Ctx(), n, ref st);
            Assert.AreEqual(EnemyTier.Ultra, ultra.StrongestTier); Assert.AreEqual(_s.UltraPresence, ultra.TierPresence, 1e-6f); Assert.IsTrue(ultra.StrongVersionPresent);
            _enemies[1].Tier = EnemyTier.Special; st = new ThreatState();
            ThreatSnapshot sp = Eval(Ctx(), n, ref st);
            Assert.AreEqual(_s.SpecialPresence, sp.TierPresence, 1e-6f); Assert.IsFalse(sp.StrongVersionPresent);
            Assert.Greater(ultra.Raw, sp.Raw);
        }

        [Test]
        public void RoundArasi_Tavan015_DEC065_SavastaKalkar()
        {
            var st = new ThreatState();
            int n = Ring(8, 3f, EnemyTier.Ultra, true);
            ThreatSnapshot between = Eval(Ctx(inFight: false, health: 0.2f, round: 1f), n, ref st, 1f);
            Assert.LessOrEqual(between.Intensity, _s.BetweenRoundsCeiling + 1e-6f); Assert.LessOrEqual(between.Raw, _s.BetweenRoundsCeiling + 1e-6f); Assert.AreEqual(0, between.Layer);
            for (int i = 0; i < 30; i++) Eval(Ctx(inFight: false, health: 0.2f, round: 1f), n, ref st, 0.1f);
            Assert.LessOrEqual(st.Intensity, _s.BetweenRoundsCeiling + 1e-6f, "tavan zamanla da aşılmaz");
            ThreatSnapshot fight = Eval(Ctx(inFight: true, health: 0.2f, round: 1f), n, ref st, 0.1f);
            Assert.Greater(fight.Raw, 0.8f, "savaşta ham tehdit yüksek");
            Assert.Greater(fight.Intensity, between.Intensity, "savaşa dönünce yükselmeye başlar");
        }

        [Test]
        public void Yumusatma_Asimetrik_YukselisHizli_DususYavas_Deterministik()
        {
            var st = new ThreatState();
            Eval(Ctx(), 0, ref st, 0.1f);   // initialised at 0
            int n = Ring(8, 3f, EnemyTier.Ultra, true);
            float t = 0f; int riseTicks = 0;
            while (st.Intensity < 0.9f && riseTicks < 200) { Eval(Ctx(health: 0.2f, round: 1f), n, ref st, 0.05f); t += 0.05f; riseTicks++; }
            Assert.Less(t, 1.0f, "yükseliş 1 s içinde (RiseRate 1,5/s)"); Assert.Greater(riseTicks, 2, "anında zıplamaz");
            float peak = st.Intensity; int fallTicks = 0;
            while (st.Intensity > 0.2f && fallTicks < 400) { Eval(Ctx(), 0, ref st, 0.05f); fallTicks++; }
            Assert.Greater(fallTicks * 0.05f, 1.2f, "düşüş yavaş (FallRate 0,45/s)"); Assert.Greater(fallTicks, riseTicks * 2);
            // determinism: same sequence → same trace
            var a = new ThreatState(); var b = new ThreatState(); float sumA = 0f, sumB = 0f;
            for (int i = 0; i < 50; i++) { int c = (i % 7) + 1; Ring(c, 2f + i % 5, (EnemyTier)(i % 4), i % 3 == 0); sumA += Eval(Ctx(health: 0.5f + 0.01f * i), c, ref a, 0.033f).Intensity; Ring(c, 2f + i % 5, (EnemyTier)(i % 4), i % 3 == 0); sumB += Eval(Ctx(health: 0.5f + 0.01f * i), c, ref b, 0.033f).Intensity; }
            Assert.AreEqual(sumA, sumB, 0f, "deterministik");
            Assert.Greater(peak, 0.9f);
        }

        [Test]
        public void KatmanHisterezisi_EsikEtrafindaTitremez()
        {
            var st = new ThreatState { Initialised = true, Intensity = 0.24f, Layer = 0 };
            // hold raw exactly around the layer-1 threshold (0.2 ± 0.05): with 8 far enemies + health the raw sits ~0.24; force via ceiling trick instead:
            // use custom weights so that proximity alone maps: near/8 * 1.0 → raw = near/8
            _s.ProximityWeight = 1f; _s.EncirclementWeight = 0f; _s.TierWeight = 0f; _s.RoundWeight = 0f; _s.DangerWeight = 0f; _s.AttackPrepWeight = 0f;
            _s.RiseRate = 100f; _s.FallRate = 100f;   // no smoothing: intensity == raw
            int n2 = Ring(2, 3f, spanDeg: 30f);  // raw 0.25 → ≥ 0.25 → layer 1
            Assert.AreEqual(1, Eval(Ctx(), n2, ref st).Layer);
            int n1 = Ring(1, 3f);                 // raw 0.125 → < 0.15 → layer 0
            Assert.AreEqual(0, Eval(Ctx(), n1, ref st).Layer);
            Assert.AreEqual(1, Eval(Ctx(), n2, ref st).Layer);
            // raw 0.2 (between down 0.15 and up 0.25): stays wherever it was
            _s.ProximityFullCount = 10; int n2b = Ring(2, 3f, spanDeg: 30f);   // 0.2
            Assert.AreEqual(1, Eval(Ctx(), n2b, ref st).Layer, "0,20 histerezis bandında: 1 kalır");
            st.Layer = 0;
            Assert.AreEqual(0, Eval(Ctx(), n2b, ref st).Layer, "0,20 histerezis bandında: 0 kalır");
            // full climb / descent one layer per evaluation, up to 3
            _s.ProximityFullCount = 8; int n8 = Ring(8, 3f);   // raw 1.0
            Assert.AreEqual(1, Eval(Ctx(), n8, ref st).Layer); Assert.AreEqual(2, Eval(Ctx(), n8, ref st).Layer); Assert.AreEqual(3, Eval(Ctx(), n8, ref st).Layer); Assert.AreEqual(3, Eval(Ctx(), n8, ref st).Layer);
        }

        [Test]
        public void ArkadanSaldiri_DusukCan_Bayraklar_Tutma()
        {
            var st = new ThreatState();
            _enemies[0] = new ThreatEnemyInput { Position = new Vector3(0f, 0f, -3f), Tier = EnemyTier.Standard, WindingUp = true };   // behind (view +Z)
            ThreatSnapshot rear = Eval(Ctx(forward: Vector3.forward), 1, ref st);
            Assert.IsTrue(rear.RearAttack); Assert.AreEqual(1, rear.WindingUp); Assert.AreEqual(1f / _s.AttackPrepFullCount, rear.AttackPrep, 1e-6f);
            _enemies[0].Position = new Vector3(0f, 0f, 3f);   // in front
            st = new ThreatState();
            Assert.IsFalse(Eval(Ctx(forward: Vector3.forward), 1, ref st).RearAttack);
            _enemies[0].Position = new Vector3(3f, 0f, -0.5f);   // side, slightly behind: 99° < 100° → not rear
            st = new ThreatState();
            Assert.IsFalse(Eval(Ctx(forward: Vector3.forward), 1, ref st).RearAttack);
            _enemies[0].WindingUp = false; _enemies[0].Position = new Vector3(0f, 0f, -3f);
            st = new ThreatState();
            Assert.IsFalse(Eval(Ctx(forward: Vector3.forward), 1, ref st).RearAttack, "hazırlık yoksa arkadaki düşman uyarı değil");
            // hold: flag stays FlagHoldSeconds after the cause ends, then drops
            st = new ThreatState(); _enemies[0].WindingUp = true;
            Eval(Ctx(forward: Vector3.forward), 1, ref st, 0.1f);
            Assert.IsTrue(Eval(Ctx(forward: Vector3.forward), 0, ref st, 0.1f).RearAttack, "tutma penceresi");
            for (int i = 0; i < 10; i++) Eval(Ctx(forward: Vector3.forward), 0, ref st, 0.1f);
            Assert.IsFalse(Eval(Ctx(forward: Vector3.forward), 0, ref st, 0.1f).RearAttack, "tutma bitti");
            // low health
            st = new ThreatState();
            Assert.IsTrue(Eval(Ctx(health: 0.3f), 0, ref st).LowHealth); Assert.IsFalse(Eval(Ctx(health: 0.5f), 0, ref st).LowHealth);
            Assert.AreEqual(0.7f, Eval(Ctx(health: 0.3f), 0, ref st).PlayerDanger, 1e-6f);
        }

        [Test]
        public void Model_AyirmaSifir_Maliyet005ms_40Dusman()
        {
            var st = new ThreatState();
            int n = Ring(40, 5f, EnemyTier.Elite, true);
            ThreatContext ctx = Ctx(health: 0.5f, round: 0.5f);
            for (int i = 0; i < 200; i++) Eval(in ctx, n, ref st, 0.016f);   // warm-up
            HeapWindow.Prepare();
            byte[] yem = null;
            HeapWindow.Result control = HeapWindow.Measure(() => { yem = new byte[64 * 1024]; }, 0);
            Assert.Greater(control.Delta, 0);
            HeapWindow.Result window = HeapWindow.Measure(() => { for (int i = 0; i < 2000; i++) Eval(in ctx, n, ref st, 0.016f); }, 1);
            Assert.AreEqual(0, window.Gen0); Assert.LessOrEqual(window.Delta, 4 * 1024, "model ayırma yapmaz (" + window.Delta + " B, yem " + yem.Length + ")");
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 5000; i++) Eval(in ctx, n, ref st, 0.016f);
            sw.Stop();
            double perCall = sw.Elapsed.TotalMilliseconds / 5000.0;
            UnityEngine.Debug.Log("[DD-TEST] wp033.model 40 enemies " + perCall.ToString("0.0000") + " ms/eval");
            Assert.LessOrEqual(perCall, 0.05, "≤ 0,05 ms @ 40 düşman");
        }

        [Test]
        public void U12b_ArkaYay_YonYogunlukAciSayi_OnTarafSayilmaz_SifirAyirma()
        {
            float r = _s.NearRadius * 0.5f;
            var st = new ThreatState();

            // nothing behind: two in front → zero reading
            _enemies[0] = new ThreatEnemyInput { Position = new Vector3(0f, 0f, r), Tier = EnemyTier.Standard };
            _enemies[1] = new ThreatEnemyInput { Position = new Vector3(0.5f, 0f, r), Tier = EnemyTier.Standard };
            ThreatSnapshot front = Eval(Ctx(), 2, ref st);
            Assert.AreEqual(0, front.RearCount); Assert.AreEqual(0f, front.RearIntensity); Assert.AreEqual(Vector3.zero, front.RearDirection);

            // one straight behind (view +Z → body at −Z)
            _enemies[0] = new ThreatEnemyInput { Position = new Vector3(0f, 0f, -r), Tier = EnemyTier.Standard };
            ThreatSnapshot back = Eval(Ctx(), 1, ref st);
            Assert.AreEqual(1, back.RearCount);
            Assert.AreEqual(-1f, back.RearDirection.z, 1e-4f, "yön oyuncudan tehdide (−Z)");
            Assert.AreEqual(180f, Mathf.Abs(back.RearBearingDegrees), 1e-3f);
            Assert.AreEqual(0.5f * 1f / ThreatModel.RearFullWeight, back.RearIntensity, 1e-4f, "yarı mesafe Standard = 0,5 / tam ağırlık");

            // behind-right: bearing positive (clockwise from above), behind-left negative
            _enemies[0] = new ThreatEnemyInput { Position = new Vector3(r, 0f, -r), Tier = EnemyTier.Standard };
            Assert.Greater(Eval(Ctx(), 1, ref st).RearBearingDegrees, 90f, "arka-sağ = +");
            _enemies[0] = new ThreatEnemyInput { Position = new Vector3(-r, 0f, -r), Tier = EnemyTier.Standard };
            Assert.Less(Eval(Ctx(), 1, ref st).RearBearingDegrees, -90f, "arka-sol = −");

            // the dangerous one wins the direction: a winding-up Ultra behind-left outweighs two Standards behind-right
            _enemies[0] = new ThreatEnemyInput { Position = new Vector3(-r, 0f, -r * 0.5f), Tier = EnemyTier.Ultra, WindingUp = true };
            _enemies[1] = new ThreatEnemyInput { Position = new Vector3(r, 0f, -r), Tier = EnemyTier.Standard };
            _enemies[2] = new ThreatEnemyInput { Position = new Vector3(r * 1.2f, 0f, -r), Tier = EnemyTier.Standard };
            ThreatSnapshot mix = Eval(Ctx(), 3, ref st);
            Assert.AreEqual(3, mix.RearCount);
            Assert.Less(mix.RearDirection.x, 0f, "sarılan Ultra yönü belirler");
            Assert.LessOrEqual(mix.RearIntensity, 1f);

            // view turned toward the pressure → it is no longer behind
            ThreatSnapshot turned = Eval(Ctx(forward: Vector3.back), 3, ref st);
            Assert.AreEqual(0, turned.RearCount, "görüş konisinde kalanlar arka yay değil");

            // data only: the rule surfaces are unchanged by the rear reading (same flags / layer inputs)
            Assert.AreEqual(mix.NearCount, 3);

            // 0 allocation on the model (warm call first)
            Eval(Ctx(), 3, ref st);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 100; k++) Eval(Ctx(), 3, ref st);
            long after = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0, after - before, "model ayırma yapmaz (Editor Mono'da sayaç 0 okuyabilir; yapısal kontrol)");
        }
    }
}
