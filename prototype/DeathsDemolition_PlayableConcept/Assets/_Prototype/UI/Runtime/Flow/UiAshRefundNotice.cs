using System;
using DD.Prototype.Profile;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-104 (S-09; KARAR-WP104-11.10 decision 2, S-04 B1a) · the one-time "Ash refunded: +X" notice. The v5 → v6 profile step
    /// gives every Ash spent back and records the amount once in <see cref="ProfileService.PendingAshRefundNotice"/>; S-09 shows it
    /// and then clears it with <see cref="ProfileService.AcknowledgeAshRefundNotice"/> (S-04's contract,
    /// Profile/MetaPath/ProfileService.Awakening.cs).
    /// WHERE AND WHEN: the main menu, the first time it stands revealed in a session - the curtain open, no dialog over it - as the
    /// kit's brush notice (a single-action C31: title, the S-04 sentence with the amount, GOT IT). It is a decision-free notice, so
    /// GOT IT, Cancel (Esc / B) and a click on its action all mean "read": the notice is acknowledged (cleared on disk) and closes;
    /// the focus returns to where it was. A failed write (save locked / failed) leaves RAM = disk: the notice comes back next launch,
    /// but not again in this session.
    /// NEVER IN A HARNESS: batch mode, <c>-ddUiCapture</c> and <c>-ddQaBot</c> runs never show it (C-00 decision 5's rule for
    /// overlays); the profile is not read before the menu is entered (a PlayMode run's profile isolation is set up by then).
    /// No allocation per frame: the host polls a flag, the curtain and Cancel only while the menu waits or the notice stands.
    /// </summary>
    public sealed class UiAshRefundNotice : MonoBehaviour
    {
        public const string ModalName = "AshRefundNotice";
        private const string MenuScreen = "MainMenuScreen";

        /// <summary>Tests: the pending amount (null = <see cref="ProfileService.PendingAshRefundNotice"/>).</summary>
        internal static Func<int> PendingSource;

        /// <summary>Tests: the acknowledgement (null = <see cref="ProfileService.AcknowledgeAshRefundNotice"/>).</summary>
        internal static Func<MetaCommandResult> AcknowledgeAction;

        /// <summary>Tests: force the harness suppression off (null = batch mode / capture / bot suppress).</summary>
        internal static bool? SuppressOverride;

        private static UiAshRefundNotice s_host;
        private static bool s_menuEntered;
        private static bool s_shownThisSession;

        private UiModal _modal;

        /// <summary>The notice standing now (tests, captures).</summary>
        public static UiModal Current => s_host != null && s_host._modal != null && s_host._modal.IsOpen ? s_host._modal : null;

        public static bool ShownThisSession => s_shownThisSession;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_host = null;
            s_menuEntered = false;
            s_shownThisSession = false;
            PendingSource = null;
            AcknowledgeAction = null;
            SuppressOverride = null;
        }

        /// <summary>Tests: a fresh session (the notice may show again; the seams are the caller's to set and clear).</summary>
        internal static void ResetForTests()
        {
            if (s_host != null)
            {
                if (s_host._modal != null)
                {
                    Destroy(s_host._modal.gameObject);
                }

                Destroy(s_host.gameObject);
            }

            s_host = null;
            s_shownThisSession = false;
        }

        /// <summary>After UiEvents' own reset (SubsystemRegistration): listen for the menu. No profile read here.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            UiEvents.Raised -= OnRaised;
            UiEvents.Raised += OnRaised;
        }

        private static void OnRaised(UiEvent uiEvent, string detail)
        {
            if (detail != MenuScreen)
            {
                return;
            }

            if (uiEvent == UiEvent.ScreenEntered)
            {
                s_menuEntered = true;
                if (!s_shownThisSession && !Suppressed() && s_host == null)
                {
                    var go = new GameObject("UiAshRefundNotice");
                    DontDestroyOnLoad(go);
                    s_host = go.AddComponent<UiAshRefundNotice>();
                }
            }
            else if (uiEvent == UiEvent.ScreenLeft)
            {
                s_menuEntered = false;
            }
        }

        private static bool Suppressed()
        {
            if (SuppressOverride.HasValue)
            {
                return SuppressOverride.Value;
            }

            if (Application.isBatchMode)
            {
                return true;
            }

            string[] args = Environment.GetCommandLineArgs();
            return Array.IndexOf(args, "-ddUiCapture") >= 0 || Array.IndexOf(args, "-ddQaBot") >= 0;
        }

        private static int Pending()
        {
            Func<int> source = PendingSource;
            return source != null ? source() : ProfileService.PendingAshRefundNotice;
        }

        private void Update()
        {
            if (_modal != null)
            {
                if (_modal.IsOpen && UiInput.CancelPressed())
                {
                    UiSound.Play(UiSoundKind.Back);
                    Acknowledge();   // a notice: Cancel reads it too
                }

                return;
            }

            if (s_shownThisSession)
            {
                Destroy(gameObject);
                return;
            }

            // The menu revealed and free: the curtain open, no dialog standing (the quit confirm, a load error).
            if (!s_menuEntered || UiCurtain.IsCovering || UiInput.TopModal != null)
            {
                return;
            }

            int amount = Pending();
            if (amount <= 0)
            {
                s_shownThisSession = true;   // nothing to say: done for the session
                return;
            }

            MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
            Canvas canvas = menu != null ? menu.GetComponentInChildren<Canvas>() : null;
            if (canvas == null)
            {
                return;
            }

            Show((RectTransform)canvas.transform, amount);
        }

        /// <summary>Builds and opens the notice over <paramref name="screenRoot"/> (tests may call it directly).</summary>
        internal void Show(RectTransform screenRoot, int amount)
        {
            string title = UiStrings.Get("awakening.refund.title", "ASH REFUNDED");
            string body = UiStrings.Format("meta.ash_refund", "value", UiStrings.Number(Mathf.Max(0, amount)),
                "+{value} Ash refunded. Every weapon now opens in every run, so the PATH's WEAPONS page is gone. Spend the Ash on the AWAKENING page.");
            string ok = UiStrings.Get("awakening.refund.ok", "GOT IT");
            _modal = UiModal.Confirm(ModalName, screenRoot, title, body, ok, Acknowledge, null, null, false, UiControlStyle.Brush);
            _modal.SetActionIcons("Menu.Confirm", null);
            _modal.Open();
            s_shownThisSession = true;
        }

        private void Acknowledge()
        {
            Func<MetaCommandResult> acknowledge = AcknowledgeAction;
            MetaCommandResult result = acknowledge != null ? acknowledge() : ProfileService.AcknowledgeAshRefundNotice();
            if (result != MetaCommandResult.Done)
            {
                Debug.LogWarning("[UI] Ash refund notice: acknowledgement not saved (" + result + "); it returns next launch.");
            }

            if (_modal != null)
            {
                _modal.Close();
                Destroy(_modal.gameObject);
                _modal = null;
            }

            Destroy(gameObject);
            s_host = null;
        }

        private void OnDestroy()
        {
            if (s_host == this)
            {
                s_host = null;
            }
        }
    }
}
