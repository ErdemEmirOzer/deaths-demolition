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
        public const string PlayerFpController = ArtCharacterContract.PlayerFolder + "/Generated/DD_Player_FP.controller";   // WP-071 B4b-3
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

        /// <summary>WP-096: the last zombie stride calibration (ArtZombieStrideCalibration) of a build, for the intake line.</summary>
        public static string ZombieCalibrationSummary = "calibration=not run";

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
            WriteWeaponSets(ArtCharacterPrefabBuilder.PlayerAnimationSet, player, clips, manifest);
            WriteFirstPerson(ArtCharacterPrefabBuilder.PlayerAnimationSet, clips, manifest);
            WriteSet(ArtCharacterPrefabBuilder.ZombieAnimationSet, zombie, manifest, clips, unitySpeeds, "zombie", ZombieSource, manifest[C.ZombieShambleA].speedMps,
                clips[C.ZombieIdleSway], clips[C.ZombieShambleA], manifest[C.ZombieShambleA].speedMps);
            // WP-096: the AP-033 gaits' runtime speed = their stride measured on the shipped zombie rig with this controller
            ArtZombieStrideCalibration.Apply(AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet), zombie, manifest, out ZombieCalibrationSummary);

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "built: " + report.clips + " clips, player " + report.playerStates + " states / " + report.playerLayers + " layers, zombie "
                      + report.zombieStates + " states / " + report.zombieLayers + " layers");
            return report;
        }

        /// <summary>
        /// WP-096 (AP-033 p1) · the zombie half of <see cref="BuildAll"/> only: the zombie rig calibration, DD_Zombie.controller and
        /// CharacterAnimationSet_Zombie.asset, rebuilt in place (GUIDs kept; the set keeps its per-clip minRate / maxRate). The player
        /// controller, its sets, the FP viewmodel and the masks are left as they are on disk (the UpperBody mask is read, built only
        /// when missing) — a zombie clip intake does not regenerate the player's YAML.
        /// </summary>
        public static Report BuildZombieOnly()
        {
            Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest = ArtAnimationClipContract.Manifest();
            if (manifest == null) throw new FileNotFoundException(LogPrefix + "manifest missing", ArtAnimationClipContract.ManifestPath);
            if (manifest.Count != C.ClipCount) throw new InvalidDataException(LogPrefix + "manifest lists " + manifest.Count + " clips != " + C.ClipCount);

            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (ArtAnimationClipContract.ClipSpec spec in manifest.Values)
            {
                if (spec.target != "zombie") continue;
                AnimationClip clip = ArtAnimationClipContract.ClipOf(spec);
                if (clip == null) throw new InvalidOperationException(LogPrefix + "clip not imported: " + spec.name + " (" + ArtAnimationClipContract.FbxPath(spec) + ")");
                clips[spec.name] = clip;
            }

            AvatarMask upper = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMask);
            if (upper == null)
            {
                EnsureFolder(MasksFolder);
                upper = BuildMask(UpperBodyMask, body: true, head: true, arms: true, legs: false, root: false);
            }

            var report = new Report { clips = clips.Count };
            Dictionary<string, float> unitySpeeds = Calibrate(manifest, clips, "zombie");
            AnimatorController zombie = BuildZombie(clips, manifest, upper, out report.zombieStates, out report.zombieLayers);
            WriteSet(ArtCharacterPrefabBuilder.ZombieAnimationSet, zombie, manifest, clips, unitySpeeds, "zombie", ZombieSource, manifest[C.ZombieShambleA].speedMps,
                clips[C.ZombieIdleSway], clips[C.ZombieShambleA], manifest[C.ZombieShambleA].speedMps);
            // WP-096: the AP-033 gaits' runtime speed = their stride measured on the shipped zombie rig with this controller
            ArtZombieStrideCalibration.Apply(AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet), zombie, manifest, out ZombieCalibrationSummary);

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "built (zombie only): " + report.clips + " zombie clips, " + report.zombieStates + " states / " + report.zombieLayers + " layers");
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

        /// <summary>
        /// WP-083 B3b (C-00 05.10, locomotion v4 skeleton): the LocomotionV4 sub-state machine on the base layer — one state per AP-026
        /// start / stop / pivot / turn clip present in <paramref name="clips"/>, entered from AnyState when the int
        /// <see cref="LocomotionV4.ClipParam"/> equals its id (and Alive, when the controller has it), back to Locomotion near its end.
        /// Adds nothing (not even the parameter) while no such clip exists. Returns the number of states added.
        /// </summary>
        public static int AddLocomotionV4(AnimatorController c, AnimatorStateMachine sm, AnimatorState loco, IDictionary<string, AnimationClip> clips)
        {
            int n = 0;
            for (int id = 0; id < LocomotionV4.Count; id++) if (clips.ContainsKey(LocomotionV4.ClipName(id))) n++;
            if (n == 0) return 0;
            c.AddParameter(new AnimatorControllerParameter { name = LocomotionV4.ClipParam, type = AnimatorControllerParameterType.Int, defaultInt = LocomotionV4.None });
            bool alive = false;
            foreach (AnimatorControllerParameter p in c.parameters) if (p.name == C.Alive) alive = true;
            AnimatorStateMachine v4 = sm.AddStateMachine(LocomotionV4.StateMachine);
            for (int id = 0; id < LocomotionV4.Count; id++)
            {
                if (!clips.TryGetValue(LocomotionV4.ClipName(id), out AnimationClip clip) || clip == null) continue;
                AnimatorState s = v4.AddState(LocomotionV4.StateName(id));
                s.motion = clip;
                s.writeDefaultValues = true;
                AnimatorStateTransition into = sm.AddAnyStateTransition(s);
                into.hasExitTime = false; into.hasFixedDuration = true; into.duration = 0.1f; into.canTransitionToSelf = false;
                into.AddCondition(AnimatorConditionMode.Equals, id, LocomotionV4.ClipParam);
                if (alive) into.AddCondition(AnimatorConditionMode.If, 0f, C.Alive);
                AnimatorStateTransition back = s.AddTransition(loco);
                back.hasExitTime = true; back.exitTime = 0.85f; back.hasFixedDuration = true; back.duration = 0.15f;
            }

            return n;
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
                if (aims[w] == null) continue;
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
            // WP-071 MUST-4 (a): the four AP-009 v004 run entries (5.91 / 4.9 / 5.85 / 5.85) left with AP-009 — the contract names are AP-022
            // now, whose contact "spd" is authoritative (MUST-4 c); re-measure with AnimationLiveCalibrationTests before adding any entry back.
            // WP-071 MUST-4 (c) + D-1 (AnimationLiveCalibrationTests at the backward top speed 4.03 m/s): shipped 3.60 slips −7.8 cm/stance
            // (r29), shipped 4.10 slips +4.1 cm (r30) → zero ≈ 3.93 m/s = the ankle sampler's 3.94; the Blender contact spd 3.60 under-reads it
            { C.PlayerRunBack, 3.93f },
            // WP-071 MUST-I1 (r24b): the AP-022 zombie_run read 4.885 sampled (contact spd 5.30); the PlayMode contact slip at 4.6 m/s over
            // 4.885 measured −7.9 cm/step → zero crossing ≈ 5.35 m/s, within 1 % of the contact spd the builder now uses.
        };

        /// <summary>WP-032: measure every locomotion clip on the character's avatar (StrideCalibrator); keyed by clip name.</summary>
        public static Dictionary<string, float> Calibrate(Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest, Dictionary<string, AnimationClip> clips, string onlyTarget = null)
        {
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (string target in new[] { "player", "zombie" })
            {
                if (onlyTarget != null && target != onlyTarget) continue;   // WP-096: BuildZombieOnly
                GameObject rig = InstantiateCalibrationRig(target);
                if (rig == null) continue;
                try
                {
                    foreach (ArtAnimationClipContract.ClipSpec s in manifest.Values)
                    {
                        if (s.target != target || s.speedMps <= 0.01f) continue;
                        // WP-071 MUST-4 (b): the AP-022 travel axis from the row (travel_dir_unity_xz), the name rule only without it
                        Vector3 footTravel = s.hasTravelDir ? -s.bodyTravel.normalized : StrideCalibrator.FootTravelDirection(s.name);
                        StrideCalibrator.Result r = StrideCalibrator.Measure(rig, rig.GetComponent<Animator>(), clips[s.name], s.frames, ArtAnimationClipContract.Fps, s.EventsText(),
                            footTravel, s.stanceFrames);
                        if (r.windows > 0) result[s.name] = r.speedMps;
                        // WP-071 MUST-4 (c): an AP-022 contact-point "spd" is authoritative — the ankle sampler under-reads (walk_fwd −8.6 %, run_fwd −9.1 %, zombie_run −7.8 %)
                        if (s.contactSpeed) result[s.name] = s.speedMps;
                        Debug.Log(LogPrefix + "stride " + s.name + ": manifest " + s.speedMps.ToString("F3") + " m/s → Unity " + r.speedMps.ToString("F3") + " m/s (" + r.windows + " windows: " + r.report + ")"
                                  + (s.contactSpeed ? " — contact spd " + s.speedMps.ToString("F3") + " used" : ""));
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
                if (onlyTarget != null && manifest[o.Key].target != onlyTarget) continue;
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
                (C.ReloadEmpty, AnimatorControllerParameterType.Bool),
                (C.SwitchOut, AnimatorControllerParameterType.Trigger), (C.SwitchIn, AnimatorControllerParameterType.Trigger),
                (C.Lower, AnimatorControllerParameterType.Trigger), (C.Raise, AnimatorControllerParameterType.Trigger),
                (C.SwHold, AnimatorControllerParameterType.Int), (C.Hurt, AnimatorControllerParameterType.Trigger), (C.HurtStyle, AnimatorControllerParameterType.Int),
                (C.SwFire, AnimatorControllerParameterType.Trigger), (C.Cycle, AnimatorControllerParameterType.Trigger),
                (C.AdsWeight, AnimatorControllerParameterType.Float), (C.FireHeld, AnimatorControllerParameterType.Bool),
                (C.Still, AnimatorControllerParameterType.Bool));
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
            // WP-071 MUST-3 (P-06 AD refuter B): the centre is a static stand (idle frame 0 held) as long as the walk cycle and at the walk
            // children's time scale, so it never stretches the tree cycle; the idle breath plays in its own state (Idle_Breath, below).
            float walkScale = PlayerSkinnedVisual.ChildTimeScale(reference, SpeedOf(m, unitySpeeds, C.PlayerWalkFwd));
            AnimationClip stand = BuildStandStillClip(clips[C.PlayerIdle], clips[C.PlayerWalkFwd].length);
            tree.children = new[]
            {
                stand != null ? Child2D(stand, 0f, 0f, walkScale) : Child2D(clips[C.PlayerIdle], 0f, 0f, 1f),
                Ring(clips, m, unitySpeeds, reference, C.PlayerWalkFwd, 0f, 1f),
                Ring(clips, m, unitySpeeds, reference, C.PlayerWalkBack, 0f, -1f),
                Ring(clips, m, unitySpeeds, reference, C.PlayerStrafeLeft, -1f, 0f),
                Ring(clips, m, unitySpeeds, reference, C.PlayerStrafeRight, 1f, 0f),
                Child2D(clips[C.PlayerRunFwd], 0f, 1f, PlayerSkinnedVisual.RunChildTimeScale(reference, SpeedOf(m, unitySpeeds, C.PlayerRunFwd))),
                // WP-071 D-1 (C-00 01.10, user: backward ×0.65, S-02 PlayerConfigSection): run_back sits at the backward top speed (4.03 m/s → clip rate 1.12)
                Child2D(clips[C.PlayerRunBack], 0f, -DD.Prototype.Core.Config.PlayerConfigSection.BackwardSpeedMultiplier,
                    PlayerSkinnedVisual.RunChildTimeScale(reference, SpeedOf(m, unitySpeeds, C.PlayerRunBack), DD.Prototype.Core.Config.PlayerConfigSection.BackwardSpeedMultiplier)),
                Child2D(clips[C.PlayerRunStrafeLeft], -1f, 0f, PlayerSkinnedVisual.RunChildTimeScale(reference, SpeedOf(m, unitySpeeds, C.PlayerRunStrafeLeft))),
                Child2D(clips[C.PlayerRunStrafeRight], 1f, 0f, PlayerSkinnedVisual.RunChildTimeScale(reference, SpeedOf(m, unitySpeeds, C.PlayerRunStrafeRight)))
            };
            loco.speedParameterActive = true;
            loco.speedParameter = C.LocoRate;
            loco.writeDefaultValues = true;
            sm.defaultState = loco;
            // WP-071 MUST-3: the idle breath at rate 1 in its own base state — in after 0.2 s under 0.1 m/s (PlayerSkinnedVisual sets Still), 0.12 s blends
            AnimatorState breath = sm.AddState(C.StillState);
            breath.motion = clips[C.PlayerIdle];
            breath.writeDefaultValues = true;
            AnimatorStateTransition toBreath = loco.AddTransition(breath);
            toBreath.hasExitTime = false; toBreath.hasFixedDuration = true; toBreath.duration = 0.12f;
            toBreath.AddCondition(AnimatorConditionMode.If, 0f, C.Still);
            AnimatorStateTransition fromBreath = breath.AddTransition(loco);
            fromBreath.hasExitTime = false; fromBreath.hasFixedDuration = true; fromBreath.duration = 0.12f;
            fromBreath.AddCondition(AnimatorConditionMode.IfNot, 0f, C.Still);
            // WP-083 B3b: the v4 start / stop / pivot sub-state machine — only the AP-026 clips the set carries (none until AP-024/026)
            AddLocomotionV4(c, sm, loco, clips);

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
            // aims[] is indexed by the Weapon pose: 0..2 the M3 classes, 3 none (Empty), 4 the WP-071 v3 generic set (AOC per weapon)
            string v3Base = C.WeaponAnimSetsV3[0];
            bool v3 = clips.ContainsKey(C.PlayerWeaponClipV3(v3Base, "idle_aim"));
            var aims = new AnimatorState[v3 ? C.WeaponPoseV3 + 1 : 3];
            for (int w = 0; w < 3; w++)
            {
                aims[w] = osm.AddState(C.AimState + "_" + w);
                aims[w].motion = clips[C.PlayerWeaponClip(w, "idle_aim")];
                aims[w].writeDefaultValues = false;
            }

            if (v3)
            {
                aims[C.WeaponPoseV3] = osm.AddState(C.AimV3State);
                aims[C.WeaponPoseV3].motion = clips[C.PlayerWeaponClipV3(v3Base, "idle_aim")];
                aims[C.WeaponPoseV3].writeDefaultValues = false;
                // WP-071 r2: TP aim down sights — the rest blends hip idle_aim → ads_idle by AdsWeight (the per-set AOC swaps both)
                if (clips.TryGetValue(C.PlayerWeaponClipV3(v3Base, "ads_idle"), out AnimationClip adsIdle))
                {
                    var adsTree = new BlendTree { name = C.AimV3State + "_Ads", blendType = BlendTreeType.Simple1D, blendParameter = C.AdsWeight, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
                    AssetDatabase.AddObjectToAsset(adsTree, AssetDatabase.GetAssetPath(c));
                    adsTree.children = new[] { Child1D(clips[C.PlayerWeaponClipV3(v3Base, "idle_aim")], 0f), Child1D(adsIdle, 1f) };
                    aims[C.WeaponPoseV3].motion = adsTree;
                }
            }

            var holds = new AnimatorState[3];
            holds[0] = osm.AddState(C.SwHoldState + "_1"); holds[0].motion = clips[C.PlayerSwHoldBell]; holds[0].writeDefaultValues = false;
            holds[1] = osm.AddState(C.SwHoldState + "_2"); holds[1].motion = clips[C.PlayerSwHoldLoom]; holds[1].writeDefaultValues = false;
            holds[2] = osm.AddState(C.SwHoldState + "_3"); holds[2].motion = clips[C.PlayerSwHoldJewel]; holds[2].writeDefaultValues = false;   // AP-009G v007 (Tide Jewel; no swing clip — the pull is a VFX beat)
            // Empty / Aim_x / SwHold_x ↔ the weapon and special-weapon selection (no exit time)
            var rests = new List<AnimatorState> { overEmpty }; foreach (AnimatorState a in aims) if (a != null) rests.Add(a); rests.AddRange(holds);
            foreach (AnimatorState from in rests)
            {
                for (int w = 0; w < aims.Length; w++)
                {
                    if (aims[w] == null || from == aims[w]) continue;
                    AnimatorStateTransition t = from.AddTransition(aims[w]);
                    t.hasExitTime = false; t.hasFixedDuration = true; t.duration = from == overEmpty ? 0f : 0.15f;
                    t.AddCondition(AnimatorConditionMode.Equals, w, C.Weapon);
                    t.AddCondition(AnimatorConditionMode.Equals, 0f, C.SwHold);
                }

                for (int h = 0; h < holds.Length; h++)
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

            if (v3)
            {
                // WP-071 B4b: the v3 generic one-shots (Weapon == 4); the per-weapon AOC swaps fire / reload_tac / reload_empty
                AnimatorState tac = OneShotUpper(osm, aims, holds, C.ReloadTacState, clips[C.PlayerWeaponClipV3(v3Base, "reload_tac")], 0.98f, 0.1f);
                tac.speedParameterActive = true; tac.speedParameter = C.ReloadRate;
                FromAny(osm, tac, 0.05f, (AnimatorConditionMode.If, 0f, C.Reload), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.IfNot, 0f, C.ReloadEmpty), (AnimatorConditionMode.If, 0f, C.Alive));
                AnimatorState empty = OneShotUpper(osm, aims, holds, C.ReloadEmptyState, clips[C.PlayerWeaponClipV3(v3Base, "reload_empty")], 0.98f, 0.1f);
                empty.speedParameterActive = true; empty.speedParameter = C.ReloadRate;
                FromAny(osm, empty, 0.05f, (AnimatorConditionMode.If, 0f, C.Reload), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.ReloadEmpty), (AnimatorConditionMode.If, 0f, C.Alive));
                AnimatorState vOut = OneShotUpper(osm, aims, holds, C.SwitchOutV3State, clips[C.PlayerWeaponClipV3(v3Base, "switch_out")], 0.98f, 0.05f);
                FromAny(osm, vOut, 0.03f, (AnimatorConditionMode.If, 0f, C.SwitchOut), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
                AnimatorState vIn = OneShotUpper(osm, aims, holds, C.SwitchInV3State, clips[C.PlayerWeaponClipV3(v3Base, "switch_in")], 0.98f, 0.1f);
                FromAny(osm, vIn, 0.03f, (AnimatorConditionMode.If, 0f, C.SwitchIn), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
                // WP-071 B4c: the bolt cycle after a shot — the state carries the sniper's own clip (only a set with hasCycle triggers it)
                foreach (string cs in C.WeaponAnimSetsV3)
                {
                    if (!clips.TryGetValue(C.PlayerWeaponCycleClipV3(cs), out AnimationClip cycleClip)) continue;
                    AnimatorState cycle = OneShotUpper(osm, aims, holds, C.CycleV3State, cycleClip, 0.98f, 0.08f);
                    bool adsVariant = clips.TryGetValue(C.PlayerWeaponCycleAdsClipV3(cs), out AnimationClip cycleAdsClip);
                    if (!adsVariant)
                    {
                        FromAny(osm, cycle, 0.05f, (AnimatorConditionMode.If, 0f, C.Cycle), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
                        break;
                    }

                    // WP-071 r2: from the ADS pose the sniper cycles its ADS bolt (AdsWeight ≥ 0.5)
                    FromAny(osm, cycle, 0.05f, (AnimatorConditionMode.If, 0f, C.Cycle), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive), (AnimatorConditionMode.Less, 0.5f, C.AdsWeight));
                    AnimatorState cycleAds = OneShotUpper(osm, aims, holds, C.CycleAdsV3State, cycleAdsClip, 0.98f, 0.08f);
                    FromAny(osm, cycleAds, 0.05f, (AnimatorConditionMode.If, 0f, C.Cycle), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive), (AnimatorConditionMode.Greater, 0.5f, C.AdsWeight));
                    break;
                }

                // lower / raise: the v3 set has none yet → the rifle's (pose 0) clips
                AnimatorState vLower = OneShotUpper(osm, aims, holds, C.WeaponLowerState + "_" + C.WeaponPoseV3, clips[C.PlayerWeaponLowerClip(0)], 0.98f, 0.1f);
                FromAny(osm, vLower, 0.05f, (AnimatorConditionMode.If, 0f, C.Lower), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
                AnimatorState vRaise = OneShotUpper(osm, aims, holds, C.WeaponRaiseState + "_" + C.WeaponPoseV3, clips[C.PlayerWeaponRaiseClip(0)], 0.98f, 0.1f);
                FromAny(osm, vRaise, 0.05f, (AnimatorConditionMode.If, 0f, C.Raise), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
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

            if (v3)
            {
                AnimatorState fireV3 = OneShot(add.stateMachine, C.FireV3State, clips[C.PlayerWeaponClipV3(v3Base, "fire")], addEmpty, 0.9f, 0.03f);
                FromAny(add.stateMachine, fireV3, 0.0f, (AnimatorConditionMode.If, 0f, C.Fire), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
                // WP-071 r2: a sustained string (FireHeld) loops the class's fire_loop instead of the per-shot kick (the LMGs)
                foreach (string fs in C.WeaponAnimSetsV3)
                {
                    if (!clips.TryGetValue(C.PlayerWeaponFireLoopClipV3(fs), out AnimationClip loopClip)) continue;
                    AnimatorState fireLoop = add.stateMachine.AddState(C.FireLoopV3State);
                    fireLoop.motion = loopClip;
                    fireLoop.writeDefaultValues = false;
                    FromAny(add.stateMachine, fireLoop, 0.05f, (AnimatorConditionMode.If, 0f, C.FireHeld), (AnimatorConditionMode.Equals, C.WeaponPoseV3, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
                    AnimatorStateTransition loopOut = fireLoop.AddTransition(addEmpty);
                    loopOut.hasExitTime = false; loopOut.hasFixedDuration = true; loopOut.duration = 0.08f;
                    loopOut.AddCondition(AnimatorConditionMode.IfNot, 0f, C.FireHeld);
                    AnimatorStateTransition loopDead = fireLoop.AddTransition(addEmpty);   // death ends it too (the entry only checks Alive)
                    loopDead.hasExitTime = false; loopDead.hasFixedDuration = true; loopDead.duration = 0.08f;
                    loopDead.AddCondition(AnimatorConditionMode.IfNot, 0f, C.Alive);
                    break;
                }
            }

            AnimatorState fire = OneShot(add.stateMachine, C.FireState, clips[C.PlayerFireRecoil], addEmpty, 0.9f, 0.05f);
            FromAny(add.stateMachine, fire, 0.0f, (AnimatorConditionMode.If, 0f, C.Fire), (AnimatorConditionMode.Equals, 3f, C.Weapon), (AnimatorConditionMode.If, 0f, C.Alive));
            // WP-032E: Special Weapon swings — generated additive clips over the SwHold pose (bell: two-arm sweep with a chest twist; loom: two-hand cast forward)
            AnimationClip[] swFire = BuildSwFireClips();
            for (int h = 0; h < swFire.Length; h++)
            {
                AnimatorState sw = OneShot(add.stateMachine, C.SwFireState + "_" + (h + 1), swFire[h], addEmpty, 0.95f, 0.05f);
                FromAny(add.stateMachine, sw, 0.0f, (AnimatorConditionMode.If, 0f, C.SwFire), (AnimatorConditionMode.Equals, h + 1, C.SwHold), (AnimatorConditionMode.If, 0f, C.Alive));
            }

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
                (C.AttackMirror, AnimatorControllerParameterType.Bool), (C.Electro, AnimatorControllerParameterType.Bool),
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
            // WP-071 MUST-I1: one child per ZombieGaitClips entry at its index (AP-009 0–3, AP-022 tier gaits 4–8 where imported)
            // WP-096 AP-033 p1: + 9–13; index 11 is walk_limp_A again with the child's mirror on (limp R, no second file)
            var gaitChildren = new List<ChildMotion>();
            for (int g = 0; g < C.ZombieGaitClips.Length; g++)
            {
                if (!clips.ContainsKey(C.ZombieGaitClips[g])) continue;
                ChildMotion child = Child1D(clips[C.ZombieGaitClips[g]], g);
                child.mirror = C.IsGaitMirrored(g);
                gaitChildren.Add(child);
            }

            gait.children = gaitChildren.ToArray();
            loco.speedParameterActive = true;
            loco.speedParameter = C.GaitRate;
            loco.writeDefaultValues = true;
            sm.defaultState = loco;

            c.AddParameter(ZombieTransitionData.RateParameter, AnimatorControllerParameterType.Float);
            SetDefault(c, ZombieTransitionData.RateParameter, 1f);
            foreach (string name in ZombieTransitionData.Clips)
            {
                if (!clips.TryGetValue(name, out AnimationClip transitionClip)) continue;
                AnimatorState state = sm.AddState(name);
                state.motion = transitionClip;
                state.writeDefaultValues = true;
                state.speedParameterActive = true;
                state.speedParameter = ZombieTransitionData.RateParameter;
                if (ZombieTransitionData.NominalYaw(name) > 0f)
                {
                    AnimatorState mirrored = sm.AddState(name + "_R");
                    mirrored.motion = transitionClip;
                    mirrored.mirror = true;
                    mirrored.writeDefaultValues = true;
                    mirrored.speedParameterActive = true;
                    mirrored.speedParameter = ZombieTransitionData.RateParameter;
                }
            }

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
            // WP-071: AP-022 slice 1 attacks where the set carries them (+ Attack_BiteGrab 4, Attack_SwipeL 5), else the AP-009 clips
            string[] attackStates = { C.AttackSwipeState, C.AttackLungeState, C.AttackSweepState, C.AttackOverheadState, C.AttackBiteGrabState, C.AttackSwipeLState };
            for (int i = 0; i < attackStates.Length; i++)
            {
                string clipName = clips.ContainsKey(C.ZombieAttackClipsV3[i]) ? C.ZombieAttackClipsV3[i] : i < C.ZombieAttackClips.Length ? C.ZombieAttackClips[i] : null;
                if (clipName == null) continue;
                bool hold = i == 3 && OverheadHoldSeconds > 0f;
                AnimatorState a = OneShot(sm, attackStates[i], clips[clipName], loco, hold ? 1f : 0.97f, hold ? OverheadHoldSeconds : 0.12f);
                a.speedParameterActive = true;
                a.speedParameter = C.AttackRate;
                a.mirrorParameterActive = true;   // WP-071 B5: swipe_L / any left-handed read = the clip mirrored
                a.mirrorParameter = C.AttackMirror;
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
                // WP-071: the AP-022 death onsets where present (side_R = spin_side mirrored)
                bool v3 = clips.ContainsKey(C.ZombieDeathClipsV3[i]);
                AnimatorState d = OneShot(sm, C.DeathState + "_" + i, clips[v3 ? C.ZombieDeathClipsV3[i] : C.ZombieDeathClips[i]], null);
                if (v3 && i == 4) d.mirror = true;
                FromAny(sm, d, 0.05f, (AnimatorConditionMode.If, 0f, C.Die), (AnimatorConditionMode.Equals, (float)i, C.DeathStyle));
            }

            // WP-071 AP-022 slice 2a: Death_8 arc_electro / Death_9 gel_slip (the visual picks them from the body state) and the
            // Raiju seize loop, held while Electro and left on its release (the death leaves it through Any State)
            for (int k = 0; k < C.ZombieSpecialDeathClipsV3.Length; k++)
            {
                if (!clips.ContainsKey(C.ZombieSpecialDeathClipsV3[k])) continue;
                int style = C.DeathStyleArcElectro + k;
                AnimatorState d = OneShot(sm, C.DeathState + "_" + style, clips[C.ZombieSpecialDeathClipsV3[k]], null);
                FromAny(sm, d, 0.05f, (AnimatorConditionMode.If, 0f, C.Die), (AnimatorConditionMode.Equals, (float)style, C.DeathStyle));
            }

            if (clips.ContainsKey(C.ZombieHitElectroLoopV3))
            {
                AnimatorState electro = sm.AddState(C.ElectroState);
                electro.motion = clips[C.ZombieHitElectroLoopV3];
                electro.writeDefaultValues = false;
                FromAny(sm, electro, 0.08f, (AnimatorConditionMode.If, 0f, C.Electro), (AnimatorConditionMode.If, 0f, C.Alive));
                AnimatorStateTransition off = electro.AddTransition(loco);
                off.hasExitTime = false; off.hasFixedDuration = true; off.duration = 0.2f;
                off.AddCondition(AnimatorConditionMode.IfNot, 0f, C.Electro);
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

        /// <summary>
        /// WP-032E: two 0.5 s additive Humanoid clips (muscle curves, reference pose = frame 0) for the Special Weapon use beat
        /// over the SwHold pose — bell = both arms sweep out and up with a chest twist (the ring), loom = both hands thrust
        /// forward and down (the cast). Bridge until AP-009F ships sw_fire clips. Written to the player's Generated folder.
        /// </summary>
        public static AnimationClip[] BuildSwFireClips()
        {
            string folder = ArtCharacterContract.PlayerFolder + "/Generated";
            EnsureFolder(folder);
            var clips = new AnimationClip[C.SwFireClips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                string path = folder + "/" + C.SwFireClips[i] + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    clip = new AnimationClip { name = C.SwFireClips[i] };
                    AssetDatabase.CreateAsset(clip, path);
                }

                clip.ClearCurves();
                clip.frameRate = 30f;
                if (i == 0)
                {
                    Pulse(clip, "Left Arm Down-Up", 0.55f); Pulse(clip, "Right Arm Down-Up", 0.55f);
                    Pulse(clip, "Left Arm Front-Back", -0.35f); Pulse(clip, "Right Arm Front-Back", -0.35f);
                    Pulse(clip, "Chest Twist Left-Right", 0.35f); Pulse(clip, "Spine Front-Back", -0.15f);
                }
                else
                {
                    Pulse(clip, "Left Arm Front-Back", 0.5f); Pulse(clip, "Right Arm Front-Back", 0.5f);
                    Pulse(clip, "Left Forearm Stretch", 0.4f); Pulse(clip, "Right Forearm Stretch", 0.4f);
                    Pulse(clip, "Spine Front-Back", 0.2f); Pulse(clip, "Head Nod Down-Up", -0.2f);
                }

                AnimationUtility.SetAdditiveReferencePose(clip, clip, 0f);
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = false;
                settings.keepOriginalPositionY = true; settings.keepOriginalOrientation = true; settings.keepOriginalPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
                clips[i] = clip;
            }

            return clips;
        }

        /// <summary>0.5 s pulse: 0 → peak at 0.12 s → 0 at 0.5 s (a use beat, not a wind-up).</summary>
        private static void Pulse(AnimationClip clip, string muscle, float peak)
        {
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.12f, peak), new Keyframe(0.3f, peak * 0.6f), new Keyframe(0.5f, 0f));
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            clip.SetCurve("", typeof(Animator), muscle, curve);
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

        /// <summary>
        /// WP-071 MUST-3: the static stand at the player tree's centre — every curve of the idle clip held at its frame 0 for
        /// <paramref name="seconds"/> (the walk cycle's length). Null when the idle exposes no curves (the idle stays the centre).
        /// </summary>
        public static AnimationClip BuildStandStillClip(AnimationClip idle, float seconds)
        {
            if (idle == null) return null;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(idle);
            if (bindings == null || bindings.Length == 0)
            {
                Debug.LogWarning(LogPrefix + "stand-still: " + idle.name + " exposes no curves — the idle stays the locomotion centre");
                return null;
            }

            string folder = ArtCharacterContract.PlayerFolder + "/Generated";
            EnsureFolder(folder);
            string path = folder + "/" + C.PlayerStandStill + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = C.PlayerStandStill };
                AssetDatabase.CreateAsset(clip, path);
            }

            clip.ClearCurves();
            clip.frameRate = 30f;
            float length = Mathf.Max(1f / 30f, seconds);
            foreach (EditorCurveBinding b in bindings)
            {
                AnimationCurve src = AnimationUtility.GetEditorCurve(idle, b);
                if (src == null) continue;
                float v = src.Evaluate(0f);
                AnimationUtility.SetEditorCurve(clip, b, new AnimationCurve(new Keyframe(0f, v), new Keyframe(length, v)));
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            Debug.Log(LogPrefix + "stand-still: " + bindings.Length + " curves of " + idle.name + " f0 held " + length.ToString("F3") + " s → " + path);
            return clip;
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

        // An AOC keeps its serialized pairs across rebuilds, including clips no longer in the controller (a deleted class FP clip
        // stayed as a GUID without an asset, C-00 25.09); the pairs are rewritten from the current controller every build.
        private static void ClearStalePairs(AnimatorOverrideController aoc)
        {
            var so = new SerializedObject(aoc);
            SerializedProperty list = so.FindProperty("m_Clips");
            if (list == null || list.arraySize == 0) return;
            list.ClearArray();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// WP-071 B4b (K8 / A6.2): one AnimatorOverrideController per v3 animation set (WeaponDefinition.AnimSet), overriding only
        /// the weapon-own slots (fire / reload_tac / reload_empty) over the base controller, whose v3 states carry the first set's
        /// clips; the class slots stay the base clips. Written next to the player controller (Player/Generated).
        /// </summary>
        /// <summary>
        /// WP-071 B4b-3 (A6.4 #49): the FP viewmodel controller — fp_base (the arms pose: Aim_V3 rest, ReloadTac / ReloadEmpty at
        /// ReloadRate, SwitchIn_V3 / SwitchOut_V3), fp_additive (Fire_V3 kick; S-07 owns the camera/arm-root spring), fp_sway
        /// (walk ↔ run sway by FpRun, layer weight = speed share from code). Base clips = the first v3 set's; one AOC per set.
        /// Nothing is built while the set has no FP clips.
        /// </summary>
        private static void WriteFirstPerson(string setPath, Dictionary<string, AnimationClip> clips, Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(setPath);
            if (set == null) return;
            string baseSet = C.WeaponAnimSetsV3[0];
            if (FpClipFor(clips, baseSet, "idle_aim") == null) { set.ConfigureFirstPerson(null, null); EditorUtility.SetDirty(set); return; }
            EnsureFolder(ArtCharacterContract.PlayerFolder + "/Generated");
            AnimatorController c = Fresh(PlayerFpController,
                (C.Fire, AnimatorControllerParameterType.Trigger), (C.Reload, AnimatorControllerParameterType.Trigger), (C.ReloadRate, AnimatorControllerParameterType.Float),
                (C.ReloadEmpty, AnimatorControllerParameterType.Bool), (C.SwitchIn, AnimatorControllerParameterType.Trigger), (C.SwitchOut, AnimatorControllerParameterType.Trigger),
                (C.FpRun, AnimatorControllerParameterType.Float), (C.Alive, AnimatorControllerParameterType.Bool));
            SetDefault(c, C.ReloadRate, 1f);
            SetDefault(c, C.Alive, true);
            ApplyLayer(c, 0, l => l.name = C.FpBaseLayer);
            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState aim = sm.AddState(C.AimV3State);
            aim.motion = FpClipFor(clips, baseSet, "idle_aim");
            aim.writeDefaultValues = false;
            sm.defaultState = aim;
            AnimatorState tac = OneShot(sm, C.ReloadTacState, FpClipFor(clips, baseSet, "reload_tac"), aim, 0.98f, 0.1f);
            tac.speedParameterActive = true; tac.speedParameter = C.ReloadRate;
            FromAny(sm, tac, 0.05f, (AnimatorConditionMode.If, 0f, C.Reload), (AnimatorConditionMode.IfNot, 0f, C.ReloadEmpty));
            AnimatorState emp = OneShot(sm, C.ReloadEmptyState, FpClipFor(clips, baseSet, "reload_empty"), aim, 0.98f, 0.1f);
            emp.speedParameterActive = true; emp.speedParameter = C.ReloadRate;
            FromAny(sm, emp, 0.05f, (AnimatorConditionMode.If, 0f, C.Reload), (AnimatorConditionMode.If, 0f, C.ReloadEmpty));
            AnimatorState sOut = OneShot(sm, C.SwitchOutV3State, FpClipFor(clips, baseSet, "switch_out"), aim, 0.98f, 0.05f);
            FromAny(sm, sOut, 0.03f, (AnimatorConditionMode.If, 0f, C.SwitchOut));
            AnimatorState sIn = OneShot(sm, C.SwitchInV3State, FpClipFor(clips, baseSet, "switch_in"), aim, 0.98f, 0.1f);
            FromAny(sm, sIn, 0.03f, (AnimatorConditionMode.If, 0f, C.SwitchIn));
            AnimatorControllerLayer add = AddLayer(c, C.FpAdditiveLayer, null, AnimatorLayerBlendingMode.Additive, false);
            AnimatorState addEmpty = add.stateMachine.AddState(C.EmptyState);
            add.stateMachine.defaultState = addEmpty;
            AnimatorState fire = OneShot(add.stateMachine, C.FireV3State, FpClipFor(clips, baseSet, "fire"), addEmpty, 0.9f, 0.03f);
            FromAny(add.stateMachine, fire, 0f, (AnimatorConditionMode.If, 0f, C.Fire));

            AnimatorControllerLayer swayL = AddLayer(c, C.FpSwayLayer, null, AnimatorLayerBlendingMode.Additive, false);
            if (clips.TryGetValue(C.FpWalkSway, out AnimationClip walk) && clips.TryGetValue(C.FpRunSway, out AnimationClip run))
            {
                AnimatorState sway = c.CreateBlendTreeInController(C.FpSwayState, out BlendTree tree, 2);
                tree.blendType = BlendTreeType.Simple1D;
                tree.blendParameter = C.FpRun;
                tree.useAutomaticThresholds = false;
                tree.children = new[] { Child1D(walk, 0f), Child1D(run, 1f) };
                c.layers[2].stateMachine.defaultState = sway;
            }

            ApplyLayer(c, 1, l => { l.blendingMode = AnimatorLayerBlendingMode.Additive; l.defaultWeight = 1f; });
            ApplyLayer(c, 2, l => { l.blendingMode = AnimatorLayerBlendingMode.Additive; l.defaultWeight = 0f; });   // weight from speed (code)
            EditorUtility.SetDirty(c);

            var sets = new List<CharacterAnimationSet.WeaponAnimSet>();
            string folder = ArtCharacterContract.PlayerFolder + "/Generated";
            foreach (string animSet in C.WeaponAnimSetsV3)
            {
                if (FpClipFor(clips, animSet, "fire") == null) continue;
                string path = folder + "/DD_Player_FP_" + animSet + ".overrideController";
                var aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
                if (aoc == null) { aoc = new AnimatorOverrideController(c); AssetDatabase.CreateAsset(aoc, path); }
                aoc.runtimeAnimatorController = c;
                ClearStalePairs(aoc);
                // FP: every slot may have a weapon-specific clip (AP022_fp_<set>_<slot>, e.g. ar_2's own rest on its fpView) over the class one
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                foreach (string slot in ClassAndOwnSlots)
                {
                    AnimationClip from = FpClipFor(clips, baseSet, slot);
                    if (from == null) continue;
                    AnimationClip to = FpClipFor(clips, animSet, slot);
                    pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(from, to == null || to == from ? null : to));
                }

                aoc.ApplyOverrides(pairs);
                EditorUtility.SetDirty(aoc);
                manifest.TryGetValue(C.PlayerFpClipV3(animSet, "idle_aim"), out ArtAnimationClipContract.ClipSpec rest);
                if (rest == null) manifest.TryGetValue("AP022_fp_" + animSet + "_idle_aim", out rest);
                sets.Add(new CharacterAnimationSet.WeaponAnimSet
                {
                    animSet = animSet, overrides = aoc, hasLeftHand = rest != null && rest.hasLeftHandInWeapon, leftHandInWeapon = rest != null ? rest.leftHandInWeapon : Vector3.zero,
                    hasSocketRot = rest != null && rest.hasSocketRot, socketRotInWeapon = rest != null && rest.hasSocketRot ? rest.socketRotInWeapon : Quaternion.identity,
                    fpCameraOffset = FpCameraOffsets.TryGetValue(animSet, out Vector3 camOffset) ? camOffset : Vector3.zero
                });
            }

            // AP-022 FP arms v2 (P-06 / C-00 25.09): a v3 set without its own FP clips (smg_2) borrows its class sibling's clips and
            // fp_camera offset, and its left-hand point moves with the weapon's own grip_l socket (AP-021 contract): donor point +
            // (grip_l(set) − grip_l(donor)) in the weapon rig frame — the same hand on a handguard that sits elsewhere.
            foreach (string animSet in C.WeaponAnimSetsV3)
            {
                if (sets.Exists(x => x.animSet == animSet)) continue;
                CharacterAnimationSet.WeaponAnimSet donor = sets.Find(x => C.WeaponClassOf(x.animSet) == C.WeaponClassOf(animSet));
                if (donor == null) continue;
                Vector3? own = WeaponGripLeft(animSet), from = WeaponGripLeft(donor.animSet);
                bool moved = own.HasValue && from.HasValue;
                sets.Add(new CharacterAnimationSet.WeaponAnimSet
                {
                    animSet = animSet, overrides = donor.overrides, hasLeftHand = donor.hasLeftHand && moved,
                    leftHandInWeapon = moved ? donor.leftHandInWeapon + (own.Value - from.Value) : donor.leftHandInWeapon,
                    hasSocketRot = donor.hasSocketRot, socketRotInWeapon = donor.socketRotInWeapon, fpCameraOffset = donor.fpCameraOffset
                });
                Debug.Log(LogPrefix + "FP set " + animSet + " borrows " + donor.animSet + (moved ? " (left hand moved by grip_l " + (own.Value - from.Value).ToString("F3") + ")" : " (no grip_l — left hand IK off)"));
            }

            set.ConfigureFirstPerson(c, sets);
            EditorUtility.SetDirty(set);
            Debug.Log(LogPrefix + "FP viewmodel controller: " + c.layers.Length + " layers, sets " + sets.Count);
        }

        /// <summary>
        /// AP-022 slice 5b (P-06 25.09, manifest weapon_layer.fp.anchor): fp_camera's constant per-weapon offset (Blender bone space x right,
        /// y look → rig x, z). Unity drops a Humanoid clip's extra-transform curves (measured: 0 fp_camera bindings even with a model mask),
        /// so the offset is applied by FirstPersonArms.Place from here.
        /// </summary>
        private static readonly Dictionary<string, Vector3> FpCameraOffsets = new Dictionary<string, Vector3>
        {
            { "ar_1", new Vector3(-0.08f, 0f, -0.16f) }, { "ar_2", new Vector3(-0.12f, 0f, -0.30f) }, { "smg_1", new Vector3(-0.04f, 0f, -0.06f) },
            { "smg_2", new Vector3(-0.02f, -0.04f, -0.16f) },   // AP-022 FP v003 (P-06 26.09; x right, y up, z look)
            { "sw_arc", new Vector3(-0.16f, 0f, -0.36f) }, { "sw_gel", new Vector3(-0.16f, -0.04f, -0.36f) }   // AP-022 FP Special (P-06 28.09: x right / y look / z up → here x, y up, z look)
        };

        /// <summary>The AP-021 model's grip_l socket in the weapon rig frame (the model root sits on the rig at identity — WeaponVisualRig.SetModel).</summary>
        private static Vector3? WeaponGripLeft(string key)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prototype/Weapons/Arsenal/Models/AP021_" + key + "/AP021_" + key + ".fbx");
            if (model == null) return null;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == "grip_l") return model.transform.InverseTransformPoint(t.position);
            return null;
        }

        private static AnimationClip FpClipFor(Dictionary<string, AnimationClip> clips, string animSet, string slot)
        {
            if (clips.TryGetValue("AP022_fp_" + animSet + "_" + slot, out AnimationClip own)) return own;
            return clips.TryGetValue(C.PlayerFpClipV3(animSet, slot), out AnimationClip cls) ? cls : null;
        }

        private static readonly string[] ClassAndOwnSlots = { "idle_aim", "switch_in", "switch_out", "fire", "reload_tac", "reload_empty" };
        /// <summary>WP-071 r2: the TP AOC slots — + ads_idle (the Aim_V3 ADS blend); the FP controller has no ADS state.</summary>
        private static readonly string[] TpClassAndOwnSlots = { "idle_aim", "switch_in", "switch_out", "fire", "reload_tac", "reload_empty", "ads_idle" };

        private static void WriteWeaponSets(string setPath, AnimatorController controller, Dictionary<string, AnimationClip> clips, Dictionary<string, ArtAnimationClipContract.ClipSpec> manifest)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(setPath);
            if (set == null) return;
            string folder = ArtCharacterContract.PlayerFolder + "/Generated";
            EnsureFolder(folder);
            var sets = new List<CharacterAnimationSet.WeaponAnimSet>();
            string baseSet = C.WeaponAnimSetsV3[0];
            foreach (string animSet in C.WeaponAnimSetsV3)
            {
                if (!clips.ContainsKey(C.PlayerWeaponClipV3(animSet, "fire"))) continue;
                string path = folder + "/DD_Player_" + animSet + ".overrideController";
                var aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
                if (aoc == null) { aoc = new AnimatorOverrideController(controller); AssetDatabase.CreateAsset(aoc, path); }
                aoc.runtimeAnimatorController = controller;
                ClearStalePairs(aoc);
                // weapon slots always; the class slots too when the set is another class (smg_1 over the ar base)
                bool otherClass = C.WeaponClassOf(animSet) != C.WeaponClassOf(baseSet);
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                foreach (string slot in otherClass ? TpClassAndOwnSlots : C.WeaponOwnSlots)
                {
                    if (!clips.TryGetValue(C.PlayerWeaponClipV3(baseSet, slot), out AnimationClip from)) continue;
                    AnimationClip to = V3SlotClip(clips, animSet, slot);
                    pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(from, animSet == baseSet ? null : to));
                }

                aoc.ApplyOverrides(pairs);
                EditorUtility.SetDirty(aoc);
                if (!manifest.TryGetValue("AP022_tp_" + animSet + "_idle_aim", out ArtAnimationClipContract.ClipSpec rest))
                    manifest.TryGetValue(C.PlayerWeaponClipV3(animSet, "idle_aim"), out rest);
                sets.Add(new CharacterAnimationSet.WeaponAnimSet
                {
                    animSet = animSet, overrides = aoc,
                    hasLeftHand = rest != null && rest.hasLeftHandInWeapon, leftHandInWeapon = rest != null ? rest.leftHandInWeapon : Vector3.zero,
                    hasCycle = clips.ContainsKey(C.PlayerWeaponCycleClipV3(animSet)),
                    hasFireLoop = clips.ContainsKey(C.PlayerWeaponFireLoopClipV3(animSet))
                });
            }

            set.ConfigureWeaponSets(sets);
            EditorUtility.SetDirty(set);
            Debug.Log(LogPrefix + "weapon animation sets: " + sets.Count + " (" + string.Join(", ", C.WeaponAnimSetsV3) + ")");
        }

        /// <summary>
        /// WP-071 B4c: a set's clip for a slot — its own, else a same-class sibling's own weapon slot (lmg_2 has no reloads yet → lmg_1's),
        /// else, for reload_tac, its own reload_empty (the launcher has one reload).
        /// </summary>
        private static AnimationClip V3SlotClip(Dictionary<string, AnimationClip> clips, string animSet, string slot)
        {
            if (clips.TryGetValue("AP022_tp_" + animSet + "_" + slot, out AnimationClip own)) return own;
            if (clips.TryGetValue(C.PlayerWeaponClipV3(animSet, slot), out AnimationClip cls)) return cls;
            if (System.Array.IndexOf(C.WeaponOwnSlots, slot) < 0) return null;
            foreach (string sibling in C.WeaponAnimSetsV3)
                if (sibling != animSet && C.WeaponClassOf(sibling) == C.WeaponClassOf(animSet) && clips.TryGetValue(C.PlayerWeaponClipV3(sibling, slot), out AnimationClip s)) return s;
            return slot == "reload_tac" && clips.TryGetValue(C.PlayerWeaponClipV3(animSet, "reload_empty"), out AnimationClip empty) ? empty : null;
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

            // WP-096 P3: the per-clip rate band (minRate / maxRate) is authored on the set, not in the manifest — a rebuild keeps it.
            var bands = new Dictionary<string, Vector2>();
            if (!created && set.Clips != null)
            {
                foreach (CharacterAnimationSet.Entry old in set.Clips)
                {
                    if (old != null && old.name != null && (old.minRate > 0f || old.maxRate > 0f)) bands[old.name] = new Vector2(old.minRate, old.maxRate);
                }
            }

            var table = new List<CharacterAnimationSet.Entry>();
            var names = new List<string>(manifest.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
            {
                ArtAnimationClipContract.ClipSpec s = manifest[name];
                if (s.target != target) continue;
                bands.TryGetValue(s.name, out Vector2 band);
                table.Add(new CharacterAnimationSet.Entry
                {
                    name = s.name, clip = clips[s.name], loop = s.loop, seconds = s.seconds, speedMps = s.speedMps,
                    unitySpeedMps = unitySpeeds != null && unitySpeeds.TryGetValue(s.name, out float u) ? u : 0f,
                    layer = s.layer, additive = s.additive, frames = s.frames, events = s.EventsText(), stanceFrames = s.stanceFrames,
                    minRate = band.x, maxRate = band.y, transition = s.transition
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
