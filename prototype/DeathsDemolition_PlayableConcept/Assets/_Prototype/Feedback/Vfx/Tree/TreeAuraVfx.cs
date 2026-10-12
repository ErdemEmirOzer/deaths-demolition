using DD.Prototype.Rounds;
using DD.Prototype.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-104 B4 (S-07) · The player-side persistent tree layers (AD §1.1 (d), K7):
    /// <list type="bullet">
    /// <item>B11 global power aura — <see cref="TreeStats.AuraLevel"/> 1–3: the grey AP-041 ground ring tinted #5C8DFF / #FFE8B8 /
    /// #E8302A at r 1.0 / 1.3 / 1.6 m and α ≤ 0.45 / 0.55 / 0.60 (the sheet's authored size IS that radius), its tier's rising sparks
    /// (6 / 12 / 20 per s baked in the sheet, ≤ 12 / 18 / 24 live) and at III the sumi smoke (α ≤ 0.25). One level at a time; the
    /// ring lies on the ground under the body, never over the obi / head band.</item>
    /// <item>B14 shield dome — the TD rim at body height (r 1.1 m) while Spirit Ward / Stone Guardian's shield stands. S-04 12.10 §4:
    /// <see cref="TreeProcKind.ShieldBreak"/> (16) hides it, <see cref="TreeProcKind.ShieldUp"/> (25: refilled to full / re-raised) shows
    /// it again, and so does a run or round start (the shield starts full there without a proc). An absorbed hit
    /// (<see cref="TreeProcKind.ShieldHit"/>, 26) is the binder's B14 hit wave. While the dome stands the aura sparks drop to 50 % (K7).</item>
    /// </list>
    /// AD-B4 M6: under the ring lies its sumi underdraw — the same sheet and frame once more, #12100F at α ≤ 0.35, × 1.04, 1 cm lower
    /// (<see cref="UnderLift"/>: farther from any camera above, so the back-to-front transparent queue always draws it first) — the
    /// ink rim the pale rings need on light stone.
    /// AD-B4 S2 (B4-2): over the III ring, under its sparks (<see cref="LineLift"/>), a thin light inner line — a procedural soft
    /// annulus (<see cref="BuildLineTexture"/>, premultiplied white, built once; no asset) on the ring sheet's own material, tinted
    /// <see cref="TreeVfxLadder.AuraLineTint"/> — the value step the crimson ring lacks on wet slate.
    /// Reduce Flashes: sparks × 0.5, the ring holds still (no pulse); Reduced Motion: every layer holds its frame. The layers are six
    /// own quads (no pool: they live the whole run and must never be recycled), frames from <see cref="VfxClock.Now"/> so the
    /// diorama shows the "after" aura too. 0 B per frame; marker <c>WP104.Vfx.Aura</c>.
    /// </summary>
    [DefaultExecutionOrder(961)]
    public sealed class TreeAuraVfx : MonoBehaviour
    {
        public const float RingLift = .03f, SparksLift = .05f, SmokeLift = .06f, DomeHeight = 1f;
        /// <summary>AD-B4 M6: the ring's sumi underdraw, 1 cm under the ring.</summary>
        public const float UnderLift = .02f;
        /// <summary>AD-B4 S2: the III inner line, over the ring (0.03) and under the sparks (0.05).</summary>
        public const float LineLift = .035f;
        /// <summary>The inner line texture: its annulus at this share of the quad's half size (the quad is sized around it).</summary>
        public const float LineTexRadius = .9f;
        public const int LineTexSize = 256;
        public const float EntryPulseSeconds = .6f, EntryGrowFrom = .35f;

        private const int Ring = 0, Sparks = 1, Smoke = 2, Dome = 3, Under = 4, Line = 5, QuadCount = 6;
        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int AlphaCapId = Shader.PropertyToID("_AlphaCap");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int SheetId = Shader.PropertyToID("_Sheet");
        private static readonly int ColsId = Shader.PropertyToID("_Cols");
        private static readonly int RowsId = Shader.PropertyToID("_Rows");
        private static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f);   // VfxLibrary.Quad (XY, normal −Z) lying face up
        // AD-B4 M6: sumi, its α the coverage scale (DD/VFX Flipbook: fade = tint α × _Alpha; the 0.35 cap holds the result)
        private static readonly Color s_underTint = new Color(TreeVfxLadder.AuraUnderTint.r, TreeVfxLadder.AuraUnderTint.g,
            TreeVfxLadder.AuraUnderTint.b, TreeVfxLadder.AuraUnderCoverage);
        // AD-B4 S2: the line's colour, its α the coverage scale (the texture's annulus peaks at 1; the cap holds the same α)
        private static readonly Color s_lineTint = new Color(TreeVfxLadder.AuraLineTint.r, TreeVfxLadder.AuraLineTint.g,
            TreeVfxLadder.AuraLineTint.b, TreeVfxLadder.AuraLineAlpha);

        private readonly MeshRenderer[] _quads = new MeshRenderer[QuadCount];
        private readonly MaterialPropertyBlock[] _blocks = new MaterialPropertyBlock[QuadCount];
        private readonly VfxCatalogEntry[] _drawn = new VfxCatalogEntry[QuadCount];
        private TreeVfxBinder _binder;
        private int _level;
        private float _levelSince;
        private bool _shieldBroken;
        // AD-B4 S2: the inner line's runtime row (its own texture on a copy of the III ring's material), owned here
        private VfxCatalogEntry _lineRow;
        private Texture2D _lineTex;
        private Material _lineMat;

        /// <summary>The aura level drawn (0 = none).</summary>
        public int Level => _level;
        public bool RingVisible => IsOn(Ring);
        public bool SparksVisible => IsOn(Sparks);
        public bool SmokeVisible => IsOn(Smoke);
        public bool DomeVisible => IsOn(Dome);
        public VfxCatalogEntry RingRow => _drawn[Ring];
        public VfxCatalogEntry SparksRow => _drawn[Sparks];
        /// <summary>The sparks' alpha cap this frame (1, × 0.5 under Reduce Flashes, × 0.5 with the shield up).</summary>
        public float SparkAlphaCap { get; private set; }
        public float RingScale => IsOn(Ring) ? _quads[Ring].transform.localScale.x : 0f;
        public int RingFrame { get; private set; }
        /// <summary>AD-B4 M6: the ring's sumi underdraw (the ring's row and frame, × <see cref="TreeVfxLadder.AuraUnderScale"/>).</summary>
        public bool UnderVisible => IsOn(Under);
        public VfxCatalogEntry UnderRow => _drawn[Under];
        public float UnderScale => IsOn(Under) ? _quads[Under].transform.localScale.x : 0f;
        public int UnderFrame { get; private set; }
        /// <summary>The ring's and the underdraw's renderers (tests: draw order, property block).</summary>
        public Renderer RingRenderer => _quads[Ring];
        public Renderer UnderRenderer => _quads[Under];
        /// <summary>AD-B4 S2: the III inner line (its runtime row, quad size in metres, renderer).</summary>
        public bool LineVisible => IsOn(Line);
        public VfxCatalogEntry LineRow => _lineRow;
        public float LineScale => IsOn(Line) ? _quads[Line].transform.localScale.x : 0f;
        public Renderer LineRenderer => _quads[Line];

        /// <summary>The inner line quad's size at <paramref name="level"/>: its annulus (at <see cref="LineTexRadius"/> of the half size)
        /// on <see cref="TreeVfxLadder.AuraLineRadiusShare"/> × the level's ring radius.</summary>
        public static float LineQuadSize(int level) => 2f * TreeVfxLadder.AuraRadius(level) * TreeVfxLadder.AuraLineRadiusShare / LineTexRadius;

        internal void Configure(TreeVfxBinder binder)
        {
            _binder = binder;
            if (_quads[Ring] == null)
            {
                Build(Ring, "AuraRing");
                Build(Sparks, "AuraSparks");
                Build(Smoke, "AuraSmoke");
                Build(Dome, "ShieldDome");
                Build(Under, "AuraRingUnder");
                Build(Line, "AuraInnerLine");
            }

            EnsureLine();
        }

        /// <summary>AD-B4 S2: the inner line's row, once the III ring's material is in the catalog (idempotent; Configure runs per scene).</summary>
        private void EnsureLine()
        {
            if (_lineRow != null || _binder == null) return;
            VfxCatalogEntry ring = _binder.Row(TreeVfxRow.B11Ring3);
            if (ring == null || ring.Material == null) return;
            float halfWidth = LineTexRadius * .5f * TreeVfxLadder.AuraLineWidthMetres
                              / Mathf.Max(.01f, TreeVfxLadder.AuraLineRadiusShare * TreeVfxLadder.AuraRadius(TreeVfxLadder.AuraLevels));
            using (TreeVfxMarkers.Procedural.Auto()) _lineTex = BuildLineTexture(LineTexSize, LineTexRadius, halfWidth);
            _lineMat = new Material(ring.Material) { name = "DD_AuraInnerLine (runtime)", hideFlags = HideFlags.DontSave };
            _lineMat.SetTexture(SheetId, _lineTex);
            _lineMat.SetFloat(ColsId, 1f);
            _lineMat.SetFloat(RowsId, 1f);
            _lineRow = new VfxCatalogEntry
            {
                Id = "rt_ap041t_b11_inner_line", Family = ring.Family, Kind = ring.Kind, Texture = _lineTex, Material = _lineMat, Columns = 1, Rows = 1,
                FramesUsed = 1, WorldSizeMetres = 1f, WorldHeightMetres = 1f, Premultiplied = true
            };
        }

        /// <summary>
        /// The inner line: a soft annulus — α = (1 − t²)², t = |r − <paramref name="radius"/>| / <paramref name="halfWidth"/> (half-size
        /// units) — premultiplied white in a linear RGBA32 texture (rgb = α), mipmapped, built once (no asset, no per-frame cost).
        /// </summary>
        public static Texture2D BuildLineTexture(int size, float radius, float halfWidth)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "DD_AuraInnerLine (runtime)", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var px = new Color32[size * size];
            float half = size * .5f, hw = Mathf.Max(1e-4f, halfWidth);
            for (int y = 0; y < size; y++)
            {
                float dy = (y + .5f - half) / half;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f - half) / half;
                    float u = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - radius) / hw;
                    if (u >= 1f) continue;
                    float a = (1f - u * u) * (1f - u * u);
                    byte b = (byte)(a * 255f + .5f);
                    px[y * size + x] = new Color32(b, b, b, b);
                }
            }

            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        private void OnDestroy()
        {
            if (_lineMat != null) Destroy(_lineMat);
            if (_lineTex != null) Destroy(_lineTex);
            _lineMat = null;
            _lineTex = null;
            _lineRow = null;
        }

        private void Build(int index, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = VfxLibrary.Quad;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.enabled = false;
            _quads[index] = r;
            _blocks[index] = new MaterialPropertyBlock();
        }

        /// <summary>TreeEvents.AuraChanged: a higher level grows its ring in once (B11 "kademe giriş halkası").</summary>
        public void SetLevel(int level, bool entryPulse)
        {
            level = TreeVfxLadder.ClampAura(level);
            if (level == _level) return;
            bool up = level > _level;
            _level = level;
            _levelSince = VfxClock.Now;
            Transform a = TreeVfxBinder.PlayerAnchor();
            if (entryPulse && up && a != null && _binder != null) _binder.AuraEntry(level, a.position);
        }

        /// <summary>ShieldBreak proc (16): the dome goes until <see cref="OnShieldUp"/> or the next run / round start.</summary>
        public void OnShieldBreak() => _shieldBroken = true;

        /// <summary>ShieldUp proc (25): the shield refilled to full or came up again — the dome is back.</summary>
        public void OnShieldUp() => _shieldBroken = false;

        /// <summary>Run / round start: S-04 fills the shield without a proc (TreeHooks.ShieldFill), so the dome is back too.</summary>
        public void ResetShield() => _shieldBroken = false;

        /// <summary>The dome is hidden by a break (tests / F1).</summary>
        public bool ShieldBroken => _shieldBroken;

        private void LateUpdate()
        {
            using (TreeVfxMarkers.Aura.Auto())
            {
                Transform anchor = TreeVfxBinder.PlayerAnchor();
                ref readonly TreeStats s = ref (TreeVfxPreview.Active ? ref TreeVfxPreview.Stats : ref TreeRuntime.Current);
                int level = TreeVfxLadder.ClampAura(s.AuraLevel);
                if (level != _level)
                {
                    _level = level;
                    _levelSince = VfxClock.Now;
                }

                if (anchor == null || _binder == null)
                {
                    HideAll();
                    return;
                }

                float now = VfxClock.Now;
                bool reducedFlashes = GameFeelSettings.FlashScale < .99f;
                bool still = UiMotion.ReducedMotion;
                bool dome = s.ShieldShare > 0f && !_shieldBroken;
                Vector3 feet = anchor.position;

                if (dome) Draw(Dome, TreeVfxRow.B14Dome, feet + Vector3.up * DomeHeight, 1f, Color.white, 0f, true);
                else Hide(Dome);

                if (level < 1)
                {
                    Hide(Ring);
                    Hide(Under);
                    Hide(Line);
                    Hide(Sparks);
                    Hide(Smoke);
                    SparkAlphaCap = 0f;
                    return;
                }

                float age = now - _levelSince;
                bool ringStill = still || reducedFlashes;
                TreeVfxRow ring = TreeVfxIds.AuraRing(level);
                UnderFrame = Draw(Under, ring, feet + Vector3.up * UnderLift, TreeVfxLadder.AuraUnderAlpha, s_underTint, age, ringStill,
                    TreeVfxLadder.AuraUnderScale);
                RingFrame = Draw(Ring, ring, feet + Vector3.up * RingLift, TreeVfxLadder.AuraAlpha(level), TreeVfxLadder.AuraTint(level), age, ringStill);
                // AD-B4 S2: III's light inner line, still (one frame), over the ring
                float line = TreeVfxLadder.AuraLine(level);
                if (line > 0f && _lineRow != null) Draw(Line, _lineRow, feet + Vector3.up * LineLift, line, s_lineTint, 0f, true, LineQuadSize(level));
                else Hide(Line);
                SparkAlphaCap = (reducedFlashes ? TreeVfxLadder.AuraSparkDamp : 1f) * (dome ? TreeVfxLadder.AuraSparkDamp : 1f);
                Draw(Sparks, TreeVfxIds.AuraSparks(level), feet + Vector3.up * SparksLift, SparkAlphaCap, Color.white, age, still);
                if (level >= 3) Draw(Smoke, TreeVfxRow.B11Smoke3, feet + Vector3.up * SmokeLift, TreeVfxLadder.AuraSmokeAlpha, Color.white, age, still);
                else Hide(Smoke);
            }
        }

        /// <summary>One looping layer: the row's material, its authored size × <paramref name="scale"/>, the frame of
        /// <paramref name="age"/> (held at 0 when still).</summary>
        private int Draw(int index, TreeVfxRow row, Vector3 position, float alphaCap, Color tint, float age, bool hold, float scale = 1f) =>
            Draw(index, _binder.Row(row), position, alphaCap, tint, age, hold, scale);

        private int Draw(int index, VfxCatalogEntry entry, Vector3 position, float alphaCap, Color tint, float age, bool hold, float scale)
        {
            MeshRenderer r = _quads[index];
            if (entry == null || entry.Material == null || r == null)
            {
                Hide(index);
                return 0;
            }

            if (_drawn[index] != entry)
            {
                r.sharedMaterial = entry.Material;
                _drawn[index] = entry;
            }

            float size = Mathf.Max(.05f, entry.WorldSizeMetres);
            float height = entry.WorldHeightMetres > 0f ? entry.WorldHeightMetres : size;
            Transform t = r.transform;
            t.SetPositionAndRotation(position, Flat);
            t.localScale = new Vector3(size * scale, height * scale, 1f);
            int frames = Mathf.Max(1, entry.FramesUsed);
            float fps = entry.Fps > 0f ? entry.Fps : 24f;
            int frame = hold || frames == 1 ? 0 : (int)(Mathf.Max(0f, age) * fps) % frames;
            MaterialPropertyBlock block = _blocks[index];
            block.SetFloat(FrameId, frame);
            block.SetFloat(AlphaId, 1f);
            block.SetFloat(AlphaCapId, Mathf.Clamp01(alphaCap));
            block.SetColor(TintId, tint);
            r.SetPropertyBlock(block);
            if (!r.enabled) r.enabled = true;
            return frame;
        }

        private void Hide(int index)
        {
            MeshRenderer r = _quads[index];
            if (r != null && r.enabled) r.enabled = false;
        }

        private void HideAll()
        {
            for (int i = 0; i < QuadCount; i++) Hide(i);
        }

        private bool IsOn(int index) => _quads[index] != null && _quads[index].enabled;
    }
}
