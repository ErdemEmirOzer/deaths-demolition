using System;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-005 · UI navigation state sözleşmesi (saf model, Unity nesnesi yok).
    ///
    /// Kilitlenen kurallar: tek taban frame sahibi; katman yalnız kendi tabanında;
    /// Pause yalnız HUD (veya HUD+shop) üstünde; Results×Pause ve Results×Upgrade
    /// imkânsız; aynı duruma ikinci giriş idempotent (olay yok); yeni taban eski
    /// katman/overlay'i düşürür.
    /// </summary>
    [TestFixture]
    [Category("WP005")]
    public sealed class UiNavigationModelTests
    {
        private UiNavigationModel _model;
        private int _changed;

        [SetUp]
        public void SetUp()
        {
            _model = new UiNavigationModel();
            _changed = 0;
            _model.Changed += _ => _changed++;
        }

        [Test]
        public void TabanGecisleri_TekTabanFrameSahibi()
        {
            var sira = new[]
            {
                UiContext.MainMenu, UiContext.Selection, UiContext.GameplayHud, UiContext.RunResults, UiContext.MainMenu
            };

            for (int i = 0; i < sira.Length; i++)
            {
                Assert.AreEqual(UiNavigationResult.Changed, _model.EnterBase(sira[i]), "adım " + i);
                Assert.AreEqual(sira[i], _model.Base);
                Assert.AreEqual(UiContext.None, _model.Layer);
                Assert.AreEqual(UiContext.None, _model.Overlay);
                Assert.AreEqual(sira[i], _model.Current);
                Assert.AreEqual(i + 1, _changed, "her taban geçişi tam bir Changed üretir");
            }

            Debug.Log("[DD-TEST] nav.base=OK changed=" + _changed);
        }

        [Test]
        public void AnaMenuPaneleri_AcilirKapanirKardesDegistirir()
        {
            _model.EnterBase(UiContext.MainMenu);
            int taban = _changed;

            Assert.AreEqual(UiNavigationResult.Changed, _model.EnterLayer(UiContext.Profile));
            Assert.AreEqual(UiContext.Profile, _model.Layer);
            Assert.AreEqual(UiContext.Profile, _model.Current);

            Assert.AreEqual(UiNavigationResult.Changed, _model.EnterLayer(UiContext.Statistics));
            Assert.AreEqual(UiContext.Statistics, _model.Layer, "kardeş pane öncekinin yerine geçer");

            Assert.AreEqual(UiNavigationResult.Changed, _model.EnterLayer(UiContext.Achievements));
            Assert.AreEqual(UiContext.Achievements, _model.Layer);
            Assert.AreEqual(UiContext.MainMenu, _model.Base, "taban değişmez");

            Assert.AreEqual(UiNavigationResult.Changed, _model.ExitLayer(UiContext.Achievements));
            Assert.AreEqual(UiContext.None, _model.Layer);
            Assert.AreEqual(UiContext.MainMenu, _model.Current, "geri dönüş ana menüye");

            Assert.AreEqual(taban + 4, _changed);
        }

        [Test]
        public void CelisenDurumlar_Reddedilir()
        {
            _model.EnterBase(UiContext.RunResults);
            int once = _changed;

            Assert.AreEqual(UiNavigationResult.Refused, _model.EnterLayer(UiContext.Paused), "Results üstünde pause");
            Assert.AreEqual(UiNavigationResult.Refused, _model.EnterLayer(UiContext.RoundUpgrade), "Results üstünde shop");
            Assert.AreEqual(UiNavigationResult.Refused, _model.EnterLayer(UiContext.Profile), "Results üstünde pane");
            Assert.IsFalse(_model.MayLayer(UiContext.Paused));

            _model.EnterBase(UiContext.MainMenu);
            Assert.AreEqual(UiNavigationResult.Refused, _model.EnterLayer(UiContext.RoundUpgrade), "menüde shop");
            Assert.AreEqual(UiNavigationResult.Refused, _model.EnterLayer(UiContext.Paused), "menüde pause");

            _model.EnterBase(UiContext.GameplayHud);
            Assert.AreEqual(UiNavigationResult.Refused, _model.EnterLayer(UiContext.Profile), "HUD üstünde pane");
            Assert.AreEqual(UiNavigationResult.Refused, _model.EnterLayer(UiContext.MainMenu), "taban katman değil");
            Assert.AreEqual(UiNavigationResult.Refused, _model.EnterBase(UiContext.Profile), "katman taban değil");

            Assert.AreEqual(once + 2, _changed, "yalnız iki taban geçişi Changed üretti; red hiç üretmez");
        }

        [Test]
        public void AyniDurum_IdempotentEventYok()
        {
            _model.EnterBase(UiContext.MainMenu);
            Assert.AreEqual(UiNavigationResult.AlreadyThere, _model.EnterBase(UiContext.MainMenu));

            _model.EnterLayer(UiContext.Profile);
            Assert.AreEqual(UiNavigationResult.AlreadyThere, _model.EnterLayer(UiContext.Profile));

            _model.EnterBase(UiContext.GameplayHud);
            _model.EnterLayer(UiContext.Paused);
            Assert.AreEqual(UiNavigationResult.AlreadyThere, _model.EnterLayer(UiContext.Paused));
            Assert.AreEqual(UiNavigationResult.AlreadyThere, _model.ExitLayer(UiContext.RoundUpgrade), "açık olmayan katman");
            Assert.AreEqual(UiNavigationResult.AlreadyThere, _model.ExitOverlay(UiContext.None));

            // MainMenu, Profile, GameplayHud (katman düşer), Paused = 4 gerçek değişiklik.
            Assert.AreEqual(4, _changed, "duplicate istek Changed üretmez");
        }

        [Test]
        public void YeniTaban_EskiKatmanVeOverlayiDusurur()
        {
            _model.EnterBase(UiContext.GameplayHud);
            _model.EnterLayer(UiContext.RoundUpgrade);
            _model.EnterLayer(UiContext.Paused);
            Assert.IsTrue(_model.IsPaused);
            Assert.IsTrue(_model.IsShopOpen);

            Assert.AreEqual(UiNavigationResult.Changed, _model.EnterBase(UiContext.RunResults));
            Assert.AreEqual(UiContext.None, _model.Layer);
            Assert.AreEqual(UiContext.None, _model.Overlay);
            Assert.IsFalse(_model.IsPaused, "Results ile pause aynı anda açık olamaz");
            Assert.IsFalse(_model.IsShopOpen, "Results ile upgrade aynı anda açık olamaz");

            // Yeni koşu: HUD tabanı yeniden girildiğinde de eski katman gelmez.
            _model.EnterBase(UiContext.GameplayHud);
            Assert.AreEqual(UiContext.GameplayHud, _model.Current);
            Assert.IsFalse(_model.IsShopOpen);
        }

        [Test]
        public void PauseShopUstuneBiner_GirdiKapisi()
        {
            _model.EnterBase(UiContext.GameplayHud);
            Assert.IsTrue(_model.GameplayInputAllowed, "çıplak HUD: oyun girdisi açık");

            _model.EnterLayer(UiContext.RoundUpgrade);
            Assert.IsFalse(_model.GameplayInputAllowed, "shop açıkken oyun girdisi kapalı");

            Assert.AreEqual(UiNavigationResult.Changed, _model.EnterLayer(UiContext.Paused));
            Assert.AreEqual(UiContext.Paused, _model.Current);
            Assert.IsTrue(_model.IsShopOpen, "pause shop'u kapatmaz");
            Assert.IsFalse(_model.GameplayInputAllowed);

            _model.ExitOverlay(UiContext.Paused);
            Assert.IsTrue(_model.IsShopOpen, "pause kapanınca shop hâlâ açık");
            Assert.AreEqual(UiContext.RoundUpgrade, _model.Current);

            _model.ExitLayer(UiContext.RoundUpgrade);
            Assert.IsTrue(_model.GameplayInputAllowed);
        }

        [TestCase(UiContext.MainMenu, false)]
        [TestCase(UiContext.Selection, false)]
        [TestCase(UiContext.RunResults, false)]
        [TestCase(UiContext.GameplayHud, true)]
        public void OyunGirdisi_YalnizHudTabanindaAcik(UiContext taban, bool beklenen)
        {
            _model.EnterBase(taban);
            Assert.AreEqual(beklenen, _model.GameplayInputAllowed, taban.ToString());
        }

        [Test]
        public void BozukDinleyici_AkisiDurdurmaz()
        {
            int sonraki = 0;
            _model.Changed += _ => throw new InvalidOperationException("test-dinleyici");
            _model.Changed += _ => sonraki++;

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("test-dinleyici"));

            Assert.AreEqual(UiNavigationResult.Changed, _model.EnterBase(UiContext.MainMenu));
            Assert.AreEqual(UiContext.MainMenu, _model.Base, "durum değişti");
            // Delegate zinciri istisna atan üyede durur; model bunu yutar ve durum tutarlı kalır.
            Assert.GreaterOrEqual(_changed, 1);
            Debug.Log("[DD-TEST] nav.brokenListener sonraki=" + sonraki);
        }

        [Test]
        public void AyniTabanaYenidenGiris_KatmaniKorur()
        {
            // Sahne dosyasındaki ekran Awake'te tabanı yazar ve pane'ini açar; installer
            // sceneLoaded'da aynı tabanı yeniden yazar — pane silinmemeli.
            _model.EnterBase(UiContext.MainMenu);
            _model.EnterLayer(UiContext.Profile);
            int once = _changed;
            Assert.AreEqual(UiNavigationResult.AlreadyThere, _model.EnterBase(UiContext.MainMenu));
            Assert.AreEqual(UiContext.Profile, _model.Layer);
            Assert.AreEqual(once, _changed);
        }

        [Test]
        public void Clear_HerSeyiSifirlar()
        {
            _model.EnterBase(UiContext.GameplayHud);
            _model.EnterLayer(UiContext.Paused);
            _model.Clear();
            Assert.AreEqual(UiContext.None, _model.Base);
            Assert.AreEqual(UiContext.None, _model.Current);
            Assert.IsFalse(_model.IsPaused);

            int once = _changed;
            _model.Clear();
            Assert.AreEqual(once, _changed, "boşken Clear olay üretmez");
        }

        /// <summary>WP-022: the Settings pane is a MainMenu layer like the other three, and only there.</summary>
        [Test]
        public void AyarlarKatmani_YalnizAnaMenude()
        {
            Assert.IsTrue(UiNavigationModel.IsLayer(UiContext.Settings));
            Assert.AreEqual(UiContext.MainMenu, UiNavigationModel.BaseOf(UiContext.Settings));

            _model.EnterBase(UiContext.MainMenu);
            Assert.AreEqual(UiNavigationResult.Changed, _model.EnterLayer(UiContext.Settings));
            Assert.AreEqual(UiContext.Settings, _model.Layer);
            Assert.AreEqual(UiNavigationResult.Changed, _model.EnterLayer(UiContext.Profile), "kardeş pane Settings'in yerine geçer");

            _model.EnterBase(UiContext.GameplayHud);
            Assert.AreEqual(UiNavigationResult.Refused, _model.EnterLayer(UiContext.Settings), "HUD tabanında Settings katmanı yok");
        }
    }
}
