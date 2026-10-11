using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B3 · the THIN ADAPTER between S-04's tree systems and S-03's body (C-00 12.10 alignment). Every member is a
    /// one-to-one call of S-03's B3 draft (<c>Enemies/ZombieTreeStatus.cs</c>: same names, same parameters; <see cref="IceResult"/>
    /// and <see cref="TreeStunKind"/> are S-03's types in DD.Prototype.Enemies) or of a member <c>ZombieEnemy</c> already has;
    /// nothing here is a new body signature. Production: <c>TreeZombieBinding</c> wraps each living zombie in a
    /// pooled adapter (no field or interface on <c>ZombieEnemy</c>). Tests: a fake receiver.
    ///
    /// Split of state (PUSH model, S-03 S6): the BODY owns cold stacks / slow / freeze / freeze cooldown / tier rules
    /// (FreezeEliteShare, FreezeUltraSlow), stun (StunHeavyShare, Max(until)), pull (HolePull*Share, core dead zone),
    /// FreezeVulnerability and the ice tint. S-04 owns burn and venom (stacks, DoT, Inferno spread, Corrosion threshold,
    /// fire / venom vulnerability), zones, blasts, wells, the cores and every death-triggered proc.
    /// </summary>
    public interface ITreeStatusReceiver
    {
        /// <summary>Status slot (adapter-side, -1 = none). Only <see cref="TreeStatusSystem"/> writes it. Not on the body.</summary>
        int TreeSlot { get; set; }

        // ---------------------------------------------------------------- existing ZombieEnemy members
        EnemyTier Tier { get; }
        bool IsAlive { get; }
        /// <summary>Root (feet) position; tree area tests are horizontal with a fixed body reach.</summary>
        Vector3 Position { get; }
        float MaxHealth { get; }

        /// <summary>
        /// Blast damage through the body's existing explosion path (<c>ZombieEnemy.ApplyExplosion</c>: regional split,
        /// explosion reaction, death). The adapter sets DamageHitContext (weapon credit) around it.
        /// </summary>
        DamageResult ApplyExplosion(float totalDamage, Vector3 origin);

        /// <summary>Existing DEC-112 tree stagger (Thunder Lord 0.4 s; the body's 1.5 s lock applies, S3).</summary>
        bool ApplyTreeStagger(float seconds, Vector3 direction);

        // ---------------------------------------------------------------- S-03 B3 plan §3 (1:1)
        /// <summary>Cold stacks; the body runs slow / freeze / cooldown / tier rules (S1: a freeze spends the stacks).</summary>
        /// <param name="ignoreCooldown">Frozen Gravity only (S-03 ApplyIce overload, 12.10): the freeze cooldown is skipped; tier rules stay.</param>
        IceResult ApplyIce(int stacks, bool ignoreCooldown = false);

        /// <summary>Stun for <paramref name="seconds"/> (raw; the body applies StunHeavyShare to Elite / Ultra and Max(until)).</summary>
        bool ApplyStun(float seconds, TreeStunKind kind);

        /// <summary>Black hole pull (raw HolePullSpeed; the body applies the tier share and the core dead zone), held for <paramref name="hold"/> s.</summary>
        void ApplyPull(Vector3 center, float speed, float hold);

        /// <summary>DoT / ground / hole / bolt damage: no reaction, no push, regional armour pierced (S5); death and reward as usual.</summary>
        DamageResult ApplyStatusDamage(float amount, TreeElement element);

        /// <summary>The body's ONE status tint (element, 0..1; None / 0 clears). S-04 picks it: frozen &gt; burning &gt; venom &gt; cold stacks.</summary>
        void SetStatusTint(TreeElement element, float strength);

        /// <summary>Frozen right now (Frozen Gravity collapse ×1.5, Frozen Gravity freeze gate).</summary>
        bool IsFrozen { get; }

        /// <summary>Was frozen when it died (locked in Die) — Shatter.</summary>
        bool FrozenAtDeath { get; }

        /// <summary>The body's live cold stacks (0 when expired) — only for the status tint.</summary>
        int IceStacks { get; }
    }

    /// <summary>What kind of tree damage is being applied (read from <see cref="TreeDamageContext"/> during a call).</summary>
    public enum TreeDamageKind : byte
    {
        None = 0,
        /// <summary>Burn / venom tick on the body (4 Hz).</summary>
        Dot = 1,
        /// <summary>Fire ground tick (0.5 s).</summary>
        Area = 2,
        /// <summary>A tree blast.</summary>
        Blast = 3,
        /// <summary>Black hole damage while inside.</summary>
        Hole = 4,
        /// <summary>Thunder Cascade bolt.</summary>
        Bolt = 5
    }

    /// <summary>
    /// Ambient facts about the tree damage being applied RIGHT NOW (the DamageHitContext pattern), set around one receiver
    /// call and restored after it, single-threaded. A death inside the call is of <see cref="Depth"/> (chain generation).
    /// </summary>
    public static class TreeDamageContext
    {
        public static bool Active { get; private set; }
        public static TreeDamageKind Kind { get; private set; }
        public static TreeProcKind Proc { get; private set; }
        public static Vector3 Origin { get; private set; }
        public static TreeElement Element { get; private set; }
        public static int Depth { get; private set; }
        /// <summary>The weapon credited with the kill (the weapon in hand at the last hit).</summary>
        public static ClassicWeaponId Weapon { get; private set; }

        public static bool IsDot => Kind == TreeDamageKind.Dot || Kind == TreeDamageKind.Area;

        /// <summary>Receiver damage calls made through the systems since the last reset (tests / F1).</summary>
        public static int Calls { get; private set; }

        /// <summary>Blast damage: <see cref="ITreeStatusReceiver.ApplyExplosion"/> with the context set.</summary>
        internal static DamageResult Explosion(ITreeStatusReceiver body, float amount, TreeElement element, int depth, TreeProcKind proc, Vector3 origin)
        {
            if (body == null || amount <= 0f) return default;
            Saved saved = Push(TreeDamageKind.Blast, proc, origin, element, depth);
            try
            {
                return body.ApplyExplosion(amount, origin);
            }
            finally
            {
                Pop(in saved);
            }
        }

        /// <summary>Status damage (DoT, ground, hole, bolt): <see cref="ITreeStatusReceiver.ApplyStatusDamage"/> with the context set.</summary>
        internal static DamageResult Status(ITreeStatusReceiver body, float amount, TreeElement element, int depth, TreeDamageKind kind,
            TreeProcKind proc, Vector3 origin)
        {
            if (body == null || amount <= 0f) return default;
            Saved saved = Push(kind, proc, origin, element, depth);
            try
            {
                return body.ApplyStatusDamage(amount, element);
            }
            finally
            {
                Pop(in saved);
            }
        }

        /// <summary>Opens a damage transaction when none is open (one id per blast / tick batch); true when this call owns it.</summary>
        internal static bool BeginTransaction(int id)
        {
            if (DamageTransaction.Current != 0) return false;
            DamageTransaction.Resume(id > 0 ? id : DamageTransaction.NextId());
            return true;
        }

        internal static void EndTransaction(bool owned)
        {
            if (owned) DamageTransaction.End();
        }

        private struct Saved
        {
            public bool Active;
            public TreeDamageKind Kind;
            public TreeProcKind Proc;
            public Vector3 Origin;
            public TreeElement Element;
            public int Depth;
            public ClassicWeaponId Weapon;
        }

        private static Saved Push(TreeDamageKind kind, TreeProcKind proc, Vector3 origin, TreeElement element, int depth)
        {
            var saved = new Saved { Active = Active, Kind = Kind, Proc = Proc, Origin = Origin, Element = Element, Depth = Depth, Weapon = Weapon };
            Active = true;
            Kind = kind;
            Proc = proc;
            Origin = origin;
            Element = element;
            Depth = depth;
            Weapon = TreeSystemsRunner.CurrentWeapon;
            Calls++;
            return saved;
        }

        private static void Pop(in Saved s)
        {
            Active = s.Active;
            Kind = s.Kind;
            Proc = s.Proc;
            Origin = s.Origin;
            Element = s.Element;
            Depth = s.Depth;
            Weapon = s.Weapon;
        }

        internal static void Reset()
        {
            Active = false;
            Kind = TreeDamageKind.None;
            Proc = TreeProcKind.None;
            Origin = Vector3.zero;
            Element = TreeElement.None;
            Depth = 0;
            Weapon = default;
            Calls = 0;
        }
    }
}
