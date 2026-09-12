using System;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Every place the player can be in the meta flow (WP-005).
    ///
    /// Two kinds of value live in one enum on purpose, so a screen only ever has to
    /// name one thing:
    ///
    ///   BASE contexts own the frame. Exactly one is current, and it changes when a
    ///   scene changes: <see cref="MainMenu"/>, <see cref="Selection"/>,
    ///   <see cref="GameplayHud"/>, <see cref="RunResults"/>.
    ///
    ///   LAYER contexts sit on a base. The main menu's reading panes
    ///   (<see cref="Profile"/>, <see cref="Statistics"/>, <see cref="Achievements"/>) are
    ///   layers on <see cref="MainMenu"/>; the round-end shop (<see cref="RoundUpgrade"/>)
    ///   and the pause screen (<see cref="Paused"/>) are layers on
    ///   <see cref="GameplayHud"/>. The pause screen is also allowed on top of the shop,
    ///   which is the one two-layer stack the flow has.
    /// </summary>
    public enum UiContext
    {
        None = 0,

        // ---- base contexts: one owns the frame at a time
        MainMenu = 1,
        Selection = 2,
        GameplayHud = 3,
        RunResults = 4,

        // ---- layers on MainMenu
        Profile = 10,
        Statistics = 11,
        Achievements = 12,

        // ---- layers on GameplayHud
        RoundUpgrade = 20,
        Paused = 21
    }

    /// <summary>What <see cref="UiNavigationModel"/> did with a request.</summary>
    public enum UiNavigationResult
    {
        /// <summary>The state changed.</summary>
        Changed = 0,

        /// <summary>Already there. Nothing happened and nothing was raised (idempotent).</summary>
        AlreadyThere = 1,

        /// <summary>The layer is not allowed on the current base, or the value is not a layer/base.</summary>
        Refused = 2
    }

    /// <summary>
    /// The one place that knows which screen states may coexist.
    ///
    /// A plain class with no Unity dependency so the rules can be proven in EditMode:
    /// the four base contexts are mutually exclusive by construction (there is one
    /// field), a layer is only accepted on the base it belongs to, and the two stacks
    /// that exist - shop under pause, and nothing else - are spelled out in
    /// <see cref="MayLayer"/>. There are no booleans to contradict each other: Results
    /// and Upgrade cannot both be open because Results is a base and Upgrade only
    /// layers on a different base; Pause and Results likewise.
    ///
    /// Every mutation is idempotent. Entering the state you are already in returns
    /// <see cref="UiNavigationResult.AlreadyThere"/> and raises nothing, which is what
    /// stops a duplicated event from opening a second screen.
    /// </summary>
    public sealed class UiNavigationModel
    {
        private UiContext _base;
        private UiContext _layer;
        private UiContext _overlay;

        /// <summary>The base context that owns the frame. <see cref="UiContext.None"/> before the first screen.</summary>
        public UiContext Base
        {
            get { return _base; }
        }

        /// <summary>
        /// The layer on the base: a main-menu pane, or the round-end shop. None when the
        /// base is showing on its own.
        /// </summary>
        public UiContext Layer
        {
            get { return _layer; }
        }

        /// <summary>The pause screen, when it is up. The only context that stacks on a layer.</summary>
        public UiContext Overlay
        {
            get { return _overlay; }
        }

        /// <summary>The innermost open context: overlay, else layer, else base.</summary>
        public UiContext Current
        {
            get
            {
                if (_overlay != UiContext.None)
                {
                    return _overlay;
                }

                return _layer != UiContext.None ? _layer : _base;
            }
        }

        public bool IsPaused
        {
            get { return _overlay == UiContext.Paused; }
        }

        public bool IsShopOpen
        {
            get { return _layer == UiContext.RoundUpgrade; }
        }

        /// <summary>
        /// True while gameplay input should reach the player: the HUD is the base and
        /// nothing is layered on it. Menus, the shop and the pause screen all block.
        /// </summary>
        public bool GameplayInputAllowed
        {
            get { return _base == UiContext.GameplayHud && _layer == UiContext.None && _overlay == UiContext.None; }
        }

        /// <summary>Raised after any change. Never raised for an idempotent no-op.</summary>
        public event Action<UiNavigationModel> Changed;

        public static bool IsBase(UiContext context)
        {
            return context == UiContext.MainMenu ||
                   context == UiContext.Selection ||
                   context == UiContext.GameplayHud ||
                   context == UiContext.RunResults;
        }

        public static bool IsLayer(UiContext context)
        {
            return context == UiContext.Profile ||
                   context == UiContext.Statistics ||
                   context == UiContext.Achievements ||
                   context == UiContext.RoundUpgrade;
        }

        /// <summary>The base a layer belongs to. None for a value that is not a layer.</summary>
        public static UiContext BaseOf(UiContext layer)
        {
            switch (layer)
            {
                case UiContext.Profile:
                case UiContext.Statistics:
                case UiContext.Achievements:
                    return UiContext.MainMenu;

                case UiContext.RoundUpgrade:
                case UiContext.Paused:
                    return UiContext.GameplayHud;

                default:
                    return UiContext.None;
            }
        }

        /// <summary>Would <paramref name="layer"/> be accepted right now? Pure; changes nothing.</summary>
        public bool MayLayer(UiContext layer)
        {
            if (layer == UiContext.Paused)
            {
                // Pause sits on the HUD or on the shop, never on a menu or on Results.
                return _base == UiContext.GameplayHud;
            }

            return IsLayer(layer) && BaseOf(layer) == _base;
        }

        /// <summary>
        /// Enters a base context. Every layer and overlay is dropped with the old base:
        /// a new scene never inherits the previous scene's pane, shop or pause.
        ///
        /// Re-entering the CURRENT base is a no-op that keeps its layers. Two writers
        /// name the base for one scene - the screen object serialised in the scene
        /// (Awake, before the sceneLoaded callback) and the installer (sceneLoaded) -
        /// and the second must not wipe the pane the first has already opened.
        /// </summary>
        public UiNavigationResult EnterBase(UiContext context)
        {
            if (!IsBase(context))
            {
                return UiNavigationResult.Refused;
            }

            if (_base == context)
            {
                return UiNavigationResult.AlreadyThere;
            }

            _base = context;
            _layer = UiContext.None;
            _overlay = UiContext.None;
            Raise();
            return UiNavigationResult.Changed;
        }

        /// <summary>
        /// Opens a layer on the current base. Replaces the current layer when it is a
        /// sibling (Profile -> Statistics), and is refused when the base does not host it
        /// (a shop cannot open on the main menu; a pane cannot open on the HUD).
        /// </summary>
        public UiNavigationResult EnterLayer(UiContext layer)
        {
            if (layer == UiContext.Paused)
            {
                return EnterOverlay(layer);
            }

            if (!MayLayer(layer))
            {
                return UiNavigationResult.Refused;
            }

            if (_layer == layer)
            {
                return UiNavigationResult.AlreadyThere;
            }

            _layer = layer;
            Raise();
            return UiNavigationResult.Changed;
        }

        /// <summary>Closes the current layer, if any. The overlay is untouched.</summary>
        public UiNavigationResult ExitLayer(UiContext layer)
        {
            if (_layer != layer || layer == UiContext.None)
            {
                return UiNavigationResult.AlreadyThere;
            }

            _layer = UiContext.None;
            Raise();
            return UiNavigationResult.Changed;
        }

        private UiNavigationResult EnterOverlay(UiContext overlay)
        {
            if (!MayLayer(overlay))
            {
                return UiNavigationResult.Refused;
            }

            if (_overlay == overlay)
            {
                return UiNavigationResult.AlreadyThere;
            }

            _overlay = overlay;
            Raise();
            return UiNavigationResult.Changed;
        }

        /// <summary>Closes the overlay, if it is up.</summary>
        public UiNavigationResult ExitOverlay(UiContext overlay)
        {
            if (_overlay != overlay || overlay == UiContext.None)
            {
                return UiNavigationResult.AlreadyThere;
            }

            _overlay = UiContext.None;
            Raise();
            return UiNavigationResult.Changed;
        }

        /// <summary>Back to nothing. Used when the flow leaves every scene, and by tests.</summary>
        public void Clear()
        {
            bool had = _base != UiContext.None || _layer != UiContext.None || _overlay != UiContext.None;
            _base = UiContext.None;
            _layer = UiContext.None;
            _overlay = UiContext.None;

            if (had)
            {
                Raise();
            }
        }

        public override string ToString()
        {
            return "base=" + _base + " layer=" + _layer + " overlay=" + _overlay;
        }

        private void Raise()
        {
            Action<UiNavigationModel> handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(this);
            }
            catch (Exception e)
            {
                // A broken listener must never stop a screen from opening.
                Debug.LogException(e);
            }
        }
    }

    /// <summary>
    /// The live navigation state of the running game: one <see cref="UiNavigationModel"/>
    /// that every screen reports into and that anything gating input or a control may
    /// read. Static, like <c>RunEvents</c> and <c>PrototypeUiFlow</c>, and reset with them.
    ///
    /// WHO WRITES WHAT
    ///   <c>PrototypeUiInstaller</c>   the base, when a scene's screens are built
    ///   <c>MainMenuScreen</c>         the Profile / Statistics / Achievements layer
    ///   <c>UpgradeScreen</c>          the RoundUpgrade layer
    ///   <c>PauseScreen</c>            the Paused overlay
    ///
    /// WHO READS
    ///   <c>UpgradeScreen</c>          IsPaused, instead of searching the scene for the
    ///                                 pause screen every frame (WP-004 hand-off note)
    ///   <c>PauseScreen</c>            MayLayer(Paused), so a pause can never open on
    ///                                 top of Results or a menu
    ///   tests                         everything
    /// </summary>
    public static class UiNavigation
    {
        private static UiNavigationModel s_model = new UiNavigationModel();

        public static UiNavigationModel Model
        {
            get { return s_model; }
        }

        public static UiContext Base
        {
            get { return s_model.Base; }
        }

        public static UiContext Layer
        {
            get { return s_model.Layer; }
        }

        public static UiContext Overlay
        {
            get { return s_model.Overlay; }
        }

        public static UiContext Current
        {
            get { return s_model.Current; }
        }

        public static bool IsPaused
        {
            get { return s_model.IsPaused; }
        }

        public static bool GameplayInputAllowed
        {
            get { return s_model.GameplayInputAllowed; }
        }

        public static event Action<UiNavigationModel> Changed
        {
            add { s_model.Changed += value; }
            remove { s_model.Changed -= value; }
        }

        public static UiNavigationResult EnterBase(UiContext context)
        {
            return s_model.EnterBase(context);
        }

        public static UiNavigationResult EnterLayer(UiContext layer)
        {
            return s_model.EnterLayer(layer);
        }

        public static UiNavigationResult ExitLayer(UiContext layer)
        {
            return s_model.ExitLayer(layer);
        }

        public static UiNavigationResult ExitOverlay(UiContext overlay)
        {
            return s_model.ExitOverlay(overlay);
        }

        public static bool MayLayer(UiContext layer)
        {
            return s_model.MayLayer(layer);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_model = new UiNavigationModel();
        }
    }
}
