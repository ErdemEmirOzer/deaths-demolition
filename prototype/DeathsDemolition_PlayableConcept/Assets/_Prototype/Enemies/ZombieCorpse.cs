using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Damage;
using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-076 §2.3–2.4 / DEC-096 · S-03 is the single owner of the corpse: its lifetime (CrowdFeelSettings v2 × quality
    /// tier), the corpse ceiling (S-06 6 / 10 / 16 — over it the oldest corpse is buried, the same event S-06 plays as
    /// "the oldest sinks"), and the floor guard. A corpse that falls off the world is a SILENT despawn: the reward was
    /// paid at death, so it is not an out-of-world removal (no <see cref="OutOfWorldRemoved"/>, no removal counter);
    /// only <see cref="CorpsesFellOffWorld"/> counts it. After death S-03 writes no transform on the driven body.
    /// </summary>
    public sealed partial class ZombieEnemy
    {
        /// <summary>The impulse handed to the driver at this life's death (readout / tests).</summary>
        public DeathImpulse LastDeathImpulse { get; private set; }

        /// <summary>WP-076 B4/B5: shock / gel set this (Electrocuted / Sliding / Downed); Standing = no override.</summary>
        private ZombieBodyState _bodyStateOverride;

        /// <summary>The body's state now (§2.1): a Special override, else Staggered while the stagger runs, else Standing (a held grab is Standing).</summary>
        public ZombieBodyState BodyState => _bodyStateOverride != ZombieBodyState.Standing ? _bodyStateOverride
            : _animator != null && _animator.IsStaggering ? ZombieBodyState.Staggered : ZombieBodyState.Standing;

        /// <summary>Tests: corpse ceiling override (≤ 0 = the quality tier's value).</summary>
        public static int CorpseCeilingOverride { get; set; }
        /// <summary>Corpses buried early because the ceiling was reached.</summary>
        public static int CorpsesEvicted { get; private set; }
        /// <summary>Corpses despawned by the floor guard (telemetry corpse_fell_off_world).</summary>
        public static int CorpsesFellOffWorld { get; private set; }
        public static int CorpseCount => s_corpses.Count;

        private static readonly List<ZombieEnemy> s_corpses = new List<ZombieEnemy>(64);   // oldest first
        private bool _inCorpseList;

        private void RegisterCorpse()
        {
            if (!_inCorpseList) { s_corpses.Add(this); _inCorpseList = true; }
            int ceiling = CorpseCeilingOverride > 0 ? CorpseCeilingOverride : CrowdFeelSettings.Active.CorpseCeilingFor(QualityTiers.Current);
            while (s_corpses.Count > ceiling)
            {
                ZombieEnemy oldest = s_corpses[0];
                if (oldest == this) break;
                CorpsesEvicted++;
                if (oldest == null || !oldest._dying) { s_corpses.RemoveAt(0); if (oldest != null) oldest._inCorpseList = false; continue; }
                oldest.Despawn();   // OnDespawned → UnregisterCorpse
            }
        }

        private void UnregisterCorpse()
        {
            if (!_inCorpseList) return;
            s_corpses.Remove(this);
            _inCorpseList = false;
        }

        /// <summary>§2.4-2: the corpse fell below the absolute floor or <see cref="FallDepth"/> under the last on-mesh point → silent despawn.</summary>
        private bool TickCorpseFloor()
        {
            float y = transform.position.y;
            if (y >= AbsoluteFloorY && (!_hasOnMeshY || y >= _lastOnMeshY - FallDepth)) return false;
            CorpsesFellOffWorld++;
            Despawn();
            return true;
        }

        /// <summary>Tests: zero the corpse counters (the list empties itself as bodies despawn).</summary>
        public static void ResetCorpseStatics() { CorpseCeilingOverride = 0; CorpsesEvicted = 0; CorpsesFellOffWorld = 0; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCorpseDomain() { s_corpses.Clear(); ResetCorpseStatics(); }
    }
}
