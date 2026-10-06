using System.Collections;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-087 (C-00 06.10; councils K/wp-087/konsey/KARAR-ArmsCarryKick-06.10.md + K/wp-083/KARAR-HELDKICK-API-06.10.KONSOLIDE.md,
    /// order A → B → C → D) · the held weapon's kick carried by the weapon-first hold's arms. Step A (this file's first test): the
    /// rig side is neutral — a rig nobody carries (WeaponVisualRig.CarriedBy null: no controller wiring yet) keeps the old path with
    /// the hold on and off: the body inside the rig kicks back on the shot, the arms' share stays 0, and the arms' own channel
    /// runs (it reads on the shot's frame) without moving anything. Step C adds the carried measurements (K5–K7).
    /// </summary>
    [TestFixture]
    [Category("WP087")]
    public sealed class Wp087TutusTepmeTests
    {
        private SpecialWeaponSahne _s;
        private ClassicWeaponId _oncekiSecim;

        [SetUp]
        public void Hazirla() => _oncekiSecim = WeaponRegistry.SelectedWeapon;   // EquipClassic writes it process-wide (main-ci 285b7fe8)

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = 0f;
            if (_s != null) { yield return _s.Bitir(); _s = null; }
            DD.Prototype.CameraSystem.CameraModeService.ResetForTests();
            WeaponHoldIk.ResetStatics();
            WeaponRegistry.SelectedWeapon = _oncekiSecim;
        }

        private static string Cm(float m) => (m * 100f).ToString("0.00", CultureInfo.InvariantCulture);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator A_TasinmayanRig_TutusAcikVeKapali_GovdeEskiYolda_KolPayiSifir()
        {
            foreach (bool holdOn in new[] { true, false })
            {
                WeaponHoldIk.Enabled = holdOn;
                _s = new SpecialWeaponSahne();
                yield return _s.Kur();
                DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
                Time.captureDeltaTime = 1f / 60f;
                Transform mount = _s.Rig.GetComponentInChildren<PlayerWeaponBinding>().WeaponMount;
                _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
                yield return SpecialWeaponSahne.Kareler(60);
                WeaponVisualRig rig = mount.Find("ClassicWeaponRig").GetComponent<WeaponVisualRig>();
                Assert.IsNotNull(rig.GetComponent<WeaponRigCarriedPose>(), "895 pay bileşeni rig'de");
                Assert.IsNull(rig.CarriedBy, "A: rig'i taşıyan yok (bağlantı C adımında)");
                int serial0 = _s.Controller.ShotSerial;
                float bodyBack = 0f, armsBack = 0f, share = 0f;
                _s.Girdi.AtesBasti = true; _s.Girdi.AtesBasili = true;
                yield return null;
                _s.Girdi.AtesBasti = false; _s.Girdi.AtesBasili = false;
                for (int f = 0; f < 8; f++)
                {
                    yield return null;
                    bodyBack = Mathf.Max(bodyBack, -rig.Body.localPosition.z);
                    armsBack = Mathf.Max(armsBack, rig.ArmsKickBack);
                    share = Mathf.Max(share, rig.ArmsCarryKick);
                }

                int shots = _s.Controller.ShotSerial - serial0;
                Debug.Log("[DD-TEST] wp087.a.neutral hold " + (holdOn ? "on" : "off") + " shots " + shots + " body back " + Cm(bodyBack) +
                          " cm, arms channel " + Cm(armsBack) + " cm (not applied), share " + share);
                Assert.AreEqual(1, shots, "kaynak kayıtta: tek atış");
                Assert.AreEqual(0f, share, "taşınmayan rig'de kol payı 0");
                Assert.Greater(bodyBack, 0.01f, "gövde eskisi gibi geri teper (hold " + holdOn + ")");
                Assert.Greater(armsBack, 0f, "kol kanalı atışı aldı (uygulanmıyor)");
                yield return _s.Bitir(); _s = null;
                DD.Prototype.CameraSystem.CameraModeService.ResetForTests();
            }
        }
    }
}
