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
    /// WP-071 · B2 · locomotion v3 (K3, III A2): the cadence stays in the band and the rest slides; a crowd spawns out of
    /// step (per-spawn cadence spread + cycle phase); the spine leans into turns; the player's planted foot holds while the
    /// cadence is honest (foot lock).
    /// </summary>
    [TestFixture]
    [Category("WP071")]
    public sealed class AnimationLocomotionV3Tests
    {
        private const float SahneZamanAsimi = 60f;
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); FootIkAdapter.ResetStatics(); CharacterAnimatorLod.ResetStatics(); LocomotionLean.ResetStatics(); ZombieSkinnedVisual.GaitVariety = 1f; }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (GameObject go in _temp) if (go != null) Object.Destroy(go);
            _temp.Clear();
            FootIkAdapter.ResetStatics();
            CharacterAssetSet.ResetCache();
            yield return null;
        }

        private IEnumerator Stage()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(ground);
            ground.transform.localScale = new Vector3(120f, 0.2f, 120f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.layer = PrototypeLayers.Environment;
        }

        private ZombieSkinnedVisual Zombie(Vector3 at, uint seed)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var root = new GameObject("WP071_Zombie");
            _temp.Add(root);
            root.transform.SetPositionAndRotation(at, Quaternion.identity);
            GameObject go = Object.Instantiate(set.ZombiePrefab, root.transform, false);
            var v = go.GetComponent<ZombieSkinnedVisual>();
            v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, seed));
            v.ApplyMaterial(set.ZombieLibrary, 0);
            v.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return v;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zombi_KadansBandi_KalabalikFarkliFaz_Kayar()
        {
            yield return Stage();
            var zs = new List<ZombieSkinnedVisual>();
            for (int i = 0; i < 6; i++) zs.Add(Zombie(new Vector3(i * 2f, 0f, 0f), (uint)(40 + i)));
            var phases = new HashSet<int>();
            var muls = new HashSet<int>();
            foreach (ZombieSkinnedVisual z in zs)
            {
                phases.Add(Mathf.RoundToInt(Mathf.Repeat(z.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f) * 20f));
                muls.Add(Mathf.RoundToInt(z.GaitMultiplier * 100f));
                Assert.That(z.GaitMultiplier, Is.InRange(1f - ZombieSkinnedVisual.GaitSpread - 1e-4f, 1f + ZombieSkinnedVisual.GaitSpread + 1e-4f));
            }

            Time.captureDeltaTime = 1f / 60f;
            for (int f = 0; f < 90; f++) { foreach (ZombieSkinnedVisual z in zs) z.SetSpeed(2.7f, Time.deltaTime); yield return null; }
            var rows = new List<string>();
            foreach (ZombieSkinnedVisual z in zs)
            {
                float clip = z.AnimationSet.SpeedOf(C.ZombieGaitClips[z.Gait]);
                rows.Add("rate=" + z.GaitRate.ToString("F2", CultureInfo.InvariantCulture) + " slide=" + (CadenceBand.SlideFraction(2.7f, clip, z.GaitRate) * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%");
                Assert.That(z.GaitRate, Is.InRange(CadenceBand.Min - 1e-3f, CadenceBand.Max + 1e-3f), "kadans bandı");
            }

            Debug.Log("[DD-TEST] wp071.cadence phases=" + phases.Count + " multipliers=" + muls.Count + " | " + string.Join(" · ", rows));
            Assert.GreaterOrEqual(phases.Count, 4, "kalabalık aynı fazda yürüyor");
            Assert.GreaterOrEqual(muls.Count, 4, "kadans yayılımı yok");
        }

        /// <summary>
        /// WP-071 MUST-I2 (P-06 AD): the gait rate honours the body scale — the Executioner (1.48) at its 1.7 m/s walks the
        /// scaled stride (rate ≈ 1.7 / (clip × 1.48), inside the lock band) instead of the scale-1 cadence (×1.08, over it).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zombi_GovdeOlcegi_KadansOlcekliAdimla()
        {
            yield return Stage();
            ZombieSkinnedVisual.GaitVariety = 0f;
            const float speed = 1.7f, scale = 1.48f;
            ZombieSkinnedVisual big = Zombie(new Vector3(0f, 0f, 0f), 60u);
            ZombieSkinnedVisual unit = Zombie(new Vector3(4f, 0f, 0f), 60u);
            big.transform.parent.localScale = Vector3.one * scale;
            big.ResetForSpawn(EnemyTier.Ultra, speed, "A", 0, false);
            unit.ResetForSpawn(EnemyTier.Ultra, speed, "A", 0, false);
            Time.captureDeltaTime = 1f / 60f;
            for (int f = 0; f < 60; f++) { big.SetSpeed(speed, Time.deltaTime); unit.SetSpeed(speed, Time.deltaTime); yield return null; }
            float clip = big.AnimationSet.SpeedOf(C.ZombieGaitClips[big.Gait]);
            float honest = Mathf.Clamp(speed / (clip * scale), CadenceBand.Min, CadenceBand.Max);
            Debug.Log("[DD-TEST] wp071.bodyscale clip=" + clip.ToString("F2", CultureInfo.InvariantCulture) + " body=" + big.GaitBodyScale.ToString("F2", CultureInfo.InvariantCulture) + " rate=" + big.GaitRate.ToString("F2", CultureInfo.InvariantCulture) + " unscaled=" + unit.GaitRate.ToString("F2", CultureInfo.InvariantCulture) + " slide=" + (CadenceBand.SlideFraction(speed, clip * scale, big.GaitRate) * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%");
            Assert.AreEqual(scale, big.GaitBodyScale, 1e-3f, "gövde ölçeği okunmadı");
            Assert.AreEqual(1f, unit.GaitBodyScale, 1e-3f);
            Assert.AreEqual(honest, big.GaitRate, 1e-3f, "kadans ölçekli adıma göre değil");
            Assert.LessOrEqual(big.GaitRate, CadenceBand.LockMaxRate, "ölçekli beden ayak kilidi bandında");
            Assert.Greater(unit.GaitRate, big.GaitRate * 1.3f, "ölçek 1 beden daha hızlı kadans (kontrol)");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zombi_DonusteIceEgilir_DuzdeDik()
        {
            yield return Stage();
            ZombieSkinnedVisual z = Zombie(new Vector3(4f, 0f, 0f), 50u);
            Transform root = z.transform.parent;
            Assert.IsNotNull(z.Lean, "LocomotionLean bağlı değil");
            Time.captureDeltaTime = 1f / 60f;
            const float r = 4f, speed = 2.7f;
            float theta = 0f;
            for (int f = 0; f < 120; f++)
            {
                theta += speed / r * Time.deltaTime;   // counter-clockwise from above = a LEFT turn
                root.position = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta)) * r;
                root.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(theta), 0f, Mathf.Cos(theta)));
                z.SetSpeed(speed, Time.deltaTime);
                yield return null;
            }

            float turnRoll = z.Lean.Roll;
            Vector3 dir = root.forward;
            for (int f = 0; f < 120; f++)
            {
                root.position += dir * speed * Time.deltaTime;
                z.SetSpeed(speed, Time.deltaTime);
                yield return null;
            }

            float straightRoll = z.Lean.Roll;
            Debug.Log("[DD-TEST] wp071.lean turnRoll=" + turnRoll.ToString("F1", CultureInfo.InvariantCulture) + "° straightRoll=" + straightRoll.ToString("F1", CultureInfo.InvariantCulture) + "° pitch=" + z.Lean.Pitch.ToString("F1", CultureInfo.InvariantCulture));
            Assert.Greater(turnRoll, 1.5f, "sola dönüşte sola (içe) eğilmeli");
            Assert.Less(Mathf.Abs(straightRoll), 1f, "düz yürüyüşte dik");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_YuruyusteBasanAyakKilitli_KaymaAz()
        {
            yield return Stage();
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP071_PlayerStage");
            _temp.Add(stage);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(stage.transform, false);
            GameObject go = Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            Vector3 velocity = Vector3.zero;
            pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
            pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Assert.IsNotNull(pv.FootIk, "FootIkAdapter yok");
            float walk = pv.AnimationSet.SpeedOf(C.PlayerWalkFwd);
            Time.captureDeltaTime = 1f / 60f;
            velocity = Vector3.forward * walk;
            Transform lf = pv.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            int locked = 0, frames = 0, gateOpen = 0;
            bool was = false;
            Vector3 start = Vector3.zero;
            var drifts = new List<float>();
            float drift = 0f;
            for (int f = 0; f < 240; f++)
            {
                stage.transform.position += velocity * Time.deltaTime;
                yield return null;
                if (f < 60) continue;
                frames++;
                bool now = pv.FootIk.IsLocked(0);
                if (pv.FootIk.LockGateOpen) gateOpen++;
                if (now) locked++;
                if (now && !was) { start = lf.position; drift = 0f; }
                if (now) { Vector3 d = lf.position - start; d.y = 0f; drift = Mathf.Max(drift, d.magnitude); }
                if (!now && was) drifts.Add(drift);
                was = now;
            }

            drifts.Sort();
            float median = drifts.Count > 0 ? drifts[drifts.Count / 2] : float.NaN;
            Debug.Log("[DD-TEST] wp071.footlock walk=" + walk.ToString("F2", CultureInfo.InvariantCulture) + " m/s lockedFrames=" + locked + "/" + frames + " episodes=" + drifts.Count + " medianDrift=" + (median * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm gate=" + gateOpen + " served=" + pv.FootIk.Served);
            Assert.GreaterOrEqual(drifts.Count, 3, "ayak kilidi devreye girmedi");
            Assert.LessOrEqual(median, 0.03f, "kilitli ayak kaydı > 3 cm");
        }

        // R1-B AI2 (C-00 30.09): the stand-in for the Animator — rewrites the spine every frame before LateUpdate while "animating".
        private sealed class SpineWriter : MonoBehaviour
        {
            public Transform Spine;
            public bool Writing = true;
            private void Update() { if (Writing && Spine != null) Spine.localRotation = Quaternion.identity; }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Lean_KapiKapaninca_OmurgayaBilesikUygulanmaz()
        {
            // R1-B AI2: after the ragdoll hand-off the Animator stops writing the spine; a decaying lean multiplied onto the
            // unrefreshed bone compounded (≈10× at 60 fps). With the gate closed the bone must stay where it is.
            var body = new GameObject("WP071_LeanBody");
            _temp.Add(body);
            var spine = new GameObject("Spine").transform;
            spine.SetParent(body.transform, false);
            var writer = body.AddComponent<SpineWriter>();
            writer.Spine = spine;
            bool gate = true;
            var lean = body.AddComponent<LocomotionLean>();
            lean.Configure(spine, body.transform, () => gate);
            Time.captureDeltaTime = 1f / 60f;
            for (int f = 0; f < 40; f++) { lean.Feed(Vector3.forward * (f * 0.2f)); yield return null; }   // 12 m/s² forward
            Assert.Greater(lean.Pitch, 2f, "ivmede eğilme oluşmadı (test kurulumu)");
            gate = false; writer.Writing = false;   // ragdoll hand-off: gate closes, nothing rewrites the spine
            yield return null;
            Quaternion held = spine.rotation;
            for (int f = 0; f < 60; f++) yield return null;
            float drift = Quaternion.Angle(held, spine.rotation);
            Debug.Log("[DD-TEST] r1b.ai2 spineDrift=" + drift.ToString("F2", CultureInfo.InvariantCulture) + "° pitch=" + lean.Pitch.ToString("F2", CultureInfo.InvariantCulture));
            Assert.Less(drift, 0.1f, "kapı kapalıyken eğilme omurgaya bileşik uygulandı");
            Assert.AreEqual(0f, lean.Pitch, "kapı kapalı → eğilme sıfır");
        }
    }
}
