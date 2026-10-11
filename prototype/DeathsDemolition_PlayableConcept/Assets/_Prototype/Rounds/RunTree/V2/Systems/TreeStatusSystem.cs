using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B3 (GDEV §2 #14 #15 #17, (d) (e); C-00 12.10 alignment with S-03's push model) · the one manager of the
    /// statuses S-04 owns on a body: burn DoT and its Inferno spread, venom stacks → DoT and the Corrosion threshold, and the
    /// fire / venom vulnerability scale. Cold, freeze, stun and pull state live on the body (S-03): this system only routes
    /// them (<see cref="ApplyIce"/>, <see cref="ApplyShockStagger"/>) and raises the proc events.
    ///
    /// Data: <see cref="TreeGuardrails.StatusSlots"/> (64) fixed slots of a plain struct, a free list and two 64-bit masks
    /// (used / has a status). A body gets a STABLE slot on registration (the <c>ZombieEnemy.Live</c> index is not stable);
    /// death or despawn frees it. Fixed indices never move, so a death inside a tick only clears its own slot. The slot table
    /// is also the tree's target registry (<see cref="Query"/>: no PhysX at any radius).
    ///
    /// Tick: <see cref="TreeGuardrails.StatusTickHz"/> (4 Hz) per slot, slots split over <see cref="TreeGuardrails.StatusPhases"/>
    /// (4) phases (slot % 4), one phase every 1/16 s, so a frame touches about a quarter of the burning bodies. A slot's tick
    /// is ONE <see cref="ITreeStatusReceiver.ApplyStatusDamage"/> call (fire + venom summed; element = the larger share of that
    /// tick, C-00 12.10 (b)) inside one damage transaction per phase step, then the time counts down: totals are exactly
    /// dps × seconds whatever the phase. A death inside any system loop (a batch) frees its slot after the loop (12.10 (a)).
    /// The body's single status tint is chosen here (frozen &gt; burning &gt; venom &gt; cold stacks) and sent on change.
    /// No allocation; <see cref="TreeMarkers.Status"/>.
    /// </summary>
    public static class TreeStatusSystem
    {
        public const int NoSlot = -1;

        // 64 slots = one ulong mask. TreeGuardrails.StatusSlots must stay ≤ 64 (TreeSystemsTests.StatusSlots_FitOneMask).
        private const int Cap = TreeGuardrails.StatusSlots;
        private const int Phases = TreeGuardrails.StatusPhases;
        private const float SlotTick = 1f / TreeGuardrails.StatusTickHz;
        private const float PhaseStep = 1f / (TreeGuardrails.StatusTickHz * TreeGuardrails.StatusPhases);
        private const float Eps = 1e-4f;

        private struct BodyStatus
        {
            public float BurnDps, BurnLeft, NextSpreadAt;
            public bool SpreadDue;
            public int VenomStacks;
            public float VenomLeft, VenomStackDps;
            public TreeElement TintElement;
            public float TintStrength;
        }

        private static readonly ITreeStatusReceiver[] s_bodies = new ITreeStatusReceiver[Cap];
        private static readonly BodyStatus[] s_status = new BodyStatus[Cap];
        private static readonly int[] s_free = new int[Cap];
        // registration serial per slot: a pooled adapter re-bound to a new body is a new generation (multi-frame holders check it)
        private static readonly int[] s_generation = new int[Cap];
        private static int s_nextGeneration;
        private static int s_freeCount;
        private static ulong s_usedMask, s_activeMask, s_tintMask;
        // C-00 12.10 (a): a death inside a batch (TreeStatusEnded fires inside a lethal ApplyStatusDamage) releases its slot
        // only when the outermost batch ends — no slot changes under an iterator. A mask, no list, no allocation.
        private static ulong s_pendingRelease;
        private static int s_batch;
        private static int s_phase;
        private static float s_accumulator;
        private static float s_now;
        private static int s_spreadPending;

        static TreeStatusSystem()
        {
            ResetTable();
        }

        // ------------------------------------------------------------------ readouts (tests / F1)

        public static int Capacity => Cap;
        public static int ActiveSlots { get; private set; }
        public static int PeakSlots { get; private set; }
        /// <summary>Registrations refused because all 64 slots were taken (the body then takes no tree status / area damage).</summary>
        public static int RegisterRefused { get; private set; }
        /// <summary>Slots freed by the tick because the body was no longer alive (a missed TreeStatusEnded).</summary>
        public static int StaleReleased { get; private set; }
        public static int Ignites { get; private set; }
        public static int Spreads { get; private set; }
        public static int IceApplied { get; private set; }
        public static int Freezes { get; private set; }
        public static int UltraSlows { get; private set; }
        public static int DotTicks { get; private set; }
        public static float DotDamage { get; private set; }
        /// <summary>Slots holding a burn or venom right now.</summary>
        public static int StatusBodies => PopCount(s_activeMask);
        /// <summary>Slot releases waiting for the end of the running batch.</summary>
        public static int PendingReleases => PopCount(s_pendingRelease);
        /// <summary>Releases that were deferred to a batch end since the last reset (tests).</summary>
        public static int DeferredReleases { get; private set; }
        /// <summary>The phase the next 1/16 s step runs (0–3).</summary>
        public static int NextPhase => s_phase;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Gives the body a stable slot (idempotent). False when the table is full.</summary>
        public static bool Register(ITreeStatusReceiver body)
        {
            if (body == null || !body.IsAlive) return false;
            int known = SlotOf(body);
            if (known >= 0)
            {
                if ((s_pendingRelease & (1UL << known)) == 0UL) return true;
                ReleaseNow(known);   // a re-bound adapter whose old life is still waiting: free it first, then a fresh slot
            }

            TreeSystemsDriver.EnsureExists();
            if (s_freeCount == 0)
            {
                RegisterRefused++;
                body.TreeSlot = NoSlot;
                return false;
            }

            int slot = s_free[--s_freeCount];
            s_bodies[slot] = body;
            s_status[slot] = default;
            s_generation[slot] = ++s_nextGeneration;
            s_usedMask |= 1UL << slot;
            body.TreeSlot = slot;
            ActiveSlots++;
            if (ActiveSlots > PeakSlots) PeakSlots = ActiveSlots;
            return true;
        }

        /// <summary>Frees the body's slot (despawn without death; a death goes through <see cref="TreeSystemsRunner.OnBodyDied"/>).</summary>
        public static void Release(ITreeStatusReceiver body)
        {
            int slot = SlotOf(body);
            if (slot < 0) return;
            ReleaseSlot(slot);
        }

        /// <summary>Opens a batch (nestable): slot releases inside it wait for the outermost <see cref="EndBatch"/>.</summary>
        public static void BeginBatch() => s_batch++;

        /// <summary>Closes a batch; the outermost one frees the slots of the bodies that died inside it.</summary>
        public static void EndBatch()
        {
            if (s_batch > 0) s_batch--;
            if (s_batch > 0 || s_pendingRelease == 0UL) return;
            for (int i = 0; i < Cap; i++)
            {
                if ((s_pendingRelease & (1UL << i)) != 0UL) ReleaseNow(i);
            }

            s_pendingRelease = 0UL;
        }

        /// <summary>The body's validated slot, or <see cref="NoSlot"/> (a stale number left from a reset never matches).</summary>
        public static int SlotOf(ITreeStatusReceiver body)
        {
            if (body == null) return NoSlot;
            int slot = body.TreeSlot;
            return slot >= 0 && slot < Cap && ReferenceEquals(s_bodies[slot], body) ? slot : NoSlot;
        }

        /// <summary>The registration serial of the body's slot (0 = not registered). Same body, same life → same number.</summary>
        public static int GenerationOf(ITreeStatusReceiver body)
        {
            int slot = SlotOf(body);
            return slot >= 0 ? s_generation[slot] : 0;
        }

        // ------------------------------------------------------------------ hit-time application

        /// <summary>
        /// Applies the elements in <paramref name="elements"/> to a hit body (the runner picks the mask: one owned element per
        /// hit in turn, all of them with Elemental Fusion). Shock is S-02's chain (stagger: <see cref="ApplyShockStagger"/>).
        /// </summary>
        public static void ApplyHit(ITreeStatusReceiver body, TreeElement elements)
        {
            if (body == null || elements == TreeElement.None) return;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            float element = s.WeaponPower * ElementScale(in s);
            if ((elements & TreeElement.Fire) != 0) Ignite(body, s.FireDotShare * element, s.FireSeconds);
            if ((elements & TreeElement.Ice) != 0) ApplyIce(body, Mathf.Max(1, s.IceStacksPerHit));
            if ((elements & TreeElement.Venom) != 0) AddVenomStacks(body, Mathf.Max(1, s.VenomStacksPerHit));
        }

        /// <summary>Element Nova: the full stack / state of every element in <paramref name="elements"/>.</summary>
        public static void ApplyFullStates(ITreeStatusReceiver body, TreeElement elements, Vector3 from)
        {
            if (body == null || elements == TreeElement.None) return;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            float element = s.WeaponPower * ElementScale(in s);
            if ((elements & TreeElement.Fire) != 0) Ignite(body, s.FireDotShare * element, s.FireSeconds);
            if ((elements & TreeElement.Ice) != 0) ApplyIce(body, Mathf.Max(1, s.IceMaxStacks));
            if ((elements & TreeElement.Venom) != 0) AddVenomStacks(body, Mathf.Max(1, s.VenomMaxStacks));
            if ((elements & TreeElement.Shock) != 0) ApplyShockStagger(body, from);
        }

        /// <summary>Sets the body burning: dps keeps the higher value while it burns, the time refreshes (never sums).</summary>
        public static bool Ignite(ITreeStatusReceiver body, float damagePerSecond, float seconds)
        {
            int i = SlotOf(body);
            if (i < 0 || damagePerSecond <= 0f || seconds <= 0f || !body.IsAlive) return false;
            ref BodyStatus st = ref s_status[i];
            bool fresh = st.BurnLeft <= 0f;
            st.BurnDps = fresh ? damagePerSecond : Mathf.Max(st.BurnDps, damagePerSecond);
            st.BurnLeft = Mathf.Max(st.BurnLeft, seconds);
            s_activeMask |= 1UL << i;
            if (fresh)
            {
                st.NextSpreadAt = s_now + TreeSystemsTuning.FireSpreadInterval;
                Ignites++;
                RefreshTint(i);
                TreeSystemsRunner.Raise(TreeProcKind.Ignite, body.Position, 0f, damagePerSecond, TreeElement.Fire, TreeBranch.Element,
                    TreeSynergy.None, body, 0);
            }

            return true;
        }

        /// <summary>
        /// Cold stacks → the body (S-03 owns slow, freeze, cooldown and the Elite / Ultra rule). Raises <see cref="TreeProcKind.Freeze"/>
        /// when the body reports a freeze. Nothing without ice in the build.
        /// </summary>
        public static IceResult ApplyIce(ITreeStatusReceiver body, int stacks, bool ignoreCooldown = false)
        {
            if (body == null || stacks <= 0 || !body.IsAlive) return IceResult.None;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s.IceSlowPerStack <= 0f && s.FreezeSeconds <= 0f) return IceResult.None;
            IceApplied++;
            IceResult r = body.ApplyIce(stacks, ignoreCooldown);
            int slot = SlotOf(body);
            if (slot >= 0) RefreshTint(slot);
            if (r == IceResult.Frozen)
            {
                Freezes++;
                TreeSystemsRunner.Raise(TreeProcKind.Freeze, body.Position, 0f, 0f, TreeElement.Ice, TreeBranch.Element, TreeSynergy.None, body, 0, s.FreezeSeconds);
            }
            else if (r == IceResult.UltraSlowed)
            {
                UltraSlows++;
            }

            return r;
        }

        /// <summary>Venom stacks (capped at VenomMaxStacks), time refreshed; Corrosion threshold and the venom tint follow.</summary>
        public static void AddVenomStacks(ITreeStatusReceiver body, int stacks)
        {
            int i = SlotOf(body);
            if (i < 0 || stacks <= 0 || !body.IsAlive) return;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s.VenomMaxStacks <= 0 || s.VenomStackShare <= 0f) return;
            ref BodyStatus st = ref s_status[i];
            float stackDps = s.VenomStackShare * s.WeaponPower * ElementScale(in s);
            st.VenomStackDps = st.VenomStacks > 0 ? Mathf.Max(st.VenomStackDps, stackDps) : stackDps;
            st.VenomStacks = Mathf.Min(s.VenomMaxStacks, st.VenomStacks + stacks);
            st.VenomLeft = Mathf.Max(st.VenomLeft, s.VenomSeconds > 0f ? s.VenomSeconds : SlotTick);
            s_activeMask |= 1UL << i;
            RefreshTint(i);
        }

        /// <summary>Thunder Lord: a chained body staggers through the body's existing ApplyTreeStagger (DEC-112 lock, S3).</summary>
        public static bool ApplyShockStagger(ITreeStatusReceiver body, Vector3 from)
        {
            if (body == null || !body.IsAlive) return false;
            float seconds = TreeRuntime.Current.ShockStaggerSeconds;
            if (seconds <= 0f) return false;
            Vector3 away = body.Position - from;
            away.y = 0f;
            return body.ApplyTreeStagger(seconds, away);
        }

        // ------------------------------------------------------------------ reads (the body / systems)

        /// <summary>
        /// The fire / venom part of the vulnerability (S5: multiplied): burning × FireVulnerability, corroded (venom ≥ threshold)
        /// × VenomVulnerability. The body multiplies it with its own FreezeVulnerability into every incoming damage. 1 with no tree.
        /// </summary>
        public static float DamageTakenScale(ITreeStatusReceiver body)
        {
            int i = SlotOf(body);
            if (i < 0) return 1f;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            ref readonly BodyStatus st = ref s_status[i];
            float scale = 1f;
            if (st.BurnLeft > 0f && s.FireVulnerability > 0f) scale *= s.FireVulnerability;
            if (s.VenomArmorIgnoreStacks > 0 && st.VenomStacks >= s.VenomArmorIgnoreStacks && s.VenomVulnerability > 0f) scale *= s.VenomVulnerability;
            return scale;
        }

        /// <summary>Corrosion: venom stacks at or above VenomArmorIgnoreStacks — the body's weapon hits pass regional armour.</summary>
        public static bool IsCorroded(ITreeStatusReceiver body)
        {
            int i = SlotOf(body);
            if (i < 0) return false;
            int threshold = TreeRuntime.Current.VenomArmorIgnoreStacks;
            return threshold > 0 && s_status[i].VenomStacks >= threshold;
        }

        public static bool IsBurning(ITreeStatusReceiver body) { int i = SlotOf(body); return i >= 0 && s_status[i].BurnLeft > 0f; }
        public static int VenomStacks(ITreeStatusReceiver body) { int i = SlotOf(body); return i >= 0 ? s_status[i].VenomStacks : 0; }

        // ------------------------------------------------------------------ area scans (registry, no PhysX)

        /// <summary>
        /// Living registered bodies whose root is within <paramref name="radius"/> + body reach of <paramref name="center"/>
        /// on the horizontal plane, in slot order (deterministic). Fills <paramref name="results"/> from 0; returns the count.
        /// Each system passes its own buffer (no shared static buffer, no nested-loop hazard).
        /// </summary>
        public static int Query(Vector3 center, float radius, ITreeStatusReceiver[] results)
        {
            if (results == null || radius <= 0f) return 0;
            float reach = radius + TreeSystemsTuning.BodyReach;
            float r2 = reach * reach;
            int n = 0;
            ulong m = s_usedMask;
            for (int i = 0; i < Cap && m != 0UL; i++)
            {
                ulong bit = 1UL << i;
                if ((m & bit) == 0UL) continue;
                m &= ~bit;
                if ((s_pendingRelease & bit) != 0UL) continue;
                ITreeStatusReceiver b = s_bodies[i];
                if (b == null || !b.IsAlive) continue;
                Vector3 p = b.Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                if (n >= results.Length) break;
                results[n++] = b;
            }

            return n;
        }

        /// <summary>The <paramref name="count"/> nearest living bodies within <paramref name="radius"/>, nearest first (selection scan, k small).</summary>
        public static int Nearest(Vector3 center, float radius, ITreeStatusReceiver[] results, int count)
        {
            if (results == null || radius <= 0f || count <= 0) return 0;
            if (count > results.Length) count = results.Length;
            float reach = radius + TreeSystemsTuning.BodyReach;
            float limit = reach * reach;
            int n = 0;
            float lastD = 0f;
            int lastSlot = -1;
            while (n < count)
            {
                int best = -1;
                float bestD = 0f;
                for (int i = 0; i < Cap; i++)
                {
                    if ((s_usedMask & (1UL << i)) == 0UL || (s_pendingRelease & (1UL << i)) != 0UL) continue;
                    ITreeStatusReceiver b = s_bodies[i];
                    if (b == null || !b.IsAlive) continue;
                    Vector3 p = b.Position;
                    float dx = p.x - center.x, dz = p.z - center.z;
                    float d = dx * dx + dz * dz;
                    if (d > limit) continue;
                    // strictly after the previous pick in (distance, slot) order: no duplicates, no allocation
                    if (lastSlot >= 0 && (d < lastD || (d == lastD && i <= lastSlot))) continue;
                    if (best < 0 || d < bestD || (d == bestD && i < best))
                    {
                        best = i;
                        bestD = d;
                    }
                }

                if (best < 0) break;
                results[n++] = s_bodies[best];
                lastD = bestD;
                lastSlot = best;
            }

            return n;
        }

        // ------------------------------------------------------------------ tick

        /// <summary>Runs the due DoT phases (1/16 s each). Called by <see cref="TreeSystemsRunner.Tick"/>.</summary>
        internal static void Tick(float deltaTime, float now)
        {
            s_now = now;
            if (deltaTime <= 0f) return;
            if ((s_activeMask | s_tintMask) == 0UL)
            {
                s_accumulator = 0f;
                return;
            }

            using (TreeMarkers.Status.Auto())
            {
                s_accumulator += deltaTime;
                int steps = 0;
                while (s_accumulator + Eps >= PhaseStep && steps < Phases * 2)
                {
                    RunPhase(s_phase);
                    s_phase = (s_phase + 1) % Phases;
                    s_accumulator -= PhaseStep;
                    steps++;
                }

                // A very long frame: at most two full cycles run; the rest is not replayed (DoT time only counts down on
                // a tick, so the totals stay exact; the remainder just lands later).
                if (s_accumulator > PhaseStep) s_accumulator = PhaseStep;
                if (s_accumulator < 0f) s_accumulator = 0f;
            }
        }

        /// <summary>Inferno spreads that came due in the DoT phase; each takes one area query (budget) and lights one neighbour.</summary>
        internal static void RunSpreads()
        {
            if (s_spreadPending <= 0) return;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            float radius = s.FireSpreadRadius;
            for (int i = 0; i < Cap && s_spreadPending > 0; i++)
            {
                if (!s_status[i].SpreadDue || (s_pendingRelease & (1UL << i)) != 0UL) continue;
                ITreeStatusReceiver body = s_bodies[i];
                ref BodyStatus st = ref s_status[i];
                if (body == null || !body.IsAlive || radius <= 0f || st.BurnLeft <= 0f)
                {
                    st.SpreadDue = false;
                    s_spreadPending--;
                    continue;
                }

                if (!TreeTargetQuery.TryUse()) return;   // keep it due: next frame
                st.SpreadDue = false;
                s_spreadPending--;
                st.NextSpreadAt = s_now + TreeSystemsTuning.FireSpreadInterval;
                int j = NearestUnburnt(i, body.Position, radius);
                if (j < 0) continue;
                float seconds = s.FireSeconds > 0f ? s.FireSeconds : st.BurnLeft;
                if (Ignite(s_bodies[j], st.BurnDps, seconds)) Spreads++;
            }

            if (s_spreadPending < 0) s_spreadPending = 0;
        }

        private static void RunPhase(int phase)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            bool owned = TreeDamageContext.BeginTransaction(0);   // one transaction per phase step: each body takes ONE call (12.10 (b))
            BeginBatch();
            try
            {
                for (int i = phase; i < Cap; i += Phases)
                {
                    ulong bit = 1UL << i;
                    if (((s_activeMask | s_tintMask) & bit) == 0UL || (s_pendingRelease & bit) != 0UL) continue;
                    ITreeStatusReceiver body = s_bodies[i];
                    if (body == null)
                    {
                        s_activeMask &= ~bit;
                        s_tintMask &= ~bit;
                        continue;
                    }

                    if (!body.IsAlive)
                    {
                        StaleReleased++;
                        ReleaseSlot(i);
                        continue;
                    }

                    ref BodyStatus st = ref s_status[i];
                    float burnSec = st.BurnLeft > 0f ? Mathf.Min(SlotTick, st.BurnLeft) : 0f;
                    float venomSec = st.VenomStacks > 0 ? Mathf.Min(SlotTick, st.VenomLeft) : 0f;
                    float burn = st.BurnDps * burnSec;
                    float venom = st.VenomStackDps * st.VenomStacks * venomSec;

                    // damage first, time after: a body that dies on this tick still reads as burning / poisoned (Wildfire,
                    // Plague). One call: burn + venom summed, the element is the larger share of this tick (12.10 (b)).
                    if (burn + venom > 0f)
                    {
                        TreeElement element = burn >= venom ? TreeElement.Fire : TreeElement.Venom;
                        DotTicks++;
                        DotDamage += burn + venom;
                        TreeDamageContext.Status(body, burn + venom, element, 0, TreeDamageKind.Dot,
                            element == TreeElement.Fire ? TreeProcKind.Ignite : TreeProcKind.None, body.Position);
                        if (!body.IsAlive || (s_pendingRelease & bit) != 0UL) continue;   // died: its slot frees after the loop
                    }

                    if (burnSec > 0f)
                    {
                        st.BurnLeft -= burnSec;
                        if (st.BurnLeft <= Eps)
                        {
                            st.BurnLeft = 0f;
                            st.BurnDps = 0f;
                            if (st.SpreadDue)
                            {
                                st.SpreadDue = false;
                                s_spreadPending--;
                            }
                        }
                        else if (s.FireSpreadRadius > 0f && !st.SpreadDue && s_now >= st.NextSpreadAt - Eps)
                        {
                            st.SpreadDue = true;
                            s_spreadPending++;
                        }
                    }

                    if (venomSec > 0f)
                    {
                        st.VenomLeft -= venomSec;
                        if (st.VenomLeft <= Eps)
                        {
                            st.VenomLeft = 0f;
                            st.VenomStacks = 0;
                            st.VenomStackDps = 0f;
                        }
                    }

                    if (st.BurnLeft <= 0f && st.VenomStacks <= 0) s_activeMask &= ~bit;
                    RefreshTint(i);
                }
            }
            finally
            {
                EndBatch();
                TreeDamageContext.EndTransaction(owned);
            }
        }

        // ------------------------------------------------------------------ internals

        /// <summary>The body's one tint: frozen (ice 1) &gt; burning (fire 1) &gt; venom (stacks / cap) &gt; cold stacks (stacks / cap). Sent on change.</summary>
        private static void RefreshTint(int i)
        {
            ITreeStatusReceiver body = s_bodies[i];
            if (body == null || !body.IsAlive) return;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            ref BodyStatus st = ref s_status[i];
            TreeElement element = TreeElement.None;
            float k = 0f;
            if (body.IsFrozen)
            {
                element = TreeElement.Ice;
                k = 1f;
            }
            else if (st.BurnLeft > 0f)
            {
                element = TreeElement.Fire;
                k = 1f;
            }
            else if (st.VenomStacks > 0)
            {
                element = TreeElement.Venom;
                k = s.VenomMaxStacks > 0 ? Mathf.Clamp01((float)st.VenomStacks / s.VenomMaxStacks) : 1f;
            }
            else
            {
                int ice = body.IceStacks;
                if (ice > 0)
                {
                    element = TreeElement.Ice;
                    k = Mathf.Clamp01((float)ice / Mathf.Max(1, s.IceMaxStacks));
                }
            }

            ulong bit = 1UL << i;
            if (element != TreeElement.None) s_tintMask |= bit;
            else s_tintMask &= ~bit;
            if (element == st.TintElement && Mathf.Abs(k - st.TintStrength) < 1e-4f) return;
            st.TintElement = element;
            st.TintStrength = k;
            body.SetStatusTint(element, k);
        }

        private static int NearestUnburnt(int self, Vector3 center, float radius)
        {
            float reach = radius + TreeSystemsTuning.BodyReach;
            float best = reach * reach;
            int found = -1;
            for (int i = 0; i < Cap; i++)
            {
                if (i == self || (s_usedMask & (1UL << i)) == 0UL || (s_pendingRelease & (1UL << i)) != 0UL) continue;
                ITreeStatusReceiver b = s_bodies[i];
                if (b == null || !b.IsAlive || s_status[i].BurnLeft > 0f) continue;
                Vector3 p = b.Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                float d = dx * dx + dz * dz;
                if (d <= best && (found < 0 || d < best))
                {
                    best = d;
                    found = i;
                }
            }

            return found;
        }

        private static void ReleaseSlot(int slot)
        {
            if (s_batch > 0)
            {
                ulong bit = 1UL << slot;
                if ((s_pendingRelease & bit) == 0UL)
                {
                    s_pendingRelease |= bit;
                    DeferredReleases++;
                }

                return;
            }

            ReleaseNow(slot);
        }

        private static void ReleaseNow(int slot)
        {
            ITreeStatusReceiver body = s_bodies[slot];
            if (body == null) return;
            if (s_status[slot].SpreadDue && s_spreadPending > 0) s_spreadPending--;
            s_bodies[slot] = null;
            s_status[slot] = default;
            s_generation[slot] = 0;
            ulong bit = 1UL << slot;
            s_usedMask &= ~bit;
            s_activeMask &= ~bit;
            s_tintMask &= ~bit;
            s_pendingRelease &= ~bit;
            s_free[s_freeCount++] = slot;
            ActiveSlots--;
            if (body.TreeSlot == slot) body.TreeSlot = NoSlot;
        }

        internal static float ElementScale(in TreeStats s) => s.ElementDamageScale > 0f ? s.ElementDamageScale : 1f;

        private static int PopCount(ulong m)
        {
            int n = 0;
            while (m != 0UL)
            {
                m &= m - 1UL;
                n++;
            }

            return n;
        }

        // ------------------------------------------------------------------ reset

        /// <summary>Run start / end, scene change, tests: forget every body (adapters keep stale slot numbers; <see cref="SlotOf"/> rejects them).</summary>
        internal static void Reset()
        {
            ResetTable();
            PeakSlots = 0;
            RegisterRefused = 0;
            StaleReleased = 0;
            Ignites = 0;
            Spreads = 0;
            IceApplied = 0;
            Freezes = 0;
            UltraSlows = 0;
            DeferredReleases = 0;
            DotTicks = 0;
            DotDamage = 0f;
        }

        private static void ResetTable()
        {
            for (int i = 0; i < Cap; i++)
            {
                s_bodies[i] = null;
                s_status[i] = default;
                s_generation[i] = 0;
                s_free[i] = Cap - 1 - i;   // pop order 0, 1, 2 … (deterministic)
            }

            s_freeCount = Cap;
            s_usedMask = 0UL;
            s_activeMask = 0UL;
            s_tintMask = 0UL;
            s_pendingRelease = 0UL;
            s_batch = 0;
            s_phase = 0;
            s_accumulator = 0f;
            s_now = 0f;
            s_spreadPending = 0;
            ActiveSlots = 0;
        }
    }
}
