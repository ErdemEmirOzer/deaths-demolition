using System;
using System.Collections;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.PlayerSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>Kamera testleri için sürülebilir girdi: imleç konumu ve sağ analog.</summary>
    internal sealed class KameraTestGirdisi : IInputSource
    {
        public AimSourceKind Tur = AimSourceKind.Pointer;
        public Vector2 Imlec;
        public Vector2 Analog;

        public Vector2 Hareket;

        public bool IsEnabled => true;
        public Vector2 Move => Hareket;
        public AimSourceKind AimKind => Tur;
        public Vector2 AimScreenPosition => Imlec;
        public Vector2 AimStick => Analog;
        public bool FireHeld => false;
        public bool FirePressed => false;
        public bool FireReleased => false;
        public bool ReloadPressed => false;
        public bool PausePressed => false;
        public bool DebugPanelPressed => false;
#pragma warning disable 67
        public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
        public void Enable() { }
        public void Disable() { }
    }

    /// <summary>
    /// WP-002 · KAMERA SÖZLEŞMESİ nöbetçisi. REQ-030 … REQ-035 · DEC-003.
    ///
    /// RUN SAHNESİ YOK: rig'siz Main Menu sahnesi yüklenir; dünyanın çok dışında (Kok) zemin +
    /// kamera rig'i (CameraRigFactory) + takip hedefi kurulur. Rig gerçek PrototypeConfig'i
    /// Resources'tan okur. Ölçeksiz saat kullanan yollar (occlusion sondası) gerçek karelerle
    /// koşar: sabit yakalama adımı (captureDeltaTime) ölçeksiz zamanı ilerletmez.
    ///
    /// SAYILAR PROTOTYPE-ONLY — ÜRÜN KARARI DEĞİL
    /// -----------------------------------------
    /// Height 15, BackDistance 13, FOV 42, FollowSmoothTime 0.12, MaxAimLookAhead 2.5,
    /// OcclusionTargetAlpha 0.22 bugünkü placeholder değerlerdir; teste sabit yazıldılar.
    /// Kilitlenen şey sözleşmedir:
    ///   1. rotasyon dışarıdan yazılsa bile bir sonraki karede geri alınır; FOV/offset sabit;
    ///   2. takip yumuşak ve kare hızından (30/60/120) bağımsızdır;
    ///   3. fare ve sağ analog look-ahead'i aynı tavana (MaxAimLookAhead) vurur;
    ///   4. sarsıntı nişan noktasını oynatmaz ve bitince kalıntı bırakmaz;
    ///   5. araya giren büyük nesneler saydamlaşır, kalkınca geri döner; kamera yaklaşmaz.
    /// </summary>
    [TestFixture]
    public sealed class CameraContractTests
    {
        private const float Yukseklik = 15f;            // CameraConfigSection.Height
        private const float GeriMesafe = 13f;           // CameraConfigSection.BackDistance
        private const float Fov = 42f;                  // CameraConfigSection.FieldOfView
        private const float TakipSmooth = 0.12f;        // CameraConfigSection.FollowSmoothTime
        private const float MaxLookAhead = 2.5f;        // CameraConfigSection.MaxAimLookAhead
        private const float HedefAlfa = 0.22f;          // CameraConfigSection.OcclusionTargetAlpha
        private const float OdakYukseklik = 1f;         // PrototypeCameraRig._focusHeightOffset

        /// <summary>Ölçüm dünyanın (±44 m) çok dışında; hiçbir dünya nesnesi araya giremez.</summary>
        private static readonly Vector3 Kok = new Vector3(320f, 0f, 320f);

        private Scene _sahne;
        private GameObject _zemin;
        private GameObject _hedef;
        private PrototypeCameraRig _rig;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;

            // Bilinen, rig'siz bir sahne: önceki fixture'dan kalan bir Run sahnesi (ve onun
            // rig'i) ölçümü kirletmesin. Main Menu'de PrototypeCameraRig yoktur; ölçüm dünyanın
            // çok dışında (Kok) kurulur. (Sahne oluşturup init sahnesini boşaltmak test
            // koşucusunu askıya aldı — iter2; bu yüzden Single yükleme kalıbı.)
            SceneManager.LoadScene(DD.Prototype.Core.Boot.PrototypeScenes.MainMenu, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != DD.Prototype.Core.Boot.PrototypeScenes.MainMenu
                   && Time.realtimeSinceStartup - t0 < 20f)
            {
                yield return null;
            }

            Assert.AreEqual(DD.Prototype.Core.Boot.PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name, "Main Menu sahnesi yüklenemedi.");
            _sahne = SceneManager.GetActiveScene();
            yield return null;
            Assert.IsNull(PrototypeCameraRig.Active, "Main Menu sahnesinde bir kamera rig'i etkin; ölçüm kirlenir.");

            _zemin = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _zemin.name = "WP002-Zemin";
            _zemin.transform.position = Kok;
            _zemin.transform.localScale = new Vector3(20f, 1f, 20f);
            _zemin.layer = PrototypeLayers.Environment;

            _hedef = new GameObject("WP002-Hedef");
            _hedef.transform.position = Kok;

            _rig = CameraRigFactory.EnsureRig(_hedef.transform);
            Assert.IsNotNull(_rig);
            Time.captureDeltaTime = 1f / 60f;

            // Sabit tazelik nöbetçisi (sayı kilitlemez; sapınca "türetmeyi yenile").
            DD.Prototype.Core.Config.CameraConfigSection cam = DD.Prototype.Core.Config.PrototypeConfig.Active.Camera;
            const string not = " — test sabiti config'ten saptı; türetmeyi yenile (PrototypeOnly).";
            Assert.AreEqual(Yukseklik, cam.Height, 0.0001f, "Camera.Height" + not);
            Assert.AreEqual(GeriMesafe, cam.BackDistance, 0.0001f, "Camera.BackDistance" + not);
            Assert.AreEqual(Fov, cam.FieldOfView, 0.0001f, "Camera.FieldOfView" + not);
            Assert.AreEqual(TakipSmooth, cam.FollowSmoothTime, 0.0001f, "Camera.FollowSmoothTime" + not);
            Assert.AreEqual(MaxLookAhead, cam.MaxAimLookAhead, 0.0001f, "Camera.MaxAimLookAhead" + not);
            Assert.AreEqual(HedefAlfa, cam.OcclusionTargetAlpha, 0.0001f, "Camera.OcclusionTargetAlpha" + not);

            // Isınma: ilk LateUpdate odağı hedefe oturtur.
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Yik()
        {
            Time.captureDeltaTime = 0f;
            CameraShakeService.StopAll();

            if (_rig != null)
            {
                UnityEngine.Object.Destroy(_rig.gameObject);
            }

            if (_hedef != null) UnityEngine.Object.Destroy(_hedef);
            if (_zemin != null) UnityEngine.Object.Destroy(_zemin);
            _rig = null;
            _hedef = null;
            _zemin = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        private static IEnumerator Kareler(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        private Vector3 BeklenenSabitOfset()
        {
            // Yaw 0: offset = (0, Height, -BackDistance)
            return new Vector3(0f, Yukseklik, -GeriMesafe);
        }

        // ================================================================ REQ-031 / REQ-034

        /// <summary>
        /// Rotasyon dışarıdan yazılır; bir kare sonra rig sabit rotasyonu yeniden dayatmış olmalı.
        /// FOV ve odak ofseti hedef hareket ederken değişmez (zoom yok).
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SabitAciVeYon_DisYazmaGeriAlinir_ZoomDegismez()
        {
            Quaternion sabit = _rig.StableRotation;
            Vector3 ofset = _rig.StablePosition - (_hedef.transform.position + Vector3.up * OdakYukseklik);
            Assert.AreEqual(0f, Vector3.Distance(ofset, BeklenenSabitOfset()), 0.05f,
                "Odak ofseti (0, " + Yukseklik + ", -" + GeriMesafe + ") değil: " + ofset);

            _rig.transform.rotation = Quaternion.Euler(10f, 45f, 5f);
            yield return null;
            Assert.Less(Quaternion.Angle(_rig.StableRotation, sabit), 0.01f,
                "REQ-031: dışarıdan yazılan rotasyon bir sonraki karede geri alınmadı.");

            float fov0 = _rig.Camera.fieldOfView;
            Assert.AreEqual(Fov, fov0, 0.01f);

            // Hedef 3 sn boyunca koşar; FOV ve ofset sabit kalır.
            float enBuyukOfsetSapma = 0f;
            for (int i = 0; i < 180; i++)
            {
                _hedef.transform.position += new Vector3(6.2f, 0f, 0f) * Time.deltaTime;
                yield return null;

                Assert.AreEqual(fov0, _rig.Camera.fieldOfView, 0.0001f, "REQ-034: FOV değişti.");
                Assert.Less(Quaternion.Angle(_rig.StableRotation, sabit), 0.01f, "Rotasyon sürüklendi.");

                Vector3 o = _rig.StablePosition - (_hedef.transform.position + Vector3.up * OdakYukseklik);
                // Yatay gecikme takipten gelir; DİKEY ve DERİNLİK (y, z) ofseti sabittir.
                enBuyukOfsetSapma = Mathf.Max(enBuyukOfsetSapma,
                    Mathf.Abs(o.y - Yukseklik) + Mathf.Abs(o.z + GeriMesafe));
            }

            Assert.Less(enBuyukOfsetSapma, 0.05f,
                "REQ-034: takip sırasında kamera-odak mesafesi (yükseklik/geri) değişti: " + enBuyukOfsetSapma);
        }

        // ================================================================ REQ-032

        /// <summary>
        /// Hedef 1 sn boyunca 6.2 m/s ile koşar, sonra durur ve 1 sn beklenir. 30/60/120 fps'te
        /// (a) kamera hiç sıçramaz (kare başına yer değiştirme ≤ hedef hızının 1.5 katı × dt),
        /// (b) durduktan 1 sn sonra odak hedefe oturmuştur (|fark| &lt; 2 cm — SmoothDamp 0.12 s
        /// ile 1 s'de 8 zaman sabiti; e^-8 ≈ 0.0003 × 6.2 m gecikme ≈ 2 mm),
        /// (c) hareket içi kararlı gecikme formülle V·(τ − dt/2)'dir: SmoothDamp'ın exp
        /// yaklaşımı ≤ %0.2 sapar, baskın terim kare SONUNDA örneklemeden gelen V·dt/2'dir
        /// (30/60/120 fps → 0.640 / 0.692 / 0.718 m). Ölçülen tepe gecikme bu formüle
        /// %3·V·τ (= 0.022 m) payla, uç-örnekleme düzeltilmiş üç değer ise birbirine %2 içinde
        /// vurmalıdır — kare hızına bağımlılık yalnız bu bilinen terim kadardır.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator YumusakTakip_SicramazVe_KareHizindanBagimsiz()
        {
            float[] adimlar = { 1f / 30f, 1f / 60f, 1f / 120f };
            float[] tepeGecikme = new float[3];
            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] kamera.takip (SmoothDamp " + TakipSmooth + " s)");

            for (int a = 0; a < adimlar.Length; a++)
            {
                float dt = adimlar[a];
                Time.captureDeltaTime = dt;

                // Yeniden hizala: hedef köke, odak ısınsın.
                _hedef.transform.position = Kok;
                _rig.FollowTarget = _hedef.transform;   // _warmedUp = false → anında oturur
                yield return null;
                yield return null;

                Vector3 oncekiKamera = _rig.StablePosition;
                float enBuyukKareAdimi = 0f;
                float enBuyukGecikme = 0f;
                int kosuKare = Mathf.RoundToInt(1f / dt);

                for (int i = 0; i < kosuKare; i++)
                {
                    _hedef.transform.position += new Vector3(6.2f, 0f, 0f) * dt;
                    yield return null;

                    float kareAdimi = Vector3.Distance(_rig.StablePosition, oncekiKamera);
                    oncekiKamera = _rig.StablePosition;
                    enBuyukKareAdimi = Mathf.Max(enBuyukKareAdimi, kareAdimi);

                    Vector3 odak = _rig.StablePosition - BeklenenSabitOfset() - Vector3.up * OdakYukseklik;
                    enBuyukGecikme = Mathf.Max(enBuyukGecikme, Vector3.Distance(odak, _hedef.transform.position));
                }

                Assert.Less(enBuyukKareAdimi, 6.2f * 1.5f * dt + 0.001f,
                    "REQ-032: kamera " + (1f / dt) + " fps'te bir karede " + enBuyukKareAdimi.ToString("F3") +
                    " m sıçradı.");

                int beklemeKare = Mathf.RoundToInt(1f / dt);
                yield return Kareler(beklemeKare);

                Vector3 sonOdak = _rig.StablePosition - BeklenenSabitOfset() - Vector3.up * OdakYukseklik;
                float kalan = Vector3.Distance(sonOdak, _hedef.transform.position);
                Assert.Less(kalan, 0.02f,
                    "Hedef durduktan 1 sn sonra odak hâlâ " + kalan.ToString("F4") + " m geride (" + (1f / dt) + " fps).");

                tepeGecikme[a] = enBuyukGecikme;
                rapor.AppendLine("  " + (1f / dt).ToString("F0") + " fps: tepe gecikme=" +
                                 enBuyukGecikme.ToString("F4") + " m  en buyuk kare adimi=" +
                                 enBuyukKareAdimi.ToString("F4") + " m  kalan=" + kalan.ToString("F4") + " m");
            }

            Debug.Log(rapor.ToString());

            float[] duzeltilmis = new float[3];
            for (int a = 0; a < adimlar.Length; a++)
            {
                float beklenen = 6.2f * (TakipSmooth - adimlar[a] * 0.5f);
                Assert.AreEqual(beklenen, tepeGecikme[a], 0.03f * 6.2f * TakipSmooth,
                    "REQ-032: " + (1f / adimlar[a]).ToString("F0") + " fps tepe gecikmesi " + tepeGecikme[a].ToString("F4") +
                    " m, formül V·(τ − dt/2) = " + beklenen.ToString("F4") + " m.\n" + rapor);
                duzeltilmis[a] = tepeGecikme[a] + 6.2f * adimlar[a] * 0.5f;
            }

            float enAz = Mathf.Min(duzeltilmis[0], Mathf.Min(duzeltilmis[1], duzeltilmis[2]));
            float enCok = Mathf.Max(duzeltilmis[0], Mathf.Max(duzeltilmis[1], duzeltilmis[2]));
            Assert.Greater(enAz, 0.1f, "Takip gecikmesi ölçülemedi (odak hedefe yapışık); ölçüm boş.");
            Assert.Less((enCok - enAz) / enAz, 0.02f,
                "REQ-032: takip gecikmesi (uç-örnekleme düzeltilmiş) kare hızına bağlı: 30/60/120 fps " +
                duzeltilmis[0].ToString("F4") + " / " + duzeltilmis[1].ToString("F4") + " / " +
                duzeltilmis[2].ToString("F4") + " m (sapma %" + ((enCok - enAz) / enAz * 100f).ToString("F1") + ").\n" + rapor);
        }

        // ================================================================ REQ-033 / REQ-040

        /// <summary>
        /// Fare ekranın köşesinde ve sağ analog tam itilmiş: ikisi de MaxAimLookAhead'e
        /// (2.5 m) vurur, hiçbiri aşmaz; sıfır girdide kadraj merkeze döner.
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator LookAhead_FareVeAnalog_AyniTavanaVurur()
        {
            var aimGo = new GameObject("WP002-Aim");
            aimGo.transform.position = Kok;
            var girdi = new KameraTestGirdisi { Tur = AimSourceKind.Pointer, Imlec = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) };
            PlayerAim aim = aimGo.AddComponent<PlayerAim>();
            aim.SetInputSource(girdi);
            _rig.SetLookAheadProvider(aim);
            _rig.FollowTarget = aimGo.transform;

            try
            {
                yield return Kareler(90);
                float merkez = LookAheadBuyuklugu(aimGo.transform.position);
                Assert.Less(merkez, 0.35f, "İmleç ekran merkezindeyken kadraj kayması " + merkez.ToString("F3") + " m.");

                // Fare: sağ üst köşe.
                girdi.Imlec = new Vector2(Screen.width - 1f, Screen.height - 1f);
                yield return Kareler(120);   // 0.18 s smooth için 2 s bol
                Assert.IsTrue(aim.HasAimPoint, "Köşedeki imleç nişan düzlemine düşmedi.");
                float fare = LookAheadBuyuklugu(aimGo.transform.position);

                // Analog: tam itilmiş.
                girdi.Tur = AimSourceKind.Stick;
                girdi.Analog = new Vector2(0.7071f, 0.7071f);
                yield return Kareler(120);
                float analog = LookAheadBuyuklugu(aimGo.transform.position);

                girdi.Analog = Vector2.zero;
                girdi.Tur = AimSourceKind.Pointer;
                girdi.Imlec = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                yield return Kareler(120);
                float geri = LookAheadBuyuklugu(aimGo.transform.position);

                Debug.Log("[DD-TEST] kamera.lookahead merkez=" + merkez.ToString("F3") + " fare-kose=" + fare.ToString("F3") +
                          " analog-tam=" + analog.ToString("F3") + " geri=" + geri.ToString("F3") + " (tavan " + MaxLookAhead + ")");

                Assert.LessOrEqual(fare, MaxLookAhead + 0.02f, "REQ-033: fare kadraj kayması tavanı aştı: " + fare);
                Assert.LessOrEqual(analog, MaxLookAhead + 0.02f, "REQ-040: analog kadraj kayması tavanı aştı: " + analog);
                Assert.AreEqual(MaxLookAhead, analog, 0.05f, "Tam itilmiş analog tavana oturmalı: " + analog);
                Assert.Greater(fare, MaxLookAhead * 0.9f, "Köşedeki imleç tavana yaklaşmalı (clamp aynı sözleşme): " + fare);
                Assert.Less(geri, 0.35f, "Girdi merkeze dönünce kadraj merkeze dönmedi: " + geri);
            }
            finally
            {
                _rig.SetLookAheadProvider(null);
                _rig.FollowTarget = _hedef.transform;
                UnityEngine.Object.Destroy(aimGo);
            }
        }

        private float LookAheadBuyuklugu(Vector3 hedefKonum)
        {
            Vector3 odak = _rig.StablePosition - BeklenenSabitOfset() - Vector3.up * OdakYukseklik;
            Vector3 d = odak - hedefKonum;
            d.y = 0f;
            return d.magnitude;
        }

        // ================================================================ REQ-036 / REQ-040 (kamera tabanı)

        /// <summary>
        /// Kamera yaw'ı 90°'ye çevrilir (sahne yazarlık değeri; yansıtma ile — üretimde girdi
        /// yolu YOK). WASD (0,1) girdisi artık dünya +z'ye değil kameranın düzlem ileri
        /// yönüne (+x) götürür; sağ analog (0,1) da aynı tabana göre nişan alır. Yaw 0'da
        /// dünya-göreli ile kamera-göreli aynı sonucu verdiğinden bu ayrım şimdiye kadar hiç
        /// ölçülmemişti. Ayrıca çapraz girdi hız tavanını aşmaz (ölçülen hız ≤ 6.2 m/s).
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator KameraYawIle_HareketVeAnalogNisan_KameraTabaninaGore()
        {
            System.Reflection.FieldInfo yawAlani = typeof(PrototypeCameraRig).GetField(
                "_yawDegrees", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(yawAlani, "PrototypeCameraRig._yawDegrees alanı bulunamadı; test kurulumu güncellenmeli.");

            var oyuncu = new GameObject("WP002-Oyuncu");
            oyuncu.transform.position = Kok + new Vector3(0f, 0.05f, 0f);
            oyuncu.layer = PrototypeLayers.Player;
            PlayerMotor motor = oyuncu.AddComponent<PlayerMotor>();
            PlayerAim aim = oyuncu.AddComponent<PlayerAim>();
            var girdi = new KameraTestGirdisi { Tur = AimSourceKind.Stick };
            motor.SetInputSource(girdi);
            aim.SetInputSource(girdi);
            _rig.FollowTarget = oyuncu.transform;

            try
            {
                yawAlani.SetValue(_rig, 90f);
                DD.Prototype.Core.Config.PrototypeConfig.Active.NotifyChanged();   // ApplyConfig → taban yeniden hesaplanır
                yield return null;
                yield return null;

                Vector3 ileri = _rig.PlanarForward;
                Vector3 sag = _rig.PlanarRight;
                Assert.Greater(Vector3.Dot(ileri, Vector3.right), 0.99f, "Yaw 90'da kamera düzlem-ileri +x olmalı: " + ileri);
                Assert.Greater(Vector3.Dot(sag, Vector3.back), 0.99f, "Yaw 90'da kamera düzlem-sağ −z olmalı: " + sag);

                // ---- WASD ileri: 0.5 s → yer değiştirme kamera ileri yönünde (dünya +x).
                Vector3 basla = oyuncu.transform.position;
                girdi.Hareket = new Vector2(0f, 1f);
                yield return Kareler(30);
                girdi.Hareket = Vector2.zero;
                Vector3 yol = oyuncu.transform.position - basla;
                yol.y = 0f;
                Assert.Greater(yol.magnitude, 1.5f, "Oyuncu 0.5 s'de hareket etmedi: " + yol.magnitude);
                Assert.Greater(Vector3.Dot(yol.normalized, ileri), 0.99f,
                    "REQ-036: (0,1) girdisi kamera ileri yönüne (" + ileri + ") değil " + yol.normalized + " yönüne götürdü.");

                // ---- Çapraz girdi: hız tavanı aşılmaz.
                yield return Kareler(20);   // dur
                girdi.Hareket = new Vector2(1f, 1f);
                float enBuyukHiz = 0f;
                for (int i = 0; i < 40; i++)
                {
                    yield return null;
                    enBuyukHiz = Mathf.Max(enBuyukHiz, motor.PlanarVelocity.magnitude);
                }

                girdi.Hareket = Vector2.zero;
                Assert.LessOrEqual(enBuyukHiz, 6.2f + 0.01f, "Çapraz girdi hız tavanını aştı: " + enBuyukHiz.ToString("F3") + " m/s.");
                Assert.Greater(enBuyukHiz, 6.0f, "Çapraz girdide tam hıza ulaşılmadı (normalize hatası?): " + enBuyukHiz.ToString("F3"));

                // ---- Sağ analog (0,1): nişan kamera ileri yönünde; (1,0): kamera sağ yönünde.
                girdi.Analog = new Vector2(0f, 1f);
                yield return Kareler(30);
                Assert.Greater(Vector3.Dot(aim.AimDirection, ileri), 0.99f,
                    "REQ-040: sağ analog (0,1) nişanı kamera ileri yönüne (" + ileri + ") değil " + aim.AimDirection + " yönüne aldı.");
                girdi.Analog = new Vector2(1f, 0f);
                yield return Kareler(30);
                Assert.Greater(Vector3.Dot(aim.AimDirection, sag), 0.99f,
                    "REQ-040: sağ analog (1,0) nişanı kamera sağ yönüne almadı: " + aim.AimDirection);
                Assert.Greater(Vector3.Dot(oyuncu.transform.forward, sag), 0.99f, "Karakter analog nişan yönüne dönmedi.");
            }
            finally
            {
                yawAlani.SetValue(_rig, 0f);
                DD.Prototype.Core.Config.PrototypeConfig.Active.NotifyChanged();
                _rig.FollowTarget = _hedef.transform;
                UnityEngine.Object.Destroy(oyuncu);
            }
        }

        // ================================================================ REQ-126 (kamera tarafı)

        /// <summary>
        /// Sarsıntı sırasında imlecin dünya noktası oynamaz (shake-free poz); sarsıntı bitince
        /// shake düğümü tam sıfıra döner — takipte veya look-ahead'de kalıcı drift yok.
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Sarsinti_NisanNoktasiniOynatmaz_BitinceKalintiBirakmaz()
        {
            Vector2 ekranMerkez = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float duzlemY = Kok.y + 1.15f;

            Vector3 once;
            Assert.IsTrue(_rig.ScreenPointToAimPlane(ekranMerkez, duzlemY, out once), "Nişan projeksiyonu başarısız.");

            Transform shakeNode = _rig.Camera.transform;
            Vector3 stableOnce = _rig.StablePosition;

            CameraShakeService.Shake(0.2f, 0.4f, ShakeKind.Rumble);
            CameraShakeService.Shake(0.15f, 0.3f, ShakeKind.Punch, Vector3.right);

            float enBuyukShake = 0f;
            float enBuyukNisanSapma = 0f;
            float enBuyukHamSapma = 0f;   // sarsıntı telafisi OLMASAYDI (ham kamera ışını) oynama
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                enBuyukShake = Mathf.Max(enBuyukShake, shakeNode.localPosition.magnitude);

                Vector3 simdi;
                Assert.IsTrue(_rig.ScreenPointToAimPlane(ekranMerkez, duzlemY, out simdi));
                enBuyukNisanSapma = Mathf.Max(enBuyukNisanSapma, Vector3.Distance(simdi, once));

                Ray ham = _rig.Camera.ScreenPointToRay(ekranMerkez);
                if (Mathf.Abs(ham.direction.y) > 1e-5f)
                {
                    Vector3 hamNokta = ham.origin + ham.direction * ((duzlemY - ham.origin.y) / ham.direction.y);
                    enBuyukHamSapma = Mathf.Max(enBuyukHamSapma, Vector3.Distance(hamNokta, once));
                }
            }

            Debug.Log("[DD-TEST] kamera.sarsinti shake max=" + enBuyukShake.ToString("F3") + " m  nisan sapma telafili=" +
                      (enBuyukNisanSapma * 1000f).ToString("F2") + " mm  telafisiz=" + (enBuyukHamSapma * 1000f).ToString("F1") + " mm");

            Assert.Greater(enBuyukShake, 0.01f, "Sarsıntı hiç uygulanmadı; ölçüm boş.");
            Assert.Greater(enBuyukHamSapma, 0.05f, "Ham kamera ışını bile oynamadı; telafi ölçülemiyor (ölçüm boş).");
            // Telafi: shake-free pozdan yeniden kurulan ışın, ham ışının oynamasının en az %95'ini
            // yok eder ve mutlak sapma 1 cm'in altında kalır (float payı; dünya koordinatı ~450 m).
            Assert.Less(enBuyukNisanSapma, 0.01f,
                "REQ-126: sarsıntı imlecin dünya noktasını " + (enBuyukNisanSapma * 1000f).ToString("F2") + " mm oynattı.");
            Assert.Less(enBuyukNisanSapma, enBuyukHamSapma * 0.05f,
                "REQ-126: telafi ham oynamanın %95'ini yok etmiyor: telafili " + (enBuyukNisanSapma * 1000f).ToString("F2") +
                " mm, telafisiz " + (enBuyukHamSapma * 1000f).ToString("F1") + " mm.");

            yield return Kareler(60);   // 1 s: her iki sarsıntı da bitti
            Assert.AreEqual(0, CameraShakeService.ActiveCount, "Sarsıntılar bitmedi.");
            Assert.Less(shakeNode.localPosition.magnitude, 1e-5f, "Shake düğümü sıfıra dönmedi: " + shakeNode.localPosition);
            Assert.Less(Quaternion.Angle(shakeNode.localRotation, Quaternion.identity), 1e-3f, "Shake roll'u kaldı.");
            Assert.Less(Vector3.Distance(_rig.StablePosition, stableOnce), 1e-3f, "Sarsıntı kararlı pozu kaydırdı (drift).");
        }

        // ================================================================ REQ-035

        /// <summary>
        /// İki büyük nesne kamera ile hedefin arasına girer: ikisi de hedef alfaya yakın
        /// saydamlaşır, kamera hedefe YAKLAŞMAZ; nesneler kalkınca orijinal materyaller
        /// geri döner, fade bileşeni kapanır.
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Occlusion_IkiEngelSaydamlasir_KalkincaGeriDoner_KameraYaklasmaz()
        {
            int occLayer = LayerMask.NameToLayer(PrototypeLayers.CameraOccluderName);
            Assert.GreaterOrEqual(occLayer, 0, "CameraOccluder katmanı yok.");

            // Sonda aralığı ölçeksiz saatle atılır (pause'da da çalışsın diye); sabit yakalama
            // adımı ölçeksiz zamanı ilerletmez → bu test gerçek karelerle koşar.
            Time.captureDeltaTime = 0f;
            float unscaled0 = Time.unscaledTime;
            yield return null;
            yield return null;
            Assert.Greater(Time.unscaledTime - unscaled0, 0f, "Ölçeksiz saat ilerlemiyor; sonda hiç atılmaz.");

            Vector3 odak = _hedef.transform.position + Vector3.up * OdakYukseklik;
            Vector3 kamera = _rig.StablePosition;
            float mesafeOnce = Vector3.Distance(kamera, odak);

            GameObject duvar1 = Engel("WP002-Duvar1", Vector3.Lerp(kamera, odak, 0.5f), occLayer);
            GameObject duvar2 = Engel("WP002-Duvar2", Vector3.Lerp(kamera, odak, 0.7f), occLayer);
            Material orijinal1 = duvar1.GetComponent<Renderer>().sharedMaterial;
            Material orijinal2 = duvar2.GetComponent<Renderer>().sharedMaterial;

            try
            {
                Physics.SyncTransforms();
                float bekle0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - bekle0 < 0.75f)   // sonda 20 Hz + fade 0.15 s, gerçek zaman
                {
                    yield return null;
                }

                OccluderFadeState f1 = duvar1.GetComponent<OccluderFadeState>();
                OccluderFadeState f2 = duvar2.GetComponent<OccluderFadeState>();
                if (f1 == null)
                {
                    // Tanı: sonda geometrisini testin kendisi de atar.
                    Vector3 from = _rig.StablePosition;
                    Vector3 to = _hedef.transform.position + Vector3.up * 1.1f;
                    Vector3 d = to - from;
                    RaycastHit rh;
                    bool ray = Physics.Raycast(from, d.normalized, out rh, d.magnitude, 1 << occLayer, QueryTriggerInteraction.Ignore);
                    RaycastHit sh;
                    bool sphere = Physics.SphereCast(from, 0.65f, d.normalized, out sh, d.magnitude, 1 << occLayer, QueryTriggerInteraction.Ignore);
                    Collider c1 = duvar1.GetComponent<Collider>();
                    Assert.Fail("Duvar 1 için fade bileşeni oluşmadı. Tanı: occLayer=" + occLayer + " PrototypeLayers.CameraOccluder=" +
                                PrototypeLayers.CameraOccluder + " duvar.layer=" + duvar1.layer + " collider=" + (c1 != null ? c1.enabled + " " + c1.bounds : "yok") +
                                " from=" + from.ToString("F1") + " to=" + to.ToString("F1") + " ray=" + ray + (ray ? " " + rh.collider.name : "") +
                                " sphere=" + sphere + (sphere ? " " + sh.collider.name : "") + " timeScale=" + Time.timeScale +
                                " dt=" + Time.deltaTime.ToString("F4") + " autoSync=" + Physics.autoSyncTransforms +
                                " followTarget=" + (_rig.FollowTarget != null ? _rig.FollowTarget.name : "null") +
                                " fader=" + (_rig.GetComponent<CameraOcclusionFader>() != null ? _rig.GetComponent<CameraOcclusionFader>().enabled.ToString() : "yok"));
                }
                Assert.IsNotNull(f2, "Duvar 2 için fade bileşeni oluşmadı (birden fazla engel).");
                Assert.IsTrue(f1.IsFaded && f2.IsFaded, "Engeller saydamlaşmadı.");
                Assert.AreEqual(HedefAlfa, f1.Alpha, 0.05f, "Duvar 1 hedef alfaya inmedi: " + f1.Alpha);
                Assert.AreEqual(HedefAlfa, f2.Alpha, 0.05f, "Duvar 2 hedef alfaya inmedi: " + f2.Alpha);
                Assert.AreNotEqual(orijinal1, duvar1.GetComponent<Renderer>().sharedMaterial, "Duvar 1 materyali klonlanmadı.");
                StringAssert.Contains("Occluder Fade", duvar1.GetComponent<Renderer>().sharedMaterial.name);

                float mesafeSonra = Vector3.Distance(_rig.StablePosition, _hedef.transform.position + Vector3.up * OdakYukseklik);
                Assert.AreEqual(mesafeOnce, mesafeSonra, 0.01f, "REQ-035: kamera engel nedeniyle hedefe yaklaştı.");

                // Engel 1 kalkar, engel 2 kalır.
                duvar1.transform.position += new Vector3(30f, 0f, 0f);
                Physics.SyncTransforms();
                bekle0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - bekle0 < 0.75f) yield return null;

                Assert.IsFalse(f1.IsFaded, "Kalkan engel geri dönmedi: alfa " + f1.Alpha);
                Assert.AreEqual(orijinal1, duvar1.GetComponent<Renderer>().sharedMaterial, "Duvar 1 orijinal materyaline dönmedi.");
                Assert.IsFalse(f1.enabled, "Geri dönen engelin fade bileşeni kapanmadı.");
                Assert.IsTrue(f2.IsFaded, "Kalan engel yanlışlıkla geri döndü (state karışması).");

                duvar2.transform.position += new Vector3(30f, 0f, 0f);
                Physics.SyncTransforms();
                bekle0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - bekle0 < 0.75f) yield return null;
                Assert.IsFalse(f2.IsFaded, "İkinci engel kalkınca geri dönmedi.");
                Assert.AreEqual(orijinal2, duvar2.GetComponent<Renderer>().sharedMaterial);
                Assert.IsFalse(f2.enabled);
            }
            finally
            {
                UnityEngine.Object.Destroy(duvar1);
                UnityEngine.Object.Destroy(duvar2);
            }
        }

        private static GameObject Engel(string ad, Vector3 konum, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = ad;
            go.transform.position = konum;
            go.transform.localScale = new Vector3(6f, 6f, 0.5f);
            go.layer = layer;
            return go;
        }
    }
}
