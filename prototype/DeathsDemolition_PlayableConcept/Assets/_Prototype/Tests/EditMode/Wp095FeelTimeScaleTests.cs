using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using DamageSourceKind = DD.Prototype.Damage.DamageSourceKind;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-095 phase 2 · time slice (C-00 K1 / K2 / K5, 08.10; his sözleşmesi §4.11, §5, §9 S3 / S4). The one clock arbiter
    /// <see cref="FeelTimeScale"/> driven directly with a fake 60 fps real clock (<see cref="FeelTimeScale.Advance"/>), the
    /// top-down requests through <see cref="FeelHitStopTD"/>'s FeelEvents handlers: min(requests); the pause wins and the
    /// envelope resumes; a foreign writer silences it; 0.40 s interval, same group ≤ 4 frames, other group refused; the
    /// auto-fire guard; S3 (10 kills/s × 30 s); the last zombie's 0.83 s envelope; the switches; first / third person
    /// untouched; the K2 validator.
    /// </summary>
    [TestFixture]
    public sealed class Wp095FeelTimeScaleTests
    {
        private const float Dt = 1f / 60f;

        private float _timeScaleWas;
        private FeelProfileTD _profile;

        [SetUp]
        public void SetUp()
        {
            _timeScaleWas = Time.timeScale;
            Time.timeScale = 1f;
            CameraModeService.ResetForTests();   // top-down
            RunFlowClock.ClearHolds();
            DeathSequence.Cancel();
            FeelTimeScale.ResetForTests();
            FeelHitStopTD.ResetForTests();
            HitStopService.ResetForTests();
            _profile = ScriptableObject.CreateInstance<FeelProfileTD>();
            _profile.ResetToDefaults();
            FeelProfileTD.SetActiveForTests(_profile);
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            RunFlowClock.ClearHolds();
            DeathSequence.Cancel();
            DeathSequence.ReleasePushIn();
            FeelTimeScale.ResetForTests();
            FeelHitStopTD.ResetForTests();
            HitStopService.ResetForTests();
            FeelProfileTD.SetActiveForTests(null);
            Object.DestroyImmediate(_profile);
            CameraModeService.ResetForTests();
            Time.timeScale = _timeScaleWas;
        }

        // ------------------------------------------------------------------ helpers

        private static void Frames(int n)
        {
            for (int i = 0; i < n; i++) FeelTimeScale.Advance(Dt);
        }

        /// <summary>Runs the current stop out, then the 0.40 s interval (25 frames: the request lands a frame after the gap).</summary>
        private static void RunOutStop()
        {
            for (int i = 0; i < 30 && FeelTimeScale.GlobalStopActive; i++) FeelTimeScale.Advance(Dt);
            Frames(25);
        }

        /// <summary>Frames on the stop scale until the running stop ends (each Advance = the frame that follows it).</summary>
        private static int StopFramesToEnd()
        {
            int n = 0;
            for (int i = 0; i < 30 && FeelTimeScale.GlobalStopActive; i++)
            {
                FeelTimeScale.Advance(Dt);
                if (FeelTimeScale.GlobalStopApplied) n++;
            }

            return n;
        }

        private static FeelKill Kill(int group, bool critical = false, bool heavy = false, EnemyTier tier = EnemyTier.Standard,
            ClassicWeaponId weapon = ClassicWeaponId.AssaultRifle, bool lastOfRound = false) =>
            new FeelKill(null, tier, 1, Vector3.zero, Vector3.forward, BodyRegion.UpperBody, DamageSourceKind.Bullet, critical, heavy, 0f,
                0, 1, group, true, weapon, false, default, 1, 1, lastOfRound);

        private static FeelHit ArmourBreak(int group, ClassicWeaponId weapon = ClassicWeaponId.AssaultRifle) =>
            new FeelHit(null, EnemyTier.Standard, 1, Vector3.zero, Vector3.forward, 0f, 10f, true, false, false, BodyRegion.UpperBody,
                DamageSourceKind.Bullet, 1f, 1f, false, false, group, true, weapon, false, default);

        private static FeelPlayerDamage HeavyHurt(bool fatal = false) =>
            new FeelPlayerDamage(Vector3.zero, Vector3.forward, 30f, 100f, fatal ? 0f : 70f, 100f, fatal, true);

        private static FeelLastZombie Last() => new FeelLastZombie(new Vector3(1f, 0f, 2f), 1, EnemyTier.Standard, null);

        // ------------------------------------------------------------------ K1 · min(requests)

        [Test]
        [Category("WP095")]
        public void Hakem_EnKucukIstekKazanir_SonZombiDurmaninAltindaBaslar()
        {
            Assert.IsTrue(FeelTimeScale.RequestStop(7, 4, FeelEventId.LastZombie));
            Assert.IsTrue(FeelTimeScale.BeginLastZombie(Vector3.one));
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "istek karesinde saat yazılmaz (geç kare uygular)");

            FeelTimeScale.Advance(Dt);
            Assert.AreEqual(_profile.globalStopTimeScale, Time.timeScale, 1e-5f, "min(durma 0,06, ağır çekim 1→0,25) = durma");
            Assert.IsTrue(FeelTimeScale.IsDriving);
            Frames(3);
            Assert.IsTrue(FeelTimeScale.GlobalStopApplied, "4 karelik durma dördüncü karede sürüyor");
            Assert.AreEqual(_profile.globalStopTimeScale, Time.timeScale, 1e-5f);

            FeelTimeScale.Advance(Dt);
            Assert.IsFalse(FeelTimeScale.GlobalStopActive, "4 kare bitti");
            Assert.AreEqual(FeelTimeScale.LastZombieScaleAt(4f * Dt), Time.timeScale, 1e-5f, "durma ağır çekime devreder");
            Assert.Less(Time.timeScale, 0.3f, "giriş rampası durmanın altında koştu: 1'e geri sıçrama yok");

            Frames(20);   // 0.40 s: hold
            Assert.AreEqual(_profile.lastZombieTimeScale, Time.timeScale, 1e-5f, "tutma");
            Assert.AreEqual(1f, FeelTimeScale.LastZombieBlend01, 1e-4f, "kamera zarfı tutmada 1");
            Assert.That(FeelTimeScale.LastZombieProgress01, Is.InRange(0.4f, 0.6f));
        }

        // ------------------------------------------------------------------ K1 / S4 · the pause wins, the envelope resumes

        [Test]
        [Category("WP095")]
        public void Duraklatma_Kazanir_ZarfKaldigiYerdenSurer_SonundaBir()
        {
            // hit-stop: 2 frames before the pause, 2 after
            Assert.IsTrue(FeelTimeScale.RequestStop(3, 4, FeelEventId.HeavyDeath_Ultra));
            Frames(2);
            float remaining = FeelTimeScale.StopRemainingSeconds;
            Assert.AreEqual(3f * Dt, remaining, 1e-5f);

            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            Assert.AreEqual(0f, Time.timeScale, 1e-6f, "duraklatma her zaman kazanır");
            Assert.IsTrue(FeelTimeScale.GlobalStopActive, "TD: duraklatma zarfı askıya alır, bitirmez (K1)");
            Assert.AreEqual(1, FeelTimeScale.Suspends);
            float clock = FeelTimeScale.Clock;
            Frames(120);
            Assert.AreEqual(remaining, FeelTimeScale.StopRemainingSeconds, 1e-6f, "donukken zarf yaşlanmaz");
            Assert.AreEqual(clock, FeelTimeScale.Clock, 1e-6f, "hakemin saati donukken durur");
            Assert.AreEqual(0f, Time.timeScale, 1e-6f, "donukken hakem yazmaz");

            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "çözülünce taban (1) döner, 0,06 değil");
            Assert.AreEqual(2, StopFramesToEnd(), "kalan zarf sürer: 4 = 2 önce + 2 sonra");
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "sonunda 1");
            Assert.IsFalse(FeelTimeScale.IsDriving);

            // last zombie: pause in the hold, resume, the whole envelope is still 0.83 s of running time
            Frames(30);
            Assert.IsTrue(FeelTimeScale.BeginLastZombie(Vector3.zero));
            Frames(19);   // 18 frames aged (0.30 s)
            Assert.AreEqual(_profile.lastZombieTimeScale, Time.timeScale, 1e-5f);
            float elapsed = FeelTimeScale.LastZombieElapsed;
            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            Frames(300);
            Assert.AreEqual(elapsed, FeelTimeScale.LastZombieElapsed, 1e-6f);
            Assert.IsTrue(FeelTimeScale.ShouldDeferRoundEndHold, "K5: duraklatılmış an hâlâ raund sonu donmasını erteler");
            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);
            FeelTimeScale.Advance(Dt);
            Assert.AreEqual(_profile.lastZombieTimeScale, Time.timeScale, 1e-5f, "çözülen kare ağır çekimi yeniden uygular");
            int more = 0;
            while (FeelTimeScale.LastZombieActive && more < 120) { FeelTimeScale.Advance(Dt); more++; }
            // frames the envelope was on the clock: the grant frame + 18 + the resumed frame + every further one but the ending one
            Assert.AreEqual(50, 1 + 18 + 1 + (more - 1), 1, "toplam zarf 0,83 s ± 1 kare çalışan zaman (duraklatma hariç)");
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);

            // the death beat in top-down: suspended by the pause, resumed after (first / third person: PlayerExperienceTests)
            Assert.IsTrue(DeathSequence.Begin(0.5f, 0.1f, 0.5f, 0.1f));
            DeathSequence.Tick(0.2f);
            Assert.AreEqual(0.5f, Time.timeScale, 1e-4f);
            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            Assert.IsTrue(DeathSequence.IsActive);
            DeathSequence.Tick(1f);
            Assert.AreEqual(0.2f, DeathSequence.Elapsed, 1e-5f, "donukken ölüm vuruşu yaşlanmaz");
            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "0,5 değil 1");
            DeathSequence.Tick(0.05f);
            Assert.AreEqual(0.5f, Time.timeScale, 1e-4f, "vuruş kaldığı yerden");
            DeathSequence.Tick(1f);
            Assert.IsFalse(DeathSequence.IsActive);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);

            // the round-end hold ENDS the moment (it is not a pause): K5 takes it only after the slow motion, the safety cut ends it
            Frames(30);
            Assert.IsTrue(FeelTimeScale.BeginLastZombie(Vector3.zero));
            Frames(10);
            RunFlowClock.AddHold(RunFlowClock.RoundEndHold);
            Assert.IsFalse(FeelTimeScale.LastZombieActive);
            RunFlowClock.RemoveHold(RunFlowClock.RoundEndHold);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "raund sonu donması 0,25 değil 1 hatırlar");
        }

        // ------------------------------------------------------------------ K1 · foreign writers

        [Test]
        [Category("WP095")]
        public void YabanciYazar_HakemSusar_SaatOnlarinKalir()
        {
            Assert.IsTrue(FeelTimeScale.RequestStop(5, 4, FeelEventId.ArmorBreak));
            FeelTimeScale.Advance(Dt);
            Assert.AreEqual(_profile.globalStopTimeScale, Time.timeScale, 1e-5f);

            Time.timeScale = 0f;   // a capture agent freezes the picture, bypassing RunFlowClock
            FeelTimeScale.Advance(Dt);
            Assert.IsFalse(FeelTimeScale.GlobalStopActive, "yabancı yazımda zarf biter");
            Assert.AreEqual(1, FeelTimeScale.Yields);
            Assert.AreEqual(0f, Time.timeScale, 1e-6f, "yabancı değer korunur");
            Frames(30);
            Assert.AreEqual(0f, Time.timeScale, 1e-6f, "hakem susar");
            Assert.IsFalse(FeelTimeScale.RequestStop(6, 2, FeelEventId.ArmorBreak), "başkasının durdurduğu saatte istek reddedilir");
            Assert.IsFalse(FeelTimeScale.BeginLastZombie(Vector3.zero));

            Time.timeScale = 1f;   // the capture gives the clock back
            Assert.IsTrue(FeelTimeScale.RequestStop(8, 2, FeelEventId.ArmorBreak));
            Assert.AreEqual(2, StopFramesToEnd());
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);

            Assert.IsTrue(FeelTimeScale.BeginLastZombie(Vector3.zero));
            Frames(12);
            Assert.AreEqual(_profile.lastZombieTimeScale, Time.timeScale, 1e-5f);
            Time.timeScale = 0.5f;   // a capture writes its own scale mid slow motion
            FeelTimeScale.Advance(Dt);
            Assert.IsFalse(FeelTimeScale.LastZombieActive);
            Assert.AreEqual(0.5f, Time.timeScale, 1e-6f, "hakem 0,5'in üstüne yazmaz");
            Assert.AreEqual(2, FeelTimeScale.Yields);
        }

        // ------------------------------------------------------------------ K2 · no accumulation

        [Test]
        [Category("WP095")]
        public void K2_Aralik040_AyniGrupDortKare_BaskaGrupReddedilir()
        {
            Assert.AreEqual(3, FeelTimeScale.FramesFor(2, 1.25f, 4), "2 × 1,25 = 2,5 → yukarı 3");
            Assert.AreEqual(1, FeelTimeScale.FramesFor(2, 0.5f, 4));
            Assert.AreEqual(2, FeelTimeScale.FramesFor(2, 0.75f, 4), "1,5 → 2");
            Assert.AreEqual(4, FeelTimeScale.FramesFor(3, 1.5f, 4), "4,5 → tavan 4");
            Assert.AreEqual(4, FeelTimeScale.FramesFor(4, 1f, 9), "K2: profil 4'ün üstünü istese de 4");
            Assert.AreEqual(2, FeelTimeScale.FramesFor(4, 1f, 2), "profil tavanı");
            Assert.AreEqual(2, FeelTimeScale.FramesFor(2, 0f, 4), "satırı olmayan silah ölçeği 1");
            Assert.AreEqual(0, FeelTimeScale.FramesFor(0, 2f, 4), "durmasız satır durmasız kalır");

            // the request frame: one stop per frame, the longest request of the frame takes it (order-independent)
            Assert.IsTrue(FeelTimeScale.RequestStop(10, 2, FeelEventId.ArmorBreak));
            Assert.IsFalse(FeelTimeScale.RequestStop(11, 1, FeelEventId.PlayerDamaged_Heavy), "aynı kare, daha kısa başka grup: red");
            Assert.IsTrue(FeelTimeScale.RequestStop(12, 3, FeelEventId.HeavyDeath_Elite), "aynı kare: en uzun istek tek durmayı alır");
            Assert.AreEqual(12, FeelTimeScale.ActiveStopGroup);
            Assert.AreEqual(3, FeelTimeScale.ActiveStopFrames);
            Assert.AreEqual(2, FeelTimeScale.Refused, "kısa olan ve devredilen sayılır");

            FeelTimeScale.Advance(Dt);   // on the clock now
            Assert.IsFalse(FeelTimeScale.RequestStop(13, 4, FeelEventId.HeavyDeath_Ultra), "saatteyken başka grup reddedilir");
            Assert.AreEqual(3, FeelTimeScale.Refused);
            Assert.AreEqual(3, FeelTimeScale.ActiveStopFrames, "başka grup uzatmaz");
            Assert.IsTrue(FeelTimeScale.RequestStop(12, 4, FeelEventId.MultiKill_T3), "aynı grup yükseltir");
            Assert.AreEqual(4, FeelTimeScale.ActiveStopFrames);
            Assert.IsTrue(FeelTimeScale.RequestStop(12, 9, FeelEventId.LastZombie));
            Assert.AreEqual(4, FeelTimeScale.ActiveStopFrames, "yükseltme en çok 4 kare (K2)");
            Assert.AreEqual(1, FeelTimeScale.Extended);
            Assert.AreEqual(4, 1 + StopFramesToEnd(), "istek karesi tam ilk karesini korur: 4 kare = 0,067 s");
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);

            Assert.IsFalse(FeelTimeScale.RequestStop(14, 2, FeelEventId.ArmorBreak), "bittiği an: aralık");
            Frames(23);
            Assert.IsFalse(FeelTimeScale.RequestStop(15, 2, FeelEventId.ArmorBreak), "0,383 s < 0,40 s");
            Frames(1);
            Assert.IsTrue(FeelTimeScale.RequestStop(16, 2, FeelEventId.ArmorBreak), "0,40 s sonra yeni durma");
            Assert.AreEqual(5, FeelTimeScale.Refused);
            Assert.AreEqual(2, FeelTimeScale.Stops);
        }

        // ------------------------------------------------------------------ §5 auto-fire guard

        [Test]
        [Category("WP095")]
        public void OtoAtesKorumasi_YalnizBuyukAnlarGirer()
        {
            FeelHitStopTD.NotifyTrigger(true, 600f);   // ar_1-like: 600 rpm producing shots
            Frames(10);   // 0.17 s < 0.35 s
            Assert.IsFalse(FeelHitStopTD.AutoGuardActive);
            FeelHitStopTD.OnPlayerDamaged(HeavyHurt());
            Assert.AreEqual(1, FeelTimeScale.Stops, "tutuş kısa: ağır hasar durur");
            RunOutStop();

            Assert.IsTrue(FeelHitStopTD.AutoGuardActive, "≥ 500 rpm, ≥ 0,35 s");
            FeelHitStopTD.OnPlayerDamaged(HeavyHurt());
            FeelHitStopTD.OnKill(Kill(21, critical: true, weapon: ClassicWeaponId.Ar2));
            Assert.AreEqual(1, FeelTimeScale.Stops, "koruma: ağır hasar ve kritik öldürme girmez");
            Assert.AreEqual(2, FeelTimeScale.Guarded);

            FeelHitStopTD.OnHit(ArmourBreak(22));
            Assert.AreEqual(2, FeelTimeScale.Stops, "zırh kırılma girer");
            RunOutStop();
            FeelHitStopTD.OnKill(Kill(23, heavy: true, tier: EnemyTier.Elite));
            Assert.AreEqual(3, FeelTimeScale.Stops, "ağır ölüm girer");
            Assert.AreEqual(3, FeelTimeScale.ActiveStopFrames, "Elite 3 × ar_1 1,0");
            RunOutStop();
            FeelHitStopTD.OnMultiKill(new FeelMultiKill(2, 5, true, 24, 2, Vector3.zero));
            Assert.AreEqual(4, FeelTimeScale.Stops, "çoklu öldürme T2 girer");
            RunOutStop();
            FeelHitStopTD.OnKill(Kill(25, weapon: ClassicWeaponId.AssaultRifle, lastOfRound: true));
            FeelHitStopTD.OnLastZombie(Last());
            Assert.AreEqual(5, FeelTimeScale.Stops, "son zombi girer");
            Assert.AreEqual(25, FeelTimeScale.ActiveStopGroup, "son zombinin durması öldüren çekişin grubunda");
            for (int i = 0; i < 120 && FeelTimeScale.LastZombieActive; i++) FeelTimeScale.Advance(Dt);
            Assert.IsFalse(FeelTimeScale.LastZombieActive);
            Frames(25);

            FeelHitStopTD.NotifyTrigger(false, 0f);
            FeelHitStopTD.NotifyTrigger(true, 420f);   // lmg_2: below 500 rpm, held long
            Frames(60);
            Assert.IsFalse(FeelHitStopTD.AutoGuardActive, "500 rpm altı korunmaz");
            FeelHitStopTD.OnPlayerDamaged(HeavyHurt());
            Assert.AreEqual(6, FeelTimeScale.Stops);
            RunOutStop();
            FeelHitStopTD.NotifyTrigger(false, 0f);
            Assert.IsFalse(FeelHitStopTD.AutoGuardActive, "tetik bırakıldı");
        }

        // ------------------------------------------------------------------ S3 · accumulation (his sözleşmesi §9)

        /// <summary>Deterministic 32-bit LCG; the S3 stream is reproducible off Unity (the arbiter rules were simulated with the same draws).</summary>
        private sealed class Lcg
        {
            private uint _s;
            public Lcg(uint seed) { _s = seed; }

            public double Next()
            {
                _s = unchecked(_s * 1664525u + 1013904223u);
                return ((_s >> 8) + 0.5) / 16777216.0;
            }
        }

        private struct S3Result
        {
            public float Share;
            public int MinGapFrames;
            public int LongestStopFrames;
            public int StopRuns;
            public int Guarded;
        }

        /// <summary>
        /// §5 / §9 S3 model (the game designer's: a Poisson kill stream, every kill its own trigger pull): 10 kills/s for 30 s
        /// at 60 fps. mix 0 = single-shot play (ar_2 / sg_1 alternate, HS 1.0 / 1.25): critical kill 19 %, armour break 8 %,
        /// Elite 2.5 %, Ultra 0.5 % — 30 % of kills ask for a stop (≈ the share the contract's own numbers imply). mix 1 =
        /// automatic play (ar_1 600 rpm held all along): armour 8 %, Elite 2.5 %, Ultra 0.5 %, heavy player hit 2 %. mix 2 =
        /// saturation: every kill breaks armour with sr_1 (3 frames).
        /// </summary>
        private static S3Result RunS3(uint seed, int mix)
        {
            FeelTimeScale.ResetForTests();
            FeelHitStopTD.ResetForTests();
            Time.timeScale = 1f;
            if (mix == 1) FeelHitStopTD.NotifyTrigger(true, 600f);

            const double rate = 10.0, seconds = 30.0;
            var rng = new Lcg(seed);
            double next = -Math.Log(1.0 - rng.Next()) / rate;
            double u = next < seconds ? rng.Next() : 0.0;
            int k = 0;
            int frames = (int)Math.Round(seconds * 60.0);
            int stopped = 0, gap = 0, minGap = int.MaxValue, run = 0, longest = 0, runs = 0;
            bool prev = false, seen = false;
            for (int f = 0; f < frames; f++)
            {
                double frameEnd = (f + 1) / 60.0;
                while (next < seconds && next < frameEnd)
                {
                    Emit(k, u, mix);
                    k++;
                    next += -Math.Log(1.0 - rng.Next()) / rate;
                    if (next < seconds) u = rng.Next();
                }

                FeelTimeScale.Advance(Dt);
                bool on = FeelTimeScale.GlobalStopApplied;
                if (on)
                {
                    stopped++;
                    if (seen && !prev) { minGap = Math.Min(minGap, gap); }
                    if (!prev) runs++;
                    seen = true;
                    gap = 0;
                    run++;
                    longest = Math.Max(longest, run);
                }
                else
                {
                    run = 0;
                    if (seen) gap++;
                }

                prev = on;
            }

            return new S3Result
            {
                Share = stopped / (float)frames, MinGapFrames = minGap, LongestStopFrames = longest, StopRuns = runs, Guarded = FeelTimeScale.Guarded,
            };
        }

        private static void Emit(int k, double u, int mix)
        {
            int group = k + 1;
            if (mix == 2)
            {
                FeelHitStopTD.OnHit(ArmourBreak(group, ClassicWeaponId.Sr1));
                FeelHitStopTD.OnKill(Kill(group, weapon: ClassicWeaponId.Sr1));
                return;
            }

            ClassicWeaponId weapon = mix == 1 ? ClassicWeaponId.AssaultRifle : (k % 2 == 0 ? ClassicWeaponId.Ar2 : ClassicWeaponId.CombatShotgun);
            double acc = 0.0;
            if (mix == 0)
            {
                if (u < (acc += .19)) { FeelHitStopTD.OnKill(Kill(group, critical: true, weapon: weapon)); return; }
                if (u < (acc += .08)) { FeelHitStopTD.OnHit(ArmourBreak(group, weapon)); FeelHitStopTD.OnKill(Kill(group, weapon: weapon)); return; }
                if (u < (acc += .025)) { FeelHitStopTD.OnKill(Kill(group, heavy: true, tier: EnemyTier.Elite, weapon: weapon)); return; }
                if (u < (acc += .005)) { FeelHitStopTD.OnKill(Kill(group, heavy: true, tier: EnemyTier.Ultra, weapon: weapon)); return; }
            }
            else
            {
                if (u < (acc += .08)) { FeelHitStopTD.OnHit(ArmourBreak(group, weapon)); FeelHitStopTD.OnKill(Kill(group, weapon: weapon)); return; }
                if (u < (acc += .025)) { FeelHitStopTD.OnKill(Kill(group, heavy: true, tier: EnemyTier.Elite, weapon: weapon)); return; }
                if (u < (acc += .005)) { FeelHitStopTD.OnKill(Kill(group, heavy: true, tier: EnemyTier.Ultra, weapon: weapon)); return; }
                if (u < (acc += .02)) { FeelHitStopTD.OnPlayerDamaged(HeavyHurt()); return; }
            }

            FeelHitStopTD.OnKill(Kill(group, weapon: weapon));   // an ordinary kill: no stop
        }

        [Test]
        [Category("WP095")]
        public void S3_Birikme_Saniyede10Oldurme30s_PayAltiYuzde_AralikKirkSantisaniye()
        {
            S3Result single = RunS3(95u, 0);
            S3Result auto = RunS3(95u, 1);
            S3Result saturated = RunS3(95u, 2);
            TestContext.WriteLine("[DD-TEST] wp095.s3 single share=" + single.Share.ToString("P2") + " stops=" + single.StopRuns +
                                  " minGap=" + single.MinGapFrames + "f longest=" + single.LongestStopFrames + "f | auto share=" +
                                  auto.Share.ToString("P2") + " stops=" + auto.StopRuns + " guarded=" + auto.Guarded +
                                  " | saturated share=" + saturated.Share.ToString("P2") + " minGap=" + saturated.MinGapFrames + "f");

            Assert.LessOrEqual(single.Share, 0.06f, "S3: tek-atış oynayışta küresel pay ≤ %6");
            Assert.Greater(single.Share, 0.03f, "ölçüm boş değil: durmalar gerçekten oldu");
            Assert.GreaterOrEqual(single.StopRuns, 20);
            Assert.LessOrEqual(auto.Share, 0.06f, "S3: otomatik oynayışta küresel pay ≤ %6");
            Assert.Greater(auto.StopRuns, 0);
            foreach (S3Result r in new[] { single, auto, saturated })
            {
                Assert.GreaterOrEqual(r.MinGapFrames * Dt, 0.40f - 1e-4f, "K2: ard arda iki küresel durma arası ≥ 0,40 s");
                Assert.LessOrEqual(r.LongestStopFrames, FeelTimeScale.MaxStopFrames, "K2: tek durma ≤ 4 kare");
            }

            // Saturation (every kill qualifies) shows the rules' own ceiling: stop / (stop + interval) ≈ 3 / 28 frames. The ≤ 6 %
            // share is a property of the stream (the designer's Poisson model), not a guarantee of the rules — see the report.
            Assert.LessOrEqual(saturated.Share, 4f / 29f, "kuralların tavanı: 4 kare / (4 + 25)");
        }

        // ------------------------------------------------------------------ §4.11 · the last zombie, the switches

        [Test]
        [Category("WP095")]
        public void SonZombi_Zarf083ArtiEksiBirKare_SondaBir_KapaliAnahtarIstekYok()
        {
            FeelHitStopTD.OnKill(Kill(31, weapon: ClassicWeaponId.AssaultRifle, lastOfRound: true));
            Assert.IsFalse(FeelTimeScale.GlobalStopActive, "son öldürmenin durmasını LastZombie alır (§4.11)");
            FeelHitStopTD.OnLastZombie(Last());
            Assert.IsTrue(FeelTimeScale.GlobalStopActive);
            Assert.AreEqual(4, FeelTimeScale.ActiveStopFrames, "4 kare küresel durma");
            Assert.IsTrue(FeelTimeScale.LastZombieActive);
            Assert.IsTrue(FeelTimeScale.ShouldDeferRoundEndHold, "K5: TD + ağır çekim → raund sonu donması ertelenir");
            Assert.AreEqual(new Vector3(1f, 0f, 2f), FeelTimeScale.LastZombiePosition);

            int slow = 0, stopFrames = 0;
            float lowest = 1f;
            for (int i = 0; i < 200 && FeelTimeScale.LastZombieActive; i++)
            {
                FeelTimeScale.Advance(Dt);
                if (FeelTimeScale.GlobalStopApplied) stopFrames++;
                if (Time.timeScale < 0.999f) slow++;
                if (!FeelTimeScale.GlobalStopActive) lowest = Mathf.Min(lowest, Time.timeScale);
            }

            float total = _profile.lastZombieInSeconds + _profile.lastZombieHoldSeconds + _profile.lastZombieOutSeconds;
            Assert.AreEqual(0.83f, total, 1e-4f, "profil: 0,08 + 0,45 + 0,30");
            Assert.AreEqual(total, slow * Dt, Dt + 1e-4f, "zarf 0,83 s ± 1 kare (gerçek zaman)");
            Assert.AreEqual(4, stopFrames);
            Assert.AreEqual(_profile.lastZombieTimeScale, lowest, 1e-4f, "ağır çekim 0,25");
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "sonunda 1");
            Assert.IsFalse(FeelTimeScale.IsDriving);
            Assert.IsFalse(FeelTimeScale.ShouldDeferRoundEndHold);

            // both switches off: no request at all
            FeelTimeScale.ResetForTests();
            _profile.hitStopGlobal = false;
            _profile.lastZombieSlowMo = false;
            FeelHitStopTD.OnHit(ArmourBreak(41));
            FeelHitStopTD.OnKill(Kill(42, heavy: true, tier: EnemyTier.Ultra));
            FeelHitStopTD.OnKill(Kill(43, critical: true, weapon: ClassicWeaponId.Sr1));
            FeelHitStopTD.OnMultiKill(new FeelMultiKill(3, 8, true, 44, 3, Vector3.zero));
            FeelHitStopTD.OnPlayerDamaged(HeavyHurt());
            FeelHitStopTD.OnKill(Kill(45, lastOfRound: true));
            FeelHitStopTD.OnLastZombie(Last());
            Assert.AreEqual(0, FeelTimeScale.Stops);
            Assert.AreEqual(0, FeelTimeScale.LastZombies);
            Assert.AreEqual(0, FeelTimeScale.Refused, "kapalı anahtar istek bile üretmez");
            Frames(60);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);
            Assert.IsFalse(FeelTimeScale.HasWork);

            // slow motion off, stop on: the 4-frame stop stays (§4.11)
            _profile.hitStopGlobal = true;
            FeelHitStopTD.OnKill(Kill(46, lastOfRound: true));
            FeelHitStopTD.OnLastZombie(Last());
            Assert.AreEqual(1, FeelTimeScale.Stops);
            Assert.IsFalse(FeelTimeScale.LastZombieActive);
            Assert.IsFalse(FeelTimeScale.ShouldDeferRoundEndHold, "ağır çekim kapalı: erteleme yok");
            RunOutStop();

            // stop off, slow motion on: only the slow motion
            _profile.hitStopGlobal = false;
            _profile.lastZombieSlowMo = true;
            FeelHitStopTD.OnKill(Kill(47, lastOfRound: true));
            FeelHitStopTD.OnLastZombie(Last());
            Assert.AreEqual(1, FeelTimeScale.Stops);
            Assert.IsTrue(FeelTimeScale.LastZombieActive);

            // ordinary kills never stop; a critical kill only with a single-shot class weapon
            FeelTimeScale.ResetForTests();
            _profile.hitStopGlobal = true;
            FeelHitStopTD.OnKill(Kill(51));
            FeelHitStopTD.OnKill(Kill(52, critical: true, weapon: ClassicWeaponId.AutoSmg));
            FeelHitStopTD.OnMultiKill(new FeelMultiKill(3, 9, false, 0, 0, Vector3.zero));   // a chain: never stops
            FeelHitStopTD.OnMultiKill(new FeelMultiKill(1, 3, true, 53, 1, Vector3.zero));   // T1: never stops
            FeelHitStopTD.OnPlayerDamaged(HeavyHurt(fatal: true));                           // the death beat's
            Assert.AreEqual(0, FeelTimeScale.Stops);
            FeelHitStopTD.OnKill(Kill(54, critical: true, weapon: ClassicWeaponId.Sr1));
            Assert.AreEqual(1, FeelTimeScale.Stops);
            Assert.AreEqual(3, FeelTimeScale.ActiveStopFrames, "kritik + sr_1: 2 × 1,5 = 3 kare");
        }

        /// <summary>
        /// S-02 (f2b: fixedDt 0.0010 after the PlayMode death beat): the arbiter scales the physics step with its slow clock and must
        /// give the base back on every exit — three stops in a row, the last zombie, the death beat — never compounding (a drive
        /// that starts on a step it scaled itself must not take that step as the new base).
        /// </summary>
        [Test]
        [Category("WP095")]
        public void FizikAdimi_HerCikistaTabanaDoner_ArdArdaDurmalardaBirikmez()
        {
            const float baseFixed = 0.02f;
            float project = Time.fixedDeltaTime;   // in EditMode this IS ProjectSettings/TimeManager — it must not move
            FeelTimeScale.TestFixedSink = true;
            FeelTimeScale.TestFixedValue = baseFixed;
            try
            {
                for (int k = 0; k < 3; k++)
                {
                    Assert.IsTrue(FeelTimeScale.RequestStop(100 + k, 4, FeelEventId.ArmorBreak), "stop " + k);
                    Frames(1);
                    Assert.Less(FeelTimeScale.TestFixedValue, baseFixed, "stop " + k + ": the step shrinks with the clock");
                    Assert.GreaterOrEqual(FeelTimeScale.TestFixedValue, baseFixed * 0.2f - 1e-6f, "never below 0.2 × base");
                    RunOutStop();
                    Assert.AreEqual(baseFixed, FeelTimeScale.TestFixedValue, 1e-6f, "stop " + k + ": the base comes back, no compounding");
                }

                Assert.IsTrue(FeelTimeScale.BeginLastZombie(Vector3.zero));
                for (int i = 0; i < 120 && FeelTimeScale.HasWork; i++) FeelTimeScale.Advance(Dt);
                Assert.IsFalse(FeelTimeScale.LastZombieActive);
                Assert.AreEqual(baseFixed, FeelTimeScale.TestFixedValue, 1e-6f, "after the last zombie");

                Assert.IsTrue(DeathSequence.Begin(0.5f, 0.1f, 0.3f, 0.1f));
                for (int i = 0; i < 120 && DeathSequence.IsActive; i++) { DeathSequence.Tick(Dt); FeelTimeScale.Advance(Dt); }
                Assert.IsFalse(DeathSequence.IsActive);
                Assert.AreEqual(baseFixed, FeelTimeScale.TestFixedValue, 1e-6f, "after the death beat");
                Assert.AreEqual(1f, Time.timeScale, 1e-6f);
            }
            finally
            {
                FeelTimeScale.TestFixedSink = false;
            }

            Assert.AreEqual(project, Time.fixedDeltaTime, 0f, "the project's Fixed Timestep is never written outside play mode");
        }

        /// <summary>gt-f2 MUST-1: in first / third person the death beat still drives the clock, the physics step stays at the base.</summary>
        [Test]
        [Category("WP095")]
        [Category("FPTP")]
        public void FizikAdimi_IlkUcuncuSahis_OlumVurusundaDegismez()
        {
            const float baseFixed = 0.02f;
            float project = Time.fixedDeltaTime;
            FeelTimeScale.TestFixedSink = true;
            FeelTimeScale.TestFixedValue = baseFixed;
            try
            {
                foreach (CameraMode mode in new[] { CameraMode.FirstPerson, CameraMode.ThirdPerson })
                {
                    CameraModeService.Request(mode, instant: true);
                    Assert.AreEqual(mode, CameraModeService.Current);
                    Assert.IsTrue(DeathSequence.Begin(0.5f, 0.1f, 0.3f, 0.1f), mode + " beat");
                    float smallest = baseFixed;
                    for (int i = 0; i < 120 && DeathSequence.IsActive; i++)
                    {
                        DeathSequence.Tick(Dt);
                        FeelTimeScale.Advance(Dt);
                        smallest = Mathf.Min(smallest, FeelTimeScale.TestFixedValue);
                    }

                    Assert.Less(Time.timeScale, 1.01f);
                    Assert.AreEqual(baseFixed, smallest, 0f, mode + ": the step never moved during the beat (user: top-down only)");
                    Assert.AreEqual(baseFixed, FeelTimeScale.TestFixedValue, 0f, mode + ": the step after the beat");
                }
            }
            finally
            {
                FeelTimeScale.TestFixedSink = false;
                CameraModeService.ResetForTests();
            }

            Assert.AreEqual(project, Time.fixedDeltaTime, 0f, "the project's Fixed Timestep untouched");
        }

        [Test]
        [Category("WP095")]
        public void Olum_Kazanir_TopDownSlotlariBiter_YeniIstekReddedilir()
        {
            Assert.IsTrue(FeelTimeScale.BeginLastZombie(Vector3.zero));
            Assert.IsTrue(FeelTimeScale.RequestStop(61, 2, FeelEventId.PlayerDamaged_Heavy));
            FeelTimeScale.Advance(Dt);
            Assert.AreEqual(_profile.globalStopTimeScale, Time.timeScale, 1e-5f);

            Assert.IsTrue(DeathSequence.Begin(0.5f, 0.1f, 0.5f, 0.1f));
            Assert.IsFalse(FeelTimeScale.GlobalStopActive, "ölüm kazanır (§4.11 P 100)");
            Assert.IsFalse(FeelTimeScale.LastZombieActive);
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "vuruş tabandan başlar, ekrandaki 0,06'dan değil");
            Assert.IsFalse(FeelTimeScale.RequestStop(62, 2, FeelEventId.ArmorBreak));
            Assert.IsFalse(FeelTimeScale.BeginLastZombie(Vector3.zero));
            FeelHitStopTD.OnLastZombie(Last());
            Assert.AreEqual(1, FeelTimeScale.LastZombies);

            DeathSequence.Tick(0.2f);
            Assert.AreEqual(0.5f, Time.timeScale, 1e-4f, "ölüm vuruşu hakem üzerinden yazar");
            Assert.IsTrue(FeelTimeScale.IsDriving);
            DeathSequence.Cancel();
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);
            Assert.IsFalse(FeelTimeScale.IsDriving);
        }

        // ------------------------------------------------------------------ first / third person untouched

        [Test]
        [Category("WP095")]
        [Category("FPTP")]
        public void IlkVeUcuncuSahis_HicIstekYok_YerelTavan004()
        {
            foreach (CameraMode mode in new[] { CameraMode.FirstPerson, CameraMode.ThirdPerson })
            {
                CameraModeService.Request(mode, instant: true);
                FeelTimeScale.ResetForTests();
                HitStopService.ResetForTests();
                FeelHitStopTD.OnHit(ArmourBreak(71));
                FeelHitStopTD.OnKill(Kill(72, heavy: true, tier: EnemyTier.Ultra));
                FeelHitStopTD.OnKill(Kill(73, critical: true, weapon: ClassicWeaponId.Sr1));
                FeelHitStopTD.OnMultiKill(new FeelMultiKill(3, 8, true, 74, 3, Vector3.zero));
                FeelHitStopTD.OnPlayerDamaged(HeavyHurt());
                FeelHitStopTD.OnKill(Kill(75, lastOfRound: true));
                FeelHitStopTD.OnLastZombie(Last());
                Assert.IsFalse(FeelTimeScale.RequestStop(76, 4, FeelEventId.LastZombie), mode + ": küresel durma yalnız TD");
                Assert.IsFalse(FeelTimeScale.BeginLastZombie(Vector3.zero), mode + ": ağır çekim yalnız TD");
                Assert.AreEqual(0, FeelTimeScale.Stops, mode.ToString());
                Assert.AreEqual(0, FeelTimeScale.LastZombies, mode.ToString());
                Frames(30);
                Assert.AreEqual(1f, Time.timeScale, 1e-6f, mode + ": saat değişmez");
                Assert.IsFalse(FeelTimeScale.HasWork);

                Assert.IsTrue(HitStopService.RequestForGroup(7, .2f, 0f, .06f));
                Assert.AreEqual(HitStopService.LegacyMaxSeconds, HitStopService.RemainingRealSeconds, 1e-6f, mode + ": yerel zarf 0,04 kalır");
                Assert.AreEqual(1f, Time.timeScale, 1e-6f);
            }

        }

        // C-00 TD-only: keep the mixed method's TD assertions outside its FPTP category.
        [Test]
        [Category("WP095")]
        public void Ustten_YerelTavan_DortKare()
        {
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            HitStopService.ResetForTests();
            Assert.IsTrue(HitStopService.RequestForGroup(8, .2f, 0f, .06f));
            Assert.AreEqual(4f / 60f, HitStopService.RemainingRealSeconds, 1e-6f, "TD: yerel zarf tavanı maxStopFrames / 60 (K2)");
        }

        [Test]
        [Category("WP095")]
        public void HitStopService_KureselDurmadaGorselDelta_IkiKezYavaslatmaz()
        {
            Assert.IsTrue(HitStopService.RequestForGroup(9, .035f, 0f, .06f));
            Assert.AreEqual(Dt * .06f, HitStopService.VisualDelta(9, Dt, Dt), 1e-6f, "yalnız yerel zarf: grubun VFX'i %6");

            Assert.IsTrue(FeelTimeScale.RequestStop(81, 2, FeelEventId.ArmorBreak));
            Assert.AreEqual(Dt * .06f, HitStopService.VisualDelta(9, Dt, Dt), 1e-6f, "istek karesi: küresel durma henüz saatte değil");
            FeelTimeScale.Advance(Dt);
            Assert.IsTrue(FeelTimeScale.GlobalStopApplied);
            Assert.AreEqual(0.123f, HitStopService.VisualDelta(9, 0.123f, Dt), 1e-6f, "küresel durma saatte: ölçekli delta, iki kez değil");
        }

        // ------------------------------------------------------------------ K2 · validator

        [Test]
        [Category("WP095")]
        public void Validator_K2_MaxContinuous0067Kabul_YerelSatirlar004Kalir()
        {
            FeelConfigData d = FeelConfig.CreateDefaults();
            Assert.IsTrue(FeelConfigValidator.Validate(d, out string error), "0,04 taban geçerli kalır: " + error);
            d.hitStop.maxContinuousSeconds = 4f / 60f;
            Assert.IsTrue(FeelConfigValidator.Validate(d, out error), "4 kare: " + error);
            d.hitStop.maxContinuousSeconds = .067f;
            Assert.IsTrue(FeelConfigValidator.Validate(d, out error), "0,067: " + error);
            d.hitStop.maxContinuousSeconds = .068f;
            Assert.IsFalse(FeelConfigValidator.Validate(d, out error));
            Assert.That(error, Does.Contain("hitStop"));
            d.hitStop.maxContinuousSeconds = .03f;
            Assert.IsFalse(FeelConfigValidator.Validate(d, out _));

            // the FeelConfig impact rows (the local VFX stops, first / third person) keep their 0.04 ceiling and zero rule
            d = FeelConfig.CreateDefaults();
            d.impacts[(int)ImpactFeelId.ArmorBreak].hitStopSeconds = .05f;
            Assert.IsFalse(FeelConfigValidator.Validate(d, out _));
            d = FeelConfig.CreateDefaults();
            d.impacts[(int)ImpactFeelId.Death].hitStopSeconds = .02f;
            Assert.IsFalse(FeelConfigValidator.Validate(d, out _));
        }
    }
}
