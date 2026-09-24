using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-047D · the koi pond's look (S-05 WP-038 `DD/Water v2`) read once per scene so the water VFX match it:
    /// the splash / ring tint comes from the material's shallow colour, the Tide arc from the deep colour. No pond
    /// in the scene = the neutral defaults. The scan allocates (FindObjectsOfType, material wrappers), so it runs from
    /// <see cref="PrimeAtRoundStart"/> (the dressing's RoundStarted hook — the world is built by then), never inside a hit
    /// (WP-073: the first water hit of a round paid it, ≈ 10 900 allocations for the shader-name reads of every renderer).
    /// A read before any round start still scans lazily.
    /// </summary>
    public static class WaterLook
    {
        private static readonly int ShallowId = Shader.PropertyToID("_ShallowColor");
        private static readonly int BaseId = Shader.PropertyToID("_BaseColor");
        private static readonly Color DefaultSplash = new Color(0.86f, 0.94f, 0.97f, 1f);
        private static readonly Color DefaultArc = new Color(0.55f, 0.78f, 0.86f, 1f);

        private static int s_sceneStamp = -1;
        private static Color s_splash = DefaultSplash;
        private static Color s_arc = DefaultArc;

        public static bool Found { get; private set; }

        private static readonly System.Collections.Generic.HashSet<int> s_otherShaders = new System.Collections.Generic.HashSet<int>();

        public static Color SplashTint { get { Refresh(); return s_splash; } }
        public static Color ArcTint { get { Refresh(); return s_arc; } }

        /// <summary>Round start: scan the active scene unless it was already scanned with the pond found.</summary>
        public static void PrimeAtRoundStart()
        {
            if (!Found)
            {
                s_sceneStamp = -1;
            }

            Refresh();
        }

        private static void Refresh()
        {
            int stamp = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (stamp == s_sceneStamp)
            {
                return;
            }

            s_sceneStamp = stamp;
            s_splash = DefaultSplash;
            s_arc = DefaultArc;
            Found = false;
            s_otherShaders.Clear();
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material m = renderers[i].sharedMaterial;
                Shader shader = m != null ? m.shader : null;
                // Shader.name allocates a string per read: each distinct shader is named once.
                if (shader == null || s_otherShaders.Contains(shader.GetInstanceID()))
                {
                    continue;
                }

                if (shader.name != "DD/Water v2")
                {
                    s_otherShaders.Add(shader.GetInstanceID());
                    continue;
                }

                Color shallow = m.HasProperty(ShallowId) ? m.GetColor(ShallowId) : DefaultSplash;
                Color deep = m.HasProperty(BaseId) ? m.GetColor(BaseId) : DefaultArc;
                // Splash: the shallow colour lifted toward white (foam); arc: the deep colour brightened (the well's light).
                s_splash = Color.Lerp(new Color(shallow.r, shallow.g, shallow.b, 1f), Color.white, 0.55f);
                s_arc = new Color(Mathf.Clamp01(deep.r * 2.2f + 0.2f), Mathf.Clamp01(deep.g * 2.0f + 0.25f), Mathf.Clamp01(deep.b * 1.9f + 0.3f), 1f);
                Found = true;
                break;
            }
        }

        /// <summary>Test seam: forget the scene (a scene loaded without a reload).</summary>
        public static void Reset()
        {
            s_sceneStamp = -1;
        }
    }
}
