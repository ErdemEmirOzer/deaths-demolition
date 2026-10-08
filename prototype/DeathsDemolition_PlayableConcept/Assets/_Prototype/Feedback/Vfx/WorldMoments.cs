using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>What a moment's air does (WP-092 ANLAR.md `s07` lines, layout-data.json wp092.layout.v1 v002).</summary>
    public enum MomentMist : byte
    {
        None = 0,
        Lane = 1,     // M1: a lane of ground mist drifting with its own vector (+x)
        Pocket = 2,   // M3: a static cold pocket inside a rect
        Water = 3     // M8: over the pond surface only
    }

    public enum MomentDrips : byte
    {
        None = 0,
        Glints = 1,    // M1 / M6: warm drops round the moment's lamps (r = DripRadius)
        Curtain = 2,   // M5: a lit eave curtain r 2.5 m round the lamps (≤ 40 drops) + landing rings (≤ 12)
        Wedge = 3,     // M3: drops in the door's light wedge
        Under = 4      // M7: sparse drops straight under the lanterns
    }

    /// <summary>One WP-092 moment: its circle on the map and the WP-093 effects bound to it.</summary>
    public struct MomentSpec
    {
        public string Id;
        public Vector2 Center;
        public float Radius;
        public float GroundY;
        /// <summary>Pendulum amplitude (degrees) for the hanging chōchin / tsuri inside (0 = the default <see cref="WorldMoments.DefaultSwingDegrees"/>).</summary>
        public float SwingDegrees;
        /// <summary>Emissive lamps inside get a halo too (M7: "halo d0.7 a0.12 per lantern").</summary>
        public bool HaloEmissive;
        /// <summary>Emissive lamps inside get this many sparks at most (M3: "0–2 sparks at sconce").</summary>
        public int EmissiveSparks;
        public MomentMist Mist;
        public Vector4 MistRect;       // Pocket: x0, z0, x1, z1
        public Color MistColour;
        public float MistAlpha;        // ≤ 0.12 (pocket / water ≤ 0.10)
        public float MistTop;          // ≤ 0.3 m (water ≤ 0.4 m)
        public Vector3 MistDrift;
        public bool Hanafubuki;
        public bool Door;
        public Vector3 DoorPoint, DoorDir;
        public float DoorWidth, ShaftLength;
        public MomentDrips Drips;
        public float DripRadius, DripRate;
        public bool FootstepRings;
        public bool AxisStreaks;
        public bool Shoji;
    }

    /// <summary>
    /// WP-093 · the WP-092 moments M1–M8 as data (K/wp-092/layout/layout-data.json v002 `moments[]`, the `s07` line of each; S-05's
    /// LAMP-CONTRACT.md). Live only in the layout world (the S-05 builder root <see cref="LayoutRootName"/> is in the scene) or when a test
    /// sets <see cref="Override"/>. Both LanternFlames (pendulum amplitude, halo / spark rules, axis streaks, shoji) and MomentVfxSystem
    /// (mist, petals, door shaft, drips, footstep rings) read it.
    /// </summary>
    public static class WorldMoments
    {
        /// <summary>WorldV5Layout.RootName (S-05, DD.Art editor assembly — mirrored).</summary>
        public const string LayoutRootName = "Layout v5 (WP-092)";
        public const string ToroGroupName = "M8_toro";
        public const float DefaultSwingDegrees = 3f;

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        /// <summary>The v002 moments (centres / radii from layout-data.json; the numbers after each `s07` line).</summary>
        public static readonly MomentSpec[] V6 =
        {
            new MomentSpec { Id = "M1", Center = new Vector2(-45f, 3f), Radius = 12f, SwingDegrees = 4f,
                Mist = MomentMist.Lane, MistColour = Hex(0xC9D0D6), MistAlpha = 0.10f, MistTop = 0.3f, MistDrift = new Vector3(0.12f, 0f, 0f),
                Drips = MomentDrips.Glints, DripRadius = 1f, DripRate = 2f },
            new MomentSpec { Id = "M2", Center = new Vector2(36f, 1f), Radius = 11f, Hanafubuki = true },
            new MomentSpec { Id = "M3", Center = new Vector2(41f, 50f), Radius = 7f, EmissiveSparks = 2,
                Mist = MomentMist.Pocket, MistRect = new Vector4(45f, 52f, 52f, 54.5f), MistColour = Hex(0x9AA6B2), MistAlpha = 0.08f, MistTop = 0.3f,
                Door = true, DoorPoint = new Vector3(37.74f, 0.25f, 51.2f), DoorDir = Vector3.right, DoorWidth = 2f, ShaftLength = 3.5f,
                Drips = MomentDrips.Wedge, DripRate = 2f },
            new MomentSpec { Id = "M4", Center = new Vector2(0f, 8f), Radius = 16f, FootstepRings = true, AxisStreaks = true },
            new MomentSpec { Id = "M5", Center = new Vector2(0f, 35f), Radius = 14f, SwingDegrees = 3f,
                Drips = MomentDrips.Curtain, DripRadius = 2.5f, DripRate = 14f, Shoji = true },
            new MomentSpec { Id = "M6", Center = new Vector2(-32f, -35f), Radius = 8f, Shoji = true,
                Drips = MomentDrips.Glints, DripRadius = 2f, DripRate = 1.5f },
            new MomentSpec { Id = "M7", Center = new Vector2(-12f, 28f), Radius = 9f, SwingDegrees = 5f, HaloEmissive = true,
                Drips = MomentDrips.Under, DripRadius = 0.4f, DripRate = 1.5f },
            new MomentSpec { Id = "M8", Center = new Vector2(0f, -31f), Radius = 12f, GroundY = -0.15f,
                Mist = MomentMist.Water, MistColour = Hex(0xC6CED6), MistAlpha = 0.08f, MistTop = 0.4f, MistDrift = new Vector3(0f, 0f, -0.03f) },
        };

        /// <summary>Per-lamp halo alpha overrides (M1: "M1_L02 halo a 0.08").</summary>
        public static float HaloAlphaFor(string lampId, float fallback) => lampId == "M1_L02" ? 0.08f : fallback;

        /// <summary>Tests: a synthetic moment set (null = the v6 table, live only in the layout world).</summary>
        public static MomentSpec[] Override;

        /// <summary>Set by the lantern scan when it finds <see cref="LayoutRootName"/> (or forced by tests).</summary>
        public static bool LayoutPresent;

        public static bool Live => Override != null || LayoutPresent;
        public static MomentSpec[] Current => Override ?? V6;

        /// <summary>Index into <see cref="Current"/> of the first live moment whose circle holds <paramref name="p"/> (xz), else −1.</summary>
        public static int Find(Vector3 p)
        {
            if (!Live) return -1;
            MomentSpec[] m = Current;
            for (int i = 0; i < m.Length; i++)
            {
                float dx = p.x - m[i].Center.x, dz = p.z - m[i].Center.y;
                if (dx * dx + dz * dz <= m[i].Radius * m[i].Radius) return i;
            }

            return -1;
        }

        public static int IndexOf(string id)
        {
            MomentSpec[] m = Current;
            for (int i = 0; i < m.Length; i++) if (m[i].Id == id) return i;
            return -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Override = null;
            LayoutPresent = false;
        }
    }
}
