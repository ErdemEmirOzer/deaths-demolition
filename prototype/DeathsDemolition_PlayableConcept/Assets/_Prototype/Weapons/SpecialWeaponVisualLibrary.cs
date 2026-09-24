using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-037 (AP-014) · the Special Weapon models as data: per weapon the pickup pose
    /// (lying on the ground) and the hand pose (authored in the socket_weapon_r frame:
    /// grip along local +Y, business end +Y, +Z up) as LOD0–2 meshes, one atlas material
    /// (M_sw_atlas, bronze — the gold band is the pickup reserve), plus the AP-014 use
    /// sheets for the VFX layer. Written by <c>ArtSpecialWeaponBuilder</c> into
    /// <c>Assets/_Art/Resources/DD_SpecialWeapons.asset</c>; read by
    /// <see cref="SpecialWeaponPickup"/> (ground) and <see cref="WeaponController"/>
    /// (hand). A weapon with no entry keeps the WP-007 placeholder primitives (Tide Jewel:
    /// no AP-014 model). Data only; no gameplay value lives here.
    ///
    /// v2 (WP-069 B4c, AP-021 P-07): an entry may carry the whole imported model
    /// (<see cref="Entry.model"/>: one FBX, ADR-042-1 frame — root at the grip, +Z barrel, +Y up —
    /// with the contract sockets, the moving parts <c>part_*</c> and their own materials). It wins
    /// over the AP-014 meshes: the same model goes into the hand (the classic rig path) and, laid
    /// on its side, onto the ground. LOD0 is the first-person mesh, LOD1/2 third person / top-down.
    /// </summary>
    public sealed class SpecialWeaponVisualLibrary : ScriptableObject
    {
        public const string Schema = "dd-special-weapon-visuals-v2";
        public const string ResourcePath = "DD_SpecialWeapons";
        public const int LodCount = 3;

        [Serializable]
        public sealed class Entry
        {
            public SpecialWeaponId id;
            public Mesh[] pickupLods = new Mesh[LodCount];
            public Mesh[] handLods = new Mesh[LodCount];
            public int[] pickupTriangles = new int[LodCount];
            public int[] handTriangles = new int[LodCount];

            /// <summary>WP-069 B4c: the AP-021 FBX root (prefab asset; instantiate with <see cref="InstantiateModel"/>, never mutate).</summary>
            public GameObject model;
            public string modelPackage = "";

            /// <summary>
            /// Rotation (Euler, degrees) that puts the imported content into the ADR-042-1 frame, applied by
            /// <see cref="InstantiateModel"/>. AP-021 v002 lands barrel-up (+Y) under the no-bake importer → (90, 0, 0)
            /// until P-07's export carries the frame itself (then zero).
            /// </summary>
            public Vector3 modelFrameEuler;
            public int[] modelTriangles = new int[LodCount];

            public bool HasModel => model != null;
            public bool HasPickup => HasModel || (pickupLods != null && pickupLods.Length > 0 && pickupLods[0] != null);
            public bool HasHand => HasModel || (handLods != null && handLods.Length > 0 && handLods[0] != null);
        }

        [Serializable]
        public sealed class Sheet
        {
            /// <summary>Usage id the VFX layer binds by (SpecialWeaponUsageIds families): sw_kagura_wave, sw_ofuda_dart, sw_ofuda_web, sw_ofuda_collapse.</summary>
            public string usage;
            public Texture2D texture;
            public int columns = 1;
            public int rows = 1;
            public float fps;
            public float durationSeconds;
            public bool loop;
            public float worldSizeMetres;
        }

        public string schema = Schema;
        public string package = "AP-014";
        public Material material;
        public Entry[] entries = Array.Empty<Entry>();
        public Sheet[] sheets = Array.Empty<Sheet>();

        private static SpecialWeaponVisualLibrary s_loaded;
        private static bool s_lookedUp;

        /// <summary>The Resources library, or null when the art package is not imported (placeholders stay).</summary>
        public static SpecialWeaponVisualLibrary Current
        {
            get
            {
                if (!s_lookedUp)
                {
                    s_lookedUp = true;
                    s_loaded = Resources.Load<SpecialWeaponVisualLibrary>(ResourcePath);
                }

                return s_loaded;
            }
        }

        public Entry Find(SpecialWeaponId id)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].id == id) return entries[i];
            }

            return null;
        }

        public Sheet FindSheet(string usage)
        {
            if (sheets == null) return null;
            for (int i = 0; i < sheets.Length; i++)
            {
                if (sheets[i] != null && sheets[i].usage == usage) return sheets[i];
            }

            return null;
        }

        /// <summary>Test seam: drop the cached lookup so a test-built library can be picked up (or the absence of one).</summary>
        public static void ResetLookup()
        {
            s_lookedUp = false;
            s_loaded = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetLookup();
        }

        /// <summary>
        /// Build a LOD0–2 model under <paramref name="parent"/>: one MeshFilter/MeshRenderer per
        /// LOD in a LODGroup, the shared atlas material, no colliders. Returns the root.
        /// </summary>
        public static GameObject Instantiate(string name, Mesh[] lods, Material material, Transform parent, float[] screenHeights)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var group = root.AddComponent<LODGroup>();
            var lodList = new LOD[LodCount];
            int count = 0;
            for (int i = 0; i < LodCount; i++)
            {
                Mesh mesh = lods != null && i < lods.Length ? lods[i] : null;
                if (mesh == null)
                {
                    continue;
                }

                var go = new GameObject("LOD" + i);
                go.transform.SetParent(root.transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
                float height = screenHeights != null && i < screenHeights.Length ? screenHeights[i] : 0.01f;
                lodList[count++] = new LOD(height, new Renderer[] { renderer });
            }

            if (count == 0)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            if (count < LodCount)
            {
                Array.Resize(ref lodList, count);
            }

            group.SetLODs(lodList);
            group.RecalculateBounds();
            return root;
        }

        /// <summary>Prefix of the AP-021 convex hull meshes; the hand model and the pickup carry no collider (the character / pickup trigger is the volume).</summary>
        public const string ColliderPrefix = "COL_";

        private static readonly List<Renderer>[] s_lodScratch = { new List<Renderer>(16), new List<Renderer>(16), new List<Renderer>(16) };
        private static readonly List<LODGroup> s_groupScratch = new List<LODGroup>(8);

        /// <summary>
        /// WP-069 B4c: instantiate an entry's AP-021 model under <paramref name="parent"/> (null = no parent).
        /// The COL_* hulls go; the importer's per-part LODGroups (body, part_cell_*, part_trigger, …) become ONE
        /// group on the root with every <c>*_LOD{n}</c> renderer in level n, so the parts never switch apart
        /// from the body, re-thresholded to <paramref name="screenHeights"/>. The imported materials stay.
        /// Not a hot path (equip / drop).
        /// </summary>
        public static GameObject InstantiateModel(Entry entry, Transform parent, float[] screenHeights)
        {
            if (entry == null || entry.model == null)
            {
                return null;
            }

            GameObject go = parent != null ? UnityEngine.Object.Instantiate(entry.model, parent, false) : UnityEngine.Object.Instantiate(entry.model);
            go.name = "Model";
            if (entry.modelFrameEuler != Vector3.zero)
            {
                // The rig resets the instance root to identity (WeaponVisualRig.SetModel), so the fix lives on its children.
                Quaternion fix = Quaternion.Euler(entry.modelFrameEuler);
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    Transform c = go.transform.GetChild(i);
                    c.localPosition = fix * c.localPosition;
                    c.localRotation = fix * c.localRotation;
                }
            }
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i] != go.transform && all[i].name.StartsWith(ColliderPrefix, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(all[i].gameObject);
                }
            }

            for (int i = 0; i < LodCount; i++) s_lodScratch[i].Clear();
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                int lod = LodIndexOf(renderers[i].name);
                if (lod >= 0) s_lodScratch[lod].Add(renderers[i]);
            }

            s_groupScratch.Clear();
            go.GetComponentsInChildren(true, s_groupScratch);
            for (int i = 0; i < s_groupScratch.Count; i++)
            {
                if (s_groupScratch[i].gameObject != go) UnityEngine.Object.DestroyImmediate(s_groupScratch[i]);
            }

            LODGroup group = go.GetComponent<LODGroup>();
            if (group == null) group = go.AddComponent<LODGroup>();
            var lods = new LOD[LodCount];
            int count = 0;
            for (int i = 0; i < LodCount; i++)
            {
                if (s_lodScratch[i].Count == 0) continue;
                float height = screenHeights != null && i < screenHeights.Length ? screenHeights[i] : 0.01f;
                lods[count++] = new LOD(height, s_lodScratch[i].ToArray());
            }

            if (count < LodCount) Array.Resize(ref lods, count);
            group.SetLODs(lods);
            group.RecalculateBounds();
            for (int i = 0; i < LodCount; i++) s_lodScratch[i].Clear();
            s_groupScratch.Clear();
            return go;
        }

        /// <summary>0..2 for a renderer named <c>…_LOD0/1/2</c>, else -1.</summary>
        public static int LodIndexOf(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 5) return -1;
            int at = name.LastIndexOf("_LOD", StringComparison.Ordinal);
            if (at < 0 || at + 5 != name.Length) return -1;
            int n = name[at + 4] - '0';
            return n >= 0 && n < LodCount ? n : -1;
        }

        /// <summary>
        /// WP-069 B4c: the LOD0 renderers' bounds in <paramref name="root"/>'s own space (mesh bounds through each
        /// child's transform), for laying a model on the ground. Empty bounds when there is no LOD0 renderer.
        /// </summary>
        public static Bounds LocalLod0Bounds(Transform root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            bool any = false;
            var b = new Bounds();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter f = filters[i];
                if (f.sharedMesh == null || LodIndexOf(f.name) != 0) continue;
                Bounds mb = f.sharedMesh.bounds;
                Matrix4x4 m = toRoot * f.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }

            return b;
        }
    }
}
