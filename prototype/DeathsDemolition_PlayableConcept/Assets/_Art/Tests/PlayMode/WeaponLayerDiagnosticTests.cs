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
    /// WP-032D · P-06 / AD note "reload and fire frames identical to the aim": measures, on the live rig, how far the
    /// hands travel (model space) while Reload_<w> and Fire_<w> play over the walk — a binding / mask / weight problem
    /// reads as ≈ 0 travel, a clip-amplitude question as small but non-zero travel. Not a gate.
    /// (Original header follows.) Live-rig calibration of the player's four run cycles: with the body moving at 6.2 m/s along each
    /// clip's direction and the run ring selected, the state rate is set by hand to two values and the signed contact
    /// slip is measured at each; the zero crossing gives the LocoRate at which the landed foot does not skate, and
    /// no-slip speed = sampled speed / LocoRate0. Writes DD-TEST lines for ArtAnimatorBuilder.LiveCalibrationOverrides;
    /// also asserts every run clip's shipped calibration is within 3 cm/step (the gate).
    /// </summary>
    [TestFixture]
    [Category("WP032DDiag")]
    public sealed class WeaponLayerDiagnosticTests
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

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Silah_Reload_Fire_ElYolu_CanliOlcum()
        {
            yield return MainMenu();
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP032D_WeaponDiag");
            _temp.Add(stage);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(stage.transform, false);
            GameObject go = UnityEngine.Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            Vector3 velocity = Vector3.zero;
            pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
            Animator an = pv.Animator;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            int over = -1, add = -1;
            for (int i = 0; i < an.layerCount; i++) { if (an.GetLayerName(i) == C.OverrideUpperLayer) over = i; if (an.GetLayerName(i) == C.AdditiveLayer) add = i; }
            Transform rh = an.GetBoneTransform(HumanBodyBones.RightHand), lh = an.GetBoneTransform(HumanBodyBones.LeftHand), root = an.transform;
            var rows = new List<string>();
            for (int w = 0; w < 3; w++)
            {
                pv.SetWeapon(w);
                for (int i = 0; i < 40; i++) yield return null;
                Vector3 rAim = root.InverseTransformPoint(rh.position), lAim = root.InverseTransformPoint(lh.position);
                // reload at the config time: sample the hands every frame, track the max distance from the aim pose
                float reloadSeconds = pv.AnimationSet.Find(C.PlayerWeaponClip(w, "reload")).seconds;
                pv.PlayReload(reloadSeconds);
                float maxR = 0f, maxL = 0f, t0 = Time.time;
                int inReload = 0, frames = 0;
                while (Time.time - t0 < reloadSeconds + 0.2f)
                {
                    yield return null;
                    frames++;
                    if (an.GetCurrentAnimatorStateInfo(over).IsName(C.ReloadState + "_" + w)) inReload++;
                    maxR = Mathf.Max(maxR, Vector3.Distance(root.InverseTransformPoint(rh.position), rAim));
                    maxL = Mathf.Max(maxL, Vector3.Distance(root.InverseTransformPoint(lh.position), lAim));
                }

                for (int i = 0; i < 20; i++) yield return null;
                pv.PlayFire();
                float fireR = 0f; int inFire = 0;
                for (int i = 0; i < 12; i++)
                {
                    yield return null;
                    if (an.GetCurrentAnimatorStateInfo(add).IsName(C.FireState + "_" + w)) inFire++;
                    fireR = Mathf.Max(fireR, Vector3.Distance(root.InverseTransformPoint(rh.position), rAim));
                }

                rows.Add("w=" + w + " overWeight=" + an.GetLayerWeight(over).ToString("F2", CultureInfo.InvariantCulture) + " addWeight=" + an.GetLayerWeight(add).ToString("F2", CultureInfo.InvariantCulture)
                         + " reloadFrames=" + inReload + "/" + frames + " handTravelR=" + (maxR * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm L=" + (maxL * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm"
                         + " fireFrames=" + inFire + " fireHandR=" + (fireR * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm aimR=" + rAim.ToString("F2") + " aimL=" + lAim.ToString("F2"));
                for (int i = 0; i < 20; i++) yield return null;
            }

            Debug.Log("[DD-TEST] wp032d.weaponlayer\n" + string.Join("\n", rows));
            Assert.AreEqual(3, rows.Count);
        }
    }
}
