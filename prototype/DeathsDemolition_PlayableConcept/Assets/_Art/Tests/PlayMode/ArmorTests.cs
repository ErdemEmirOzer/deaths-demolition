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
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Tests;
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
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-017 · AP-008 armour in the Run scene (Metal): 40 skinned zombies with every piece
    /// fitted through the WP-006 harness — binder live on each, five armour renderers per LOD0
    /// (helmet + dō/kote + haidate/suneate), one shared armour material, the garment under the
    /// armour inside the AP-008 rules; the Present → Damaged → Broken transition driven by
    /// real hits with the break event on the anchor; and the DEC-077 armour reading measured
    /// in-engine from the gameplay rig (ΔV armour vs the cloth beneath, vs the adjacent
    /// unarmoured band of the same figures, vs the ground ring; silhouette change) — the
    /// numbers ADR-017-1 records against the art-director's proposal (min 0.10 / mean 0.15
    /// + shape); the rule value is reported, not asserted.
    /// </summary>
    [TestFixture]
    [Category("WP017")]
    public sealed class ArmorTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, DolusZamanAsimi = 90f, YatismaZamanAsimi = 30f;
        private const int W = CharacterTests.W, H = CharacterTests.H;
        private EnemySpawnDirector _director;

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
            Time.timeScale = 1f;
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

        private IEnumerator RunAP005()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, SahneZamanAsimi, "World_AP005");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "run sistemleri");
            yield return CharacterTests.Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
        }

        private static ZombieAppearance AppearanceOf(ZombieEnemy z)
        {
            var a = z.VersionVisual as ZombieAppearance;
            Assert.IsNotNull(a, z.name + ": ZombieAppearance yok");
            return a;
        }

        private static bool ViolatesRules(ZombieAppearance a)
        {
            ZombieLookConstraints c = a.CurrentConstraints;
            ZombieLook look = a.Look;
            if (c.ExcludesTop(look.top) || c.ExcludesBottom(look.bottom) || c.ExcludesHead(look.head)) return true;
            if (c.fadedTop && look.topColourway != ZombieModuleLibrary.ColourwayFaded && look.topColourway != ZombieModuleLibrary.NoColourway) return true;
            if (c.fadedBottom && look.bottomColourway != ZombieModuleLibrary.ColourwayFaded && look.bottomColourway != ZombieModuleLibrary.NoColourway) return true;
            return false;
        }

        // ================================================================== 40 zırhlı zombi

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Run_AP005v2_40SkinnedZombi_TamZirh_Binder_5Renderer_TekMateryal_KurallarUyumlu()
        {
            yield return RunAP005();
            _director.ForceActiveTarget(40, true);
            float t0 = Time.realtimeSinceStartup;
            yield return CharacterTests.Bekle(() => _director.ActiveCount >= 40, DolusZamanAsimi, "40 zombi doğmadı");
            for (int i = 0; i < 10; i++) yield return null;

            int fitted = 0, armored = 0, renderers = 0, tris = 0, violations = 0, reassembled = 0;
            var materials = new HashSet<Material>();
            var variants = new HashSet<string>(StringComparer.Ordinal);
            var hatalar = new List<string>();
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null || !z.IsAlive) continue;
                ZombieAppearance a = AppearanceOf(z);
                if (!a.HasArmor) { hatalar.Add(z.name + ": zırh binder'ı yok"); continue; }
                string before = a.Look.Key;
                fitted += z.FitArmorComposition(true, true, true);
                if (a.Look.Key != before) reassembled++;
                armored++;
                ArmorVisualBinder b = a.ArmorBinder;
                foreach (ArmorSlot slot in new[] { ArmorSlot.Helmet, ArmorSlot.Body, ArmorSlot.Leg })
                {
                    if (b.StateOf(slot) != ArmorVisualState.Present) hatalar.Add(z.name + ": " + slot + " " + b.StateOf(slot));
                    if (z.ArmorVisualStateOf(slot) != ArmorVisualState.Present) hatalar.Add(z.name + ": görünüm sözleşmesi " + slot);
                }

                int n = b.ActiveRendererCount();
                renderers += n;
                if (n != 5) hatalar.Add(z.name + ": " + n + " zırh renderer'ı (helmet 1 + dō/kote 2 + haidate/suneate 2 = 5)");
                tris += b.ActiveLod0Triangles();
                if (ViolatesRules(a)) { violations++; hatalar.Add(z.name + ": giysi kuralı ihlali " + a.Look.Key + " zırh " + a.ArmorLook.Key); }
                variants.Add(a.ArmorLook.Key);
                for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                for (int s = 0; s < ZombieSkinnedVisual.ArmorSlotCount; s++)
                for (int part = 0; part < ZombieSkinnedVisual.ArmorPartCount; part++)
                {
                    SkinnedMeshRenderer r = z.Skinned.ArmorRenderer(lod, s, part);
                    if (r != null && r.enabled && r.sharedMaterial != null) materials.Add(r.sharedMaterial);
                }

                // The M1 plate placeholders never show on a skinned + armoured body.
                foreach (GameObject[] group in new[] { z.Rig.HelmetPieces, z.Rig.BodyArmorPieces, z.Rig.LegArmorPieces })
                {
                    if (group == null) continue;
                    foreach (GameObject piece in group) if (piece != null && piece.activeSelf) { hatalar.Add(z.name + ": yer tutucu plaka görünür " + piece.name); break; }
                }
            }

            Debug.Log("[DD-TEST] wp017.armor.crowd armored=" + armored + " piecesFitted=" + fitted + " avgRenderers=" + ((float)renderers / Mathf.Max(1, armored)).ToString("F2") + " avgLod0Tris=" + (tris / Mathf.Max(1, armored)) + " materials=" + materials.Count + " variantSets=" + variants.Count + " reassembledByRules=" + reassembled + " violations=" + violations + " fillSeconds=" + (Time.realtimeSinceStartup - t0).ToString("F2", CultureInfo.InvariantCulture));
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar.GetRange(0, Math.Min(30, hatalar.Count))));
            Assert.GreaterOrEqual(armored, 40);
            Assert.AreEqual(1, materials.Count, "kalabalıkta tek zırh materyali (Standard)");
            Assert.GreaterOrEqual(variants.Count, 4, "zırh varyant kümeleri çeşitli");

            // Art-director evidence: the armoured crowd from the gameplay rig (post-fx on).
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 6f) yield return null;
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp017-captures");
            Directory.CreateDirectory(dir);
            ArtVisualRegression.CameraPose pose = CharacterTests.GameplayPose("armor-crowd40");
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, W, H, 90, f => warm = f);
            CharacterTests.Capture(pose, Path.Combine(dir, "armor-crowd40-1920x1080.png"));
            Debug.Log("[DD-TEST] wp017.armor.crowd.capture warm=" + warm + " -> " + Path.Combine(dir, "armor-crowd40-1920x1080.png"));
            _director.ForceActiveTarget(0, false);
        }

        // ================================================================== durum geçişi

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ZirhDurumGecisi_HasarYoluyla_Present_Damaged_Broken_OlayAnkrajda_GiysiSabit()
        {
            yield return RunAP005();
            Vector3 p = PlayerRegistry.Primary.transform.position;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, p + new Vector3(0f, 0f, 8f), Quaternion.identity);
            Assert.IsNotNull(z, "zombi doğmadı");
            yield return null;
            ZombieAppearance a = AppearanceOf(z);
            Assert.IsTrue(a.HasArmor, "binder");
            Assert.AreEqual(3, z.FitArmorComposition(true, true, true));
            ArmorVisualBinder b = a.ArmorBinder;
            string look = a.Look.Key;
            var events = new List<ArmorVisualBinder.BrokenEvent>();
            Action<ArmorVisualBinder.BrokenEvent> handler = e => events.Add(e);
            ArmorVisualBinder.AnyBroken += handler;
            try
            {
                SkinnedMeshRenderer helmet = z.Skinned.ArmorRenderer(0, ArmorVisualBinder.Index(ArmorSlot.Helmet), ZombieArmorLibrary.PartMain);
                SkinnedMeshRenderer body = z.Skinned.ArmorRenderer(0, ArmorVisualBinder.Index(ArmorSlot.Body), ZombieArmorLibrary.PartMain);
                SkinnedMeshRenderer kote = z.Skinned.ArmorRenderer(0, ArmorVisualBinder.Index(ArmorSlot.Body), ZombieArmorLibrary.PartSub);
                Assert.IsTrue(helmet.enabled && helmet.sharedMesh != null && helmet.sharedMesh.name.Contains("_Present_"), "Present kask meshi: " + (helmet.sharedMesh != null ? helmet.sharedMesh.name : "-"));
                Assert.IsTrue(body.enabled && body.sharedMesh.name.Contains("_Present_") && kote.enabled && kote.sharedMesh.name.Contains("kote"), "Present dō + kote");
                string presentHelmet = helmet.sharedMesh.name;

                float durability = PrototypeConfig.Active.Enemies.Armor.DurabilityFor(ArmorSlot.Helmet);
                Assert.Greater(durability, 0f);
                var hit = new DamageHit { Amount = durability * 0.5f, Region = BodyRegion.Head, Point = z.transform.position + Vector3.up * 1.6f, Direction = Vector3.forward, ImpactForce = 1f, Source = DamageSourceKind.Bullet };
                z.ApplyHit(in hit);
                yield return null;
                Assert.AreEqual(ArmorVisualState.Damaged, b.StateOf(ArmorSlot.Helmet), "yarı dayanıklılık → Damaged");
                Assert.IsTrue(helmet.enabled && helmet.sharedMesh.name.Contains("_Damaged_"), "Damaged kask meshi: " + helmet.sharedMesh.name);
                Assert.AreNotEqual(presentHelmet, helmet.sharedMesh.name);
                Assert.AreEqual(ArmorVisualState.Present, b.StateOf(ArmorSlot.Body), "gövde dokunulmadı");
                Assert.AreEqual(0, events.Count);

                var hit2 = new DamageHit { Amount = durability * 2f, Region = BodyRegion.Head, Point = hit.Point, Direction = Vector3.forward, ImpactForce = 1f, Source = DamageSourceKind.Bullet };
                z.ApplyHit(in hit2);
                yield return null;
                Assert.AreEqual(ArmorVisualState.Broken, b.StateOf(ArmorSlot.Helmet), "dayanıklılık bitti → Broken");
                Assert.IsTrue(helmet.enabled && helmet.sharedMesh.name.Contains("Broken_remnants"), "Broken = artık mesh (asla boş değil): " + helmet.sharedMesh.name);
                Assert.AreEqual(ArmorVisualState.Broken, z.ArmorVisualStateOf(ArmorSlot.Helmet));
                Assert.AreEqual(1, events.Count, "tek kırılma olayı");
                Assert.AreEqual(ArmorSlot.Helmet, events[0].Slot);
                Assert.AreEqual(BodyRegion.Head, events[0].Region);
                Assert.AreEqual(z.Skinned.HelmetAnchor, events[0].Anchor, "olay ankrajı anchor_helmet");
                Assert.AreEqual(a.ArmorLook.helmet, events[0].Variant);
                Assert.AreEqual(look, a.Look.Key, "kırılma giysiyi değiştirmez (kısıtlar bir hayat boyu gevşemez)");

                // Idempotent: re-applying the same state changes nothing and raises nothing.
                a.Apply(ArmorSlot.Helmet, ArmorVisualState.Broken, 0f);
                Assert.AreEqual(1, events.Count);

                // Body: straight to Broken through the WP-006 harness (BreakAllArmor) — remnants on, kote off.
                int broken = z.BreakAllArmor();
                yield return null;
                Assert.AreEqual(2, broken, "gövde + bacak kırıldı (kask zaten kırıktı)");
                Assert.AreEqual(ArmorVisualState.Broken, b.StateOf(ArmorSlot.Body));
                Assert.IsTrue(body.enabled && body.sharedMesh.name.Contains("Broken_remnants"), body.sharedMesh.name);
                Assert.IsFalse(kote.enabled, "Broken gövde: kote kapalı");
                Assert.AreEqual(3, events.Count);
                Debug.Log("[DD-TEST] wp017.armor.states helmet " + presentHelmet + " → Damaged → " + helmet.sharedMesh.name + " events=" + events.Count + " look=" + look + " armor=" + a.ArmorLook.Key);
            }
            finally
            {
                ArmorVisualBinder.AnyBroken -= handler;
                if (z != null && z.IsAlive) z.Kill();
            }
        }

        // ================================================================== DEC-077 zırh okunurluğu

        // DEC-077 armour addendum (user 16.09, planning/00) and the R1 baseline it must not fall below (WP-084 Z1, C-00 06.10).
        private const float Dec077SilhouetteChange = 0.30f, Dec077BandMean = 0.12f, Dec077BandMin = 0.08f;
        // R1 baseline = the mean of two deterministic runs (0.286 / 0.279, 0.014 / 0.015); tolerance = repeat gap + 0.02 (C-00 06.10).
        private const float R1SilhouetteChange = 0.2825f, R1SilhouetteTolerance = 0.027f, R1BandMean = 0.0145f, R1BandTolerance = 0.021f;

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet()
        {
            yield return RunAP005();
            PlayerRig rig = PlayerRegistry.Primary;
            PlayerSkinnedVisual pv = CharacterTests.PlayerVisual();
            Vector3 feet = rig.transform.position;

            var zombies = new List<ZombieEnemy>();
            // WP-084 Z1 (C-00 06.10): the same twelve looks every run — the factory's seed stream otherwise depends on what the
            // session spawned before (R1 measured 0.299 and 0.264 in two runs of one commit).
            System.Reflection.MethodInfo resetSeed = typeof(EnemyFactory).GetMethod("ResetSeedForTests", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Assert.IsNotNull(resetSeed, "kaynak yok: EnemyFactory.ResetSeedForTests (internal; DD.Art.Tests has no InternalsVisibleTo)");
            resetSeed.Invoke(null, new object[] { 77u });
            for (int i = 0; i < 12; i++)
            {
                float ang = i * Mathf.PI * 2f / 12f;
                float r = 5f + (i % 3) * 2f;
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, feet + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r), Quaternion.identity);
                if (z != null) zombies.Add(z);
            }

            Assert.GreaterOrEqual(zombies.Count, 10, "kalabalık doğmadı");
            yield return null;
            // Every piece on every figure (worst case for "which region is armoured").
            foreach (ZombieEnemy z in zombies) Assert.AreEqual(3, z.FitArmorComposition(true, true, true), z.name);

            // WP-084 Z1: 30 fixed steps into the stance, then the simulation frozen BEFORE the streaming warm-up — its frame count
            // varies (5–6) and the crowd walked a different distance every run.
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 30; i++) yield return null;
            Time.timeScale = 0f;
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp017-captures");
            Directory.CreateDirectory(dir);
            ArtVisualRegression.CameraPose pose = CharacterTests.GameplayPose("armor-readability");
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, W, H, 90, f => warm = f);

            var zombieRenderers = new List<SkinnedMeshRenderer>();
            foreach (ZombieEnemy z in zombies) zombieRenderers.AddRange(z.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;
            try
            {
                Color32[] armored = CharacterTests.Capture(pose, Path.Combine(dir, "readability-armor-on.png"));
                CharacterTests.SetEnabled(pv.Renderers, false);
                Color32[] noPlayer = CharacterTests.Capture(pose, null);
                CharacterTests.SetEnabled(pv.Renderers, true);
                CharacterTests.SetEnabled(zombieRenderers, false);
                Color32[] noZombies = CharacterTests.Capture(pose, Path.Combine(dir, "readability-armor-nozombies.png"));
                CharacterTests.SetEnabled(zombieRenderers, true);

                // Per-figure armour masks: hide one zombie's armour at a time (its garments stay — the cloth beneath is exactly what the plate covers).
                var armorMasks = new List<bool[]>();
                var deltaUnder = new List<float>();
                var deltaBand = new List<float>();
                bool[] playerMask = CharacterTests.Diff(armored, noPlayer);
                bool[] zombieMaskArmored = CharacterTests.Diff(armored, noZombies);
                foreach (ZombieEnemy z in zombies)
                {
                    var armorRenderers = new List<SkinnedMeshRenderer>();
                    for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                    for (int s = 0; s < ZombieSkinnedVisual.ArmorSlotCount; s++)
                    for (int part = 0; part < ZombieSkinnedVisual.ArmorPartCount; part++)
                    {
                        SkinnedMeshRenderer r = z.Skinned.ArmorRenderer(lod, s, part);
                        if (r != null && r.enabled) armorRenderers.Add(r);
                    }

                    CharacterTests.SetEnabled(armorRenderers, false);
                    Color32[] bare = CharacterTests.Capture(pose, null);
                    CharacterTests.SetEnabled(armorRenderers, true);
                    bool[] m = CharacterTests.Diff(armored, bare);
                    if (CharacterTests.Count(m) < 40) continue;   // off-frame / occluded figure
                    armorMasks.Add(m);
                    // (a) the plate vs the cloth beneath it (same pixels, armour off); (b) the plate vs the
                    // adjacent 6-px unarmoured band of the same figure (AP-008's metric, in-engine).
                    deltaUnder.Add(Mathf.Abs(CharacterTests.MeanValue(armored, m) - CharacterTests.MeanValue(bare, m)));
                    var near = new bool[m.Length];
                    CharacterTests.Dilate(m, near, 6);
                    for (int i = 0; i < near.Length; i++) near[i] = near[i] && !m[i] && zombieMaskArmored[i];
                    if (CharacterTests.Count(near) > 20) deltaBand.Add(Mathf.Abs(CharacterTests.MeanValue(armored, m) - CharacterTests.MeanValue(armored, near)));
                }

                Assert.GreaterOrEqual(armorMasks.Count, 6, "zırh maskesi çıkan figür sayısı");
                var all = new bool[armored.Length];
                foreach (bool[] m in armorMasks) for (int i = 0; i < all.Length; i++) all[i] |= m[i];
                float armorV = CharacterTests.MeanValue(armored, all);
                float groundV = CharacterTests.MeanValue(armored, CharacterTests.Ring(zombieMaskArmored, playerMask, 4, 10));
                float deltaGround = Mathf.Abs(armorV - groundV);

                // Shape: the armoured silhouette vs the bare one (all armour off at once).
                var allArmor = new List<SkinnedMeshRenderer>();
                var armorBySlot = new List<SkinnedMeshRenderer>[ZombieSkinnedVisual.ArmorSlotCount];
                for (int s = 0; s < armorBySlot.Length; s++) armorBySlot[s] = new List<SkinnedMeshRenderer>();
                foreach (ZombieEnemy z in zombies)
                for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                for (int s = 0; s < ZombieSkinnedVisual.ArmorSlotCount; s++)
                for (int part = 0; part < ZombieSkinnedVisual.ArmorPartCount; part++)
                {
                    SkinnedMeshRenderer r = z.Skinned.ArmorRenderer(lod, s, part);
                    if (r != null && r.enabled) { allArmor.Add(r); armorBySlot[s].Add(r); }
                }

                CharacterTests.SetEnabled(allArmor, false);
                Color32[] bareAll = CharacterTests.Capture(pose, Path.Combine(dir, "readability-armor-off.png"));
                CharacterTests.SetEnabled(allArmor, true);
                bool[] zombieMaskBare = CharacterTests.Diff(bareAll, noZombies);
                int grown = 0, lost = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (zombieMaskArmored[i] && !zombieMaskBare[i]) grown++;
                    if (!zombieMaskArmored[i] && zombieMaskBare[i]) lost++;
                }

                float silhouetteChange = (grown + lost) / (float)Mathf.Max(1, CharacterTests.Count(zombieMaskBare));

                // WP-084 (C-00 06.10, for the AP-008C direction): the silhouette change by armour slot — each slot ALONE on over the bare
                // crowd (helmet / body = dō + kote / leg): grown = pixels the slot adds, lost = pixels it hides. Reported, not asserted.
                string[] armorSlotNames = { "helmet", "body", "leg" };
                var bySlot = new System.Text.StringBuilder();
                CharacterTests.SetEnabled(allArmor, false);
                for (int s = 0; s < ZombieSkinnedVisual.ArmorSlotCount; s++)
                {
                    CharacterTests.SetEnabled(armorBySlot[s], true);
                    Color32[] only = CharacterTests.Capture(pose, Path.Combine(dir, "readability-armor-only-" + armorSlotNames[s] + ".png"));
                    CharacterTests.SetEnabled(armorBySlot[s], false);
                    bool[] maskOnly = CharacterTests.Diff(only, noZombies);
                    int slotGrown = 0, slotLost = 0;
                    for (int i = 0; i < maskOnly.Length; i++)
                    {
                        if (maskOnly[i] && !zombieMaskBare[i]) slotGrown++;
                        if (!maskOnly[i] && zombieMaskBare[i]) slotLost++;
                    }

                    bySlot.Append(' ').Append(armorSlotNames[s]).Append('=').Append(((slotGrown + slotLost) / (float)Mathf.Max(1, CharacterTests.Count(zombieMaskBare))).ToString("F3", CultureInfo.InvariantCulture))
                          .Append(" (grown ").Append(slotGrown).Append(" lost ").Append(slotLost).Append(" renderers ").Append(armorBySlot[s].Count).Append(')');
                }

                CharacterTests.SetEnabled(allArmor, true);
                Debug.Log("[DD-OBS] wp084.z1.silhouette.bySlot total=" + silhouetteChange.ToString("F3", CultureInfo.InvariantCulture) + " (grown " + grown + " lost " + lost + " bare " + CharacterTests.Count(zombieMaskBare) + ")" + bySlot);
                float underMean = Mean(deltaUnder), underMin = Min(deltaUnder), bandMean = Mean(deltaBand), bandMin = Min(deltaBand);
                Debug.Log("[DD-TEST] wp017.armor.readability warm=" + warm + " figures=" + armorMasks.Count + " armorPx=" + CharacterTests.Count(all) + " zombiePx=" + CharacterTests.Count(zombieMaskArmored) +
                          " deltaV_under mean=" + underMean.ToString("F3", CultureInfo.InvariantCulture) + " min=" + underMin.ToString("F3", CultureInfo.InvariantCulture) +
                          " deltaV_band mean=" + bandMean.ToString("F3", CultureInfo.InvariantCulture) + " min=" + bandMin.ToString("F3", CultureInfo.InvariantCulture) +
                          " deltaV_ground=" + deltaGround.ToString("F3", CultureInfo.InvariantCulture) + " armorV=" + armorV.ToString("F3", CultureInfo.InvariantCulture) + " groundV=" + groundV.ToString("F3", CultureInfo.InvariantCulture) +
                          " silhouetteChange=" + silhouetteChange.ToString("F3", CultureInfo.InvariantCulture) + " (grown " + grown + " lost " + lost + ") -> " + dir);
                // Z1 (WP-084, C-00 06.10 K3): DEC-077 (user 16.09) — armour silhouette change ≥ 0.30 and the armour / 6 px unarmoured
                // neighbour band |ΔV| mean ≥ 0.12, min ≥ 0.08 — is ASSERTED (it was only reported against an older AD proposal), plus
                // no regression against R1 measured with this deterministic test (84941661, worktree-r1 06.10, two runs: silhouette
                // 0.286 / 0.279, band mean 0.014 / 0.015) — baseline the mean, tolerance the measured repeat gap + 0.02.
                // The source is in the record first (R2 standard): an armour mask and enough figures, then the R2 artefact.
                Assert.Greater(CharacterTests.Count(all), 500, "kaynak yok: zırh piksel maskesi boş");
                Assert.Greater(silhouetteChange, 0.02f, "zırh silüeti değiştirmeli (kask / omuz / tasset)");
                R2Olcum.Yaz("Z1", "DD.Art.Tests.PlayMode.ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet", new Dictionary<string, double>
                {
                    { "silhouetteChange", silhouetteChange }, { "deltaVBandMean", bandMean }, { "deltaVBandMin", bandMin },
                    { "deltaVUnderMean", underMean }, { "deltaVUnderMin", underMin }, { "deltaVGround", deltaGround },
                }, armorMasks.Count);
                Assert.GreaterOrEqual(silhouetteChange, R1SilhouetteChange - R1SilhouetteTolerance, "R1'e göre gerileme: siluet değişimi (R1 " + R1SilhouetteChange + ")");
                Assert.GreaterOrEqual(bandMean, R1BandMean - R1BandTolerance, "R1'e göre gerileme: komşu bant ΔV ort (R1 " + R1BandMean + ")");
                Assert.GreaterOrEqual(silhouetteChange, Dec077SilhouetteChange, "DEC-077: zırh siluet değişimi ≥ 0,30");
                Assert.GreaterOrEqual(bandMean, Dec077BandMean, "DEC-077: zırh ↔ komşu bant ΔV ort ≥ 0,12");
                Assert.GreaterOrEqual(bandMin, Dec077BandMin, "DEC-077: zırh ↔ komşu bant ΔV min ≥ 0,08");
            }
            finally
            {
                Time.timeScale = 1f;
                Time.captureDeltaTime = 0f;
                foreach (ZombieEnemy z in zombies) if (z != null && z.IsAlive) z.Kill();
            }
        }

        private static float Mean(List<float> v)
        {
            if (v.Count == 0) return 0f;
            float s = 0f;
            foreach (float x in v) s += x;
            return s / v.Count;
        }

        private static float Min(List<float> v)
        {
            float m = float.MaxValue;
            foreach (float x in v) m = Mathf.Min(m, x);
            return v.Count > 0 ? m : 0f;
        }
    }
}
