using UnityEngine;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// Dresses one zombie and keeps its armour readable - REQ-060, REQ-062, REQ-063.
    ///
    /// Two jobs:
    ///   1. Spawn-time variation. Every zombie is the SAME family (REQ-060): same body,
    ///      same proportions, differing only in cloth tone, sash, silhouette lean and
    ///      per-tier scale. No supernatural aura, no emissive, per the request.
    ///   2. Live armour readout. Plates are shed one at a time as durability falls, so a
    ///      player can see how close a piece is to breaking, and the under-cloth is exposed
    ///      the moment it does break.
    ///
    /// All state changes go through shared materials and SetActive. Nothing here allocates
    /// after the first call, and nothing instantiates a material.
    /// </summary>
    public sealed class ZombieAppearance : IArmorVisual, IEnemyVersionVisual
    {
        private EnemyTier _appliedVersion = EnemyTier.Standard;

        /// <summary>IEnemyVersionVisual: the version last applied by <see cref="ApplyVersion"/>.</summary>
        public EnemyTier AppliedVersion => _appliedVersion;

        /// <summary>Back to "no version applied" (Standard) - called when the body returns to the pool, so a readout never sees a stale version.</summary>
        public void ResetVersion()
        {
            _appliedVersion = EnemyTier.Standard;
        }

        /// <summary>
        /// IEnemyVersionVisual (WP-010): the placeholder look of a version - cloth tones,
        /// sash, per-tier silhouette (scale / lean), plates hidden. Exactly the WP-002
        /// <see cref="ApplyVariation"/> behaviour (parity); the final art replaces the body
        /// of this method, not its callers.
        /// </summary>
        public void ApplyVersion(EnemyTier tier, ref EnemyRandom random)
        {
            _appliedVersion = tier;
            ApplyVariation(tier, ref random);
        }

        // ----------------------------------------------------- per-tier silhouette
        // NOT cosmetic. Scale is written to rig.Visual, which is the PARENT of the three
        // region hitboxes, so every number here changes how large a target the tier is and
        // therefore how hard it is to hit. The request gives adjectives only - "forward
        // leaning" Runner, "wider" Guard, "large and easily recognised" Executioner - so
        // every magnitude below is invented.

        [PrototypeOnly(PendingDecisions.None, "Runner body scale. Scales its hitboxes too.", true)]
        private const float RunnerScale = 0.96f;

        [PrototypeOnly(PendingDecisions.None, "Runner shoulder width factor.", true)]
        private const float RunnerShoulderWiden = 0.92f;

        [PrototypeOnly(PendingDecisions.None, "Runner resting forward lean, degrees.", true)]
        private const float RunnerLeanDegrees = 22f;

        [PrototypeOnly(PendingDecisions.None, "Guard body scale. Scales its hitboxes too.", true)]
        private const float GuardScale = 1.16f;

        [PrototypeOnly(PendingDecisions.None, "Guard shoulder width factor - the 'wider silhouette'.", true)]
        private const float GuardShoulderWiden = 1.30f;

        [PrototypeOnly(PendingDecisions.None, "Guard resting forward lean, degrees.", true)]
        private const float GuardLeanDegrees = 6f;

        [PrototypeOnly(PendingDecisions.None,
            "Executioner body scale. Scales its hitboxes too - this is why an Ultra is both " +
            "unmistakable and easier to hit.", true)]
        private const float ExecutionerScale = 1.48f;

        [PrototypeOnly(PendingDecisions.None, "Executioner shoulder width factor.", true)]
        private const float ExecutionerShoulderWiden = 1.42f;

        [PrototypeOnly(PendingDecisions.None, "Executioner resting forward lean, degrees.", true)]
        private const float ExecutionerLeanDegrees = 4f;

        [PrototypeOnly(PendingDecisions.None, "Standard body scale. The 1.0 every other tier is relative to.", true)]
        private const float StandardScale = 1f;

        [PrototypeOnly(PendingDecisions.None, "Standard shoulder width factor.", true)]
        private const float StandardShoulderWiden = 1f;

        [PrototypeOnly(PendingDecisions.None, "Standard resting forward lean, degrees.", true)]
        private const float StandardLeanDegrees = 10f;

        [PrototypeOnly(PendingDecisions.None,
            "Lower bound of the per-instance scale jitter, so a crowd of Standards is not a " +
            "clone army. Also jitters hitbox size.", true)]
        private const float ScaleJitterMin = 0.94f;

        [PrototypeOnly(PendingDecisions.None, "Upper bound of the per-instance scale jitter.", true)]
        private const float ScaleJitterMax = 1.06f;

        [PrototypeOnly(PendingDecisions.None, "Lower bound of the per-instance lean jitter, degrees.", true)]
        private const float LeanJitterMinDegrees = -4f;

        [PrototypeOnly(PendingDecisions.None, "Upper bound of the per-instance lean jitter, degrees.", true)]
        private const float LeanJitterMaxDegrees = 6f;

        private readonly ZombieRig _rig;

        // WP-016 (ADR-016-2): the AP-007 skinned look. Null = the M1 block body.
        private ZombieSkinnedVisual _skinned;
        private ZombieModuleLibrary _library;
        private ZombieLook _look;
        private uint _lookSeed;

        public ZombieAppearance(ZombieRig rig)
        {
            _rig = rig;
        }

        /// <summary>WP-016: bind the AP-007 visual and its module library (template build); the look is chosen per spawn from <see cref="LookSeed"/>.</summary>
        public void SetSkinned(ZombieSkinnedVisual skinned, ZombieModuleLibrary library)
        {
            _skinned = skinned;
            _library = library;
        }

        public bool IsSkinned => _skinned != null && _library != null;

        /// <summary>The spawn seed the look is derived from (set by ZombieEnemy.Initialise before ApplyVersion).</summary>
        public uint LookSeed
        {
            get => _lookSeed;
            set => _lookSeed = value;
        }

        /// <summary>The assembled look (skinned only).</summary>
        public ZombieLook Look => _look;

        /// <summary>
        /// Apply a fresh look. Called from OnSpawned, before the first frame is drawn.
        ///
        /// PROTOTYPE-ONLY / INVENTED: tone weighting, lean angles and tier scales. The
        /// request specifies the palette and the silhouette adjectives ("forward-leaning"
        /// Runner, "wider" Guard, "large and easily recognised" Ultra) but no numbers.
        /// </summary>
        public void ApplyVariation(EnemyTier tier, ref EnemyRandom random)
        {
            if (_rig == null || _rig.Visual == null)
            {
                return;
            }

            if (IsSkinned)
            {
                // WP-016: fit + top + bottom + head + colourways from the spawn seed
                // (deterministic, ratio-ruled); the version picks the shared material variant
                // (cloth-masked tint) — no per-instance material, no emissive (DEC-067).
                _look = ZombieLookSelector.Select(_library, _lookSeed);
                _skinned.Assemble(_library, _look);
                _skinned.ApplyMaterial(_library, (int)tier);
                ApplySilhouette(tier, ref random);
                HideAllArmor();
                return;
            }

            // ------------------------------------------------------------- cloth layers
            // Dark indigo and black dominate; dirty cream is the accent, exactly as the
            // palette line asks. Two different tones per zombie so the kimono reads as
            // layered rather than as one painted blob.
            int primary = PickTone(ref random);
            int secondary = PickTone(ref random);
            if (secondary == primary)
            {
                secondary = (secondary + 1) % ZombiePalette.ClothToneCount;
            }

            Material primaryCloth = ZombiePalette.Cloth(primary);
            Material secondaryCloth = ZombiePalette.Cloth(secondary);

            SetMaterial(_rig.TorsoBlock, primaryCloth);
            SetMaterial(_rig.PelvisBlock, secondaryCloth);
            SetMaterial(_rig.SkirtBlock, secondaryCloth);
            SetMaterial(_rig.HeadBlock, ZombiePalette.Of(ZombiePalette.Accent.Flesh));

            // Sleeves: some zombies wear both, some one, some none - the "different arm
            // and sash variations" line. A bare arm uses flesh.
            Material flesh = ZombiePalette.Of(ZombiePalette.Accent.Flesh);
            bool sleeveL = random.Chance(0.72f);
            bool sleeveR = random.Chance(0.72f);
            SetLimb(0, sleeveL ? primaryCloth : flesh);
            SetLimb(1, sleeveR ? primaryCloth : flesh);
            SetLimb(2, random.Chance(0.35f) ? secondaryCloth : flesh);
            SetLimb(3, random.Chance(0.35f) ? secondaryCloth : flesh);
            SetLimb(4, primaryCloth);
            SetLimb(5, primaryCloth);
            SetLimb(6, random.Chance(0.5f) ? secondaryCloth : primaryCloth);
            SetLimb(7, random.Chance(0.5f) ? secondaryCloth : primaryCloth);

            // ---------------------------------------------------------------- sash + tier
            Material sash = tier == EnemyTier.Special
                ? ZombiePalette.Of(ZombiePalette.Accent.SashRed)   // request: Runner wears a red/dark sash
                : random.Chance(0.25f)
                    ? ZombiePalette.Of(ZombiePalette.Accent.SashRed)
                    : ZombiePalette.Of(ZombiePalette.Accent.SashDark);
            SetMaterial(_rig.SashBlock, sash);

            ApplySilhouette(tier, ref random);
            HideAllArmor();
        }

        private static int PickTone(ref EnemyRandom random)
        {
            // Weighted so the crowd reads as dark indigo / black with dirty cream accents.
            float roll = random.Next01();
            if (roll < 0.34f) return (int)ZombiePalette.ClothTone.DarkIndigo;
            if (roll < 0.62f) return (int)ZombiePalette.ClothTone.Black;
            if (roll < 0.80f) return (int)ZombiePalette.ClothTone.DirtyCream;
            if (roll < 0.92f) return (int)ZombiePalette.ClothTone.FadedBrown;
            return (int)ZombiePalette.ClothTone.WashedSlate;
        }

        /// <summary>Per-tier body shape. Scale is on the Visual node, so hitboxes scale with it.</summary>
        private void ApplySilhouette(EnemyTier tier, ref EnemyRandom random)
        {
            float scale;
            float shoulderWiden;
            float lean;

            switch (tier)
            {
                case EnemyTier.Special:   // Runner - lighter, pitched forward
                    scale = RunnerScale;
                    shoulderWiden = RunnerShoulderWiden;
                    lean = RunnerLeanDegrees;
                    break;
                case EnemyTier.Elite:     // Guard - heavier body, wider silhouette
                    scale = GuardScale;
                    shoulderWiden = GuardShoulderWiden;
                    lean = GuardLeanDegrees;
                    break;
                case EnemyTier.Ultra:     // Executioner - large, slow, unmistakable
                    scale = ExecutionerScale;
                    shoulderWiden = ExecutionerShoulderWiden;
                    lean = ExecutionerLeanDegrees;
                    break;
                default:                  // Standard
                    scale = StandardScale;
                    shoulderWiden = StandardShoulderWiden;
                    lean = StandardLeanDegrees;
                    break;
            }

            // A little per-instance noise so a crowd of Standards is not a clone army.
            scale *= random.Range(ScaleJitterMin, ScaleJitterMax);
            lean += random.Range(LeanJitterMinDegrees, LeanJitterMaxDegrees);

            _rig.Visual.localScale = new Vector3(scale, scale, scale);
            BaseLeanDegrees = lean;

            if (_rig.ShoulderL != null && _rig.ShoulderR != null)
            {
                Vector3 l = _rig.RestPosition(ZombieRig.JointShoulderL);
                Vector3 r = _rig.RestPosition(ZombieRig.JointShoulderR);
                _rig.ShoulderL.localPosition = new Vector3(l.x * shoulderWiden, l.y, l.z);
                _rig.ShoulderR.localPosition = new Vector3(r.x * shoulderWiden, r.y, r.z);
            }

            // Widen the torso BLOCK, not the Chest joint. The chest is the parent of the
            // upper-body and head hitboxes, and a non-uniformly scaled joint would quietly
            // deform both of them - the silhouette must never change where the bullets land.
            if (_rig.TorsoBlock != null)
            {
                if (!_torsoBaseScaleCaptured)
                {
                    _torsoBaseScale = _rig.TorsoBlock.transform.localScale;
                    _torsoBaseScaleCaptured = true;
                }

                float widen = Mathf.Lerp(1f, shoulderWiden, 0.6f);
                _rig.TorsoBlock.transform.localScale = new Vector3(
                    _torsoBaseScale.x * widen, _torsoBaseScale.y, _torsoBaseScale.z * widen);
            }
        }

        private Vector3 _torsoBaseScale = Vector3.one;
        private bool _torsoBaseScaleCaptured;

        /// <summary>Resting forward lean in degrees, produced by the tier silhouette.</summary>
        public float BaseLeanDegrees { get; private set; } = StandardLeanDegrees;

        // -------------------------------------------------------------------- armour

        // Indexed by (int)ArmorSlot; index 0 (None) unused. The contract's StateOf answers
        // from here - what was last applied - never from the scene.
        private readonly ArmorVisualState[] _armorState = new ArmorVisualState[4];

        /// <summary>
        /// The rig's three attachment anchors (REQ-115 kabuto / dō / haidate). WP-016: with the
        /// AP-007 body these are the model's <c>anchor_helmet / anchor_torso / anchor_legs</c>
        /// empties (Head / Chest / Hips bone children, identity orientation) — the AP-008
        /// armour parents there (WP-017); the M1 placeholder roots already hang under them.
        /// </summary>
        public ArmorAnchors Anchors => IsSkinned && _skinned.HelmetAnchor != null
            ? new ArmorAnchors(_skinned.HelmetAnchor, _skinned.TorsoAnchor, _skinned.LegsAnchor)
            : new ArmorAnchors(
                _rig.HelmetRoot != null ? _rig.HelmetRoot.transform : null,
                _rig.BodyArmorRoot != null ? _rig.BodyArmorRoot.transform : null,
                _rig.LegArmorRoot != null ? _rig.LegArmorRoot.transform : null);

        /// <summary>IArmorVisual: the state machine over the four plate methods below. Idempotent, allocation-free.</summary>
        public void Apply(ArmorSlot slot, ArmorVisualState state, float ratio01)
        {
            int i = (int)slot;
            if (i <= 0 || i >= _armorState.Length)
            {
                return;
            }

            switch (state)
            {
                case ArmorVisualState.Absent:
                    SetPieces(PiecesFor(slot), 0);
                    break;
                case ArmorVisualState.Present:
                    // AP-007 rule: the mino cape never sits under a dō — swap the top first.
                    if (slot == ArmorSlot.Body && IsSkinned && _look.top == ZombieModuleLibrary.TopMinoCape)
                    {
                        _look = ZombieLookSelector.WithoutMinoCape(_library, _look, _lookSeed);
                        _skinned.Assemble(_library, _look);
                    }

                    EquipArmor(slot);
                    break;
                case ArmorVisualState.Damaged:
                    RefreshArmorDamage(slot, ratio01);
                    break;
                case ArmorVisualState.Broken:
                    if (_armorState[i] != ArmorVisualState.Broken)
                    {
                        BreakArmor(slot);
                    }
                    break;
            }

            _armorState[i] = state;
        }

        public ArmorVisualState StateOf(ArmorSlot slot)
        {
            int i = (int)slot;
            return i > 0 && i < _armorState.Length ? _armorState[i] : ArmorVisualState.Absent;
        }

        public void HideAllArmor()
        {
            SetPieces(_rig.HelmetPieces, 0);
            SetPieces(_rig.BodyArmorPieces, 0);
            SetPieces(_rig.LegArmorPieces, 0);

            for (int i = 0; i < _armorState.Length; i++)
            {
                _armorState[i] = ArmorVisualState.Absent;
            }
        }

        /// <summary>Show a full plate group. Called when a piece is equipped at spawn.</summary>
        public void EquipArmor(ArmorSlot slot)
        {
            GameObject[] pieces = PiecesFor(slot);
            SetPieces(pieces, pieces != null ? pieces.Length : 0);
        }

        /// <summary>
        /// Reflect current durability. 4 plates map to 4 visible steps, so the player sees
        /// pieces flying off well before the break: full -> 3 plates -> 2 -> 1 -> gone.
        /// Returns how many plates were removed by this call, so the caller can spawn
        /// exactly that many shard bursts.
        /// </summary>
        public int RefreshArmorDamage(ArmorSlot slot, float ratio01)
        {
            GameObject[] pieces = PiecesFor(slot);
            if (pieces == null || pieces.Length == 0)
            {
                return 0;
            }

            int before = CountActive(pieces);
            int target = ratio01 <= 0f ? 0 : Mathf.Max(1, Mathf.CeilToInt(ratio01 * pieces.Length));
            SetPieces(pieces, target);
            return Mathf.Max(0, before - target);
        }

        /// <summary>
        /// A plate group broke. Every remaining plate comes off and the under-cloth beneath
        /// it is revealed - the request's "lower cloth becomes visible" beat (REQ-063).
        /// </summary>
        public void BreakArmor(ArmorSlot slot)
        {
            SetPieces(PiecesFor(slot), 0);

            Material under = ZombiePalette.Of(ZombiePalette.Accent.UnderCloth);
            switch (slot)
            {
                case ArmorSlot.Helmet:
                    SetMaterial(_rig.HeadBlock, ZombiePalette.Of(ZombiePalette.Accent.Flesh));
                    break;
                case ArmorSlot.Body:
                    SetMaterial(_rig.TorsoBlock, under);
                    break;
                case ArmorSlot.Leg:
                    SetMaterial(_rig.SkirtBlock, under);
                    SetLimb(4, under);
                    SetLimb(5, under);
                    break;
            }
        }

        private GameObject[] PiecesFor(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return _rig.HelmetPieces;
                case ArmorSlot.Body: return _rig.BodyArmorPieces;
                case ArmorSlot.Leg: return _rig.LegArmorPieces;
                default: return null;
            }
        }

        private static int CountActive(GameObject[] pieces)
        {
            int count = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] != null && pieces[i].activeSelf)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Keep the first <paramref name="activeCount"/> plates, hide the rest.</summary>
        private static void SetPieces(GameObject[] pieces, int activeCount)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] == null)
                {
                    continue;
                }

                bool shouldBeActive = i < activeCount;
                if (pieces[i].activeSelf != shouldBeActive)
                {
                    pieces[i].SetActive(shouldBeActive);
                }
            }
        }

        private void SetLimb(int index, Material material)
        {
            if (_rig.LimbBlocks == null || index < 0 || index >= _rig.LimbBlocks.Length)
            {
                return;
            }

            SetMaterial(_rig.LimbBlocks[index], material);
        }

        private static void SetMaterial(Renderer renderer, Material material)
        {
            if (renderer != null && material != null && renderer.sharedMaterial != material)
            {
                renderer.sharedMaterial = material;
            }
        }
    }
}
