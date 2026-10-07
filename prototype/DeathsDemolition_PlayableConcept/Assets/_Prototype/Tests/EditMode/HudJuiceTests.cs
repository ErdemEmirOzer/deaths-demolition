using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.Feedback;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // the Is.Not.AllocatingGCMemory() extension
using UnityEngine.UI;
using Is = UnityEngine.TestTools.Constraints.Is;   // NUnit's Is is spelled out where used
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-094 HUD v4 juice (R3, C-00 07.10; K/wp-094/HAREKET-HUD.md) · the logic under the HUD's motion, on the motion core's test
    /// clock (<see cref="UiMotion.TestNow"/> + Tick), no scene: the catalogue's tokens on the six steps, every loop under
    /// MaxLoopHz (PulsePeriod 0.92, the final-seconds band no longer × 0.5), the rate gates (punch 120 ms, empty trigger 250
    /// ms, ≤ 10 shot accents a second, the slow-shot numeral), the heartbeat (lub / dub, ≤ 1.1 Hz even from an external
    /// clock, the critical gain withheld while the edges show), the round transition (R2 tiers by letters, the R1 → R3
    /// timeline's silent swap and seal cues, StampLanded "round" on the contact - also when skipped), the pools (chunk 3 with
    /// the newest widened, casing 6, wedge 2 with a same-side re-flare), the per-edge onset limit, the feel budget, the "+N"
    /// merge, the new kit API (Rotate, Flare, Punch, RomanReveal, Eject) and its reduced-motion variants, the
    /// HudFeelSource seam (one adapter, one bridge), and 0 allocations for all of it.
    /// </summary>
    [TestFixture]
    [Category("WP094")]
    public sealed class HudJuiceTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private bool _reduced;
        private float _flash;

        [SetUp]
        public void SetUp()
        {
            _reduced = UiMotion.ReducedMotion;
            _flash = UiTokens.FlashScale;
            UiMotion.ReducedMotion = false;
            UiTokens.FlashScale = 1f;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 100f;
            UiEvents.ResetCounters();
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ResetForTests();
            UiMotion.TestNow = null;
            UiMotion.ReducedMotion = _reduced;
            UiTokens.FlashScale = _flash;
            UiEvents.ResetCounters();
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        private static float Now => UiMotion.TestNow.Value;

        private static void Step(float seconds)
        {
            UiMotion.TestNow += seconds;
            UiMotion.Tick();
        }

        private RectTransform Rect(string name = "R")
        {
            var go = new GameObject(name, typeof(RectTransform));
            _created.Add(go);
            return (RectTransform)go.transform;
        }

        private Image Img(string name = "I")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _created.Add(go);
            return go.GetComponent<Image>();
        }

        private sealed class CueLog : IUiMotionListener
        {
            public readonly List<int> Ids = new List<int>();

            public void OnCue(int cueId)
            {
                Ids.Add(cueId);
            }
        }

        // ------------------------------------------------------------------ tokens and loops

        [Test]
        public void Belirtecler_HareketKatalogu_AltiKademede_MetinTavaniAsilmaz()
        {
            Assert.AreEqual(0.35f, UiMotionTokens.PlusMergeWindow, 1e-6f, "G3 \"+N\" merges in 0.35 s (feel contract §4.9)");
            Assert.AreEqual(UiMotionTokens.Seconds(UiDuration.Emphasis), UiMotionTokens.SealHandoffDelay, 1e-6f, "R2 SealHandoffDelay = Emphasis");
            Assert.AreEqual(0.18f, UiMotionTokens.BeatDubDelay, 1e-6f, "H5 dub +0.18 s: the heartbeat rumble's double beat (feel contract §4.7)");
            Assert.AreEqual(0.16f, UiMotionTokens.DeniedNudgeSeconds, 1e-6f, "the DeniedNudge kit token");
            Assert.AreEqual(UiMotionTokens.PunchMinInterval, HudGoldTarget.PunchMinSeconds, 1e-6f, "one punch interval: the token");
            Assert.AreEqual(1.18f, UiMotionTokens.PunchPeakGlyph, 1e-6f);
            Assert.AreEqual(UiMotionTokens.PunchPeakGlyph, RunHudScreen.GoldPunchCoin, 1e-6f);
            Assert.AreEqual(1.12f, RunHudScreen.GoldPunchText, 1e-6f, "the gold counter pops 1.12 (feel contract §4.9 - past the kit's 1.06, as it names it)");
            Assert.AreEqual(0.12f, RunHudScreen.GoldCounterPopSeconds, 1e-6f);
            Assert.AreEqual(0.25f, RunHudScreen.GoldRollSeconds, 1e-6f, "the count 0.25 s easeOutCubic");
            Assert.LessOrEqual(HudShotCadence.PopScale, UiMotionTokens.TextPopMaxScale + 1e-6f, "the ammo numeral is text: ≤ 1.06");
            Assert.LessOrEqual(UiMotionTokens.HealGlowAlpha, 0.45f + 1e-6f);
            Assert.AreEqual(UiMotionTokens.Seconds(UiDuration.Screen), RunHudScreen.SpecialPickupFillSeconds, 1e-6f, "S2: 320 → Screen 280");
        }

        [Test]
        public void Donguler_MaxLoopHzAltinda_SonSaniyeBandiTamPeriyot()
        {
            Assert.LessOrEqual(1f / UiTokens.PulsePeriod, UiMotionTokens.MaxLoopHz + 1e-6f, "PulsePeriod " + UiTokens.PulsePeriod + " s ≤ 1.1 Hz");
            // The heartbeat is the contract's clock (§4.7: 70 → 110 bpm) - not a loop, so not under MaxLoopHz - and under WCAG's 3 Hz.
            Assert.AreEqual(60f / 70f, HudHeartbeat.PeriodFor(0.30f), 1e-4f, "70 bpm at 30 %");
            Assert.AreEqual(60f / 90f, HudHeartbeat.PeriodFor(0.20f), 1e-4f, "90 bpm at 20 %");
            Assert.AreEqual(60f / 110f, HudHeartbeat.PeriodFor(0.05f), 1e-4f, "110 bpm at and under 10 %");
            Assert.Greater(HudHeartbeat.PeriodFor(0f), 1f / 3f, "under 3 flashes a second");

            // S4 (the motion lead's main-branch finding): the final-seconds band ran at PulsePeriod × 0.5 = 2.2 Hz. No HUD
            // file halves the pulse period again (source lock, comments stripped).
            string root = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime");
            foreach (string file in new[] { "Screens/RunHudScreen.cs", "Screens/RunHudScreen.Ink.cs", "Screens/RunHudScreen.Juice.cs" })
            {
                string code = UiOrnamentCensusTests.StripComments(File.ReadAllText(Path.Combine(root, file)));
                Assert.IsFalse(Regex.IsMatch(code, @"PulsePeriod\s*\*\s*0\.5"), file + ": a pulse at half the period (> MaxLoopHz)");
            }
        }

        // ------------------------------------------------------------------ gates and cadence

        [Test]
        public void HizKapisi_AralikIcindeKapali_SonraAcik()
        {
            var gate = new HudRateGate(UiMotionTokens.PunchMinInterval);
            Assert.IsTrue(gate.Try(Now));
            Assert.IsFalse(gate.Try(Now + 0.05f), "inside 120 ms");
            Assert.IsFalse(gate.Ready(Now + 0.11f));
            Assert.IsTrue(gate.Try(Now + 0.12f), "at 120 ms (frame-stamp slack)");
            gate.Reset();
            Assert.IsTrue(gate.Ready(Now));
        }

        [Test]
        public void AtisKadansi_SaniyedeEnCokOnVurgu_RakamYalnizYavasTekAtista()
        {
            var cadence = new HudShotCadence();
            int accents = 0, pops = 0;
            float t = Now;
            for (int i = 0; i < 24; i++, t += 1f / 12f)   // 12 shots a second, automatic
            {
                HudShotAccent a = cadence.Note(t, false);
                accents += (a & HudShotAccent.Accent) != 0 ? 1 : 0;
                pops += (a & HudShotAccent.PopNumeral) != 0 ? 1 : 0;
            }

            Assert.LessOrEqual(accents, 2 * 10 + 1, "≤ 10 accents a second");
            Assert.AreEqual(12, accents, "faster than 10 / s: every second shot accented");
            Assert.AreEqual(0, pops, "an automatic weapon's count never moves per shot");

            cadence.Reset();
            pops = 0;
            for (int i = 0; i < 4; i++)
            {
                pops += (cadence.Note(Now + 10f + i * 0.3f, true) & HudShotAccent.PopNumeral) != 0 ? 1 : 0;
            }

            Assert.AreEqual(4, pops, "a single-fire weapon at 300 ms: the numeral pops each shot");
            cadence.Reset();
            cadence.Note(Now + 20f, true);
            Assert.AreEqual(HudShotAccent.Accent, cadence.Note(Now + 20.2f, true), "a fast tap (200 ms): an accent, no pop");
        }

        // ------------------------------------------------------------------ heartbeat

        [Test]
        public void KalpAtisi_LubDub_PeriyotIle_DisSaatDe11HzUstuneCikmaz()
        {
            var beat = new HudHeartbeat();
            Assert.AreEqual(HudBeat.None, beat.Tick(Now), "not running");
            beat.Start(Now, 0.13f);
            Assert.AreEqual(HudBeat.None, beat.Tick(Now + 0.1f), "the stamp lands first");
            Assert.AreEqual(HudBeat.Lub, beat.Tick(Now + 0.13f));
            Assert.AreEqual(HudBeat.None, beat.Tick(Now + 0.3f));
            Assert.AreEqual(HudBeat.Dub, beat.Tick(Now + 0.13f + UiMotionTokens.BeatDubDelay), "dub +280 ms");

            var lubs = new List<float>();
            for (float t = Now + 0.5f; t < Now + 10f; t += 1f / 60f)
            {
                if (beat.Tick(t) == HudBeat.Lub)
                {
                    lubs.Add(t);
                }
            }

            for (int i = 1; i < lubs.Count; i++)
            {
                Assert.GreaterOrEqual(lubs[i] - lubs[i - 1], HudHeartbeat.MinExternalPeriod - 0.02f, "lub to lub ≥ 1 / 1.1 Hz");
            }

            Assert.That(lubs.Count, NUnit.Framework.Is.InRange(10, 12), "≈ 9.5 s / 0.857 s (70 bpm before SetRate)");

            // §4.7: the rate follows health - at 10 % the lubs come 0.545 s apart.
            beat.SetRate(0.10f);
            int fast = 0;
            for (float t = Now + 12f; t < Now + 17.5f; t += 1f / 60f)
            {
                fast += beat.Tick(t) == HudBeat.Lub ? 1 : 0;
            }

            Assert.That(fast, NUnit.Framework.Is.InRange(9, 11), "≈ 5.5 s / 0.545 s at 110 bpm");

            // A hitch (a 3 s frame) resumes the rhythm - one lub, not a burst.
            int burst = 0;
            float hitch = Now + 20f;
            for (int i = 0; i < 3; i++)
            {
                burst += beat.Tick(hitch + i * 0.001f) == HudBeat.Lub ? 1 : 0;
            }

            Assert.AreEqual(1, burst, "no catch-up burst after a hitch");

            // S-08's external heartbeat (FeelEvents.HeartBeat): faster than 1.1 Hz is thinned.
            var external = new HudHeartbeat { UsesExternal = true };
            external.Start(Now, 0f);
            int ext = 0;
            for (int i = 0; i < 10; i++)
            {
                ext += external.External(Now + i * 0.25f) == HudBeat.Lub ? 1 : 0;   // a double raise every 0.25 s from outside
            }

            Assert.AreEqual(5, ext, "beats closer than 0.4 s are one beat (150 bpm, past the contract's 110)");
            Assert.AreNotEqual(HudBeat.Lub, external.Tick(Now + 50f), "the own clock plays no lub under an external one (the last dub only)");
            Assert.AreEqual(HudBeat.None, external.Tick(Now + 51f), "nothing more without an external beat");

            beat.Stop();
            Assert.IsFalse(beat.Running);
            Assert.AreEqual(HudBeat.None, beat.Tick(Now + 100f));
        }

        [Test]
        public void KalpAtisi_KritikKazanc_KenarAcikkenYok_TavanVeFlashScale()
        {
            Assert.AreEqual(UiMotionTokens.BeatCriticalGain, HudHeartbeat.Gain(0.10f, false), 1e-6f, "under 15 %: × 1.3");
            Assert.AreEqual(1f, HudHeartbeat.Gain(0.10f, true), 1e-6f, "the damage edges show: no gain (D1 open)");
            Assert.AreEqual(1f, HudHeartbeat.Gain(0.25f, false), 1e-6f, "LOW but over 15 %");
            Assert.AreEqual(UiMotionTokens.BeatLub, HudHeartbeat.Peak(HudBeat.Lub, 1f), 1e-6f);
            Assert.AreEqual(UiMotionTokens.BeatDub, HudHeartbeat.Peak(HudBeat.Dub, 1f), 1e-6f);
            Assert.AreEqual(1.13f, HudHeartbeat.Peak(HudBeat.Lub, UiMotionTokens.BeatCriticalGain), 1e-4f);
            Assert.AreEqual(UiMotionTokens.BeatRimPeak, HudHeartbeat.RimPeak(1f, 1f), 1e-6f);
            Assert.AreEqual(UiMotionTokens.BeatRimRest + 0.4f * 0.4f, HudHeartbeat.RimPeak(1f, 0.4f), 1e-5f, "the rim's lift × FlashScale");
            Assert.LessOrEqual(HudHeartbeat.RimPeak(10f, 1f), 1f);
        }

        // ------------------------------------------------------------------ the round transition

        [Test]
        public void RomaAcilimi_HarfSayisinaGoreKademe_TemasZamani()
        {
            Assert.AreEqual(UiDuration.Base, UiPresets.RomanRevealDuration(RomanNumerals.Length(1)), "I");
            Assert.AreEqual(UiDuration.Base, UiPresets.RomanRevealDuration(RomanNumerals.Length(4)), "IV");
            Assert.AreEqual(UiDuration.Base, UiPresets.RomanRevealDuration(RomanNumerals.Length(9)), "IX");
            Assert.AreEqual(UiDuration.Screen, UiPresets.RomanRevealDuration(RomanNumerals.Length(14)), "XIV");
            Assert.AreEqual(UiDuration.Emphasis, UiPresets.RomanRevealDuration(RomanNumerals.Length(23)), "XXIII");
            Assert.AreEqual(UiDuration.Reveal, UiPresets.RomanRevealDuration(RomanNumerals.Length(38)), "XXXVIII");
            Assert.AreEqual(0.4f + 0.22f, HudRoundTransition.SealSeconds(2), 1e-5f, "R3 starts at the reveal's end");
            Assert.AreEqual(0.4f + 0.6f + 0.09f, HudRoundTransition.ContactSeconds(7), 1e-5f, "the S-08 cue on the contact frame");

            // RomanReveal opens a clip 0 → width on the Brush curve; under reduced motion it is open at once.
            RectTransform clip = Rect("Clip");
            clip.sizeDelta = new Vector2(120f, 40f);
            UiPresets.RomanReveal(clip, 120f, 3);
            Assert.AreEqual(0f, clip.sizeDelta.x, 1e-4f, "closed at the start");
            Step(0.14f);
            Assert.That(clip.sizeDelta.x, NUnit.Framework.Is.InRange(1f, 119f), "opening");
            Step(0.2f);
            Assert.AreEqual(120f, clip.sizeDelta.x, 1e-3f, "open after Screen");
            UiMotion.ReducedMotion = true;
            UiPresets.RomanReveal(clip, 80f, 7);
            Assert.AreEqual(80f, clip.sizeDelta.x, 1e-3f, "RM: open at once");
        }

        [Test]
        public void RaundGecisi_SessizCueVeTemasCuesu_AtlamaTemasaCoker()
        {
            const string round = RunHudScreen.RoundSealCueDetail;
            var log = new CueLog();
            int glyphs = RomanNumerals.Length(4);
            UiTimeline tl = UiMotion.Timeline(7, log);
            tl.Call(HudRoundTransition.HandoffSeconds, HudRoundTransition.CueSwap)
              .Call(HudRoundTransition.SealSeconds(glyphs), HudRoundTransition.CueSeal)
              .Cue(UiEvent.StampLanded, round, HudRoundTransition.ContactSeconds(glyphs), HudRoundTransition.CueContact);
            UiEvents.ResetCounters();

            Step(HudRoundTransition.HandoffSeconds + 0.001f);
            CollectionAssert.AreEqual(new[] { HudRoundTransition.CueSwap }, log.Ids, "the swap at the hand-off");
            Assert.AreEqual(0, UiEvents.RaisedCount, "a Call cue raises nothing");
            Step(0.221f);
            CollectionAssert.AreEqual(new[] { HudRoundTransition.CueSwap, HudRoundTransition.CueSeal }, log.Ids);
            Assert.AreEqual(0, UiEvents.RaisedCount);
            Step(UiMotionTokens.StampInSeconds);
            CollectionAssert.AreEqual(new[] { HudRoundTransition.CueSwap, HudRoundTransition.CueSeal, HudRoundTransition.CueContact }, log.Ids);
            Assert.AreEqual(1, UiEvents.RaisedCount, "one raise");
            Assert.AreEqual(UiEvent.StampLanded, UiEvents.Last);
            Assert.AreEqual(round, UiEvents.LastDetail, "StampLanded \"round\" (S-08)");
            Step(0.01f);
            Assert.IsFalse(tl.IsAlive);

            // Skipped before the hand-off: the cues collapse to the contact - the listener lands the numeral, S-08 still hears it.
            var skip = new CueLog();
            UiTimeline t2 = UiMotion.Timeline(8, skip);
            t2.Call(HudRoundTransition.HandoffSeconds, HudRoundTransition.CueSwap)
              .Call(HudRoundTransition.SealSeconds(glyphs), HudRoundTransition.CueSeal)
              .Cue(UiEvent.StampLanded, round, HudRoundTransition.ContactSeconds(glyphs), HudRoundTransition.CueContact);
            UiEvents.ResetCounters();
            Step(0.05f);
            t2.Complete();
            CollectionAssert.AreEqual(new[] { HudRoundTransition.CueContact }, skip.Ids, "the skip lands on the contact cue");
            Assert.AreEqual(1, UiEvents.RaisedCount);
            Assert.AreEqual(round, UiEvents.LastDetail);
        }

        // ------------------------------------------------------------------ pools, limits, budget

        [Test]
        public void Havuzlar_ParcaDorduncudeEnYeniGenisler_KovanEnEskiyiKullanir()
        {
            var chunks = new HudSlotPool(RunHudScreen.HealthChunkPool);
            bool extended;
            int a = chunks.TakeOrExtendNewest(Now, 0.28f, out extended);
            Assert.IsFalse(extended);
            int b = chunks.TakeOrExtendNewest(Now + 0.01f, 0.28f, out extended);
            int c = chunks.TakeOrExtendNewest(Now + 0.02f, 0.28f, out extended);
            Assert.AreEqual(3, chunks.LiveCount(Now + 0.03f));
            int d = chunks.TakeOrExtendNewest(Now + 0.03f, 0.28f, out extended);
            Assert.IsTrue(extended, "the fourth blow widens");
            Assert.AreEqual(c, d, "the newest chunk");
            Assert.AreNotEqual(a, b);
            Assert.AreEqual(0, chunks.LiveCount(Now + 1f), "all fallen");

            var casings = new HudSlotPool(RunHudScreen.CasingPool);
            int first = casings.Take(Now, 0.22f);
            for (int i = 1; i < RunHudScreen.CasingPool; i++)
            {
                casings.Take(Now + i * 0.01f, 0.22f);
            }

            Assert.AreEqual(first, casings.Take(Now + 0.07f, 0.22f), "a full pool reuses the oldest casing");
            Assert.AreEqual(RunHudScreen.CasingPool, casings.LiveCount(Now + 0.08f), "never more than 6 on screen");
        }

        [Test]
        public void KenarBaslangici_KenarBasinaSaniyedeIki_KenarlarBagimsiz()
        {
            var limit = new HudOnsetLimiter(4, UiMotionTokens.FlashGateOnsetsPerElement);
            Assert.IsTrue(limit.Allow(0, Now));
            Assert.IsTrue(limit.Allow(0, Now + 0.2f));
            Assert.IsFalse(limit.Allow(0, Now + 0.4f), "a third onset on one edge inside a second merges");
            Assert.IsTrue(limit.Allow(1, Now + 0.4f), "another edge is its own element");
            Assert.IsTrue(limit.Allow(0, Now + 1.01f), "a second later");
        }

        [Test]
        public void HisButcesi_UcBelirginTekAtim_SusDusulur_TehlikedeHicYok()
        {
            var budget = new HudFeelBudget();
            Assert.IsTrue(budget.OrnamentAllowed(Now, false));
            Assert.IsFalse(budget.OrnamentAllowed(Now, true), "the danger posture drops ornaments");
            budget.Note(Now, 0.22f);
            budget.Note(Now, 0.28f);
            Assert.IsTrue(budget.OrnamentAllowed(Now + 0.01f, false));
            budget.Note(Now, 0.4f);
            Assert.AreEqual(3, budget.Live(Now + 0.01f));
            Assert.IsFalse(budget.OrnamentAllowed(Now + 0.01f, false), "a fourth prominent one-shot: the ornament is dropped");
            Assert.IsTrue(budget.OrnamentAllowed(Now + 0.3f, false), "two ended");
        }

        [Test]
        public void ArtiN_350msIcindeBirlesir()
        {
            var plus = new HudPlusMerge();
            Assert.AreEqual(5, plus.Add(Now, 5));
            Assert.AreEqual(12, plus.Add(Now + 0.2f, 7), "inside the window: one growing +N");
            Assert.AreEqual(15, plus.Add(Now + 0.5f, 3), "the window restarts on each gain");
            Assert.AreEqual(4, plus.Add(Now + 1.5f, 4), "a new +N after a pause");
        }

        // ------------------------------------------------------------------ kit API

        [Test]
        public void Rotate_EksiAcidanDonmedenGecer_FlareYukselTutDus()
        {
            RectTransform rect = Rect();
            rect.localRotation = Quaternion.Euler(0f, 0f, -3f);
            UiMotion.Rotate(rect, -8f, UiDuration.Fast, UiEase.Linear, UiMotionClass.Decor);
            Step(0.07f);
            float mid = Mathf.DeltaAngle(0f, rect.localEulerAngles.z);
            Assert.That(mid, NUnit.Framework.Is.InRange(-8f, -3f), "−3 → −8 the short way (not through 357)");
            Step(0.1f);
            Assert.AreEqual(-8f, Mathf.DeltaAngle(0f, rect.localEulerAngles.z), 1e-3f);

            Image glow = Img();
            UiPresets.Flare(glow, 0.45f, UiDuration.Fast, 0.14f, UiDuration.Screen);
            Assert.AreEqual(0f, glow.canvasRenderer.GetAlpha(), 1e-4f, "rises from nothing");
            Step(0.15f);
            Assert.AreEqual(0.45f, glow.canvasRenderer.GetAlpha(), 1e-3f, "peak after the rise");
            Step(0.12f);
            Assert.AreEqual(0.45f, glow.canvasRenderer.GetAlpha(), 1e-3f, "holds");
            Step(0.35f);
            Assert.AreEqual(0f, glow.canvasRenderer.GetAlpha(), 1e-3f, "gone after the fall");

            // Light, not motion: the flare plays under reduced motion.
            UiMotion.ReducedMotion = true;
            UiPresets.Flare(glow, 0.45f, UiDuration.Fast, 0.14f, UiDuration.Screen);
            Step(0.15f);
            Assert.AreEqual(0.45f, glow.canvasRenderer.GetAlpha(), 1e-3f, "RM: the flare still shows");
        }

        [Test]
        public void Punch_MetinTavani_RmDeOlcekYok_EjectSonDurum()
        {
            RectTransform coin = Rect("Coin");
            RectTransform text = Rect("Text");
            UiPresets.Punch(coin, UiMotionTokens.PunchPeakGlyph);
            UiPresets.Punch(text, UiMotionTokens.PunchPeakGlyph, true);
            Step(0f);   // the core applies on its next tick (the frame the punch was asked on)
            Assert.AreEqual(1.18f, coin.localScale.x, 1e-4f, "a glyph jumps to its peak");
            Assert.AreEqual(UiMotionTokens.TextPopMaxScale, text.localScale.x, 1e-4f, "text never past 1.06");
            Step(0.23f);
            Assert.AreEqual(1f, coin.localScale.x, 1e-4f, "settled over Base");

            UiMotion.ReducedMotion = true;
            UiPresets.Punch(coin, UiMotionTokens.PunchPeakGlyph);
            Assert.AreEqual(1f, coin.localScale.x, 1e-4f, "RM: no scale (Decor lands at rest)");
            UiMotion.ReducedMotion = false;

            RectTransform arc = Rect("Arc");
            Image body = Img("Body");
            body.rectTransform.SetParent(arc, false);
            UiPresets.Eject(arc, new Vector2(40f, 6f), 10f, body);
            Assert.AreEqual(1f, body.canvasRenderer.GetAlpha(), 1e-4f);
            Step(0.07f);
            Assert.Greater(arc.anchoredPosition.y + body.rectTransform.anchoredPosition.y, 6f + 8f, "rising toward the apex");
            Step(0.16f);
            Assert.AreEqual(new Vector2(50f, 6f + UiPresets.EjectRisePx), arc.anchoredPosition);
            Assert.AreEqual(-UiPresets.EjectFallPx, body.rectTransform.anchoredPosition.y, 1e-3f);
            Assert.AreEqual(-90f, Mathf.DeltaAngle(0f, body.rectTransform.localEulerAngles.z), 1e-2f, "a quarter turn");
            Assert.AreEqual(0f, body.canvasRenderer.GetAlpha(), 1e-4f, "faded");
        }

        // ------------------------------------------------------------------ D2 wedges

        [Test]
        public void Kamalar_IkiYonIkiKama_AyniYonTekKama()
        {
            RectTransform layer = Rect("Layer");
            var arcs = new HudPlayerArcs(layer);
            Assert.AreSame(UiSprites.ArcSoft60, arcs.DamageWedge(1).sprite, "both wedges are the soft arc");
            arcs.Flash(Vector3.right);
            arcs.Flash(Vector3.left);
            Assert.AreEqual(2, arcs.LiveWedges(Now), "two attackers, two wedges (the second no longer turns the first away)");
            Assert.AreEqual(0f, arcs.DamageWedge(0).canvasRenderer.GetAlpha(), 1e-4f, "§4.6: the wedge rises from 0");
            arcs.Tick(Now + UiTokens.DamageEdgeRiseSeconds + 0.01f);
            Assert.Greater(arcs.DamageWedge(0).canvasRenderer.GetAlpha(), 0.5f);
            Assert.Greater(arcs.DamageWedge(1).canvasRenderer.GetAlpha(), 0.5f);
            float first = Mathf.DeltaAngle(0f, arcs.DamageWedge(0).rectTransform.localEulerAngles.z);
            float second = Mathf.DeltaAngle(0f, arcs.DamageWedge(1).rectTransform.localEulerAngles.z);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(first, second)), 170f, "they face opposite sides");

            arcs.Tick(Now + HudDamageEnvelope.Life + 0.01f);
            Assert.AreEqual(0f, arcs.DamageWedge(0).canvasRenderer.GetAlpha(), 1e-4f, "gone after 0.6 s");
            Step(1f);
            Assert.AreEqual(0, arcs.LiveWedges(Now));
            arcs.Flash(Vector3.right);
            arcs.Flash((Vector3.right + Vector3.up * 0.2f).normalized);
            Assert.AreEqual(1, arcs.LiveWedges(Now), "a hit from the same side re-flares that wedge");
            Assert.Greater(arcs.DamageWedge(0).canvasRenderer.GetAlpha(), 0.5f, "a re-flare starts past its rise - no dip");

            // §4.6: at most four arcs - a fifth side takes the oldest.
            Step(1f);
            arcs.Flash(Vector3.right);
            arcs.Flash(Vector3.left);
            arcs.Flash(Vector3.up);
            arcs.Flash(Vector3.down);
            Assert.AreEqual(HudPlayerArcs.DamageWedges, arcs.LiveWedges(Now), "four sides, four wedges");
            arcs.Flash((Vector3.right + Vector3.up).normalized);
            Assert.AreEqual(4, arcs.LiveWedges(Now), "never more than four");
        }

        [Test]
        public void HasarZarfi_Giris005_Kalis025_Cikis03InQuad()
        {
            Assert.AreEqual(0.05f, UiTokens.DamageEdgeRiseSeconds, 1e-6f);
            Assert.AreEqual(0.25f, UiTokens.DamageEdgeHoldSeconds, 1e-6f);
            Assert.AreEqual(0.3f, UiTokens.DamageEdgeDecaySeconds, 1e-6f);
            Assert.AreEqual(0.6f, HudDamageEnvelope.Life, 1e-5f);
            Assert.AreEqual(0f, HudDamageEnvelope.At(0f), 1e-6f);
            Assert.AreEqual(0.5f, HudDamageEnvelope.At(0.025f), 1e-4f, "half way up the rise");
            Assert.AreEqual(1f, HudDamageEnvelope.At(0.2f), 1e-6f, "held");
            Assert.AreEqual(1f - 0.25f, HudDamageEnvelope.At(0.3f + 0.15f), 1e-4f, "easeInQuad: a quarter gone half way out");
            Assert.AreEqual(0f, HudDamageEnvelope.At(0.61f), 1e-6f);
            Assert.AreEqual(0.05f, HudDamageEnvelope.RestartAge(0.4f), 1e-6f, "a lit edge restarts past its rise");
        }

        [Test]
        public void SozlesmePopu_OutBack_AsarVeOturur_RmdeYok()
        {
            Assert.AreEqual(0f, UiEaseCurve.OutBack(0f), 1e-5f);
            Assert.AreEqual(1f, UiEaseCurve.OutBack(1f), 1e-5f);
            float peak = 0f;
            for (int i = 1; i < 100; i++)
            {
                peak = Mathf.Max(peak, UiEaseCurve.OutBack(i / 100f));
            }

            Assert.Greater(peak, 1.05f, "easeOutBack passes its end (≈ 1.10)");
            Assert.Less(peak, 1.12f);

            RectTransform reticle = Rect("Reticle");
            UiMotion.PopSeconds(reticle, 1.22f, 1f, 0.12f);
            Step(0f);
            Assert.AreEqual(1.22f, reticle.localScale.x, 1e-4f, "the pop's peak");
            Step(0.07f);
            Assert.Less(reticle.localScale.x, 1f, "easeOutBack dips past the rest on the way back");
            Step(0.06f);
            Assert.AreEqual(1f, reticle.localScale.x, 1e-4f, "at rest after 0.12 s");

            UiMotion.ReducedMotion = true;
            UiMotion.PopSeconds(reticle, 1.3f, 1f, 0.15f);
            Assert.AreEqual(1f, reticle.localScale.x, 1e-4f, "RM: no pop");
            UiMotion.ReducedMotion = false;

            // §4.2: a critical number 1.3 → 1 easeOutBack; a plain one 1.15 → 1 Settle.
            Assert.AreEqual(1.3f, UiWorldNumbers.PopScale(true, 0f), 1e-4f);
            Assert.Less(UiWorldNumbers.PopScale(true, 0.08f), 1f, "the crit's number passes 1");
            Assert.AreEqual(1f, UiWorldNumbers.PopScale(true, 0.12f), 1e-4f);
            Assert.AreEqual(1.15f, UiWorldNumbers.PopScale(false, 0f), 1e-4f);
            Assert.GreaterOrEqual(UiWorldNumbers.PopScale(false, 0.05f), 1f, "a plain number never passes 1");
        }

        [Test]
        public void AfisZarflari_RaundBasi_SonZombi_Varsayilan()
        {
            HudBannerEnvelope start = HudBannerEnvelope.RoundStart;
            Assert.AreEqual(0.35f, start.Wipe, 1e-6f, "§4.10 drawn over 0.35 s");
            Assert.AreEqual(0.9f, start.Hold, 1e-6f);
            Assert.AreEqual(0.3f, start.Fade, 1e-6f);
            Assert.AreEqual(1.55f, start.Total, 1e-5f);
            HudBannerEnvelope clear = HudBannerEnvelope.RoundClear;
            Assert.AreEqual(0.15f, clear.Delay, 1e-6f, "§4.11 starts at 0.15 s");
            Assert.AreEqual(0.35f, clear.Wipe, 1e-6f);
            Assert.AreEqual(0.8f, clear.Hold, 1e-6f);
            Assert.AreEqual(0.3f, clear.Fade, 1e-6f);
            Assert.AreEqual(HudBanner.Seconds, HudBannerEnvelope.Default.Total, 1e-5f, "E13's banners keep their 2.4 s");

            // The envelope on a live banner: nothing during the delay, the words open, the life is the envelope's.
            RectTransform host = Rect("Host");
            host.sizeDelta = new Vector2(1920f, 1080f);
            var banner = new HudBanner(host, 0f, true, "ClearBannerRoot", "ClearBanner");
            float life = banner.Show("ROUND IV CLEAR", Now, clear);
            Assert.AreEqual(clear.Total, life, 1e-5f);
            Assert.AreEqual(0f, banner.Clip.sizeDelta.x, 1e-3f, "closed at the start");
            Step(0.1f);
            Assert.AreEqual(0f, banner.Clip.sizeDelta.x, 1e-3f, "still closed in the 0.15 s delay");
            Step(0.45f);
            Assert.Greater(banner.Clip.sizeDelta.x, 100f, "open after the wipe");
            Assert.AreEqual("ClearBanner", banner.Text.name);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), banner.Root.anchorMin, "centred");
        }

        // ------------------------------------------------------------------ the HudFeelSource seam

        [Test]
        public void HisKaynagi_TekAdaptor_TekKopru_SonDinleyicideKalkar()
        {
            var info = new FeelPlayerDamage(Vector3.zero, Vector3.left, 30f, 100f, 70f, 100f, false, true);
            HudDamage hud = HudFeelSource.FromFeel(info);
            Assert.AreEqual(Vector3.left, hud.HitDirection, "FeelEvents' Direction (attacker → player), never WorldPoint − player");
            Assert.AreEqual(30f, hud.AppliedAmount);
            Assert.AreEqual(1f, hud.NormalizedBefore, 1e-6f);
            Assert.AreEqual(0.7f, hud.NormalizedAfter, 1e-6f);
            Assert.AreEqual(0.3f, hud.Fraction, 1e-6f);
            Assert.IsTrue(hud.Heavy, "≥ 25 % of max");
            Assert.AreEqual(HudDamageKind.Unknown, hud.Kind, "FeelEvents carries no kind yet");

            // 08.10: the bridge sits on FeelEvents.PlayerDamaged - and no longer on PlayerHealth.AnyDamaged (no double trigger).
            FieldInfo field = typeof(FeelEvents).GetField("PlayerDamaged", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(field, "FeelEvents.PlayerDamaged backing field");
            FieldInfo legacy = typeof(DD.Prototype.PlayerSystem.PlayerHealth).GetField("AnyDamaged", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            int legacyBefore = Count(legacy);
            int before = Count(field);
            var heard = new List<HudDamage>();
            Action<HudDamage> a = heard.Add;
            Action<HudDamage> b = d => { };
            HudFeelSource.PlayerDamaged += a;
            HudFeelSource.PlayerDamaged += b;
            try
            {
                Assert.AreEqual(before + 1, Count(field), "two HUD listeners, ONE bridge on the source");
                Assert.AreEqual(legacyBefore, Count(legacy), "nothing of the HUD's on PlayerHealth.AnyDamaged");
                ((Action<FeelPlayerDamage>)field.GetValue(null)).Invoke(info);
                Assert.AreEqual(1, heard.Count, "the hit arrives once, as the HUD's payload");
                Assert.AreEqual(0.3f, heard[0].Fraction, 1e-6f);
            }
            finally
            {
                HudFeelSource.PlayerDamaged -= a;
                HudFeelSource.PlayerDamaged -= b;
            }

            Assert.AreEqual(before, Count(field), "the last listener gone: the bridge is gone (the menu carries none)");
            Assert.IsFalse(HudFeelSource.HasListeners);
        }

        private static int Count(FieldInfo field)
        {
            var d = field.GetValue(null) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        // ------------------------------------------------------------------ 0 B

        [Test]
        public void SifirAyirma_Kurallar_Havuzlar_ZamanCizelgesi_Presetler()
        {
            var gate = new HudRateGate(0.12f);
            var cadence = new HudShotCadence();
            var beat = new HudHeartbeat();
            var chunks = new HudSlotPool(3);
            var casings = new HudSlotPool(6);
            var limit = new HudOnsetLimiter(4, 2);
            var budget = new HudFeelBudget();
            var plus = new HudPlusMerge();
            var log = new CueLog();
            RectTransform clip = Rect("Clip");
            RectTransform coin = Rect("Coin");
            RectTransform arc = Rect("Arc");
            Image body = Img("Body");
            Image glow = Img("Glow");
            body.rectTransform.SetParent(arc, false);
            beat.Start(Now, 0f);

            Action cycle = () =>
            {
                float t = UiMotion.TestNow.Value;
                bool extended;
                gate.Try(t);
                cadence.Note(t, true);
                beat.Tick(t);
                chunks.TakeOrExtendNewest(t, 0.28f, out extended);
                casings.Take(t, 0.22f);
                limit.Allow(1, t);
                budget.Note(t, 0.2f);
                budget.OrnamentAllowed(t, false);
                plus.Add(t, 3);
                HudRoundTransition.ContactSeconds(RomanNumerals.Length(23));
                UiTimeline tl = UiMotion.Timeline(9, log);
                tl.Call(0.01f, 1).Cue(UiEvent.StampLanded, RunHudScreen.RoundSealCueDetail, 0.02f, 3);
                UiPresets.RomanReveal(clip, 90f, 5);
                UiPresets.Punch(coin, 1.18f);
                UiPresets.Flare(glow, 0.45f, UiDuration.Fast, 0.14f, UiDuration.Screen);
                UiPresets.Eject(arc, Vector2.zero, 10f, body);
                for (int f = 0; f < 10; f++)
                {
                    Step(0.016f);
                }
            };

            cycle();   // warm-up: first calls, JIT, the timeline slot's arrays
            log.Ids.Capacity = 4096;

            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, Is.AllocatingGCMemory(), "positive control: the recorder sees allocations");
            Assert.That(() =>
            {
                for (int n = 0; n < 40; n++) cycle();
            }, Is.Not.AllocatingGCMemory(), "the juice's rules, pools, timeline cues and presets allocate nothing");
            Debug.Log("[DD-TEST] wp094.juiceAlloc threadAlloc=none (40 cycles) control=" + (bait != null ? bait.Length : 0) + " B");
        }
    }
}
