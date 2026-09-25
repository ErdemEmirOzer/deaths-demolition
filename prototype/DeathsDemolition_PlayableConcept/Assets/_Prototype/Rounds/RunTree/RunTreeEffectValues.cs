using System;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-077 K10b: the run's tree effects resolved into plain fields, in RUNTIME units (a percent as 0.2), rebuilt
    /// by <see cref="RunTreeState"/> whenever ownership changes (buy, undo, Reforge, a new run). The gameplay hooks
    /// read these fields on their hot paths — no lookup, no allocation. A field at its neutral value (0, 1, false)
    /// means the node is not owned. Numbers come from the catalog args (run-tree.json), the same ones the card shows.
    /// </summary>
    public sealed class RunTreeEffectValues
    {
        // ------------------------------------------------------------------ weapon-lane keystones
        /// <summary>ks.piercing.line: the first N pierced bodies take full damage (no per-body loss).</summary>
        public int PierceFullBodies;
        /// <summary>ks.ricochet.seeker: a ricochet turns to the nearest enemy.</summary>
        public bool RicochetSeek;
        /// <summary>ks.explosion.ashcloud: explosions leave burning ground for <see cref="AshCloudSeconds"/> at this share of the blast per second.</summary>
        public float AshCloudShare;
        public float AshCloudSeconds;
        /// <summary>ks.chain.web: the chain's first jump splits into this many (0 = off).</summary>
        public int ChainFirstSplit;
        /// <summary>ks.firerate.warmup: after <see cref="WarmupSeconds"/> of held fire, fire rate × (1 + this).</summary>
        public float WarmupFireRate;
        public float WarmupSeconds;
        /// <summary>ks.reload.fullmag: a reload from empty fills the magazine this share past full.</summary>
        public float FullMagBonus;
        /// <summary>ks.armorbreak.plate: the first hit on each armour plate deals × this (1 = off).</summary>
        public float PlateFirstHitMultiplier = 1f;
        /// <summary>ks.impact.stagger: staggered targets take this share more damage.</summary>
        public float StaggeredDamageBonus;
        /// <summary>ks.cluster.seedling: extra bomblets.</summary>
        public int ClusterExtraBomblets;

        // ------------------------------------------------------------------ body lane
        /// <summary>ks.body.evade: after a hit, move speed × (1 + this) for <see cref="EvadeSeconds"/>.</summary>
        public float EvadeSpeedBonus;
        public float EvadeSeconds;
        /// <summary>ks.body.thickskin: at full health the next hit deals this share less.</summary>
        public float ThickSkinReduction;
        /// <summary>ks.body.breath: each kill cuts the regeneration wait by these seconds.</summary>
        public float BreathCutSeconds;
        /// <summary>ks.body.bloodstep: each kill restores this much health.</summary>
        public float BloodStepHeal;
        /// <summary>fork.body.ashstep: damage taken while moving × (1 − this).</summary>
        public float MovingDamageReduction;
        /// <summary>fork.body.ironknee: damage taken while standing still × (1 − this).</summary>
        public float StillDamageReduction;
        /// <summary>cap.body.secondbreath: once per run a killing blow leaves <see cref="SecondWindHealth"/> health and <see cref="SecondWindInvulnerableSeconds"/> of invulnerability.</summary>
        public bool SecondWind;
        public float SecondWindHealth;
        public float SecondWindInvulnerableSeconds;

        // ------------------------------------------------------------------ the run weapon's own nodes
        /// <summary>The run weapon's WP-069 slot ("ar_1" …); the per-weapon nodes below belong to it.</summary>
        public string WeaponSlot = string.Empty;
        public bool Seal;
        public bool ForkX;
        public bool ForkY;
        public bool Capstone;

        private RunTreeNode _seal, _forkX, _forkY, _capstone;

        /// <summary>An arg of the run weapon's seal / fork / capstone in DISPLAY units (0 when not owned).</summary>
        public float SealArg(string name, float fallback = 0f) => Seal && _seal != null ? _seal.Arg(name, fallback) : fallback;
        public float ForkXArg(string name, float fallback = 0f) => ForkX && _forkX != null ? _forkX.Arg(name, fallback) : fallback;
        public float ForkYArg(string name, float fallback = 0f) => ForkY && _forkY != null ? _forkY.Arg(name, fallback) : fallback;
        public float CapstoneArg(string name, float fallback = 0f) => Capstone && _capstone != null ? _capstone.Arg(name, fallback) : fallback;

        /// <summary>No tree effect (menus, tests, no run).</summary>
        public static readonly RunTreeEffectValues None = new RunTreeEffectValues();

        internal void Reset(string weaponSlot)
        {
            PierceFullBodies = 0; RicochetSeek = false; AshCloudShare = 0f; AshCloudSeconds = 0f; ChainFirstSplit = 0;
            WarmupFireRate = 0f; WarmupSeconds = 0f; FullMagBonus = 0f; PlateFirstHitMultiplier = 1f; StaggeredDamageBonus = 0f;
            ClusterExtraBomblets = 0;
            EvadeSpeedBonus = 0f; EvadeSeconds = 0f; ThickSkinReduction = 0f; BreathCutSeconds = 0f; BloodStepHeal = 0f;
            MovingDamageReduction = 0f; StillDamageReduction = 0f; SecondWind = false; SecondWindHealth = 0f; SecondWindInvulnerableSeconds = 0f;
            WeaponSlot = weaponSlot ?? string.Empty;
            Seal = ForkX = ForkY = Capstone = false;
            _seal = _forkX = _forkY = _capstone = null;
        }

        /// <summary>Folds one owned node into the values (display args → runtime units).</summary>
        internal void Apply(RunTreeNode n)
        {
            switch (n.EffectId)
            {
                case "ks.piercing.line": PierceFullBodies = (int)n.Arg("count", 2); return;
                case "ks.ricochet.seeker": RicochetSeek = true; return;
                case "ks.explosion.ashcloud": AshCloudSeconds = n.Arg("seconds", 2f); AshCloudShare = n.Arg("value", 25f) / 100f; return;
                case "ks.chain.web": ChainFirstSplit = (int)n.Arg("count", 2); return;
                case "ks.firerate.warmup":
                    WarmupFireRate = n.Arg("value", 20f) / 100f;
                    if (WarmupSeconds <= 0f) WarmupSeconds = n.Arg("seconds", 1.5f);
                    return;
                case "ks.reload.fullmag": FullMagBonus = n.Arg("value", 25f) / 100f; return;
                case "ks.armorbreak.plate": PlateFirstHitMultiplier = Math.Max(1f, n.Arg("value", 2f)); return;
                case "ks.impact.stagger": StaggeredDamageBonus = n.Arg("value", 15f) / 100f; return;
                case "ks.cluster.seedling": ClusterExtraBomblets = (int)n.Arg("count", 1); return;
                case "ks.body.evade": EvadeSpeedBonus = n.Arg("value", 20f) / 100f; EvadeSeconds = n.Arg("seconds", 2f); return;
                case "ks.body.thickskin": ThickSkinReduction = n.Arg("value", 30f) / 100f; return;
                case "ks.body.breath": BreathCutSeconds = n.Arg("value", 0.5f); return;
                case "ks.body.bloodstep": BloodStepHeal = n.Arg("value", 2f); return;
                case "fork.body.ashstep": MovingDamageReduction = n.Arg("value", 15f) / 100f; return;
                case "fork.body.ironknee": StillDamageReduction = n.Arg("value", 20f) / 100f; return;
                case "cap.body.secondbreath":
                    SecondWind = true; SecondWindHealth = n.Arg("value", 1f); SecondWindInvulnerableSeconds = n.Arg("seconds", 2f);
                    return;
            }

            switch (n.Kind)
            {
                case RunTreeNodeKind.WeaponSeal: Seal = true; _seal = n; break;
                case RunTreeNodeKind.Capstone when n.Lane == RunTreeLane.Weapon: Capstone = true; _capstone = n; break;
                case RunTreeNodeKind.Fork when n.Lane == RunTreeLane.Weapon:
                    if (n.Id.EndsWith("_x", StringComparison.Ordinal)) { ForkX = true; _forkX = n; }
                    else { ForkY = true; _forkY = n; }
                    break;
            }

            // smg_1 fork Y moves the Warm-Up threshold (catalog: "Warm-Up starts after {seconds} s").
            if (n.EffectId == "fork.smg_1.y") WarmupSeconds = n.Arg("seconds", 0.8f);
        }
    }
}
