using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B0 · özellik bazlı ağacın donmuş sözleşmesi: nötr TreeStats "ağaç yok" demek (×1, +0, kapalı),
    /// enum değerleri yalnız sona eklenir (int olarak kaydedilir), dal kademe paketi, korkuluk tutarlılığı,
    /// TreeRuntime yayın/test geçersizleştirme ve olay yüzeyi.
    /// </summary>
    [TestFixture]
    public sealed class TreeContractV2Tests
    {
        [TearDown]
        public void TearDown()
        {
            TreeRuntime.ClearTestStats();
            TreeRuntime.Reset();
            TreeEvents.ResetForTests();
        }

        [Test]
        public void Neutral_IsNoTree()
        {
            TreeStats n = TreeStats.Neutral;
            Assert.AreEqual(1f, n.DamageScale);
            Assert.AreEqual(1f, n.ProjectileScale);
            Assert.AreEqual(0, n.ExtraShots);
            Assert.AreEqual(1f, n.PierceKeep);
            Assert.AreEqual(1f, n.MagazineScale);
            Assert.AreEqual(1f, n.ReloadScale);
            Assert.AreEqual(1f, n.FireRateScale);
            Assert.AreEqual(1f, n.MoveScale);
            Assert.IsFalse(n.FullAuto);
            Assert.AreEqual(1f, n.FullAutoRpmScale);
            Assert.AreEqual(0, n.FullAutoMagazine);
            Assert.AreEqual(TreeElement.None, n.Elements);
            Assert.AreEqual(TreeChaosCore.None, n.Core);
            Assert.AreEqual(0f, n.CritChance);
            Assert.AreEqual(0f, n.ChainChance);
            Assert.AreEqual(1f, n.MaxHealthScale);
            Assert.AreEqual(0f, n.ShieldShare);
            Assert.AreEqual(0f, n.DodgeChance);
            Assert.AreEqual(1f, n.GoldScale);
            Assert.AreEqual(1f, n.WallBuyScale);
            Assert.AreEqual(1f, n.AmmoBuyScale);
            Assert.AreEqual(1f, n.HearthDamageScale);
            Assert.AreEqual(TreeSynergy.None, n.Synergies);
            Assert.AreEqual(0, n.AuraLevel);
        }

        [Test]
        public void Enums_AppendOnly()
        {
            Assert.AreEqual(7, Enum.GetValues(typeof(TreeBranch)).Length);
            Assert.AreEqual(0, (int)TreeBranch.Shot);
            Assert.AreEqual(6, (int)TreeBranch.Economy);
            Assert.AreEqual(1 << 9, (int)TreeSynergy.GoldenGun);
            Assert.AreEqual(11, Enum.GetValues(typeof(TreeSynergy)).Length, "None + 10 synergies (design §3)");
            Assert.AreEqual(8, (int)TreeElement.Venom);
            Assert.AreEqual(24, (int)TreeProcKind.ClusterSplit);
            Assert.AreEqual(6, (int)TreeNodeState.AwakeningLocked);
            Assert.AreEqual(3, TreeMode.Count);
        }

        [Test]
        public void BranchTiers_PackAndClamp()
        {
            TreeStats s = TreeStats.Neutral;
            foreach (TreeBranch b in Enum.GetValues(typeof(TreeBranch))) s.SetBranchTier(b, (int)b % 6);
            foreach (TreeBranch b in Enum.GetValues(typeof(TreeBranch))) Assert.AreEqual((int)b % 6, s.BranchTier(b), b.ToString());
            s.SetBranchTier(TreeBranch.Chaos, 9);
            Assert.AreEqual(7, s.BranchTier(TreeBranch.Chaos));
            Assert.AreEqual((int)TreeBranch.Survival % 6, s.BranchTier(TreeBranch.Survival), "neighbour untouched");
        }

        [Test]
        public void Has_RequiresWholeFlag()
        {
            TreeStats s = TreeStats.Neutral;
            s.Elements = TreeElement.Fire | TreeElement.Ice;
            s.Synergies = TreeSynergy.Barrage;
            Assert.IsTrue(s.Has(TreeElement.Fire));
            Assert.IsFalse(s.Has(TreeElement.Shock));
            Assert.IsFalse(s.Has(TreeElement.None));
            Assert.IsTrue(s.Has(TreeSynergy.Barrage));
            Assert.IsFalse(s.Has(TreeSynergy.Barrage | TreeSynergy.PinballStorm));
        }

        [Test]
        public void Guardrails_Consistent()
        {
            Assert.LessOrEqual(TreeGuardrails.LiveProjectilesSoft, TreeGuardrails.LiveProjectilesHard);
            Assert.LessOrEqual(TreeGuardrails.TreeBlastsPerFrame, TreeGuardrails.TreeBlastsPerSecond);
            Assert.LessOrEqual(TreeGuardrails.TreeBlastsPerSecond, TreeGuardrails.TreeBlastQueue);
            Assert.LessOrEqual(TreeGuardrails.FireGroundsActive + TreeGuardrails.VenomCloudsActive, TreeGuardrails.AreaZones);
            Assert.LessOrEqual(TreeGuardrails.ExtraShotsMax + 1, TreeGuardrails.ShotsPerPull);
            Assert.AreEqual(0, TreeGuardrails.StatusSlots % TreeGuardrails.StatusPhases);
            Assert.LessOrEqual(TreeGuardrails.RocketAutoRpm / 60f * 3f, TreeGuardrails.LiveRockets, "≤ 3 s flight at 200 rpm fits the rocket cap");
            Assert.LessOrEqual(TreeGuardrails.DodgeChanceMax, 0.30f);
            Assert.LessOrEqual(TreeGuardrails.ChainDepth, 3);
        }

        [Test]
        public void Runtime_PublishTestOverrideAndReset()
        {
            int events = 0;
            TreeEvents.StatsChanged += () => events++;
            int v0 = TreeRuntime.Version;

            TreeStats s = TreeStats.Neutral;
            s.DamageScale = 1.15f;
            TreeRuntime.Publish(s);
            Assert.AreEqual(1.15f, TreeRuntime.Current.DamageScale);
            Assert.AreEqual(1, events);
            Assert.Greater(TreeRuntime.Version, v0);

            TreeStats t = TreeStats.Neutral;
            t.DamageScale = 3f;
            TreeRuntime.SetTestStats(t);
            Assert.AreEqual(3f, TreeRuntime.Current.DamageScale, "test override wins");
            TreeRuntime.ClearTestStats();
            Assert.AreEqual(1.15f, TreeRuntime.Current.DamageScale);

            TreeRuntime.SetMode(TreeMode.Demolition);
            Assert.AreEqual(TreeMode.Demolition, TreeRuntime.Mode);
            TreeRuntime.SetMode(7);
            Assert.AreEqual(TreeMode.Normal, TreeRuntime.Mode, "unknown mode falls back to Normal");

            TreeRuntime.Reset();
            Assert.AreEqual(1f, TreeRuntime.Current.DamageScale);
        }

        [Test]
        public void Events_RaiseAndReset()
        {
            TreeProcKind seen = TreeProcKind.None;
            TreeSynergy syn = TreeSynergy.None;
            TreeEvents.Proc += p => seen = p.Kind;
            TreeEvents.SynergyUnlocked += s => syn = s;
            TreeEvents.RaiseProc(new TreeProcInfo { Kind = TreeProcKind.MiniNuke, Point = Vector3.one, Radius = 5f });
            TreeEvents.RaiseSynergyUnlocked(TreeSynergy.Barrage);
            Assert.AreEqual(TreeProcKind.MiniNuke, seen);
            Assert.AreEqual(TreeSynergy.Barrage, syn);

            TreeEvents.ResetForTests();
            seen = TreeProcKind.None;
            TreeEvents.RaiseProc(new TreeProcInfo { Kind = TreeProcKind.Crit });
            Assert.AreEqual(TreeProcKind.None, seen, "listeners dropped");
        }

        [Test]
        public void WeaponProfile_PowerAndClass()
        {
            var rl = new TreeWeaponProfile
            {
                Id = ClassicWeaponId.Rl1, Key = "rl_1", FireMode = WeaponFireMode.Single,
                BaseRpm = 20f, BaseDamage = 110f, Pellets = 1, Magazine = 1, Projectile = ProjectileKind.Rocket
            };
            Assert.IsTrue(rl.IsRocket);
            Assert.IsTrue(rl.IsSlow);
            Assert.IsFalse(rl.IsAutomatic);
            Assert.AreEqual(20f / 60f * 110f, rl.BaseDps, 1e-4f);

            var sg = new TreeWeaponProfile { FireMode = WeaponFireMode.Single, BaseRpm = 72f, BaseDamage = 9f, Pellets = 8, Projectile = ProjectileKind.Pellet };
            Assert.AreEqual(72f / 60f * 9f * 8f, sg.BaseDps, 1e-4f);
            Assert.IsTrue(sg.IsSlow);

            var smg = new TreeWeaponProfile { FireMode = WeaponFireMode.Auto, BaseRpm = 900f, BaseDamage = 8f, Pellets = 0 };
            Assert.AreEqual(900f / 60f * 8f, smg.BaseDps, 1e-4f, "0 pellets counts as one");
            Assert.IsFalse(smg.IsSlow);
            Assert.IsTrue(smg.IsAutomatic);
        }

        [Test]
        public void Snapshot_DefaultsAreEmpty()
        {
            var snap = new TreeScreenSnapshot();
            Assert.AreEqual(0, snap.Nodes.Length);
            Assert.AreEqual(0, snap.Synergies.Length);
            Assert.AreEqual(-1, snap.TargetIndex);
            Assert.AreEqual(2, snap.ElementCap);
        }
    }
}
