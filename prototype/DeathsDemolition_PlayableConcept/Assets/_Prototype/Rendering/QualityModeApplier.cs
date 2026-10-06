using System;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Rendering
{
    /// <summary>
    /// WP-070 · S-05 quality module (C-00 06.10, bug fix) · Applies the camera-mode profile's shadow cascade count at runtime.
    ///
    /// CONTRACT (QualityTiers §3 + the CameraModeProfile single-writer rule): the active URP asset's
    /// <c>shadowCascadeCount</c> = <see cref="QualityTiers.ShadowCascades"/>(tier, <see cref="CameraModeService.CurrentProfile"/>.ShadowCascades)
    /// = min(tier cap, mode), re-applied on <see cref="CameraModeService.Changed"/>, <see cref="CameraModeService.ProfilesChanged"/>
    /// and <see cref="QualityTiers.Changed"/>. Until now nothing called the contract at runtime: every mode rendered the asset's
    /// own count (Performance 2, High 2, Cinematic 4).
    ///
    /// NOT applied here: the profile's shadow distance and LOD bias factor (QualityTiers.ShadowDistance / LodBias) — the visual
    /// decision is open at C-00 / AD; the assets keep their distance and the QualitySettings level its lodBias.
    ///
    /// The URP assets are shared project files (book §11.26 / §11.31): the first write to an asset records its serialized count,
    /// and <see cref="RestoreOriginals"/> puts it back on <c>Application.quitting</c> and, in the Editor, on ExitingPlayMode — a play
    /// session never leaves an asset different from the disk. The event path does not allocate (the record grows once, one entry
    /// per quality level's asset; the tier is read through <c>QualitySettings.count</c>, not the allocating <c>names</c>).
    ///
    /// Static state resets on SubsystemRegistration (domain reload off). Subscriptions are made on AfterSceneLoad and re-armed
    /// remove-then-add on every scene load: <c>CameraModeService.ResetForTests</c> clears its events (static-bus rearm lesson).
    /// </summary>
    public static class QualityModeApplier
    {
        /// <summary>URP 14 <c>UniversalRenderPipelineAsset.shadowCascadeCount</c> accepts 1..4 (its setter throws outside).</summary>
        private const int MinCascades = 1, MaxCascades = 4;

        private static readonly Dictionary<UniversalRenderPipelineAsset, int> s_originalCascades = new Dictionary<UniversalRenderPipelineAsset, int>(4);
        private static readonly Action<CameraModeChange> s_onModeChanged = OnModeChanged;
        private static readonly Action s_onProfilesChanged = OnProfilesChanged;
        private static readonly Action<QualityTier> s_onTierChanged = OnTierChanged;
        private static readonly UnityEngine.Events.UnityAction<Scene, LoadSceneMode> s_onSceneLoaded = OnSceneLoaded;
        private static readonly Action s_onQuitting = RestoreOriginals;
        private static bool s_enabled = true;

        /// <summary>
        /// Measurement / test hook. False stops every write; the assets keep what they hold until <see cref="RestoreOriginals"/>
        /// (the "applier off" side of an A/B). Back to true on the next play session.
        /// </summary>
        public static bool Enabled
        {
            get { return s_enabled; }
            set { s_enabled = value; }
        }

        /// <summary>The cascade count written last this session (−1: none yet). Readout for tests and probes.</summary>
        public static int LastAppliedCascades { get; private set; } = -1;

        /// <summary>URP assets written this session whose serialized count is on record (0 after <see cref="RestoreOriginals"/>).</summary>
        public static int RecordedAssetCount => s_originalCascades.Count;

        /// <summary>The decision: the contract's min(tier cap, mode cascades), kept inside URP's accepted range.</summary>
        public static int CascadesFor(QualityTier tier, CameraModeProfile profile)
        {
            return Mathf.Clamp(QualityTiers.ShadowCascades(tier, profile.ShadowCascades), MinCascades, MaxCascades);
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
        }

        private static void Apply()
        {
            if (!s_enabled || !Application.isPlaying)
            {
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
            LastAppliedCascades = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= s_onSceneLoaded;
            SceneManager.sceneLoaded += s_onSceneLoaded;
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
