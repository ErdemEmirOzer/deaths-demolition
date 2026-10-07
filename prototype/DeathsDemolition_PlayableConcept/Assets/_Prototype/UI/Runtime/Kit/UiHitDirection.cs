using DD.Prototype.CameraSystem;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>The four screen boundaries a damage flash can land on.</summary>
    public enum UiScreenEdge
    {
        Left = 0,
        Right = 1,
        Top = 2,
        Bottom = 3
    }

    /// <summary>
    /// World direction -> screen edge. Pure functions, no Unity object state, so the
    /// mapping can be measured on its own rather than inferred from a screenshot.
    ///
    /// WHY THIS IS NOT A ONE-LINER
    /// ---------------------------
    /// The obvious version - "the attacker is at +X in world space, light the right
    /// edge" - is wrong here, and wrong in a way that looks right about a quarter of
    /// the time. The camera is fixed angle (REQ-030..035): it has an authored compass
    /// yaw, and it is elevated and pulled back, so it is also pitched down. World +X is
    /// therefore not screen right, and world +Z is not screen up.
    ///
    /// Two errors have to be removed, not one:
    ///
    ///   YAW.   Handled by expressing the direction in the camera's own basis. This is
    ///          the error people remember; PrototypeCameraRig.PlanarRight / PlanarForward
    ///          already exist for it and WASD is resolved against them.
    ///
    ///   PITCH. Not handled by the planar basis, and this is the one that gets missed.
    ///          A horizontal world direction lying along PlanarForward does NOT project
    ///          to a full unit of screen height: it projects to sin(pitch) of one,
    ///          because the ground plane is seen edge-on-ish. At the shipped framing -
    ///          15 m up, 13 m back, so pitch = atan(15/13) = 49.1 deg - that factor is
    ///          sin(49.1) = 0.756. Feeding (dot(d, PlanarRight), dot(d, PlanarForward))
    ///          straight into an edge test therefore over-reports the vertical axis by
    ///          1/0.756 = 32%, and a hit arriving 40 deg off the screen horizontal reads
    ///          as if it arrived at 50 deg. Diagonals lean the wrong way.
    ///
    /// Both are removed at once by rotating the direction into camera-local space:
    ///
    ///     local  = Quaternion.Inverse(cameraRotation) * worldDirection
    ///     screen = normalize(local.x, local.y)
    ///
    /// local.x and local.y ARE the view-plane components - that is what a camera basis
    /// is - and local.z, the part pointing into the screen, is simply dropped. This is
    /// an orthographic projection of the direction, which is exact at the centre of
    /// frame and very nearly exact everywhere else; the rig keeps the player near the
    /// centre by construction, so the approximation is evaluated where it is best. It
    /// also costs one quaternion multiply, and it keeps working if somebody re-authors
    /// the yaw or the height/back ratio, which a hardcoded 0.756 would not.
    ///
    /// The rotation used is the rig's SHAKE-FREE rotation. Reading the live camera
    /// transform instead would let the damage shake wobble the damage indicator that
    /// caused it, which is a feedback loop and reads as a bug.
    /// </summary>
    public static class UiHitDirection
    {
        /// <summary>
        /// Floating-point noise floor for a squared magnitude, NOT a tunable: below this a
        /// direction has no meaningful view-plane component and the hit counts as coming
        /// from nowhere. Occupies no design decision and carries no attribute for the same
        /// reason a division guard does not.
        /// </summary>
        private const float MinScreenComponent = 1e-4f;

        /// <summary>
        /// Projects a world direction onto the screen plane using the live camera's
        /// orientation. Returns <see cref="Vector2.zero"/> when there is no usable
        /// direction, which the caller reads as "flash every edge".
        /// </summary>
        public static Vector2 ToScreenDirection(Vector3 worldDirection)
        {
            return ToScreenDirection(worldDirection, CurrentCameraRotation());
        }

        /// <summary>Same projection against an explicit camera orientation. Deterministic; no scene lookup.</summary>
        public static Vector2 ToScreenDirection(Vector3 worldDirection, Quaternion cameraRotation)
        {
            if (worldDirection.sqrMagnitude <= MinScreenComponent)
            {
                return Vector2.zero;
            }

            Vector3 local = Quaternion.Inverse(cameraRotation) * worldDirection;
            var screen = new Vector2(local.x, local.y);

            return screen.sqrMagnitude <= MinScreenComponent ? Vector2.zero : screen.normalized;
        }

        /// <summary>
        /// Splits one screen-space direction across the four edges.
        ///
        /// Weight per edge is the clamped cosine against that edge's outward normal, so
        /// a hit square from the right lights only the right edge at 1, and a hit from
        /// the upper right lights right and top at 0.707 each. That is what makes a
        /// diagonal legible without an eight-way sprite set or an arc mesh.
        ///
        /// A zero direction means the attacker gave no position at all. Every edge then
        /// gets <see cref="UiTokens.DamageEdgeOmniWeight"/> - the honest answer to "where
        /// from?" in that case is "unknown", and a full ring says that.
        /// </summary>
        public static void EdgeWeights(Vector2 screenDirection, float[] weights)
        {
            if (weights == null || weights.Length < 4)
            {
                return;
            }

            if (screenDirection.sqrMagnitude <= MinScreenComponent)
            {
                for (int i = 0; i < 4; i++)
                {
                    weights[i] = UiTokens.DamageEdgeOmniWeight;
                }

                return;
            }

            weights[(int)UiScreenEdge.Left] = Mathf.Max(0f, -screenDirection.x);
            weights[(int)UiScreenEdge.Right] = Mathf.Max(0f, screenDirection.x);
            weights[(int)UiScreenEdge.Top] = Mathf.Max(0f, screenDirection.y);
            weights[(int)UiScreenEdge.Bottom] = Mathf.Max(0f, -screenDirection.y);
        }

        /// <summary>
        /// Shake-free camera orientation, preferring the rig. Falls back to the main
        /// camera, then to identity, so a bare test scene still produces a defined answer
        /// instead of a null reference.
        /// </summary>
        public static Quaternion CurrentCameraRotation()
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            if (rig != null)
            {
                return rig.StableRotation;
            }

            Camera main = Camera.main;
            return main != null ? main.transform.rotation : Quaternion.identity;
        }
    }

    /// <summary>
    /// REQ-045 · the screen half of "screen edge, a short sound and a small camera
    /// response - do not interrupt control".
    ///
    /// WHAT WAS ACTUALLY BROKEN
    /// ------------------------
    /// Nothing about this was missing from the design. PlayerDamageFeedback's own header
    /// listed "the screen-edge vignette -> HUD task, PlayerHealth.AnyDamaged" as a job
    /// belonging to somebody else, and then nobody took it. The camera bump shipped, the
    /// sound shipped, and the player was left with a picture that shook but never said
    /// which way to turn. This class is that missing half.
    ///
    /// WHICH EVENT, AND WHY NOT THE OTHER ONE
    /// --------------------------------------
    /// This binds to PlayerHealth.AnyDamaged, not to DamageEvents.PlayerAttacked, and
    /// the difference is not cosmetic:
    ///   * PlayerAttacked fires when an attack's active window OVERLAPPED the player.
    ///     Its DamageApplied flag can be false, i.e. it fires for hits that took nothing.
    ///     An indicator on that event would sometimes announce damage that did not
    ///     happen, which is worse than announcing nothing.
    ///   * PlayerAttacked is raised only by enemy melee. AnyDamaged is raised by every
    ///     source there will ever be, so an explosion or a hazard needs no new wiring.
    ///   * AnyDamaged is raised AFTER the health value is written, so the edge can never
    ///     disagree with the health bar in the same frame.
    ///   * AppliedAmount, MaxHealth and NormalizedAfter only exist on PlayerDamageInfo,
    ///     and all three are needed to size the flash.
    /// It is also the event the camera bump and the damage sound already use, so all
    /// three channels answer one raise, on one frame, which is the only reason a hit
    /// reads as a single event rather than three near-misses.
    ///
    /// SIMULTANEOUS HITS - REQ-044
    /// ---------------------------
    /// There is no player invulnerability window, so two enemies can land on the same
    /// frame from opposite sides. Each edge carries its OWN amplitude and its OWN
    /// countdown; a hit only ever raises the edges it points at and never clears the
    /// others. Left and right therefore light together and fade independently, which is
    /// the readable answer. A second hit on an edge that is still lit takes the louder of
    /// what is currently visible and what the new hit is worth, and restarts that edge's
    /// clock - so being surrounded holds the ring up instead of strobing it.
    ///
    /// WP-094 HUD v4 (R3, C-00 07.10; HAREKET-HUD D1 MUST, the motion lead's main-branch findings)
    /// -------------------------------------------------------------------------------------------
    ///   * THE FLASH GATE. A lit edge is 1920 × 150 px - a large flash (≥ 43 000 px²) - so every onset now passes
    ///     <see cref="UiMotion.FlashOnset"/> (≤ 3 a second across the whole UI) and a per-edge limit (≤ 2 a second). A
    ///     refused onset MERGES: an edge already lit takes the louder peak on its running clock; a dark edge stays dark (the
    ///     top-down wedge round the player still says where - it is small, outside the gate). Before, it bypassed the gate.
    ///   * NO MESH REBUILD. The edges' colour is set once; the envelope writes CanvasRenderer alpha (no vertex rebuild),
    ///     and only when the 8-bit value moves. Before, it wrote Image.color - a mesh rebuild - every frame of every live
    ///     edge. The layer is its own nested canvas (RunHudScreen), so the decay re-batches only it.
    ///   * THE SOURCE. It listens to <see cref="HudFeelSource.PlayerDamaged"/> - PlayerHealth.AnyDamaged today, behind the
    ///     one seam S-02's FeelEvents will replace.
    ///
    /// WHAT THIS CLASS CANNOT DO
    /// -------------------------
    /// It has no reference to input, to the motor, to the aim or to Time.timeScale, and
    /// every Image it owns is raycastTarget = false. There is no code path from taking
    /// damage to losing control, and there is nothing here to remove to restore one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiHitDirectionLayer : MonoBehaviour
    {
        private const int EdgeCount = 4;

        private static readonly float[] s_weights = new float[EdgeCount];

        private readonly Image[] _edges = new Image[EdgeCount];

        /// <summary>Peak this edge was raised to, 0..1 before the peak-alpha scale.</summary>
        private readonly float[] _amplitude = new float[EdgeCount];

        /// <summary>Seconds of life left on this edge: hold first, then decay.</summary>
        private readonly float[] _remaining = new float[EdgeCount];

        private int _liveEdges;

        /// <summary>The alpha each edge's CanvasRenderer was last given (−1: not yet).</summary>
        private readonly float[] _shown = { -1f, -1f, -1f, -1f };

        /// <summary>WP-094 (D1): ≤ 2 onsets a second per edge, next to the global FlashGate.</summary>
        private readonly HudOnsetLimiter _perEdge = new HudOnsetLimiter(EdgeCount, UiMotionTokens.FlashGateOnsetsPerElement);

        /// <summary>Hits whose onset the flash gate refused (merged into what was lit). Test / diagnosis readout.</summary>
        public int MergedOnsets { get; private set; }

        /// <summary>Hits this layer has drawn since it was created. Diagnosis readout.</summary>
        public int HitCount { get; private set; }

        /// <summary>True while any edge is drawing something.</summary>
        public bool IsShowing
        {
            get { return _liveEdges > 0; }
        }

        /// <summary>
        /// Alpha the given edge is CURRENTLY drawing, 0 when it is off. Reads the graphic
        /// rather than the model, so a test measures what a player would see.
        /// </summary>
        public float EdgeAlpha(UiScreenEdge edge)
        {
            Image image = _edges[(int)edge];
            if (image == null || !image.enabled)
            {
                return 0f;
            }

            return image.canvasRenderer.GetAlpha();   // WP-094: the envelope lives on the renderer; the colour is fixed
        }

        /// <summary>The graphic for one edge. Exposed so a test can check it takes no input.</summary>
        public Graphic EdgeGraphic(UiScreenEdge edge)
        {
            return _edges[(int)edge];
        }

        private void Awake()
        {
            var self = (RectTransform)transform;
            UiFactory.Fill(self);

            // Anchored to the boundary and stretched along it, so the band is a fixed
            // depth at every aspect ratio while the canvas scaler handles the rest.
            _edges[(int)UiScreenEdge.Left] = BuildEdge(
                "Left", UiSprites.FadeLeft,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(UiTokens.DamageEdgeDepth, 0f));

            _edges[(int)UiScreenEdge.Right] = BuildEdge(
                "Right", UiSprites.FadeRight,
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(UiTokens.DamageEdgeDepth, 0f));

            _edges[(int)UiScreenEdge.Top] = BuildEdge(
                "Top", UiSprites.FadeDown,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, UiTokens.DamageEdgeDepth));

            _edges[(int)UiScreenEdge.Bottom] = BuildEdge(
                "Bottom", UiSprites.FadeUp,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiTokens.DamageEdgeDepth));
        }

        private Image BuildEdge(string name, Sprite ramp, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 size)
        {
            Image image = UiFactory.NewImage(name, transform, UiColor.DamageEdge, ramp);
            var rect = (RectTransform)image.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;

            // Belt and braces on top of UiFactory's default: this element sits over the
            // whole play area and must never be able to swallow a click. REQ-045.
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        private void OnEnable()
        {
            HudFeelSource.PlayerDamaged += OnDamaged;   // WP-094: the one seam (PlayerHealth.AnyDamaged today)
        }

        private void OnDisable()
        {
            HudFeelSource.PlayerDamaged -= OnDamaged;
        }

        private void OnDamaged(HudDamage info)
        {
            // HitDirection travels FROM the attacker TOWARDS the player, so the attacker
            // is in the opposite direction, and that is the edge to light.
            Vector2 screen = UiHitDirection.ToScreenDirection(-info.HitDirection);
            UiHitDirection.EdgeWeights(screen, s_weights);

            float strength = StrengthOf(info);
            bool any = false;
            for (int i = 0; i < EdgeCount; i++)
            {
                any |= s_weights[i] * strength > 0f;
            }

            if (!any)
            {
                return;
            }

            // WP-094 (D1): one onset per hit through the UI's FlashGate; each edge also through its own ≤ 2 / s.
            bool onset = UiMotion.FlashOnset(true);
            float now = UiFxGlobals.Now;
            bool raised = false;

            for (int i = 0; i < EdgeCount; i++)
            {
                float level = s_weights[i] * strength;
                if (level > 0f && Raise(i, level, onset && _perEdge.Allow(i, now)))
                {
                    raised = true;
                }
            }

            if (!onset)
            {
                MergedOnsets++;
            }

            if (raised)
            {
                HitCount++;
                UiEvents.Raise(UiEvent.HudDamageEdge);
            }
        }

        /// <summary>
        /// How loud this hit is, 0..1.
        ///
        /// Scaled by damage as a fraction of MAXIMUM health rather than of remaining
        /// health, which is the same scale the camera bump uses, floored so a graze still
        /// answers the question the element exists to answer.
        ///
        /// LOW HEALTH. A hit that leaves the player at or below UiTokens.LowHealthFraction
        /// is promoted to UiTokens.LowHealthEdgeStrength. This is the only low-health treatment on the
        /// screen edge, and that is deliberate: the HUD already signals low health
        /// continuously - the health bar and its readout switch to their low tokens at the
        /// same threshold, read from the same constant - and adding a second, persistent
        /// red edge would compete with this one for the same pixels in the same colour
        /// family. Modulating the existing transient instead of adding a new permanent one
        /// is what keeps the two from colliding.
        /// </summary>
        private static float StrengthOf(in HudDamage info)
        {
            float fraction = info.MaxHealth > 0f ? info.AppliedAmount / info.MaxHealth : 0f;
            // Mathf.Max here is a division guard against a zeroed token, nothing more.
            float strength = Mathf.Clamp01(
                fraction / Mathf.Max(0.0001f, UiTokens.DamageEdgeFullDamageFraction));

            strength = Mathf.Max(strength, UiTokens.DamageEdgeMinStrength);

            if (info.NormalizedAfter <= UiTokens.LowHealthFraction)
            {
                // AD v005 MUST-2: promoted, but capped - 0.45 α at the edge, not 0.92. No
                // persistent pulse is added on purpose (see the summary above): the bar
                // and readout carry the continuous low-health signal.
                strength = Mathf.Max(strength, UiTokens.LowHealthEdgeStrength);
            }

            return strength;
        }

        /// <summary>
        /// Raises one edge. Returns false for an edge this hit does not point at, or (WP-094) one the flash gate keeps dark.
        /// <paramref name="onset"/>: the gates allowed a new onset - the louder of what shows and the hit, on a fresh clock.
        /// Refused: an edge already lit takes the louder peak on its running clock (merged, not queued); a dark edge stays dark.
        /// </summary>
        private bool Raise(int index, float level, bool onset)
        {
            if (level <= 0f)
            {
                return false;
            }

            float visible = _amplitude[index] * Envelope(_remaining[index]);
            if (!onset)
            {
                if (visible <= 0f)
                {
                    return false;
                }

                _amplitude[index] = Mathf.Clamp01(Mathf.Max(_amplitude[index], level));
                Draw(index, _amplitude[index] * Envelope(_remaining[index]));
                return true;
            }

            _amplitude[index] = Mathf.Clamp01(Mathf.Max(visible, level));
            _remaining[index] = UiTokens.DamageEdgeHoldSeconds + UiTokens.DamageEdgeDecaySeconds;

            Draw(index, _amplitude[index]);
            return true;
        }

        private void Update()
        {
            if (_liveEdges <= 0)
            {
                return;
            }

            // Unscaled on purpose. A hit stop drops the time scale to 0.06 for 45 ms, and
            // an indicator that stretched with it would still be on screen long after the
            // hit it belongs to, mistimed against the sound, which does not stretch either.
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                return;
            }

            for (int i = 0; i < EdgeCount; i++)
            {
                if (_remaining[i] <= 0f)
                {
                    continue;
                }

                _remaining[i] -= dt;
                Draw(i, _amplitude[i] * Envelope(_remaining[i]));
            }
        }

        /// <summary>
        /// Flat while the hold lasts, then a straight fall to nothing. Linear rather than
        /// eased: the fall is what carries "this is over", and an ease-out spends its last
        /// frames near zero where they say nothing and cost overdraw.
        /// </summary>
        private static float Envelope(float remaining)
        {
            if (remaining <= 0f)
            {
                return 0f;
            }

            return remaining >= UiTokens.DamageEdgeDecaySeconds
                ? 1f
                : remaining / UiTokens.DamageEdgeDecaySeconds;
        }

        private void Draw(int index, float level)
        {
            Image image = _edges[index];
            if (image == null)
            {
                return;
            }

            // 0.001 is the point below which an 8-bit alpha channel rounds to zero anyway,
            // i.e. the threshold at which the graphic stops existing on screen. Not a taste
            // value and not a fade floor.
            float alpha = Mathf.Clamp01(level) * UiTokens.DamageEdgePeakAlpha * UiTokens.FlashScale; // WP-022G reduce flashes
            bool on = alpha > 0.001f;

            if (image.enabled != on)
            {
                image.enabled = on;
                _liveEdges += on ? 1 : -1;
                _shown[index] = -1f;   // a re-enabled graphic takes its alpha afresh
            }

            if (!on)
            {
                _amplitude[index] = 0f;
                _remaining[index] = 0f;
                return;
            }

            // WP-094 (D1): the renderer's alpha, never Image.color - no vertex rebuild; and only when the 8-bit value moves.
            if (_shown[index] < 0f || Mathf.Abs(alpha - _shown[index]) >= 1f / 255f)
            {
                _shown[index] = alpha;
                image.canvasRenderer.SetAlpha(alpha);
            }
        }
    }
}
