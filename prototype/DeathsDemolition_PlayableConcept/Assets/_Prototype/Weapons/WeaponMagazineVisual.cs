using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-082 visual (S-02 contract v1, C-00 05.10): the weapon model's own magazine part (AP-021 <c>part_mag</c>, pivot at the
    /// magazine seat; its LOD renderers move with it and the root LODGroup keeps them) goes to the left hand's magazine socket
    /// (<c>socket_mag_l</c>, S-06 WP-083 B2b) while <see cref="WeaponMagazineOwnership"/> has it in a hand, and back to its
    /// exact authored place otherwise. In the hand it keeps the world rotation it had when it left the well (no turn on the
    /// pickup) and its world scale (the FP view model is scaled), its pivot on the palm. A model without the part, or no
    /// socket (an FP weapon shown without arms), leaves the magazine in the weapon. Not a hot path (reload beats only).
    /// </summary>
    public sealed class WeaponMagazineVisual
    {
        public const string PartName = "part_mag";

        private Transform _part;
        private Transform _home;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private Vector3 _homeScale;

        /// <summary>The magazine part this visual moves (null when the weapon's model has none).</summary>
        public Transform Part => _part;

        /// <summary>The socket the part sits in now (null = in the weapon).</summary>
        public Transform Socket { get; private set; }

        public bool InHand => Socket != null;

        /// <summary>Times the part went to a hand / came home (tests, S-10 H3).</summary>
        public int ToHandCount { get; private set; }
        public int HomeCount { get; private set; }

        /// <summary>Find the part under <paramref name="weaponRoot"/> and remember its authored place. Sends a held part home first.</summary>
        public void Bind(Transform weaponRoot)
        {
            Transform found = weaponRoot != null ? Find(weaponRoot, PartName) : null;
            if (found == _part && _part != null) return;
            Home();
            _part = found;
            if (_part == null) return;
            _home = _part.parent;
            _homePosition = _part.localPosition;
            _homeRotation = _part.localRotation;
            _homeScale = _part.localScale;
        }

        /// <summary>The part leaves the well for <paramref name="socket"/>; false (and nothing moves) without a part or a socket.</summary>
        public bool ToHand(Transform socket)
        {
            if (_part == null || socket == null || !socket.gameObject.activeInHierarchy) return false;
            if (Socket == socket) return true;
            Vector3 worldScale = _part.lossyScale;
            _part.SetParent(socket, true);   // keeps the world rotation it left the well with
            _part.localPosition = Vector3.zero;
            Vector3 s = socket.lossyScale;
            _part.localScale = new Vector3(Safe(worldScale.x, s.x), Safe(worldScale.y, s.y), Safe(worldScale.z, s.z));
            SetLayer(_part, socket.gameObject.layer);
            Socket = socket;
            ToHandCount++;
            return true;
        }

        /// <summary>The part back in the weapon, at its authored local pose and the weapon's layer.</summary>
        public void Home()
        {
            if (_part == null || Socket == null) return;
            Socket = null;
            if (_home == null)
            {
                return;   // the weapon model went away (pooled / rebuilt): the part goes with the next Bind
            }

            _part.SetParent(_home, false);
            _part.localPosition = _homePosition;
            _part.localRotation = _homeRotation;
            _part.localScale = _homeScale;
            SetLayer(_part, _home.gameObject.layer);
            HomeCount++;
        }

        private static float Safe(float world, float parent) => Mathf.Abs(parent) > 1e-6f ? world / parent : world;

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
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
