using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-069 B4c / B4d · how an AP-021 (P-07) weapon model becomes a scene object — shared by the Special library
    /// (hand + ground) and the classic library (hand). The FBX root is the ADR-042-1 frame (root at the grip, +Z barrel,
    /// +Y up, +X port) with the contract sockets, the moving parts <c>part_*</c> and its own materials:
    ///   · the COL_* convex hulls go (the character / the pickup trigger is the hit volume);
    ///   · the importer's per-part LODGroups (body, part_mag, part_trigger, …) become ONE group on the root with every
    ///     <c>*_LOD{n}</c> renderer in level n, so a part never switches apart from the body, re-thresholded to the
    ///     caller's screen heights (LOD0 is the first-person mesh, LOD1/2 third person / top-down);
    ///   · an optional frame fix rotates the content (children of the root — the rig resets the root itself).
    /// The imported materials stay. Not a hot path (equip / drop).
    /// </summary>
    public static class AuthoredWeaponModel
    {
        public const string ColliderPrefix = "COL_";
        public const string OwnMaterialPrefix = "M_AP021_";
        public const int LodCount = 3;

        private static readonly List<Renderer>[] s_lodScratch = { new List<Renderer>(16), new List<Renderer>(16), new List<Renderer>(16) };
        private static readonly List<LODGroup> s_groupScratch = new List<LODGroup>(8);

        public static GameObject Instantiate(GameObject model, Transform parent, float[] screenHeights, Vector3 frameEuler)
        {
            if (model == null)
            {
                return null;
            }

            GameObject go = parent != null ? UnityEngine.Object.Instantiate(model, parent, false) : UnityEngine.Object.Instantiate(model);
            go.name = "Model";
            if (frameEuler != Vector3.zero)
            {
                Quaternion fix = Quaternion.Euler(frameEuler);
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
            Material body = BodyMaterialOf(renderers);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (body != null) FillStraySlots(renderers[i], body);
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

        /// <summary>The model's own body material (<c>M_AP021_&lt;key&gt;_body</c>), or null.</summary>
        private static Material BodyMaterialOf(Renderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                foreach (Material m in renderers[i].sharedMaterials)
                {
                    if (m != null && m.name.StartsWith(OwnMaterialPrefix, StringComparison.Ordinal) && m.name.EndsWith("_body", StringComparison.Ordinal)) return m;
                }
            }

            return null;
        }

        /// <summary>
        /// WP-069 B4e: a slot that is not an AP-021 material — a sub-mesh the FBX left without one (lmg_1 part_trigger_LOD2,
        /// P-07 batch B) comes in with the pipeline default "Lit" — takes the model's body material. Equip time only.
        /// </summary>
        private static void FillStraySlots(Renderer r, Material body)
        {
            Material[] materials = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].name.StartsWith(OwnMaterialPrefix, StringComparison.Ordinal)) continue;
                materials[i] = body;
                changed = true;
            }

            if (changed) r.sharedMaterials = materials;
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

        /// <summary>The LOD0 renderers' bounds in <paramref name="root"/>'s own space (for laying a model on the ground).</summary>
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
