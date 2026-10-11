using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B1 · owned nodes + the weapon in hand → <see cref="TreeStats"/> (design §2, K2–K6). Runs on purchase, equip, Hearth
    /// and run start only (never per frame), inside <see cref="TreeMarkers.Resolve"/>. Same-line tiers replace each other
    /// (catalog order is tier-ascending, so the later write wins); different nodes multiply. Over a cap the power goes to damage (K6).
    /// </summary>
    public static class TreeResolver
    {
        public struct Input
        {
            public bool[] Owned;
            /// <summary>Purchase order per node (1 = first; 0 = not owned / unknown). Ties of the dominant element go to the later one.</summary>
            public int[] Order;
            public TreeWeaponProfile Weapon;
            public int Mode;
            public int HearthStokes;
            public TreeAwakening Awakening;
        }

        public static TreeStats Resolve(in Input input)
        {
            using (TreeMarkers.Resolve.Auto())
            {
                TreeCatalogDocumentV2 doc = TreeCatalogV2.Document;
                TreeStats s = TreeStats.Neutral;
                TreeWeaponProfile w = input.Weapon;
                bool[] owned = input.Owned;
                int power = 0;
                int tierMask = 0;
                int domTier = 0, domOrder = -1;

                for (int i = 0; i < doc.nodes.Length; i++)
                {
                    if (owned == null || i >= owned.Length || !owned[i]) continue;
                    TreeNodeDefV2 n = doc.nodes[i];
                    if (n == null) continue;
                    power += n.tier;
                    if (n.tier > s.BranchTier(n.Branch)) s.SetBranchTier(n.Branch, n.tier);
                    if (n.IsAwakening) tierMask |= 1 << (int)n.Branch;
                    if (n.Element != TreeElement.None)
                    {
                        int order = input.Order != null && i < input.Order.Length ? input.Order[i] : 0;
                        if (n.tier > domTier || (n.tier == domTier && order >= domOrder))
                        {
                            domTier = n.tier;
                            domOrder = order;
                            s.DominantElement = n.Element;
                        }
                    }

                    Apply(ref s, n, w);
                }

                s.AltCapstones = (byte)tierMask;

                // Full-Auto Conversion: class table (design §2.3); already automatic weapons gain rpm ×1.3.
                if (s.FullAuto)
                {
                    TreeNodeDefV2 fa = TreeCatalogV2.Find("sp_fullauto");
                    if (w.IsAutomatic)
                    {
                        s.FullAutoRpmScale = fa != null ? fa.Arg("autoRpm", 1.3f) : 1.3f;
                        s.FullAutoDamageScale = 1f;
                        s.FullAutoMagazine = 0;
                    }
                    else
                    {
                        TreeFullAutoDefV2 row = TreeCatalogV2.FullAutoFor(w.Key);
                        s.FullAutoRpmScale = row.rpmScale;
                        s.FullAutoDamageScale = row.damageScale;
                        s.FullAutoMagazine = row.magazine;
                        if (w.IsRocket) s.OwnBlastRadiusScale *= row.radiusScale;
                    }
                }

                // Synergies (design §3): free, from owned pairs; Barrage also needs rl_1 in hand.
                for (int i = 0; i < doc.synergies.Length; i++)
                {
                    TreeSynergyDefV2 sy = doc.synergies[i];
                    if (sy == null || sy.Synergy == TreeSynergy.None) continue;
                    bool a = sy.NodeA >= 0 && owned != null && sy.NodeA < owned.Length && owned[sy.NodeA];
                    bool b = sy.needsRocket ? w.IsRocket : sy.NodeB >= 0 && owned != null && sy.NodeB < owned.Length && owned[sy.NodeB];
                    if (a && b) s.Synergies |= sy.Synergy;
                }

                if (s.Has(TreeSynergy.Barrage))
                {
                    TreeFullAutoDefV2 row = TreeCatalogV2.FullAutoFor(w.Key);
                    if (row.radiusScale > 0f && !Mathf.Approximately(row.radiusScale, 1f)) s.OwnBlastRadiusScale /= row.radiusScale; // radius penalty lifted
                }

                // Hearth: +2 % weapon damage per stoke (kept from WP-077).
                TreeHearthDefV2 hearth = doc.hearth ?? new TreeHearthDefV2();
                s.HearthDamageScale = 1f + hearth.value * Mathf.Clamp(input.HearthStokes, 0, hearth.max);

                // Caps (design §8.2). Fire rate: the tree's own scale ≤ ×3, the trigger ≤ 1 200 rpm; the excess becomes damage (K6).
                float treeRate = s.FireRateScale;
                if (treeRate > TreeGuardrails.TreeFireRateScaleMax)
                {
                    s.DamageScale *= treeRate / TreeGuardrails.TreeFireRateScaleMax;
                    s.FireRateScale = TreeGuardrails.TreeFireRateScaleMax;
                }

                float rpm = Mathf.Max(1f, w.BaseRpm) * s.FireRateScale * s.FullAutoRpmScale;
                if (rpm > TreeGuardrails.TriggerRpmMax)
                {
                    float over = rpm / TreeGuardrails.TriggerRpmMax;
                    s.DamageScale *= over;
                    s.FireRateScale /= over;
                }

                s.ExtraShots = Mathf.Clamp(s.ExtraShots, 0, TreeGuardrails.ExtraShotsMax);
                s.PierceCount = Mathf.Clamp(s.PierceCount, 0, TreeGuardrails.PierceMax);
                s.RicochetBounces = Mathf.Clamp(s.RicochetBounces, 0, TreeGuardrails.RicochetMax);
                s.ShockJumps = Mathf.Clamp(s.ShockJumps, 0, TreeGuardrails.ShockJumpsMax);
                s.DodgeChance = Mathf.Clamp(s.DodgeChance, 0f, TreeGuardrails.DodgeChanceMax);
                float goldMax = doc.goldScaleMax > 0f ? doc.goldScaleMax : TreeGuardrails.GoldScaleMax;
                s.GoldScale = Mathf.Min(s.GoldScale, goldMax);
                TreeModeDefV2 mode = TreeCatalogV2.Mode(input.Mode);
                if (s.InterestRate > 0f && mode != null) s.InterestCap = mode.interestCap;

                // SG (K2): base DPS × Shot / Speed / crit expectation — elements and chaos excluded (no recursion).
                float crit = 1f + s.CritChance * Mathf.Max(0f, s.CritMultiplier - 1f);
                float volley = 1f + s.ExtraShots * s.ExtraShotDamage;
                s.WeaponPower = w.BaseDps * s.DamageScale * s.FireRateScale * s.FullAutoRpmScale * s.FullAutoDamageScale * volley * crit * s.HearthDamageScale;

                s.PowerScore = power;
                int[] aura = doc.auraThresholds;
                int level = 0;
                for (int t = 0; aura != null && t < aura.Length; t++)
                {
                    if (power >= aura[t]) level = t + 1;
                }

                s.AuraLevel = level;
                return s;
            }
        }

        private static void Apply(ref TreeStats s, TreeNodeDefV2 n, in TreeWeaponProfile w)
        {
            switch (n.id)
            {
                // ---------------------------------------------------------------- Shot
                case "sh_heavy": s.DamageScale *= n.Arg("dmg", 1f); break;
                case "sh_bore": s.ProjectileScale = n.Arg("scale", 1f); s.DamageScale *= n.Arg("dmg", 1f); break;
                case "sh_double":
                case "sh_triple":
                case "sh_fan":
                    s.ExtraShots = Mathf.Max(s.ExtraShots, (int)n.Arg("extra", 0f));
                    s.ExtraShotDamage = n.Arg("extraDmg", 0f);
                    s.FanArcDegrees = n.Arg("arc", 0f);
                    break;
                case "sh_pierce":
                case "sh_rail":
                    s.PierceCount = Mathf.Max(s.PierceCount, (int)n.Arg("pierce", 0f));
                    s.PierceKeep = n.Arg("keep", 1f);
                    s.PierceStaggerSeconds = Mathf.Max(s.PierceStaggerSeconds, n.Arg("stagger", 0f));
                    break;
                case "sh_ricochet":
                case "sh_storm":
                    s.RicochetBounces = Mathf.Max(s.RicochetBounces, (int)n.Arg("bounce", 0f));
                    s.RicochetRadius = Mathf.Max(s.RicochetRadius, n.Arg("radius", 0f));
                    s.RicochetKeep = n.Arg("keep", 1f);
                    s.SeekDegrees = Mathf.Max(s.SeekDegrees, n.Arg("seek", 0f));
                    break;
                case "sh_explosive":
                    if (w.IsRocket) s.OwnBlastRadiusScale *= n.Arg("ownRadius", 1f);
                    else
                    {
                        s.ExplosiveRadius = n.Arg("radius", 0f);
                        s.ExplosiveAreaShare = n.Arg("area", 0f);
                    }

                    break;
                case "sh_cap":
                    s.SeekDegrees = Mathf.Max(s.SeekDegrees, n.Arg("seek", 0f));
                    s.ExtraShotDamage = Mathf.Max(s.ExtraShotDamage, n.Arg("extraDmg", 0f));
                    break;

                // ---------------------------------------------------------------- Magazine
                case "mg_ext":
                case "mg_double":
                    s.MagazineScale *= n.Arg("mag", 1f);
                    break;
                case "mg_drum":
                    s.MagazineScale = Mathf.Min(s.MagazineScale * n.Arg("mag", 1f), n.Arg("magMax", 6f));
                    s.MagazineTailShare = n.Arg("tail", 0f);
                    s.MagazineTailDamage = n.Arg("tailDmg", 1f);
                    break;
                case "mg_reload1":
                case "mg_reload2":
                case "mg_reload3":
                    s.ReloadScale = Mathf.Min(s.ReloadScale, n.Arg("reload", 1f));
                    if (n.id == "mg_reload3") s.ReloadMoveFree = true;
                    break;
                case "mg_autoload":
                    s.AutoReloadIdleSeconds = n.Arg("idle", 0f);
                    s.AutoReloadPerSecond = n.Arg("rate", 0f);
                    break;
                case "mg_endless":
                case "mg_cap":
                    s.EndlessKills = s.EndlessKills > 0 ? Mathf.Min(s.EndlessKills, (int)n.Arg("kills", 0f)) : (int)n.Arg("kills", 0f);
                    s.EndlessSeconds = Mathf.Max(s.EndlessSeconds, n.Arg("seconds", 0f));
                    s.EndlessFireRateScale = Mathf.Max(s.EndlessFireRateScale, n.Arg("rpm", 1f));
                    break;

                // ---------------------------------------------------------------- Speed
                case "sp_rate1":
                case "sp_rate2":
                case "sp_rate3":
                    s.FireRateScale = Mathf.Max(s.FireRateScale, n.Arg("rpm", 1f));
                    break;
                case "sp_move":
                case "sp_dash":
                    s.MoveScale = Mathf.Max(s.MoveScale, n.Arg("move", 1f));
                    if (n.id == "sp_move") s.FiringMovePenaltyScale = Mathf.Min(s.FiringMovePenaltyScale, n.Arg("firePenalty", 1f));
                    else
                    {
                        s.KillSpeedBoost = n.Arg("killBoost", 1f);
                        s.KillSpeedBoostSeconds = n.Arg("killSeconds", 0f);
                    }

                    break;
                case "sp_fullauto": s.FullAuto = true; break;
                case "sp_twin":
                    s.TwinBarrel = true;
                    s.FireRateScale *= n.Arg("rpm", 1f);
                    s.TwinFreeEvery = (int)n.Arg("freeEvery", 0f);
                    break;
                case "sp_cap":
                    s.OverclockStep = n.Arg("step", 0f);
                    s.OverclockMax = n.Arg("max", 0f);
                    break;

                // ---------------------------------------------------------------- Element
                case "el_fire":
                case "el_fire2":
                case "el_fire3":
                    s.Elements |= TreeElement.Fire;
                    s.FireDotShare = Mathf.Max(s.FireDotShare, n.Arg("dot", 0f));
                    s.FireSeconds = Mathf.Max(s.FireSeconds, n.Arg("seconds", 0f));
                    s.FireGroundRadius = Mathf.Max(s.FireGroundRadius, n.Arg("ground", 0f));
                    s.FireGroundSeconds = Mathf.Max(s.FireGroundSeconds, n.Arg("groundSeconds", 0f));
                    s.FireSpreadRadius = Mathf.Max(s.FireSpreadRadius, n.Arg("spread", 0f));
                    s.FireVulnerability = Mathf.Max(s.FireVulnerability, n.Arg("vuln", 1f));
                    break;
                case "el_ice":
                    s.Elements |= TreeElement.Ice;
                    s.IceSlowPerStack = n.Arg("slow", 0f);
                    s.IceMaxStacks = (int)n.Arg("maxStacks", 0f);
                    s.IceStacksPerHit = w.IsSlow ? (int)n.Arg("slowStacks", 1f) : 1;
                    s.IceSeconds = n.Arg("seconds", 0f);
                    break;
                case "el_ice2":
                    s.FreezeSeconds = n.Arg("freeze", 0f);
                    s.FreezeCooldown = n.Arg("cooldown", 0f);
                    s.FreezeVulnerability = n.Arg("vuln", 1f);
                    s.FreezeEliteShare = n.Arg("eliteShare", 0.5f);
                    s.FreezeUltraSlow = n.Arg("ultraSlow", 0.5f);
                    break;
                case "el_ice3":
                    s.ShatterRadius = n.Arg("radius", 0f);
                    s.ShatterShare = n.Arg("share", 0f);
                    break;
                case "el_shock":
                case "el_shock2":
                case "el_shock3":
                    s.Elements |= TreeElement.Shock;
                    s.ShockJumps = Mathf.Max(s.ShockJumps, (int)n.Arg("jumps", 0f));
                    s.ShockShare = n.Arg("k", s.ShockShare);
                    s.ShockEventsMax = Mathf.Max(s.ShockEventsMax, n.Arg("eventMax", 0f));
                    s.ShockStaggerSeconds = Mathf.Max(s.ShockStaggerSeconds, n.Arg("stagger", 0f));
                    break;
                case "el_venom":
                    s.Elements |= TreeElement.Venom;
                    s.VenomStackShare = n.Arg("stack", 0f);
                    s.VenomMaxStacks = Mathf.Max(s.VenomMaxStacks, (int)n.Arg("maxStacks", 0f));
                    s.VenomStacksPerHit = w.IsSlow ? (int)n.Arg("slowStacks", 1f) : 1;
                    s.VenomSeconds = n.Arg("seconds", 0f);
                    break;
                case "el_venom2":
                    s.VenomCloudRadius = n.Arg("cloud", 0f);
                    s.VenomCloudSeconds = n.Arg("cloudSeconds", 0f);
                    s.VenomMaxStacks = Mathf.Max(s.VenomMaxStacks, (int)n.Arg("maxStacks", 0f));
                    break;
                case "el_venom3":
                    s.VenomArmorIgnoreStacks = (int)n.Arg("threshold", 0f);
                    s.VenomVulnerability = n.Arg("vuln", 1f);
                    s.VenomMaxStacks = Mathf.Max(s.VenomMaxStacks, (int)n.Arg("maxStacks", 0f));
                    break;
                case "el_fusion":
                    s.Fusion = true;
                    s.ElementDamageScale = n.Arg("dmg", 1f);
                    break;
                case "el_cap":
                    s.NovaCooldown = n.Arg("cooldown", 0f);
                    s.NovaRadius = n.Arg("radius", 0f);
                    s.NovaShare = n.Arg("share", 0f);
                    break;

                // ---------------------------------------------------------------- Chaos
                case "kx_crit":
                case "kx_crit2":
                    s.CritChance = Mathf.Max(s.CritChance, n.Arg("chance", 0f));
                    s.CritMultiplier = Mathf.Max(s.CritMultiplier, n.Arg("mult", 1f));
                    if (n.id == "kx_crit") s.CritPity = (int)n.Arg("pity", 0f);
                    s.CritRingRadius = Mathf.Max(s.CritRingRadius, n.Arg("ring", 0f));
                    s.CritRingShare = Mathf.Max(s.CritRingShare, n.Arg("ringShare", 0f));
                    break;
                case "kx_chain":
                    s.ChainChance = n.Arg("chance", 0f);
                    s.ChainRadius = n.Arg("radius", 0f);
                    s.ChainHealthShare = n.Arg("hp", 0f);
                    s.ChainFalloff = n.Arg("falloff", 0.5f);
                    break;
                case "kx_nuke":
                    s.Core |= TreeChaosCore.Nuke;
                    s.NukeHitsPerPullRate = n.Arg("k", 0f);
                    s.NukeRadius = n.Arg("radius", 0f);
                    s.NukeShare = n.Arg("share", 0f);
                    s.NukeStunSeconds = n.Arg("stun", 0f);
                    s.StunHeavyShare = n.Arg("stunHeavy", s.StunHeavyShare);
                    break;
                case "kx_hole":
                    s.Core |= TreeChaosCore.Hole;
                    s.HoleCooldown = n.Arg("cooldown", 0f);
                    s.HoleRadius = n.Arg("radius", 0f);
                    s.HoleSeconds = n.Arg("seconds", 0f);
                    s.HoleShare = n.Arg("share", 0f);
                    s.HoleCollapseShare = n.Arg("collapse", 0f);
                    s.HolePullSpeed = n.Arg("pull", 0f);
                    s.HolePullSpecialShare = n.Arg("pullSpecial", 0.5f);
                    s.HolePullHeavyShare = n.Arg("pullHeavy", 0.2f);
                    break;
                case "kx_mass":
                    // Critical Mass strengthens the core(s) owned (catalog order: cores are tier III, before IV).
                    if ((s.Core & TreeChaosCore.Nuke) != 0)
                    {
                        s.NukeHitsPerPullRate *= n.Arg("nukeK", 1f);
                        s.NukeRadius = Mathf.Max(s.NukeRadius, n.Arg("nukeRadius", 0f));
                    }

                    if ((s.Core & TreeChaosCore.Hole) != 0)
                    {
                        s.HoleCooldown = n.Arg("holeCooldown", s.HoleCooldown);
                        s.HoleCollapseShare *= n.Arg("collapse", 1f);
                    }

                    break;
                case "kx_cap":
                    s.BellCooldown = n.Arg("cooldown", 0f);
                    s.BellRadius = n.Arg("radius", 0f);
                    s.BellShare = n.Arg("share", 0f);
                    s.BellStunSeconds = n.Arg("stun", 0f);
                    s.StunHeavyShare = n.Arg("stunHeavy", s.StunHeavyShare);
                    break;

                // ---------------------------------------------------------------- Survival
                case "sv_hp1":
                case "sv_hp2":
                case "sv_hp3":
                    s.MaxHealthScale = Mathf.Max(s.MaxHealthScale, n.Arg("hp", 1f));
                    s.RegenDelayScale = Mathf.Min(s.RegenDelayScale, n.Arg("regenDelay", 1f));
                    break;
                case "sv_shield":
                case "sv_bulwark":
                    s.ShieldShare = Mathf.Max(s.ShieldShare, n.Arg("shield", 0f));
                    if (n.id == "sv_shield")
                    {
                        s.ShieldDelaySeconds = n.Arg("delay", 0f);
                        s.ShieldRegenPerSecond = n.Arg("regen", 0f);
                    }
                    else
                    {
                        s.BulwarkRadius = n.Arg("radius", 0f);
                        s.BulwarkCooldown = n.Arg("cooldown", 0f);
                        s.BulwarkStunSeconds = n.Arg("stun", 0f);
                        s.StunHeavyShare = n.Arg("stunHeavy", s.StunHeavyShare);
                    }

                    break;
                case "sv_leech":
                    s.LeechStandard = n.Arg("standard", 0f);
                    s.LeechElite = n.Arg("elite", 0f);
                    s.LeechUltra = n.Arg("ultra", 0f);
                    s.LeechCapPerSecond = n.Arg("cap", 0f);
                    break;
                case "sv_dodge":
                    s.DodgeChance = n.Arg("chance", 0f);
                    s.DodgeGhostSeconds = n.Arg("ghost", 0f);
                    break;
                case "sv_cap":
                    s.UndyingPerRound = (int)n.Arg("perRound", 0f);
                    s.UndyingSeconds = n.Arg("seconds", 0f);
                    s.UndyingHealthShare = n.Arg("hp", 0f);
                    break;

                // ---------------------------------------------------------------- Economy
                case "ec_gold1":
                case "ec_gold2":
                case "ec_gold3":
                    s.GoldScale = Mathf.Max(s.GoldScale, n.Arg("gold", 1f));
                    s.MagnetScale = Mathf.Max(s.MagnetScale, n.Arg("magnet", 1f));
                    break;
                case "ec_dealer":
                    s.WallBuyScale = n.Arg("wall", 1f);
                    s.AmmoBuyScale = n.Arg("ammo", 1f);
                    break;
                case "ec_interest":
                    s.InterestRate = n.Arg("rate", 0f);
                    break;
                case "ec_midas":
                    s.MidasGoldStep = (int)n.Arg("per", 0f);
                    s.MidasDamageStep = n.Arg("step", 0f);
                    s.MidasDamageMax = n.Arg("cap", 0f);
                    break;
            }
        }
    }
}
