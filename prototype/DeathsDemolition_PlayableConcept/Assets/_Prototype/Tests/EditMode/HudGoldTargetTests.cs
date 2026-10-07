using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // WP-051C: Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-094 HUD v4 (R3, C-00 07.10) · the gold target's ledger and the counter's arithmetic, with explicit clocks and
    /// frames (no scene; the live point is HudV4Tests in PlayMode, top-down). The contract is K/wp-094/ALTIN-HEDEF-API.md:
    /// - the counter shows balance − in flight and never counts down for a flight, in either order of credit and departure;
    /// - an arrival releases the oldest flight first, counts up and punches at most once per 120 ms, the gain gathering;
    /// - a flight that never arrives is released after 2.0 s unscaled; a HUD (re)bind empties the ledger;
    /// - without a Depart the counter is the pre-WP-094 counter.
    /// </summary>
    [TestFixture]
    [Category("WP094")]
    public sealed class HudGoldTargetTests
    {
        private const float Roll = 0.6f;   // the counter's roll (UiDuration.Reveal)

        private sealed class FakeView : IHudGoldView
        {
            public int Flights;
            public readonly List<int> Arrivals = new List<int>();
            public bool Visible = true;

            public bool GoldTargetVisible => Visible;

            public void GoldFlightsChanged()
            {
                Flights++;
            }

            public void GoldArrived(int amount)
            {
                Arrivals.Add(amount);
            }
        }

        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            HudGoldTarget.ResetStatics();
        }

        [TearDown]
        public void TearDown()
        {
            HudGoldTarget.ResetStatics();
            if (_target != null)
            {
                Object.DestroyImmediate(_target);
            }
        }

        private FakeView Bind()
        {
            _target = new GameObject("HudGoldTargetTests.Target", typeof(RectTransform));
            var view = new FakeView();
            HudGoldTarget.Bind(view, (RectTransform)_target.transform, null);
            return view;
        }

        [Test]
        public void HudGoldTarget_DepartArrive_SayaciTutarVeSerbestBirakir()
        {
            FakeView view = Bind();
            var arrived = new List<int>();
            HudGoldTarget.Arrived += arrived.Add;
            var readout = new HudGoldReadout();
            int balance = 100, frame = 1;
            float now = 10f;

            Assert.AreEqual(HudGoldCounterAction.SetNow, readout.Resolve(balance, HudGoldTarget.InFlight, frame, now, Roll), "the run's first value is set");
            Assert.AreEqual(100, readout.Shown);
            Assert.IsFalse(readout.TakeFeedback(now, out _, out _), "no gain on the first value");

            // A coin leaves a frame before its credit lands: the counter holds at 100 - it never counts down for a flight.
            frame++;
            now += 0.016f;
            HudGoldTarget.Depart(25, now);
            Assert.AreEqual(25, HudGoldTarget.InFlight);
            Assert.AreEqual(1, view.Flights, "the HUD re-reads on a departure");
            Assert.AreEqual(HudGoldCounterAction.None, readout.Resolve(balance, HudGoldTarget.InFlight, frame, now, Roll));
            Assert.AreEqual(100, readout.Shown, "held, not 75");
            frame++;
            now += 0.016f;
            balance += 25;   // the wallet credit
            Assert.AreEqual(HudGoldCounterAction.None, readout.Resolve(balance, HudGoldTarget.InFlight, frame, now, Roll));
            Assert.AreEqual(100, readout.Shown, "balance 125 − 25 flying");

            // The other order in one frame: the credit, then its departure - undone before anything was drawn, so SET back.
            frame++;
            now += 0.016f;
            balance += 10;
            Assert.AreEqual(HudGoldCounterAction.RollTo, readout.Resolve(balance, HudGoldTarget.InFlight, frame, now, Roll));
            Assert.AreEqual(110, readout.Shown);
            HudGoldTarget.Depart(10, now);
            Assert.AreEqual(35, HudGoldTarget.InFlight);
            Assert.AreEqual(HudGoldCounterAction.SetNow, readout.Resolve(balance, HudGoldTarget.InFlight, frame, now, Roll), "same frame: set back, never roll from 110");
            Assert.AreEqual(100, readout.Shown);
            Assert.AreEqual(0, readout.GainPending, "+10 then −10: no '+N' for a coin still flying");
            Assert.IsFalse(readout.TakeFeedback(now, out _, out _));

            // The first coin lands: FIFO releases its 25, the counter counts up to 125, a punch is due with "+25", the event rises.
            frame++;
            now += 0.3f;
            HudGoldTarget.Arrive(25, now);
            readout.NoteArrival();   // what RunHudScreen's GoldArrived does
            Assert.AreEqual(10, HudGoldTarget.InFlight);
            CollectionAssert.AreEqual(new[] { 25 }, view.Arrivals);
            CollectionAssert.AreEqual(new[] { 25 }, arrived, "Arrived once, with this arrival's amount");
            Assert.AreEqual(HudGoldCounterAction.RollTo, readout.Resolve(balance, HudGoldTarget.InFlight, frame, now, Roll));
            Assert.AreEqual(125, readout.Shown);
            Assert.IsTrue(readout.TakeFeedback(now, out int gain, out bool punch));
            Assert.IsTrue(punch);
            Assert.AreEqual(25, gain);

            // The second lands 50 ms later: counted at once, but the punch waits for its 120 ms slot and the gain gathers.
            frame++;
            now += 0.05f;
            HudGoldTarget.Arrive(10, now);
            readout.NoteArrival();
            Assert.AreEqual(0, HudGoldTarget.InFlight);
            Assert.AreEqual(HudGoldCounterAction.RollTo, readout.Resolve(balance, HudGoldTarget.InFlight, frame, now, Roll));
            Assert.AreEqual(135, readout.Shown, "the counter does not wait for the punch");
            Assert.IsFalse(readout.TakeFeedback(now, out _, out _), "inside the 120 ms punch slot");
            now += HudGoldTarget.PunchMinSeconds;
            Assert.IsTrue(readout.TakeFeedback(now, out gain, out punch));
            Assert.IsTrue(punch);
            Assert.AreEqual(10, gain);

            // An arrival with nothing in flight only punches and raises the event: the counter is not touched.
            HudGoldTarget.Arrive(5, now + 1f);
            Assert.AreEqual(0, HudGoldTarget.InFlight);
            CollectionAssert.AreEqual(new[] { 25, 10, 5 }, arrived);
            frame++;
            Assert.AreEqual(HudGoldCounterAction.None, readout.Resolve(balance, HudGoldTarget.InFlight, frame, now + 1f, Roll));

            // Nothing ≤ 0 counts.
            HudGoldTarget.Depart(0, now);
            HudGoldTarget.Depart(-3, now);
            HudGoldTarget.Arrive(0, now);
            Assert.AreEqual(0, HudGoldTarget.InFlight);
            Assert.AreEqual(3, arrived.Count);

            // A purchase is shown as it is: the counter rolls down, no gain.
            frame++;
            balance -= 60;
            Assert.AreEqual(HudGoldCounterAction.RollTo, readout.Resolve(balance, HudGoldTarget.InFlight, frame, now + 2f, Roll));
            Assert.AreEqual(75, readout.Shown);
            Assert.AreEqual(0, readout.GainPending);
            Debug.Log("[DD-TEST] wp094.goldTarget departArrive=OK arrivals=" + arrived.Count);
        }

        [Test]
        public void HudGoldTarget_VarmayanAltin_2SaniyedeSerbest_BaglamaDefteriBosaltir()
        {
            FakeView view = Bind();
            int arrivedCount = 0;
            HudGoldTarget.Arrived += a => arrivedCount++;
            var readout = new HudGoldReadout();
            readout.Resolve(50, 0, 1, 0f, Roll);

            HudGoldTarget.Depart(7, 0f);
            HudGoldTarget.Depart(3, 1.5f);
            Assert.AreEqual(10, HudGoldTarget.InFlight);
            Assert.AreEqual(0, HudGoldTarget.Tick(1.99f), "not yet");
            Assert.AreEqual(7, HudGoldTarget.Tick(HudGoldTarget.StaleSeconds), "the first flight is 2.0 s old");
            Assert.AreEqual(3, HudGoldTarget.InFlight);
            Assert.AreEqual(3, view.Flights, "two departures + one release");
            Assert.AreEqual(0, HudGoldTarget.Tick(3.49f));
            Assert.AreEqual(3, HudGoldTarget.Tick(3.5f));
            Assert.AreEqual(0, HudGoldTarget.InFlight);
            Assert.AreEqual(0, arrivedCount, "a stale release is not an arrival");

            // The counter behind it: credited 10 while flying (held at 50), the stale release counts it up without a punch.
            Assert.AreEqual(HudGoldCounterAction.None, readout.Resolve(60, 10, 2, 0.1f, Roll));
            Assert.AreEqual(50, readout.Shown);
            Assert.AreEqual(HudGoldCounterAction.RollTo, readout.Resolve(60, 0, 3, 3.5f, Roll));
            Assert.AreEqual(60, readout.Shown);
            Assert.IsTrue(readout.TakeFeedback(3.5f, out int gain, out bool punch));
            Assert.IsFalse(punch, "no coin landed");
            Assert.AreEqual(10, gain);

            // A stale flight is also released by the next Depart / Arrive, without a Tick.
            HudGoldTarget.Depart(4, 10f);
            HudGoldTarget.Arrive(1, 12.5f);
            Assert.AreEqual(0, HudGoldTarget.InFlight, "released at 12.5 (4 stale), the arrival found nothing");

            // Past the capacity the newest flight absorbs the rest; the total stays exact.
            for (int i = 0; i < HudGoldTarget.FlightCapacity + 5; i++)
            {
                HudGoldTarget.Depart(2, 20f + i * 0.001f);
            }

            Assert.AreEqual((HudGoldTarget.FlightCapacity + 5) * 2, HudGoldTarget.InFlight);
            HudGoldTarget.Arrive(HudGoldTarget.FlightCapacity * 2 + 10, 20.5f);
            Assert.AreEqual(0, HudGoldTarget.InFlight);

            // A HUD (re)bind, a run boundary and a statics reset empty the ledger.
            HudGoldTarget.Depart(9, 30f);
            int version = HudGoldTarget.LayoutVersion;
            HudGoldTarget.Bind(view, (RectTransform)_target.transform, null);
            Assert.AreEqual(0, HudGoldTarget.InFlight, "a rebuilt HUD holds nothing");
            Assert.Greater(HudGoldTarget.LayoutVersion, version, "a bind is a new layout");
            HudGoldTarget.Depart(9, 30f);
            HudGoldTarget.ResetFlights();
            Assert.AreEqual(0, HudGoldTarget.InFlight, "run start / run end");
            HudGoldTarget.Depart(9, 30f);
            HudGoldTarget.ResetStatics();
            Assert.AreEqual(0, HudGoldTarget.InFlight);
            Assert.IsFalse(HudGoldTarget.IsBound);
            Assert.AreEqual(2, arrivedCount, "the two arrivals above (12.5 s and 20.5 s)");
            HudGoldTarget.Arrive(1, 31f);
            Assert.AreEqual(2, arrivedCount, "the listener added before ResetStatics was dropped (static-bus rearm)");
        }

        [Test]
        public void HudGoldTarget_DepartYokken_SayacBugunkuGibi()
        {
            // No HUD bound: there is no point to fly to.
            Assert.IsFalse(HudGoldTarget.TryGetScreenPoint(out _));
            Assert.IsFalse(HudGoldTarget.TryGetWorldPoint(null, 10f, out _));

            // The pre-WP-094 counter: the first value set, every later balance rolled with its whole delta as "+N".
            var readout = new HudGoldReadout();
            int[] balances = { 0, 7, 7, 19, 4, 30 };
            int frame = 1;
            float now = 1f;
            int previous = int.MinValue;
            foreach (int balance in balances)
            {
                HudGoldCounterAction action = readout.Resolve(balance, HudGoldTarget.InFlight, frame, now, Roll);
                if (previous == int.MinValue)
                {
                    Assert.AreEqual(HudGoldCounterAction.SetNow, action, "first value");
                }
                else if (balance == previous)
                {
                    Assert.AreEqual(HudGoldCounterAction.None, action, "quiet frame");
                }
                else
                {
                    Assert.AreEqual(HudGoldCounterAction.RollTo, action, "a change rolls");
                }

                Assert.AreEqual(balance, readout.Shown, "the counter shows the balance");
                bool fed = readout.TakeFeedback(now, out int gain, out bool punch);
                Assert.IsFalse(punch, "no punch without an arrival");
                if (previous != int.MinValue && balance > previous)
                {
                    Assert.IsTrue(fed);
                    Assert.AreEqual(balance - previous, gain, "'+N' = the credit, on its own frame");
                }
                else
                {
                    Assert.IsFalse(fed, "no '+N' for a first value, a quiet frame or a purchase");
                }

                previous = balance;
                frame++;
                now += 0.25f;
            }

            Assert.AreEqual(0, HudGoldTarget.InFlight);
            Assert.AreEqual(0, HudGoldTarget.LayoutVersion, "nothing bound, nothing moved");

            // Quiet frames allocate nothing (WP-051C: the recorder is shown to see an allocation first).
            readout.Resolve(30, 0, frame, now, Roll);
            byte[] probe = null;
            Assert.That(() => { probe = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "positive control");
            Assert.IsNotNull(probe);
            Assert.That(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    readout.Resolve(30, HudGoldTarget.InFlight, frame + i, now, Roll);
                    readout.TakeFeedback(now, out _, out _);
                    HudGoldTarget.Tick(now);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
