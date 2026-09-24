using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;

namespace DD.Prototype.Profile
{
    /// <summary>
    /// WP-077 B2b: the meta PATH rules over a profile — ownership, the seven S-09 states, the respec
    /// refund, and the run modifiers the owned passives give the in-run tree. Pure (reads a
    /// <see cref="ProfileData"/>, writes nothing); the commands live in <see cref="ProfileService"/>.
    /// </summary>
    public static class MetaPathRules
    {
        /// <summary>WP-077 §5 achievement links (access, never power): the ids whose ownership opens something.</summary>
        public const string LinkUltraHunter = "ultra_hunter";          // sg_1's capstone needs no mastery
        public const string LinkArmorBreaker = "armor_breaker_100";    // the ArmorBreak keystone from round 1
        public const string LinkSpecialPickups = "special_pickup_10";  // SPECIAL column 1 without its level gate

        /// <summary>
        /// WP-077 (S-09 Selection lock line, direction §3.1 "PATH · 120 Kül · L11"): how a weapon opens for this
        /// profile. Entitled = playable now; Ladder = opens on the level ladder (no Kül; LevelGate is the ladder
        /// level); otherwise a PATH weapon node (Kül + level). Ready = both met and buyable now ("PATH › ready").
        /// </summary>
        public struct WeaponLockInfo
        {
            public bool Entitled;
            public bool Ladder;
            public bool OnPath;
            public int LevelGate;
            public int AshCost;
            public bool LevelMet;
            public bool AshMet;
            public bool Ready;
            public RunTreeNodeStatus Status;
            public MetaLockReason LockReason;
        }

        public static WeaponLockInfo WeaponLock(ProfileData p, string weaponKey, int ladderLevel)
        {
            var info = new WeaponLockInfo { LevelGate = ladderLevel, Status = RunTreeNodeStatus.Locked };
            if (p == null || string.IsNullOrEmpty(weaponKey)) return info;
            info.Entitled = p.Has(p.UnlockedWeapons, weaponKey);
            int index = -1;
            var nodes = MetaPathCatalog.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Kind == MetaNodeKind.Weapon && nodes[i].WeaponKey == weaponKey) { index = i; break; }
            }

            if (index < 0 || IsLadderWeapon(nodes[index]))
            {
                info.Ladder = true;
                info.LevelMet = p.ProfileLevel >= ladderLevel;
                info.Status = info.Entitled ? RunTreeNodeStatus.Purchased : RunTreeNodeStatus.Locked;
                info.LockReason = info.Entitled ? MetaLockReason.None : MetaLockReason.LadderWeapon;
                return info;
            }

            MetaPathNode n = nodes[index];
            info.OnPath = true;
            info.LevelGate = n.LevelGate;
            info.AshCost = n.AshCost;
            info.LevelMet = p.ProfileLevel >= n.LevelGate;
            info.AshMet = p.AshBalance >= n.AshCost;
            info.Status = Status(p, index, out info.LockReason, out _, out _);
            info.Ready = info.Status == RunTreeNodeStatus.Available;
            return info;
        }

        /// <summary>Respecs after the first refund this share of the Ash spent on the refunded nodes (K10/§6).</summary>
        public const float RespecRefundShare = 0.75f;

        /// <summary>Owned: a weapon node when the weapon is entitled (ladder or bought), any other node when it is in MetaNodes.</summary>
        public static bool IsOwned(ProfileData p, MetaPathNode n)
        {
            if (p == null || n == null) return false;
            if (n.Kind == MetaNodeKind.Weapon)
            {
                return p.Has(p.UnlockedWeapons, n.WeaponKey);
            }

            return p.Has(p.MetaNodes, n.Id);
        }

        /// <summary>A weapon that opens on the level ladder (DEC-085: smg_1 L3, sg_1 L5) or is the default one — never bought.</summary>
        public static bool IsLadderWeapon(MetaPathNode n) => n != null && n.Kind == MetaNodeKind.Weapon && n.AshCost == 0;

        public static RunTreeNodeStatus Status(ProfileData p, int index, out MetaLockReason reason, out int missing, out int closedBy)
        {
            reason = MetaLockReason.None;
            missing = -1;
            closedBy = -1;
            var nodes = MetaPathCatalog.Nodes;
            MetaPathNode n = nodes[index];

            if (IsOwned(p, n)) return RunTreeNodeStatus.Purchased;
            if (IsLadderWeapon(n))
            {
                reason = MetaLockReason.LadderWeapon;
                return RunTreeNodeStatus.Locked;
            }

            if (n.ExclusionGroup != 0)
            {
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (j != index && nodes[j].ExclusionGroup == n.ExclusionGroup && IsOwned(p, nodes[j]))
                    {
                        closedBy = j;
                        return RunTreeNodeStatus.Excluded;
                    }
                }
            }

            bool gateWaived = n.Kind == MetaNodeKind.SpecialLean && n.Column == 0 && p.Has(p.Achievements, LinkSpecialPickups);
            if (p.ProfileLevel < n.LevelGate && !gateWaived)
            {
                reason = MetaLockReason.LevelGate;
                return RunTreeNodeStatus.Locked;
            }

            if (!MetaPathEffects.IsLive(n.EffectId))
            {
                reason = MetaLockReason.EffectPending;
                return RunTreeNodeStatus.Locked;
            }

            for (int k = 0; k < n.Parents.Length; k++)
            {
                if (!IsOwned(p, nodes[n.Parents[k]]))
                {
                    missing = n.Parents[k];
                    return RunTreeNodeStatus.PrereqMissing;
                }
            }

            return n.AshCost <= p.AshBalance ? RunTreeNodeStatus.Available : RunTreeNodeStatus.Unaffordable;
        }

        /// <summary>Ash a respec would give back now: the non-weapon nodes' cost, 100 % the first time, 75 % after.</summary>
        public static int RespecRefund(ProfileData p)
        {
            if (p == null) return 0;
            long spent = 0;
            foreach (string id in p.MetaNodes)
            {
                MetaPathNode n = MetaPathCatalog.Find(id);
                if (n != null && n.Kind != MetaNodeKind.Weapon) spent += n.AshCost;
            }

            double share = p.MetaRespecCount == 0 ? 1.0 : RespecRefundShare;
            return (int)System.Math.Min(int.MaxValue, (long)System.Math.Floor(spent * share));
        }

        /// <summary>True when the profile owns a node whose effect is <paramref name="effectId"/>; its value in <paramref name="value"/>.</summary>
        public static bool Owns(ProfileData p, string effectId, out float value)
        {
            value = 0f;
            if (p == null) return false;
            foreach (string id in p.MetaNodes)
            {
                MetaPathNode n = MetaPathCatalog.Find(id);
                if (n != null && n.EffectId == effectId && MetaPathEffects.IsLive(effectId))
                {
                    value = n.EffectValue;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The in-run modifiers the owned passives grant (read once at run start; the run never writes the
        /// profile). K6 (DEC-098, audit #6): the weapon's mastery M1 (150 kills) lets its capstone into the offer; a pin brings it earlier.
        /// </summary>
        public static MetaRunModifiers RunModifiers(ProfileData p, string weaponKey)
        {
            MetaRunModifiers m = MetaRunModifiers.None;
            if (p == null) return m;
            if (Owns(p, MetaPathEffects.RerollCost, out float reroll)) m.RerollCostOverride = (int)reroll;
            if (Owns(p, MetaPathEffects.HearthSlope, out float slope)) m.HearthCostStepOverride = (int)slope;
            if (Owns(p, MetaPathEffects.PurseAtStart, out _)) m.PurseAtStart = true;
            if (Owns(p, MetaPathEffects.CapstoneDiscount, out float discount)) m.CapstoneDiscount = discount;
            m.CapstoneMasteryMet = p.MasteryKills(weaponKey) >= MasteryThresholds[0] || (weaponKey == "sg_1" && p.Has(p.Achievements, LinkUltraHunter));
            m.ArmorBreakKeystoneOpen = p.Has(p.Achievements, LinkArmorBreaker);
            return m;
        }

        /// <summary>Kills with one weapon for mastery M1…M5 (direction §3.3; calibrated in B3).</summary>
        public static readonly int[] MasteryThresholds = { 150, 400, 900, 1800, 3500 };

        public static int MasteryLevel(int kills)
        {
            int m = 0;
            for (int i = 0; i < MasteryThresholds.Length; i++) if (kills >= MasteryThresholds[i]) m = i + 1;
            return m;
        }
    }
}
