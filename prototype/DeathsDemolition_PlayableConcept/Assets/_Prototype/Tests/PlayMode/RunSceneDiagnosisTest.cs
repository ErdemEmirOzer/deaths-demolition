using System.Collections;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// GT · tanı testi. Duman testi "RunDirector oluşmadı" diye kaldı; bu test o iddiayı
    /// kod okuyarak değil ÖLÇEREK doğrular: Run sahnesine girer, oynanışın taşıyıcı
    /// sistemlerinin hangilerinin var olduğunu tek tek sayar ve raporlar.
    ///
    /// Kaldığında verdiği çıktı doğrudan hata paketidir.
    /// </summary>
    [TestFixture]
    public sealed class RunSceneDiagnosisTest
    {
        [UnityTest]
        [Category("Diag")]
        [Timeout(180000)]
        public IEnumerator RunSahnesindeOynanisSistemleriVarMi()
        {
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            // Dünya kurulumu, NavMesh bake'i ve UI kurulumu için cömert bir pencere.
            float baslangic = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - baslangic < 12f)
            {
                yield return null;
            }

            Assert.AreEqual(PrototypeScenes.Run, SceneManager.GetActiveScene().name,
                "Run sahnesi etkin değil.");

            bool runDirector = RunDirector.Instance != null;
            bool spawnDirector = EnemySpawnDirector.Instance != null;
            bool oyuncu = PlayerRegistry.Primary != null;
            bool silah = WeaponRegistry.ActiveController != null;
            bool hud = Object.FindObjectOfType<RunHudScreen>() != null;
            bool dunya = DD.Prototype.World.WorldBootstrap.Last.Root != null;
            bool dunyaOk = DD.Prototype.World.WorldBootstrap.Last.Ok;

            int sahneKok = SceneManager.GetActiveScene().rootCount;
            int davranis = Object.FindObjectsOfType<MonoBehaviour>(true).Length;

            var sb = new StringBuilder();
            sb.AppendLine("[DD-TEST] run.systems");
            sb.AppendLine("  WorldBootstrap.Last.Root : " + dunya + "  (Ok=" + dunyaOk + ")");
            sb.AppendLine("  RunDirector.Instance     : " + runDirector);
            sb.AppendLine("  EnemySpawnDirector.Inst. : " + spawnDirector);
            sb.AppendLine("  PlayerRegistry.Primary   : " + oyuncu);
            sb.AppendLine("  WeaponRegistry.Active    : " + silah);
            sb.AppendLine("  RunHudScreen             : " + hud);
            sb.AppendLine("  sahne kök nesne sayısı   : " + sahneKok);
            sb.AppendLine("  MonoBehaviour sayısı     : " + davranis);
            Debug.Log(sb.ToString());

            var eksik = new StringBuilder();
            if (!runDirector) eksik.AppendLine("  RunDirector YOK — tur akışı hiç başlamaz, Results'a geçiş olmaz.");
            if (!spawnDirector) eksik.AppendLine("  EnemySpawnDirector YOK — düşman doğmaz.");
            if (!oyuncu) eksik.AppendLine("  PlayerRig YOK — oyuncu doğmaz.");
            if (!silah) eksik.AppendLine("  WeaponController YOK — ateş edilemez.");
            if (!hud) eksik.AppendLine("  RunHudScreen YOK — HUD çizilmez.");
            if (!dunyaOk) eksik.AppendLine("  WorldBuilder kendi öz-denetiminden KALDI (Ok=false).");

            if (eksik.Length > 0)
            {
                Assert.Fail("RUN SAHNESİ EKSİK SİSTEMLERLE AÇILIYOR:\n" + eksik + "\n" + sb);
            }
        }
    }
}
