using DD.Prototype.Characters;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>Reload parts follow their authored pivot calibration in the AP-024 hand.
    /// Other sockets keep the previous palm-centred contract. Home always restores the exact local pose.
    /// sw_arc carries its three cells as one group, preserving their rest arrangement.</summary>
    public sealed class WeaponMagazineVisual
    {
        public const string PartName = "part_mag";
        private struct Pose
        {
            public Transform Part, Home;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public Matrix4x4 InPrimary;
        }
        private Pose[] _poses;
        private Transform _boundRoot;
        private string _key;
        public Transform Part => _poses != null && _poses.Length > 0 ? _poses[0].Part : null;
        public Transform Socket { get; private set; }
        public bool InHand => Socket != null;
        public int ToHandCount { get; private set; }
        public int HomeCount { get; private set; }

        public bool Contains(Transform t)
        {
            if (_poses == null || t == null) return false;
            for (int i = 0; i < _poses.Length; i++)
                if (_poses[i].Part != null && t.IsChildOf(_poses[i].Part)) return true;
            return false;
        }

        public void CollectRenderers(System.Collections.Generic.List<Renderer> into)
        {
            if (_poses == null || into == null) return;
            for (int i = 0; i < _poses.Length; i++)
                if (_poses[i].Part != null)
                    foreach (Renderer r in _poses[i].Part.GetComponentsInChildren<Renderer>(true))
                        if (!into.Contains(r)) into.Add(r);
        }

        public void Bind(Transform weaponRoot, string key = null)
        {
            if (weaponRoot != null && weaponRoot == _boundRoot && Part != null && _poses[0].Home != null && _poses[0].Home.IsChildOf(weaponRoot))
            {
                Home();
                return;
            }
            Home();
            _boundRoot = weaponRoot;
            _key = key ?? FindKey(weaponRoot);
            Transform found = weaponRoot != null ? Find(weaponRoot, ReloadPartCalibration.PartName(_key)) : null;
            _poses = null;
            if (found == null) return;
            string[] group = _key == "sw_arc" ? ReloadPartCalibration.ArcGroup : null;
            _poses = new Pose[group != null ? group.Length : 1];
            for (int i = 0; i < _poses.Length; i++)
            {
                Transform part = group != null ? Find(weaponRoot, group[i]) : found;
                _poses[i] = part == null ? default : new Pose { Part = part, Home = part.parent, Position = part.localPosition,
                    Rotation = part.localRotation, Scale = part.localScale, InPrimary = found.worldToLocalMatrix * part.localToWorldMatrix };
            }
        }

        public bool ToHand(Transform socket)
        {
            if (Part == null || socket == null || !socket.gameObject.activeInHierarchy) return false;
            if (Socket == socket) return true;
            ReloadPartSocket row = socket.GetComponent<ReloadPartSocket>();
            bool calibrated = row != null && ReloadPartCalibration.TryGet(row.Key, out _);
            Matrix4x4 primary = Matrix4x4.identity;
            if (calibrated && ReloadPartCalibration.TryGet(row.Key, out Matrix4x4 m))
                primary = Matrix4x4.TRS(socket.TransformPoint(m.GetColumn(3)), socket.rotation * m.rotation, Part.lossyScale);
            for (int i = 0; i < _poses.Length; i++)
            {
                Transform part = _poses[i].Part;
                if (part == null) continue;
                Vector3 worldScale = part.lossyScale;
                part.SetParent(socket, true);
                if (calibrated)
                {
                    Matrix4x4 local = socket.worldToLocalMatrix * primary * _poses[i].InPrimary;
                    part.localPosition = local.GetColumn(3);
                    part.localRotation = local.rotation;
                }
                else part.localPosition = Vector3.zero;
                Vector3 s = socket.lossyScale;
                part.localScale = new Vector3(Safe(worldScale.x, s.x), Safe(worldScale.y, s.y), Safe(worldScale.z, s.z));
                SetLayer(part, socket.gameObject.layer);
            }
            Socket = socket;
            ToHandCount++;
            return true;
        }

        public void Home()
        {
            if (Socket == null) return;
            Socket = null;
            if (_poses != null)
                for (int i = 0; i < _poses.Length; i++)
                {
                    Pose p = _poses[i];
                    if (p.Part == null) continue;
                    if (p.Home == null) { Object.Destroy(p.Part.gameObject); continue; }
                    p.Part.SetParent(p.Home, false);
                    p.Part.localPosition = p.Position;
                    p.Part.localRotation = p.Rotation;
                    p.Part.localScale = p.Scale;
                    SetLayer(p.Part, p.Home.gameObject.layer);
                }
            HomeCount++;
        }
        private static float Safe(float world, float parent) => Mathf.Abs(parent) > 1e-6f ? world / parent : world;
        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
        private static string FindKey(Transform root)
        {
            if (root == null) return null;
            if (root.name.StartsWith("AP021_", System.StringComparison.Ordinal))
                return root.name.Substring(6).Replace("(Clone)", "");
            for (int i = 0; i < root.childCount; i++)
            {
                string key = FindKey(root.GetChild(i));
                if (key != null) return key;
            }
            return null;
        }
        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = Find(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
