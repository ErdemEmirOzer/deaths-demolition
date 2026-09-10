using System.Collections.Generic;
using UnityEngine;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// Assembles the procedural zombie body once, on the pool template.
    ///
    /// PROTOTYPE-ONLY GEOMETRY. Every dimension below is invented; the request only asks
    /// for "simple procedural clothing layers" in dirty kimono blocks with dark indigo,
    /// black and dirty cream tones, plus kabuto / do-kote / haidate-suneate armour shapes.
    /// The numbers are metres on a ~1.8 m standing figure.
    ///
    /// COST
    /// 13 always-on renderers per zombie plus 12 armour renderers that stay disabled until
    /// a piece is actually equipped. At 40 concurrent enemies that is ~520 visible
    /// renderers sharing 11 materials, which SRP-batches into a handful of draws.
    ///
    /// LAYERS
    ///   root      -> Enemy          (body capsule, blocks the player - DEC-032)
    ///   hitboxes  -> EnemyHitbox    (triggers, raycast targets only)
    ///   attack    -> AttackHitbox   (socket that defines the attack overlap - DEC-029)
    /// </summary>
    public static class ZombieBodyBuilder
    {
        // ----------------------------------------------------------------- proportions
        // All PROTOTYPE-ONLY / invented. These are not decoration: every joint below is a
        // PARENT of a region hitbox or of the attack socket, so the skeleton decides where
        // a head shot ends and a torso shot begins.

        [PrototypeOnly(PendingDecisions.None,
            "Standing height in metres. Also sets the CharacterController height and the " +
            "waist-height centre an explosion is measured from.", true)]
        public const float StandHeight = 1.80f;

        [PrototypeOnly(PendingDecisions.None, "Hip joint height in metres. Root of the lower-body hitbox.", true)]
        private const float HipsHeight = 0.94f;

        [PrototypeOnly(PendingDecisions.None, "Spine joint offset above the hips, metres.", true)]
        private const float SpineLength = 0.20f;

        [PrototypeOnly(PendingDecisions.None,
            "Chest joint offset above the spine, metres. Parent of the upper-body hitbox, " +
            "the head and the attack socket.", true)]
        private const float ChestLength = 0.26f;

        [PrototypeOnly(PendingDecisions.None,
            "Head joint offset above the chest, metres. Sets how high the 2x head region sits.", true)]
        private const float NeckLength = 0.20f;

        [PrototypeOnly(PendingDecisions.None, "Half shoulder span in metres.", true)]
        private const float ShoulderWidth = 0.19f;

        [PrototypeOnly(PendingDecisions.None, "Upper arm length in metres.", true)]
        private const float UpperArmLength = 0.30f;

        [PrototypeOnly(PendingDecisions.None, "Forearm length in metres.", true)]
        private const float ForearmLength = 0.29f;

        [PrototypeOnly(PendingDecisions.None, "Thigh length in metres.", true)]
        private const float ThighLength = 0.47f;

        [PrototypeOnly(PendingDecisions.None, "Shin length in metres.", true)]
        private const float ShinLength = 0.44f;

        [PrototypeOnly(PendingDecisions.None, "Skull radius in metres. Anchors the head hitbox.", true)]
        private const float HeadRadius = 0.115f;

        // -------------------------------------------------------------------- hitboxes
        // GAMEPLAY GEOMETRY, not decoration. These three volumes are the entire regional
        // damage model's contact surface: with the exposed head multiplier at 2x and leg
        // at 0.8x, their sizes are a balance decision the request never made.

        [PrototypeOnly(PendingDecisions.None,
            "Radius of the head hitbox, metres. Defines how hard a 2x head shot is to land.", true)]
        private const float HeadHitboxRadius = 0.155f;

        [PrototypeOnly(PendingDecisions.None, "Radius of the upper-body hitbox, metres.", true)]
        private const float UpperHitboxRadius = 0.26f;

        [PrototypeOnly(PendingDecisions.None, "Height of the upper-body hitbox capsule, metres.", true)]
        private const float UpperHitboxHeight = 0.72f;

        [PrototypeOnly(PendingDecisions.None,
            "Vertical placement of the upper-body hitbox, as a share of ChestLength below the chest joint.", true)]
        private const float UpperHitboxDropFactor = 0.40f;

        [PrototypeOnly(PendingDecisions.None, "Radius of the lower-body hitbox, metres.", true)]
        private const float LowerHitboxRadius = 0.25f;

        [PrototypeOnly(PendingDecisions.None, "Height of the lower-body hitbox capsule, metres.", true)]
        private const float LowerHitboxHeight = 1.02f;

        [PrototypeOnly(PendingDecisions.None,
            "Vertical placement of the lower-body hitbox below the hips, metres.", true)]
        private const float LowerHitboxDrop = -0.40f;

        [PrototypeOnly(PendingDecisions.None,
            "Forward reach of the attack socket from the chest, metres. Together with " +
            "EnemyAttackState's hit radius this is how far a swing actually connects.", true)]
        private const float AttackSocketForward = 0.55f;

        [PrototypeOnly(PendingDecisions.None, "Vertical placement of the attack socket, metres.", true)]
        private const float AttackSocketDrop = -0.05f;

        /// <summary>Build the whole hierarchy under <paramref name="root"/> and fill <paramref name="rig"/>.</summary>
        public static void Build(GameObject root, ZombieRig rig)
        {
            if (root == null || rig == null)
            {
                return;
            }

            var limbBlocks = new List<Renderer>(8);

            // ------------------------------------------------------------------ skeleton
            rig.Visual = NewNode("Visual", root.transform, Vector3.zero);
            rig.Hips = NewNode("Hips", rig.Visual, new Vector3(0f, HipsHeight, 0f));
            rig.Spine = NewNode("Spine", rig.Hips, new Vector3(0f, SpineLength * 0.35f, 0f));
            rig.Chest = NewNode("Chest", rig.Spine, new Vector3(0f, ChestLength, 0f));
            rig.Head = NewNode("Head", rig.Chest, new Vector3(0f, NeckLength, 0f));

            rig.ShoulderL = NewNode("ShoulderL", rig.Chest, new Vector3(-ShoulderWidth, 0.07f, 0f));
            rig.ShoulderR = NewNode("ShoulderR", rig.Chest, new Vector3(ShoulderWidth, 0.07f, 0f));
            rig.UpperArmL = NewNode("UpperArmL", rig.ShoulderL, Vector3.zero);
            rig.UpperArmR = NewNode("UpperArmR", rig.ShoulderR, Vector3.zero);
            rig.ForearmL = NewNode("ForearmL", rig.UpperArmL, new Vector3(0f, -UpperArmLength, 0f));
            rig.ForearmR = NewNode("ForearmR", rig.UpperArmR, new Vector3(0f, -UpperArmLength, 0f));

            rig.ThighL = NewNode("ThighL", rig.Hips, new Vector3(-0.11f, -0.04f, 0f));
            rig.ThighR = NewNode("ThighR", rig.Hips, new Vector3(0.11f, -0.04f, 0f));
            rig.ShinL = NewNode("ShinL", rig.ThighL, new Vector3(0f, -ThighLength, 0f));
            rig.ShinR = NewNode("ShinR", rig.ThighR, new Vector3(0f, -ThighLength, 0f));

            // ------------------------------------------------------- cloth / flesh blocks
            rig.PelvisBlock = Block("Pelvis", rig.Hips, ZombiePalette.Cube,
                new Vector3(0f, -0.05f, 0f), new Vector3(0.30f, 0.20f, 0.20f), ZombiePalette.Cloth(0));

            rig.TorsoBlock = Block("Torso", rig.Chest, ZombiePalette.Cube,
                new Vector3(0f, -ChestLength * 0.45f, 0f), new Vector3(0.36f, 0.46f, 0.22f), ZombiePalette.Cloth(0));

            rig.HeadBlock = Block("Skull", rig.Head, ZombiePalette.Sphere,
                new Vector3(0f, HeadRadius, 0f), new Vector3(HeadRadius * 2f, HeadRadius * 2.2f, HeadRadius * 2f),
                ZombiePalette.Of(ZombiePalette.Accent.Flesh));

            // Lower kimono - the loose skirt block that reads as a period garment.
            rig.SkirtBlock = Block("Skirt", rig.Hips, ZombiePalette.Cube,
                new Vector3(0f, -0.26f, 0f), new Vector3(0.36f, 0.40f, 0.26f), ZombiePalette.Cloth(0));

            // Sash. Tier and variation repaint it; the Runner's is red by request.
            rig.SashBlock = Block("Sash", rig.Hips, ZombiePalette.Cube,
                new Vector3(0f, 0.03f, 0f), new Vector3(0.33f, 0.09f, 0.23f),
                ZombiePalette.Of(ZombiePalette.Accent.SashDark));

            limbBlocks.Add(Block("UpperArmL", rig.UpperArmL, ZombiePalette.Capsule,
                new Vector3(0f, -UpperArmLength * 0.5f, 0f), new Vector3(0.115f, UpperArmLength * 0.62f, 0.115f),
                ZombiePalette.Cloth(0)));
            limbBlocks.Add(Block("UpperArmR", rig.UpperArmR, ZombiePalette.Capsule,
                new Vector3(0f, -UpperArmLength * 0.5f, 0f), new Vector3(0.115f, UpperArmLength * 0.62f, 0.115f),
                ZombiePalette.Cloth(0)));
            limbBlocks.Add(Block("ForearmL", rig.ForearmL, ZombiePalette.Capsule,
                new Vector3(0f, -ForearmLength * 0.5f, 0f), new Vector3(0.10f, ForearmLength * 0.60f, 0.10f),
                ZombiePalette.Of(ZombiePalette.Accent.Flesh)));
            limbBlocks.Add(Block("ForearmR", rig.ForearmR, ZombiePalette.Capsule,
                new Vector3(0f, -ForearmLength * 0.5f, 0f), new Vector3(0.10f, ForearmLength * 0.60f, 0.10f),
                ZombiePalette.Of(ZombiePalette.Accent.Flesh)));

            limbBlocks.Add(Block("ThighL", rig.ThighL, ZombiePalette.Capsule,
                new Vector3(0f, -ThighLength * 0.5f, 0f), new Vector3(0.145f, ThighLength * 0.62f, 0.145f),
                ZombiePalette.Cloth(0)));
            limbBlocks.Add(Block("ThighR", rig.ThighR, ZombiePalette.Capsule,
                new Vector3(0f, -ThighLength * 0.5f, 0f), new Vector3(0.145f, ThighLength * 0.62f, 0.145f),
                ZombiePalette.Cloth(0)));
            limbBlocks.Add(Block("ShinL", rig.ShinL, ZombiePalette.Capsule,
                new Vector3(0f, -ShinLength * 0.5f, 0f), new Vector3(0.12f, ShinLength * 0.60f, 0.12f),
                ZombiePalette.Cloth(0)));
            limbBlocks.Add(Block("ShinR", rig.ShinR, ZombiePalette.Capsule,
                new Vector3(0f, -ShinLength * 0.5f, 0f), new Vector3(0.12f, ShinLength * 0.60f, 0.12f),
                ZombiePalette.Cloth(0)));

            rig.LimbBlocks = limbBlocks.ToArray();

            // ------------------------------------------------------------------- armour
            BuildHelmet(rig);
            BuildBodyArmor(rig);
            BuildLegArmor(rig);

            // ----------------------------------------------------------------- hitboxes
            // Parented to animated joints so a leaning, lunging or staggering zombie is hit
            // where it actually is. Triggers on the EnemyHitbox layer: raycast targets only,
            // never physical blockers.
            rig.HeadHitbox = SphereHitbox("Hitbox_Head", rig.Head, new Vector3(0f, HeadRadius, 0f), HeadHitboxRadius);
            rig.UpperHitbox = CapsuleHitbox("Hitbox_UpperBody", rig.Chest,
                new Vector3(0f, -ChestLength * UpperHitboxDropFactor, 0f),
                UpperHitboxRadius, UpperHitboxHeight);
            rig.LowerHitbox = CapsuleHitbox("Hitbox_LowerBody", rig.Hips,
                new Vector3(0f, LowerHitboxDrop, 0f), LowerHitboxRadius, LowerHitboxHeight);

            // ------------------------------------------------------------------ sockets
            rig.AttackSocket = NewNode("AttackSocket", rig.Chest,
                new Vector3(0f, AttackSocketDrop, AttackSocketForward));
            rig.AttackSocket.gameObject.layer = PrototypeLayers.AttackHitbox;

            rig.ChestSocket = NewNode("ChestSocket", rig.Chest, new Vector3(0f, -0.10f, 0.14f));
            rig.FootSocket = NewNode("FootSocket", rig.Visual, new Vector3(0f, 0.04f, 0f));

            rig.CaptureRestPose();
        }

        // ------------------------------------------------------------------ armour sets
        // Each set is four separate blocks. Durability loss disables them one at a time,
        // so armour damage is visible BEFORE the break (REQ-063, REQ-129) with no material
        // work at all: plates simply come off.

        private static void BuildHelmet(ZombieRig rig)
        {
            rig.HelmetRoot = NewNode("Armor_Helmet", rig.Head, Vector3.zero).gameObject;
            Material metal = ZombiePalette.Of(ZombiePalette.Accent.OxidisedMetal);
            Material dark = ZombiePalette.Of(ZombiePalette.Accent.DarkMetal);

            // Order matters: last entries are shed first as durability drops.
            var bowl = Block("Kabuto_Bowl", rig.HelmetRoot.transform, ZombiePalette.Sphere,
                new Vector3(0f, HeadRadius + 0.035f, 0f), new Vector3(0.27f, 0.20f, 0.27f), metal).gameObject;

            var mask = Block("Kabuto_Mask", rig.HelmetRoot.transform, ZombiePalette.Cube,
                new Vector3(0f, HeadRadius - 0.02f, 0.10f), new Vector3(0.17f, 0.11f, 0.05f), dark).gameObject;

            var flapL = Block("Kabuto_FlapL", rig.HelmetRoot.transform, ZombiePalette.Cube,
                new Vector3(-0.13f, HeadRadius - 0.02f, -0.02f), new Vector3(0.05f, 0.13f, 0.17f), metal).gameObject;

            var crest = Block("Kabuto_Crest", rig.HelmetRoot.transform, ZombiePalette.Cube,
                new Vector3(0f, HeadRadius + 0.16f, 0.02f), new Vector3(0.03f, 0.12f, 0.10f), dark).gameObject;

            rig.HelmetPieces = new[] { bowl, mask, flapL, crest };
        }

        private static void BuildBodyArmor(ZombieRig rig)
        {
            rig.BodyArmorRoot = NewNode("Armor_Body", rig.Chest, Vector3.zero).gameObject;
            Material metal = ZombiePalette.Of(ZombiePalette.Accent.OxidisedMetal);
            Material dark = ZombiePalette.Of(ZombiePalette.Accent.DarkMetal);

            var doFront = Block("Do_Front", rig.BodyArmorRoot.transform, ZombiePalette.Cube,
                new Vector3(0f, -ChestLength * 0.45f, 0.13f), new Vector3(0.38f, 0.44f, 0.06f), metal).gameObject;

            var doBack = Block("Do_Back", rig.BodyArmorRoot.transform, ZombiePalette.Cube,
                new Vector3(0f, -ChestLength * 0.45f, -0.13f), new Vector3(0.36f, 0.42f, 0.05f), dark).gameObject;

            // Kote sit on the forearms, so they ride the arm animation.
            var koteL = Block("Kote_L", rig.ForearmL, ZombiePalette.Cube,
                new Vector3(0f, -ForearmLength * 0.45f, 0f), new Vector3(0.15f, 0.22f, 0.15f), metal).gameObject;

            var koteR = Block("Kote_R", rig.ForearmR, ZombiePalette.Cube,
                new Vector3(0f, -ForearmLength * 0.45f, 0f), new Vector3(0.15f, 0.22f, 0.15f), metal).gameObject;

            rig.BodyArmorPieces = new[] { doFront, doBack, koteL, koteR };
        }

        private static void BuildLegArmor(ZombieRig rig)
        {
            rig.LegArmorRoot = NewNode("Armor_Leg", rig.Hips, Vector3.zero).gameObject;
            Material metal = ZombiePalette.Of(ZombiePalette.Accent.OxidisedMetal);
            Material dark = ZombiePalette.Of(ZombiePalette.Accent.DarkMetal);

            var haidateL = Block("Haidate_L", rig.ThighL, ZombiePalette.Cube,
                new Vector3(0f, -ThighLength * 0.45f, 0.02f), new Vector3(0.20f, 0.30f, 0.19f), dark).gameObject;

            var haidateR = Block("Haidate_R", rig.ThighR, ZombiePalette.Cube,
                new Vector3(0f, -ThighLength * 0.45f, 0.02f), new Vector3(0.20f, 0.30f, 0.19f), dark).gameObject;

            var suneateL = Block("Suneate_L", rig.ShinL, ZombiePalette.Cube,
                new Vector3(0f, -ShinLength * 0.45f, 0.03f), new Vector3(0.15f, 0.30f, 0.09f), metal).gameObject;

            var suneateR = Block("Suneate_R", rig.ShinR, ZombiePalette.Cube,
                new Vector3(0f, -ShinLength * 0.45f, 0.03f), new Vector3(0.15f, 0.30f, 0.09f), metal).gameObject;

            rig.LegArmorPieces = new[] { haidateL, haidateR, suneateL, suneateR };
        }

        // --------------------------------------------------------------------- plumbing

        private static Transform NewNode(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            return t;
        }

        private static Renderer Block(string name, Transform parent, Mesh mesh, Vector3 localPosition,
            Vector3 size, Material material)
        {
            var go = new GameObject(name);
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.identity;
            t.localScale = size;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            return renderer;
        }

        private static Collider SphereHitbox(string name, Transform parent, Vector3 localCentre, float radius)
        {
            var go = new GameObject(name);
            go.layer = PrototypeLayers.EnemyHitbox;
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            var collider = go.AddComponent<SphereCollider>();
            collider.center = localCentre;
            collider.radius = radius;
            collider.isTrigger = true;
            return collider;
        }

        private static Collider CapsuleHitbox(string name, Transform parent, Vector3 localCentre,
            float radius, float height)
        {
            var go = new GameObject(name);
            go.layer = PrototypeLayers.EnemyHitbox;
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            var collider = go.AddComponent<CapsuleCollider>();
            collider.center = localCentre;
            collider.radius = radius;
            collider.height = height;
            collider.direction = 1; // Y
            collider.isTrigger = true;
            return collider;
        }
    }
}
