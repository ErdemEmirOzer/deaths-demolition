using System.Collections;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Characters;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    [TestFixture]
    public sealed class R3HeroE5LifecycleTests
    {
        private SpecialWeaponSahne _s;
        private ClassicWeaponId _selected;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _selected = WeaponRegistry.SelectedWeapon;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _s.Special.CapOverrideSeconds = 120f;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            if (_s != null) yield return _s.Bitir();
            WeaponRegistry.SelectedWeapon = _selected;
        }
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Special_ReloadCompletionCancellationDisable_ReleasesAllParts()
        {
            PlayerSkinnedVisual body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(body);
            foreach (SpecialWeaponId id in new[] { SpecialWeaponId.Namekuji, SpecialWeaponId.Raiju })
            {
                _s.Controller.ActivateSpecialWeapon(id);
                yield return SpecialWeaponSahne.Kareler(40);
                Assert.IsTrue(_s.Controller.SpecialWeaponActive);
                for (int exit = 0; exit < 3; exit++)
                {
                    SpecialWeaponRuntime runtime = _s.Controller.Special;
                    // Spend a real round through the runtime; no ammo/refill reflection or synthetic reload event.
                    int beforeShot = runtime.Ammo;
                    _s.Girdi.AtesBasti = _s.Girdi.AtesBasili = true;
                    float fireStart = Time.time;
                    while (runtime.Ammo >= beforeShot && Time.time - fireStart < 5f) yield return null;
                    _s.Girdi.AtesBasti = _s.Girdi.AtesBasili = false;
                    Assert.Less(runtime.Ammo, beforeShot, "real input must spend a round");
                    Assert.IsTrue(runtime.IsReloading || runtime.TryBeginReload());
                    yield return null;
                    Assert.IsTrue(body.SpecialReloadPresentationActive, "FeelEvents start must drive presentation");
                    int ammoBefore = runtime.Ammo;
                    float t0 = Time.time;
                    while (!body.SpecialMagazineVisual.InHand && runtime.IsReloading && Time.time - t0 < 8f) yield return null;
                    Assert.IsTrue(body.SpecialMagazineVisual.InHand, "canister/cell detach reaches hand");
                    int layer = body.Animator.GetLayerIndex(CharacterAnimatorContract.OverrideUpperLayer);
                    Assert.GreaterOrEqual(layer, 0);
                    string expected = CharacterAnimatorContract.PlayerWeaponClipV3(body.WeaponAnimSet, "reload_tac");
                    Assert.IsTrue(System.Array.Exists(body.Animator.GetCurrentAnimatorClipInfo(layer), c => c.clip != null && c.clip.name == expected),
                        "part timer fallback must not hide a missing v009 reload clip: " + expected);
                    if (exit == 0)
                    {
                        while (runtime.IsReloading && Time.time - t0 < 12f) yield return null;
                        yield return null;
                        Assert.Greater(runtime.Ammo, ammoBefore, "gameplay completion owns ammo");
                    }
                    else if (exit == 1)
                    {
                        _s.Controller.CancelSpecialWeapon();
                        yield return null;
                    }
                    else
                    {
                        body.enabled = false;
                        Assert.IsTrue(runtime.IsReloading, "disable only clears presentation");
                        Assert.AreEqual(ammoBefore, runtime.Ammo, "presentation must not refill ammo");
                        body.enabled = true;
                    }
                    Assert.IsFalse(body.SpecialReloadPresentationActive);
                    Assert.IsFalse(body.SpecialMagazineVisual.InHand, "no orphan part after exit");
                    Assert.AreEqual(MagazineOwner.Weapon, body.SpecialMagazine.Owner);
                    if (exit != 0)
                    {
                        if (_s.Controller.SpecialWeaponActive) _s.Controller.CancelSpecialWeapon();
                        yield return SpecialWeaponSahne.Kareler(30);
                        _s.Controller.ActivateSpecialWeapon(id);
                        yield return SpecialWeaponSahne.Kareler(40);
                    }
                }
                _s.Controller.CancelSpecialWeapon();
                yield return SpecialWeaponSahne.Kareler(30);
            }
        }
    }
}
