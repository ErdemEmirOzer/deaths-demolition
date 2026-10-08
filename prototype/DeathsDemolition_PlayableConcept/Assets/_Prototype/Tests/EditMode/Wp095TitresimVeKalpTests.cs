using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Feedback;
using DD.Prototype.Inputs;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-095 phase 2 (R3, C-00 K3 08.10) · the top-down rumble mix, the input-preference v4 migration, the first / third person
    /// path's unchanged numbers, the low-health heartbeat clock (his sözleşmesi §4.7) and the TD profile's rows (§3 / §4.0).
    /// </summary>
    [TestFixture]
    public sealed class Wp095TitresimVeKalpTests
    {
        [SetUp]
        public void SetUp()
        {
            FeelEvents.ResetForTests();
            FeelEvents.TestClock = 10f;
        }

        [TearDown]
        public void TearDown()
        {
            FeelEvents.ResetForTests();
            FeelEvents.Disarm();
            FeelProfileTD.SetActiveForTests(null);
        }

        // ---------------------------------------------------------------- mixer

        [Test]
        public void Mixer_StrongerWins_WeakerNeverCutsItShort()
        {
            var mix = new TopDownRumbleMixer();
            mix.Event(0.6f, 0.4f, 0.2f, 10f);
            mix.Event(0.2f, 0.1f, 0.5f, 10.05f);   // weaker: dropped, does not extend
            mix.Sample(10.1f, 0.15f, 1f, out float low, out float high);
            Assert.AreEqual(0.6f, low, 1e-5f);
            Assert.AreEqual(0.4f, high, 1e-5f);
            mix.Sample(10.21f, 0.15f, 1f, out low, out high);
            Assert.AreEqual(0f, low, 1e-5f, "the weaker request did not keep the motors running");

            mix.Event(0.3f, 0.3f, 0.2f, 11f);
            mix.Event(0.9f, 0.5f, 0.1f, 11.05f);   // stronger replaces
            mix.Sample(11.06f, 0.15f, 1f, out low, out high);
            Assert.AreEqual(0.9f, low, 1e-5f);
        }

        [Test]
        public void Mixer_SustainedFire_HoldsThenFades_EventRidesAboveIt_CeilingClamps()
        {
            var mix = new TopDownRumbleMixer();
            mix.SustainOn(0.15f, 0.35f);
            mix.Sample(10f, 0.15f, 1f, out float low, out float high);
            Assert.AreEqual(0.15f, low, 1e-5f);
            Assert.AreEqual(0.35f, high, 1e-5f);

            mix.Event(0.10f, 0.25f, 0.04f, 10f);   // a kill over sustained fire: +0.15 above the sustained level
            mix.Sample(10.01f, 0.15f, 1f, out low, out high);
            Assert.AreEqual(0.30f, low, 1e-5f);
            Assert.AreEqual(0.50f, high, 1e-5f);

            mix.SustainOff(0.06f, 10.1f);
            mix.Sample(10.13f, 0.15f, 1f, out low, out _);
            Assert.AreEqual(0.075f, low, 1e-4f, "half way through the 0.06 s fade");
            mix.Sample(10.2f, 0.15f, 1f, out low, out high);
            Assert.AreEqual(0f, low, 1e-6f);
            Assert.AreEqual(0f, high, 1e-6f);

            mix.Event(2f, 2f, 0.1f, 11f);
            mix.Sample(11.01f, 0.15f, 1f, out low, out high);
            Assert.AreEqual(1f, low, 1e-6f, "ceiling 1.0 (K3)");
        }

        [Test]
        public void Mixer_ScheduledSecondBeat_PlaysOnTime()
        {
            var mix = new TopDownRumbleMixer();
            mix.Event(0.18f, 0f, 0.05f, 10f);
            mix.Schedule(0.18f, 0.12f, 0f, 0.05f, 10f);
            mix.Sample(10.1f, 0.15f, 1f, out float low, out _);
            Assert.AreEqual(0f, low, 1e-6f, "between the beats");
            mix.Sample(10.19f, 0.15f, 1f, out low, out _);
            Assert.AreEqual(0.12f, low, 1e-5f, "the 'dub' 0.18 s later");
        }

        // ---------------------------------------------------------------- preferences (K3)

        [Test]
        public void Preferences_V3CeilingMigratesToTheV4Scale_FirstThirdPersonKeepsItsNumbers()
        {
            InputPreferences full = InputPreferences.FromJson("{\"Version\":3,\"StickDeadzone\":0.15,\"RumbleEnabled\":true,\"RumbleIntensity\":0.3,\"AimAssistLevel\":2}");
            InputPreferences half = InputPreferences.FromJson("{\"Version\":3,\"StickDeadzone\":0.15,\"RumbleEnabled\":true,\"RumbleIntensity\":0.15,\"AimAssistLevel\":2}");
            Assert.AreEqual(InputPreferences.CurrentVersion, full.Clamp().Version);
            Assert.AreEqual(0.7f, full.RumbleIntensity, 1e-5f, "v3 0.3 ('full') → the v4 default 0.7");
            Assert.AreEqual(0.35f, half.Clamp().RumbleIntensity, 1e-5f, "the same share of the default");
            Assert.AreEqual(0.7f, half.Copy().Clamp().RumbleIntensity * 2f, 1e-5f, "migrates once, not on every clamp");
            Assert.AreEqual(0.7f, new InputPreferences().RumbleIntensity, 1e-6f);

            Assert.AreEqual(0.3f, GamepadRumble.LegacyScale(0.7f), 1e-6f, "first / third person at the default: the WP-024 0.3 ceiling");
            Assert.AreEqual(0.15f, GamepadRumble.LegacyScale(0.35f), 1e-6f);
            Assert.AreEqual(0.3f, GamepadRumble.LegacyScale(1f), 1e-6f, "never above the WP-024 ceiling");
        }

        // ---------------------------------------------------------------- heartbeat (§4.7)

        [Test]
        public void Heartbeat_70BpmAtEntry_110AtTenPercent_NoneWhenFrozenOrOut()
        {
            var beats = new List<FeelHeartbeat>();
            FeelEvents.Heartbeat += b => beats.Add(b);

            FeelEvents.TickHeartbeat(10f, false);
            Assert.AreEqual(0, beats.Count, "not in low health");

            FeelEvents.UpdateLowHealth(30f, 100f, false);
            FeelEvents.TickHeartbeat(10f, false);
            Assert.AreEqual(1, beats.Count, "the first beat on entering");
            Assert.AreEqual(70f, beats[0].Bpm, 1e-3f);
            FeelEvents.TickHeartbeat(10.8f, false);
            Assert.AreEqual(1, beats.Count, "60 / 70 = 0.857 s");
            FeelEvents.TickHeartbeat(10.86f, false);
            Assert.AreEqual(2, beats.Count);

            FeelEvents.UpdateLowHealth(10f, 100f, false);
            Assert.AreEqual(110f, FeelEvents.HeartbeatBpm(0.10f), 1e-3f);
            FeelEvents.TickHeartbeat(20f, true);
            Assert.AreEqual(2, beats.Count, "frozen (pause / shop): no beat");
            FeelEvents.TickHeartbeat(20f, false);
            Assert.AreEqual(3, beats.Count);
            Assert.AreEqual(110f, beats[2].Bpm, 1e-3f);

            FeelEvents.UpdateLowHealth(40f, 100f, false);
            FeelEvents.TickHeartbeat(30f, false);
            Assert.AreEqual(3, beats.Count, "out of the band: silent");
        }

        // ---------------------------------------------------------------- profile (§3 / §4.0)

        [Test]
        public void Profile_ResourcesAsset_IsTheContractsDefaults_AndLoadsAsActive()
        {
            FeelProfileTD asset = Resources.Load<FeelProfileTD>(FeelProfileTD.ResourceName);
            Assert.IsNotNull(asset, "Resources/" + FeelProfileTD.ResourceName + " (FeelProfileTDAssetBuilder.Build)");
            FeelProfileTD defaults = ScriptableObject.CreateInstance<FeelProfileTD>();
            try
            {
                defaults.ResetToDefaults();
                Assert.AreEqual(defaults.weapons.Length, asset.weapons.Length);
                for (int i = 0; i < defaults.weapons.Length; i++)
                    Assert.AreEqual(JsonUtility.ToJson(defaults.weapons[i]), JsonUtility.ToJson(asset.weapons[i]), "weapon row " + i);
                Assert.AreEqual(defaults.events.Length, asset.events.Length);
                for (int i = 0; i < defaults.events.Length; i++)
                    Assert.AreEqual(JsonUtility.ToJson(defaults.events[i]), JsonUtility.ToJson(asset.events[i]), "event row " + i);
                // gt-f2 S3: every serialized field, not two of them
                Assert.AreEqual(JsonUtility.ToJson(defaults), JsonUtility.ToJson(asset), "FeelProfile_TD.asset = the code defaults (all fields)");
                FeelProfileTD.SetActiveForTests(null);
                Assert.AreSame(asset, FeelProfileTD.Active, "the Resources asset is the active profile");
            }
            finally
            {
                Object.DestroyImmediate(defaults);
            }
        }

        [Test]
        public void Profile_ThirteenWeaponRows_EveryClassicAndSpecialFound_EventRowsIndexedById()
        {
            FeelProfileTD profile = ScriptableObject.CreateInstance<FeelProfileTD>();
            try
            {
                profile.ResetToDefaults();
                Assert.AreEqual(13, profile.weapons.Length, "10 classics + 2 Specials + the reserved pistol");
                Assert.AreEqual(23, profile.events.Length);
                for (int i = 0; i < profile.events.Length; i++) Assert.AreEqual((FeelEventId)i, profile.events[i].id, "row " + i);

                foreach (ClassicWeaponId id in System.Enum.GetValues(typeof(ClassicWeaponId)))
                {
                    WeaponFeelTD row = profile.Weapon(id);
                    Assert.IsNotNull(row.label, id + " has a row");
                    Assert.AreNotEqual(FeelProfileTD.PistolLabel, row.label, id + " is not the reserved pistol row");
                    Assert.LessOrEqual(row.fireTraumaAdd, row.fireTraumaCap, id + " add ≤ cap");
                    Assert.LessOrEqual(row.recoilBackMeters, 0.15f, id + " within the WP-087 arm channel");
                }

                foreach (SpecialWeaponId id in System.Enum.GetValues(typeof(SpecialWeaponId)))
                    Assert.IsTrue(profile.Weapon(id).special, id + " has its Special row");

                Assert.AreEqual("ar_1", profile.Weapon(ClassicWeaponId.AssaultRifle).label);
                Assert.AreEqual("rl_1", profile.Weapon(ClassicWeaponId.Rl1).label);
                foreach (ClassicWeaponId id in System.Enum.GetValues(typeof(ClassicWeaponId)))
                {
                    bool single = id == ClassicWeaponId.Ar2 || id == ClassicWeaponId.CombatShotgun || id == ClassicWeaponId.Sg2 || id == ClassicWeaponId.Sr1;
                    Assert.AreEqual(single, profile.Weapon(id).singleShotClass, id + " tek-atış sınıfı (§4.0 / §4.3)");
                }

                Assert.AreEqual(2, profile.critKillGlobalFrames);
                Assert.AreEqual(4, profile.Event(FeelEventId.LastZombie).globalStopFrames);
                Assert.AreEqual(0, profile.Event(FeelEventId.Kill).globalStopFrames, "a normal kill never stops the world");
                Assert.AreEqual(0.30f, profile.Thresholds().LowHealthEnter, 1e-6f);
                Assert.AreEqual(0.38f, profile.Thresholds().LowHealthExit, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
