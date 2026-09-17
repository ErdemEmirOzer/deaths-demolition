using DD.Prototype.Rendering;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Inputs;
using UnityEngine;
using DD.Prototype.Characters;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// Builds the player from primitives, in code.
    ///
    /// WHY NOT A PREFAB
    /// ----------------
    /// Four tasks are authoring this project at the same time and a binary-ish prefab
    /// is the one artefact that cannot be merged or reviewed. A factory is diffable,
    /// cannot carry a broken serialized reference, and gives the run flow a one-liner:
    ///
    ///     PlayerRig rig = PlayerRigFactory.Create(spawnPoint.position, spawnRotation, skin);
    ///
    /// The hierarchy it produces is deliberately readable, because every pseudo
    /// animation is written onto these transforms by name:
    ///
    ///     Player                      CharacterController, motor, aim, health,
    ///                                 animator, rig, damage feedback, debug hooks
    ///       Visual                    collapses on death
    ///         Hips                    bob
    ///           Torso                 lean, recoil pitch, flinch
    ///             Head
    ///             Sash                accent colour, reads the facing at a glance
    ///             Arm.L               swings with the run
    ///             Arm.R               weapon arm, drops during reload
    ///               WeaponMount       recoil kick lives here
    ///                 Muzzle
    ///           Leg.L                 swings with the run
    ///           Leg.R
    ///       CameraFollow              at the feet, what the camera rig follows
    ///
    /// Only the root has a collider. The limbs are visual, so the swarm collides with
    /// one clean capsule and bullets are never blocked by the player's own arm.
    /// </summary>
    public static class PlayerRigFactory
    {
        public const string RootName = "Player";
        public const string VisualName = "Visual";
        public const string WeaponMountName = "WeaponMount";
        public const string MuzzleName = "Muzzle";
        public const string CameraFollowName = "CameraFollow";

        // PROTOTYPE-ONLY body proportions, invented. Expressed as fractions of the
        // capsule height from PrototypeConfig.Player.CollisionHeight, so changing the
        // capsule in the F1 panel changes the silhouette with it.
        [PrototypeOnly(PendingDecisions.None, "Hip height as a fraction of the capsule height.", true)]
        private const float HipsHeightFraction = 0.50f;

        [PrototypeOnly(PendingDecisions.None, "Torso length as a fraction of the capsule height.", true)]
        private const float TorsoHeightFraction = 0.42f;

        [PrototypeOnly(PendingDecisions.None, "Head radius as a fraction of the capsule height.", true)]
        private const float HeadRadiusFraction = 0.115f;

        [PrototypeOnly(PendingDecisions.None, "Leg length as a fraction of the capsule height.", true)]
        private const float LegLengthFraction = 0.48f;

        [PrototypeOnly(PendingDecisions.None, "Arm length as a fraction of the capsule height.", true)]
        private const float ArmLengthFraction = 0.36f;

        private static readonly Dictionary<int, Material> s_materials = new Dictionary<int, Material>(16);

        /// <summary>Creates a fully wired player and returns its rig component.</summary>
        public static PlayerRig Create(Vector3 position, Quaternion rotation, PlayerSkin skin)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            float height = config != null ? Mathf.Max(0.6f, config.Player.CollisionHeight) : 1.8f;
            float radius = config != null ? Mathf.Max(0.1f, config.Player.CollisionRadius) : 0.45f;

            // Built inactive: components must be wired before the first OnEnable, or
            // ApplyConfig would run against half a rig.
            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetPositionAndRotation(position, rotation);

            CharacterController controller = root.AddComponent<CharacterController>();
            controller.radius = radius;
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);

            PlayerSkins.Palette palette = PlayerSkins.Get(skin);

            // ----------------------------------------------------------- visual tree

            var visual = new GameObject(VisualName);
            visual.transform.SetParent(root.transform, false);

            var hips = new GameObject("Hips");
            hips.transform.SetParent(visual.transform, false);
            hips.transform.localPosition = new Vector3(0f, height * HipsHeightFraction, 0f);

            Transform torso = CreatePart(
                hips.transform, "Torso", PrimitiveType.Capsule,
                Vector3.zero,
                new Vector3(radius * 1.55f, height * TorsoHeightFraction * 0.5f, radius * 1.15f),
                palette.Primary);

            Transform head = CreatePart(
                torso, "Head", PrimitiveType.Sphere,
                new Vector3(0f, height * TorsoHeightFraction * 0.62f, 0f),
                Vector3.one * (height * HeadRadiusFraction * 2f),
                palette.Secondary);

            // The sash is the readability trick: a bright accent slab on the chest
            // makes the facing direction obvious from 15 metres up.
            CreatePart(
                torso, "Sash", PrimitiveType.Cube,
                new Vector3(0f, height * 0.06f, radius * 0.95f),
                new Vector3(radius * 1.5f, height * 0.13f, radius * 0.22f),
                palette.Accent);

            Transform armLeft = CreateLimb(
                torso, "Arm.L",
                new Vector3(-radius * 1.25f, height * TorsoHeightFraction * 0.30f, 0f),
                height * ArmLengthFraction, radius * 0.34f, palette.Secondary);

            Transform armRight = CreateLimb(
                torso, "Arm.R",
                new Vector3(radius * 1.25f, height * TorsoHeightFraction * 0.30f, 0f),
                height * ArmLengthFraction, radius * 0.34f, palette.Secondary);

            // The weapon arm is pushed forward so the silhouette reads as "aiming".
            armRight.localRotation = Quaternion.Euler(-72f, 0f, 0f);

            var weaponMount = new GameObject(WeaponMountName);
            weaponMount.transform.SetParent(armRight, false);
            weaponMount.transform.localPosition = new Vector3(0f, -height * ArmLengthFraction * 0.95f, 0f);
            weaponMount.transform.localRotation = Quaternion.Euler(72f, 0f, 0f);

            var muzzle = new GameObject(MuzzleName);
            muzzle.transform.SetParent(weaponMount.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0f, radius * 1.4f);

            Transform legLeft = CreateLimb(
                hips.transform, "Leg.L",
                new Vector3(-radius * 0.5f, 0f, 0f),
                height * LegLengthFraction, radius * 0.42f, palette.Primary);

            Transform legRight = CreateLimb(
                hips.transform, "Leg.R",
                new Vector3(radius * 0.5f, 0f, 0f),
                height * LegLengthFraction, radius * 0.42f, palette.Primary);

            var cameraFollow = new GameObject(CameraFollowName);
            cameraFollow.transform.SetParent(root.transform, false);
            cameraFollow.transform.localPosition = Vector3.zero;

            // ------------------------------------------------------------ components

            PlayerMotor motor = root.AddComponent<PlayerMotor>();
            PlayerAim aim = root.AddComponent<PlayerAim>();
            PlayerHealth health = root.AddComponent<PlayerHealth>();
            PlayerProceduralAnimator animator = root.AddComponent<PlayerProceduralAnimator>();

            animator.Bind(
                visual.transform, hips.transform, torso, head,
                armLeft, armRight, legLeft, legRight, weaponMount.transform);

            PlayerRig rig = root.AddComponent<PlayerRig>();
            rig.Bind(motor, aim, health, animator, weaponMount.transform, muzzle.transform,
                cameraFollow.transform);

            root.AddComponent<PlayerDamageFeedback>();
            root.AddComponent<PlayerDebugHooks>();

            // WP-016/018: the AP-006 skinned body under Visual, animated by the AP-009 Animator;
            // the primitives stay hidden; the weapon moves to socket_weapon_r.
            AttachSkinned(root, visual.transform, weaponMount.transform, motor, aim, animator, skin);

            // Input must exist before the first Update, whatever scene we are in.
            PrototypeInputInstaller.EnsureExists();

            // Layer is applied to the whole tree at the end, once every child exists.
            SetLayerRecursively(root, PrototypeLayers.Player);

            root.SetActive(true);

            // The camera rig has to be told what to follow and where to lead the frame.
            PrototypeCameraRig cameraRig = CameraRigFactory.EnsureRig(cameraFollow.transform);
            if (cameraRig != null)
            {
                cameraRig.SetLookAheadProvider(aim);
            }

            return rig;
        }

        /// <summary>
        /// WP-016: instantiate <c>Player_AP006</c> (CharacterAssetSet, Resources) under the
        /// rig's Visual node, hide the primitive parts, hand the motor / aim to the Animator, re-parent
        /// the weapon mount to <c>socket_weapon_r</c> and apply the skin albedo. No-op with the
        /// asset absent or the source Procedural (the M1 primitive body then stays visible).
        /// </summary>
        private static void AttachSkinned(GameObject root, Transform visual, Transform weaponMount, PlayerMotor motor, PlayerAim aim,
            PlayerProceduralAnimator animator, PlayerSkin skin)
        {
            if (!CharacterAssetSet.UseSkinnedPlayer())
            {
                return;
            }

            CharacterAssetSet set = CharacterAssetSet.Load();
            GameObject go = Object.Instantiate(set.PlayerPrefab, visual, false);
            go.name = PlayerSkinnedVisual.PrefabName;
            var skinned = go.GetComponent<PlayerSkinnedVisual>();
            if (skinned == null)
            {
                Debug.LogError("[Player] " + PlayerSkinnedVisual.PrefabName + " prefab has no PlayerSkinnedVisual; keeping the primitive body.");
                DestroySafely(go);
                return;
            }

            PlayerSkinnedVisual.HidePrimitiveParts(visual, null);
            // WP-018: the Animator owns the pose; the motor / aim feed the blend through delegates.
            Transform socket = skinned.Attach(visual,
                () => motor != null ? motor.PlanarVelocity : Vector3.zero,
                () => motor != null && motor.MaxSpeed > 0.01f ? motor.MaxSpeed : 1f,
                () => aim != null ? aim.AimDirection : visual.forward);
            if (animator != null) animator.Skinned = skinned;
            if (socket != null && weaponMount != null)
            {
                weaponMount.SetParent(socket, false);
                weaponMount.localPosition = Vector3.zero;
                weaponMount.localRotation = Quaternion.identity;
                // WP-059: the socket's rotation does not survive Humanoid retargeting (hand roll); the mount
                // re-asserts the authored frame (facing, level) after the Animator every frame.
                WeaponMountAlignment.Attach(weaponMount, root.GetComponent<PlayerRig>());
            }

            ApplySkinAlbedo(skinned, skin);
        }

        private static void ApplySkinAlbedo(PlayerSkinnedVisual skinned, PlayerSkin skin)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            if (skinned == null || set == null || set.PlayerSkinAlbedos == null)
            {
                return;
            }

            int index = (int)skin;
            if (index < 0 || index >= set.PlayerSkinAlbedos.Length)
            {
                index = (int)PlayerSkins.Default;
            }

            skinned.ApplySkin(index, set.PlayerSkinAlbedos[index]);
        }

        /// <summary>Creates a player with the default skin, facing world forward.</summary>
        public static PlayerRig Create(Vector3 position)
        {
            return Create(position, Quaternion.identity, PlayerSkins.Default);
        }

        /// <summary>
        /// Recolours an existing rig. Cheap: it only touches the three shared materials
        /// this factory hands out, through the renderer's material slot.
        /// </summary>
        public static void PaintRig(GameObject root, PlayerSkin skin)
        {
            if (root == null)
            {
                return;
            }

            PlayerSkins.Palette palette = PlayerSkins.Get(skin);

            Transform visual = root.transform.Find(VisualName);
            if (visual == null)
            {
                return;
            }

            // WP-016: the skinned body takes the skin as an albedo swap; the primitives are
            // repainted too (hidden, but a Procedural A/B run shows them).
            ApplySkinAlbedo(root.GetComponentInChildren<PlayerSkinnedVisual>(true), skin);

            Paint(visual, "Hips/Torso", palette.Primary);
            Paint(visual, "Hips/Torso/Head", palette.Secondary);
            Paint(visual, "Hips/Torso/Sash", palette.Accent);
            Paint(visual, "Hips/Torso/Arm.L/Mesh", palette.Secondary);
            Paint(visual, "Hips/Torso/Arm.R/Mesh", palette.Secondary);
            Paint(visual, "Hips/Leg.L/Mesh", palette.Primary);
            Paint(visual, "Hips/Leg.R/Mesh", palette.Primary);
        }

        private static void Paint(Transform visual, string path, Color color)
        {
            Transform t = visual.Find(path);
            if (t == null)
            {
                return;
            }

            var renderer = t.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetMaterial(color);
            }
        }

        /// <summary>
        /// A limb is a pivot at the joint plus a mesh offset half its length, so a
        /// rotation on the pivot swings the limb from the shoulder or hip rather than
        /// from its middle.
        /// </summary>
        private static Transform CreateLimb(
            Transform parent, string name, Vector3 localPosition, float length, float radius, Color color)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPosition;

            CreatePart(
                pivot.transform, "Mesh", PrimitiveType.Capsule,
                new Vector3(0f, -length * 0.5f, 0f),
                new Vector3(radius * 2f, length * 0.5f, radius * 2f),
                color);

            return pivot.transform;
        }

        private static Transform CreatePart(
            Transform parent, string name, PrimitiveType primitive,
            Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = name;

            // Primitives arrive with a collider. The player needs exactly one collider,
            // the CharacterController capsule on the root, so these go.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                DestroySafely(collider);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetMaterial(color);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            return go.transform;
        }

        /// <summary>
        /// One shared URP/Lit material per colour, so a whole crowd of primitives is a
        /// handful of draw calls rather than one per part.
        /// </summary>
        public static Material GetMaterial(Color color)
        {
            int key = ColorKey(color);

            Material material;
            if (s_materials.TryGetValue(key, out material) && material != null)
            {
                return material;
            }

            material = new Material(PrototypeShaders.Lit);
            material.name = "PrototypePlayer_" + key.ToString("X8");
            material.hideFlags = HideFlags.HideAndDontSave;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.18f);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }

            s_materials[key] = material;
            return material;
        }

        private static int ColorKey(Color color)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
            int g = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
            int b = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
            int a = Mathf.RoundToInt(Mathf.Clamp01(color.a) * 255f);
            return (r << 24) | (g << 16) | (b << 8) | a;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (layer < 0)
            {
                return;
            }

            go.layer = layer;

            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
            }
        }

        private static void DestroySafely(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_materials.Clear();
        }
    }
}
