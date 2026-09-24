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
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SilahGlifi_SoyTonunda_TamAlfa()
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
        }
    }
}
