using System.Globalization;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>WP-104 B3 · what the fire path knows about one body hit (S-02 fills it; plain struct).</summary>
    public struct TreeHitFacts
    {
        /// <summary>Trigger pulls per second of the weapon in hand (design K3: N = k × pulls/s for the nuke).</summary>
        public float PullsPerSecond;
        /// <summary>How much this hit counts toward the nuke (1 bullet; 1 / pellets for a pellet so a full shell counts 1). 0 → 1.</summary>
        public float HitWeight;
        public bool Crit;
        /// <summary>The round had already pierced a body (Toxic Rail: +2 venom stacks).</summary>
        public bool Pierced;
        public ClassicWeaponId Weapon;
    }

    /// <summary>F1 / telemetry snapshot of the tree systems (plain struct, no allocation).</summary>
    public struct TreeSystemsCounters
    {
        public int ActiveSlots, StatusBodies, QueueCount, Merged, Quiet, Presented, Detonated, QueriesLastFrame, PeakQueriesPerFrame;
        public int Zones, Wells, DepthRefused, NestedDetonations;
    }

    /// <summary>
    /// WP-104 B3 · the one entry point and clock of the tree's runtime systems (S-04): <see cref="TreeStatusSystem"/>,
    /// <see cref="TreeAreaPool"/>, <see cref="TreeBlastQueue"/>, <see cref="TreeGravityWells"/>. Static and frame-driven:
    /// <see cref="TreeSystemsDriver"/> calls <see cref="Tick"/> once a frame right after the projectile step (execution order
    /// 61 &gt; ProjectileDriver 60), on the same scaled clock (<c>Time.deltaTime</c>, <c>Time.time</c>), so blasts queued
    /// by this frame's hits land this frame. Run start / end and scene changes <see cref="Reset"/> everything.
    ///
    /// Frame order: binding sync (production) → query budget reset → status (DoT phases) → wells (pull / damage / collapse) →
    /// presented blasts → zones → quiet blasts → fire spreads → anchor pulses (Element Nova, Doomsday Bell; detonate next frame).
    ///
    /// Callers: S-02 through <c>TreeZombieBinding.OnWeaponHit</c> / <c>OnChainShock</c> (→ <see cref="OnWeaponHit"/>,
    /// <see cref="OnChainShock"/>) and <see cref="TreeBlastQueue.EnqueueExplosiveRound"/>, <see cref="TreeBlastQueue.EnqueueCritRing"/>,
    /// <see cref="TreeBlastQueue.OnBarrageRocketImpact"/>; S-03 through <c>ZombieEnemy.TreeStatusEnded</c> (→ <see cref="OnBodyDied"/>
    /// or <see cref="TreeStatusSystem.Release"/>) and <c>TreeZombieBinding.StatusDamageScale</c> / <c>IgnoresArmor</c>.
    /// With neutral stats every call returns at once: Normal play is unchanged.
    /// Damage: SG = <see cref="TreeStats.WeaponPower"/>; element damage = SG share × ElementDamageScale (Fusion), chaos damage
    /// = SG share (chain blasts: share of the victim's max health).
    /// </summary>
    public static class TreeSystemsRunner
    {
        private static uint s_rng = 1u;
        private static float s_nukeCounter;
        private static bool s_nukeArmed;
        private static float s_nukeReadyAt;
        private static float s_novaReadyAt, s_bellReadyAt, s_nextAnchorCheck;
        private static Vector3 s_anchor;
        private static bool s_hasAnchor;
        private static int s_elementTurn;
        private static readonly float[] s_shockWindow = new float[TreeGuardrails.ShockEventsPerSecond];
        private static int s_shockNext;

        static TreeSystemsRunner()
        {
            ResetShockWindow();
        }

        /// <summary>Frames ticked since the last reset.</summary>
        public static int Frame { get; private set; }

        /// <summary>The systems' clock (the last <see cref="Tick"/>'s now).</summary>
        public static float Now { get; private set; }

        /// <summary>The weapon credited with tree kills (last hit's weapon).</summary>
        public static ClassicWeaponId CurrentWeapon { get; private set; }

        public static int Nukes { get; private set; }
        public static int Novas { get; private set; }
        public static int Bells { get; private set; }
        public static int ChainBlastsQueued { get; private set; }
        public static int DeathsSeen { get; private set; }
        public static bool NukeArmed => s_nukeArmed;
        public static float NukeCounter => s_nukeCounter;

        // ------------------------------------------------------------------ frame

        /// <summary>
        /// Production binding hook (<c>TreeZombieBinding</c>): runs first in every <see cref="Tick"/> to register new living
        /// bodies. Null in tests. A static delegate so this file compiles without the body side.
        /// </summary>
        public static System.Action BeforeTick;

        /// <summary>Raised at the end of <see cref="Reset"/> (the binding forgets its body → adapter map).</summary>
        public static System.Action AfterReset;

        public static void Tick(float deltaTime, float now)
        {
            BeforeTick?.Invoke();
            Frame++;
            Now = now;
            TreeTargetQuery.BeginFrame();
            if (deltaTime <= 0f) return;   // paused: nothing advances

            TreeStatusSystem.Tick(deltaTime, now);
            TreeGravityWells.Tick(deltaTime, now);
            TreeBlastQueue.FlushPresented(now);
            TreeAreaPool.Tick(deltaTime);
            TreeBlastQueue.FlushQuiet(now);
            TreeStatusSystem.RunSpreads();
            TickAnchorPulses(now);
        }

        /// <summary>Run start / end, scene change, tests. <paramref name="seed"/> drives the chain-reaction rolls (deterministic replay).</summary>
        public static void Reset(uint seed = 1u)
        {
            s_rng = seed != 0u ? seed : 1u;
            s_nukeCounter = 0f;
            s_nukeArmed = false;
            s_nukeReadyAt = 0f;
            s_novaReadyAt = 0f;
            s_bellReadyAt = 0f;
            s_nextAnchorCheck = 0f;
            s_anchor = Vector3.zero;
            s_hasAnchor = false;
            s_elementTurn = 0;
            ResetShockWindow();
            Frame = 0;
            Now = 0f;
            CurrentWeapon = default;
            Nukes = 0;
            Novas = 0;
            Bells = 0;
            ChainBlastsQueued = 0;
            DeathsSeen = 0;

            TreeStatusSystem.Reset();
            TreeAreaPool.Reset();
            TreeBlastQueue.Reset();
            TreeGravityWells.Reset();
            TreeTargetQuery.Reset();
            TreeDamageContext.Reset();
            AfterReset?.Invoke();
        }

        /// <summary>The player position for Element Nova / Doomsday Bell (the driver sets it every frame).</summary>
        public static void SetAnchor(Vector3 position)
        {
            s_anchor = position;
            s_hasAnchor = true;
        }

        public static void ClearAnchor() => s_hasAnchor = false;

        // ------------------------------------------------------------------ hit (S-02)

        /// <summary>
        /// One body hit by the weapon in hand (after the hit landed). Applies the element of this hit (one owned element per
        /// hit in turn; all of them with Fusion), counts toward the Mini Nuke and fires it when armed and off cooldown, opens
        /// a black hole when ready. Returns the elements applied (S-02 runs its chain when Shock is in it).
        /// <paramref name="body"/> may be null (a hit on nothing alive still counts for the cores).
        /// </summary>
        public static TreeElement OnWeaponHit(ITreeStatusReceiver body, Vector3 point, in TreeHitFacts facts)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            CurrentWeapon = facts.Weapon;
            TreeElement applied = TreeElement.None;

            if (s.Elements != TreeElement.None && body != null && body.IsAlive)
            {
                if (TreeStatusSystem.SlotOf(body) < 0) TreeStatusSystem.Register(body);   // a body that predates the last reset
                applied = s.Fusion ? s.Elements : NextElement(s.Elements);
                TreeStatusSystem.ApplyHit(body, applied);
                if (facts.Pierced && (s.Synergies & TreeSynergy.ToxicRail) != 0 && (s.Elements & TreeElement.Venom) != 0)
                {
                    TreeStatusSystem.AddVenomStacks(body, TreeSystemsTuning.ToxicRailStacks);   // GD sy_toxic_rail
                    applied |= TreeElement.Venom;
                }
            }

            if ((s.Core & TreeChaosCore.Nuke) != 0) CountNuke(in s, point, in facts);
            if ((s.Core & TreeChaosCore.Hole) != 0) TreeGravityWells.TryOpenOnHit(point, Now);
            return applied;
        }

        /// <summary>S-02's chain lightning reached <paramref name="body"/> from <paramref name="from"/> (Thunder Lord stagger via ApplyTreeStagger, S3).</summary>
        public static bool OnChainShock(ITreeStatusReceiver body, Vector3 from) => TreeStatusSystem.ApplyShockStagger(body, from);

        /// <summary>Shared shock-event gate (≤ <see cref="TreeGuardrails.ShockEventsPerSecond"/> per second, sliding window).</summary>
        public static bool TryShockEvent()
        {
            float oldest = s_shockWindow[s_shockNext];
            if (Now - oldest < 1f - 1e-4f) return false;
            s_shockWindow[s_shockNext] = Now;
            s_shockNext = (s_shockNext + 1) % s_shockWindow.Length;
            return true;
        }

        // ------------------------------------------------------------------ death (S-03)

        /// <summary>
        /// S-03's <c>Die</c> calls this once, after the body stopped being alive. Reads the body's status, queues what its death
        /// sets off (Chain Reaction, Shatter, Wildfire ground, Plague Cloud) and frees its slot. Never detonates: the queue
        /// drains next tick. The death's chain generation is the one of the tree damage that killed it (0 for a weapon kill).
        /// </summary>
        public static void OnBodyDied(ITreeStatusReceiver body)
        {
            if (body == null) return;
            DeathsSeen++;
            int depth = TreeDamageContext.Active ? TreeDamageContext.Depth : 0;
            bool burning = TreeStatusSystem.IsBurning(body);
            bool frozen = body.FrozenAtDeath || body.IsFrozen;   // S-03 locks FrozenAtDeath in Die
            bool poisoned = TreeStatusSystem.VenomStacks(body) > 0;
            Vector3 point = body.Position;
            float maxHealth = body.MaxHealth;
            TreeStatusSystem.Release(body);

            ref readonly TreeStats s = ref TreeRuntime.Current;
            int next = depth + 1;
            float element = s.WeaponPower * TreeStatusSystem.ElementScale(in s);

            if (s.ChainChance > 0f && s.ChainRadius > 0f && s.ChainHealthShare > 0f && next <= TreeGuardrails.ChainDepth && maxHealth > 0f)
            {
                if (NextRoll() < ChainChanceAt(in s, depth))
                {
                    bool cascade = (s.Synergies & TreeSynergy.ThunderCascade) != 0;
                    var r = new TreeBlastRequest
                    {
                        Kind = TreeProcKind.ChainBlast, Point = point, Radius = s.ChainRadius, Damage = s.ChainHealthShare * maxHealth, Depth = next,
                        Synergy = cascade ? TreeSynergy.ThunderCascade : TreeSynergy.None
                    };
                    if (TreeBlastQueue.Enqueue(in r)) ChainBlastsQueued++;
                }
            }

            if (frozen && s.ShatterRadius > 0f && s.ShatterShare > 0f && next <= TreeGuardrails.ChainDepth)
            {
                var r = new TreeBlastRequest
                {
                    Kind = TreeProcKind.Shatter, Point = point, Radius = s.ShatterRadius, Damage = s.ShatterShare * element, Element = TreeElement.Ice,
                    Depth = next, IceStacks = TreeSystemsTuning.ShatterIceStacks
                };
                TreeBlastQueue.Enqueue(in r);
            }

            if (burning && s.FireGroundRadius > 0f && s.FireGroundSeconds > 0f)
            {
                TreeAreaPool.Ignite(TreeAreaKind.Fire, point, s.FireGroundRadius, s.FireGroundSeconds, s.FireDotShare * element, depth);
            }

            if (poisoned && s.VenomCloudRadius > 0f && s.VenomCloudSeconds > 0f)
            {
                TreeAreaPool.Ignite(TreeAreaKind.Venom, point, s.VenomCloudRadius, s.VenomCloudSeconds, 0f, depth);
            }
        }

        // ------------------------------------------------------------------ rules (public for tests / simulator)

        /// <summary>True when the build uses any B3 system (a body must then be bound for blasts / chains to see it).</summary>
        public static bool Needed(in TreeStats s) =>
            s.Elements != TreeElement.None || s.Core != TreeChaosCore.None || s.ChainChance > 0f || s.ExplosiveRadius > 0f
            || s.NovaCooldown > 0f || s.BellCooldown > 0f || s.CritRingRadius > 0f || (s.Synergies & TreeSynergy.Barrage) != 0;

        /// <summary>Chance that a death of generation <paramref name="deathDepth"/> blasts: ChainChance × falloff^depth (Thunder Cascade ×0.7).</summary>
        public static float ChainChanceAt(in TreeStats s, int deathDepth)
        {
            float falloff = (s.Synergies & TreeSynergy.ThunderCascade) != 0 ? TreeSystemsTuning.ThunderCascadeFalloff
                : s.ChainFalloff > 0f ? s.ChainFalloff : TreeSystemsTuning.ChainFalloffDefault;
            float chance = s.ChainChance;
            for (int d = 0; d < deathDepth; d++) chance *= falloff;
            return chance;
        }

        /// <summary>Hits needed to arm the nuke: N = k × pulls per second (smg_1 150, ar_1 100, sg_1 12, sr_1 8, rl_1 4), at least 1.</summary>
        public static float NukeHitsNeeded(in TreeStats s, float pullsPerSecond) =>
            Mathf.Max(1f, s.NukeHitsPerPullRate * Mathf.Max(0f, pullsPerSecond));

        // ------------------------------------------------------------------ internals

        private static void CountNuke(in TreeStats s, Vector3 point, in TreeHitFacts facts)
        {
            if (s.NukeHitsPerPullRate <= 0f || s.NukeRadius <= 0f) return;

            if (s_nukeArmed)
            {
                if (Now < s_nukeReadyAt - 1e-4f) return;   // GD: the counter waits for the 3 s cap
                s_nukeArmed = false;
                s_nukeCounter = 0f;
                s_nukeReadyAt = Now + TreeGuardrails.NukeCooldownSeconds;
                var r = new TreeBlastRequest
                {
                    Kind = TreeProcKind.MiniNuke, Point = point, Radius = s.NukeRadius, Damage = s.NukeShare * s.WeaponPower,
                    StunSeconds = s.NukeStunSeconds, StunKind = TreeStunKind.Nuke,
                    Synergy = (s.Synergies & TreeSynergy.CriticalMeltdown) != 0 ? TreeSynergy.CriticalMeltdown : TreeSynergy.None
                };
                if (TreeBlastQueue.Enqueue(in r)) Nukes++;
                return;
            }

            float weight = facts.HitWeight > 0f ? facts.HitWeight : 1f;
            if (facts.Crit && (s.Synergies & TreeSynergy.CriticalMeltdown) != 0) weight *= TreeSystemsTuning.MeltdownCritCount;
            s_nukeCounter += weight;
            if (s_nukeCounter + 1e-4f >= NukeHitsNeeded(in s, facts.PullsPerSecond)) s_nukeArmed = true;
        }

        private static void TickAnchorPulses(float now)
        {
            if (!s_hasAnchor || now < s_nextAnchorCheck) return;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            bool nova = s.NovaCooldown > 0f && s.NovaRadius > 0f && now >= s_novaReadyAt;
            bool bell = s.BellCooldown > 0f && s.BellRadius > 0f && now >= s_bellReadyAt;
            if (!nova && !bell) return;
            s_nextAnchorCheck = now + TreeSystemsTuning.AnchorCheckSeconds;

            if (nova && AnyWithin(s_anchor, s.NovaRadius))
            {
                var r = new TreeBlastRequest
                {
                    Kind = TreeProcKind.ElementNova, Point = s_anchor, Radius = s.NovaRadius,
                    Damage = s.NovaShare * s.WeaponPower * TreeStatusSystem.ElementScale(in s), Element = s.Elements, ApplyElementStates = true
                };
                if (TreeBlastQueue.Enqueue(in r))
                {
                    Novas++;
                    s_novaReadyAt = now + s.NovaCooldown;
                }
            }

            if (bell && AnyWithin(s_anchor, s.BellRadius))
            {
                var r = new TreeBlastRequest
                {
                    Kind = TreeProcKind.DoomsdayBell, Point = s_anchor, Radius = s.BellRadius, Damage = s.BellShare * s.WeaponPower,
                    StunSeconds = s.BellStunSeconds, StunKind = TreeStunKind.Bell
                };
                if (TreeBlastQueue.Enqueue(in r))
                {
                    Bells++;
                    s_bellReadyAt = now + s.BellCooldown;
                }
            }
        }

        private static readonly ITreeStatusReceiver[] s_probe = new ITreeStatusReceiver[1];

        private static bool AnyWithin(Vector3 center, float radius)
        {
            int n = TreeStatusSystem.Nearest(center, radius, s_probe, 1);
            s_probe[0] = null;
            return n > 0;
        }

        /// <summary>One owned element per hit, in turn (bit order Fire, Ice, Shock, Venom).</summary>
        private static TreeElement NextElement(TreeElement owned)
        {
            int count = 0;
            for (int b = 0; b < 4; b++)
            {
                if (((int)owned & (1 << b)) != 0) count++;
            }

            if (count <= 1) return owned;
            int pick = s_elementTurn++ % count;
            if (s_elementTurn < 0) s_elementTurn = 0;
            for (int b = 0; b < 4; b++)
            {
                if (((int)owned & (1 << b)) == 0) continue;
                if (pick-- == 0) return (TreeElement)(1 << b);
            }

            return owned;
        }

        /// <summary>xorshift32 in [0, 1): the run's own stream (UnityEngine.Random is never consumed).</summary>
        private static float NextRoll()
        {
            uint x = s_rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            s_rng = x;
            return (x >> 8) * (1f / 16777216f);
        }

        private static void ResetShockWindow()
        {
            for (int i = 0; i < s_shockWindow.Length; i++) s_shockWindow[i] = float.NegativeInfinity;
            s_shockNext = 0;
        }

        /// <summary>Raises one <see cref="TreeEvents.Proc"/> (tier from the branch ladder; target only for the callback).</summary>
        internal static void Raise(TreeProcKind kind, Vector3 point, float radius, float damage, TreeElement element, TreeBranch branch,
            TreeSynergy synergy, ITreeStatusReceiver target, int depth, float seconds = 0f)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            var info = new TreeProcInfo
            {
                Kind = kind, Point = point, Radius = radius, Damage = damage, Element = element,
                Tier = (s.BranchTiers >> ((int)branch * 3)) & 7, Synergy = synergy, Weapon = CurrentWeapon,
                Target = target != null ? target.Unit : null, Depth = depth, Seconds = seconds
            };
            TreeEvents.RaiseProc(in info);
        }

        // ------------------------------------------------------------------ F1

        public static TreeSystemsCounters Counters => new TreeSystemsCounters
        {
            ActiveSlots = TreeStatusSystem.ActiveSlots,
            StatusBodies = TreeStatusSystem.StatusBodies,
            QueueCount = TreeBlastQueue.Count,
            Merged = TreeBlastQueue.Merged,
            Quiet = TreeBlastQueue.Quiet,
            Presented = TreeBlastQueue.Presented,
            Detonated = TreeBlastQueue.Detonated,
            QueriesLastFrame = TreeTargetQuery.LastFrameUsed,
            PeakQueriesPerFrame = TreeTargetQuery.PeakPerFrame,
            Zones = TreeAreaPool.Count,
            Wells = TreeGravityWells.ActiveCount,
            DepthRefused = TreeBlastQueue.DepthRefused,
            NestedDetonations = TreeBlastQueue.NestedDetonations
        };

        /// <summary>F1 text (allocates; panel only).</summary>
        public static string DebugLine()
        {
            TreeSystemsCounters c = Counters;
            var sb = new StringBuilder(160);
            sb.Append("slots ").Append(c.ActiveSlots).Append('/').Append(TreeGuardrails.StatusSlots)
                .Append(" · status ").Append(c.StatusBodies)
                .Append(" · queue ").Append(c.QueueCount).Append('/').Append(TreeGuardrails.TreeBlastQueue)
                .Append(" · merged ").Append(c.Merged)
                .Append(" · quiet ").Append(c.Quiet)
                .Append(" · overlap/frame ").Append(c.QueriesLastFrame).Append(" (peak ").Append(c.PeakQueriesPerFrame).Append('/')
                .Append(TreeGuardrails.OverlapQueriesPerFrame).Append(')')
                .Append(" · zones ").Append(c.Zones)
                .Append(" · wells ").Append(c.Wells)
                .Append(" · nuke ").Append(s_nukeArmed ? "armed" : s_nukeCounter.ToString("0", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
