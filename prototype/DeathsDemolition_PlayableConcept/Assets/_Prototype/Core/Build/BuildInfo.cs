using System;
using UnityEngine;

namespace DD.Prototype.Core.Build
{
    /// <summary>
    /// WP-025: the version stamp of the running player, read from
    /// <c>Resources/build-info.json</c> (written by the Editor build tool right before a
    /// build, restored to the committed placeholder right after). Runtime code only reads;
    /// the UI shows <see cref="Label"/> in a corner, the crash log writes <see cref="Describe"/>.
    ///
    /// The committed placeholder carries <c>"stamped": false</c>, so an Editor play session or
    /// an unstamped build reads as "dev-unstamped" instead of a stale number.
    /// </summary>
    [Serializable]
    public sealed class BuildInfo
    {
        public const string ResourceName = "build-info";

        public bool stamped;
        public string describe = "dev-unstamped";
        public string commit = "";
        public string branch = "";
        public string date = "";
        public string mode = "";
        public string unity = "";

        /// <summary>Short text for a menu corner: "wp-017-…-2-g00bcde0 · release · 2026-09-16".</summary>
        public string Label
        {
            get
            {
                if (!stamped)
                {
                    return describe;
                }

                string day = date != null && date.Length >= 10 ? date.Substring(0, 10) : date;
                return describe + " · " + mode + " · " + day;
            }
        }

        /// <summary>One line with every field, for logs.</summary>
        public string Describe()
        {
            return "describe=" + describe + " commit=" + commit + " branch=" + branch + " date=" + date +
                   " mode=" + mode + " unity=" + unity + " stamped=" + stamped;
        }

        private static BuildInfo s_cached;

        /// <summary>Reads (and caches) the stamp. Never throws; a missing or broken resource reads as unstamped.</summary>
        public static BuildInfo Read()
        {
            if (s_cached != null)
            {
                return s_cached;
            }

            BuildInfo info = null;
            try
            {
                TextAsset text = Resources.Load<TextAsset>(ResourceName);
                if (text != null && !string.IsNullOrEmpty(text.text))
                {
                    info = Parse(text.text);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BuildInfo] could not read " + ResourceName + ": " + e.Message);
            }

            s_cached = info ?? new BuildInfo();
            return s_cached;
        }

        /// <summary>Parses the JSON written by the Editor tool. Null on malformed input.</summary>
        public static BuildInfo Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                BuildInfo info = JsonUtility.FromJson<BuildInfo>(json);
                if (info == null || string.IsNullOrEmpty(info.describe))
                {
                    return null;
                }

                return info;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cached = null;
        }
    }
}
