using System;
using System.Collections.Generic;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Weapons.Attachments
{
    /// <summary>
    /// WP-104 B4 (S-06, C-00 12.10 · B4-PLAN-12.10 §3) · one classic weapon model's attachment sockets and mounted add-ons. It sits
    /// on the model root (the AP-021 FBX root under WeaponVisualRig's body), so a model swap destroys sockets and add-ons with it.
    /// - SOCKET_Mag rides part_mag where the model has one: the add-on travels to the hand on reload (WeaponMagazineVisual) and the
    ///   original magazine's renderers are switched off while an E-mag is fitted. Without part_mag (sg_1 / sg_2 / rl_1) it hangs on the root.
    /// - SOCKET_Muzzle carries E3 (+ its rear-drop bridge) and E5 at the AP-040 combination offsets; SOCKET_Side carries E4.
    /// - The existing Muzzle / marker_mag transforms are never touched (the hit ray and the Wp087 aim alignment stay).
    /// - Every add-on joins the model's single LODGroup (its LOD children by name).
    /// Work happens on Apply / SetShown only (tree, equip and camera events); nothing runs per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponAttachMount : MonoBehaviour
    {
        public const string MagSocketName = "SOCKET_Mag", MuzzleSocketName = "SOCKET_Muzzle", SideSocketName = "SOCKET_Side";
        public const string SecondaryMuzzleName = "SOCKET_SecondaryMuzzle", PowerMuzzleName = "SOCKET_PowerMuzzle", ElementVfxName = "SOCKET_ElementVFX";

        private Transform _mag, _muzzle, _side;
        private Renderer[] _part = Array.Empty<Renderer>();
        private LOD[] _baseLods;
        private GameObject _magGo, _twinGo, _ringGo, _sideGo, _bridgeGo;
        private WeaponAttachSet _set;
        private bool _applied, _shown = true;
        private static readonly List<Renderer> s_lod = new List<Renderer>(32), s_tmp = new List<Renderer>(16);

        public WeaponAttachLibrary.Row Row { get; private set; }
        public WeaponAttachSet Set => _set;
        public bool Shown => _shown;
        public Transform MagSocket => _mag;
        public Transform MuzzleSocket => _muzzle;
        public Transform SideSocket => _side;
        public Transform SecondaryMuzzle { get; private set; }
        public Transform PowerMuzzle { get; private set; }
        public Transform ElementVfx { get; private set; }
        public GameObject MagAttachment => _magGo;
        public GameObject TwinAttachment => _twinGo;
        public GameObject RingAttachment => _ringGo;
        public GameObject SideAttachment => _sideGo;

        /// <summary>
        /// The mount of <paramref name="root"/>, created with its three sockets on first call. part_mag must be at home (WeaponController
        /// installs a model as Home → SetModel → WeaponEquipped, so it is).
        /// </summary>
        public static WeaponAttachMount Ensure(Transform root, WeaponAttachLibrary.Row row)
        {
            if (root == null || row == null) return null;
            WeaponAttachMount m = root.GetComponent<WeaponAttachMount>();
            if (m != null && m.Row != null && m.Row.id == row.id) return m;
            if (m == null) m = root.gameObject.AddComponent<WeaponAttachMount>();
            else m.Clear();   // another row on this model: the old add-ons, the hidden part and the grown LODs go first
            m.Row = row;
            Transform part = row.magOnPart ? WeaponSocketContract.Find(root, WeaponMagazineVisual.PartName) : null;
            m._part = part != null ? PartRenderers(part) : Array.Empty<Renderer>();
            m._mag = Socket(part != null ? part : root, MagSocketName, row.mag, root);
            m._muzzle = Socket(root, MuzzleSocketName, row.muzzle, root);
            m._side = Socket(root, SideSocketName, row.side, root);
            LODGroup g = root.GetComponent<LODGroup>();
            if (g != null) m._baseLods = g.GetLODs();
            return m;
        }

        /// <summary>The original magazine's renderers — never an add-on under SOCKET_Mag (a cleared one dies only at the frame end).</summary>
        private static Renderer[] PartRenderers(Transform part)
        {
            Transform socket = WeaponSocketContract.Find(part, MagSocketName);
            s_tmp.Clear();
            part.GetComponentsInChildren(true, s_tmp);
            if (socket != null) s_tmp.RemoveAll(r => r.transform.IsChildOf(socket));
            return s_tmp.ToArray();
        }

        private static Transform Socket(Transform parent, string name, in AttachPose p, Transform root)
        {
            Transform t = WeaponSocketContract.Find(parent, name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }

            // the table is in the model root's frame, whatever the parent (part_mag is offset inside the model)
            t.SetPositionAndRotation(root.TransformPoint(p.position), root.rotation * p.rotation);
            return t;
        }

        /// <summary>Back to the bare model: no add-ons, part_mag drawn, the model's own LODs; the next Apply mounts from scratch.</summary>
        private void Clear()
        {
            Kill(_magGo);
            Kill(_twinGo);
            Kill(_ringGo);
            Kill(_sideGo);
            Kill(_bridgeGo);
            _magGo = _twinGo = _ringGo = _sideGo = _bridgeGo = null;
            SecondaryMuzzle = PowerMuzzle = ElementVfx = null;
            for (int i = 0; i < _part.Length; i++)
            {
                if (_part[i] != null) _part[i].forceRenderingOff = false;
            }

            LODGroup g = GetComponent<LODGroup>();
            if (g != null && _baseLods != null) g.SetLODs(_baseLods);
            _applied = false;
            _set = default;
        }

        /// <summary>Mount the attachments of <paramref name="set"/> (an unchanged set returns at once, allocation-free).</summary>
        public void Apply(in WeaponAttachSet set, WeaponAttachLibrary lib)
        {
            if (lib == null || Row == null) return;
            if (_applied && set.Equals(_set)) return;
            bool mag = !_applied || set.Mag != _set.Mag;
            bool muzzle = !_applied || set.Twin != _set.Twin || set.Ring != _set.Ring;
            bool side = !_applied || set.Side != _set.Side;
            _applied = true;
            _set = set;
            if (mag) _magGo = Replace(_magGo, MagPrefab(set.Mag, lib), _mag, Vector3.zero, 1f);
            if (muzzle)
            {
                AttachCombo c = Row.combos != null && Row.combos.Length == WeaponAttachLibrary.TwinVariants * WeaponAttachLibrary.RingVariants
                    ? Row.combos[WeaponAttachLibrary.ComboIndex(set.Twin, set.Ring)] : default;
                _twinGo = Replace(_twinGo, set.Twin > 0 ? At(lib.e3, set.Twin - 1) : null, _muzzle, c.e3Offset, 1f);
                _bridgeGo = ReplaceMesh(_bridgeGo, set.Twin > 0 ? c.bridge : null, _muzzle, _twinGo);
                _ringGo = Replace(_ringGo, set.Ring > 0 ? At(lib.e5, set.Ring - 1) : null, _muzzle, c.e5Offset, c.e5Scale > 0f ? c.e5Scale : 1f);
                SecondaryMuzzle = _twinGo != null ? WeaponSocketContract.Find(_twinGo.transform, SecondaryMuzzleName) : null;
                PowerMuzzle = _ringGo != null ? WeaponSocketContract.Find(_ringGo.transform, PowerMuzzleName) : null;
            }

            if (side)
            {
                int e = WeaponAttachRules.ElementIndex(set.Side);
                _sideGo = Replace(_sideGo, e >= 0 ? At(lib.e4, e) : null, _side, Vector3.zero, 1f);
                ElementVfx = _sideGo != null ? WeaponSocketContract.Find(_sideGo.transform, ElementVfxName) : null;
            }

            SetShown(_shown, true);
            RebuildLods();
            WeaponAttachEvents.RaiseChanged(this);
        }

        /// <summary>C-00 Q1: the add-ons show in TopDown only; elsewhere the weapon is its original self (part_mag back on).</summary>
        public void SetShown(bool on, bool force = false)
        {
            if (_shown == on && !force) return;
            _shown = on;
            Show(_magGo, on);
            Show(_twinGo, on);
            Show(_ringGo, on);
            Show(_sideGo, on);
            Show(_bridgeGo, on);
            bool hidePart = on && _magGo != null && _mag != null && _mag.parent != transform;   // the add-on replaces part_mag
            for (int i = 0; i < _part.Length; i++)
            {
                if (_part[i] != null) _part[i].forceRenderingOff = hidePart;
            }
        }

        private static GameObject MagPrefab(byte mag, WeaponAttachLibrary lib) =>
            mag == 5 ? lib.e6 : mag == 1 ? lib.e1 : mag >= 2 && mag <= 4 ? At(lib.e2, mag - 2) : null;

        private static GameObject At(GameObject[] list, int index) => list != null && index >= 0 && index < list.Length ? list[index] : null;

        private GameObject Replace(GameObject current, GameObject prefab, Transform socket, Vector3 localOffset, float scale)
        {
            Kill(current);
            if (prefab == null || socket == null) return null;
            GameObject go = Instantiate(prefab, socket, false);
            go.name = prefab.name;
            go.transform.localPosition = localOffset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * scale;
            SetLayer(go.transform, gameObject.layer);
            return go;
        }

        private GameObject ReplaceMesh(GameObject current, Mesh mesh, Transform socket, GameObject materialSource)
        {
            Kill(current);
            if (mesh == null || socket == null) return null;
            var go = new GameObject("rear_drop_bridge_LOD0", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(socket, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            Renderer src = materialSource != null ? materialSource.GetComponentInChildren<Renderer>(true) : null;
            if (src != null) go.GetComponent<MeshRenderer>().sharedMaterial = src.sharedMaterial;
            go.layer = gameObject.layer;
            return go;
        }

        private static void Kill(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        /// <summary>The model's LODGroup re-made with the add-ons in their LOD levels (event-time allocation; never per frame).</summary>
        private void RebuildLods()
        {
            LODGroup g = GetComponent<LODGroup>();
            if (g == null || _baseLods == null) return;
            var lods = new LOD[_baseLods.Length];
            for (int i = 0; i < lods.Length; i++)
            {
                s_lod.Clear();
                if (_baseLods[i].renderers != null) s_lod.AddRange(_baseLods[i].renderers);
                Add(_magGo, i);
                Add(_twinGo, i);
                Add(_ringGo, i);
                Add(_sideGo, i);
                Add(_bridgeGo, i);
                lods[i] = new LOD(_baseLods[i].screenRelativeTransitionHeight, s_lod.ToArray());
            }

            g.SetLODs(lods);
            g.RecalculateBounds();
        }

        private static void Add(GameObject go, int level)
        {
            if (go == null) return;
            go.GetComponentsInChildren(true, s_tmp);
            for (int i = 0; i < s_tmp.Count; i++)
            {
                int l = AuthoredWeaponModel.LodIndexOf(s_tmp[i].name);
                if (l == level || (l < 0 && level == 0)) s_lod.Add(s_tmp[i]);
            }
        }
    }
}
