using DD.Prototype.Core.InputAbstraction;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 §4 #26 / V1 · the one owner of the OS pointer. The hardware cursor is the V1 art (Cream100 fill,
    /// Sumi900 outline; Arrow, Press while a control is held), 32 px or 64 px on a Retina backing (RGBA32,
    /// <c>Cursor.SetCursor</c> once per change, never per frame, never ForceSoftware).
    ///
    /// Visibility = what the context wants AND the last device is not a gamepad: the menus, Pause and the shop want
    /// it, the run hides it (the reticle is the pointer), loading hides it; a pad hides it and the first mouse move
    /// (the scheme flips back) returns it. Screens and the curtain say what they want through
    /// <see cref="SetContextVisible"/> - nothing else writes <c>Cursor.visible</c>.
    /// </summary>
    public static class UiCursor
    {
        public const string ArrowName = "Cursor.Arrow";
        public const string PressName = "Cursor.Press";

        /// <summary>Hotspots from the AP-012 v004 manifest: (2, 1) at 32 px, (4, 2) at 64 px.</summary>
        public static readonly Vector2 Hotspot32 = new Vector2(2f, 1f);
        public static readonly Vector2 Hotspot64 = new Vector2(4f, 2f);

        /// <summary>Backing scale at which the 64 px art is used (Retina reports ≈ 220 dpi).</summary>
        public const float RetinaDpi = 144f;

        private static bool s_context = true;
        private static bool s_pressed;
        private static bool s_installed;
        private static Texture2D s_shown;

        /// <summary>What the current screen wants (true in menus, false in a run and under the loading curtain).</summary>
        public static bool ContextVisible
        {
            get { return s_context; }
        }

        /// <summary>The pointer's resulting visibility: the context wants it and the last device was not a pad.</summary>
        public static bool WantVisible
        {
            get { return s_context && UiInputScheme.Current != InputSchemeKind.Gamepad; }
        }

        public static bool Pressed
        {
            get { return s_pressed; }
        }

        public static void SetContextVisible(bool visible)
        {
            if (s_context == visible && s_installed && Cursor.visible == WantVisible)
            {
                return;
            }

            s_context = visible;
            Apply();
        }

        /// <summary>Press art while a kit control is held (UiButton pointer down / up).</summary>
        public static void SetPressed(bool pressed)
        {
            if (s_pressed == pressed)
            {
                return;
            }

            s_pressed = pressed;
            ApplyTexture();
        }

        /// <summary>The cursor texture for a state and backing (tests; null when the V1 art is absent).</summary>
        public static Texture2D Texture(bool pressed, bool retina)
        {
            return Resources.Load<Texture2D>(UiTheme.CursorFolder + (pressed ? PressName : ArrowName) + (retina ? "_64" : "_32"));
        }

        private static void Apply()
        {
            EnsureInstalled();
            bool visible = WantVisible;
            if (Cursor.visible != visible)
            {
                Cursor.visible = visible;
            }
        }

        private static void ApplyTexture()
        {
            if (Application.isBatchMode)
            {
                return; // no OS pointer in a batch run
            }

            bool retina = Screen.dpi >= RetinaDpi;
            Texture2D texture = Texture(s_pressed, retina);
            if (texture == null || texture == s_shown)
            {
                return;
            }

            s_shown = texture;
            Cursor.SetCursor(texture, retina ? Hotspot64 : Hotspot32, CursorMode.Auto);
        }

        private static void EnsureInstalled()
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;
            UiInputScheme.Changed += OnSchemeChanged;
            ApplyTexture();
        }

        private static void OnSchemeChanged(InputSchemeKind scheme)
        {
            Apply();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_context = true;
            s_pressed = false;
            s_installed = false;
            s_shown = null;
        }

        // After UiInputScheme's own reset (SubsystemRegistration clears its Changed): install the art and the scheme hook.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            EnsureInstalled();
        }
    }
}
