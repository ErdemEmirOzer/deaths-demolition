using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-082 reload magazine contract v1 (S-02 with S-06, C-00 05.10) on the current rig: a real reload of a magazine weapon
    /// walks weapon → hand (spent) → hand (fresh) → weapon — the clip's AP-009C beats (mag_out / mag_in / chamber) plus the
    /// timeline for the beat no clip carries yet (mag_hand_attach) — and ends with the magazine in the weapon; a reload cut short
    /// (a weapon switch) puts it back at once; rl_1 starts on an empty tube. The ammo's timing is the runtime's (presentation only).
    /// Game frames (1/60).
    /// </summary>
    [TestFixture]
    [Category("WP082")]
    public sealed class WeaponReloadMagazineTests
    {
        private SpecialWeaponSahne _s;
        private float _dt0;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = _dt0;
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        private IEnumerator Kur()
        {
            _dt0 = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        private IEnumerator UcAtisVeReload()
        {
            for (int i = 0; i < 3; i++)
            {
                _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                yield return SpecialWeaponSahne.Kareler(8);
                _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                yield return SpecialWeaponSahne.Kareler(8);
            }

            _s.Girdi.DoldurBasti = true;
            yield return null;
            _s.Girdi.DoldurBasti = false;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Reload_SarjorSilahElElSilah_TamDortVurus()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            int event0 = m.FromEvent, timer0 = m.FromTimer, early0 = m.EndedEarly;
            int ammoBefore = _s.Controller.Classic.Ammo;
            yield return UcAtisVeReload();
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 1f, "reload başlamadı");
            Assert.IsTrue(m.Active, "şarjörlü reload sahiplik makinesini başlatmalı");
            var owners = new List<MagazineOwner> { m.Owner };
            float t0 = Time.time;
            while (_s.Controller.Classic.IsReloading && Time.time - t0 < 6f)
            {
                yield return null;
                if (owners[owners.Count - 1] != m.Owner) owners.Add(m.Owner);
            }

            yield return null;
            string seq = string.Join(" → ", owners);
            Debug.Log("[DD-TEST] wp082.reload ar_1 " + seq + " | from clip " + (m.FromEvent - event0) + ", from timer " + (m.FromTimer - timer0) +
                      " | ammo " + ammoBefore + " → " + _s.Controller.Classic.Ammo + " | clip drives " + _s.Controller.ClipDrivesMechanism);
            Assert.IsFalse(_s.Controller.Classic.IsReloading, "reload bitmedi");
            Assert.IsTrue(_s.Controller.Classic.LastReloadCompleted, "reload tamamlanmadı");
            CollectionAssert.AreEqual(new[] { MagazineOwner.Weapon, MagazineOwner.HandOld, MagazineOwner.HandNew, MagazineOwner.Weapon }, owners, seq);
            Assert.IsFalse(m.Active);
            Assert.AreEqual(MagazineOwner.Weapon, m.Owner, "şarjör silahta bitmeli");
            Assert.AreEqual(4, (m.FromEvent - event0) + (m.FromTimer - timer0), "dört vuruş tam bir kez");
            Assert.AreEqual(early0, m.EndedEarly);
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator ReloadKesilir_SilahDegisimi_SarjorAninaSilahta()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            int early0 = m.EndedEarly;
            yield return UcAtisVeReload();
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 1f, "reload başlamadı");
            yield return SpecialWeaponSahne.Bekle(() => m.Owner == MagazineOwner.HandOld || m.Owner == MagazineOwner.HandNew, 3f, "şarjör ele geçmedi");
            _s.Controller.EquipClassic(ClassicWeaponId.AutoSmg, _s.Config);   // a switch mid-reload
            yield return null;
            yield return null;
            Assert.IsFalse(m.Active, "kesilen reload sahipliği kapatmalı");
            Assert.AreEqual(MagazineOwner.Weapon, m.Owner, "şarjör elde kaldı");
            Assert.AreEqual(early0 + 1, m.EndedEarly);
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Roketatar_BosTupleBaslar()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.Rl1, _s.Config);
            yield return SpecialWeaponSahne.Kareler(30);
            WeaponMagazineOwnership m = _s.Controller.Magazine;
            _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
            yield return SpecialWeaponSahne.Kareler(4);
            _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.Classic.IsReloading, 3f, "rl_1 boş tüpte reload başlamadı");
            Assert.IsTrue(m.Active);
            Assert.AreNotEqual(MagazineOwner.HandOld, m.Owner, "boş tüpte çıkarılan eski şarjör yok");
            Assert.GreaterOrEqual(m.NextStep, (int)MagazineStep.HandAttach, "rl_1 detach'sız başlar");
            yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.Classic.IsReloading, 8f, "rl_1 reload bitmedi");
            yield return null;
            Assert.AreEqual(MagazineOwner.Weapon, m.Owner);
            Assert.IsFalse(m.Active);
        }
    }
}
