using System.Collections.Generic;
using DD.Prototype.UI;
using DD.Prototype.UI.EditorTools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UI;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-098A (C-00 08.10; HAREKET-MENU §1–§7, DEC-114) · menu v5 motion on the test clock (UiMotion.TestNow + Tick):
    ///   tokens     the DEC-114 Icon family, UiEase.Sweep (.4, 0, .2, 1) exported to UiTokens.hlsl, the menu's §7 numbers inside
    ///              the feel budget (MaxLoopHz 1.1, the FlashGate area, the 1.2 s arrival);
    ///   icon       the idle arbiter (one icon, one pass a focus episode, at most one idle run - T6), its suppressions, the
    ///              press contact before a Submit's choice, Reduced Motion at rest at once and mid-track, ≤ 3 hover traces in fast
    ///              travel, 0 B per Play / Tick;
    ///   ink        the wave's 80 ms dwell gate, 400 ms interval, three at once, none under RM; the splash's interval, its
    ///              FlashGate on the hero, FlashScale, none under RM;
    ///   panes      PaneOpen / PaneClose (the presets S-04's windows share) and the row stagger's shared sixth delay.
    /// </summary>
    [TestFixture]
    [Category("WP098")]
    public sealed class MenuV5MotionTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private bool _reduced;
        private float _flash;
        private RectTransform _root;

        [SetUp]
        public void SetUp()
        {
            _reduced = UiMotion.ReducedMotion;
            _flash = UiTokens.FlashScale;
            UiMotion.ReducedMotion = false;
            UiTokens.FlashScale = 1f;
            UiMotion.ResetForTests();
            UiInkWave.ResetForTests();
            UiMotion.TestNow = 200f;
            UiIconMotion.FocusForTests = true;
            UiFxGlobals.IconTierOverride = 1f;
            var go = new GameObject("Root", typeof(RectTransform));
            _created.Add(go);
            _root = (RectTransform)go.transform;
            _root.sizeDelta = new Vector2(1920f, 1080f);
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedMotion = _reduced;
            UiTokens.FlashScale = _flash;
            UiMotion.ResetForTests();
            UiInkWave.ResetForTests();
            UiMotion.TestNow = null;
            UiFxGlobals.IconTierOverride = null;
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        private static void Step(float seconds)
        {
            UiMotion.TestNow += seconds;
            UiMotion.Tick();
        }

        private UiIconButton Icon(string key, string nameKey, string fallback)
        {
            return UiIconControls.Button(_root, key, nameKey, fallback, null, UiIconSize.L, null, null, UiIconLabel.Beside, UiButtonVariant.Ghost);
        }

        private UiIconButton[] Rail(int count)
        {
            string[] keys = { "Menu.Play", "Menu.Profile", "Menu.Statistics", "Menu.Achievements", "Menu.Settings", "Menu.Controls", "Menu.Accessibility", "Menu.Quit" };
            var icons = new UiIconButton[count];
            for (int i = 0; i < count; i++)
            {
                icons[i] = Icon(keys[i % keys.Length], "menu.tab.profile", "ITEM " + i);
            }

            return icons;
        }

        private static void Focus(UiIconButton icon)
        {
            icon.Button.OnSelect(new BaseEventData(null));
        }

        private static void Unfocus(UiIconButton icon)
        {
            icon.Button.OnDeselect(new BaseEventData(null));
        }

        // ================================================================== tokens

        /// <summary>
        /// R4 WP-105 (user note 6: "the main menu's Deaths Demolition is the loading screen's own, with the logo beside it") ·
        /// the title lockup is the AP-035 wordmark + seal side by side, as wide as the spine (never wider), in AP-042's proportions
        /// (seal box 400 / 522 of the wordmark's, gap 48 / 522), capped at the 1080p size on a wide page; both sprites resolve.
        /// </summary>
        [Test]
        public void Baslik_WordmarkVeMuhur_OmurgaGenisliginde_MurekkepBoylariEsit()
        {
            foreach (float spine in new[] { 324f, 366f, MainMenuScreen.TitleLockupReferenceWidth, 640f })
            {
                Vector2 size = MainMenuScreen.TitleLockup(spine, out Vector2 seal, out Vector2 word);
                Assert.LessOrEqual(size.x, spine + 1f, "spine " + spine + ": the lockup stays in the spine");
                Assert.AreEqual(seal.x + MainMenuScreen.TitleGap(word.y) + word.x, size.x, 0.01f, "seal + gap + wordmark");
                Assert.AreEqual(word.x / word.y, MainMenuScreen.WordmarkAspect, 0.02f, "the wordmark keeps its 2048:768");
                Assert.AreEqual(400f / 522f, seal.y / word.y, 0.02f, "AP-042 title-lockup-layout: the seal's box is 400 / 522 of the wordmark's");
                Assert.LessOrEqual(size.y, MainMenuScreen.TitleLockupMaxHeight, "capped on a wide page");
                Debug.Log("[DD-TEST] wp105.titleLockup spine=" + spine + " seal=" + seal + " wordmark=" + word + " size=" + size);
            }

            Vector2 ref1080 = MainMenuScreen.TitleLockup(MainMenuScreen.TitleLockupReferenceWidth, out _, out Vector2 w1080);
            Assert.GreaterOrEqual(w1080.y * (650f / 768f), 96f, "1080p: the wordmark's two lines ≥ 96 px of ink (the v4 Display title's; ink 650 of the sprite's 768)");
            Assert.IsNotNull(Resources.Load<Sprite>(UiLogoAssets.WordmarkResource), "the loading screen's wordmark sprite");
            Assert.IsNotNull(Resources.Load<Sprite>(UiLogoAssets.SealResource), "the loading screen's seal sprite");
            Assert.Greater(ref1080.x, 0f);
        }

        [Test]
        public void Tokenlar_DEC114_IkonAilesi_Sweep_MenuButcesi()
        {
            Assert.AreEqual(0.6f, UiMotionTokens.IconNotify, 1e-6f, "DEC-114 IconNotify 600");
            Assert.AreEqual(3.6f, UiMotionTokens.IconIdle, 1e-6f, "DEC-114 IconIdle 3600");
            Assert.AreEqual(2.4f, UiMotionTokens.IconIdleDelay, 1e-6f, "DEC-114 IconIdleDelay 2.4 s");
            Assert.AreEqual(new Vector4(0.4f, 0f, 0.2f, 1f), UiEaseCurve.ControlPoints(UiEase.Sweep), "DEC-114 Sweep (.4, 0, .2, 1)");
            Assert.LessOrEqual(1f / UiMotionTokens.IconIdle, 0.28f, "≈ 0.28 Hz");

            // The family's transform parts end inside Emphasis; the longer tails are light only (council §2.4).
            float emphasis = UiMotionTokens.Seconds(UiDuration.Emphasis);
            foreach (float transform in new[] { UiMotionTokens.IconPress, UiMotionTokens.IconLift, UiMotionTokens.IconDisable, UiMotionTokens.IconChooseTransform, UiMotionTokens.IconSettle })
            {
                Assert.LessOrEqual(transform, emphasis);
            }

            Assert.AreEqual(UiMotionTokens.Seconds(UiDuration.Instant), UiMenuMotionTokens.HoverDwellGate, 1e-6f, "HoverDwellGate = Instant");
            Assert.Less(UiMenuMotionTokens.InkWaveAlpha, UiTokens.OpacityHoverWash, "the wave stays under the hover wash");
            Assert.Less(UiMenuMotionTokens.SplashSize * UiMenuMotionTokens.SplashSize, UiMotionTokens.FlashGateAreaPx, "the cream splash is a small flash");
            Assert.GreaterOrEqual(UiMenuMotionTokens.SplashSizeHero * UiMenuMotionTokens.SplashSizeHero, UiMotionTokens.FlashGateAreaPx, "PLAY's splash goes through the gate");
            Assert.LessOrEqual(UiMenuMotionTokens.MenuIntroMax, MenuCinematic.Hold + 1e-4f, "the arrival fits the world's opening hold");
            Assert.LessOrEqual(UiMenuMotionTokens.LogoIntroMax, 1.6f + 1e-4f);
            Assert.LessOrEqual(1f / UiMenuMotionTokens.BreathPeriod, 0.25f + 1e-4f, "breath ≤ 0.25 Hz");
            Assert.LessOrEqual(1f / UiMenuMotionTokens.AshPeriod, UiMotionTokens.MaxLoopHz);
            Assert.LessOrEqual(1f / UiMenuMotionTokens.LeafPeriod, UiMotionTokens.MaxLoopHz);
            Assert.AreEqual(UiMotionTokens.Seconds(UiDuration.Screen), UiMenuMotionTokens.FreezeDelay, 1e-6f, "§5-3: the freeze Screen 280 after the open");

            string hlsl = UiTokenExport.BuildHlsl();
            StringAssert.Contains("#define DD_UI_EASE_SWEEP float4(0.4, 0, 0.2, 1)\n", hlsl, "Sweep exported for the shader");
            StringAssert.Contains("#define DD_UI_ICON_LIFT float2(2, 1.5714)\n", hlsl, "the lifted lob exported");
            StringAssert.Contains("\"sweep\": { \"$type\": \"cubicBezier\", \"$value\": [0.4, 0, 0.2, 1] }", UiTokenExport.BuildJson());
        }

        // ================================================================== icon tracks and the idle arbiter (DEC-114)

        /// <summary>DEC-114 T6: three icons; focus on A → one pass 2.4 s later, 3.6 s long, then quiet; focus on C → C passes once. ≤ 1 idle run at every tick.</summary>
        [Test]
        public void IkonBekleme_TekIkon_TekDongu()
        {
            UiIconButton[] icons = Rail(3);
            Step(0.1f);
            Focus(icons[0]);
            Assert.AreEqual(UiIconTrack.Lift, UiIconMotion.TrackOf(icons[0]), "focus lifts at once");
            Assert.AreSame(icons[0], UiIconMotion.IdleCandidate);

            int passesA = 0, passesB = 0, passesC = 0;
            bool wasIdleA = false, wasIdleC = false;
            float idleStartedAt = -1f;
            float focusAt = UiMotion.TestNow.Value;
            for (int i = 0; i < 16 * 20; i++)   // 16 s
            {
                Step(0.05f);
                Assert.LessOrEqual(UiIconMotion.IdleRuns, 1, "at most one idle run");
                bool idleA = UiIconMotion.TrackOf(icons[0]) == UiIconTrack.Idle;
                if (idleA && !wasIdleA)
                {
                    passesA++;
                    idleStartedAt = UiMotion.TestNow.Value;
                }

                wasIdleA = idleA;
                if (UiIconMotion.TrackOf(icons[1]) == UiIconTrack.Idle) passesB++;
            }

            Assert.AreEqual(1, passesA, "one pass per focus episode");
            Assert.AreEqual(UiMotionTokens.IconIdleDelay, idleStartedAt - focusAt, 0.06f, "2.4 s after focus");
            Assert.AreEqual(0, passesB, "an unfocused icon never idles");
            Assert.AreEqual(UiIconPose.Lifted.Sheen, icons[0].ShownPose.Sheen, 1e-3f, "the pass ends on the lifted rest");

            Unfocus(icons[0]);
            Focus(icons[2]);
            for (int i = 0; i < 8 * 20; i++)
            {
                Step(0.05f);
                Assert.LessOrEqual(UiIconMotion.IdleRuns, 1);
                bool idleC = UiIconMotion.TrackOf(icons[2]) == UiIconTrack.Idle;
                if (idleC && !wasIdleC) passesC++;
                wasIdleC = idleC;
                Assert.AreNotEqual(UiIconTrack.Idle, UiIconMotion.TrackOf(icons[0]), "the old focus never passes again");
            }

            Assert.AreEqual(1, passesC, "the new focus passes once");
        }

        [Test]
        public void IkonBekleme_Bastirmalar_PencereOdagi_Kahraman_Kapsam_Kademe()
        {
            UiIconButton[] icons = Rail(2);
            Step(0.1f);

            // The window away: no pass; back: the calm re-arms the delay, then one pass.
            UiIconMotion.FocusForTests = false;
            Focus(icons[0]);
            for (int i = 0; i < 60; i++) Step(0.05f);
            Assert.AreEqual(0, UiIconMotion.IdleRuns, "window unfocused: no idle");
            UiIconMotion.FocusForTests = true;
            Step(1f);
            Assert.AreEqual(0, UiIconMotion.IdleRuns, "the delay re-arms after the calm returns");
            for (int i = 0; i < 40; i++) Step(0.05f);
            Assert.AreEqual(1, UiIconMotion.IdleRuns, "then it passes");

            // A hero choreography drops a running pass (not spent: it comes back after another calm delay).
            UiIconMotion.HeroActive = true;
            Step(0.05f);
            Assert.AreEqual(0, UiIconMotion.IdleRuns, "hero choreography: dropped");
            Assert.AreEqual(UiIconPose.Lifted.Sheen, icons[0].ShownPose.Sheen, 1e-4f, "dropped: the sheen jumps to its rest, never swept back");
            UiIconMotion.HeroActive = false;
            for (int i = 0; i < 60; i++) Step(0.05f);
            Assert.AreEqual(1, UiIconMotion.IdleRuns, "not spent: it passes after the calm");

            // Outside the scope (a pane open, focus off the rail): none. The Performance tier: none.
            Unfocus(icons[0]);
            var other = new GameObject("Elsewhere", typeof(RectTransform));
            _created.Add(other);
            UiIconMotion.IdleScope = (RectTransform)other.transform;
            Focus(icons[1]);
            for (int i = 0; i < 70; i++) Step(0.05f);
            Assert.AreEqual(0, UiIconMotion.IdleRuns, "focus outside the idle scope");
            UiIconMotion.IdleScope = null;
            UiFxGlobals.IconTierOverride = 0f;
            for (int i = 0; i < 70; i++) Step(0.05f);
            Assert.AreEqual(0, UiIconMotion.IdleRuns, "Performance tier: the sheen is off, no pass");

            // Reduced Motion: never.
            UiFxGlobals.IconTierOverride = 1f;
            UiMotion.ReducedMotion = true;
            for (int i = 0; i < 70; i++) Step(0.05f);
            Assert.AreEqual(0, UiIconMotion.ActiveRuns, "RM: no track at all");
        }

        /// <summary>M9 + council §1.4/3: a pad Submit presses and chooses in one frame - the choice waits for the press's 50 ms contact.</summary>
        [Test]
        public void IkonHareket_SubmitBasmaTemasi_SecimBekler_SonraSecimDinlenmesi()
        {
            UiIconButton icon = Icon("Menu.Settings", "menu.tab.settings", "SETTINGS");
            Step(0.1f);
            icon.Button.OnSubmit(new BaseEventData(null));
            Assert.IsTrue(icon.Button.Pulsing, "Submit shows the press (PressPulse)");
            Assert.AreEqual(UiIconTrack.Press, UiIconMotion.TrackOf(icon));
            icon.Button.SetSelectedChoice(true);
            Assert.AreEqual(UiIconTrack.Press, UiIconMotion.TrackOf(icon), "the choice waits for the contact");

            Step(0.03f);
            Assert.Less(icon.ShownPose.Scale, 1f, "pressing toward 0.94");
            Step(0.03f);
            Assert.AreEqual(UiIconTrack.Choose, UiIconMotion.TrackOf(icon), "past the contact the choice plays");
            Assert.AreEqual(0.75f, icon.ShownPose.Draw, 0.02f, "the brush front starts at 0.75");
            Step(0.1f);
            Assert.IsFalse(icon.Button.Pulsing, "the pulse released after Instant 80");
            for (int i = 0; i < 10; i++) Step(0.05f);
            Assert.AreEqual(UiIconTrack.None, UiIconMotion.TrackOf(icon));
            Vector3 light = icon.IconMaterial.IconLight;
            Assert.AreEqual(UiIconPose.Chosen.Sheen, light.x, 1e-4f, "chosen rest: sheen 1 (HAREKET-MENU §3)");
            Assert.AreEqual(UiIconPose.Chosen.Glow, light.y, 1e-4f, "chosen rest: glow 0.04");
            Assert.AreEqual(0f, light.z, 1e-4f, "no spread");
            Assert.AreEqual(1f, icon.IconMaterial.Parameters.x, 1e-4f, "drawn whole");
            Assert.AreEqual(1f, icon.Glyph.rectTransform.localScale.x, 1e-4f, "chosen, not lifted: scale 1");
        }

        [Test]
        public void IkonHareket_AzaltilmisHareket_AnindaDinlenme_IzOrtasindaBitis()
        {
            UiIconButton a = Icon("Menu.Profile", "menu.tab.profile", "PROFILE");
            UiIconButton b = Icon("Menu.Settings", "menu.tab.settings", "SETTINGS");
            Step(0.1f);
            UiMotion.ReducedMotion = true;
            Focus(a);
            Assert.AreEqual(0, UiIconMotion.ActiveRuns, "RM: no track");
            Assert.AreEqual(UiFx.IconRestLight, a.IconMaterial.IconLight, "RM rest: sheen off, no glow");
            Assert.AreEqual(1f, a.Glyph.rectTransform.localScale.x, 1e-5f);

            UiMotion.ReducedMotion = false;
            Focus(b);
            Step(0.05f);
            Assert.AreEqual(UiIconTrack.Lift, UiIconMotion.TrackOf(b));
            UiMotion.ReducedMotion = true;
            Step(0.016f);
            Assert.AreEqual(0, UiIconMotion.ActiveRuns, "RM mid-track: landed on the next tick");
            Assert.AreEqual(UiFx.IconRestLight, b.IconMaterial.IconLight);
            Assert.AreEqual(0f, b.IconMaterial.IconLift, 1e-5f, "the lob back at Elev1");
        }

        /// <summary>M7 / HAREKET-MENU §2: 10 steps a second down eight icons - never more than three hover traces; the last lands lifted.</summary>
        [Test]
        public void IkonHareket_HizliGezinme_EnCokUcIz_SonPozDogru()
        {
            UiIconButton[] icons = Rail(8);
            Step(0.1f);
            UiIconButton previous = null;
            for (int s = 0; s < 30; s++)
            {
                UiIconButton next = icons[s % icons.Length];
                if (previous != null) Unfocus(previous);
                Focus(next);
                previous = next;
                for (int f = 0; f < 6; f++)
                {
                    Step(1f / 60f);
                    int hover = UiIconMotion.CountOf(UiIconTrack.Lift) + UiIconMotion.CountOf(UiIconTrack.Settle);
                    Assert.LessOrEqual(hover, UiMenuMotionTokens.MaxHoverTraces, "≤ 3 hover traces");
                }
            }

            for (int i = 0; i < 20; i++) Step(0.05f);
            Assert.AreEqual(UiIconPose.Lifted.Scale, previous.ShownPose.Scale, 1e-4f, "the last focus rests lifted");
            Assert.AreEqual(1f, previous.ShownPose.Lift, 1e-4f, "its lob at Elev2");
            if (previous.IconMaterial != null)
            {
                Assert.AreEqual(1f, previous.IconMaterial.IconLift, 1e-4f, "written to the material's uv2.w");
            }
            for (int i = 0; i < icons.Length - 1; i++)
            {
                if (icons[i] != previous)
                {
                    Assert.AreEqual(1f, icons[i].ShownPose.Scale, 1e-4f, "the others settled");
                }
            }
        }

        [Test]
        public void IkonHareket_SifirAyirma_OynatVeIlerle()
        {
            UiIconButton[] icons = Rail(8);
            Step(0.1f);
            Cycle(icons);   // warm-up: JIT, the canvas registry, the pose writes
            byte[] control = null;
            Assert.That(() => { control = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "positive control");
            Assert.That(() =>
            {
                for (int n = 0; n < 10; n++) Cycle(icons);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "UiIconMotion Play / Tick allocate nothing");
            Debug.Log("[DD-TEST] wp098.iconMotionAlloc none (10 × 8 tracks × 20 ticks) control=" + (control != null ? control.Length : 0));
        }

        private static readonly UiIconTrack[] CycleTracks = { UiIconTrack.Lift, UiIconTrack.Settle, UiIconTrack.Press, UiIconTrack.Notify };

        private static void Cycle(UiIconButton[] icons)
        {
            for (int i = 0; i < icons.Length; i++)
            {
                UiIconMotion.Play(icons[i], CycleTracks[i % CycleTracks.Length]);
            }

            for (int f = 0; f < 20; f++) Step(0.016f);
        }

        // ================================================================== ink: the wave and the splash

        [Test]
        public void MurekkepDalgasi_BeklemeKapisi_Aralik_EnCokUc_RMYok()
        {
            var waves = new UiInkWave[4];
            for (int i = 0; i < waves.Length; i++)
            {
                RectTransform control = UiFactory.NewRect("Control" + i, _root);
                control.sizeDelta = new Vector2(56f, 56f);
                waves[i] = UiInkWave.Attach(control);
                Assert.AreSame(waves[i], UiInkWave.Attach(control), "built once");
                Assert.AreEqual(0, waves[i].transform.GetSiblingIndex(), "behind the control's own graphics");
            }

            Assert.IsTrue(waves[0].Lift(true));
            Step(0.04f);
            waves[0].Lift(false);   // inside the 80 ms gate
            Step(0.2f);
            Assert.AreEqual(0f, waves[0].Group.alpha, 1e-5f, "left inside the dwell gate: never shown");

            Step(0.5f);
            Assert.IsTrue(waves[0].Lift(true));
            Step(0.1f);
            Assert.Greater(waves[0].Group.alpha, 0.5f, "past the gate the wave shows");
            Assert.IsFalse(waves[0].Lift(true), "one wave per control per 400 ms");
            Assert.IsTrue(waves[1].Lift(true));
            Assert.IsTrue(waves[2].Lift(true));
            Assert.IsFalse(waves[3].Lift(true), "three at once across the UI");
            Assert.LessOrEqual(UiInkWave.Busy(UiMotion.TestNow.Value), UiMenuMotionTokens.MaxHoverTraces);
            Step(1f);
            Assert.AreEqual(0f, waves[0].Group.alpha, 1e-4f, "faded out by Emphasis");

            UiMotion.ReducedMotion = true;
            Assert.IsFalse(waves[3].Lift(true), "Reduced Motion: no wave");
        }

        [Test]
        public void MurekkepSicramasi_Aralik_FlasKapisi_FlashScale_RMYok()
        {
            UiInkSplashLayer layer = UiInkSplashLayer.Create(_root);
            Assert.IsTrue(layer.Play(Vector2.zero, 1, false));
            Image first = layer.transform.GetChild(layer.transform.childCount - 1).GetComponent<Image>();
            Assert.AreEqual(UiMenuMotionTokens.SplashAlpha, first.color.a, 1e-4f, "cream α 0.14 × FlashScale 1");
            Assert.AreEqual(UiMenuMotionTokens.SplashSize, first.rectTransform.sizeDelta.x, 1e-3f);
            Assert.IsFalse(layer.Play(Vector2.zero, 1, false), "one splash per source per 280 ms");
            Assert.IsTrue(layer.Play(Vector2.one * 40f, 2, false), "another control splashes");

            Step(0.3f);
            UiTokens.FlashScale = 0.4f;
            Assert.IsTrue(layer.Play(Vector2.zero, 3, true), "PLAY: the hero splash");
            Image hero = layer.transform.GetChild(layer.transform.childCount - 1).GetComponent<Image>();
            Assert.AreEqual(UiMenuMotionTokens.SplashAlphaHero * 0.4f, hero.color.a, 1e-4f, "vermilion α 0.5 × Reduce Flashes 0.4");
            Assert.AreEqual(UiMenuMotionTokens.SplashSizeHero, hero.rectTransform.sizeDelta.x, 1e-3f);
            Step(0.3f);
            Assert.IsTrue(layer.Play(Vector2.zero, 4, true));
            Step(0.3f);
            Assert.IsTrue(layer.Play(Vector2.zero, 5, true));
            Step(0.05f);
            Assert.IsFalse(layer.Play(Vector2.zero, 6, true), "a fourth large onset inside a second merges (FlashGate)");
            Assert.AreEqual(5, layer.Played);

            Step(2f);
            UiMotion.ReducedMotion = true;
            Assert.IsFalse(layer.Play(Vector2.zero, 7, false), "Reduced Motion: the bevel carries the press");
        }

        // ================================================================== panes: the shared presets and the stagger

        [Test]
        public void PanePresetleri_AcKapa_Silme_RM_Kademe()
        {
            var cardGo = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup));
            _created.Add(cardGo);
            cardGo.transform.SetParent(_root, false);
            CanvasGroup card = cardGo.GetComponent<CanvasGroup>();
            Image frameImage = UiFactory.NewImage("Frame", cardGo.transform, UiColor.TextSecondary, UiSprites.Solid);
            UiFx frame = frameImage.gameObject.AddComponent<UiFx>();
            RectTransform content = UiFactory.NewRect("Content", cardGo.transform);
            CanvasGroup contentGroup = UiFactory.Group(content.gameObject);
            Vector2 rest = content.anchoredPosition;

            UiPresets.PaneOpen(card, frame, contentGroup, content);
            Assert.AreEqual(0f, card.alpha, 1e-5f, "the card fades in");
            Assert.AreEqual(0f, contentGroup.alpha, 1e-5f);
            Assert.AreEqual(rest.y - UiPresets.Travel, content.anchoredPosition.y, 1e-4f, "the content rises 8 px");
            Assert.AreEqual(UiFxMode.InkReveal, frame.Mode);
            Assert.AreEqual(UiPresets.PaneFrameWipe, frame.Parameters, "the frame wipes in");
            Assert.AreEqual(UiMotionTokens.Seconds(UiDuration.Screen), frame.Duration, 1e-5f, "over Screen 280");
            Assert.AreEqual(UiFxGlobals.Now, frame.StartTime, 1e-4f);
            Step(0.6f);
            Assert.AreEqual(1f, card.alpha, 1e-4f);
            Assert.AreEqual(1f, contentGroup.alpha, 1e-4f);
            Assert.AreEqual(rest.y, content.anchoredPosition.y, 1e-4f);

            float seconds = UiPresets.PaneClose(card, frame, contentGroup);
            Assert.AreEqual(UiMotionTokens.Seconds(UiDuration.Fast), seconds, 1e-5f, "close over Fast 140");
            Assert.AreEqual(UiPresets.PaneFrameErase, frame.Parameters, "the frame wiped off in the direction it came");
            Step(0.2f);
            Assert.AreEqual(0f, card.alpha, 1e-4f);
            Assert.AreEqual(0f, contentGroup.alpha, 1e-4f);

            UiMotion.ReducedMotion = true;
            UiPresets.PaneOpen(card, frame, contentGroup, content);
            Assert.AreEqual(1f, card.alpha, 1e-5f, "RM: the card at once");
            Assert.AreEqual(1f, contentGroup.alpha, 1e-5f);
            Assert.AreEqual(rest.y, content.anchoredPosition.y, 1e-4f, "RM: in place");
            Assert.AreEqual(0f, UiPresets.PaneClose(card, frame, contentGroup), 1e-6f, "RM: a cut");
            UiMotion.ReducedMotion = false;

            // M13: eight rows, 40 ms apart, the seventh and eighth share the sixth's delay.
            var rows = new List<RectTransform>();
            for (int i = 0; i < 8; i++)
            {
                RectTransform row = UiFactory.NewRect("Row" + i, _root);
                row.anchoredPosition = new Vector2(0f, -40f * i);
                rows.Add(row);
            }

            Assert.AreEqual(8, UiPresets.StaggerRows(rows, UiMotionTokens.StaggerRow, 0f));
            Step(5 * UiMotionTokens.StaggerRow + 0.05f);
            CanvasGroup g0 = rows[0].GetComponent<CanvasGroup>(), g5 = rows[5].GetComponent<CanvasGroup>(), g6 = rows[6].GetComponent<CanvasGroup>(), g7 = rows[7].GetComponent<CanvasGroup>();
            Assert.Greater(g0.alpha, g5.alpha, "the first ahead of the sixth");
            Assert.AreEqual(g5.alpha, g6.alpha, 1e-5f, "the seventh shares the sixth's delay");
            Assert.AreEqual(g5.alpha, g7.alpha, 1e-5f);
            Step(1f);
            Assert.AreEqual(1f, g7.alpha, 1e-4f);
            Assert.AreEqual(-40f * 7, rows[7].anchoredPosition.y, 1e-3f, "at rest");
        }
    }
}
