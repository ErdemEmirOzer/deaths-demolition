using System;
using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>The director's single funnel, as a delegate (the <c>in</c> parameter rules out Func).</summary>
    public delegate bool AudioPlayFunc(in AudioPlacement placement, Vector3 position, float gainScale, int cuePriority);

    /// <summary>
    /// WP-021 · The listeners the WP-002 director did not have: the zombie VOCAL set
    /// (DEC-067) from the damage contract and from the attack state machine.
    ///
    ///   EnemyDamaged  → pain grunt, throttled per enemy life (a shotgun's nine pellets
    ///                   are one grunt, not nine)
    ///   EnemyDied     → the death vocal, once per life, over the body cue
    ///   attack WINDUP → the telegraph growl. No event exists for it, so this polls
    ///                   <see cref="ZombieEnemy.AttackPhase"/> on the live list and fires
    ///                   on the None/Recovery → Windup edge. The telegraph is 0.55 s
    ///                   (EnemyTierSettings), so the growl lands ≥ 0.3 s before the swing
    ///                   by construction; it is positional (REQ-104 "yön + tehdit").
    ///   attack ACTIVE → the swing, on the Windup → Active edge.
    ///
    /// Polling forty enemies' enum once a frame is a few hundred instructions and no
    /// allocation; it keeps this package out of <c>Enemies/</c> entirely and it goes away
    /// the day WP-018's animation events (ADR-018-1 hit_window) reach main - see the
    /// reserved rows in <see cref="AudioEventMap"/>.
    ///
    /// Everything is routed through the director's single funnel so the budget, the
    /// bus, the duck and the mixer group are the same as for every other sound.
    /// </summary>
    public sealed class AudioEventRouter
    {
        /// <summary>Seconds between pain grunts from the same enemy. PROTOTYPE-ONLY / INVENTED.</summary>
        public const float HitThrottleSeconds = 0.35f;

        private struct Tracked
        {
            public ZombieAttackPhase Phase;
            public float LastHit;
            public float NextIdle;
            public bool Seen;
        }

        /// <summary>Idle breath cadence per enemy (seconds), only inside <see cref="IdleRange"/> of the listener. PROTOTYPE-ONLY / INVENTED.</summary>
        public const float IdleMinSeconds = 6f;
        public const float IdleMaxSeconds = 14f;
        public const float IdleRange = 9f;

        /// <summary>Delay between the break cue and the plate landing. PROTOTYPE-ONLY / INVENTED.</summary>
        public const float PieceDropDelay = 0.42f;

        private struct PendingDrop
        {
            public float At;
            public Vector3 Point;
        }

        private readonly List<PendingDrop> _drops = new List<PendingDrop>(8);
        private SynthRandom _rng = new SynthRandom(0x51D3);
        private System.Func<Vector3> _listenerPosition;

        private readonly AudioPlayFunc _play;
        private readonly Dictionary<ZombieEnemy, Tracked> _tracked = new Dictionary<ZombieEnemy, Tracked>(48);
        private readonly List<ZombieEnemy> _stale = new List<ZombieEnemy>(16);
        private int _sweep;

        /// <summary>Windup cues raised. Test/F1 readout.</summary>
        public int WindupCount { get; private set; }
        public int AttackCount { get; private set; }
        public int HitCount { get; private set; }
        public int DeathCount { get; private set; }

        /// <summary>Hits swallowed by the per-enemy throttle. Test readout.</summary>
        public int HitThrottled { get; private set; }
        public int IdleCount { get; private set; }
        public int StaggerCount { get; private set; }
        public int RiseCount { get; private set; }
        public int ImpactCount { get; private set; }
        public int PieceDropCount { get; private set; }
        public int BreakCount { get; private set; }
        public int FootstepCount { get; private set; }
        public int SettleCount { get; private set; }
        /// <summary>WP-021C: aggro growls (anim.rise_end) and UI event cues actually played.</summary>
        public int AggroCount { get; private set; }
        public int UiCount { get; private set; }

        /// <summary>WP-021B: foot → surface lookup (down ray from the character root). PROTOTYPE-ONLY / INVENTED.</summary>
        private const float FootRayUp = 0.4f;
        private const float FootRayDown = 1.2f;
        private readonly Dictionary<CharacterAnimationEvents, ZombieEnemy> _animOwner = new Dictionary<CharacterAnimationEvents, ZombieEnemy>(48);
        private readonly RaycastHit[] _footHits = new RaycastHit[4];

        /// <summary>Where the listener is, for the idle range. Null = origin.</summary>
        public void SetListener(System.Func<Vector3> position)
        {
            _listenerPosition = position;
        }

        public bool Subscribed { get; private set; }

        public AudioEventRouter(AudioPlayFunc play)
        {
            _play = play;
        }

        public void Subscribe()
        {
            if (Subscribed)
            {
                return;
            }

            DamageEvents.EnemyDamaged += OnEnemyDamaged;
            DamageEvents.EnemyDied += OnEnemyDied;
            DamageEvents.PlayerAttacked += OnPlayerAttacked;
            DamageEvents.ArmorBroken += OnArmorBroken;
            Props.BreakableProp.Broke += OnPropBroke;
            CharacterAnimationEvents.Any += OnAnimationEvent;
            DD.Prototype.UI.UiEvents.Raised += OnUiEvent;
            Subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!Subscribed)
            {
                return;
            }

            DamageEvents.EnemyDamaged -= OnEnemyDamaged;
            DamageEvents.EnemyDied -= OnEnemyDied;
            DamageEvents.PlayerAttacked -= OnPlayerAttacked;
            DamageEvents.ArmorBroken -= OnArmorBroken;
            Props.BreakableProp.Broke -= OnPropBroke;
            CharacterAnimationEvents.Any -= OnAnimationEvent;
            DD.Prototype.UI.UiEvents.Raised -= OnUiEvent;
            Subscribed = false;
        }

        public void Clear()
        {
            _tracked.Clear();
            _drops.Clear();
            _animOwner.Clear();
        }

        // ------------------------------------------------------------------ WP-021B · ADR-018-1 animation events

        /// <summary>
        /// The WP-018 contract (foot_l / foot_r / breath / death_end) — the reserved rows of
        /// the event map. Footsteps take the surface under the character (down ray →
        /// <see cref="AudioSurfaceHints"/> → <see cref="SurfaceResolver"/>; since WP-021C the AP-005
        /// instances carry SurfaceTags from their placement family and the courtyard ground is
        /// refined by the stone routes; Default still reads as stone). A zombie's breath uses the idle clip and re-arms the polled idle
        /// timer so the two paths never double; hit_window/fire/pickup are already covered by
        /// the polled windup edge and the weapon/Special cues.
        /// </summary>
        private void OnAnimationEvent(CharacterAnimationEvents source, CharacterAnimationEvent e)
        {
            if (source == null)
            {
                return;
            }

            switch (e)
            {
                case CharacterAnimationEvent.FootLeft:
                case CharacterAnimationEvent.FootRight:
                    OnFootstep(source);
                    break;

                case CharacterAnimationEvent.Breath:
                {
                    ZombieEnemy z = OwnerOf(source);
                    if (z != null && z.IsAlive)
                    {
                        _tracked.TryGetValue(z, out Tracked t);
                        t.NextIdle = Time.unscaledTime + _rng.Range(IdleMinSeconds, IdleMaxSeconds);
                        _tracked[z] = t;
                        Vector3 at = z.transform.position + Vector3.up * 1.4f;
                        if (_play(AudioMixPlan.ZombieIdle(AudioEventMap.ZombieIdle(z.Tier), WeightOf(z.Tier)), at, 1f, 10))
                        {
                            IdleCount++;
                        }
                    }

                    break;
                }

                case CharacterAnimationEvent.DeathEnd:
                {
                    ZombieEnemy z = OwnerOf(source);
                    EnemyTierWeight w = z != null ? WeightOf(z.Tier) : EnemyTierWeight.Light;
                    if (_play(AudioMixPlan.BodySettle(w), source.transform.position, 1f, 25))
                    {
                        SettleCount++;
                    }

                    break;
                }

                case CharacterAnimationEvent.RiseEnd:
                {
                    // WP-021C: the earth was ZombieRise (first live frame); this is the voice - up, and looking at you.
                    ZombieEnemy z = OwnerOf(source);
                    if (z != null && z.IsAlive)
                    {
                        Vector3 at = z.transform.position + Vector3.up * 1.4f;
                        if (_play(AudioMixPlan.ZombieAggro(AudioEventMap.ZombieAggro(z.Tier), WeightOf(z.Tier)), at, 1f, 55))
                        {
                            AggroCount++;
                        }
                    }

                    break;
                }
            }
        }

        // ------------------------------------------------------------------ WP-021C · WP-022B UI events

        /// <summary>
        /// The bound rows of the map's UI section. Everything not listed here is a reserved row:
        /// the same frame already plays that sound through its owner (UiSound → PrototypeAudio,
        /// the Run / Combat / Player sources) and a second voice would only blur it.
        /// </summary>
        private void OnUiEvent(DD.Prototype.UI.UiEvent e, string detail)
        {
            AudioSoundId sound = UiSoundFor(e);
            if (sound == AudioSoundId.None)
            {
                return;
            }

            if (_play(AudioMixPlan.UiEvent(sound), Vector3.zero, 1f, 45))
            {
                UiCount++;
            }
        }

        /// <summary>UiEvent → sound for the bound rows; None for the reserved ones. Mirrors the map (the EditMode test keeps the two equal).</summary>
        public static AudioSoundId UiSoundFor(DD.Prototype.UI.UiEvent e)
        {
            switch (e)
            {
                case DD.Prototype.UI.UiEvent.ButtonHover: return AudioSoundId.UiHover;
                case DD.Prototype.UI.UiEvent.ScreenEntered: return AudioSoundId.UiTransition;
                case DD.Prototype.UI.UiEvent.PaneOpened: return AudioSoundId.UiPaneOpen;
                case DD.Prototype.UI.UiEvent.ChoiceSelected: return AudioSoundId.UiSelect;
                case DD.Prototype.UI.UiEvent.SliderChanged: return AudioSoundId.UiSliderTick;
                case DD.Prototype.UI.UiEvent.ToggleChanged: return AudioSoundId.UiToggle;
                case DD.Prototype.UI.UiEvent.DialogOpened: return AudioSoundId.UiDialogOpen;
                case DD.Prototype.UI.UiEvent.HudLowHealthEntered: return AudioSoundId.HudLowHealth;
                case DD.Prototype.UI.UiEvent.HudMagazineEmpty: return AudioSoundId.HudMagazineEmpty;
                case DD.Prototype.UI.UiEvent.HudThreatEncircled: return AudioSoundId.HudThreatEncircled;
                case DD.Prototype.UI.UiEvent.HudThreatRearAttack: return AudioSoundId.HudThreatRear;
                case DD.Prototype.UI.UiEvent.HudThreatStrongVersion: return AudioSoundId.HudThreatStrong;
                case DD.Prototype.UI.UiEvent.UpgradeRerolled: return AudioSoundId.UiReroll;
                case DD.Prototype.UI.UiEvent.UpgradeOfferLocked: return AudioSoundId.UiLock;
                case DD.Prototype.UI.UiEvent.LevelUp: return AudioSoundId.UiLevelUp;
                case DD.Prototype.UI.UiEvent.UnlockRevealed: return AudioSoundId.UiUnlock;
                case DD.Prototype.UI.UiEvent.AchievementRevealed: return AudioSoundId.UiAchievement;
                case DD.Prototype.UI.UiEvent.NewBestRound: return AudioSoundId.UiNewBest;
                default: return AudioSoundId.None;
            }
        }

        private ZombieEnemy OwnerOf(CharacterAnimationEvents source)
        {
            if (!_animOwner.TryGetValue(source, out ZombieEnemy z))
            {
                z = source.GetComponentInParent<ZombieEnemy>();
                _animOwner[source] = z;
            }

            return z;
        }

        private void OnFootstep(CharacterAnimationEvents source)
        {
            Vector3 root = source.transform.position;
            SurfaceKind surface = SurfaceUnder(root);
            ZombieEnemy z = OwnerOf(source);

            if (z != null)
            {
                if (!z.IsAlive)
                {
                    return;
                }

                EnemyTierWeight w = WeightOf(z.Tier);
                AudioSoundId sound = w == EnemyTierWeight.Heavy ? AudioSoundId.ZombieStepHeavy : AudioSoundId.ZombieStepLight;
                if (_play(AudioMixPlan.ZombieFootstep(sound, w), root, 1f, 10))
                {
                    FootstepCount++;
                }

                return;
            }

            PlayerRig rig = PlayerRegistry.Primary;
            bool player = rig != null && source.transform.IsChildOf(rig.transform);
            if (!player)
            {
                return;
            }

            bool running = rig.Motor != null && rig.Motor.Speed01 > 0.7f;
            if (_play(AudioMixPlan.PlayerFootstep(FootstepFor(surface), running), root, 1f, 20))
            {
                FootstepCount++;
            }
        }

        private SurfaceKind SurfaceUnder(Vector3 root)
        {
            int mask = (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            int n = Physics.RaycastNonAlloc(root + Vector3.up * FootRayUp, Vector3.down, _footHits, FootRayDown, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; SurfaceKind kind = SurfaceKind.Default;
            for (int i = 0; i < n; i++)
            {
                if (_footHits[i].distance < best)
                {
                    best = _footHits[i].distance;
                    kind = AudioSurfaceHints.Resolve(_footHits[i].collider, _footHits[i].point);   // WP-021C: tag + route refinement
                }
            }

            return kind;
        }

        /// <summary>Surface → player footstep clip. Untagged ground is the courtyard's paving (stone).</summary>
        public static AudioSoundId FootstepFor(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Wood: return AudioSoundId.FootstepWood;
                case SurfaceKind.Dirt: return AudioSoundId.FootstepDirt;
                case SurfaceKind.Foliage: return AudioSoundId.FootstepFoliage;
                case SurfaceKind.Water: return AudioSoundId.FootstepWater;
                default: return AudioSoundId.FootstepStone;
            }
        }

        // ------------------------------------------------------------------ v003 rows

        private void OnPlayerAttacked(EnemyAttackLandedInfo info)
        {
            if (_play(AudioMixPlan.AttackImpactBody(), info.Point, 1f, 90))
            {
                ImpactCount++;
            }
        }

        private void OnArmorBroken(ArmorBreakInfo info)
        {
            // The break itself is the tier cue (WP-006 N1); this is only the plate landing.
            _drops.Add(new PendingDrop { At = Time.unscaledTime + PieceDropDelay, Point = info.Point });
        }

        private void OnPropBroke(Props.BreakableProp prop, Vector3 point, Vector3 direction)
        {
            if (_play(AudioMixPlan.BreakCeramic(), point, 1f, 60))
            {
                BreakCount++;
            }
        }

        // ------------------------------------------------------------------ events

        private void OnEnemyDamaged(EnemyDamageInfo info)
        {
            if (info.Result.Killed || info.Result.HealthDamage <= 0f && info.Result.ArmorDamage <= 0f)
            {
                return; // the death vocal owns a killing blow; an empty result is nothing
            }

            ZombieEnemy z = info.Owner != null ? info.Owner.GetComponent<ZombieEnemy>() : null;
            float now = Time.unscaledTime;
            if (z != null)
            {
                if (_tracked.TryGetValue(z, out Tracked t) && now - t.LastHit < HitThrottleSeconds)
                {
                    HitThrottled++;
                    return;
                }

                t.LastHit = now;
                _tracked[z] = t;
            }

            EnemyTierWeight weight = WeightOf(info.Tier);
            if (_play(AudioMixPlan.ZombieHit(AudioEventMap.ZombieHit(info.Tier), weight), info.Point, 1f, 30))
            {
                HitCount++;
            }

            if (info.CausedStagger && _play(AudioMixPlan.ZombieStagger(weight), info.Point, 1f, 45))
            {
                StaggerCount++;
            }
        }

        private void OnEnemyDied(EnemyDeathInfo info)
        {
            EnemyTierWeight weight = WeightOf(info.Tier);
            if (_play(AudioMixPlan.ZombieDeath(AudioEventMap.ZombieDeath(info.Tier), weight), info.Position, 1f, 70))
            {
                DeathCount++;
            }

            ZombieEnemy z = info.Owner != null ? info.Owner.GetComponent<ZombieEnemy>() : null;
            if (z != null)
            {
                _tracked.Remove(z);
            }
        }

        // ------------------------------------------------------------------ polling

        /// <summary>Once per frame from the director. Detects attack-phase edges on the live list.</summary>
        public void Tick()
        {
            float now = Time.unscaledTime;
            Vector3 listener = _listenerPosition != null ? _listenerPosition() : Vector3.zero;

            // Plates landing after their break.
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                if (_drops[i].At <= now)
                {
                    if (_play(AudioMixPlan.ArmorPieceDrop(), _drops[i].Point, 1f, 20))
                    {
                        PieceDropCount++;
                    }

                    _drops.RemoveAt(i);
                }
            }

            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                ZombieEnemy z = live[i];
                if (z == null || !z.IsAlive)
                {
                    continue;
                }

                _tracked.TryGetValue(z, out Tracked t);

                if (!t.Seen)
                {
                    // First frame alive in this life: the rise from the ground.
                    t.Seen = true;
                    t.NextIdle = now + _rng.Range(IdleMinSeconds, IdleMaxSeconds);
                    EnemyTierWeight w = WeightOf(z.Tier);
                    if (_play(AudioMixPlan.ZombieRise(w), z.transform.position, 1f, 55))
                    {
                        RiseCount++;
                    }
                }
                else if (now >= t.NextIdle)
                {
                    t.NextIdle = now + _rng.Range(IdleMinSeconds, IdleMaxSeconds);
                    Vector3 at = z.transform.position + Vector3.up * 1.4f;
                    if (z.AttackPhase == ZombieAttackPhase.None && (at - listener).sqrMagnitude <= IdleRange * IdleRange)
                    {
                        if (_play(AudioMixPlan.ZombieIdle(AudioEventMap.ZombieIdle(z.Tier), WeightOf(z.Tier)), at, 1f, 10))
                        {
                            IdleCount++;
                        }
                    }
                }

                ZombieAttackPhase phase = z.AttackPhase;
                if (phase == t.Phase)
                {
                    _tracked[z] = t;
                    continue;
                }

                if (phase == ZombieAttackPhase.Windup && t.Phase != ZombieAttackPhase.Windup)
                {
                    Vector3 at = z.transform.position + Vector3.up * 1.4f;
                    if (_play(AudioMixPlan.ZombieWindup(AudioEventMap.ZombieWindup(z.Tier), WeightOf(z.Tier)), at, 1f, 60))
                    {
                        WindupCount++;
                    }
                }
                else if (phase == ZombieAttackPhase.Active && t.Phase == ZombieAttackPhase.Windup)
                {
                    Vector3 at = z.transform.position + Vector3.up * 1.4f;
                    if (_play(AudioMixPlan.ZombieAttack(AudioEventMap.ZombieAttack(z.Tier), WeightOf(z.Tier)), at, 1f, 50))
                    {
                        AttackCount++;
                    }
                }

                t.Phase = phase;
                _tracked[z] = t;
            }

            // Forget pooled-out bodies a few at a time so the dictionary never grows past
            // the pool and the sweep never costs a frame.
            if (_tracked.Count > live.Count && (++_sweep & 63) == 0)
            {
                _stale.Clear();
                foreach (KeyValuePair<ZombieEnemy, Tracked> pair in _tracked)
                {
                    if (pair.Key == null || !pair.Key.IsAlive)
                    {
                        _stale.Add(pair.Key);
                    }
                }

                for (int i = 0; i < _stale.Count; i++)
                {
                    _tracked.Remove(_stale[i]);
                }
            }
        }

        public static EnemyTierWeight WeightOf(EnemyTier tier)
        {
            return tier == EnemyTier.Elite || tier == EnemyTier.Ultra ? EnemyTierWeight.Heavy : EnemyTierWeight.Light;
        }

        public string Describe()
        {
            return string.Format("windup {0} · attack {1} · hit {2} (throttled {3}) · death {4} · rise {5} · idle {6} · stagger {7} · claw {8} · plate {9} · ceramic {10} · steps {11} · settle {12} · aggro {14} · ui {15} · tracked {13}",
                WindupCount, AttackCount, HitCount, HitThrottled, DeathCount, RiseCount, IdleCount, StaggerCount, ImpactCount, PieceDropCount, BreakCount, FootstepCount, SettleCount, _tracked.Count, AggroCount, UiCount);
        }
    }
}
