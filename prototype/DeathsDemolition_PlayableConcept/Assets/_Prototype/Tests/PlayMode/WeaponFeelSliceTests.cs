using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-042 · B (DEC-082: no pickup lock — movement and aim keep running through the grab,
    /// a hit does not cancel the Special, death ends it) and C (mechanism slots: casing per
    /// shot on A/B, at the pump on C; magazine at the reload start; the shotgun ring pattern
    /// fires through the real controller; the equipped weapon's rumble profile is live).
    /// Uses the WP-007 kit (KUR-B): live Run scene, scripted input, config copy.
    /// </summary>
    [TestFixture]
    [Category("WP042")]
    public sealed class WeaponFeelSliceTests
    {
        private const float Pencere = 0.5f;

        private SpecialWeaponSahne _s;
        private readonly List<WeaponMechanismEvent> _mechanism = new List<WeaponMechanismEvent>(64);

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            WeaponEvents.Mechanism -= OnMechanism;
            _mechanism.Clear();
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            RunFlowClock.ClearHolds();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            }

            LogAssert.ignoreFailingMessages = false;
            FeelRuntime.ResetForTests();
            FeelRuntime.Initialize(null);
        }

        private IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _mechanism.Clear();
            WeaponEvents.Mechanism += OnMechanism;
        }

        private void OnMechanism(WeaponMechanismEvent e) => _mechanism.Add(e);

        private int Count(WeaponMechanismKind kind)
        {
            int n = 0;
            for (int i = 0; i < _mechanism.Count; i++) if (_mechanism[i].Kind == kind) n++;
            return n;
        }

        private IEnumerator SpawnVeAl()
        {
            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            Assert.IsNotNull(pickup, "Pickup yok.");
            _s.PickupaGit(pickup);
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "alma");
        }

        [UnityTest]
        [Category("WP062")]
        public IEnumerator StagedNFields_CommitAtRealRunSceneBoundary_BeforeSetup()
        {
            FeelRuntime.ResetForTests();
            FeelRuntime.Initialize(null);
            FeelConfigData staged = FeelConfig.CreateDefaults();
            staged.camera.heightMeters = 18f;
            BudgetCategoryFeelRow impact = System.Array.Find(staged.budgets.categories, row => row.id == "Impact");
            impact.capacity = 31;
            Assert.IsTrue(FeelRuntime.QueueAuthored(staged, "run-boundary", "22c8ead", out string error), error);
            Assert.IsTrue(FeelRuntime.BeginFrame(62062));
            Assert.IsTrue(FeelRuntime.HasPendingNextRun);
            Assert.AreNotEqual(18f, FeelRuntime.Current.Camera.heightMeters);

            yield return Kur();

            Assert.AreEqual(18f, FeelRuntime.Current.Camera.heightMeters, 1e-6f);
            Assert.AreEqual(31, FeelRuntime.Current.Budgets.CapacityFor("Impact", -1));
            Assert.IsFalse(FeelRuntime.HasPendingNextRun);
        }

        // ------------------------------------------------------------------ B · DEC-082 no pickup lock

        [UnityTest]
        public IEnumerator DEC082_AlmaSirasinda_HareketVeNisanKilitlenmez()
        {
            yield return Kur();

            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            Assert.IsNotNull(pickup);
            _s.Girdi.Yon = new Vector2(1f, 0f);
            _s.Girdi.Imlec = new Vector2(Screen.width * 0.9f, Screen.height * 0.5f);
            _s.PickupaGit(pickup);
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "alma");

            // The AP-009C grab clip is 0.8 s long; through that window the motor keeps the stick and moves.
            float t0 = Time.time;
            Vector3 p0 = _s.Rig.transform.position;
            float maxSpeed = 0f;
            Vector3 aim0 = _s.Rig.Aim.AimDirection;
            while (Time.time - t0 < 0.8f)
            {
                Assert.AreEqual(1f, _s.Rig.Motor.RawInput.x, 1e-3f, "motor girdisi alma sırasında düşmez");
                maxSpeed = Mathf.Max(maxSpeed, _s.Rig.Motor.PlanarVelocity.magnitude);
                yield return null;
            }

            Assert.Greater(maxSpeed, 0.5f, "oyuncu alma sırasında hareket etti (m/s)");
            Assert.Greater((_s.Rig.transform.position - p0).magnitude, 0.3f, "yer değiştirme");
            _s.Girdi.Imlec = new Vector2(Screen.width * 0.1f, Screen.height * 0.5f);
            // The aim turns at its own rate; give it up to a second (it must move at all — a lock would hold it).
            yield return SpecialWeaponSahne.Bekle(() => Vector3.Angle(aim0, _s.Rig.Aim.AimDirection) > 20f, 1f, "nişan alma sırasında imleci takip eder");
            Assert.IsTrue(_s.Controller.SpecialWeaponActive, "Special aktif kaldı");
        }

        [UnityTest]
        public IEnumerator AlmaSirasindaVurulma_SpecialAktifKalir_SureIsler_AtesEder()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return SpawnVeAl();
                float kalan0 = WeaponEvents.Special.ActiveRemaining;
                // A hit inside the grab window: the pickup is committed at the trigger, the clip is cosmetic.
                _s.Rig.Health.DiagnosisInvulnerable = false;   // the kit opens the player invulnerable (diagnosis); this test needs a real hit
                Assert.IsTrue(_s.Rig.Health.ApplyDamage(5f, _s.Rig.transform.position + Vector3.forward * 2f, "test.hit"), "vuruş uygulanmalı");
                yield return SpecialWeaponSahne.Kareler(6);
                Assert.IsTrue(_s.Controller.SpecialWeaponActive, "vuruş Special'ı iptal etmez");
                Assert.AreEqual(SpecialWeaponPhase.Active, WeaponEvents.Special.Phase);
                Assert.Less(WeaponEvents.Special.ActiveRemaining, kalan0, "aktif süre işlemeye devam eder");
                CollectionAssert.IsEmpty(sayac.Iptaller, "iptal yok");

                _s.Girdi.AtesBasili = true;
                yield return SpecialWeaponSahne.Bekle(() => sayac.CueSayisi(CombatAudioEvent.HeavyWeaponFire) > 0, 5f, "Special ateşi");
                _s.Girdi.AtesBasili = false;
            }
        }

        [UnityTest]
        public IEnumerator AlmaSirasindaOlum_SpecialBiter_ElModeliGizlenir()
        {
            yield return Kur();
            LogAssert.ignoreFailingMessages = true;
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return SpawnVeAl();
                // WP-032C: the grab clip hands the weapon over at pickup_done (0.73 s; 0.8 s fallback) — the hand model shows then.
                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.PickupDonePending, 1.5f, "pickup_done / yedek");
                Assert.IsTrue(_s.Controller.SpecialRigVisible, "aktifken el rig'i görünür");
                _s.Rig.Health.DiagnosisInvulnerable = false;
                Assert.IsTrue(_s.Rig.Health.ApplyDamage(100000f, _s.Rig.transform.position + Vector3.forward * 2f, "test.kill"), "öldürücü vuruş uygulanmalı");
                Assert.IsTrue(_s.Rig.Health.IsDead, "oyuncu öldü");
                // Death ends the Special well before its own timer (11 s) would.
                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.SpecialWeaponActive, 4f, "ölümle bitiş");
                Assert.IsFalse(_s.Controller.SpecialRigVisible, "ölümde el modeli gizlenir");
                Assert.IsTrue(sayac.Iptaller.Count > 0 || sayac.FazSirasi.Contains(SpecialWeaponPhase.None), "koşu sonu iptali / faz None");
            }
        }

        // ------------------------------------------------------------------ C · mechanism slots

        [UnityTest]
        public IEnumerator Mekanizma_A_KovanHerAtista_SarjorReloadBasinda_ZamanlayiciYedegi()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return null;
            int ammo0 = _s.Controller.Classic.Ammo;
            _s.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.Ammo <= ammo0 - 3, 3f, "üç atış");
            _s.Girdi.AtesBasili = false;
            // WP-068 U12a: the HUD snapshot carries the runtime's own spread (bloom included) — the honest reticle input.
            Assert.AreEqual(_s.Controller.Classic.CurrentSpreadDegrees, WeaponEvents.Classic.CurrentSpreadDegrees, 1e-5f, "U12a: anlık yayılma snapshot'ta");
            Assert.AreEqual(1, WeaponEvents.Classic.PelletCount);
            Assert.AreEqual(0f, WeaponEvents.Classic.PelletRingDegrees, "A: halka yok");
            yield return SpecialWeaponSahne.Kareler(2);
            int shots = ammo0 - _s.Controller.Classic.Ammo;
            Assert.AreEqual(shots, Count(WeaponMechanismKind.CasingEjected), "A: her atışta bir kovan");
            for (int i = 0; i < _mechanism.Count; i++)
            {
                Assert.AreEqual(ClassicWeaponId.AssaultRifle, _mechanism[i].Weapon);
                Assert.IsFalse(_mechanism[i].FromClipEvent, "A/B kovanı atışta yayınlanır (klipten değil — WP-042C: additive fire klibi otomatik ateşte olayı düşürüyor)");
                Assert.Greater(_mechanism[i].Direction.magnitude, 0.9f, "yön birim");
            }

            _s.Girdi.DoldurBasti = true;
            yield return null;
            _s.Girdi.DoldurBasti = false;
            yield return SpecialWeaponSahne.Bekle(() => Count(WeaponMechanismKind.MagazineDropped) == 1, 1.5f, "şarjör düşme (mag_out: klip ya da 0,4 s yedek)");
            yield return SpecialWeaponSahne.Bekle(() => Count(WeaponMechanismKind.ShellSeated) == 1, 2f, "şarjör takma (mag_in: klip ya da yedek)");
            Debug.Log("[DD-TEST] wp042c.mechanism A clipDriven=" + _s.Controller.ClipDrivesMechanism + " magOutFromClip=" + _mechanism.Find(e => e.Kind == WeaponMechanismKind.MagazineDropped).FromClipEvent);
            Assert.AreEqual(1, Count(WeaponMechanismKind.MagazineDropped), "reload başına tek şarjör");
        }

        [UnityTest]
        public IEnumerator Mekanizma_C_KovanPumpta_FisekUcKez_HalkaDeseniGercekAtista()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.CombatShotgun, _s.Config);
            yield return null;
            Assert.IsTrue(_s.Controller.Classic.UsesPelletRing, "C halka deseni");
            int ammo0 = _s.Controller.Classic.Ammo;
            _s.Girdi.AtesBasti = true;
            _s.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.Ammo == ammo0 - 1, 2f, "tek atış");
            Assert.AreEqual(_s.Controller.Classic.PelletCount, WeaponEvents.Classic.PelletCount, "U12a: pellet sayısı snapshot'ta");
            Assert.AreEqual(_s.Controller.Classic.PelletRingDegrees, WeaponEvents.Classic.PelletRingDegrees, 1e-5f, "U12a: halka yarı açısı snapshot'ta");
            Assert.Greater(WeaponEvents.Classic.PelletRingDegrees, 0f, "C: ikinci halka");
            _s.Girdi.AtesBasti = false;
            _s.Girdi.AtesBasili = false;
            Assert.AreEqual(0, Count(WeaponMechanismKind.CasingEjected), "C: kovan atış anında değil, pump'ta");
            yield return SpecialWeaponSahne.Bekle(() => Count(WeaponMechanismKind.CasingEjected) == 1, 1.5f, "pump (eject ≈ 0,4 s)");

            // WP-059C: the fire clip's own pump (eject f12, chamber f20 ≈ 0.67 s) may land before or during the
            // reload (the additive fire clip keeps playing under the override reload — v006b) or be cut (v005b):
            // the shot's pump contributes 0–1 chambers, the reload's pump exactly one, and the reload's is the LAST
            // mechanism event (chamber f60 after mag_in_3 f46).
            _s.Girdi.DoldurBasti = true;
            yield return null;
            _s.Girdi.DoldurBasti = false;
            float reload = _s.Controller.Classic.ReloadDuration;
            Assert.Greater(reload, 1f);
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.Classic.IsReloading, reload + 1f, "reload biter");
            Assert.AreEqual(0, Count(WeaponMechanismKind.MagazineDropped), "C: şarjör düşmez (fişek)");
            Assert.AreEqual(3, Count(WeaponMechanismKind.ShellSeated), "C: üç fişek slotu (AP-009C mag_in / mag_in_2 / mag_in_3; klip ya da yedek)");
            Assert.That(Count(WeaponMechanismKind.Chambered), Is.InRange(1, 2), "C: atışın pump'ı (0–1) + reload'un pump chamber'ı (1)");
            Assert.AreEqual(WeaponMechanismKind.Chambered, _mechanism[_mechanism.Count - 1].Kind, "C: reload'un son mekanizma olayı chamber (pump)");
        }

        [UnityTest]
        public IEnumerator Titresim_TakiliSilahaGore_KusanmadaOnbellek()
        {
            yield return Kur();
            var rumble = new DD.Prototype.Inputs.GamepadRumble();
            try
            {
                rumble.Subscribe();
                _s.Controller.EquipClassic(ClassicWeaponId.CombatShotgun, _s.Config);
                yield return null;
                ClassicWeaponSettings c = _s.Config.Weapons.Find(ClassicWeaponId.CombatShotgun);
                Assert.AreEqual(c.RumbleFraction, rumble.WeaponFireFraction, 1e-4f);
                Assert.AreEqual(c.RumbleMilliseconds, rumble.WeaponFireMilliseconds);
                _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
                yield return null;
                ClassicWeaponSettings a = _s.Config.Weapons.Find(ClassicWeaponId.AssaultRifle);
                Assert.AreEqual(a.RumbleFraction, rumble.WeaponFireFraction, 1e-4f);
            }
            finally
            {
                rumble.Unsubscribe();
                rumble.Dispose();
            }
        }
    }
}
