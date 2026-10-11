using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B3 · production binding of the tree systems to S-03's body (C-00 12.10: the adapter calls S-03's B3 surface
    /// one-to-one; nothing new on <c>ZombieEnemy</c> beyond S-03's own plan). LANDS WITH S-03's <c>Enemies/ZombieTreeStatus.cs</c>
    /// (it calls ApplyIce / ApplyStun / ApplyPull / ApplyStatusDamage / SetStatusTint / IsFrozen / FrozenAtDeath /
    /// TreeStatusEnded); every other B3 file compiles without it.
    ///
    /// <list type="bullet">
    /// <item>A pool of <see cref="TreeGuardrails.StatusSlots"/> adapters, created once; a living zombie is wrapped on first sight.</item>
    /// <item>Registration: <see cref="Sync"/> runs first in every tick (<see cref="TreeSystemsRunner.BeforeTick"/>) and wraps
    /// living zombies of <c>ZombieEnemy.Live</c> it does not know yet (≤ 48 dictionary probes, no allocation).</item>
    /// <item>Release: <c>ZombieEnemy.TreeStatusEnded</c> (S-03 raises it in Die and on despawn). While the body
    /// <c>IsDying</c> it is a death (<see cref="TreeSystemsRunner.OnBodyDied"/>: chain, Shatter, ground, cloud), otherwise a
    /// plain release.</item>
    /// <item>Weapon credit: the adapter sets DamageHitContext (classic weapon = the weapon in hand) around the body's damage
    /// calls, the RunTreeGroundFire pattern; DoT / ground / hole / bolt also MarkGroundFire (no stagger force).</item>
    /// </list>
    /// S-02 calls <see cref="OnWeaponHit"/> / <see cref="OnChainShock"/> with the zombie it hit; S-03 reads
    /// <see cref="StatusDamageScale"/> and <see cref="IgnoresArmor"/> in its damage paths.
    /// </summary>
    public static class TreeZombieBinding
    {
        private const int Cap = TreeGuardrails.StatusSlots;

        /// <summary>One pooled adapter: a 1:1 forward of S-03's B3 surface.</summary>
        private sealed class ZombieTreeReceiver : ITreeStatusReceiver
        {
            public ZombieEnemy Body;

            public int TreeSlot { get; set; } = TreeStatusSystem.NoSlot;
            public EnemyTier Tier => Body.Tier;
            public bool IsAlive => Body != null && Body.IsAlive;
            public Vector3 Position => Body.transform.position;
            public float MaxHealth => Body.MaxHealth;
            public bool IsFrozen => Body != null && Body.IsFrozen;
            public bool FrozenAtDeath => Body != null && Body.FrozenAtDeath;
            public int IceStacks => Body != null ? Body.IceStacks : 0;

            public DamageResult ApplyExplosion(float totalDamage, Vector3 origin)
            {
                DamageHitContext.Set(1f, 1f, true, TreeDamageContext.Weapon, null);
                try { return Body.ApplyExplosion(totalDamage, origin); }
                finally { DamageHitContext.Clear(); }
            }

            public DamageResult ApplyStatusDamage(float amount, TreeElement element)
            {
                // S-03 keeps the caller's weapon attribution (FromClassicWeapon / Weapon) and marks the ground-fire force itself
                DamageHitContext.Set(1f, 1f, true, TreeDamageContext.Weapon, null);
                try { return Body.ApplyStatusDamage(amount, element); }
                finally { DamageHitContext.Clear(); }
            }

            public bool ApplyTreeStagger(float seconds, Vector3 direction) => Body.ApplyTreeStagger(seconds, direction);
            public IceResult ApplyIce(int stacks, bool ignoreCooldown = false) => ignoreCooldown ? Body.ApplyIce(stacks, true) : Body.ApplyIce(stacks);
            public bool ApplyStun(float seconds, TreeStunKind kind) => Body.ApplyStun(seconds, kind);
            public void ApplyPull(Vector3 center, float speed, float hold) => Body.ApplyPull(center, speed, hold);
            public void SetStatusTint(TreeElement element, float strength) => Body.SetStatusTint(element, strength);
        }

        private static readonly ZombieTreeReceiver[] s_adapters = new ZombieTreeReceiver[Cap];
        private static readonly Dictionary<ZombieEnemy, ZombieTreeReceiver> s_map = new Dictionary<ZombieEnemy, ZombieTreeReceiver>(Cap * 2);

        static TreeZombieBinding()
        {
            for (int i = 0; i < Cap; i++) s_adapters[i] = new ZombieTreeReceiver();
        }

        public static int Bound => s_map.Count;

        // ------------------------------------------------------------------ S-02 / S-03 entry points

        /// <summary>S-02: one body hit (after the hit landed). See <see cref="TreeSystemsRunner.OnWeaponHit"/>.</summary>
        public static TreeElement OnWeaponHit(ZombieEnemy body, Vector3 point, in TreeHitFacts facts)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (!TreeSystemsRunner.Needed(in s)) return TreeElement.None;   // no tree build: Normal untouched
            return TreeSystemsRunner.OnWeaponHit(Find(body), point, in facts);
        }

        /// <summary>S-02: the chain lightning reached this body (Thunder Lord stagger).</summary>
        public static bool OnChainShock(ZombieEnemy body, Vector3 from)
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (!TreeSystemsRunner.Needed(in s)) return false;   // no tree build: nothing binds
            return TreeSystemsRunner.OnChainShock(Find(body), from);
        }

        /// <summary>S-03: the fire / venom vulnerability factor (× its own FreezeVulnerability) for every incoming damage.</summary>
        public static float StatusDamageScale(ZombieEnemy body)
        {
            // Read-only (S-03 12.10): a body the tree never touched has no status → ×1; reading never binds a slot or spawns the driver.
            ZombieTreeReceiver a = Peek(body);
            return a != null ? TreeStatusSystem.DamageTakenScale(a) : 1f;
        }

        /// <summary>S-03: Corrosion — weapon hits on this body pass regional armour (MarkIgnoreArmor).</summary>
        public static bool IgnoresArmor(ZombieEnemy body)
        {
            ZombieTreeReceiver a = Peek(body);
            return a != null && TreeStatusSystem.IsCorroded(a);
        }

        // ------------------------------------------------------------------ binding

        /// <summary>Wraps every living zombie not bound yet. First thing in each tick.</summary>
        public static void Sync()
        {
            // a body destroyed without TreeStatusEnded (Unity-null): give its adapter back
            for (int i = 0; i < Cap; i++)
            {
                ZombieTreeReceiver a = s_adapters[i];
                if (ReferenceEquals(a.Body, null) || a.Body != null) continue;
                s_map.Remove(a.Body);
                TreeStatusSystem.Release(a);
                a.Body = null;
                a.TreeSlot = TreeStatusSystem.NoSlot;
            }

            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                ZombieEnemy z = live[i];
                if (z == null || !z.IsAlive || s_map.ContainsKey(z)) continue;
                Bind(z);
            }
        }

        /// <summary>The adapter of an already bound body, else null. Never binds (damage-path reads).</summary>
        private static ZombieTreeReceiver Peek(ZombieEnemy body)
        {
            return body != null && s_map.TryGetValue(body, out ZombieTreeReceiver a) ? a : null;
        }

        /// <summary>Bound adapter, binding a living body on first tree contact (write paths only: hits, chains, systems).</summary>
        private static ZombieTreeReceiver Find(ZombieEnemy body)
        {
            if (body == null) return null;
            if (s_map.TryGetValue(body, out ZombieTreeReceiver a)) return a;
            return body.IsAlive ? Bind(body) : null;
        }

        private static ZombieTreeReceiver Bind(ZombieEnemy z)
        {
            for (int i = 0; i < Cap; i++)
            {
                ZombieTreeReceiver a = s_adapters[i];
                if (!ReferenceEquals(a.Body, null)) continue;
                a.Body = z;
                a.TreeSlot = TreeStatusSystem.NoSlot;
                if (!TreeStatusSystem.Register(a))
                {
                    a.Body = null;
                    return null;
                }

                s_map[z] = a;
                return a;
            }

            return null;
        }

        /// <summary>S-03's TreeStatusEnded (Die and despawn).</summary>
        private static void OnStatusEnded(ZombieEnemy z)
        {
            if (ReferenceEquals(z, null) || !s_map.TryGetValue(z, out ZombieTreeReceiver a)) return;
            s_map.Remove(z);
            if (z.IsDying) TreeSystemsRunner.OnBodyDied(a);
            else TreeStatusSystem.Release(a);
            a.Body = null;
            a.TreeSlot = TreeStatusSystem.NoSlot;
        }

        private static void Forget()
        {
            s_map.Clear();
            for (int i = 0; i < Cap; i++)
            {
                s_adapters[i].Body = null;
                s_adapters[i].TreeSlot = TreeStatusSystem.NoSlot;
            }
        }

        /// <summary>A build with a B3 system (elements, cores, chain, explosive rounds, nova, bell, ring, Barrage) starts the driver.</summary>
        private static void OnStatsChanged()
        {
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (TreeSystemsRunner.Needed(in s)) TreeSystemsDriver.EnsureExists();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            ZombieEnemy.TreeStatusEnded = OnStatusEnded;   // the SlipOutcomeHandler pattern: one owner, assigned
            TreeSystemsRunner.BeforeTick = Sync;
            TreeSystemsRunner.AfterReset = Forget;
            TreeEvents.StatsChanged -= OnStatsChanged;   // remove-then-add: never two copies (static bus rearm lesson)
            TreeEvents.StatsChanged += OnStatsChanged;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Forget();
    }
}
