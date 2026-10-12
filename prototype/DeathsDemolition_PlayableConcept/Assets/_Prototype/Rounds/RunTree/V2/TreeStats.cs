namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B0 · the run tree's effects resolved for the weapon in hand: flat numbers only, so a hook is one field read.
    /// <see cref="Neutral"/> is "no tree" (×1, +0, off): every hook returns the shipped value for it. Same-branch tiers replace
    /// each other, different branches multiply (design K5); B1 writes the result through <see cref="TreeRuntime.Publish"/>.
    /// Damage of elements and chaos is a share of <see cref="WeaponPower"/> (SG, design K2) so every weapon class gains the same ratio.
    /// Owners of the reads: Shot / Magazine / Speed → S-02 fire path; Element / Chaos → S-04 systems + S-03 receivers;
    /// Survival → player health / motor; Economy → gold + WP-107 wall buy.
    /// </summary>
    public struct TreeStats
    {
        // ---------------------------------------------------------------- weapon in hand
        /// <summary>SG: base DPS of the weapon in hand × Shot/Speed/crit scales (elements and chaos excluded, no recursion).</summary>
        public float WeaponPower;

        // ---------------------------------------------------------------- Shot
        public float DamageScale;
        /// <summary>Bullet size (hit box and trace).</summary>
        public float ProjectileScale;
        /// <summary>Extra shots per pull (≤ <see cref="TreeGuardrails.ExtraShotsMax"/>); the whole shot incl. pellets repeats (K4).</summary>
        public int ExtraShots;
        /// <summary>Damage share of each extra shot (0.60 = 60 %).</summary>
        public float ExtraShotDamage;
        /// <summary>Spread of the extra shots, total arc in degrees.</summary>
        public float FanArcDegrees;
        public int PierceCount;
        /// <summary>Damage kept per pierced body (1 = no loss).</summary>
        public float PierceKeep;
        public float PierceStaggerSeconds;
        public int RicochetBounces;
        public float RicochetRadius;
        public float RicochetKeep;
        /// <summary>Homing cone of extra shots and bounces (0 = off).</summary>
        public float SeekDegrees;
        /// <summary>Explosive Rounds blast radius (0 = off).</summary>
        public float ExplosiveRadius;
        /// <summary>Share of the shot's damage dealt as area damage.</summary>
        public float ExplosiveAreaShare;
        /// <summary>Weapons that already explode (rl_1) scale their own blast radius instead.</summary>
        public float OwnBlastRadiusScale;

        // ---------------------------------------------------------------- Magazine
        public float MagazineScale;
        public float ReloadScale;
        /// <summary>Quick Hands III: no movement penalty while reloading.</summary>
        public bool ReloadMoveFree;
        /// <summary>Auto-Loader: seconds without firing before the magazine refills by itself (0 = off).</summary>
        public float AutoReloadIdleSeconds;
        /// <summary>Auto-Loader: share of the magazine refilled per second.</summary>
        public float AutoReloadPerSecond;
        /// <summary>Bottomless Drum: the last share of the magazine hits harder.</summary>
        public float MagazineTailShare;
        public float MagazineTailDamage;
        /// <summary>Endless Window: kills that open the no-reload window (0 = off).</summary>
        public int EndlessKills;
        public float EndlessSeconds;
        public float EndlessFireRateScale;

        // ---------------------------------------------------------------- Speed
        public float FireRateScale;
        public float MoveScale;
        /// <summary>Scale of the firing movement penalty (0.5 = half the slowdown).</summary>
        public float FiringMovePenaltyScale;
        public float KillSpeedBoost;
        public float KillSpeedBoostSeconds;
        /// <summary>Full-Auto Conversion: non-automatic weapons fire automatically.</summary>
        public bool FullAuto;
        /// <summary>Full-Auto: rpm scale for the weapon in hand (class table, design §2.3; rl_1 20 → 200).</summary>
        public float FullAutoRpmScale;
        public float FullAutoDamageScale;
        /// <summary>Full-Auto: magazine size for the weapon in hand (0 = unchanged; rl_1 12-rocket box).</summary>
        public int FullAutoMagazine;
        public bool TwinBarrel;
        /// <summary>Twin Barrel: every Nth round does not use ammo (0 = off).</summary>
        public int TwinFreeEvery;
        /// <summary>Overclock: rpm step per 0.5 s of sustained fire (0 = off).</summary>
        public float OverclockStep;
        public float OverclockMax;

        // ---------------------------------------------------------------- Element
        public TreeElement Elements;
        /// <summary>Elemental Fusion: both elements apply on every hit, element damage scale.</summary>
        public bool Fusion;
        public float ElementDamageScale;
        // fire
        public float FireDotShare;
        public float FireSeconds;
        public float FireGroundRadius;
        public float FireGroundSeconds;
        public float FireSpreadRadius;
        public float FireVulnerability;
        // ice
        public float IceSlowPerStack;
        public int IceMaxStacks;
        public int IceStacksPerHit;
        public float IceSeconds;
        public float FreezeSeconds;
        public float FreezeCooldown;
        public float FreezeVulnerability;
        public float ShatterRadius;
        public float ShatterShare;
        // shock
        public int ShockJumps;
        public float ShockShare;
        public float ShockEventsMax;
        public float ShockStaggerSeconds;
        // venom
        public float VenomStackShare;
        public int VenomMaxStacks;
        public int VenomStacksPerHit;
        public float VenomSeconds;
        public float VenomCloudRadius;
        public float VenomCloudSeconds;
        public int VenomArmorIgnoreStacks;
        public float VenomVulnerability;
        // nova (Primal Cataclysm)
        public float NovaCooldown;
        public float NovaRadius;
        public float NovaShare;

        // ---------------------------------------------------------------- Chaos
        public float CritChance;
        public float CritMultiplier;
        /// <summary>Bad-luck guard: after this many crit-less pulls the next is a crit (0 = off).</summary>
        public int CritPity;
        public float CritRingRadius;
        public float CritRingShare;
        public float ChainChance;
        public float ChainRadius;
        /// <summary>Share of the victim's max health dealt by its death blast.</summary>
        public float ChainHealthShare;
        /// <summary>Chance falloff per generation (0.5 = halves).</summary>
        public float ChainFalloff;
        public TreeChaosCore Core;
        /// <summary>Mini Nuke: N = k × pulls per second (smg 150 … rl 4).</summary>
        public float NukeHitsPerPullRate;
        public float NukeRadius;
        public float NukeShare;
        public float NukeStunSeconds;
        public float HoleCooldown;
        public float HoleRadius;
        public float HoleSeconds;
        public float HoleShare;
        public float HoleCollapseShare;
        public float BellCooldown;
        public float BellRadius;
        public float BellShare;
        public float BellStunSeconds;

        // ---------------------------------------------------------------- Survival
        public float MaxHealthScale;
        public float RegenDelayScale;
        /// <summary>Shield as a share of max health (0 = none).</summary>
        public float ShieldShare;
        public float ShieldDelaySeconds;
        public float ShieldRegenPerSecond;
        public float LeechStandard;
        public float LeechElite;
        public float LeechUltra;
        public float LeechCapPerSecond;
        public float DodgeChance;
        public float DodgeGhostSeconds;
        public float BulwarkRadius;
        public float BulwarkCooldown;
        public int UndyingPerRound;
        public float UndyingSeconds;
        public float UndyingHealthShare;

        // ---------------------------------------------------------------- Economy
        public float GoldScale;
        public float MagnetScale;
        /// <summary>WP-107 wall buy price scale (Arms Dealer 0.80).</summary>
        public float WallBuyScale;
        /// <summary>WP-107 ammo price scale on top of the half price.</summary>
        public float AmmoBuyScale;
        public float InterestRate;
        public int InterestCap;
        public int MidasGoldStep;
        public float MidasDamageStep;
        public float MidasDamageMax;
        /// <summary>Hearth stokes: +2 % weapon damage each (kept from WP-077).</summary>
        public float HearthDamageScale;

        // ---------------------------------------------------------------- presentation
        public TreeSynergy Synergies;
        /// <summary>Sum of owned node tiers (AD aura thresholds 15 / 35 / 60, set in B5).</summary>
        public int PowerScore;
        /// <summary>0–3 (AD B11).</summary>
        public int AuraLevel;
        /// <summary>Highest owned tier per <see cref="TreeBranch"/> index 0–6, packed 3 bits each (visual tier ladder).</summary>
        public int BranchTiers;
        /// <summary>WP-104 B1: the awakening alternative capstones owned, bit per <see cref="TreeBranch"/> index.</summary>
        public byte AltCapstones;
        /// <summary>WP-104 B1 (S-06 E4 capsule, AD-VISUAL K1): the element of the highest-tier owned element node; a tie goes to the
        /// later purchase. None without an element.</summary>
        public TreeElement DominantElement;
        /// <summary>WP-104 B1 (S-03 B3 receivers): Black Hole pull speed m/s and the share Special / Elite·Ultra feel (Standard 1).</summary>
        public float HolePullSpeed;
        public float HolePullSpecialShare;
        public float HolePullHeavyShare;
        /// <summary>Deep Freeze: Elite freeze length share; Ultra never freezes and slows by this share instead.</summary>
        public float FreezeEliteShare;
        public float FreezeUltraSlow;
        /// <summary>Nuke / Bell / Stone Guardian stuns: Elite·Ultra feel this share of the length.</summary>
        public float StunHeavyShare;
        /// <summary>Stone Guardian push wave stun (s).</summary>
        public float BulwarkStunSeconds;
        /// <summary>WP-104 Special resonance (design §3, KARAR-WP104 6; replaces the v1 Special links): Storm Chain owned → Raiju
        /// lasts this × longer and jumps this many more; Deep Freeze owned → Namekuji's gel freezes.</summary>
        public float RaijuDurationScale;
        public int RaijuExtraJumps;
        public bool NamekujiFreezes;
        /// <summary>Undying Oath: the saving blow pushes bodies within this radius away (sv_cap radius 6).</summary>
        public float UndyingPushRadius;
        /// <summary>Seeker Swarm: the EXTRA shots of a pull home inside this cone (sh_cap 25°). <see cref="SeekDegrees"/> stays the
        /// ricochet search (Bouncing Storm 15°, Seeker Swarm 25°, Pinball Storm 20°) — S-02 12.10.</summary>
        public float ExtraShotSeekDegrees;

        public int BranchTier(TreeBranch branch) => (BranchTiers >> ((int)branch * 3)) & 7;

        public void SetBranchTier(TreeBranch branch, int tier)
        {
            int shift = (int)branch * 3;
            BranchTiers = (BranchTiers & ~(7 << shift)) | ((tier < 0 ? 0 : tier > 7 ? 7 : tier) << shift);
        }

        public bool Has(TreeSynergy synergy) => (Synergies & synergy) == synergy && synergy != TreeSynergy.None;
        public bool Has(TreeElement element) => (Elements & element) == element && element != TreeElement.None;

        /// <summary>No tree: every hook returns the shipped value.</summary>
        public static TreeStats Neutral => new TreeStats
        {
            DamageScale = 1f,
            ProjectileScale = 1f,
            PierceKeep = 1f,
            RicochetKeep = 1f,
            OwnBlastRadiusScale = 1f,
            MagazineScale = 1f,
            ReloadScale = 1f,
            MagazineTailDamage = 1f,
            EndlessFireRateScale = 1f,
            FireRateScale = 1f,
            MoveScale = 1f,
            FiringMovePenaltyScale = 1f,
            KillSpeedBoost = 1f,
            FullAutoRpmScale = 1f,
            FullAutoDamageScale = 1f,
            ElementDamageScale = 1f,
            FireVulnerability = 1f,
            FreezeVulnerability = 1f,
            VenomVulnerability = 1f,
            CritMultiplier = 1f,
            MaxHealthScale = 1f,
            RegenDelayScale = 1f,
            GoldScale = 1f,
            MagnetScale = 1f,
            WallBuyScale = 1f,
            AmmoBuyScale = 1f,
            HearthDamageScale = 1f,
            // S-03 B3: a stun from a node without its own arg still reaches Elite·Ultra at half length (0 would mean immune).
            StunHeavyShare = 0.5f,
            RaijuDurationScale = 1f
        };
    }
}
