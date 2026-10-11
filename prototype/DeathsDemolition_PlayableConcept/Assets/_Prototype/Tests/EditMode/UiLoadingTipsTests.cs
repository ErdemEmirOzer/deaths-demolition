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
    /// R4 WP-105 (MUST-4, D5, V-49): the tips say only what is true in this build, retired HINT bits are never reused,
    /// every key is in the pack, and a stalled load's second exit is offered off the main menu only.
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

        /// <summary>
        /// R4 WP-105 (MUST-4): 24 tips again after the retirements, each ≤ 90 characters with a unique key; HINT bits unique,
        /// inside the int mask, and never one of the retired tips' bits (a player who saw the old tip must still get the new one).
        /// </summary>
        [Test]
        public void Ipuclari_24_En90Karakter_AnahtarlarTekil_HintBitleriTekil_EmekliBitYok()
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
                    Assert.IsTrue(tip.HintBit >= 0 && tip.HintBit < 31 && bits.Add(tip.HintBit), tip.Key + " HINT biti");
                    Assert.AreEqual(-1, System.Array.IndexOf(UiLoadingTips.RetiredHintBits, tip.HintBit), tip.Key + ": emekli bit yeniden kullanılmaz");
                }
                else
                {
                    Assert.AreEqual(-1, tip.HintBit, tip.Key);
                }
            }

            Assert.AreEqual(HintCount(), bits.Count);
            Assert.AreEqual(7, bits.Count, "yedi HINT (eski dokuzdan dördü emekli, iki yeni Raiju ipucu)");
        }

        /// <summary>
        /// R4 WP-105 (MUST-4, D5): no tip names a Special that is not in this build (DEC-094: Raiju and Namekuji only) or a
        /// rule that is not true here (Specials have ammunition and reload; Single and Burst weapons exist; rounds 10-13 allow
        /// three purchases). The retired keys are out of the set.
        /// </summary>
        [Test]
        public void Ipuclari_OlmayanOzelSilahVeYanlisKuralYok_EmekliAnahtarlarSettenCikti()
        {
            string[] banned = { "Kagura", "Bell", "Ofuda", "Tide", "no ammo", "no reload", "Every classic weapon is automatic", "Up to two of them" };
            string[] retired = { "loading.tip.ctrl.01", "loading.tip.econ.01", "loading.tip.sw.01", "loading.tip.sw.03", "loading.tip.sw.04", "loading.tip.sw.05", "loading.tip.tier.04" };
            foreach (UiTip tip in UiLoadingTips.All)
            {
                foreach (string word in banned)
                {
                    StringAssert.DoesNotContain(word, tip.Text, tip.Key);
                }

                Assert.AreEqual(-1, System.Array.IndexOf(retired, tip.Key), tip.Key + " emekli");
            }

            bool raiju = false, namekuji = false;
            foreach (UiTip tip in UiLoadingTips.All)
            {
                raiju |= tip.Group == UiTipGroup.Special && tip.Text.Contains("Raiju");
                namekuji |= tip.Group == UiTipGroup.Special && tip.Text.Contains("Namekuji");
            }

            Assert.IsTrue(raiju && namekuji, "Özel grup bu yapının iki Özel silahını anlatır");
        }

        /// <summary>
        /// R4 WP-105: every tip key has its row in the content pack, and the pack's English is the code's text (UXW: the
        /// English column is the code's text). Fails until S-09 adds the new rows (loading.tip.ctrl.05, econ.06, sw.07) to both
        /// r1-strings.json copies; tips.011/012/015/017 are R1 pack rows already.
        /// </summary>
        [Test]
        public void Ipuclari_HerAnahtarPakette_IngilizcesiKodlaAyni()
        {
            string language = UiStrings.Language;
            UiStrings.Language = UiStrings.English;
            try
            {
                var missing = new List<string>();
                foreach (UiTip tip in UiLoadingTips.All)
                {
                    if (!UiStrings.Has(tip.Key))
                    {
                        missing.Add(tip.Key);
                        continue;
                    }

                    Assert.AreEqual(tip.Text, UiStrings.Get(tip.Key, string.Empty), tip.Key + ": paketin İngilizcesi kodun metni");
                }

                Assert.IsEmpty(missing, "pakette olmayan ipucu anahtarı:\n" + string.Join("\n", missing));
            }
            finally
            {
                UiStrings.Language = language;
            }
        }

        /// <summary>
        /// R4 WP-105: a player who has already seen all nine old HINTs (mask 0x1FF, bits 1/2/3/6 belong to retired tips) still
        /// gets the two new Raiju HINTs first, then FACT.
        /// </summary>
        [Test]
        public void EskiMaskeDolu_YeniHintlerOnceGelir_SonraFact()
        {
            int mask = (1 << 9) - 1;
            int first = UiLoadingTips.Pick(true, ref mask);
            Assert.GreaterOrEqual(first, 0);
            Assert.AreEqual(UiTipClass.Hint, UiLoadingTips.All[first].Class, "eski maske: yeni HINT görülmemiş sayılır");
            Assert.GreaterOrEqual(UiLoadingTips.All[first].HintBit, 9);
            int second = UiLoadingTips.Pick(true, ref mask);
            Assert.AreEqual(UiTipClass.Hint, UiLoadingTips.All[second].Class);
            Assert.GreaterOrEqual(UiLoadingTips.All[second].HintBit, 9);
            Assert.AreEqual(ExpectedMask() | ((1 << 9) - 1), mask, "iki yeni bit eklendi");
            int third = UiLoadingTips.Pick(true, ref mask);
            Assert.AreEqual(UiTipClass.Fact, UiLoadingTips.All[third].Class, "HINT kalmadı: FACT");
        }

        /// <summary>R4 WP-105 (V-49): a stalled load offers RETURN TO MAIN MENU on the way anywhere but the main menu itself.</summary>
        [Test]
        public void YuklemeTakilmasi_AnaMenuDisindaGeriYolVar_AnaMenuyeGidendeYok()
        {
            Assert.IsFalse(PrototypeUiFlow.StallOffersWayBack(DD.Prototype.Core.Boot.PrototypeScenes.MainMenu), "ana menüye giden takıldı: geri yol yine aynı yük - yalnız QUIT");
            Assert.IsTrue(PrototypeUiFlow.StallOffersWayBack(DD.Prototype.Core.Boot.PrototypeScenes.Selection));
            Assert.IsTrue(PrototypeUiFlow.StallOffersWayBack(DD.Prototype.Core.Boot.PrototypeScenes.Run));
            Assert.IsTrue(PrototypeUiFlow.StallOffersWayBack(DD.Prototype.Core.Boot.PrototypeScenes.Results));
            Assert.IsFalse(PrototypeUiFlow.StallOffersWayBack(null));
        }

        private static int HintCount()
        {
            int n = 0;
            foreach (UiTip tip in UiLoadingTips.All)
            {
                if (tip.Class == UiTipClass.Hint) n++;
            }

            return n;
        }

        private static int ExpectedMask()
        {
            int mask = 0;
            foreach (UiTip tip in UiLoadingTips.All)
            {
                if (tip.Class == UiTipClass.Hint) mask |= 1 << tip.HintBit;
            }

            return mask;
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

        /// <summary>
        /// R4 WP-105 (V-49): the stall notice (now saying what comes next) stays one line at TextScale 1.25 - it hangs under
        /// the tip strip, 88 px above the screen's foot, and a second line would reach the edge. The TR row is measured too once
        /// S-09 has added it to the pack.
        /// </summary>
        [Test]
        public void T19_TakilmaBildirimi_125te_TekSatir()
        {
            UiTokens.TextScale = 1.25f;
            var host = new GameObject("Host", typeof(RectTransform));
            _created.Add(host);
            TMP_Text probe = UiFactory.Label("Probe", host.transform, string.Empty, UiType.Body, UiColor.TextPrimary, TextAnchor.UpperLeft);
            float width = UiCurtain.TipMaxWidth - UiNotices.TextX - (int)UiSpace.M;   // UiNotices.Notice's text box at the strip's width
            float oneLine = probe.GetPreferredValues("Ag", width, 0f).y;
            float twoLines = probe.GetPreferredValues("Ag\nAg", width, 0f).y;
            Assert.Greater(twoLines, oneLine + 1f, "ölçü çalışıyor");
            Assert.LessOrEqual(probe.GetPreferredValues(UiLoadingTips.StalledLine, width, 0f).y, oneLine + 0.5f, "EN bildirim 1,25'te tek satır");

            if (UiStrings.Has("loading.stalled.wait"))
            {
                string language = UiStrings.Language;
                UiStrings.Language = UiStrings.Turkish;
                try
                {
                    string tr = UiStrings.Get("loading.stalled.wait", UiLoadingTips.StalledLine);
                    Assert.LessOrEqual(probe.GetPreferredValues(tr, width, 0f).y, oneLine + 0.5f, "TR bildirim 1,25'te tek satır: " + tr);
                }
                finally
                {
                    UiStrings.Language = language;
                }
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

            Assert.AreEqual(HintCount(), hints, "her HINT bir kez");
            Assert.AreEqual(ExpectedMask(), mask, "HINT maskesi: bütün HINT bitleri, emekliler değil");

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
