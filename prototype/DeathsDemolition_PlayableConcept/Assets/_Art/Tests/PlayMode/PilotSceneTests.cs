using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Art;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// WP-011 · DD.Art.Tests assembly'sinin profil izolasyonu.
//
// DD.Prototype.Tests içindeki TestProfileIsolation bir [SetUpFixture]'dır ve NUnit'te
// assembly kapsamlıdır: BU assembly'nin fixture'larından önce koşacağının garantisi yok
// (koşu sırası assembly'ye göre değişir). Boot yüklemek gerçek profili okur ve Play Mode
// çıkışında UiFlowRunner.OnApplicationQuit → ProfileService.Save gerçek dosyayı yazar.
// WP-009'dan beri kullanıcı verisi tabanı hash'le izlenir; bu yüzden aynı dikiş burada da
// kurulur. Override bilerek geri alınmaz (gerekçe TestProfileIsolation'da).
[SetUpFixture]
public sealed class ArtTestProfileIsolation
{
    public static string TestProfilePath =>
        Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-art-test-profile.json");

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        ProfileStore.SetPathOverride(TestProfilePath);
        ProfileService.ResetProfile();

        Assert.AreEqual(TestProfilePath, ProfileStore.Path,
            "Profil yolu override edilemedi; testler gerçek profili yazabilir.");
        Assert.IsTrue(File.Exists(TestProfilePath),
            "Geçici test profili oluşturulamadı: " + TestProfilePath);

        Debug.Log("[DD-TEST] art.profile.isolation path=" + TestProfilePath);
    }
}

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-011 · Pilot sahne (AP001_GoldenSlice) duman nöbetçisi — gerçek zincir, gerçek Metal.
    ///
    /// Boot → MainMenu → pilot sahne (SceneManager.LoadScene; RunDirector 0.35 s sonra 1. turu
    /// kendi başlatır, Run sahnesindeki gibi) → bootstrap raporu: 823 instance, 0 eksik, NavMesh
    /// üçgeni &gt; 0, erişilemeyen nokta 0 (12/12 + 12/12) → kamera/oyuncu/director → 40 düşman
    /// (SafetyCeiling, tavan config'ten okunur) → sabit kameradan PNG yakalama ve kendisiyle
    /// karşılaştırma (0) + pozitif kontrol (siyah kareye karşı &gt; 0: render gerçekten oldu).
    ///
    /// Sayı kilitlenmez: tavan <c>EnemySpawnDirector.SafetyCeiling</c>'den, 823 AP-001 manifest
    /// beyanı (GoldenSliceLayoutTests'te JSON'la çapraz doğrulanır). Zaman aşımları gerçek
    /// kare: 823 instance + NavMesh bake ilk yüklemede saniyeler alır.
    /// </summary>
    [TestFixture]
    [Category("WP011")]
    public sealed class PilotSceneTests
    {
        /// <summary>= ArtPipelinePaths.PilotSceneName; PlayMode assembly'si yol sabitlerine bağlanmasın diye burada.</summary>
        private const string PilotSahneAdi = "AP001_GoldenSlice";
        private const int BeklenenAileOrnegi = 823;
        private const float SahneZamanAsimi = 20f;
        private const float KurulumZamanAsimi = 40f;
        private const float OyuncuZamanAsimi = 10f;
        private const float DolusZamanAsimi = 60f;
        private const float YatismaZamanAsimi = 20f;
        private const int YakalamaGenislik = 640;
        private const int YakalamaYukseklik = 360;

        private EnemySpawnDirector _director;
        private PlayerRig _rig;

        // ============================================================== kurulum / temizlik

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            _director = null;
            _rig = null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;

            if (_director != null)
            {
                _director.ForceActiveTarget(0, false);
            }

            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            // Diğer fixture'lar gibi Main Menu'ye dönülür ki sonraki test temiz bir sahneden
            // başlasın; teardown'da assert yok, yalnız sınırlı bekleme.
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi)
                {
                    yield return null;
                }
            }

            _director = null;
            _rig = null;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        // ============================================================== yardımcılar (LifecycleTestKit kalıbı; o assembly'e erişim yok)

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float baslangic = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - baslangic > saniye)
                {
                    Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                }

                yield return null;
            }
        }

        private static IEnumerator SahneyiBekle(string ad)
        {
            yield return Bekle(
                () => SceneManager.GetActiveScene().name == ad && SceneManager.GetActiveScene().isLoaded,
                SahneZamanAsimi,
                "'" + ad + "' sahnesi etkin hale gelmedi. Şu an: '" + SceneManager.GetActiveScene().name + "'");
        }

        private static IEnumerator BootTanMainMenuye()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
        }

        /// <summary>Boot → MainMenu → pilot; bootstrap raporu gelene kadar bekler ve döner.</summary>
        private IEnumerator PilotSahneyiYukle()
        {
            yield return BootTanMainMenuye();
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil; gerçek profil risk altında.");

            // WP-015 (ADR-015-2): the pilot is a dev-only (disabled) Build Settings entry → by path.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ArtPipelinePaths.PilotScene, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PilotSahneAdi && SceneManager.GetActiveScene().isLoaded,
                KurulumZamanAsimi, "Pilot sahne etkin olmadı (Build Settings'te dev-only giriş var mı?). Şu an: '" + SceneManager.GetActiveScene().name + "'");

            // Statik Last önceki yüklemeden kalmış olabilir; yok edilmiş bir kök UnityEngine.Object
            // karşılaştırmasında null okunur, bu yüzden "Root != null" taze raporu bekler.
            yield return Bekle(() => PilotSceneBootstrap.Last.Root != null, KurulumZamanAsimi, "PilotSceneBootstrap raporu gelmedi (Awake koşmadı?).");
        }

        // ============================================================== sabit poz yakalamaları (1080p)

        /// <summary>
        /// Görsel regresyon / Presentation A karşılaştırması için sekiz standart pozun 1920×1080
        /// yakalaması. Çıkış klasörü DD_ART_CAPTURE_DIR ortam değişkeni (kapı betiği verir) yoksa
        /// geçici önbellek. Kanıt sınıfı: Editor PlayMode, gerçek Metal, MSAA 1 (RT), post-fx kapalı.
        /// Kabul: dosya var, siyaha karşı fark &gt; 0 (render gerçekten oldu), kendine karşı 0.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator PilotSahne_StandartPozlar_1080pYakalama()
        {
            yield return PilotSahneyiYukle();
            yield return Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "Oyuncu/kamera oluşmadı.");
            // Işık/gölge ve LOD cross-fade yerleşsin.
            for (int i = 0; i < 10; i++) yield return null;

            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Application.temporaryCachePath, "wp011-captures");
            }

            Directory.CreateDirectory(dir);
            var siyah = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var px = new Color32[1920 * 1080];
            siyah.SetPixels32(px);
            siyah.Apply();

            var satirlar = new List<string>();
            try
            {
                foreach (ArtVisualRegression.CameraPose poz in ArtVisualRegression.StandardPoses)
                {
                    string yol = Path.Combine(dir, "pilot-" + poz.name + "-1920x1080.png");
                    ArtVisualRegression.CaptureToPng(poz, 1920, 1080, yol);
                    yield return null;
                    Assert.IsTrue(File.Exists(yol), "Yakalama yazılmadı: " + yol);

                    ArtVisualRegression.Diff kendi = ArtVisualRegression.ComparePng(yol, yol, 8);
                    Assert.AreEqual(0.0, kendi.ChangedFraction, 1e-9, poz.name + ": kendine karşı fark 0 olmalı.");

                    byte[] bytes = File.ReadAllBytes(yol);
                    var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    tex.LoadImage(bytes);
                    ArtVisualRegression.Diff vsSiyah = ArtVisualRegression.Compare(tex, siyah, 8);
                    UnityEngine.Object.DestroyImmediate(tex);
                    Assert.Greater(vsSiyah.MeanAbs, 1.0, poz.name + ": kare siyah (render olmadı?).");
                    satirlar.Add(poz.name + " meanVsBlack=" + vsSiyah.MeanAbs.ToString("F1", CultureInfo.InvariantCulture) + " bytes=" + bytes.Length);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(siyah);
            }

            Debug.Log("[DD-TEST] wp011.pilot.captures dir=" + dir + "\n  " + string.Join("\n  ", satirlar));
        }

        // ============================================================== LOD geçiş mesafeleri (ölçüm)

        /// <summary>
        /// LOD eşikleri kamera geometrisinden türetildi (32 / 52 / 138 m, en büyük kenar ölçüsü,
        /// Quality lodBias 2 ile etkin mesafe ×2). Bu test her aile prefabı için kamerayı artan
        /// mesafelere koyar, bir kare render eder ve hangi LOD renderer'ının görünür olduğunu okur
        /// (Unity'de "aktif LOD" API'si yoktur; Renderer.isVisible cull sonrası doğru kaynaktır).
        /// Kabul: yakında LOD0 görünür, seviye mesafeyle monoton artar; ölçülen geçiş mesafeleri
        /// rapora (ADR-011-3 adayı) gider — sayı burada kilitlenmez.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator LodGecisleri_KameraMesafesiyleOlculur_Monoton()
        {
            yield return PilotSahneyiYukle();
            yield return Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "Oyuncu/kamera oluşmadı.");

            var library = PilotSceneBootstrap.Last.Library;
            Assert.IsNotNull(library, "Bootstrap kütüphane referansı yok.");
            float bias = QualitySettings.lodBias;
            float[] mesafeler = { 6f, 12f, 20f, 28f, 36f, 44f, 56f, 64f, 80f, 100f, 120f, 140f, 200f, 260f };

            // Ölçüm kamerası: pilot kamera rig'inden bağımsız, 42° FOV, far 300. Prefab uzak bir
            // boş noktada (dilim dışı, y=0) tek başına örneklenir; ana kamera kapatılmaz (isVisible
            // herhangi bir kameraya göre true olur), bu yüzden ana kamera geçici olarak devre dışı.
            Camera ana = Camera.main;
            bool anaAcik = ana != null && ana.enabled;
            if (ana != null) ana.enabled = false;
            var camGo = new GameObject("LodMeasureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            var hatalar = new List<string>();
            var rapor = new List<string>();
            try
            {
                foreach (string aile in ArtFamilies.All)
                {
                    GameObject prefab = library.Find(aile);
                    Assert.IsNotNull(prefab, aile + " prefabı kütüphanede yok.");
                    GameObject ornek = UnityEngine.Object.Instantiate(prefab, new Vector3(-70f, 0f, -70f), Quaternion.identity);
                    var grup = ornek.GetComponent<LODGroup>();
                    // Ölçüm örneğinde cross-fade kapatılır: geçiş sırasında iki LOD birden çizilir ve
                    // isVisible ikisinde de true okunur; eşik ölçümü saf seçimi ister. Sahnedeki
                    // prefab/instance'lar CrossFade'de kalır (sözleşme), yalnız bu kopya değişir.
                    grup.fadeMode = LODFadeMode.None;
                    grup.animateCrossFading = false;
                    Renderer[] lod = new Renderer[3];
                    for (int i = 0; i < 3; i++)
                    {
                        Transform t = ornek.transform.Find("Geometry/LOD" + i);
                        lod[i] = t != null ? t.GetComponent<Renderer>() : null;
                    }

                    Vector3 merkez = grup.transform.TransformPoint(grup.localReferencePoint);
                    var seviyeler = new List<string>();
                    int onceki = -1;
                    for (int d = 0; d < mesafeler.Length; d++)
                    {
                        cam.transform.position = merkez + new Vector3(0f, 0.35f * mesafeler[d], -mesafeler[d]).normalized * mesafeler[d];
                        cam.transform.LookAt(merkez);
                        // WaitForEndOfFrame -batchmode'da hiç dönmez (Unity sınırı); kamera açıkça
                        // render edilir, cull sonucu (isVisible) bir sonraki karede okunur.
                        for (int f = 0; f < 3; f++)
                        {
                            cam.Render();
                            yield return null;
                        }
                        int seviye = -1;
                        for (int i = 0; i < 3; i++)
                        {
                            if (lod[i] != null && lod[i].isVisible) { seviye = i; break; }
                        }

                        seviyeler.Add(mesafeler[d].ToString("0", CultureInfo.InvariantCulture) + "m:" + (seviye < 0 ? "cull" : "LOD" + seviye));
                        int sira = seviye < 0 ? 3 : seviye;
                        if (sira < onceki)
                        {
                            hatalar.Add(aile + ": LOD seviyesi mesafeyle geri gitti (" + string.Join(" ", seviyeler) + ")");
                        }

                        onceki = sira;
                        if (d == 0 && seviye != 0)
                        {
                            hatalar.Add(aile + ": 6 m'de LOD0 beklenir, " + (seviye < 0 ? "cull" : "LOD" + seviye));
                        }
                    }

                    rapor.Add(aile + " size=" + grup.size.ToString("0.00", CultureInfo.InvariantCulture) + " -> " + string.Join(" ", seviyeler));
                    UnityEngine.Object.Destroy(ornek);
                    yield return null;
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(camGo);
                if (ana != null) ana.enabled = anaAcik;
            }

            Debug.Log("[DD-TEST] wp011.lod.measure lodBias=" + bias.ToString("0.00", CultureInfo.InvariantCulture) + " fov=42\n  " + string.Join("\n  ", rapor));
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
        }

        private static ArtVisualRegression.CameraPose Poz(string ad)
        {
            foreach (ArtVisualRegression.CameraPose p in ArtVisualRegression.StandardPoses)
            {
                if (p.name == ad) return p;
            }

            return null;
        }

        private static string Yol(Transform t, Transform kok)
        {
            string yol = t.name;
            for (Transform p = t.parent; p != null && p != kok; p = p.parent)
            {
                yol = p.name + "/" + yol;
            }

            return yol;
        }

        // ============================================================== P1 · yükleme, sayımlar, 40 düşman, yakalama

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator PilotSahne_823Instance_NavMeshParite_40Dusman_Yakalama()
        {
            yield return PilotSahneyiYukle();

            PilotSceneBootstrap.Result r = PilotSceneBootstrap.Last;
            Debug.Log("[DD-TEST] wp011.pilot.report ok=" + r.Ok + " instances=" + r.Stats.Instances + " missing=" + r.Stats.Missing +
                      " renderers=" + r.Stats.Renderers + " lodGroups=" + r.Stats.LodGroups + " colliders=" + r.Stats.Colliders +
                      " triangles=" + r.Stats.Triangles + " navTris=" + r.NavMeshTriangles + " unreachable=" + r.Unreachable + "\n" + r.Report);

            Assert.IsTrue(r.Ok, "[Pilot] bootstrap Ok değil:\n" + r.Report);
            Assert.AreEqual(BeklenenAileOrnegi, r.Stats.Instances, "Spawn edilen aile instance sayısı.");
            Assert.AreEqual(0, r.Stats.Missing, "Kütüphanede prefabı bulunamayan giriş sayısı.");
            Assert.Greater(r.Stats.Renderers, 0);
            Assert.AreEqual(BeklenenAileOrnegi, r.Stats.LodGroups, "Her instance bir LODGroup taşır.");
            Assert.Greater(r.NavMeshTriangles, 0, "NavMesh üçgeni 0: bake koşmadı ya da collider katmanı yanlış.");
            Assert.AreEqual(0, r.Unreachable, "Erişilemeyen nokta var (12 spawn + 12 silah noktası paritesi bozuk):\n" + r.Report);

            // Sahnede gerçekten var mı — rapor sayısı ile sahnedeki kök çapraz kontrol.
            Transform sliceKoku = r.Root.transform.Find(GoldenSliceSpawner.RootName);
            if (sliceKoku == null)
            {
                GameObject bulunan = GameObject.Find(GoldenSliceSpawner.RootName);
                sliceKoku = bulunan != null ? bulunan.transform : null;
            }

            Assert.IsNotNull(sliceKoku, "'" + GoldenSliceSpawner.RootName + "' kökü sahnede yok.");
            // Her instance kökü bir LODGroup taşır; sayım hiyerarşi düzenine (düz/aile grubu) bağlı değildir.
            Assert.AreEqual(BeklenenAileOrnegi, sliceKoku.GetComponentsInChildren<LODGroup>(true).Length, "[GoldenSlice] altındaki instance (LODGroup) sayısı rapordan farklı.");

            // Kamera, oyuncu, director, koşu.
            yield return Bekle(() => Camera.main != null, OyuncuZamanAsimi, "Camera.main oluşmadı (rig fabrikası koşmadı?).");
            yield return Bekle(() => PlayerRegistry.Primary != null, OyuncuZamanAsimi, "Oyuncu doğmadı (PlayerSpawner / WorldMarkers).");
            yield return Bekle(() => EnemySpawnDirector.Instance != null, KurulumZamanAsimi, "EnemySpawnDirector yok (RunSystemsInstaller koşmadı?).");
            yield return Bekle(() => RunDirector.Instance != null, KurulumZamanAsimi, "RunDirector yok.");
            yield return Bekle(() => WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "Silah bağlanmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");

            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            Assert.IsNotNull(_rig.Health, "Oyuncu sağlığı yok.");
            _rig.Health.DiagnosisInvulnerable = true; // 40 düşman altında ölüm → Results geçişi testi bozar

            Assert.IsNotNull(PrototypeCameraRig.Active, "PrototypeCameraRig.Active yok.");

            // 40 düşman: tavan config'ten (RoundProbe "Force Spawn to 40" ile aynı çağrı).
            int tavan = _director.SafetyCeiling;
            Assert.Greater(tavan, 0, "SafetyCeiling 0.");
            _director.ForceActiveTarget(tavan, true);
            float t0 = Time.realtimeSinceStartup;
            yield return Bekle(() => _director.ActiveCount >= tavan, DolusZamanAsimi,
                "Aktif düşman tavana ulaşmadı: " + _director.ActiveCount + "/" + tavan);
            float dolusSure = Time.realtimeSinceStartup - t0;
            Assert.GreaterOrEqual(_director.ActiveCount, tavan);
            Assert.LessOrEqual(_director.ActiveCount, tavan, "Tavan aşıldı: " + _director.ActiveCount + "/" + tavan);
            Debug.Log("[DD-TEST] wp011.pilot.spawn active=" + _director.ActiveCount + "/" + tavan + " fillSeconds=" + dolusSure.ToString("0.00", CultureInfo.InvariantCulture));

            // Görsel yakalama: aynı dosya kendisiyle 0; siyah kareye karşı > 0 (render gerçekten oldu).
            ArtVisualRegression.CameraPose poz = Poz("gameplay_courtyard");
            Assert.IsNotNull(poz, "'gameplay_courtyard' pozu StandardPoses'ta yok.");
            string png = Path.Combine(Application.temporaryCachePath, "wp011-pilot-gameplay_courtyard.png");
            if (File.Exists(png)) File.Delete(png);

            // WaitForEndOfFrame değil: batchmode koşusunda Game view render etmez ve o yield asla dönmez.
            yield return null;
            ArtVisualRegression.CaptureToPng(poz, YakalamaGenislik, YakalamaYukseklik, png);
            Assert.IsTrue(File.Exists(png), "Yakalama dosyası yazılmadı: " + png);
            Assert.Greater(new FileInfo(png).Length, 1000L, "Yakalama dosyası anlamsız küçük: " + png);

            ArtVisualRegression.Diff kendisi = ArtVisualRegression.ComparePng(png, png, 8);
            Assert.AreEqual(YakalamaGenislik, kendisi.Width);
            Assert.AreEqual(YakalamaYukseklik, kendisi.Height);
            Assert.AreEqual(0d, kendisi.ChangedFraction, 0d, "Kendisiyle karşılaştırma 0 olmalı.");
            Assert.AreEqual(0d, kendisi.MeanAbs, 0d);

            Texture2D yakalanan = null;
            Texture2D siyah = null;
            try
            {
                yakalanan = new Texture2D(2, 2, TextureFormat.RGB24, false);
                Assert.IsTrue(yakalanan.LoadImage(File.ReadAllBytes(png), false), "PNG çözülemedi.");
                siyah = new Texture2D(yakalanan.width, yakalanan.height, TextureFormat.RGB24, false);
                var px = new Color32[yakalanan.width * yakalanan.height];
                siyah.SetPixels32(px);
                siyah.Apply(false, false);

                ArtVisualRegression.Diff pozitif = ArtVisualRegression.Compare(yakalanan, siyah, 8);
                Assert.Greater(pozitif.ChangedFraction, 0.05d, "Yakalama neredeyse tümüyle siyah: kamera hiçbir şey render etmedi (" + pozitif + ").");
                Debug.Log("[DD-TEST] wp011.pilot.capture path=" + png + " vsBlack=" + pozitif);
            }
            finally
            {
                if (yakalanan != null) UnityEngine.Object.Destroy(yakalanan);
                if (siyah != null) UnityEngine.Object.Destroy(siyah);
            }
        }

        // ============================================================== P2 · occluder katmanı ve saydamlaştırıcı

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator PilotSahne_OccluderKatmani_GercekPrefabta_FaderRigde()
        {
            yield return PilotSahneyiYukle();
            PilotSceneBootstrap.Result r = PilotSceneBootstrap.Last;
            Assert.IsTrue(r.Ok, "[Pilot] bootstrap Ok değil:\n" + r.Report);

            int occ = PrototypeLayers.CameraOccluder;
            int env = PrototypeLayers.Environment;
            Assert.IsTrue(occ >= 0 && env >= 0, "Environment/CameraOccluder katmanları tanımlı değil.");

            Transform sliceKoku = r.Root.transform.Find(GoldenSliceSpawner.RootName);
            if (sliceKoku == null)
            {
                GameObject bulunan = GameObject.Find(GoldenSliceSpawner.RootName);
                sliceKoku = bulunan != null ? bulunan.transform : null;
            }

            Assert.IsNotNull(sliceKoku, "'" + GoldenSliceSpawner.RootName + "' kökü sahnede yok.");
            LODGroup[] instanceKokleri = sliceKoku.GetComponentsInChildren<LODGroup>(true);
            Assert.Greater(instanceKokleri.Length, 0, "[GoldenSlice] boş.");

            // Her instance: kök katmanı Environment ya da CameraOccluder; bütün çocuklar (LOD
            // renderer'ları ve COL_ collider'ları) kökle aynı katmanda. Aile → katman eşlemesi
            // EditMode'da (ArtPrefabTests.Katman_…) kanıtlanır; burada sahnedeki gerçek
            // instance'ların o prefab'ı bozmadan taşıdığı ölçülür.
            var hatalar = new List<string>();
            int occluderInstance = 0;
            int occluderCollider = 0;
            Collider ornekOccluderCollider = null;
            Renderer ornekOccluderRenderer = null;

            for (int i = 0; i < instanceKokleri.Length; i++)
            {
                Transform inst = instanceKokleri[i].transform;
                int kok = inst.gameObject.layer;
                if (kok != occ && kok != env)
                {
                    if (hatalar.Count < 30) hatalar.Add(inst.name + ": kök katmanı " + LayerMask.LayerToName(kok) + " (Environment/CameraOccluder beklenir).");
                    continue;
                }

                foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                {
                    if (t.gameObject.layer != kok && hatalar.Count < 30)
                    {
                        hatalar.Add(inst.name + "/" + t.name + ": katman " + LayerMask.LayerToName(t.gameObject.layer) + ", kök " + LayerMask.LayerToName(kok));
                    }
                }

                if (kok != occ)
                {
                    continue;
                }

                occluderInstance++;
                foreach (Collider c in inst.GetComponentsInChildren<Collider>(true))
                {
                    if (c.gameObject.layer == occ)
                    {
                        occluderCollider++;
                        if (ornekOccluderCollider == null) ornekOccluderCollider = c;
                    }
                }

                if (ornekOccluderRenderer == null)
                {
                    foreach (Renderer rr in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        if (rr.gameObject.layer == occ) { ornekOccluderRenderer = rr; break; }
                    }
                }
            }

            Assert.IsEmpty(hatalar, "Katman hataları (ilk 30):\n" + string.Join("\n", hatalar));
            Assert.Greater(occluderInstance, 0, "Sahnede CameraOccluder katmanında hiç instance yok (AP-001: 36 occluder adayı instance).");
            Assert.IsNotNull(ornekOccluderRenderer, "CameraOccluder katmanında renderer yok.");
            Assert.AreEqual(occ, ornekOccluderRenderer.gameObject.layer);
            Assert.Greater(occluderCollider, 0, "CameraOccluder katmanında hiç collider yok: sphere cast hiçbir şeye çarpmaz, saydamlaşma asla tetiklenmez.");

            // Fader'ın Resolve yolu (CameraOcclusionFader.Resolve): önce çarpılan collider'ın
            // ebeveyn zincirinde OccluderFadeState aranır; yoksa collider'ın kendinde/ebeveyninde
            // renderer bulunmalıdır. SM_ prefab'larında COL_i ve LOD_n kardeş olduğu için
            // ArtFamilyPrefabBuilder occluder ailelerinin köküne kapalı bir OccluderFadeState
            // koyar — Resolve onu bulur ve kökün altındaki LOD renderer'larını soldurur.
            var onceden = ornekOccluderCollider.GetComponentInParent<OccluderFadeState>();
            Assert.IsTrue(onceden != null || ornekOccluderCollider.GetComponentInChildren<Renderer>(true) != null || ornekOccluderCollider.GetComponentInParent<Renderer>() != null,
                "Occluder collider'ı (" + ornekOccluderCollider.name + ") için ne ebeveyninde OccluderFadeState ne de kendinde/ebeveyninde renderer var; CameraOcclusionFader.Resolve null döner.");
            if (onceden != null)
            {
                Assert.IsFalse(onceden.enabled, "Önceden yerleştirilen OccluderFadeState soldurma yokken kapalı olmalı (sıfır maliyet).");
                Assert.Greater(onceden.GetComponentsInChildren<Renderer>(true).Length, 0, "OccluderFadeState'in altında soldurulacak renderer yok.");
            }

            // Rig ve saydamlaştırıcı bileşeni.
            yield return Bekle(() => PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "Kamera rig'i oluşmadı.");
            var fader = PrototypeCameraRig.Active.GetComponent<CameraOcclusionFader>();
            Assert.IsNotNull(fader, "Rig'de CameraOcclusionFader yok; REQ-035 saydamlaşma pilotta çalışmaz.");
            Assert.IsTrue(fader.enabled, "CameraOcclusionFader kapalı.");

            Debug.Log("[DD-TEST] wp011.pilot.occluder instances=" + occluderInstance + " colliders=" + occluderCollider +
                      " sampleRenderer=" + Yol(ornekOccluderRenderer.transform, sliceKoku) +
                      " sampleCollider=" + Yol(ornekOccluderCollider.transform, sliceKoku));
        }
    }
}
