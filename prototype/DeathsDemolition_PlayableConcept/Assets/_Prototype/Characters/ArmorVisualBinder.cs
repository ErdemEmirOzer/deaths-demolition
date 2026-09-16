using System;
using DD.Prototype.Core.Config;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-017 · The AP-008 armour state machine on one skinned zombie (ADR-017-1): per slot
    /// (Helmet / Body / Leg) two SkinnedMeshRenderers per LOD on the <c>Zombie_AP007</c> prefab
    /// (main plate + sub-mesh), re-pointed by bone name to this instance's skeleton.
    /// <see cref="Apply"/> maps the <see cref="IArmorVisual"/> contract to a mesh swap:
    ///   Absent  → the slot's renderers are disabled;
    ///   Present → the variant's Present meshes (kabuto | dō + kote | haidate + suneate);
    ///   Damaged → the variant's Damaged main mesh (one panel / lame / tasset gone, flaked lacquer), same sub-mesh;
    ///   Broken  → the family's remnants (straps / cords) — never nothing, so the lost plate
    ///             still reads, and the region beneath is open (REQ-063 / REQ-084).
    /// Idempotent and allocation-free after construction (state changes happen on every hit).
    /// The break raises <see cref="Broken"/> / <see cref="AnyBroken"/> with the region and the
    /// anchor transform — the S-07 effect hook (no effect in this package).
    /// </summary>
    public sealed class ArmorVisualBinder
    {
        public readonly struct BrokenEvent
        {
            public BrokenEvent(ArmorVisualBinder binder, ArmorSlot slot, BodyRegion region, Transform anchor, string variant)
            {
                Binder = binder;
                Slot = slot;
                Region = region;
                Anchor = anchor;
                Variant = variant;
            }

            public ArmorVisualBinder Binder { get; }
            public ArmorSlot Slot { get; }
            public BodyRegion Region { get; }
            /// <summary>The AP-007 anchor of the slot (anchor_helmet / anchor_torso / anchor_legs), world-posed.</summary>
            public Transform Anchor { get; }
            public string Variant { get; }
        }

        /// <summary>Per-binder break hook.</summary>
        public event Action<BrokenEvent> Broken;

        /// <summary>Global break hook for the S-07 effect layer (one subscription serves every zombie).</summary>
        public static event Action<BrokenEvent> AnyBroken;

        public const int SlotCount = 3;   // helmet, body, leg — index = (int)ArmorSlot - 1

        private readonly ZombieArmorLibrary _library;
        private readonly ZombieSkinnedVisual _visual;
        private readonly ArmorVisualState[] _state = new ArmorVisualState[SlotCount];
        private ArmorLook _look;
        private string _fit;
        private int _materialTier = -1;

        public ArmorVisualBinder(ZombieArmorLibrary library, ZombieSkinnedVisual visual)
        {
            _library = library;
            _visual = visual;
        }

        public ZombieArmorLibrary Library => _library;
        public ArmorLook Look => _look;
        public string Fit => _fit;
        public bool IsReady => _library != null && _library.IsReady && _visual != null && _visual.HasArmorSlots;

        /// <summary>The variants of this spawn and the body fit the meshes must match. Resets every slot to Absent.</summary>
        public void SetLook(in ArmorLook look, string fit)
        {
            _look = look;
            _fit = fit;
            Reset();
        }

        /// <summary>The body fit changed with the garments (it never does — the fit is the seed's first draw — but the binder follows the look, not the assumption).</summary>
        public void SetFit(string fit)
        {
            if (_fit == fit) return;
            _fit = fit;
            for (int i = 0; i < SlotCount; i++)
            {
                if (_state[i] == ArmorVisualState.Absent) continue;
                ZombieArmorLibrary.Variant variant = _library.Find(_look.Of((ArmorSlot)(i + 1)));
                ZombieArmorLibrary.Fit f = variant != null ? variant.FindFit(_fit) : null;
                Show(i, f != null ? f.Of(_state[i]) : null);
            }
        }

        /// <summary>Every slot Absent (renderers off). Pool return, spawn start.</summary>
        public void Reset()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                _state[i] = ArmorVisualState.Absent;
                Show(i, null);
            }
        }

        public ArmorVisualState StateOf(ArmorSlot slot)
        {
            int i = Index(slot);
            return i >= 0 ? _state[i] : ArmorVisualState.Absent;
        }

        public string VariantOf(ArmorSlot slot) => _look.Of(slot);

        /// <summary>IArmorVisual semantics for one slot. Returns true when the shown mesh changed.</summary>
        public bool Apply(ArmorSlot slot, ArmorVisualState state)
        {
            int i = Index(slot);
            if (i < 0 || !IsReady) return false;
            bool changed = _state[i] != state;
            _state[i] = state;
            if (!changed) return false;

            if (state == ArmorVisualState.Absent)
            {
                Show(i, null);
                return true;
            }

            ZombieArmorLibrary.Variant variant = _library.Find(_look.Of(slot));
            ZombieArmorLibrary.Fit fit = variant != null ? variant.FindFit(_fit) : null;
            Show(i, fit != null ? fit.Of(state) : null);

            if (state == ArmorVisualState.Broken)
            {
                // WP-018 (ADR-018-2): the break is the heavy stagger's only trigger on the driven body.
                _visual?.NotifyArmorBroken();
                var e = new BrokenEvent(this, slot, DamageRegions.RegionFor(slot), AnchorOf(slot), variant != null ? variant.id : null);
                Broken?.Invoke(e);
                AnyBroken?.Invoke(e);
            }

            return true;
        }

        /// <summary>The shared armour material variant of an enemy version on every armour slot (no instance material).</summary>
        public void ApplyMaterial(int tierIndex)
        {
            if (_visual == null || _library == null) return;
            Material m = _library.MaterialFor(tierIndex);
            if (m == null) return;
            _visual.ApplyArmorMaterial(m);
            _materialTier = tierIndex;
        }

        public int MaterialTier => _materialTier;

        /// <summary>Slots with an enabled main renderer at LOD 0 (tests / probe).</summary>
        public int ActiveSlotCount()
        {
            int n = 0;
            for (int s = 0; s < SlotCount; s++)
            {
                SkinnedMeshRenderer r = _visual != null ? _visual.ArmorRenderer(0, s, ZombieArmorLibrary.PartMain) : null;
                if (r != null && r.enabled && r.sharedMesh != null) n++;
            }

            return n;
        }

        /// <summary>Enabled armour renderers at LOD 0, both parts (tests / probe: the draw-call cost of the armour).</summary>
        public int ActiveRendererCount()
        {
            int n = 0;
            for (int s = 0; s < SlotCount; s++)
            for (int part = 0; part < ZombieArmorLibrary.PartCount; part++)
            {
                SkinnedMeshRenderer r = _visual != null ? _visual.ArmorRenderer(0, s, part) : null;
                if (r != null && r.enabled && r.sharedMesh != null) n++;
            }

            return n;
        }

        /// <summary>Triangles of the LOD0 armour meshes currently shown (tests / probe).</summary>
        public int ActiveLod0Triangles()
        {
            int n = 0;
            for (int s = 0; s < SlotCount; s++)
            for (int part = 0; part < ZombieArmorLibrary.PartCount; part++)
            {
                SkinnedMeshRenderer r = _visual != null ? _visual.ArmorRenderer(0, s, part) : null;
                if (r != null && r.enabled && r.sharedMesh != null) n += (int)(r.sharedMesh.GetIndexCount(0) / 3);
            }

            return n;
        }

        private Transform AnchorOf(ArmorSlot slot)
        {
            if (_visual == null) return null;
            switch (slot)
            {
                case ArmorSlot.Helmet: return _visual.HelmetAnchor;
                case ArmorSlot.Body: return _visual.TorsoAnchor;
                case ArmorSlot.Leg: return _visual.LegsAnchor;
                default: return null;
            }
        }

        private void Show(int slotIndex, ZombieArmorLibrary.StateMeshes meshes)
        {
            if (_visual == null) return;
            for (int lod = 0; lod < ZombieArmorLibrary.LodCount; lod++)
            for (int part = 0; part < ZombieArmorLibrary.PartCount; part++)
            {
                _visual.BindArmor(lod, slotIndex, part, meshes != null ? meshes.Part(part, lod) : null);
            }
        }

        public static int Index(ArmorSlot slot)
        {
            int i = (int)slot - 1;
            return i >= 0 && i < SlotCount ? i : -1;
        }
    }
}
