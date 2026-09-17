using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-036 (ADR-036-1) · AP-014 tier parts in Unity: files + import contract, the tier library
    /// (4 parts × 3 fits × 4 LODs, bones on the ZombieRig), the tint table straight from the
    /// manifest, the Tier Lit materials (keyword only on tinted versions, lacquer floor only with
    /// a lacquer tint), and the triangle budgets with the known LOD2/3 overage recorded.
    /// </summary>
    [Category("WP036")]
    public sealed class ArtTierTests
    {
        private static ZombieTierLibrary Library()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ZombieTierLibrary>(ArtTierBuilder.LibraryPath);
            Assert.IsNotNull(lib, "DD_ZombieTier.asset (run DD/Art/Run Tier Parts)");
            return lib;
        }

        [Test]
        public void Dosyalar_Sozlesme_4Fbx_Atlas_Manifest()
        {
            string[] fbx = { "zombie_tier_special_collar", "zombie_tier_elite_sode", "zombie_tier_elite_backframe", "zombie_tier_ultra_yoke" };
            foreach (string f in fbx)
            {
                string path = ArtTierBuilder.TierFolder + "/" + f + ".fbx";
                Assert.IsTrue(File.Exists(path), path);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.IsNotNull(importer, "importer " + f);
                Assert.AreEqual(ModelImporterAnimationType.Human, importer.animationType, f + " Humanoid (contract v1.1)");
                Assert.IsTrue(ArtTierBuilder.IsTierModel(path));
            }

            foreach (string t in new[] { ArtTierBuilder.BaseColorMap, ArtTierBuilder.NormalMap, ArtTierBuilder.PackedMap, ArtTierBuilder.MaskMap, ArtTierBuilder.MetallicSmoothnessMap })
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(t);
                Assert.IsNotNull(tex, t);
                Assert.AreEqual(1024, tex.width, t + " 1K atlas");
            }

            Assert.IsTrue(File.Exists(ArtTierBuilder.ManifestJson));
            Assert.IsFalse(ArtTierBuilder.IsTierModel(ArtArmorBuilder.ArmorFolder + "/x.fbx"));
        }

        [Test]
        public void Kutuphane_4Parca_3Fit_4LOD_KemiklerRigde_Ankrajlar()
        {
            ZombieTierLibrary lib = Library();
            Assert.AreEqual(ZombieTierLibrary.Schema, lib.schema); Assert.AreEqual("AP-014", lib.package);
            Assert.AreEqual(4, lib.parts.Length);
            var byId = new Dictionary<string, ZombieTierLibrary.Part>();
            foreach (ZombieTierLibrary.Part p in lib.parts) byId[p.id] = p;
            Assert.AreEqual(EnemyTier.Special, byId["special_collar"].tier); Assert.AreEqual("anchor_torso", byId["special_collar"].anchor);
            Assert.AreEqual(EnemyTier.Elite, byId["elite_sode"].tier); Assert.AreEqual(1, byId["elite_sode"].materialIndex, "sode: AP-008 armour atlas");
            Assert.AreEqual(EnemyTier.Elite, byId["elite_backframe"].tier); Assert.AreEqual("socket_back", byId["elite_backframe"].anchor);
            Assert.AreEqual(EnemyTier.Ultra, byId["ultra_yoke"].tier); Assert.AreEqual("socket_back", byId["ultra_yoke"].anchor);
            var rigBones = new HashSet<string>(ArtCharacterContract.Bones(AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterContract.ZombieAvatarSource), ArtCharacterPrefabBuilder.ZombieArmature).Keys);
            int refs = 0; var report = new System.Text.StringBuilder();
            foreach (ZombieTierLibrary.Part p in lib.parts)
            {
                Assert.AreEqual(3, p.fits.Length, p.id + " fits");
                foreach (ZombieTierLibrary.Fit f in p.fits)
                {
                    Assert.AreEqual(4, f.lods.Length);
                    for (int lod = 0; lod < 4; lod++)
                    {
                        ZombieModuleLibrary.MeshRef m = f.lods[lod];
                        Assert.IsNotNull(m, p.id + " " + f.fit + " LOD" + lod); Assert.IsNotNull(m.mesh, p.id + " " + f.fit + " LOD" + lod + " mesh");
                        Assert.Greater(m.boneNames.Length, 0);
                        foreach (string b in m.boneNames) if (!string.IsNullOrEmpty(b)) Assert.IsTrue(rigBones.Contains(b), p.id + " bone " + b + " not on ZombieRig");
                        refs++;
                    }

                    report.Append(p.id).Append('/').Append(f.fit).Append(" tris ").Append(f.lods[0].triangles).Append('/').Append(f.lods[1].triangles).Append('/').Append(f.lods[2].triangles).Append('/').Append(f.lods[3].triangles).Append('\n');
                }

                Assert.LessOrEqual(p.trianglesLod0, 1500, p.id + " LOD0 bütçesi 1500");
                // WP-055 / AP-014B: the far LODs are inside the manifest budgets too (collar / yoke hulls 158 / 78; budgets.ok = true)
                int[] budget = { 1500, 600, 200, 80 };
                for (int f = 0; f < p.fits.Length; f++) for (int lod = 0; lod < ZombieTierLibrary.LodCount; lod++)
                    Assert.LessOrEqual(p.fits[f].lods[lod].triangles, budget[lod], p.id + " " + p.fits[f].fit + " LOD" + lod + " bütçesi " + budget[lod] + " (AP-014B)");
            }

            Assert.AreEqual(48, refs, "4 × 3 × 4 skinned mesh referansı");
            var buffer = new ZombieTierLibrary.Part[ZombieTierLibrary.MaxPartsPerTier];
            Assert.AreEqual(0, lib.PartsOf(EnemyTier.Standard, buffer)); Assert.AreEqual(1, lib.PartsOf(EnemyTier.Special, buffer)); Assert.AreEqual(2, lib.PartsOf(EnemyTier.Elite, buffer)); Assert.AreEqual(1, lib.PartsOf(EnemyTier.Ultra, buffer));
            Debug.Log("[DD-TEST] wp036.library\n" + report + "(AP-014B: dört LOD da bütçede — 1500/600/200/80)");
        }

        [Test]
        public void TintTablosu_ManifestIleBirebir_MateryallerTierLit()
        {
            ZombieTierLibrary lib = Library();
            ArtTierBuilder.ReadParts(out Dictionary<string, object> doc, out _);
            List<ArtTierBuilder.TintRow> rows = ArtTierBuilder.ReadTints(doc);
            Assert.AreEqual(4, lib.tints.Length);
            for (int t = 0; t < 4; t++)
            {
                ZombieTierLibrary.TintEntry e = lib.TintOf((EnemyTier)t);
                Assert.IsNotNull(e);
                Assert.AreEqual(rows[t].clothK, e.clothK, 1e-5f, "clothK " + (EnemyTier)t); Assert.AreEqual(rows[t].lacquerK, e.lacquerK, 1e-5f, "lacquerK " + (EnemyTier)t);
                Assert.AreEqual(rows[t].cloth, e.cloth); Assert.AreEqual(rows[t].lacquer, e.lacquer);
            }

            // AP-014 table (AP-014E, K3): Special cloth pale ash #AEB4BA k 0.56 (no lacquer; v001 ash #585F64 k 0.42 sat on the ground value under the Look B LUT); Elite plum lacquer #4A3448 k 0.70; Ultra rust lacquer #6B5140 k 0.72; Standard none
            Assert.AreEqual(0f, lib.TintOf(EnemyTier.Standard).clothK); Assert.AreEqual(0f, lib.TintOf(EnemyTier.Standard).lacquerK);
            Assert.AreEqual(0.56f, lib.TintOf(EnemyTier.Special).clothK, 1e-4f); Assert.AreEqual(0f, lib.TintOf(EnemyTier.Special).lacquerK);
            Assert.AreEqual(0.7f, lib.TintOf(EnemyTier.Elite).lacquerK, 1e-4f); Assert.AreEqual(0.72f, lib.TintOf(EnemyTier.Ultra).lacquerK, 1e-4f);
            Assert.AreEqual("#AEB4BA", "#" + ColorUtility.ToHtmlStringRGB(lib.TintOf(EnemyTier.Special).cloth));
            Assert.AreEqual("#4A3448", "#" + ColorUtility.ToHtmlStringRGB(lib.TintOf(EnemyTier.Elite).lacquer));
            Assert.AreEqual("#6B5140", "#" + ColorUtility.ToHtmlStringRGB(lib.TintOf(EnemyTier.Ultra).lacquer));

            Shader tierLit = Shader.Find(ArtTierBuilder.ShaderName);
            Assert.IsNotNull(tierLit, "DD/Character/Tier Lit derlendi");
            for (int t = 0; t < 4; t++)
            {
                var tier = (EnemyTier)t;
                Material part = lib.PartMaterial(tier, 0), body = lib.BodyMaterial(tier), armor = lib.ArmorMaterial(tier);
                Assert.IsNotNull(part); Assert.IsNotNull(body); Assert.IsNotNull(armor);
                foreach (Material m in new[] { part, body, armor }) { Assert.AreEqual(tierLit, m.shader, m.name); Assert.IsNotNull(m.GetTexture("_TierMask"), m.name + " _TierMask"); Assert.IsFalse(m.IsKeywordEnabled("_DETAIL_MULX2"), m.name + " çarpımsal detay kapalı"); }
                bool tinted = t > 0;
                Assert.AreEqual(tinted, part.IsKeywordEnabled("_TIER_TINT"), part.name + " keyword");
                // body: cloth only (AP-007 Mask.R = skin!); armour: lacquer only (AP-008 Mask.R)
                Assert.AreEqual(0f, body.GetFloat("_TierLacquerK"), body.name + " gövdede lake tint yok (Mask.R = deri)");
                Assert.AreEqual(0f, armor.GetFloat("_TierClothK"), armor.name + " zırhta kumaş tint yok");
                Assert.AreEqual(lib.TintOf(tier).clothK, body.GetFloat("_TierClothK"), 1e-5f);
                Assert.AreEqual(lib.TintOf(tier).lacquerK, armor.GetFloat("_TierLacquerK"), 1e-5f);
                Assert.AreEqual(lib.TintOf(tier).lacquerK > 0f ? ArtTierBuilder.LacquerFloor : 0f, armor.GetFloat("_TierLacquerFloor"), 1e-5f, armor.name + " lake tabanı yalnız lake tint'iyle");
                Assert.AreEqual(t == 0 ? 0f : 1f, body.GetFloat("_TierTintOn") + armor.GetFloat("_TierTintOn") > 0f ? 1f : 0f, "tint yalnız versiyonlarda");
            }

            Assert.AreEqual(0.38f, ArtTierBuilder.LacquerFloor, 1e-5f, "AP-014: lake tabanı v 0,35–0,40");
        }
    }
}
