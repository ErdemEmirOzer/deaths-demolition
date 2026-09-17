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
    /// WP-032D · Live-rig calibration of the player's four run cycles: with the body moving at 6.2 m/s along each
    /// clip's direction and the run ring selected, the state rate is set by hand to two values and the signed contact
    /// slip is measured at each; the zero crossing gives the LocoRate at which the landed foot does not skate, and
    /// no-slip speed = sampled speed / LocoRate0. Writes DD-TEST lines for ArtAnimatorBuilder.LiveCalibrationOverrides;
    /// also asserts every run clip's shipped calibration is within 3 cm/step (the gate).
    /// </summary>
    [TestFixture]
    [Category("WP032D")]
    public sealed class AnimationLiveCalibrationTests
    {
        private const float SahneZamanAsimi = 60f;
        private readonly List<GameObject> _temp = new List<GameObject>();

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

        /// <summary>Signed contact slip per landing (cm) at a fixed LocoRate; the contact rule of AnimationV002Tests / StrideCalibrator.</summary>
        private IEnumerator MeasureSlip(PlayerSkinnedVisual pv, Transform root, Vector3 travel, float locoRate, float speed, int stanceFrames, float timeScale, List<float> outSlips)
        {
            Animator an = pv.Animator;
            an.SetFloat(C.LocoRate, locoRate);
            float clipRate = locoRate * timeScale;
            float stanceSeconds = stanceFrames / 30f / Mathf.Max(0.1f, clipRate);
            Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform[] feet = { lf, rf };
            for (int i = 0; i < 30; i++) { an.SetFloat(C.LocoRate, locoRate); root.position += travel * speed * Time.deltaTime; yield return null; }
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

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Oyuncu_DortKosuDongusu_CanliKalibrasyon_SifirGecisi()
        {
            yield return MainMenu();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(ground);
            ground.transform.localScale = new Vector3(120f, 0.2f, 120f); ground.transform.position = new Vector3(0f, -0.1f, 0f); ground.layer = PrototypeLayers.Environment;
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
            Animator an = pv.Animator;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            root.rotation = Quaternion.identity;   // body faces +Z; the clip's travel is its own axis
            const float speed = 6.2f;
            float reference = pv.AnimationSet.LocomotionReferenceSpeed;
            (string clip, Vector2 move, Vector3 travel)[] cycles =
            {
                (C.PlayerRunFwd, new Vector2(0f, 1f), Vector3.forward), (C.PlayerRunBack, new Vector2(0f, -1f), Vector3.back),
                (C.PlayerRunStrafeLeft, new Vector2(-1f, 0f), Vector3.left), (C.PlayerRunStrafeRight, new Vector2(1f, 0f), Vector3.right)
            };
            Time.captureDeltaTime = 1f / 60f;
            pv.enabled = false;   // the test drives MoveX / MoveY / LocoRate
            var rows = new List<string>();
            var fails = new List<string>();
            foreach ((string clip, Vector2 move, Vector3 travel) cy in cycles)
            {
                CharacterAnimationSet.Entry e = pv.AnimationSet.Find(cy.clip);
                float sampled = pv.AnimationSet.SpeedOf(cy.clip);
                float timeScale = PlayerSkinnedVisual.ChildTimeScale(reference, sampled);
                int stance = Mathf.Max(1, e.stanceFrames);
                root.position = new Vector3(-30f, 0f, -30f) - cy.travel * 20f;
                an.SetFloat(C.MoveX, cy.move.x); an.SetFloat(C.MoveY, cy.move.y);
                float[] rates = { 1.0f, 1.25f };
                float[] mean = new float[2];
                for (int k = 0; k < 2; k++)
                {
                    var slips = new List<float>();
                    yield return MeasureSlip(pv, root, cy.travel, rates[k], speed, stance, timeScale, slips);
                    float sum = 0f; foreach (float s in slips) sum += s;
                    mean[k] = slips.Count > 0 ? sum / slips.Count : float.NaN;
                    Assert.GreaterOrEqual(slips.Count, 6, cy.clip + " @" + rates[k] + " yeterli temas ölçülmedi");
                }

                // slip is linear in the rate over this band; zero crossing → LocoRate0; no-slip speed = sampled / LocoRate0
                float slope = (mean[1] - mean[0]) / (rates[1] - rates[0]);
                float rate0 = Mathf.Abs(slope) > 1e-6f ? rates[0] - mean[0] / slope : rates[0];
                float live = sampled / rate0;
                float shippedSlipCm = mean[0] * 100f;   // LocoRate 1.0 = the shipped calibration at 6.2 m/s
                rows.Add(cy.clip + " sampled=" + sampled.ToString("F3", CultureInfo.InvariantCulture) + " slip@1.0=" + shippedSlipCm.ToString("F1", CultureInfo.InvariantCulture) + "cm slip@1.25=" + (mean[1] * 100f).ToString("F1", CultureInfo.InvariantCulture)
                         + "cm rate0=" + rate0.ToString("F3", CultureInfo.InvariantCulture) + " live=" + live.ToString("F3", CultureInfo.InvariantCulture) + " m/s");
                if (Mathf.Abs(shippedSlipCm) > 3f) fails.Add(cy.clip + " " + shippedSlipCm.ToString("F1", CultureInfo.InvariantCulture) + " cm (canlı " + live.ToString("F2", CultureInfo.InvariantCulture) + " m/s)");
            }

            Time.captureDeltaTime = 0f;
            Debug.Log("[DD-TEST] wp032d.livecal\n" + string.Join("\n", rows));
            Assert.IsEmpty(fails, "sevk edilen kalibrasyonla 6,2 m/s'de kayma > 3 cm/adım:\n" + string.Join("\n", fails));
        }
    }
}
