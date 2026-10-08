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
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); FootIkAdapter.ResetStatics(); CharacterAnimatorLod.ResetStatics(); LocomotionLean.ResetStatics(); WeaponHoldIk.ResetStatics(); ZombieSkinnedVisual.GaitVariety = 1f; }

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
            // WP-096 P2: the per-spawn variety is the MOVE SPEED jitter (the cadence multiplier is retired, GaitMultiplier ≡ 1); the
            // ordinal restarts so the six bodies are ordinals 0..5 whatever ran before (three antithetic pairs, six distinct speeds)
            ZombieSkinnedVisual.ResetGaitOrdinalForTests();
            var zs = new List<ZombieSkinnedVisual>();
            for (int i = 0; i < 6; i++) zs.Add(Zombie(new Vector3(i * 2f, 0f, 0f), (uint)(40 + i)));
            var phases = new HashSet<int>();
            var muls = new HashSet<int>();
            foreach (ZombieSkinnedVisual z in zs)
            {
                phases.Add(Mathf.RoundToInt(Mathf.Repeat(z.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f) * 20f));
                muls.Add(Mathf.RoundToInt(z.SpeedJitter * 1000f));
                Assert.AreEqual(1f, z.GaitMultiplier, 1e-6f, "WP-096 P2: kadans çarpanı emekli (oran dürüst)");
                Assert.That(z.SpeedJitter, Is.InRange(1f - ZombieLocomotionMath.SpeedSpread - 1e-4f, 1f + ZombieLocomotionMath.SpeedSpread + 1e-4f));
            }

            Time.captureDeltaTime = 1f / 60f;
            for (int f = 0; f < 90; f++) { foreach (ZombieSkinnedVisual z in zs) z.SetSpeed(2.7f, Time.deltaTime); yield return null; }
            var rows = new List<string>();
            foreach (ZombieSkinnedVisual z in zs)
            {
                float clip = z.AnimationSet.SpeedOf(C.ZombieGaitClips[z.Gait]);
                rows.Add("rate=" + z.GaitRate.ToString("F2", CultureInfo.InvariantCulture) + " slide=" + (CadenceBand.SlideFraction(2.7f, clip, z.GaitRate) * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%");
                Assert.That(z.GaitRate, Is.InRange(z.GaitRateMin - 1e-3f, z.GaitRateMax + 1e-3f), "kadans bandı (klibin kendi bandı, WP-096 P3)");
            }

            Debug.Log("[DD-TEST] wp071.cadence phases=" + phases.Count + " multipliers=" + muls.Count + " (WP-096: speedJitter) | " + string.Join(" · ", rows));
            Assert.GreaterOrEqual(phases.Count, 4, "kalabalık aynı fazda yürüyor");
            Assert.GreaterOrEqual(muls.Count, 4, "hız yayılımı yok (WP-096 P2)");
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
            // WP-096 P3: the clip's own band — walk_stomp's floor 0.6 leaves the honest ×0.676 unclamped (was 0.75, slide 11 %)
            float honest = Mathf.Clamp(speed / (clip * scale), big.GaitRateMin, big.GaitRateMax);
            Debug.Log("[DD-TEST] wp071.bodyscale clip=" + clip.ToString("F2", CultureInfo.InvariantCulture) + " body=" + big.GaitBodyScale.ToString("F2", CultureInfo.InvariantCulture) + " rate=" + big.GaitRate.ToString("F2", CultureInfo.InvariantCulture) + " unscaled=" + unit.GaitRate.ToString("F2", CultureInfo.InvariantCulture) + " slide=" + (CadenceBand.SlideFraction(speed, clip * scale, big.GaitRate) * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%");
            Assert.AreEqual(scale, big.GaitBodyScale, 1e-3f, "gövde ölçeği okunmadı");
            Assert.AreEqual(1f, unit.GaitBodyScale, 1e-3f);
            Assert.AreEqual(honest, big.GaitRate, 1e-3f, "kadans ölçekli adıma göre değil");
            if (C.ZombieGaitClips[big.Gait] == C.ZombieWalkStompV3)
            {
                Assert.AreEqual(speed / (clip * scale), big.GaitRate, 1e-3f, "WP-096 P3: walk_stomp dürüst oranı kırpılmamalı");
                Assert.IsTrue(big.GaitRateHonest, "WP-096 P3: dürüst oran bayrağı");
            }
            // WP-096 AP-033 p1: GaitVariety 0 → both bodies on the Executioner's primary walk_stomp_A (native 1.15 = 1.7 / 1.48): rate ≈ 1
            Assert.AreEqual(big.Gait, unit.Gait, "iki beden aynı klipte (varyete 0 → birincil)");
            if (C.ZombieGaitClips[big.Gait] == C.ZombieWalkStompAV4)
            {
                // WP-096 p2 window: the set speed is the intake's Unity-rig measurement (no longer the Blender 1.15), so the rate is ≈ 1 ± the retarget share
                Assert.That(big.GaitRate, Is.InRange(0.85f, 1.25f), "WP-096 AP-033: walk_stomp_A ölçekli bedende oran ≈ 1");
                Assert.IsTrue(big.GaitRateHonest, "WP-096 AP-033: dürüst oran");
            }
            // the zombie foot lock's own gate (ZombieSkinnedVisual.FootLockMaxRate 1.25, WP-096 P3), not the player's CadenceBand.LockMaxRate
            Assert.LessOrEqual(big.GaitRate, ZombieSkinnedVisual.FootLockMaxRate, "ölçekli beden ayak kilidi bandında");
            // control: the scale-1 body's honest cadence is 1.48× the scaled one. WP-096 AP-033: over walk_stomp_A that is ×1.48, past the
            // band cap 1.2 — the PLAYED rate is capped (the scale-1 Executioner never ships), so the control compares the honest rate
            float unitClip = unit.AnimationSet.SpeedOf(C.ZombieGaitClips[unit.Gait]);
            float unitHonest = speed / unitClip;
            Assert.AreEqual(Mathf.Clamp(unitHonest, unit.GaitRateMin, unit.GaitRateMax), unit.GaitRate, 1e-3f, "ölçek 1 beden: dürüst oran, bantla kırpılmış");
            Assert.Greater(unitHonest, big.GaitRate * 1.3f, "ölçek 1 beden daha hızlı kadans (kontrol)");
            Assert.Greater(unit.GaitRate, big.GaitRate, "ölçek 1 beden oynatılan oran da yüksek");
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

        /// <summary>
        /// WP-071 MUST-3 / MUST-4 (P-06 AD refuter B, AP-022 player locomotion): at the walk ring (2.6 m/s fwd) the walk clip plays at its
        /// authored rate (LocoRate × child time scale ≈ 1, not the old 1.80×); at 6.2 m/s the run_fwd plays at 6.2 / 5.8; standing for 0.2 s
        /// the base layer is in Idle_Breath; the foot-lock gate is open on the walk ring and shut on run_back at 6.2 m/s (30 % slide).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_YuruyusHalkasi_KendiHizinda_DururkenNefes_KilitBaskinCocukta()
        {
            yield return Stage();
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP071_PlayerRate");
            _temp.Add(stage);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(stage.transform, false);
            GameObject go = Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            Vector3 velocity = Vector3.zero;
            pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
            pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            CharacterAnimationSet anim = pv.AnimationSet;
            float reference = anim.LocomotionReferenceSpeed;
            float walk = anim.SpeedOf(C.PlayerWalkFwd), run = anim.SpeedOf(C.PlayerRunFwd);
            Time.captureDeltaTime = 1f / 60f;

            velocity = Vector3.forward * walk;
            for (int f = 0; f < 60; f++) { stage.transform.position += velocity * Time.deltaTime; yield return null; }
            float walkClipRate = pv.LocoRate * PlayerSkinnedVisual.ChildTimeScale(reference, walk);
            bool walkGate = pv.FootIk.LockGateOpen;

            velocity = Vector3.forward * reference;
            for (int f = 0; f < 60; f++) { stage.transform.position += velocity * Time.deltaTime; yield return null; }
            float runClipRate = pv.LocoRate * PlayerSkinnedVisual.RunChildTimeScale(reference, run);

            // D-1 (C-00 01.10, user: backward ×0.65): the backward top speed; run_back's child sits at that radius → clip rate 4.03 / 3.6 = 1.12
            float backMul = DD.Prototype.Core.Config.PlayerConfigSection.BackwardSpeedMultiplier;
            velocity = Vector3.back * reference * backMul;
            for (int f = 0; f < 60; f++) { stage.transform.position += velocity * Time.deltaTime; yield return null; }
            bool backGate = pv.FootIk.LockGateOpen;
            float back = anim.SpeedOf(C.PlayerRunBack);
            float backRate = backMul * PlayerSkinnedVisual.RunChildTimeScale(reference, back, backMul);
            float backSlide = CadenceBand.SlideFraction(reference * backMul, back, backRate);

            velocity = Vector3.zero;
            for (int f = 0; f < 40; f++) yield return null;
            bool breath = pv.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.StillState) || pv.Animator.GetNextAnimatorStateInfo(0).IsName(C.StillState);
            Debug.Log("[DD-TEST] wp071.locorate walk=" + walk.ToString("F2", CultureInfo.InvariantCulture) + " walkClipRate=" + walkClipRate.ToString("F3", CultureInfo.InvariantCulture)
                      + " run=" + run.ToString("F2", CultureInfo.InvariantCulture) + " runClipRate=" + runClipRate.ToString("F3", CultureInfo.InvariantCulture)
                      + " walkGate=" + walkGate + " backGate=" + backGate + " backRate=" + backRate.ToString("F2", CultureInfo.InvariantCulture) + " backSlide=" + (backSlide * 100f).ToString("F0", CultureInfo.InvariantCulture) + "% still=" + pv.StillRequested + " breathState=" + breath);
            Assert.AreEqual(1f, walkClipRate, 0.05f, "yürüyüş halkası kendi hızında (MUST-3; eski 1,80×)");
            Assert.AreEqual(reference / run, runClipRate, 0.03f, "koşu 6,2 / spd");
            Assert.IsTrue(walkGate, "yürüyüş halkasında ayak kilidi açık");
            Assert.LessOrEqual(backSlide, CadenceBand.SlideClose, "run_back geri azami hızda kayma ≤ %20 (D-1)");
            Assert.AreEqual(backRate <= CadenceBand.LockMaxRate && backSlide <= PlayerSkinnedVisual.LockMaxSlide, backGate, "kilit kapısı baskın çocuğun oranı/kaymasıyla (MUST-4 e)");
            Assert.IsTrue(pv.StillRequested && breath, "dururken Idle_Breath (MUST-3)");
        }

        /// <summary>
        /// WP-082 / WP-083 B2b (C-00 05.10, S-02 magazine contract v1): the player's left hand carries socket_mag_l at the palm centre
        /// (wrist → middle-finger base, half-way) and it follows the hand through the animated pose.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_SolEl_SarjorSoketi_AvucOrtasinda_EliIzler()
        {
            yield return Stage();
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP083_MagSocket");
            _temp.Add(stage);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(stage.transform, false);
            GameObject go = Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            Vector3 velocity = Vector3.zero;
            pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
            pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Transform socket = pv.MagazineSocket;
            Transform hand = pv.Animator.GetBoneTransform(HumanBodyBones.LeftHand), middle = pv.Animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            Assert.IsNotNull(socket, "socket_mag_l yok");
            Assert.AreEqual(MagazineSocket.Name, socket.name);
            Assert.AreSame(hand, socket.parent, "socket_mag_l sol elin çocuğu olmalı");
            Assert.AreSame(socket, MagazineSocket.Ensure(pv.Animator), "ikinci çağrı aynı soketi döndürmeli (çift soket yok)");
            Time.captureDeltaTime = 1f / 60f;
            velocity = Vector3.forward * 3f;
            float maxErr = 0f;
            for (int f = 0; f < 60; f++)
            {
                stage.transform.position += velocity * Time.deltaTime;
                yield return null;
                Vector3 palm = Vector3.Lerp(hand.position, middle.position, MagazineSocket.PalmShare);
                maxErr = Mathf.Max(maxErr, Vector3.Distance(palm, socket.position));
            }

            float toWrist = Vector3.Distance(socket.position, hand.position);
            Debug.Log("[DD-TEST] wp083.magsocket local=" + socket.localPosition.ToString("F3") + " toWrist=" + (toWrist * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm palmErrMax=" + (maxErr * 100f).ToString("F2", CultureInfo.InvariantCulture) + " cm");
            Assert.Less(maxErr, 0.005f, "soket koşuda avuç ortasından ayrıldı (> 5 mm)");
            Assert.That(toWrist, Is.InRange(0.02f, 0.10f), "avuç ortası bilekten 2–10 cm");
        }

        /// <summary>
        /// WP-083 B2c (C-00 05.10, decision A): the weapon-first hold. Off (the default) adds nothing to the body. On, the right hand's
        /// socket_weapon_r sits on the grip pivot (≤ 1 cm, ≤ 2° — H1's limit) and the pivot faces the body's facing pitched by the aim
        /// (≤ 1°), standing and
        /// walking. The left hand reaches the weapon's grip_l through WeaponGripIk on the placed weapon (≤ 1 cm). Switching the hold off
        /// fades it to 0 within the blend time and the hand returns to the clip.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_SilahTutusu_SoketPivotta_SolElKabzada_NisanYonunde()
        {
            yield return Stage();
            CharacterAssetSet set = CharacterAssetSet.Load();
            try
            {
                WeaponHoldIk.Enabled = false;
                var offStage = new GameObject("WP083_HoldOff");
                _temp.Add(offStage);
                Transform offVisual = new GameObject("Visual").transform;
                offVisual.SetParent(offStage.transform, false);
                var offPv = Object.Instantiate(set.PlayerPrefab, offVisual, false).GetComponent<PlayerSkinnedVisual>();
                offPv.Attach(offVisual, () => Vector3.zero, () => 6.2f, () => Vector3.forward);
                Assert.IsNull(offPv.HoldIk, "kapalıyken WeaponHoldIk eklenmemeli");
                Object.Destroy(offStage);

                WeaponHoldIk.Enabled = true;
                var stage = new GameObject("WP083_Hold");
                _temp.Add(stage);
                Transform visual = new GameObject("Visual").transform;
                visual.SetParent(stage.transform, false);
                var pv = Object.Instantiate(set.PlayerPrefab, visual, false).GetComponent<PlayerSkinnedVisual>();
                Vector3 velocity = Vector3.zero;
                Vector3 aim = new Vector3(0.25f, 0.15f, 1f).normalized;
                pv.Attach(visual, () => velocity, () => 6.2f, () => aim);
                pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                WeaponHoldIk hold = pv.HoldIk;
                Assert.IsNotNull(hold, "açıkken WeaponHoldIk eklenmeli");
                Assert.IsTrue(hold.Ready, "tutuş kemikleri/soketi bulunamadı");
                Transform socket = pv.WeaponSocket, lHand = pv.Animator.GetBoneTransform(HumanBodyBones.LeftHand);
                // a weapon's support point: grip_l on the socket (the weapon frame), where a rifle's handguard is
                Transform grip = new GameObject("grip_l").transform;
                grip.SetParent(socket, false);
                grip.localPosition = new Vector3(-0.02f, 0.02f, 0.18f);

                Time.captureDeltaTime = 1f / 60f;
                float maxR = 0f, maxRot = 0f, maxAim = 0f, maxL = 0f;
                int measured = 0, leftMeasured = 0;
                for (int f = 0; f < 120; f++)
                {
                    if (f == 40) velocity = Vector3.forward * 3f;
                    stage.transform.position += velocity * Time.deltaTime;
                    yield return null;
                    if (hold.Weight < 1f) continue;
                    measured++;
                    maxR = Mathf.Max(maxR, Vector3.Distance(socket.position, hold.PivotPosition));
                    maxRot = Mathf.Max(maxRot, Quaternion.Angle(socket.rotation, hold.PivotRotation));
                    // the hold faces the body's facing pitched by the aim's elevation (WeaponMountAlignment's rest frame)
                    Vector3 expected = Quaternion.LookRotation(Vector3.ProjectOnPlane(pv.Animator.transform.forward, Vector3.up).normalized) * Quaternion.Euler(-Mathf.Asin(aim.y) * Mathf.Rad2Deg, 0f, 0f) * Vector3.forward;
                    maxAim = Mathf.Max(maxAim, Vector3.Angle(hold.PivotRotation * Vector3.forward, expected));
                    if (pv.GripIk != null && pv.GripIk.Weight >= 1f) { maxL = Mathf.Max(maxL, Vector3.Distance(lHand.position, grip.position)); leftMeasured++; }
                }

                Debug.Log("[DD-TEST] wp083.hold measured=" + measured + " socketPivotMax=" + (maxR * 100f).ToString("F2", CultureInfo.InvariantCulture) + " cm rotMax=" +
                          maxRot.ToString("F2", CultureInfo.InvariantCulture) + "° aimMax=" + maxAim.ToString("F2", CultureInfo.InvariantCulture) + "° left(n=" + leftMeasured + ")Max=" +
                          (maxL * 100f).ToString("F2", CultureInfo.InvariantCulture) + " cm");
                Assert.GreaterOrEqual(measured, 90, "tutuş ağırlığı 1'e çıkmadı (kapı: dinlenme pozu)");
                Assert.LessOrEqual(maxR, 0.01f, "socket_weapon_r pivottan > 1 cm");
                Assert.LessOrEqual(maxRot, 2f, "socket_weapon_r dönüşü pivottan > 2°");
                Assert.LessOrEqual(maxAim, 1f, "pivot gövde yönü + nişan eğiminden > 1°");
                Assert.GreaterOrEqual(leftMeasured, 60, "sol el kabza IK'sı tam ağırlıkta değil");
                Assert.LessOrEqual(maxL, 0.01f, "sol el kabzadan > 1 cm");

                WeaponHoldIk.Enabled = false;
                int fade = Mathf.CeilToInt(WeaponHoldIk.BlendSeconds * 60f) + 2;
                for (int f = 0; f < fade; f++) yield return null;
                Assert.AreEqual(0f, hold.Weight, "kapatınca ağırlık karışım süresinde 0'a inmeli");
                Assert.IsFalse(hold.Active);
                Assert.Greater(Vector3.Distance(socket.position, hold.PivotPosition), 0.02f, "kapalıyken el klibe dönmeli (pivotta kalmamalı)");
            }
            finally
            {
                WeaponHoldIk.ResetStatics();
            }
        }

        /// <summary>
        /// WP-083 B2e (C-00 06.10, council finding): the hold is the classic weapons'. A Special in the hand (SwHold) fades it out and
        /// back to the clip; the classic weapon back in the hand brings it in again. A hidden weapon's markers under the same socket
        /// (the classic rig while a Special is out) are never the held weapon's: with an inactive attach_stock / Muzzle under the socket
        /// and the shouldered row selected, the pivot does not pop to the shoulder (frame-to-frame ≤ 1 cm, standing).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_SilahTutusu_SpecialEldeyken_Kapali_GizliSilahIsaretiSayilmaz()
        {
            yield return Stage();
            CharacterAssetSet set = CharacterAssetSet.Load();
            try
            {
                WeaponHoldIk.Enabled = true;
                var stage = new GameObject("WP083_HoldSpecial");
                _temp.Add(stage);
                Transform visual = new GameObject("Visual").transform;
                visual.SetParent(stage.transform, false);
                var pv = Object.Instantiate(set.PlayerPrefab, visual, false).GetComponent<PlayerSkinnedVisual>();
                pv.Attach(visual, () => Vector3.zero, () => 6.2f, () => Vector3.forward);
                pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                WeaponHoldIk hold = pv.HoldIk;
                Assert.IsNotNull(hold, "açıkken WeaponHoldIk eklenmeli");
                Transform socket = pv.WeaponSocket;
                Transform grip = new GameObject("grip_l").transform;
                grip.SetParent(socket, false);
                grip.localPosition = new Vector3(-0.02f, 0.02f, 0.18f);
                // a hidden weapon under the same socket: its stock behind the grip, its muzzle ahead (rl_1-like tube)
                var hidden = new GameObject("HiddenWeapon");
                hidden.transform.SetParent(socket, false);
                new GameObject("attach_stock").transform.SetParent(hidden.transform, false);
                hidden.transform.GetChild(0).localPosition = new Vector3(0f, 0.05f, -0.45f);
                Transform muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(hidden.transform, false);
                muzzle.localPosition = new Vector3(0f, 0.05f, 0.6f);
                hidden.SetActive(false);
                hold.SetWeapon(ClassicWeaponId.Rl1);

                Time.captureDeltaTime = 1f / 60f;
                for (int f = 0; f < 60; f++) yield return null;
                Assert.AreEqual(1f, hold.Weight, "klasik elde tutuş tam ağırlıkta olmalı");
                Vector3 last = hold.PivotPosition;
                float maxJump = 0f;
                for (int f = 0; f < 60; f++)
                {
                    yield return null;
                    maxJump = Mathf.Max(maxJump, Vector3.Distance(hold.PivotPosition, last));
                    last = hold.PivotPosition;
                }

                pv.SetSpecialHold(1);
                int fade = Mathf.CeilToInt(WeaponHoldIk.BlendSeconds * 60f) + 2;
                for (int f = 0; f < fade; f++) yield return null;
                float specialWeight = hold.Weight;
                bool specialActive = hold.Active;
                for (int f = 0; f < 30; f++) yield return null;
                float specialWeightLater = hold.Weight;
                pv.SetSpecialHold(0);
                for (int f = 0; f < 60; f++) yield return null;
                float backWeight = hold.Weight;

                Debug.Log("[DD-TEST] wp083.holdspecial pivotJumpMax=" + (maxJump * 100f).ToString("F2", CultureInfo.InvariantCulture) + " cm specialWeight=" +
                          specialWeight.ToString("F2", CultureInfo.InvariantCulture) + "/" + specialWeightLater.ToString("F2", CultureInfo.InvariantCulture) +
                          " backWeight=" + backWeight.ToString("F2", CultureInfo.InvariantCulture));
                Assert.LessOrEqual(maxJump, 0.01f, "gizli silahın işaretleri tutuşu sıçrattı (pivot kare arası > 1 cm)");
                Assert.AreEqual(0f, specialWeight, "Special eldeyken tutuş karışım süresinde 0'a inmeli");
                Assert.IsFalse(specialActive);
                Assert.AreEqual(0f, specialWeightLater, "Special eldeyken tutuş kapalı kalmalı");
                Assert.AreEqual(1f, backWeight, "klasiğe dönünce tutuş yeniden tam ağırlıkta olmalı");
            }
            finally
            {
                WeaponHoldIk.ResetStatics();
            }
        }

        /// <summary>WP-083 B2c diagnostics: the hold's reach — arm length, left shoulder → grip_l and both hands' errors for candidate hip holds.</summary>
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(300000)]
        public IEnumerator Tani_SilahTutusu_Erisim_AdayTutuslar()
        {
            yield return Stage();
            CharacterAssetSet set = CharacterAssetSet.Load();
            Vector3[] holds = { new Vector3(0.14f, -0.20f, 0.30f), new Vector3(0.10f, -0.20f, 0.25f), new Vector3(0.08f, -0.22f, 0.22f), new Vector3(0.06f, -0.18f, 0.20f), new Vector3(0.10f, -0.12f, 0.22f), new Vector3(0.04f, -0.22f, 0.26f) };
            try
            {
                WeaponHoldIk.Enabled = true;
                Time.captureDeltaTime = 1f / 60f;
                Vector3 aim = new Vector3(0.25f, 0.15f, 1f).normalized;
                for (int h = 0; h < holds.Length; h++)
                {
                    WeaponHoldIk.HipHold = holds[h];
                    var stage = new GameObject("WP083_Reach" + h);
                    _temp.Add(stage);
                    stage.transform.position = new Vector3(h * 3f, 0f, 0f);
                    Transform visual = new GameObject("Visual").transform;
                    visual.SetParent(stage.transform, false);
                    var pv = Object.Instantiate(set.PlayerPrefab, visual, false).GetComponent<PlayerSkinnedVisual>();
                    pv.Attach(visual, () => Vector3.zero, () => 6.2f, () => aim);
                    pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Transform socket = pv.WeaponSocket;
                    Transform grip = new GameObject("grip_l").transform;
                    grip.SetParent(socket, false);
                    grip.localPosition = new Vector3(-0.02f, 0.02f, 0.18f);
                    for (int f = 0; f < 40; f++) yield return null;
                    Animator an = pv.Animator;
                    Transform lu = an.GetBoneTransform(HumanBodyBones.LeftUpperArm), ll = an.GetBoneTransform(HumanBodyBones.LeftLowerArm), lh = an.GetBoneTransform(HumanBodyBones.LeftHand);
                    float arm = Vector3.Distance(lu.position, ll.position) + Vector3.Distance(ll.position, lh.position);
                    Debug.Log("[DD-DIAG] wp083.reach hold=" + holds[h].ToString("F2") + " arm=" + (arm * 100f).ToString("F1") + "cm shoulderToGrip=" + (Vector3.Distance(lu.position, grip.position) * 100f).ToString("F1") +
                              "cm left=" + (Vector3.Distance(lh.position, grip.position) * 100f).ToString("F2") + "cm right=" + (Vector3.Distance(socket.position, pv.HoldIk.PivotPosition) * 100f).ToString("F2") +
                              "cm gripLocal=" + an.transform.InverseTransformPoint(grip.position).ToString("F2") + " pivotLocal=" + an.transform.InverseTransformPoint(pv.HoldIk.PivotPosition).ToString("F2"));
                }
            }
            finally
            {
                WeaponHoldIk.ResetStatics();
            }
        }

        /// <summary>Diagnostics: every foot event the clips fire, with the firing clip and its blend weight.</summary>
        private sealed class FootEventLog : MonoBehaviour
        {
            public readonly List<string> Rows = new List<string>();
            public int Plants, Lifts, WeakPlants;
            private void Add(string name, AnimationEvent e, bool plant)
            {
                float w = e.animatorClipInfo.weight;
                if (plant) { Plants++; if (w < 0.5f) WeakPlants++; } else Lifts++;
                if (Rows.Count < 48) Rows.Add(Time.frameCount + ":" + name + "@" + (e.animatorClipInfo.clip != null ? e.animatorClipInfo.clip.name.Replace("AP009_player_", "").Replace("AP022_player_", "") : "?") + "·" + w.ToString("F2", CultureInfo.InvariantCulture));
            }

            public void foot_l(AnimationEvent e) => Add("L", e, true);
            public void foot_r(AnimationEvent e) => Add("R", e, true);
            public void foot_l_off(AnimationEvent e) => Add("Loff", e, false);
            public void foot_r_off(AnimationEvent e) => Add("Roff", e, false);
        }

        /// <summary>
        /// WP-071 (C-00 01.10, 4a) diagnostic: the player's planted-foot drift per walk direction (aim fixed forward) — the
        /// diagonals blend two clips, so both clips' plant / lift events fire. Logs per direction: lock episodes, median / max
        /// drift, plants (and those from a clip under half weight), lifts, the first events.
        /// </summary>
        [UnityTest]
        [Explicit, Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator Tani_Oyuncu_AyakKilidi_Yonlere()
        {
            yield return Stage();
            CharacterAssetSet set = CharacterAssetSet.Load();
            foreach (float debounce in new[] { 0f, 0.1f })
            {
                foreach (float deg in new[] { 0f, 45f, 90f, 135f, 180f })
                {
                    FootIkAdapter.PlantDebounceSeconds = debounce;
                    var stage = new GameObject("WP071_PlayerDir" + deg);
                    _temp.Add(stage);
                    Transform visual = new GameObject("Visual").transform;
                    visual.SetParent(stage.transform, false);
                    GameObject go = Object.Instantiate(set.PlayerPrefab, visual, false);
                    var pv = go.GetComponent<PlayerSkinnedVisual>();
                    Vector3 velocity = Vector3.zero;
                    pv.Attach(visual, () => velocity, () => 6.2f, () => Vector3.forward);
                    pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    FootEventLog log = pv.Animator.gameObject.AddComponent<FootEventLog>();
                    float walk = pv.AnimationSet.SpeedOf(C.PlayerWalkFwd);
                    Time.captureDeltaTime = 1f / 60f;
                    velocity = Quaternion.Euler(0f, deg, 0f) * Vector3.forward * walk;
                    var feet = new[] { pv.Animator.GetBoneTransform(HumanBodyBones.LeftFoot), pv.Animator.GetBoneTransform(HumanBodyBones.RightFoot) };
                    var was = new bool[2];
                    var start = new Vector3[2];
                    var drift = new float[2];
                    var drifts = new List<float>();
                    // stance slide (lock or not): the foot's horizontal travel while it is within 2 cm of its lowest height
                    var track = new[] { new List<Vector3>(), new List<Vector3>() };
                    var slides = new List<float>();
                    // lock geometry at the lock frame: the foot's forward offset from its hip in body space, hip→foot / leg length
                    Transform[] hips = { pv.Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), pv.Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) };
                    Transform knee = pv.Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                    float leg = Vector3.Distance(hips[0].position, knee.position) + Vector3.Distance(knee.position, feet[0].position);
                    var lockFwd = new List<float>();
                    var lockStretch = new List<float>();
                    int locked = 0, frames = 0;
                    for (int f = 0; f < 360; f++)
                    {
                        stage.transform.position += velocity * Time.deltaTime;
                        yield return null;
                        if (f < 60) { log.Rows.Clear(); log.Plants = log.Lifts = log.WeakPlants = 0; continue; }
                        frames++;
                        for (int i = 0; i < 2; i++)
                        {
                            bool now = pv.FootIk.IsLocked(i);
                            if (now) locked++;
                            if (now && !was[i])
                            {
                                start[i] = feet[i].position; drift[i] = 0f;
                                Vector3 rel = Quaternion.Inverse(pv.transform.rotation) * (feet[i].position - hips[i].position);
                                lockFwd.Add(rel.z); lockStretch.Add((feet[i].position - hips[i].position).magnitude / leg);
                            }
                            if (now) { Vector3 d = feet[i].position - start[i]; d.y = 0f; drift[i] = Mathf.Max(drift[i], d.magnitude); }
                            if (!now && was[i]) drifts.Add(drift[i]);
                            was[i] = now;
                            track[i].Add(feet[i].position);
                        }
                    }

                    // stance slide (lock or not): the foot's horizontal travel while within 2 cm of its lowest height in the window
                    for (int i = 0; i < 2; i++)
                    {
                        float low = float.MaxValue;
                        foreach (Vector3 q in track[i]) low = Mathf.Min(low, q.y);
                        bool inSt = false; float run = 0f;
                        for (int k = 0; k < track[i].Count; k++)
                        {
                            bool st = track[i][k].y < low + 0.02f;
                            if (st && inSt) { Vector3 d = track[i][k] - track[i][k - 1]; d.y = 0f; run += d.magnitude; }
                            if (!st && inSt) slides.Add(run);
                            if (st && !inSt) run = 0f;
                            inSt = st;
                        }
                    }

                    drifts.Sort();
                    slides.Sort();
                    lockFwd.Sort();
                    lockStretch.Sort();
                    float median = drifts.Count > 0 ? drifts[drifts.Count / 2] : float.NaN, max = drifts.Count > 0 ? drifts[drifts.Count - 1] : float.NaN;
                    float sMed = slides.Count > 0 ? slides[slides.Count / 2] : float.NaN, sMax = slides.Count > 0 ? slides[slides.Count - 1] : float.NaN;
                    FootIkAdapter ik = pv.FootIk;
                    Debug.Log("[DD-TEST] wp071.footdir debounce=" + debounce.ToString("F2", CultureInfo.InvariantCulture) + " deg=" + deg + " lockedFootFrames=" + locked + "/" + (frames * 2) + " episodes=" + drifts.Count
                        + " medianDrift=" + (median * 100f).ToString("F1", CultureInfo.InvariantCulture) + " maxDrift=" + (max * 100f).ToString("F1", CultureInfo.InvariantCulture)
                        + " cm stanceSlide med=" + (sMed * 100f).ToString("F1", CultureInfo.InvariantCulture) + " max=" + (sMax * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm (n=" + slides.Count + ")"
                        + " lockAt fwd med=" + (lockFwd.Count > 0 ? (lockFwd[lockFwd.Count / 2] * 100f).ToString("F1", CultureInfo.InvariantCulture) : "-") + " cm stretch med=" + (lockStretch.Count > 0 ? lockStretch[lockStretch.Count / 2].ToString("F3", CultureInfo.InvariantCulture) : "-")
                        + " ikLeg=" + ik.LegLength.ToString("F3", CultureInfo.InvariantCulture) + " testLeg=" + leg.ToString("F3", CultureInfo.InvariantCulture) + " lastReleaseRatio=" + ik.LastReleaseRatio.ToString("F3", CultureInfo.InvariantCulture)
                        + " plants=" + log.Plants + " accepted=" + ik.PlantsAccepted + " debounced=" + ik.PlantsDebounced + " released stretch/gate/lift/plant=" + ik.ReleasedByStretch + "/" + ik.ReleasedByGate + "/" + ik.ReleasedByLift + "/" + ik.ReleasedByPlant
                        + " | " + string.Join(" ", log.Rows.GetRange(0, Mathf.Min(12, log.Rows.Count))));
                    Object.Destroy(stage);
                    yield return null;
                }
            }

            FootIkAdapter.PlantDebounceSeconds = 0.1f;
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
