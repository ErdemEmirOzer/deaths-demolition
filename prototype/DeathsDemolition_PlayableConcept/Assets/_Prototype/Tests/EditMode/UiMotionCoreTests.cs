using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 2 · the UiMotion core (UI v3 direction §3; tests T4 / T5 / T6 +
    /// FlashGate). Driven with the test clock (<see cref="UiMotion.TestNow"/> + Tick), so
    /// time-based and frame-rate independent. Covers end states, the Reduced Motion policy
    /// by motion class, one tween per (target, channel), overflow landing, timelines with
    /// ordered / collapsing cues, the FlashGate, 0 allocations for 256 tweens, and the
    /// drift ratchet over the screens' hand-rolled motion.
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiMotionCoreTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private bool _reduced;

        [SetUp]
        public void SetUp()
        {
            _reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 100f;
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ResetForTests();
            UiMotion.TestNow = null;
            UiMotion.ReducedMotion = _reduced;
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        private CanvasGroup Group()
        {
            var go = new GameObject("G", typeof(RectTransform), typeof(CanvasGroup));
            _created.Add(go);
            return go.GetComponent<CanvasGroup>();
        }

        private static void Step(float seconds)
        {
            UiMotion.TestNow += seconds;
            UiMotion.Tick();
        }

        [Test]
        public void Tween_SuresindeHedefe_Varir_SonraSerbest()
        {
            CanvasGroup g = Group();
            g.alpha = 0f;
            UiTweenHandle h = UiMotion.Alpha(g, 1f, UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
            Assert.IsTrue(h.IsAlive);
            Step(0.14f);
            Assert.Greater(g.alpha, 0.5f, "Settle yarı sürede yarıyı geçer");
            Assert.Less(g.alpha, 1f);
            Step(0.15f);
            Assert.AreEqual(1f, g.alpha, 1e-5f);
            Assert.IsFalse(h.IsAlive, "biten tween havuza döner");
            Assert.AreEqual(0, UiMotion.ActiveCount);
        }

        [Test]
        public void AzHareket_SinifPolitikasi()
        {
            UiMotion.ReducedMotion = true;

            CanvasGroup decor = Group();
            decor.alpha = 0f;
            UiTweenHandle h = UiMotion.Alpha(decor, 1f, UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
            Assert.IsFalse(h.IsValid, "Decor RM'de tween açmaz");
            Assert.AreEqual(1f, decor.alpha, "Decor RM'de son duruma iner (fade 0 ms)");

            CanvasGroup delta = Group();
            delta.alpha = 0f;
            UiMotion.Alpha(delta, 0.7f, UiDuration.Reveal, UiEase.Settle, UiMotionClass.Delta);
            Assert.AreEqual(0.7f, delta.alpha, 1e-5f, "Delta RM'de son değer");

            CanvasGroup data = Group();
            data.alpha = 0f;
            UiTweenHandle d = UiMotion.Alpha(data, 1f, UiDuration.Screen, UiEase.Linear, UiMotionClass.Data);
            Assert.IsTrue(d.IsAlive, "Data RM'de değişmez");
            Step(0.14f);
            Assert.AreEqual(0.5f, data.alpha, 1e-3f, "Data doğrusal ilerler");

            var go = new GameObject("P", typeof(RectTransform), typeof(Image));
            _created.Add(go);
            var image = go.GetComponent<Image>();
            UiTweenHandle p = UiMotion.Pulse(image, 0.2f, 0.9f, 1f);
            Assert.IsFalse(p.IsValid, "Attention RM'de döngü açmaz");
            Assert.AreEqual(0.9f, image.canvasRenderer.GetAlpha(), 1e-5f, "Attention RM'de tepede tutulur");
        }

        [Test]
        public void HedefKanalBasinaTekTween_YenidenHedefleme()
        {
            CanvasGroup g = Group();
            g.alpha = 0f;
            UiMotion.Alpha(g, 1f, UiDuration.Screen, UiEase.Linear, UiMotionClass.Decor);
            Step(0.14f);
            UiMotion.Alpha(g, 0f, UiDuration.Screen, UiEase.Linear, UiMotionClass.Decor);
            Assert.AreEqual(1, UiMotion.ActiveCount, "(hedef, kanal) başına bir tween");
            float from = g.alpha;
            Step(0.14f);
            Assert.Less(g.alpha, from, "mevcut değerden yeni hedefe");
        }

        [Test]
        public void Tasma_SonDurumaIner_CalisaniEzmez()
        {
            var groups = new CanvasGroup[UiMotion.Capacity + 1];
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = Group();
                groups[i].alpha = 0f;
                UiMotion.Alpha(groups[i], 1f, UiDuration.Reveal, UiEase.Linear, UiMotionClass.Decor);
            }

            Assert.AreEqual(UiMotion.Capacity, UiMotion.ActiveCount);
            Assert.AreEqual(1f, groups[groups.Length - 1].alpha, "taşan tween son durumuna iner");
            Step(0.3f);
            Assert.AreEqual(0.5f, groups[0].alpha, 1e-3f, "çalışan tween ezilmedi");
        }

        private sealed class CueLog : IUiMotionListener
        {
            public readonly List<int> Ids = new List<int>();

            public void OnCue(int cueId)
            {
                Ids.Add(cueId);
            }
        }

        [Test]
        public void ZamanCizelgesi_CuelarSiraliKareIcinde_AtlamaSonCueyaCoker()
        {
            var log = new CueLog();
            UiTimeline tl = UiMotion.Timeline(1, log);
            tl.Cue(UiEvent.ResultsRevealed, 0.30f, 3).Cue(UiEvent.ResultsRevealed, 0.10f, 1).Cue(UiEvent.ResultsRevealed, 0.20f, 2);
            Step(0.25f);
            CollectionAssert.AreEqual(new[] { 1, 2 }, log.Ids, "iki cue aynı karede, yazım sırasıyla");
            Step(0.10f);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, log.Ids);
            Step(0.01f);
            Assert.IsFalse(tl.IsAlive, "cue'ları ve adımları biten çizelge kapanır");

            var skip = new CueLog();
            CanvasGroup g = Group();
            g.alpha = 0f;
            UiTimeline t2 = UiMotion.Timeline(2, skip);
            t2.At(0.2f).Add(UiMotion.Alpha(g, 1f, UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor, t2.Cursor));
            t2.Cue(UiEvent.ResultsRevealed, 0.1f, 10).Cue(UiEvent.LevelUp, 0.3f, 11).Cue(UiEvent.UnlockRevealed, 0.5f, 12);
            t2.Complete();
            Assert.AreEqual(1f, g.alpha, "atlama görselleri t=1'e indirir");
            CollectionAssert.AreEqual(new[] { 12 }, skip.Ids, "cue'lar son cue'ya çöker");
            Assert.IsFalse(t2.IsAlive);
        }

        [Test]
        public void FlashKapisi_SaniyedeUcBuyukBaslangic()
        {
            int allowed = 0;
            for (int i = 0; i < 10; i++)
            {
                if (UiMotion.FlashOnset(true)) allowed++;
                Step(0.1f);
            }

            Assert.AreEqual(UiMotionTokens.FlashGateOnsetsPerSecond, allowed, "10 başlangıç/s → ≤ 3 (fazlası birleşir)");
            Assert.IsTrue(UiMotion.FlashOnset(false), "küçük parıltılar sayılmaz");
            Step(1.1f);
            Assert.IsTrue(UiMotion.FlashOnset(true), "bir saniye sonra yeniden");
        }

        [Test]
        public void SifirAyirma_256Tween_OynatVeIlerle()
        {
            var groups = new CanvasGroup[UiMotion.Capacity];
            for (int i = 0; i < groups.Length; i++) groups[i] = Group();

            // Warm-up: first calls, JIT, pools.
            Cycle(groups);
            UiMotion.ResetForTests();

            // The EditMode allocation referee (HeapDelta: a full collection before each window,
            // the minimum over three windows, a 64 KB positive control read the same way).
            long control = HeapDelta.PositiveControl();
            long delta = HeapDelta.Min(() =>
            {
                for (int n = 0; n < 40; n++) Cycle(groups);
            });

            Debug.Log("[DD-TEST] wp068.motionAlloc delta=" + delta + " B (40 × 256 Play + 40 × 20 Tick) control=" + control + " B");
            Assert.GreaterOrEqual(control, HeapDelta.PositiveControlBytes, "pozitif kontrol: 64 KB görülmedi (ölçüm kör)");
            Assert.Less(delta, 16384, "UiMotion Play/Tick ayırmamalı");
        }

        private static void Cycle(CanvasGroup[] groups)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                UiMotion.Alpha(groups[i], (i & 1) == 0 ? 1f : 0f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
            }

            for (int f = 0; f < 20; f++) Step(0.016f);
        }

        [Test]
        public void Buton_KareBasiGuncellemeYok()
        {
            MethodInfo update = typeof(UiButton).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNull(update, "UiButton her kare çalışmaz (renkler Tint tween'i, etiket anında)");
            MethodInfo barUpdate = typeof(UiBar).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNull(barUpdate, "UiBar dolumu UiMotion Fill tween'i");
        }

        // ------------------------------------------------------------------ T6 drift ratchet

        /// <summary>Per-screen counts today (WP-068 slice 2 baseline). They may only go down as screen slices move motion onto presets.</summary>
        private static readonly Dictionary<string, int[]> Baseline = new Dictionary<string, int[]>
        {
            // file: { Lerp, unscaledDeltaTime, MoveTowards, new Color(, Update/LateUpdate }
            { "MainMenuScreen.cs", new[] { 0, 0, 0, 1, 1 } },
            { "PauseScreen.cs", new[] { 0, 0, 0, 0, 1 } },
            { "ResultsScreen.cs", new[] { 0, 0, 0, 0, 1 } },
            { "RunHudScreen.cs", new[] { 7, 3, 2, 0, 2 } },
            { "SelectionScreen.cs", new[] { 0, 0, 0, 0, 1 } },
            { "UpgradeScreen.cs", new[] { 0, 1, 1, 0, 1 } }
        };

        private static readonly string[] Patterns =
        {
            @"Mathf\.Lerp|Color\.Lerp", @"unscaledDeltaTime", @"MoveTowards", @"new Color\(", @"void (Late)?Update\(\)"
        };

        private static readonly string[] KitUpdateAllowList =
        {
            "UiBar.cs", "UiEnemyStatusMarkers.cs", "UiHitDirection.cs", "UiMotion.cs", "UiMotionCore.cs", "UiInputGlyphs.cs",
            "UiBackdrop.cs"   // slice 6: parallax follow + lantern breath read the pointer / stick every frame
        };

        [Test]
        public void SuruklenmeKilidi_EkranlarElYapimiHareketArtiramaz()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime");
            var failures = new List<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(root, "Screens"), "*.cs"))
            {
                string name = Path.GetFileName(file);
                string text = File.ReadAllText(file);
                int[] limit;
                if (!Baseline.TryGetValue(name, out limit))
                {
                    limit = new int[Patterns.Length]; // a new screen starts clean
                }

                for (int p = 0; p < Patterns.Length; p++)
                {
                    int count = Regex.Matches(text, Patterns[p]).Count;
                    if (count > limit[p])
                    {
                        failures.Add(name + " '" + Patterns[p] + "' " + count + " > " + limit[p]);
                    }
                }
            }

            foreach (string file in Directory.GetFiles(Path.Combine(root, "Kit"), "*.cs"))
            {
                string name = Path.GetFileName(file);
                if (Regex.IsMatch(File.ReadAllText(file), Patterns[4]) && System.Array.IndexOf(KitUpdateAllowList, name) < 0)
                {
                    failures.Add("Kit/" + name + " per-frame Update (izin listesinde değil: UiMotion preset'i kullan)");
                }
            }

            Assert.IsEmpty(failures, "el yapımı hareket arttı (T6):\n" + string.Join("\n", failures));
        }
    }
}
