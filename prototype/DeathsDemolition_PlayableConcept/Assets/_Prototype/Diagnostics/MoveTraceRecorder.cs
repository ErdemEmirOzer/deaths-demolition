using System;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.PlayerSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-087 R3 (C-00 06.10 / 07.10; the user's release play, TopDown: "the character moves on its own, WASD does not steer it right,
    /// it reacts late") · <c>-ddMoveTrace [file.csv]</c> writes one row per top-down frame of the real player path — the frame's times
    /// (unscaled / scaled dt, timeScale), the physical W / A / S / D keys and the keyboard's last Input System update time, the run's
    /// input source's Move, the motor's commanded and actual planar velocity, the position, the facing, whether control is on — so a
    /// player who meets the problem again hands back one file that tells input from motor from frame time.
    /// Without the flag nothing is created (the default path is untouched). With no file the trace goes next to Player.log, never into
    /// the player's data folder. Top-down only (user 06.10): other camera modes write no rows.
    ///
    /// WRITTEN EXCEPTION (C-00 07.10, like PrototypeCaptureAgent's H12 / Codex-audit M-55): diagnostics are normally dev-build only
    /// (<c>#if DEVELOPMENT_BUILD || UNITY_EDITOR</c>, and DD.Prototype.Qa compiles empty in release). This one deliberately lives in
    /// DD.Prototype.Runtime with no guard: the user plays the RELEASE build, and the trace is the instruction we hand them. Active only
    /// with <c>-ddMoveTrace</c>; without it Boot scans the command line and creates nothing. Do not add any other behaviour to it. Flag runs only; the rows are buffered and
    /// written in blocks of BlockRows and at quit / destroy (formatting allocates — a diagnostic, not a hot path).
    /// </summary>
    public sealed class MoveTraceRecorder : MonoBehaviour
    {
        public const string Flag = "-ddMoveTrace";
        public const int BlockRows = 1024;
        public const string Header = "frame,t_real,dt_unscaled,dt,time_scale,key_w,key_a,key_s,key_d,kb_last_update,move_x,move_y,cmd_vx,cmd_vz,vel_vx,vel_vz,pos_x,pos_z,facing_deg,control";

        private struct Row
        {
            public int Frame;
            public double TReal, KbLast;
            public float DtUnscaled, Dt, TimeScale, MoveX, MoveY, CmdX, CmdZ, VelX, VelZ, PosX, PosZ, Facing;
            public byte Keys;   // bit 0 W, 1 A, 2 S, 3 D
            public bool Control;
        }

        private readonly Row[] _rows = new Row[BlockRows];
        private int _count;
        private StreamWriter _writer;
        private readonly StringBuilder _line = new StringBuilder(256);

        /// <summary>The file this trace writes (tests, the log line).</summary>
        public string FilePath { get; private set; }

        /// <summary>Rows written to the file so far (tests).</summary>
        public int RowsWritten { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!TryResolvePath(args, Application.consoleLogPath, DateTime.UtcNow, out string path)) return;
            Arm(path);
        }

        /// <summary>
        /// The file for <paramref name="args"/>: false without the flag; the flag's value when it has one (not another <c>-</c> flag);
        /// else movetrace-&lt;utc&gt;.csv beside <paramref name="consoleLogPath"/> (Player.log), else in the working directory.
        /// </summary>
        public static bool TryResolvePath(string[] args, string consoleLogPath, DateTime utc, out string path)
        {
            path = null;
            if (args == null || Array.IndexOf(args, Flag) < 0) return false;
            string value = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], Flag, StringComparison.Ordinal)) { value = args[i + 1]; break; }
            }

            if (!string.IsNullOrEmpty(value) && !value.StartsWith("-", StringComparison.Ordinal))
            {
                path = value;
                return true;
            }

            string dir = string.IsNullOrEmpty(consoleLogPath) ? null : Path.GetDirectoryName(consoleLogPath);
            path = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "movetrace-" + utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".csv");
            return true;
        }

        /// <summary>Starts a trace into <paramref name="path"/> (Boot, and PlayMode tests).</summary>
        public static MoveTraceRecorder Arm(string path)
        {
            var carrier = new GameObject("MoveTraceRecorder");
            DontDestroyOnLoad(carrier);
            MoveTraceRecorder r = carrier.AddComponent<MoveTraceRecorder>();
            r.Open(path);
            return r;
        }

        private void Open(string path)
        {
            FilePath = path;
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _writer = new StreamWriter(path, false, new UTF8Encoding(false));
            _writer.WriteLine("# DD move trace (WP-087 R3) · " + Application.version + " · Unity " + Application.unityVersion + " · " + Screen.width + "x" + Screen.height +
                              " · refresh " + Screen.currentResolution.refreshRateRatio.value.ToString("0.##", CultureInfo.InvariantCulture) + " Hz · vSync " + QualitySettings.vSyncCount +
                              " · targetFrameRate " + Application.targetFrameRate + " · quality " + QualitySettings.GetQualityLevel() + " · top-down frames only");
            _writer.WriteLine(Header);
            _writer.Flush();
            Debug.Log("[MoveTrace] armed -> " + path);
        }

        private void LateUpdate()
        {
            if (_writer == null || CameraModeService.Current != CameraMode.TopDown) return;
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null || rig.Motor == null) return;
            PlayerMotor motor = rig.Motor;
            IInputSource input = InputSourceRegistry.Current;
            Keyboard kb = Keyboard.current;
            Vector2 move = input != null ? input.Move : Vector2.zero;
            Vector3 cmd = motor.CommandedPlanarVelocity, vel = motor.PlanarVelocity, pos = motor.transform.position, f = motor.transform.forward;
            byte keys = 0;
            if (kb != null)
            {
                if (kb.wKey.isPressed) keys |= 1;
                if (kb.aKey.isPressed) keys |= 2;
                if (kb.sKey.isPressed) keys |= 4;
                if (kb.dKey.isPressed) keys |= 8;
            }

            _rows[_count++] = new Row
            {
                Frame = Time.frameCount, TReal = Time.realtimeSinceStartupAsDouble, DtUnscaled = Time.unscaledDeltaTime, Dt = Time.deltaTime, TimeScale = Time.timeScale,
                Keys = keys, KbLast = kb != null ? kb.lastUpdateTime : -1.0, MoveX = move.x, MoveY = move.y, CmdX = cmd.x, CmdZ = cmd.z, VelX = vel.x, VelZ = vel.z,
                PosX = pos.x, PosZ = pos.z, Facing = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, Control = motor.ControlEnabled
            };
            if (_count == BlockRows) Flush();
        }

        /// <summary>Writes the buffered rows (a full block, scene changes, quit; tests call it before reading the file).</summary>
        public void Flush()
        {
            if (_writer == null || _count == 0) return;
            for (int i = 0; i < _count; i++)
            {
                _writer.WriteLine(Format(_rows[i], _line));
            }

            RowsWritten += _count;
            _count = 0;
            _writer.Flush();
        }

        private static string Format(Row r, StringBuilder sb)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            sb.Clear();
            sb.Append(r.Frame).Append(',').Append(r.TReal.ToString("0.0000", c)).Append(',').Append(r.DtUnscaled.ToString("0.0000", c)).Append(',')
              .Append(r.Dt.ToString("0.0000", c)).Append(',').Append(r.TimeScale.ToString("0.###", c)).Append(',')
              .Append((r.Keys & 1) != 0 ? '1' : '0').Append(',').Append((r.Keys & 2) != 0 ? '1' : '0').Append(',')
              .Append((r.Keys & 4) != 0 ? '1' : '0').Append(',').Append((r.Keys & 8) != 0 ? '1' : '0').Append(',')
              .Append(r.KbLast.ToString("0.0000", c)).Append(',').Append(r.MoveX.ToString("0.###", c)).Append(',').Append(r.MoveY.ToString("0.###", c)).Append(',')
              .Append(r.CmdX.ToString("0.###", c)).Append(',').Append(r.CmdZ.ToString("0.###", c)).Append(',').Append(r.VelX.ToString("0.###", c)).Append(',')
              .Append(r.VelZ.ToString("0.###", c)).Append(',').Append(r.PosX.ToString("0.###", c)).Append(',').Append(r.PosZ.ToString("0.###", c)).Append(',')
              .Append(r.Facing.ToString("0.#", c)).Append(',').Append(r.Control ? '1' : '0');
            return sb.ToString();
        }

        private void OnApplicationQuit() => Close();

        private void OnDestroy() => Close();

        private void Close()
        {
            if (_writer == null) return;
            Flush();
            _writer.Dispose();
            _writer = null;
            Debug.Log("[MoveTrace] closed " + FilePath + " rows=" + RowsWritten);
        }
    }
}
