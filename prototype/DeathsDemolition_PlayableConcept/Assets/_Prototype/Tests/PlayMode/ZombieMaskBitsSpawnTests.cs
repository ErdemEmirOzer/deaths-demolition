using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-084 · AP-025 contract v002 on real spawns (S-05 §17 A1, C-00 06.10): every slot and armour renderer of every body, at every
    /// LOD, keeps the light / decal layers in the low 16 bits exactly (<see cref="ZombieSkinnedVisual.CharacterRenderingLayers"/> —
    /// the fill light and the shadow layer test read them) and carries the zombie's own skin / dirt / blood indices in bits 16-30
    /// (ZombieAppearance.MaskCombo of its look seed), bit 31 clear. Run scene, every version, two passes so the second one reuses
    /// pooled bodies (a previous life's indices must not survive). The face slot (SlotFace) is bound at LOD0 on every body.
    /// </summary>
    [TestFixture]
    [Category("WP084")]
    public sealed class ZombieMaskBitsSpawnTests
    {
        private const int PerTier = 8;
        private static readonly EnemyTier[] Tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };

        [TearDown]
        public void TearDown()
        {
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Dogus_RenderingLayer_AltBitlerKarakter_UstBitlerMaskeKombinasyonu_YuzYuvasi()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (!(RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                     && RunDirector.Instance.Phase == RunPhase.Fighting) && Time.realtimeSinceStartup - t0 < 40f) yield return null;
            Assert.IsTrue(RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting, "Run sahnesi / Fighting");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;

            Vector3 origin = PlayerRegistry.Primary.transform.position + new Vector3(0f, 0f, 14f);
            int bodiesChecked = 0, renderersChecked = 0, armourChecked = 0, faces = 0, wrong = 0;
            var combos = new HashSet<int>();
            var examples = new List<string>();
            var bodies = new List<ZombieEnemy>(PerTier * Tiers.Length);
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (ZombieEnemy z in bodies) if (z != null && z.IsSpawned) z.Remove();
                bodies.Clear();
                yield return null;
                for (int t = 0; t < Tiers.Length; t++)
                {
                    for (int i = 0; i < PerTier; i++)
                    {
                        ZombieEnemy z = EnemyFactory.Spawn(Tiers[t], 8, origin + new Vector3((i % 4) * 1.5f - 2f, 0f, t * 2f + (i / 4) * 1f), Quaternion.identity);
                        Assert.IsNotNull(z, Tiers[t] + " spawn " + i);
                        bodies.Add(z);
                    }
                }

                yield return null;
                foreach (ZombieEnemy z in bodies)
                {
                    ZombieSkinnedVisual v = z.Skinned;
                    var appearance = z.VersionVisual as ZombieAppearance;
                    Assert.IsNotNull(v, "skinned visual yok");
                    Assert.IsNotNull(appearance, "ZombieAppearance yok");
                    int combo = ZombieAppearance.MaskCombo(appearance.LookSeed);
                    combos.Add(combo);
                    uint high = ((uint)(combo / 9) << ZombieSkinnedVisual.VariationShiftSkin) | ((uint)(combo / 3 % 3) << ZombieSkinnedVisual.VariationShiftDirt)
                                | ((uint)(combo % 3) << ZombieSkinnedVisual.VariationShiftBlood);
                    uint expected = ZombieSkinnedVisual.CharacterRenderingLayers | high;
                    if (v.VariationBits != high) { wrong++; if (examples.Count < 6) examples.Add(z.name + " VariationBits " + v.VariationBits.ToString("X8") + " ≠ " + high.ToString("X8")); }
                    bodiesChecked++;
                    for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                    {
                        for (int s = 0; s < ZombieSkinnedVisual.SlotCount; s++)
                        {
                            SkinnedMeshRenderer r = v.Slot(lod, s);
                            Assert.IsNotNull(r, "yuva " + lod + "/" + s);
                            renderersChecked++;
                            if (r.renderingLayerMask != expected) { wrong++; if (examples.Count < 6) examples.Add(z.name + " lod" + lod + " slot" + s + " " + r.renderingLayerMask.ToString("X8") + " ≠ " + expected.ToString("X8")); }
                        }

                        if (!v.HasArmorSlots) continue;
                        for (int a = 0; a < ZombieSkinnedVisual.ArmorSlotCount; a++)
                        {
                            for (int part = 0; part < ZombieSkinnedVisual.ArmorPartCount; part++)
                            {
                                SkinnedMeshRenderer r = v.ArmorRenderer(lod, a, part);
                                if (r == null) continue;
                                armourChecked++;
                                if (r.renderingLayerMask != expected) { wrong++; if (examples.Count < 6) examples.Add(z.name + " armour lod" + lod + " " + a + "/" + part + " " + r.renderingLayerMask.ToString("X8") + " ≠ " + expected.ToString("X8")); }
                            }
                        }
                    }

                    SkinnedMeshRenderer face = v.Slot(0, ZombieSkinnedVisual.SlotFace);
                    if (v.Look.HasFace && face.enabled && face.sharedMesh != null) faces++;
                    else if (examples.Count < 6) examples.Add(z.name + " face slot boş (look.face=" + v.Look.face + ")");
                }
            }

            Debug.Log("[DD-OBS] wp084.maskbits bodies=" + bodiesChecked + " slotRenderers=" + renderersChecked + " armourRenderers=" + armourChecked
                      + " faces=" + faces + " combos=" + combos.Count + "/27 wrong=" + wrong + " examples=" + string.Join(" | ", examples));
            Assert.AreEqual(2 * PerTier * Tiers.Length, bodiesChecked, "kaynak yok: gövde eksik");
            Assert.AreEqual(bodiesChecked * ZombieModuleLibrary.LodCount * ZombieSkinnedVisual.SlotCount, renderersChecked, "kaynak yok: yuva renderer'ı eksik");
            Assert.Greater(armourChecked, 0, "kaynak yok: zırh renderer'ı yok");
            Assert.AreEqual(0, wrong, "renderingLayerMask: alt 16 bit karakter katmanı, 16-30 maske indeksleri, 31 sıfır");
            Assert.AreEqual(bodiesChecked, faces, "her gövdede LOD0 yüz yuvası bağlı");
            Assert.GreaterOrEqual(combos.Count, 15, "64 gövdede kombinasyon çeşitliliği");

            foreach (ZombieEnemy z in bodies) if (z != null && z.IsSpawned) z.Remove();
            yield return null;
        }
    }
}
