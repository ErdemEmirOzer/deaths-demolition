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
        /// <summary>Frame-noise allowance between a measured frame and its control frame (allocation counts).</summary>
        private const int HarnessAllocationSlack = 12;
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
            // WP-095 (R3, his sözleşmesi §2; C-00 K1–K7 08.10): top-down shakes through TopDownFeelCamera — the death's PlayerDamaged
            // (fatal = heavy) is world trauma (K2), not a CameraShakeService request; first / third person keep the service
            bool topDown = CameraModeService.Current == CameraMode.TopDown;
            TopDownFeelCamera td = cam.TopDownFeel;
            int worldBefore = td != null ? td.WorldEvents : 0;
            float scaleBefore = Time.timeScale;
            Assert.AreEqual(1f, scaleBefore, 0.001f);

            Vur(Vector3.left, 5f);   // lethal
            float tOlum = Time.realtimeSinceStartup;   // S-02 (f2a): the beat's real-time length is measured (Results comes at resultsDelay real)
            yield return null;
            Assert.IsTrue(_s.Rig.Health.IsDead, "Ölüm gerçekleşmedi.");
            Assert.IsTrue(DeathSequence.IsActive, "Ölüm sekansı başlamadı.");
            Assert.AreEqual(RunPhase.Dead, RunDirector.Instance.Phase);
            Assert.IsFalse(_s.Rig.Motor.ControlEnabled, "Ölümde kontrol kapanmalı (tek istisna).");
            if (topDown)
            {
                Assert.IsNotNull(td, "TD his kamerası yok.");
                Assert.Greater(td.WorldEvents, worldBefore, "Ölüm sarsıntısı yok (TD: K2 dünya travması).");
            }
            else
            {
                Assert.Greater(CameraShakeService.ActiveCount, shakesBefore, "Ölüm sarsıntısı yok.");
            }

            yield return new WaitForSecondsRealtime(feel.DeathSlowInSeconds + 0.15f);
            Assert.AreEqual(feel.DeathSlowScale, Time.timeScale, 0.05f, "Yavaşlama ölçeği tutmadı.");
            Assert.AreEqual(1f, DeathSequence.PushIn01, 0.05f);
            yield return null;
            float distPeak = (cam.transform.position - follow.position).magnitude;
            Assert.Less(distPeak, distBefore - 0.8f, "Kamera yaklaşmadı: " + distBefore.ToString("F2") + " → " + distPeak.ToString("F2"));

            // The beat ends on its own, before the results hand-off, and the clock is 1 again (or frozen by a hold — never 0.3).
            float total = feel.DeathSlowTotalSeconds;
            yield return SpecialWeaponSahne.Bekle(() => !DeathSequence.IsActive, total + 1f, "ölüm sekansı bitmedi");
            float vurusGercek = Time.realtimeSinceStartup - tOlum;
            Debug.Log("[DD-TEST] wp095.death beatEndReal=" + vurusGercek.ToString("F2") + "s total=" + total.ToString("F2") + "s frozen=" + RunFlowClock.IsFrozen +
                      " fixedDt=" + Time.fixedDeltaTime.ToString("F4"));
            Assert.Less(vurusGercek, total + 0.3f, "WP-095 K1: ölüm vuruşu gerçek zamanda uzadı (hakem / donma)");
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
                _s.Controller.ActivateSpecialWeapon(SpecialWeaponId.Namekuji);
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

        /// <summary>
        /// WP-059 / WP-059D: the hurt path (PlayerHealth.ApplyDamage → Damaged/HealthChanged subscribers: feedback,
        /// flinch, shake, rumble, HUD edges, audio) allocates nothing per hit.
        ///
        /// WP-059D: the old form took the WORST frame of a 6-frame window after each of 10 hits and failed once in a
        /// full suite run (S-08, main ~eb0ff98: one frame of 3 370 allocations / 200 KB ≈ 59 B each, idle 7; the two
        /// full runs before it passed). A per-hit allocation would have shown on all ten hits in every run, so that
        /// frame was scene noise inside the window, not the hurt path. Deterministic form (the WP-044 referee
        /// method, WP-032H pattern): inside ONE frame, <see cref="HurtAmplify"/> hits go through the real path; a
        /// control frame runs the same loop without the damage call; min over <see cref="HurtPairs"/> pairs on both
        /// sides (a stray spike needs to hit every pair to move a min), plus the frame after the burst for
        /// delayed per-hit work; a 64 × 1 KB positive control proves the counter sees the frame it is read in.
        /// </summary>
        [UnityTest]
        public IEnumerator YaralanmaYolu_KareBasiAyirma_Sifir()
        {
            const int HurtAmplify = 25, HurtPairs = 4;
            const float HurtFraction = 0.002f;   // 25 hits = 5 % per burst; warm-up + 4 bursts leave ~70 % health (no low-health edge, no death)
            yield return Kur();
            _s.Rig.Health.DiagnosisInvulnerable = false;
            bool profilerWas = UnityEngine.Profiling.Profiler.enabled;
            UnityEngine.Profiling.Profiler.enabled = true;
            // WP-059 finding: with Application.targetFrameRate capped (the bootstrapper sets 60 once a fixture has
            // booted the full flow) the recorders' LastValue never updates in the Editor test runner. Uncapped here.
            int fpsWas = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            yield return null;
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            {
                yield return null;
                yield return null;
                Assert.IsTrue(bytes.Valid && count.Valid, "Profiler kare sayaçları yok.");

                // Warm-up: single hits from both sides (both clips) and one full burst, so one-time sizing (pools,
                // lookups) happens before the measured frames — the WP-032H "first step sizes the arrays" rule.
                Vur(Vector3.forward, 0.02f); yield return null;
                Vur(Vector3.right, 0.02f); yield return null;
                for (int k = 0; k < HurtAmplify; k++) Vur((k & 1) == 0 ? Vector3.forward : Vector3.right, HurtFraction);
                yield return new WaitForSeconds(1.5f);

                long controlMin = long.MaxValue, controlMax = 0, hitMin = long.MaxValue, afterMin = long.MaxValue, hitBytesAtMin = 0;
                var pairs = new System.Text.StringBuilder();
                for (int pair = 0; pair < HurtPairs; pair++)
                {
                    yield return new WaitForSeconds(0.6f);   // the previous burst's clip, shake and edges have settled

                    // control frame: the same loop shape, no damage call
                    Transform t = _s.Rig.transform;
                    Vector3 sink = Vector3.zero;
                    for (int k = 0; k < HurtAmplify; k++) sink += t.position + t.TransformDirection((k & 1) == 0 ? Vector3.forward : Vector3.right) * 1.5f;
                    yield return null;                                  // LastValue = the control frame
                    long control = count.LastValue;

                    for (int k = 0; k < HurtAmplify; k++) Vur((k & 1) == 0 ? Vector3.forward : Vector3.right, HurtFraction);
                    yield return null;                                  // LastValue = the burst frame
                    long hit = count.LastValue, hitBytes = bytes.LastValue;
                    yield return null;                                  // LastValue = the frame after (delayed per-hit work)
                    long after = count.LastValue;

                    controlMin = Math.Min(controlMin, control);
                    controlMax = Math.Max(controlMax, control);
                    if (hit < hitMin) { hitMin = hit; hitBytesAtMin = hitBytes; }
                    afterMin = Math.Min(afterMin, after);
                    pairs.Append(' ').Append(control).Append('/').Append(hit).Append('/').Append(after);
                    if (sink.x == float.MaxValue) Debug.Log(sink);
                }

                var keep = new object[64];
                for (int i = 0; i < keep.Length; i++) keep[i] = new byte[1024];
                yield return null;
                long positiveCount = count.LastValue, positiveBytes = bytes.LastValue;
                GC.KeepAlive(keep);

                Debug.Log("[DD-TEST] wp059d.hurt-alloc hits/frame=" + HurtAmplify + " control/hit/after per pair:" + pairs
                          + " controlMin=" + controlMin + " controlMax=" + controlMax + " hitMin=" + hitMin + " (" + hitBytesAtMin + " B) afterMin=" + afterMin
                          + " positive=" + positiveCount + " / " + positiveBytes + " B health=" + _s.Rig.Health.Current);
                Assert.GreaterOrEqual(positiveCount, 64, "Pozitif kontrol: sayaç 64 × 1 KB ayırmayı görmedi (ölçüm kör).");
                Assert.GreaterOrEqual(positiveBytes, 64 * 1024, "Pozitif kontrol: 64 KB görülmedi.");
                Assert.IsFalse(_s.Rig.Health.IsDead, "Ölçüm sırasında oyuncu öldü (ölüm yolu ayrı ölçülür).");
                // A per-hit allocation adds ≥ HurtAmplify (25) to the burst frame; the gate leaves room only for frame noise.
                Assert.LessOrEqual(hitMin - controlMin, HarnessAllocationSlack,
                    "Yaralanma yolu vuruş başına ayırıyor: " + HurtAmplify + " vuruşluk karede +" + (hitMin - controlMin) + " (kontrol " + controlMin + ").");
                Assert.LessOrEqual(afterMin - controlMin, HarnessAllocationSlack,
                    "Yaralanma yolu vuruştan sonraki karede ayırıyor: +" + (afterMin - controlMin) + " (kontrol " + controlMin + ").");
            }

            UnityEngine.Profiling.Profiler.enabled = profilerWas;
            Application.targetFrameRate = fpsWas;
        }
    }
}
