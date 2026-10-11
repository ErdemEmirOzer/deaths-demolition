using System;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Rendering
{
    /// <summary>
    /// WP-070 · S-05 quality module (C-00 06.10, bug fix) · Applies the camera-mode profile's shadow cascade count and shadow
    /// distance at runtime.
    ///
    /// CONTRACT (QualityTiers §3 + the CameraModeProfile single-writer rule): the active URP asset's
    /// <c>shadowCascadeCount</c> = <see cref="QualityTiers.ShadowCascades"/>(tier, <see cref="CameraModeService.CurrentProfile"/>.ShadowCascades)
    /// = min(tier cap, mode), re-applied on <see cref="CameraModeService.Changed"/>, <see cref="CameraModeService.ProfilesChanged"/>
    /// and <see cref="QualityTiers.Changed"/>. Until now nothing called the contract at runtime: every mode rendered the asset's
    /// own count (Performance 2, High 2, Cinematic 4).
    /// The shadow distance had the same gap (C-00 06.10 decision 3): <c>shadowDistance</c> = <see cref="QualityTiers.ShadowDistance"/>
    /// (tier, profile.ShadowDistance) = min(tier cap, mode) — Cinematic 120 m → TD 60 / TP 60 / FP 70 m; High and Performance
    /// already sat at their cap (60 / 45 m).
    ///
    /// NOT applied here: the profile's LOD bias factor (QualityTiers.LodBias; measured equal to the level's lodBias today, a
    /// later slice with its own measurement).
    ///
    /// The menu's cinematic backdrop is not a camera mode (gt Y-2, WP-081 in main 06.10): while <see cref="WorldViewer.Active"/>
    /// the assets keep their own cascades and distance — the values S-09 tuned the menu under — and the profile applies again
    /// on the next event once the backdrop has gone (the run never sees the flag set).
    ///
    /// The URP assets are shared project files (book §11.26 / §11.31): the first write to an asset records its serialized count,
    /// and <see cref="RestoreOriginals"/> puts it back on <c>Application.quitting</c> and, in the Editor, on ExitingPlayMode — a play
    /// session never leaves an asset different from the disk. The event path does not allocate (the record grows once, one entry
    /// per quality level's asset; the tier is read through <c>QualitySettings.count</c>, not the allocating <c>names</c>).
    ///
    /// Static state resets on SubsystemRegistration (a safety for a domain-reload-off editor; this project reloads the domain,
    /// so the restore rests on the two exit hooks — gt Y-1). Subscriptions are made on AfterSceneLoad and re-armed
    /// remove-then-add on every scene load: <c>CameraModeService.ResetForTests</c> clears its events (static-bus rearm lesson).
    /// </summary>
    public static class QualityModeApplier
    {
        /// <summary>URP 14 <c>UniversalRenderPipelineAsset.shadowCascadeCount</c> accepts 1..4 (its setter throws outside).</summary>
        private const int MinCascades = 1, MaxCascades = 4;

        private static readonly Dictionary<UniversalRenderPipelineAsset, int> s_originalCascades = new Dictionary<UniversalRenderPipelineAsset, int>(4);
        private static readonly Dictionary<UniversalRenderPipelineAsset, float> s_originalDistances = new Dictionary<UniversalRenderPipelineAsset, float>(4);
        private static readonly Action<CameraModeChange> s_onModeChanged = OnModeChanged;
        private static readonly Action s_onProfilesChanged = OnProfilesChanged;
        private static readonly Action<QualityTier> s_onTierChanged = OnTierChanged;
        private static readonly UnityEngine.Events.UnityAction<Scene, LoadSceneMode> s_onSceneLoaded = OnSceneLoaded;
        private static readonly UnityEngine.Events.UnityAction<Scene> s_onSceneUnloaded = OnSceneUnloaded;
        private static readonly Action s_onQuitting = RestoreOriginals;
        private static bool s_enabled = true;
        private static bool s_applyShadowDistance = true;

        /// <summary>FINAL 10.10 perf: lantern flame cards farther than this are hidden in High top-down.</summary>
        public const float HighTopDownFlameCullMetres = 40f;

        /// <summary>
        /// FINAL 10.10 perf (C-00, profiler: the main thread is the limit): true while High × TopDown is applied. The planar
        /// reflection draws the sky only, every few frames, and the vegetation push map runs at its Performance size; false
        /// everywhere else (Cinematic unchanged).
        /// </summary>
        public static bool HighTopDownLite { get; private set; }
        private static readonly int GroundLiteId = Shader.PropertyToID("_DD_GroundLite");
        /// <summary>Experiment flag (C-00 10.10): <c>-ddRenderScale v</c> (0.5..1); absent = the asset's own value.</summary>
        public const string RenderScaleFlag = "-ddRenderScale";
        private static readonly Dictionary<UniversalRenderPipelineAsset, float> s_originalRenderScales = new Dictionary<UniversalRenderPipelineAsset, float>(4);
        private static float s_renderScaleOverride = ReadRenderScaleFlag();

        private static float ReadRenderScaleFlag()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == RenderScaleFlag && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                    return Mathf.Clamp(v, 0.5f, 1f);
            }

            return 0f;
        }

        /// <summary>
        /// Measurement / test hook. False stops every write; the assets keep what they hold until <see cref="RestoreOriginals"/>
        /// (the "applier off" side of an A/B). Back to true on the next play session.
        /// </summary>
        public static bool Enabled
        {
            get { return s_enabled; }
            set { s_enabled = value; }
        }

        /// <summary>
        /// Measurement hook (the AD's cascades-only frames): false leaves the shadow distance alone (the asset's), cascades still
        /// apply. Back to true on the next play session.
        /// </summary>
        public static bool ApplyShadowDistance
        {
            get { return s_applyShadowDistance; }
            set { s_applyShadowDistance = value; }
        }

        /// <summary>The shadow distance written last this session (−1: none yet).</summary>
        public static float LastAppliedShadowDistance { get; private set; } = -1f;

        /// <summary>The cascade count written last this session (−1: none yet). Readout for tests and probes.</summary>
        public static int LastAppliedCascades { get; private set; } = -1;

        /// <summary>URP assets written this session whose serialized count is on record (0 after <see cref="RestoreOriginals"/>).</summary>
        public static int RecordedAssetCount => s_originalCascades.Count;

        /// <summary>The decision: the contract's min(tier cap, mode cascades), kept inside URP's accepted range.</summary>
        public static int CascadesFor(QualityTier tier, CameraModeProfile profile)
        {
            return Mathf.Clamp(QualityTiers.ShadowCascades(tier, profile.ShadowCascades), MinCascades, MaxCascades);
        }

        /// <summary>The distance decision: the contract's min(tier cap, mode distance), never negative.</summary>
        public static float ShadowDistanceFor(QualityTier tier, CameraModeProfile profile)
        {
            return Mathf.Max(0f, QualityTiers.ShadowDistance(tier, profile.ShadowDistance));
        }

        /// <summary>The mode-aware decision the runtime writes (FINAL 10.10: High top-down caps at QualityTiers.TopDownHighShadowDistance).</summary>
        public static float ShadowDistanceFor(QualityTier tier, CameraMode mode, CameraModeProfile profile)
        {
            return Mathf.Max(0f, QualityTiers.ShadowDistance(tier, mode, profile.ShadowDistance));
        }

        /// <summary>Re-arms the subscriptions (remove-then-add) and applies the current tier × mode now. Play mode only.</summary>
        public static void ApplyNow()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Arm();
            Apply();
        }

        /// <summary>
        /// Puts every asset this session wrote back to its recorded serialized count and forgets the record. Runs on quit and on
        /// leaving play mode; tests call it to compare against the file. Writes again on the next event while <see cref="Enabled"/>.
        /// </summary>
        public static void RestoreOriginals()
        {
            foreach (KeyValuePair<UniversalRenderPipelineAsset, int> entry in s_originalCascades)
            {
                if (entry.Key != null && entry.Key.shadowCascadeCount != entry.Value)
                {
                    entry.Key.shadowCascadeCount = entry.Value;
                }
            }

            s_originalCascades.Clear();
            foreach (KeyValuePair<UniversalRenderPipelineAsset, float> entry in s_originalDistances)
            {
                if (entry.Key != null && !Mathf.Approximately(entry.Key.shadowDistance, entry.Value))
                {
                    entry.Key.shadowDistance = entry.Value;
                }
            }

            s_originalDistances.Clear();
            foreach (KeyValuePair<UniversalRenderPipelineAsset, float> entry in s_originalRenderScales)
            {
                if (entry.Key != null && !Mathf.Approximately(entry.Key.renderScale, entry.Value)) entry.Key.renderScale = entry.Value;
            }

            s_originalRenderScales.Clear();
            Shader.SetGlobalFloat(GroundLiteId, 0f);   // the High top-down switches never outlive the session
            HighTopDownLite = false;
            DD.Prototype.Feedback.Vfx.LanternFlames.CardCullMetres = 0f;
        }

        private static void Apply()
        {
            if (!s_enabled || !Application.isPlaying)
            {
                return;
            }

            if (WorldViewer.Active)
            {
                RestoreOriginals();   // the menu backdrop renders with the assets' own values
                return;
            }

            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null)
            {
                return;
            }

            // QualityTiers.Current's mapping, through QualitySettings.count (QualitySettings.names allocates a string[]).
            QualityTier tier = QualityTiers.TierOfLevel(QualitySettings.GetQualityLevel(), QualitySettings.count);
            int cascades = CascadesFor(tier, CameraModeService.CurrentProfile);
            if (!s_originalCascades.ContainsKey(asset))
            {
                s_originalCascades.Add(asset, asset.shadowCascadeCount);
            }

            if (asset.shadowCascadeCount != cascades)
            {
                asset.shadowCascadeCount = cascades;
            }

            LastAppliedCascades = cascades;

            if (!s_originalDistances.ContainsKey(asset))
            {
                s_originalDistances.Add(asset, asset.shadowDistance);
            }

            float distance = s_applyShadowDistance ? ShadowDistanceFor(tier, CameraModeService.Current, CameraModeService.CurrentProfile) : s_originalDistances[asset];
            if (!Mathf.Approximately(asset.shadowDistance, distance))
            {
                asset.shadowDistance = distance;
            }

            LastAppliedShadowDistance = distance;
            SmallShadowCasters.Apply(tier, CameraModeService.Current);   // FINAL 10.10 perf: High top-down only

            // FINAL 10.10 perf (C-00): High top-down lightening — the ground's lite path and the far lantern-card cull.
            bool highTopDown = tier == QualityTier.Yuksek && CameraModeService.Current == CameraMode.TopDown;
            HighTopDownLite = highTopDown;
            Shader.SetGlobalFloat(GroundLiteId, highTopDown ? 1f : 0f);
            DD.Prototype.Feedback.Vfx.LanternFlames.CardCullMetres = highTopDown ? HighTopDownFlameCullMetres : 0f;

            // Experiment only (C-00 10.10): -ddRenderScale <0.5..1> on the command line sets the active asset's render scale.
            if (s_renderScaleOverride > 0f)
            {
                if (!s_originalRenderScales.ContainsKey(asset)) s_originalRenderScales.Add(asset, asset.renderScale);
                if (!Mathf.Approximately(asset.renderScale, s_renderScaleOverride)) asset.renderScale = s_renderScaleOverride;
            }
        }

        private static void Arm()
        {
            CameraModeService.Changed -= s_onModeChanged;
            CameraModeService.Changed += s_onModeChanged;
            CameraModeService.ProfilesChanged -= s_onProfilesChanged;
            CameraModeService.ProfilesChanged += s_onProfilesChanged;
            QualityTiers.Changed -= s_onTierChanged;
            QualityTiers.Changed += s_onTierChanged;
        }

        private static void Disarm()
        {
            CameraModeService.Changed -= s_onModeChanged;
            CameraModeService.ProfilesChanged -= s_onProfilesChanged;
            QualityTiers.Changed -= s_onTierChanged;
            SceneManager.sceneLoaded -= s_onSceneLoaded;
            SceneManager.sceneUnloaded -= s_onSceneUnloaded;
            Application.quitting -= s_onQuitting;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        }

        private static void OnModeChanged(CameraModeChange change)
        {
            Apply();
        }

        private static void OnProfilesChanged()
        {
            Apply();
        }

        private static void OnTierChanged(QualityTier tier)
        {
            Apply();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Arm();
            Apply();
        }

        /// <summary>The menu backdrop's world goes after its flag clears: the profile applies again without waiting for an event.</summary>
        private static void OnSceneUnloaded(Scene scene)
        {
            Apply();
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            int restored = s_originalCascades.Count;
            RestoreOriginals();
            Disarm();
            Debug.Log("[Quality] mode cascades: " + restored + " URP asset(s) restored to their serialized count on ExitingPlayMode");
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // A session that ended without its exit hook (crash, killed batch) left its record: put those values back first.
            RestoreOriginals();
            Disarm();
            s_enabled = true;
            s_applyShadowDistance = true;
            LastAppliedCascades = -1;
            LastAppliedShadowDistance = -1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= s_onSceneLoaded;
            SceneManager.sceneLoaded += s_onSceneLoaded;
            SceneManager.sceneUnloaded -= s_onSceneUnloaded;
            SceneManager.sceneUnloaded += s_onSceneUnloaded;
            Application.quitting -= s_onQuitting;
            Application.quitting += s_onQuitting;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
            Arm();
            Apply();
        }
    }
}
