using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using Unity.Profiling;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-104 B4-2 (S-07) · AD-B4 §6 / M9 post: the add-on glow's bloom spot as a card. b4-r4 measured M9's raw step (the glow slot
    /// lights: ≥ +0.15 mean luminance) but the post frame's glowSpotPx stayed far under the add-on's own screen area (E4 28 / ≈ 126,
    /// E5 3 / ≈ 71 px): the slot is a side window a few pixels wide from the TD rig, so its bloom is a few pixels too. AD-B4 §6: the
    /// bloom spread moves to a socket glint card from an existing AP-041 glint sheet, no new asset — this class is its pure half
    /// (<see cref="TreeAttachGlow"/> draws it):
    /// <list type="bullet">
    /// <item>Sheet: <see cref="Row"/> (AP-041 <c>G_B7_heat_glint</c>, the only glint page), <see cref="Frame"/> 0 held — the four-point
    /// star with its hot round core; drawn ADDED (its premultiplied rgb, Blend One One): a glint never darkens what it lies on.</item>
    /// <item>Where: the glow slot's own world position (E4 the side window, E5 the ring band: submesh 1's centre), every frame, lifted
    /// <see cref="Lift"/> towards the TD rig so the add-on's own surface never hides it, facing the rig (<see cref="Facing"/>).</item>
    /// <item>When: the rungs whose emission passes the bloom threshold (<see cref="Shows"/>: E4 III–V, E5 IV–V), the add-on shown
    /// (TopDown); never while it is hidden.</item>
    /// <item>Value (AD-B4 M9, "değer × FlashScale"): the add-on's own ladder value (E4 III 1.4 · IV 1.6 · V 2.0, E5 IV 1.4 · V 2.0)
    /// × FlashScale as the card's linear HDR peak; its hue (<see cref="Hue"/>) never above 1 in any channel, so Reduce Flashes' 0.4
    /// takes every glint under the bloom threshold (RF: no bloom, accepted) while the glint itself stays.</item>
    /// <item>Size: <see cref="SizeFactor"/> × the add-on mesh's diagonal (its lossy scale in) — the sheet's bright core (L ≥ 0.3,
    /// r ≈ 0.11 of the cell) then covers about the add-on's own screen area at any weapon scale (E4 V01 0.20 m → a 0.59 m card,
    /// ≈ 13 px bright core at 100 px/m; ar_1's E5 0.16 m → 0.48 m).</item>
    /// </list>
    /// </summary>
    public static class TreeSocketCard
    {
        /// <summary>The socket cards' own marker (TreeAttachGlow: event-time paint + the per-frame track).</summary>
        public static readonly ProfilerMarker Marker = new ProfilerMarker("WP104.Vfx.SocketCard");

        /// <summary>AP-041 <c>G_B7_heat_glint_srgb_pm</c> (catalog <c>fb_ap041t_b7_heat_glint</c>): the glint sheet.</summary>
        public const TreeVfxRow Row = TreeVfxRow.B7HeatGlint;
        /// <summary>The sheet's first cell: the four-point star at full strength (later cells fade into a ring); held, no twinkle.</summary>
        public const int Frame = 0;
        /// <summary>The card's edge over the add-on mesh's diagonal (see the class note), clamped to <see cref="SizeMinMetres"/> –
        /// <see cref="SizeMaxMetres"/>.</summary>
        public const float SizeFactor = 3f, SizeMinMetres = .2f, SizeMaxMetres = 1.2f;
        /// <summary>The card sits this share of the add-on's diagonal (at least <see cref="LiftMinMetres"/>) towards the TD rig: in
        /// front of the add-on's own surface, on the same screen point.</summary>
        public const float LiftShare = .5f, LiftMinMetres = .02f;

        /// <summary>A card only where the AD set a bloom-level value (≥ the bloom threshold before FlashScale), on a shown add-on with a
        /// glow slot (<paramref name="slot"/>: V03 electric has none).</summary>
        public static bool Shows(TreeVfxLadder.AddOnGlow kind, int tier, bool shown, bool slot) =>
            shown && slot && TreeVfxLadder.AddOnEmissive(kind, tier) >= TreeVfxLadder.BloomThreshold;

        /// <summary>
        /// The card's linear colour before its value, the brightest channel 1:
        /// <list type="bullet">
        /// <item>E4 (Element owns the capsule light, AD §1.1 (c)): the element's glow and core (AD §1.2, linear) averaged — fire a hot
        /// orange (h ≈ 23°, under the 35–65° gold reserve), ice a pale cyan, venom a pale yellow-green; none / an unknown element:
        /// white;</item>
        /// <item>E5 (Speed's white-hot): white — the sheet is already #FFF4E0-warm (s ≈ 0.12–0.16); the chaos ring
        /// (<paramref name="ring"/> 2, AD §3 "E5'e mor kakma") the violet edge #9B5CFF averaged with it.</item>
        /// </list>
        /// </summary>
        public static Color Hue(TreeVfxLadder.AddOnGlow kind, TreeElement element, int ring)
        {
            Color c;
            if (kind == TreeVfxLadder.AddOnGlow.Side)
            {
                TreeElement e = TreeVfxLadder.Single(element);
                if (e == TreeElement.None) return Color.white;
                c = (TreeVfxLadder.Linear(TreeVfxLadder.ElementGlow(e)) + TreeVfxLadder.Linear(TreeVfxLadder.ElementCore(e))) * .5f;
            }
            else
            {
                if (ring != 2) return Color.white;
                c = (TreeVfxLadder.Linear(TreeVfxLadder.ChaosEdge) + Color.white) * .5f;
            }

            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return max > 0f ? new Color(c.r / max, c.g / max, c.b / max, 1f) : Color.white;
        }

        /// <summary>The card's <c>_Tint</c> (linear, set with SetVector — no gamma step): <see cref="Hue"/> × the add-on's ladder value
        /// × FlashScale; w 1.</summary>
        public static Vector4 Tint(TreeVfxLadder.AddOnGlow kind, int tier, TreeElement element, int ring, float flashScale)
        {
            Color h = Hue(kind, element, ring);
            float v = TreeVfxLadder.AddOnEmissive(kind, tier) * Mathf.Clamp01(flashScale);
            return new Vector4(h.r * v, h.g * v, h.b * v, 1f);
        }

        /// <summary>The card's edge (m) for an add-on mesh of this world diagonal.</summary>
        public static float Size(float meshDiagonal) => Mathf.Clamp(SizeFactor * Mathf.Max(0f, meshDiagonal), SizeMinMetres, SizeMaxMetres);

        /// <summary>How far (m) the card sits towards the TD rig from the glow slot.</summary>
        public static float Lift(float meshDiagonal) => Mathf.Max(LiftMinMetres, LiftShare * Mathf.Max(0f, meshDiagonal));

        /// <summary>The direction from the weapon towards the TD camera — the rig's own (PrototypeCameraRig.ApplyFeel: yaw × (0, height,
        /// −back)); straight up when the feel has no offset.</summary>
        public static Vector3 Facing(in CameraFeel cam)
        {
            Vector3 v = Quaternion.Euler(0f, cam.yawDegrees, 0f) * new Vector3(0f, cam.heightMeters, -cam.backDistanceMeters);
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.up;
        }

        /// <summary>The card's rotation for <paramref name="facing"/>: its quad (VfxLibrary.Quad, XY) square to the rig, its +Y the
        /// screen's up, so the star's arms lie along the screen axes.</summary>
        public static Quaternion Rotation(Vector3 facing) =>
            Quaternion.LookRotation(-facing, Mathf.Abs(facing.y) > .999f ? Vector3.forward : Vector3.up);

        /// <summary>
        /// The glow slot of an add-on renderer: its centre in the renderer's own space (submesh <see cref="TreeAttachGlow.GlowSlot"/>'s
        /// bounds, the whole mesh's when it has none) and the add-on mesh's world diagonal (its bounds × the lossy scale). A renderer
        /// without a mesh filter falls back to its world bounds. Event time only.
        /// </summary>
        public static bool TryAnchor(Renderer r, out Vector3 local, out float diagonal)
        {
            local = Vector3.zero;
            diagonal = 0f;
            if (r == null) return false;
            Transform t = r.transform;
            MeshFilter f = r.GetComponent<MeshFilter>();
            Mesh mesh = f != null ? f.sharedMesh : null;
            if (mesh != null)
            {
                Bounds all = mesh.bounds;
                Bounds slot = mesh.subMeshCount > TreeAttachGlow.GlowSlot ? mesh.GetSubMesh(TreeAttachGlow.GlowSlot).bounds : all;
                local = slot.size.sqrMagnitude > 0f ? slot.center : all.center;
                diagonal = Vector3.Scale(all.size, t.lossyScale).magnitude;
                return diagonal > 0f;
            }

            Bounds b = r.bounds;
            local = t.InverseTransformPoint(b.center);
            diagonal = b.size.magnitude;
            return diagonal > 0f;
        }
    }
}
