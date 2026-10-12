using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-108 · The AP-040 v001 world kit (P-05; catalog groups plaza / garden / hf: 47 families, 110 FBX, staged by
    /// wp-108/ap040-intake/stage-ap040.py) as the Unity side needs it beyond AP040_Library.json:
    ///
    ///   groups       which families are plaza pieces (the S-05 §6 / Y5 collider rules apply), garden or HF, and which are flora
    ///                (vc1 wind layer: no UV2, no collider, DD/Foliage Wind roles);
    ///   shadows      AP040_shadow_cast per variation — an FBX custom property the generator reads but the library JSON does not
    ///                carry (stage-ap040.py keeps it in its plan only), so the table below is that plan column verbatim;
    ///   names        SM_&lt;family&gt;_VNN_LOD0|1|2, [AP040_]&lt;family&gt;_VNN_proxyNN_COL, SOCKET_&lt;name&gt;[.NNN] (a Blender
    ///                duplicate suffix: toro_post V02 ships SOCKET_Flame.001 / SOCKET_Light.001);
    ///   rules        S-05 §6 no plaza collider top in (0.30, 0.50) m; S-05 Y5 a plaza collider top in 1.21–1.39 m is lowered to
    ///                1.20 m (barricade_cart 1.225, hokora 1.220 — the bullet passes over a shoot-over obstacle at 1.39 m);
    ///   textures     the four shared maps S-02 imports (WP-107B) and the fourteen T_ap040_* maps the staging copies.
    ///
    /// The tables come from wp-108/ap040-intake/derived/stage-ap040-plan.json (v001). A family the tables do not know stops the
    /// build (<see cref="CheckNames"/> / <see cref="CastsShadow"/>): a new delivery updates them first.
    /// </summary>
    public static class ArtAp040WorldKit
    {
        public const string Package = "AP-040";
        private const string LogPrefix = "[ArtPipeline] ";

        public const int ExpectedFamilies = 47;
        public const int ExpectedVariations = 110;
        public const int ExpectedSockets = 18;
        public const int ExpectedColliderProxies = 34;
        public const int ExpectedStagedTextures = 14;

        /// <summary>Catalog group plaza (18 families, 42 FBX).</summary>
        public static readonly string[] PlazaFamilies =
        {
            "apocalypse_set", "barricade_cart", "daihachi_cart", "debris_small", "hokora", "ido", "kibako_stack", "nobori_pole", "pottery_cluster",
            "produce_stall", "sakadaru_stack", "stone_lantern", "taiko_stand", "tea_stall", "toro_post", "water_trough", "yatai", "yatai_broken"
        };

        /// <summary>Catalog group garden (25 families, 64 FBX).</summary>
        public static readonly string[] GardenFamilies =
        {
            "autumn_dry_shrub", "bamboo_fence", "camellia_shrub", "clover_white", "dry_grass", "fallen_log_root", "fern", "garden_tools", "ginkgo_sapling",
            "hagi", "higanbana", "ishidoro_small", "jizo", "kakine_broken", "leaf_litter", "maki_stack", "momiji_sapling", "oke_set", "path_marker",
            "stone_bridge_steps", "stone_cluster_lichen", "susuki_dry", "tsukubai", "wisteria_arbor", "yamabuki"
        };

        /// <summary>Catalog group hf (4 families, 4 FBX).</summary>
        public static readonly string[] HfFamilies = { "apocalypse_hf", "jizo_hf", "momiji_hf", "produce_basket_hf" };

        /// <summary>The vc1 carriers (13 garden flora + the HF momiji): roles ap040_flora / ap040_momiji_hf, no collider, no UV2.</summary>
        public static readonly string[] FloraFamilies =
        {
            "autumn_dry_shrub", "camellia_shrub", "clover_white", "dry_grass", "fern", "ginkgo_sapling", "hagi", "higanbana", "leaf_litter",
            "momiji_hf", "momiji_sapling", "susuki_dry", "wisteria_arbor", "yamabuki"
        };

        /// <summary>
        /// AP040_shadow_cast per variation (character i = V0(i+1)); the FBX carries the same value on all three LODs. 45 of 110 cast:
        /// small tufts, litter and low dressing do not (integration v001 "Small tufts/litter off; large shrubs/arbors per scene budget").
        /// </summary>
        private static readonly Dictionary<string, string> ShadowCastTable = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "apocalypse_hf", "1" },
            { "apocalypse_set", "11" },
            { "autumn_dry_shrub", "000" },
            { "bamboo_fence", "00" },
            { "barricade_cart", "11" },
            { "camellia_shrub", "000" },
            { "clover_white", "000" },
            { "daihachi_cart", "11" },
            { "debris_small", "000000" },
            { "dry_grass", "000" },
            { "fallen_log_root", "00" },
            { "fern", "000" },
            { "garden_tools", "00" },
            { "ginkgo_sapling", "111" },
            { "hagi", "000" },
            { "higanbana", "000" },
            { "hokora", "11" },
            { "ido", "11" },
            { "ishidoro_small", "00" },
            { "jizo", "00" },
            { "jizo_hf", "0" },
            { "kakine_broken", "00" },
            { "kibako_stack", "111" },
            { "leaf_litter", "000" },
            { "maki_stack", "00" },
            { "momiji_hf", "1" },
            { "momiji_sapling", "011" },
            { "nobori_pole", "111" },
            { "oke_set", "00" },
            { "path_marker", "11" },
            { "pottery_cluster", "00" },
            { "produce_basket_hf", "0" },
            { "produce_stall", "11" },
            { "sakadaru_stack", "111" },
            { "stone_bridge_steps", "00" },
            { "stone_cluster_lichen", "000" },
            { "stone_lantern", "11" },
            { "susuki_dry", "001" },
            { "taiko_stand", "1" },
            { "tea_stall", "11" },
            { "toro_post", "11" },
            { "tsukubai", "00" },
            { "water_trough", "00" },
            { "wisteria_arbor", "111" },
            { "yamabuki", "000" },
            { "yatai", "11" },
            { "yatai_broken", "11" }
        };

        /// <summary>S-05 §6: a collider top is ≤ 0.30 m (stepped over) or ≥ 0.50 m (an obstacle); 5 mm tolerance (stage-ap040.py BAND_EPS).</summary>
        public const float ForbiddenBandLow = 0.30f, ForbiddenBandHigh = 0.50f, BandTolerance = 0.005f;

        /// <summary>S-05 Y5: a shoot-over obstacle's collider top stays ≤ 1.20 m (the bullet passes at 1.39 m); plaza tops in (1.20, 1.39] are lowered to it.</summary>
        public const float ShootOverTop = 1.20f, ShootOverBandHigh = 1.39f;

        private const float TopSlack = 0.0005f;   // float noise of a top written as 1.2 in the JSON

        private static readonly Regex SocketPattern = new Regex(@"^SOCKET_[A-Za-z0-9_]+(\.\d{3})?$", RegexOptions.CultureInvariant);

        public static bool IsPlaza(string family) => Array.IndexOf(PlazaFamilies, family) >= 0;
        public static bool IsGarden(string family) => Array.IndexOf(GardenFamilies, family) >= 0;
        public static bool IsHf(string family) => Array.IndexOf(HfFamilies, family) >= 0;
        public static bool IsFlora(string family) => Array.IndexOf(FloraFamilies, family) >= 0;
        public static bool IsKnownFamily(string family) => IsPlaza(family) || IsGarden(family) || IsHf(family);

        /// <summary>True when the table has a flag for this variation (<c>V01</c> … in order).</summary>
        public static bool HasShadowCastEntry(string family, string variation)
        {
            if (!ShadowCastTable.TryGetValue(family ?? string.Empty, out string flags)) return false;
            int index = VariationIndex(variation);
            return index >= 0 && index < flags.Length;
        }

        /// <summary>AP040_shadow_cast of one variation. Throws for a variation the v001 table does not know (update the table with the delivery).</summary>
        public static bool CastsShadow(string family, string variation)
        {
            if (!ShadowCastTable.TryGetValue(family ?? string.Empty, out string flags))
            {
                throw new InvalidOperationException(LogPrefix + "AP-040 family '" + family + "' has no AP040_shadow_cast row (ArtAp040WorldKit.ShadowCastTable, from stage-ap040-plan.json).");
            }

            int index = VariationIndex(variation);
            if (index < 0 || index >= flags.Length)
            {
                throw new InvalidOperationException(LogPrefix + "AP-040 " + family + " " + variation + " has no AP040_shadow_cast flag (row '" + flags + "').");
            }

            return flags[index] == '1';
        }

        /// <summary>Number of variations the shadow table lists for a family (0 when the family is not in it).</summary>
        public static int ShadowCastVariationCount(string family)
        {
            return ShadowCastTable.TryGetValue(family ?? string.Empty, out string flags) ? flags.Length : 0;
        }

        private static int VariationIndex(string variation)
        {
            if (variation == null || variation.Length != 3 || variation[0] != 'V' || !char.IsDigit(variation[1]) || !char.IsDigit(variation[2])) return -1;
            return (variation[1] - '0') * 10 + (variation[2] - '0') - 1;
        }

        public static bool InForbiddenBand(float top)
        {
            return top > ForbiddenBandLow + BandTolerance && top < ForbiddenBandHigh - BandTolerance;
        }

        /// <summary>A top above 1.20 m up to the band's upper edge (1.39 m + 5 mm): the Y5 band the plaza tops are lowered out of.</summary>
        public static bool InShootOverBand(float top)
        {
            return top > ShootOverTop + TopSlack && top <= ShootOverBandHigh + BandTolerance;
        }

        // ------------------------------------------------------------------ names

        public static string PrefabName(string family, string variation) => "SM_" + family + "_" + variation;

        public static string LodObject(string family, string variation, int lod) => "SM_" + family + "_" + variation + "_LOD" + lod;

        /// <summary><c>[AP040_]&lt;family&gt;_VNN_proxyNN_COL</c> (the integration file writes the short form, the FBX the prefixed one).</summary>
        public static bool IsProxyObject(string objectName, string family, string variation)
        {
            if (objectName == null) return false;
            string body = objectName.StartsWith("AP040_", StringComparison.Ordinal) ? objectName.Substring(6) : objectName;
            string head = family + "_" + variation + "_proxy";
            return body.StartsWith(head, StringComparison.Ordinal) && body.Length == head.Length + 6 && char.IsDigit(body[head.Length]) &&
                   char.IsDigit(body[head.Length + 1]) && body.EndsWith("_COL", StringComparison.Ordinal);
        }

        /// <summary><c>SOCKET_&lt;name&gt;</c> with an optional Blender duplicate suffix <c>.NNN</c>.</summary>
        public static bool IsSocketName(string name)
        {
            return name != null && SocketPattern.IsMatch(name);
        }

        /// <summary>
        /// Every way a variation can break the AP-040 naming the builder relies on (empty = sound): prefab name, three LODs named
        /// SM_&lt;family&gt;_VNN_LODn, proxy names, socket names, a known family with a shadow flag.
        /// </summary>
        public static List<string> NameProblems(ArtLibraryManifest.Family family, ArtLibraryManifest.Variation variation)
        {
            var problems = new List<string>();
            string f = family.family, v = variation.id;
            if (!IsKnownFamily(f)) problems.Add("family '" + f + "' is in no AP-040 v001 group (plaza / garden / hf)");
            if (!HasShadowCastEntry(f, v)) problems.Add("no AP040_shadow_cast flag");
            if (variation.prefab != PrefabName(f, v)) problems.Add("prefab '" + variation.prefab + "' ≠ " + PrefabName(f, v));
            if (variation.parts.Count != 1) problems.Add(variation.parts.Count + " parts (AP-040: one LOD chain)");
            foreach (ArtLibraryManifest.Part part in variation.parts)
            {
                if (part.lods.Count != 3) problems.Add("part '" + part.name + "': " + part.lods.Count + " LODs (AP-040: 3)");
                for (int lod = 0; lod < part.lods.Count; lod++)
                    if (part.lods[lod].@object != LodObject(f, v, lod)) problems.Add("LOD" + lod + " object '" + part.lods[lod].@object + "' ≠ " + LodObject(f, v, lod));
                if (part.wind != IsFlora(f)) problems.Add("wind " + part.wind + " but the flora table says " + IsFlora(f));
            }

            foreach (ArtLibraryManifest.Collider c in variation.colliders)
            {
                if (!IsProxyObject(c.@object, f, v)) problems.Add("collider '" + c.@object + "' is not [AP040_]" + f + "_" + v + "_proxyNN_COL");
                if (c.broken || !string.IsNullOrEmpty(c.part)) problems.Add("collider '" + c.@object + "' on a part / broken state (AP-040 has neither)");
            }

            if (variation.sockets != null)
                foreach (ArtLibraryManifest.Socket s in variation.sockets)
                    if (!IsSocketName(s.name)) problems.Add("socket '" + s.name + "' is not SOCKET_<name>[.NNN]");

            return problems;
        }

        /// <summary>Throws with every naming problem of a variation (the builder's first step for AP-040).</summary>
        public static void CheckNames(ArtLibraryManifest.Family family, ArtLibraryManifest.Variation variation)
        {
            List<string> problems = NameProblems(family, variation);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException("[ArtLibrary] AP-040 " + variation.prefab + ": " + string.Join("; ", problems));
            }
        }

        // ------------------------------------------------------------------ import

        /// <summary>The family folder of a model under Families/AP-040/&lt;family&gt;/, or null.</summary>
        public static string FamilyOfModel(string assetPath)
        {
            if (assetPath == null) return null;
            string p = assetPath.Replace('\\', '/');
            string root = ArtLibraryPaths.LibraryRoot(Package) + "/";
            if (!p.StartsWith(root, StringComparison.Ordinal)) return null;
            int slash = p.IndexOf('/', root.Length);
            return slash > root.Length ? p.Substring(root.Length, slash - root.Length) : null;
        }

        /// <summary>An AP-040 flora FBX (Import Contract: no lightmap UVs — probe-lit like the AP-031 kit).</summary>
        public static bool IsFloraModel(string assetPath)
        {
            return assetPath != null && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) && IsFlora(FamilyOfModel(assetPath));
        }

        // ------------------------------------------------------------------ textures

        /// <summary>The WP-107B shared atlas maps the kit binds (S-02 imports and owns them; this pipeline only reads them).</summary>
        public static string[] SharedTextureFiles()
        {
            string stem = ArtLibraryMaterialRoles.Ap040SharedTextureFolder + "/" + ArtLibraryMaterialRoles.Ap040SharedTextureStem + "_";
            return new[] { stem + "BaseColor.png", stem + "Normal.png", stem + "MaskMap.png", stem + "Emission.png" };
        }

        /// <summary>
        /// The fourteen maps stage-ap040.py copies into Textures/Source as T_&lt;role&gt;_&lt;Map&gt;.png: the three HF Lit sets (BaseColor +
        /// Normal + MaskMap), the HF momiji (BaseColor + Normal + VegetationORM), the flora VegetationORM + PetalMask.
        /// </summary>
        public static List<string> StagedTextureFiles()
        {
            var list = new List<string>(ExpectedStagedTextures);
            foreach (string family in ArtLibraryMaterialRoles.Ap040HfLitFamilies)
                foreach (string map in new[] { "BaseColor", "Normal", "MaskMap" })
                    list.Add(ArtPipelinePaths.SourceTexture(ArtLibraryMaterialRoles.Ap040RolePrefix + family, map));
            foreach (string map in new[] { "BaseColor", "Normal", "VegetationORM" })
                list.Add(ArtPipelinePaths.SourceTexture(ArtLibraryMaterialRoles.Ap040RolePrefix + "momiji_hf", map));
            foreach (string map in new[] { "VegetationORM", "PetalMask" })
                list.Add(ArtPipelinePaths.SourceTexture(ArtLibraryMaterialRoles.Ap040RolePrefix + "flora", map));
            return list;
        }

        /// <summary>
        /// Before the run: the staged library JSON, every FBX it names, the four shared maps and the fourteen staged maps are on disk.
        /// Throws with the full list of what is missing (run stage-ap040.py --wt first; the shared atlas comes with S-02's 8aef79f4).
        /// </summary>
        public static void Preflight()
        {
            var missing = new List<string>();
            string json = ArtLibraryPaths.ManifestJson(Package);
            if (!File.Exists(json))
            {
                missing.Add(json);
            }
            else
            {
                ArtLibraryManifest m = ArtLibraryManifest.FromJson(File.ReadAllText(json));
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (ArtLibraryManifest.Family f in m.families)
                foreach (ArtLibraryManifest.Variation v in f.variations)
                foreach (ArtLibraryManifest.Part p in v.parts)
                foreach (ArtLibraryManifest.Lod l in p.lods)
                {
                    string fbx = ArtLibraryPaths.Fbx(Package, f.family, l.fbx);
                    if (seen.Add(fbx) && !File.Exists(fbx)) missing.Add(fbx);
                }
            }

            foreach (string f in SharedTextureFiles()) if (!File.Exists(f)) missing.Add(f + " (S-02 WP-107B shared atlas)");
            foreach (string f in StagedTextureFiles()) if (!File.Exists(f)) missing.Add(f);
            if (missing.Count > 0)
            {
                throw new FileNotFoundException(LogPrefix + "AP-040 inputs missing (" + missing.Count + "; stage with wp-108/ap040-intake/stage-ap040.py --wt):\n" + string.Join("\n", missing));
            }
        }

        // ------------------------------------------------------------------ verification (batch evidence)

        /// <summary>
        /// After the run: every manifest variation has its prefab with a root LODGroup of three LODs, sockets at the root (18), the proxy
        /// count (34 objects; no flora collider; no collider renders), plaza collider tops out of (0.30, 0.50) m and not above 1.20 m in
        /// the Y5 band. Returns the one-line summary; throws with every failure.
        /// </summary>
        public static string Verify()
        {
            ArtLibraryManifest m = ArtLibraryPrefabBuilder.LoadManifest(Package);
            var failures = new List<string>();
            int prefabs = 0, sockets = 0, proxies = 0, colliders = 0, shadowOff = 0;
            float highestPlazaY5 = 0f;
            var tops = new List<string>();
            foreach (ArtLibraryManifest.Family f in m.families)
            foreach (ArtLibraryManifest.Variation v in f.variations)
            {
                proxies += v.colliders.Count;
                string path = ArtLibraryPaths.Prefab(Package, f.family, v.prefab);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { failures.Add(path + ": missing"); continue; }
                prefabs++;
                var group = prefab.GetComponent<LODGroup>();
                if (group == null || group.GetLODs().Length != 3) failures.Add(v.prefab + ": root LODGroup with 3 LODs expected");
                foreach (ArtLibraryManifest.Socket s in v.sockets)
                {
                    if (prefab.transform.Find(s.name) == null) failures.Add(v.prefab + ": socket " + s.name + " not at the root");
                    else sockets++;
                }

                if (!CastsShadow(f.family, v.id)) shadowOff++;
                Collider[] all = prefab.GetComponentsInChildren<Collider>(true);
                colliders += all.Length;
                if (IsFlora(f.family) && all.Length > 0) failures.Add(v.prefab + ": flora with " + all.Length + " colliders");
                foreach (Collider c in all)
                {
                    if (c.GetComponent<Renderer>() != null) failures.Add(v.prefab + "/" + c.name + ": a collider renders");
                    if (!IsPlaza(f.family)) continue;
                    float top = ColliderTop(c, prefab.transform);
                    tops.Add(v.prefab + " " + top.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                    if (InForbiddenBand(top)) failures.Add(v.prefab + "/" + c.name + ": top " + top.ToString("0.000") + " m in (0.30, 0.50) (S-05 §6)");
                    if (top > ShootOverTop + BandTolerance && top <= ShootOverBandHigh + BandTolerance) failures.Add(v.prefab + "/" + c.name + ": top " + top.ToString("0.000") + " m in the Y5 band (≤ 1.20)");
                    if (top <= ShootOverBandHigh + BandTolerance) highestPlazaY5 = Mathf.Max(highestPlazaY5, top);
                }
            }

            if (m.families.Count != ExpectedFamilies) failures.Add("families " + m.families.Count + " ≠ " + ExpectedFamilies);
            if (prefabs != m.VariationCount || prefabs != ExpectedVariations) failures.Add("prefabs " + prefabs + " (manifest " + m.VariationCount + ", v001 " + ExpectedVariations + ")");
            if (sockets != ExpectedSockets) failures.Add("sockets " + sockets + " ≠ " + ExpectedSockets);
            if (proxies != ExpectedColliderProxies) failures.Add("collider proxies " + proxies + " ≠ " + ExpectedColliderProxies);
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(LogPrefix + "AP-040 verify: " + failures.Count + " failures:\n" + string.Join("\n", failures));
            }

            return LogPrefix + "wp108.ap040 families=" + m.families.Count + " prefabs=" + prefabs + " sockets=" + sockets + " proxies=" + proxies +
                   " colliders=" + colliders + " shadowOffVariations=" + shadowOff + " plazaTopMaxBelowY5=" +
                   highestPlazaY5.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " | plaza tops: " + string.Join(", ", tops);
        }

        /// <summary>The highest point of a collider in prefab-root space (asset prefabs are not in a scene, so Collider.bounds is empty: corners are mapped).</summary>
        public static float ColliderTop(Collider c, Transform root)
        {
            Matrix4x4 m = root.worldToLocalMatrix * c.transform.localToWorldMatrix;
            float top = float.MinValue;
            if (c is BoxCollider b)
            {
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.size * 0.5f, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
                    top = Mathf.Max(top, m.MultiplyPoint3x4(corner).y);
                }
            }
            else if (c is CapsuleCollider k)
            {
                Vector3 axis = k.direction == 0 ? Vector3.right : k.direction == 1 ? Vector3.up : Vector3.forward;
                float half = Mathf.Max(0f, k.height * 0.5f - k.radius);
                foreach (float s in new[] { -1f, 1f }) top = Mathf.Max(top, m.MultiplyPoint3x4(k.center + axis * (s * half)).y + k.radius * m.lossyScale.y);
            }
            else if (c is MeshCollider mc && mc.sharedMesh != null)
            {
                Bounds mb = mc.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) != 0 ? mb.max.x : mb.min.x, (i & 2) != 0 ? mb.max.y : mb.min.y, (i & 4) != 0 ? mb.max.z : mb.min.z);
                    top = Mathf.Max(top, m.MultiplyPoint3x4(corner).y);
                }
            }

            return top;
        }
    }
}
