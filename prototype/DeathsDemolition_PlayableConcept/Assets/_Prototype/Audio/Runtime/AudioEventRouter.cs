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
    ///   telegraph_start → the telegraph growl (WP-040 beat since WP-048; was an AttackPhase
    ///                   poll). The telegraph is 0.55 s (EnemyTierSettings), so the growl lands
    ///                   ≥ 0.3 s before the swing by construction; positional (REQ-104).
    ///   attack_commit / attack_hit / stagger / armor_break_&lt;region&gt; / death_&lt;tier&gt; /
    ///   spawn_rise / spawn_land → their voices, see <see cref="OnCombatBeat"/>.
    ///
    /// The live list is still walked once a frame, only for the idle breath clock.
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
        /// <summary>WP-048: region armour-break voices (WP-040 armor_break_&lt;region&gt;).</summary>
        public int RegionBreakCount { get; private set; }
        /// <summary>WP-048B: weapon-handling foley + pickup cues (WP-032C clip events).</summary>
        public int FoleyCount { get; private set; }

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

        // WP-058: the director's field-loop seam; stop carries the field origin so concurrent Tide wells release the matching source.
        private Func<AudioSoundId, Vector3, int, bool> _fieldStart;
        private Action<int, Vector3> _fieldStop;
        private readonly float[] _swHitLast = new float[3];
        /// <summary>Special usage cues played (fire / field / hit). Test readout.</summary>
        public int SpecialUsageCount { get; private set; }

        public void SetFieldLoopHandlers(Func<AudioSoundId, Vector3, int, bool> start, Action<int, Vector3> stop)
        {
            _fieldStart = start;
            _fieldStop = stop;
        }

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

            DamageEvents.EnemyDamaged += OnEnemyDamaged;       // pain grunt: the one moment the beat layer has no row for
            DamageEvents.EnemyDied += OnEnemyDied;             // bookkeeping only since WP-048 (the death vocal is the death_<tier> beat)
            EnemyCombatEvents.Raised += OnCombatBeat;          // WP-048: WP-040 beat surface (ADR-040-1)
            Props.BreakableProp.Broke += OnPropBroke;
            CharacterAnimationEvents.Any += OnAnimationEvent;
            DD.Prototype.UI.UiEvents.Raised += OnUiEvent;
            DD.Prototype.Weapons.SpecialWeaponUsageEvents.Usage += OnSpecialUsage;   // WP-058 (WP-056 ADR-056-1)
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
            EnemyCombatEvents.Raised -= OnCombatBeat;
            Props.BreakableProp.Broke -= OnPropBroke;
            CharacterAnimationEvents.Any -= OnAnimationEvent;
            DD.Prototype.UI.UiEvents.Raised -= OnUiEvent;
            DD.Prototype.Weapons.SpecialWeaponUsageEvents.Usage -= OnSpecialUsage;
            Subscribed = false;
        }

        public void Clear()
        {
            _tracked.Clear();
            _drops.Clear();
            _animOwner.Clear();
            s_breakSounds.Clear();
            _pullLast.Clear();
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

                case CharacterAnimationEvent.ShiftLeft:
                case CharacterAnimationEvent.ShiftRight:
                    OnFootstep(source, 0.5f);   // WP-032B idle weight shift: half a step
                    break;

                case CharacterAnimationEvent.Eject:
                case CharacterAnimationEvent.Chamber:
                case CharacterAnimationEvent.MagOut:
                case CharacterAnimationEvent.MagIn:
                case CharacterAnimationEvent.MagIn2:
                case CharacterAnimationEvent.MagIn3:
                case CharacterAnimationEvent.SwitchReady:
                case CharacterAnimationEvent.SwitchAway:
                case CharacterAnimationEvent.Lowered:
                case CharacterAnimationEvent.Raised:
                case CharacterAnimationEvent.PickupGrab:
                case CharacterAnimationEvent.PickupDone:
                    OnWeaponHandling(source, e);   // WP-048B (WP-032C)
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

                // RiseEnd: since WP-048 the aggro growl rides the spawn_land beat (WP-040), which also
                // covers bodies that spawn without a rise clip; the clip event alone would miss those.
            }
        }

        // ------------------------------------------------------------------ WP-048 · WP-040 EnemyCombatEvents (ADR-040-1)

        /// <summary>
        /// The beat layer. One subscription replaces the WP-021 attack-phase polling (telegraph /
        /// commit edges), the PlayerAttacked claw, the CausedStagger stagger, the ArmorBroken plate
        /// and the EnemyDied vocal: every zombie moment now has one owner and the same frame.
        /// </summary>
        private void OnCombatBeat(EnemyCombatEventInfo info)
        {
            EnemyTierWeight w = WeightOf(info.Tier);
            Vector3 head = info.Body != null ? info.Body.position + Vector3.up * 1.4f : info.Position;
            switch (info.Kind)
            {
                case EnemyCombatEventKind.TelegraphStart:
                    if (_play(AudioMixPlan.ZombieWindup(AudioEventMap.ZombieWindup(info.Tier), w), head, 1f, 60)) WindupCount++;
                    break;
                case EnemyCombatEventKind.AttackCommit:
                    if (_play(AudioMixPlan.ZombieAttack(AudioEventMap.ZombieAttack(info.Tier), w), head, 1f, 50)) AttackCount++;
                    break;
                case EnemyCombatEventKind.AttackHit:
                    if (_play(AudioMixPlan.AttackImpactBody(), info.Position, 1f, 90)) ImpactCount++;
                    break;
                case EnemyCombatEventKind.AttackMiss:
                    break;   // the swing already sounded at commit; a whiff is the silence after it (matrix row: covered)
                case EnemyCombatEventKind.Stagger:
                    if (_play(AudioMixPlan.ZombieStagger(w), info.Position, 1f, 45)) StaggerCount++;
                    break;
                case EnemyCombatEventKind.ArmorBreak:
                    // The break itself is the tier cue (ReportEnemyHit, WP-006 N1); this is the REGION voice (v007) + the plate landing.
                    if (_play(AudioMixPlan.ArmorBreakRegion(AudioEventMap.ArmorBreakRegion(info.Region), w), info.Position, 1f, 70)) RegionBreakCount++;
                    _drops.Add(new PendingDrop { At = Time.unscaledTime + PieceDropDelay, Point = info.Position });
                    break;
                case EnemyCombatEventKind.Death:
                    if (_play(AudioMixPlan.ZombieDeath(AudioEventMap.ZombieDeath(info.Tier), w), info.Position, 1f, 70)) DeathCount++;
                    break;
                case EnemyCombatEventKind.SpawnRise:
                    if (_play(AudioMixPlan.ZombieRise(w), info.Position, 1f, 55)) RiseCount++;
                    break;
                case EnemyCombatEventKind.SpawnLand:
                    if (_play(AudioMixPlan.ZombieAggro(AudioEventMap.ZombieAggro(info.Tier), w), head, 1f, 55)) AggroCount++;
                    break;
                // WP-058C · WP-055 Special reactions (the body's side of the Kagura push / Ofuda web / Tide pull)
                case EnemyCombatEventKind.SpecialPush:
                    if (_play(AudioMixPlan.SpecialReaction(AudioSoundId.ZombieReactPush, w), head, 1f, 44)) ReactionCount++;
                    break;
                case EnemyCombatEventKind.SpecialWeb:
                    if (_play(AudioMixPlan.SpecialReaction(AudioSoundId.ZombieReactWeb, w), head, 1f, 44)) ReactionCount++;
                    break;
                case EnemyCombatEventKind.SpecialPull:
                {
                    // Pull ticks repeat every few frames per body: one voice per body per 0.25 s.
                    float now = Time.unscaledTime;
                    if (info.Body != null)
                    {
                        int id = info.Body.GetInstanceID();
                        if (_pullLast.TryGetValue(id, out float last) && now - last < 0.25f) break;
                        _pullLast[id] = now;
                    }

                    if (_play(AudioMixPlan.SpecialReaction(AudioSoundId.ZombieReactPull, w), head, 0.8f, 30)) ReactionCount++;
                    break;
                }
            }
        }

        private readonly Dictionary<int, float> _pullLast = new Dictionary<int, float>(48);
        /// <summary>WP-058C: Special reaction voices (sw_push / sw_web / sw_pull) played.</summary>
        public int ReactionCount { get; private set; }

        // ------------------------------------------------------------------ WP-058 · WP-056 SpecialUsageEvent

        /// <summary>
        /// Per-Special usage voices over the shared lifecycle cues: Fired adds the weapon's own
        /// character to HeavyWeaponFire, FieldSpawned starts the field (Kagura one-shot front,
        /// Ofuda / Tide 3D loops), FieldEnded stops the loop and plays the end, Hit is the per-body
        /// touch (gain × Falloff01, throttled 60 ms per Special so a wave over eight bodies is a
        /// chord, not a machine gun). Activated / Deactivated are owned by the spin-up / expired cues.
        /// </summary>
        private void OnSpecialUsage(DD.Prototype.Weapons.SpecialUsageEvent e)
        {
            int slot = Mathf.Clamp((int)e.Id, 0, 2);
            AudioSoundId sound = AudioEventMap.SpecialUsage(e.Id, e.Kind);
            switch (e.Kind)
            {
                case DD.Prototype.Weapons.SpecialUsageKind.Fired:
                    if (_play(AudioMixPlan.SpecialFire(sound), e.Origin, 1f, 55)) SpecialUsageCount++;
                    break;
                case DD.Prototype.Weapons.SpecialUsageKind.FieldSpawned:
                    if (e.Id == DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell)
                    {
                        if (_play(AudioMixPlan.SpecialField(sound), e.Origin, 1f, 65)) SpecialUsageCount++;
                    }
                    else
                    {
                        if (_fieldStart != null && _fieldStart(sound, e.Origin, slot)) SpecialUsageCount++;
                    }
                    break;
                case DD.Prototype.Weapons.SpecialUsageKind.FieldEnded:
                    _fieldStop?.Invoke(slot, e.Origin);
                    if (_play(AudioMixPlan.SpecialField(sound), e.Origin, 1f, 60)) SpecialUsageCount++;
                    break;
                case DD.Prototype.Weapons.SpecialUsageKind.Hit:
                {
                    float now = Time.unscaledTime;
                    if (now - _swHitLast[slot] < 0.06f) return;
                    _swHitLast[slot] = now;
                    Vector3 at = e.Origin + Vector3.up * 0.6f;   // PublishHit: Origin = the hit point on the body
                    if (_play(AudioMixPlan.SpecialHit(sound), at, Mathf.Clamp(e.Falloff01, 0.35f, 1f), 40)) SpecialUsageCount++;
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

        private void OnFootstep(CharacterAnimationEvents source, float gain = 1f)
        {
            Vector3 root = source.transform.position;
            SurfaceKind surface = SurfaceUnder(root, out Collider under, out Vector3 underPoint);
            ZombieEnemy z = OwnerOf(source);

            if (z != null)
            {
                if (!z.IsAlive)
                {
                    return;
                }

                EnemyTierWeight w = WeightOf(z.Tier);
                AudioSoundId sound = w == EnemyTierWeight.Heavy ? AudioSoundId.ZombieStepHeavy : AudioSoundId.ZombieStepLight;
                if (_play(AudioMixPlan.ZombieFootstep(sound, w), root, gain, 10))
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
            AudioSoundId step = AudioSurfaceHints.Footstep(under, underPoint, surface, FootstepFor);   // WP-058B: plate variants (gravel / pavement / shallow water)
            if (_play(AudioMixPlan.PlayerFootstep(step, running), root, gain, 20))
            {
                FootstepCount++;
            }
        }

        // ------------------------------------------------------------------ WP-048B · WP-032C weapon handling (player rig)

        /// <summary>
        /// The AP-009C weapon-handling clip events on the PLAYER rig → the equipped roster weapon's
        /// own foley (A Rifle / B Carbine / C Shotgun, v007). Events on a zombie rig (none today)
        /// are ignored; the pickup pair is weapon-independent.
        /// </summary>
        private void OnWeaponHandling(CharacterAnimationEvents source, CharacterAnimationEvent e)
        {
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null || !source.transform.IsChildOf(rig.transform))
            {
                return;
            }

            AudioSoundId sound;
            AudioPlacement placement;
            if (e == CharacterAnimationEvent.PickupGrab || e == CharacterAnimationEvent.PickupDone)
            {
                sound = e == CharacterAnimationEvent.PickupGrab ? AudioSoundId.PickupGrab : AudioSoundId.PickupDone;
                placement = AudioMixPlan.Pickup(sound);
            }
            else
            {
                sound = AudioEventMap.WeaponFoley(DD.Prototype.Weapons.WeaponRegistry.SelectedWeapon, e);
                if (sound == AudioSoundId.None)
                {
                    return;
                }

                placement = AudioMixPlan.WeaponFoley(sound);
            }

            if (_play(placement, rig.transform.position, 1f, 30))
            {
                FoleyCount++;
            }
        }

        private SurfaceKind SurfaceUnder(Vector3 root, out Collider under, out Vector3 point)
        {
            int mask = (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            int n = Physics.RaycastNonAlloc(root + Vector3.up * FootRayUp, Vector3.down, _footHits, FootRayDown, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; SurfaceKind kind = SurfaceKind.Default; under = null; point = root;
            for (int i = 0; i < n; i++)
            {
                if (_footHits[i].distance < best)
                {
                    best = _footHits[i].distance;
                    under = _footHits[i].collider;
                    point = _footHits[i].point;
                    kind = AudioSurfaceHints.Resolve(under, point);   // WP-021C: tag + route refinement
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

        private void OnPropBroke(Props.BreakableProp prop, Vector3 point, Vector3 direction)
        {
            if (_play(AudioMixPlan.Breakable(BreakSoundFor(prop)), point, 1f, 60))
            {
                BreakCount++;
            }
        }

        /// <summary>
        /// WP-048 · AP-017 breakable families → their own voice (AP017-breakable-event-table-v001: BreakStone
        /// lantern_stone, BreakCeramic planter, BreakWood bench/sign, BreakBamboo fence_panel, BreakPaper
        /// lantern_paper/shoji, BreakStraw strawbale). The family is read from the prop's root name once per
        /// prop (a break is a rare event; the lookup is cached per instance) - the AP-003B small_breakable
        /// and anything unknown stay on the ceramic voice.
        /// </summary>
        public static AudioSoundId BreakSoundFor(Props.BreakableProp prop)
        {
            if (prop == null)
            {
                return AudioSoundId.BreakCeramic;
            }

            if (s_breakSounds.TryGetValue(prop, out AudioSoundId cached))
            {
                return cached;
            }

            AudioSoundId sound = BreakSoundForEvent(prop.EventId, prop.gameObject.name);
            s_breakSounds[prop] = sound;
            return sound;
        }

        /// <summary>
        /// WP-060 (WP-038B on main): the break event id is DATA on the prop (<c>BreakableProp.EventId</c>,
        /// six AP-017 events written by the prefab builder). The event picks the voice; the root name only
        /// refines inside that event (BreakWood → bench / sign, BreakPaper → lantern_paper / shoji, BreakStone
        /// → lantern_stone, BreakCeramic → planter vs the AP-003B ceramic). An unknown event id falls back to
        /// the name rule, then ceramic. Public for the EditMode table test.
        /// </summary>
        public static AudioSoundId BreakSoundForEvent(string eventId, string name)
        {
            AudioSoundId byName = BreakSoundForName(name);
            switch (eventId)
            {
                case "BreakStone": return AudioSoundId.BreakLanternStone;
                case "BreakBamboo": return AudioSoundId.BreakFence;
                case "BreakStraw": return AudioSoundId.BreakBale;
                case "BreakWood": return byName == AudioSoundId.BreakSign ? AudioSoundId.BreakSign : AudioSoundId.BreakBench;
                case "BreakPaper": return byName == AudioSoundId.BreakShoji ? AudioSoundId.BreakShoji : AudioSoundId.BreakLanternPaper;
                case "BreakCeramic": return byName == AudioSoundId.BreakPlanter ? AudioSoundId.BreakPlanter : AudioSoundId.BreakCeramic;
                default: return byName;
            }
        }

        private static readonly Dictionary<Props.BreakableProp, AudioSoundId> s_breakSounds = new Dictionary<Props.BreakableProp, AudioSoundId>(64);

        /// <summary>Family name (prefab / instance root) → break voice. Public for the EditMode table test.</summary>
        public static AudioSoundId BreakSoundForName(string name)
        {
            if (string.IsNullOrEmpty(name)) return AudioSoundId.BreakCeramic;
            if (name.IndexOf("lantern_stone", StringComparison.OrdinalIgnoreCase) >= 0) return AudioSoundId.BreakLanternStone;
            if (name.IndexOf("lantern_paper", StringComparison.OrdinalIgnoreCase) >= 0) return AudioSoundId.BreakLanternPaper;
            if (name.IndexOf("planter", StringComparison.OrdinalIgnoreCase) >= 0) return AudioSoundId.BreakPlanter;
            if (name.IndexOf("fence", StringComparison.OrdinalIgnoreCase) >= 0) return AudioSoundId.BreakFence;
            if (name.IndexOf("strawbale", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("bale", StringComparison.OrdinalIgnoreCase) >= 0) return AudioSoundId.BreakBale;
            if (name.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0) return AudioSoundId.BreakBench;
            if (name.IndexOf("sign", StringComparison.OrdinalIgnoreCase) >= 0) return AudioSoundId.BreakSign;
            if (name.IndexOf("shoji", StringComparison.OrdinalIgnoreCase) >= 0) return AudioSoundId.BreakShoji;
            return AudioSoundId.BreakCeramic;
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

            // CausedStagger: the stagger vocal is the WP-040 `stagger` beat since WP-048 (same frame, one owner).
        }

        private void OnEnemyDied(EnemyDeathInfo info)
        {
            // The death vocal is the `death_<tier>` beat (WP-048); this only forgets the body.
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
                    // First frame alive in this life: arm the idle clock. The spawn_rise / spawn_land
                    // beats (WP-040) own the spawn sounds since WP-048.
                    t.Seen = true;
                    t.NextIdle = now + _rng.Range(IdleMinSeconds, IdleMaxSeconds);
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

                // Attack-phase edges are the telegraph_start / attack_commit beats since WP-048; the
                // phase is only tracked for the idle gate.
                t.Phase = z.AttackPhase;
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
            return string.Format("windup {0} · attack {1} · hit {2} (throttled {3}) · death {4} · rise {5} · idle {6} · stagger {7} · claw {8} · plate {9} · ceramic {10} · steps {11} · settle {12} · aggro {14} · ui {15} · regionBreak {16} · foley {17} · special {18} · react {19} · tracked {13}",
                WindupCount, AttackCount, HitCount, HitThrottled, DeathCount, RiseCount, IdleCount, StaggerCount, ImpactCount, PieceDropCount, BreakCount, FootstepCount, SettleCount, _tracked.Count, AggroCount, UiCount, RegionBreakCount, FoleyCount, SpecialUsageCount, ReactionCount);
        }
    }
}
