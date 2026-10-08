#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using DD.Prototype.Audio;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-099 (S-01 td-capture, C-00 07.10) · TopDown evidence capture for a shell script: fixed-pose stills for before/after
    /// comparison, or a real-time gameplay video (JPEG frame sequence + grab timestamps) that a muxer pairs with the
    /// <c>-ddAudioCapture</c> WAV. DEVELOPMENT PLAYER / EDITOR ONLY (whole file under the M-55 guard — no release exception,
    /// unlike PrototypeCaptureAgent); without <c>-ddTdCapture</c> it creates nothing (<see cref="TryStart"/>).
    ///
    /// Command line (parsed by <see cref="TdCaptureOptions"/>; a malformed value writes &lt;outdir&gt;/error.json and quits 2):
    ///   -ddTdCapture &lt;abs dir&gt;   arm; artefacts go under it
    ///   -ddTdCaptureMode stills|video|boot
    ///   -ddTdHud on|off           round 3: off disables every UI canvas while the Run scene is active (default on)
    ///   -ddTdSeed &lt;int&gt;          UnityEngine.Random.InitState at BeforeSceneLoad (recorded; the QA bot reseeds with its own -ddQaBotSeed)
    ///   -ddTdResolution WxH       optional windowed Screen.SetResolution first
    ///   stills: -ddTdPoses &lt;file&gt; ('key x z [crowd40]' lines, see <see cref="TdPoseFile"/>), -ddTdHoldSpawns 0|1 (default 1),
    ///           -ddTdSettleSeconds f (default 2), -ddTdIntroSeconds f (default 5), -ddTdCrowdWaitSeconds f (default 20)
    ///   video:  -ddTdVideoSeconds 10..120 (45), -ddTdVideoFps 1..60 (30), -ddTdWarmupSeconds f (3),
    ///           -ddTdVideoMaxWidth px (1920; 0 = native backbuffer), -ddTdVideoFlip auto|0|1 (auto)
    ///   boot:   the video flags (no warm-up) + -ddTdBootMenuSeconds f (4; 0..60)
    ///
    /// Both modes wait for the Run scene in Fighting with the player rig and the camera rig present, and refuse anything but
    /// the TopDown camera (exit 2). Like the other capture agents it starts only inside a sandbox — round 3 (gt SHOULD-1): ALL of
    /// -ddProfilePath, -ddUiDataRoot and -ddTelemetryDir (<see cref="TdCaptureOptions.CheckSandbox"/>), and on the first frame the
    /// live redirect (ProfileStore / UiDataPaths) is re-checked; the user-data guard around the Player is the script's job.
    ///
    /// STILLS → &lt;outdir&gt;/stills/nn-key.png + poses.json. Per pose: spawns held (default), the player is teleported onto the
    /// NavMesh within 2 m of (x, z) exactly as PilotPerfProbe -ddPilotStart does (sampled from the rig's own height), facing the
    /// camera; the camera focus is snapped (PrototypeCameraRig.SnapTopDownDev) and the aim look-ahead suppressed
    /// (SuppressLookAheadDev), the player's motor / aim read a still input source (no move, stick aim toward the camera, so the
    /// OS cursor cannot turn the body); after the settle (unscaled) the backbuffer is captured at end of frame
    /// (CaptureScreenshotAsTexture, the Retina-safe path). A pose with no NavMesh within 2 m is recorded as "navmesh-miss" and
    /// skipped. Round 2 (C-00 07.10): the first pose waits -ddTdIntroSeconds after playable (the ROUND 1 intro), every capture
    /// waits (≤ 6 s) until RunHudScreen's centre banner is gone; a <c>crowd40</c> pose (always last, the parser enforces it)
    /// releases the hold and forces the safety-ceiling crowd (<see cref="ForceCrowd"/>), waiting ≤ -ddTdCrowdWaitSeconds for
    /// ≥ ceiling − 2 bodies within 15 m; poses.json records the counts and the wait (<c>crowd</c>, top-level <c>crowdShort</c>).
    /// Exit 0 when every pose was captured (a short crowd is recorded, not failed), 3 otherwise.
    ///
    /// VIDEO → &lt;outdir&gt;/video/: frames/f-NNNNNN.jpg (q 90), frames.tsv ("idx\tt", t = real seconds since t0 when the
    /// frame was grabbed), sync.json (t0 anchors incl. the WAV offset), audio-sync.tsv (one WAV anchor per second), summary.json,
    /// ref-calibration.png / ref-readback.jpg (the orientation check). The game is NOT slowed (no captureDeltaTime): the QA bot
    /// plays it in real time, so the WAV stays in step. Per rendered frame, at end of frame, when the grab time reaches the next
    /// 1/fps slot (<see cref="TdFrameSchedule"/>) the backbuffer is copied on the GPU (CaptureScreenshotIntoRenderTexture,
    /// optional GPU downscale), read back asynchronously (AsyncGPUReadback, no stall) and JPEG-encoded + written on thread-pool
    /// threads (ImageConversion.EncodeArrayToJPG). At most <see cref="MaxInFlight"/> frames are in the pipeline; a due slot
    /// beyond that is skipped and counted, never queued. Stops after -ddTdVideoSeconds, or when the run ends, or when the app
    /// quits under it (the bot finishing) — summary.json is written in every case.
    ///
    /// A/V: video t → WAV frame = F0 + t·R, F0 = AudioOutputCapture.FramesCaptured at t0 (sync.json audio.framesCapturedAtT0),
    /// R = AudioSettings.outputSampleRate; tdmux --wav-offset = F0 / R (sync.json audio.wavOffsetSeconds). summary.json
    /// audio.driftSeconds checks that the WAV ran gap-free over the window. General form (round 3, needed by boot mode where
    /// the WAV has no samples yet at t0): the first observation (ta, Fa) with FramesCaptured &gt; 0 is the anchor and
    /// wavOffsetSeconds = Fa / R − ta (negative = the WAV starts after video t0); with audio flowing at t0 it is F0 / R as before.
    ///
    /// BOOT (round 3, C-00 08.10) → &lt;outdir&gt;/video/ exactly as VIDEO, recorded from the first rendered frame: the agent is
    /// created at BeforeSceneLoad (DontDestroyOnLoad) and arms AudioOutputCapture on itself there (-ddAudioCapture), so the
    /// boot seal / loading screen, the main menu with its logo intro and the first seconds of Fighting are all in the take.
    /// No -ddDirectToRun and no -ddQaBot (the bot drives the menu away in one frame): the agent itself calls the flow's own
    /// GoToSelection -ddTdBootMenuSeconds after the menu is revealed, then StartRun 1 s after Selection is revealed (the
    /// QaRunBot boot-flow calls, with its re-seed). sync.json "phases" stamps loadingStart / loadingEnd / menu / fighting
    /// (+ selectionRequested / selection / runRequested) in real seconds from t0, null when never seen.
    /// </summary>
    public sealed class TdCaptureAgent : MonoBehaviour
    {
        private const float SceneTimeoutSeconds = 180f;
        private const float NavMeshSampleMetres = 2f;   // PilotPerfProbe StartSampleMetres
        private const int MaxInFlight = 6;
        private const int JpegQuality = 90;
        private const float DrainTimeoutSeconds = 15f;
        private const float CalibrationTimeoutSeconds = 5f;
        private const float BannerWaitMaxSeconds = 6f;
        private const float CrowdRadiusMetres = 15f;
        private const float BootSelectionHoldSeconds = 1f;
        private const float BootVerifyAfterMenuSeconds = 1.5f;
        private const float HudRefreshSeconds = 0.25f;

        private static bool s_seedApplied;
        private static int s_seed;
        private static bool s_started;   // round 3: boot mode starts at BeforeSceneLoad; AfterSceneLoad must not start a second agent

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_started = false;
        }

        private TdCaptureOptions _o;
        private List<TdPose> _poses;
        private bool _finished;   // error.json / poses.json / summary.json written; nothing else is

        // ---------------------------------------------------------------- boot

        /// <summary>Seed as early as the engine allows, before the Boot scene draws anything.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SeedEarly()
        {
            s_seedApplied = false;
            int seed;
            if (TdCaptureOptions.TryReadSeed(Environment.GetCommandLineArgs(), out seed))
            {
                UnityEngine.Random.InitState(seed);
                s_seed = seed;
                s_seedApplied = true;
                Debug.Log("[TdCapture] Random.InitState(" + seed + ") at BeforeSceneLoad");
            }
        }

        /// <summary>Round 3: boot mode starts here, before the first scene loads, so the first rendered frame is already recorded.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootEarly()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (TdCaptureOptions.IsRequested(args) && TdCaptureOptions.Arg(args, TdCaptureOptions.ModeFlag) == "boot")
            {
                TryStart(args);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (s_started) return;
            TryStart(Environment.GetCommandLineArgs());
        }

        /// <summary>
        /// Starts the agent only when <paramref name="args"/> carries <c>-ddTdCapture &lt;dir&gt;</c>; returns whether it did anything.
        /// Without the flag: a scan of the array, no GameObject, no allocation (TdCaptureLogicTests).
        /// </summary>
        public static bool TryStart(string[] args)
        {
            if (!TdCaptureOptions.IsRequested(args))
            {
                return false;
            }

            s_started = true;
            string error;
            TdCaptureOptions o = TdCaptureOptions.Parse(args, out error);
            if (o == null)
            {
                string dir = TdCaptureOptions.Arg(args, TdCaptureOptions.OutFlag);
                WriteErrorFile(Path.IsPathRooted(dir ?? "") ? dir : null, "args", error, 2);
                Quit(2);
                return true;
            }

            List<TdPose> poses = null;
            if (o.Mode == TdCaptureMode.Stills)
            {
                string text = null;
                try
                {
                    text = File.ReadAllText(o.PosesPath);
                }
                catch (Exception e)
                {
                    error = TdCaptureOptions.PosesFlag + " '" + o.PosesPath + "' unreadable: " + e.GetType().Name + ": " + e.Message;
                }

                if (error == null && !TdPoseFile.TryParse(text, out poses, out error))
                {
                    error = "pose file '" + o.PosesPath + "': " + error;
                }

                if (error != null)
                {
                    WriteErrorFile(o.OutDir, "poses", error, 2);
                    Quit(2);
                    return true;
                }
            }

            // M-55 (C-00 30.09) + gt SHOULD-1 (C-00 08.10): a capture run plays a run, so it only starts inside a FULL sandbox —
            // all three of -ddProfilePath, -ddUiDataRoot, -ddTelemetryDir (AND, not OR). The live redirect is re-checked in Start.
            if (!Application.isEditor && !TdCaptureOptions.CheckSandbox(args, out error))
            {
                WriteErrorFile(o.OutDir, "sandbox", error, 2);
                Quit(2);
                return true;
            }

            var go = new GameObject("TdCaptureAgent");
            DontDestroyOnLoad(go);
            TdCaptureAgent agent = go.AddComponent<TdCaptureAgent>();
            agent._o = o;
            agent._poses = poses;
            if (o.Mode == TdCaptureMode.Boot)
            {
                // The WAV must exist from the first frame as well: arm the recorder on this DDOL host now (PrototypeAudioDirector's own
                // TryArm later returns this instance). Without -ddAudioCapture TryArm does nothing.
                AudioOutputCapture.TryArm(go);
            }
            Debug.Log("[TdCapture] armed mode=" + o.Mode + " out=" + o.OutDir + (o.HasSeed ? " seed=" + o.Seed : "") + " holdSpawns=" + o.HoldSpawns);
            return true;
        }

        private IEnumerator Start()
        {
            try
            {
                Directory.CreateDirectory(_o.OutDir);
            }
            catch (Exception e)
            {
                Fail("outdir", "cannot create " + _o.OutDir + ": " + e.Message, 2);
                yield break;
            }

            // The live redirect, not just the flags (FirstPersonArmsCaptureAgent rule): by the first frame every BeforeSceneLoad
            // sandbox hook (ProfileSandboxFlag, QaUiDataSandbox) has run.
            if (!Application.isEditor && (!ProfileStore.HasPathOverride || !UiDataPaths.IsProcessLocal))
            {
                Fail("sandbox", "refused: the profile / UI data are not redirected (ProfileStore.HasPathOverride=" + ProfileStore.HasPathOverride +
                                ", UiDataPaths.IsProcessLocal=" + UiDataPaths.IsProcessLocal + ")", 2);
                yield break;
            }

            if (_o.ResolutionWidth > 0)
            {
                Screen.SetResolution(_o.ResolutionWidth, _o.ResolutionHeight, FullScreenMode.Windowed);
                yield return null;
                yield return null;
                yield return new WaitForSecondsRealtime(1f);   // SetResolution lands on a later frame (PrototypeCaptureAgent)
                Debug.Log("[TdCapture] resolution requested " + _o.ResolutionWidth + "x" + _o.ResolutionHeight + " -> screen " + Screen.width + "x" + Screen.height);
            }

            if (_o.Mode == TdCaptureMode.Boot)
            {
                yield return BootVideo();
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (!RunPlayable())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Fail("scene-ready", "Run scene not playable (Fighting + player + camera rig) within " + SceneTimeoutSeconds + " s; scene " +
                         SceneManager.GetActiveScene().name + ", phase " + (RunDirector.Instance != null ? RunDirector.Instance.Phase.ToString() : "no RunDirector"), 3);
                    yield break;
                }

                yield return null;
            }

            if (CameraModeService.Current != CameraMode.TopDown)
            {
                Fail("scene-ready", "camera mode is " + CameraModeService.Current + "; td-capture is TopDown only (pass -ddCameraMode topdown)", 2);
                yield break;
            }

            if (_o.HoldSpawns)
            {
                HoldSpawns();
            }

            if (_o.Mode == TdCaptureMode.Stills)
            {
                yield return Stills();
            }
            else
            {
                yield return Video();
            }
        }

        private static bool RunPlayable()
        {
            Scene s = SceneManager.GetActiveScene();
            return s.name == PrototypeScenes.Run && s.isLoaded && RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting &&
                   PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null;
        }

        /// <summary>Spawning off + field cleared through the dev switch (EnemySpawnDirector.SpawningEnabledDev, WP-076 B9), re-asserted per pose.</summary>
        private static void HoldSpawns()
        {
            EnemySpawnDirector dir = EnemySpawnDirector.Instance;
            if (dir == null) return;
            if (dir.SpawningEnabledDev) dir.SpawningEnabledDev = false;
            if (dir.ActiveCount > 0) dir.ClearAllEnemies();
        }

        // ---------------------------------------------------------------- stills

        /// <summary>No movement, stick aim held toward the camera: the body's facing no longer depends on the OS cursor.</summary>
        private sealed class StillInput : IInputSource
        {
            public bool IsEnabled => true;
            public Vector2 Move => Vector2.zero;
            public AimSourceKind AimKind => AimSourceKind.Stick;
            public Vector2 AimScreenPosition => Vector2.zero;
            public Vector2 AimStick => new Vector2(0f, -1f);
            public bool FireHeld => false;
            public bool FirePressed => false;
            public bool FireReleased => false;
            public bool ReloadPressed => false;
            public bool PausePressed => false;
            public bool DebugPanelPressed => false;
#pragma warning disable 67
            public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
            public void Enable() { }
            public void Disable() { }
        }

        private IEnumerator Stills()
        {
            string dir = Path.Combine(_o.OutDir, "stills");
            Directory.CreateDirectory(dir);
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = true;   // evidence only
            var still = new StillInput();
            rig.Motor.SetInputSource(still);
            rig.Aim.SetInputSource(still);
            PrototypeCameraRig.SuppressLookAheadDev = true;

            // The ROUND 1 intro (banner + its HUD beat) must not be in frame 01: a fixed wait after playable, then per pose the
            // banner itself is waited out (bounded) before the capture.
            if (_o.IntroSeconds > 0f) yield return new WaitForSecondsRealtime(_o.IntroSeconds);

            var entries = new List<string>(_poses.Count);
            int captured = 0, missed = 0, crowdShort = 0;
            for (int i = 0; i < _poses.Count; i++)
            {
                TdPose p = _poses[i];
                if (_o.HoldSpawns && !p.Crowd) HoldSpawns();   // the parser guarantees no plain pose follows a crowd pose
                rig = PlayerRegistry.Primary;
                PrototypeCameraRig cam = PrototypeCameraRig.Active;
                if (rig == null || cam == null)
                {
                    Fail("stills", "player or camera rig lost before pose '" + p.Key + "'", 3);
                    yield break;
                }

                NavMeshHit hit;
                if (!NavMesh.SamplePosition(new Vector3(p.X, rig.transform.position.y, p.Z), out hit, NavMeshSampleMetres, NavMesh.AllAreas))
                {
                    missed++;
                    entries.Add(PoseJson(i, p, "navmesh-miss", null, Vector3.zero, null, 0, 0, null));
                    Debug.LogWarning("[TdCapture] pose " + p.Key + " (" + p.X + ", " + p.Z + ") not within " + NavMeshSampleMetres + " m of the NavMesh — skipped");
                    continue;
                }

                Vector3 toCamera = -cam.PlanarForward;
                rig.Teleport(hit.position, toCamera.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toCamera, Vector3.up) : Quaternion.identity);
                cam.SnapTopDownDev();
                yield return null;   // the teleport is rendered; LateUpdate re-seeded the focus on the new position
                if (PrototypeCameraRig.Active != null) PrototypeCameraRig.Active.SnapTopDownDev();   // the capsule may settle a frame later

                string crowdJson = null;
                if (p.Crowd)
                {
                    var crowd = new CrowdResult();
                    yield return ForceCrowd(crowd);
                    if (!crowd.Reached) crowdShort++;
                    crowdJson = crowd.Json();
                }

                yield return new WaitForSecondsRealtime(_o.SettleSeconds);   // roof fade (OccluderFadeState 0.15 s), LOD, light probes, animation
                float bannerWait = 0f;
                RunHudScreen hud = FindObjectOfType<RunHudScreen>();
                float bannerDeadline = Time.realtimeSinceStartup + BannerWaitMaxSeconds;
                while (hud != null && hud.BannerSecondsLeftDev > 0f && Time.realtimeSinceStartup < bannerDeadline)
                {
                    bannerWait += Time.unscaledDeltaTime;
                    yield return null;
                }

                bool bannerLive = hud != null && hud.BannerSecondsLeftDev > 0f;
                if (_o.HudOff) HideHud(true);   // a canvas built this frame must not reach the still
                yield return new WaitForEndOfFrame();

                string file = TdPoseFile.StillFileName(i, p.Key);
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                int w = shot.width, h = shot.height;
                try
                {
                    File.WriteAllBytes(Path.Combine(dir, file), shot.EncodeToPNG());
                }
                finally
                {
                    Destroy(shot);
                }

                captured++;
                Vector3 actual = PlayerRegistry.Primary != null ? PlayerRegistry.Primary.transform.position : hit.position;
                string extra = "\"bannerWaitSeconds\": " + TdJson.Num(bannerWait, 2) + ", \"bannerLiveAtCapture\": " + TdJson.Bool(bannerLive) +
                               (crowdJson != null ? ", \"crowd\": " + crowdJson : "");
                entries.Add(PoseJson(i, p, "captured", hit, actual, file, w, h, extra));
                Debug.Log("[TdCapture] still " + file + " at " + actual.ToString("F2") + " (" + w + "x" + h + ", screen " + Screen.width + "x" + Screen.height + ")");
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"wp\": \"WP-099\",\n  \"mode\": \"stills\",\n");
            AppendSeed(sb);
            sb.Append("  \"holdSpawns\": ").Append(TdJson.Bool(_o.HoldSpawns)).Append(",\n");
            sb.Append("  ").Append(HudJson()).Append(", \"hudNote\": \"the Development Build watermark is engine-drawn and stays\",\n");
            sb.Append("  \"settleSeconds\": ").Append(TdJson.Num(_o.SettleSeconds, 2)).Append(",\n");
            sb.Append("  \"posesFile\": ").Append(TdJson.Str(_o.PosesPath)).Append(",\n");
            sb.Append("  \"navMeshSampleMetres\": ").Append(TdJson.Num(NavMeshSampleMetres, 1)).Append(",\n");
            sb.Append("  \"screen\": [").Append(Screen.width).Append(", ").Append(Screen.height).Append("],\n");
            sb.Append("  \"cameraMode\": ").Append(TdJson.Str(CameraModeService.Current.ToString())).Append(",\n");
            sb.Append("  \"scene\": ").Append(TdJson.Str(SceneManager.GetActiveScene().name)).Append(",\n");
            sb.Append("  \"introSeconds\": ").Append(TdJson.Num(_o.IntroSeconds, 2)).Append(", \"crowdWaitSeconds\": ").Append(TdJson.Num(_o.CrowdWaitSeconds, 1))
              .Append(", \"crowdRadiusMetres\": ").Append(TdJson.Num(CrowdRadiusMetres, 1)).Append(",\n");
            sb.Append("  \"captured\": ").Append(captured).Append(",\n  \"missed\": ").Append(missed).Append(",\n  \"crowdShort\": ").Append(crowdShort).Append(",\n");
            sb.Append("  \"poses\": [\n");
            for (int i = 0; i < entries.Count; i++) sb.Append("    ").Append(entries[i]).Append(i + 1 < entries.Count ? ",\n" : "\n");
            sb.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(dir, "poses.json"), sb.ToString());
            _finished = true;
            Debug.Log("[TdCapture] stills done captured=" + captured + " missed=" + missed);
            yield return null;
            Quit(missed == 0 ? 0 : 3);
        }

        // ---------------------------------------------------------------- crowd40

        private sealed class CrowdResult
        {
            public int Target, Threshold, Alive, Within;
            public float WaitSeconds;
            public bool Reached, Reseeded, DirectorMissing;

            public string Json()
            {
                return "{\"target\": " + Target + ", \"threshold\": " + Threshold + ", \"alive\": " + Alive + ", \"withinRadius\": " + Within +
                       ", \"waitSeconds\": " + TdJson.Num(WaitSeconds, 2) + ", \"reached\": " + TdJson.Bool(Reached) +
                       ", \"reseeded\": " + TdJson.Bool(Reseeded) + ", \"directorMissing\": " + TdJson.Bool(DirectorMissing) + "}";
            }
        }

        /// <summary>
        /// A <c>crowd40</c> pose: the hold is released and the director is pinned at its safety ceiling exactly as PilotPerfProbe's
        /// stage 02 does (<see cref="EnemySpawnDirector.ForceActiveTarget"/>(SafetyCeiling, ignorePool), the director's own
        /// gates and 4-per-tick fill — nothing copied); Random is re-seeded with -ddTdSeed right before so the spawn draws repeat.
        /// The player is already invulnerable (PlayerHealth.DiagnosisInvulnerable, the QaRunBot / PilotPerfProbe path). Waits
        /// (bounded by -ddTdCrowdWaitSeconds) until ≥ ceiling − 2 bodies are alive within <see cref="CrowdRadiusMetres"/>.
        /// </summary>
        private IEnumerator ForceCrowd(CrowdResult r)
        {
            EnemySpawnDirector dir = EnemySpawnDirector.Instance;
            if (dir == null)
            {
                r.DirectorMissing = true;
                yield break;
            }

            if (_o.HasSeed)
            {
                UnityEngine.Random.InitState(_o.Seed);
                r.Reseeded = true;
            }

            r.Target = dir.SafetyCeiling;
            r.Threshold = Mathf.Max(1, r.Target - 2);
            dir.SpawningEnabledDev = true;
            dir.ForceActiveTarget(r.Target, true);
            float t0 = Time.realtimeSinceStartup;
            while (true)
            {
                CountCrowd(out r.Alive, out r.Within);
                r.WaitSeconds = Time.realtimeSinceStartup - t0;
                if (r.Alive >= r.Threshold && r.Within >= r.Threshold) { r.Reached = true; break; }
                if (r.WaitSeconds >= _o.CrowdWaitSeconds) break;
                yield return null;
            }

            Debug.Log("[TdCapture] crowd40 target " + r.Target + " alive " + r.Alive + " within " + CrowdRadiusMetres + " m " + r.Within +
                      " after " + r.WaitSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s" + (r.Reached ? "" : " (SHORT)"));
        }

        private static void CountCrowd(out int alive, out int within)
        {
            alive = 0;
            within = 0;
            PlayerRig rig = PlayerRegistry.Primary;
            Vector3 at = rig != null ? rig.transform.position : Vector3.zero;
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                ZombieEnemy z = live[i];
                if (z == null || !z.IsAlive) continue;
                alive++;
                Vector3 d = z.transform.position - at;
                d.y = 0f;
                if (d.sqrMagnitude <= CrowdRadiusMetres * CrowdRadiusMetres) within++;
            }
        }

        private static string PoseJson(int index, TdPose p, string status, NavMeshHit? hit, Vector3 actual, string file, int w, int h, string extra)
        {
            var sb = new StringBuilder();
            sb.Append("{\"index\": ").Append(index + 1).Append(", \"key\": ").Append(TdJson.Str(p.Key)).Append(", \"line\": ").Append(p.Line)
              .Append(", \"crowd40\": ").Append(TdJson.Bool(p.Crowd))
              .Append(", \"requested\": {\"x\": ").Append(TdJson.Num(p.X, 3)).Append(", \"z\": ").Append(TdJson.Num(p.Z, 3)).Append('}')
              .Append(", \"status\": ").Append(TdJson.Str(status));
            if (hit.HasValue)
            {
                Vector3 n = hit.Value.position;
                float snap = new Vector2(n.x - p.X, n.z - p.Z).magnitude;
                sb.Append(", \"navmesh\": ").Append(Vec(n)).Append(", \"snapDistance\": ").Append(TdJson.Num(snap, 3))
                  .Append(", \"actual\": ").Append(Vec(actual))
                  .Append(", \"png\": ").Append(TdJson.Str(file)).Append(", \"size\": [").Append(w).Append(", ").Append(h).Append(']');
            }

            if (!string.IsNullOrEmpty(extra)) sb.Append(", ").Append(extra);
            sb.Append(", \"unscaledTime\": ").Append(TdJson.Num(Time.unscaledTime, 3))
              .Append(", \"realtime\": ").Append(TdJson.Num(Time.realtimeSinceStartupAsDouble, 3)).Append('}');
            return sb.ToString();
        }

        private static string Vec(Vector3 v)
        {
            return "{\"x\": " + TdJson.Num(v.x, 3) + ", \"y\": " + TdJson.Num(v.y, 3) + ", \"z\": " + TdJson.Num(v.z, 3) + "}";
        }

        private void AppendSeed(StringBuilder sb)
        {
            sb.Append("  \"seed\": ").Append(_o.HasSeed ? _o.Seed.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(", \"seedApplied\": ").Append(TdJson.Bool(s_seedApplied && _o.HasSeed && s_seed == _o.Seed))
              .Append(", \"seedAppliedAt\": ").Append(TdJson.Str(s_seedApplied ? "BeforeSceneLoad" : null)).Append(",\n");
        }

        // ---------------------------------------------------------------- video

        private sealed class EncodeJob
        {
            public TdCaptureAgent Owner;
            public byte[] Buffer;
            public int Width, Height;
            public bool Flip;
            public string Path;
        }

        private static readonly WaitCallback s_encode = Encode;

        private string _videoDir, _framesDir;
        private StreamWriter _tsv, _audioTsv;
        private TdFrameSchedule _schedule;
        private RenderTexture _full, _scaled;
        private int _backW, _backH, _screenW0, _screenH0, _outW, _outH, _resizes;
        private bool _flip;
        private string _flipMethod;
        private int _flipMeasured = -1;
        private double _flipErrStraight, _flipErrFlipped;
        private float _refMeanLuma, _grabMeanLuma;
        private byte[] _calibration;
        private bool _calibrationDone;

        private double _t0Realtime, _t0Dsp;
        private float _t0Unscaled;
        private int _t0Frame;
        private bool _audioArmed;
        private string _audioPath;
        private int _audioRate;
        private long _audioF0, _audioDropped0;
        private double _nextAudioSync;
        private double _stopT = -1d;
        private string _stopReason;
        private bool _recording, _runEnded;

        private int _inFlight;   // readbacks + encodes in the pipeline (capacity)
        private int _encoding;   // encodes queued / running (the quit drain waits for these)
        private int _framesListed, _framesWritten, _encodeFailed, _readbackErrors;
        private string _lastEncodeError;
        private readonly Stack<byte[]> _pool = new Stack<byte[]>();

        private IEnumerator Video()
        {
            if (!PrepareVideo()) yield break;
            RunEvents.RunEnded += OnRunEnded;
            Application.quitting += OnQuitting;

            float warmEnd = Time.realtimeSinceStartup + _o.WarmupSeconds;
            while (Time.realtimeSinceStartup < warmEnd && !_runEnded)
            {
                if (_o.HoldSpawns) HoldSpawns();
                yield return null;
            }

            yield return Calibrate();
            if (_finished) yield break;

            yield return null;
            yield return new WaitForEndOfFrame();
            BeginRecording();

            // The first slot (t ≈ 0) is grabbed in this same end-of-frame.
            while (true)
            {
                double t = Time.realtimeSinceStartupAsDouble - _t0Realtime;
                if (t >= _o.VideoSeconds) { _stopReason = "duration"; _stopT = t; break; }
                if (_runEnded) { _stopReason = "run-ended"; _stopT = t; break; }
                if (RecordFrame(t) && _o.HoldSpawns) HoldSpawns();
                yield return null;
                yield return new WaitForEndOfFrame();
            }

            yield return EndRecording();
        }

        // ---------------------------------------------------------------- boot (round 3)

        private readonly string[] _phaseNames = { "loadingStart", "loadingEnd", "menu", "selectionRequested", "selection", "runRequested", "fighting" };
        private readonly double[] _phaseT = { -1d, -1d, -1d, -1d, -1d, -1d, -1d };
        private const int PhLoadingStart = 0, PhLoadingEnd = 1, PhMenu = 2, PhSelectionRequested = 3, PhSelection = 4, PhRunRequested = 5, PhFighting = 6;
        private string _cameraModeAtFighting;
        private bool _verifyRequested, _verifyDone;
        private byte[] _verify;
        private float[] _verifyRefBands;
        private int _verifyMeasured = -2;   // -2 not run, -1 ambiguous, 0 straight, 1 flipped
        private double _verifyErrStraight, _verifyErrFlipped;

        /// <summary>
        /// Boot mode: the recording runs from the first rendered frame through the loading screen and the main menu into Fighting
        /// for -ddTdVideoSeconds of real time; the agent walks the menu → Selection → Run itself (see the class summary).
        /// </summary>
        private IEnumerator BootVideo()
        {
            if (!PrepareVideo()) yield break;
            Application.quitting += OnQuitting;

            // The first frames are usually black (boot seal not yet in): the orientation is decided now (measured if the frame has
            // structure, else graphicsUVStartsAtTop — measured equal on Metal in smoke-3 / once-bc901614) and VERIFIED on the menu.
            yield return Calibrate();
            if (_finished) yield break;

            yield return null;
            yield return new WaitForEndOfFrame();
            BeginRecording();

            double selectionSeenAt = -1d;
            while (true)
            {
                double t = Time.realtimeSinceStartupAsDouble - _t0Realtime;
                TrackPhases(t);
                if (t >= _o.VideoSeconds) { _stopReason = "duration"; _stopT = t; break; }

                // menu → Selection → Run, through the flow's own calls (QaRunBot's boot flow), after the menu has been SEEN
                if (_phaseT[PhMenu] >= 0d && _phaseT[PhSelectionRequested] < 0d && t - _phaseT[PhMenu] >= _o.BootMenuSeconds && !PrototypeUiFlow.IsLoading)
                {
                    PrototypeUiFlow.GoToSelection();
                    Stamp(PhSelectionRequested, t);
                }

                if (_phaseT[PhSelection] >= 0d && _phaseT[PhRunRequested] < 0d)
                {
                    if (selectionSeenAt < 0d) selectionSeenAt = t;
                    if (t - selectionSeenAt >= BootSelectionHoldSeconds && !PrototypeUiFlow.IsLoading)
                    {
                        PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                        if (_o.HasSeed) UnityEngine.Random.InitState(_o.Seed);   // the run's first draw happens after StartRun (QaRunBot)
                        PrototypeUiFlow.StartRun();
                        Stamp(PhRunRequested, t);
                    }
                }

                if (!_verifyRequested && _phaseT[PhMenu] >= 0d && t - _phaseT[PhMenu] >= BootVerifyAfterMenuSeconds)
                {
                    StartVerify();
                }

                if (_verifyDone && _verifyMeasured == -2) FinishVerify();
                if (RecordFrame(t) && _o.HoldSpawns && _phaseT[PhFighting] >= 0d) HoldSpawns();
                yield return null;
                yield return new WaitForEndOfFrame();
            }

            yield return EndRecording();
        }

        /// <summary>
        /// Phase hooks, polled once per recorded frame (no instance is created): loading = UiCurtain.IsCovering (boot seal and
        /// every loading curtain, set from the doors closing until they are half open again) or PrototypeUiFlow.IsLoading or the
        /// Boot scene; the first loading stretch gives loadingStart / loadingEnd. menu = MainMenu active and revealed;
        /// selection likewise; fighting = RunDirector in RunPhase.Fighting.
        /// </summary>
        private void TrackPhases(double t)
        {
            Scene scene = SceneManager.GetActiveScene();
            bool covering = UiCurtain.IsCovering;
            bool loading = covering || PrototypeUiFlow.IsLoading || scene.name == PrototypeScenes.Boot;
            if (loading && _phaseT[PhLoadingStart] < 0d) Stamp(PhLoadingStart, t);
            if (!loading && _phaseT[PhLoadingStart] >= 0d && _phaseT[PhLoadingEnd] < 0d) Stamp(PhLoadingEnd, t);
            if (!covering && scene.isLoaded && scene.name == PrototypeScenes.MainMenu && _phaseT[PhMenu] < 0d) Stamp(PhMenu, t);
            if (!covering && !PrototypeUiFlow.IsLoading && scene.isLoaded && scene.name == PrototypeScenes.Selection && _phaseT[PhSelection] < 0d) Stamp(PhSelection, t);
            if (_phaseT[PhFighting] < 0d && RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting)
            {
                Stamp(PhFighting, t);
                _cameraModeAtFighting = CameraModeService.Current.ToString();
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig != null && rig.Health != null) rig.Health.DiagnosisInvulnerable = true;   // no human plays: the take must not end in a death
            }
        }

        private void Stamp(int phase, double t)
        {
            _phaseT[phase] = t;
            Debug.Log("[TdCapture] phase " + _phaseNames[phase] + " at " + t.ToString("F3", CultureInfo.InvariantCulture) + " s (scene " + SceneManager.GetActiveScene().name + ")");
            WriteSyncJson();   // stamps survive a killed Player
        }

        private void AppendPhases(StringBuilder sb)
        {
            sb.Append("{");
            for (int i = 0; i < _phaseNames.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(_phaseNames[i]).Append("\": ").Append(_phaseT[i] >= 0d ? TdJson.Num(_phaseT[i], 3) : "null");
            }

            sb.Append("}");
        }

        /// <summary>One CaptureScreenshotAsTexture (a one-frame stall) on the revealed menu, against a readback of the same frame:
        /// the orientation decided on the (usually black) first frame is checked, never changed mid-take.</summary>
        private void StartVerify()
        {
            _verifyRequested = true;
            Texture2D reference = ScreenCapture.CaptureScreenshotAsTexture();
            Color32[] px = reference.GetPixels32();
            var bytes = new byte[px.Length * 4];
            for (int i = 0, o = 0; i < px.Length; i++) { bytes[o++] = px[i].r; bytes[o++] = px[i].g; bytes[o++] = px[i].b; bytes[o++] = px[i].a; }
            _verifyRefBands = TdOrientation.BandLuma(bytes, reference.width, reference.height, TdOrientation.Bands);
            Destroy(reference);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
            RenderTexture src = Downscale();
            AsyncGPUReadback.Request(src, 0, TextureFormat.RGBA32, req =>
            {
                _verifyDone = true;
                if (req.hasError) return;
                var data = req.GetData<byte>();
                _verify = new byte[data.Length];
                data.CopyTo(_verify);
            });
        }

        private void FinishVerify()
        {
            _verifyMeasured = -1;
            if (_verify == null || _verifyRefBands == null) return;
            _verifyMeasured = TdOrientation.ChooseFlip(_verifyRefBands, TdOrientation.BandLuma(_verify, _outW, _outH, TdOrientation.Bands), out _verifyErrStraight, out _verifyErrFlipped);
            Debug.Log("[TdCapture] orientation verify on the menu: measured " + _verifyMeasured + " (flip in use " + _flip + ")" +
                      (_verifyMeasured >= 0 && (_verifyMeasured == 1) != _flip ? " MISMATCH - every frame of this take is upside down" : ""));
        }

        // ---------------------------------------------------------------- recording core (video + boot)

        private bool PrepareVideo()
        {
            _videoDir = Path.Combine(_o.OutDir, "video");
            _framesDir = Path.Combine(_videoDir, "frames");
            Directory.CreateDirectory(_framesDir);
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Fail("video", "AsyncGPUReadback is not supported on " + SystemInfo.graphicsDeviceType, 3);
                return false;
            }

            return true;
        }

        /// <summary>t0: opens frames.tsv / audio-sync.tsv, takes the clock and audio anchors, writes sync.json. Called at end of frame.</summary>
        private void BeginRecording()
        {
            _tsv = new StreamWriter(Path.Combine(_videoDir, "frames.tsv"), false, new UTF8Encoding(false)) { NewLine = "\n" };
            _audioTsv = new StreamWriter(Path.Combine(_videoDir, "audio-sync.tsv"), false, new UTF8Encoding(false)) { NewLine = "\n" };
            _audioTsv.WriteLine("# t_seconds\twav_frames_captured\tdsp_time\trealtime\tdropped_samples");
            _schedule = new TdFrameSchedule(_o.VideoFps);

            _t0Realtime = Time.realtimeSinceStartupAsDouble;
            _t0Dsp = AudioSettings.dspTime;
            _t0Unscaled = Time.unscaledTime;
            _t0Frame = Time.frameCount;
            AudioOutputCapture audio = AudioOutputCapture.Armed;
            _audioArmed = audio != null;
            _audioArmedAtT0 = audio != null;
            _audioRate = AudioSettings.outputSampleRate;
            if (audio != null)
            {
                _audioPath = audio.Path;
                _audioF0 = audio.FramesCaptured;
                _audioDropped0 = audio.DroppedSamples;
            }

            TryAnchorAudio(0d);
            WriteSyncJson();
            WriteAudioSync(0d);
            _nextAudioSync = 1d;
            _recording = true;
            Debug.Log("[TdCapture] " + _o.Mode + " t0 realtime=" + _t0Realtime.ToString("F3", CultureInfo.InvariantCulture) + " dsp=" + _t0Dsp.ToString("F6", CultureInfo.InvariantCulture) +
                      " out=" + _outW + "x" + _outH + " flip=" + _flip + " (" + _flipMethod + ") audio=" + (_audioArmed ? "F0 " + _audioF0 : "not armed"));
        }

        /// <summary>One end-of-frame: grab if a slot is due, keep the audio anchors. True once per second (the caller's 1 Hz chores).</summary>
        private bool RecordFrame(double t)
        {
            if (!_audioAnchored && TryAnchorAudio(t)) WriteSyncJson();
            long slot;
            if (_schedule.TryTake(t, Volatile.Read(ref _inFlight) >= MaxInFlight, out slot))
            {
                Grab(t);
            }

            if (t < _nextAudioSync) return false;
            WriteAudioSync(t);
            _nextAudioSync = Math.Floor(t) + 1d;
            return true;
        }

        private bool _audioAnchored, _audioArmedAtT0;
        private double _audioAnchorT;
        private long _audioAnchorF;

        /// <summary>
        /// The A/V anchor: the first (t, FramesCaptured) with samples in the WAV. With audio flowing at t0 that is (0, F0) and the
        /// offset is F0 / R exactly as before; in boot mode the recorder is armed before any listener exists, so the anchor is the
        /// first frame after the first mixed buffer and the offset Fa / R − ta may be negative (the WAV starts after video t0).
        /// </summary>
        private bool TryAnchorAudio(double t)
        {
            AudioOutputCapture audio = AudioOutputCapture.Armed;
            if (audio == null) return false;
            if (!_audioArmed)
            {
                _audioArmed = true;   // armed after t0 (the director arms it in its Awake)
                _audioPath = audio.Path;
            }

            long f = audio.FramesCaptured;
            if (f <= 0) return false;
            _audioAnchored = true;
            _audioAnchorT = t;
            _audioAnchorF = f;
            return true;
        }

        private double WavOffsetSeconds => _audioAnchored && _audioRate > 0 ? _audioAnchorF / (double)_audioRate - _audioAnchorT : double.NaN;

        private IEnumerator EndRecording()
        {
            _recording = false;
            WriteAudioSync(_stopT);
            float drainEnd = Time.realtimeSinceStartup + DrainTimeoutSeconds;
            while (Volatile.Read(ref _inFlight) > 0 && Time.realtimeSinceStartup < drainEnd)
            {
                yield return null;
            }

            FinishVideo();
            yield return null;
            Quit(0);
        }

        // ---------------------------------------------------------------- HUD off (round 3)

        private readonly HashSet<int> _hudHidden = new HashSet<int>();
        private Canvas[] _canvases;
        private float _hudRefreshAt;

        /// <summary>
        /// -ddTdHud off: while the Run scene is active every Canvas (screen-space HUD, toasts, banner, reticle, hit markers, damage
        /// vignette, world-space markers) is disabled — Canvas.enabled only: no gameplay state, no saved setting, the UI scripts keep
        /// running. Re-asserted every frame, the list refreshed every 0.25 s (and right before a still). The "Development Build"
        /// watermark is drawn by the engine, not a Canvas, and stays.
        /// </summary>
        private void LateUpdate()
        {
            // not while a curtain covers: the loading screen's own exit over the Run scene stays in the take
            if (_o != null && _o.HudOff && !UiCurtain.IsCovering && SceneManager.GetActiveScene().name == PrototypeScenes.Run) HideHud(false);
        }

        private void HideHud(bool refresh)
        {
            if (refresh || _canvases == null || Time.unscaledTime >= _hudRefreshAt)
            {
                _canvases = FindObjectsOfType<Canvas>(true);
                _hudRefreshAt = Time.unscaledTime + HudRefreshSeconds;
            }

            for (int i = 0; i < _canvases.Length; i++)
            {
                Canvas c = _canvases[i];
                if (c == null || !c.enabled) continue;
                c.enabled = false;
                _hudHidden.Add(c.GetInstanceID());
            }
        }

        private string HudJson()
        {
            return "\"hud\": " + TdJson.Str(_o.HudOff ? "off" : "on") + ", \"hudCanvasesHidden\": " + _hudHidden.Count;
        }

        private void OnRunEnded(RunSummary s)
        {
            _runEnded = true;
        }

        /// <summary>The app is quitting under the recording (the QA bot finished, or the Player was closed): drain what can be drained, write the summary.</summary>
        private void OnQuitting()
        {
            if (_finished || _o == null || _o.Mode == TdCaptureMode.Stills) return;
            if (_stopT < 0d)
            {
                _stopT = _recording ? Time.realtimeSinceStartupAsDouble - _t0Realtime : 0d;
                _stopReason = "app-quitting";
            }

            _recording = false;
            // Readbacks cannot complete without another frame; encodes already on the pool can: wait for those (bounded).
            DateTime end = DateTime.UtcNow.AddSeconds(3);
            while (Volatile.Read(ref _encoding) > 0 && DateTime.UtcNow < end) Thread.Sleep(10);
            FinishVideo();
        }

        private void OnDestroy()
        {
            RunEvents.RunEnded -= OnRunEnded;
            Application.quitting -= OnQuitting;
            PrototypeCameraRig.SuppressLookAheadDev = false;
            ReleaseTargets();
        }

        /// <summary>
        /// One frame both ways in the same end-of-frame: the known-good CaptureScreenshotAsTexture (its size is the backbuffer's)
        /// and the RT readback the recording uses; the luma band profiles decide the readback's row order.
        /// </summary>
        private IEnumerator Calibrate()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D reference = ScreenCapture.CaptureScreenshotAsTexture();
            _backW = reference.width;
            _backH = reference.height;
            _screenW0 = Screen.width;
            _screenH0 = Screen.height;
            OutputSize(_backW, _backH, _o.VideoMaxWidth, out _outW, out _outH);
            CreateTargets(_backW, _backH);

            _calibrationDone = false;
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
            RenderTexture src = Downscale();
            AsyncGPUReadback.Request(src, 0, TextureFormat.RGBA32, OnCalibrationReadback);

            Color32[] px = reference.GetPixels32();
            var refBytes = new byte[px.Length * 4];
            for (int i = 0, o = 0; i < px.Length; i++)
            {
                refBytes[o++] = px[i].r; refBytes[o++] = px[i].g; refBytes[o++] = px[i].b; refBytes[o++] = px[i].a;
            }

            File.WriteAllBytes(Path.Combine(_videoDir, "ref-calibration.png"), reference.EncodeToPNG());
            Destroy(reference);
            float[] refBands = TdOrientation.BandLuma(refBytes, _backW, _backH, TdOrientation.Bands);
            _refMeanLuma = TdOrientation.Mean(refBands);

            float deadline = Time.realtimeSinceStartup + CalibrationTimeoutSeconds;
            while (!_calibrationDone && Time.realtimeSinceStartup < deadline) yield return null;

            if (_calibration != null)
            {
                float[] grabBands = TdOrientation.BandLuma(_calibration, _outW, _outH, TdOrientation.Bands);
                _grabMeanLuma = TdOrientation.Mean(grabBands);
                _flipMeasured = TdOrientation.ChooseFlip(refBands, grabBands, out _flipErrStraight, out _flipErrFlipped);
            }

            if (_o.VideoFlip >= 0) { _flip = _o.VideoFlip == 1; _flipMethod = "forced"; }
            else if (_flipMeasured >= 0) { _flip = _flipMeasured == 1; _flipMethod = "measured"; }
            else { _flip = SystemInfo.graphicsUVStartsAtTop; _flipMethod = _calibration != null ? "ambiguous-fallback-graphicsUVStartsAtTop" : "no-readback-fallback-graphicsUVStartsAtTop"; }

            if (_calibration != null)
            {
                try
                {
                    if (_flip) TdOrientation.FlipRows(_calibration, _outW, _outH, new byte[_outW * 4]);
                    File.WriteAllBytes(Path.Combine(_videoDir, "ref-readback.jpg"),
                        ImageConversion.EncodeArrayToJPG(_calibration, GraphicsFormat.R8G8B8A8_UNorm, (uint)_outW, (uint)_outH, 0, JpegQuality));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[TdCapture] ref-readback.jpg not written: " + e.Message);
                }
            }

            Debug.Log("[TdCapture] calibration backbuffer " + _backW + "x" + _backH + " screen " + _screenW0 + "x" + _screenH0 + " out " + _outW + "x" + _outH +
                      " flip=" + _flip + " (" + _flipMethod + ", err straight " + _flipErrStraight.ToString("F2", CultureInfo.InvariantCulture) +
                      " flipped " + _flipErrFlipped.ToString("F2", CultureInfo.InvariantCulture) + ") luma ref " + _refMeanLuma.ToString("F1", CultureInfo.InvariantCulture) +
                      " grab " + _grabMeanLuma.ToString("F1", CultureInfo.InvariantCulture));
        }

        private void OnCalibrationReadback(AsyncGPUReadbackRequest req)
        {
            _calibrationDone = true;
            if (req.hasError) return;
            var data = req.GetData<byte>();
            _calibration = new byte[data.Length];
            data.CopyTo(_calibration);
        }

        /// <summary>Output size: the backbuffer, scaled to ≤ maxWidth (0 = native), both sides even (H.264 4:2:0).</summary>
        private static void OutputSize(int backW, int backH, int maxWidth, out int w, out int h)
        {
            w = backW;
            h = backH;
            if (maxWidth > 0 && backW > maxWidth)
            {
                w = maxWidth;
                h = Mathf.Max(2, Mathf.RoundToInt(backH * (maxWidth / (float)backW)));
            }

            w &= ~1;
            h &= ~1;
        }

        private void CreateTargets(int fullW, int fullH)
        {
            if (_full != null) { _full.Release(); Destroy(_full); }
            _full = new RenderTexture(fullW, fullH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "TdCapture.Full", filterMode = FilterMode.Bilinear };
            _full.Create();
            if ((fullW != _outW || fullH != _outH) && _scaled == null)
            {
                _scaled = new RenderTexture(_outW, _outH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "TdCapture.Out", filterMode = FilterMode.Bilinear };
                _scaled.Create();
            }
        }

        private void ReleaseTargets()
        {
            if (_full != null) { _full.Release(); Destroy(_full); _full = null; }
            if (_scaled != null) { _scaled.Release(); Destroy(_scaled); _scaled = null; }
        }

        /// <summary>The RT the readback reads: the backbuffer copy itself, or its GPU downscale to the locked output size.</summary>
        private RenderTexture Downscale()
        {
            if (_full.width == _outW && _full.height == _outH) return _full;
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(_full, _scaled);
            RenderTexture.active = previous;
            return _scaled;
        }

        private void Grab(double t)
        {
            // A window resize mid-recording: the backbuffer copy follows the screen (proportionally to the calibrated
            // backbuffer / screen ratio); the output size stays locked so every JPEG has the same dimensions.
            if (Screen.width != _screenW0 || Screen.height != _screenH0)
            {
                int w = Mathf.Max(2, Mathf.RoundToInt(_backW * (Screen.width / (float)_screenW0)));
                int h = Mathf.Max(2, Mathf.RoundToInt(_backH * (Screen.height / (float)_screenH0)));
                if (w != _full.width || h != _full.height)
                {
                    _resizes++;
                    CreateTargets(w, h);
                    Debug.LogWarning("[TdCapture] window resized mid-recording -> backbuffer copy " + w + "x" + h + " (output stays " + _outW + "x" + _outH + ")");
                }
            }

            ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
            RenderTexture src = Downscale();
            Interlocked.Increment(ref _inFlight);
            double grabT = t;
            AsyncGPUReadback.Request(src, 0, TextureFormat.RGBA32, req => OnFrameReadback(req, grabT));
        }

        private void OnFrameReadback(AsyncGPUReadbackRequest req, double t)
        {
            if (req.hasError || _tsv == null || _finished)
            {
                if (req.hasError) _readbackErrors++;
                Interlocked.Decrement(ref _inFlight);
                return;
            }

            var data = req.GetData<byte>();
            byte[] buffer = Rent(data.Length);
            data.CopyTo(buffer);
            int idx = _framesListed++;
            _tsv.Write(idx.ToString(CultureInfo.InvariantCulture));
            _tsv.Write('\t');
            _tsv.WriteLine(t.ToString("F6", CultureInfo.InvariantCulture));
            Interlocked.Increment(ref _encoding);
            ThreadPool.QueueUserWorkItem(s_encode, new EncodeJob
            {
                Owner = this, Buffer = buffer, Width = req.width, Height = req.height, Flip = _flip,
                Path = Path.Combine(_framesDir, "f-" + idx.ToString("000000", CultureInfo.InvariantCulture) + ".jpg")
            });
        }

        /// <summary>Thread-pool side: row flip, JPEG encode (EncodeArrayToJPG is thread-safe), write; the buffer goes back to the pool.</summary>
        private static void Encode(object state)
        {
            var job = (EncodeJob)state;
            TdCaptureAgent owner = job.Owner;
            try
            {
                if (job.Flip) TdOrientation.FlipRows(job.Buffer, job.Width, job.Height, new byte[job.Width * 4]);
                byte[] jpg = ImageConversion.EncodeArrayToJPG(job.Buffer, GraphicsFormat.R8G8B8A8_UNorm, (uint)job.Width, (uint)job.Height, 0, JpegQuality);
                File.WriteAllBytes(job.Path, jpg);
                Interlocked.Increment(ref owner._framesWritten);
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref owner._encodeFailed);
                owner._lastEncodeError = System.IO.Path.GetFileName(job.Path) + ": " + e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                owner.Return(job.Buffer);
                Interlocked.Decrement(ref owner._encoding);
                Interlocked.Decrement(ref owner._inFlight);
            }
        }

        private byte[] Rent(int length)
        {
            lock (_pool)
            {
                while (_pool.Count > 0)
                {
                    byte[] b = _pool.Pop();
                    if (b.Length == length) return b;
                }
            }

            return new byte[length];
        }

        private void Return(byte[] buffer)
        {
            lock (_pool)
            {
                if (_pool.Count < MaxInFlight) _pool.Push(buffer);
            }
        }

        private void WriteAudioSync(double t)
        {
            if (_audioTsv == null) return;
            AudioOutputCapture audio = AudioOutputCapture.Armed;
            _audioTsv.WriteLine(t.ToString("F6", CultureInfo.InvariantCulture) + "\t" + (audio != null ? audio.FramesCaptured.ToString(CultureInfo.InvariantCulture) : "") + "\t" +
                                AudioSettings.dspTime.ToString("F6", CultureInfo.InvariantCulture) + "\t" + Time.realtimeSinceStartupAsDouble.ToString("F6", CultureInfo.InvariantCulture) + "\t" +
                                (audio != null ? audio.DroppedSamples.ToString(CultureInfo.InvariantCulture) : ""));
            _audioTsv.Flush();
        }

        private void WriteSyncJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"wp\": \"WP-099\",\n");
            AppendSeed(sb);
            sb.Append("  \"t0\": {\"realtimeSinceStartup\": ").Append(TdJson.Num(_t0Realtime, 6))
              .Append(", \"dspTime\": ").Append(TdJson.Num(_t0Dsp, 6))
              .Append(", \"unscaledTime\": ").Append(TdJson.Num(_t0Unscaled, 6))
              .Append(", \"frameCount\": ").Append(_t0Frame).Append("},\n");
            sb.Append("  \"frameTime\": \"frames.tsv t = realtimeSinceStartup at the end-of-frame grab - t0.realtimeSinceStartup (real seconds; the game is not slowed)\",\n");
            sb.Append("  \"audio\": {\"armed\": ").Append(TdJson.Bool(_audioArmed))
              .Append(", \"wav\": ").Append(TdJson.Str(_audioPath))
              .Append(", \"sampleRate\": ").Append(_audioRate)
              .Append(", \"framesCapturedAtT0\": ").Append(_audioArmedAtT0 ? _audioF0.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(", \"droppedSamplesAtT0\": ").Append(_audioArmedAtT0 ? _audioDropped0.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(", \"armedAtT0\": ").Append(TdJson.Bool(_audioArmedAtT0))
              .Append(", \"anchorT\": ").Append(_audioAnchored ? TdJson.Num(_audioAnchorT, 6) : "null")
              .Append(", \"anchorFrames\": ").Append(_audioAnchored ? _audioAnchorF.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(", \"wavOffsetSeconds\": ").Append(TdJson.Num(WavOffsetSeconds, 6))
              .Append(",\n    \"formula\": \"wavFrame(t) = anchorFrames + (t - anchorT) * sampleRate, i.e. tdmux --wav-offset = anchorFrames / sampleRate - anchorT (= framesCapturedAtT0 / sampleRate when audio flows at t0; negative = the WAV starts after video t0); audio-sync.tsv: one anchor per second for a drift / gap check\"},\n");
            if (_o.Mode == TdCaptureMode.Boot)
            {
                sb.Append("  \"phases\": ");
                AppendPhases(sb);
                sb.Append(",\n  \"bootMenuSeconds\": ").Append(TdJson.Num(_o.BootMenuSeconds, 2)).Append(",\n");
            }
            sb.Append("  \"orientation\": {\"flip\": ").Append(TdJson.Bool(_flip)).Append(", \"method\": ").Append(TdJson.Str(_flipMethod)).Append("},\n");
            sb.Append("  \"backbuffer\": [").Append(_backW).Append(", ").Append(_backH).Append("], \"screen\": [").Append(_screenW0).Append(", ").Append(_screenH0)
              .Append("], \"output\": [").Append(_outW).Append(", ").Append(_outH).Append("]\n}\n");
            File.WriteAllText(Path.Combine(_videoDir, "sync.json"), sb.ToString());
        }

        private void FinishVideo()
        {
            if (_finished) return;
            _finished = true;
            AudioOutputCapture audio = AudioOutputCapture.Armed;
            long f1 = audio != null ? audio.FramesCaptured : 0;
            long dropped1 = audio != null ? audio.DroppedSamples : 0;
            double stopRealtime = _t0Realtime + Math.Max(0d, _stopT);
            double drift = _audioAnchored && audio != null && _audioRate > 0 ? (f1 - _audioAnchorF) / (double)_audioRate - (_stopT - _audioAnchorT) : double.NaN;
            int written = Volatile.Read(ref _framesWritten);

            try
            {
                if (_tsv != null) { _tsv.Flush(); _tsv.Dispose(); _tsv = null; }
                if (_audioTsv != null) { _audioTsv.Flush(); _audioTsv.Dispose(); _audioTsv = null; }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TdCapture] closing the tsv files: " + e.Message);
            }

            var sb = new StringBuilder();
            sb.Append("{\n  \"wp\": \"WP-099\",\n  \"mode\": ").Append(TdJson.Str(_o.Mode == TdCaptureMode.Boot ? "boot" : "video")).Append(",\n");
            AppendSeed(sb);
            sb.Append("  ").Append(HudJson()).Append(",\n");
            if (_o.Mode == TdCaptureMode.Boot)
            {
                sb.Append("  \"phases\": ");
                AppendPhases(sb);
                sb.Append(",\n  \"cameraModeAtFighting\": ").Append(TdJson.Str(_cameraModeAtFighting))
                  .Append(", \"orientationVerify\": {\"measured\": ").Append(_verifyMeasured).Append(", \"errStraight\": ").Append(TdJson.Num(_verifyErrStraight, 3))
                  .Append(", \"errFlipped\": ").Append(TdJson.Num(_verifyErrFlipped, 3))
                  .Append(", \"mismatch\": ").Append(TdJson.Bool(_verifyMeasured >= 0 && (_verifyMeasured == 1) != _flip)).Append("},\n");
            }
            sb.Append("  \"stopReason\": ").Append(TdJson.Str(_stopReason ?? "not-started")).Append(",\n");
            sb.Append("  \"fpsTarget\": ").Append(_o.VideoFps).Append(", \"secondsTarget\": ").Append(_o.VideoSeconds)
              .Append(", \"warmupSeconds\": ").Append(TdJson.Num(_o.WarmupSeconds, 2)).Append(", \"holdSpawns\": ").Append(TdJson.Bool(_o.HoldSpawns)).Append(",\n");
            sb.Append("  \"durationSeconds\": ").Append(TdJson.Num(Math.Max(0d, _stopT), 4)).Append(",\n");
            sb.Append("  \"framesListed\": ").Append(_framesListed).Append(", \"framesWritten\": ").Append(written)
              .Append(", \"encodeFailed\": ").Append(Volatile.Read(ref _encodeFailed)).Append(", \"readbackErrors\": ").Append(_readbackErrors)
              .Append(", \"inFlightAtStop\": ").Append(Volatile.Read(ref _inFlight)).Append(",\n");
            sb.Append("  \"lastEncodeError\": ").Append(TdJson.Str(_lastEncodeError)).Append(",\n");
            sb.Append("  \"slots\": {\"taken\": ").Append(_schedule != null ? _schedule.Taken : 0)
              .Append(", \"skipped\": ").Append(_schedule != null ? _schedule.Skipped : 0)
              .Append(", \"missed\": ").Append(_schedule != null ? _schedule.MissedSlots : 0)
              .Append(", \"busySkipped\": ").Append(_schedule != null ? _schedule.BusySkipped : 0).Append("},\n");
            sb.Append("  \"effectiveFps\": ").Append(TdJson.Num(TdFrameSchedule.EffectiveFps(written, _stopT), 3)).Append(",\n");
            sb.Append("  \"resolution\": {\"backbuffer\": [").Append(_backW).Append(", ").Append(_backH).Append("], \"screenAtT0\": [").Append(_screenW0).Append(", ").Append(_screenH0)
              .Append("], \"output\": [").Append(_outW).Append(", ").Append(_outH).Append("], \"maxWidth\": ").Append(_o.VideoMaxWidth).Append(", \"resizes\": ").Append(_resizes).Append("},\n");
            sb.Append("  \"orientation\": {\"flip\": ").Append(TdJson.Bool(_flip)).Append(", \"method\": ").Append(TdJson.Str(_flipMethod))
              .Append(", \"measured\": ").Append(_flipMeasured).Append(", \"errStraight\": ").Append(TdJson.Num(_flipErrStraight, 3))
              .Append(", \"errFlipped\": ").Append(TdJson.Num(_flipErrFlipped, 3)).Append(", \"refMeanLuma\": ").Append(TdJson.Num(_refMeanLuma, 2))
              .Append(", \"readbackMeanLuma\": ").Append(TdJson.Num(_grabMeanLuma, 2)).Append(", \"graphicsUVStartsAtTop\": ").Append(TdJson.Bool(SystemInfo.graphicsUVStartsAtTop))
              .Append(", \"device\": ").Append(TdJson.Str(SystemInfo.graphicsDeviceType.ToString())).Append("},\n");
            sb.Append("  \"start\": {\"realtime\": ").Append(TdJson.Num(_t0Realtime, 6)).Append(", \"dspTime\": ").Append(TdJson.Num(_t0Dsp, 6)).Append("},\n");
            sb.Append("  \"stop\": {\"realtime\": ").Append(TdJson.Num(stopRealtime, 6)).Append(", \"dspTime\": ").Append(TdJson.Num(AudioSettings.dspTime, 6)).Append("},\n");
            sb.Append("  \"audio\": {\"armed\": ").Append(TdJson.Bool(_audioArmed)).Append(", \"wav\": ").Append(TdJson.Str(_audioPath)).Append(", \"sampleRate\": ").Append(_audioRate)
              .Append(", \"framesCapturedAtT0\": ").Append(_audioArmedAtT0 ? _audioF0.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(", \"framesCapturedAtStop\": ").Append(_audioArmed && audio != null ? f1.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(", \"anchorT\": ").Append(_audioAnchored ? TdJson.Num(_audioAnchorT, 6) : "null")
              .Append(", \"anchorFrames\": ").Append(_audioAnchored ? _audioAnchorF.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(", \"wavOffsetSeconds\": ").Append(TdJson.Num(WavOffsetSeconds, 6))
              .Append(", \"driftSeconds\": ").Append(TdJson.Num(drift, 4))
              .Append(", \"droppedSamplesInWindow\": ").Append(_audioArmed && audio != null ? (dropped1 - _audioDropped0).ToString(CultureInfo.InvariantCulture) : "null").Append("}\n}\n");
            try
            {
                File.WriteAllText(Path.Combine(_videoDir, "summary.json"), sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[TdCapture] summary.json not written: " + e.Message);
            }

            Debug.Log("[TdCapture] video done reason=" + _stopReason + " frames " + written + "/" + _framesListed + " skipped " + (_schedule != null ? _schedule.Skipped : 0) +
                      " effectiveFps " + TdFrameSchedule.EffectiveFps(written, _stopT).ToString("F2", CultureInfo.InvariantCulture) + " drift " + TdJson.Num(drift, 4));
        }

        // ---------------------------------------------------------------- exits

        private void Fail(string stage, string message, int code)
        {
            if (_finished) return;
            _finished = true;
            WriteErrorFile(_o != null ? _o.OutDir : null, stage, message, code);
            Quit(code);
        }

        private static void WriteErrorFile(string outDir, string stage, string message, int code)
        {
            Debug.LogError("[TdCapture] " + stage + ": " + message + " (exit " + code + ")");
            if (string.IsNullOrEmpty(outDir)) return;
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "error.json"),
                    "{\"wp\": \"WP-099\", \"stage\": " + TdJson.Str(stage) + ", \"error\": " + TdJson.Str(message) + ", \"exitCode\": " + code +
                    ", \"scene\": " + TdJson.Str(SceneManager.GetActiveScene().name) + ", \"realtime\": " + TdJson.Num(Time.realtimeSinceStartupAsDouble, 3) + "}\n");
            }
            catch (Exception e)
            {
                Debug.LogError("[TdCapture] error.json not written: " + e.Message);
            }
        }

        private static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }
    }
}
#endif
