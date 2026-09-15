using System;
using System.Collections.Generic;
using DD.Prototype.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-012 · Writes <see cref="ArtLibraryPaths.ShowcaseScene"/>: every prefab of every
    /// imported library on one shelf, one row per family (variations along +X, families
    /// along +Z, libraries stacked), a 3D label per row, the shared overcast lighting
    /// (same sun / volume / sky as the pilot) and a stand-in ground. Editor-only: not in
    /// the build settings. This is where the art director judges the libraries in URP and
    /// where the visual-regression reference pose <see cref="ReferencePose"/> is taken.
    /// </summary>
    public static class ArtLibraryShowcaseBuilder
    {
        private const string LogPrefix = "[ArtLibrary] ";

        public const float ColumnSpacing = 8f;
        public const float RowSpacing = 10f;
        public const float LibraryGap = 10f;
        /// <summary>Stand-in ground: the shelf runs to ~320 m in +Z (three libraries, 29 family rows).</summary>
        public const float GroundSize = 420f;

        /// <summary>Camera pose for the regression reference: high, looking down the shelf from its south-west corner.</summary>
        public static readonly ArtVisualRegression.CameraPose ReferencePose =
            new ArtVisualRegression.CameraPose("showcase_overview", new Vector3(-30f, 30f, 60f), new Vector3(30f, 0f, 150f), 42f);

        public static void Build()
        {
            PilotSceneBuilder.GuardEditMode("Build Library Showcase");
            PilotSceneBuilder.ConfirmSceneChange();

            var libraries = new List<ArtLibraryAsset>();
            var missing = new List<string>();
            for (int i = 0; i < ArtLibraryPaths.Packages.Length; i++)
            {
                string ap = ArtLibraryPaths.Packages[i];
                var index = AssetDatabase.LoadAssetAtPath<ArtLibraryAsset>(ArtLibraryPaths.IndexAsset(ap));
                if (index == null)
                {
                    missing.Add(ap);
                }
                else
                {
                    libraries.Add(index);
                }
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(LogPrefix + "Library index missing for " + string.Join(", ", missing) +
                                                    " (run ArtLibraryPrefabBuilder.BuildAll first).");
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtLibraryPaths.OvercastVolumeProfile);
            var earth = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("earth"));

            PilotSceneBuilder.EnsureFolder(PilotSceneBuilder.AssetFolderOf(ArtLibraryPaths.ShowcaseScene));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            PilotSceneBuilder.ApplyEnvironment();
            PilotSceneBuilder.CreateSun();
            if (profile != null)
            {
                PilotSceneBuilder.CreateVolume(profile);
            }

            BuildGround(earth);

            float z = 0f;
            int instances = 0;
            for (int l = 0; l < libraries.Count; l++)
            {
                ArtLibraryAsset library = libraries[l];
                var shelf = new GameObject(library.ap);
                string currentFamily = null;
                int column = 0;
                for (int i = 0; i < library.items.Count; i++)
                {
                    ArtLibraryAsset.Item item = library.items[i];
                    if (item == null || item.prefab == null)
                    {
                        continue;
                    }

                    if (!string.Equals(item.family, currentFamily, StringComparison.Ordinal))
                    {
                        if (currentFamily != null)
                        {
                            z += RowSpacing;
                        }

                        currentFamily = item.family;
                        column = 0;
                        BuildLabel(shelf.transform, library.ap + " · " + item.family, new Vector3(-ColumnSpacing, 0.1f, z));
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(item.prefab, scene);
                    instance.transform.SetParent(shelf.transform, true);
                    instance.transform.position = new Vector3(column * ColumnSpacing, 0f, z);
                    column++;
                    instances++;
                }

                z += RowSpacing + LibraryGap;
            }

            PilotSceneBuilder.Save(scene, ArtLibraryPaths.ShowcaseScene);
            Debug.Log(LogPrefix + "Wrote " + ArtLibraryPaths.ShowcaseScene + " with " + instances + " prefab instances from " +
                      libraries.Count + " libraries (rows to z = " + z.ToString("0") + " m). Not in the build settings on purpose.");
        }

        /// <summary>Captures the reference pose of the open showcase scene to a PNG (evidence; never committed).</summary>
        public static void CaptureReference(string path, int width, int height)
        {
            ArtVisualRegression.CaptureToPng(ReferencePose, width, height, path);
            Debug.Log(LogPrefix + "Showcase reference captured: " + path);
        }

        private static void BuildGround(Material earth)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Ground (stand-in)";
            go.transform.position = new Vector3(30f, 0f, 150f);
            go.transform.localScale = new Vector3(GroundSize / 10f, 1f, GroundSize / 10f);
            go.isStatic = true;
            var renderer = go.GetComponent<MeshRenderer>();
            if (earth != null)
            {
                renderer.sharedMaterial = earth;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void BuildLabel(Transform parent, string text, Vector3 position)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.characterSize = 0.5f;
            mesh.fontSize = 32;
            mesh.anchor = TextAnchor.MiddleRight;
            mesh.color = Color.white;
        }
    }
}
