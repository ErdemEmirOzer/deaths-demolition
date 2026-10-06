using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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
    /// WP-091 R2 · Z3 static character budget (S-05; C-00 06.10 definitions in tools/qa/r2-measure-contract.md "Z3").
    /// The Z3 keys that are not probe ratios need Unity (skinned bone lists, imported texture sizes, material counts). This
    /// test measures them on the shipped character assets and hands them to the acceptance chain as
    /// &lt;DD_R2_MEASURE_DIR&gt;/z3-static.json (schema dd-r2-z3-static-v1; the chain runs it in its r2 stage with
    /// DD_R2_MEASURE_DIR = &lt;E&gt;/r2). tools/qa/r2_z3.py (load_static) merges the file with the R2 / R1 probe ratios into
    /// r2-Z3.json. Without the variable nothing is written (ordinary CI), as with R2Olcum; DD_R2_COMMIT stamps the commit.
    ///
    /// The key list is not hard-coded: it is read from tools/qa/r2-gates.json (Z3 checks + report minus the three ratio
    /// keys r2_z3.RATIOS computes, as r2_z3.z3_keys does), and the test fails before measuring when the contract asks for
    /// a key it cannot measure or when it would write a key the contract does not have.
    ///
    /// Family = a body fit of the zombie module library (A / B / C; AP-025 O1 bodies, fit C = the H2 heavy fit). A family's
    /// skinned meshes are its body, every module variant of that fit (top, bottom, head, face, and the library-only accessory),
    /// the AP-008 armour meshes of that fit and the AP-014 tier parts of that fit.
    ///  - bonesMax: the largest SkinnedMeshRenderer.bones.Length a family binds. ZombieSkinnedVisual.Bind / BindArmor and
    ///    TierSilhouetteSlot.Bind write renderer.bones from MeshRef.boneNames, which ArtCharacterPrefabBuilder.MeshRefOf copied
    ///    from the imported FBX renderer; both lists are read and must agree. anchor_helmet / anchor_torso / anchor_legs and empty
    ///    (null-transform) entries are not counted (C-00 06.10).
    ///  - materialsPerFamilyMax (C-00 06.10 13:30: armour and tier parts included): distinct materials on the renderers of one
    ///    zombie instance of a family, per version, with every
    ///    armour slot fitted (the Z3 frame runs -ddArmor all) and the version's tier parts — the material arrays
    ///    ZombieSkinnedVisual.ApplyMaterial binds (module / per-LOD lists, ZombieModuleLibrary.MaterialsFor with the
    ///    ZombieTierLibrary body tint as ZombieAppearance.ApplyTierTint does), the armour tint, TierSilhouetteSlot's part material.
    ///  - materialsPerFamilyCrowdTotal (report): the skin / cloth materials of one body family over the four versions.
    ///  - atlasesPerFamilyMax: the texture sets (ArtImportContract role slug) whose PBR maps the family's skin / cloth materials
    ///    bind — the family atlases only (C-00 06.10 13:30).
    ///  - armorTierAtlasesTotal (report): the texture sets the AP-008 armour and AP-014 tier-part materials bind; they are shared
    ///    by every family, so they are counted once, apart from the family line (C-00 06.10 13:30).
    ///  - mapsPerAtlasMax: distinct PBR maps (BaseColor, Normal, ORM, MetallicSmoothness, Packed) of one atlas over every
    ///    material a zombie instance binds; the mask Texture2DArray, ID / tier masks, detail masks and the library's 4×4 tint
    ///    sub-textures are not atlas maps (logged).
    ///  - characterTextureMaxPx: the largest imported size of a Character-class texture (a Texture2DArray reports its slice).
    ///  - shippedCharacterMB: the character row of ArtAssetGateTests.DokuPolitikasi_… (ADR-015-1 v3 / DEC-093): the same rule
    ///    (ArtImportContract.IsShippedMap — since WP-091 the _TierMask masks ship) and the same ArtImportContract.EstimatedShippedBytes
    ///    estimate (since WP-091 the AP-025 mask array as 9 BC4 slices). Every Character-class map a measured material binds must
    ///    be in that row, or the source is not in the recording.
    /// The source is in the recording (C-00 06.10): every asset a value rests on is logged ([DD-OBS] r2.z3.static.*) and the test
    /// fails without writing when a source is missing (no family, no skinned mesh, no imported renderer, no material, no texture).
    /// After writing, the values are asserted against the contract limits read from the same file (CI catches a regression early;
    /// the gate verdict stays with r2_z3.py).
    /// </summary>
    [TestFixture]
    [Category("WP091")]
    public sealed class Z3CharacterBudgetTests
    {
        public const string TestName = "DD.Art.Tests.EditMode.Z3CharacterBudgetTests.Z3_KarakterButcesi_StatikAnahtarlar_Yazilir";
        public const string Schema = "dd-r2-z3-static-v1";
        public const string FileName = "z3-static.json";
        public const string GateId = "Z3";
        /// <summary>From the repository root (the Unity project sits at prototype/DeathsDemolition_PlayableConcept).</summary>
        public const string ContractRelativePath = "tools/qa/r2-gates.json";
        public const string MeasureDirVariable = "DD_R2_MEASURE_DIR";   // R2Olcum (Assets/_Prototype/Tests/Shared/R2Olcum.cs)
        public const string CommitVariable = "DD_R2_COMMIT";             // R2Olcum: the runner sets the FINAL SHA

        /// <summary>r2_z3.RATIOS: computed by the chain tool from the probe windows; every other Z3 key is static (r2_z3.z3_keys).</summary>
        private static readonly string[] RatioKeys = { "setPassRatio", "batchRatio", "trianglesRatio" };

        private const string KeyTextureMaxPx = "characterTextureMaxPx";
        private const string KeyAtlases = "atlasesPerFamilyMax";
        private const string KeyMapsPerAtlas = "mapsPerAtlasMax";
        private const string KeyShippedMb = "shippedCharacterMB";
        private const string KeyBones = "bonesMax";
        private const string KeyMaterials = "materialsPerFamilyMax";
        private const string KeyCrowdTotal = "materialsPerFamilyCrowdTotal";
        private const string KeyArmorTierAtlases = "armorTierAtlasesTotal";
        /// <summary>What this test can measure; the contract's static key set must equal it.</summary>
        private static readonly string[] MeasuredKeys = { KeyTextureMaxPx, KeyAtlases, KeyMapsPerAtlas, KeyShippedMb, KeyBones, KeyMaterials, KeyCrowdTotal, KeyArmorTierAtlases };

        private static readonly string[] AnchorBones = { ZombieSkinnedVisual.AnchorHelmet, ZombieSkinnedVisual.AnchorTorso, ZombieSkinnedVisual.AnchorLegs };
        private static readonly string[] AtlasOnly = { ZombieModuleLibrary.MaterialAtlas };
        private static readonly string[] ClothOnly = { ZombieModuleLibrary.MaterialCloth };
        private static readonly EnemyTier[] Tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };

        // ---------------------------------------------------------------------------------------- r2-gates.json (JsonUtility)

        [Serializable]
        public sealed class ContractCheck
        {
            public string key;
            public string op;
            public double limit;
        }

        [Serializable]
        public sealed class ContractGate
        {
            public string id;
            public string kind;
            public ContractCheck[] checks;
            public string[] report;
        }

        [Serializable]
        public sealed class ContractFile
        {
            public string schema;
            public ContractGate[] gates;
        }

        // ---------------------------------------------------------------------------------------------------------- test

        [Test]
        public void Z3_KarakterButcesi_StatikAnahtarlar_Yazilir()
        {
            ContractGate gate = Z3Contract(out string contractPath);
            List<string> staticKeys = StaticKeys(gate);
            var notMeasured = new List<string>();
            foreach (string k in staticKeys) if (Array.IndexOf(MeasuredKeys, k) < 0) notMeasured.Add(k);
            var notInContract = new List<string>();
            foreach (string k in MeasuredKeys) if (!staticKeys.Contains(k)) notInContract.Add(k);
            Assert.IsEmpty(notMeasured, "r2-gates.json Z3 bu testin ölçmediği statik anahtar istiyor (testi genişlet): " + string.Join(", ", notMeasured));
            Assert.IsEmpty(notInContract, "test sözleşmede olmayan anahtar yazardı (r2_z3.load_static 'unknown' der): " + string.Join(", ", notInContract));
            Debug.Log("[DD-OBS] r2.z3.static.contract " + contractPath + " staticKeys=" + string.Join(",", staticKeys));

            var set = AssetDatabase.LoadAssetAtPath<CharacterAssetSet>(ArtCharacterPrefabBuilder.CharacterAssets);
            Assert.IsNotNull(set, "kaynak yok: " + ArtCharacterPrefabBuilder.CharacterAssets);
            ZombieModuleLibrary lib = set.ZombieLibrary;
            Assert.IsNotNull(lib, "kaynak yok: DD_Characters zombi modül kütüphanesine bağlı değil");
            ZombieArmorLibrary armor = set.ZombieArmor;
            Assert.IsTrue(armor != null && armor.IsReady, "kaynak yok: zırh kütüphanesi (Z3 karesi -ddArmor all ile koşar)");
            var tierLib = AssetDatabase.LoadAssetAtPath<ZombieTierLibrary>(ArtTierBuilder.LibraryPath);
            Assert.IsTrue(tierLib != null && tierLib.IsReady, "kaynak yok: " + ArtTierBuilder.LibraryPath + " (sürüm tint'i ve tier parçaları)");
            List<string> fits = Families(lib);
            Assert.Greater(fits.Count, 0, "kaynak yok: kütüphanede gövde ailesi (fit) yok");
            Debug.Log("[DD-OBS] r2.z3.static.families n=" + fits.Count + " fits=" + string.Join(",", fits)
                      + " | modules " + AssetDatabase.GetAssetPath(lib) + " " + lib.version + " sha=" + Short(lib.sourceSha256)
                      + " | armour " + AssetDatabase.GetAssetPath(armor) + " " + armor.version + " sha=" + Short(armor.sourceSha256)
                      + " | tier " + AssetDatabase.GetAssetPath(tierLib) + " " + tierLib.version + " sha=" + Short(tierLib.sourceSha256)
                      + " | prefab " + (set.ZombiePrefab != null ? AssetDatabase.GetAssetPath(set.ZombiePrefab) : "-"));

            var problems = new List<string>();
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            values[KeyBones] = MeasureBones(lib, armor, tierLib, fits, problems);
            var bound = new SortedSet<string>(StringComparer.Ordinal);
            MeasureMaterials(lib, armor, tierLib, fits, problems, bound, out int materialsMax, out int crowdTotal, out int atlasesMax, out int mapsMax, out int armorTierAtlases);
            values[KeyMaterials] = materialsMax;
            values[KeyCrowdTotal] = crowdTotal;
            values[KeyAtlases] = atlasesMax;
            values[KeyMapsPerAtlas] = mapsMax;
            values[KeyArmorTierAtlases] = armorTierAtlases;
            values[KeyTextureMaxPx] = MeasureTextureMaxPx(problems);
            values[KeyShippedMb] = MeasureShippedCharacterMb(problems, bound);

            // the source is in the recording: no artefact unless every value rests on assets that are there
            Assert.IsEmpty(problems, "kaynak yok — " + FileName + " yazılmadı (Z3 BLOCKED):\n" + string.Join("\n", problems));
            foreach (string k in staticKeys)
            {
                double v = values[k];
                Assert.IsFalse(double.IsNaN(v) || double.IsInfinity(v), k + " düz sayı değil");
                Assert.Greater(v, 0d, k + ": sayı / boyut / MB > 0 olmalı (r2_z3.load_static; -1 nöbetçi değeri yok)");
            }

            string written = Write(staticKeys, values);
            var summary = new StringBuilder();
            foreach (string k in staticKeys) summary.Append(' ').Append(k).Append('=').Append(values[k].ToString("R", CultureInfo.InvariantCulture));
            Debug.Log("[DD-OBS] r2.z3.static.values" + summary + " | " + (written != null
                ? "written " + written + " commit=" + (Environment.GetEnvironmentVariable(CommitVariable) ?? "")
                : "not written (" + MeasureDirVariable + " unset: ordinary CI, R2Olcum behaviour)"));

            // the contract limits, read from the same file (the artefact is re-judged by r2_z3.py; both use these numbers)
            var over = new List<string>();
            if (gate.checks != null)
            {
                foreach (ContractCheck c in gate.checks)
                {
                    if (c == null || !values.ContainsKey(c.key)) continue;
                    double v = values[c.key];
                    bool ok;
                    switch (c.op)
                    {
                        case "<=": ok = v <= c.limit; break;
                        case ">=": ok = v >= c.limit; break;
                        case "==": ok = Math.Abs(v - c.limit) < 1e-9; break;
                        default: ok = false; break;
                    }

                    if (!ok) over.Add(c.key + " = " + v.ToString("R", CultureInfo.InvariantCulture) + " (r2-gates.json Z3: " + c.op + " " + c.limit.ToString("R", CultureInfo.InvariantCulture) + ")");
                }
            }

            Assert.IsEmpty(over, "Z3 statik bütçe aşıldı:\n" + string.Join("\n", over));
        }

        // ------------------------------------------------------------------------------------------------------ contract

        private static ContractGate Z3Contract(out string path)
        {
            path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ContractRelativePath));
            Assert.IsTrue(File.Exists(path), "R2 sözleşmesi yok: " + path + " (test depo kökündeki " + ContractRelativePath + " dosyasını okur)");
            ContractFile file = JsonUtility.FromJson<ContractFile>(File.ReadAllText(path, Encoding.UTF8));
            Assert.IsTrue(file != null && file.gates != null, path + ": gates okunamadı");
            foreach (ContractGate g in file.gates)
            {
                if (g != null && g.id == GateId) return g;
            }

            Assert.Fail(path + ": " + GateId + " kapısı yok");
            return null;
        }

        /// <summary>r2_z3.z3_keys: checks then report, minus the ratio keys (which must be there).</summary>
        private static List<string> StaticKeys(ContractGate gate)
        {
            var keys = new List<string>();
            if (gate.checks != null)
            {
                foreach (ContractCheck c in gate.checks) if (c != null && !string.IsNullOrEmpty(c.key)) keys.Add(c.key);
            }

            if (gate.report != null)
            {
                foreach (string r in gate.report) if (!string.IsNullOrEmpty(r)) keys.Add(r);
            }

            var missingRatios = new List<string>();
            foreach (string k in RatioKeys) if (!keys.Contains(k)) missingRatios.Add(k);
            Assert.IsEmpty(missingRatios, "r2-gates.json Z3 oran anahtarları eksik (r2_z3.z3_keys ContractError): " + string.Join(", ", missingRatios));
            keys.RemoveAll(k => Array.IndexOf(RatioKeys, k) >= 0);
            Assert.Greater(keys.Count, 0, "r2-gates.json Z3: statik anahtar yok");
            return keys;
        }

        // ------------------------------------------------------------------------------------------------------ families

        private static List<string> Families(ZombieModuleLibrary lib)
        {
            var fits = new List<string>();
            if (lib.bodies == null) return fits;
            foreach (ZombieModuleLibrary.Body b in lib.bodies)
            {
                if (b != null && !string.IsNullOrEmpty(b.fit) && !fits.Contains(b.fit)) fits.Add(b.fit);
            }

            return fits;
        }

        /// <summary>Every skinned mesh a family can bind, labelled "kind:id/…/LODn" (kind = body | module kind | armour | tier).</summary>
        private static List<KeyValuePair<string, ZombieModuleLibrary.MeshRef>> FamilyMeshes(ZombieModuleLibrary lib, ZombieArmorLibrary armor, ZombieTierLibrary tierLib, string fit)
        {
            var list = new List<KeyValuePair<string, ZombieModuleLibrary.MeshRef>>();
            ZombieModuleLibrary.Body body = lib.FindBody(fit);
            if (body != null && body.lods != null)
            {
                for (int lod = 0; lod < body.lods.Length; lod++) list.Add(new KeyValuePair<string, ZombieModuleLibrary.MeshRef>("body:" + fit + "/LOD" + lod, body.lods[lod]));
            }

            if (lib.modules != null)
            {
                foreach (ZombieModuleLibrary.Module m in lib.modules)
                {
                    if (m == null || m.variants == null) continue;
                    foreach (ZombieModuleLibrary.Variant v in m.variants)
                    {
                        if (v == null || v.fit != fit || v.lods == null) continue;
                        for (int lod = 0; lod < v.lods.Length; lod++)
                            list.Add(new KeyValuePair<string, ZombieModuleLibrary.MeshRef>(m.kind + ":" + m.id + "/" + v.colourway + "/LOD" + lod, v.lods[lod]));
                    }
                }
            }

            if (armor != null && armor.variants != null)
            {
                foreach (ZombieArmorLibrary.Variant av in armor.variants)
                {
                    ZombieArmorLibrary.Fit af = av != null ? av.FindFit(fit) : null;
                    if (af == null) continue;
                    AddState(list, "armour:" + av.id + "/" + ZombieArmorLibrary.StatePresent, af.present);
                    AddState(list, "armour:" + av.id + "/" + ZombieArmorLibrary.StateDamaged, af.damaged);
                    AddState(list, "armour:" + av.id + "/" + ZombieArmorLibrary.StateBroken, af.broken);
                }
            }

            if (tierLib != null && tierLib.parts != null)
            {
                foreach (ZombieTierLibrary.Part part in tierLib.parts)
                {
                    ZombieTierLibrary.Fit pf = PartFit(part, fit);
                    if (pf == null || pf.lods == null) continue;
                    for (int lod = 0; lod < pf.lods.Length; lod++)
                        list.Add(new KeyValuePair<string, ZombieModuleLibrary.MeshRef>("tier:" + part.id + "/LOD" + lod, pf.lods[lod]));
                }
            }

            return list;
        }

        private static void AddState(List<KeyValuePair<string, ZombieModuleLibrary.MeshRef>> list, string label, ZombieArmorLibrary.StateMeshes s)
        {
            if (s == null) return;
            for (int lod = 0; lod < ZombieArmorLibrary.LodCount; lod++)
            {
                list.Add(new KeyValuePair<string, ZombieModuleLibrary.MeshRef>(label + "/main/LOD" + lod, s.main != null && lod < s.main.Length ? s.main[lod] : null));
                list.Add(new KeyValuePair<string, ZombieModuleLibrary.MeshRef>(label + "/sub/LOD" + lod, s.sub != null && lod < s.sub.Length ? s.sub[lod] : null));
            }
        }

        /// <summary>The part's meshes for exactly this fit (TierSilhouetteSlot.FitOf falls back to the first fit; a family is measured on its own).</summary>
        private static ZombieTierLibrary.Fit PartFit(ZombieTierLibrary.Part part, string fit)
        {
            if (part == null || part.fits == null) return null;
            foreach (ZombieTierLibrary.Fit f in part.fits)
            {
                if (f != null && f.fit == fit) return f;
            }

            return null;
        }

        private static bool HasMesh(ZombieModuleLibrary.MeshRef r) => r != null && r.mesh != null;

        // --------------------------------------------------------------------------------------------------------- bones

        private static double MeasureBones(ZombieModuleLibrary lib, ZombieArmorLibrary armor, ZombieTierLibrary tierLib, List<string> fits, List<string> problems)
        {
            var imported = new Dictionary<Mesh, SkinnedMeshRenderer>();
            var loadedModels = new HashSet<string>(StringComparer.Ordinal);
            int max = 0;
            string maxAt = null;
            foreach (string fit in fits)
            {
                int familyMax = 0, counted = 0, empty = 0, anchors = 0, emptySlots = 0, fbxChecked = 0;
                string familyMaxAt = null;
                var kindMax = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var kindCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, ZombieModuleLibrary.MeshRef> e in FamilyMeshes(lib, armor, tierLib, fit))
                {
                    ZombieModuleLibrary.MeshRef r = e.Value;
                    if (!HasMesh(r))
                    {
                        emptySlots++;   // an armour sub-mesh a helmet / remnant leaves empty (ZombieArmorLibrary.PartSub)
                        continue;
                    }

                    if (r.boneNames == null || r.boneNames.Length == 0)
                    {
                        problems.Add("aile " + fit + " " + e.Key + ": kemik listesi boş (" + AssetDatabase.GetAssetPath(r.mesh) + ")");
                        continue;
                    }

                    int n = CountBones(r.boneNames, ref anchors, ref empty);
                    SkinnedMeshRenderer smr = ImportedRenderer(r.mesh, imported, loadedModels);
                    if (smr == null)
                    {
                        problems.Add("aile " + fit + " " + e.Key + ": içe aktarılmış SkinnedMeshRenderer yok (" + AssetDatabase.GetAssetPath(r.mesh) + " / " + r.mesh.name + ")");
                    }
                    else
                    {
                        int nf = CountBones(smr.bones);
                        fbxChecked++;
                        if (nf != n) problems.Add("aile " + fit + " " + e.Key + ": kütüphane " + n + " kemik, içe aktarılmış renderer " + nf + " (" + AssetDatabase.GetAssetPath(r.mesh) + "; kütüphane eski — Run Characters)");
                    }

                    counted++;
                    int colon = e.Key.IndexOf(':');
                    string kind = colon > 0 ? e.Key.Substring(0, colon) : e.Key;
                    kindMax[kind] = Math.Max(kindMax.TryGetValue(kind, out int km) ? km : 0, n);
                    kindCount[kind] = (kindCount.TryGetValue(kind, out int kc) ? kc : 0) + 1;
                    if (n > familyMax)
                    {
                        familyMax = n;
                        familyMaxAt = e.Key + " (" + AssetDatabase.GetAssetPath(r.mesh) + ")";
                    }
                }

                if (!kindCount.ContainsKey("body")) problems.Add("aile " + fit + ": gövde mesh'i yok");
                if (counted == 0) problems.Add("aile " + fit + ": sayılan skinned mesh yok");
                var kinds = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in kindMax) kinds.Append(' ').Append(kv.Key).Append('=').Append(kv.Value).Append('/').Append(kindCount[kv.Key]).Append("mesh");
                Debug.Log("[DD-OBS] r2.z3.static.bones fit=" + fit + " max=" + familyMax + " at " + familyMaxAt + " | meshes=" + counted + " fbxRenderers=" + fbxChecked
                          + " emptySlots=" + emptySlots + " anchorsExcluded=" + anchors + " emptyEntriesExcluded=" + empty + " | per kind (max/meshes):" + kinds);
                if (familyMax > max)
                {
                    max = familyMax;
                    maxAt = fit + " " + familyMaxAt;
                }
            }

            Debug.Log("[DD-OBS] r2.z3.static.bones bonesMax=" + max + " at " + maxAt);
            return max;
        }

        /// <summary>Bound bones: non-empty names other than the three armour anchors.</summary>
        private static int CountBones(string[] names, ref int anchors, ref int empty)
        {
            int n = 0;
            foreach (string b in names)
            {
                if (string.IsNullOrEmpty(b)) { empty++; continue; }
                if (Array.IndexOf(AnchorBones, b) >= 0) { anchors++; continue; }
                n++;
            }

            return n;
        }

        private static int CountBones(Transform[] bones)
        {
            int n = 0;
            if (bones == null) return 0;
            foreach (Transform t in bones)
            {
                if (t != null && Array.IndexOf(AnchorBones, t.name) < 0) n++;
            }

            return n;
        }

        /// <summary>The renderer of <paramref name="mesh"/> inside its imported FBX (searched by mesh, not by object name).</summary>
        private static SkinnedMeshRenderer ImportedRenderer(Mesh mesh, Dictionary<Mesh, SkinnedMeshRenderer> cache, HashSet<string> loaded)
        {
            if (cache.TryGetValue(mesh, out SkinnedMeshRenderer hit)) return hit;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path) || !loaded.Add(path)) return null;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return null;
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null && !cache.ContainsKey(smr.sharedMesh)) cache.Add(smr.sharedMesh, smr);
            }

            return cache.TryGetValue(mesh, out hit) ? hit : null;
        }

        // ------------------------------------------------------------------------------------------ materials and atlases

        private static void MeasureMaterials(ZombieModuleLibrary lib, ZombieArmorLibrary armor, ZombieTierLibrary tierLib, List<string> fits, List<string> problems,
            SortedSet<string> bound, out int materialsMax, out int crowdTotalMax, out int atlasesMax, out int mapsMax, out int armorTierAtlases)
        {
            materialsMax = 0;
            crowdTotalMax = 0;
            atlasesMax = 0;
            mapsMax = 0;
            string materialsMaxAt = null;
            var everyInstanceMaterial = new List<Material>();
            var armorTier = new List<Material>();   // the armour tint + tier-part materials: shared by every family
            var partBuffer = new ZombieTierLibrary.Part[ZombieTierLibrary.MaxPartsPerTier];
            foreach (string fit in fits)
            {
                var crowd = new List<Material>();      // skin + cloth over the four versions (the report key)
                var crowdAll = new List<Material>();   // + armour + tier parts (diagnostic)
                var line = new StringBuilder();
                foreach (EnemyTier tier in Tiers)
                {
                    int t = (int)tier;
                    string at = fit + "/" + tier;
                    // ZombieAppearance.ApplyVariation → ApplyMaterial(library, tier) → ApplyTierTint: the version's body tint stands in for the atlas
                    Material atlasOverride = tierLib.BodyMaterial(tier);
                    var skinCloth = new List<Material>();
                    AddAll(skinCloth, lib.MaterialsFor(AtlasOnly, t, atlasOverride), at + " body", problems);   // body slot: no module list = atlas only
                    int moduleMeshes = 0;
                    if (lib.modules != null)
                    {
                        foreach (ZombieModuleLibrary.Module m in lib.modules)
                        {
                            if (m == null || m.variants == null) continue;
                            foreach (ZombieModuleLibrary.Variant v in m.variants)
                            {
                                if (v == null || v.fit != fit || v.lods == null) continue;
                                foreach (ZombieModuleLibrary.MeshRef r in v.lods)
                                {
                                    if (!HasMesh(r)) continue;
                                    // ZombieSkinnedVisual.ApplyMaterial: the per-LOD list when it is non-empty (WP-038C / WP-088), else the module's
                                    string[] names = r.materials != null && r.materials.Length > 0 ? r.materials : m.materials;
                                    Material[] mats = names != null && names.Length > 1
                                        ? lib.MaterialsFor(names, t, atlasOverride)
                                        : lib.MaterialsFor(names != null && names.Length == 1 && names[0] == ZombieModuleLibrary.MaterialCloth ? ClothOnly : AtlasOnly, t, atlasOverride);
                                    AddAll(skinCloth, mats, at + " " + m.id, problems);
                                    moduleMeshes++;
                                }
                            }
                        }
                    }

                    if (moduleMeshes == 0) problems.Add("aile " + at + ": giysi / yüz modülü yok");
                    var instance = new List<Material>(skinCloth);
                    // -ddArmor all: ArmorVisualBinder.ApplyMaterial(tier), then ApplyTierTint puts the armour tint on every armour renderer
                    if (FitHasArmour(armor, fit))
                    {
                        Material tint = tierLib.ArmorMaterial(tier);
                        AddOne(instance, tint != null ? tint : armor.MaterialFor(t), at + " armour", problems);
                        AddOne(armorTier, tint != null ? tint : armor.MaterialFor(t), at + " armour", problems);
                    }
                    else
                    {
                        problems.Add("aile " + fit + ": zırh mesh'i yok (Z3 karesi -ddArmor all)");
                    }

                    // TierSilhouetteSlot.ApplySkinned: the version's parts, each on PartMaterial(tier, materialIndex)
                    int parts = tierLib.PartsOf(tier, partBuffer);
                    var partIds = new List<string>();
                    for (int p = 0; p < parts; p++)
                    {
                        ZombieTierLibrary.Fit pf = PartFit(partBuffer[p], fit);
                        if (pf == null || pf.lods == null || pf.lods.Length == 0 || !HasMesh(pf.lods[0])) continue;
                        AddOne(instance, tierLib.PartMaterial(tier, partBuffer[p].materialIndex), at + " " + partBuffer[p].id, problems);
                        AddOne(armorTier, tierLib.PartMaterial(tier, partBuffer[p].materialIndex), at + " " + partBuffer[p].id, problems);
                        partIds.Add(partBuffer[p].id);
                    }

                    if (instance.Count == 0) problems.Add("aile " + at + ": örnekte materyal yok");
                    if (instance.Count > materialsMax)
                    {
                        materialsMax = instance.Count;
                        materialsMaxAt = at;
                    }

                    AddAll(crowd, skinCloth, at, problems);
                    AddAll(crowdAll, instance, at, problems);
                    AddAll(everyInstanceMaterial, instance, at, problems);
                    line.Append(" | ").Append(tier).Append(' ').Append(instance.Count).Append(" [").Append(Names(instance)).Append("] parts=").Append(partIds.Count > 0 ? string.Join("+", partIds) : "-");
                }

                crowdTotalMax = Math.Max(crowdTotalMax, crowd.Count);
                var outside = new SortedSet<string>(StringComparer.Ordinal);
                SortedDictionary<string, SortedSet<string>> familyAtlases = PbrAtlases(crowd, outside, bound);
                if (familyAtlases.Count == 0) problems.Add("aile " + fit + ": deri / kumaş materyallerinde PBR atlası yok");
                atlasesMax = Math.Max(atlasesMax, familyAtlases.Count);
                Debug.Log("[DD-OBS] r2.z3.static.materials fit=" + fit + " perInstance:" + line
                          + " || crowd skin+cloth=" + crowd.Count + " [" + Names(crowd) + "] crowd all=" + crowdAll.Count
                          + " || atlases=" + familyAtlases.Count + " [" + string.Join(", ", familyAtlases.Keys) + "]");
            }

            var notAtlas = new SortedSet<string>(StringComparer.Ordinal);
            SortedDictionary<string, SortedSet<string>> atlases = PbrAtlases(everyInstanceMaterial, notAtlas, bound);
            if (atlases.Count == 0) problems.Add("zombi örneklerinin materyallerinde PBR atlas haritası yok");
            var maps = new StringBuilder();
            foreach (KeyValuePair<string, SortedSet<string>> kv in atlases)
            {
                mapsMax = Math.Max(mapsMax, kv.Value.Count);
                maps.Append(" | ").Append(kv.Key).Append(' ').Append(kv.Value.Count).Append(':');
                foreach (string p in kv.Value) maps.Append(' ').Append(Path.GetFileName(p));
            }

            // C-00 06.10 13:30: the armour and tier-part atlases are shared by every family — one report line, not the family line
            SortedDictionary<string, SortedSet<string>> shared = PbrAtlases(armorTier, new SortedSet<string>(StringComparer.Ordinal), bound);
            armorTierAtlases = shared.Count;
            if (armorTierAtlases == 0) problems.Add("zırh / tier parçası materyallerinde PBR atlası yok");
            Debug.Log("[DD-OBS] r2.z3.static.materials materialsPerFamilyMax=" + materialsMax + " at " + materialsMaxAt + " crowdTotalMax=" + crowdTotalMax + " atlasesPerFamilyMax=" + atlasesMax
                      + " armorTierAtlasesTotal=" + armorTierAtlases + " [" + string.Join(", ", shared.Keys) + "] from " + armorTier.Count + " materials [" + Names(armorTier) + "]");
            Debug.Log("[DD-OBS] r2.z3.static.atlases mapsPerAtlasMax=" + mapsMax + maps + " || not atlas maps: " + string.Join(", ", notAtlas));
        }

        private static bool FitHasArmour(ZombieArmorLibrary armor, string fit)
        {
            if (armor == null || armor.variants == null) return false;
            foreach (ZombieArmorLibrary.Variant v in armor.variants)
            {
                ZombieArmorLibrary.Fit f = v != null ? v.FindFit(fit) : null;
                if (f != null && f.present != null && f.present.main != null && f.present.main.Length > 0 && HasMesh(f.present.main[0])) return true;
            }

            return false;
        }

        private static void AddAll(List<Material> into, IList<Material> mats, string at, List<string> problems)
        {
            if (mats == null || mats.Count == 0)
            {
                problems.Add(at + ": materyal dizisi boş");
                return;
            }

            foreach (Material m in mats) AddOne(into, m, at, problems);
        }

        private static void AddOne(List<Material> into, Material m, string at, List<string> problems)
        {
            if (m == null)
            {
                problems.Add(at + ": null materyal");
                return;
            }

            if (!into.Contains(m)) into.Add(m);
        }

        private static string Names(List<Material> mats)
        {
            var n = new List<string>(mats.Count);
            foreach (Material m in mats) n.Add(m != null ? m.name : "null");
            return string.Join(", ", n);
        }

        private static bool IsPbrMap(ArtImportContract.TextureRole role)
        {
            return role == ArtImportContract.TextureRole.BaseColor || role == ArtImportContract.TextureRole.Normal || role == ArtImportContract.TextureRole.Orm
                   || role == ArtImportContract.TextureRole.MetallicSmoothness || role == ArtImportContract.TextureRole.Packed;
        }

        /// <summary>
        /// Texture set (role slug) → the distinct PBR map assets the materials bind. Only properties the material's shader
        /// declares are read (a saved property of an earlier shader is never sampled). Everything else bound is added to
        /// <paramref name="outside"/>: the mask array, ID / tier masks, detail masks, sub-textures stored inside an asset. Every contract
        /// map bound (atlas map or not) is added to <paramref name="bound"/>.
        /// </summary>
        private static SortedDictionary<string, SortedSet<string>> PbrAtlases(List<Material> materials, SortedSet<string> outside, SortedSet<string> bound)
        {
            var atlases = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var props = new List<string>();
            foreach (Material m in materials)
            {
                if (m == null || m.shader == null) continue;
                props.Clear();
                m.GetTexturePropertyNames(props);
                foreach (string prop in props)
                {
                    if (m.shader.FindPropertyIndex(prop) < 0) continue;
                    Texture tex = m.GetTexture(prop);
                    if (tex == null) continue;
                    string path = AssetDatabase.GetAssetPath(tex);
                    bool main = !string.IsNullOrEmpty(path) && AssetDatabase.IsMainAsset(tex);
                    ArtImportContract.TextureRole role = main ? ArtImportContract.RoleOf(path) : ArtImportContract.TextureRole.Unknown;
                    if (main && role != ArtImportContract.TextureRole.Unknown) bound.Add(path);   // every contract map the measured materials bind
                    string slug = main ? ArtImportContract.RoleSlugOf(path) : null;
                    if (slug == null || !IsPbrMap(role))
                    {
                        outside.Add(m.name + "." + prop + "=" + (main ? Path.GetFileName(path) : tex.name + "@" + Path.GetFileName(path)) + "(" + role + ")");
                        continue;
                    }

                    if (!atlases.TryGetValue(slug, out SortedSet<string> maps))
                    {
                        maps = new SortedSet<string>(StringComparer.Ordinal);
                        atlases[slug] = maps;
                    }

                    maps.Add(path);
                }
            }

            return atlases;
        }

        // ---------------------------------------------------------------------------------------------------- textures

        /// <summary>Every PNG under Assets/_Art the texture policy test scans (ArtAssetGateTests.DiskteUzanti(".png")).</summary>
        private static List<string> ArtPngs()
        {
            var list = new List<string>();
            foreach (string f in Directory.GetFiles(ArtImportContract.ArtRoot, "*.png", SearchOption.AllDirectories))
            {
                string p = f.Replace('\\', '/');
                if (ArtImportContract.IsLutTexture(p) || ArtImportContract.IsMenuPlateTexture(p) || ArtImportContract.IsLightCookieTexture(p)) continue;
                list.Add(p);
            }

            list.Sort(string.CompareOrdinal);
            return list;
        }

        private static bool IsCharacterTexture(string path)
        {
            if (ArtVfxContract.IsVfxTexture(path)) return false;   // WP-019: the VFX class has its own line
            ArtImportContract.TextureClass? c = ArtImportContract.ClassifiedClassOf(path);
            return c.HasValue && ArtImportContract.IsCharacterClass(c.Value);
        }

        private static double MeasureTextureMaxPx(List<string> problems)
        {
            int max = 0, n = 0;
            string maxAt = null;
            var rows = new List<string>();
            foreach (string path in ArtPngs())
            {
                if (!IsCharacterTexture(path)) continue;
                var tex = AssetDatabase.LoadMainAssetAtPath(path) as Texture;
                if (tex == null)
                {
                    problems.Add(path + ": içe aktarılmış doku yüklenemedi");
                    continue;
                }

                int px = Math.Max(tex.width, tex.height);   // a Texture2DArray reports one slice (AP-025 mask array: 1024)
                string source = PngSize(path, out int w, out int h) ? w + "x" + h : "?";
                rows.Add(Path.GetFileName(path) + " " + tex.width + "x" + tex.height + (tex is Texture2DArray array ? "x" + array.depth : "")
                         + " (source " + source + ", cap " + ArtImportContract.MaxTextureSizeOf(path) + ")");
                n++;
                if (px > max)
                {
                    max = px;
                    maxAt = path;
                }
            }

            if (n == 0) problems.Add("Character sınıfında içe aktarılmış doku yok (" + ArtImportContract.ArtRoot + ")");
            Debug.Log("[DD-OBS] r2.z3.static.textures characterTextureMaxPx=" + max + " at " + maxAt + " | Character class " + n + ": " + string.Join(" | ", rows));
            return max;
        }

        /// <summary>
        /// The shipped character row of ArtAssetGateTests.DokuPolitikasi_HerDokuSinifli_BoyutVeStreamingSinifa_SevkToplamiButceAltinda
        /// (ADR-015-1 v3 / DEC-093, ArtImportContract.ShippedCharacterBudgetMb): PNG-header size at the class / role / path cap,
        /// full mip chain (the mask array as its BC4 slices), every Character-class map ArtImportContract.IsShippedMap keeps — the
        /// same rule and the same estimate as the gate test. A Character-class map the measured materials bind (<paramref name="bound"/>)
        /// that the row does not count is a broken source: the MB would be short.
        /// </summary>
        private static double MeasureShippedCharacterMb(List<string> problems, SortedSet<string> bound)
        {
            long bytes = 0;
            int count = 0;
            var rows = new List<string>();
            var skipped = new List<string>();
            var counted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in ArtPngs())
            {
                if (!IsCharacterTexture(path)) continue;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    problems.Add(path + ": TextureImporter yok");
                    continue;
                }

                if (!ArtImportContract.IsShippedMap(path))
                {
                    skipped.Add(Path.GetFileName(path));
                    continue;
                }

                if (!PngSize(path, out int w, out int h))
                {
                    problems.Add(path + ": PNG başlığı okunamadı (LFS pointer?)");
                    continue;
                }

                long b = ArtImportContract.EstimatedShippedBytes(path, w, h, importer.mipmapEnabled);
                bytes += b;
                count++;
                counted.Add(path);
                bool array = ArtImportContract.RoleOf(path) == ArtImportContract.TextureRole.Array;
                rows.Add(Path.GetFileName(path) + " " + w + "x" + h + (array ? " array " + ArtImportContract.MaskArrayColumns * ArtImportContract.MaskArrayRows + " BC4 slices" : "@" + ArtImportContract.MaxTextureSizeOf(path)) + (importer.mipmapEnabled ? " mip " : " ")
                         + (b / 1048576.0).ToString("F2", CultureInfo.InvariantCulture) + "MB");
            }

            if (count == 0) problems.Add("sevk edilen Character sınıfı doku yok");
            foreach (string path in bound)
            {
                if (IsCharacterTexture(path) && !counted.Contains(path)) problems.Add(path + ": ölçülen bir materyal bağlıyor ama sevk satırı saymıyor (ArtImportContract.IsShippedMap)");
            }

            double mb = bytes / 1048576.0;
            Debug.Log("[DD-OBS] r2.z3.static.shipped shippedCharacterMB=" + mb.ToString("F2", CultureInfo.InvariantCulture) + " / " + ArtImportContract.ShippedCharacterBudgetMb
                      + " maps=" + count + " (ArtAssetGateTests character row) | " + string.Join(" | ", rows) + " || not shipped (provenance): " + string.Join(", ", skipped));
            return Math.Round(mb, 2);
        }

        private static bool PngSize(string path, out int w, out int h)
        {
            w = h = 0;
            var head = new byte[24];
            using (FileStream f = File.OpenRead(path))
            {
                if (f.Read(head, 0, 24) != 24) return false;
            }

            if (head[1] != (byte)'P' || head[2] != (byte)'N' || head[3] != (byte)'G') return false;
            w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return w > 0 && h > 0;
        }

        // ------------------------------------------------------------------------------------------------------- writer

        /// <summary>
        /// &lt;DD_R2_MEASURE_DIR&gt;/z3-static.json, or nothing (null) without the variable — R2Olcum's rule. Values in contract order,
        /// plain numbers (R format, invariant culture), commit from DD_R2_COMMIT (the FINAL SHA the runner sets).
        /// </summary>
        private static string Write(List<string> keys, Dictionary<string, double> values)
        {
            string dir = Environment.GetEnvironmentVariable(MeasureDirVariable);
            if (string.IsNullOrEmpty(dir)) return null;
            string commit = Environment.GetEnvironmentVariable(CommitVariable) ?? "";
            var sb = new StringBuilder();
            sb.Append("{\"schema\": \"").Append(Schema).Append("\", \"commit\": \"").Append(Escape(commit))
              .Append("\", \"source\": \"").Append(TestName).Append("\", \"values\": {");
            for (int i = 0; i < keys.Count; i++)
            {
                double v = values[keys[i]];
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException(GateId + "." + keys[i] + " is not a plain number");
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(keys[i]).Append("\": ").Append(v.ToString("R", CultureInfo.InvariantCulture));
            }

            sb.Append("}}");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, FileName);
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string Short(string sha) => string.IsNullOrEmpty(sha) ? "-" : sha.Length > 12 ? sha.Substring(0, 12) : sha;
    }
}
