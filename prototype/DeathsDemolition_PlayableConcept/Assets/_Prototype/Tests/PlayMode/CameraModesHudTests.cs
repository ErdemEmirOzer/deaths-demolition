using System.Collections;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// M4-R1 N2 (WP-070 §5.1, §4.5): the Pause CAMERA selector switches the live camera and saves the choice; after the
    /// blend the run HUD aims down the screen centre with the look-mode reticle, and top-down comes back unchanged.
    /// WP-068 12b: first person mirrors the weapon cluster's internals onto one left rail; top-down restores the corner layout.
    /// </summary>
    public sealed class CameraModesHudTests
    {
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator PauseKameraSecici_UcuncuSahis_MerkezNisangah_TependenGeri()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return Bekle(() => RunEvents.Current.Phase == RunPhase.Fighting, 20f, "savaş fazı");

            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(pause, "PauseScreen yok");
            Assert.IsNotNull(hud, "HUD yok");
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "koşu tepeden başlar");

            pause.SetPaused(true);
            yield return null;
            Assert.IsTrue(pause.IsPaused, "Pause açılmadı");
            Assert.IsTrue(pause.CameraButton(CameraMode.TopDown).IsSelectedChoice, "seçili = mevcut mod");

            pause.ChooseCamera(CameraMode.ThirdPerson);
            Assert.AreEqual(CameraMode.ThirdPerson, CameraModeService.Target, "seçim anında istenir");
            Assert.AreEqual(1, UiSettings.CameraMode, "seçim anında kaydedilir");
            Assert.IsTrue(pause.CameraButton(CameraMode.ThirdPerson).IsSelectedChoice);
            Assert.IsFalse(pause.CameraButton(CameraMode.TopDown).IsSelectedChoice);
            StringAssert.Contains("shoulder", pause.CameraCaption);

            pause.SetPaused(false);
            yield return Bekle(() => !CameraModeService.IsTransitioning, 5f, "harman biter");
            yield return null;
            yield return null;

            Assert.AreEqual(Vector2.zero, hud.Reticle.Root.anchoredPosition, "TP: nişangâh ekran merkezinde");
            Assert.IsTrue(hud.Reticle.DotShown, "TP: merkez nokta");
            Assert.AreEqual(HudReticle.LookMinRadius, hud.Reticle.MinimumRadius, 0.001f);
            Assert.IsFalse(UiCursor.ContextVisible, "oyunda imleç bağlamı gizli");

            // WP-070V: the FP viewmodel stays out of the weapon cluster, so the cluster keeps its corner in first person.
            Vector2 home = hud.WeaponCluster.anchoredPosition;
            pause.SetPaused(true);
            yield return null;
            pause.ChooseCamera(CameraMode.FirstPerson);
            pause.SetPaused(false);
            yield return Bekle(() => !CameraModeService.IsTransitioning, 5f, "harman biter");
            yield return new WaitForSecondsRealtime(0.5f);   // the cluster's Base move
            float height = ((RectTransform)hud.WeaponCluster.parent).rect.height;
            Vector2 fpAt = RunHudScreen.WeaponClusterPosition(home, true, ((RectTransform)hud.WeaponCluster.parent).rect.size);
            Assert.AreEqual(fpAt.x, hud.WeaponCluster.anchoredPosition.x, 2f, "FP: silah kümesi sol altta (C-00 25.09, görüş modeli v2)");
            Assert.AreEqual(RunHudScreen.FpWeaponClusterBottom, hud.WeaponCluster.anchoredPosition.y, 2f, "FP: can bloğunun üstünde");
            Assert.IsTrue(hud.Reticle.DotShown);

            // WP-068 12b (AD FP cluster SHOULD-1): in first person the cluster's internals mirror onto one left rail - the ring at
            // the box's left end, the count 8 px right of it (x 100 at 1080), the ladder under it ending at x 295, the state word
            // left-aligned with the count. The box itself is the one asserted above.
            {
                RectTransform cluster = hud.WeaponCluster;
                Assert.IsTrue(hud.WeaponClusterMirrored, "FP: iç düzen aynalı");
                Rect ring = LocalRect(cluster, cluster.Find("Back/ReloadRing"));
                Rect ammo = LocalRect(cluster, cluster.Find("Content/Ammo"));
                Rect ladder = LocalRect(cluster, cluster.Find("Content/Ladder"));
                Rect caption = LocalRect(cluster, cluster.Find("Content/ReloadCaption"));
                float left = cluster.rect.xMin;
                Assert.AreEqual(left, ring.xMin, 2f, "FP: halka kutunun sol ucunda (x 32)");
                Assert.Less(ring.xMax, ammo.xMin, "FP: halka sayının solunda");
                Assert.AreEqual(left + RunHudScreen.FpCountInset, ammo.xMin, 1f, "FP: sayı halkanın 8 px sağında (x 100)");
                Assert.AreEqual(ammo.xMin, caption.xMin, 1f, "FP: durum kelimesi sayıyla sola hizalı");
                Assert.AreEqual(left + RunHudScreen.FpCountInset + RunHudScreen.FpLadderWidth, ladder.xMax, 2f, "FP: merdiven can fırçasıyla biter (x 295)");
            }

            // AD R1-A MUST-1: BEHIND YOU straight behind the camera shows the edge arc on screen, its stroke above the bottom.
            Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            RunHudScreen.CaptureRearDirection = -forward;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(hud.RearEdge.Shown, "FP: arkadaki tehdit kenar yayı olarak görünür");
            Vector2 arc = hud.RearEdge.Position;
            Assert.Less(Mathf.Abs(arc.x), height, "yay ekranda (park edilmiş değil)");
            Assert.Less(arc.y, 0f, "tam arka = alt yarı");
            Assert.Greater(arc.y - HudRearEdge.StrokeMidRadius, -height * 0.5f + HudRearEdge.EdgeInset - 3f, "çizgi iç kenarda, fiziksel kenarda değil");
            RunHudScreen.CaptureRearDirection = null;

            pause.SetPaused(true);
            yield return null;
            pause.ChooseCamera(CameraMode.TopDown);
            pause.SetPaused(false);
            yield return Bekle(() => !CameraModeService.IsTransitioning, 5f, "harman biter");
            yield return null;
            yield return null;
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
            Assert.IsFalse(hud.Reticle.DotShown, "tepeden: nokta yok");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(home.y, hud.WeaponCluster.anchoredPosition.y, 2f, "tepeden: küme köşesine döner");
            Assert.IsFalse(hud.WeaponClusterMirrored, "tepeden: köşe düzeni geri (12b)");
            Rect tdRing = LocalRect(hud.WeaponCluster, hud.WeaponCluster.Find("Back/ReloadRing"));
            Assert.AreEqual(hud.WeaponCluster.rect.xMax, tdRing.xMax, 3.5f, "tepeden: halka ekran kenarında (paralaks ≤ 3 px)");
            Assert.AreEqual(0, UiSettings.CameraMode);

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>A child's box in <paramref name="space"/>'s local units (its world corners brought back; a turned child's bounds).</summary>
        private static Rect LocalRect(RectTransform space, Transform target)
        {
            Assert.IsNotNull(target, "silah kümesi alt öğesi bulunamadı");
            var corners = new Vector3[4];
            ((RectTransform)target).GetWorldCorners(corners);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = space.InverseTransformPoint(corners[i]);
                xMin = Mathf.Min(xMin, p.x);
                yMin = Mathf.Min(yMin, p.y);
                xMax = Mathf.Max(xMax, p.x);
                yMax = Mathf.Max(yMax, p.y);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
