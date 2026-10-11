using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons.Attachments
{
    /// <summary>WP-104 B4 · a socket pose in the AP-021 model root's frame (ADR-042-1: root at the grip, +Z the barrel, +Y up, +X right).</summary>
    [Serializable]
    public struct AttachPose
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>
    /// WP-104 B4 · one E3 (twin barrel) × E5 (power ring) combination on a weapon — AP-040 sockets.json measured the E3 offset with
    /// the E5 variant in place (5 weapons drop the twin barrel under a bigger ring) and derives the muzzles from it.
    /// </summary>
    [Serializable]
    public struct AttachCombo
    {
        public byte e3, e5;
        public Vector3 e3Offset, e5Offset, secondaryMuzzle, powerMuzzle;
        public float e5Scale;
        /// <summary>The rear-drop bridge under the twin barrel (weapons where sockets.json requires one; builder-made mesh), else null.</summary>
        public Mesh bridge;
    }

    /// <summary>
    /// WP-104 B4 (S-06, C-00 12.10 · B4-PLAN-12.10) · the baked AP-040 v001 weapon-attachment data: per classic weapon the
    /// SOCKET_Mag / Muzzle / Side poses from sockets.json (sha-pinned), the E3×E5 combinations, and the E1–E6 prefabs.
    /// ArtAp040WeaponAttachBuilder writes it to Resources/<see cref="ResourcePath"/>; the runtime reads it at equip and on
    /// tree changes only (no per-frame work, no JSON at runtime).
    /// </summary>
    public sealed class WeaponAttachLibrary : ScriptableObject
    {
        public const string ResourcePath = "DD_WeaponAttach";
        public const string Schema = "AP040.weapon-attach-sockets.v001";
        /// <summary>E3 sizes (S / M / L), E5 variants (speed / chaos), E4 elements (fire / ice / shock / venom), E2 sizes (S / M / L).</summary>
        public const int TwinVariants = 3, RingVariants = 2, ElementVariants = 4, DrumVariants = 3;

        [Serializable]
        public sealed class Row
        {
            public ClassicWeaponId id;
            /// <summary>The v3 key (ar_1 …), as sockets.json names the weapon.</summary>
            public string key;
            public AttachPose mag, muzzle, side;
            /// <summary>The model has part_mag: the Mag add-on rides the part (to the hand on reload) and hides the original magazine.</summary>
            public bool magOnPart;
            /// <summary>sockets.json order: (E3 v1, E5 v1), (E3 v1, E5 v2), (E3 v2, E5 v1), … — see <see cref="ComboIndex"/>.</summary>
            public AttachCombo[] combos = new AttachCombo[TwinVariants * RingVariants];
        }

        /// <summary>sha256 of the sockets.json the rows were baked from (the builder refuses another).</summary>
        public string sourceSha256;
        public Row[] rows = Array.Empty<Row>();
        /// <summary>E1 extended magazine, E6 rocket belt box (rl_1 only).</summary>
        public GameObject e1, e6;
        public GameObject[] e2 = new GameObject[DrumVariants];
        public GameObject[] e3 = new GameObject[TwinVariants];
        public GameObject[] e4 = new GameObject[ElementVariants];
        public GameObject[] e5 = new GameObject[RingVariants];

        private static WeaponAttachLibrary s_current;
        private static bool s_loaded;

        /// <summary>The baked library (Resources), or null before the builder ran — every caller treats null as "no attachments".</summary>
        public static WeaponAttachLibrary Current
        {
            get
            {
                if (!s_loaded)
                {
                    s_loaded = true;
                    s_current = Resources.Load<WeaponAttachLibrary>(ResourcePath);
                }

                return s_current;
            }
        }

        /// <summary>Tests: drop the cached library (a rebuilt asset, a test library).</summary>
        public static void ResetCache(WeaponAttachLibrary replacement = null)
        {
            s_current = replacement;
            s_loaded = replacement != null;
        }

        /// <summary>
        /// The combination row of an E3 size (1–3) and an E5 variant (1–2). E3 alone takes the E5 v1 row (the weapon↔E3 offset was
        /// measured with it); E5 alone the E3 v1 row (the E5 data does not depend on E3).
        /// </summary>
        public static int ComboIndex(int twin, int ring) => (Mathf.Clamp(twin, 1, TwinVariants) - 1) * RingVariants + Mathf.Clamp(ring, 1, RingVariants) - 1;

        public Row Find(ClassicWeaponId id)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] != null && rows[i].id == id) return rows[i];
            }

            return null;
        }
    }
}
