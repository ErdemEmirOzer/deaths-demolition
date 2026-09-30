using System.Collections;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// C-00 01.10 GC finding (HitFeedbackDirector.Update 4224 B in Kalabalik40 after the V6 / FP-arms suites): the lantern
    /// scan (FindObjectsOfType + .name reads) must run in a scene's first frames — immediately on the scene change and once
    /// more LanternFlames.SettleFrames later — and never again on a steady frame, however far the clock runs.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073LanternScanGcTests
    {
        [UnityTest]
        public IEnumerator SahneDegisimi_TaramaHemen_SonraKararliKare0B()
        {
            VfxDressing vfx = HitFeedbackDirector.Instance.Vfx;
            Assert.IsNotNull(vfx);
            LanternFlames lanterns = vfx.Lanterns;
            Assert.IsNotNull(lanterns);
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.CreateScene("wp073-lantern-gc");
            try
            {
                vfx.Tick(Time.time);   // settle whatever the previous scene left
                int before = lanterns.Scans;
                SceneManager.SetActiveScene(scene);
                vfx.Tick(Time.time);   // the old code waited up to RescanInterval (5 s) here
                Assert.AreEqual(before + 1, lanterns.Scans, "sahne değişimi aynı karede taranır");
                for (int i = 0; i <= LanternFlames.SettleFrames; i++) { yield return null; vfx.Tick(Time.time); }
                Assert.AreEqual(before + 2, lanterns.Scans, "tek yerleşme taraması");

                float t = Time.time;
                void Steady() { for (int i = 0; i < 8; i++) { t += LanternFlames.RescanInterval; vfx.Tick(t); } }
                Steady();
                byte[] bait = null;
                Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
                Assert.That(() => Steady(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "kararlı kare 0 B (tarama yok)");
                Assert.AreEqual(before + 2, lanterns.Scans, "kararlı karede yeniden tarama yok");
                Assert.IsNotNull(bait);
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }

            yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
