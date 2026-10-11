using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B3 · ağaç durumlarının gövde matematiği (TreeStatusMath): buz yığını ve tavanı, yavaşlatma çarpanı (yığınsızken
    /// tam 1), kademe payları (Elite donma payı, Ultra donmaz, ağır sersemletme payı, çekim payları; pay ≤ 0 = bağışık),
    /// süre uzatma kuralı (uzatır, yığmaz), donma beklemesi tutmanın sonundan sayılır, çekim merkezi aşmaz ve çekirdekte durur.
    /// </summary>
    [TestFixture]
    public sealed class TreeStatusMathTests
    {
        [Test]
        public void Yigin_TavanaKadar_NegatifEklemeYok()
        {
            Assert.AreEqual(1, TreeStatusMath.AddStacks(0, 1, 4));
            Assert.AreEqual(4, TreeStatusMath.AddStacks(3, 3, 4), "yavaş silah 3 yığın: tavan 4");
            Assert.AreEqual(2, TreeStatusMath.AddStacks(2, -5, 4));
            Assert.AreEqual(0, TreeStatusMath.AddStacks(3, 1, 0), "tavan kapalı → yığın yok");
        }

        [Test]
        public void Yavaslatma_YiginBasina_YigininsizTamBir()
        {
            Assert.AreEqual(1f, TreeStatusMath.IceSpeedFactor(0, 0.10f));
            Assert.AreEqual(1f, TreeStatusMath.IceSpeedFactor(3, 0f));
            Assert.AreEqual(0.9f, TreeStatusMath.IceSpeedFactor(1, 0.10f), 1e-6f);
            Assert.AreEqual(0.6f, TreeStatusMath.IceSpeedFactor(4, 0.10f), 1e-6f, "GD el_ice: 4 × %10");
            Assert.AreEqual(TreeStatusMath.MinSpeedFactor, TreeStatusMath.IceSpeedFactor(20, 0.10f), "taban koruması");
            Assert.AreEqual(0.5f, TreeStatusMath.UltraSlowFactor(0.5f), 1e-6f, "Ultra %50 yavaşlar");
            Assert.AreEqual(1f, TreeStatusMath.UltraSlowFactor(0f));
        }

        [Test]
        public void Donma_KademePayi_UltraDonmaz()
        {
            Assert.AreEqual(1f, TreeStatusMath.FreezeShare(EnemyTier.Standard, 0.5f));
            Assert.AreEqual(1f, TreeStatusMath.FreezeShare(EnemyTier.Special, 0.5f));
            Assert.AreEqual(0.5f, TreeStatusMath.FreezeShare(EnemyTier.Elite, 0.5f), "Elite 0,75 sn / 1,5 sn");
            Assert.AreEqual(0f, TreeStatusMath.FreezeShare(EnemyTier.Ultra, 0.5f));
            Assert.AreEqual(0f, TreeStatusMath.FreezeShare(EnemyTier.Elite, 0f), "pay 0 = bağışık, varsayılan yok");
        }

        [Test]
        public void Sersemletme_AgirKademeYarim()
        {
            Assert.AreEqual(1f, TreeStatusMath.StunShare(EnemyTier.Standard, 0.5f));
            Assert.AreEqual(1f, TreeStatusMath.StunShare(EnemyTier.Special, 0.5f));
            Assert.AreEqual(0.5f, TreeStatusMath.StunShare(EnemyTier.Elite, 0.5f));
            Assert.AreEqual(0.5f, TreeStatusMath.StunShare(EnemyTier.Ultra, 0.5f));
        }

        [Test]
        public void Cekim_KademePayi()
        {
            Assert.AreEqual(1f, TreeStatusMath.PullShare(EnemyTier.Standard, 0.5f, 0.2f));
            Assert.AreEqual(0.5f, TreeStatusMath.PullShare(EnemyTier.Special, 0.5f, 0.2f));
            Assert.AreEqual(0.2f, TreeStatusMath.PullShare(EnemyTier.Elite, 0.5f, 0.2f));
            Assert.AreEqual(0.2f, TreeStatusMath.PullShare(EnemyTier.Ultra, 0.5f, 0.2f));
        }

        [Test]
        public void Sure_UzatirYigmaz_BeklemeTutmaSonundan()
        {
            Assert.AreEqual(12f, TreeStatusMath.Extend(12f, 10f, 1f), "kalan daha uzun → değişmez");
            Assert.AreEqual(13f, TreeStatusMath.Extend(10.5f, 10f, 3f), "yeni daha uzun → uzar");
            Assert.AreEqual(10.5f, TreeStatusMath.Extend(10.5f, 10f, 0f));
            Assert.AreEqual(15.5f, TreeStatusMath.NextFreezeReady(11.5f, 4f), "4 sn bekleme donma bitince başlar");
            Assert.AreEqual(11.5f, TreeStatusMath.NextFreezeReady(11.5f, -1f));
        }

        [Test]
        public void Cekim_MerkeziAsmaz_CekirdekteDurur()
        {
            Vector3 v = TreeStatusMath.PullVelocity(new Vector3(3f, 2f, 0f), 5f, 1f, 1f / 60f);
            Assert.AreEqual(5f, v.magnitude, 1e-4f);
            Assert.AreEqual(0f, v.y, "düzlemsel");
            Assert.Greater(v.x, 0f, "merkeze doğru");

            Vector3 son = TreeStatusMath.PullVelocity(new Vector3(1.05f, 0f, 0f), 5f, 1f, 1f / 60f);
            Assert.LessOrEqual(son.magnitude * (1f / 60f), 0.05f + 1e-5f, "bir karede çekirdeği aşmaz");

            Assert.AreEqual(Vector3.zero, TreeStatusMath.PullVelocity(new Vector3(0.8f, 0f, 0f), 5f, 1f, 1f / 60f), "çekirdek içinde sıfır");
            Assert.AreEqual(Vector3.zero, TreeStatusMath.PullVelocity(new Vector3(3f, 0f, 0f), 0f, 1f, 1f / 60f));
        }

        [Test]
        public void Cekim_CekirdektenSonraBirMetredeDogrusalIncelir()
        {
            float dt = 1f / 60f;
            Assert.AreEqual(5f, TreeStatusMath.PullVelocity(new Vector3(2.0f + TreeStatusMath.PullTaperMetres, 0f, 0f), 5f, 2f, dt).magnitude, 1e-4f, "inceltme dışında tam hız");
            Assert.AreEqual(2.5f, TreeStatusMath.PullVelocity(new Vector3(2.5f, 0f, 0f), 5f, 2f, dt).magnitude, 1e-4f, "çekirdekten 0,5 m → yarı hız");
            Assert.AreEqual(1.25f, TreeStatusMath.PullVelocity(new Vector3(2.25f, 0f, 0f), 5f, 2f, dt).magnitude, 1e-4f, "0,25 m → çeyrek");
            Assert.AreEqual(0.5f, TreeStatusMath.PulledCrowdShare, "çekilen gövde kalabalık düzeltmesinin yarısını alır");
        }

        [Test]
        public void Donma_BeklemeDolmadan_YalnizIgnoreCooldownIleDonar()
        {
            float hazir = TreeStatusMath.NextFreezeReady(11.5f, 4f);   // donma 11,5'te bitti → 15,5'e dek bekleme
            Assert.IsFalse(TreeStatusMath.FreezeReady(13f, hazir, false), "bekleme dolmadan bayraksız donmaz");
            Assert.IsTrue(TreeStatusMath.FreezeReady(13f, hazir, true), "Frozen Gravity (ignoreCooldown) beklemeyi atlar");
            Assert.IsTrue(TreeStatusMath.FreezeReady(15.5f, hazir, false), "bekleme dolunca bayraksız da donar");
            // the tier rules stay with the flag: Elite × share, the Ultra never freezes
            Assert.AreEqual(0.5f, TreeStatusMath.FreezeShare(EnemyTier.Elite, 0.5f));
            Assert.AreEqual(0f, TreeStatusMath.FreezeShare(EnemyTier.Ultra, 0.5f));
        }
    }
}
