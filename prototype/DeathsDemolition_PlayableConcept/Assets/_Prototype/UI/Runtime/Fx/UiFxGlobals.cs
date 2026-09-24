using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 4 · The DD/UI/Fx shader globals (§2.5 names): <c>_DD_UiTime</c> (unscaled), <c>_DD_WorldTime</c>,
    /// <c>_DD_Motion</c> (0 under reduced motion), <c>_DD_Flash</c> (<see cref="UiTokens.FlashScale"/>), the palette
    /// (<c>_DD_Ink</c> / <c>_DD_Paper</c> / leaf tints) and the ink kit <c>_DD_InkMap</c>. One process-long driver writes
    /// them every frame before the canvases render (no allocation: cached property ids, value types only).
    /// The ink kit is a procedural placeholder (<see cref="BuildPlaceholderInkMap"/>) until the AP A8 BC4 kit lands;
    /// <see cref="SetInkMap"/> is the swap.
    /// </summary>
    public static class UiFxGlobals
    {
        public const int InkMapSize = 256;

        private static readonly int UiTimeId = Shader.PropertyToID("_DD_UiTime");
        private static readonly int WorldTimeId = Shader.PropertyToID("_DD_WorldTime");
        private static readonly int MotionId = Shader.PropertyToID("_DD_Motion");
        private static readonly int FlashId = Shader.PropertyToID("_DD_Flash");
        private static readonly int InkId = Shader.PropertyToID("_DD_Ink");
        private static readonly int PaperId = Shader.PropertyToID("_DD_Paper");
        private static readonly int GoldLeafId = Shader.PropertyToID("_DD_GoldLeaf");
        private static readonly int SilverLeafId = Shader.PropertyToID("_DD_SilverLeaf");
        private static readonly int InkMapId = Shader.PropertyToID("_DD_InkMap");

        private static Texture2D s_placeholder;
        private static Texture s_inkMap;
        private static bool s_installed;

        /// <summary>The unscaled clock the shader reads (a UiFx start time is stamped on the same clock).</summary>
        public static float Now => TimeOverride ?? Time.unscaledTime;

        /// <summary>Captures / tests: freeze the Fx clock at a value (null = live).</summary>
        public static float? TimeOverride;

        /// <summary>Tests: force reduced motion / a flash scale regardless of the player settings (null = settings).</summary>
        public static bool? MotionOverride;
        public static float? FlashOverride;

        public static Texture InkMap => s_inkMap;

        public static void EnsureInstalled()
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;
            if (s_inkMap == null)
            {
                SetInkMap(PlaceholderInkMap);
            }

            Write();
            if (Application.isPlaying)
            {
                var go = new GameObject("[UiFxGlobals]");
                go.hideFlags = HideFlags.HideInHierarchy;
                Object.DontDestroyOnLoad(go);
                go.AddComponent<UiFxGlobalsDriver>();
            }
        }

        /// <summary>The A8 swap: any RGBA texture with the kit's channel layout (R brush, G bleed, B hanko, A leaf crack).</summary>
        public static void SetInkMap(Texture map)
        {
            s_inkMap = map != null ? map : PlaceholderInkMap;
            Shader.SetGlobalTexture(InkMapId, s_inkMap);
        }

        public static Texture2D PlaceholderInkMap
        {
            get
            {
                if (s_placeholder == null)
                {
                    s_placeholder = BuildPlaceholderInkMap(InkMapSize);
                }

                return s_placeholder;
            }
        }

        /// <summary>Called once per frame by the driver; tests call it directly.</summary>
        public static void Write()
        {
            Shader.SetGlobalFloat(UiTimeId, Now);
            Shader.SetGlobalFloat(WorldTimeId, Time.time);
            bool reduced = MotionOverride.HasValue ? !MotionOverride.Value : UiSettings.ReducedMotion;
            Shader.SetGlobalFloat(MotionId, reduced ? 0f : 1f);
            Shader.SetGlobalFloat(FlashId, FlashOverride ?? UiTokens.FlashScale);
            Shader.SetGlobalColor(InkId, UiTokens.Get(UiColor.SurfaceInset));
            Shader.SetGlobalColor(PaperId, UiTokens.Get(UiColor.TextPrimary));
            Shader.SetGlobalColor(GoldLeafId, UiTokens.Get(UiColor.StateGold));
            Shader.SetGlobalColor(SilverLeafId, UiTokens.Get(UiColor.TextPrimary));
            if (s_inkMap != null)
            {
                Shader.SetGlobalTexture(InkMapId, s_inkMap);
            }
        }

        /// <summary>
        /// Deterministic RGBA32 placeholder for the A8 ink kit (time maps: 0 = first ink, 1 = empty).
        /// R: a horizontal brush stroke — inks left → right with a ragged, fibrous front and dry-brush streaks.
        /// G: a bleed / leak — spreads from the centre along noisy fingers.
        /// B: a hanko rim — mostly early, a rough ring of late (unevenly inked) texels at the edge.
        /// A: a kinpaku crack tile (repeats): bright leaf with dark cracks.
        /// </summary>
        public static Texture2D BuildPlaceholderInkMap(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "DD_UiInkMap (placeholder, A8 pending)",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;

                    // R — brush: position along the stroke + a fibrous front (per-row noise) + dry streaks.
                    float fibre = Fbm(u * 3f, v * 26f, 3) - 0.5f;
                    float streak = Fbm(3.1f, v * 40f, 2);
                    float brush = u * 0.96f + fibre * 0.12f + (streak > 0.62f ? 0.06f : 0f) + 0.02f;   // AD MUST 3: spans the envelope (last ink at eased t ≥ 0.95)

                    // G — bleed: radial distance warped by noise fingers.
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float warp = Fbm(u * 5f + 11f, v * 5f + 7f, 4) - 0.5f;
                    float bleed = Mathf.Sqrt(dx * dx + dy * dy) * 1.35f + warp * 0.35f;

                    // B — hanko: early inside, a rough late rim.
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float rim = Mathf.Clamp01((r - 0.72f) / 0.28f);
                    float hanko = Mathf.Clamp01(rim * 0.8f + (Fbm(u * 18f, v * 18f, 3) - 0.35f) * 0.6f * rim);

                    // A — crack tile (tileable: noise sampled on a torus).
                    float crackNoise = TileFbm(u, v, 6f);
                    float crack = Mathf.Abs(crackNoise - 0.5f) < 0.035f ? 0.15f : 0.85f + (crackNoise - 0.5f) * 0.3f;

                    px[y * size + x] = new Color32(
                        (byte)(Mathf.Clamp01(brush) * 255f), (byte)(Mathf.Clamp01(bleed) * 255f),
                        (byte)(hanko * 255f), (byte)(Mathf.Clamp01(crack) * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        private static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x, y) * amp;
                norm += amp;
                x *= 2.03f; y *= 2.03f; amp *= 0.5f;
            }

            return sum / norm;
        }

        private static float TileFbm(float u, float v, float scale)
        {
            // Four-corner blend makes the tile repeat seamlessly.
            float a = Fbm(u * scale, v * scale, 3);
            float b = Fbm((u - 1f) * scale, v * scale, 3);
            float c = Fbm(u * scale, (v - 1f) * scale, 3);
            float d = Fbm((u - 1f) * scale, (v - 1f) * scale, 3);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_installed = false;
            MotionOverride = null;
            FlashOverride = null;
            TimeOverride = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInstall()
        {
            EnsureInstalled();
        }
    }

    /// <summary>Writes the Fx globals once a frame, before canvases render (late in the frame, after screen Updates).</summary>
    [DefaultExecutionOrder(950)]
    internal sealed class UiFxGlobalsDriver : MonoBehaviour
    {
        private void LateUpdate()
        {
            UiFxGlobals.Write();
        }
    }
}
