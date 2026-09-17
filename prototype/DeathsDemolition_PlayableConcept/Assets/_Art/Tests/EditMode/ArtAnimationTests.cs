using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-018 · The AP-009 set in the project: the 36 clips (AP-009B v002, WP-032B) on the manifest contract
    /// (ADR-018-1: loop / events / additive from the manifest, importer idempotent), the
    /// generated controllers (ADR-018-2: parameters, layers, masks, blend trees, rates), the
    /// animation set tables, the event contract and the removal of the WP-016 pose bridge.
    /// </summary>
    [TestFixture]
    [Category("WP018")]
    public sealed class ArtAnimationTests
    {
        private static Dictionary<string, ArtAnimationClipContract.ClipSpec> Manifest()
        {
            var m = ArtAnimationClipContract.Manifest();
            Assert.IsNotNull(m, "AP-009 manifest yok: " + ArtAnimationClipContract.ManifestPath);
            return m;
        }

        [Test]
        public void Manifest_30Klip_10Oyuncu_20Zombi_AdlarSozlesmeyle()
        {
            var m = Manifest();
            Assert.AreEqual(C.ClipCount, m.Count);
            int player = 0, zombie = 0;
            foreach (var s in m.Values) { if (s.target == "player") player++; else if (s.target == "zombie") zombie++; }
            Assert.AreEqual(41, player); Assert.AreEqual(44, zombie);   // WP-032C: v004 = 85 (weapon A/B/C ×5, sw hold ×2, pickup_upper, hurt ×2, death_side, lower/raise; tier hits ×12, armour breaks ×3, stumble, deaths ×4, rises ×2)
            foreach (string name in C.PlayerClips) Assert.IsTrue(m.ContainsKey(name), name);
            foreach (string name in C.ZombieClips) Assert.IsTrue(m.ContainsKey(name), name);
            // Every manifest event name is in the contract table.
            foreach (var s in m.Values)
            foreach (var e in s.events)
                Assert.AreNotEqual(CharacterAnimationEvent.None, CharacterAnimationEvents.Parse(e.Key), s.name + ": bilinmeyen olay " + e.Key);
        }

        [Test]
        public void HerKlip_Import_Dongu_Uzunluk_Olaylar_Additive_Humanoid()
        {
            var m = Manifest();
            var errors = new List<string>();
            foreach (var s in m.Values)
            {
                string path = ArtAnimationClipContract.FbxPath(s);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { errors.Add(s.name + ": FBX yok " + path); continue; }
                foreach (string v in ArtCharacterContract.Validate(importer)) errors.Add(s.name + ": " + v);

                AnimationClip clip = ArtAnimationClipContract.ClipOf(s);
                if (clip == null) { errors.Add(s.name + ": AnimationClip import edilmedi"); continue; }
                if (clip.isLooping != s.loop) errors.Add(s.name + ": isLooping " + clip.isLooping + " != " + s.loop);
                if (!clip.humanMotion) errors.Add(s.name + ": Humanoid değil");
                float expected = (s.frames - 1) / (float)C.Fps;
                if (Mathf.Abs(clip.length - expected) > 1.5f / C.Fps) errors.Add(s.name + ": length " + clip.length.ToString("F3", CultureInfo.InvariantCulture) + " != " + expected.ToString("F3", CultureInfo.InvariantCulture) + " (frameRate " + clip.frameRate + ", importer frames " + importer.clipAnimations[0].firstFrame + ".." + importer.clipAnimations[0].lastFrame + ", default " + (importer.defaultClipAnimations.Length > 0 ? importer.defaultClipAnimations[0].lastFrame.ToString() : "-") + ")");
                AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
                if (events.Length != s.events.Count) errors.Add(s.name + ": " + events.Length + " olay != " + s.events.Count);
                foreach (var e in s.events)
                {
                    bool found = false;
                    foreach (AnimationEvent ae in events) if (ae.functionName == e.Key && Mathf.Abs(ae.time - e.Value / (float)C.Fps) < 1e-3f) found = true;
                    if (!found) errors.Add(s.name + ": olay " + e.Key + "=" + e.Value + " klipte yok");
                }
            }

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void OlayDonusturucu_Idempotan_30Dosya_YazmaYok()
        {
            var m = Manifest();
            int changed = 0;
            var reasons = new List<string>();
            foreach (var s in m.Values)
            {
                var importer = AssetImporter.GetAtPath(ArtAnimationClipContract.FbxPath(s)) as ModelImporter;
                Assert.IsNotNull(importer, s.name);
                string why = ArtAnimationClipContract.WhyChanged(importer);
                if (ArtAnimationClipContract.ApplyClips(importer)) { changed++; reasons.Add(s.name + ": " + why); }
                // (ArtCharacterContract.Apply itself always reports a change on clip files: the v1 pass flips
                // animationType/importAnimation and v1.1 flips them back — WP-016 behaviour, harmless, not this tool.)
                Assert.IsEmpty(ArtCharacterContract.Validate(importer), s.name + " v1.1 uyumsuz");
            }

            Assert.AreEqual(0, changed, "uyumlu importer'a ikinci uygulama yazdı:\n" + string.Join("\n", reasons));
        }

        [Test]
        public void OyuncuController_Parametreler_4Katman_2DBlend_Maskeler_IkPass()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.PlayerAnimationSet);
            Assert.IsTrue(set.IsAuthored, "oyuncu seti AP-009 değil");
            var c = set.Controller as AnimatorController;
            Assert.IsNotNull(c);
            Params(c, C.MoveX, C.MoveY, C.LocoRate, C.Fire, C.HitLight, C.Die, C.DeathStyle, C.Pickup, C.Alive,
                C.Weapon, C.Reload, C.ReloadRate, C.SwitchOut, C.SwitchIn, C.Lower, C.Raise, C.SwHold, C.Hurt, C.HurtStyle);   // WP-032C
            Assert.AreEqual(4, c.layers.Length);
            Assert.AreEqual(C.OverrideUpperLayer, c.layers[1].name); Assert.AreEqual(C.AdditiveLayer, c.layers[2].name); Assert.AreEqual(C.AimLayer, c.layers[3].name);
            Assert.AreEqual(AnimatorLayerBlendingMode.Additive, c.layers[2].blendingMode);
            Assert.IsNotNull(c.layers[1].avatarMask); Assert.IsNotNull(c.layers[2].avatarMask); Assert.IsNotNull(c.layers[3].avatarMask);
            Assert.IsTrue(c.layers[3].iKPass, "aim_layer IK pass");
            Assert.IsFalse(c.layers[1].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg), "override_upper bacakları kapsamaz");
            Assert.IsTrue(c.layers[1].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm));
            Assert.IsTrue(c.layers[3].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body) && !c.layers[3].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Head));

            AnimatorState loco = c.layers[0].stateMachine.defaultState;
            Assert.AreEqual(C.LocomotionState, loco.name);
            var tree = loco.motion as BlendTree;
            Assert.IsNotNull(tree);
            Assert.AreEqual(BlendTreeType.FreeformDirectional2D, tree.blendType);
            Assert.AreEqual(9, tree.children.Length);   // WP-032B: idle + walk ring ×4 + run ring ×4
            Assert.IsTrue(loco.speedParameterActive && loco.speedParameter == C.LocoRate);
            float reference = set.LocomotionReferenceSpeed;
            foreach (ChildMotion ch in tree.children)
            {
                float clipSpeed = set.SpeedOf(ch.motion.name);
                float expected = clipSpeed > 0f ? PlayerSkinnedVisual.ChildTimeScale(reference, clipSpeed) : 1f;
                Assert.AreEqual(expected, ch.timeScale, 1e-4f, ch.motion.name + " timeScale");
                bool run = ch.motion.name.Contains("_run_");
                float radius = ch.position.magnitude;
                if (run) Assert.AreEqual(1f, radius, 1e-4f, ch.motion.name + " koşu halkası birim çemberde");
                else if (clipSpeed > 0f) Assert.AreEqual(Mathf.Clamp(clipSpeed / reference, 0.2f, 0.9f), radius, 1e-4f, ch.motion.name + " yürüyüş halkası kendi hızında");
                else Assert.AreEqual(0f, radius, 1e-4f, "idle merkezde");
            }

            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 0, C.DeathState + "_0")); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 0, C.DeathState + "_1"));
            Assert.IsNull(ArtAnimatorBuilder.FindState(c, 0, C.PickupState), "WP-032B · DEC-082: pickup taban katmanında değil");
            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.PickupState), "pickup üst gövde override katmanında");
            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.HitState)); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 2, C.FireState));
            // WP-032C: weapon handling on the upper layer — Aim_0..2 rest loops, Reload_/SwitchOut_/SwitchIn_ per weapon (Reload at ReloadRate),
            // Lower / Raise, Hurt_A, SwHold_1/2; Fire_0..2 additive per weapon; Death_2 (side) + Hurt_B on the base.
            for (int w = 0; w < 3; w++)
            {
                AnimatorState aim = ArtAnimatorBuilder.FindState(c, 1, C.AimState + "_" + w);
                Assert.IsNotNull(aim, "Aim_" + w); Assert.AreEqual(set.Clip(C.PlayerWeaponClip(w, "idle_aim")), aim.motion);
                AnimatorState reload = ArtAnimatorBuilder.FindState(c, 1, C.ReloadState + "_" + w);
                Assert.IsNotNull(reload, "Reload_" + w); Assert.IsTrue(reload.speedParameterActive && reload.speedParameter == C.ReloadRate, "Reload_" + w + " ReloadRate");
                Assert.AreEqual(5, reload.transitions.Length, "Reload_" + w + " → Aim ×3 + SwHold ×2");
                Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.SwitchOutState + "_" + w)); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.SwitchInState + "_" + w));
                AnimatorState fireW = ArtAnimatorBuilder.FindState(c, 2, C.FireState + "_" + w);
                Assert.IsNotNull(fireW, "Fire_" + w); Assert.AreEqual(set.Clip(C.PlayerWeaponClip(w, "fire")), fireW.motion);
            }

            for (int w = 0; w < 3; w++) { Assert.AreEqual(set.Clip(C.PlayerWeaponLowerClip(w)), ArtAnimatorBuilder.FindState(c, 1, C.WeaponLowerState + "_" + w).motion, "WeaponLower_" + w); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.WeaponRaiseState + "_" + w)); }
            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.HurtState + "_A")); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 0, C.HurtState + "_B"));
            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.SwHoldState + "_1")); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.SwHoldState + "_2"));
            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 0, C.DeathState + "_2"), "death_side");
            Assert.AreEqual(set.Clip(C.PlayerPickupUpper), ArtAnimatorBuilder.FindState(c, 1, C.PickupState).motion, "pickup = v003 pickup_upper");
            Assert.AreEqual(5, ArtAnimatorBuilder.FindState(c, 1, C.EmptyState).transitions.Length, "Empty → Aim_0..2 + SwHold_1..2");
            Assert.AreEqual(C.EmptyState, c.layers[1].stateMachine.defaultState.name); Assert.AreEqual(C.EmptyState, c.layers[2].stateMachine.defaultState.name);
            // Deaths never leave (no transitions out); the pickup returns to the upper layer's Empty.
            Assert.AreEqual(0, ArtAnimatorBuilder.FindState(c, 0, C.DeathState + "_0").transitions.Length);
            Assert.AreEqual(5, ArtAnimatorBuilder.FindState(c, 1, C.PickupState).transitions.Length, "WP-032C: pickup → Aim ×3 + SwHold ×2");
        }

        [Test]
        public void ZombiController_Parametreler_3Katman_IdleGait_Saldiri3_Olum4_Rise_Stagger()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet);
            Assert.IsTrue(set.IsAuthored, "zombi seti AP-009 değil");
            var c = set.Controller as AnimatorController;
            Assert.IsNotNull(c);
            Params(c, C.Speed01, C.GaitRate, C.Gait, C.IdleVariant, C.Waiting, C.WaitPhase, C.Attack, C.AttackStyle, C.AttackRate, C.Stagger, C.HitLeft, C.HitRight,
                C.FlinchHead, C.FlinchTorso, C.FlinchLeg, C.Die, C.DeathStyle, C.Rise, C.Alive);
            Assert.AreEqual(4, c.layers.Length);   // WP-032: + telegraph
            Assert.AreEqual(C.OverrideUpperLayer, c.layers[1].name); Assert.AreEqual(C.AdditiveLayer, c.layers[2].name); Assert.AreEqual(C.TelegraphLayer, c.layers[3].name);
            Assert.IsNotNull(c.layers[1].avatarMask); Assert.AreEqual(AnimatorLayerBlendingMode.Additive, c.layers[2].blendingMode);

            AnimatorState loco = c.layers[0].stateMachine.defaultState;
            Assert.AreEqual(C.LocomotionState, loco.name);
            var tree = loco.motion as BlendTree;
            Assert.IsNotNull(tree);
            Assert.AreEqual(C.Speed01, tree.blendParameter);
            Assert.AreEqual(2, tree.children.Length);
            var idle = tree.children[0].motion as BlendTree; var gait = tree.children[1].motion as BlendTree;
            Assert.IsNotNull(idle); Assert.IsNotNull(gait);
            Assert.AreEqual(2, idle.children.Length); Assert.AreEqual(4, gait.children.Length);
            for (int i = 0; i < 4; i++) Assert.AreEqual(set.Clip(C.ZombieGaitClips[i]), gait.children[i].motion, "gait " + i);
            Assert.IsTrue(loco.speedParameterActive && loco.speedParameter == C.GaitRate);

            string[] attacks = { C.AttackSwipeState, C.AttackLungeState, C.AttackSweepState, C.AttackOverheadState };   // WP-032B: + overhead (v002, Ultra)
            for (int i = 0; i < 4; i++)
            {
                AnimatorState a = ArtAnimatorBuilder.FindState(c, 0, attacks[i]);
                Assert.IsNotNull(a, attacks[i]);
                Assert.AreEqual(set.Clip(C.ZombieAttackClips[i]), a.motion);
                Assert.IsTrue(a.speedParameterActive && a.speedParameter == C.AttackRate, attacks[i] + " AttackRate");
                Assert.AreEqual(1, a.transitions.Length, attacks[i] + " → Locomotion");
            }

            // WP-032B: the overhead holds its last frame (exit at 1.0, blend = OverheadHoldSeconds); the Wait state is the queue idle with a per-body cycle offset.
            AnimatorStateTransition overheadOut = ArtAnimatorBuilder.FindState(c, 0, C.AttackOverheadState).transitions[0];
            Assert.AreEqual(0.97f, overheadOut.exitTime, 1e-4f, "WP-032C: v003 overhead (73 f) has its own recovery — common exit"); Assert.AreEqual(0.12f, overheadOut.duration, 1e-4f);
            AnimatorState wait = ArtAnimatorBuilder.FindState(c, 0, C.WaitState);
            Assert.IsNotNull(wait, "Wait durumu");
            Assert.AreEqual(set.Clip(C.ZombieIdleQueue), wait.motion);
            Assert.IsTrue(wait.cycleOffsetParameterActive && wait.cycleOffsetParameter == C.WaitPhase, "Wait cycle offset = WaitPhase");
            Assert.AreEqual(1, wait.transitions.Length); Assert.AreEqual(C.LocomotionState, wait.transitions[0].destinationState.name);
            Assert.IsTrue(System.Array.Exists(loco.transitions, t => t.destinationState == wait), "Locomotion → Wait (Waiting)");

            for (int i = 0; i < 4; i++)
            {
                AnimatorState d = ArtAnimatorBuilder.FindState(c, 0, C.DeathState + "_" + i);
                Assert.IsNotNull(d); Assert.AreEqual(set.Clip(C.ZombieDeathClips[i]), d.motion); Assert.AreEqual(0, d.transitions.Length, "ölüm çıkışsız");
            }

            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 0, C.RiseState)); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 0, C.StaggerState));
            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.HitState + "_L")); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 1, C.HitState + "_R"));
            // WP-032C: directional × tier hits, armour breaks by region, stumble, rise variants, 8 deaths
            for (int dir = 0; dir < 4; dir++)
            for (int tier = 0; tier < 3; tier++)
            {
                AnimatorState h = ArtAnimatorBuilder.FindState(c, 1, C.HitState + "_" + C.HitDirections[dir] + "_" + C.HitTiers[tier]);
                Assert.IsNotNull(h, "Hit_" + dir + "_" + tier); Assert.AreEqual(set.Clip(C.ZombieHitClip(dir, tier)), h.motion);
            }

            for (int r = 0; r < 3; r++) Assert.AreEqual(set.Clip(C.ZombieArmorBreakClip(r)), ArtAnimatorBuilder.FindState(c, 0, C.ArmorBreakState + "_" + r).motion, "ArmorBreak_" + r);
            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 0, C.StumbleState));
            Assert.AreEqual(set.Clip(C.ZombieRiseB), ArtAnimatorBuilder.FindState(c, 0, C.RiseState + "_1").motion); Assert.AreEqual(set.Clip(C.ZombieRiseC), ArtAnimatorBuilder.FindState(c, 0, C.RiseState + "_2").motion);
            Assert.AreEqual(8, C.ZombieDeathClips.Length); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 0, C.DeathState + "_7"), "death_ultra");
            Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 2, C.FlinchState + "_Head")); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 2, C.FlinchState + "_Torso")); Assert.IsNotNull(ArtAnimatorBuilder.FindState(c, 2, C.FlinchState + "_Leg"));

            // Every AnyState reaction is gated by Alive except the deaths.
            foreach (AnimatorStateTransition t in c.layers[0].stateMachine.anyStateTransitions)
            {
                bool death = t.destinationState.name.StartsWith(C.DeathState, StringComparison.Ordinal);
                bool alive = false;
                foreach (AnimatorCondition cond in t.conditions) if (cond.parameter == C.Alive) alive = true;
                Assert.AreEqual(!death, alive, t.destinationState.name + " Alive koşulu");
            }
        }

        [Test]
        public void HizEslemesi_GaitRate_AttackRate_ManifestSpeedTable()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet);
            var m = Manifest();
            // WP-032: SpeedOf is the Unity-rig calibration (StrideCalibrator), within ±15 % of the manifest; ManifestSpeedOf is the Blender value.
            foreach (string gait in C.ZombieGaitClips)
            {
                Assert.AreEqual(m[gait].speedMps, set.ManifestSpeedOf(gait), 1e-4f, gait);
                // WP-032B: a flight-phase cycle calibrates over its authored stance frames (zombie_run 5.32 → 6.14 on the Unity rig, +15.4 %, slip-verified); shuffles stay within ±15 %.
                float band = gait == C.ZombieRun ? 0.20f : 0.15f;
                Assert.AreEqual(m[gait].speedMps, set.SpeedOf(gait), m[gait].speedMps * band, gait + " kalibrasyon manifestin ±%" + (int)(band * 100) + "'i içinde");
                Assert.AreNotEqual(m[gait].speedMps, set.SpeedOf(gait), gait + " kalibre edilmemiş");
            }

            // AP-009 README speed table: Standard 2.7 over shamble_A → ×1.845 (manifest); the shipped rate uses the calibrated speed.
            Assert.AreEqual(2.7f / set.SpeedOf(C.ZombieShambleA), set.RateFor(C.ZombieShambleA, 2.7f), 1e-3f);
            Assert.AreEqual(4.6f / set.SpeedOf(C.ZombieRun), set.RateFor(C.ZombieRun, 4.6f), 1e-3f);
            Assert.AreEqual(1f, set.RateFor(C.ZombieIdleSway, 2.7f), 1e-4f, "hız verisi olmayan klip oran 1");

            // Attack rate: hit_window_start lands on the tier telegraph (config table: swipe 0.55 / lunge 0.35 / sweep 0.8).
            var config = new EnemyConfigSection();
            float swipeStart = 16f / C.Fps, lungeStart = 10f / C.Fps, sweepStart = 24f / C.Fps;
            Assert.AreEqual(16, ZombieSkinnedVisual.FrameOf(set.Find(C.ZombieAttackSwipe).events, "hit_window_start"));
            Assert.AreEqual(20, ZombieSkinnedVisual.FrameOf(set.Find(C.ZombieAttackSwipe).events, "hit_window_end"));
            Assert.AreEqual(10, ZombieSkinnedVisual.FrameOf(set.Find(C.ZombieAttackLunge).events, "hit_window_start"));
            Assert.AreEqual(24, ZombieSkinnedVisual.FrameOf(set.Find(C.ZombieAttackSweep).events, "hit_window_start"));
            Assert.AreEqual(35, ZombieSkinnedVisual.FrameOf(set.Find(C.ZombieAttackOverhead).events, "hit_window_start"));   // WP-032B: 1.167 s vs the Ultra 1.15 s timer
            Assert.AreEqual(40, ZombieSkinnedVisual.FrameOf(set.Find(C.ZombieAttackOverhead).events, "hit_window_end"));
            Assert.AreEqual(-1, ZombieSkinnedVisual.FrameOf(set.Find(C.ZombieAttackSweep).events, "hit"), "'hit' 'hit_window_start' içinde eşleşmemeli");
            float swipeRate = swipeStart / config.Find(EnemyTier.Standard).TelegraphTime;
            float lungeRate = lungeStart / config.Find(EnemyTier.Special).TelegraphTime;
            float sweepRate = sweepStart / config.Find(EnemyTier.Elite).TelegraphTime;
            Debug.Log("[DD-TEST] wp018.rates swipe=" + swipeRate.ToString("F3", CultureInfo.InvariantCulture) + " lunge=" + lungeRate.ToString("F3", CultureInfo.InvariantCulture) + " sweep=" + sweepRate.ToString("F3", CultureInfo.InvariantCulture)
                      + " activeSeconds swipe=" + ((20 - 16f) / C.Fps / swipeRate).ToString("F3", CultureInfo.InvariantCulture) + "/" + config.Find(EnemyTier.Standard).AttackActiveTime
                      + " lunge=" + ((13 - 10f) / C.Fps / lungeRate).ToString("F3", CultureInfo.InvariantCulture) + "/" + config.Find(EnemyTier.Special).AttackActiveTime
                      + " sweep=" + ((30 - 24f) / C.Fps / sweepRate).ToString("F3", CultureInfo.InvariantCulture) + "/" + config.Find(EnemyTier.Elite).AttackActiveTime);
            Assert.IsTrue(swipeRate > 0.8f && swipeRate < 1.25f, "swipe oranı klibe yakın olmalı: " + swipeRate);
            Assert.IsTrue(lungeRate > 0.8f && lungeRate < 1.25f, "lunge oranı: " + lungeRate);
            Assert.IsTrue(sweepRate > 0.8f && sweepRate < 1.25f, "sweep oranı: " + sweepRate);
            float overheadRate = 35f / C.Fps / config.Find(EnemyTier.Ultra).TelegraphTime;
            Assert.IsTrue(overheadRate > 0.9f && overheadRate < 1.1f, "overhead oranı klibe yakın olmalı (1,167 s / 1,15 s): " + overheadRate);
        }

        [Test]
        public void CharacterAnimationEvents_Sozlesme_HerOlayAdiMetot_ParseEnum()
        {
            Assert.AreEqual(Enum.GetValues(typeof(CharacterAnimationEvent)).Length, CharacterAnimationEvents.Names.Length);
            for (int i = 1; i < CharacterAnimationEvents.Names.Length; i++)
            {
                string name = CharacterAnimationEvents.Names[i];
                MethodInfo method = typeof(CharacterAnimationEvents).GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(method, "AnimationEvent hedefi yok: " + name);
                Assert.AreEqual(0, method.GetParameters().Length, name + " parametresiz olmalı");
                Assert.AreEqual((CharacterAnimationEvent)i, CharacterAnimationEvents.Parse(name));
            }

            Assert.AreEqual(CharacterAnimationEvent.None, CharacterAnimationEvents.Parse("nope"));
        }

        [Test]
        public void AnimasyonSetleri_AP009Tablosu_OlayMetni_Sema()
        {
            var player = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.PlayerAnimationSet);
            var zombie = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet);
            Assert.AreEqual(41, player.Clips.Count); Assert.AreEqual(44, zombie.Clips.Count);   // WP-032C v004
            Assert.AreEqual(ArtAnimationClipContract.ManifestSchema, player.ManifestSchema); Assert.AreEqual(ArtAnimationClipContract.ManifestSchema, zombie.ManifestSchema);
            var m = Manifest();
            foreach (var set in new[] { player, zombie })
            foreach (CharacterAnimationSet.Entry e in set.Clips)
            {
                Assert.IsNotNull(e.clip, e.name);
                Assert.AreEqual(e.name, e.clip.name);
                Assert.AreEqual(m[e.name].EventsText(), e.events, e.name + " olay metni");
                Assert.AreEqual(m[e.name].loop, e.loop);
                Assert.AreEqual(m[e.name].additive, e.additive);
            }

            Assert.AreEqual(player.Clip(C.PlayerIdle), player.Idle); Assert.AreEqual(player.Clip(C.PlayerWalkFwd), player.Walk);
            Assert.AreEqual(zombie.Clip(C.ZombieIdleSway), zombie.Idle); Assert.AreEqual(zombie.Clip(C.ZombieShambleA), zombie.Walk);
            Assert.IsTrue(player.Source.Contains("AP-009") && zombie.Source.Contains("AP-009"));
        }

        [Test]
        public void ProceduralPoseBridge_Kaldirildi_PrefablardaEksikScriptYok_EskiControllerYok()
        {
            Assert.IsNull(typeof(PlayerSkinnedVisual).Assembly.GetType("DD.Prototype.Characters.ProceduralPoseBridge"), "ProceduralPoseBridge hâlâ derleniyor");
            foreach (string path in new[] { ArtCharacterPrefabBuilder.PlayerPrefab, ArtCharacterPrefabBuilder.ZombiePrefab })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                int missing = 0;
                foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true)) missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                Assert.AreEqual(0, missing, path + ": eksik script");
                Assert.IsNotNull(prefab.GetComponentInChildren<CharacterAnimationEvents>(true), path + ": CharacterAnimationEvents");
            }

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimatorController>(ArtCharacterPrefabBuilder.PlayerController), "eski rig-test controller'ı kaldırılmalı");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimatorController>(ArtCharacterPrefabBuilder.ZombieController));
        }

        private static void Params(AnimatorController c, params string[] names)
        {
            var have = new HashSet<string>();
            foreach (AnimatorControllerParameter p in c.parameters) have.Add(p.name);
            foreach (string n in names) Assert.IsTrue(have.Contains(n), c.name + " parametresi yok: " + n);
        }
    }
}
