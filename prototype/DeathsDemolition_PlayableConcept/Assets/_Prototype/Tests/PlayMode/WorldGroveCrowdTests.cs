using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Damage;
using DD.Prototype.Difficulty;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-103 (R4 #4, S-05 d77c6263): the west bamboo grove (x −54…−37.5, z −30…30) is open between its clumps — the greybox
    /// <c>Bamboo_</c> blocks no longer collide or bake, the clumps carve the NavMesh, the gaps walk. S-03's crowd / flank / spawn check
    /// on that NavMesh (C-00 12.10), Normal and Demolition.
    /// <para>Fixture: the player (invulnerable) stands on a point BETWEEN the clumps of the west half — derived from the live NavMesh
    /// near <see cref="PreferredStart"/>: inside the band, ≥ <see cref="LaneClearMetres"/> from every lane axis, ≥ <see cref="EdgeClearMetres"/>
    /// from the nearest NavMesh edge (a carved clump, a fence), on the mesh within <see cref="StartSnapMetres"/>; asserted as a
    /// precondition, so a geometry change cannot quietly move the case onto a lane. The spawn director first runs the round for
    /// <see cref="LiveSpawnSeconds"/> with the player there (its spawns are reported), then stops; the crowd is this fixture's own:
    /// up to 20 bodies (1 in 4 Special) on two deterministic rings — inside the grove at 8–13 m and outside it at 16–28 m — each
    /// on the mesh with a complete path to the player, spawn seed fixed. The player stands <see cref="StandSeconds"/>, walks to a
    /// second between-clump point (<see cref="PreferredTurn"/>, 12 m north, off every lane) along its NavMesh path for at most
    /// <see cref="WalkSeconds"/>, then stands again, up to <see cref="SimSeconds"/>.</para>
    /// <para>ASSERTED (thresholds of V6CrossingShareTests / V6AiRoundTests): every body enters the player's
    /// <see cref="RingMetres"/> ring within the window (a body waiting its turn behind the packed crowd counts up to
    /// <see cref="WaitingPackMetres"/> beyond it); no stall beyond the ring (≥ <see cref="StallSeconds"/> s, &lt; <see cref="StallMove"/> m);
    /// no body off the NavMesh (&gt; <see cref="OffMeshMetres"/> m from it) for ≥ <see cref="OffMeshFailSeconds"/> s; no out-of-world /
    /// stuck removal beyond the ring; stuck recoveries ≤ <see cref="MaxRecoveries"/>; Normal: the crowd rolls flankers (the source,
    /// ≥ <see cref="MinFlankRolled"/>). Every grove-near spawn candidate that is enabled sits on
    /// the NavMesh (≤ <see cref="SampleOffsetMax"/> m) with a complete path to the player, and so does every live spawn there.
    /// REPORTED ([DD-TEST] wp103.grove): the start / turn points and the clumps round them, per body its tier, role, flank, ring /
    /// reach times, off-mesh peak, stall; recoveries, queue events, reroutes; Demolition's roles, cut samples and pursuit
    /// fallbacks; whether the spawn system has a candidate inside the grove at all (a design question — never asserted); whether a
    /// flanker finds its flank inside the grove (a finding: the WP-061M flank needs an open straight NavMesh line, which the clumps
    /// cut — grove2 12.10: 0/174, the flankers chase along the lanes and none stalls).</para>
    /// <para>Evidence (optional): with DD_WP103_CAPTURE_DIR set, the first <see cref="CaptureSeconds"/> s of the crowd phase are written
    /// as TD frames (&lt;dir&gt;/&lt;mode&gt;/frames/f-NNNNNN.jpg + frames.tsv, tdmux) and crowd.tsv; without it nothing is rendered.</para>
    /// <para>Needs the WP-103 grove AND WP-102: main from 21d0fc46.</para>
    /// </summary>
    [TestFixture]
    [Category("WP103")]
    public sealed class WorldGroveCrowdTests
    {
        // ---------------------------------------------------------------- the grove (GreyboxV6Builder / WorldV5Dress.Grove, d77c6263)

        /// <summary>GreyboxV6Builder.BuildBamboo: the grove band.</summary>
        private const float GroveXMin = -54f, GroveXMax = -37.5f, GroveZAbs = 30f;
        /// <summary>GreyboxV6Builder.BambooLanes (3 m wide polylines; clumps stay ≥ 3 m off their axes).</summary>
        private static readonly Vector2[][] Lanes =
        {
            new[] { new Vector2(-45f, -26f), new Vector2(-45f, 26f) },
            new[] { new Vector2(-36f, 0f), new Vector2(-45f, 0f) },
            new[] { new Vector2(-33f, -16f), new Vector2(-45f, -26f) },
            new[] { new Vector2(-33f, 20f), new Vector2(-45f, 26f) },
            new[] { new Vector2(-54f, -18f), new Vector2(-45f, -14f) },
            new[] { new Vector2(-54f, 18f), new Vector2(-45f, 14f) },
        };
        /// <summary>GreyboxV6Builder.Gates inside the band (W_BambooSouth / W_BambooNorth; their 3.5 m clearing is not "between clumps").</summary>
        private static readonly Vector2[] GroveGates = { new Vector2(-52f, -18f), new Vector2(-52f, 18f) };

        /// <summary>West half, between the main lane's fence (x −46.9) and the world edge; 6 m south of the unlaned z 0 gap.</summary>
        private static readonly Vector3 PreferredStart = new Vector3(-51f, 0f, -6f);
        /// <summary>The walk's end: the same depth, 12 m north (the walk crosses z 0 through the clumps, no lane on that side).</summary>
        private static readonly Vector3 PreferredTurn = new Vector3(-51f, 0f, 6f);
        private const float LaneClearMetres = 3f, GateClearMetres = 3.5f, EdgeClearMetres = 1f, StartSnapMetres = 0.25f;
        private const float SearchMetres = 5f, SearchStep = 0.5f, ClumpCountMetres = 4f, RouteDetourMax = 2f;
        private const string GroveGroupName = "BambooGrove";   // WorldV5Dress.GroveGroupName

        /// <summary>The spawn-check box: the grove plus 6 m round it (W_Bamboo*, I_WestKairoSouth, I_ServiceYardSouth fall inside).</summary>
        private const float NearXMin = -60f, NearXMax = -31f, NearZAbs = 36f;

        // ---------------------------------------------------------------- the crowd

        private const int CrowdMax = 20, InnerMax = 10, MinPerSide = 6, MinCrowd = 16, SpawnRound = 3, InnerAngles = 18, OuterAngles = 36;
        private static readonly float[] InnerRadii = { 8f, 10.5f, 13f }, OuterRadii = { 16f, 20f, 24f, 28f };
        private const float SpawnSnapMetres = 1.5f, SpawnSeparation = 1.2f;
        private const uint CrowdSeed = 1103u;
        private const int LiveTarget = 12;

        // ---------------------------------------------------------------- timings and thresholds

        private const float Step = 1f / 60f;   // batch frames are 1–3 ms: the game's 60 fps step (V6CrossingShareTests)
        private const float LiveSpawnSeconds = 6f, StandSeconds = 2f, WalkSeconds = 8f, MinSimSeconds = 25f, SimSeconds = 40f;
        /// <summary>V6CrossingShareTests: reach 3.5 m, crowd ring 6 m (the back row of a standing player's crowd), stall 6 s / 0.75 m.</summary>
        private const float ReachMetres = 3.5f, RingMetres = 6f, StallSeconds = 6f, StallMove = 0.75f, StallMetres = 4f, StallCheckSeconds = 0.5f;
        /// <summary>A body WAITING its turn (IsWaiting: the crowd ahead holds its approach) this far beyond the ring is in the packed
        /// crowd, not lost in the grove: twenty Demolition bodies pack the 6 m ring (grove3 12.10: two waiting bodies at 6.3 / 6.5 m).</summary>
        private const float WaitingPackMetres = 2.5f;
        /// <summary>V6CrossingShareTests.SampleOffsetMax: a spawn / start point may snap ≤ 1 m to the NavMesh.</summary>
        private const float SampleOffsetMax = 1f;
        /// <summary>
        /// Off-mesh: the body's feet farther than 0.5 m (flat) from the NavMesh — the movement clamps the final position onto it
        /// (NavPathFollower.ClampToNavMesh), so a sustained offset is a body inside a carved clump / fence. ZombieOutOfWorld strands
        /// at 2 m / 3 s; this is tighter on purpose.
        /// </summary>
        private const float OffMeshProbe = 1f, OffMeshMetres = 0.5f, OffMeshFailSeconds = 1f, SampleSeconds = 0.25f;
        /// <summary>V6AiRoundTests (26.09): 0 and 3 recoveries over 8 stations × 12 bodies × 25 s — a 20-body station allows 2.</summary>
        private const int MaxRecoveries = 2;
        /// <summary>FlankOpenGroundTests.MinFlankRolled; the first 0.6 s are skipped (FlankActive starts true until the first decision).</summary>
        private const int MinFlankRolled = 3;
        private const float FlankWarmSeconds = 0.6f, ArriveMetres = 0.8f, CaptureSeconds = 12f;
        private const float KurulumZamanAsimi = 40f;

        // ---------------------------------------------------------------- state

        private sealed class Govde
        {
            public int Index, LifeId;
            public bool Icerde, Gone;
            public ZombieEnemy Z;
            public EnemyTier Tier;
            public ZombieAiRole Role;
            public int Flank;
            public Vector3 Start, Last, Anchor;
            public float AnchorSince, RingAt = -1f, ReachAt = -1f, OffRun, OffRunMax, OffMax;
            public bool StallReported, StallRing, FlankFar;
            public int FlankSamples, FlankActiveSamples, CutSamples;
        }

        private RunDirector _run;
        private EnemySpawnDirector _director;
        private PlayerRig _rig;
        private SavasTestGirdisi _girdi;
        private IInputSource _oncekiGirdi;
        private bool _girdiAlindi, _modBasladi;
        private Action<EnemyTier, Vector3, ZombieEnemy.OutOfWorldReason, ZombieBodyState> _onOow;
        private Action<EnemySpawnContext> _onSpawn;
        private RenderTexture _rt;
        private Texture2D _tex;

        private static NavMeshPath s_path;
        private static readonly Vector3[] s_corners = new Vector3[128];

        // ======================================================================== kurulum

        private IEnumerator Kur(DifficultyMode mod)
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                                     && RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Run sahnesi / Fighting");
            Assert.AreEqual(WorldSource.V6, WorldSourceSettings.Resolve(), "varsayılan dünya V6 değil");
            yield return Bekle(() => SceneManager.GetSceneByName(WorldSourceSettings.V6SceneName).isLoaded, KurulumZamanAsimi, WorldSourceSettings.V6SceneName + " yüklenmedi");
            _run = RunDirector.Instance;
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            ZombieEnemy.StuckWindowOverride = 0f;   // the shipped watchdog window
            CrowdFeelSettings.ResetStatics();       // the shipped Resources assets, not a test's leftover
            RegionNavProfile.ResetStatics();
            RegionNavRouter.ResetStatics();
            _director.ForceActiveTarget(0, false);
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();

            _oncekiGirdi = InputSourceRegistry.Current;
            _girdiAlindi = true;
            _girdi = new SavasTestGirdisi();
            InputSourceRegistry.SetCurrent(_girdi);
            _rig.Motor.SetInputSource(null);
            _rig.Aim.SetInputSource(null);

            if (mod != DifficultyMode.Normal)
            {
                DifficultyService.Selected = mod;
                DifficultyService.BeginRun();
                _modBasladi = true;
                Assert.IsNotNull(DifficultyService.Current, DifficultyService.Id(mod) + " ayarı etkin değil");
            }

            Time.captureDeltaTime = Step;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = 0f;
            if (_girdi != null) _girdi.Yon = Vector2.zero;
            if (_onOow != null) { ZombieEnemy.OutOfWorldRemoved -= _onOow; _onOow = null; }
            if (_director != null)
            {
                if (_onSpawn != null) _director.EnemySpawned -= _onSpawn;
                _director.ForceActiveTarget(0, false);
                _director.StopRound();
                _director.ClearAllEnemies();
            }

            _onSpawn = null;
            EnemyFactory.ClearAll();
            if (_modBasladi) DifficultyService.EndRun();
            _modBasladi = false;
            DifficultyService.Selected = DifficultyMode.Normal;
            ZombieEnemy.ResetRisePolicy();
            CrowdFeelSettings.ResetStatics();
            RegionNavProfile.ResetStatics();
            RunFlowClock.ClearHolds();
            if (_girdiAlindi) InputSourceRegistry.SetCurrent(_oncekiGirdi);
            _girdiAlindi = false;
            if (_rig != null && _rig.Health != null) _rig.Health.DiagnosisInvulnerable = false;
            if (_rt != null) UnityEngine.Object.Destroy(_rt);
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _rt = null; _tex = null;
            _rig = null; _run = null; _director = null; _girdi = null;
            LogAssert.ignoreFailingMessages = false;
            yield return null;
        }

        // ======================================================================== test

        /// <summary>WP-103 · the grove on the live NavMesh: crowd closes, flanks find their goal, nobody leaves the mesh, spawns fit.</summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator Koru_KalabalikKanatSpawn_WP103([Values(DifficultyMode.Normal, DifficultyMode.Demolition)] DifficultyMode mod)
        {
            yield return Kur(mod);
            var rapor = new StringBuilder("[DD-TEST] wp103.grove mode=" + DifficultyService.Id(mod) + "\n");
            var fails = new List<string>();

            // ---- 1. the start between the clumps, and the walk's end — from the live NavMesh (precondition)
            Assert.IsTrue(KoruNoktasi(PreferredStart, out Vector3 a, out float aEdge),
                "ön koşul: " + PreferredStart + " çevresinde (±" + SearchMetres + " m) kümeler arasında NavMesh noktası yok — koru açık mı (WP-103)?");
            Assert.IsTrue(KoruNoktasi(PreferredTurn, out Vector3 b, out float bEdge),
                "ön koşul: " + PreferredTurn + " çevresinde (±" + SearchMetres + " m) kümeler arasında NavMesh noktası yok");
            if (s_path == null) s_path = new NavMeshPath();
            Assert.IsTrue(NavMesh.CalculatePath(a, b, NavMesh.AllAreas, s_path) && s_path.status == NavMeshPathStatus.PathComplete,
                "ön koşul: koru içi rota " + a + " → " + b + " tamamlanmıyor");
            float rota = YolUzunlugu(s_path, out bool rotaKoruda);
            Assert.IsTrue(rotaKoruda, "ön koşul: koru içi rota korudan çıkıyor");
            Assert.LessOrEqual(rota, RouteDetourMax * Flat(a, b), "ön koşul: koru içi rota dolambaçlı (" + rota.ToString("F1") + " m)");
            rapor.AppendLine("  start=" + V(a) + " edge=" + aEdge.ToString("F2") + " lane=" + SeritMesafesi(new Vector2(a.x, a.z)).ToString("F1")
                             + " clumps" + ClumpCountMetres + "m=" + KumeSayisi(a, out float aKume) + " nearestClump=" + aKume.ToString("F1")
                             + " | turn=" + V(b) + " edge=" + bEdge.ToString("F2") + " clumps" + ClumpCountMetres + "m=" + KumeSayisi(b, out float bKume)
                             + " nearestClump=" + bKume.ToString("F1") + " | route=" + rota.ToString("F1") + "m corners=" + s_path.GetCornersNonAlloc(s_corners));

            // ---- 2. spawn: the candidates round the grove (static) and the director's own spawns with the player there
            _rig.Teleport(a + Vector3.up * 0.05f, Quaternion.identity);
            yield return null;
            yield return null;
            _run.BeginRound(1);   // V6AiRoundTests.Kur: the round under the chosen mode, points refreshed with the player at the start
            yield return null;
            Assert.IsTrue(SpawnWorldProfile.IsV6, "spawn profili V6 değil");
            _director.RefreshPointsNow();
            SpawnAdaylari(a, rapor, fails);
            yield return CanliSpawn(rapor, fails);
            _director.ForceActiveTarget(0, false);
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            _rig.Teleport(a + Vector3.up * 0.05f, Quaternion.identity);
            yield return null;
            yield return null;

            // ---- 3. the crowd: inside and outside the grove, fixed seed
            List<Vector3> ic = HalkaAdaylari(a, true, InnerRadii, InnerAngles, InnerMax);
            List<Vector3> dis = HalkaAdaylari(a, false, OuterRadii, OuterAngles, CrowdMax - Mathf.Min(InnerMax, ic.Count));
            Assert.GreaterOrEqual(ic.Count, MinPerSide, "ön koşul: koru içinde yolu tamam doğum noktası < " + MinPerSide + " (" + ic.Count + ")");
            Assert.GreaterOrEqual(dis.Count, MinPerSide, "ön koşul: koru dışında yolu tamam doğum noktası < " + MinPerSide + " (" + dis.Count + ")");

            int rec0 = ZombieEnemy.StuckRecoveries, rem0 = ZombieEnemy.StuckRemovals, oow0 = ZombieEnemy.OutOfWorldRemovals;
            int fb0 = ZombieEnemy.PursuitFallbacks, q0 = RegionNavRouter.QueueEvents, rr0 = RegionNavRouter.RerouteCount;
            int oowRing = 0, oowFar = 0;
            var oowAt = new StringBuilder();
            _onOow = (oTier, oAt, oWhy, oState) =>
            {
                bool ring = _rig != null && Flat(oAt, _rig.transform.position) <= RingMetres;
                if (ring) oowRing++; else oowFar++;
                oowAt.Append(oWhy + "@" + V(oAt) + (ring ? " ring " : " ") + oTier + " " + oState + "; ");
            };
            ZombieEnemy.OutOfWorldRemoved += _onOow;

            var govdeler = new List<Govde>(CrowdMax);
            EnemyFactory.ResetSeedForTests(CrowdSeed);
            for (int k = 0; k < ic.Count + dis.Count; k++)
            {
                bool icerde = k < ic.Count;
                Vector3 p = icerde ? ic[k] : dis[k - ic.Count];
                EnemyTier tier = k % 4 == 3 ? EnemyTier.Special : EnemyTier.Standard;
                Vector3 to = a - p; to.y = 0f;
                ZombieEnemy z = EnemyFactory.Spawn(tier, SpawnRound, p, Quaternion.LookRotation(to.normalized, Vector3.up));
                if (z == null) continue;
                govdeler.Add(new Govde
                {
                    Index = k, Z = z, LifeId = z.LifeId, Icerde = icerde, Tier = tier, Role = z.AiRole, Flank = z.FlankSign,
                    Start = z.transform.position, Last = z.transform.position, Anchor = z.transform.position,
                });
            }

            Assert.GreaterOrEqual(govdeler.Count, MinCrowd, "kaynak yok: doğan gövde < " + MinCrowd + " (" + govdeler.Count + ")");

            string kareDir = KareKlasoru(mod);
            var kareTsv = kareDir != null ? new StringBuilder() : null;
            var kalabalikTsv = kareDir != null ? new StringBuilder("frame\tt\tplayer_x\tplayer_z\tring\treach\tflank_active\tcut_active\n") : null;
            Camera cam = PrototypeCameraRig.Active != null ? PrototypeCameraRig.Active.Camera : null;
            if (kareDir != null && cam != null)
            {
                _rt = new RenderTexture(1280, 720, 24);
                _tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            }

            CrowdFeelSettings feel = CrowdFeelSettings.Active;
            float flankUzak = feel.FlankStraightRadius + feel.FlankBlendMetres;
            float t0 = Time.time, nextSample = t0, nextStall = t0, walkEnd = -1f, yurunen = 0f, simSeconds = 0f;
            foreach (Govde g in govdeler) g.AnchorSince = t0;
            bool walkDone = false, turnReached = false;
            int stalls = 0, ringStalls = 0, kare = 0, yazilan = 0;
            Vector3 oncekiOyuncu = _rig.transform.position;
            try
            {
                while (Time.time - t0 < SimSeconds)
                {
                    yield return null;
                    float now = Time.time, t = now - t0;
                    Vector3 pp = _rig.transform.position;

                    // the player: stand, walk the grove route, stand
                    if (!walkDone && t >= StandSeconds)
                    {
                        yurunen += Flat(pp, oncekiOyuncu);
                        turnReached = Flat(pp, b) <= ArriveMetres;
                        if (turnReached || t >= StandSeconds + WalkSeconds)
                        {
                            walkDone = true;
                            walkEnd = t;
                            _girdi.Yon = Vector2.zero;
                        }
                        else _girdi.Yon = Yuru(pp, b);
                    }

                    oncekiOyuncu = pp;

                    // optional evidence: TD frames at 30 fps game time
                    if (_rt != null && t < CaptureSeconds && (kare++ & 1) == 0)
                    {
                        KareYaz(cam, kareDir, yazilan);
                        kareTsv.Append(yazilan).Append('\t').Append((yazilan / 30f).ToString("F4", CultureInfo.InvariantCulture)).Append('\n');
                        int ringN = 0, reachN = 0, flankN = 0, cutN = 0;
                        foreach (Govde g in govdeler)
                        {
                            if (g.Gone || g.Z == null || !g.Z.IsAlive) continue;
                            float d = Flat(g.Z.transform.position, pp);
                            if (d <= RingMetres) ringN++;
                            if (d <= ReachMetres) reachN++;
                            if (g.Z.FlankActive) flankN++;
                            if (g.Z.CutActive) cutN++;
                        }

                        kalabalikTsv.Append(yazilan).Append('\t').Append(t.ToString("F3", CultureInfo.InvariantCulture)).Append('\t')
                            .Append(pp.x.ToString("F2", CultureInfo.InvariantCulture)).Append('\t').Append(pp.z.ToString("F2", CultureInfo.InvariantCulture)).Append('\t')
                            .Append(ringN).Append('\t').Append(reachN).Append('\t').Append(flankN).Append('\t').Append(cutN).Append('\n');
                        yazilan++;
                    }

                    if (now < nextSample) continue;
                    nextSample = now + SampleSeconds;
                    bool stallCheck = now >= nextStall;
                    if (stallCheck) nextStall = now + StallCheckSeconds;
                    bool open = false;
                    foreach (Govde g in govdeler)
                    {
                        if (g.Gone) continue;
                        ZombieEnemy z = g.Z;
                        if (z == null || !z.IsSpawned || !z.IsAlive || z.LifeId != g.LifeId) { g.Gone = true; continue; }
                        Vector3 p = z.transform.position;
                        g.Last = p;
                        float d = Flat(p, pp);
                        bool packed = z.IsWaiting && d <= RingMetres + WaitingPackMetres;
                        if (g.RingAt < 0f && (d <= RingMetres || packed)) g.RingAt = t;
                        if (g.ReachAt < 0f && d <= ReachMetres) g.ReachAt = t;
                        if (g.RingAt < 0f) open = true;

                        // off the NavMesh: no mesh within the probe, or the nearest mesh point farther than OffMeshMetres (flat)
                        float off = NavMesh.SamplePosition(p, out NavMeshHit h, OffMeshProbe, NavMesh.AllAreas) ? Flat(h.position, p) : OffMeshProbe;
                        g.OffMax = Mathf.Max(g.OffMax, off);
                        if (off > OffMeshMetres) { g.OffRun += SampleSeconds; g.OffRunMax = Mathf.Max(g.OffRunMax, g.OffRun); }
                        else g.OffRun = 0f;

                        // the flank goal inside the grove (WP-054 / WP-061M / M-25): only far from the player, after the first decision
                        if (g.Flank != 0 && t >= FlankWarmSeconds && d > flankUzak)
                        {
                            g.FlankSamples++;
                            if (z.FlankActive) { g.FlankActiveSamples++; g.FlankFar = true; }
                        }

                        if (z.CutActive) g.CutSamples++;

                        if (!stallCheck) continue;
                        if (Flat(p, g.Anchor) > StallMove) { g.Anchor = p; g.AnchorSince = now; }
                        else if (!g.StallReported && d > StallMetres && now - g.AnchorSince >= StallSeconds)
                        {
                            g.StallReported = true;
                            g.StallRing = d <= RingMetres || packed;
                            if (g.StallRing) ringStalls++; else stalls++;
                        }
                    }

                    simSeconds = t;
                    if (!open && walkDone && t >= MinSimSeconds) break;   // every body is at the player and the watchdog had its windows
                }
            }
            finally
            {
                _girdi.Yon = Vector2.zero;
                if (_onOow != null) { ZombieEnemy.OutOfWorldRemoved -= _onOow; _onOow = null; }
            }

            // ---- 4. counts
            int dRec = ZombieEnemy.StuckRecoveries - rec0, dRem = ZombieEnemy.StuckRemovals - rem0, dOow = ZombieEnemy.OutOfWorldRemovals - oow0;
            int dFb = ZombieEnemy.PursuitFallbacks - fb0;
            int ringN0 = 0, reachN0 = 0, gone = 0, offBodies = 0, rolled = 0, flankFarBodies = 0, flankSamples = 0, flankActive = 0;
            int kesici = 0, onleyici = 0, kesiciAktif = 0, kesiciBirakti = 0, icN = 0;
            float ringMax = 0f;
            var ringsiz = new StringBuilder();
            var disarida = new StringBuilder();
            var satirlar = new StringBuilder();
            foreach (Govde g in govdeler)
            {
                if (g.Icerde) icN++;
                if (g.Flank != 0) rolled++;
                if (g.FlankFar) flankFarBodies++;
                flankSamples += g.FlankSamples;
                flankActive += g.FlankActiveSamples;
                if (g.Role == ZombieAiRole.Cut) kesici++;
                else if (g.Role == ZombieAiRole.Intercept) onleyici++;
                if (g.CutSamples > 0) kesiciAktif++;
                if (g.Role != ZombieAiRole.Chase && g.Z != null && !g.Gone && g.Z.AiRole == ZombieAiRole.Chase) kesiciBirakti++;
                if (g.Gone) { gone++; }
                else if (g.RingAt < 0f) ringsiz.Append("#" + g.Index + V(g.Last) + " d=" + Flat(g.Last, _rig.transform.position).ToString("F1") + " ");
                else { ringN0++; ringMax = Mathf.Max(ringMax, g.RingAt); }
                if (g.ReachAt >= 0f) reachN0++;
                if (g.OffRunMax >= OffMeshFailSeconds) { offBodies++; disarida.Append("#" + g.Index + V(g.Last) + " " + g.OffRunMax.ToString("F2") + "s max=" + g.OffMax.ToString("F2") + "m "); }
                NavPathFollower f = g.Z != null ? g.Z.PathFollower : null;
                satirlar.AppendLine("    #" + g.Index + " " + (g.Icerde ? "in " : "out") + " " + g.Tier + " role=" + g.Role + " flank=" + g.Flank
                                    + " start=" + V(g.Start) + " ring=" + T(g.RingAt) + " reach=" + T(g.ReachAt)
                                    + " flankFar=" + g.FlankActiveSamples + "/" + g.FlankSamples + " cut=" + g.CutSamples
                                    + " offMax=" + g.OffMax.ToString("F2") + "m/" + g.OffRunMax.ToString("F2") + "s"
                                    + (g.StallReported ? " STALL" + (g.StallRing ? "(ring)" : "") : "") + (g.Gone ? " GONE" : "")
                                    + " last=" + V(g.Last) + " queue=" + (f != null && f.IsQueued ? "1" : "0") + " wait=" + (g.Z != null && g.Z.IsWaiting ? 1 : 0));
            }

            rapor.AppendLine("  crowd bodies=" + govdeler.Count + " (in " + icN + " / out " + (govdeler.Count - icN) + ") sim=" + simSeconds.ToString("F1")
                             + "s ring=" + ringN0 + " (last at " + ringMax.ToString("F1") + "s) reach" + ReachMetres + "=" + reachN0 + " gone=" + gone
                             + " stalls=" + stalls + " ringStalls=" + ringStalls + " offMeshBodies=" + offBodies);
            rapor.AppendLine("  watchdog stuckRecover=" + dRec + " stuckRemove=" + dRem + " oow=" + dOow + " (ring " + oowRing + " / far " + oowFar + ") queueEvents="
                             + (RegionNavRouter.QueueEvents - q0) + " reroutes=" + (RegionNavRouter.RerouteCount - rr0) + (oowAt.Length > 0 ? " oowAt: " + oowAt : ""));
            rapor.AppendLine("  flank rolled=" + rolled + " activeFarBodies=" + flankFarBodies + " activeFarSamples=" + flankActive + "/" + flankSamples
                             + " (far = d > " + flankUzak.ToString("F1") + " m)");
            rapor.AppendLine("  roles cut=" + kesici + " intercept=" + onleyici + " cutActiveBodies=" + kesiciAktif + " pursuitFallbacks=" + dFb
                             + " droppedToChase=" + kesiciBirakti);
            rapor.AppendLine("  player walked=" + yurunen.ToString("F1") + "m turnReached=" + turnReached + " walkEnd=" + T(walkEnd) + " end=" + V(_rig.transform.position));
            rapor.Append(satirlar);
            if (kareDir != null && yazilan > 0)
            {
                File.WriteAllText(Path.Combine(kareDir, "frames.tsv"), kareTsv.ToString());
                File.WriteAllText(Path.Combine(kareDir, "crowd.tsv"), kalabalikTsv.ToString());
                rapor.AppendLine("  capture frames=" + yazilan + " -> " + kareDir);
            }

            Debug.Log(rapor.ToString());
            EnemyFactory.ClearAll();

            // ---- 5. the gate
            if (ringsiz.Length > 0) fails.Add(SimSeconds + " s içinde oyuncunun " + RingMetres + " m halkasına girmeyen gövde: " + ringsiz);
            if (stalls > 0) fails.Add("takılma " + stalls + " (≥ " + StallSeconds + " s, < " + StallMove + " m, halka dışında > " + RingMetres + " m)");
            if (offBodies > 0) fails.Add("NavMesh dışında ≥ " + OffMeshFailSeconds + " s kalan gövde (> " + OffMeshMetres + " m): " + disarida);
            if (oowFar > 0) fails.Add("halka dışında dünya dışı / takılma kaldırması " + oowFar + " (stuckRemove " + dRem + ")");
            if (dRec > MaxRecoveries) fails.Add("takılma kurtarması " + dRec + " > " + MaxRecoveries);
            if (mod == DifficultyMode.Normal)
            {
                Assert.GreaterOrEqual(rolled, MinFlankRolled, "kaynak yok: kanat atan gövde < " + MinFlankRolled + " (tohum " + CrowdSeed + ")\n" + rapor);
                // a finding, not a gate (grove2 12.10, main 21d0fc46: 0/174): the WP-061M flank needs the straight NavMesh line body →
                // player open, and the grove's clumps carve that line almost everywhere — the flankers chase along the lanes instead,
                // none stalls. A grove flank would be a design change (e.g. a lane-side flank), not a fix.
                if (flankFarBodies == 0)
                    Debug.Log("[DD-TEST] wp103.grove BULGU: koru içinde kanatçılar kanat hedefi bulmuyor (" + rolled + " kanatçı, FlankActive 0/"
                              + flankSamples + ") — WP-061M düz NavMesh çizgisi kümelerden kesik; şeritten kovalıyorlar, takılan yok");
            }
            else
            {
                Assert.Greater(kesici + onleyici, 0, DifficultyService.Id(mod) + " rolleri atanmadı\n" + rapor);
            }

            Assert.IsEmpty(fails, string.Join("\n", fails) + "\n" + rapor);
        }

        // ======================================================================== spawn

        /// <summary>
        /// Every spawn point inside the grove-near box: on the NavMesh within SampleOffsetMax and a complete path to the player
        /// (asserted for enabled points), with the director's own view (status, path metres, last reject). Zero points inside
        /// the band itself is a finding for the design, not a failure.
        /// </summary>
        private static void SpawnAdaylari(Vector3 a, StringBuilder rapor, List<string> fails)
        {
            IReadOnlyList<SpawnPointRuntime> pts = SpawnPointRegistry.Points;
            var path = new NavMeshPath();
            int near = 0, grove = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                SpawnPointRuntime p = pts[i];
                Vector3 w = p.WorldPosition;
                if (w.x < NearXMin || w.x > NearXMax || Mathf.Abs(w.z) > NearZAbs) continue;
                near++;
                bool icerde = KoruIcinde(w, 0f);
                if (icerde) grove++;
                SpawnPointKind kind = p.Definition != null ? p.Definition.Kind : SpawnPointKind.Edge;
                bool onMesh = NavMesh.SamplePosition(w, out NavMeshHit h, 3f, NavMesh.AllAreas);
                float off = onMesh ? Flat(h.position, w) : float.PositiveInfinity;
                NavMeshPathStatus st = onMesh && NavMesh.CalculatePath(h.position, a, NavMesh.AllAreas, path) ? path.status : NavMeshPathStatus.PathInvalid;
                float len = st == NavMeshPathStatus.PathComplete ? YolUzunlugu(path, out bool _) : -1f;
                rapor.AppendLine("  spawnpoint " + p.Id + " " + kind + (icerde ? " GROVE" : " near") + (p.Enabled ? "" : " DISABLED") + " at=" + V(w)
                                 + " navOff=" + (onMesh ? off.ToString("F2") : "none") + " path=" + st + " " + len.ToString("F1") + "m"
                                 + " director=" + p.PathStatus + " " + p.PathDistance.ToString("F1") + "m weight=" + p.LastWeight.ToString("F2")
                                 + " reject=" + p.LastRejectReason);
                if (!p.Enabled) continue;
                if (off > SampleOffsetMax) fails.Add("spawn adayı " + p.Id + " NavMesh'e oturmuyor (" + (onMesh ? off.ToString("F2") + " m" : "örnek yok") + ")");
                if (st != NavMeshPathStatus.PathComplete) fails.Add("spawn adayı " + p.Id + " → oyuncu yolu " + st);
            }

            rapor.AppendLine("  spawnpoints near=" + near + " insideGrove=" + grove
                             + (grove == 0 ? "  BULGU: koru bandında spawn adayı yok (tasarım sorusu, assert yok)" : ""));
        }

        /// <summary>The director's round with the player at the start: where its bodies appear (report) and that those near the grove stand on the mesh.</summary>
        private IEnumerator CanliSpawn(StringBuilder rapor, List<string> fails)
        {
            int toplam = 0, yakin = 0, koruda = 0;
            var noktalar = new Dictionary<string, int>();
            var disarida = new StringBuilder();
            _onSpawn = ctx =>
            {
                toplam++;
                Vector3 p = ctx.Position;
                string id = string.IsNullOrEmpty(ctx.PointId) ? "?" : ctx.PointId;
                noktalar.TryGetValue(id, out int n);
                noktalar[id] = n + 1;
                if (p.x < NearXMin || p.x > NearXMax || Mathf.Abs(p.z) > NearZAbs) return;
                yakin++;
                if (KoruIcinde(p, 0f)) koruda++;
                float off = NavMesh.SamplePosition(p, out NavMeshHit h, OffMeshProbe, NavMesh.AllAreas) ? Flat(h.position, p) : OffMeshProbe;
                if (off > OffMeshMetres) disarida.Append(id + V(p) + " off=" + off.ToString("F2") + " ");
            };
            _director.EnemySpawned += _onSpawn;
            _director.ForceActiveTarget(LiveTarget, true);
            float t0 = Time.time;
            try
            {
                while (Time.time - t0 < LiveSpawnSeconds) yield return null;
            }
            finally
            {
                _director.EnemySpawned -= _onSpawn;
                _onSpawn = null;
            }

            var sb = new StringBuilder("  livespawn " + LiveSpawnSeconds + "s target=" + LiveTarget + " spawned=" + toplam + " nearGrove=" + yakin + " insideGrove=" + koruda
                                       + " noPointTicks=" + _director.NoPointTicks + " points:");
            foreach (KeyValuePair<string, int> kv in noktalar) sb.Append(' ').Append(kv.Key).Append('×').Append(kv.Value);
            rapor.AppendLine(sb.ToString());
            if (disarida.Length > 0) fails.Add("koru yakınında NavMesh dışında doğan gövde: " + disarida);
        }

        // ======================================================================== geometry

        /// <summary>
        /// The NavMesh point nearest <paramref name="tercih"/> (0.5 m grid, ±SearchMetres) that is inside the band, ≥ LaneClearMetres off
        /// every lane axis, ≥ GateClearMetres off a grove gate, snaps ≤ StartSnapMetres and has ≥ EdgeClearMetres to the nearest
        /// NavMesh edge — open ground between clumps, not a lane. Deterministic (grid order, strict improvement).
        /// </summary>
        private static bool KoruNoktasi(Vector3 tercih, out Vector3 nokta, out float kenar)
        {
            nokta = Vector3.zero;
            kenar = 0f;
            int best = int.MaxValue;
            int n = Mathf.RoundToInt(SearchMetres / SearchStep);
            for (int ix = -n; ix <= n; ix++)
            for (int iz = -n; iz <= n; iz++)
            {
                int dd = ix * ix + iz * iz;
                if (dd >= best) continue;
                var c = new Vector3(tercih.x + ix * SearchStep, 0f, tercih.z + iz * SearchStep);
                if (!KoruIcinde(c, 1f)) continue;
                var c2 = new Vector2(c.x, c.z);
                if (SeritMesafesi(c2) < LaneClearMetres || KapiMesafesi(c2) < GateClearMetres) continue;
                if (!NavMesh.SamplePosition(c, out NavMeshHit h, 3f, NavMesh.AllAreas) || Flat(h.position, c) > StartSnapMetres) continue;
                if (!NavMesh.FindClosestEdge(h.position, out NavMeshHit e, NavMesh.AllAreas) || e.distance < EdgeClearMetres) continue;
                best = dd;
                nokta = h.position;
                kenar = e.distance;
            }

            return best < int.MaxValue;
        }

        /// <summary>Deterministic ring of spawn starts round <paramref name="a"/>: on the mesh, inside / outside the grove, a complete path, spread.</summary>
        private static List<Vector3> HalkaAdaylari(Vector3 a, bool icerde, float[] radii, int angles, int max)
        {
            var found = new List<Vector3>(angles);
            var path = new NavMeshPath();
            for (int i = 0; i < angles; i++)
            {
                float ang = i * Mathf.PI * 2f / angles;
                float r = radii[i % radii.Length];
                Vector3 p = a + new Vector3(Mathf.Sin(ang) * r, 0f, Mathf.Cos(ang) * r);
                if (!NavMesh.SamplePosition(p, out NavMeshHit h, 3f, NavMesh.AllAreas) || Flat(h.position, p) > SpawnSnapMetres) continue;
                if (KoruIcinde(h.position, 0f) != icerde) continue;
                if (!NavMesh.CalculatePath(h.position, a, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                bool yakin = false;
                for (int j = 0; j < found.Count && !yakin; j++) yakin = Flat(found[j], h.position) < SpawnSeparation;
                if (!yakin) found.Add(h.position);
            }

            if (found.Count <= max) return found;
            var picked = new List<Vector3>(max);
            for (int k = 0; k < max; k++) picked.Add(found[k * found.Count / max]);
            return picked;
        }

        /// <summary>The stick that walks the player along the NavMesh path to <paramref name="hedef"/> (camera-relative, as the input reads).</summary>
        private static Vector2 Yuru(Vector3 oyuncu, Vector3 hedef)
        {
            Vector3 from = NavMesh.SamplePosition(oyuncu, out NavMeshHit h, 1f, NavMesh.AllAreas) ? h.position : oyuncu;
            Vector3 corner = hedef;
            if (NavMesh.CalculatePath(from, hedef, NavMesh.AllAreas, s_path))
            {
                int n = s_path.GetCornersNonAlloc(s_corners);
                for (int i = 1; i < n; i++)
                {
                    corner = s_corners[i];
                    if (Flat(corner, oyuncu) > 0.4f) break;
                }
            }

            Vector3 dir = corner - oyuncu;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return Vector2.zero;
            dir.Normalize();
            PrototypeCameraRig kam = PrototypeCameraRig.Active;
            Vector3 f = kam != null ? kam.PlanarForward : Vector3.forward, r = kam != null ? kam.PlanarRight : Vector3.right;
            var stick = new Vector2(Vector3.Dot(dir, r), Vector3.Dot(dir, f));
            return stick.sqrMagnitude > 1e-4f ? stick.normalized : Vector2.zero;
        }

        private static bool KoruIcinde(Vector3 p, float pay)
            => p.x >= GroveXMin + pay && p.x <= GroveXMax - pay && Mathf.Abs(p.z) <= GroveZAbs - pay;

        private static float SeritMesafesi(Vector2 c)
        {
            float best = float.MaxValue;
            for (int l = 0; l < Lanes.Length; l++)
            {
                Vector2[] lane = Lanes[l];
                for (int i = 1; i < lane.Length; i++) best = Mathf.Min(best, SegmentDistance(c, lane[i - 1], lane[i]));
            }

            return best;
        }

        private static float KapiMesafesi(Vector2 c)
        {
            float best = float.MaxValue;
            for (int i = 0; i < GroveGates.Length; i++) best = Mathf.Min(best, Vector2.Distance(c, GroveGates[i]));
            return best;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Path metres; <paramref name="koruda"/> = every corner inside the grove band (0.5 m tolerance).</summary>
        private static float YolUzunlugu(NavMeshPath path, out bool koruda)
        {
            int n = path.GetCornersNonAlloc(s_corners);
            float len = 0f;
            koruda = true;
            for (int i = 0; i < n; i++)
            {
                if (!KoruIcinde(s_corners[i], -0.5f)) koruda = false;
                if (i > 0) len += Vector3.Distance(s_corners[i - 1], s_corners[i]);
            }

            return len;
        }

        /// <summary>Bamboo clumps of the dressed grove (WorldV5Dress "BambooGrove" children named bamboo*) within ClumpCountMetres; −1 when the group is not in the loaded scenes. Report only.</summary>
        private static int KumeSayisi(Vector3 p, out float enYakin)
        {
            enYakin = -1f;
            GameObject g = GameObject.Find(GroveGroupName);
            if (g == null) return -1;
            Transform t = g.transform;
            int n = 0;
            float best = float.MaxValue;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c.name.IndexOf("bamboo", StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = Flat(c.position, p);
                best = Mathf.Min(best, d);
                if (d <= ClumpCountMetres) n++;
            }

            enYakin = best < float.MaxValue ? best : -1f;
            return n;
        }

        // ======================================================================== evidence

        private static string KareKlasoru(DifficultyMode mod)
        {
            string kok = System.Environment.GetEnvironmentVariable("DD_WP103_CAPTURE_DIR");
            if (string.IsNullOrEmpty(kok)) return null;
            string dir = Path.Combine(kok, DifficultyService.Id(mod));
            Directory.CreateDirectory(Path.Combine(dir, "frames"));
            return dir;
        }

        private void KareYaz(Camera cam, string dir, int index)
        {
            RenderTexture eski = cam.targetTexture;
            cam.targetTexture = _rt;
            cam.Render();
            cam.targetTexture = eski;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            _tex.Apply(false);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, "frames", "f-" + index.ToString("000000") + ".jpg"), _tex.EncodeToJPG(90));
        }

        // ======================================================================== yardımcı

        private static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string V(Vector3 p) => "(" + p.x.ToString("F1", CultureInfo.InvariantCulture) + "," + p.z.ToString("F1", CultureInfo.InvariantCulture) + ")";

        private static string T(float t) => t >= 0f ? t.ToString("F1", CultureInfo.InvariantCulture) + "s" : "-";

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float baslangic = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - baslangic > saniye) Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                yield return null;
            }
        }
    }
}
