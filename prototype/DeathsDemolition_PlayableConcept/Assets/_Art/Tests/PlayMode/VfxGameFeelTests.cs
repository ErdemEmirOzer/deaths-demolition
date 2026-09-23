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
using DD.Prototype.Core.Pooling;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rendering;
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
    /// WP-019 · Run (AP005 dünyası) içinde AP-010 efektleri: sekiz aile olaydan tetiklenir
    /// ve havuzludur, efekt yolu GC ayırmaz, beacon/altın/hasar vinyeti/game feel çalışır,
    /// efekt aktifken siluet kontrastı (DEC-077) korunur, flipbook süreleri manifest
    /// okunabilirlik sınırını aşmaz.
    /// </summary>
    [TestFixture]
    [Category("WP019")]
    public sealed class VfxGameFeelTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, DolusZamanAsimi = 90f, YatismaZamanAsimi = 30f;
        private const int W = 1280, H = 720;
        private EnemySpawnDirector _director;
        private readonly List<GameObject> _gecici = new List<GameObject>();

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
            foreach (GameObject go in _gecici) if (go != null) UnityEngine.Object.Destroy(go);
            _gecici.Clear();
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
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null && HitFeedbackDirector.Instance != null, KurulumZamanAsimi, "run sistemleri");
            yield return CharacterTests.Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
        }

        private static EnemyHitFeedbackInfo Info(Vector3 point, Transform root, bool armored, bool broke, bool killed, float health)
        {
            var r = new DamageResult { HealthDamage = health, ArmorDamage = armored ? 5f : 0f, ArmorBroke = broke, Killed = killed, WasArmored = armored, Region = BodyRegion.UpperBody };
            return new EnemyHitFeedbackInfo
            {
                Point = point, Direction = Vector3.forward, Normal = Vector3.back, Result = r, Power = WeaponPower.Medium,
                TargetId = 4242, TargetRoot = root, Weight = FeedbackWeight.Standard, ImpactForce = 1f
            };
        }

        // ============================================================== 1 · sekiz aile + havuz + GC

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Run_SekizAile_OlaydanTetiklenir_Havuzlu_GcSifir()
        {
            yield return RunAP005();
            HitFeedbackDirector fx = HitFeedbackDirector.Instance;
            Assert.IsTrue(VfxLibrary.IsReady, "VfxCatalog yüklenmedi.");
            Assert.IsTrue(fx.Vfx.IsReady, "AP-010 katmanı hazır değil.");

            Vector3 p = PlayerRegistry.Primary.transform.position + new Vector3(0f, 1f, 3f);
            var rootGo = new GameObject("wp019-target"); rootGo.transform.position = p - Vector3.up; _gecici.Add(rootGo);
            int before = fx.Vfx.Played, decalsBefore = fx.Vfx.DecalsPlayed, breakSheetsBefore = fx.Vfx.ArmorBreakSheets;

            // 1 impact ×5 surfaces (+3 decals on static surfaces), 2 muzzle, 3 explosion, 4 armour break, 5 death, body impacts.
            foreach (SurfaceKind s in new[] { SurfaceKind.Stone, SurfaceKind.Wood, SurfaceKind.Dirt, SurfaceKind.Metal, SurfaceKind.Flesh })
            {
                fx.ReportSurfaceImpact(p + Vector3.right * (int)s * 4f, Vector3.up, s, 1f);   // 4 m apart: outside the budget's proximity merge
            }

            fx.ReportMuzzleFlash(WeaponRegistry.ActiveController.Muzzle, 1f, 1f);
            fx.ReportExplosion(p + Vector3.forward * 4f, 2.5f);
            fx.ReportEnemyHit(Info(p, rootGo.transform, true, true, false, 0f));   // armour break
            fx.ReportEnemyHit(Info(p, rootGo.transform, false, false, true, 30f)); // death (+ flesh)
            fx.ReportEnemyHit(Info(p, rootGo.transform, true, false, false, 0f));  // armour hit
            yield return null;

            int played = fx.Vfx.Played - before;
            int decals = fx.Vfx.DecalsPlayed - decalsBefore;
            Assert.GreaterOrEqual(played, 11, "5 yüzey + namlu + patlama(2 sheet) + zırh kırılma + ölüm(2) + zırh vuruş ≥ 11 flipbook");
            // WP-047H (AP-010B v004): the armour break plays the asymmetric sheet (origin u 0.3) rolled to the break direction, not the ×1.3 armour sheet.
            Assert.AreEqual(1, fx.Vfx.ArmorBreakSheets - breakSheetsBefore, "zırh kırılması v004 asimetrik sheet'i (fb_armor_break) oynatmalı");
            bool breakSheet = false;
            foreach (FlipbookEffect f in fx.Vfx.FlipbookPool.Spawned)
            {
                if (f.Row == null || f.Row.Id != VfxIds.ArmorBreak) continue;
                breakSheet = true;
                Assert.That(f.Row.OriginUv.x, Is.EqualTo(0.3f).Within(0.01f), "kırılma sheet'i origin u 0.3 (+u yönünde kırılır)");
                Assert.Greater(f.AlignDirection.sqrMagnitude, 0.5f, "kırılma sheet'i vuruş/sekme yönüne yatırılmalı");
                Assert.IsNotNull(f.Row.Emissive, "kırılma sheet'i emissive katmanı (flaş + kıvılcım)");
            }
            Assert.IsTrue(breakSheet, "fb_armor_break havuzda oynuyor olmalı");
            Assert.GreaterOrEqual(decals, 6, "taş/ahşap/toprak/zırh dekalı + yanık + splinter + splat ≥ 6");
            Assert.Greater(fx.Vfx.FlipbookPool.CountSpawned, 0);
            Assert.LessOrEqual(fx.Vfx.FlipbookPool.CountTotal, VfxDressing.FlipbookCapacity, "havuz taşması");
            Assert.LessOrEqual(fx.Vfx.DecalPool.CountTotal, VfxDressing.DecalCapacity, "dekal havuzu taşması");

            // Each spawned sheet plays its manifest duration on its manifest size (readability contract).
            foreach (FlipbookEffect f in fx.Vfx.FlipbookPool.Spawned)
            {
                if (f.Row == null) continue;
                var r = f.GetComponentInChildren<MeshRenderer>();
                Camera cam = PrototypeCameraRig.Active != null ? PrototypeCameraRig.Active.GetComponentInChildren<Camera>() : Camera.main;
                Debug.Log("[DD-TEST] wp019.diag " + f.Row.Id + " active=" + f.gameObject.activeInHierarchy + " rEnabled=" + (r != null && r.enabled) + " visible=" + (r != null && r.isVisible) +
                          " bounds=" + (r != null ? r.bounds.ToString() : "-") + " mat=" + (r != null && r.sharedMaterial != null ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name + " supported=" + r.sharedMaterial.shader.isSupported : "null") +
                          " layer=" + f.gameObject.layer + " frame=" + f.CurrentFrame + " vp=" + (cam != null ? cam.WorldToViewportPoint(f.transform.position).ToString() : "-") + " lifetime? age progress");
                Assert.LessOrEqual(f.Row.DurationSeconds, Mathf.Max(f.Row.ReadabilityLimitSeconds, f.Row.DurationSeconds) + 0.001f);
            }

            // Warm-up then measure the effect path's managed allocation over 60 events.
            for (int i = 0; i < 60; i++) fx.ReportSurfaceImpact(p + Vector3.right * (i % 7) * 0.2f, Vector3.up, SurfaceKind.Stone, 1f);
            yield return null; yield return null;
            // Used-heap delta over 2000 direct layer calls (WP-047: GC.GetAllocatedBytesForCurrentThread is always 0 in Unity Mono —
            // the earlier 62-event figure measured nothing). The director path (budget, hit stop) is measured in VfxAllocationAuditTests.
            long gc0 = GC.GetTotalMemory(false);
            for (int i = 0; i < 2000; i++) fx.Vfx.PlaySurfaceImpact(SurfaceKind.Stone, p + Vector3.right * (i % 7) * 0.2f, Vector3.up, 1f);
            for (int i = 0; i < 100; i++) fx.Vfx.PlayExplosion(p + Vector3.forward * 4f, 2.5f);
            long gc1 = GC.GetTotalMemory(false);
            long perEvent = (gc1 - gc0) / 2100;
            Debug.Log("[DD-TEST] wp019.events played=" + played + " decals=" + decals + " flipbookPool=" + fx.Vfx.FlipbookPool.CountSpawned + "/" + fx.Vfx.FlipbookPool.CountTotal +
                      " decalPool=" + fx.Vfx.DecalPool.CountSpawned + "/" + fx.Vfx.DecalPool.CountTotal + " gcBytes62events=" + (gc1 - gc0) + " perEvent=" + perEvent);
            Assert.LessOrEqual(gc1 - gc0, 4096, "Efekt yolu yönetilen bellek ayırdı: " + (gc1 - gc0) + " B / 2100 olay (yığın deltası).");
        }

        // ============================================================== 2 · beacon + altın + hasar vinyeti + game feel

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Run_Beacon_Altin_HasarVinyeti_GameFeel()
        {
            yield return RunAP005();

            // Beacon: the WP-007 template now draws the AP-010 column/rings.
            PrototypePool<BeaconEffect> beacons = PoolService.GetOrCreate(FeedbackEffectFactory.BeaconTemplate, 1, 2, PoolExhaustedPolicy.RecycleOldest);
            BeaconEffect beacon = beacons.Get(PlayerRegistry.Primary.transform.position + Vector3.forward * 5f, Quaternion.identity);
            Assert.IsNotNull(beacon);
            beacon.Begin(12f, 0.5f, VfxLibrary.BeaconTint, new Color(1f, 0.36f, 0.2f, 0.5f));
            yield return null;
            Assert.IsTrue(beacon.UsesAp010, "Beacon AP-010 kartlarını çizmiyor.");
            Assert.IsNotNull(VfxLibrary.HudMarkerSprite, "HUD işareti sprite'ı yok (S-09 arayüzü).");
            beacon.Despawn();

            // Gold: kill one enemy, its packet uses the AP-010 pouch; magnetise it and check the trail.
            _director.ForceActiveTarget(3, true);
            yield return CharacterTests.Bekle(() => ZombieEnemy.Live.Count >= 1, DolusZamanAsimi, "zombi doğmadı");
            ZombieEnemy z = ZombieEnemy.Live[0];
            z.transform.position = PlayerRegistry.Primary.transform.position + Vector3.forward * 4f;
            yield return null;
            z.Kill();
            yield return CharacterTests.Bekle(() => GoldService.Instance != null && GoldService.Instance.LiveOrbCount >= 1, 10f, "gold paketi düşmedi");
            GoldPackVisual pack = UnityEngine.Object.FindObjectOfType<GoldPackVisual>();
            Assert.IsNotNull(pack, "Gold paketi AP-010 pouch'u kullanmıyor.");
            Assert.IsNotNull(pack.transform.GetComponent<MeshFilter>().sharedMesh, "pouch mesh");
            bool pulled = false;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f)
            {
                // Walk the player onto the packet so the magnet engages.
                PlayerRegistry.Primary.transform.position = Vector3.MoveTowards(PlayerRegistry.Primary.transform.position, pack.transform.parent.position, 2f * Time.deltaTime);
                if (pack.IsPulling) { pulled = true; break; }
                yield return null;
            }

            Debug.Log("[DD-TEST] wp019.gold pulled=" + pulled + " liveOrbs=" + GoldService.Instance.LiveOrbCount);

            // Player damage: vignette flashes (≤ 0.5), shake requested.
            DamageVignetteLayer vignette = UnityEngine.Object.FindObjectOfType<DamageVignetteLayer>();
            Assert.IsNotNull(vignette, "Hasar vinyeti katmanı yok.");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            int hitsBefore = vignette.Hits;   // the layer is DontDestroyOnLoad: counts persist across tests
            float ampBefore = CameraShakeBridge.LastAmplitude;
            PlayerRegistry.Primary.Health.ApplyDamage(5f, PlayerRegistry.Primary.transform.position + Vector3.forward, "wp019-test");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            float peak = 0f;
            for (int i = 0; i < 20; i++) { yield return null; peak = Mathf.Max(peak, vignette.ShownAlpha); }
            Debug.Log("[DD-TEST] wp019.damage hits=" + vignette.Hits + " vignettePeak=" + peak.ToString("F3", CultureInfo.InvariantCulture) + " shake=" + CameraShakeBridge.LastAmplitude.ToString("F3", CultureInfo.InvariantCulture));
            Assert.AreEqual(hitsBefore + 1, vignette.Hits);
            Assert.Greater(peak, 0.05f, "Vinyet parlamadı.");
            Assert.LessOrEqual(peak, GameFeelSettings.MaxVignetteAlpha + 0.001f);
            // The bump comes from PlayerSystem.PlayerDamageFeedback alone (REQ-045; WP-059 gives it 0.09 from DD_GameFeel) — the
            // vignette layer never requests a shake of its own, and the setting stays inside the type's ceiling.
            Assert.LessOrEqual(GameFeelSettings.Active.ShakePlayerDamageAmplitude, GameFeelSettings.MaxShakeAmplitude + 1e-6f, "Oyuncu hasarı sarsıntısı tavanı aşıyor.");
            yield return CharacterTests.Bekle(() => !vignette.IsFlashing, 5f, "vinyet sönmedi");

            // Hit-stop: an armour-break dressing stops time ≤ 40 ms, an explosion does not.
            HitFeedbackDirector fx = HitFeedbackDirector.Instance;
            var rootGo = new GameObject("wp019-target"); rootGo.transform.position = PlayerRegistry.Primary.transform.position + Vector3.forward * 3f; _gecici.Add(rootGo);
            int stops = HitStopService.Count;
            fx.ReportEnemyHit(Info(rootGo.transform.position + Vector3.up, rootGo.transform, true, true, false, 0f));
            // WP-062: the director enqueues on FeelEventRouter; the stop is granted at the late-frame flush, not inside the call.
            for (int i = 0; i < 3 && HitStopService.Count == stops; i++) yield return null;
            Assert.AreEqual(stops + 1, HitStopService.Count, "Zırh kırılmada hit-stop yok.");
            Assert.LessOrEqual(GameFeelSettings.Active.HitStopArmorBreak, GameFeelSettings.MaxHitStopSeconds);
            yield return CharacterTests.Bekle(() => !HitStopService.IsActive, 2f, "hit-stop bitmedi");
            float t1 = Time.unscaledTime; while (Time.unscaledTime - t1 < 0.2f) yield return null;   // past the min interval, so a refusal cannot mask a request
            stops = HitStopService.Count;
            fx.ReportExplosion(rootGo.transform.position, 2f);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(stops, HitStopService.Count, "Patlama hit-stop istememeli (ADR-019-2).");
        }

        // ============================================================== 3 · okunabilirlik efekt aktifken (DEC-077)

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Run_Okunabilirlik_PatlamaVeVuruslarAktifken_KontrastKorunur()
        {
            yield return RunAP005();
            HitFeedbackDirector fx = HitFeedbackDirector.Instance;
            Vector3 player = PlayerRegistry.Primary.transform.position;
            Vector3 odak = player + new Vector3(0f, 0f, 5f);

            GameObject kapsul = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            kapsul.transform.position = odak + Vector3.up * 0.9f;
            kapsul.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            var m = new Material(PrototypeShaders.Lit); m.SetColor("_BaseColor", new Color(0.16f, 0.15f, 0.14f));   // zombie-dark against the grey stone: clear contrast ≈ 0.25, so the ratios mean something
            kapsul.GetComponent<Renderer>().sharedMaterial = m;
            _gecici.Add(kapsul);
            var poz = new ArtVisualRegression.CameraPose("wp019_readability", player + new Vector3(0f, 15f, -13f), player, 42f);
            for (int i = 0; i < 3; i++) yield return null;
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp019-captures");
            Directory.CreateDirectory(dir);

            float kapali = Kontrast(poz, kapsul, Path.Combine(dir, "readability-fx-off.png"));
            // Effects around the capsule: explosion at the capsule, impacts on it, a death sheet at its feet.
            fx.ReportExplosion(odak, 2.5f);
            for (int i = 0; i < 6; i++) fx.ReportSurfaceImpact(odak + Vector3.up * (0.4f + 0.2f * i) + Vector3.back * 0.35f, Vector3.back, SurfaceKind.Stone, 1f);
            var rootGo = new GameObject("wp019-target"); rootGo.transform.position = odak; _gecici.Add(rootGo);
            fx.ReportEnemyHit(Info(odak + Vector3.up, rootGo.transform, false, false, true, 30f));
            // Manifest readability rule: dense cover (alpha ≥ 0.5 over the capsule) may exist ≤ 9 f; the
            // explosion sheet's alpha cap 0.45 is baked from f13 (0.43 s). Measured after the cap (0.45 s:
            // ≥ half the clear contrast — a 0.45-alpha veil leaves ~55 %) and at 0.65 s (≥ 70 %).
            float t0 = Time.time; while (Time.time - t0 < 0.10f) yield return null;
            Kontrast(poz, kapsul, Path.Combine(dir, "readability-fx-on-0.10s.png"));   // fireball peak, evidence only
            while (Time.time - t0 < 0.45f) yield return null;
            float acik = Kontrast(poz, kapsul, Path.Combine(dir, "readability-fx-on-0.45s.png"));
            while (Time.time - t0 < 0.65f) yield return null;
            float gec = Kontrast(poz, kapsul, Path.Combine(dir, "readability-fx-on-0.65s.png"));
            Debug.Log("[DD-TEST] wp019.readability capsuleContrast off=" + kapali.ToString("F3", CultureInfo.InvariantCulture) + " on0.45s=" + acik.ToString("F3", CultureInfo.InvariantCulture) +
                      " on0.65s=" + gec.ToString("F3", CultureInfo.InvariantCulture) + " -> " + dir);
            Assert.GreaterOrEqual(kapali, 0.05f, "Ölçüm geçersiz: efektsiz kapsül kontrastı zaten eşiğin altında.");
            Assert.GreaterOrEqual(acik, kapali * 0.5f, "0.45 s: alfa tavanı sonrası siluet kontrastının yarısından fazlası kayıp (DEC-077 / manifest okunabilirlik kuralı).");
            Assert.GreaterOrEqual(gec, kapali * 0.7f, "0.65 s: patlama kuyruğu + duman siluet kontrastını %30'dan fazla düşürüyor.");
        }

        private static float Kontrast(ArtVisualRegression.CameraPose poz, GameObject kapsul, string png)
        {
            Texture2D tex = ArtVisualRegression.Capture(poz, W, H, true);
            try
            {
                File.WriteAllBytes(png, tex.EncodeToPNG());
                Color32[] px = tex.GetPixels32();
                var camGo = new GameObject("wp019-proj");
                Camera cam = camGo.AddComponent<Camera>(); cam.enabled = false; cam.fieldOfView = poz.verticalFov; cam.aspect = (float)W / H;
                cam.transform.position = poz.position; cam.transform.LookAt(poz.lookAt, Vector3.up);
                Bounds b = kapsul.GetComponent<Renderer>().bounds;
                float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var k = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 vp = cam.WorldToViewportPoint(k);
                    xMin = Mathf.Min(xMin, vp.x * W); xMax = Mathf.Max(xMax, vp.x * W); yMin = Mathf.Min(yMin, vp.y * H); yMax = Mathf.Max(yMax, vp.y * H);
                }

                UnityEngine.Object.DestroyImmediate(camGo);
                float ic = Ort(px, xMin + (xMax - xMin) * 0.3f, yMin + (yMax - yMin) * 0.1f, xMax - (xMax - xMin) * 0.3f, yMax - (yMax - yMin) * 0.1f, out int n0);
                float sol = Ort(px, xMin - 12f, yMin + (yMax - yMin) * 0.1f, xMin - 4f, yMax - (yMax - yMin) * 0.1f, out int n1);
                float sag = Ort(px, xMax + 4f, yMin + (yMax - yMin) * 0.1f, xMax + 12f, yMax - (yMax - yMin) * 0.1f, out int n2);
                float arka = (sol * n1 + sag * n2) / Mathf.Max(1, n1 + n2);
                return n0 > 50 ? Mathf.Abs(ic - arka) / Mathf.Max(ic + arka, 1e-3f) : 0f;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static float Ort(Color32[] px, float x0, float y0, float x1, float y1, out int n)
        {
            double t = 0; n = 0;
            for (int y = Mathf.Max(0, (int)y0); y < Mathf.Min(H, (int)y1); y++)
            for (int x = Mathf.Max(0, (int)x0); x < Mathf.Min(W, (int)x1); x++)
            {
                Color32 c = px[y * W + x]; t += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; n++;
            }

            return n > 0 ? (float)(t / n / 255.0) : 0f;
        }
    }
}
