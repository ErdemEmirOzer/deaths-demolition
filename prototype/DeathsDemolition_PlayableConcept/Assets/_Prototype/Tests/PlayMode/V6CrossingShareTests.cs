using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-088 r4 (C-00 06.10) · the route share of the C1 stepping causeway on the default V6 world at its shipped cost multiplier
    /// 1.3 (option A: measured 10/24 deck, 14/24 shore at 1.3 and 12/24 / 12/24 at 1.05 — the static model's 0/24 at 1.3 ignores the
    /// router's 0.35 s re-decision; DD_C1SHARE_COST reruns it at another cost). ASSERTED: of the bodies that cross Pond1, the deck share ≥ 25 % and the west
    /// shore share ≥ 25 %; no body stalls beyond the held player's crowd ring (≥ 6 s without moving 0.75 m while more than
    /// <see cref="RingMetres"/> from the player); the stuck watchdog removes no body beyond the ring; no body that chose a route is
    /// left on the way (unreached and more than 8 m from the player when the window ends). Within the ring (the back row of 24
    /// bodies on one standing player) stalls, removals and stuckRecover are reported — S-03's watchdog slice (06.10).
    /// <para>Fixture: the player (invulnerable, held in place) on the south bank past the deck's south end, (−8, −41) — outside the
    /// C1 volume, so the router offers the shore; the spawn director stopped; 24 Standard bodies on the north bank in a front
    /// symmetric about the deck axis: the shore side (x −16 / −14.5), straight on (x −8.75 / −7.25) and the east side (x −1.5 / 0),
    /// each 2 columns × 4 rows (z −13 … −17.5). Positions and spawn seeds are fixed. The route rolls are stratified: every group
    /// carries the same eight rolls, one from the middle of each eighth of [0, 1) (the game draws them uniformly from the spawn
    /// seed, REQ-112), so the shares measure the router's policy on this front, not the luck of eight draws.</para>
    /// <para>A body's route is the first crossing zone it is seen in while inside Pond1's z extent at the deck (z −32 … −25.5, where
    /// the pond leaves only the deck / island or the west shore — asserted on the NavMesh): |x + 8| ≤ 2 → deck, x ≤ −10.5 → shore,
    /// x ≥ −5 → other (east of the pond; counted in the denominator of both shares).</para>
    /// <para>Reported ([DD-OBS] wp088.c1share): the shares, per group, the two-group reading (shore side + straight on only) and
    /// per body its start, roll, the router's deck-keeping threshold θ at the start (the direct route is kept while roll ≤ θ =
    /// (alt / direct − 1) / RouteBiasSpread, from RegionNavRouter's own cost functions on the live NavMesh), route and reach time.</para>
    /// </summary>
    [TestFixture]
    [Category("WP088")]
    public sealed class V6CrossingShareTests
    {
        // ---------------------------------------------------------------- the C1 geometry (asserted against the loaded scene)

        private const string C1Id = "C1_SteppingCauseway";
        /// <summary>GreyboxV6Builder.Crossings[C1Causeway]: deck centre / size (world XZ) and the C1 shore alternative.</summary>
        private static readonly Vector2 DeckCentre = new Vector2(-8f, -28.5f), DeckSize = new Vector2(2f, 17f), ShoreVia = new Vector2(-13.3f, -28.5f);
        /// <summary>Pond1 (centre (−8.4, −29), NotWalkable strips to z −35.4 … −22.6 at the deck): the crossing span, ≥ 2.9 m inside it.</summary>
        private const float SpanZMin = -32f, SpanZMax = -25.5f;
        /// <summary>|x − deck x| ≤ half the deck + DeckBand covers the 3 m island; inside the span the west shore is walkable only west of x ≈ −12.5.</summary>
        private const float DeckBand = 1f, ShoreXMax = -10.5f, OtherXMin = -5f;
        /// <summary>Water beside the deck inside the span (no NavMesh within 0.4 m): the zones above cannot overlap.</summary>
        private static readonly Vector2[] WaterBesideDeck =
        {
            new Vector2(-10.3f, -31.5f), new Vector2(-10.3f, -28.5f), new Vector2(-10.3f, -26f),
            new Vector2(-5.6f, -31.5f), new Vector2(-5.6f, -28.5f), new Vector2(-5.6f, -26f),
        };

        // ---------------------------------------------------------------- the fixture

        private static readonly Vector3 PlayerAt = new Vector3(-8f, 0f, -41f);
        private static readonly (string name, float x0, float x1)[] Groups = { ("kiyiTarafi", -16f, -14.5f), ("duz", -8.75f, -7.25f), ("dogu", -1.5f, 0f) };
        private static readonly float[] Rows = { -13f, -14.5f, -16f, -17.5f };
        private const int Columns = 2, Strata = 8;   // Strata = Rows × Columns: one roll per eighth of [0, 1) in every group
        private const uint SpawnSeed = 1u;
        private const int SpawnRound = 1;

        private const float SimSeconds = 60f, ReachMetres = 3.5f, StallSeconds = 6f, StallMove = 0.75f, StallMetres = 4f,
            StallCheckSeconds = 0.5f, FarUnreachedMetres = 8f, PlayerHoldMetres = 0.75f, SampleOffsetMax = 1f;
        private const float MinShare = 0.25f;
        /// <summary>
        /// S-05 / S-03 06.10: the held player's crowd ring. 24 bodies on one standing player stack beyond the attack ring; the back
        /// row (4–5.6 m, measured at three player spots and both C1 costs) stands still and the stuck watchdog removes it — S-03's
        /// watchdog slice (a body pressed in the ring counts as waiting), not the crossing. Stalls and removals within this ground
        /// distance of the player are reported, not failed; beyond it they fail as before.
        /// </summary>
        public const float RingMetres = 6f;
        private const int MinRouted = 18;   // ≥ 3/4 of the 24 bodies must cross Pond1 or the shares stand on too little

        private static readonly FieldInfo RouteRollField = typeof(NavPathFollower).GetField("_routeRoll", BindingFlags.Instance | BindingFlags.NonPublic);

        private enum Route { None = 0, Deck = 1, Shore = 2, Other = 3 }

        private sealed class Body
        {
            public string Group;
            public int GroupIndex, Row, Column, Stratum;
            public Vector3 Start;
            public ZombieEnemy Z;
            public int LifeId, Flank;
            public float Roll = float.NaN, Theta = float.NaN, DirectCost = -1f, AltCost = -1f;
            public bool DirectCrossesDeck, Gone;
            public Route Route;
            public float RouteAt = -1f, ReachedAt = -1f, AnchorSince;
            public bool ReroutedAtRoute, StallReported;
            public Vector3 Anchor, Last;
        }

        private EnemySpawnDirector _director;
        private PlayerRig _rig;
        private ClassicWeaponId _weaponBefore;
        private Action<EnemyTier, Vector3, ZombieEnemy.OutOfWorldReason, ZombieBodyState> _onOow;
        private NavRegionVolume _c1Overridden;
        private float _c1CostBefore = -1f;

        /// <summary>
        /// Diagnostic switches (S-05 06.10, report only; unset = the acceptance fixture): DD_C1SHARE_COST runs the same fixture at
        /// another C1 cost on the live volume (the r4 control at main's 1.3), DD_C1SHARE_PLAYER_Z holds the player further south
        /// (the crowd ring off the deck landing).
        /// </summary>
        public const string CostVariable = "DD_C1SHARE_COST", PlayerZVariable = "DD_C1SHARE_PLAYER_Z";

        [SetUp]
        public void SetUp()
        {
            _weaponBefore = WeaponRegistry.SelectedWeapon;
        }

        [TearDown]
        public void TearDown()
        {
            if (_onOow != null) { ZombieEnemy.OutOfWorldRemoved -= _onOow; _onOow = null; }
            if (_c1Overridden != null && _c1CostBefore > 0f) _c1Overridden.costMultiplier = _c1CostBefore;
            _c1Overridden = null;
            Time.captureDeltaTime = 0f;
            ZombieEnemy.ResetRisePolicy();
            RunFlowClock.ClearHolds();
            CrowdFeelSettings.ResetStatics();
            RegionNavProfile.ResetStatics();
            WeaponRegistry.SelectedWeapon = _weaponBefore;
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(DD.Prototype.Core.Boot.PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                                     && RunDirector.Instance.Phase == RunPhase.Fighting, 40f, "Run sahnesi / Fighting");
            Assert.AreEqual(WorldSource.V6, WorldSourceSettings.Resolve(), "varsayılan dünya V6 değil");
            yield return Bekle(() => SceneManager.GetSceneByName(WorldSourceSettings.V6SceneName).isLoaded, 40f, WorldSourceSettings.V6SceneName + " yüklenmedi");
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            ZombieEnemy.StuckWindowOverride = 0f;                // the shipped watchdog window
            RegionNavRouter.AlternativeQueryOverride = null;     // the live NavMesh query, as in the game
            CrowdFeelSettings.ResetStatics();                    // the shipped Resources assets, not a test's leftover
            RegionNavProfile.ResetStatics();
            // the director stopped: only this fixture's bodies walk
            _director.ForceActiveTarget(0, false);
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;
        }

        /// <summary>
        /// The acceptance test (C-00 06.10): C1 deck share ≥ 25 % and shore share ≥ 25 % on the V6 north-bank front, no stall and
        /// no stuck removal beyond the player's crowd ring.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator C1_GuverteVeKiyiPayi_HerBiri25Ustu_TakilmaYok()
        {
            yield return Kur();

            // ---- preconditions: the C1 volume and the pond the zones stand on
            NavRegionVolume c1 = null;
            foreach (NavRegionMarker m in UnityEngine.Object.FindObjectsOfType<NavRegionMarker>())
                if (m.Volume != null && m.Volume.id == C1Id) { c1 = m.Volume; break; }
            Assert.IsNotNull(c1, "ön koşul: sahnede " + C1Id + " bölge hacmi yok (V6 dünyası mı?)");
            Assert.AreEqual(NavRegionKind.Corridor, c1.kind, "ön koşul: C1 türü");
            Assert.AreEqual(DeckCentre.x, c1.center.x, 0.05f, "ön koşul: C1 merkezi x (geometri değişti mi?)");
            Assert.AreEqual(DeckCentre.y, c1.center.z, 0.05f, "ön koşul: C1 merkezi z (geometri değişti mi?)");
            Assert.AreEqual(DeckSize.x, c1.size.x, 0.05f, "ön koşul: C1 genişliği");
            Assert.AreEqual(DeckSize.y, c1.size.z, 0.05f, "ön koşul: C1 uzunluğu");
            Assert.IsTrue(c1.hasAlternative, "ön koşul: C1 kıyı alternatifi yok");
            Assert.AreEqual(ShoreVia.x, c1.alternative.x, 0.05f, "ön koşul: C1 kıyı alternatifi x");
            Assert.AreEqual(ShoreVia.y, c1.alternative.z, 0.05f, "ön koşul: C1 kıyı alternatifi z");
            foreach (Vector2 w in WaterBesideDeck)
                Assert.IsFalse(NavMesh.SamplePosition(new Vector3(w.x, 0.1f, w.y), out NavMeshHit _, 0.4f, NavMesh.AllAreas),
                    "ön koşul: geçit açıklığında güverte ile kıyı arasında NavMesh var (" + w.x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                    + w.y.ToString("0.0", CultureInfo.InvariantCulture) + ") — bölge sınıflaması geçersiz");

            float costOverride = EnvFloat(CostVariable), playerZ = EnvFloat(PlayerZVariable);
            if (!float.IsNaN(costOverride))
            {
                _c1Overridden = c1;
                _c1CostBefore = c1.costMultiplier;
                c1.costMultiplier = costOverride;
            }

            Vector3 playerAt = float.IsNaN(playerZ) ? PlayerAt : new Vector3(PlayerAt.x, PlayerAt.y, playerZ);
            RegionNavProfile profile = RegionNavProfile.Active;
            Assert.IsTrue(NavMesh.SamplePosition(playerAt + Vector3.up * 0.5f, out NavMeshHit playerHit, 1.5f, NavMesh.AllAreas), "ön koşul: oyuncu noktasında NavMesh yok");
            Vector3 goal = playerHit.position;
            Assert.LessOrEqual(Flat(goal, playerAt), SampleOffsetMax, "ön koşul: oyuncu noktası NavMesh'e > 1 m kaydı");
            Assert.IsFalse(c1.Contains(goal), "ön koşul: oyuncu C1 hacminin içinde (kıyı rotası hiç sunulmaz)");

            var bodies = new List<Body>(Rows.Length * Groups.Length * Columns);
            for (int r = 0; r < Rows.Length; r++)
            for (int g = 0; g < Groups.Length; g++)
            for (int c = 0; c < Columns; c++)
            {
                var at = new Vector3(c == 0 ? Groups[g].x0 : Groups[g].x1, 0f, Rows[r]);
                Assert.IsTrue(NavMesh.SamplePosition(at + Vector3.up * 0.5f, out NavMeshHit h, 1.5f, NavMesh.AllAreas), "ön koşul: başlangıçta NavMesh yok " + at);
                Assert.LessOrEqual(Flat(h.position, at), SampleOffsetMax, "ön koşul: başlangıç NavMesh'e > 1 m kaydı " + at);
                bodies.Add(new Body { Group = Groups[g].name, GroupIndex = g, Row = r, Column = c, Stratum = c == 0 ? 2 * r : Strata - 1 - 2 * r, Start = h.position });
            }

            Body straight = bodies.Find(b => b.GroupIndex == 1);
            Assert.IsTrue(RegionNavRouter.AlternativeAhead(c1, straight.Start, goal, profile.AlternativeAheadMargin),
                "ön koşul: düz gelen gövdeye kıyı alternatifi sunulmuyor (RegionNavRouter.AlternativeAhead)");

            _rig.Teleport(goal + Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.forward));
            yield return null;
            yield return null;

            // ---- the router's own view of each start (report only): direct cost, shore cost, deck-keeping threshold θ
            yield return Predict(bodies, c1, goal, profile);

            // ---- spawn: fixed spawn seeds, stratified route rolls
            uint[] routeSeeds = StratifiedRouteSeeds();
            int rec0 = ZombieEnemy.StuckRecoveries, rem0 = ZombieEnemy.StuckRemovals, oow0 = ZombieEnemy.OutOfWorldRemovals;
            int switches0 = RegionNavRouter.RouteSwitchCount, queue0 = RegionNavRouter.QueueEvents;
            var oowAt = new List<string>();
            int oowRing = 0, oowFar = 0;
            _onOow = (t, p, why, s) =>
            {
                bool ring = Flat(p, goal) <= RingMetres;
                if (ring) oowRing++; else oowFar++;
                oowAt.Add(why + "@(" + F1(p.x) + "," + F1(p.z) + " d=" + F1(Flat(p, goal)) + (ring ? " ring" : "") + ") " + s);
            };
            ZombieEnemy.OutOfWorldRemoved += _onOow;
            Time.captureDeltaTime = 1f / 60f;   // batch frames are 1–3 ms: the game's 60 fps step (and its per-frame path-query budget)
            int spawned = 0, holds = 0, stalls = 0, ringStalls = 0;
            float simSeconds = 0f;
            var stallAt = new StringBuilder();
            var report = new StringBuilder();
            try
            {
                EnemyFactory.ResetSeedForTests(SpawnSeed);
                foreach (Body b in bodies)
                {
                    Vector3 to = goal - b.Start; to.y = 0f;
                    ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, SpawnRound, b.Start, Quaternion.LookRotation(to.normalized));
                    if (z == null) continue;
                    spawned++;
                    b.Z = z; b.LifeId = z.LifeId; b.Flank = z.FlankSign; b.Anchor = b.Last = z.transform.position;
                    if (routeSeeds != null && z.PathFollower != null) z.PathFollower.SetRouteSeed(routeSeeds[b.Stratum]);
                    b.Roll = RouteRollOf(z.PathFollower);
                }

                float t0 = Time.time, nextStall = t0;
                foreach (Body b in bodies) b.AnchorSince = t0;
                while (Time.time - t0 < SimSeconds)
                {
                    yield return null;
                    float now = Time.time;
                    if (Flat(_rig.transform.position, goal) > PlayerHoldMetres) { _rig.Teleport(goal + Vector3.up * 0.05f, _rig.transform.rotation); holds++; }
                    Vector3 pp = _rig.transform.position;
                    bool stallCheck = now >= nextStall;
                    if (stallCheck) nextStall = now + StallCheckSeconds;
                    bool open = false;
                    foreach (Body b in bodies)
                    {
                        if (b.Z == null || b.Gone) continue;
                        ZombieEnemy z = b.Z;
                        if (!z.IsSpawned || !z.IsAlive || z.LifeId != b.LifeId) { b.Gone = true; continue; }
                        Vector3 p = z.transform.position;
                        b.Last = p;
                        float d = Flat(p, pp);
                        if (b.Route == Route.None)
                        {
                            Route route = Classify(p);
                            if (route != Route.None)
                            {
                                b.Route = route; b.RouteAt = now - t0;
                                b.ReroutedAtRoute = z.PathFollower != null && z.PathFollower.IsRerouted;
                            }
                        }

                        if (b.ReachedAt < 0f && d <= ReachMetres) b.ReachedAt = now - t0;
                        if (b.ReachedAt < 0f) open = true;
                        if (!stallCheck) continue;
                        if (Flat(p, b.Anchor) > StallMove) { b.Anchor = p; b.AnchorSince = now; }
                        else if (!b.StallReported && d > StallMetres && now - b.AnchorSince >= StallSeconds)
                        {
                            b.StallReported = true;
                            if (d > RingMetres) stalls++; else ringStalls++;
                            NavPathFollower f = z.PathFollower;
                            stallAt.Append("(" + F1(p.x) + "," + F1(p.z) + " d=" + F1(d) + (d > RingMetres ? "" : " ring") + " " + b.Group + " route=" + b.Route
                                           + " queue=" + (f != null && f.IsQueued && f.QueueVolume != null ? f.QueueVolume.id : "-") + " wait=" + (z.IsWaiting ? 1 : 0) + ") ");
                        }
                    }

                    simSeconds = now - t0;
                    if (!open) break;   // every body reached the player (or left)
                }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                if (_onOow != null) { ZombieEnemy.OutOfWorldRemoved -= _onOow; _onOow = null; }
                _director.ForceActiveTarget(0, false);
                _director.ClearAllEnemies();
                EnemyFactory.ClearAll();
            }

            // ---- counts
            int dRec = ZombieEnemy.StuckRecoveries - rec0, dRem = ZombieEnemy.StuckRemovals - rem0, dOow = ZombieEnemy.OutOfWorldRemovals - oow0;
            int deck = 0, shore = 0, other = 0, none = 0, reached = 0, farUnreached = 0;
            var far = new StringBuilder();
            var groupDeck = new int[Groups.Length];
            var groupShore = new int[Groups.Length];
            var groupOther = new int[Groups.Length];
            var groupReached = new int[Groups.Length];
            var groupExpDeck = new float[Groups.Length];
            var groupN = new int[Groups.Length];
            float expDeck = 0f;
            int expN = 0, predDeck = 0;
            foreach (Body b in bodies)
            {
                if (b.Z == null) continue;
                groupN[b.GroupIndex]++;
                switch (b.Route)
                {
                    case Route.Deck: deck++; groupDeck[b.GroupIndex]++; break;
                    case Route.Shore: shore++; groupShore[b.GroupIndex]++; break;
                    case Route.Other: other++; groupOther[b.GroupIndex]++; break;
                    default: none++; break;
                }

                if (b.ReachedAt >= 0f) { reached++; groupReached[b.GroupIndex]++; }
                else if (b.Route != Route.None && Flat(b.Last, goal) > FarUnreachedMetres)
                {
                    farUnreached++;
                    far.Append("(" + F1(b.Last.x) + "," + F1(b.Last.z) + " " + b.Group + " route=" + b.Route + ") ");
                }

                if (!float.IsNaN(b.Theta))
                {
                    float pDeck = Mathf.Clamp01(b.Theta);
                    expDeck += pDeck; expN++;
                    groupExpDeck[b.GroupIndex] += pDeck;
                    if (!float.IsNaN(b.Roll) && b.Roll <= b.Theta) predDeck++;
                }
            }

            int routed = deck + shore + other;
            float deckShare = routed > 0 ? (float)deck / routed : 0f, shoreShare = routed > 0 ? (float)shore / routed : 0f;
            int twoDeck = groupDeck[0] + groupDeck[1], twoShore = groupShore[0] + groupShore[1], twoRouted = twoDeck + twoShore + groupOther[0] + groupOther[1];

            report.Append("[DD-OBS] wp088.c1share deck=" + deck + "/" + routed + " (" + F2(deckShare) + ") shore=" + shore + "/" + routed + " (" + F2(shoreShare) + ")"
                          + " other=" + other + " none=" + none + " spawned=" + spawned + " reached=" + reached + " stalls=" + stalls
                          + " ringStalls=" + ringStalls + " stuckRecover=" + dRec + " stuckRemove=" + dRem + " oow=" + dOow + " (ring " + oowRing + ", far " + oowFar + ")"
                          + " farUnreached=" + farUnreached
                          + " routeSwitches=" + (RegionNavRouter.RouteSwitchCount - switches0) + " queueEvents=" + (RegionNavRouter.QueueEvents - queue0)
                          + " cost=" + F2(c1.costMultiplier) + " cap=" + c1.capacity + " spread=" + F2(profile.RouteBiasSpread)
                          + " margin=" + F2(profile.AlternativeAheadMargin) + " maxQueue=" + F2(profile.MaxQueueSeconds)
                          + " expDeck=" + (expN > 0 ? F2(expDeck / expN) : "-") + " predDeck=" + predDeck + "/" + spawned
                          + " strat=" + (routeSeeds != null ? "on" : "off") + " seed=" + SpawnSeed + " sim=" + F1(simSeconds) + "s playerHolds=" + holds
                          + " overrides=" + (_c1Overridden != null ? "cost(" + F2(_c1CostBefore) + "->" + F2(c1.costMultiplier) + ")" : "-")
                          + (float.IsNaN(EnvFloat(PlayerZVariable)) ? "" : " playerZ=" + F1(goal.z)) + "\n");
            report.Append("  twoGroup(kiyiTarafi+duz) deck=" + twoDeck + "/" + twoRouted + " shore=" + twoShore + "/" + twoRouted
                          + " · sentence: duz.deck=" + groupDeck[1] + "/" + groupN[1] + " kiyiTarafi.shore=" + groupShore[0] + "/" + groupN[0] + "\n");
            for (int g = 0; g < Groups.Length; g++)
                report.Append("  group " + Groups[g].name + " n=" + groupN[g] + " deck=" + groupDeck[g] + " shore=" + groupShore[g] + " other=" + groupOther[g]
                              + " reached=" + groupReached[g] + " expDeck=" + F2(groupN[g] > 0 ? groupExpDeck[g] / groupN[g] : 0f) + "\n");
            for (int i = 0; i < bodies.Count; i++)
            {
                Body b = bodies[i];
                report.Append("  b" + i.ToString("00", CultureInfo.InvariantCulture) + " " + b.Group + " r" + b.Row + "c" + b.Column + " k=" + b.Stratum
                              + " start=(" + F2(b.Start.x) + "," + F2(b.Start.z) + ") roll=" + F2(b.Roll) + " theta=" + F2(b.Theta)
                              + " direct=" + F1(b.DirectCost) + (b.DirectCrossesDeck ? "(deck)" : "(off-deck)") + " alt=" + F1(b.AltCost)
                              + " flank=" + (b.Z != null ? b.Flank.ToString(CultureInfo.InvariantCulture) : "-")
                              + " -> " + b.Route + (b.RouteAt >= 0f ? "@" + F1(b.RouteAt) + "s rerouted=" + (b.ReroutedAtRoute ? 1 : 0) : "")
                              + " reached=" + (b.ReachedAt >= 0f ? F1(b.ReachedAt) + "s" : "-") + " end=(" + F1(b.Last.x) + "," + F1(b.Last.z) + ") d=" + F1(Flat(b.Last, goal))
                              + (b.Gone ? " gone" : "") + "\n");
            }

            report.Append("  stalls: " + (stallAt.Length > 0 ? stallAt.ToString() : "-") + "\n");
            report.Append("  farUnreached: " + (far.Length > 0 ? far.ToString() : "-") + "\n");
            report.Append("  oow: " + (oowAt.Count > 0 ? string.Join("; ", oowAt) : "-"));
            Debug.Log(report.ToString());

            // ---- the source is in the record: the bodies were spawned and crossed the pond
            Assert.AreEqual(bodies.Count, spawned, "kaynak yok: " + bodies.Count + " gövdenin hepsi doğmadı\n" + report);
            Assert.GreaterOrEqual(routed, MinRouted, "kaynak yok: Pond1'i geçen (rota seçen) gövde < " + MinRouted + "\n" + report);

            var fails = new List<string>();
            if (deckShare < MinShare) fails.Add("güverte payı " + F2(deckShare) + " < " + F2(MinShare) + " (" + deck + "/" + routed + ")");
            if (shoreShare < MinShare) fails.Add("kıyı payı " + F2(shoreShare) + " < " + F2(MinShare) + " (" + shore + "/" + routed + ")");
            if (stalls > 0) fails.Add("takılma " + stalls + " (≥ " + StallSeconds + " s, < " + StallMove + " m, oyuncu halkası dışında > " + RingMetres + " m)");
            // removals carry their position: only those beyond the ring fail; the ring's are S-03's watchdog slice (reported above).
            // stuckRecover has no position, so it cannot tell the ring's back row from a crossing body: reported only until S-03's
            // watchdog slice makes the ring a wait, then asserted again (stuckRecover 0) — S-03 measures that slice on this fixture.
            if (oowFar != 0) fails.Add("halka dışında kaldırma " + oowFar + " (toplam oow " + dOow + ", stuckRemove " + dRem + ")");
            if (farUnreached > 0) fails.Add("yolda kalan " + farUnreached + " (rota seçti, " + F1(SimSeconds) + " s sonunda oyuncudan > " + F1(FarUnreachedMetres) + " m)");
            Assert.IsEmpty(fails, string.Join("\n", fails) + "\n" + report);
        }

        /// <summary>
        /// The router's decision at each start, from its own functions: the direct corners' effective cost (RegionNavRouter.EffectiveCost),
        /// whether that route crosses C1, and the cost through C1's shore alternative (RegionNavRouter.TryAlternative, two budgeted path
        /// queries — one start per frame). θ = (alt / direct − 1) / RouteBiasSpread: the body keeps the deck while its roll ≤ θ; 0 when the
        /// direct route avoids the deck, 1 when no detour is offered. Report only: the body re-decides on every repath as it walks.
        /// </summary>
        private static IEnumerator Predict(List<Body> bodies, NavRegionVolume c1, Vector3 goal, RegionNavProfile profile)
        {
            NavPathService paths = CrowdService.Paths;
            Assert.IsNotNull(paths, "ön koşul: CrowdService.Paths yok");
            var direct = new Vector3[NavPathService.MaxCorners];
            var alt = new Vector3[NavPathService.MaxCorners];
            foreach (Body b in bodies)
            {
                int n = paths.CalculateCorners(b.Start, goal, direct, out NavPathStatus status);
                Assert.IsTrue(n >= 2 && status == NavPathStatus.Complete, "ön koşul: başlangıçtan oyuncuya tam yol yok " + b.Start + " (" + status + ")");
                b.DirectCost = RegionNavRouter.EffectiveCost(profile, direct, n, out NavRegionVolume costly);
                b.DirectCrossesDeck = costly == c1;
                if (!b.DirectCrossesDeck) { b.Theta = 0f; continue; }
                if (!RegionNavRouter.AlternativeAhead(c1, b.Start, goal, profile.AlternativeAheadMargin)) { b.Theta = 1f; continue; }
                int na = 0;
                float altCost = -1f;
                for (int attempt = 0; attempt < 5 && na < 2; attempt++)
                {
                    yield return null;   // a fresh frame: TryAlternative takes two queries from the per-frame budget
                    na = RegionNavRouter.TryAlternative(profile, c1, b.Start, goal, alt, out altCost);
                }

                b.AltCost = na >= 2 ? altCost : -1f;
                b.Theta = na >= 2 && b.DirectCost > 0f ? (altCost / b.DirectCost - 1f) / Mathf.Max(1e-4f, profile.RouteBiasSpread) : float.NaN;
            }
        }

        /// <summary>The crossing zone of <paramref name="p"/> inside Pond1's span (None outside it or over the water either side of the deck).</summary>
        private static Route Classify(Vector3 p)
        {
            if (p.z < SpanZMin || p.z > SpanZMax) return Route.None;
            if (Mathf.Abs(p.x - DeckCentre.x) <= DeckSize.x * 0.5f + DeckBand) return Route.Deck;
            if (p.x <= ShoreXMax) return Route.Shore;
            if (p.x >= OtherXMin) return Route.Other;
            return Route.None;
        }

        /// <summary>
        /// One route seed per eighth of [0, 1): the first seed counting up from 1 whose roll lies in the middle half of stratum k
        /// (read back from a probe follower). Null when the roll cannot be read (NavPathFollower changed): the bodies then keep
        /// their spawn-seed rolls and the report says strat=off.
        /// </summary>
        private static uint[] StratifiedRouteSeeds()
        {
            if (RouteRollField == null || RouteRollField.FieldType != typeof(float)) return null;
            var probe = new NavPathFollower(4);
            var seeds = new uint[Strata];
            int found = 0;
            for (uint seed = 1u; seed < 200000u && found < Strata; seed++)
            {
                probe.SetRouteSeed(seed);
                float roll = RouteRollOf(probe);
                int k = Mathf.Clamp((int)(roll * Strata), 0, Strata - 1);
                float inStratum = roll * Strata - k;
                if (seeds[k] != 0u || inStratum < 0.25f || inStratum > 0.75f) continue;
                seeds[k] = seed;
                found++;
            }

            return found == Strata ? seeds : null;
        }

        private static float RouteRollOf(NavPathFollower follower)
            => follower != null && RouteRollField != null && RouteRollField.FieldType == typeof(float) ? (float)RouteRollField.GetValue(follower) : float.NaN;

        private static float EnvFloat(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrEmpty(v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : float.NaN;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string F1(float v) => float.IsNaN(v) ? "?" : v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string F2(float v) => float.IsNaN(v) ? "?" : v.ToString("0.00", CultureInfo.InvariantCulture);

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - t > saniye) Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                yield return null;
            }
        }
    }
}
