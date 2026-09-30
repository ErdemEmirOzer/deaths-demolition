using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>WP-077 B6 (direction §4 telemetry set, C-00 M-33): what happened in the round-end shop's tree.</summary>
    public enum RunTreeEventKind
    {
        NodeBought = 0,
        PinSet = 1,
        OfferDrawn = 2,
        HearthStoked = 3,
        Undo = 4,
        Reforged = 5
    }

    /// <summary>
    /// One tree event. <see cref="NodeId"/> is the node id (for <see cref="RunTreeEventKind.OfferDrawn"/> the offered ids,
    /// comma-separated); <see cref="Count"/> is the Hearth stack after a stoke or the frontier size of a draw;
    /// <see cref="Pity"/> the offered keystones / seals / capstones the luck guard doubled.
    /// </summary>
    public readonly struct RunTreeEvent
    {
        public readonly RunTreeEventKind Kind;
        public readonly string NodeId;
        public readonly int Round;
        public readonly int Price;
        public readonly RunTreeLane Lane;
        public readonly RunTreeNodeKind NodeKind;
        public readonly int Count;
        public readonly int Pity;
        public readonly bool Reroll;

        public RunTreeEvent(RunTreeEventKind kind, string nodeId, int round, int price = 0, RunTreeLane lane = RunTreeLane.Weapon,
            RunTreeNodeKind nodeKind = RunTreeNodeKind.Tier, int count = 0, int pity = 0, bool reroll = false)
        {
            Kind = kind;
            NodeId = nodeId ?? string.Empty;
            Round = round;
            Price = price;
            Lane = lane;
            NodeKind = nodeKind;
            Count = count;
            Pity = pity;
            Reroll = reroll;
        }
    }

    /// <summary>DEC-112: which capstone fired on the hit path (<see cref="RunTreeHooks.CapstoneProc"/>).</summary>
    public enum CapstoneProcKind
    {
        /// <summary>cap.ar_2 Third Eye: the burst's 3rd round struck (value % harder, chains to a nearby enemy).</summary>
        ThirdEyeArc = 0,
        /// <summary>cap.lmg_2 Black Iron: the body whose plate the round broke started its stagger.</summary>
        BlackIronStagger = 1,
        /// <summary>cap.smg_2 Lightning Fang: a hit-staggered body took the capstone's bonus damage.</summary>
        LightningFangBonus = 2
    }

    /// <summary>One capstone proc. <see cref="Body"/> is the struck zombie (null for another receiver); <see cref="TargetLifeId"/> its life id (0 = none).</summary>
    public readonly struct CapstoneProcInfo
    {
        public readonly CapstoneProcKind Kind;
        public readonly ClassicWeaponId Weapon;
        public readonly Vector3 Point;
        public readonly ZombieEnemy Body;
        public readonly int TargetLifeId;

        public CapstoneProcInfo(CapstoneProcKind kind, ClassicWeaponId weapon, Vector3 point, ZombieEnemy body)
        {
            Kind = kind;
            Weapon = weapon;
            Point = point;
            Body = body;
            TargetLifeId = body != null ? body.LifeId : 0;
        }
    }

    /// <summary>
    /// WP-077 B6: the tree's event bus (RunTelemetry listens). Raised by <see cref="RoundUpgradeShop"/> at the shop's
    /// command points — never on a combat hot path. A throwing listener is logged, never propagated.
    /// </summary>
    public static class RunTreeEvents
    {
        public static event Action<RunTreeEvent> Raised;

        /// <summary>No listener: callers skip building an event's strings.</summary>
        public static bool IsSilent => Raised == null;

        public static void Raise(in RunTreeEvent e)
        {
            Action<RunTreeEvent> handler = Raised;
            if (handler == null) return;
            try { handler.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Raised = null;
        }
    }
}
