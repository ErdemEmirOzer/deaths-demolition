using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    [TestFixture]
    [Category("WP062")]
    public sealed class FeelEventRouterTests
    {
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
            FeelRuntime.ResetForTests(); FeelRuntime.Initialize(null);
            FeelEventRouter.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            FeelEventRouter.ResetForTests(); FeelRuntime.ResetForTests();
            Time.timeScale = _timeScale;
        }

        [Test]
        public void SameGroup_ChoosesOneHighestPriorityPresentation()
        {
            int group = FeelEventRouter.NextGroupId();
            FeelEventRouter.Enqueue(ImpactFeelId.ArmorBreak, group, Vector3.zero, Vector3.forward);
            FeelEventRouter.Enqueue(ImpactFeelId.HeavyDeath, group, Vector3.zero, Vector3.forward);
            FeelEventRouter.Flush();
            Assert.AreEqual(1, FeelEventRouter.CameraRequests);
            Assert.AreEqual(1, FeelEventRouter.AcceptedLocalStops);
            Assert.AreEqual(.04f, HitStopService.RemainingRealSeconds, 1e-6f);
            // WP-095 K1 (C-00 08.10): the top-down GLOBAL stop may slow the clock now, but it is FeelTimeScale's, requested from
            // FeelEvents — the router's stop stays the local VFX envelope. Expectation unchanged (verified), message narrowed.
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "The router's local stop must never write the global clock (K1: only FeelTimeScale does).");
            Assert.IsFalse(FeelTimeScale.GlobalStopActive, "K1: an impact-router flush asks the arbiter for nothing.");
        }

        [Test]
        public void OrdinaryAndOfudaImpacts_HaveNoLocalStop()
        {
            FeelEventRouter.Enqueue(ImpactFeelId.FleshHit, FeelEventRouter.NextGroupId(), Vector3.zero, Vector3.forward);
            FeelEventRouter.Enqueue(ImpactFeelId.Death, FeelEventRouter.NextGroupId(), Vector3.zero, Vector3.forward);
            FeelEventRouter.Enqueue(ImpactFeelId.Explosion, FeelEventRouter.NextGroupId(), Vector3.zero, Vector3.forward);
            FeelEventRouter.Enqueue(ImpactFeelId.OfudaCollapse, FeelEventRouter.NextGroupId(), Vector3.zero, Vector3.forward);
            FeelEventRouter.Flush();
            Assert.AreEqual(0, FeelEventRouter.AcceptedLocalStops);
            Assert.IsFalse(HitStopService.IsActive);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);   // WP-095 K1: unchanged (verified) — the router never writes the clock
        }

        [Test]
        public void DifferentGroups_ArmorThenHeavy_ChooseOneGlobalStrongestStop()
        {
            int armor = FeelEventRouter.NextGroupId();
            int heavy = FeelEventRouter.NextGroupId();
            FeelEventRouter.Enqueue(ImpactFeelId.ArmorBreak, armor, Vector3.zero, Vector3.forward);
            FeelEventRouter.Enqueue(ImpactFeelId.HeavyDeath, heavy, Vector3.zero, Vector3.forward);
            FeelEventRouter.Flush();

            Assert.AreEqual(1, FeelEventRouter.AcceptedLocalStops);
            Assert.AreEqual(1, HitStopService.Count);
            Assert.AreEqual(heavy, HitStopService.ActiveGroupId);
            Assert.AreEqual(.04f, HitStopService.RemainingRealSeconds, 1e-6f);
        }

        [Test]
        public void DifferentGroups_HeavyThenArmor_ChooseSameGlobalStrongestStop()
        {
            int heavy = FeelEventRouter.NextGroupId();
            int armor = FeelEventRouter.NextGroupId();
            FeelEventRouter.Enqueue(ImpactFeelId.HeavyDeath, heavy, Vector3.zero, Vector3.forward);
            FeelEventRouter.Enqueue(ImpactFeelId.ArmorBreak, armor, Vector3.zero, Vector3.forward);
            FeelEventRouter.Flush();

            Assert.AreEqual(1, FeelEventRouter.AcceptedLocalStops);
            Assert.AreEqual(1, HitStopService.Count);
            Assert.AreEqual(heavy, HitStopService.ActiveGroupId);
            Assert.AreEqual(.04f, HitStopService.RemainingRealSeconds, 1e-6f);
        }

        /// <summary>
        /// WP-069 review R1-A #8: pellets of one trigger pull land over several frames; the group shakes once,
        /// a later frame only dispatches again when stronger (a kill), never the same strength twice.
        /// </summary>
        [Test]
        public void AyniTetikGrubu_SonrakiKaredeTekrarSarsmaz_GucluOlayYukseltir()
        {
            int group = FeelEventRouter.NextGroupId();
            FeelEventRouter.Enqueue(ImpactFeelId.ArmorBreak, group, Vector3.zero, Vector3.forward);
            FeelEventRouter.Flush();
            int shakes = FeelEventRouter.CameraRequests;
            Assert.AreEqual(1, shakes, "ilk kare sarsar");

            FeelEventRouter.Enqueue(ImpactFeelId.ArmorBreak, group, Vector3.zero, Vector3.forward);
            FeelEventRouter.Flush();
            Assert.AreEqual(shakes, FeelEventRouter.CameraRequests, "aynı grup aynı güçte ikinci karede sarsmaz");
            Assert.AreEqual(1, FeelEventRouter.SuppressedRepeats);

            FeelEventRouter.Enqueue(ImpactFeelId.HeavyDeath, group, Vector3.zero, Vector3.forward);
            FeelEventRouter.Flush();
            Assert.AreEqual(shakes + 1, FeelEventRouter.CameraRequests, "daha güçlü olay (öldüren pellet) yükseltir");

            FeelEventRouter.Enqueue(ImpactFeelId.ArmorBreak, FeelEventRouter.NextGroupId(), Vector3.zero, Vector3.forward);
            FeelEventRouter.Flush();
            Assert.AreEqual(shakes + 2, FeelEventRouter.CameraRequests, "yeni tetik yeni grup");
        }

        [Test]
        public void LateFrame_AgesExistingTokenBeforeStartingNewToken()
        {
            int group = FeelEventRouter.NextGroupId();
            FeelEventRouter.Enqueue(ImpactFeelId.ArmorBreak, group, Vector3.zero, Vector3.forward);
            FeelEventRouter.AdvanceLateFrame(1f / 30f);
            Assert.AreEqual(.035f, HitStopService.RemainingRealSeconds, 1e-6f,
                "A token resolved in this LateUpdate must keep its full first-frame envelope.");

            FeelEventRouter.AdvanceLateFrame(1f / 30f);
            Assert.AreEqual(.035f - 1f / 30f, HitStopService.RemainingRealSeconds, 1e-6f);
        }

        [Test]
        public void LocalToken_UsesExactOverlap_WithoutTwoFrameExtensionAt30Fps()
        {
            Assert.IsTrue(HitStopService.RequestForGroup(7, .035f, .25f, .06f));
            float first = HitStopService.VisualDelta(7, 1f / 30f, 1f / 30f);
            HitStopService.Tick(1f / 30f);
            float second = HitStopService.VisualDelta(7, 1f / 30f, 1f / 30f);
            HitStopService.Tick(1f / 30f);
            Assert.AreEqual((1f / 30f) * .06f, first, 1e-6f);
            Assert.AreEqual((.035f - 1f / 30f) * .06f + (2f / 30f - .035f), second, 1e-6f);
            Assert.IsFalse(HitStopService.IsActive);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);   // WP-095 K1: unchanged (verified) — HitStopService still never writes the clock
        }
    }
}
