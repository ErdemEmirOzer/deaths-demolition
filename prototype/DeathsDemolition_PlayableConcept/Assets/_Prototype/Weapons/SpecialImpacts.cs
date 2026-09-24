using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using RegistryTarget = DD.Prototype.Damage.DamageTarget;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-069 B4 · what the two Specials' projectiles do where they land (DIRECTION §7a / §7b, DEC-097).
    ///
    /// Raiju orb → the body it struck starts a chain (S-03 <see cref="ChainTargetResolver"/>: greedy nearest, 6 m hops,
    /// line of sight, up to maxTargets). Hop i lands at i × jumpDelaySeconds: <see cref="ZombieEnemy.ApplyShock"/> for
    /// the tremble, a usage Hit (Count = hop index) and the tier rule — a KILL rule lands its lethal damage at the END of
    /// the tremble (death / score / gold then, agreed with S-03), a SHARE rule (the Ultra: 60 % + 2.5 s) lands at once.
    /// Electricity passes regional armour. A chain body within selfShockRadius of the player shocks the player once per
    /// shot (first target ≤ selfShockFirstRadius: the larger hit) plus a short slow. An orb that meets the world, or runs
    /// out of range / life, fizzles: usage FieldEnded, no chain.
    ///
    /// Namekuji blob → a body: the direct-hit tier rule (liquefy, or a share + S-03's knock-down) and a puddle under it;
    /// the world (a wall runs down to the floor): a puddle. The puddles, the slip outcome and the player's slide are
    /// <see cref="SpecialGelEffects"/> on S-03's <see cref="GelPuddleRegistry"/>.
    ///
    /// Scaled time (ticked by <see cref="ProjectileSystem.Tick"/>): pause and hit-stop freeze a chain with the orbs.
    /// Pooled records, no allocation per shot.
    /// </summary>
    public static class SpecialImpacts
    {
        public const int MaxChains = 8;

        private sealed class Chain
        {
            public bool Live;
            public int Transaction;
            public WeaponDefinition Def;
            public Vector3 Contact;
            public float T;
            public int Count;
            public int NextHop;
            public int Round;
            public readonly DamageTarget[] Targets = new DamageTarget[ChainTargetResolver.BufferSize];
            public readonly RegistryTarget[] Resolved = new RegistryTarget[ChainTargetResolver.BufferSize];
            public readonly ZombieEnemy[] Bodies = new ZombieEnemy[ChainTargetResolver.BufferSize];
            public readonly float[] LethalAt = new float[ChainTargetResolver.BufferSize];   // < 0 = nothing pending
        }

        private static readonly Chain[] s_chains = CreateChains();
        private static bool s_hooked;

        /// <summary>Tests / probes.</summary>
        public static int ActiveChains { get; private set; }
        public static int HopsApplied { get; private set; }
        public static int LethalApplied { get; private set; }
        public static int SelfShocks { get; private set; }
        public static int Fizzles { get; private set; }
        public static int GelHits { get; private set; }
        public static int Liquefied { get; private set; }
        public static int Knockdowns { get; private set; }

        /// <summary>Test seam: the player the self-shock rule measures against (null = the active weapon controller's player).</summary>
        public static Transform PlayerOverride { get; set; }

        private static Chain[] CreateChains()
        {
            var chains = new Chain[MaxChains];
            for (int i = 0; i < chains.Length; i++) chains[i] = new Chain();
            return chains;
        }

        internal static void EnsureHooked()
        {
            if (s_hooked) return;
            s_hooked = true;
            ProjectileEvents.Expired += OnExpired;
        }

        // ------------------------------------------------------------------ contacts (HitscanEngine)

        internal static void OnBody(in ProjectileState p, in DamageTarget target, Vector3 point)
        {
            EnsureHooked();
            if (!SpecialWeaponRuntime.TryGetSpecial(p.WeaponId, out SpecialWeaponId id)) return;
            WeaponDefinition def = SpecialWeaponConfigSection.DefinitionOf(id);
            if (def == null) return;

            if (id == SpecialWeaponId.Raiju)
            {
                StartChain(p.TransactionId, def, target, point);
                SpecialWeaponUsageEvents.Publish(new SpecialUsageEvent(id, SpecialUsageKind.FieldEnded, SpecialWeaponUsage.For(id).FieldVfxFamily,
                    point, p.Direction, def.arcChain.jumpRadius, 0f, 1, target, def.projectile.impulse, 1f, p.TransactionId));
                return;
            }

            GelDirectHit(p.TransactionId, def, target, point, p.Direction);
        }

        internal static void OnWorld(in ProjectileState p, Vector3 point, Vector3 normal)
        {
            EnsureHooked();
            if (!SpecialWeaponRuntime.TryGetSpecial(p.WeaponId, out SpecialWeaponId id)) return;
            WeaponDefinition def = SpecialWeaponConfigSection.DefinitionOf(id);
            if (id == SpecialWeaponId.Namekuji && def != null)
            {
                SpecialGelEffects.MakePuddle(p.TransactionId, point, normal, def.gelPuddle.puddleRadius, def.gelPuddle.puddleLifetimeSeconds);
                return;
            }

            Fizzle(id, def, point, p.Direction);
        }

        private static void OnExpired(ProjectileExpireInfo info)
        {
            if (info.Reason == ProjectileEndReason.Cleared || !SpecialWeaponRuntime.TryGetSpecial(info.WeaponId, out SpecialWeaponId id)) return;
            Fizzle(id, SpecialWeaponConfigSection.DefinitionOf(id), info.Position, Vector3.forward);
        }

        private static void Fizzle(SpecialWeaponId id, WeaponDefinition def, Vector3 point, Vector3 direction)
        {
            Fizzles++;
            float seconds = def != null && id == SpecialWeaponId.Raiju ? def.arcChain.orbFizzleSeconds : 0f;
            // Raiju: the orb dies where it is. Namekuji: a blob that ran out of range / life in the air leaves nothing.
            SpecialWeaponUsageEvents.Publish(id, SpecialUsageKind.FieldEnded, SpecialWeaponUsage.For(id).FieldVfxFamily,
                point, direction, 0f, seconds, 0);
        }

        // ------------------------------------------------------------------ Raiju chain

        private static void StartChain(int transaction, WeaponDefinition def, in DamageTarget first, Vector3 contact)
        {
            Chain c = null;
            for (int i = 0; i < s_chains.Length; i++)
            {
                if (!s_chains[i].Live) { c = s_chains[i]; break; }
            }

            if (c == null) c = s_chains[0];   // eight chains at once is far past the magazine; recycle the oldest slot

            c.Live = true;
            c.Transaction = transaction;
            c.Def = def;
            c.Contact = contact;
            c.T = 0f;
            c.NextHop = 0;
            c.Round = Mathf.Max(1, SpecialWeaponRoundBridge.CurrentRound);
            // S-03's resolver speaks the registry's target; each hop is turned back into the weapon-side target (same id / height).
            var seed = new RegistryTarget(first.Root, first.Receiver, first.Explosive, first.Root != null ? first.Root.position : contact, 0.5f);
            c.Count = ChainTargetResolver.Resolve(seed, contact, Mathf.Max(1, def.arcChain.maxTargets), def.arcChain.jumpRadius, c.Resolved);
            for (int i = 0; i < c.Count; i++)
            {
                if (i == 0) c.Targets[i] = first;
                else if (!DamageTargetResolver.TryFromReceiver(c.Resolved[i].Receiver, out c.Targets[i])) c.Targets[i] = default;
                c.Resolved[i] = default;
                c.Bodies[i] = c.Targets[i].Receiver as ZombieEnemy;
                c.LethalAt[i] = -1f;
            }

            RecountActive();
            SelfShock(c);
        }

        /// <summary>Advance every live chain. Called from <see cref="ProjectileSystem.Tick"/> outside the projectile steps.</summary>
        public static void Tick(float dt)
        {
            SpecialGelEffects.Tick();
            if (ActiveChains == 0 || dt <= 0f) return;

            for (int ci = 0; ci < s_chains.Length; ci++)
            {
                Chain c = s_chains[ci];
                if (!c.Live) continue;
                c.T += dt;
                WeaponArcChainBlock arc = c.Def.arcChain;

                while (c.NextHop < c.Count && c.T + 1e-5f >= c.NextHop * Mathf.Max(0f, arc.jumpDelaySeconds))
                {
                    Hop(c, c.NextHop);
                    c.NextHop++;
                }

                bool pending = c.NextHop < c.Count;
                for (int i = 0; i < c.NextHop; i++)
                {
                    if (c.LethalAt[i] < 0f) continue;
                    if (c.T + 1e-5f >= c.LethalAt[i])
                    {
                        c.LethalAt[i] = -1f;
                        Land(c, i, true, 0f);
                    }
                    else
                    {
                        pending = true;
                    }
                }

                if (!pending) c.Live = false;
            }

            RecountActive();
        }

        private static void Hop(Chain c, int i)
        {
            DamageTarget target = c.Targets[i];
            ZombieEnemy body = c.Bodies[i];
            if (!target.IsValid || body == null || !body.IsAlive) return;

            WeaponArcChainBlock arc = c.Def.arcChain;
            SpecialTierRule rule = RuleFor(arc.tierRules, body.Tier);
            bool kill = Kills(rule, c.Round);
            float seconds = kill ? arc.shockSeconds : Mathf.Max(arc.shockSeconds, rule != null ? rule.stunSeconds : 0f);
            body.ApplyShock(c.Transaction, seconds);
            HopsApplied++;

            Vector3 from = i == 0 ? c.Contact : HitscanEngine.CentreOf(c.Targets[i - 1]);
            SpecialWeaponUsageEvents.Publish(new SpecialUsageEvent(SpecialWeaponId.Raiju, SpecialUsageKind.Hit, SpecialWeaponUsage.For(SpecialWeaponId.Raiju).FireVfxFamily,
                from, HitscanEngine.CentreOf(target) - from, arc.jumpRadius, seconds, i, target, c.Def.projectile.impulse, 1f, c.Transaction));

            if (kill)
            {
                c.LethalAt[i] = c.T + seconds;   // death, score and gold at the end of the tremble (S-03 contract)
            }
            else
            {
                Land(c, i, false, rule != null ? rule.maxHpShare : 0f);
            }
        }

        private static void Land(Chain c, int i, bool lethal, float share)
        {
            ZombieEnemy body = c.Bodies[i];
            if (body == null || !body.IsAlive) return;
            float amount = lethal ? body.Health + 1f : body.MaxHealth * Mathf.Clamp01(share);
            if (amount <= 0f) return;
            Apply(SpecialWeaponId.Raiju, c.Transaction, c.Targets[i], amount, c.Def.arcChain.ignoresArmor, c.Def.projectile.impulse, DamageSourceKind.Chain,
                HitscanEngine.CentreOf(c.Targets[i]), Vector3.up);
            if (lethal) LethalApplied++;
            if (!body.IsAlive) SpecialLinks.OnRaijuKill();   // WP-077 link.arc.storm
        }

        private static void SelfShock(Chain c)
        {
            WeaponArcChainBlock arc = c.Def.arcChain;
            Transform player = PlayerOverride;
            WeaponController controller = WeaponRegistry.ActiveController;
            if (player == null && controller != null) player = controller.transform.root;
            if (player == null || c.Count == 0) return;

            Vector3 at = player.position;
            float damage = 0f;
            if (arc.selfShockFirstRadius > 0f && Planar(HitscanEngine.CentreOf(c.Targets[0]) - at) <= arc.selfShockFirstRadius)
            {
                damage = arc.selfShockFirstDamage;
            }
            else if (arc.selfShockRadius > 0f)
            {
                for (int i = 0; i < c.Count; i++)
                {
                    if (Planar(HitscanEngine.CentreOf(c.Targets[i]) - at) <= arc.selfShockRadius) { damage = arc.selfShockDamage; break; }
                }
            }

            if (damage <= 0f) return;
            SelfShocks++;
            PlayerHealth health = player.GetComponentInChildren<PlayerHealth>();
            if (health != null) health.ApplyDamage(damage, c.Contact, SpecialWeaponKeys.Raiju);
            if (controller != null) controller.ApplySpecialSelfSlow(arc.selfSlowSeconds);
            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.Raiju, SpecialUsageKind.SelfShock, SpecialWeaponUsage.For(SpecialWeaponId.Raiju).FireVfxFamily,
                at, c.Contact - at, arc.selfShockRadius, arc.selfSlowSeconds, Mathf.RoundToInt(damage));
        }

        private static float Planar(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        // ------------------------------------------------------------------ Namekuji direct hit

        private static void GelDirectHit(int transaction, WeaponDefinition def, in DamageTarget target, Vector3 point, Vector3 direction)
        {
            GelHits++;
            WeaponGelPuddleBlock gel = def.gelPuddle;
            var body = target.Receiver as ZombieEnemy;
            SpecialWeaponUsageEvents.Publish(new SpecialUsageEvent(SpecialWeaponId.Namekuji, SpecialUsageKind.Hit, SpecialWeaponUsage.For(SpecialWeaponId.Namekuji).FireVfxFamily,
                point, direction, gel.puddleRadius, 0f, 0, target, def.projectile.impulse, 1f, transaction));

            // The puddle lands under the body (its feet), not at the chest the blob struck.
            Vector3 feet = target.Root != null ? target.Root.position : point;
            if (body != null && body.IsAlive)
            {
                SpecialTierRule rule = RuleFor(gel.directHitRules, body.Tier);
                bool kill = Kills(rule, Mathf.Max(1, SpecialWeaponRoundBridge.CurrentRound));
                float amount = kill ? body.Health + 1f : body.MaxHealth * Mathf.Clamp01(rule != null ? rule.maxHpShare : 0f);
                if (amount > 0f)
                {
                    Apply(SpecialWeaponId.Namekuji, transaction, target, amount, gel.ignoresArmor, def.projectile.impulse, DamageSourceKind.Bullet, point, direction);
                }

                if (kill && !body.IsAlive)
                {
                    Liquefied++;
                    SpecialWeaponUsageEvents.Publish(new SpecialUsageEvent(SpecialWeaponId.Namekuji, SpecialUsageKind.Liquefied, SpecialWeaponUsage.For(SpecialWeaponId.Namekuji).FireVfxFamily,
                        feet, direction, 0f, 0f, 1, target, def.projectile.impulse, 1f, transaction));
                }
                else if (body.IsAlive && rule != null && rule.stunSeconds > 0f && body.ApplyKnockdown(transaction, rule.stunSeconds))
                {
                    Knockdowns++;
                }
            }

            SpecialGelEffects.MakePuddle(transaction, feet, Vector3.up, gel.puddleRadius, gel.puddleLifetimeSeconds);
        }

        // ------------------------------------------------------------------ shared

        /// <summary>A tier's rule; the Standard row when the tier has none.</summary>
        public static SpecialTierRule RuleFor(SpecialTierRule[] rules, EnemyTier tier)
        {
            if (rules == null || rules.Length == 0) return null;
            for (int i = 0; i < rules.Length; i++)
            {
                if (rules[i] != null && rules[i].tier == tier) return rules[i];
            }

            return rules[0];
        }

        /// <summary>Kill outright this round (no rule = kill).</summary>
        public static bool Kills(SpecialTierRule rule, int round)
        {
            if (rule == null) return true;
            return rule.kill && (rule.killUpToRound <= 0 || round <= rule.killUpToRound);
        }

        internal static void Apply(SpecialWeaponId special, int transaction, in DamageTarget target, float amount, bool ignoreArmor, float force,
            DamageSourceKind source, Vector3 point, Vector3 direction)
        {
            if (target.Receiver == null) return;
            // Inside a projectile step (the gel's direct hit) the shot's transaction is already open; a chain hop resumes it.
            bool resume = DamageTransaction.Current != transaction;
            if (resume) DamageTransaction.Resume(transaction);
            DamageHitContext.SetSpecial(special, ignoreArmor, null);
            try
            {
                target.Receiver.ApplyHit(new DamageHit
                {
                    Amount = amount,
                    Region = BodyRegion.UpperBody,
                    Point = point,
                    Direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up,
                    ImpactForce = force,
                    Source = source
                });
            }
            finally
            {
                DamageHitContext.Clear();
                if (resume) DamageTransaction.End();
            }
        }

        private static void RecountActive()
        {
            int n = 0;
            for (int i = 0; i < s_chains.Length; i++) if (s_chains[i].Live) n++;
            ActiveChains = n;
        }

        /// <summary>Drop every chain (scene unload, run end, tests).</summary>
        public static void Clear()
        {
            for (int i = 0; i < s_chains.Length; i++)
            {
                Chain c = s_chains[i];
                c.Live = false;
                c.Def = null;
                for (int k = 0; k < c.Bodies.Length; k++) { c.Bodies[k] = null; c.Targets[k] = default; c.Resolved[k] = default; }
            }

            ActiveChains = 0;
            SpecialGelEffects.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clear();
            HopsApplied = 0;
            LethalApplied = 0;
            SelfShocks = 0;
            Fizzles = 0;
            GelHits = 0;
            Liquefied = 0;
            Knockdowns = 0;
            PlayerOverride = null;
            if (s_hooked) ProjectileEvents.Expired -= OnExpired;
            s_hooked = false;
        }
    }
}
