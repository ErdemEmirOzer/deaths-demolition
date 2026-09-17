using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-047D · the koi pond's look (S-05 WP-038 `DD/Water v2`) read once per scene so the water VFX match it:
    /// the splash / ring tint comes from the material's shallow colour, the Tide arc from the deep colour. No pond
    /// in the scene = the neutral defaults. Scanned lazily on first use (FindObjectsOfType allocates — never per hit
    /// after the first, and the first happens in the warm-up).
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

        public static Color SplashTint { get { Refresh(); return s_splash; } }
        public static Color ArcTint { get { Refresh(); return s_arc; } }

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
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material m = renderers[i].sharedMaterial;
                if (m == null || m.shader == null || m.shader.name != "DD/Water v2")
                {
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
