using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073CasingContractTests
    {
        private SpecialWeaponSahne _scene;
        private VfxDressing _vfx;
        private int _pumpEvents;
        private int _casingsAtPump;
        private Vector3 _pumpPort;

        // C-00 30.09 (S-09 root cause, K/wp-068e/evidence r1e-cipin + r1e-pindiag): TrySetClassicWeapon writes the process-wide
        // WeaponRegistry.SelectedWeapon, and ResetStatics runs once per PlayMode run — the RunTree* tier gate later in the CI
        // order saw rl_1. The selection is saved before each test and given back as the teardown's last step.
        private ClassicWeaponId _seciliOnce;

        [SetUp]
        public void SeciliSilahiSakla() => _seciliOnce = WeaponRegistry.SelectedWeapon;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            WeaponEvents.Mechanism -= OnMechanism;
            if (_scene != null) { yield return _scene.Bitir(); _scene = null; }
            RunFlowClock.ClearHolds();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            }
            LogAssert.ignoreFailingMessages = false;
            WeaponRegistry.SelectedWeapon = _seciliOnce;
        }

        private IEnumerator SetUpWeapon(ClassicWeaponId id)
        {
            _scene = new SpecialWeaponSahne();
            yield return _scene.Kur();
            LogAssert.ignoreFailingMessages = false;
            Assert.IsTrue(WeaponQaHooks.TrySetClassicWeapon(id));
            yield return new WaitForSeconds(1f);
            _vfx = HitFeedbackDirector.Instance.Vfx;
            Assert.IsTrue(_vfx.IsReady);
            Assert.IsNotNull(_vfx.CasingPool, "AP-013 casing library/pool must exist; absence cannot make suppression pass");
        }

        private IEnumerator FireOne()
        {
            int ammo = _scene.Controller.Classic.Ammo;
            _scene.Girdi.AtesBasti = true;
            _scene.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Bekle(() => _scene.Controller.Classic.Ammo == ammo - 1, 2f, "real controller did not fire");
            _scene.Girdi.AtesBasti = false;
            _scene.Girdi.AtesBasili = false;
        }

        private void OnMechanism(WeaponMechanismEvent info)
        {
            if (info.Weapon != ClassicWeaponId.CombatShotgun || info.Kind != WeaponMechanismKind.CasingEjected) return;
            _pumpEvents++;
            _casingsAtPump = _vfx.CasingsEjected;
            _pumpPort = info.Position;
        }

        [UnityTest]
        public IEnumerator Rl1_RealShot_FlashWithoutRifleCasing()
        {
            yield return SetUpWeapon(ClassicWeaponId.Rl1);
            Assert.IsFalse(VfxWeaponProfiles.Rl1.HasCasing);
            int casings = _vfx.CasingsEjected, flashes = _vfx.MuzzlePlays;
            yield return FireOne();
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(flashes + 1, _vfx.MuzzlePlays, "suppression must keep the real muzzle flash");
            Assert.AreEqual(casings, _vfx.CasingsEjected, "rl_1 must not eject a rifle casing, including delayed mechanism events");
        }

        [UnityTest]
        public IEnumerator Sg1_RealShot_CasingOnlyAtPumpEvent()
        {
            yield return SetUpWeapon(ClassicWeaponId.CombatShotgun);
            Assert.AreEqual(0.40f, WeaponMechanismTimeline.Shotgun.CasingDelaySeconds, 1e-5f);
            _pumpEvents = 0;
            // Re-arming twice must not duplicate the new mechanism subscription.
            _vfx.Binder.Subscribe();
            _vfx.Binder.Subscribe();
            WeaponEvents.Mechanism += OnMechanism; // observe after the VFX handler, including after re-arm
            int casings = _vfx.CasingsEjected;
            yield return FireOne();
            Assert.AreEqual(0, _pumpEvents);
            Assert.AreEqual(casings, _vfx.CasingsEjected, "shot flash must not eject sg_1 shell");
            yield return new WaitForSeconds(0.20f);
            Assert.AreEqual(casings, _vfx.CasingsEjected, "no independent early VFX ejection timer");
            yield return SpecialWeaponSahne.Bekle(() => _pumpEvents == 1, 1.2f, "existing clip/timer pump event did not arrive");
            Assert.AreEqual(casings + 1, _casingsAtPump, "VFX shell must spawn synchronously with the authoritative pump event");
            Assert.AreEqual(casings + 1, _vfx.CasingsEjected);
            bool atPort = false;
            foreach (CasingEffect casing in _vfx.CasingPool.Spawned)
                if (casing != null && Vector3.Distance(casing.transform.position, _pumpPort) < 0.15f) atPort = true;
            Assert.IsTrue(atPort, "shell must leave the real ejection socket");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(casings + 1, _vfx.CasingsEjected, "one pump event must not duplicate the shell");
        }

        [UnityTest]
        public IEnumerator Ar1_KeepsImmediateShotCasing()
        {
            yield return SetUpWeapon(ClassicWeaponId.AssaultRifle);
            int casings = _vfx.CasingsEjected;
            yield return FireOne();
            Assert.AreEqual(casings + 1, _vfx.CasingsEjected, "ar_1 immediate shot casing contract");
            yield return new WaitForSeconds(0.65f);
            Assert.AreEqual(casings + 1, _vfx.CasingsEjected, "ar_1 mechanism must not duplicate the shot casing");
        }

        /// <summary>M-21 (C-00 30.09, S-02 contract): sg_2 is a break-open double — no shell on the shot; opening the breech
        /// on reload raises two CasingEjected events and the VFX lays one shell per event. Needs S-02's BreakOpen timeline.</summary>
        [UnityTest]
        public IEnumerator Sg2_BreakOpen_NoShotCasing_TwoShellsOnReload()
        {
            yield return SetUpWeapon(ClassicWeaponId.Sg2);
            int events = 0;
            void Count(WeaponMechanismEvent e) { if (e.Weapon == ClassicWeaponId.Sg2 && e.Kind == WeaponMechanismKind.CasingEjected) events++; }
            WeaponEvents.Mechanism += Count;
            try
            {
                int casings = _vfx.CasingsEjected;
                yield return FireOne();
                yield return new WaitForSeconds(0.65f);
                Assert.AreEqual(casings, _vfx.CasingsEjected, "sg_2: no shell on the shot");
                Assert.AreEqual(0, events, "sg_2: no mechanism shell on the shot");
                _scene.Girdi.DoldurBasti = true;
                yield return null;
                _scene.Girdi.DoldurBasti = false;
                yield return SpecialWeaponSahne.Bekle(() => events >= 2, 4f, "sg_2 break-open did not raise two CasingEjected events");
                yield return new WaitForSeconds(0.1f);
                Assert.AreEqual(2, events);
                Assert.AreEqual(casings + 2, _vfx.CasingsEjected, "sg_2: one shell per break-open event");
            }
            finally
            {
                WeaponEvents.Mechanism -= Count;
            }
        }
    }
}
