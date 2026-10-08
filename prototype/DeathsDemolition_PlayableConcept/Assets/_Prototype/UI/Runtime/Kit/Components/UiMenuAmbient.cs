using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-098A (C-00 08.10; HAREKET-MENU M17, AP-034 P1) · the menu's drifting ash and leaves: two Fx Particles loops built once
    /// - <see cref="UiMenuMotionTokens.AmbientAsh"/> ash flakes (P1 row 1, 4–8 px, α ≤ 0.22, 13–24 px/s, a 14 s loop) and
    /// <see cref="UiMenuMotionTokens.AmbientLeaf"/> leaves (P1 row 2, 11–18 px, α ≤ 0.35, an 11 s loop) - scattered over x ∈
    /// [0.32 W, W] so they never cross the spine, in TextMuted (the kit's "muted": decoration, never a state colour). The GPU
    /// moves them (<c>_DD_UiTime</c>): the CPU writes nothing per frame. Reduced Motion freezes them where they are (the shader's
    /// Decor rule). <see cref="SetShown"/> fades the layer out over Base 220 (a pane open, the window unfocused) and back over
    /// Emphasis 400; the screen follows it with the 12 px near parallax (<see cref="UiBackdrop.Follow"/>).
    ///
    /// USAGE: <see cref="Create"/> once under the screen root, behind the page; <see cref="SetShown"/> on the state changes.
    /// DON'T: put motes behind words (the region keeps out of the spine), or raise the alphas (texture, not content).
    /// ACCESSIBILITY: decoration only, ≤ 0.35 in the muted tone; still under Reduced Motion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiMenuAmbient : MonoBehaviour
    {
        public const string ObjectName = "Ambient";

        private CanvasGroup _group;
        private UiFxParticles _ash;
        private UiFxParticles _leaves;
        private bool _shown = true;

        public bool Shown
        {
            get { return _shown; }
        }

        public CanvasGroup Group
        {
            get { return _group; }
        }

        public UiFxParticles Ash
        {
            get { return _ash; }
        }

        public UiFxParticles Leaves
        {
            get { return _leaves; }
        }

        public static UiMenuAmbient Create(RectTransform parent)
        {
            RectTransform region = UiFactory.NewRect(ObjectName, parent);
            region.anchorMin = new Vector2(UiMenuMotionTokens.AmbientRegionStart, 0f);
            region.anchorMax = new Vector2(1f, 1f);
            region.offsetMin = Vector2.zero;
            region.offsetMax = Vector2.zero;
            var ambient = region.gameObject.AddComponent<UiMenuAmbient>();
            ambient._group = UiFactory.Group(region.gameObject);
            ambient._group.blocksRaycasts = false;
            ambient._group.interactable = false;

            Sprite sheet = UiBrushArt.Get(UiBrushSprite.Motes);
            Texture texture = sheet != null ? sheet.texture : null;
            bool authored = UiBrushArt.IsAuthored(UiBrushSprite.Motes);
            int cells = authored ? UiBrushArt.MoteCellsPerRow : 1;
            ambient._ash = Emitter(region, "Ash", UiMenuMotionTokens.AmbientAsh, UiMenuMotionTokens.AshSize + 2f, UiMenuMotionTokens.AshSpeed + 5f,
                UiMenuMotionTokens.AshPeriod, UiMenuMotionTokens.AshAlpha, texture, UiBrushArt.MoteCell(UiBrushArt.AshCell), cells);
            ambient._leaves = Emitter(region, "Leaves", UiMenuMotionTokens.AmbientLeaf, UiMenuMotionTokens.LeafSize + 3f, UiMenuMotionTokens.LeafSpeed + 6f,
                UiMenuMotionTokens.LeafPeriod, UiMenuMotionTokens.LeafAlpha, texture, UiBrushArt.MoteCell(UiBrushArt.LeafCell), cells);
            return ambient;
        }

        /// <summary>
        /// One loop: <paramref name="count"/> quads of <paramref name="size"/> px (the shader draws 0.6–1 of it), falling at up to
        /// <paramref name="speed"/> px/s (0.55–1 of it) with a sway, over <paramref name="period"/> s, scattered over the region.
        /// </summary>
        private static UiFxParticles Emitter(RectTransform region, string name, int count, float size, float speed, float period, float alpha,
            Texture texture, Rect cell, int cells)
        {
            RectTransform rect = UiFactory.NewRect(name, region);
            UiFactory.Fill(rect);
            var particles = rect.gameObject.AddComponent<UiFxParticles>();
            Color tone = UiTokens.Get(UiColor.TextMuted);
            tone.a = alpha;
            particles.color = tone;
            particles.Configure(count, size, speed, 0f, period, period, texture);
            particles.Scatter(true).Cells(cell, cells);
            return particles;
        }

        /// <summary>Fade the layer out (Base 220, Exit) or back in (Emphasis 400, Settle); Decor - a cut under Reduced Motion.</summary>
        public void SetShown(bool shown)
        {
            if (shown == _shown || _group == null)
            {
                return;
            }

            _shown = shown;
            UiMotion.Alpha(_group, shown ? 1f : 0f, shown ? UiDuration.Emphasis : UiDuration.Base, shown ? UiEase.Settle : UiEase.Exit, UiMotionClass.Decor);
        }
    }
}
