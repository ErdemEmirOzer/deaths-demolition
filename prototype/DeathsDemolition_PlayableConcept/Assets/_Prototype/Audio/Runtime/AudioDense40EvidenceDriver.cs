#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// Evidence-only load request for WP-065. The QA bot remains the action owner; this
    /// driver only asks the shipped spawner to hold its existing safety ceiling while an
    /// armed output capture records the real fight.
    /// </summary>
    public static class AudioDense40EvidenceDriver
    {
        private const string Flag = "-ddAudioDense40";
        private const string SecondsFlag = "-ddAudioDenseSeconds";
        private const string RoundFlag = "-ddAudioDenseRound";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0)
            {
                return;
            }

            var host = new GameObject("Audio Dense 40 Evidence");
            UnityEngine.Object.DontDestroyOnLoad(host);
            Driver driver = host.AddComponent<Driver>();
            driver.DenseSeconds = ReadSeconds();
            driver.TargetRound = ReadRound();
        }

        private static float ReadSeconds()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], SecondsFlag, StringComparison.Ordinal) &&
                    float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                {
                    return Mathf.Clamp(seconds, 5f, 120f);
                }
            }

            return 30f;
        }

        private static int ReadRound()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], RoundFlag, StringComparison.Ordinal) && int.TryParse(args[i + 1], out int round))
                {
                    return Mathf.Max(1, round);
                }
            }

            return 1;
        }

        private sealed class Driver : MonoBehaviour
        {
            private bool _applied;
            private float _appliedAt;
            private float _denseAt = -1f;
            public float DenseSeconds { private get; set; }
            public int TargetRound { private get; set; }

            private void Update()
            {
                if (RunDirector.Instance == null || RunDirector.Instance.Phase != RunPhase.Fighting ||
                    RunDirector.Instance.Round < TargetRound || EnemySpawnDirector.Instance == null)
                {
                    return;
                }

                EnemySpawnDirector spawn = EnemySpawnDirector.Instance;
                if (!_applied)
                {
                    spawn.ForceActiveTarget(spawn.SafetyCeiling, true);
                    _applied = true;
                    _appliedAt = Time.unscaledTime;
                    Debug.Log("[AudioDense40Evidence] target=" + spawn.SafetyCeiling + " round=" + RunDirector.Instance.Round);
                }

                if (_denseAt < 0f && spawn.ActiveCount >= spawn.SafetyCeiling)
                {
                    _denseAt = Time.unscaledTime;
                    Debug.Log("[AudioDense40Evidence] dense checkpoint reached; capture window=" + DenseSeconds.ToString("0.0") + "s");
                }

                if (_denseAt >= 0f && Time.unscaledTime - _denseAt >= DenseSeconds)
                {
                    Debug.Log("[AudioDense40Evidence] capture window complete");
                    Application.Quit(0);
                }
                else if (_denseAt < 0f && Time.unscaledTime - _appliedAt >= 90f)
                {
                    Debug.LogError("[AudioDense40Evidence] dense checkpoint timeout active=" + spawn.ActiveCount + " target=" + spawn.SafetyCeiling);
                    Application.Quit(3);
                }
            }
        }
    }
}
#endif
