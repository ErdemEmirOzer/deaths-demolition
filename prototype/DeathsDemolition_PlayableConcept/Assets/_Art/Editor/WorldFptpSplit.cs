using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Prototype.Core;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-092 A (R3 #8, S-05; C-00 decision: split, never delete) · World_V6_Greybox's never-top-down-visible visuals move into a second
    /// scene, <see cref="SceneName"/>, that <see cref="WorldFptpOnlyLoader"/> loads only for TP / FP (and the menu backdrop).
    /// <para><b>Unit.</b> The outermost prefab instance root, else the nearest LODGroup root, else the renderer's own GameObject — all
    /// under the world root. Key = the path from the world root + "@x,y,z" (cm), "#n" for exact duplicates; fingerprint = family,
    /// renderer / triangle counts, rotation, scale, mode. Keys do not change when the unit sits in the FPTP scene (the FPTP root mirrors
    /// the world root's container path), so the audit reads a split world the same as a whole one (<see cref="OpenMerged"/>).</para>
    /// <para><b>Modes.</b> <see cref="Mode.Move"/>: the unit is visual only — it moves. <see cref="Mode.Proxy"/>: a prefab / LOD unit
    /// with colliders — the world keeps a plain GameObject of the same name, transform and collider tree (copied components), the
    /// instance moves with its colliders disabled. <see cref="Mode.Twin"/>: a plain GameObject whose renderer shares it with colliders
    /// or children — its MeshRenderer stays (disabled; the surface tagging reads its material) and a renderer twin goes to FPTP.
    /// Colliders, NavMesh components, scripts, lights and probes never move.</para>
    /// <para><b>Pins</b> (<see cref="StaticPin"/>): what the top-down runtime reads by renderer — the occlusion fader's hierarchy
    /// (CameraOccluder layer, CameraOccluderClass, OccluderFadeState), tier groups and lighting / reflection components above the unit,
    /// the start-up scanners' names (LanternFlames: lantern; AtmosphereSystem: sakura, bamboo, momiji, maple, water, plaza), water /
    /// GroundTiles / SunDisc shaders, disabled or inactive renderers, non-MeshRenderers, any other component in the unit; lamps (a Light in or
    /// above the unit, a kit SOCKET_ anchor, a layout lamp by its Lamp_ name — WP-092 lead 08.10) and fade shells (OccluderFadeState above).</para>
    /// <para><b>Freshness.</b> The list carries the audit's <see cref="ContentHash"/> (units, pins, roof colliders, NavMesh, sun, rig,
    /// rule version). The builder splits only when the built world hashes the same: content added or moved after the audit leaves the
    /// world whole (top-down never loses a pixel to a stale list) until TdVisibilityAudit runs again.</para>
    /// </summary>
    public static class WorldFptpSplit
    {
        public const string SceneName = WorldFptpOnlyLoader.DefaultSceneName;
        public const string ScenePath = "Assets/_Art/Scenes/" + SceneName + ".unity";
        /// <summary>The committed visibility list (TdVisibilityAudit writes it; the builder reads it).</summary>
        public const string ListPath = GreyboxV6Builder.DataFolder + "/fptp-only-v6.json";
        /// <summary>What the last build actually moved (the builder writes it with the two scenes; <see cref="OpenMerged"/> reads it).</summary>
        public const string AppliedPath = GreyboxV6Builder.DataFolder + "/fptp-only-v6.applied.json";
        public const string RootName = Ap005WorldBootstrap.RootName + " (FPTP Only)";
        public const string NoSplitFlag = "-ddNoFptpSplit";
        /// <summary>The built world's hash input when a list is stale (diagnosis).</summary>
        public const string BuiltHashInputPath = "Library/FptpHash/built-hash-input.txt";
        public const string ForceFlag = "-ddFptpForce";
        public const string Schema = "dd.fptp-only/v1";
        /// <summary>Bump when the unit, key, pin, mode or hash rules change: every older list is then stale.</summary>
        public const int RuleVersion = 2;   // 2: the layout root pinned (WP-092 lead 08.10)

        public enum Mode { Move, Proxy, Twin }

        [Serializable]
        public sealed class Entry
        {
            public string key;
            public string family;
            public string fingerprint;
            public string mode;
            public int renderers;
            public int triangles;
        }

        [Serializable]
        public sealed class VisibilityList
        {
            public string schema = Schema;
            public int ruleVersion = RuleVersion;
            public string world = GreyboxV6Builder.ScenePath;
            public string contentHash;
            public string rule;
            public int units;
            public int moved;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        public sealed class AppliedEntry
        {
            public string key;
            public string mode;
            public string parent;
            public int sibling;
        }

        [Serializable]
        public sealed class Applied
        {
            public string schema = Schema + "/applied";
            public string contentHash;
            public string reason;
            public List<AppliedEntry> entries = new List<AppliedEntry>();
        }

        public sealed class Unit
        {
            public Transform Root;
            public string Key, Path, Family, Fingerprint, Pin;
            public Mode Mode;
            public bool HasColliders;
            public int Triangles;
            public readonly List<Renderer> Renderers = new List<Renderer>();
        }

        public struct Report
        {
            public bool Applied;
            public string Reason;
            public int Listed, Moved, Proxies, Twins, Missing, Pinned;
            public string ContentHash;

            public override string ToString()
            {
                return Applied
                    ? "FPTP split " + Moved + " units (" + Proxies + " collider proxies, " + Twins + " twins; " + Missing + " stale / " + Pinned + " pinned of " + Listed + ") -> " + ScenePath
                    : "FPTP split not applied (" + Reason + "); " + ScenePath + " empty";
            }
        }

        // ------------------------------------------------------------------------------------------------ units, keys, pins

        private static readonly HashSet<string> VisualComponents = new HashSet<string>(StringComparer.Ordinal) { "Transform", "MeshFilter", "MeshRenderer", "LODGroup" };
        private static readonly HashSet<string> ColliderComponents = new HashSet<string>(StringComparer.Ordinal) { "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider" };
        /// <summary>Components above a unit that manage its renderers at runtime (the fade, the tier toggles). Not VolumetricLight (it sits
        /// on the dress root and only publishes shader globals), PlanarReflection or SunDisc (no child renderer access; their receiver /
        /// disc are pinned by shader).</summary>
        private static readonly HashSet<string> ManagerAncestors = new HashSet<string>(StringComparer.Ordinal)
        {
            "CameraOccluderClass", "OccluderFadeState", "AtmosphereTierGroup", "WorldFptpOnlyLoader"
        };
        /// <summary>Names the top-down start-up scanners select renderers by (LanternFlames, AtmosphereSystem, WaterLook).</summary>
        public static readonly string[] ScannerTokens = { "lantern", "sakura", "bamboo", "momiji", "maple", "water", "plaza" };
        /// <summary>Shaders whose renderers top-down systems read (water look, the planar-reflection receiver, the sun disc).</summary>
        public static readonly string[] PinnedShaders = { "DD/Water v1", "DD/Water v2", "DD/GroundTiles", "DD/SunDisc" };
        /// <summary>WP-092 lead (08.10): the kit's light / flame anchors (SOCKET_Light, SOCKET_Flame — LanternFlames' pool reads them) and the
        /// layout's lamps (WorldV5Layout: "Lamp_&lt;id&gt;_&lt;light&gt;" under "Layout v5 (WP-092)", lit, emissive or unlit) never move.</summary>
        public const string SocketPrefix = "SOCKET_", LampPrefix = "Lamp_";

        /// <summary>Every renderer-bearing unit under <paramref name="worldRoot"/>, in hierarchy order, keyed, fingerprinted and classified.</summary>
        public static List<Unit> Units(Transform worldRoot)
        {
            var units = new List<Unit>();
            var byRoot = new Dictionary<Transform, Unit>();
            foreach (Renderer r in worldRoot.GetComponentsInChildren<Renderer>(true))
            {
                Transform root = UnitRoot(r.transform, worldRoot);
                if (!byRoot.TryGetValue(root, out Unit u))
                {
                    u = new Unit { Root = root };
                    byRoot.Add(root, u);
                    units.Add(u);
                }

                u.Renderers.Add(r);
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Unit u in units)
            {
                u.Path = RelativePath(u.Root, worldRoot);
                string key = u.Path + "@" + P(u.Root.position);
                if (seen.TryGetValue(key, out int n))
                {
                    seen[key] = n + 1;
                    key += "#" + (n + 1);
                }
                else
                {
                    seen[key] = 0;
                }

                u.Key = key;
                bool hierarchy = IsHierarchyUnit(u.Root);
                u.HasColliders = hierarchy ? u.Root.GetComponentsInChildren<Collider>(true).Length > 0 : u.Root.GetComponents<Collider>().Length > 0;
                u.Mode = hierarchy ? (u.HasColliders ? Mode.Proxy : Mode.Move) : (u.Root.childCount == 0 && !u.HasColliders ? Mode.Move : Mode.Twin);
                u.Family = FamilyOf(u);
                u.Triangles = 0;
                foreach (Renderer r in u.Renderers) u.Triangles += Triangles(r);
                u.Fingerprint = u.Family + "|r" + u.Renderers.Count + "|t" + u.Triangles + "|e" + E(u.Root.eulerAngles) + "|s" + P(u.Root.lossyScale) + "|" + u.Mode;
            }

            foreach (Unit u in units) u.Pin = StaticPin(u, worldRoot, byRoot);
            return units;
        }

        public static bool IsHierarchyUnit(Transform root) => PrefabUtility.IsOutermostPrefabInstanceRoot(root.gameObject) || root.GetComponent<LODGroup>() != null;

        public static Transform UnitRoot(Transform t, Transform worldRoot)
        {
            GameObject outer = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
            if (outer != null && outer.transform != worldRoot && outer.transform.IsChildOf(worldRoot)) return outer.transform;
            for (Transform p = t; p != null && p != worldRoot; p = p.parent)
                if (p.GetComponent<LODGroup>() != null) return p;
            return t;
        }

        /// <summary>The reason a unit must stay in the world scene whatever the camera sees (null = it may move).</summary>
        public static string StaticPin(Unit u, Transform worldRoot, Dictionary<Transform, Unit> byRoot)
        {
            if (u.Root == worldRoot) return "root";
            foreach (Renderer r in u.Renderers)
            {
                if (!(r is MeshRenderer)) return "renderer:" + r.GetType().Name;
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) return "inactive";
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (Array.IndexOf(PinnedShaders, m.shader != null ? m.shader.name : "") >= 0) return "shader:" + m.shader.name;
                    string token = ScannerToken(m.name);
                    if (token != null) return "scanner:" + token;
                }

                string own = ScannerToken(r.name);
                if (own != null) return "scanner:" + own;
            }

            string rootToken = ScannerToken(u.Root.name) ?? ScannerToken(u.Family);
            if (rootToken != null) return "scanner:" + rootToken;

            // lamps: a Light or a kit socket in the unit's subtree, a Light or a layout lamp's name above it
            foreach (Transform t in u.Root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<Light>() != null) return "light";
                if (t.name.StartsWith(SocketPrefix, StringComparison.Ordinal)) return "socket";
            }

            for (Transform p = u.Root; p != null && p != worldRoot; p = p.parent)
            {
                if (p.GetComponent<Light>() != null) return "light";
                if (p.name.StartsWith(LampPrefix, StringComparison.Ordinal)) return "lamp";
                if (p.name == WorldV5Layout.RootName) return "layout";   // WP-092 lead 08.10: the plan's content is placed to be seen (interiors under faded roofs)
            }

            for (Transform p = u.Root; p != null && p != worldRoot; p = p.parent)
            {
                if (p.gameObject.layer == PrototypeLayers.CameraOccluder) return "occluder";
                foreach (Component c in p.GetComponents<Component>())
                    if (c != null && ManagerAncestors.Contains(c.GetType().Name)) return "managed:" + c.GetType().Name;
                if (p != u.Root && byRoot.ContainsKey(p) && IsHierarchyUnit(p)) return "nested";
            }

            Component[] components = IsHierarchyUnit(u.Root) ? u.Root.GetComponentsInChildren<Component>(true) : u.Root.GetComponents<Component>();
            foreach (Component c in components)
            {
                if (c == null) return "component:missing-script";
                string name = c.GetType().Name;
                if (VisualComponents.Contains(name)) continue;
                if (ColliderComponents.Contains(name))
                {
                    if (c.gameObject.layer == PrototypeLayers.CameraOccluder) return "occluder";
                    continue;
                }

                return "component:" + name;
            }

            if (IsHierarchyUnit(u.Root))
            {
                // a hierarchy unit carries its subtree: another unit inside it would move with it
                foreach (Renderer r in u.Root.GetComponentsInChildren<Renderer>(true))
                    if (!u.Renderers.Contains(r)) return "contains-units";
                foreach (Transform t in u.Root.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.layer == PrototypeLayers.CameraOccluder) return "occluder";
            }

            return null;
        }

        public static string ScannerToken(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (string token in ScannerTokens)
                if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return token;
            return null;
        }

        public static string FamilyOf(Unit u)
        {
            if (PrefabUtility.IsOutermostPrefabInstanceRoot(u.Root.gameObject))
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(u.Root.gameObject);
                if (source != null) return source.name;
            }

            foreach (Renderer r in u.Renderers)
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) return f.sharedMesh.name;
            }

            return u.Root.name;
        }

        public static int Triangles(Renderer r)
        {
            MeshFilter f = r.GetComponent<MeshFilter>();
            Mesh m = f != null ? f.sharedMesh : null;
            if (m == null) return 0;
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++)
                if (m.GetTopology(s) == MeshTopology.Triangles) n += m.GetIndexCount(s) / 3;
            return (int)n;
        }

        public static string RelativePath(Transform t, Transform root)
        {
            if (t == root) return "";
            var parts = new List<string>();
            for (Transform p = t; p != null && p != root; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static string P(Vector3 v) => F2(v.x) + "," + F2(v.y) + "," + F2(v.z);

        private static string E(Vector3 euler) => F1(Mathf.Repeat(euler.x, 360f)) + "," + F1(Mathf.Repeat(euler.y, 360f)) + "," + F1(Mathf.Repeat(euler.z, 360f));

        private static string F2(float v)
        {
            float r = Mathf.Round(v * 100f) / 100f;
            return (r == 0f ? 0f : r).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string F1(float v)
        {
            float r = Mathf.Round(v * 10f) / 10f;
            if (r >= 360f) r -= 360f;
            return (r == 0f ? 0f : r).ToString("0.0", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------------------------------------ content hash

        /// <summary>
        /// What the audit's verdicts depend on, hashed: every unit (key, fingerprint, pin), the CameraOccluder colliders (the fade),
        /// the NavMesh (the pose grid), the effective sun (the shadow rule), the top-down rig and <see cref="RuleVersion"/>.
        /// </summary>
        public static string ContentHash(Transform worldRoot, List<Unit> units, Vector3 sunEuler, string rig) => ContentHash(worldRoot, units, sunEuler, rig, out _);

        /// <param name="canonical">The hashed text (a stale list's diagnosis: diff the builder's against the audit's).</param>
        public static string ContentHash(Transform worldRoot, List<Unit> units, Vector3 sunEuler, string rig, out string canonical)
        {
            var lines = new List<string>(units.Count + 128);
            foreach (Unit u in units) lines.Add("u " + u.Key + "|" + u.Fingerprint + "|" + (u.Pin ?? ""));
            foreach (Collider c in worldRoot.GetComponentsInChildren<Collider>(true))
                if (c.gameObject.layer == PrototypeLayers.CameraOccluder && c.enabled && c.gameObject.activeInHierarchy)
                    lines.Add("o " + RelativePath(c.transform, worldRoot) + "|" + P(c.bounds.center) + "|" + P(c.bounds.size));
            lines.Sort(StringComparer.Ordinal);
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            var nav = new Bounds();
            for (int i = 0; i < tri.vertices.Length; i++)
                if (i == 0) nav = new Bounds(tri.vertices[0], Vector3.zero);
                else nav.Encapsulate(tri.vertices[i]);
            var sb = new StringBuilder(lines.Count * 64);
            sb.Append("rule ").Append(RuleVersion).Append('\n');
            sb.Append("nav ").Append(tri.vertices.Length).Append(' ').Append(tri.indices.Length).Append(' ').Append(P(nav.min)).Append(' ').Append(P(nav.max)).Append('\n');
            sb.Append("sun ").Append(E(sunEuler)).Append('\n');
            sb.Append("rig ").Append(rig).Append('\n');
            foreach (string line in lines) sb.Append(line).Append('\n');
            canonical = sb.ToString();
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var hex = new StringBuilder(40);
                foreach (byte b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>The sun the shadow rule uses: the scene's baked sun if there is one, else the layout's (DEC-102).</summary>
        public static Vector3 EffectiveSunEuler()
        {
            Light sun = WorldAtmosphere.FindBakedSun();
            return sun != null ? sun.transform.eulerAngles : WorldAtmosphere.SunEulerFor(WorldLayoutKind.V6);
        }

        // ------------------------------------------------------------------------------------------------ list io

        public static VisibilityList ReadList(string path = ListPath)
        {
            if (!File.Exists(path)) return null;
            var list = JsonUtility.FromJson<VisibilityList>(File.ReadAllText(path));
            return list != null && list.schema == Schema ? list : null;
        }

        public static void WriteList(VisibilityList list, string path = ListPath)
        {
            list.entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            File.WriteAllText(path, JsonUtility.ToJson(list, true) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
        }

        public static Applied ReadApplied(string path = AppliedPath)
        {
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<Applied>(File.ReadAllText(path));
        }

        private static void WriteApplied(Applied applied, string path)
        {
            File.WriteAllText(path, JsonUtility.ToJson(applied, true) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
        }

        // ------------------------------------------------------------------------------------------------ split

        /// <summary>
        /// Moves the listed units of <paramref name="worldRoot"/> (scene <paramref name="main"/>) into <paramref name="fptp"/> under
        /// <see cref="RootName"/>; returns what it did and fills <paramref name="applied"/>. Nothing moves when the list is missing, of
        /// another rule version, or (unless <paramref name="force"/>) hashes other content. Entries whose unit is gone, changed or now
        /// pinned stay where they are. Deterministic: entries in key order, containers created on first use.
        /// </summary>
        public static Report Apply(Scene main, Transform worldRoot, Scene fptp, VisibilityList list, bool force, Applied applied)
        {
            var report = new Report();
            Physics.SyncTransforms();
            List<Unit> units = Units(worldRoot);
            report.ContentHash = ContentHash(worldRoot, units, EffectiveSunEuler(), RigSignature(), out string canonical);
            if (applied != null) applied.contentHash = report.ContentHash;
            Transform froot = EnsureFptpRoot(fptp, worldRoot);
            if (list == null) return Skip(report, applied, "no list at " + ListPath);
            if (list.ruleVersion != RuleVersion) return Skip(report, applied, "list rule v" + list.ruleVersion + " != v" + RuleVersion);
            if (!force && list.contentHash != report.ContentHash)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BuiltHashInputPath));
                File.WriteAllText(BuiltHashInputPath, canonical, new UTF8Encoding(false));
                return Skip(report, applied, "stale list: audited " + Short(list.contentHash) + ", built " + Short(report.ContentHash) + " - run TdVisibilityAudit (what differs: diff " +
                                             BuiltHashInputPath + " against the audit's td-visibility-hash-input.txt)");
            }

            var byKey = new Dictionary<string, Unit>(StringComparer.Ordinal);
            foreach (Unit u in units) byKey[u.Key] = u;
            var entries = new List<Entry>(list.entries);
            entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            report.Listed = entries.Count;

            // sibling indices before anything moves (the merge restores them)
            var plan = new List<(Unit unit, string parent, int sibling)>();
            foreach (Entry e in entries)
            {
                if (!byKey.TryGetValue(e.key, out Unit u) || u.Fingerprint != e.fingerprint) { report.Missing++; continue; }
                if (u.Pin != null) { report.Pinned++; continue; }
                plan.Add((u, RelativePath(u.Root.parent, worldRoot), u.Root.GetSiblingIndex()));
            }

            Scene active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(main);
            var containers = new Dictionary<string, Transform>(StringComparer.Ordinal) { { "", froot } };
            try
            {
                foreach ((Unit u, string parent, int sibling) in plan)
                {
                    Transform container = Container(u.Root.parent, worldRoot, containers);
                    switch (u.Mode)
                    {
                        case Mode.Move:
                            MoveInto(u.Root, container, fptp);
                            break;
                        case Mode.Proxy:
                            CollidersProxy(u.Root);
                            MoveInto(u.Root, container, fptp);
                            report.Proxies++;
                            break;
                        default:
                            Twin(u.Root, container, fptp);
                            report.Twins++;
                            break;
                    }

                    report.Moved++;
                    applied?.entries.Add(new AppliedEntry { key = u.Key, mode = u.Mode.ToString(), parent = parent, sibling = sibling });
                }
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }

            WorldFptpOnlyLoader loader = worldRoot.GetComponent<WorldFptpOnlyLoader>();
            if (report.Moved > 0)
            {
                if (loader == null) loader = worldRoot.gameObject.AddComponent<WorldFptpOnlyLoader>();
                loader.Configure(SceneName, report.Moved);
            }
            else if (loader != null)
            {
                UnityEngine.Object.DestroyImmediate(loader);
            }

            report.Applied = report.Moved > 0;
            report.Reason = report.Applied ? "fresh list" : "no listed unit matched";
            if (applied != null) applied.reason = report.Reason;
            if (!EditorSceneManager.IsPreviewScene(main)) EditorSceneManager.MarkSceneDirty(main);
            if (!EditorSceneManager.IsPreviewScene(fptp)) EditorSceneManager.MarkSceneDirty(fptp);
            return report;
        }

        private static Report Skip(Report report, Applied applied, string reason)
        {
            report.Reason = reason;
            if (applied != null) applied.reason = reason;
            Debug.LogWarning("[FPTP] " + reason + " - the world stays whole.");
            return report;
        }

        private static string Short(string hash) => string.IsNullOrEmpty(hash) ? "(none)" : hash.Substring(0, Math.Min(10, hash.Length));

        /// <summary>The top-down rig the audit poses use (FeelConfig camera), as hashed text.</summary>
        public static string RigSignature()
        {
            TdVisibilityAudit.Rig rig = TdVisibilityAudit.GameplayRig();
            return P(rig.Offset) + "|" + F2(rig.Fov) + "|" + F2(rig.FocusHeight) + "|" + F2(rig.LookAhead) + "|" + F2(TdVisibilityAudit.FovMarginDegrees) + "|" + F2(TdVisibilityAudit.GridStep);
        }

        private static Transform EnsureFptpRoot(Scene fptp, Transform worldRoot)
        {
            foreach (GameObject go in fptp.GetRootGameObjects())
                if (go.name == RootName) return go.transform;
            var root = new GameObject(RootName);
            ToScene(root, fptp);
            root.layer = worldRoot.gameObject.layer;
            root.transform.SetPositionAndRotation(worldRoot.position, worldRoot.rotation);
            root.transform.localScale = worldRoot.localScale;
            return root.transform;
        }

        /// <summary>The FPTP mirror of a world container (same names and local transforms down from the root), created on first use.</summary>
        private static Transform Container(Transform original, Transform worldRoot, Dictionary<string, Transform> containers)
        {
            string path = RelativePath(original, worldRoot);
            if (containers.TryGetValue(path, out Transform made)) return made;
            Transform parent = Container(original.parent, worldRoot, containers);
            var go = new GameObject(original.name) { layer = original.gameObject.layer };
            ToScene(go, parent.gameObject.scene);
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(original.gameObject));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = original.localPosition;
            go.transform.localRotation = original.localRotation;
            go.transform.localScale = original.localScale;
            containers.Add(path, go.transform);
            return go.transform;
        }

        private static void MoveInto(Transform t, Transform container, Scene fptp)
        {
            Vector3 lp = t.localPosition, ls = t.localScale;
            Quaternion lr = t.localRotation;
            t.SetParent(null, false);
            SceneManager.MoveGameObjectToScene(t.gameObject, fptp);
            t.SetParent(container, false);
            t.localPosition = lp;
            t.localRotation = lr;
            t.localScale = ls;
        }

        /// <summary>Proxy mode: a plain GameObject in the instance's place with its collider tree (copied components); the instance's colliders off.</summary>
        private static void CollidersProxy(Transform instance)
        {
            var proxy = new GameObject(instance.name);
            ToScene(proxy, instance.gameObject.scene);
            CopyGameObjectState(instance.gameObject, proxy);
            proxy.transform.SetParent(instance.parent, false);
            proxy.transform.localPosition = instance.localPosition;
            proxy.transform.localRotation = instance.localRotation;
            proxy.transform.localScale = instance.localScale;
            proxy.transform.SetSiblingIndex(instance.GetSiblingIndex());
            var mirrors = new Dictionary<Transform, Transform> { { instance, proxy.transform } };
            foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
            {
                Transform holder = Mirror(c.transform, mirrors);
                Component copy = holder.gameObject.AddComponent(c.GetType());
                EditorUtility.CopySerialized(c, copy);
                c.enabled = false;
            }
        }

        private static Transform Mirror(Transform original, Dictionary<Transform, Transform> mirrors)
        {
            if (mirrors.TryGetValue(original, out Transform made)) return made;
            Transform parent = Mirror(original.parent, mirrors);
            var go = new GameObject(original.name);
            ToScene(go, parent.gameObject.scene);
            CopyGameObjectState(original.gameObject, go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = original.localPosition;
            go.transform.localRotation = original.localRotation;
            go.transform.localScale = original.localScale;
            mirrors.Add(original, go.transform);
            return go.transform;
        }

        private static void ToScene(GameObject go, Scene scene)
        {
            if (go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
        }

        private static void CopyGameObjectState(GameObject from, GameObject to)
        {
            to.layer = from.layer;
            to.tag = from.tag;
            GameObjectUtility.SetStaticEditorFlags(to, GameObjectUtility.GetStaticEditorFlags(from));
            to.SetActive(from.activeSelf);
        }

        /// <summary>Twin mode: the renderer's copy goes to FPTP; the world keeps the GameObject, its colliders and children, renderer off.</summary>
        private static void Twin(Transform original, Transform container, Scene fptp)
        {
            var twin = new GameObject(original.name);
            ToScene(twin, fptp);
            CopyGameObjectState(original.gameObject, twin);
            twin.transform.SetParent(container, false);
            twin.transform.localPosition = original.localPosition;
            twin.transform.localRotation = original.localRotation;
            twin.transform.localScale = original.localScale;
            twin.AddComponent<MeshFilter>().sharedMesh = original.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer source = original.GetComponent<MeshRenderer>();
            MeshRenderer copy = twin.AddComponent<MeshRenderer>();
            EditorUtility.CopySerialized(source, copy);
            copy.enabled = true;
            source.enabled = false;
        }

        // ------------------------------------------------------------------------------------------------ merge (the whole world back)

        /// <summary>
        /// Opens the world as the builder made it before the split: World_V6_Greybox, then World_FPTPOnly's units put back where
        /// <see cref="AppliedPath"/> says they were (sibling order restored; proxies replaced by their instances, colliders as the proxy
        /// had them; twins folded back into their renderer). The FPTP scene is closed; nothing is saved. With no FPTP scene or an
        /// empty one this is OpenScene. For the audit and the world's content tests.
        /// </summary>
        public static Scene OpenMerged(string mainPath = GreyboxV6Builder.ScenePath)
        {
            Scene main = EditorSceneManager.OpenScene(mainPath, OpenSceneMode.Single);
            if (!File.Exists(ScenePath)) return main;
            Scene fptp = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Merge(main, fptp, ReadApplied());
            }
            finally
            {
                EditorSceneManager.CloseScene(fptp, true);
                SceneManager.SetActiveScene(main);
            }

            Physics.SyncTransforms();
            return main;
        }

        /// <summary>Folds <paramref name="fptp"/>'s units back into <paramref name="main"/>; returns how many.</summary>
        public static int Merge(Scene main, Scene fptp, Applied applied)
        {
            Transform worldRoot = FindRoot(main, Ap005WorldBootstrap.RootName), froot = FindRoot(fptp, RootName);
            if (worldRoot == null || froot == null) return 0;
            List<Unit> moved = Units(froot);
            if (moved.Count == 0) return 0;
            if (applied == null) throw new InvalidOperationException("[FPTP] " + ScenePath + " holds " + moved.Count + " units but " + AppliedPath + " is missing");
            var byKey = new Dictionary<string, Unit>(StringComparer.Ordinal);
            foreach (Unit u in moved) byKey[u.Key] = u;
            var order = new List<AppliedEntry>(applied.entries);
            order.Sort((a, b) => a.parent != b.parent ? string.CompareOrdinal(a.parent, b.parent) : a.sibling.CompareTo(b.sibling));
            int merged = 0;
            foreach (AppliedEntry e in order)
            {
                // an exact duplicate ("#n") may be numbered differently once its twin stayed behind
                if (!byKey.TryGetValue(e.key, out Unit u) && !byKey.TryGetValue(BaseKey(e.key), out u))
                    throw new InvalidOperationException("[FPTP] applied unit missing from " + ScenePath + ": " + e.key);
                byKey.Remove(u.Key);
                Transform parent = e.parent.Length == 0 ? worldRoot : worldRoot.Find(e.parent);
                if (parent == null) throw new InvalidOperationException("[FPTP] world container missing for " + e.key + ": " + e.parent);
                Transform t = u.Root;
                switch (e.mode)
                {
                    case nameof(Mode.Twin):
                    {
                        Transform source = parent.GetChild(e.sibling);
                        MeshRenderer r = source.GetComponent<MeshRenderer>();
                        if (source.name != t.name || r == null) throw new InvalidOperationException("[FPTP] twin source mismatch for " + e.key);
                        r.enabled = true;
                        UnityEngine.Object.DestroyImmediate(t.gameObject);
                        break;
                    }
                    case nameof(Mode.Proxy):
                    {
                        Transform proxy = parent.GetChild(e.sibling);
                        if (proxy.name != t.name || proxy.GetComponentInChildren<Renderer>(true) != null) throw new InvalidOperationException("[FPTP] proxy mismatch for " + e.key);
                        RestoreColliders(t, proxy);
                        UnityEngine.Object.DestroyImmediate(proxy.gameObject);
                        PutBack(t, parent, e.sibling, main);
                        break;
                    }
                    default:
                        PutBack(t, parent, e.sibling, main);
                        break;
                }

                merged++;
            }

            return merged;
        }

        private static string BaseKey(string key)
        {
            int hash = key.LastIndexOf('#');
            int at = key.LastIndexOf('@');
            return hash > at && at >= 0 ? key.Substring(0, hash) : key;
        }

        private static void RestoreColliders(Transform instance, Transform proxy)
        {
            Collider[] own = instance.GetComponentsInChildren<Collider>(true), copies = proxy.GetComponentsInChildren<Collider>(true);
            if (own.Length != copies.Length) throw new InvalidOperationException("[FPTP] collider count differs: " + instance.name);
            for (int i = 0; i < own.Length; i++) own[i].enabled = copies[i].enabled;
        }

        private static void PutBack(Transform t, Transform parent, int sibling, Scene main)
        {
            Vector3 lp = t.localPosition, ls = t.localScale;
            Quaternion lr = t.localRotation;
            t.SetParent(null, false);
            SceneManager.MoveGameObjectToScene(t.gameObject, main);
            t.SetParent(parent, false);
            t.localPosition = lp;
            t.localRotation = lr;
            t.localScale = ls;
            t.SetSiblingIndex(sibling);
        }

        public static Transform FindRoot(Scene scene, string name)
        {
            if (!scene.IsValid()) return null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == name) return go.transform;
            return null;
        }

        // ------------------------------------------------------------------------------------------------ builder entry

        /// <summary>
        /// GreyboxV6Builder, after the world is built, baked for NavMesh and saved: the split by the committed list (when
        /// <paramref name="split"/> and fresh, or <paramref name="force"/>), then both scenes saved and the applied record written.
        /// The FPTP scene is always written (empty when nothing moved) and stays open, so the optional bake lights both together.
        /// </summary>
        public static Report ApplyAfterBuild(Scene main, Transform worldRoot, bool split, bool force)
        {
            Scene fptp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(main);
            var applied = new Applied();
            Report report;
            if (split)
            {
                report = Apply(main, worldRoot, fptp, ReadList(), force, applied);
            }
            else
            {
                EnsureFptpRoot(fptp, worldRoot);
                report = new Report { Reason = NoSplitFlag };
                applied.reason = NoSplitFlag;
                WorldFptpOnlyLoader stale = worldRoot.GetComponent<WorldFptpOnlyLoader>();
                if (stale != null) UnityEngine.Object.DestroyImmediate(stale);
            }

            EditorSceneManager.SaveScene(fptp, ScenePath);
            EditorSceneManager.SaveScene(main);
            WriteApplied(applied, AppliedPath);
            Debug.Log("[FPTP] " + report);
            return report;
        }

        /// <summary>The release list entry right after World_V6_Greybox (GreyboxV6Builder.RegisterDevScene).</summary>
        public static void Register(List<EditorBuildSettingsScene> scenes)
        {
            scenes.RemoveAll(s => s.path == ScenePath);
            int at = scenes.FindIndex(s => s.path == GreyboxV6Builder.ScenePath);
            scenes.Insert(at >= 0 ? at + 1 : scenes.Count, new EditorBuildSettingsScene(ScenePath, true));
        }

        /// <summary>
        /// Batch: before the first split build — an empty World_FPTPOnly (the FPTP root only) and its release entry, so the build list
        /// and the scene exist from now on; the world scene is not touched (no loader: nothing loads it until the builder splits).
        /// </summary>
        public static void PrepareCli()
        {
            if (!File.Exists(ScenePath))
            {
                Scene main = EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);
                Transform worldRoot = FindRoot(main, Ap005WorldBootstrap.RootName);
                Scene fptp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EnsureFptpRoot(fptp, worldRoot);
                EditorSceneManager.SaveScene(fptp, ScenePath);
                WriteApplied(new Applied { reason = "prepared (no split yet)" }, AppliedPath);
                EditorSceneManager.CloseScene(fptp, true);
            }

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            Register(scenes);
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[FPTP] prepared " + ScenePath + " (release entry " + (scenes.FindIndex(s => s.path == ScenePath) + 1) + ")");
        }
    }
}
