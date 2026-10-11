using DD.Art.Pipeline.EditorTools;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-103 (R4 #4, S-05) · "Where nothing is in the way, the hero walks." The top-down world as the game loads it (World_V6_Greybox,
    /// no FPTP scene) walked by the player's own CharacterController on the <see cref="WorldWalkScan.Cell"/> grid from the spawn: no step
    /// may stop at a collider nothing is drawn on (Invisible) or past what is drawn (Oversize) — C-00 11.10: the acceptance is 0.
    /// Before WP-103: 484 Invisible (the bamboo grove's solid blocks), 91 Oversize (the plank bridge's rail ends, three boulders' boxes).
    /// StepFail (the controller slides 0.3–0.6 m along a drawn structure — a kairo floor edge, the chūmon platform, a boulder's hull —
    /// and the classifier names no contact) is reported and held under <see cref="StepFailCeiling"/> as a regression guard (61 after).
    /// </summary>
    public sealed class WorldWalkScanTests
    {
        public const int StepFailCeiling = 80;

        [Test]
        [Category("R4")]
        public void V6_GorunmeyenEngel_Sifir_KoruIciYurunur()
        {
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);
            WorldWalkScan.Report r = WorldWalkScan.Scan(WorldWalkScan.DefaultRadius, null);
            Debug.Log("[DD-TEST] wp103.walk " + WorldWalkScan.Describe(r));
            var unexplained = new System.Text.StringBuilder();
            foreach (WorldWalkScan.Blocker b in r.blockers)
            {
                if (b.kind != "Invisible" && b.kind != "Oversize" && b.kind != "StepFail") continue;
                if (unexplained.Length < 1200) unexplained.Append('\n').Append(b.kind).Append(' ').Append(b.edges).Append(" @ (").Append(b.x.ToString("F1")).Append(", ")
                    .Append(b.z.ToString("F1")).Append(") ").Append(b.collider).Append(' ').Append(b.via);
            }

            Assert.AreEqual(0, r.invisible + r.oversize, "görünmüyor ama geçilemiyor:" + unexplained);
            Assert.LessOrEqual(r.stepFail, StepFailCeiling, "çizili yapı boyunca kayıp duran adımlar arttı:" + unexplained);
            Assert.Greater(r.reachableM2, 10500f, "bambu korusunun içi yürünebilir (WP-103 öncesi 10 074 m²)");
        }
    }
}
