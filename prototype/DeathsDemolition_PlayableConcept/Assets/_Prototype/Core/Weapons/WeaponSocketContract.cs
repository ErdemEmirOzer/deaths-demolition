using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.Weapons
{
    /// <summary>
    /// WP-042 · ADR-042-1 · THE WEAPON SOCKET CONTRACT. One frame for every classic weapon
    /// model — the AP-013 blockouts P-06 solved the AP-009C hands to, and the final AP-013
    /// models that replace them: the model root sits at the grip / trigger, +Z is the barrel,
    /// +Y up, +X right; the root is parented to the character's <see cref="RootSocket"/>
    /// (<c>socket_weapon_r</c>, identity). Child transforms (exact names):
    /// <list type="bullet">
    ///   <item><see cref="Muzzle"/> — flash / tracer origin, on the barrel axis.</item>
    ///   <item><see cref="Ejection"/> — casing port, ejects along +X.</item>
    ///   <item><see cref="AttachBarrel"/> / <see cref="AttachTop"/> / <see cref="AttachStock"/> — the three
    ///   core-branch attachment mounts (WP-034 §3).</item>
    ///   <item><see cref="MarkerGrip"/> (origin) / <see cref="MarkerForegrip"/> / <see cref="MarkerMag"/> — the
    ///   measure markers the hands were solved to.</item>
    /// </list>
    /// The measures in <see cref="MeasuresOf"/> are the AP-009C v003 blockout values; a model
    /// may move a marker by at most <see cref="MarkerToleranceMetres"/> before the clips need a
    /// re-solve (P-06, one build run). LOD0–2 expected, no collider on the hand model (the
    /// character's collider is the hit volume), no rig (rigid mesh).
    /// </summary>
    public static class WeaponSocketContract
    {
        public const string Version = "ADR-042-1";

        public const string RootSocket = "socket_weapon_r";
        public const string Muzzle = "Muzzle";
        public const string Ejection = "Ejection";
        public const string AttachBarrel = "attach_barrel";
        public const string AttachTop = "attach_top";
        public const string AttachStock = "attach_stock";
        public const string MarkerGrip = "marker_grip";
        public const string MarkerForegrip = "marker_foregrip";
        public const string MarkerMag = "marker_mag";

        /// <summary>Sockets every model must carry (the measure markers are checked separately).</summary>
        public static readonly string[] RequiredSockets = { Muzzle, Ejection, AttachBarrel, AttachTop, AttachStock };

        /// <summary>Metres a marker may drift from the AP-009C solve before the hands no longer meet the weapon.</summary>
        public const float MarkerToleranceMetres = 0.03f;

        /// <summary>LOD levels a shipped model carries (LOD0–2; the blockouts carry one mesh and are exempt).</summary>
        public const int LodLevels = 3;

        /// <summary>Base-model triangle ceilings per LOD (WP-034 visual contract §2: LOD0 ≤ 3 000 · LOD1 ≤ 1 200 · LOD2 ≤ 400; attachment LOD0 ≤ 400).</summary>
        public static readonly int[] TriangleBudget = { 3000, 1200, 400 };
        public const int AttachmentLod0TriangleBudget = 400;

        /// <summary>The AP-009C blockout measures of one weapon class (model space, metres).</summary>
        public readonly struct Measures
        {
            public readonly float Length;
            public readonly Vector3 Muzzle;
            public readonly Vector3 Foregrip;
            public readonly Vector3 Mag;

            public Measures(float length, Vector3 muzzle, Vector3 foregrip, Vector3 mag)
            {
                Length = length;
                Muzzle = muzzle;
                Foregrip = foregrip;
                Mag = mag;
            }
        }

        /// <summary>
        /// The AP-009C v003 blockout markers as exported (`exports/v003/props/AP013_*_blockout.fbx`,
        /// read from the files — the README quotes the z only): A 1.20 m — muzzle (0, 0.065, 0.86),
        /// foregrip (0, −0.01, 0.27), mag (0, −0.11, 0.10); B 0.85 m — muzzle (0, 0.06, 0.56),
        /// foregrip (0, −0.01, 0.22), mag (0, −0.13, 0.06); C 0.78 m — muzzle (0, 0.07, 0.52),
        /// pump (0, 0, 0.20), shell port (0, −0.05, 0.05).
        /// </summary>
        public static Measures MeasuresOf(WeaponTypeClass type)
        {
            switch (type)
            {
                case WeaponTypeClass.Rifle: return new Measures(1.20f, new Vector3(0f, 0.065f, 0.86f), new Vector3(0f, -0.01f, 0.27f), new Vector3(0f, -0.11f, 0.10f));
                case WeaponTypeClass.Carbine: return new Measures(0.85f, new Vector3(0f, 0.06f, 0.56f), new Vector3(0f, -0.01f, 0.22f), new Vector3(0f, -0.13f, 0.06f));
                case WeaponTypeClass.Shotgun: return new Measures(0.78f, new Vector3(0f, 0.07f, 0.52f), new Vector3(0f, 0f, 0.20f), new Vector3(0f, -0.05f, 0.05f));
                default: return new Measures(0f, Vector3.zero, Vector3.zero, Vector3.zero);
            }
        }

        /// <summary>Depth-first search by exact name under <paramref name="root"/> (the root itself excluded).</summary>
        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c.name == name) return c;
                Transform deep = Find(c, name);
                if (deep != null) return deep;
            }

            return null;
        }

        /// <summary>
        /// Every contract violation of a model root as "socket: reason" (empty = compliant).
        /// Names must match exactly; the muzzle / foregrip / mag markers must sit within the
        /// tolerance of the class measures when the marker exists (the muzzle is checked through
        /// the <see cref="Muzzle"/> socket, which every model carries).
        /// </summary>
        public static List<string> Validate(Transform root, WeaponTypeClass type, List<string> faults = null)
        {
            var v = faults ?? new List<string>();
            if (root == null)
            {
                v.Add("root: null");
                return v;
            }

            for (int i = 0; i < RequiredSockets.Length; i++)
            {
                if (Find(root, RequiredSockets[i]) == null) v.Add(RequiredSockets[i] + ": missing");
            }

            Measures m = MeasuresOf(type);
            if (m.Length <= 0f) return v;

            CheckMarker(v, root, Muzzle, m.Muzzle, true);
            CheckMarker(v, root, MarkerForegrip, m.Foregrip, false);
            CheckMarker(v, root, MarkerMag, m.Mag, false);
            return v;
        }

        private static void CheckMarker(List<string> v, Transform root, string name, Vector3 expected, bool required)
        {
            Transform t = Find(root, name);
            if (t == null)
            {
                if (required) v.Add(name + ": missing");
                return;
            }

            Vector3 local = root.InverseTransformPoint(t.position);
            float d = Vector3.Distance(local, expected);
            if (d > MarkerToleranceMetres)
            {
                v.Add(name + ": " + d.ToString("0.000") + " m off the AP-009C measure (tolerance " + MarkerToleranceMetres.ToString("0.00") + ")");
            }
        }
    }
}
