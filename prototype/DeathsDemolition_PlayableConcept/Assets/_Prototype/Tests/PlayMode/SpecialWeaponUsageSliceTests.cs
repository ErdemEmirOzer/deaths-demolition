using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Inputs;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-056 · Special Weapon usage v2 through the live controller: each of the three weapons
    /// publishes Activated → Fired → FieldSpawned → FieldEnded → Deactivated on the usage bus
    /// with the table's ids and the weapon's own reach / seconds (bell wave, loom seal, jewel
    /// well); the gamepad pulse follows the active Special; the hand model / hold come from
    /// WP-037B / WP-032C (no change here).
    /// </summary>
    [TestFixture]
    [Category("WP056")]
    public sealed class SpecialWeaponUsageSliceTests
    {
        private SpecialWeaponSahne _s;
        private readonly List<SpecialUsageEvent> _events = new List<SpecialUsageEvent>(64);

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            _events.Clear();
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
        }

        private IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _events.Clear();
            SpecialWeaponUsageEvents.Usage += OnUsage;
        }

        private void OnUsage(SpecialUsageEvent e) => _events.Add(e);

        private int Count(SpecialUsageKind kind)
        {
            int n = 0;
            for (int i = 0; i < _events.Count; i++) if (_events[i].Kind == kind) n++;
            return n;
        }

        private SpecialUsageEvent First(SpecialUsageKind kind)
        {
            for (int i = 0; i < _events.Count; i++) if (_events[i].Kind == kind) return _events[i];
            Assert.Fail(kind + " olayı yok (" + _events.Count + " olay).");
            return default;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator KaguraBell_Etkin_Ates_Dalga_Bitis_Titresim()
        {
            yield return Kur();
            var rumble = new GamepadRumble();
            try
            {
                rumble.Subscribe();
                SpecialWeaponConfigSection s = _s.Config.Weapons.Special;
                _s.Controller.ActivateSpecialWeapon(SpecialWeaponId.KaguraBell);
                yield return null;
                SpecialUsageEvent act = First(SpecialUsageKind.Activated);
                Assert.AreEqual(SpecialWeaponId.KaguraBell, act.Id);
                Assert.AreEqual("sw_kagura_wave", act.UsageId);
                Assert.AreEqual(s.ActiveDuration, act.Seconds, 0.01f, "Activated: aktif süre");

                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.PickupDonePending, 1.5f, "el modeli");
                // Two bodies inside the wave's reach: the front crosses them → one targeted Hit each (S-03 stumble).
                ZombieEnemy z1 = _s.Dogur(_s.Zemin(-2f, 4f));
                ZombieEnemy z2 = _s.Dogur(_s.Zemin(2f, 5f));
                yield return new WaitForSeconds(2.5f);
                _s.Girdi.AtesBasili = true;
                yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.Fired) >= 1, 3f, "çan çaldı");
                _s.Girdi.AtesBasili = false;
                // The pulse profile resolves on the first heavy-fire cue of the activation (one lookup; no extra notify).
                Assert.AreEqual(s.KaguraBell.Common.RumbleFraction, rumble.SpecialFireFraction, 1e-4f, "çan titreşim profili");
                Assert.AreEqual(s.KaguraBell.Common.RumbleMilliseconds, rumble.SpecialFireMilliseconds);
                SpecialUsageEvent fired = First(SpecialUsageKind.Fired);
                Assert.AreEqual(s.KaguraBell.WaveRadius, fired.Radius, 1e-3f, "Fired: dalga yarıçapı");
                Assert.AreEqual(s.KaguraBell.WaveTravelSeconds, fired.Seconds, 1e-3f, "Fired: dalga süresi");
                Assert.Less(Vector3.Distance(fired.Origin, _s.Rig.transform.position), 0.5f, "dalga KARAKTERDEN çıkar (namludan değil)");
                SpecialUsageEvent field = First(SpecialUsageKind.FieldSpawned);
                Assert.AreEqual("sw_kagura_field", field.UsageId);
                Assert.AreEqual(fired.Radius, field.Radius, 1e-3f);
                yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.FieldEnded) >= 1, s.KaguraBell.WaveTravelSeconds + 1f, "dalga kenara vardı");
                Assert.GreaterOrEqual(First(SpecialUsageKind.FieldEnded).Count, 2, "iki gövde geçildi");
                Assert.GreaterOrEqual(Count(SpecialUsageKind.Hit), 2, "gövde başına hedefli Hit");
                bool z1Hit = false, z2Hit = false;
                for (int i = 0; i < _events.Count; i++)
                {
                    if (_events[i].Kind != SpecialUsageKind.Hit) continue;
                    Assert.IsTrue(_events[i].Target.IsValid, "Hit hedefi geçerli");
                    Assert.AreEqual("sw_kagura_field", _events[i].UsageId);
                    Assert.AreEqual(s.KaguraBell.Common.ImpactForce, _events[i].Force, 1e-3f, "kuvvet = dalga darbe kuvveti");
                    Assert.That(_events[i].Falloff01, Is.InRange(s.KaguraBell.FalloffAtRim - 1e-3f, 1f), "kenar zayıflaması bandı");
                    Assert.Greater(_events[i].TransactionId, 0, "işlem kimliği");
                    Transform r = _events[i].Target.Root;
                    if (r == z1.transform || r.IsChildOf(z1.transform)) z1Hit = true;
                    if (r == z2.transform || r.IsChildOf(z2.transform)) z2Hit = true;
                }

                Assert.IsTrue(z1Hit && z2Hit, "her iki gövde de hedefli Hit aldı");

                _s.Controller.DeactivateSpecialWeapon();
                yield return null;
                Assert.AreEqual(1, Count(SpecialUsageKind.Deactivated));
                Assert.AreEqual(SpecialWeaponId.KaguraBell, First(SpecialUsageKind.Deactivated).Id);
                Assert.AreEqual(GamepadRumble.HeavyFireFraction, rumble.SpecialFireFraction, 1e-4f, "bitişte profil WP-024 varsayılanına döner");
            }
            finally
            {
                rumble.Unsubscribe();
                rumble.Dispose();
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator OfudaLoom_Dart_Muhur_Alan_Bitis()
        {
            yield return Kur();
            SpecialWeaponConfigSection s = _s.Config.Weapons.Special;
            s.OfudaLoom.SealSeconds = 1.2f;   // config COPY (kit): a short seal so the web's expiry is observable
            s.OfudaLoom.SealArmSeconds = 0.1f;
            Vector3 merkez = _s.Zemin(0f, 5f);
            ZombieEnemy z = _s.Dogur(merkez);
            yield return new WaitForSeconds(2.5f);   // DEC-081 rise + blend

            _s.Controller.ActivateSpecialWeapon(SpecialWeaponId.OfudaLoom);
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.PickupDonePending, 1.5f, "el modeli");
            Assert.AreEqual("sw_ofuda_dart", First(SpecialUsageKind.Activated).UsageId);

            // Fire the runtime straight at the body (the dart is a hitscan; the pointer would need a screen position).
            Collider hitbox = z.HitboxFor(BodyRegion.UpperBody);
            Assert.IsNotNull(hitbox);
            Vector3 hedef = hitbox.bounds.center;
            Vector3 origin = _s.Rig.transform.position + Vector3.up * (hedef.y - _s.Rig.transform.position.y);
            _s.Controller.Special.Fire(origin, (hedef - origin).normalized);
            yield return null;
            SpecialUsageEvent hit = First(SpecialUsageKind.Hit);
            Assert.AreEqual("sw_ofuda_web", hit.UsageId);
            Assert.IsTrue(hit.Target.Root == z.transform || hit.Target.Root.IsChildOf(z.transform), "mühür Hit'i o gövdeyi hedefler");
            Assert.AreEqual(0f, hit.Force, "mühür kuvvetsiz");
            Assert.AreEqual(1.2f, hit.Seconds, 1e-3f, "mühür süresi");
            SpecialUsageEvent seal = First(SpecialUsageKind.FieldSpawned);
            Assert.AreEqual("sw_ofuda_web", seal.UsageId);
            Assert.AreEqual(1, seal.Count, "ilk mühür → web'de 1 mühür");
            Assert.AreEqual(1.2f, seal.Seconds, 1e-3f, "mühür süresi");
            Assert.Less(Vector3.Distance(seal.Origin, hedef), 1.5f, "mühür gövde merkezinde");
            Assert.AreEqual(1, _s.Controller.Special.Web.SealCount);

            yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.FieldEnded) >= 1, 3f, "mühür süresi doldu → web bitti");
            Assert.AreEqual(0, First(SpecialUsageKind.FieldEnded).Count, "çökme olmadan bitiş: harcanan 0");
            Assert.AreEqual(0, _s.Controller.Special.Web.SealCount);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator TideJewel_Firlat_Kuyu_Patlama()
        {
            yield return Kur();
            SpecialWeaponConfigSection s = _s.Config.Weapons.Special;
            _s.Controller.ActivateSpecialWeapon(SpecialWeaponId.TideJewel);
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.PickupDonePending, 1.5f, "el modeli");
            Assert.AreEqual("sw_tide_pull", First(SpecialUsageKind.Activated).UsageId);

            _s.Girdi.AtesBasti = true;
            _s.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.Fired) >= 1, 3f, "mücevher fırlatıldı");
            _s.Girdi.AtesBasti = false;
            _s.Girdi.AtesBasili = false;
            SpecialUsageEvent fired = First(SpecialUsageKind.Fired);
            Assert.AreEqual(s.TideJewel.ThrowRange, fired.Radius, 1e-3f, "Fired: atış menzili");
            yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.FieldSpawned) >= 1, 2f, "kuyu açıldı");
            SpecialUsageEvent well = First(SpecialUsageKind.FieldSpawned);
            Assert.AreEqual("sw_tide_well", well.UsageId);
            Assert.AreEqual(s.TideJewel.WellRadius, well.Radius, 1e-3f, "kuyu yarıçapı");
            Assert.AreEqual(s.TideJewel.WellSeconds, well.Seconds, 1e-3f, "kuyu süresi");
            yield return SpecialWeaponSahne.Bekle(() => Count(SpecialUsageKind.FieldEnded) >= 1, s.TideJewel.WellSeconds + 2f, "kuyu patladı");
            SpecialUsageEvent ended = First(SpecialUsageKind.FieldEnded);
            Assert.AreEqual("sw_tide_well", ended.UsageId);
            Assert.Less(Vector3.Distance(ended.Origin, well.Origin), 0.05f, "patlama kuyunun yerinde");
            Assert.GreaterOrEqual(ended.Count, 0, "çekme tik sayısı");
            Debug.Log("[DD-TEST] wp056.usage events=" + _events.Count + " tide pulls=" + ended.Count);
        }
    }
}
