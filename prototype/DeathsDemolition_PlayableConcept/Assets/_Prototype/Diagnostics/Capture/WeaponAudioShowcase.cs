#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DD.Prototype.Audio;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-097 K3 (C-00 08.10) · the weapon-audio acceptance video's driver. DEVELOPMENT PLAYER / EDITOR ONLY (whole file under the
    /// M-55 guard); without <c>-ddWeaponShowcase</c> it creates nothing (<see cref="TryStart"/>: one scan of the argument array).
    ///
    /// It plays a fixed ~56 s script in top-down so the user hears the WP-097 space work: three zones — an OPEN court next to a
    /// wall (slapback), a CORRIDOR, an INTERIOR (Room when the world resolves one, else the smallest Hall) — and in each zone five
    /// classic weapons at their own RPM: ar_1, smg_1, lmg_1 held ~1.2 s then 1.5 s of release tail; sg_1 three single shots,
    /// sr_1 two, each followed by 1.5 s of tail. A zone change is a teleport + 0.5 s of silence. A small OnGUI label shows the
    /// zone and the weapon. It pairs with S-01's td-capture video (<c>-ddTdCapture &lt;out&gt; -ddTdCaptureMode video</c>) but does not
    /// reference it: the script starts when the capture agent writes <c>&lt;out&gt;/video/sync.json</c> (its t0).
    ///
    /// Command line:
    ///   -ddWeaponShowcase                arm (a switch, no value)
    ///   -ddWeaponShowcaseOut &lt;dir&gt;      artefact folder (default: the -ddTdCapture folder; neither → refused, exit 2)
    ///   -ddWeaponShowcaseZones &lt;file&gt;   optional candidate override, one "zone key x z [aimDeg]" per line (zone: avlu |
    ///                                    koridor | ic-mekan; aimDeg = world compass, 0 = +Z north, 90 = +X east); a zone the
    ///                                    file names loses its built-in candidates, the others keep theirs
    ///   -ddWeaponShowcaseRoomVolume      opt-in, OFF by default: when no interior candidate resolves as Room, put a dev-only
    ///                                    <see cref="AcousticVolume"/> (Room) on the chosen built-in interior's JSON box. Flagged
    ///                                    ROOM-VOLUME-DEV in the label, the events and the summary. Not needed on main since WP-092
    ///                                    (S-08 10.10): acoustic-spaces-v6.json lists R2_EastPavilion, R3_Kura and R4_TeaHouse as Room
    ///
    /// Before t0 (during the capture's warm-up — pass -ddTdWarmupSeconds 20) every candidate point is visited once: teleport onto
    /// the NavMesh within 2 m (the td-capture / PilotPerfProbe rule), 1.1 s for the director's <see cref="AcousticProbe"/> to
    /// refresh twice (its hysteresis), then the space the game itself resolves there (<see cref="AcousticSpaces.At"/>) and the
    /// probe's reflector count are recorded. A zone takes the candidate whose resolved space matches it (open court: the most
    /// walls, no roof), so the video never claims a space the game did not hear; a fallback is flagged in the events and the
    /// summary (ZONE-MISMATCH, OPEN-NO-WALL and NO-NAVMESH count as issues; ROOM-FALLBACK-HALL — the fallback the K3 brief
    /// allows — and ROOM-VOLUME-DEV are recorded, not counted). The aim is the farthest probe ray (away from the nearest wall)
    /// unless the zone file gives one.
    ///
    /// Safety: refuses without a sandbox (-ddProfilePath or a process-local UI data root) and with -ddQaBot (one driver only). The
    /// player is made invulnerable (PlayerHealth.DiagnosisInvulnerable), spawning is held and the field cleared every 0.5 s
    /// (EnemySpawnDirector.SpawningEnabledDev, the td-capture hold); weapons are put in hand through <see cref="WeaponQaHooks"/>
    /// (full magazine, no gameplay path). Input goes through its own <see cref="IInputSource"/> (registry + motor + aim); the
    /// previous source and the selected weapon are given back at the end. No hot path is touched: everything here runs only
    /// with the flag.
    ///
    /// Output (written at the end, and on quit if the app goes first):
    ///   showcase-events.tsv   t · zone · weapon · event · wav_s · realtime · detail. t is on the MP4 timeline:
    ///                         (AudioOutputCapture.FramesCaptured − F0) / rate with F0 = video/sync.json audio.framesCapturedAtT0
    ///                         (what tdmux lays at --wav-offset); without sync.json t = WAV seconds; without the WAV tap,
    ///                         realtimeSinceStartup. The header line says which.
    ///   showcase-summary.json zones (candidates, choice, flags), blocks, noFire, result.
    /// Each fire block compares WeaponFireAudio.FireVoices before / after (source in the recording): no increase → a NO-FIRE event.
    /// Exit (only when no td-capture is armed; otherwise the capture agent ends the Player): 0 · 2 refused · 3 scene not ready ·
    /// 4 a NO-FIRE / equip failure / zone fallback.
    /// </summary>
    public sealed class WeaponAudioShowcase : MonoBehaviour
    {
        public const string Flag = "-ddWeaponShowcase";
        public const string OutFlag = "-ddWeaponShowcaseOut";
        public const string ZonesFlag = "-ddWeaponShowcaseZones";
        public const string RoomVolumeFlag = "-ddWeaponShowcaseRoomVolume";
        private const string TdOutFlag = "-ddTdCapture";
        private const string QaBotFlag = "-ddQaBot";

        private const float SceneTimeoutSeconds = 180f;
        private const float IntroSeconds = 2f;
        private const float NavMeshSampleMetres = 2f;
        private const float ScanSettleSeconds = 1.1f;
        private const float SyncWaitSeconds = 90f;
        private const float LeadInSeconds = 1.0f;
        private const float ZoneSilenceSeconds = 0.5f;
        private const float EquipSeconds = 0.7f;
        private const float AutoHoldSeconds = 1.2f;
        private const float TailSeconds = 1.5f;
        private const float TapHoldSeconds = 0.06f;
        private const float TapGapPadSeconds = 0.05f;
        private const float HoldEverySeconds = 0.5f;
        private const float SafetyQuitSeconds = 30f;
        private const float ListenerHeight = 1.4f;

        private static readonly string[] WeaponKeys = { "ar_1", "smg_1", "sg_1", "lmg_1", "sr_1" };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private enum ZoneKind { Open, Corridor, Interior }

        private sealed class Candidate
        {
            public string Key;
            public float X, Z;
            public float AimDeg = float.NaN;
            public bool Scanned, NavOk;
            public Vector3 Pos;
            public float Snap;
            public AcousticSpace Space, Guess;
            public int Walls;
            public bool Roof;
            public float RoofMetres = float.PositiveInfinity;
            public float[] Dist;
            public float[] Box;   // interior JSON box: x0, z0, x1, z1, floor, ceiling (built-in candidates only)
        }

        private sealed class Zone
        {
            public string Key, Label;
            public ZoneKind Kind;
            public readonly List<Candidate> Candidates = new List<Candidate>();
            public Candidate Chosen;
            public string Flag = "unscanned";
            public Vector3 AimWorld = Vector3.forward;
        }

        private struct ShowEvent
        {
            public double Realtime;
            public long Frames;
            public string Zone, Weapon, Kind, Detail;
        }

        private string _out;
        private bool _tdArmed;
        private string _zonesFile;
        private bool _roomVolume;
        private AcousticVolume _devVolume;
        private readonly List<Zone> _zones = new List<Zone>();
        private readonly List<ShowEvent> _events = new List<ShowEvent>();
        private readonly ShowcaseInput _input = new ShowcaseInput();
        private IInputSource _prevInput;
        private ClassicWeaponId _prevSelected;
        private bool _driving;
        private float _nextHold;
        private bool _written;
        private string _label, _subLabel;
        private GUIStyle _style;
        private int _blocks, _noFire, _noShot, _equipFailed, _zoneIssues, _pushed;
        private bool _syncFound, _syncLate;
        private double _scenarioStartRt = -1d, _scenarioEndRt = -1d;
        private string _stopReason = "running";
        private string _curZone = "-", _curWeapon = "-";

        // shot tracking (one weapon at a time)
        private WeaponController _trackWc;
        private int _lastSerial;
        private readonly List<double> _shotTimes = new List<double>();

        private static MethodInfo s_snap;
        private static bool s_snapLooked;

        // ---------------------------------------------------------------- boot

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            TryStart(Environment.GetCommandLineArgs());
        }

        /// <summary>Starts the driver only when <paramref name="args"/> carries <c>-ddWeaponShowcase</c>; returns whether it did anything.</summary>
        public static bool TryStart(string[] args)
        {
            if (args == null || Array.IndexOf(args, Flag) < 0)
            {
                return false;
            }

            string outDir = Arg(args, OutFlag);
            if (string.IsNullOrEmpty(outDir)) outDir = Arg(args, TdOutFlag);
            if (string.IsNullOrEmpty(outDir) || !Path.IsPathRooted(outDir))
            {
                Debug.LogError("[WeaponShowcase] refused: no absolute output folder (" + OutFlag + " or " + TdOutFlag + ") — nothing is written to the user's folders");
                Quit(2);
                return true;
            }

            if (Array.IndexOf(args, QaBotFlag) >= 0)
            {
                WriteError(outDir, "refused: " + QaBotFlag + " is also armed — the showcase drives the player alone");
                Quit(2);
                return true;
            }

            // M-55 (C-00 30.09): a driver that plays a run only starts inside a sandbox.
            if (!Application.isEditor && !ProfileStore.HasPathOverride && !UiDataPaths.IsProcessLocal)
            {
                WriteError(outDir, "refused: no sandbox (pass -ddProfilePath and -ddUiDataRoot)");
                Quit(2);
                return true;
            }

            var go = new GameObject("WeaponAudioShowcase");
            DontDestroyOnLoad(go);
            WeaponAudioShowcase s = go.AddComponent<WeaponAudioShowcase>();
            s._out = outDir;
            s._tdArmed = Array.IndexOf(args, TdOutFlag) >= 0;
            s._zonesFile = Arg(args, ZonesFlag);
            s._roomVolume = Array.IndexOf(args, RoomVolumeFlag) >= 0;
            Debug.Log("[WeaponShowcase] armed out=" + outDir + " tdCapture=" + s._tdArmed + (s._zonesFile != null ? " zones=" + s._zonesFile : ""));
            return true;
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
            }

            return null;
        }

        // ---------------------------------------------------------------- zones

        private void BuildZones()
        {
            // V6 map (World_V6_Greybox), +Z north, +X east (td-capture-poses.txt axes). Priority order inside a zone.
            Zone open = AddZone("avlu", "AÇIK AVLU", ZoneKind.Open);
            AddCandidate(open, "inner_court", 0f, 31f);        // between R6 chumon (z 23) and R1 main hall's south wall (z 39); cloisters x ±20
            AddCandidate(open, "rgate_west", 23f, 4f);         // garden path end, 4 m west of the R7 court wall (x 27)
            AddCandidate(open, "lantern_axis", 0f, 11f);       // td pose 2: chumon 7 m north
            AddCandidate(open, "spawn_courtyard", 0f, -3f);    // td pose 1: the plaza

            Zone corridor = AddZone("koridor", "KORİDOR", ZoneKind.Corridor);
            AddCandidate(corridor, "k3_cloister_west", -22f, 29f);   // acoustic-spaces-v6 K3_CloisterWest (x −24..−20, z 22..36)
            AddCandidate(corridor, "k1_west_kairo", -33f, 2f);       // K1_WestKairo, closed (x −35.6..−30.4, z −16..20)
            AddCandidate(corridor, "k3_cloister_east", 22f, 29f);    // K3_CloisterEast
            AddCandidate(corridor, "k6_rear_kairo", 0f, 52.5f);      // K6_RearKairo

            Zone interior = AddZone("ic-mekan", "İÇ MEKÂN", ZoneKind.Interior);
            AddCandidate(interior, "r4_teahouse", -32f, -36f).Box = new[] { -35f, -39f, -29f, -33f, 0.45f, 4.05f };       // R4_TeaHouse 36 m² (JSON: Room since WP-092) — the smallest interior
            AddCandidate(interior, "r2_east_pavilion", 35f, 28f).Box = new[] { 30.8f, 24.8f, 39.2f, 31.2f, 0.25f, 4.05f };  // R2_EastPavilion 53.8 m² (JSON: Room)
            AddCandidate(interior, "r3_kura", -39f, 40f).Box = new[] { -44f, 36f, -34f, 44f, 0.3f, 4.3f };                  // R3_Kura 80 m² (JSON: Room)

            if (string.IsNullOrEmpty(_zonesFile)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_zonesFile);
            }
            catch (Exception e)
            {
                Note("-", "-", "zones-file-unreadable", _zonesFile + ": " + e.Message);
                return;
            }

            var cleared = new HashSet<string>();
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                string[] tok = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length == 0) continue;
                Zone z = _zones.Find(q => q.Key == tok[0]);
                float x, zz, aim = float.NaN;
                if (z == null || tok.Length < 4 || !float.TryParse(tok[2], NumberStyles.Float, Inv, out x) || !float.TryParse(tok[3], NumberStyles.Float, Inv, out zz)
                    || (tok.Length > 4 && !float.TryParse(tok[4], NumberStyles.Float, Inv, out aim)))
                {
                    Note("-", "-", "zones-file-line-skipped", "line " + (n + 1) + ": '" + lines[n].Trim() + "'");
                    continue;
                }

                if (cleared.Add(z.Key)) z.Candidates.Clear();
                Candidate c = AddCandidate(z, tok[1], x, zz);
                c.AimDeg = aim;
            }
        }

        private Zone AddZone(string key, string label, ZoneKind kind)
        {
            var z = new Zone { Key = key, Label = label, Kind = kind };
            _zones.Add(z);
            return z;
        }

        private static Candidate AddCandidate(Zone z, string key, float x, float zz)
        {
            var c = new Candidate { Key = key, X = x, Z = zz };
            z.Candidates.Add(c);
            return c;
        }

        // ---------------------------------------------------------------- main script

        private IEnumerator Start()
        {
            try
            {
                Directory.CreateDirectory(_out);
            }
            catch (Exception e)
            {
                Debug.LogError("[WeaponShowcase] cannot create " + _out + ": " + e.Message);
                Quit(2);
                yield break;
            }

            Application.quitting += OnQuitting;
            BuildZones();

            float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (!RunPlayable())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Finish("scene-not-ready");
                    if (!_tdArmed) Quit(3);
                    yield break;
                }

                yield return null;
            }

            if (CameraModeService.Current != CameraMode.TopDown)
            {
                Note("-", "-", "REFUSED", "camera mode " + CameraModeService.Current + " (pass -ddCameraMode topdown)");
                Finish("not-topdown");
                if (!_tdArmed) Quit(2);
                yield break;
            }

            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = true;   // evidence only
            _prevSelected = WeaponRegistry.SelectedWeapon;
            _prevInput = InputSourceRegistry.Current;
            InputSourceRegistry.SetCurrent(_input);
            rig.Motor.SetInputSource(_input);
            rig.Aim.SetInputSource(_input);
            _driving = true;
            HoldSpawns();
            yield return new WaitForSecondsRealtime(IntroSeconds);

            // ---- scan (before t0)
            for (int i = 0; i < _zones.Count; i++)
            {
                yield return Scan(_zones[i]);
            }

            // ---- zone 1 pre-positioned, first weapon in hand, then wait for the video's t0
            int first = _zones.FindIndex(z => z.Chosen != null);
            bool firstInHand = false;
            if (first >= 0)
            {
                yield return EnterZone(_zones[first], false);
                firstInHand = Equip(_zones[first], WeaponKeys[0]);
                yield return Wait(EquipSeconds, false);
            }

            string sync = Path.Combine(_out, "video", "sync.json");
            if (_tdArmed)
            {
                _syncLate = File.Exists(sync);
                if (_syncLate) Note("-", "-", "WARN", "video t0 came before the scan ended — raise -ddTdWarmupSeconds (zone 1 may be cut)");
                float until = Time.realtimeSinceStartup + SyncWaitSeconds;
                while (!File.Exists(sync) && Time.realtimeSinceStartup < until) yield return Wait(0.1f, false);
                _syncFound = File.Exists(sync);
                if (!_syncFound) Note("-", "-", "WARN", "no video/sync.json after " + SyncWaitSeconds + " s — starting without the video clock");
            }

            _scenarioStartRt = Time.realtimeSinceStartupAsDouble;
            Note("-", "-", "scenario-start", "zones=" + _zones.Count + " weapons=" + string.Join(",", WeaponKeys));
            yield return Wait(LeadInSeconds, false);

            for (int i = 0; i < _zones.Count; i++)
            {
                Zone z = _zones[i];
                if (z.Chosen == null)
                {
                    Note(z.Key, "-", "zone-skipped", z.Flag);
                    continue;
                }

                if (i != first)
                {
                    yield return EnterZone(z, true);
                }

                for (int w = 0; w < WeaponKeys.Length; w++)
                {
                    yield return FireBlock(z, WeaponKeys[w], i == first && w == 0 && firstInHand);
                }
            }

            _scenarioEndRt = Time.realtimeSinceStartupAsDouble;
            Note("-", "-", "scenario-end", "seconds=" + F(_scenarioEndRt - _scenarioStartRt, 2));
            _label = null;
            Release();
            Finish("done");

            if (!_tdArmed)
            {
                yield return null;
                Quit(Issues() ? 4 : 0);
                yield break;
            }

            // The td-capture agent ends the Player after -ddTdVideoSeconds; a safety stop if it never does.
            yield return new WaitForSecondsRealtime(SafetyQuitSeconds);
            Debug.LogWarning("[WeaponShowcase] the capture agent did not end the Player " + SafetyQuitSeconds + " s after the script — quitting");
            Quit(Issues() ? 4 : 0);
        }

        private static bool RunPlayable()
        {
            Scene s = SceneManager.GetActiveScene();
            return s.name == PrototypeScenes.Run && s.isLoaded && RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting &&
                   PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null && WeaponRegistry.ActiveController != null;
        }

        private bool Issues()
        {
            return _noFire > 0 || _noShot > 0 || _equipFailed > 0 || _zoneIssues > 0;
        }

        // ---------------------------------------------------------------- scan

        private IEnumerator Scan(Zone z)
        {
            Candidate roomFallback = null, firstNav = null, best = null;
            for (int i = 0; i < z.Candidates.Count; i++)
            {
                Candidate c = z.Candidates[i];
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig == null) break;
                c.Scanned = true;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(new Vector3(c.X, rig.transform.position.y, c.Z), out hit, NavMeshSampleMetres, NavMesh.AllAreas))
                {
                    Note(z.Key, "-", "scan", c.Key + " (" + F(c.X, 1) + ", " + F(c.Z, 1) + ") navmesh-miss");
                    continue;
                }

                c.NavOk = true;
                c.Pos = hit.position;
                c.Snap = new Vector2(hit.position.x - c.X, hit.position.z - c.Z).magnitude;
                rig.Teleport(hit.position, rig.transform.rotation);
                SnapCamera();
                yield return Wait(ScanSettleSeconds, false);
                ReadSpace(c);
                Note(z.Key, "-", "scan", c.Key + " " + V(c.Pos) + " snap=" + F(c.Snap, 2) + " space=" + c.Space + " guess=" + c.Guess + " walls=" + c.Walls +
                                        " roof=" + (c.Roof ? F(c.RoofMetres, 1) : "none"));

                if (firstNav == null) firstNav = c;
                if (z.Kind == ZoneKind.Open)
                {
                    if (c.Space == AcousticSpace.Open && !c.Roof && (best == null || c.Walls > best.Walls)) best = c;
                }
                else if (z.Kind == ZoneKind.Corridor)
                {
                    if (c.Space == AcousticSpace.Corridor) { best = c; break; }
                }
                else
                {
                    if (c.Space == AcousticSpace.Room) { best = c; break; }
                    if (c.Space == AcousticSpace.Hall && roomFallback == null) roomFallback = c;
                }
            }

            if (best != null)
            {
                z.Chosen = best;
                z.Flag = z.Kind == ZoneKind.Open && best.Walls == 0 ? "OPEN-NO-WALL" : "ok";
            }
            else if (roomFallback != null)
            {
                z.Chosen = roomFallback;
                z.Flag = "ROOM-FALLBACK-HALL";
                if (_roomVolume && roomFallback.Box != null)
                {
                    yield return DevRoomVolume(z, roomFallback);
                }
            }
            else if (firstNav != null)
            {
                z.Chosen = firstNav;
                z.Flag = "ZONE-MISMATCH";
            }
            else
            {
                z.Flag = "NO-NAVMESH";
            }

            if (z.Flag != "ok" && z.Flag != "ROOM-FALLBACK-HALL" && z.Flag != "ROOM-VOLUME-DEV") _zoneIssues++;
            if (z.Chosen != null) z.AimWorld = AimFor(z.Chosen);
            Note(z.Key, "-", "zone-select", (z.Chosen != null ? z.Chosen.Key + " space=" + z.Chosen.Space + " walls=" + z.Chosen.Walls : "none") +
                                            " flag=" + z.Flag + " aim=" + V(z.AimWorld));
        }

        /// <summary>-ddWeaponShowcaseRoomVolume: a dev Room volume on the interior's JSON box; kept only if the game then resolves Room.</summary>
        private IEnumerator DevRoomVolume(Zone z, Candidate c)
        {
            float[] b = c.Box;
            var go = new GameObject("WeaponShowcase.DevRoomVolume");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3((b[0] + b[2]) * 0.5f, (b[4] + b[5]) * 0.5f, (b[1] + b[3]) * 0.5f);
            go.SetActive(false);
            AcousticVolume v = go.AddComponent<AcousticVolume>();
            v.Enclosure = AcousticSpace.Room;
            v.Size = new Vector3(b[2] - b[0], b[5] - b[4], b[3] - b[1]);
            go.SetActive(true);
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig != null) rig.Teleport(c.Pos, rig.transform.rotation);
            SnapCamera();
            yield return Wait(ScanSettleSeconds, false);
            ReadSpace(c);
            if (c.Space == AcousticSpace.Room)
            {
                _devVolume = v;
                z.Flag = "ROOM-VOLUME-DEV";
                Note(z.Key, "-", "WARN", "dev Room volume on " + c.Key + " box x " + F(b[0], 1) + ".." + F(b[2], 1) + " z " + F(b[1], 1) + ".." + F(b[3], 1) +
                                         " — the shipping game rings Hall here until S-05 adds a Room row / volume");
            }
            else
            {
                Note(z.Key, "-", "WARN", "dev Room volume did not take (space " + c.Space + ") — removed");
                Destroy(go);
            }
        }

        private static void ReadSpace(Candidate c)
        {
            PrototypeAudioDirector d = PrototypeAudioDirector.Instance;
            AcousticProbe probe = d != null ? d.Probe : null;
            PlayerRig rig = PlayerRegistry.Primary;
            Vector3 ear = probe != null && probe.HasData ? probe.Origin
                : (rig != null ? rig.transform.position : c.Pos) + Vector3.up * ListenerHeight;
            c.Space = AcousticSpaces.At(ear);
            if (probe != null && probe.HasData)
            {
                c.Guess = probe.Guess;
                c.Walls = probe.ValidCount;
                c.Roof = probe.Roof;
                c.RoofMetres = probe.RoofDistance;
                c.Dist = (float[])probe.Distances.Clone();
            }
        }

        /// <summary>The zone file's compass aim, else the farthest probe ray (away from the nearest wall), else north.</summary>
        private static Vector3 AimFor(Candidate c)
        {
            if (!float.IsNaN(c.AimDeg))
            {
                float r = c.AimDeg * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
            }

            if (c.Dist == null) return Vector3.forward;
            int far = 0;
            for (int i = 1; i < c.Dist.Length; i++)
            {
                if (c.Dist[i] > c.Dist[far]) far = i;
            }

            Vector3 d = AcousticProbe.Direction(far);
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        // ---------------------------------------------------------------- zones / weapons

        private IEnumerator EnterZone(Zone z, bool silence)
        {
            _curZone = z.Key;
            _curWeapon = "-";
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null) yield break;
            Candidate c = z.Chosen;
            _input.SetAim(z.AimWorld);
            rig.Teleport(c.Pos, Quaternion.LookRotation(z.AimWorld, Vector3.up));
            SnapCamera();
            _label = z.Label;
            _subLabel = SubLabel(z);
            Note(z.Key, "-", "zone", c.Key + " " + V(c.Pos) + " space=" + c.Space + " flag=" + z.Flag + " label='" + z.Label + "'");
            yield return null;
            yield return null;
            SnapCamera();   // the capsule may settle a frame later (td-capture)
            Vector3 at = PlayerRegistry.Primary != null ? PlayerRegistry.Primary.transform.position : c.Pos;
            float off = new Vector2(at.x - c.Pos.x, at.z - c.Pos.z).magnitude;
            if (off > 0.5f)
            {
                _pushed++;
                Note(z.Key, "-", "WARN", "player " + F(off, 2) + " m from the teleport point " + V(at));
            }

            if (silence) yield return Wait(ZoneSilenceSeconds, false);
        }

        /// <summary>The label's second line: the space the game resolved, the wall count in the open, a fallback in words.</summary>
        private static string SubLabel(Zone z)
        {
            Candidate c = z.Chosen;
            string s = c.Space.ToString();
            if (z.Kind == ZoneKind.Open) s += " · " + c.Walls + " duvar";
            switch (z.Flag)
            {
                case "ok": break;
                case "ROOM-FALLBACK-HALL": s += " · Room satırı yok (Hall)"; break;
                case "ROOM-VOLUME-DEV": s += " · geçici dev hacmi"; break;
                default: s += " · " + z.Flag; break;
            }

            return s;
        }

        private bool Equip(Zone z, string key)
        {
            _curWeapon = key;
            ClassicWeaponId id;
            if (!WeaponIdMigration.TryGetRuntimeId(key, out id) || !WeaponQaHooks.TrySetClassicWeapon(id))
            {
                _equipFailed++;
                Note(z.Key, key, "EQUIP-FAILED", "not an enabled classic weapon, or a Special is in hand");
                return false;
            }

            WeaponDefinition def = Arsenal.Get(id);
            Note(z.Key, key, "equip", def != null ? "mode=" + def.fire.fireMode + " rpm=" + F(def.fire.rpm, 1) : "no definition");
            return true;
        }

        private IEnumerator FireBlock(Zone z, string key, bool alreadyInHand)
        {
            _curWeapon = key;
            _label = z.Label + "  ·  " + key;
            if (!alreadyInHand)
            {
                if (!Equip(z, key)) yield break;
                yield return Wait(EquipSeconds, false);
            }

            ClassicWeaponId id;
            WeaponIdMigration.TryGetRuntimeId(key, out id);
            WeaponDefinition def = Arsenal.Get(id);
            WeaponController wc = WeaponRegistry.ActiveController;
            PrototypeAudioDirector dir = PrototypeAudioDirector.Instance;
            WeaponFireAudio f = dir != null ? dir.WeaponFire : null;
            AudioOutputCapture cap = AudioOutputCapture.Armed;
            if (wc == null)
            {
                _noShot++;
                Note(z.Key, key, "NO-FIRE", "no weapon controller");
                yield break;
            }

            int voices0 = f != null ? f.FireVoices : 0, refused0 = f != null ? f.FireRefused : 0, slaps0 = f != null ? f.SlapsPlayed : 0;
            int slapRef0 = f != null ? f.SlapsRefused : 0, rel0 = f != null ? f.Releases : 0, echo0 = f != null ? f.EchoesPlayed : 0, bed0 = f != null ? f.BedStarts : 0;
            long fc0 = cap != null ? cap.FramesCaptured : -1;
            _trackWc = wc;
            _lastSerial = wc.ShotSerial;
            int serial0 = _lastSerial;
            _shotTimes.Clear();

            bool auto = def == null || def.fire.fireMode != WeaponFireMode.Single;
            if (auto)
            {
                _input.Fire = true;
                Note(z.Key, key, "fire-start", "hold " + F(AutoHoldSeconds, 2) + " s");
                yield return Wait(AutoHoldSeconds, true);
                _input.Fire = false;
                Note(z.Key, key, "fire-stop", "shots so far " + (wc.ShotSerial - serial0));
            }
            else
            {
                int taps = key == "sr_1" ? 2 : 3;
                float cycle = def != null && def.fire.rpm > 0f ? 60f / def.fire.rpm : 1f;
                for (int t = 0; t < taps; t++)
                {
                    _input.Fire = true;
                    Note(z.Key, key, "tap", (t + 1) + "/" + taps);
                    yield return Wait(TapHoldSeconds, true);
                    _input.Fire = false;
                    if (t + 1 < taps) yield return Wait(Mathf.Max(0f, cycle + TapGapPadSeconds - TapHoldSeconds), true);
                }
            }

            yield return Wait(TailSeconds, true);
            Note(z.Key, key, "tail-end", F(TailSeconds, 2) + " s after the trigger");

            int shots = wc.ShotSerial - serial0;
            int voices = f != null ? f.FireVoices - voices0 : -1;
            string rpm = _shotTimes.Count >= 2 ? F((_shotTimes.Count - 1) * 60.0 / Math.Max(1e-6, _shotTimes[_shotTimes.Count - 1] - _shotTimes[0]), 1) : "n/a";
            string wav = cap != null && fc0 >= 0 ? F((cap.FramesCaptured - fc0) / (double)Math.Max(1, AudioSettings.outputSampleRate), 2) : "not-armed";
            var sb = new StringBuilder();
            sb.Append("shots=").Append(shots).Append(" voices=").Append(voices);
            if (f != null)
            {
                sb.Append(" refused=").Append(f.FireRefused - refused0).Append(" slaps=").Append(f.SlapsPlayed - slaps0).Append(" slapsRefused=").Append(f.SlapsRefused - slapRef0)
                  .Append(" lastSlapTaps=").Append(f.LastSlapCount).Append(" slap0=").Append(F(f.LastSlapDelay0 * 1000f, 0)).Append("ms/").Append(F(f.LastSlapGainDb0, 1)).Append("dB")
                  .Append(" bedStarts=").Append(f.BedStarts - bed0).Append(" releases=").Append(f.Releases - rel0).Append(" echoes=").Append(f.EchoesPlayed - echo0)
                  .Append(" space=").Append(f.LastSpace).Append(" tail=").Append(f.LastTail).Append(" releaseTail=").Append(f.LastReleaseTail).Append(" voice=").Append(f.LastVoice);
            }

            sb.Append(" rpmNominal=").Append(def != null ? F(def.fire.rpm, 0) : "?").Append(" rpmMeasured=").Append(rpm).Append(" wavAdvance=").Append(wav);
            _blocks++;
            Note(z.Key, key, "block", sb.ToString());
            Debug.Log("[WeaponShowcase] block " + z.Key + " " + key + " " + sb);
            if (shots <= 0)
            {
                _noShot++;
                Note(z.Key, key, "NO-FIRE", "the weapon fired no shot (shots=0) — input / equip / CanFire");
            }
            else if (voices <= 0)
            {
                _noFire++;
                Note(z.Key, key, "NO-FIRE", "shots=" + shots + " but WeaponFireAudio.FireVoices did not increase (voices=" + voices + ")");
            }

            if (cap != null && fc0 >= 0 && cap.FramesCaptured <= fc0)
            {
                Note(z.Key, key, "WARN", "the WAV tap did not advance during the block (FramesCaptured " + fc0 + ")");
            }

            _trackWc = null;
        }

        /// <summary>Realtime wait (the game is not slowed); logs each shot the weapon fired meanwhile when <paramref name="track"/>.</summary>
        private IEnumerator Wait(float seconds, bool track)
        {
            float end = Time.realtimeSinceStartup + seconds;
            do
            {
                if (track) TrackShots();
                yield return null;
            }
            while (Time.realtimeSinceStartup < end);

            if (track) TrackShots();
        }

        private void TrackShots()
        {
            WeaponController wc = _trackWc;
            if (wc == null) return;
            int s = wc.ShotSerial;
            if (s == _lastSerial) return;
            double now = Time.realtimeSinceStartupAsDouble;
            int k = s - _lastSerial;
            _lastSerial = s;
            for (int i = 0; i < k; i++) _shotTimes.Add(now);
            Note(_curZone, _curWeapon, "shot", "serial=" + s + (k > 1 ? " (+" + k + " this frame)" : "") + " ammo=" + (wc.Classic != null ? wc.Classic.Ammo : -1));
        }

        // ---------------------------------------------------------------- per frame (only while armed)

        private void Update()
        {
            if (!_driving) return;
            if (!ReferenceEquals(InputSourceRegistry.Current, _input)) InputSourceRegistry.SetCurrent(_input);
            if (Time.realtimeSinceStartup >= _nextHold)
            {
                _nextHold = Time.realtimeSinceStartup + HoldEverySeconds;
                HoldSpawns();
            }
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_label)) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.024f)),
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                    padding = new RectOffset(14, 14, 6, 6)
                };
                _style.normal.textColor = Color.white;
            }

            string text = string.IsNullOrEmpty(_subLabel) ? _label : _label + "\n" + _subLabel;
            var content = new GUIContent(text);
            Vector2 size = _style.CalcSize(content);
            GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.07f, size.x, size.y), content, _style);
        }

        /// <summary>Spawning off and the field cleared (the td-capture hold, EnemySpawnDirector.SpawningEnabledDev).</summary>
        private static void HoldSpawns()
        {
            EnemySpawnDirector dir = EnemySpawnDirector.Instance;
            if (dir == null) return;
            if (dir.SpawningEnabledDev) dir.SpawningEnabledDev = false;
            if (dir.ActiveCount > 0) dir.ClearAllEnemies();
        }

        /// <summary>S-01's dev seam PrototypeCameraRig.SnapTopDownDev (s01-r3-td-capture) when the build has it; else the rig's own follow.</summary>
        private static void SnapCamera()
        {
            PrototypeCameraRig cam = PrototypeCameraRig.Active;
            if (cam == null) return;
            if (!s_snapLooked)
            {
                s_snapLooked = true;
                s_snap = typeof(PrototypeCameraRig).GetMethod("SnapTopDownDev", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            }

            if (s_snap != null) s_snap.Invoke(cam, null);
        }

        private void Release()
        {
            if (!_driving) return;
            _driving = false;
            _input.Fire = false;
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig != null)
            {
                rig.Motor.SetInputSource(null);
                rig.Aim.SetInputSource(null);
            }

            if (ReferenceEquals(InputSourceRegistry.Current, _input)) InputSourceRegistry.SetCurrent(_prevInput);
            if (_devVolume != null) Destroy(_devVolume.gameObject);
            WeaponRegistry.SelectedWeapon = _prevSelected;   // the selection TrySetClassicWeapon wrote (ders-test-secim-sizintisi)
        }

        private void OnDestroy()
        {
            Application.quitting -= OnQuitting;
            Release();
        }

        private void OnQuitting()
        {
            if (_written) return;
            Note("-", "-", "app-quitting", _scenarioEndRt < 0d ? "the Player is ending before the script finished" : "after the script");
            Finish(_scenarioEndRt < 0d ? "app-quitting" : "done");
        }

        // ---------------------------------------------------------------- events / files

        private void Note(string zone, string weapon, string kind, string detail)
        {
            AudioOutputCapture cap = AudioOutputCapture.Armed;
            _events.Add(new ShowEvent
            {
                Realtime = Time.realtimeSinceStartupAsDouble,
                Frames = cap != null ? cap.FramesCaptured : -1,
                Zone = zone, Weapon = weapon, Kind = kind, Detail = detail ?? ""
            });
            if (kind != "shot") Debug.Log("[WeaponShowcase] " + zone + " " + weapon + " " + kind + " " + detail);
        }

        private void Finish(string reason)
        {
            if (_written) return;
            _written = true;
            _stopReason = reason;

            // The clock: the video's t0 from S-01's sync.json when there is one.
            long f0 = -1;
            int rate = AudioSettings.outputSampleRate;
            double t0Rt = double.NaN;
            string syncPath = Path.Combine(_out, "video", "sync.json");
            try
            {
                if (File.Exists(syncPath))
                {
                    string s = File.ReadAllText(syncPath);
                    Match m = Regex.Match(s, "\"framesCapturedAtT0\"\\s*:\\s*(-?\\d+)");
                    if (m.Success) f0 = long.Parse(m.Groups[1].Value, Inv);
                    m = Regex.Match(s, "\"sampleRate\"\\s*:\\s*(\\d+)");
                    if (m.Success) rate = int.Parse(m.Groups[1].Value, Inv);
                    m = Regex.Match(s, "\"realtimeSinceStartup\"\\s*:\\s*(-?[0-9.]+)");
                    if (m.Success) t0Rt = double.Parse(m.Groups[1].Value, Inv);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WeaponShowcase] sync.json unreadable: " + e.Message);
            }

            bool armed = AudioOutputCapture.Armed != null;
            string clock = armed && f0 >= 0 ? "video-wav" : !double.IsNaN(t0Rt) ? "video-realtime" : armed ? "wav" : "realtime";
            var tsv = new StringBuilder();
            tsv.Append("# WP-097 K3 weapon audio showcase · clock=").Append(clock).Append(" · sampleRate=").Append(rate)
               .Append(" · F0=").Append(f0).Append(" (video/sync.json audio.framesCapturedAtT0) · t0Realtime=").Append(double.IsNaN(t0Rt) ? "none" : F(t0Rt, 6)).Append('\n');
            tsv.Append("# t: video-wav = (FramesCaptured − F0)/rate, the MP4 second (tdmux --wav-offset F0/rate); video-realtime = realtime − t0Realtime; ")
               .Append("wav = FramesCaptured/rate; realtime = realtimeSinceStartup. Stamped when the game raised the event; the sound leaves ≤ one DSP buffer later.\n");
            tsv.Append("t\tzone\tweapon\tevent\twav_s\trealtime\tdetail\n");
            for (int i = 0; i < _events.Count; i++)
            {
                ShowEvent e = _events[i];
                double t;
                if (clock == "video-wav" && e.Frames >= 0) t = (e.Frames - f0) / (double)rate;
                else if (clock == "video-wav" || clock == "video-realtime") t = e.Realtime - t0Rt;
                else if (clock == "wav" && e.Frames >= 0) t = e.Frames / (double)rate;
                else t = e.Realtime;
                tsv.Append(F(t, 3)).Append('\t').Append(e.Zone).Append('\t').Append(e.Weapon).Append('\t').Append(e.Kind).Append('\t')
                   .Append(e.Frames >= 0 ? F(e.Frames / (double)rate, 3) : "").Append('\t').Append(F(e.Realtime, 3)).Append('\t')
                   .Append(e.Detail.Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
            }

            var js = new StringBuilder();
            js.Append("{\n  \"wp\": \"WP-097 K3\",\n  \"tool\": \"WeaponAudioShowcase\",\n");
            js.Append("  \"result\": ").Append(Str(Issues() || reason != "done" ? "issues" : "ok")).Append(", \"stopReason\": ").Append(Str(_stopReason)).Append(",\n");
            js.Append("  \"clock\": ").Append(Str(clock)).Append(", \"sampleRate\": ").Append(rate).Append(", \"framesCapturedAtT0\": ").Append(f0)
              .Append(", \"tdCaptureArmed\": ").Append(Bool(_tdArmed)).Append(", \"syncFound\": ").Append(Bool(_syncFound)).Append(", \"syncBeforeScanEnd\": ").Append(Bool(_syncLate))
              .Append(", \"audioArmed\": ").Append(Bool(armed)).Append(", \"roomVolumeDev\": ").Append(Bool(_devVolume != null)).Append(",\n");
            js.Append("  \"scenarioSeconds\": ").Append(_scenarioStartRt >= 0d && _scenarioEndRt >= 0d ? F(_scenarioEndRt - _scenarioStartRt, 2) : "null")
              .Append(", \"scenarioStartVideoT\": ").Append(_scenarioStartRt >= 0d && !double.IsNaN(t0Rt) ? F(_scenarioStartRt - t0Rt, 3) : "null").Append(",\n");
            js.Append("  \"weapons\": [");
            for (int i = 0; i < WeaponKeys.Length; i++) js.Append(i > 0 ? ", " : "").Append(Str(WeaponKeys[i]));
            js.Append("],\n  \"blocks\": ").Append(_blocks).Append(", \"noFire\": ").Append(_noFire).Append(", \"noShot\": ").Append(_noShot)
              .Append(", \"equipFailed\": ").Append(_equipFailed).Append(", \"zoneIssues\": ").Append(_zoneIssues).Append(", \"pushedOffTeleport\": ").Append(_pushed).Append(",\n");
            js.Append("  \"zones\": [\n");
            for (int i = 0; i < _zones.Count; i++)
            {
                Zone z = _zones[i];
                js.Append("    {\"key\": ").Append(Str(z.Key)).Append(", \"label\": ").Append(Str(z.Label)).Append(", \"kind\": ").Append(Str(z.Kind.ToString()))
                  .Append(", \"flag\": ").Append(Str(z.Flag)).Append(", \"chosen\": ").Append(Str(z.Chosen != null ? z.Chosen.Key : null))
                  .Append(", \"aim\": [").Append(F(z.AimWorld.x, 3)).Append(", ").Append(F(z.AimWorld.z, 3)).Append("], \"candidates\": [");
                for (int c = 0; c < z.Candidates.Count; c++)
                {
                    Candidate q = z.Candidates[c];
                    js.Append(c > 0 ? ", " : "").Append("{\"key\": ").Append(Str(q.Key)).Append(", \"x\": ").Append(F(q.X, 2)).Append(", \"z\": ").Append(F(q.Z, 2))
                      .Append(", \"scanned\": ").Append(Bool(q.Scanned)).Append(", \"navmesh\": ").Append(Bool(q.NavOk));
                    if (q.NavOk)
                    {
                        js.Append(", \"pos\": [").Append(F(q.Pos.x, 2)).Append(", ").Append(F(q.Pos.y, 2)).Append(", ").Append(F(q.Pos.z, 2)).Append("], \"snap\": ").Append(F(q.Snap, 2))
                          .Append(", \"space\": ").Append(Str(q.Space.ToString())).Append(", \"guess\": ").Append(Str(q.Guess.ToString())).Append(", \"walls\": ").Append(q.Walls)
                          .Append(", \"roofMetres\": ").Append(q.Roof ? F(q.RoofMetres, 2) : "null");
                    }

                    js.Append('}');
                }

                js.Append("]}").Append(i + 1 < _zones.Count ? ",\n" : "\n");
            }

            js.Append("  ]\n}\n");

            try
            {
                Directory.CreateDirectory(_out);
                File.WriteAllText(Path.Combine(_out, "showcase-events.tsv"), tsv.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(_out, "showcase-summary.json"), js.ToString(), new UTF8Encoding(false));
                Debug.Log("[WeaponShowcase] wrote showcase-events.tsv (" + _events.Count + " events, clock " + clock + ") + showcase-summary.json — blocks " + _blocks +
                          " noFire " + _noFire + " noShot " + _noShot + " zoneIssues " + _zoneIssues + " reason " + reason);
            }
            catch (Exception e)
            {
                Debug.LogError("[WeaponShowcase] output not written: " + e.Message);
            }
        }

        private static void WriteError(string outDir, string message)
        {
            Debug.LogError("[WeaponShowcase] " + message);
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "showcase-error.json"), "{\"wp\": \"WP-097 K3\", \"error\": " + Str(message) + "}\n");
            }
            catch (Exception e)
            {
                Debug.LogError("[WeaponShowcase] showcase-error.json not written: " + e.Message);
            }
        }

        private static string F(double v, int digits) => v.ToString("F" + digits, Inv);
        private static string V(Vector3 v) => "(" + F(v.x, 2) + ", " + F(v.y, 2) + ", " + F(v.z, 2) + ")";
        private static string Bool(bool b) => b ? "true" : "false";

        private static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char ch in s)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", Inv));
                else sb.Append(ch);
            }

            return sb.Append('"').ToString();
        }

        private static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }

        // ---------------------------------------------------------------- input

        /// <summary>The script's hands: the trigger on demand, stick aim in a fixed world direction, no movement.</summary>
        private sealed class ShowcaseInput : IInputSource
        {
            public bool Fire;
            private Vector3 _aimWorld = Vector3.forward;
            private bool _was;
            private int _frame = -1;
            private bool _pressed, _released;

            public void SetAim(Vector3 world)
            {
                world.y = 0f;
                _aimWorld = world.sqrMagnitude > 1e-6f ? world.normalized : Vector3.forward;
            }

            public bool IsEnabled => true;
            public Vector2 Move => Vector2.zero;
            public AimSourceKind AimKind => AimSourceKind.Stick;
            public Vector2 AimScreenPosition => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            /// <summary>The world aim as a camera-relative stick (top-down: the camera's planar forward is stick up).</summary>
            public Vector2 AimStick
            {
                get
                {
                    PrototypeCameraRig cam = PrototypeCameraRig.Active;
                    Vector3 fwd = cam != null ? cam.PlanarForward : Vector3.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                    fwd.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, fwd);
                    var stick = new Vector2(Vector3.Dot(_aimWorld, right), Vector3.Dot(_aimWorld, fwd));
                    return stick.sqrMagnitude > 1e-6f ? stick.normalized : Vector2.up;
                }
            }

            public bool FireHeld => Fire;

            public bool FirePressed
            {
                get
                {
                    Edges();
                    return _pressed;
                }
            }

            public bool FireReleased
            {
                get
                {
                    Edges();
                    return _released;
                }
            }

            public bool ReloadPressed => false;
            public bool PausePressed => false;
            public bool DebugPanelPressed => false;
#pragma warning disable 67
            public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
            public void Enable() { }
            public void Disable() { }

            private void Edges()
            {
                if (_frame == Time.frameCount) return;
                _frame = Time.frameCount;
                _pressed = Fire && !_was;
                _released = !Fire && _was;
                _was = Fire;
            }
        }
    }
}
#endif
