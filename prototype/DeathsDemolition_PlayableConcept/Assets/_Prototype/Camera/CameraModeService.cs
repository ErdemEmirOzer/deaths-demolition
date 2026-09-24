using System;
using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// WP-070 (M4-R1 N2) · The one place that knows which camera mode is active.
    ///
    /// CONTRACT (consumers: S-02 combat/aim, S-03 AI, S-06 animation, S-07 feel/VFX,
    /// S-08 audio, S-09 UI, S-10 QA)
    ///   * <see cref="Current"/> is the mode the game logic must obey this frame (movement
    ///     basis, aim source, cursor lock, per-mode profiles). It flips at the START of a
    ///     transition, so input and aim never run in a half state.
    ///   * <see cref="Transition01"/> runs 0 → 1 while the rig blends the pose from the
    ///     old mode to the new one (<see cref="IsTransitioning"/>); visual-only consumers
    ///     (viewmodel fade, head hiding, HUD reticle morph) may ease on it.
    ///   * <see cref="Changed"/> fires once per accepted request, after <see cref="Current"/>
    ///     is updated. Requesting the current mode is a no-op (no event).
    ///   * <see cref="Settings"/> is the live player preference set. Mutate it only through
    ///     <see cref="ApplySettings"/>, which sanitises and raises <see cref="SettingsChanged"/>.
    ///     Persistence is the caller's job (UI writes ui-settings.json / input-settings.json);
    ///     this class never touches the disk, so tests and bots cannot write user data.
    ///   * Command line: <c>-ddCameraMode topdown|tp|fp</c> selects the start mode for a
    ///     process (QA bot, S-10 WP-078) without touching the saved preference.
    ///
    /// Static state is reset on SubsystemRegistration (domain-reload-off safe).
    /// </summary>
    public static class CameraModeService
    {
        public const string CommandLineFlag = "-ddCameraMode";

        private static CameraMode s_current = CameraMode.TopDown;
        private static CameraMode s_previous = CameraMode.TopDown;
        private static float s_transition01 = 1f;
        private static CameraModeSettings s_settings = new CameraModeSettings();
        private static bool s_commandLineParsed;
        private static CameraMode? s_commandLineMode;
        private static readonly CameraModeProfile[] s_profiles =
        {
            CameraModeProfile.Default(CameraMode.TopDown),
            CameraModeProfile.Default(CameraMode.ThirdPerson),
            CameraModeProfile.Default(CameraMode.FirstPerson),
        };
        private static readonly System.Collections.Generic.HashSet<string> s_lookBlocks = new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.HashSet<string> s_modeLocks = new System.Collections.Generic.HashSet<string>();
        private static CameraMode? s_pending;

        /// <summary>Mode the game logic obeys this frame.</summary>
        public static CameraMode Current => s_current;

        /// <summary>Mode the rig is blending away from during a transition (== Current when idle).</summary>
        public static CameraMode Previous => s_previous;

        /// <summary>
        /// Where the camera is heading: a request queued behind a mode lock (death sequence)
        /// if there is one, otherwise <see cref="Current"/>.
        /// </summary>
        public static CameraMode Target => s_pending ?? s_current;

        /// <summary>True while a mode lock holds (death sequence owns the camera); requests are queued.</summary>
        public static bool IsModeChangeLocked => s_modeLocks.Count > 0;

        /// <summary>
        /// Locks mode changes (e.g. <c>DeathSequence</c> for the death beat). While locked,
        /// <see cref="Request"/> queues the last requested mode and returns false; the queued
        /// mode is applied (smoothly) when the last lock is released.
        /// </summary>
        public static void LockModeChanges(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                s_modeLocks.Add(id);
            }
        }

        /// <summary>Releases a mode lock; the last release applies a queued request.</summary>
        public static void UnlockModeChanges(string id)
        {
            if (string.IsNullOrEmpty(id) || !s_modeLocks.Remove(id) || s_modeLocks.Count > 0 || !s_pending.HasValue)
            {
                return;
            }

            CameraMode next = s_pending.Value;
            s_pending = null;
            Request(next);
        }

        /// <summary>
        /// Direction doc D-8: the cursor state the camera wants this frame. Locked only in TP/FP
        /// gameplay: not while the run clock is frozen (pause), not under a look block, not in
        /// top-down. UiCursor (S-09) is the single writer and applies it.
        /// </summary>
        public static CameraCursorIntent CursorIntent
        {
            get
            {
                // R1-A #18: reads the run clock directly, so the intent is right even without a rig.
                bool frozen = s_frozen || DD.Prototype.Rounds.RunFlowClock.IsFrozen;
                return IsLookMode && !IsLookBlocked && !frozen ? CameraCursorIntent.Locked : CameraCursorIntent.Free;
            }
        }

        /// <summary>
        /// Set by UiCursor (S-09) once it applies <see cref="CursorIntent"/>. Until then the camera
        /// rig applies Cursor.lockState itself so TP/FP stay playable (lockState only; UiCursor keeps
        /// sole ownership of Cursor.visible).
        /// </summary>
        public static bool CursorIntentHandledExternally { get; set; }

        private static bool s_frozen;

        /// <summary>Rig side: whether the run clock is frozen this frame (pause, card offer freeze).</summary>
        public static void SetFrozen(bool frozen)
        {
            s_frozen = frozen;
        }

        /// <summary>
        /// Direction doc §5.1 blend times per mode pair (unscaled seconds): TopDown↔TP 0.45,
        /// TP↔FP 0.30, TopDown↔FP 0.55; 0 with ReducedMotion.
        /// </summary>
        public static float TransitionSeconds(CameraMode from, CameraMode to)
        {
            if (from == to || s_settings.ReducedMotion)
            {
                return 0f;
            }

            bool hasTop = from == CameraMode.TopDown || to == CameraMode.TopDown;
            bool hasFp = from == CameraMode.FirstPerson || to == CameraMode.FirstPerson;
            if (hasTop && hasFp)
            {
                return 0.55f;
            }

            return hasTop ? 0.45f : 0.30f;
        }

        /// <summary>0 at the start of a transition, 1 when the rig has arrived (and when idle).</summary>
        public static float Transition01 => s_transition01;

        public static bool IsTransitioning => s_transition01 < 1f;

        /// <summary>True for the look-driven modes (TP/FP): cursor locked, screen-centre aim, camera-relative body yaw.</summary>
        public static bool IsLookMode => s_current != CameraMode.TopDown;

        /// <summary>Live player preferences. Treat as read-only; change through <see cref="ApplySettings"/>.</summary>
        public static CameraModeSettings Settings => s_settings;

        /// <summary>Rendering profile of the current mode (see <see cref="CameraModeProfile"/> single-writer rule).</summary>
        public static CameraModeProfile CurrentProfile => s_profiles[(int)s_current];

        /// <summary>Rendering profile of <paramref name="mode"/>.</summary>
        public static CameraModeProfile Profile(CameraMode mode)
        {
            return s_profiles[(int)mode];
        }

        /// <summary>Replaces a mode's profile (S-05 quality tiers, S-07 feel); raises <see cref="ProfilesChanged"/>.</summary>
        public static void SetProfile(CameraMode mode, CameraModeProfile profile)
        {
            s_profiles[(int)mode] = profile;
            ProfilesChanged?.Invoke();
        }

        /// <summary>Raised after <see cref="SetProfile"/>.</summary>
        public static event Action ProfilesChanged;

        /// <summary>
        /// True while any UI holds a look block (shop, card offer, dialogs that run without
        /// freezing the game): the TP/FP camera stops reading look input and releases the
        /// cursor. Pause (RunFlowClock freeze) releases it on its own; no block needed.
        /// </summary>
        public static bool IsLookBlocked => s_lookBlocks.Count > 0;

        /// <summary>Adds a named look block (idempotent). S-09 UI calls this for non-freezing menus.</summary>
        public static void AddLookBlock(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                s_lookBlocks.Add(id);
            }
        }

        /// <summary>Removes a named look block (idempotent).</summary>
        public static void RemoveLookBlock(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                s_lookBlocks.Remove(id);
            }
        }

        /// <summary>Raised once per accepted mode change, after <see cref="Current"/> is updated.</summary>
        public static event Action<CameraModeChange> Changed;

        /// <summary>Raised after <see cref="ApplySettings"/> changed the live settings.</summary>
        public static event Action SettingsChanged;

        /// <summary>
        /// Switches mode. <paramref name="instant"/> snaps the rig (run start, tests, death
        /// cleanup); otherwise the rig blends over the per-mode transition time. Returns false
        /// when <paramref name="mode"/> is already current.
        /// </summary>
        public static bool Request(CameraMode mode, bool instant = false)
        {
            if (!Enum.IsDefined(typeof(CameraMode), mode))
            {
                return false;
            }

            if (instant && mode == s_current)
            {
                // R1-A #7: an instant request of the current mode (run start) must also finish a
                // blend left pending by a scene change or a rig-less request, or firing stays
                // swallowed. No event: the mode did not change.
                CompleteTransition();
                return false;
            }

            if (IsModeChangeLocked)
            {
                s_pending = mode == s_current ? (CameraMode?)null : mode;
                return false;
            }

            if (mode == s_current)
            {
                return false;
            }

            instant |= s_settings.ReducedMotion;

            CameraMode from = s_current;
            s_previous = from;
            s_current = mode;
            s_transition01 = instant ? 1f : 0f;
            Changed?.Invoke(new CameraModeChange(from, mode, instant));
            return true;
        }

        /// <summary>
        /// Replaces the live settings (sanitised copy). When the saved mode differs from the
        /// current one and <paramref name="applyMode"/> is set, the mode follows (Pause menu:
        /// "Camera" selector writes Settings.Mode and calls this).
        /// </summary>
        public static void ApplySettings(CameraModeSettings settings, bool applyMode = true, bool instant = false)
        {
            CameraModeSettings next = settings != null ? settings.Clone() : new CameraModeSettings();
            next.Sanitize();
            s_settings = next;
            SettingsChanged?.Invoke();

            if (applyMode)
            {
                Request(next.Mode, instant);
            }
        }

        /// <summary>
        /// The mode a run should start in: the command line (<c>-ddCameraMode</c>) wins over
        /// the saved preference so QA can pin a mode without editing user settings.
        /// </summary>
        public static CameraMode ResolveStartMode()
        {
            CameraMode? cli = CommandLineMode();
            return cli ?? s_settings.Mode;
        }

        /// <summary>Ends any pending blend at once (rig disabled, scene change, run start).</summary>
        public static void CompleteTransition()
        {
            s_transition01 = 1f;
            s_previous = s_current;
        }

        /// <summary>
        /// R1-A #17: the profile visual systems should follow during a blend — the PREVIOUS mode's
        /// until halfway (roofs keep fading while the camera is still overhead), then the current one.
        /// </summary>
        public static CameraModeProfile EffectiveProfile
        {
            get { return IsTransitioning && s_transition01 < 0.5f ? s_profiles[(int)s_previous] : s_profiles[(int)s_current]; }
        }

        /// <summary>Called by the rig while it blends; clamps and ends the transition at 1.</summary>
        public static void AdvanceTransition(float delta01)
        {
            if (s_transition01 >= 1f)
            {
                return;
            }

            s_transition01 = Mathf.Clamp01(s_transition01 + Mathf.Max(0f, delta01));
            if (s_transition01 >= 1f)
            {
                s_previous = s_current;
            }
        }

        /// <summary>Parses <c>-ddCameraMode</c>. Public for tests.</summary>
        public static bool TryParseMode(string value, out CameraMode mode)
        {
            mode = CameraMode.TopDown;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "topdown": case "top-down": case "top": case "td": mode = CameraMode.TopDown; return true;
                case "thirdperson": case "third": case "tp": mode = CameraMode.ThirdPerson; return true;
                case "firstperson": case "first": case "fp": mode = CameraMode.FirstPerson; return true;
                default: return false;
            }
        }

        private static CameraMode? CommandLineMode()
        {
            if (s_commandLineParsed)
            {
                return s_commandLineMode;
            }

            s_commandLineParsed = true;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                CameraMode mode;
                if (string.Equals(args[i], CommandLineFlag, StringComparison.Ordinal) && TryParseMode(args[i + 1], out mode))
                {
                    s_commandLineMode = mode;
                    break;
                }
            }

            return s_commandLineMode;
        }

        /// <summary>Test hook: back to a fresh process (TopDown, default settings, no subscribers).</summary>
        public static void ResetForTests()
        {
            ResetStatics();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = CameraMode.TopDown;
            s_previous = CameraMode.TopDown;
            s_transition01 = 1f;
            s_settings = new CameraModeSettings();
            s_commandLineParsed = false;
            s_commandLineMode = null;
            Changed = null;
            SettingsChanged = null;
            ProfilesChanged = null;
            s_lookBlocks.Clear();
            s_modeLocks.Clear();
            s_pending = null;
            s_frozen = false;
            CursorIntentHandledExternally = false;
            s_profiles[0] = CameraModeProfile.Default(CameraMode.TopDown);
            s_profiles[1] = CameraModeProfile.Default(CameraMode.ThirdPerson);
            s_profiles[2] = CameraModeProfile.Default(CameraMode.FirstPerson);
        }
    }
}
