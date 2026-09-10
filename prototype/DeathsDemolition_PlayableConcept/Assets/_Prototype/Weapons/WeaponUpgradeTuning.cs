using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// How an upgrade tier turns into a weapon-side number.
    ///
    /// The central <see cref="PrototypeConfig"/> owns the economy of the branches - cost,
    /// max tier, offer weight - because that is the shop's business. What a tier
    /// actually *does* to a bullet is the weapon's business and lives here.
    ///
    /// It is a ScriptableObject with a code default, so it works with or without an
    /// authored asset: drop a <c>WeaponUpgradeTuning</c> into any Resources folder under
    /// the name in <see cref="ResourcesPath"/> and it takes over; do nothing and the
    /// defaults below are used. No scene reference, no missing reference.
    ///
    /// Every number here is PROTOTYPE-ONLY and invented: the request says only
    /// "tier arttikca hedef sayisi artar" and "sekme ihtimali veya sayisi artar".
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponUpgradeTuning", menuName = "Deaths Demolition/Weapon Upgrade Tuning", order = 10)]
    public sealed class WeaponUpgradeTuning : ScriptableObject
    {
        public const string ResourcesPath = "WeaponUpgradeTuning";

        private static WeaponUpgradeTuning s_active;

        public static WeaponUpgradeTuning Active
        {
            get
            {
                if (s_active != null)
                {
                    return s_active;
                }

                s_active = Resources.Load<WeaponUpgradeTuning>(ResourcesPath);

                if (s_active == null)
                {
                    s_active = CreateInstance<WeaponUpgradeTuning>();
                    s_active.name = "WeaponUpgradeTuning (code defaults)";
                    s_active.hideFlags = HideFlags.HideAndDontSave;
                }

                return s_active;
            }
        }

        // ------------------------------------------------------------------ Piercing

        [Header("Piercing - bullet passes through additional targets")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Extra enemies a bullet passes through per tier.", true)]
        public int PierceTargetsPerTier = 1;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Damage retained after each pass-through. Below 1 so piercing is reach, not raw multiplier.", true)]
        [Range(0.3f, 1f)]
        public float PierceDamageFalloff = 0.82f;

        // ------------------------------------------------------------------ Ricochet

        [Header("Ricochet - bullet bounces to a nearby enemy")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Chance to bounce at tier 1.", true)]
        [Range(0f, 1f)]
        public float RicochetBaseChance = 0.35f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Chance added per extra tier.", true)]
        [Range(0f, 0.5f)]
        public float RicochetChancePerTier = 0.18f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "One extra bounce every N tiers.", true)]
        public int RicochetTiersPerExtraBounce = 2;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Search radius for a bounce target, metres.", true)]
        public float RicochetRadius = 6.5f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Damage of a bounce relative to the original hit.", true)]
        [Range(0.1f, 1f)]
        public float RicochetDamageScale = 0.6f;

        // ------------------------------------------------------------------ Explosion

        [Header("Explosion - certain hits detonate")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Chance a hit detonates at tier 1.", true)]
        [Range(0f, 1f)]
        public float ExplosionBaseChance = 0.14f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Chance added per extra tier.", true)]
        [Range(0f, 0.5f)]
        public float ExplosionChancePerTier = 0.10f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Blast radius at tier 1, metres. 'Small explosion'.", true)]
        public float ExplosionBaseRadius = 2.2f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Extra radius per tier, metres.", true)]
        public float ExplosionRadiusPerTier = 0.5f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Blast damage as a multiple of the hit that triggered it, at tier 1.", true)]
        public float ExplosionBaseDamageScale = 1.1f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Extra blast damage multiple per tier.", true)]
        public float ExplosionDamageScalePerTier = 0.45f;

        // ------------------------------------------------------------------ Chain

        [Header("Chain - damage jumps to nearby targets")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Additional targets hit per tier.", true)]
        public int ChainTargetsPerTier = 1;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Jump distance, metres.", true)]
        public float ChainRadius = 5.0f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Damage of the first jump relative to the original hit.", true)]
        [Range(0.1f, 1f)]
        public float ChainDamageScale = 0.5f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Damage retained on each further jump.", true)]
        [Range(0.3f, 1f)]
        public float ChainDamageFalloff = 0.75f;

        // ------------------------------------------------------------------ Utility

        [Header("Utility branches applied on the weapon side")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Fire rate added per tier, as a fraction of base.", true)]
        [Range(0f, 0.5f)]
        public float FireRateBonusPerTier = 0.08f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Reload time removed per tier, as a fraction of base.", true)]
        [Range(0f, 0.5f)]
        public float ReloadSpeedBonusPerTier = 0.10f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Floor on reload time after all tiers, as a fraction of base. Stops a build reaching zero.", true)]
        [Range(0.1f, 1f)]
        public float MinimumReloadFraction = 0.45f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Ceiling on total fire-rate multiplier. Stops a build outrunning the pool budget.", true)]
        public float MaximumFireRateMultiplier = 2.2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active = null;
        }
    }
}
