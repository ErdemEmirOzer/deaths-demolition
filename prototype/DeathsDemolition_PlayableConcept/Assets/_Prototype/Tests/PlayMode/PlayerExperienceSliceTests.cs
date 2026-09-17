using System;
using System.Collections;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-059 · the player's hurt / death chain in the real Run scene: the hurt clip follows
    /// the blow's direction (frontal gut step vs upper-body), never a control lock (REQ-045);
    /// the death beat slows the world from GameFeelSettings, pushes the camera in and gives
    /// the clock back before the results hand-off; a hit during a Special pickup / hold
    /// changes nothing about the Special; the hurt path allocates nothing per frame.
    /// </summary>
    [TestFixture]
    [Category("WP059")]
    public sealed class PlayerExperienceSliceTests
    {
        private const int HarnessAllocationSlack = 12;
        /// <summary>Frames a per-frame counter sample is looked for after the frame it belongs to (the profiler may hand it over late).</summary>
        private const int SampleWindowFrames = 6;
        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            DeathSequence.Cancel();
            DeathSequence.ReleasePushIn();
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            RunFlowClock.ClearHolds();
            Time.timeScale = 1f;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _s.Rig.Health.DiagnosisInvulnerable = false;
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>A blow from <paramref name="fromLocal"/> (character space: forward = in front of the player) worth <paramref name="fraction"/> of max health.</summary>
        private void Vur(Vector3 fromLocal, float fraction)
        {
            Transform t = _s.Rig.transform;
            Vector3 kaynak = t.position + t.TransformDirection(fromLocal.normalized) * 1.5f;
            _s.Rig.Health.ApplyDamage(_s.Rig.Health.Max * fraction, kaynak, "Test.Hurt");
        }

        private static bool StateOn(Animator a, string layerName, string state)
        {
            if (a == null) return false;
            int layer = a.GetLayerIndex(layerName);
            if (layer < 0) return false;
            return a.GetCurrentAnimatorStateInfo(layer).IsName(state) || a.GetNextAnimatorStateInfo(layer).IsName(state);
        }

        // ------------------------------------------------------------------ hurt clip by direction

        [UnityTest]
        public IEnumerator Yaralanma_YoneVeGuceGore_HurtAveB_KontrolKilitlenmez()
        {
            yield return Kur();
            PlayerProceduralAnimator anim = _s.Rig.Animation;
            PlayerSkinnedVisual skinned = anim.Skinned;
            Assert.IsNotNull(skinned, "AP-006 skinned body yok.");
            Assert.IsTrue(skinned.Authored, "Player animator authored değil.");
            Animator a = skinned.Animator;
            int baseLayer = 0;
            int upper = a.GetLayerIndex(CharacterAnimatorContract.OverrideUpperLayer);
            Assert.GreaterOrEqual(upper, 0, "override_upper katmanı yok.");
            int hurtsBefore = anim.HurtCount;

            // 1) A heavy blow from the front, standing → the gut hit with a step (hurt_B, base layer).
            Vur(Vector3.forward, 0.2f);
            yield return null;
            Assert.AreEqual(hurtsBefore + 1, anim.HurtCount, "Hurt clip istenmedi.");
            Assert.IsFalse(anim.LastHurtUpper, "Önden ağır darbe → hurt_B (gövde) olmalı.");
            Assert.AreEqual(1, a.GetInteger(CharacterAnimatorContract.HurtStyle), "HurtStyle 1 = hurt_B");
            yield return null;
            yield return null;
            Assert.IsTrue(StateOn(a, a.GetLayerName(baseLayer), CharacterAnimatorContract.HurtState + "_B"), "Base katmanı Hurt_B'ye geçmedi: " + StateName(a, baseLayer));
            Assert.IsTrue(_s.Rig.Motor.ControlEnabled, "REQ-045: hasar kontrolü kilitledi.");
            Assert.IsFalse(anim.IsDead);

            yield return new WaitForSeconds(1.2f);

            // 2) A blow from the right side → the upper-body hurt (hurt_A, override_upper) over the loco.
            Vur(Vector3.right, 0.2f);
            yield return null;
            Assert.IsTrue(anim.LastHurtUpper, "Yandan darbe → hurt_A (üst gövde) olmalı.");
            Assert.AreEqual(0, a.GetInteger(CharacterAnimatorContract.HurtStyle));
            yield return null;
            yield return null;
            Assert.IsTrue(StateOn(a, CharacterAnimatorContract.OverrideUpperLayer, CharacterAnimatorContract.HurtState + "_A"), "override_upper Hurt_A'ya geçmedi: " + StateName(a, upper));
            Assert.IsTrue(StateOn(a, a.GetLayerName(baseLayer), CharacterAnimatorContract.LocomotionState) || !StateOn(a, a.GetLayerName(baseLayer), CharacterAnimatorContract.HurtState + "_B"),
                "Yandan darbe base katmanını kesti.");
            yield return new WaitForSeconds(1.2f);

            // 3) A light frontal tap stays upper-body.
            Vur(Vector3.forward, 0.05f);
            yield return null;
            Assert.IsTrue(anim.LastHurtUpper, "Hafif önden darbe → hurt_A.");
            yield return new WaitForSeconds(1.2f);

            // 4) Running forward into a heavy frontal blow → upper-body (the gut step would slide); the motor keeps moving.
            _s.Girdi.Yon = Vector2.up;
            yield return SpecialWeaponSahne.Bekle(() => _s.Rig.Motor.PlanarVelocity.magnitude > PlayerProceduralAnimator.HurtStepMaxSpeed, 3f, "koşu hızı");
            Vector3 before = _s.Rig.transform.position;
            Vur(Vector3.forward, 0.2f);
            yield return null;
            Assert.IsTrue(anim.LastHurtUpper, "Koşarken önden darbe → hurt_A.");
            yield return new WaitForSeconds(0.3f);
            Assert.Greater((_s.Rig.transform.position - before).magnitude, 0.3f, "REQ-045: darbe hareketi durdurdu.");
            _s.Girdi.Yon = Vector2.zero;

            Debug.Log("[DD-TEST] wp059.hurt count=" + anim.HurtCount + " health=" + _s.Rig.Health.Current + "/" + _s.Rig.Health.Max);
        }

        private static string StateName(Animator a, int layer)
        {
            AnimatorStateInfo si = a.GetCurrentAnimatorStateInfo(layer);
            return si.shortNameHash + " (" + si.normalizedTime.ToString("F2") + ")";
        }

        // ------------------------------------------------------------------ death beat

        [UnityTest]
        public IEnumerator Olum_Yavaslama_KameraYaklasma_SaatGeriVerilir()
        {
            yield return Kur();
            GameFeelSettings feel = GameFeelSettings.Active;
            Assert.Less(feel.DeathSlowScale, 0.999f, "Ölüm yavaşlaması kapalı (asset).");
            PrototypeCameraRig cam = PrototypeCameraRig.Active;
            Assert.IsNotNull(cam, "Kamera rig'i yok.");
            Transform follow = _s.Rig.CameraFollowTarget;
            float distBefore = (cam.transform.position - follow.position).magnitude;
            int shakesBefore = CameraShakeService.ActiveCount;
            float scaleBefore = Time.timeScale;
            Assert.AreEqual(1f, scaleBefore, 0.001f);

            Vur(Vector3.left, 5f);   // lethal
            yield return null;
            Assert.IsTrue(_s.Rig.Health.IsDead, "Ölüm gerçekleşmedi.");
            Assert.IsTrue(DeathSequence.IsActive, "Ölüm sekansı başlamadı.");
            Assert.AreEqual(RunPhase.Dead, RunDirector.Instance.Phase);
            Assert.IsFalse(_s.Rig.Motor.ControlEnabled, "Ölümde kontrol kapanmalı (tek istisna).");
            Assert.Greater(CameraShakeService.ActiveCount, shakesBefore, "Ölüm sarsıntısı yok.");

            yield return new WaitForSecondsRealtime(feel.DeathSlowInSeconds + 0.15f);
            Assert.AreEqual(feel.DeathSlowScale, Time.timeScale, 0.05f, "Yavaşlama ölçeği tutmadı.");
            Assert.AreEqual(1f, DeathSequence.PushIn01, 0.05f);
            yield return null;
            float distPeak = (cam.transform.position - follow.position).magnitude;
            Assert.Less(distPeak, distBefore - 0.8f, "Kamera yaklaşmadı: " + distBefore.ToString("F2") + " → " + distPeak.ToString("F2"));

            // The beat ends on its own, before the results hand-off, and the clock is 1 again (or frozen by a hold — never 0.3).
            float total = feel.DeathSlowTotalSeconds;
            yield return SpecialWeaponSahne.Bekle(() => !DeathSequence.IsActive, total + 1f, "ölüm sekansı bitmedi");
            Assert.IsTrue(Time.timeScale > 0.99f || RunFlowClock.IsFrozen, "Sekans sonrası saat " + Time.timeScale);
            Assert.AreEqual(1f, DeathSequence.PushIn01, 1e-3f, "push-in Results'a kadar tutulur");
            yield return null;
            float distHeld = (cam.transform.position - follow.position).magnitude;
            Assert.Less(distHeld, distBefore - 0.8f, "Kamera sekans bitince geri sıçradı: " + distHeld.ToString("F2"));
            Assert.Less(total, feel.DeathResultsDelaySeconds, "Yavaşlama Results teslimini geçiyor.");
            Debug.Log("[DD-TEST] wp059.death scale=" + feel.DeathSlowScale + " total=" + total.ToString("F2") + "s pushIn=" + feel.DeathCameraPushIn +
                      " dist " + distBefore.ToString("F2") + "→" + distPeak.ToString("F2") + " resultsDelay=" + feel.DeathResultsDelaySeconds);
        }

        // ------------------------------------------------------------------ hit during pickup / special hold

        [UnityTest]
        public IEnumerator SpecialAlirkenVeTutarken_Vurulma_SpecialSurer_KontrolAcik()
        {
            yield return Kur();
            int fired = 0, deactivated = 0;
            Action<SpecialUsageEvent> dinle = e =>
            {
                if (e.Kind == SpecialUsageKind.Fired) fired++;
                if (e.Kind == SpecialUsageKind.Deactivated) deactivated++;
            };
            SpecialWeaponUsageEvents.Usage += dinle;
            try
            {
                _s.Controller.ActivateSpecialWeapon(SpecialWeaponId.TideJewel);
                yield return null;
                Assert.IsTrue((WeaponEvents.Special.Phase == SpecialWeaponPhase.Active), "Special etkin değil.");
                Assert.IsTrue(_s.Controller.PickupDonePending, "pickup_done bekleniyor olmalı (WP-042C).");

                // Hit while the grab clip plays: control stays, the Special stays, the grab still finishes.
                Vur(Vector3.forward, 0.2f);
                yield return null;
                Assert.IsTrue(_s.Rig.Motor.ControlEnabled, "REQ-045: pickup sırasında hasar kontrolü kilitledi.");
                Assert.IsTrue((WeaponEvents.Special.Phase == SpecialWeaponPhase.Active), "Pickup sırasında hasar Special'ı düşürdü.");
                Assert.AreEqual(0, deactivated);
                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.PickupDonePending, 1.5f, "pickup_done (hasara rağmen)");

                // Hit while holding: still armed, still fires.
                Vur(Vector3.right, 0.2f);
                yield return null;
                Assert.IsTrue((WeaponEvents.Special.Phase == SpecialWeaponPhase.Active), "Tutarken hasar Special'ı düşürdü.");
                Assert.AreEqual(0, deactivated);
                _s.Girdi.AtesBasti = true;
                _s.Girdi.AtesBasili = true;
                yield return SpecialWeaponSahne.Bekle(() => fired >= 1, 3f, "hasar sonrası Special ateşi");
                _s.Girdi.AtesBasti = false;
                _s.Girdi.AtesBasili = false;
                Assert.IsTrue(_s.Rig.Motor.ControlEnabled);
                Assert.IsFalse(_s.Rig.Animation.IsDead);
                Debug.Log("[DD-TEST] wp059.special-hit fired=" + fired + " deactivated=" + deactivated + " health=" + _s.Rig.Health.Current);
            }
            finally
            {
                SpecialWeaponUsageEvents.Usage -= dinle;
            }
        }

        // ------------------------------------------------------------------ hurt path allocation

        [UnityTest]
        public IEnumerator YaralanmaYolu_KareBasiAyirma_Sifir()
        {
            yield return Kur();
            _s.Rig.Health.DiagnosisInvulnerable = false;
            bool profilerWas = UnityEngine.Profiling.Profiler.enabled;
            UnityEngine.Profiling.Profiler.enabled = true;
            // WP-059 finding: with Application.targetFrameRate capped (the bootstrapper sets 60 once a fixture has
            // booted the full flow, e.g. FlowSmokeTest) the recorders' LastValue never updates in the Editor test
            // runner (CurrentValue still moves) — every frame sample reads 0, the positive control fails. Uncapped
            // for the measurement, restored after; reads are the max over a short window for good measure.
            int fpsWas = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            yield return null;
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null;
                yield return null;
                Assert.IsTrue(bytes.Valid && count.Valid, "Profiler kare sayaçları yok.");

                // Positive control (the counter is read over the next three frames: which frame a coroutine step lands in
                // relative to the profiler's frame boundary is not ours to fix; the max over three is).
                var yem = new object[300];
                for (int i = 0; i < yem.Length; i++) yem[i] = new byte[80];
                long control = 0;
                var diag = new System.Text.StringBuilder("[DD-TEST] wp059.hurt-alloc control frames:");
                for (int f = 0; f < SampleWindowFrames; f++)
                {
                    yield return null;
                    control = Math.Max(control, count.LastValue);
                    diag.Append(" last=").Append(count.LastValue);
                }
                Debug.Log(diag.ToString());
                Assert.Greater(control, 250, "Pozitif kontrol: sayaç 300 ayırmayı görmedi (" + control + ", profiler " + UnityEngine.Profiling.Profiler.enabled + ").");
                GC.KeepAlive(yem);

                // Warm-up hits (both clips), then settle.
                Vur(Vector3.forward, 0.02f); yield return null;
                Vur(Vector3.right, 0.02f); yield return null;
                yield return new WaitForSeconds(1.5f);
                long idleSum = 0; int idleN = 5;
                for (int i = 0; i < idleN; i++) { yield return null; idleSum += count.LastValue; }
                long idle = idleSum / idleN;

                // Ten hits (alternating direction), health never near zero (0.5 % each); each hit frame read over the window.
                long worst = 0, worstBytes = 0;
                for (int i = 0; i < 10; i++)
                {
                    Vur((i & 1) == 0 ? Vector3.forward : Vector3.right, 0.005f);
                    for (int f = 0; f < SampleWindowFrames; f++)
                    {
                        yield return null;
                        if (count.LastValue > worst) { worst = count.LastValue; worstBytes = bytes.LastValue; }
                    }
                }

                Debug.Log("[DD-TEST] wp059.hurt-alloc idle=" + idle + " worstHitFrame=" + worst + "/" + worstBytes + "B");
                Assert.LessOrEqual(worst, idle + HarnessAllocationSlack, "Yaralanma yolu karede ayırma yaptı: " + worst + " (idle " + idle + ").");
            }

            UnityEngine.Profiling.Profiler.enabled = profilerWas;
            Application.targetFrameRate = fpsWas;
        }
    }
}
