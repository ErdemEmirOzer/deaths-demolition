using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
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
    /// WP-028 · The B/B/B version layer on real bodies in the Run scene: profile resolved per
    /// life, Special timer scales, silhouette slot per anchor, Ultra fit C, armour tendency
    /// on the spawn roll, and a clean second life from the pool.
    /// </summary>
    [Category("WP028")]
    public sealed class EnemyVersionBbbSliceTests
    {
        private const float KurulumZamanAsimi = 30f;
        private EnemySpawnDirector _director;
        private RunDirector _run;
        private PlayerRig _rig;
        private PrototypeConfig _config;

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
            _run = RunDirector.Instance;
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            _config = PrototypeConfig.Active;
            TierBehaviourProfileSet.ResetStatics();   // asset / code defaults
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            TierBehaviourProfileSet.ResetStatics();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private ZombieEnemy Dogur(EnemyTier tier, int round, int i)
        {
            Vector3 p = _rig.transform.position + new Vector3((i % 8) * 1.5f - 5f, 0f, 8f + (i / 8) * 1.5f);
            ZombieEnemy z = EnemyFactory.Spawn(tier, round, p, Quaternion.identity);
            Assert.IsNotNull(z, tier + " doğmadı");
            return z;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator HerTier_ProfilUygulanir_Siluet_Zamanlayici_FitC_IkinciYasamTemiz()
        {
            yield return RunSahnesiniHazirla();
            TierBehaviourProfileSet set = TierBehaviourProfileSet.Active;
            ZombieEnemy std = Dogur(EnemyTier.Standard, 8, 0), sp = Dogur(EnemyTier.Special, 8, 1), el = Dogur(EnemyTier.Elite, 8, 2), ul = Dogur(EnemyTier.Ultra, 8, 3);
            yield return null;

            Assert.IsTrue(std.VersionProfile.IsIdentity, "Standard: katman yok");
            Assert.IsTrue(std.Silhouette == null || !std.Silhouette.IsShown, "Standard siluet parçası göstermez");

            // Special: telegraph × 0.85, recovery × 1.25 (Stats + saldırı durumu), aktif değişmez; siluet parçası
            EnemyTierSettings s = _config.Enemies.Find(EnemyTier.Special);
            Assert.AreEqual(s.TelegraphTime * set.For(EnemyTier.Special).TelegraphScale, sp.Stats.TelegraphTime, 1e-4f);
            Assert.AreEqual(s.AttackRecoveryTime * set.For(EnemyTier.Special).RecoveryScale, sp.Stats.AttackRecoveryTime, 1e-4f);
            Assert.AreEqual(s.AttackActiveTime, sp.Stats.AttackActiveTime, 0f);
            Assert.AreEqual(s.MoveSpeed, sp.Stats.MoveSpeed, 1e-4f, "hız katmanı 1 → değişmez");
            Assert.IsNotNull(sp.Silhouette); Assert.IsTrue(sp.Silhouette.IsShown); Assert.AreEqual(set.For(EnemyTier.Special).SilhouetteAnchor, sp.Silhouette.Anchor);
            Assert.IsNull(sp.Silhouette.GetComponent<Collider>(), "siluet parçası hitbox değil (DEC-029)");

            // Elite: siluet omuz; zamanlayıcı değişmez
            Assert.IsNotNull(el.Silhouette); Assert.IsTrue(el.Silhouette.IsShown); Assert.AreEqual(TierSilhouetteAnchor.Shoulder, el.Silhouette.Anchor);
            Assert.AreEqual(_config.Enemies.Find(EnemyTier.Elite).TelegraphTime, el.Stats.TelegraphTime, 1e-5f);

            // Ultra: siluet baş; skinned gövdede fit C
            Assert.IsNotNull(ul.Silhouette); Assert.IsTrue(ul.Silhouette.IsShown); Assert.AreEqual(TierSilhouetteAnchor.Head, ul.Silhouette.Anchor);
            var ulLook = (ZombieAppearance)ul.VersionVisual;
            if (ulLook.IsSkinned) Assert.AreEqual("C", ulLook.Look.fit, "Ultra B: fit C");
            Debug.Log("[DD-TEST] wp028.bodies skinned=" + ulLook.IsSkinned + " ultraFit=" + ulLook.Look.fit + " spTelegraph=" + sp.Stats.TelegraphTime + " spRecovery=" + sp.Stats.AttackRecoveryTime +
                      " silhouettes=" + sp.Silhouette.Anchor + "/" + el.Silhouette.Anchor + "/" + ul.Silhouette.Anchor + " ultraParent=" + ul.Silhouette.transform.parent.name);

            // İkinci yaşam: Ultra → havuz → Standard: profil null'dan kimliğe, siluet gizli, fit seed'in
            ul.Remove();
            yield return null;
            Assert.IsNull(ul.VersionProfile, "havuzdaki gövde profil taşımaz");
            Assert.IsFalse(ul.Silhouette.IsShown, "havuzdaki gövde siluet parçasını gizler");
            ZombieEnemy again = Dogur(EnemyTier.Standard, 1, 3);
            yield return null;
            Assert.IsTrue(again.VersionProfile.IsIdentity);
            Assert.IsTrue(again.Silhouette == null || !again.Silhouette.IsShown);
            Debug.Log("[DD-TEST] wp028.pool sameBody=" + ReferenceEquals(ul, again));
            EnemyFactory.ClearAll();
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator ZirhEgilimi_GercekRoll_EliteDo_UltraUcBolge()
        {
            yield return RunSahnesiniHazirla();
            int n = 40, eliteBody = 0, ultraThree = 0, stdBodyNoFloor = 0;
            var elites = new ZombieEnemy[n];
            for (int i = 0; i < n; i++) elites[i] = Dogur(EnemyTier.Elite, 6, i);
            yield return null;
            for (int i = 0; i < n; i++) { if (elites[i].ArmorVisualStateOf(ArmorSlot.Body) == ArmorVisualState.Present) eliteBody++; elites[i].Remove(); }
            yield return null;
            var ultras = new ZombieEnemy[n];
            for (int i = 0; i < n; i++) ultras[i] = Dogur(EnemyTier.Ultra, 8, i);
            yield return null;
            for (int i = 0; i < n; i++)
            {
                if (ultras[i].ArmorVisualStateOf(ArmorSlot.Helmet) == ArmorVisualState.Present && ultras[i].ArmorVisualStateOf(ArmorSlot.Body) == ArmorVisualState.Present && ultras[i].ArmorVisualStateOf(ArmorSlot.Leg) == ArmorVisualState.Present) ultraThree++;
                ultras[i].Remove();
            }

            yield return null;
            Debug.Log("[DD-TEST] wp028.armour eliteBody=" + eliteBody + "/" + n + " ultraThree=" + ultraThree + "/" + n + " baseEliteBodyR6=" + _config.ArmorChance(EnemyTier.Elite, ArmorSlot.Body, 6));
            // floor 0.85 → 40'ta beklenen ≥ 34, sd 2.3 → ≥ 28; Ultra 0.97³ ≈ 0.913 → beklenen 36.5, sd 1.8 → ≥ 30
            Assert.GreaterOrEqual(eliteBody, 28, "Elite dō eğilimi gerçek roll'da");
            Assert.GreaterOrEqual(ultraThree, 30, "Ultra üç bölge eğilimi gerçek roll'da");
            Assert.AreEqual(0, stdBodyNoFloor);
            EnemyFactory.ClearAll();
        }
    }
}
