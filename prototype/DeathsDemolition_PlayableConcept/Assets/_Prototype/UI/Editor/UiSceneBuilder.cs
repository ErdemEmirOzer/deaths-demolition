using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// Generates the three meta scenes and puts the whole flow into the build settings.
    ///
    /// WHY THE SCENES ARE GENERATED AND NOT AUTHORED
    /// ---------------------------------------------
    /// Each of these scenes contains exactly one empty GameObject with one component on
    /// it. Everything the player sees is built by <see cref="UiFactory"/> when the
    /// screen wakes up. Three consequences, all of them the point:
    ///
    ///   * no colour, font size or margin is ever serialised into a .unity file, so the
    ///     accessibility audit measures the values that are actually drawn;
    ///   * the scenes can be produced with one menu item instead of by hand-writing
    ///     YAML, which is both error-prone and unreviewable;
    ///   * two people can add screens without ever touching the same file.
    ///
    /// Run.unity is NEVER written by this tool - it belongs to the run task. It is only
    /// added to the build settings, and only if it already exists.
    /// </summary>
    public static class UiSceneBuilder
    {
        private const string SceneFolder = "Assets/_Prototype/Scenes";

        private const string MenuRoot = "Tools/Deaths Demolition/UI/";

        /// <summary>Build-settings order. Boot must be index 0: it is the entry point.</summary>
        private static readonly string[] s_flowOrder =
        {
            PrototypeScenes.Boot,
            PrototypeScenes.MainMenu,
            PrototypeScenes.Selection,
            PrototypeScenes.Run,
            PrototypeScenes.Results
        };

        [MenuItem(MenuRoot + "Build Meta Scenes", false, 100)]
        public static void BuildMetaScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[UI] Leave play mode before building the meta scenes.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Directory.CreateDirectory(SceneFolder);

            CreateScene<MainMenuScreen>(PrototypeScenes.MainMenu);
            CreateScene<SelectionScreen>(PrototypeScenes.Selection);
            CreateScene<ResultsScreen>(PrototypeScenes.Results);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SyncBuildSettings();

            Debug.Log(
                "[UI] Meta scenes built: MainMenu, Selection, Results. " +
                "Build settings order is now " + DescribeBuildSettings() + ".");
        }

        [MenuItem(MenuRoot + "Sync Build Settings", false, 101)]
        public static void SyncBuildSettings()
        {
            var ordered = new List<EditorBuildSettingsScene>(s_flowOrder.Length);
            var seen = new HashSet<string>();

            for (int i = 0; i < s_flowOrder.Length; i++)
            {
                string path = PathFor(s_flowOrder[i]);
                if (!File.Exists(path))
                {
                    // Expected while another task has not created its scene yet.
                    continue;
                }

                ordered.Add(new EditorBuildSettingsScene(path, true));
                seen.Add(path);
            }

            // Anything already registered that is not part of the flow keeps its place
            // at the end. This tool owns the ORDER of the flow, not the whole list.
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null && !seen.Contains(existing[i].path))
                {
                    ordered.Add(existing[i]);
                }
            }

            EditorBuildSettings.scenes = ordered.ToArray();
        }

        [MenuItem(MenuRoot + "Rebuild Screens In Active Scene", false, 120)]
        public static void RebuildScreensInActiveScene()
        {
            // Runtime callable on purpose: the same entry point the scene-loaded hook
            // uses, so a screen can be re-created during play without a scene reload.
            PrototypeUiInstaller.BuildForScene(SceneManager.GetActiveScene().name);
        }

        [MenuItem(MenuRoot + "Open Profile Save Folder", false, 140)]
        public static void OpenProfileFolder()
        {
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }

        private static void CreateScene<T>(string sceneName) where T : MonoBehaviour
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // The screen builds its own canvas, event system and clearing camera when it
            // wakes, so the scene itself needs nothing else at all.
            var host = new GameObject("UI." + typeof(T).Name);
            host.AddComponent<T>();

            string path = PathFor(sceneName);
            EditorSceneManager.SaveScene(scene, path);
        }

        private static string PathFor(string sceneName)
        {
            return SceneFolder + "/" + sceneName + ".unity";
        }

        private static string DescribeBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            var names = new List<string>(scenes.Length);

            for (int i = 0; i < scenes.Length; i++)
            {
                names.Add(Path.GetFileNameWithoutExtension(scenes[i].path));
            }

            return names.Count == 0 ? "empty" : string.Join(" -> ", names);
        }
    }
}
