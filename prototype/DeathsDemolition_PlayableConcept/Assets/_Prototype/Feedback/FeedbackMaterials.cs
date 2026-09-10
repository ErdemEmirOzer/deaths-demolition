using DD.Prototype.Rendering;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// Flat-colour materials built in code, because this prototype ships no art assets
    /// and no authored prefabs.
    ///
    /// Two families, and the difference matters:
    ///
    ///   * Mesh / line materials (<see cref="Opaque"/>, <see cref="Alpha"/>,
    ///     <see cref="Additive"/>) are coloured per renderer through a
    ///     MaterialPropertyBlock. URP's Unlit shader ignores vertex colour, so a
    ///     property block is the only way to fade one tracer without instancing a
    ///     material for it.
    ///   * Particle materials (<see cref="ParticleAlpha"/>) must respect per-particle
    ///     vertex colour, because that is how <c>ParticleSystem.main.startColor</c>
    ///     reaches the screen. URP's Unlit shader would silently ignore it and every
    ///     burst would come out white - which is exactly the kind of bug that gets
    ///     written off as "placeholder art looks bad".
    ///
    /// Everything is cached: a colour asked for twice returns the same material.
    /// </summary>
    public static class FeedbackMaterials
    {
        private static readonly Dictionary<int, Material> s_opaque = new Dictionary<int, Material>(16);
        private static readonly Dictionary<int, Material> s_additive = new Dictionary<int, Material>(16);
        private static readonly Dictionary<int, Material> s_alpha = new Dictionary<int, Material>(16);

        private static Shader s_meshShader;
        private static Shader s_particleShader;
        private static Material s_particleAlpha;

        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int ColorId = Shader.PropertyToID("_Color");

        private enum BlendKind
        {
            Opaque,
            Alpha,
            Additive
        }

        private static Shader MeshShader
        {
            get
            {
                if (s_meshShader != null)
                {
                    return s_meshShader;
                }

                // Was Shader.Find with a Sprites/Default -> Unlit/Color ladder beneath it.
                // Both rungs are Built-in pipeline shaders; under URP they do not draw a
                // simpler version of the effect, they draw a broken one.
                s_meshShader = PrototypeShaders.Unlit;
                return s_meshShader;
            }
        }

        private static Shader ParticleShader
        {
            get
            {
                if (s_particleShader != null)
                {
                    return s_particleShader;
                }

                s_particleShader = PrototypeShaders.ParticlesUnlit;
                return s_particleShader;
            }
        }

        /// <summary>Solid, depth-writing. Debris shards, the procedural weapon body.</summary>
        public static Material Opaque(Color color) => Get(s_opaque, color, BlendKind.Opaque);

        /// <summary>Alpha-blended mesh material. Soft panels, beacon column.</summary>
        public static Material Alpha(Color color) => Get(s_alpha, color, BlendKind.Alpha);

        /// <summary>Additive mesh / line material. Muzzle flash, tracer, chain arc.</summary>
        public static Material Additive(Color color) => Get(s_additive, color, BlendKind.Additive);

        /// <summary>
        /// The single shared particle material. White on purpose: every burst colours
        /// itself through <c>startColor</c>, so one material serves all of them and the
        /// draw calls batch.
        /// </summary>
        public static Material ParticleAlpha()
        {
            if (s_particleAlpha != null)
            {
                return s_particleAlpha;
            }

            s_particleAlpha = new Material(ParticleShader)
            {
                name = "Feedback_ParticleAlpha",
                hideFlags = HideFlags.HideAndDontSave
            };

            ApplyColor(s_particleAlpha, Color.white);
            ApplyTransparency(s_particleAlpha, false);
            return s_particleAlpha;
        }

        private static Material Get(Dictionary<int, Material> cache, Color color, BlendKind kind)
        {
            int key = ColorKey(color);
            if (cache.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            var material = new Material(MeshShader)
            {
                name = $"Feedback_{kind}_{key:X8}",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };

            ApplyColor(material, color);

            if (kind != BlendKind.Opaque)
            {
                ApplyTransparency(material, kind == BlendKind.Additive);
            }

            cache[key] = material;
            return material;
        }

        /// <summary>Writes a colour into whichever colour property the resolved shader exposes.</summary>
        public static void ApplyColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }

            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }
        }

        /// <summary>Writes a colour into a property block, covering both property names.</summary>
        public static void ApplyColor(MaterialPropertyBlock block, Color color)
        {
            if (block == null)
            {
                return;
            }

            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
        }

        private static void ApplyTransparency(Material material, bool additive)
        {
            // Driven by property existence rather than by which shader was found, so the
            // same call configures URP Unlit, URP Particles/Unlit and the sprite fallback.
            SetIfPresent(material, "_Surface", 1f);
            SetIfPresent(material, "_Blend", additive ? 1f : 0f);
            SetIfPresent(material, "_ZWrite", 0f);
            SetIfPresent(material, "_AlphaClip", 0f);
            SetIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetIfPresent(material, "_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static int ColorKey(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            return (r << 24) | (g << 16) | (b << 8) | a;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_opaque.Clear();
            s_alpha.Clear();
            s_additive.Clear();
            s_meshShader = null;
            s_particleShader = null;
            s_particleAlpha = null;
        }
    }
}
