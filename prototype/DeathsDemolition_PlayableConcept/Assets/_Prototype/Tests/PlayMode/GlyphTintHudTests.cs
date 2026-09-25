using System.Collections;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// M4-R1 N19 §2 on the live HUD: the classic weapon glyph wears its lineage's identity tint (UiGlyphTints.Weapon) at
    /// full alpha while the magazine is healthy - colour is identity, never state.
    /// </summary>
    public sealed class GlyphTintHudTests
    {
        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            RunHudScreen.CaptureSlick = false;
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SilahGlifi_SoyTonunda_TamAlfa_JeldeSlick()
        {
            LogAssert.ignoreFailingMessages = true;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            yield return null;
            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "RunHudScreen yok.");

            Image weapon = null;
            foreach (Image image in hud.GetComponentsInChildren<Image>(true))
            {
                if (image.name == "WeaponIcon")
                {
                    weapon = image;
                    break;
                }
            }

            Assert.IsNotNull(weapon, "WeaponIcon");
            Color want = UiTokens.Get(UiGlyphTints.Weapon(WeaponEvents.Classic.Id));
            Assert.AreEqual(want.r, weapon.color.r, 0.01f, "R · " + WeaponEvents.Classic.Id);
            Assert.AreEqual(want.g, weapon.color.g, 0.01f, "G");
            Assert.AreEqual(want.b, weapon.color.b, 0.01f, "B");
            Assert.AreEqual(1f, weapon.color.a, 0.01f, "dolu şarjörde tam alfa (LOW / EMPTY 0,70)");

            // WP-068 12b (AD FP cluster SHOULD-2): the ammo word is the pack's "LOW AMMO" and fits the cluster's word slot.
            Assert.AreEqual(UiStrings.Get("hud.ammo.low", "LOW AMMO"), hud.AmmoLowWord, "paket metni hud.ammo.low");
            TMPro.TMP_Text caption = null;
            foreach (TMPro.TMP_Text t in hud.GetComponentsInChildren<TMPro.TMP_Text>(true)) if (t.name == "ReloadCaption") caption = t;
            Assert.IsNotNull(caption, "ReloadCaption");
            Assert.LessOrEqual(Mathf.Ceil(caption.GetPreferredValues(hud.AmmoLowWord).x), caption.rectTransform.rect.width, "LOW AMMO kelime yuvasına sığar");

            // N19 §2: in gel the SLICK hint takes the E23 slot - Status.Sliding in the gel tint + the pack word, no box.
            Assert.IsFalse(hud.SlickShown, "jel yokken ipucu yok");
            RunHudScreen.CaptureSlick = true;
            yield return null;
            yield return null;
            Assert.IsTrue(hud.SlickShown, "jelde SLICK ipucu");
            RectTransform slick = null;
            foreach (RectTransform r in hud.GetComponentsInChildren<RectTransform>(true)) if (r.name == "Slick") slick = r;
            Assert.IsNotNull(slick);
            TMPro.TMP_Text word = slick.GetComponentInChildren<TMPro.TMP_Text>(true);
            Assert.AreEqual(UiStrings.Get("hud.slick", "SLICK"), word.text, "paket metni hud.slick");
            Image glyph = null;
            foreach (Image im in slick.GetComponentsInChildren<Image>(true)) if (im.name == "Glyph") glyph = im;
            Assert.IsNotNull(glyph, "Status.Sliding glifi (N19 G1)");
            Color gel = UiTokens.Get(UiGlyphTints.Gel);
            Assert.AreEqual(gel.r, glyph.color.r, 0.01f, "jel kimlik tonu");
            RunHudScreen.CaptureSlick = false;
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(hud.SlickShown, "jelden çıkınca ipucu gider");
        }
    }
}
