using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-047E · AP-013C casings (P-06, credit 0): rifle brass (A), carbine brass (B), shotgun shell (C) — LOD0 meshes and the
    /// shared 256 material. Built by <c>ArtCasingBuilder</c> into <c>Resources/DD_Casings.asset</c>; absent = no casings.
    /// </summary>
    public sealed class CasingLibrary : ScriptableObject
    {
        public const string ResourceName = "DD_Casings";

        public Mesh CasingA;
        public Mesh CasingB;
        public Mesh ShellC;
        public Material Material;
        /// <summary>Case length along the mesh +Z axis (manifest: A 0.056, B 0.029, C 0.070 m).</summary>
        public float LengthA = 0.056f, LengthB = 0.029f, LengthC = 0.070f;

        private static CasingLibrary s_current;
        private static bool s_tried;

        public static CasingLibrary Current
        {
            get
            {
                if (!s_tried) { s_tried = true; s_current = Resources.Load<CasingLibrary>(ResourceName); }
                return s_current;
            }
        }

        public Mesh MeshFor(MuzzleVariant variant)
        {
            switch (variant)
            {
                case MuzzleVariant.B: return CasingB;
                case MuzzleVariant.C: return ShellC;
                default: return CasingA;
            }
        }

        public bool IsComplete => CasingA != null && CasingB != null && ShellC != null && Material != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_current = null; s_tried = false; }
    }
}
