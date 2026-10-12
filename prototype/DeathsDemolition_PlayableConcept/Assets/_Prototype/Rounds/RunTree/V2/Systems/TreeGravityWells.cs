using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B3 (GDEV §2 #19, (c); GD-TREE kx_hole, kx_mass, sy_frozen_gravity) · Black Hole Rounds. Off cooldown
    /// (<see cref="TreeStats.HoleCooldown"/>), the next hit opens a well of <see cref="TreeStats.HoleRadius"/> for
    /// <see cref="TreeStats.HoleSeconds"/>; at most <see cref="TreeGuardrails.BlackHolesActive"/> (1) live at once, 2 with the
    /// Twin Cores awakening (both cores owned).
    ///
    /// A live well pulls at <see cref="TreeSystemsTuning.HolePullHz"/> (10 Hz): one registry query (frame budget), and every
    /// living body inside gets <see cref="ITreeStatusReceiver.ApplyPull"/> with the raw <see cref="TreeStats.HolePullSpeed"/>
    /// and a hold of <see cref="TreeSystemsTuning.HolePullHoldSeconds"/> (the body applies the tier share — Special
    /// HolePullSpecialShare, Elite / Ultra HolePullHeavyShare — and its core dead zone; the pull never cancels an attack, S4).
    /// Bodies inside take <see cref="TreeStats.HoleShare"/> × SG per second each (status damage on a 0.5 s tick). At the end the
    /// well collapses: a <see cref="TreeProcKind.BlackHoleCollapse"/> blast of <see cref="TreeStats.HoleCollapseShare"/> × SG
    /// through <see cref="TreeBlastQueue"/>. Frozen Gravity: a body inside for 1 s gets a full set of cold stacks (the body
    /// freezes by its own rules), the collapse hits frozen bodies ×1.5 and throws a 3 m ice blast of 1.0×SG.
    /// No physics, no allocation; <see cref="TreeMarkers.Gravity"/>.
    /// </summary>
    public static class TreeGravityWells
    {
        private const int Slots = TreeSystemsTuning.TwinCoresBlackHoles;
        private const float Eps = 1e-4f;

        private struct Well
        {
            public bool Active;
            public int Id;
            public Vector3 Center;
            public float Radius, OpenedAt, EndsAt, NextPull, NextDamage;
        }

        private static readonly Well[] s_wells = new Well[Slots];
        private static readonly ITreeStatusReceiver[] s_targets = new ITreeStatusReceiver[TreeGuardrails.StatusSlots];
        // per status slot: who was seen inside, since when, last seen (Frozen Gravity's 1 s)
        private static readonly int[] s_seenGen = new int[TreeGuardrails.StatusSlots];
        private static readonly float[] s_insideSince = new float[TreeGuardrails.StatusSlots];
        private static readonly float[] s_lastSeen = new float[TreeGuardrails.StatusSlots];
        private static float s_readyAt;
        private static int s_nextId;
        // B6 Event Horizon: one small persistent well orbiting the player (outside the slots and the active cap)
        private static bool s_horizonUp;
        private static float s_horizonAngle, s_horizonNextPull, s_horizonNextDamage;
        private static Vector3 s_horizonCenter;

        /// <summary>WP-104 B6 Event Horizon: the orbiting well is up and where it is (S-07 follows it every frame).</summary>
        public static bool HorizonActive => s_horizonUp;
        public static Vector3 HorizonCenter => s_horizonCenter;
        public static int HorizonPulls { get; private set; }

        public static int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Slots; i++)
                {
                    if (s_wells[i].Active) n++;
                }

                return n;
            }
        }

        /// <summary>1, or 2 when both chaos cores are owned (Twin Cores awakening).</summary>
        public static int MaxActive
        {
            get
            {
                TreeChaosCore both = TreeChaosCore.Nuke | TreeChaosCore.Hole;
                return (TreeRuntime.Current.Core & both) == both ? TreeSystemsTuning.TwinCoresBlackHoles : TreeGuardrails.BlackHolesActive;
            }
        }

        public static float ReadyAt => s_readyAt;
        public static int Opened { get; private set; }
        public static int Collapsed { get; private set; }
        public static int Refused { get; private set; }
        public static int PullsApplied { get; private set; }
        public static int FrozenByGravity { get; private set; }
        public static float LastPullSpeed { get; private set; }

        /// <summary>The hit hook: off cooldown, a hit opens a well at its point. True when one opened.</summary>
        public static bool TryOpenOnHit(Vector3 point, float now)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if ((s.Core & TreeChaosCore.Hole) == 0 || s.HoleRadius <= 0f || s.HoleSeconds <= 0f) return false;
            if (now < s_readyAt - Eps) return false;
            if (!Open(point, now)) return false;
            s_readyAt = now + Mathf.Max(0f, s.HoleCooldown);
            return true;
        }

        /// <summary>Opens a well now (no cooldown check; the active cap holds). Tests / Twin Cores.</summary>
        public static bool Open(Vector3 point, float now)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s.HoleRadius <= 0f || s.HoleSeconds <= 0f) return false;
            if (ActiveCount >= MaxActive)
            {
                Refused++;
                return false;
            }

            for (int i = 0; i < Slots; i++)
            {
                if (s_wells[i].Active) continue;
                s_wells[i] = new Well
                {
                    Active = true, Id = ++s_nextId, Center = point, Radius = s.HoleRadius, OpenedAt = now, EndsAt = now + s.HoleSeconds,
                    NextPull = now, NextDamage = now + TreeSystemsTuning.HoleDamageTickSeconds
                };
                Opened++;
                TreeSystemsDriver.EnsureExists();
                TreeSystemsRunner.Raise(TreeProcKind.BlackHoleOpen, point, s.HoleRadius, 0f, TreeElement.None, TreeBranch.Chaos,
                    (s.Synergies & TreeSynergy.FrozenGravity) != 0 ? TreeSynergy.FrozenGravity : TreeSynergy.None, null, 0, s.HoleSeconds);
                return true;
            }

            return false;
        }

        /// <summary>Pull ticks, damage ticks, Frozen Gravity, collapse. Called by <see cref="TreeSystemsRunner.Tick"/>.</summary>
        internal static void Tick(float deltaTime, float now)
        {
            if (deltaTime <= 0f) return;
            bool any = false;
            for (int i = 0; i < Slots; i++) any |= s_wells[i].Active;
            if (!any) return;

            using (TreeMarkers.Gravity.Auto())
            {
                for (int i = 0; i < Slots; i++)
                {
                    if (!s_wells[i].Active) continue;
                    if (now >= s_wells[i].NextPull - Eps && TreeTargetQuery.TryUse())
                    {
                        s_wells[i].NextPull = now + 1f / TreeSystemsTuning.HolePullHz;   // no catch-up: a late pull is just late
                        Pull(i, now);
                    }

                    if (now >= s_wells[i].EndsAt - Eps) Collapse(i);
                }
            }
        }

        private static void Pull(int w, float now)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            Well well = s_wells[w];
            bool frozenGravity = (s.Synergies & TreeSynergy.FrozenGravity) != 0;
            bool damageDue = now >= well.NextDamage - Eps;
            float damage = s.HoleShare * s.WeaponPower * TreeSystemsTuning.HoleDamageTickSeconds;
            if (damageDue) s_wells[w].NextDamage = well.NextDamage + TreeSystemsTuning.HoleDamageTickSeconds;

            float interval = 1f / TreeSystemsTuning.HolePullHz;
            float speed = PullSpeed(in s);
            int n = TreeStatusSystem.Query(well.Center, well.Radius, s_targets);
            bool owned = damageDue ? TreeDamageContext.BeginTransaction(0) : false;   // one transaction per damage tick
            TreeStatusSystem.BeginBatch();   // a death inside frees its slot after the loop (12.10 (a))
            try
            {
                for (int k = 0; k < n; k++)
                {
                    ITreeStatusReceiver body = s_targets[k];
                    if (body == null || !body.IsAlive) continue;

                    // raw speed: the body applies HolePullSpecialShare / HolePullHeavyShare and its core dead zone (S4)
                    body.ApplyPull(well.Center, speed, TreeSystemsTuning.HolePullHoldSeconds);
                    LastPullSpeed = speed;
                    PullsApplied++;

                    int slot = TreeStatusSystem.SlotOf(body);
                    if (slot >= 0)
                    {
                        int gen = TreeStatusSystem.GenerationOf(body);
                        if (s_seenGen[slot] != gen || now - s_lastSeen[slot] > interval * 2.5f)
                        {
                            s_seenGen[slot] = gen;
                            s_insideSince[slot] = now;
                        }

                        s_lastSeen[slot] = now;
                        if (frozenGravity && now - s_insideSince[slot] >= TreeSystemsTuning.FrozenGravityAfter - Eps && !body.IsFrozen)
                        {
                            // the body's freeze (tier rule, cooldown) from a full set of cold stacks
                            if (TreeStatusSystem.ApplyIce(body, Mathf.Max(1, s.IceMaxStacks), true) == IceResult.Frozen) FrozenByGravity++;
                        }
                    }

                    if (damageDue && damage > 0f && body.IsAlive)
                    {
                        TreeDamageContext.Status(body, damage, TreeElement.None, 0, TreeDamageKind.Hole, TreeProcKind.BlackHoleOpen, well.Center);
                    }
                }
            }
            finally
            {
                TreeStatusSystem.EndBatch();
                TreeDamageContext.EndTransaction(owned);
                for (int k = 0; k < n; k++) s_targets[k] = null;
            }
        }

        private static void Collapse(int w)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            Well well = s_wells[w];
            s_wells[w] = default;
            Collapsed++;

            bool frozenGravity = (s.Synergies & TreeSynergy.FrozenGravity) != 0;
            var collapse = new TreeBlastRequest
            {
                Kind = TreeProcKind.BlackHoleCollapse, Point = well.Center, Radius = well.Radius, Damage = s.HoleCollapseShare * s.WeaponPower,
                FrozenBonus = frozenGravity ? TreeSystemsTuning.FrozenGravityCollapseBonus : 0f,
                Synergy = frozenGravity ? TreeSynergy.FrozenGravity : TreeSynergy.None
            };
            TreeBlastQueue.Enqueue(in collapse);

            if (!frozenGravity) return;
            var ice = new TreeBlastRequest
            {
                Kind = TreeProcKind.Shatter, Point = well.Center, Radius = TreeSystemsTuning.FrozenGravityIceRadius,
                Damage = TreeSystemsTuning.FrozenGravityIceShare * s.WeaponPower * TreeStatusSystem.ElementScale(in s),
                Element = TreeElement.Ice, Synergy = TreeSynergy.FrozenGravity
            };
            TreeBlastQueue.Enqueue(in ice);
        }

        /// <summary>
        /// B6 Event Horizon (awakening, Chaos V): a well of HorizonRadius orbits the player at HorizonOrbit, pulls at
        /// HorizonPullShare × the hole's pull speed (10 Hz, frame budget) and deals HorizonShare × SG per second (0.5 s ticks).
        /// It never collapses; it goes down without an anchor (no player) or without the node.
        /// </summary>
        internal static void TickHorizon(float deltaTime, float now, Vector3 anchor, bool hasAnchor)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s.HorizonRadius <= 0f || !hasAnchor)
            {
                s_horizonUp = false;
                return;
            }

            if (!s_horizonUp)
            {
                s_horizonUp = true;
                s_horizonNextPull = now;
                s_horizonNextDamage = now + TreeSystemsTuning.HoleDamageTickSeconds;
                TreeSystemsRunner.Raise(TreeProcKind.BlackHoleOpen, anchor, s.HorizonRadius, 0f, TreeElement.None, TreeBranch.Chaos, TreeSynergy.None, null, 0, 0f);
            }

            s_horizonAngle = Mathf.Repeat(s_horizonAngle + deltaTime * TreeSystemsTuning.HorizonAngularSpeed, Mathf.PI * 2f);
            float orbit = Mathf.Max(0f, s.HorizonOrbit);
            s_horizonCenter = anchor + new Vector3(Mathf.Cos(s_horizonAngle) * orbit, 0f, Mathf.Sin(s_horizonAngle) * orbit);
            if (now < s_horizonNextPull - Eps || !TreeTargetQuery.TryUse()) return;
            s_horizonNextPull = now + 1f / TreeSystemsTuning.HolePullHz;

            using (TreeMarkers.Gravity.Auto())
            {
                bool damageDue = now >= s_horizonNextDamage - Eps;
                if (damageDue) s_horizonNextDamage += TreeSystemsTuning.HoleDamageTickSeconds;
                float damage = s.HorizonShare * s.WeaponPower * TreeSystemsTuning.HoleDamageTickSeconds;
                float speed = PullSpeed(in s) * TreeSystemsTuning.HorizonPullShare;
                int n = TreeStatusSystem.Query(s_horizonCenter, s.HorizonRadius, s_targets);
                bool owned = damageDue ? TreeDamageContext.BeginTransaction(0) : false;
                TreeStatusSystem.BeginBatch();
                try
                {
                    for (int k = 0; k < n; k++)
                    {
                        ITreeStatusReceiver body = s_targets[k];
                        if (body == null || !body.IsAlive) continue;
                        body.ApplyPull(s_horizonCenter, speed, TreeSystemsTuning.HolePullHoldSeconds);
                        HorizonPulls++;
                        if (damageDue && damage > 0f && body.IsAlive)
                        {
                            TreeDamageContext.Status(body, damage, TreeElement.None, 0, TreeDamageKind.Hole, TreeProcKind.BlackHoleOpen, s_horizonCenter);
                        }
                    }
                }
                finally
                {
                    TreeStatusSystem.EndBatch();
                    TreeDamageContext.EndTransaction(owned);
                    for (int k = 0; k < n; k++) s_targets[k] = null;
                }
            }
        }

        /// <summary>HolePullSpeed from the build (B1b), the tuning value when the catalog gives none.</summary>
        public static float PullSpeed(in TreeStats s) => s.HolePullSpeed > 0f ? s.HolePullSpeed : TreeSystemsTuning.HolePullSpeedFallback;

        internal static void Reset()
        {
            for (int i = 0; i < Slots; i++) s_wells[i] = default;
            for (int i = 0; i < s_targets.Length; i++)
            {
                s_targets[i] = null;
                s_seenGen[i] = 0;
                s_insideSince[i] = 0f;
                s_lastSeen[i] = 0f;
            }

            s_readyAt = 0f;
            s_nextId = 0;
            s_horizonUp = false;
            s_horizonAngle = 0f;
            s_horizonNextPull = 0f;
            s_horizonNextDamage = 0f;
            s_horizonCenter = Vector3.zero;
            HorizonPulls = 0;
            Opened = 0;
            Collapsed = 0;
            Refused = 0;
            PullsApplied = 0;
            FrozenByGravity = 0;
            LastPullSpeed = 0f;
        }
    }
}
