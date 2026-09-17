using UnityEngine;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Enemies
{
    /// <summary>Locomotion pose the animator is currently playing.</summary>
    public enum ZombiePose
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Limp = 3
    }

    /// <summary>Per-tier attack shape. Every one has a readable windup / active / recovery.</summary>
    public enum ZombieAttackStyle
    {
        /// <summary>Standard: one-armed overhand swipe.</summary>
        Swipe = 0,

        /// <summary>Runner: crouch, then a committed forward lunge.</summary>
        Lunge = 1,

        /// <summary>Guard: two-armed horizontal sweep with a long wind-up.</summary>
        Sweep = 2,

        /// <summary>Executioner: both arms overhead, then a heavy smash.</summary>
        Overhead = 3
    }

    public enum ZombieAttackPhase
    {
        None = 0,
        Windup = 1,
        Active = 2,
        Recovery = 3
    }

    /// <summary>Multiple death reactions, chosen from hit direction and severity.</summary>
    public enum ZombieDeathStyle
    {
        FallForward = 0,
        FallBackward = 1,
        Crumple = 2,
        SpinFall = 3,
        HeadshotDrop = 4
    }

    /// <summary>
    /// Procedural pseudo-animation for one zombie - the whole "PSEUDO ANIMASYON" block.
    ///
    /// There is no Animator, no clip and no state machine asset. Every pose is a function
    /// of a gait phase and a handful of blend weights written straight onto local
    /// rotations. That buys three things the prototype actually needs:
    ///   * multiple walk variations for free (change amplitude, cadence, arm carriage)
    ///   * an additive flinch layer that does NOT stop locomotion (REQ-123)
    ///   * attack windup / active / recovery that is visually readable by construction,
    ///     because the pose is driven by the same normalised phase the hitbox is (DEC-029)
    ///
    /// Every constant here is PROTOTYPE-ONLY and invented. Degrees and metres.
    /// </summary>
    public sealed class ZombieAnimator
    {
        // ------------------------------------------------- gameplay-visible thresholds
        // Everything else in this file is pose authoring - the procedural equivalent of
        // keyframes. These few numbers are not: they are read by other systems.

        [PrototypeOnly(PendingDecisions.None,
            "Speed fraction below which the pose reads as Idle. Invented.", true)]
        private const float IdlePoseSpeedThreshold = 0.06f;

        [PrototypeOnly(PendingDecisions.None,
            "Speed fraction above which the pose reads as Run. NOT cosmetic: " +
            "EnemyHitReactionState scores a running enemy as EASIER to stagger (it has " +
            "momentum to lose, RunningStaggerBonus), so this line decides when that bonus applies.", true)]
        private const float RunPoseSpeedThreshold = 0.72f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds an ordinary death reaction takes. The body cannot despawn before it " +
            "finishes, so this plus CorpseLingerSeconds is how long a corpse occupies the map.", true)]
        private const float DeathDurationNormal = 1.05f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds an Elite / Ultra death takes. Longer on purpose so the moment lands " +
            "(REQ-125). Invented.", true)]
        private const float DeathDurationHeavy = 1.45f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds a clean head-shot drop takes. Shortest of the three.", true)]
        private const float DeathDurationHeadshot = 0.85f;

        private readonly ZombieRig _rig;

        // ------------------------------------------------------------------- gait state
        private float _phase;
        private float _speed01;
        private float _speedMps;

        private int _gaitVariant;
        private bool _limps;
        private float _limpSide;         // -1 left leg, +1 right leg
        private float _cadenceScale = 1f;
        private float _amplitudeScale = 1f;
        private float _armCarriage;      // 0 = hanging, 1 = raised forward
        private float _torsoTwistScale = 1f;
        private float _baseLean = 10f;
        private float _headTilt;

        // ------------------------------------------------------------- additive flinch
        private Vector3 _flinch;
        private Vector3 _flinchVelocity;
        private float _flinchRegionBias;  // -1 low, 0 torso, +1 head

        // ------------------------------------------------------------------- stagger
        private float _staggerTime;
        private float _staggerDuration;
        private Vector3 _staggerLocalDirection;
        private float _staggerIntensity;

        // -------------------------------------------------------------------- attack
        private ZombieAttackStyle _attackStyle;
        private ZombieAttackPhase _attackPhase;
        private float _attackT;

        // --------------------------------------------------------------------- death
        private bool _dying;
        private float _deathTime;
        private float _deathDuration = DeathDurationNormal;
        private ZombieDeathStyle _deathStyle;
        private float _deathSide;
        private EnemyTier _tier;   // WP-032C
        private float _deathSpin;

        public ZombiePose Pose { get; private set; } = ZombiePose.Idle;

        /// <summary>True once the death reaction has settled and the body can be despawned.</summary>
        public bool DeathFinished => _dying && (Driven
            ? Driver.DeathFinished || _deathTime >= DrivenDeathFallbackSeconds
            : _deathTime >= _deathDuration);

        /// <summary>0..1 progress through the death reaction. Feeds the sink-and-fade.</summary>
        public float DeathProgress => _dying ? Mathf.Clamp01(_deathTime / _deathDuration) : 0f;

        /// <summary>
        /// WP-016: a skinned visual's Animator owns the walk cycle (legs, hips bob) and the
        /// bridge carries the rest; the procedural arm SWING is switched off so the clip's
        /// swing is not doubled, while the carriage (arms forward) and everything else stays.
        /// </summary>
        public bool SkinnedLocomotion { get; set; }

        /// <summary>
        /// WP-018: the AP-009 skinned body. When set (and authored), every pose is the
        /// driver's — attack / flinch / stagger / death clips on the Animator — and this class
        /// keeps only the gameplay clocks (stagger window, death fallback) and the Pose /
        /// IsStaggering / DeathFinished contract ZombieEnemy reads. Null on the block rig.
        /// </summary>
        public IZombieAnimationDriver Driver { get; set; }

        /// <summary>True when the AP-009 driven body (WP-018) is authored on this rig — the clip events drive the windows.</summary>
        public bool Driven => Driver != null && Driver.Authored;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds after BeginDeath at which a driven body is declared finished even if the clip's " +
            "death_end never arrived (clip missing, Animator disabled). Longer than the longest AP-009 " +
            "death (1.433 s). Safety net, invented.", true)]
        private const float DrivenDeathFallbackSeconds = 2.0f;

        [PrototypeOnly(PendingDecisions.None,
            "Flinch intensity (DamageForce units) from which the driven body also plays the " +
            "upper-body hit_light L/R clip on top of the regional additive flinch. Below it only the " +
            "region twitches, so an SMG stream does not re-trigger the override every bullet. Invented.", true)]
        private const float DrivenUpperBodyFlinchIntensity = 0.9f;

        /// <summary>Tier attack telegraph the driven attack clip is rate-scaled to (set by SetAttackTiming).</summary>
        private float _telegraphSeconds;
        /// <summary>The zombie's version top speed (driven gait rate).</summary>
        private float _topSpeedMps = 1f;
        private ZombieAttackPhase _drivenPhase = ZombieAttackPhase.None;

        public ZombieAnimator(ZombieRig rig)
        {
            _rig = rig;
        }

        // ==================================================================== lifecycle

        /// <summary>
        /// Roll a fresh gait. Called from OnSpawned; this is where "multiple walk
        /// variations" (REQ-064) actually comes from.
        /// </summary>
        public void ResetForSpawn(EnemyTier tier, float baseLeanDegrees, ref EnemyRandom random)
        {
            _tier = tier;   // WP-032C: the v003 tier deaths / directional hit reads
            _phase = random.Range(0f, Mathf.PI * 2f);
            _speed01 = 0f;
            _speedMps = 0f;
            _drivenPhase = ZombieAttackPhase.None;

            _gaitVariant = random.Range(0, 4);
            _baseLean = baseLeanDegrees;

            // PROTOTYPE-ONLY gait table. Four readable silhouettes of "shambling".
            switch (_gaitVariant)
            {
                case 0: // dragging shuffle - short steps, low arms
                    _cadenceScale = 0.86f;
                    _amplitudeScale = 0.72f;
                    _armCarriage = 0.05f;
                    _torsoTwistScale = 0.6f;
                    break;
                case 1: // classic reach - long steps, arms half up
                    _cadenceScale = 1f;
                    _amplitudeScale = 1f;
                    _armCarriage = 0.65f;
                    _torsoTwistScale = 1f;
                    break;
                case 2: // lurching - big amplitude, slow cadence, heavy twist
                    _cadenceScale = 0.78f;
                    _amplitudeScale = 1.28f;
                    _armCarriage = 0.25f;
                    _torsoTwistScale = 1.35f;
                    break;
                default: // hurried scuttle - fast small steps
                    _cadenceScale = 1.24f;
                    _amplitudeScale = 0.82f;
                    _armCarriage = 0.4f;
                    _torsoTwistScale = 0.85f;
                    break;
            }

            // Limp is a walk variation, not a separate state. Elites and Ultras limp less
            // often: a limping Executioner reads as damaged rather than as a slow giant.
            float limpChance = tier == EnemyTier.Ultra ? 0.1f : tier == EnemyTier.Elite ? 0.18f : 0.3f;
            _limps = random.Chance(limpChance);
            _limpSide = random.Sign();
            _headTilt = random.Range(-14f, 14f);

            _flinch = Vector3.zero;
            _flinchVelocity = Vector3.zero;
            _flinchRegionBias = 0f;
            _staggerTime = 0f;
            _staggerDuration = 0f;
            _staggerIntensity = 0f;

            _attackPhase = ZombieAttackPhase.None;
            _attackT = 0f;

            _dying = false;
            _deathTime = 0f;
            _deathSpin = 0f;
            Pose = ZombiePose.Idle;
        }

        // ==================================================================== inputs

        /// <summary>Locomotion drive. speed01 is current speed over the tier's top speed.</summary>
        public void SetLocomotion(float speed01, float speedMetersPerSecond)
        {
            _speed01 = Mathf.Clamp01(speed01);
            _speedMps = Mathf.Max(0f, speedMetersPerSecond);

            if (_speed01 < IdlePoseSpeedThreshold)
            {
                Pose = ZombiePose.Idle;
            }
            else if (_speed01 > RunPoseSpeedThreshold)
            {
                Pose = ZombiePose.Run;
            }
            else
            {
                Pose = _limps ? ZombiePose.Limp : ZombiePose.Walk;
            }
        }

        public void SetAttack(ZombieAttackStyle style, ZombieAttackPhase phase, float normalisedTime)
        {
            _attackStyle = style;
            _attackPhase = phase;
            _attackT = Mathf.Clamp01(normalisedTime);

            if (Driven && phase != _drivenPhase)
            {
                if (phase == ZombieAttackPhase.Windup)
                {
                    Driver.PlayAttack(style, _telegraphSeconds);
                }

                _drivenPhase = phase;
            }
        }

        public void ClearAttack()
        {
            bool wasLive = _attackPhase == ZombieAttackPhase.Windup || _attackPhase == ZombieAttackPhase.Active;
            _attackPhase = ZombieAttackPhase.None;
            _attackT = 0f;
            if (Driven && wasLive)
            {
                Driver.CancelAttack();
            }

            _drivenPhase = ZombieAttackPhase.None;
        }

        /// <summary>
        /// Additive directional hit reaction - REQ-122, REQ-123.
        /// Never touches the gait phase, so a light bullet nudges the upper body while the
        /// legs keep walking. Intensity is in DamageForce units.
        /// </summary>
        public void AddFlinch(Vector3 worldDirection, float intensity, BodyRegion region, Transform reference)
        {
            if (_dying || intensity <= 0f)
            {
                return;
            }

            Vector3 local = reference != null
                ? reference.InverseTransformDirection(worldDirection)
                : worldDirection;

            local.y = 0f;
            if (local.sqrMagnitude > 0.0001f)
            {
                local.Normalize();
            }

            // Bullet travels along +local. The body is pushed the same way, so the torso
            // pitches away from the shooter and twists toward the bullet's lateral side.
            float magnitude = Mathf.Clamp(intensity, 0f, 3.5f);
            float pitch = local.z * magnitude * 16f;
            float yaw = local.x * magnitude * 11f;
            float roll = -local.x * magnitude * 7f;

            switch (region)
            {
                case BodyRegion.Head:
                    pitch *= 1.45f;
                    roll *= 1.6f;
                    _flinchRegionBias = 1f;
                    break;
                case BodyRegion.LowerBody:
                    pitch *= 0.55f;
                    yaw *= 0.5f;
                    _flinchRegionBias = -1f;
                    break;
                default:
                    _flinchRegionBias = 0f;
                    break;
            }

            _flinchVelocity += new Vector3(pitch, yaw, roll) * 12f;

            // Cap so a shotgun's eight pellets in one frame cannot spin the torso.
            _flinchVelocity = Vector3.ClampMagnitude(_flinchVelocity, 900f);

            if (Driven)
            {
                Driver.PlayFlinch(local, region, magnitude >= DrivenUpperBodyFlinchIntensity);
            }
        }

        /// <summary>
        /// A real stagger: the whole body reacts and locomotion is suppressed for its
        /// duration. Gated by the caller against FeedbackConfigSection.StaggerCooldown so
        /// REQ-124 (no stun-lock) holds.
        /// </summary>
        public void BeginStagger(Vector3 worldDirection, float intensity, float duration, Transform reference)
        {
            if (_dying)
            {
                return;
            }

            Vector3 local = reference != null
                ? reference.InverseTransformDirection(worldDirection)
                : worldDirection;
            local.y = 0f;
            if (local.sqrMagnitude > 0.0001f)
            {
                local.Normalize();
            }

            _staggerLocalDirection = local;
            _staggerIntensity = Mathf.Clamp(intensity, 0.3f, 2.5f);
            _staggerDuration = Mathf.Max(0.08f, duration);
            _staggerTime = 0f;
            ClearAttack();

            if (Driven)
            {
                // ADR-018-2: a gameplay stagger (REQ-124 window, 0.16–0.5 s) shows as the upper-body
                // hit clip while the legs stop (locomotion is suppressed by the enemy); the full-body
                // hit_heavy_stagger clip is reserved for an armour break (ArmorVisualBinder).
                Driver.PlayFlinch(local, BodyRegion.UpperBody, true);
            }
        }

        public bool IsStaggering => _staggerTime < _staggerDuration && _staggerDuration > 0f;

        /// <summary>Pick and start a death reaction. The enemy despawns when DeathFinished.</summary>
        public void BeginDeath(Vector3 worldDirection, float intensity, BodyRegion region, bool heavy,
            Transform reference, ref EnemyRandom random)
        {
            if (_dying)
            {
                return;
            }

            Vector3 local = reference != null
                ? reference.InverseTransformDirection(worldDirection)
                : worldDirection;
            local.y = 0f;
            if (local.sqrMagnitude > 0.0001f)
            {
                local.Normalize();
            }

            // Direction and region choose the reaction; a roll breaks ties so repeated
            // kills from the same angle do not look like a copy-paste.
            if (region == BodyRegion.Head && intensity < 1.6f)
            {
                _deathStyle = ZombieDeathStyle.HeadshotDrop;
            }
            else if (intensity >= 2.2f)
            {
                _deathStyle = local.z < -0.35f ? ZombieDeathStyle.FallBackward : ZombieDeathStyle.SpinFall;
            }
            else if (local.z > 0.35f)
            {
                _deathStyle = ZombieDeathStyle.FallForward;
            }
            else if (local.z < -0.35f)
            {
                _deathStyle = ZombieDeathStyle.FallBackward;
            }
            else
            {
                _deathStyle = random.Chance(0.5f) ? ZombieDeathStyle.Crumple : ZombieDeathStyle.SpinFall;
            }

            _deathSide = Mathf.Abs(local.x) > 0.05f ? Mathf.Sign(local.x) : random.Sign();
            _deathSpin = random.Range(120f, 260f) * _deathSide;

            // PROTOTYPE-ONLY: heavy tiers fall slower so the moment lands (REQ-125).
            _deathDuration = heavy
                ? DeathDurationHeavy
                : _deathStyle == ZombieDeathStyle.HeadshotDrop
                    ? DeathDurationHeadshot
                    : DeathDurationNormal;
            _deathTime = 0f;
            _dying = true;
            ClearAttack();
            _staggerDuration = 0f;

            if (Driven)
            {
                // AP-009 has four deaths; the five M1 styles fold onto them (HeadshotDrop → kneel,
                // Crumple → kneel, SpinFall → side — WP-032C: side_R when the push is to the right).
                // WP-032C tier deaths (v003): Special → special_A (forward skid) / special_B (spin, on the back) by the
                // push, Ultra → the 60 f death_ultra (3-frame standing hold for the S-07 cover VFX).
                int style = _deathStyle == ZombieDeathStyle.FallForward ? 0
                    : _deathStyle == ZombieDeathStyle.FallBackward ? 1
                    : _deathStyle == ZombieDeathStyle.SpinFall ? (_deathSide > 0f ? 4 : 2)
                    : 3;
                if (_tier == EnemyTier.Ultra) style = 7;
                else if (_tier == EnemyTier.Special) style = local.z > 0.35f ? 5 : 6;
                Driver.PlayDeath(style);
            }
        }

        /// <summary>WP-018: the tier's telegraph + top speed, so the driven clips are rate-scaled to the code timings.</summary>
        public void SetTiming(float telegraphSeconds, float topSpeedMetersPerSecond)
        {
            SetTiming(telegraphSeconds, topSpeedMetersPerSecond, 1f);
        }

        /// <summary>
        /// WP-032: + the version's telegraph scale (WP-028 TierBehaviourProfile): a shorter wind-up gets the
        /// full additive emphasis, a longer one proportionally less (emphasis = 1 / scale, clamped 0..1).
        /// </summary>
        public void SetTiming(float telegraphSeconds, float topSpeedMetersPerSecond, float telegraphScale)
        {
            _telegraphSeconds = telegraphSeconds;
            _topSpeedMps = Mathf.Max(0.1f, topSpeedMetersPerSecond);
            if (Driven)
            {
                Driver.SetTelegraphEmphasis(telegraphScale > 0.01f ? 1f / telegraphScale : 1f);
            }
        }

        /// <summary>WP-032: idle-in-crowd / queue wait for the driven body (WP-030 / S-03 calls this; the block rig ignores it).</summary>
        /// <summary>WP-032C: Runner stumble (v003 hit_special_stumble) — S-03's tier-ability hook on the driven body; the block rig ignores it.</summary>
        public void PlayStumble()
        {
            if (_dying) return;
            if (Driven) Driver.PlayStumble();
        }

        public void SetWaiting(bool waiting)
        {
            if (Driven)
            {
                Driver.SetWaiting(waiting);
            }
        }

        /// <summary>WP-018: (re)bind the driven body after the look / version is known. Rise plays when asked.</summary>
        public void ResetDriven(EnemyTier tier, string fit, bool rise, ref EnemyRandom random)
        {
            if (!Driven)
            {
                return;
            }

            Driver.ResetForSpawn(tier, _topSpeedMps, fit, random.Range(0, 2), rise);
        }

        // ======================================================================== tick

        public void Tick(float dt)
        {
            if (Driven)
            {
                // The Animator owns the pose; keep the gameplay clocks only (same arithmetic as the procedural tick).
                if (_dying)
                {
                    _deathTime += dt;
                    return;
                }

                if (_staggerDuration > 0f)
                {
                    _staggerTime += dt;
                    if (_staggerTime >= _staggerDuration)
                    {
                        _staggerDuration = 0f;
                        _staggerTime = 0f;
                    }
                }

                _flinchVelocity = Vector3.zero;
                _flinch = Vector3.zero;
                return;
            }

            if (_rig == null || !_rig.IsComplete)
            {
                return;
            }

            if (dt <= 0f)
            {
                return;
            }

            // Flinch spring runs even while dying so the last hit still registers.
            TickFlinch(dt);

            if (_dying)
            {
                _deathTime += dt;
                PoseDeath();
                return;
            }

            if (_staggerDuration > 0f)
            {
                _staggerTime += dt;
                if (_staggerTime >= _staggerDuration)
                {
                    _staggerDuration = 0f;
                    _staggerTime = 0f;
                }
            }

            AdvancePhase(dt);
            PoseLocomotion();
            PoseAttackOverlay();
            PoseStaggerOverlay();
            ApplyFlinch();
        }

        // --------------------------------------------------------------------- phase

        private void AdvancePhase(float dt)
        {
            // Cadence follows real speed so a slowed enemy does not moonwalk.
            // PROTOTYPE-ONLY: 2.1 rad per metre travelled is one step per ~1.5 m.
            float cadence = (0.9f + _speedMps * 2.1f) * _cadenceScale;
            if (IsStaggering)
            {
                cadence *= 0.25f;
            }

            _phase += cadence * dt;
            if (_phase > Mathf.PI * 2f)
            {
                _phase -= Mathf.PI * 2f;
            }
        }

        // ---------------------------------------------------------------- locomotion

        private void PoseLocomotion()
        {
            float p = _phase;
            bool running = Pose == ZombiePose.Run;

            // Amplitude scales with speed so idle is a slow sway, not a march in place.
            float drive = Mathf.Lerp(0.25f, 1f, _speed01);
            float amp = (running ? 38f : 26f) * _amplitudeScale * drive;
            float kneeAmp = (running ? 62f : 40f) * _amplitudeScale * drive;

            float leftBias = 1f;
            float rightBias = 1f;
            if (_limps)
            {
                // The bad leg takes a shorter step and the hip drops onto it.
                if (_limpSide < 0f) leftBias = 0.5f; else rightBias = 0.5f;
            }

            float sinL = Mathf.Sin(p);
            float sinR = Mathf.Sin(p + Mathf.PI);

            SetLocal(_rig.ThighL, ZombieRig.JointThighL, new Vector3(sinL * amp * leftBias, 0f, 0f));
            SetLocal(_rig.ThighR, ZombieRig.JointThighR, new Vector3(sinR * amp * rightBias, 0f, 0f));

            // Knees only bend one way.
            float kneeL = Mathf.Max(0f, Mathf.Sin(p + 2.4f)) * kneeAmp * leftBias;
            float kneeR = Mathf.Max(0f, Mathf.Sin(p + 2.4f + Mathf.PI)) * kneeAmp * rightBias;
            SetLocal(_rig.ShinL, ZombieRig.JointShinL, new Vector3(-kneeL, 0f, 0f));
            SetLocal(_rig.ShinR, ZombieRig.JointShinR, new Vector3(-kneeR, 0f, 0f));

            // Hips: vertical bob on double frequency, roll on step frequency.
            float bob = (running ? 0.055f : 0.032f) * _amplitudeScale * drive;
            float limpDip = _limps ? Mathf.Max(0f, Mathf.Sin(p + (_limpSide < 0f ? 0f : Mathf.PI))) * 0.045f : 0f;
            Vector3 hipsRest = _rig.RestPosition(ZombieRig.JointHips);
            _rig.Hips.localPosition = new Vector3(
                hipsRest.x,
                hipsRest.y - bob * 0.5f + Mathf.Abs(Mathf.Sin(p * 2f)) * bob - limpDip,
                hipsRest.z);

            float hipRoll = sinL * (running ? 7f : 5f) * _amplitudeScale;
            float hipYaw = -sinL * 6f * _torsoTwistScale * drive;
            SetLocal(_rig.Hips, ZombieRig.JointHips, new Vector3(0f, hipYaw, hipRoll));

            // Spine carries the tier lean; running leans harder.
            float lean = _baseLean + (running ? 16f : 0f) * drive;
            float spineSway = Mathf.Sin(p * 2f) * 2.4f * _amplitudeScale;
            SetLocal(_rig.Spine, ZombieRig.JointSpine, new Vector3(lean + spineSway, 0f, 0f));

            // Chest counter-twists against the hips.
            float chestYaw = sinL * 9f * _torsoTwistScale * drive;
            SetLocal(_rig.Chest, ZombieRig.JointChest, new Vector3(0f, chestYaw, 0f));

            // Head keeps its dead tilt and looks slightly down.
            SetLocal(_rig.Head, ZombieRig.JointHead, new Vector3(6f, -chestYaw * 0.5f, _headTilt));

            // Arms. Carriage decides how far forward they hang; they swing against the legs.
            float carriage = Mathf.Lerp(-8f, -78f, _armCarriage);
            float armSwing = SkinnedLocomotion ? 0f : (running ? 26f : 15f) * _amplitudeScale * drive;
            float elbow = Mathf.Lerp(-14f, -52f, _armCarriage) - Mathf.Abs(sinL) * 8f;

            SetLocal(_rig.UpperArmL, ZombieRig.JointUpperArmL,
                new Vector3(carriage + sinR * armSwing, 0f, 8f));
            SetLocal(_rig.UpperArmR, ZombieRig.JointUpperArmR,
                new Vector3(carriage + sinL * armSwing, 0f, -8f));
            SetLocal(_rig.ForearmL, ZombieRig.JointForearmL, new Vector3(elbow, 0f, 0f));
            SetLocal(_rig.ForearmR, ZombieRig.JointForearmR, new Vector3(elbow, 0f, 0f));

            // Visual node stays upright during locomotion; death and stagger own it.
            _rig.Visual.localRotation = _rig.RestRotation(ZombieRig.JointVisual);
        }

        // -------------------------------------------------------------------- attack
        // Windup, active and recovery each get a distinct, exaggerated shape. The active
        // window is the SHORTEST and FASTEST of the three, which is what makes the swing
        // read as a strike rather than as a wave.

        private void PoseAttackOverlay()
        {
            if (_attackPhase == ZombieAttackPhase.None || IsStaggering)
            {
                return;
            }

            switch (_attackStyle)
            {
                case ZombieAttackStyle.Lunge: PoseLunge(); break;
                case ZombieAttackStyle.Sweep: PoseSweep(); break;
                case ZombieAttackStyle.Overhead: PoseOverhead(); break;
                default: PoseSwipe(); break;
            }
        }

        private void PoseSwipe()
        {
            float t = _attackT;
            switch (_attackPhase)
            {
                case ZombieAttackPhase.Windup:
                {
                    // Ease-out so the arm snaps up early and HOLDS at the top: the hold is
                    // the telegraph the player reads.
                    float e = 1f - (1f - t) * (1f - t);
                    Blend(_rig.Chest, ZombieRig.JointChest, new Vector3(-6f * e, -34f * e, 0f), 1f);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(-150f * e, 20f * e, -30f * e), 1f);
                    Blend(_rig.ForearmR, ZombieRig.JointForearmR, new Vector3(-70f * e, 0f, 0f), 1f);
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean - 12f * e, 0f, 0f), 1f);
                    break;
                }
                case ZombieAttackPhase.Active:
                {
                    float e = t * t;   // accelerate through the strike
                    Blend(_rig.Chest, ZombieRig.JointChest, new Vector3(10f * e, Mathf.Lerp(-34f, 26f, e), 0f), 1f);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR,
                        new Vector3(Mathf.Lerp(-150f, 34f, e), Mathf.Lerp(20f, -18f, e), Mathf.Lerp(-30f, 12f, e)), 1f);
                    Blend(_rig.ForearmR, ZombieRig.JointForearmR, new Vector3(Mathf.Lerp(-70f, -12f, e), 0f, 0f), 1f);
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean + 14f * e, 0f, 0f), 1f);
                    break;
                }
                case ZombieAttackPhase.Recovery:
                {
                    float w = 1f - t;
                    Blend(_rig.Chest, ZombieRig.JointChest, new Vector3(10f, 26f, 0f), w);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(34f, -18f, 12f), w);
                    Blend(_rig.ForearmR, ZombieRig.JointForearmR, new Vector3(-12f, 0f, 0f), w);
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean + 14f, 0f, 0f), w);
                    break;
                }
            }
        }

        private void PoseLunge()
        {
            float t = _attackT;
            switch (_attackPhase)
            {
                case ZombieAttackPhase.Windup:
                {
                    // Coil: drop, pull the arms back. Short but unmistakable - the request
                    // asks for a "short, visible preparation".
                    float e = 1f - (1f - t) * (1f - t);
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean + 26f * e, 0f, 0f), 1f);
                    Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(52f * e, 0f, 18f * e), 1f);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(52f * e, 0f, -18f * e), 1f);
                    Blend(_rig.ThighL, ZombieRig.JointThighL, new Vector3(26f * e, 0f, 0f), 1f);
                    Blend(_rig.ThighR, ZombieRig.JointThighR, new Vector3(26f * e, 0f, 0f), 1f);
                    Blend(_rig.ShinL, ZombieRig.JointShinL, new Vector3(-48f * e, 0f, 0f), 1f);
                    Blend(_rig.ShinR, ZombieRig.JointShinR, new Vector3(-48f * e, 0f, 0f), 1f);
                    Offset(_rig.Hips, ZombieRig.JointHips, new Vector3(0f, -0.16f * e, 0f));
                    break;
                }
                case ZombieAttackPhase.Active:
                {
                    float e = Mathf.Sqrt(t);   // explode out of the coil
                    Blend(_rig.Visual, ZombieRig.JointVisual, new Vector3(24f * e, 0f, 0f), 1f);
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean + 20f, 0f, 0f), 1f);
                    Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(-120f * e, 0f, 22f), 1f);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(-120f * e, 0f, -22f), 1f);
                    Blend(_rig.ForearmL, ZombieRig.JointForearmL, new Vector3(-10f, 0f, 0f), 1f);
                    Blend(_rig.ForearmR, ZombieRig.JointForearmR, new Vector3(-10f, 0f, 0f), 1f);
                    Offset(_rig.Hips, ZombieRig.JointHips, new Vector3(0f, -0.08f, 0f));
                    break;
                }
                case ZombieAttackPhase.Recovery:
                {
                    // Overshoot and stumble - the Runner pays for the commitment.
                    float w = 1f - t;
                    Blend(_rig.Visual, ZombieRig.JointVisual, new Vector3(24f, 0f, 0f), w);
                    Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(-120f, 0f, 22f), w);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(-120f, 0f, -22f), w);
                    break;
                }
            }
        }

        private void PoseSweep()
        {
            float t = _attackT;
            switch (_attackPhase)
            {
                case ZombieAttackPhase.Windup:
                {
                    // Long, wide, obvious. The Guard's telegraph is its whole design.
                    float e = 1f - (1f - t) * (1f - t);
                    Blend(_rig.Chest, ZombieRig.JointChest, new Vector3(0f, -58f * e, 0f), 1f);
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean - 10f * e, 0f, 0f), 1f);
                    Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(-84f * e, -30f * e, 62f * e), 1f);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(-84f * e, -30f * e, -62f * e), 1f);
                    Blend(_rig.ForearmL, ZombieRig.JointForearmL, new Vector3(-20f * e, 0f, 0f), 1f);
                    Blend(_rig.ForearmR, ZombieRig.JointForearmR, new Vector3(-20f * e, 0f, 0f), 1f);
                    break;
                }
                case ZombieAttackPhase.Active:
                {
                    float e = t * t;
                    Blend(_rig.Chest, ZombieRig.JointChest, new Vector3(0f, Mathf.Lerp(-58f, 62f, e), 0f), 1f);
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean + 8f * e, 0f, 0f), 1f);
                    Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(-84f, Mathf.Lerp(-30f, 30f, e), 74f), 1f);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(-84f, Mathf.Lerp(-30f, 30f, e), -74f), 1f);
                    break;
                }
                case ZombieAttackPhase.Recovery:
                {
                    float w = 1f - t;
                    Blend(_rig.Chest, ZombieRig.JointChest, new Vector3(0f, 62f, 0f), w);
                    Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(-84f, 30f, 74f), w);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(-84f, 30f, -74f), w);
                    break;
                }
            }
        }

        private void PoseOverhead()
        {
            float t = _attackT;
            switch (_attackPhase)
            {
                case ZombieAttackPhase.Windup:
                {
                    // Arms straight up, body arched back. Visible from across the courtyard.
                    float e = 1f - (1f - t) * (1f - t);
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean - 26f * e, 0f, 0f), 1f);
                    Blend(_rig.Chest, ZombieRig.JointChest, new Vector3(-14f * e, 0f, 0f), 1f);
                    Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(-172f * e, 0f, 14f * e), 1f);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(-172f * e, 0f, -14f * e), 1f);
                    Blend(_rig.ForearmL, ZombieRig.JointForearmL, new Vector3(-18f * e, 0f, 0f), 1f);
                    Blend(_rig.ForearmR, ZombieRig.JointForearmR, new Vector3(-18f * e, 0f, 0f), 1f);
                    Offset(_rig.Hips, ZombieRig.JointHips, new Vector3(0f, 0.05f * e, 0f));
                    break;
                }
                case ZombieAttackPhase.Active:
                {
                    float e = t * t * t;   // very sharp: the smash is almost instantaneous
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(Mathf.Lerp(_baseLean - 26f, _baseLean + 46f, e), 0f, 0f), 1f);
                    Blend(_rig.Chest, ZombieRig.JointChest, new Vector3(Mathf.Lerp(-14f, 22f, e), 0f, 0f), 1f);
                    Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(Mathf.Lerp(-172f, 20f, e), 0f, 14f), 1f);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(Mathf.Lerp(-172f, 20f, e), 0f, -14f), 1f);
                    Offset(_rig.Hips, ZombieRig.JointHips, new Vector3(0f, Mathf.Lerp(0.05f, -0.14f, e), 0f));
                    break;
                }
                case ZombieAttackPhase.Recovery:
                {
                    // Long, heavy recovery. This is the Ultra's window of vulnerability.
                    float w = 1f - t;
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean + 46f, 0f, 0f), w);
                    Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(20f, 0f, 14f), w);
                    Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(20f, 0f, -14f), w);
                    Offset(_rig.Hips, ZombieRig.JointHips, new Vector3(0f, -0.14f * w, 0f));
                    break;
                }
            }
        }

        // ------------------------------------------------------------------- stagger

        private void PoseStaggerOverlay()
        {
            if (!IsStaggering)
            {
                return;
            }

            // One decaying half-sine: hard reaction immediately, recovery back to gait.
            float t = _staggerTime / _staggerDuration;
            float w = Mathf.Sin((1f - t) * Mathf.PI * 0.5f);
            float m = _staggerIntensity * w;

            Vector3 d = _staggerLocalDirection;
            Blend(_rig.Visual, ZombieRig.JointVisual, new Vector3(d.z * 22f * m, d.x * 14f * m, -d.x * 16f * m), 1f);
            Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean + d.z * 24f * m, 0f, -d.x * 10f * m), 1f);
            Blend(_rig.Chest, ZombieRig.JointChest, new Vector3(d.z * 16f * m, d.x * 18f * m, 0f), 1f);
            Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(-40f * m, 0f, 34f * m), 1f);
            Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(-40f * m, 0f, -34f * m), 1f);
            Offset(_rig.Hips, ZombieRig.JointHips, new Vector3(0f, -0.07f * m, 0f));
        }

        // -------------------------------------------------------------------- flinch

        private void TickFlinch(float dt)
        {
            // Critically-ish damped spring back to zero. PROTOTYPE-ONLY constants tuned so a
            // rifle bullet reads as a twitch and is gone in ~0.18 s.
            const float stiffness = 420f;
            const float damping = 26f;

            _flinchVelocity += (-_flinch * stiffness - _flinchVelocity * damping) * dt;
            _flinch += _flinchVelocity * dt;

            if (_flinch.sqrMagnitude < 0.0004f && _flinchVelocity.sqrMagnitude < 0.02f)
            {
                _flinch = Vector3.zero;
                _flinchVelocity = Vector3.zero;
            }
        }

        private void ApplyFlinch()
        {
            if (_flinch == Vector3.zero)
            {
                return;
            }

            // Distribution shifts with the region that was hit: a head hit snaps the head,
            // a leg hit rocks the hips. The legs keep walking either way (REQ-123).
            float headWeight = 0.55f + 0.45f * Mathf.Max(0f, _flinchRegionBias);
            float chestWeight = 1f - 0.35f * Mathf.Abs(_flinchRegionBias);
            float hipsWeight = 0.18f + 0.32f * Mathf.Max(0f, -_flinchRegionBias);

            AddEuler(_rig.Chest, _flinch * chestWeight);
            AddEuler(_rig.Head, _flinch * headWeight);
            AddEuler(_rig.Hips, _flinch * hipsWeight);
        }

        // --------------------------------------------------------------------- death

        private void PoseDeath()
        {
            float t = Mathf.Clamp01(_deathTime / _deathDuration);
            float fall = t * t;                       // gravity-ish acceleration
            float settle = Mathf.Clamp01((t - 0.7f) / 0.3f);

            Vector3 hipsRest = _rig.RestPosition(ZombieRig.JointHips);
            float sink = 0f;
            Vector3 visualEuler = Vector3.zero;

            switch (_deathStyle)
            {
                case ZombieDeathStyle.FallForward:
                    visualEuler = new Vector3(88f * fall, 0f, 6f * fall * _deathSide);
                    sink = 0.30f * fall;
                    break;

                case ZombieDeathStyle.FallBackward:
                    visualEuler = new Vector3(-84f * fall, 0f, -6f * fall * _deathSide);
                    sink = 0.34f * fall;
                    break;

                case ZombieDeathStyle.Crumple:
                    // Legs give out first, then the torso folds over them.
                    visualEuler = new Vector3(30f * fall, 0f, 12f * fall * _deathSide);
                    sink = 0.62f * Mathf.Sqrt(t);
                    Blend(_rig.ThighL, ZombieRig.JointThighL, new Vector3(-72f * fall, 0f, 0f), 1f);
                    Blend(_rig.ThighR, ZombieRig.JointThighR, new Vector3(-72f * fall, 0f, 0f), 1f);
                    Blend(_rig.ShinL, ZombieRig.JointShinL, new Vector3(-110f * fall, 0f, 0f), 1f);
                    Blend(_rig.ShinR, ZombieRig.JointShinR, new Vector3(-110f * fall, 0f, 0f), 1f);
                    Blend(_rig.Spine, ZombieRig.JointSpine, new Vector3(_baseLean + 62f * fall, 0f, 0f), 1f);
                    break;

                case ZombieDeathStyle.SpinFall:
                    visualEuler = new Vector3(40f * fall, _deathSpin * fall, 82f * fall * _deathSide);
                    sink = 0.32f * fall;
                    break;

                default: // HeadshotDrop - no flail, straight down. Reads as a clean kill.
                    visualEuler = new Vector3(16f * fall, 0f, 10f * fall * _deathSide);
                    sink = 0.78f * Mathf.Sqrt(t);
                    Blend(_rig.Head, ZombieRig.JointHead, new Vector3(46f * fall, 0f, 40f * fall * _deathSide), 1f);
                    Blend(_rig.ThighL, ZombieRig.JointThighL, new Vector3(-58f * fall, 0f, 0f), 1f);
                    Blend(_rig.ThighR, ZombieRig.JointThighR, new Vector3(-58f * fall, 0f, 0f), 1f);
                    Blend(_rig.ShinL, ZombieRig.JointShinL, new Vector3(-96f * fall, 0f, 0f), 1f);
                    Blend(_rig.ShinR, ZombieRig.JointShinR, new Vector3(-96f * fall, 0f, 0f), 1f);
                    break;
            }

            _rig.Visual.localRotation = _rig.RestRotation(ZombieRig.JointVisual) * Quaternion.Euler(visualEuler);
            _rig.Hips.localPosition = new Vector3(hipsRest.x, hipsRest.y - sink, hipsRest.z);

            if (_deathStyle != ZombieDeathStyle.Crumple && _deathStyle != ZombieDeathStyle.HeadshotDrop)
            {
                // Loose limbs on a rigid fall: arms trail, legs straighten.
                float flail = Mathf.Sin(t * Mathf.PI) * 40f;
                Blend(_rig.UpperArmL, ZombieRig.JointUpperArmL, new Vector3(-flail, 0f, 26f * fall), 1f);
                Blend(_rig.UpperArmR, ZombieRig.JointUpperArmR, new Vector3(-flail, 0f, -26f * fall), 1f);
                Blend(_rig.ThighL, ZombieRig.JointThighL, new Vector3(-14f * fall, 0f, 0f), 1f);
                Blend(_rig.ThighR, ZombieRig.JointThighR, new Vector3(10f * fall, 0f, 0f), 1f);
                Blend(_rig.ShinL, ZombieRig.JointShinL, new Vector3(-22f * fall, 0f, 0f), 1f);
                Blend(_rig.ShinR, ZombieRig.JointShinR, new Vector3(-18f * fall, 0f, 0f), 1f);
            }

            // Last twitch as the body settles.
            if (settle > 0f)
            {
                AddEuler(_rig.Chest, new Vector3(Mathf.Sin(settle * 14f) * (1f - settle) * 4f, 0f, 0f));
            }

            ApplyFlinch();
        }

        // ------------------------------------------------------------------ plumbing

        private void SetLocal(Transform joint, int index, Vector3 euler)
        {
            if (joint != null)
            {
                joint.localRotation = _rig.RestRotation(index) * Quaternion.Euler(euler);
            }
        }

        /// <summary>Blend an override pose over whatever locomotion already wrote.</summary>
        private void Blend(Transform joint, int index, Vector3 euler, float weight)
        {
            if (joint == null || weight <= 0f)
            {
                return;
            }

            Quaternion target = _rig.RestRotation(index) * Quaternion.Euler(euler);
            joint.localRotation = weight >= 1f
                ? target
                : Quaternion.Slerp(joint.localRotation, target, weight);
        }

        private void Offset(Transform joint, int index, Vector3 localOffset)
        {
            if (joint != null)
            {
                joint.localPosition += localOffset;
            }
        }

        private static void AddEuler(Transform joint, Vector3 euler)
        {
            if (joint != null)
            {
                joint.localRotation *= Quaternion.Euler(euler);
            }
        }
    }
}
