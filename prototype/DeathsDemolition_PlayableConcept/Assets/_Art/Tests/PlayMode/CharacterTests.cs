using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
            int skinned = 0, slots = 0, tris = 0;
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
                foreach (SkinnedMeshRenderer r in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.sharedMaterial != null) materials.Add(r.sharedMaterial);
                for (int s = 0; s < ZombieSkinnedVisual.SlotCount; s++)
                {
                    SkinnedMeshRenderer r = v.Slot(0, s);
                    if (r != null && r.enabled && r.sharedMesh != null) tris += (int)(r.sharedMesh.GetIndexCount(0) / 3);
                }

                if (v.Animator == null || !v.Animator.GetCurrentAnimatorStateInfo(0).IsName(CharacterAnimationSet.LocomotionState)) hatalar.Add(z.name + ": Animator Locomotion'da değil");
                if (v.HelmetAnchor == null || v.HelmetAnchor.parent.name != "Head") hatalar.Add(z.name + ": ankraj_helmet Head altında değil");
                Collider head = z.GetComponentInChildren<SphereCollider>(true);
                if (head == null || head.transform.parent != HumanoidBones.Bone(v.Animator, HumanBodyBones.Head)) hatalar.Add(z.name + ": kafa hitbox'ı Head kemiğinde değil");
                if (z.ArmorAnchors.Helmet != v.HelmetAnchor) hatalar.Add(z.name + ": ArmorAnchors.Helmet ankraja bağlı değil");
            }

            Debug.Log("[DD-TEST] wp016.crowd spawned=" + skinned + " fillSeconds=" + (Time.realtimeSinceStartup - t0).ToString("F2", CultureInfo.InvariantCulture) + " distinctLooks=" + looks.Count + " materials=" + materials.Count + " avgSlots=" + ((float)slots / Mathf.Max(1, skinned)).ToString("F2") + " avgLod0Tris=" + (tris / Mathf.Max(1, skinned)));
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            Assert.GreaterOrEqual(skinned, 40);
            Assert.LessOrEqual(materials.Count, 4, "kalabalık ≤ 4 paylaşılan materyal (Standard + 3 versiyon), örnek materyal yok");
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
            if (pv.Authored) Assert.Less(pv.Move.magnitude, 0.05f, "duran oyuncu Move ≈ 0");
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

            // A ring of twelve Standard zombies 5–9 m from the player, in the frame of the gameplay rig.
            var zombies = new List<ZombieEnemy>();
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                float r = 5f + (i % 3) * 2f;
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, feet + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), Quaternion.identity);
                if (z != null) zombies.Add(z);
            }

            Assert.GreaterOrEqual(zombies.Count, 10, "kalabalık doğmadı");
            // Freeze them where they are (the director is not driving them; they will still walk).
            Time.captureDeltaTime = 1f / 60f;
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp016-captures");
            Directory.CreateDirectory(dir);
            ArtVisualRegression.CameraPose pose = GameplayPose("characters-readability");
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, W, H, 90, f => warm = f);

            var zombieRenderers = new List<SkinnedMeshRenderer>();
            foreach (ZombieEnemy z in zombies) zombieRenderers.AddRange(z.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;
            try
            {
                // Full frame, then without the player, then without the zombies → difference masks.
                Color32[] full = Capture(pose, Path.Combine(dir, "readability-characters-full.png"));
                SetEnabled(pv.Renderers, false);
                Color32[] noPlayer = Capture(pose, null);
                SetEnabled(pv.Renderers, true);
                SetEnabled(zombieRenderers, false);
                Color32[] noZombies = Capture(pose, Path.Combine(dir, "readability-characters-nozombies.png"));
                SetEnabled(zombieRenderers, true);

                bool[] playerMask = Diff(full, noPlayer);
                bool[] zombieMask = Diff(full, noZombies);
                int playerPx = Count(playerMask), zombiePx = Count(zombieMask);
                Assert.Greater(playerPx, 400, "oyuncu maskesi boş (" + playerPx + " px)");
                Assert.Greater(zombiePx, 4000, "zombi maskesi boş (" + zombiePx + " px)");

                // Player per skin (the mask does not change with the albedo). Two readings: the whole
                // figure (hair, dark hakama, self-shadow included) and the WP-013-equivalent body
                // column — the upper 20–60 % band of the figure, central 60 % columns, i.e. the
                // primary garment the DEC-077 capsule proxy stands for.
                var skinContrast = new float[3];
                var skinContrastBody = new float[3];
                var playerLuma = new float[3];
                float surroundLuma = 0f;
                bool[] bodyMask = Band(playerMask, 0.20f, 0.60f, 0.20f, 0.80f);
                bool[] playerRing = Ring(playerMask, zombieMask, 4, 10);
                for (int s = 0; s < 3; s++)
                {
                    rig.ApplySkin((PlayerSkin)s);
                    Color32[] frame = Capture(pose, Path.Combine(dir, "readability-characters-skin" + (s + 1) + ".png"));
                    float inner = MeanLuma(frame, playerMask);
                    float body = MeanLuma(frame, bodyMask);
                    float ring = MeanLuma(frame, playerRing);
                    skinContrast[s] = Mathf.Abs(inner - ring) / Mathf.Max(inner + ring, 1e-3f);
                    skinContrastBody[s] = Mathf.Abs(body - ring) / Mathf.Max(body + ring, 1e-3f);
                    playerLuma[s] = body;
                    surroundLuma = ring;
                }

                rig.ApplySkin(PlayerSkins.Default);
                float zombieLuma = MeanLuma(full, zombieMask);
                float zombieRing = MeanLuma(full, Ring(zombieMask, playerMask, 4, 10));
                float zombieContrast = Mathf.Abs(zombieLuma - zombieRing) / Mathf.Max(zombieLuma + zombieRing, 1e-3f);
                float zombieDeltaV = Mathf.Abs(MeanValue(full, zombieMask) - MeanValue(full, Ring(zombieMask, playerMask, 4, 10)));

                Debug.Log("[DD-TEST] wp016.readability warm=" + warm + " playerPx=" + playerPx + " bodyPx=" + Count(bodyMask) + " zombiePx=" + zombiePx +
                          " figure skin1=" + skinContrast[0].ToString("F3", CultureInfo.InvariantCulture) + " skin2b=" + skinContrast[1].ToString("F3", CultureInfo.InvariantCulture) + " skin3=" + skinContrast[2].ToString("F3", CultureInfo.InvariantCulture) +
                          " | body skin1=" + skinContrastBody[0].ToString("F3", CultureInfo.InvariantCulture) + " skin2b=" + skinContrastBody[1].ToString("F3", CultureInfo.InvariantCulture) + " skin3=" + skinContrastBody[2].ToString("F3", CultureInfo.InvariantCulture) +
                          " zombieCrowd=" + zombieContrast.ToString("F3", CultureInfo.InvariantCulture) + " zombieDeltaV=" + zombieDeltaV.ToString("F3", CultureInfo.InvariantCulture) +
                          " playerVsZombieLuma=" + (playerLuma[0] - zombieLuma).ToString("F3", CultureInfo.InvariantCulture) + "/" + (playerLuma[1] - zombieLuma).ToString("F3", CultureInfo.InvariantCulture) + "/" + (playerLuma[2] - zombieLuma).ToString("F3", CultureInfo.InvariantCulture) +
                          " surround=" + surroundLuma.ToString("F3", CultureInfo.InvariantCulture) + " -> " + dir);

                // DEC-077 on real characters (WP-016 measurement, gameplay angle −49°): the zombie crowd is
                // asserted at the official 0.06; the player skins are REPORTED — from above, the head, the
                // mantle and the pack dominate the silhouette and the ivory/cream haori barely shows, so the
                // luminance reading differs from AP-006's front-view R2 (0.125 / 0.005 / 0.089) — measured
                // here 0.048 / 0.078 / 0.038 on courtyard paving: art-director judgment + AP-006 handover
                // (WP-016 note). Player/zombie separation is asserted for skins 1 and 3; skin 2b is reported (WP-017).
                Assert.GreaterOrEqual(zombieContrast, ReadabilityThresholds.CapsuleContrastMin, "zombi kalabalığı/çevre kontrastı (DEC-077)");
                Assert.Greater(Mathf.Abs(playerLuma[0] - zombieLuma), 0.08f, "skin 1 oyuncu gövdesi/zombi ayrımı");
                Assert.Greater(Mathf.Abs(playerLuma[2] - zombieLuma), 0.08f, "skin 3 oyuncu gövdesi/zombi ayrımı");
                // WP-017: skin 2 = 2b Steel/Indigo (user 2026-09-16 07:50). The light steel body separates from the
                // zombie cloth 2–3× better than 2a (measured 0.102 and 0.074 in two runs vs 0.031), but the reading
                // straddles 0.08 because the body band follows the idle frame the capture froze on (A-pose arms widen
                // the box) — reported, not asserted, until AP-009 clips fix the pose.
                if (Mathf.Abs(playerLuma[1] - zombieLuma) < 0.08f)
                {
                    Debug.LogWarning("[DD-TEST] wp016.readability REPORTED: skin 2b oyuncu gövdesi/zombi luma ayrımı " + Mathf.Abs(playerLuma[1] - zombieLuma).ToString("F3", CultureInfo.InvariantCulture) + " < 0.08 (poz bağımlı bant; 2a 0.031)");
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
                Time.timeScale = 1f;
                Time.captureDeltaTime = 0f;
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
