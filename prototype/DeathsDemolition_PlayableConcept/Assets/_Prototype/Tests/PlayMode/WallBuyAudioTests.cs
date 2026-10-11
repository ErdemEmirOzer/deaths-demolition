using System.Collections;
using System.Reflection;
using DD.Prototype.Audio;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension (bare Is stays NUnit-free: fully qualified below)

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-107 (R4) · wall buy sesleri (AP-011 v024 wb_*, IDS.md 1000–1004; Run sahnesi, gerçek director): satın alma = sikke + raf
    /// (roketatarda ağır raf) istasyonda, yenileme = yalnız sikke, red = istasyon reddi + UiDenied, istem yalnız görünür kenarında
    /// çalar. Silahın kendi equip / pickup foley'si wall buy yolundan ikinci kez çalmaz. Abonelik: Rearm / director yeniden etkin /
    /// bus sıfırlanması sonrası tek dinleyici. Ses yolu kararlı durumda GC 0. WallBuyDirector kapatılır: olaylar testten yayınlanır,
    /// oyuncunun doğduğu yerdeki istem sayıları bozmaz.
    /// </summary>
    [TestFixture]
    public sealed class WallBuyAudioTests
    {
        private SpecialWeaponSahne _s;
        private PrototypeAudioDirector _d;
        private WallBuyDirector _wallBuy;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _d = PrototypeAudioDirector.EnsureExists();
            yield return SpecialWeaponSahne.Bekle(() => _d.Library.PrewarmComplete, 30f, "Ses kütüphanesi prewarm bitmedi.");
            yield return null;
            _wallBuy = WallBuyDirector.Instance;
            if (_wallBuy != null)
            {
                _wallBuy.enabled = false;   // OnDisable → WallBuyEvents.ClearState: a prompt up at spawn is dropped here, not mid-test
            }

            WallBuyEvents.ClearState();
            Time.captureDeltaTime = 0f;
        }

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            WallBuyEvents.ClearState();
            if (_d != null && !_d.enabled)
            {
                _d.enabled = true;
            }

            if (_wallBuy != null)
            {
                _wallBuy.enabled = true;
            }

            yield return _s.Bitir();
        }

        private Vector3 Istasyon()
        {
            return _s.Rig.transform.position + new Vector3(1.4f, 0f, 1.1f);
        }

        private static WallBuyOutcome Sonuc(WallBuyResult result, WallBuyBlock block, ClassicWeaponId weapon, Vector3 at)
        {
            return new WallBuyOutcome(result, block, "test_" + (int)weapon, weapon, 500, at);
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator WallBuy_SatinAlYenileRed_IstasyondaKendiSesleri_EquipCiftCalmaz()
        {
            AudioEventRouter r = _d.Router;
            Vector3 at = Istasyon();
            float grab0 = _d.WorldPool.LastPlayedAt(AudioSoundId.PickupGrab);
            float done0 = _d.WorldPool.LastPlayedAt(AudioSoundId.PickupDone);
            float swPickup0 = _d.SignalPool.LastPlayedAt(AudioSoundId.SwPickup);

            // ---- Bought (not the rocket launcher): coins + the wooden rack, at the station
            int calan = _d.PlayedCount, ses = r.WallBuyVoices, sonuc = r.WallBuyOutcomes;
            WallBuyEvents.PublishResolved(Sonuc(WallBuyResult.Bought, WallBuyBlock.None, ClassicWeaponId.AutoSmg, at));
            float simdi = Time.unscaledTime;
            Assert.AreEqual(sonuc + 1, r.WallBuyOutcomes, "tek abonelik: bir basış bir sonuç");
            Assert.AreEqual(calan + 2, _d.PlayedCount, "satın alma = sikke + raf (dropped=" + _d.DroppedCues + ")");
            Assert.AreEqual(ses + 2, r.WallBuyVoices);
            Assert.AreEqual(simdi, _d.WorldPool.LastPlayedAt(AudioSoundId.WallBuyCoins), 1e-5f, "sikke World havuzunda (Impact)");
            Assert.AreEqual(simdi, _d.WorldPool.LastPlayedAt(AudioSoundId.WallBuyRack), 1e-5f, "raf World havuzunda");
            Assert.AreEqual(AudioSoundId.WallBuyRack, r.LastWallBuySound, "raf sikkeden sonra");
            Assert.AreEqual(at, r.LastWallBuyAt, "konum = o.StationWorld");
            Assert.AreEqual(AudioMixPlan.WallBuyRackDelay, AudioMixPlan.WallBuy(AudioSoundId.WallBuyRack).Delay, 1e-6f, "ödeme önde");

            // The weapon's own equip / pickup is the rig's (anim.pickup_grab / pickup_done); the wall buy path never plays it.
            Assert.AreEqual(grab0, _d.WorldPool.LastPlayedAt(AudioSoundId.PickupGrab), "wall buy PickupGrab çalmaz (çift equip)");
            Assert.AreEqual(done0, _d.WorldPool.LastPlayedAt(AudioSoundId.PickupDone), "wall buy PickupDone çalmaz");
            Assert.AreEqual(swPickup0, _d.SignalPool.LastPlayedAt(AudioSoundId.SwPickup), "wall buy Special pickup sesi değil");
            yield return new WaitForSecondsRealtime(0.4f);   // past the rack's 0.3 s gate

            // ---- Bought rl_1: coins + the heavy rack, no wooden rack
            float raf0 = _d.WorldPool.LastPlayedAt(AudioSoundId.WallBuyRack);
            calan = _d.PlayedCount;
            WallBuyEvents.PublishResolved(Sonuc(WallBuyResult.Bought, WallBuyBlock.None, ClassicWeaponId.Rl1, at));
            simdi = Time.unscaledTime;
            Assert.AreEqual(calan + 2, _d.PlayedCount, "roketatar = sikke + ağır raf");
            Assert.AreEqual(AudioSoundId.WallBuyHeavy, r.LastWallBuySound);
            Assert.AreEqual(simdi, _d.WorldPool.LastPlayedAt(AudioSoundId.WallBuyHeavy), 1e-5f);
            Assert.AreEqual(raf0, _d.WorldPool.LastPlayedAt(AudioSoundId.WallBuyRack), "roketatarda tahta raf çalmaz");
            yield return new WaitForSecondsRealtime(0.4f);

            // ---- Refilled: the coins alone
            calan = _d.PlayedCount;
            WallBuyEvents.PublishResolved(Sonuc(WallBuyResult.Refilled, WallBuyBlock.None, ClassicWeaponId.AutoSmg, at));
            Assert.AreEqual(calan + 1, _d.PlayedCount, "yenileme = yalnız sikke");
            Assert.AreEqual(AudioSoundId.WallBuyCoins, r.LastWallBuySound);
            Assert.AreEqual(at, r.LastWallBuyAt);

            // ---- Denied: the station's refusal (world) + UiDenied (interface, 2D)
            calan = _d.PlayedCount;
            WallBuyEvents.PublishResolved(Sonuc(WallBuyResult.Denied, WallBuyBlock.NotEnoughGold, ClassicWeaponId.AutoSmg, at));
            simdi = Time.unscaledTime;
            Assert.AreEqual(calan + 2, _d.PlayedCount, "red = istasyon reddi + UiDenied");
            Assert.AreEqual(simdi, _d.WorldPool.LastPlayedAt(AudioSoundId.WallBuyDenied), 1e-5f);
            Assert.AreEqual(simdi, _d.SignalPool.LastPlayedAt(AudioSoundId.UiDenied), 1e-5f, "UiDenied arayüz havuzunda");
            Assert.AreEqual(AudioSoundId.UiDenied, r.LastWallBuySound);
            Assert.AreEqual(Vector3.zero, r.LastWallBuyAt, "UiDenied 2D");

            // A mashed key inside the 0.25 s gate is one refusal at the station (same frame: no long-frame dependence).
            calan = _d.PlayedCount;
            WallBuyEvents.PublishResolved(Sonuc(WallBuyResult.Denied, WallBuyBlock.AmmoFull, ClassicWeaponId.AutoSmg, at));
            Assert.AreEqual(calan, _d.PlayedCount, "kapı içinde ikinci red sessiz (istasyon 0,25 s, UiDenied 0,06 s)");
            Debug.Log("[DD-TEST] wp107.audio wallbuy voices=" + (r.WallBuyVoices - ses) + " produced=" + _d.Library.IsProduced(AudioSoundId.WallBuyCoins)
                      + " world=" + _d.WorldPool.Describe());
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator WallBuy_Istem_YalnizGorunurKenarindaCalar_TeklifDegisimiSessiz()
        {
            AudioEventRouter r = _d.Router;
            Vector3 at = Istasyon();
            var istem = new WallBuyPrompt("test_istem", ClassicWeaponId.AutoSmg, "smg_1", new WallBuyQuote(WallBuyAction.Buy, 900, WallBuyBlock.None), at);
            var fakir = new WallBuyPrompt("test_istem", ClassicWeaponId.AutoSmg, "smg_1", new WallBuyQuote(WallBuyAction.Buy, 900, WallBuyBlock.NotEnoughGold), at);
            Assert.IsFalse(WallBuyEvents.CurrentPrompt.Visible, "öncül: istem kapalı");

            int ping = r.WallBuyPromptPings;
            WallBuyEvents.PublishPrompt(istem);
            Assert.AreEqual(ping + 1, r.WallBuyPromptPings, "false → true kenarı çalar");
            Assert.AreEqual(AudioSoundId.WallBuyPrompt, r.LastWallBuySound);
            Assert.AreEqual(Time.unscaledTime, _d.SignalPool.LastPlayedAt(AudioSoundId.WallBuyPrompt), 1e-5f, "istem arayüz havuzunda (2D)");

            WallBuyEvents.PublishPrompt(fakir);
            Assert.AreEqual(ping + 1, r.WallBuyPromptPings, "görünür istemde teklif değişimi kenar değil");
            WallBuyEvents.PublishPrompt(default);
            Assert.AreEqual(ping + 1, r.WallBuyPromptPings, "true → false sessiz");

            // On the reach radius: hide / show inside the 1 s gate is one ping.
            WallBuyEvents.PublishPrompt(istem);
            Assert.AreEqual(ping + 1, r.WallBuyPromptPings, "1 s kapısı yarıçap titremesini yutar");
            WallBuyEvents.PublishPrompt(default);

            yield return new WaitForSecondsRealtime(1.1f);
            WallBuyEvents.PublishPrompt(istem);
            Assert.AreEqual(ping + 2, r.WallBuyPromptPings, "kapı geçince yeni kenar çalar");
            WallBuyEvents.ClearState();
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator WallBuy_Abonelik_RearmDirectorYenidenEtkin_BusSifirlanmasi_TekDinleyici()
        {
            AudioEventRouter r = _d.Router;
            Vector3 at = Istasyon();
            WallBuyOutcome red = Sonuc(WallBuyResult.Denied, WallBuyBlock.NotEnoughGold, ClassicWeaponId.AutoSmg, at);

            int n = r.WallBuyOutcomes;
            WallBuyEvents.PublishResolved(red);
            Assert.AreEqual(n + 1, r.WallBuyOutcomes, "kurulumdan sonra tek dinleyici (sahne değişimi Rearm'ı dahil)");

            // The scene-change path, twice: remove-then-add never doubles.
            r.Rearm();
            r.Rearm();
            WallBuyEvents.PublishResolved(red);
            Assert.AreEqual(n + 2, r.WallBuyOutcomes, "iki Rearm sonrası yine tek dinleyici");

            // Director OnDisable → unsubscribed; OnEnable → exactly one again.
            _d.enabled = false;
            WallBuyEvents.PublishResolved(red);
            Assert.AreEqual(n + 2, r.WallBuyOutcomes, "director kapalıyken dinleyici yok");
            _d.enabled = true;
            yield return null;
            WallBuyEvents.PublishResolved(red);
            Assert.AreEqual(n + 3, r.WallBuyOutcomes, "director yeniden etkin: tek dinleyici");

            // The bus cleared under the router (what WallBuyEvents.ResetStatics does at SubsystemRegistration), surgically: only
            // Resolved's backing field - the HUD's PromptChanged listener is left alone. The next Rearm brings the router back.
            FieldInfo bus = typeof(WallBuyEvents).GetField("Resolved", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(bus, "WallBuyEvents.Resolved alanı yok (S-02 sözleşmesi değişti mi?)");
            bus.SetValue(null, null);
            WallBuyEvents.PublishResolved(red);
            Assert.AreEqual(n + 3, r.WallBuyOutcomes, "öncül: sıfırlanan bus dinleyicisiz");
            r.Rearm();
            WallBuyEvents.PublishResolved(red);
            Assert.AreEqual(n + 4, r.WallBuyOutcomes, "Rearm sıfırlanan busa tek dinleyiciyle döner");
            r.Rearm();
            WallBuyEvents.PublishResolved(red);
            Assert.AreEqual(n + 5, r.WallBuyOutcomes);
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator WallBuy_SesYolu_KararliDurumda_SifirAyirma()
        {
            Vector3 at = Istasyon();
            WallBuyOutcome al = Sonuc(WallBuyResult.Bought, WallBuyBlock.None, ClassicWeaponId.AutoSmg, at);
            WallBuyOutcome roket = Sonuc(WallBuyResult.Bought, WallBuyBlock.None, ClassicWeaponId.Rl1, at);
            WallBuyOutcome yenile = Sonuc(WallBuyResult.Refilled, WallBuyBlock.None, ClassicWeaponId.AutoSmg, at);
            WallBuyOutcome red = Sonuc(WallBuyResult.Denied, WallBuyBlock.NotEnoughGold, ClassicWeaponId.AutoSmg, at);
            var istem = new WallBuyPrompt("test_gc", ClassicWeaponId.AutoSmg, "smg_1", new WallBuyQuote(WallBuyAction.Buy, 900, WallBuyBlock.None), at);

            // Warm-up: every path's first call (clip deck, gate dictionary entry) is allowed to allocate.
            for (int i = 0; i < 4; i++)
            {
                WallBuyEvents.PublishResolved(al);
                WallBuyEvents.PublishResolved(roket);
                WallBuyEvents.PublishResolved(yenile);
                WallBuyEvents.PublishResolved(red);
                WallBuyEvents.PublishPrompt(istem);
                WallBuyEvents.PublishPrompt(default);
                yield return new WaitForSecondsRealtime(0.35f);
            }

            // The HUD prompt (S-02 / S-09) formats its label on every PromptChanged - its allocations are not the audio path's.
            DD.Prototype.UI.HudWallBuyPrompt hud = UnityEngine.Object.FindObjectOfType<DD.Prototype.UI.HudWallBuyPrompt>();
            if (hud != null) hud.enabled = false;
            try
            {
                Assert.That(() =>
                {
                    for (int i = 0; i < 50; i++)
                    {
                        WallBuyEvents.PublishResolved(al);
                        WallBuyEvents.PublishResolved(roket);
                        WallBuyEvents.PublishResolved(yenile);
                        WallBuyEvents.PublishResolved(red);
                        WallBuyEvents.PublishPrompt(istem);
                        WallBuyEvents.PublishPrompt(default);
                    }
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "wall buy ses yolu 300 olayda ayırdı");
            }
            finally
            {
                WallBuyEvents.ClearState();
                if (hud != null) hud.enabled = true;
            }

            Debug.Log("[DD-TEST] wp107.audio gc=none over 300 wall buy events");
        }
    }
}
