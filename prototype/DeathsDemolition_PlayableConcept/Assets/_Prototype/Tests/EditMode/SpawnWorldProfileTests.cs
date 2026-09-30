using DD.Prototype.Core.Config;
using DD.Prototype.Navigation;
using DD.Prototype.Spawn;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B6 · the v6 world profile (C-00 decision A; V6 is the default world since afabd44f): the layout signal V6 or a
    /// loaded v6 scene → v6, the AP005-forced layout without a v6 scene → AP005 values (Resolve, no editor-scene dependence),
    /// ApplyV6 writes the 22–45 / 75 m distances into a copy, inner gates weigh ×0.25 within 10 m and are steered toward a
    /// 35 % inner share of the last 24 spawns (R1 28.09), edge gates and the profile-off case are untouched.
    /// </summary>
    [Category("WP076")]
    public sealed class SpawnWorldProfileTests
    {
        [SetUp] public void SetUp() { SpawnWorldProfile.ResetStatics(); CrowdFeelSettings.ResetStatics(); }
        [TearDown] public void TearDown() { SpawnWorldProfile.ResetStatics(); CrowdFeelSettings.ResetStatics(); }

        private static SpawnPointRuntime Kapi(SpawnPointKind tur, float duz, float yol = 30f)
        {
            var tanim = new SpawnPointDefinition { Id = "G", Region = RingRegion.North, Position = new Vector3(0f, 0f, yol), BaseWeight = 1f, Enabled = true, FaceMapCenter = true, Kind = tur };
            return new SpawnPointRuntime(0, tanim) { PathDistance = yol, StraightDistance = duz, PathStatus = NavPathStatus.Complete, LastUsedTime = -999f };
        }

        private static float Bolge(SpawnPointRuntime p, float innerShare)
        {
            var g = new SpawnWeightInputs { PlayerRegion = RingRegionMap.Classify(Vector3.zero, Vector3.zero, 14f), Now = 100f, InnerShare = innerShare };
            var tuning = new SpawnWeightTuning(); tuning.Sanitize();
            SpawnWeightCalculator.Evaluate(p, in g, new SpawnConfigSection(), tuning, SpawnRelaxation.None, out SpawnWeightBreakdown b);
            return b.Region;
        }

        [Test]
        public void V6Varsayilan_Ap005Zorlamali_V6Mesafeleri_Kopyaya()
        {
            Assert.IsTrue(SpawnWorldProfile.Resolve(WorldLayoutKind.V6, false), "varsayılan dünya V6 → v6 profili");
            Assert.IsTrue(SpawnWorldProfile.Resolve(WorldLayoutKind.Ap005, true), "sinyalsiz v6 sahnesi → yedek yol v6");
            Assert.IsFalse(SpawnWorldProfile.Resolve(WorldLayoutKind.Ap005, false), "AP005 zorlamalı → AP005 değerleri");
            Assert.IsFalse(SpawnWorldProfile.AlwaysV6, "C-00 (A): tek satır anahtarı kapalı");
            SpawnWorldProfile.Override = false; SpawnWorldProfile.Refresh();
            Assert.IsFalse(SpawnWorldProfile.IsV6, "Override false → AP005");
            SpawnWorldProfile.Override = true; SpawnWorldProfile.Refresh();
            Assert.IsTrue(SpawnWorldProfile.IsV6);
            var asil = new SpawnConfigSection();
            var kopya = JsonUtility.FromJson<SpawnConfigSection>(JsonUtility.ToJson(asil));
            SpawnWorldProfile.ApplyV6(kopya);
            Assert.AreEqual(22f, kopya.PreferredDistanceMin); Assert.AreEqual(45f, kopya.PreferredDistanceMax); Assert.AreEqual(75f, kopya.MaxUsefulDistance);
            Assert.AreEqual(62f, asil.MaxUsefulDistance, "asıl config değişmez");
        }

        [Test]
        public void OrtaKapi_Yakin_Pay_KenarEtkilenmez()
        {
            float kenar = Bolge(Kapi(SpawnPointKind.Edge, 9f), 0f);
            Assert.AreEqual(kenar * 0.25f, Bolge(Kapi(SpawnPointKind.Inner, 9f), 0f), 1e-5f, "orta kapı ≤ 10 m ×0,25");
            Assert.AreEqual(kenar, Bolge(Kapi(SpawnPointKind.Inner, 20f), 0f), 1e-5f);
            Assert.AreEqual(kenar * SpawnWorldProfile.InnerSteer(0.6f), Bolge(Kapi(SpawnPointKind.Inner, 20f), 0.6f), 1e-5f, "orta payı hedefin üstünde → yönlendirme");
            Assert.AreEqual(kenar, Bolge(Kapi(SpawnPointKind.Inner, 20f), 0.35f), 1e-5f, "hedefte (%35) ceza yok");
            Assert.AreEqual(0.29f, SpawnWorldProfile.InnerSteer(0.5f), 0.01f, "(odds 0,35 / odds 0,5)²");
            Assert.AreEqual(SpawnWorldProfile.InnerSteerFloor, SpawnWorldProfile.InnerSteer(0.9f), 1e-6f, "taban");
            Assert.AreEqual(kenar, Bolge(Kapi(SpawnPointKind.Inner, 9f), -1f), 1e-5f, "profil kapalı → kural yok");

            for (int i = 0; i < 24; i++) SpawnWorldProfile.RecordSpawn(i < 9 ? SpawnPointKind.Inner : SpawnPointKind.Edge);
            Assert.AreEqual(9f / 24f, SpawnWorldProfile.InnerShare, 1e-6f, "hedef 65/35 civarı");
        }

        [Test]
        public void OrtaKota_SiradakiKayit_EnEskiKaydiHesaplar()
        {
            Assert.IsTrue(SpawnWorldProfile.CanRecordInner, "Y5: SteeringShare önseliyle oturumun ilk doğuşu iç kapı olabilir");
            SpawnWorldProfile.RecordSpawn(SpawnPointKind.Edge);
            Assert.IsTrue(SpawnWorldProfile.CanRecordInner);
            SpawnWorldProfile.ResetStatics();
            for (int i = 0; i < 24; i++) SpawnWorldProfile.RecordSpawn(i < 12 ? SpawnPointKind.Inner : SpawnPointKind.Edge);
            Assert.IsTrue(SpawnWorldProfile.CanRecordInner, "en eski inner çıkınca bir inner yerine geçebilir");
            SpawnWorldProfile.ResetStatics();
            for (int i = 0; i < 24; i++) SpawnWorldProfile.RecordSpawn(i < 12 ? SpawnPointKind.Edge : SpawnPointKind.Inner);
            Assert.IsFalse(SpawnWorldProfile.CanRecordInner, "en eski edge çıkınca inner payı aşar");
        }

        [Test]
        public void OrtaKota_HerGevsetmedeKorunur_ProfilKapaliykenUygulanmaz()
        {
            var tuning = new SpawnWeightTuning(); tuning.Sanitize();
            var config = new SpawnConfigSection();
            var inputs = new SpawnWeightInputs { PlayerRegion = RingRegion.North, Now = 100f, InnerShare = 0.5f, InnerQuotaFull = true };
            var inner = Kapi(SpawnPointKind.Inner, 20f);
            var edge = Kapi(SpawnPointKind.Edge, 20f);
            foreach (SpawnRelaxation relaxation in new[] { SpawnRelaxation.None, SpawnRelaxation.Screen,
                SpawnRelaxation.Screen | SpawnRelaxation.Cooldown,
                SpawnRelaxation.Screen | SpawnRelaxation.Cooldown | SpawnRelaxation.Reachability })
            {
                Assert.AreEqual(0f, SpawnWeightCalculator.Evaluate(inner, in inputs, config, tuning, relaxation, out _));
                Assert.Greater(SpawnWeightCalculator.Evaluate(edge, in inputs, config, tuning, relaxation, out _), 0f);
            }
            // B9 Y5: only the explicit InnerQuota relaxation lets an inner gate through a full quota
            Assert.Greater(SpawnWeightCalculator.Evaluate(inner, in inputs, config, tuning,
                SpawnRelaxation.Screen | SpawnRelaxation.Cooldown | SpawnRelaxation.InnerQuota, out _), 0f);
            inputs.InnerShare = -1f;
            Assert.Greater(SpawnWeightCalculator.Evaluate(inner, in inputs, config, tuning, SpawnRelaxation.None, out _), 0f);
        }

        /// <summary>
        /// WP-076 B9 Y5 (C-00 30.09) ladder None → Screen → +Cooldown → +InnerQuota → +Reachability. (a) the only eligible gate is
        /// inner and the quota is full → it is picked with InnerQuota (not Reachability); with an edge gate available the quota holds;
        /// with the quota not full the step is skipped.
        /// </summary>
        [Test]
        public void OrtaKota_TekIcKapi_KotaDolu_InnerQuotaIleSecilir()
        {
            var tuning = new SpawnWeightTuning(); tuning.Sanitize();
            var config = new SpawnConfigSection();
            var inputs = new SpawnWeightInputs { PlayerRegion = RingRegion.North, Now = 100f, InnerShare = 0.5f, InnerQuotaFull = true };
            var selector = new SpawnPointSelector();
            System.Func<float> r = () => 0.5f;

            var onlyInner = new System.Collections.Generic.List<SpawnPointRuntime> { Kapi(SpawnPointKind.Inner, 20f) };
            SpawnPointRuntime chosen = selector.SelectWithLadder(onlyInner, in inputs, config, tuning, r, out SpawnRelaxation used);
            Assert.IsNotNull(chosen, "kota dolu, yalnız iç kapı → spawn durmaz");
            Assert.AreEqual(SpawnRelaxation.Screen | SpawnRelaxation.Cooldown | SpawnRelaxation.InnerQuota, used, "Reachability'den önce InnerQuota");

            var mixed = new System.Collections.Generic.List<SpawnPointRuntime> { Kapi(SpawnPointKind.Inner, 20f), Kapi(SpawnPointKind.Edge, 20f) };
            chosen = selector.SelectWithLadder(mixed, in inputs, config, tuning, r, out used);
            Assert.AreEqual(SpawnPointKind.Edge, chosen.Definition.Kind, "kenar varken kota korunur");
            Assert.AreEqual(SpawnRelaxation.None, used);

            inputs.InnerQuotaFull = false;
            chosen = selector.SelectWithLadder(onlyInner, in inputs, config, tuning, r, out used);
            Assert.AreEqual(SpawnRelaxation.None, used, "kota dolu değilken merdiven basamağı atlanır");
        }

        /// <summary>
        /// WP-076 B9 Y5 (b): an Elite pick (tier path gate ≤ 55 m) whose only gate within 55 m is inner, with the quota full, is
        /// rerouted to that inner gate (the tier path uses the same ladder) instead of falling back to the far edge gate.
        /// </summary>
        [Test]
        public void OrtaKota_EliteYolKapisi_KotaDolu_IcKapiyaYonlenir()
        {
            var tuning = new SpawnWeightTuning(); tuning.Sanitize();
            var config = new SpawnConfigSection();
            var inputs = new SpawnWeightInputs { PlayerRegion = RingRegion.North, Now = 100f, InnerShare = 0.5f, InnerQuotaFull = true,
                                                 MaxPathDistance = SpawnModeTable.HeavyTierMaxPath };
            var selector = new SpawnPointSelector();
            var points = new System.Collections.Generic.List<SpawnPointRuntime>
            {
                Kapi(SpawnPointKind.Edge, 50f, SpawnModeTable.HeavyTierMaxPath + 12f),   // edge: beyond the Elite path gate
                Kapi(SpawnPointKind.Inner, 30f, 40f)
            };
            SpawnPointRuntime chosen = selector.SelectWithLadder(points, in inputs, config, tuning, () => 0.5f, out SpawnRelaxation used);
            Assert.IsNotNull(chosen, "Elite için ≤ 55 m iç kapı var → yönlenir");
            Assert.AreEqual(SpawnPointKind.Inner, chosen.Definition.Kind);
            Assert.AreNotEqual(SpawnRelaxation.None, used & SpawnRelaxation.InnerQuota);
            Assert.AreEqual(SpawnRelaxation.None, used & SpawnRelaxation.Reachability, "yolu olan iç kapı, Reachability gevşetilmeden");
        }

        /// <summary>
        /// R1 28.09 (innerShare24 0.58): weighted draws over 10 edge + N inner gates whose unsteered inner mass is 55–75 %.
        /// The hard cap keeps the 24-window ≤ 0.5 at the round-size checkpoints (12 / 24 / 48 / 96 / 240 spawns) for every mass;
        /// the mean is held to K10 (≤ 0.45, C-00 30.09 M-26) where the odds² steering can hold it (≤ 65 %); 75 % is reported
        /// (K10 OPEN above ≈ 65 %, decided by the measured [SpawnV6] innerMass). Deterministic seed.
        /// </summary>
        [TestCase(0.55f, true)]
        [TestCase(0.65f, true)]
        [TestCase(0.75f, false)]
        public void OrtaPayi_Yonlendirme_Pencere24Hedefte(float innerMass, bool k10)
        {
            const int edges = 10, inners = 10, draws = 240, seeds = 50;
            float innerW = innerMass / (1f - innerMass) * edges / inners;   // per-gate weight giving that unsteered inner mass
            var edgePoint = Kapi(SpawnPointKind.Edge, 20f);
            var innerPoint = Kapi(SpawnPointKind.Inner, 20f);
            var config = new SpawnConfigSection();
            var tuning = new SpawnWeightTuning(); tuning.Sanitize();
            double sum = 0; int n = 0;
            var log = new System.Text.StringBuilder("[DD-OBS] wp076.innerSteer m=" + innerMass + " seeds=" + seeds + " (seed 0 checkpoints):");
            for (int seed = 0; seed < seeds; seed++)
            {
                SpawnWorldProfile.ResetStatics();
                var rng = new System.Random(76028 + seed);
                for (int d = 1; d <= draws; d++)
                {
                    var inputs = new SpawnWeightInputs
                    {
                        PlayerRegion = RingRegionMap.Classify(Vector3.zero, Vector3.zero, 14f), Now = 100f,
                        InnerShare = SpawnWorldProfile.SteeringShare, InnerQuotaFull = !SpawnWorldProfile.CanRecordInner
                    };
                    float edgeWeight = SpawnWeightCalculator.Evaluate(edgePoint, in inputs, config, tuning, SpawnRelaxation.None, out _);
                    float f = SpawnWeightCalculator.Evaluate(innerPoint, in inputs, config, tuning, SpawnRelaxation.None, out _) / edgeWeight;
                    double wIn = inners * innerW * f, wEdge = edges;
                    bool inner = rng.NextDouble() * (wIn + wEdge) < wIn;
                    SpawnWorldProfile.RecordSpawn(inner ? SpawnPointKind.Inner : SpawnPointKind.Edge);
                    if (d >= 12) { sum += SpawnWorldProfile.InnerShare; n++; }
                    if (d == 12 || d == 24 || d == 48 || d == 96 || d == 240)
                    {
                        if (seed == 0) log.Append(' ').Append(d).Append('=').Append(SpawnWorldProfile.InnerShare.ToString("0.00"));
                        Assert.LessOrEqual(SpawnWorldProfile.InnerShare, 0.5f + 1e-4f, "innerShare24 > 0,5 @" + d + " m=" + innerMass + " seed=" + seed);
                    }
                }
            }
            Debug.Log(log.Append(" mean=").Append((sum / n).ToString("0.000")).ToString());
            if (k10) Assert.LessOrEqual(sum / n, 0.45, "K10: ortalama pencere payı ≤ 0,45 (65/35 ± 10)");
        }
    }
}
