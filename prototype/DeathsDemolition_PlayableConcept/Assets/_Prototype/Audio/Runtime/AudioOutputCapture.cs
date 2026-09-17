#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Threading;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-060 · Player output recorder (evidence only; development Player / Editor).
    ///
    /// <c>-ddAudioCapture &lt;file.wav&gt;</c> arms it. A tap (<see cref="AudioOutputTap"/>) rides every
    /// <see cref="AudioListener"/> in the scene and, for the one that is enabled, forwards the final
    /// mix - post AudioMixer, post limiter, exactly what the speakers get - into a single-producer /
    /// single-consumer ring; <see cref="Update"/> drains the ring to a 16-bit PCM WAV. Nothing is
    /// allocated on the audio thread; if the main thread stalls for longer than the ring holds, the
    /// dropped sample count is reported in the sidecar rather than blocking the DSP.
    ///
    /// The sidecar <c>&lt;file&gt;.markers.txt</c> lists <c>seconds \t realtime \t kind \t detail</c>
    /// (scene loads, run phases, rounds, run end, player death) so the recording can be cut per phase
    /// offline (tools: AP-011 <c>audio_capture_report.py</c>) and aligned with the QA bot's events.txt.
    /// </summary>
    public sealed class AudioOutputCapture : MonoBehaviour
    {
        public const string Flag = "-ddAudioCapture";

        private const int RingSeconds = 8;
        private const float RescanSeconds = 0.5f;

        private float[] _ring;
        private long _write;             // samples pushed (audio thread)
        private long _read;              // samples drained (main thread)
        private long _dropped;
        private int _channels;           // set by the first callback
        private int _sampleRate;

        private FileStream _file;
        private StreamWriter _markers;
        private byte[] _pcm;
        private long _framesWritten;
        private float _rescanAt;
        private RunPhase _lastPhase = RunPhase.None;
        private string _path;

        public static AudioOutputCapture Armed { get; private set; }

        /// <summary>Total frames written so far (evidence / tests).</summary>
        public long FramesWritten => _framesWritten;
        public long DroppedSamples => Interlocked.Read(ref _dropped);
        public string Path => _path;

        /// <summary>Reads the command line; adds the recorder to <paramref name="host"/> when the flag is present.</summary>
        public static AudioOutputCapture TryArm(GameObject host)
        {
            string path = Arg(Flag);
            if (string.IsNullOrEmpty(path) || Armed != null)
            {
                return Armed;
            }

            return Arm(host, path);
        }

        public static AudioOutputCapture Arm(GameObject host, string path)
        {
            var c = host.AddComponent<AudioOutputCapture>();
            c.Begin(path);
            Armed = c;
            return c;
        }

        private static string Arg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private void Begin(string path)
        {
            _path = path;
            _sampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            int channels = SpeakerChannels(AudioSettings.driverCapabilities);
            _ring = new float[RingSeconds * _sampleRate * Mathf.Max(2, channels)];
            _pcm = new byte[_ring.Length * 2];

            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            _file.Write(new byte[44], 0, 44);   // header patched in Finish() once the channel count is known
            _markers = new StreamWriter(path + ".markers.txt", false, new UTF8Encoding(false));
            _markers.WriteLine("# seconds\trealtime\tkind\tdetail");
            Mark("capture", "rate " + _sampleRate + " dsp " + bufferLength + "x" + numBuffers + " speakers " + AudioSettings.driverCapabilities);

            SceneManager.sceneLoaded += OnSceneLoaded;
            RunEvents.PhaseChanged += OnPhaseChanged;
            RunEvents.RunStarted += OnRunStarted;
            RunEvents.RoundStarted += OnRoundStarted;
            RunEvents.RoundCompleted += OnRoundCompleted;
            RunEvents.RunEnded += OnRunEnded;
            DD.Prototype.PlayerSystem.PlayerHealth.AnyPlayerDied += OnPlayerDied;
            Rescan();
        }

        private static int SpeakerChannels(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                default: return 2;
            }
        }

        // ------------------------------------------------------------------ audio thread

        /// <summary>Called by the enabled listener's tap. Copies into the ring; never blocks, never allocates.</summary>
        internal void Push(float[] data, int channels)
        {
            if (_ring == null) return;
            if (_channels == 0) _channels = channels;

            long write = Interlocked.Read(ref _write);
            long read = Interlocked.Read(ref _read);
            long free = _ring.Length - (write - read);
            if (free < data.Length)
            {
                Interlocked.Add(ref _dropped, data.Length);
                return;
            }

            int head = (int)(write % _ring.Length);
            int first = Mathf.Min(data.Length, _ring.Length - head);
            Array.Copy(data, 0, _ring, head, first);
            if (first < data.Length)
            {
                Array.Copy(data, first, _ring, 0, data.Length - first);
            }

            Interlocked.Add(ref _write, data.Length);
        }

        // ------------------------------------------------------------------ main thread

        private void Update()
        {
            if (Time.unscaledTime >= _rescanAt)
            {
                Rescan();
            }

            Drain();
        }

        private void Rescan()
        {
            _rescanAt = Time.unscaledTime + RescanSeconds;
            AudioListener[] all = FindObjectsOfType<AudioListener>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].GetComponent<AudioOutputTap>() == null)
                {
                    all[i].gameObject.AddComponent<AudioOutputTap>().Bind(this, all[i]);
                }
            }
        }

        private void Drain()
        {
            if (_file == null) return;
            long write = Interlocked.Read(ref _write);
            long read = _read;
            int count = (int)Math.Min(write - read, _ring.Length);
            if (count <= 0) return;

            int head = (int)(read % _ring.Length);
            int o = 0;
            for (int i = 0; i < count; i++)
            {
                float v = _ring[(head + i) % _ring.Length];
                int s = (int)(Mathf.Clamp(v, -1f, 1f) * 32767f);
                _pcm[o++] = (byte)(s & 0xFF);
                _pcm[o++] = (byte)((s >> 8) & 0xFF);
            }

            _file.Write(_pcm, 0, o);
            Interlocked.Add(ref _read, count);
            if (_channels > 0) _framesWritten += count / _channels;
        }

        private double Seconds => _channels > 0 && _sampleRate > 0 ? Interlocked.Read(ref _write) / (double)(_channels * _sampleRate) : 0.0;

        private void Mark(string kind, string detail)
        {
            if (_markers == null) return;
            _markers.WriteLine(Seconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "\t"
                               + Time.realtimeSinceStartup.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "\t"
                               + kind + "\t" + detail);
            _markers.Flush();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Mark("scene", scene.name); _rescanAt = 0f; }
        private void OnRunStarted(int runId) { Mark("run", "start " + runId); }
        private void OnRoundStarted(int round) { Mark("round", "start " + round); }
        private void OnRoundCompleted(int round) { Mark("round", "complete " + round); }
        private void OnRunEnded(RunSummary s) { Mark("run", "end " + s.EndReason + " rounds " + s.CompletedRounds + " duration " + s.RunDuration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)); }
        private void OnPlayerDied(DD.Prototype.PlayerSystem.PlayerDeathInfo info) { Mark("player", "died"); }

        private void OnPhaseChanged(RunPhase phase)
        {
            if (phase == _lastPhase) return;
            _lastPhase = phase;
            Mark("phase", phase.ToString());
        }

        private void OnApplicationQuit() { Finish(); }
        private void OnDestroy() { Finish(); }

        private void Finish()
        {
            if (_file == null) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RunEvents.PhaseChanged -= OnPhaseChanged;
            RunEvents.RunStarted -= OnRunStarted;
            RunEvents.RoundStarted -= OnRoundStarted;
            RunEvents.RoundCompleted -= OnRoundCompleted;
            RunEvents.RunEnded -= OnRunEnded;
            DD.Prototype.PlayerSystem.PlayerHealth.AnyPlayerDied -= OnPlayerDied;

            Drain();
            Mark("capture", "end frames " + _framesWritten + " dropped " + DroppedSamples + " channels " + _channels);
            _markers.Dispose();
            _markers = null;

            int ch = Mathf.Max(1, _channels);
            long dataBytes = _file.Position - 44;
            _file.Seek(0, SeekOrigin.Begin);
            using (var w = new BinaryWriter(_file, Encoding.ASCII, true))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write((int)(36 + dataBytes));
                w.Write(Encoding.ASCII.GetBytes("WAVE")); w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(_sampleRate);
                w.Write(_sampleRate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write((int)dataBytes);
            }

            _file.Dispose();
            _file = null;
            if (Armed == this) Armed = null;
        }
    }

    /// <summary>The per-listener tap: forwards the mixed output of the listener it sits on while that listener is enabled.</summary>
    public sealed class AudioOutputTap : MonoBehaviour
    {
        private AudioOutputCapture _owner;
        private AudioListener _listener;
        private volatile bool _active;

        internal void Bind(AudioOutputCapture owner, AudioListener listener)
        {
            _owner = owner;
            _listener = listener;
        }

        private void Update()
        {
            _active = _listener != null && _listener.enabled && _listener.gameObject.activeInHierarchy;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_active && _owner != null)
            {
                _owner.Push(data, channels);
            }
        }
    }
}
#endif
