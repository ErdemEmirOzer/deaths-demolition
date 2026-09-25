using System.Collections;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Feedback;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-073 B4 · camera juice (direction A4): the recoil returns on a critically damped spring by the firing weapon's
    /// recovery (%95 inside it; the sniper slower than the rifle), the FP rest pose is still (no bob / breath at rest), walking
    /// bobs within the A4.1 band, reduced motion zeroes bob / kick roll / FOV punch (recoil unchanged), a shot's roll stays in the
    /// 1.5° cap and never moves the aim ray (J1), and every Arsenal weapon has a camera row (J7). Fixture: CameraModesPlayTests.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073CameraFeelTests
    {
        private static readonly Vector3 Kok = new Vector3(-360f, 0f, 360f);
        private GameObject _zemin, _govde, _hedef;
        private PrototypeCameraRig _rig;
        private CameraModeSettings _before;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            CameraModeService.ResetForTests();
            SceneManager.LoadScene(DD.Prototype.Core.Boot.PrototypeScenes.MainMenu, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != DD.Prototype.Core.Boot.PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            yield return null;

            _zemin = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _zemin.transform.position = Kok;
            _zemin.transform.localScale = new Vector3(40f, 1f, 40f);
            _zemin.layer = PrototypeLayers.Environment;
            _govde = new GameObject("WP073-Govde");
            _govde.transform.SetPositionAndRotation(Kok, Quaternion.identity);
            _hedef = new GameObject("CameraFollow");
            _hedef.transform.SetParent(_govde.transform, false);
            InputSourceRegistry.SetCurrent(new KameraBakisGirdisi());
            _rig = CameraRigFactory.EnsureRig(_hedef.transform);
            Assert.IsNotNull(_rig);
            _before = CameraModeService.Settings;
            Time.captureDeltaTime = 1f / 60f;
            CameraModeService.Request(CameraMode.FirstPerson, instant: true);
            _rig.SetViewAngles(0f, 0f);
            CameraModesFeel.ActiveRow = null;
            yield return Kareler(10);
        }

        [UnityTearDown]
        public IEnumerator Yik()
        {
            Time.captureDeltaTime = 0f;
            CameraShakeService.StopAll();
            InputSourceRegistry.Clear();
            if (_before != null) CameraModeService.ApplySettings(_before, false, true);
            CameraModesFeel.ActiveRow = null;
            if (_rig != null) Object.Destroy(_rig.gameObject);
            if (_govde != null) Object.Destroy(_govde);
            if (_zemin != null) Object.Destroy(_zemin);
            yield return null;
            CameraModeService.ResetForTests();
            Cursor.lockState = CursorLockMode.None;
            LogAssert.ignoreFailingMessages = false;
        }

        private static IEnumerator Kareler(int n) { for (int i = 0; i < n; i++) yield return null; }

        private Transform Kamera => _rig.GetComponentInChildren<Camera>().transform;

        private float AimPitch()
        {
            CameraAimRay ray;
            _rig.TryGetAimRay(Vector2.zero, 0f, out ray);
            return Mathf.Asin(Mathf.Clamp(ray.Direction.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Recoil_KritikSonumluYay_SilahToparlanmasinda()
        {
            float p0 = AimPitch();
            CameraModesFeel.ActiveRow = CameraModesFeel.For("ar_1");   // 0.35 s
            CameraReactions.AddRecoil(2f, 0f);
            yield return null;
            Assert.AreEqual(2f, AimPitch() - p0, 0.2f, "recoil bu karenin görüşünde");
            yield return Kareler(21);                                   // 0.35 s at 1/60
            float ar = AimPitch() - p0;
            Assert.Less(Mathf.Abs(ar), 0.1f + 0.02f, "ar_1: %95 toparlanma 0,35 s içinde (" + ar + "°)");
            Assert.GreaterOrEqual(ar, -0.02f, "kritik sönüm: aşma yok");

            yield return Kareler(30);
            p0 = AimPitch();
            CameraModesFeel.ActiveRow = CameraModesFeel.For("sr_1");   // 0.90 s
            CameraReactions.AddRecoil(2f, 0f);
            yield return Kareler(22);
            float sr = AimPitch() - p0;
            Debug.Log("[DD-TEST] wp073.b4 recoil ar@0.35=" + ar + " sr@0.35=" + sr);
            Assert.Greater(sr, 0.5f, "sr_1 daha yavaş toparlanır (0,90 s)");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator FP_DinlenmeSabit_YururkenAdimBob_AzaltilmisHareketSifir()
        {
            // At rest: nothing moves (no breath / bob at rest — the FP rest silhouette is unchanged).
            Vector3 p0 = Kamera.localPosition;
            Quaternion r0 = Kamera.localRotation;
            yield return Kareler(40);
            Assert.Less((Kamera.localPosition - p0).magnitude, 1e-4f, "dinlenmede kamera kıpırdamamalı");
            Assert.Less(Quaternion.Angle(Kamera.localRotation, r0), 0.01f);

            // Walking 3 m/s: a step bob inside the A4.1 walk band (1.4 cm vertical peak-to-peak × HeadBobScale).
            float minY = float.MaxValue, maxY = float.MinValue, maxRoll = 0f;
            for (int i = 0; i < 120; i++)
            {
                _govde.transform.position += Vector3.forward * (3f / 60f);
                yield return null;
                if (i < 20) continue;
                minY = Mathf.Min(minY, Kamera.localPosition.y);
                maxY = Mathf.Max(maxY, Kamera.localPosition.y);
                float roll = Mathf.DeltaAngle(0f, Kamera.localEulerAngles.z);
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(roll));
            }

            float pp = (maxY - minY) * 100f;
            Debug.Log("[DD-TEST] wp073.b4 bob walk peak-to-peak=" + pp + " cm roll=" + maxRoll);
            Assert.Greater(pp, 0.5f, "yürürken bob yok");
            Assert.Less(pp, 3.5f, "yürüme bob'u bandın üstünde");
            Assert.LessOrEqual(maxRoll, CameraModesFeel.RollCapFpDeg + 0.01f);

            // Reduced motion: bob, kick roll and FOV punch 0 (recoil is game and stays).
            CameraModeSettings reduced = new CameraModeSettings { ReducedMotion = true, HeadBobScale = 1f };
            CameraModeService.ApplySettings(reduced, false, true);
            yield return Kareler(20);
            float fov0 = _rig.GetComponentInChildren<Camera>().fieldOfView;
            minY = float.MaxValue; maxY = float.MinValue; maxRoll = 0f;
            CameraReactions.AddKick(Vector3.zero, 1f);
            CameraReactions.FovPunch(3f, 0.2f);
            for (int i = 0; i < 60; i++)
            {
                _govde.transform.position += Vector3.forward * (3f / 60f);
                yield return null;
                if (i < 10) continue;
                minY = Mathf.Min(minY, Kamera.localPosition.y);
                maxY = Mathf.Max(maxY, Kamera.localPosition.y);
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Mathf.DeltaAngle(0f, Kamera.localEulerAngles.z)));
            }

            Assert.Less((maxY - minY) * 100f, 0.05f, "azaltılmış harekette bob 0");
            Assert.Less(maxRoll, 0.01f, "azaltılmış harekette roll 0");
            Assert.AreEqual(fov0, _rig.GetComponentInChildren<Camera>().fieldOfView, 0.01f, "azaltılmış harekette FOV punch 0");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator J5_AyakTemasiBobCukuru_HitStopModTablosu()
        {
            // Walking: a reported footfall puts the camera in the bob trough on the next frame (J5: ≤ 1 frame).
            for (int i = 0; i < 40; i++) { _govde.transform.position += Vector3.forward * (3f / 60f); yield return null; }
            float lowest = float.MaxValue;
            for (int i = 0; i < 40; i++) { _govde.transform.position += Vector3.forward * (3f / 60f); yield return null; lowest = Mathf.Min(lowest, Kamera.localPosition.y); }
            _govde.transform.position += Vector3.forward * (3f / 60f);
            CameraModesFeel.ReportFootContact(true);
            yield return null;
            float atContact = Kamera.localPosition.y;
            Debug.Log("[DD-TEST] wp073.b4 J5 contact y=" + atContact + " lowest=" + lowest);
            Assert.LessOrEqual(atContact, lowest + 0.002f, "ayak temasında kamera bob çukurunda olmalı");

            // Hit-stop per mode (A4.5 §5.1): FP ≤ 35 ms from the table; top-down keeps the authored FeelConfig value.
            Assert.AreEqual(0.015f, FeelEventRouter.ModeHitStop(ImpactFeelId.Death, 0.05f), 1e-4f, "FP öldürme 15 ms");
            Assert.AreEqual(0.030f, FeelEventRouter.ModeHitStop(ImpactFeelId.ArmorBreak, 0.05f), 1e-4f, "FP zırh kırılması 30 ms");
            Assert.AreEqual(0.035f, FeelEventRouter.ModeHitStop(ImpactFeelId.Explosion, 0.08f), 1e-4f, "FP tavanı 35 ms");
            Assert.AreEqual(0f, FeelEventRouter.ModeHitStop(ImpactFeelId.Fire, 0f), 1e-6f, "durdurmasız olay durdurmasız kalır");
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            yield return null;
            Assert.AreEqual(0.05f, FeelEventRouter.ModeHitStop(ImpactFeelId.Death, 0.05f), 1e-4f, "tepeden: yazılan değer");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Atis_RollTavaninda_NisanOynamaz_HerSilahaSatir()
        {
            CameraFeelDriver driver = CameraFeelDriver.Ensure();
            CameraAimRay once;
            _rig.TryGetAimRay(Vector2.zero, 0f, out once);
            float maxRoll = 0f;
            for (int i = 0; i < 12; i++)
            {
                driver.Shot(CameraModesFeel.For("rl_1"));   // the biggest roll (1.2°) repeatedly: the cap holds
                yield return null;
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Mathf.DeltaAngle(0f, Kamera.localEulerAngles.z)));
                CameraAimRay ray;
                _rig.TryGetAimRay(Vector2.zero, 0f, out ray);
                Assert.Less(Vector3.Angle(once.Direction, ray.Direction), 0.01f, "kozmetik kanallar nişanı oynatmaz (J1)");
            }

            Debug.Log("[DD-TEST] wp073.b4 shot roll max=" + maxRoll);
            Assert.Greater(maxRoll, 0.1f, "atış roll'u yok");
            Assert.LessOrEqual(maxRoll, CameraModesFeel.RollCapFpDeg + 0.01f, "FP roll tavanı 1,5°");

            // WP-069 §4 weight classes and the Special runtime ids 100 / 101 (S-02 review).
            Assert.AreEqual(FeelWeight.W2, CameraModesFeel.For("sg_1").Weight);
            Assert.AreEqual(FeelWeight.W3, CameraModesFeel.For("sr_1").Weight);
            Assert.AreEqual(FeelWeight.W3, CameraModesFeel.For("sw_gel").Weight);
            Assert.AreEqual(FeelWeight.W4, CameraModesFeel.For("rl_1").Weight);
            Assert.AreEqual("sw_arc", CameraModesFeel.For((ClassicWeaponId)CameraModesFeel.SpecialArcRuntimeId).WeaponId);
            Assert.AreEqual("sw_gel", CameraModesFeel.For((ClassicWeaponId)CameraModesFeel.SpecialGelRuntimeId).WeaponId);

            // J7: every Arsenal weapon has its own row.
            foreach (ClassicWeaponId id in System.Enum.GetValues(typeof(ClassicWeaponId)))
            {
                DD.Prototype.Core.Weapons.WeaponDefinition d = DD.Prototype.Core.Weapons.Arsenal.Get(id);
                if (d == null || string.IsNullOrEmpty(d.weaponId)) continue;
                Assert.AreEqual(d.weaponId, CameraModesFeel.For(id).WeaponId, "kamera satırı eksik: " + d.weaponId);
            }
        }
    }
}
