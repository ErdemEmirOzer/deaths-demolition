using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-018 · ADR-018-2 · Generates the two AP-009 Animator Controllers, the avatar masks and
    /// the <see cref="CharacterAnimationSet"/> tables from the manifest — idempotent, GUID
    /// stable (the controller / set assets are rebuilt in place).
    ///
    /// Player (<c>DD_Player.controller</c>): base = 2-D freeform-directional locomotion
    /// (idle / walk ring at the walk clips' no-slide radius / run ring at radius 1 — AP-009B v002
    /// run_fwd/back/strafe_L/R — on MoveX·MoveY; child time scales = reference speed / clip
    /// speed; state speed = LocoRate), Death_0/1; <c>override_upper</c> = hit_light + Pickup
    /// (WP-032B · DEC-082, recorded by S-02 in WP-042: the grab plays over uninterrupted locomotion, no movement lock);
    /// <c>additive</c> = fire_recoil; <c>aim_layer</c> =
    /// empty state with the IK pass (PlayerAimLayer writes Spine/Chest/UpperChest).
    /// Zombie (<c>DD_Zombie.controller</c>): base = Locomotion (Speed01: idle sway/twitch ↔ gait
    /// shamble_A/B/C/run by Gait; state speed = GaitRate), Wait (idle_queue, cycle offset =
    /// WaitPhase, gated by Waiting), Attack_Swipe/Lunge/Sweep/Overhead (speed = AttackRate, so
    /// hit_window_start lands on the tier telegraph; the overhead holds its last frame 0.3 s into
    /// the recovery), Stagger, Death_0..3, Rise;
    /// <c>override_upper</c> = hit_light L/R; <c>additive</c> = hit_region head/torso/leg.
    /// Every one-shot returns to its layer's rest state on exit time; nothing loops but the
    /// manifest loops; Alive gates every reaction so a corpse never re-enters locomotion.
    /// </summary>
    public static class ArtAnimatorBuilder
    {
        public const string LogPrefix = "[ArtAnimator] ";
        public const string MasksFolder = ArtImportContract.CharactersRoot + "/Masks";
        public const string UpperBodyMask = MasksFolder + "/UpperBody.mask";
        public const string AimMask = MasksFolder + "/Aim.mask";
        public const string PlayerController = ArtCharacterContract.PlayerFolder + "/DD_Player.controller";
        public const string GeneratedFolder = ArtCharacterContract.ZombieFolder + "/Generated";   // WP-032 telegraph emphasis clips
        public const string ZombieController = ArtCharacterContract.ZombieFolder + "/DD_Zombie.controller";
        public const string PlayerSource = "AP-009D v004 (85 clips, AP009-clips-manifest.json) — WP-018/032B/032C generated controller";
        public const string ZombieSource = "AP-009D v004 (85 clips, AP009-clips-manifest.json) — WP-018/032B/032C generated controller";
        /// <summary>WP-032B: seconds the overhead's last frame was held before the blend back to locomotion. WP-032C: v003 recut the recovery to 33 f (73 f = telegraph + window + AttackRecoveryTime) — no hold, the common exit.</summary>
        public const float OverheadHoldSeconds = 0f;

        [Serializable]
        public struct Report
        {
            public int clips;
            public int playerStates;
            public int zombieStates;
            public int playerLayers;
            public int zombieLayers;
        }

        /// <summary>True when the AP-009 folder + manifest are present (the rig-test set is built otherwise).</summary>
        public static bool HasAp009()
        {
            return File.Exists(Path.Combine(Directory.GetCurrentDirectory(), ArtAnimationClipContract.ManifestPath));
        }

        public static Report BuildAll(float playerReferenceSpeed)
        {
            Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest = ArtAnimationClipContract.Manifest();
            if (manifest == null) throw new FileNotFoundException(LogPrefix + "manifest missing", ArtAnimationClipContract.ManifestPath);
            if (manifest.Count != C.ClipCount) throw new InvalidDataException(LogPrefix + "manifest lists " + manifest.Count + " clips != " + C.ClipCount);

            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (ArtAnimationClipContract.ClipSpec spec in manifest.Values)
            {
                AnimationClip clip = ArtAnimationClipContract.ClipOf(spec);
                if (clip == null) throw new InvalidOperationException(LogPrefix + "clip not imported: " + spec.name + " (" + ArtAnimationClipContract.FbxPath(spec) + ")");
                clips[spec.name] = clip;
            }

            EnsureFolder(MasksFolder);
            AvatarMask upper = BuildMask(UpperBodyMask, body: true, head: true, arms: true, legs: false, root: false);
            AvatarMask aim = BuildMask(AimMask, body: true, head: false, arms: false, legs: false, root: false);

            var report = new Report { clips = clips.Count };
            // WP-032 (ADR-032-1): the no-slip speeds measured on the Unity rig, used by the 2-D blend time scales and the sets.
            Dictionary<string, float> unitySpeeds = Calibrate(manifest, clips);
            AnimatorController player = BuildPlayer(clips, manifest, unitySpeeds, upper, aim, playerReferenceSpeed, out report.playerStates, out report.playerLayers);
            AnimatorController zombie = BuildZombie(clips, manifest, upper, out report.zombieStates, out report.zombieLayers);

            WriteSet(ArtCharacterPrefabBuilder.PlayerAnimationSet, player, manifest, clips, unitySpeeds, "player", PlayerSource, playerReferenceSpeed,
                clips[C.PlayerIdle], clips[C.PlayerWalkFwd], manifest[C.PlayerWalkFwd].speedMps);
            WriteSet(ArtCharacterPrefabBuilder.ZombieAnimationSet, zombie, manifest, clips, unitySpeeds, "zombie", ZombieSource, manifest[C.ZombieShambleA].speedMps,
                clips[C.ZombieIdleSway], clips[C.ZombieShambleA], manifest[C.ZombieShambleA].speedMps);

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "built: " + report.clips + " clips, player " + report.playerStates + " states / " + report.playerLayers + " layers, zombie "
                      + report.zombieStates + " states / " + report.zombieLayers + " layers");
            return report;
        }

        // ================================================================== masks

        private static AvatarMask BuildMask(string path, bool body, bool head, bool arms, bool legs, bool root)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            bool created = mask == null;
            if (created)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, path);
            }

            bool changed = created;
            changed |= Part(mask, AvatarMaskBodyPart.Root, root);
            changed |= Part(mask, AvatarMaskBodyPart.Body, body);
            changed |= Part(mask, AvatarMaskBodyPart.Head, head);
            changed |= Part(mask, AvatarMaskBodyPart.LeftArm, arms);
            changed |= Part(mask, AvatarMaskBodyPart.RightArm, arms);
            changed |= Part(mask, AvatarMaskBodyPart.LeftFingers, arms);
            changed |= Part(mask, AvatarMaskBodyPart.RightFingers, arms);
            changed |= Part(mask, AvatarMaskBodyPart.LeftLeg, legs);
            changed |= Part(mask, AvatarMaskBodyPart.RightLeg, legs);
            changed |= Part(mask, AvatarMaskBodyPart.LeftFootIK, legs);
            changed |= Part(mask, AvatarMaskBodyPart.RightFootIK, legs);
            changed |= Part(mask, AvatarMaskBodyPart.LeftHandIK, arms);
            changed |= Part(mask, AvatarMaskBodyPart.RightHandIK, arms);
            if (changed) EditorUtility.SetDirty(mask);
            return mask;
        }

        private static bool Part(AvatarMask mask, AvatarMaskBodyPart part, bool active)
        {
            if (mask.GetHumanoidBodyPartActive(part) == active) return false;
            mask.SetHumanoidBodyPartActive(part, active);
            return true;
        }

        // ============================================================ controllers

        private static AnimatorController Fresh(string path, params (string name, AnimatorControllerParameterType type)[] parameters)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            }
            else
            {
                // Rebuild in place: drop every layer, parameter and the orphaned state / transition /
                // tree sub-assets, keep the GUID (the animation set and the prefabs point at it).
                while (controller.layers.Length > 0) controller.RemoveLayer(controller.layers.Length - 1);
                AnimatorControllerParameter[] old = controller.parameters;
                for (int i = old.Length - 1; i >= 0; i--) controller.RemoveParameter(old[i]);
                foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o == null || o == controller) continue;
                    if (o is AnimatorStateMachine || o is AnimatorState || o is AnimatorTransitionBase || o is BlendTree)
                    {
                        UnityEngine.Object.DestroyImmediate(o, true);
                    }
                }

                controller.AddLayer("Base Layer");
            }

            foreach ((string name, AnimatorControllerParameterType type) in parameters)
            {
                controller.AddParameter(name, type);
            }

            return controller;
        }

        private static AnimatorControllerLayer AddLayer(AnimatorController controller, string name, AvatarMask mask, AnimatorLayerBlendingMode mode, bool ikPass)
        {
            var layer = new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = 1f,
                avatarMask = mask,
                blendingMode = mode,
                iKPass = ikPass,
                stateMachine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy }
            };
            AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
            controller.AddLayer(layer);
            return controller.layers[controller.layers.Length - 1];
        }

        private static AnimatorState OneShot(AnimatorStateMachine sm, string name, Motion motion, AnimatorState rest, float exitTime = 0.95f, float exitDuration = 0.15f)
        {
            AnimatorState state = sm.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = false;
            if (rest != null)
            {
                AnimatorStateTransition back = state.AddTransition(rest);
                back.hasExitTime = true;
                back.exitTime = exitTime;
                back.hasFixedDuration = true;
                back.duration = exitDuration;
                back.canTransitionToSelf = false;
            }

            return state;
        }

        private static AnimatorStateTransition FromAny(AnimatorStateMachine sm, AnimatorState to, float duration, params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.canTransitionToSelf = false;
            t.interruptionSource = TransitionInterruptionSource.None;
            foreach ((AnimatorConditionMode mode, float threshold, string parameter) in conditions) t.AddCondition(mode, threshold, parameter);
            return t;
        }

        /// <summary>WP-032C: an upper-layer one-shot that returns, on exit time, to the rest state the Weapon / SwHold parameters select (Aim_0..2 or SwHold_1..2).</summary>
        private static AnimatorState OneShotUpper(AnimatorStateMachine osm, AnimatorState[] aims, AnimatorState[] holds, string name, Motion motion, float exitTime, float exitDuration)
        {
            AnimatorState st = osm.AddState(name);
            st.motion = motion;
            st.writeDefaultValues = false;
            for (int w = 0; w < aims.Length; w++)
            {
                AnimatorStateTransition back = st.AddTransition(aims[w]);
                back.hasExitTime = true; back.exitTime = exitTime; back.hasFixedDuration = true; back.duration = exitDuration; back.canTransitionToSelf = false;
                back.AddCondition(AnimatorConditionMode.Equals, w, C.Weapon);
                back.AddCondition(AnimatorConditionMode.Equals, 0f, C.SwHold);
            }

            for (int h = 0; h < holds.Length; h++)
            {
                AnimatorStateTransition back = st.AddTransition(holds[h]);
                back.hasExitTime = true; back.exitTime = exitTime; back.hasFixedDuration = true; back.duration = exitDuration; back.canTransitionToSelf = false;
                back.AddCondition(AnimatorConditionMode.Equals, h + 1, C.SwHold);
            }

            return st;
        }

        private static void ApplyLayer(AnimatorController controller, int index, Action<AnimatorControllerLayer> edit)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            edit(layers[index]);
            controller.layers = layers;
        }

        // ------------------------------------------------------------------ player

        private static float SpeedOf(Dictionary<string, ArtAnimationClipContract.ClipSpec> m, Dictionary<string, float> unity, string clip)
        {
            return unity != null && unity.TryGetValue(clip, out float u) && u > 0.01f ? u : m[clip].speedMps;
        }

        /// <summary>
        /// WP-032C · Live-rig calibration overrides. The sampled calibration (StrideCalibrator on the prefab rig) and the
        /// live Animator disagree on the v004 player run_fwd by ≈ 20 % (sampled 5.02 m/s; the PlayMode contact-slip test read
        /// −11.1 cm/step at 5.02 and −15.6 at 4.52 → zero crossing ≈ 6.26 m/s; the walk / shamble / zombie run agree within
        /// 2 %). The live value wins for the clip the test measures; the cause (blend-tree evaluation vs SampleAnimation on
        /// a flight-phase clip) is an open S-06 item — the other three player run cycles carry the sampled value untested.
        /// </summary>
        public static readonly Dictionary<string, float> LiveCalibrationOverrides = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            // WP-032D · AnimationLiveCalibrationTests (two LocoRate points, zero crossing, 6.2 m/s along each clip's axis, iterated
            // twice — the slip is only linear near the zero): fwd 5.91, back 5.04, strafe L/R 5.85 / 5.85 (sampled 5.02 / 4.50 /
            // 4.40 / 4.56 — the sampled run cycles under-read by 15–33 %; the walk cycles agree with the live rig).
            { C.PlayerRunFwd, 5.91f },
            { C.PlayerRunBack, 4.9f },   // 4.83 → −2.2 cm, 5.04 → +5.2 cm (noisy contact on the back cycle) — the midpoint
            { C.PlayerRunStrafeLeft, 5.85f },
            { C.PlayerRunStrafeRight, 5.85f },
        };

        /// <summary>WP-032: measure every locomotion clip on the character's avatar (StrideCalibrator); keyed by clip name.</summary>
        public static Dictionary<string, float> Calibrate(Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest, Dictionary<string, AnimationClip> clips)
        {
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (string target in new[] { "player", "zombie" })
            {
                GameObject rig = InstantiateCalibrationRig(target);
                if (rig == null) continue;
                try
                {
                    foreach (ArtAnimationClipContract.ClipSpec s in manifest.Values)
                    {
                        if (s.target != target || s.speedMps <= 0.01f) continue;
                        StrideCalibrator.Result r = StrideCalibrator.Measure(rig, rig.GetComponent<Animator>(), clips[s.name], s.frames, ArtAnimationClipContract.Fps, s.EventsText(),
                            StrideCalibrator.FootTravelDirection(s.name), s.stanceFrames);
                        if (r.windows > 0) result[s.name] = r.speedMps;
                        Debug.Log(LogPrefix + "stride " + s.name + ": manifest " + s.speedMps.ToString("F3") + " m/s → Unity " + r.speedMps.ToString("F3") + " m/s (" + r.windows + " windows: " + r.report + ")");
                    }
                }
                finally
                {
                    DestroyCalibrationRig();
                }
            }

            foreach (KeyValuePair<string, float> o in LiveCalibrationOverrides)
            {
                if (!manifest.ContainsKey(o.Key)) continue;
                Debug.Log(LogPrefix + "stride " + o.Key + ": live-rig override " + o.Value.ToString("F3") + " m/s (sampled " + (result.TryGetValue(o.Key, out float sampled) ? sampled.ToString("F3") : "-") + ")");
                result[o.Key] = o.Value;
            }

            return result;
        }

        private static AnimatorController BuildPlayer(Dictionary<string, AnimationClip> clips, Dictionary<string, ArtAnimationClipContract.ClipSpec> m, Dictionary<string, float> unitySpeeds,
            AvatarMask upper, AvatarMask aim, float reference, out int states, out int layers)
        {
            AnimatorController c = Fresh(PlayerController,
                (C.MoveX, AnimatorControllerParameterType.Float), (C.MoveY, AnimatorControllerParameterType.Float), (C.LocoRate, AnimatorControllerParameterType.Float),
                (C.Fire, AnimatorControllerParameterType.Trigger), (C.HitLight, AnimatorControllerParameterType.Trigger),
                (C.Die, AnimatorControllerParameterType.Trigger), (C.DeathStyle, AnimatorControllerParameterType.Int),
                (C.Pickup, AnimatorControllerParameterType.Trigger), (C.Alive, AnimatorControllerParameterType.Bool),
                // WP-032C weapon handling + hurt
                (C.Weapon, AnimatorControllerParameterType.Int), (C.Reload, AnimatorControllerParameterType.Trigger), (C.ReloadRate, AnimatorControllerParameterType.Float),
                (C.SwitchOut, AnimatorControllerParameterType.Trigger), (C.SwitchIn, AnimatorControllerParameterType.Trigger),
                (C.Lower, AnimatorControllerParameterType.Trigger), (C.Raise, AnimatorControllerParameterType.Trigger),
                (C.SwHold, AnimatorControllerParameterType.Int), (C.Hurt, AnimatorControllerParameterType.Trigger), (C.HurtStyle, AnimatorControllerParameterType.Int));
            SetDefault(c, C.LocoRate, 1f);
            SetDefault(c, C.ReloadRate, 1f);
            SetDefault(c, C.Alive, true);

            // ---- base: 2-D locomotion + deaths + pickup
            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState loco = c.CreateBlendTreeInController(C.LocomotionState, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = C.MoveX;
            tree.blendParameterY = C.MoveY;
            tree.useAutomaticThresholds = false;
            // WP-032B: two rings. The walk clips sit at their own no-slide speed over the reference
            // (radius = clip speed / reference, so the blend passes THROUGH the walk at rate 1) and
            // the v002 run cycles sit on the unit circle; between them the tree cross-fades walk→run
            // (AP-009B README: blend above ~1.6× the walk speed — the walk scaled beyond ×2.1 reads as skating).
            tree.children = new[]
            {
                Child2D(clips[C.PlayerIdle], 0f, 0f, 1f),
                Ring(clips, m, unitySpeeds, reference, C.PlayerWalkFwd, 0f, 1f),
                Ring(clips, m, unitySpeeds, reference, C.PlayerWalkBack, 0f, -1f),
                Ring(clips, m, unitySpeeds, reference, C.PlayerStrafeLeft, -1f, 0f),
                Ring(clips, m, unitySpeeds, reference, C.PlayerStrafeRight, 1f, 0f),
                Child2D(clips[C.PlayerRunFwd], 0f, 1f, PlayerSkinnedVisual.ChildTimeScale(reference, SpeedOf(m, unitySpeeds, C.PlayerRunFwd))),
                Child2D(clips[C.PlayerRunBack], 0f, -1f, PlayerSkinnedVisual.ChildTimeScale(reference, SpeedOf(m, unitySpeeds, C.PlayerRunBack))),
                Child2D(clips[C.PlayerRunStrafeLeft], -1f, 0f, PlayerSkinnedVisual.ChildTimeScale(reference, SpeedOf(m, unitySpeeds, C.PlayerRunStrafeLeft))),
                Child2D(clips[C.PlayerRunStrafeRight], 1f, 0f, PlayerSkinnedVisual.ChildTimeScale(reference, SpeedOf(m, unitySpeeds, C.PlayerRunStrafeRight)))
            };
            loco.speedParameterActive = true;
            loco.speedParameter = C.LocoRate;
            loco.writeDefaultValues = true;
            sm.defaultState = loco;

            for (int i = 0; i < C.PlayerDeathClips.Length; i++)   // WP-032C: + death_side (2)
            {
                AnimatorState d = OneShot(sm, C.DeathState + "_" + i, clips[C.PlayerDeathClips[i]], null);
                FromAny(sm, d, 0.05f, (AnimatorConditionMode.If, 0f, C.Die), (AnimatorConditionMode.Equals, (float)i, C.DeathStyle));
            }

            // WP-032C: hurt_B = full-body gut hit with a step (base one-shot); hurt_A stays on the upper layer.
            AnimatorState hurtB = OneShot(sm, C.HurtState + "_B", clips[C.PlayerHurtB], loco, 0.9f, 0.12f);
            FromAny(sm, hurtB, 0.03f, (AnimatorConditionMode.If, 0f, C.Hurt), (AnimatorConditionMode.Equals, 1f, C.HurtStyle), (AnimatorConditionMode.If, 0f, C.Alive));

            // ---- override_upper (WP-032C · AP-009C weapon handling): the rest state while armed is Aim_<Weapon> (idle_aim loop,
            // breathing) — Empty stays the entry state and hands over to the weapon's Aim at once. One-shots (reload / switch /
            // lower / raise / hit / hurt_A / pickup_upper) return to the CURRENT weapon's Aim; SwHold_1/2 (Kagura Bell / Ofuda
            // Loom in the right hand) replace the Aim while a Special Weapon is held. DEC-082 (S-02 WP-042): the pickup grab
            // plays here over uninterrupted locomotion, no movement lock.
            AnimatorControllerLayer over = AddLayer(c, C.OverrideUpperLayer, upper, AnimatorLayerBlendingMode.Override, false);
            AnimatorStateMachine osm = over.stateMachine;
            AnimatorState overEmpty = osm.AddState(C.EmptyState);
            osm.defaultState = overEmpty;
            var aims = new AnimatorState[3];
            for (int w = 0; w < 3; w++)
            {
                aims[w] = osm.AddState(C.AimState + "_" + w);
                aims[w].motion = clips[C.PlayerWeaponClip(w, "idle_aim")];
                aims[w].writeDefaultValues = false;
            }

            var holds = new AnimatorState[2];
            holds[0] = osm.AddState(C.SwHoldState + "_1"); holds[0].motion = clips[C.PlayerSwHoldBell]; holds[0].writeDefaultValues = false;
            holds[1] = osm.AddState(C.SwHoldState + "_2"); holds[1].motion = clips[C.PlayerSwHoldLoom]; holds[1].writeDefaultValues = false;
            // Empty / Aim_x / SwHold_x ↔ the weapon and special-weapon selection (no exit time)
            var rests = new List<AnimatorState> { overEmpty }; rests.AddRange(aims); rests.AddRange(holds);
            foreach (AnimatorState from in rests)
            {
                for (int w = 0; w < 3; w++)
                {
                    if (from == aims[w]) continue;
                    AnimatorStateTransition t = from.AddTransition(aims[w]);
                    t.hasExitTime = false; t.hasFixedDuration = true; t.duration = from == overEmpty ? 0f : 0.15f;
                    t.AddCondition(AnimatorConditionMode.Equals, w, C.Weapon);
                    t.AddCondition(AnimatorConditionMode.Equals, 0f, C.SwHold);
                }

                for (int h = 0; h < 2; h++)
                {
                    if (from == holds[h]) continue;
                    AnimatorStateTransition t = from.AddTransition(holds[h]);
                    t.hasExitTime = false; t.hasFixedDuration = true; t.duration = 0.15f;
                    t.AddCondition(AnimatorConditionMode.Equals, h + 1, C.SwHold);
                }
            }

            for (int w = 0; w < 3; w++)
            {
                AnimatorState reload = OneShotUpper(osm, aims, holds, C.ReloadState + "_" + w, clips[C.PlayerWeaponClip(w, "reload")], 0.98f, 0.1f);
                reload.speedParameterActive = true; reload.speedParameter = C.ReloadRate;   // clip seconds / WeaponConfig.ReloadTime
                FromAny(osm, reload, 0.05f, (AnimatorConditionMode.If, 0f, C.Reload), (AnimatorConditionMode.Equals, w, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
                AnimatorState sOut = OneShotUpper(osm, aims, holds, C.SwitchOutState + "_" + w, clips[C.PlayerWeaponClip(w, "switch_out")], 0.98f, 0.05f);
                FromAny(osm, sOut, 0.03f, (AnimatorConditionMode.If, 0f, C.SwitchOut), (AnimatorConditionMode.Equals, w, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
                AnimatorState sIn = OneShotUpper(osm, aims, holds, C.SwitchInState + "_" + w, clips[C.PlayerWeaponClip(w, "switch_in")], 0.98f, 0.1f);
                FromAny(osm, sIn, 0.03f, (AnimatorConditionMode.If, 0f, C.SwitchIn), (AnimatorConditionMode.Equals, w, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
            }

            for (int w = 0; w < 3; w++)   // v004: lower / raise per weapon (A = the v003 clip)
            {
                AnimatorState lower = OneShotUpper(osm, aims, holds, C.WeaponLowerState + "_" + w, clips[C.PlayerWeaponLowerClip(w)], 0.98f, 0.1f);
                FromAny(osm, lower, 0.05f, (AnimatorConditionMode.If, 0f, C.Lower), (AnimatorConditionMode.Equals, w, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
                AnimatorState raise = OneShotUpper(osm, aims, holds, C.WeaponRaiseState + "_" + w, clips[C.PlayerWeaponRaiseClip(w)], 0.98f, 0.1f);
                FromAny(osm, raise, 0.05f, (AnimatorConditionMode.If, 0f, C.Raise), (AnimatorConditionMode.Equals, w, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
            }
            AnimatorState hit = OneShotUpper(osm, aims, holds, C.HitState, clips[C.PlayerHitLight], 0.9f, 0.1f);
            FromAny(osm, hit, 0.03f, (AnimatorConditionMode.If, 0f, C.HitLight), (AnimatorConditionMode.If, 0f, C.Alive));
            AnimatorState hurtA = OneShotUpper(osm, aims, holds, C.HurtState + "_A", clips[C.PlayerHurtA], 0.9f, 0.1f);
            FromAny(osm, hurtA, 0.03f, (AnimatorConditionMode.If, 0f, C.Hurt), (AnimatorConditionMode.Equals, 0f, C.HurtStyle), (AnimatorConditionMode.If, 0f, C.Alive));
            AnimatorState pickup = OneShotUpper(osm, aims, holds, C.PickupState, clips[C.PlayerPickupUpper], 0.95f, 0.12f);
            FromAny(osm, pickup, 0.08f, (AnimatorConditionMode.If, 0f, C.Pickup), (AnimatorConditionMode.If, 0f, C.Alive));

            // ---- additive: Fire_<Weapon> (v003 recoil, deltas over the weapon's idle_aim frame 0) + the v001 fire_recoil as the Weapon = 3 fallback
            AnimatorControllerLayer add = AddLayer(c, C.AdditiveLayer, upper, AnimatorLayerBlendingMode.Additive, false);
            AnimatorState addEmpty = add.stateMachine.AddState(C.EmptyState);
            add.stateMachine.defaultState = addEmpty;
            for (int w = 0; w < 3; w++)
            {
                AnimatorState fireW = OneShot(add.stateMachine, C.FireState + "_" + w, clips[C.PlayerWeaponClip(w, "fire")], addEmpty, 0.9f, 0.03f);
                FromAny(add.stateMachine, fireW, 0.0f, (AnimatorConditionMode.If, 0f, C.Fire), (AnimatorConditionMode.Equals, w, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
            }

            AnimatorState fire = OneShot(add.stateMachine, C.FireState, clips[C.PlayerFireRecoil], addEmpty, 0.9f, 0.05f);
            FromAny(add.stateMachine, fire, 0.0f, (AnimatorConditionMode.If, 0f, C.Fire), (AnimatorConditionMode.Equals, 3f, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));

            // ---- aim_layer: IK pass only (PlayerAimLayer)
            AnimatorControllerLayer aimLayer = AddLayer(c, C.AimLayer, aim, AnimatorLayerBlendingMode.Override, true);
            aimLayer.stateMachine.defaultState = aimLayer.stateMachine.AddState(C.EmptyState);

            ApplyLayer(c, 1, l => { l.avatarMask = upper; l.blendingMode = AnimatorLayerBlendingMode.Override; l.defaultWeight = 1f; });
            ApplyLayer(c, 2, l => { l.avatarMask = upper; l.blendingMode = AnimatorLayerBlendingMode.Additive; l.defaultWeight = 1f; });
            ApplyLayer(c, 3, l => { l.avatarMask = aim; l.blendingMode = AnimatorLayerBlendingMode.Override; l.defaultWeight = 1f; l.iKPass = true; });

            states = CountStates(c);
            layers = c.layers.Length;
            EditorUtility.SetDirty(c);
            return c;
        }

        // ------------------------------------------------------------------ zombie

        private static AnimatorController BuildZombie(Dictionary<string, AnimationClip> clips, Dictionary<string, ArtAnimationClipContract.ClipSpec> m,
            AvatarMask upper, out int states, out int layers)
        {
            AnimatorController c = Fresh(ZombieController,
                (C.Speed01, AnimatorControllerParameterType.Float), (C.GaitRate, AnimatorControllerParameterType.Float),
                (C.Gait, AnimatorControllerParameterType.Float), (C.IdleVariant, AnimatorControllerParameterType.Float),
                (C.Waiting, AnimatorControllerParameterType.Bool), (C.WaitPhase, AnimatorControllerParameterType.Float),
                (C.Attack, AnimatorControllerParameterType.Trigger), (C.AttackStyle, AnimatorControllerParameterType.Int), (C.AttackRate, AnimatorControllerParameterType.Float),
                (C.Stagger, AnimatorControllerParameterType.Trigger),
                (C.HitLeft, AnimatorControllerParameterType.Trigger), (C.HitRight, AnimatorControllerParameterType.Trigger),
                (C.FlinchHead, AnimatorControllerParameterType.Trigger), (C.FlinchTorso, AnimatorControllerParameterType.Trigger), (C.FlinchLeg, AnimatorControllerParameterType.Trigger),
                (C.Die, AnimatorControllerParameterType.Trigger), (C.DeathStyle, AnimatorControllerParameterType.Int),
                (C.Rise, AnimatorControllerParameterType.Trigger), (C.Alive, AnimatorControllerParameterType.Bool),
                (C.Telegraph, AnimatorControllerParameterType.Trigger), (C.TelegraphStyle, AnimatorControllerParameterType.Int), (C.TelegraphRate, AnimatorControllerParameterType.Float),
                // WP-032C tier reactions / variants
                (C.HitLight, AnimatorControllerParameterType.Trigger), (C.HitDir, AnimatorControllerParameterType.Int), (C.HitTier, AnimatorControllerParameterType.Int), (C.RiseVariant, AnimatorControllerParameterType.Int),
                (C.ArmorBreak, AnimatorControllerParameterType.Trigger), (C.ArmorBreakRegion, AnimatorControllerParameterType.Int), (C.Stumble, AnimatorControllerParameterType.Trigger));
            SetDefault(c, C.GaitRate, 1f);
            SetDefault(c, C.AttackRate, 1f);
            SetDefault(c, C.TelegraphRate, 1f);
            SetDefault(c, C.Alive, true);

            // ---- base: Locomotion = Speed01 blend of (idle by IdleVariant) and (gait by Gait)
            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState loco = c.CreateBlendTreeInController(C.LocomotionState, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = C.Speed01;
            tree.useAutomaticThresholds = false;
            BlendTree idle = tree.CreateBlendTreeChild(0f);
            idle.name = C.IdleState;
            idle.blendType = BlendTreeType.Simple1D;
            idle.blendParameter = C.IdleVariant;
            idle.useAutomaticThresholds = false;
            idle.children = new[] { Child1D(clips[C.ZombieIdleSway], 0f), Child1D(clips[C.ZombieIdleTwitch], 1f) };
            BlendTree gait = tree.CreateBlendTreeChild(1f);
            gait.name = C.GaitState;
            gait.blendType = BlendTreeType.Simple1D;
            gait.blendParameter = C.Gait;
            gait.useAutomaticThresholds = false;
            gait.children = new[]
            {
                Child1D(clips[C.ZombieShambleA], 0f), Child1D(clips[C.ZombieShambleB], 1f), Child1D(clips[C.ZombieShambleC], 2f), Child1D(clips[C.ZombieRun], 3f)
            };
            loco.speedParameterActive = true;
            loco.speedParameter = C.GaitRate;
            loco.writeDefaultValues = true;
            sm.defaultState = loco;

            // WP-032B: Wait = the v002 queue idle, entered from Locomotion on Waiting and left on !Waiting;
            // the cycle offset is the body's WaitPhase so a throat queue never shifts its weight in unison.
            AnimatorState wait = sm.AddState(C.WaitState);
            wait.motion = clips[C.ZombieIdleQueue];
            wait.writeDefaultValues = false;
            wait.cycleOffsetParameterActive = true;
            wait.cycleOffsetParameter = C.WaitPhase;
            AnimatorStateTransition toWait = loco.AddTransition(wait);
            toWait.hasExitTime = false; toWait.hasFixedDuration = true; toWait.duration = 0.35f;
            toWait.AddCondition(AnimatorConditionMode.If, 0f, C.Waiting);
            toWait.AddCondition(AnimatorConditionMode.If, 0f, C.Alive);
            AnimatorStateTransition fromWait = wait.AddTransition(loco);
            fromWait.hasExitTime = false; fromWait.hasFixedDuration = true; fromWait.duration = 0.35f;
            fromWait.AddCondition(AnimatorConditionMode.IfNot, 0f, C.Waiting);

            // attacks (speed = AttackRate → hit_window_start lands on TelegraphTime); WP-032B: + overhead (Ultra); WP-032C: the v003
            // overhead carries its own 33-frame recovery, OverheadHoldSeconds = 0 → the common exit.
            string[] attackStates = { C.AttackSwipeState, C.AttackLungeState, C.AttackSweepState, C.AttackOverheadState };
            for (int i = 0; i < C.ZombieAttackClips.Length; i++)
            {
                bool hold = i == 3 && OverheadHoldSeconds > 0f;
                AnimatorState a = OneShot(sm, attackStates[i], clips[C.ZombieAttackClips[i]], loco, hold ? 1f : 0.97f, hold ? OverheadHoldSeconds : 0.12f);
                a.speedParameterActive = true;
                a.speedParameter = C.AttackRate;
                FromAny(sm, a, 0.06f, (AnimatorConditionMode.If, 0f, C.Attack), (AnimatorConditionMode.Equals, (float)i, C.AttackStyle), (AnimatorConditionMode.If, 0f, C.Alive));
            }

            AnimatorState stagger = OneShot(sm, C.StaggerState, clips[C.ZombieHitHeavyStagger], loco, 0.95f, 0.15f);
            FromAny(sm, stagger, 0.05f, (AnimatorConditionMode.If, 0f, C.Stagger), (AnimatorConditionMode.If, 0f, C.Alive));

            // WP-032C: armour break by region (base, full body — replaces the heavy stagger as the armour-break reaction) and the Runner stumble
            for (int r = 0; r < C.ArmorBreakRegions.Length; r++)
            {
                AnimatorState ab = OneShot(sm, C.ArmorBreakState + "_" + r, clips[C.ZombieArmorBreakClip(r)], loco, 0.95f, 0.12f);
                FromAny(sm, ab, 0.04f, (AnimatorConditionMode.If, 0f, C.ArmorBreak), (AnimatorConditionMode.Equals, (float)r, C.ArmorBreakRegion), (AnimatorConditionMode.If, 0f, C.Alive));
            }

            AnimatorState stumble = OneShot(sm, C.StumbleState, clips[C.ZombieHitSpecialStumble], loco, 0.95f, 0.12f);
            FromAny(sm, stumble, 0.04f, (AnimatorConditionMode.If, 0f, C.Stumble), (AnimatorConditionMode.If, 0f, C.Alive));

            for (int i = 0; i < C.ZombieDeathClips.Length; i++)   // WP-032C: 8 styles (+ side_R, special_A/B, ultra)
            {
                AnimatorState d = OneShot(sm, C.DeathState + "_" + i, clips[C.ZombieDeathClips[i]], null);
                FromAny(sm, d, 0.05f, (AnimatorConditionMode.If, 0f, C.Die), (AnimatorConditionMode.Equals, (float)i, C.DeathStyle));
            }

            for (int i = 0; i < C.ZombieRiseClips.Length; i++)   // WP-032C: Rise_0 (A 48 f), Rise_1 (B 36 f), Rise_2 (C 28 f) by RiseVariant; B/C carry spawn_rise / spawn_land
            {
                AnimatorState rs = OneShot(sm, i == 0 ? C.RiseState : C.RiseState + "_" + i, clips[C.ZombieRiseClips[i]], loco, 0.97f, 0.15f);
                FromAny(sm, rs, 0.0f, (AnimatorConditionMode.If, 0f, C.Rise), (AnimatorConditionMode.Equals, (float)i, C.RiseVariant), (AnimatorConditionMode.If, 0f, C.Alive));
            }

            // ---- override_upper: WP-032C directional × tier hits (Hit_<dir>_<tier>, HitDir 0..3 × HitTier 0..2; the Ultra reads are
            // upper-body only by design) + the v001 hit_light L / R (HitL / HitR triggers, kept for the block-rig parity path)
            AnimatorControllerLayer over = AddLayer(c, C.OverrideUpperLayer, upper, AnimatorLayerBlendingMode.Override, false);
            AnimatorState overEmpty = over.stateMachine.AddState(C.EmptyState);
            over.stateMachine.defaultState = overEmpty;
            for (int dir = 0; dir < 4; dir++)
            for (int tier = 0; tier < 3; tier++)
            {
                AnimatorState h = OneShot(over.stateMachine, C.HitState + "_" + C.HitDirections[dir] + "_" + C.HitTiers[tier], clips[C.ZombieHitClip(dir, tier)], overEmpty, 0.9f, 0.1f);
                FromAny(over.stateMachine, h, 0.03f, (AnimatorConditionMode.If, 0f, C.HitLight), (AnimatorConditionMode.Equals, (float)dir, C.HitDir), (AnimatorConditionMode.Equals, (float)tier, C.HitTier), (AnimatorConditionMode.If, 0f, C.Alive));
            }

            AnimatorState hitL = OneShot(over.stateMachine, C.HitState + "_L", clips[C.ZombieHitLightL], overEmpty, 0.9f, 0.1f);
            AnimatorState hitR = OneShot(over.stateMachine, C.HitState + "_R", clips[C.ZombieHitLightR], overEmpty, 0.9f, 0.1f);
            FromAny(over.stateMachine, hitL, 0.03f, (AnimatorConditionMode.If, 0f, C.HitLeft), (AnimatorConditionMode.If, 0f, C.Alive));
            FromAny(over.stateMachine, hitR, 0.03f, (AnimatorConditionMode.If, 0f, C.HitRight), (AnimatorConditionMode.If, 0f, C.Alive));

            // ---- additive: regional flinches (each clip only moves its region; deltas from frame 0)
            AnimatorControllerLayer add = AddLayer(c, C.AdditiveLayer, null, AnimatorLayerBlendingMode.Additive, false);
            AnimatorState addEmpty = add.stateMachine.AddState(C.EmptyState);
            add.stateMachine.defaultState = addEmpty;
            AnimatorState fHead = OneShot(add.stateMachine, C.FlinchState + "_Head", clips[C.ZombieHitRegionHead], addEmpty, 0.9f, 0.05f);
            AnimatorState fTorso = OneShot(add.stateMachine, C.FlinchState + "_Torso", clips[C.ZombieHitRegionTorso], addEmpty, 0.9f, 0.05f);
            AnimatorState fLeg = OneShot(add.stateMachine, C.FlinchState + "_Leg", clips[C.ZombieHitRegionLeg], addEmpty, 0.9f, 0.05f);
            FromAny(add.stateMachine, fHead, 0f, (AnimatorConditionMode.If, 0f, C.FlinchHead), (AnimatorConditionMode.If, 0f, C.Alive));
            FromAny(add.stateMachine, fTorso, 0f, (AnimatorConditionMode.If, 0f, C.FlinchTorso), (AnimatorConditionMode.If, 0f, C.Alive));
            FromAny(add.stateMachine, fLeg, 0f, (AnimatorConditionMode.If, 0f, C.FlinchLeg), (AnimatorConditionMode.If, 0f, C.Alive));

            // ---- telegraph (WP-032): additive upper-body emphasis over the wind-up, generated clips, weight = tier emphasis at runtime
            AnimationClip[] telegraph = BuildTelegraphClips();
            AnimatorControllerLayer tele = AddLayer(c, C.TelegraphLayer, upper, AnimatorLayerBlendingMode.Additive, false);
            AnimatorState teleEmpty = tele.stateMachine.AddState(C.EmptyState);
            tele.stateMachine.defaultState = teleEmpty;
            for (int i = 0; i < telegraph.Length; i++)
            {
                AnimatorState t = OneShot(tele.stateMachine, C.TelegraphState + "_" + i, telegraph[i], teleEmpty, 0.98f, 0.08f);
                t.speedParameterActive = true;
                t.speedParameter = C.TelegraphRate;
                FromAny(tele.stateMachine, t, 0f, (AnimatorConditionMode.If, 0f, C.Telegraph), (AnimatorConditionMode.Equals, (float)i, C.TelegraphStyle), (AnimatorConditionMode.If, 0f, C.Alive));
            }

            ApplyLayer(c, 0, l => { l.iKPass = true; });   // WP-032: LookAt + foot IK
            // WP-032 (ADR-032-1): Humanoid retargeting shortened the authored stride (shamble_A 0.78 → 0.51 m measured);
            // the built-in foot IK on the base states restores the authored foot placement, so the manifest
            // speed_mps stays the honest rate reference.
            foreach (ChildAnimatorState cs in c.layers[0].stateMachine.states) cs.state.iKOnFeet = true;
            ApplyLayer(c, 1, l => { l.avatarMask = upper; l.blendingMode = AnimatorLayerBlendingMode.Override; l.defaultWeight = 1f; });
            ApplyLayer(c, 2, l => { l.avatarMask = null; l.blendingMode = AnimatorLayerBlendingMode.Additive; l.defaultWeight = 1f; });
            ApplyLayer(c, 3, l => { l.avatarMask = upper; l.blendingMode = AnimatorLayerBlendingMode.Additive; l.defaultWeight = 1f; });

            states = CountStates(c);
            layers = c.layers.Length;
            EditorUtility.SetDirty(c);
            return c;
        }

        // ------------------------------------------------------ WP-032 telegraph clips

        /// <summary>
        /// Three 1 s additive Humanoid clips (muscle curves, reference pose = frame 0 = zero) that
        /// exaggerate the wind-up silhouette the AD could not read at 19.9 m / −49°: swipe = the
        /// striking arm rises above the helmet line with a back-lean; lunge = crouch (body down,
        /// arms back/out); sweep = both arms wide with a chest twist. Played on the additive
        /// telegraph layer at TelegraphRate = 1 / wind-up seconds, so the emphasis peaks just
        /// before hit_window_start and is gone by it. Bridge until AP-009B re-cuts the clips.
        /// </summary>
        public static AnimationClip[] BuildTelegraphClips()
        {
            EnsureFolder(GeneratedFolder);
            var clips = new AnimationClip[C.TelegraphClips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                string path = GeneratedFolder + "/" + C.TelegraphClips[i] + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                bool created = clip == null;
                if (created)
                {
                    clip = new AnimationClip { name = C.TelegraphClips[i] };
                    AssetDatabase.CreateAsset(clip, path);
                }

                clip.ClearCurves();
                clip.frameRate = 30f;
                switch (i)
                {
                    case 0:   // swipe: right arm up over the helmet line, forearm folds, spine leans back then releases
                        Muscle(clip, "Right Arm Down-Up", 0f, 0.6f, 0.9f, 0.95f, 0f);
                        Muscle(clip, "Right Arm Front-Back", 0f, 0.3f, 0.45f, 0.45f, 0f);
                        Muscle(clip, "Right Forearm Stretch", 0f, -0.35f, -0.55f, -0.55f, 0f);
                        Muscle(clip, "Spine Front-Back", 0f, -0.25f, -0.4f, -0.4f, 0f);
                        Muscle(clip, "Chest Twist Left-Right", 0f, 0.2f, 0.35f, 0.35f, 0f);
                        break;
                    case 1:   // lunge (AD S1): deep crouch with head/chest forward, arms abducted wide and back (hands outside the hips)
                        Muscle(clip, "Spine Front-Back", 0f, 0.4f, 0.65f, 0.65f, 0f);
                        Muscle(clip, "Chest Front-Back", 0f, 0.3f, 0.5f, 0.5f, 0f);
                        Muscle(clip, "Head Nod Down-Up", 0f, -0.25f, -0.4f, -0.4f, 0f);
                        Muscle(clip, "Left Arm Down-Up", 0f, 0.45f, 0.7f, 0.7f, 0f);
                        Muscle(clip, "Right Arm Down-Up", 0f, 0.45f, 0.7f, 0.7f, 0f);
                        Muscle(clip, "Left Arm Front-Back", 0f, -0.35f, -0.6f, -0.6f, 0f);
                        Muscle(clip, "Right Arm Front-Back", 0f, -0.35f, -0.6f, -0.6f, 0f);
                        Muscle(clip, "Left Forearm Stretch", 0f, 0.3f, 0.5f, 0.5f, 0f);
                        Muscle(clip, "Right Forearm Stretch", 0f, 0.3f, 0.5f, 0.5f, 0f);
                        Muscle(clip, "RootT.y", 0f, -0.1f, -0.18f, -0.18f, 0f);
                        break;
                    default:  // sweep (AD S1): both arms wide and high with OPEN elbows (hands outside the shoulder line), chest wound to the side
                        Muscle(clip, "Left Arm Down-Up", 0f, 0.55f, 0.9f, 0.9f, 0f);
                        Muscle(clip, "Right Arm Down-Up", 0f, 0.55f, 0.9f, 0.9f, 0f);
                        Muscle(clip, "Left Arm Front-Back", 0f, -0.15f, -0.25f, -0.25f, 0f);
                        Muscle(clip, "Right Arm Front-Back", 0f, -0.15f, -0.25f, -0.25f, 0f);
                        Muscle(clip, "Left Forearm Stretch", 0f, 0.4f, 0.7f, 0.7f, 0f);
                        Muscle(clip, "Right Forearm Stretch", 0f, 0.4f, 0.7f, 0.7f, 0f);
                        Muscle(clip, "Chest Twist Left-Right", 0f, -0.3f, -0.5f, -0.5f, 0f);
                        Muscle(clip, "Spine Twist Left-Right", 0f, -0.15f, -0.3f, -0.3f, 0f);
                        break;
                }

                AnimationUtility.SetAdditiveReferencePose(clip, clip, 0f);
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = false;
                settings.keepOriginalPositionY = true;
                settings.keepOriginalOrientation = true;
                settings.keepOriginalPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
                clips[i] = clip;
            }

            return clips;
        }

        /// <summary>Muscle curve over the 1 s clip: keys at 0 / 0.25 / 0.6 / 0.9 / 1.0 s (AD S2: ≥ half the peak by 40 % of the wind-up; hold; release before the strike frame).</summary>
        private static void Muscle(AnimationClip clip, string muscle, float k0, float k1, float k2, float k3, float k4)
        {
            var curve = new AnimationCurve(new Keyframe(0f, k0), new Keyframe(0.25f, k1), new Keyframe(0.6f, k2), new Keyframe(0.9f, k3), new Keyframe(1f, k4));
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            clip.SetCurve("", typeof(Animator), muscle, curve);
        }

        // ------------------------------------------------------------------ helpers

        private static ChildMotion Child1D(Motion motion, float threshold)
        {
            return new ChildMotion { motion = motion, threshold = threshold, timeScale = 1f, directBlendParameter = "" };
        }

        /// <summary>WP-032B: a walk child at its own no-slide radius (clip speed / reference) along (x, y), time scale reference / clip speed.</summary>
        private static ChildMotion Ring(Dictionary<string, AnimationClip> clips, Dictionary<string, ArtAnimationClipContract.ClipSpec> m, Dictionary<string, float> unity,
            float reference, string clip, float x, float y)
        {
            float speed = SpeedOf(m, unity, clip);
            float radius = Mathf.Clamp(speed / Mathf.Max(0.1f, reference), 0.2f, 0.9f);
            return Child2D(clips[clip], x * radius, y * radius, PlayerSkinnedVisual.ChildTimeScale(reference, speed));
        }

        private static ChildMotion Child2D(Motion motion, float x, float y, float timeScale)
        {
            return new ChildMotion { motion = motion, position = new Vector2(x, y), timeScale = timeScale, directBlendParameter = "" };
        }

        private static void SetDefault(AnimatorController c, string name, float value)
        {
            AnimatorControllerParameter[] ps = c.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].name == name) ps[i].defaultFloat = value;
            }

            c.parameters = ps;
        }

        private static void SetDefault(AnimatorController c, string name, bool value)
        {
            AnimatorControllerParameter[] ps = c.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].name == name) ps[i].defaultBool = value;
            }

            c.parameters = ps;
        }

        public static int CountStates(AnimatorController c)
        {
            int n = 0;
            foreach (AnimatorControllerLayer l in c.layers) n += l.stateMachine.states.Length;
            return n;
        }

        public static AnimatorState FindState(AnimatorController c, int layer, string name)
        {
            foreach (ChildAnimatorState s in c.layers[layer].stateMachine.states)
            {
                if (s.state.name == name) return s.state;
            }

            return null;
        }

        public static int LayerIndex(AnimatorController c, string name)
        {
            for (int i = 0; i < c.layers.Length; i++)
            {
                if (c.layers[i].name == name) return i;
            }

            return -1;
        }

        private static void WriteSet(string setPath, AnimatorController controller, Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest,
            Dictionary<string, AnimationClip> clips, Dictionary<string, float> unitySpeeds, string target, string source, float referenceSpeed, AnimationClip idle, AnimationClip walk, float walkSpeed)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(setPath);
            bool created = set == null;
            if (created)
            {
                set = ScriptableObject.CreateInstance<CharacterAnimationSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }

            var table = new List<CharacterAnimationSet.Entry>();
            var names = new List<string>(manifest.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
            {
                ArtAnimationClipContract.ClipSpec s = manifest[name];
                if (s.target != target) continue;
                table.Add(new CharacterAnimationSet.Entry
                {
                    name = s.name, clip = clips[s.name], loop = s.loop, seconds = s.seconds, speedMps = s.speedMps,
                    unitySpeedMps = unitySpeeds != null && unitySpeeds.TryGetValue(s.name, out float u) ? u : 0f,
                    layer = s.layer, additive = s.additive, frames = s.frames, events = s.EventsText(), stanceFrames = s.stanceFrames
                });
            }

            set.Configure(idle, walk, walkSpeed, controller, source, table, ArtAnimationClipContract.ManifestSchema, referenceSpeed);
            EditorUtility.SetDirty(set);
            Debug.Log(LogPrefix + "animation set " + (created ? "created" : "updated") + ": " + setPath + " (" + table.Count + " clips, controller " + controller.name + ")");
        }

        /// <summary>The avatar source model with an Animator (avatar set), for clip sampling in the editor.</summary>
        /// <summary>
        /// The character's shipped prefab (Player_AP006 / Zombie_AP007: the runtime Animator, avatar and skinned hierarchy)
        /// when it exists — WP-032C: sampling the avatar SOURCE model under-read the v004 run_fwd by 15 % against the live
        /// rig (the PlayMode slip test), the prefab samples the same bones the game plays; the source model is the fallback
        /// for a first build. The returned object is the Animator's GameObject (SampleAnimation root).
        /// </summary>
        private static GameObject InstantiateCalibrationRig(string target)
        {
            string prefabPath = target == "player" ? ArtCharacterPrefabBuilder.PlayerPrefab : ArtCharacterPrefabBuilder.ZombiePrefab;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.hideFlags = HideFlags.HideAndDontSave;
                Animator prefabAnimator = instance.GetComponentInChildren<Animator>(true);
                if (prefabAnimator != null && prefabAnimator.avatar != null)
                {
                    prefabAnimator.applyRootMotion = false;
                    prefabAnimator.runtimeAnimatorController = null;   // SampleAnimation only; the controller is being rebuilt
                    s_calibrationInstance = instance;
                    return prefabAnimator.gameObject;
                }

                UnityEngine.Object.DestroyImmediate(instance);
            }

            string source = target == "player" ? ArtCharacterContract.PlayerAvatarSource : ArtCharacterContract.ZombieAvatarSource;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (model == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            Animator animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.avatar = ArtCharacterContract.AvatarOf(source);
            animator.applyRootMotion = false;
            s_calibrationInstance = go;
            return go;
        }

        private static GameObject s_calibrationInstance;

        private static void DestroyCalibrationRig()
        {
            if (s_calibrationInstance != null) UnityEngine.Object.DestroyImmediate(s_calibrationInstance);
            s_calibrationInstance = null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
