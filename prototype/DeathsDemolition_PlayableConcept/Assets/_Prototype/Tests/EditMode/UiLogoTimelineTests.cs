using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// AP-035 R3 (S-11 O2, C-00 08.10) · the logo timeline (pure C#): absolute-clock phases (a long frame jumps to the right
    /// pose - C-00 08.10), the +0.5 s rule on every ready time (K3), reduced motion (K6), the menu intro ≤ 2.0 s and its skip
    /// (K7), the seal samples (pivot bottom 648), the loading outro (v001 17 frames), zero allocation per tick.
    /// </summary>
    [TestFixture]
    [Category("AP035")]
    public sealed class UiLogoTimelineTests
    {
        private const float T0 = 100f;

        [Test]
        public void Zaman_MutlakSaat_TekBuyukAdimDogruPozaAtlar()
        {
            var t = new UiLogoTimeline();
            t.BeginLoading(T0, false);
            // one 0.9 s step: the pose of 0.9 s, not of one frame
            Assert.AreEqual(UiLogoPhase.Intro, Phase(t, T0 + 0.9f));
            Assert.AreEqual(0.9f * UiLogoTimeline.IntroSpeed, t.SpecTime(T0 + 0.9f), 1e-4f, "spec time = (now - start) x speed");

            // intro -> loop in one big step lands in the loop at the right loop time
            float wall = UiLogoTimeline.IntroSeconds / UiLogoTimeline.IntroSpeed;
            Assert.AreEqual(2.0991f, wall, 1e-3f, "K2: 67 frames x 1.33");
            Assert.AreEqual(UiLogoPhase.Loop, Phase(t, T0 + wall + 9.3f));
            Assert.AreEqual(9.3f - 2f * UiLogoTimeline.LoopSeconds, t.LoopTime(T0 + wall + 9.3f), 1e-3f, "loop time from the clock (9.3 s = 2 loops + 1.3)");
            Assert.AreEqual(UiLogoTimeline.IntroSeconds, t.SpecTime(T0 + wall + 9.3f), 1e-4f, "the loop stands on the intro's last frame");

            // the same answer stepped finely: no accumulation
            var fine = new UiLogoTimeline();
            fine.BeginLoading(T0, false);
            for (float now = T0; now <= T0 + wall + 9.3f; now += 1f / 240f)
            {
                fine.Tick(now);
            }

            Assert.AreEqual(UiLogoPhase.Loop, Phase(fine, T0 + wall + 9.3f));
            Assert.AreEqual(t.LoopTime(T0 + wall + 9.3f), fine.LoopTime(T0 + wall + 9.3f), 1e-4f);
        }

        [Test]
        public void EkBekleme_HicbirHazirlikZamanindaYarimSaniyeyiAsmaz()
        {
            foreach (float speed in new[] { 1f, UiLogoTimeline.IntroSpeed })
            {
                float worst = 0f;
                for (float ready = 0f; ready <= 8f; ready += 0.01f)
                {
                    var t = new UiLogoTimeline();
                    t.BeginLoading(T0, false, speed);
                    t.NotifyReady(T0 + ready);
                    float leave = T0 + ready;
                    while (!t.CanLeave(leave))
                    {
                        leave += 1f / 240f;
                    }

                    float extra = leave - (T0 + ready);
                    worst = Mathf.Max(worst, extra);
                    Assert.LessOrEqual(extra, UiLogoTimeline.MaxExtraHoldSeconds + 1f / 240f, "speed " + speed + " ready " + ready.ToString("F2"));
                    if (ready * speed >= UiLogoTimeline.CanonicalSeconds)
                    {
                        Assert.AreEqual(0f, extra, 1e-4f, "past the canonical frame: no hold (ready " + ready.ToString("F2") + ")");
                    }
                }

                Debug.Log("[DD-TEST] ap035.logo.extraHold speed=" + speed + " worst=" + worst.ToString("F3"));
            }

            // K2 with the 1.2 s minimum display: the canonical frame (1.69 s) arrives inside the 0.5 s window
            var k2 = new UiLogoTimeline();
            k2.BeginLoading(T0, false);
            k2.NotifyReady(T0 + 1.2f);
            Assert.IsTrue(k2.CanLeave(T0 + UiLogoTimeline.CanonicalSeconds / UiLogoTimeline.IntroSpeed + 1e-3f), "the canonical frame lets the load go");
            Assert.Less(UiLogoTimeline.CanonicalSeconds / UiLogoTimeline.IntroSpeed - 1.2f, UiLogoTimeline.MaxExtraHoldSeconds, "1.69 - 1.2 < 0.5");
        }

        [Test]
        public void AzaltilmisHareket_YuklemeStatik_BekletmezKisaSonumle_MenuOynamaz()
        {
            var t = new UiLogoTimeline();
            t.BeginLoading(T0, true);
            Assert.AreEqual(UiLogoPhase.Static, Phase(t, T0));
            Assert.IsTrue(t.CanLeave(T0), "RM: no hold");
            Assert.AreEqual(0f, t.Alpha(T0), 1e-4f, "RM: soft fade-in from 0");
            Assert.AreEqual(1f, t.Alpha(T0 + UiLogoTimeline.ReducedFadeInSeconds), 1e-4f, "RM: in after 0.5 s");
            Assert.AreEqual(UiLogoTimeline.IntroSeconds, t.SpecTime(T0 + 0.2f), 1e-4f, "RM: the canonical pose, still");
            t.BeginOutro(T0 + 2f);
            Assert.AreEqual(UiLogoPhase.FadeOut, Phase(t, T0 + 2f), "RM: a short fade instead of the outro");
            Assert.AreEqual(UiLogoPhase.Hidden, Phase(t, T0 + 2f + UiLogoTimeline.SkipFadeSeconds + 1e-3f));

            var menu = new UiLogoTimeline();
            menu.BeginMenu(T0, true);
            Assert.AreEqual(UiLogoPhase.Hidden, Phase(menu, T0), "RM: the menu intro never plays");
        }

        [Test]
        public void MenuGirisi_En2Saniye_KanonikteSoner_AtlamaKisaSonum()
        {
            Assert.LessOrEqual(UiLogoTimeline.MenuIntroDuration, 2.0f, "K7: ≤ 2.0 s");
            var t = new UiLogoTimeline();
            t.BeginMenu(T0, false);
            float canonical = T0 + UiLogoTimeline.CanonicalSeconds / UiLogoTimeline.IntroSpeed;
            Assert.AreEqual(UiLogoPhase.Intro, Phase(t, canonical - 1e-3f));
            Assert.AreEqual(UiLogoPhase.FadeOut, Phase(t, canonical + 1e-3f), "fades on its own canonical frame");
            Assert.AreEqual(UiLogoTimeline.CanonicalSeconds, t.SpecTime(canonical + 0.1f), 1e-4f, "the fade holds the canonical pose");
            Assert.AreEqual(UiLogoPhase.Hidden, Phase(t, T0 + UiLogoTimeline.MenuIntroDuration + 1e-3f), "gone by 2.0 s");

            var skip = new UiLogoTimeline();
            skip.BeginMenu(T0, false);
            skip.Skip(T0 + 0.4f);
            skip.Skip(T0 + 0.45f);   // twice: harmless
            Assert.AreEqual(UiLogoPhase.FadeOut, Phase(skip, T0 + 0.41f));
            Assert.AreEqual(UiLogoTimeline.CanonicalSeconds, skip.SpecTime(T0 + 0.41f), 1e-4f, "a skip shows the last frame");
            Assert.AreEqual(UiLogoPhase.Hidden, Phase(skip, T0 + 0.4f + UiLogoTimeline.SkipFadeSeconds + 1e-3f));
        }

        [Test]
        public void MuhurOrnekleri_PivotAltKenar648_AraKareDogrusal()
        {
            Assert.IsFalse(UiLogoTimeline.SealPose(Spec(38.9f), out _, out _, out _, out _), "before frame 39: no seal");
            for (int f = 47; f <= 54; f++)
            {
                Assert.IsTrue(UiLogoTimeline.SealPose(Spec(f), out float bottom, out float sx, out float sy, out _));
                Assert.AreEqual(UiLogoTimeline.SealRestBottom, bottom, 0.5f, "frame " + f + ": y + 570 sy = 648 (bottom-centre pivot)");
            }

            UiLogoTimeline.SealPose(Spec(43.5f), out float mid, out _, out _, out _);
            Assert.AreEqual((-470f - 384f) * 0.5f + 570f, mid, 1e-3f, "between samples: linear");
            UiLogoTimeline.SealPose(Spec(60f), out float rest, out float rx, out float ry, out float rg);
            Assert.AreEqual(648f, rest, 1e-3f);
            Assert.AreEqual(1f, rx, 1e-5f);
            Assert.AreEqual(1f, ry, 1e-5f);
            Assert.AreEqual(0f, rg, 1e-5f);

            Assert.AreEqual(new Vector2(2f, -1f), UiLogoTimeline.Shake(Spec(48.2f)), "camera shake frame 48");
            Assert.AreEqual(Vector2.zero, UiLogoTimeline.Shake(Spec(51f)));
            Assert.AreEqual(0f, UiLogoTimeline.RingAlpha(Spec(46f), out _), 1e-5f);
            Assert.AreEqual(UiLogoTimeline.RingPeakAlpha, UiLogoTimeline.RingAlpha(Spec(50.5f), out _), 1e-4f, "ring peak mid-impact");
            Assert.AreEqual(UiLogoTimeline.BlotPeakAlpha, UiLogoTimeline.BlotAlpha(Spec(50.5f), out _), 1e-4f, "blot peak mid-impact");
            Assert.AreEqual(0f, UiLogoTimeline.LoopEnvelope(0f), 1e-6f, "loop seam: 0 at the start");
            Assert.AreEqual(0f, UiLogoTimeline.LoopEnvelope(UiLogoTimeline.LoopSeconds), 1e-6f, "loop seam: 0 at the end");
        }

        [Test]
        public void Cikis_V001_17Kare_KanonikteBaslar()
        {
            var t = new UiLogoTimeline();
            t.BeginLoading(T0, false);
            float open = T0 + 6f;   // in the loop
            t.BeginOutro(open);
            Assert.AreEqual(UiLogoPhase.Outro, Phase(t, open));
            Assert.AreEqual(UiLogoTimeline.IntroSeconds, t.SpecTime(open + 0.1f), 1e-4f, "the pose holds where the outro began");
            Assert.AreEqual(1f, t.Alpha(open + UiLogoTimeline.OutroFadeFrom - 1e-3f), 1e-4f, "frames 1..10: full");
            Assert.AreEqual(UiLogoPhase.Hidden, Phase(t, open + UiLogoTimeline.OutroSeconds + 1e-3f), "gone after 17 frames (0.71 s)");
            Assert.AreEqual(17f / 24f, UiLogoTimeline.OutroSeconds, 1e-5f);
            t.BeginOutro(open + 1f);
            Assert.AreEqual(UiLogoPhase.Hidden, Phase(t, open + 1f), "an outro on a hidden logo does nothing");
        }

        [Test]
        public void Tick_SifirAyirma()
        {
            var t = new UiLogoTimeline();
            t.BeginLoading(T0, false);
            float now = T0;
            long seen = HeapDelta.Min(() =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    now += 1f / 60f;
                    t.Tick(now);
                    t.Alpha(now);
                    float spec = t.SpecTime(now);
                    UiLogoTimeline.SealPose(spec, out _, out _, out _, out _);
                    UiLogoTimeline.RingAlpha(spec, out _);
                    UiLogoTimeline.BlotAlpha(spec, out _);
                    UiLogoTimeline.Shake(spec);
                    UiLogoTimeline.LoopEnvelope(t.LoopTime(now));
                    t.CanLeave(now);
                }
            });
            long control = HeapDelta.PositiveControl();
            Debug.Log("[DD-TEST] ap035.logo.tickAlloc bytes=" + seen + " control=" + control);
            Assert.GreaterOrEqual(control, HeapDelta.PositiveControlBytes, "positive control");
            Assert.AreEqual(0, seen, "2000 ticks allocate nothing");
        }

        /// <summary>
        /// R4 WP-105 (user note 6) · a seal that lands lower (the loading ring) still starts its fall where the spec does, lands
        /// on the impact frame at rest + landing and keeps the spec's own bounce; landing 0 is the canonical pose.
        /// </summary>
        [Test]
        public void Muhur_AsagiIner_DususBasiAyni_CarpmadaHedefte_SekmeGenligiKorunur()
        {
            const float landing = 1000f;
            float Top(float frame)
            {
                UiLogoTimeline.SealPose(Spec(frame), out float bottom, out _, out float sy, out _);
                return bottom - UiLogoTimeline.SealLayerSize * sy;
            }

            for (float f = 30f; f <= 60f; f += 0.5f)
            {
                Assert.AreEqual(Top(f), UiLogoTimeline.LandedTop(Top(f), Spec(f), 0f), 1e-4f, "landing 0: the canonical pose at frame " + f);
            }

            Assert.AreEqual(Top(39f), UiLogoTimeline.LandedTop(Top(39f), Spec(39f), landing), 1e-3f, "the fall starts where the spec starts it");
            Assert.AreEqual(UiLogoTimeline.SealRestTop + landing, UiLogoTimeline.LandedTop(Top(47f), Spec(47f), landing), 1e-3f, "the impact frame lands at rest + landing");
            for (float f = 47f; f <= 60f; f += 0.5f)
            {
                Assert.AreEqual(Top(f) + landing, UiLogoTimeline.LandedTop(Top(f), Spec(f), landing), 1e-3f, "the bounce keeps its amplitude at frame " + f);
            }

            float last = float.MinValue;
            for (float f = 39f; f < 47f; f += 0.25f)
            {
                float top = UiLogoTimeline.LandedTop(Top(f), Spec(f), landing);
                Assert.GreaterOrEqual(top, last - 1e-3f, "the stretched fall only goes down (frame " + f + ")");
                last = top;
            }
        }

        private static float Spec(float frame)
        {
            return (frame - 1f) / UiLogoTimeline.Fps;
        }

        private static UiLogoPhase Phase(UiLogoTimeline t, float now)
        {
            t.Tick(now);
            return t.Phase;
        }
    }
}
