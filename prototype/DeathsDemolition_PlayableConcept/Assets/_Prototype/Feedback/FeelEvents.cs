using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Feedback
{
    /// <summary>WP-095 (R3, C-00 07.10): the game-feel moments, one per <see cref="FeelEvents"/> event (counters, telemetry).</summary>
    public enum FeelEventKind
    {
        ShotFired = 0,
        EnemyHit = 1,
        EnemyKilled = 2,
        MultiKill = 3,
        Explosion = 4,
        PlayerDamaged = 5,
        LowHealth = 6,
        ReloadStarted = 7,
        ReloadEnded = 8,
        DryFire = 9,
        GoldCollected = 10,
        RoundStarted = 11,
        RoundEnded = 12,
        LastZombie = 13,
    }

    /// <summary>One trigger pull (a shotgun's pellets are one shot). Raised after the round is spent.</summary>
    public readonly struct FeelShot
    {
        public FeelShot(bool special, ClassicWeaponId classic, SpecialWeaponId specialId, WeaponFeelId feel, Vector3 origin,
            Vector3 direction, int pellets, int groupId, int shotSerial, float powerScale, int ammoLeft, int magazineSize)
        {
            Special = special; Classic = classic; SpecialId = specialId; Feel = feel; Origin = origin; Direction = direction;
            Pellets = pellets; GroupId = groupId; ShotSerial = shotSerial; PowerScale = powerScale; AmmoLeft = ammoLeft;
            MagazineSize = magazineSize;
        }

        public readonly bool Special;
        public readonly ClassicWeaponId Classic;
        public readonly SpecialWeaponId SpecialId;
        public readonly WeaponFeelId Feel;
        /// <summary>The shot origin (top-down: the fixed shot height above the root), not the visual muzzle.</summary>
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;
        public readonly int Pellets;
        /// <summary>The trigger pull's presentation group (<see cref="FeelEventRouter.NextGroupId"/>); 0 for a Special.</summary>
        public readonly int GroupId;
        /// <summary>The classic shot serial (WeaponController.ShotSerial); 0 for a Special.</summary>
        public readonly int ShotSerial;
        public readonly float PowerScale;
        public readonly int AmmoLeft;
        public readonly int MagazineSize;
        public bool LastRound => AmmoLeft <= 0;
    }

    /// <summary>
    /// One damage application on one zombie (DamageEvents.EnemyDamaged): per pellet per body, per body per blast, per ground-fire
    /// tick. A critical is a flag here (DamageRegions.IsCritical), not an event of its own.
    /// </summary>
    public readonly struct FeelHit
    {
        public FeelHit(Transform enemy, EnemyTier tier, int lifeId, Vector3 point, Vector3 direction, float healthDamage,
            float armorDamage, bool armorBroke, bool killed, bool critical, BodyRegion region, DamageSourceKind source,
            float impactForce, float healthRatio, bool stagger, bool groundFire, int groupId, bool fromClassic,
            ClassicWeaponId classic, bool fromSpecial, SpecialWeaponId specialId)
        {
            Enemy = enemy; Tier = tier; LifeId = lifeId; Point = point; Direction = direction; HealthDamage = healthDamage;
            ArmorDamage = armorDamage; ArmorBroke = armorBroke; Killed = killed; Critical = critical; Region = region;
            Source = source; ImpactForce = impactForce; HealthRatio = healthRatio; Stagger = stagger; GroundFire = groundFire;
            GroupId = groupId; FromClassic = fromClassic; Classic = classic; FromSpecial = fromSpecial; SpecialId = specialId;
        }

        public readonly Transform Enemy;
        public readonly EnemyTier Tier;
        public readonly int LifeId;
        public readonly Vector3 Point;
        /// <summary>The hit's travel direction (shooter → enemy).</summary>
        public readonly Vector3 Direction;
        public readonly float HealthDamage;
        public readonly float ArmorDamage;
        public readonly bool ArmorBroke;
        public readonly bool Killed;
        /// <summary>Unarmoured head, direct hit, health taken (DamageRegions.IsCritical) — lethal or not.</summary>
        public readonly bool Critical;
        public readonly BodyRegion Region;
        public readonly DamageSourceKind Source;
        public readonly float ImpactForce;
        public readonly float HealthRatio;
        public readonly bool Stagger;
        /// <summary>A run-tree ground-fire tick (damage over time): most layers should skip it.</summary>
        public readonly bool GroundFire;
        /// <summary>The trigger pull's presentation group, 0 when the hit did not come from a grouped shot / blast.</summary>
        public readonly int GroupId;
        public readonly bool FromClassic;
        public readonly ClassicWeaponId Classic;
        public readonly bool FromSpecial;
        public readonly SpecialWeaponId SpecialId;
    }

    /// <summary>One zombie life ended (DamageEvents.EnemyDied — exactly once per life, every source).</summary>
    public readonly struct FeelKill
    {
        public FeelKill(Transform enemy, EnemyTier tier, int lifeId, Vector3 position, Vector3 direction, BodyRegion region,
            DamageSourceKind source, bool critical, bool heavy, float overkill, int goldReward, int round, int groupId,
            bool fromClassic, ClassicWeaponId classic, bool fromSpecial, SpecialWeaponId specialId, int groupKills,
            int chainKills, bool lastOfRound)
        {
            Enemy = enemy; Tier = tier; LifeId = lifeId; Position = position; Direction = direction; Region = region;
            Source = source; Critical = critical; Heavy = heavy; Overkill = overkill; GoldReward = goldReward; Round = round;
            GroupId = groupId; FromClassic = fromClassic; Classic = classic; FromSpecial = fromSpecial; SpecialId = specialId;
            GroupKills = groupKills; ChainKills = chainKills; LastOfRound = lastOfRound;
        }

        public readonly Transform Enemy;
        public readonly EnemyTier Tier;
        public readonly int LifeId;
        public readonly Vector3 Position;
        public readonly Vector3 Direction;
        public readonly BodyRegion Region;
        public readonly DamageSourceKind Source;
        public readonly bool Critical;
        /// <summary>Elite / Ultra (EnemyDeathInfo.WasHeavyDeath).</summary>
        public readonly bool Heavy;
        public readonly float Overkill;
        public readonly int GoldReward;
        public readonly int Round;
        public readonly int GroupId;
        public readonly bool FromClassic;
        public readonly ClassicWeaponId Classic;
        public readonly bool FromSpecial;
        public readonly SpecialWeaponId SpecialId;
        /// <summary>Kills of this trigger pull / blast so far, this one included (1 when ungrouped).</summary>
        public readonly int GroupKills;
        /// <summary>Kills inside the sliding chain window (<see cref="FeelEventThresholds.MultiKillChainWindowSeconds"/>), this one included.</summary>
        public readonly int ChainKills;
        /// <summary>The round's pool is empty and nobody is left alive: <see cref="FeelEvents.LastZombie"/> follows.</summary>
        public readonly bool LastOfRound;
    }

    /// <summary>A multi-kill reached a new tier (raised on each promotion only: T1, then T2, then T3).</summary>
    public readonly struct FeelMultiKill
    {
        public FeelMultiKill(int tier, int count, bool sameGroup, int groupId, Vector3 position)
        {
            Tier = tier; Count = count; SameGroup = sameGroup; GroupId = groupId; Position = position;
        }

        /// <summary>1..3 (his sözleşmesi §4.4: T1 3–4, T2 5–7, T3 8+).</summary>
        public readonly int Tier;
        public readonly int Count;
        /// <summary>True when one trigger pull / blast made it (a hit-stop may run); false for a chain over the window (none).</summary>
        public readonly bool SameGroup;
        public readonly int GroupId;
        public readonly Vector3 Position;
    }

    /// <summary>One blast (HitFeedbackDirector.ReportExplosion — before its damage lands; a split rocket raises one per split).</summary>
    public readonly struct FeelExplosion
    {
        public FeelExplosion(Vector3 origin, float radius, int groupId, ExplosionSource source)
        {
            Origin = origin; Radius = radius; GroupId = groupId; Source = source;
        }

        public readonly Vector3 Origin;
        public readonly float Radius;
        public readonly int GroupId;
        public readonly ExplosionSource Source;
    }

    /// <summary>The player lost health (PlayerHealth.AnyDamaged). Invulnerable / second-wind refusals raise nothing.</summary>
    public readonly struct FeelPlayerDamage
    {
        public FeelPlayerDamage(Vector3 position, Vector3 direction, float amount, float healthBefore, float healthAfter,
            float maxHealth, bool fatal, bool heavy)
        {
            Position = position; Direction = direction; Amount = amount; HealthBefore = healthBefore; HealthAfter = healthAfter;
            MaxHealth = maxHealth; Fatal = fatal; Heavy = heavy;
        }

        public readonly Vector3 Position;
        /// <summary>Flat attacker → player direction (PlayerDamageInfo.HitDirection; not WorldPoint, which is the player).</summary>
        public readonly Vector3 Direction;
        public readonly float Amount;
        public readonly float HealthBefore;
        public readonly float HealthAfter;
        public readonly float MaxHealth;
        public readonly bool Fatal;
        /// <summary>Applied ≥ <see cref="FeelEventThresholds.HeavyPlayerHitFraction"/> of max health.</summary>
        public readonly bool Heavy;
        public float Fraction => MaxHealth > 0f ? Amount / MaxHealth : 0f;
    }

    /// <summary>Low-health edge with hysteresis (enter ≤ 30 %, leave ≥ 38 %; death leaves it).</summary>
    public readonly struct FeelLowHealth
    {
        public FeelLowHealth(bool entered, float healthRatio, bool dead)
        {
            Entered = entered; HealthRatio = healthRatio; Dead = dead;
        }

        public readonly bool Entered;
        public readonly float HealthRatio;
        /// <summary>Left because the player died (no recovery beat).</summary>
        public readonly bool Dead;
    }

    /// <summary>A reload began or ended (classic: every exit — completed, cancelled, switched; Special: start and completion).</summary>
    public readonly struct FeelReload
    {
        public FeelReload(bool special, ClassicWeaponId classic, SpecialWeaponId specialId, float durationSeconds, bool empty,
            bool completed, Vector3 position)
        {
            Special = special; Classic = classic; SpecialId = specialId; DurationSeconds = durationSeconds; Empty = empty;
            Completed = completed; Position = position;
        }

        public readonly bool Special;
        public readonly ClassicWeaponId Classic;
        public readonly SpecialWeaponId SpecialId;
        public readonly float DurationSeconds;
        /// <summary>Started on an empty magazine (start only).</summary>
        public readonly bool Empty;
        /// <summary>End only: true = the magazine is in; false = cut short (trigger, switch, pickup).</summary>
        public readonly bool Completed;
        public readonly Vector3 Position;
    }

    /// <summary>The trigger was pulled on an empty classic magazine (press edge only).</summary>
    public readonly struct FeelDryFire
    {
        public FeelDryFire(ClassicWeaponId classic, Vector3 position)
        {
            Classic = classic; Position = position;
        }

        public readonly ClassicWeaponId Classic;
        public readonly Vector3 Position;
    }

    /// <summary>Gold landed in the wallet (GoldWallet.Credited: pickups, the round-end sweep, the direct fallback).</summary>
    public readonly struct FeelGold
    {
        public FeelGold(int amount, int balance, Vector3 position, int chainIndex)
        {
            Amount = amount; Balance = balance; Position = position; ChainIndex = chainIndex;
        }

        public readonly int Amount;
        public readonly int Balance;
        public readonly Vector3 Position;
        /// <summary>0 for the first credit, +1 for each further one inside <see cref="FeelEventThresholds.GoldChainWindowSeconds"/>.</summary>
        public readonly int ChainIndex;
    }

    public readonly struct FeelRound
    {
        public FeelRound(int round)
        {
            Round = round;
        }

        public readonly int Round;
    }

    /// <summary>The round's last zombie died: raised right after its <see cref="FeelEvents.EnemyKilled"/>, a frame before RoundEnded.</summary>
    public readonly struct FeelLastZombie
    {
        public FeelLastZombie(Vector3 position, int round, EnemyTier tier, Transform enemy)
        {
            Position = position; Round = round; Tier = tier; Enemy = enemy;
        }

        public readonly Vector3 Position;
        public readonly int Round;
        public readonly EnemyTier Tier;
        public readonly Transform Enemy;
    }

    /// <summary>The thresholds the derived events use (his sözleşmesi K/wp-095/HIS-SOZLESMESI.md; FeelProfile_TD sets them).</summary>
    [Serializable]
    public struct FeelEventThresholds
    {
        public float MultiKillChainWindowSeconds;
        public int MultiKillGroupMin;
        public int MultiKillChainMin;
        public int MultiKillTier2;
        public int MultiKillTier3;
        public float LowHealthEnter;
        public float LowHealthExit;
        public float HeavyPlayerHitFraction;
        public float GoldChainWindowSeconds;

        public static FeelEventThresholds Default => new FeelEventThresholds
        {
            MultiKillChainWindowSeconds = 0.6f, MultiKillGroupMin = 3, MultiKillChainMin = 4, MultiKillTier2 = 5, MultiKillTier3 = 8,
            LowHealthEnter = 0.30f, LowHealthExit = 0.38f, HeavyPlayerHitFraction = 0.25f, GoldChainWindowSeconds = 0.6f,
        };
    }

    /// <summary>
    /// WP-095 (R3, C-00 07.10) · the ONE game-feel event source. Every feel layer (camera, hit-stop, rumble — S-02; zombie
    /// reaction — S-03; VFX — S-07; audio — S-08; HUD — S-09; gold — S-04) subscribes here instead of picking one of the
    /// several parallel gameplay signals (a kill alone had six). Each moment is raised once, from its authoritative place:
    /// <list type="bullet">
    /// <item>shot / reload / dry fire: <c>WeaponController</c> (S-02), Special reloads from SpecialWeaponUsageEvents;</item>
    /// <item>hit / kill: DamageEvents (inside ZombieEnemy — every weapon, Specials and ground fire included);</item>
    /// <item>explosion: HitFeedbackDirector.ReportExplosion; player damage / low health: PlayerHealth's static mirrors;</item>
    /// <item>gold: the run's GoldWallet.Credited; rounds: RunEvents; derived here: multi-kill, low-health edges, last zombie.</item>
    /// </list>
    /// Payloads are readonly structs, nothing allocates per event; a throwing subscriber is logged, never reaches the producer.
    /// Statics reset at SubsystemRegistration; the bridge re-arms after every scene load (unsubscribe-then-subscribe).
    /// </summary>
    public static class FeelEvents
    {
        public static event Action<FeelShot> ShotFired;
        public static event Action<FeelHit> EnemyHit;
        public static event Action<FeelKill> EnemyKilled;
        public static event Action<FeelMultiKill> MultiKill;
        public static event Action<FeelExplosion> Explosion;
        public static event Action<FeelPlayerDamage> PlayerDamaged;
        public static event Action<FeelLowHealth> LowHealth;
        public static event Action<FeelReload> ReloadStarted;
        public static event Action<FeelReload> ReloadEnded;
        public static event Action<FeelDryFire> DryFire;
        public static event Action<FeelGold> GoldCollected;
        public static event Action<FeelRound> RoundStarted;
        public static event Action<FeelRound> RoundEnded;
        public static event Action<FeelLastZombie> LastZombie;

        public static FeelEventThresholds Thresholds = FeelEventThresholds.Default;

        private const int KindCount = 14;
        private static readonly int[] s_counts = new int[KindCount];

        /// <summary>Events raised since the last reset, per kind (tests / probes / telemetry).</summary>
        public static int Count(FeelEventKind kind) => s_counts[(int)kind];

        /// <summary>True while the player is in the low-health band (hysteresis state).</summary>
        public static bool InLowHealth { get; private set; }

        // ------------------------------------------------------------------ producers (S-02 call sites)

        public static void RaiseShot(in FeelShot shot) => Emit(ShotFired, shot, FeelEventKind.ShotFired);

        public static void RaiseReloadStarted(in FeelReload reload) => Emit(ReloadStarted, reload, FeelEventKind.ReloadStarted);

        public static void RaiseReloadEnded(in FeelReload reload) => Emit(ReloadEnded, reload, FeelEventKind.ReloadEnded);

        public static void RaiseDryFire(in FeelDryFire dry) => Emit(DryFire, dry, FeelEventKind.DryFire);

        public static void RaiseExplosion(Vector3 origin, float radius, int groupId, ExplosionSource source) =>
            Emit(Explosion, new FeelExplosion(origin, radius, groupId, source), FeelEventKind.Explosion);

        // ------------------------------------------------------------------ the shot group of the damage being applied

        private static int s_hitGroup;

        /// <summary>The presentation group of the shot / blast whose damage is being applied right now (0 = none).</summary>
        public static int CurrentHitGroup => s_hitGroup;

        /// <summary>
        /// Weapons wrap one damage application in Push / Pop so the hit and kill it raises carry their trigger pull's group:
        /// <c>int prev = FeelEvents.PushHitGroup(group); try { … } finally { FeelEvents.PopHitGroup(prev); }</c>.
        /// </summary>
        public static int PushHitGroup(int groupId)
        {
            int previous = s_hitGroup;
            s_hitGroup = groupId;
            return previous;
        }

        public static void PopHitGroup(int previous) => s_hitGroup = previous;

        // ------------------------------------------------------------------ the bridge (derived from the gameplay buses)

        private static readonly Action<EnemyDamageInfo> s_onEnemyDamaged = OnEnemyDamaged;
        private static readonly Action<EnemyDeathInfo> s_onEnemyDied = OnEnemyDied;
        private static readonly Action<PlayerDamageInfo> s_onPlayerDamaged = OnPlayerDamaged;
        private static readonly Action<PlayerHealthSnapshot> s_onHealthChanged = OnHealthChanged;
        private static readonly Action<int> s_onRunStarted = OnRunStarted;
        private static readonly Action<int> s_onRoundStarted = OnRoundStarted;
        private static readonly Action<int> s_onRoundCompleted = OnRoundCompleted;
        private static readonly Action<SpecialUsageEvent> s_onSpecialUsage = OnSpecialUsage;
        private static readonly Action<int, int, Vector3> s_onCredited = OnCredited;

        private static bool s_installed;
        private static GoldWallet s_wallet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            Rearm();
            if (s_installed) return;
            s_installed = true;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private static void OnActiveSceneChanged(Scene from, Scene to) => Rearm();

        /// <summary>Subscribes the bridge to its sources exactly once (unsubscribe-then-subscribe); tests call it directly.</summary>
        internal static void Rearm()
        {
            DamageEvents.EnemyDamaged -= s_onEnemyDamaged; DamageEvents.EnemyDamaged += s_onEnemyDamaged;
            DamageEvents.EnemyDied -= s_onEnemyDied; DamageEvents.EnemyDied += s_onEnemyDied;
            PlayerHealth.AnyDamaged -= s_onPlayerDamaged; PlayerHealth.AnyDamaged += s_onPlayerDamaged;
            PlayerHealth.AnyHealthChanged -= s_onHealthChanged; PlayerHealth.AnyHealthChanged += s_onHealthChanged;
            RunEvents.RunStarted -= s_onRunStarted; RunEvents.RunStarted += s_onRunStarted;
            RunEvents.RoundStarted -= s_onRoundStarted; RunEvents.RoundStarted += s_onRoundStarted;
            RunEvents.RoundCompleted -= s_onRoundCompleted; RunEvents.RoundCompleted += s_onRoundCompleted;
            SpecialWeaponUsageEvents.Usage -= s_onSpecialUsage; SpecialWeaponUsageEvents.Usage += s_onSpecialUsage;
            BindWallet();
        }

        /// <summary>Takes the bridge off every source (tests' tear-down; the game never disarms).</summary>
        internal static void Disarm()
        {
            DamageEvents.EnemyDamaged -= s_onEnemyDamaged;
            DamageEvents.EnemyDied -= s_onEnemyDied;
            PlayerHealth.AnyDamaged -= s_onPlayerDamaged;
            PlayerHealth.AnyHealthChanged -= s_onHealthChanged;
            RunEvents.RunStarted -= s_onRunStarted;
            RunEvents.RoundStarted -= s_onRoundStarted;
            RunEvents.RoundCompleted -= s_onRoundCompleted;
            SpecialWeaponUsageEvents.Usage -= s_onSpecialUsage;
            if (s_wallet != null) s_wallet.Credited -= s_onCredited;
            s_wallet = null;
        }

        /// <summary>Follows the run's wallet (a new run may bring a new GoldService); a reference compare when unchanged.</summary>
        private static void BindWallet()
        {
            GoldService service = GoldService.Instance;
            GoldWallet wallet = service != null ? service.Wallet : null;
            if (ReferenceEquals(wallet, s_wallet)) return;
            if (s_wallet != null) s_wallet.Credited -= s_onCredited;
            s_wallet = wallet;
            if (s_wallet != null) { s_wallet.Credited -= s_onCredited; s_wallet.Credited += s_onCredited; }
        }

        internal static void OnEnemyDamaged(EnemyDamageInfo info)
        {
            DamageResult r = info.Result;
            Emit(EnemyHit, new FeelHit(info.Owner, info.Tier, info.Meta.LifeId, info.Point, info.Direction, r.HealthDamage,
                r.ArmorDamage, r.ArmorBroke, r.Killed, DamageRegions.IsCritical(in r, info.Source), r.Region, info.Source,
                info.ImpactForce, info.HealthRatio, info.CausedStagger, DamageHitContext.GroundFire, s_hitGroup,
                DamageHitContext.FromClassicWeapon, DamageHitContext.Weapon, DamageHitContext.FromSpecialWeapon,
                DamageHitContext.Special), FeelEventKind.EnemyHit);
        }

        internal static void OnEnemyDied(EnemyDeathInfo info)
        {
            BindWallet();
            float now = Now();
            int group = s_hitGroup;
            int groupKills = CountGroupKill(group, now);
            int chainKills = CountChainKill(now);
            bool last = IsLastOfRound();
            Emit(EnemyKilled, new FeelKill(info.Owner, info.Tier, info.LifeId, info.Position, info.KillDirection, info.KillRegion,
                info.KillSource, info.WasCritical, info.WasHeavyDeath, info.Overkill, info.GoldReward, info.SpawnRound, group,
                DamageHitContext.FromClassicWeapon, DamageHitContext.Weapon, DamageHitContext.FromSpecialWeapon,
                DamageHitContext.Special, groupKills, chainKills, last), FeelEventKind.EnemyKilled);
            EvaluateMultiKill(group, groupKills, chainKills, info.Position, now);
            if (last) Emit(LastZombie, new FeelLastZombie(info.Position, info.SpawnRound, info.Tier, info.Owner), FeelEventKind.LastZombie);
        }

        internal static void OnPlayerDamaged(PlayerDamageInfo info)
        {
            Vector3 position = info.Player != null ? info.Player.transform.position : info.WorldPoint;
            bool heavy = info.MaxHealth > 0f && info.AppliedAmount >= Thresholds.HeavyPlayerHitFraction * info.MaxHealth;
            Emit(PlayerDamaged, new FeelPlayerDamage(position, info.HitDirection, info.AppliedAmount, info.HealthBefore,
                info.HealthAfter, info.MaxHealth, info.WasFatal, heavy), FeelEventKind.PlayerDamaged);
        }

        private static void OnHealthChanged(PlayerHealthSnapshot s) => UpdateLowHealth(s.Current, s.Max, s.IsDead);

        /// <summary>The hysteresis edge (enter ≤ LowHealthEnter, leave ≥ LowHealthExit; death leaves it).</summary>
        internal static void UpdateLowHealth(float current, float max, bool dead)
        {
            float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (dead || current <= 0f)
            {
                if (!InLowHealth) return;
                InLowHealth = false;
                Emit(LowHealth, new FeelLowHealth(false, ratio, true), FeelEventKind.LowHealth);
                return;
            }

            if (!InLowHealth && ratio <= Thresholds.LowHealthEnter)
            {
                InLowHealth = true;
                Emit(LowHealth, new FeelLowHealth(true, ratio, false), FeelEventKind.LowHealth);
            }
            else if (InLowHealth && ratio >= Thresholds.LowHealthExit)
            {
                InLowHealth = false;
                Emit(LowHealth, new FeelLowHealth(false, ratio, false), FeelEventKind.LowHealth);
            }
        }

        private static void OnRunStarted(int startRound)
        {
            InLowHealth = false;
            ClearKillWindows();
            BindWallet();
        }

        private static void OnRoundStarted(int round)
        {
            BindWallet();
            Emit(RoundStarted, new FeelRound(round), FeelEventKind.RoundStarted);
        }

        private static void OnRoundCompleted(int round) => Emit(RoundEnded, new FeelRound(round), FeelEventKind.RoundEnded);

        internal static void OnSpecialUsage(SpecialUsageEvent e)
        {
            if (e.Kind == SpecialUsageKind.ReloadStarted)
                Emit(ReloadStarted, new FeelReload(true, default, e.Id, e.Seconds, e.Count <= 0, false, e.Origin), FeelEventKind.ReloadStarted);
            else if (e.Kind == SpecialUsageKind.ReloadEnded)
                Emit(ReloadEnded, new FeelReload(true, default, e.Id, e.Seconds, false, true, e.Origin), FeelEventKind.ReloadEnded);
        }

        private static float s_lastGoldTime = float.NegativeInfinity;
        private static int s_goldChain;

        internal static void OnCredited(int amount, int balance, Vector3 position)
        {
            float now = Now();
            s_goldChain = now - s_lastGoldTime <= Thresholds.GoldChainWindowSeconds ? s_goldChain + 1 : 0;
            s_lastGoldTime = now;
            Emit(GoldCollected, new FeelGold(amount, balance, position, s_goldChain), FeelEventKind.GoldCollected);
        }

        // ------------------------------------------------------------------ multi-kill (his sözleşmesi §4.4)

        private const int GroupSlots = 16;
        private static readonly int[] s_groupId = new int[GroupSlots];
        private static readonly int[] s_groupKills = new int[GroupSlots];
        private static readonly float[] s_groupTime = new float[GroupSlots];
        private static int s_groupNext;

        private const int ChainSlots = 32;
        private static readonly float[] s_chainTime = new float[ChainSlots];
        private static int s_chainNext;

        private static int s_streakTier;
        private static float s_streakTime = float.NegativeInfinity;

        /// <summary>Kills of <paramref name="group"/> so far, this one included; a group idle for the chain window starts over.</summary>
        private static int CountGroupKill(int group, float now)
        {
            if (group == 0) return 1;
            float window = Thresholds.MultiKillChainWindowSeconds;
            for (int i = 0; i < GroupSlots; i++)
            {
                if (s_groupId[i] != group) continue;
                s_groupKills[i] = now - s_groupTime[i] <= window ? s_groupKills[i] + 1 : 1;
                s_groupTime[i] = now;
                return s_groupKills[i];
            }

            s_groupId[s_groupNext] = group; s_groupKills[s_groupNext] = 1; s_groupTime[s_groupNext] = now;
            s_groupNext = (s_groupNext + 1) % GroupSlots;
            return 1;
        }

        /// <summary>Kills inside the sliding window ending now, this one included.</summary>
        private static int CountChainKill(float now)
        {
            s_chainTime[s_chainNext] = now;
            s_chainNext = (s_chainNext + 1) % ChainSlots;
            float window = Thresholds.MultiKillChainWindowSeconds;
            int n = 0;
            for (int i = 0; i < ChainSlots; i++) if (now - s_chainTime[i] <= window) n++;
            return n;
        }

        /// <summary>Tier of a count: 0 below the minimum, then T1 / T2 (≥ MultiKillTier2) / T3 (≥ MultiKillTier3).</summary>
        internal static int TierOf(int count, int minimum)
        {
            if (count < minimum) return 0;
            if (count >= Thresholds.MultiKillTier3) return 3;
            return count >= Thresholds.MultiKillTier2 ? 2 : 1;
        }

        private static void EvaluateMultiKill(int group, int groupKills, int chainKills, Vector3 position, float now)
        {
            if (now - s_streakTime > Thresholds.MultiKillChainWindowSeconds) s_streakTier = 0;
            s_streakTime = now;
            int groupTier = group != 0 ? TierOf(groupKills, Thresholds.MultiKillGroupMin) : 0;
            int chainTier = TierOf(chainKills, Thresholds.MultiKillChainMin);
            int tier = Mathf.Max(groupTier, chainTier);
            if (tier <= s_streakTier) return;
            s_streakTier = tier;
            bool sameGroup = groupTier >= chainTier && groupTier > 0;
            Emit(MultiKill, new FeelMultiKill(tier, sameGroup ? groupKills : chainKills, sameGroup, sameGroup ? group : 0, position),
                FeelEventKind.MultiKill);
        }

        private static void ClearKillWindows()
        {
            for (int i = 0; i < GroupSlots; i++) { s_groupId[i] = 0; s_groupKills[i] = 0; s_groupTime[i] = 0f; }
            for (int i = 0; i < ChainSlots; i++) s_chainTime[i] = float.NegativeInfinity;
            s_groupNext = 0; s_chainNext = 0; s_streakTier = 0; s_streakTime = float.NegativeInfinity;
        }

        // ------------------------------------------------------------------ shared

        /// <summary>Test seam: ≥ 0 replaces the unscaled clock the windows use (EditMode time does not advance inside a test).</summary>
        internal static float TestClock = -1f;

        /// <summary>Test seam: -1 asks the spawn director, 0 / 1 force the answer.</summary>
        internal static int TestLastOfRound = -1;

        private static float Now() => TestClock >= 0f ? TestClock : Time.unscaledTime;

        /// <summary>The ledger already counted this death (ZombieEnemy.Die notifies it before EnemyDied): pool empty and nobody alive.</summary>
        private static bool IsLastOfRound()
        {
            if (TestLastOfRound >= 0) return TestLastOfRound == 1;
            EnemySpawnDirector spawn = EnemySpawnDirector.Instance;
            return spawn != null && spawn.RemainingInPool == 0 && spawn.ActiveCount == 0;
        }

        private static void Emit<T>(Action<T> handler, in T payload, FeelEventKind kind)
        {
            s_counts[(int)kind]++;
            if (handler == null) return;
            try
            {
                handler.Invoke(payload);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ShotFired = null; EnemyHit = null; EnemyKilled = null; MultiKill = null; Explosion = null; PlayerDamaged = null;
            LowHealth = null; ReloadStarted = null; ReloadEnded = null; DryFire = null; GoldCollected = null;
            RoundStarted = null; RoundEnded = null; LastZombie = null;
            for (int i = 0; i < KindCount; i++) s_counts[i] = 0;
            Thresholds = FeelEventThresholds.Default;
            InLowHealth = false;
            s_hitGroup = 0;
            s_wallet = null;
            s_lastGoldTime = float.NegativeInfinity; s_goldChain = 0;
            TestClock = -1f; TestLastOfRound = -1;
            ClearKillWindows();
            // s_installed stays: the scene-change hook is a static subscription that survives; Install re-arms the sources.
        }

#if UNITY_EDITOR
        /// <summary>Tests: subscribers, counters and the derived state cleared, the bridge re-armed.</summary>
        public static void ResetForTests()
        {
            ResetStatics();
            Rearm();
        }
#endif
    }
}
