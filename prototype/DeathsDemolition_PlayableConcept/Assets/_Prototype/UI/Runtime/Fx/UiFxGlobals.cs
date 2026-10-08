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
    /// WP-098A (C-00 08.10): AP-034 v002.1's two icon pages - I1 <c>_DD_IconCavity</c> (cavity / ink density on G1's UVs) and
    /// I2 <c>_DD_IconGrain</c> (the ink grain, 1:1 in screen pixels) - are bound once at install, never per frame (a global
    /// texture keeps its binding); missing, they bind white: no cavity, no grain, no dry tail.
    /// </summary>
    public static class UiFxGlobals
    {
        public const string InkKitResource = "Fx/AP012_v004_ink_timemaps_1024";
        public const string LeafTileResource = "Fx/AP012_tile_kinpaku_128";
        public const string PaperTileResource = "Fx/AP012_tile_washi_256";

        /// <summary>WP-098A · AP-034 v002.1 I1: R cavity / AO, G ink density on G1's UVs one-to-one (linear, clamp, no mips).</summary>
        public const string IconCavityResource = "Fx/ap034_icons_g1cavity_1024";

        /// <summary>WP-098A · AP-034 v002.1 I2: the ink grain tile (one linear channel, repeat), read 1:1 in screen pixels.</summary>
        public const string IconGrainResource = "Fx/ap034_tile_inkgrain_256";

        /// <summary>WP-098A (M14) · AP-034 v001.1 M3, the full-screen brush wipe's time map (<see cref="UiInkChannel.Wipe"/>; linear, clamp).</summary>
        public const string InkWipeResource = "Fx/ap034_kit_m3wipe_1024x512_time";

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
        private static readonly int IconCavityId = Shader.PropertyToID("_DD_IconCavity");
        private static readonly int IconGrainId = Shader.PropertyToID("_DD_IconGrain");
        private static readonly int InkWipeId = Shader.PropertyToID("_DD_InkWipe");

        private static Texture s_inkMap, s_leafTile, s_paperTile, s_iconCavity, s_iconGrain, s_inkWipe;
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
        public static Texture IconCavity => s_iconCavity;
        public static Texture IconGrain => s_iconGrain;
        public static Texture InkWipe => s_inkWipe;   // WP-098A: null = no brush curtain (the doors stay)

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
            if (i >= s_masks.Length) return UiSprites.Solid;   // WP-098A: the Wipe channel's mark is the whole rect
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

            // WP-098A: the icon pages, once (white = the neutral cavity / ink / grain: the row's ink terms read 0).
            s_iconCavity = Resources.Load<Texture2D>(IconCavityResource);
            s_iconGrain = Resources.Load<Texture2D>(IconGrainResource);
            Shader.SetGlobalTexture(IconCavityId, s_iconCavity != null ? s_iconCavity : Texture2D.whiteTexture);
            Shader.SetGlobalTexture(IconGrainId, s_iconGrain != null ? s_iconGrain : Texture2D.whiteTexture);
            s_inkWipe = Resources.Load<Texture2D>(InkWipeResource);   // WP-098A: once; white = empty (nothing inks)
            Shader.SetGlobalTexture(InkWipeId, s_inkWipe != null ? s_inkWipe : Texture2D.whiteTexture);
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
