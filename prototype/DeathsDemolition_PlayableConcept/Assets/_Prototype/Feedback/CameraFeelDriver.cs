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
        }

        private void Unsubscribe()
        {
            ProjectileEvents.Spawned -= OnSpawned;
            HitFeedbackDirector.EnemyHitDressed -= OnEnemyHitDressed;
            PlayerHealth.AnyDamaged -= OnPlayerDamaged;
            DD.Prototype.Characters.CharacterAnimationEvents.Any -= OnAnimationEvent;
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

            if (_killCount > 0 && Time.time - _killFirst >= CameraModesFeel.KillMergeWindow)
            {
                KillBeat();
            }
        }

        // ------------------------------------------------------------------ kill confirm

        private void OnEnemyHitDressed(EnemyHitFeedbackInfo info, HitFeedbackTier tier)
        {
            if (tier != HitFeedbackTier.Death && tier != HitFeedbackTier.HeavyDeath) return;
            if (_killCount == 0) _killFirst = Time.time;
            _killCount++;
            _killHead |= info.Result.Region == DD.Prototype.Damage.BodyRegion.Head;
        }

        private void KillBeat()
        {
            if (Time.time - _killLastBeat < CameraModesFeel.KillMinInterval)
            {
                return;   // wait: beats stay ≥ 120 ms apart (the merged count carries over)
            }

            bool big = _killCount >= 3;
            CameraMode mode = CameraModeService.Current;
            if (mode == CameraMode.FirstPerson)
            {
                CameraReactions.AddKick(Vector3.zero, CameraModesFeel.KillBumpRollFpDeg * (big ? 1.5f : 1f) * _rollSign);
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
        }

        // ------------------------------------------------------------------ directional damage

        private void OnPlayerDamaged(PlayerDamageInfo info)
        {
            if (info.AppliedAmount <= 0f || info.Player == null) return;
            CameraMode mode = CameraModeService.Current;
            if (mode == CameraMode.TopDown) return;   // top-down keeps the existing PlayerDamage shake
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
