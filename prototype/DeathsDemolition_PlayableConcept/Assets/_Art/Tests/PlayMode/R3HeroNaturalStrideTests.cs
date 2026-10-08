#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;
using Object = UnityEngine.Object;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// R3 hero diagnosis: 24 pure ring children on the actual Animator at LocoRate 1, fixed body, foot IK off.
    /// The mean backward foot velocity over complete foot_x -> foot_x_off windows is the natural-cadence speed.
    /// u_raw = u_natural / childScale; S_hero = u_natural * (clip.length / childScale) = u_raw * clip.length.
    /// No signed-slip fit, no motor/performance gate, no claim that locomotion acceptance thresholds passed.
    /// Source/output paths are explicit; an existing output directory is never reused.
    /// </summary>
    [TestFixture]
    public sealed class R3HeroNaturalStrideTests
    {
        public const string TestName = "DD.Art.Tests.PlayMode.R3HeroNaturalStrideTests.Natural24_ActualAnimator_NoIk_PhaseStride";
        public const string OutputEnv = "DD_NATURAL24_OUT", CommitEnv = "DD_NATURAL24_COMMIT";
        internal const int Fps = 60, RequiredCycles = 6;
        internal const float VerticalLimit = 0.15f;
        private readonly List<GameObject> _temporary = new List<GameObject>();
        private float _captureBefore;
        private int _forceBefore;
        private bool _ignoreBefore;
        private Natural24Recorder _recorder;

        [SetUp]
        public void SetUp()
        {
            _captureBefore = Time.captureDeltaTime;
            _forceBefore = FootIkAdapter.Force;
            _ignoreBefore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            CharacterAssetSet.ResetCache();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_recorder != null) _recorder.Stop();
            Time.captureDeltaTime = _captureBefore;
            FootIkAdapter.Force = _forceBefore;
            foreach (GameObject go in _temporary) if (go != null) Object.Destroy(go);
            _temporary.Clear();
            CharacterAssetSet.ResetCache();
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            LogAssert.ignoreFailingMessages = _ignoreBefore;
            yield return null;
        }

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator Natural24_ActualAnimator_NoIk_PhaseStride()
        {
            string output = Environment.GetEnvironmentVariable(OutputEnv);
            Assert.IsFalse(string.IsNullOrEmpty(output), OutputEnv + " gerekli");
            Assert.IsTrue(Path.IsPathRooted(output), "çıktı yolu mutlak olmalı");
            output = Path.GetFullPath(output);
            Assert.IsFalse(Directory.Exists(output) || File.Exists(output), "yeni ve kullanılmamış çıktı dizini gerekli");
            string commit = Environment.GetEnvironmentVariable(CommitEnv);
            Assert.IsTrue(IsSha(commit), CommitEnv + " tam 40-hex commit olmalı");
            commit = commit.ToLowerInvariant();
            string actualHead = ReadHead(Path.GetDirectoryName(Application.dataPath));
            Assert.AreEqual(commit, actualHead, "istenen commit gerçek worktree HEAD ile aynı olmalı");
            Directory.CreateDirectory(output);
            var proof = new Natural24Proof { utc = DateTime.UtcNow.ToString("o"), sourceCommit = actualHead,
                unityVersion = Application.unityVersion, fps = Fps, cyclesPerFootRequired = RequiredCycles,
                verticalFilterMps = VerticalLimit, measurement = "ACTUAL_ANIMATOR_FIXED_ROOT_RATE1_PHASE_ALL_NO_FOOT_IK",
                units = "uNatural: hero m/s at child time scale; uRaw: hero m/s at delivered clip rate 1; stride: hero m/cycle",
                acceptance = "DIAGNOSTIC_ONLY_NO_L1_L1T_L2_L3_OR_PERFORMANCE_PASS_CLAIM" };
            StreamWriter csv = null;
            try
            {
                csv = new StreamWriter(new FileStream(Path.Combine(output, "natural24-frames.csv"), FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false));
                csv.WriteLine("clip,frame,phase,normalizedTime,dt,actualRate,stateSpeed,stateMultiplier,animatorSpeed,expectedClipWeight,ikForce,ikServed,ikWeight,locked,bodyVxz,footUL,footUR,footVyL,footVyR,phaseL,phaseR,windowEligibleL,windowEligibleR");
                SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
                yield return null;
                yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, 60f, "MainMenu");
                yield return null;
                CameraModeService.Request(CameraMode.TopDown, instant: true);
                Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
                CharacterAssetSet assets = CharacterAssetSet.Load();
                Assert.IsNotNull(assets);
                Assert.IsNotNull(assets.PlayerPrefab);
                var stage = new GameObject("R3_Natural24_FixedRoot");
                _temporary.Add(stage);
                Transform visual = new GameObject("Visual").transform;
                visual.SetParent(stage.transform, false);
                var body = Object.Instantiate(assets.PlayerPrefab, visual, false).GetComponent<PlayerSkinnedVisual>();
                Assert.IsNotNull(body);
                body.Attach(visual, () => Vector3.zero, () => 6.2f, () => Vector3.forward);
                Assert.IsTrue(body.Authored && body.Animator != null && body.Animator.isHuman);
                Assert.IsNotNull(body.Events);
                Assert.IsNotNull(body.FootIk);
                body.enabled = false;
                Animator animator = body.Animator;
                animator.applyRootMotion = false;
                animator.speed = 1f;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                FootIkAdapter.Force = 0;
                Time.captureDeltaTime = 1f / Fps;
                AnimatorController controller = BaseController(animator.runtimeAnimatorController);
                AnimatorState loco = FindState(controller.layers[0].stateMachine, C.LocomotionState);
                Assert.IsNotNull(loco, "base Locomotion state yok");
                int locoHash = Animator.StringToHash(loco.name);
                Assert.AreEqual(1f, loco.speed, 1e-6f);
                Assert.AreEqual(0f, loco.cycleOffset, 1e-6f);
                Assert.IsFalse(loco.cycleOffsetParameterActive || loco.mirror || loco.mirrorParameterActive || loco.timeParameterActive,
                    "phase penceresi için kaydırmasız, aynasız ve doğal state clock gerekli");
                Assert.IsTrue(loco.speedParameterActive && loco.speedParameter == C.LocoRate);
                var rootTree = loco.motion as BlendTree;
                Assert.IsNotNull(rootTree);
                Assert.AreEqual(BlendTreeType.Simple1D, rootTree.blendType);
                Assert.AreEqual(C.LocoSpeed, rootTree.blendParameter);
                Assert.AreEqual(4, rootTree.children.Length);
                CharacterAnimationSet set = body.AnimationSet;
                Assert.IsTrue(set.Locomotion.HasRings);
                Assert.AreEqual(24, set.Locomotion.loops.Count);
                proof.avatar = Pin(animator.avatar);
                proof.controller = Pin(controller);
                proof.runtimeControllerName = animator.runtimeAnimatorController.name;
                proof.runtimeControllerType = animator.runtimeAnimatorController.GetType().FullName;
                // Attach creates an in-memory AOC. Pin its persisted base and the override pairs, not a nonexistent AOC file.
                var aoc = animator.runtimeAnimatorController as AnimatorOverrideController;
                if (aoc != null) {
                    var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(aoc.overridesCount);
                    aoc.GetOverrides(pairs);
                    foreach (var pair in pairs) if (pair.Value != null && pair.Value != pair.Key)
                        proof.runtimeOverrides.Add(new Natural24Override { original = Pin(pair.Key), replacement = Pin(pair.Value) });
                }
                proof.animationSet = Pin(set);
                proof.prefab = Pin(assets.PlayerPrefab);
                proof.sources = new[] {
                    PinPath("Assets/_Art/Tests/PlayMode/R3HeroNaturalStrideTests.cs"),
                    PinPath("Assets/_Prototype/Characters/PlayerSkinnedVisual.cs"),
                    PinPath("Assets/_Prototype/Characters/CharacterAnimationSet.cs"),
                    PinPath("Assets/_Prototype/Characters/FootIkAdapter.cs"),
                    PinPath("Assets/_Prototype/Characters/StrideCalibrator.cs"),
                    PinPath("Assets/_Prototype/Characters/CharacterAnimationEvents.cs"),
                    PinPath("Assets/_Prototype/Characters/LocomotionV4.cs"),
                    PinPath("Assets/_Art/Editor/ArtAnimatorBuilder.cs"),
                    PinPath("Assets/_Art/Characters/Animations/AP026-loco-manifest.json") };
                proof.layerNames = new string[animator.layerCount];
                proof.layerWeights = new float[animator.layerCount];
                for (int layer = 0; layer < animator.layerCount; layer++) {
                    proof.layerNames[layer] = animator.GetLayerName(layer);
                    proof.layerWeights[layer] = animator.GetLayerWeight(layer);
                }
                _recorder = stage.AddComponent<Natural24Recorder>();
                var seen = new HashSet<int>();
                foreach (CharacterAnimationSet.LocoLoop loop in set.Locomotion.loops)
                {
                    Assert.IsTrue(loop.ringSlot >= 1 && loop.ringSlot <= 3 && loop.dirIndex >= 0 && loop.dirIndex < 8);
                    Assert.IsTrue(seen.Add((loop.ringSlot - 1) * 8 + loop.dirIndex), "çift loop slotu");
                    CharacterAnimationSet.Entry entry = set.Find(loop.clip);
                    Assert.IsNotNull(entry, loop.clip);
                    Assert.IsNotNull(entry.clip, loop.clip);
                    ChildMotion ringChild = rootTree.children[loop.ringSlot];
                    var ring = ringChild.motion as BlendTree;
                    Assert.IsNotNull(ring, loop.clip);
                    Assert.AreEqual(BlendTreeType.SimpleDirectional2D, ring.blendType);
                    Assert.AreEqual(C.MoveX, ring.blendParameter);
                    Assert.AreEqual(C.MoveY, ring.blendParameterY);
                    ChildMotion child = FindChild(ring, entry.clip);
                    Assert.Greater(child.timeScale, 0f);
                    Assert.AreEqual(0f, child.cycleOffset, 1e-6f);
                    Assert.AreEqual(0f, ringChild.cycleOffset, 1e-6f);
                    Assert.IsFalse(child.mirror || ringChild.mirror, loop.clip + " actual child mirrored");
                    Assert.AreEqual(1f, ringChild.timeScale, 1e-6f, "ek outer-ring timeScale bu ölçüm sözleşmesinde yok");
                    float cycle = entry.clip.length / child.timeScale;
                    Assert.AreEqual(loop.cycleSecondsNatural, cycle, 0.002f, loop.clip + " actual doğal döngü süresi");
                    Assert.AreEqual(set.Locomotion.ringThresholdMps[loop.ringSlot], ringChild.threshold, 1e-5f);
                    Assert.AreEqual(1f, child.position.magnitude, 0.001f, loop.clip + " unit direction");
                    Vector2 direction = child.position.normalized;
                    Vector3 localDirection = new Vector3(direction.x, 0f, direction.y);
                    Vector3 worldDirection = animator.transform.TransformDirection(localDirection);
                    worldDirection.y = 0f;
                    Assert.Greater(worldDirection.sqrMagnitude, 1e-6f, loop.clip + " yatay world travel direction yok");
                    worldDirection.Normalize();
                    var row = new Natural24Row { clipName = loop.clip, slot = loop.ringSlot, directionIndex = loop.dirIndex,
                        localDirection = localDirection, direction = worldDirection, animatorRootRotation = animator.transform.rotation,
                        thresholdMps = ringChild.threshold,
                        childScale = child.timeScale, clipSeconds = entry.clip.length, expectedCycleSeconds = cycle,
                        storedUnityRawMps = entry.unitySpeedMps, templateStrideM = loop.strideCycleMTemplate,
                        globalK = set.Locomotion.heroStrideScale, clip = Pin(entry.clip) };
                    proof.rows.Add(row);
                    Action hold = () => {
                        animator.SetBool(C.Alive, true);
                        animator.SetBool(C.Still, false);
                        animator.SetInteger(LocomotionV4.ClipParam, LocomotionV4.None);
                        animator.SetFloat(LocomotionV4.TimeParam, 0f);
                        animator.SetFloat(C.LocoSpeed, ringChild.threshold);
                        animator.SetFloat(C.MoveX, direction.x);
                        animator.SetFloat(C.MoveY, direction.y);
                        animator.SetFloat(C.LocoRate, 1f);
                    };
                    hold();
                    animator.Play(locoHash, 0, 0f);
                    animator.Update(0f);   // setup through the real Animator; no SampleAnimation or synthetic foot events
                    int warmup = Mathf.Max(Fps, Mathf.CeilToInt(cycle * Fps) + 2);
                    for (int frame = 0; frame < warmup; frame++) { hold(); yield return null; }
                    _recorder.Begin(animator, body.FootIk, body.Events, row, entry.clip, csv, stage.transform.position, locoHash);
                    try
                    {
                        int cap = Mathf.CeilToInt(cycle * Fps * (RequiredCycles + 3)) + Fps;
                        for (int frame = 0; frame < cap && !_recorder.Enough; frame++) { hold(); yield return null; }
                    }
                    finally { _recorder.Stop(); }
                    Assert.AreEqual(0, row.invalidFrames, loop.clip + " ölçüm izolasyonu: " + row.firstViolation);
                    Assert.GreaterOrEqual(row.left.completeWindows, RequiredCycles, loop.clip + " sol tam basış");
                    Assert.GreaterOrEqual(row.right.completeWindows, RequiredCycles, loop.clip + " sağ tam basış");
                    Assert.GreaterOrEqual(row.left.cycleIntervals, RequiredCycles, loop.clip + " sol tam döngü aralığı");
                    Assert.GreaterOrEqual(row.right.cycleIntervals, RequiredCycles, loop.clip + " sağ tam döngü aralığı");
                    Assert.GreaterOrEqual(row.left.plantEvents, RequiredCycles, loop.clip + " gerçek foot_l olayları");
                    Assert.GreaterOrEqual(row.right.plantEvents, RequiredCycles, loop.clip + " gerçek foot_r olayları");
                    Assert.GreaterOrEqual(row.left.offEvents, RequiredCycles, loop.clip + " gerçek foot_l_off olayları");
                    Assert.GreaterOrEqual(row.right.offEvents, RequiredCycles, loop.clip + " gerçek foot_r_off olayları");
                    Assert.IsTrue(row.left.hasMean && row.right.hasMean && Finite(row.naturalMps) && row.naturalMps > 0f, loop.clip + " hız ölçülemedi");
                    Assert.AreEqual(cycle, row.left.observedCycleSeconds, 2f / Fps, loop.clip + " sol actual cycle");
                    Assert.AreEqual(cycle, row.right.observedCycleSeconds, 2f / Fps, loop.clip + " sağ actual cycle");
                    row.complete = true;
                    Debug.Log("[DD-TEST] wp082.natural24 " + loop.clip + " uNatural=" + row.naturalMps.ToString("F5", CultureInfo.InvariantCulture) +
                        " uRaw=" + row.rawMps.ToString("F5", CultureInfo.InvariantCulture) + " S=" + row.heroStrideM.ToString("F5", CultureInfo.InvariantCulture) +
                        " T=" + cycle.ToString("F5", CultureInfo.InvariantCulture) + " nL/R=" + row.left.samples + "/" + row.right.samples +
                        " vyFilteredL/R=" + row.left.filteredSamples + "/" + row.right.filteredSamples);
                }
                Assert.AreEqual(24, seen.Count);
                proof.complete = true;
            }
            finally
            {
                if (_recorder != null) _recorder.Stop();
                if (csv != null) csv.Dispose();
                WriteNew(Path.Combine(output, "natural24.json"), JsonUtility.ToJson(proof, true) + "\n");
                var manifest = new Natural24Manifest { sourceCommit = actualHead, complete = proof.complete,
                    items = new[] { PinPath(Path.Combine(output, "natural24.json")), PinPath(Path.Combine(output, "natural24-frames.csv")) } };
                WriteNew(Path.Combine(output, "manifest.json"), JsonUtility.ToJson(manifest, true) + "\n");
                var sums = new StringBuilder();
                foreach (string name in new[] { "natural24.json", "natural24-frames.csv", "manifest.json" })
                    if (File.Exists(Path.Combine(output, name))) sums.Append(Hash(Path.Combine(output, name))).Append("  ").Append(name).Append('\n');
                WriteNew(Path.Combine(output, "SHA256SUMS"), sums.ToString());
                FootIkAdapter.Force = _forceBefore;
                Time.captureDeltaTime = _captureBefore;
            }
        }

        internal static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static AnimatorController BaseController(RuntimeAnimatorController controller)
        {
            for (int depth = 0; depth < 8 && controller is AnimatorOverrideController; depth++)
                controller = ((AnimatorOverrideController)controller).runtimeAnimatorController;
            var result = controller as AnimatorController;
            Assert.IsNotNull(result, "base controller asset Editor içinde okunabilir olmalı");
            return result;
        }
        private static AnimatorState FindState(AnimatorStateMachine machine, string name)
        {
            foreach (ChildAnimatorState state in machine.states) if (state.state.name == name) return state.state;
            return null;
        }
        private static ChildMotion FindChild(BlendTree tree, AnimationClip clip)
        {
            foreach (ChildMotion child in tree.children) if (child.motion == clip) return child;
            Assert.Fail(clip.name + " actual controller child yok");
            return default(ChildMotion);
        }
        private static bool IsSha(string value)
        {
            if (value == null || value.Length != 40) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')) return false;
            return true;
        }
        private static string ReadHead(string start)
        {
            string root = start;
            while (!string.IsNullOrEmpty(root) && !Directory.Exists(Path.Combine(root, ".git")) && !File.Exists(Path.Combine(root, ".git")))
                root = Path.GetDirectoryName(root);
            Assert.IsFalse(string.IsNullOrEmpty(root), "worktree git root yok");
            string git = Path.Combine(root, ".git");
            if (File.Exists(git)) {
                string marker = File.ReadAllText(git).Trim();
                Assert.IsTrue(marker.StartsWith("gitdir: ", StringComparison.Ordinal));
                git = Path.GetFullPath(Path.Combine(root, marker.Substring(8).Trim()));
            }
            string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
            if (IsSha(head)) return head.ToLowerInvariant();
            Assert.IsTrue(head.StartsWith("ref: ", StringComparison.Ordinal), "HEAD ref okunamadı");
            string reference = head.Substring(5).Trim();
            string common = git;
            if (File.Exists(Path.Combine(git, "commondir")))
                common = Path.GetFullPath(Path.Combine(git, File.ReadAllText(Path.Combine(git, "commondir")).Trim()));
            foreach (string directory in new[] { git, common }) {
                string loose = Path.Combine(directory, reference);
                if (File.Exists(loose)) return File.ReadAllText(loose).Trim().ToLowerInvariant();
            }
            string packed = Path.Combine(common, "packed-refs");
            if (File.Exists(packed)) foreach (string line in File.ReadLines(packed)) {
                string[] parts = line.Split(' ');
                if (parts.Length == 2 && parts[1] == reference && IsSha(parts[0])) return parts[0].ToLowerInvariant();
            }
            Assert.Fail("HEAD SHA bulunamadı: " + reference);
            return null;
        }
        private static Natural24AssetPin Pin(Object asset)
        {
            Assert.IsNotNull(asset);
            var pin = PinPath(AssetDatabase.GetAssetPath(asset));
            pin.name = asset.name;
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long fileId), asset.name + " GUID/fileID yok");
            pin.guid = guid;
            pin.fileId = fileId.ToString(CultureInfo.InvariantCulture);
            pin.dependencyHash = AssetDatabase.GetAssetDependencyHash(pin.path).ToString();
            return pin;
        }
        private static Natural24AssetPin PinPath(string path)
        {
            Assert.IsFalse(string.IsNullOrEmpty(path), "kaynak yolu boş");
            string full = Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
            Assert.IsTrue(File.Exists(full), "kaynak yok: " + full);
            return new Natural24AssetPin { path = path, fileSha256 = Hash(full),
                metaSha256 = File.Exists(full + ".meta") ? Hash(full + ".meta") : "" };
        }
        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        private static void WriteNew(string path, string text)
        {
            using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false))) writer.Write(text);
        }
    }

    [Serializable] internal sealed class Natural24AssetPin { public string name, path, guid, fileId, dependencyHash, fileSha256, metaSha256; }
    [Serializable] internal sealed class Natural24Override { public Natural24AssetPin original, replacement; }
    [Serializable] internal sealed class Natural24Manifest { public string sourceCommit; public bool complete; public Natural24AssetPin[] items; }
    [Serializable] internal sealed class Natural24Proof
    {
        public string utc, sourceCommit, unityVersion, measurement, units, acceptance, runtimeControllerName, runtimeControllerType;
        public int fps, cyclesPerFootRequired;
        public float verticalFilterMps;
        public bool complete;
        public Natural24AssetPin avatar, controller, animationSet, prefab;
        public List<Natural24Override> runtimeOverrides = new List<Natural24Override>();
        public Natural24AssetPin[] sources;
        public string[] layerNames;
        public float[] layerWeights;
        public List<Natural24Row> rows = new List<Natural24Row>();
    }
    [Serializable] internal sealed class Natural24Foot
    {
        public int samples, filteredSamples, completeWindows, plantEvents, offEvents, cycleIntervals;
        public bool hasMean, hasFilteredMean;
        public float naturalMps, filteredNaturalMps, observedCycleSeconds, minAbsVy, maxAbsVy;
    }
    [Serializable] internal sealed class Natural24Row
    {
        public string clipName, firstViolation;
        public int slot, directionIndex, frames, invalidFrames;
        public bool complete;
        public Vector3 localDirection, direction;
        public Quaternion animatorRootRotation;
        public float thresholdMps, childScale, clipSeconds, expectedCycleSeconds, storedUnityRawMps, templateStrideM, globalK;
        public float naturalMps, rawMps, heroStrideM, plantPhaseLeft, offPhaseLeft, plantPhaseRight, offPhaseRight;
        public Natural24AssetPin clip;
        public Natural24Foot left = new Natural24Foot(), right = new Natural24Foot();
    }

    /// <summary>Actual final pose, same LateUpdate order as the E3 recorder. No world-root motion or IK is allowed in this diagnosis.</summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class Natural24Recorder : MonoBehaviour
    {
        private Animator _animator;
        private AnimationClip _expectedClip;
        private FootIkAdapter _ik;
        private CharacterAnimationEvents _events;
        private Transform _left, _right;
        private Natural24Row _row;
        private StreamWriter _csv;
        private Vector3 _fixedRoot, _fixedAnimatorRoot, _previousRoot, _previousLeft, _previousRight;
        private Quaternion _fixedAnimatorRotation;
        private bool _hasPrevious, _running;
        private int _stateHash;
        private float _plantL, _offL, _plantR, _offR;
        private readonly List<AnimatorClipInfo> _clips = new List<AnimatorClipInfo>();
        private readonly FootAccumulator _l = new FootAccumulator(), _r = new FootAccumulator();
        internal bool Enough => _row != null && _l.Complete >= R3HeroNaturalStrideTests.RequiredCycles && _r.Complete >= R3HeroNaturalStrideTests.RequiredCycles &&
            _l.Intervals >= R3HeroNaturalStrideTests.RequiredCycles && _r.Intervals >= R3HeroNaturalStrideTests.RequiredCycles;

        internal void Begin(Animator animator, FootIkAdapter ik, CharacterAnimationEvents events, Natural24Row row, AnimationClip expectedClip, StreamWriter csv, Vector3 fixedRoot, int stateHash)
        {
            Stop();
            _animator = animator; _ik = ik; _events = events; _row = row; _csv = csv; _fixedRoot = fixedRoot; _stateHash = stateHash;
            _expectedClip = expectedClip; _fixedAnimatorRoot = animator.transform.position; _fixedAnimatorRotation = animator.transform.rotation;
            _left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Assert.IsNotNull(_left); Assert.IsNotNull(_right);
            AnimationClip clip = null;
            animator.GetCurrentAnimatorClipInfo(0, _clips);
            foreach (AnimatorClipInfo info in _clips) if (info.clip == _expectedClip) clip = info.clip;
            Assert.IsNotNull(clip, row.clipName + " actual base-layer clip yok");
            _plantL = EventPhase(clip, "foot_l"); _offL = EventPhase(clip, "foot_l_off");
            _plantR = EventPhase(clip, "foot_r"); _offR = EventPhase(clip, "foot_r_off");
            row.plantPhaseLeft = _plantL; row.offPhaseLeft = _offL; row.plantPhaseRight = _plantR; row.offPhaseRight = _offR;
            Assert.Greater(WindowLength(_plantL, _offL), 0f); Assert.Greater(WindowLength(_plantR, _offR), 0f);
            _l.Reset(); _r.Reset(); _hasPrevious = false;
            _events.Raised += OnEvent;
            _running = true;
        }
        internal void Stop()
        {
            _running = false;
            if (_events != null) _events.Raised -= OnEvent;
            _events = null;
            if (_row == null) return;
            _l.CopyTo(_row.left); _r.CopyTo(_row.right);
            int samples = _row.left.samples + _row.right.samples;
            _row.naturalMps = samples > 0 ? (float)((_l.Sum + _r.Sum) / samples) : 0f;
            _row.rawMps = _row.childScale > 0f ? _row.naturalMps / _row.childScale : 0f;
            _row.heroStrideM = _row.naturalMps * _row.expectedCycleSeconds;
            _csv?.Flush();
            _csv = null; _row = null;   // idempotent: teardown can stop after the writer has been disposed
        }
        private void OnEvent(CharacterAnimationEvent e)
        {
            if (!_running || _row == null) return;
            if (e == CharacterAnimationEvent.FootLeft) _row.left.plantEvents++;
            else if (e == CharacterAnimationEvent.FootRight) _row.right.plantEvents++;
            else if (e == CharacterAnimationEvent.FootLOff) _row.left.offEvents++;
            else if (e == CharacterAnimationEvent.FootROff) _row.right.offEvents++;
        }
        private static float EventPhase(AnimationClip clip, string name)
        {
            int found = 0; float phase = 0f;
            foreach (AnimationEvent e in clip.events) if (e.functionName == name) { found++; phase = e.time / clip.length; }
            Assert.AreEqual(1, found, clip.name + " actual event " + name);
            return Mathf.Repeat(phase, 1f);
        }
        private static float WindowLength(float plant, float off) => Mathf.Repeat(off - plant, 1f);
        private static bool InWindow(float phase, float plant, float off) => Mathf.Repeat(phase - plant, 1f) < WindowLength(plant, off);

        private void LateUpdate()
        {
            if (!_running) return;
            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
            float phase = Mathf.Repeat(state.normalizedTime, 1f), dt = Time.deltaTime;
            float weight = 0f;
            _animator.GetCurrentAnimatorClipInfo(0, _clips);
            foreach (AnimatorClipInfo clip in _clips) if (clip.clip == _expectedClip) weight += clip.weight;
            float rate = _animator.GetFloat(C.LocoRate);
            float effectiveRate = state.speed * state.speedMultiplier * _animator.speed;
            string violation = null;
            if (FootIkAdapter.Force != 0 || _ik.Served || !R3HeroNaturalStrideTests.Finite(_ik.Weight) || _ik.Weight > 0.001f || _ik.LockedFeet != 0) violation = "foot IK tam kapalı değil";
            else if (!R3HeroNaturalStrideTests.Finite(rate) || Mathf.Abs(rate - 1f) > 1e-4f || !R3HeroNaturalStrideTests.Finite(effectiveRate) || Mathf.Abs(effectiveRate - 1f) > 1e-4f) violation = "actual LocoRate/effective state rate != 1";
            else if (_animator.IsInTransition(0) || state.shortNameHash != _stateHash || !R3HeroNaturalStrideTests.Finite(weight) || weight < 0.999f) violation = "pure actual clip/state değil";
            else if (Mathf.Abs(dt - 1f / R3HeroNaturalStrideTests.Fps) > 1e-5f) violation = "actual dt != 1/60";
            else if (Vector3.Distance(transform.position, _fixedRoot) > 1e-5f ||
                Vector3.Distance(_animator.transform.position, _fixedAnimatorRoot) > 1e-5f ||
                Quaternion.Angle(_animator.transform.rotation, _fixedAnimatorRotation) > 0.001f) violation = "fixed root hareket etti";
            Vector3 root = _animator.transform.position, left = _left.position, right = _right.position;
            bool contactL = InWindow(phase, _plantL, _offL), contactR = InWindow(phase, _plantR, _offR);
            _row.frames++;
            if (_hasPrevious && dt > 0f)
            {
                Vector3 bodyV = (root - _previousRoot) / dt;
                Vector3 lv = (left - _previousLeft) / dt - bodyV, rv = (right - _previousRight) / dt - bodyV;
                float ul = -Vector3.Dot(lv, _row.direction), ur = -Vector3.Dot(rv, _row.direction);
                if (violation == null && !(R3HeroNaturalStrideTests.Finite(ul) && R3HeroNaturalStrideTests.Finite(ur) &&
                    R3HeroNaturalStrideTests.Finite(lv.y) && R3HeroNaturalStrideTests.Finite(rv.y))) violation = "sonlu foot velocity değil";
                bool valid = violation == null;
                bool usedL = _l.Push(contactL, ul, lv.y, Time.time, valid);
                bool usedR = _r.Push(contactR, ur, rv.y, Time.time, valid);
                _csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10},{11},{12:R},{13},{14:R},{15:R},{16:R},{17:R},{18:R},{19},{20},{21},{22}",
                    _row.clipName, Time.frameCount, phase, state.normalizedTime, dt, rate, state.speed, state.speedMultiplier, _animator.speed, weight, FootIkAdapter.Force, _ik.Served ? 1 : 0, _ik.Weight, _ik.LockedFeet,
                    new Vector2(bodyV.x, bodyV.z).magnitude, ul, ur, lv.y, rv.y, contactL ? 1 : 0, contactR ? 1 : 0, usedL ? 1 : 0, usedR ? 1 : 0));
            }
            else { _l.Seed(contactL); _r.Seed(contactR); }
            if (violation != null) { _row.invalidFrames++; if (_row.firstViolation == null) _row.firstViolation = violation; }
            _previousRoot = root; _previousLeft = left; _previousRight = right; _hasPrevious = true;
        }
        private sealed class FootAccumulator
        {
            private bool _previousContact, _active;
            private int _samples, _filtered, _complete, _intervals, _windowSamples, _windowFiltered;
            private double _sum, _filteredSum, _periodSum, _windowSum, _windowFilteredSum;
            private float _lastStart, _minVy, _maxVy, _windowMinVy, _windowMaxVy;
            internal int Complete => _complete;
            internal int Intervals => _intervals;
            internal double Sum => _sum;
            internal void Reset()
            {
                _previousContact = _active = false; _samples = _filtered = _complete = _intervals = 0;
                _sum = _filteredSum = _periodSum = 0; _lastStart = -1f; _minVy = float.MaxValue; _maxVy = 0f;
                ResetWindow();
            }
            private void ResetWindow()
            {
                _windowSamples = _windowFiltered = 0; _windowSum = _windowFilteredSum = 0;
                _windowMinVy = float.MaxValue; _windowMaxVy = 0f;
            }
            internal void Seed(bool contact) { _previousContact = contact; _active = false; }
            internal bool Push(bool contact, float u, float vy, float now, bool valid)
            {
                if (!valid) { _active = false; _previousContact = contact; _lastStart = -1f; ResetWindow(); return false; }
                if (contact && !_previousContact)
                {
                    _active = true; ResetWindow();
                    if (_lastStart >= 0f) { _periodSum += now - _lastStart; _intervals++; }
                    _lastStart = now;
                }
                // CSV eligibility is provisional. Aggregates commit only at the off edge; partial first/last windows do not count.
                bool eligible = contact && _active;
                if (eligible)
                {
                    _windowSamples++; _windowSum += u;
                    float av = Mathf.Abs(vy); _windowMinVy = Mathf.Min(_windowMinVy, av); _windowMaxVy = Mathf.Max(_windowMaxVy, av);
                    if (av <= R3HeroNaturalStrideTests.VerticalLimit) { _windowFiltered++; _windowFilteredSum += u; }
                }
                if (!contact && _previousContact && _active)
                {
                    if (_windowSamples > 0) {
                        _complete++; _samples += _windowSamples; _filtered += _windowFiltered;
                        _sum += _windowSum; _filteredSum += _windowFilteredSum;
                        _minVy = Mathf.Min(_minVy, _windowMinVy); _maxVy = Mathf.Max(_maxVy, _windowMaxVy);
                    }
                    _active = false; ResetWindow();
                }
                _previousContact = contact;
                return eligible;
            }
            internal void CopyTo(Natural24Foot foot)
            {
                foot.samples = _samples; foot.filteredSamples = _filtered; foot.completeWindows = _complete; foot.cycleIntervals = _intervals;
                foot.hasMean = _samples > 0; foot.hasFilteredMean = _filtered > 0;
                foot.naturalMps = _samples > 0 ? (float)(_sum / _samples) : 0f;
                foot.filteredNaturalMps = _filtered > 0 ? (float)(_filteredSum / _filtered) : 0f;
                foot.observedCycleSeconds = _intervals > 0 ? (float)(_periodSum / _intervals) : 0f;
                foot.minAbsVy = _samples > 0 ? _minVy : 0f; foot.maxAbsVy = _maxVy;
            }
        }
    }
}
#endif
