using System;
using System.Collections;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-081 · the main menu's cinematic backdrop (the user's G5 addition; AP-028 v002's five shots approved 05.10): the
    /// real run world - <see cref="WorldSourceSettings"/>, the map the player gets with no flag (World V6 today) - loaded
    /// additively under the menu as a view only (<see cref="WorldViewer"/>: no markers, no run systems, no HUD, no planar
    /// reflection), one perspective camera walking the shot list. 5 × 12 s = 60 s, seamless: each slice holds its start
    /// 1.2 s, moves 9.6 s on smoothstep, holds its end while the next shot's start fades in under the held frame for the
    /// last 1.2 s (S05 → S01 the same), so 0 s and 60 s are one image. The live camera is the only scene render; the held
    /// frame is one render texture. The clock is unscaled time from the menu's arrival; no package, no video.
    ///
    /// Still: Reduced Motion shows S01's start and nothing moves; a pane open or the window unfocused crossfades to S01's
    /// start and holds there (AP-028 loop.freeze), the loop starting again from it; while the curtain covers (leaving),
    /// the camera simply stops. The menu canvas goes over the live image as an overlay (no bloom or grain on words); the
    /// plates step aside, the spine's text-safe scrim (α 0.55, C-00 05.10 decision b) stays. If the world cannot load,
    /// the plates simply stay.
    /// </summary>
    public sealed class MenuCinematic : MonoBehaviour
    {
        public const string ShotResource = "MenuCinematic/menu-cinematic";   // AP-028 v002 shot-list.json (sha256 0372ad0a…) with S04 adapted locally on V6 (C-00 05.10; local_adaptation in the file)
        public const string Schema = "dd.menu-cinematic.design/2";
        public const float Slice = 12f;
        public const float Hold = 1.2f;
        public const float Move = 9.6f;
        public const float Blend = 1.2f;

        /// <summary>The arrival waits behind the curtain for the world at most this long; past it the world fades in when ready.</summary>
        public const float RevealCapSeconds = 8f;

        /// <summary>Dev builds: <c>-ddMenuWorld off</c> keeps the plates (the A/B baseline for the menu's frame cost).</summary>
        public const string Flag = "-ddMenuWorld";

        /// <summary>Dev builds: <c>-ddMenuShots &lt;path&gt;</c> - the shot list from a file (pose probes).</summary>
        public const string ShotsFlag = "-ddMenuShots";

        [Serializable]
        public sealed class Shot
        {
            public string id;
            public string landmark;
            public float[] position_start;
            public float[] position_end;
            public float[] target_start;
            public float[] target_end;
            public float vfov = 44f;
            public float[] screen_target_uv;

            public Vector3 Start => V(position_start);
            public Vector3 End => V(position_end);
            public Vector3 TargetStart => V(target_start);
            public Vector3 TargetEnd => V(target_end);
            public Vector2 ScreenTarget => screen_target_uv != null && screen_target_uv.Length >= 2 ? new Vector2(screen_target_uv[0], screen_target_uv[1]) : new Vector2(0.5f, 0.5f);

            private static Vector3 V(float[] a)
            {
                return a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
            }
        }

        [Serializable]
        private sealed class ShotFile
        {
            public string schema;
            public Shot[] shots;
        }

        /// <summary>A camera pose: where it stands, what it frames, its vertical FOV, and where on screen the target sits.</summary>
        public struct Pose
        {
            public Vector3 Position;
            public Vector3 Target;
            public float VerticalFov;
            public Vector2 ScreenTarget;
        }

        private static Shot[] s_shots;

        /// <summary>Tests: run in batch mode too (the Editor test runner is batch mode; the menu leaves the world out there).</summary>
        public static bool ForceInBatch;

        /// <summary>Capture seam (dev builds): a fixed loop time instead of the clock; null = live.</summary>
        public static float? TimeOverride;

        /// <summary>Tests: the window's focus as the backdrop sees it (the batch Editor never has focus); null = the real one.</summary>
        public static bool? FocusForTests;

        /// <summary>The backdrop of the menu that is up (null in any other scene).</summary>
        public static MenuCinematic Current { get; private set; }

        private Camera _camera;
        private GameObject _lookVolume;
        private RenderTexture _held;
        private RenderTexture[] _sliceEnds;   // R3 (C-00 07.10, F): each shot's end, drawn once on load; the cut only swaps textures
        private Vector2Int _sliceEndsSize;
        private RawImage _heldImage;
        private Canvas _heldCanvas;
        private float _clock;
        private int _heldSlice = -1;
        private float _freezeFadeAt = -1f;
        private bool _wasStill;
        private bool _live;
        private bool _failed;
        private Action _onLive;
        private Func<bool> _holdStill;
        private AsyncOperation _load;
        private float _beganAt;

        /// <summary>The shot list (Resources), five shots in order; empty when the file is missing or not this schema.</summary>
        public static Shot[] Shots
        {
            get
            {
                if (s_shots == null)
                {
                    // Dev builds: -ddMenuShots <path> reads the list from a file - pose probes for a shot's local adaptation.
                    string path = Debug.isDebugBuild ? Arg(ShotsFlag) : null;
                    string json = null;
                    if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                    {
                        json = System.IO.File.ReadAllText(path);
                        Debug.Log("[UI] Menu cinematic: shots from " + path + " (dev probe).");
                    }
                    else
                    {
                        TextAsset file = Resources.Load<TextAsset>(ShotResource);
                        json = file != null ? file.text : null;
                    }

                    ShotFile parsed = json != null ? JsonUtility.FromJson<ShotFile>(json) : null;
                    s_shots = parsed != null && parsed.schema == Schema && parsed.shots != null ? parsed.shots : new Shot[0];
                }

                return s_shots;
            }
        }

        public static float Period
        {
            get { return Shots.Length * Slice; }
        }

        /// <summary>The menu shows the world: a player (not batch, unless a test asks) with the shot list and no <c>-ddMenuWorld off</c>.</summary>
        public static bool Allowed
        {
            get
            {
                if (Application.isBatchMode && !ForceInBatch)
                {
                    return false;
                }

                return Shots.Length > 0 && !(Debug.isDebugBuild && Arg(Flag) == "off");
            }
        }

        /// <summary>True while the arrival should wait behind the curtain: the world is loading and the cap has not passed.</summary>
        public static bool HoldsReveal
        {
            get
            {
                MenuCinematic c = Current;
                return c != null && !c._live && !c._failed && Time.unscaledTime - c._beganAt < RevealCapSeconds;
            }
        }

        /// <summary>The world loading under the menu, 0..1 (the curtain's ring while the arrival waits for it).</summary>
        public static float LoadProgress
        {
            get
            {
                MenuCinematic c = Current;
                return c == null || c._live || c._load == null ? 1f : Mathf.Clamp01(c._load.progress / 0.9f);
            }
        }

        public bool Live => _live;
        public float Clock => _clock;
        public Camera Camera => _camera;
        public bool Still => _wasStill;

        /// <summary>The shot ends drawn on load (tests, logs): one per shot, or 0 when they were never drawn or let go.</summary>
        public int PrewarmedSliceEnds
        {
            get
            {
                int n = 0;
                if (_sliceEnds != null)
                {
                    foreach (RenderTexture rt in _sliceEnds)
                    {
                        n += rt != null ? 1 : 0;
                    }
                }

                return n;
            }
        }

        /// <summary>The name every pre-drawn shot end carries (tests count the live ones: none outside the menu).</summary>
        public const string SliceEndName = "MenuCinematicSliceEnd";

        private static string Arg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        // ======================================================================== the timeline (pure, tested)

        public static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        /// <summary>The slice at <paramref name="time"/> (0-based, wrapping at the period) and the time inside it.</summary>
        public static int SliceAt(float time, out float local)
        {
            int count = Mathf.Max(1, Shots.Length);
            float t = Mathf.Repeat(time, count * Slice);
            int slice = Mathf.Min(count - 1, Mathf.FloorToInt(t / Slice));
            local = t - slice * Slice;
            return slice;
        }

        /// <summary>The shot's pose at <paramref name="local"/> seconds: held 1.2 s, smoothstep over 9.6 s, held to the end.</summary>
        public static Pose PoseOf(Shot shot, float local)
        {
            float u = Smooth((local - Hold) / Move);
            return new Pose
            {
                Position = Vector3.Lerp(shot.Start, shot.End, u),
                Target = Vector3.Lerp(shot.TargetStart, shot.TargetEnd, u),
                VerticalFov = shot.vfov,
                ScreenTarget = shot.ScreenTarget
            };
        }

        /// <summary>The held frame's weight over the live next shot: 1 when the blend starts, smoothstep down to 0 at the seam.</summary>
        public static float HeldWeight(float local)
        {
            return 1f - Smooth((local - (Slice - Blend)) / Blend);
        }

        /// <summary>True inside a slice's last 1.2 s (the held end over the next shot's start).</summary>
        public static bool Blending(float local)
        {
            return local >= Slice - Blend;
        }

        /// <summary>The live camera's pose at <paramref name="time"/>: the slice's own, or - inside its blend - the next shot's start.</summary>
        public static Pose LivePose(float time)
        {
            int slice = SliceAt(time, out float local);
            Shot[] shots = Shots;
            if (Blending(local))
            {
                return PoseOf(shots[(slice + 1) % shots.Length], 0f);
            }

            return PoseOf(shots[slice], local);
        }

        /// <summary>
        /// The camera's rotation for a pose: no roll (the horizon stays level), turned so the target lands at its screen point
        /// (AP-028: u 0.68, v 0.48 - the art's focus right of centre, clear of the spine) at this aspect. In camera space the
        /// target lies along (a, b, 1); the pitch is the one that gives that ray the target's elevation, the yaw (about world
        /// up) the one that gives it the target's bearing.
        /// </summary>
        public static Quaternion RotationOf(Pose pose, float aspect)
        {
            Vector3 w = pose.Target - pose.Position;
            if (w.sqrMagnitude < 1e-6f)
            {
                w = Vector3.forward;
            }

            w.Normalize();
            float tanV = Mathf.Tan(pose.VerticalFov * 0.5f * Mathf.Deg2Rad);
            float a = (pose.ScreenTarget.x - 0.5f) * 2f * tanV * aspect;
            float b = (pose.ScreenTarget.y - 0.5f) * 2f * tanV;
            float n = Mathf.Sqrt(a * a + b * b + 1f);
            // Unity's pitch (about X) takes (a, b, 1) to y = b cos p - sin p = r cos(p + phi), r = sqrt(b² + 1), phi = atan2(1, b).
            float r = Mathf.Sqrt(b * b + 1f);
            float phi = Mathf.Atan2(1f, b);
            float pitch = Mathf.Acos(Mathf.Clamp(n * w.y / r, -1f, 1f)) - phi;
            float z1 = b * Mathf.Sin(pitch) + Mathf.Cos(pitch);
            float yaw = Mathf.Atan2(w.x, w.z) - Mathf.Atan2(a, z1);
            return Quaternion.Euler(pitch * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, 0f);
        }

        /// <summary>Puts <paramref name="camera"/> at a pose (the live camera, the held frame, the capture and the tests).</summary>
        public static void Place(Camera camera, Pose pose)
        {
            camera.transform.position = pose.Position;
            camera.fieldOfView = pose.VerticalFov;
            camera.transform.rotation = RotationOf(pose, camera.aspect);
        }

        // ======================================================================== runtime

        /// <summary>
        /// Starts the backdrop on <paramref name="owner"/> (the menu screen); <paramref name="onLive"/> runs on the frame the
        /// world first shows, <paramref name="holdStill"/> says when the menu wants the image still (a pane is open).
        /// Null when the world is not shown (batch, the flag, no shot list, no authored world in the build).
        /// </summary>
        public static MenuCinematic Begin(GameObject owner, Action onLive, Func<bool> holdStill)
        {
            if (!Allowed || Current != null)
            {
                return null;
            }

            WorldSource source = WorldSourceSettings.Resolve(out string worldScene);
            if ((source != WorldSource.AP005 && source != WorldSource.V6) || !Application.CanStreamedLevelBeLoaded(worldScene))
            {
                Debug.Log("[UI] Menu cinematic: no authored world to show (" + source + "); the plates stay.");
                return null;
            }

            var cinematic = owner.AddComponent<MenuCinematic>();
            cinematic._onLive = onLive;
            cinematic._holdStill = holdStill;
            cinematic._beganAt = Time.unscaledTime;
            Current = cinematic;
            // Set before the load starts: the world scene's bootstrap reads it in its Awake on activation.
            WorldViewer.Active = true;
            cinematic._load = SceneManager.LoadSceneAsync(worldScene, LoadSceneMode.Additive);
            if (cinematic._load == null)
            {
                cinematic._failed = true;
                WorldViewer.Active = false;
                Debug.Log("[UI] Menu cinematic: '" + worldScene + "' could not start loading; the plates stay.");
                return cinematic;
            }

            cinematic.StartCoroutine(cinematic.WaitForWorld(worldScene));
            return cinematic;
        }

        private IEnumerator WaitForWorld(string worldScene)
        {
            while (!_load.isDone)
            {
                yield return null;
            }

            BuildCamera();
            _lookVolume = MenuLook.Current.ApplyWorld();   // the menu's grade / light over the world (C-00 05.10 options)
            yield return null;                              // the world's Start has run: the shot ends see it as the live view will
            PrewarmSliceEnds();
            _clock = 0f;
            Apply(0f, true);
            UiFactory.MoveScreensToOverlay();   // the frame is full now: the menu goes over it as an overlay
            _live = true;
            Debug.Log("[UI] Menu cinematic live: " + worldScene + ", " + Shots.Length + " shots, " + Period + " s, +"
                + (Time.unscaledTime - _beganAt).ToString("F2") + " s after the menu.");
            if (_onLive != null)
            {
                _onLive();
            }
        }

        private void BuildCamera()
        {
            // The gameplay camera's set-up (CameraRigFactory.Create): sky clear, HDR, MSAA, the world's Look B volume
            // through WorldAtmosphere.ConfigureCamera. Never the MainCamera tag - the UI camera lookup must not find it.
            var go = new GameObject("MenuCinematicCamera");
            _camera = go.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            _camera.nearClipPlane = 0.1f;
            _camera.cullingMask = ~(1 << 5);   // everything but the UI layer
            _camera.depth = -60f;              // under any other camera; the menu canvas draws over it as an overlay
            WorldAtmosphere.ConfigureCamera(_camera);
        }

        private void Update()
        {
            if (!_live)
            {
                return;
            }

            bool still = false;
            if (TimeOverride.HasValue)
            {
                _clock = TimeOverride.Value;
            }
            else
            {
                still = UiMotion.ReducedMotion || !(FocusForTests ?? Application.isFocused) || (_holdStill != null && _holdStill());
                if (still && !_wasStill)
                {
                    // Into the still: the view so far crossfades to S01's start (RM: a cut - nothing moves).
                    if (!UiMotion.ReducedMotion && !UiCurtain.IsCovering && _clock > 0f)
                    {
                        HoldFrame(null);
                        _freezeFadeAt = Time.unscaledTime;
                    }

                    _clock = 0f;
                }
                else if (!still && !UiCurtain.IsCovering)
                {
                    _clock += Time.unscaledDeltaTime;
                }
            }

            _wasStill = still;
            Apply(_clock, false);
        }

        /// <summary>Puts the camera where the timeline says and fades the held frame over what it shows now.</summary>
        private void Apply(float time, bool first)
        {
            int slice = SliceAt(time, out float local);
            bool blending = Blending(local);
            if (blending && _heldSlice != slice)
            {
                // R3 (C-00 07.10, F): the slice's end was drawn on load, the cut swaps the texture - no second world render in
                // the cut's frame (b31: 85 ms there). A screen that changed size since falls back to rendering it now.
                if (!ShowSliceEnd(slice))
                {
                    HoldFrame(PoseOf(Shots[slice], Slice));
                }

                _heldSlice = slice;
                _freezeFadeAt = -1f;
            }
            else if (!blending)
            {
                _heldSlice = -1;
            }

            Place(_camera, LivePose(time));
            float weight = blending ? HeldWeight(local) : 0f;
            if (_freezeFadeAt >= 0f)
            {
                float freeze = 1f - Smooth((Time.unscaledTime - _freezeFadeAt) / Blend);
                weight = Mathf.Max(weight, freeze);
                if (freeze <= 0f)
                {
                    _freezeFadeAt = -1f;
                }
            }

            if (_heldImage != null)
            {
                _heldImage.enabled = !first && weight > 0.001f;
                _heldImage.color = new Color(1f, 1f, 1f, weight);
            }
        }

        /// <summary>Renders a pose (null = where the camera stands now) once into the held frame shown over the live camera.</summary>
        private void HoldFrame(Pose? pose)
        {
            RenderInto(pose);
            EnsureHeldImage();
            _heldImage.texture = _held;
        }

        /// <summary>Renders a pose (null = where the camera stands now) into <see cref="_held"/>, the screen-sized MSAA target.</summary>
        private void RenderInto(Pose? pose)
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (_held != null && (_held.width != width || _held.height != height))
            {
                _held.Release();
                Destroy(_held);
                _held = null;
            }

            if (_held == null)
            {
                // The pipeline's MSAA (URP takes a target texture's own sample count): at 1 the held frame was the only aliased one -
                // each blend opened with a one-frame sharpness step (video check 06.10: 7-26 %).
                var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                _held = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
                {
                    name = "MenuCinematicHeld",
                    antiAliasing = _camera.allowMSAA && pipeline != null ? Mathf.Max(1, pipeline.msaaSampleCount) : 1
                };
            }

            if (pose.HasValue)
            {
                Place(_camera, pose.Value);
            }

            _camera.targetTexture = _held;
            _camera.Render();
            _camera.targetTexture = null;
        }

        private void EnsureHeldImage()
        {
            if (_heldImage != null)
            {
                return;
            }

            var go = new GameObject("MenuCinematicHeld", typeof(RectTransform), typeof(Canvas), typeof(RawImage));
            _heldCanvas = go.GetComponent<Canvas>();
            _heldCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _heldCanvas.sortingOrder = UiTokens.SortingScreen - 1;   // over the world, under the menu
            _heldImage = go.GetComponent<RawImage>();
            _heldImage.raycastTarget = false;
            _heldImage.enabled = false;
            UiFactory.Fill(_heldImage.rectTransform);
        }

        /// <summary>
        /// R3 (C-00 07.10, option F): every shot's end, drawn once while the doors are still closed - into the MSAA target, then
        /// resolved into its own colour-only texture (1080p 5 × 8.3 MB, the Mac's 3024 × 1964 5 × 23.8 MB). They live while the
        /// menu does (OnDestroy lets them go: none during a run).
        /// </summary>
        private void PrewarmSliceEnds()
        {
            Shot[] shots = Shots;
            if (shots.Length == 0 || _camera == null)
            {
                return;
            }

            float started = Time.realtimeSinceStartup;
            ReleaseSliceEnds();
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            _sliceEnds = new RenderTexture[shots.Length];
            for (int i = 0; i < shots.Length; i++)
            {
                RenderInto(PoseOf(shots[i], Slice));
                _sliceEnds[i] = new RenderTexture(width, height, 0, RenderTextureFormat.Default) { name = SliceEndName + i };
                Graphics.Blit(_held, _sliceEnds[i]);   // resolves the MSAA target into the shot's own texture
            }

            _sliceEndsSize = new Vector2Int(width, height);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[UI] Menu cinematic: {0} shot ends drawn on load ({1}x{2}, {3:F1} MB, {4:F0} ms).",
                shots.Length, width, height, shots.Length * (double)width * height * 4.0 / (1024.0 * 1024.0),
                (Time.realtimeSinceStartup - started) * 1000f));
        }

        /// <summary>Shows the pre-drawn end of <paramref name="slice"/>; false when there is none for this screen size.</summary>
        private bool ShowSliceEnd(int slice)
        {
            if (_sliceEnds == null || slice < 0 || slice >= _sliceEnds.Length || _sliceEnds[slice] == null)
            {
                return false;
            }

            if (_sliceEndsSize.x != Screen.width || _sliceEndsSize.y != Screen.height)
            {
                Debug.Log("[UI] Menu cinematic: the screen changed size since the shot ends were drawn - the cut renders its frame.");
                ReleaseSliceEnds();
                return false;
            }

            EnsureHeldImage();
            _heldImage.texture = _sliceEnds[slice];
            return true;
        }

        private void ReleaseSliceEnds()
        {
            if (_sliceEnds == null)
            {
                return;
            }

            foreach (RenderTexture rt in _sliceEnds)
            {
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
            }

            _sliceEnds = null;
        }

        private void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }

            WorldViewer.Active = false;
            UiFactory.OverlayScreens = false;
            if (_held != null)
            {
                _held.Release();
                Destroy(_held);
            }

            ReleaseSliceEnds();   // R3 F: the shot ends go with the menu - 0 MB during a run
            if (_heldCanvas != null)
            {
                Destroy(_heldCanvas.gameObject);
            }

            if (_camera != null)
            {
                Destroy(_camera.gameObject);
            }

            if (_lookVolume != null)
            {
                Destroy(_lookVolume);
            }
        }

        /// <summary>Tests: forget the loaded shot list and the seams.</summary>
        public static void ResetForTests()
        {
            s_shots = null;
            TimeOverride = null;
            ForceInBatch = false;
            FocusForTests = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_shots = null;
            TimeOverride = null;
            ForceInBatch = false;
            FocusForTests = null;
            Current = null;
            UiFactory.OverlayScreens = false;
        }
    }
}
