using System;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · The art pipeline as one menu: AP-001 export → Unity assets, in the only
    /// order that works. Each step reads what the previous one wrote through the
    /// AssetDatabase, so imports must have COMPLETED between steps:
    ///
    ///   1 repack     Source PNG (packed roughness/metallic) → Derived MetallicSmoothness PNG
    ///   2 materials  need the derived PNG IMPORTED as a Texture2D
    ///   3 prefabs    need the materials as assets and the FBX meshes imported
    ///   4 library    needs the 18 prefab assets
    ///   5 layout     JSON → ScriptableObject (independent, but the scene needs it)
    ///   6 scenes     need layout, library, materials, ground mesh
    ///   7 register   needs the pilot scene file on disk
    ///
    /// That is why there is no StartAssetEditing/StopAssetEditing bracket around the run:
    /// batching would defer the very imports the next step depends on, and step 2 would
    /// see a PNG file with no Texture2D behind it. Every step is followed by SaveAssets +
    /// Refresh instead. Slower by a few seconds; correct.
    ///
    /// FAILURE IS AN EXCEPTION, NOT A LOG LINE
    /// ---------------------------------------
    /// The gate runs this headless
    ///   Unity -batchmode -quit -projectPath &lt;project&gt; \
    ///     -executeMethod DD.Art.Pipeline.EditorTools.ArtPipelineMenu.RunAll
    /// and reads the process exit code. Unity only returns non-zero if the executed
    /// method throws, so a step that merely logged an error and returned would produce a
    /// green run with half the assets missing. Every step therefore rethrows, wrapped with
    /// its step number so the log says WHICH step died, and the remaining steps are not
    /// attempted on top of a broken foundation.
    /// </summary>
    public static class ArtPipelineMenu
    {
        private const string LogPrefix = "[ArtPipeline] ";
        private const string MenuRoot = "Tools/Deaths Demolition/Art Pipeline/";

        private const int StepCount = 8;

        [MenuItem(MenuRoot + "Run All", priority = 300)]
        public static void RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run All cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run All: start (" + (Application.isBatchMode ? "batch" : "interactive") + ", Unity " +
                      Application.unityVersion + ")");

            Step(1, "Repack textures", ArtTextureRepack.BuildAll);
            Step(2, "Build materials", ArtMaterialBuilder.BuildAll);
            Step(3, "Build family prefabs", ArtFamilyPrefabBuilder.BuildAll);
            Step(4, "Build family library", BuildLibrary);
            Step(5, "Build layout asset", BuildLayout);
            Step(6, "Build pilot scene", PilotSceneBuilder.BuildPilotScene);
            Step(7, "Build kit shelf", PilotSceneBuilder.BuildKitShelf);
            Step(8, "Register pilot scene in build settings", PilotSceneBuilder.RegisterInBuildSettings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // The summary goes to the log on purpose: in a batch run the Editor.log is the
            // only artefact, and this block is what the evidence folder keeps.
            Debug.Log(ArtPipelineReport.Describe());
            Debug.Log(LogPrefix + "Run All: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-013 atmosphere & VFX

        /// <summary>
        /// WP-013 · Re-authors the library materials on the wind/water shaders, builds the VFX
        /// prefabs and binds the breakables. Materials and prefabs keep their GUIDs, so no
        /// scene or prefab reference changes. Batch: <c>-executeMethod …RunAtmosphereVfx</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Run Atmosphere VFX (WP-013)", priority = 302)]
        public static void RunAtmosphereVfx()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run Atmosphere VFX cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run Atmosphere VFX: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");

            Step(1, "Library materials on DD/Foliage Wind + DD/Water v1", ArtMaterialBuilder.BuildAllLibraryRoles);
            Step(2, "VFX prefabs (pollen, petals, break burst)", ArtVfxBuilder.BuildAll);
            Step(3, "Bind BreakableProp on small_breakable prefabs", () => ArtVfxBuilder.BindBreakables());
            Step(4, "Renderer post-process data (URP default)", () => ArtVfxBuilder.EnablePostProcessData());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run Atmosphere VFX: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-019 AP-010 effects

        /// <summary>WP-019 · AP-010 manifest → VfxCatalog + materials (idempotent). Batch: <c>-executeMethod …RunVfxCatalog</c>.</summary>
        [MenuItem(MenuRoot + "Run VFX Catalog (WP-019)", priority = 303)]
        public static void RunVfxCatalog()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run VFX Catalog cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Step(1, "AP-010 manifest → VfxCatalog + materials", () => ArtVfxImporter.BuildCatalog());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run VFX Catalog: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-012 libraries

        private const int LibraryStepCount = 9;

        /// <summary>
        /// WP-012 · Imports the production libraries on top of the AP-001 pipeline: masks and
        /// materials for the new roles, the shared lighting base, the AP-001 prefabs rebuilt
        /// under ADR-012-1, one prefab per library variation, the pilot/kit-shelf/showcase
        /// scenes, and Run.unity aligned to DEC-068. Batch: <c>-executeMethod …RunLibraries
        /// [-ddShowcaseCapture &lt;png&gt;]</c> writes the showcase reference image after the build.
        /// </summary>
        [MenuItem(MenuRoot + "Run Libraries (WP-012)", priority = 301)]
        public static void RunLibraries()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run Libraries cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run Libraries: start (" + (Application.isBatchMode ? "batch" : "interactive") + ", Unity " +
                      Application.unityVersion + ")");

            LibraryStep(1, "Repack textures (AP-001 + library roles)", () => { ArtTextureRepack.BuildAll(); ArtTextureRepack.BuildAllLibrary(); });
            LibraryStep(2, "Build materials (AP-001 + library roles)", () => { ArtMaterialBuilder.BuildAll(); ArtMaterialBuilder.BuildAllLibraryRoles(); });
            LibraryStep(3, "Build lighting base (overcast profile + shared sky)", ArtLightingBuilder.BuildAll);
            LibraryStep(4, "Rebuild AP-001 family prefabs (ADR-012-1 thresholds, LOD2 shadows off)", ArtFamilyPrefabBuilder.BuildAll);
            LibraryStep(5, "Build library prefabs (AP-002, AP-003, AP-003B)", ArtLibraryPrefabBuilder.BuildAll);
            LibraryStep(6, "Rebuild pilot scene (shared profile)", PilotSceneBuilder.BuildPilotScene);
            LibraryStep(7, "Rebuild kit shelf", PilotSceneBuilder.BuildKitShelf);
            LibraryStep(8, "Align Run.unity (DEC-068 sky, shared profile)", ArtLightingBuilder.AlignRunScene);
            LibraryStep(9, "Build library showcase", ArtLibraryShowcaseBuilder.Build);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string capture = Arg("-ddShowcaseCapture");
            if (!string.IsNullOrEmpty(capture))
            {
                ArtLibraryShowcaseBuilder.CaptureReference(capture, 1920, 1080);
            }

            Debug.Log(ArtPipelineReport.Describe());
            Debug.Log(LogPrefix + "Run Libraries: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-016 characters

        /// <summary>
        /// WP-016 · AP-006 player + AP-007 zombie → Unity (Import Contract v1.1, prefabs,
        /// module library, animation sets, Resources/DD_Characters). Batch:
        /// <c>-executeMethod …RunCharacters</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Run Characters (WP-016)", priority = 304)]
        public static void RunCharacters()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run Characters cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run Characters: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");
            LibraryStep(1, "Character avatars (v1.1 re-import of dependents)", () => ArtCharacterPrefabBuilder.EnsureAvatars());
            LibraryStep(2, "Character derived textures (MetallicSmoothness+AO, DetailMask)", ArtCharacterPrefabBuilder.BuildTextures);
            LibraryStep(3, "Character materials (M_player, M_zombie_atlas ×4)", ArtCharacterPrefabBuilder.BuildMaterials);
            LibraryStep(4, "Animation sets + controllers (WP-018: AP-009 blend trees / layers / events)", ArtCharacterPrefabBuilder.BuildAnimation);
            LibraryStep(5, "Player_AP006 prefab", () => ArtCharacterPrefabBuilder.BuildPlayerPrefab());
            DD.Prototype.Characters.ZombieModuleLibrary library = null;
            LibraryStep(6, "Zombie module library", () => library = ArtCharacterPrefabBuilder.BuildZombieLibrary());
            // WP-017 · AP-008 armour: derived maps, atlas materials, merged-mesh library (before the prefab, which carries the armour slots).
            LibraryStep(7, "Armour derived textures (MetallicSmoothness+AO, lacquer DetailMask)", ArtArmorBuilder.BuildTextures);
            LibraryStep(8, "Armour materials (M_zombie_armor ×4)", ArtArmorBuilder.BuildMaterials);
            LibraryStep(9, "Armour library (6 variants × 3 fits × 3 states × 4 LODs, merged sub-meshes)", () => ArtArmorBuilder.BuildLibrary());
            LibraryStep(10, "Zombie_AP007 prefab (+ armour slots)", () => ArtCharacterPrefabBuilder.BuildZombiePrefab(library));
            LibraryStep(11, "Resources/DD_Characters asset set", () => ArtCharacterPrefabBuilder.BuildAssetSet());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run Characters: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-015 asset budget

        /// <summary>
        /// WP-015 · Asset budget pass (ADR-015-1/2): re-import every contract texture at its
        /// class size/streaming, rebuild the lighting base (the sky horizon follows the DEC-076
        /// fog colour), refresh the scenes that store RenderSettings (pilot, Run alignment,
        /// showcase) and re-import the AP-005 world (placement overrides, stand-in ground,
        /// NavMesh, build settings). Prefabs and materials are untouched.
        /// Batch: <c>-executeMethod …RunAssetBudget</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Run Asset Budget (WP-015)", priority = 303)]
        public static void RunAssetBudget()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run Asset Budget cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run Asset Budget: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");
            LibraryStep(1, "Re-import contract textures (ADR-015-1 classes, streaming)", ReimportContractTextures);
            LibraryStep(2, "Build lighting base (overcast profile + shared sky, DEC-076 horizon)", ArtLightingBuilder.BuildAll);
            LibraryStep(3, "Rebuild pilot scene (RenderSettings)", PilotSceneBuilder.BuildPilotScene);
            LibraryStep(4, "Align Run.unity (shared profile + sky)", ArtLightingBuilder.AlignRunScene);
            LibraryStep(5, "Import AP-005 placement (overrides, stand-in ground, NavMesh, build settings)", () => PlacementImporter.ImportAll());
            LibraryStep(6, "Rebuild library showcase (RenderSettings)", ArtLibraryShowcaseBuilder.Build);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run Asset Budget: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-029 lighting & look

        /// <summary>
        /// WP-029 · Look pass without the bake: neutral LUT strips, the overcast profile (+ Color
        /// Lookup when DD_Look_v1.png exists), renderer SSAO + shadow distance, Run.unity alignment.
        /// Batch: <c>-executeMethod …RunLook</c>. The bake is <see cref="ArtLightingBake.BakeWorld"/> (tools/bake-lighting.sh).
        /// </summary>
        [MenuItem(MenuRoot + "Run Look (WP-029)", priority = 306)]
        public static void RunLook()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run Look cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run Look: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");
            LibraryStep(1, "Contract v1.2 environment models (lightmap UVs, calculated margin)", ReimportContractModels);
            LibraryStep(2, "Neutral LUT strips (32 / 64)", LutBuilder.BuildNeutralStrips);
            LibraryStep(3, "Overcast profile v2 (grade + Color Lookup)", () => ArtLightingBuilder.BuildOvercastProfile());
            LibraryStep(4, "Renderer: SSAO feature + shadow distance", ArtLightingBake.ConfigureRenderer);
            LibraryStep(5, "Align Run.unity (shared profile + sky)", ArtLightingBuilder.AlignRunScene);
            LibraryStep(6, "Prepare World_AP005 for the bake (sun, probes, reflection probes, settings)", () => ArtLightingBake.PrepareWorld());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run Look: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-017 world v2

        /// <summary>
        /// WP-017 · AP-005B → Unity world v2: prop textures/masks + roof-course normal, library
        /// materials (prop roles, world-master copies), the AP-005B prop prefabs, then the
        /// placement import with the delta (asset + World_AP005.unity + NavMesh + build settings)
        /// and the showcase. Batch: <c>-executeMethod …RunWorldV2</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Run World v2 (WP-017)", priority = 305)]
        public static void RunWorldV2()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run World v2 cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run World v2: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");
            LibraryStep(1, "Library masks (prop ORM → MetallicSmoothness) + roof-course normal", ArtTextureRepack.BuildAllLibrary);
            LibraryStep(2, "Library materials (prop roles, plaster/roof world copies)", ArtMaterialBuilder.BuildAllLibraryRoles);
            LibraryStep(3, "Build AP-005B prop prefabs", () => ArtLibraryPrefabBuilder.BuildLibrary("AP-005B"));
            LibraryStep(4, "Import AP-005 placement + AP-005B delta (asset, World_AP005.unity, NavMesh, build settings)", () => PlacementImporter.ImportAll());
            LibraryStep(5, "Rebuild library showcase (with AP-005B)", ArtLibraryShowcaseBuilder.Build);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run World v2: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-038 world v4

        /// <summary>
        /// WP-038 · AP-015P → Unity world v4: the surface materials (DD/GroundBlend, DD/Water v2), then the
        /// placement import (v3 chain) with the ground / route / paving plates and water meshes in place of
        /// the stand-in, NavMesh rebake on the plates, the look's world prepare (sun / probes / chart policy)
        /// and the showcase. The lightmap bake is a separate step (tools/bake-lighting.sh).
        /// Batch: <c>-executeMethod …RunWorldV4</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Run World v4 (WP-038)", priority = 307)]
        public static void RunWorldV4()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run World v4 cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run World v4: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");
            LibraryStep(1, "Contract models (mesh compression, lightmap UVs)", ReimportContractModels);
            LibraryStep(2, "Contract textures (derived masks ≤ 1024, water inputs)", ReimportContractTextures);
            LibraryStep(2, "Library masks + roof-course normal", ArtTextureRepack.BuildAllLibrary);
            LibraryStep(3, "Library materials", ArtMaterialBuilder.BuildAllLibraryRoles);
            LibraryStep(4, "AP-018 wet packed maps → derived MetallicSmoothness", ArtSurfaceImporter.BuildWetTextures);
            LibraryStep(4, "Decal renderer feature (AP-018 puddles)", ArtSurfaceImporter.ConfigureDecalFeature);
            LibraryStep(4, "Overcast sky (AP-019 band → lat-long, WP-038E)", () => ArtSkyBuilder.Build());
            LibraryStep(4, "Surface materials (M_ground_blend, M_water_v2, M_kerb_stone, M_decal_puddle, M_horizon_*)", ArtSurfaceImporter.BuildMaterials);
            // Mesh compression (Contract v1.3) quantises the imported vertices: the family / library prefabs carry LOD reference
            // points and bounds from the meshes, so they are rebuilt from the compressed meshes (idempotence gate).
            LibraryStep(4, "Rebuild AP-001 family prefabs + library prefabs on the compressed meshes", () => { ArtFamilyPrefabBuilder.BuildAll(); ArtLibraryPrefabBuilder.BuildAll(); });
            LibraryStep(5, "Import AP-005 placement + delta chain + AP-015P surfaces (asset, World_AP005.unity, NavMesh, build settings)", () => PlacementImporter.ImportAll());
            LibraryStep(6, "Prepare World_AP005 for the bake (sun, probes, reflection probes, chart policy)", () => ArtLightingBake.PrepareWorld());
            LibraryStep(7, "Rebuild library showcase", ArtLibraryShowcaseBuilder.Build);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run World v4: done in " + Seconds(total) + " s.");
        }

        /// <summary>
        /// WP-074 B5 · The AP-023 world v5 kit (P-05 manifest v004, staged by wp-074/tools/stage-ap023.py): contract import, masks,
        /// materials, one prefab per variation, the showcase. Batch: <c>-executeMethod …RunLibraryAp023</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Run Library AP-023 (WP-074 B5)", priority = 308)]
        public static void RunLibraryAp023()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run Library AP-023 cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run Library AP-023: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");
            LibraryStep(1, "Contract models (AP-023 FBX: mesh compression, lightmap UVs)", ReimportContractModels);
            LibraryStep(2, "Contract textures (AP-023 tiles + atlases at the ADR-015-1 v3 classes)", ReimportContractTextures);
            LibraryStep(3, "Library masks (AP-023 ORM → MetallicSmoothnessAO)", ArtTextureRepack.BuildAllLibrary);
            LibraryStep(4, "Library materials (ap023_* roles)", ArtMaterialBuilder.BuildAllLibraryRoles);
            LibraryStep(5, "AP-023 prefabs", () => ArtLibraryPrefabBuilder.BuildLibrary("AP-023"));
            LibraryStep(6, "Rebuild library showcase", ArtLibraryShowcaseBuilder.Build);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run Library AP-023: done in " + Seconds(total) + " s.");
        }

        /// <summary>
        /// WP-038 · The surface half of <see cref="RunWorldV4"/> only: surface materials (parameter round), the placement
        /// import with the AP-015P plates (scene, NavMesh with the water rows, build settings) and the bake preparation.
        /// Models, textures and prefabs are untouched — re-bake afterwards (<c>ArtLightingBake.BakeWorld</c>).
        /// </summary>
        [MenuItem(MenuRoot + "Run World v4 Surfaces (WP-038)", priority = 308)]
        public static void RunWorldV4Surfaces()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run World v4 Surfaces cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run World v4 Surfaces: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");
            LibraryStep(1, "Contract models (WP-038E: the nine ground plates readable for the puddle seeds)", ReimportContractModels);
            LibraryStep(1, "Contract textures (AP-018 wet inputs)", ReimportContractTextures);
            LibraryStep(1, "AP-018 wet packed maps → derived MetallicSmoothness", ArtSurfaceImporter.BuildWetTextures);
            LibraryStep(1, "Decal renderer feature (AP-018 puddles)", ArtSurfaceImporter.ConfigureDecalFeature);
            LibraryStep(1, "Overcast sky (AP-019 band → lat-long, WP-038E)", () => ArtSkyBuilder.Build());
            LibraryStep(1, "Surface materials (M_ground_blend, M_water_v2, M_kerb_stone, M_decal_puddle, M_horizon_*)", ArtSurfaceImporter.BuildMaterials);
            LibraryStep(2, "Import AP-005 placement + delta chain + AP-015P surfaces (asset, World_AP005.unity, NavMesh, build settings)", () => PlacementImporter.ImportAll());
            LibraryStep(3, "Prepare World_AP005 for the bake (sun, probes, reflection probes, chart policy)", () => ArtLightingBake.PrepareWorld());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run World v4 Surfaces: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-020 world v3

        /// <summary>
        /// WP-020 · AP-005C → Unity world v3: derived masks from the ORM v002 props (+ player ORM v004),
        /// library materials, then the placement import with the delta chain v002 → v003 (overrides
        /// folded → empty file), the named NavMesh areas (Bridge / Corridor / Gate) and the NavMesh
        /// rebake, and the showcase. Batch: <c>-executeMethod …RunWorldV3</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Run World v3 (WP-020)", priority = 306)]
        public static void RunWorldV3()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run World v3 cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run World v3: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");
            LibraryStep(1, "Library masks (prop ORM v002 → MetallicSmoothness) + roof-course normal", ArtTextureRepack.BuildAllLibrary);
            LibraryStep(2, "Character derived masks (player ORM v004 → MetallicSmoothness)", ArtCharacterPrefabBuilder.BuildTextures);
            LibraryStep(3, "Library materials (prop roles, plaster/roof world copies)", ArtMaterialBuilder.BuildAllLibraryRoles);
            LibraryStep(4, "Import AP-005 placement + delta chain v002 → v003 (asset, World_AP005.unity, NavMesh areas + bake, build settings)", () => PlacementImporter.ImportAll());
            LibraryStep(5, "Rebuild library showcase", ArtLibraryShowcaseBuilder.Build);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run World v3: done in " + Seconds(total) + " s.");
        }

        // ------------------------------------------------------------ WP-014 world assembly

        /// <summary>
        /// WP-014 · AP-005 → Unity world: the AP-005 module prefabs, the placement asset,
        /// World_AP005.unity (instances + baked NavMesh), the build-settings entry, the
        /// Resources world-source asset, and the showcase rebuilt with the new library.
        /// Batch: <c>-executeMethod …RunWorldAssembly [-ddWorldSourceDefault Procedural|AP005]</c>.
        /// </summary>
        [MenuItem(MenuRoot + "Run World Assembly (WP-014)", priority = 302)]
        public static void RunWorldAssembly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run World Assembly cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "Run World Assembly: start (" + (Application.isBatchMode ? "batch" : "interactive") + ")");

            LibraryStep(1, "Build AP-005 module prefabs", () => ArtLibraryPrefabBuilder.BuildLibrary("AP-005"));
            LibraryStep(2, "Import AP-005 placement (asset + World_AP005.unity + NavMesh + build settings)", () => PlacementImporter.ImportAll());
            LibraryStep(3, "Rebuild library showcase (with AP-005)", ArtLibraryShowcaseBuilder.Build);

            string source = Arg("-ddWorldSourceDefault");
            if (!string.IsNullOrEmpty(source) && Enum.TryParse(source, true, out DD.Prototype.World.WorldSource parsed))
            {
                PlacementImporter.EnsureWorldSourceAsset(parsed);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run World Assembly: done in " + Seconds(total) + " s.");
        }

        /// <summary>
        /// Re-imports every contract texture whose importer no longer matches the contract
        /// (after a contract change the postprocessor only runs on the next import).
        /// </summary>
        /// <summary>
        /// WP-029 · Re-imports every environment model whose importer no longer matches Contract v1.2
        /// (lightmap UV generation and the calculated chart margin — ADR-029-2). Characters have their
        /// own contract and are left alone. Cheap when nothing is stale; part of <c>Run Look</c> and of
        /// <see cref="ArtLightingBake.BakeWorld"/> so a margin change cannot be baked with stale UVs.
        /// </summary>
        [MenuItem(MenuRoot + "Reimport Contract Models", priority = 342)]
        public static void ReimportContractModels()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { ArtImportContract.ArtRoot });
            int reimported = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!ArtImportContract.IsContractModel(path) || ArtCharacterContract.IsCharacterModel(path))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null || ArtImportContract.ValidateModel(importer).Count == 0)
                {
                    continue;
                }

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                reimported++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "Reimport contract models: " + reimported + " of " + guids.Length + " re-imported.");
        }

        [MenuItem(MenuRoot + "Reimport Contract Textures", priority = 341)]
        public static void ReimportContractTextures()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ArtImportContract.ArtRoot });
            int reimported = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!ArtImportContract.IsContractTexture(path))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || ArtImportContract.ValidateTexture(importer, ArtImportContract.RoleOf(path)).Count == 0)
                {
                    continue;
                }

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                reimported++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "Reimport contract textures: " + reimported + " of " + guids.Length + " re-imported.");
        }

        private static void LibraryStep(int index, string title, Action action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Debug.Log(LogPrefix + "[" + index + "/" + LibraryStepCount + "] " + title);
            action();
            Debug.Log(LogPrefix + "[" + index + "/" + LibraryStepCount + "] " + title + " - " + Seconds(sw) + " s");
        }

        private static string Arg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        // ------------------------------------------------------------ single steps

        [MenuItem(MenuRoot + "1. Repack Textures", priority = 320)]
        public static void RepackTextures()
        {
            Single("Repack textures", ArtTextureRepack.BuildAll);
        }

        [MenuItem(MenuRoot + "2. Build Materials", priority = 321)]
        public static void BuildMaterials()
        {
            Single("Build materials", ArtMaterialBuilder.BuildAll);
        }

        [MenuItem(MenuRoot + "3. Build Family Prefabs", priority = 322)]
        public static void BuildFamilyPrefabs()
        {
            Single("Build family prefabs", ArtFamilyPrefabBuilder.BuildAll);
            Single("Build family library", BuildLibrary);
        }

        [MenuItem(MenuRoot + "4. Build Layout Asset", priority = 323)]
        public static void BuildLayoutAsset()
        {
            Single("Build layout asset", BuildLayout);
        }

        [MenuItem(MenuRoot + "5. Build Pilot Scene", priority = 324)]
        public static void BuildPilotScene()
        {
            Single("Build pilot scene", PilotSceneBuilder.BuildPilotScene);
        }

        [MenuItem(MenuRoot + "6. Build Kit Shelf", priority = 325)]
        public static void BuildKitShelf()
        {
            Single("Build kit shelf", PilotSceneBuilder.BuildKitShelf);
        }

        [MenuItem(MenuRoot + "7. Register Pilot Scene In Build Settings", priority = 326)]
        public static void RegisterInBuildSettings()
        {
            Single("Register pilot scene in build settings", PilotSceneBuilder.RegisterInBuildSettings);
        }

        // ------------------------------------------------------------- utilities

        [MenuItem(MenuRoot + "Print Report", priority = 340)]
        public static void PrintReport()
        {
            Debug.Log(ArtPipelineReport.Describe());
        }

        [MenuItem(MenuRoot + "Open Pilot Scene", priority = 360)]
        public static void OpenPilotScene()
        {
            OpenScene(ArtPipelinePaths.PilotScene);
        }

        [MenuItem(MenuRoot + "Open Kit Shelf", priority = 361)]
        public static void OpenKitShelf()
        {
            OpenScene(ArtPipelinePaths.KitShelfScene);
        }

        // ------------------------------------------------------------- internals

        private static void BuildLibrary()
        {
            ArtFamilyPrefabBuilder.BuildLibrary();
        }

        private static void BuildLayout()
        {
            GoldenSliceLayoutBuilder.Build();
        }

        /// <summary>One RunAll step: timed, logged, imports flushed, failure wrapped and rethrown.</summary>
        private static void Step(int index, string name, Action action)
        {
            // index 0 = run from its own menu item, outside the numbered sequence.
            string label = index > 0
                ? "step " + index + "/" + StepCount + " '" + name + "'"
                : "'" + name + "'";
            Stopwatch watch = Stopwatch.StartNew();
            Debug.Log(LogPrefix + label + ": start");

            try
            {
                action();

                // Flush before the next step reads the AssetDatabase. See class remarks.
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (OperationCanceledException)
            {
                // The user declined to save the open scene. Not a failure, so no error
                // line; Single() turns it into a message and batch mode never raises it.
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + label + " FAILED after " + Seconds(watch) + " s: " + e.Message);
                throw new InvalidOperationException(LogPrefix + label + " failed: " + e.Message, e);
            }

            Debug.Log(LogPrefix + label + ": done in " + Seconds(watch) + " s");
        }

        /// <summary>
        /// A step run from its own menu item. Same flush as inside RunAll; a user
        /// cancelling a "save the open scene?" dialog is a message, not an error.
        /// </summary>
        private static void Single(string name, Action action)
        {
            try
            {
                Step(0, name, action);
            }
            catch (OperationCanceledException e)
            {
                Debug.Log(e.Message);
            }
        }

        private static void OpenScene(string path)
        {
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning(LogPrefix + path + " does not exist yet. Run the pipeline first.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static string Seconds(Stopwatch watch)
        {
            return watch.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
