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
    /// ui-settings.json (a temp copy here).
    /// </summary>
    [Category("WP068")]
    public sealed class LoadingScreenTests
    {
        private string _settingsPath;
        private bool _fast;

        [SetUp]
        public void SetUp()
        {
            _fast = PrototypeUiFlow.FastTransitions;
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-wp068-loading-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
        }

        [TearDown]
        public void TearDown()
        {
            PrototypeUiFlow.FastTransitions = _fast;
            RunFlowClock.ClearHolds();
            UiSettings.SetPathOverride(null);
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Ekran_KapilarSahneEtkinOlmadanAcilmaz_HalkaIleriGider_AsgariGosterim_DunyaAktivasyondaCozulur()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            UiSettings.RecordLoad(PrototypeScenes.Selection, 5f); // measured as slow → SCREEN depth
            RunFlowClock.AddHold("wp068-loading-test");

            PrototypeUiFlow.GoToSelection();
            Assert.IsTrue(PrototypeUiFlow.IsLoading, "yükleme uçuşta");
            Assert.IsTrue(UiCurtain.IsCovering, "kapılar istek anında kapanmaya başlar");

            UiCurtain curtain = UiCurtain.Instance;
            float lastFill = 0f;
            float groupFrom = -1f, enteringAt = -1f;
            bool heldWhileGroup = false, sawTip = false, sawPlate = false;
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
            Assert.Greater(UiSettings.LastLoadSeconds(PrototypeScenes.Selection), 0f, "ölçülen süre geri yazıldı");
            Assert.Less(UiSettings.LastLoadSeconds(PrototypeScenes.Selection), 5f, "ölçüm eski değerin yerine geçti");
            Assert.IsTrue(sawTip, "10b: MainMenu → Selection ekranında ipucu şeridi");
            Assert.IsTrue(sawPlate, "10b: SCREEN derinliği Look B plakası üstünde");
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
                UiSettings.RecordLoad(PrototypeScenes.Selection, 5f); // SCREEN by the table
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

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Perde_HizliSahne_GrupGorunmez_KapilarEnAzKapali()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
            UiSettings.RecordLoad(PrototypeScenes.Selection, 0.2f); // measured as fast → CURTAIN depth

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

            Assert.IsFalse(groupSeen, "CURTAIN derinliğinde grup (halka + etiketler) görünmez");
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
            UiSettings.RecordLoad(PrototypeScenes.Selection, 0.2f);
            UiSettings.RecordLoad(PrototypeScenes.MainMenu, 0.2f);
            bool cursor = Cursor.visible;

            PrototypeUiFlow.GoToSelection();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, 30f, "Selection etkin olmadı");
            Assert.IsFalse(PrototypeUiFlow.IsLoading, "yükleme aktivasyonda biter (açılış yüklemeye dahil değil)");
            Assert.IsTrue(UiCurtain.IsCovering, "açılış henüz sürüyor");

            PrototypeUiFlow.GoToMainMenu(); // the pattern of every flow suite: next step as soon as the scene is live
            Assert.IsTrue(PrototypeUiFlow.IsLoading, "açılış sırasındaki istek kabul edildi");
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
            Assert.AreEqual(cursor, Cursor.visible, "imleç ilk kapanıştaki durumuna döndü");
        }
    }
}
