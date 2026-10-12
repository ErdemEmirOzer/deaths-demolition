using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons.Attachments;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-104 B4 (S-07) · AD §2's emissive ladder on S-06's weapon add-ons (AD §1.4 / A14: a 5 px E4 capsule reads only by its
    /// bloom spot). S-06 raises <see cref="WeaponAttachEvents.Changed"/> on a tree / equip / camera change (never per frame); the
    /// mount's <see cref="WeaponAttachMount.Set"/> carries <c>SideTier</c> (E4, the Element branch's tier while a dominant element
    /// shows) and <c>RingTier</c> (E5). Every LOD renderer of <see cref="WeaponAttachMount.SideAttachment"/> /
    /// <see cref="WeaponAttachMount.RingAttachment"/> whose material slot 1 is the shared <c>M_AP040_Glow</c> takes
    /// <c>_EmissionColor</c> = white × AD-B4 M9 × FlashScale (E4 III 1.4 · IV 1.6 · V 2.0, E5 IV 1.4 · V 2.0; the open rungs keep §2's
    /// 0 / 0.3 / 0.6 — <see cref="TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow, int, float, bool)"/>) through its own
    /// MaterialPropertyBlock on that slot — the shared material never changes. While the add-ons are not shown (S-06: TopDown
    /// only) the slot is black. SetShown raises no event, so the mount's Shown and Reduce Flashes are compared once a frame (two
    /// field reads) and the ladder re-applied only when one changed. <c>element_capsule_V03</c> (electric) has no glow slot
    /// (AP-040 data): it is counted in <see cref="SlotlessRenderers"/> and stays dark until AP-040 v002 or the B4-2 ElementVfx
    /// socket VFX. Work happens on events only; 0 B per frame; marker <c>WP104.Vfx.Glow</c>.
    /// Where the glow shows (AD-B4 §2/13, measured 12.10 on the AP-040 v001 meshes): slot 1 is the add-on's side-facing window —
    /// E4 V01 (fire) shows 0 px of it from straight above (its +Y-facing slot-1 area is 0.0007 m² against 0.010 m² per side) and
    /// ≈ 30 px at 101 px/m from the game's 49° TD rig; E5 V01 ≈ 3 px from above, 1–7 px from the rig. A straight-down capture
    /// therefore cannot see this ladder; the slot's binding (URP Lit, <c>_EMISSION</c>, <c>_EmissionColor</c>) is sound and
    /// <see cref="EmissionOffRenderers"/> stays 0.
    /// <para>WP-104 B4-2 (AD-B4 §6 / M9 post): the slot's bloom spot is a socket glint card (<see cref="TreeSocketCard"/>) — one own
    /// quad per add-on (E4, E5), the AP-041 glint sheet added at the glow slot's world position, painted with the ladder (value ×
    /// FlashScale, E4 III+ / E5 IV+, none while hidden) and moved onto the slot every frame (0 B, marker <c>WP104.Vfx.SocketCard</c>).
    /// The cards are this component's children, never the add-on's: S-06's renderers, their bounds and the AD capture's glow-slot
    /// mask stay the add-on's own.</para>
    /// </summary>
    [DefaultExecutionOrder(962)]
    public sealed class TreeAttachGlow : MonoBehaviour
    {
        public const int GlowSlot = 1;
        public const string GlowMaterialName = "M_AP040_Glow";
        /// <summary>URP Lit's emission keyword: off, the shader compiles emission out and a block's _EmissionColor is inert.</summary>
        public const string EmissionKeyword = "_EMISSION";

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int AlphaCapId = Shader.PropertyToID("_AlphaCap");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly List<Renderer> s_renderers = new List<Renderer>(16);
        private static readonly List<Material> s_materials = new List<Material>(4);

        private MaterialPropertyBlock _block;
        private Material _knownGlow;   // the shared M_AP040_Glow once seen (a reference compare, no name string per renderer)
        private WeaponAttachMount _mount;
        private bool _shown;
        private float _flash = -1f;

        // WP-104 B4-2 · the socket glint cards (one own quad per add-on; the catalog row's material once, added)
        private sealed class SocketCard
        {
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Block;
            public Transform Anchor;   // the add-on's glow-slot renderer
            public Vector3 Local;      // the glow slot's centre in the anchor's space
            public float Size, Lift;
            public Vector4 Tint;
            public bool On;
        }

        private readonly SocketCard _sideCard = new SocketCard(), _ringCard = new SocketCard();
        private Material _cardMaterial;
        private bool _cardMaterialTried;
        private bool _cardsEnabled = true;

        /// <summary>The mount last painted (tests / F1).</summary>
        public WeaponAttachMount Mount => _mount;
        /// <summary>Paints since start-up (event-time only).</summary>
        public int Applies { get; private set; }
        /// <summary>Renderers whose glow slot took the ladder in the last paint.</summary>
        public int GlowRenderers { get; private set; }
        /// <summary>Add-on renderers without a <c>M_AP040_Glow</c> slot 1 in the last paint (E4 V03 electric).</summary>
        public int SlotlessRenderers { get; private set; }
        /// <summary>Glow-slot renderers painted in the last paint whose material has <c>_EMISSION</c> off (a block cannot turn a keyword
        /// on: the paint would be invisible). 0 while M_AP040_Glow keeps the keyword (its m_ValidKeywords).</summary>
        public int EmissionOffRenderers { get; private set; }
        public Color SideEmission { get; private set; }
        public Color RingEmission { get; private set; }

        // WP-104 B4-2 socket glint cards (tests / F1 / the AD capture)
        /// <summary>The E4 / E5 socket glint card is drawn.</summary>
        public bool SideCardShown => _sideCard.On;
        public bool RingCardShown => _ringCard.On;
        /// <summary>The card's linear <c>_Tint</c> (hue × the ladder value × FlashScale; 0 while off).</summary>
        public Vector4 SideCardTint => _sideCard.On ? _sideCard.Tint : Vector4.zero;
        public Vector4 RingCardTint => _ringCard.On ? _ringCard.Tint : Vector4.zero;
        /// <summary>The card's edge (m) — <see cref="TreeSocketCard.Size"/> of its add-on.</summary>
        public float SideCardSize => _sideCard.On ? _sideCard.Size : 0f;
        public float RingCardSize => _ringCard.On ? _ringCard.Size : 0f;
        /// <summary>The card quads (null until a card was first needed).</summary>
        public Renderer SideCardRenderer => _sideCard.Renderer;
        public Renderer RingCardRenderer => _ringCard.Renderer;
        /// <summary>The AP-041 glint row resolved (the catalog has <see cref="TreeSocketCard.Row"/> with a material).</summary>
        public bool CardRowReady => _cardMaterial != null;

        /// <summary>
        /// Tests / captures: false takes both cards off at once (true paints them back with the ladder). The AD capture's glow-slot mask
        /// (the slot drawn unlit white vs black into an 8-bit frame) must read the slot without an added card over it.
        /// </summary>
        public bool CardsEnabled
        {
            get => _cardsEnabled;
            set
            {
                if (_cardsEnabled == value) return;
                _cardsEnabled = value;
                if (!value) HideCards();
                else Paint();
            }
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            WeaponAttachEvents.Changed -= OnChanged;
            HideCards();
            _flash = -1f;   // re-enabled: the next poll repaints (and shows the cards again)
        }

        private void OnDestroy()
        {
            if (_cardMaterial != null) Destroy(_cardMaterial);
            _cardMaterial = null;
        }

        /// <summary>(Re)subscribes (TreeVfxBinder.Rearm after a fixture cleared the static bus) and adopts the mount in hand.</summary>
        public void Subscribe()
        {
            WeaponAttachEvents.Changed -= OnChanged;
            WeaponAttachEvents.Changed += OnChanged;
            WeaponAttachMount active = WeaponAttachments.Active;
            if (active != null && active != _mount) OnChanged(active);
        }

        private void OnChanged(WeaponAttachMount mount)
        {
            _mount = mount;
            Paint();
        }

        private void LateUpdate()
        {
            using (TreeVfxMarkers.Glow.Auto())
            {
                WeaponAttachMount m = _mount;
                if (m == null)
                {
                    // destroyed with its model (a weapon switch): the next mount's Apply raises Changed; the presenter's Active covers a
                    // mount that changed before this component listened
                    _mount = null;
                    WeaponAttachMount active = WeaponAttachments.Active;
                    if (active != null) OnChanged(active);
                    else HideCards();   // WP-104 B4-2: no mount, no glint
                    return;
                }

                if (m.Shown != _shown || Mathf.Abs(Mathf.Clamp01(GameFeelSettings.FlashScale) - _flash) > 1e-4f) Paint();
            }

            TrackCards();   // WP-104 B4-2: the glint follows the slot every frame
        }

        /// <summary>The ladder on the current mount's E4 / E5 glow slots (event time; tests call it after building a mount).</summary>
        public void Paint()
        {
            WeaponAttachMount m = _mount;
            if (m == null || _block == null) return;
            _shown = m.Shown;
            _flash = Mathf.Clamp01(GameFeelSettings.FlashScale);
            WeaponAttachSet set = m.Set;
            SideEmission = TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Side, set.SideTier, _flash, _shown && set.Side != TreeElement.None);
            RingEmission = TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Ring, set.RingTier, _flash, _shown && set.Ring > 0);
            int glow = 0, slotless = 0, emissionOff = 0;
            Renderer sideSlot = PaintAddOn(m.SideAttachment, SideEmission, ref glow, ref slotless, ref emissionOff);
            Renderer ringSlot = PaintAddOn(m.RingAttachment, RingEmission, ref glow, ref slotless, ref emissionOff);
            GlowRenderers = glow;
            SlotlessRenderers = slotless;
            EmissionOffRenderers = emissionOff;
            Applies++;
            using (TreeSocketCard.Marker.Auto())
            {
                // WP-104 B4-2 (AD-B4 §6): the bloom spot as a glint card on each lit slot (E4 III+, E5 IV+), × FlashScale, none while hidden
                PaintCard(_sideCard, sideSlot, TreeVfxLadder.AddOnGlow.Side, set.SideTier, set.Side, 0, _shown && set.Side != TreeElement.None);
                PaintCard(_ringCard, ringSlot, TreeVfxLadder.AddOnGlow.Ring, set.RingTier, TreeElement.None, set.Ring, _shown && set.Ring > 0);
            }
        }

        /// <summary>Paints the add-on's glow-slot renderers; returns the first of them (the socket card's anchor), null when none.</summary>
        private Renderer PaintAddOn(GameObject addOn, Color emission, ref int glow, ref int slotless, ref int emissionOff)
        {
            if (addOn == null) return null;
            Renderer first = null;
            addOn.GetComponentsInChildren(true, s_renderers);
            for (int i = 0; i < s_renderers.Count; i++)
            {
                Renderer r = s_renderers[i];
                if (r == null) continue;
                r.GetSharedMaterials(s_materials);
                Material slot = s_materials.Count > GlowSlot ? s_materials[GlowSlot] : null;
                if (slot == null || (!ReferenceEquals(slot, _knownGlow) && !IsGlowMaterial(slot)))
                {
                    slotless++;
                    continue;
                }

                _knownGlow = slot;
                if (!slot.IsKeywordEnabled(EmissionKeyword)) emissionOff++;

                r.GetPropertyBlock(_block, GlowSlot);
                _block.SetColor(EmissionColorId, emission);
                r.SetPropertyBlock(_block, GlowSlot);
                glow++;
                if (first == null) first = r;
            }

            s_renderers.Clear();
            s_materials.Clear();
            return first;
        }

        // ------------------------------------------------------------------ WP-104 B4-2 socket glint cards (AD-B4 §6 / M9 post)

        /// <summary>
        /// Event time (a paint): the card of one add-on — off unless <see cref="TreeSocketCard.Shows"/> (and the glint row resolved,
        /// and FlashScale above 0); else anchored on <paramref name="slot"/>'s glow submesh, sized by its mesh, tinted by the ladder ×
        /// FlashScale, on the add-on's layer (the cameras that see the add-on see its glint), and placed at once.
        /// </summary>
        private void PaintCard(SocketCard c, Renderer slot, TreeVfxLadder.AddOnGlow kind, int tier, TreeElement element, int ring, bool shown)
        {
            if (!_cardsEnabled || _flash <= 0f || !TreeSocketCard.Shows(kind, tier, shown, slot != null)
                || !TreeSocketCard.TryAnchor(slot, out Vector3 local, out float diagonal))
            {
                HideCard(c);
                return;
            }

            Material material = CardMaterial();
            if (material == null)
            {
                HideCard(c);
                return;
            }

            if (c.Renderer == null) BuildCard(c, kind == TreeVfxLadder.AddOnGlow.Side ? "E4 glint card" : "E5 glint card");
            c.Anchor = slot.transform;
            c.Local = local;
            c.Size = TreeSocketCard.Size(diagonal);
            c.Lift = TreeSocketCard.Lift(diagonal);
            c.Tint = TreeSocketCard.Tint(kind, tier, element, ring, _flash);
            MeshRenderer r = c.Renderer;
            if (r.gameObject.layer != slot.gameObject.layer) r.gameObject.layer = slot.gameObject.layer;
            if (r.sharedMaterial != material) r.sharedMaterial = material;
            r.transform.localScale = new Vector3(c.Size, c.Size, 1f);
            c.Block.SetFloat(FrameId, TreeSocketCard.Frame);
            c.Block.SetFloat(AlphaId, 1f);
            c.Block.SetFloat(AlphaCapId, 1f);
            c.Block.SetVector(TintId, c.Tint);   // linear HDR: no gamma step (SetColor would raise 2.0 to 4.6)
            r.SetPropertyBlock(c.Block);
            c.On = true;
            Place(c, TreeSocketCard.Facing(FeelRuntime.Current.Camera));
            if (!r.enabled) r.enabled = true;
        }

        /// <summary>Every frame: each shown card onto its slot, facing the TD rig (0 B); a card whose add-on went is hidden until the
        /// next paint.</summary>
        private void TrackCards()
        {
            if (!_sideCard.On && !_ringCard.On) return;
            using (TreeSocketCard.Marker.Auto())
            {
                Vector3 facing = TreeSocketCard.Facing(FeelRuntime.Current.Camera);
                Place(_sideCard, facing);
                Place(_ringCard, facing);
            }
        }

        private static void Place(SocketCard c, Vector3 facing)
        {
            if (!c.On) return;
            if (c.Anchor == null)
            {
                HideCard(c);
                return;
            }

            c.Renderer.transform.SetPositionAndRotation(c.Anchor.TransformPoint(c.Local) + facing * c.Lift, TreeSocketCard.Rotation(facing));
        }

        private void HideCards()
        {
            HideCard(_sideCard);
            HideCard(_ringCard);
        }

        private static void HideCard(SocketCard c)
        {
            c.On = false;
            c.Anchor = null;
            if (c.Renderer != null && c.Renderer.enabled) c.Renderer.enabled = false;
        }

        /// <summary>The glint row's material once (VfxLibrary), as an own copy drawn ADDED: the premultiplied sheet on Blend One One.
        /// Null without the catalog row (the art pipeline has not run): no card, the ladder unchanged.</summary>
        private Material CardMaterial()
        {
            if (_cardMaterial != null || _cardMaterialTried) return _cardMaterial;
            _cardMaterialTried = true;
            if (!VfxLibrary.TryGetPlayable(TreeVfxIds.Id(TreeSocketCard.Row), out VfxCatalogEntry row)) return null;
            _cardMaterial = new Material(row.Material) { name = row.Material.name + " (WP-104 socket glint, added)", hideFlags = HideFlags.DontSave };
            _cardMaterial.SetFloat(DstBlendId, (float)BlendMode.One);
            return _cardMaterial;
        }

        private void BuildCard(SocketCard c, string name)
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
            c.Renderer = r;
            c.Block = new MaterialPropertyBlock();
        }

        /// <summary>S-06's slot-1 contract: the shared <c>M_AP040_Glow</c> (URP Lit with _EMISSION).</summary>
        public static bool IsGlowMaterial(Material m) =>
            m != null && m.name.StartsWith(GlowMaterialName, System.StringComparison.Ordinal) && m.HasProperty(EmissionColorId);

        /// <summary>The glow slot really renders a block's <c>_EmissionColor</c> (tests): the glow material with <c>_EMISSION</c> on,
        /// an emission map, and its own submesh — a slot past the mesh's submeshes would redraw the last one whole.</summary>
        public static bool GlowSlotRenders(Renderer r)
        {
            if (r == null) return false;
            Material[] mats = r.sharedMaterials;
            if (mats.Length <= GlowSlot || !IsGlowMaterial(mats[GlowSlot]) || !mats[GlowSlot].IsKeywordEnabled(EmissionKeyword)) return false;
            if (!mats[GlowSlot].HasProperty("_EmissionMap") || mats[GlowSlot].GetTexture("_EmissionMap") == null) return false;
            MeshFilter f = r.GetComponent<MeshFilter>();
            var skinned = r as SkinnedMeshRenderer;
            Mesh mesh = f != null ? f.sharedMesh : skinned != null ? skinned.sharedMesh : null;
            return mesh != null && mesh.subMeshCount > GlowSlot && mesh.GetSubMesh(GlowSlot).indexCount > 0;
        }

        /// <summary>The emission a renderer's glow slot carries now (tests): its slot-1 block's _EmissionColor, black when unset.</summary>
        public static Color EmissionOf(Renderer r, MaterialPropertyBlock scratch)
        {
            if (r == null || scratch == null) return Color.black;
            r.GetPropertyBlock(scratch, GlowSlot);
            return scratch.isEmpty ? Color.black : scratch.GetColor(EmissionColorId);
        }
    }
}
