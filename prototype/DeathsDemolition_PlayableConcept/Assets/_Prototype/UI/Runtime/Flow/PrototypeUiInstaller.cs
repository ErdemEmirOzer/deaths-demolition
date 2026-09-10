using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Puts the right screen into whichever scene just loaded.
    ///
    /// WHY THE UI INSTALLS ITSELF INSTEAD OF LIVING IN THE SCENE FILES
    /// --------------------------------------------------------------
    /// Three reasons, in order of how much they matter:
    ///
    ///   1. The Run scene belongs to another task. The HUD and the pause screen have to
    ///      appear in it without this module ever editing Run.unity, and a scene-loaded
    ///      hook does that with no shared file and no merge conflict.
    ///   2. A UI serialised into a .unity file stores literal colours and font sizes
    ///      where no token can reach them. Everything here is built by
    ///      <see cref="UiFactory"/> at runtime, so the accessibility audit measures what
    ///      is actually drawn.
    ///   3. The meta scenes can then be almost empty, which means they can be generated
    ///      by a menu item rather than hand-written as YAML.
    ///
    /// A screen that is already present in a scene is left alone, so a hand-authored
    /// scene can still override any of this.
    /// </summary>
    public static class PrototypeUiInstaller
    {
        private static bool s_hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            if (s_hooked)
            {
                return;
            }

            s_hooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;

            // The first scene is already loaded by the time this runs.
            BuildForScene(SceneManager.GetActiveScene().name);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive)
            {
                return;
            }

            BuildForScene(scene.name);
        }

        /// <summary>
        /// Creates the screens a scene needs. Public and idempotent, so an editor tool
        /// or a hand-made test scene can call it directly.
        /// </summary>
        public static void BuildForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            if (sceneName == PrototypeScenes.MainMenu)
            {
                Ensure<MainMenuScreen>();
            }
            else if (sceneName == PrototypeScenes.Selection)
            {
                Ensure<SelectionScreen>();
            }
            else if (sceneName == PrototypeScenes.Results)
            {
                Ensure<ResultsScreen>();
            }
            else if (sceneName == PrototypeScenes.Run)
            {
                Ensure<RunHudScreen>();

                // The round-end upgrade screen. It lives in the run scene and not in a
                // scene of its own: the round-end freeze is a RunFlowClock hold, not a
                // scene change, so the run has to stay loaded underneath it.
                Ensure<UpgradeScreen>();

                // Created last of the three canvases so its sorting order (SortingPause)
                // is the only thing deciding what is on top, not the creation order.
                Ensure<PauseScreen>();
                Ensure<SelectedSkinApplier>();
            }
        }

        private static T Ensure<T>() where T : MonoBehaviour
        {
            T existing = Object.FindObjectOfType<T>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("UI." + typeof(T).Name);
            return go.AddComponent<T>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_hooked = false;
        }
    }

    /// <summary>
    /// Carries the selection screen's cosmetic choice into the run.
    ///
    /// The run scene may spawn its player from a <c>PlayerSpawner</c> that was authored
    /// with a fixed skin, and the skin is the one part of the loadout with no existing
    /// hand-off point - the weapon already has <c>WeaponRegistry.SelectedWeapon</c>.
    /// Rather than reach into another task's scene, this waits for the rig to exist and
    /// recolours it.
    ///
    /// This is the ONLY thing in the UI module that writes to a gameplay object, and it
    /// is safe by construction: <c>PlayerRig.ApplySkin</c> is documented as cosmetic
    /// only, and the request states the three skins never affect gameplay. If the run
    /// flow later applies the skin itself, this becomes a redundant no-op.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SelectedSkinApplier : MonoBehaviour
    {
        private PlayerRig _applied;

        private void Update()
        {
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null || rig == _applied)
            {
                return;
            }

            _applied = rig;
            rig.ApplySkin(PrototypeUiFlow.SelectedSkin);
        }
    }
}
