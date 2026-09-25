using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Boot;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 slice 10a · the async loading flow (UI v3 direction §15, T15–T17). Real scene
    /// loads with the curtain's real timings (FastTransitions off): the doors never open
    /// before the new scene is active, the ring only moves forward and never past 0.9 before
    /// activation, the loading group stays ≥ 1.2 s, the frozen world is released only at
    /// activation, a scene measured as fast gets the CURTAIN depth (no group), a second
    /// Load() while one is in flight is ignored, and the measured time is written back to
    /// load-timing.json (a temp copy here) - never to ui-settings.json.
    /// </summary>
    [Category("WP068")]
    public sealed class LoadingScreenTests
    {
        private string _settingsPath;
        private string _timingPath;
        private bool _fast;

        [SetUp]
        public void SetUp()
        {
            _fast = PrototypeUiFlow.FastTransitions;
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-wp068-loading-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
            _timingPath = Path.Combine(Path.GetTempPath(), "dd-wp068-load-timing-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiLoadTiming.SetPathOverride(_timingPath);
        }

        [TearDown]
        public void TearDown()
        {
            PrototypeUiFlow.FastTransitions = _fast;
            PrototypeUiFlow.ArtificialLoadSeconds = 0f;
            RunFlowClock.ClearHolds();
            UiSettings.SetPathOverride(null);
            UiLoadTiming.SetPathOverride(null);
            foreach (string path in new[] { _settingsPath, _timingPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Ekran_KapilarSahneEtkinOlmadanAcilmaz_HalkaIleriGider_AsgariGosterim_DunyaAktivasyondaCozulur()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 5f); // measured as slow → SCREEN depth
            RunFlowClock.AddHold("wp068-loading-test");

            PrototypeUiFlow.GoToSelection();
            Assert.IsTrue(PrototypeUiFlow.IsLoading, "yükleme uçuşta");
            Assert.IsTrue(UiCurtain.IsCovering, "kapılar istek anında kapanmaya başlar");

            UiCurtain curtain = UiCurtain.Instance;
            float lastFill = 0f;
            float groupFrom = -1f, enteringAt = -1f;
            bool heldWhileGroup = false, sawTip = false, sawPlate = false, sawWetBand = false, wetCream = false, sealAtRest = false;
            float deadline = Time.realtimeSinceStartup + 60f;
            // IsLoading ends at activation; the doors open after it - watch until both are done.
            while ((PrototypeUiFlow.IsLoading || UiCurtain.IsCovering) && Time.realtimeSinceStartup < deadline)
            {
                string active = SceneManager.GetActiveScene().name;
                if (!UiCurtain.IsCovering)
                {
                    Assert.AreEqual(PrototypeScenes.Selection, active, "kapılar yeni sahne etkin olmadan açıldı");
                }

                if (curtain.GroupVisible)
                {
                    sawTip |= !string.IsNullOrEmpty(curtain.TipText);
                    sawPlate |= curtain.PlateVisible;
                    if (groupFrom < 0f) groupFrom = Time.realtimeSinceStartup;
                    Assert.GreaterOrEqual(curtain.RingFill, lastFill - 1e-4f, "halka geri gitmez");
                    lastFill = curtain.RingFill;
                    if (curtain.EyebrowText == "LOADING")
                    {
                        // AD 10b-2 MUST-1 / T15: the ring's extent never passes the progress, on any frame.
                        Assert.LessOrEqual(curtain.WetFill, curtain.ShownProgress + 1e-4f, "ıslak uç ilerlemenin önüne geçmez");
                        Assert.LessOrEqual(curtain.RingFill, Mathf.Max(0f, curtain.ShownProgress - UiCurtain.WetBand) + 1e-4f, "kuru darbe ilerlemenin 0,04 gerisinde");
                    }

                    if (curtain.EyebrowText == "LOADING" && curtain.ShownProgress > UiCurtain.WetBand)
                    {
                        sawWetBand |= Mathf.Abs(curtain.ShownProgress - curtain.RingFill - UiCurtain.WetBand) < 1e-3f
                                      && curtain.WetFill >= curtain.ShownProgress - UiCurtain.WetTremble - 1e-3f;
                        Color wet = curtain.WetColor;
                        wetCream |= Mathf.Abs(wet.a - UiCurtain.WetAlpha) < 0.02f && wet.r > 0.8f;
                        sealAtRest |= Mathf.Abs(curtain.SealColor.a - UiCurtain.SealRestAlpha) < 0.02f;
                    }
                    if (curtain.EyebrowText == "LOADING")
                    {
                        Assert.LessOrEqual(curtain.RingFill, UiCurtain.LoadShare + 1e-4f, "aktivasyondan önce halka 0,9'u geçmez");
                        heldWhileGroup |= RunFlowClock.HasHold("wp068-loading-test");
                    }
                    else if (enteringAt < 0f)
                    {
                        enteringAt = Time.realtimeSinceStartup;
                    }
                }

                yield return null;
            }

            Assert.IsFalse(PrototypeUiFlow.IsLoading, "yükleme 60 s içinde bitti");
            Assert.AreEqual(PrototypeScenes.Selection, SceneManager.GetActiveScene().name);
            Assert.IsFalse(UiCurtain.IsCovering, "yükleme bitince perde açık");
            Assert.Greater(groupFrom, 0f, "SCREEN derinliği: grup göründü");
            Assert.Greater(enteringAt, 0f, "ENTERING durumu gösterildi");
            Assert.GreaterOrEqual(enteringAt - groupFrom, PrototypeUiFlow.MinimumDisplaySeconds - 0.05f, "grup asgari gösterim süresi");
            Assert.IsTrue(heldWhileGroup, "dünya yükleme boyunca donuk kaldı (ReleaseSimulation aktivasyonda)");
            Assert.AreEqual(0, RunFlowClock.HoldCount, "aktivasyonda dünya çözüldü");
            Assert.Greater(UiLoadTiming.LastLoadSeconds(PrototypeScenes.Selection), 0f, "ölçülen süre geri yazıldı");
            Assert.Less(UiLoadTiming.LastLoadSeconds(PrototypeScenes.Selection), 5f, "ölçüm eski değerin yerine geçti");
            Assert.IsTrue(File.Exists(_timingPath), "ölçüm load-timing.json'a yazıldı");
            Assert.IsFalse(File.Exists(_settingsPath), "yükleme ui-settings.json yazmaz (yalnız oyuncu ayar değiştirince)");
            Assert.IsTrue(sawTip, "10b: MainMenu → Selection ekranında ipucu şeridi");
            Assert.IsTrue(sawPlate, "10b: SCREEN derinliği Look B plakası üstünde");
            Assert.IsTrue(sawWetBand, "10b-2: ıslak uç darbenin son 0,04'ü, ilerlemede biter");
            Assert.IsTrue(wetCream, "AD 10b-2 MUST-1: ıslak uç krem α 0,6");
            Assert.IsTrue(sealAtRest, "10b-2: mühür yüklenirken dinlenme mürekkebinde (α 0,35)");
            Assert.Greater(curtain.SealColor.a, 0.95f, "10b-2: aktivasyonda mühür damgalanır (TextPrimary)");
        }

        /// <summary>
        /// R1-D (N20): the way into the run and the way to the Results show their painted key art over the plates (the
        /// plates step aside); the menus keep the plates; the ring-only depth shows neither.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AnahtarSanat_KosuVeResults_MenulerPlaka_HalkaYalnizHicbiri()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            UiCurtain curtain = UiCurtain.Instance;
            Assert.IsNotNull(curtain);
            Assert.IsNotNull(UiCurtain.KeyArtFor(PrototypeScenes.Run));
            Assert.IsNotNull(UiCurtain.KeyArtFor(PrototypeScenes.Results));
            Assert.IsNull(UiCurtain.KeyArtFor(PrototypeScenes.MainMenu), "the menus keep the Look B plates");

            curtain.ShowGroup(UiCurtain.DestinationFor(PrototypeScenes.Run), string.Empty, null, true, UiCurtain.KeyArtFor(PrototypeScenes.Run));
            yield return null;
            Assert.IsTrue(curtain.KeyArtVisible, "the run's key art shows");
            Assert.IsFalse(curtain.PlateVisible, "the plates step aside under the opaque art");

            curtain.ShowGroup(UiCurtain.DestinationFor(PrototypeScenes.MainMenu), string.Empty, null, false, UiCurtain.KeyArtFor(PrototypeScenes.MainMenu));
            yield return null;
            Assert.IsFalse(curtain.KeyArtVisible, "no key art for the menu");
            Assert.IsTrue(curtain.PlateVisible, "the menu keeps its plate");

            curtain.ShowGroup(UiCurtain.DestinationFor(PrototypeScenes.Results), string.Empty, null, false, UiCurtain.KeyArtFor(PrototypeScenes.Results));
            yield return null;
            Assert.IsTrue(curtain.KeyArtVisible, "the Results' key art shows");
            curtain.ShowRingOnly();
            yield return null;
            Assert.IsFalse(curtain.KeyArtVisible, "the ring-only depth shows no art");
            curtain.Open(true);
            yield return null;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AzHareket_GrupT0daGorunur_AsgariGosterimTutulur()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            bool reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = true;
            try
            {
                UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 5f); // SCREEN by the table
                float t0 = Time.realtimeSinceStartup;
                PrototypeUiFlow.GoToSelection();
                UiCurtain curtain = UiCurtain.Instance;
                Assert.IsTrue(curtain.GroupVisible, "RM: grup t0'da dinlenmede (kapılar kesildi)");
                Assert.IsNotEmpty(curtain.TipText, "RM: ipucu şeridi RM satırını taşır");
                float enteringAt = -1f;
                float deadline = Time.realtimeSinceStartup + 60f;
                while ((PrototypeUiFlow.IsLoading || UiCurtain.IsCovering) && Time.realtimeSinceStartup < deadline)
                {
                    if (enteringAt < 0f && curtain.EyebrowText == "ENTERING") enteringAt = Time.realtimeSinceStartup;
                    yield return null;
                }

                Assert.Greater(enteringAt, 0f, "RM: ENTERING gösterildi");
                Assert.GreaterOrEqual(enteringAt - t0, PrototypeUiFlow.MinimumDisplaySeconds - 0.05f, "RM: asgari gösterim tutulur");
                Assert.AreEqual(PrototypeScenes.Selection, SceneManager.GetActiveScene().name);
            }
            finally
            {
                UiMotion.ReducedMotion = reduced;
            }
        }

        private static IEnumerator OlumeKadar()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            PrototypeUiFlow.FastTransitions = false;
            DD.Prototype.PlayerSystem.PlayerRig rig = DD.Prototype.PlayerSystem.PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = false;
            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp068-ink-test");
        }

        /// <summary>
        /// 10b-2 · E22 / §15.1 ink variant: a death bleeds ink from t 0.6 of the beat; the
        /// Results load runs under it - no doors, no plate group - and Results opens as the ink
        /// recedes.
        /// </summary>
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Olum_MurekkepVaryanti_KapiYok_ResultsMurekkeptenAcilir()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return OlumeKadar();
            UiCurtain curtain = UiCurtain.Instance;
            bool sawInk = false, sawDoors = false, sawPlateGroup = false;
            int resultsDrawnUnderInk = 0;
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!(SceneManager.GetActiveScene().name == PrototypeScenes.Results && !UiCurtain.IsCovering) && Time.realtimeSinceStartup < deadline)
            {
                sawInk |= curtain.InkCovering;
                sawDoors |= curtain.DoorsShut && curtain.InkCovering;
                sawPlateGroup |= curtain.GroupVisible && curtain.PlateVisible;
                if (curtain.InkCovering && !curtain.InkReceding && ResultsDrawn())
                {
                    resultsDrawnUnderInk++;
                }

                yield return null;
            }

            yield return Bekle(() => !curtain.InkCovering, 3f, "mürekkep çekilmedi");   // Revealed fires half way through the recede
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(PrototypeScenes.Results, SceneManager.GetActiveScene().name, "Results açıldı");
            Assert.IsTrue(sawInk, "ölüm vuruşunda mürekkep sızdı");
            Assert.IsFalse(sawDoors, "mürekkep varyantında kapı çizilmez");
            Assert.IsFalse(sawPlateGroup, "mürekkep varyantında plaka grubu yok");
            Assert.IsFalse(curtain.InkCovering, "açılışta mürekkep çekildi");
            Assert.GreaterOrEqual(resultsDrawnUnderInk, 1, "AD 10b-2 MUST-2: Results mürekkep çekilmeden önce en az bir kare çizili (çekilme boş alan açmaz)");
        }

        /// <summary>The Results screen is on screen at rest: its canvas and its three blocks (tiles, report, rewards) fully opaque.</summary>
        private static bool ResultsDrawn()
        {
            ResultsScreen screen = Object.FindObjectOfType<ResultsScreen>();
            if (screen == null)
            {
                return false;
            }

            CanvasGroup[] groups = screen.GetComponentsInChildren<CanvasGroup>(true);
            int columns = 0;
            foreach (CanvasGroup group in groups)
            {
                bool column = group.name == "Tiles" || group.name == "Report" || group.name == "Rewards";   // R1-D Results v3 blocks
                if ((column || group.GetComponent<Canvas>() != null) && group.alpha < 0.99f)
                {
                    return false;
                }

                columns += column ? 1 : 0;
            }

            return columns == 3;
        }

        /// <summary>10b-2 · WP-061J under the ink: an abort during the death beat still wins - the doors close under the ink, the menu opens, no Results.</summary>
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Olum_MurekkepAltindaIptal_AnaMenu_ResultsYok()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return OlumeKadar();
            UiCurtain curtain = UiCurtain.Instance;
            yield return Bekle(() => curtain.InkCovering, 5f, "mürekkep başlamadı");
            PrototypeUiFlow.AbortRunToMainMenu();
            bool sawResults = false;
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!(SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && !UiCurtain.IsCovering) && Time.realtimeSinceStartup < deadline)
            {
                sawResults |= SceneManager.GetActiveScene().name == PrototypeScenes.Results;
                yield return null;
            }

            yield return new WaitForSecondsRealtime(3f);   // the death beat's own Results hand-off must not fire
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name);
            Assert.IsFalse(sawResults, "iptal Results'u geçersiz kıldı");
            Assert.IsFalse(curtain.InkCovering, "mürekkep kalmadı");
        }

        /// <summary>
        /// 10b-2 · E1 / §15.3 (U7): the boot seal is the first loading screen. No doors, the
        /// vermilion seal stamps with one BootLogo bell, the hold lasts 1.6 s, then the stamp is
        /// the activation and the main menu opens.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Boot_MuhurIlkYuklemeEkranidir_TekCan_Tutma16_AnaMenu()
        {
            PrototypeUiFlow.FastTransitions = false;
            int bells = 0;
            System.Action<UiEvent, string> count = (e, d) => { if (e == UiEvent.BootLogo) bells++; };
            UiEvents.Raised += count;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
                yield return Bekle(() => UiCurtain.Instance != null && UiCurtain.Instance.BootVisible, 10f, "mühür görünmedi");
                Assert.IsFalse(UiCurtain.Instance.GroupVisible && UiCurtain.Instance.PlateVisible, "mühür varyantında plaka yok");
                yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && !UiCurtain.IsCovering, 30f, "ana menü açılmadı");
                float revealed = Time.realtimeSinceStartup - t0;
                Assert.AreEqual(1, bells, "tek BootLogo çanı (mühür teması)");
                Assert.GreaterOrEqual(revealed, PrototypeUiFlow.BootHoldSeconds - 0.1f, "mühür koreografisi 1,6 s tutar");
            }
            finally
            {
                UiEvents.Raised -= count;
            }
        }

        /// <summary>10b-2 · a skip (Submit / Cancel / click) lands the choreography at once; a ready menu opens early, never faked.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Boot_Atlama_KoreografiyiBitirir_HazirMenuErkenAcilir()
        {
            PrototypeUiFlow.FastTransitions = false;
            float t0 = Time.realtimeSinceStartup;
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return Bekle(() => UiCurtain.Instance != null && UiCurtain.Instance.BootVisible, 10f, "mühür görünmedi");
            UiCurtain.Instance.RequestBootSkip();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && !UiCurtain.IsCovering, 30f, "ana menü açılmadı");
            float revealed = Time.realtimeSinceStartup - t0;
            Assert.Less(revealed, PrototypeUiFlow.BootHoldSeconds + 1f, "atlama tutmayı kısaltır (yükleme hazırsa)");
            Assert.AreEqual(1f, UiCurtain.Instance.SealColor.a, 0.05f, "atlama: mühür t = 1'de (damgalı)");
        }

        /// <summary>
        /// §15.2 "slower than expected": a scene measured fast gets the doors only (CURTAIN); if
        /// the load is still running at 0.9 s the full group enters late and holds its minimum
        /// display from there.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Perde_BeklenendenYavas_Grup09daGecGirer_AsgariGosterimGiristen()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 0.2f);   // measured fast → CURTAIN
            PrototypeUiFlow.ArtificialLoadSeconds = 2f;                  // … but this load is slow

            float t0 = Time.realtimeSinceStartup;
            PrototypeUiFlow.GoToSelection();
            UiCurtain curtain = UiCurtain.Instance;
            float groupFrom = -1f, enteringAt = -1f;
            float deadline = t0 + 60f;
            while ((PrototypeUiFlow.IsLoading || UiCurtain.IsCovering) && Time.realtimeSinceStartup < deadline)
            {
                if (curtain.GroupVisible && groupFrom < 0f) groupFrom = Time.realtimeSinceStartup;
                if (curtain.GroupVisible && curtain.EyebrowText == "ENTERING" && enteringAt < 0f) enteringAt = Time.realtimeSinceStartup;
                yield return null;
            }

            Assert.Greater(groupFrom, 0f, "yavaş CURTAIN yüklemesinde grup geç girdi");
            Assert.GreaterOrEqual(groupFrom - t0, PrototypeUiFlow.CurtainThresholdSeconds - 0.05f, "grup 0,9 s'den önce girmedi");
            Assert.Greater(enteringAt, 0f, "ENTERING gösterildi");
            Assert.GreaterOrEqual(enteringAt - groupFrom, PrototypeUiFlow.MinimumDisplaySeconds - 0.05f, "asgari gösterim girişten sayılır");
            Assert.AreEqual(PrototypeScenes.Selection, SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Perde_HizliSahne_GrupGorunmez_KapilarEnAzKapali()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 0.2f); // measured as fast → CURTAIN depth

            float t0 = Time.realtimeSinceStartup;
            PrototypeUiFlow.GoToSelection();
            UiCurtain curtain = UiCurtain.Instance;
            bool groupSeen = false;
            float revealedAt = -1f;
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                groupSeen |= curtain.GroupVisible;
                if (!PrototypeUiFlow.IsLoading && !UiCurtain.IsCovering)
                {
                    revealedAt = Time.realtimeSinceStartup;
                    break;
                }

                yield return null;
            }

            Assert.Greater(revealedAt, 0f, "perde 60 s içinde açıldı");

            // A real load slower than 0.9 s escalates to the late group (§15.2, the test above);
            // only a load that really was fast may keep the doors alone.
            float measured = UiLoadTiming.LastLoadSeconds(PrototypeScenes.Selection);
            if (measured < PrototypeUiFlow.CurtainThresholdSeconds)
            {
                Assert.IsFalse(groupSeen, "hızlı yüklemede CURTAIN derinliğinde grup (halka + etiketler) görünmez");
            }
            else
            {
                Debug.Log("[DD-TEST] wp068.curtain real load " + measured.ToString("0.00") + " s ≥ 0.9: escalation path (covered by the slow-curtain test)");
            }
            Assert.AreEqual(PrototypeScenes.Selection, SceneManager.GetActiveScene().name);
            Assert.GreaterOrEqual(revealedAt - t0, PrototypeUiFlow.MinimumClosedSeconds, "kapılar en az 0,3 s kapalı");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator TekYukleme_IkinciIstekYokSayilir()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();

            PrototypeUiFlow.GoToSelection();
            LogAssert.Expect(LogType.Log, new Regex("ignored: a scene is still loading"));
            PrototypeUiFlow.GoToResults();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return Bekle(() => !PrototypeUiFlow.IsLoading, 10f, "yükleme bitmedi");
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(PrototypeScenes.Selection, SceneManager.GetActiveScene().name, "ikinci istek Results'a gitmedi");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Takilma_Bildirim_VeHataModali_BirincilEtiket_IptalYok()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            UiCurtain curtain = UiCurtain.Instance;
            curtain.Close(true);
            curtain.ShowGroup("ROUND 1", "ASSAULT RIFLE", "Unspent gold carries into the next round.", true);
            yield return null;
            Assert.AreEqual("ASSAULT RIFLE", curtain.SublineText, "koşuya giden alt satır silah adı");
            Assert.IsTrue(curtain.MidPlateVisible, "koşuya giderken orta plaka (fenerler)");
            curtain.ShowStalled();
            Assert.IsTrue(curtain.StallVisible, "10 s takılma bildirimi");

            bool primary = false;
            curtain.ShowError(PrototypeScenes.Run, false, "RETURN TO MAIN MENU", () => primary = true);
            Assert.IsTrue(curtain.ErrorOpen, "hata modalı açık");
            Assert.AreEqual("the run", UiCurtain.SentenceFor(PrototypeScenes.Run));
            curtain.HideError();
            Assert.IsFalse(curtain.ErrorOpen);
            Assert.IsFalse(primary, "birincil eylem yalnız basınca");
            curtain.Open(true);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AktivasyondanSonrakiIstek_AcilisiDevralir()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false; // a real reveal is under way when the next request lands
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 0.2f);
            UiLoadTiming.RecordLoad(PrototypeScenes.MainMenu, 0.2f);
            bool cursor = UiCursor.ContextVisible;

            PrototypeUiFlow.GoToSelection();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, 30f, "Selection etkin olmadı");
            Assert.IsFalse(PrototypeUiFlow.IsLoading, "yükleme aktivasyonda biter (açılış yüklemeye dahil değil)");
            Assert.IsTrue(UiCurtain.IsCovering, "açılış henüz sürüyor");

            PrototypeUiFlow.GoToMainMenu(); // the pattern of every flow suite: next step as soon as the scene is live
            Assert.IsTrue(PrototypeUiFlow.IsLoading, "açılış sırasındaki istek kabul edildi");
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
            Assert.AreEqual(cursor, UiCursor.ContextVisible, "imleç ilk kapanıştaki durumuna döndü");
        }

        /// <summary>
        /// User-data rule: a run through the real flow leaves the player's own UI files
        /// exactly as it found them. The run measures a real load, picks a tip and changes a
        /// setting, and all of it lands in the process-local folder (TestUiDataIsolation).
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Kosu_GercekKullaniciDosyalarinaDokunmaz()
        {
            string before = TestUiDataIsolation.RealFiles();
            if (UiDataPaths.RunsUnderTestRunner)
            {
                // What a test assembly WITHOUT the fixture sees under -runTests: still process-local.
                UiDataPaths.SetRootOverride(null);
                StringAssert.StartsWith(UiDataPaths.ProcessLocalRoot, UiSettings.FilePath, "-runTests: varsayılan kök süreç-yerel");
                UiDataPaths.UseProcessLocalRoot();
            }

            UiSettings.SetPathOverride(null); // the run's own paths: the process-local root
            UiLoadTiming.SetPathOverride(null);
            StringAssert.StartsWith(UiDataPaths.ProcessLocalRoot, UiSettings.FilePath);
            StringAssert.StartsWith(UiDataPaths.ProcessLocalRoot, UiLoadTiming.FilePath);
            StringAssert.StartsWith(UiDataPaths.ProcessLocalRoot, UiHintState.FilePath);

            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 5f); // SCREEN depth: the group and a tip
            bool hints = UiSettings.Hints;
            UiSettings.Hints = !hints; // a setting the player could change
            UiSettings.Hints = hints;

            PrototypeUiFlow.GoToSelection();
            yield return Bekle(() => !PrototypeUiFlow.IsLoading && !UiCurtain.IsCovering, 60f, "yükleme bitmedi");
            Assert.Less(UiLoadTiming.LastLoadSeconds(PrototypeScenes.Selection), 5f, "gerçek yükleme ölçüldü");
            Assert.IsTrue(File.Exists(UiSettings.FilePath), "ayar süreç-yerel dosyaya yazıldı");
            Assert.IsTrue(File.Exists(UiLoadTiming.FilePath), "ölçüm süreç-yerel dosyaya yazıldı");
            Assert.AreEqual(before, TestUiDataIsolation.RealFiles(), "oyuncunun gerçek UI dosyaları değişti");
        }
    }
}
