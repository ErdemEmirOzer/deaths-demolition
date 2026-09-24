using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-069 B4c · AP-021 (P-07) weapon models into the game — the two Specials first (Raiju sw_arc,
    /// Namekuji sw_gel, v002); the classic keys follow the same path.
    ///
    /// Source files are copied verbatim (sha256 against the AP-021 manifest, see each folder's
    /// <c>AP021_&lt;key&gt;.provenance.json</c>) into <see cref="Root"/>/AP021_&lt;key&gt;/ — outside the
    /// <c>Assets/_Art</c> import contract, so this builder owns every importer setting:
    ///   1. textures: BaseColor / Emissive sRGB, Normal as normal map, MRA / EmissiveMask linear;
    ///   2. MRA (R metallic, G roughness, B AO) → derived URP mask with <see cref="ArtWeaponBuilder.MraToMetallicSmoothnessAo"/>
    ///      (the AP-013 rule: R metallic, G occlusion, A smoothness);
    ///   3. materials: body (URP Lit, maps + emissive), glass (transparent, both faces), glow (emissive), gel
    ///      (sw_gel: transparent emissive) — the manifest's values;
    ///   4. the FBX: file scale, no axis bake (the ADR-042-1 hand-held rule of AP-013 — root = grip, +Z barrel),
    ///      hierarchy preserved, no collider / animation / camera / light, the FBX material slots remapped by
    ///      name onto (3);
    ///   5. <c>DD_SpecialWeapons</c> entries get the model (<see cref="SpecialWeaponVisualLibrary.Entry.model"/>) and
    ///      its per-LOD triangle totals.
    /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtAp021WeaponBuilder.BuildSpecials</c>.
    ///
    /// WP-069 B4d · the classics (<see cref="Classics"/>, as P-07 delivers them): the same 1–4 with the body material only
    /// (textures under exports/textures/&lt;key&gt;/, no emissive); <c>DD_ClassicWeapons</c> gets an own-material entry per
    /// runtime id (<see cref="ClassicWeaponVisualLibrary.Entry.ownMaterials"/>) beside the AP-013 A/B/C, and the AP-013
    /// rebuild keeps them. Batch: <c>…ArtAp021WeaponBuilder.BuildClassics</c>.
    /// </summary>
    public static class ArtAp021WeaponBuilder
    {
        public const string LogPrefix = "[ArtAp021WeaponBuilder] ";
        public const string Package = "AP-021";
        public const string Root = "Assets/_Prototype/Weapons/Arsenal/Models";
        public const string ShaderName = "Universal Render Pipeline/Lit";

        /// <summary>
        /// Frame fix for the AP-021 exports (see <see cref="SpecialWeaponVisualLibrary.Entry.modelFrameEuler"/>). Zero since P-07's
        /// v002-uf (25.09): the exporter bakes the Unity frame (root = grip, +Z barrel, +Y up, +X port) — the first v002
        /// landed barrel-up and mirrored.
        /// </summary>
        public static readonly Vector3 FrameFixEuler = Vector3.zero;

        public static readonly (SpecialWeaponId id, string key)[] Specials =
        {
            (SpecialWeaponId.Raiju, SpecialWeaponKeys.Raiju),
            (SpecialWeaponId.Namekuji, SpecialWeaponKeys.Namekuji),
        };

        /// <summary>WP-069 B4d: the AP-021 classics in the project (P-07 v002, Unity frame at export).</summary>
        public static readonly (ClassicWeaponId id, string key)[] Classics =
        {
            (ClassicWeaponId.Ar2, "ar_2"),
            (ClassicWeaponId.Sg2, "sg_2"),
        };

        public static string Folder(string key) => Root + "/AP021_" + key;
        public static string ModelPath(string key) => Folder(key) + "/AP021_" + key + ".fbx";
        public static string TexturePath(string key, string map) => Folder(key) + "/Textures/T_AP021_" + key + "_" + map + ".png";
        public static string DerivedMaskPath(string key) => Folder(key) + "/Derived/T_AP021_" + key + "_MetallicSmoothness.png";
        public static string MaterialPath(string key, string slot) => Folder(key) + "/Materials/M_AP021_" + key + "_" + slot + ".mat";

        /// <summary>The imported model of a Special, or null when the package is not in the project.</summary>
        public static GameObject ModelFor(SpecialWeaponId id)
        {
            foreach ((SpecialWeaponId sid, string key) in Specials)
            {
                if (sid == id) return AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(key));
            }

            return null;
        }

        /// <summary>The imported model of a classic, or null when it is not in the project.</summary>
        public static GameObject ModelFor(ClassicWeaponId id)
        {
            foreach ((ClassicWeaponId cid, string key) in Classics)
            {
                if (cid == id) return AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(key));
            }

            return null;
        }

        [MenuItem("DD/Arsenal/Import AP-021 Classics (WP-069 B4d)", priority = 332)]
        public static void BuildClassics()
        {
            foreach ((ClassicWeaponId _, string key) in Classics)
            {
                BuildOne(key);
            }

            var library = AssetDatabase.LoadAssetAtPath<ClassicWeaponVisualLibrary>(ArtWeaponBuilder.LibraryPath);
            if (library == null) throw new FileNotFoundException(LogPrefix + "DD_ClassicWeapons is missing — run the AP-013 library first", ArtWeaponBuilder.LibraryPath);
            var entries = new List<ClassicWeaponVisualLibrary.Entry>();
            foreach (ClassicWeaponVisualLibrary.Entry e in library.entries)
            {
                if (e != null && !e.ownMaterials) entries.Add(e);
            }

            AppendClassics(entries);
            library.entries = entries.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "classics done");
        }

        /// <summary>One own-material entry per imported AP-021 classic. Also called by the AP-013 library rebuild so it never drops them.</summary>
        public static void AppendClassics(List<ClassicWeaponVisualLibrary.Entry> entries)
        {
            foreach ((ClassicWeaponId id, string key) in Classics)
            {
                GameObject model = ModelFor(id);
                if (model == null) continue;
                var e = new ClassicWeaponVisualLibrary.Entry { id = id, rosterKey = key, model = model, ownMaterials = true, modelPackage = Package };
                foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh == null || f.name.StartsWith(AuthoredWeaponModel.ColliderPrefix, StringComparison.Ordinal)) continue;
                    int lod = AuthoredWeaponModel.LodIndexOf(f.name);
                    if (lod >= 0 && lod < e.lodTriangles.Length) e.lodTriangles[lod] += Triangles(f.sharedMesh);
                }

                entries.RemoveAll(x => x != null && x.id == id);
                entries.Add(e);
                Transform muzzle = WeaponSocketContract.Find(model.transform, WeaponSocketContract.Muzzle);
                Transform port = WeaponSocketContract.Find(model.transform, WeaponSocketContract.Ejection);
                Debug.Log(LogPrefix + key + " tris " + string.Join("/", e.lodTriangles) + " muzzle " +
                          (muzzle != null ? model.transform.InverseTransformPoint(muzzle.position).ToString("F3") : "MISSING") + " ejection " +
                          (port != null ? model.transform.InverseTransformPoint(port.position).ToString("F3") : "MISSING"));
            }
        }

        [MenuItem("DD/Arsenal/Import AP-021 Specials (WP-069 B4c)", priority = 331)]
        public static void BuildSpecials()
        {
            foreach ((SpecialWeaponId _, string key) in Specials)
            {
                BuildOne(key);
            }

            SpecialWeaponVisualLibrary library = AssetDatabase.LoadAssetAtPath<SpecialWeaponVisualLibrary>(ArtSpecialWeaponBuilder.LibraryPath);
            if (library == null) throw new FileNotFoundException(LogPrefix + "DD_SpecialWeapons is missing — run the AP-014 library first", ArtSpecialWeaponBuilder.LibraryPath);
            library.schema = SpecialWeaponVisualLibrary.Schema;
            foreach ((SpecialWeaponId id, string _) in Specials)
            {
                SpecialWeaponVisualLibrary.Entry e = library.Find(id);
                if (e == null) throw new InvalidOperationException(LogPrefix + "no library entry for " + id);
                ApplyModel(e);
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "done");
        }

        /// <summary>Put the AP-021 model (if imported) on an entry. Also called by the AP-014 library rebuild so it never drops the model.</summary>
        public static void ApplyModel(SpecialWeaponVisualLibrary.Entry e)
        {
            GameObject model = ModelFor(e.id);
            e.model = model;
            e.modelPackage = model != null ? Package : "";
            e.modelFrameEuler = model != null ? FrameFixEuler : Vector3.zero;
            e.modelTriangles = new int[SpecialWeaponVisualLibrary.LodCount];
            if (model == null) return;

            foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || f.name.StartsWith(SpecialWeaponVisualLibrary.ColliderPrefix, StringComparison.Ordinal)) continue;
                int lod = SpecialWeaponVisualLibrary.LodIndexOf(f.name);
                if (lod < 0) continue;
                e.modelTriangles[lod] += Triangles(f.sharedMesh);
            }

            Quaternion fix = Quaternion.Euler(e.modelFrameEuler);
            Transform muzzle = WeaponSocketContract.Find(model.transform, WeaponSocketContract.Muzzle);
            Transform port = WeaponSocketContract.Find(model.transform, WeaponSocketContract.Ejection);
            Debug.Log(LogPrefix + e.id + " tris " + string.Join("/", e.modelTriangles) + " muzzle " +
                      (muzzle != null ? (fix * model.transform.InverseTransformPoint(muzzle.position)).ToString("F3") : "MISSING") + " ejection " +
                      (port != null ? (fix * model.transform.InverseTransformPoint(port.position)).ToString("F3") : "MISSING") + " (frame fix " + e.modelFrameEuler + ")");
        }

        private static void BuildOne(string key)
        {
            if (!File.Exists(ModelPath(key))) throw new FileNotFoundException(LogPrefix + "model missing", ModelPath(key));

            ImportTexture(TexturePath(key, "BaseColor"), true, false);
            ImportTexture(TexturePath(key, "Normal"), false, true);
            ImportTexture(TexturePath(key, "MRA"), false, false);
            if (File.Exists(TexturePath(key, "Emissive"))) ImportTexture(TexturePath(key, "Emissive"), true, false);
            if (File.Exists(TexturePath(key, "EmissiveMask"))) ImportTexture(TexturePath(key, "EmissiveMask"), false, false);

            string mask = DerivedMaskPath(key);
            ArtTextureRepack.BuildDerived(TexturePath(key, "MRA"), mask, Path.ChangeExtension(mask, null) + ".provenance.json",
                ArtWeaponBuilder.MraToMetallicSmoothnessAo, ArtWeaponBuilder.MraAlgorithm);
            ImportTexture(mask, false, false);

            Dictionary<string, Material> slots = BuildMaterials(key);
            ImportModel(key, slots);
        }

        // ------------------------------------------------------------------ textures

        private static void ImportTexture(string path, bool srgb, bool normal)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            if (importer == null) throw new FileNotFoundException(LogPrefix + "texture missing", path);

            bool changed = false;
            TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type) { importer.textureType = type; changed = true; }
            if (!normal && importer.sRGBTexture != srgb) { importer.sRGBTexture = srgb; changed = true; }
            if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
            if (importer.maxTextureSize != 2048) { importer.maxTextureSize = 2048; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.CompressedHQ) { importer.textureCompression = TextureImporterCompression.CompressedHQ; changed = true; }
            if (importer.isReadable) { importer.isReadable = false; changed = true; }
            if (changed) importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ materials

        private static Dictionary<string, Material> BuildMaterials(string key)
        {
            Shader lit = Shader.Find(ShaderName);
            if (lit == null) throw new InvalidOperationException(LogPrefix + "shader missing: " + ShaderName);
            var slots = new Dictionary<string, Material>();

            Material body = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(MaterialPath(key, "body"), lit, "M_AP021_" + key + "_body");
            ArtCharacterPrefabBuilder.ApplyCharacterMapsPublic(body, Load<Texture2D>(TexturePath(key, "BaseColor")), Load<Texture2D>(TexturePath(key, "Normal")),
                Load<Texture2D>(DerivedMaskPath(key)));
            if (File.Exists(TexturePath(key, "Emissive")))
            {
                body.SetTexture("_EmissionMap", Load<Texture2D>(TexturePath(key, "Emissive")));
                body.SetColor("_EmissionColor", Color.white);
                body.EnableKeyword("_EMISSION");
            }

            body.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            body.enableInstancing = true;
            slots["body"] = Save(body);
            if (key != SpecialWeaponKeys.Raiju && key != SpecialWeaponKeys.Namekuji)
            {
                return slots;   // WP-069 B4d: a classic carries the body material only (AP-021 manifest)
            }

            // Manifest: glass #C9DAD3 α 0.25, smoothness 0.95, both faces (the dome / the lantern window).
            Material glass = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(MaterialPath(key, "glass"), lit, "M_AP021_" + key + "_glass");
            SetTransparent(glass, Hex("#C9DAD3", 0.25f), 0.95f, true);
            slots["glass"] = Save(glass);

            if (key == SpecialWeaponKeys.Raiju)
            {
                // Idle filament glow #FFC65A ×2 (S-07 drives the per-state colour / intensity).
                Material glow = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(MaterialPath(key, "glow"), lit, "M_AP021_" + key + "_glow");
                SetEmissive(glow, Hex("#FFC65A", 1f), 2f);
                slots["glow"] = Save(glow);
            }
            else
            {
                // Gel core #FF5FD2 ×1.5 idle; the gel body #B040FF α 0.8, smoothness 0.92 (S-07's liquid shader replaces it).
                Material glow = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(MaterialPath(key, "glow"), lit, "M_AP021_" + key + "_glow");
                SetEmissive(glow, Hex("#FF5FD2", 1f), 1.5f);
                slots["glow"] = Save(glow);

                Material gel = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(MaterialPath(key, "gel"), lit, "M_AP021_" + key + "_gel");
                SetTransparent(gel, Hex("#B040FF", 0.8f), 0.92f, false);
                gel.SetColor("_EmissionColor", Hex("#B040FF", 1f));
                gel.EnableKeyword("_EMISSION");
                gel.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                slots["gel"] = Save(gel);
            }

            return slots;
        }

        private static void SetTransparent(Material m, Color color, float smoothness, bool bothFaces)
        {
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", bothFaces ? (float)CullMode.Off : (float)CullMode.Back);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        private static void SetEmissive(Material m, Color color, float intensity)
        {
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.5f);
            m.SetColor("_EmissionColor", color * intensity);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        private static Material Save(Material m)
        {
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ model

        private static void ImportModel(string key, Dictionary<string, Material> slots)
        {
            string path = ModelPath(key);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as ModelImporter;
            }

            if (importer == null) throw new FileNotFoundException(LogPrefix + "model importer missing", path);

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;   // ADR-042-1 hand-held rule (ArtImportContract.BakesAxis: AP-013 imports right without it)
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.preserveHierarchy = true;
            importer.generateSecondaryUV = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            foreach (KeyValuePair<string, Material> slot in slots)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_AP021_" + key + "_" + slot.Key), slot.Value);
            }

            importer.SaveAndReimport();
        }

        private static int Triangles(Mesh mesh)
        {
            long indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) indices += mesh.GetIndexCount(s);
            return (int)(indices / 3);
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException(LogPrefix + "asset missing", path);
            return asset;
        }

        private static Color Hex(string hex, float alpha)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            c.a = alpha;
            return c;
        }
    }
}
