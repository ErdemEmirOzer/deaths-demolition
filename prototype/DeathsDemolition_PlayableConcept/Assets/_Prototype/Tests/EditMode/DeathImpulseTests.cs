using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B2 · DEC-096: the DeathImpulse guarantees (unit non-zero direction, force ≥ 0, speed ≥ 0) and the §3.3
    /// weapon-class classifier used until DamageHit carries ADR-069-1's class (debug kill → unknown).
    /// </summary>
    [Category("WP076")]
    public sealed class DeathImpulseTests
    {
        private static WeaponClass Sinif(DamageSourceKind s, float f)
        {
            Assert.IsTrue(WeaponClassifier.TryClassify(s, f, false, out WeaponClass c), s + " bilinir");
            return c;
        }

        [Test]
        public void Siniflandirici_KaynakVeKuvvet()
        {
            Assert.AreEqual(WeaponClass.Shotgun, Sinif(DamageSourceKind.Pellet, 0.85f));
            Assert.AreEqual(WeaponClass.Launcher, Sinif(DamageSourceKind.Explosion, 6f));
            Assert.AreEqual(WeaponClass.Special, Sinif(DamageSourceKind.Chain, 0.4f));
            Assert.AreEqual(WeaponClass.Sniper, Sinif(DamageSourceKind.Bullet, 3.2f));
            Assert.AreEqual(WeaponClass.Lmg, Sinif(DamageSourceKind.Bullet, 0.8f));
            Assert.AreEqual(WeaponClass.Smg, Sinif(DamageSourceKind.Bullet, 0.55f));
            Assert.AreEqual(WeaponClass.AssaultRifle, Sinif(DamageSourceKind.Bullet, 1.0f));
            Assert.AreEqual(WeaponClass.AssaultRifle, Sinif(DamageSourceKind.Ricochet, 1.0f));
            Assert.IsFalse(WeaponClassifier.TryClassify(DamageSourceKind.Bullet, 1f, true, out _), "debug Kill → bilinmez");
        }

        [Test]
        public void Build_YonAsla_Sifir_KuvvetHizNegatifDegil()
        {
            DeathImpulse a = DeathImpulse.Build(Vector3.zero, new Vector3(0f, 0f, 2f), -1f, DamageSourceKind.Bullet, false,
                BodyRegion.Head, new Vector3(1f, 2f, 3f), -0.5f, ZombieBodyState.Staggered);
            Assert.AreEqual(1f, a.Direction.magnitude, 1e-5f); Assert.AreEqual(1f, a.Direction.z, 1e-5f, "yedek: beden ileri");
            Assert.AreEqual(0f, a.Force); Assert.AreEqual(0f, a.BodySpeed);
            Assert.AreEqual(BodyRegion.Head, a.Region); Assert.AreEqual(ZombieBodyState.Staggered, a.State); Assert.AreEqual(new Vector3(1f, 2f, 3f), a.Point);

            DeathImpulse b = DeathImpulse.Build(new Vector3(3f, 0f, 4f), Vector3.forward, float.NaN, DamageSourceKind.Explosion, false,
                BodyRegion.UpperBody, Vector3.zero, 2f, ZombieBodyState.Standing);
            Assert.AreEqual(0.6f, b.Direction.x, 1e-5f); Assert.AreEqual(0f, b.Force, "NaN → 0");
            Assert.IsTrue(b.WeaponKnown); Assert.AreEqual(WeaponClass.Launcher, b.WeaponClass);

            DeathImpulse c = DeathImpulse.Build(Vector3.zero, Vector3.zero, 1f, DamageSourceKind.Bullet, true, BodyRegion.UpperBody, Vector3.zero, 0f, ZombieBodyState.Standing);
            Assert.AreEqual(Vector3.forward, c.Direction, "iki yön de sıfır → dünya ileri"); Assert.IsFalse(c.WeaponKnown);
        }
    }
}
