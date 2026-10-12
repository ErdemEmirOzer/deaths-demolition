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
    /// Reduce Flashes: sparks × 0.5, the ring holds still (no pulse); Reduced Motion: every layer holds its frame. The layers are five
    /// own quads (no pool: they live the whole run and must never be recycled), frames from <see cref="VfxClock.Now"/> so the
    /// diorama shows the "after" aura too. 0 B per frame; marker <c>WP104.Vfx.Aura</c>.
    /// </summary>
    [DefaultExecutionOrder(961)]
    public sealed class TreeAuraVfx : MonoBehaviour
    {
        public const float RingLift = .03f, SparksLift = .05f, SmokeLift = .06f, DomeHeight = 1f;
        /// <summary>AD-B4 M6: the ring's sumi underdraw, 1 cm under the ring.</summary>
        public const float UnderLift = .02f;
        public const float EntryPulseSeconds = .6f, EntryGrowFrom = .35f;

        private const int Ring = 0, Sparks = 1, Smoke = 2, Dome = 3, Under = 4, QuadCount = 5;
        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int AlphaCapId = Shader.PropertyToID("_AlphaCap");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f);   // VfxLibrary.Quad (XY, normal −Z) lying face up
        // AD-B4 M6: sumi, its α the coverage scale (DD/VFX Flipbook: fade = tint α × _Alpha; the 0.35 cap holds the result)
        private static readonly Color s_underTint = new Color(TreeVfxLadder.AuraUnderTint.r, TreeVfxLadder.AuraUnderTint.g,
            TreeVfxLadder.AuraUnderTint.b, TreeVfxLadder.AuraUnderCoverage);

        private readonly MeshRenderer[] _quads = new MeshRenderer[QuadCount];
        private readonly MaterialPropertyBlock[] _blocks = new MaterialPropertyBlock[QuadCount];
        private readonly VfxCatalogEntry[] _drawn = new VfxCatalogEntry[QuadCount];
        private TreeVfxBinder _binder;
        private int _level;
        private float _levelSince;
        private bool _shieldBroken;

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

        internal void Configure(TreeVfxBinder binder)
        {
            _binder = binder;
            if (_quads[Ring] != null) return;
            Build(Ring, "AuraRing");
            Build(Sparks, "AuraSparks");
            Build(Smoke, "AuraSmoke");
            Build(Dome, "ShieldDome");
            Build(Under, "AuraRingUnder");
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
                SparkAlphaCap = (reducedFlashes ? TreeVfxLadder.AuraSparkDamp : 1f) * (dome ? TreeVfxLadder.AuraSparkDamp : 1f);
                Draw(Sparks, TreeVfxIds.AuraSparks(level), feet + Vector3.up * SparksLift, SparkAlphaCap, Color.white, age, still);
                if (level >= 3) Draw(Smoke, TreeVfxRow.B11Smoke3, feet + Vector3.up * SmokeLift, TreeVfxLadder.AuraSmokeAlpha, Color.white, age, still);
                else Hide(Smoke);
            }
        }

        /// <summary>One looping layer: the row's material, its authored size × <paramref name="scale"/>, the frame of
        /// <paramref name="age"/> (held at 0 when still).</summary>
        private int Draw(int index, TreeVfxRow row, Vector3 position, float alphaCap, Color tint, float age, bool hold, float scale = 1f)
        {
            VfxCatalogEntry entry = _binder.Row(row);
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
