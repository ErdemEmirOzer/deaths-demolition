using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    /// WP-088 (C-00 05.10, from the Codex AP-025 diagnosis) · every garment slot of a real spawn samples the atlas its mesh is
    /// unwrapped for. Real bodies in the Run scene, every version (Standard / Special / Elite / Ultra) through the factory's
    /// normal spawn path. Each skinned slot renderer (every LOD × body/top/bottom/head, every sub-mesh) is classified by the
    /// atlas of its material's base map (body atlas / cloth atlas / armour / tier parts) and compared with the atlas the AP-007
    /// library data names for that mesh — independent of the code under test: the zombie's look → module → variant LOD material
    /// names, or the module's own list when the LOD list is empty (M_Zombie_Atlas → body atlas, M_Zombie_Cloth → cloth atlas;
    /// the body slot is the body atlas). ASSERTED: 0 sub-mesh slots on the wrong atlas. REPORTED: per version compared / correct / wrong, the
    /// wrong pairs (expected → actual), first examples; JSON to DD_WP088_AP025_OUT when set (K/wp-088/evidence/ap025-material).
    /// </summary>
    [TestFixture]
    [Category("WP088")]
    public sealed class ZombieMaterialAtlasTests
    {
        private const int PerTier = 12;
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
        public IEnumerator KiyafetSlotlari_DogruAtlas_TumVersiyonlar()
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

            CharacterAssetSet set = CharacterAssetSet.Load();
            ZombieModuleLibrary library = set != null ? set.ZombieLibrary : null;
            Assert.IsNotNull(library, "zombie module library");

            var json = new StringBuilder("{\"schema\":\"dd-wp088-ap025-material-v1\",\"tiers\":[");
            int wrongAll = 0, comparedAll = 0;
            Vector3 origin = PlayerRegistry.Primary.transform.position + new Vector3(0f, 0f, 14f);
            for (int t = 0; t < Tiers.Length; t++)
            {
                EnemyTier tier = Tiers[t];
                var bodies = new List<ZombieEnemy>(PerTier);
                for (int i = 0; i < PerTier; i++)
                {
                    ZombieEnemy z = EnemyFactory.Spawn(tier, 8, origin + new Vector3((i % 6) * 1.5f, 0f, (i / 6) * 1.5f), Quaternion.identity);
                    Assert.IsNotNull(z, tier + " spawn " + i);
                    bodies.Add(z);
                }
                yield return null;

                int compared = 0, wrong = 0, renderers = 0;
                var hist = new SortedDictionary<string, int>(StringComparer.Ordinal);
                string first = "";
                var pairs = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var examples = new List<string>();
                foreach (ZombieEnemy z in bodies)
                {
                    ZombieSkinnedVisual v = z.Skinned;
                    Assert.IsNotNull(v, tier + ": skinned visual yok");
                    // what the spawn path left on each slot
                    var actual = new List<string[]>();
                    var names = new List<string>();
                    for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                    {
                        for (int s = 0; s < ZombieSkinnedVisual.SlotCount; s++)
                        {
                            SkinnedMeshRenderer r = v.Slot(lod, s);
                            actual.Add(r != null && r.gameObject.activeInHierarchy ? Families(r.sharedMaterials) : null);
                            names.Add(r != null ? r.name : "-");
                            if (r != null && r.gameObject.activeInHierarchy)
                            {
                                foreach (Material mm in r.sharedMaterials)
                                {
                                    string key = (mm != null ? mm.name : "null") + "=" + Family(mm);
                                    hist[key] = hist.TryGetValue(key, out int c) ? c + 1 : 1;
                                }
                                if (lod == 0 && z == bodies[0]) first += " s" + s + ":" + r.name + "→" + (r.sharedMaterial != null ? r.sharedMaterial.name : "null");
                            }
                        }
                    }
                    // what the library data names for the same slots (the mesh's own atlas)
                    var appearance = z.VersionVisual as ZombieAppearance;
                    Assert.IsNotNull(appearance, tier + ": ZombieAppearance yok");
                    ZombieLook look = v.Look;   // the look the visual actually assembled
                    int k = 0;
                    for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                    {
                        for (int s = 0; s < ZombieSkinnedVisual.SlotCount; s++, k++)
                        {
                            string[] a = actual[k];
                            if (a == null) continue;
                            renderers++;
                            string[] e = Expected(library, look, lod, s);
                            for (int m = 0; m < Math.Max(a.Length, e.Length); m++)
                            {
                                compared++;
                                string ea = m < e.Length ? e[m] : "none", aa = m < a.Length ? a[m] : "none";
                                if (ea == aa) continue;
                                wrong++;
                                string pair = ea + "->" + aa;
                                pairs[pair] = pairs.TryGetValue(pair, out int n) ? n + 1 : 1;
                                if (examples.Count < 6) examples.Add(tier + " lod" + lod + " slot" + s + "[" + m + "] " + names[k] + " " + pair + " " + Detail(library, look, lod, s, v.Slot(lod, s)));
                            }
                        }
                    }
                }

                var h = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in hist) h.Append(h.Length > 0 ? ", " : "").Append(kv.Key).Append(" ×").Append(kv.Value);
                Debug.Log("[DD-OBS] wp088.ap025.material.detail tier=" + tier + " tierLibReady=" + (ZombieTierLibrary.Active != null && ZombieTierLibrary.Active.IsReady)
                          + " lod0(first)=" + first + " | spawned materials: " + h);
                foreach (ZombieEnemy z in bodies) if (z != null && z.IsSpawned) z.Remove();
                yield return null;

                comparedAll += compared;
                wrongAll += wrong;
                var p = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in pairs) p.Append(p.Length > 0 ? "," : "").Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
                Debug.Log("[DD-OBS] wp088.ap025.material tier=" + tier + " zombies=" + bodies.Count + " renderers=" + renderers + " subMeshSlots=" + compared
                          + " correct=" + (compared - wrong) + " wrong=" + wrong + " pairs={" + p + "} examples=" + string.Join(" | ", examples));
                if (t > 0) json.Append(',');
                json.Append("{\"tier\":\"").Append(tier).Append("\",\"zombies\":").Append(bodies.Count).Append(",\"renderers\":").Append(renderers)
                    .Append(",\"subMeshSlots\":").Append(compared).Append(",\"correct\":").Append(compared - wrong).Append(",\"wrong\":").Append(wrong)
                    .Append(",\"wrongShare\":").Append(compared > 0 ? ((float)wrong / compared).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "0")
                    .Append(",\"pairs\":{").Append(p).Append("},\"examples\":[");
                for (int i = 0; i < examples.Count; i++) json.Append(i > 0 ? "," : "").Append('"').Append(examples[i].Replace("\"", "'")).Append('"');
                json.Append("]}");
            }
            json.Append("],\"subMeshSlots\":").Append(comparedAll).Append(",\"wrong\":").Append(wrongAll).Append('}');

            string outPath = Environment.GetEnvironmentVariable("DD_WP088_AP025_OUT");
            if (!string.IsNullOrEmpty(outPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, json.ToString());
            }

            Assert.Greater(comparedAll, 0, "no garment slot compared");
            Assert.AreEqual(0, wrongAll, "garment sub-mesh slots on the wrong atlas: " + wrongAll + "/" + comparedAll);
        }

        /// <summary>The atlas families the AP-007 library data names for one slot's sub-meshes (see the class summary).</summary>
        private static string[] Expected(ZombieModuleLibrary library, ZombieLook look, int lod, int slot)
        {
            if (slot == ZombieSkinnedVisual.SlotBody) return new[] { "body" };
            string id = slot == ZombieSkinnedVisual.SlotTop ? look.top : slot == ZombieSkinnedVisual.SlotBottom ? look.bottom : look.head;
            string colourway = slot == ZombieSkinnedVisual.SlotTop ? look.topColourway : slot == ZombieSkinnedVisual.SlotBottom ? look.bottomColourway : ZombieModuleLibrary.NoColourway;
            ZombieModuleLibrary.Module module = library.FindModule(id);
            if (module == null) return new[] { "body" };
            ZombieModuleLibrary.Variant variant = module.Find(colourway, look.fit);
            string[] lodNames = variant != null && variant.lods != null && lod < variant.lods.Length && variant.lods[lod] != null ? variant.lods[lod].materials : null;
            string[] names = lodNames != null && lodNames.Length > 0 ? lodNames : module.materials;
            if (names == null || names.Length == 0) names = new[] { ZombieModuleLibrary.MaterialAtlas };
            var f = new string[names.Length];
            for (int i = 0; i < names.Length; i++) f[i] = names[i] == ZombieModuleLibrary.MaterialCloth ? "cloth" : "body";
            return f;
        }

        private static string Detail(ZombieModuleLibrary library, ZombieLook look, int lod, int slot, SkinnedMeshRenderer r)
        {
            string id = slot == ZombieSkinnedVisual.SlotTop ? look.top : slot == ZombieSkinnedVisual.SlotBottom ? look.bottom : slot == ZombieSkinnedVisual.SlotHead ? look.head : "body";
            string cw = slot == ZombieSkinnedVisual.SlotTop ? look.topColourway : slot == ZombieSkinnedVisual.SlotBottom ? look.bottomColourway : ZombieModuleLibrary.NoColourway;
            ZombieModuleLibrary.Module module = slot == ZombieSkinnedVisual.SlotBody ? null : library.FindModule(id);
            ZombieModuleLibrary.Variant variant = module != null ? module.Find(cw, look.fit) : null;
            string[] lodNames = variant != null && variant.lods != null && lod < variant.lods.Length && variant.lods[lod] != null ? variant.lods[lod].materials : null;
            var mats = new StringBuilder();
            foreach (Material m in r.sharedMaterials) mats.Append(mats.Length > 0 ? "+" : "").Append(m != null ? m.name : "null");
            return "{" + id + ":" + cw + "/" + look.fit + " lodList=[" + (lodNames != null ? string.Join(",", lodNames) : "null") + "] module=[" + (module != null && module.materials != null ? string.Join(",", module.materials) : "-")
                   + "] subMeshes=" + (r.sharedMesh != null ? r.sharedMesh.subMeshCount : -1) + " actual=" + mats + "}";
        }

        /// <summary>Atlas family of each material by its base map: body / cloth / armor / tier / other:name / none.</summary>
        private static string[] Families(Material[] materials)
        {
            var f = new string[materials.Length];
            for (int i = 0; i < materials.Length; i++) f[i] = Family(materials[i]);
            return f;
        }

        private static string Family(Material m)
        {
            if (m == null) return "none";
            Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
            string n = tex != null ? tex.name.ToLowerInvariant() : "";
            if (n.Contains("zombie_cloth")) return "cloth";
            if (n.Contains("zombie_atlas")) return "body";
            if (n.Contains("zombie_armor")) return "armor";
            if (n.Contains("zombie_tier")) return "tier";
            return "other:" + (tex != null ? tex.name : m.name);
        }
    }
}
