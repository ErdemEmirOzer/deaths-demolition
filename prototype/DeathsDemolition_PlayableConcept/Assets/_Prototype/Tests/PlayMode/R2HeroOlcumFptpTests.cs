using System.Collections;
using DD.Prototype.CameraSystem;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// R3 (C-00 07.10: split the mixed tests, delete no FP / TP line): the third-person halves of R2HeroOlcumTests' camera-specific
    /// tests, the same lines run on <see cref="CameraMode.ThirdPerson"/>. Out of scope by the user's 06.10 decision (top-down only);
    /// WP083 and FPTP sit on the test methods: UTF 1.1.33 hides method categories when a fixture has its own category.
    /// The fixture drives an R2HeroOlcumTests instance: its SetUp / TearDown (TopDown after) and its bodies.
    /// </summary>
    [TestFixture]
    public sealed class R2HeroOlcumFptpTests
    {
        private readonly R2HeroOlcumTests _h = new R2HeroOlcumTests();

        [UnitySetUp]
        public IEnumerator SetUp() => _h.SetUp();

        [UnityTearDown]
        public IEnumerator TearDown() => _h.TearDown();

        /// <summary>KavramaSatiri_LOD0Kesitiyle_TumSilahlar from the third-person game camera (the R2 version).</summary>
        [Category("WP083")]
        [Category("FPTP")]
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator KavramaSatiri_LOD0Kesitiyle_TumSilahlar_TP() => _h.KavramaSatiriOlc(CameraMode.ThirdPerson);

        /// <summary>Kare_M1's third-person frames (the R2 set's TP side); PNGs to DD_B2D_COMPARE_DIR.</summary>
        [Category("WP083")]
        [Category("FPTP")]
        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(1800000)]
        public IEnumerator Kare_M1_SilahGovde_TP_NisanTepmeReload() => _h.KareM1(CameraMode.ThirdPerson);
    }
}
