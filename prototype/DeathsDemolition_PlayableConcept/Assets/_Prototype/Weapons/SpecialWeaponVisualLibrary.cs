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
            /// <see cref="InstantiateModel"/>. Zero for AP-021 v002-uf (the export carries the frame); kept for a package
            /// that lands in another frame.
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

        /// <summary>Prefix of the AP-021 convex hull meshes (see <see cref="AuthoredWeaponModel"/>).</summary>
        public const string ColliderPrefix = AuthoredWeaponModel.ColliderPrefix;

        /// <summary>WP-069 B4c: instantiate an entry's AP-021 model (<see cref="AuthoredWeaponModel.Instantiate"/> with the entry's frame fix).</summary>
        public static GameObject InstantiateModel(Entry entry, Transform parent, float[] screenHeights)
        {
            return entry == null ? null : AuthoredWeaponModel.Instantiate(entry.model, parent, screenHeights, entry.modelFrameEuler);
        }

        /// <summary>0..2 for a renderer named <c>…_LOD0/1/2</c>, else -1.</summary>
        public static int LodIndexOf(string name) => AuthoredWeaponModel.LodIndexOf(name);

        /// <summary>The LOD0 renderers' bounds in <paramref name="root"/>'s own space.</summary>
        public static Bounds LocalLod0Bounds(Transform root) => AuthoredWeaponModel.LocalLod0Bounds(root);
    }
}
