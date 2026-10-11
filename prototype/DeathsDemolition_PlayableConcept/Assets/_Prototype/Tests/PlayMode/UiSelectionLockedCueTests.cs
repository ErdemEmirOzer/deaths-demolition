using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-105 (SHOULD-8) on the real Prepare Run screen: a press on a locked weapon row is ONE cue - Denied - where it used
    /// to be Confirm (UiButton) and Denied (SelectWeapon) in the same frame; the selection does not change. A press on an
    /// unlocked row still confirms (Confirm + ButtonPress) and chooses it. Kept in its own file: S-02's wp-107 branch
    /// reworks the locked rows (only the wall-buy weapon stays locked), and this suite only needs one locked row - it
    /// steps aside (Inconclusive) when the roster has none.
    /// </summary>
    public sealed class UiSelectionLockedCueTests
    {
        private readonly Dictionary<UiEvent, int> _seen = new Dictionary<UiEvent, int>();

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        private void Record(UiEvent e, string detail)
        {
            int n;
            _seen.TryGetValue(e, out n);
            _seen[e] = n + 1;
        }

        private int Seen(UiEvent e)
        {
            int n;
            return _seen.TryGetValue(e, out n) ? n : 0;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Secim_KilitliSilahaBasis_YalnizRedSesi_AcikSilahaBasis_OnaySesi()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LifecycleTestKit.BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            yield return null;

            SelectionScreen screen = Object.FindObjectOfType<SelectionScreen>();
            Assert.IsNotNull(screen, "SelectionScreen");

            UiButton locked = null;
            UiButton open = null;
            ClassicWeaponId openId = default(ClassicWeaponId);
            foreach (UiButton row in screen.GetComponentsInChildren<UiButton>(true))
            {
                ClassicWeaponId id;
                if (!TryWeapon(row, out id))
                {
                    continue;
                }

                bool unlocked = ProfileService.IsWeaponUnlocked(id);
                Assert.AreEqual(unlocked, row.ConfirmCue, row.name + ": the row's Confirm cue follows its lock (SHOULD-8)");
                if (!unlocked && locked == null)
                {
                    locked = row;
                }
                else if (unlocked && open == null && id != PrototypeUiFlow.SelectedWeapon)
                {
                    open = row;
                    openId = id;
                }
            }

            if (locked == null)
            {
                Assert.Inconclusive("no locked weapon row on this profile (wp-107: only the wall-buy weapon stays locked)");
            }

            ClassicWeaponId before = PrototypeUiFlow.SelectedWeapon;
            UiEvents.Raised += Record;
            try
            {
                Assert.IsTrue(locked.IsInteractable(), "a locked row stays focusable (its stage explains the lock)");
                _seen.Clear();
                locked.OnSubmit(null);
                yield return null;
                Assert.AreEqual(1, Seen(UiEvent.Denied), "a locked row says Denied once");
                Assert.AreEqual(0, Seen(UiEvent.Confirm), "and no Confirm in the same frame (SHOULD-8)");
                Assert.AreEqual(0, Seen(UiEvent.ButtonPress), "no ButtonPress either (Confirm's own row in AudioEventMap)");
                Assert.AreEqual(0, Seen(UiEvent.ChoiceSelected), "nothing was chosen");
                Assert.AreEqual(before, PrototypeUiFlow.SelectedWeapon, "the locked weapon is not selected");

                if (open != null)
                {
                    yield return new WaitForSecondsRealtime(0.1f);   // past UiSound's 35 ms retrigger window
                    _seen.Clear();
                    open.OnSubmit(null);
                    yield return null;
                    Assert.AreEqual(1, Seen(UiEvent.Confirm), "an unlocked row still confirms");
                    Assert.AreEqual(1, Seen(UiEvent.ButtonPress));
                    Assert.AreEqual(0, Seen(UiEvent.Denied), "no Denied on an unlocked row");
                    Assert.AreEqual(1, Seen(UiEvent.ChoiceSelected), "the weapon is chosen");
                    Assert.AreEqual(openId, PrototypeUiFlow.SelectedWeapon);
                }
            }
            finally
            {
                UiEvents.Raised -= Record;
                PrototypeUiFlow.SelectedWeapon = before;   // the session's choice is put back (a global: never leak it)
            }

            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>A weapon row is "Weapon.&lt;weaponId&gt;.&lt;ClassicWeaponId&gt;" (SelectionScreen.BuildWeapons).</summary>
        private static bool TryWeapon(UiButton row, out ClassicWeaponId id)
        {
            id = default(ClassicWeaponId);
            if (!row.name.StartsWith("Weapon.", System.StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = row.name.Split('.');
            return parts.Length >= 3 && System.Enum.TryParse(parts[parts.Length - 1], out id);
        }
    }
}
