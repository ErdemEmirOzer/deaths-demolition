using UnityEngine;

namespace DD.Prototype.Core.Boot
{
    /// <summary>
    /// PROTOTYPE-ONLY: shrinks the opening window until it fits the desktop it opened on.
    ///
    /// The build asks for 1920x1080 because that is the resolution the UI is laid out
    /// against. That number is baked at build time, when nothing can know what screen the
    /// game will be launched on. On a 14" laptop the desktop is about 1512x982 points, so
    /// the requested window is larger than the screen it has to live in: the title bar
    /// leaves the top of the display, the bottom of the HUD is pushed off, and the window
    /// cannot be dragged back into view.
    ///
    /// The reference resolution is deliberately left alone. CanvasScaler already scales the
    /// UI by height from 1920x1080, so a smaller window means a smaller UI, not a cropped
    /// one. All that is needed is an honest starting size.
    /// </summary>
    public static class PrototypeWindowFit
    {
        /// <summary>What the UI is authored against, and the largest window we ever ask for.</summary>
        private const int TasarimGenislik = 1920;
        private const int TasarimYukseklik = 1080;

        /// <summary>
        /// Vertical room the desktop is not free to give: the menu bar at the top and the
        /// window's own title bar. Measured on macOS 15 at default scaling - 25 points of
        /// menu bar, 28 of title bar, plus a little air so the window never lands flush
        /// against the Dock.
        ///
        /// Subtracting a fixed allowance rather than scaling the whole screen by a fraction
        /// is what lets the window be as large as it honestly can be. A blanket 10% cut was
        /// costing about 20% of the window's area on a laptop for no reason: the chrome does
        /// not get bigger when the screen does.
        /// </summary>
        private const int DikeyCerceve = 64;

        /// <summary>Side air, so the window is not flush with the screen edges.</summary>
        private const int YatayCerceve = 16;

        /// <summary>Below this the HUD stops being readable; better to overflow slightly.</summary>
        private const int AsgariGenislik = 1280;
        private const int AsgariYukseklik = 720;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Uygula()
        {
#if !UNITY_EDITOR
            if (Screen.fullScreenMode != FullScreenMode.Windowed)
            {
                // The player asked for full screen. Their choice outranks our fitting.
                return;
            }

            // Units matter more than anything else here, and they do not match.
            // Screen.currentResolution and Display.main.system* report the display in
            // BACKING PIXELS (3024x1964 on a 14" MacBook Pro), while a window is sized in
            // POINTS (1512x982 on that same machine). Comparing the two directly is what
            // made the first version of this fix do nothing: it saw 3024 pixels of room,
            // concluded 1920 fits easily, and left the window larger than the desktop.
            Resolution masaustu = Screen.currentResolution;
            int pikselG = Mathf.Max(masaustu.width, Display.main.systemWidth);
            int pikselY = Mathf.Max(masaustu.height, Display.main.systemHeight);
            int olcek = BackingOlcegi(pikselG, pikselY);
            int ekranG = pikselG / olcek;
            int ekranY = pikselY / olcek;

            // One line, every launch. The window being bigger than the desktop is invisible
            // in a log and unmistakable on screen; this is what makes it diagnosable.
            Debug.Log(string.Format(
                "[Window] currentResolution {0}x{1} · Display.main.system {2}x{3} · window {4}x{5}",
                masaustu.width, masaustu.height,
                Display.main.systemWidth, Display.main.systemHeight,
                Screen.width, Screen.height));

            if (ekranG == int.MaxValue || ekranY == int.MaxValue)
            {
                return;
            }

            int enGenis = ekranG - YatayCerceve;
            int enYuksek = ekranY - DikeyCerceve;

            if (enGenis >= TasarimGenislik && enYuksek >= TasarimYukseklik)
            {
                // The design size already fits; leave the build's own request alone.
                return;
            }

            Vector2Int boyut = SigdirilmisBoyut(enGenis, enYuksek);
            if (boyut.x == Screen.width && boyut.y == Screen.height)
            {
                return;
            }

            Debug.Log(string.Format(
                "[Window] Desktop is {0}x{1}; opening at {2}x{3} instead of {4}x{5} so the window fits.",
                masaustu.width, masaustu.height, boyut.x, boyut.y, TasarimGenislik, TasarimYukseklik));

            Screen.SetResolution(boyut.x, boyut.y, FullScreenMode.Windowed);
#endif
        }

        /// <summary>
        /// The display's backing scale: 2 on a HiDPI screen, 1 otherwise.
        ///
        /// Unity does not expose the scale factor, so it is read off the only thing that is
        /// visible - the reported pixel size. No desktop is laid out WIDER than 2560 points,
        /// so a surface past that is doubled pixels: 3024x1964 -> 1512x982, which is exactly what
        /// macOS reports for this machine. An ordinary 1920x1080 or 2560x1440 monitor is
        /// passed through untouched. The comparison is strictly greater on purpose: a real
        /// 2560x1440 monitor is not HiDPI and must keep its full 1920x1080 window.
        ///
        /// Decided ONCE from the WIDTH and applied to both axes. Testing each axis on its
        /// own is wrong and was wrong here: 3024 is over the threshold but its own height
        /// 1964 is under it, so a per-axis rule turns a 1512x982 desktop into 1512x1964 and
        /// believes there is twice the vertical room there really is.
        ///
        /// Guessing wrong is not symmetric. Guessing small opens a smaller window than it
        /// had to; guessing large is the fault this whole class exists to prevent.
        /// </summary>
        public static int BackingOlcegi(int pikselGenislik, int pikselYukseklik)
        {
            return pikselGenislik > 2560 ? 2 : 1;
        }

        /// <summary>
        /// Largest 16:9 box inside the allowance, never above the design size and never
        /// below the readable floor. Public because the arithmetic is what can be wrong
        /// here, and a test can check it without needing a real desktop to open on.
        /// </summary>
        public static Vector2Int SigdirilmisBoyut(int enGenis, int enYuksek)
        {
            float oran = (float)TasarimGenislik / TasarimYukseklik;

            int g = Mathf.Min(enGenis, TasarimGenislik);
            int y = Mathf.RoundToInt(g / oran);

            if (y > enYuksek)
            {
                y = Mathf.Min(enYuksek, TasarimYukseklik);
                g = Mathf.RoundToInt(y * oran);
            }

            g = Mathf.Max(g, AsgariGenislik);
            y = Mathf.Max(y, AsgariYukseklik);
            return new Vector2Int(g, y);
        }
    }
}
