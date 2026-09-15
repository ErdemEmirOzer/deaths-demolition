using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Art;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Props;
using DD.Prototype.Rendering;
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
    /// WP-013 · Pilot sahnede atmosfer/VFX: rüzgâr sürücüsü ve drift alanları bütçe içinde;
    /// okunabilirlik (REQ-104/105) sis + parçacık + post-fx açıkken ölçülür (sentetik 1.8 m
    /// kapsül + altın bant, kapalıya karşı kontrast oranı); kırılabilir prop gerçek hitscan
    /// ile kırılır; dört sabit kamera (rüzgâr/sis açık-kapalı) görsel regresyon referansı yazar.
    /// </summary>
    [TestFixture]
    [Category("WP013")]
    public sealed class AtmosphereVfxTests
    {
        private const string PilotSahneAdi = "AP001_GoldenSlice";
        private const float SahneZamanAsimi = 20f;
        private const float KurulumZamanAsimi = 40f;
        private const float OyuncuZamanAsimi = 10f;
        private const float YatismaZamanAsimi = 20f;
        private const int W = 1280;
        private const int H = 720;

        private readonly List<GameObject> _gecici = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            foreach (GameObject go in _gecici)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }

            _gecici.Clear();
            RenderSettings.fog = true;
            AtmosferAyarla(true);
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi)
                {
                    yield return null;
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        // ============================================================== yardımcılar

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - t0 > saniye)
                {
                    Assert.Fail("Zaman aşımı (" + saniye + " s): " + aciklama);
                }

                yield return null;
            }
        }

        private static IEnumerator PilotSahneyiYukle()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu, SahneZamanAsimi, "MainMenu");
            yield return null;
            PrototypeUiFlow.ReleaseSimulation();
            // WP-015 (ADR-015-2): the pilot is a dev-only (disabled) Build Settings entry, so it
            // is loaded by path, as the showcase already is; by name it would not resolve.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ArtPipelinePaths.PilotScene, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PilotSahneAdi && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "pilot sahne");
            yield return Bekle(() => PilotSceneBootstrap.Last.Root != null, KurulumZamanAsimi, "PilotSceneBootstrap raporu gelmedi.");
            yield return Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "Oyuncu/kamera oluşmadı.");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 10; i++) yield return null;
        }

        private static void AtmosferAyarla(bool acik)
        {
            foreach (WindDriver w in UnityEngine.Object.FindObjectsOfType<WindDriver>())
            {
                w.EnabledScale = acik ? 1f : 0f;
            }

            foreach (AtmosphereDrift d in UnityEngine.Object.FindObjectsOfType<AtmosphereDrift>())
            {
                d.SetEnabledScale(acik ? 1f : 0f);
                if (!acik) d.System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                else if (!d.System.isPlaying) d.System.Play(true);
            }
        }

        private static string YakalamaKlasoru()
        {
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp013-captures");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ============================================================== 1 · rüzgâr + drift

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Pilot_RuzgarSurucusuVeDriftAlanlari_ButceIcinde()
        {
            yield return PilotSahneyiYukle();

            WindDriver[] ruzgar = UnityEngine.Object.FindObjectsOfType<WindDriver>();
            Assert.AreEqual(1, ruzgar.Length, "Sahnede tam bir WindDriver olmalı.");
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Greater(WorldWind.Strength, 0f, "Rüzgâr globali yazılmıyor.");
            Assert.LessOrEqual(WorldWind.Strength, 1f);
            Vector4 v = Shader.GetGlobalVector(WorldWind.WindVectorId);
            Assert.AreEqual(WorldWind.Strength, v.z, 1e-4f, "_DD_WindVector.z = strength");
            Assert.Greater(v.w, 0f, "rüzgâr saati ilerlemeli");

            AtmosphereDrift[] alanlar = UnityEngine.Object.FindObjectsOfType<AtmosphereDrift>();
            Assert.GreaterOrEqual(alanlar.Length, 1, "En az polen alanı olmalı.");
            int polen = 0, yaprak = 0, canli = 0, tavan = 0;
            foreach (AtmosphereDrift d in alanlar)
            {
                Assert.IsTrue(d.System.isPlaying, d.name + " oynamıyor.");
                Assert.IsTrue(d.System.externalForces.enabled, d.name + " rüzgâr kuvveti kapalı.");
                canli += d.System.particleCount;
                tavan += d.System.main.maxParticles;
                if (d.name.StartsWith(WorldAtmosphere.PollenResourceName, StringComparison.Ordinal)) polen++;
                else yaprak++;
            }

            Assert.AreEqual(1, polen, "Bir polen alanı.");
            Assert.LessOrEqual(tavan, 480, "Toplam parçacık tavanı (REQ-104 sayı argümanı).");
            Assert.LessOrEqual(canli, tavan);

            var foliageShader = Shader.Find("DD/Foliage Wind");
            int ruzgarli = 0;
            foreach (Renderer r in PilotSceneBootstrap.Last.Root.GetComponentsInChildren<Renderer>(false))
            {
                Material m = r.sharedMaterial;
                if (m != null && m.shader == foliageShader) ruzgarli++;
            }

            Debug.Log("[DD-TEST] wp013.pilot.atmosphere wind=" + WorldWind.Strength.ToString("F2", CultureInfo.InvariantCulture) +
                      " fields=" + alanlar.Length + " (pollen " + polen + ", petals " + yaprak + ") alive=" + canli + "/" + tavan +
                      " windShaderRenderers=" + ruzgarli + " sakuraCrowns=" + (yaprak > 0 ? "yes" : "no"));
        }

        // ============================================================== 2 · okunabilirlik

        /// <summary>
        /// REQ-104/105 ölçümü: sabit gameplay pozunda 1.8 m gri kapsül (düşman siluet vekili) ve
        /// altın bant. Sis + parçacık + post-fx açıkken kapsülün çevresine karşı Michelson
        /// kontrastı, kapalıya göre ≥ %90 ve mutlak ≥ <see cref="ReadabilityThresholds.CapsuleContrastMin"/>
        /// (0.06 — WP-015: kullanıcı kararı DEC-077, resmi eşik; WP-013'te 0.05 çalışma eşiğiydi);
        /// altın bandın çevreden ayrışması ≥ %80.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Pilot_Okunabilirlik_SisParcacikPostFxAcikken_KontrastKorunur()
        {
            yield return PilotSahneyiYukle();

            ArtVisualRegression.CameraPose poz = ArtVisualRegression.FindPose("silhouette_readability");
            Assert.IsNotNull(poz);
            Vector3 odak = poz.lookAt;

            // Vekil siluet: 1.8 m kapsül, orta gri Lit; altın bant: 0.6 m emissive-siz unlit altın.
            GameObject kapsul = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            kapsul.name = "wp013-readability-capsule";
            kapsul.transform.position = odak + new Vector3(0f, 0.9f, 6f);
            kapsul.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            var kapsulMat = new Material(PrototypeShaders.Lit);
            kapsulMat.SetColor("_BaseColor", new Color(0.32f, 0.30f, 0.28f));
            kapsul.GetComponent<Renderer>().sharedMaterial = kapsulMat;
            _gecici.Add(kapsul);

            GameObject altin = GameObject.CreatePrimitive(PrimitiveType.Quad);
            altin.name = "wp013-readability-gold";
            altin.transform.position = odak + new Vector3(2.5f, 1.2f, 6f);
            altin.transform.rotation = Quaternion.LookRotation(Vector3.forward + Vector3.down * 0.9f, Vector3.up);
            altin.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            var altinMat = new Material(PrototypeShaders.Unlit);
            altinMat.SetColor("_BaseColor", new Color(1f, 0.78f, 0.12f));
            altin.GetComponent<Renderer>().sharedMaterial = altinMat;
            _gecici.Add(altin);
            for (int i = 0; i < 3; i++) yield return null;

            string dir = YakalamaKlasoru();
            AtmosferAyarla(false);
            RenderSettings.fog = false;
            for (int i = 0; i < 3; i++) yield return null;
            Olcum kapali = Olc(poz, kapsul, altin, false, Path.Combine(dir, "readability-off-" + W + "x" + H + ".png"));
            yield return null;

            AtmosferAyarla(true);
            RenderSettings.fog = true;
            for (int i = 0; i < 90; i++) yield return null;   // drift alanları dolsun (prewarm yalnız Awake'de)
            Olcum acik = Olc(poz, kapsul, altin, true, Path.Combine(dir, "readability-on-" + W + "x" + H + ".png"));
            yield return null;

            Debug.Log("[DD-TEST] wp013.readability capsuleContrast off=" + kapali.Kontrast.ToString("F3", CultureInfo.InvariantCulture) +
                      " on=" + acik.Kontrast.ToString("F3", CultureInfo.InvariantCulture) +
                      " goldSaturation off=" + kapali.AltinDoygunluk.ToString("F3", CultureInfo.InvariantCulture) +
                      " on=" + acik.AltinDoygunluk.ToString("F3", CultureInfo.InvariantCulture) +
                      " goldSeparation off=" + kapali.AltinAyrisma.ToString("F3", CultureInfo.InvariantCulture) +
                      " on=" + acik.AltinAyrisma.ToString("F3", CultureInfo.InvariantCulture) +
                      " capsulePx=" + acik.KapsulPiksel + " goldPx=" + acik.AltinPiksel + " -> " + dir);

            Assert.Greater(acik.KapsulPiksel, 200, "Kapsül ekranda görünmüyor (ölçüm geçersiz).");
            Assert.Greater(acik.AltinPiksel, 50, "Altın bant ekranda görünmüyor (ölçüm geçersiz).");
            Assert.GreaterOrEqual(acik.Kontrast, ReadabilityThresholds.CapsuleContrastMin, "Siluet kontrastı resmi eşiğin (DEC-077, " + ReadabilityThresholds.CapsuleContrastMin + ") altında.");
            Assert.GreaterOrEqual(acik.Kontrast, kapali.Kontrast * 0.9f, "Sis/parçacık/post-fx siluet kontrastını %10'dan fazla düşürdü.");
            // REQ-105 çevreden ayrışma (çalışma eşiği; kullanıcı kararı §23 #4 açık): altın bandın
            // doygunluğu çevresindeki halkadan mutlak ≥ 0.30 yüksek ve grade'siz karenin ≥ %80'i;
            // doygunluk mutlak ≥ 0.60. Neutral tonemapping parlak doygun renkleri yapısal olarak
            // sıkıştırır (ölçüm: 0.878 → 0.809; ayrışma 0.516 → 0.430), bu yüzden %90 değil %80.
            Assert.GreaterOrEqual(acik.AltinDoygunluk, 0.6f, "Altın band doygunluğu mutlak eşiğin altında (REQ-105).");
            Assert.GreaterOrEqual(acik.AltinAyrisma, 0.3f, "Altın bandın çevreden ayrışması mutlak eşiğin altında (REQ-105).");
            Assert.GreaterOrEqual(acik.AltinAyrisma, kapali.AltinAyrisma * 0.8f, "Altın bandın çevreden ayrışması %20'den fazla düştü (REQ-105).");
        }

        private struct Olcum
        {
            public float Kontrast;
            public float AltinDoygunluk;
            /// <summary>REQ-105: gold saturation minus the saturation of the ring around it (separation from the palette).</summary>
            public float AltinAyrisma;
            public int KapsulPiksel;
            public int AltinPiksel;
        }

        private static Olcum Olc(ArtVisualRegression.CameraPose poz, GameObject kapsul, GameObject altin, bool postFx, string pngYol)
        {
            Texture2D tex = ArtVisualRegression.Capture(poz, W, H, postFx);
            try
            {
                File.WriteAllBytes(pngYol, tex.EncodeToPNG());
                Color32[] px = tex.GetPixels32();

                // Yakalama kamerasının projeksiyonu: aynı poz, aynı FOV, aynı en-boy.
                var camGo = new GameObject("wp013-proj");
                Camera cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = poz.verticalFov;
                cam.aspect = (float)W / H;
                cam.transform.position = poz.position;
                cam.transform.LookAt(poz.lookAt, Vector3.up);

                Olcum o;
                Rect kr = EkranDikdortgeni(cam, kapsul.GetComponent<Renderer>().bounds);
                Rect ar = EkranDikdortgeni(cam, altin.GetComponent<Renderer>().bounds);
                UnityEngine.Object.DestroyImmediate(camGo);

                // Kapsül: iç sütun (%30 daraltılmış), arka plan: sağ/sol 4..12 px şerit.
                float ic = Ortalama(px, Daralt(kr, 0.3f, 0.1f), out o.KapsulPiksel);
                int n1, n2;
                float solArka = Ortalama(px, new Rect(kr.xMin - 12f, kr.yMin + kr.height * 0.1f, 8f, kr.height * 0.8f), out n1);
                float sagArka = Ortalama(px, new Rect(kr.xMax + 4f, kr.yMin + kr.height * 0.1f, 8f, kr.height * 0.8f), out n2);
                float arka = (solArka * n1 + sagArka * n2) / Mathf.Max(1, n1 + n2);
                o.Kontrast = Mathf.Abs(ic - arka) / Mathf.Max(ic + arka, 1e-3f);
                o.AltinDoygunluk = Doygunluk(px, Daralt(ar, 0.3f, 0.3f), out o.AltinPiksel);
                int h1, h2;
                float halkaSol = Doygunluk(px, new Rect(ar.xMin - 16f, ar.yMin, 8f, ar.height), out h1);
                float halkaSag = Doygunluk(px, new Rect(ar.xMax + 8f, ar.yMin, 8f, ar.height), out h2);
                float halka = (halkaSol * h1 + halkaSag * h2) / Mathf.Max(1, h1 + h2);
                o.AltinAyrisma = o.AltinDoygunluk - halka;
                return o;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static Rect EkranDikdortgeni(Camera cam, Bounds b)
        {
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var kose = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 vp = cam.WorldToViewportPoint(kose);
                float sx = vp.x * W, sy = vp.y * H;   // ReadPixels satır 0 = alt; viewport y de alttan.
                xMin = Mathf.Min(xMin, sx); xMax = Mathf.Max(xMax, sx);
                yMin = Mathf.Min(yMin, sy); yMax = Mathf.Max(yMax, sy);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Rect Daralt(Rect r, float x, float y)
        {
            return Rect.MinMaxRect(r.xMin + r.width * x, r.yMin + r.height * y, r.xMax - r.width * x, r.yMax - r.height * y);
        }

        private static float Ortalama(Color32[] px, Rect r, out int sayi)
        {
            double toplam = 0; sayi = 0;
            for (int y = Mathf.Max(0, (int)r.yMin); y < Mathf.Min(H, (int)r.yMax); y++)
            for (int x = Mathf.Max(0, (int)r.xMin); x < Mathf.Min(W, (int)r.xMax); x++)
            {
                Color32 c = px[y * W + x];
                toplam += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                sayi++;
            }

            return sayi > 0 ? (float)(toplam / sayi / 255.0) : 0f;
        }

        private static float Doygunluk(Color32[] px, Rect r, out int sayi)
        {
            double toplam = 0; sayi = 0;
            for (int y = Mathf.Max(0, (int)r.yMin); y < Mathf.Min(H, (int)r.yMax); y++)
            for (int x = Mathf.Max(0, (int)r.xMin); x < Mathf.Min(W, (int)r.xMax); x++)
            {
                Color32 c = px[y * W + x];
                Color.RGBToHSV(c, out _, out float s, out _);
                toplam += s;
                sayi++;
            }

            return sayi > 0 ? (float)(toplam / sayi) : 0f;
        }

        // ============================================================== 3 · kırılabilir

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Pilot_Kirilabilir_GercekHitscanIsabetinde_KirilirTekPatlama()
        {
            yield return PilotSahneyiYukle();

            ArtFamilyLibrary library = PilotSceneBootstrap.Last.Library;
            GameObject prefab = null;
            foreach (string aday in new[] { "AP-003B/small_breakable", "small_breakable" })
            {
                prefab = library != null ? library.Find(aday) : null;
                if (prefab != null) break;
            }

            if (prefab == null)
            {
                // Kütüphane AP-001 aileleriyle sınırlıysa prefabı Resources dışı yoldan yükle (Editor PlayMode).
#if UNITY_EDITOR
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Art/Environment/Families/AP-003B/small_breakable/SM_small_breakable_V01.prefab");
#endif
            }

            Assert.IsNotNull(prefab, "small_breakable prefabı bulunamadı.");

            Vector3 oyuncu = PlayerRegistry.Primary.transform.position;
            Vector3 yer = oyuncu + new Vector3(0f, 0f, 4f);
            GameObject prop = UnityEngine.Object.Instantiate(prefab, yer, Quaternion.identity);
            _gecici.Add(prop);
            yield return null;
            var bp = prop.GetComponent<BreakableProp>();
            Assert.IsNotNull(bp, "Prefabda BreakableProp yok.");
            Assert.IsFalse(bp.IsBroken);

            Collider proxy = bp.Geometry.GetComponentInChildren<Collider>(false);
            Assert.IsNotNull(proxy, "Sağlam proxy collider yok.");
            Vector3 hedef = proxy.bounds.center;
            Vector3 origin = hedef + new Vector3(0f, 0.15f, -3f);
            int once = BreakableProp.BreakCount;
            int patlamaOnce = UnityEngine.Object.FindObjectsOfType<ParticleSystem>().Length;

            var istek = new ShotRequest
            {
                Origin = origin,
                Direction = (hedef - origin).normalized,
                Damage = 10f,
                Range = 20f,
                ImpactForce = 1f,
                Source = DamageSourceKind.Bullet,
                Power = WeaponPower.Medium,
                PierceDamageFalloff = 1f,
                RicochetDamageScale = 1f,
                ChainDamageScale = 1f,
                ChainDamageFalloff = 1f
            };
            ShotResult sonuc = HitscanEngine.Fire(in istek);
            yield return null;

            Assert.IsTrue(sonuc.HitSomething, "Atış prop'a çarpmadı (katman/proxy?).");
            Assert.IsTrue(bp.IsBroken, "Prop kırılmadı.");
            Assert.AreEqual(once + 1, BreakableProp.BreakCount);
            Assert.IsFalse(bp.Geometry.gameObject.activeSelf);
            Assert.IsTrue(bp.Broken.gameObject.activeSelf);
            GameObject patlama = GameObject.Find(BreakableProp.BurstResourceName + "(Clone)");
            Assert.IsNotNull(patlama, "Kırılma patlaması oluşmadı (DD_BreakBurst Resources'ta mı?).");

            // İkinci atış: kırık proxy sessiz, sayaç sabit, ikinci patlama yok.
            ShotResult ikinci = HitscanEngine.Fire(in istek);
            yield return null;
            Assert.AreEqual(once + 1, BreakableProp.BreakCount, "İkinci isabet ikinci kırılma saydı.");
            int patlamalar = 0;
            foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
            {
                if (ps.transform.root.name == BreakableProp.BurstResourceName + "(Clone)") patlamalar++;
            }

            Assert.LessOrEqual(patlamalar, 2, "Tek prefab = en fazla 2 sistem (chips + dust); ikinci patlama olmamalı.");
            Debug.Log("[DD-TEST] wp013.breakable hit=" + sonuc.HitSomething + " broken=" + bp.IsBroken + " breakCount=" + BreakableProp.BreakCount +
                      " second=" + ikinci.HitSomething + " burstSystems=" + patlamalar + " psBefore=" + patlamaOnce);
        }

        // ============================================================== 4 · görsel regresyon referansları

        /// <summary>
        /// Dört sabit kare: rüzgâr açık/kapalı (sakura_density pozu, rüzgâr sabit faz) ve sis
        /// açık/kapalı (beauty_overview, uzak alan). Kabul: kareler siyah/magenta değil; rüzgâr
        /// çifti ve sis çifti birbirinden FARKLI (shader gerçekten yer değiştiriyor, sis gerçekten
        /// görünüyor). Referanslar DD_ART_CAPTURE_DIR'e yazılır; art-director bunları yargılar.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Pilot_DortSabitKamera_RuzgarVeSisAcikKapali_Referans()
        {
            string dir = YakalamaKlasoru();

            // Sis çifti: pilot sahne, uzak alan (beauty_overview).
            yield return PilotSahneyiYukle();
            ArtVisualRegression.CameraPose uzak = ArtVisualRegression.FindPose("beauty_overview");
            Assert.IsNotNull(uzak);
            RenderSettings.fog = false;
            yield return null;
            string sisKapali = Path.Combine(dir, "pilot-fog-off-" + W + "x" + H + ".png");
            ArtVisualRegression.CaptureToPng(uzak, W, H, sisKapali, true);
            yield return null;
            RenderSettings.fog = true;
            yield return null;
            string sisAcik = Path.Combine(dir, "pilot-fog-on-" + W + "x" + H + ".png");
            ArtVisualRegression.CaptureToPng(uzak, W, H, sisAcik, true);
            yield return null;

            // Rüzgâr çifti: vitrin sahnesi (AP-003 bitki aileleri orada; pilotun AP-001 kartları
            // rüzgâr rengi taşımaz, WP-014 yerleşimine kadar pilotta sallanan bitki yok). Sürücü
            // yok, global elle yazılır: strength 0 ve 1, aynı rüzgâr saati → deterministik çift.
            string ruzgarKapali = Path.Combine(dir, "showcase-wind-off-" + W + "x" + H + ".png");
            string ruzgarAcik = Path.Combine(dir, "showcase-wind-on-" + W + "x" + H + ".png");
#if UNITY_EDITOR
            var parameters = new LoadSceneParameters(LoadSceneMode.Single);
            Scene vitrin = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/_Art/Scenes/AP_LibraryShowcase.unity", parameters);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(vitrin.isLoaded, "Vitrin sahnesi yüklenmedi.");
            var ap003 = new ArtVisualRegression.CameraPose("showcase_ap003_wind", new Vector3(-6f, 6f, 120f), new Vector3(24f, 1f, 158f), 42f);
            WorldWind.Apply(new Vector3(1f, 0f, 0.4f), 0f, 3.1f);
            yield return null;
            ArtVisualRegression.CaptureToPng(ap003, W, H, ruzgarKapali, true);
            yield return null;
            WorldWind.Apply(new Vector3(1f, 0f, 0.4f), 1f, 3.1f);
            yield return null;
            ArtVisualRegression.CaptureToPng(ap003, W, H, ruzgarAcik, true);
            yield return null;
            // Su v1 yakın plan (art-director koşulu): gölet yüzeyi örneği, alçak açı, rüzgâr orta.
            GameObject golet = null;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (t.name.StartsWith("SM_pond_water_surface_V01", StringComparison.Ordinal) && t.GetComponent<LODGroup>() != null) { golet = t.gameObject; break; }
            }

            if (golet == null)
            {
                foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    if (r.sharedMaterial != null && r.sharedMaterial.name == "M_pond_water") { golet = r.transform.root.gameObject; break; }
                }
            }

            Assert.IsNotNull(golet, "Vitrinde pond_water_surface örneği yok.");

            // Vitrinde su yüzeyi kendi yerel −0.15 m'sinde, yani vitrin zemin düzleminin altında
            // (üretimde koi_pond_basin çukuruna oturur; WP-014). Kanıt karesi için basin V01 ile
            // birlikte zeminin üstüne kaldırılır — yalnız bu oyun oturumunda, sahne kaydedilmez.
            GameObject basin = null;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (t.name.StartsWith("SM_koi_pond_basin_V01", StringComparison.Ordinal) && t.GetComponent<LODGroup>() != null) { basin = t.gameObject; break; }
            }

            if (basin != null)
            {
                basin.transform.position += Vector3.up * 0.75f;
                golet.transform.position = basin.transform.position;
            }
            else
            {
                golet.transform.position += Vector3.up * 0.35f;
            }

            yield return null;
            Bounds gb = new Bounds(golet.transform.position, Vector3.zero);
            bool ilk = true;
            foreach (Renderer r in golet.GetComponentsInChildren<Renderer>()) { if (ilk) { gb = r.bounds; ilk = false; } else gb.Encapsulate(r.bounds); }
            var suPoz = new ArtVisualRegression.CameraPose("showcase_water_closeup", gb.center + new Vector3(-4.5f, 1.6f, -5.5f), gb.center, 38f);
            WorldWind.Apply(new Vector3(1f, 0f, 0.4f), 0.6f, 7.3f);
            yield return null;
            string su = Path.Combine(dir, "showcase-water-closeup-" + W + "x" + H + ".png");
            ArtVisualRegression.CaptureToPng(suPoz, W, H, su, true);
            yield return null;
            Assert.IsTrue(File.Exists(su), su);
            WorldWind.ApplyCalm();
#else
            Assert.Ignore("Vitrin sahnesi yalnız Editor PlayMode.");
            yield break;
#endif

            foreach (string yol in new[] { ruzgarKapali, ruzgarAcik, sisKapali, sisAcik })
            {
                Assert.IsTrue(File.Exists(yol), yol);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                tex.LoadImage(File.ReadAllBytes(yol));
                Color32[] px = tex.GetPixels32();
                UnityEngine.Object.DestroyImmediate(tex);
                long sum = 0; int magenta = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    sum += px[i].r + px[i].g + px[i].b;
                    if (px[i].r > 200 && px[i].b > 200 && px[i].g < 60) magenta++;
                }

                Assert.Greater(sum / (3.0 * px.Length), 1.0, Path.GetFileName(yol) + ": kare siyah.");
                Assert.Less((double)magenta / px.Length, 0.001, Path.GetFileName(yol) + ": magenta (eksik shader?).");
            }

            ArtVisualRegression.Diff ruzgarFark = ArtVisualRegression.ComparePng(ruzgarKapali, ruzgarAcik, 8);
            ArtVisualRegression.Diff sisFark = ArtVisualRegression.ComparePng(sisKapali, sisAcik, 8);
            Debug.Log("[DD-TEST] wp013.captures windDiff=" + (ruzgarFark.ChangedFraction * 100.0).ToString("F2", CultureInfo.InvariantCulture) +
                      "% fogDiff=" + (sisFark.ChangedFraction * 100.0).ToString("F2", CultureInfo.InvariantCulture) + "% dir=" + dir);
            Assert.Greater(ruzgarFark.ChangedFraction, 0.001, "Rüzgâr açık/kapalı kareler aynı: shader yer değiştirmiyor.");
            Assert.Greater(sisFark.ChangedFraction, 0.001, "Sis açık/kapalı kareler aynı: sis görünmüyor.");
        }
    }
}
