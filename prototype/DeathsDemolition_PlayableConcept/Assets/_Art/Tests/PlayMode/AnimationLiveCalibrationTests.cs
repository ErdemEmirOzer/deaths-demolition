using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
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
    /// WP-032D · WP-082 R3 · Live-rig calibration of the player's locomotion loops on the hero. The AP-026 8-way tree (LocoSpeed):
    /// every one of the 24 loops (walk / run / sprint × 8 directions) alone — LocoSpeed on its ring's threshold (the 1-D blend's
    /// pure ring), MoveX / MoveY its unit direction — with the body moving at the loop's shipped honest speed (stride_template × k /
    /// cycle) along the loop's travel direction; the state rate (LocoRate) is set by hand to two values and the signed contact slip is
    /// measured at each. The zero crossing gives the rate at which the landed foot does not skate: live no-slip speed = body speed /
    /// rate0, and the loop's k = live × cycle / stride_template (LOCO-SOZLESME §12.1; the contract defines k on sprint_fwd). Writes
    /// DD-TEST lines (wp032d.livecal, wp082.k) for loco_constants.json#hero_stride_scale; asserts every loop's shipped calibration slips
    /// ≤ 3 cm per stance (the WP-032D gate) — red while the shipped k is not the hero's.
    /// The AP-022 4-way set (no ring tables) keeps the WP-032D four run cycles.
    /// </summary>
    [TestFixture]
    [Category("WP032D")]
    public sealed class AnimationLiveCalibrationTests
    {
        private const float SahneZamanAsimi = 60f;
        /// <summary>WP-032D gate: the shipped calibration's contact slip per stance, cm.</summary>
        public const float SlipSiniriCm = 3f;
        private readonly List<GameObject> _temp = new List<GameObject>();
        private static readonly int LocoSpeedId = Animator.StringToHash(C.LocoSpeed);

        [SetUp] public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); FootIkAdapter.ResetStatics(); }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            FootIkAdapter.Force = -1;
            foreach (GameObject go in _temp) if (go != null) UnityEngine.Object.Destroy(go);
            _temp.Clear();
            CharacterAssetSet.ResetCache();
            yield return null;
        }

        private IEnumerator MainMenu()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
        }

        /// <summary>Signed contact slip per landing (m) at a fixed LocoRate; the contact rule of AnimationV002Tests / StrideCalibrator.</summary>
        private IEnumerator MeasureSlip(PlayerSkinnedVisual pv, Transform root, Vector3 travel, float locoRate, float speed, int stanceFrames, float timeScale, List<float> outSlips, Action hold)
        {
            Animator an = pv.Animator;
            hold?.Invoke();
            an.SetFloat(C.LocoRate, locoRate);
            float clipRate = locoRate * timeScale;
            float stanceSeconds = stanceFrames / 30f / Mathf.Max(0.1f, clipRate);
            Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform[] feet = { lf, rf };
            for (int i = 0; i < 30; i++) { hold?.Invoke(); an.SetFloat(C.LocoRate, locoRate); root.position += travel * speed * Time.deltaTime; yield return null; }
            bool pending = false, started = false;
            int landedFoot = 0, samples = 0;
            float lastP = 0f, slip = 0f, contactTime = 0f;
            Action<CharacterAnimationEvent> onEvent = e =>
            {
                if (e != CharacterAnimationEvent.FootLeft && e != CharacterAnimationEvent.FootRight) return;
                if (pending && samples > 0) outSlips.Add(slip);
                landedFoot = e == CharacterAnimationEvent.FootLeft ? 0 : 1;
                pending = true; started = false; samples = 0; slip = 0f; contactTime = 0f; lastP = Vector3.Dot(feet[landedFoot].position, travel);
            };
            pv.Events.Raised += onEvent;
            float t0 = Time.time;
            try
            {
                while (Time.time - t0 < 4f)
                {
                    hold?.Invoke();
                    an.SetFloat(C.LocoRate, locoRate);
                    float bodyD = speed * Time.deltaTime;
                    root.position += travel * bodyD;
                    yield return null;
                    if (!pending) continue;
                    float p = Vector3.Dot(feet[landedFoot].position, travel);
                    float d = p - lastP; lastP = p;
                    float relative = d - bodyD;
                    float eps = StrideCalibrator.SettleEpsilon * 30f * Time.deltaTime * clipRate;
                    if (!started) { if (relative >= -eps) continue; started = true; }
                    if (relative > eps || contactTime >= stanceSeconds) { pending = false; outSlips.Add(slip); continue; }
                    slip += d; samples++; contactTime += Time.deltaTime;
                }
            }
            finally { pv.Events.Raised -= onEvent; }
        }

        private IEnumerator Sahne(Action<PlayerSkinnedVisual, Transform> ready)
        {
            yield return MainMenu();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(ground);
            ground.transform.localScale = new Vector3(400f, 0.2f, 400f); ground.transform.position = new Vector3(0f, -0.1f, 0f); ground.layer = PrototypeLayers.Environment;
            FootIkAdapter.Force = 0;
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP032D_PlayerStage");
            _temp.Add(stage);
            Transform root = stage.transform;
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);
            GameObject go = UnityEngine.Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            Vector3 velocity = Vector3.zero;
            pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
            Assert.IsTrue(pv.Authored);
            pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            root.rotation = Quaternion.identity;   // body faces +Z; the clip's travel is its own axis
            ready(pv, root);
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Oyuncu_DortKosuDongusu_CanliKalibrasyon_SifirGecisi()
        {
            PlayerSkinnedVisual pv = null;
            Transform root = null;
            yield return Sahne((a, b) => { pv = a; root = b; });
            CharacterAnimationSet.LocomotionTables tables = pv.AnimationSet.Locomotion;
            Time.captureDeltaTime = 1f / 60f;
            pv.enabled = false;   // the test drives LocoSpeed / MoveX / MoveY / LocoRate
            if (tables.HasRings) yield return Halkalar8(pv, root, tables);
            else yield return Ap022DortKosu(pv, root);
            Time.captureDeltaTime = 0f;
        }

        /// <summary>WP-082 R3: the 24 AP-026 loops, each alone on its ring.</summary>
        private IEnumerator Halkalar8(PlayerSkinnedVisual pv, Transform root, CharacterAnimationSet.LocomotionTables tables)
        {
            Animator an = pv.Animator;
            float kShipped = tables.heroStrideScale;
            var rows = new List<string>();
            var fails = new List<string>();
            var kByRing = new[] { new List<float>(), new List<float>(), new List<float>() };
            float kSprintFwd = float.NaN;
            foreach (CharacterAnimationSet.LocoLoop loop in tables.loops)
            {
                CharacterAnimationSet.Entry e = pv.AnimationSet.Find(loop.clip);
                Assert.IsNotNull(e, loop.clip + " sette yok");
                int slot = Mathf.Clamp(loop.ringSlot, 1, 3);
                float threshold = tables.ringThresholdMps[slot];
                float a = loop.dirIndex * 45f * Mathf.Deg2Rad;
                var move = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                Vector3 travel = new Vector3(move.x, 0f, move.y);
                float timeScale = loop.cycleSecondsNatural > 1e-4f ? e.seconds / loop.cycleSecondsNatural : 1f;
                float natural = loop.strideCycleMTemplate / Mathf.Max(1e-4f, loop.cycleSecondsNatural);   // template m/s
                float bodySpeed = natural * kShipped;                                                     // the shipped honest speed
                int stance = Mathf.Max(1, e.stanceFrames);
                root.position = -travel * 80f;   // 2 × 4.5 s at ≤ 7 m/s stays on the 400 m floor
                Action hold = () => { an.SetFloat(LocoSpeedId, threshold); an.SetFloat(C.MoveX, move.x); an.SetFloat(C.MoveY, move.y); };
                float[] rates = { 1.0f, 1.25f };
                float[] mean = new float[2];
                for (int k = 0; k < 2; k++)
                {
                    var slips = new List<float>();
                    yield return MeasureSlip(pv, root, travel, rates[k], bodySpeed, stance, timeScale, slips, hold);
                    float sum = 0f; foreach (float s in slips) sum += s;
                    mean[k] = slips.Count > 0 ? sum / slips.Count : float.NaN;
                    Assert.GreaterOrEqual(slips.Count, 6, loop.clip + " @" + rates[k] + " yeterli temas ölçülmedi");
                }

                float slope = (mean[1] - mean[0]) / (rates[1] - rates[0]);
                float rate0 = Mathf.Abs(slope) > 1e-6f ? rates[0] - mean[0] / slope : rates[0];
                float live = bodySpeed / Mathf.Max(1e-3f, rate0);             // no-slip speed at the natural cadence (LocoRate 1)
                float kLoop = live / Mathf.Max(1e-4f, natural);
                kByRing[slot - 1].Add(kLoop);
                if (slot == 3 && loop.dirIndex == 0) kSprintFwd = kLoop;
                float shippedSlipCm = mean[0] * 100f;
                rows.Add(loop.clip + " ring " + slot + " dir " + loop.dirIndex + " natural(template)=" + F3(natural) + " body=" + F3(bodySpeed) + " slip@1.0=" + F1(shippedSlipCm) + "cm slip@1.25=" + F1(mean[1] * 100f) +
                         "cm rate0=" + F3(rate0) + " live=" + F3(live) + " m/s k=" + F4(kLoop) + " stance=" + stance + " timeScale=" + F3(timeScale));
                if (Mathf.Abs(shippedSlipCm) > SlipSiniriCm) fails.Add(loop.clip + " " + F1(shippedSlipCm) + " cm @ " + F2(bodySpeed) + " m/s (canlı " + F2(live) + " m/s, k " + F4(kLoop) + ")");
            }

            string[] ad = { "walk", "run", "sprint" };
            var ozet = new List<string>();
            var hepsi = new List<float>();
            for (int g = 0; g < 3; g++) { ozet.Add(ad[g] + " median " + F4(Medyan(kByRing[g])) + " [" + F4(Min(kByRing[g])) + ".." + F4(Max(kByRing[g])) + "] n " + kByRing[g].Count); hepsi.AddRange(kByRing[g]); }
            Debug.Log("[DD-TEST] wp032d.livecal\n" + string.Join("\n", rows));
            Debug.Log("[DD-TEST] wp082.k shipped=" + F4(kShipped) + " sprint_fwd(contract)=" + F4(kSprintFwd) + " all24 median=" + F4(Medyan(hepsi)) + " :: " + string.Join(" | ", ozet) +
                      " :: thresholds(template x k) walk " + F4(1.7161f * kSprintFwd) + " run " + F4(4.025f * kSprintFwd) + " sprint " + F4(6.2f * kSprintFwd));
            Assert.IsEmpty(fails, "sevk edilen k (" + F4(kShipped) + ") ile kayma bütçe dışında (≤ " + SlipSiniriCm + " cm/basış):\n" + string.Join("\n", fails));
        }

        /// <summary>WP-032D (the AP-022 4-way set): the four run cycles at 6.2 m/s.</summary>
        private IEnumerator Ap022DortKosu(PlayerSkinnedVisual pv, Transform root)
        {
            Animator an = pv.Animator;
            const float speed = 6.2f;
            float reference = pv.AnimationSet.LocomotionReferenceSpeed;
            (string clip, Vector2 move, Vector3 travel)[] cycles =
            {
                (C.PlayerRunFwd, new Vector2(0f, 1f), Vector3.forward), (C.PlayerRunBack, new Vector2(0f, -1f), Vector3.back),
                (C.PlayerRunStrafeLeft, new Vector2(-1f, 0f), Vector3.left), (C.PlayerRunStrafeRight, new Vector2(1f, 0f), Vector3.right)
            };
            var rows = new List<string>();
            var fails = new List<string>();
            foreach ((string clip, Vector2 move, Vector3 travel) cy in cycles)
            {
                CharacterAnimationSet.Entry e = pv.AnimationSet.Find(cy.clip);
                float sampled = pv.AnimationSet.SpeedOf(cy.clip);
                float radius = cy.clip == C.PlayerRunBack ? DD.Prototype.Core.Config.PlayerConfigSection.BackwardSpeedMultiplier : 1f;
                float bodySpeed = speed * radius;
                float timeScale = PlayerSkinnedVisual.RunChildTimeScale(reference, sampled, radius);
                int stance = Mathf.Max(1, e.stanceFrames);
                root.position = new Vector3(-30f, 0f, -30f) - cy.travel * 20f;
                an.SetFloat(C.MoveX, cy.move.x * radius); an.SetFloat(C.MoveY, cy.move.y * radius);
                float[] rates = { 1.0f, 1.25f };
                float[] mean = new float[2];
                for (int k = 0; k < 2; k++)
                {
                    var slips = new List<float>();
                    yield return MeasureSlip(pv, root, cy.travel, rates[k] * radius, bodySpeed, stance, timeScale, slips, null);
                    float sum = 0f; foreach (float s in slips) sum += s;
                    mean[k] = slips.Count > 0 ? sum / slips.Count : float.NaN;
                    Assert.GreaterOrEqual(slips.Count, 6, cy.clip + " @" + rates[k] + " yeterli temas ölçülmedi");
                }

                float slope = (mean[1] - mean[0]) / (rates[1] - rates[0]);
                float rate0 = Mathf.Abs(slope) > 1e-6f ? rates[0] - mean[0] / slope : rates[0];
                float live = bodySpeed / Mathf.Max(1e-3f, rate0 * radius * timeScale);
                float shippedSlipCm = mean[0] * 100f;
                rows.Add(cy.clip + " sampled=" + F3(sampled) + " slip@1.0=" + F1(shippedSlipCm) + "cm slip@1.25=" + F1(mean[1] * 100f) + "cm rate0=" + F3(rate0) + " live=" + F3(live) + " m/s");
                bool capped = timeScale < PlayerSkinnedVisual.ChildTimeScale(reference, sampled) - 1e-4f;
                float stanceSeconds = stance / 30f / Mathf.Max(0.1f, radius * timeScale);
                float slideShare = Mathf.Abs(mean[0]) / Mathf.Max(0.01f, bodySpeed * stanceSeconds);
                rows[rows.Count - 1] += (capped ? " capped" : " honest") + " slideShare=" + (slideShare * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%";
                if (!capped && Mathf.Abs(shippedSlipCm) > SlipSiniriCm) fails.Add(cy.clip + " " + F1(shippedSlipCm) + " cm @ " + F2(bodySpeed) + " m/s (canlı " + F2(live) + " m/s)");
                if (capped && slideShare > CadenceBand.SlideClose) fails.Add(cy.clip + " kapaklı kayma %" + (slideShare * 100f).ToString("F0", CultureInfo.InvariantCulture) + " > %" + (CadenceBand.SlideClose * 100f).ToString("F0", CultureInfo.InvariantCulture));
            }

            Debug.Log("[DD-TEST] wp032d.livecal\n" + string.Join("\n", rows));
            Assert.IsEmpty(fails, "sevk edilen kalibrasyonla kayma bütçe dışında:\n" + string.Join("\n", fails));
        }

        private static float Medyan(List<float> xs)
        {
            if (xs.Count == 0) return float.NaN;
            var c = new List<float>(xs);
            c.Sort();
            return c.Count % 2 == 1 ? c[c.Count / 2] : 0.5f * (c[c.Count / 2 - 1] + c[c.Count / 2]);
        }

        private static float Min(List<float> xs) { float m = float.PositiveInfinity; foreach (float x in xs) m = Mathf.Min(m, x); return xs.Count > 0 ? m : float.NaN; }
        private static float Max(List<float> xs) { float m = float.NegativeInfinity; foreach (float x in xs) m = Mathf.Max(m, x); return xs.Count > 0 ? m : float.NaN; }
        private static string F1(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
        private static string F2(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        private static string F3(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
        private static string F4(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
    }
}
