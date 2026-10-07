using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Economy;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
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
    ///   corner, and no cluster, row, banner, Special block or "+N" meets another - at 1920 × 1080 (UI 1.0, text 1.0) and the
    ///   Mac 3024 × 1964 (UI 1.2, text 1.25), each a standalone HUD drawn into a target of that size with the scaler's Expand
    ///   by hand (UiControlsProfileResultsTests). A temp ui-settings file - the user's is never touched.
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
            try
            {
                hud.CaptureRound(38);
                Assert.AreEqual(0, cues, "the cue waits for the contact frame");
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.AreEqual(1, cues, "one StampLanded \"round\" at the seal's contact");
            }
            finally
            {
                UiEvents.Raised -= listen;
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

            // The gold: directly above the health, one UiSpace.S or more clear, at most 16 px, on the same left rail.
            Assert.GreaterOrEqual(gold.yMin, health.yMax + (int)UiSpace.S - 0.5f, where + ": the gold sits above the health, ≥ 8 px clear");
            Assert.LessOrEqual(gold.yMin, health.yMax + 16f + 0.5f, where + ": directly above (≤ 16 px)");
            Assert.AreEqual(health.xMin, gold.xMin, 0.5f, where + ": one left rail");
            float goldBottom = gold.yMin - canvasRect.yMin;
            Assert.AreEqual(Mathf.Round(goldBottom / 8f) * 8f, goldBottom, 0.01f, where + ": the gold's bottom on the 8 px grid (from the canvas bottom)");

            // The round: the top-right corner, InkMargin in from both edges; the numeral is the Roman one and fits its box.
            Assert.AreEqual(canvasRect.xMax - 32f, round.xMax, 0.5f, where + ": round's right edge");
            Assert.AreEqual(canvasRect.yMax - 32f, round.yMax, 0.5f, where + ": round's top edge");
            Assert.Greater(round.xMin, canvasRect.center.x + canvasRect.width * 0.25f, where + ": round in the right quarter");
            Rect seal = CanvasRectOf(screen, hud.RoundSeal);
            Assert.AreEqual(round.xMax, seal.xMax, 3f, where + ": the seal accent at the corner (≤ 2 px drift)");
            Assert.Greater(hud.RoundSealHitAt, 0f, "the reveal scheduled the seal's hit");
            if (hud.RoundNumeral.DrawsGlyphs)
            {
                Assert.AreEqual(7, hud.RoundNumeral.GlyphCount, "XXXVIII as glyphs");
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
