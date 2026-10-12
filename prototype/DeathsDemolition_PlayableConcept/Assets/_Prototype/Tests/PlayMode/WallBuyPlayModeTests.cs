using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-107 (R4, kullanıcı 11.10 not 8) · wall buy oyunda: Run sahnesi (V6), istasyonlar NavMesh'e oturur ve oyuncu
    /// başlangıcından yürünerek ulaşılır (roketatar en uzun yolda), her istasyonda silahın modeli durur. Satın alma akışı:
    /// başka silah tam fiyata alınır (silah değişir, şarjör ve o turun yedeği dolu, Prepare Run seçimi DEĞİŞMEZ), aynı silah
    /// dolu cephanede ücretsiz reddedilir, ateş edilince yarı fiyata yenilenir; altın yetmezse reddedilir; Special eldeyken
    /// reddedilir; uzaklaşınca istem kalkar; HUD istemi fiyatı yazar. Yerleşim ölçümü (istasyon, yol boyu) DD_WALLBUY_DIR'e
    /// (yoksa geçici önbelleğe) wallbuy-placement.json olarak yazılır - S-05'in yerleşim girdisi.
    /// </summary>
    [TestFixture]
    public sealed class WallBuyPlayModeTests
    {
        private PlayerRig _rig;

        private IEnumerator LoadRun()
        {
            LogAssert.ignoreFailingMessages = true;
            int previousScene = SceneManager.GetActiveScene().handle;   // an earlier test's Run scene must not satisfy the wait
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            float limit = Time.realtimeSinceStartup + 120f;
            while (!(SceneManager.GetActiveScene().handle != previousScene && SceneManager.GetActiveScene().name == PrototypeScenes.Run
                     && RunDirector.Instance != null && PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null
                     && GoldService.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting))
            {
                Assert.Less(Time.realtimeSinceStartup, limit, "Run sahnesi / Fighting fazı gelmedi");
                yield return null;
            }

            if (EnemySpawnDirector.Instance != null) { EnemySpawnDirector.Instance.StopRound(); EnemySpawnDirector.Instance.ClearAllEnemies(); }
            EnemyFactory.ClearAll();
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            limit = Time.realtimeSinceStartup + 30f;
            while (WallBuyDirector.Instance == null || WallBuyDirector.Instance.Stations.Count == 0)
            {
                Assert.Less(Time.realtimeSinceStartup, limit, "wall buy istasyonları kurulmadı");
                yield return null;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_rig != null && _rig.Health != null) _rig.Health.DiagnosisInvulnerable = false;
            _rig = null;
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        private void Stand(WallBuyStation station)
        {
            Vector3 front = station.transform.position + station.transform.forward * (station.FrontDepth + 0.9f);
            _rig.Teleport(front, Quaternion.LookRotation(-station.transform.forward));
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Istasyonlar_NavMesheOturur_BaslangictanYurunerekUlasilir_RoketatarEnUzakta()
        {
            yield return LoadRun();
            WallBuyDirector director = WallBuyDirector.Instance;
            WallBuyCatalog catalog = director.Catalog;
            Assert.AreEqual(catalog.Stations.Count, director.Stations.Count, "her katalog noktası bir istasyon");

            Assert.IsTrue(NavMesh.SamplePosition(_rig.transform.position, out NavMeshHit startHit, 2f, NavMesh.AllAreas), "oyuncu başlangıcı NavMesh'te");
            var path = new NavMeshPath();
            var json = new StringBuilder("{\n  \"world\": \"V6\",\n  \"playerStart\": [" + F(startHit.position.x) + ", " + F(startHit.position.z) + "],\n  \"stations\": [\n");
            float rocketPath = -1f, longestOther = 0f;
            var blocked = new StringBuilder();
            for (int i = 0; i < director.Stations.Count; i++)
            {
                WallBuyStation s = director.Stations[i];
                WallBuyCatalog.StationRow row = catalog.Stations.First(r => r.id == s.Id);
                Vector3 p = s.transform.position;
                Assert.IsTrue(NavMesh.SamplePosition(p, out NavMeshHit hit, 0.6f, NavMesh.AllAreas), s.Id + " NavMesh'te değil (" + p + ")");
                Vector3 approach = p + s.transform.forward * (s.FrontDepth + 0.9f);
                Assert.IsTrue(NavMesh.SamplePosition(approach, out NavMeshHit front, 1.2f, NavMesh.AllAreas), s.Id + " önünde durulacak yer yok");
                Assert.IsTrue(NavMesh.CalculatePath(startHit.position, front.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete,
                    s.Id + " başlangıçtan yürünerek ulaşılamıyor (" + path.status + ")");
                float length = 0f;
                for (int c = 1; c < path.corners.Length; c++) length += Vector3.Distance(path.corners[c - 1], path.corners[c]);
                if (s.Weapon == ClassicWeaponId.Rl1) rocketPath = length; else longestOther = Mathf.Max(longestOther, length);
                Assert.IsNotNull(s.Model, s.Id + " silah modeli yok");
                // WP-107B (AD 12.10 MUST 1/2): open sky over the station and its standing point, no body overlap, standing room.
                // (collected, asserted after the placement file is written: a failing station still leaves its measurement)
                string why = WallBuyDirector.BlockReason(s);
                if (why != null || s.SettledMetres < 0f) blocked.Append(s.Id).Append(": ").Append(why ?? "no clean point").Append("; ");
                float moved = new Vector2(p.x - row.x, p.z - row.z).magnitude;
                json.Append("    {\"id\": \"").Append(s.Id).Append("\", \"weapon\": \"").Append(s.WeaponKey).Append("\", \"price\": ").Append(catalog.PriceOf(s.Weapon))
                    .Append(", \"x\": ").Append(F(p.x)).Append(", \"y\": ").Append(F(p.y)).Append(", \"z\": ").Append(F(p.z))
                    .Append(", \"snapMetres\": ").Append(F(moved)).Append(", \"settledMetres\": ").Append(F(s.SettledMetres)).Append(", \"walkMetres\": ").Append(F(length))
                    .Append(", \"body\": ").Append(s.Body != null ? "true" : "false").Append(", \"decal\": ").Append(s.Decal != null ? "true" : "false")
                    .Append(", \"lantern\": ").Append(s.Lantern != null ? "true" : "false").Append(", \"blocked\": ").Append(why != null ? "\"" + why.Replace("\"", "'") + "\"" : "null")
                    .Append(i + 1 < director.Stations.Count ? "},\n" : "}\n");
            }

            json.Append("  ]\n}\n");
            string dir = Environment.GetEnvironmentVariable("DD_WALLBUY_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp107-wallbuy");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "wallbuy-placement.json"), json.ToString());
            Debug.Log("[DD-TEST] wp107.wallbuy placement dir=" + dir + " rocketWalk=" + F(rocketPath) + " longestOther=" + F(longestOther));
            Assert.AreEqual(0, blocked.Length, "açıkta olmayan istasyon (çatı / örtü / çakışma / durma yeri): " + blocked);
            Assert.Greater(rocketPath, longestOther, "kullanıcı: roketatara ulaşmak zor - en uzun yürüyüş onun");
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator SatinAl_DoluCephaneRed_YariFiyataYenile_YetersizAltin_Special_SecimDegismez()
        {
            yield return LoadRun();
            WallBuyDirector director = WallBuyDirector.Instance;
            WallBuyCatalog catalog = director.Catalog;
            WeaponController controller = WeaponRegistry.ActiveController;
            GoldWallet wallet = GoldService.Instance.Wallet;
            ClassicWeaponId start = controller.Classic.Id;
            ClassicWeaponId selected = WeaponRegistry.SelectedWeapon;
            int round = Mathf.Max(1, RunDirector.Instance.Round);
            HudWallBuyPrompt hud = UnityEngine.Object.FindObjectOfType<HudWallBuyPrompt>();
            Assert.IsNotNull(hud, "HUD wall buy istemi kurulmadı");

            WallBuyStation buy = director.Stations.First(s => s.Weapon != start && s.Weapon != ClassicWeaponId.Rl1);
            int price = catalog.PriceOf(buy.Weapon), refill = catalog.RefillPriceOf(buy.Weapon);
            Assert.AreEqual(GoldTransactionResult.Applied, wallet.Credit(price + refill + 7, GoldSource.EnemyPickup, wallet.NextTransactionId()));
            var outcomes = new List<WallBuyOutcome>();
            Action<WallBuyOutcome> listen = o => outcomes.Add(o);
            WallBuyEvents.Resolved += listen;
            try
            {
                // ---- far away: no prompt
                Assert.IsFalse(WallBuyEvents.CurrentPrompt.Visible && director.InReach == null);

                // ---- buy a different weapon
                Stand(buy);
                yield return Frames(3);
                Assert.AreEqual(RunPhase.Fighting, RunDirector.Instance.Phase, "tur sürüyor");
                WallBuyPrompt p = WallBuyEvents.CurrentPrompt;
                Assert.IsTrue(p.Visible, "istasyonun önünde istem yok");
                Assert.AreEqual(buy.Weapon, p.Weapon);
                Assert.AreEqual(WallBuyAction.Buy, p.Action);
                Assert.AreEqual(price, p.Cost);
                Assert.AreEqual(WallBuyBlock.None, p.Block);
                Assert.IsTrue(hud.Shown, "HUD istemi görünmüyor");
                StringAssert.Contains(price.ToString(CultureInfo.InvariantCulture), hud.Text);

                int balance = wallet.Balance, spent = wallet.SpentOnWallBuys;
                director.TestPress = true;
                yield return Frames(2);
                Assert.AreEqual(1, outcomes.Count);
                Assert.AreEqual(WallBuyResult.Bought, outcomes[0].Result);
                Assert.AreEqual(buy.Weapon, controller.Classic.Id, "silah değişmedi");
                Assert.AreEqual(selected, WeaponRegistry.SelectedWeapon, "wall buy Prepare Run seçimini değiştirmez");
                Assert.AreEqual(balance - price, wallet.Balance);
                Assert.AreEqual(spent + price, wallet.SpentOnWallBuys);
                Assert.AreEqual(controller.Classic.MagazineSize, controller.Classic.Ammo, "dolu şarjör");
                Assert.IsTrue(controller.IsClassicAmmoFull(round), "o turun yedeği");

                // ---- the same weapon with full ammo: refused, nothing taken
                yield return Frames(2);
                p = WallBuyEvents.CurrentPrompt;
                Assert.AreEqual(WallBuyAction.Refill, p.Action);
                Assert.AreEqual(refill, p.Cost);
                Assert.AreEqual(WallBuyBlock.AmmoFull, p.Block);
                balance = wallet.Balance;
                director.TestPress = true;
                yield return Frames(2);
                Assert.AreEqual(WallBuyResult.Denied, outcomes[1].Result);
                Assert.AreEqual(WallBuyBlock.AmmoFull, outcomes[1].Block);
                Assert.AreEqual(balance, wallet.Balance, "dolu cephaneye altın alınmaz");

                // ---- fire a few rounds: half-price refill
                for (int i = 0; i < 3; i++) controller.Classic.ConsumeShot();
                yield return Frames(2);
                Assert.AreEqual(WallBuyBlock.None, WallBuyEvents.CurrentPrompt.Block);
                director.TestPress = true;
                yield return Frames(2);
                Assert.AreEqual(WallBuyResult.Refilled, outcomes[2].Result);
                Assert.AreEqual(balance - refill, wallet.Balance);
                Assert.AreEqual(controller.Classic.MagazineSize, controller.Classic.Ammo);

                // ---- not enough gold at another station
                if (wallet.Balance > 0) Assert.AreEqual(GoldTransactionResult.Applied, wallet.TrySpend(wallet.Balance, GoldSource.UpgradePurchase, wallet.NextTransactionId()));
                WallBuyStation other = director.Stations.First(s => s.Weapon != buy.Weapon && s.Weapon != ClassicWeaponId.Rl1);
                Stand(other);
                yield return Frames(3);
                Assert.AreEqual(WallBuyBlock.NotEnoughGold, WallBuyEvents.CurrentPrompt.Block);
                StringAssert.Contains(UiStrings.Upper(UiStrings.Get("wallbuy.not_enough_gold", "Not enough gold")), hud.Text);
                director.TestPress = true;
                yield return Frames(2);
                Assert.AreEqual(WallBuyResult.Denied, outcomes[3].Result);
                Assert.AreEqual(WallBuyBlock.NotEnoughGold, outcomes[3].Block);
                Assert.AreEqual(buy.Weapon, controller.Classic.Id, "altın yetmeyince silah değişmez");

                // ---- a Special in hand blocks it
                Assert.AreEqual(GoldTransactionResult.Applied, wallet.Credit(catalog.PriceOf(other.Weapon), GoldSource.EnemyPickup, wallet.NextTransactionId()));
                controller.ActivateSpecialWeapon();
                yield return Frames(2);
                Assert.IsTrue(controller.SpecialWeaponActive, "Special açılmadı");
                Assert.AreEqual(WallBuyBlock.SpecialActive, WallBuyEvents.CurrentPrompt.Block);
                balance = wallet.Balance;
                director.TestPress = true;
                yield return Frames(2);
                Assert.AreEqual(WallBuyBlock.SpecialActive, outcomes[4].Block);
                Assert.AreEqual(balance, wallet.Balance);
                controller.CancelSpecialWeapon();
                yield return Frames(2);

                // ---- walk away: the prompt goes
                _rig.Teleport(new Vector3(0f, 0.1f, -3f), Quaternion.identity);
                yield return Frames(3);
                Assert.IsFalse(WallBuyEvents.CurrentPrompt.Visible, "uzakta istem kalmamalı");
                yield return Frames(12);
                Assert.IsFalse(hud.Shown);
            }
            finally
            {
                WallBuyEvents.Resolved -= listen;
                WeaponRegistry.SelectedWeapon = selected;
            }
        }

        /// <summary>
        /// WP-107B (AD 12.10 MUST 4/5): the HUD prompt in its five states - buy, refill, not enough gold, ammo full, a Special in
        /// hand. Every refusal shows an icon (≥ 20 px); the content sits PadX in from both chip edges (the AP-042 brackets end
        /// BracketInner in), so both inner gaps are ≥ 8 px and equal.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator HudIstemi_BesDurum_RedlerdeIkon_IcBosluklarSimetrik()
        {
            yield return LoadRun();
            HudWallBuyPrompt hud = UnityEngine.Object.FindObjectOfType<HudWallBuyPrompt>();
            Assert.IsNotNull(hud);
            _rig.Teleport(new Vector3(0f, 0.1f, -3f), Quaternion.identity);
            yield return Frames(3);
            WallBuyDirector director = WallBuyDirector.Instance;
            director.enabled = false;   // its per-frame "no station in reach" would overwrite the prompts published here (Instance clears in OnDisable)
            var states = new (WallBuyAction action, WallBuyBlock block, ClassicWeaponId weapon, string key, int cost)[]
            {
                (WallBuyAction.Buy, WallBuyBlock.None, ClassicWeaponId.Lmg2, "lmg_2", 800),
                (WallBuyAction.Refill, WallBuyBlock.None, ClassicWeaponId.AssaultRifle, "ar_1", 125),
                (WallBuyAction.Buy, WallBuyBlock.NotEnoughGold, ClassicWeaponId.Rl1, "rl_1", 2500),
                (WallBuyAction.Refill, WallBuyBlock.AmmoFull, ClassicWeaponId.AssaultRifle, "ar_1", 125),
                (WallBuyAction.Buy, WallBuyBlock.SpecialActive, ClassicWeaponId.Sr1, "sr_1", 650),
            };
            var log = new StringBuilder();
            foreach (var st in states)
            {
                WallBuyEvents.PublishPrompt(new WallBuyPrompt("WB_Test_" + st.block, st.weapon, st.key, new WallBuyQuote(st.action, st.cost, st.block), Vector3.zero));
                yield return Frames(2);
                Assert.IsTrue(hud.Shown, st.block + " gösterilmedi");
                if (st.block != WallBuyBlock.None)
                {
                    Assert.IsNotNull(hud.IconShown, st.block + ": ret ikonsuz");
                    Assert.GreaterOrEqual(HudWallBuyPrompt.IconSize, 20f);
                }

                float left = hud.ContentLeft - HudWallBuyPrompt.BracketInner, right = hud.ChipWidth - hud.ContentRight - HudWallBuyPrompt.BracketInner;
                log.Append(st.block).Append(' ').Append(st.action).Append(": gaps ").Append(F(left)).Append(" / ").Append(F(right)).Append(" chip ").Append(F(hud.ChipWidth)).Append(" '").Append(hud.Text).Append("'\n");
                Assert.GreaterOrEqual(left, 8f, st.block + " sol iç boşluk");
                Assert.GreaterOrEqual(right, 8f, st.block + " sağ iç boşluk");
                Assert.LessOrEqual(Mathf.Abs(left - right), 3f, st.block + " iç boşluklar simetrik değil");
            }

            Debug.Log("[DD-TEST] wp107b.hud.states\n" + log);
            WallBuyEvents.PublishPrompt(default);
            director.enabled = true;
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
