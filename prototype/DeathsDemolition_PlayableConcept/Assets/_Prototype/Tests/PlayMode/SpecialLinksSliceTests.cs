using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-069 × WP-077 §2.4 on the live scene: a Special picked up names the run tree's link slots (OnSpecialPickedUp,
    /// nothing else calls it at run time) and opens its window; Storm refills the parked classic magazine per Raiju kill;
    /// Slick floor adds 15 % to every hit on a body in gel; Gel harvest pays +1 gold for a death in gel (GoldService.KillBonus).
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class SpecialLinksSliceTests
    {
        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            SpecialLinks.OwnedOverride = null;
            SpecialLinks.CloseAll();
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            RunFlowClock.ClearHolds();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            SpecialLinks.EnsureHooked();
            SpecialLinks.CloseAll();
        }

        private ZombieEnemy Spawn(EnemyTier tier, Vector3 at)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, 1, at, Quaternion.identity);
            Assert.IsNotNull(z, tier + " doğmadı");
            return z;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Alim_BagSlotlariniAdlandirir_Firtina_SarjoruDoldurur()
        {
            yield return Kur();
            RunTreeState tree = RunDirector.Instance.Shop.Tree;
            ClassicWeaponRuntime classic = _s.Controller.Classic;
            for (int i = 0; i < 12; i++) classic.ConsumeShot();   // a part-spent magazine to refill
            int ammo0 = classic.Ammo;

            _s.Controller.ActivateSpecialWeapon(SpecialWeaponId.Raiju);
            yield return null;
            Assert.IsTrue(SpecialLinks.IsOpen(SpecialWeaponId.Raiju));
            Assert.AreEqual(SpecialWeaponKeys.Raiju, tree.LinkedSpecial, "ilk Special bağ slotlarını adlandırır");

            SpecialLinks.OwnedOverride = e => e == RunTreeEffects.LinkArcStorm;
            int expected = Mathf.Max(1, Mathf.FloorToInt(RunTreeCatalog.FindLink(SpecialWeaponKeys.Raiju, 2).effectValue * classic.MagazineSize));
            Assert.AreEqual(expected, SpecialLinks.OnRaijuKill(), "Raiju öldürmesi başına şarjörün %10'u");
            Assert.AreEqual(ammo0 + expected, classic.Ammo, "park edilmiş şarjöre");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator KayganZemin_JeldekiGovdeyeYuzde15_JelHasadi_Arti1()
        {
            yield return Kur();
            ZombieEnemy z = Spawn(EnemyTier.Ultra, _s.Zemin(0f, 9f));
            z.FitArmorComposition(false, false, false);
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual((System.Func<ZombieEnemy, float>)SpecialLinks.SlickScale, ZombieEnemy.IncomingDamageScale, "kanca kurulu");
            Assert.AreEqual((System.Func<EnemyDeathInfo, int>)SpecialLinks.HarvestBonus, GoldService.KillBonus, "altın kancası kurulu");

            var hit = new DamageHit { Amount = 10f, Region = BodyRegion.UpperBody, Point = z.transform.position + Vector3.up, Direction = Vector3.forward, ImpactForce = 0.1f, Source = DamageSourceKind.Bullet };
            float h0 = z.Health;
            DamageTransaction.Begin(); z.ApplyHit(in hit); DamageTransaction.End();
            Assert.AreEqual(10f, h0 - z.Health, 0.01f, "jelde değilken +%0");

            Assert.IsTrue(z.ApplyKnockdown(DamageTransaction.NextId(), 3f));
            yield return null;
            Assert.AreNotEqual(GelState.None, z.Gel);
            _s.Controller.ActivateSpecialWeapon(SpecialWeaponId.Namekuji);
            yield return null;
            SpecialLinks.OwnedOverride = e => e == RunTreeEffects.LinkGelSlickFloor || e == RunTreeEffects.LinkGelFreeze;

            float h1 = z.Health;
            DamageTransaction.Begin(); z.ApplyHit(in hit); DamageTransaction.End();
            Assert.AreEqual(11.5f, h1 - z.Health, 0.01f, "jelde +%15 (Kaygan Zemin)");

            EnemyDeathInfo death = default;
            bool died = false;
            System.Action<EnemyDeathInfo> onDied = i => { if (i.Owner == z.transform) { death = i; died = true; } };
            DamageEvents.EnemyDied += onDied;
            try
            {
                var lethal = hit;
                lethal.Amount = 9999f;
                DamageTransaction.Begin(); z.ApplyHit(in lethal); DamageTransaction.End();
                yield return null;
            }
            finally
            {
                DamageEvents.EnemyDied -= onDied;
            }

            Assert.IsTrue(died);
            Assert.AreEqual(1, SpecialLinks.HarvestBonus(death), "jelde ölüm +1 altın (Jel Hasadı)");
            SpecialLinks.OwnedOverride = e => false;
            Assert.AreEqual(0, SpecialLinks.HarvestBonus(death), "düğüm alınmadıysa yok");
        }
    }
}
