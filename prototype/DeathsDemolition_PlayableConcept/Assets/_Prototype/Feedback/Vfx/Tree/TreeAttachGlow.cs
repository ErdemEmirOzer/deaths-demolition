using System.Collections.Generic;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons.Attachments;
using UnityEngine;

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
    /// </summary>
    [DefaultExecutionOrder(962)]
    public sealed class TreeAttachGlow : MonoBehaviour
    {
        public const int GlowSlot = 1;
        public const string GlowMaterialName = "M_AP040_Glow";
        /// <summary>URP Lit's emission keyword: off, the shader compiles emission out and a block's _EmissionColor is inert.</summary>
        public const string EmissionKeyword = "_EMISSION";

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly List<Renderer> s_renderers = new List<Renderer>(16);
        private static readonly List<Material> s_materials = new List<Material>(4);

        private MaterialPropertyBlock _block;
        private Material _knownGlow;   // the shared M_AP040_Glow once seen (a reference compare, no name string per renderer)
        private WeaponAttachMount _mount;
        private bool _shown;
        private float _flash = -1f;

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

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => WeaponAttachEvents.Changed -= OnChanged;

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
                    return;
                }

                if (m.Shown != _shown || Mathf.Abs(Mathf.Clamp01(GameFeelSettings.FlashScale) - _flash) > 1e-4f) Paint();
            }
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
            PaintAddOn(m.SideAttachment, SideEmission, ref glow, ref slotless, ref emissionOff);
            PaintAddOn(m.RingAttachment, RingEmission, ref glow, ref slotless, ref emissionOff);
            GlowRenderers = glow;
            SlotlessRenderers = slotless;
            EmissionOffRenderers = emissionOff;
            Applies++;
        }

        private void PaintAddOn(GameObject addOn, Color emission, ref int glow, ref int slotless, ref int emissionOff)
        {
            if (addOn == null) return;
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
            }

            s_renderers.Clear();
            s_materials.Clear();
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
