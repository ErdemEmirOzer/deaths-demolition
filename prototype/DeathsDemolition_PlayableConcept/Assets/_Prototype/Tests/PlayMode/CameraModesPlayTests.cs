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
            InputSourceRegistry.Clear();
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
