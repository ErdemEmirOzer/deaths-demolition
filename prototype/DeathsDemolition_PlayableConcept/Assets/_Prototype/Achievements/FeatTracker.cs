using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Achievements
{
    /// <summary>
    /// WP-077 A10 · the weapon / Special feats of the R1 content pack §6, read from the existing gameplay buses and
    /// handed to <see cref="AchievementRunTracker"/> (which ignores them outside a run). The weapon behind a kill or a
    /// plate break is <see cref="DamageHitContext"/> — valid only inside the synchronous handler. Grouping (per Raiju
    /// shot, per Tomi round, per Bohiya transaction, per body for Nito) uses small fixed rings: no allocation.
    /// </summary>
    public static class FeatTracker
    {
        private const int Ring = 8;
        private const float NitoWindowSeconds = 1f;   // "in one breath": both barrels within a second

        private static readonly int[] s_key = new int[Ring];
        private static readonly int[] s_count = new int[Ring];
        private static int s_next;

        private static readonly int[] s_nitoLife = new int[Ring];
        private static readonly int[] s_nitoFirstShell = new int[Ring];
        private static readonly int[] s_nitoLastShell = new int[Ring];
        private static readonly float[] s_nitoTime = new float[Ring];
        private static int s_nitoNext;
        private static int s_sg2Shell;

        private static bool s_raijuActive;
        private static bool s_raijuShocked;
        private static int s_lmg2Plates;

        // ------------------------------------------------------------------ rings

        /// <summary>Adds one to the group <paramref name="key"/> (group ids reset per bucket kind via the offset) and returns its count.</summary>
        private static int Bump(int key)
        {
            for (int i = 0; i < Ring; i++)
            {
                if (s_key[i] == key && s_count[i] > 0) return ++s_count[i];
            }

            int slot = s_next;
            s_next = (s_next + 1) % Ring;
            s_key[slot] = key;
            s_count[slot] = 1;
            return 1;
        }

        // Keys of different feats never collide: each feat folds its id into its own range.
        private static int RaijuKey(int transactionId) => (transactionId << 2) | 1;
        private static int TomiKey(int projectileId) => (projectileId << 2) | 2;
        private static int BohiyaKey(int transactionId) => (transactionId << 2) | 3;

        // ------------------------------------------------------------------ handlers

        private static void OnUsage(SpecialUsageEvent e)
        {
            if (e.Id != SpecialWeaponId.Raiju) return;
            switch (e.Kind)
            {
                case SpecialUsageKind.Activated:
                    s_raijuActive = true;
                    s_raijuShocked = false;
                    break;
                case SpecialUsageKind.SelfShock:
                    s_raijuShocked = true;
                    break;
                case SpecialUsageKind.Hit:
                    AchievementRunTracker.RecordFeatMax(FeatKind.RaijuChainMax, Bump(RaijuKey(e.TransactionId)));
                    break;
                case SpecialUsageKind.Deactivated:
                    // Every activation starts full; "emptied" = the load ran out (not a cancel), no self-shock since.
                    WeaponController c = WeaponRegistry.ActiveController;
                    bool emptied = c != null && c.Special != null && c.Special.OutOfAmmo;
                    if (s_raijuActive && emptied && !s_raijuShocked) AchievementRunTracker.AddFeat(FeatKind.RaijuGrounded);
                    s_raijuActive = false;
                    break;
            }
        }

        private static void OnEnemyDied(EnemyDeathInfo info)
        {
            if (DamageHitContext.FromSpecialWeapon && DamageHitContext.Special == SpecialWeaponId.Raiju)
            {
                AchievementRunTracker.AddFeat(FeatKind.RaijuKills);
            }

            ZombieEnemy body = info.Owner != null ? info.Owner.GetComponent<ZombieEnemy>() : null;
            if (body != null && (body.Gel == GelState.Sliding || body.Gel == GelState.Falling))
            {
                AchievementRunTracker.AddFeat(FeatKind.GelSlideKills);   // a slip-fall kill dies "Falling"
            }

            if (!DamageHitContext.FromClassicWeapon) return;
            switch (DamageHitContext.Weapon)
            {
                case ClassicWeaponId.Rl1:
                    AchievementRunTracker.RecordFeatMax(FeatKind.BohiyaKillsPerRocketMax, Bump(BohiyaKey(info.TransactionId)));
                    break;
                case ClassicWeaponId.Smg2:
                    if (info.Tier == EnemyTier.Special) AchievementRunTracker.AddFeat(FeatKind.TetsuiRunnerKills);   // the Runner
                    break;
                case ClassicWeaponId.Sg2:
                    if (info.Tier == EnemyTier.Elite && body != null && NitoBothBarrels(body.LifeId)) AchievementRunTracker.AddFeat(FeatKind.NitoDoubleGuard);
                    break;
            }
        }

        private static void OnEnemyDamaged(EnemyDamageInfo info)
        {
            if (!DamageHitContext.FromClassicWeapon || DamageHitContext.Weapon != ClassicWeaponId.Sg2 || info.Owner == null) return;
            ZombieEnemy body = info.Owner.GetComponent<ZombieEnemy>();
            if (body == null) return;
            int life = body.LifeId;
            for (int i = 0; i < Ring; i++)
            {
                if (s_nitoLife[i] == life && s_nitoTime[i] > 0f)
                {
                    s_nitoLastShell[i] = s_sg2Shell;
                    return;
                }
            }

            int slot = s_nitoNext;
            s_nitoNext = (s_nitoNext + 1) % Ring;
            s_nitoLife[slot] = life;
            s_nitoFirstShell[slot] = s_sg2Shell;
            s_nitoLastShell[slot] = s_sg2Shell;
            s_nitoTime[slot] = Time.time;
        }

        private static bool NitoBothBarrels(int life)
        {
            for (int i = 0; i < Ring; i++)
            {
                if (s_nitoLife[i] != life || s_nitoTime[i] <= 0f) continue;
                bool both = s_nitoLastShell[i] != s_nitoFirstShell[i] && Time.time - s_nitoTime[i] <= NitoWindowSeconds;
                s_nitoTime[i] = 0f;
                return both;
            }

            return false;
        }

        private static void OnProjectileSpawned(ProjectileSpawnInfo info)
        {
            if (info.WeaponId == ClassicWeaponId.Sg2 && info.PelletIndex == 0 && info.VolleyIndex == 0) s_sg2Shell++;   // one shell = one barrel (WP-104 B2: a tree volley is not a barrel)
        }

        private static void OnProjectileImpact(ProjectileImpactInfo info)
        {
            if (info.WeaponId == ClassicWeaponId.Sr1 && info.Killed)
            {
                AchievementRunTracker.RecordFeatMax(FeatKind.TomiKillsPerShotMax, Bump(TomiKey(info.ProjectileId)));
            }
        }

        private static void OnArmorBroken(ArmorBreakInfo info)
        {
            if (!DamageHitContext.FromClassicWeapon || DamageHitContext.Weapon != ClassicWeaponId.Lmg2) return;
            s_lmg2Plates++;
            AchievementRunTracker.RecordFeatMax(FeatKind.KuroganePlatesPerMagMax, s_lmg2Plates);
        }

        private static void OnMagazineBoundary() => s_lmg2Plates = 0;

        private static void OnWeaponEquipped(ClassicWeaponId weapon) => s_lmg2Plates = 0;

        private static void OnPuddleCreated(GelPuddleInfo info)
        {
            AchievementRunTracker.RecordFeatMax(FeatKind.PuddlesMax, GelPuddleRegistry.Count);
        }

        private static void OnCombatEvent(EnemyCombatEventInfo info)
        {
            if (info.Kind == EnemyCombatEventKind.SpecialFall && info.Tier == EnemyTier.Ultra) AchievementRunTracker.AddFeat(FeatKind.GelKnockdownUltra);
        }

        // ------------------------------------------------------------------ install

        /// <summary>Idempotent (remove-then-add): the usage bus has no domain reset, a test may clear any bus.</summary>
        public static void EnsureHooked()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            SpecialWeaponUsageEvents.Usage += OnUsage;
            DamageEvents.EnemyDied -= OnEnemyDied;
            DamageEvents.EnemyDied += OnEnemyDied;
            DamageEvents.EnemyDamaged -= OnEnemyDamaged;
            DamageEvents.EnemyDamaged += OnEnemyDamaged;
            DamageEvents.ArmorBroken -= OnArmorBroken;
            DamageEvents.ArmorBroken += OnArmorBroken;
            ProjectileEvents.Spawned -= OnProjectileSpawned;
            ProjectileEvents.Spawned += OnProjectileSpawned;
            ProjectileEvents.Impact -= OnProjectileImpact;
            ProjectileEvents.Impact += OnProjectileImpact;
            WeaponEvents.ReloadCompleted -= OnMagazineBoundary;
            WeaponEvents.ReloadCompleted += OnMagazineBoundary;
            WeaponEvents.WeaponEquipped -= OnWeaponEquipped;
            WeaponEvents.WeaponEquipped += OnWeaponEquipped;
            GelPuddleRegistry.PuddleCreated -= OnPuddleCreated;
            GelPuddleRegistry.PuddleCreated += OnPuddleCreated;
            EnemyCombatEvents.Raised -= OnCombatEvent;
            EnemyCombatEvents.Raised += OnCombatEvent;
        }

        /// <summary>Clears the in-flight groups (run start, scene change, tests).</summary>
        public static void ResetState()
        {
            for (int i = 0; i < Ring; i++)
            {
                s_key[i] = 0; s_count[i] = 0;
                s_nitoLife[i] = 0; s_nitoTime[i] = 0f; s_nitoFirstShell[i] = 0; s_nitoLastShell[i] = 0;
            }

            s_next = s_nitoNext = 0;
            s_sg2Shell = 0;
            s_raijuActive = s_raijuShocked = false;
            s_lmg2Plates = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ResetState();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            EnsureHooked();
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private static void OnActiveSceneChanged(Scene from, Scene to)
        {
            ResetState();
            EnsureHooked();
        }

        /// <summary>Tests: drive the handlers without a scene.</summary>
        public static class TestSeam
        {
            public static void Usage(SpecialUsageEvent e) => OnUsage(e);
            public static void Impact(ProjectileImpactInfo info) => OnProjectileImpact(info);
            public static void CombatEvent(EnemyCombatEventInfo info) => OnCombatEvent(info);
        }
    }
}
