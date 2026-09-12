using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>Where one round is in its life. Exactly one value is true at a time.</summary>
    public enum RoundState
    {
        /// <summary>No round has been started since the last reset.</summary>
        NotStarted = 0,

        /// <summary>BeginRound ran: pool sized, counters zeroed, points reset. No refill yet.</summary>
        Starting = 1,

        /// <summary>The pool is flowing. The only state that admits a spawn.</summary>
        Active = 2,

        /// <summary>Every enemy of the pool has been released; stragglers are still alive.</summary>
        PoolDepleted = 3,

        /// <summary>Pool empty AND no living enemy. Completion is owed but not yet published.</summary>
        Completing = 4,

        /// <summary>Completion published. Terminal until the next BeginRound.</summary>
        Completed = 5,

        /// <summary>The run ended (death, quit, debug) before the round completed. Terminal.</summary>
        Aborted = 6
    }

    /// <summary>
    /// Everything the population of one round is made of, as one value. Read by the HUD,
    /// the debug panel, the tests and the WP-004 round-end hand-off.
    /// </summary>
    public readonly struct RoundPopulationSnapshot
    {
        public RoundPopulationSnapshot(int round, RoundState state, int poolTotal, int unspawned,
            int spawnedTotal, int alive, int occupied, int killed, int targetActive, int ceiling,
            int activationFailures, int reconciled)
        {
            Round = round;
            State = state;
            PoolTotal = poolTotal;
            Unspawned = unspawned;
            SpawnedTotal = spawnedTotal;
            Alive = alive;
            Occupied = occupied;
            Killed = killed;
            TargetActive = targetActive;
            Ceiling = ceiling;
            ActivationFailures = activationFailures;
            Reconciled = reconciled;
        }

        /// <summary>1-based round number.</summary>
        public int Round { get; }

        public RoundState State { get; }

        /// <summary>Total pool of the round, from the round plan.</summary>
        public int PoolTotal { get; }

        /// <summary>Enemies of the pool not released yet.</summary>
        public int Unspawned { get; }

        /// <summary>Enemies released this round (successful activations only).</summary>
        public int SpawnedTotal { get; }

        /// <summary>Living enemies. Corpses are NOT counted (REQ-012).</summary>
        public int Alive { get; }

        /// <summary>Bodies checked out of the pool: living plus corpses. This is what the ceiling limits.</summary>
        public int Occupied { get; }

        /// <summary>Deaths registered this round. Cleanup removals are not kills.</summary>
        public int Killed { get; }

        /// <summary>Tempo target for <see cref="Alive"/> (REQ-072). Never above <see cref="Ceiling"/>.</summary>
        public int TargetActive { get; }

        /// <summary>Technical safety ceiling for <see cref="Occupied"/> (REQ-072). Never crossed.</summary>
        public int Ceiling { get; }

        /// <summary>Spawns that came back from the enemy dead-on-arrival and were rolled back.</summary>
        public int ActivationFailures { get; }

        /// <summary>Slots the owner had to close from housekeeping because no despawn notice arrived.</summary>
        public int Reconciled { get; }

        public bool IsCompleted => State == RoundState.Completed;

        /// <summary>The request's HUD line: pool left plus alive.</summary>
        public int EnemiesRemaining => Unspawned + Alive;
    }

    /// <summary>
    /// The round's population ledger: state machine, counters and the completion rule,
    /// with no scene, no MonoBehaviour and no allocation after construction. This is the
    /// part of <see cref="EnemySpawnDirector"/> that EditMode can prove.
    ///
    /// THE COMPLETION RULE (REQ-012, REQ-019)
    /// --------------------------------------
    /// A round completes when BOTH hold: the pool has no unspawned enemy left AND no
    /// spawned enemy is still alive. Corpses do not count as alive; a corpse still
    /// occupies its pool slot until it despawns, which is why <see cref="Alive"/> and
    /// <see cref="Occupied"/> are two numbers. Pool empty alone is not completion, and a
    /// moment of zero living enemies while the pool still has stock is not completion.
    ///
    /// TICKETS
    /// -------
    /// Every successful reservation hands out a ticket. The enemy carries it and reports
    /// its death and its despawn with it; each report is idempotent, so a double death
    /// or a double despawn cannot move a counter twice. A ticket from a previous round or
    /// a previous run is simply unknown to the current ledger and is ignored.
    ///
    /// TWO NUMBERS, NOT ONE (REQ-072)
    /// ------------------------------
    /// <see cref="TargetActive"/> is the tempo the refill aims <see cref="Alive"/> at.
    /// <see cref="Ceiling"/> is the technical limit on <see cref="Occupied"/>. BeginRound
    /// clamps the target under the ceiling; nothing here ever raises it above.
    ///
    /// COMPLETION IS TWO-STEP ON PURPOSE
    /// ---------------------------------
    /// A death is reported from inside the enemy's damage call, i.e. from the middle of a
    /// bullet. <see cref="Evaluate"/> only moves the state to <see cref="RoundState.Completing"/>;
    /// the owner calls <see cref="TryComplete"/> from its own Update, so the world is
    /// never frozen from inside a hitscan and a run that ends in between (Abort) wins.
    /// </summary>
    public sealed class RoundLedger
    {
        private struct Slot
        {
            public int Ticket;
            public bool Occupied;
            public bool Alive;

            /// <summary>True from reservation until the owner confirms the enemy came back alive.</summary>
            public bool PendingActivation;
        }

        private const int NoTicket = 0;

        private readonly List<Slot> _slots;
        private int _nextTicket = 1;

        private int _round;
        private RoundState _state = RoundState.NotStarted;
        private int _poolTotal;
        private int _unspawned;
        private int _spawnedTotal;
        private int _alive;
        private int _occupied;
        private int _killed;
        private int _targetActive;
        private int _ceiling = 1;
        private int _activationFailures;
        private int _reconciled;
        private bool _debugIgnorePool;

        public RoundLedger(int slotCapacity = 64)
        {
            _slots = new List<Slot>(Mathf.Max(1, slotCapacity));
        }

        // ------------------------------------------------------------------ events

        /// <summary>Raised on every state change, after the field is updated.</summary>
        public event Action<RoundState> StateChanged;

        /// <summary>Raised once per round when the last pooled enemy has been reserved. Argument: round.</summary>
        public event Action<int> PoolDepleted;

        /// <summary>Raised exactly once per round, from <see cref="TryComplete"/>. Argument: round.</summary>
        public event Action<int> RoundCompleted;

        // ------------------------------------------------------------------ readouts

        public int Round => _round;
        public RoundState State => _state;
        public int PoolTotal => _poolTotal;
        public int Unspawned => _unspawned;
        public int SpawnedTotal => _spawnedTotal;
        public int Alive => _alive;
        public int Occupied => _occupied;
        public int Killed => _killed;
        public int TargetActive => _targetActive;
        public int Ceiling => _ceiling;
        public int ActivationFailures => _activationFailures;
        public int Reconciled => _reconciled;

        /// <summary>Debug only: spawns stop consuming the pool and completion is suspended.</summary>
        public bool DebugIgnorePool
        {
            get => _debugIgnorePool;
            set => _debugIgnorePool = value;
        }

        /// <summary>
        /// Debug only: throws away every unspawned enemy of the round, so the next
        /// <see cref="Evaluate"/> sees an empty pool. The honest way to finish a round from
        /// a panel button: completion still needs the living count to reach zero.
        /// </summary>
        public void DebugDrainPool()
        {
            _unspawned = 0;
        }

        public bool IsRoundLive => _state == RoundState.Starting ||
                                   _state == RoundState.Active ||
                                   _state == RoundState.PoolDepleted ||
                                   _state == RoundState.Completing;

        public bool IsTerminal => _state == RoundState.Completed || _state == RoundState.Aborted;

        public RoundPopulationSnapshot Snapshot()
        {
            return new RoundPopulationSnapshot(_round, _state, _poolTotal, _unspawned, _spawnedTotal,
                _alive, _occupied, _killed, _targetActive, _ceiling, _activationFailures, _reconciled);
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// Opens a round. Invalid input is normalised rather than refused: a negative pool
        /// becomes 0 (the round completes at once), a ceiling below 1 becomes 1, a target
        /// outside [0, ceiling] is clamped. Every slot from the previous round is dropped;
        /// the owner must have despawned those bodies first.
        /// </summary>
        public void BeginRound(int round, int poolTotal, int targetActive, int ceiling)
        {
            _round = Mathf.Max(1, round);
            _poolTotal = Mathf.Max(0, poolTotal);
            _ceiling = Mathf.Max(1, ceiling);
            _targetActive = Mathf.Clamp(targetActive, 0, _ceiling);

            _unspawned = _poolTotal;
            _spawnedTotal = 0;
            _alive = 0;
            _occupied = 0;
            _killed = 0;
            _activationFailures = 0;
            _reconciled = 0;

            _slots.Clear();

            SetState(RoundState.Starting);
        }

        /// <summary>Starting → Active. The owner calls this once the points and the pool are ready.</summary>
        public bool Activate()
        {
            if (_state != RoundState.Starting)
            {
                return false;
            }

            SetState(RoundState.Active);
            return true;
        }

        /// <summary>
        /// Live config edit mid-round: retarget without touching the counters. The target
        /// stays under the ceiling; a lower ceiling never evicts anyone, it just stops spawns.
        /// </summary>
        public void Retarget(int targetActive, int ceiling)
        {
            _ceiling = Mathf.Max(1, ceiling);
            _targetActive = Mathf.Clamp(targetActive, 0, _ceiling);
        }

        /// <summary>
        /// The run ended under this round. Terminal: no spawn, no completion, ever, until
        /// the next BeginRound. Bodies stay on the map; their later reports are ignored.
        /// </summary>
        public void Abort()
        {
            if (_state == RoundState.NotStarted || IsTerminal)
            {
                return;
            }

            SetState(RoundState.Aborted);
        }

        /// <summary>Back to NotStarted with nothing remembered. New run.</summary>
        public void Reset()
        {
            _round = 0;
            _poolTotal = 0;
            _unspawned = 0;
            _spawnedTotal = 0;
            _alive = 0;
            _occupied = 0;
            _killed = 0;
            _activationFailures = 0;
            _reconciled = 0;
            _slots.Clear();
            _debugIgnorePool = false;

            SetState(RoundState.NotStarted);
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>
        /// Whether one more spawn is admissible right now, and if not, why. The ceiling
        /// test is on <see cref="Occupied"/> (bodies), the target test on <see cref="Alive"/>.
        /// </summary>
        public bool CanSpawn(out string reason)
        {
            if (_state != RoundState.Active)
            {
                reason = "round not active";
                return false;
            }

            if (_occupied >= _ceiling)
            {
                reason = "ceiling";
                return false;
            }

            if (_alive >= _targetActive)
            {
                reason = "target reached";
                return false;
            }

            if (_unspawned <= 0 && !_debugIgnorePool)
            {
                reason = "pool empty";
                return false;
            }

            reason = "ok";
            return true;
        }

        /// <summary>
        /// Takes one enemy out of the pool and opens a living, occupied slot for it.
        /// Returns false (ticket 0) when <see cref="CanSpawn"/> says no. The caller must
        /// either hand the ticket to a living enemy or <see cref="RollbackSpawn"/> it.
        /// </summary>
        public bool TryReserveSpawn(out int ticket)
        {
            if (!CanSpawn(out _))
            {
                ticket = NoTicket;
                return false;
            }

            ticket = _nextTicket++;
            if (_nextTicket <= 0)
            {
                _nextTicket = 1;
            }

            _slots.Add(new Slot { Ticket = ticket, Occupied = true, Alive = true, PendingActivation = true });

            if (!_debugIgnorePool)
            {
                _unspawned--;
            }

            _spawnedTotal++;
            _alive++;
            _occupied++;
            return true;
        }

        /// <summary>
        /// The enemy holding this ticket came back from activation alive: the reservation
        /// is now a real life. Until this is called, a despawn report for the ticket is
        /// treated as an activation failure (see <see cref="RegisterDespawn"/>), so the
        /// order in which the owner returns the body and rolls back cannot lose pool stock.
        /// </summary>
        public bool ConfirmActivation(int ticket)
        {
            int index = IndexOf(ticket);
            if (index < 0)
            {
                return false;
            }

            Slot slot = _slots[index];
            slot.PendingActivation = false;
            _slots[index] = slot;
            return true;
        }

        /// <summary>True while the ticket is reserved but not yet confirmed alive.</summary>
        public bool IsPendingActivation(int ticket)
        {
            int index = IndexOf(ticket);
            return index >= 0 && _slots[index].PendingActivation;
        }

        /// <summary>
        /// The reservation could not be honoured (pool returned nothing, or the enemy came
        /// back not alive after activation). Every counter is restored and the failure is
        /// counted so it is visible instead of silent.
        /// </summary>
        public bool RollbackSpawn(int ticket)
        {
            int index = IndexOf(ticket);
            if (index < 0)
            {
                return false;
            }

            RollbackAt(index);
            return true;
        }

        private void RollbackAt(int index)
        {
            _slots.RemoveAt(index);

            if (!_debugIgnorePool)
            {
                _unspawned++;
            }

            _spawnedTotal = Mathf.Max(0, _spawnedTotal - 1);
            _alive = Mathf.Max(0, _alive - 1);
            _occupied = Mathf.Max(0, _occupied - 1);
            _activationFailures++;
        }

        // ------------------------------------------------------------------ reports

        /// <summary>
        /// The enemy holding this ticket died. First report counts a kill and frees the
        /// living count; the body still occupies its slot. Later reports are ignored.
        /// </summary>
        public bool RegisterDeath(int ticket)
        {
            int index = IndexOf(ticket);
            if (index < 0)
            {
                return false;
            }

            Slot slot = _slots[index];
            if (!slot.Alive)
            {
                return false;
            }

            slot.Alive = false;
            slot.PendingActivation = false;   // a death proves the enemy was alive
            _slots[index] = slot;

            _alive = Mathf.Max(0, _alive - 1);
            _killed++;
            return true;
        }

        /// <summary>
        /// The body holding this ticket went back to the pool. Frees the occupied slot; an
        /// enemy removed without dying (round cleanup) leaves the living count without
        /// scoring a kill. Later reports are ignored.
        /// </summary>
        public bool RegisterDespawn(int ticket)
        {
            int index = IndexOf(ticket);
            if (index < 0)
            {
                return false;
            }

            Slot slot = _slots[index];

            // A body that goes back to the pool before its activation was confirmed never
            // lived: this IS the activation failure, whichever order the owner reports it
            // in. Pool stock is restored, nothing is silently lost.
            if (slot.PendingActivation)
            {
                RollbackAt(index);
                return true;
            }

            _slots.RemoveAt(index);

            if (slot.Alive)
            {
                _alive = Mathf.Max(0, _alive - 1);
            }

            _occupied = Mathf.Max(0, _occupied - 1);
            return true;
        }

        /// <summary>
        /// Housekeeping path: the owner found the body gone (destroyed, or released behind
        /// the ledger's back) without a despawn report. Same effect as RegisterDespawn,
        /// counted separately so a leak in the contract shows up in the numbers.
        /// </summary>
        public bool Reconcile(int ticket)
        {
            if (!RegisterDespawn(ticket))
            {
                return false;
            }

            _reconciled++;
            return true;
        }

        /// <summary>True while the ticket is known to this round.</summary>
        public bool IsTracked(int ticket) => IndexOf(ticket) >= 0;

        /// <summary>True while the ticket is known AND its enemy has not died.</summary>
        public bool IsAlive(int ticket)
        {
            int index = IndexOf(ticket);
            return index >= 0 && _slots[index].Alive;
        }

        // ------------------------------------------------------------------ completion

        /// <summary>
        /// Advances Active → PoolDepleted → Completing as the counters allow. Safe to call
        /// every frame; each transition happens once. Does NOT complete: see
        /// <see cref="TryComplete"/>.
        /// </summary>
        public RoundState Evaluate()
        {
            if (_debugIgnorePool)
            {
                return _state;
            }

            if (_state == RoundState.Active && _unspawned <= 0)
            {
                SetState(RoundState.PoolDepleted);
                Raise(PoolDepleted, _round);
            }

            if (_state == RoundState.PoolDepleted && _alive <= 0)
            {
                SetState(RoundState.Completing);
            }

            return _state;
        }

        /// <summary>
        /// Completing → Completed, publishing <see cref="RoundCompleted"/> exactly once.
        /// Returns false in every other state, so the owner can call it every frame.
        /// </summary>
        public bool TryComplete()
        {
            if (_state != RoundState.Completing)
            {
                return false;
            }

            SetState(RoundState.Completed);
            Raise(RoundCompleted, _round);
            return true;
        }

        // ------------------------------------------------------------------ internals

        private int IndexOf(int ticket)
        {
            if (ticket == NoTicket)
            {
                return -1;
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Ticket == ticket)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SetState(RoundState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            Raise(StateChanged, state);
        }

        private static void Raise<T>(Action<T> handler, T argument)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(argument);
            }
            catch (Exception e)
            {
                // A broken listener must never stop a round from ending.
                Debug.LogException(e);
            }
        }
    }
}
