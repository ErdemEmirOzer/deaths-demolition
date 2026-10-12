using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DD.Prototype.UI
{
    /// <summary>
    /// R4 WP-106 P1 · Prepare Run's weapon stage: the chosen classic weapon, turning on the AP-040 v001 display pedestal, drawn
    /// off screen into a render texture that a RawImage shows inside the screen's layout (<see cref="Create"/>).
    ///
    /// THE SET. AP-040's Prepare Run composition (preview/AP040_PrepareRun_v001.json, Blender metres through
    /// <see cref="BlenderToUnity"/>, the one conversion - AD-REVIEW-WP106 S1: [−x, z, −y], as Unity imports the FBX; the json's
    /// [x, z, −y] mirrored the whole set): the rotatable F1 pedestal at the origin (SOCKET_Turntable 0.70 m, SOCKET_Weapon 0.94 m
    /// under the rotor), the shoji backdrop at z −1.3, the warm key lantern at x +1.05, the cool fill lantern at x −1.05
    /// (<see cref="Set"/>). <c>PrepareRunStageBuilder</c> (UI/Editor) builds it from the FBX into <see cref="StageResource"/>; the
    /// stage puts each piece where <see cref="Set"/> says whatever the prefab was built with. Without the prefab the stage still
    /// turns the weapon on a bare pivot at the same heights (<see cref="HasSet"/> false). The json's three Blender area lights
    /// become URP spots at the same places and aims, colour by temperature, intensity <see cref="SpotIntensity"/>; AD M2 adds
    /// <c>PR_PanelWash</c> on the shoji, out of the weapon's way (<see cref="Lights"/>). The camera is AD M1's: an 80 mm lens at
    /// 4.4 m, 10° up, on the seated weapons' centre (<see cref="OrbitTarget"/>). The stage owns a Neutral tonemap (the world's mode,
    /// <see cref="WorldAtmosphere.PostFx"/>) on its own volume; the ground is <c>SurfaceInset</c>, never lighter than the set.
    ///
    /// THE WEAPON. The model the run puts in the hand (WeaponController.EnsureClassicModel's order): the
    /// <see cref="ClassicWeaponVisualLibrary"/> entry, else its class sibling's (presentation.placeholderVisual), else the roster
    /// blockout. ADR-042-1 frame (root at the grip, +Z barrel, +Y up), upright, the barrel along the rotor's two post caps
    /// (x ±0.22, top 0.894 m); the turntable rests turned so the posts' line is square to the first view, muzzle to the screen's
    /// left (the AP-036 side views). One uniform scale from its LOD0 bounds: the weapon's class display length
    /// (<see cref="DisplayLength"/>), at most <see cref="MaxWeaponHeight"/> tall. It lies on the post caps (AD M1): each AP-021
    /// model's slide, tilt and lift come from <see cref="Seats"/> (measured offline from its LOD0 triangles; checked against the
    /// imported meshes by PrepareRunStageTests); a model without a seat stands level with its lowest point on the cap plane. LOD0
    /// is pinned (a close-up). Each model is built once per id and kept, hidden, for the next time.
    ///
    /// ISOLATION. Everything lives under one root at <see cref="StageOrigin"/>, 2 km below the menus' origin, on layer 5 (UI):
    /// the world cameras leave that layer out (MenuCinematic ~(1 &lt;&lt; 5), PlanarReflection mask &amp; ~(1 &lt;&lt; 5)), the
    /// volumetric light skips a camera without Environment, and the cameras that do draw it (the UI camera's 10 m box at the
    /// origin, a gameplay camera's far plane) never reach 2 km down. The stage camera draws that layer only, on the feature-free
    /// renderer (<see cref="FeatureFreeRendererIndex"/>), never tagged MainCamera, always into its own target (UiFactory's screen
    /// camera lookup skips it). Its lights reach that layer only; renderers sample no probes (nothing is baked down there).
    ///
    /// DRAWING. The camera is never enabled: LateUpdate renders it on demand (Camera.Render), once, on the frames something
    /// changed - an orbit, a zoom, a weapon, the auto turn (at most <see cref="AutoTurnFps"/> renders a second) or a lost /
    /// resized target - and only while the view shows (active, on an enabled canvas, alpha above zero, the curtain not shut). The target follows the view's size in pixels, the short
    /// side at most <see cref="MaxShortSide"/>. Per frame nothing is allocated (the PlayMode test measures it).
    ///
    /// INPUT. Mouse: drag (left button) turns the weapon (x) and tilts the camera (y), the wheel zooms, a double click resets
    /// the view. Pad: <see cref="UiInput.ViewStick"/> turns and tilts, <see cref="UiInput.ViewZoomAxis"/> zooms (RT in, LT out) -
    /// UiInput only, never the Input System (ADR-024-2). The view is no focus stop: the pad's controls work while the focus is
    /// on the screen's rows. A modal over the stage, or the curtain, takes the pad's controls away.
    ///
    /// MOTION. The pitch keeps to the json's 8°..35°, the distance to 3.8..6.1 m; the turn is free (the turntable). The auto turn
    /// is a slow idle spin that waits <see cref="AutoTurnResumeSeconds"/> after the last interaction; Reduced Motion stops it.
    /// The clock is UiMotion's unscaled clock (TestNow in tests).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrepareRunStage : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        // ------------------------------------------------------------------ names and resources

        /// <summary>The AP-040 set prefab (PrepareRunStageBuilder): Assets/_Prototype/Art/AP040/PrepareRun/Resources/PrepareRun/PR_Stage.prefab.</summary>
        public const string StageResource = "PrepareRun/PR_Stage";

        public const string TurntableSocket = "SOCKET_Turntable";
        public const string WeaponSocket = "SOCKET_Weapon";

        public const string ViewName = "PrepareRunStage.View";
        public const string WorldName = "PrepareRunStage.World";
        public const string CameraName = "PrepareRunStage.Camera";
        public const string TargetName = "PrepareRunStage.Target";
        public const string ProfileName = "PrepareRunStage.Look";
        public const string MountName = "PrepareRunStage.Mount";

        /// <summary>Layer 5 "UI" (TagManager): the one named layer every world camera leaves out (see the class remarks).</summary>
        public const int StageLayer = 5;

        /// <summary>DD_URP_Renderer_Planar: no renderer features - the second entry of every tier's pipeline asset (PlanarReflection.PlanarRendererIndex).</summary>
        public const int FeatureFreeRendererIndex = 1;

        public const int MaxShortSide = 1024;
        public const int MaxLongSide = 2048;
        public const int MinSide = 16;

        /// <summary>The stage's root in the world: 2 km under the menus' origin, outside every other camera's reach.</summary>
        public static readonly Vector3 StageOrigin = new Vector3(0f, -2000f, 0f);

        // ------------------------------------------------------------------ the composition (stage space, Unity metres)

        /// <summary>
        /// AD-REVIEW-WP106 M1 (12.10): the orbit target is the weapon's centre on the rotor axis - the turn stays on the weapon. The
        /// review's 1.08 m was the centre of a weapon standing with its foot on the socket; lying on the caps (<see cref="Seats"/>) the
        /// ten centres are 0.88..1.04 m (mean 0.93), so 0.94 m keeps every one in the review's 40-60 % band from the top.
        /// </summary>
        public static readonly Vector3 OrbitTarget = new Vector3(0f, 0.94f, 0f);

        /// <summary>AD M1: an 80 mm lens on Blender's 36 mm sensor, sensor fit Auto (the frame's longer side) - 25.4° across the well.</summary>
        public const float LensMm = 80f;
        public const float SensorMm = 36f;

        /// <summary>
        /// AD M1: the first pose - AP-040's 28.6° azimuth mirrored (S1: the camera stands at −x), 10° above the horizon, 4.4 m from
        /// the target: the rifle fills ~48 % of the well's width, the shoji the whole background.
        /// </summary>
        public const float InitialAzimuth = -28.6f;
        public const float InitialPitch = 10f;
        public const float InitialDistance = 4.4f;

        /// <summary>The first pose's camera, (−2.07, 1.70, 3.80) (the review's (−2.07, 1.84, 3.80) on the lower target).</summary>
        public static readonly Vector3 InitialCamera = OrbitTarget + OrbitOffset(InitialAzimuth, InitialPitch, InitialDistance);

        /// <summary>AD M1: the tilt range (AP-040 8°..35° with the top at 30°) and the zoom range 3.0..5.6 m (no class over 85 % of the well).</summary>
        public const float PitchMin = 8f;
        public const float PitchMax = 30f;
        public const float DistanceMin = 3.0f;
        public const float DistanceMax = 5.6f;

        /// <summary>The turntable's rest turn (degrees about up): the post line square to the first view, the muzzle to the screen's left.</summary>
        public const float TurntableRestYaw = InitialAzimuth;

        /// <summary>The weapon mount's turn on the rotor: the barrel (+Z) along the post line (the rotor's +X).</summary>
        public const float MountYaw = 90f;

        /// <summary>The bare pivot's heights when the set prefab is missing: SOCKET_Turntable 0.70 m, SOCKET_Weapon 0.94 m.</summary>
        public static readonly Vector3 TurntableLocal = new Vector3(0f, 0.70f, 0f);
        public static readonly Vector3 WeaponSocketLocal = new Vector3(0f, 0.24f, 0f);

        /// <summary>
        /// The F1 rotor's two post caps (AP040_display_pedestal_rotatable_V01.fbx, measured offline from SM_display_pedestal_turntable
        /// LOD0): centres x ±0.22 on the rotor, caps 0.11 m along the line × 0.14 m across, top 0.894 m; the disc's top 0.704 m.
        /// </summary>
        public const float PostOffset = 0.22f;
        public const float PostCapLength = 0.11f;
        public const float PostCapDepth = 0.14f;
        public const float PostCapTop = 0.894f;
        public const float WeaponSocketHeight = 0.94f;
        public const float RotorDiscTop = 0.704f;

        /// <summary>The caps' top in the weapon mount (on SOCKET_Weapon): 4.6 cm below the socket.</summary>
        public const float CapBelowSocket = PostCapTop - WeaponSocketHeight;

        /// <summary>The tallest a weapon stands on the socket (m): the launcher's sight never climbs into the lanterns.</summary>
        public const float MaxWeaponHeight = 0.42f;

        /// <summary>
        /// One AP-021 model's rest on the post caps, in the weapon mount (+Z along the post line, +Y up, origin on SOCKET_Weapon):
        /// the model's LOD0 centre slides <see cref="Slide"/> m along the line, the model tilts <see cref="Tilt"/>° about the mount's
        /// x (+ nose down) and its centre stands <see cref="Lift"/> m above the socket - so its underside lies on both caps
        /// (<see cref="Gap"/>: the rise over the other cap where two-cap contact is out of reach). <see cref="Scale"/> is the fit the
        /// numbers were measured at; a model whose fit differs (re-exported) falls back to standing level on the cap plane.
        /// </summary>
        public readonly struct Seat
        {
            public readonly string Key;
            public readonly float Scale;
            public readonly float Slide;
            public readonly float Tilt;
            public readonly float Lift;
            public readonly float Gap;

            public Seat(string key, float scale, float slide, float tilt, float lift, float gap)
            {
                Key = key;
                Scale = scale;
                Slide = slide;
                Tilt = tilt;
                Lift = lift;
                Gap = gap;
            }
        }

        /// <summary>
        /// AD M1 · the AP-021 v003 classics on the F1 posts (K/wp-106/draft/stage-ad/tools/seat.py: LOD0 triangles clipped to each cap's
        /// footprint, slide within ±0.12 m - the well's ±6 % - tilt within ±10°, lowest point ≥ 0.71 m). Seven lie on both caps (gap
        /// &lt; 1 mm). rl_1 lies on both when slid 0.1325 m (6.7 % of the well, S-09 12.10: seated over centred). Two cannot within
        /// those bounds - a grip or magazine hangs where the second cap would be - and lie level on one cap: ar_2 (2.9 cm over the
        /// other), smg_1 (7.1 cm); an AP-040 cradle for them is asked of C-00.
        /// </summary>
        public static readonly Seat[] Seats =
        {
            new Seat("ar_1", 0.7917f, +0.0200f, +5.122f, -0.0503f, 0f),
            new Seat("ar_2", 0.8637f, +0.0475f, 0f, +0.0456f, 0.0287f),
            new Seat("smg_1", 0.9412f, -0.0275f, 0f, -0.0464f, 0.0708f),
            new Seat("smg_2", 1.1111f, +0.0600f, -0.170f, -0.0563f, 0.0003f),
            new Seat("sg_1", 1.1539f, -0.0125f, +4.019f, -0.0143f, 0.0002f),
            new Seat("sg_2", 0.9783f, -0.0475f, +1.009f, -0.0163f, 0.0002f),
            new Seat("lmg_1", 0.8076f, +0.0875f, +2.853f, -0.0165f, 0.0004f),
            new Seat("lmg_2", 0.8751f, -0.0300f, -1.261f, +0.0971f, 0.0001f),
            new Seat("sr_1", 0.7589f, +0.0250f, +5.993f, -0.0435f, 0f),
            new Seat("rl_1", 0.9545f, +0.1325f, -0.100f, +0.0096f, 0.0004f),   // S-09: slid past ±0.12 to lie on both caps (6.7 %)
        };

        /// <summary>The fit a seat holds for (relative): past it the model was re-exported and its seat is stale.</summary>
        public const float SeatScaleTolerance = 0.005f;

        /// <summary>One piece of the AP-040 set: the prefab child, its FBX, its place in Blender metres (preview json placements).</summary>
        public readonly struct SetPiece
        {
            public readonly string Name;
            public readonly string Model;
            public readonly Vector3 Blender;

            public SetPiece(string name, string model, Vector3 blender)
            {
                Name = name;
                Model = model;
                Blender = blender;
            }

            public Vector3 Position
            {
                get { return BlenderToUnity(Blender); }
            }
        }

        /// <summary>placements[] (position_blender_m): the pedestal, the backdrop, the warm key lantern V01, the cool fill lantern V02.</summary>
        public static readonly SetPiece[] Set =
        {
            new SetPiece("PR_Pedestal", "AP040_display_pedestal_rotatable_V01.fbx", new Vector3(0f, 0f, 0f)),
            new SetPiece("PR_Backdrop", "AP040_backdrop_panel_V01.fbx", new Vector3(0f, 1.3f, 0f)),
            new SetPiece("PR_KeyLantern", "AP040_stage_lantern_V01.fbx", new Vector3(-1.05f, 0.4f, 0f)),
            new SetPiece("PR_FillLantern", "AP040_stage_lantern_V02.fbx", new Vector3(1.05f, 0.4f, 0f)),
        };

        /// <summary>
        /// One stage light as a URP spot, placed in Blender metres (<see cref="BlenderToUnity"/>). Intensity from the area light's
        /// watts (<see cref="SpotIntensity"/>) unless <see cref="FixedIntensity"/> is set (URP units at exposure 1); the cone covers
        /// <see cref="SpotCoverRadius"/> at the aim unless <see cref="FixedAngle"/> is set.
        /// </summary>
        public readonly struct StageLight
        {
            public readonly string Name;
            public readonly Vector3 BlenderPosition;
            public readonly Vector3 BlenderAim;
            public readonly float Watts;
            public readonly float Kelvin;
            public readonly bool Shadows;
            public readonly float FixedIntensity;
            public readonly float FixedAngle;

            public StageLight(string name, Vector3 blenderPosition, Vector3 blenderAim, float watts, float kelvin, bool shadows, float fixedIntensity = 0f, float fixedAngle = 0f)
            {
                Name = name;
                BlenderPosition = blenderPosition;
                BlenderAim = blenderAim;
                Watts = watts;
                Kelvin = kelvin;
                Shadows = shadows;
                FixedIntensity = fixedIntensity;
                FixedAngle = fixedAngle;
            }

            public Vector3 Position
            {
                get { return BlenderToUnity(BlenderPosition); }
            }

            public Vector3 Aim
            {
                get { return BlenderToUnity(BlenderAim); }
            }

            /// <summary>The spot's intensity on the stage (× <see cref="StageExposure"/>).</summary>
            public float Intensity
            {
                get { return FixedIntensity > 0f ? FixedIntensity * StageExposure : SpotIntensity(Watts); }
            }

            /// <summary>The spot's full cone, degrees.</summary>
            public float Angle
            {
                get { return FixedAngle > 0f ? FixedAngle : SpotAngle((Aim - Position).magnitude); }
            }
        }

        /// <summary>
        /// lights[] (position_blender_m / aim_blender_m / energy_w / temperature_K): PR_KeyWarm2800 (now at x +1.05, by the warm
        /// lantern), PR_FillCoolSoft (x −1.05), PR_RimWarmSoft; and AD M2's PR_PanelWash - Unity (0, 2.6, −0.5) to (0, 1.1, −1.3),
        /// 72°, 3000 K, 28 at exposure 1, no shadow - which washes the shoji and keeps the weapon (0, 1.05, 0) 46° off its axis,
        /// outside its 36° half cone.
        /// </summary>
        public static readonly StageLight[] Lights =
        {
            new StageLight("PR_KeyWarm2800", new Vector3(-1.05f, 0.205f, 1.47f), new Vector3(0f, 0f, 0.94f), 105f, 2800f, true),
            new StageLight("PR_FillCoolSoft", new Vector3(1.05f, 0.205f, 1.77f), new Vector3(0f, 0f, 1.05f), 62f, 6500f, false),
            new StageLight("PR_RimWarmSoft", new Vector3(0f, 0.85f, 2.65f), new Vector3(0f, 0f, 0.85f), 80f, 3400f, false),
            new StageLight("PR_PanelWash", new Vector3(0f, 0.5f, 2.6f), new Vector3(0f, 1.3f, 1.1f), 0f, 3000f, false, 28f, 72f),
        };

        /// <summary>AD M2: exposure on every stage light (1 = the physical conversion of the json's watts).</summary>
        public const float StageExposure = 1.3f;

        /// <summary>AD M2: the radius each json spot's cone covers at its aim point (m), the share of a cone at full strength, the range.</summary>
        public const float SpotCoverRadius = 1.6f;
        public const float SpotInnerShare = 0.55f;
        public const float SpotRangeFactor = 3f;

        // ------------------------------------------------------------------ input and motion

        /// <summary>A drag across the view's full height turns the weapon this far (x) and tilts the camera this far (y).</summary>
        public const float DragTurnPerHeight = 240f;
        public const float DragTiltPerHeight = 90f;

        /// <summary>The right stick at full tilt, degrees per second (turn, tilt).</summary>
        public const float StickTurnPerSecond = 150f;
        public const float StickTiltPerSecond = 60f;

        /// <summary>One zoom step divides the distance by this; the triggers run this many steps a second.</summary>
        public const float ZoomStep = 1.12f;
        public const float TriggerStepsPerSecond = 3f;

        /// <summary>Scroll lines per zoom step (the Input System UI module reports a Windows wheel notch as 6 lines) and the most one event takes.</summary>
        public const float ScrollLinesPerStep = 6f;
        public const float MaxScrollSteps = 2f;

        public const float AutoTurnDegreesPerSecond = 12f;
        public const float AutoTurnResumeSeconds = 2.5f;
        public const float AutoTurnFps = 30f;

        /// <summary>The longest step the clock takes in one frame (a hitch does not fling the view).</summary>
        public const float MaxStepSeconds = 0.1f;

        // ------------------------------------------------------------------ state

        private RawImage _view;
        private RectTransform _rect;
        private GameObject _root;
        private Transform _turntable;
        private Transform _mount;
        private Quaternion _turntableRest = Quaternion.identity;
        private Vector3 _spinAxis = Vector3.up;
        private Camera _camera;
        private RenderTexture _target;
        private VolumeProfile _profile;
        private Action<ScriptableRenderContext, Camera> _onEndCamera;

        private readonly GameObject[] _models = new GameObject[16];
        private GameObject _current;
        private int _weaponIndex = -1;

        private float _yaw;
        private float _pitch;
        private float _distance;
        private float _viewHeightPixels = 1f;
        private bool _dirty;
        private bool _autoDirty;
        private bool _renderDue;
        private bool _dragging;
        private bool _padActive;
        private float _lastTick = -1f;
        private float _lastInteraction = float.NegativeInfinity;
        private float _lastRenderAt = float.NegativeInfinity;

        // ------------------------------------------------------------------ API

        /// <summary>
        /// Builds the stage for <paramref name="host"/>: a RawImage filling it (the view, which takes the pointer), the off-screen
        /// set, lights and camera, and a render target the size of the host in pixels. Build time, never per frame.
        /// </summary>
        public static PrepareRunStage Create(RectTransform host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            RectTransform rect = UiFactory.Fill(UiFactory.NewRect(ViewName, host));
            rect.gameObject.layer = host.gameObject.layer;
            var view = rect.gameObject.AddComponent<RawImage>();
            view.raycastTarget = true;
            var stage = rect.gameObject.AddComponent<PrepareRunStage>();
            stage.Build(view);
            return stage;
        }

        /// <summary>The image the stage draws into (fills the host; the pointer's drag, wheel and double click land here).</summary>
        public RawImage View
        {
            get { return _view; }
        }

        /// <summary>The weapon on the pedestal (its v3 key, ar_1 … rl_1), or null before the first <see cref="ShowWeapon(string)"/>.</summary>
        public string WeaponId { get; private set; }

        /// <summary>
        /// Puts a classic weapon on the pedestal - its v3 key (ar_1 …) or an M3 name (AssaultRifle …). The model is built on its
        /// first show and kept; a later show of the same id only swaps which model is active. A Special or an unknown key changes
        /// nothing (logged).
        /// </summary>
        public void ShowWeapon(string classicWeaponId)
        {
            if (!TryResolve(classicWeaponId, out ClassicWeaponId id))
            {
                Debug.LogWarning("[UI] Prepare Run stage: '" + classicWeaponId + "' is not a classic weapon; the pedestal keeps " + (WeaponId ?? "nothing") + ".");
                return;
            }

            ShowWeapon(id);
        }

        /// <summary>Turns the weapon (x, degrees, free) and tilts the camera (y, degrees, kept to <see cref="PitchMin"/>..<see cref="PitchMax"/>).</summary>
        public void Orbit(Vector2 degrees)
        {
            if (degrees.x == 0f && degrees.y == 0f)
            {
                return;
            }

            _yaw = WrapYaw(_yaw + degrees.x);
            _pitch = ClampPitch(_pitch + degrees.y);
            _dirty = true;
            _lastInteraction = UiMotion.Now(UiClock.Unscaled);
        }

        /// <summary>Zooms by <paramref name="delta"/> steps (+ in, − out), the distance kept to <see cref="DistanceMin"/>..<see cref="DistanceMax"/>.</summary>
        public void Zoom(float delta)
        {
            if (delta == 0f)
            {
                return;
            }

            _distance = ZoomedDistance(_distance, delta);
            _dirty = true;
            _lastInteraction = UiMotion.Now(UiClock.Unscaled);
        }

        /// <summary>The weapon at its rest heading, the camera at the json's first pose.</summary>
        public void ResetView()
        {
            _yaw = 0f;
            _pitch = InitialPitch;
            _distance = InitialDistance;
            _dirty = true;
            _lastInteraction = UiMotion.Now(UiClock.Unscaled);
        }

        /// <summary>The turntable's turn from the rest heading, degrees in (−180, 180].</summary>
        public float Yaw
        {
            get { return _yaw; }
        }

        /// <summary>The camera's distance from the orbit target, metres.</summary>
        public float Distance
        {
            get { return _distance; }
        }

        /// <summary>The slow idle turn (on by default). Never turns under Reduced Motion, while the view is hidden or being handled.</summary>
        public bool AutoTurn { get; set; } = true;

        /// <summary>True while the pointer drags the view or the pad's view controls are held.</summary>
        public bool Interacting
        {
            get { return _dragging || _padActive; }
        }

        /// <summary>Tests and captures: the stage camera's renders so far.</summary>
        public int Renders { get; private set; }

        // ------------------------------------------------------------------ API (appended)

        /// <summary>As <see cref="ShowWeapon(string)"/> by runtime id.</summary>
        public void ShowWeapon(ClassicWeaponId id)
        {
            int index = (int)id;
            if (index < 0 || index >= _models.Length)
            {
                return;
            }

            if (_weaponIndex == index)
            {
                return;
            }

            GameObject next = _models[index];
            if (next == null)
            {
                next = BuildModel(id);
                _models[index] = next;
            }

            if (_current != null)
            {
                _current.SetActive(false);
            }

            _current = next;
            if (_current != null)
            {
                _current.SetActive(true);
            }

            _weaponIndex = index;
            WeaponId = KeyOf(id);
            _dirty = true;
        }

        /// <summary>The camera's elevation over the orbit target, degrees.</summary>
        public float Pitch
        {
            get { return _pitch; }
        }

        /// <summary>True when the AP-040 set prefab was found (false: the weapon turns on a bare pivot).</summary>
        public bool HasSet { get; private set; }

        /// <summary>The view shows: active, on an enabled canvas, alpha above zero, the curtain not shut.</summary>
        public bool Visible
        {
            get
            {
                if (_view == null || !_view.isActiveAndEnabled || UiCurtain.Shut)
                {
                    return false;
                }

                Canvas canvas = _view.canvas;
                if (canvas == null || !canvas.isActiveAndEnabled)
                {
                    return false;
                }

                CanvasRenderer renderer = _view.canvasRenderer;
                return renderer.GetInheritedAlpha() * renderer.GetAlpha() * _view.color.a > 0.001f;
            }
        }

        /// <summary>The render target (tests).</summary>
        public RenderTexture Target
        {
            get { return _target; }
        }

        /// <summary>Tests: models built so far (one per weapon id shown).</summary>
        internal int CachedModelCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _models.Length; i++)
                {
                    if (_models[i] != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Tests: the active weapon model (null before the first show).</summary>
        internal GameObject CurrentModel
        {
            get { return _current; }
        }

        /// <summary>Tests: the transform the weapon sits on (SOCKET_Weapon, or the bare pivot's).</summary>
        internal Transform WeaponSocketTransform
        {
            get { return _mount != null ? _mount.parent : null; }
        }

        internal Camera StageCamera
        {
            get { return _camera; }
        }

        // ------------------------------------------------------------------ pure rules (EditMode tests)

        /// <summary>The target's size for a view <paramref name="pixels"/> big: the short side at most <paramref name="maxShortSide"/>, the long at most <see cref="MaxLongSide"/>.</summary>
        public static Vector2Int TargetSize(Vector2 pixels, int maxShortSide)
        {
            float w = Mathf.Max(1f, pixels.x);
            float h = Mathf.Max(1f, pixels.y);
            float k = Mathf.Min(1f, maxShortSide / Mathf.Min(w, h));
            k = Mathf.Min(k, MaxLongSide / Mathf.Max(w, h));
            return new Vector2Int(Mathf.Max(MinSide, Mathf.RoundToInt(w * k)), Mathf.Max(MinSide, Mathf.RoundToInt(h * k)));
        }

        /// <summary>The vertical field of view (degrees) of a <paramref name="lensMm"/> lens on a <paramref name="sensorMm"/> sensor fitted to the frame's longer side.</summary>
        public static float VerticalFov(float lensMm, float sensorMm, float aspect)
        {
            float tanHalf = sensorMm * 0.5f / Mathf.Max(1e-3f, lensMm);
            if (aspect > 1f)
            {
                tanHalf /= aspect;   // landscape: the sensor's width runs across the frame
            }

            return 2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg;
        }

        /// <summary>The camera's offset from the orbit target: <paramref name="azimuth"/> from +Z toward +X, <paramref name="pitch"/> above the horizon.</summary>
        public static Vector3 OrbitOffset(float azimuth, float pitch, float distance)
        {
            float a = azimuth * Mathf.Deg2Rad;
            float p = pitch * Mathf.Deg2Rad;
            float flat = Mathf.Cos(p) * distance;
            return new Vector3(Mathf.Sin(a) * flat, Mathf.Sin(p) * distance, Mathf.Cos(a) * flat);
        }

        public static float AzimuthOf(Vector3 offset)
        {
            return Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        }

        public static float PitchOf(Vector3 offset)
        {
            float flat = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
            return Mathf.Atan2(offset.y, flat) * Mathf.Rad2Deg;
        }

        public static float ClampPitch(float pitch)
        {
            return Mathf.Clamp(pitch, PitchMin, PitchMax);
        }

        public static float ClampDistance(float distance)
        {
            return Mathf.Clamp(distance, DistanceMin, DistanceMax);
        }

        /// <summary>A turn in degrees folded into (−180, 180].</summary>
        public static float WrapYaw(float yaw)
        {
            return Mathf.DeltaAngle(0f, yaw);
        }

        /// <summary>The distance after <paramref name="steps"/> zoom steps (+ in), kept to the limits.</summary>
        public static float ZoomedDistance(float distance, float steps)
        {
            return ClampDistance(distance / Mathf.Pow(ZoomStep, steps));
        }

        /// <summary>A wheel event's zoom steps (+ in when the wheel turns away from the player), at most <see cref="MaxScrollSteps"/>.</summary>
        public static float ScrollSteps(float scrollLines)
        {
            return Mathf.Clamp(scrollLines / ScrollLinesPerStep, -MaxScrollSteps, MaxScrollSteps);
        }

        /// <summary>
        /// A Blender area light's watts as a URP spot's intensity: an area light of P W sends P/π W/sr along its normal, a diffuse
        /// surface returns E/π (Blender), URP's diffuse has no 1/π - so intensity = P / π² (× <see cref="StageExposure"/>).
        /// </summary>
        public static float SpotIntensity(float watts)
        {
            return watts / (Mathf.PI * Mathf.PI) * StageExposure;
        }

        /// <summary>The spot's full cone (degrees) for a light <paramref name="distance"/> m from its aim, covering <see cref="SpotCoverRadius"/>.</summary>
        public static float SpotAngle(float distance)
        {
            return Mathf.Clamp(2f * Mathf.Atan(SpotCoverRadius / Mathf.Max(0.1f, distance)) * Mathf.Rad2Deg, 30f, 120f);
        }

        /// <summary>
        /// Blender metres (+Z up, −Y forward, right-handed) as Unity metres the way Unity imports the AP-040 FBX: [−x, z, −y]
        /// (AD-REVIEW-WP106 S1, 12.10: the json's [x, z, −y] kept the handedness and mirrored the set against its own models -
        /// PR_Stage's SOCKET_Light at Blender (0, −0.115, 1.47) imports at (−0, 1.47, 0.115)). The one conversion in the stage.
        /// </summary>
        public static Vector3 BlenderToUnity(Vector3 blender)
        {
            return new Vector3(-blender.x, blender.z, -blender.y);
        }

        /// <summary>
        /// A weapon class's display length on the stand (m): the run's sizes in order (the SMG the shortest, the sniper the longest),
        /// pulled into the 0.80..1.10 m the pedestal holds (AD M1: AR 48 %, SMG 40 %, SR 56 % of the well at rest; ≤ 85 % at 3.0 m).
        /// </summary>
        public static float DisplayLength(WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClass.Smg: return 0.80f;
                case WeaponClass.Shotgun: return 0.90f;
                case WeaponClass.Lmg: return 1.05f;
                case WeaponClass.Sniper: return 1.10f;
                case WeaponClass.Launcher: return 1.05f;
                case WeaponClass.Special: return 1.00f;
                default: return 0.95f;   // assault rifle
            }
        }

        /// <summary>
        /// The one uniform scale for a model of local LOD0 <paramref name="size"/>: its horizontal run (the barrel, standing upright)
        /// to <paramref name="displayLength"/>, its height at most <paramref name="maxHeight"/>. 1 for an empty size.
        /// </summary>
        public static float FitScale(Vector3 size, float displayLength, float maxHeight)
        {
            float length = Mathf.Max(size.x, size.z);
            float scale = length > 1e-4f ? displayLength / length : 1f;
            if (size.y > 1e-4f)
            {
                scale = Mathf.Min(scale, maxHeight / size.y);
            }

            return scale;
        }

        /// <summary>The level fallback's local position in the mount: the model's lowest point on the caps' plane, centred on the rotor axis.</summary>
        public static Vector3 SeatOffset(Bounds bounds, float scale)
        {
            return new Vector3(-bounds.center.x * scale, CapBelowSocket - bounds.min.y * scale, -bounds.center.z * scale);
        }

        /// <summary>The seat of <paramref name="modelKey"/> when its table fit matches <paramref name="scale"/>; false otherwise.</summary>
        public static bool TrySeat(string modelKey, float scale, out Seat seat)
        {
            for (int i = 0; i < Seats.Length; i++)
            {
                if (Seats[i].Key == modelKey)
                {
                    seat = Seats[i];
                    return Mathf.Abs(scale - seat.Scale) <= SeatScaleTolerance * seat.Scale;
                }
            }

            seat = default(Seat);
            return false;
        }

        /// <summary>The model's local rotation in the mount for a seat (tilt about the mount's x, + nose down).</summary>
        public static Quaternion SeatRotation(Seat seat)
        {
            return Quaternion.Euler(seat.Tilt, 0f, 0f);
        }

        /// <summary>The model's local position in the mount for a seat: its LOD0 centre at (0, lift, slide) after the tilt.</summary>
        public static Vector3 SeatPosition(Bounds bounds, float scale, Seat seat)
        {
            return SeatRotation(seat) * (-bounds.center * scale) + new Vector3(0f, seat.Lift, seat.Slide);
        }

        private static readonly string[] s_keys = { "ar_1", "smg_1", "sg_1", "ar_2", "smg_2", "sg_2", "lmg_1", "lmg_2", "sr_1", "rl_1" };

        /// <summary>The v3 key of a runtime id (WeaponIdMigration's table read back), or null.</summary>
        public static string KeyOf(ClassicWeaponId id)
        {
            int index = (int)id;
            return index >= 0 && index < s_keys.Length ? s_keys[index] : null;
        }

        /// <summary>A classic weapon's runtime id from its v3 key or M3 name; false for a Special, an unknown key or null.</summary>
        public static bool TryResolve(string weaponId, out ClassicWeaponId id)
        {
            string key = WeaponIdMigration.FromLegacyName(weaponId);
            if (key == null)
            {
                id = default(ClassicWeaponId);
                return false;
            }

            return WeaponIdMigration.TryGetRuntimeId(key, out id);
        }

        // ------------------------------------------------------------------ build (once)

        private void Build(RawImage view)
        {
            _view = view;
            _rect = view.rectTransform;
            _yaw = 0f;
            _pitch = InitialPitch;
            _distance = InitialDistance;

            _root = new GameObject(WorldName);
            _root.layer = StageLayer;
            _root.transform.SetPositionAndRotation(StageOrigin, Quaternion.identity);

            BuildSet();
            BuildLights();
            BuildLook();
            BuildCamera();
            EnsureTarget(TargetSize(ViewPixels(), MaxShortSide));
            _dirty = true;
        }

        private void BuildSet()
        {
            Transform socket = null;
            GameObject art = Resources.Load<GameObject>(StageResource);
            if (art != null)
            {
                GameObject set = Instantiate(art, _root.transform, false);
                set.name = "PR_Stage";
                PrepareTree(set);
                PlaceSet(set.transform);
                _turntable = FindDeep(set.transform, TurntableSocket);
                socket = FindDeep(set.transform, WeaponSocket);
                HasSet = _turntable != null && socket != null && socket.IsChildOf(_turntable);
                if (!HasSet)
                {
                    Debug.LogWarning("[UI] Prepare Run stage: " + StageResource + " has no " + WeaponSocket + " under " + TurntableSocket + "; the weapon turns on a bare pivot.");
                    Destroy(set);
                }
            }

            if (!HasSet)
            {
                _turntable = NewChild(TurntableSocket, _root.transform, TurntableLocal);
                socket = NewChild(WeaponSocket, _turntable, WeaponSocketLocal);
            }

            _turntableRest = _turntable.localRotation;
            Vector3 axis = _turntable.parent.InverseTransformDirection(_root.transform.up);
            _spinAxis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;

            // The mount carries the barrel along the posts' line (the rotor's +X at rest) whatever frame the socket was imported with;
            // it turns with the rotor, whose rest turn (TurntableRestYaw) ApplyPose adds.
            _mount = NewChild(MountName, socket, Vector3.zero);
            _mount.rotation = _root.transform.rotation * Quaternion.Euler(0f, MountYaw, 0f);
            float socketScale = socket.lossyScale.x;
            if (socketScale > 1e-4f && Mathf.Abs(socketScale - 1f) > 1e-3f)
            {
                _mount.localScale = Vector3.one * (1f / socketScale);
            }
        }

        /// <summary>S1: each set piece where <see cref="Set"/> puts it (a prefab built before the mirror fix lands right too).</summary>
        private static void PlaceSet(Transform set)
        {
            for (int i = 0; i < Set.Length; i++)
            {
                Transform piece = set.Find(Set[i].Name);
                if (piece != null)
                {
                    piece.localPosition = Set[i].Position;
                }
            }
        }

        private void BuildLights()
        {
            for (int i = 0; i < Lights.Length; i++)
            {
                StageLight spec = Lights[i];
                Vector3 position = spec.Position;
                Vector3 toAim = spec.Aim - position;
                Transform t = NewChild(spec.Name, _root.transform, position);
                t.localRotation = Quaternion.LookRotation(toAim, Vector3.up);
                Light light = t.gameObject.AddComponent<Light>();
                float distance = toAim.magnitude;
                light.type = LightType.Spot;
                light.spotAngle = spec.Angle;
                light.innerSpotAngle = light.spotAngle * SpotInnerShare;
                light.range = distance * SpotRangeFactor;
                light.intensity = spec.Intensity;
                light.color = Color.white;
                light.useColorTemperature = true;   // URP sets GraphicsSettings.lightsUseColorTemperature: Blender's blackbody
                light.colorTemperature = spec.Kelvin;
                light.cullingMask = 1 << StageLayer;
                light.renderMode = LightRenderMode.ForcePixel;
                light.shadows = spec.Shadows ? LightShadows.Soft : LightShadows.None;   // drawn where the tier has additional-light shadows (Cinematic)
            }
        }

        private void BuildLook()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = ProfileName;
            Tonemapping tonemapping = _profile.Add<Tonemapping>(false);
            tonemapping.mode.Override((TonemappingMode)WorldAtmosphere.PostFx.TonemappingMode);

            Transform t = NewChild(ProfileName, _root.transform, Vector3.zero);
            Volume volume = t.gameObject.AddComponent<Volume>();
            volume.isGlobal = true;   // global on the stage layer: only a camera whose volume mask holds layer 5 (the stage's) reads it
            volume.sharedProfile = _profile;
        }

        private void BuildCamera()
        {
            Transform t = NewChild(CameraName, _root.transform, OrbitTarget + OrbitOffset(InitialAzimuth, _pitch, _distance));
            _camera = t.gameObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = UiTokens.Get(UiColor.SurfaceInset);   // AD M2: the ground never lighter than the set
            _camera.cullingMask = 1 << StageLayer;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 30f;
            _camera.depth = -100f;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            _camera.useOcclusionCulling = false;

            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.SetRenderer(FeatureFreeRendererIndex);
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;   // MSAA from the target, as the world camera
            data.volumeLayerMask = 1 << StageLayer;
            data.renderShadows = true;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.requiresColorOption = CameraOverrideOption.Off;
        }

        /// <summary>A weapon model under the mount: WeaponController.EnsureClassicModel's source order, the stage's frame, fit and seat.</summary>
        private GameObject BuildModel(ClassicWeaponId id)
        {
            GameObject model = InstantiateModel(id, out string modelKey);
            if (model == null)
            {
                Debug.LogWarning("[UI] Prepare Run stage: no model for " + KeyOf(id) + ".");
                return null;
            }

            Transform t = model.transform;
            t.SetParent(_mount, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            PrepareTree(model);

            LODGroup[] groups = model.GetComponentsInChildren<LODGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i].ForceLOD(0);   // a close-up: LOD0 whatever the screen height
            }

            Bounds bounds = ModelBounds(t);
            WeaponDefinition definition = Arsenal.Get(id);
            float scale = FitScale(bounds.size, DisplayLength(definition != null ? definition.weaponClass : WeaponClass.AssaultRifle), MaxWeaponHeight);
            t.localScale = Vector3.one * scale;
            if (TrySeat(modelKey, scale, out Seat seat))
            {
                t.localRotation = SeatRotation(seat);
                t.localPosition = SeatPosition(bounds, scale, seat);
            }
            else
            {
                if (seat.Key != null)
                {
                    Debug.LogWarning("[UI] Prepare Run stage: " + modelKey + " fits at " + scale.ToString("F4") + ", its seat was measured at "
                        + seat.Scale.ToString("F4") + " (model re-exported?) - it stands level on the caps' plane until Seats is measured again.");
                }

                t.localPosition = SeatOffset(bounds, scale);
            }

            model.SetActive(false);
            return model;
        }

        /// <summary>The model the run would hold; <paramref name="modelKey"/> is its library entry's key (null for a roster blockout).</summary>
        private static GameObject InstantiateModel(ClassicWeaponId id, out string modelKey)
        {
            modelKey = null;
            ClassicWeaponVisualLibrary library = ClassicWeaponVisualLibrary.Current;
            ClassicWeaponVisualLibrary.Entry entry = library != null ? library.Find(id) : null;
            WeaponDefinition definition = Arsenal.Get(id);
            if (entry == null && library != null && definition != null)
            {
                entry = library.Find(definition.presentation.placeholderVisual);
            }

            if (entry != null && entry.HasModel)
            {
                modelKey = entry.rosterKey;
                return ClassicWeaponVisualLibrary.Instantiate(entry, library.material, AuthoredWeaponModel.DefaultScreenHeights);
            }

            ClassicWeaponDefinition roster = WeaponRoster.Find(id);
            if (roster == null || string.IsNullOrEmpty(roster.ModelResource))
            {
                return null;
            }

            GameObject prefab = Resources.Load<GameObject>(roster.ModelResource);
            if (prefab == null)
            {
                return null;
            }

            GameObject go = Instantiate(prefab);
            go.name = "Model";
            return go;
        }

        /// <summary>The model's LOD0 bounds in its own space (every mesh when no renderer is named _LOD0, e.g. a blockout).</summary>
        private static Bounds ModelBounds(Transform root)
        {
            Bounds lod0 = AuthoredWeaponModel.LocalLod0Bounds(root);
            if (lod0.size.sqrMagnitude > 1e-8f)
            {
                return lod0;
            }

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            bool any = false;
            var bounds = new Bounds();
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Bounds mb = mesh.bounds;
                Matrix4x4 m = toRoot * filters[i].transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }

            return bounds;
        }

        /// <summary>Every object on the stage layer; renderers cast and take shadows and sample no probes (nothing is baked 2 km down).</summary>
        private static void PrepareTree(GameObject root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.layer = StageLayer;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.On;
                renderers[i].receiveShadows = true;
                renderers[i].lightProbeUsage = LightProbeUsage.Off;
                renderers[i].reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        private static Transform NewChild(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.layer = StageLayer;
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ the target

        /// <summary>The view's size in screen pixels (its canvas units times the root canvas' scale).</summary>
        private Vector2 ViewPixels()
        {
            Canvas canvas = _view != null ? _view.canvas : null;
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            return _rect.rect.size * scale;
        }

        /// <summary>(Re)makes the target at <paramref name="size"/>; false when it already is that size. A resize, never a frame.</summary>
        private bool EnsureTarget(Vector2Int size)
        {
            if (_target != null && _target.width == size.x && _target.height == size.y)
            {
                return false;
            }

            ReleaseTarget();
            _target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = TargetName,
                antiAliasing = PipelineMsaa(),
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _target.Create();
            ClearTarget();
            _camera.targetTexture = _target;
            _view.texture = _target;
            _dirty = true;
            return true;
        }

        /// <summary>The pipeline's MSAA (URP takes a target's own sample count; at 1 the weapon's edges stair-step).</summary>
        private static int PipelineMsaa()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return pipeline != null ? Mathf.Max(1, pipeline.msaaSampleCount) : 1;
        }

        /// <summary>A new or lost target shows the stage's ground colour, not old memory, until the camera draws it.</summary>
        private void ClearTarget()
        {
            RenderTexture was = RenderTexture.active;
            RenderTexture.active = _target;
            GL.Clear(true, true, _camera.backgroundColor);
            RenderTexture.active = was;
        }

        private void ReleaseTarget()
        {
            if (_target == null)
            {
                return;
            }

            if (_camera != null)
            {
                _camera.targetTexture = null;
            }

            if (_view != null)
            {
                _view.texture = null;
            }

            _target.Release();
            Discard(_target);
            _target = null;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            Tick(UiMotion.Now(UiClock.Unscaled));
            if (_renderDue)
            {
                _renderDue = false;
                Draw();
            }
        }

        /// <summary>
        /// One frame at <paramref name="now"/> (UiMotion's unscaled clock): the pad's view controls, the auto turn, then the draw
        /// decision (<see cref="Present"/>); the draw itself is LateUpdate's, once a frame however often this runs. Allocation-free
        /// (PrepareRunStagePlayTests measures it). Internal for the tests' explicit clock.
        /// </summary>
        internal void Tick(float now)
        {
            if (_camera == null)
            {
                return;
            }

            float dt = _lastTick < 0f ? 0f : Mathf.Clamp(now - _lastTick, 0f, MaxStepSeconds);
            _lastTick = now;
            bool visible = Visible;

            bool pad = false;
            if (visible && OwnsPad())
            {
                Vector2 stick = UiInput.ViewStick();
                float zoom = UiInput.ViewZoomAxis();
                if (stick.x != 0f || stick.y != 0f)
                {
                    Orbit(new Vector2(-stick.x * StickTurnPerSecond * dt, -stick.y * StickTiltPerSecond * dt));
                    pad = true;
                }

                if (zoom != 0f)
                {
                    Zoom(zoom * TriggerStepsPerSecond * dt);
                    pad = true;
                }
            }

            _padActive = pad;
            if (_dragging || pad)
            {
                _lastInteraction = now;
            }

            if (AutoTurn && visible && !UiMotion.ReducedMotion && !Interacting && now - _lastInteraction >= AutoTurnResumeSeconds && dt > 0f)
            {
                _yaw = WrapYaw(_yaw + AutoTurnDegreesPerSecond * dt);
                _autoDirty = true;
            }

            Present(now, visible);
        }

        /// <summary>The pad's controls belong to the stage unless a modal stands over it.</summary>
        private bool OwnsPad()
        {
            UiModal modal = UiInput.TopModal;
            return modal == null || transform.IsChildOf(modal.transform);
        }

        /// <summary>
        /// The draw decision: due only when the view shows and something changed (the auto turn at most <see cref="AutoTurnFps"/> a
        /// second). Changes made while hidden wait for the view to show.
        /// </summary>
        private void Present(float now, bool visible)
        {
            if (!visible)
            {
                return;
            }

            Vector2 pixels = ViewPixels();
            _viewHeightPixels = Mathf.Max(1f, pixels.y);
            Vector2Int size = TargetSize(pixels, MaxShortSide);
            if (_target == null || Mathf.Abs(_target.width - size.x) > 1 || Mathf.Abs(_target.height - size.y) > 1)
            {
                EnsureTarget(size);
            }
            else if (!_target.IsCreated())
            {
                _target.Create();   // lost with the device: drawn again before it shows
                ClearTarget();
                _dirty = true;
            }

            bool autoDue = _autoDirty && now - _lastRenderAt >= 1f / AutoTurnFps;
            if (_dirty || autoDue)
            {
                _dirty = false;
                _autoDirty = false;
                _lastRenderAt = now;
                _renderDue = true;
            }
        }

        /// <summary>
        /// WP-106 fix (PM run w106-tests, 12.10): the one render of a frame, drawn on demand by the disabled camera - the pose, then
        /// <see cref="Camera.Render"/> into the target. The camera is never enabled: an enabled camera stays enabled until the next
        /// LateUpdate, and the Editor (batch, the shipped 60 FPS cap) draws an enabled camera on every editor tick in between -
        /// one change was ~20 renders (21, 24, 442 for 20) and the idle frames none. On demand, one change is exactly one render
        /// in every runner and the Player, and the texture is current before this frame's canvases draw.
        /// </summary>
        private void Draw()
        {
            if (_camera == null || _target == null)
            {
                return;
            }

            ApplyPose();
            _camera.Render();
        }

        /// <summary>The turntable at <see cref="Yaw"/>, the camera on its orbit looking at the target, the lens for the target's aspect.</summary>
        private void ApplyPose()
        {
            _turntable.localRotation = Quaternion.AngleAxis(TurntableRestYaw + _yaw, _spinAxis) * _turntableRest;
            Transform root = _root.transform;
            Vector3 target = root.TransformPoint(OrbitTarget);
            Vector3 eye = root.TransformPoint(OrbitTarget + OrbitOffset(InitialAzimuth, _pitch, _distance));
            _camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, root.up));
            _camera.fieldOfView = VerticalFov(LensMm, SensorMm, (float)_target.width / _target.height);
        }

        private void OnEnable()
        {
            if (_onEndCamera == null)
            {
                _onEndCamera = OnEndCamera;
            }

            RenderPipelineManager.endCameraRendering += _onEndCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= _onEndCamera;
            _dragging = false;
            _padActive = false;
            if (_camera != null)
            {
                _camera.enabled = false;   // never enabled (Draw renders on demand); kept against an outside switch
            }
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera)
            {
                Renders++;
            }
        }

        private void OnDestroy()
        {
            ReleaseTarget();
            if (_profile != null)
            {
                for (int i = 0; i < _profile.components.Count; i++)
                {
                    Discard(_profile.components[i]);
                }

                Discard(_profile);
                _profile = null;
            }

            if (_root != null)
            {
                Destroy(_root);   // the set, the lights, the volume, the camera and every cached weapon model
                _root = null;
            }
        }

        private static void Discard(Object o)
        {
            if (Application.isPlaying)
            {
                Destroy(o);
            }
            else
            {
                DestroyImmediate(o);
            }
        }

        // ------------------------------------------------------------------ pointer

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            _dragging = true;
            _lastInteraction = UiMotion.Now(UiClock.Unscaled);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            Vector2 delta = eventData.delta;
            Orbit(new Vector2(-delta.x / _viewHeightPixels * DragTurnPerHeight, -delta.y / _viewHeightPixels * DragTiltPerHeight));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
            _lastInteraction = UiMotion.Now(UiClock.Unscaled);
        }

        public void OnScroll(PointerEventData eventData)
        {
            Zoom(ScrollSteps(eventData.scrollDelta.y));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2)
            {
                ResetView();
            }
        }
    }
}
