using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// Fades one occluding object out and back in. Added automatically by
    /// <see cref="CameraOcclusionFader"/> the first time an object gets in the way; it
    /// then stays on the object and costs nothing while the object is clear.
    ///
    /// WHY MATERIAL SWAP AND NOT MaterialPropertyBlock
    /// ------------------------------------------------
    /// A MaterialPropertyBlock can override _BaseColor, but opacity in URP/Lit is not a
    /// property, it is a SURFACE MODE: it needs the _Surface float, the
    /// _SURFACE_TYPE_TRANSPARENT shader keyword, _SrcBlend / _DstBlend, _ZWrite and a
    /// render queue in the transparent range. Keywords and render queue are per
    /// material, not per renderer, so a property block physically cannot make an opaque
    /// URP/Lit material translucent - it would only darken it.
    ///
    /// So this component:
    ///   1. remembers the original sharedMaterials array (one reference, no copy),
    ///   2. on the first occlusion, builds a per-renderer clone of each material and
    ///      switches that clone to Transparent once,
    ///   3. animates only _BaseColor.a on the clone while fading,
    ///   4. puts the original shared materials back when the object is clear again.
    ///
    /// The consequences are deliberate: while an object is clear it renders with the
    /// original shared material and batches exactly as before, so the common case pays
    /// nothing. Only genuinely occluding objects hold a material clone, and there are
    /// at most a handful at a time. The clones are destroyed with the component.
    ///
    /// Known prototype limitation, accepted: transparent rendering writes no depth, so
    /// two overlapping faded occluders can sort oddly against each other. That is
    /// cheaper and far more readable than dissolve or stencil work in a blockout.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OccluderFadeState : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");

        private sealed class Entry
        {
            public Renderer Renderer;
            public Material[] Original;
            public Material[] Faded;
            public Color[] BaseColors;
            public ShadowCastingMode OriginalShadows;
            public bool Swapped;
        }

        private readonly List<Entry> _entries = new List<Entry>(4);

        private float _alpha = 1f;
        private float _targetAlpha = 1f;
        private float _fadeTime = 0.15f;
        private int _markedFrame = -1;
        private bool _collected;

        /// <summary>Current opacity, 1 when fully restored.</summary>
        public float Alpha
        {
            get { return _alpha; }
        }

        /// <summary>True while this object is faded at all.</summary>
        public bool IsFaded
        {
            get { return _alpha < 0.999f; }
        }

        /// <summary>
        /// Called by the fader every frame the object blocks the view. Not calling it
        /// is what makes the object fade back in.
        /// </summary>
        public void MarkOccluding(float targetAlpha, float fadeTime)
        {
            _markedFrame = Time.frameCount;
            _targetAlpha = Mathf.Clamp01(targetAlpha);
            _fadeTime = Mathf.Max(0.01f, fadeTime);

            // The component switches itself off once it is fully restored, so a wall
            // that is not in the way costs zero per-frame work.
            enabled = true;
        }

        private void LateUpdate()
        {
            // One frame of grace, so a probe that flickers for a single frame does not
            // produce a visible pop.
            bool occluding = _markedFrame >= Time.frameCount - 1;
            float target = occluding ? _targetAlpha : 1f;

            if (Mathf.Approximately(_alpha, target))
            {
                if (!occluding && _alpha >= 0.999f)
                {
                    Restore();
                    enabled = false;
                }

                return;
            }

            float step = Time.deltaTime / _fadeTime;
            _alpha = Mathf.MoveTowards(_alpha, target, step);

            if (_alpha >= 0.999f)
            {
                _alpha = 1f;
                Restore();
                enabled = false;
                return;
            }

            EnsureCollected();
            EnsureSwapped();
            WriteAlpha();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.Faded == null)
                {
                    continue;
                }

                for (int m = 0; m < e.Faded.Length; m++)
                {
                    if (e.Faded[m] != null)
                    {
                        Destroy(e.Faded[m]);
                    }
                }
            }

            _entries.Clear();
        }

        private void EnsureCollected()
        {
            if (_collected)
            {
                return;
            }

            _collected = true;

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r is ParticleSystemRenderer)
                {
                    continue;
                }

                Material[] shared = r.sharedMaterials;
                if (shared == null || shared.Length == 0)
                {
                    continue;
                }

                _entries.Add(new Entry
                {
                    Renderer = r,
                    Original = shared,
                    OriginalShadows = r.shadowCastingMode
                });
            }
        }

        private void EnsureSwapped()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.Swapped || e.Renderer == null)
                {
                    continue;
                }

                if (e.Faded == null)
                {
                    e.Faded = new Material[e.Original.Length];
                    e.BaseColors = new Color[e.Original.Length];

                    for (int m = 0; m < e.Original.Length; m++)
                    {
                        Material source = e.Original[m];
                        if (source == null)
                        {
                            continue;
                        }

                        var clone = new Material(source);
                        clone.name = source.name + " (Occluder Fade)";
                        clone.hideFlags = HideFlags.HideAndDontSave;
                        e.BaseColors[m] = ReadBaseColor(clone);
                        MakeTransparent(clone);
                        e.Faded[m] = clone;
                    }
                }

                e.Renderer.sharedMaterials = e.Faded;

                // Keep the shadow: the silhouette on the ground is what tells the
                // player the wall is still there.
                e.Renderer.shadowCastingMode = e.OriginalShadows == ShadowCastingMode.Off
                    ? ShadowCastingMode.Off
                    : ShadowCastingMode.On;

                e.Swapped = true;
            }
        }

        private void WriteAlpha()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (!e.Swapped || e.Faded == null)
                {
                    continue;
                }

                for (int m = 0; m < e.Faded.Length; m++)
                {
                    Material mat = e.Faded[m];
                    if (mat == null)
                    {
                        continue;
                    }

                    Color c = e.BaseColors[m];
                    c.a *= _alpha;

                    if (mat.HasProperty(BaseColorId))
                    {
                        mat.SetColor(BaseColorId, c);
                    }

                    if (mat.HasProperty(ColorId))
                    {
                        mat.SetColor(ColorId, c);
                    }
                }
            }
        }

        private void Restore()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (!e.Swapped || e.Renderer == null)
                {
                    continue;
                }

                e.Renderer.sharedMaterials = e.Original;
                e.Renderer.shadowCastingMode = e.OriginalShadows;
                e.Swapped = false;
            }
        }

        private static Color ReadBaseColor(Material mat)
        {
            if (mat.HasProperty(BaseColorId))
            {
                return mat.GetColor(BaseColorId);
            }

            if (mat.HasProperty(ColorId))
            {
                return mat.GetColor(ColorId);
            }

            return Color.white;
        }

        /// <summary>
        /// Switches a URP/Lit or URP/Unlit material to the Transparent surface mode at
        /// runtime. Mirrors what the URP shader GUI writes when you flip the Surface
        /// Type dropdown, which is the only supported way to do this: setting alpha
        /// alone on an opaque material does nothing.
        ///
        /// Falls back to the built-in Standard shader's Fade mode so the fader still
        /// works if a blockout piece ends up on a non-URP material.
        /// </summary>
        public static void MakeTransparent(Material mat)
        {
            if (mat == null)
            {
                return;
            }

            if (mat.HasProperty(SurfaceId))
            {
                mat.SetFloat(SurfaceId, 1f);                    // 0 opaque, 1 transparent

                if (mat.HasProperty(BlendId))
                {
                    mat.SetFloat(BlendId, 0f);                  // 0 alpha blend
                }

                if (mat.HasProperty(SrcBlendId))
                {
                    mat.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
                }

                if (mat.HasProperty(DstBlendId))
                {
                    mat.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
                }

                if (mat.HasProperty(ZWriteId))
                {
                    mat.SetFloat(ZWriteId, 0f);
                }

                if (mat.HasProperty(AlphaClipId))
                {
                    mat.SetFloat(AlphaClipId, 0f);
                }

                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.DisableKeyword("_ZWRITE_ON");
                mat.renderQueue = (int)RenderQueue.Transparent;
                return;
            }

            if (mat.HasProperty(ModeId))
            {
                mat.SetFloat(ModeId, 2f);                       // Standard shader: Fade
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
        }
    }
}
