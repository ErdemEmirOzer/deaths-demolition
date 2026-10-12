using DD.Prototype.Core.Weapons;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-104 B4 (S-07) · the run tree's half of <see cref="TracerRibbonRenderer"/>'s top-down block (WP-101): one call per seam in
    /// the WP-101 file (TdLook → <see cref="StyleTd"/>, TickTd → <see cref="TdMarked"/> / <see cref="TdAgeDelta"/>, RecordTdProjectile →
    /// <see cref="AdmitTdVolley"/> / <see cref="TdTracerOf"/> / <see cref="TdMarkedAtBirth"/> / <see cref="TdRefused"/>, TdTrail →
    /// <see cref="TracerStyle.ScaledTrail"/>, DrawTd → <see cref="TracerStyle"/> colours + <see cref="TdStep"/> / <see cref="TdCap"/> /
    /// <see cref="TdBase"/> / <see cref="SyncTdEdgeTint"/>, LateUpdate → <see cref="DrawTdInk"/>, ClearTd → <see cref="ClearTdTree"/>),
    /// everything else here:
    /// <list type="bullet">
    /// <item>the round's look at birth (AD-B4 §1): the core at its WP-101 width × S-02's per-round SizeScale (capped, A5), the halo +
    /// the max(Shot, Element) tier's px step and V's HDR, the trail × the Shot tier, S-04's dominant element (K1);</item>
    /// <item>S-02's extra volleys (VolleyIndex &gt; 0): a body streak each, the tracer rhythm counts the main rounds only (the rhythm id
    /// skips every extra), and at most <see cref="TracerStyle.VolleyRecordsMax"/> of the 48 records — past it an extra merges into its
    /// pull's main record (× √(1 + n) ≤ 1.25, S-02's merge rule);</item>
    /// <item>K3: the marked chaos round (RunTreeHooks.TreeRoundMarkedChaos) — a violet tracer with the V halo whatever its tier, whose
    /// sumi core (0.85 × the core, AD-B4 M4) is drawn premultiplied in one more instanced draw one queue step after the streaks and
    /// halos (Transparent+11: on top whatever order those two take).</item>
    /// <item>B4-2 B9: a seeking round (S-02 sh_cap: <c>ProjectileState.SeekTrail</c>) keeps its path (TickTd → <see cref="TdSeekPath"/>,
    /// <c>ProjectileSystem.TryGetPath</c> into the record's own buffer, the muzzle catch-up added) and every layer of its streak —
    /// the same look — is laid along it (DrawTd / DrawTdInk → <see cref="BendTd"/>, <see cref="TreeSeekTrail"/>), as long as the kept
    /// path up to 10 m (S-07 12.10 decision (a), <see cref="TreeSeekTrail.SeekLength"/>): consecutive instances, ≤ PathPoints a
    /// record, t read along the whole bent streak (DD/VFX Ribbon <c>_Span.w</c> = −length). On impact the kept path stays and the
    /// tail runs in along it. Every straight round is untouched.</item>
    /// </list>
    /// A Special's legacy hitscan shot (a synthetic record, Id &lt; 0) stays WP-101's unless the S-09 tree preview births it.
    /// Neutral tree + size 1 = WP-101 byte-identical (look 0, no ceiling, no halo step, the WP-101 trail, no ink). 0 B per frame.
    /// </summary>
    public sealed partial class TracerRibbonRenderer
    {
        /// <summary>The marked round's ink draws one queue step after the ribbons (DD/VFX Ribbon is Transparent+10).</summary>
        public const int TdInkQueueOffset = 11;

        private static readonly int EdgeTintId = Shader.PropertyToID("_EdgeTint");

        private int _tdVolleyExtras;
        private int _tdLastMain = -1;
        private Material _tdInkMaterial;
        private float _tdEdgeHdr = 1f;   // the halo material's rim factor (Awake: WP-101's, 1)

        // WP-104 B4-2 (B9): each record's kept path (TryGetPath's oldest-first points + now, ≤ PathPoints + 1) and the lay-out scratch
        private readonly Vector3[][] _tdPaths = NewTdPaths();
        private readonly TreeSeekTrail.Segment[] _tdSegs = new TreeSeekTrail.Segment[ProjectileSystem.PathPoints];

        private static Vector3[][] NewTdPaths()
        {
            var paths = new Vector3[TdCapacity][];
            for (int i = 0; i < paths.Length; i++) paths[i] = new Vector3[ProjectileSystem.PathPoints + 1];
            return paths;
        }

        /// <summary>Live records whose streak bends along a kept path (B9; tests / F1).</summary>
        public int LiveTdSeekPaths
        {
            get
            {
                int n = 0;
                for (int i = 0; i < TdCapacity; i++) if (_tdRounds[i].Alive && _tdRounds[i].SeekPoints > 0) n++;
                return n;
            }
        }

        /// <summary>Instances laid along kept paths beyond one per record, since start-up (B9; tests diff it, F1).</summary>
        public int TdBentInstances { get; private set; }

        /// <summary>Extra volley rounds that found no record of their own and widened their pull's main record (tests / F1).</summary>
        public int TdVolleyMerged { get; private set; }
        /// <summary>Extra volley rounds with neither a record nor a main record of their pull to merge into.</summary>
        public int TdVolleyDropped { get; private set; }
        /// <summary>Live records of extra volley rounds (recounted on each admission; tests / F1).</summary>
        public int LiveTdVolleyRecords { get; private set; }
        /// <summary>Sumi cores drawn last frame (K3 marked rounds).</summary>
        public int DrawnInk { get; private set; }

        // ------------------------------------------------------------------ the look

        /// <summary>TdLook's last step: the WP-101 width becomes the floor of the round's ceiling, then the run's look applies.</summary>
        private static void StyleTd(ref TdRound r, bool tracer)
        {
            r.Base = r.Core;
            r.HaloPx = 0f;   // WP-101's halo, trail and HDR until the run's look says otherwise
            r.TrailScale = 1f;
            r.HaloHdr = 1f;
            if (r.Id < 0 && !TracerStyle.PreviewActive)
            {
                // a Special's legacy hitscan shot (Emit) is not the run tree's: WP-101 as it is
                r.Look = TracerStyle.NeutralLook;
                r.CapPx = 0f;
                return;
            }

            float size = (r.Size > 0f ? r.Size : 1f) * TracerStyle.MergeFactor(r.Merged);
            TracerStyle.ApplyTd(ref r.Core, ref r.Halo, ref r.Look, ref r.CapPx, ref r.HaloPx, ref r.TrailScale, ref r.HaloHdr, size, tracer);
        }

        /// <summary>K3 (AD): the round that carries the chaos core's next hit becomes a full violet tracer the frame its tag is seen
        /// (a shotgun's marked centre pellet too: it is the pull's main projectile), with the V halo whatever its tier (AD-B4 M4).</summary>
        private static void TdMarked(ref TdRound r, in ProjectileState p)
        {
            if (r.Look == TracerStyle.MarkedLook || !TracerStyle.IsMarkedRound(p.TreeFlags)) return;
            TdLook(ref r, VfxWeaponProfiles.For(p.WeaponId), true);
            TracerStyle.MarkTd(ref r.Look, ref r.CapPx, ref r.HaloPx, ref r.HaloHdr);
        }

        /// <summary>DD/VFX Ribbon <c>_Extra.y</c> of this record's instance: −(a tracer halo's tier step, px at 1080p; AD-B4 §1), 0 =
        /// WP-101.</summary>
        private static float TdStep(int layer, in TdRound r) => TracerStyle.ShaderStep(layer, r.Tracer, r.HaloPx);

        /// <summary>DD/VFX Ribbon <c>_Extra.z</c> of this record's instance on <paramref name="layer"/> (0 = WP-101: no ceiling).</summary>
        private static float TdCap(int layer, in TdRound r) => TracerStyle.ShaderCap(layer, r.Tracer, r.Pellet && !r.Tracer, r.CapPx);

        /// <summary>DD/VFX Ribbon <c>_Extra.w</c>: −(the instance's WP-101 width, m), the ceiling's floor (0 = WP-101: none).</summary>
        private static float TdBase(int layer, in TdRound r) => TracerStyle.ShaderBase(layer, r.Tracer, r.Pellet && !r.Tracer, r.CapPx, r.Base);

        /// <summary>
        /// AD-B4 M3: the neutral halo's amber rim (DD/VFX Ribbon <c>_EdgeTint</c>, a colour of the shared halo material, not of an
        /// instance) follows the halo's HDR under Reduce Flashes. Awake set it with SetColor (gamma → linear in a linear project); a dimmed
        /// rim is that linear value × the factor (SetVector: no conversion) and FlashScale 1 restores WP-101's call. Written on a change
        /// only: 0 B, no per-frame material write. V's S1 boost stays per instance (the rim keeps ×1).
        /// </summary>
        private void SyncTdEdgeTint(float flash)
        {
            float hdr = TracerStyle.HaloHdr(1f, flash);
            if (_tdGlowMaterial == null || hdr == _tdEdgeHdr) return;
            _tdEdgeHdr = hdr;
            Color rim = TdHaloEdge * TdHaloHdr;
            if (hdr >= 1f) _tdGlowMaterial.SetColor(EdgeTintId, rim);
            else _tdGlowMaterial.SetVector(EdgeTintId, (QualitySettings.activeColorSpace == ColorSpace.Linear ? rim.linear : rim) * hdr);
        }

        /// <summary>
        /// K3 at birth: ProjectileSystem raises Spawned after the new projectile joined its order, so the last live one is this round
        /// (its TreeFlags already carry the tag). A round that hits inside the driver's first steps (before any TickTd) is still drawn
        /// marked. A synthetic record (Emit) never matches.
        /// </summary>
        private void TdMarkedAtBirth(int slot, in ProjectileSpawnInfo info)
        {
            int last = ProjectileSystem.LiveCount - 1;
            if (last < 0 || info.ProjectileId < 0) return;
            ref readonly ProjectileState p = ref ProjectileSystem.Get(last);
            if (p.Generation == info.ProjectileId) TdMarked(ref _tdRounds[slot], in p);
        }

        /// <summary>
        /// The record's age step: the presentation clock (unscaled while the S-09 tree preview runs) for a synthetic record (the
        /// diorama's Emit shots), the game clock for a real round — its projectile stands still at timeScale 0, so it must not age
        /// past <see cref="TdMaxFlightSeconds"/> behind the tree screen. Outside the preview both are <paramref name="delta"/>.
        /// </summary>
        private static float TdAgeDelta(in TdRound r, float delta) => r.Id < 0 || !VfxClock.PreviewActive ? delta : Mathf.Max(0f, Time.deltaTime);

        // ------------------------------------------------------------------ S-02 extra volleys

        /// <summary>ClearTd: the main-round rhythm and the merge target go with the records (a cleared renderer is WP-101's again).</summary>
        private void ClearTdTree()
        {
            _tdVolleyExtras = 0;
            _tdLastMain = -1;
            LiveTdVolleyRecords = 0;
        }

        /// <summary>
        /// An extra volley round (VolleyIndex &gt; 0) asks for a record: yes while the extras hold fewer than
        /// <see cref="TracerStyle.VolleyRecordsMax"/> and a record is free; else (the 48 ceiling) it merges into its pull's main record
        /// (spawned just before it, within one pull's <see cref="TreeGuardrails.ShotsPerPull"/> ids) and gets none.
        /// </summary>
        private bool AdmitTdVolley(in ProjectileSpawnInfo info)
        {
            _tdVolleyExtras++;   // the tracer rhythm counts main rounds only (TdTracerOf)
            int extras = 0, free = 0;
            for (int i = 0; i < TdCapacity; i++)
            {
                if (!_tdRounds[i].Alive) free++;
                else if (_tdRounds[i].Extra) extras++;
            }

            LiveTdVolleyRecords = extras;
            if (extras < TracerStyle.VolleyRecordsMax && free > 0) return true;
            if (_tdLastMain >= 0 && _tdLastMain < TdCapacity)
            {
                ref TdRound m = ref _tdRounds[_tdLastMain];
                if (m.Alive && !m.Extra && !m.Stopped && info.ProjectileId > m.Id && info.ProjectileId - m.Id <= TreeGuardrails.ShotsPerPull)
                {
                    m.Merged++;
                    TdLook(ref m, VfxWeaponProfiles.For(info.WeaponId), m.Tracer);
                    TdVolleyMerged++;
                    return false;
                }
            }

            TdVolleyDropped++;
            return false;
        }

        /// <summary>
        /// Whether the round just admitted to record <paramref name="slot"/> is a tracer: only a main round (VolleyIndex 0) can be, on
        /// its class rhythm counted over the main rounds (the id minus every extra spawned so far — with no extras that is the WP-101
        /// id itself). Remembers the pull's main record for the merge: a bullet, or a shell's centre pellet (index 0; sg_2's tracer).
        /// </summary>
        private bool TdTracerOf(VfxWeaponProfile profile, in ProjectileSpawnInfo info, bool pellet, int slot)
        {
            if (info.VolleyIndex > 0) return false;   // S-02: an extra volley round is a body streak
            if (!pellet || info.PelletIndex == 0) _tdLastMain = slot;
            return IsTdTracer(profile, info.ProjectileId - _tdVolleyExtras, pellet, info.PelletIndex);
        }

        /// <summary>A round that found no record: a refused main round is nobody's merge target (its extras do not widen the last pull's).</summary>
        private void TdRefused(in ProjectileSpawnInfo info)
        {
            if (info.VolleyIndex == 0 && (info.Kind != ProjectileKind.Pellet || info.PelletIndex == 0)) _tdLastMain = -1;
        }

        // ------------------------------------------------------------------ K3 sumi core

        /// <summary>Awake: the ink material — the halo's premultiplied blend, the streak's tail, one queue step later.</summary>
        private void BuildTdInk()
        {
            if (_tdGlowMaterial == null) return;
            _tdInkMaterial = new Material(_tdGlowMaterial) { name = "DD_VfxRibbon TD marked ink (runtime)", hideFlags = HideFlags.DontSave };
            SetTail(_tdInkMaterial, TdCoreFalloff, TdCoreRampShare);
            _tdInkMaterial.renderQueue = (int)RenderQueue.Transparent + TdInkQueueOffset;
        }

        /// <summary>
        /// LateUpdate (TD): every live marked tracer's sumi core along its own streak (head, one-exposure tail, the tail-out), ×
        /// <see cref="TracerStyle.InkWidthShare"/> of the core, α <see cref="TracerStyle.InkAlpha"/> — one instanced draw, none without a
        /// marked round.
        /// </summary>
        private int DrawTdInk()
        {
            using var marker = TreeVfxMarkers.Ink.Auto();
            DrawnInk = 0;
            if (_tdInkMaterial == null) return 0;
            int n = 0;
            for (int i = 0; i < TdCapacity && n < Capacity; i++)
            {
                ref TdRound r = ref _tdRounds[i];
                if (!r.Alive || r.Look != TracerStyle.MarkedLook || !r.Tracer || !TdCoreLive(r)) continue;
                float trail = Mathf.Min((r.Head - r.Origin).magnitude, TdTrail(r));
                if (r.Stopped) trail *= 1f - Mathf.Clamp01((r.Age - r.StopAge) / TdTailOutSeconds);
                if (trail < .01f) continue;
                Vector3 tail = r.Head - r.Direction * trail;
                Color ink = TracerStyle.Ink;
                // the span starts 1 m out (as DrawTd's streaks): the shader's 0.4 m fade-in stays off it
                Write(n, 0, r.Head, tail, r.Core * TracerStyle.InkWidthShare, 0f, 1f + trail, 1f, 0f, 0f, ink, ink, false, false);
                _extra[n] = new Vector4(TracerStyle.InkMinPx, 0f, TracerStyle.ShaderCap(0, true, false, r.CapPx) * TracerStyle.InkWidthShare,
                    TracerStyle.ShaderBase(0, true, false, r.CapPx, r.Base) * TracerStyle.InkWidthShare);
                _headColor[n].w = TracerStyle.InkAlpha;
                _tailColor[n].w = 0f;
                n++;
                n = BendTd(n, i, false, 0f);   // B4-2 (B9): the ink rides the bent streak too
            }

            if (n == 0) return 0;
            _block.SetVectorArray(HeadId, _head); _block.SetVectorArray(TailId, _tail);
            _block.SetVectorArray(HeadColorId, _headColor); _block.SetVectorArray(TailColorId, _tailColor);
            _block.SetVectorArray(SpanId, _span); _block.SetVectorArray(ExtraId, _extra);
            Graphics.DrawMeshInstanced(_quad, 0, _tdInkMaterial, _matrices, n, _block, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
            DrawnInk = n;
            return n;
        }

        // ------------------------------------------------------------------ B4-2 B9 the bending seeker trail

        /// <summary>
        /// TickTd, a live record matched to its projectile: a round that locked a body at spawn (S-02 sh_cap,
        /// <c>ProjectileState.SeekTrail</c>) copies its kept path into the record's own buffer (oldest first, then where it is now),
        /// shifted by this frame's muzzle catch-up like its head. A straight round returns at once (WP-101 / B4-1 as they are). 0 B.
        /// </summary>
        private void TdSeekPath(int slot, ref TdRound r, in ProjectileState p, Vector3 lift)
        {
            if (!p.SeekTrail) return;
            using (TreeSeekTrail.Marker.Auto())
            {
                Vector3[] path = _tdPaths[slot];
                int n = ProjectileSystem.TryGetPath(r.Id, path);
                if (n <= 0) return;   // nothing new: the last kept path stays
                if (lift.sqrMagnitude > 0f) for (int k = 0; k < n; k++) path[k] += lift;
                r.SeekPoints = n;
            }
        }

        /// <summary>
        /// DrawTd / DrawTdInk, right after a record's instance was written straight (head back along its direction): when the record
        /// keeps a path, that instance is re-laid along it — the same width, colours, α, cover, rim, ceiling and floor, on
        /// <see cref="TreeSeekTrail.Lay"/>'s consecutive segments (head end first), each with <c>_Span</c> = (its tail / head distance
        /// along the streak, the rim / fade z, −the streak's length): DD/VFX Ribbon then reads t along the whole streak (one head bead
        /// — <paramref name="bead"/> m, one tail falloff, one head → tail colour) and skips the muzzle fade-in. The length (S-07 12.10
        /// decision (a)): the sniper vapour (<paramref name="vapour"/>) the whole path from the muzzle, every other layer the kept path
        /// up to 10 m (<see cref="TreeSeekTrail.SeekLength"/>) × its tail-out. One instance stays for every later record (the 256 arrays
        /// never overflow); a record with no path (every straight round) returns <paramref name="n"/> untouched. 0 B.
        /// </summary>
        private int BendTd(int n, int slot, bool vapour, float bead)
        {
            ref TdRound r = ref _tdRounds[slot];
            if (r.SeekPoints <= 0 || n <= 0) return n;
            using (TreeSeekTrail.Marker.Auto())
            {
                Vector3[] path = _tdPaths[slot];
                float arc = TreeSeekTrail.ArcLength(path, r.SeekPoints, r.Head);
                float length = vapour ? arc : TreeSeekTrail.SeekLength(arc);   // decision (a): the kept path, ≤ 10 m (the Shot ladder's ceiling)
                if (!vapour && r.Stopped) length *= 1f - Mathf.Clamp01((r.Age - r.StopAge) / TdTailOutSeconds);   // the tail runs in along the path
                if (length < .01f) return n;
                int room = Capacity - n - (TdCapacity - 1 - slot);
                int count = TreeSeekTrail.Lay(path, r.SeekPoints, r.Head, length, 1 + Mathf.Max(0, room), _tdSegs);
                if (count <= 0) return n;
                int k0 = n - 1;
                Vector4 head = _head[k0], headColor = _headColor[k0], tailColor = _tailColor[k0], span = _span[k0], extra = _extra[k0];
                float beadShare = bead > 0f ? Mathf.Clamp01(bead / length) : 0f;
                for (int s = 0; s < count; s++)
                {
                    int k = k0 + s;
                    TreeSeekTrail.Segment g = _tdSegs[s];
                    _head[k] = new Vector4(g.Head.x, g.Head.y, g.Head.z, head.w);
                    _tail[k] = new Vector4(g.Tail.x, g.Tail.y, g.Tail.z, beadShare);
                    _headColor[k] = headColor;
                    _tailColor[k] = tailColor;
                    _span[k] = new Vector4(g.TailAt, g.HeadAt, span.z, -length);
                    _extra[k] = extra;
                }

                TdBentInstances += count - 1;
                return k0 + count;
            }
        }

        /// <summary>
        /// Tests / captures: lays <paramref name="points"/> (oldest first; at most PathPoints + 1) as the kept path of the live TD record
        /// of <paramref name="projectileId"/> — a record with no live projectile (a TdSpawn-made record, a synthetic Emit) keeps it; a
        /// real seeker's TickTd replaces it with S-02's own every frame. The head stays the record's. False: no such record.
        /// </summary>
        public bool SetTdPathForTests(int projectileId, Vector3[] points, int count)
        {
            if (points == null) return false;
            for (int i = 0; i < TdCapacity; i++)
            {
                ref TdRound r = ref _tdRounds[i];
                if (!r.Alive || r.Id != projectileId) continue;
                Vector3[] path = _tdPaths[i];
                int n = Mathf.Clamp(count, 0, Mathf.Min(points.Length, path.Length));
                for (int k = 0; k < n; k++) path[k] = points[k];
                r.SeekPoints = n;
                return true;
            }

            return false;
        }
    }
}
