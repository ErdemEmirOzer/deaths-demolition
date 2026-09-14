#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
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
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-006 · test-only regional armour / explosion probe for the real Player.
    ///
    /// Drives one real run through the public flow API and records, in the Player rather
    /// than the Editor, what the manual kit asks for: the 0/1/2/3 compositions on a
    /// Standard body with their visual states, a real rifle ray into each hitbox landing
    /// only on that region's plate, a helmet that breaks exactly once with the overflow
    /// discarded, the exposed head taking health, one blast splitting one budget across
    /// three plates with no health loss on a fully armoured body, one application per
    /// enemy per blast under one transaction id, a corpse taking nothing late (no second
    /// death, no second gold, no second kill), and a body checked out again from the pool
    /// carrying nothing from its previous life. Nothing here changes product behaviour: it
    /// calls the same public methods the weapons and the F1 panel call and reads public
    /// state. It also writes screenshots at the visible moments as manual-kit evidence.
    ///
    /// Compiled ONLY into development builds and the Editor (file-level #if); a release
    /// Player contains none of it, and without <c>-ddArmorProbe</c> a development Player
    /// allocates nothing either.
    ///
    /// Command line:
    ///   -ddArmorProbe &lt;outDir&gt;     enable; write stage JSON files + screenshots into outDir, then quit
    ///   -ddProfilePath &lt;file&gt;      redirect the profile file (keeps the real profile untouched)
    /// </summary>
    public static class ArmorProbe
    {
        private const string OutFlag = "-ddArmorProbe";
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
                Debug.Log("[ArmorProbe] profile path override -> " + profilePath);
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

            var carrier = new GameObject("ArmorProbe");
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

            public int lifeId;
            public int transactionId;
            public int eventId;
            public string kind;
            public string slot;
            public float healthBefore;
            public float healthAfter;
            public float helmetBefore;
            public float helmetAfter;
            public float bodyBefore;
            public float bodyAfter;
            public float legBefore;
            public float legAfter;
            public float armorAbsorbed;
            public float healthRemoved;
            public string visualHelmet;
            public string visualBody;
            public string visualLeg;
            public string resolution;
            public int regionFallbacks;
            public int duplicateApplications;
            public int overlapSaturation;
            public int nestedBegins;

            public int damagedEvents;
            public int brokenEvents;
            public int diedEvents;
            public int dressedEvents;

            public int totalKills;
            public int standardKills;
            public int armorPiecesBroken;
            public int ledgerGranted;
            public int ledgerRejectedDuplicate;

            public string screenshot;
            public string detail;
        }

        private sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeout = 30f;
            private string _outDir;
            private readonly List<Stage> _stages = new List<Stage>();
            private int _damaged, _broken, _died, _dressed;
            private readonly Dictionary<int, int> _damagedByLife = new Dictionary<int, int>();
            private readonly Dictionary<int, int> _dressedByTarget = new Dictionary<int, int>();

            internal void Configure(string outDir)
            {
                _outDir = outDir;
            }

            private void OnEnable()
            {
                DamageEvents.EnemyDamaged += OnDamaged;
                DamageEvents.ArmorBroken += OnBroken;
                DamageEvents.EnemyDied += OnDied;
                HitFeedbackDirector.EnemyHitDressed += OnDressed;
            }

            private void OnDisable()
            {
                DamageEvents.EnemyDamaged -= OnDamaged;
                DamageEvents.ArmorBroken -= OnBroken;
                DamageEvents.EnemyDied -= OnDied;
                HitFeedbackDirector.EnemyHitDressed -= OnDressed;
            }

            private EnemyDamageInfo _lastInfo;

            private void OnDamaged(EnemyDamageInfo i)
            {
                _lastInfo = i;
                _damaged++;
                _damagedByLife.TryGetValue(i.LifeId, out int n);
                _damagedByLife[i.LifeId] = n + 1;
            }

            private void OnBroken(ArmorBreakInfo i) { _broken++; }
            private void OnDied(EnemyDeathInfo i) { _died++; }

            private void OnDressed(EnemyHitFeedbackInfo i, HitFeedbackTier t)
            {
                _dressed++;
                _dressedByTarget.TryGetValue(i.TargetId, out int n);
                _dressedByTarget[i.TargetId] = n + 1;
            }

            private void ResetCounters()
            {
                _damaged = _broken = _died = _dressed = 0;
                _damagedByLife.Clear();
                _dressedByTarget.Clear();
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
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && GoldService.Instance != null &&
                                             PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting");

                RunDirector run = RunDirector.Instance;
                EnemySpawnDirector dir = EnemySpawnDirector.Instance;
                GoldService gold = GoldService.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                PrototypeConfig config = PrototypeConfig.Active;
                rig.Health.DiagnosisInvulnerable = true;

                // Controlled floor: no director spawns, no bodies. The probe places its own.
                SetSpawning(dir, false);
                dir.ClearAllEnemies();
                EnemyFactory.ClearAll();
                DamageTargetResolver.ResetRegionDiagnostics();
                yield return null;

                Vector3 basePos = rig.transform.position + new Vector3(0f, 0f, 7f);

                // ---------------------------------------------------------------- K1: compositions 0/1/2/3, visual states follow the model
                ZombieEnemy z = Spawn(rig, basePos, run.Round);
                yield return null;
                bool compOk = z != null && config.RoundDifficultyOverrides.Count == 0;
                var compDetail = new System.Text.StringBuilder();
                for (int mask = 0; mask < 8 && compOk; mask++)
                {
                    bool h = (mask & 1) != 0, b = (mask & 2) != 0, l = (mask & 4) != 0;
                    int fitted = z.FitArmorComposition(h, b, l);
                    int expected = (h ? 1 : 0) + (b ? 1 : 0) + (l ? 1 : 0);
                    bool ok = fitted == expected && z.Armor.EquippedCount == expected && z.Armor.ProtectingCount == expected &&
                              z.ArmorVisualStateOf(ArmorSlot.Helmet) == (h ? ArmorVisualState.Present : ArmorVisualState.Absent) &&
                              z.ArmorVisualStateOf(ArmorSlot.Body) == (b ? ArmorVisualState.Present : ArmorVisualState.Absent) &&
                              z.ArmorVisualStateOf(ArmorSlot.Leg) == (l ? ArmorVisualState.Present : ArmorVisualState.Absent);
                    compOk &= ok;
                    compDetail.Append(mask).Append(':').Append(fitted).Append(ok ? " " : "! ");
                }

                z.FitArmorComposition(true, true, true);
                yield return null;
                Stage s1 = Capture("01-compositions-0123", run, gold, z, "1,27");
                s1.pass = compOk && z.Armor.FullyProtected;
                s1.detail = "masks " + compDetail + " dec021rows=" + config.RoundDifficultyOverrides.Count;
                s1.note = "eight helmet/torso/leg masks fitted deterministically on one Standard body; visual state per slot follows the model; DEC-021 table empty";
                yield return Shot(s1, "01-full-armor.png");
                Write(s1);

                // ---------------------------------------------------------------- K2: real rifle ray into each plate, armour only
                ResetCounters();
                bool directOk = true;
                var directDetail = new System.Text.StringBuilder();
                foreach (BodyRegion region in new[] { BodyRegion.Head, BodyRegion.UpperBody, BodyRegion.LowerBody })
                {
                    ArmorSlot slot = DamageRegions.SlotFor(region);
                    float armorBefore = z.Armor.Current(slot);
                    float healthBefore = z.Health;
                    float otherA = OtherArmorSum(z, slot);
                    ShotResult shot = Fire(z, region, 5f);
                    DamageEventMeta m = z.LastDamageMeta;
                    bool ok = shot.EnemiesHit == 1 && DamageTargetResolver.LastResolution == RegionResolution.Registry &&
                              m.Kind == DamageKind.Direct && m.Slot == slot &&
                              Mathf.Approximately(z.Armor.Current(slot), armorBefore - 5f) && Mathf.Approximately(z.Health, healthBefore) &&
                              Mathf.Approximately(OtherArmorSum(z, slot), otherA) && z.ArmorVisualStateOf(slot) == ArmorVisualState.Damaged;
                    directOk &= ok;
                    directDetail.Append(region).Append('=').Append(armorBefore).Append("->").Append(z.Armor.Current(slot)).Append(ok ? " " : "! ");
                }

                Stage s2 = Capture("02-direct-hits-region-to-slot", run, gold, z, "4,5,6,7");
                s2.pass = directOk && _damaged == 3 && _dressed == 3 && _broken == 0 && DamageTargetResolver.RegionFallbackCount == 0;
                s2.detail = directDetail + "damaged=" + _damaged + " dressed=" + _dressed + " fallback=" + DamageTargetResolver.RegionFallbackCount;
                s2.note = "three real rays, one per hitbox: each lands only on its own plate, health untouched, other plates untouched, one dressing per hit, region resolved from the registry";
                Write(s2);

                // ---------------------------------------------------------------- K3: helmet breaks once, overflow discarded
                ResetCounters();
                float hBefore = z.Armor.Current(ArmorSlot.Helmet);
                float healthAtBreak = z.Health;
                ShotResult breakShot = Fire(z, BodyRegion.Head, 1000f);
                yield return null;
                Stage s3 = Capture("03-helmet-breaks-once", run, gold, z, "8,9,10");
                s3.pass = breakShot.AnyArmorBreak && _broken == 1 && z.Armor.IsBroken(ArmorSlot.Helmet) && Mathf.Approximately(z.Health, healthAtBreak) &&
                          z.ArmorVisualStateOf(ArmorSlot.Helmet) == ArmorVisualState.Broken && run.Stats.ArmorPiecesBroken == 1 &&
                          z.LastDamageMeta.ArmorBefore(ArmorSlot.Helmet) > 0f && Mathf.Approximately(z.LastDamageMeta.ArmorAfter(ArmorSlot.Helmet), 0f);
                s3.detail = "helmet " + hBefore + "->0 from a 1000 hit; health " + healthAtBreak + "->" + z.Health + " broken=" + _broken + " statBreaks=" + run.Stats.ArmorPiecesBroken;
                s3.note = "a 1000-damage ray on a helmet: the plate breaks exactly once, the overflow is discarded, health unchanged, the plate is visually gone";
                yield return Shot(s3, "03-helmet-broken.png");
                Write(s3);

                // ---------------------------------------------------------------- K4: exposed head takes health (existing PrototypeOnly multiplier)
                // The break above staggers the body (WP-002 hit reaction): in the reaction pose
                // the torso hitbox can sit in front of the head along a level ray, so a "head"
                // shot fired during the stagger lands on the torso plate. That is geometry, not
                // routing; the probe lets the pose settle (real time, no scene search) and records
                // which region each attempt actually struck, so nothing is silent in the evidence.
                float settleStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - settleStart < 1.5f) yield return null;
                ResetCounters();
                float healthBeforeExposed = z.Health;
                int exposedFrame = -1;
                var exposedTrace = new System.Text.StringBuilder();
                for (int frame = 0; frame < 30 && exposedFrame < 0; frame++)
                {
                    Collider hb = z.HitboxFor(BodyRegion.Head);
                    float headShotHealthBefore = z.Health;
                    ShotResult exposedShot = Fire(z, BodyRegion.Head, 1f);
                    bool landedOnHead = exposedShot.EnemiesHit > 0 && _lastInfo.Result.Region == BodyRegion.Head && z.Health < headShotHealthBefore;
                    if (landedOnHead)
                    {
                        exposedFrame = frame;
                        break;
                    }

                    exposedTrace.Append("f").Append(frame).Append(":hit=").Append(exposedShot.HitSomething).Append("/").Append(exposedShot.EnemiesHit)
                        .Append(" region=").Append(exposedShot.EnemiesHit > 0 ? _lastInfo.Result.Region.ToString() : "-")
                        .Append(" hb=").Append(hb != null ? hb.bounds.center.ToString("F2") : "null").Append(hb != null && hb.enabled ? "" : "(off)")
                        .Append(" pos=").Append(z.transform.position.ToString("F2")).Append(" ts=").Append(Time.timeScale.ToString("F2")).Append("; ");
                    yield return null;
                }

                DamageEventMeta exposed = z.LastDamageMeta;
                float expectedExposed = 1f * config.Enemies.Armor.ExposedHeadMultiplier;
                Stage s4 = Capture("04-exposed-head-health", run, gold, z, "9");
                s4.pass = exposedFrame >= 0 && exposed.Kind == DamageKind.Direct && exposed.Slot == ArmorSlot.Helmet && Mathf.Approximately(exposed.HealthRemoved, expectedExposed) &&
                          Mathf.Approximately(exposed.ArmorAbsorbed, 0f) && (DamageEventClassifier.Classify(in _lastInfo) & DamageEventClass.DirectHealthDamage) != 0 &&
                          _broken == 0;
                s4.detail = "health " + healthBeforeExposed + "->" + z.Health + " removed=" + exposed.HealthRemoved + " expected=" + expectedExposed +
                            " (config multiplier, PrototypeOnly) landedOnHeadAtAttempt=" + exposedFrame + " offTarget=[" + exposedTrace + "]";
                s4.note = "after the break the same ray reaches health; amount equals the existing PrototypeOnly exposed-head multiplier from config";
                Write(s4);

                // ---------------------------------------------------------------- K5/K6: one blast, three bodies, one application each, budget preserved
                EnemyFactory.ClearAll();
                yield return null;
                Vector3 c = basePos + new Vector3(0f, 0f, 2f);
                ZombieEnemy a3 = Spawn(rig, c + new Vector3(-0.9f, 0f, 0f), run.Round);
                ZombieEnemy a1 = Spawn(rig, c + new Vector3(0.9f, 0f, 0f), run.Round);
                ZombieEnemy a0 = Spawn(rig, c + new Vector3(0f, 0f, 0.9f), run.Round);
                yield return null;
                a3.FitArmorComposition(true, true, true);
                a1.FitArmorComposition(true, false, false);
                a0.FitArmorComposition(false, false, false);
                ResetCounters();
                const float total = 30f;
                int saturationBefore = HitscanEngine.OverlapSaturationCount;
                HitscanEngine.Explode(c + Vector3.up * 0.9f, total, 3f);
                yield return null;
                bool oneEach = Count(_damagedByLife, a3.LifeId) == 1 && Count(_damagedByLife, a1.LifeId) == 1 && Count(_damagedByLife, a0.LifeId) == 1 &&
                               Count(_dressedByTarget, a3.GetInstanceID()) == 1 && Count(_dressedByTarget, a1.GetInstanceID()) == 1 && Count(_dressedByTarget, a0.GetInstanceID()) == 1;
                bool sameTx = a3.LastDamageMeta.TransactionId > 0 && a3.LastDamageMeta.TransactionId == a1.LastDamageMeta.TransactionId &&
                              a1.LastDamageMeta.TransactionId == a0.LastDamageMeta.TransactionId;
                bool budget = a3.LastDamageMeta.ArmorAbsorbed + a3.LastDamageMeta.HealthRemoved <= total + 0.001f &&
                              a1.LastDamageMeta.ArmorAbsorbed + a1.LastDamageMeta.HealthRemoved <= total + 0.001f &&
                              a0.LastDamageMeta.ArmorAbsorbed + a0.LastDamageMeta.HealthRemoved <= total + 0.001f;
                bool fullNoHealth = Mathf.Approximately(a3.LastDamageMeta.HealthRemoved, 0f) && a3.LastDamageMeta.HeadArmorDamage > 0f &&
                                    a3.LastDamageMeta.UpperArmorDamage > 0f && a3.LastDamageMeta.LowerArmorDamage > 0f;
                bool helmetOnly = a1.LastDamageMeta.HeadArmorDamage > 0f && Mathf.Approximately(a1.LastDamageMeta.HeadHealthDamage, 0f) &&
                                  a1.LastDamageMeta.UpperHealthDamage > 0f && a1.LastDamageMeta.LowerHealthDamage > 0f;
                bool bare = Mathf.Approximately(a0.LastDamageMeta.ArmorAbsorbed, 0f) && a0.LastDamageMeta.HealthRemoved > 0f;
                Stage s5 = Capture("05-explosion-one-budget-three-bodies", run, gold, a3, "11,12,13,14,16,17,28");
                s5.pass = oneEach && sameTx && budget && fullNoHealth && helmetOnly && bare && _broken == 0 && _died == 0 &&
                          a3.LastDamageMeta.Kind == DamageKind.Explosion && ZombieEnemy.DuplicateApplicationCount == 0 &&
                          HitscanEngine.OverlapSaturationCount == saturationBefore;
                s5.detail = "tx=" + a3.LastDamageMeta.TransactionId + " full=" + a3.LastDamageMeta.ArmorAbsorbed + "/" + a3.LastDamageMeta.HealthRemoved +
                            " helmetOnly=" + a1.LastDamageMeta.ArmorAbsorbed + "/" + a1.LastDamageMeta.HealthRemoved +
                            " bare=" + a0.LastDamageMeta.ArmorAbsorbed + "/" + a0.LastDamageMeta.HealthRemoved +
                            " damaged=" + _damaged + " dressed=" + _dressed + " dup=" + ZombieEnemy.DuplicateApplicationCount;
                s5.note = "one 30-damage blast over three bodies (3/1/0 plates): each body applied and dressed exactly once under one transaction, budget never exceeds the total, fully armoured body loses no health, helmet-only body loses health only through torso+leg shares, bare body takes all to health";
                yield return Shot(s5, "05-explosion.png");
                Write(s5);

                // ---------------------------------------------------------------- K6: corpse takes nothing late; kill/gold/break counted once
                EnemyFactory.ClearAll();
                yield return null;
                ZombieEnemy d = Spawn(rig, basePos, run.Round);
                yield return null;
                d.FitArmorComposition(true, true, true);
                int killsBefore = run.Stats.KillsFor(EnemyTier.Standard);
                int breaksBefore = run.Stats.ArmorPiecesBroken;
                int grantedBefore = gold.Ledger.Granted;
                int dupBefore = gold.Ledger.RejectedDuplicate;
                ResetCounters();
                Fire(d, BodyRegion.Head, 1000f);
                int guard = 0;
                while (d.IsAlive && guard++ < 20)
                {
                    Fire(d, BodyRegion.Head, 50f);
                }

                yield return null;
                int diedAtDeath = _died, brokenAtDeath = _broken, damagedAtDeath = _damaged, dressedAtDeath = _dressed;
                Fire(d, BodyRegion.UpperBody, 50f);
                DamageResult late = d.ApplyHit(new DamageHit { Amount = 100f, Region = BodyRegion.UpperBody, Point = d.transform.position, Direction = Vector3.forward, ImpactForce = 1f, Source = DamageSourceKind.Bullet });
                HitscanEngine.Explode(d.transform.position + Vector3.up, 500f, 3f);
                yield return null;
                Stage s6 = Capture("06-corpse-late-damage-nothing", run, gold, d, "22,23,24");
                s6.pass = !d.IsAlive && diedAtDeath == 1 && brokenAtDeath == 1 && _died == 1 && _broken == 1 && _damaged == damagedAtDeath && _dressed == dressedAtDeath &&
                          DamageResults.IsEmpty(in late) && run.Stats.KillsFor(EnemyTier.Standard) == killsBefore + 1 && run.Stats.ArmorPiecesBroken == breaksBefore + 1 &&
                          gold.Ledger.Granted == grantedBefore + 1 && gold.Ledger.RejectedDuplicate == dupBefore;
                s6.detail = "died=" + _died + " broken=" + _broken + " standardKills+" + (run.Stats.KillsFor(EnemyTier.Standard) - killsBefore) +
                            " breaks+" + (run.Stats.ArmorPiecesBroken - breaksBefore) + " granted+" + (gold.Ledger.Granted - grantedBefore) + " lateEmpty=" + DamageResults.IsEmpty(in late);
                s6.note = "helmet broken then killed through the exposed head: one death, one break, one Standard kill, one gold grant; a late ray, a late direct call and a late blast on the corpse produce nothing";
                Write(s6);

                // ---------------------------------------------------------------- K7: pool second life clean
                int oldLife = d.LifeId;
                EnemyFactory.ClearAll();
                yield return null;
                ZombieEnemy p = Spawn(rig, basePos, run.Round);
                yield return null;
                bool cleanLife = p != null && p.LifeId > oldLife && p.IsAlive && Mathf.Approximately(p.Health, p.MaxHealth) && !p.LastDamageMeta.IsPresent &&
                                 !p.Armor.IsBroken(ArmorSlot.Helmet) && !p.Armor.IsBroken(ArmorSlot.Body) && !p.Armor.IsBroken(ArmorSlot.Leg) &&
                                 p.ArmorVisualStateOf(ArmorSlot.Helmet) != ArmorVisualState.Broken && p.ArmorVisualStateOf(ArmorSlot.Body) != ArmorVisualState.Broken &&
                                 p.ArmorVisualStateOf(ArmorSlot.Leg) != ArmorVisualState.Broken && p.HitboxFor(BodyRegion.Head) != null && p.HitboxFor(BodyRegion.Head).enabled;
                Stage s7 = Capture("07-pool-second-life-clean", run, gold, p, "21");
                s7.pass = cleanLife && DamageTransaction.NestedBegins == 0 && DamageTransaction.Current == 0;
                s7.detail = "life " + oldLife + "->" + (p != null ? p.LifeId : -1) + " sameBody=" + ReferenceEquals(d, p) + " armor=" + (p != null ? p.DescribeArmor() : "-");
                s7.note = "a body checked out again after a death carries no broken plate, no stale meta, full health, live hitboxes; no transaction left open";
                Write(s7);

                // ---------------------------------------------------------------- K8: pause freezes armour state (corpse linger as positive control)
                p.FitArmorComposition(true, false, true);
                Fire(p, BodyRegion.Head, 5f);
                float pausedHelmet = p.Armor.Current(ArmorSlot.Helmet);
                float pausedHealth = p.Health;
                ZombieEnemy corpse = Spawn(rig, basePos + new Vector3(3f, 0f, 0f), run.Round);
                yield return null;
                if (corpse != null) corpse.Kill();
                bool corpseLingered = corpse != null && corpse.IsSpawned;
                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                yield return null;
                float pauseStart = Time.realtimeSinceStartup;
                int frozenFrames = 0;
                bool frozenOk = Time.timeScale == 0f;
                while (Time.realtimeSinceStartup - pauseStart < 1.2f)
                {
                    frozenOk &= Time.deltaTime == 0f && Mathf.Approximately(p.Armor.Current(ArmorSlot.Helmet), pausedHelmet) && Mathf.Approximately(p.Health, pausedHealth) &&
                                p.IsAlive && p.ArmorVisualStateOf(ArmorSlot.Helmet) == ArmorVisualState.Damaged && corpse != null && corpse.IsSpawned;
                    frozenFrames++;
                    yield return null;
                }

                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                yield return null;
                float resumeStart = Time.realtimeSinceStartup;
                while (corpse != null && corpse.IsSpawned && Time.realtimeSinceStartup - resumeStart < 3f) yield return null;
                bool corpseDespawnedAfterResume = corpse != null && !corpse.IsSpawned;
                Stage s8 = Capture("08-pause-freezes-armor-state", run, gold, p, "25");
                s8.pass = corpseLingered && frozenOk && frozenFrames > 10 && corpseDespawnedAfterResume && Time.timeScale == 1f;
                s8.detail = "frozenFrames=" + frozenFrames + " helmet=" + pausedHelmet + " health=" + pausedHealth + " corpseHeldUnderPause=" + frozenOk + " corpseDespawnedAfterResume=" + corpseDespawnedAfterResume;
                s8.note = "under the pause hold for 1.2 s real time the damaged plate, health and a corpse's despawn countdown all stay frozen; on resume the countdown runs and the corpse returns to the pool";
                Write(s8);

                // ---------------------------------------------------------------- K9: hit path allocation in the Player (300 direct + 30 blasts)
                EnemyFactory.ClearAll();
                yield return null;
                var four = new ZombieEnemy[4];
                for (int i = 0; i < 4; i++)
                {
                    four[i] = Spawn(rig, c + new Vector3((i - 1.5f) * 0.9f, 0f, 0f), run.Round);
                }

                yield return null;
                for (int i = 0; i < 4; i++)
                {
                    if (four[i] == null) continue;
                    four[i].FitArmorComposition(true, true, true);
                    four[i].Armor.Equip(ArmorSlot.Helmet, 100000f);
                    four[i].Armor.Equip(ArmorSlot.Body, 100000f);
                    four[i].Armor.Equip(ArmorSlot.Leg, 100000f);
                }

                for (int i = 0; i < 20; i++)
                {
                    Fire(four[i % 4], BodyRegion.UpperBody, 1f);
                    HitscanEngine.Explode(c + Vector3.up * 0.9f, 1f, 3f);
                }

                yield return null;

                // WP-010 (D-1): the measurement must not depend on an idle machine. A full
                // collection right before the windows empties the nursery so a gen0 that was
                // already due does not land inside a window; a window that still sees a gen0
                // is measured once more after another collection, and the verdict reads the
                // heap delta (positive-controlled) together with the gen0 count.
                HeapWindow.Prepare();
                yield return null;
                yield return null;
                byte[] yemA = null;
                HeapWindow.Result ctrlA = HeapWindow.Measure(() => { yemA = new byte[64 * 1024]; }, 0);
                long ctrlAgc = ctrlA.GcDelta; long ctrlAmono = ctrlA.MonoDelta;
                object[] yemB = new object[300];
                HeapWindow.Result ctrlB = HeapWindow.Measure(() => { for (int i = 0; i < yemB.Length; i++) yemB[i] = new byte[80]; }, 0);
                long ctrlBgc = ctrlB.GcDelta; long ctrlBmono = ctrlB.MonoDelta;
                // Four windows: direct ×300, blast ×30, then the same two again. A first-pass
                // cost that vanishes on the repeat is one-time warm-up (a pool growing to the
                // burst size); a steady-state cost that repeats is a per-hit allocation.
                // Windows 4/5 repeat the pair with the HUD damage numbers switched off: the
                // number text (DamageNumberEffect.Refresh, int.ToString) is the one hit consumer
                // that must build a string per hit - a WP-002 presentation surface, not the
                // WP-006 damage path - and the Editor test runs where no number is rendered.
                HitFeedbackDirector feedback = HitFeedbackDirector.Instance;
                bool numbersWere = feedback != null && feedback.ShowDamageNumbers;
                long[] winGc = new long[6]; long[] winMono = new long[6]; int[] winEvents = new int[6]; long[] winGen0 = new long[6]; int[] winRetries = new int[6];
                for (int w = 0; w < 6; w++)
                {
                    if (w == 4 && feedback != null) feedback.ShowDamageNumbers = false;
                    bool direct = (w & 1) == 0;
                    // A collection landing inside the window (machine load / a due nursery
                    // sweep) re-measures the same window up to twice after a collection (HeapWindow).
                    HeapWindow.Result r = HeapWindow.Measure(() =>
                    {
                        ResetCounters();
                        if (direct)
                        {
                            for (int i = 0; i < 300; i++) Fire(four[i % 4], (BodyRegion)(i % 3), 1f);
                        }
                        else
                        {
                            for (int i = 0; i < 30; i++) HitscanEngine.Explode(c + Vector3.up * 0.9f, 1f, 3f);
                        }
                    }, 2);
                    winGc[w] = r.GcDelta; winMono[w] = r.MonoDelta; winGen0[w] = r.Gen0; winRetries[w] = r.Retries; winEvents[w] = _damaged;
                    yield return null;
                }

                if (feedback != null) feedback.ShowDamageNumbers = numbersWere;

                // Bisect: the audio director (WP-002 surface, untouched here) is the one hit
                // consumer that only runs with a real audio device - the Editor test runs in
                // batch mode where it is silent. 300 bare audio cues, same counters.
                ResetCounters();
                HeapWindow.Result audio = HeapWindow.Measure(() =>
                {
                    for (int i = 0; i < 300; i++)
                    {
                        AudioEvents.Raise(CombatAudioEvent.ArmorHit, four[i % 4].transform.position + Vector3.up, 1f);
                    }
                }, 1);
                long audioGc = audio.GcDelta; long audioMono = audio.MonoDelta; long audioGen0 = audio.Gen0;
                yield return null;

                bool smallSensitive = ctrlBgc > 0 || ctrlBmono > 0;
                long steadyDirect = ctrlBmono > 0 ? winMono[2] : winGc[2];
                long steadyBlast = ctrlBmono > 0 ? winMono[3] : winGc[3];
                long pathDirect = ctrlBmono > 0 ? winMono[4] : winGc[4];    // damage path alone (numbers off)
                long pathBlast = ctrlBmono > 0 ? winMono[5] : winGc[5];
                long gen0Total = winGen0[0] + winGen0[1] + winGen0[2] + winGen0[3] + winGen0[4] + winGen0[5];
                int retriesTotal = winRetries[0] + winRetries[1] + winRetries[2] + winRetries[3] + winRetries[4] + winRetries[5];
                // WP-010 (D-1): the verdict is the positive-controlled heap delta of the windows
                // that matter (steady state and numbers-off). A gen0 that still lands inside one
                // of THOSE windows after two re-measurements makes the delta unreadable (a sweep
                // can hide the window's own garbage), so it is inconclusive - reported, never
                // green; a gen0 in the warm-up pair (windows 0/1) is reported only.
                bool verdictWindowsClean = winGen0[2] == 0 && winGen0[3] == 0 && (!smallSensitive || (winGen0[4] == 0 && winGen0[5] == 0));
                bool heapOk = verdictWindowsClean && winGc[2] <= 16 * 1024 && winGc[3] <= 16 * 1024 && (!smallSensitive || (pathDirect <= 4 * 1024 && pathBlast <= 4 * 1024));
                Stage s9 = Capture("09-hit-path-allocation", run, gold, four[0], "26");
                s9.pass = winEvents[0] == 300 && winEvents[1] == 30 * 4 && winEvents[2] == 300 && winEvents[3] == 30 * 4 && winEvents[4] == 300 && winEvents[5] == 30 * 4 &&
                          (ctrlAgc + ctrlAmono) > 0 && heapOk;
                s9.detail = "controlA(64KB) gc=" + ctrlAgc + " mono=" + ctrlAmono + " controlB(300x80B) gc=" + ctrlBgc + " mono=" + ctrlBmono +
                            " | pass1 direct(" + winEvents[0] + ") gc=" + winGc[0] + " mono=" + winMono[0] + " blast(" + winEvents[1] + ") gc=" + winGc[1] + " mono=" + winMono[1] +
                            " | pass2 direct(" + winEvents[2] + ") gc=" + winGc[2] + " mono=" + winMono[2] + " blast(" + winEvents[3] + ") gc=" + winGc[3] + " mono=" + winMono[3] +
                            " | numbersOff direct(" + winEvents[4] + ") gc=" + winGc[4] + " mono=" + winMono[4] + " blast(" + winEvents[5] + ") gc=" + winGc[5] + " mono=" + winMono[5] +
                            " | audioOnly(300 cues) gc=" + audioGc + " mono=" + audioMono + " gen0=" + audioGen0 +
                            " | steadyWithNumbers direct=" + steadyDirect + " blast=" + steadyBlast + " (HUD number text, WP-002 surface)" +
                            " gen0=" + gen0Total + " gen0Retries=" + retriesTotal + " verdictWindowsClean=" + verdictWindowsClean + (verdictWindowsClean ? "" : " INCONCLUSIVE(gen0 inside a verdict window after 2 retries)") + " heapOk=" + heapOk + " class=" + (ctrlBmono > 0 ? "mono-small-sensitive" : ctrlBgc > 0 ? "gc-small-sensitive" : "large-class-only-OBSERVATION") + " yem=" + yemA.Length + "/" + yemB.Length;
                s9.note = "300 direct rays and 30 blasts over four fully armoured bodies, run twice in the real Player: the repeat pass is the steady-state hit path (no gen0, within threshold); a first-pass-only cost is one-time warm-up and is reported, not hidden; the numbers-off pair isolates the damage path from the HUD damage-number text (a pre-existing WP-002 presentation surface that builds one string per shown hit), and the audio-only window rules the audio director in or out";
                Write(s9);
                EnemyFactory.ClearAll();

                // summary
                int passCount = 0;
                for (int i = 0; i < _stages.Count; i++) if (_stages[i].pass) passCount++;
                File.WriteAllText(Path.Combine(_outDir, "summary.txt"),
                    "ArmorProbe " + DateTime.UtcNow.ToString("o") + " stages=" + _stages.Count + " pass=" + passCount + "\n" +
                    string.Join("\n", StageLines()) + "\n");
                Debug.Log("[ArmorProbe] DONE stages=" + _stages.Count + " pass=" + passCount);

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

            private static ZombieEnemy Spawn(PlayerRig rig, Vector3 pos, int round)
            {
                Vector3 look = rig.transform.position - pos; look.y = 0f;
                Quaternion rot = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity;
                return EnemyFactory.Spawn(EnemyTier.Standard, round, pos, rot);
            }

            /// <summary>Real weapon path: a rifle bullet from 2.5 m, level with the hitbox centre, from the body's front.</summary>
            private static ShotResult Fire(ZombieEnemy z, BodyRegion region, float damage)
            {
                Collider hitbox = z.HitboxFor(region);
                Vector3 target = hitbox != null ? hitbox.bounds.center : z.transform.position + Vector3.up;
                Vector3 back = -z.transform.forward; back.y = 0f;
                back = back.sqrMagnitude > 0.0001f ? back.normalized : Vector3.back;
                Vector3 origin = target - back * 2.5f; origin.y = target.y;
                var request = new ShotRequest
                {
                    Origin = origin,
                    Direction = (target - origin).normalized,
                    Damage = damage,
                    Range = 6f,
                    ImpactForce = DamageForce.RifleBullet,
                    Source = DamageSourceKind.Bullet,
                    Power = WeaponPower.Medium,
                    PierceTargets = 0,
                    PierceDamageFalloff = 1f,
                    DrawTracer = false
                };
                return HitscanEngine.Fire(in request);
            }

            private static float OtherArmorSum(ZombieEnemy z, ArmorSlot except)
            {
                float sum = 0f;
                if (except != ArmorSlot.Helmet) sum += z.Armor.Current(ArmorSlot.Helmet);
                if (except != ArmorSlot.Body) sum += z.Armor.Current(ArmorSlot.Body);
                if (except != ArmorSlot.Leg) sum += z.Armor.Current(ArmorSlot.Leg);
                return sum;
            }

            private static int Count(Dictionary<int, int> d, int key) => d.TryGetValue(key, out int n) ? n : 0;

            private static void SetSpawning(EnemySpawnDirector dir, bool on)
            {
                // The F1 "Spawning enabled" switch. Diagnostics only.
                System.Reflection.FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) f.SetValue(dir, on);
            }

            private Stage Capture(string stage, RunDirector r, GoldService g, ZombieEnemy z, string kit)
            {
                bool hasRun = !ReferenceEquals(r, null);
                bool hasGold = !ReferenceEquals(g, null);
                bool hasZ = z != null;
                DamageEventMeta m = hasZ ? z.LastDamageMeta : default;

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
                    lifeId = hasZ ? z.LifeId : 0,
                    transactionId = m.TransactionId,
                    eventId = m.EventId,
                    kind = m.Kind.ToString(),
                    slot = m.Slot.ToString(),
                    healthBefore = m.HealthBefore,
                    healthAfter = m.HealthAfter,
                    helmetBefore = m.HelmetBefore,
                    helmetAfter = m.HelmetAfter,
                    bodyBefore = m.BodyBefore,
                    bodyAfter = m.BodyAfter,
                    legBefore = m.LegBefore,
                    legAfter = m.LegAfter,
                    armorAbsorbed = m.ArmorAbsorbed,
                    healthRemoved = m.HealthRemoved,
                    visualHelmet = hasZ ? z.ArmorVisualStateOf(ArmorSlot.Helmet).ToString() : "-",
                    visualBody = hasZ ? z.ArmorVisualStateOf(ArmorSlot.Body).ToString() : "-",
                    visualLeg = hasZ ? z.ArmorVisualStateOf(ArmorSlot.Leg).ToString() : "-",
                    resolution = DamageTargetResolver.LastResolution.ToString(),
                    regionFallbacks = DamageTargetResolver.RegionFallbackCount,
                    duplicateApplications = ZombieEnemy.DuplicateApplicationCount,
                    overlapSaturation = HitscanEngine.OverlapSaturationCount,
                    nestedBegins = DamageTransaction.NestedBegins,
                    damagedEvents = _damaged,
                    brokenEvents = _broken,
                    diedEvents = _died,
                    dressedEvents = _dressed,
                    totalKills = hasRun ? r.Stats.TotalKills : 0,
                    standardKills = hasRun ? r.Stats.KillsFor(EnemyTier.Standard) : 0,
                    armorPiecesBroken = hasRun ? r.Stats.ArmorPiecesBroken : 0,
                    ledgerGranted = hasGold ? g.Ledger.Granted : 0,
                    ledgerRejectedDuplicate = hasGold ? g.Ledger.RejectedDuplicate : 0
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
                Debug.Log("[ArmorProbe] " + (s.pass ? "PASS " : "FAIL ") + s.stage + " | " + s.note + " | " + s.detail);
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
                        Debug.LogError("[ArmorProbe] TIMEOUT: " + what + " (scene " + SceneManager.GetActiveScene().name + ")");
                        Application.Quit(3);
                        yield break;
                    }

                    yield return null;
                }
            }
        }
    }
}
#endif
