using DD.Prototype.Rendering;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// Shared meshes and materials for the whole zombie family.
    ///
    /// PERFORMANCE CONTRACT
    /// Every zombie part points at a material from this fixed set - never at an instanced
    /// copy and never at a MaterialPropertyBlock. Both of those break SRP batching, and a
    /// crowd of 40 zombies is ~550 renderers; batching is the only thing that keeps that
    /// inside the 60 FPS budget. Per-enemy variation comes from PICKING a different shared
    /// material, not from tinting a unique one.
    ///
    /// PROTOTYPE-ONLY. The colours implement the request's palette line for enemies:
    /// dark indigo, black, dirty cloth and oxidised metal. No supernatural aura, no
    /// emissive - the request explicitly reserves effect intensity for combat response.
    /// </summary>
    public static class ZombiePalette
    {
        // ------------------------------------------------------------------ shared meshes

        private static Mesh s_cube;
        private static Mesh s_sphere;
        private static Mesh s_capsule;

        public static Mesh Cube => s_cube != null ? s_cube : (s_cube = ExtractPrimitiveMesh(PrimitiveType.Cube));
        public static Mesh Sphere => s_sphere != null ? s_sphere : (s_sphere = ExtractPrimitiveMesh(PrimitiveType.Sphere));
        public static Mesh Capsule => s_capsule != null ? s_capsule : (s_capsule = ExtractPrimitiveMesh(PrimitiveType.Capsule));

        private static Mesh ExtractPrimitiveMesh(PrimitiveType type)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            temp.SetActive(false);
            Object.DestroyImmediate(temp);
            return mesh;
        }

        // --------------------------------------------------------------- shared materials

        /// <summary>Cloth variants a zombie can be dressed in. Index is the "kimono layer" choice.</summary>
        public enum ClothTone
        {
            DarkIndigo = 0,
            Black = 1,
            DirtyCream = 2,
            FadedBrown = 3,
            WashedSlate = 4
        }

        /// <summary>Non-cloth material roles.</summary>
        public enum Accent
        {
            Flesh = 0,
            OxidisedMetal = 1,
            DarkMetal = 2,
            SashRed = 3,
            SashDark = 4,
            UnderCloth = 5
        }

        private static Material[] s_cloth;
        private static Material[] s_accent;

        public const int ClothToneCount = 5;

        public static Material Cloth(ClothTone tone)
        {
            EnsureMaterials();
            int i = Mathf.Clamp((int)tone, 0, s_cloth.Length - 1);
            return s_cloth[i];
        }

        public static Material Cloth(int index)
        {
            EnsureMaterials();
            return s_cloth[((index % s_cloth.Length) + s_cloth.Length) % s_cloth.Length];
        }

        public static Material Of(Accent accent)
        {
            EnsureMaterials();
            int i = Mathf.Clamp((int)accent, 0, s_accent.Length - 1);
            return s_accent[i];
        }

        private static void EnsureMaterials()
        {
            if (s_cloth != null && s_cloth.Length > 0 && s_cloth[0] != null)
            {
                return;
            }

            Shader shader = ResolveShader();

            s_cloth = new[]
            {
                Make(shader, "DD_Zombie_Cloth_DarkIndigo", new Color32(0x2A, 0x2F, 0x45, 0xFF), 0.92f),
                Make(shader, "DD_Zombie_Cloth_Black", new Color32(0x1A, 0x1A, 0x1E, 0xFF), 0.95f),
                Make(shader, "DD_Zombie_Cloth_DirtyCream", new Color32(0xA9, 0x9E, 0x86, 0xFF), 0.88f),
                Make(shader, "DD_Zombie_Cloth_FadedBrown", new Color32(0x4A, 0x3B, 0x2E, 0xFF), 0.9f),
                Make(shader, "DD_Zombie_Cloth_WashedSlate", new Color32(0x3A, 0x3E, 0x42, 0xFF), 0.9f)
            };

            s_accent = new[]
            {
                Make(shader, "DD_Zombie_Flesh", new Color32(0x7E, 0x86, 0x74, 0xFF), 0.75f),
                Make(shader, "DD_Zombie_MetalOxidised", new Color32(0x4C, 0x51, 0x49, 0xFF), 0.42f, 0.55f),
                Make(shader, "DD_Zombie_MetalDark", new Color32(0x30, 0x32, 0x37, 0xFF), 0.35f, 0.65f),
                Make(shader, "DD_Zombie_SashRed", new Color32(0x70, 0x20, 0x22, 0xFF), 0.85f),
                Make(shader, "DD_Zombie_SashDark", new Color32(0x22, 0x22, 0x2A, 0xFF), 0.9f),
                Make(shader, "DD_Zombie_UnderCloth", new Color32(0x5C, 0x53, 0x46, 0xFF), 0.95f)
            };
        }

        /// <summary>
        /// The zombie surface shader, from the serialized library.
        ///
        /// This used to walk down Shader.Find("URP/Lit") -> Simple Lit -> Standard. In a
        /// Player build all three string lookups after the first were dead ends and the
        /// last one succeeded, so every zombie shipped wearing a Built-in pipeline shader
        /// URP cannot draw. The warning it printed was the only trace, and it was easy to
        /// read as harmless. There is no fallback now: under URP a non-URP shader is not a
        /// degraded picture, it is a broken one.
        /// </summary>
        private static Shader ResolveShader()
        {
            return PrototypeShaders.Lit;
        }

        /// <param name="roughness">0 = mirror, 1 = fully matte. Cloth sits near 0.9.</param>
        private static Material Make(Shader shader, string name, Color colour, float roughness, float metallic = 0f)
        {
            float smoothness = roughness;
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };

            // URP Lit and Standard disagree on property names; set whichever exists.
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", colour);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", colour);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 1f - smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 1f - smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            material.enableInstancing = true;
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cube = null;
            s_sphere = null;
            s_capsule = null;
            s_cloth = null;
            s_accent = null;
        }
    }
}
