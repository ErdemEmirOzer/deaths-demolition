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
    /// ApplyV6 writes the 22–45 / 75 m distances into a copy, inner gates weigh ×0.25 within 10 m and ×0.7 above a
    /// 50 % inner share of the last 24 spawns, edge gates and the profile-off case are untouched.
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
            Assert.AreEqual(kenar * 0.7f, Bolge(Kapi(SpawnPointKind.Inner, 20f), 0.6f), 1e-5f, "orta payı > %50 → ×0,7");
            Assert.AreEqual(kenar, Bolge(Kapi(SpawnPointKind.Inner, 9f), -1f), 1e-5f, "profil kapalı → kural yok");

            for (int i = 0; i < 24; i++) SpawnWorldProfile.RecordSpawn(i < 9 ? SpawnPointKind.Inner : SpawnPointKind.Edge);
            Assert.AreEqual(9f / 24f, SpawnWorldProfile.InnerShare, 1e-6f, "hedef 65/35 civarı");
        }
    }
}
