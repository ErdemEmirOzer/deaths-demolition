using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-032F · AP-006C secondary chains on the live rigs: the player's four chains and the zombie's
    /// seven are bound, swing under locomotion within the contract's angle limits, keep the contract
    /// clearance from the parent capsule after the push-out, respond to a hit (hachimaki whip), and
    /// cost nothing in allocations on a 40-body crowd within the time budget.
    /// </summary>
    [TestFixture]
    [Category("WP032F")]
    public sealed class AnimationSecondaryTests
    {
        private const float SahneZamanAsimi = 60f;
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); SecondaryChainDriver.ResetStatics(); FootIkAdapter.ResetStatics(); CharacterAnimatorLod.ResetStatics(); }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            SecondaryChainDriver.ResetStatics();
            FootIkAdapter.Force = -1;
            foreach (GameObject go in _temp) if (go != null) UnityEngine.Object.Destroy(go);
            _temp.Clear();
            CharacterAssetSet.ResetCache();
            EnemyTargeting.Clear();
            yield return null;
        }

        private IEnumerator MainMenu()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
        }

        private ZombieSkinnedVisual ZombieOnStage(Vector3 at, uint seed)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var root = new GameObject("WP032F_Zombie");
            _temp.Add(root);
            root.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.right));
            GameObject go = UnityEngine.Object.Instantiate(set.ZombiePrefab, root.transform, false);
            var v = go.GetComponent<ZombieSkinnedVisual>();
            v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, seed));
            v.ApplyMaterial(set.ZombieLibrary, 0);
            v.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return v;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_DortZincir_KosuSalinimi_AciSiniri_Klerans_HitKirbaci()
        {
            yield return MainMenu();
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP032F_PlayerStage");
            _temp.Add(stage);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(stage.transform, false);
            GameObject go = UnityEngine.Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            Vector3 velocity = Vector3.zero;
            pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
            pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            SecondaryChainDriver chains = pv.Chains;
            Assert.IsNotNull(chains, "oyuncuda SecondaryChainDriver yok (AP-006C rig?)");
            // WP-082 R3: hero v2 drives only its weighted chain — HeroV2-chains.json: sash (max 50°); sec_band / pouch / gourd exist on
            // the rig without geometry (AP-024 README), so there is no band to whip. The AP-006C body's four chains left with it.
            Assert.AreEqual(1, chains.Chains.Length, "hero v2: yalnız sash (HeroV2-chains.json)");
            int sash = -1, band = -1;
            for (int i = 0; i < chains.Chains.Length; i++) { if (chains.Chains[i].name == "sash") sash = i; if (chains.Chains[i].name == "band") band = i; }
            Assert.IsTrue(sash >= 0, "sash zinciri yok");
            float sashLimit = chains.Chains[sash].maxAngleDegrees;
            Time.captureDeltaTime = 1f / 60f;
            velocity = Vector3.forward * 6.2f;
            float maxSash = 0f, maxBand = 0f, minClr = float.MaxValue, tipTravel = 0f;
            int sashAtClamp = 0, runFrames = 0;
            Vector3 tipRoot = Vector3.zero;
            for (int f = 0; f < 240; f++)
            {
                stage.transform.position += Vector3.forward * 6.2f * Time.deltaTime;
                yield return null;
                if (f < 60) continue;
                Assert.IsTrue(chains.ServedLastFrame, "oyuncu zincirleri servis edilmedi");
                float sashAngle = chains.MaxAngleFromRest(sash);
                maxSash = Mathf.Max(maxSash, sashAngle);
                if (sashAngle >= sashLimit - 1f) sashAtClamp++;   // WP-111 R4: riding the clamp = a hard stop between the thighs
                runFrames++;
                if (band >= 0) maxBand = Mathf.Max(maxBand, chains.MaxAngleFromRest(band));
                for (int c = 0; c < chains.Chains.Length; c++) minClr = Mathf.Min(minClr, chains.MinClearance(c));
                Vector3[] p = chains.Particles(sash);
                Vector3 tipLocal = pv.transform.InverseTransformPoint(p[p.Length - 1]);
                if (f == 60) tipRoot = tipLocal; else tipTravel = Mathf.Max(tipTravel, Vector3.Distance(tipLocal, tipRoot));
            }

            velocity = Vector3.zero;
            for (int i = 0; i < 30; i++) yield return null;
            // Whip = the largest frame-to-frame swing of the band (the idle sag sits ~35° from rest and a head jerk can shrink it —
            // the level says nothing, the rate does).
            // hero v2 has no band chain: the whip reads n/a (−1) and is not gated
            float bandBefore = band >= 0 ? chains.MaxAngleFromRest(band) : -1f, prev = bandBefore, idleWhip = 0f;
            float maxHitBand = -1f, hitWhip = -1f;
            if (band >= 0)
            {
                for (int i = 0; i < 30; i++) { yield return null; float a = chains.MaxAngleFromRest(band); idleWhip = Mathf.Max(idleWhip, Mathf.Abs(a - prev)); prev = a; }
                pv.PlayHurt(true);   // the upper-body blow (hurt_A): the head knock the hachimaki should answer
                maxHitBand = 0f; hitWhip = 0f;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    float a = chains.MaxAngleFromRest(band);
                    maxHitBand = Mathf.Max(maxHitBand, a);
                    hitWhip = Mathf.Max(hitWhip, Mathf.Abs(a - prev));
                    prev = a;
                }
            }
            Debug.Log("[DD-TEST] wp032f.player sashMax=" + maxSash.ToString("F1", CultureInfo.InvariantCulture) + " bandMax=" + maxBand.ToString("F1", CultureInfo.InvariantCulture) + " minClr=" + (minClr * 100f).ToString("F1", CultureInfo.InvariantCulture)
                      + "cm sashTip=" + (tipTravel * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm bandHit=" + maxHitBand.ToString("F1", CultureInfo.InvariantCulture) + " (before " + bandBefore.ToString("F1", CultureInfo.InvariantCulture) + ") whip=" + hitWhip.ToString("F1", CultureInfo.InvariantCulture) + "°/frame (idle " + idleWhip.ToString("F1", CultureInfo.InvariantCulture) + ")");
            Assert.LessOrEqual(maxSash, sashLimit + 0.5f, "sash açı klampı (" + sashLimit + "°)");
            Debug.Log("[DD-TEST] wp111.sash atClamp=" + sashAtClamp + "/" + runFrames + " max=" + maxSash.ToString("F1", CultureInfo.InvariantCulture) + "/" + sashLimit);
            // WP-111 R4: at 0.45 / 0.15 the run pinned the tails on the clamp 27 % of the frames; the tuned sash stays off it
            Assert.LessOrEqual(sashAtClamp, runFrames / 20, "sash koşuda klampa dayanıyor (" + sashAtClamp + "/" + runFrames + " kare)");
            Assert.GreaterOrEqual(minClr, 0.005f, "zincir gövde kapsülüne girdi (kontrat klerans ≥ +0,01 m)");
            Assert.Greater(tipTravel, 0.01f, "sash koşuda salınmadı");
            if (band >= 0)
            {
                Assert.LessOrEqual(maxBand, chains.Chains[band].maxAngleDegrees + 0.5f, "band açı klampı");
                // REPORTED (not a gate): the band already swings ~4°/frame with the idle aim sway; the blow's whip reads against that in the log.
                Assert.Greater(hitWhip, 1f, "hachimaki darbede hareketsiz");
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zombi_YediZincir_Shamble_AciSiniri_Klerans()
        {
            yield return MainMenu();
            ZombieSkinnedVisual v = ZombieOnStage(new Vector3(-10f, 0f, 0f), 7u);
            SecondaryChainDriver chains = v.Chains;
            Assert.IsNotNull(chains, "zombide SecondaryChainDriver yok (AP-007 v003 rig?)");
            Assert.AreEqual(7, chains.Chains.Length, "mino ×3, hair_mass, queue, hachimaki, bandage_tie");
            Time.captureDeltaTime = 1f / 60f;
            float[] maxAngle = new float[7];
            float minClr = float.MaxValue;
            for (int f = 0; f < 240; f++)
            {
                v.transform.parent.position += Vector3.right * 2.7f * Time.deltaTime;
                v.SetSpeed(2.7f, Time.deltaTime);
                yield return null;
                if (f < 60) continue;
                for (int c = 0; c < 7; c++) { maxAngle[c] = Mathf.Max(maxAngle[c], chains.MaxAngleFromRest(c)); minClr = Mathf.Min(minClr, chains.MinClearance(c)); }
            }

            var rows = new List<string>();
            for (int c = 0; c < 7; c++)
            {
                rows.Add(chains.Chains[c].name + "=" + maxAngle[c].ToString("F1", CultureInfo.InvariantCulture) + "°/" + chains.Chains[c].maxAngleDegrees);
                Assert.LessOrEqual(maxAngle[c], chains.Chains[c].maxAngleDegrees + 0.5f, chains.Chains[c].name + " açı klampı");
            }

            Debug.Log("[DD-TEST] wp032f.zombie " + string.Join(" ", rows) + " minClr=" + (minClr * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm");
            Assert.GreaterOrEqual(minClr, 0.005f, "zincir gövde kapsülüne girdi");
        }

        /// <summary>
        /// WP-111 R4 (S-06, C-00 11.10 · R3 note: the hero sash rides its 50° clamp on a run) · calibration only: the player test's
        /// 6.2 m/s run with the sash's stiffness / damping swept at runtime — peak angle, the share of frames within 1° of the clamp,
        /// the tip's swing and the clearance per pair. Picks nothing, asserts nothing.
        /// </summary>
        [UnityTest]
        [Explicit("WP-111 R4 kalibrasyon — sash sertlik / sönüm taraması")]
        [Timeout(300000)]
        public IEnumerator Oyuncu_Sash_SertlikSonum_Taramasi()
        {
            yield return MainMenu();
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP111_SashStage");
            _temp.Add(stage);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(stage.transform, false);
            GameObject go = UnityEngine.Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            Vector3 velocity = Vector3.zero;
            pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
            pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            SecondaryChainDriver chains = pv.Chains;
            int sash = -1;
            for (int i = 0; i < chains.Chains.Length; i++) if (chains.Chains[i].name == "sash") sash = i;
            Assert.IsTrue(sash >= 0, "sash zinciri yok");
            SecondaryChainDriver.Chain ch = chains.Chains[sash];
            float s0 = ch.stiffness, d0 = ch.damping, limit = ch.maxAngleDegrees;
            Time.captureDeltaTime = 1f / 60f;
            var pairs = new[] { new Vector2(s0, d0), new Vector2(0.55f, 0.2f), new Vector2(0.65f, 0.25f), new Vector2(0.75f, 0.3f), new Vector2(0.65f, 0.35f), new Vector2(0.85f, 0.35f) };
            try
            {
                foreach (Vector2 sd in pairs)
                {
                    ch.stiffness = sd.x;
                    ch.damping = sd.y;
                    chains.Reset();
                    velocity = Vector3.zero;
                    for (int i = 0; i < 30; i++) yield return null;
                    velocity = Vector3.forward * 6.2f;
                    float maxA = 0f, minClr = float.MaxValue, tipTravel = 0f;
                    int atClamp = 0, n = 0;
                    Vector3 tipRoot = Vector3.zero;
                    for (int f = 0; f < 240; f++)
                    {
                        stage.transform.position += Vector3.forward * 6.2f * Time.deltaTime;
                        yield return null;
                        if (f < 60) continue;
                        float a = chains.MaxAngleFromRest(sash);
                        maxA = Mathf.Max(maxA, a);
                        if (a >= limit - 1f) atClamp++;
                        n++;
                        minClr = Mathf.Min(minClr, chains.MinClearance(sash));
                        Vector3[] p = chains.Particles(sash);
                        Vector3 tipLocal = pv.transform.InverseTransformPoint(p[p.Length - 1]);
                        if (f == 60) tipRoot = tipLocal; else tipTravel = Mathf.Max(tipTravel, Vector3.Distance(tipLocal, tipRoot));
                    }

                    Debug.Log("[DD-TEST] wp111.sash.sweep stiffness=" + sd.x.ToString("F2", CultureInfo.InvariantCulture) + " damping=" + sd.y.ToString("F2", CultureInfo.InvariantCulture)
                              + " max=" + maxA.ToString("F1", CultureInfo.InvariantCulture) + "/" + limit.ToString("F0", CultureInfo.InvariantCulture)
                              + " atClamp=" + (100f * atClamp / Mathf.Max(1, n)).ToString("F0", CultureInfo.InvariantCulture) + "%"
                              + " tip=" + (tipTravel * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm minClr=" + (minClr * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm");
                }
            }
            finally
            {
                ch.stiffness = s0;
                ch.damping = d0;
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Kalabalik40_Zincirler_SifirAyirma_ButceSure()
        {
            yield return MainMenu();
            SecondaryChainDriver.CrowdLodForce = 0;   // WP-082 R3: this budget reads every body every frame (the crowd LOD has its own test)
            var visuals = new List<ZombieSkinnedVisual>();
            for (int i = 0; i < 40; i++) visuals.Add(ZombieOnStage(new Vector3((i % 8) * 1.5f, 0f, (i / 8) * 1.5f), (uint)(20 + i)));
            int served = 0;
            for (int f = 0; f < 30; f++)
            {
                for (int i = 0; i < visuals.Count; i++) visuals[i].SetSpeed(2.7f * ((f / 15 + i) % 2), Time.deltaTime);
                yield return null;
                served = Mathf.Max(served, SecondaryChainDriver.ServedLastCompletedFrame);
            }

            Assert.AreEqual(40, served, "40 gövde servis edilmeli (bütçe 40)");

            // WP-032H: the old gate (chains on − off over 10-frame windows) read the scene's own 4–7 allocations/frame and flipped
            // sign between full-suite runs (−0.68 … +0.86). Deterministic form (WP-044 referee method): inside ONE frame, step all
            // 40 bodies Amplify times through the real driver path; a control frame runs the same loop without the step; a positive
            // control frame allocates 64 × 1 KB. Any per-step allocation shows as ≥ 40 × Amplify counts; the frame noise stays < 20.
            const int Amplify = 50, Pairs = 4;
            int savedRate = Application.targetFrameRate;
            Application.targetFrameRate = -1;   // WP-052: the Editor runner keeps LastValue at 0 while capped at 60
            using var recCount = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocation In Frame Count");
            using var recBytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            var drivers = new SecondaryChainDriver[visuals.Count];
            for (int i = 0; i < visuals.Count; i++) drivers[i] = visuals[i].Chains;
            SecondaryChainDriver.Force = 1;
            for (int i = 0; i < drivers.Length; i++) drivers[i].StepForProbe(1f / 60f);   // first step sizes the particle arrays (one-time)
            yield return null; yield return null;

            long stepMin = long.MaxValue, controlMax = 0, positiveCount = 0, positiveBytes = 0;
            object[] keep = null;
            for (int pair = 0; pair < Pairs; pair++)
            {
                // control frame: the same loop shape, no step
                int sink = 0;
                for (int k = 0; k < Amplify; k++) for (int i = 0; i < drivers.Length; i++) sink += drivers[i] != null ? 1 : 0;
                yield return null;                                  // LastValue now = the control frame
                controlMax = Math.Max(controlMax, recCount.LastValue);
                for (int k = 0; k < Amplify; k++) for (int i = 0; i < drivers.Length; i++) drivers[i].StepForProbe(1f / 60f);
                yield return null;                                  // LastValue now = the stepping frame
                stepMin = Math.Min(stepMin, recCount.LastValue);
                if (sink < 0) Debug.Log(sink);
            }

            keep = new object[64];
            for (int i = 0; i < keep.Length; i++) keep[i] = new byte[1024];
            yield return null;
            positiveCount = recCount.LastValue; positiveBytes = recBytes.LastValue;
            keep = null;
            SecondaryChainDriver.Force = -1;
            Application.targetFrameRate = savedRate;

            int bodySteps = Amplify * drivers.Length;
            Debug.Log("[DD-TEST] wp032h.alloc bodySteps/frame=" + bodySteps + " stepFrameMin=" + stepMin + " controlFrameMax=" + controlMax
                      + " positive=" + positiveCount + " / " + positiveBytes + " B");
            Assert.GreaterOrEqual(positiveCount, 64, "pozitif kontrol: sayaç 64 × 1 KB ayırmayı görmedi (ölçüm kör)");
            Assert.GreaterOrEqual(positiveBytes, 64 * 1024, "pozitif kontrol: 64 KB görülmedi");
            // A per-step allocation would add ≥ bodySteps (2000) to the stepping frame; the gate leaves room only for frame noise.
            Assert.LessOrEqual(stepMin - controlMax, 20, "zincir adımı ayırıyor: " + bodySteps + " gövde adımında +" + (stepMin - controlMax));
        }

        /// <summary>
        /// WP-082 R3 perf (C-00 11.10) · the crowd LOD: Yüksek + TopDown only; ranked from the eye, the nearest 12 bodies step every
        /// frame, the next 16 every 2nd, the rest every 4th — per body exactly that cadence, fewer steps a frame than the 40 without it,
        /// and the chains still inside the contract's angle limit (the carried time does not fling them).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Kalabalik40_KalabalikLod_YakinTamHiz_UzakSeyrek_SabitMaliyet()
        {
            Assert.IsTrue(SecondaryChainDriver.CrowdLodApplies(QualityTier.Yuksek, DD.Prototype.CameraSystem.CameraMode.TopDown), "Yüksek + TD");
            Assert.IsFalse(SecondaryChainDriver.CrowdLodApplies(QualityTier.Sinematik, DD.Prototype.CameraSystem.CameraMode.TopDown), "Sinematik aynı kalır");
            Assert.IsFalse(SecondaryChainDriver.CrowdLodApplies(QualityTier.Performans, DD.Prototype.CameraSystem.CameraMode.TopDown), "Performans aynı kalır");
            Assert.IsFalse(SecondaryChainDriver.CrowdLodApplies(QualityTier.Yuksek, DD.Prototype.CameraSystem.CameraMode.ThirdPerson), "TP aynı kalır");
            Assert.IsFalse(SecondaryChainDriver.CrowdLodApplies(QualityTier.Yuksek, DD.Prototype.CameraSystem.CameraMode.FirstPerson), "FP aynı kalır");

            yield return MainMenu();
            // far from the menu scene so only these 40 rank first; the eye sits off the grid's diagonal (no equal distances)
            Vector3 o = new Vector3(500f, 0f, 500f);
            var visuals = new List<ZombieSkinnedVisual>();
            for (int i = 0; i < 40; i++) visuals.Add(ZombieOnStage(o + new Vector3((i % 8) * 1.5f, 0f, (i / 8) * 1.5f), (uint)(60 + i)));
            var eye = new GameObject("WP082_LodEye");
            _temp.Add(eye);
            eye.transform.position = o + new Vector3(-6.3f, 14f, -2.1f);
            SecondaryChainDriver.LodEyeOverride = eye.transform;
            // WP-111 R4: a fixed 60 fps clock, so the carried step is the same on every machine (the batch editor's frames are 1–3 ms).
            // The r4hero / r4hero2 reds (hair_mass 7.4° / 10.1° past its cone on a sparse body, also at a fixed 16.7 ms) were the stale
            // particles between sparse steps — fixed in the driver's gap carry, not here.
            Time.captureDeltaTime = 1f / 60f;
            var drivers = new SecondaryChainDriver[visuals.Count];
            for (int i = 0; i < visuals.Count; i++) drivers[i] = visuals[i].Chains;
            Assert.GreaterOrEqual(SecondaryChainDriver.CrowdCount, 40, "kalabalık gövdeleri LOD sırasına kayıtlı değil");

            // the expected rank / cadence by the driver's own rule (same positions, same arithmetic; no equal distances here)
            var d2 = new float[drivers.Length];
            for (int i = 0; i < drivers.Length; i++) d2[i] = (drivers[i].transform.position - eye.transform.position).sqrMagnitude;
            var rank = new int[drivers.Length];
            for (int i = 0; i < drivers.Length; i++) for (int k = 0; k < drivers.Length; k++) if (d2[k] < d2[i]) rank[i]++;
            var stride = new int[drivers.Length];
            for (int i = 0; i < drivers.Length; i++)
                stride[i] = rank[i] < SecondaryChainDriver.LodFullBodies ? 1 : rank[i] < SecondaryChainDriver.LodFullBodies + SecondaryChainDriver.LodHalfBodies ? 2 : 4;

            const int Warm = 8, Frames = 32;
            double microsOff = 0, microsOn = 0;
            SecondaryChainDriver.CrowdLodForce = 0;
            int offMin = int.MaxValue;
            for (int f = 0; f < Warm + Frames; f++)
            {
                for (int i = 0; i < visuals.Count; i++) visuals[i].SetSpeed(2.7f * ((f / 8 + i) % 2), Time.deltaTime);
                yield return null;
                if (f < Warm) continue;
                int n = 0;
                for (int i = 0; i < drivers.Length; i++) if (drivers[i].ServedLastFrame) n++;
                offMin = Mathf.Min(offMin, n);
                microsOff += SecondaryChainDriver.MicrosLastCompletedFrame;
            }

            Assert.AreEqual(40, offMin, "LOD kapalıyken her gövde her kare");
            SecondaryChainDriver.CrowdLodForce = 1;
            var steps = new int[drivers.Length];
            int perMin = int.MaxValue, perMax = 0, perSum = 0;
            bool finite = true;
            float worstOver = float.MinValue, maxDt = 0f;
            string worstChain = "";
            for (int f = 0; f < Warm + Frames; f++)
            {
                for (int i = 0; i < visuals.Count; i++) visuals[i].SetSpeed(2.7f * ((f / 8 + i) % 2), Time.deltaTime);
                yield return null;
                if (f < Warm) continue;
                int n = 0;
                maxDt = Mathf.Max(maxDt, Time.deltaTime);
                for (int i = 0; i < drivers.Length; i++)
                {
                    if (!drivers[i].ServedLastFrame) continue;
                    steps[i]++; n++;
                    // checked on the frame it stepped: a skipped body's particles are its last step's world points while the parent moved
                    // on (its bones follow the parent rigidly in between), so they are not a pose to clamp-check
                    for (int c = 0; c < drivers[i].Chains.Length; c++)
                    {
                        Vector3[] p = drivers[i].Particles(c);
                        if (p == null) continue;
                        foreach (Vector3 q in p) if (float.IsNaN(q.x) || float.IsInfinity(q.x)) finite = false;
                        float over = drivers[i].MaxAngleFromRest(c) - drivers[i].Chains[c].maxAngleDegrees;
                        if (over > worstOver) { worstOver = over; worstChain = drivers[i].Chains[c].name + "#" + i + "/s" + stride[i]; }
                    }
                }

                perMin = Mathf.Min(perMin, n); perMax = Mathf.Max(perMax, n); perSum += n;
                microsOn += SecondaryChainDriver.MicrosLastCompletedFrame;
            }

            SecondaryChainDriver.CrowdLodForce = -1;
            var wrong = new List<string>();
            int[] tierCount = new int[5];
            for (int i = 0; i < drivers.Length; i++)
            {
                tierCount[stride[i]]++;
                if (steps[i] != Frames / stride[i] || drivers[i].LodRank != rank[i]) wrong.Add(i + ":r" + rank[i] + "/" + drivers[i].LodRank + ":" + stride[i] + "→" + steps[i]);
            }

            float perMean = perSum / (float)Frames;
            Debug.Log("[DD-TEST] wp082.chainlod tiers full/half/quarter=" + tierCount[1] + "/" + tierCount[2] + "/" + tierCount[4]
                      + " stepsPerFrame mean=" + perMean.ToString("F1", CultureInfo.InvariantCulture) + " min=" + perMin + " max=" + perMax
                      + " micros/frame off=" + (microsOff / Frames).ToString("F0", CultureInfo.InvariantCulture) + " on=" + (microsOn / Frames).ToString("F0", CultureInfo.InvariantCulture)
                      + " maxDt=" + (maxDt * 1000f).ToString("F1", CultureInfo.InvariantCulture) + "ms"
                      + " wrong=" + wrong.Count + " worstAngleOver=" + worstOver.ToString("F1", CultureInfo.InvariantCulture) + " (" + worstChain + ")");
            Assert.AreEqual(SecondaryChainDriver.LodFullBodies, tierCount[1], "tam hız gövde sayısı");
            Assert.AreEqual(SecondaryChainDriver.LodHalfBodies, tierCount[2], "yarım hız gövde sayısı");
            Assert.IsEmpty(wrong, "gövde sırası / kadansı (i:r beklenen/gerçek:adım→sayılan): " + string.Join(" ", wrong));
            Assert.IsTrue(finite, "zincir parçacığı sonlu değil");
            Assert.LessOrEqual(worstOver, 0.5f, worstChain + " açı klampı (adım karesinde)");
            // 12 + 16/2 + 12/4 = 23 every frame: the rank is the phase, so the count does not swing
            Assert.AreEqual(23, perMin, "kare başı adım (en az)");
            Assert.AreEqual(23, perMax, "kare başı adım (en çok)");
        }
    }
}
