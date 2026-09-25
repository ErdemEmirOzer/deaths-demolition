using DD.Prototype.CameraSystem;
using DD.Prototype.Core.InputAbstraction;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 §4 #26 / V1 · the one owner of the OS pointer. The hardware cursor is the V1 art (Cream100 fill,
    /// Sumi900 outline; Arrow, Press while a control is held) as an RGBA32 texture (<c>Cursor.SetCursor</c> once per
    /// change, never per frame, never ForceSoftware).
    ///
    /// M4-R1 N21 (user 24.09: "the cursor is too big"; ui-ux-designer ruling): the arrow is
    /// <see cref="ArrowHeightFor"/> tall - 28 pt on macOS (Unity shows a cursor texture's pixels as POINTS there, so the
    /// old 64 px Retina art was a 63 pt arrow against the system's ≈ 23 pt), 24-31 px elsewhere by the OS scale. The
    /// texture is 32 px at every size (a larger one risks Unity's software path on Windows), built once per size from
    /// the 64 px master by <see cref="UiCursorArt"/>; the UI scale setting does not touch the pointer (it is the OS's).
    ///
    /// Visibility = what the context wants AND the last device is not a gamepad: the menus, Pause and the shop want
    /// it, the run hides it (the reticle is the pointer), loading hides it; a pad hides it and the first mouse move
    /// (the scheme flips back) returns it. Screens and the curtain say what they want through
    /// <see cref="SetContextVisible"/> - nothing else writes <c>Cursor.visible</c>.
    ///
    /// 12b (AD G1 SHOULD-4): the Press art is bound while a kit control is held (UiButton, the UiSlider bead drag),
    /// and a pointer that hides mid-hold (a pad took over, the run or the curtain hid it) drops the Press art, so the
    /// mouse move that restores it always shows the Arrow.
    /// </summary>
    public static class UiCursor
    {
        public const string ArrowName = "Cursor.Arrow";
        public const string PressName = "Cursor.Press";

        /// <summary>
        /// The tip, for every size (the AP-012 v004 manifest's 32 px hotspot). 12b (AD G1 SHOULD-4): one hotspot is
        /// right because the OS only ever gets the <see cref="TextureSize"/> texture - N21 builds it at every arrow
        /// height (24-31 px, and Mac's 28 pt) from the 64 px master, and <see cref="UiCursorArt"/> places the outlined
        /// tip on this point; the 64 px master is a source, never a cursor, so the AD's "(2, 2) at 32 → (4, 4) at 64"
        /// (the v004 32 / 64 pair) has no second texture to apply to. UiCursorSizeTests asserts the tip at every size.
        /// </summary>
        public static readonly Vector2 Hotspot = new Vector2(2f, 1f);

        /// <summary>N21: the arrow's height on macOS, in points (fixed - points already normalise the backing).</summary>
        public const int MacArrowPoints = 28;
        public const int MinArrowPx = 24;
        /// <summary>A texture over 32 px risks Unity's software-cursor path on Windows (the latency ban).</summary>
        public const int MaxArrowPx = 31;
        public const int TextureSize = 32;

        private static readonly System.Collections.Generic.Dictionary<int, Texture2D> s_sized =
            new System.Collections.Generic.Dictionary<int, Texture2D>();

        private static bool s_context = true;
        private static bool s_pressed;
        private static bool s_installed;
        private static Texture2D s_shown;
        private static bool s_locked;
        private static GameObject s_lockDriver;

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

        /// <summary>The V1 master art for a state and backing (the 64 px Arrow is the source of every size; tests).</summary>
        public static Texture2D Texture(bool pressed, bool retina)
        {
            return Resources.Load<Texture2D>(UiTheme.CursorFolder + (pressed ? PressName : ArrowName) + (retina ? "_64" : "_32"));
        }

        /// <summary>
        /// N21: the arrow's height (outline included). macOS: <see cref="MacArrowPoints"/> pt. Elsewhere
        /// round(24 × OS scale) clamped to 24-31 px; the OS scale is dpi / 96, or the height over 1080 where the dpi is
        /// missing or a fixed 96 (Linux).
        /// </summary>
        public static int ArrowHeightFor(RuntimePlatform platform, float dpi, int screenHeight)
        {
            if (platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor)
            {
                return MacArrowPoints;
            }

            bool linux = platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor;
            float factor = dpi > 0f && !linux ? dpi / 96f : screenHeight / 1080f;
            return Mathf.Clamp(Mathf.RoundToInt(MinArrowPx * factor), MinArrowPx, MaxArrowPx);
        }

        /// <summary>The cursor at an arrow height, built once from the 64 px master (null when the art is absent).</summary>
        public static Texture2D Sized(bool pressed, int arrowHeight)
        {
            int key = arrowHeight * 2 + (pressed ? 1 : 0);
            Texture2D texture;
            if (s_sized.TryGetValue(key, out texture) && texture != null)
            {
                return texture;
            }

            Texture2D master = Texture(false, true);
            texture = master != null ? UiCursorArt.Build(master, arrowHeight, pressed) : null;
            s_sized[key] = texture;
            return texture;
        }

        private static void Apply()
        {
            EnsureInstalled();
            bool visible = WantVisible;
            if (!visible && s_pressed)
            {
                // 12b (AD G1 SHOULD-4): a hidden pointer never returns in the Press art (its pointer-up may never come).
                s_pressed = false;
                ApplyTexture();
            }

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

            Texture2D texture = Sized(s_pressed, ArrowHeightFor(Application.platform, Screen.dpi, Screen.height));
            if (texture == null || texture == s_shown)
            {
                return;
            }

            s_shown = texture;
            Cursor.SetCursor(texture, Hotspot, CursorMode.Auto);
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

            // M4-R1 N2 (WP-070 D-8): the pointer's lock is ours too - the camera rig stops writing it.
            CameraModeService.CursorIntentHandledExternally = true;
            if (Application.isPlaying && s_lockDriver == null)
            {
                s_lockDriver = new GameObject("[UiCursorLock]", typeof(UiCursorLockDriver)) { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(s_lockDriver);
            }
        }

        /// <summary>
        /// WP-070 D-8: whether the pointer is locked. The camera says what it wants (<see cref="CameraModeService.CursorIntent"/>:
        /// Locked in TP/FP play, Free under pause, a look block or top-down); the context outranks it - a menu, Pause or the
        /// shop that wants the pointer frees it - and an unfocused window never holds it.
        /// </summary>
        public static bool WantLocked(CameraCursorIntent intent, bool contextVisible, bool focused)
        {
            return intent == CameraCursorIntent.Locked && !contextVisible && focused;
        }

        public static bool Locked
        {
            get { return s_locked; }
        }

        /// <summary>Applies the lock when it changed; called once a frame by the lock driver (a state read, no allocation).</summary>
        public static void SyncLock()
        {
            bool want = WantLocked(CameraModeService.CursorIntent, s_context, Application.isFocused);
            if (want == s_locked)
            {
                return;
            }

            s_locked = want;
            Cursor.lockState = want ? CursorLockMode.Locked : CursorLockMode.None;
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
            s_locked = false;
            s_lockDriver = null;
        }

        // After UiInputScheme's own reset (SubsystemRegistration clears its Changed): install the art and the scheme hook.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            EnsureInstalled();
        }
    }
}
