using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Audio;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-052 on real bodies in the Run scene: (1) the beat → VFX chain — spawn dust on the rise, claw swipe
    /// on a commit, the ground slam + shake on the Ultra overhead — and the beat → audio chain (WP-048
    /// counters) advance from the same beats; (2) the Ultra overhead is clip-driven (window events, no
    /// timer fallback) so telegraph parity holds live; (3) the WP-032C reactions through S-03's hooks:
    /// the Runner stumble on a stagger, the armour break by region, the tier deaths; (4) the Ultra notice
    /// fires once per life.
    /// </summary>
    [Category("WP052")]
    public sealed class TierExperienceSliceTests
    {
        private const float KurulumZamanAsimi = 30f;
        private EnemySpawnDirector _director;
        private PlayerRig _rig;
        private readonly List<EnemyCombatEventInfo> _beats = new List<EnemyCombatEventInfo>();
        private void Kaydet(EnemyCombatEventInfo i) => _beats.Add(i);
        private ZombieAttackStyle CommitStyle(int lifeId)
        {
            for (int i = 0; i < _beats.Count; i++) if (_beats[i].LifeId == lifeId && _beats[i].Kind == EnemyCombatEventKind.AttackCommit) return _beats[i].Style;
            Assert.Fail("commit vuruşu yok (life " + lifeId + ")");
            return ZombieAttackStyle.Swipe;
        }

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        private IEnumerator RunSahnesiniHazirla()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null, KurulumZamanAsimi, "Sahne kurulmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            TierBehaviourProfileSet.ResetStatics();
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            EnemyCombatEvents.ResetStatics();
            EnemyAttackState.ResetStatics();
            _beats.Clear();
            EnemyCombatEvents.Raised += Kaydet;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            EnemyCombatEvents.Raised -= Kaydet;
            Time.captureDeltaTime = 0f;
            ZombieEnemy.ResetRisePolicy();
            TierBehaviourProfileSet.ResetStatics();
            EnemyCombatEvents.ResetStatics();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private Vector3 Zemin(float x, float z)
        {
            Vector3 p = _rig.transform.position + new Vector3(x, 0f, z);
            p.y = _rig.transform.position.y;
            return p;
        }

        private ZombieEnemy Dogur(EnemyTier tier, Vector3 konum, SpawnVisibility v = SpawnVisibility.Unknown)
        {
            Vector3 d = _rig.transform.position - konum; d.y = 0f;
            Quaternion bakis = d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
            ZombieEnemy z = EnemyFactory.Spawn(tier, 9, konum, bakis, v);
            Assert.IsNotNull(z, tier + " doğmadı");
            return z;
        }

        // ================================================================= beats → audio (WP-048) + style payload + window events

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Vuruslar_StilYuku_SesSayaclari_UltraOverheadKlipOlayi_FarkEtme()
        {
            yield return RunSahnesiniHazirla();
            AudioEventRouter router = PrototypeAudioDirector.Instance != null ? PrototypeAudioDirector.Instance.Router : null;
            int windup0 = router != null ? router.WindupCount : 0, attack0 = router != null ? router.AttackCount : 0, rise0 = router != null ? router.RiseCount : 0;

            // spawn rise (hybrid policy, off-screen): the rise beat carries the feet position for S-07's spawn dust and drives the WP-048 rise voice
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.OffScreenOnly;
            ZombieEnemy riser = Dogur(EnemyTier.Standard, Zemin(-4f, 9f), SpawnVisibility.OffScreen);
            yield return null;
            bool skinned = ((ZombieAppearance)riser.VersionVisual).IsSkinned;
            if (skinned)
            {
                Assert.AreEqual(1, EnemyCombatEvents.Count(EnemyCombatEventKind.SpawnRise));
                if (router != null) Debug.Log("[DD-OBS] wp052.audio riseCount " + rise0 + " → " + router.RiseCount + " (S-08 mix plan / budget decides; the beat is asserted, the voice is observed)");
            }
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;

            // a Standard swing: commit carries Style = Swipe; WP-048 windup / attack voices come from the same beats
            ZombieEnemy std = Dogur(EnemyTier.Standard, Zemin(0f, 1.3f));
            yield return Bekle(() => EnemyCombatEvents.Count(EnemyCombatEventKind.AttackCommit) >= 1, 8f, "Standard commit gelmedi");
            yield return null;
            Assert.AreEqual(ZombieAttackStyle.Swipe, CommitStyle(std.LifeId), "Standard commit stili Swipe");
            if (router != null)
            {
                Assert.Greater(router.WindupCount, windup0, "WP-048 hazırlık sesi aynı vuruştan");
                Assert.Greater(router.AttackCount, attack0, "WP-048 saldırı sesi aynı vuruştan");
            }
            std.Kill(); yield return null;
            EnemyFactory.ClearAll(); yield return null;

            // the Ultra: notice once per life, the overhead commit carries Style = Overhead (S-07's ground slam moment),
            // and the window is clip-driven (telegraph parity live: no timer fallback, window events arrive)
            int fallback0 = EnemyAttackState.WindowFallbackCount, events0 = EnemyAttackState.WindowEventCount;
            EnemyCombatEvents.ResetStatics();
            ZombieEnemy ul = Dogur(EnemyTier.Ultra, Zemin(0f, 1.6f));
            yield return null;
            Assert.IsTrue(ul.Noticed, "Executioner oyuncuyu fark etti (menzil içi)");
            yield return Bekle(() => EnemyCombatEvents.Count(EnemyCombatEventKind.AttackCommit) >= 1, 10f, "Ultra commit gelmedi");
            yield return null;
            Assert.AreEqual(ZombieAttackStyle.Overhead, CommitStyle(ul.LifeId), "Ultra commit stili Overhead");
            if (ul.AttackAnimationDriven)
            {
                Assert.AreEqual(fallback0, EnemyAttackState.WindowFallbackCount, "Ultra overhead penceresi klip olayından açıldı (zamanlayıcı yedeği kullanılmadı)");
                Assert.Greater(EnemyAttackState.WindowEventCount, events0, "hit_window olayları geldi");
            }
            Debug.Log("[DD-OBS] wp052.beats windowEvents=" + (EnemyAttackState.WindowEventCount - events0) + " fallbacks=" + (EnemyAttackState.WindowFallbackCount - fallback0) + " driven=" + ul.AttackAnimationDriven + " skinned=" + skinned + " noticed=" + ul.Noticed);
            EnemyFactory.ClearAll();
        }

        // ================================================================= WP-032C reactions on the live body (S-03 hooks)

        private static bool InState(Animator a, int layer, string stateName)
        {
            AnimatorStateInfo cur = a.GetCurrentAnimatorStateInfo(layer), next = a.GetNextAnimatorStateInfo(layer);
            return cur.IsName(stateName) || (a.IsInTransition(layer) && next.IsName(stateName));
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Tepkiler_SpecialSendelemesiStumble_ZirhKirilmasiBolge_TierOlumleri()
        {
            yield return RunSahnesiniHazirla();
            ZombieEnemy sp = Dogur(EnemyTier.Special, Zemin(-2f, 7f));
            ZombieEnemy el = Dogur(EnemyTier.Elite, Zemin(0f, 7f));
            ZombieEnemy ul = Dogur(EnemyTier.Ultra, Zemin(2f, 7f));
            yield return null;
            var look = (ZombieAppearance)el.VersionVisual;
            bool skinned = look.IsSkinned && look.Skinned != null && look.Skinned.Authored;
            if (!skinned) { Debug.Log("[DD-OBS] wp052.reactions block body — driven states not exercised"); EnemyFactory.ClearAll(); yield break; }

            // Special B: a stagger-strength hit → the Runner stumble (base layer) instead of the upper-body hit
            int stumble0 = sp.Animator.StumbleCount;
            sp.ApplyHit(new DamageHit { Amount = 30f, Region = BodyRegion.UpperBody, Point = sp.transform.position + Vector3.up * 1.1f, Direction = -sp.transform.forward, ImpactForce = DamageForce.ShotgunPellet * 8f, Source = DamageSourceKind.Bullet });
            yield return null;
            Animator spA = ((ZombieAppearance)sp.VersionVisual).Skinned.Animator;
            bool staggered = sp.Animator.IsStaggering;
            if (staggered)
            {
                Assert.Greater(sp.Animator.StumbleCount, stumble0, "Special sendelemesi stumble kancasını çağırır");
                Assert.IsTrue(InState(spA, CharacterAnimatorContract.BaseLayer, CharacterAnimatorContract.StumbleState), "Runner Stumble durumunda");
            }
            Debug.Log("[DD-OBS] wp052.reactions special staggered=" + staggered + " stumbles=" + (sp.Animator.StumbleCount - stumble0));

            // armour break by region: dō → ArmorBreak_torso (base) and the WP-040 hold
            el.FitArmorComposition(false, true, false);
            el.Armor.Equip(ArmorSlot.Body, 5f);
            el.ApplyHit(new DamageHit { Amount = 1000f, Region = BodyRegion.UpperBody, Point = el.transform.position + Vector3.up * 1.1f, Direction = -el.transform.forward, ImpactForce = DamageForce.RifleBullet, Source = DamageSourceKind.Bullet });
            yield return null;
            Assert.IsFalse(el.Armor.Protects(ArmorSlot.Body));
            Animator elA = look.Skinned.Animator;
            if (look.HasArmor)
            {
                Assert.IsTrue(InState(elA, CharacterAnimatorContract.BaseLayer, CharacterAnimatorContract.ArmorBreakState + "_1"), "dō kırılması ArmorBreak_1 (torso) durumu");
                Assert.IsTrue(look.Skinned.IsHeavyStaggering, "kırılma tutuşu (WP-040)");
            }

            // tier deaths: Ultra → death_ultra (7), Special → special_A/B (5/6); the Elite keeps an M1 style (0..4)
            ul.Kill(); sp.Kill(); el.Kill();
            yield return null;
            Assert.AreEqual(7, ul.Animator.LastDeathStyleIndex, "Executioner ölümü death_ultra");
            Assert.That(sp.Animator.LastDeathStyleIndex, Is.InRange(5, 6), "Runner ölümü special_A/B");
            Assert.That(el.Animator.LastDeathStyleIndex, Is.InRange(0, 4), "Guard M1 stili (side_R dahil)");
            Debug.Log("[DD-OBS] wp052.reactions deaths ultra=" + ul.Animator.LastDeathStyleIndex + " special=" + sp.Animator.LastDeathStyleIndex + " elite=" + el.Animator.LastDeathStyleIndex);
            EnemyFactory.ClearAll();
        }
    }
}
