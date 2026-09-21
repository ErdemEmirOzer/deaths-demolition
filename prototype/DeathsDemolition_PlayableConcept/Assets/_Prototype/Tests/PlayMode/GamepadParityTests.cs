using System.Collections;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Inputs;
using DD.Prototype.Inputs.Testing;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-024 · gamepad parity through the REAL adapter (virtual gamepad on the Input
    /// System event queue): move + deadzone, stick aim + hold-last-direction, fire,
    /// device switch, pause, aim assist (direction only, damage identical), rumble
    /// pulses, UI module bridge. Runs on the Special Weapon scene kit (Run scene, live
    /// director) with the registry switched back to the installer's source.
    /// </summary>
    [TestFixture]
    [Category("WP024")]
    public sealed class GamepadParityTests
    {
        private const int Kare = 6;

        private SpecialWeaponSahne _s;
        private VirtualGamepadDriver _pad;
        private IInputSource _kitInput;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            _pad?.Dispose();
            _pad = null;
            InputPreferencesStore.SetPathOverride(null);
            if (_s != null)
            {
                if (_kitInput != null)
                {
                    InputSourceRegistry.SetCurrent(_kitInput);
                }

                yield return _s.Bitir();
                _s = null;
            }

            Time.timeScale = 1f;
        }

        private IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _kitInput = InputSourceRegistry.Current;

            PrototypeInputInstaller installer = PrototypeInputInstaller.EnsureExists();
            yield return null;
            Assert.IsNotNull(installer.Source, "Gerçek girdi kaynağı kurulmadı.");
            InputSourceRegistry.SetCurrent(installer.Source);
            installer.SetInputEnabled(true);

            // Preferences to a temp file so the test never touches the user's settings.
            InputPreferencesStore.SetPathOverride(System.IO.Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "wp024-test-input-settings.json"));
            InputPreferencesStore.ResetToDefaults();

            _pad = VirtualGamepadDriver.Create();
            _pad.MakeCurrent();
            Assert.IsTrue(_pad.IsConnected, "Sanal gamepad eklenmedi.");
            yield return null;
        }

        private static IExtendedInputSource Src => InputSourceRegistry.Current as IExtendedInputSource;

        private IEnumerator Kareler(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SanalGamepad_HareketOluBolge_NisanYonu_Ates_CihazGecisi_Pause()
        {
            yield return Kur();
            IExtendedInputSource src = Src;
            Assert.IsNotNull(src, "Kaynak IExtendedInputSource değil.");

            // ---- deadzone: a 0.10 push is inside the 0.15 radial deadzone → no movement
            Vector3 p0 = _s.Rig.transform.position;
            _pad.Set(new Vector2(0.10f, 0f), Vector2.zero);
            yield return Kareler(Kare);
            Assert.AreEqual(Vector2.zero, src.Move, "Ölü bölge içi çubuk hareket üretti.");
            Assert.Less((_s.Rig.transform.position - p0).magnitude, 0.02f, "Ölü bölge içinde oyuncu hareket etti.");
            Assert.AreEqual(0.15f, src.StickDeadzone, 1e-4f);

            // ---- move: a full push is read as a unit move vector and drives the motor along the
            // camera basis (the raw input / planar velocity are asserted rather than the displacement,
            // because the AP005 world may put a wall or prop in front of the spawn point).
            PrototypeCameraRig cam = PrototypeCameraRig.Active;
            Assert.IsNotNull(cam);
            _pad.Set(new Vector2(0f, 1f), Vector2.zero);
            yield return Kareler(20);
            Assert.AreEqual(1f, src.Move.magnitude, 0.02f, "Tam itiş birim hareket vektörü vermedi.");
            Assert.AreEqual(1f, _s.Rig.Motor.RawInput.y, 0.02f, "Motor sol çubuğun ham girdisini almadı.");
            Vector3 vel = _s.Rig.Motor.PlanarVelocity;
            Debug.Log("[DD-TEST] wp024.move raw=" + _s.Rig.Motor.RawInput + " vel=" + vel + " speed=" + vel.magnitude + " max=" + _s.Rig.Motor.MaxSpeed);
            Assert.Greater(vel.magnitude, 0.2f, "Sol çubuk motoru hareket ettirmedi (planar hız 0).");
            Assert.Greater(Vector3.Dot(vel.normalized, cam.PlanarForward), 0.9f, "Hareket kamera ileri yönünde değil.");
            Assert.AreEqual(InputSchemeKind.Gamepad, src.ActiveScheme);

            // ---- aim: right stick = world direction on the camera basis; released stick keeps it
            _pad.Set(Vector2.zero, new Vector2(1f, 0f));
            yield return Kareler(Kare);
            Assert.AreEqual(AimSourceKind.Stick, src.AimKind, "Sağ çubuk nişan kaynağını Stick yapmadı.");
            Assert.AreEqual(AimSourceKind.Stick, _s.Rig.Aim.AimKind);
            Assert.Greater(Vector3.Dot(_s.Rig.Aim.RawStickDirection, cam.PlanarRight), 0.98f, "Ham çubuk yönü kamera sağı değil.");
            Vector3 held = _s.Rig.Aim.AimDirection;
            _pad.Release();
            yield return Kareler(Kare);
            Assert.Less(Vector3.Angle(held, _s.Rig.Aim.AimDirection), 0.5f, "Çubuk bırakılınca yön korunmadı.");
            Assert.AreEqual(AimSourceKind.Stick, src.AimKind, "Boş çubuk nişan kaynağını çalmamalı.");

            // ---- fire: right trigger consumes ammunition through the real weapon path
            ClassicWeaponRuntime klasik = _s.Controller.Classic;
            int ammo = klasik.Ammo;
            int pulses = PrototypeInputInstaller.Instance.Rumble.PulseCount;
            _pad.Set(Vector2.zero, Vector2.zero, fire: true);
            yield return Kareler(12);
            Assert.IsTrue(src.FireHeld, "Sağ tetik FireHeld vermedi.");
            _pad.Release();
            yield return Kareler(2);
            Assert.Less(klasik.Ammo, ammo, "Tetik ateş etmedi (mermi düşmedi).");
            Assert.Greater(PrototypeInputInstaller.Instance.Rumble.PulseCount, pulses, "Ateş titreşim darbesi üretmedi.");
            Assert.LessOrEqual(PrototypeInputInstaller.Instance.Rumble.LastIntensity, InputPreferences.MaxRumbleIntensity + 1e-4f, "Titreşim tavanı 0.3.");

            // ---- special / interact edges reach the extended surface
            _pad.Set(Vector2.zero, Vector2.zero, special: true);
            yield return null;
            Assert.IsTrue(src.SpecialHeld, "Sol tetik SpecialHeld vermedi.");
            _pad.Release();
            yield return Kareler(2);

            // ---- device switch: a keyboard key pulls the scheme (and aim) back to keyboard/mouse
            Assert.IsTrue(_pad.KeyDown("W"));
            yield return Kareler(Kare);
            Debug.Log("[DD-TEST] wp024.switch move=" + src.Move + " device=" + PrototypeInputInstaller.Instance.Source.GetType().Name + " scheme=" + src.ActiveScheme);
            Assert.AreEqual(InputSchemeKind.KeyboardMouse, src.ActiveScheme, "Klavye tuşu şemayı geri almadı.");
            Assert.AreEqual(AimSourceKind.Pointer, src.AimKind, "Klavye tuşu nişanı imlece geri almadı.");
            _pad.KeyDown(null);
            _pad.Set(Vector2.zero, new Vector2(0f, 1f));
            yield return Kareler(Kare);
            Assert.AreEqual(InputSchemeKind.Gamepad, src.ActiveScheme, "Çubuk şemayı gamepad'e almadı.");
            _pad.Release();
            yield return Kareler(2);

            // ---- pause: Start opens the pause hold, Start again releases it
            Assert.IsFalse(RunFlowClock.HasHold(RunFlowClock.PauseHold));
            _pad.Set(Vector2.zero, Vector2.zero, start: true);
            yield return Kareler(3);
            _pad.Release();
            yield return Kareler(3);
            Assert.IsTrue(RunFlowClock.HasHold(RunFlowClock.PauseHold), "Start pause'u açmadı.");
            _pad.Set(Vector2.zero, Vector2.zero, start: true);
            yield return Kareler(3);
            _pad.Release();
            yield return Kareler(3);
            Assert.IsFalse(RunFlowClock.HasHold(RunFlowClock.PauseHold), "Start pause'u kapatmadı.");

            // ---- UI bridge: the scene's EventSystem runs on the Input System UI module
            if (EventSystem.current != null)
            {
                Assert.IsTrue(PrototypeInputInstaller.Instance.UiModuleBridged, "EventSystem Input System UI modülüne köprülenmedi.");
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator NisanYardimi_YalnizYonuBuker_HasarAyni_KapatilabilirVeKoniDisiEtkisiz()
        {
            yield return Kur();
            IExtendedInputSource src = Src;
            PrototypeCameraRig cam = PrototypeCameraRig.Active;

            // No spread so the shot goes exactly where the aim direction says (test parameter on the config clone).
            ClassicWeaponSettings w = _s.Config.Weapons.Find(WeaponRegistry.SelectedWeapon);
            w.SpreadDegrees = 0f;
            w.SpreadBloomPerShot = 0f;
            w.PelletCount = 1;
            _s.Config.NotifyChanged();
            yield return null;

            // Zombie 8 m away, 4° to the right of the camera-forward stick direction.
            Vector3 fwd = cam.PlanarForward;
            Vector3 toZombie = Quaternion.AngleAxis(4f, Vector3.up) * fwd;
            Vector3 zombiPos = _s.Rig.transform.position + toZombie * 8f;
            zombiPos.y = _s.Rig.transform.position.y;
            ZombieEnemy z = _s.Dogur(zombiPos);
            // Keep the geometric 4°/8 m fixture fixed. The target remains alive, registered
            // and damageable; only its movement/animation Update is disabled so the region
            // hitboxes cannot leave the ray between the assist sample and the one test shot.
            z.CompleteRise();
            z.enabled = false;
            yield return Kareler(3);
            Physics.SyncTransforms();

            _pad.Set(Vector2.zero, new Vector2(0f, 1f));
            yield return Kareler(Kare);
            Assert.AreEqual(AimSourceKind.Stick, src.AimKind);
            AimAssist.Result r = _s.Rig.Aim.LastAssist;
            Assert.IsTrue(r.Assisted, "4°/8 m içindeki zombiye yapışmadı.");
            // The selected direction must stay inside the configured cone.
            Assert.That(r.AngleDegrees, Is.InRange(0.5f, InputPreferences.MaxAimAssistAngleDegrees), "Yapışma açısı koni içinde değil.");
            Vector3 aimed = _s.Rig.Aim.AimDirection;
            Vector3 exact = z.transform.position - _s.Rig.transform.position;
            exact.y = 0f;
            Assert.Less(Vector3.Angle(aimed, exact), 0.5f, "Yardımlı yön zombiye bakmıyor.");
            Assert.Greater(Vector3.Angle(_s.Rig.Aim.RawStickDirection, aimed), 0.5f, "Ham yön ile yardımlı yön arasında fark yok.");

            // Damage with assist == damage from an exact manual aim at the same target (direction is the only thing assist touches).
            yield return SpecialWeaponSahne.Bekle(() => _s.Rig.Aim.FacingErrorDegrees < 1f, 5f, "Karakter yardımlı yöne dönmedi.");
            float assisted = 0f, direct = 0f;
            using (var sayac = new SpecialOlaySayaci())
            {
                int ammoOnce = _s.Controller.Classic.Ammo;
                // Hold the trigger until ONE shot leaves (the fire-rate cooldown decides the frame).
                float tFire = Time.realtimeSinceStartup;
                while (_s.Controller.Classic.Ammo == ammoOnce && Time.realtimeSinceStartup - tFire < 3f)
                {
                    _pad.Set(Vector2.zero, new Vector2(0f, 1f), fire: true);
                    yield return null;
                }

                _pad.Set(Vector2.zero, new Vector2(0f, 1f));
                yield return Kareler(3);
                Debug.Log("[DD-TEST] wp024.assisted shots=" + (ammoOnce - _s.Controller.Classic.Ammo) + " damaged=" + sayac.Damaged.Count + " assist=" + _s.Rig.Aim.LastAssist.Assisted);
                Assert.GreaterOrEqual(sayac.Damaged.Count, 1, "Yardımlı atış zombiyi vurmadı.");
                assisted = sayac.Damaged[0].Result.HealthDamage + sayac.Damaged[0].Result.ArmorDamage;
                Assert.AreEqual(z.LifeId, sayac.Damaged[0].LifeId);
            }

            // Assist off → raw direction; then aim the stick EXACTLY at the zombie and shoot: same damage.
            InputPreferences off = InputPreferencesStore.Current.Copy();
            off.AimAssistEnabled = false;
            InputPreferencesStore.Apply(off);
            _pad.Set(Vector2.zero, new Vector2(0f, 1f));
            yield return Kareler(Kare);
            Assert.IsFalse(_s.Rig.Aim.LastAssist.Assisted, "Yardım kapalıyken yapışma sürdü.");
            Assert.Less(Vector3.Angle(_s.Rig.Aim.AimDirection, _s.Rig.Aim.RawStickDirection), 0.01f, "Yardım kapalıyken yön ham değil.");

            // Re-aim the stick at the target's live position every frame (what the assist did for us above).
            System.Func<Vector2> stickToZombie = () =>
            {
                Vector3 e = z.transform.position - _s.Rig.transform.position;
                e.y = 0f;
                e.Normalize();
                return new Vector2(Vector3.Dot(e, cam.PlanarRight), Vector3.Dot(e, cam.PlanarForward));
            };
            for (int i = 0; i < Kare; i++)
            {
                _pad.Set(Vector2.zero, stickToZombie());
                yield return null;
            }

            float t0 = Time.realtimeSinceStartup;
            while (_s.Rig.Aim.FacingErrorDegrees >= 1f && Time.realtimeSinceStartup - t0 < 5f)
            {
                _pad.Set(Vector2.zero, stickToZombie());
                yield return null;
            }

            using (var sayac = new SpecialOlaySayaci())
            {
                int ammoOnce = _s.Controller.Classic.Ammo;
                Vector3 e0 = z.transform.position - _s.Rig.transform.position;
                e0.y = 0f;
                Debug.Log("[DD-TEST] wp024.direct alive=" + z.IsAlive + " hp=" + z.Health + " dist=" + e0.magnitude.ToString("0.00") +
                          " aimErr=" + Vector3.Angle(_s.Rig.Aim.AimDirection, e0).ToString("0.00") + " facingErr=" + _s.Rig.Aim.FacingErrorDegrees.ToString("0.00") +
                          " ammo=" + ammoOnce + " reloading=" + _s.Controller.Classic.IsReloading + " kind=" + src.AimKind);
                float tFire = Time.realtimeSinceStartup;
                while (_s.Controller.Classic.Ammo == ammoOnce && Time.realtimeSinceStartup - tFire < 3f)
                {
                    _pad.Set(Vector2.zero, stickToZombie(), fire: true);
                    yield return null;
                }

                _pad.Set(Vector2.zero, stickToZombie());
                yield return Kareler(3);
                Debug.Log("[DD-TEST] wp024.direct shots=" + (ammoOnce - _s.Controller.Classic.Ammo) + " damaged=" + sayac.Damaged.Count);
                Assert.GreaterOrEqual(sayac.Damaged.Count, 1, "Doğrudan atış zombiyi vurmadı.");
                direct = sayac.Damaged[0].Result.HealthDamage + sayac.Damaged[0].Result.ArmorDamage;
            }

            Assert.AreEqual(direct, assisted, 1e-3f, "Nişan yardımı hasarı değiştirdi (ADR-024-1 ihlali).");
            Assert.Greater(assisted, 0f);

            // Cone limits: 8° off (6 m) → never chosen; 14 m at 3° → chosen; 17 m at 1° → outside the 15 m limit.
            // Zombies walk towards the player, so distances are asserted as ranges over the short window.
            InputPreferencesStore.ResetToDefaults();
            yield return null;
            z.Kill();
            yield return Kareler(2);
            _s.Dogur(_s.Rig.transform.position + (Quaternion.AngleAxis(-8f, Vector3.up) * fwd) * 6f);
            _s.Dogur(_s.Rig.transform.position + (Quaternion.AngleAxis(3f, Vector3.up) * fwd) * 14f);
            _s.Dogur(_s.Rig.transform.position + (Quaternion.AngleAxis(1f, Vector3.up) * fwd) * 17f);
            yield return Kareler(2);
            _pad.Set(Vector2.zero, new Vector2(0f, 1f));
            yield return Kareler(Kare);
            r = _s.Rig.Aim.LastAssist;
            Assert.IsTrue(r.Assisted, "14 m / 3° adayı seçilmedi.");
            Assert.That(r.AngleDegrees, Is.InRange(1.5f, 5.5f), "Koni içi seçim 3° adayı olmalı (8° dışarıda).");
            Assert.That(r.Distance, Is.InRange(11.5f, InputPreferences.MaxAimAssistDistance), "Seçilen aday 14 m'lik olmalı (17 m dışarıda).");
        }
    }
}
