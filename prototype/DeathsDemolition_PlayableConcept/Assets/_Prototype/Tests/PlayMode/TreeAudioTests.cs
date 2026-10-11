using System;
using System.Collections;
using DD.Prototype.Audio;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension (bare Is stays NUnit-free: fully qualified below)

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-104 (R4) · ağaç sesleri (B0 TreeEvents → TreeAudio, AP-011 v024; Run sahnesi, gerçek director).
    /// - Her TreeProcKind kendi sesini çalar, sessiz türler susar.
    /// - FireGround / VenomCloud döngüleri zon ömrü boyunca sürer: zon ≤ 16, en yakın 3 duyulur. Kara delik açılır, çökerken ya da
    ///   ömrü bitince kapanır.
    /// - Sık seslerde LOD: aynı karede 10 Crit → 1; Leech saniyede ≤ 5; Crit canlı tavanı 2.
    /// - Mini Nuke ≤ 1 / 3 s (birkaç yüz ms erken gelen bekletilir, kaybolmaz) ve kısa duck; Doomsday ≤ 1.
    /// - Ağaç ekranı: damga, sinerji, aura yükselişi.
    /// - Abonelik: Rearm / ResetForTests / director yeniden etkin sonrası tek dinleyici.
    /// - Kararlı durumda GC 0.
    /// </summary>
    [TestFixture]
    public sealed class TreeAudioTests
    {
        private SpecialWeaponSahne _s;
        private PrototypeAudioDirector _d;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _d = PrototypeAudioDirector.EnsureExists();
            yield return SpecialWeaponSahne.Bekle(() => _d.Library.PrewarmComplete, 30f, "Ses kütüphanesi prewarm bitmedi.");
            Time.captureDeltaTime = 0f;
            _d.Tree.ResetRun();
            _d.Tree.ResetGatesForTests();
            // The director outlives a test: voices the previous test left ringing must not take a pool slot from this one's sounds.
            _d.WorldPool.StopAll();
            _d.SignalPool.StopAll();
        }

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            TreeRuntime.ClearTestStats();
            if (_d != null)
            {
                if (!_d.enabled) _d.enabled = true;
                _d.Tree.StopAll();
            }

            yield return _s.Bitir();
        }

        private Vector3 Konum(float dx, float dz)
        {
            return _d.Listener.Transform.position + new Vector3(dx, 0f, dz);
        }

        private static TreeProcInfo Proc(TreeProcKind kind, Vector3 at)
        {
            return new TreeProcInfo { Kind = kind, Point = at, Radius = 2f, Tier = 3 };
        }

        private void KisaOmur(float seconds)
        {
            TreeStats stats = TreeStats.Neutral;
            stats.FireGroundSeconds = seconds;
            stats.VenomCloudSeconds = seconds;
            stats.HoleSeconds = seconds;
            TreeRuntime.SetTestStats(stats);
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Agac_HerProcTuru_KendiSesi_SessizTurlerSusar()
        {
            TreeAudio t = _d.Tree;
            KisaOmur(1f);
            // Every kind in a row of ~3 s leaves a dozen long voices (the 8 s bell, the 6 s nuke) ringing: the whole hard capacity, so
            // the test measures routing, not the World budget (HavuzTavani_* owns that).
            int kapasite = _d.WorldPool.Capacity;
            _d.WorldPool.Capacity = _d.WorldPool.HardCapacity;
            try
            {
                foreach (TreeProcKind kind in Enum.GetValues(typeof(TreeProcKind)))
                {
                    AudioSoundId beklenen = TreeAudio.SoundFor(kind);
                    int calan = _d.PlayedCount, dongu = t.LoopStarts, olay = t.ProcEvents;
                    TreeEvents.RaiseProc(Proc(kind, Konum(2f, 3f)));
                    Assert.AreEqual(olay + 1, t.ProcEvents, kind + ": tek abonelik");
                    switch (kind)
                    {
                        case TreeProcKind.FireGround:
                        case TreeProcKind.VenomCloud:
                            yield return null;   // the zone is heard from the next Tick
                            Assert.IsTrue(t.FieldLoopActive(beklenen), kind + ": " + beklenen + " döngüsü başlamadı");
                            Assert.AreEqual(dongu + 1, t.LoopStarts, kind + ": döngü başlangıcı");
                            break;
                        case TreeProcKind.BlackHoleOpen:
                            Assert.IsTrue(t.BlackHoleActive, "kara delik döngüsü açılmadı");
                            Assert.AreEqual(dongu + 1, t.LoopStarts);
                            Assert.AreEqual(AudioSoundId.TreeBlackHoleLoop, beklenen);
                            break;
                        case TreeProcKind.BlackHoleCollapse:
                            Assert.AreEqual(calan + 1, _d.PlayedCount, "çökme sesi");
                            Assert.AreEqual(AudioSoundId.TreeBlackHoleCollapse, _d.LastPlayed);
                            Assert.IsFalse(t.BlackHoleActive, "çökme döngüyü kapatır");
                            break;
                        default:
                            if (beklenen == AudioSoundId.None)
                            {
                                Assert.AreEqual(calan, _d.PlayedCount, kind + " sessiz olmalı (patlama / bomblet kendi cue'su ya da HUD / VFX vuruşu)");
                            }
                            else
                            {
                                Assert.AreEqual(calan + 1, _d.PlayedCount, kind + " ses üretmedi (dropped=" + _d.DroppedCues + ")");
                                Assert.AreEqual(beklenen, _d.LastPlayed, kind + " yanlış ses");
                                Assert.AreEqual(beklenen, t.LastSound);
                            }

                            break;
                    }

                    yield return new WaitForSecondsRealtime(0.12f);   // past every frequent sound's interval (Crit → CritRing)
                }
            }
            finally
            {
                _d.WorldPool.Capacity = kapasite;
            }

            Debug.Log("[DD-TEST] wp104.audio kinds=" + Enum.GetValues(typeof(TreeProcKind)).Length + " " + t.Describe()
                      + " produced=" + _d.Library.IsProduced(AudioSoundId.TreeCrit));
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Agac_Donguler_ZonOmruBoyunca_EnYakinUcDuyulur_ZonTavani_KaraDelikCokmeVeOmur()
        {
            TreeAudio t = _d.Tree;
            KisaOmur(1f);

            // Three near zones and two far ones: the three nearest are heard.
            TreeEvents.RaiseProc(Proc(TreeProcKind.FireGround, Konum(3f, 0f)));
            TreeEvents.RaiseProc(Proc(TreeProcKind.VenomCloud, Konum(0f, 5f)));
            TreeEvents.RaiseProc(Proc(TreeProcKind.FireGround, Konum(-7f, 0f)));
            TreeEvents.RaiseProc(Proc(TreeProcKind.FireGround, Konum(40f, 0f)));
            TreeEvents.RaiseProc(Proc(TreeProcKind.VenomCloud, Konum(0f, 45f)));
            yield return null;
            yield return null;
            Assert.AreEqual(5, t.ZoneCount);
            Assert.AreEqual(AudioMixPlan.TreeFieldVoices, t.FieldLoopsActive, "en yakın 3 zon duyulur");
            Assert.Less(t.HeardFarthestMeters, 10f, "uzak zonlar sessiz kalır");
            Assert.IsTrue(t.FieldLoopActive(AudioSoundId.TreeFireLoop) && t.FieldLoopActive(AudioSoundId.TreePoisonLoop));

            // The same zone republished refreshes it.
            int tazele = t.ZoneRefreshes;
            TreeEvents.RaiseProc(Proc(TreeProcKind.FireGround, Konum(3.5f, 0f)));
            Assert.AreEqual(tazele + 1, t.ZoneRefreshes);
            Assert.AreEqual(5, t.ZoneCount, "aynı zon ikinci kez açılmaz");

            // Life over: every loop ends.
            yield return new WaitForSecondsRealtime(1.0f + AudioMixPlan.TreeLoopFadeOut + 0.3f);
            Assert.AreEqual(0, t.ZoneCount, "zon ömrü bitti");
            Assert.AreEqual(0, t.FieldLoopsActive, "döngüler kapandı");

            // B0 AreaZones: 17 zones → 16 tracked, the one ending first gives way.
            int yer = t.ZonesReplaced;
            for (int i = 0; i < TreeGuardrails.AreaZones + 1; i++)
            {
                TreeEvents.RaiseProc(Proc(i % 2 == 0 ? TreeProcKind.FireGround : TreeProcKind.VenomCloud, Konum(60f + 5f * i, 60f)));
            }

            Assert.AreEqual(TreeGuardrails.AreaZones, t.ZoneCount, "zon tavanı 16");
            Assert.AreEqual(yer + 1, t.ZonesReplaced);
            yield return null;
            Assert.LessOrEqual(t.FieldLoopsActive, AudioMixPlan.TreeFieldVoices, "sesli döngü ≤ 3");
            t.StopAll();

            // Black hole: open → collapse.
            int cokme = t.PlayedOf(AudioSoundId.TreeBlackHoleCollapse);
            TreeEvents.RaiseProc(Proc(TreeProcKind.BlackHoleOpen, Konum(4f, 4f)));
            yield return null;
            Assert.IsTrue(t.BlackHoleActive);
            TreeEvents.RaiseProc(Proc(TreeProcKind.BlackHoleCollapse, Konum(4f, 4f)));
            Assert.AreEqual(cokme + 1, t.PlayedOf(AudioSoundId.TreeBlackHoleCollapse));
            Assert.IsFalse(t.BlackHoleActive);
            yield return new WaitForSecondsRealtime(AudioMixPlan.TreeHoleCollapseFade + 0.15f);
            Assert.IsFalse(t.BlackHoleSounding, "çökmede döngü 120 ms'de kesilir");

            // Black hole without a collapse event: it ends with its life (HoleSeconds + grace), no collapse sound.
            int sure = t.HoleTimeouts;
            cokme = t.PlayedOf(AudioSoundId.TreeBlackHoleCollapse);
            TreeEvents.RaiseProc(Proc(TreeProcKind.BlackHoleOpen, Konum(4f, 4f)));
            yield return new WaitForSecondsRealtime(1f + AudioMixPlan.TreeHoleGrace + AudioMixPlan.TreeLoopFadeOut + 0.3f);
            Assert.AreEqual(sure + 1, t.HoleTimeouts, "çökme gelmezse ömür sonunda kapanır");
            Assert.IsFalse(t.BlackHoleSounding);
            Assert.AreEqual(cokme, t.PlayedOf(AudioSoundId.TreeBlackHoleCollapse), "duyulmayan çökme çalınmaz");
            Debug.Log("[DD-TEST] wp104.audio loops " + t.Describe());
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Agac_SikSesLod_AyniKareTek_SaniyedeN_CanliTavan_NukeAraligi_Doomsday()
        {
            if (!_d.Library.IsProduced(AudioSoundId.TreeCrit) || !_d.Library.IsProduced(AudioSoundId.TreeMiniNuke))
            {
                Assert.Fail("v024 ağaç kimlikleri katalogda değil (veri katmanı: ap011_r4_import + AudioCatalogBuilder).");
            }

            TreeAudio t = _d.Tree;

            // ---- the same frame: ten crits (Crit and CritRing share TreeCrit) are one voice
            int crit = t.PlayedOf(AudioSoundId.TreeCrit), kare = t.RejectedFrame;
            for (int i = 0; i < 10; i++)
            {
                TreeEvents.RaiseProc(Proc(i % 2 == 0 ? TreeProcKind.Crit : TreeProcKind.CritRing, Konum(i, 2f)));
            }

            Assert.AreEqual(crit + 1, t.PlayedOf(AudioSoundId.TreeCrit), "aynı karede 10 Crit → 1 ses");
            Assert.AreEqual(kare + 9, t.RejectedFrame);

            // ---- the live ceiling: a third crit while two ring is refused (hold = the first clip's length)
            float hold = t.HoldSecondsOf(AudioSoundId.TreeCrit);
            float ilk = t.LastPlayedAt(AudioSoundId.TreeCrit);
            int dusen = t.Dropped;
            // The gate's own clock (unscaled frame time), not real time: a long batch frame can resume a realtime wait before the
            // frame clock has moved past Crit's 0.06 s interval, and the interval gate - not the live ceiling - refuses the crit.
            while (Time.unscaledTime - ilk < 0.08f) yield return null;
            TreeEvents.RaiseProc(Proc(TreeProcKind.Crit, Konum(1f, 2f)));
            Assert.AreEqual(crit + 2, t.PlayedOf(AudioSoundId.TreeCrit), "ikinci Crit çalar · " + t.Describe());
            float ikinci = t.LastPlayedAt(AudioSoundId.TreeCrit);
            while (Time.unscaledTime - ikinci < 0.08f) yield return null;
            int canli = t.RejectedLive;
            TreeEvents.RaiseProc(Proc(TreeProcKind.Crit, Konum(2f, 2f)));
            if (Time.unscaledTime - ilk < hold)
            {
                Assert.AreEqual(dusen, t.Dropped, "havuz Crit düşürmedi (ölçülen kapı TreeAudio'nun canlı tavanı)");
                Assert.AreEqual(crit + 2, t.PlayedOf(AudioSoundId.TreeCrit), "iki Crit canlı");
                Assert.AreEqual(canli + 1, t.RejectedLive, "üçüncü Crit canlı tavanında (2) reddedilir · " + t.Describe());
            }
            else
            {
                Debug.Log("[DD-TEST] wp104.audio live-cap check skipped: frames outran the crit hold " + hold.ToString("0.00") + " s");
            }

            // ---- the rate: one Leech per frame for 0.9 s → never more than its PerSecond
            AudioVoiceLod leech = AudioMixPlan.TreeLod(AudioSoundId.TreeLifesteal);
            int l0 = t.PlayedOf(AudioSoundId.TreeLifesteal), oran = t.RejectedRate, istek = 0;
            float basla = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - basla < 0.9f)
            {
                TreeEvents.RaiseProc(Proc(TreeProcKind.Leech, Konum(0f, 1f)));
                istek++;
                yield return null;
            }

            int kabul = t.PlayedOf(AudioSoundId.TreeLifesteal) - l0;
            Assert.LessOrEqual(kabul, leech.PerSecond, "Leech saniyede ≤ " + leech.PerSecond + " (istek " + istek + ")");
            Assert.Greater(kabul, 0);
            Assert.Greater(t.RejectedRate + t.RejectedInterval + t.RejectedLive, oran, "fazlası reddedildi");

            // ---- Mini Nuke: ≤ 1 per 3 s; a short duck; one a little early is held and plays when the gate opens
            int nuke = t.PlayedOf(AudioSoundId.TreeMiniNuke), held = t.Held, heldPlayed = t.HeldPlayed;
            TreeEvents.RaiseProc(Proc(TreeProcKind.MiniNuke, Konum(6f, 6f)));
            Assert.AreEqual(nuke + 1, t.PlayedOf(AudioSoundId.TreeMiniNuke));
            float n1 = t.LastPlayedAt(AudioSoundId.TreeMiniNuke);
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.Less(_d.Mixer.DuckLevel(AudioBus.Ambience), 0.9f, "Mini Nuke yatakları kısar");
            yield return new WaitForSecondsRealtime(0.4f);
            TreeEvents.RaiseProc(Proc(TreeProcKind.MiniNuke, Konum(6f, 6f)));
            Assert.AreEqual(nuke + 1, t.PlayedOf(AudioSoundId.TreeMiniNuke), "3 s içinde ikinci Nuke yok");
            Assert.AreEqual(held, t.Held, "çok erken gelen bekletilmez (≤ 0,6 s erken olan bekler)");
            while (Time.unscaledTime - n1 < 1.0f) yield return null;
            Assert.Greater(_d.Mixer.DuckLevel(AudioBus.Ambience), 0.95f, "kısa duck: 1 s içinde geri döner");

            while (Time.unscaledTime - n1 < 2.6f) yield return null;
            bool erken = Time.unscaledTime - n1 < 2.95f;   // a frame long enough to pass 3 s plays it at once instead
            TreeEvents.RaiseProc(Proc(TreeProcKind.MiniNuke, Konum(6f, 6f)));
            if (erken)
            {
                Assert.AreEqual(held + 1, t.Held, "kapıdan biraz önce gelen Nuke bekletilir");
                Assert.AreEqual(nuke + 1, t.PlayedOf(AudioSoundId.TreeMiniNuke));
            }
            float bitis = Time.realtimeSinceStartup + 2f;
            while (t.PlayedOf(AudioSoundId.TreeMiniNuke) < nuke + 2 && Time.realtimeSinceStartup < bitis) yield return null;
            Assert.AreEqual(nuke + 2, t.PlayedOf(AudioSoundId.TreeMiniNuke), "bekletilen Nuke kapı açılınca çalar");
            if (erken)
            {
                Assert.AreEqual(heldPlayed + 1, t.HeldPlayed);
            }
            Assert.GreaterOrEqual(t.LastPlayedAt(AudioSoundId.TreeMiniNuke) - n1, 3f - 1e-3f, "≤ 1 Nuke / 3 s");

            // ---- Doomsday: ≤ 1 alive (its 8 s bell)
            int bell = t.PlayedOf(AudioSoundId.TreeDoomsdayBell);
            TreeEvents.RaiseProc(Proc(TreeProcKind.DoomsdayBell, Konum(-6f, 6f)));
            Assert.AreEqual(bell + 1, t.PlayedOf(AudioSoundId.TreeDoomsdayBell));
            yield return new WaitForSecondsRealtime(1.2f);
            canli = t.RejectedLive;
            TreeEvents.RaiseProc(Proc(TreeProcKind.DoomsdayBell, Konum(-6f, 6f)));
            Assert.AreEqual(bell + 1, t.PlayedOf(AudioSoundId.TreeDoomsdayBell), "çan çalarken ikinci Doomsday yok");
            Assert.AreEqual(canli + 1, t.RejectedLive);
            Debug.Log("[DD-TEST] wp104.audio lod leech=" + kabul + "/" + istek + " crit-hold=" + hold.ToString("0.00") + " " + t.Describe());
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Agac_EkranOlaylari_Damga_Sinerji_AuraYalnizYukseliste()
        {
            TreeAudio t = _d.Tree;
            int damga = t.PlayedOf(AudioSoundId.TreeStamp);
            TreeEvents.RaiseNodePurchased("sh_double", TreeBranch.Shot, 1);
            Assert.AreEqual(damga + 1, t.PlayedOf(AudioSoundId.TreeStamp));
            Assert.AreEqual(Time.unscaledTime, _d.SignalPool.LastPlayedAt(AudioSoundId.TreeStamp), 1e-5f, "damga arayüz havuzunda");

            int sinerji = t.PlayedOf(AudioSoundId.TreeSynergy);
            TreeEvents.RaiseSynergyUnlocked(DD.Prototype.Rounds.TreeSynergy.PinballStorm);
            Assert.AreEqual(sinerji + 1, t.PlayedOf(AudioSoundId.TreeSynergy));
            Assert.Greater(AudioMixPlan.Tree(AudioSoundId.TreeSynergy).Delay, 0f, "sinerji damganın ardından");

            int aura = t.PlayedOf(AudioSoundId.TreeAuraUp);
            TreeEvents.RaiseAuraChanged(1);
            Assert.AreEqual(aura + 1, t.PlayedOf(AudioSoundId.TreeAuraUp), "aura 0 → 1");
            yield return new WaitForSecondsRealtime(0.35f);
            TreeEvents.RaiseAuraChanged(1);
            TreeEvents.RaiseAuraChanged(0);
            Assert.AreEqual(aura + 1, t.PlayedOf(AudioSoundId.TreeAuraUp), "aynı seviye / düşüş sessiz");
            TreeEvents.RaiseAuraChanged(2);
            Assert.AreEqual(aura + 2, t.PlayedOf(AudioSoundId.TreeAuraUp), "0 → 2 yükseliş");
            t.ResetRun();
            Assert.AreEqual(0, t.AuraLevel, "yeni koşu auraya 0'dan başlar");
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Agac_Abonelik_RearmResetForTestsDirectorYenidenEtkin_TekDinleyici()
        {
            TreeAudio t = _d.Tree;
            TreeProcInfo sessiz = Proc(TreeProcKind.OverclockPeak, Konum(1f, 1f));   // a silent kind: only the handler count moves
            int n = t.ProcEvents, e = t.ScreenEvents;

            TreeEvents.RaiseProc(sessiz);
            Assert.AreEqual(n + 1, t.ProcEvents, "kurulumdan sonra tek dinleyici (sahne değişimi Rearm'ı dahil)");

            t.Rearm();
            t.Rearm();
            TreeEvents.RaiseProc(sessiz);
            TreeEvents.RaiseAuraChanged(0);
            Assert.AreEqual(n + 2, t.ProcEvents, "iki Rearm sonrası yine tek dinleyici");
            Assert.AreEqual(e + 1, t.ScreenEvents);

            // An S-04 / S-07 test SetUp clears the bus under the live director: the next scene change (Rearm) brings it back.
            TreeEvents.ResetForTests();
            TreeEvents.RaiseProc(sessiz);
            Assert.AreEqual(n + 2, t.ProcEvents, "öncül: ResetForTests busu boşalttı");
            t.Rearm();
            TreeEvents.RaiseProc(sessiz);
            Assert.AreEqual(n + 3, t.ProcEvents, "Rearm sıfırlanan busa tek dinleyiciyle döner");

            _d.enabled = false;
            Assert.IsFalse(t.Subscribed);
            TreeEvents.RaiseProc(sessiz);
            Assert.AreEqual(n + 3, t.ProcEvents, "director kapalıyken dinleyici yok");
            _d.enabled = true;
            yield return null;
            TreeEvents.RaiseProc(sessiz);
            Assert.AreEqual(n + 4, t.ProcEvents, "director yeniden etkin: tek dinleyici");
            Assert.IsTrue(t.Subscribed);
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Agac_SesYolu_KararliDurumda_SifirAyirma()
        {
            TreeAudio t = _d.Tree;
            KisaOmur(0.5f);
            TreeProcInfo crit = Proc(TreeProcKind.Crit, Konum(2f, 2f));
            TreeProcInfo leech = Proc(TreeProcKind.Leech, Konum(0f, 1f));
            TreeProcInfo shatter = Proc(TreeProcKind.Shatter, Konum(3f, 1f));
            TreeProcInfo atesA = Proc(TreeProcKind.FireGround, Konum(4f, 0f));
            TreeProcInfo atesB = Proc(TreeProcKind.FireGround, Konum(-4f, 0f));
            TreeProcInfo zehir = Proc(TreeProcKind.VenomCloud, Konum(0f, 6f));
            TreeProcInfo delik = Proc(TreeProcKind.BlackHoleOpen, Konum(5f, 5f));
            TreeProcInfo cokme = Proc(TreeProcKind.BlackHoleCollapse, Konum(5f, 5f));

            // Warm-up: every path's first call (clip deck, hold cache, loop clip) may allocate.
            for (int i = 0; i < 3; i++)
            {
                TreeEvents.RaiseProc(crit);
                TreeEvents.RaiseProc(leech);
                TreeEvents.RaiseProc(shatter);
                TreeEvents.RaiseProc(atesA);
                TreeEvents.RaiseProc(atesB);
                TreeEvents.RaiseProc(zehir);
                TreeEvents.RaiseProc(delik);
                TreeEvents.RaiseProc(cokme);
                TreeEvents.RaiseNodePurchased("sh_double", TreeBranch.Shot, 1);
                yield return new WaitForSecondsRealtime(0.3f);
            }

            TestDelegate blok = () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    TreeEvents.RaiseProc(crit);
                    TreeEvents.RaiseProc(leech);
                    TreeEvents.RaiseProc(shatter);
                    TreeEvents.RaiseProc(i % 2 == 0 ? atesA : atesB);
                    TreeEvents.RaiseProc(zehir);
                    TreeEvents.RaiseProc(delik);
                    if (i % 10 == 9) TreeEvents.RaiseProc(cokme);
                    TreeEvents.RaiseNodePurchased("sh_double", TreeBranch.Shot, 1);
                    t.Tick(0.016f);
                }
            };

            // The stamp is the one Interface sound here: its first start on a signal voice the warm-up left unused allocates once
            // (S-08 probe 12.10, K/wp-097/evidence/r4a-probe2: frame 0 only; the next five frames' crit / stamp / UI starts are clean).
            // The block runs once unmeasured, then is measured a frame later.
            blok();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(blok, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "ağaç ses yolu 800 olay + 100 Tick'te ayırdı");
            Debug.Log("[DD-TEST] wp104.audio gc=none over 800 tree events + 100 ticks · " + t.Describe());
        }
    }
}
