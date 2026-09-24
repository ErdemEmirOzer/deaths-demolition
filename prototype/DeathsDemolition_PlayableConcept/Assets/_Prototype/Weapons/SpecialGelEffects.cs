using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-069 B4 · Namekuji's puddles on S-03's <see cref="GelPuddleRegistry"/> (DIRECTION §7b, agreed with S-03 24.09).
    ///
    /// A blob's contact makes a puddle on the floor below it (a wall or a body: the floor under it) — r 1.6 m, 25 s. The
    /// registry's cap, fade and slip fall come from the sw_gel data (<see cref="EnsureConfigured"/>), and so do the player's
    /// slide numbers on <see cref="PlayerMotor"/>. A body that slips (S-03's slide → fall) lands in
    /// <see cref="ApplySlipOutcome"/>: the slip tier rule — Standard / Runner / Guard die every round and leave a small
    /// puddle (r 0.9 m, 8 s: the chain), the Executioner loses 20 % and is immune for 3 s once it is back up. The slip
    /// damage is its own transaction attributed to the gel (armour ignored). Usage events: FieldSpawned per puddle
    /// (Count = puddle id, Radius, Seconds, Direction = the surface normal), FieldEnded when it expires.
    /// </summary>
    public static class SpecialGelEffects
    {
        /// <summary>How far below a contact the floor is searched for (a blob that hits a wall runs down).</summary>
        [PrototypeOnly(PendingDecisions.None, "WP-069 B4: floor probe below a gel contact (m).", true)]
        public const float FloorProbeMetres = 4f;

        private struct Tracked
        {
            public int Id;
            public Vector3 Center;
            public float Radius;
        }

        private static readonly Tracked[] s_tracked = new Tracked[GelPuddleRegistry.Capacity];
        private static WeaponDefinition s_configured;
        private static bool s_hooked;

        /// <summary>Tests / probes.</summary>
        public static int PuddlesMade { get; private set; }
        public static int DeathPuddles { get; private set; }
        public static int SlipKills { get; private set; }
        public static int SlipShares { get; private set; }

        /// <summary>Reads the sw_gel data into the registry, the player's slide and the slip hook. Cheap when nothing changed.</summary>
        public static void EnsureConfigured()
        {
            WeaponDefinition def = SpecialWeaponConfigSection.DefinitionOf(SpecialWeaponId.Namekuji);
            if (!s_hooked)
            {
                s_hooked = true;
                GelPuddleRegistry.PuddleCreated += OnPuddleCreated;
                GelPuddleRegistry.PuddleExpired += OnPuddleExpired;
            }

            ZombieEnemy.SlipOutcomeHandler = ApplySlipOutcome;
            if (def == null || ReferenceEquals(def, s_configured)) return;
            s_configured = def;
            WeaponGelPuddleBlock gel = def.gelPuddle;
            GelPuddleRegistry.Configure(gel.maxPuddles > 0 ? gel.maxPuddles : 12, gel.fadeSeconds, gel.slipFallSeconds > 0f ? gel.slipFallSeconds : 0.9f);
            PlayerMotor.GelSpeedMultiplier = Mathf.Max(1f, gel.playerSlideSpeedMultiplier);
            PlayerMotor.GelSteerMultiplier = Mathf.Clamp(gel.playerSteerMultiplier, 0.05f, 1f);
            PlayerMotor.GelExitSeconds = Mathf.Max(0f, gel.slideExitSeconds);
        }

        /// <summary>A puddle on the floor below <paramref name="at"/>; 0 when the size or time is zero.</summary>
        public static int MakePuddle(int transaction, Vector3 at, Vector3 normal, float radius, float seconds)
        {
            if (radius <= 0f || seconds <= 0f) return 0;
            EnsureConfigured();
            Vector3 center = at;
            if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, FloorProbeMetres, 1 << PrototypeLayers.Environment, QueryTriggerInteraction.Ignore))
            {
                center = floor.point;
            }

            int id = GelPuddleRegistry.Register(center, radius, center.y, seconds, transaction);
            PuddlesMade++;
            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.Namekuji, SpecialUsageKind.FieldSpawned, SpecialWeaponUsage.For(SpecialWeaponId.Namekuji).FieldVfxFamily,
                center, normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up, radius, seconds, id);
            return id;
        }

        /// <summary>S-03's <see cref="ZombieEnemy.SlipOutcomeHandler"/>: the fall has landed.</summary>
        public static void ApplySlipOutcome(ZombieEnemy body, int puddleId)
        {
            if (body == null || !body.IsAlive) return;
            WeaponDefinition def = s_configured ?? SpecialWeaponConfigSection.DefinitionOf(SpecialWeaponId.Namekuji);
            if (def == null) return;
            WeaponGelPuddleBlock gel = def.gelPuddle;
            if (!DamageTargetResolver.TryFromReceiver(body, out DamageTarget target)) return;

            SpecialTierRule rule = SpecialImpacts.RuleFor(gel.slipRules, body.Tier);
            bool kill = SpecialImpacts.Kills(rule, Mathf.Max(1, SpecialWeaponRoundBridge.CurrentRound));
            float amount = kill ? body.Health + 1f : body.MaxHealth * Mathf.Clamp01(rule != null ? rule.maxHpShare : 0f);
            Vector3 feet = body.transform.position;
            int transaction = DamageTransaction.NextId();   // the slip is its own damage event, resumed (never begun) inside Apply
            if (amount > 0f)
            {
                SpecialImpacts.Apply(SpecialWeaponId.Namekuji, transaction, target, amount, gel.ignoresArmor, 0f, DamageSourceKind.Bullet,
                    feet + Vector3.up * 0.3f, Vector3.up);
            }

            if (kill)
            {
                SlipKills++;
                if (!body.IsAlive && MakePuddle(transaction, feet, Vector3.up, gel.deathPuddleRadius, gel.deathPuddleSeconds) != 0) DeathPuddles++;
                return;
            }

            SlipShares++;
            if (rule != null && rule.immuneSeconds > 0f) body.SetGelImmunity(rule.immuneSeconds);
        }

        /// <summary>Lets the registry expire puddles on time with no body or player querying it.</summary>
        public static void Tick()
        {
            if (s_hooked) GelPuddleRegistry.Tick();
        }

        private static void OnPuddleCreated(GelPuddleInfo info)
        {
            for (int i = 0; i < s_tracked.Length; i++)
            {
                if (s_tracked[i].Id != 0) continue;
                s_tracked[i] = new Tracked { Id = info.Id, Center = info.Center, Radius = info.Radius };
                return;
            }
        }

        private static void OnPuddleExpired(int id)
        {
            Vector3 center = Vector3.zero;
            float radius = 0f;
            for (int i = 0; i < s_tracked.Length; i++)
            {
                if (s_tracked[i].Id != id) continue;
                center = s_tracked[i].Center;
                radius = s_tracked[i].Radius;
                s_tracked[i] = default;
                break;
            }

            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.Namekuji, SpecialUsageKind.FieldEnded, SpecialWeaponUsage.For(SpecialWeaponId.Namekuji).FieldVfxFamily,
                center, Vector3.up, radius, 0f, id);
        }

        /// <summary>Scene unload / run end: every live puddle ends now (a new run never starts on the old run's gel).</summary>
        public static void Clear()
        {
            for (int i = 0; i < s_tracked.Length; i++)
            {
                int id = s_tracked[i].Id;
                if (id != 0) GelPuddleRegistry.Expire(id);   // raises PuddleExpired → FieldEnded, clears the slot
                s_tracked[i] = default;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < s_tracked.Length; i++) s_tracked[i] = default;
            s_configured = null;
            s_hooked = false;   // the registry drops its subscribers on the same reset
            PuddlesMade = 0;
            DeathPuddles = 0;
            SlipKills = 0;
            SlipShares = 0;
            ZombieEnemy.SlipOutcomeHandler = null;
        }

        /// <summary>Tests: drop the slip hook and the counters (the registry's own puddles: GelPuddleRegistry.ResetStatics).</summary>
        public static void ResetForTests()
        {
            Clear();
            if (s_hooked)
            {
                GelPuddleRegistry.PuddleCreated -= OnPuddleCreated;
                GelPuddleRegistry.PuddleExpired -= OnPuddleExpired;
            }

            ResetStatics();
        }
    }
}
