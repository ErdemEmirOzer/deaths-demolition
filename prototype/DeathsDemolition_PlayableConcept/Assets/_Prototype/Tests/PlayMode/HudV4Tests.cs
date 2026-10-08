using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-094 HUD v4 (R3, C-00 07.10; top-down only, the user 06.10) · the live half of the gold target and the v4 layout.
    /// - HudGoldTarget_NoktaAltinSemboluMerkezi: in a fighting run the target point is the coin's centre on screen, above the
    ///   health cluster; a flight holds the counter until it lands (then one punch, one Arrived), a lost flight frees itself
    ///   after 2 s; the menu has no target.
    /// - HudV4Yerlesim_…: the gold sits directly above the health on its left rail, the round (XXXVIII) at the top-right
    ///   corner, the threat chips at InkMargin in the freed top-centre slot (S-09 decision (d)), and no cluster, row, banner,
    ///   Special block or "+N" meets another - at 1920 × 1080 (UI 1.0, text 1.0) and the Mac 3024 × 1964 (UI 1.2, text
    ///   1.25), each a standalone HUD drawn into a target of that size with the scaler's Expand by hand
    ///   (UiControlsProfileResultsTests). A temp ui-settings file - the user's is never touched.
    /// WP-094 juice (K/wp-094/HAREKET-HUD.md, top-down only; the FP / TP layouts are untouched and untested):
    /// - HudV4Raund_…: the R1 → R3 round transition is one timeline - the old numeral leaves, the new one is laid at the
    ///   hand-off and opens, StampLanded "round" rises once on the seal's contact, a newer change lands the running one (cue
    ///   still raised), reduced motion swaps at once and still raises the cue.
    /// - HudV4Juice_Saglik_…: the clusters that change per frame are nested canvases; a hit breaks a chunk off over a snapped
    ///   fill with the trail held, two simultaneous hits keep two wedges and light two edges, LOW beats on the heartbeat clock
    ///   (70 → 110 bpm, feel contract §4.7) with the rim breathing, a heal out of the band glows and pulses white once;
    ///   reduced motion drops the chunk.
    /// - HudV4Juice_Sarjor_…: a shot sparks and throws a casing, accents stay ≤ 10 a second, LOW breathes and EMPTY stops it,
    ///   an empty trigger shakes once per 250 ms, the reload previews the rounds and gives way on landing, a weapon change
    ///   swaps the glyph through a stamp.
    /// - HudV4His_FeelEventsAnlari_…: (08.10, K/wp-095/HIS-SOZLESMESI.md) the HUD hears its moments from FeelEvents - the crit
    ///   notch (§4.2), the kill pop (§4.3), "×N" (§4.4), the round start band (§4.10) and the last zombie's stroke (§4.11).
    /// </summary>
    [Category("WP094")]
    public sealed class HudV4Tests
    {
        private readonly List<Object> _owned = new List<Object>();
        private string _settingsPath;

        [SetUp]
        public void SetUp()
        {
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-wp094-hud-settings-" + Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i] != null)
                {
                    Object.Destroy(_owned[i]);
                }
            }

            _owned.Clear();
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
            UiSettings.SetPathOverride(null);
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }

        // ================================================================== the target point, in a run

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HudGoldTarget_NoktaAltinSemboluMerkezi()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            Assert.IsFalse(HudGoldTarget.TryGetScreenPoint(out _), "the menu has no HUD to fly to");
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return QuietField();

            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "HUD");
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "R3: top-down only");
            Assert.IsTrue(HudGoldTarget.IsBoundTo(hud), "the run's HUD holds the target");

            // 1 · the point is the coin's centre (overlay: camera null), on screen, above the health cluster in its column.
            Assert.IsTrue(HudGoldTarget.TryGetScreenPoint(out Vector2 point), "a live HUD in a fighting run is a target");
            Image coin = Find<Image>(hud.transform, "Gold", "Coin");
            // WP-094 AP-032 v001: the koban is bound - the §1 layer for its 40 px body on this screen, full colour (no tint).
            Sprite koban = HudArt.GoldCoin(RunHudScreen.GoldCoinBody * HudArt.CanvasScale);
            Assert.IsNotNull(koban, "AP-032's koban is in the build (UI/Hud)");
            Assert.AreSame(koban, coin.sprite, "the gold symbol is AP-032's koban, not the G1 glyph");
            Assert.AreEqual(Color.white, coin.color, "full-colour art takes no tint");
            RectTransform coinRect = coin.rectTransform;
            Vector2 coinCentre = RectTransformUtility.WorldToScreenPoint(null, coinRect.TransformPoint(coinRect.rect.center));
            Assert.Less(Vector2.Distance(point, coinCentre), 0.5f, "target " + point + " vs coin centre " + coinCentre);
            Assert.IsTrue(point.x >= 0f && point.x <= Screen.width && point.y >= 0f && point.y <= Screen.height, "on screen: " + point + " in " + Screen.width + "x" + Screen.height);
            Rect health = ScreenRectOf(ClusterNamed(hud, "Health"));
            Assert.Greater(point.y, health.yMax, "above the health cluster (" + health + ")");
            Assert.IsTrue(point.x > health.xMin && point.x < health.xMax, "in the health column (" + health + ")");

            int version = HudGoldTarget.LayoutVersion;
            Assert.Greater(version, 0, "the bind and the first layout count");
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(version, HudGoldTarget.LayoutVersion, "a still HUD is a still layout: " + version + " → " + HudGoldTarget.LayoutVersion);

            Camera camera = Camera.main;
            Assert.IsTrue(HudGoldTarget.TryGetWorldPoint(camera, 6f, out Vector3 world), "the world point on the camera's 6 m plane");
            Vector3 back = camera.WorldToScreenPoint(world);
            Assert.Less(Vector2.Distance(new Vector2(back.x, back.y), point), 1f, "world point projects back onto the target");
            Assert.AreEqual(6f, back.z, 0.01f);

            // 2 · a flight holds the counter; its arrival counts up, punches once and raises Arrived once.
            UiCounter counter = Find<TMP_Text>(hud.transform, "Gold", "Value").GetComponentInParent<UiCounter>();
            PlayerRig rig = PlayerRegistry.Primary;
            int before = counter.Target;
            Assert.AreEqual(GoldService.Instance.Wallet.Balance, before, "no flight: the counter is the balance");
            var arrived = new List<int>();
            Action<int> listen = arrived.Add;
            HudGoldTarget.Arrived += listen;
            try
            {
                HudGoldTarget.Depart(10);
                Assert.IsTrue(GoldService.Instance.Wallet.Credit(10, rig.transform.position));
                Assert.AreEqual(10, HudGoldTarget.InFlight);
                Assert.AreEqual(before, counter.Target, "held while the coin flies");
                yield return null;
                Assert.AreEqual(before, counter.Target, "still held a frame later");

                int punches = hud.GoldPunches;
                HudGoldTarget.Arrive(10);
                Assert.AreEqual(0, HudGoldTarget.InFlight);
                Assert.AreEqual(before + 10, counter.Target, "counts up on the arrival");
                CollectionAssert.AreEqual(new[] { 10 }, arrived, "Arrived once with the amount");
                yield return null;
                Assert.AreEqual(punches + 1, hud.GoldPunches, "one punch");

                // 3 · a flight that never lands frees itself after 2 s (unscaled): the counter catches up, no event.
                HudGoldTarget.Depart(5);
                Assert.IsTrue(GoldService.Instance.Wallet.Credit(5, rig.transform.position));
                Assert.AreEqual(before + 10, counter.Target);
                yield return new WaitForSecondsRealtime(HudGoldTarget.StaleSeconds + 0.25f);
                Assert.AreEqual(0, HudGoldTarget.InFlight, "released");
                Assert.AreEqual(before + 15, counter.Target, "caught up");
                CollectionAssert.AreEqual(new[] { 10 }, arrived, "a release is not an arrival");
            }
            finally
            {
                HudGoldTarget.Arrived -= listen;
            }

            Debug.Log("[DD-TEST] wp094.goldTarget point=" + point + " coin=" + coinCentre + " layout=" + HudGoldTarget.LayoutVersion + " screen=" + Screen.width + "x" + Screen.height);
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
            Assert.IsFalse(HudGoldTarget.TryGetScreenPoint(out _), "back in the menu: no target");
        }

        /// <summary>No spawns, no enemies, no orbs, an invulnerable player: nothing else credits gold or ends the run.</summary>
        private static IEnumerator QuietField()
        {
            yield return Bekle(() => EnemySpawnDirector.Instance != null && GoldService.Instance != null, 20f, "spawn director / gold service");
            FieldInfo spawning = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(spawning, "EnemySpawnDirector._spawningEnabled");
            spawning.SetValue(EnemySpawnDirector.Instance, false);
            EnemySpawnDirector.Instance.ClearAllEnemies();
            GoldService.Instance.ClearOrbs(false);
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            yield return null;
            yield return null;
        }

        // ================================================================== the v4 layout at two frames

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HudV4Yerlesim_AltinCaninUstunde_RoundSagUstte_CakismaYok()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return Layout(1920, 1080, 1f, 1f);
            yield return Layout(3024, 1964, 1.2f, 1.25f);
        }

        private IEnumerator Layout(int width, int height, float uiScale, float textScale)
        {
            string frame = width + "x" + height + " ui" + uiScale.ToString("0.0") + " text" + textScale.ToString("0.00");
            UiSettings.UiScale = uiScale;
            UiSettings.TextScale = textScale;
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "R3: top-down only");

            var root = new GameObject("HudV4.Layout");
            _owned.Add(root);
            RunHudScreen hud = root.AddComponent<RunHudScreen>();
            Canvas canvas = hud.transform.Find(typeof(RunHudScreen).Name + ".Canvas").GetComponent<Canvas>();

            // The frame: a camera on a target of the frame's size and the scaler's Expand by hand, so the canvas is the one
            // that frame gets (1920 × 1080; the Mac at UI 1.2: 1600 × 1039), whatever window the runner has.
            var target = new RenderTexture(width, height, 0);
            target.Create();
            _owned.Add(target);
            var cameraObject = new GameObject("HudV4.Camera");
            _owned.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10f;
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = Mathf.Min(width / (1920f / uiScale), height / (1080f / uiScale));
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var screen = (RectTransform)canvas.transform;
            Assert.AreEqual(width / canvas.scaleFactor, screen.rect.width, 1f, frame + ": canvas width");
            Assert.AreEqual(height / canvas.scaleFactor, screen.rect.height, 1f, frame + ": canvas height");

            // XXXVIII: the widest numeral a run plausibly reaches; its reveal schedules the seal's contact cue for S-08.
            int cues = 0;
            Action<UiEvent, string> listen = (e, detail) =>
            {
                if (e == UiEvent.StampLanded && detail == RunHudScreen.RoundSealCueDetail)
                {
                    cues++;
                }
            };
            UiEvents.Raised += listen;
            bool reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            try
            {
                // WP-094 (R1 → R3): the seal's contact comes after the hand-off (0.4 s) and XXXVIII's reveal (7 letters: 600 ms).
                hud.CaptureRound(38);
                Assert.AreEqual(0, cues, "the cue waits for the contact frame");
                Assert.IsTrue(hud.RoundTransitionLive, "R1 → R3 runs as one timeline");
                float contact = HudRoundTransition.ContactSeconds(RomanNumerals.Length(38));
                yield return new WaitForSecondsRealtime(contact - 0.15f);
                Assert.AreEqual(0, cues, "not before the seal's contact (" + contact + " s)");
                yield return new WaitForSecondsRealtime(0.45f);
                Assert.AreEqual(1, cues, "one StampLanded \"round\" at the seal's contact");
            }
            finally
            {
                UiEvents.Raised -= listen;
                UiMotion.ReducedMotion = reduced;
            }

            Rect banner = hud.CaptureBanner("ROUND 38");
            yield return null;
            Canvas.ForceUpdateCanvases();

            Rect canvasRect = screen.rect;
            Rect gold = CanvasRectOf(screen, ClusterNamed(hud, "Gold"));
            Rect health = CanvasRectOf(screen, ClusterNamed(hud, "Health"));
            Rect round = CanvasRectOf(screen, ClusterNamed(hud, "Round"));
            Rect weapon = CanvasRectOf(screen, ClusterNamed(hud, "ClassicWeapon"));
            Rect threats = CanvasRectOf(screen, (RectTransform)screen.Find("ThreatRow"));
            Rect finalSeconds = CanvasRectOf(screen, (RectTransform)screen.Find("FinalSeconds"));
            Rect special = CanvasRectOf(screen, (RectTransform)screen.Find("SpecialWeapon"));
            Rect delta = CanvasRectOf(screen, Find<TMP_Text>(hud.transform, "Gold", "Delta").rectTransform);
            string where = frame + " gold " + gold + " health " + health + " round " + round + " weapon " + weapon;

            // The gold: directly above the health, one UiSpace.S or more clear, at most 16 px (S + the 8 px grid's remainder),
            // on the same left rail. Measured from the health cluster's LAID-OUT top: the posture draws the cluster at CalmScale
            // about its bottom-left corner in calm (r3-w1: 94.91 drawn of 100.97 laid out at 1080), and the gold - the coins'
            // flight target - keeps its place on the grid rather than follow a draw scale (RunHudScreen.GoldClusterBottom).
            RectTransform healthCluster = ClusterNamed(hud, "Health");
            float healthTop = health.yMin + health.height / Mathf.Max(0.01f, healthCluster.localScale.y);
            Assert.AreEqual(RunHudScreen.HealthClusterHeight(), healthTop - health.yMin, 0.5f, where + ": the laid-out health height");
            Assert.GreaterOrEqual(gold.yMin, healthTop + (int)UiSpace.S - 0.5f, where + ": the gold sits above the health, ≥ 8 px clear");
            Assert.LessOrEqual(gold.yMin, healthTop + (int)UiSpace.S + 8f + 0.01f, where + ": directly above (≤ S + the grid's 8 px)");
            Assert.GreaterOrEqual(gold.yMin, health.yMax + (int)UiSpace.S - 0.5f, where + ": ≥ 8 px clear of the drawn health in either posture");
            Assert.AreEqual(health.xMin, gold.xMin, 0.5f, where + ": one left rail");
            float goldBottom = gold.yMin - canvasRect.yMin;
            Assert.AreEqual(Mathf.Round(goldBottom / 8f) * 8f, goldBottom, 0.01f, where + ": the gold's bottom on the 8 px grid (from the canvas bottom)");

            // WP-094 (S-09 decision (d)): the threat chips moved up into the freed top-centre slot at InkMargin.
            Assert.AreEqual(canvasRect.yMax - 32f, threats.yMax, 0.5f, where + ": the threat chips' top at InkMargin " + threats);
            Assert.AreEqual(canvasRect.center.x, threats.center.x, 1f, where + ": the chip row centred");

            // The round: the top-right corner, InkMargin in from both edges; the numeral is the Roman one and fits its box.
            Assert.AreEqual(canvasRect.xMax - 32f, round.xMax, 0.5f, where + ": round's right edge");
            Assert.AreEqual(canvasRect.yMax - 32f, round.yMax, 0.5f, where + ": round's top edge");
            Rect seal = CanvasRectOf(screen, hud.RoundSeal);
            Rect letters = CanvasRectOf(screen, hud.RoundClip);
            if (hud.RoundPlateAuthored)
            {
                // AP-032 v001 §3.3 (AD notes 3 / 4): the margin is measured from the plate's tail tip (the cluster is the plate);
                // the seal sits 0.55 diameters inside the dense zone's right end at the cap's middle, the letters end 0.22 em
                // before it. The readable impression (letters + seal) keeps the right quarter; the plate itself - its spare width
                // goes to the head by the W' rule, 419 px for XXXVIII - stays in the right half (on the Mac's 1600-wide canvas a
                // quarter is 400 px).
                float plateWidth, plateHeight, runRight, sealCentre, sealDiameter;
                HudArt.LaySmear(hud.RoundCap, hud.RoundNumeral.Width, hud.RoundNumeral.FirstBearingEm,
                    out plateWidth, out plateHeight, out runRight, out sealCentre, out sealDiameter);
                Assert.AreEqual(plateWidth, round.width, 0.5f, where + ": the cluster is the plate (W' rule)");
                Assert.AreEqual(round.xMax - sealCentre, seal.center.x, 0.5f, where + ": the seal 0.55 diameters inside the dense zone");
                Assert.AreEqual(round.center.y, seal.center.y, 0.5f, where + ": the seal at the cap's middle");
                Assert.Greater(letters.xMin, canvasRect.center.x + canvasRect.width * 0.25f, where + ": the numeral in the right quarter");
                Assert.Less(letters.xMax, seal.xMin, where + ": the letters before the seal");
                // 08.10: RoundSeal is the seal's footprint (the ring); the AP-032 cell it draws overflows it, centred, 256 / 72 wide.
                Assert.AreEqual(sealDiameter, seal.width, 0.5f, where + ": the seal's footprint is the ring (0.45 em)");
                if (hud.RoundSealAuthored)
                {
                    Rect cell = CanvasRectOf(screen, (RectTransform)hud.RoundSeal.Find("Seal"));
                    Assert.AreEqual(sealDiameter / HudArt.SealRingShareOfCell, cell.width, 0.5f, where + ": the cell drawn round the ring");
                    Assert.AreEqual(seal.center.x, cell.center.x, 0.5f, where + ": the cell centred on the ring");
                }
                Assert.Greater(round.xMin, canvasRect.center.x, where + ": the plate in the right half");
            }
            else
            {
                Assert.Greater(round.xMin, canvasRect.center.x + canvasRect.width * 0.25f, where + ": round in the right quarter");
                Assert.AreEqual(round.xMax, seal.xMax, 3f, where + ": the seal accent at the corner (≤ 2 px drift)");
            }

            Assert.Greater(hud.RoundSealHitAt, 0f, "the reveal scheduled the seal's hit");
            // WP-094 AP-032 v001: with the atlases in the build the numeral is AP-032's brush cells, the TMP line off.
            Assert.AreEqual(HudArt.RomanAuthored, hud.RoundNumeral.DrawsGlyphs, frame + ": AP-032's cells draw the numeral when they are in");
            if (hud.RoundNumeral.DrawsGlyphs)
            {
                Assert.AreEqual(7, hud.RoundNumeral.GlyphCount, "XXXVIII as glyphs");
                Assert.IsFalse(Find<TMP_Text>(hud.transform, "Round", "RoundValue").enabled, frame + ": no TMP on the glyph path");
            }
            else
            {
                TMP_Text numeral = Find<TMP_Text>(hud.transform, "Round", "RoundValue");
                Assert.AreEqual("XXXVIII", numeral.text);
                numeral.ForceMeshUpdate();
                Assert.IsFalse(numeral.isTextTruncated, frame + ": the numeral is not cut");
                Assert.LessOrEqual(numeral.GetPreferredValues(numeral.text).x, numeral.rectTransform.rect.width + 0.5f, frame + ": the numeral fits its box");
            }

            // Nothing meets anything: the four clusters, the chip row, the final-seconds line, the banner, the Special block,
            // and the "+N" (part of the gold - it may leave the gold's rect, never into a neighbour's).
            var named = new List<KeyValuePair<string, Rect>>
            {
                new KeyValuePair<string, Rect>("gold", gold),
                new KeyValuePair<string, Rect>("round", round),
                new KeyValuePair<string, Rect>("health", health),
                new KeyValuePair<string, Rect>("weapon", weapon),
                new KeyValuePair<string, Rect>("threats", threats),
                new KeyValuePair<string, Rect>("finalSeconds", finalSeconds),
                new KeyValuePair<string, Rect>("banner", banner),
                new KeyValuePair<string, Rect>("special", special),
            };
            var failures = new List<string>();
            for (int a = 0; a < named.Count; a++)
            {
                for (int b = a + 1; b < named.Count; b++)
                {
                    if (Inset(named[a].Value).Overlaps(Inset(named[b].Value)))
                    {
                        failures.Add(named[a].Key + " " + named[a].Value + " meets " + named[b].Key + " " + named[b].Value);
                    }
                }

                if (named[a].Key != "gold" && Inset(delta).Overlaps(Inset(named[a].Value)))
                {
                    failures.Add("\"+N\" " + delta + " meets " + named[a].Key + " " + named[a].Value);
                }
            }

            Assert.IsEmpty(failures, frame + ":\n" + string.Join("\n", failures));

            // The play area stays clear (REQ-104/105).
            var zone = new Rect(
                canvasRect.xMin + canvasRect.width * RunHudScreen.HudSafeZone.xMin,
                canvasRect.yMin + canvasRect.height * RunHudScreen.HudSafeZone.yMin,
                canvasRect.width * RunHudScreen.HudSafeZone.width,
                canvasRect.height * RunHudScreen.HudSafeZone.height);
            foreach (Rect cluster in new[] { gold, round, health, weapon })
            {
                Assert.IsFalse(cluster.Overlaps(zone), frame + ": a cluster enters the play area: " + cluster + " vs " + zone);
            }

            // The gold target through this canvas's camera: still the coin's centre.
            if (HudGoldTarget.IsBoundTo(hud) && HudGoldTarget.TryGetScreenPoint(out Vector2 point))
            {
                RectTransform coin = Find<Image>(hud.transform, "Gold", "Coin").rectTransform;
                Vector2 expected = RectTransformUtility.WorldToScreenPoint(camera, coin.TransformPoint(coin.rect.center));
                Assert.Less(Vector2.Distance(point, expected), 0.5f, frame + ": target " + point + " vs coin " + expected);
            }

            Debug.Log("[DD-TEST] wp094.layout " + frame + " canvas=" + canvasRect.size + " gold=" + gold + " health=" + health + " round=" + round +
                      " banner=" + banner + " delta=" + delta);
            Object.Destroy(root);
            yield return null;
        }

        // ================================================================== WP-094 juice: the round transition

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HudV4Raund_R1R3_TekCizelge_RmAnindaDegisirSesKalir()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "R3: top-down only");
            var root = new GameObject("HudV4.Round");
            _owned.Add(root);
            RunHudScreen hud = root.AddComponent<RunHudScreen>();
            yield return null;
            yield return null;
            TMP_Text numeral = Find<TMP_Text>(hud.transform, "Round", "RoundValue");
            CanvasGroup group = hud.RoundClip.GetComponent<CanvasGroup>();
            int cues = 0;
            Action<UiEvent, string> listen = (e, detail) =>
            {
                if (e == UiEvent.StampLanded && detail == RunHudScreen.RoundSealCueDetail)
                {
                    cues++;
                }
            };
            bool reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            UiEvents.Raised += listen;
            try
            {
                hud.CaptureRound(4);
                yield return new WaitForSecondsRealtime(HudRoundTransition.ContactSeconds(2) + 0.2f);
                Assert.AreEqual("IV", numeral.text);
                Assert.AreEqual(1, cues);

                // R1: the old numeral leaves first; the banner reads before the corner is written.
                hud.CaptureRound(9);
                yield return null;
                Assert.IsTrue(hud.RoundTransitionLive, "one timeline");
                yield return new WaitForSecondsRealtime(HudRoundTransition.HandoffSeconds * 0.6f);
                Assert.AreEqual("IV", numeral.text, "no swap before the hand-off (0.4 s)");
                Assert.Less(group.alpha, 0.5f, "R1: the old numeral faded out");

                // R2: laid at the hand-off, the clip opening left to right.
                yield return new WaitForSecondsRealtime(HudRoundTransition.HandoffSeconds * 0.4f + 0.06f);
                Assert.AreEqual("IX", numeral.text, "R2: the new numeral is laid at the hand-off");
                Assert.AreEqual(1f, group.alpha, 1e-3f);
                float opening = hud.RoundClip.sizeDelta.x;

                // R3: the seal's stamp and the S-08 cue on its contact.
                yield return new WaitForSecondsRealtime(HudRoundTransition.ContactSeconds(2) - HudRoundTransition.HandoffSeconds + 0.2f);
                Assert.AreEqual(2, cues, "R3: StampLanded \"round\" once on the contact");
                Assert.IsFalse(hud.RoundTransitionLive);
                Assert.Less(opening, hud.RoundClip.sizeDelta.x - 0.5f, "the clip was still opening at the hand-off");
                Assert.AreEqual(1f, hud.RoundSeal.localScale.x, 1e-3f, "the seal at rest after its stamp");

                // A newer change lands the running transition - its cue still raised - and starts its own.
                hud.CaptureRound(14);
                yield return null;
                hud.CaptureRound(23);
                Assert.AreEqual(3, cues, "a skipped transition still raises its cue (the sound is not motion)");
                Assert.AreEqual("XIV", numeral.text, "the skipped one landed");
                yield return new WaitForSecondsRealtime(HudRoundTransition.ContactSeconds(5) + 0.2f);
                Assert.AreEqual("XXIII", numeral.text);
                Assert.AreEqual(4, cues);

                // Reduced motion: the swap at once, the cue at once.
                UiMotion.ReducedMotion = true;
                hud.CaptureRound(24);
                Assert.AreEqual("XXIV", numeral.text, "RM: the numeral swaps at once");
                Assert.AreEqual(5, cues, "RM: the sound still plays");
                Assert.IsFalse(hud.RoundTransitionLive);
                Debug.Log("[DD-TEST] wp094.roundTransition cues=" + cues + " opening=" + opening + " full=" + hud.RoundClip.sizeDelta.x);
            }
            finally
            {
                UiEvents.Raised -= listen;
                UiMotion.ReducedMotion = reduced;
            }
        }

        // ================================================================== WP-094 juice: health, damage direction

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HudV4Juice_Saglik_ParcaIzIsimaNabiz_IkiKama_Adalar()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "HUD");
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "R3: top-down only");
            PlayerRig rig = PlayerRegistry.Primary;
            PlayerHealth health = rig.Health;
            Vector3 at = rig.transform.position;
            Vector3 side = ScreenRightOnGround();
            bool reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            try
            {
                // "Ada kuralı": the clusters that change frame by frame are their own canvases (a shot, a beat, an edge's decay
                // never re-batch the main HUD canvas).
                Assert.IsNotNull(ClusterNamed(hud, "Health").GetComponent<Canvas>(), "the health front plane is a nested canvas");
                Assert.IsNotNull(ClusterNamed(hud, "ClassicWeapon").Find("Content").GetComponent<Canvas>(), "the weapon's Content is a nested canvas");
                Assert.IsNotNull(ClusterNamed(hud, "Round").GetComponent<Canvas>(), "the round's impression is a nested canvas");
                Assert.IsNotNull(hud.DamageEdges.GetComponent<Canvas>(), "the edge layer is a nested canvas");

                // H1 / H2: a 30 % hit - a chunk breaks off over a snapped fill, the trail holds, then falls.
                health.HealToFull();
                yield return new WaitForSecondsRealtime(1.1f);
                UiBar bar = ClusterNamed(hud, "Health").GetComponentInChildren<UiBar>();
                Hit(health, at, side, 0.3f);
                Assert.GreaterOrEqual(hud.HealthChunksLive, 1, "H1: a chunk broke off");
                Assert.AreEqual(0.7f, bar.FillImage.fillAmount, 0.02f, "H1: the fill snapped under the chunk");
                Assert.Greater(bar.GhostValue, 0.95f, "H2: the trail holds the old value");
                yield return new WaitForSecondsRealtime(0.7f);
                Assert.AreEqual(0, hud.HealthChunksLive, "the chunk has fallen (Screen)");
                Assert.AreEqual(0.7f, bar.GhostValue, 0.02f, "H2: the trail fell to the value");

                // D2 / D1: two hits from opposite sides in one frame - two wedges, two edges.
                health.HealToFull();
                yield return new WaitForSecondsRealtime(1.1f);
                health.DiagnosisInvulnerable = false;
                Assert.IsTrue(health.ApplyDamage(health.Max * 0.1f, at - side * 2f, "Test.WP094"));
                Assert.IsTrue(health.ApplyDamage(health.Max * 0.1f, at + side * 2f, "Test.WP094"));
                health.DiagnosisInvulnerable = true;
                Assert.AreEqual(2, hud.PlayerArcs.LiveWedges(UiFxGlobals.Now), "D2: the second hit no longer turns the first wedge away");
                // §4.6 (feel contract): the edge rises over 0.05 s, then holds 0.25 s - read it on the hold.
                yield return new WaitForSecondsRealtime(UiTokens.DamageEdgeRiseSeconds + 0.04f);
                Assert.Greater(hud.DamageEdges.EdgeAlpha(UiScreenEdge.Left), 0.3f, "D1: left edge");
                Assert.Greater(hud.DamageEdges.EdgeAlpha(UiScreenEdge.Right), 0.3f, "D1: right edge");

                // H5: LOW - the heart beats ≤ 1.1 Hz and the coral rim breathes; the words never pulse.
                yield return new WaitForSecondsRealtime(0.6f);
                health.HealToFull();
                yield return new WaitForSecondsRealtime(1.1f);
                Hit(health, at, -side, 0.8f);
                yield return null;
                Assert.IsTrue(hud.HeartbeatRunning, "H5: LOW starts the heartbeat");
                int beats = hud.HeartBeats;
                // §4.7 (feel contract): the rhythm follows health - 90 bpm at 20 %.
                yield return new WaitForSecondsRealtime(3f * HudHeartbeat.PeriodFor(health.Current / health.Max) + 0.25f);
                beats = hud.HeartBeats - beats;
                Assert.That(beats, NUnit.Framework.Is.InRange(3, 4), "three periods of the heartbeat clock (70 → 110 bpm)");
                Assert.Greater(hud.LowHealthRim.canvasRenderer.GetAlpha(), 0.3f, "the rim breathes between 0.35 and 0.75");

                // H4: a heal of 25 % glows moss over the gained range.
                int glows = hud.HealGlows;
                int exits = hud.LowHealthExitPulses;
                health.Heal(health.Max * 0.25f);
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.AreEqual(glows + 1, hud.HealGlows, "H4: one glow");
                Assert.IsFalse(hud.HeartbeatRunning, "§4.7: 45 % ≥ 38 % - out of the FeelEvents band");
                Assert.AreEqual(exits + 1, hud.LowHealthExitPulses, "§4.7: one white pulse on the way out");
                Assert.Greater(hud.LowHealthRim.canvasRenderer.GetAlpha(), 0.05f, "§4.7: the rim flares cream once (Fast in, Screen out)");
                Assert.Greater(hud.HealGlowGraphic.canvasRenderer.GetAlpha(), 0.2f, "H4: the glow at its peak");
                health.HealToFull();
                yield return null;
                Assert.IsFalse(hud.HeartbeatRunning, "out of LOW: the heartbeat stops");
                Assert.AreEqual(exits + 1, hud.LowHealthExitPulses, "a heal further out of the band pulses no more");
                yield return new WaitForSecondsRealtime(0.45f);   // the white pulse: 0.14 in + 0.28 out
                Assert.AreEqual(0f, hud.LowHealthRim.canvasRenderer.GetAlpha(), 1e-3f);

                // Reduced motion: no chunk - the trail alone carries the loss.
                UiMotion.ReducedMotion = true;
                yield return new WaitForSecondsRealtime(1.1f);
                Hit(health, at, side, 0.3f);
                Assert.AreEqual(0, hud.HealthChunksLive, "RM: no chunk");
                Debug.Log("[DD-TEST] wp094.healthJuice beats=" + beats + " glows=" + hud.HealGlows + " knocks=" + hud.HealthKnocks);
            }
            finally
            {
                UiMotion.ReducedMotion = reduced;
                health.DiagnosisInvulnerable = true;
            }

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== WP-094 juice: magazine and ammo

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HudV4Juice_Sarjor_KivilcimKovan_Nabiz_Bos_Onizleme_Degisim()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "HUD");
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "R3: top-down only");
            WeaponController weapon = WeaponRegistry.ActiveController;
            Assert.IsNotNull(weapon, "weapon controller");
            ClassicWeaponRuntime classic = weapon.Classic;
            ClassicWeaponId selected = WeaponRegistry.SelectedWeapon;
            ClassicWeaponId home = classic.Id;
            bool reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            try
            {
                if (classic.Ammo < classic.MagazineSize && classic.TryBeginReload())
                {
                    yield return Bekle(() => !classic.IsReloading, 8f, "a full magazine");
                }

                yield return new WaitForSecondsRealtime(0.3f);

                // A1 / A2: a shot - the spark at the emptied slot, a casing thrown.
                int accents = hud.ShotAccents;
                classic.ConsumeShot();
                yield return null;
                yield return null;
                Assert.AreEqual(accents + 1, hud.ShotAccents, "A1: the shot accents");
                Assert.GreaterOrEqual(hud.CasingsLive, 1, "A2: a casing in flight");
                Assert.Greater(hud.LadderSpark.canvasRenderer.GetAlpha(), 0f, "A1: the spark");

                // ≤ 10 accents a second, however fast the rounds go.
                yield return new WaitForSecondsRealtime(0.15f);
                accents = hud.ShotAccents;
                float t0 = Time.realtimeSinceStartup;
                for (int i = 0; i < 5 && classic.Ammo > 2; i++)
                {
                    classic.ConsumeShot();
                    yield return null;
                }

                yield return null;
                float spent = Time.realtimeSinceStartup - t0;
                Assert.LessOrEqual(hud.ShotAccents - accents, 1 + Mathf.FloorToInt(spent / UiMotionTokens.ShotAccentMinInterval), "≤ 10 accents a second (" + spent + " s)");

                // A5: LOW breathes; A6: EMPTY stops it and an empty trigger shakes at most once per 250 ms.
                int low = Mathf.Max(1, Mathf.FloorToInt(classic.MagazineSize * RunHudViewModel.LowAmmoFraction));
                while (classic.Ammo > low)
                {
                    classic.ConsumeShot();
                }

                yield return null;
                yield return null;
                Assert.IsTrue(hud.AmmoPulsing, "A5: the ring's track and the rounds breathe at LOW");
                while (classic.Ammo > 0)
                {
                    classic.ConsumeShot();
                }

                yield return null;
                yield return null;
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.IsFalse(hud.AmmoPulsing, "EMPTY stops the LOW pulse");
                int shakes = hud.DryFireShakes;
                // The HUD hears the empty trigger from FeelEvents (08.10) - raised as WeaponController raises it.
                var dry = new DD.Prototype.Feedback.FeelDryFire(classic.Id, PlayerRegistry.Primary.transform.position);
                DD.Prototype.Feedback.FeelEvents.RaiseDryFire(dry);
                DD.Prototype.Feedback.FeelEvents.RaiseDryFire(dry);
                Assert.AreEqual(shakes + 1, hud.DryFireShakes, "A6: ≤ 1 shake per 250 ms");

                // A3 / A4: the reload previews the rounds coming in, and gives way to them on landing.
                Assert.IsTrue(classic.TryBeginReload(), "reload");
                yield return Bekle(() => classic.ReloadProgress01 >= 0.5f, 8f, "half the reload");
                yield return null;
                yield return null;
                Assert.Greater(hud.ReloadPreviewRounds, 0, "A3: the empty slots preview the reload");
                yield return Bekle(() => !classic.IsReloading, 8f, "the reload lands");
                yield return null;
                yield return null;
                Assert.AreEqual(-1, hud.ReloadPreviewRounds, "A4: landed - the preview gave way to the rounds");

                // A7: another weapon - the glyph swaps through a stamp.
                int swaps = hud.WeaponSwaps;
                ClassicWeaponId other = home == ClassicWeaponId.CombatShotgun ? ClassicWeaponId.AssaultRifle : ClassicWeaponId.CombatShotgun;
                weapon.EquipClassic(other);
                yield return null;
                yield return null;
                Assert.AreEqual(swaps + 1, hud.WeaponSwaps, "A7: the weapon change swaps through a stamp");
                yield return new WaitForSecondsRealtime(0.4f);
                Image glyph = Find<Image>(hud.transform, "ClassicWeapon", "WeaponIcon");
                Assert.AreEqual(1f, glyph.canvasRenderer.GetAlpha(), 1e-3f, "the new glyph stands");
                Assert.AreEqual(1f, glyph.rectTransform.localScale.x, 1e-3f);
                Debug.Log("[DD-TEST] wp094.ammoJuice accents=" + hud.ShotAccents + " shakes=" + hud.DryFireShakes + " swaps=" + hud.WeaponSwaps);
            }
            finally
            {
                if (classic.Id != home)
                {
                    weapon.EquipClassic(home);
                }

                WeaponRegistry.SelectedWeapon = selected;   // the selection-leak lesson: a test gives the weapon back
                UiMotion.ReducedMotion = reduced;
            }

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>
        /// WP-094 · feel contract (K/wp-095/HIS-SOZLESMESI.md) §4.2 / §4.3 / §4.4 / §4.10 / §4.11: the HUD hears its moments from
        /// FeelEvents alone (08.10). Each moment is raised the way the bridge raises it (the event's backing field - the bridge's
        /// sources need a live zombie) and read back off the HUD: a crit notches the reticle, a kill pops it, a multi-kill writes
        /// "×N" (the numeral only), the round start draws "ROUND IV" over the RoundStart envelope, the last zombie strokes
        /// "ROUND N CLEAR" through the middle. Top-down only.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator HudV4His_FeelEventsAnlari_Notch_Pop_CokluOldurme_RaundAfisleri()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "HUD");
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "R3: top-down only");
            Vector3 at = PlayerRegistry.Primary.transform.position + Vector3.forward * 3f;
            bool reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            try
            {
                yield return new WaitForSecondsRealtime(0.3f);

                // §4.2: a crit notches the TD reticle; a ground-fire tick never does.
                int notches = hud.Reticle.CritNotches;
                FeelRaise("EnemyHit", FeelHitAt(at, true, false));
                Assert.AreEqual(notches + 1, hud.Reticle.CritNotches, "§4.2: the crit notch");
                FeelRaise("EnemyHit", FeelHitAt(at, true, true));
                FeelRaise("EnemyHit", FeelHitAt(at, false, false));
                Assert.AreEqual(notches + 1, hud.Reticle.CritNotches, "§4.2: ground fire and plain hits leave the reticle be");

                // §4.3: a kill pops the reticle (kill beats ≥ 0.12 s apart).
                int pops = hud.Reticle.KillPops;
                FeelRaise("EnemyKilled", Kill(at));
                FeelRaise("EnemyKilled", Kill(at));
                Assert.AreEqual(pops + 1, hud.Reticle.KillPops, "§4.3: one pop per kill beat");
                yield return new WaitForSecondsRealtime(0.15f);
                FeelRaise("EnemyKilled", Kill(at));
                Assert.AreEqual(pops + 2, hud.Reticle.KillPops, "§4.3: the next beat pops again");

                // §4.4: "×N" - the numeral only, popped 1.3 → 1, held 0.8 s, gone over 0.3 s.
                int shown = hud.MultiKillsShown;
                FeelRaise("MultiKill", new DD.Prototype.Feedback.FeelMultiKill(2, 5, true, 1, 2, at));
                Assert.AreEqual(shown + 1, hud.MultiKillsShown, "§4.4: the multi-kill shows");
                Assert.AreEqual(5, hud.MultiKillCount);
                TMP_Text multi = hud.MultiKillRoot.GetComponentInChildren<TMP_Text>(true);
                Assert.IsNotNull(multi, "the ×N numeral");
                multi.ForceMeshUpdate();
                Assert.AreEqual("\u00D75", multi.GetParsedText(), "§4.4: \"×N\", no word");
                yield return null;
                Assert.Greater(Mathf.Abs(hud.MultiKillRoot.localScale.x - 1f), 1e-4f, "§4.4: the 1.3 → 1 pop is under way");
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.AreEqual(1f, hud.MultiKillRoot.localScale.x, 1e-3f, "settled at 1");
                Assert.AreEqual(1f, multi.canvasRenderer.GetAlpha(), 1e-3f, "§4.4: still held at 0.6 s");
                yield return new WaitForSecondsRealtime(0.8f);
                Assert.AreEqual(0f, multi.canvasRenderer.GetAlpha(), 1e-3f, "§4.4: gone after 0.8 + 0.3 s");

                // §4.10: the round start's brush band names the round in Roman numerals.
                FeelRaise("RoundStarted", new DD.Prototype.Feedback.FeelRound(4));
                HudBanner banner = TopBanner(hud);
                Assert.IsTrue(banner.Live(Time.unscaledTime), "§4.10: the round banner is up");
                StringAssert.EndsWith(" IV", banner.Text.text, "§4.10: ROUND IV / RAUND IV");
                Assert.AreEqual(HudBannerEnvelope.RoundStart.Total, 0.35f + 0.9f + 0.3f, 1e-3f, "§4.10: draw 0.35 + hold 0.9 + fade 0.3");
                yield return new WaitForSecondsRealtime(HudBannerEnvelope.RoundStart.Total + 0.2f);
                Assert.IsFalse(banner.Live(Time.unscaledTime), "§4.10: the band is gone");

                // §4.11: the last zombie - the centre stroke, "ROUND N CLEAR" / "RAUND N TEMİZ".
                int clears = hud.RoundClears;
                FeelRaise("LastZombie", new DD.Prototype.Feedback.FeelLastZombie(at, 4, EnemyTier.Standard, null));
                Assert.AreEqual(clears + 1, hud.RoundClears, "§4.11: the clear stroke");
                Assert.IsTrue(hud.ClearBanner.Live(Time.unscaledTime));
                StringAssert.Contains(" IV ", hud.ClearBanner.Text.text, "§4.11: the round's numeral");
                yield return new WaitForSecondsRealtime(HudBannerEnvelope.RoundClear.Total + 0.2f);
                Assert.IsFalse(hud.ClearBanner.Live(Time.unscaledTime), "§4.11: held 0.8 s, gone over 0.3 s");
                Debug.Log("[DD-TEST] wp094.feel notches=" + hud.Reticle.CritNotches + " pops=" + hud.Reticle.KillPops + " multi="
                    + hud.MultiKillsShown + " clears=" + hud.RoundClears);
            }
            finally
            {
                UiMotion.ReducedMotion = reduced;
            }

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>Raises one FeelEvents moment through its backing field, as the bridge's Emit does.</summary>
        private static void FeelRaise<T>(string name, T payload)
        {
            FieldInfo field = typeof(DD.Prototype.Feedback.FeelEvents).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "FeelEvents." + name);
            ((Action<T>)field.GetValue(null))?.Invoke(payload);
        }

        private static DD.Prototype.Feedback.FeelHit FeelHitAt(Vector3 at, bool critical, bool groundFire)
        {
            return new DD.Prototype.Feedback.FeelHit(null, EnemyTier.Standard, 1, at, Vector3.forward, 10f, 0f, false, false, critical,
                DD.Prototype.Damage.BodyRegion.Head, DD.Prototype.Damage.DamageSourceKind.Bullet, 1f, 0.5f, false, groundFire, 1, true,
                ClassicWeaponId.AssaultRifle, false, SpecialWeaponId.Raiju);
        }

        private static DD.Prototype.Feedback.FeelKill Kill(Vector3 at)
        {
            return new DD.Prototype.Feedback.FeelKill(null, EnemyTier.Standard, 1, at, Vector3.forward, DD.Prototype.Damage.BodyRegion.UpperBody,
                DD.Prototype.Damage.DamageSourceKind.Bullet, false, false, 0f, 0, 1, 1, true, ClassicWeaponId.AssaultRifle, false,
                SpecialWeaponId.Raiju, 1, 1, false);
        }

        /// <summary>The HUD's top banner (the one the round start writes).</summary>
        private static HudBanner TopBanner(RunHudScreen hud)
        {
            FieldInfo field = typeof(RunHudScreen).GetField("_banner", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "RunHudScreen._banner");
            return (HudBanner)field.GetValue(hud);
        }

        /// <summary>Boot → selection → a fighting top-down run with a quiet field.</summary>
        private static IEnumerator KosuyaGir()
        {
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return QuietField();
        }

        /// <summary>One real hit of <paramref name="share"/> of max health from <paramref name="towards"/> (the attacker's side).</summary>
        private static void Hit(PlayerHealth health, Vector3 at, Vector3 towards, float share)
        {
            health.DiagnosisInvulnerable = false;
            Assert.IsTrue(health.ApplyDamage(health.Max * share, at + towards * 2f, "Test.WP094"), "the hit landed");
            health.DiagnosisInvulnerable = true;
        }

        /// <summary>The screen's right on the ground (the rig's shake-free right, flattened): a hit from it lights the right edge.</summary>
        private static Vector3 ScreenRightOnGround()
        {
            PrototypeCameraRig cameraRig = PrototypeCameraRig.Active;
            Vector3 right = cameraRig != null ? cameraRig.StableRotation * Vector3.right : Vector3.right;
            right.y = 0f;
            return right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
        }

        // ================================================================== helpers

        private static RectTransform ClusterNamed(RunHudScreen hud, string name)
        {
            foreach (RectTransform cluster in hud.Clusters)
            {
                if (cluster.name == name)
                {
                    return cluster;
                }
            }

            Assert.Fail("no " + name + " cluster");
            return null;
        }

        private static readonly Vector3[] s_corners = new Vector3[4];

        /// <summary>The rect in the canvas's own space (centre origin), from its world corners.</summary>
        private static Rect CanvasRectOf(RectTransform canvas, RectTransform rect)
        {
            Assert.IsNotNull(rect, "rect");
            rect.GetWorldCorners(s_corners);
            Vector3 a = canvas.InverseTransformPoint(s_corners[0]);
            Vector3 b = canvas.InverseTransformPoint(s_corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>An overlay rect in screen pixels (an overlay canvas's world space is the screen).</summary>
        private static Rect ScreenRectOf(RectTransform rect)
        {
            rect.GetWorldCorners(s_corners);
            return Rect.MinMaxRect(s_corners[0].x, s_corners[0].y, s_corners[2].x, s_corners[2].y);
        }

        /// <summary>0.5 px in from every side: two rects that share an edge do not overlap.</summary>
        private static Rect Inset(Rect r)
        {
            return Rect.MinMaxRect(r.xMin + 0.5f, r.yMin + 0.5f, r.xMax - 0.5f, r.yMax - 0.5f);
        }

        private static T Find<T>(Transform root, string block, string name) where T : Component
        {
            foreach (T c in root.GetComponentsInChildren<T>(true))
            {
                if (c.name != name)
                {
                    continue;
                }

                for (Transform p = c.transform.parent; p != null; p = p.parent)
                {
                    if (p.name == block)
                    {
                        return c;
                    }
                }
            }

            Assert.Fail("not found: " + block + "/" + name);
            return null;
        }
    }
}
