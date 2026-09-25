using System;
using System.Collections;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.InputAbstraction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>WP-070 test girdisi: fare deltası + bakış çubuğu sürülebilir.</summary>
    internal sealed class KameraBakisGirdisi : ILookInputSource
    {
        public Vector2 Delta;
        public Vector2 Cubuk;

        public bool IsEnabled => true;
        public Vector2 Move => Vector2.zero;
        public AimSourceKind AimKind => AimSourceKind.Pointer;
        public Vector2 AimScreenPosition => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        public Vector2 AimStick => Cubuk;
        public bool FireHeld => false;
        public bool FirePressed => false;
        public bool FireReleased => false;
        public bool ReloadPressed => false;
        public bool PausePressed => false;
        public bool DebugPanelPressed => false;
        public Vector2 LookDelta => Delta;
        public Vector2 LookStick => Cubuk;
        public bool OmuzBas;
        public bool ShoulderSwapPressed => OmuzBas;
#pragma warning disable 67
        public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
        public void Enable() { }
        public void Disable() { }
    }

    /// <summary>
    /// WP-070 · Kamera modları (Tepeden / TP / FP) sözleşmesi. CameraContractTests kalıbı:
    /// rig'siz Main Menu, dünyanın çok dışında (Kok) zemin + takip hedefi (+ gövde ebeveyni).
    /// Sayılar rig varsayılanlarıdır (S-07 CameraModesFeel gelince türetme yenilenir).
    /// </summary>
    [TestFixture]
    public sealed class CameraModesPlayTests
    {
        private static readonly Vector3 Kok = new Vector3(-320f, 0f, 320f);
        private const float TpBoom = 2.8f;          // _tpDistance
        private const float TpPivot = 1.45f;        // _tpPivotHeight
        private const float FpGoz = 1.62f;          // _fpEyeHeight

        private GameObject _zemin;
        private GameObject _govde;
        private GameObject _hedef;
        private GameObject _engel;
        private PrototypeCameraRig _rig;
        private KameraBakisGirdisi _girdi;
        private IInputSource _oncekiGirdi;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            CameraModeService.ResetForTests();
            SceneManager.LoadScene(DD.Prototype.Core.Boot.PrototypeScenes.MainMenu, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != DD.Prototype.Core.Boot.PrototypeScenes.MainMenu
                   && Time.realtimeSinceStartup - t0 < 20f)
            {
                yield return null;
            }

            yield return null;
            Assert.IsNull(PrototypeCameraRig.Active, "Main Menu sahnesinde bir kamera rig'i etkin; ölçüm kirlenir.");

            _zemin = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _zemin.name = "WP070-Zemin";
            _zemin.transform.position = Kok;
            _zemin.transform.localScale = new Vector3(20f, 1f, 20f);
            _zemin.layer = PrototypeLayers.Environment;

            _govde = new GameObject("WP070-Govde");
            _govde.transform.SetPositionAndRotation(Kok, Quaternion.Euler(0f, 90f, 0f));   // doğuya bakıyor
            GameObject gorsel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            UnityEngine.Object.Destroy(gorsel.GetComponent<Collider>());
            gorsel.transform.SetParent(_govde.transform, false);
            gorsel.transform.localPosition = Vector3.up;
            _hedef = new GameObject("CameraFollow");
            _hedef.transform.SetParent(_govde.transform, false);

            _girdi = new KameraBakisGirdisi();
            _oncekiGirdi = InputSourceRegistry.Current;   // WP-079C: restored in teardown (the installer sets it once per process)
            InputSourceRegistry.SetCurrent(_girdi);

            _rig = CameraRigFactory.EnsureRig(_hedef.transform);
            Assert.IsNotNull(_rig);
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Yik()
        {
            Time.captureDeltaTime = 0f;
            CameraShakeService.StopAll();
            InputSourceRegistry.SetCurrent(_oncekiGirdi);   // not Clear(): later fixtures (LifecycleHardening) need the real source
            if (_rig != null) UnityEngine.Object.Destroy(_rig.gameObject);
            if (_engel != null) UnityEngine.Object.Destroy(_engel);
            if (_govde != null) UnityEngine.Object.Destroy(_govde);
            if (_zemin != null) UnityEngine.Object.Destroy(_zemin);
            yield return null;
            CameraModeService.ResetForTests();
            Cursor.lockState = CursorLockMode.None;
            LogAssert.ignoreFailingMessages = false;
        }

        private static IEnumerator Kareler(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TP_OmuzUstuBumVeKameraYaw()
        {
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            _rig.SetViewAngles(0f, 0f);
            yield return Kareler(20);

            Vector3 pivot = Kok + Vector3.up * TpPivot;
            float mesafe = Vector3.Distance(_rig.StablePosition, pivot);
            Assert.AreEqual(Mathf.Sqrt(TpBoom * TpBoom + 0.55f * 0.55f + 0.35f * 0.35f), mesafe, 0.05f, "TP bum uzunluğu");
            Assert.Less(_rig.StablePosition.z, pivot.z, "TP kamera pivotun arkasında (yaw 0 → -Z) olmalı.");
            Assert.Greater(_rig.StablePosition.x, pivot.x, "Sağ omuz: kamera +X tarafında olmalı.");
            Assert.Less(Vector3.Angle(_rig.PlanarForward, Vector3.forward), 0.5f, "Hareket tabanı kamera yaw'ını izlemeli.");

            _rig.SetViewAngles(90f, 0f);
            yield return Kareler(3);
            Assert.Less(Vector3.Angle(_rig.PlanarForward, Vector3.right), 0.5f, "Yaw 90 → PlanarForward +X.");
            Assert.AreEqual(CameraModeService.Settings.ThirdPersonFov, _rig.Camera.fieldOfView, 0.01f, "TP FOV ayardan.");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TP_OmuzDegistirme_TusIleSolaGecer()
        {
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            _rig.SetViewAngles(0f, 0f);
            yield return Kareler(10);
            Assert.Greater(_rig.StablePosition.x, Kok.x, "Başlangıç sağ omuz.");

            _girdi.OmuzBas = true;
            yield return null;
            _girdi.OmuzBas = false;
            Assert.AreEqual(CameraShoulder.Left, CameraModeService.Settings.Shoulder);
            yield return Kareler(40);
            Assert.Less(_rig.StablePosition.x, Kok.x, "Omuz değişince kamera -X tarafına geçmeli.");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TP_DuvarItmesi_KameraAslaDuvarIcindeDegil()
        {
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            _rig.SetViewAngles(0f, 0f);
            yield return Kareler(10);

            _engel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _engel.name = "WP070-Duvar";
            _engel.layer = PrototypeLayers.Environment;
            _engel.transform.position = Kok + new Vector3(0f, 1.5f, -1.4f);
            _engel.transform.localScale = new Vector3(6f, 3f, 0.3f);
            Physics.SyncTransforms();

            for (int i = 0; i < 30; i++)
            {
                yield return null;
                Assert.IsFalse(Physics.CheckSphere(_rig.StablePosition, 0.05f, 1 << PrototypeLayers.Environment),
                    "Kare " + i + ": TP kamera duvarın içinde (" + _rig.StablePosition + ").");
            }

            Assert.Less(_rig.ThirdPersonBoom, 1.4f, "Duvar bumu kısaltmalı.");
            Assert.AreEqual(0, _rig.InsideColliderFramesTotal, "WP-078 A2: kamera hiçbir karede duvar içinde sayılmamalı.");
            Assert.Greater(_rig.ProbedFramesTotal, 30);
            UnityEngine.Object.Destroy(_engel);
            _engel = null;
            yield return Kareler(90);
            Assert.AreEqual(Mathf.Sqrt(TpBoom * TpBoom + 0.55f * 0.55f + 0.35f * 0.35f), _rig.ThirdPersonBoom, 0.1f,
                "Duvar kalkınca bum yumuşakça geri açılmalı.");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator FP_GozNoktasiYatayFovVeMerkezIsini()
        {
            CameraModeService.Request(CameraMode.FirstPerson, instant: true);
            _rig.SetViewAngles(0f, 0f);
            yield return Kareler(5);

            Assert.AreEqual(Kok.y + FpGoz, _rig.StablePosition.y, 0.02f, "FP göz yüksekliği");
            Assert.AreEqual(Kok.x, _rig.StablePosition.x, 0.02f);
            float yatay169 = 2f * Mathf.Atan(Mathf.Tan(_rig.Camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * (16f / 9f)) * Mathf.Rad2Deg;
            Assert.AreEqual(CameraModeService.Settings.FirstPersonFov, yatay169, 0.2f, "FP FOV 16:9 referanslı yatay derece (Hor+) olmalı.");
            Assert.AreEqual(90f, CameraModeService.Settings.FirstPersonFov, 0.001f, "Varsayılan FP FOV 90 (C-00 24.09).");

            _engel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _engel.layer = PrototypeLayers.Environment;
            _engel.transform.position = Kok + new Vector3(0f, FpGoz, 10f);
            Physics.SyncTransforms();
            yield return null;

            CameraAimRay ray;
            Assert.IsTrue(_rig.TryGetAimRay(Vector2.zero, 0f, out ray));
            Assert.IsTrue(ray.HasHit, "Merkez ışını önündeki küpe çarpmalı.");
            Assert.AreEqual(9.5f - 0.12f, ray.Distance, 0.1f, "Göz 0,12 m önde; küp yüzü z 9,5.");
            Assert.IsTrue(CameraView.IsRearBlind);
            Assert.IsFalse(CameraView.IsInView(Kok + new Vector3(0f, 1f, -5f)), "Arkadaki nokta FP'de görüş dışı.");
            Assert.IsTrue(CameraView.IsInView(Kok + new Vector3(0f, 1.6f, 8f)));

            CameraAimRay kisa;
            Assert.IsTrue(_rig.TryGetAimRay(Vector2.zero, 0f, 5f, out kisa));
            Assert.IsFalse(kisa.HasHit, "5 m sınırında küpe (10 m) ulaşılmamalı.");
            Assert.AreEqual(5f, kisa.Distance, 0.001f);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator FP_GoruntuModeliOverlayKamerasi()
        {
            int katman = CameraLayers.FirstPersonView;
            Assert.GreaterOrEqual(katman, 0, "TagManager'da FirstPersonView katmanı yok.");
            Assert.IsNull(_rig.ViewmodelAnchor, "Tepeden modda görüntü modeli kurulmamalı.");

            CameraModeService.Request(CameraMode.FirstPerson, instant: true);
            yield return Kareler(3);

            Assert.IsNotNull(_rig.ViewmodelAnchor);
            Assert.AreSame(_rig.ViewmodelAnchor, CameraView.ViewmodelAnchor);
            Camera vm = _rig.ViewmodelCamera;
            Assert.IsNotNull(vm);
            Assert.AreSame(vm.transform, _rig.ViewmodelAnchor.parent);
            Assert.IsTrue(vm.enabled);
            Assert.AreEqual(1 << katman, vm.cullingMask, "Overlay kamera yalnız FirstPersonView çizmeli.");
            Assert.AreNotEqual("MainCamera", vm.tag, "S-03: Camera.main dünya kamerası kalmalı.");
            Assert.AreEqual(0, _rig.Camera.cullingMask & (1 << katman), "Dünya kamerası görüntü modeli katmanını çizmemeli.");
            Assert.IsTrue(_rig.IsViewmodelOverlayStacked, "Overlay kamera URP yığınına eklenmeli.");
            Assert.AreEqual(_rig.Camera.fieldOfView, vm.fieldOfView, 0.01f, "Faz 1: görüntü modeli FOV'u dünyaya kilitli.");

            // WP-070V: the viewmodel pass carries a screen transform; the mapped world point must be drawn at the
            // same screen position and depth by the world camera (both projections as the cameras use them).
            // WP-070I: the default is identity now, so the mapping is exercised with an explicit non-identity transform.
            CameraLayers.ViewmodelScreenScale = 0.8f;
            CameraLayers.ViewmodelScreenPivot = new Vector2(0.3f, 0f);
            CameraLayers.ViewmodelScreenShift = new Vector2(-0.1f, 0.02f);
            yield return null;
            Transform ct = _rig.Camera.transform;
            foreach (Vector3 yerel in new[] { new Vector3(0.18f, -0.22f, 0.5f), new Vector3(-0.1f, 0.05f, 0.9f), new Vector3(0f, 0f, 2f) })
            {
                Vector3 dunya = ct.TransformPoint(yerel);
                Vector3 a = vm.WorldToViewportPoint(dunya);
                Vector3 b = _rig.Camera.WorldToViewportPoint(CameraView.ViewmodelToWorld(dunya));
                Assert.Less(Vector2.Distance(a, b), 1e-3f, "ViewmodelToWorld ekran konumu: " + yerel + " vm " + a + " dünya " + b);
                Assert.AreEqual(a.z, b.z, 1e-3f, "ViewmodelToWorld derinliği korur");
            }

            CameraLayers.ViewmodelScreenScale = 1f;
            CameraLayers.ViewmodelScreenShift = Vector2.zero;
            yield return null;
            Vector3 d0 = ct.TransformPoint(new Vector3(0.18f, -0.22f, 0.5f));
            Assert.Less(Vector3.Distance(d0, CameraView.ViewmodelToWorld(d0)), 0.001f, "Özdeş dönüşüm + FOV kilidinde ViewmodelToWorld özdeşlik.");
            CameraLayers.ViewmodelScreenScale = CameraLayers.DefaultViewmodelScreenScale;
            CameraLayers.ViewmodelScreenShift = CameraLayers.DefaultViewmodelScreenShift;
            CameraLayers.ViewmodelScreenPivot = CameraLayers.DefaultViewmodelScreenPivot;

            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            yield return Kareler(2);
            Assert.IsFalse(vm.enabled, "FP dışında overlay kamera kapalı.");
            Assert.IsNull(CameraView.ViewmodelAnchor);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Okluzyon_ModPolitikasi_FPdeKapaliTPdeCatiSolmaz()
        {
            // Tepeden kamera hattının ortasında (odak 1 m üstü → kamera (0, 16, -13)) iki engel.
            yield return Kareler(5);
            GameObject duvar = Engel("WP070-OkDuvar", Kok + new Vector3(-0.8f, 9f, -6.8f), null);
            GameObject cati = Engel("WP070-OkCati", Kok + new Vector3(0.8f, 9f, -6.8f), CameraOccluderKind.Roof);
            Physics.SyncTransforms();
            yield return GercekSure(0.5f);
            Assert.IsNotNull(duvar.GetComponent<OccluderFadeState>(), "Tepeden (FadeAll): duvar solmalı.");
            Assert.IsNotNull(cati.GetComponent<OccluderFadeState>(), "Tepeden (FadeAll): çatı da solmalı.");
            Assert.Less(cati.GetComponent<OccluderFadeState>().Alpha, 0.9f);

            // TP politikası (FadeCameraLine) aynı hatta: çatı solmaz, duvar solar.
            CameraModeProfile p = CameraModeService.Profile(CameraMode.TopDown);
            p.OccluderPolicy = CameraOccluderPolicy.FadeCameraLine;
            CameraModeService.SetProfile(CameraMode.TopDown, p);
            yield return GercekSure(0.6f);
            Assert.Less(duvar.GetComponent<OccluderFadeState>().Alpha, 0.9f, "FadeCameraLine: duvar solu kalır.");
            Assert.Greater(cati.GetComponent<OccluderFadeState>().Alpha, 0.95f, "FadeCameraLine: çatı (Roof) geri döner.");

            // FP (None): hiçbir şey solmaz, soldurulan geri döner.
            p.OccluderPolicy = CameraOccluderPolicy.None;
            CameraModeService.SetProfile(CameraMode.TopDown, p);
            yield return GercekSure(0.6f);
            Assert.Greater(duvar.GetComponent<OccluderFadeState>().Alpha, 0.95f, "None: duvar geri dönmeli.");
            Assert.AreEqual(CameraOccluderPolicy.None, CameraModeService.Profile(CameraMode.FirstPerson).OccluderPolicy);

            UnityEngine.Object.Destroy(duvar);
            UnityEngine.Object.Destroy(cati);
        }

        private GameObject Engel(string ad, Vector3 konum, CameraOccluderKind? tur)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = ad;
            go.layer = PrototypeLayers.CameraOccluder;
            go.transform.position = konum;
            go.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
            if (tur.HasValue)
            {
                go.AddComponent<CameraOccluderClass>().Kind = tur.Value;
            }

            return go;
        }

        private static IEnumerator GercekSure(float saniye)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < saniye)
            {
                yield return null;
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Bakis_FareDeltasiYawVeInvertY()
        {
            CameraModeService.ApplySettings(new CameraModeSettings { Mode = CameraMode.FirstPerson, LookSensitivity = 0.1f }, instant: true);
            _rig.SetViewAngles(0f, 0f);
            yield return null;

            _girdi.Delta = new Vector2(10f, 0f);
            yield return Kareler(10);
            _girdi.Delta = Vector2.zero;
            yield return null;
            Assert.AreEqual(10f, _rig.ViewYaw, 0.6f, "10 kare × 10 px × 0,1 °/px = 10° yaw.");

            _girdi.Delta = new Vector2(0f, 10f);   // fare yukarı → yukarı bak (pitch negatif)
            yield return null;
            _girdi.Delta = Vector2.zero;
            yield return null;
            Assert.AreEqual(-1f, _rig.ViewPitch, 0.1f);

            CameraModeService.ApplySettings(new CameraModeSettings { Mode = CameraMode.FirstPerson, LookSensitivity = 0.1f, InvertY = true }, instant: true);
            _girdi.Delta = new Vector2(0f, 10f);
            yield return null;
            _girdi.Delta = Vector2.zero;
            yield return null;
            Assert.AreEqual(0f, _rig.ViewPitch, 0.1f, "Y-ters: aynı delta ters yönde.");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Gecis_TepedenTPYumusakVeSureIcindeBiter()
        {
            Vector3 tepeden = _rig.StablePosition;
            CameraModeService.Request(CameraMode.ThirdPerson);
            Assert.IsTrue(CameraModeService.IsTransitioning);
            yield return null;
            Vector3 ilkKare = _rig.StablePosition;
            Assert.Less(Vector3.Distance(ilkKare, tepeden), 3f, "İlk karede sıçrama olmamalı (harman).");

            float t0 = Time.realtimeSinceStartup;
            int kare = 0;
            while (CameraModeService.IsTransitioning && kare < 600)
            {
                kare++;
                yield return null;
            }

            Assert.IsFalse(CameraModeService.IsTransitioning, "Geçiş bitmedi.");
            Assert.Less(Vector3.Distance(_rig.StablePosition, Kok + Vector3.up * TpPivot), 4f, "Geçiş sonu TP pozu.");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Tepkiler_RecoilNisanaGirerKickVeFovPunchGirmez()
        {
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            _rig.SetViewAngles(0f, 10f);
            yield return Kareler(5);

            CameraAimRay once;
            _rig.TryGetAimRay(Vector2.zero, 0f, out once);
            CameraReactions.AddKick(new Vector3(0.05f, 0.05f, -0.05f), 2f);
            CameraReactions.FovPunch(3f, 0.2f);
            yield return null;
            CameraAimRay kickSonra;
            _rig.TryGetAimRay(Vector2.zero, 0f, out kickSonra);
            Assert.Less(Vector3.Angle(once.Direction, kickSonra.Direction), 0.01f, "Kick/FOV punch nişan ışınını oynatmamalı.");

            CameraReactions.AddRecoil(1f, 0f);
            yield return null;
            CameraAimRay recoilSonra;
            _rig.TryGetAimRay(Vector2.zero, 0f, out recoilSonra);
            Assert.AreEqual(1f, Vector3.Angle(once.Direction, recoilSonra.Direction), 0.2f, "Recoil nişana dahil (D-20).");
            Assert.Greater(recoilSonra.Direction.y, once.Direction.y, "Recoil yukarı kaldırmalı.");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator R1A17_FPdenCikis_GovdeBasTarafinda20YuzdeGizliKalir()
        {
            Renderer govde = _govde.GetComponentInChildren<Renderer>();
            CameraModeService.Request(CameraMode.FirstPerson, instant: true);
            yield return Kareler(3);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, govde.shadowCastingMode, "FP: gövde gölge-yalnız.");

            CameraModeService.Request(CameraMode.ThirdPerson);
            yield return null;
            Assert.Less(CameraModeService.Transition01, 0.2f);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, govde.shadowCastingMode,
                "FP→TP ilk %20: kamera hâlâ göz hizasında, gövde gizli kalmalı (R1-A #17).");

            int kare = 0;
            while (CameraModeService.IsTransitioning && kare++ < 600) yield return null;
            yield return null;
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, govde.shadowCastingMode, "Geçiş bitince gövde görünür.");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator R1A7_RigKapaninca_BekleyenGecisBiter()
        {
            CameraModeService.Request(CameraMode.ThirdPerson);
            yield return null;
            Assert.IsTrue(CameraModeService.IsTransitioning);
            _rig.gameObject.SetActive(false);
            Assert.IsFalse(CameraModeService.IsTransitioning, "Rig kapanınca geçiş askıda kalmamalı (ateş yutulmaya devam eder).");
            _rig.gameObject.SetActive(true);
            yield return null;
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator R1A18_OlumVurusu_ModIsteginiKuyruklar()
        {
            Assert.IsTrue(DD.Prototype.Feedback.DeathSequence.Begin(0.5f, 0.05f, 0.15f, 0.05f));
            Assert.IsTrue(CameraModeService.IsModeChangeLocked, "Ölüm vuruşu mod değişimini kilitlemeli (§5.2).");
            Assert.IsFalse(CameraModeService.Request(CameraMode.ThirdPerson));
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
            Assert.AreEqual(CameraMode.ThirdPerson, CameraModeService.Target);

            float t0 = Time.realtimeSinceStartup;
            while (DD.Prototype.Feedback.DeathSequence.IsActive && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Assert.IsFalse(CameraModeService.IsModeChangeLocked);
            Assert.AreEqual(CameraMode.ThirdPerson, CameraModeService.Current, "Kuyruktaki istek vuruş bitince uygulanmalı.");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator DuvarIciSayaci_PozitifKontrol()
        {
            yield return Kareler(5);
            int once = _rig.InsideColliderFramesTotal;
            _engel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _engel.layer = PrototypeLayers.Environment;
            _engel.transform.position = _rig.StablePosition;
            _engel.transform.localScale = Vector3.one * 2f;
            Physics.SyncTransforms();
            yield return Kareler(10);
            Assert.GreaterOrEqual(_rig.InsideColliderFramesTotal - once, 8, "Kamera küpün içindeyken sayaç artmalı (WP-078 A2).");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Tepeden_NisanIsiniEskiDuzlemIzdusumuyleAyni()
        {
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
            yield return Kareler(5);
            Vector2 ekran = new Vector2(Screen.width * 0.6f, Screen.height * 0.4f);
            Vector3 eski;
            Assert.IsTrue(_rig.ScreenPointToAimPlane(ekran, Kok.y + 1.2f, out eski));
            CameraAimRay ray;
            Assert.IsTrue(_rig.TryGetAimRay(ekran, Kok.y + 1.2f, out ray));
            Assert.Less(Vector3.Distance(eski, ray.HitPoint), 0.001f, "TopDown nişanı birebir korunmalı (K1).");
            Assert.Less(Vector3.Angle(_rig.PlanarForward, Vector3.forward), 0.01f, "TopDown hareket tabanı sabit.");
        }
    }
}
