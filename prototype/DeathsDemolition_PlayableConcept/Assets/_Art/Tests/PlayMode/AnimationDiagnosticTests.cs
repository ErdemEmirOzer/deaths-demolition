using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-071 r2 (C-00 30.09): S-06's opt-in diagnostics (Codex 29.09, 5f40e9c6 / f94ccab3), moved out of AnimationTests — they only run
    /// when explicitly selected (NUnit [Explicit]) with their command-line flags (-ddS06PalmProof …); CI never lists them.
    /// </summary>
    [TestFixture]
    [Explicit("S-06 diagnostic — run explicitly with DD_S06_PALM_PROOF=<file> (env; tools/test.sh passes no S06 flag, H11)")]
    [Category("Diagnostics")]
    public sealed class AnimationDiagnosticTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, YatismaZamanAsimi = 30f;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            UiDataPaths.UseProcessLocalRoot();   // never the real persistentDataPath
            RunFlowClock.ClearHolds();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi) yield return null;
            }
        }

        // S-06: one normal boot/selection/run observation; no reload, pose reset or aim writer.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator S06_LegacyAr1_ImportedSurface_OnePostHumanoidFrame()
        {
            string path = S06PalmSurfaceObserver.Argument("-ddS06PalmProof");
            if (string.IsNullOrEmpty(path))
                Assert.Fail("S-06 imported-surface diagnostic not requested (DD_S06_PALM_PROOF / -ddS06PalmProof absent) — explicit diagnostic, TANI-TESTI-POLITIKASI: no Ignore");
            string correspondencePath = S06PalmSurfaceObserver.Argument("-ddS06CorrespondenceProof");
            // M-50 (C-00 30.09): the output paths never point into the user's data and never overwrite a file
            Assert.IsNull(S06PalmSurfaceObserver.RefuseOutputPath(path), "palm proof path");
            if (!string.IsNullOrEmpty(correspondencePath)) Assert.IsNull(S06PalmSurfaceObserver.RefuseOutputPath(correspondencePath), "correspondence path");
            if (!string.IsNullOrEmpty(correspondencePath))
            {
                Assert.IsFalse(string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(correspondencePath), StringComparison.OrdinalIgnoreCase), "Palm and correspondence output paths must be distinct");
                Assert.IsFalse(System.IO.File.Exists(path) || System.IO.File.Exists(correspondencePath), "Correspondence run requires fresh output paths; no prior proof overwrite");
            }
            UiDataPaths.SetRootOverride(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "ui-sandbox"));
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu, SahneZamanAsimi, "normal MainMenu");
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "normal Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "normal Run/player/weapon");
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(pv, "Actual loaded skinned player unavailable");
            yield return CharacterTests.Bekle(() =>
            {
                var an = pv.Animator; int layer = an != null ? an.GetLayerIndex(C.OverrideUpperLayer) : -1;
                return layer >= 0 && !an.IsInTransition(layer) && an.GetCurrentAnimatorStateInfo(layer).IsName(C.AimState + "_0")
                    && WeaponRegistry.ActiveController.Classic.IsConfigured && !WeaponRegistry.ActiveController.SpecialWeaponActive;
            }, OyuncuZamanAsimi, "actual legacy Aim_0, no pose forcing");
            var observer = pv.gameObject.AddComponent<S06PalmSurfaceObserver>();
            observer.Visual = pv; observer.OutputPath = path;
            yield return CharacterTests.Bekle(() => observer.Completed, OyuncuZamanAsimi, "ONE postHumanoid record");
            bool measured = observer.ImportedSurfaceObserved;
            UnityEngine.Object.Destroy(observer);
            Assert.IsTrue(System.IO.File.Exists(path), "Actual observer proof output missing");
            if (!string.IsNullOrEmpty(correspondencePath))
            {
                Assert.IsTrue(System.IO.File.Exists(correspondencePath), "Requested same-frame correspondence arrays/UNKNOWN receipt missing; no retake");
                StringAssert.Contains("s06.same-frame-imported-baked-arrays.v1", System.IO.File.ReadAllText(correspondencePath), "Correspondence receipt overwritten or wrong schema");
            }
            Assert.IsTrue(measured, "Imported surface or actual identity unavailable; preserve JSON UNKNOWN, no retake");
        }
    }

    /// <summary>One opt-in read-only observation after the existing runtime writers. It never defines an anatomical palm from a bone.</summary>
    [DefaultExecutionOrder(20000)]
    public sealed class S06PalmSurfaceObserver : MonoBehaviour
    {
        public PlayerSkinnedVisual Visual;
        public string OutputPath;
        public bool Completed { get; private set; }
        public bool ImportedSurfaceObserved { get; private set; }
        [Serializable] public sealed class AssetId
        {
            public string name, path, guid, fileId, fileSha256, dependencyHash;
        }
        [Serializable] public sealed class TransformId
        {
            public string name, hierarchy;
            public Vector3 localPosition, worldPosition, localScale, lossyScale;
            public Quaternion localRotation, worldRotation;
            public float[] localToWorldRowMajor, worldToLocalRowMajor;
            public float localToWorldDeterminant;
        }
        [Serializable] public sealed class ClipId
        {
            public AssetId asset;
            public float weight, lengthSeconds, animatorNormalizedTime, observedLoopSeconds, observedFrameAt30fps;
        }
        [Serializable] public sealed class Surface
        {
            public string landmarkName = "ImportedLOD0_SurfaceClosestToAuthoredRightGrip";
            public string anatomicalIdentity = "UNKNOWN: no sealed source anatomical palm CP/polygon/barycentric landmark";
            public string sourceControlpointMapping = "UNKNOWN: importer welding/split/reorder; source CP index is not Unity vertex index";
            public string selection = "Minimum world-space squared distance over all actual baked LOD0 skin triangles; exact float ties counted; no anatomical mask or acceptance threshold";
            public AssetId mesh;
            public TransformId renderer;
            public int bakedVertexCount, importedVertexCount, triangleOrdinal, submeshIndex, submeshTriangleOrdinal, exactFloatTies;
            public int[] orderedBakedVertexIndices;
            public string bakedToImportedVertexCorrespondence = "UNKNOWN: imported mesh unreadable; vertex ordinal equivalence not independently verified";
            public bool rendererEnabled, rendererActiveInHierarchy, rendererIsVisible;
            public string scaleConversion = "BakeMesh(useScale:false), then renderer.localToWorld once; Unity renderer-local to world metres. Source FBX/Blender to Unity correspondence UNKNOWN";
            public Vector3[] orderedBakedRendererLocalVertices, orderedBakedWorldVertices;
            public Vector3 barycentric, rendererLocalPoint, worldPoint, worldWindingNormal, gripMinusSurfaceWorld;
            public float distanceToAuthoredGripMetres;
            public string originalMeshReadable, restWeightsAccess, bakedSnapshotSha256;
            public TransformId[] boundBones;
        }
        [Serializable] public sealed class HumanMap { public string humanName, boneName; public bool useDefaultLimits; public Vector3 minimum, maximum, center; public float axisLength; }
        [Serializable] public sealed class RestBone { public string name; public Vector3 position, scale; public Quaternion rotation; }
        [Serializable] public sealed class MuscleScalar { public string name; public float normalizedValue; }
        [Serializable] public sealed class VertexLayout
        {
            public string attribute, format;
            public int dimension, stream, offset, stride;
        }
        [Serializable] public sealed class UvArray { public int channel, dimension; public Vector4[] values; }
        [Serializable] public sealed class SubmeshArray
        {
            public string topology;
            public int indexStart, indexCount, baseVertex, firstVertex, vertexCount;
            public int[] canonicalIndices;
            public string indicesDomain = "GetIndices(applyBaseVertex:true); baseVertex already applied exactly once";
        }
        [Serializable] public sealed class SkinInfluence { public int boneIndex, weightFloat32Bits; public float weight; }
        [Serializable] public sealed class MatrixArray { public float[] rowMajor; }
        [Serializable] public sealed class EffectiveImporter
        {
            public string access = "UNKNOWN_NOT_CAPTURED", error, animationType, avatarSetup, sourceAvatarReference, skinWeights, normalMode, tangentMode, metaSha256;
            public bool isReadable, bakeAxisConversion, useFileUnits, weldVertices, keepQuads, optimizeMeshPolygons, optimizeMeshVertices, optimizeBones, optimizeGameObjects, customSkinPruningActive;
            public float globalScale, minBoneWeight;
            public int maxBonesPerVertex;
        }
        [Serializable] public sealed class MeshArrays
        {
            public string access = "UNKNOWN_NOT_CAPTURED", error, indexFormat, positionsFloat32LittleEndianBase64;
            public string domain;
            public int vertexCount;
            public Vector3[] positions, normals;
            public Vector4[] tangents;
            public Color[] colors;
            public VertexLayout[] layout;
            public UvArray[] uvChannels;
            public SubmeshArray[] submeshes;
        }
        // Additive raw primitives only: actual instance-loaded metadata and separately timed disk bytes.
        [Serializable] public sealed class ModuleClock
        {
            public string access = "UNKNOWN", utc, phase, stage, unityVersion, error;
            public int frameCount = -1;
            public float timeSeconds, deltaTimeSeconds, realtimeSinceStartupSeconds;
            public long stopwatchTicks;
        }
        [Serializable] public sealed class AssemblyDiskRead
        {
            public string access = "UNKNOWN_NOT_READ", path, fileSha256, error;
            public long bytesRead = -1;
            public ModuleClock readBegin, readEnd;
            public string identityLimit = "Assembly.Location disk bytes during this read; not the loaded or mapped image SHA";
        }
        [Serializable] public sealed class LoadedTypeModule
        {
            public string role, actualTypeFullName, assemblyFullName, moduleName, mvid, location, error;
            public string mvidAccess = "UNKNOWN", locationAccess = "UNKNOWN";
            public string mvidSource = "Actual instance.GetType().Module.ModuleVersionId; never parsed from a disk DLL";
            public string mappedImageSha256, mappedImageShaAccess = "UNKNOWN_NOT_READ_FROM_MAPPED_IMAGE";
            public ModuleClock reflectionBegin, reflectionEnd;
            public AssemblyDiskRead onDiskIdentity = new AssemblyDiskRead();
        }
        [Serializable] public sealed class LoadedModuleBoundary
        {
            public string schema = "s06.actual-loaded-type-module-boundary.v1";
            public string access = "UNKNOWN_PAIRED_RAW_BODY_BAKE_NOT_REACHED";
            public string observerSourceClaim, sourceClaimLimit = "Caller source origin, not executing HEAD or loaded-code SHA";
            public string boundaryLimit = "Readonly before/after the ONE existing exact Body BakeMesh(false); primitive availability does not grant runtime or scientific acceptance";
            public ModuleClock beforeBake, afterBake;
            public LoadedTypeModule observerBeforeBake, observerAfterBake, playerRuntimeBeforeBake, playerRuntimeAfterBake;
        }
        [Serializable] public sealed class CorrespondenceProof
        {
            public string schema = "s06.same-frame-imported-baked-arrays.v1";
            public string status = "UNKNOWN_SOURCE_TO_IMPORTED_TO_BAKED_MAPPING", observerSourceSha, utc, unityVersion, error;
            public string mappingLimit = "Raw observation only. Counts/topology alone do not prove source corner or duplicate vertex identity. No closest-grip triangle chooses anatomy.";
            public string skinAccess = "UNKNOWN_NOT_CAPTURED", skinError;
            public string effectiveSkinLimit = "Stored influences are observed without normalization/pruning. Renderer/QualitySettings policies recorded; CPU/GPU effective correspondence not assumed.";
            public string bakeDomain = "ONE existing BakeMesh(false) sample, renderer-local; baked positions to world via recorded localToWorld exactly once; no extra scale";
            public string rendererQuality, qualitySkinWeights, bakedSnapshotSha256;
            public int frameCount, exactBodyMeshMatches;
            public bool originalIsReadable, rendererEnabled, rendererActive, rendererVisible;
            public float timeSeconds, deltaTimeSeconds;
            public AssetId importedMesh, avatar;
            public bool avatarValid, avatarHuman;
            public string observedState, observedWeaponId, stage;
            public int observedWeaponParameter;
            public EffectiveImporter effectiveImporter;
            public TransformId renderer;
            public Vector3 gripWorld;
            public ClipId[] activeUpperClips;
            public MeshArrays importedRest, bakedRendererLocal;
            public TransformId[] rendererBones;
            public LoadedModuleBoundary loadedModuleBoundary;
            public MatrixArray[] importedBindposes;
            public int[] bonesPerVertex;
            public SkinInfluence[] allBoneWeights;
            public string[] blendshapeNames;
            public float[] blendshapeWeights;
        }
        [Serializable] public sealed class Proof
        {
            public string schema = "s06.imported-skin-grip-observer.v1";
            public string status = "UNKNOWN_ANATOMICAL_PALM_AND_SOURCE_VERTEX_MATCH";
            public string observerSourceSha, utc, stage = "LateUpdate order20000, after Animator/Humanoid and runtime mount900/left-grip910 writers";
            public string domain = "Unity world metres; vectors are Unity axes. Triangle normal from ordered baked world geometry, not bone rotation. Source Blender units are not subtracted.";
            public int frameCount;
            public float timeSeconds, deltaTimeSeconds;
            public string expectedWeapon = "ar_1 / HINAWA, legacy Aim_0", observedWeaponAnimSet, observedState, error;
            public string testIdentity = "DD.Art.Tests.PlayMode.AnimationDiagnosticTests.S06_LegacyAr1_ImportedSurface_OnePostHumanoidFrame", unityVersion, observedWeaponId, observedDisplayName, observedFireMode, observedWorldSource, observedWorldScene, classicModelSource, libraryRosterKey, libraryModelPackage;
            public AssetId[] candidateLod0SkinMeshes;
            public bool actualLoadedLegacyIdentityKnown, specialWeaponActive, actualModelMeshSourcesMatchLibrary, avatarValid, avatarHuman;
            public AssetId[] actualModelMeshes;
            public string activeClipTimeLimit = "Fractional state normalized time times clip length is a state-clock estimate, not source animation sample/correspondence proof";
            public int observedWeaponParameter, activeAuthoredClassicModelCount;
            public bool fpArmsEnabled, tpSpecialDefaultEnabled, stateTransition, mountAligned, mountDeltaApplied;
            public float mountDeltaDegrees, leftGripWeight, leftGripErrorMetres;
            public bool leftGripErrorFinite;
            public AssetId playerPrefab, avatar, baseController, weaponDefinition, weaponLibraryModel;
            public ClipId[] activeUpperClips;
            public HumanMap[] avatarHumanMapping;
            public RestBone[] avatarRestSkeleton;
            public MuscleScalar[] rightArmHandMuscles;
            public TransformId root, rightHandBoneReference, middleProximalBoneReference, socket, weaponMount, actualModelRoot, actualModelFirstChild, authoredGripMarker, leftGripMarker;
            public Vector3 authoredGripOffsetModelMetres, authoredGripOffsetWorldPoint;
            public string actualGripMarkerName;
            public Vector3 observedGripWorldPoint;
            public float socketToObservedGripMetres;
            public Surface surface;
            public LoadedModuleBoundary loadedModuleBoundary = new LoadedModuleBoundary();
            public string counterPart = "WAIT: f44 FP diagnostic inputs absent in frozen legacy TP candidate; FP remains OFF; no extra intake/sample";
            public string proofLimit = "Loaded source/Avatar/mesh identities and observed imported surface can be proven; source CP equivalence/anatomical palm/contact/cause/fix cannot. Historical bone-midpoint proxies are not surface landmarks.";
        }
        /// <summary>
        /// An input of the diagnostic (C-00 30.09, H11: tools/test.sh passes no S06 flag): the environment variable first —
        /// -ddS06PalmProof → DD_S06_PALM_PROOF, -ddS06PalmSourceSha → DD_S06_PALM_SOURCE_SHA, -ddS06CorrespondenceProof →
        /// DD_S06_CORRESPONDENCE_PROOF — then a direct Unity command-line flag of the same name.
        /// </summary>
        /// <summary>
        /// M-50 (C-00 30.09): why an output path is refused, or null — it must be absolute, must not exist (no overwrite), must not
        /// sit under the user's data roots (persistentDataPath, ~/Library/Application Support, ~/Library/Preferences, the live
        /// profile's folder) and must not carry a protected name (profile / settings / prefs / .plist).
        /// </summary>
        public static string RefuseOutputPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathRooted(path)) return "not an absolute path: " + path;
            string full = System.IO.Path.GetFullPath(path);
            if (System.IO.File.Exists(full) || System.IO.Directory.Exists(full)) return "exists (no overwrite): " + full;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            var roots = new List<string> { Application.persistentDataPath, System.IO.Path.Combine(home, "Library", "Application Support"), System.IO.Path.Combine(home, "Library", "Preferences") };
            string profileDir = System.IO.Path.GetDirectoryName(DD.Prototype.Profile.ProfileStore.Path);
            if (!string.IsNullOrEmpty(profileDir)) roots.Add(profileDir);
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string r = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                if (full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return "under a user data root (" + root + "): " + full;
            }

            string file = System.IO.Path.GetFileName(full).ToLowerInvariant();
            foreach (string bad in new[] { "profile", "settings", "prefs", ".plist" })
                if (file.Contains(bad)) return "protected name (" + bad + "): " + full;
            return null;
        }

        public static string Argument(string name)
        {
            string env = Environment.GetEnvironmentVariable(EnvName(name));
            if (!string.IsNullOrEmpty(env)) return env;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        // -ddS06PalmSourceSha → DD_S06_PALM_SOURCE_SHA
        private static string EnvName(string flag)
        {
            var sb = new StringBuilder("DD_");
            string core = flag.StartsWith("-dd", StringComparison.Ordinal) ? flag.Substring(3) : flag.TrimStart('-');
            for (int i = 0; i < core.Length; i++)
            {
                char ch = core[i];
                if (i > 0 && char.IsUpper(ch) && !char.IsUpper(core[i - 1])) sb.Append('_');
                sb.Append(char.ToUpperInvariant(ch));
            }

            return sb.ToString();
        }
        private static string Hierarchy(Transform t)
        {
            if (t == null) return null;
            string path = t.name; while (t.parent != null) { t = t.parent; path = t.name + "/" + path; } return path;
        }
        private static TransformId TransformOf(Transform t)
        {
            if (t == null) return null;
            var m = t.localToWorldMatrix; var inv = t.worldToLocalMatrix; var values = new float[16]; var inverseValues = new float[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) { values[r * 4 + c] = m[r, c]; inverseValues[r * 4 + c] = inv[r, c]; }
            return new TransformId { name = t.name, hierarchy = Hierarchy(t), localPosition = t.localPosition,
                worldPosition = t.position, localScale = t.localScale, lossyScale = t.lossyScale, localRotation = t.localRotation, worldRotation = t.rotation, localToWorldRowMajor = values, worldToLocalRowMajor = inverseValues, localToWorldDeterminant = m.determinant };
        }
        private static AssetId Identity(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            var id = new AssetId { name = obj.name };
#if UNITY_EDITOR
            id.path = UnityEditor.AssetDatabase.GetAssetPath(obj);
            if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long fileId)) { id.guid = guid; id.fileId = fileId.ToString(CultureInfo.InvariantCulture); }
            if (!string.IsNullOrEmpty(id.path))
            {
                id.dependencyHash = UnityEditor.AssetDatabase.GetAssetDependencyHash(id.path).ToString();
                if (System.IO.File.Exists(id.path))
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        id.fileSha256 = BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(id.path))).Replace("-", "").ToLowerInvariant();
            }
#endif
            return id;
        }
        private void LateUpdate()
        {
            if (Completed || Visual == null || string.IsNullOrEmpty(OutputPath)) return;
            var p = new Proof { observerSourceSha = Argument("-ddS06PalmSourceSha"), utc = DateTime.UtcNow.ToString("o"),
                frameCount = Time.frameCount, timeSeconds = Time.time, deltaTimeSeconds = Time.deltaTime,
                fpArmsEnabled = PlayerSkinnedVisual.FirstPersonV3Arms, tpSpecialDefaultEnabled = PlayerSkinnedVisual.TpV3On("sw_arc") };
            try { Observe(p); ImportedSurfaceObserved = p.actualLoadedLegacyIdentityKnown && p.surface != null; if (ImportedSurfaceObserved) p.status = "PARTIAL_IMPORTED_SURFACE_OBSERVED_ANATOMY_AND_SOURCE_MAPPING_UNKNOWN"; } catch (Exception e) { p.error = e.ToString(); p.status = "UNKNOWN_OBSERVER_ACCESS_OR_PLUMBING"; }
            string correspondencePath = Argument("-ddS06CorrespondenceProof");
            if (!string.IsNullOrEmpty(correspondencePath) && !System.IO.File.Exists(correspondencePath))
                try { WriteCorrespondence(correspondencePath, new CorrespondenceProof { observerSourceSha = p.observerSourceSha, utc = p.utc,
                    frameCount = p.frameCount, unityVersion = p.unityVersion, loadedModuleBoundary = p.loadedModuleBoundary, error = p.error ?? "Requested capture was not reached; no alternate sample or retake" }); }
                catch (Exception e) { p.error = (p.error ?? "") + "\nCorrespondence receipt write failed: " + e; }
            try
            {
                if (RefuseOutputPath(OutputPath) != null) { Debug.LogError("[S06-PalmProof] refused: " + RefuseOutputPath(OutputPath)); return; }
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutputPath));
                System.IO.File.WriteAllText(OutputPath, JsonUtility.ToJson(p, true));
                Debug.Log("[S06-PalmProof] frame=" + p.frameCount + " status=" + p.status + " surface=" + (p.surface != null ? p.surface.landmarkName : "UNKNOWN"));
            }
            finally { Completed = true; }
        }
        private void Observe(Proof p)
        {
            var an = Visual.Animator;
            p.unityVersion = Application.unityVersion;
            p.observedWorldSource = WorldSourceSettings.Resolve(out string worldScene).ToString(); p.observedWorldScene = worldScene;
            var wc = WeaponRegistry.ActiveController;
            if (wc == null || !wc.Classic.IsConfigured) throw new InvalidOperationException("Actual configured weapon controller unavailable");
            var actualDef = wc.Classic.Definition;
            p.weaponDefinition = Identity(actualDef); p.observedWeaponId = actualDef != null ? actualDef.weaponId : null;
            p.observedDisplayName = wc.Classic.DisplayName; p.observedFireMode = wc.Classic.FireMode.ToString(); p.specialWeaponActive = wc.SpecialWeaponActive;
            int layer = an.GetLayerIndex(C.OverrideUpperLayer);
            if (layer < 0 || an.avatar == null || !an.avatar.isValid || !an.avatar.isHuman) throw new InvalidOperationException("Actual Humanoid Avatar or override layer unavailable");
            var state = an.GetCurrentAnimatorStateInfo(layer);
            p.observedState = state.IsName(C.AimState + "_0") ? "Aim_0" : state.shortNameHash.ToString(CultureInfo.InvariantCulture);
            p.stateTransition = an.IsInTransition(layer); p.observedWeaponParameter = an.GetInteger(C.Weapon); p.observedWeaponAnimSet = Visual.WeaponAnimSet;
            p.avatar = Identity(an.avatar);
            p.avatarValid = an.avatar.isValid; p.avatarHuman = an.avatar.isHuman;
            p.playerPrefab = Identity(CharacterAssetSet.Load().PlayerPrefab);
            p.baseController = Identity(an.runtimeAnimatorController);
                        var library = ClassicWeaponVisualLibrary.Current; var entry = library != null ? library.Find(wc.Classic.Id) : null;
            if (entry == null || entry.model == null) throw new InvalidOperationException("Loaded classic library entry unavailable");
            int entryMatches = 0; foreach (var candidate in library.entries) if (candidate != null && candidate.id == wc.Classic.Id) entryMatches++;
            if (entryMatches != 1) throw new InvalidOperationException("Classic library identity is ambiguous");
            p.weaponLibraryModel = Identity(entry.model); p.libraryRosterKey = entry.rosterKey; p.libraryModelPackage = entry.modelPackage;
            p.classicModelSource = wc.ClassicModelSource.ToString();
            var skinIds = new List<AssetId>(); foreach (var sr in Visual.Renderers) if (sr != null && sr.sharedMesh != null && sr.name.EndsWith("_LOD0", StringComparison.Ordinal)) skinIds.Add(Identity(sr.sharedMesh));
            p.candidateLod0SkinMeshes = skinIds.ToArray();
            var ci = an.GetCurrentAnimatorClipInfo(layer); p.activeUpperClips = new ClipId[ci.Length];
            for (int i = 0; i < ci.Length; i++)
            {
                float seconds = (state.normalizedTime - Mathf.Floor(state.normalizedTime)) * ci[i].clip.length;
                p.activeUpperClips[i] = new ClipId { asset = Identity(ci[i].clip), weight = ci[i].weight, lengthSeconds = ci[i].clip.length,
                    animatorNormalizedTime = state.normalizedTime, observedLoopSeconds = seconds, observedFrameAt30fps = seconds * 30f };
            }
            var hd = an.avatar.humanDescription; p.avatarHumanMapping = new HumanMap[hd.human.Length];
            for (int i = 0; i < hd.human.Length; i++) { var h = hd.human[i]; p.avatarHumanMapping[i] = new HumanMap { humanName = h.humanName, boneName = h.boneName,
                useDefaultLimits = h.limit.useDefaultValues, minimum = h.limit.min, maximum = h.limit.max, center = h.limit.center, axisLength = h.limit.axisLength }; }
            p.avatarRestSkeleton = new RestBone[hd.skeleton.Length];
            for (int i = 0; i < hd.skeleton.Length; i++) { var b = hd.skeleton[i]; p.avatarRestSkeleton[i] = new RestBone { name = b.name, position = b.position, rotation = b.rotation, scale = b.scale }; }
            using (var handler = new HumanPoseHandler(an.avatar, an.transform))
            {
                var hp = new HumanPose(); handler.GetHumanPose(ref hp); var muscles = new List<MuscleScalar>();
                for (int i = 0; i < HumanTrait.MuscleName.Length; i++) if (HumanTrait.MuscleName[i].StartsWith("Right", StringComparison.Ordinal))
                    muscles.Add(new MuscleScalar { name = HumanTrait.MuscleName[i], normalizedValue = hp.muscles[i] });
                p.rightArmHandMuscles = muscles.ToArray();
            }
            var rig = PlayerRegistry.Primary;
            p.root = TransformOf(rig.transform); p.rightHandBoneReference = TransformOf(an.GetBoneTransform(HumanBodyBones.RightHand));
            p.middleProximalBoneReference = TransformOf(an.GetBoneTransform(HumanBodyBones.RightMiddleProximal));
            p.weaponMount = TransformOf(rig.WeaponMount); p.socket = TransformOf(rig.WeaponMount.parent);
            var align = rig.WeaponMount.GetComponent<WeaponMountAlignment>();
            if (align != null) { p.mountAligned = align.AlignedThisFrame; p.mountDeltaApplied = align.DeltaApplied; p.mountDeltaDegrees = align.DeltaDegrees; }
            p.leftGripWeight = Visual.GripIk.Weight; p.leftGripErrorFinite = !float.IsNaN(Visual.GripIk.LastError) && !float.IsInfinity(Visual.GripIk.LastError);
            if (p.leftGripErrorFinite) p.leftGripErrorMetres = Visual.GripIk.LastError;
            p.leftGripMarker = TransformOf(Visual.GripIk.Grip);
            Transform model = null;
            var ownedRenderers = new List<Renderer>(); wc.CollectWeaponRenderers(ownedRenderers);
            var ownedRigs = new HashSet<WeaponVisualRig>();
            foreach (var wr in ownedRenderers) if (wr != null) { var vr = wr.GetComponentInParent<WeaponVisualRig>(); if (vr != null) ownedRigs.Add(vr); }
            foreach (var vr in ownedRigs)
                if (vr.HasAuthoredModel && vr.Body != null && vr.Body != wc.SpecialRigBody && vr.Body.gameObject.activeInHierarchy)
                    foreach (Transform child in vr.Body) if (child.name == "Model" && child.gameObject.activeInHierarchy) { p.activeAuthoredClassicModelCount++; model = child; }
            if (p.activeAuthoredClassicModelCount != 1) throw new InvalidOperationException("Actual classic model is not unique");
            var actualMeshIds = new List<AssetId>(); bool match = true;
            var expectedMeshes = new HashSet<Mesh>();
            foreach (var f in entry.model.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) expectedMeshes.Add(f.sharedMesh);
            foreach (var f in model.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) { actualMeshIds.Add(Identity(f.sharedMesh)); match &= expectedMeshes.Contains(f.sharedMesh); }
            p.actualModelMeshes = actualMeshIds.ToArray(); p.actualModelMeshSourcesMatchLibrary = match && actualMeshIds.Count > 0;
            p.actualLoadedLegacyIdentityKnown = p.observedWeaponId == "ar_1" && wc.Classic.Id == ClassicWeaponId.AssaultRifle && entry.rosterKey == "ar_1"
                && wc.ClassicModelSource == WeaponController.ClassicModelKind.Authored && !p.specialWeaponActive && p.observedState == "Aim_0" && !p.stateTransition && p.observedWeaponParameter == 0
                && string.IsNullOrEmpty(p.observedWeaponAnimSet) && p.actualModelMeshSourcesMatchLibrary;
            if (!p.actualLoadedLegacyIdentityKnown) throw new InvalidOperationException("Actual loaded ar1 legacy pose/model identity does not match requested observation");
            p.actualModelRoot = TransformOf(model); p.actualModelFirstChild = model.childCount > 0 ? TransformOf(model.GetChild(0)) : null;
            Transform marker = null; int markerCount = 0;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == "grip_r") { marker = t; markerCount++; }
            if (markerCount == 0) foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == DD.Prototype.Core.Weapons.WeaponSocketContract.MarkerGrip) { marker = t; markerCount++; }
            if (markerCount != 1) throw new InvalidOperationException("Authored right grip marker unavailable or ambiguous");
            p.authoredGripMarker = TransformOf(marker); p.actualGripMarkerName = marker != null ? marker.name : null;
            var def = actualDef; p.authoredGripOffsetModelMetres = def.GripOffset;
            p.authoredGripOffsetWorldPoint = model.TransformPoint(def.GripOffset);
            if (marker == null) throw new InvalidOperationException("No actual authored right-grip marker; geometry target not guessed");
            p.observedGripWorldPoint = marker.position; p.socketToObservedGripMetres = Vector3.Distance(rig.WeaponMount.parent.position, marker.position);
            p.surface = ClosestActualSkin(Visual.Renderers, marker.position, p);
        }
        private static ModuleClock ModuleTime(string stage, string phase)
        {
            var clock = new ModuleClock { stage = stage, phase = phase };
            try
            {
                clock.utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                clock.stopwatchTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                clock.unityVersion = Application.unityVersion; clock.frameCount = Time.frameCount;
                clock.timeSeconds = Time.time; clock.deltaTimeSeconds = Time.deltaTime;
                clock.realtimeSinceStartupSeconds = Time.realtimeSinceStartup;
                clock.access = clock.frameCount >= 0 && !string.IsNullOrEmpty(clock.unityVersion)
                    && Finite(clock.timeSeconds) && Finite(clock.deltaTimeSeconds) && Finite(clock.realtimeSinceStartupSeconds)
                    ? "OBSERVED_UNITY_CLOCK" : "UNKNOWN_CLOCK_PRIMITIVE";
            }
            catch (Exception e) { clock.error = e.ToString(); clock.access = "UNKNOWN_CLOCK_ACCESS"; }
            return clock;
        }
        private static LoadedTypeModule ReadLoadedTypeModule(object instance, string role, string stage, string phase)
        {
            var result = new LoadedTypeModule { role = role, reflectionBegin = ModuleTime(stage, phase + "/reflection-begin") };
            try
            {
                if (instance == null) throw new InvalidOperationException("Actual instance unavailable; no declared-type or disk fallback");
                var type = instance.GetType(); var module = type.Module; var assembly = type.Assembly;
                result.actualTypeFullName = type.FullName; result.assemblyFullName = assembly.FullName; result.moduleName = module.Name;
                try
                {
                    var value = module.ModuleVersionId;
                    if (value != Guid.Empty) { result.mvid = value.ToString("D"); result.mvidAccess = "OBSERVED_ACTUAL_TYPE_MODULE_REFLECTION"; }
                }
                catch (Exception e) { result.error = "MVID unavailable: " + e; }
                try
                {
                    result.location = assembly.Location;
                    if (!string.IsNullOrEmpty(result.location)) result.locationAccess = "OBSERVED_ACTUAL_ASSEMBLY_LOCATION";
                }
                catch (Exception e) { result.error = (result.error ?? "") + "\nLocation unavailable: " + e; }
            }
            catch (Exception e) { result.error = (result.error ?? "") + "\nLoaded primitive unavailable: " + e; }
            finally { result.reflectionEnd = ModuleTime(stage, phase + "/reflection-end"); }
            if (!string.IsNullOrEmpty(result.location))
            {
                var disk = result.onDiskIdentity; disk.path = result.location;
                disk.readBegin = ModuleTime(stage, phase + "/location-file-read-begin");
                try
                {
                    if (!System.IO.Path.IsPathRooted(result.location)) throw new InvalidOperationException("Actual Assembly.Location is not absolute");
                    var bytes = System.IO.File.ReadAllBytes(result.location); disk.bytesRead = bytes.LongLength;
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        disk.fileSha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    disk.access = "OBSERVED_LOCATION_FILE_BYTES_NOT_LOADED_IMAGE";
                }
                catch (Exception e) { disk.error = e.ToString(); disk.access = "UNKNOWN_LOCATION_FILE_READ"; }
                finally { disk.readEnd = ModuleTime(stage, phase + "/location-file-read-end"); }
            }
            return result;
        }
        private Surface ClosestActualSkin(SkinnedMeshRenderer[] renderers, Vector3 grip, Proof observation)
        {
            Surface best = null; float bestD2 = float.PositiveInfinity; int ties = 0;
            string correspondencePath = Argument("-ddS06CorrespondenceProof");
            int bodyMatches = 0; bool correspondenceWritten = false;
            if (!string.IsNullOrEmpty(correspondencePath))
                foreach (var r in renderers) if (r != null && r.sharedMesh != null && IsExactBody(Identity(r.sharedMesh))) bodyMatches++;
            foreach (var r in renderers)
            {
                if (r == null || r.sharedMesh == null || !r.name.EndsWith("_LOD0", StringComparison.Ordinal)) continue;
                var baked = new Mesh { name = "S06_TransientActualSkin" };
                try
                {
                    LoadedModuleBoundary modules = null;
                    if (!string.IsNullOrEmpty(correspondencePath) && bodyMatches == 1 && IsExactBody(Identity(r.sharedMesh)))
                    {
                        modules = observation.loadedModuleBoundary;
                        modules.observerSourceClaim = observation.observerSourceSha;
                        modules.access = "UNKNOWN_STARTED_BEFORE_EXISTING_BODY_BAKE";
                        modules.observerBeforeBake = ReadLoadedTypeModule(this, "observer", observation.stage, "before-existing-body-bake");
                        modules.playerRuntimeBeforeBake = ReadLoadedTypeModule(Visual, "actual-player-runtime", observation.stage, "before-existing-body-bake");
                        modules.beforeBake = ModuleTime(observation.stage, "immediately-before-existing-body-bake");
                    }
                    r.BakeMesh(baked, false);
                    if (modules != null)
                    {
                        modules.afterBake = ModuleTime(observation.stage, "immediately-after-existing-body-bake");
                        modules.observerAfterBake = ReadLoadedTypeModule(this, "observer", observation.stage, "after-existing-body-bake");
                        modules.playerRuntimeAfterBake = ReadLoadedTypeModule(Visual, "actual-player-runtime", observation.stage, "after-existing-body-bake");
                        modules.access = "REFLECTION_ATTEMPTED_PRIMITIVE_AVAILABILITY_EXPLICIT_ACCEPTANCE_UNKNOWN";
                    }
                    var verts = baked.vertices; string bakedSha = SnapshotHash(baked, verts); int globalTriangle = 0;
                    if (!string.IsNullOrEmpty(correspondencePath) && bodyMatches == 1 && IsExactBody(Identity(r.sharedMesh)))
                    {
                        var captured = CaptureCorrespondence(r, baked, bakedSha, grip, observation, bodyMatches);
                        captured.loadedModuleBoundary = observation.loadedModuleBoundary;
                        WriteCorrespondence(correspondencePath, captured);
                        correspondenceWritten = true;
                    }
                    for (int sub = 0; sub < baked.subMeshCount; sub++)
                    {
                        var indices = baked.GetTriangles(sub);
                        for (int tri = 0; tri < indices.Length; tri += 3, globalTriangle++)
                        {
                            int ia = indices[tri], ib = indices[tri + 1], ic = indices[tri + 2];
                            var a = r.transform.TransformPoint(verts[ia]); var b = r.transform.TransformPoint(verts[ib]); var c = r.transform.TransformPoint(verts[ic]);
                            if (Vector3.Cross(b - a, c - a).sqrMagnitude == 0f) continue;
                            Vector3 bary; Vector3 q = ClosestPoint(grip, a, b, c, out bary); float d2 = (q - grip).sqrMagnitude;
                            if (float.IsNaN(d2) || float.IsInfinity(d2)) throw new InvalidOperationException("Non-finite baked geometry distance");
                            if (d2 == bestD2) { ties++; continue; } if (d2 > bestD2) continue;
                            bestD2 = d2; ties = 1;
                            var bones = new TransformId[r.bones.Length]; for (int k = 0; k < bones.Length; k++) bones[k] = TransformOf(r.bones[k]);
                            best = new Surface { bakedSnapshotSha256 = bakedSha, mesh = Identity(r.sharedMesh), renderer = TransformOf(r.transform), bakedVertexCount = verts.Length,
                                importedVertexCount = r.sharedMesh.vertexCount, triangleOrdinal = globalTriangle, submeshIndex = sub, submeshTriangleOrdinal = tri / 3,
                                orderedBakedVertexIndices = new[] { ia, ib, ic }, rendererEnabled = r.enabled, rendererActiveInHierarchy = r.gameObject.activeInHierarchy, rendererIsVisible = r.isVisible, orderedBakedRendererLocalVertices = new[] { verts[ia], verts[ib], verts[ic] },
                                orderedBakedWorldVertices = new[] { a, b, c }, barycentric = bary, rendererLocalPoint = bary.x * verts[ia] + bary.y * verts[ib] + bary.z * verts[ic],
                                worldPoint = q, worldWindingNormal = Vector3.Cross(b - a, c - a).normalized, gripMinusSurfaceWorld = grip - q,
                                distanceToAuthoredGripMetres = Mathf.Sqrt(d2), originalMeshReadable = r.sharedMesh.isReadable.ToString(),
                                restWeightsAccess = "No readability/importer mutation; source CP/skin-region identity remains UNKNOWN", boundBones = bones };
                        }
                    }
                }
                finally { UnityEngine.Object.Destroy(baked); }
            }
            if (!string.IsNullOrEmpty(correspondencePath) && !correspondenceWritten)
                WriteCorrespondence(correspondencePath, new CorrespondenceProof { observerSourceSha = observation.observerSourceSha,
                    utc = observation.utc, frameCount = observation.frameCount, exactBodyMeshMatches = bodyMatches, loadedModuleBoundary = observation.loadedModuleBoundary,
                    error = "Exact hash/GUID/localID body renderer is absent or not unique; no alternate mesh or retake" });
            if (best != null) best.exactFloatTies = ties;
            return best;
        }
        // M-50: the body file's GUID is read at run time (mirrors ArtCharacterContract.PlayerAvatarSource — the editor assembly is not
        // referenced here); the LOD0 mesh's local id and the file hash stay pinned: the correspondence holds for that exact mesh only.
        private const string PlayerBodyPath = "Assets/_Art/Characters/Player/player_base.fbx";
        private static bool IsExactBody(AssetId mesh)
        {
            string guid = null;
#if UNITY_EDITOR
            guid = UnityEditor.AssetDatabase.AssetPathToGUID(PlayerBodyPath);
#endif
            return mesh != null && !string.IsNullOrEmpty(guid) && mesh.guid == guid && mesh.path == PlayerBodyPath && mesh.fileId == "-1683454014250015237"
                && mesh.fileSha256 == "26cff12942a4b99714aa4400f898b2fccbc8ae20249c92a5af2a03b908696d15";
        }
        private static void WriteCorrespondence(string path, CorrespondenceProof data)
        {
            string refused = RefuseOutputPath(path);
            if (refused != null) throw new InvalidOperationException("correspondence output refused: " + refused);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(data, false));
        }
        private static CorrespondenceProof CaptureCorrespondence(SkinnedMeshRenderer r, Mesh baked, string bakedSha, Vector3 grip, Proof p, int bodyMatches)
        {
            var proof = new CorrespondenceProof { observerSourceSha = p.observerSourceSha, utc = p.utc, unityVersion = p.unityVersion,
                frameCount = p.frameCount, timeSeconds = p.timeSeconds, deltaTimeSeconds = p.deltaTimeSeconds, exactBodyMeshMatches = bodyMatches,
                importedMesh = Identity(r.sharedMesh), avatar = p.avatar, renderer = TransformOf(r.transform), gripWorld = grip,
                avatarValid = p.avatarValid, avatarHuman = p.avatarHuman,
                observedState = p.observedState, observedWeaponId = p.observedWeaponId, observedWeaponParameter = p.observedWeaponParameter, stage = p.stage,
                activeUpperClips = p.activeUpperClips, originalIsReadable = r.sharedMesh.isReadable,
                rendererEnabled = r.enabled, rendererActive = r.gameObject.activeInHierarchy, rendererVisible = r.isVisible,
                rendererQuality = r.quality.ToString(), qualitySkinWeights = QualitySettings.skinWeights.ToString(), bakedSnapshotSha256 = bakedSha };
            proof.importedRest = ReadMeshArrays(r.sharedMesh, "Imported mesh-local rest; source basis UNKNOWN until independently matched");
            proof.bakedRendererLocal = ReadMeshArrays(baked, "Actual CPU posed renderer-local from same BakeMesh(false) instance");
            proof.effectiveImporter = ReadEffectiveImporter(proof.importedMesh);
            try
            {
                var bones = r.bones; proof.rendererBones = new TransformId[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null) throw new InvalidOperationException("Null bound bone");
                    proof.rendererBones[i] = TransformOf(bones[i]);
                }
                var bindposes = r.sharedMesh.bindposes; proof.importedBindposes = new MatrixArray[bindposes.Length];
                for (int i = 0; i < bindposes.Length; i++)
                {
                    var values = new float[16]; for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++)
                    {
                        float value = bindposes[i][row, col]; if (!Finite(value)) throw new InvalidOperationException("Non-finite bindpose");
                        values[4 * row + col] = value;
                    }
                    proof.importedBindposes[i] = new MatrixArray { rowMajor = values };
                }
                // Borrowed mesh storage: copy immediately; do not dispose or retain these views.
                var counts = r.sharedMesh.GetBonesPerVertex(); var weights = r.sharedMesh.GetAllBoneWeights();
                proof.bonesPerVertex = new int[counts.Length]; for (int i = 0; i < counts.Length; i++) proof.bonesPerVertex[i] = counts[i];
                proof.allBoneWeights = new SkinInfluence[weights.Length]; int expectedWeights = 0;
                foreach (int count in proof.bonesPerVertex) expectedWeights += count;
                if (counts.Length != r.sharedMesh.vertexCount || expectedWeights != weights.Length || bindposes.Length != bones.Length)
                    throw new InvalidOperationException("Stored skin slices/counts/bindpose/bone count mismatch");
                for (int i = 0; i < weights.Length; i++)
                {
                    var w = weights[i];
                    if (w.boneIndex < 0 || w.boneIndex >= bones.Length || !Finite(w.weight) || w.weight < 0f)
                        throw new InvalidOperationException("Stored skin influence invalid/non-finite");
                    proof.allBoneWeights[i] = new SkinInfluence { boneIndex = w.boneIndex, weight = w.weight,
                        weightFloat32Bits = BitConverter.ToInt32(BitConverter.GetBytes(w.weight), 0) };
                }
                proof.blendshapeNames = new string[r.sharedMesh.blendShapeCount]; proof.blendshapeWeights = new float[r.sharedMesh.blendShapeCount];
                for (int i = 0; i < proof.blendshapeNames.Length; i++)
                {
                    proof.blendshapeNames[i] = r.sharedMesh.GetBlendShapeName(i); proof.blendshapeWeights[i] = r.GetBlendShapeWeight(i);
                    if (!Finite(proof.blendshapeWeights[i])) throw new InvalidOperationException("Non-finite blendshape weight");
                }
                proof.skinAccess = "COPIED_STORED_WEIGHTS_BINDPOSES_BONE_ORDER_NO_MUTATION";
            }
            catch (Exception e) { proof.skinAccess = "UNKNOWN_SKIN_ACCESS_OR_CONTRACT"; proof.skinError = e.ToString(); }
            return proof;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static EffectiveImporter ReadEffectiveImporter(AssetId mesh)
        {
            var result = new EffectiveImporter();
#if UNITY_EDITOR
            try
            {
                var importer = UnityEditor.AssetImporter.GetAtPath(mesh.path) as UnityEditor.ModelImporter;
                if (importer == null) throw new InvalidOperationException("Actual body ModelImporter unavailable");
                result.animationType = importer.animationType.ToString(); result.avatarSetup = importer.avatarSetup.ToString();
                var sourceAvatar = Identity(importer.sourceAvatar);
                result.sourceAvatarReference = sourceAvatar == null ? "NOT_REFERENCED" : JsonUtility.ToJson(sourceAvatar);
                result.isReadable = importer.isReadable; result.bakeAxisConversion = importer.bakeAxisConversion;
                result.useFileUnits = importer.useFileUnits; result.globalScale = importer.globalScale;
                result.weldVertices = importer.weldVertices; result.keepQuads = importer.keepQuads;
                result.optimizeMeshPolygons = importer.optimizeMeshPolygons; result.optimizeMeshVertices = importer.optimizeMeshVertices;
                result.optimizeBones = importer.optimizeBones; result.optimizeGameObjects = importer.optimizeGameObjects;
                result.skinWeights = importer.skinWeights.ToString(); result.maxBonesPerVertex = importer.maxBonesPerVertex;
                result.customSkinPruningActive = result.skinWeights == "Custom";
                result.minBoneWeight = importer.minBoneWeight; result.normalMode = importer.importNormals.ToString(); result.tangentMode = importer.importTangents.ToString();
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    result.metaSha256 = BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(mesh.path + ".meta"))).Replace("-", "").ToLowerInvariant();
                result.access = "OBSERVED_EFFECTIVE_IMPORTER_GETTERS_NO_MUTATION";
            }
            catch (Exception e) { result.access = "UNKNOWN_EFFECTIVE_IMPORTER_ACCESS"; result.error = e.ToString(); }
#else
            result.access = "UNKNOWN_EDITOR_IMPORTER_GETTERS_REQUIRED";
#endif
            return result;
        }
        private static MeshArrays ReadMeshArrays(Mesh mesh, string domain)
        {
            var result = new MeshArrays { domain = domain, vertexCount = mesh.vertexCount };
#if UNITY_EDITOR
            try
            {
                // Editor snapshot bypasses isReadable without changing importer/asset flags.
                using (var snapshot = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var data = snapshot[0]; result.vertexCount = data.vertexCount; result.indexFormat = data.indexFormat.ToString();
                    var layout = new List<VertexLayout>();
                    foreach (UnityEngine.Rendering.VertexAttribute attribute in Enum.GetValues(typeof(UnityEngine.Rendering.VertexAttribute)))
                        if (data.HasVertexAttribute(attribute))
                        {
                            int stream = data.GetVertexAttributeStream(attribute);
                            layout.Add(new VertexLayout { attribute = attribute.ToString(), format = data.GetVertexAttributeFormat(attribute).ToString(),
                                dimension = data.GetVertexAttributeDimension(attribute), stream = stream, offset = data.GetVertexAttributeOffset(attribute), stride = data.GetVertexBufferStride(stream) });
                        }
                    result.layout = layout.ToArray();
                    using (var values = new Unity.Collections.NativeArray<Vector3>(data.vertexCount, Unity.Collections.Allocator.Temp))
                    {
                        data.GetVertices(values); result.positions = values.ToArray();
                        foreach (var v in result.positions) if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) throw new InvalidOperationException("Non-finite mesh position");
                        result.positionsFloat32LittleEndianBase64 = PositionBytes(result.positions);
                        if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal))
                        {
                            data.GetNormals(values); result.normals = values.ToArray();
                            foreach (var v in result.normals) if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) throw new InvalidOperationException("Non-finite mesh normal");
                        }
                    }
                    using (var values = new Unity.Collections.NativeArray<Vector4>(data.vertexCount, Unity.Collections.Allocator.Temp))
                    {
                        if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent))
                        {
                            data.GetTangents(values); result.tangents = values.ToArray();
                            foreach (var v in result.tangents) if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z) || !Finite(v.w)) throw new InvalidOperationException("Non-finite mesh tangent");
                        }
                        var uvs = new List<UvArray>();
                        for (int channel = 0; channel < 8; channel++)
                        {
                            var attr = (UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel);
                            if (!data.HasVertexAttribute(attr)) continue;
                            int dimension = data.GetVertexAttributeDimension(attr);
                            if (dimension < 1 || dimension > 4) throw new InvalidOperationException("Unsupported UV dimension");
                            data.GetUVs(channel, values);
                            var copied = values.ToArray();
                            foreach (var v in copied) if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z) || !Finite(v.w)) throw new InvalidOperationException("Non-finite mesh UV");
                            uvs.Add(new UvArray { channel = channel, dimension = dimension, values = copied });
                        }
                        result.uvChannels = uvs.ToArray();
                    }
                    if (data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                        using (var values = new Unity.Collections.NativeArray<Color>(data.vertexCount, Unity.Collections.Allocator.Temp))
                        {
                            data.GetColors(values); result.colors = values.ToArray();
                            foreach (var v in result.colors) if (!Finite(v.r) || !Finite(v.g) || !Finite(v.b) || !Finite(v.a)) throw new InvalidOperationException("Non-finite mesh color");
                        }
                    result.submeshes = new SubmeshArray[data.subMeshCount];
                    for (int sub = 0; sub < data.subMeshCount; sub++)
                    {
                        var descriptor = data.GetSubMesh(sub);
                        using (var indices = new Unity.Collections.NativeArray<int>(descriptor.indexCount, Unity.Collections.Allocator.Temp))
                        {
                            data.GetIndices(indices, sub, true); var canonical = indices.ToArray();
                            foreach (int index in canonical) if (index < 0 || index >= data.vertexCount) throw new InvalidOperationException("Mesh index out of range");
                            result.submeshes[sub] = new SubmeshArray { topology = descriptor.topology.ToString(), indexStart = descriptor.indexStart,
                                indexCount = descriptor.indexCount, baseVertex = descriptor.baseVertex, firstVertex = descriptor.firstVertex,
                                vertexCount = descriptor.vertexCount, canonicalIndices = canonical };
                        }
                    }
                    result.access = "COPIED_READ_ONLY_MESH_DATA_NO_IMPORTER_MUTATION";
                }
            }
            catch (Exception e) { result.access = "UNKNOWN_MESH_DATA_ACCESS"; result.error = e.ToString(); }
#else
            result.access = "UNKNOWN_EDITOR_MESHUTILITY_REQUIRED_NO_READABILITY_CHANGE";
#endif
            return result;
        }
        private static string PositionBytes(Vector3[] positions)
        {
            using (var bytes = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(bytes))
            {
                foreach (var v in positions) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                writer.Flush(); return Convert.ToBase64String(bytes.ToArray());
            }
        }
        private static string SnapshotHash(Mesh baked, Vector3[] verts)
        {
            using (var memory = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(memory))
            {
                writer.Write(verts.Length); foreach (var v in verts) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                writer.Write(baked.subMeshCount);
                for (int sub = 0; sub < baked.subMeshCount; sub++) { var triangles = baked.GetTriangles(sub); writer.Write(triangles.Length); foreach (int index in triangles) writer.Write(index); }
                writer.Flush(); using (var sha = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(memory.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
        private static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a; float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { bary = new Vector3(1, 0, 0); return a; }
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { bary = new Vector3(0, 1, 0); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); bary = new Vector3(1 - v, v, 0); return a + v * ab; }
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { bary = new Vector3(0, 0, 1); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); bary = new Vector3(1 - w, 0, w); return a + w * ac; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); bary = new Vector3(0, 1 - w, w); return b + w * (c - b); }
            float inv = 1f / (va + vb + vc), v2 = vb * inv, w2 = vc * inv;
            bary = new Vector3(1 - v2 - w2, v2, w2); return a + ab * v2 + ac * w2;
        }
    }

    internal static class ArtAnimatorLayers
    {
        public static int Index(Animator animator, string layerName)
        {
            for (int i = 0; i < animator.layerCount; i++) if (animator.GetLayerName(i) == layerName) return i;
            return -1;
        }
    }
}
