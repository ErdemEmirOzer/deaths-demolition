using System.Collections;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 slice 8 · E18 damage numbers (S-07): one overlay canvas at WorldNumber 40, 48 pooled TMP numbers on one
    /// FaceOutline material, the GD size hierarchy, merge per target, ≤ 12 visible, flush on RoundComplete / Dead on the
    /// unscaled clock, upward-only rise vs reduced motion at rest, zero allocation per hit.
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiWorldNumbersTests
    {
        private Camera _cam;
        private float _timeScaleWas;

        [SetUp]
        public void SetUp()
        {
            _timeScaleWas = Time.timeScale;
            var go = new GameObject("WorldNumbersTestCamera");
            _cam = go.AddComponent<Camera>();
            _cam.transform.position = Vector3.zero;
            _cam.transform.rotation = Quaternion.identity;
            UiWorldNumbers.CameraOverride = _cam;
            UiWorldNumbers.Instance.Clear();
            UiMotion.ReducedMotion = false;
        }

        [TearDown]
        public void TearDown()
        {
            UiWorldNumbers.Instance.Clear();
            UiWorldNumbers.CameraOverride = null;
            UiMotion.ReducedMotion = false;
            Time.timeScale = _timeScaleWas;
            if (_cam != null) Object.Destroy(_cam.gameObject);
        }

        private static readonly Vector3 Ahead = new Vector3(0f, 0f, 10f);

        [Test]
        public void TekKanvas40_Havuz48_TekMateryal_FaceOutline()
        {
            UiWorldNumbers numbers = UiWorldNumbers.Instance;
            Canvas canvas = numbers.Canvas;
            Assert.IsNotNull(canvas);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.AreEqual(UiLayer.WorldNumber, canvas.sortingOrder, "sıra 40 (HUD'un altında)");
            Assert.IsFalse(canvas.GetComponent<GraphicRaycaster>().enabled, "sayılar tıklama hedefi değil");

            TextMeshProUGUI[] texts = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
            Assert.AreEqual(UiWorldNumbers.Capacity, texts.Length, "havuz 48");
            Material shared = UiWorldNumbers.SharedMaterial;
            Assert.IsNotNull(shared, "sayı materyali yok (Numeric SDF?)");
            foreach (TextMeshProUGUI t in texts)
            {
                Assert.AreSame(shared, t.fontSharedMaterial, "tek materyal → tek batch");
                Assert.IsFalse(t.raycastTarget);
            }

            Assert.IsTrue(shared.IsKeywordEnabled(ShaderUtilities.Keyword_Outline), "FaceOutline: InkStroke kontur");
            Assert.IsTrue(shared.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay), "FaceOutline: 2 px gölge");
            StringAssert.Contains("Oswald", texts[0].font != null ? texts[0].font.faceInfo.familyName : "", "Oswald (Numeric)");
        }

        [Test]
        public void Hiyerarsi_Boyutlar_Birlestirme_Starburst()
        {
            Assert.AreEqual(22, UiWorldNumbers.PointSize(HitFeedbackDirector.NumberStyle(HitFeedbackTier.FleshHit, false).Size), "et M 22");
            Assert.AreEqual(18, UiWorldNumbers.PointSize(HitFeedbackDirector.NumberStyle(HitFeedbackTier.ArmorHit, true).Size), "zırh tuttu S 18");
            Assert.AreEqual(UiColor.TextSecondary, HitFeedbackDirector.NumberStyle(HitFeedbackTier.ArmorHit, true).Color, "zırh gri");
            Assert.AreEqual(26, UiWorldNumbers.PointSize(HitFeedbackDirector.NumberStyle(HitFeedbackTier.CriticalHit, false).Size), "kritik L 26");
            Assert.IsTrue(HitFeedbackDirector.NumberStyle(HitFeedbackTier.CriticalHit, false).Burst, "kritik = starburst (şekil)");
            Assert.IsTrue(HitFeedbackDirector.NumberStyle(HitFeedbackTier.Death, false).Bold, "kill bold");
            Assert.AreEqual(30, UiWorldNumbers.PointSize(HitFeedbackDirector.NumberStyle(HitFeedbackTier.HeavyDeath, false).Size), "elite/ultra kill XL 30");

            UiWorldNumbers n = UiWorldNumbers.Instance;
            n.Spawn(7, 10f, HitFeedbackDirector.NumberStyle(HitFeedbackTier.FleshHit, false), Ahead);
            Assert.IsTrue(n.TryRead(7, out float amount, out float size, out bool burst, out bool bold));
            Assert.AreEqual(10f, amount);
            Assert.AreEqual(22f, size);
            Assert.IsFalse(burst);

            Assert.IsTrue(n.TryMerge(7, 5f, HitFeedbackDirector.NumberStyle(HitFeedbackTier.CriticalHit, false), Ahead, 1f), "pencere içinde aynı hedef birleşir");
            n.TryRead(7, out amount, out size, out burst, out bold);
            Assert.AreEqual(15f, amount, "tutar toplanır");
            Assert.AreEqual(26f, size, "en yüksek kademe kalır");
            Assert.IsTrue(burst);
            Assert.AreEqual(1, n.VisibleCount, "tek sayı");

            Assert.IsFalse(n.TryMerge(8, 5f, HitFeedbackDirector.NumberStyle(HitFeedbackTier.FleshHit, false), Ahead, 1f), "başka hedef birleşmez");
            Assert.IsFalse(n.TryMerge(0, 5f, HitFeedbackDirector.NumberStyle(HitFeedbackTier.FleshHit, false), Ahead, 1f), "sahipsiz birleşmez");
        }

        [Test]
        public void EnFazla12Gorunur_EnEskiYerVerir()
        {
            UiWorldNumbers n = UiWorldNumbers.Instance;
            var style = new UiWorldNumberStyle(UiWorldNumberSize.M, UiColor.TextPrimary);
            for (int i = 1; i <= 20; i++) n.Spawn(i, i, style, Ahead);
            Assert.AreEqual(UiWorldNumbers.MaxVisible, n.VisibleCount, "≤ 12 görünür");
            Assert.IsFalse(n.TryRead(1, out _, out _, out _, out _), "en eski yer verdi");
            Assert.IsTrue(n.TryRead(20, out _, out _, out _, out _), "en yeni görünür");
        }

        [UnityTest]
        public IEnumerator FazDegisimi_RoundCompleteVeDead_UnscaledFlush()
        {
            UiWorldNumbers n = UiWorldNumbers.Instance;
            var style = new UiWorldNumberStyle(UiWorldNumberSize.M, UiColor.TextPrimary);
            foreach (RunPhase phase in new[] { RunPhase.RoundComplete, RunPhase.Dead })
            {
                Time.timeScale = 1f;
                for (int i = 1; i <= 3; i++) n.Spawn(i, 22f, style, Ahead);
                yield return null;
                Time.timeScale = 0f;   // the world freezes on the beat — the numbers must not
                RunEvents.PublishPhaseChanged(phase);
                Assert.AreEqual(0, n.VisibleCount, phase + ": flush başladı");
                Assert.AreEqual(3, n.DrawingCount, phase + ": 200 ms içinde soluyor");
                yield return new WaitForSecondsRealtime(UiWorldNumbers.FlushSeconds + 0.1f);
                Assert.AreEqual(0, n.DrawingCount, phase + ": timeScale 0 altında da 200 ms'de gitti");
            }

            Time.timeScale = 1f;
            n.Spawn(9, 22f, style, Ahead);
            RunEvents.PublishPhaseChanged(RunPhase.Countdown);
            Assert.AreEqual(1, n.VisibleCount, "diğer fazlar sayıya dokunmaz");
        }

        [UnityTest]
        public IEnumerator Yukselis_YalnizYukari_RmDinlenmede()
        {
            UiWorldNumbers n = UiWorldNumbers.Instance;
            var style = new UiWorldNumberStyle(UiWorldNumberSize.M, UiColor.TextPrimary);
            n.Spawn(1, 10f, style, Ahead);
            yield return null;
            n.TryReadPlacement(1, out Vector2 p0, out float a0);
            yield return new WaitForSeconds(0.3f);
            n.TryReadPlacement(1, out Vector2 p1, out float a1);
            Assert.AreEqual(p0.x, p1.x, 0.01f, "yanal kayma yok");
            Assert.That(p1.y - p0.y, Is.InRange(8f, UiWorldNumbers.RisePx), "24 px yükselişin yolunda");
            Assert.AreEqual(1f, a1, 1e-3f, "son 200 ms'ye kadar tam opak");
            yield return new WaitForSeconds(UiWorldNumbers.LifeSeconds);
            Assert.IsFalse(n.TryReadPlacement(1, out _, out _), "600 ms sonunda bitti");

            UiMotion.ReducedMotion = true;
            n.Spawn(2, 10f, style, Ahead);
            yield return null;
            n.TryReadPlacement(2, out Vector2 r0, out _);
            yield return new WaitForSeconds(0.35f);
            n.TryReadPlacement(2, out Vector2 r1, out float ra);
            Assert.AreEqual(r0.y, r1.y, 0.01f, "RM: dinlenmede, yükseliş yok");
            Assert.AreEqual(1f, ra, 1e-3f, "RM: 500 ms tam görünür");
            yield return new WaitForSeconds(0.45f);
            Assert.IsFalse(n.TryReadPlacement(2, out _, out _), "RM: 500 + 200 ms'de gitti");
        }

        [UnityTest]
        public IEnumerator VurusBasinaSifirAyirma()
        {
            UiWorldNumbers n = UiWorldNumbers.Instance;
            var flesh = new UiWorldNumberStyle(UiWorldNumberSize.M, UiColor.TextPrimary);
            var crit = new UiWorldNumberStyle(UiWorldNumberSize.L, UiColor.TextPrimary, false, true);
            for (int i = 1; i <= 12; i++) n.Spawn(i, i, i % 3 == 0 ? crit : flesh, Ahead);   // warm: every slot styled once
            yield return null;
            n.Clear();
            yield return null;

            // WP-044 referee method (WP-059D pattern): a burst frame (25 hits: merges + spawns, the TMP rebuilds they cause)
            // vs a control frame, min over 4 pairs, with a positive control — GC.GetAllocatedBytesForCurrentThread is 0 in
            // Unity Mono.
            UnityEngine.Profiling.Profiler.enabled = true;
            using (var bytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                var bait = new object[64];
                for (int i = 0; i < bait.Length; i++) bait[i] = new byte[1024];
                yield return null;
                long control = bytes.LastValue;
                System.GC.KeepAlive(bait);
                Assert.Greater(control, 60 * 1024, "pozitif kontrol: kayıtçı ayırmayı görmeli");

                long best = long.MaxValue;
                for (int pair = 0; pair < 4; pair++)
                {
                    for (int i = 1; i <= 25; i++)
                    {
                        if (!n.TryMerge(i % 6 + 1, 7f, i % 4 == 0 ? crit : flesh, Ahead, 1f))
                        {
                            n.Spawn(i % 6 + 1, 7f, flesh, Ahead);
                        }
                    }

                    yield return null;
                    long burstFrame = bytes.LastValue;
                    yield return null;
                    long quiet = bytes.LastValue;
                    best = System.Math.Min(best, burstFrame - quiet);
                }

                Assert.LessOrEqual(best, 0, "25 vuruşluk kare kontrol karesinden fazla ayırdı: +" + best + " B");
            }
        }
    }
}
