using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Navigation;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // WP-051C: Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-030 (ADR-030-1) · Regional navigation profile and router: the ring map defaults,
    /// route cost comparison, chokepoint occupancy / queue rule, fit-scaled separation radius,
    /// route roll determinism, allocation-free hot paths, the spawn visibility factor and the
    /// post-bake area-cost assignment on a single-area bake.
    /// </summary>
    [Category("WP030")]
    public sealed class RegionNavProfileTests
    {
        private RegionNavProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = RegionNavProfile.CreateDefault();
            RegionNavProfile.SetActive(_profile);
            RegionNavRouter.ResetStatics();
        }

        [TearDown]
        public void TearDown()
        {
            RegionNavProfile.ResetStatics();
            RegionNavRouter.ResetStatics();
            if (_profile != null) Object.DestroyImmediate(_profile);
        }

        private static ICrowdMember Uye(Vector3 p) => new SahteUye(p);

        private sealed class SahteUye : ICrowdMember
        {
            private readonly Vector3 _p;
            public SahteUye(Vector3 p) { _p = p; }
            public Vector3 Position => _p;
            public float Radius => 0.45f;
            public float Mass => 1f;
            public Vector3 SteeringTarget => _p;
            public void ApplySteering(Vector3 velocity) { }
        }

        [Test]
        public void Varsayilanlar_HalkaHaritasi_BesHacim_KoprüKoridorKapilar()
        {
            NavRegionVolume[] v = _profile.Volumes;
            Assert.AreEqual(5, v.Length);
            NavRegionVolume bridge = System.Array.Find(v, x => x.id == "S_Bridge");
            NavRegionVolume corridor = System.Array.Find(v, x => x.id == "W_Corridor");
            NavRegionVolume gw = System.Array.Find(v, x => x.id == "N_GateWest"), ge = System.Array.Find(v, x => x.id == "N_GateEast");
            Assert.IsNotNull(bridge); Assert.IsNotNull(corridor); Assert.IsNotNull(gw); Assert.IsNotNull(ge);
            Assert.AreEqual(NavRegionKind.Bridge, bridge.kind); Assert.IsTrue(bridge.IsCostly && bridge.IsChokepoint && bridge.hasAlternative);
            Assert.IsTrue(bridge.Contains(new Vector3(4f, 0f, -28f)), "köprü ortası"); Assert.IsFalse(bridge.Contains(new Vector3(10f, 0f, -28f)), "köprü dışı");
            Assert.IsTrue(corridor.Contains(new Vector3(-33f, 0f, 5f))); Assert.IsFalse(corridor.Contains(new Vector3(-20f, 0f, 5f)));
            Assert.IsTrue(gw.Contains(new Vector3(-8f, 0f, 34f)) && ge.Contains(new Vector3(8f, 0f, 34f)));
            // gates are each other's alternative; bridge alternative is off the bridge (shore path)
            Assert.IsTrue(ge.Contains(new Vector3(gw.alternative.x, 0f, 34f)) || Mathf.Abs(gw.alternative.x - ge.center.x) < 0.01f);
            Assert.IsFalse(bridge.Contains(bridge.alternative));
            // Find: first containing volume; open ground → null
            Assert.AreSame(bridge, _profile.Find(new Vector3(4f, 0f, -20f)));
            Assert.IsNull(_profile.Find(new Vector3(0f, 0f, 0f)));
            Assert.AreEqual(1f, _profile.FitRadiusScaleB); Assert.Less(_profile.FitRadiusScaleA, 1f); Assert.Greater(_profile.FitRadiusScaleC, 1f);
            Assert.AreEqual(_profile.FitRadiusScaleC, _profile.FitRadiusScale("C")); Assert.AreEqual(_profile.FitRadiusScaleB, _profile.FitRadiusScale("?"));
        }

        [Test]
        public void EtkinMaliyet_KopruUzerindenCarpanli_AcikAlanUzunluk_IlkMaliyetliHacim()
        {
            var direct = new[] { new Vector3(4f, 0f, -10f), new Vector3(4f, 0f, -20f), new Vector3(4f, 0f, -38f), new Vector3(4f, 0f, -44f) };
            float cost = RegionNavRouter.EffectiveCost(_profile, direct, direct.Length, out NavRegionVolume costly);
            Assert.IsNotNull(costly); Assert.AreEqual("S_Bridge", costly.id);
            // segment 1 (−10→−20): midpoint −15 outside; end −20 inside → costly; segments 2 inside; segment 3 (−38→−44): midpoint −41 outside, end outside
            float expected = 10f * 1.35f + 18f * 1.35f + 6f * 1f;   // WP-054: bridge ×1.35
            Assert.AreEqual(expected, cost, 1e-3f);
            var open = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 10f), new Vector3(5f, 0f, 10f) };
            Assert.AreEqual(15f, RegionNavRouter.EffectiveCost(_profile, open, open.Length, out NavRegionVolume none), 1e-4f);
            Assert.IsNull(none);
            // the shore route around the bridge: longer in metres, cheaper in effective cost when the multiplier bites
            var shore = new[] { new Vector3(4f, 0f, -10f), new Vector3(18f, 0f, -20f), new Vector3(18f, 0f, -38f), new Vector3(4f, 0f, -44f) };
            float shoreCost = RegionNavRouter.EffectiveCost(_profile, shore, shore.Length, out _);
            float shoreLen = 0f; for (int i = 1; i < shore.Length; i++) shoreLen += Vector3.Distance(shore[i - 1], shore[i]);
            Assert.AreEqual(shoreLen, shoreCost, 1e-3f, "kıyı yolu hacim dışı → maliyet = uzunluk");
            Assert.Greater(shoreLen, 34f, "kıyı yolu köprüden uzun");
            // WP-054: with the bridge at ×1.35 (v3 NavMesh) this synthetic shore is dearer than the bridge (50.4 vs 43.8 m) —
            // diversification (REQ-112) now comes from the acceptance band: a high route roll still takes the shore
            // (alt < direct × (1 + roll × 0.25)), a low roll keeps the bridge. Both sides asserted through the router itself.
            Assert.Less(shoreCost, cost * (1f + _profile.RouteBiasSpread), "kıyı yolu kabul bandı içinde (roll 1)");
            Assert.Greater(shoreCost, cost, "köprü ×1,35 ile kıyı yolu artık düz maliyetten pahalı (roll 0 köprüde kalır)");
            float shoreCostCopy = shoreCost;
            RegionNavRouter.AlternativeQueryOverride = (RegionNavProfile pr, NavRegionVolume v, Vector3 a, Vector3 b, Vector3[] buf, out float c) => { for (int i = 0; i < shore.Length; i++) buf[i] = shore[i]; c = shoreCostCopy; return shore.Length; };
            var direct1 = (Vector3[])direct.Clone();
            Assert.AreEqual(shore.Length, RegionNavRouter.ChooseRoute(_profile, direct1[0], direct1[3], direct1, 4, 1f, out bool rr1)); Assert.IsTrue(rr1, "roll 1 → kıyı");
            var direct0 = (Vector3[])direct.Clone();
            Assert.AreEqual(4, RegionNavRouter.ChooseRoute(_profile, direct0[0], direct0[3], direct0, 4, 0f, out bool rr0)); Assert.IsFalse(rr0, "roll 0 → köprü");
            RegionNavRouter.AlternativeQueryOverride = null;
        }

        [Test]
        public void RotaSecimi_AlternatifKaynagaGore_Deterministik_BosProfilKimlik()
        {
            // WP-040 (WP-039 sanity E2): the choice must not depend on which scene the editor holds — with a baked
            // NavMesh loaded (WP-020 world) the live query legitimately finds the shore alternative (6 corners), without
            // one it cannot. The test installs the query itself: "no NavMesh" → direct; a synthetic alternative → the
            // acceptance rule (alt < direct × (1 + roll × spread)) decides, both ways.
            Vector3[] Direkt() => new[] { new Vector3(4f, 0f, -10f), new Vector3(4f, 0f, -20f), new Vector3(4f, 0f, -38f), new Vector3(4f, 0f, -44f), Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
            var direct = Direkt();
            float directCost = RegionNavRouter.EffectiveCost(_profile, direct, 4, out NavRegionVolume costly);
            Assert.IsNotNull(costly, "direkt rota köprüden geçer (maliyetli hacim)");

            // 1. no NavMesh (or no alternative reachable) → direct, no reroute
            RegionNavRouter.AlternativeQueryOverride = (RegionNavProfile p, NavRegionVolume v, Vector3 a, Vector3 b, Vector3[] buf, out float c) => { c = -1f; return 0; };
            int n = RegionNavRouter.ChooseRoute(_profile, direct[0], direct[3], direct, 4, 1f, out bool rerouted);
            Assert.AreEqual(4, n); Assert.IsFalse(rerouted, "alternatif yok → direkt rota");
            Assert.AreEqual(0, RegionNavRouter.RerouteCount);

            // 2. a synthetic shore alternative (6 corners) cheaper than the acceptance → rerouted, buffer rewritten
            Vector3[] kiyi = { new Vector3(4f, 0f, -10f), new Vector3(18f, 0f, -18f), new Vector3(20f, 0f, -30f), new Vector3(16f, 0f, -40f), new Vector3(8f, 0f, -44f), new Vector3(4f, 0f, -44f) };
            float sentetik = directCost * 0.9f;
            RegionNavRouter.AlternativeQueryOverride = (RegionNavProfile p, NavRegionVolume v, Vector3 a, Vector3 b, Vector3[] buf, out float c) => { for (int i = 0; i < kiyi.Length; i++) buf[i] = kiyi[i]; c = sentetik; return kiyi.Length; };
            direct = Direkt();
            n = RegionNavRouter.ChooseRoute(_profile, direct[0], direct[3], direct, 4, 0f, out rerouted);
            Assert.AreEqual(6, n); Assert.IsTrue(rerouted, "ucuz alternatif kabul edilir (roll 0 → alt < direkt)");
            Assert.AreEqual(kiyi[2], direct[2], "tampon alternatifle yeniden yazılır");
            Assert.AreEqual(1, RegionNavRouter.RerouteCount);

            // 3. the same alternative priced above the acceptance band → direct (roll 1 → alt must be < direkt × (1 + spread))
            sentetik = directCost * (1f + _profile.RouteBiasSpread) + 0.01f;
            direct = Direkt();
            n = RegionNavRouter.ChooseRoute(_profile, direct[0], direct[3], direct, 4, 1f, out rerouted);
            Assert.AreEqual(4, n); Assert.IsFalse(rerouted, "pahalı alternatif reddedilir");
            Assert.AreEqual(1, RegionNavRouter.RerouteCount);

            // 4. empty profile = identity whatever the query says
            RegionNavRouter.AlternativeQueryOverride = (RegionNavProfile p, NavRegionVolume v, Vector3 a, Vector3 b, Vector3[] buf, out float c) => { c = 0f; return 6; };
            var empty = ScriptableObject.CreateInstance<RegionNavProfile>();
            direct = Direkt();
            Assert.AreEqual(4, RegionNavRouter.ChooseRoute(empty, direct[0], direct[3], direct, 4, 1f, out rerouted)); Assert.IsFalse(rerouted, "boş profil: maliyetli hacim yok → sorgu hiç çağrılmaz");
            Object.DestroyImmediate(empty);
            RegionNavRouter.AlternativeQueryOverride = null;
        }

        [Test]
        public void RotaSecimi_GuverteHedefYaDaBaslangic_ViaGeride_AlternatifSorulmaz()
        {
            // WP-088 (R2, V6 C8 / Waterfall 1 stall): a detour that ends on the deck anyway, or a waypoint the body has already reached,
            // is never offered — the query (always the cheapest here, so any call would reroute) must not even be asked. The V6 body
            // beside the C8 waypoint was sent back to it on every 0.35 s repath (detour ≈ 2 × its distance, inside the roll band).
            NavRegionVolume bridge = _profile.Find(new Vector3(4f, 0f, -28.75f));
            Assert.IsTrue(bridge != null && bridge.hasAlternative, "kaynak yok: köprü hacmi ve kıyı alternatifi");
            int calls = 0;
            Vector3[] kiyi = { new Vector3(4f, 0f, -10f), bridge.alternative, new Vector3(4f, 0f, -44f) };
            RegionNavRouter.AlternativeQueryOverride = (RegionNavProfile p, NavRegionVolume v, Vector3 a, Vector3 b, Vector3[] buf, out float c) => { calls++; for (int i = 0; i < kiyi.Length; i++) buf[i] = kiyi[i]; c = 0f; return kiyi.Length; };
            Vector3[] Rota(Vector3 from, Vector3 to) => new[] { from, new Vector3(4f, 0f, -20f), to, Vector3.zero, Vector3.zero, Vector3.zero };

            // positive control: from the north bank to the south bank the waypoint is ahead → asked, rerouted (roll 1)
            Vector3[] r = Rota(new Vector3(4f, 0f, -10f), new Vector3(4f, 0f, -44f));
            Assert.AreEqual(kiyi.Length, RegionNavRouter.ChooseRoute(_profile, r[0], r[2], r, 3, 1f, out bool rerouted));
            Assert.IsTrue(rerouted, "kontrol: kıyıdan kıyıya kıyı rotası seçilir");
            Assert.AreEqual(1, calls);

            // 1. the goal is on the deck (V6 Waterfall / C8 room) → direct
            r = Rota(new Vector3(4f, 0f, -10f), new Vector3(4f, 0f, -26.6f));
            Assert.AreEqual(3, RegionNavRouter.ChooseRoute(_profile, r[0], r[2], r, 3, 1f, out rerouted)); Assert.IsFalse(rerouted, "hedef güvertede → dolambaç yok");
            // 2. the body is on the deck → direct (going back round would end on the same deck)
            r = Rota(new Vector3(4f, 0f, -22f), new Vector3(4f, 0f, -44f));
            Assert.AreEqual(3, RegionNavRouter.ChooseRoute(_profile, r[0], r[2], r, 3, 1f, out rerouted)); Assert.IsFalse(rerouted, "gövde güvertede → dolambaç yok");
            // 3. the body stands at the waypoint (1 m off it) → not ahead any more → direct
            Vector3 atVia = bridge.alternative + new Vector3(-1f, 0f, 0f);
            r = Rota(atVia, new Vector3(4f, 0f, -44f));
            Assert.AreEqual(3, RegionNavRouter.ChooseRoute(_profile, r[0], r[2], r, 3, 1f, out rerouted)); Assert.IsFalse(rerouted, "via'nın yanında → via artık önde değil");
            Assert.AreEqual(1, calls, "korunan durumlarda sorgu hiç çağrılmaz");
            RegionNavRouter.AlternativeQueryOverride = null;
        }

        [Test]
        public void Bogaz_KutuSayimi_KuyrukKurali_AzamiBekleme()
        {
            var solver = new CrowdSolver();
            NavRegionVolume bridge = _profile.Find(new Vector3(4f, 0f, -28f));
            for (int i = 0; i < 3; i++) solver.Register(Uye(new Vector3(4f, 0f, -22f - i)));   // inside the bridge
            solver.Register(Uye(new Vector3(20f, 0f, -22f)));                                   // outside
            Assert.AreEqual(3, solver.CountMembersInBox(bridge.center, bridge.size));
            Assert.AreEqual(4, solver.CountMembersInBox(new Vector3(0f, 0f, 0f), new Vector3(200f, 10f, 200f)));
            // queue rule geometry: member outside, next corner inside a chokepoint → volume reported; capacity 0 → never queue; max wait → never queue
            Assert.IsFalse(RegionNavRouter.ShouldQueue(_profile, new Vector3(4f, 0f, -10f), new Vector3(4f, 0f, -20f), _profile.MaxQueueSeconds, out NavRegionVolume v1));
            Assert.AreSame(bridge, v1, "azami bekleme dolunca hacim yine raporlanır, kuyruk yok");
            Assert.IsFalse(RegionNavRouter.ShouldQueue(_profile, new Vector3(4f, 0f, -25f), new Vector3(4f, 0f, -30f), 0f, out NavRegionVolume v2), "hacmin içindeki üye kuyruğa girmez");
            Assert.IsNull(v2);
            var open = new NavRegionVolume { id = "x", center = Vector3.zero, size = new Vector3(4f, 4f, 4f), capacity = 0 };
            var p2 = ScriptableObject.CreateInstance<RegionNavProfile>(); p2.Set(new[] { open });
            Assert.IsFalse(RegionNavRouter.ShouldQueue(p2, new Vector3(5f, 0f, 0f), Vector3.zero, 0f, out NavRegionVolume v3)); Assert.IsNull(v3);
            Object.DestroyImmediate(p2);
            // occupancy through the service is 0 without a live solver (EditMode) → the rule cannot queue; the full case is PlayMode
            Assert.AreEqual(0, RegionNavRouter.Occupancy(bridge));
            Assert.IsFalse(RegionNavRouter.ShouldQueue(_profile, new Vector3(4f, 0f, -10f), new Vector3(4f, 0f, -20f), 0f, out _));
        }

        [Test]
        public void RotaRulosu_SeedDeterministik_Dagilim_HizFaktoruVarsayilan()
        {
            var a = new NavPathFollower(); var b = new NavPathFollower();
            a.SetRouteSeed(12345u); b.SetRouteSeed(12345u);
            Assert.AreEqual(1f, a.SpeedFactor); Assert.IsFalse(a.IsQueued); Assert.IsFalse(a.IsRerouted);
            float min = 1f, max = 0f;
            for (uint s = 1; s <= 1000; s++)
            {
                var f = new NavPathFollower(); f.SetRouteSeed(s);
                float roll = Rulo(f);
                min = Mathf.Min(min, roll); max = Mathf.Max(max, roll);
            }

            Assert.Less(min, 0.1f); Assert.Greater(max, 0.9f);
            Assert.AreEqual(Rulo(a), Rulo(b), 0f, "aynı seed aynı rulo");
        }

        private static float Rulo(NavPathFollower f)
        {
            var field = typeof(NavPathFollower).GetField("_routeRoll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (float)field.GetValue(f);
        }

        [Test]
        public void SicakYollar_AyirmaSifir_PozitifKontrol()
        {
            var corners = new[] { new Vector3(4f, 0f, -10f), new Vector3(4f, 0f, -20f), new Vector3(4f, 0f, -38f), new Vector3(4f, 0f, -44f) };
            var solver = new CrowdSolver();
            for (int i = 0; i < 40; i++) solver.Register(Uye(new Vector3(i * 0.5f, 0f, -20f)));
            NavRegionVolume bridge = _profile.Find(new Vector3(4f, 0f, -28f));
            // WP-051C: the process-wide heap counters (HeapWindow: GC.GetTotalMemory / mono used size) also see other Editor
            // threads' allocations; the test framework's GC.Alloc recorder is filtered to the calling thread only, and a
            // positive control proves it is not blind.
            byte[] yem = null;
            Assert.That(() => { yem = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.That(() =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    RegionNavRouter.EffectiveCost(_profile, corners, corners.Length, out _);
                    _profile.Find(corners[i & 3]);
                    solver.CountMembersInBox(bridge.center, bridge.size);
                    RegionNavRouter.ShouldQueue(_profile, corners[0], corners[1], 0f, out _);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "sıcak yollar ayırma yapmaz (bu iş parçacığında GC.Alloc örneği görüldü; yem " + (yem != null ? yem.Length : 0) + " B)");
        }

        [Test]
        public void SpawnGorunurluk_EkranIciNoktaFaktoru_EkranDisiBir()
        {
            var config = new SpawnConfigSection(); var tuning = new SpawnWeightTuning();
            Assert.AreEqual(0.35f, tuning.OnScreenFactor, 1e-6f);
            var tanim = new SpawnPointDefinition { Id = "p", Region = RingRegion.North, Position = new Vector3(0f, 0f, 30f), BaseWeight = 1f, Enabled = true, FaceMapCenter = true };
            var p = new SpawnPointRuntime(0, tanim) { PathDistance = 30f, StraightDistance = 30f, PathStatus = NavPathStatus.Complete, LastUsedTime = -999f };
            var inputs = new SpawnWeightInputs { PlayerPosition = Vector3.zero, PlayerRegion = RingRegion.Center, PlayerVelocity = Vector3.zero, Now = 100f, NavDegraded = false };
            p.IsOnScreen = false;
            float off = SpawnWeightCalculator.Evaluate(p, in inputs, config, tuning, SpawnRelaxation.None, out SpawnWeightBreakdown bOff);
            p.IsOnScreen = true;
            float on = SpawnWeightCalculator.Evaluate(p, in inputs, config, tuning, SpawnRelaxation.None, out SpawnWeightBreakdown bOn);
            Assert.Greater(off, 0f);
            Assert.AreEqual(1f, bOff.Visibility); Assert.AreEqual(tuning.OnScreenFactor, bOn.Visibility, 1e-6f);
            Assert.AreEqual(off * tuning.OnScreenFactor, on, 1e-4f, "ekran içi nokta OnScreenFactor ile çarpılır (reddedilmez: 30 m > OnScreenRejectDistance)");
            float relaxed = SpawnWeightCalculator.Evaluate(p, in inputs, config, tuning, SpawnRelaxation.Screen, out SpawnWeightBreakdown bRelax);
            Assert.AreEqual(1f, bRelax.Visibility, "ekran gevşetmesinde faktör 1");
            Assert.AreEqual(off, relaxed, 1e-4f);
        }

        [Test]
        public void AlanMaliyetiAtama_WP020AdliAlanlar_BesHacimAtanir_RaporEdilir()
        {
            // WP-020: World_AP005 bakes Bridge (3) / Corridor (4) / Gate (5) areas — every default volume names one → 5 assignments.
            int applied = _profile.ApplyAreaCosts();
            Assert.AreEqual(5, applied, "5 hacim adlandırılmış alana maliyet yazar (Bridge ×1,35 — WP-054, Corridor ×1,3, Gate ×1 / ×1,15)");
            Assert.AreEqual(1.35f, UnityEngine.AI.NavMesh.GetAreaCost(UnityEngine.AI.NavMesh.GetAreaFromName("Bridge")), 1e-4f);
            Assert.AreEqual(1.3f, UnityEngine.AI.NavMesh.GetAreaCost(UnityEngine.AI.NavMesh.GetAreaFromName("Corridor")), 1e-4f);
            Assert.IsFalse(RegionNavRouter.AreaCostsAppliedOnce);
            RegionNavRouter.EnsureAreaCosts();
            Assert.IsTrue(RegionNavRouter.AreaCostsAppliedOnce); Assert.AreEqual(5, RegionNavRouter.AreaCostsApplied);
        }
    }
}
