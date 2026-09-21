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
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "Combat presentation must never write the global clock.");
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
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);
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
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);
        }
    }
}
