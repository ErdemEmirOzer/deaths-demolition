using DD.Prototype.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B9 (M-23, C-00 30.09; AI-SPAWN-04 on 4934e80f/12c7215c): bounded corner hold of
    /// <see cref="NavPathFollower"/>. No NavMesh: a stub <see cref="INavSegmentQuery"/> answers
    /// budget / projection / segment queries and the clock is driven through
    /// <see cref="NavPathFollower.AdvanceCornersForTests"/>.
    ///
    /// Path used by every test (XZ, metres): A (0,0) → C (0,5) → N (5,5), a 90° bend at C.
    /// Arrive radius 0.65 m (field default), hold cap <see cref="NavPathFollower.CornerHoldMaxSeconds"/>.
    /// </summary>
    [TestFixture]
    [Category("WP076")]
    public sealed class NavPathFollowerCornerTests
    {
        private static readonly Vector3 A = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 C = new Vector3(0f, 0f, 5f);
        private static readonly Vector3 N = new Vector3(5f, 0f, 5f);

        private sealed class StubQuery : INavSegmentQuery
        {
            public bool Budget = true;
            public bool Projects = true;
            public bool Blocked = true;
            public int BudgetCalls;
            public int SegmentCalls;

            public bool TryTakeQueryBudget()
            {
                BudgetCalls++;
                return Budget;
            }

            public bool TryProject(Vector3 point, out Vector3 result)
            {
                result = point;
                return Projects;
            }

            public bool SegmentBlocked(Vector3 from, Vector3 to)
            {
                SegmentCalls++;
                return Blocked;
            }
        }

        private StubQuery _query;

        [SetUp]
        public void SetUp()
        {
            NavPathFollower.ResetCornerHoldCounters();
            _query = new StubQuery();
        }

        [TearDown]
        public void TearDown()
        {
            NavPathFollower.ResetCornerHoldCounters();
        }

        private NavPathFollower Bend(params Vector3[] corners)
        {
            var follower = new NavPathFollower { SegmentQuery = _query };
            Vector3[] path = corners.Length > 0 ? corners : new[] { A, C, N };
            follower.SetCornersForTests(path, path.Length);
            Assert.IsTrue(follower.HasCompletePath, "fixture: seeded path");
            Assert.AreEqual(path.Length - 1, follower.RemainingCorners, "fixture: steering starts at corner 1");
            return follower;
        }

        [Test]
        public void Kose_DuzDonemec_GecilinceTuketilir()
        {
            NavPathFollower f = Bend();

            f.AdvanceCornersForTests(new Vector3(0f, 0f, 4.5f), 0f);   // 0.5 m before C, skip blocked
            Assert.AreEqual(2, f.RemainingCorners, "blocked, not passed: C is held");
            Assert.AreEqual(1, NavPathFollower.CornerHoldsStarted);

            f.AdvanceCornersForTests(new Vector3(0.2f, 0f, 5.1f), 0.1f);   // past C on N's side
            Assert.AreEqual(1, f.RemainingCorners, "passed: C consumed, steering at N");
            Assert.AreEqual(0, NavPathFollower.CornerHoldTimeouts, "released by passing, not by the timeout");
        }

        [Test]
        public void Kose_AdimAsimi_TekTiktaGecilinceTuketilir()
        {
            NavPathFollower f = Bend();

            f.AdvanceCornersForTests(new Vector3(0f, 0f, 4f), 0f);   // 1 m before C: out of reach
            Assert.AreEqual(2, f.RemainingCorners);

            f.AdvanceCornersForTests(new Vector3(0f, 0f, 5.5f), 1f / 60f);   // one step 1.5 m: overshoots C along A→C
            Assert.AreEqual(1, f.RemainingCorners, "overshoot counts as passed");
            Assert.AreEqual(0, _query.SegmentCalls, "a passed corner needs no segment query");
            Assert.AreEqual(0, NavPathFollower.CornerHoldsStarted);
        }

        [Test]
        public void Kose_GenisGovdeEngelliKose_YarimSaniyedeBirakilir()
        {
            NavPathFollower f = Bend();
            f.BodyRadius = 0.9f;
            Vector3 pinned = new Vector3(0f, 0f, 4.2f);   // 0.8 m from C: never within 0.65 m

            f.AdvanceCornersForTests(pinned, 0f);
            Assert.AreEqual(2, f.RemainingCorners, "wide body reaches the hold at 0.8 m");
            Assert.AreEqual(1, NavPathFollower.CornerHoldsStarted);

            f.AdvanceCornersForTests(pinned, 0.25f);
            f.AdvanceCornersForTests(pinned, 0.49f);
            Assert.AreEqual(2, f.RemainingCorners, "still held before the cap");
            Assert.AreEqual(0, NavPathFollower.CornerHoldTimeouts);

            f.AdvanceCornersForTests(pinned, NavPathFollower.CornerHoldMaxSeconds);
            Assert.AreEqual(1, f.RemainingCorners, "released at the cap");
            Assert.AreEqual(1, NavPathFollower.CornerHoldTimeouts);
            Assert.AreEqual(1, NavPathFollower.CornerHoldsStarted, "one hold, not restarted per tick");
        }

        [Test]
        public void Kose_EngelliKose_EnFazlaYarimSaniyeTutulur()
        {
            NavPathFollower f = Bend();
            Vector3 near = new Vector3(0f, 0f, 4.6f);
            float released = -1f;
            for (int tick = 0; tick < 600 && released < 0f; tick++)
            {
                float t = tick / 60f;
                f.AdvanceCornersForTests(near, t);
                if (f.RemainingCorners == 1) released = t;
            }

            Assert.GreaterOrEqual(released, 0f, "a blocked corner is not held forever");
            Assert.GreaterOrEqual(released, NavPathFollower.CornerHoldMaxSeconds - 1e-4f, "held for the full cap while blocked");
            Assert.LessOrEqual(released, NavPathFollower.CornerHoldMaxSeconds + 1f / 60f + 1e-4f, "released within one tick of the cap");
            Assert.AreEqual(1, NavPathFollower.CornerHoldTimeouts);
            Assert.LessOrEqual(_query.SegmentCalls, Mathf.CeilToInt(NavPathFollower.CornerHoldMaxSeconds / NavPathFollower.CornerRequerySeconds) + 1,
                "held corner re-queried at most every CornerRequerySeconds");
        }

        [Test]
        public void Kose_AgDisiKaynak_IzdusumYoksaTutulmaz()
        {
            NavPathFollower f = Bend();
            _query.Projects = false;   // body pushed off the mesh

            f.AdvanceCornersForTests(new Vector3(0f, 0f, 4.6f), 0f);
            Assert.AreEqual(1, f.RemainingCorners, "no projection: consume, never a false 'blocked'");
            Assert.AreEqual(0, _query.SegmentCalls, "no segment query from an off-mesh source");
            Assert.AreEqual(0, NavPathFollower.CornerHoldsStarted);
        }

        [Test]
        public void Kose_ButceYok_SonSonucKullanilirVarsayilanTuketir()
        {
            NavPathFollower held = Bend();
            Vector3 near = new Vector3(0f, 0f, 4.6f);
            held.AdvanceCornersForTests(near, 0f);
            Assert.AreEqual(2, held.RemainingCorners);

            _query.Budget = false;
            held.AdvanceCornersForTests(near, 0.2f);
            Assert.AreEqual(2, held.RemainingCorners, "no budget: last 'blocked' answer for this corner is reused");
            Assert.AreEqual(1, _query.SegmentCalls);

            NavPathFollower fresh = Bend();
            fresh.AdvanceCornersForTests(near, 0.2f);
            Assert.AreEqual(1, fresh.RemainingCorners, "no budget and no earlier answer: not blocked, consume");
            Assert.AreEqual(1, _query.SegmentCalls, "no query without budget");
        }

        [Test]
        public void Kose_SonKose_SorgusuzTuketilir()
        {
            NavPathFollower f = Bend(A, C);

            f.AdvanceCornersForTests(new Vector3(0f, 0f, 4.6f), 0f);
            Assert.AreEqual(0, f.RemainingCorners, "final corner consumed inside the arrive radius");
            Assert.AreEqual(0, _query.BudgetCalls, "final corner never queries");
        }

        [Test]
        public void Kose_AcikDonemec_BeklemedenTuketilir()
        {
            NavPathFollower f = Bend();
            _query.Blocked = false;

            f.AdvanceCornersForTests(new Vector3(0f, 0f, 4.6f), 0f);
            Assert.AreEqual(1, f.RemainingCorners, "clear skip: consumed at the arrive radius (no single-file at open bends)");
            Assert.AreEqual(0, NavPathFollower.CornerHoldsStarted);
        }

        [Test]
        public void Kose_TutmaYolu_BellekAyirmaz()
        {
            NavPathFollower f = Bend();
            Vector3 near = new Vector3(0f, 0f, 4.6f);
            f.AdvanceCornersForTests(near, 0f);   // warm
            byte[] control = null;
            Assert.That(() => { control = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "positive control: the calling-thread GC.Alloc recorder must see allocations");
            Assert.That(() =>
            {
                for (int i = 1; i < 25; i++) f.AdvanceCornersForTests(near, i * 0.01f);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "held-corner advance allocates");
            Assert.AreEqual(2, f.RemainingCorners, "fixture: stayed on the held path");
            System.GC.KeepAlive(control);
        }
    }
}
