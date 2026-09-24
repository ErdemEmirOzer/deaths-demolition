using System.Collections;
using DD.Prototype.Enemies;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// M4-R1 N19 / DEC-094: the enemy status stamps of the two new Specials - SHOCK while Raiju's chain holds a body,
    /// DOWN while the gel (or a knockdown) has it on the ground - stamped over the body, parked at group alpha 0 when unbound.
    /// </summary>
    public sealed class EnemyStatusStampsTests
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
        public IEnumerator Damgalar_ShockVeDown_GovdeUstunde_ParkSifirAlfa()
        {
            LogAssert.ignoreFailingMessages = true;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "RunHudScreen yok.");
            Assert.IsNotNull(hud.EnemyStatus, "durum işareti katmanı");

            Vector3 centre = _s.Zemin(0f, 6f);
            ZombieEnemy shocked = _s.Dogur(centre + Vector3.left);
            ZombieEnemy downed = _s.Dogur(centre + Vector3.right);
            yield return null;
            yield return null;

            Assert.IsTrue(shocked.ApplyShock(9101, 3f), "şok uygulanmadı");
            Assert.IsTrue(downed.ApplyKnockdown(9102, 3f), "düşürme uygulanmadı");
            yield return null;
            yield return null;
            Assert.IsTrue(shocked.IsElectrocuted, "A şokta");
            Assert.AreNotEqual(GelState.None, downed.Gel, "B yerde");

            var words = new System.Collections.Generic.List<string>();
            foreach (TMP_Text t in hud.EnemyStatus.GetComponentsInChildren<TMP_Text>(true))
            {
                if (((RectTransform)t.transform.parent).anchoredPosition.x > -9000f)
                {
                    words.Add(t.text);
                }
            }

            Assert.GreaterOrEqual(hud.EnemyStatus.VisibleCount, 1, "en az bir damga (kamera görüyorsa iki)");
            Assert.IsTrue(words.Contains("SHOCK") || words.Contains("DOWN"), "görünür damga SHOCK / DOWN: " + string.Join(",", words));
            Assert.IsFalse(words.Contains("NET") || words.Contains("DAZED"), "eski Special kelimeleri kalmadı");

            foreach (CanvasGroup group in hud.EnemyStatus.GetComponentsInChildren<CanvasGroup>(true))
            {
                if (((RectTransform)group.transform).anchoredPosition.x < -9000f)
                {
                    Assert.AreEqual(0f, group.alpha, 0.001f, "park edilmiş damga çizilmez (grup α 0): " + group.name);
                }
            }
        }
    }
}
