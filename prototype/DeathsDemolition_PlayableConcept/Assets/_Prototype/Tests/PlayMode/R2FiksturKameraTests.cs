using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// R3 (C-00 07.10; S-01 FP/TP inventory §4.3): the R2 measurement fixtures hand the next test the process default camera,
    /// TopDown (user 06.10: tests run top-down only), whatever their test body asked for. Their TearDowns used to leave ThirdPerson
    /// (R2HeroOlcumTests, R2HeroGovdeTests, R2LokomotionOlcumTests) or whatever the body set (R2LokomotionIkincilTests), and
    /// SpecialWeaponSahne.Kur does not reset the mode, so every PlayMode test after one of them ran in third person — in main-ci
    /// after KavramaSatiri, the one R2 test it runs. Each fixture's TearDown runs here on its own after a test body's ThirdPerson; it
    /// needs no scene (its scene handle is null, Bitir is skipped).
    /// </summary>
    public sealed class R2FiksturKameraTests
    {
        private bool _fpArms;

        [SetUp]
        public void SetUp()
        {
            _fpArms = PlayerSkinnedVisual.FirstPersonV3Arms;
        }

        [TearDown]
        public void TearDown()
        {
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            PlayerSkinnedVisual.FirstPersonV3Arms = _fpArms;
            FootIkAdapter.ResetStatics();
            WeaponHoldIk.ResetStatics();
            WeaponGripIk.ResetStatics();
        }

        [UnityTest]
        public IEnumerator R2Fiksturleri_TearDown_KamerayiTopDownaBirakir()
        {
            var fixtures = new (string Name, Func<IEnumerator> TearDown)[]
            {
                ("R2HeroOlcumTests", () => new R2HeroOlcumTests().TearDown()),
                ("R2HeroGovdeTests", () => new R2HeroGovdeTests().TearDown()),
                ("R2LokomotionOlcumTests", () => new R2LokomotionOlcumTests().TearDown()),
                ("R2LokomotionIkincilTests", () => new R2LokomotionIkincilTests().TearDown()),
            };
            var leaked = new List<string>();
            foreach ((string name, Func<IEnumerator> tearDown) in fixtures)
            {
                CameraModeService.Request(CameraMode.ThirdPerson, instant: true);   // what the R2 test bodies ask for
                Assert.AreEqual(CameraMode.ThirdPerson, CameraModeService.Current, name + ": ThirdPerson isteği uygulanmadı (mod kilidi?)");
                yield return tearDown();
                if (CameraModeService.Current != CameraMode.TopDown) leaked.Add(name + " → " + CameraModeService.Current);
            }

            Assert.IsEmpty(leaked, "R2 fikstürü TearDown'da kamerayı TopDown'a bırakmıyor; sonraki PlayMode testi bu modda koşar");
        }
    }
}
