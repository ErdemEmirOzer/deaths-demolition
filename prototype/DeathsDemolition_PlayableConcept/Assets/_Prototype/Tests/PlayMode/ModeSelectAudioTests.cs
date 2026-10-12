using System.Collections;
using System.Reflection;
using DD.Prototype.Audio;
using DD.Prototype.Difficulty;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-102 (R4) · mod seçimi sesleri (AP-011 v024 mode_*, IDS.md 1010 / 1011; gerçek director, sahne gerekmez):
    /// <c>DifficultyService.SelectedChanged</c> Hard / Demolition'da kendi stinger'ını kontrol edilebilir UI sesinde çalar, aynı seçim
    /// tekrar çalmaz, seçim değişince çalan stinger 150 ms'de söner, Normal yeni ses çalmaz (onayı mevcut UiConfirm yolu). Abonelik:
    /// Rearm / director yeniden etkin / bus sıfırlanması sonrası tek dinleyici. Kanca S-03'ün (difficulty-hook.patch önerisi).
    /// </summary>
    [TestFixture]
    public sealed class ModeSelectAudioTests
    {
        private PrototypeAudioDirector _d;
        private DifficultyMode _onceki;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            _d = PrototypeAudioDirector.EnsureExists();
            yield return SpecialWeaponSahne.Bekle(() => _d.Library.PrewarmComplete, 30f, "Ses kütüphanesi prewarm bitmedi.");
            _onceki = DifficultyService.Selected;
            DifficultyService.Selected = DifficultyMode.Normal;
            yield return new WaitForSecondsRealtime(0.25f);   // a stinger the baseline faded is silent before the test starts
        }

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_d != null && !_d.enabled)
            {
                _d.enabled = true;
            }

            DifficultyService.Selected = _onceki;
            yield return null;
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ModSecimi_ZorYikim_KendiStingeri_AyniSecimSessiz_DegisinceSoner_NormalYeniSesYok()
        {
            AudioEventRouter r = _d.Router;
            int olay = r.ModeSelectEvents, ses = r.ModeSelectVoices, calan = _d.PlayedCount;

            DifficultyService.Selected = DifficultyMode.Hard;
            Assert.AreEqual(olay + 1, r.ModeSelectEvents, "tek abonelik: bir değişim bir olay");
            Assert.AreEqual(ses + 1, r.ModeSelectVoices, "Hard kendi stinger'ını çalar");
            Assert.AreEqual(AudioSoundId.ModeSelectHard, _d.LastPlayed);
            yield return null;
            Assert.IsTrue(_d.UiVoicePlaying(AudioSoundId.ModeSelectHard), "Hard stinger'ı UI sesinde çalmıyor");

            DifficultyService.Selected = DifficultyMode.Hard;
            Assert.AreEqual(olay + 1, r.ModeSelectEvents, "aynı seçim olay değil (setter yalnız değişimde yayınlar)");
            Assert.AreEqual(ses + 1, r.ModeSelectVoices, "aynı seçim tekrar çalmaz");

            DifficultyService.Selected = DifficultyMode.Demolition;
            Assert.AreEqual(ses + 2, r.ModeSelectVoices, "Demolition kendi stinger'ını çalar");
            Assert.AreEqual(AudioSoundId.ModeSelectDemolition, _d.LastPlayed);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsTrue(_d.UiVoicePlaying(AudioSoundId.ModeSelectDemolition), "Demolition stinger'ı çalmıyor");
            Assert.IsFalse(_d.UiVoicePlaying(AudioSoundId.ModeSelectHard), "seçim değişince Hard stinger'ı 150 ms'de sönmeli (üst üste binmez)");

            calan = _d.PlayedCount;
            DifficultyService.Selected = DifficultyMode.Normal;
            Assert.AreEqual(olay + 3, r.ModeSelectEvents);
            Assert.AreEqual(calan, _d.PlayedCount, "Normal yeni ses çalmaz (onay mevcut UiConfirm yolundan)");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(_d.UiVoicePlaying(AudioSoundId.ModeSelectDemolition), "Normal'e dönünce çalan stinger söner");
            Debug.Log("[DD-TEST] wp102.audio mode voices=" + (r.ModeSelectVoices - ses) + " produced=" + _d.Library.IsProduced(AudioSoundId.ModeSelectHard));
        }

        [Category("WP021")]
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ModSecimi_Abonelik_RearmDirectorYenidenEtkin_BusSifirlanmasi_TekDinleyici()
        {
            AudioEventRouter r = _d.Router;
            int n = r.ModeSelectEvents;

            DifficultyService.Selected = DifficultyMode.Hard;
            Assert.AreEqual(n + 1, r.ModeSelectEvents, "kurulumdan sonra tek dinleyici");

            r.Rearm();
            r.Rearm();
            DifficultyService.Selected = DifficultyMode.Normal;
            Assert.AreEqual(n + 2, r.ModeSelectEvents, "iki Rearm sonrası yine tek dinleyici");

            _d.enabled = false;
            DifficultyService.Selected = DifficultyMode.Hard;
            Assert.AreEqual(n + 2, r.ModeSelectEvents, "director kapalıyken dinleyici yok");
            _d.enabled = true;
            yield return null;
            DifficultyService.Selected = DifficultyMode.Demolition;
            Assert.AreEqual(n + 3, r.ModeSelectEvents, "director yeniden etkin: tek dinleyici");

            // The bus cleared under the router (what DifficultyService.ResetStatics does at SubsystemRegistration), surgically: only
            // SelectedChanged's backing field, so no other listener of the service is touched. The next Rearm brings the router back.
            FieldInfo bus = typeof(DifficultyService).GetField("SelectedChanged", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(bus, "DifficultyService.SelectedChanged alanı yok (S-03 kancası değişti mi?)");
            DifficultyMode before = DifficultyService.Selected;
            bus.SetValue(null, null);
            DifficultyService.Selected = DifficultyMode.Hard;
            Assert.AreEqual(n + 3, r.ModeSelectEvents, "öncül: sıfırlanan bus dinleyicisiz");
            r.Rearm();
            DifficultyService.Selected = DifficultyMode.Demolition;
            Assert.AreEqual(n + 4, r.ModeSelectEvents, "Rearm sıfırlanan busa tek dinleyiciyle döner");
            DifficultyService.Selected = before;
            yield return new WaitForSecondsRealtime(0.25f);
        }
    }
}
