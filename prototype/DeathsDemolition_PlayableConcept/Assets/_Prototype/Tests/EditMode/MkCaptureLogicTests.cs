using System.Collections.Generic;
using System.Linq;
using DD.Prototype.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-110 (S-01 mk-capture) · the pure logic of MkCaptureAgent: the command line, the highlight director (when a clip
    /// starts, how far it reaches back, how events extend it, when it ends) and the preroll ring. No Player, no scene, no files.
    /// </summary>
    [TestFixture]
    [Category("WP110")]
    public sealed class MkCaptureLogicTests
    {
        private static string[] Argv(params string[] tail)
        {
            var list = new List<string> { "DeathsDemolition" };
            list.AddRange(tail);
            return list.ToArray();
        }

        private static readonly string Out = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mk-out");

        // ---------------------------------------------------------------- options

        [Test]
        public void Secenekler_BayrakYoksaEtkisiz_SarkikBayrakEtkisiz()
        {
            string error;
            Assert.IsNull(MkCaptureOptions.Parse(Argv("-ddQaBot", "/x"), out error)); Assert.IsNull(error);
            Assert.IsFalse(MkCaptureOptions.IsRequested(Argv("-ddMkCapture")));
            Assert.IsFalse(MkCaptureOptions.IsRequested(Argv("-ddMkCapture", "-ddMkFps", "30")));
            Assert.IsFalse(MkCaptureOptions.IsRequested(null));
        }

        [Test]
        public void Secenekler_Varsayilanlar_DikeyAnaKareVeOyununYatayAcisi()
        {
            string error;
            MkCaptureOptions o = MkCaptureOptions.Parse(Argv("-ddMkCapture", Out), out error);
            Assert.IsNull(error);
            Assert.AreEqual(1080, o.Width); Assert.AreEqual(1920, o.Height); Assert.AreEqual(30, o.Fps);
            Assert.AreEqual(68.6f, o.Fov, 1e-4f); Assert.IsTrue(o.HudOff); Assert.IsFalse(o.HudOn);
            Assert.AreEqual(15f, o.MinClip); Assert.AreEqual(36f, o.MaxClip);
            Assert.AreEqual(24f, o.Preroll, "S-11 §9-4: ~24 s preroll + 8 s postroll"); Assert.AreEqual(8f, o.Postroll);
            Assert.AreEqual('O', o.Camera); Assert.AreEqual(6, o.MultiKillMin); Assert.IsFalse(o.MusicOff); Assert.AreEqual(3f, o.Look);
            Assert.IsTrue(o.FrameBand, "dry-1: the hero band is on by default");
            Assert.AreEqual(640, o.RingMb, "gt S4: ≈ 18 s of hud-both frame pairs at 30 fps"); Assert.AreEqual(5f, o.RawGb, "gt M1: raw ≤ 6 GB (S-11 §9-13)");
            Assert.AreEqual(2.5f, MkCaptureOptions.Parse(Argv("-ddMkCapture", Out, "-ddMkRawGB", "2.5"), out error).RawGb);
            Assert.IsNull(MkCaptureOptions.Parse(Argv("-ddMkCapture", Out, "-ddMkRawGB", "0"), out error)); Assert.IsNotNull(error);
            Assert.IsFalse(MkCaptureOptions.Parse(Argv("-ddMkCapture", Out, "-ddMkFrameBand", "0"), out error).FrameBand);
            Assert.IsNull(MkCaptureOptions.Parse(Argv("-ddMkCapture", Out, "-ddMkFrameBand", "yes"), out error)); Assert.IsNotNull(error);
            Assert.IsFalse(o.HudSafe); Assert.IsTrue(MkCaptureOptions.Parse(Argv("-ddMkCapture", Out, "-ddMkHudSafe", "1"), out error).HudSafe);
            Assert.IsNull(MkCaptureOptions.Parse(Argv("-ddMkCapture", Out, "-ddMkHudSafe", "2"), out error));
            // 68.6° vertical on a 9:16 master = the game's 42° vertical as its horizontal angle (so the 1:1 crop frames like the game)
            double h = 2 * System.Math.Atan(System.Math.Tan(o.Fov * System.Math.PI / 360) * o.Width / o.Height) * 180 / System.Math.PI;
            Assert.AreEqual(42.0, h, 0.2);
        }

        [Test]
        public void Secenekler_Degerler_OkunurSinirdisiHata()
        {
            string error;
            MkCaptureOptions o = MkCaptureOptions.Parse(Argv("-ddMkCapture", Out, "-ddMkSize", "1440x2560", "-ddMkFps", "60", "-ddMkFov", "0",
                "-ddMkPreroll", "12", "-ddMkPostroll", "6", "-ddMkClips", "20", "-ddMkSeconds", "1800", "-ddMkThreshold", "7.5", "-ddMkStills", "3",
                "-ddMkHud", "both", "-ddMkCamera", "Y", "-ddMkMusicOff", "1", "-ddMkMultiKillMin", "4", "-ddMkLookMetres", "0", "-ddMkRingMB", "200"), out error);
            Assert.IsNull(error, error);
            Assert.AreEqual(1440, o.Width); Assert.AreEqual(2560, o.Height); Assert.AreEqual(60, o.Fps); Assert.AreEqual(0f, o.Fov);
            Assert.AreEqual(12f, o.Preroll); Assert.AreEqual(6f, o.Postroll); Assert.AreEqual(20, o.Clips); Assert.AreEqual(1800, o.Seconds);
            Assert.AreEqual(7.5f, o.Threshold); Assert.AreEqual(3, o.Stills);
            Assert.IsTrue(o.HudOff && o.HudOn, "both = the two streams"); Assert.AreEqual('Y', o.Camera); Assert.IsTrue(o.MusicOff);
            Assert.AreEqual(4, o.MultiKillMin); Assert.AreEqual(0f, o.Look); Assert.AreEqual(200, o.RingMb);
            o = MkCaptureOptions.Parse(Argv("-ddMkCapture", Out, "-ddMkHud", "on"), out error);
            Assert.IsTrue(o.HudOn && !o.HudOff, "on = the HUD stream only");
            Assert.AreEqual(0.6f, MkCaptureOptions.DistanceScale('Y')); Assert.AreEqual(0.8f, MkCaptureOptions.DistanceScale('O'));
            Assert.AreEqual(1f, MkCaptureOptions.DistanceScale('G'));

            foreach (string[] bad in new[]
                     {
                         new[] { "-ddMkSize", "1081x1920" }, new[] { "-ddMkSize", "big" }, new[] { "-ddMkSize", "100x100" },
                         new[] { "-ddMkFps", "5" }, new[] { "-ddMkFov", "10" }, new[] { "-ddMkFov", "x" }, new[] { "-ddMkHud", "yes" },
                         new[] { "-ddMkCamera", "W" }, new[] { "-ddMkMusicOff", "2" }, new[] { "-ddMkPreroll", "31" }, new[] { "-ddMkRingMB", "8" },
                         new[] { "-ddMkMinClipSeconds", "40", "-ddMkMaxClipSeconds", "30" }, new[] { "-ddMkStills", "4" }, new[] { "-ddMkSeconds", "10" }
                     })
            {
                var args = new List<string> { "-ddMkCapture", Out };
                args.AddRange(bad);
                Assert.IsNull(MkCaptureOptions.Parse(Argv(args.ToArray()), out error), string.Join(" ", bad));
                Assert.IsNotNull(error, string.Join(" ", bad));
            }

            Assert.IsNull(MkCaptureOptions.Parse(Argv("-ddMkCapture", "relative/dir"), out error));
            StringAssert.Contains("absolute", error);
        }

        // ---------------------------------------------------------------- director

        private static MkHighlightDirector Director(int clips = 3) => new MkHighlightDirector(10f, 10f, 8f, 15f, 30f, clips);

        [Test]
        public void Yonetmen_EsikAltindaKlipYok_IsiPencereyleSoner()
        {
            MkHighlightDirector d = Director();
            d.Add(1.0, 3f); d.Add(2.0, 3f); d.Add(3.0, 3f);
            Assert.AreEqual(MkDecision.None, d.Tick(3.0, 0.0));
            Assert.AreEqual(9f, d.Heat(3.5), 1e-4f);
            Assert.AreEqual(6f, d.Heat(5.5), 1e-4f, "the 1.0 s event leaves the 4 s window");
            Assert.AreEqual(0f, d.Heat(20.0), 1e-4f);
            Assert.IsFalse(d.InClip);
        }

        [Test]
        public void Yonetmen_EsikteBaslar_OnKayitTamponlaVeEnAz15sIleSinirli()
        {
            MkHighlightDirector d = Director();
            d.Add(30.0, 6f); d.Add(30.5, 6f);
            Assert.AreEqual(MkDecision.Start, d.Tick(30.5, 0.0));
            Assert.AreEqual(20.5, d.ClipStart, 1e-6, "trigger − preroll");
            Assert.AreEqual(38.5, d.ClipEnd, 1e-6, "trigger + postroll (≥ start + 15 already)");
            Assert.AreEqual(30.5, d.TriggerT, 1e-6);

            MkHighlightDirector e = Director();
            e.Add(30.0, 12f);
            Assert.AreEqual(MkDecision.Start, e.Tick(30.0, 26.0));
            Assert.AreEqual(26.0, e.ClipStart, 1e-6, "no frame older than the ring holds");
            Assert.AreEqual(41.0, e.ClipEnd, 1e-6, "start + minClip when the postroll alone is shorter");

            MkHighlightDirector f = Director();
            f.Add(5.0, 12f);
            Assert.AreEqual(MkDecision.Start, f.Tick(5.0, double.PositiveInfinity));
            Assert.AreEqual(5.0, f.ClipStart, 1e-6, "empty ring: the clip starts now");
        }

        [Test]
        public void Yonetmen_OlaylarSonuIter_30snTavan_SonundaBiter()
        {
            MkHighlightDirector d = Director();
            d.Add(30.0, 12f);
            Assert.AreEqual(MkDecision.Start, d.Tick(30.0, 0.0));   // start 20, end 38
            d.Add(35.0, 1f);
            Assert.AreEqual(38.0, d.ClipEnd, 1e-6, "a lone kill (weight < 2) does not extend");
            d.Add(36.0, 3f);
            Assert.AreEqual(44.0, d.ClipEnd, 1e-6, "an explosion pushes the end to its time + postroll");
            d.Add(45.0, 3f);
            Assert.AreEqual(50.0, d.ClipEnd, 1e-6, "never past start + maxClip (20 + 30)");
            Assert.AreEqual(MkDecision.None, d.Tick(49.9, 0.0));
            Assert.AreEqual(MkDecision.End, d.Tick(50.0, 0.0));
            Assert.IsFalse(d.InClip);
            Assert.AreEqual(50.0, d.LastClipEnd, 1e-6);
        }

        [Test]
        public void Yonetmen_SogumaVeSonrakiKlipOncekininSonundanBaslar_KotaDolunca_Tukenir()
        {
            MkHighlightDirector d = Director(2);
            d.Add(30.0, 12f);
            d.Tick(30.0, 0.0);
            Assert.AreEqual(MkDecision.End, d.Tick(38.0, 0.0));
            d.Add(39.0, 12f);
            Assert.AreEqual(MkDecision.None, d.Tick(39.0, 0.0), "cool-down 2 s after the last clip");
            Assert.AreEqual(MkDecision.Start, d.Tick(40.1, 0.0));
            Assert.AreEqual(38.0, d.ClipStart, 1e-6, "never overlaps the previous clip");
            Assert.AreEqual(2, d.Clips);
            Assert.AreEqual(MkDecision.End, d.Tick(60.0, 0.0));
            Assert.IsTrue(d.Exhausted);
            d.Add(70.0, 50f);
            Assert.AreEqual(MkDecision.None, d.Tick(70.0, 0.0), "no clips left");
        }

        [Test]
        public void Yonetmen_ZorlaBitir_TepeIzlenir()
        {
            MkHighlightDirector d = Director();
            d.Add(30.0, 12f);
            d.Tick(30.0, 0.0);
            d.Add(31.0, 9f);
            d.Tick(31.0, 0.0);
            Assert.AreEqual(31.0, d.PeakT, 1e-6); Assert.AreEqual(21f, d.PeakHeat, 1e-4f);
            d.ForceEnd(33.0);
            Assert.IsFalse(d.InClip); Assert.AreEqual(33.0, d.ClipEnd, 1e-6); Assert.AreEqual(33.0, d.LastClipEnd, 1e-6);
        }

        [Test]
        public void Agirliklar_PatlamaVeCokluOldurmeTekOldurmedenAgir_UltraEnAgir()
        {
            float kill = MkWeights.Of(MkEventKind.Kill, 0, 0);
            Assert.Less(kill, MkWeights.Of(MkEventKind.Explosion, 0, 0));
            Assert.Less(MkWeights.Of(MkEventKind.MultiKill, 2, 0), MkWeights.Of(MkEventKind.MultiKill, 5, 0));
            Assert.Less(kill, MkWeights.Of(MkEventKind.Kill, 0, 3));
            Assert.Less(MkWeights.Of(MkEventKind.EliteNear, 0, 2), MkWeights.Of(MkEventKind.UltraNear, 0, 3));
            // S-11 §9-1: a burst below the marketing minimum weighs like loose kills; at it, a beat
            Assert.Less(MkWeights.Of(MkEventKind.MultiKill, 5, 0, 6), MkHighlightDirector.ExtendWeight * 2f);
            Assert.GreaterOrEqual(MkWeights.Of(MkEventKind.MultiKill, 6, 0, 6), 9f);
            Assert.Greater(MkWeights.Of(MkEventKind.RocketBurst, 0, 0), MkWeights.Of(MkEventKind.Explosion, 0, 0));
            Assert.AreEqual(MkWeights.KindCount, System.Enum.GetValues(typeof(MkEventKind)).Length);
            Assert.Less(MkWeights.Of(MkEventKind.Kill, 0, 0), MkHighlightDirector.ExtendWeight, "a lone kill never extends a clip");
        }

        // ---------------------------------------------------------------- ring

        [Test]
        public void Halka_KapasitedeEnEskiDuser_TakeFromSiraliVeKalaniTutar()
        {
            var r = new MkFrameRing(4);
            Assert.AreEqual(double.PositiveInfinity, r.OldestT);
            for (int i = 0; i < 6; i++) r.Push(i, i * 0.5, new byte[] { (byte)i });
            Assert.AreEqual(4, r.Count);
            Assert.AreEqual(1.0, r.OldestT, 1e-9, "frames 0 and 1 dropped");

            var s = new MkFrameRing(8);
            s.Push(3, 1.5, null); s.Push(1, 0.5, null); s.Push(2, 1.0, null); s.Push(0, 0.0, null);   // completion order ≠ grab order
            List<MkFrameRing.Frame> taken = s.TakeFrom(0.9);
            CollectionAssert.AreEqual(new long[] { 2, 3 }, taken.Select(f => f.Index).ToArray());
            Assert.AreEqual(2, s.Count, "the older frames stay");
            Assert.AreEqual(0.0, s.OldestT, 1e-9);
        }

        [Test]
        public void Halka_BaytButcesi_EnEskiDuser_HudKaresiDeSayilir()
        {
            var r = new MkFrameRing(100, 1000);
            for (int i = 0; i < 6; i++) r.Push(i, i, new byte[200], new byte[100]);   // 300 B per frame
            Assert.AreEqual(3, r.Count, "1000 B hold three 300 B frames");
            Assert.AreEqual(900, r.Bytes); Assert.AreEqual(3, r.BudgetEvictions); Assert.AreEqual(900, r.PeakBytes);
            Assert.AreEqual(3.0, r.OldestT, 1e-9);
            List<MkFrameRing.Frame> taken = r.TakeFrom(4.0);
            Assert.AreEqual(2, taken.Count); Assert.AreEqual(300, r.Bytes, "what stays is still counted");
            Assert.IsNotNull(taken[0].JpegHud);
        }

        // ---------------------------------------------------------------- framing (dry-1 11.10)

        [Test]
        public void Kadraj_BantIcindeDokunmaz_DisindaKenaraCeker()
        {
            float tx, ty;
            Assert.IsFalse(MkFraming.BandTarget(0.5f, 0.4f, out tx, out ty));
            Assert.AreEqual((0.5f, 0.4f), (tx, ty));
            Assert.IsTrue(MkFraming.BandTarget(0.07f, 0.3f, out tx, out ty), "dry-1: the hero at 7 % of the width");
            Assert.AreEqual((MkFraming.X0, 0.3f), (tx, ty));
            Assert.IsTrue(MkFraming.BandTarget(0.9f, 0.95f, out tx, out ty));
            Assert.AreEqual((MkFraming.X1, MkFraming.Y1), (tx, ty));
        }

        [Test]
        public void HudGuvenliAlan_ReelsDikdortgeni_1080x1920Piksel()
        {
            Rect r = MkFraming.HudSafe;   // S-11 11.10: x 0–960, y 250–1520 from the top of 1080×1920
            Assert.AreEqual(0f, r.xMin * 1080f, 0.01f); Assert.AreEqual(960f, r.xMax * 1080f, 0.01f);
            Assert.AreEqual(1920f - 1520f, r.yMin * 1920f, 0.01f, "viewport origin is bottom-left");
            Assert.AreEqual(1920f - 250f, r.yMax * 1920f, 0.01f);
        }

        [Test]
        public void Kadraj_BakisPayiUzunEksende_YanaEnCok1m()
        {
            Vector3 v = MkFraming.Look(new Vector3(6f, 3f, 8f), 3f);
            Assert.AreEqual(0f, v.y); Assert.LessOrEqual(Mathf.Abs(v.x), MkFraming.LookSideMax + 1e-5f);
            Assert.AreEqual(2.4f, v.z, 1e-4f, "8 m north of a 10 m crowd vector, clamped to 3 m → 2.4 m");
            Assert.AreEqual(Vector3.zero, MkFraming.Look(Vector3.zero, 3f));
            Assert.AreEqual(-1f, MkFraming.Look(new Vector3(-20f, 0f, 0f), 3f).x, 1e-5f);
        }

        // ---------------------------------------------------------------- gt M1 / S3 (11.10)

        [Test]
        public void HamButce_SinirdaAsilir()
        {
            var b = new MkRawBudget(0.000001f);   // ≈ 1073 B
            Assert.IsFalse(b.Exceeded);
            b.Add(1000); Assert.IsFalse(b.Exceeded);
            b.Add(100); Assert.IsTrue(b.Exceeded, "a session stops at its raw budget");
            Assert.AreEqual(1100, b.Used);
        }

        [Test]
        public void OlayKaydi_OnKayitOlaylari_TetikTuruPatlamadan()
        {
            var log = new MkEventLog(20.0);
            log.Add(10.0, MkEventKind.Kill, 1f, "a");
            log.Add(20.0, MkEventKind.Explosion, 3f, "b");
            log.Add(20.1, MkEventKind.Kill, 1f, "c");
            log.Add(20.2, MkEventKind.Explosion, 3f, "d");
            log.Add(20.3, MkEventKind.Kill, 1f, "e");
            Assert.AreEqual(MkEventKind.Explosion, log.TriggerKind(20.3, 4.0), "dry-2 clip 02: the burst was explosions, the last event a kill");
            Assert.AreEqual(new[] { "b", "c", "d", "e" }, log.Since(15.0).Select(e => e.Detail).ToArray(), "the preroll's events belong to the clip");
            Assert.IsNull(log.TriggerKind(5.0, 4.0));
            log.Add(45.0, MkEventKind.Kill, 1f, "f");
            Assert.AreEqual(new[] { "f" }, log.Since(0.0).Select(e => e.Detail).ToArray(), "older than keepSeconds is dropped");
        }

        [Test]
        public void CokluOldurmeVurusu_OyununKademeOlayiDegil_OldurmelerdenSayilir()
        {
            // S-11 12.10 (dry-2 clip 02): the game's FeelMultiKill says 3 / 5 / 8 (tier reached), so a 6- or 7-body rocket never
            // said 6 — the beat is counted from the kills (GroupKills of one blast, ChainKills of the 0.6 s chain).
            var beat = new MkBurstBeat(6);
            float sum = 0f; int burst = 0;
            for (int g = 1; g <= 5; g++) Assert.AreEqual(0f, beat.OnKill(g, g, out burst), "below the minimum: no beat");
            float w6 = beat.OnKill(6, 6, out burst);
            Assert.AreEqual(6, burst); Assert.AreEqual(MkWeights.Of(MkEventKind.MultiKill, 6, 0, 6), w6, 1e-4f, "a 6-body blast is the §9-1 beat");
            Assert.GreaterOrEqual(w6, 9f);
            sum += w6 + beat.OnKill(7, 7, out burst) + beat.OnKill(8, 4, out burst);
            Assert.AreEqual(MkWeights.Of(MkEventKind.MultiKill, 8, 0, 6), sum, 1e-4f, "every further body adds 1.5: an 8-body blast weighs 1.5 × 8 once");
            Assert.AreEqual(0f, beat.OnKill(5, 6, out burst), "a smaller count inside the same burst adds nothing");
            Assert.AreEqual(0f, beat.OnKill(1, 4, out burst), "the chain still runs (≥ 3): the same burst, no second beat");
            Assert.AreEqual(0f, beat.OnKill(1, 1, out burst), "both below 3: the burst is over");
            for (int g = 2; g <= 5; g++) beat.OnKill(g, g, out burst);
            Assert.AreEqual(MkWeights.Of(MkEventKind.MultiKill, 6, 0, 6), beat.OnKill(6, 6, out burst), 1e-4f, "the next blast is a beat of its own");
            Assert.AreEqual(0f, MkWeights.GameMultiKill(8, 6), "the game's tier event from the minimum on: never counted twice");
            Assert.AreEqual(MkWeights.Of(MkEventKind.MultiKill, 5, 0, 6), MkWeights.GameMultiKill(5, 6), "below it: the game's tier event as before");
        }

        [Test]
        public void OlayKaydi_OldurmeSayilariTasinir()
        {
            var log = new MkEventLog(20.0);
            log.Add(1.0, MkEventKind.Kill, 1f, "k", 4, 6);
            log.Add(1.1, MkEventKind.MultiKill, 9f, "burst", 6);
            MkEventLog.Entry[] e = log.Since(0.0).ToArray();
            Assert.AreEqual(4, e[0].Count); Assert.AreEqual(6, e[0].Chain); Assert.AreEqual(6, e[1].Count); Assert.AreEqual(0, e[1].Chain,
                "a clip seeded from the preroll keeps each kill's blast and chain count (clip.json maxGroupKills / maxChainKills)");
        }

        // ---------------------------------------------------------------- rocket burst + HUD composite

        [Test]
        public void RoketPatlamasi_BirSaniyedeUcAtis_BirOlay()
        {
            var b = new MkRocketBurst();
            Assert.IsFalse(b.Shot(0.0)); Assert.IsFalse(b.Shot(0.4));
            Assert.IsTrue(b.Shot(0.8), "third shot within 1 s");
            Assert.IsFalse(b.Shot(0.9), "the same burst is one event");
            Assert.IsFalse(b.Shot(2.5)); Assert.IsFalse(b.Shot(3.6), "1.1 s apart: never three within 1 s");
            Assert.IsFalse(b.Shot(4.0)); Assert.IsTrue(b.Shot(4.3));
        }

        [Test]
        public void HudBindirme_FarkMatlama_OpakSaydamYari()
        {
            byte[] game = { 10, 200, 30, 255,  10, 200, 30, 255,  10, 200, 30, 255 };
            // pixel 0: no UI (black over black, white over white) · pixel 1: opaque red UI · pixel 2: 50 % white UI (linear)
            byte[] black = { 0, 0, 0, 255,  255, 0, 0, 255,  188, 188, 188, 255 };
            byte[] white = { 255, 255, 255, 255,  255, 0, 0, 255,  255, 255, 255, 255 };
            var dst = new byte[12];
            MkHudComposite.Composite(game, black, white, dst);
            CollectionAssert.AreEqual(new byte[] { 10, 200, 30, 255 }, dst.Take(4).ToArray(), "no UI: the game pixel");
            CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, dst.Skip(4).Take(4).ToArray(), "opaque UI: the UI pixel");
            Assert.AreEqual(0f, MkHudComposite.Coverage(0, 0, 0, 255, 255, 255), 1e-4f);
            Assert.AreEqual(1f, MkHudComposite.Coverage(255, 0, 0, 255, 0, 0), 1e-4f);
            Assert.AreEqual(0.5f, MkHudComposite.Coverage(188, 188, 188, 255, 255, 255), 0.01f);
            Assert.Greater(dst[8], (byte)10, "half-covered: brighter than the game's red channel"); Assert.Less(dst[9], (byte)255);
            MkHudComposite.Composite(game, black, white, black);   // in place into the black buffer (the agent reuses it)
            CollectionAssert.AreEqual(dst, black);
        }
    }
}
