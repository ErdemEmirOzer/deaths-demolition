using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>WP-093 Faz2: HIS-SOZLESMESI §4.2–4.5, AP-037 v001.1's measured mask diameters.</summary>
    [TestFixture, Category("WP093Faz2"), Category("TD")]
    public sealed class Wp093FeelBrushPolicyTests
    {
        [TestCase(1, 1.5f)]
        [TestCase(2, 2.5f)]
        [TestCase(3, 3.5f)]
        public void Multi_MaskVisibleRadiusMatchesMetres(int tier, float radius)
        {
            float visibleRadius = FeelBrushVfxPolicy.MultiQuadMetres(tier) * .840f * .5f;
            Assert.That(visibleRadius, Is.EqualTo(radius).Within(.0001f), "§4.4: mask radius, not quad half-width");
        }

        [TestCase(.5f)]
        [TestCase(2f)]
        [TestCase(8f)]
        public void Shock_VisibleOuterEdgeReachesOnePointTwoBlastRadii(float radius)
        {
            float visibleRadius = FeelBrushVfxPolicy.ShockQuadMetres(radius) * .829f * .5f;
            Assert.That(visibleRadius, Is.EqualTo(1.2f * radius).Within(.0001f));
            Assert.That(FeelBrushVfxPolicy.EaseOutCubic(1f), Is.EqualTo(1f));
            Assert.That(FeelBrushVfxPolicy.ShockGrowSeconds, Is.EqualTo(.18f));
            Assert.That(FeelBrushVfxPolicy.ShockSeconds, Is.EqualTo(.25f));
        }

        [Test]
        public void Atlas_KillUsesFramesOneToFifteen_AndStainUsesLowerRow()
        {
            Assert.That(FeelBrushVfxPolicy.KillFrame(0f), Is.EqualTo(1), "cell zero is the empty setup frame");
            Assert.That(FeelBrushVfxPolicy.KillFrame(.5f), Is.EqualTo(8));
            Assert.That(FeelBrushVfxPolicy.KillFrame(1f), Is.EqualTo(15));
            Assert.That(FeelBrushVfxPolicy.AtlasUv(1, 4, 4), Is.EqualTo(new Vector4(.25f, .25f, .25f, .75f)));
            Assert.That(FeelBrushVfxPolicy.AtlasUv(15, 4, 4), Is.EqualTo(new Vector4(.25f, .25f, .75f, 0f)));
            Assert.That(FeelBrushVfxPolicy.AtlasUv(4, 4, 2), Is.EqualTo(new Vector4(.25f, .5f, 0f, 0f)));
            Assert.That(FeelBrushVfxPolicy.AtlasUv(7, 4, 2), Is.EqualTo(new Vector4(.25f, .5f, .75f, 0f)));
        }

        [Test]
        public void GroundRotation_QuadFacesSlopedGround_AndHorizontalAxisTracksShot()
        {
            Vector3 normal = new Vector3(.2f, 1f, -.1f).normalized;
            Vector3 direction = new Vector3(1f, 6f, 2f);
            Quaternion rotation = FeelBrushVfxPolicy.GroundRotation(direction, normal);
            Assert.That(Vector3.Dot(rotation * Vector3.forward, normal), Is.GreaterThan(.9999f));
            Vector3 shotOnGround = Vector3.ProjectOnPlane(FeelBrushVfxPolicy.FlatDirection(direction), normal).normalized;
            Assert.That(Vector3.Dot(rotation * Vector3.right, shotOnGround), Is.GreaterThan(.9999f));
            Assert.That(FeelBrushVfxPolicy.FlatDirection(Vector3.up), Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void KillWindow_GroupAndChainHaveDistinctCentroids_NotTheLastDeath()
        {
            var window = new FeelBrushKillWindow();
            window.Record(new Vector3(0f, 0f, 0f), 7, 10f);
            window.Record(new Vector3(4f, 0f, 0f), 8, 10.1f);
            window.Record(new Vector3(8f, 0f, 0f), 7, 10.2f);
            window.Record(new Vector3(12f, 0f, 0f), 7, 10.3f);
            Vector3 fallback = new Vector3(99f, 0f, 99f);
            Assert.That(window.Center(10.3f, true, 7, fallback).x, Is.EqualTo(20f / 3f).Within(.0001f));
            Assert.That(window.Center(10.3f, false, 0, fallback).x, Is.EqualTo(6f).Within(.0001f));
            Assert.That(window.Center(10.3f, true, 999, fallback), Is.EqualTo(fallback));
        }

        [Test]
        public void KillWindow_ExpiresSlidingWindow_AndClearRemovesPreviousRound()
        {
            var window = new FeelBrushKillWindow();
            window.Record(Vector3.zero, 1, 10f);
            window.Record(Vector3.right * 8f, 1, 10.4f);
            Assert.That(window.Center(10.61f, false, 0, Vector3.back).x, Is.EqualTo(8f));
            Assert.That(window.Center(11.01f, false, 0, Vector3.back), Is.EqualTo(Vector3.back));
            window.Clear();
            Assert.That(window.Count, Is.Zero);
            Assert.That(window.Center(10.4f, false, 0, Vector3.back), Is.EqualTo(Vector3.back));
        }

        [Test]
        public void KillWindow_ContinuousGroupKeepsItsEarlyDeaths_ThenIdleGapStartsANewGroup()
        {
            var window = new FeelBrushKillWindow();
            window.Record(Vector3.zero, 7, 10f);
            window.Record(Vector3.right * 4f, 7, 10.4f);
            window.Record(Vector3.right * 8f, 7, 10.8f);
            Assert.That(window.Center(10.8f, true, 7, Vector3.back).x, Is.EqualTo(4f).Within(.0001f),
                "FeelEvents group expires by idle time, not age of first death");
            Assert.That(window.Center(10.8f, false, 0, Vector3.back).x, Is.EqualTo(6f).Within(.0001f),
                "the chain still uses the sliding .6-second window");
            window.Record(Vector3.right * 20f, 7, 11.5f);
            Assert.That(window.Center(11.5f, true, 7, Vector3.back).x, Is.EqualTo(20f).Within(.0001f));
        }

        [Test]
        public void KillWindow_OverflowRetainsLatestThirtyTwoKills()
        {
            var window = new FeelBrushKillWindow();
            for (int i = 0; i < 100; i++) window.Record(Vector3.right * i, 1, 10f);
            Assert.That(window.Count, Is.EqualTo(32));
            Assert.That(window.Center(10f, true, 1, Vector3.zero).x, Is.EqualTo(83.5f).Within(.0001f));
        }
    }
}
