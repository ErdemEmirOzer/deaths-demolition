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
        private static void TraceReload(PlayerSkinnedVisual body, SpecialWeaponRuntime runtime, string beat)
        {
            Animator a = body.Animator;
            int layer = a.GetLayerIndex(CharacterAnimatorContract.OverrideUpperLayer);
            if (layer < 0) { Debug.Log("[E5-RELOAD] " + beat + " layer-missing initialized=" + a.isInitialized); return; }
            AnimatorStateInfo state = a.GetCurrentAnimatorStateInfo(layer), next = a.GetNextAnimatorStateInfo(layer);
            string Clips(AnimatorClipInfo[] cs) => string.Join(",", System.Array.ConvertAll(cs, c => c.clip != null ? c.clip.name + "@" + c.weight + "/length=" + c.clip.length : "null"));
            Debug.Log("[E5-RELOAD] " + beat + " frame=" + Time.frameCount + " time=" + Time.time + " dt=" + Time.deltaTime +
                " initialized=" + a.isInitialized + " animatorSpeed=" + a.speed + " layer=" + layer + " transition=" + a.IsInTransition(layer) +
                " current=" + state.fullPathHash + "/phase=" + state.normalizedTime + "/length=" + state.length + "/speed=" + state.speed + "/mult=" + state.speedMultiplier +
                " next=" + next.fullPathHash + "/phase=" + next.normalizedTime + "/length=" + next.length + "/speed=" + next.speed + "/mult=" + next.speedMultiplier +
                " clips=[" + Clips(a.GetCurrentAnimatorClipInfo(layer)) + "] nextClips=[" + Clips(a.GetNextAnimatorClipInfo(layer)) +
                "] reloadRate=" + a.GetFloat(CharacterAnimatorContract.ReloadRate) + " set=" + body.WeaponAnimSet +
                " presentation=" + body.SpecialReloadPresentationActive + " runtime=" + runtime.IsReloading + "/progress=" + runtime.ReloadProgress01);
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
                    TraceReload(body, runtime, "before-request");
                    Assert.IsTrue(runtime.IsReloading || runtime.TryBeginReload());
                    TraceReload(body, runtime, "after-request");
                    yield return null;
                    TraceReload(body, runtime, "first-evaluation");
                    Assert.IsTrue(body.SpecialReloadPresentationActive, "FeelEvents start must drive presentation");
                    int ammoBefore = runtime.Ammo;
                    float t0 = Time.time;
                    int traceFrames = 0, lastState = 0, lastNext = 0;
                    while (!body.SpecialMagazineVisual.InHand && runtime.IsReloading && Time.time - t0 < 8f)
                    {
                        int traceLayer = body.Animator.GetLayerIndex(CharacterAnimatorContract.OverrideUpperLayer);
                        int state = traceLayer >= 0 ? body.Animator.GetCurrentAnimatorStateInfo(traceLayer).fullPathHash : 0;
                        int nextState = traceLayer >= 0 ? body.Animator.GetNextAnimatorStateInfo(traceLayer).fullPathHash : 0;
                        if (traceFrames < 12 || state != lastState || nextState != lastNext)
                            TraceReload(body, runtime, "wait-" + traceFrames);
                        traceFrames++; lastState = state; lastNext = nextState;
                        yield return null;
                    }
                    TraceReload(body, runtime, "in-hand-check");
                    Assert.IsTrue(body.SpecialMagazineVisual.InHand, "canister/cell detach reaches hand");
                    int layer = body.Animator.GetLayerIndex(CharacterAnimatorContract.OverrideUpperLayer);
                    Assert.GreaterOrEqual(layer, 0);
                    string expected = CharacterAnimatorContract.PlayerWeaponClipV3(body.WeaponAnimSet, "reload_tac");
                    AnimatorClipInfo[] clips = body.Animator.GetCurrentAnimatorClipInfo(layer);
                    AnimatorClipInfo[] nextClips = body.Animator.GetNextAnimatorClipInfo(layer);
                    string observed = string.Join(",", System.Array.ConvertAll(clips, c => c.clip != null ? c.clip.name + "@" + c.weight : "null"));
                    string next = string.Join(",", System.Array.ConvertAll(nextClips, c => c.clip != null ? c.clip.name + "@" + c.weight : "null"));
                    Assert.IsTrue(System.Array.Exists(clips, c => c.clip != null && c.clip.name == expected),
                        "part timer fallback must not hide a missing v009 reload clip: " + expected +
                        " current=[" + observed + "] next=[" + next + "] state=" + body.Animator.GetCurrentAnimatorStateInfo(layer).fullPathHash +
                        " normalized=" + body.Animator.GetCurrentAnimatorStateInfo(layer).normalizedTime +
                        " transition=" + body.Animator.IsInTransition(layer) + " set=" + body.WeaponAnimSet + " v3=" + body.SpecialV3Active +
                        " Weapon=" + body.Animator.GetInteger(CharacterAnimatorContract.Weapon) + " SwHold=" + body.Animator.GetInteger(CharacterAnimatorContract.SwHold) +
                        " Alive=" + body.Animator.GetBool(CharacterAnimatorContract.Alive) + " empty=" + body.Animator.GetBool(CharacterAnimatorContract.ReloadEmpty) +
                        " reloadProgress=" + runtime.ReloadProgress01 + " duration=" + runtime.ReloadDuration + " ammo=" + runtime.Ammo);
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
