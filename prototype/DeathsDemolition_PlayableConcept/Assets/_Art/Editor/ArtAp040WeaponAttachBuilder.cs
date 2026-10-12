using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using DD.Prototype.Weapons.Attachments;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-104 B4 (S-06, C-00 12.10 · B4-PLAN-12.10 §3.2 / §4 steps 2–3 / §5) · bakes the AP-040 v001 EK1 weapon attachments (E1–E6) into
    /// the game. The builder bakes data only; the runtime (<c>Assets/_Prototype/Weapons/Attachments</c>) builds the three sockets on the
    /// equipped model and swaps the attachments at event time, never reading JSON.
    ///
    /// Source files are copied verbatim (sha256 against the AP-040 export's SHA256SUMS and manifest, see
    /// <c>AP040_weapon_attach.provenance.json</c> and the folder's own SHA256SUMS) into <see cref="SourceFolder"/>: the 14 E-family FBX,
    /// the E4 / E5 scalar emissive masks and <c>weapon_attach/sockets.json</c> as <see cref="SocketsJson"/>. The shared atlas is NOT
    /// copied: its one home is S-02's <see cref="SharedMaterialFolder"/> (C-00 decision); the builder only resolves its two materials.
    ///
    /// Build steps:
    ///   1. every copied file against the folder's SHA256SUMS; the socket table read and checked (schema <see cref="WeaponAttachLibrary.Schema"/>,
    ///      measured, ten classic weapons, six E3×E5 combinations each) before anything is written;
    ///   2. <see cref="SharedMaterialPath"/> / <see cref="GlowMaterialPath"/> resolved by path (a clear exception while S-02's home is absent);
    ///   3. importers: the FBX like the AP-021 weapons (file scale, no axis bake, readable off, no collider / animation / camera / light)
    ///      with no materials imported; the masks linear, clamped, mipmapped, no alpha (emissive.json "import");
    ///   4. one prefab per variant under <see cref="PrefabFolder"/>: the mount pivot is the prefab root (the FBX's own mount marker at the
    ///      origin is dropped), the <c>*_LOD0..2</c> children keep their names and FBX poses, the derived <c>SOCKET_*</c> children come from
    ///      the FBX in the attachment frame (identity rotation: +Z down the barrel, the sockets.json convention), no LODGroup (the runtime
    ///      folds the attachment into the weapon's single LODGroup); slot 0 <c>M_AP040_Shared</c>, slot 1 (E4 / E5, the FBX's
    ///      <c>M_AP040_Glow</c> slot) <c>M_AP040_Glow</c>. Frame check (plan §4.3): E4 must extend along +X — Unity's FBX import mirrors X
    ///      against the Blender authoring frame, so a capsule that lands on −X is turned 180° about Y (E4 is symmetric in Z);
    ///   5. the rear-drop bridges (AP-040 gave dimensions only, no FBX) as Mesh assets under <see cref="BridgeFolder"/>, one per weapon and
    ///      E5 variant where the table requires one;
    ///   6. <see cref="LibraryPath"/>: a row per classic weapon (id by v3 key, the three poses, magOnPart, the six combinations in
    ///      sockets.json order) plus the prefab slots and the table's sha256.
    /// Menu <c>DD/Arsenal/Build AP-040 Weapon Attachments</c>; batch <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtAp040WeaponAttachBuilder.BuildCli</c>.
    /// </summary>
    public static class ArtAp040WeaponAttachBuilder
    {
        public const string LogPrefix = "[ArtAp040WeaponAttachBuilder] ";
        public const string Package = "AP-040";
        public const string Root = "Assets/_Prototype/Weapons/Arsenal/Attachments";
        public const string SourceFolder = Root + "/AP040";
        public const string PrefabFolder = Root + "/Prefabs";
        public const string BridgeFolder = Root + "/Bridges";
        public const string SocketsJson = SourceFolder + "/AP040_weapon_attach_sockets.json";
        public const string SourceSums = SourceFolder + "/SHA256SUMS";
        public const string LibraryPath = "Assets/_Art/Resources/" + WeaponAttachLibrary.ResourcePath + ".asset";

        /// <summary>The one home of the AP-040 shared atlas and its materials (S-02 owns it, C-00 decision) — never copied here.</summary>
        public const string SharedMaterialFolder = "Assets/_Prototype/Art/AP040/Shared";
        /// <summary>S-02's WP-107B layout (cbf0196d): Shared/Materials/ beside Shared/Textures/ (the four 2048 atlas maps).</summary>
        public const string SharedMaterialPath = SharedMaterialFolder + "/Materials/M_AP040_Shared.mat";
        public const string GlowMaterialPath = SharedMaterialFolder + "/Materials/M_AP040_Glow.mat";

        public const string SocketMag = "SOCKET_Mag";
        public const string SocketMuzzle = "SOCKET_Muzzle";
        public const string SocketSide = "SOCKET_Side";
        public const string SocketSecondaryMuzzle = "SOCKET_SecondaryMuzzle";
        public const string SocketPowerMuzzle = "SOCKET_PowerMuzzle";
        public const string SocketElementVfx = "SOCKET_ElementVFX";
        public static readonly string[] SocketRockets = { "SOCKET_Rocket_1", "SOCKET_Rocket_2", "SOCKET_Rocket_3", "SOCKET_Rocket_4" };

        /// <summary>The E4 / E5 scalar emissive masks (S-07's glow ladder reads them; emissive.json).</summary>
        public static readonly string[] EmissiveMasks =
        {
            SourceFolder + "/T_AP040_E4_EmissiveMask.png",
            SourceFolder + "/T_AP040_E5_EmissiveMask.png",
            SourceFolder + "/T_AP040_E5_FullPatternMask.png",
            SourceFolder + "/T_AP040_E5_ChaosInlayMask.png",
        };

        /// <summary>
        /// The rear-drop bridge's atlas cell (AP040-atlas-v001.json role <c>upgrade_speed</c>, uv_rect 0.7539/0.5039–0.8711/0.6211, bottom-left
        /// origin): AP-040's fit proof drew the bridge in that role, so the runtime can render it with <c>M_AP040_Shared</c>.
        /// </summary>
        public static readonly Vector2 BridgeAtlasCell = new Vector2(0.8125f, 0.5625f);
        private const float BridgeUvHalf = 0.02f;

        /// <summary>One AP-040 attachment variant (the CARD_*.json facts the builder relies on).</summary>
        public readonly struct Variant
        {
            /// <summary>FBX / prefab base name (AP040_&lt;family&gt;_VNN).</summary>
            public readonly string Name;
            /// <summary>"E1" … "E6".</summary>
            public readonly string Family;
            /// <summary>Slot in the library's family array (variant − 1).</summary>
            public readonly int Index;
            /// <summary>The weapon socket the attachment mounts on; the FBX marks it at the origin (= the prefab root).</summary>
            public readonly string MountSocket;
            /// <summary>Material slots: 1 = M_AP040_Shared; 2 = M_AP040_Shared + M_AP040_Glow (the FBX's slot order).</summary>
            public readonly int MaterialSlots;
            /// <summary>The derived sockets the FBX carries (CARD notes.sockets minus the mount).</summary>
            public readonly string[] Sockets;
            /// <summary>E4: the capsule sits on the weapon's right side, the mesh must extend along +X.</summary>
            public readonly bool ExtendsPlusX;

            public Variant(string name, string family, int index, string mountSocket, int materialSlots, string[] sockets, bool extendsPlusX = false)
            {
                Name = name;
                Family = family;
                Index = index;
                MountSocket = mountSocket;
                MaterialSlots = materialSlots;
                Sockets = sockets;
                ExtendsPlusX = extendsPlusX;
            }
        }

        private static readonly string[] NoSockets = Array.Empty<string>();

        /// <summary>AP-040 v001 EK1: E1 extended mag, E2 drum S/M/L, E3 twin barrel S/M/L, E4 element capsule fire/ice/shock/venom, E5 power ring speed/chaos, E6 rl_1 belt box.</summary>
        public static readonly Variant[] Variants =
        {
            new Variant("AP040_extended_magazine_V01", "E1", 0, SocketMag, 1, NoSockets),
            new Variant("AP040_drum_magazine_V01", "E2", 0, SocketMag, 1, NoSockets),
            new Variant("AP040_drum_magazine_V02", "E2", 1, SocketMag, 1, NoSockets),
            new Variant("AP040_drum_magazine_V03", "E2", 2, SocketMag, 1, NoSockets),
            new Variant("AP040_double_barrel_V01", "E3", 0, SocketMuzzle, 1, new[] { SocketSecondaryMuzzle }),
            new Variant("AP040_double_barrel_V02", "E3", 1, SocketMuzzle, 1, new[] { SocketSecondaryMuzzle }),
            new Variant("AP040_double_barrel_V03", "E3", 2, SocketMuzzle, 1, new[] { SocketSecondaryMuzzle }),
            new Variant("AP040_element_capsule_V01", "E4", 0, SocketSide, 2, new[] { SocketElementVfx }, true),
            new Variant("AP040_element_capsule_V02", "E4", 1, SocketSide, 2, new[] { SocketElementVfx }, true),
            new Variant("AP040_element_capsule_V03", "E4", 2, SocketSide, 2, new[] { SocketElementVfx }, true),
            new Variant("AP040_element_capsule_V04", "E4", 3, SocketSide, 2, new[] { SocketElementVfx }, true),
            new Variant("AP040_muzzle_power_ring_V01", "E5", 0, SocketMuzzle, 2, new[] { SocketPowerMuzzle }),
            new Variant("AP040_muzzle_power_ring_V02", "E5", 1, SocketMuzzle, 2, new[] { SocketPowerMuzzle }),
            new Variant("AP040_rocket_full_auto_box_V01", "E6", 0, SocketMag, 1, SocketRockets),
        };

        public static string FbxPath(string name) => SourceFolder + "/" + name + ".fbx";
        public static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
        public static string BridgePath(string key, int ring) => BridgeFolder + "/AP040_bridge_" + key + "_E5v" + ring + ".asset";

        /// <summary>The library slot a variant fills (null for an unknown family).</summary>
        public static GameObject SlotOf(WeaponAttachLibrary library, Variant v)
        {
            if (library == null) return null;
            switch (v.Family)
            {
                case "E1": return library.e1;
                case "E2": return Slot(library.e2, v.Index);
                case "E3": return Slot(library.e3, v.Index);
                case "E4": return Slot(library.e4, v.Index);
                case "E5": return Slot(library.e5, v.Index);
                case "E6": return library.e6;
                default: return null;
            }
        }

        private static GameObject Slot(GameObject[] slots, int index) => slots != null && index >= 0 && index < slots.Length ? slots[index] : null;

        // ------------------------------------------------------------------ socket table (sockets.json, AP040.weapon-attach-sockets.v001)

        [Serializable]
        public sealed class SocketsDto
        {
            public string schema;
            public string date;
            public bool measured;
            public string units;
            public WeaponDto[] weapons;
        }

        [Serializable]
        public sealed class WeaponDto
        {
            public string weapon_id;
            public string source_git_path;
            public string source_sha256;
            public PoseDto SOCKET_Muzzle;
            public PoseDto SOCKET_Mag;
            public PoseDto SOCKET_Side;
            public ComboDto[] combinations;
        }

        [Serializable]
        public sealed class PoseDto
        {
            public float[] position_unity_m;
            public float[] rotation_unity_deg;
            /// <summary>Only on SOCKET_Mag; preferred over the Euler angles when present.</summary>
            public float[] rotation_unity_quaternion_xyzw;
            public float uniformScale;
        }

        [Serializable]
        public sealed class ComboDto
        {
            public int E3_variant;
            public int E5_variant;
            public float E3_uniformScale;
            public float E5_uniformScale;
            public float[] E3_offset_unity_m;
            public float[] E5_offset_unity_m;
            public PoseDto SOCKET_SecondaryMuzzle;
            public PoseDto SOCKET_PowerMuzzle;
            public AdapterDto adapter;
        }

        [Serializable]
        public sealed class AdapterDto
        {
            public string type;
            public bool required;
            public float width_m;
            public float rear_y_m;
            public float from_z_m;
            public float to_z_m;
            public float[] rails_x_m;
        }

        /// <summary>Reads and checks the socket table; throws on a schema / structure the library cannot hold.</summary>
        public static SocketsDto ReadTable(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(LogPrefix + "socket table missing (copy AP-040 v001 weapon_attach/sockets.json)", path);
            SocketsDto table = JsonUtility.FromJson<SocketsDto>(File.ReadAllText(path));
            if (table == null) throw new InvalidOperationException(LogPrefix + "socket table unreadable: " + path);
            if (table.schema != WeaponAttachLibrary.Schema)
            {
                throw new InvalidOperationException(LogPrefix + "socket table schema '" + table.schema + "' is not " + WeaponAttachLibrary.Schema + " — refusing " + path);
            }

            if (!table.measured) throw new InvalidOperationException(LogPrefix + "socket table is not marked measured: " + path);
            if (table.units != "metres") throw new InvalidOperationException(LogPrefix + "socket table units '" + table.units + "', expected metres: " + path);
            if (table.weapons == null || table.weapons.Length != ArtAp021WeaponBuilder.Classics.Length)
            {
                throw new InvalidOperationException(LogPrefix + "socket table has " + (table.weapons?.Length ?? 0) + " weapons, expected " +
                                                    ArtAp021WeaponBuilder.Classics.Length + " classics: " + path);
            }

            var seen = new HashSet<ClassicWeaponId>();
            foreach (WeaponDto w in table.weapons)
            {
                if (w == null || !WeaponIdMigration.TryGetRuntimeId(w.weapon_id, out ClassicWeaponId id))
                {
                    throw new InvalidOperationException(LogPrefix + "socket table weapon '" + w?.weapon_id + "' is not a classic v3 key");
                }

                if (!seen.Add(id)) throw new InvalidOperationException(LogPrefix + "socket table lists " + w.weapon_id + " twice");
                Vec(w.SOCKET_Muzzle?.position_unity_m, w.weapon_id + " SOCKET_Muzzle");
                Vec(w.SOCKET_Mag?.position_unity_m, w.weapon_id + " SOCKET_Mag");
                Vec(w.SOCKET_Side?.position_unity_m, w.weapon_id + " SOCKET_Side");
                if (w.SOCKET_Mag.uniformScale > 0f && Mathf.Abs(w.SOCKET_Mag.uniformScale - 1f) > 1e-4f)
                {
                    throw new InvalidOperationException(LogPrefix + w.weapon_id + " SOCKET_Mag uniformScale " + w.SOCKET_Mag.uniformScale + " — the library holds a unit Mag pose only");
                }

                int expected = WeaponAttachLibrary.TwinVariants * WeaponAttachLibrary.RingVariants;
                if (w.combinations == null || w.combinations.Length != expected)
                {
                    throw new InvalidOperationException(LogPrefix + w.weapon_id + ": " + (w.combinations?.Length ?? 0) + " combinations, expected " + expected);
                }

                for (int i = 0; i < w.combinations.Length; i++)
                {
                    ComboDto c = w.combinations[i];
                    if (c.E3_variant < 1 || c.E3_variant > WeaponAttachLibrary.TwinVariants || c.E5_variant < 1 || c.E5_variant > WeaponAttachLibrary.RingVariants)
                    {
                        throw new InvalidOperationException(LogPrefix + w.weapon_id + " combination " + i + ": E3 v" + c.E3_variant + " / E5 v" + c.E5_variant + " out of range");
                    }

                    if (WeaponAttachLibrary.ComboIndex(c.E3_variant, c.E5_variant) != i)
                    {
                        throw new InvalidOperationException(LogPrefix + w.weapon_id + " combination " + i + " (E3 v" + c.E3_variant + ", E5 v" + c.E5_variant +
                                                            ") is out of the sockets.json order WeaponAttachLibrary.ComboIndex expects");
                    }

                    if (Mathf.Abs(c.E3_uniformScale - 1f) > 1e-4f)
                    {
                        throw new InvalidOperationException(LogPrefix + w.weapon_id + " combination " + i + ": E3 uniformScale " + c.E3_uniformScale + " — the library holds a unit E3 only");
                    }

                    if (c.E5_uniformScale <= 0f) throw new InvalidOperationException(LogPrefix + w.weapon_id + " combination " + i + ": E5 uniformScale " + c.E5_uniformScale);
                    string at = w.weapon_id + " combination " + i;
                    Vec(c.E3_offset_unity_m, at + " E3_offset_unity_m");
                    Vec(c.E5_offset_unity_m, at + " E5_offset_unity_m");
                    Vec(c.SOCKET_SecondaryMuzzle?.position_unity_m, at + " SOCKET_SecondaryMuzzle");
                    Vec(c.SOCKET_PowerMuzzle?.position_unity_m, at + " SOCKET_PowerMuzzle");
                    if (c.adapter == null || c.adapter.type != "rear_drop_bridge")
                    {
                        throw new InvalidOperationException(LogPrefix + at + ": adapter '" + c.adapter?.type + "' is not a rear_drop_bridge");
                    }

                    if (c.adapter.required && (c.adapter.rails_x_m == null || c.adapter.rails_x_m.Length == 0 || c.adapter.width_m <= 0f))
                    {
                        throw new InvalidOperationException(LogPrefix + at + ": required bridge without rails / width");
                    }
                }
            }

            return table;
        }

        /// <summary>A table pose: the position, and the table quaternion when given (checked against the Euler angles) else Euler(rotation_unity_deg).</summary>
        public static AttachPose PoseOf(PoseDto p, string what)
        {
            var pose = new AttachPose { position = Vec(p?.position_unity_m, what), rotation = Quaternion.identity };
            Quaternion euler = p.rotation_unity_deg != null && p.rotation_unity_deg.Length == 3
                ? Quaternion.Euler(p.rotation_unity_deg[0], p.rotation_unity_deg[1], p.rotation_unity_deg[2])
                : Quaternion.identity;
            if (p.rotation_unity_quaternion_xyzw != null && p.rotation_unity_quaternion_xyzw.Length == 4)
            {
                float[] q = p.rotation_unity_quaternion_xyzw;
                Quaternion table = new Quaternion(q[0], q[1], q[2], q[3]).normalized;
                if (Quaternion.Angle(table, euler) > 0.1f)
                {
                    throw new InvalidOperationException(LogPrefix + what + ": table quaternion and Euler angles disagree by " +
                                                        Quaternion.Angle(table, euler).ToString("0.000", CultureInfo.InvariantCulture) + "°");
                }

                pose.rotation = table;
            }
            else
            {
                pose.rotation = euler;
            }

            return pose;
        }

        private static Vector3 Vec(float[] v, string what)
        {
            if (v == null || v.Length != 3) throw new InvalidOperationException(LogPrefix + what + ": expected [x, y, z], got " + (v == null ? "nothing" : v.Length + " values"));
            return new Vector3(v[0], v[1], v[2]);
        }

        // ------------------------------------------------------------------ build

        [MenuItem("DD/Arsenal/Build AP-040 Weapon Attachments", priority = 333)]
        public static void Build()
        {
            VerifySources();
            SocketsDto table = ReadTable(SocketsJson);
            Material shared = RequireMaterial(SharedMaterialPath);
            Material glow = RequireMaterial(GlowMaterialPath);
            var parted = new Dictionary<ClassicWeaponId, bool>();
            foreach (WeaponDto w in table.weapons)
            {
                WeaponIdMigration.TryGetRuntimeId(w.weapon_id, out ClassicWeaponId id);
                parted[id] = HasPartMag(id, w);   // every AP-021 model checked before anything is written
            }

            foreach (string mask in EmissiveMasks) ImportMask(mask);
            foreach (Variant v in Variants) ImportModel(FbxPath(v.Name));

            EnsureFolder(PrefabFolder);
            EnsureFolder(BridgeFolder);
            var prefabs = new Dictionary<string, GameObject>();
            foreach (Variant v in Variants) prefabs[v.Name] = BuildPrefab(v, shared, glow);

            WeaponAttachLibrary library = LoadOrCreateLibrary();
            library.sourceSha256 = Sha256(SocketsJson);
            var rows = new List<WeaponAttachLibrary.Row>(table.weapons.Length);
            int bridges = 0;
            foreach (WeaponDto w in table.weapons) rows.Add(BuildRow(w, parted, ref bridges));
            library.rows = rows.ToArray();
            library.e2 = new GameObject[WeaponAttachLibrary.DrumVariants];
            library.e3 = new GameObject[WeaponAttachLibrary.TwinVariants];
            library.e4 = new GameObject[WeaponAttachLibrary.ElementVariants];
            library.e5 = new GameObject[WeaponAttachLibrary.RingVariants];
            foreach (Variant v in Variants) Assign(library, v, prefabs[v.Name]);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            WeaponAttachLibrary.ResetCache();
            Debug.Log(LogPrefix + "done: " + Variants.Length + " prefabs, " + bridges + " bridge meshes, " + library.rows.Length + " rows, sockets.json sha256 " +
                      library.sourceSha256 + " → " + LibraryPath);
        }

        /// <summary>Batch entry: <see cref="Build"/>, then save and refresh.</summary>
        public static void BuildCli()
        {
            Build();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Batch entry (S-06 12.10): the verbatim sources checked and the masks / FBX given their importer settings (no material import,
        /// the AP-021 scale) — everything that does not need S-02's shared AP-040 materials, so the copies never sit on Unity defaults.
        /// </summary>
        public static void ImportSourcesCli()
        {
            VerifySources();
            foreach (string mask in EmissiveMasks) ImportMask(mask);
            foreach (Variant v in Variants) ImportModel(FbxPath(v.Name));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "sources imported: " + EmissiveMasks.Length + " masks, " + Variants.Length + " models (prefabs wait for " + SharedMaterialPath + ")");
        }

        // ------------------------------------------------------------------ sources

        /// <summary>Every file the folder's SHA256SUMS lists, re-hashed (the copy is verbatim; a changed file is refused).</summary>
        public static void VerifySources()
        {
            if (!File.Exists(SourceSums)) throw new FileNotFoundException(LogPrefix + "SHA256SUMS missing — copy the AP-040 v001 sources first", SourceSums);
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in File.ReadAllLines(SourceSums))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int space = line.IndexOf(' ');
                if (space != 64) throw new InvalidOperationException(LogPrefix + "malformed SHA256SUMS line: " + line);
                string expected = line.Substring(0, 64);
                string name = line.Substring(space).Trim().TrimStart('*');
                string path = SourceFolder + "/" + name;
                if (!File.Exists(path)) throw new FileNotFoundException(LogPrefix + "listed source missing", path);
                string actual = Sha256(path);
                if (actual != expected) throw new InvalidOperationException(LogPrefix + name + " sha256 " + actual + " ≠ SHA256SUMS " + expected + " — the copy must stay verbatim");
                listed.Add(name);
            }

            var required = new List<string> { Path.GetFileName(SocketsJson) };
            foreach (Variant v in Variants) required.Add(v.Name + ".fbx");
            foreach (string mask in EmissiveMasks) required.Add(Path.GetFileName(mask));
            foreach (string name in required)
            {
                if (!listed.Contains(name)) throw new InvalidOperationException(LogPrefix + name + " is not listed in " + SourceSums);
            }
        }

        public static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static Material RequireMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                throw new InvalidOperationException(LogPrefix + "shared AP-040 material missing: " + path + " — S-02 owns the AP-040 shared atlas home (" +
                                                    SharedMaterialFolder + ", C-00 decision); land it before building the weapon attachments");
            }

            return material;
        }

        /// <summary>magOnPart: the AP-021 model carries part_mag (the Mag add-on rides it to the hand on reload).</summary>
        private static bool HasPartMag(ClassicWeaponId id, WeaponDto w)
        {
            GameObject model = ArtAp021WeaponBuilder.ModelFor(id);
            if (model == null)
            {
                throw new InvalidOperationException(LogPrefix + "AP-021 model of " + w.weapon_id + " missing — run DD/Arsenal/Import AP-021 Classics first");
            }

            string modelPath = AssetDatabase.GetAssetPath(model);
            if (!string.IsNullOrEmpty(w.source_sha256) && File.Exists(modelPath) && Sha256(modelPath) != w.source_sha256)
            {
                Debug.LogWarning(LogPrefix + w.weapon_id + ": " + modelPath + " is not the FBX AP-040 measured (sockets.json source_sha256 " + w.source_sha256 +
                                 ") — the table may be stale; Wp104B4AttachSocketTests locks the frame");
            }

            return WeaponSocketContract.Find(model.transform, WeaponMagazineVisual.PartName) != null;
        }

        // ------------------------------------------------------------------ importers

        private static void ImportMask(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            if (importer == null) throw new FileNotFoundException(LogPrefix + "mask missing", path);

            bool changed = false;
            if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; changed = true; }
            if (importer.sRGBTexture) { importer.sRGBTexture = false; changed = true; }
            if (importer.alphaSource != TextureImporterAlphaSource.None) { importer.alphaSource = TextureImporterAlphaSource.None; changed = true; }
            if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
            if (importer.maxTextureSize != 2048) { importer.maxTextureSize = 2048; changed = true; }
            if (importer.isReadable) { importer.isReadable = false; changed = true; }
            if (changed) importer.SaveAndReimport();
        }

        /// <summary>The AP-021 weapon import (file scale, no axis bake — ADR-042-1 hand-held rule) with no materials imported.</summary>
        private static void ImportModel(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as ModelImporter;
            }

            if (importer == null) throw new FileNotFoundException(LogPrefix + "model importer missing", path);

            bool changed = false;
            if (importer.globalScale != 1f) { importer.globalScale = 1f; changed = true; }
            if (!importer.useFileScale) { importer.useFileScale = true; changed = true; }
            if (importer.bakeAxisConversion) { importer.bakeAxisConversion = false; changed = true; }
            if (importer.importNormals != ModelImporterNormals.Import) { importer.importNormals = ModelImporterNormals.Import; changed = true; }
            if (importer.importTangents != ModelImporterTangents.CalculateMikk) { importer.importTangents = ModelImporterTangents.CalculateMikk; changed = true; }
            if (importer.meshCompression != ModelImporterMeshCompression.Off) { importer.meshCompression = ModelImporterMeshCompression.Off; changed = true; }
            if (importer.isReadable) { importer.isReadable = false; changed = true; }
            if (importer.addCollider) { importer.addCollider = false; changed = true; }
            if (importer.importAnimation) { importer.importAnimation = false; changed = true; }
            if (importer.animationType != ModelImporterAnimationType.None) { importer.animationType = ModelImporterAnimationType.None; changed = true; }
            if (importer.importBlendShapes) { importer.importBlendShapes = false; changed = true; }
            if (importer.importCameras) { importer.importCameras = false; changed = true; }
            if (importer.importLights) { importer.importLights = false; changed = true; }
            if (importer.importVisibility) { importer.importVisibility = false; changed = true; }
            if (!importer.preserveHierarchy) { importer.preserveHierarchy = true; changed = true; }
            if (importer.generateSecondaryUV) { importer.generateSecondaryUV = false; changed = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            if (changed) importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ prefabs

        private static GameObject BuildPrefab(Variant v, Material shared, Material glow)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(v.Name));
            if (model == null) throw new FileNotFoundException(LogPrefix + "model missing", FbxPath(v.Name));
            Transform modelRoot = model.transform;

            var root = new GameObject(v.Name);
            try
            {
                int lods = 0;
                var sockets = new HashSet<string>(StringComparer.Ordinal);
                bool mount = false;
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (t == modelRoot) continue;
                    // Pose in the model asset's space (its root at the origin): a file-scale on the importer root stays baked into the child.
                    Vector3 position = Quaternion.Inverse(modelRoot.rotation) * (t.position - modelRoot.position);
                    Quaternion rotation = Quaternion.Inverse(modelRoot.rotation) * t.rotation;
                    var filter = t.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        int lod = AuthoredWeaponModel.LodIndexOf(t.name);
                        if (lod < 0) throw new InvalidOperationException(LogPrefix + v.Name + ": mesh '" + t.name + "' has no _LOD0.._LOD2 suffix");
                        if ((lods & (1 << lod)) != 0) throw new InvalidOperationException(LogPrefix + v.Name + ": two meshes for LOD" + lod);
                        // The FBX slot order is M_AP040_Shared, M_AP040_Glow (E4 / E5); element_capsule_V03 paints every face with slot 0,
                        // so its mesh may come in with one submesh.
                        int slots = filter.sharedMesh.subMeshCount;
                        if (slots < 1 || slots > v.MaterialSlots)
                        {
                            throw new InvalidOperationException(LogPrefix + v.Name + ": " + t.name + " has " + slots + " submeshes, the card says at most " +
                                                                v.MaterialSlots + " material slots (M_AP040_Shared" + (v.MaterialSlots == 2 ? " + M_AP040_Glow" : "") + ")");
                        }

                        lods |= 1 << lod;
                        var go = new GameObject(t.name);
                        go.transform.SetParent(root.transform, false);
                        go.transform.localPosition = position;
                        go.transform.localRotation = rotation;
                        go.transform.localScale = t.lossyScale;
                        go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                        var renderer = go.AddComponent<MeshRenderer>();
                        renderer.sharedMaterials = slots == 2 ? new[] { shared, glow } : new[] { shared };
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                        continue;
                    }

                    if (!t.name.StartsWith("SOCKET_", StringComparison.Ordinal)) continue;
                    if (t.name == v.MountSocket)
                    {
                        // The mount marker sits at the origin: the prefab root is the mount pivot, the marker is not kept (a second
                        // SOCKET_Muzzle / SOCKET_Mag under the weapon's own socket would shadow it in WeaponSocketContract.Find).
                        if (position.sqrMagnitude > 1e-8f) throw new InvalidOperationException(LogPrefix + v.Name + ": mount " + t.name + " is off the origin " + position.ToString("F4"));
                        mount = true;
                        continue;
                    }

                    if (!sockets.Add(t.name)) throw new InvalidOperationException(LogPrefix + v.Name + ": two " + t.name);
                    var socket = new GameObject(t.name);
                    socket.transform.SetParent(root.transform, false);
                    socket.transform.localPosition = position;
                    socket.transform.localRotation = Quaternion.identity;   // attachment frame: +Z down the barrel (sockets.json rotation 0)
                    socket.transform.localScale = Vector3.one;
                }

                if (lods != 7) throw new InvalidOperationException(LogPrefix + v.Name + ": expected LOD0, LOD1 and LOD2 meshes (mask " + lods + ")");
                if (!mount) throw new InvalidOperationException(LogPrefix + v.Name + ": the FBX has no mount marker " + v.MountSocket);
                foreach (string s in v.Sockets)
                {
                    if (!sockets.Contains(s)) throw new InvalidOperationException(LogPrefix + v.Name + ": the FBX carries no " + s + " (CARD_*.json lists it)");
                }

                string frame = FixFrame(v, root.transform);
                Bounds b = LodBounds(root.transform, 0);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(v.Name), out bool success);
                if (!success || saved == null) throw new InvalidOperationException(LogPrefix + "SaveAsPrefabAsset failed for " + PrefabPath(v.Name));
                Debug.Log(LogPrefix + v.Name + " LOD0 bounds " + b.min.ToString("F4") + "–" + b.max.ToString("F4") + " sockets [" + string.Join(", ", sockets) + "]" + frame);
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Plan §4.3 frame check. Unity's FBX import mirrors X against AP-040's Blender authoring frame (the card's [x, z, −y]); the X-symmetric
        /// families do not care, but E4 must sit on the weapon's right: a capsule that lands on −X is turned 180° about Y (E4 is symmetric in Z).
        /// </summary>
        private static string FixFrame(Variant v, Transform root)
        {
            if (!v.ExtendsPlusX) return "";
            Bounds b = LodBounds(root, 0);
            string note = "";
            if (b.center.x < 0f)
            {
                Quaternion turn = Quaternion.Euler(0f, 180f, 0f);
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform c = root.GetChild(i);
                    c.localPosition = turn * c.localPosition;
                    bool socket = c.name.StartsWith("SOCKET_", StringComparison.Ordinal);
                    c.localRotation = socket ? Quaternion.identity : turn * c.localRotation;
                }

                b = LodBounds(root, 0);
                note = " (frame: turned 180° about Y, the import put it on −X)";
            }

            if (b.min.x < -0.002f || b.max.x <= 0.05f)
            {
                throw new InvalidOperationException(LogPrefix + v.Name + ": must extend along +X, LOD0 bounds " + b.min.ToString("F4") + "–" + b.max.ToString("F4"));
            }

            return note;
        }

        /// <summary>The AABB of one LOD's meshes in <paramref name="root"/>'s space (exact mesh bounds through each child's matrix).</summary>
        public static Bounds LodBounds(Transform root, int lod)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || AuthoredWeaponModel.LodIndexOf(f.name) != lod) continue;
                Matrix4x4 m = root.worldToLocalMatrix * f.transform.localToWorldMatrix;
                Bounds mb = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) bounds = new Bounds(p, Vector3.zero);
                    else bounds.Encapsulate(p);
                    any = true;
                }
            }

            if (!any) throw new InvalidOperationException(LogPrefix + root.name + ": no LOD" + lod + " mesh");
            return bounds;
        }

        private static void Assign(WeaponAttachLibrary library, Variant v, GameObject prefab)
        {
            switch (v.Family)
            {
                case "E1": library.e1 = prefab; break;
                case "E2": library.e2[v.Index] = prefab; break;
                case "E3": library.e3[v.Index] = prefab; break;
                case "E4": library.e4[v.Index] = prefab; break;
                case "E5": library.e5[v.Index] = prefab; break;
                case "E6": library.e6 = prefab; break;
                default: throw new InvalidOperationException(LogPrefix + "unknown family " + v.Family);
            }
        }

        // ------------------------------------------------------------------ rows and bridges

        private static WeaponAttachLibrary.Row BuildRow(WeaponDto w, Dictionary<ClassicWeaponId, bool> parted, ref int bridges)
        {
            WeaponIdMigration.TryGetRuntimeId(w.weapon_id, out ClassicWeaponId id);
            var row = new WeaponAttachLibrary.Row
            {
                id = id,
                key = w.weapon_id,
                mag = PoseOf(w.SOCKET_Mag, w.weapon_id + " SOCKET_Mag"),
                muzzle = PoseOf(w.SOCKET_Muzzle, w.weapon_id + " SOCKET_Muzzle"),
                side = PoseOf(w.SOCKET_Side, w.weapon_id + " SOCKET_Side"),
                magOnPart = parted[id],
                combos = new AttachCombo[WeaponAttachLibrary.TwinVariants * WeaponAttachLibrary.RingVariants],
            };

            var bridgeOf = new Dictionary<int, (Mesh mesh, AdapterDto adapter)>();
            for (int i = 0; i < w.combinations.Length; i++)
            {
                ComboDto c = w.combinations[i];
                string at = w.weapon_id + " combination " + i;
                Mesh bridge = null;
                if (c.adapter.required)
                {
                    // to_z = −0.008 − the E3 drop, measured per E5 variant: one mesh per (weapon, E5 variant), the same for every E3 size.
                    if (bridgeOf.TryGetValue(c.E5_variant, out var made))
                    {
                        if (!SameBridge(made.adapter, c.adapter)) throw new InvalidOperationException(LogPrefix + at + ": the bridge differs between E3 sizes of E5 v" + c.E5_variant);
                        bridge = made.mesh;
                    }
                    else
                    {
                        bridge = WriteBridge(w.weapon_id, c.E5_variant, c.adapter);
                        bridgeOf[c.E5_variant] = (bridge, c.adapter);
                        bridges++;
                    }
                }

                // Model-root frame like the table (secondary / power muzzle absolute, the offsets relative to SOCKET_Muzzle).
                row.combos[WeaponAttachLibrary.ComboIndex(c.E3_variant, c.E5_variant)] = new AttachCombo
                {
                    e3 = (byte)c.E3_variant,
                    e5 = (byte)c.E5_variant,
                    e3Offset = Vec(c.E3_offset_unity_m, at + " E3_offset_unity_m"),
                    e5Offset = Vec(c.E5_offset_unity_m, at + " E5_offset_unity_m"),
                    secondaryMuzzle = Vec(c.SOCKET_SecondaryMuzzle.position_unity_m, at + " SOCKET_SecondaryMuzzle"),
                    powerMuzzle = Vec(c.SOCKET_PowerMuzzle.position_unity_m, at + " SOCKET_PowerMuzzle"),
                    e5Scale = c.E5_uniformScale,
                    bridge = bridge,
                };
            }

            return row;
        }

        private static bool SameBridge(AdapterDto a, AdapterDto b)
        {
            if (Mathf.Abs(a.width_m - b.width_m) > 1e-6f || Mathf.Abs(a.rear_y_m - b.rear_y_m) > 1e-6f || Mathf.Abs(a.from_z_m - b.from_z_m) > 1e-6f ||
                Mathf.Abs(a.to_z_m - b.to_z_m) > 1e-6f || a.rails_x_m.Length != b.rails_x_m.Length) return false;
            for (int i = 0; i < a.rails_x_m.Length; i++)
            {
                if (Mathf.Abs(a.rails_x_m[i] - b.rails_x_m[i]) > 1e-6f) return false;
            }

            return true;
        }

        private static Mesh WriteBridge(string key, int ring, AdapterDto adapter)
        {
            string path = BridgePath(key, ring);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            FillBridge(mesh, adapter.rails_x_m, adapter.width_m, adapter.rear_y_m, adapter.from_z_m, adapter.to_z_m);
            mesh.name = Path.GetFileNameWithoutExtension(path);
            if (create) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// The rear-drop bridge in SOCKET_Muzzle's frame (AP040_attach_fit.py: one square beam per rail from muzzle + (x, rear_y, from_z) to
        /// muzzle + (x, rear_y, to_z) in Blender): Blender [x, y, z] → Unity [x, z, −y], so each rail is a box at x, centred on Unity z = −rear_y,
        /// spanning y from_z..to_z, width × width in x and z. Flat-shaded, UVs inside <see cref="BridgeAtlasCell"/>.
        /// </summary>
        public static void FillBridge(Mesh mesh, float[] railsX, float width, float rearY, float fromZ, float toZ)
        {
            var vertices = new List<Vector3>(24 * railsX.Length);
            var normals = new List<Vector3>(24 * railsX.Length);
            var uvs = new List<Vector2>(24 * railsX.Length);
            var triangles = new List<int>(36 * railsX.Length);
            float half = width * 0.5f;
            foreach (float x in railsX)
            {
                var center = new Vector3(x, (fromZ + toZ) * 0.5f, -rearY);
                var extents = new Vector3(half, Mathf.Abs(toZ - fromZ) * 0.5f, half);
                AddFace(center, extents, Vector3.right, Vector3.up, Vector3.forward, vertices, normals, uvs, triangles);
                AddFace(center, extents, Vector3.left, Vector3.up, Vector3.forward, vertices, normals, uvs, triangles);
                AddFace(center, extents, Vector3.up, Vector3.right, Vector3.forward, vertices, normals, uvs, triangles);
                AddFace(center, extents, Vector3.down, Vector3.right, Vector3.forward, vertices, normals, uvs, triangles);
                AddFace(center, extents, Vector3.forward, Vector3.right, Vector3.up, vertices, normals, uvs, triangles);
                AddFace(center, extents, Vector3.back, Vector3.right, Vector3.up, vertices, normals, uvs, triangles);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        private static void AddFace(Vector3 center, Vector3 extents, Vector3 normal, Vector3 u, Vector3 v,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            Vector3 c = center + Vector3.Scale(normal, extents);
            Vector3 du = Vector3.Scale(u, extents), dv = Vector3.Scale(v, extents);
            int start = vertices.Count;
            vertices.Add(c - du - dv);
            vertices.Add(c - du + dv);
            vertices.Add(c + du + dv);
            vertices.Add(c + du - dv);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            uvs.Add(BridgeAtlasCell + new Vector2(-BridgeUvHalf, -BridgeUvHalf));
            uvs.Add(BridgeAtlasCell + new Vector2(-BridgeUvHalf, BridgeUvHalf));
            uvs.Add(BridgeAtlasCell + new Vector2(BridgeUvHalf, BridgeUvHalf));
            uvs.Add(BridgeAtlasCell + new Vector2(BridgeUvHalf, -BridgeUvHalf));
            // Unity's front face: Cross(b − a, c − a) along the outward normal.
            bool front = Vector3.Dot(Vector3.Cross(vertices[start + 1] - vertices[start], vertices[start + 2] - vertices[start]), normal) > 0f;
            if (front)
            {
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            else
            {
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
            }
        }

        // ------------------------------------------------------------------ assets

        private static WeaponAttachLibrary LoadOrCreateLibrary()
        {
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(LibraryPath);
            if (existing != null)
            {
                if (existing is WeaponAttachLibrary library) return library;
                throw new InvalidOperationException(LogPrefix + LibraryPath + " is a " + existing.GetType().Name + ", not a WeaponAttachLibrary");
            }

            EnsureFolder(Path.GetDirectoryName(LibraryPath)?.Replace('\\', '/'));
            var created = ScriptableObject.CreateInstance<WeaponAttachLibrary>();
            created.name = WeaponAttachLibrary.ResourcePath;
            AssetDatabase.CreateAsset(created, LibraryPath);
            return created;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
