using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>Where a version's silhouette piece hangs (AP-007 anchors; AP-014 delivers the mesh, until then a placeholder primitive).</summary>
    public enum TierSilhouetteAnchor
    {
        None = 0,
        Head = 1,       // anchor_helmet
        Shoulder = 2,   // anchor_torso, offset to the side
        Back = 3,       // anchor_torso, offset behind
    }

    /// <summary>
    /// WP-028 (ADR-028-1) · The B / B / B version layer chosen by the user on 2026-09-16 (WP-027
    /// proposal): what a version ADDS on top of its DEC-021 stat profile without touching the
    /// DEC-021 table, the damage / armour rules or the combat contract (DEC-029). One profile
    /// per tier; the shipped defaults live in code (<see cref="TierBehaviourProfileSet.CreateDefault"/>)
    /// and an optional Resources asset overrides them for tuning. Every number here is
    /// PROTOTYPE-ONLY: the user picked the shape (B), not the values.
    ///
    /// * Special (B): the existing Lunge, with the wind-up shortened and the recovery lengthened
    ///   (timer scales; the animator consumes the same scales as playback rates), plus one
    ///   silhouette piece.
    /// * Elite (B): the body plate (dō) tends to be present - a per-slot chance FLOOR / multiplier
    ///   applied on top of <c>PrototypeConfig.ArmorChance</c> (the DEC-021 value is read, never
    ///   written), plus 1-2 silhouette pieces.
    /// * Ultra (B): body fit C forced, all three armour regions tend to be present, one
    ///   silhouette piece; the attack animation is the sweep clip played phase-wise at the
    ///   Ultra timer's rates until AP-014 delivers an overhead clip (DEC-029 windows are the
    ///   timer's, unchanged).
    /// </summary>
    [Serializable]
    public sealed class TierBehaviourProfile
    {
        public EnemyTier Tier = EnemyTier.Standard;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Attack wind-up timer multiplier (Special B: shorter wind-up). 1 = the DEC-021 tier value.", true)]
        [Range(0.25f, 2f)] public float TelegraphScale = 1f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Attack recovery timer multiplier (Special B: longer punish window). 1 = the DEC-021 tier value.", true)]
        [Range(0.5f, 3f)] public float RecoveryScale = 1f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Move speed multiplier on top of the tier value. 1 = unchanged.", true)]
        [Range(0.5f, 2f)] public float SpeedScale = 1f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Armour tendency: per-slot multiplier on ArmorChance (Helmet, Body, Leg). 1 = the DEC-021 odds.", true)]
        public float HelmetChanceMultiplier = 1f, BodyChanceMultiplier = 1f, LegChanceMultiplier = 1f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Armour tendency: per-slot minimum equip chance (Helmet, Body, Leg). 0 = no floor.", true)]
        [Range(0f, 1f)] public float HelmetChanceFloor = 0f;
        [Range(0f, 1f)] public float BodyChanceFloor = 0f;
        [Range(0f, 1f)] public float LegChanceFloor = 0f;

        [PrototypeOnly(PendingDecisions.None, "AP-007 body fit forced for this version ('' = the spawn seed's fit; Ultra B = 'C').")]
        public string ForcedFit = "";

        [PrototypeOnly(PendingDecisions.None, "Silhouette piece anchor (AP-014 mesh; placeholder primitive until then).")]
        public TierSilhouetteAnchor SilhouetteAnchor = TierSilhouetteAnchor.None;

        [PrototypeOnly(PendingDecisions.None, "Placeholder silhouette piece size (m) and local offset from the anchor.", true)]
        public Vector3 SilhouetteSize = new Vector3(0.2f, 0.2f, 0.2f);
        public Vector3 SilhouetteOffset = Vector3.zero;

        [PrototypeOnly(PendingDecisions.None, "Version tint (lacquer channel Mask.R / placeholder piece). Outside the style-guide reserves (gold, beacon, warning).", true)]
        public Color Tint = Color.white;

        [PrototypeOnly(PendingDecisions.None, "Attack clip used until the version's own clip exists (Ultra B: the sweep clip, phase-scaled to the Ultra timer).")]
        public string AttackClipPlaceholder = "";

        public bool HasSilhouette => SilhouetteAnchor != TierSilhouetteAnchor.None;
        public bool ForcesFit => !string.IsNullOrEmpty(ForcedFit);
        public bool HasAttackPlaceholder => !string.IsNullOrEmpty(AttackClipPlaceholder);

        /// <summary>The tier's equip chance for <paramref name="slot"/> after the tendency: max(base × multiplier, floor), clamped to [0, 1]. Base = the DEC-021 value, unchanged.</summary>
        public float ArmourChance(ArmorSlot slot, float baseChance)
        {
            float mul, floor;
            switch (slot)
            {
                case ArmorSlot.Helmet: mul = HelmetChanceMultiplier; floor = HelmetChanceFloor; break;
                case ArmorSlot.Body: mul = BodyChanceMultiplier; floor = BodyChanceFloor; break;
                case ArmorSlot.Leg: mul = LegChanceMultiplier; floor = LegChanceFloor; break;
                default: return Mathf.Clamp01(baseChance);
            }

            if (baseChance <= 0f && floor <= 0f) return 0f;   // a slot the table gives 0 stays 0 unless the version floors it
            return Mathf.Clamp01(Mathf.Max(baseChance * Mathf.Max(0f, mul), Mathf.Clamp01(floor)));
        }

        public bool IsIdentity => Mathf.Approximately(TelegraphScale, 1f) && Mathf.Approximately(RecoveryScale, 1f) && Mathf.Approximately(SpeedScale, 1f) &&
                                  Mathf.Approximately(HelmetChanceMultiplier, 1f) && Mathf.Approximately(BodyChanceMultiplier, 1f) && Mathf.Approximately(LegChanceMultiplier, 1f) &&
                                  HelmetChanceFloor <= 0f && BodyChanceFloor <= 0f && LegChanceFloor <= 0f && !ForcesFit && !HasSilhouette && !HasAttackPlaceholder;

        public TierBehaviourProfile Clone() => (TierBehaviourProfile)MemberwiseClone();
    }

    /// <summary>The four profiles. <see cref="Active"/> = the Resources asset when one exists, else the code defaults.</summary>
    public sealed class TierBehaviourProfileSet : ScriptableObject
    {
        public const string ResourcesPath = "DD_TierProfiles";

        [SerializeField] private TierBehaviourProfile[] _profiles = new TierBehaviourProfile[0];

        private static TierBehaviourProfileSet s_active;
        private static readonly TierBehaviourProfile s_identity = new TierBehaviourProfile();

        public static TierBehaviourProfileSet Active
        {
            get
            {
                if (s_active == null)
                {
                    s_active = Resources.Load<TierBehaviourProfileSet>(ResourcesPath);
                    if (s_active == null)
                    {
                        s_active = CreateDefault();
                        s_active.hideFlags = HideFlags.HideAndDontSave;
                    }
                }

                return s_active;
            }
        }

        /// <summary>Test / probe harness: use this set until <see cref="ResetStatics"/>; null = back to the asset / defaults.</summary>
        public static void SetActive(TierBehaviourProfileSet set) => s_active = set;

        public static void ResetStatics() => s_active = null;

        public TierBehaviourProfile[] Profiles => _profiles;

        /// <summary>The tier's profile; an identity profile (scales 1, no floors, no piece) when the set has none for it.</summary>
        public TierBehaviourProfile For(EnemyTier tier)
        {
            for (int i = 0; i < _profiles.Length; i++)
            {
                if (_profiles[i] != null && _profiles[i].Tier == tier) return _profiles[i];
            }

            return s_identity;
        }

        public void Set(TierBehaviourProfile[] profiles) => _profiles = profiles ?? new TierBehaviourProfile[0];

        /// <summary>The shipped defaults (B / B / B). PROTOTYPE-ONLY numbers - the user picked the shapes, not the values.</summary>
        public static TierBehaviourProfileSet CreateDefault()
        {
            var set = CreateInstance<TierBehaviourProfileSet>();
            set.name = "DD_TierProfiles (code defaults)";
            set._profiles = new[]
            {
                new TierBehaviourProfile { Tier = EnemyTier.Standard },
                new TierBehaviourProfile
                {
                    Tier = EnemyTier.Special,
                    TelegraphScale = 0.85f, RecoveryScale = 1.25f,                       // short wind-up / long recovery emphasis (B)
                    SilhouetteAnchor = TierSilhouetteAnchor.Back, SilhouetteSize = new Vector3(0.12f, 0.34f, 0.08f), SilhouetteOffset = new Vector3(0f, 0.05f, -0.16f),
                    Tint = new Color(0.72f, 0.75f, 0.78f),                                // ash band (concept note: ~200°, s ≤ 0.12)
                },
                new TierBehaviourProfile
                {
                    Tier = EnemyTier.Elite,
                    BodyChanceFloor = 0.85f, BodyChanceMultiplier = 1.5f,               // dō tends to be present (B)
                    SilhouetteAnchor = TierSilhouetteAnchor.Shoulder, SilhouetteSize = new Vector3(0.22f, 0.10f, 0.20f), SilhouetteOffset = new Vector3(0.24f, 0.18f, 0f),
                    Tint = new Color(0.46f, 0.22f, 0.40f),                                // plum lacquer (concept note: ~300°)
                },
                new TierBehaviourProfile
                {
                    Tier = EnemyTier.Ultra,
                    ForcedFit = "C",
                    HelmetChanceFloor = 0.97f, BodyChanceFloor = 0.97f, LegChanceFloor = 0.97f,   // three regions tend to be present (≈ 91 % all three)
                    SilhouetteAnchor = TierSilhouetteAnchor.Head, SilhouetteSize = new Vector3(0.30f, 0.14f, 0.30f), SilhouetteOffset = new Vector3(0f, 0.12f, 0f),
                    Tint = new Color(0.16f, 0.13f, 0.12f),                                // soot, darkest band (concept note) + rust lacquer
                    AttackClipPlaceholder = "AP009_zombie_attack_sweep",
                },
            };
            return set;
        }
    }
}
