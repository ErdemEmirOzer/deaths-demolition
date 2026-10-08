using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-098: birlesmis kitin public davranis sozlesmeleri. Test saati ayni Tick yolunu
    /// ilerletir; sahne, kare bekleme veya performans olcumu gerektirmez.
    /// </summary>
    [TestFixture]
    [Category("WP098")]
    [Category("NONPERF")]
    [Category("TD")]
    public sealed class UiMotionAliasIntegrationTests
    {
        private float? _previousTestNow;
        private bool _previousReducedMotion;
        private GameObject _barObject;

        private sealed class CueLog : IUiMotionListener
        {
            public readonly List<int> Ids = new List<int>();

            public void OnCue(int cueId)
            {
                Ids.Add(cueId);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _previousTestNow = UiMotion.TestNow;
            _previousReducedMotion = UiMotion.ReducedMotion;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 100f;
            UiMotion.ReducedMotion = false;
            UiEvents.ResetCounters();
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ResetForTests();
            if (_barObject != null)
            {
                Object.DestroyImmediate(_barObject);
                _barObject = null;
            }

            UiEvents.ResetCounters();
            UiMotion.TestNow = _previousTestNow;
            UiMotion.ReducedMotion = _previousReducedMotion;
        }

        private static void TickAt(float seconds)
        {
            UiMotion.TestNow = seconds;
            UiMotion.Tick();
        }

        [Test]
        public void CallAndCallback_RunInTimeOrder_OnceWithoutUiEvents()
        {
            var log = new CueLog();
            UiTimeline timeline = UiMotion.Timeline(980901, log)
                .Callback(0.3f, 30)
                .Call(0.1f, 10)
                .Call(0.4f, 40)
                .Callback(0.2f, 20);
            Assert.IsTrue(timeline.IsAlive, "zaman cizelgesi kurulmus olmali");

            TickAt(100.05f);
            Assert.IsEmpty(log.Ids, "ilk cue zamanindan once cagri yok");
            TickAt(100.25f);
            CollectionAssert.AreEqual(new[] { 10, 20 }, log.Ids, "tek Tick iki aliasi zaman sirasiyla isler");
            TickAt(100.25f);
            CollectionAssert.AreEqual(new[] { 10, 20 }, log.Ids, "ayni saat yeniden islenince cue tekrarlanmaz");
            TickAt(100.5f);
            CollectionAssert.AreEqual(new[] { 10, 20, 30, 40 }, log.Ids);
            Assert.IsFalse(timeline.IsAlive, "son cue sonrasi zaman cizelgesi biter");

            timeline.Complete();
            TickAt(101f);
            CollectionAssert.AreEqual(new[] { 10, 20, 30, 40 }, log.Ids, "bitmis cizelge tekrar cagri uretmez");
            Assert.AreEqual(0, UiEvents.RaisedCount, "Call ve Callback yalniz listener'a gider");
        }

        [Test]
        public void CancelAndComplete_DoNotRepeatCallbacks()
        {
            const int cancelOwner = 980902;
            var cancelledLog = new CueLog();
            UiTimeline cancelled = UiMotion.Timeline(cancelOwner, cancelledLog)
                .Call(0.1f, 1)
                .Callback(0.2f, 2);
            TickAt(100.11f);
            CollectionAssert.AreEqual(new[] { 1 }, cancelledLog.Ids);

            UiMotion.KillOwner(cancelOwner, false);
            Assert.IsFalse(cancelled.IsAlive, "owner iptali gelecekteki cue'lari birakir");
            TickAt(100.5f);
            CollectionAssert.AreEqual(new[] { 1 }, cancelledLog.Ids, "iptal edilen Callback calismaz");

            var skippedLog = new CueLog();
            UiTimeline skipped = UiMotion.Timeline(980903, skippedLog)
                .Callback(0.1f, 11)
                .Call(0.2f, 12)
                .Callback(0.3f, 13);
            cancelled.Complete();
            Assert.IsTrue(skipped.IsAlive, "eski handle yeni zaman cizelgesini tamamlayamaz");
            skipped.Complete();
            CollectionAssert.AreEqual(new[] { 13 }, skippedLog.Ids, "skip kalan cue'lari yalniz son cue'ya indirger");
            Assert.IsFalse(skipped.IsAlive);

            skipped.Complete();
            cancelled.Complete();
            UiMotion.KillOwner(cancelOwner, false);
            TickAt(101f);
            CollectionAssert.AreEqual(new[] { 1 }, cancelledLog.Ids);
            CollectionAssert.AreEqual(new[] { 13 }, skippedLog.Ids, "tekrar Complete ve Tick ikinci callback uretmez");
            Assert.AreEqual(0, UiEvents.RaisedCount, "iptal ve skip sessiz cue'lari UiEvent'e donusturmez");
        }

        [Test]
        public void Reveal_HoldsExplicitFromUntilDelay_ThenFillsToValue()
        {
            _barObject = new GameObject("RevealBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiBar));
            Image fill = _barObject.GetComponent<Image>();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            UiBar bar = _barObject.GetComponent<UiBar>();
            bar.Configure(fill, UiColor.TextPrimary);
            bar.SetValue(0.8f, true);

            const float from = 0.2f;
            const float delay = 0.25f;
            float duration = UiMotionTokens.Seconds(UiDuration.Base);
            UiTweenHandle reveal = bar.Reveal(from, UiDuration.Base, delay, 980904);
            Assert.IsTrue(reveal.IsAlive, "reveal hareketi kurulmus olmali");
            Assert.AreEqual(from, fill.fillAmount, 1e-5f, "cagri aninda explicit baslangic gorunur");
            Assert.AreEqual(0.8f, bar.Value, 1e-5f, "gorsel reveal veri degerini degistirmez");

            TickAt(100f + delay * 0.5f);
            Assert.AreEqual(from, fill.fillAmount, 1e-5f, "gecikme boyunca baslangic korunur");
            TickAt(100f + delay);
            Assert.AreEqual(from, fill.fillAmount, 1e-5f, "gecikme sinirinda hareket baslangictadir");
            TickAt(100f + delay + duration * 0.5f);
            Assert.Greater(fill.fillAmount, from, "sure icinde dolum ilerler");
            Assert.Less(fill.fillAmount, bar.Value, "sure bitmeden hedefe sicramaz");
            TickAt(100f + delay + duration + 0.01f);
            Assert.AreEqual(bar.Value, fill.fillAmount, 1e-5f, "sure sonunda veri degerine ulasir");
            Assert.IsFalse(reveal.IsAlive);

            UiMotion.ReducedMotion = true;
            bar.Reveal(from, UiDuration.Base, delay, 980904);
            Assert.AreEqual(bar.Value, fill.fillAmount, 1e-5f, "Delta: azaltilmis harekette gecikmeden son deger");
        }
    }
}
