using System;
using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>
    /// WP-014 · Which world the Run scene plays in.
    ///
    ///   Procedural   the M0 blockout WorldBuilder assembles on Awake (the fallback and the A/B reference)
    ///   AP005        the authored AP-005 world scene, loaded additively into Run (World_AP005.unity)
    /// </summary>
    public enum WorldSource
    {
        Procedural = 0,
        AP005 = 1
    }

    /// <summary>
    /// WP-014 · The run-world switch as a ScriptableObject in Resources (ADR-014-1): not a
    /// ProjectSetting, not a scene reference — one asset the art pipeline writes and the
    /// Run scene reads on Awake. A command-line flag (<c>-ddWorldSource Procedural|AP005</c>)
    /// or the <c>DD_WORLD_SOURCE</c> environment variable overrides it for measurements and
    /// A/B runs without touching the asset; the resolved value is logged once.
    /// </summary>
    public sealed class WorldSourceSettings : ScriptableObject
    {
        /// <summary>Resources name (Assets/_Art/Resources/DD_WorldSource.asset).</summary>
        public const string ResourceName = "DD_WorldSource";

        public const string CommandLineFlag = "-ddWorldSource";
        public const string EnvironmentVariable = "DD_WORLD_SOURCE";

        [Tooltip("The world the Run scene loads. Procedural = M0 blockout (fallback); AP005 = the authored world scene.")]
        [SerializeField] private WorldSource _source = WorldSource.Procedural;

        [Tooltip("Scene loaded additively into Run when the source is AP005. Must be in the build settings.")]
        [SerializeField] private string _ap005SceneName = "World_AP005";

        public WorldSource Source => _source;
        public string Ap005SceneName => _ap005SceneName;

        private static bool s_logged;

        /// <summary>The effective world source: override flag/env if present, else the asset, else Procedural.</summary>
        public static WorldSource Resolve()
        {
            return Resolve(out _);
        }

        public static WorldSource Resolve(out string sceneName)
        {
            var settings = Resources.Load<WorldSourceSettings>(ResourceName);
            sceneName = settings != null ? settings.Ap005SceneName : "World_AP005";
            WorldSource source = settings != null ? settings.Source : WorldSource.Procedural;
            string origin = settings != null ? "asset" : "default (no " + ResourceName + " asset)";

            string forced = Arg(CommandLineFlag);
            if (string.IsNullOrEmpty(forced))
            {
                forced = Environment.GetEnvironmentVariable(EnvironmentVariable);
            }

            if (!string.IsNullOrEmpty(forced) && Enum.TryParse(forced, true, out WorldSource parsed))
            {
                source = parsed;
                origin = "override '" + forced + "'";
            }

            if (!s_logged)
            {
                s_logged = true;
                Debug.Log("[World] WorldSource = " + source + " (" + origin + ")");
            }

            return source;
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_logged = false;
        }
    }
}
