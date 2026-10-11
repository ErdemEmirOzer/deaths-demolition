using System.Collections.Generic;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Difficulty;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-106 · Prepare Run's measured layout as arithmetic (PrepareRunLayout), the lock words (SelectionScreen.LockChipFor /
    /// LockLineFor), the short tip pool and the difficulty seam - no scene, no canvas. The sizes are the gates': the 1080p page
    /// (1728 wide) and the Mac canvas at UI scale 1.2 (1408 wide), text 1.0 and 1.25.
    /// </summary>
    [TestFixture]
    [Category("WP106")]
    public sealed class PrepareRunLayoutTests
    {
        [SetUp]
        public void SetUp()
        {
            UiStrings.ResetForTests();
            UiStrings.Language = UiStrings.English;
            DifficultyService.ResetForTests();
            PrepareDifficulty.SetSourceForTests(null);
        }

        [TearDown]
        public void TearDown()
        {
            UiStrings.Language = UiStrings.English;
            DifficultyService.ResetForTests();
            PrepareDifficulty.SetSourceForTests(null);
        }

        // ------------------------------------------------------------------ the page

        [Test]
        public void Sutunlar_1728IzgarasindaDolar_SiraKurulumSahneSilah()
        {
            Assert.AreEqual(456f, PrepareRunLayout.SetupShare * 1728f, 0.01f);
            Assert.AreEqual(480f, PrepareRunLayout.StageFrom * 1728f, 0.01f);
            Assert.AreEqual(1144f, PrepareRunLayout.StageTo * 1728f, 0.01f);
            Assert.AreEqual(1168f, PrepareRunLayout.WeaponsFrom * 1728f, 0.01f);
            Assert.AreEqual(560f, (1f - PrepareRunLayout.WeaponsFrom) * 1728f, 0.01f, "the weapons column keeps its v3 width");
        }

        // ------------------------------------------------------------------ stat cards

        [Test]
        public void StatKartlari_BesSigarsaTekSira_SigmazsaUcArtiIki_SonraIkili_SonraTekli()
        {
            var fits = new List<float> { 113f, 112f, 110f, 90f, 100f };   // five cells of (616 - 4 × 12) / 5 = 113.6
            Assert.AreEqual(5, PrepareRunLayout.StatColumns(616f, 12f, fits), "1080p stage (616): five cards of 113.6 hold 113-wide words");
            Assert.AreEqual(3, PrepareRunLayout.StatColumns(616f, 12f, new List<float> { 120f, 118f, 110f, 90f, 100f }), "a 120-wide word: three over two");
            var wide = new List<float> { 120f, 160f, 110f, 90f, 100f };
            Assert.AreEqual(3, PrepareRunLayout.StatColumns(616f, 12f, wide), "one wider card: three over two");
            Assert.AreEqual(2, PrepareRunLayout.Rows(5, 3));
            var wider = new List<float> { 120f, 250f, 110f, 90f, 100f };
            Assert.AreEqual(2, PrepareRunLayout.StatColumns(616f, 12f, wider), "a card wider than a third: two per row");
            var widest = new List<float> { 120f, 400f, 110f, 90f, 100f };
            Assert.AreEqual(1, PrepareRunLayout.StatColumns(616f, 12f, widest), "a card wider than half: one per row");
            Assert.AreEqual(1, PrepareRunLayout.StatColumns(616f, 12f, new List<float>()), "no card: one column, no division by zero");

            for (int columns = 1; columns <= 5; columns++)
            {
                float cell = PrepareRunLayout.CellWidth(616f, 12f, columns);
                Assert.LessOrEqual(cell * columns + 12f * (columns - 1), 616f + 0.01f, columns + " columns stay inside the card");
            }
        }

        [Test]
        public void StatKartlari_SacmaliSilahtaYayilma_DigerlerindeMenzil_HerZamanAtisModu()
        {
            var rifle = ScriptableObject.CreateInstance<WeaponDefinition>();
            var shotgun = ScriptableObject.CreateInstance<WeaponDefinition>();
            var launcher = ScriptableObject.CreateInstance<WeaponDefinition>();
            try
            {
                rifle.fire.pelletCount = 1;
                shotgun.fire.pelletCount = 8;
                launcher.fire.pelletCount = 1;
                launcher.explosive.enabled = true;
                string[] r = PrepareRunLayout.CardKeys(rifle);
                string[] s = PrepareRunLayout.CardKeys(shotgun);
                Assert.AreEqual(PrepareRunLayout.StatCount, r.Length);
                Assert.AreEqual(PrepareRunLayout.StatCount, s.Length);
                CollectionAssert.AreEqual(new[] { WeaponStatReadout.Damage, WeaponStatReadout.FireRate, WeaponStatReadout.Magazine, WeaponStatReadout.Range, PrepareRunLayout.FireModeKey }, r);
                Assert.AreEqual(PrepareRunLayout.SpreadKey, s[3], "a pellet weapon shows its cone");
                Assert.AreEqual(PrepareRunLayout.FireModeKey, s[4]);
                Assert.AreEqual(WeaponStatReadout.Range, PrepareRunLayout.CardKeys(launcher)[3], "a blast weapon keeps its reach");
                Assert.AreEqual(WeaponStatReadout.Range, PrepareRunLayout.CardKeys(null)[3], "no definition: the bullet set");
            }
            finally
            {
                Object.DestroyImmediate(rifle);
                Object.DestroyImmediate(shotgun);
                Object.DestroyImmediate(launcher);
            }
        }

        // ------------------------------------------------------------------ the stage

        [Test]
        public void Sahne_OnizlemeArtaniAlir_KisaysaOnceBosluklarKapanir_SonraOnizlemeKuculur_MetinHicVermez()
        {
            // 1080p: 708 of content, ~240 of text → the preview takes the rest at full gaps.
            PrepareRunLayout.StageBands roomy = PrepareRunLayout.Stage(708f, 240f, 16f, 8f, 120f, 56f, 492f);
            Assert.AreEqual(16f, roomy.Gap);
            Assert.AreEqual(708f - 240f - 48f, roomy.Preview);

            // Short: the gaps close first so the preview reaches its minimum.
            PrepareRunLayout.StageBands tight = PrepareRunLayout.Stage(400f, 250f, 16f, 8f, 120f, 56f, 492f);
            Assert.Less(tight.Gap, 16f);
            Assert.GreaterOrEqual(tight.Gap, 8f);
            Assert.AreEqual(400f, 250f + tight.Gap * 3f + tight.Preview, 1f, "the bands fill the card exactly");

            // Shorter: the gaps at their floor, the preview below its minimum but still drawn.
            PrepareRunLayout.StageBands shorter = PrepareRunLayout.Stage(360f, 250f, 16f, 8f, 120f, 56f, 492f);
            Assert.AreEqual(8f, shorter.Gap);
            Assert.AreEqual(360f - 250f - 24f, shorter.Preview);

            // A tall 4:3 page (1074 of content): the preview stops at 0.8 × the card's width and stands in the middle of its band.
            PrepareRunLayout.StageBands tall = PrepareRunLayout.Stage(1074f, 240f, 16f, 8f, 120f, 56f, 492f);
            Assert.AreEqual(492f, tall.Preview);
            Assert.AreEqual(16f, tall.Gap);
            Assert.AreEqual(1074f - 240f - 48f - 492f, tall.Slack);
            Assert.AreEqual(0f, roomy.Slack, "uncapped: no slack");

            // No room: the preview goes, the texts keep theirs (never a cut word).
            PrepareRunLayout.StageBands none = PrepareRunLayout.Stage(300f, 250f, 16f, 8f, 120f, 56f, 492f);
            Assert.AreEqual(0f, none.Preview);
            Assert.AreEqual(0f, none.Slack);
        }

        // ------------------------------------------------------------------ the setup column

        [Test]
        public void Kurulum_BosluklarSonraHaritaGorseliSonraIpucuSonraGorsel_ZorlukVeAdHepYerinde()
        {
            // 1080p: everything fits at its ideal.
            PrepareRunLayout.SetupBands roomy = PrepareRunLayout.Setup(756f, 260f, 70f, 40f, 256f, 72f, 24f, 12f);
            Assert.AreEqual(24f, roomy.Gap);
            Assert.AreEqual(256f, roomy.Thumb);
            Assert.IsTrue(roomy.Tip);

            // Mac 16:9 at UI 1.2 × text 1.25: the gaps close, the art shrinks, the tip stays.
            PrepareRunLayout.SetupBands mac = PrepareRunLayout.Setup(580f, 300f, 90f, 50f, 209f, 72f, 24f, 12f);
            Assert.AreEqual(12f, mac.Gap);
            Assert.IsTrue(mac.Tip);
            Assert.AreEqual(580f - 300f - 90f - 50f - 24f, mac.Thumb, 1f);
            Assert.GreaterOrEqual(mac.Thumb, 72f);

            // Shorter: the tip goes before the art drops under its minimum.
            PrepareRunLayout.SetupBands tighter = PrepareRunLayout.Setup(480f, 300f, 90f, 50f, 209f, 72f, 24f, 12f);
            Assert.IsFalse(tighter.Tip);
            Assert.GreaterOrEqual(tighter.Thumb, 72f);
            Assert.LessOrEqual(300f + tighter.Gap + 90f + tighter.Thumb, 480f + 0.5f);

            // No room for the art at all: it goes; the difficulty and the map's words stay.
            PrepareRunLayout.SetupBands none = PrepareRunLayout.Setup(420f, 300f, 90f, 50f, 209f, 72f, 24f, 12f);
            Assert.IsFalse(none.Tip);
            Assert.AreEqual(0f, none.Thumb);

            // No tip qualified: no tip band is planned.
            Assert.IsFalse(PrepareRunLayout.Setup(756f, 260f, 70f, 0f, 256f, 72f, 24f, 12f).Tip);
        }

        [Test]
        public void HaritaGorseli_KapakSigdirma_GenisKutudaUstOdak_DarKutudaOrtaSerit()
        {
            // L01 is 2560 × 1440 (16:9); a 456 × 160 box is wider: the full width, a band of the height near the top.
            Rect wide = PrepareRunLayout.CoverUv(2560f, 1440f, 456f, 160f, 0.62f);
            Assert.AreEqual(0f, wide.x);
            Assert.AreEqual(1f, wide.width);
            Assert.AreEqual((2560f / 1440f) / (456f / 160f), wide.height, 1e-4f);
            Assert.AreEqual(0.62f, wide.center.y, 1e-4f, "the band is centred on the focus");
            Assert.LessOrEqual(wide.yMax, 1f + 1e-5f);

            // A focus too high is clamped inside the art.
            Rect top = PrepareRunLayout.CoverUv(2560f, 1440f, 456f, 100f, 0.95f);
            Assert.AreEqual(1f, top.yMax, 1e-5f);

            // A narrower box: the full height, the centre strip.
            Rect narrow = PrepareRunLayout.CoverUv(2560f, 1440f, 200f, 200f, 0.62f);
            Assert.AreEqual(1f, narrow.height);
            Assert.AreEqual(0.5f, narrow.center.x, 1e-5f);
            Assert.AreEqual(1440f / 2560f, narrow.width, 1e-4f);

            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), PrepareRunLayout.CoverUv(0f, 0f, 10f, 10f, 0.5f), "no art: the whole rect");
        }

        // ------------------------------------------------------------------ the Specials tiles (U2)

        [Test]
        public void OzelKarolar_AdVeModYanYanaSigmazsaUstUste_YukseklikIkiSatir()
        {
            // 1080p tile (~276): NAMEKUJI (90) and LOB at 80 % (24) beside a 40 px side view.
            Assert.IsTrue(PrepareRunLayout.SpecialBeside(276f, 40f, 90f, 24f, 12f, 8f));
            // The Mac canvas at text 1.25 (tile ~224): NAMEKUJİ (118) and YAY ATIŞ at 80 % (62) do not fit → stacked.
            Assert.IsFalse(PrepareRunLayout.SpecialBeside(224f, 40f, 118f, 62f, 12f, 8f));
            Assert.AreEqual(48f, PrepareRunLayout.SpecialTileHeight(true, 48f, 24f, 22f, 8f), "beside: the one band");
            Assert.AreEqual(62f, PrepareRunLayout.SpecialTileHeight(false, 48f, 24f, 22f, 8f), "stacked: two lines and their padding");
            Assert.AreEqual(48f, PrepareRunLayout.SpecialTileHeight(false, 48f, 14f, 12f, 4f), "never under the row's minimum");
        }

        // ------------------------------------------------------------------ navigation

        [Test]
        public void Gezinme_SilahSatirindanSolKurulumSatirina_UclarKenetli()
        {
            Assert.AreEqual(0, PrepareRunLayout.SetupRowFor(0, 10, 3));
            Assert.AreEqual(1, PrepareRunLayout.SetupRowFor(4, 10, 3));
            Assert.AreEqual(2, PrepareRunLayout.SetupRowFor(9, 10, 3));
            Assert.AreEqual(0, PrepareRunLayout.SetupRowFor(5, 10, 1), "one mode: every weapon row lands on it");
            Assert.AreEqual(-1, PrepareRunLayout.SetupRowFor(3, 10, 0), "no setup rows: no target");
            for (int i = 0; i < 10; i++)
            {
                int row = PrepareRunLayout.SetupRowFor(i, 10, 3);
                Assert.That(row, Is.InRange(0, 2));
            }
        }

        // ------------------------------------------------------------------ the lock words

        [Test]
        public void KilitCipi_SeviyePakettenGelir_DuvardanSatinAlmaWp107_YolKalibiKorunur_EnTr()
        {
            var ladder = new MetaPathRules.WeaponLockInfo { Ladder = true, LevelGate = 3, LockReason = MetaLockReason.LadderWeapon };
            var wallBuy = new MetaPathRules.WeaponLockInfo { LevelGate = 99, LockReason = MetaLockReason.WallBuyOnly };
            var path = new MetaPathRules.WeaponLockInfo { OnPath = true, LevelGate = 11, LockReason = MetaLockReason.LevelGate };

            Assert.AreEqual("LEVEL 3", SelectionScreen.LockChipFor(ladder), "selection.lock.chip, no bare L3 any more");
            Assert.AreEqual("WALL BUY", SelectionScreen.LockChipFor(wallBuy));
            Assert.IsTrue(PrepareRunLayout.IsWallBuyOnly(wallBuy));
            Assert.IsFalse(PrepareRunLayout.IsWallBuyOnly(ladder));
            Assert.AreEqual("PATH · L11", SelectionScreen.LockChipFor(path), "the PATH chip keeps the pack's L{level} form");
            StringAssert.StartsWith("Bought at its wall", SelectionScreen.LockLineFor(wallBuy));

            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual("SEVİYE 3", SelectionScreen.LockChipFor(ladder));
            Assert.AreEqual("DUVARDAN", SelectionScreen.LockChipFor(wallBuy));
            StringAssert.DoesNotContain("{", SelectionScreen.LockLineFor(wallBuy));
        }

        // ------------------------------------------------------------------ the tip

        [Test]
        public void Ipucu_TekKisaSatir_EkonomiCeliskileriYok_OnceFactEnKisaOnce_HintYalnizIpuclariAcikken()
        {
            int[] off = PrepareRunLayout.TipPool(false);
            int[] on = PrepareRunLayout.TipPool(true);
            Assert.Greater(off.Length, 0, "at least one short FACT tip");
            Assert.GreaterOrEqual(on.Length, off.Length);
            int factLength = 0, hintLength = 0;
            bool hintSeen = false;
            foreach (int i in on)
            {
                UiTip tip = UiLoadingTips.All[i];
                Assert.LessOrEqual(tip.Text.Length, PrepareRunLayout.TipMaxChars, tip.Key + ": one short line");
                CollectionAssert.DoesNotContain(PrepareRunLayout.TipExcluded, tip.Key, tip.Key + ": the R4 tree contradicts it");
                if (tip.Class == UiTipClass.Hint)
                {
                    hintSeen = true;
                    Assert.GreaterOrEqual(tip.Text.Length, hintLength, tip.Key + ": shortest first among the hints");
                    hintLength = tip.Text.Length;
                }
                else
                {
                    Assert.IsFalse(hintSeen, tip.Key + ": FACT tips come first");
                    Assert.GreaterOrEqual(tip.Text.Length, factLength, tip.Key + ": shortest first among the facts");
                    factLength = tip.Text.Length;
                }
            }

            foreach (int i in off)
            {
                Assert.AreEqual(UiTipClass.Fact, UiLoadingTips.All[i].Class, "hints off: FACT only");
            }

            // The rotation: one step per opening, wrapping; the loading screen's session is not touched.
            Assert.AreEqual(off[0], PrepareRunLayout.TipFor(off, 0));
            Assert.AreEqual(off[1 % off.Length], PrepareRunLayout.TipFor(off, 1));
            Assert.AreEqual(off[0], PrepareRunLayout.TipFor(off, off.Length));
            Assert.AreEqual(-1, PrepareRunLayout.TipFor(new int[0], 3));
            Assert.AreEqual(-1, PrepareRunLayout.TipFor(null, 0));
        }

        // ------------------------------------------------------------------ the difficulty seam

        [Test]
        public void Zorluk_UcModWp102Sirasinda_SecimServiseYazilir_KilitliSecilmez()
        {
            IPrepareDifficulty source = PrepareDifficulty.Source;
            Assert.AreEqual(3, source.Count);
            Assert.AreEqual("normal", source.IdAt(0));
            Assert.AreEqual("hard", source.IdAt(1));
            Assert.AreEqual("demolition", source.IdAt(2));
            Assert.IsNull(source.IdAt(3));
            Assert.AreEqual(1, source.LevelAt(0));
            Assert.AreEqual(3, source.LevelAt(2));
            Assert.AreEqual(0, source.Selected, "Normal by default");
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(source.IsLockedAt(i), "WP-102: every mode open");
            }

            Assert.IsTrue(source.Select(2));
            Assert.AreEqual(DifficultyMode.Demolition, DifficultyService.Selected, "the choice is the service's");
            Assert.AreEqual(2, source.Selected);
            Assert.IsFalse(source.Select(5), "out of range");
            Assert.AreEqual(DifficultyMode.Demolition, DifficultyService.Selected);

            var locked = new FakeDifficulty();
            PrepareDifficulty.SetSourceForTests(locked);
            Assert.AreSame(locked, PrepareDifficulty.Source);
            Assert.IsFalse(PrepareDifficulty.Source.Select(1), "a locked mode refuses");
            Assert.AreEqual(0, locked.Selected);
        }

        /// <summary>
        /// S-03 (12.10): a <c>-ddDifficulty</c> launch fixes the mode for the process - the menu's writes are ignored - so the
        /// overridden mode is the chosen row and the other rows are locked (a press is refused, nothing changes).
        /// </summary>
        [Test]
        public void Zorluk_BaslatmaGecersizKilmasi_SeciliSatirKalir_DigerleriKilitli()
        {
            PrepareDifficulty.SetSourceForTests(null);
            DifficultyService.SetLaunchOverrideForTests(DifficultyMode.Hard);
            IPrepareDifficulty source = PrepareDifficulty.Source;
            Assert.AreEqual(1, source.Selected, "the launch mode is the chosen row");
            Assert.IsTrue(source.IsLockedAt(0));
            Assert.IsFalse(source.IsLockedAt(1));
            Assert.IsTrue(source.IsLockedAt(2));
            Assert.IsFalse(source.Select(2), "a locked row refuses");
            Assert.AreEqual(DifficultyMode.Hard, DifficultyService.Selected);

            DifficultyService.ResetForTests();
            Assert.IsFalse(source.IsLockedAt(2), "without the override every mode is open");
        }

        private sealed class FakeDifficulty : IPrepareDifficulty
        {
            public int Count => 2;
            public string IdAt(int index) => index == 0 ? "normal" : "hard";
            public int LevelAt(int index) => index + 1;
            public bool IsLockedAt(int index) => index == 1;
            public int Selected { get; private set; }

            public bool Select(int index)
            {
                if (index < 0 || index >= Count || IsLockedAt(index))
                {
                    return false;
                }

                Selected = index;
                return true;
            }
        }
    }
}
