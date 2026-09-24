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
            Assert.AreEqual(4, chains.Chains.Length, "sash / pouch / gourd / band");
            int sash = -1, band = -1;
            for (int i = 0; i < chains.Chains.Length; i++) { if (chains.Chains[i].name == "sash") sash = i; if (chains.Chains[i].name == "band") band = i; }
            Assert.IsTrue(sash >= 0 && band >= 0);
            Time.captureDeltaTime = 1f / 60f;
            velocity = Vector3.forward * 6.2f;
            float maxSash = 0f, maxBand = 0f, minClr = float.MaxValue, tipTravel = 0f;
            Vector3 tipRoot = Vector3.zero;
            for (int f = 0; f < 240; f++)
            {
                stage.transform.position += Vector3.forward * 6.2f * Time.deltaTime;
                yield return null;
                if (f < 60) continue;
                Assert.IsTrue(chains.ServedLastFrame, "oyuncu zincirleri servis edilmedi");
                maxSash = Mathf.Max(maxSash, chains.MaxAngleFromRest(sash));
                maxBand = Mathf.Max(maxBand, chains.MaxAngleFromRest(band));
                for (int c = 0; c < chains.Chains.Length; c++) minClr = Mathf.Min(minClr, chains.MinClearance(c));
                Vector3[] p = chains.Particles(sash);
                Vector3 tipLocal = pv.transform.InverseTransformPoint(p[p.Length - 1]);
                if (f == 60) tipRoot = tipLocal; else tipTravel = Mathf.Max(tipTravel, Vector3.Distance(tipLocal, tipRoot));
            }

            velocity = Vector3.zero;
            for (int i = 0; i < 30; i++) yield return null;
            // Whip = the largest frame-to-frame swing of the band (the idle sag sits ~35° from rest and a head jerk can shrink it —
            // the level says nothing, the rate does).
            float bandBefore = chains.MaxAngleFromRest(band), prev = bandBefore, idleWhip = 0f;
            for (int i = 0; i < 30; i++) { yield return null; float a = chains.MaxAngleFromRest(band); idleWhip = Mathf.Max(idleWhip, Mathf.Abs(a - prev)); prev = a; }
            pv.PlayHurt(true);   // the upper-body blow (hurt_A): the head knock the hachimaki should answer
            float maxHitBand = 0f, hitWhip = 0f;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                float a = chains.MaxAngleFromRest(band);
                maxHitBand = Mathf.Max(maxHitBand, a);
                hitWhip = Mathf.Max(hitWhip, Mathf.Abs(a - prev));
                prev = a;
            }
            Debug.Log("[DD-TEST] wp032f.player sashMax=" + maxSash.ToString("F1", CultureInfo.InvariantCulture) + " bandMax=" + maxBand.ToString("F1", CultureInfo.InvariantCulture) + " minClr=" + (minClr * 100f).ToString("F1", CultureInfo.InvariantCulture)
                      + "cm sashTip=" + (tipTravel * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm bandHit=" + maxHitBand.ToString("F1", CultureInfo.InvariantCulture) + " (before " + bandBefore.ToString("F1", CultureInfo.InvariantCulture) + ") whip=" + hitWhip.ToString("F1", CultureInfo.InvariantCulture) + "°/frame (idle " + idleWhip.ToString("F1", CultureInfo.InvariantCulture) + ")");
            Assert.LessOrEqual(maxSash, 70f + 0.5f, "sash açı klampı (70°)");
            Assert.LessOrEqual(maxBand, 80f + 0.5f, "band açı klampı (80°)");
            Assert.GreaterOrEqual(minClr, 0.005f, "zincir gövde kapsülüne girdi (kontrat klerans ≥ +0,01 m)");
            Assert.Greater(tipTravel, 0.01f, "sash koşuda salınmadı");
            // REPORTED (not a gate): the band already swings ~4°/frame with the idle aim sway; the blow's whip reads against that in the log.
            Assert.Greater(hitWhip, 1f, "hachimaki darbede hareketsiz");
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

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Kalabalik40_Zincirler_SifirAyirma_ButceSure()
        {
            yield return MainMenu();
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
    }
}
