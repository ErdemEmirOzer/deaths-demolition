#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Art;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Qa
{
    /// <summary>
    /// WP-026 · DEC-077 readability gate on the real spawner mix (development Player / Editor).
    ///
    /// <c>-ddQaReadability &lt;outDir&gt;</c> arms it; <c>-ddQaReadabilityRounds 1,4,8</c>,
    /// <c>-ddQaCrowd 40</c>, <c>-ddQaSkins 0,1,2</c>. For each round the run is moved to that
    /// round through <see cref="RunDirector.BeginRound"/> (the F1 "Skip to round" call), the spawner
    /// is asked for a full crowd (<see cref="EnemySpawnDirector.ForceActiveTarget"/>) so the
    /// version/armour/garment mix is the shipped one, and after the crowd settles the driver
    /// measures from the gameplay camera:
    ///   • capsule occlusion: 5 rays per on-screen zombie (feet/mid/head/±shoulder, ≤ 35 m) against Environment|CameraOccluder;
    ///     a zombie with more than <see cref="ReadabilityThresholds.OcclusionRatioMax"/> of its points blocked counts as occluded;
    ///   • contrast (WP-016 CharacterTests.Okunabilirlik method): difference masks from captures with/without the
    ///     player and the zombie renderers; |luma(figure) − luma(ring 4–10 px)| / (sum) for the zombie crowd and for the
    ///     player in each requested skin (figure and body band), plus player body ↔ zombie crowd; threshold
    ///     <see cref="ReadabilityThresholds.CapsuleContrastMin"/> (DEC-077);
    ///   • mix census: tier, armour pieces per slot, garment ids (cw2 / T7 / H0 shares) of the crowd on screen.
    /// The armour form test (≥ 30 % + band 0.08/0.12) stays with the DD.Art PlayMode test
    /// <c>ArmorTests.Okunabilirlik_DEC077_…</c>; regression.sh runs it and the aggregate report cites it.
    /// </summary>
    public static class QaReadabilityGate
    {
        public const string OutFlag = "-ddQaReadability";
        public const string RoundsFlag = "-ddQaReadabilityRounds";
        public const string CrowdFlag = "-ddQaCrowd";
        public const string SkinsFlag = "-ddQaSkins";
        public const string ProfileFlag = "-ddProfilePath";
        public static readonly int[] DefaultRounds = { 1, 4, 8 };
        public static readonly int[] DefaultSkins = { 0, 1, 2 };
        public const int DefaultCrowd = 40;
        public const int CaptureWidth = 960;
        public const int CaptureHeight = 540;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            if (string.IsNullOrEmpty(QaArgs.Arg(OutFlag))) return;
            string profilePath = QaArgs.Arg(ProfileFlag);
            if (!string.IsNullOrEmpty(profilePath))
            {
                ProfileStore.SetPathOverride(profilePath);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string outDir = QaArgs.Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir)) return;
            Arm(outDir, QaArgs.ArgIntList(RoundsFlag, DefaultRounds, 1, 50), QaArgs.ArgInt(CrowdFlag, DefaultCrowd, 1, 80),
                QaArgs.ArgIntList(SkinsFlag, DefaultSkins, 0, 2), true);
        }

        public static Driver Arm(string outDir, int[] rounds, int crowd, int[] skins, bool bootFlow)
        {
            var carrier = new GameObject("QaReadabilityGate");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            Driver d = carrier.AddComponent<Driver>();
            d.Configure(outDir, rounds, crowd, skins, bootFlow);
            Debug.Log("[QaReadabilityGate] armed rounds=" + string.Join(",", rounds) + " crowd=" + crowd + " skins=" + string.Join(",", skins) + " out=" + outDir);
            return d;
        }

        [Serializable]
        public sealed class RoundResult
        {
            public int round;
            public int crowdRequested;
            public int alive;
            public int nearPlayer;             // within 14 m when captured
            public int onScreen;
            public int occludedOverHalf;
            public float meanVisiblePoints;
            public int raysCast, raysBlocked, raysBlockedEnvironment, raysBlockedOccluder;
            public float zombieEnvDeltaVMean, zombieEnvDeltaVMin;
            public float zombieMeanV;
            public float[] playerEnvDeltaV;       // per skin index: figure/ring contrast (WP-016 method)
            public float[] playerBodyContrast;    // per skin index: body band (rows 20–60 %) / ring contrast
            public float[] playerZombieDeltaV;    // per skin index: body luma vs zombie crowd luma contrast
            public int playerPixels, zombiePixels;
            public float zombieShadowShare, playerShadowShare;   // share of the raw difference mask dropped as cast shadow / AO (P-06)
            public float[] playerFigureLuma, playerRingLuma;   // per skin: raw lumas behind the contrast (WP-044)
            public string playerLod;                           // LOD level of the player figure at capture (WP-044)
            public string shadowsNote;                         // what the masks/rings include (WP-044)
            [NonSerialized] public List<Renderer> zombieRenderers;
            [NonSerialized] public List<Renderer>[] tierRenderers;   // index = (int)EnemyTier, on-screen bodies only
            // WP-036 / DEC-077 in-engine tier reading (index = (int)EnemyTier): figure/environment contrast, tier vs Standard luma
            // contrast and raw ΔV, pixels; -1 = tier not on screen
            public float[] tierEnvContrast, tierVsStandardContrast, tierVsStandardDeltaV, tierArmouredShare;
            public int[] tierPixels, tierOnScreen;
            public int standard, special, elite, ultra;
            public int helmet, body, leg, bare;
            public int fadedGarments, garments, minoCape, bald;
            public bool pass;
            public string note;
        }

        [Serializable]
        public sealed class Summary
        {
            public string schema = "dd-qa-readability-v1";
            public string decision = ReadabilityThresholds.Decision;
            public float occlusionRatioMax = ReadabilityThresholds.OcclusionRatioMax;
            public float capsuleContrastMin = ReadabilityThresholds.CapsuleContrastMin;
            public int[] skins;
            public bool pass;
            public string note;
            public List<RoundResult> rounds = new List<RoundResult>();
        }

        public sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeoutSeconds = 120f;
            private const float CrowdTimeoutSeconds = 60f;
            private const float SettleSeconds = 1.0f;
            private const float ApproachTimeoutSeconds = 45f;
            private const float NearRadius = 14f;
            private const int NearTarget = 12;
            private const int PointsPerCapsule = 5;

            private string _outDir;
            private int[] _rounds;
            private int _crowd;
            private int[] _skins;
            private bool _bootFlow;
            private string _fail;
            private readonly Summary _summary = new Summary();
            private bool _finished;

            public bool Finished => _finished;
            public Summary Result => _summary;

            public void Configure(string outDir, int[] rounds, int crowd, int[] skins, bool bootFlow)
            {
                _outDir = outDir; _rounds = rounds; _crowd = crowd; _skins = skins; _bootFlow = bootFlow;
                _summary.skins = skins;
            }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_outDir);
                if (_bootFlow)
                {
                    Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
                    yield return null;
                    yield return WaitForScene(PrototypeScenes.MainMenu, SceneTimeoutSeconds);
                    if (_fail != null) { Finish(); yield break; }
                    yield return null;
                    PrototypeUiFlow.GoToSelection();
                    yield return WaitForScene(PrototypeScenes.Selection, SceneTimeoutSeconds);
                    if (_fail != null) { Finish(); yield break; }
                    PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                    PrototypeUiFlow.StartRun();
                    yield return WaitForScene(PrototypeScenes.Run, SceneTimeoutSeconds);
                    if (_fail != null) { Finish(); yield break; }
                }

                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null &&
                                             PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting", SceneTimeoutSeconds);
                if (_fail != null) { Finish(); yield break; }

                PlayerRig rig = PlayerRegistry.Primary;
                rig.Health.DiagnosisInvulnerable = true;
                RunDirector run = RunDirector.Instance;
                EnemySpawnDirector dir = EnemySpawnDirector.Instance;

                for (int i = 0; i < _rounds.Length; i++)
                {
                    int round = _rounds[i];
                    if (run.Round != round)
                    {
                        EnemyFactory.ClearAll();
                        run.BeginRound(round);         // the F1 "Skip to round" path: pool, tier policy and spawner re-plan
                        yield return null;
                    }

                    dir.ForceActiveTarget(_crowd, true);
                    float t0 = Time.realtimeSinceStartup;
                    while (AliveCount() < _crowd && Time.realtimeSinceStartup - t0 < CrowdTimeoutSeconds)
                    {
                        yield return null;
                    }

                    // let the crowd close in on the (invulnerable) player: readability is judged on the fight, not on the spawn ring
                    t0 = Time.realtimeSinceStartup;
                    while (NearCount(rig.transform.position, NearRadius) < Mathf.Min(NearTarget, AliveCount()) && Time.realtimeSinceStartup - t0 < ApproachTimeoutSeconds)
                    {
                        yield return null;
                    }

                    // S-03 (WP-054 / DEC-081): non-Standard tiers spawn off-camera and become visible as they close in — same rule
                    // as the bot's tier frames: wait until a Special/Elite/Ultra body is within 10 m, or 25 s
                    t0 = Time.realtimeSinceStartup;
                    while (TierAlive() && !TierNear(rig.transform.position, 10f) && Time.realtimeSinceStartup - t0 < 25f)
                    {
                        yield return null;
                    }

                    yield return new WaitForSeconds(SettleSeconds);
                    RoundResult rr = Measure(round, rig);
                    rr.nearPlayer = NearCount(rig.transform.position, NearRadius);
                    yield return MeasureSkins(rr, rig);
                    _summary.rounds.Add(rr);
                    WriteRound(rr);
                }

                dir.ForceActiveTarget(0, false);
                Finish();
            }

            private static bool TierAlive()
            {
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++) if (live[i] != null && live[i].IsAlive && live[i].Tier != EnemyTier.Standard) return true;
                return false;
            }

            private static bool TierNear(Vector3 from, float radius)
            {
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || !z.IsAlive || z.Tier == EnemyTier.Standard) continue;
                    Vector3 d = z.transform.position - from; d.y = 0f;
                    if (d.sqrMagnitude <= radius * radius) return true;
                }
                return false;
            }

            private static int NearCount(Vector3 from, float radius)
            {
                int n = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || !z.IsAlive) continue;
                    Vector3 d = z.transform.position - from; d.y = 0f;
                    if (d.sqrMagnitude <= radius * radius) n++;
                }

                return n;
            }

            private static int AliveCount()
            {
                int n = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++) if (live[i] != null && live[i].IsAlive) n++;
                return n;
            }

            // ------------------------------------------------------------------ measurement (WP-016 DEC-077 method)
            //
            // Difference masks: the frame with and without the player / the zombie renderers give exact
            // figure masks; contrast = |luma(figure) − luma(ring 4–10 px around it)| / (sum) — the same
            // reading CharacterTests.Okunabilirlik (WP-016) asserts at ReadabilityThresholds.CapsuleContrastMin.
            // Time is frozen for the three captures so the masks line up.

            private const int RingInner = 4, RingOuter = 10;
            private const float OcclusionMaxDistance = 35f;

            private RoundResult Measure(int round, PlayerRig rig)
            {
                var r = new RoundResult { round = round, crowdRequested = _crowd };
                Camera cam = ResolveCamera();
                if (cam == null)
                {
                    r.note = "no camera";
                    return r;
                }

                Vector3 camPos = cam.transform.position;
                int mask = PrototypeLayers.AimBlockMask;
                var points = new Vector3[PointsPerCapsule];
                double visibleSum = 0d;
                var zombieRenderers = new List<Renderer>(256);
                RaycastHit hit;

                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int e = 0; e < live.Count; e++)
                {
                    ZombieEnemy z = live[e];
                    if (z == null || !z.IsAlive) continue;
                    r.alive++;
                    Census(z, r);
                    zombieRenderers.AddRange(z.GetComponentsInChildren<Renderer>(true));

                    float radius = Mathf.Max(0.05f, z.BodyRadius);
                    float height = ZombieBodyBuilder.StandHeight;
                    CharacterController cc = z.GetComponent<CharacterController>();
                    if (cc != null && cc.height > 0.1f) height = cc.height;
                    CapsulePoints(z.transform.position, z.transform.right, radius, height, points);

                    // occlusion is judged for the zombies the camera can see (in the frustum, within range):
                    // a body behind the camera or 80 m away is not a readability failure
                    Vector3 vp = cam.WorldToViewportPoint(points[1]);
                    bool inFrustum = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f && vp.z <= OcclusionMaxDistance;
                    if (!inFrustum) continue;
                    r.onScreen++;
                    int ti = (int)z.Tier;
                    if (r.tierRenderers == null) r.tierRenderers = new List<Renderer>[4];
                    if (ti >= 0 && ti < 4)
                    {
                        if (r.tierRenderers[ti] == null) r.tierRenderers[ti] = new List<Renderer>(64);
                        r.tierRenderers[ti].AddRange(z.GetComponentsInChildren<Renderer>(true));
                        if (r.tierOnScreen == null) r.tierOnScreen = new int[4];
                        r.tierOnScreen[ti]++;
                        if (r.tierArmouredShare == null) r.tierArmouredShare = new float[4];
                        if (z.Armor != null && (z.Armor.IsEquipped(ArmorSlot.Helmet) || z.Armor.IsEquipped(ArmorSlot.Body) || z.Armor.IsEquipped(ArmorSlot.Leg))) r.tierArmouredShare[ti]++;
                    }

                    int visible = 0;
                    for (int p = 0; p < points.Length; p++)
                    {
                        Vector3 d = points[p] - camPos;
                        float len = d.magnitude;
                        r.raysCast++;
                        if (len > 0.001f && Physics.Raycast(camPos, d / len, out hit, len, mask, QueryTriggerInteraction.Ignore))
                        {
                            r.raysBlocked++;
                            int layer = hit.collider.gameObject.layer;
                            if (layer == PrototypeLayers.Environment) r.raysBlockedEnvironment++;
                            else if (layer == PrototypeLayers.CameraOccluder) r.raysBlockedOccluder++;
                        }
                        else
                        {
                            visible++;
                        }
                    }

                    visibleSum += visible;
                    if (1f - (float)visible / points.Length > ReadabilityThresholds.OcclusionRatioMax) r.occludedOverHalf++;
                }

                r.meanVisiblePoints = r.onScreen > 0 ? (float)(visibleSum / r.onScreen) : 0f;
                r.zombieRenderers = zombieRenderers;
                return r;
            }

            private IEnumerator MeasureSkins(RoundResult r, PlayerRig rig)
            {
                r.playerEnvDeltaV = new float[_skins.Length];
                r.playerZombieDeltaV = new float[_skins.Length];
                r.playerBodyContrast = new float[_skins.Length];
                r.playerFigureLuma = new float[_skins.Length];
                r.playerRingLuma = new float[_skins.Length];
                Camera cam = ResolveCamera();
                if (cam == null) { Evaluate(r); yield break; }
                PlayerSkin original = rig.Skin;
                // WP-059C: the figure is the body — the weapon in hand (level in front of the chest since WP-059B) is not player pixels.
                WeaponController weaponsInHand = WeaponRegistry.ActiveController;
                Renderer[] playerRenderers = Array.FindAll(rig.GetComponentsInChildren<Renderer>(true), rr => weaponsInHand == null || !weaponsInHand.IsWeaponRenderer(rr));
                List<Renderer> zombieRenderers = r.zombieRenderers ?? new List<Renderer>();
                float timeScale = Time.timeScale;
                Time.timeScale = 0f;
                List<Renderer> hiddenPlayer = null, hiddenZombies = null, hiddenTier = null;
                try
                {
                    yield return null;
                    Color32[] full = Pixels(cam, Path.Combine(_outDir, "readability-r" + r.round.ToString("D2") + "-full.png"));
                    hiddenPlayer = Hide(playerRenderers);
                    yield return null;
                    Color32[] noPlayer = Pixels(cam, null);
                    Show(hiddenPlayer);
                    hiddenZombies = Hide(zombieRenderers);
                    yield return null;
                    Color32[] noZombies = Pixels(cam, Path.Combine(_outDir, "readability-r" + r.round.ToString("D2") + "-nozombies.png"));
                    Show(hiddenZombies);
                    yield return null;
                    r.playerLod = LodState(rig);
                    r.shadowsNote = "masks = renderer on/off difference (Diff > 24) → cast shadows of the hidden body are inside the mask; ring = 4–10 px outside the mask (shadow pixels excluded from the ring)";

                    // WP-038G: the DEC-077 figure mask is shared with DD.Art (ReadabilityThresholds.FigureDiff — difference above
                    // FigureDiffThreshold that is not a uniform darkening; cast shadow / SSAO stays out of the body). The share the
                    // plain difference would have added is reported so the two readings stay comparable with WP-016/WP-026 numbers.
                    bool[] playerMask = ReadabilityThresholds.FigureDiff(full, noPlayer);
                    bool[] zombieMask = ReadabilityThresholds.FigureDiff(full, noZombies);
                    r.zombieShadowShare = ShadowShare(Diff(full, noZombies), zombieMask);
                    r.playerShadowShare = ShadowShare(Diff(full, noPlayer), playerMask);
                    r.playerPixels = Count(playerMask);
                    r.zombiePixels = Count(zombieMask);
                    bool[] zombieRing = Ring(zombieMask, playerMask, RingInner, RingOuter);
                    float zombieLuma = MeanLuma(full, zombieMask);
                    float zombieRingLuma = MeanLuma(full, zombieRing);
                    r.zombieEnvDeltaVMean = Contrast(zombieLuma, zombieRingLuma);            // official crowd/environment reading
                    r.zombieEnvDeltaVMin = Mathf.Abs(MeanValue(full, zombieMask) - MeanValue(full, zombieRing));   // raw ΔV (report)
                    r.zombieMeanV = zombieLuma;

                    // WP-036 tier reading: hide one tier at a time → its mask; compare with the environment ring and with the
                    // Standard bodies (the remainder of the crowd mask once every non-Standard tier is removed)
                    r.tierEnvContrast = new float[] { -1f, -1f, -1f, -1f };
                    r.tierVsStandardContrast = new float[] { -1f, -1f, -1f, -1f };
                    r.tierVsStandardDeltaV = new float[] { -1f, -1f, -1f, -1f };
                    r.tierPixels = new int[4];
                    if (r.tierRenderers != null)
                    {
                        bool[] standardMask = (bool[])zombieMask.Clone();
                        var tierMasks = new bool[4][];
                        for (int t = 1; t < 4; t++)
                        {
                            if (r.tierRenderers[t] == null || r.tierRenderers[t].Count == 0) continue;
                            // back-to-back pair per tier (shader time keeps running at timeScale 0 — foliage sway would otherwise
                            // leak into the mask), then clipped to the crowd mask: a tier body is by definition part of the crowd
                            Color32[] fullT = Pixels(cam, null);
                            hiddenTier = Hide(r.tierRenderers[t]);
                            yield return null;
                            Color32[] noTier = Pixels(cam, null);
                            Show(hiddenTier);
                            yield return null;
                            tierMasks[t] = ReadabilityThresholds.FigureDiff(fullT, noTier);
                            for (int i = 0; i < tierMasks[t].Length; i++) if (!zombieMask[i]) tierMasks[t][i] = false;
                            for (int i = 0; i < standardMask.Length; i++) if (tierMasks[t][i]) standardMask[i] = false;
                        }

                        float standardLuma = MeanLuma(full, standardMask);
                        float standardValue = MeanValue(full, standardMask);
                        int standardPixels = Count(standardMask);
                        for (int t = 1; t < 4; t++)
                        {
                            if (tierMasks[t] == null) continue;
                            r.tierPixels[t] = Count(tierMasks[t]);
                            if (r.tierPixels[t] == 0) continue;
                            bool[] tierRing = Ring(tierMasks[t], zombieMask, RingInner, RingOuter);   // environment only: other bodies excluded
                            for (int i = 0; i < tierRing.Length; i++) if (playerMask[i]) tierRing[i] = false;
                            float tierLuma = MeanLuma(full, tierMasks[t]);
                            r.tierEnvContrast[t] = Contrast(tierLuma, MeanLuma(full, tierRing));
                            r.tierVsStandardContrast[t] = standardPixels > 0 ? Contrast(tierLuma, standardLuma) : -1f;
                            r.tierVsStandardDeltaV[t] = standardPixels > 0 ? Mathf.Abs(MeanValue(full, tierMasks[t]) - standardValue) : -1f;
                        }
                        r.tierPixels[0] = standardPixels;
                        if (r.tierArmouredShare != null && r.tierOnScreen != null)
                            for (int t = 0; t < 4; t++) r.tierArmouredShare[t] = r.tierOnScreen[t] > 0 ? r.tierArmouredShare[t] / r.tierOnScreen[t] : -1f;
                    }

                    bool[] bodyMask = Band(playerMask, 0.20f, 0.60f, 0.20f, 0.80f);
                    bool[] playerRing = Ring(playerMask, zombieMask, RingInner, RingOuter);
                    for (int s = 0; s < _skins.Length; s++)
                    {
                        rig.ApplySkin((PlayerSkin)_skins[s]);
                        yield return null;
                        Color32[] frame = Pixels(cam, Path.Combine(_outDir, "readability-r" + r.round.ToString("D2") + "-skin" + _skins[s] + ".png"));
                        float figure = MeanLuma(frame, playerMask);
                        float body = MeanLuma(frame, bodyMask);
                        float ring = MeanLuma(frame, playerRing);
                        r.playerFigureLuma[s] = figure; r.playerRingLuma[s] = ring;
                        r.playerEnvDeltaV[s] = r.playerPixels > 0 ? Contrast(figure, ring) : -1f;
                        r.playerBodyContrast[s] = r.playerPixels > 0 ? Contrast(body, ring) : -1f;
                        r.playerZombieDeltaV[s] = r.playerPixels > 0 && r.zombiePixels > 0 ? Contrast(body, zombieLuma) : -1f;
                    }
                }
                finally
                {
                    Show(hiddenPlayer);
                    Show(hiddenZombies);
                    Show(hiddenTier);
                    rig.ApplySkin(original);
                    Time.timeScale = timeScale;
                }

                r.zombieRenderers = null;
                Evaluate(r);
            }

            private void Evaluate(RoundResult r)
            {
                bool occl = r.occludedOverHalf == 0;
                bool zEnv = r.zombiePixels == 0 || r.zombieEnvDeltaVMean >= ReadabilityThresholds.CapsuleContrastMin;
                bool pEnv = true, pZ = true;
                for (int s = 0; r.playerEnvDeltaV != null && s < r.playerEnvDeltaV.Length; s++)
                {
                    pEnv &= r.playerEnvDeltaV[s] >= ReadabilityThresholds.CapsuleContrastMin;
                    pZ &= r.playerZombieDeltaV[s] >= ReadabilityThresholds.CapsuleContrastMin;
                }

                r.pass = occl && zEnv && pEnv && pZ && r.alive > 0 && r.zombiePixels > 0;
                r.note = "alive " + r.alive + " near " + r.nearPlayer + " onScreen " + r.onScreen + " occluded>50% " + r.occludedOverHalf +
                         " px player/zombie " + r.playerPixels + "/" + r.zombiePixels +
                         " zombieCrowd contrast " + QaArgs.F(r.zombieEnvDeltaVMean, 3) + " (ΔV " + QaArgs.F(r.zombieEnvDeltaVMin, 3) + ", shadow/AO dropped " + QaArgs.F(r.zombieShadowShare * 100f, 0) + " %)" +
                         " player figure " + Join(r.playerEnvDeltaV) + " body " + Join(r.playerBodyContrast) + " player↔zombie " + Join(r.playerZombieDeltaV) +
                         " tiers " + r.standard + "/" + r.special + "/" + r.elite + "/" + r.ultra +
                         " armour H/B/L/bare " + r.helmet + "/" + r.body + "/" + r.leg + "/" + r.bare +
                         " cw2 " + r.fadedGarments + "/" + r.garments + " T7 " + r.minoCape + " H0 " + r.bald +
                         (r.tierEnvContrast != null ? " tierEnv S/E/U " + QaArgs.F(r.tierEnvContrast[1], 3) + "/" + QaArgs.F(r.tierEnvContrast[2], 3) + "/" + QaArgs.F(r.tierEnvContrast[3], 3) +
                         " tierVsStd " + QaArgs.F(r.tierVsStandardContrast[1], 3) + "/" + QaArgs.F(r.tierVsStandardContrast[2], 3) + "/" + QaArgs.F(r.tierVsStandardContrast[3], 3) +
                         " (ΔV " + QaArgs.F(r.tierVsStandardDeltaV[1], 3) + "/" + QaArgs.F(r.tierVsStandardDeltaV[2], 3) + "/" + QaArgs.F(r.tierVsStandardDeltaV[3], 3) + ")" : "");
            }

            // ---- pixel helpers (CharacterTests.Okunabilirlik parity: Diff > 24, ring 4–10 px, luma 0.2126/0.7152/0.0722)

            private static float Contrast(float a, float b) => Mathf.Abs(a - b) / Mathf.Max(a + b, 1e-3f);

            /// <summary>
            /// Removes shadow-like pixels from a difference mask: same chroma direction as the frame without the body
            /// (cosine &gt; 0.995) and darker (luma ratio 0.35–0.97). Returns the share of the mask that was removed.
            /// </summary>
            private static float ShadowShare(bool[] raw, bool[] figure)
            {
                int total = 0, kept = 0;
                for (int i = 0; i < raw.Length && i < figure.Length; i++) { if (raw[i]) { total++; if (figure[i]) kept++; } }
                return total > 0 ? 1f - (float)kept / total : 0f;
            }

            private static float StripShadowLike(bool[] mask, Color32[] with, Color32[] without)
            {
                int total = 0, removed = 0;
                for (int i = 0; i < mask.Length && i < with.Length && i < without.Length; i++)
                {
                    if (!mask[i]) continue;
                    total++;
                    Color32 a = with[i], b = without[i];
                    float la = Luma(a), lb = Luma(b);
                    if (lb < 1e-3f) continue;
                    float ratio = la / lb;
                    if (ratio > 0.97f || ratio < 0.35f) continue;
                    float dot = a.r * (float)b.r + a.g * (float)b.g + a.b * (float)b.b;
                    float na = Mathf.Sqrt(a.r * (float)a.r + a.g * (float)a.g + a.b * (float)a.b);
                    float nb = Mathf.Sqrt(b.r * (float)b.r + b.g * (float)b.g + b.b * (float)b.b);
                    if (na < 1e-3f || nb < 1e-3f) continue;
                    if (dot / (na * nb) > 0.995f) { mask[i] = false; removed++; }
                }
                return total > 0 ? (float)removed / total : 0f;
            }

            private Color32[] Pixels(Camera cam, string png)
            {
                Texture2D tex = CaptureFrame(cam, CaptureWidth, CaptureHeight);
                if (tex == null) return new Color32[CaptureWidth * CaptureHeight];
                try
                {
                    if (png != null) File.WriteAllBytes(png, tex.EncodeToPNG());
                    return tex.GetPixels32();
                }
                finally
                {
                    UnityEngine.Object.Destroy(tex);
                }
            }

            /// <summary>
            /// Hides the renderers that are currently visible and returns exactly that set, so <see cref="Show"/> never switches on a
            /// renderer the game keeps disabled (P-06 finding on run1: the player's hidden M1 capsule body was re-enabled by a blanket
            /// enabled=true and the skin readings measured the capsule palette instead of the AP-006 albedo).
            /// </summary>
            private static List<Renderer> Hide(IEnumerable<Renderer> renderers)
            {
                var hidden = new List<Renderer>();
                foreach (Renderer rr in renderers)
                {
                    if (rr != null && rr.enabled) { rr.enabled = false; hidden.Add(rr); }
                }
                return hidden;
            }

            private static void Show(List<Renderer> hidden)
            {
                if (hidden == null) return;
                foreach (Renderer rr in hidden) if (rr != null) rr.enabled = true;
                hidden.Clear();
            }

            private static string LodState(Component root)
            {
                if (root == null) return "n/a";
                LODGroup g = root.GetComponentInChildren<LODGroup>(true);
                if (g == null) return "noLODGroup";
                LOD[] lods = g.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    for (int k = 0; k < lods[i].renderers.Length; k++)
                    {
                        Renderer rr = lods[i].renderers[k];
                        if (rr != null && rr.enabled && rr.isVisible) return "LOD" + i;
                    }
                }
                return "LOD?";
            }

            // WP-038G (S-05, parity with CharacterTests.Okunabilirlik since WP-038E): the DEC-077 figure mask excludes the
            // figures' own cast shadows / SSAO on the ground (uniform darkening) — the plain difference counted them as
            // crowd (60–68 % of the pixels) and read the M3 closure crowd at 0.019 while the bodies sat at 0.07.
            public static bool[] Diff(Color32[] a, Color32[] b) => DD.Art.ReadabilityThresholds.FigureDiff(a, b);

            public static int Count(bool[] m)
            {
                int n = 0;
                for (int i = 0; i < m.Length; i++) if (m[i]) n++;
                return n;
            }

            public static bool[] Ring(bool[] m, bool[] exclude, int inner, int outer)
            {
                var near = Dilate(m, inner);
                var far = Dilate(m, outer);
                var ring = new bool[m.Length];
                for (int i = 0; i < m.Length; i++) ring[i] = far[i] && !near[i] && !m[i] && !exclude[i];
                return ring;
            }

            private static bool[] Dilate(bool[] src, int radius)
            {
                int w = CaptureWidth, h = CaptureHeight;
                var tmp = new bool[src.Length];
                var dst = new bool[src.Length];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = false;
                    for (int dx = -radius; dx <= radius && !v; dx++) { int xx = x + dx; if (xx >= 0 && xx < w && src[y * w + xx]) v = true; }
                    tmp[y * w + x] = v;
                }

                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = false;
                    for (int dy = -radius; dy <= radius && !v; dy++) { int yy = y + dy; if (yy >= 0 && yy < h && tmp[yy * w + x]) v = true; }
                    dst[y * w + x] = v;
                }

                return dst;
            }

            /// <summary>The part of a figure mask inside a band of its bounding box (row fractions from the top, column fractions from the left).</summary>
            public static bool[] Band(bool[] m, float top, float bottom, float left, float right)
            {
                int w = CaptureWidth, h = CaptureHeight;
                int minX = w, maxX = -1, minY = h, maxY = -1;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (m[y * w + x]) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
                var band = new bool[m.Length];
                if (maxX < 0) return band;
                // rows counted from the TOP of the figure: texture row 0 is the bottom, so the top is maxY
                int y0 = maxY - Mathf.RoundToInt((maxY - minY) * bottom), y1 = maxY - Mathf.RoundToInt((maxY - minY) * top);
                int x0 = minX + Mathf.RoundToInt((maxX - minX) * left), x1 = minX + Mathf.RoundToInt((maxX - minX) * right);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (y >= 0 && y < h && x >= 0 && x < w) band[y * w + x] = m[y * w + x];
                return band;
            }

            private static float Luma(Color32 c) => (float)(0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b);

            public static float MeanLuma(Color32[] px, bool[] m)
            {
                double sum = 0; int n = 0;
                for (int i = 0; i < px.Length; i++) if (m[i]) { sum += 0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b; n++; }
                return n > 0 ? (float)(sum / n / 255.0) : 0f;
            }

            public static float MeanValue(Color32[] px, bool[] m)
            {
                double sum = 0; int n = 0;
                for (int i = 0; i < px.Length; i++) if (m[i]) { sum += Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b)); n++; }
                return n > 0 ? (float)(sum / n / 255.0) : 0f;
            }

            private static string Join(float[] v)
            {
                if (v == null) return "-";
                var sb = new StringBuilder();
                for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append('/'); sb.Append(QaArgs.F(v[i], 3)); }
                return sb.ToString();
            }

            private static void Census(ZombieEnemy z, RoundResult r)
            {
                switch (z.Tier)
                {
                    case EnemyTier.Special: r.special++; break;
                    case EnemyTier.Elite: r.elite++; break;
                    case EnemyTier.Ultra: r.ultra++; break;
                    default: r.standard++; break;
                }

                ArmorSet armor = z.Armor;
                bool any = false;
                if (armor != null)
                {
                    if (armor.IsEquipped(ArmorSlot.Helmet)) { r.helmet++; any = true; }
                    if (armor.IsEquipped(ArmorSlot.Body)) { r.body++; any = true; }
                    if (armor.IsEquipped(ArmorSlot.Leg)) { r.leg++; any = true; }
                }

                if (!any) r.bare++;

                var app = z.VersionVisual as ZombieAppearance;   // the appearance is a plain object owned by the enemy, not a component
                if (app != null && app.IsSkinned)
                {
                    ZombieLook look = app.Look;
                    if (look.IsValid)
                    {
                        r.garments += 2;
                        if (look.topColourway == ZombieModuleLibrary.ColourwayFaded) r.fadedGarments++;
                        if (look.bottomColourway == ZombieModuleLibrary.ColourwayFaded) r.fadedGarments++;
                        if (look.top == ZombieModuleLibrary.TopMinoCape) r.minoCape++;
                        if (look.head == ZombieModuleLibrary.HeadNone) r.bald++;
                    }
                }
            }

            public static void CapsulePoints(Vector3 feet, Vector3 right, float radius, float height, Vector3[] points)
            {
                Vector3 up = Vector3.up;
                points[0] = feet + up * 0.2f;
                points[1] = feet + up * (height * 0.5f);
                points[2] = feet + up * (height - 0.1f);
                points[3] = feet + up * (height * 0.75f) - right * radius;
                points[4] = feet + up * (height * 0.75f) + right * radius;
            }

            public static Texture2D CaptureFrame(Camera cam, int width, int height)
            {
                RenderTexture rt = null;
                Texture2D result = null;
                try
                {
                    rt = new RenderTexture(width, height, 24) { name = "QaReadability RT", antiAliasing = 1 };
                    rt.Create();
                    RenderTexture previousTarget = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = previousTarget;
                    result = new Texture2D(width, height, TextureFormat.RGB24, false);
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                    result.Apply(false, false);
                    RenderTexture.active = previous;
                    return result;
                }
                catch (Exception e)
                {
                    Debug.LogError("[QaReadabilityGate] capture failed: " + e.Message);
                    if (result != null) UnityEngine.Object.Destroy(result);
                    return null;
                }
                finally
                {
                    if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                }
            }

            private static Camera ResolveCamera()
            {
                Camera cam = Camera.main;
                if (cam != null) return cam;
                Camera[] all = Camera.allCameras;
                for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].enabled) return all[i];
                return null;
            }

            // ------------------------------------------------------------------ output

            private void WriteRound(RoundResult r)
            {
                File.WriteAllText(Path.Combine(_outDir, "readability-r" + r.round.ToString("D2") + ".json"), JsonUtility.ToJson(r, true));
                Debug.Log("[QaReadabilityGate] " + (r.pass ? "PASS " : "FAIL ") + "round-" + r.round + " | " + r.note);
            }

            private void Finish()
            {
                if (_finished) return;
                _finished = true;
                if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
                {
                    PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
                }

                bool all = _fail == null && _summary.rounds.Count == _rounds.Length;
                for (int i = 0; i < _summary.rounds.Count; i++) all &= _summary.rounds[i].pass;
                _summary.pass = all;
                _summary.note = _fail ?? "ok";

                var sb = new StringBuilder();
                sb.Append("QaReadabilityGate ").Append(all ? "PASS" : "FAIL").Append(" rounds=").Append(string.Join(",", _rounds)).Append(" crowd=").Append(_crowd).Append(" note=").Append(_summary.note).Append('\n');
                QaReport report = QaReport.Create("QaReadabilityGate", 0);
                for (int i = 0; i < _summary.rounds.Count; i++)
                {
                    RoundResult r = _summary.rounds[i];
                    sb.Append(r.pass ? "PASS " : "FAIL ").Append("round-").Append(r.round.ToString("D2")).Append(" | ").Append(r.note).Append('\n');
                    string src = "readability-r" + r.round.ToString("D2") + ".json";
                    report.Add("readability.r" + r.round + ".occlusion", r.occludedOverHalf == 0 && r.alive > 0, r.occludedOverHalf + "/" + r.onScreen + " on screen", "== 0 over " + QaArgs.F(ReadabilityThresholds.OcclusionRatioMax * 100f, 0) + " %", "capsule 5-ray occlusion", src);
                    report.Add("readability.r" + r.round + ".zombieCrowdContrast", r.zombiePixels == 0 || r.zombieEnvDeltaVMean >= ReadabilityThresholds.CapsuleContrastMin, QaArgs.F(r.zombieEnvDeltaVMean, 3) + " (ΔV " + QaArgs.F(r.zombieEnvDeltaVMin, 3) + ")", ">= " + QaArgs.F(ReadabilityThresholds.CapsuleContrastMin) + " (WP-016 method)", r.onScreen + " on screen, " + r.zombiePixels + " px, FigureDiff (WP-038G shared rule) dropped " + QaArgs.F(r.zombieShadowShare * 100f, 0) + " % shadow/AO px", src);
                    for (int s = 0; r.playerEnvDeltaV != null && s < r.playerEnvDeltaV.Length; s++)
                    {
                        report.Add("readability.r" + r.round + ".playerEnvContrast.skin" + _skins[s], r.playerEnvDeltaV[s] >= ReadabilityThresholds.CapsuleContrastMin, QaArgs.F(r.playerEnvDeltaV[s], 3) + " (body " + QaArgs.F(r.playerBodyContrast != null ? r.playerBodyContrast[s] : -1f, 3) + ")", ">= " + QaArgs.F(ReadabilityThresholds.CapsuleContrastMin), "luma figure " + QaArgs.F(r.playerFigureLuma != null ? r.playerFigureLuma[s] : -1f, 3) + " / ring " + QaArgs.F(r.playerRingLuma != null ? r.playerRingLuma[s] : -1f, 3) + ", " + r.playerLod + ", shadows in mask", src);
                        report.Add("readability.r" + r.round + ".playerZombieContrast.skin" + _skins[s], r.playerZombieDeltaV[s] >= ReadabilityThresholds.CapsuleContrastMin, QaArgs.F(r.playerZombieDeltaV[s], 3), ">= " + QaArgs.F(ReadabilityThresholds.CapsuleContrastMin), "", src);
                    }

                    // WP-036 tier reading (DEC-077 in-engine): environment contrast is the same DEC-077 rule; tier-vs-Standard is report-only
                    string[] tierNames = { "Standard", "Special", "Elite", "Ultra" };
                    for (int t = 1; r.tierEnvContrast != null && t < 4; t++)
                    {
                        if (r.tierPixels == null || r.tierPixels[t] <= 0) continue;
                        string who = tierNames[t] + " ×" + (r.tierOnScreen != null ? r.tierOnScreen[t] : 0) + " on screen, " + r.tierPixels[t] + " px, armoured " + QaArgs.F(r.tierArmouredShare != null ? r.tierArmouredShare[t] * 100f : -1f, 0) + " %";
                        report.Add("readability.r" + r.round + ".tierEnvContrast." + tierNames[t], r.tierEnvContrast[t] >= ReadabilityThresholds.CapsuleContrastMin, QaArgs.F(r.tierEnvContrast[t], 3), ">= " + QaArgs.F(ReadabilityThresholds.CapsuleContrastMin), who, src);
                        report.Add("readability.r" + r.round + ".tierVsStandard." + tierNames[t], true, QaArgs.F(r.tierVsStandardContrast[t], 3) + " (ΔV " + QaArgs.F(r.tierVsStandardDeltaV[t], 3) + ")", "report only (AP-014 Blender ΔV min 0.064)", "Standard " + (r.tierPixels[0]) + " px", src);
                    }
                }

                if (_fail != null) report.Add("readability.driver", false, "aborted", "", _fail);
                report.summary = "armour form test (≥ 30 % + band 0.08/0.12): DD.Art PlayMode ArmorTests.Okunabilirlik_DEC077_… (regression.sh step)";
                try
                {
                    File.WriteAllText(Path.Combine(_outDir, "readability-summary.json"), JsonUtility.ToJson(_summary, true));
                    File.WriteAllText(Path.Combine(_outDir, "summary.txt"), sb.ToString());
                    report.Write(_outDir);
                }
                catch (Exception e)
                {
                    Debug.LogError("[QaReadabilityGate] write failed: " + e);
                }

                Debug.Log("[QaReadabilityGate] " + (all ? "PASS" : "FAIL") + " " + _summary.note);
                if (_bootFlow) StartCoroutine(QuitSoon(all));
            }

            private IEnumerator QuitSoon(bool ok)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                Application.Quit(ok ? 0 : 3);
            }

            private IEnumerator WaitForScene(string name, float timeout)
            {
                yield return WaitUntil(() => SceneManager.GetActiveScene().name == name, "scene '" + name + "' did not load", timeout);
            }

            private IEnumerator WaitUntil(Func<bool> condition, string what, float timeout)
            {
                float t0 = Time.realtimeSinceStartup;
                while (!condition())
                {
                    if (Time.realtimeSinceStartup - t0 > timeout)
                    {
                        _fail = what + " within " + QaArgs.F(timeout, 0) + " s";
                        Debug.LogError("[QaReadabilityGate] " + _fail);
                        yield break;
                    }

                    yield return null;
                }
            }
        }
    }
}
#endif
