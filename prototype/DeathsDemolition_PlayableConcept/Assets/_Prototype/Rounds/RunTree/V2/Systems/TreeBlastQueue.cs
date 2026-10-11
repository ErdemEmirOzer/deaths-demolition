using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>WP-104 B3 · one tree blast as a caller asks for it. Plain struct; zero / false fields are "off".</summary>
    public struct TreeBlastRequest
    {
        /// <summary>ExplosiveRound, ClusterSplit, CritRing, ChainBlast, MiniNuke, Shatter, ElementNova, DoomsdayBell, BlackHoleCollapse.</summary>
        public TreeProcKind Kind;
        public Vector3 Point;
        public float Radius;
        /// <summary>Flat damage to every body inside (no falloff: the design numbers are "N×SG in r m").</summary>
        public float Damage;
        public TreeElement Element;
        /// <summary>Chain generation (0 = direct). Requests deeper than <see cref="TreeGuardrails.ChainDepth"/> are refused.</summary>
        public int Depth;
        /// <summary>Raw stun seconds (the body applies StunHeavyShare to Elite / Ultra, S2).</summary>
        public float StunSeconds;
        /// <summary>S-03's stun source (Nuke / Bell / Bulwark); read only when <see cref="StunSeconds"/> &gt; 0.</summary>
        public TreeStunKind StunKind;
        /// <summary>Cold stacks left on the survivors (Shatter 2).</summary>
        public int IceStacks;
        /// <summary>Damage multiplier on frozen bodies (Frozen Gravity collapse 1.5; ≤ 1 = off).</summary>
        public float FrozenBonus;
        /// <summary>Element Nova: the full stack / state of <see cref="Element"/> on the survivors.</summary>
        public bool ApplyElementStates;
        /// <summary>Wildfire Rounds: leaves a fire ground at the point.</summary>
        public bool FireGround;
        public TreeSynergy Synergy;
    }

    /// <summary>
    /// WP-104 B3 (GDEV §2 #5 #18 #20, (c), §3) · EVERY tree-caused blast goes through here: Explosive Rounds, Barrage cluster
    /// parts, crit-kill rings, Chain Reaction death blasts, Mini Nuke, Shatter / Frozen Gravity ice blasts, Element Nova,
    /// Doomsday Bell, black hole collapse.
    ///
    /// <para><b>Reentrancy.</b> Nothing detonates where it is asked for: a request (from a hit, a death callback, a zone or a
    /// well) only appends to a fixed queue of <see cref="TreeGuardrails.TreeBlastQueue"/> (32). The queue is drained by
    /// <see cref="TreeSystemsRunner.Tick"/> after the projectile step; a death inside a detonation queues its own blast for
    /// the next frame (only blasts pending at the start of a flush may go off in it), so chains ripple one generation per
    /// frame and no overlap loop ever runs inside another (HSE <c>OverlapEnemies</c> rule).</para>
    ///
    /// <para><b>Caps (K6: power merges, never drops).</b> Presented blasts (proc event → VFX / audio) ≤
    /// <see cref="TreeGuardrails.TreeBlastsPerFrame"/> per frame and ≤ <see cref="TreeGuardrails.TreeBlastsPerSecond"/> in any
    /// 1 s window; each takes one area query of the frame budget (<see cref="TreeTargetQuery"/>, ≤ 8). When more are pending
    /// than the frame may present, blasts within <see cref="TreeSystemsTuning.BlastMergeDistance"/> of each other fuse first
    /// (damage summed, radius max). A full queue fuses the newcomer into the nearest pending blast. A blast that waited
    /// <see cref="TreeSystemsTuning.BlastMaxWaitSeconds"/> lands quietly (damage only, no proc; GD §8.3) with what is left of
    /// the query budget. Damage in = damage out (<see cref="DamageEnqueued"/> / <see cref="DamageDetonated"/>).</para>
    ///
    /// <para><b>Mass deaths.</b> A blast reaching more than <see cref="TreeSystemsTuning.MassDeathChunk"/> bodies hits them
    /// over <see cref="TreeGuardrails.MassDeathSpreadFrames"/> frames (one transaction, same damage each), so corpses, ragdolls
    /// and gold orbs of a nuke do not land on one frame (GDEV §6 risk 4).</para>
    ///
    /// Targets come from the status registry (<see cref="TreeStatusSystem.Query"/>, ≤ 64 bodies, no PhysX at any radius).
    /// No allocation; <see cref="TreeMarkers.BlastQueue"/> (+ <see cref="TreeMarkers.Nuke"/> around a nuke).
    /// </summary>
    public static class TreeBlastQueue
    {
        private const int Capacity = TreeGuardrails.TreeBlastQueue;
        private const int WindowSize = TreeGuardrails.TreeBlastsPerSecond;
        private const float Eps = 1e-4f;

        private struct Blast
        {
            public TreeBlastRequest R;
            public float EnqueuedAt;
        }

        private static readonly Blast[] s_queue = new Blast[Capacity];
        private static int s_count;
        private static readonly float[] s_window = new float[WindowSize];
        private static int s_windowNext;
        private static readonly ITreeStatusReceiver[] s_targets = new ITreeStatusReceiver[TreeGuardrails.StatusSlots];
        private static readonly ITreeStatusReceiver[] s_bolts = new ITreeStatusReceiver[TreeGuardrails.StatusSlots];

        // a mass-death application in progress (at most one; a second one finishes it first)
        private static readonly ITreeStatusReceiver[] s_spread = new ITreeStatusReceiver[TreeGuardrails.StatusSlots];
        private static readonly int[] s_spreadGen = new int[TreeGuardrails.StatusSlots];   // a re-bound adapter is skipped
        private static int s_spreadCount, s_spreadNext, s_spreadPerFrame, s_spreadTransaction;
        private static TreeBlastRequest s_spreadBlast;
        private static bool s_spreadActive;

        private static int s_detonating;
        private static int s_barrageCounter;

        static TreeBlastQueue()
        {
            ResetWindow();
        }

        // ------------------------------------------------------------------ readouts (tests / F1)

        public static int Count => s_count;
        public static int Enqueued { get; private set; }
        public static int Detonated { get; private set; }
        public static int Presented { get; private set; }
        public static int Quiet { get; private set; }
        /// <summary>Blasts fused into another (overflow + same-spot), K6.</summary>
        public static int Merged { get; private set; }
        public static int OverflowMerges { get; private set; }
        public static int DepthRefused { get; private set; }
        /// <summary>A detonation that started inside another one. Must stay 0 (reentrancy rule).</summary>
        public static int NestedDetonations { get; private set; }
        public static int PresentedThisFrame { get; private set; }
        public static int PeakPresentedPerFrame { get; private set; }
        public static int MaxDepthDetonated { get; private set; }
        public static float DamageEnqueued { get; private set; }
        public static float DamageDetonated { get; private set; }
        public static int BodiesHit { get; private set; }
        public static int BoltsThrown { get; private set; }
        public static bool SpreadActive => s_spreadActive;
        /// <summary>Presented blasts inside the last second (window), as of the last flush.</summary>
        public static int PresentedLastSecond(float now) => WindowSize - WindowFree(now);

        // ------------------------------------------------------------------ requests

        /// <summary>Queues a blast. False when it carries nothing or is deeper than the chain guardrail.</summary>
        public static bool Enqueue(in TreeBlastRequest request)
        {
            if (request.Radius <= 0f) return false;
            if (request.Damage <= 0f && request.StunSeconds <= 0f && request.IceStacks <= 0 && !request.ApplyElementStates) return false;
            if (request.Depth > TreeGuardrails.ChainDepth)
            {
                DepthRefused++;
                return false;
            }

            TreeSystemsDriver.EnsureExists();
            Enqueued++;
            DamageEnqueued += Mathf.Max(0f, request.Damage);

            if (s_count < Capacity)
            {
                s_queue[s_count++] = new Blast { R = request, EnqueuedAt = TreeSystemsRunner.Now };
                return true;
            }

            // full: K6 — fuse into the nearest pending blast (damage summed, radius max)
            int nearest = NearestPending(request.Point, -1);
            Fuse(ref s_queue[nearest].R, in request);
            OverflowMerges++;
            Merged++;
            return true;
        }

        public static bool Enqueue(TreeProcKind kind, Vector3 point, float radius, float damage, TreeElement element = TreeElement.None, int depth = 0)
        {
            var r = new TreeBlastRequest { Kind = kind, Point = point, Radius = radius, Damage = damage, Element = element, Depth = depth };
            return Enqueue(in r);
        }

        /// <summary>S-02: an Explosive Rounds hit (bullet weapons). Area damage = shot damage × ExplosiveAreaShare; Wildfire Rounds lights the ground.</summary>
        public static bool EnqueueExplosiveRound(Vector3 point, float shotDamage, int depth = 0)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s.ExplosiveRadius <= 0f || s.ExplosiveAreaShare <= 0f || shotDamage <= 0f) return false;
            bool wildfire = (s.Synergies & TreeSynergy.WildfireRounds) != 0;
            var r = new TreeBlastRequest
            {
                Kind = TreeProcKind.ExplosiveRound, Point = point, Radius = s.ExplosiveRadius, Damage = shotDamage * s.ExplosiveAreaShare,
                Depth = depth, FireGround = wildfire, Synergy = wildfire ? TreeSynergy.WildfireRounds : TreeSynergy.None
            };
            return Enqueue(in r);
        }

        /// <summary>S-02: a crit kill (Fortune's Edge): a ring of CritRingShare × the hit's damage.</summary>
        public static bool EnqueueCritRing(Vector3 point, float hitDamage)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s.CritRingRadius <= 0f || s.CritRingShare <= 0f || hitDamage <= 0f) return false;
            return Enqueue(TreeProcKind.CritRing, point, s.CritRingRadius, hitDamage * s.CritRingShare);
        }

        /// <summary>
        /// S-02: every rocket impact while Barrage is live; every <see cref="TreeSystemsTuning.BarrageEvery"/>th one splits into
        /// <see cref="TreeSystemsTuning.BarrageParts"/> cluster parts around the impact. Returns the parts queued.
        /// </summary>
        public static int OnBarrageRocketImpact(Vector3 point, float rocketDamage)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if ((s.Synergies & TreeSynergy.Barrage) == 0 || rocketDamage <= 0f) return 0;
            s_barrageCounter++;
            if (s_barrageCounter < TreeSystemsTuning.BarrageEvery) return 0;
            s_barrageCounter = 0;

            int queued = 0;
            for (int p = 0; p < TreeSystemsTuning.BarrageParts; p++)
            {
                float angle = p * (Mathf.PI * 2f / TreeSystemsTuning.BarrageParts);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * TreeSystemsTuning.BarragePartOffset;
                var r = new TreeBlastRequest
                {
                    Kind = TreeProcKind.ClusterSplit, Point = point + offset, Radius = TreeSystemsTuning.BarragePartRadius,
                    Damage = rocketDamage * TreeSystemsTuning.BarragePartShare, Synergy = TreeSynergy.Barrage
                };
                if (Enqueue(in r)) queued++;
            }

            return queued;
        }

        // ------------------------------------------------------------------ drain (TreeSystemsRunner.Tick)

        /// <summary>Presented detonations of this frame (oldest first, within the frame / second / query budgets).</summary>
        internal static void FlushPresented(float now)
        {
            PresentedThisFrame = 0;
            if (s_spreadActive) ContinueSpread();
            if (s_count == 0) return;

            using (TreeMarkers.BlastQueue.Auto())
            {
                int budget = Mathf.Min(TreeGuardrails.TreeBlastsPerFrame, Mathf.Min(WindowFree(now), TreeTargetQuery.Remaining));
                if (budget <= 0) return;
                if (s_count > budget) FuseSameSpot(budget);

                int eligible = Mathf.Min(budget, s_count);   // blasts queued during this flush wait for the next frame
                for (int k = 0; k < eligible && s_count > 0; k++)
                {
                    if (!TreeTargetQuery.TryUse()) break;
                    TreeBlastRequest b = PopHead();
                    s_window[s_windowNext] = now;
                    s_windowNext = (s_windowNext + 1) % WindowSize;
                    PresentedThisFrame++;
                    if (PresentedThisFrame > PeakPresentedPerFrame) PeakPresentedPerFrame = PresentedThisFrame;
                    Detonate(in b, true);
                }
            }
        }

        /// <summary>Blasts that waited past the cap: damage only, with the query budget left this frame.</summary>
        internal static void FlushQuiet(float now)
        {
            if (s_count == 0) return;
            using (TreeMarkers.BlastQueue.Auto())
            {
                int eligible = s_count;
                for (int k = 0; k < eligible && s_count > 0; k++)
                {
                    if (now - s_queue[0].EnqueuedAt < TreeSystemsTuning.BlastMaxWaitSeconds - Eps) break;
                    if (!TreeTargetQuery.TryUse()) break;
                    TreeBlastRequest b = PopHead();
                    Detonate(in b, false);
                }
            }
        }

        // ------------------------------------------------------------------ detonation

        private static void Detonate(in TreeBlastRequest b, bool presented)
        {
            s_detonating++;
            if (s_detonating > 1) NestedDetonations++;
            bool nuke = b.Kind == TreeProcKind.MiniNuke;
            if (nuke) TreeMarkers.Nuke.Begin();
            try
            {
                Detonated++;
                if (presented) Presented++;
                else Quiet++;
                DamageDetonated += Mathf.Max(0f, b.Damage);
                if (b.Depth > MaxDepthDetonated) MaxDepthDetonated = b.Depth;

                int n = TreeStatusSystem.Query(b.Point, b.Radius, s_targets);
                if (presented)
                {
                    TreeSystemsRunner.Raise(b.Kind, b.Point, b.Radius, b.Damage, b.Element, BranchOf(b.Kind), b.Synergy, null, b.Depth);
                }

                if (n > TreeSystemsTuning.MassDeathChunk)
                {
                    if (s_spreadActive) FinishSpread();
                    for (int k = 0; k < n; k++)
                    {
                        s_spread[k] = s_targets[k];
                        s_spreadGen[k] = TreeStatusSystem.GenerationOf(s_targets[k]);
                    }
                    s_spreadCount = n;
                    s_spreadNext = 0;
                    s_spreadPerFrame = (n + TreeGuardrails.MassDeathSpreadFrames - 1) / TreeGuardrails.MassDeathSpreadFrames;
                    s_spreadBlast = b;
                    s_spreadTransaction = DamageTransaction.NextId();
                    s_spreadActive = true;
                    ContinueSpread();
                }
                else if (n > 0)
                {
                    ApplyRange(in b, s_targets, null, 0, n, DamageTransaction.NextId());
                }

                for (int k = 0; k < n; k++) s_targets[k] = null;

                if (b.FireGround)
                {
                    ref readonly TreeStats s = ref TreeRuntime.Current;
                    float dps = s.FireDotShare * s.WeaponPower * TreeStatusSystem.ElementScale(in s);
                    TreeAreaPool.Ignite(TreeAreaKind.Fire, b.Point, TreeSystemsTuning.WildfireRoundsRadius, TreeSystemsTuning.WildfireRoundsSeconds, dps, b.Depth);
                }

                if (b.Kind == TreeProcKind.ChainBlast && (TreeRuntime.Current.Synergies & TreeSynergy.ThunderCascade) != 0) Bolts(in b);
            }
            finally
            {
                if (nuke) TreeMarkers.Nuke.End();
                s_detonating--;
            }
        }

        private static void ApplyRange(in TreeBlastRequest b, ITreeStatusReceiver[] targets, int[] generations, int from, int to, int transaction)
        {
            bool owned = TreeDamageContext.BeginTransaction(transaction);   // one transaction per blast: each body once
            TreeStatusSystem.BeginBatch();   // a death inside frees its slot after the loop (12.10 (a))
            try
            {
                for (int k = from; k < to; k++)
                {
                    ITreeStatusReceiver body = targets[k];
                    if (body == null || !body.IsAlive) continue;
                    if (generations != null && TreeStatusSystem.GenerationOf(body) != generations[k]) continue;   // another life now

                    float damage = b.Damage;
                    if (b.FrozenBonus > 1f && body.IsFrozen) damage *= b.FrozenBonus;
                    if (damage > 0f)
                    {
                        BodiesHit++;
                        TreeDamageContext.Explosion(body, damage, b.Element, b.Depth, b.Kind, b.Point);
                    }

                    if (!body.IsAlive) continue;   // died: OnBodyDied only queued
                    if (b.StunSeconds > 0f) body.ApplyStun(b.StunSeconds, b.StunKind);
                    if (b.IceStacks > 0) TreeStatusSystem.ApplyIce(body, b.IceStacks);
                    if (b.ApplyElementStates) TreeStatusSystem.ApplyFullStates(body, b.Element, b.Point);
                }
            }
            finally
            {
                TreeStatusSystem.EndBatch();
                TreeDamageContext.EndTransaction(owned);
            }
        }

        private static void ContinueSpread()
        {
            if (!s_spreadActive) return;
            int to = Mathf.Min(s_spreadCount, s_spreadNext + Mathf.Max(1, s_spreadPerFrame));
            ApplyRange(in s_spreadBlast, s_spread, s_spreadGen, s_spreadNext, to, s_spreadTransaction);
            s_spreadNext = to;
            if (s_spreadNext >= s_spreadCount) EndSpread();
        }

        private static void FinishSpread()
        {
            if (!s_spreadActive) return;
            ApplyRange(in s_spreadBlast, s_spread, s_spreadGen, s_spreadNext, s_spreadCount, s_spreadTransaction);
            EndSpread();
        }

        private static void EndSpread()
        {
            for (int k = 0; k < s_spreadCount; k++)
            {
                s_spread[k] = null;
                s_spreadGen[k] = 0;
            }
            s_spreadCount = 0;
            s_spreadNext = 0;
            s_spreadActive = false;
            s_spreadBlast = default;
        }

        /// <summary>Thunder Cascade: each chain death blast throws bolts at the nearest living bodies (shock events ≤ 6/s).</summary>
        private static void Bolts(in TreeBlastRequest b)
        {
            if (!TreeTargetQuery.TryUse()) return;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            float damage = TreeSystemsTuning.ThunderCascadeShare * s.WeaponPower * TreeStatusSystem.ElementScale(in s);
            int n = TreeStatusSystem.Nearest(b.Point, TreeSystemsTuning.ThunderCascadeReach, s_bolts, TreeSystemsTuning.ThunderCascadeBolts);
            bool owned = TreeDamageContext.BeginTransaction(0);   // the bolts are their own attack (a blast survivor takes them too)
            TreeStatusSystem.BeginBatch();
            try
            {
                for (int k = 0; k < n; k++)
                {
                    ITreeStatusReceiver body = s_bolts[k];
                    s_bolts[k] = null;
                    if (body == null || !body.IsAlive || !TreeSystemsRunner.TryShockEvent()) continue;
                    BoltsThrown++;
                    TreeSystemsRunner.Raise(TreeProcKind.ShockChain, b.Point, 0f, damage, TreeElement.Shock, TreeBranch.Element, TreeSynergy.ThunderCascade, body, b.Depth);
                    TreeDamageContext.Status(body, damage, TreeElement.Shock, b.Depth, TreeDamageKind.Bolt, TreeProcKind.ShockChain, b.Point);
                    if (body.IsAlive) TreeStatusSystem.ApplyShockStagger(body, b.Point);
                }
            }
            finally
            {
                TreeStatusSystem.EndBatch();
                TreeDamageContext.EndTransaction(owned);
                for (int k = 0; k < n; k++) s_bolts[k] = null;
            }
        }

        // ------------------------------------------------------------------ queue bookkeeping

        private static TreeBlastRequest PopHead()
        {
            TreeBlastRequest head = s_queue[0].R;
            for (int i = 1; i < s_count; i++) s_queue[i - 1] = s_queue[i];
            s_queue[--s_count] = default;
            return head;
        }

        /// <summary>Over budget: fuse pending blasts that sit within the merge distance (older keeps its place).</summary>
        private static void FuseSameSpot(int budget)
        {
            float d2 = TreeSystemsTuning.BlastMergeDistance * TreeSystemsTuning.BlastMergeDistance;
            for (int i = 0; i < s_count && s_count > budget; i++)
            {
                for (int j = s_count - 1; j > i && s_count > budget; j--)
                {
                    Vector3 a = s_queue[i].R.Point, c = s_queue[j].R.Point;
                    float dx = a.x - c.x, dz = a.z - c.z;
                    if (dx * dx + dz * dz > d2) continue;
                    Fuse(ref s_queue[i].R, in s_queue[j].R);
                    RemoveAt(j);
                    Merged++;
                }
            }
        }

        private static void Fuse(ref TreeBlastRequest into, in TreeBlastRequest from)
        {
            if (Priority(from.Kind) > Priority(into.Kind))
            {
                into.Kind = from.Kind;
                into.Point = from.Point;
            }

            into.Damage = Mathf.Max(0f, into.Damage) + Mathf.Max(0f, from.Damage);
            into.Radius = Mathf.Max(into.Radius, from.Radius);
            into.Element |= from.Element;
            into.Depth = Mathf.Max(into.Depth, from.Depth);
            if (from.StunSeconds > into.StunSeconds) into.StunKind = from.StunKind;
            into.StunSeconds = Mathf.Max(into.StunSeconds, from.StunSeconds);
            into.IceStacks = Mathf.Max(into.IceStacks, from.IceStacks);
            into.FrozenBonus = Mathf.Max(into.FrozenBonus, from.FrozenBonus);
            into.ApplyElementStates |= from.ApplyElementStates;
            into.FireGround |= from.FireGround;
            into.Synergy |= from.Synergy;
        }

        private static int Priority(TreeProcKind kind)
        {
            switch (kind)
            {
                case TreeProcKind.MiniNuke: return 8;
                case TreeProcKind.DoomsdayBell: return 7;
                case TreeProcKind.ElementNova: return 6;
                case TreeProcKind.BlackHoleCollapse: return 5;
                case TreeProcKind.Shatter: return 4;
                case TreeProcKind.ChainBlast: return 3;
                case TreeProcKind.ExplosiveRound: return 2;
                case TreeProcKind.ClusterSplit: return 1;
                default: return 0;
            }
        }

        private static TreeBranch BranchOf(TreeProcKind kind)
        {
            switch (kind)
            {
                case TreeProcKind.ExplosiveRound:
                case TreeProcKind.ClusterSplit:
                    return TreeBranch.Shot;
                case TreeProcKind.Shatter:
                case TreeProcKind.ElementNova:
                    return TreeBranch.Element;
                default:
                    return TreeBranch.Chaos;
            }
        }

        private static int NearestPending(Vector3 point, int skip)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < s_count; i++)
            {
                if (i == skip) continue;
                Vector3 p = s_queue[i].R.Point;
                float dx = p.x - point.x, dz = p.z - point.z;
                float d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }

        private static void RemoveAt(int index)
        {
            for (int i = index + 1; i < s_count; i++) s_queue[i - 1] = s_queue[i];
            s_queue[--s_count] = default;
        }

        private static int WindowFree(float now)
        {
            int used = 0;
            for (int i = 0; i < WindowSize; i++)
            {
                if (now - s_window[i] < 1f - Eps) used++;
            }

            return WindowSize - used;
        }

        private static void ResetWindow()
        {
            for (int i = 0; i < WindowSize; i++) s_window[i] = float.NegativeInfinity;
            s_windowNext = 0;
        }

        // ------------------------------------------------------------------ reset

        internal static void Reset()
        {
            for (int i = 0; i < Capacity; i++) s_queue[i] = default;
            for (int i = 0; i < s_targets.Length; i++)
            {
                s_targets[i] = null;
                s_bolts[i] = null;
                s_spread[i] = null;
            }

            s_count = 0;
            s_spreadActive = false;
            s_spreadCount = 0;
            s_spreadNext = 0;
            s_spreadBlast = default;
            s_detonating = 0;
            s_barrageCounter = 0;
            ResetWindow();
            Enqueued = 0;
            Detonated = 0;
            Presented = 0;
            Quiet = 0;
            Merged = 0;
            OverflowMerges = 0;
            DepthRefused = 0;
            NestedDetonations = 0;
            PresentedThisFrame = 0;
            PeakPresentedPerFrame = 0;
            MaxDepthDetonated = 0;
            DamageEnqueued = 0f;
            DamageDetonated = 0f;
            BodiesHit = 0;
            BoltsThrown = 0;
        }
    }
}
