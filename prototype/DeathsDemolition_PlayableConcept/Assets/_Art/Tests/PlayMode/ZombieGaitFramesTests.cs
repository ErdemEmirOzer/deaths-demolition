using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;
using Object = UnityEngine.Object;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-071 MUST-I1 (C-00 01.10) frames: Standard / Guard / Executioner at their tier scale and speed, walking side-on — before
    /// (the AP-009 fallback shamble the tiers shared) and after (the tier's own AP-022 gait, ZombieSkinnedVisual.GaitIndexFor), three
    /// phases each, with the gait rate and the body scale. A measurement: explicit, writes PNGs + a summary to DD_S06_GAIT_OUT.
    /// </summary>
    [TestFixture]
    [Explicit("S-06 diagnostic — zombie tier gait before / after frames (DD_S06_GAIT_OUT)")]
    [Category("Diagnostics")]
    public sealed class ZombieGaitFramesTests
    {
        private const int W = 960, H = 720;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); FootIkAdapter.ResetStatics(); ZombieSkinnedVisual.GaitVariety = 0f; }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            ZombieSkinnedVisual.GaitVariety = 1f;
            foreach (GameObject go in _temp) if (go != null) Object.Destroy(go);
            _temp.Clear();
            CharacterAssetSet.ResetCache();
            yield return null;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Tani_ZombiTierYurume_OncesiSonrasi()
        {
            string output = Environment.GetEnvironmentVariable("DD_S06_GAIT_OUT");
            if (string.IsNullOrEmpty(output)) Assert.Fail("DD_S06_GAIT_OUT not set (explicit diagnostic — TANI-TESTI-POLITIKASI: no Ignore)");
            string label = Environment.GetEnvironmentVariable("DD_S06_GAIT_LABEL") ?? "run";
            output = Path.Combine(output, label);
            Directory.CreateDirectory(output);
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, 60f, "MainMenu");
            yield return null;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(ground);
            ground.transform.localScale = new Vector3(400f, 0.2f, 40f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.layer = PrototypeLayers.Environment;
            var lightGo = new GameObject("WP071_GaitLight");
            _temp.Add(lightGo);
            Light sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var camGo = new GameObject("WP071_GaitCam");
            _temp.Add(camGo);
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 32f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.58f, 0.62f);
            var rt = new RenderTexture(W, H, 24);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var config = new EnemyConfigSection();
            CharacterAssetSet set = CharacterAssetSet.Load();
            CharacterAnimationSet anim = set.ZombieAnimation;
            var summary = new List<string>();
            (EnemyTier tier, string name, float scale)[] tiers = { (EnemyTier.Standard, "standard", 1f), (EnemyTier.Elite, "guard", 1.16f), (EnemyTier.Ultra, "executioner", 1.48f) };
            float lane = 0f;
            foreach ((EnemyTier tier, string name, float scale) t in tiers)
            {
                float speed = config.Find(t.tier).MoveSpeed;
                foreach (bool after in new[] { false, true })
                {
                    var root = new GameObject("WP071_Gait_" + t.name + (after ? "_after" : "_before"));
                    _temp.Add(root);
                    root.transform.SetPositionAndRotation(new Vector3(-60f, 0f, lane), Quaternion.LookRotation(Vector3.right));
                    lane += 6f;
                    root.transform.localScale = Vector3.one * t.scale;
                    GameObject go = Object.Instantiate(set.ZombiePrefab, root.transform, false);
                    var v = go.GetComponent<ZombieSkinnedVisual>();
                    v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, 70u));
                    v.ApplyMaterial(set.ZombieLibrary, 0);
                    v.ResetForSpawn(t.tier, speed, "A", 0, false);
                    int gait = after ? ZombieSkinnedVisual.GaitIndexFor(t.tier, "A", anim) : ZombieSkinnedVisual.GaitIndexFor(t.tier, "A", null);
                    v.ForceGaitForTest(gait);
                    v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Time.captureDeltaTime = 1f / 60f;
                    for (int f = 0; f < 150; f++)
                    {
                        root.transform.position += Vector3.right * speed * Time.deltaTime;
                        v.SetSpeed(speed, Time.deltaTime);
                        yield return null;
                        if (f >= 90 && (f - 90) % 12 == 0)
                        {
                            Vector3 at = root.transform.position + Vector3.up * (0.9f * t.scale);
                            cam.transform.position = at + new Vector3(0f, 0.25f * t.scale, -5.2f * t.scale);
                            cam.transform.LookAt(at);
                            cam.targetTexture = rt;
                            cam.Render();
                            RenderTexture prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                            tex.Apply();
                            RenderTexture.active = prev;
                            cam.targetTexture = null;
                            string file = "gait-" + t.name + "-" + (after ? "after" : "before") + "-p" + ((f - 90) / 12) + ".png";
                            File.WriteAllBytes(Path.Combine(output, file), tex.EncodeToPNG());
                        }
                    }

                    string line = t.name + " " + (after ? "after " : "before") + " clip=" + C.ZombieGaitClips[v.Gait] + " speed=" + speed.ToString("F2", Inv) + " scale=" + t.scale.ToString("F2", Inv)
                                  + " clipSpeed=" + anim.SpeedOf(C.ZombieGaitClips[v.Gait]).ToString("F2", Inv) + " rate=" + v.GaitRate.ToString("F2", Inv)
                                  + " slide=" + (CadenceBand.SlideFraction(speed, anim.SpeedOf(C.ZombieGaitClips[v.Gait]) * t.scale, v.GaitRate) * 100f).ToString("F0", Inv) + "%";
                    summary.Add(line);
                    Debug.Log("[DD-TEST] wp071.gaitframes " + line);
                    Object.Destroy(root);
                    yield return null;
                }
            }

            // ---------------- TP player locomotion (C-00 01.10): walk / walk strafe / run / run strafe / run back at fixed speeds — the
            // label (DD_S06_GAIT_LABEL) tells the tree: before = AP-009 loops + Lerp LocoRate, after = AP-022 loops + MUST-3/4
            (string name, Vector3 dir, float speed)[] moves =
            {
                ("walk_fwd", Vector3.forward, 2.6f), ("walk_strafe_r", Vector3.right, 2.1f), ("run_fwd", Vector3.forward, 6.2f), ("run_strafe_r", Vector3.right, 6.2f), ("run_back", Vector3.back, 6.2f)
            };
            foreach ((string name, Vector3 dir, float speed) mv in moves)
            {
                var stage = new GameObject("WP071_PlayerFrames_" + mv.name);
                _temp.Add(stage);
                stage.transform.position = new Vector3(-60f, 0f, lane);
                lane += 6f;
                Transform visual = new GameObject("Visual").transform;
                visual.SetParent(stage.transform, false);
                GameObject go = Object.Instantiate(set.PlayerPrefab, visual, false);
                var pv = go.GetComponent<PlayerSkinnedVisual>();
                Vector3 velocity = Vector3.zero;
                pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
                pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Time.captureDeltaTime = 1f / 60f;
                velocity = mv.dir * mv.speed;
                for (int f = 0; f < 150; f++)
                {
                    stage.transform.position += velocity * Time.deltaTime;
                    yield return null;
                    if (f >= 90 && (f - 90) % 12 == 0)
                    {
                        Vector3 at = stage.transform.position + Vector3.up * 0.9f;
                        Vector3 side = Mathf.Abs(mv.dir.x) > 0.5f ? Vector3.forward : Vector3.right;   // strafes from the front, fwd / back from the side
                        cam.transform.position = at + Vector3.up * 0.25f + side * 5.2f;
                        cam.transform.LookAt(at);
                        cam.targetTexture = rt;
                        cam.Render();
                        RenderTexture prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                        tex.Apply();
                        RenderTexture.active = prev;
                        cam.targetTexture = null;
                        File.WriteAllBytes(Path.Combine(output, "player-" + mv.name + "-" + label + "-p" + ((f - 90) / 12) + ".png"), tex.EncodeToPNG());
                    }
                }

                string line = "player " + mv.name + " " + label + " speed=" + mv.speed.ToString("F2", Inv) + " locoRate=" + pv.LocoRate.ToString("F3", Inv)
                              + " walkClip=" + C.PlayerWalkFwd + " (" + pv.AnimationSet.SpeedOf(C.PlayerWalkFwd).ToString("F2", Inv) + ") runClip=" + C.PlayerRunFwd + " (" + pv.AnimationSet.SpeedOf(C.PlayerRunFwd).ToString("F2", Inv) + ")";
                summary.Add(line);
                Debug.Log("[DD-TEST] wp071.gaitframes " + line);
                Object.Destroy(stage);
                yield return null;
            }

            Object.Destroy(rt);
            File.WriteAllText(Path.Combine(output, "summary-" + label + ".txt"), string.Join("\n", summary) + "\n");
            Assert.AreEqual(11, summary.Count);
        }
    }
}
