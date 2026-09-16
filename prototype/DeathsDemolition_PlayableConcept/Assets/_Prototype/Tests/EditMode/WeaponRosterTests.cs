using System;
using System.Collections.Generic;
using DD.Prototype.Audio;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-034 · the classic weapon roster as data: loads, validates, branch sets follow
    /// the proposal principle (a), the shop gate, the default weapon, the unlock order,
    /// the AP-013 identifiers, A parity (numbers stay in PrototypeConfig), and the
    /// pellet branch contract on BuildShot (ADR-034-1).
    /// </summary>
    [TestFixture]
    [Category("WP034")]
    public sealed class WeaponRosterTests
    {
        private static PrototypeConfig Config => Resources.Load<PrototypeConfig>("PrototypeConfig");

        [TearDown]
        public void TearDown()
        {
            WeaponRoster.SetForTests(null);
            WeaponRoster.EnforceCoreBranchSets = true;
        }

        [Test]
        public void Roster_Yuklenir_Dogrulanir_ABC_Acik_D_VeriYalniz()
        {
            IReadOnlyList<ClassicWeaponDefinition> all = WeaponRoster.All;
            Assert.AreEqual(4, all.Count, "Roster 4 tanım (A/B/C + D) taşımalı.");
            CollectionAssert.IsEmpty(WeaponRoster.Validate(), "Roster doğrulaması hata verdi.");

            Assert.AreEqual(3, WeaponRoster.Enabled.Count, "A/B/C etkin.");
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, WeaponRoster.Enabled[0].Id);
            Assert.AreEqual(ClassicWeaponId.AutoSmg, WeaponRoster.Enabled[1].Id);
            Assert.AreEqual(ClassicWeaponId.CombatShotgun, WeaponRoster.Enabled[2].Id);

            ClassicWeaponDefinition d = WeaponRoster.FindByKey("D");
            Assert.IsNotNull(d);
            Assert.IsFalse(d.Enabled, "D kapalı gelmeli.");
            Assert.IsFalse(d.HasRuntimeId, "D'nin runtime kimliği yok (kullanıcı kararı sonrası).");
            Assert.AreEqual(WeaponTypeClass.HeavyPistol, d.TypeClass);
            foreach (ClassicWeaponDefinition e in WeaponRoster.Enabled)
            {
                Assert.AreNotEqual(WeaponTypeClass.HeavyPistol, e.TypeClass, "D runtime'a sızmamalı.");
            }
        }

        [Test]
        public void DalIlkesi_a_UcCekirdek_DortOrtak_PompaliChainRicochetTasimaz()
        {
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                Assert.AreEqual(3, d.CoreBranches.Length, d.RosterKey + ": 3 çekirdek dal.");
                foreach (UpgradeBranch b in d.CoreBranches)
                {
                    Assert.IsTrue(WeaponBranchContract.IsWeaponCore(b), d.RosterKey + ": " + b + " silah-çekirdek dalı değil.");
                }
            }

            ClassicWeaponDefinition c = WeaponRoster.Find(ClassicWeaponId.CombatShotgun);
            Assert.AreEqual(WeaponShotModel.Pellet, c.ShotModel);
            Assert.IsFalse(c.HasCoreBranch(UpgradeBranch.Chain), "Pompalıya Chain verilmez.");
            Assert.IsFalse(c.HasCoreBranch(UpgradeBranch.Ricochet), "Pompalıya Ricochet verilmez.");
            Assert.IsTrue(c.HasCoreBranch(UpgradeBranch.Piercing) && c.HasCoreBranch(UpgradeBranch.Explosion));

            // Proposal table: A Piercing/FireRate/ReloadSpeed · B Chain/Ricochet/FireRate · C Explosion/Piercing/ReloadSpeed.
            CollectionAssert.AreEquivalent(new[] { UpgradeBranch.Piercing, UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed }, WeaponRoster.Find(ClassicWeaponId.AssaultRifle).CoreBranches);
            CollectionAssert.AreEquivalent(new[] { UpgradeBranch.Chain, UpgradeBranch.Ricochet, UpgradeBranch.FireRate }, WeaponRoster.Find(ClassicWeaponId.AutoSmg).CoreBranches);
            CollectionAssert.AreEquivalent(new[] { UpgradeBranch.Explosion, UpgradeBranch.Piercing, UpgradeBranch.ReloadSpeed }, c.CoreBranches);

            // Shared player branches are exactly the four non-weapon branches; the two sets partition the enum.
            var all = new List<UpgradeBranch>((UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch)));
            foreach (UpgradeBranch b in all)
            {
                Assert.IsTrue(WeaponBranchContract.IsWeaponCore(b) ^ WeaponBranchContract.IsSharedPlayer(b), b + " tam olarak bir kümede olmalı.");
            }

            Assert.AreEqual(4, WeaponBranchContract.SharedPlayerBranches.Length);
        }

        [Test]
        public void DukkanKapisi_CekirdekVeOrtakDallar_Teklif_DigerleriDegil_KapaliykenHepsi()
        {
            Assert.IsTrue(WeaponRoster.IsBranchOffered(ClassicWeaponId.AssaultRifle, UpgradeBranch.Piercing));
            Assert.IsTrue(WeaponRoster.IsBranchOffered(ClassicWeaponId.AssaultRifle, UpgradeBranch.MovementSpeed), "Ortak oyuncu dalı her silahta.");
            Assert.IsFalse(WeaponRoster.IsBranchOffered(ClassicWeaponId.AssaultRifle, UpgradeBranch.Explosion), "Tüfek çekirdeğinde olmayan dal teklif edilmez.");
            Assert.IsFalse(WeaponRoster.IsBranchOffered(ClassicWeaponId.CombatShotgun, UpgradeBranch.Chain));
            Assert.IsTrue(WeaponRoster.IsBranchOffered(ClassicWeaponId.AutoSmg, UpgradeBranch.Chain));

            WeaponRoster.EnforceCoreBranchSets = false;
            Assert.IsTrue(WeaponRoster.IsBranchOffered(ClassicWeaponId.AssaultRifle, UpgradeBranch.Explosion), "Kapı kapalıyken WP-034 öncesi davranış.");
            WeaponRoster.EnforceCoreBranchSets = true;

            // The shop reads the gate through UpgradeCardCatalog.HasLiveConsumer.
            Func<UpgradeBranch, bool> previous = UpgradeCardCatalog.WeaponBranchGate;
            try
            {
                UpgradeCardCatalog.WeaponBranchGate = b => WeaponRoster.IsBranchOffered(ClassicWeaponId.AssaultRifle, b);
                Assert.IsFalse(UpgradeCardCatalog.HasLiveConsumer(UpgradeBranch.Chain), "Kapı HasLiveConsumer'a bağlı değil.");
                Assert.IsTrue(UpgradeCardCatalog.HasLiveConsumer(UpgradeBranch.FireRate));
                UpgradeCardCatalog.WeaponBranchGate = null;
                Assert.IsTrue(UpgradeCardCatalog.HasLiveConsumer(UpgradeBranch.Chain), "Kapı yokken her dal canlı.");
            }
            finally
            {
                UpgradeCardCatalog.WeaponBranchGate = previous;
            }
        }

        [Test]
        public void Varsayilan_A_AcilisSirasi_A1_B2_C3_SecimVerisi()
        {
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, WeaponRoster.DefaultWeapon, "DEC-037/039 varsayılan A.");
            PrototypeConfig config = Config;
            Assert.IsNotNull(config, "PrototypeConfig Resources'ta yok.");

            int previousLevel = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                int level = d.UnlockLevel(config);
                Assert.GreaterOrEqual(level, previousLevel, d.RosterKey + ": açılış sırası roster sırasıyla artmalı.");
                previousLevel = level;
            }

            Assert.AreEqual(1, WeaponRoster.Find(ClassicWeaponId.AssaultRifle).UnlockLevel(config), "A açık.");
            Assert.AreEqual(WeaponUnlockKind.Default, WeaponRoster.Find(ClassicWeaponId.AssaultRifle).UnlockKind);
            Assert.Greater(WeaponRoster.Find(ClassicWeaponId.AutoSmg).UnlockLevel(config), 1, "B profil seviyesiyle (DEC-008).");
            Assert.Greater(WeaponRoster.Find(ClassicWeaponId.CombatShotgun).UnlockLevel(config), WeaponRoster.Find(ClassicWeaponId.AutoSmg).UnlockLevel(config));

            // Selection source: enabled entries in order, default flagged once, D absent.
            IWeaponRosterSource src = WeaponRosterSource.Instance;
            IReadOnlyList<WeaponRosterEntry> entries = src.Entries;
            Assert.AreEqual(3, entries.Count);
            int defaults = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsDefault) defaults++;
                Assert.IsFalse(string.IsNullOrEmpty(entries[i].DisplayName));
                Assert.AreEqual(WeaponRoster.Enabled[i].Id, entries[i].Id);
            }

            Assert.AreEqual(1, defaults);
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, src.DefaultWeapon);
        }

        [Test]
        public void AP013_Kimlikleri_SoketVfxSfx_Tanimli()
        {
            foreach (ClassicWeaponDefinition d in WeaponRoster.All)
            {
                Assert.AreEqual("socket_weapon_r", d.SocketRoot, d.RosterKey + ": AP-006 soket adı.");
                Assert.AreEqual("Muzzle", d.MuzzleSocket, d.RosterKey + ": WeaponVisualRig muzzle işaretçisi.");
                Assert.AreEqual(3, d.AttachmentSockets.Length, d.RosterKey + ": çekirdek dal başına bir eklenti soketi.");
                Assert.IsTrue(Enum.IsDefined(typeof(AudioSoundId), d.FireSfx), d.RosterKey + ": FireSfx AudioSoundId değil: " + d.FireSfx);
                Assert.IsTrue(Enum.IsDefined(typeof(AudioSoundId), d.ReloadSfx), d.RosterKey + ": ReloadSfx AudioSoundId değil.");
                Assert.AreEqual("muzzle", d.MuzzleVfxFamily);
                Assert.AreEqual("impact", d.ImpactVfxFamily);
            }
        }

        /// <summary>A parity: the roster adds no numbers — the rifle's settings ARE the PrototypeConfig entry.</summary>
        [Test]
        public void A_Parite_SayilarPrototypeConfigte_KalirBirebir()
        {
            PrototypeConfig config = Config;
            ClassicWeaponSettings viaRoster = WeaponRoster.Find(ClassicWeaponId.AssaultRifle).Settings(config);
            ClassicWeaponSettings viaConfig = config.Weapons.Find(ClassicWeaponId.AssaultRifle);
            Assert.AreSame(viaConfig, viaRoster, "Roster ikinci bir sayı kaynağı açmamalı (tek kaynak: PrototypeConfig).");

            var runtime = new ClassicWeaponRuntime();
            runtime.Equip(viaConfig, null);
            ShotRequest shot = runtime.BuildShot(Vector3.zero, Vector3.forward);
            Assert.AreEqual(viaConfig.Damage, shot.Damage, 1e-6f);
            Assert.AreEqual(Mathf.Max(1f, viaConfig.Range), shot.Range, 1e-6f);
            Assert.AreEqual(DamageSourceKind.Bullet, shot.Source);
            Assert.AreEqual(1, runtime.PelletCount);
        }

        /// <summary>ADR-034-1 pellet contract: Piercing per pellet, Explosion normalized, Chain / Ricochet on pellet 0 only.</summary>
        [Test]
        public void PelletSozlesmesi_PiercingHerPellet_ExplosionNormalize_ChainRicochetTekAtis()
        {
            Assert.AreEqual(PelletBranchResolution.PerPellet, WeaponBranchContract.ResolutionOf(UpgradeBranch.Piercing));
            Assert.AreEqual(PelletBranchResolution.PelletNormalized, WeaponBranchContract.ResolutionOf(UpgradeBranch.Explosion));
            Assert.AreEqual(PelletBranchResolution.PerShot, WeaponBranchContract.ResolutionOf(UpgradeBranch.Chain));
            Assert.AreEqual(PelletBranchResolution.PerShot, WeaponBranchContract.ResolutionOf(UpgradeBranch.Ricochet));

            PrototypeConfig config = Config;
            ClassicWeaponSettings shotgun = config.Weapons.Find(ClassicWeaponId.CombatShotgun);
            Assert.Greater(shotgun.PelletCount, 1, "Öncül: pompalı çok saçmalı.");

            var modifiers = new WeaponModifierState();
            modifiers.AddTier(UpgradeBranch.Piercing, 1, config);
            modifiers.AddTier(UpgradeBranch.Explosion, 1, config);
            modifiers.AddTier(UpgradeBranch.Chain, 1, config);
            modifiers.AddTier(UpgradeBranch.Ricochet, 1, config);
            Assume.That(modifiers.ChainTargets, Is.GreaterThan(0), "Öncül: Chain tier 1 hedef veriyor.");
            Assume.That(modifiers.RicochetChance, Is.GreaterThan(0f), "Öncül: Ricochet tier 1 şans veriyor.");

            var runtime = new ClassicWeaponRuntime();
            runtime.Equip(shotgun, modifiers);
            int pellets = runtime.PelletCount;
            for (int i = 0; i < pellets; i++)
            {
                ShotRequest s = runtime.BuildShot(Vector3.zero, Vector3.forward, i);
                Assert.AreEqual(DamageSourceKind.Pellet, s.Source);
                Assert.AreEqual(modifiers.PierceTargets, s.PierceTargets, "Piercing her pellet'te (" + i + ").");
                Assert.AreEqual(modifiers.ExplosionChance / pellets, s.ExplosionChance, 1e-6f, "Explosion pellet-normalize (" + i + ").");
                if (i == 0)
                {
                    Assert.AreEqual(modifiers.ChainTargets, s.ChainTargets, "Chain ilk pellet'te.");
                    Assert.AreEqual(modifiers.RicochetChance, s.RicochetChance, 1e-6f, "Ricochet ilk pellet'te.");
                    Assert.AreEqual(modifiers.RicochetBounces, s.RicochetBounces);
                }
                else
                {
                    Assert.AreEqual(0, s.ChainTargets, "Chain pellet " + i + "'de olmamalı (atış başına).");
                    Assert.AreEqual(0f, s.RicochetChance, 1e-6f, "Ricochet pellet " + i + "'de olmamalı (atış başına).");
                    Assert.AreEqual(0, s.RicochetBounces);
                }
            }

            // Single-pellet weapon: pellet 0 carries everything, unchanged from WP-004.
            var rifle = new ClassicWeaponRuntime();
            rifle.Equip(config.Weapons.Find(ClassicWeaponId.AssaultRifle), modifiers);
            ShotRequest r = rifle.BuildShot(Vector3.zero, Vector3.forward);
            Assert.AreEqual(modifiers.ChainTargets, r.ChainTargets);
            Assert.AreEqual(modifiers.RicochetChance, r.RicochetChance, 1e-6f);
            Assert.AreEqual(modifiers.ExplosionChance, r.ExplosionChance, 1e-6f, "Tek mermide normalize yok.");
        }

        [Test]
        public void Dogrulama_HataliTanimlariYakalar()
        {
            var bad = ScriptableObject.CreateInstance<ClassicWeaponDefinition>();
            bad.name = "bad";
            bad.RosterKey = "X";
            bad.ShotModel = WeaponShotModel.Pellet;
            bad.CoreBranches = new[] { UpgradeBranch.Chain, UpgradeBranch.Chain, UpgradeBranch.MovementSpeed };
            bad.SocketRoot = "";
            var faults = new List<string>();
            Assert.IsFalse(bad.Validate(faults));
            Assert.IsTrue(faults.Exists(f => f.Contains("duplicate core branch")), "Çift dal yakalanmadı.");
            Assert.IsTrue(faults.Exists(f => f.Contains("not a weapon-core branch")), "Oyuncu dalı çekirdekte yakalanmadı.");
            Assert.IsTrue(faults.Exists(f => f.Contains("Chain or Ricochet")), "Pellet + Chain yakalanmadı.");
            Assert.IsTrue(faults.Exists(f => f.Contains("SocketRoot")), "Boş soket yakalanmadı.");

            WeaponRoster.SetForTests(new List<ClassicWeaponDefinition> { bad });
            Assert.IsTrue(WeaponRoster.Validate().Exists(f => f.Contains("no enabled definition is the Default")), "Varsayılan yokluğu yakalanmadı.");
            UnityEngine.Object.DestroyImmediate(bad);
        }
    }
}
