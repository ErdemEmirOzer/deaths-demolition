using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B3 (S-03 12.10 finding): without a tree build the zombie binding never binds — the damage-path reads return the
    /// neutral value, no status slot is taken and no TreeSystemsDriver GameObject is created (Normal untouched).
    /// </summary>
    [TestFixture]
    public sealed class TreeBindingNeutralTests
    {
        [SetUp]
        public void SetUp()
        {
            TreeRuntime.ClearTestStats();
            TreeRuntime.Reset();
        }

        [Test]
        public void NoTree_ReadsAreNeutral_NothingBindsOrSpawns()
        {
            Assert.IsFalse(TreeSystemsRunner.Needed(in TreeRuntime.Current), "neutral stats need no tree systems");
            Assert.AreEqual(1f, TreeZombieBinding.StatusDamageScale(null));
            Assert.IsFalse(TreeZombieBinding.IgnoresArmor(null));
            Assert.IsFalse(TreeZombieBinding.OnChainShock(null, Vector3.zero));
            Assert.AreEqual(TreeElement.None, TreeZombieBinding.OnWeaponHit(null, Vector3.zero, default));
            Assert.AreEqual(0, TreeStatusSystem.ActiveSlots, "no status slot taken");
            Assert.IsNull(Object.FindObjectOfType<TreeSystemsDriver>(), "no driver GameObject");
        }
    }
}
