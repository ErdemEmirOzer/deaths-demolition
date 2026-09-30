using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-071 single rest pose A/B (C-00 30.09): the same frames before (every file its own avatar rest) and after (one rest,
    /// AP022_player_fparms_v2) — FP ar_2 / smg_2 at rest and in ADS, and a side camera on the TP grip (smg_2 on the v3 path,
    /// ar_1 on the legacy class pose), in the default world (V6). Numbers per frame: the FP hand errors and the finger
    /// closure (index / thumb bend, degrees) of the hand in view, and the hand muscles pinned at their limit. A measurement, not a gate: run explicitly with
    /// DD_S06_AB_OUT=&lt;folder&gt; and DD_S06_AB_LABEL=before|after (env; no tools/test.sh flag, H11).
    /// </summary>
    [TestFixture]
    [Explicit("S-06 rest-pose A/B frames — run explicitly with DD_S06_AB_OUT=<folder>")]
    [Category("Diagnostics")]
    public sealed class AnimationRestAbTests
    {
        private const float SahneZamanAsimi = 60f, OyuncuZamanAsimi = 30f, YatismaZamanAsimi = 30f;
        private const int W = 1920, H = 1080;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private bool _armsBefore;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            _armsBefore = PlayerSkinnedVisual.FirstPersonV3Arms;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            PlayerSkinnedVisual.FirstPersonV3Arms = _armsBefore;   // the shipped default is untouched
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            UiDataPaths.UseProcessLocalRoot();   // never the real persistentDataPath
            RunFlowClock.ClearHolds();
            LogAssert.ignoreFailingMessages = false;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi) yield return null;
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator RestPoz_AB_Kareleri_FpAr2Smg2_TpTutus()
        {
            string output = Environment.GetEnvironmentVariable("DD_S06_AB_OUT");
            if (string.IsNullOrEmpty(output)) Assert.Fail("DD_S06_AB_OUT not set (explicit diagnostic — TANI-TESTI-POLITIKASI: no Ignore)");
            Directory.CreateDirectory(output);
            UiDataPaths.SetRootOverride(Path.Combine(output, "ui-sandbox"));
            PlayerSkinnedVisual.FirstPersonV3Arms = true;   // this test only
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "V6");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(WorldSourceSettings.V6SceneName).isLoaded, SahneZamanAsimi, "World V6");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;   // frames only
            StopSpawning();   // r6: a mobbed player hid the hands and the FP frames
            for (int i = 0; i < 30; i++) { StopSpawning(); yield return null; }
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            var json = new StringBuilder("{\n  \"schema\": \"wp071.rest-ab.v1\",\n  \"frames\": [");
            bool first = true;
            void Row(string frame, Animator a, FirstPersonArms fp)
            {
                json.Append(first ? "\n    " : ",\n    ").Append("{\"frame\": \"").Append(frame).Append(".png\"");
                if (fp != null) json.Append(", \"rightCm\": ").Append((fp.LastRightError * 100f).ToString("F2", Inv)).Append(", \"leftCm\": ").Append((fp.LastLeftError * 100f).ToString("F2", Inv));
                foreach (string side in new[] { "Right", "Left" })
                {
                    json.Append(", \"").Append(side.ToLowerInvariant()).Append("IndexBend\": ").Append(Bend(a, side, "Index").ToString("F1", Inv))
                        .Append(", \"").Append(side.ToLowerInvariant()).Append("ThumbBend\": ").Append(Bend(a, side, "Thumb").ToString("F1", Inv));
                }

                json.Append(", \"handMusclesAtLimit\": ").Append(MusclesAtLimit(a)).Append('}');
                first = false;
            }

            // TP grip: a side camera on the right hand (third person; no FP arms in this mode)
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            foreach (string key in new[] { "smg_2", "ar_1" })
            {
                var def = DD.Prototype.Core.Weapons.Arsenal.Get(key);
                pv.EquipWeapon(def, def.id);
                for (int i = 0; i < 40; i++) yield return null;
                yield return new WaitForEndOfFrame();
                SideCapture(pv.Animator, Path.Combine(output, "tp-" + key + ".png"));
                Row("tp-" + key, pv.Animator, null);
            }

            CameraModeService.Request(CameraMode.FirstPerson, instant: true);
            yield return CharacterTests.Bekle(() => CameraModeService.Current == CameraMode.FirstPerson, 5f, "FP kamera");   // r6: grabbed while still TP
            FirstPersonArms arms = pv.FirstPersonArms;
            try
            {
                foreach (string key in new[] { "ar_2", "smg_2" })
                {
                    var def = DD.Prototype.Core.Weapons.Arsenal.Get(key);
                    pv.EquipWeapon(def, def.id);
                    yield return CharacterTests.Bekle(() => arms.Visible && arms.FrameCalibrated, 5f, key + " kollar");
                    for (int i = 0; i < 20; i++) { StopSpawning(); yield return null; }
                    foreach (bool ads in new[] { false, true })
                    {
                        arms.SetAds(ads);
                        for (int i = 0; i < 25; i++) yield return null;
                        yield return new WaitForEndOfFrame();
                        string name = "fp-" + key + (ads ? "-ads" : "-rest");
                        CameraCapture(Camera.main, Path.Combine(output, name + ".png"));
                        Row(name, arms.Animator, arms);
                    }

                    arms.SetAds(false);
                }
            }
            finally
            {
                CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            }

            json.Append("\n  ],\n  \"variant\": \"").Append(Environment.GetEnvironmentVariable("DD_S06_AB_LABEL") ?? "").Append("\"\n}\n");
            File.WriteAllText(Path.Combine(output, "summary.json"), json.ToString());
            Debug.Log("[DD-TEST] wp071.rest-ab → " + output);
        }

        // Bend at the finger's middle joint + knuckle (degrees between consecutive segments; 0 = straight).
        private static float Bend(Animator a, string side, string finger)
        {
            if (a == null || !a.isHuman) return float.NaN;
            HumanBodyBones b1 = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + finger + "Proximal");
            HumanBodyBones b2 = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + finger + "Intermediate");
            HumanBodyBones b3 = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + finger + "Distal");
            Transform hand = a.GetBoneTransform(side == "Right" ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform t1 = a.GetBoneTransform(b1), t2 = a.GetBoneTransform(b2), t3 = a.GetBoneTransform(b3);
            if (hand == null || t1 == null || t2 == null || t3 == null) return float.NaN;
            return Vector3.Angle(t1.position - hand.position, t2.position - t1.position) + Vector3.Angle(t2.position - t1.position, t3.position - t2.position);
        }

        // Panel condition (C-00 30.09): finger / thumb / wrist muscles pinned at their limit (|m| ≥ 0.98) on this frame — a
        // clamped hand after the rest change shows up here before it shows in the pixels.
        private static int MusclesAtLimit(Animator a)
        {
            if (a == null || !a.isHuman || a.avatar == null) return -1;
            var handler = new HumanPoseHandler(a.avatar, a.transform);
            try
            {
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                int n = 0;
                for (int i = 0; i < pose.muscles.Length && i < HumanTrait.MuscleName.Length; i++)
                {
                    string m = HumanTrait.MuscleName[i];
                    bool hand = m.Contains("Thumb") || m.Contains("Index") || m.Contains("Middle") || m.Contains("Ring") || m.Contains("Little") || m.Contains("Hand");   // the wrist muscles are "Left/Right Hand Down-Up / In-Out"
                    if (hand && Mathf.Abs(pose.muscles[i]) >= 0.98f) n++;
                }

                return n;
            }
            finally
            {
                handler.Dispose();
            }
        }

        private static void StopSpawning()
        {
            DD.Prototype.Spawn.EnemySpawnDirector dir = DD.Prototype.Spawn.EnemySpawnDirector.Instance;
            if (dir == null) return;
            dir.ForceActiveTarget(0, false);
            dir.ClearAllEnemies();
        }

        private static void SideCapture(Animator a, string file)
        {
            Transform hand = a.GetBoneTransform(HumanBodyBones.RightHand), body = a.transform;
            var go = new GameObject("wp071-rest-ab-side");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.fieldOfView = 35f;
                cam.nearClipPlane = 0.05f;
                go.transform.position = hand.position + body.right * 1.1f + body.forward * 0.35f + Vector3.up * 0.1f;
                go.transform.LookAt(hand.position);
                CameraCapture(cam, file);
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
            }
        }

        private static void CameraCapture(Camera cam, string file)
        {
            Assert.IsNotNull(cam, "kamera");
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevTarget = cam.targetTexture, prev = RenderTexture.active;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            try
            {
                cam.targetTexture = rt;
                cam.Render();   // a URP base camera renders its stack (the FP viewmodel overlay) into its target
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                File.WriteAllBytes(file, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prev;
                UnityEngine.Object.Destroy(tex);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }
    }
}
