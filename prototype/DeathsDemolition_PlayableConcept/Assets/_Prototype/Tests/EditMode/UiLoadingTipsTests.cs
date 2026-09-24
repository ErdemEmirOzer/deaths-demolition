using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 10b · T19 (direction §15.7) and the U9 selection rules (UXW §1.1): 24 tips
    /// ≤ 90 characters, two lines at most on the strip at TextScale 1.25, HINT first while
    /// hints are on and each HINT once per profile, FACT only when off, round-robin with no
    /// repeat in a session, and no survival tip on the first loading screen.
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiLoadingTipsTests
    {
        private float _scale;
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _scale = UiTokens.TextScale;
            UiLoadingTips.ResetSession();
        }

        [TearDown]
        public void TearDown()
        {
            UiTokens.TextScale = _scale;
            UiLoadingTips.ResetSession();
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Ipuclari_24_En90Karakter_AnahtarlarTekil_DokuzHintBiti()
        {
            Assert.AreEqual(24, UiLoadingTips.All.Length);
            var keys = new HashSet<string>();
            var bits = new HashSet<int>();
            foreach (UiTip tip in UiLoadingTips.All)
            {
                Assert.LessOrEqual(tip.Text.Length, UiLoadingTips.MaxLength, tip.Key);
                Assert.IsTrue(keys.Add(tip.Key), "tekrar eden anahtar " + tip.Key);
                if (tip.Class == UiTipClass.Hint)
                {
                    Assert.IsTrue(tip.HintBit >= 0 && tip.HintBit < 9 && bits.Add(tip.HintBit), tip.Key + " HINT biti");
                }
                else
                {
                    Assert.AreEqual(-1, tip.HintBit, tip.Key);
                }
            }

            Assert.AreEqual(9, bits.Count, "dokuz HINT");
        }

        [Test]
        public void T19_Serit_125te_EnFazlaIkiSatir()
        {
            UiTokens.TextScale = 1.25f;
            var host = new GameObject("Host", typeof(RectTransform));
            _created.Add(host);
            TMP_Text probe = UiFactory.Label("Probe", host.transform, string.Empty, UiType.Body, UiColor.TextPrimary, TextAnchor.UpperLeft);
            float width = UiCurtain.TipMaxWidth - (int)UiSpace.L * 2f;
            // Two lines as this label lays them out (face line box + the label's own line spacing).
            float twoLines = probe.GetPreferredValues("Ag\nAg", width, 0f).y;
            float threeLines = probe.GetPreferredValues("Ag\nAg\nAg", width, 0f).y;
            Assert.Greater(threeLines, twoLines + 1f, "ölçü çalışıyor");
            foreach (UiTip tip in UiLoadingTips.All)
            {
                float height = probe.GetPreferredValues(tip.Text, width, 0f).y;
                Assert.LessOrEqual(height, twoLines + 0.5f, tip.Key + " 1,25'te ikiden çok satır");
            }
        }

        [Test]
        public void IpuclariAcik_OnceHint_HerHintBirKez_SonraFact_IlkEkrandaHayattaKalmaYok()
        {
            int mask = 0;
            var seen = new HashSet<int>();
            int first = UiLoadingTips.Pick(true, ref mask);
            Assert.GreaterOrEqual(first, 0);
            Assert.AreEqual(UiTipClass.Hint, UiLoadingTips.All[first].Class, "ipuçları açık: önce HINT");
            Assert.AreNotEqual(UiTipGroup.Survival, UiLoadingTips.All[first].Group, "oturumun ilk ekranı hayatta kalma ipucu değil");
            seen.Add(first);

            int hints = 1;
            int facts = 0;
            for (int n = 0; n < 40; n++)
            {
                int pick = UiLoadingTips.Pick(true, ref mask);
                if (pick < 0)
                {
                    break;
                }

                Assert.IsTrue(seen.Add(pick), "oturumda tekrar: " + UiLoadingTips.All[pick].Key);
                if (UiLoadingTips.All[pick].Class == UiTipClass.Hint)
                {
                    Assert.AreEqual(0, facts, "bir FACT geldikten sonra HINT gelmez (önce HINT'ler)");
                    hints++;
                }
                else
                {
                    facts++;
                }
            }

            Assert.AreEqual(9, hints, "dokuz HINT'in hepsi bir kez");
            Assert.AreEqual((1 << 9) - 1, mask, "HINT maskesi dolu");

            // A new session on the same profile: HINTs already seen are not shown again.
            UiLoadingTips.ResetSession();
            int again = UiLoadingTips.Pick(true, ref mask);
            Assert.AreEqual(UiTipClass.Fact, UiLoadingTips.All[again].Class, "görülen HINT profil başına bir kez");
        }

        [Test]
        public void T19_IpucuYok_Acilis_SonuclaraGiden_Iptal()
        {
            Assert.IsFalse(PrototypeUiFlow.ShowsTip(DD.Prototype.Core.Boot.PrototypeScenes.Boot, DD.Prototype.Core.Boot.PrototypeScenes.MainMenu), "Boot → MainMenu: ipucu yok");
            Assert.IsFalse(PrototypeUiFlow.ShowsTip(DD.Prototype.Core.Boot.PrototypeScenes.Run, DD.Prototype.Core.Boot.PrototypeScenes.Results), "Results'a giden: ipucu yok");
            Assert.IsFalse(PrototypeUiFlow.ShowsTip(DD.Prototype.Core.Boot.PrototypeScenes.Run, DD.Prototype.Core.Boot.PrototypeScenes.MainMenu), "Pause iptali: ipucu yok");
            Assert.IsTrue(PrototypeUiFlow.ShowsTip(DD.Prototype.Core.Boot.PrototypeScenes.MainMenu, DD.Prototype.Core.Boot.PrototypeScenes.Selection));
            Assert.IsTrue(PrototypeUiFlow.ShowsTip(DD.Prototype.Core.Boot.PrototypeScenes.Selection, DD.Prototype.Core.Boot.PrototypeScenes.Run));
            Assert.IsTrue(PrototypeUiFlow.ShowsTip(DD.Prototype.Core.Boot.PrototypeScenes.Results, DD.Prototype.Core.Boot.PrototypeScenes.MainMenu));
        }

        [Test]
        public void IpuclariKapali_YalnizFact_GruplarArasiSira()
        {
            int mask = 0;
            UiTipGroup? last = null;
            for (int n = 0; n < 5; n++)
            {
                int pick = UiLoadingTips.Pick(false, ref mask);
                Assert.GreaterOrEqual(pick, 0);
                Assert.AreEqual(UiTipClass.Fact, UiLoadingTips.All[pick].Class, "ipuçları kapalı: yalnız FACT");
                Assert.AreNotEqual(last, UiLoadingTips.All[pick].Group, "gruplar arası sıra");
                last = UiLoadingTips.All[pick].Group;
            }

            Assert.AreEqual(0, mask, "HINT maskesi değişmez");
        }
    }
}
