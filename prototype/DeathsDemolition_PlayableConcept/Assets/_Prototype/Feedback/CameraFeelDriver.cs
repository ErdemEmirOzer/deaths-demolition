using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// WP-073 B4 · the S-07 side of the camera juice (direction A4): turns game events into <see cref="CameraReactions"/>
    /// calls with the <see cref="CameraModesFeel"/> values. The rig (S-01 sink) owns the springs, bob and caps.
    /// <list type="bullet">
    /// <item>per shot (a classic round's birth, the first pellet only) — FP: the weapon row's roll, one frame late (weapon
    /// first, head after = weight), the FOV punch and the micro rattle; TP: kick back / down (VmBackCm × 0.005, ≤ 0.12 m),
    /// roll × 0.6 (≤ 1°), FOV × 0.65. Top-down unchanged (A4.4: its fire feel stays in WeaponFeelRow);</item>
    /// <item>kill confirm (A4.5 §5.2) — 80 ms merge, ≥ 120 ms apart: TP / top-down bump 0.008 m (0.012 for 3+), FP roll bump
    /// 0.10°; a head kill adds FOV +0.5° (not top-down);</item>
    /// <item>directional damage (§5.3) — roll toward the hit side (FP 1.2°, TP 0.8°, ×1.6 at ≥ 25 % HP), TP 0.03 m away from
    /// the source; the second hit inside 0.25 s only replaces a smaller one.</item>
    /// </list>
    /// Reduced motion (CameraModeSettings) and the shake slider are applied by the rig. 0 B per event.
    /// <para>WP-095 (R3) · top-down: the rig's <see cref="TopDownFeelCamera"/> is fed from <see cref="FeelEvents"/> only (his
    /// sözleşmesi §2 / §4.0, FeelProfileTD rows): shot → K1 / K3 / K4, fire edges → K4, explosion / player damage / heavy death /
    /// multi-kill / last zombie / armour break → K2 (+ K3, K5), crit → K3, round start → the K5 breath. Kills merge into one
    /// beat (killMergeSeconds, ≥ killBeatMinInterval apart) whose K3 is the strongest row that joined it, along the shot.</para>
    /// </summary>
    public sealed class CameraFeelDriver : MonoBehaviour
    {
        private static CameraFeelDriver s_instance;

        private CameraWeaponFeelRow _pendingRow;
        private int _pendingFrame = -1;
        private float _rollSign = 1f;

        private float _killFirst = -10f, _killLastBeat = -10f;
        private int _killCount;
        private bool _killHead;
        private Vector3 _killDir;     // WP-095 TD: the last merged kill's shot direction
        private float _killKick;      // WP-095 TD: the strongest K3 row that joined the pending beat

        private float _damageAt = -10f, _damageSize;

        public static CameraFeelDriver Instance => s_instance;
        public int Shots { get; private set; }
        public int KillBeats { get; private set; }
        public int DamageNudges { get; private set; }

        public static CameraFeelDriver Ensure()
        {
            if (s_instance == null)
            {
                var go = new GameObject("[CameraFeelDriver]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<CameraFeelDriver>();
            }

            s_instance.Rearm();
            return s_instance;
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
            s_instance = this;
            SceneManager.activeSceneChanged += OnSceneChanged;
            Rearm();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (s_instance == this) s_instance = null;
        }

        private void OnSceneChanged(Scene a, Scene b) => Rearm();

        // Static buses can be reset (tests / SubsystemRegistration): remove-then-add on each scene change.
        private void Rearm()
        {
            Unsubscribe();
            ProjectileEvents.Spawned += OnSpawned;
            HitFeedbackDirector.EnemyHitDressed += OnEnemyHitDressed;
            PlayerHealth.AnyDamaged += OnPlayerDamaged;
            DD.Prototype.Characters.CharacterAnimationEvents.Any += OnAnimationEvent;
            SubscribeTopDown();
        }

        private void Unsubscribe()
        {
            ProjectileEvents.Spawned -= OnSpawned;
            HitFeedbackDirector.EnemyHitDressed -= OnEnemyHitDressed;
            PlayerHealth.AnyDamaged -= OnPlayerDamaged;
            DD.Prototype.Characters.CharacterAnimationEvents.Any -= OnAnimationEvent;
            UnsubscribeTopDown();
        }

        // ------------------------------------------------------------------ J5 · the player's footfalls lock the bob phase

        private Object _playerRoot;
        private DD.Prototype.Characters.CharacterAnimationEvents _playerEvents;

        private void OnAnimationEvent(DD.Prototype.Characters.CharacterAnimationEvents source, DD.Prototype.Characters.CharacterAnimationEvent e)
        {
            if (e != DD.Prototype.Characters.CharacterAnimationEvent.FootLeft && e != DD.Prototype.Characters.CharacterAnimationEvent.FootRight) return;
            var player = PlayerRegistry.Primary;
            if (player == null) return;
            if (!ReferenceEquals(_playerRoot, player))
            {
                _playerRoot = player;
                _playerEvents = player.GetComponentInChildren<DD.Prototype.Characters.CharacterAnimationEvents>(true);
            }

            if (source != null && source == _playerEvents)
            {
                CameraModesFeel.ReportFootContact(e == DD.Prototype.Characters.CharacterAnimationEvent.FootLeft);
                FootContacts++;
            }
        }

        public int FootContacts { get; private set; }

        // ------------------------------------------------------------------ fire

        private void OnSpawned(ProjectileSpawnInfo info)
        {
            if (info.Kind == ProjectileKind.Pellet && info.PelletIndex != 0) return;   // one shot, not one per pellet
            Shot(CameraModesFeel.For(info.WeaponId));
        }

        /// <summary>One shot's cosmetic camera (public for tests / the capture agent).</summary>
        public void Shot(CameraWeaponFeelRow row)
        {
            if (row == null) return;
            CameraModesFeel.ActiveRow = row;
            Shots++;
            _rollSign = -_rollSign;   // alternate so a burst shivers instead of drifting
            CameraMode mode = CameraModeService.Current;
            switch (mode)
            {
                case CameraMode.FirstPerson:
                    CameraReactions.FovPunch(row.FpFovPunchDeg, CameraModesFeel.FovPunchPeakSeconds + 0.12f);
                    if (row.FpMicroDeg > 0f)
                    {
                        // FP shake is rotation only (D-15, 12°/m in the rig): the micro rattle as a metre amplitude.
                        CameraShakeService.Shake(row.FpMicroDeg / CameraModesFeel.FpShakeDegreesPerMeter,
                            row.FpMicroSeconds > 0f ? row.FpMicroSeconds : 0.12f, ShakeKind.Rattle);
                    }

                    _pendingRow = row;       // the roll lands one frame after the weapon's own kick
                    _pendingFrame = Time.frameCount + 1;
                    break;
                case CameraMode.ThirdPerson:
                    float back = row.TpBackMeters;
                    CameraReactions.AddKick(new Vector3(0f, -back * 0.3f, -back), row.TpRollDeg * _rollSign);
                    CameraReactions.FovPunch(row.TpFovPunchDeg, CameraModesFeel.FovPunchPeakSeconds + 0.12f);
                    break;
            }
        }

        private void Update()
        {
            if (_pendingRow != null && Time.frameCount >= _pendingFrame)
            {
                if (CameraModeService.Current == CameraMode.FirstPerson)
                {
                    CameraReactions.AddKick(Vector3.zero, _pendingRow.FpRollDeg * _rollSign);
                }

                _pendingRow = null;
            }

            if (_killCount > 0 && Time.time - _killFirst >= (IsTopDown ? FeelProfileTD.Active.killMergeSeconds : CameraModesFeel.KillMergeWindow))
            {
                KillBeat();
            }
        }

        // ------------------------------------------------------------------ kill confirm

        private void OnEnemyHitDressed(EnemyHitFeedbackInfo info, HitFeedbackTier tier)
        {
            if (tier != HitFeedbackTier.Death && tier != HitFeedbackTier.HeavyDeath) return;
            if (IsTopDown) return;   // WP-095: top-down counts its kills from FeelEvents.EnemyKilled (OnKillTD)
            if (_killCount == 0) _killFirst = Time.time;
            _killCount++;
            _killHead |= info.Result.Region == DD.Prototype.Damage.BodyRegion.Head;
        }

        private void KillBeat()
        {
            CameraMode mode = CameraModeService.Current;
            float minInterval = mode == CameraMode.TopDown ? FeelProfileTD.Active.killBeatMinInterval : CameraModesFeel.KillMinInterval;
            if (Time.time - _killLastBeat < minInterval)
            {
                return;   // wait: beats stay ≥ 120 ms apart (the merged count carries over)
            }

            bool big = _killCount >= 3;
            if (mode == CameraMode.FirstPerson)
            {
                CameraReactions.AddKick(Vector3.zero, CameraModesFeel.KillBumpRollFpDeg * (big ? 1.5f : 1f) * _rollSign);
            }
            else if (mode == CameraMode.TopDown)
            {
                TopDownKillBeat();   // WP-095 K3: the merged kill's row along the shot (replaces the 0.008 m bump in top-down)
            }
            else
            {
                CameraReactions.AddKick(new Vector3(0f, -(big ? CameraModesFeel.KillBumpBigMeters : CameraModesFeel.KillBumpMeters), 0f), 0f);
            }

            if (_killHead && mode != CameraMode.TopDown)
            {
                CameraReactions.FovPunch(CameraModesFeel.HeadshotFovDeg, CameraModesFeel.KillBumpSeconds + 0.1f);
            }

            KillBeats++;
            _killLastBeat = Time.time;
            _killCount = 0;
            _killHead = false;
            _killKick = 0f;
        }

        // ------------------------------------------------------------------ directional damage

        private void OnPlayerDamaged(PlayerDamageInfo info)
        {
            if (info.AppliedAmount <= 0f || info.Player == null) return;
            CameraMode mode = CameraModeService.Current;
            if (mode == CameraMode.TopDown) return;   // WP-095: top-down damage is K2 / K3 from FeelEvents.PlayerDamaged (OnHurtTD)
            bool heavy = info.MaxHealth > 0f && info.AppliedAmount / info.MaxHealth >= CameraModesFeel.HeavyDamageFraction;
            float size = heavy ? CameraModesFeel.HeavyDamageScale : 1f;
            if (Time.time - _damageAt < CameraModesFeel.DamageStackWindow && size <= _damageSize) return;   // the bigger one stays
            _damageAt = Time.time;
            _damageSize = size;

            Camera cam = Camera.main;
            Vector3 toSource = info.WorldPoint - info.Player.transform.position;
            toSource.y = 0f;
            float side = cam != null && toSource.sqrMagnitude > 1e-4f ? Mathf.Sign(Vector3.Dot(cam.transform.right, toSource)) : 1f;
            // Roll toward the hit side (the head turns into it): positive roll tilts left.
            float roll = -side * (mode == CameraMode.FirstPerson ? CameraModesFeel.DamageRollFpDeg : CameraModesFeel.DamageRollTpDeg) * size;
            Vector3 push = Vector3.zero;
            if (mode == CameraMode.ThirdPerson && cam != null && toSource.sqrMagnitude > 1e-4f)
            {
                Vector3 away = cam.transform.InverseTransformDirection(-toSource.normalized);
                push = away * CameraModesFeel.DamageKickTpMeters * size;
            }

            CameraReactions.AddKick(push, roll);
            DamageNudges++;
        }

        // ------------------------------------------------------------------ WP-095 (R3) · top-down channels K1–K5 (FeelEvents)

        // Cached once (SubscribeTopDown): the rearm after every scene change adds / removes these, nothing allocates per event.
        private System.Action<FeelShot> _onShotTD;
        private System.Action<FeelHit> _onHitTD;
        private System.Action<FeelKill> _onKillTD;
        private System.Action<FeelMultiKill> _onMultiTD;
        private System.Action<FeelExplosion> _onExplosionTD;
        private System.Action<FeelPlayerDamage> _onHurtTD;
        private System.Action<FeelReload> _onReloadTD;
        private System.Action<FeelRound> _onRoundStartTD, _onRoundEndTD;
        private System.Action<FeelLastZombie> _onLastTD;
        private System.Action<DD.Prototype.Core.Config.ClassicWeaponId> _onFireStartTD, _onFireStopTD;

        private static bool IsTopDown => CameraModeService.Current == CameraMode.TopDown;

        /// <summary>The active rig's top-down channels while the camera is top-down, else null (FP / TP never see them).</summary>
        private static TopDownFeelCamera TopDownChannels
        {
            get
            {
                if (!IsTopDown) return null;
                PrototypeCameraRig rig = PrototypeCameraRig.Active;
                return rig != null ? rig.TopDownFeel : null;
            }
        }

        private void SubscribeTopDown()
        {
            _onShotTD ??= OnShotTD;
            _onHitTD ??= OnHitTD;
            _onKillTD ??= OnKillTD;
            _onMultiTD ??= OnMultiTD;
            _onExplosionTD ??= OnExplosionTD;
            _onHurtTD ??= OnHurtTD;
            _onReloadTD ??= OnReloadStartTD;
            _onRoundStartTD ??= OnRoundStartTD;
            _onRoundEndTD ??= OnRoundEndTD;
            _onLastTD ??= OnLastTD;
            _onFireStartTD ??= OnFireStartTD;
            _onFireStopTD ??= OnFireStopTD;
            FeelEvents.ShotFired += _onShotTD;
            FeelEvents.EnemyHit += _onHitTD;
            FeelEvents.EnemyKilled += _onKillTD;
            FeelEvents.MultiKill += _onMultiTD;
            FeelEvents.Explosion += _onExplosionTD;
            FeelEvents.PlayerDamaged += _onHurtTD;
            FeelEvents.ReloadStarted += _onReloadTD;
            FeelEvents.RoundStarted += _onRoundStartTD;
            FeelEvents.RoundEnded += _onRoundEndTD;
            FeelEvents.LastZombie += _onLastTD;
            WeaponEvents.FireStarted += _onFireStartTD;
            WeaponEvents.FireStopped += _onFireStopTD;
        }

        private void UnsubscribeTopDown()
        {
            FeelEvents.ShotFired -= _onShotTD;
            FeelEvents.EnemyHit -= _onHitTD;
            FeelEvents.EnemyKilled -= _onKillTD;
            FeelEvents.MultiKill -= _onMultiTD;
            FeelEvents.Explosion -= _onExplosionTD;
            FeelEvents.PlayerDamaged -= _onHurtTD;
            FeelEvents.ReloadStarted -= _onReloadTD;
            FeelEvents.RoundStarted -= _onRoundStartTD;
            FeelEvents.RoundEnded -= _onRoundEndTD;
            FeelEvents.LastZombie -= _onLastTD;
            WeaponEvents.FireStarted -= _onFireStartTD;
            WeaponEvents.FireStopped -= _onFireStopTD;
        }

        /// <summary>K1 / K3 / K4 of one trigger pull from the TD profile's weapon row; a Special (no fire edges) leans once.</summary>
        private void OnShotTD(FeelShot shot)
        {
            TopDownFeelCamera td = TopDownChannels;
            if (td == null) return;
            FeelProfileTD profile = td.Profile;
            WeaponFeelTD row = shot.Special ? profile.Weapon(shot.SpecialId) : profile.Weapon(shot.Classic);
            td.Shot(row, shot.Direction, shot.Special);
        }

        private void OnFireStartTD(DD.Prototype.Core.Config.ClassicWeaponId id)
        {
            TopDownFeelCamera td = TopDownChannels;
            if (td != null) td.FireHeld(true);
        }

        /// <summary>K4 lets go on release — in any mode, so a release never strands a lean.</summary>
        private void OnFireStopTD(DD.Prototype.Core.Config.ClassicWeaponId id) => ReleaseLean();

        /// <summary>§4.8: a reload start lets the lean go.</summary>
        private void OnReloadStartTD(FeelReload reload) => ReleaseLean();

        /// <summary>§4.10: the round's end lets the lean go (K5 runs out on its own envelope).</summary>
        private void OnRoundEndTD(FeelRound round) => ReleaseLean();

        private static void ReleaseLean()
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            if (rig != null) rig.TopDownFeel.FireHeld(false);
        }

        /// <summary>§4.0: an armour break → its row (K2 + K3 along the hit); a non-lethal crit → the Crit row's K3. Ground fire never.</summary>
        private void OnHitTD(FeelHit hit)
        {
            TopDownFeelCamera td = TopDownChannels;
            if (td == null || hit.GroundFire) return;
            if (hit.ArmorBroke) td.Event(FeelEventId.ArmorBreak, hit.Direction);
            else if (hit.Critical && !hit.Killed) td.Event(FeelEventId.Crit, hit.Direction);
        }

        /// <summary>A kill joins the merged beat (its row's K3 along the shot); a heavy death adds its K2 / K5 now.</summary>
        private void OnKillTD(FeelKill kill)
        {
            TopDownFeelCamera td = TopDownChannels;
            if (td == null) return;
            FeelEventId id = !kill.Heavy ? FeelEventId.Kill
                : kill.Tier == DD.Prototype.Core.Config.EnemyTier.Ultra ? FeelEventId.HeavyDeath_Ultra : FeelEventId.HeavyDeath_Elite;
            if (kill.Heavy) td.EventWithoutKick(id);
            if (_killCount == 0) _killFirst = Time.time;
            _killCount++;
            Vector3 d = kill.Direction;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-6f) _killDir = d;
            _killKick = Mathf.Max(_killKick, td.Profile.Event(id).kickMeters);
        }

        /// <summary>§4.4: the tier row's K2 / K5 now; its K3 rides the pending kill beat (no second push).</summary>
        private void OnMultiTD(FeelMultiKill multi)
        {
            TopDownFeelCamera td = TopDownChannels;
            if (td == null) return;
            FeelEventId id = multi.Tier >= 3 ? FeelEventId.MultiKill_T3 : multi.Tier == 2 ? FeelEventId.MultiKill_T2 : FeelEventId.MultiKill_T1;
            td.EventWithoutKick(id);
            JoinBeatOrKick(td, multi.Position - PlayerPosition(), td.Profile.Event(id).kickMeters);
        }

        /// <summary>§4.11: the last zombie's K2 and K5 (the slow-motion envelope); its K3 rides the kill beat it follows.</summary>
        private void OnLastTD(FeelLastZombie last)
        {
            TopDownFeelCamera td = TopDownChannels;
            if (td == null) return;
            td.LastZombie();
            JoinBeatOrKick(td, _killDir, td.Profile.Event(FeelEventId.LastZombie).kickMeters);
        }

        private void JoinBeatOrKick(TopDownFeelCamera td, Vector3 direction, float kickMeters)
        {
            if (_killCount > 0)
            {
                _killKick = Mathf.Max(_killKick, kickMeters);
                return;
            }

            td.Kick(direction, kickMeters);
        }

        /// <summary>The merged kill beat: the strongest joined row (3+ merged kills: at least the MultiKill_T1 row), along the shot.</summary>
        private void TopDownKillBeat()
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            if (rig == null) return;
            TopDownFeelCamera td = rig.TopDownFeel;
            FeelProfileTD profile = td.Profile;
            float kick = _killKick;
            if (_killCount >= profile.multiKillTier1) kick = Mathf.Max(kick, profile.Event(FeelEventId.MultiKill_T1).kickMeters);
            td.Kick(_killDir, kick);
        }

        /// <summary>§4.5: the Explosion row × A(d), away from the blast.</summary>
        private void OnExplosionTD(FeelExplosion blast)
        {
            TopDownFeelCamera td = TopDownChannels;
            if (td != null) td.Explosion(blast.Origin, PlayerPosition());
        }

        /// <summary>§4.0: K2 Light → Heavy with the damage fraction, K3 away from the attacker.</summary>
        private void OnHurtTD(FeelPlayerDamage damage)
        {
            TopDownFeelCamera td = TopDownChannels;
            if (td == null || damage.Amount <= 0f) return;
            td.PlayerDamaged(damage.Fraction, damage.Heavy, damage.Direction);
        }

        /// <summary>§4.10: the round-start breath, skipped while a living zombie is within 8 m of the player.</summary>
        private void OnRoundStartTD(FeelRound round)
        {
            TopDownFeelCamera td = TopDownChannels;
            if (td == null) return;
            td.RoundStart(ZombieWithin(PlayerPosition(), TopDownFeelCamera.RoundStartClearRadiusMeters));
        }

        private static Vector3 PlayerPosition()
        {
            PlayerRig player = PlayerRegistry.Primary;
            if (player != null) return player.transform.position;
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            return rig != null && rig.FollowTarget != null ? rig.FollowTarget.position : Vector3.zero;
        }

        private static bool ZombieWithin(Vector3 point, float radius)
        {
            System.Collections.Generic.IReadOnlyList<DD.Prototype.Enemies.ZombieEnemy> live = DD.Prototype.Enemies.ZombieEnemy.Live;
            float r2 = radius * radius;
            for (int i = 0; i < live.Count; i++)
            {
                DD.Prototype.Enemies.ZombieEnemy zombie = live[i];
                if (zombie == null || !zombie.IsAlive) continue;
                Vector3 d = zombie.transform.position - point;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) return true;
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
