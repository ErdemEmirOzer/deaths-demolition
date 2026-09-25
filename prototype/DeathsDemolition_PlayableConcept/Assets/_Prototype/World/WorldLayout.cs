using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>What a square metre of ground is made of. Drives material AND impact sound.</summary>
    public enum GroundSurface
    {
        /// <summary>Outside the walkable world, or the koi pond: no ground is generated.</summary>
        None = 0,

        /// <summary>Raked gravel of the central courtyard.</summary>
        Gravel = 1,

        /// <summary>Cut stone paving: the palace road, the ring route, the radial shortcuts.</summary>
        Stone = 2,

        /// <summary>Packed earth: the narrow secondary tracks and shortcuts.</summary>
        Dirt = 3,

        /// <summary>Moss and grass: everything that is not a route.</summary>
        Moss = 4
    }

    /// <summary>
    /// THE topology. Every other file in this folder asks this one where things are:
    /// the ground painter, the prop placement, the spawn markers, the Special Weapon
    /// points and the atmosphere emitters. There is exactly one source of truth for the
    /// map, and it is data, not geometry.
    ///
    /// AXES (REQ-106): +Z north, +X east, -Z south, -X west, origin at the courtyard.
    /// This matches <see cref="DD.Prototype.Spawn.DefaultSpawnPointLayout"/> and
    /// <see cref="DD.Prototype.Navigation.RingRegionMap"/> exactly.
    ///
    /// HOW THE ROUTE NETWORK IS DEFINED
    /// --------------------------------
    /// As a list of segments with a half-width, not as painted rectangles. A segment is
    /// a capsule on the XZ plane; a ground cell takes the surface of the highest-priority
    /// segment it falls inside. That gives, for free:
    ///   - varying road widths (REQ-112): 16 m palace road, 14 m forecourt, 7 m ring,
    ///     6 m radials, 5 m bridge approach, 3-4 m garden tracks, 3 m bamboo corridor;
    ///   - a deliberately imperfect ring (REQ-106): the legs are straight chords between
    ///     corners at different radii, 25-40 m from the centre, never a circle;
    ///   - two-way traversal and no dead ends, because every segment endpoint is shared
    ///     with at least one other segment. <see cref="ValidateConnectivity"/> asserts it.
    ///
    /// EVERY NUMBER IN THIS FILE IS PROTOTYPE-ONLY AND INVENTED. The request fixes the
    /// 90x90 m size, the five regions and the rules; it never gives a coordinate.
    /// </summary>
    /// <summary>WP-074 B1b · Which world layout is live: the ±45 map (procedural Run / World_AP005) or the v6 ±55 layout (greybox now, the World v5 scene later).</summary>
    public enum WorldLayoutKind
    {
        Ap005 = 0,
        V6 = 1
    }

    public static class WorldLayout
    {
        // ----------------------------------------------------------------- live layout (WP-074 B1b)

        /// <summary>
        /// The layout of the world that is loaded, set by the world bootstrap from its placement asset (<c>layout</c> "v6"); S-03's
        /// SpawnWorldProfile reads it (v6 band 22–45 m, MaxUseful 75, inner gates) instead of a scene name — the greybox and the later
        /// World v5 scene are different scenes with the same layout. Defaults to <see cref="WorldLayoutKind.Ap005"/>.
        /// </summary>
        public static WorldLayoutKind Current { get; private set; } = WorldLayoutKind.Ap005;

        /// <summary>Raised with the new layout when <see cref="Current"/> changes.</summary>
        public static event System.Action<WorldLayoutKind> Changed;

        public static void SetCurrent(WorldLayoutKind layout)
        {
            if (Current == layout) return;
            Current = layout;
            Changed?.Invoke(layout);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCurrent()
        {
            Current = WorldLayoutKind.Ap005;
            Changed = null;
        }

        // ----------------------------------------------------------------- extents

        public const float MapSize = 90f;
        public const float HalfMap = 45f;

        /// <summary>Ground cell edge in metres. 1 m keeps the paint crisp at 90x90.</summary>
        public const float CellSize = 1f;

        /// <summary>Half-extent of the square courtyard. Ring classification uses 14 m.</summary>
        public const float CourtyardHalf = 13.5f;

        /// <summary>Corner rounding of the courtyard, so it does not read as a box.</summary>
        public const float CourtyardCornerRadius = 3.5f;

        /// <summary>Value handed to <see cref="DD.Prototype.Navigation.RingRegionMap"/>.</summary>
        public const float CenterRadius = 14f;

        /// <summary>Where the player starts Round 1: courtyard, a little south of centre, facing the palace.</summary>
        public static readonly Vector3 PlayerStart = new Vector3(0f, 0.1f, -3f);

        // ----------------------------------------------------------------- heights

        public const float GroundY = 0f;
        public const float WaterY = -0.45f;
        public const float PondFloorY = -1.1f;
        public const float BridgeDeckY = 0.9f;

        /// <summary>
        /// Every raised walkable platform is kept UNDER the NavMesh agent's climb height
        /// (0.4 m in NavMeshSettings), so the veranda and the courtyard dais link
        /// themselves to the ground mesh without a single hand-placed off-mesh link.
        /// That is the entire reason these are 0.35 and 0.3 and not "whatever looks
        /// right": above 0.4 they become islands and enemies stop following the player
        /// onto them.
        /// </summary>
        public const float VerandaY = 0.35f;

        public const float DaisY = 0.3f;

        /// <summary>Agent climb from NavMeshSettings. Anything higher needs a ramp.</summary>
        public const float MaxAutoStep = 0.4f;

        // ----------------------------------------------------------------- pond

        /// <summary>
        /// The koi pond as a union of three overlapping rectangles. A union, not a
        /// rectangle, because REQ-110 wants a shoreline you have to read rather than
        /// a shape you can guess.
        /// </summary>
        public static readonly Rect[] Pond =
        {
            Rect.MinMaxRect(-11f, -33f, 6f, -20f),
            Rect.MinMaxRect(2f, -36f, 12f, -24f),
            Rect.MinMaxRect(-9f, -38f, 1f, -30f)
        };

        /// <summary>Narrow bridge centre line (x is fixed, it runs north-south).</summary>
        public const float BridgeX = 4f;

        public const float BridgeNorthZ = -18f;
        public const float BridgeSouthZ = -39.5f;

        /// <summary>Ramp run at each end, from ground level up to the deck.</summary>
        public const float BridgeRampLength = 2.5f;

        /// <summary>Deck width. Narrow on purpose - it is the choke REQ-110 asks for.</summary>
        public const float BridgeWidth = 3f;

        // ----------------------------------------------------------------- corridor

        /// <summary>Enclosed west corridor: walls at these x, roof over the gap.</summary>
        public const float CorridorWestX = -36f;

        public const float CorridorEastX = -30f;
        public const float CorridorSouthZ = -12f;
        public const float CorridorNorthZ = 12f;
        public const float CorridorRoofY = 3.4f;

        /// <summary>Half-height of the mid doorway in the corridor's east wall (REQ-111: >= 2 links).</summary>
        public const float CorridorDoorHalf = 2.5f;

        // ----------------------------------------------------------------- palace

        public const float PalaceSouthZ = 34f;
        public const float PalaceNorthZ = 44f;
        public const float PalaceHalfX = 20f;
        public const float PalaceWallY = 6.5f;

        // ----------------------------------------------------------------- routes

        public struct PathSegment
        {
            public Vector2 A;
            public Vector2 B;
            public float HalfWidth;
            public GroundSurface Surface;

            public PathSegment(float ax, float az, float bx, float bz, float halfWidth, GroundSurface surface)
            {
                A = new Vector2(ax, az);
                B = new Vector2(bx, bz);
                HalfWidth = halfWidth;
                Surface = surface;
            }
        }

        /// <summary>
        /// The route network, in paint priority order: later entries win, so a dirt
        /// track crossing the stone forecourt does not cut a hole in it.
        /// </summary>
        public static readonly PathSegment[] Paths =
        {
            // ---------- secondary tracks (painted first, overwritten by anything wider)

            // Radial shortcuts on the diagonals (REQ-106: centre reachable from every region).
            new PathSegment(10f, 10f, 23f, 22f, 2.0f, GroundSurface.Dirt),
            new PathSegment(23f, 22f, 26f, 25f, 2.0f, GroundSurface.Dirt),
            new PathSegment(10f, -10f, 23f, -22f, 2.0f, GroundSurface.Dirt),
            new PathSegment(23f, -22f, 25f, -25f, 2.0f, GroundSurface.Dirt),
            new PathSegment(-10f, -10f, -23f, -22f, 2.0f, GroundSurface.Dirt),
            new PathSegment(-23f, -22f, -27f, -24f, 2.0f, GroundSurface.Dirt),
            new PathSegment(-10f, 10f, -23f, 22f, 2.0f, GroundSurface.Dirt),
            new PathSegment(-23f, 22f, -26f, 25f, 2.0f, GroundSurface.Dirt),

            // Sakura garden inner trails (REQ-109: medium-width paths through the grove).
            // Both ends of both trails deliberately land ON another road: the west end on
            // the east radial, the north/south end on the ring. Their first draft started
            // at (16, +-6), which is open moss - ValidateConnectivity caught it.
            new PathSegment(17f, 2f, 24f, 10f, 1.8f, GroundSurface.Dirt),
            new PathSegment(24f, 10f, 30f, 16.5f, 1.8f, GroundSurface.Dirt),
            new PathSegment(17f, -2f, 24f, -10f, 1.8f, GroundSurface.Dirt),
            new PathSegment(24f, -10f, 30f, -16.5f, 1.8f, GroundSurface.Dirt),
            new PathSegment(39f, 1f, 42f, -10f, 1.8f, GroundSurface.Dirt),
            new PathSegment(42f, -10f, 34f, -14f, 1.8f, GroundSurface.Dirt),

            // Bamboo bypass: a second, winding way past the enclosed corridor (REQ-111).
            new PathSegment(-33f, -14f, -40f, -10f, 1.5f, GroundSurface.Dirt),
            new PathSegment(-40f, -10f, -40f, 10f, 1.5f, GroundSurface.Dirt),
            new PathSegment(-40f, 10f, -33f, 14f, 1.5f, GroundSurface.Dirt),

            // Pagoda spur in the south-west, looping back to the shore road (no dead end).
            new PathSegment(-27f, -28f, -31f, -32f, 2.0f, GroundSurface.Dirt),
            new PathSegment(-31f, -32f, -24f, -38f, 2.0f, GroundSurface.Dirt),
            new PathSegment(-24f, -38f, -20f, -41f, 2.0f, GroundSurface.Dirt),

            // ---------- the south: the bridge and the two shore roads parallel to it

            new PathSegment(20f, -41f, -20f, -41f, 2.5f, GroundSurface.Dirt),   // outer shore road
            new PathSegment(-15f, -17f, -15f, -41f, 2.5f, GroundSurface.Dirt),  // west shore, parallel to the bridge
            new PathSegment(16f, -17f, 16f, -41f, 2.5f, GroundSurface.Dirt),    // east shore, parallel to the bridge
            new PathSegment(22f, -17f, 25f, -25f, 2.5f, GroundSurface.Dirt),
            new PathSegment(-22f, -17f, -27f, -24f, 2.5f, GroundSurface.Dirt),
            new PathSegment(25f, -25f, 27f, -32f, 2.5f, GroundSurface.Dirt),
            new PathSegment(27f, -32f, 20f, -41f, 2.5f, GroundSurface.Dirt),
            new PathSegment(-20f, -41f, -27f, -32f, 2.5f, GroundSurface.Dirt),
            new PathSegment(-27f, -32f, -27f, -24f, 3.0f, GroundSurface.Dirt),

            // ---------- the ring proper (stone)

            new PathSegment(-22f, -17f, 22f, -17f, 3.0f, GroundSurface.Stone),  // pond north shore
            new PathSegment(-27f, -24f, -33f, -12f, 3.0f, GroundSurface.Stone), // south-west climb
            new PathSegment(-33f, -12f, -33f, 12f, 2.4f, GroundSurface.Stone),  // enclosed corridor
            new PathSegment(-33f, 12f, -26f, 25f, 3.0f, GroundSurface.Stone),   // north-west climb
            new PathSegment(26f, 25f, 31f, 16f, 4.0f, GroundSurface.Stone),     // north-east descent
            new PathSegment(31f, 16f, 34f, 8f, 3.5f, GroundSurface.Stone),
            new PathSegment(34f, 8f, 39f, 1f, 2.5f, GroundSurface.Stone),       // outer fork, great sakura
            new PathSegment(39f, 1f, 34f, -7f, 2.5f, GroundSurface.Stone),
            new PathSegment(34f, 8f, 30f, 1f, 2.5f, GroundSurface.Stone),       // inner fork, great sakura
            new PathSegment(30f, 1f, 34f, -7f, 2.5f, GroundSurface.Stone),
            new PathSegment(34f, -7f, 30f, -17f, 3.0f, GroundSurface.Stone),
            new PathSegment(30f, -17f, 25f, -25f, 3.0f, GroundSurface.Stone),

            // ---------- radial shortcuts on the cardinals

            new PathSegment(13f, 0f, 30f, 1f, 3.5f, GroundSurface.Stone),       // east radial
            new PathSegment(-13f, 0f, -30f, 0f, 3.0f, GroundSurface.Stone),     // west radial into the corridor door
            new PathSegment(0f, -13f, 0f, -17f, 4.0f, GroundSurface.Stone),     // south radial
            new PathSegment(BridgeX, -13f, BridgeX, -17f, 3.0f, GroundSurface.Stone),

            // ---------- the north: the widest ground on the map (REQ-108)

            new PathSegment(-26f, 25f, 26f, 25f, 7.0f, GroundSurface.Stone),    // 14 m forecourt band
            new PathSegment(-14f, 31f, 14f, 31f, 3.0f, GroundSurface.Stone),    // along the veranda
            new PathSegment(0f, 22f, 0f, 31f, 6.0f, GroundSurface.Stone),
            new PathSegment(0f, 13f, 0f, 22f, 8.0f, GroundSurface.Stone)        // 16 m palace road
        };

        // ----------------------------------------------------------------- queries

        /// <summary>Ground surface at a world position. The map's single classifier.</summary>
        public static GroundSurface Classify(float x, float z)
        {
            if (x < -HalfMap || x > HalfMap || z < -HalfMap || z > HalfMap)
            {
                return GroundSurface.None;
            }

            if (IsInPond(x, z))
            {
                return GroundSurface.None;
            }

            if (IsInCourtyard(x, z))
            {
                return GroundSurface.Gravel;
            }

            GroundSurface surface = GroundSurface.Moss;

            for (int i = 0; i < Paths.Length; i++)
            {
                PathSegment segment = Paths[i];
                if (DistanceToSegment(x, z, segment.A, segment.B) <= segment.HalfWidth)
                {
                    surface = segment.Surface;
                }
            }

            return surface;
        }

        public static bool IsInPond(float x, float z)
        {
            for (int i = 0; i < Pond.Length; i++)
            {
                Rect r = Pond[i];
                if (x >= r.xMin && x <= r.xMax && z >= r.yMin && z <= r.yMax)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Rounded square. The corner radius is what stops it reading as a box.</summary>
        public static bool IsInCourtyard(float x, float z)
        {
            float ax = Mathf.Abs(x);
            float az = Mathf.Abs(z);

            if (ax > CourtyardHalf || az > CourtyardHalf)
            {
                return false;
            }

            float inner = CourtyardHalf - CourtyardCornerRadius;
            if (ax <= inner || az <= inner)
            {
                return true;
            }

            float dx = ax - inner;
            float dz = az - inner;
            return dx * dx + dz * dz <= CourtyardCornerRadius * CourtyardCornerRadius;
        }

        /// <summary>True inside the enclosed corridor's interior, doorway included.</summary>
        public static bool IsInCorridor(float x, float z)
        {
            return x >= CorridorWestX && x <= CorridorEastX &&
                   z >= CorridorSouthZ && z <= CorridorNorthZ;
        }

        /// <summary>True on the bridge deck footprint (ramps included).</summary>
        public static bool IsOnBridge(float x, float z)
        {
            return Mathf.Abs(x - BridgeX) <= BridgeWidth * 0.5f &&
                   z >= BridgeSouthZ && z <= BridgeNorthZ;
        }

        /// <summary>
        /// Deck height at a point on the bridge, ramps interpolated.
        ///
        /// The ramp starts at <see cref="BridgeLipY"/> rather than 0 so the deck is never
        /// coplanar with the ground it lands on: coplanar quads z-fight, and the fixed
        /// camera looks straight down the bridge, which is the worst possible angle for
        /// it. 6 cm is far under the agent climb, so the NavMesh still welds the two.
        /// </summary>
        public static float BridgeHeight(float z)
        {
            float fromNorth = BridgeNorthZ - z;
            float fromSouth = z - BridgeSouthZ;
            float t = Mathf.Min(fromNorth, fromSouth) / BridgeRampLength;
            float ramp = Mathf.Lerp(BridgeLipY, BridgeDeckY, Mathf.Clamp01(t));

            // Gentle arch on top of the ramp, so the landmark reads as a bridge from the
            // far shore instead of as a plank. Peak is BridgeArch metres over ~21 m of
            // run: about 3 degrees, which is nothing to walk on and everything to look at.
            float span = Mathf.InverseLerp(BridgeSouthZ, BridgeNorthZ, z);
            float arch = Mathf.Sin(span * Mathf.PI) * BridgeArch;

            return ramp + arch * Mathf.Clamp01(t);
        }

        /// <summary>Height of the bridge deck where it meets the shore.</summary>
        public const float BridgeLipY = 0.06f;

        /// <summary>Rise of the arch at the middle of the span.</summary>
        public const float BridgeArch = 0.55f;

        /// <summary>
        /// Walkable surface height, used to sit markers, props and pickups on the world
        /// instead of guessing. Returns false where there is no ground at all.
        /// </summary>
        public static bool TryGroundHeight(float x, float z, out float y)
        {
            if (IsOnBridge(x, z))
            {
                y = BridgeHeight(z);
                return true;
            }

            if (IsInPond(x, z))
            {
                y = WaterY;
                return false;
            }

            if (x < -HalfMap || x > HalfMap || z < -HalfMap || z > HalfMap)
            {
                y = GroundY;
                return false;
            }

            y = GroundY;
            return true;
        }

        public static float DistanceToSegment(float x, float z, Vector2 a, Vector2 b)
        {
            float abx = b.x - a.x;
            float abz = b.y - a.y;
            float apx = x - a.x;
            float apz = z - a.y;

            float lenSq = abx * abx + abz * abz;
            float t = lenSq > 0.0001f ? Mathf.Clamp01((apx * abx + apz * abz) / lenSq) : 0f;

            float dx = apx - abx * t;
            float dz = apz - abz * t;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Cheap deterministic value noise, so scatter is identical every run.</summary>
        public static float Noise(float x, float z, float frequency, int seed)
        {
            float fx = x * frequency;
            float fz = z * frequency;

            int x0 = Mathf.FloorToInt(fx);
            int z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0;
            float tz = fz - z0;

            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);

            float n00 = Hash(x0, z0, seed);
            float n10 = Hash(x0 + 1, z0, seed);
            float n01 = Hash(x0, z0 + 1, seed);
            float n11 = Hash(x0 + 1, z0 + 1, seed);

            return Mathf.Lerp(Mathf.Lerp(n00, n10, tx), Mathf.Lerp(n01, n11, tx), tz);
        }

        public static float Hash(int x, int z, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + z * 668265263 + seed * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffff) / 8388607f;
            }
        }

        // ----------------------------------------------------------------- landmarks

        /// <summary>
        /// REQ-114: every region gets one thing you can name and steer by. These are the
        /// anchors the prop builder places its biggest silhouettes on, and they are data
        /// so the report and the builder cannot disagree about where "the Great Sakura"
        /// is.
        /// </summary>
        public struct Landmark
        {
            public readonly string Name;
            public readonly Vector2 Position;
            public readonly float Height;

            public Landmark(string name, float x, float z, float height)
            {
                Name = name;
                Position = new Vector2(x, z);
                Height = height;
            }
        }

        public static readonly Landmark[] Landmarks =
        {
            new Landmark("Great Pine (centre)", -6f, 5f, 8.5f),
            new Landmark("Palace and tiled roof (north)", 0f, 39f, 10.2f),
            new Landmark("Main gate, vermilion (north)", 0f, 22f, 6.4f),
            new Landmark("Corner watchtower (north-east)", 27f, 28f, 9.5f),
            new Landmark("Great Sakura (east)", 34.5f, 0.5f, 9f),
            new Landmark("Arched bridge over the koi pond (south)", BridgeX, -28f, 2.6f),
            new Landmark("Five-tier stone pagoda (south-west)", -31f, -32f, 11f),
            new Landmark("Enclosed corridor (west)", -33f, 0f, 3.9f),
            new Landmark("Bell tower (north-west)", -31f, 30f, 7.5f)
        };

        // ----------------------------------------------------------------- validation

        /// <summary>
        /// Proves the three topology rules the request states but geometry cannot show:
        /// the network is ONE connected component (REQ-107 "tamamen acik"), it has no
        /// dead end (request: "cikmaz sokaklardan kacin"), and every outer region owns a
        /// radial that actually reaches the courtyard (REQ-106).
        ///
        /// It runs on the data, not on the built scene, so a bad coordinate is caught
        /// before a single triangle is generated. Returns a human-readable report and
        /// sets <paramref name="ok"/>.
        /// </summary>
        public static string ValidateConnectivity(out bool ok)
        {
            // Slack on the overlap test, in metres. Roads that "meet" in the data by
            // landing on each other's edge are connected in fact; this absorbs the float
            // noise without letting a genuine two-metre gap pass.
            const float Slack = 0.25f;

            int n = Paths.Length;

            // NODES ARE SEGMENTS, NOT ENDPOINTS. An earlier version welded endpoint to
            // endpoint and reported nineteen dead ends, every one of them false: most of
            // this network joins T-wise, an endpoint landing in the MIDDLE of another
            // road. The physical question is "do the two painted strips overlap", so that
            // is the question asked, and it handles T-junctions, crossings and
            // end-to-end joins with one test.
            var parent = new int[n + 1];
            for (int i = 0; i <= n; i++)
            {
                parent[i] = i;
            }

            int courtyard = n;

            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }

                return i;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra != rb)
                {
                    parent[rb] = ra;
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (TouchesCourtyard(Paths[i]))
                {
                    Union(i, courtyard);
                }

                for (int j = i + 1; j < n; j++)
                {
                    float gap = SegmentDistance(Paths[i].A, Paths[i].B, Paths[j].A, Paths[j].B);
                    if (gap <= Paths[i].HalfWidth + Paths[j].HalfWidth + Slack)
                    {
                        Union(i, j);
                    }
                }
            }

            // The bridge is geometry, not paint, so it is not in Paths. Weld the roads at
            // its two ends by hand or the south reads as two components.
            int northEnd = NearestSegment(new Vector2(BridgeX, BridgeNorthZ + 0.5f));
            int southEnd = NearestSegment(new Vector2(BridgeX, BridgeSouthZ - 1.5f));
            if (northEnd >= 0 && southEnd >= 0)
            {
                Union(northEnd, southEnd);
            }

            var report = new System.Text.StringBuilder(512);

            int root = Find(courtyard);
            int stranded = 0;
            for (int i = 0; i < n; i++)
            {
                if (Find(i) != root)
                {
                    stranded++;
                    report.Append("  DISCONNECTED segment ").Append(i)
                        .Append(' ').Append(Paths[i].A).Append(" -> ").Append(Paths[i].B).Append('\n');
                }
            }

            // A dead end is an endpoint that no OTHER road covers and that is not in the
            // courtyard: you walk to it and you have to turn around.
            int deadEnds = 0;
            for (int i = 0; i < n; i++)
            {
                deadEnds += CheckEnd(report, i, Paths[i].A, Slack);
                deadEnds += CheckEnd(report, i, Paths[i].B, Slack);
            }

            // REQ-106: a radial from every outer region into the courtyard. A radial is a
            // segment with exactly one endpoint inside the courtyard proper.
            bool[] radial = new bool[5];
            for (int i = 0; i < n; i++)
            {
                PathSegment s = Paths[i];
                bool aIn = IsInCourtyard(s.A.x, s.A.y);
                bool bIn = IsInCourtyard(s.B.x, s.B.y);

                if (aIn == bIn)
                {
                    continue;
                }

                Vector2 outer = aIn ? s.B : s.A;
                radial[(int)DD.Prototype.Navigation.RingRegionMap.Classify(
                    new Vector3(outer.x, 0f, outer.y), Vector3.zero, CenterRadius)] = true;
            }

            for (int r = 1; r <= 4; r++)
            {
                if (!radial[r])
                {
                    report.Append("  NO RADIAL from region ")
                        .Append((DD.Prototype.Navigation.RingRegion)r).Append('\n');
                }
            }

            bool allRadials = radial[1] && radial[2] && radial[3] && radial[4];
            ok = stranded == 0 && deadEnds == 0 && allRadials;

            report.Insert(0, "[World] Topology: " + n + " segments, " + stranded +
                             " disconnected, " + deadEnds + " dead ends, radials N/E/S/W = " +
                             radial[1] + "/" + radial[2] + "/" + radial[3] + "/" + radial[4] +
                             (ok ? " -> OK\n" : " -> FAILED\n"));

            return report.ToString();
        }

        private static int CheckEnd(System.Text.StringBuilder report, int self, Vector2 end, float slack)
        {
            if (IsInCourtyard(end.x, end.y))
            {
                return 0;
            }

            // The bridge landings are real connections made of geometry rather than paint.
            if (Mathf.Abs(end.x - BridgeX) < 4f &&
                (Mathf.Abs(end.y - BridgeNorthZ) < 4f || Mathf.Abs(end.y - BridgeSouthZ) < 4f))
            {
                return 0;
            }

            for (int j = 0; j < Paths.Length; j++)
            {
                if (j == self)
                {
                    continue;
                }

                if (DistanceToSegment(end.x, end.y, Paths[j].A, Paths[j].B)
                    <= Paths[j].HalfWidth + Paths[self].HalfWidth + slack)
                {
                    return 0;
                }
            }

            report.Append("  DEAD END at ").Append(end)
                .Append(" (segment ").Append(self).Append(")\n");
            return 1;
        }

        private static bool TouchesCourtyard(PathSegment segment)
        {
            return IsInCourtyard(segment.A.x, segment.A.y) ||
                   IsInCourtyard(segment.B.x, segment.B.y);
        }

        private static int NearestSegment(Vector2 point)
        {
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < Paths.Length; i++)
            {
                float d = DistanceToSegment(point.x, point.y, Paths[i].A, Paths[i].B) - Paths[i].HalfWidth;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return bestDistance <= 3f ? best : -1;
        }

        /// <summary>Shortest distance between two 2D segments. Zero when they cross.</summary>
        public static float SegmentDistance(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
        {
            if (SegmentsIntersect(p1, q1, p2, q2))
            {
                return 0f;
            }

            float d = DistanceToSegment(p1.x, p1.y, p2, q2);
            d = Mathf.Min(d, DistanceToSegment(q1.x, q1.y, p2, q2));
            d = Mathf.Min(d, DistanceToSegment(p2.x, p2.y, p1, q1));
            d = Mathf.Min(d, DistanceToSegment(q2.x, q2.y, p1, q1));
            return d;
        }

        private static bool SegmentsIntersect(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
        {
            float d1 = Cross(q2 - p2, p1 - p2);
            float d2 = Cross(q2 - p2, q1 - p2);
            float d3 = Cross(q1 - p1, p2 - p1);
            float d4 = Cross(q1 - p1, q2 - p1);

            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                   ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
