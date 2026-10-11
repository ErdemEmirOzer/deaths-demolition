using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
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
    /// WP-016 · The AP-006 player and AP-007 zombies in the Run scene (Metal): the skinned
    /// player under the M1 rig with the weapon on <c>socket_weapon_r</c> and the skin albedo
    /// swap; 40 skinned zombies through the real director with deterministic, ratio-ruled looks,
    /// shared materials, hitboxes on the bones; the locomotion Animator responding to speed;
    /// spawn → death → despawn; and the DEC-077 readability measured on REAL characters from
    /// the gameplay rig (player vs surround per skin, zombie crowd vs surround, player vs
    /// zombies) with the WP-013 luminance method on difference masks.
    /// </summary>
    [TestFixture]
    [Category("WP016")]
    public sealed class CharacterTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, DolusZamanAsimi = 90f, YatismaZamanAsimi = 30f;
        internal const int W = 1920, H = 1080;
        private EnemySpawnDirector _director;
        // WP-096 R3 (gt S4): what the DEC-077 test changed, restored in UnityTearDown as well — a timed-out iterator may skip its finally
        private float? _dec077PhaseBefore;
        private uint? _dec077SeedBefore;
        private UnityEngine.Random.State? _dec077RandomBefore;
        private bool _dec077PhaseSet;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            _director = null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Dec077Restore();
            Time.captureDeltaTime = 0f;
            if (_director != null) _director.ForceActiveTarget(0, false);
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            Environment.SetEnvironmentVariable(CharacterAssetSet.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi) yield return null;
            }

            _director = null;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------ helpers

        internal static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - t0 > saniye) Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                yield return null;
            }
        }

        private IEnumerator RunAP005()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, SahneZamanAsimi, "World_AP005");
            yield return Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            yield return Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "run sistemleri");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
        }

        internal static PlayerSkinnedVisual PlayerVisual()
        {
            PlayerRig rig = PlayerRegistry.Primary;
            Assert.IsNotNull(rig, "oyuncu yok");
            var visual = rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(visual, "PlayerSkinnedVisual yok (DD_Characters Skinned mi?)");
            return visual;
        }

        internal static ArtVisualRegression.CameraPose GameplayPose(string name)
        {
            Vector3 feet = PlayerRegistry.Primary.transform.position;
            return new ArtVisualRegression.CameraPose(name, feet + new Vector3(0f, 15f, -13f), feet + Vector3.up * 0.9f, 42f);
        }

        // ================================================================== yapı: oyuncu + 40 zombi

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Run_AP005_SkinnedOyuncu_40SkinnedZombi_Yapi_Paylasilan_Materyal_Hitbox_Kemikte()
        {
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerVisual();
            PlayerRig rig = PlayerRegistry.Primary;
            Assert.AreEqual(PlayerRigFactory.VisualName, pv.transform.parent.name, "skinned oyuncu Visual altında");
            Assert.AreEqual(pv.WeaponSocket, rig.WeaponMount.parent, "silah montajı socket_weapon_r altında");
            Assert.IsTrue(pv.Animator.isHuman && pv.Animator.runtimeAnimatorController != null);
            int hidden = 0, visible = 0;
            foreach (MeshRenderer mr in pv.transform.parent.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.transform.IsChildOf(rig.WeaponMount)) continue;   // the weapon model stays visible
                if (mr.enabled) visible++; else hidden++;
            }

            Assert.AreEqual(0, visible, "ilkel gövde parçaları gizli olmalı (" + hidden + " gizli)");
            CharacterAssetSet set = CharacterAssetSet.Load();
            Assert.AreEqual(set.PlayerSkinAlbedos[(int)rig.Skin], pv.BoundAlbedo(), "skin albedosu bağlı");

            _director.ForceActiveTarget(40, true);
            float t0 = Time.realtimeSinceStartup;
            yield return Bekle(() => _director.ActiveCount >= 40, DolusZamanAsimi, "40 zombi doğmadı");
            for (int i = 0; i < 30; i++) yield return null;

            var materials = new HashSet<Material>();
            var looks = new HashSet<string>(StringComparer.Ordinal);
            int skinned = 0, slots = 0, tris = 0, rising = 0, waiting = 0;
            var hatalar = new List<string>();
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null || !z.IsAlive) continue;
                ZombieSkinnedVisual v = z.Skinned;
                if (v == null) { hatalar.Add(z.name + ": skinned değil"); continue; }
                skinned++;
                int active = v.ActiveSlotCount();
                slots += active;
                if (active < 3) hatalar.Add(z.name + ": " + active + " etkin slot (gövde+üst+alt en az 3)");
                if (!v.Look.IsValid) hatalar.Add(z.name + ": geçersiz look");
                looks.Add(v.Look.Key);
                foreach (SkinnedMeshRenderer r in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) foreach (Material sm in r.sharedMaterials) if (sm != null) materials.Add(sm);
                for (int s = 0; s < ZombieSkinnedVisual.SlotCount; s++)
                {
                    SkinnedMeshRenderer r = v.Slot(0, s);
                    if (r != null && r.enabled && r.sharedMesh != null) tris += (int)(r.sharedMesh.GetIndexCount(0) / 3);
                }

                // WP-040 (DEC-081 hybrid rise): a share of the spawns rises from the ground first (Rise state for a second or two
                // after the fill), and WP-030/032B queue bodies at the bridge choke (Wait state) — both are authored controller
                // states, exempt here (WP-038D; the rise handshake and the queue have their own tests).
                if (v.Animator == null) hatalar.Add(z.name + ": Animator yok");
                else if (z.IsRising) rising++;
                else
                {
                    AnimatorStateInfo st = v.Animator.GetCurrentAnimatorStateInfo(0);
                    // Rise / Rise_1 / Rise_2 (the WP-040 variants): the timer handshake can end a few frames before the clip's own exit.
                    bool rise = st.IsName(CharacterAnimatorContract.RiseState) || st.IsName(CharacterAnimatorContract.RiseState + "_1") || st.IsName(CharacterAnimatorContract.RiseState + "_2");
                    if (rise) rising++;
                    else if (st.IsName(CharacterAnimatorContract.WaitState)) waiting++;
                    else if (!st.IsName(CharacterAnimationSet.LocomotionState)) hatalar.Add(z.name + ": Animator Locomotion'da değil (" + st.shortNameHash + ")");
                }
                if (v.HelmetAnchor == null || v.HelmetAnchor.parent.name != "Head") hatalar.Add(z.name + ": ankraj_helmet Head altında değil");
                Collider head = z.GetComponentInChildren<SphereCollider>(true);
                if (head == null || head.transform.parent != HumanoidBones.Bone(v.Animator, HumanBodyBones.Head)) hatalar.Add(z.name + ": kafa hitbox'ı Head kemiğinde değil");
                if (z.ArmorAnchors.Helmet != v.HelmetAnchor) hatalar.Add(z.name + ": ArmorAnchors.Helmet ankraja bağlı değil");
            }

            Debug.Log("[DD-TEST] wp016.crowd spawned=" + skinned + " rising=" + rising + " waiting=" + waiting + " fillSeconds=" + (Time.realtimeSinceStartup - t0).ToString("F2", CultureInfo.InvariantCulture) + " distinctLooks=" + looks.Count + " materials=" + materials.Count + " avgSlots=" + ((float)slots / Mathf.Max(1, skinned)).ToString("F2") + " avgLod0Tris=" + (tris / Mathf.Max(1, skinned)));
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            Assert.GreaterOrEqual(skinned, 40);
            Assert.LessOrEqual(materials.Count, 8, "kalabalık ≤ 8 paylaşılan materyal (atlas + kumaş atlası (WP-020) × Standard + 3 versiyon), örnek materyal yok");
            Assert.GreaterOrEqual(looks.Count, 30, "40 spawn'da ≥ 30 farklı look");

            // Art-director evidence: the crowd closing on the player from the gameplay rig, 1080p, post-fx on.
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 6f) yield return null;
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp016-captures");
            Directory.CreateDirectory(dir);
            ArtVisualRegression.CameraPose pose = GameplayPose("characters-crowd40");
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, W, H, 90, f => warm = f);
            Capture(pose, Path.Combine(dir, "characters-crowd40-1920x1080.png"));
            Debug.Log("[DD-TEST] wp016.crowd.capture warm=" + warm + " -> " + Path.Combine(dir, "characters-crowd40-1920x1080.png"));
            _director.ForceActiveTarget(0, false);
        }

        // ================================================================== skin seçimi

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SkinSecimi_UcSkin_AlbedoTakasi_AyniMateryal()
        {
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerVisual();
            PlayerRig rig = PlayerRegistry.Primary;
            CharacterAssetSet set = CharacterAssetSet.Load();
            Material shared = pv.Renderers[0].sharedMaterial;
            for (int i = 0; i < 3; i++)
            {
                rig.ApplySkin((PlayerSkin)i);
                yield return null;
                Assert.AreEqual(set.PlayerSkinAlbedos[i], pv.BoundAlbedo(), "skin " + i + " albedosu");
                Assert.AreEqual(i, pv.AppliedSkinIndex);
                foreach (SkinnedMeshRenderer r in pv.Renderers) Assert.AreEqual(shared, r.sharedMaterial, "albedo takası materyal örneği yaratmamalı");
            }

            rig.ApplySkin(PlayerSkins.Default);
            Debug.Log("[DD-TEST] wp016.skins albedos=" + set.PlayerSkinAlbedos[0].name + "," + set.PlayerSkinAlbedos[1].name + "," + set.PlayerSkinAlbedos[2].name);
        }

        // ================================================================== animator + ölüm

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Animator_IdleWalk_HizParametresi_OlumDespawn()
        {
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerVisual();
            for (int i = 0; i < 10; i++) yield return null;
            // WP-018: the authored set drives MoveX/MoveY/LocoRate; the rig-test set drives Speed.
            int speedId = Animator.StringToHash(CharacterAnimationSet.SpeedParameter);
            int speed01Id = Animator.StringToHash(CharacterAnimatorContract.Speed01);
            // WP-082 R3: on the AP-026 8-way tree MoveX / MoveY are the unit direction (kept while standing); standing = LocoSpeed ≈ 0
            if (pv.Authored && pv.Locomotion8) Assert.Less(pv.Animator.GetFloat(CharacterAnimatorContract.LocoSpeed), 0.05f, "duran oyuncu LocoSpeed ≈ 0");
            else if (pv.Authored) Assert.Less(pv.Move.magnitude, 0.05f, "duran oyuncu Move ≈ 0");
            else Assert.Less(pv.Animator.GetFloat(speedId), 0.05f, "duran oyuncu Speed ≈ 0");

            Vector3 p = PlayerRegistry.Primary.transform.position;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, p + new Vector3(0f, 0f, 12f), Quaternion.identity);
            Assert.IsNotNull(z, "zombi doğmadı");
            Assert.IsNotNull(z.Skinned);
            float t0 = Time.realtimeSinceStartup;
            float maxSpeed = 0f;
            Vector3 start = z.transform.position;
            float moved = 0f;
            while (Time.realtimeSinceStartup - t0 < 5f)
            {
                maxSpeed = Mathf.Max(maxSpeed, z.Skinned.Animator.GetFloat(z.Skinned.Authored ? speed01Id : speedId));
                moved = Mathf.Max(moved, Vector3.Distance(start, z.transform.position));
                yield return null;
            }

            Debug.Log("[DD-TEST] wp016.animator attached=" + z.Skinned.Attached + " authored=" + z.Skinned.Authored + " gait=" + z.Skinned.Gait + " zombieSpeedMax=" + maxSpeed.ToString("F2", CultureInfo.InvariantCulture) + " moved=" + moved.ToString("F2", CultureInfo.InvariantCulture) + " m alive=" + z.IsAlive + " state=" + z.Skinned.Animator.GetCurrentAnimatorStateInfo(0).IsName(CharacterAnimationSet.LocomotionState) + " controller=" + (z.Skinned.Animator.runtimeAnimatorController != null) + " enabled=" + z.Skinned.Animator.enabled);
            Assert.Greater(moved, 1f, "zombi oyuncuya doğru yürümedi (" + moved.ToString("F2") + " m)");
            Assert.Greater(maxSpeed, 0.2f, "oyuncuya yürüyen zombinin hız parametresi yükselmeli");

            z.Kill();
            yield return Bekle(() => z.IsDying || !z.IsAlive, 5f, "ölüm başlamadı");
            yield return Bekle(() => !z.gameObject.activeInHierarchy, 20f, "ceset havuza dönmedi");
        }

        // ================================================================== DEC-077 gerçek karakterler

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Okunabilirlik_DEC077_GercekKarakterler_OyuncuSkinler_ZombiKalabaligi()
        {
            yield return RunAP005();
            PlayerRig rig = PlayerRegistry.Primary;
            PlayerSkinnedVisual pv = PlayerVisual();
            Vector3 feet = rig.transform.position;

            // WP-096 R3 (C-00 10.10): one frozen frame read 0.042–0.085 on the same tree (S-05, six runs). Measured causes, in order:
            //  1. the cloud shadow — the sun cookie drifts 1.3 m/s (CloudShadowDriver) and no phase was fixed, so each run read the phase
            //     its frame count landed on. WP-075 direction §4 (binding): DEC-077 runs at the drift phases 0 / 0.25 / 0.5 / 0.75 and
            //     passes in EACH (CloudShadowDriver.ForcedPhase);
            //  2. the crowd's make-up — twelve bodies are one draw of the looks pool: at one phase the crowd luma read 0.12–0.27 by seed.
            //     The crowd is now the session's first Dec077Crowds × 12 bodies (the factory seed from its session-start value, the
            //     round director stopped and its own bodies gone), each crowd measured at every phase, the gates on the phase means;
            //  3. the frame count — the streaming warm-up runs before any crowd exists; each crowd walks at 1/60 until every body is out
            //     of its p3 start and settled in Locomotion, Dec077WalkFrames more, then holds its pose (a fixed count froze it inside the
            //     start); the player keeps animating and is sampled Dec077SamplesPerPhase times per phase and crowd.
            _director.StopRound();
            _director.ClearAllEnemies();
            FieldInfo seedField = Dec077SeedField();
            Assert.IsNotNull(seedField, "EnemyFactory.s_seedCounter yok — tohum sabitlenemez");
            _dec077RandomBefore = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Dec077Seed);
            _dec077SeedBefore = (uint)seedField.GetValue(null);
            seedField.SetValue(null, Dec077FactorySeed);
            // gt S1: the looks come from the factory seed, the gait clip (s_gaitPick) and its phase / speed jitter (s_gaitOrdinal) from
            // statics earlier spawns advance — reset, so a suite run freezes the same crowd an isolated run does
            ZombieSkinnedVisual.ResetGaitOrdinalForTests();
            _dec077PhaseBefore = CloudShadowDriver.ForcedPhase;
            _dec077PhaseSet = true;
            Time.captureDeltaTime = 1f / 60f;
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp016-captures");
            Directory.CreateDirectory(dir);
            ArtVisualRegression.CameraPose pose = GameplayPose("characters-readability");
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, W, H, 90, f => warm = f);

            // WP-059C (S-02): the weapon in hand sits level in front of the chest since WP-059B — hidden for the captures so the
            // figure / body band is the body itself, as the DEC-077 method measured it (S-10's gate excludes the same renderers).
            var weaponRenderers = new List<Renderer>();
            if (WeaponRegistry.ActiveController != null) WeaponRegistry.ActiveController.CollectWeaponRenderers(weaponRenderers);
            foreach (Renderer wr in weaponRenderers) if (wr != null) wr.enabled = false;
            int np = Dec077Phases.Length;
            var phaseSamples = new List<Dec077Sample>[np];
            for (int ph = 0; ph < np; ph++) phaseSamples[ph] = new List<Dec077Sample>(Dec077Crowds * Dec077SamplesPerPhase);
            var crowdPhaseSep = new float[Dec077Crowds, np];
            var crowdPhaseCrowd = new float[Dec077Crowds, np];
            var settleFrames = new int[Dec077Crowds];
            var zombies = new List<ZombieEnemy>();
            try
            {
                for (int c = 0; c < Dec077Crowds; c++)
                {
                    if (c > 0)
                    {
                        SetCrowdFrozen(zombies, false);
                        EnemyFactory.ClearAll();   // no kill credit; the next crowd takes the next twelve seeds
                        zombies.Clear();
                    }

                    // A ring of twelve Standard zombies 5–9 m from the player, in the frame of the gameplay rig.
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * Mathf.PI * 2f / 12f;
                        float r = 5f + (i % 3) * 2f;
                        ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, feet + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), Quaternion.identity);
                        if (z != null) zombies.Add(z);
                    }

                    Assert.GreaterOrEqual(zombies.Count, 10, "kalabalık " + c + " doğmadı");
                    var zombieRenderers = new List<SkinnedMeshRenderer>();
                    foreach (ZombieEnemy z in zombies) zombieRenderers.AddRange(z.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                    Time.timeScale = 1f;
                    Time.captureDeltaTime = 1f / 60f;
                    CloudShadowDriver.ForcedPhase = Dec077Phases[0];
                    // gt S2 (dec077settle60: a fixed 30 → 60 frames moved the worst phase 0.153 → 0.091 — the count froze the crowd inside
                    // its AP-033 p3 start): the crowd freezes in its walk — every body out of its p3 move and settled in Locomotion, then
                    // Dec077WalkFrames more (deterministic at 1/60)
                    int settle = 0;
                    while (settle < Dec077SettleMaxFrames && !Dec077CrowdWalking(zombies)) { settle++; yield return null; }
                    Assert.IsTrue(Dec077CrowdWalking(zombies), "kalabalık " + c + " " + Dec077SettleMaxFrames + " karede yürüyüşe oturmadı");
                    for (int f = 0; f < Dec077WalkFrames; f++) yield return null;
                    settleFrames[c] = settle;
                    SetCrowdFrozen(zombies, true);
                    for (int ph = 0; ph < np; ph++)
                    {
                        CloudShadowDriver.ForcedPhase = Dec077Phases[ph];
                        var crowd = new List<Dec077Sample>(Dec077SamplesPerPhase);
                        for (int k = 0; k < Dec077SamplesPerPhase; k++)
                        {
                            Time.timeScale = 1f;
                            Time.captureDeltaTime = 1f / 60f;
                            for (int f = 0; f < Dec077StepFrames; f++) yield return null;
                            Time.captureDeltaTime = 0f;
                            Time.timeScale = 0f;
                            crowd.Add(MeasureDec077(rig, pv, zombieRenderers, pose, c == 0 && ph == 0 && k == 0 ? dir : null));
                        }

                        phaseSamples[ph].AddRange(crowd);
                        crowdPhaseSep[c, ph] = Dec077Mean(crowd, x => Mathf.Abs(x.PlayerLuma[0] - x.ZombieLuma));
                        crowdPhaseCrowd[c, ph] = Dec077Mean(crowd, x => x.ZombieContrast);
                    }
                }

                var all = new List<Dec077Sample>();
                var phaseSep = new float[np, 3];
                var phaseCi = new float[np];
                var phaseCrowd = new float[np];
                for (int ph = 0; ph < np; ph++)
                {
                    List<Dec077Sample> l = phaseSamples[ph];
                    Assert.AreEqual(Dec077Crowds * Dec077SamplesPerPhase, l.Count, "DEC-077 faz " + ph + " örnekleri kayıtta değil");
                    all.AddRange(l);
                    for (int s = 0; s < 3; s++) { int sk = s; phaseSep[ph, s] = Dec077Mean(l, x => Mathf.Abs(x.PlayerLuma[sk] - x.ZombieLuma)); }
                    phaseCi[ph] = Dec077Ci95(l, x => Mathf.Abs(x.PlayerLuma[0] - x.ZombieLuma));
                    phaseCrowd[ph] = Dec077Mean(l, x => x.ZombieContrast);
                    var perCrowd = new System.Text.StringBuilder();
                    for (int c = 0; c < Dec077Crowds; c++)
                        perCrowd.Append(' ').Append(crowdPhaseSep[c, ph].ToString("F3", CultureInfo.InvariantCulture)).Append('/').Append(crowdPhaseCrowd[c, ph].ToString("F3", CultureInfo.InvariantCulture));
                    Debug.Log("[DD-TEST] wp016.readability.phase " + Dec077Phases[ph].ToString("F2", CultureInfo.InvariantCulture)
                              + " pL1=" + Dec077Mean(l, x => x.PlayerLuma[0]).ToString("F3", CultureInfo.InvariantCulture) + " zL=" + Dec077Mean(l, x => x.ZombieLuma).ToString("F3", CultureInfo.InvariantCulture)
                              + " sep1=" + phaseSep[ph, 0].ToString("F3", CultureInfo.InvariantCulture) + " ±" + phaseCi[ph].ToString("F3", CultureInfo.InvariantCulture)
                              + " sep2b=" + phaseSep[ph, 1].ToString("F3", CultureInfo.InvariantCulture) + " sep3=" + phaseSep[ph, 2].ToString("F3", CultureInfo.InvariantCulture)
                              + " zombieCrowd=" + phaseCrowd[ph].ToString("F3", CultureInfo.InvariantCulture) + " sep1/zombieCrowd per crowd:" + perCrowd);
                }

                int worst = 0, worstCrowd = 0;
                for (int ph = 1; ph < np; ph++)
                {
                    if (phaseSep[ph, 0] < phaseSep[worst, 0]) worst = ph;
                    if (phaseCrowd[ph] < phaseCrowd[worstCrowd]) worstCrowd = ph;
                }

                var skinContrast = new float[3];
                var skinContrastBody = new float[3];
                var sepWorst = new float[3];
                for (int s = 0; s < 3; s++)
                {
                    int sk = s;
                    skinContrast[s] = Dec077Mean(all, x => x.Figure[sk]);
                    skinContrastBody[s] = Dec077Mean(all, x => x.Body[sk]);
                    sepWorst[s] = phaseSep[0, s];
                    for (int ph = 1; ph < np; ph++) sepWorst[s] = Mathf.Min(sepWorst[s], phaseSep[ph, s]);
                }

                Debug.Log("[DD-TEST] wp016.readability warm=" + warm + " settle=" + string.Join("/", settleFrames) + "+" + Dec077WalkFrames + " crowds=" + Dec077Crowds + " phases=" + np + "x" + Dec077SamplesPerPhase + " seed=" + Dec077FactorySeed
                          + " playerPx=" + Dec077Mean(all, x => x.PlayerPx).ToString("F0", CultureInfo.InvariantCulture)
                          + " bodyPx=" + Dec077Mean(all, x => x.BodyPx).ToString("F0", CultureInfo.InvariantCulture)
                          + " zombiePx=" + Dec077Mean(all, x => x.ZombiePx).ToString("F0", CultureInfo.InvariantCulture) +
                          " figure skin1=" + skinContrast[0].ToString("F3", CultureInfo.InvariantCulture) + " skin2b=" + skinContrast[1].ToString("F3", CultureInfo.InvariantCulture) + " skin3=" + skinContrast[2].ToString("F3", CultureInfo.InvariantCulture) +
                          " | body skin1=" + skinContrastBody[0].ToString("F3", CultureInfo.InvariantCulture) + " skin2b=" + skinContrastBody[1].ToString("F3", CultureInfo.InvariantCulture) + " skin3=" + skinContrastBody[2].ToString("F3", CultureInfo.InvariantCulture) +
                          " zombieCrowd=" + phaseCrowd[worstCrowd].ToString("F3", CultureInfo.InvariantCulture) + " zombieDeltaV=" + Dec077Mean(all, x => x.ZombieDeltaV).ToString("F3", CultureInfo.InvariantCulture) +
                          " playerVsZombieLuma=" + sepWorst[0].ToString("F3", CultureInfo.InvariantCulture) + "/" + sepWorst[1].ToString("F3", CultureInfo.InvariantCulture) + "/" + sepWorst[2].ToString("F3", CultureInfo.InvariantCulture) +
                          " worstPhase=" + Dec077Phases[worst].ToString("F2", CultureInfo.InvariantCulture) + " ci95=" + phaseCi[worst].ToString("F3", CultureInfo.InvariantCulture) +
                          " surround=" + Dec077Mean(all, x => x.Surround).ToString("F3", CultureInfo.InvariantCulture) + " -> " + dir);

                // DEC-077 on real characters (WP-016 measurement, gameplay angle −49°): the zombie crowd is
                // asserted at the official 0.06; the player skins are REPORTED — from above, the head, the
                // mantle and the pack dominate the silhouette and the ivory/cream haori barely shows, so the
                // luminance reading differs from AP-006's front-view R2 (0.125 / 0.005 / 0.089) — measured
                // here 0.048 / 0.078 / 0.038 on courtyard paving: art-director judgment + AP-006 handover
                // (WP-016 note). Player/zombie separation is asserted for skin 1; skins 2b and 3 are reported (WP-017, WP-038G).
                // WP-096 R3: both gates hold in EVERY cloud phase (WP-075 §4) on the crowds' mean — the worst phase is asserted; the
                // thresholds are unchanged.
                Assert.GreaterOrEqual(phaseCrowd[worstCrowd], ReadabilityThresholds.CapsuleContrastMin,
                    "zombi kalabalığı/çevre kontrastı (DEC-077), bulut fazı " + Dec077Phases[worstCrowd].ToString("F2", CultureInfo.InvariantCulture)
                    + ": " + phaseCrowd[worstCrowd].ToString("F3", CultureInfo.InvariantCulture));
                Assert.Greater(sepWorst[0], 0.08f, "skin 1 oyuncu gövdesi/zombi ayrımı, en kötü bulut fazı " + Dec077Phases[worst].ToString("F2", CultureInfo.InvariantCulture)
                    + ": " + sepWorst[0].ToString("F3", CultureInfo.InvariantCulture) + " ±" + phaseCi[worst].ToString("F3", CultureInfo.InvariantCulture)
                    + " (fazlar " + phaseSep[0, 0].ToString("F3", CultureInfo.InvariantCulture) + " / " + phaseSep[1, 0].ToString("F3", CultureInfo.InvariantCulture)
                    + " / " + phaseSep[2, 0].ToString("F3", CultureInfo.InvariantCulture) + " / " + phaseSep[3, 0].ToString("F3", CultureInfo.InvariantCulture) + ")");
                // WP-038G: the character fill (K2) lifts the player and the crowd together (+0.07 / +0.09 luma), so the skin 3
                // body/zombie separation stays where the DEC-077 shadow-free mask (WP-038E) left it — 0.079 against the 0.08 line.
                // Skin 3's marginal separation is a product decision (WP-026 / AP-006B: QA-gate artefact, skin 3 v002 stays):
                // reported like skin 2b, not asserted.
                if (sepWorst[2] < 0.08f)
                {
                    Debug.LogWarning("[DD-TEST] wp016.readability REPORTED: skin 3 oyuncu gövdesi/zombi luma ayrımı " + sepWorst[2].ToString("F3", CultureInfo.InvariantCulture) + " < 0.08 (ürün kararı WP-026 / AP-006B)");
                }
                // WP-017: skin 2 = 2b Steel/Indigo (user 2026-09-16 07:50). The light steel body separates from the
                // zombie cloth 2–3× better than 2a (measured 0.102 and 0.074 in two runs vs 0.031), but the reading
                // straddles 0.08 because the body band follows the idle frame the capture froze on (A-pose arms widen
                // the box) — reported, not asserted, until AP-009 clips fix the pose.
                if (sepWorst[1] < 0.08f)
                {
                    Debug.LogWarning("[DD-TEST] wp016.readability REPORTED: skin 2b oyuncu gövdesi/zombi luma ayrımı " + sepWorst[1].ToString("F3", CultureInfo.InvariantCulture) + " < 0.08 (poz bağımlı bant; 2a 0.031)");
                }
                for (int s = 0; s < 3; s++)
                {
                    if (skinContrastBody[s] < ReadabilityThresholds.CapsuleContrastMin)
                    {
                        Debug.LogWarning("[DD-TEST] wp016.readability REPORTED: skin " + (s + 1) + " oyuncu gövdesi/çevre R2 " + skinContrastBody[s].ToString("F3", CultureInfo.InvariantCulture) + " < " + ReadabilityThresholds.CapsuleContrastMin + " (gameplay açısı; AP-006 devri)");
                    }
                }
            }
            finally
            {
                Dec077Restore();
                Time.timeScale = 1f;
                Time.captureDeltaTime = 0f;
                foreach (Renderer wr in weaponRenderers) if (wr != null) wr.enabled = true;   // WP-059C
                SetCrowdFrozen(zombies, false);
                foreach (ZombieEnemy z in zombies) if (z != null && z.IsAlive) z.Kill();
            }
        }

        // ------------------------------------------------------------------ DEC-077 sampling (WP-096 R3)

        private const int Dec077Seed = 16077;
        /// <summary>EnemyFactory's seed counter at session start (it resets to 1 at SubsystemRegistration): the crowds are the session's first bodies.</summary>
        private const uint Dec077FactorySeed = 1u;
        /// <summary>Consecutive crowds of twelve — one crowd is one draw of the looks pool (its luma read 0.12–0.27 by seed at one phase).</summary>
        private const int Dec077Crowds = 4;
        /// <summary>WP-075 direction §4: the cloud-cookie drift phases DEC-077 is measured at (each must pass).</summary>
        private static readonly float[] Dec077Phases = { 0f, 0.25f, 0.5f, 0.75f };
        private const int Dec077SamplesPerPhase = 3;   // per crowd and phase: the player's idle / aim pose, 0.2 s apart
        private const int Dec077SettleMaxFrames = 240; // 4 s at 1/60: the crowd must be walking (p3 start over, Locomotion) by then
        private const int Dec077WalkFrames = 10;       // then this many walk frames before it holds its pose
        private const int Dec077StepFrames = 12;       // 0.2 s between samples (the cookie phase is held)

        private struct Dec077Sample
        {
            public float PlayerPx, ZombiePx, BodyPx, Surround, ZombieLuma, ZombieContrast, ZombieDeltaV;
            public float[] Figure, Body, PlayerLuma;
        }

        private static FieldInfo Dec077SeedField() => typeof(EnemyFactory).GetField("s_seedCounter", BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>WP-096 R3 (gt S3 / S4): the cookie phase, EnemyFactory's seed counter and UnityEngine.Random's state go back — from the
        /// test's finally and again from UnityTearDown (each restores once).</summary>
        private void Dec077Restore()
        {
            if (_dec077PhaseSet) { CloudShadowDriver.ForcedPhase = _dec077PhaseBefore; _dec077PhaseSet = false; _dec077PhaseBefore = null; }
            if (_dec077SeedBefore.HasValue) { Dec077SeedField()?.SetValue(null, _dec077SeedBefore.Value); _dec077SeedBefore = null; }
            if (_dec077RandomBefore.HasValue) { UnityEngine.Random.state = _dec077RandomBefore.Value; _dec077RandomBefore = null; }
        }

        private static readonly int Dec077Speed01Id = Animator.StringToHash(CharacterAnimatorContract.Speed01);

        /// <summary>Every live body of the crowd walks: under way (Speed01 &gt; 0.5 — a fresh body stands in Locomotion before it sets
        /// off), out of its p3 start and settled in the Locomotion state (not blending into it).</summary>
        private static bool Dec077CrowdWalking(List<ZombieEnemy> zombies)
        {
            foreach (ZombieEnemy z in zombies)
            {
                if (z == null || !z.IsAlive || z.Skinned == null || z.Skinned.Animator == null) continue;
                Animator an = z.Skinned.Animator;
                if (an.GetFloat(Dec077Speed01Id) <= 0.5f || z.Skinned.TransitionActive || an.IsInTransition(0)
                    || !an.GetCurrentAnimatorStateInfo(0).IsName(CharacterAnimationSet.LocomotionState)) return false;
            }

            return true;
        }

        /// <summary>The crowd holds its pose (no walk, no animation) while the player keeps animating between samples.</summary>
        private static void SetCrowdFrozen(List<ZombieEnemy> zombies, bool frozen)
        {
            foreach (ZombieEnemy z in zombies)
            {
                if (z == null) continue;
                z.enabled = !frozen;
                if (z.Skinned != null && z.Skinned.Animator != null) z.Skinned.Animator.enabled = !frozen;
            }
        }

        /// <summary>One DEC-077 reading on the frozen frame: full / no player / no zombies → difference masks, then the three skins.</summary>
        private static Dec077Sample MeasureDec077(PlayerRig rig, PlayerSkinnedVisual pv, List<SkinnedMeshRenderer> zombieRenderers, ArtVisualRegression.CameraPose pose, string dir)
        {
            var m = new Dec077Sample { Figure = new float[3], Body = new float[3], PlayerLuma = new float[3] };
            Color32[] full = Capture(pose, dir != null ? Path.Combine(dir, "readability-characters-full.png") : null);
            SetEnabled(pv.Renderers, false);
            Color32[] noPlayer = Capture(pose, null);
            SetEnabled(pv.Renderers, true);
            SetEnabled(zombieRenderers, false);
            Color32[] noZombies = Capture(pose, dir != null ? Path.Combine(dir, "readability-characters-nozombies.png") : null);
            SetEnabled(zombieRenderers, true);

            // WP-038E (P-06 AP-007D measurement): the plain frame diff also picks up the zombies' cast shadows and SSAO
            // on the ground (60–68 % of the "zombie" pixels), which drags the crowd luma down as the crowd grows —
            // a pixel that merely got darker while keeping the ground's hue is shadow, not body, and is excluded.
            bool[] playerMask = DiffFigure(full, noPlayer);
            bool[] zombieMask = DiffFigure(full, noZombies);
            int playerPx = Count(playerMask), zombiePx = Count(zombieMask);
            Assert.Greater(playerPx, 400, "oyuncu maskesi boş (" + playerPx + " px)");
            Assert.Greater(zombiePx, 4000, "zombi maskesi boş (" + zombiePx + " px)");
            m.PlayerPx = playerPx; m.ZombiePx = zombiePx;

            // Player per skin (the mask does not change with the albedo). Two readings: the whole
            // figure (hair, dark hakama, self-shadow included) and the WP-013-equivalent body
            // column — the upper 20–60 % band of the figure, central 60 % columns, i.e. the
            // primary garment the DEC-077 capsule proxy stands for.
            bool[] bodyMask = Band(playerMask, 0.20f, 0.60f, 0.20f, 0.80f);
            bool[] playerRing = Ring(playerMask, zombieMask, 4, 10);
            m.BodyPx = Count(bodyMask);
            for (int s = 0; s < 3; s++)
            {
                rig.ApplySkin((PlayerSkin)s);
                Color32[] frame = Capture(pose, dir != null ? Path.Combine(dir, "readability-characters-skin" + (s + 1) + ".png") : null);
                float inner = MeanLuma(frame, playerMask);
                float body = MeanLuma(frame, bodyMask);
                float ring = MeanLuma(frame, playerRing);
                m.Figure[s] = Mathf.Abs(inner - ring) / Mathf.Max(inner + ring, 1e-3f);
                m.Body[s] = Mathf.Abs(body - ring) / Mathf.Max(body + ring, 1e-3f);
                m.PlayerLuma[s] = body;
                m.Surround = ring;
            }

            rig.ApplySkin(PlayerSkins.Default);
            bool[] zombieRing = Ring(zombieMask, playerMask, 4, 10);
            m.ZombieLuma = MeanLuma(full, zombieMask);
            float zr = MeanLuma(full, zombieRing);
            m.ZombieContrast = Mathf.Abs(m.ZombieLuma - zr) / Mathf.Max(m.ZombieLuma + zr, 1e-3f);
            m.ZombieDeltaV = Mathf.Abs(MeanValue(full, zombieMask) - MeanValue(full, zombieRing));
            return m;
        }

        private static float Dec077Mean(List<Dec077Sample> l, Func<Dec077Sample, float> f)
        {
            double sum = 0;
            foreach (Dec077Sample x in l) sum += f(x);
            return l.Count > 0 ? (float)(sum / l.Count) : 0f;
        }

        private static readonly double[] StudentT975 = { 0, 0, 12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228, 2.201 };

        /// <summary>Half-width of the 95 % confidence interval of the mean (Student t, n − 1 degrees of freedom).</summary>
        private static float Dec077Ci95(List<Dec077Sample> l, Func<Dec077Sample, float> f)
        {
            if (l.Count < 2) return 0f;
            float mean = Dec077Mean(l, f);
            double ss = 0;
            foreach (Dec077Sample x in l) { double d = f(x) - mean; ss += d * d; }
            double sd = Math.Sqrt(ss / (l.Count - 1));
            double t = l.Count < StudentT975.Length ? StudentT975[l.Count] : 1.96;
            return (float)(t * sd / Math.Sqrt(l.Count));
        }

        /// <summary>
        /// WP-111 R4 (S-06, C-00 11.10 · AD-R3READ SHOULD-1 / N1) · calibration only: the DEC-077 scene, masks and body band above, with
        /// the hero's Tier Lit _HERO_READ parameters swept through a property block — the darks-only lift (_HeroLift.x) and the rim
        /// (_HeroRim rgb) — per skin: body / figure luma against the crowd, and the silhouette edge (mask pixels with an outside
        /// neighbour) against the surround ring (the rim's job). Picks nothing, asserts nothing.
        /// </summary>
        [UnityTest]
        [Explicit("WP-111 R4 kalibrasyon — kahraman koyu kaldırma + kenar ışığı taraması")]
        [Timeout(600000)]
        public IEnumerator Okunabilirlik_DEC077_KahramanOkumaTaramasi()
        {
            yield return RunAP005();
            PlayerRig rig = PlayerRegistry.Primary;
            PlayerSkinnedVisual pv = PlayerVisual();
            Vector3 feet = rig.transform.position;
            var zombies = new List<ZombieEnemy>();
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                float r = 5f + (i % 3) * 2f;
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, feet + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), Quaternion.identity);
                if (z != null) zombies.Add(z);
            }

            Assert.GreaterOrEqual(zombies.Count, 10, "kalabalık doğmadı");
            Time.captureDeltaTime = 1f / 60f;
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp016-captures");
            Directory.CreateDirectory(dir);
            ArtVisualRegression.CameraPose pose = GameplayPose("characters-readability");
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, W, H, 90, f => warm = f);

            var zombieRenderers = new List<SkinnedMeshRenderer>();
            foreach (ZombieEnemy z in zombies) zombieRenderers.AddRange(z.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            var weaponRenderers = new List<Renderer>();
            if (WeaponRegistry.ActiveController != null) WeaponRegistry.ActiveController.CollectWeaponRenderers(weaponRenderers);
            foreach (Renderer wr in weaponRenderers) if (wr != null) wr.enabled = false;
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;
            var block = new MaterialPropertyBlock();
            int liftId = Shader.PropertyToID("_HeroLift"), rimId = Shader.PropertyToID("_HeroRim");
            Material shared = pv.Renderers[0].sharedMaterial;
            Vector4 materialLift = shared.GetVector(liftId), materialRim = shared.GetVector(rimId);
            Assert.IsTrue(shared.IsKeywordEnabled("_HERO_READ"), "M_player _HERO_READ değil (" + shared.shader.name + ")");
            try
            {
                Color32[] full = Capture(pose, null);
                SetEnabled(pv.Renderers, false);
                Color32[] noPlayer = Capture(pose, null);
                SetEnabled(pv.Renderers, true);
                SetEnabled(zombieRenderers, false);
                Color32[] noZombies = Capture(pose, null);
                SetEnabled(zombieRenderers, true);
                bool[] playerMask = DiffFigure(full, noPlayer);
                bool[] zombieMask = DiffFigure(full, noZombies);
                bool[] bodyMask = Band(playerMask, 0.20f, 0.60f, 0.20f, 0.80f);
                bool[] ring = Ring(playerMask, zombieMask, 4, 10);
                var edge = new bool[playerMask.Length];
                for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    int i = y * W + x;
                    edge[i] = playerMask[i] && (!playerMask[i - 1] || !playerMask[i + 1] || !playerMask[i - W] || !playerMask[i + W]);
                }

                float zombieLuma = MeanLuma(full, zombieMask);
                Debug.Log("[DD-TEST] wp111.read.sweep warm=" + warm + " playerPx=" + Count(playerMask) + " bodyPx=" + Count(bodyMask) + " edgePx=" + Count(edge) + " zombiePx=" + Count(zombieMask) +
                          " zombieLuma=" + zombieLuma.ToString("F3", CultureInfo.InvariantCulture) + " material lift=" + materialLift + " rim=" + materialRim);
                // (dark gain, rim linear blue channel, rim exponent): 1/0 = the plain atlas, 1.78/0 = the lift alone (R3's darks), then
                // stronger lifts and rims (the first sweep: a rim ≤ 0.12 at exponent 3 moved the edge luma ≤ 0.005 — invisible from TD)
                var combos = new[] { new Vector3(1f, 0f, 3f), new Vector3(1.78f, 0f, 3f), new Vector3(2.4f, 0.25f, 2f), new Vector3(2.4f, 0.4f, 2f),
                                     new Vector3(3f, 0.25f, 2f), new Vector3(3f, 0.4f, 2f), new Vector3(2.4f, 0.4f, 3f) };
                foreach (Vector3 c in combos)
                {
                    var row = new List<string>();
                    for (int s = 0; s < 3; s++)
                    {
                        rig.ApplySkin((PlayerSkin)s);
                        foreach (SkinnedMeshRenderer r in pv.Renderers)
                        {
                            if (r == null) continue;
                            r.GetPropertyBlock(block);
                            block.SetVector(liftId, new Vector4(c.x, materialLift.y, materialLift.z, 0f));
                            block.SetVector(rimId, new Vector4(c.y * materialRim.x / Mathf.Max(materialRim.z, 1e-4f), c.y * materialRim.y / Mathf.Max(materialRim.z, 1e-4f), c.y, c.z));
                            r.SetPropertyBlock(block);
                        }

                        string tag = "g" + c.x.ToString("F2", CultureInfo.InvariantCulture) + "-r" + c.y.ToString("F2", CultureInfo.InvariantCulture) + "-e" + c.z.ToString("F0", CultureInfo.InvariantCulture);
                        Color32[] frame = Capture(pose, s != 1 ? Path.Combine(dir, "sweep-skin" + (s + 1) + "-" + tag + ".png") : null);
                        float body = MeanLuma(frame, bodyMask), fig = MeanLuma(frame, playerMask), e = MeanLuma(frame, edge), sur = MeanLuma(frame, ring);
                        row.Add("skin" + (s + 1) + " body=" + body.ToString("F3", CultureInfo.InvariantCulture) + " sep=" + (body - zombieLuma).ToString("F3", CultureInfo.InvariantCulture) +
                                " figure=" + fig.ToString("F3", CultureInfo.InvariantCulture) + " edge=" + e.ToString("F3", CultureInfo.InvariantCulture) +
                                " edgeR2=" + (Mathf.Abs(e - sur) / Mathf.Max(e + sur, 1e-3f)).ToString("F3", CultureInfo.InvariantCulture) + " surround=" + sur.ToString("F3", CultureInfo.InvariantCulture));
                    }

                    Debug.Log("[DD-TEST] wp111.read.sweep gain=" + c.x.ToString("F2", CultureInfo.InvariantCulture) + " rim=" + c.y.ToString("F2", CultureInfo.InvariantCulture) + " exp=" + c.z.ToString("F0", CultureInfo.InvariantCulture) + " " + string.Join(" | ", row));
                }
            }
            finally
            {
                foreach (SkinnedMeshRenderer r in pv.Renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(block);
                    block.SetVector(liftId, materialLift);
                    block.SetVector(rimId, materialRim);
                    r.SetPropertyBlock(block);
                }

                rig.ApplySkin(PlayerSkins.Default);
                Time.timeScale = 1f;
                Time.captureDeltaTime = 0f;
                foreach (Renderer wr in weaponRenderers) if (wr != null) wr.enabled = true;
                foreach (ZombieEnemy z in zombies) if (z != null && z.IsAlive) z.Kill();
            }
        }

        // ------------------------------------------------------------------ pixel helpers

        internal static Color32[] Capture(ArtVisualRegression.CameraPose pose, string png)
        {
            Texture2D tex = ArtVisualRegression.Capture(pose, W, H, true);
            try
            {
                if (png != null) File.WriteAllBytes(png, tex.EncodeToPNG());
                return tex.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        internal static void SetEnabled(IEnumerable<SkinnedMeshRenderer> renderers, bool on)
        {
            foreach (SkinnedMeshRenderer r in renderers) if (r != null && r.sharedMesh != null) r.enabled = on;
        }

        internal static bool[] Diff(Color32[] a, Color32[] b)
        {
            var mask = new bool[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                mask[i] = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 24;
            }

            return mask;
        }

        /// <summary>DEC-077 figure mask — the shared rule (<see cref="ReadabilityThresholds.FigureDiff"/>; the QA gate reads the same).</summary>
        internal static bool[] DiffFigure(Color32[] a, Color32[] b) => ReadabilityThresholds.FigureDiff(a, b);

        internal static int Count(bool[] mask)
        {
            int n = 0;
            for (int i = 0; i < mask.Length; i++) if (mask[i]) n++;
            return n;
        }

        /// <summary>Pixels within [inner, outer] px of the mask (Chebyshev), outside it and outside the excluded mask.</summary>
        internal static bool[] Ring(bool[] mask, bool[] exclude, int inner, int outer)
        {
            var near = new bool[mask.Length];
            var far = new bool[mask.Length];
            Dilate(mask, near, inner);
            Dilate(mask, far, outer);
            var ring = new bool[mask.Length];
            for (int i = 0; i < mask.Length; i++) ring[i] = far[i] && !near[i] && !mask[i] && !exclude[i];
            return ring;
        }

        /// <summary>The part of a figure mask inside a band of its bounding box (fractions from the TOP for rows, from the left for columns).</summary>
        internal static bool[] Band(bool[] mask, float top, float bottom, float left, float right)
        {
            int minX = W, maxX = -1, minY = H, maxY = -1;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!mask[y * W + x]) continue;
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
            }

            var band = new bool[mask.Length];
            if (maxX < 0) return band;
            // ReadPixels rows run bottom → top, so "top" of the figure is the HIGH y.
            int hgt = maxY - minY, wid = maxX - minX;
            int y0 = maxY - Mathf.RoundToInt(hgt * bottom), y1 = maxY - Mathf.RoundToInt(hgt * top);
            int x0 = minX + Mathf.RoundToInt(wid * left), x1 = minX + Mathf.RoundToInt(wid * right);
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(H - 1, y1); y++)
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(W - 1, x1); x++)
                band[y * W + x] = mask[y * W + x];
            return band;
        }

        internal static void Dilate(bool[] src, bool[] dst, int radius)
        {
            // Separable box dilation.
            var tmp = new bool[src.Length];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool v = false;
                for (int dx = -radius; dx <= radius && !v; dx++) { int xx = x + dx; if (xx >= 0 && xx < W && src[y * W + xx]) v = true; }
                tmp[y * W + x] = v;
            }

            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool v = false;
                for (int dy = -radius; dy <= radius && !v; dy++) { int yy = y + dy; if (yy >= 0 && yy < H && tmp[yy * W + x]) v = true; }
                dst[y * W + x] = v;
            }
        }

        internal static float MeanLuma(Color32[] px, bool[] mask)
        {
            double sum = 0; int n = 0;
            for (int i = 0; i < px.Length; i++) if (mask[i]) { sum += 0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b; n++; }
            return n > 0 ? (float)(sum / n / 255.0) : 0f;
        }

        internal static float MeanValue(Color32[] px, bool[] mask)
        {
            double sum = 0; int n = 0;
            for (int i = 0; i < px.Length; i++) if (mask[i]) { sum += Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b)); n++; }
            return n > 0 ? (float)(sum / n / 255.0) : 0f;
        }
    }
}
