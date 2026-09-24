#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-007 · development-only Special Weapon lifecycle probe for the real Player.
    ///
    /// Drives one real run through the public flow API and records, in the Player rather
    /// than the Editor, the lifecycle the package contract asks for: an eligible round with
    /// a deterministic (injected) roll, a schedule inside the config window, a drop at a
    /// valid progress point reached by real kills, one spawn cue / one beacon loop / one HUD
    /// marker, the ground timer, the pickup, the classic weapon parked with its magazine and
    /// build, unlimited fire with no reload, a multi-target attack under one damage
    /// transaction, an armoured target on the existing armour contract, timers frozen under
    /// the pause hold, the final-seconds warning exactly once, the automatic end with the
    /// classic weapon back, the run-end cancel on death, and a clean second run. Nothing here
    /// changes product behaviour: it calls public methods and reads public state. Every
    /// number it reads comes from the config; none is asserted as a product value.
    ///
    /// Compiled ONLY into development builds and the Editor (file-level #if); a release
    /// Player contains none of it, and without <c>-ddSpecialWeaponProbe</c> a development
    /// Player allocates nothing either.
    ///
    /// Command line:
    ///   -ddSpecialWeaponProbe &lt;outDir&gt;   enable; write stage JSON files + screenshots into outDir, then quit
    ///   -ddProfilePath &lt;file&gt;            redirect the profile file (keeps the real profile untouched)
    /// </summary>
    public static class SpecialWeaponProbe
    {
        private const string OutFlag = "-ddSpecialWeaponProbe";
        private const string ProfileFlag = "-ddProfilePath";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            if (string.IsNullOrEmpty(Arg(OutFlag)))
            {
                return;
            }

            string profilePath = Arg(ProfileFlag);
            if (!string.IsNullOrEmpty(profilePath))
            {
                ProfileStore.SetPathOverride(profilePath);
                Debug.Log("[SpecialWeaponProbe] profile path override -> " + profilePath);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string outDir = Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir))
            {
                return;
            }

            var carrier = new GameObject("SpecialWeaponProbe");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().Configure(outDir);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        [Serializable]
        public sealed class Stage
        {
            public string stage;
            public string utc;
            public string scene;
            public bool pass;
            public string note;
            public string kit;

            public int runId;
            public int runRound;
            public string runPhase;
            public float timeScale;
            public float roundProgress;

            public string directorState;
            public float scheduledProgress;
            public int spawnsThisRound;
            public int placementFailures;
            public int duplicateDirectors;
            public string hudPhase;
            public float groundRemaining;
            public float activeRemaining;
            public bool finalSecondsWarning;
            public bool classicDisabled;
            public int classicAmmo;
            public string classicBuild;

            public int evSpawned, evPickedUp, evFinalSeconds, evEnded, evGroundExpired, evScheduled, evCancelled, evCleared;
            public int cueSpawn, cuePickup, cueFinalSeconds, cueExpired, cueGroundExpired;
            public int loopBeaconStart, loopBeaconStop, loopSpinStart, loopSpinStop;
            public int damagedEvents, distinctTransactions, nestedBegins, duplicateApplications, pickupCount;

            public string screenshot;
            public string detail;
        }

        /// <summary>Deterministic roll: every value the director asks for is this constant.</summary>
        private sealed class ConstantRandom : ISpecialWeaponRandom
        {
            private readonly float _value;
            public ConstantRandom(float value) { _value = value; }
            public float Value() => _value;
            public float Range(float min, float max) => min + (max - min) * _value;
            public int Range(int min, int maxExclusive) => min;
        }

        /// <summary>Probe-driven input: fire held on demand, cursor at screen centre, nothing else.</summary>
        private sealed class ProbeInput : IInputSource
        {
            public bool Fire;
            public bool IsEnabled => true;
            public Vector2 Move => Vector2.zero;
            public AimSourceKind AimKind => AimSourceKind.Pointer;
            public Vector2 AimScreenPosition => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            public Vector2 AimStick => Vector2.zero;
            public bool FireHeld => Fire;
            public bool FirePressed => false;
            public bool FireReleased => false;
            public bool ReloadPressed => false;
            public bool PausePressed => false;
            public bool DebugPanelPressed => false;
#pragma warning disable 67
            public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
            public void Enable() { }
            public void Disable() { }
        }

        private sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeout = 40f;
            private string _outDir;
            private readonly List<Stage> _stages = new List<Stage>();

            private int _evSpawned, _evPickedUp, _evFinalSeconds, _evEnded, _evGroundExpired, _evScheduled, _evCancelled, _evCleared;
            private readonly Dictionary<CombatAudioEvent, int> _cue = new Dictionary<CombatAudioEvent, int>();
            private readonly Dictionary<CombatAudioEvent, int> _loopStart = new Dictionary<CombatAudioEvent, int>();
            private readonly Dictionary<CombatAudioEvent, int> _loopStop = new Dictionary<CombatAudioEvent, int>();
            private readonly List<EnemyDamageInfo> _damaged = new List<EnemyDamageInfo>(64);
            private SpecialWeaponCancelReason _lastCancelReason;
            private SpecialWeaponPhase _lastCancelPhase;

            internal void Configure(string outDir)
            {
                _outDir = outDir;
            }

            private void OnEnable()
            {
                WeaponEvents.SpecialWeaponSpawned += OnSpawned;
                WeaponEvents.SpecialWeaponPickedUp += OnPickedUp;
                WeaponEvents.SpecialWeaponFinalSeconds += OnFinalSeconds;
                WeaponEvents.SpecialWeaponEnded += OnEnded;
                WeaponEvents.SpecialWeaponGroundExpired += OnGroundExpired;
                WeaponEvents.SpecialWeaponScheduled += OnScheduled;
                WeaponEvents.SpecialWeaponCancelled += OnCancelled;
                WeaponEvents.SpecialWeaponCleared += OnCleared;
                AudioEvents.Cue += OnCue;
                AudioEvents.CueSuppressed += OnCue;
                AudioEvents.LoopStopped += OnLoopStopped;
                DamageEvents.EnemyDamaged += OnDamaged;
            }

            private void OnDisable()
            {
                WeaponEvents.SpecialWeaponSpawned -= OnSpawned;
                WeaponEvents.SpecialWeaponPickedUp -= OnPickedUp;
                WeaponEvents.SpecialWeaponFinalSeconds -= OnFinalSeconds;
                WeaponEvents.SpecialWeaponEnded -= OnEnded;
                WeaponEvents.SpecialWeaponGroundExpired -= OnGroundExpired;
                WeaponEvents.SpecialWeaponScheduled -= OnScheduled;
                WeaponEvents.SpecialWeaponCancelled -= OnCancelled;
                WeaponEvents.SpecialWeaponCleared -= OnCleared;
                AudioEvents.Cue -= OnCue;
                AudioEvents.CueSuppressed -= OnCue;
                AudioEvents.LoopStopped -= OnLoopStopped;
                DamageEvents.EnemyDamaged -= OnDamaged;
            }

            private void OnSpawned(Vector3 p, float d) { _evSpawned++; }
            private void OnPickedUp(float d) { _evPickedUp++; }
            private void OnFinalSeconds(float r) { _evFinalSeconds++; }
            private void OnEnded() { _evEnded++; }
            private void OnGroundExpired() { _evGroundExpired++; }
            private void OnScheduled(float m) { _evScheduled++; }
            private void OnCancelled(SpecialWeaponCancelInfo i) { _evCancelled++; _lastCancelReason = i.Reason; _lastCancelPhase = i.PhaseAtCancel; }
            private void OnCleared() { _evCleared++; }
            private void OnCue(CombatAudioCue c) { Bump(c.IsLoop ? _loopStart : _cue, c.Event); }
            private void OnLoopStopped(CombatAudioEvent e) { Bump(_loopStop, e); }
            private void OnDamaged(EnemyDamageInfo i) { _damaged.Add(i); }

            private static void Bump(Dictionary<CombatAudioEvent, int> d, CombatAudioEvent e)
            {
                d.TryGetValue(e, out int n);
                d[e] = n + 1;
            }

            private static int Count(Dictionary<CombatAudioEvent, int> d, CombatAudioEvent e) => d.TryGetValue(e, out int n) ? n : 0;

            private void ResetCounters()
            {
                _evSpawned = _evPickedUp = _evFinalSeconds = _evEnded = _evGroundExpired = _evScheduled = _evCancelled = _evCleared = 0;
                _cue.Clear();
                _loopStart.Clear();
                _loopStop.Clear();
                _damaged.Clear();
            }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_outDir);

                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return null;
                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null &&
                                             PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null &&
                                             SpecialWeaponDirector.Instance != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting");

                RunDirector run = RunDirector.Instance;
                EnemySpawnDirector dir = EnemySpawnDirector.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                WeaponController wc = WeaponRegistry.ActiveController;
                SpecialWeaponDirector sw = SpecialWeaponDirector.Instance;
                PrototypeConfig config = PrototypeConfig.Active;
                SpecialWeaponConfigSection s = config.Weapons.Special;
                rig.Health.DiagnosisInvulnerable = true;

                SetSpawning(dir, false);
                dir.ClearAllEnemies();
                EnemyFactory.ClearAll();

                var input = new ProbeInput();
                InputSourceRegistry.SetCurrent(input);
                rig.Motor.SetInputSource(null);
                rig.Aim.SetInputSource(null);
                yield return null;

                // Deterministic roll: 0.1 lands inside any positive chance, puts the mark near the
                // window's low end, and selects the first weapon of the roll table.
                var rng = new ConstantRandom(0.1f);
                sw.SetRandomSource(rng);

                // ---------------------------------------------------------------- 01: eligible round → schedule inside the window
                ResetCounters();
                run.BeginRound(Mathf.Max(1, s.FirstEligibleRound));
                yield return null;
                float min = Mathf.Min(s.SpawnWindowMin, s.SpawnWindowMax);
                float max = Mathf.Max(s.SpawnWindowMin, s.SpawnWindowMax);
                Stage s1 = Capture("01-eligible-round-schedule", run, sw, wc, "1");
                s1.pass = config.SpecialWeaponSpawnChance(run.Round) > 0f && sw.State == SpecialWeaponDirector.SpecialWeaponState.Scheduled &&
                          sw.ScheduledProgress >= min - 0.0001f && sw.ScheduledProgress <= max + 0.0001f &&
                          sw.ScheduledProgress <= SpecialWeaponDirector.LatestSpawnProgress && _evScheduled == 1 && SpecialWeaponSahne.Pickups() == 0;
                s1.detail = "round=" + run.Round + " chance=" + config.SpecialWeaponSpawnChance(run.Round) + " mark=" + sw.ScheduledProgress + " window=[" + min + "," + max + "] scheduledEvents=" + _evScheduled;
                s1.note = "first eligible round (config) with an injected roll inside the chance: the drop is scheduled once, its mark inside the config window and under the late limit; nothing is on the ground";
                Write(s1);

                // ---------------------------------------------------------------- 02: real kills move the authoritative progress to the mark → one drop
                ResetCounters();
                int pool = run.RoundPoolTotal;
                int killsNeeded = Mathf.CeilToInt(sw.ScheduledProgress * pool);
                int killed = 0;
                Vector3 basePos = rig.transform.position + new Vector3(0f, 0f, 6f);
                for (int i = 0; i < killsNeeded + 1 && sw.State == SpecialWeaponDirector.SpecialWeaponState.Scheduled; i++)
                {
                    ZombieEnemy z = Spawn(rig, basePos, run.Round);
                    if (z == null) break;
                    z.Kill();
                    killed++;
                    yield return null;
                }

                yield return WaitUntil(() => sw.State != SpecialWeaponDirector.SpecialWeaponState.Scheduled, "drop did not land at the mark");
                yield return null;
                SpecialWeaponPickup pickup = SpecialWeaponSahne.Live();
                Stage s2 = Capture("02-spawn-at-progress-one-signal", run, sw, wc, "2,3");
                s2.pass = sw.State == SpecialWeaponDirector.SpecialWeaponState.OnGround && pickup != null && run.RoundProgress01 >= sw.ScheduledProgress - 0.0001f &&
                          run.RoundProgress01 <= SpecialWeaponDirector.LatestSpawnProgress + (1f / Mathf.Max(1, pool)) && _evSpawned == 1 &&
                          Count(_cue, CombatAudioEvent.SpecialWeaponSpawn) == 1 && Count(_loopStart, CombatAudioEvent.SpecialWeaponBeaconLoop) == 1 &&
                          WeaponEvents.Special.Phase == SpecialWeaponPhase.OnGround && SpecialWeaponSahne.Pickups() == 1 && sw.SpawnsThisRound == 1 &&
                          sw.PlacementFailureCount == 0;
                s2.detail = "pool=" + pool + " killed=" + killed + " progress=" + run.RoundProgress01 + " spawned=" + _evSpawned + " cueSpawn=" + Count(_cue, CombatAudioEvent.SpecialWeaponSpawn) +
                            " beaconLoopStart=" + Count(_loopStart, CombatAudioEvent.SpecialWeaponBeaconLoop) + " hud=" + WeaponEvents.Special.Phase + " pickups=" + SpecialWeaponSahne.Pickups() +
                            " weapon=" + sw.LastRolledWeapon;
                s2.note = "real kills over the round pool move RunDirector's progress to the mark; exactly one drop lands with one spawn cue, one beacon loop and the HUD OnGround marker";
                yield return Shot(s2, "02-on-ground.png");
                Write(s2);

                // ---------------------------------------------------------------- 03: ground timer runs on the ground, active timer does not
                float g0 = pickup != null ? pickup.Remaining : 0f;
                yield return new WaitForSecondsRealtime(0.5f);
                Stage s3 = Capture("03-ground-timer-only", run, sw, wc, "4");
                s3.pass = pickup != null && pickup.Remaining < g0 && !wc.SpecialWeaponActive && wc.Special.Remaining == 0f && Mathf.Approximately(pickup.Duration, s.GroundLifetime);
                s3.detail = "ground=" + g0 + "->" + (pickup != null ? pickup.Remaining : -1f) + " active=" + wc.Special.Remaining + " groundDuration=" + (pickup != null ? pickup.Duration : -1f);
                s3.note = "the ground timer counts down from the config ground lifetime while the active timer stays at zero (REQ-055)";
                Write(s3);

                // ---------------------------------------------------------------- 04: pickup → classic parked with magazine + build, HUD OnGround→Active
                ResetCounters();
                input.Fire = true;
                yield return new WaitForSecondsRealtime(0.4f);
                input.Fire = false;
                yield return null;
                int ammoParked = wc.Classic.Ammo;
                string buildParked = wc.Modifiers.DescribeBuild();
                ClassicWeaponRuntime classic = wc.Classic;
                rig.Motor.Teleport(pickup.transform.position);
                yield return WaitUntil(() => wc.SpecialWeaponActive, "pickup was not collected");
                yield return null;
                Stage s4 = Capture("04-pickup-classic-parked", run, sw, wc, "5,6");
                s4.pass = _evPickedUp == 1 && sw.State == SpecialWeaponDirector.SpecialWeaponState.InUse && WeaponEvents.Special.Phase == SpecialWeaponPhase.Active &&
                          Count(_loopStop, CombatAudioEvent.SpecialWeaponBeaconLoop) == 1 && Count(_loopStart, CombatAudioEvent.HeavyWeaponSpinUp) == 1 &&
                          Count(_cue, CombatAudioEvent.SpecialWeaponPickup) == 1 && _evGroundExpired == 0 && SpecialWeaponSahne.Pickups() == 0 &&
                          WeaponEvents.Classic.TemporarilyDisabled && ReferenceEquals(classic, wc.Classic) && wc.Classic.Ammo == ammoParked &&
                          wc.Modifiers.DescribeBuild() == buildParked && ammoParked < wc.Classic.MagazineSize &&
                          Mathf.Approximately(wc.Special.Duration, s.ActiveDuration) && wc.Special.Remaining > 0f;
                s4.detail = "pickedUp=" + _evPickedUp + " beaconLoopStop=" + Count(_loopStop, CombatAudioEvent.SpecialWeaponBeaconLoop) + " spinUpStart=" + Count(_loopStart, CombatAudioEvent.HeavyWeaponSpinUp) +
                            " ammoParked=" + ammoParked + "/" + wc.Classic.MagazineSize + " build=" + buildParked + " activeRemaining=" + wc.Special.Remaining + " hud=" + WeaponEvents.Special.Phase;
                s4.note = "collecting the drop starts the active timer from the config duration, parks the classic weapon (same runtime, same magazine, same build, HUD 'disabled'), stops the beacon loop and starts the active loop - each exactly once";
                yield return Shot(s4, "04-active.png");
                Write(s4);

                // ---------------------------------------------------------------- 05: unlimited fire, no reload; multi-target attack = one transaction; armoured target on the existing contract
                ResetCounters();
                int nestedBefore = DamageTransaction.NestedBegins;
                int dupBefore = ZombieEnemy.DuplicateApplicationCount;
                var targets = new List<ZombieEnemy>();
                int hpRound = 1;
                for (int r = 1; r <= 400; r++)
                {
                    if (config.EnemyHealth(EnemyTier.Standard, r) > s.Common(SpecialWeaponId.Raiju).Damage * 4f) { hpRound = r; break; }   // WP-069 B4 interim (S-02)
                }

                for (int i = 0; i < 3; i++)
                {
                    float a = (i - 1) * 25f * Mathf.Deg2Rad;
                    float rr = 3f;   // WP-069 B4 interim (S-02): half the removed bell's wave radius
                    Vector3 p = rig.transform.position + new Vector3(Mathf.Sin(a) * rr, 0f, Mathf.Cos(a) * rr);
                    ZombieEnemy z = Spawn(rig, p, hpRound);
                    if (z != null) { z.FitArmorComposition(i == 0, i == 0, i == 0); targets.Add(z); }
                }

                yield return null;
                int ammoBefore = wc.Classic.Ammo;
                input.Fire = true;
                yield return new WaitForSecondsRealtime(1.2f);
                input.Fire = false;
                yield return new WaitForSecondsRealtime(0.6f);
                int fireCues = Count(_cue, CombatAudioEvent.HeavyWeaponFire);
                var txByTarget = new Dictionary<int, HashSet<int>>();
                int armouredArmorHits = 0;
                foreach (EnemyDamageInfo d in _damaged)
                {
                    if (!txByTarget.TryGetValue(d.LifeId, out HashSet<int> set)) { set = new HashSet<int>(); txByTarget[d.LifeId] = set; }
                    set.Add(d.Meta.TransactionId);
                    if (targets.Count > 0 && d.LifeId == targets[0].LifeId && d.Result.ArmorDamage > 0f) armouredArmorHits++;
                }

                // Per attack (one wave band) the hit targets share the id: group by transaction id and check no id spans more than one band's worth of hits per target.
                var perTx = new Dictionary<int, int>();
                foreach (EnemyDamageInfo d in _damaged) { perTx.TryGetValue(d.Meta.TransactionId, out int n); perTx[d.Meta.TransactionId] = n + 1; }
                int multiTargetTx = 0;
                foreach (KeyValuePair<int, int> kv in perTx) if (kv.Value > 1) multiTargetTx++;
                Stage s5 = Capture("05-unlimited-fire-one-transaction-armour", run, sw, wc, "7,8,9");
                s5.pass = fireCues > 0 && wc.Classic.Ammo == ammoBefore && !wc.Classic.IsReloading && _damaged.Count > 0 && multiTargetTx > 0 &&
                          DamageTransaction.NestedBegins - nestedBefore == 0 && ZombieEnemy.DuplicateApplicationCount - dupBefore == 0 &&
                          (targets.Count == 0 || armouredArmorHits > 0) && wc.SpecialWeaponActive && sw.LastRolledWeapon == SpecialWeaponId.Raiju;
                s5.detail = "fireCues=" + fireCues + " classicAmmo=" + ammoBefore + "->" + wc.Classic.Ammo + " reloading=" + wc.Classic.IsReloading + " damaged=" + _damaged.Count +
                            " transactions=" + perTx.Count + " multiTargetTx=" + multiTargetTx + " nestedDelta=" + (DamageTransaction.NestedBegins - nestedBefore) +
                            " dupDelta=" + (ZombieEnemy.DuplicateApplicationCount - dupBefore) + " armouredArmorHits=" + armouredArmorHits + " weapon=" + sw.LastRolledWeapon;
                s5.note = "held fire for over a second: the Special Weapon fires (cues), the classic magazine never moves and no reload starts; each wave band applies to its targets under ONE transaction id (no nested begin, no duplicate refusal); the armoured body takes armour damage on the existing direct contract";
                Write(s5);
                EnemyFactory.ClearAll();

                // ---------------------------------------------------------------- 06: pause freezes the active timer and the attack clock
                ResetCounters();
                float a0 = wc.Special.Remaining;
                yield return new WaitForSecondsRealtime(0.3f);
                float a1 = wc.Special.Remaining;
                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                yield return null;
                float frozen = wc.Special.Remaining;
                int cueFrozen = Count(_cue, CombatAudioEvent.HeavyWeaponFire);
                input.Fire = true;
                yield return new WaitForSecondsRealtime(0.5f);
                input.Fire = false;
                float frozenAfter = wc.Special.Remaining;
                int cueFrozenAfter = Count(_cue, CombatAudioEvent.HeavyWeaponFire);
                float ts = Time.timeScale;
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                yield return new WaitForSecondsRealtime(0.3f);
                float resumed = wc.Special.Remaining;
                Stage s6 = Capture("06-pause-freezes-timers", run, sw, wc, "10");
                s6.pass = a1 < a0 && Mathf.Approximately(ts, 0f) && Mathf.Approximately(frozen, frozenAfter) && cueFrozenAfter == cueFrozen && resumed < frozenAfter && wc.SpecialWeaponActive;
                s6.detail = "liveDelta=" + (a0 - a1) + " timeScaleUnderHold=" + ts + " frozen=" + frozen + "->" + frozenAfter + " fireCuesUnderHold=" + (cueFrozenAfter - cueFrozen) + " resumedDelta=" + (frozenAfter - resumed);
                s6.note = "positive control first (the timer moves), then under the pause hold the active timer and the attack clock do not move even with fire held, and they move again after the hold";
                Write(s6);

                // ---------------------------------------------------------------- 07: final-seconds warning once, then automatic end with the classic weapon back
                ResetCounters();
                yield return WaitUntil(() => wc.Special.InFinalSeconds || !wc.SpecialWeaponActive, "final seconds never came");
                bool hudWarning = WeaponEvents.Special.FinalSecondsWarning;
                yield return null;
                Stage s7 = Capture("07-final-seconds-once", run, sw, wc, "11");
                s7.pass = _evFinalSeconds == 1 && Count(_cue, CombatAudioEvent.SpecialWeaponFinalSeconds) == 1 && (hudWarning || WeaponEvents.Special.FinalSecondsWarning) &&
                          wc.Special.Remaining <= s.FinalSecondsWarning + 0.05f;
                s7.detail = "finalSecondsEvents=" + _evFinalSeconds + " cue=" + Count(_cue, CombatAudioEvent.SpecialWeaponFinalSeconds) + " hudWarning=" + hudWarning + " remaining=" + wc.Special.Remaining;
                s7.note = "entering the config warning window raises the event and the cue exactly once and the HUD snapshot carries the warning";
                yield return Shot(s7, "07-final-seconds.png");
                Write(s7);

                yield return WaitUntil(() => !wc.SpecialWeaponActive, "active duration never ended");
                yield return WaitUntil(() => sw.State == SpecialWeaponDirector.SpecialWeaponState.Idle, "director did not return to Idle");
                yield return null;
                Stage s8 = Capture("08-auto-end-classic-restored", run, sw, wc, "12,13");
                s8.pass = _evEnded == 1 && _evFinalSeconds == 1 && _evCleared == 1 && _evCancelled == 0 && !WeaponEvents.Classic.TemporarilyDisabled &&
                          ReferenceEquals(classic, wc.Classic) && wc.Classic.Ammo == ammoParked && wc.Modifiers.DescribeBuild() == buildParked &&
                          WeaponEvents.Special.Phase == SpecialWeaponPhase.None && Count(_loopStop, CombatAudioEvent.HeavyWeaponSpinUp) == 1 &&
                          Count(_cue, CombatAudioEvent.SpecialWeaponExpired) == 1 && SpecialImpacts.ActiveChains == 0;
                s8.detail = "ended=" + _evEnded + " cleared=" + _evCleared + " cancelled=" + _evCancelled + " classicDisabled=" + WeaponEvents.Classic.TemporarilyDisabled + " ammo=" + wc.Classic.Ammo + "/" + ammoParked +
                            " build=" + wc.Modifiers.DescribeBuild() + " hud=" + WeaponEvents.Special.Phase + " spinStop=" + Count(_loopStop, CombatAudioEvent.HeavyWeaponSpinUp) + " expiredCue=" + Count(_cue, CombatAudioEvent.SpecialWeaponExpired);
                s8.note = "the active timer runs out: one Ended, one Cleared, the classic weapon comes back automatically with the magazine and build it was parked with, the HUD closes, the active loop stops";
                yield return Shot(s8, "08-classic-restored.png");
                Write(s8);

                // ---------------------------------------------------------------- 09: run end (death) cancels a grounded drop; nothing outlives the run
                ResetCounters();
                bool forced = sw.ForceSpawn();
                yield return WaitUntil(() => SpecialWeaponSahne.Pickups() == 1, "forced drop did not land");
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "probe-death");
                yield return WaitUntil(() => run.Phase == RunPhase.Dead, "death did not end the run");
                yield return null;
                Stage s9 = Capture("09-death-cancels-process", run, sw, wc, "14,15");
                s9.pass = forced && SceneManager.GetActiveScene().name == PrototypeScenes.Run && sw.State == SpecialWeaponDirector.SpecialWeaponState.Idle &&
                          SpecialWeaponSahne.Pickups() == 0 && WeaponEvents.Special.Phase == SpecialWeaponPhase.None && _evCancelled == 1 &&
                          _lastCancelReason == SpecialWeaponCancelReason.RunEnded && _lastCancelPhase == SpecialWeaponPhase.OnGround && _evCleared == 1 &&
                          Count(_loopStart, CombatAudioEvent.SpecialWeaponBeaconLoop) == Count(_loopStop, CombatAudioEvent.SpecialWeaponBeaconLoop) && _evGroundExpired == 0;
                s9.detail = "forced=" + forced + " state=" + sw.State + " pickups=" + SpecialWeaponSahne.Pickups() + " hud=" + WeaponEvents.Special.Phase + " cancelled=" + _evCancelled + "/" + _lastCancelReason + "/" + _lastCancelPhase +
                            " cleared=" + _evCleared + " beaconLoop=" + Count(_loopStart, CombatAudioEvent.SpecialWeaponBeaconLoop) + "/" + Count(_loopStop, CombatAudioEvent.SpecialWeaponBeaconLoop) + " groundExpired=" + _evGroundExpired;
                s9.note = "with a drop on the ground the player dies: the run's end cancels the process immediately (before the results hand-off) - director Idle, no pickup, HUD closed, beacon loop stopped, one Cancelled(RunEnded, OnGround) and one Cleared, no 'ground expired' news";
                Write(s9);

                // ---------------------------------------------------------------- 10: second run starts clean; subscriber counts equal
                Dictionary<string, int> subsRun1 = Subscribers();
                yield return WaitForScene(PrototypeScenes.Results);
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                Dictionary<string, int> subsMenu = Subscribers();
                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && WeaponRegistry.ActiveController != null && SpecialWeaponDirector.Instance != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting, "second run did not reach Fighting");
                yield return null;
                yield return null;
                Dictionary<string, int> subsRun2 = Subscribers();
                run = RunDirector.Instance; sw = SpecialWeaponDirector.Instance; wc = WeaponRegistry.ActiveController;
                bool subsEqual = true;
                var subsDetail = new System.Text.StringBuilder();
                foreach (KeyValuePair<string, int> kv in subsRun1)
                {
                    int r2 = subsRun2.TryGetValue(kv.Key, out int v) ? v : -1;
                    int m = subsMenu.TryGetValue(kv.Key, out int mv) ? mv : -1;
                    if (r2 != kv.Value || m != 0) subsEqual = false;
                    subsDetail.Append(kv.Key).Append('=').Append(kv.Value).Append('/').Append(m).Append('/').Append(r2).Append(' ');
                }

                Stage s10 = Capture("10-second-run-clean", run, sw, wc, "16,17");
                s10.pass = sw.State == SpecialWeaponDirector.SpecialWeaponState.Idle && sw.ScheduledProgress < 0f && sw.SpawnsThisRound == 0 &&
                           WeaponEvents.Special.Phase == SpecialWeaponPhase.None && SpecialWeaponSahne.Pickups() == 0 && !wc.SpecialWeaponActive &&
                           wc.Special.Remaining == 0f && SpecialImpacts.ActiveChains == 0 && !WeaponEvents.Classic.TemporarilyDisabled && subsEqual &&
                           SpecialWeaponDirector.DuplicateInstanceCount == 0;
                s10.detail = "state=" + sw.State + " mark=" + sw.ScheduledProgress + " spawns=" + sw.SpawnsThisRound + " hud=" + WeaponEvents.Special.Phase + " pickups=" + SpecialWeaponSahne.Pickups() +
                             " active=" + wc.SpecialWeaponActive + " duplicates=" + SpecialWeaponDirector.DuplicateInstanceCount + " subscribers(run1/menu/run2): " + subsDetail;
                s10.note = "results → menu → new run: no pickup, runtime, timer, schedule, HUD snapshot or loop carried over; Special/bridge static subscriber counts are equal in both runs and zero in the menu";
                yield return Shot(s10, "10-second-run.png");
                Write(s10);

                // summary
                int passCount = 0;
                for (int i = 0; i < _stages.Count; i++) if (_stages[i].pass) passCount++;
                File.WriteAllText(Path.Combine(_outDir, "summary.txt"),
                    "SpecialWeaponProbe " + DateTime.UtcNow.ToString("o") + " stages=" + _stages.Count + " pass=" + passCount + "\n" +
                    string.Join("\n", StageLines()) + "\n");
                Debug.Log("[SpecialWeaponProbe] DONE stages=" + _stages.Count + " pass=" + passCount);

                EnemyFactory.ClearAll();
                PrototypeUiFlow.AbortRunToMainMenu();
                yield return new WaitForSecondsRealtime(1f);
                PrototypeUiFlow.Quit();
            }

            private IEnumerable<string> StageLines()
            {
                for (int i = 0; i < _stages.Count; i++)
                {
                    yield return (_stages[i].pass ? "PASS " : "FAIL ") + _stages[i].stage + " | " + _stages[i].detail;
                }
            }

            private static readonly (Type Tip, string Ad)[] WatchedEvents =
            {
                (typeof(WeaponEvents), "SpecialWeaponSpawned"), (typeof(WeaponEvents), "SpecialWeaponPickedUp"), (typeof(WeaponEvents), "SpecialWeaponFinalSeconds"),
                (typeof(WeaponEvents), "SpecialWeaponEnded"), (typeof(WeaponEvents), "SpecialWeaponGroundExpired"), (typeof(WeaponEvents), "SpecialChanged"),
                (typeof(WeaponEvents), "SpecialWeaponScheduled"), (typeof(WeaponEvents), "SpecialWeaponCancelled"), (typeof(WeaponEvents), "SpecialWeaponCleared"),
                (typeof(SpecialWeaponRoundBridge), "RoundStarted"), (typeof(SpecialWeaponRoundBridge), "RoundEnded"),
                (typeof(SpecialWeaponRoundBridge), "RunStarted"), (typeof(SpecialWeaponRoundBridge), "RunEnded"),
            };

            /// <summary>Static subscriber counts of the Special/bridge events, minus this probe's own (it subscribes to nine of them).</summary>
            private Dictionary<string, int> Subscribers()
            {
                var d = new Dictionary<string, int>();
                for (int i = 0; i < WatchedEvents.Length; i++)
                {
                    FieldInfo f = WatchedEvents[i].Tip.GetField(WatchedEvents[i].Ad, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    int n = 0;
                    if (f != null && f.GetValue(null) is Delegate del)
                    {
                        foreach (Delegate h in del.GetInvocationList())
                        {
                            if (!ReferenceEquals(h.Target, this)) n++;
                        }
                    }

                    d[WatchedEvents[i].Tip.Name + "." + WatchedEvents[i].Ad] = n;
                }

                return d;
            }

            private static ZombieEnemy Spawn(PlayerRig rig, Vector3 pos, int round)
            {
                Vector3 look = rig.transform.position - pos; look.y = 0f;
                Quaternion rot = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity;
                return EnemyFactory.Spawn(EnemyTier.Standard, round, pos, rot);
            }

            private static void SetSpawning(EnemySpawnDirector dir, bool on)
            {
                FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) f.SetValue(dir, on);
            }

            private Stage Capture(string stage, RunDirector r, SpecialWeaponDirector sw, WeaponController wc, string kit)
            {
                bool hasRun = !ReferenceEquals(r, null);
                var perTx = new HashSet<int>();
                foreach (EnemyDamageInfo d in _damaged) perTx.Add(d.Meta.TransactionId);
                return new Stage
                {
                    stage = stage,
                    utc = DateTime.UtcNow.ToString("o"),
                    scene = SceneManager.GetActiveScene().name,
                    kit = kit,
                    runId = hasRun ? r.RunId : 0,
                    runRound = hasRun ? r.Round : 0,
                    runPhase = hasRun ? r.Phase.ToString() : "none",
                    timeScale = Time.timeScale,
                    roundProgress = hasRun ? r.RoundProgress01 : 0f,
                    directorState = sw != null ? sw.State.ToString() : "none",
                    scheduledProgress = sw != null ? sw.ScheduledProgress : -1f,
                    spawnsThisRound = sw != null ? sw.SpawnsThisRound : 0,
                    placementFailures = sw != null ? sw.PlacementFailureCount : 0,
                    duplicateDirectors = SpecialWeaponDirector.DuplicateInstanceCount,
                    hudPhase = WeaponEvents.Special.Phase.ToString(),
                    groundRemaining = WeaponEvents.Special.GroundRemaining,
                    activeRemaining = WeaponEvents.Special.ActiveRemaining,
                    finalSecondsWarning = WeaponEvents.Special.FinalSecondsWarning,
                    classicDisabled = WeaponEvents.Classic.TemporarilyDisabled,
                    classicAmmo = wc != null ? wc.Classic.Ammo : 0,
                    classicBuild = wc != null ? wc.Modifiers.DescribeBuild() : "",
                    evSpawned = _evSpawned, evPickedUp = _evPickedUp, evFinalSeconds = _evFinalSeconds, evEnded = _evEnded, evGroundExpired = _evGroundExpired,
                    evScheduled = _evScheduled, evCancelled = _evCancelled, evCleared = _evCleared,
                    cueSpawn = Count(_cue, CombatAudioEvent.SpecialWeaponSpawn), cuePickup = Count(_cue, CombatAudioEvent.SpecialWeaponPickup),
                    cueFinalSeconds = Count(_cue, CombatAudioEvent.SpecialWeaponFinalSeconds), cueExpired = Count(_cue, CombatAudioEvent.SpecialWeaponExpired),
                    cueGroundExpired = Count(_cue, CombatAudioEvent.SpecialWeaponGroundExpired),
                    loopBeaconStart = Count(_loopStart, CombatAudioEvent.SpecialWeaponBeaconLoop), loopBeaconStop = Count(_loopStop, CombatAudioEvent.SpecialWeaponBeaconLoop),
                    loopSpinStart = Count(_loopStart, CombatAudioEvent.HeavyWeaponSpinUp), loopSpinStop = Count(_loopStop, CombatAudioEvent.HeavyWeaponSpinUp),
                    damagedEvents = _damaged.Count, distinctTransactions = perTx.Count, nestedBegins = DamageTransaction.NestedBegins,
                    duplicateApplications = ZombieEnemy.DuplicateApplicationCount, pickupCount = SpecialWeaponSahne.Pickups()
                };
            }

            private IEnumerator Shot(Stage s, string file)
            {
                string path = Path.Combine(_outDir, file);
                ScreenCapture.CaptureScreenshot(path);
                s.screenshot = file;
                yield return null;
                yield return null;
            }

            private void Write(Stage s)
            {
                _stages.Add(s);
                File.WriteAllText(Path.Combine(_outDir, s.stage + ".json"), JsonUtility.ToJson(s, true));
                Debug.Log("[SpecialWeaponProbe] " + (s.pass ? "PASS " : "FAIL ") + s.stage + " | " + s.note + " | " + s.detail);
            }

            private static IEnumerator WaitForScene(string name)
            {
                yield return WaitUntil(
                    () => SceneManager.GetActiveScene().name == name && SceneManager.GetActiveScene().isLoaded,
                    "scene '" + name + "' not active");
            }

            private static IEnumerator WaitUntil(Func<bool> condition, string what)
            {
                float start = Time.realtimeSinceStartup;
                while (!condition())
                {
                    if (Time.realtimeSinceStartup - start > SceneTimeout)
                    {
                        Debug.LogError("[SpecialWeaponProbe] TIMEOUT: " + what + " (scene " + SceneManager.GetActiveScene().name + ")");
                        Application.Quit(3);
                        yield break;
                    }

                    yield return null;
                }
            }
        }

        /// <summary>Scene readouts the probe needs that have no registry: the live pickup(s).</summary>
        private static class SpecialWeaponSahne
        {
            public static int Pickups()
            {
                SpecialWeaponPickup[] all = UnityEngine.Object.FindObjectsOfType<SpecialWeaponPickup>();
                int n = 0;
                for (int i = 0; i < all.Length; i++) if (all[i].IsSpawned) n++;
                return n;
            }

            public static SpecialWeaponPickup Live()
            {
                SpecialWeaponPickup[] all = UnityEngine.Object.FindObjectsOfType<SpecialWeaponPickup>();
                for (int i = 0; i < all.Length; i++) if (all[i].IsSpawned) return all[i];
                return null;
            }
        }
    }
}
#endif
