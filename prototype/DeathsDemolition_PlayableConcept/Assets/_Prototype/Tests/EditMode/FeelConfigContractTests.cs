using DD.Prototype.Core.Config;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    [TestFixture]
    [Category("WP062")]
    public sealed class FeelConfigContractTests
    {
        private int _frame;

        [SetUp]
        public void SetUp()
        {
            FeelRuntime.ResetForTests();
            FeelRuntime.Initialize(null);
            _frame = 62000;
        }

        [TearDown]
        public void TearDown() => FeelRuntime.ResetForTests();

        [Test]
        public void Baseline_HasSixWeapons_ElevenImpacts_AndOnlyTwoLocalStops()
        {
            FeelConfigData data = FeelConfig.CreateDefaults();
            Assert.AreEqual(6, data.weapons.Length);
            Assert.AreEqual(11, data.impacts.Length);
            for (int i = 0; i < data.impacts.Length; i++)
            {
                ImpactFeelRow row = data.impacts[i];
                float expected = row.id == ImpactFeelId.ArmorBreak ? .035f :
                    row.id == ImpactFeelId.HeavyDeath ? .04f : 0f;
                Assert.AreEqual(expected, row.hitStopSeconds, 1e-6f, row.id.ToString());
            }
            Assert.AreEqual(0f, data.impacts[(int)ImpactFeelId.OfudaCollapse].hitStopSeconds);
        }

        [Test]
        public void Json_IsCanonicalAtomic_AndStrict()
        {
            string json = FeelProfileCodec.Export(FeelConfig.CreateDefaults(), "wp062", "22c8ead");
            Assert.IsTrue(FeelRuntime.QueueJson(json, out string error), error);
            Assert.IsTrue(FeelRuntime.BeginFrame(++_frame));
            string hash = FeelRuntime.Current.ContentHash;
            Assert.AreEqual(64, hash.Length);

            ulong revision = FeelRuntime.Current.Revision;
            Assert.IsFalse(FeelRuntime.QueueJson(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), out error));
            Assert.That(error, Does.Contain("duplicate"));
            Assert.IsFalse(FeelRuntime.QueueJson(json.Replace("\"data\":{", "\"data\":{\"unknownKnob\":1,"), out error));
            Assert.That(error, Does.Contain("unknown"));
            Assert.AreEqual(revision, FeelRuntime.Current.Revision, "Rejected documents must not partially apply.");
        }

        [Test]
        public void LiveAndNextRun_CommitAtTheirDeclaredBoundary()
        {
            FeelConfigData data = FeelConfig.CreateDefaults();
            data.camera.followSmoothSeconds = .2f;
            data.camera.heightMeters = 18f;
            Assert.IsTrue(FeelRuntime.QueueAuthored(data, "boundary", "22c8ead", out string error), error);
            Assert.IsTrue(FeelRuntime.BeginFrame(++_frame));
            Assert.AreEqual(.2f, FeelRuntime.Current.Camera.followSmoothSeconds, 1e-6f);
            Assert.AreEqual(15f, FeelRuntime.Current.Camera.heightMeters, 1e-6f);
            Assert.IsTrue(FeelRuntime.Current.PendingNextRun);
            Assert.IsTrue(FeelRuntime.ApplyPendingNextRun());
            Assert.AreEqual(18f, FeelRuntime.Current.Camera.heightMeters, 1e-6f);
            Assert.IsFalse(FeelRuntime.Current.PendingNextRun);
        }

        [Test]
        public void Snapshot_ExportsOnlyImmutableValues_AndSourceMutationCannotChangeIt()
        {
            foreach (System.Reflection.PropertyInfo property in typeof(FeelSnapshot).GetProperties())
                Assert.IsTrue(property.PropertyType.IsValueType || property.PropertyType == typeof(string), property.Name);
            Assert.IsTrue(typeof(WeaponFeel).IsValueType);
            Assert.IsTrue(typeof(ImpactFeel).IsValueType);

            FeelConfigData data = FeelConfig.CreateDefaults();
            data.camera.followSmoothSeconds = .2f;
            int changed = 0;
            FeelSnapshot delivered = null;
            FeelRuntime.Changed += snapshot => { changed++; delivered = snapshot; };
            Assert.IsTrue(FeelRuntime.QueueAuthored(data, "immutable", "22c8ead", out string error), error);
            data.camera.followSmoothSeconds = .31f;
            data.weapons[0].shakeWeight = .99f;
            Assert.IsTrue(FeelRuntime.BeginFrame(++_frame));

            Assert.AreEqual(1, changed);
            Assert.AreSame(FeelRuntime.Current, delivered);
            Assert.AreEqual(.2f, delivered.Camera.followSmoothSeconds, 1e-6f);
            Assert.AreNotEqual(.99f, delivered.Weapon(WeaponFeelId.AssaultRifle).shakeWeight);
            ulong revision = delivered.Revision;
            string hash = delivered.ContentHash;
            data.camera.followSmoothSeconds = .1f;
            Assert.AreEqual(.2f, delivered.Camera.followSmoothSeconds, 1e-6f);
            Assert.AreEqual(revision, delivered.Revision);
            Assert.AreEqual(hash, delivered.ContentHash);
        }

        [Test]
        public void LiveAndNextRun_HashesDescribeTheirActualSeparateSnapshots()
        {
            FeelConfigData data = FeelConfig.CreateDefaults();
            data.camera.followSmoothSeconds = .2f;
            data.camera.heightMeters = 18f;
            string incomingHash = FeelProfileCodec.Hash(data);
            Assert.IsTrue(FeelRuntime.QueueAuthored(data, "hashes", "22c8ead", out string error), error);
            Assert.IsTrue(FeelRuntime.BeginFrame(++_frame));

            Assert.IsTrue(FeelProfileCodec.TryImport(FeelRuntime.ExportJson("live", "22c8ead"),
                out FeelProfileDocument live, out error), error);
            Assert.AreEqual(live.contentHash, FeelRuntime.Current.ContentHash);
            Assert.AreNotEqual(incomingHash, FeelRuntime.Current.ContentHash,
                "Live merge keeps the active N fields and therefore needs its own hash.");
            string liveHash = FeelRuntime.Current.ContentHash;

            Assert.IsTrue(FeelRuntime.ApplyPendingNextRun());
            Assert.IsTrue(FeelProfileCodec.TryImport(FeelRuntime.ExportJson("next", "22c8ead"),
                out FeelProfileDocument next, out error), error);
            Assert.AreEqual(next.contentHash, FeelRuntime.Current.ContentHash);
            Assert.AreEqual(incomingHash, FeelRuntime.Current.ContentHash);
            Assert.AreNotEqual(liveHash, FeelRuntime.Current.ContentHash);
        }

        [Test]
        public void BudgetNFields_MergeAndCompareByCategoryId_AfterReorder()
        {
            FeelConfigData data = FeelConfig.CreateDefaults();
            System.Array.Reverse(data.budgets.categories);
            BudgetCategoryFeelRow impact = System.Array.Find(data.budgets.categories, row => row.id == "Impact");
            impact.capacity = 31;
            Assert.IsTrue(FeelRuntime.QueueAuthored(data, "budget-order", "22c8ead", out string error), error);
            Assert.IsTrue(FeelRuntime.BeginFrame(++_frame));
            Assert.AreEqual(40, FeelRuntime.Current.Budgets.CapacityFor("Impact", -1));
            Assert.IsTrue(FeelRuntime.Current.PendingNextRun);

            Assert.IsTrue(FeelRuntime.ApplyPendingNextRun());
            Assert.AreEqual(31, FeelRuntime.Current.Budgets.CapacityFor("Impact", -1));
        }

        [Test]
        public void GlobalDisable_InFeelConfig_PreservesLegacyOffBehavior()
        {
            FeelConfigData data = FeelConfig.CreateDefaults();
            data.enabled = false;
            Assert.IsTrue(FeelRuntime.QueueAuthored(data, "disabled", "22c8ead", out string error), error);
            Assert.IsTrue(FeelRuntime.BeginFrame(++_frame));
            GameFeelSettings feel = GameFeelSettings.Active;

            Assert.IsFalse(feel.Enabled);
            Assert.AreEqual(0f, feel.HitStopArmorBreak);
            Assert.AreEqual(0f, feel.ShakePlayerDamageAmplitude);
            Assert.AreEqual(0f, feel.VignetteFlashAlpha);
            Assert.AreEqual(1f, feel.DeathSlowScale);
            Assert.AreEqual(0f, feel.DeathSlowTotalSeconds);
            Assert.AreEqual(0f, feel.DeathCameraPushIn);
            Assert.GreaterOrEqual(feel.DeathResultsDelaySeconds, .5f);
        }

        [Test]
        public void RevisionScenarios_DataOnlyScore_Is18Of20()
        {
            int passed = 0;
            for (int scenario = 1; scenario <= 18; scenario++)
            {
                FeelRuntime.ResetForTests(); FeelRuntime.Initialize(null);
                FeelConfigData data = FeelConfig.CreateDefaults();
                Mutate(scenario, data);
                string json = FeelProfileCodec.Export(data, "scenario-" + scenario, "22c8ead");
                Assert.IsTrue(FeelRuntime.QueueJson(json, out string error), "scenario " + scenario + ": " + error);
                Assert.IsTrue(FeelRuntime.BeginFrame(++_frame), "scenario " + scenario);
                if (FeelRuntime.HasPendingNextRun) Assert.IsTrue(FeelRuntime.ApplyPendingNextRun());
                AssertScenario(scenario, FeelRuntime.Current);
                passed++;
            }

            string baseline = FeelProfileCodec.Export(FeelConfig.CreateDefaults(), "baseline", "22c8ead");
            Assert.IsFalse(FeelRuntime.QueueJson(baseline.Replace("\"data\":{", "\"data\":{\"newEventFamily\":{},"), out _));
            Assert.IsFalse(FeelRuntime.QueueJson(baseline.Replace("\"data\":{", "\"data\":{\"newKernelSkeleton\":{},"), out _));
            Assert.AreEqual(18, passed, "WP-062 target is 18/20 data-only revisions.");
            TestContext.WriteLine("[DD-TEST] wp062.data_only=18/20 code_required=19,20");
        }

        private static void Mutate(int s, FeelConfigData d)
        {
            switch (s)
            {
                case 1: d.aimAssist.maxAngleDegrees = 5f; break;
                case 2: d.aimAssist.maxDistanceMeters = 14f; break;
                case 3: d.aimAssist.lightAngleShare = .4f; break;
                case 4: d.camera.followSmoothSeconds = .2f; break;
                case 5: d.camera.maxLookAheadMeters = 2f; break;
                case 6: d.camera.lookAheadSmoothSeconds = .25f; break;
                case 7: d.weapons[(int)WeaponFeelId.AssaultRifle].shakeWeight = .4f; break;
                case 8: d.impacts[(int)ImpactFeelId.ArmorBreak].shakeAmplitudeMeters = .08f; break;
                case 9: d.impacts[(int)ImpactFeelId.Explosion].shakeSeconds = .18f; break;
                case 10: d.shake.kernels.rattleFrequency = 30f; break;
                case 11: d.weapons[(int)WeaponFeelId.AssaultRifle].recoilKickMeters = .12f; break;
                case 12: d.weapons[(int)WeaponFeelId.AssaultRifle].recoilRecoverySeconds = .14f; break;
                case 13: d.weapons[(int)WeaponFeelId.AssaultRifle].rumbleFraction = .6f; break;
                case 14: d.impacts[(int)ImpactFeelId.ArmorBreak].hitStopSeconds = .03f; break;
                case 15: d.impacts[(int)ImpactFeelId.HeavyDeath].hitStopSeconds = .038f; break;
                case 16: d.damageNumbers.mergeWindowSeconds = .25f; break;
                case 17: d.damageNumbers.lifetimeSeconds = .75f; break;
                case 18: d.budgets.categories[0].capacity = 32; break;
            }
        }

        private static void AssertScenario(int s, FeelSnapshot x)
        {
            switch (s)
            {
                case 1: Assert.AreEqual(5f, x.AimAssist.maxAngleDegrees); break;
                case 2: Assert.AreEqual(14f, x.AimAssist.maxDistanceMeters); break;
                case 3: Assert.AreEqual(.4f, x.AimAssist.lightAngleShare); break;
                case 4: Assert.AreEqual(.2f, x.Camera.followSmoothSeconds); break;
                case 5: Assert.AreEqual(2f, x.Camera.maxLookAheadMeters); break;
                case 6: Assert.AreEqual(.25f, x.Camera.lookAheadSmoothSeconds); break;
                case 7: Assert.AreEqual(.4f, x.Weapon(WeaponFeelId.AssaultRifle).shakeWeight); break;
                case 8: Assert.AreEqual(.08f, x.Impact(ImpactFeelId.ArmorBreak).shakeAmplitudeMeters); break;
                case 9: Assert.AreEqual(.18f, x.Impact(ImpactFeelId.Explosion).shakeSeconds); break;
                case 10: Assert.AreEqual(30f, x.Shake.kernels.rattleFrequency); break;
                case 11: Assert.AreEqual(.12f, x.Weapon(WeaponFeelId.AssaultRifle).recoilKickMeters); break;
                case 12: Assert.AreEqual(.14f, x.Weapon(WeaponFeelId.AssaultRifle).recoilRecoverySeconds); break;
                case 13: Assert.AreEqual(.6f, x.Weapon(WeaponFeelId.AssaultRifle).rumbleFraction); break;
                case 14: Assert.AreEqual(.03f, x.Impact(ImpactFeelId.ArmorBreak).hitStopSeconds); break;
                case 15: Assert.AreEqual(.038f, x.Impact(ImpactFeelId.HeavyDeath).hitStopSeconds); break;
                case 16: Assert.AreEqual(.25f, x.DamageNumbers.mergeWindowSeconds); break;
                case 17: Assert.AreEqual(.75f, x.DamageNumbers.lifetimeSeconds); break;
                case 18:
                    Assert.AreEqual(32, x.Budgets.CapacityFor(
                        DD.Prototype.Feedback.FeedbackCategory.Impact.ToString(), -1));
                    break;
            }
        }

        /// <summary>
        /// WP-069 M3 (C-00 01.10, S-06 r21): the shotgun row's view-model rig kick is its own column — within the A4.2 FP budget
        /// (kick × pitchDegreesPerMeter × upShare / 0.15 ≤ 6.5°) — while the camera strength keeps the recoil column (0.34 m);
        /// the other rows fall back to their recoil column; the authored DD_FeelConfig matches the defaults.
        /// </summary>
        [Test]
        public void M3_PompaliRigTekmesi_ButcedeKameraGucuAyni_VarlikVarsayilanla()
        {
            FeelConfigData data = FeelConfig.CreateDefaults();
            WeaponFeel sg = FeelRuntime.Current.Weapon(WeaponFeelId.CombatShotgun);   // the live path (the authored asset)
            float rigPitch = sg.rigKickMeters * data.recoil.pitchDegreesPerMeter * sg.rigUpShare / 0.15f;
            Assert.LessOrEqual(rigPitch, 6.5f, "pompalı rig kalkışı A4.2 bütçesinde (" + rigPitch.ToString("0.0") + "°)");
            Assert.AreEqual(0.34f, sg.recoilKickMeters, 1e-5f, "kamera gücü sütunu değişmez");
            WeaponFeel ar = FeelRuntime.Current.Weapon(WeaponFeelId.AssaultRifle);
            Assert.AreEqual(ar.recoilKickMeters, ar.rigKickMeters, 1e-6f, "rig sütunu boş → recoil sütunu");
            Assert.AreEqual(ar.recoilRecoverySeconds, ar.rigRecoverySeconds, 1e-6f);
            Assert.AreEqual(ar.recoilUpShare, ar.rigUpShare, 1e-6f);

            FeelConfig asset = Resources.Load<FeelConfig>("DD_FeelConfig");
            Assert.IsNotNull(asset, "DD_FeelConfig yok");
            foreach (WeaponFeelRow d in data.weapons)
            {
                WeaponFeelRow a = System.Array.Find(asset.AuthoredData.weapons, r => r.id == d.id);
                Assert.IsNotNull(a, d.id.ToString());
                Assert.AreEqual(d.rigKickMeters, a.rigKickMeters, 1e-6f, d.id + " rig kick (varlık)");
                Assert.AreEqual(d.rigRecoverySeconds, a.rigRecoverySeconds, 1e-6f, d.id + " rig recovery (varlık)");
                Assert.AreEqual(d.rigUpShare, a.rigUpShare, 1e-6f, d.id + " rig upShare (varlık)");
            }
        }
    }
}
