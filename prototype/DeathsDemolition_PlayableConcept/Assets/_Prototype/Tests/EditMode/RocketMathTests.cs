using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-069 B3 · rl_1 Bohiya's numbers (DIRECTION §5, DEC-097 / DEC-099): the blast falloff (full to 1.8 m, zero at
    /// 5.0 m — 2.5 m 125, 3.0 m 100, 3.5 m 75, 4.0 m 50), the self damage (45 %, cap 72: 1.8 m 72, 3.0 m 45, 4.0 m 22.5,
    /// ≥ 5 m 0), the enabled definition with its rocket block, and the §6 branch reading (Blast / Cluster, no Chain / Ricochet).
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class RocketMathTests
    {
        [Test]
        public void Patlama_DogrusalDusus_VeKendineHasar()
        {
            WeaponExplosiveBlock e = Arsenal.Get(ClassicWeaponId.Rl1).explosive;
            Assert.AreEqual(160f, e.blastDamage, 1e-4f);
            Assert.AreEqual(1.8f, e.innerRadius, 1e-4f);
            Assert.AreEqual(5.0f, e.outerRadius, 1e-4f);

            float[] metres = { 0f, 1.8f, 2.5f, 3.0f, 3.5f, 4.0f, 5.0f, 7.0f };
            float[] damage = { 160f, 160f, 125f, 100f, 75f, 50f, 0f, 0f };
            for (int i = 0; i < metres.Length; i++)
            {
                Assert.AreEqual(damage[i], e.blastDamage * RocketImpacts.FalloffShare(metres[i], e.innerRadius, e.outerRadius, e.falloffExponent), 0.01f, metres[i] + " m");
            }

            float[] self = { 72f, 72f, 45f, 22.5f, 0f };
            float[] at = { 0.5f, 1.8f, 3.0f, 4.0f, 5.0f };
            for (int i = 0; i < at.Length; i++)
            {
                Assert.AreEqual(self[i], RocketImpacts.SelfDamageAt(at[i], e.blastDamage, e.innerRadius, e.outerRadius, e.selfDamageShare, e.selfDamageMax), 0.01f, "oyuncu " + at[i] + " m");
            }

            // A bigger blast (Blast tier) still never takes more than the cap from the player.
            Assert.AreEqual(72f, RocketImpacts.SelfDamageAt(0f, 400f, 2f, 6f, e.selfDamageShare, e.selfDamageMax), 1e-4f);
        }

        [Test]
        public void Rl1_Etkin_RoketBlogu_VeDallari()
        {
            WeaponDefinition d = Arsenal.Get(ClassicWeaponId.Rl1);
            Assert.IsTrue(d.enabled, "WP-069 B3: rl_1 etkin");
            Assert.AreEqual(ProjectileKind.Rocket, d.projectile.kind);
            Assert.AreEqual(110f, d.explosive.directHitDamage, 1e-4f);
            Assert.AreEqual(4.0f, d.explosive.armingDistance, 1e-4f);
            Assert.AreEqual(30f, d.explosive.dudDamage, 1e-4f);

            ProjectileLaunch launch = ProjectileLaunch.From(d, ClassicWeaponId.Rl1, 70f);
            Assert.AreEqual(24f, launch.Speed, 1e-4f, "ilk hız");
            Assert.AreEqual(60f, launch.RocketAcceleration, 1e-4f);
            Assert.AreEqual(46f, launch.RocketMaxSpeed, 1e-4f);
            Assert.AreEqual(0.6f, launch.RocketBoostSeconds, 1e-4f);
            Assert.AreEqual(3f, launch.Gravity, 1e-4f, "itkide yerçekimi");
            Assert.AreEqual(6f, launch.GravityCoast, 1e-4f, "sonra");
            Assert.AreEqual(2.2f, launch.MaxLifetime, 1e-4f, "kendini patlatma");

            Assert.AreEqual(ExplosionSemantics.Blast, d.upgrade.explosionSemantics);
            CollectionAssert.Contains(d.upgrade.coreBranches, UpgradeBranch.Cluster);
            CollectionAssert.DoesNotContain(d.upgrade.coreBranches, UpgradeBranch.Chain);
            CollectionAssert.DoesNotContain(d.upgrade.coreBranches, UpgradeBranch.Ricochet);

            // The runtime: Explosion never rolls a chance blast on the launcher; Cluster tiers become 2 / 3 / 4 bomblets.
            var mods = new WeaponModifierState();
            var rt = new ClassicWeaponRuntime();
            PrototypeConfig config = UnityEngine.Resources.Load<PrototypeConfig>("PrototypeConfig");
            Assert.IsNotNull(config);
            config.Weapons.RebuildClassicView();
            rt.Equip(config.Weapons.Find(ClassicWeaponId.Rl1), mods);
            Assert.IsTrue(rt.UsesBlastSemantics);
            mods.AddTier(UpgradeBranch.Explosion, 2);
            mods.AddTier(UpgradeBranch.Cluster, 1);
            Assert.AreEqual(0f, rt.BuildShot(UnityEngine.Vector3.zero, UnityEngine.Vector3.forward, 0).ExplosionChance, "şans patlaması yok");
            ProjectileLaunch l = rt.Launch(0);
            Assert.AreEqual(ProjectileKind.Rocket, l.Kind);
            Assert.AreEqual(2, l.BlastTier, "Blast kademesi");
            Assert.AreEqual(2, l.ClusterBomblets, "Cluster 1 → 2 bombacık");
        }
    }
}
