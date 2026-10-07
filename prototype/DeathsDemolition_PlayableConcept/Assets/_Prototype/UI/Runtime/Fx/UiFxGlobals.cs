using DD.Prototype.Core;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 4 · The DD/UI/Fx shader globals (§2.5 names): <c>_DD_UiTime</c> (unscaled), <c>_DD_WorldTime</c>,
    /// <c>_DD_Motion</c> (0 under reduced motion), <c>_DD_Flash</c> (<see cref="UiTokens.FlashScale"/>), the palette
    /// (<c>_DD_Ink</c> / <c>_DD_Paper</c> / leaf tints) and the kit textures. One process-long driver writes them every
    /// frame before the canvases render (no allocation: cached property ids, value types only).
    /// Slice 4c: the kit is AP-012 v004 (<c>Resources/Fx/</c>) — the A8 RGBA time map (one mark per channel, each in its
    /// own rect: <see cref="InkRects"/>), the kinpaku leaf tile, the washi paper tile and the four <c>Ink.*</c> static
    /// masks (<see cref="InkMask"/>). Missing textures degrade (ink shows at once, flat leaf / paper), never throw.
    /// WP-081 ikon v2 B5b: <c>_DD_IconTier</c>, the icon material's tier switch, is written once at install and on
    /// <see cref="QualityTiers.Changed"/> - not per frame (<see cref="WriteIconTier"/>).
    /// </summary>
    public static class UiFxGlobals
    {
        public const string InkKitResource = "Fx/AP012_v004_ink_timemaps_1024";
        public const string LeafTileResource = "Fx/AP012_tile_kinpaku_128";
        public const string PaperTileResource = "Fx/AP012_tile_washi_256";

        /// <summary>
        /// Each channel's mark rect in the kit (x, y, w, h; bottom-left UV) — AP-012 v004 manifest <c>ink_kit.elements</c>
        /// <c>uv_rect_bottom_left</c>: R Ink.BrushUnderline, G Ink.BrushDivider, B Ink.Hanko, A Ink.Blot.
        /// </summary>
        public static readonly Vector4[] InkRects =
        {
            new Vector4(0f, 0.75f, 1f, 0.25f),
            new Vector4(0f, 0.625f, 1f, 0.125f),
            new Vector4(0f, 0.125f, 0.5f, 0.5f),
            new Vector4(0.5f, 0.125f, 0.5f, 0.5f)
        };

        /// <summary>
        /// Each channel's 99th-percentile time inside its mark (measured on the v004 kit PNG): the shader reads m / max so a
        /// mark's last ink lands at t = 1 instead of at ~0.8 of the timeline.
        /// </summary>
        public static readonly Vector4 InkTimeMax = new Vector4(0.812f, 0.855f, 0.796f, 0.918f);

        private static readonly string[] MaskNames = { "Ink.BrushUnderline", "Ink.BrushDivider", "Ink.Hanko", "Ink.Blot" };

        private static readonly int UiTimeId = Shader.PropertyToID("_DD_UiTime");
        private static readonly int WorldTimeId = Shader.PropertyToID("_DD_WorldTime");
        private static readonly int MotionId = Shader.PropertyToID("_DD_Motion");
        private static readonly int FlashId = Shader.PropertyToID("_DD_Flash");
        private static readonly int InkId = Shader.PropertyToID("_DD_Ink");
        private static readonly int PaperId = Shader.PropertyToID("_DD_Paper");
        private static readonly int GoldLeafId = Shader.PropertyToID("_DD_GoldLeaf");
        private static readonly int SilverLeafId = Shader.PropertyToID("_DD_SilverLeaf");
        private static readonly int InkMapId = Shader.PropertyToID("_DD_InkMap");
        private static readonly int InkMapSizeId = Shader.PropertyToID("_DD_InkMapSize");
        private static readonly int InkRectId = Shader.PropertyToID("_DD_InkRect");
        private static readonly int InkTimeMaxId = Shader.PropertyToID("_DD_InkTimeMax");
        private static readonly int LeafTileId = Shader.PropertyToID("_DD_LeafTile");
        private static readonly int PaperTileId = Shader.PropertyToID("_DD_PaperTile");
        private static readonly int IconTierId = Shader.PropertyToID("_DD_IconTier");

        private static Texture s_inkMap, s_leafTile, s_paperTile;
        private static readonly Sprite[] s_masks = new Sprite[4];
        private static bool s_installed;

        /// <summary>The unscaled clock the shader reads (a UiFx start time is stamped on the same clock); the motion core's
        /// test clock (<see cref="UiMotion.TestNow"/>) drives it too, so tween and shader timelines stay on one clock.</summary>
        public static float Now => TimeOverride ?? UiMotion.TestNow ?? Time.unscaledTime;

        /// <summary>Captures / tests: freeze the Fx clock at a value (null = live).</summary>
        public static float? TimeOverride;

        /// <summary>Tests: force reduced motion / a flash scale regardless of the player settings (null = settings).</summary>
        public static bool? MotionOverride;
        public static float? FlashOverride;

        /// <summary>Tests: force the icon tier switch (1 full material, 0 flat token) regardless of the quality tier (null = the tier).</summary>
        public static float? IconTierOverride;

        public static Texture InkMap => s_inkMap;
        public static Texture LeafTile => s_leafTile;
        public static Texture PaperTile => s_paperTile;

        public static void EnsureInstalled()
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;
            LoadKit();
            Write();
            QualityTiers.Changed -= OnTierChanged;
            QualityTiers.Changed += OnTierChanged;
            WriteIconTier();
            if (Application.isPlaying)
            {
                var go = new GameObject("[UiFxGlobals]");
                go.hideFlags = HideFlags.HideInHierarchy;
                Object.DontDestroyOnLoad(go);
                go.AddComponent<UiFxGlobalsDriver>();
            }
        }

        /// <summary>The static mask (white + alpha sprite) of a kit mark — the shape its time map inks in. Null when the kit is missing.</summary>
        public static Sprite InkMask(UiInkChannel channel)
        {
            int i = (int)channel;
            if (s_masks[i] == null)
            {
                s_masks[i] = Resources.Load<Sprite>("Fx/" + MaskNames[i]);
            }

            return s_masks[i];
        }

        private static void LoadKit()
        {
            s_inkMap = Resources.Load<Texture2D>(InkKitResource);
            s_leafTile = Resources.Load<Texture2D>(LeafTileResource);
            s_paperTile = Resources.Load<Texture2D>(PaperTileResource);
            if (s_inkMap == null)
            {
                Debug.LogWarning("[UiFxGlobals] ink kit missing (" + InkKitResource + "): marks ink at once");
            }

            Shader.SetGlobalTexture(InkMapId, s_inkMap != null ? s_inkMap : Texture2D.blackTexture);
            Shader.SetGlobalTexture(LeafTileId, s_leafTile != null ? s_leafTile : Texture2D.whiteTexture);
            Shader.SetGlobalTexture(PaperTileId, s_paperTile != null ? s_paperTile : Texture2D.whiteTexture);
            float w = s_inkMap != null ? s_inkMap.width : 4f, h = s_inkMap != null ? s_inkMap.height : 4f;
            Shader.SetGlobalVector(InkMapSizeId, new Vector4(1f / w, 1f / h, w, h));
            Shader.SetGlobalVectorArray(InkRectId, InkRects);
            Shader.SetGlobalVector(InkTimeMaxId, InkTimeMax);
        }

        /// <summary>Called once per frame by the driver; tests call it directly.</summary>
        public static void Write()
        {
            Shader.SetGlobalFloat(UiTimeId, Now);
            Shader.SetGlobalFloat(WorldTimeId, Time.time);
            bool reduced = MotionOverride.HasValue ? !MotionOverride.Value : UiMotion.ReducedMotion;   // the core's single RM source (mirrors UiSettings)
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
        /// WP-081 ikon v2 B5b · the icon material per tier (unity-material §6.1): Cinematic = High = the full material
        /// (1), Performance = the flat token, today's look (0). Reduced Motion and Reduce Flashes are not tiers - they hold in
        /// every tier through <c>_DD_Motion</c> / <c>_DD_Flash</c>. Tests call it after setting <see cref="IconTierOverride"/>.
        /// </summary>
        public static void WriteIconTier()
        {
            Shader.SetGlobalFloat(IconTierId, IconTierOverride ?? QualityTiers.Pick(0f, 1f, 1f));
        }

        private static void OnTierChanged(QualityTier tier)
        {
            Shader.SetGlobalFloat(IconTierId, IconTierOverride ?? QualityTiers.Pick(tier, 0f, 1f, 1f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_installed = false;
            MotionOverride = null;
            FlashOverride = null;
            TimeOverride = null;
            IconTierOverride = null;
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
