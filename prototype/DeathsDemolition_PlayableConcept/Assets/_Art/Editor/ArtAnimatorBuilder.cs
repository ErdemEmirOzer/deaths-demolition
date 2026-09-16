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
    /// (idle / walk_fwd / walk_back / strafe_L / strafe_R on MoveX·MoveY; child time scales =
    /// reference speed / clip speed_mps; state speed = LocoRate), Death_0/1, Pickup;
    /// <c>override_upper</c> = hit_light; <c>additive</c> = fire_recoil; <c>aim_layer</c> =
    /// empty state with the IK pass (PlayerAimLayer writes Spine/Chest/UpperChest).
    /// Zombie (<c>DD_Zombie.controller</c>): base = Locomotion (Speed01: idle sway/twitch ↔ gait
    /// shamble_A/B/C/run by Gait; state speed = GaitRate), Attack_Swipe/Lunge/Sweep (speed =
    /// AttackRate, so hit_window_start lands on the tier telegraph), Stagger, Death_0..3, Rise;
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
        public const string ZombieController = ArtCharacterContract.ZombieFolder + "/DD_Zombie.controller";
        public const string PlayerSource = "AP-009 v001 (30 clips, AP009-clips-manifest.json) — WP-018 generated controller";
        public const string ZombieSource = "AP-009 v001 (30 clips, AP009-clips-manifest.json) — WP-018 generated controller";

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
            AnimatorController player = BuildPlayer(clips, manifest, upper, aim, playerReferenceSpeed, out report.playerStates, out report.playerLayers);
            AnimatorController zombie = BuildZombie(clips, manifest, upper, out report.zombieStates, out report.zombieLayers);

            WriteSet(ArtCharacterPrefabBuilder.PlayerAnimationSet, player, manifest, clips, "player", PlayerSource, playerReferenceSpeed,
                clips[C.PlayerIdle], clips[C.PlayerWalkFwd], manifest[C.PlayerWalkFwd].speedMps);
            WriteSet(ArtCharacterPrefabBuilder.ZombieAnimationSet, zombie, manifest, clips, "zombie", ZombieSource, manifest[C.ZombieShambleA].speedMps,
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

        private static void ApplyLayer(AnimatorController controller, int index, Action<AnimatorControllerLayer> edit)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            edit(layers[index]);
            controller.layers = layers;
        }

        // ------------------------------------------------------------------ player

        private static AnimatorController BuildPlayer(Dictionary<string, AnimationClip> clips, Dictionary<string, ArtAnimationClipContract.ClipSpec> m,
            AvatarMask upper, AvatarMask aim, float reference, out int states, out int layers)
        {
            AnimatorController c = Fresh(PlayerController,
                (C.MoveX, AnimatorControllerParameterType.Float), (C.MoveY, AnimatorControllerParameterType.Float), (C.LocoRate, AnimatorControllerParameterType.Float),
                (C.Fire, AnimatorControllerParameterType.Trigger), (C.HitLight, AnimatorControllerParameterType.Trigger),
                (C.Die, AnimatorControllerParameterType.Trigger), (C.DeathStyle, AnimatorControllerParameterType.Int),
                (C.Pickup, AnimatorControllerParameterType.Trigger), (C.Alive, AnimatorControllerParameterType.Bool));
            SetDefault(c, C.LocoRate, 1f);
            SetDefault(c, C.Alive, true);

            // ---- base: 2-D locomotion + deaths + pickup
            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState loco = c.CreateBlendTreeInController(C.LocomotionState, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = C.MoveX;
            tree.blendParameterY = C.MoveY;
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                Child2D(clips[C.PlayerIdle], 0f, 0f, 1f),
                Child2D(clips[C.PlayerWalkFwd], 0f, 1f, PlayerSkinnedVisual.ChildTimeScale(reference, m[C.PlayerWalkFwd].speedMps)),
                Child2D(clips[C.PlayerWalkBack], 0f, -1f, PlayerSkinnedVisual.ChildTimeScale(reference, m[C.PlayerWalkBack].speedMps)),
                Child2D(clips[C.PlayerStrafeLeft], -1f, 0f, PlayerSkinnedVisual.ChildTimeScale(reference, m[C.PlayerStrafeLeft].speedMps)),
                Child2D(clips[C.PlayerStrafeRight], 1f, 0f, PlayerSkinnedVisual.ChildTimeScale(reference, m[C.PlayerStrafeRight].speedMps))
            };
            loco.speedParameterActive = true;
            loco.speedParameter = C.LocoRate;
            loco.writeDefaultValues = true;
            sm.defaultState = loco;

            AnimatorState death0 = OneShot(sm, C.DeathState + "_0", clips[C.PlayerDeathFwd], null);
            AnimatorState death1 = OneShot(sm, C.DeathState + "_1", clips[C.PlayerDeathBack], null);
            FromAny(sm, death0, 0.05f, (AnimatorConditionMode.If, 0f, C.Die), (AnimatorConditionMode.Equals, 0f, C.DeathStyle));
            FromAny(sm, death1, 0.05f, (AnimatorConditionMode.If, 0f, C.Die), (AnimatorConditionMode.Equals, 1f, C.DeathStyle));
            AnimatorState pickup = OneShot(sm, C.PickupState, clips[C.PlayerPickup], loco, 0.9f, 0.12f);
            FromAny(sm, pickup, 0.08f, (AnimatorConditionMode.If, 0f, C.Pickup), (AnimatorConditionMode.If, 0f, C.Alive));

            // ---- override_upper: hit_light
            AnimatorControllerLayer over = AddLayer(c, C.OverrideUpperLayer, upper, AnimatorLayerBlendingMode.Override, false);
            AnimatorState overEmpty = over.stateMachine.AddState(C.EmptyState);
            over.stateMachine.defaultState = overEmpty;
            AnimatorState hit = OneShot(over.stateMachine, C.HitState, clips[C.PlayerHitLight], overEmpty, 0.9f, 0.1f);
            FromAny(over.stateMachine, hit, 0.03f, (AnimatorConditionMode.If, 0f, C.HitLight), (AnimatorConditionMode.If, 0f, C.Alive));

            // ---- additive: fire_recoil
            AnimatorControllerLayer add = AddLayer(c, C.AdditiveLayer, upper, AnimatorLayerBlendingMode.Additive, false);
            AnimatorState addEmpty = add.stateMachine.AddState(C.EmptyState);
            add.stateMachine.defaultState = addEmpty;
            AnimatorState fire = OneShot(add.stateMachine, C.FireState, clips[C.PlayerFireRecoil], addEmpty, 0.9f, 0.05f);
            FromAny(add.stateMachine, fire, 0.0f, (AnimatorConditionMode.If, 0f, C.Fire), (AnimatorConditionMode.If, 0f, C.Alive));

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
                (C.Attack, AnimatorControllerParameterType.Trigger), (C.AttackStyle, AnimatorControllerParameterType.Int), (C.AttackRate, AnimatorControllerParameterType.Float),
                (C.Stagger, AnimatorControllerParameterType.Trigger),
                (C.HitLeft, AnimatorControllerParameterType.Trigger), (C.HitRight, AnimatorControllerParameterType.Trigger),
                (C.FlinchHead, AnimatorControllerParameterType.Trigger), (C.FlinchTorso, AnimatorControllerParameterType.Trigger), (C.FlinchLeg, AnimatorControllerParameterType.Trigger),
                (C.Die, AnimatorControllerParameterType.Trigger), (C.DeathStyle, AnimatorControllerParameterType.Int),
                (C.Rise, AnimatorControllerParameterType.Trigger), (C.Alive, AnimatorControllerParameterType.Bool));
            SetDefault(c, C.GaitRate, 1f);
            SetDefault(c, C.AttackRate, 1f);
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

            // attacks (speed = AttackRate → hit_window_start lands on TelegraphTime)
            string[] attackStates = { C.AttackSwipeState, C.AttackLungeState, C.AttackSweepState };
            for (int i = 0; i < C.ZombieAttackClips.Length; i++)
            {
                AnimatorState a = OneShot(sm, attackStates[i], clips[C.ZombieAttackClips[i]], loco, 0.97f, 0.12f);
                a.speedParameterActive = true;
                a.speedParameter = C.AttackRate;
                FromAny(sm, a, 0.06f, (AnimatorConditionMode.If, 0f, C.Attack), (AnimatorConditionMode.Equals, (float)i, C.AttackStyle), (AnimatorConditionMode.If, 0f, C.Alive));
            }

            AnimatorState stagger = OneShot(sm, C.StaggerState, clips[C.ZombieHitHeavyStagger], loco, 0.95f, 0.15f);
            FromAny(sm, stagger, 0.05f, (AnimatorConditionMode.If, 0f, C.Stagger), (AnimatorConditionMode.If, 0f, C.Alive));

            for (int i = 0; i < C.ZombieDeathClips.Length; i++)
            {
                AnimatorState d = OneShot(sm, C.DeathState + "_" + i, clips[C.ZombieDeathClips[i]], null);
                FromAny(sm, d, 0.05f, (AnimatorConditionMode.If, 0f, C.Die), (AnimatorConditionMode.Equals, (float)i, C.DeathStyle));
            }

            AnimatorState rise = OneShot(sm, C.RiseState, clips[C.ZombieRise], loco, 0.97f, 0.15f);
            FromAny(sm, rise, 0.0f, (AnimatorConditionMode.If, 0f, C.Rise), (AnimatorConditionMode.If, 0f, C.Alive));

            // ---- override_upper: hit_light L / R
            AnimatorControllerLayer over = AddLayer(c, C.OverrideUpperLayer, upper, AnimatorLayerBlendingMode.Override, false);
            AnimatorState overEmpty = over.stateMachine.AddState(C.EmptyState);
            over.stateMachine.defaultState = overEmpty;
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

            ApplyLayer(c, 1, l => { l.avatarMask = upper; l.blendingMode = AnimatorLayerBlendingMode.Override; l.defaultWeight = 1f; });
            ApplyLayer(c, 2, l => { l.avatarMask = null; l.blendingMode = AnimatorLayerBlendingMode.Additive; l.defaultWeight = 1f; });

            states = CountStates(c);
            layers = c.layers.Length;
            EditorUtility.SetDirty(c);
            return c;
        }

        // ------------------------------------------------------------------ helpers

        private static ChildMotion Child1D(Motion motion, float threshold)
        {
            return new ChildMotion { motion = motion, threshold = threshold, timeScale = 1f, directBlendParameter = "" };
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
            Dictionary<string, AnimationClip> clips, string target, string source, float referenceSpeed, AnimationClip idle, AnimationClip walk, float walkSpeed)
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
                    layer = s.layer, additive = s.additive, frames = s.frames, events = s.EventsText()
                });
            }

            set.Configure(idle, walk, walkSpeed, controller, source, table, ArtAnimationClipContract.ManifestSchema, referenceSpeed);
            EditorUtility.SetDirty(set);
            Debug.Log(LogPrefix + "animation set " + (created ? "created" : "updated") + ": " + setPath + " (" + table.Count + " clips, controller " + controller.name + ")");
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
