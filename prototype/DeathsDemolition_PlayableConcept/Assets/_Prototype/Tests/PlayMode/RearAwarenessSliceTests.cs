using System.Collections;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-076 B7 on real bodies in first person: a Standard approaching from behind raises rear_threat once within 6 m,
    /// the rear level climbs to Alert and its first hit lands ≥ 0.8 s after the beat; a Standard that appears already
    /// close behind is warned at once and starts no wind-up for 0.8 s.
    /// </summary>
    [Category("WP076")]
    public sealed class RearAwarenessSliceTests
    {
        private PlayerRig _rig;
        private int _watchLife;
        private float _beatAt = -1f, _beatDistance = -1f, _firstHitAt = -1f, _firstWindupAt = -1f;
        private int _beats;

        private void OnBeat(EnemyCombatEventInfo e)
        {
            if (e.LifeId != _watchLife) return;
            if (e.Kind == EnemyCombatEventKind.RearThreat)
            {
                _beats++;
                if (_beatAt < 0f) { _beatAt = Time.time; _beatDistance = Vector3.ProjectOnPlane(e.Body.position - _rig.transform.position, Vector3.up).magnitude; }
            }
            else if (e.Kind == EnemyCombatEventKind.AttackHit && _firstHitAt < 0f) _firstHitAt = Time.time;
            else if (e.Kind == EnemyCombatEventKind.TelegraphStart && _firstWindupAt < 0f) _firstWindupAt = Time.time;
        }

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        private IEnumerator Hazirla()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null, 30f, "Sahne kurulmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, 30f, "Koşu Fighting fazına girmedi.");
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            ZombieEnemy.ResetRearStatics();
            CrowdFeelSettings.ResetStatics();
            CrowdFeelSettings.Active.FlankShare = new[] { 0f, 0f, 0f, 0f };   // a straight approach from behind (no flank drift into view)
            CameraModeService.Request(CameraMode.FirstPerson, true);
            EnemyCombatEvents.Raised += OnBeat;
            yield return null; yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            EnemyCombatEvents.Raised -= OnBeat;
            CameraModeService.Request(CameraMode.TopDown, true);
            CrowdFeelSettings.ResetStatics();
            ZombieEnemy.ResetRisePolicy();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private ZombieEnemy Arkada(float metres)
        {
            Vector3 back = -Vector3.ProjectOnPlane(CameraView.Forward, Vector3.up).normalized;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, NavPathFollower.ClampToNavMesh(_rig.transform.position + back * metres, 3f), Quaternion.LookRotation(-back));
            Assert.IsNotNull(z, "spawn (havuz)");
            _watchLife = z.LifeId; _beatAt = -1f; _beatDistance = -1f; _firstHitAt = -1f; _firstWindupAt = -1f; _beats = 0;
            return z;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator FP_ArkadanYaklasma_TekUyari_Alert_OnUyariSuresi()
        {
            yield return Hazirla();
            Assert.AreEqual(CameraMode.FirstPerson, CameraModeService.Current);

            ZombieEnemy z = Arkada(11f);
            bool alert = false;
            float t0 = Time.time;
            while (_firstHitAt < 0f && Time.time - t0 < 12f)
            {
                alert |= ThreatHudFeed.RearLevel == ThreatRearLevel.Alert;
                yield return null;
            }
            Debug.Log("[DD-OBS] wp076.b7 beats=" + _beats + " beatAt=" + _beatDistance.ToString("0.00") + "m lead=" + (_firstHitAt - _beatAt).ToString("0.00") + "s alert=" + alert);
            Assert.AreEqual(1, _beats, "yaklaşma başına tek rear_threat");
            Assert.That(_beatDistance, Is.InRange(4.5f, 6.1f), "6 m'de");
            Assert.Greater(_firstHitAt, 0f, "vurdu");
            Assert.GreaterOrEqual(_firstHitAt - _beatAt, 0.8f, "ön uyarı ≥ 0,8 s");
            Assert.IsTrue(alert, "Alert (≤ 2,2 m)");
            Assert.GreaterOrEqual(ZombieEnemy.RearHitsWarned, 1, "B8 telemetrisi: uyarılı arka isabet sayıldı");
            Assert.AreEqual(0, ZombieEnemy.RearHitsUnwarned, "uyarısız arka isabet yok");
            Assert.GreaterOrEqual(ZombieEnemy.RearLeadMsSum / ZombieEnemy.RearHitsWarned, 800.0, "ortalama uyarı → isabet ≥ 800 ms");
            EnemyFactory.ClearAll(); yield return null;

            // already close behind: warned at once, no wind-up before the 0.8 s lead
            Arkada(2.2f);
            yield return Bekle(() => _firstWindupAt > 0f, 6f, "sonunda saldırır");
            Debug.Log("[DD-OBS] wp076.b7 closeLead=" + (_firstWindupAt - _beatAt).ToString("0.00"));
            Assert.Greater(_beatAt, 0f, "yakında belirende anında uyarı");
            Assert.GreaterOrEqual(_firstWindupAt - _beatAt, 0.8f - 0.02f, "telegraph ön uyarıdan önce başlamaz");
        }
    }
}
